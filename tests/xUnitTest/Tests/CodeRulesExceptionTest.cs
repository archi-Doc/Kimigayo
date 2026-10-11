// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace XunitTest;

public class CodeRulesExceptionTest
{
    private static readonly string AttributeSource = "using System;\n" + File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/Misc/CodeRuleExceptionAttribute.cs"));

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(70)]
    public async Task ValidBoundIsAuditedIncludingZero(int count)
    {
        var source = "class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"Closed dispatch\")] void M(){" + new string(';', count) + "} }";
        Assert.Empty(await Analyze(source));
        var report = await Analyze(source, mode: "Report");
        var row = Assert.Single(report, x => x.Id == "KR9001");
        Assert.Equal(count.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Properties["value"]);
        Assert.Equal("exception", row.Properties["kind"]);
        Assert.Equal("Closed dispatch", row.Properties["reason"]);
        Assert.Equal("70", row.Properties["limit"]);
        Assert.Equal("false", row.Properties["baselineAllowed"]);
    }

    [Theory]
    [InlineData("\"KR2001\",Limit=70,Reason=\"x\"")]
    [InlineData("\"KR2006\",Limit=60,Reason=\"x\"")]
    [InlineData("\"KR2006\",Limit=70,Reason=\" \"")]
    [InlineData("\"KR2006\",Limit=70")]
    [InlineData("\"KR2006\",Reason=\"x\"")]
    [InlineData("\"unknown\",Limit=70,Reason=\"x\"")]
    public async Task InvalidAnnotationHasItsOwnDiagnostic(string arguments)
    {
        var diagnostic = Assert.Single(await Analyze("class C { [Kimi.CodeRuleException(" + arguments + ")] void M(){} }"));
        Assert.Equal("KR9005", diagnostic.Id);
        Assert.Contains(WellKnownDiagnosticTags.NotConfigurable, diagnostic.Descriptor.CustomTags);
    }

    [Fact]
    public async Task AboveBoundHasOneExceptionErrorEvenInReport()
    {
        var source = "class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")] void M(){" + new string(';', 71) + "} }";
        Assert.Equal("KR9005", Assert.Single(await Analyze(source)).Id);
        var report = await Analyze(source, mode: "Report");
        Assert.Single(report, x => x.Id == "KR9005");
        Assert.DoesNotContain(report, x => x.Id == "KR2006");
    }

    [Fact]
    public async Task DuplicateRuleAndUnsupportedOwnerAreRejected()
    {
        const string attribute = "[Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")]";
        Assert.Equal("KR9005", Assert.Single(await Analyze("class C { " + attribute + attribute + " void M(){} }")).Id);
        Assert.Equal("KR9005", Assert.Single(await Analyze(attribute + " class C {}", valid: false)).Id);
        Assert.Equal("KR9005", Assert.Single(await Analyze("class C { int P { " + attribute + " get { return 0; } } }")).Id);
    }

    [Fact]
    public async Task ConstantsAndAliasesUseTheOneAttributeResolution()
    {
        const string source = "using ExceptionRule=Kimi.CodeRuleExceptionAttribute; class C { const string Rule=\"KR\"+\"2006\"; " +
            "[ExceptionRule(rule:Rule,Limit=60+10,Reason=nameof(C))] void M(){} }";
        var row = Assert.Single(await Analyze(source, mode: "Report"), x => x.Id == "KR9001");
        Assert.Equal("C", row.Properties["reason"]);
    }

    [Fact]
    public async Task FieldsApplyTheSameAttributeToEveryVariable()
    {
        const string source = "class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")] " +
            "System.Action a=()=>{ ; ; },b=()=>{ ; }; }";
        var rows = (await Analyze(source, mode: "Report")).Where(x => x.Id == "KR9001").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, x => x.Properties["anchor"] == "member:F:C.a" && x.Properties["value"] == "2");
        Assert.Contains(rows, x => x.Properties["anchor"] == "member:F:C.b" && x.Properties["value"] == "1");
    }

    [Fact]
    public async Task LocalAndPropertyMeasurementsUseTheirCanonicalOwner()
    {
        const string source = "class C { void M(){ [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"local\")] void L(){;} } " +
            "[Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"property\")] int P { get { return 0; } set { ; } } }";
        var rows = (await Analyze(source, mode: "Report")).Where(x => x.Id == "KR9001").ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, x => x.Properties["anchor"] == "local:M:C.M/L" && x.Properties["value"] == "1");
        Assert.Contains(rows, x => x.Properties["anchor"] == "member:P:C.P" && x.Properties["value"] == "2");
    }

    [Theory]
    [InlineData(2)]
    [InlineData(65)]
    public async Task PartialSignatureAnnotationUsesImplementationMaximum(int statements)
    {
        var compilation = Compilation(
            "partial class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"partial\")] partial void M(); }",
            "partial class C { partial void M(){" + new string(';', statements) + "} }");
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, CodeRulesAccretionTest.Options(mode: "Report"));
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001" || x.Id == "KR9005");
        var row = Assert.Single(diagnostics, x => x.Id == "KR9001");
        Assert.Equal(statements.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Properties["value"]);
        Assert.Equal("partial", row.Properties["reason"]);
    }

    [Fact]
    public async Task DuplicateAcrossPartialDeclarationsAndBaselineOverlapAreRejected()
    {
        const string attribute = "[Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")]";
        var compilation = Compilation("partial class C { " + attribute + " partial void M(); }", "partial class C { " + attribute + " partial void M(){} }");
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, CodeRulesAccretionTest.Options());
        Assert.Equal("KR9005", Assert.Single(diagnostics).Id);
        var source = "class C { " + attribute + " void M(){} }";
        var baseline = "KR2006\tT:C\tmember:M:C.M\t61\t" + CodeRulesTestHelper.SourcePath;
        Assert.Equal("KR9005", Assert.Single(await Analyze(source, baseline: baseline)).Id);
    }

    [Fact]
    public async Task NestingExceptionUsesResetDepthAndItsOwnRule()
    {
        const string source = "class C { [Kimi.CodeRuleException(\"KR2007\",Limit=5,Reason=\"nested\")] void M(bool b){if(b){if(b){if(b){if(b){if(b){}}}}}} }";
        var row = Assert.Single(await Analyze(source, mode: "Report"), x => x.Id == "KR9001");
        Assert.Equal("KR2007", row.Properties["rule"]);
        Assert.Equal("5", row.Properties["value"]);
    }

    [Fact]
    public void ConditionalExceptionUsageIsNotEmitted()
    {
        var compilation = Compilation("class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")] void M(){} }");
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Single(x => metadata.GetString(x.Name) == "M");
        Assert.Empty(method.GetCustomAttributes());
    }

    private static CSharpCompilation Compilation(params string[] sources)
        => CodeRulesTestHelper.Compilation(sources.Concat([AttributeSource]).ToArray());

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string mode = "Enforce", string baseline = "", bool valid = true)
    {
        var compilation = Compilation(source);
        if (valid)
        {
            Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
        }

        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, CodeRulesAccretionTest.Options(mode: mode, baseline: baseline));
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001");
        return diagnostics;
    }
}
