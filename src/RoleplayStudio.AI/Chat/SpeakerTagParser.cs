using System.Text;
using System.Text.RegularExpressions;
using RoleplayStudio.Domain.Authoring;
using RoleplayStudio.Domain.Chats;

namespace RoleplayStudio.AI.Chat;

/// <summary>
/// Splits a reply into speakers as it streams, by <c>**Name:**</c> tags at the start of a line. A tag naming
/// no one present stays in the text, as narration.
/// </summary>
public sealed partial class SpeakerTagParser
{
    private readonly IReadOnlyList<Character> _present;
    private readonly Guid? _untagged;
    private readonly List<(Guid? CharacterId, StringBuilder Text)> _segments = [];
    private string _held = "";
    private bool _atLineStart = true;

    public SpeakerTagParser(IReadOnlyList<Character> present)
    {
        _present = present;
        _untagged = ChatSession.ReplySpeaker(present).CharacterId;
    }

    public static IReadOnlyList<ReplySegment> Parse(string text, IReadOnlyList<Character> present)
    {
        var parser = new SpeakerTagParser(present);
        parser.Append(text);
        return parser.Complete();
    }

    /// <summary>The segments so far, the last one still growing while the reply streams.</summary>
    public IReadOnlyList<ReplySegment> Segments =>
        _segments
            .Select(s => new ReplySegment(s.CharacterId, s.Text.ToString().Trim()))
            .Where(s => s.Text.Length > 0)
            .ToList();

    public void Append(string chunk)
    {
        _held += chunk;
        Drain(final: false);
    }

    public IReadOnlyList<ReplySegment> Complete()
    {
        Drain(final: true);
        return Segments;
    }

    private void Drain(bool final)
    {
        while (_held.Length > 0)
        {
            if (_atLineStart)
            {
                var tag = Tag().Match(_held);
                if (tag.Success)
                {
                    Speak(tag);
                    _held = _held[tag.Length..];
                    _atLineStart = false;
                    continue;
                }

                // The start of a line is held back until it cannot be a tag split across chunks.
                if (!final && PartialTag().IsMatch(_held))
                {
                    return;
                }

                if (_untagged is null && NarrationLine().IsMatch(_held))
                {
                    StartSegment(null);
                }
            }

            var end = _held.IndexOf('\n');
            var take = end < 0 ? _held.Length : end + 1;
            Write(_held[..take]);
            _held = _held[take..];
            _atLineStart = end >= 0;
        }
    }

    private void Speak(Match tag)
    {
        var name = tag.Groups["name"].Value.Trim();
        if (string.Equals(name, ChatSession.NarratorName, StringComparison.OrdinalIgnoreCase))
        {
            StartSegment(null);
        }
        else if (Find(name) is { } character)
        {
            StartSegment(character.Id);
        }
        else
        {
            StartSegment(null);
            Write(tag.Value);
        }
    }

    private Character? Find(string name)
    {
        if (_present.FirstOrDefault(c => string.Equals(c.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        var byFirstName = _present.Where(c => string.Equals(c.Name.Trim().Split(' ')[0], name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return byFirstName.Count == 1 ? byFirstName[0] : null;
    }

    private void StartSegment(Guid? characterId)
    {
        if (_segments.Count == 0 || _segments[^1].CharacterId != characterId)
        {
            _segments.Add((characterId, new StringBuilder()));
        }
    }

    private void Write(string text)
    {
        if (_segments.Count == 0)
        {
            StartSegment(_untagged);
        }

        _segments[^1].Text.Append(text);
    }

    [GeneratedRegex(@"^[ \t]*\*\*(?<name>[^*\n]{1,60}?)(?::\*\*|\*\*:)[ \t]*")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^[ \t]*(\*(\*([^*\n]{0,60}(:\*?|\*\*?)?)?)?)?\z")]
    private static partial Regex PartialTag();

    [GeneratedRegex(@"^[ \t]*\*(?!\*)")]
    private static partial Regex NarrationLine();
}
