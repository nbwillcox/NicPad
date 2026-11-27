using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NicPad.Core;

/// <summary>Replace [Start, Start+Length) with Replacement, then select [SelectionStart, +SelectionLength).</summary>
public readonly record struct TextEdit(int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

public sealed record FindOptions(string Query, bool MatchCase, bool WholeWord, bool WrapAround = true);

public enum ListKind
{
    Bullet,
    Numbered,
    Task,
    Quote,
}

/// <summary>Editor operations on "\n"-separated text. Pure functions so they can be unit-tested without UI.</summary>
public static partial class TextOps
{
    // ---------- Find / replace ----------

    public static int FindNext(string text, FindOptions options, int start, bool forward)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Query))
        {
            return -1;
        }

        start = Math.Clamp(start, 0, text.Length);
        var hit = forward ? Scan(text, options, start, text.Length, true) : Scan(text, options, 0, start, false);
        if (hit < 0 && options.WrapAround)
        {
            hit = forward ? Scan(text, options, 0, text.Length, true) : Scan(text, options, 0, text.Length, false);
        }

        return hit;
    }

    public static int CountMatches(string text, FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return AllMatches(text, options).Count();
    }

    public static IEnumerable<int> AllMatches(string text, FindOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Query))
        {
            yield break;
        }

        var i = 0;
        while (i <= text.Length - options.Query.Length)
        {
            var hit = Scan(text, options, i, text.Length, true);
            if (hit < 0)
            {
                yield break;
            }

            yield return hit;
            i = hit + options.Query.Length;
        }
    }

    public static (string Text, int Count) ReplaceAll(string text, FindOptions options, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        var sb = new StringBuilder(text.Length);
        var last = 0;
        var count = 0;
        foreach (var hit in AllMatches(text, options))
        {
            sb.Append(text, last, hit - last).Append(replacement);
            last = hit + options.Query.Length;
            count++;
        }

        sb.Append(text, last, text.Length - last);
        return (count == 0 ? text : sb.ToString(), count);
    }

    public static bool IsMatchAt(string text, FindOptions options, int index)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(options);
        var q = options.Query;
        if (string.IsNullOrEmpty(q) || index < 0 || index + q.Length > text.Length)
        {
            return false;
        }

        var cmp = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Compare(text, index, q, 0, q.Length, cmp) == 0 && (!options.WholeWord || IsWholeWord(text, index, q.Length));
    }

    private static int Scan(string text, FindOptions o, int from, int to, bool forward)
    {
        var cmp = o.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var q = o.Query;
        if (forward)
        {
            var i = from;
            while (i <= to - q.Length)
            {
                var hit = text.IndexOf(q, i, to - i, cmp);
                if (hit < 0)
                {
                    return -1;
                }

                if (!o.WholeWord || IsWholeWord(text, hit, q.Length))
                {
                    return hit;
                }

                i = hit + 1;
            }

            return -1;
        }

        // Backward: last match that starts before 'to'.
        var pos = to - 1;
        while (pos >= from)
        {
            var startIndex = Math.Min(pos + q.Length - 1, text.Length - 1);
            var count = startIndex - from + 1;
            if (count < q.Length)
            {
                return -1;
            }

            var hit = text.LastIndexOf(q, startIndex, count, cmp);
            if (hit < 0)
            {
                return -1;
            }

            if (!o.WholeWord || IsWholeWord(text, hit, q.Length))
            {
                return hit;
            }

            pos = hit - 1;
        }

        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsWholeWord(string text, int index, int length) =>
        (index == 0 || !IsWordChar(text[index - 1])) && (index + length >= text.Length || !IsWordChar(text[index + length]));

    // ---------- Lines ----------

    /// <summary>Full-line span covered by a selection. A selection ending at column 0 doesn't include that line.</summary>
    public static (int Start, int End) LineSpan(string text, int selStart, int selLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        selStart = Math.Clamp(selStart, 0, text.Length);
        var selEnd = Math.Clamp(selStart + Math.Max(0, selLength), 0, text.Length);
        if (selLength > 0 && selEnd > selStart && text[selEnd - 1] == '\n')
        {
            selEnd--;
        }

        var start = selStart == 0 ? 0 : text.LastIndexOf('\n', selStart - 1) + 1;
        var end = text.IndexOf('\n', selEnd);
        return (start, end < 0 ? text.Length : end);
    }

    public static TextEdit DuplicateLines(string text, int selStart, int selLength)
    {
        var (start, end) = LineSpan(text, selStart, selLength);
        var block = text[start..end];
        var shift = block.Length + 1;
        return new TextEdit(end, 0, "\n" + block, selStart + shift, selLength);
    }

    public static TextEdit? MoveLines(string text, int selStart, int selLength, bool up)
    {
        var (start, end) = LineSpan(text, selStart, selLength);
        var block = text[start..end];
        if (up)
        {
            if (start == 0)
            {
                return null;
            }

            var prevStart = start - 1 == 0 ? 0 : text.LastIndexOf('\n', start - 2) + 1;
            var prev = text[prevStart..(start - 1)];
            return new TextEdit(prevStart, end - prevStart, block + "\n" + prev, selStart - (prev.Length + 1), selLength);
        }

        if (end >= text.Length)
        {
            return null;
        }

        var nextEnd = text.IndexOf('\n', end + 1);
        if (nextEnd < 0)
        {
            nextEnd = text.Length;
        }

        var next = text[(end + 1)..nextEnd];
        return new TextEdit(start, nextEnd - start, next + "\n" + block, selStart + next.Length + 1, selLength);
    }

    /// <summary>Index of the first character of a 1-based line number (clamped to the document).</summary>
    public static int LineStartIndex(string text, int lineNumber)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (lineNumber <= 1)
        {
            return 0;
        }

        var line = 1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && ++line == lineNumber)
            {
                return i + 1;
            }
        }

        return text.LastIndexOf('\n') + 1;
    }

    public static int LineCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.AsSpan().Count('\n') + 1;
    }

    public static int WordCount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }

        return count;
    }

    // ---------- Markdown helpers ----------

    /// <summary>Wraps the selection (or a placeholder) in prefix/suffix; unwraps if it's already wrapped.</summary>
    public static TextEdit Wrap(string text, int selStart, int selLength, string prefix, string suffix, string placeholder)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        var selEnd = selStart + selLength;
        if (selLength > 0
            && selStart >= prefix.Length
            && selEnd + suffix.Length <= text.Length
            && string.CompareOrdinal(text, selStart - prefix.Length, prefix, 0, prefix.Length) == 0
            && string.CompareOrdinal(text, selEnd, suffix, 0, suffix.Length) == 0)
        {
            var inner = text.Substring(selStart, selLength);
            return new TextEdit(selStart - prefix.Length, selLength + prefix.Length + suffix.Length, inner, selStart - prefix.Length, selLength);
        }

        var content = selLength > 0 ? text.Substring(selStart, selLength) : placeholder;
        return new TextEdit(selStart, selLength, prefix + content + suffix, selStart + prefix.Length, content.Length);
    }

    public static TextEdit InsertLink(string text, int selStart, int selLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string url = "https://";
        if (selLength > 0)
        {
            var label = text.Substring(selStart, selLength);
            var replacement = $"[{label}]({url})";
            return new TextEdit(selStart, selLength, replacement, selStart + label.Length + 3, url.Length);
        }

        const string placeholder = "link text";
        return new TextEdit(selStart, 0, $"[{placeholder}]({url})", selStart + 1, placeholder.Length);
    }

    public static TextEdit InsertCodeBlock(string text, int selStart, int selLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        var body = selLength > 0 ? text.Substring(selStart, selLength) : "code";
        var lead = selStart > 0 && text[selStart - 1] != '\n' ? "\n" : "";
        var replacement = $"{lead}```\n{body}\n```\n";
        return new TextEdit(selStart, selLength, replacement, selStart + lead.Length + 4, body.Length);
    }

    public static TextEdit InsertBlock(string text, int caret, string block)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(block);
        caret = Math.Clamp(caret, 0, text.Length);
        // Keep a blank line between the block and whatever comes before it.
        var lead = caret > 0 && text[caret - 1] != '\n' ? "\n\n" : (caret > 1 && text[caret - 2] != '\n' ? "\n" : "");
        var replacement = lead + block + "\n";
        return new TextEdit(caret, 0, replacement, caret + replacement.Length, 0);
    }

    public const string TableTemplate = "| Column 1 | Column 2 |\n| -------- | -------- |\n|          |          |";

    /// <summary>Sets (or toggles off) a heading level on every selected line.</summary>
    public static TextEdit SetHeading(string text, int selStart, int selLength, int level)
    {
        level = Math.Clamp(level, 1, 6);
        var marker = new string('#', level) + " ";
        return TransformLines(text, selStart, selLength, lines =>
        {
            var allAtLevel = lines.Where(l => l.Length > 0).All(l => HeadingRegex().Match(l) is { Success: true } m && m.Groups[1].Length == level);
            return lines.Select(l =>
            {
                var stripped = HeadingRegex().Replace(l, "", 1);
                return allAtLevel || l.Length == 0 ? stripped : marker + stripped;
            }).ToList();
        });
    }

    /// <summary>Applies (or toggles off) a list / quote marker on every selected line.</summary>
    public static TextEdit ToggleList(string text, int selStart, int selLength, ListKind kind)
    {
        return TransformLines(text, selStart, selLength, lines =>
        {
            var nonEmpty = lines.Where(l => l.Trim().Length > 0).ToList();
            var all = nonEmpty.Count > 0 && nonEmpty.All(l => MarkerRegex(kind).IsMatch(l));
            var n = 0;
            return lines.Select(l =>
            {
                if (l.Trim().Length == 0)
                {
                    return l;
                }

                var indentLength = l.Length - l.TrimStart().Length;
                var indent = l[..indentLength];
                var body = AnyListMarkerRegex().Replace(l[indentLength..], "", 1);
                if (all)
                {
                    return indent + body;
                }

                n++;
                var marker = kind switch
                {
                    ListKind.Bullet => "- ",
                    ListKind.Numbered => n.ToString(CultureInfo.InvariantCulture) + ". ",
                    ListKind.Task => "- [ ] ",
                    _ => "> ",
                };
                return indent + marker + body;
            }).ToList();
        });
    }

    private static TextEdit TransformLines(string text, int selStart, int selLength, Func<List<string>, List<string>> transform)
    {
        ArgumentNullException.ThrowIfNull(text);
        var (start, end) = LineSpan(text, selStart, selLength);
        var lines = text[start..end].Split('\n').ToList();
        var replacement = string.Join('\n', transform(lines));
        return new TextEdit(start, end - start, replacement, start, replacement.Length);
    }

    private static Regex MarkerRegex(ListKind kind) => kind switch
    {
        ListKind.Bullet => BulletRegex(),
        ListKind.Numbered => NumberedRegex(),
        ListKind.Task => TaskRegex(),
        _ => QuoteRegex(),
    };

    [GeneratedRegex(@"^(#{1,6}) ")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s*[-*+] (?!\[[ xX]\] )")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s*\d+[.)] ")]
    private static partial Regex NumberedRegex();

    [GeneratedRegex(@"^\s*[-*+] \[[ xX]\] ")]
    private static partial Regex TaskRegex();

    [GeneratedRegex(@"^\s*> ")]
    private static partial Regex QuoteRegex();

    [GeneratedRegex(@"^([-*+] \[[ xX]\] |[-*+] |\d+[.)] |> )")]
    private static partial Regex AnyListMarkerRegex();
}
