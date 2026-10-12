// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace XunitTest;

public class CodeRulesArchitectureTest
{
    private const string Consumer = "src/Kimi/Checking/Hover/Consumer.cs";
    private const string Hidden = "src/Kimi/Compiler/Binding/Hidden.cs";
    private const string Shared = "src/Kimi/Shared/Types.cs";
    private const string Types = "namespace Fixture { public class Secret { public int Value; public static int Read() => 1; } public class SecretAttribute : System.Attribute { } public class Published { } }";

    [Theory]
    [InlineData("class C { Fixture.Secret value; }", 1)]
    [InlineData("class C { System.Collections.Generic.List<Fixture.Secret> values; }", 1)]
    [InlineData("using Alias = Fixture.Secret; class C { Alias value; }", 2)]
    [InlineData("class C { object M() => typeof(Fixture.Secret); }", 1)]
    [InlineData("class C { string M() => nameof(Fixture.Secret); }", 1)]
    [InlineData("class C { int M() => Fixture.Secret.Read(); }", 2)]
    [InlineData("[Fixture.Secret] class C { }", 1)]
    [InlineData("using A = Fixture.SecretAttribute; [A] class C { }", 2)]
    [InlineData("/// <see cref=\"Fixture.Secret\"/>\nclass C { }", 0)]
    [InlineData("class C { Fixture.Published value; }", 0)]
    [InlineData("class Secret { } class C { Secret value; }", 0)]
    public async Task ReferencesUseSemanticIdentityAndOneNameOwner(string source, int count)
    {
        var diagnostics = await Analyze([(source, Consumer), (Types, Hidden)], Options(x => x[Key("allow_types.use")] = "Fixture.Published"));
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1001", x.Id));
    }

    [Fact]
    public async Task InferredReceiverStillChecksTheDeclaringMemberType()
    {
        var diagnostics = await Analyze(
            [("class C { int M() { var value = Factory.Make(); return value.Value; } }", Consumer), (Types, Hidden), ("static class Factory { public static Fixture.Secret Make() => new(); }", Shared)],
            Options());
        Assert.Contains(diagnostics, x => x.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(x.Location.SourceSpan) == "Value");
        Assert.All(diagnostics, x => Assert.Equal("KR1001", x.Id));
    }

