using System.Drawing;
using System.Net;
using System.Text;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;

namespace NicPad.Core;

/// <summary>
/// Markdown → HTML with an allowlist sanitizer and <see cref="UrlPolicy"/> applied to every link and image.
/// The result is safe to show in the preview even when the document came from someone else.
/// </summary>
public sealed class MarkdownRenderer
{
    private static readonly string[] AllowedTags =
    [
        "a", "abbr", "b", "blockquote", "br", "caption", "cite", "code", "col", "colgroup", "dd", "del", "details",
        "dfn", "div", "dl", "dt", "em", "figcaption", "figure", "h1", "h2", "h3", "h4", "h5", "h6", "hr", "i", "img",
        "input", "ins", "kbd", "li", "mark", "ol", "p", "pre", "q", "s", "samp", "small", "span", "strike", "strong",
        "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th", "thead", "tr", "u", "ul", "var",
    ];

    private static readonly string[] AllowedAttributes =
    [
        "href", "src", "alt", "title", "class", "id", "colspan", "rowspan", "align", "start", "reversed", "type",
        "checked", "disabled", "open", "width", "height", "dir", "lang", "style", "scope",
    ];

    private static readonly string[] AllowedCss =
    [
        "color", "background-color", "font-weight", "font-style", "text-decoration", "text-align", "vertical-align",
    ];

    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly Lazy<HtmlSanitizer> strict = new(() => CreateSanitizer(allowRemoteImages: false));
    private readonly Lazy<HtmlSanitizer> remote = new(() => CreateSanitizer(allowRemoteImages: true));

    public string RenderBody(string markdown, bool allowRemoteImages)
    {
        var html = Markdown.ToHtml(markdown ?? "", pipeline);
        return Sanitize(html, allowRemoteImages);
    }

    public string Sanitize(string html, bool allowRemoteImages)
    {
        return (allowRemoteImages ? remote.Value : strict.Value).Sanitize(html ?? "");
    }

    /// <summary>A complete HTML page with a restrictive Content-Security-Policy and theme colors.</summary>
    public string RenderDocument(string markdown, PreviewColors colors, bool isDark, bool allowRemoteImages, string title)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var body = RenderBody(markdown, allowRemoteImages);
        var imgSrc = allowRemoteImages ? "data: https: http:" : "data:";
        var sb = new StringBuilder(body.Length + 4096);
        sb.Append("<!DOCTYPE html>\n<html>\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; img-src ").Append(imgSrc)
          .Append("; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">\n");
        sb.Append("<meta name=\"color-scheme\" content=\"").Append(isDark ? "dark" : "light").Append("\">\n");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(title ?? "")).Append("</title>\n<style>\n");
        sb.Append(BuildCss(colors));
        sb.Append("</style>\n</head>\n<body>\n").Append(body).Append("\n</body>\n</html>\n");
        return sb.ToString();
    }

    private static string BuildCss(PreviewColors c)
    {
        string H(Color color) => Themes.ToHex(color);
        return $$"""
            body { font-family: 'Segoe UI', 'Segoe UI Emoji', 'Segoe UI Symbol', 'Microsoft YaHei UI', 'Yu Gothic UI', 'Malgun Gothic', 'Nirmala UI', sans-serif;
                   font-size: 14px; line-height: 1.6; padding: 20px; max-width: 860px; margin: 0 auto; color: {{H(c.Text)}}; background: {{H(c.Background)}}; }
            p, li, h1, h2, h3, h4, h5, h6, td, th, blockquote, dt, dd, summary { unicode-bidi: plaintext; }
            h1, h2, h3, h4, h5, h6 { color: {{H(c.Heading)}}; margin-top: 1.5em; margin-bottom: 0.5em; }
            h1, h2 { border-bottom: 1px solid {{H(c.Border)}}; padding-bottom: 0.2em; }
            code { background: {{H(c.Code)}}; padding: 2px 6px; border-radius: 3px; font-family: 'Cascadia Mono', Consolas, monospace; }
            pre { background: {{H(c.Code)}}; padding: 15px; border-radius: 5px; overflow-x: auto; }
            pre code { padding: 0; background: none; }
            blockquote { border-left: 4px solid {{H(c.Border)}}; margin-left: 0; padding-left: 20px; color: {{H(c.Muted)}}; }
            a { color: {{H(c.Link)}}; }
            table { border-collapse: collapse; width: 100%; }
            th, td { border: 1px solid {{H(c.Border)}}; padding: 8px; text-align: left; }
            th { background: {{H(c.Code)}}; }
            img { max-width: 100%; }
            hr { border: 0; border-top: 1px solid {{H(c.Border)}}; }
            li.task-list-item { list-style: none; }
            .nicpad-blocked { color: {{H(c.Muted)}}; border: 1px dashed {{H(c.Border)}}; padding: 0 6px; border-radius: 3px; font-size: 0.9em; }
            """;
    }

    private static HtmlSanitizer CreateSanitizer(bool allowRemoteImages)
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        s.AllowedTags.UnionWith(AllowedTags);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.UnionWith(AllowedAttributes);
        s.AllowedCssProperties.Clear();
        s.AllowedCssProperties.UnionWith(AllowedCss);
        s.AllowedAtRules.Clear();
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.UnionWith(["http", "https", "mailto", "data"]);
        s.UriAttributes.Clear();
        s.UriAttributes.UnionWith(["href", "src"]);
        s.UriListAttributes.Clear();
        s.AllowDataAttributes = false;
        s.KeepChildNodes = false;

        s.FilterUrl += (_, e) =>
        {
            if (e.SanitizedUrl is null)
            {
                return;
            }

            var kind = string.Equals(e.Tag?.TagName, "IMG", StringComparison.OrdinalIgnoreCase) ? UrlKind.Image : UrlKind.Link;
            e.SanitizedUrl = UrlPolicy.Filter(e.OriginalUrl, kind, allowRemoteImages) is null
                ? null
                : UrlPolicy.Filter(e.SanitizedUrl, kind, allowRemoteImages);
        };

        s.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement element)
            {
                return;
            }

            if (element.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase) && !element.HasAttribute("src"))
            {
                // Show what was blocked instead of a broken-image icon.
                var alt = element.GetAttribute("alt");
                var label = e.Document.CreateElement("span");
                label.ClassName = "nicpad-blocked";
                label.TextContent = string.IsNullOrWhiteSpace(alt) ? "image blocked" : $"image blocked: {alt}";
                label.SetAttribute("title", allowRemoteImages
                    ? "Only http, https and embedded images are shown"
                    : "Remote images are off (Tools > Allow Remote Images in Preview)");
                e.ReplacementNodes.Add(label);
            }
            else if (element.TagName.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
            {
                // Only Markdown task-list checkboxes, always read-only.
                if (!string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                {
                    e.ReplacementNodes.Add(e.Document.CreateTextNode(""));
                    return;
                }

                element.SetAttribute("disabled", "disabled");
            }
            else if (element.TagName.Equals("A", StringComparison.OrdinalIgnoreCase) && element.HasAttribute("href"))
            {
                element.SetAttribute("rel", "noopener noreferrer");
            }
        };

        return s;
    }
}
