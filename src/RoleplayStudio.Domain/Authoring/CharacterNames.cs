namespace RoleplayStudio.Domain.Authoring;

/// <summary>How a name a model wrote is matched to a character: the full name, or the first name alone ("Thorne" for "Thorne Ashby").</summary>
public static class CharacterNames
{
    public static bool Names(string fullName, string name)
    {
        var wanted = name.Trim();
        return string.Equals(fullName.Trim(), wanted, StringComparison.OrdinalIgnoreCase)
            || string.Equals(FirstName(fullName), wanted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The character with this full name, else the one character with this first name; null when none, or several, have it.</summary>
    public static Character? Find(IEnumerable<Character> characters, string name)
    {
        var wanted = name.Trim();
        var candidates = characters.ToList();
        if (candidates.FirstOrDefault(c => string.Equals(c.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase)) is { } exact)
        {
            return exact;
        }

        var byFirstName = candidates.Where(c => string.Equals(FirstName(c.Name), wanted, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return byFirstName.Count == 1 ? byFirstName[0] : null;
    }

    private static string FirstName(string fullName) => fullName.Trim().Split(' ')[0];
}
