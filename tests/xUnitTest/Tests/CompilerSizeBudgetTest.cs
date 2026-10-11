// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>Line budgets of src/Kimi/Compiler per area (docs/dev/COMPILER_ARCHITECTURE.md). A line is a newline character,
/// as counted by <c>wc -l</c>, so comments and blank lines count. The caps fall when a reduction stage exits.</summary>
public class CompilerSizeBudgetTest
{
    // R2a exit (2026-10-11): an area may grow by at most 2% over its size at the R2a exit (Core, which grew in R2a,
    // keeps its lower R1 cap). The parallel stages R2b-R5 (plan section 10.4) check their cumulative ceiling as the total.
    // R4 (ceiling 142,000 from the R3 exit at 133,004; R4 plan M14) builds the MIR analyses in the Mir area (cap 11,500),
    // gives Binding 500 lines over its R3-exit size (56,569) for the declaration views and Core 1,830 for the analysis
    // hook; the frozen Analysis and Emission paths (D5) and every other area keep their caps.
    private const int TotalCap = 142_000;

    private static readonly Dictionary<string, int> Caps = new(StringComparer.Ordinal)
    {
        ["Analysis"] = 17_942,
        ["Binding"] = 57_069,
        ["Core"] = 1_830,
        ["Documentation"] = 4_424,
        ["Emission"] = 21_320,
        ["Helper"] = 3_505,
        ["Lexing"] = 4_047,
        ["LlvmTemplates"] = 2_381,
        ["Mir"] = 11_500,
        ["Parsing"] = 16_570,
        ["Root"] = 1_064,
    };

    public static TheoryData<string> Areas
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var area in Caps.Keys)
            {
                data.Add(area);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void AreaStaysWithinItsCap(string area)
    {
        var lines = Lines().GetValueOrDefault(area);
        Assert.True(lines <= Caps[area], $"{area} has {lines} lines; its cap is {Caps[area]}.");
    }

    [Fact]
    public void EveryAreaHasACapAndTheTotalStaysWithinTheStageCap()
    {
        var lines = Lines();
        Assert.Empty(lines.Keys.Except(Caps.Keys)); // A capped area may not exist yet (Mir before its first file).
        var total = lines.Values.Sum();
        Assert.True(total <= TotalCap, $"The compiler has {total} lines; the stage cap is {TotalCap}.");
    }

    // C# files count toward their first directory below src/Kimi/Compiler (Root for files directly in it), and LLVM IR
    // templates toward LlvmTemplates.
    private static Dictionary<string, int> Lines()
    {
        var root = Path.Combine(Repository.Root, "src", "Kimi", "Compiler");
        var lines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string area;
            if (file.EndsWith(".ll.in", StringComparison.Ordinal))
            {
                area = "LlvmTemplates";
            }
            else if (file.EndsWith(".cs", StringComparison.Ordinal))
            {
                var relative = Path.GetRelativePath(root, file);
                var separator = relative.IndexOfAny(['/', '\\']);
                area = separator < 0 ? "Root" : relative[..separator];
            }
            else
            {
                continue;
            }

            lines[area] = lines.GetValueOrDefault(area) + File.ReadAllBytes(file).Count(x => x == (byte)'\n');
        }

        return lines;
    }
}
