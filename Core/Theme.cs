using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NicPad.Core;

public sealed record PreviewColors(
    Color Background,
    Color Text,
    Color Heading,
    Color Link,
    Color Code,
    Color Border,
    Color Muted);

/// <summary>Every color the UI uses, in one place.</summary>
public sealed record ThemePalette(
    string Name,
    bool IsDark,
    Color Window,
    Color Chrome,
    Color ChromeText,
    Color Editor,
    Color EditorText,
    Color Separator,
    Color TabBack,
    Color TabSelected,
    Color TabBorder,
    Color TabText,
    Color TabClose,
    Color HoverBack,
    Color HoverText,
    PreviewColors Preview)
{
    public bool IsBuiltIn => Themes.BuiltIn.Any(t => t.Name == Name);
}

public static partial class Themes
{
    public const int MaxThemeFileBytes = 64 * 1024;
    public const int MaxCustomThemes = 50;

    private static readonly PreviewColors LightPreview = new(
        Hex("#ffffff"), Hex("#333333"), Hex("#222222"), Hex("#0066cc"), Hex("#f4f4f4"), Hex("#dddddd"), Hex("#666666"));

    private static readonly PreviewColors DarkPreview = new(
        Hex("#1e1e1e"), Hex("#d4d4d4"), Hex("#ffffff"), Hex("#4ea1ff"), Hex("#2d2d2d"), Hex("#444444"), Hex("#a0a0a0"));

    private static readonly PreviewColors MatrixPreview = new(
        Hex("#000000"), Hex("#00ff00"), Hex("#33ff33"), Hex("#66ff66"), Hex("#0a1a0a"), Hex("#006400"), Hex("#00b000"));

    public static ThemePalette System => new(
        "System", false,
        SystemColors.Control, SystemColors.Control, SystemColors.ControlText,
        SystemColors.Window, SystemColors.WindowText, SystemColors.ControlDark,
        SystemColors.Control, SystemColors.Window, SystemColors.ControlDark, SystemColors.ControlText, SystemColors.ControlDark,
        SystemColors.Highlight, SystemColors.HighlightText,
        LightPreview);

    public static ThemePalette Light { get; } = new(
        "Light", false,
        Color.White, Color.FromArgb(248, 248, 248), Color.Black,
        Color.White, Color.Black, Color.FromArgb(220, 220, 220),
        Color.FromArgb(245, 245, 245), Color.White, Color.FromArgb(200, 200, 200), Color.FromArgb(50, 50, 50), Color.FromArgb(120, 120, 120),
        SystemColors.Highlight, SystemColors.HighlightText,
        LightPreview);

    public static ThemePalette Dark { get; } = new(
        "Dark", true,
        Color.FromArgb(30, 30, 30), Color.FromArgb(45, 45, 45), Color.White,
        Color.FromArgb(30, 30, 30), Color.White, Color.FromArgb(60, 60, 60),
        Color.FromArgb(45, 45, 45), Color.FromArgb(30, 30, 30), Color.FromArgb(60, 60, 60), Color.White, Color.FromArgb(180, 180, 180),
        Color.White, Color.Black,
        DarkPreview);

    public static ThemePalette Matrix { get; } = new(
        "Matrix", true,
        Color.Black, Color.FromArgb(10, 10, 10), Color.FromArgb(0, 255, 0),
        Color.Black, Color.FromArgb(0, 255, 0), Color.FromArgb(0, 100, 0),
        Color.FromArgb(10, 10, 10), Color.Black, Color.FromArgb(0, 100, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 200, 0),
        Color.FromArgb(0, 255, 0), Color.Black,
        MatrixPreview);

    public static IReadOnlyList<ThemePalette> BuiltIn => [System, Light, Dark, Matrix];

