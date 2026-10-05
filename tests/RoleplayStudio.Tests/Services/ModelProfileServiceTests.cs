using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public class ModelProfileServiceTests(PostgresFixture postgres)
{
    private ModelProfileService ServiceFor(ICurrentUser user) => new(postgres.DbFactory, user, NullLogger<ModelProfileService>.Instance);

    private static ModelProfile Profile(string name, ModelRole role = ModelRole.Chat, bool isDefault = false) => new()
    {
        Name = name,
        Role = role,
        Provider = ProviderKind.Ollama,
        ModelId = "mistral-nemo",
        IsDefault = isDefault,
    };

    [Fact]
    public async Task A_user_only_lists_their_own_model_profiles()
    {
        var alice = ServiceFor(FakeCurrentUser.NewUser());
        var bob = ServiceFor(FakeCurrentUser.NewUser());
        await alice.CreateAsync(Profile("Alice's model"));
        await bob.CreateAsync(Profile("Bob's model"));

        var listed = await alice.ListAsync();

        Assert.True(listed.IsSuccess);
        Assert.Equal(["Alice's model"], listed.Value.Select(p => p.Name));
    }

    [Fact]
    public async Task Another_users_profile_reads_as_not_found_for_every_operation()
    {
        var owner = ServiceFor(FakeCurrentUser.NewUser());
        var intruder = ServiceFor(FakeCurrentUser.NewUser());
        var profile = (await owner.CreateAsync(Profile("Private"))).Value;

        Assert.True((await intruder.GetAsync(profile.Id)).IsFailure);
        Assert.True((await intruder.MakeDefaultAsync(profile.Id)).IsFailure);
        Assert.True((await intruder.DeleteAsync(profile.Id)).IsFailure);

        var unchanged = await owner.GetAsync(profile.Id);
        Assert.Equal("Private", unchanged.Value.Name);
        Assert.False(unchanged.Value.IsDefault);
    }

    [Fact]
    public async Task Updating_another_users_profile_by_its_id_changes_nothing()
    {
        var owner = ServiceFor(FakeCurrentUser.NewUser());
        var intruder = ServiceFor(FakeCurrentUser.NewUser());
        var profile = (await owner.CreateAsync(Profile("Private"))).Value;

        var forged = Profile("Hijacked");
        forged.Id = profile.Id;
        var result = await intruder.UpdateAsync(forged);

        Assert.True(result.IsFailure);
        Assert.Equal("Private", (await owner.GetAsync(profile.Id)).Value.Name);
    }

    [Fact]
    public async Task The_owner_comes_from_the_signed_in_user_never_from_the_input()
    {
        var user = FakeCurrentUser.NewUser();
        var input = Profile("Mine");
        input.OwnerId = "someone-else";

        var created = await ServiceFor(user).CreateAsync(input);

        Assert.Equal(user.UserId, created.Value.OwnerId);
    }

    [Fact]
    public async Task Making_a_profile_the_default_clears_the_previous_default_of_that_role_only()
    {
        var service = ServiceFor(FakeCurrentUser.NewUser());
        var first = (await service.CreateAsync(Profile("First", isDefault: true))).Value;
        var utility = (await service.CreateAsync(Profile("Utility", ModelRole.Utility, isDefault: true))).Value;
        var second = (await service.CreateAsync(Profile("Second"))).Value;

        var result = await service.MakeDefaultAsync(second.Id);

        Assert.True(result.IsSuccess);
        var defaults = (await service.ListAsync()).Value.Where(p => p.IsDefault).Select(p => p.Id);
        Assert.Equal([utility.Id, second.Id], defaults.Order());
        Assert.DoesNotContain(first.Id, defaults);
    }

    [Fact]
    public async Task Creating_a_second_default_takes_over_from_the_first()
    {
        var service = ServiceFor(FakeCurrentUser.NewUser());
        await service.CreateAsync(Profile("First", isDefault: true));

        var second = await service.CreateAsync(Profile("Second", isDefault: true));

        Assert.True(second.IsSuccess);
        Assert.Equal(["Second"], (await service.ListAsync()).Value.Where(p => p.IsDefault).Select(p => p.Name));
    }

    [Fact]
    public async Task A_signed_out_caller_is_refused()
    {
        var service = ServiceFor(new FakeCurrentUser(null));

        Assert.True((await service.ListAsync()).IsFailure);
        Assert.True((await service.CreateAsync(Profile("Nobody's"))).IsFailure);
    }

    [Fact]
    public async Task An_api_key_setting_outside_the_providers_section_is_refused()
    {
        var input = Profile("Leaky");
        input.Provider = ProviderKind.OpenAICompatible;
        input.BaseUrl = "https://example.com/v1";
        input.ApiKeySetting = "ConnectionStrings:roleplaydb";

        var result = await ServiceFor(FakeCurrentUser.NewUser()).CreateAsync(input);

        Assert.True(result.IsFailure);
        Assert.False(result.IsCancelled);
    }

    [Fact]
    public async Task A_cancelled_read_reports_cancellation_rather_than_an_error()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await ServiceFor(FakeCurrentUser.NewUser()).ListAsync(cancelled.Token);

        Assert.True(result.IsCancelled);
    }

    [Fact]
    public async Task A_context_scoped_to_nobody_sees_no_owned_rows()
    {
        await ServiceFor(FakeCurrentUser.NewUser()).CreateAsync(Profile("Somebody's"));

        await using var db = await postgres.DbFactory.CreateDbContextAsync();

        Assert.Empty(await db.ModelProfiles.ToListAsync());
    }

    [Fact]
    public async Task A_context_scoped_to_nobody_cannot_write_owned_rows()
    {
        await using var db = await postgres.DbFactory.CreateDbContextAsync();
        db.ModelProfiles.Add(Profile("Orphan"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_context_cannot_save_a_row_it_attached_for_another_owner()
    {
        var owner = FakeCurrentUser.NewUser();
        var profile = (await ServiceFor(owner).CreateAsync(Profile("Private"))).Value;

        await using var db = await postgres.DbFactory.CreateForOwnerAsync(Guid.NewGuid().ToString());
        db.ModelProfiles.Attach(profile).Property(p => p.Name).IsModified = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_context_cannot_delete_a_row_it_attached_for_another_owner()
    {
        var owner = FakeCurrentUser.NewUser();
        var profile = (await ServiceFor(owner).CreateAsync(Profile("Private"))).Value;

        await using (var db = await postgres.DbFactory.CreateForOwnerAsync(Guid.NewGuid().ToString()))
        {
            db.ModelProfiles.Remove(profile);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }

        Assert.True((await ServiceFor(owner).GetAsync(profile.Id)).IsSuccess);
    }
}
