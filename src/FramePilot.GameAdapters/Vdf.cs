using System.Text;

namespace FramePilot.GameAdapters;

/// <summary>Minimal reader for Valve KeyValues text (libraryfolders.vdf, appmanifest_*.acf, cs2_video.txt).</summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
    public VdfNode? Child(string key) => Children.TryGetValue(key, out var c) ? c : null;
}

public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text).ToList();
        var pos = 0;
        var root = new VdfNode();
        ParseInto(root, tokens, ref pos, topLevel: true);
        return root;
    }

    private static void ParseInto(VdfNode node, List<string?> tokens, ref int pos, bool topLevel)
    {
        while (pos < tokens.Count)
        {
            var t = tokens[pos];
            if (t is null) // closing brace
            {
                pos++;
                if (!topLevel) return;
                continue;
            }
            if (t == "{")
            {
                pos++;
                continue;
            }

            var key = t;
            pos++;
            if (pos >= tokens.Count) return;
            var next = tokens[pos];
            if (next == "{")
            {
                pos++;
                var child = new VdfNode();
                ParseInto(child, tokens, ref pos, topLevel: false);
                node.Children[key] = child;
            }
            else if (next is null)
            {
                // key followed by a closing brace: malformed, ignore key
            }
            else
            {
                node.Values[key] = next;
                pos++;
            }
        }
    }

    /// <summary>Yields quoted/unquoted strings, "{" for opening and null for closing braces. Comments (//) are skipped.</summary>
    private static IEnumerable<string?> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '{') { i++; yield return "{"; continue; }
            if (c == '}') { i++; yield return null; continue; }
            if (c == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        var n = text[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                        i += 2;
                        continue;
                    }
                    sb.Append(text[i]);
                    i++;
                }
                i++; // closing quote
                yield return sb.ToString();
                continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"') i++;
            yield return text[start..i];
        }
    }
}
