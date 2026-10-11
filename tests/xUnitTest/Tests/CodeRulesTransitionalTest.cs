// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace XunitTest;

public class CodeRulesTransitionalTest
{
    private const string Attribute = "[Kimi.Transitional(\"R6\",Reason=\"Remove forwarding\")]";
    private static readonly string TransitionalSource = "using System;\n" + File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/Misc/TransitionalAttribute.cs"));
    private static readonly string ExceptionSource = "using System;\n" + File.ReadAllText(Path.Combine(Repository.Root, "src/Kimi/Misc/CodeRuleExceptionAttribute.cs"));

    [Theory]
    [InlineData("R0", "R0", false)]
    [InlineData("R2a", "R2b", false)]
    [InlineData("R2b", "R2a", true)]
    [InlineData("R2b", "R2b", false)]
    [InlineData("R3", "R2b", true)]
    [InlineData("R6", "R6", false)]
    [InlineData("R7", "R6", true)]
    public async Task StagesUseTheConfiguredOrderingAndExpireOnlyAfterRemoval(string current, string removal, bool overdue)
    {
        var source = "[Kimi.Transitional(\"" + removal + "\",Reason=\"x\")] class C {}";
        var diagnostics = await Analyze(source, current);
        if (overdue)
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("KR1007", diagnostic.Id);
            Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
            Assert.Contains("removal stage " + removal + " precedes current stage " + current, diagnostic.GetMessage());
        }
        else
        {
            Assert.Empty(diagnostics);
        }

