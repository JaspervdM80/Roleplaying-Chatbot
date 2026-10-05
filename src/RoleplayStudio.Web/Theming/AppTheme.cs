using System.Globalization;
using MudBlazor;

namespace RoleplayStudio.Web.Theming;

// The one palette: it emits both the CSS custom properties and the MudTheme, so the two cannot drift apart.
public static class AppTheme
{
    public const string SurfacePage = "#110e17";
    public const string SurfaceCard = "#1a1622";
    public const string SurfaceRaised = "#241f2e";
    public const string SurfaceSunken = "#0b0910";

    public const string Primary = "#f0739b";
    public const string PrimaryDeep = "#c9557b";
    public const string OnPrimary = "#1d0a13";
    public const string Accent = "#f2b56b";
    public const string OnAccent = "#211405";

    public const string Success = "#5fd39a";
    public const string Warning = "#f2c46b";
    public const string Error = "#ff6b7a";
    public const string Info = "#7cb7ff";

    // Every text and line shade is this colour at an alpha, so muted text never picks its own grey.
    public const string Ink = "#ece6f2";

    private static readonly string InkStrong = InkAt(0.92);
    private static readonly string InkSoft = InkAt(0.82);
    private static readonly string InkMuted = InkAt(0.7);
    private static readonly string InkSubtle = InkAt(0.52);
    private static readonly string InkFaint = InkAt(0.36);
    private static readonly string LineStrong = InkAt(0.22);
    private static readonly string Line = InkAt(0.12);
    private static readonly string Hover = InkAt(0.06);
    private static readonly string Stripe = InkAt(0.03);

    public const string Scrim = "rgba(5,3,8,0.7)";
    public const string Shadow = "rgba(0,0,0,0.45)";

    public const string CornerRadius = "10px";

    private const string UiFont = "'Figtree', 'Segoe UI', system-ui, sans-serif";
    private const string ProseFont = "'Atkinson Hyperlegible', 'Segoe UI', system-ui, sans-serif";
    private static readonly string[] UiFontStack = ["Figtree", "Segoe UI", "system-ui", "sans-serif"];

    public static string CssVariables { get; } =
        $$"""
        :root {
            color-scheme: dark;

            --surface-page: {{SurfacePage}};
            --surface-card: {{SurfaceCard}};
            --surface-raised: {{SurfaceRaised}};
            --surface-sunken: {{SurfaceSunken}};

            --primary: {{Primary}};
            --primary-deep: {{PrimaryDeep}};
            --on-primary: {{OnPrimary}};
            --accent: {{Accent}};
            --on-accent: {{OnAccent}};

            --success: {{Success}};
            --warning: {{Warning}};
            --error: {{Error}};
            --info: {{Info}};

            --ink: {{InkStrong}};
            --ink-soft: {{InkSoft}};
            --ink-muted: {{InkMuted}};
            --ink-subtle: {{InkSubtle}};
            --ink-faint: {{InkFaint}};
            --line: {{Line}};
            --line-strong: {{LineStrong}};
            --hover: {{Hover}};
            --selected: {{Line}};
            --scrim: {{Scrim}};
            --shadow: {{Shadow}};

            --corner-radius: {{CornerRadius}};
            --font-ui: {{UiFont}};
            --font-prose: {{ProseFont}};
            --text-ui: 0.9375rem;
            --text-prose: 1.0625rem;
            --leading-prose: 1.6;
        }
        """;

    public static MudTheme Mud { get; } = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = Primary,
            PrimaryDarken = PrimaryDeep,
            PrimaryContrastText = OnPrimary,
            Secondary = Accent,
            SecondaryContrastText = OnAccent,
            Tertiary = Info,
            Success = Success,
            Warning = Warning,
            Error = Error,
            Info = Info,
            Black = SurfaceSunken,
            Background = SurfacePage,
            BackgroundGray = SurfaceSunken,
            Surface = SurfaceCard,
            AppbarBackground = SurfacePage,
            AppbarText = InkStrong,
            DrawerBackground = SurfaceCard,
            DrawerText = InkSoft,
            DrawerIcon = InkMuted,
            TextPrimary = InkStrong,
            TextSecondary = InkMuted,
            TextDisabled = InkFaint,
            ActionDefault = InkMuted,
            ActionDisabled = InkFaint,
            ActionDisabledBackground = Hover,
            Divider = Line,
            DividerLight = Hover,
            LinesDefault = Line,
            LinesInputs = InkFaint,
            TableLines = Line,
            TableHover = Hover,
            TableStriped = Stripe,
            OverlayDark = Scrim
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = CornerRadius
        },
        // MudBlazor sets Roboto on every level, so the body font alone would leave headings and buttons in a second typeface.
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = UiFontStack },
            H1 = new H1Typography { FontFamily = UiFontStack },
            H2 = new H2Typography { FontFamily = UiFontStack },
            H3 = new H3Typography { FontFamily = UiFontStack },
            H4 = new H4Typography { FontFamily = UiFontStack, FontSize = "1.625rem", FontWeight = "700", LineHeight = "1.25", LetterSpacing = "-.01em" },
            H5 = new H5Typography { FontFamily = UiFontStack, FontSize = "1.375rem", FontWeight = "700", LineHeight = "1.3", LetterSpacing = "-.005em" },
            H6 = new H6Typography { FontFamily = UiFontStack, FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.35" },
            Subtitle1 = new Subtitle1Typography { FontFamily = UiFontStack },
            Subtitle2 = new Subtitle2Typography { FontFamily = UiFontStack },
            Body1 = new Body1Typography { FontFamily = UiFontStack, FontSize = "0.9375rem" },
            Body2 = new Body2Typography { FontFamily = UiFontStack },
            Button = new ButtonTypography { FontFamily = UiFontStack, FontWeight = "600", TextTransform = "none", LetterSpacing = "0" },
            Caption = new CaptionTypography { FontFamily = UiFontStack },
            Overline = new OverlineTypography { FontFamily = UiFontStack }
        }
    };

    private static string InkAt(double opacity)
    {
        var red = Convert.ToInt32(Ink[1..3], 16);
        var green = Convert.ToInt32(Ink[3..5], 16);
        var blue = Convert.ToInt32(Ink[5..7], 16);

        return string.Create(CultureInfo.InvariantCulture, $"rgba({red},{green},{blue},{opacity})");
    }
}
