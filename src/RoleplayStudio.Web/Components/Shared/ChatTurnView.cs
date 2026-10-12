using System.Text;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.Web.Components.Shared;

public enum TurnVoice
{
    User,
    Character,
    Narration,
}

/// <summary>One turn as the transcript shows it; <see cref="MessageId"/> is null until the turn is saved.</summary>
public sealed class ChatTurnView(TurnVoice voice, string speaker, string text, Guid? messageId = null, Guid? speakerId = null)
{
    private readonly StringBuilder _text = new(text);

    public static ChatTurnView Of(Message message) => new(
        message.Role switch
        {
            MessageRole.User => TurnVoice.User,
            MessageRole.Character => TurnVoice.Character,
            _ => TurnVoice.Narration,
        },
        message.SpeakerName,
        message.Content,
        message.Id,
        message.SpeakerCharacterId);

    public Guid? MessageId { get; } = messageId;
    public TurnVoice Voice { get; } = voice;
    public bool FromUser => Voice == TurnVoice.User;
    public string Speaker { get; set; } = speaker;

    /// <summary>The character speaking; null for the user, narration, or a character not saved anywhere (the playground).</summary>
    public Guid? SpeakerId { get; } = speakerId;

    public string Text => _text.ToString();

    public void Append(string text) => _text.Append(text);

    public void Replace(string text) => _text.Clear().Append(text);
}
