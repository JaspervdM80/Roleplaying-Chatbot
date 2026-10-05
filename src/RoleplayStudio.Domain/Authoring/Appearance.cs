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
}

/// <summary>Clothing, which can change during a chat.</summary>
public class Outfit
{
    public string? Top { get; set; }
    public string? Bottom { get; set; }
    public string? Footwear { get; set; }
    public string? Accessories { get; set; }
    public string? Notes { get; set; }
}
