using NicPad.Core;
using Xunit;

namespace NicPad.Tests;

public class TextOpsTests
{
    private static string Apply(string text, TextEdit edit) =>
        text[..edit.Start] + edit.Replacement + text[(edit.Start + edit.Length)..];

    [Fact]
    public void FindNextForwardBackwardAndWrap()
    {
        const string text = "cat dog cat bird cat";
        var o = new FindOptions("cat", MatchCase: false, WholeWord: false);
        Assert.Equal(0, TextOps.FindNext(text, o, 0, forward: true));
        Assert.Equal(8, TextOps.FindNext(text, o, 3, forward: true));
        Assert.Equal(17, TextOps.FindNext(text, o, 9, forward: true));
        Assert.Equal(0, TextOps.FindNext(text, o, 18, forward: true)); // wraps
        Assert.Equal(8, TextOps.FindNext(text, o, 17, forward: false));
        Assert.Equal(17, TextOps.FindNext(text, o, 0, forward: false)); // wraps backward
        Assert.Equal(-1, TextOps.FindNext(text, o with { WrapAround = false }, 18, forward: true));
    }

    [Fact]
    public void FindRespectsCaseAndWholeWord()
    {
        const string text = "Cat category CAT cat_x cat";
        Assert.Equal(13, TextOps.FindNext(text, new FindOptions("CAT", MatchCase: true, WholeWord: false), 0, true));
        var whole = new FindOptions("cat", MatchCase: false, WholeWord: true);
        Assert.Equal([0, 13, 23], TextOps.AllMatches(text, whole).ToArray());
    }

    [Fact]
    public void ReplaceAllCountsAndReplaces()
    {
        var (result, count) = TextOps.ReplaceAll("a-b-a-c", new FindOptions("a", false, false), "zz");
        Assert.Equal("zz-b-zz-c", result);
        Assert.Equal(2, count);

        var (same, none) = TextOps.ReplaceAll("abc", new FindOptions("x", false, false), "y");
        Assert.Equal("abc", same);
        Assert.Equal(0, none);
    }

    [Fact]
    public void ReplaceAllHandlesReplacementContainingQuery()
    {
        var (result, count) = TextOps.ReplaceAll("aa", new FindOptions("a", false, false), "aa");
        Assert.Equal("aaaa", result);
        Assert.Equal(2, count);
    }

    [Fact]
    public void DuplicateLine()
    {
        const string text = "one\ntwo\nthree";
        var edit = TextOps.DuplicateLines(text, 5, 0);
        Assert.Equal("one\ntwo\ntwo\nthree", Apply(text, edit));
        Assert.Equal(9, edit.SelectionStart);
    }

    [Fact]
    public void MoveLinesUpAndDown()
    {
        const string text = "one\ntwo\nthree";
        var up = TextOps.MoveLines(text, 5, 0, up: true)!.Value;
        Assert.Equal("two\none\nthree", Apply(text, up));
        Assert.Equal(1, up.SelectionStart);

        var down = TextOps.MoveLines(text, 5, 0, up: false)!.Value;
        Assert.Equal("one\nthree\ntwo", Apply(text, down));

        Assert.Null(TextOps.MoveLines(text, 0, 0, up: true));
        Assert.Null(TextOps.MoveLines(text, 10, 0, up: false));
    }

    [Fact]
    public void SelectionEndingAtLineStartExcludesThatLine()
    {
        const string text = "one\ntwo\nthree";
        Assert.Equal((0, 3), TextOps.LineSpan(text, 0, 4));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(99, 8)]
    public void LineStartIndex(int line, int expected)
    {
        Assert.Equal(expected, TextOps.LineStartIndex("one\ntwo\nthree", line));
    }

    [Fact]
    public void CountsWordsAndLines()
    {
        Assert.Equal(4, TextOps.WordCount("  one two\tthree\nfour  "));
        Assert.Equal(0, TextOps.WordCount("   "));
        Assert.Equal(3, TextOps.LineCount("a\nb\nc"));
        Assert.Equal(1, TextOps.LineCount(""));
    }

    [Fact]
    public void WrapTogglesMarkers()
    {
        const string text = "make this bold";
        var wrap = TextOps.Wrap(text, 10, 4, "**", "**", "text");
        var wrapped = Apply(text, wrap);
        Assert.Equal("make this **bold**", wrapped);
        Assert.Equal(12, wrap.SelectionStart);

        var unwrap = TextOps.Wrap(wrapped, wrap.SelectionStart, wrap.SelectionLength, "**", "**", "text");
        Assert.Equal(text, Apply(wrapped, unwrap));

        var empty = TextOps.Wrap("", 0, 0, "*", "*", "text");
        Assert.Equal("*text*", empty.Replacement);
        Assert.Equal(1, empty.SelectionStart);
        Assert.Equal(4, empty.SelectionLength);
    }

    [Fact]
    public void InsertLinkSelectsUrl()
    {
        var edit = TextOps.InsertLink("see docs", 4, 4);
        Assert.Equal("[docs](https://)", edit.Replacement);
        Assert.Equal("https://", Apply("see docs", edit).Substring(edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void HeadingsSetAndToggle()
    {
        const string text = "Title\nbody";
        var h2 = TextOps.SetHeading(text, 0, 0, 2);
        var once = Apply(text, h2);
        Assert.Equal("## Title\nbody", once);
        Assert.Equal("# Title\nbody", Apply(once, TextOps.SetHeading(once, 0, 0, 1)));
        Assert.Equal(text, Apply(once, TextOps.SetHeading(once, 0, 0, 2)));
    }

    [Fact]
    public void ListsApplyAndToggle()
    {
        const string text = "a\nb\nc";
        var numbered = Apply(text, TextOps.ToggleList(text, 0, text.Length, ListKind.Numbered));
        Assert.Equal("1. a\n2. b\n3. c", numbered);

        var bullets = Apply(numbered, TextOps.ToggleList(numbered, 0, numbered.Length, ListKind.Bullet));
        Assert.Equal("- a\n- b\n- c", bullets);

        Assert.Equal(text, Apply(bullets, TextOps.ToggleList(bullets, 0, bullets.Length, ListKind.Bullet)));
        Assert.Equal("- [ ] a", Apply("a", TextOps.ToggleList("a", 0, 0, ListKind.Task)));
        Assert.Equal("> a", Apply("a", TextOps.ToggleList("a", 0, 0, ListKind.Quote)));
    }

    [Fact]
    public void InsertBlockKeepsBlankLineBefore()
    {
        var edit = TextOps.InsertBlock("para", 4, "---");
        Assert.Equal("para\n\n---\n", Apply("para", edit));
    }
}
