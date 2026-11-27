using System.Text;
using NicPad.Core;
using Xunit;

namespace NicPad.Tests;

public class TextFileIOTests
{
    private const string Sample = "héllo wörld 😀 日本語\nline 2";

    [Fact]
    public void Utf8WithoutBomIsDetected()
    {
        var loaded = TextFileIO.Decode(new UTF8Encoding(false).GetBytes(Sample));
        Assert.Equal(TextEncodingKind.Utf8, loaded.Encoding);
        Assert.Equal(Sample, loaded.Text);
        Assert.False(loaded.LooksBinary);
    }

    [Fact]
    public void Utf8BomIsDetectedAndStripped()
    {
        var loaded = TextFileIO.Decode(TextFileIO.Encode(Sample, TextEncodingKind.Utf8Bom, LineEnding.Lf));
        Assert.Equal(TextEncodingKind.Utf8Bom, loaded.Encoding);
        Assert.Equal(Sample, loaded.Text);
    }

    [Theory]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.Utf8Bom)]
    [InlineData(TextEncodingKind.Utf16LE)]
    [InlineData(TextEncodingKind.Utf16BE)]
    public void UnicodeEncodingsRoundTrip(TextEncodingKind kind)
    {
        foreach (var eol in Enum.GetValues<LineEnding>())
        {
            var bytes = TextFileIO.Encode(Sample, kind, eol);
            var loaded = TextFileIO.Decode(bytes);
            Assert.Equal(kind, loaded.Encoding);
            Assert.Equal(eol, loaded.LineEnding);
            Assert.Equal(Sample, loaded.Text);
        }
    }

    [Fact]
    public void Utf16LeWithoutBomIsDetected()
    {
        var bytes = new UnicodeEncoding(false, false).GetBytes("plain ascii text here\r\nmore");
        var loaded = TextFileIO.Decode(bytes);
        Assert.Equal(TextEncodingKind.Utf16LE, loaded.Encoding);
        Assert.Equal("plain ascii text here\nmore", loaded.Text);
    }

    [Fact]
    public void InvalidUtf8FallsBackToAnsi()
    {
        var bytes = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 }; // "café" in Windows-1252
        var loaded = TextFileIO.Decode(bytes);
        Assert.Equal(TextEncodingKind.Ansi, loaded.Encoding);
        if (TextFileIO.AnsiCodePage == 1252)
        {
            Assert.Equal("café", loaded.Text);
        }
    }

    [Theory]
    [InlineData("a\r\nb\r\nc", LineEnding.CrLf)]
    [InlineData("a\nb\nc", LineEnding.Lf)]
    [InlineData("a\rb\rc", LineEnding.Cr)]
    [InlineData("a\nb\nc\r\nd", LineEnding.Lf)]
    [InlineData("no breaks", LineEnding.CrLf)]
    public void LineEndingsAreDetected(string text, LineEnding expected)
    {
        Assert.Equal(expected, TextFileIO.DetectLineEnding(text));
    }

    [Fact]
    public void CrlfIsRestoredOnEncode()
    {
        // BUG-09: RichTextBox hands back "\n"; the original CRLF must be written back.
        var bytes = TextFileIO.Encode("a\nb", TextEncodingKind.Utf8, LineEnding.CrLf);
        Assert.Equal("a\r\nb", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void AnsiCannotHoldEmoji()
    {
        Assert.False(TextFileIO.CanEncode("smile 😀", TextEncodingKind.Ansi));
        Assert.True(TextFileIO.CanEncode("plain", TextEncodingKind.Ansi));
        Assert.True(TextFileIO.CanEncode("smile 😀", TextEncodingKind.Utf8));
    }

    [Fact]
    public void NulBytesLookBinary()
    {
        Assert.True(TextFileIO.Decode([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x41]).LooksBinary);
    }

    [Fact]
    public void WriteAtomicCreatesAndReplacesWithoutLeavingTempFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("doc.txt");
        TextFileIO.WriteAtomic(path, Encoding.UTF8.GetBytes("first"));
        Assert.Equal("first", File.ReadAllText(path));

        TextFileIO.WriteAtomic(path, Encoding.UTF8.GetBytes("second"));
        Assert.Equal("second", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void WriteAtomicLeavesReadOnlyFileUntouched()
    {
        using var dir = new TempDir();
        var path = dir.File("locked.txt");
        File.WriteAllText(path, "original");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        Assert.Throws<UnauthorizedAccessException>(() => TextFileIO.WriteAtomic(path, Encoding.UTF8.GetBytes("new")));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public void ReadFileRefusesOversizedFiles()
    {
        using var dir = new TempDir();
        var path = dir.File("big.txt");
        File.WriteAllBytes(path, new byte[2048]);
        Assert.Throws<IOException>(() => TextFileIO.ReadFile(path, maxBytes: 1024));
    }
}
