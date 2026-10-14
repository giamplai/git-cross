using System.Text;

namespace GitCross.Core.Tools;

/// <summary>
/// A user-editable command line such as <c>collate --merge --base "$BASE" --left "$LOCAL" --right "$REMOTE" --output "$MERGED"</c>.
/// It is split into an executable and arguments and run WITHOUT a shell, so paths with spaces or special characters
/// can never be reinterpreted. Variables are substituted after splitting.
/// </summary>
public static class CommandTemplate
{
    public const string Base = "$BASE";
    public const string Local = "$LOCAL";
    public const string Remote = "$REMOTE";
    public const string Merged = "$MERGED";

    /// <summary>Splits on unquoted whitespace; single or double quotes group words and are removed; backslash is literal (Windows paths).</summary>
    public static List<string> Split(string template)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        char quote = '\0';
        var has = false;
        foreach (var c in template)
        {
            if (quote != '\0')
            {
                if (c == quote) { quote = '\0'; } else { sb.Append(c); }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
                has = true;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (has || sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); has = false; }
            }
            else
            {
                sb.Append(c);
            }
        }

        if (has || sb.Length > 0) { parts.Add(sb.ToString()); }
        return parts;
    }

    /// <summary>Executable and argument list with every variable replaced. Returns null for an empty template.</summary>
    public static (string Executable, List<string> Args)? Build(string template, IReadOnlyDictionary<string, string> vars)
    {
        var parts = Split(template);
        if (parts.Count == 0)
        {
            return null;
        }

        // Longest names first so $LOCAL2 style additions can never be clobbered by a shorter prefix.
        var names = vars.Keys.OrderByDescending(k => k.Length).ToList();
        string Sub(string s)
        {
            foreach (var n in names) { s = s.Replace(n, vars[n], StringComparison.Ordinal); }
            return s;
        }

        return (Sub(parts[0]), parts.Skip(1).Select(Sub).ToList());
    }

    /// <summary>Variables used by the template that the caller did not supply (typos like $MERGE).</summary>
    public static IReadOnlyList<string> UnknownVariables(string template, IEnumerable<string> known)
    {
        var set = known.ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '$') { continue; }
            var j = i + 1;
            while (j < template.Length && char.IsAsciiLetterUpper(template[j])) { j++; }
            if (j > i + 1 && !set.Contains(template[i..j]) && !result.Contains(template[i..j])) { result.Add(template[i..j]); }
        }

        return result;
    }
}