    [Theory]
    [InlineData("class C { void M() { var value = Factory.Make(); } }", "static class Factory { public static Fixture.Secret Make() => new(); }", "var")]
    [InlineData("class C { int M() => Factory.Run().Invoke(1); }", "static class Factory { public static Fixture.Run Run() => x => x; }", "Invoke")]
    [InlineData("class C { int? M() => Factory.Run()?.Invoke(1); }", "static class Factory { public static Fixture.Run Run() => x => x; }", "Invoke")]
    [InlineData("using Fixture; class C { int M() => 1.Twice(); }", "class Unused { }", "Twice")]
    [InlineData("class C { Hidden value; }", "global using Hidden = Fixture.Secret;", "Hidden")]
    public async Task OnlyCandidateNamesReachSemanticsAndCandidatesCoverEveryBinding(string source, string shared, string name)
    {
        const string extras = "namespace Fixture { public delegate int Run(int x); public static class Extensions { public static int Twice(this int x) => x * 2; } }";
        var diagnostics = await Analyze([(source, Consumer), (Types, Hidden), (extras, "src/Kimi/Compiler/Binding/Extras.cs"), (shared, Shared)], Options());
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("KR1001", diagnostic.Id);
        Assert.Equal(name, diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task NativeIntegerNamesBindToTheirTypes()
    {
        var diagnostics = await Analyze([("class C { nint value; }", Consumer)], Options(x => x[Key("deny_types.use")] = "System.IntPtr"));
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("nint", diagnostic.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 0)]
    public async Task EveryPartialDeclarationContributesItsLayer(bool reverse, bool allow, int count)
    {
        (string Source, string Path)[] sources =
        [
            ("class C { Fixture.Partial value; }", Consumer),
            ("namespace Fixture { public partial class Partial { } }", Hidden),
            ("namespace Fixture { public partial class Partial { } }", Shared),
        ];
        if (reverse)
        {
            Array.Reverse(sources);
        }

        var diagnostics = await Analyze(sources, Options(x => x[Key("allow_types.use")] = allow ? "Fixture.Partial" : "System.String"));
        Assert.Equal(count, diagnostics.Length);
    }

    [Theory]
    [InlineData("src/Kimi/Compiler/Root.cs", 1)]
    [InlineData("src/Kimi/Compiler/Other/Nested.cs", 0)]
    [InlineData("src/Kimi/Compiler/Binding/Hidden.cs", 0)]
    public async Task DirectFilesAndLongestPrefixDecideLayers(string path, int count)
    {
        var diagnostics = await Analyze([("class C { Target value; }", Consumer), ("class Target { }", path)], Options(x =>
        {
            x[Key("layer.root")] = "src/Kimi/Compiler/*.cs";
            x[Key("deny_layers.use")] = "root";
        }));
        Assert.Equal(count, diagnostics.Length);
    }

    [Theory]
    [InlineData("class C { int M(Fixture.Derived x) => x.Slot; }", 1)]
    [InlineData("class C { int M(Fixture.Base x) => x.Slot; }", 1)]
    [InlineData("class C { int M(Fixture.Other x) => x.Slot; }", 0)]
    [InlineData("class C { int M(Fixture.Derived x) => x.NewSlot; }", 1)]
    public async Task MemberSelectorsCoverDerivedSlotsAndOverridesWithoutNameFalsePositives(string source, int count)
    {
        const string definitions = "namespace Fixture { class Base { public virtual int Slot => 0; } class Derived : Base { public override int Slot => 1; public int NewSlot; } class Other { public int Slot; } }";
        var diagnostics = await Analyze([(source, Consumer), (definitions, Shared)], Options(x =>
            x[Key("deny_members.use")] = "Fixture.Base.Slot,Fixture.Base.NewSlot"));
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1002", x.Id));
    }

    [Theory]
    [InlineData("class C { void GetChildNodes() { } void M() => GetChildNodes(); }", 2)]
    [InlineData("class C { int GetChildNodes; }", 1)]
    [InlineData("class C { int GetChildNodes => 0; }", 1)]
    [InlineData("class C { void M() { int GetChildNodes = 0; System.Console.Write(GetChildNodes); } }", 0)]
    [InlineData("class C { void M() { void GetChildNodes() { } GetChildNodes(); } }", 0)]
    [InlineData("class GetChildNodes { }", 0)]
    [InlineData("class C { string s = \"GetChildNodes\"; /* GetChildNodes */ }", 0)]
    public async Task RemovedGlobalMemberNamesCannotBeReintroduced(string source, int count)
    {
        var diagnostics = await Analyze([(source, Shared)], Options(x => x[Key("deny_members.all")] = "GetChildNodes"));
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1002", x.Id));
    }

    [Theory]
    [InlineData("enum E { A, B }", 0)]
    [InlineData("enum E { A = 0, Alias = 0, B = 1 }", 1)]
    public async Task ClosedEnumsCountExplicitMembersIncludingAliases(string source, int count)
    {
        var diagnostics = await Analyze([(source, Shared)], Options(x => x[Key("vocabularies")] = "E:2"));
        Assert.Equal(count, diagnostics.Length);
        if (count != 0)
        {
            Assert.Equal("KR1003", diagnostics[0].Id);
            Assert.Contains("value 3, limit 2", diagnostics[0].GetMessage(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Enforce", DiagnosticSeverity.Warning)]
    [InlineData("Verify", DiagnosticSeverity.Error)]
    [InlineData("Report", DiagnosticSeverity.Warning)]
    public async Task DeletedEnumIsLocatedStaleConfigurationAndReportStillCompletes(string mode, DiagnosticSeverity severity)
    {
        var diagnostics = await Analyze([("class C { }", Shared)], Options(x => x[Key("vocabularies")] = "Missing:2", mode));
        var stale = Assert.Single(diagnostics, x => x.Id == "KR9002");
        Assert.Equal(severity, stale.Severity);
        Assert.Equal("CodeRules.globalconfig", stale.Location.GetLineSpan().Path);
        Assert.Equal(mode != "Enforce", diagnostics.Any(x => x.Id == "KR9000"));
        Assert.DoesNotContain(diagnostics, x => x.Id == "KR9003");
    }

    [Theory]
    [InlineData("deny_layers.use", "absent")]
    [InlineData("deny_types.unknown", "System.String")]
    [InlineData("deny_types.use", "Absent")]
    [InlineData("deny_types.use", "System.String,System.String")]
    [InlineData("deny_members.use", "System.String")]
    [InlineData("deny_members.all", "System.String.Length")]
    [InlineData("layer.duplicate", "src/Kimi/Checking/Hover/")]
    [InlineData("layer.use", "src/Kimi/**/*.cs")]
    [InlineData("vocabularies", "System.String:1")]
    [InlineData("vocabularies", "Missing:0")]
    [InlineData("vocabularies", "Missing:1,Missing:2")]
    public async Task InvalidPoliciesFailWithoutCompletion(string option, string value)
    {
        var diagnostics = await Analyze([("class C { }", Shared)], Options(x => x[Key(option)] = value, "Report"));
        Assert.Contains(diagnostics, x => x.Id == "KR9003");
        Assert.DoesNotContain(diagnostics, x => x.Id == "KR9000");
    }

    [Fact]
    public async Task ForbiddenReferencesFlowThroughTheCommonReportPipeline()
    {
        var diagnostics = await Analyze([("class C { Fixture.Secret value; }", Consumer), (Types, Hidden)], Options(mode: "Report"));
        var observation = Assert.Single(diagnostics, x => x.Id == "KR9001");
        Assert.Equal("KR1001", observation.Properties["rule"]);
        Assert.Equal("false", observation.Properties["baselineAllowed"]);
        Assert.Single(diagnostics, x => x.Id == "KR9000");
    }

    private static string Key(string key) => CodeRulesConfiguration.Prefix + key;

    private static AnalyzerOptions Options(Action<Dictionary<string, string>>? configure = null, string mode = "Enforce")
        => CodeRulesTestHelper.Options(mode, configure: values =>
        {
            foreach (var key in values.Keys.Where(x => x.StartsWith(Key("layer."), StringComparison.Ordinal) ||
                x.StartsWith(Key("deny_"), StringComparison.Ordinal) || x.StartsWith(Key("allow_"), StringComparison.Ordinal) || x == Key("vocabularies")).ToArray())
            {
                values.Remove(key);
            }

            values[Key("layer.use")] = "src/Kimi/Checking/Hover/";
            values[Key("layer.hidden")] = "src/Kimi/Compiler/Binding/";
            values[Key("deny_layers.use")] = "hidden";
            values[Key("allow_types.use")] = "System.String";
            values[Key("scope.density.exclude")] = "src/Kimi/";
            values[Key("scope.accretion.exclude")] = "src/Kimi/";
            configure?.Invoke(values);
        });

    private static async Task<ImmutableArray<Diagnostic>> Analyze((string Source, string Path)[] sources, AnalyzerOptions options)
    {
        var compilation = CodeRulesTestHelper.Compilation(sources);
        Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
        return await CodeRulesTestHelper.Analyze(compilation, options);
    }
}
