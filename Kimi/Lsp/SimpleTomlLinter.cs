// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Lsp;

internal static class SimpleTomlLinter
{
    public static List<TomlDiagnostic> Lint(IReadOnlyList<string> lines)
    {
        var diagnostics = new List<TomlDiagnostic>();
        var keysBySection = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        var section = string.Empty;
        keysBySection[section] = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            if (trimmed.StartsWith('['))
            {
                if (!TryParseTableHeader(trimmed, out var tableName, out var isArrayTable))
                {
                    diagnostics.Add(new TomlDiagnostic(
                        i,
                        FirstNonWhiteSpace(line),
                        Math.Max(1, line.Length - FirstNonWhiteSpace(line)),
                        "error",
                        "Invalid table header syntax."));

                    continue;
                }

                section = tableName;

                if (isArrayTable)
                {
                    keysBySection[section] = new HashSet<string>(StringComparer.Ordinal);
                    continue;
                }

                if (!keysBySection.TryAdd(section, new HashSet<string>(StringComparer.Ordinal)))
                {
                    diagnostics.Add(new TomlDiagnostic(
                        i,
                        Math.Max(0, line.IndexOf('[', StringComparison.Ordinal)),
                        Math.Max(1, line.Length),
                        "warning",
                        $"Table '{section}' is already defined."));
                }

                continue;
            }

            var equalIndex = IndexOfUnquotedEqual(line);
            if (equalIndex < 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    FirstNonWhiteSpace(line),
                    Math.Max(1, line.Length - FirstNonWhiteSpace(line)),
                    "error",
                    "Expected a key-value pair. TOML entries must use 'key = value'."));

                continue;
            }

            var keyPart = line[..equalIndex].Trim();

            if (keyPart.Length == 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex,
                    1,
                    "error",
                    "Missing key before '='."));

                continue;
            }

            if (!IsValidBareOrDottedKey(keyPart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    FirstNonWhiteSpace(line),
                    keyPart.Length,
                    "error",
                    $"Invalid key '{keyPart}'. This simple linter supports bare keys and dotted keys."));
            }

            var valuePart = RemoveComment(line[(equalIndex + 1)..]).Trim();

            if (valuePart.Length == 0)
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex,
                    1,
                    "error",
                    "Missing value after '='."));

                continue;
            }

            if (!HasBalancedQuotes(valuePart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex + 1,
                    Math.Max(1, line.Length - equalIndex - 1),
                    "error",
                    "String literal is not closed."));
            }

            if (!HasBalancedBrackets(valuePart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    equalIndex + 1,
                    Math.Max(1, line.Length - equalIndex - 1),
                    "error",
                    "Array or inline table brackets are not balanced."));
            }

            if (!keysBySection.TryGetValue(section, out var keys))
            {
                keys = new HashSet<string>(StringComparer.Ordinal);
                keysBySection[section] = keys;
            }

            if (!keys.Add(keyPart))
            {
                diagnostics.Add(new TomlDiagnostic(
                    i,
                    Math.Max(0, line.IndexOf(keyPart, StringComparison.Ordinal)),
                    Math.Max(1, keyPart.Length),
                    "error",
                    $"Duplicate key '{keyPart}' in the current table."));
            }
        }

        return diagnostics;
    }

    private static bool TryParseTableHeader(string trimmed, out string name, out bool isArrayTable)
    {
        name = string.Empty;
        isArrayTable = false;

        var body = trimmed;

        var commentIndex = IndexOfUnquotedHash(body);
        if (commentIndex >= 0)
        {
            body = body[..commentIndex].TrimEnd();
        }

        if (body.StartsWith("[[", StringComparison.Ordinal) &&
            body.EndsWith("]]", StringComparison.Ordinal))
        {
            isArrayTable = true;
            name = body[2..^2].Trim();
            return IsValidBareOrDottedKey(name);
        }

        if (body.StartsWith('[') && body.EndsWith(']'))
        {
            name = body[1..^1].Trim();
            return IsValidBareOrDottedKey(name);
        }

        return false;
    }

    private static bool IsValidBareOrDottedKey(string key)
    {
        var parts = key.Split('.', StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (!IsBareKey(part))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBareKey(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfUnquotedEqual(string line)
    {
        var inString = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (c == '#' && !inString)
            {
                return -1;
            }

            if (c == '=' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static int IndexOfUnquotedHash(string line)
    {
        var inString = false;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (c == '#' && !inString)
            {
                return i;
            }
        }

        return -1;
    }

    private static string RemoveComment(string value)
    {
        var index = IndexOfUnquotedHash(value);
        return index < 0 ? value : value[..index];
    }

    private static bool HasBalancedQuotes(string value)
    {
        var inString = false;
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
            }
        }

        return !inString;
    }

    private static bool HasBalancedBrackets(string value)
    {
        var square = 0;
        var curly = 0;
        var inString = false;
        var escaped = false;

        foreach (var c in value)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            switch (c)
            {
                case '[':
                    square++;
                    break;
                case ']':
                    square--;
                    break;
                case '{':
                    curly++;
                    break;
                case '}':
                    curly--;
                    break;
            }

            if (square < 0 || curly < 0)
            {
                return false;
            }
        }

        return square == 0 && curly == 0;
    }

    private static int FirstNonWhiteSpace(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsWhiteSpace(line[i]))
            {
                return i;
            }
        }

        return 0;
    }
}
