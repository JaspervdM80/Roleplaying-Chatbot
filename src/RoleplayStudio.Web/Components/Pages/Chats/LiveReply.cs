using System.Text;
using RoleplayStudio.AI.Chat;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;
using RoleplayStudio.Web.Components.Shared;

namespace RoleplayStudio.Web.Components.Pages.Chats;

/// <summary>A reply as it streams, shown in <paramref name="turns"/> as one live turn per speaker until it is saved.</summary>
public sealed class LiveReply(IReadOnlyList<Character> present, IReadOnlyList<Character> met, List<ChatTurnView> turns)
{
    private readonly SpeakerTagParser _parser = new(present);
    private readonly StringBuilder _text = new();
    private readonly List<ChatTurnView> _live = [];

    public string Text => _text.ToString();

    public void Append(string text)
    {
        _text.Append(text);
        _parser.Append(text);
    }

    /// <summary>A segment only ever grows, so each keeps its place.</summary>
    public void Show()
    {
        var segments = _parser.Segments;
        for (var i = 0; i < segments.Count; i++)
        {
            if (i == _live.Count)
            {
                var speaker = met.FirstOrDefault(c => c.Id == segments[i].CharacterId);
                _live.Add(speaker is null
                    ? new ChatTurnView(TurnVoice.Narration, ChatSession.NarratorName, "")
                    : new ChatTurnView(TurnVoice.Character, speaker.Name, "", speakerId: speaker.Id));
                turns.Add(_live[i]);
            }

            _live[i].Replace(segments[i].Text);
        }
    }

    public void Remove() => turns.RemoveAll(_live.Contains);
}
