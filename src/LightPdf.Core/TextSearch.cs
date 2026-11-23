using System.Text;

namespace LightPdf.Core;

public readonly record struct TextRange(int Start, int Length);

/// <summary>
/// Finds a query in page text. Any run of whitespace (including the line breaks PDFium inserts between
/// lines) matches any run of whitespace in the query, so phrases are found across line breaks.
/// </summary>
public static class TextSearch
{
    public static List<TextRange> FindAll(string text, string query, bool matchCase)
    {
        var results = new List<TextRange>();
        string needle = Collapse(query.Trim(), map: null);
        if (needle.Length == 0 || text.Length == 0) return results;

        var map = new List<int>(text.Length);
        string haystack = Collapse(text, map);
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        int from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            int hit = haystack.IndexOf(needle, from, comparison);
            if (hit < 0) break;
            int start = map[hit];
            int end = map[hit + needle.Length - 1];   // inclusive, in original text
            results.Add(new TextRange(start, end - start + 1));
            from = hit + needle.Length;                // non-overlapping
        }
        return results;
    }

    /// <summary>Collapses whitespace runs to one space; <paramref name="map"/> receives each output char's original index.</summary>
    private static string Collapse(string s, List<int>? map)
    {
        var sb = new StringBuilder(s.Length);
        bool inSpace = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || c == '\0')
            {
                if (inSpace) continue;
                inSpace = true;
                c = ' ';
            }
            else
            {
                inSpace = false;
            }
            sb.Append(c);
            map?.Add(i);
        }
        return sb.ToString();
    }
}
