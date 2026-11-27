using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace NicPad.Core;

public enum TextEncodingKind
{
    Utf8,
    Utf8Bom,
    Utf16LE,
    Utf16BE,
    Ansi,
}

public enum LineEnding
{
    CrLf,
    Lf,
    Cr,
}

/// <summary>Decoded file contents. <see cref="Text"/> always uses "\n" line breaks (what RichTextBox uses).</summary>
public sealed record LoadedText(string Text, TextEncodingKind Encoding, LineEnding LineEnding, bool LooksBinary);

/// <summary>Encoding detection, line-ending handling and crash-safe writes.</summary>
public static class TextFileIO
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    static TextFileIO()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static int AnsiCodePage
    {
        get
        {
            var cp = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            return cp is 0 or 65001 ? 1252 : cp;
        }
    }

    public static Encoding GetEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        TextEncodingKind.Utf8Bom => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        TextEncodingKind.Utf16LE => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        TextEncodingKind.Utf16BE => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        TextEncodingKind.Ansi => Encoding.GetEncoding(AnsiCodePage),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string DisplayName(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Utf8Bom => "UTF-8 BOM",
        TextEncodingKind.Utf16LE => "UTF-16 LE",
        TextEncodingKind.Utf16BE => "UTF-16 BE",
        TextEncodingKind.Ansi => $"ANSI ({AnsiCodePage})",
        _ => kind.ToString(),
    };

    public static string DisplayName(LineEnding eol) => eol switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => eol.ToString(),
    };

    public static string Sequence(LineEnding eol) => eol switch
    {
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => "\r\n",
    };

    public static LoadedText ReadFile(string path, long maxBytes)
    {
        var info = new FileInfo(path);
        if (info.Length > maxBytes)
        {
            throw new IOException($"The file is larger than {maxBytes / (1024 * 1024)} MB.");
        }

        return Decode(File.ReadAllBytes(path));
    }

    public static LoadedText Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        TextEncodingKind kind;
        var skip = 0;

        if (bytes.AsSpan().StartsWith(Utf8Bom))
        {
            kind = TextEncodingKind.Utf8Bom;
            skip = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            kind = TextEncodingKind.Utf16LE;
            skip = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            kind = TextEncodingKind.Utf16BE;
            skip = 2;
        }
        else if (GuessUtf16WithoutBom(bytes) is { } utf16)
        {
            kind = utf16;
        }
        else if (Utf8.IsValid(bytes))
        {
            kind = TextEncodingKind.Utf8;
        }
        else
        {
            kind = TextEncodingKind.Ansi;
        }

        var text = GetEncoding(kind).GetString(bytes, skip, bytes.Length - skip);
        var looksBinary = kind is not (TextEncodingKind.Utf16LE or TextEncodingKind.Utf16BE) && text.Contains('\0', StringComparison.Ordinal);
        return new LoadedText(NormalizeToLf(text), kind, DetectLineEnding(text), looksBinary);
    }

    /// <summary>Encodes "\n"-separated text with the requested line ending and encoding (BOM included where the kind has one).</summary>
    public static byte[] Encode(string text, TextEncodingKind kind, LineEnding eol)
    {
        ArgumentNullException.ThrowIfNull(text);
        var withEol = ApplyLineEnding(NormalizeToLf(text), eol);
        var encoding = GetEncoding(kind);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(withEol);
        if (preamble.Length == 0)
        {
            return body;
        }

        var result = new byte[preamble.Length + body.Length];
        preamble.CopyTo(result, 0);
        body.CopyTo(result, preamble.Length);
        return result;
    }

    /// <summary>True when every character survives the round trip (ANSI code pages can't hold emoji, CJK, etc.).</summary>
    public static bool CanEncode(string text, TextEncodingKind kind)
    {
        if (kind != TextEncodingKind.Ansi)
        {
            return true;
        }

        var strict = Encoding.GetEncoding(AnsiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            strict.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public static string NormalizeToLf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains('\r', StringComparison.Ordinal))
        {
            return text;
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    public static string ApplyLineEnding(string lfText, LineEnding eol)
    {
        ArgumentNullException.ThrowIfNull(lfText);
        return eol == LineEnding.Lf ? lfText : lfText.Replace("\n", Sequence(eol), StringComparison.Ordinal);
    }

    public static LineEnding DetectLineEnding(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (lf > crlf && lf >= cr)
        {
            return LineEnding.Lf;
        }

        if (cr > crlf && cr > lf)
        {
            return LineEnding.Cr;
        }

        return LineEnding.CrLf;
    }

    /// <summary>
    /// Writes to a temp file in the same folder, flushes it to disk, then swaps it in.
    /// A crash or full disk leaves the original untouched.
    /// </summary>
    public static void WriteAtomic(string path, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path) ?? throw new IOException("Invalid path.");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(data);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Copy(temp, path, overwrite: true);
                }
                catch (IOException) when (File.Exists(temp) && !IsLocked(path))
                {
                    // Some file systems (FAT, a few network shares) don't support ReplaceFile.
                    File.Copy(temp, path, overwrite: true);
                }
            }
            else
            {
                File.Move(temp, path);
            }
        }
        catch (UnauthorizedAccessException) when (!File.Exists(temp) && File.Exists(path) && !new FileInfo(path).IsReadOnly)
        {
            // The folder isn't writable but the file is: fall back to an in-place write.
            using var fs = new FileStream(path, FileMode.Truncate, FileAccess.Write, FileShare.None);
            fs.Write(data);
            fs.Flush(flushToDisk: true);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static bool IsLocked(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static TextEncodingKind? GuessUtf16WithoutBom(byte[] bytes)
    {
        var sample = Math.Min(bytes.Length, 4096) & ~1;
        if (sample < 4)
        {
            return null;
        }

        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < sample; i += 2)
        {
            if (bytes[i] == 0)
            {
                evenZeros++;
            }

            if (bytes[i + 1] == 0)
            {
                oddZeros++;
            }
        }

        var pairs = sample / 2;
        if (oddZeros > pairs * 0.4 && evenZeros < pairs * 0.05)
        {
            return TextEncodingKind.Utf16LE;
        }

        if (evenZeros > pairs * 0.4 && oddZeros < pairs * 0.05)
        {
            return TextEncodingKind.Utf16BE;
        }

        return null;
    }
}
