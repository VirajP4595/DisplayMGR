namespace DisplayMGR.Monitors;

/// <summary>What a monitor says it supports in its DDC/CI capabilities string.</summary>
public sealed class MonitorCapabilities
{
    public string Model { get; init; } = "";
    public string MccsVersion { get; init; } = "";

    /// <summary>
    /// Supported VCP codes. The list holds the permitted values for a non-continuous
    /// code and is empty when the monitor lists none (continuous codes).
    /// </summary>
    public IReadOnlyDictionary<byte, IReadOnlyList<byte>> Vcp { get; init; } =
        new Dictionary<byte, IReadOnlyList<byte>>();

    public bool Supports(byte code) => Vcp.ContainsKey(code);

    public IReadOnlyList<byte> ValuesFor(byte code) =>
        Vcp.TryGetValue(code, out var values) ? values : Array.Empty<byte>();
}

/// <summary>
/// Parses MCCS capabilities strings such as
/// <c>(prot(monitor)type(lcd)model(X)vcp(10 12 60(0F 11 12))mccs_ver(2.1))</c>.
/// Monitors are sloppy about this format, so the parser never throws.
/// </summary>
public static class CapabilitiesParser
{
    public static MonitorCapabilities Parse(string? capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
            return new MonitorCapabilities();

        return new MonitorCapabilities
        {
            Model = (FindGroup(capabilities, "model") ?? "").Trim(),
            MccsVersion = (FindGroup(capabilities, "mccs_ver") ?? "").Trim(),
            Vcp = ParseVcp(FindGroup(capabilities, "vcp") ?? ""),
        };
    }

    /// <summary>Returns the text inside <c>name( ... )</c>, honouring nested parentheses.</summary>
    private static string? FindGroup(string text, string name)
    {
        int search = 0;
        while (search < text.Length)
        {
            int at = text.IndexOf(name, search, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return null;

            // Must be a whole token: "vcp(" must not match inside "vcpname(".
            bool startsToken = at == 0 || !(char.IsLetterOrDigit(text[at - 1]) || text[at - 1] == '_');
            int open = at + name.Length;
            while (open < text.Length && text[open] == ' ')
                open++;

            if (startsToken && open < text.Length && text[open] == '(')
            {
                int depth = 0;
                for (int i = open; i < text.Length; i++)
                {
                    if (text[i] == '(') depth++;
                    else if (text[i] == ')' && --depth == 0)
                        return text.Substring(open + 1, i - open - 1);
                }
                // Unbalanced (truncated reply): take what is there.
                return text[(open + 1)..];
            }
            search = at + name.Length;
        }
        return null;
    }

    private static Dictionary<byte, IReadOnlyList<byte>> ParseVcp(string body)
    {
        var result = new Dictionary<byte, IReadOnlyList<byte>>();
        int i = 0;
        byte? last = null;

        while (i < body.Length)
        {
            char c = body[i];
            if (c == '(')
            {
                int end = MatchingParen(body, i);
                if (last is byte code)
                    result[code] = ParseValues(body.Substring(i + 1, end - i - 1));
                last = null;
                i = end + 1;
            }
            else if (Uri.IsHexDigit(c))
            {
                int start = i;
                while (i < body.Length && Uri.IsHexDigit(body[i]))
                    i++;
                // Some monitors omit the spaces: "101214".
                foreach (byte code in SplitHex(body.AsSpan(start, i - start)))
                {
                    result.TryAdd(code, Array.Empty<byte>());
                    last = code;
                }
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    private static List<byte> ParseValues(string body)
    {
        var values = new List<byte>();
        int i = 0;
        while (i < body.Length)
        {
            if (body[i] == '(')
            {
                i = MatchingParen(body, i) + 1; // nested detail lists are not used
            }
            else if (Uri.IsHexDigit(body[i]))
            {
                int start = i;
                while (i < body.Length && Uri.IsHexDigit(body[i]))
                    i++;
                foreach (byte v in SplitHex(body.AsSpan(start, i - start)))
                    if (!values.Contains(v))
                        values.Add(v);
            }
            else
            {
                i++;
            }
        }
        return values;
    }

    private static IEnumerable<byte> SplitHex(ReadOnlySpan<char> token)
    {
        var bytes = new List<byte>();
        if (token.Length <= 2)
        {
            bytes.Add(Convert.ToByte(token.ToString(), 16));
            return bytes;
        }
        for (int i = 0; i + 1 < token.Length; i += 2)
            bytes.Add(Convert.ToByte(token.Slice(i, 2).ToString(), 16));
        return bytes;
    }

    /// <summary>Index of the parenthesis closing the one at <paramref name="open"/>, or the text length if the reply was cut short.</summary>
    private static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
                return i;
        }
        return text.Length;
    }
}
