# Changelog

All notable changes to NicPad. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions follow [SemVer](https://semver.org/).
IDs in parentheses refer to the improvement backlog.

## [Unreleased]

## [1.1.0] - 2026-11-27

### Security
- Retargeted from .NET 8 (out of support since 2026-11-10) to .NET 10 LTS. (SEC-01)
- Markdown preview now runs on WebView2 (Edge/Chromium) instead of the retired Internet Explorer engine, with scripts, dev tools, downloads, pop-ups, permissions and navigation all disabled, an isolated profile folder, and a strict Content-Security-Policy. (SEC-12)
- Replaced the regex HTML filter with an allowlist sanitizer (HtmlSanitizer/AngleSharp). Unknown tags, event handlers, `<style>`, `<base>`, `<svg>`, `srcset` and Markdig `{onclick=…}` attributes are all removed. Covered by a 34-payload XSS test corpus. (SEC-09)
- Links and images go through a URL allowlist: links may be http, https, mailto or `#anchor`; images may be embedded raster data or, if you opt in, http/https. (SEC-03)
- `file:`, UNC (`\\host\share`) and `//host` URLs are always blocked in the preview, so a crafted `.md` file can't make Windows send your credentials to another machine. (SEC-02)
- Remote images are off by default (Tools > Allow Remote Images in Preview) and show a "blocked" placeholder, so previewing someone else's file doesn't reveal your IP address to a tracking pixel. (SEC-04)
- Clicking a link in the preview asks before opening it in your browser and shows the full address. Only http, https and mailto links can be opened. (SEC-08)
- Documents, session and settings are saved atomically (temp file, flush, swap), so a crash or full disk can't leave a half-written file. (SEC-05)
- Embedded OLE objects are stripped from `.rtf` files before loading. Text, formatting and pictures are kept. (SEC-15)
- Unexpected errors back up all unsaved work, write `crash.log` (exception details only, never document text), and tell you what happened instead of closing. (SEC-07)
- Session, settings, theme and backup files are size-limited and validated on load; backup ids are checked before they become file names. (SEC-06)
- Markdig upgraded to 1.6.0. NuGet vulnerability audit and .NET analyzers (`latest-recommended`) now run on every build, with warnings treated as errors. (SEC-10, SEC-11)
- The single-instance pipe only accepts connections from the same Windows user. (FEA-15)

### Fixed
- Opening or restoring a file no longer marks the previously selected tab as modified. (BUG-01)
- `.txt` files are no longer read and written through the ANSI code page, which replaced emoji, accents and non-Latin text with `?` on save. (BUG-02)
- Bold, italic and underline in a plain-text file are no longer silently discarded: NicPad offers to save as `.rtf`. (BUG-03)
- Font objects are no longer leaked on every font change. (BUG-04)
- Font sizes outside the preset list (for example 13.5) no longer blank the size box; it's now editable (6-72). (BUG-05)
- Session restore no longer pops dialogs at startup; problems are summarized in the status bar. (BUG-06)
- Files are read from disk once instead of twice. (BUG-07)
- Pasting from a web page into a plain-text or Markdown tab pastes plain text, so dark themes can't get invisible black text. RichEdit's hidden alignment shortcuts are disabled in plain-text tabs. (BUG-08)
- CRLF files are saved with CRLF again (they were rewritten as LF). (BUG-09)
- A second launch no longer overwrites the first window's session. (BUG-10)
- Saving to a read-only or locked file shows what happened and how to fix it instead of crashing. (SEC-06)

### Added
- Find (Ctrl+F), Find Next / Previous (F3 / Shift+F3), Replace and Replace All (Ctrl+H), with match case, whole word and wrap around. Replace All is a single undo step. (FEA-01)
- Go To Line (Ctrl+G). (FEA-02)
- Word wrap toggle (Alt+Z), remembered between sessions. (FEA-03)
- Zoom in / out / reset (Ctrl++, Ctrl+-, Ctrl+0, Ctrl+mouse wheel), restored per tab. (FEA-04)
- Insert date and time (F5). (FEA-05)
- Recent Files menu and Reopen Closed Tab (Ctrl+Shift+T). (FEA-06)
- Drag and drop files onto the window to open them. (FEA-07)
- Tab menu (right-click or Shift+F10): Close, Close Others, Close Tabs to the Right, Close All, Copy Full Path, Open Containing Folder, Reload from Disk. (FEA-08)
- Duplicate line (Ctrl+D) and move lines up/down (Alt+Up / Alt+Down). (FEA-09)
- Detects when an open file is changed or deleted by another program and offers to reload it. (FEA-10)
- Auto-backup of unsaved changes, including Untitled tabs, every 30 seconds; restored after a crash. Optional "Remember Unsaved Changes on Exit". (FEA-11)
- Side-by-side live Markdown preview (Ctrl+Shift+V) that keeps your scroll position while you type. (FEA-12)
- Export Markdown as a standalone, sanitized HTML page. (FEA-13)
- Encoding detection and choice (UTF-8, UTF-8 with BOM, UTF-16 LE/BE, ANSI) and line endings (CRLF, LF, CR) under Format. Warns before saving characters the chosen encoding can't hold. (FEA-14, LANG-01)
- Single window: opening a file from Explorer while NicPad is running opens it in the existing window. Use `--new-window` to override. (FEA-15)
- Print and Print Preview. (FEA-16)
- Markdown tools: headings (Ctrl+1/2/3), bulleted, numbered and task lists, quote, link (Ctrl+K), inline code (Ctrl+`), code block, table, horizontal rule. (FEA-17)
- Drag tabs to reorder them. (FEA-18)
- Window title shows the file name and an unsaved marker. (UI-01)
- Window size, position and maximized state are remembered. (UI-02)
- The preview follows the theme (dark preview in Dark and Matrix). (UI-03)
- Help > About (version, .NET and WebView2 versions) and Help > Keyboard Shortcuts (F1). (UI-04)
- Status bar shows line, column, selection, line/word/character counts, encoding, line ending, zoom and file type. (UI-05)
- Dark native title bar in Dark and Matrix themes. (UI-06)
- Per-monitor high-DPI support with a DPI-scaled tab close button. (UI-07)
- Close-button hover highlight, and theme-colored tab frame. (UI-08)
- Preferences kept in `settings.json` (theme, default font, word wrap, toolbar and status bar visibility, Always on Top, remote images, backups). Format > Use Current Font as Default. (CUS-01, CUS-02)
- Custom themes from JSON files in `%AppData%\NicPad\themes` (View > Theme > Open Themes Folder includes a sample). (CUS-03)
- Read Aloud (Ctrl+Shift+R) with voice and speed choices, and a Voice Typing shortcut (Win+H). (VRB-02, VRB-03)
- Right-to-left reading order for Arabic, Hebrew and Persian. (LANG-02)
- The preview sets text direction per paragraph and uses a font stack that covers CJK and emoji. (LANG-03)
- Accessible names on toolbar buttons, boxes and the status bar for screen readers; keyboard access to the tab menu and Ctrl+F4 to close a tab. (A11Y-01, A11Y-03)
- Windows High Contrast is honored automatically. (A11Y-02)

### Changed
- Save prompts say "Save / Don't Save / Cancel" and every error message says what happened and what to do next. (VRB-01)
- The theme moved from `session.json` to `settings.json`; it's migrated automatically.
- The preview is created only when you first open it, and re-renders 300 ms after you stop typing instead of on every keystroke. (PERF-01, PERF-03)
- Status bar counts no longer copy the whole document on every caret move. (PERF-02)
- `MainForm.cs` (1,667 lines) is split into `Core/` (testable logic) and `UI/` (controls), plus feature-area partial classes, and theme colors are defined once. (TEC-04, TEC-05)

### Development
- xUnit test project with 163 tests: sanitizer XSS corpus, URL policy, encodings, atomic writes, text operations, settings/session/backup validation, theme contrast (WCAG AA), RTF safety, single-instance IPC, and UI tests that drive the real window. (TEC-06, A11Y-04)
- GitHub Actions CI: build with warnings as errors, tests, vulnerable-package audit; tagged builds publish a single-file exe with a SHA-256 checksum. (TEC-07)
- Self-contained single-file publish profile (`-p:PublishProfile=win-x64`). (TEC-08)
- `tools/smoke-test.ps1` launches the app against a throwaway data folder and screenshots it.
- `.editorconfig`, nullable warnings as errors, version number in the project file. (TEC-02, TEC-03)

### Removed
- `bin/`, `obj/` and `publish/` build output is no longer tracked in git (163 MB). Get builds from GitHub Releases or build from source. (TEC-01)

## [1.0.0] - 2026-02-13

- Initial release: tabbed text and Markdown editor with preview, four themes, session restore and per-tab fonts.
