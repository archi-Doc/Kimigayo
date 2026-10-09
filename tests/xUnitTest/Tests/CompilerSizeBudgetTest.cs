// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>Line budgets of src/Kimi/Compiler per area (docs/dev/COMPILER_ARCHITECTURE.md). A line is a newline character,
/// as counted by <c>wc -l</c>, so comments and blank lines count. The caps fall when a reduction stage exits.</summary>
public class CompilerSizeBudgetTest
{
    // Stage R0: an area may grow by at most 2% over its size at 419e8e79, and the total may not grow.
    private const int TotalCap = 136_926;

    private static readonly Dictionary<string, int> Caps = new(StringComparer.Ordinal)
    {
        ["Analysis"] = 21_026,
        ["Binding"] = 61_918,
        ["Core"] = 1_616,
        ["Documentation"] = 4_425,
        ["Emission"] = 21_277,
        ["Helper"] = 3_542,
        ["Lexing"] = 4_048,
        ["LlvmTemplates"] = 2_382,
        ["Parsing"] = 18_067,
        ["Root"] = 1_369,
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
