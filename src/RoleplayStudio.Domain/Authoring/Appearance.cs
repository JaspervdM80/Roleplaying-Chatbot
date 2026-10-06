namespace RoleplayStudio.Domain.Authoring;

/// <summary>Physical looks that stay the same between scenes.</summary>
public class Appearance
{
    public string? BodyType { get; set; }
    public string? Height { get; set; }
    public string? SkinTone { get; set; }
    public string? Face { get; set; }
    public string? Hair { get; set; }
    public string? Eyes { get; set; }
    public string? DistinguishingFeatures { get; set; }
    public string? Notes { get; set; }

    public Appearance Copy() => new()
    {
        BodyType = TextFields.Clean(BodyType),
        Height = TextFields.Clean(Height),
        SkinTone = TextFields.Clean(SkinTone),
        Face = TextFields.Clean(Face),
        Hair = TextFields.Clean(Hair),
        Eyes = TextFields.Clean(Eyes),
        DistinguishingFeatures = TextFields.Clean(DistinguishingFeatures),
        Notes = TextFields.Clean(Notes),
    };

    public string? Describe() => TextFields.Describe(
        ("Body", BodyType),
        ("Height", Height),
        ("Skin", SkinTone),
        ("Face", Face),
        ("Hair", Hair),
        ("Eyes", Eyes),
        ("Distinguishing features", DistinguishingFeatures),
        ("Notes", Notes));
}

/// <summary>Clothing, which can change during a chat.</summary>
public class Outfit
{
    public string? Top { get; set; }
    public string? Bottom { get; set; }
    public string? Footwear { get; set; }
    public string? Accessories { get; set; }
    public string? Notes { get; set; }

    public Outfit Copy() => new()
    {
        Top = TextFields.Clean(Top),
        Bottom = TextFields.Clean(Bottom),
        Footwear = TextFields.Clean(Footwear),
        Accessories = TextFields.Clean(Accessories),
        Notes = TextFields.Clean(Notes),
    };

    public string? Describe() => TextFields.Describe(
        ("Top", Top),
        ("Bottom", Bottom),
        ("Footwear", Footwear),
        ("Accessories", Accessories),
        ("Notes", Notes));

    /// <summary>The pieces worn, comma-separated for a one-line glance; null when nothing is known.</summary>
    public string? Summarize()
    {
        var pieces = new[] { Top, Bottom, Footwear, Accessories }.Select(TextFields.Clean).OfType<string>().ToList();
        return pieces.Count == 0 ? TextFields.Clean(Notes) : string.Join(", ", pieces);
    }
}