        var row = Assert.Single(await Analyze(source, current, "Report"), x => x.Id == "KR9001");
        Assert.Equal("transitional", row.Properties["kind"]);
        Assert.Equal(removal, row.Properties["stage"]);
        Assert.Equal(Array.IndexOf(CodeRulesConfiguration.Stages, removal).ToString(System.Globalization.CultureInfo.InvariantCulture), row.Properties["value"]);
        Assert.Equal("false", row.Properties["baselineAllowed"]);
        Assert.False(row.Properties.ContainsKey("limit"));
    }

    [Theory]
    [InlineData("\"R99\",Reason=\"x\"")]
    [InlineData("\"r6\",Reason=\"x\"")]
    [InlineData("\"R6\",Reason=\" \"")]
    [InlineData("\"R6\"")]
    [InlineData("null,Reason=\"x\"")]
    public async Task MalformedAnnotationUsesTheSharedError(string arguments)
        => Assert.Equal("KR9005", Assert.Single(await Analyze("[Kimi.Transitional(" + arguments + ")] class C {}")).Id);

    [Theory]
    [InlineData("class C {}", "member:T:C")]
    [InlineData("struct C {}", "member:T:C")]
    [InlineData("interface C {}", "member:T:C")]
    [InlineData("delegate void C();", "member:T:C")]
    public async Task TypeDeclarationsUseTheirDocumentationIdentity(string declaration, string anchor)
    {
        var row = Assert.Single(await Analyze(Attribute + declaration, mode: "Report"), x => x.Id == "KR9001");
        Assert.Equal("T:C", row.Properties["scope"]);
        Assert.Equal(anchor, row.Properties["anchor"]);
    }

    [Fact]
    public async Task MembersEnumFieldsAndLocalFunctionsShareSourceIdentities()
    {
        var source = "class C { " + Attribute + " int a,b; " + Attribute + " int P=>0; " + Attribute + " C(){} " +
            "void M(){ " + Attribute + " void L(){} } } enum E { " + Attribute + " Value }";
        var rows = (await Analyze(source, mode: "Report")).Where(x => x.Id == "KR9001").ToArray();
        Assert.Equal(6, rows.Length);
        foreach (var anchor in new[] { "member:F:C.a", "member:F:C.b", "member:P:C.P", "member:M:C.#ctor", "local:M:C.M/L", "member:F:E.Value" })
        {
            Assert.Contains(rows, x => x.Properties["anchor"] == anchor);
        }
    }

    [Fact]
    public async Task AliasAndConstantChangesBindThroughTheExistingSemanticPath()
    {
        const string source = "using T=Kimi.TransitionalAttribute; class C { const string Removal=\"R\"+\"6\"; [T(stage:Removal,Reason=nameof(C))] void M(){} }";
        var row = Assert.Single(await Analyze(source, mode: "Report"), x => x.Id == "KR9001");
        Assert.Equal("R6", row.Properties["stage"]);
        Assert.Equal("C", row.Properties["reason"]);
        Assert.Equal("KR1007", Assert.Single(await Analyze(source.Replace("\"6\"", "\"3\"", StringComparison.Ordinal))).Id);
    }

    [Fact]
    public async Task PartialMovesKeepTheKeyAndBothKindsAreReconciled()
    {
        var annotated = "partial class C { " + Attribute + " [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"dispatch\")] partial void M(); }";
        const string body = "partial class C { partial void M(){;} }";
        var before = await CodeRulesTestHelper.Analyze(Compilation(annotated, body), Options("R4", "Report"));
        var after = await CodeRulesTestHelper.Analyze(Compilation(body, annotated), Options("R4", "Report"));
        var first = before.Where(x => x.Id == "KR9001").OrderBy(x => x.Properties["rule"], StringComparer.Ordinal).ToArray();
        var second = after.Where(x => x.Id == "KR9001").OrderBy(x => x.Properties["rule"], StringComparer.Ordinal).ToArray();
        Assert.Equal(2, first.Length);
        Assert.Equal(first.Select(x => (x.Properties["rule"], x.Properties["scope"], x.Properties["anchor"])), second.Select(x => (x.Properties["rule"], x.Properties["scope"], x.Properties["anchor"])));
        Assert.Contains(first, x => x.Properties["kind"] == "exception" && x.Properties["value"] == "1");
        Assert.Contains(first, x => x.Properties["kind"] == "transitional" && x.Properties["stage"] == "R6");
    }

    [Fact]
    public async Task DuplicatePartialAnnotationsAndUnsupportedTargetsAreRejected()
    {
        var compilation = Compilation(Attribute + " partial class C {}", Attribute + " partial class C {}");
        Assert.Equal("KR9005", Assert.Single(await CodeRulesTestHelper.Analyze(compilation, Options("R4", "Enforce"))).Id);
        var invalid = Compilation("[assembly:Kimi.Transitional(\"R6\",Reason=\"x\")] class C {} ");
        Assert.Equal("KR9005", Assert.Single(await CodeRulesTestHelper.Analyze(invalid, Options("R4", "Enforce"))).Id);
    }

    [Fact]
    public async Task LegacyAnnotationsAreAuditedButInapplicableExceptionsAreRejected()
    {
        const string path = "src/Kimi/Compiler/Emission/Fixture.cs";
        var source = Attribute + " class C { [Kimi.CodeRuleException(\"KR2006\",Limit=70,Reason=\"x\")] void M(){} }";
        var compilation = CodeRulesTestHelper.Compilation((source, path), (TransitionalSource, "src/Kimi/Transitional.cs"), (ExceptionSource, "src/Kimi/Exception.cs"));
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options("R4", "Report"));
        Assert.Single(diagnostics, x => x.Id == "KR9005");
        Assert.Single(diagnostics, x => x.Id == "KR9001" && x.Properties["kind"] == "transitional");
    }

    [Fact]
    public void ConditionalUsageIsAbsentFromProductMetadata()
    {
        var compilation = Compilation(Attribute + " class C { " + Attribute + " void M(){} }");
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success);
        stream.Position = 0;
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        Assert.Empty(metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(x => metadata.GetString(x.Name) == "C").GetCustomAttributes());
        Assert.Empty(metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Single(x => metadata.GetString(x.Name) == "M").GetCustomAttributes());
    }

    private static CSharpCompilation Compilation(params string[] sources)
        => CodeRulesTestHelper.Compilation(sources.Concat([TransitionalSource, ExceptionSource]).ToArray());

    private static Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions Options(string stage, string mode)
        => CodeRulesTestHelper.Options(mode, configure: config =>
        {
            config["kimi_code_rules.stage"] = stage;
            config["kimi_code_rules.scope.density.exclude"] = "src/Kimi/";
            foreach (var key in config.Keys.Where(x => x.Contains(".layer.", StringComparison.Ordinal) || x.Contains(".deny_", StringComparison.Ordinal) ||
                x.Contains(".allow_", StringComparison.Ordinal) || x.EndsWith(".vocabularies", StringComparison.Ordinal)).ToArray())
            {
                config.Remove(key);
            }
        });

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string stage = "R4", string mode = "Enforce")
    {
        var compilation = Compilation(source);
        Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, Options(stage, mode));
        Assert.DoesNotContain(diagnostics, x => x.Id is "AD0001" or "KR9003");
        return diagnostics;
    }
}
