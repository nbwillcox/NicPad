# NicPad

A lightweight, tabbed text and Markdown editor for Windows built with C# and WinForms.

## Features

- **Tabbed editing** - Open and work on multiple files simultaneously
- **Rich text formatting** - Bold, italic, and underline support for `.txt` files
- **Markdown preview** - Live HTML preview for `.md` files with full Markdown rendering
- **Themes** - Four built-in themes: System, Light, Dark, and Matrix
- **Session restore** - Automatically reopens your previous files and theme on launch
- **Per-tab font settings** - Choose font family and size independently for each tab
- **File association** - Pass files as command line arguments to open them directly

## Screenshots

_Dark and Matrix themes provide a comfortable editing experience for low-light environments._

## Requirements

- Windows 10 or later
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (if running from source)

## Running NicPad

### From a published build

Run the executable directly:

```
NicPad.exe
```

Optionally pass files to open on launch:

```
NicPad.exe "C:\path\to\file.txt" "C:\path\to\notes.md"
```

### From source

1. Clone the repository:

   ```
   git clone https://github.com/nbwillcox/NicPad.git
   cd NicPad
   ```

2. Build and run:

   ```
   dotnet run
   ```

3. To publish a standalone executable:
   ```
   dotnet publish -c Release -o publish
   ```
   The executable will be at `publish/NicPad.exe`.

## Project Structure

```
NicPad/
  MainForm.cs      # Main application - UI, theming, editor logic, security
  Program.cs        # Entry point and command line argument handling
  NicPad.csproj     # Project configuration and dependencies
  NicPad.sln        # Visual Studio solution file
  .gitignore        # Git exclusions for build output and IDE files
```

## Keyboard Shortcuts

| Shortcut       | Action                  |
| -------------- | ----------------------- |
| Ctrl+N         | New file                |
| Ctrl+O         | Open file               |
| Ctrl+S         | Save                    |
| Ctrl+Shift+S   | Save As                 |
| Ctrl+W         | Close tab               |
| Ctrl+Tab       | Next tab                |
| Ctrl+Shift+Tab | Previous tab            |
| Ctrl+B         | Bold                    |
| Ctrl+I         | Italic                  |
| Ctrl+U         | Underline               |
| Ctrl+P         | Toggle Markdown preview |
| Ctrl+Z         | Undo                    |
| Ctrl+Y         | Redo                    |
| Ctrl+A         | Select all              |

## Dependencies

- [Markdig](https://github.com/xoofx/markdig) v0.34.0 - Markdown parsing and HTML rendering

## Security

NicPad includes the following security measures:

- **HTML sanitization** - Markdown preview strips script tags, event handlers, and dangerous elements before rendering
- **Input validation** - File paths, font sizes, and session data are validated before use
- **Large file protection** - Warns before opening files over 50 MB
- **Session file limits** - Session restore validates file count, path lengths, and data integrity

## License

This project is provided as-is for personal and educational use.
