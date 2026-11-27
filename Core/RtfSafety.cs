using System.Text;

namespace NicPad.Core;

/// <summary>
/// RTF can embed OLE objects ({\object ...}) that RichEdit may try to instantiate — a long-standing malware vector.
/// NicPad strips them before loading; text, formatting and pictures are kept.
/// </summary>
public static class RtfSafety
{
    private static readonly string[] DangerousGroups = ["object", "objdata", "objclass", "objemb", "objlink", "objautlink"];

    public static string StripEmbeddedObjects(string rtf, out int removed)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        removed = 0;
        var sb = new StringBuilder(rtf.Length);
        var i = 0;
        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '\\' && i + 1 < rtf.Length)
            {
                if (TrySkipBinary(rtf, i, out var afterBin))
                {
                    sb.Append(rtf, i, afterBin - i);
                    i = afterBin;
                    continue;
                }

                sb.Append(c).Append(rtf[i + 1]);
                i += 2;
                continue;
            }

            if (c == '{' && StartsDangerousGroup(rtf, i + 1))
            {
                i = FindGroupEnd(rtf, i) + 1;
                removed++;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return removed == 0 ? rtf : sb.ToString();
    }

    private static bool StartsDangerousGroup(string rtf, int j)
    {
        while (j < rtf.Length && char.IsWhiteSpace(rtf[j]))
        {
            j++;
        }

        if (j + 1 < rtf.Length && rtf[j] == '\\' && rtf[j + 1] == '*')
        {
            j += 2;
            while (j < rtf.Length && char.IsWhiteSpace(rtf[j]))
            {
                j++;
            }
        }

        if (j >= rtf.Length || rtf[j] != '\\')
        {
            return false;
        }

        var start = ++j;
        while (j < rtf.Length && char.IsAsciiLetter(rtf[j]))
        {
            j++;
        }

        var word = rtf[start..j];
        return DangerousGroups.Contains(word, StringComparer.Ordinal);
    }

    /// <summary>Index of the '}' closing the group that opens at <paramref name="open"/> (or the end of input).</summary>
    private static int FindGroupEnd(string rtf, int open)
    {
        var depth = 0;
        for (var i = open; i < rtf.Length; i++)
        {
            var c = rtf[i];
            if (c == '\\')
            {
                if (TrySkipBinary(rtf, i, out var afterBin))
                {
                    i = afterBin - 1;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return i;
            }
        }

        return rtf.Length - 1;
    }

    /// <summary>"\binN" is followed by N raw bytes that may contain braces; skip them as a unit.</summary>
    private static bool TrySkipBinary(string rtf, int i, out int after)
    {
        after = i;
        if (string.CompareOrdinal(rtf, i, "\\bin", 0, 4) != 0)
        {
            return false;
        }

        var j = i + 4;
        var digitsStart = j;
        while (j < rtf.Length && char.IsAsciiDigit(rtf[j]))
        {
            j++;
        }

        if (j == digitsStart || !long.TryParse(rtf.AsSpan(digitsStart, j - digitsStart), out var count))
        {
            return false;
        }

        if (j < rtf.Length && rtf[j] == ' ')
        {
            j++;
        }

        after = (int)Math.Min(rtf.Length, j + count);
        return true;
    }
}
