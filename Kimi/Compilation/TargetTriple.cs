// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimigayo.Compilation;

public record class TargetTriple(string Architecture, string Vendor, string OperatingSystem, string Environment, string Abi)
{
    // x86_64-pc-windows-msvc
    public static readonly TargetTriple Empty = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    public override string ToString()
    {
        if (this.Abi.Length > 0)
        {
            return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}-{this.Environment}-{this.Abi}";
        }

        if (this.Environment.Length > 0)
        {
            return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}-{this.Environment}";
        }

        return $"{this.Architecture}-{this.Vendor}-{this.OperatingSystem}";
    }
}

public static class TargetTripleParser
{
    public static bool TryParse(ReadOnlySpan<char> text, out TargetTriple triple)
    {
        triple = TargetTriple.Empty;
        if (text.IsEmpty)
        {
            return false;
        }

        Span<Range> parts = stackalloc Range[5];
        var count = SplitByHyphen(text, parts);
        if (count < 3)
        {
            return false;
        }

        var architecture = text[parts[0]].ToString();
        var vendor = text[parts[1]].ToString();
        var operatingSystem = text[parts[2]].ToString();
        var environment = string.Empty;
        var abi = string.Empty;

        if (count >= 4)
        {
            var fourth = text[parts[3]];

            if (IsKnownAbi(fourth))
            {
                abi = fourth.ToString();
            }
            else
            {
                environment = fourth.ToString();
            }
        }

        if (count >= 5)
        {
            abi = text[parts[4]].ToString();
        }

        triple = new TargetTriple(architecture, vendor, operatingSystem, environment, abi);
        return true;
    }

    private static int SplitByHyphen(ReadOnlySpan<char> text, Span<Range> parts)
    {
        var count = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '-')
            {
                continue;
            }

            if (i == start)
            {
                return 0; // Empty component.
            }

            if ((uint)count >= (uint)parts.Length)
            {
                return 0; // Too many components.
            }

            parts[count++] = start..i;
            start = i + 1;
        }

        if (start >= text.Length)
        {
            return 0; // Ends with '-'.
        }

        if ((uint)count >= (uint)parts.Length)
        {
            return 0;
        }

        parts[count++] = start..text.Length;
        return count;
    }

    private static bool IsKnownAbi(ReadOnlySpan<char> text)
    {
        return text.Equals("eabi", StringComparison.Ordinal) ||
            text.Equals("eabihf", StringComparison.Ordinal) ||
            text.Equals("android", StringComparison.Ordinal) ||
            text.Equals("gnuabi64", StringComparison.Ordinal) ||
            text.Equals("gnueabi", StringComparison.Ordinal) ||
            text.Equals("gnueabihf", StringComparison.Ordinal) ||
            text.Equals("ilp32", StringComparison.Ordinal) ||
            text.Equals("musleabi", StringComparison.Ordinal) ||
            text.Equals("musleabihf", StringComparison.Ordinal);
    }
}
