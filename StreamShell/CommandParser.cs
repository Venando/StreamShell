namespace StreamShell;

public static class CommandParser
{
    public static (string[] PositionalArgs, Dictionary<string, string> NamedArgs) Parse(string input)
    {
        var parts = Split(input);
        var positional = new List<string>();
        var named = new Dictionary<string, string>();

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].StartsWith("--"))
            {
                string key = parts[i][2..];
                string value = string.Empty;
                if (i + 1 < parts.Count && !parts[i + 1].StartsWith("--"))
                {
                    value = parts[i + 1];
                    i++;
                }
                named[key] = value;
            }
            else
            {
                positional.Add(parts[i]);
            }
        }

        return (positional.ToArray(), named);
    }

    internal static List<string> Split(string input)
    {
        var result = new List<string>();
        var span = input.AsSpan();
        int i = 0;

        while (i < span.Length)
        {
            while (i < span.Length && char.IsWhiteSpace(span[i]))
                i++;

            if (i >= span.Length)
                break;

            int start = i;
            while (i < span.Length && !char.IsWhiteSpace(span[i]))
                i++;

            result.Add(span[start..i].ToString());
        }

        return result;
    }
}
