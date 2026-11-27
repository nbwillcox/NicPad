# NicPad

A lightweight, tabbed text and Markdown editor for Windows, built with C# and WinForms on .NET 10.

## Features

- **Tabbed editing**: drag to reorder, right-click (or Shift+F10) for tab actions, reopen closed tabs
- **Markdown preview**: full or side-by-side, rendered by WebView2 and locked down (see Security)
- **Markdown tools**: headings, lists, task lists, links, code, tables
- **Rich text**: bold, italic and underline, saved as `.rtf`
- **Find and Replace**, Go To Line, duplicate and move lines, insert date/time
- **Any text file**: detects UTF-8, UTF-8 BOM, UTF-16 and ANSI and saves it back the same way, keeping CRLF/LF line endings; both can be changed under Format
- **Crash recovery**: unsaved work, including Untitled tabs, is backed up every 30 seconds and restored after a crash
- **Session restore**: reopens your files, fonts, zoom and preview mode
- **Themes**: System, Light, Dark, Matrix, plus your own JSON themes. Follows Windows High Contrast
- **Read Aloud** (text-to-speech) and a Voice Typing shortcut
- **Right-to-left** reading order for Arabic, Hebrew and Persian
- **Print**, Print Preview and Export as HTML
- **One window**: files opened from Explorer go to the running NicPad (`--new-window` to override)

## Requirements

- Windows 10 (1809) or later
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) for the Markdown preview (already installed on Windows 11)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), only if you run a framework-dependent build or build from source

## Running NicPad

### From a release

Download `NicPad.exe` from [Releases](https://github.com/nbwillcox/NicPad/releases). It's a self-contained single file. Check it against the published `NicPad.exe.sha256`:

```powershell
(Get-FileHash .\NicPad.exe -Algorithm SHA256).Hash
```

Pass files to open them:

```powershell
.\NicPad.exe "C:\path\to\file.txt" "C:\path\to\notes.md"
```

### From source

```powershell
git clone https://github.com/nbwillcox/NicPad.git
cd NicPad
dotnet run --project NicPad.csproj
```

Build, test and publish:

```powershell
dotnet build NicPad.sln -c Release -warnaserror
dotnet test --solution NicPad.sln -c Release
dotnet publish NicPad.csproj -c Release -p:PublishProfile=win-x64
```

The single-file exe lands in `artifacts/publish/win-x64/`. To launch it against a throwaway data folder and take a screenshot:

```powershell
powershell -File tools/smoke-test.ps1 -Exe artifacts/publish/win-x64/NicPad.exe -OutPng smoke.png
```

## Keyboard Shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+N / Ctrl+O / Ctrl+S | New / Open / Save |
| Ctrl+Shift+S | Save As |
| Ctrl+W, Ctrl+F4 | Close tab |
| Ctrl+Shift+T | Reopen closed tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Ctrl+F / Ctrl+H | Find / Replace |
| F3 / Shift+F3 | Find next / previous |
| Ctrl+G | Go to line |
| Ctrl+D | Duplicate line |
| Alt+Up / Alt+Down | Move line up / down |
| F5 | Insert date and time |
| Ctrl+B / Ctrl+I / Ctrl+U | Bold / italic / underline (Markdown syntax in `.md` files) |
| Ctrl+1 / 2 / 3 | Heading level (Markdown) |
| Ctrl+Shift+8 / 7 / 9 | Bulleted / numbered / task list (Markdown) |
| Ctrl+K / Ctrl+` | Link / inline code (Markdown) |
| Ctrl+P | Toggle preview |
| Ctrl+Shift+V | Toggle side-by-side preview |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom in / out / reset (also Ctrl+mouse wheel) |
| Alt+Z | Word wrap |
| Ctrl+Shift+R | Read aloud / stop |
| F1 | Keyboard shortcuts |

## Customizing

Settings live in `%AppData%\NicPad`: `settings.json` (preferences), `session.json` (open tabs), `backups\` (crash recovery) and `themes\`.

**Custom themes:** View > Theme > Open Themes Folder creates `sample-theme.json.example`. Copy it to a new `.json` file, edit the `#RRGGBB` colors, then View > Theme > Reload Themes. Invalid files are skipped and reported in the status bar.

## Security

- **Locked-down preview**: WebView2 with JavaScript, dev tools, downloads, pop-ups and permission requests disabled; every navigation is blocked and links open in your browser only after you confirm the address.
- **Allowlist sanitizer**: Markdown HTML goes through HtmlSanitizer with a short list of allowed tags, attributes and CSS properties, then a URL allowlist (http, https, mailto, `#anchors`, embedded raster images).
- **No credential leaks**: `file:`, UNC (`\\server\share`) and `//host` URLs are always blocked, so a document can't make Windows authenticate to another machine.
- **No tracking pixels**: remote images are off until you turn on Tools > Allow Remote Images in Preview.
- **Safe RTF**: embedded OLE objects are stripped before an `.rtf` file is loaded.
- **Crash-safe saves**: every write goes to a temp file first and is swapped in, so the original survives a crash or full disk.
- **Validated data**: session, settings, theme and backup files are size-limited and validated; backup ids are checked before they become file names.
- **Single-instance channel** accepts connections from the same Windows user only.
- **Supply chain**: NuGet vulnerability audit and .NET security analyzers run on every build with warnings as errors. CI repeats the audit on every push.

Found a vulnerability? Please open a private security advisory on GitHub rather than a public issue.

## Project Structure

```
Core/                 Logic with no UI dependencies (unit-tested)
  MarkdownRenderer    Markdig + HtmlSanitizer + CSP page template
  UrlPolicy           Link/image/request allowlist
  TextFileIO          Encoding and line-ending detection, atomic writes
  TextOps             Find/replace, line and Markdown edits, counts
  AppSettings, SessionStore, BackupStore, Theme, RtfSafety, SingleInstance, CrashLog
UI/                   WinForms controls: DocumentTab, EditorBox, PreviewHost (WebView2),
                      ThemedControls, Find/Replace and Go To dialogs, Dialogs, SpeechReader
MainForm*.cs          Main window, split by area (Menus, Files, Editing, Theme)
Program.cs            Entry point, single instance, crash handling
NicPad.Tests/         xUnit tests, including UI tests that drive the real window
tools/smoke-test.ps1  Launch-and-screenshot smoke test
```

## Dependencies

- [Markdig](https://github.com/xoofx/markdig) 1.6.0: Markdown parsing
- [HtmlSanitizer](https://github.com/mganss/HtmlSanitizer) 11.0.74: allowlist HTML sanitizing (uses AngleSharp)
- [Microsoft.Web.WebView2](https://www.nuget.org/packages/Microsoft.Web.WebView2) 1.0.4370.26: preview browser
- [System.Speech](https://www.nuget.org/packages/System.Speech) 10.0.15: Read Aloud

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

This project is provided as-is for personal and educational use.
