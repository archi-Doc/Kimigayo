// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace XunitTest;

public class CodeRulesDensityTest
{
    [Theory]
    [InlineData(160, 0)]
    [InlineData(161, 1)]
    public async Task PhysicalLinesCountCommentsAndUtf16(int length, int count)
    {
        var line = "//" + new string('x', length - 4) + "😀";
        var diagnostics = await Analyze(line + "\r\nclass C { }\r\n");
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR3001", x.Id));
    }

    [Theory]
    [InlineData("@\"\n", "\n\"", 0)]
    [InlineData("\"\"\"\n", "\n\"\"\"", 0)]
    [InlineData("$\"\"\"\n", "\n\"\"\"", 0)]
    [InlineData("$@\"\n", "\n\"", 0)]
    [InlineData("\"", "\"", 1)]
    public async Task OnlyWholeMultilineLiteralLinesAreExcluded(string start, string end, int count)
    {
        var diagnostics = await Analyze("class C { string text = " + start + new string('x', 170) + end + "; }");
        Assert.Equal(count, diagnostics.Length);
    }

    [Fact]
    public async Task InterpolationCodeAndMixedLiteralLinesRemainVisible()
    {
        var comment = "//" + new string('x', 170);
        var code = "class C { string text = $$\"\"\"\n{{\n1 " + comment + "\n}}\n\"\"\"; }";
        Assert.Single(await Analyze(code));
        Assert.Single(await Analyze("class C { string text = @\"" + new string('x', 170) + "\n\"; }"));
    }

    [Fact]
    public async Task LineScopesFollowThePositionAndHashesIgnoreLineEndings()
    {
        var comment = "//" + new string('x', 170);
        var text = comment + "\nclass C\n{\n" + comment + "\n}\n";
        var observations = (await CodeRulesTestHelper.Analyze(text, CodeRulesTestHelper.Options("Report")))
            .Where(x => x.Id == "KR9001" && x.Properties["rule"] == "KR3001").ToArray();
        Assert.Equal(2, observations.Length);
        Assert.Contains(observations, x => x.Properties["scope"] == CodeRulesTestHelper.SourcePath);
        Assert.Contains(observations, x => x.Properties["scope"] == "T:C");
        Assert.Equal(observations[0].Properties["anchor"], observations[1].Properties["anchor"]);
        var baseline = string.Join('\n', observations.Select(x => string.Join('\t', x.Properties["rule"], x.Properties["scope"], x.Properties["anchor"], "1", x.Properties["path"])));
        var verified = await CodeRulesTestHelper.Analyze(text.Replace("\n", "\r\n", StringComparison.Ordinal), CodeRulesTestHelper.Options("Verify", baseline));
        Assert.Equal("KR9000", Assert.Single(verified).Id);
    }

    [Theory]
    [InlineData("a ? 1 : 2", 0)]
    [InlineData("a ? 1 : b ? 2 : 3", 1)]
    [InlineData("(a ? b : c) ? 1 : 2", 1)]
    [InlineData("a ? (b ? 1 : 2) : (c ? 3 : 4)", 1)]
    [InlineData("a ? F(() => b ? 1 : 2) : 3", 0)]
    [InlineData("a ? F(() => b ? 1 : c ? 2 : 3) : 4", 1)]
    [InlineData("a ? F(delegate { return b ? 1 : 2; }) : 3", 0)]
    [InlineData("a ? F(() => { int Local() => b ? 1 : c ? 2 : 3; return Local(); }) : 4", 1)]
    public async Task NestedConditionalsBelongToTheOutermostCallableExpression(string expression, int count)
    {
        var source = "class C { int M(bool a, bool b, bool c) => " + expression + "; int F(System.Func<int> f) => f(); }";
        var diagnostics = await Analyze(source, wideLines: true);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR3002", x.Id));
    }

    [Theory]
    [InlineData("var (a,b) = (F(), 1);", 1)]
    [InlineData("var (a,b) = (1, F());", 1)]
    [InlineData("var (a,b) = (0, xs.Length);", 0)]
    [InlineData("var (a,b) = (this.Value, default(int));", 0)]
    [InlineData("var (a,b) = (xs[0], 1);", 1)]
    [InlineData("var (a,b) = (xs?.Length, 1);", 1)]
    [InlineData("int a=0,b=0; (a,b) = (F(), F());", 0)]
    [InlineData("var (_,b) = (F(), 1);", 0)]
    [InlineData("var ((a,b),c) = ((F(), 1), 2);", 0)]
    [InlineData("var (a,b) = Pair();", 0)]
    [InlineData("var (a,b) = ((0), xs.Length);", 0)]
    [InlineData("var (a,b) = ((F(), 1));", 1)]
    [InlineData("var (a,b) = (((xs).Length), 1);", 0)]
    [InlineData("(var a, var b) = (F(), 1);", 1)]
    [InlineData("(int a, var b) = (1, F());", 1)]
    [InlineData("(var a, var b) = (0, xs.Length);", 0)]
    [InlineData("int a=0; (a, var b) = (F(), 1);", 0)]
    [InlineData("(var a, var _) = (F(), 1);", 0)]
    [InlineData("(var a, (var b, var c)) = (F(), (1, 2));", 0)]
    public async Task ComputedDeclarationsExcludeExistingWritesAndNonflatBindings(string statement, int count)
    {
        var diagnostics = await Analyze("class C { void M(int[] xs) { " + statement + " } int F() => 1; (int,int) Pair() => (1,2); int Value => 0; }", wideLines: true);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR3003", x.Id));
    }

    [Theory]
    [InlineData("src/Kimi/Compiler/Analysis/Fixture.cs")]
    [InlineData("src/Kimi/Compiler/Emission/Fixture.cs")]
    public async Task FrozenPathsDoNotAcquireDensityRules(string path)
    {
        var compilation = CodeRulesTestHelper.Compilation(("class C { void M() { var (a,b) = (F(), 1); } }\n//" + new string('x', 170), path));
        Assert.Empty(await CodeRulesTestHelper.Analyze(compilation));
    }

    [Fact]
    public async Task SameConditionalKeyCountsDuplicatesAndRejectsGrowth()
    {
        const string expression = "a ? 1 : b ? 2 : 3";
        const string source = "partial class C { int M(bool a, bool b) => " + expression + "; }";
        var report = await CodeRulesTestHelper.Analyze(source, Options("Report"));
        var observed = Assert.Single(report, x => x.Id == "KR9001");
        var baseline = string.Join('\t', "KR3002", observed.Properties["scope"], observed.Properties["anchor"], "1", observed.Properties["path"]);
        Assert.Equal("KR9000", Assert.Single(await CodeRulesTestHelper.Analyze(source, Options("Verify", baseline))).Id);
        var compilation = CodeRulesTestHelper.Compilation(source, source.Replace("M(", "N(", StringComparison.Ordinal));
        Assert.Equal(2, (await CodeRulesTestHelper.Analyze(compilation, Options(baseline: baseline))).Length);
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, bool wideLines = false)
    {
        var options = Options(wideLines: wideLines);
        var diagnostics = await CodeRulesTestHelper.Analyze(source, options);
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001");
        return diagnostics.Where(x => x.Id.StartsWith("KR3", StringComparison.Ordinal)).ToImmutableArray();
    }

    private static Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions Options(string mode = "Enforce", string baseline = "", bool wideLines = false)
        => CodeRulesTestHelper.Options(mode, baseline, config =>
        {
            config["kimi_code_rules.scope.accretion.exclude"] = "src/Kimi/";
            config["kimi_code_rules.max_line_length"] = wideLines ? "1000" : "160";
        });
}
