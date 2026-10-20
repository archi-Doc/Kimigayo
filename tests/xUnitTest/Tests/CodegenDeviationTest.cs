// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

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
            Assert.Contains(sources, x => Regex.IsMatch(x, $@"\bCodegen\.Expect(<[^>]*>)?\([^;]*\bCodegenDeviations\.{field.Name}\)"));
        }
    }
}
