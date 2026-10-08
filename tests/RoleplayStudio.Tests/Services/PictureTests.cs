using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RoleplayStudio.AI.Images;
using RoleplayStudio.AI.Providers;
using RoleplayStudio.AI.Upkeep;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Domain.Media;
using RoleplayStudio.Infrastructure.Data;
using RoleplayStudio.Infrastructure.Services;
using RoleplayStudio.Infrastructure.Storage;
using RoleplayStudio.Tests.Support;

namespace RoleplayStudio.Tests.Services;

[Collection(PostgresCollection.Name)]
public sealed class PictureTests(PostgresFixture postgres) : IDisposable
{
    private const string Written = """{"prompt":"a woman on the loft stairs, lantern in hand","caption":"Mira on the stairs"}""";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "roleplay-pictures-" + Guid.NewGuid().ToString("N"));
    private readonly PictureQueue _queue = new(NullLogger<PictureQueue>.Instance);

    private FileSystemImageStore Store => new(_folder);

    private PictureService Pictures(StudioUser user) => new(postgres.DbFactory, _queue, user.User, NullLogger<PictureService>.Instance);

    private ImageService Images(StudioUser user) => new(postgres.DbFactory, Store, user.User, NullLogger<ImageService>.Instance);

    private PictureWorker Worker(IChatClientFactory clients, PictureNotifier? notifier = null)
    {
        notifier ??= new PictureNotifier();
        var drawing = new PictureDrawing(postgres.DbFactory, clients, Store, _queue, notifier, TimeProvider.System, NullLogger<PictureDrawing>.Instance);
        return new PictureWorker(_queue, drawing, notifier, NullLogger<PictureWorker>.Instance);
    }

    /// <summary>Takes the job the page queued off the queue and draws it, as the background worker would.</summary>
    private async Task<Result<Guid>> DrawQueuedAsync(IChatClientFactory clients, PictureNotifier? notifier = null)
    {
        Assert.True(_queue.Reader.TryRead(out var job));
        var drawn = await Worker(clients, notifier).RunAsync(job, default);
        _queue.Finish(job.Id);
        return drawn;
    }

    private async Task<GeneratedImage> ImageAsync(StudioUser user, Guid id)
    {
        await using var db = await postgres.DbFactory.CreateForOwnerAsync(user.User.UserId!);
        return await db.Images.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    private async Task<(StudioUser User, ChatSession Session, Message Opening, Guid MiraId)> ReadyChatAsync(bool acceptsReferenceImage = false)
    {
        var user = new StudioUser(postgres);
        var session = await user.ChatAsync("Mira");
        await user.UtilityModelAsync();
        await user.ImageModelAsync(acceptsReferenceImage);
        var loaded = (await user.Sessions.GetAsync(session.Id)).Value;
        return (user, loaded, loaded.Messages[0], loaded.CharacterStates[0].CharacterId);
    }

    [Fact]
    public async Task A_picture_of_a_turn_sees_the_story_up_to_that_turn_and_not_after()
    {
        var (user, session, opening, _) = await ReadyChatAsync();
        Assert.True((await user.Sessions.AddUserMessageAsync(session.Id, "The lights go out across the whole inn.")).IsSuccess);
        var writer = new FakeChatClient([Written]);

        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null)).IsSuccess);
        Assert.True((await DrawQueuedAsync(new FakeChatClientFactory(writer, images: new FakeImageGenerator()))).IsSuccess);

        var asked = writer.LastMessages![1].Text;
        Assert.Contains(opening.Content, asked);
        Assert.DoesNotContain("The lights go out", asked);
    }

    [Fact]
    public async Task A_characters_first_portrait_becomes_their_reference_and_gives_them_its_seed()
    {
        var (user, _, _, miraId) = await ReadyChatAsync();

        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        var first = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        var second = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));

        var mira = (await user.Characters.ListAsync()).Value.Single(c => c.Id == miraId);
        Assert.Equal(first.Value, mira.ReferenceImageId);
        Assert.Equal((await ImageAsync(user, first.Value)).Seed, mira.ImageSeed);
        Assert.True((await ImageAsync(user, second.Value)).IsPortrait);
    }

    [Fact]
    public async Task A_picture_of_a_character_is_drawn_after_their_reference_with_their_seed_when_the_model_accepts_one()
    {
        var (user, session, opening, miraId) = await ReadyChatAsync(acceptsReferenceImage: true);
        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        var portrait = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        var drawer = new FakeImageGenerator();

        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, miraId)).IsSuccess);
        Assert.True((await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: drawer))).IsSuccess);

        Assert.Equal(FakeImageGenerator.Png, drawer.LastRequest!.Reference!.Data);
        Assert.Equal((await ImageAsync(user, portrait.Value)).Seed, drawer.LastRequest.Seed);
    }

    [Fact]
    public async Task A_model_that_takes_no_reference_is_sent_none()
    {
        var (user, session, opening, miraId) = await ReadyChatAsync();
        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        var drawer = new FakeImageGenerator();

        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, miraId)).IsSuccess);
        await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: drawer));

        Assert.Null(drawer.LastRequest!.Reference);
    }

    [Fact]
    public async Task Drawing_again_keeps_the_prompt_and_changes_the_seed()
    {
        var (user, session, opening, _) = await ReadyChatAsync();
        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null)).IsSuccess);
        var first = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        var original = await ImageAsync(user, first.Value);

        Assert.True((await Pictures(user).RedrawAsync(original.Id)).IsSuccess);
        var again = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient(["not asked"]), images: new FakeImageGenerator()));

        var redrawn = await ImageAsync(user, again.Value);
        Assert.Equal((original.Prompt, original.SessionId, original.MessageId, original.Caption), (redrawn.Prompt, redrawn.SessionId, redrawn.MessageId, redrawn.Caption));
        Assert.NotEqual(original.Seed, redrawn.Seed);
    }

    [Fact]
    public async Task Another_users_chat_message_or_character_cannot_be_pictured()
    {
        var (owner, session, opening, miraId) = await ReadyChatAsync();
        var other = new StudioUser(postgres);
        await other.UtilityModelAsync();
        await other.ImageModelAsync();

        Assert.True((await Pictures(other).PictureMessageAsync(session.Id, opening.Id, null)).IsFailure);
        Assert.True((await Pictures(other).DrawPortraitAsync(miraId)).IsFailure);
        Assert.False(_queue.Reader.TryRead(out _));
        Assert.True((await Pictures(owner).PictureMessageAsync(session.Id, Guid.NewGuid(), null)).IsFailure);
    }

    [Fact]
    public async Task Picture_this_is_refused_without_a_utility_model_to_write_the_prompt()
    {
        var user = new StudioUser(postgres);
        var session = (await user.Sessions.GetAsync((await user.ChatAsync()).Id)).Value;
        await user.ImageModelAsync();

        var queued = await Pictures(user).PictureMessageAsync(session.Id, session.Messages[0].Id, null);

        Assert.True(queued.IsFailure);
        Assert.Contains("utility model", queued.Error);
    }

    [Fact]
    public async Task A_cancelled_picture_is_never_drawn_and_only_its_owner_can_cancel_it()
    {
        var (user, session, opening, _) = await ReadyChatAsync();
        var queued = await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null);
        var pending = Assert.Single((await Pictures(user).PendingAsync()).Value);
        Assert.Equal((queued.Value, session.Id, opening.Id), (pending.Id, pending.SessionId!.Value, pending.MessageId!.Value));
        Assert.Empty((await Pictures(new StudioUser(postgres)).PendingAsync()).Value);
        Assert.False(_queue.Cancel(Guid.NewGuid().ToString(), queued.Value));
        var drawer = new FakeImageGenerator();

        await Pictures(user).CancelAsync(queued.Value);
        Assert.Empty((await Pictures(user).PendingAsync()).Value);
        var drawn = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: drawer));

        Assert.True(drawn.IsCancelled);
        Assert.Null(drawer.LastRequest);
        Assert.Empty((await Pictures(user).PendingAsync()).Value);
    }

    [Fact]
    public async Task The_caption_is_announced_while_drawing_and_the_picture_when_done()
    {
        var (user, session, opening, _) = await ReadyChatAsync();
        var notifier = new PictureNotifier();
        var changes = new List<PictureChange>();
        notifier.Changed += changes.Add;
        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null)).IsSuccess);
        Assert.True(_queue.Reader.TryRead(out var job));
        var drawing = new PictureDrawing(postgres.DbFactory, new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()), Store, _queue, notifier, TimeProvider.System, NullLogger<PictureDrawing>.Instance);

        var drawn = await drawing.DrawAsync(job, default);

        Assert.Equal("Mira on the stairs", Assert.Single(_queue.PendingFor(user.User.UserId!)).Caption);
        Assert.Equal(new PictureChange(job.Id, session.Id, null, null), Assert.Single(changes));
        Assert.True(drawn.IsSuccess, drawn.Error);
    }

    [Fact]
    public async Task Galleries_list_only_the_callers_pictures_of_that_chat_or_character()
    {
        var (user, session, opening, miraId) = await ReadyChatAsync();
        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null)).IsSuccess);
        var scene = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        var portrait = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        var other = new StudioUser(postgres);

        Assert.Equal([scene.Value], (await Images(user).ListForChatAsync(session.Id)).Value.Select(i => i.Id));
        Assert.Equal([portrait.Value], (await Images(user).ListForCharacterAsync(miraId)).Value.Select(i => i.Id));
        Assert.Empty((await Images(other).ListForChatAsync(session.Id)).Value);
        Assert.Empty((await Images(other).ListForCharacterAsync(miraId)).Value);
        Assert.True((await Images(other).GetAsync(scene.Value)).IsFailure);
        Assert.True((await Images(other).DeleteAsync(scene.Value)).IsFailure);
        Assert.True((await Images(other).SetReferenceAsync(portrait.Value)).IsFailure);
    }

    [Fact]
    public async Task Deleting_the_reference_portrait_removes_its_file_and_leaves_the_character_without_one()
    {
        var (user, _, _, miraId) = await ReadyChatAsync();
        Assert.True((await Pictures(user).DrawPortraitAsync(miraId)).IsSuccess);
        var portrait = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));

        Assert.True((await Images(user).DeleteAsync(portrait.Value)).IsSuccess);

        Assert.Null((await user.Characters.ListAsync()).Value.Single(c => c.Id == miraId).ReferenceImageId);
        Assert.True((await Images(user).OpenAsync(portrait.Value)).IsFailure);
        Assert.Empty(Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_picture_of_a_character_from_a_chat_can_be_made_their_reference()
    {
        var (user, session, opening, miraId) = await ReadyChatAsync();
        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, miraId)).IsSuccess);
        var picture = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));
        Assert.True((await Pictures(user).PictureMessageAsync(session.Id, opening.Id, null)).IsSuccess);
        var scene = await DrawQueuedAsync(new FakeChatClientFactory(new FakeChatClient([Written]), images: new FakeImageGenerator()));

        Assert.True((await Images(user).SetReferenceAsync(picture.Value)).IsSuccess);
        Assert.True((await Images(user).SetReferenceAsync(scene.Value)).IsFailure);

        var mira = (await user.Characters.ListAsync()).Value.Single(c => c.Id == miraId);
        Assert.Equal(picture.Value, mira.ReferenceImageId);
        Assert.Equal((await ImageAsync(user, picture.Value)).Seed, mira.ImageSeed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