    public static ThemePalette? FindBuiltIn(string? name) =>
        BuiltIn.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads validated custom themes. Invalid files are skipped and reported in <paramref name="errors"/>.</summary>
    public static List<ThemePalette> LoadCustom(string folder, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var result = new List<ThemePalette>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.OrdinalIgnoreCase).Take(MaxCustomThemes))
        {
            try
            {
                if (new FileInfo(file).Length > MaxThemeFileBytes)
                {
                    errors.Add($"{Path.GetFileName(file)}: larger than 64 KB");
                    continue;
                }

                var theme = Parse(File.ReadAllText(file));
                if (BuiltIn.Any(b => string.Equals(b.Name, theme.Name, StringComparison.OrdinalIgnoreCase))
                    || result.Any(r => string.Equals(r.Name, theme.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add($"{Path.GetFileName(file)}: a theme named \"{theme.Name}\" already exists");
                    continue;
                }

                result.Add(theme);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a theme file: { "name": "My Theme", "base": "Dark", "colors": { "editor": "#101010", ... } }.
    /// Unknown color keys are rejected so typos don't fail silently.
    /// </summary>
    public static ThemePalette Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4, AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = doc.RootElement;

        var name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
        if (!ThemeNameRegex().IsMatch(name))
        {
            throw new FormatException("\"name\" must be 1-40 letters, digits, spaces, '-' or '_'");
        }

        var baseName = root.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : "Light";
        var palette = FindBuiltIn(baseName) is { } found && found.Name != "System" ? found : Light;
        palette = palette with { Name = name };

        if (!root.TryGetProperty("colors", out var colors) || colors.ValueKind != JsonValueKind.Object)
        {
            return palette;
        }

        var preview = palette.Preview;
        foreach (var prop in colors.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String)
            {
                throw new FormatException($"\"{prop.Name}\" must be a \"#RRGGBB\" string");
            }

            var color = Hex(prop.Value.GetString()!);
            switch (prop.Name.ToLowerInvariant())
            {
                case "window": palette = palette with { Window = color }; break;
                case "chrome": palette = palette with { Chrome = color }; break;
                case "chrometext": palette = palette with { ChromeText = color }; break;
                case "editor": palette = palette with { Editor = color }; break;
                case "editortext": palette = palette with { EditorText = color }; break;
                case "separator": palette = palette with { Separator = color }; break;
                case "tabback": palette = palette with { TabBack = color }; break;
                case "tabselected": palette = palette with { TabSelected = color }; break;
                case "tabborder": palette = palette with { TabBorder = color }; break;
                case "tabtext": palette = palette with { TabText = color }; break;
                case "tabclose": palette = palette with { TabClose = color }; break;
                case "hoverback": palette = palette with { HoverBack = color }; break;
                case "hovertext": palette = palette with { HoverText = color }; break;
                case "previewback": preview = preview with { Background = color }; break;
                case "previewtext": preview = preview with { Text = color }; break;
                case "previewheading": preview = preview with { Heading = color }; break;
                case "previewlink": preview = preview with { Link = color }; break;
                case "previewcode": preview = preview with { Code = color }; break;
                case "previewborder": preview = preview with { Border = color }; break;
                case "previewmuted": preview = preview with { Muted = color }; break;
                default: throw new FormatException($"unknown color \"{prop.Name}\"");
            }
        }

        return palette with { Preview = preview, IsDark = RelativeLuminance(palette.Editor) < 0.3 };
    }

    public const string SampleTheme = """
        {
          // Copy this file, rename it to something.json, and edit. Colors are #RRGGBB.
          "name": "Solarized Dark",
          "base": "Dark",
          "colors": {
            "window": "#002b36",
            "chrome": "#073642",
            "chromeText": "#eee8d5",
            "editor": "#002b36",
            "editorText": "#93a1a1",
            "separator": "#0b4f5e",
            "tabBack": "#073642",
            "tabSelected": "#002b36",
            "tabBorder": "#0b4f5e",
            "tabText": "#eee8d5",
            "tabClose": "#93a1a1",
            "hoverBack": "#268bd2",
            "hoverText": "#fdf6e3",
            "previewBack": "#002b36",
            "previewText": "#93a1a1",
            "previewHeading": "#eee8d5",
            "previewLink": "#2aa198",
            "previewCode": "#073642",
            "previewBorder": "#0b4f5e",
            "previewMuted": "#839496"
          }
        }
        """;

    public static Color Hex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!HexColorRegex().IsMatch(value))
        {
            throw new FormatException($"\"{value}\" is not a #RRGGBB color");
        }

        var rgb = int.Parse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    public static string ToHex(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    /// <summary>WCAG relative luminance.</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>WCAG contrast ratio (1-21). Body text needs at least 4.5.</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();

    [GeneratedRegex(@"^[\p{L}\p{N} _\-]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThemeNameRegex();
}
