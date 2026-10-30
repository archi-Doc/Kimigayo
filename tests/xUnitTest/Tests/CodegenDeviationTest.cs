// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

// DECISIONS R5-M85: the codegen deviation catalog is well formed and every entry is used, so M7 counts decided deviations only. G3 reduced
// every entry to its target, so the catalog is empty and the form is checked on a synthetic sample until G4 deletes it (DECISIONS R5-M158).
public class CodegenDeviationTest
{
    private static readonly Deviation Sample = new(
        "sample-item", DeviationClass.A, "R5-M85", "22.4.3 22.5.1", "A synthetic entry of the form, which no expectation names (DECISIONS R5-M158).");

    [Fact]
    public void TheCatalogIsEmptyAndItsEntryRulesHold()
    {
        Assert.Empty(typeof(CodegenDeviations).GetFields(BindingFlags.Static | BindingFlags.NonPublic));
        var decisions = Repository.ReadText("docs", "dev", "DECISIONS.md");
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", Sample.Id);
        Assert.True(Enum.IsDefined(Sample.Class), Sample.Id);
        Assert.Matches($@"(?m)^- \*\*{Regex.Escape(Sample.Item)}[* ]", decisions);
        Assert.All(Sample.Clause.Split(' '), x => Assert.Contains(x, DiagnosticCorpus.SpecSections));
        Assert.NotEqual(string.Empty, Sample.Reason.Trim());

        // An entry is used where an Expect or a LegacyNative names it, with any type argument.
        Assert.True(Uses("Codegen.Expect<int[]>([1], [2], CodegenDeviations.Sample)", "Sample"));
        Assert.True(Uses("new LegacyNative(CodegenDeviations.Sample, string.Empty, 1, \"\")", "Sample"));
        Assert.False(Uses("Codegen.Expect(1, 2, CodegenDeviations.Other)", "Sample"));
    }

    // DECISIONS R5-M85, R5-M145: a deviation's target and legacy value differ by content, an audit order's array included, so Expect fails
    // on equal ones whatever their identity and accepts different ones (fail-before: arrays compared by reference never failed).
    [Fact]
    public void ExpectRefusesATargetEqualToItsLegacyValue()
    {
        Assert.Throws<FalseException>(() => Codegen.Expect<int[]>([1, 0, 2], [1, 0, 2], Sample));
        Assert.Throws<FalseException>(() => Codegen.Expect("a=1", "a=1", Sample));
        Assert.Equal(Codegen.Legacy ? [1, 0, 2] : [1, 0], Codegen.Expect<int[]>([1, 0], [1, 0, 2], Sample));
    }

    // DECISIONS R5-M85, R5-M106: a deviating fixture's .deviation sidecar is the same bytes in both runs: UTF-8 without a byte order mark,
    // LF line ends, the entry's fields and both outcomes in a fixed key order.
    [Fact]
    public void ADeviationSidecarIsTheSameBytesInEveryRun()
    {
        var sidecar = Codegen.Sidecar(new LegacyNative(Sample, "old\n", 1, "legacy\n"), "new\n", 2, "target\n");
        var expected = $$"""
            {
              "id": "sample-item",
              "class": "a",
              "item": "R5-M85",
              "clause": "22.4.3 22.5.1",
              "reason": {{System.Text.Json.JsonSerializer.Serialize(Sample.Reason)}},
              "legacy": {
                "stdout": "old\n",
                "exit": 1,
                "stderr": "legacy\n"
              },
              "target": {
                "stdout": "new\n",
                "exit": 2,
                "stderr": "target\n"
              }
            }

            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), System.Text.Encoding.UTF8.GetString(sidecar));
        Assert.NotEqual(0xEF, sidecar[0]);
        Assert.Equal(sidecar, Codegen.Sidecar(new LegacyNative(Sample, "old\n", 1, "legacy\n"), "new\n", 2, "target\n"));
    }

    // The use rule of the catalog check (DECISIONS R5-M85).
    private static bool Uses(string source, string field)
        => Regex.IsMatch(source, $@"\bCodegen\.Expect(<[^>]*>)?\([^;]*\bCodegenDeviations\.{field}\)|\bnew LegacyNative\(CodegenDeviations\.{field},");
}
