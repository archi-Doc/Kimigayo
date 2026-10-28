// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace XunitTest;

// DECISIONS R5-M85: the codegen deviation catalog is well formed and every entry is used, so M7 counts decided deviations only.
public class CodegenDeviationTest
{
    [Fact]
    public void EveryDeviationNamesItsDecisionAndClauseAndIsUsed()
    {
        var decisions = Repository.ReadText("docs", "dev", "DECISIONS.md");
        var root = Path.Combine(Repository.Root, "tests", "xUnitTest");
        var sources = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(x => Path.GetRelativePath(root, x).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj") && Path.GetFileName(x) != "Codegen.cs")
            .Select(File.ReadAllText)
            .ToArray();
        var fields = typeof(CodegenDeviations).GetFields(BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotEmpty(fields);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var entry = Assert.IsType<Deviation>(field.GetValue(null));
            Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", entry.Id);
            Assert.True(ids.Add(entry.Id), entry.Id);
            Assert.True(Enum.IsDefined(entry.Class), entry.Id);
            Assert.Matches($@"(?m)^- \*\*{Regex.Escape(entry.Item)}[* ]", decisions);
            Assert.All(entry.Clause.Split(' '), x => Assert.Contains(x, DiagnosticCorpus.SpecSections));
            Assert.NotEqual(string.Empty, entry.Reason.Trim());
            Assert.Contains(sources, x => Regex.IsMatch(
                x, $@"\bCodegen\.Expect(<[^>]*>)?\([^;]*\bCodegenDeviations\.{field.Name}\)|\bnew LegacyNative\(CodegenDeviations\.{field.Name},"));
        }
    }

    // DECISIONS R5-M85, R5-M145: a deviation's target and legacy value differ by content, an audit order's array included, so Expect fails
    // on equal ones whatever their identity and accepts different ones (fail-before: arrays compared by reference never failed).
    [Fact]
    public void ExpectRefusesATargetEqualToItsLegacyValue()
    {
        var deviation = CodegenDeviations.ConstantTemporaries;
        Assert.Throws<FalseException>(() => Codegen.Expect<int[]>([1, 0, 2], [1, 0, 2], deviation));
        Assert.Throws<FalseException>(() => Codegen.Expect("a=1", "a=1", deviation));
        Assert.Equal(Codegen.Legacy ? [1, 0, 2] : [1, 0], Codegen.Expect<int[]>([1, 0], [1, 0, 2], deviation));
    }

    // DECISIONS R5-M85, R5-M106: a deviating fixture's .deviation sidecar is the same bytes in both runs: UTF-8 without a byte order mark,
    // LF line ends, the entry's fields and both outcomes in a fixed key order.
    [Fact]
    public void ADeviationSidecarIsTheSameBytesInEveryRun()
    {
        var entry = CodegenDeviations.StaticCycle;
        var sidecar = Codegen.Sidecar(new LegacyNative(entry, "old\n", 1, "legacy\n"), "new\n", 2, "target\n");
        var expected = $$"""
            {
              "id": "static-cycle",
              "class": "a",
              "item": "R5-M106",
              "clause": "22.4.3 22.5.1 22.5.2",
              "reason": {{System.Text.Json.JsonSerializer.Serialize(entry.Reason)}},
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
        Assert.Equal(sidecar, Codegen.Sidecar(new LegacyNative(entry, "old\n", 1, "legacy\n"), "new\n", 2, "target\n"));
    }
}
