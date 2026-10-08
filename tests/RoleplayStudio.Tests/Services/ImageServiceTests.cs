using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Domain.Models;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Infrastructure.Storage;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public sealed class ImageServiceTests(PostgresFixture postgres) : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "roleplay-images-" + Guid.NewGuid().ToString("N"));

    private ImageService ServiceFor(ICurrentUser user) => new(postgres.DbFactory, new FileSystemImageStore(_folder), user, NullLogger<ImageService>.Instance);

    private static GeneratedImage Picture(Guid? sessionId = null, Guid? characterId = null, Guid? messageId = null) => new()
    {
        SessionId = sessionId,
        CharacterId = characterId,
        MessageId = messageId,
        Prompt = "A lighthouse",
        Provider = "Fake",
        Seed = 7,
        ContentType = "image/png",
        Width = 1,
        Height = 1,
    };

    private static async Task<byte[]> ReadAllAsync(Stream content)
    {
        await using (content)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy);
            return copy.ToArray();
        }
    }

    [Fact]
    public async Task A_saved_image_opens_for_its_owner()
    {
        var images = ServiceFor(FakeCurrentUser.NewUser());

        var saved = await images.SaveAsync(Picture(), FakeImageGenerator.Png);
        var opened = await images.OpenAsync(saved.Value.Id);

        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal("image/png", opened.Value.ContentType);
        Assert.Equal(FakeImageGenerator.Png, await ReadAllAsync(opened.Value.Content));
    }

    [Fact]
    public async Task Another_users_image_reads_as_not_found()
    {
        var saved = await ServiceFor(FakeCurrentUser.NewUser()).SaveAsync(Picture(), FakeImageGenerator.Png);

        var opened = await ServiceFor(FakeCurrentUser.NewUser()).OpenAsync(saved.Value.Id);

        Assert.True(opened.IsFailure);
    }

    [Fact]
    public async Task An_image_cannot_be_filed_under_another_users_chat()
    {
        var chat = await new StudioUser(postgres).ChatAsync();

        var saved = await ServiceFor(FakeCurrentUser.NewUser()).SaveAsync(Picture(chat.Id), FakeImageGenerator.Png);

        Assert.True(saved.IsFailure);
    }

    [Fact]
    public async Task An_image_cannot_name_another_users_character()
    {
        var character = await new StudioUser(postgres).CharacterAsync("Mira");

        var saved = await ServiceFor(FakeCurrentUser.NewUser()).SaveAsync(Picture(characterId: character.Id), FakeImageGenerator.Png);

        Assert.True(saved.IsFailure);
    }

    [Fact]
    public async Task An_image_cannot_name_a_message_from_another_chat()
    {
        var user = new StudioUser(postgres);
        var first = await user.ChatAsync("Mira");
        var second = await user.ChatAsync("Jun");
        var opening = (await user.Sessions.GetAsync(first.Id)).Value.Messages.First();

        var saved = await ServiceFor(user.User).SaveAsync(Picture(second.Id, messageId: opening.Id), FakeImageGenerator.Png);

        Assert.True(saved.IsFailure);
    }

    [Fact]
    public async Task Testing_an_image_model_stores_the_picture_it_drew()
    {
        var user = new StudioUser(postgres);
        var profile = (await user.Profiles.CreateAsync(new ModelProfile { Name = "Runware", Role = ModelRole.Image, Provider = ProviderKind.Runware, ModelId = "runware:101@1" })).Value;
        var generator = new FakeImageGenerator();
        var connections = new ModelConnectionService(
            user.Profiles, new FakeChatClientFactory(new FakeChatClient([]), images: generator), ServiceFor(user.User), NullLogger<ModelConnectionService>.Instance);

        var check = await connections.TestAsync(profile.Id);

        Assert.True(check.IsSuccess, check.Error);
        var opened = await ServiceFor(user.User).OpenAsync(check.Value.ImageId!.Value);
        Assert.True(opened.IsSuccess, opened.Error);
        Assert.Equal(FakeImageGenerator.Png, await ReadAllAsync(opened.Value.Content));

        // The seed is ours and stored, so the picture can be drawn again.
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        var row = await db.Images.SingleAsync(i => i.Id == check.Value.ImageId);
        Assert.Equal(generator.LastRequest!.Seed, row.Seed);
        Assert.Equal(generator.LastRequest.Prompt, row.Prompt);
        Assert.Equal("fake-diffusion", row.Model);
        Assert.Equal(nameof(ProviderKind.Runware), row.Provider);
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("/etc/passwd")]
    public void The_store_never_reaches_outside_its_folder(string path)
    {
        var store = new FileSystemImageStore(_folder);

        Assert.Throws<ArgumentException>(() => store.OpenRead(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
