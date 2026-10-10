// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>Line budgets of src/Kimi/Compiler per area (docs/dev/COMPILER_ARCHITECTURE.md). A line is a newline character,
/// as counted by <c>wc -l</c>, so comments and blank lines count. The caps fall when a reduction stage exits.</summary>
public class CompilerSizeBudgetTest
{
    // R2a exit (2026-10-11): an area may grow by at most 2% over its size at the R2a exit (Core, which grew in R2a,
    // keeps its lower R1 cap). R2b (a parallel stage, plan section 10.4) checks the R2b ceiling as the total and lets
    // Binding, which builds the HIR columns and plans, take the whole stage room (55,155 + 2,389 lines).
    private const int TotalCap = 129_000;

    private static readonly Dictionary<string, int> Caps = new(StringComparer.Ordinal)
    {
        ["Analysis"] = 17_942,
        ["Binding"] = 57_544,
        ["Core"] = 1_614,
        ["Documentation"] = 4_424,
        ["Emission"] = 21_320,
        ["Helper"] = 3_505,
        ["Lexing"] = 4_047,
        ["LlvmTemplates"] = 2_381,
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
        Assert.Equal(Caps.Keys.Order(StringComparer.Ordinal), lines.Keys.Order(StringComparer.Ordinal));
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
