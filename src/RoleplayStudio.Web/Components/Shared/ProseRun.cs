using System.Text.RegularExpressions;

namespace RoleplayStudio.Web.Components.Shared;

public enum ProseStyle
{
    Plain,
    Emphasis,
    Strong,
}

/// <summary>A stretch of a message in one style, read from the <c>*italics*</c> and <c>**bold**</c> a model writes.</summary>
public sealed partial record ProseRun(string Text, ProseStyle Style)
{
    public static IReadOnlyList<ProseRun> Split(string text)
    {
        var runs = new List<ProseRun>();
        var at = 0;
        foreach (Match match in Marked().Matches(text))
        {
            if (match.Index > at)
            {
                runs.Add(new ProseRun(text[at..match.Index], ProseStyle.Plain));
            }

            runs.Add(match.Groups["strong"].Success
                ? new ProseRun(match.Groups["strong"].Value, ProseStyle.Strong)
                : new ProseRun(match.Groups["em"].Value, ProseStyle.Emphasis));
            at = match.Index + match.Length;
        }

        if (at < text.Length)
        {
            runs.Add(new ProseRun(text[at..], ProseStyle.Plain));
        }

        return runs;
    }

    // Within one line, and not opening on a space, so "5 * 3" or a stray asterisk stays as written.
    [GeneratedRegex(@"\*\*(?<strong>[^*\s][^*\n]*?)\*\*|\*(?<em>[^*\s][^*\n]*?)\*")]
    private static partial Regex Marked();
}
