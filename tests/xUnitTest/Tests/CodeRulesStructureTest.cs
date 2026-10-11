// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Collections.Immutable;
using Kimi.Analyzers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace XunitTest;

public class CodeRulesStructureTest
{
    private const string Imports = "using System; using System.Collections.Generic; using Kimi.Compiler.Parsing; ";
    private const string Builder = "src/Kimi/Compiler/Mir/Build/Fixture.cs";

    [Theory]
    [InlineData("Dictionary<Koto,int> map;", 1)]
    [InlineData("IReadOnlyDictionary<IdentifierNameKoto,int> map;", 1)]
    [InlineData("ISet<Koto> nodes;", 1)]
    [InlineData("System.Runtime.CompilerServices.ConditionalWeakTable<Koto,object> map;", 1)]
    [InlineData("Dictionary<Koto,int> map = new Dictionary<Koto,int>();", 2)]
    [InlineData("Dictionary<Koto,int> map = new();", 2)]
    [InlineData("HashSet<Koto> nodes = [];", 2)]
    [InlineData("void M() { var map = new Dictionary<Koto,int>(); }", 2)]
    [InlineData("Dictionary<Koto,int> M() => null;", 1)]
    [InlineData("void M(Dictionary<Koto,int> map) { }", 1)]
    [InlineData("Dictionary<Koto,int> Map => null;", 1)]
    [InlineData("Dictionary<Koto,int>? Map => null;", 1)]
    [InlineData("void M() { Dictionary<Koto,int> Local() => null; }", 1)]
    [InlineData("Dictionary<string,Koto> map;", 0)]
    [InlineData("List<Dictionary<Koto,int>> maps;", 0)]
    [InlineData("Type Kind => typeof(Dictionary<Koto,int>);", 0)]
    [InlineData("Dictionary<Koto,int>[] maps;", 0)]
    [InlineData("void Take(out Dictionary<Koto,int> map) { map = null; } void M() { Take(out Dictionary<Koto,int> map); }", 2)]
    [InlineData("void Take(out Dictionary<Koto,int> map) { map = null; } void M() { Take(out var map); }", 2)]
    [InlineData("void M(Dictionary<Koto,int>[] maps) { foreach (var map in maps) { } }", 1)]
    [InlineData("void M(object value) { if (value is Dictionary<Koto,int> map) { } }", 1)]
    [InlineData("void M(object value) { if (value is Dictionary<Koto,int>) { } }", 0)]
    [InlineData("void M(object value) { if (value is Dictionary<Koto,int> _) { } }", 0)]
    [InlineData("void M() { var (map,n) = (default(Dictionary<Koto,int>),0); }", 1)]
    [InlineData("Func<Dictionary<Koto,int>,bool> test = map => true;", 1)]
    [InlineData("Func<Dictionary<Koto,int>,bool> test = (Dictionary<Koto,int> map) => true;", 1)]
    public async Task CollectionsUseDeclaredOrCreatedTypesOnce(string body, int count)
    {
        var diagnostics = await Analyze(Imports + "class C { " + body + " }");
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1004", x.Id));
    }

    [Theory]
    [InlineData("src/Kimi/Compiler/Binding/Fixture.cs", 1)]
    [InlineData("src/Kimi/Compiler/Emission/Fixture.cs", 1)]
    [InlineData("src/Kimi/Compiler/Parsing/Fixture.cs", 0)]
    [InlineData("src/Kimi/Compiler/Lexing/Fixture.cs", 0)]
    public async Task CollectionAliasesAndDerivedImplementationsRespectSyntaxLayer(string path, int count)
    {
        var source = "using Map = Fixture.Map; namespace Fixture { class Map : System.Collections.Generic.Dictionary<Kimi.Compiler.Parsing.Koto,int> { } } class C { Map value; }";
        var diagnostics = await Analyze(source, path);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1004", x.Id));
    }

    [Theory]
    [InlineData("class C<T> where T : Koto { Dictionary<T,int> map; }", 1)]
    [InlineData("class C<T> where T : IDictionary<Koto,int> { T map; }", 1)]
    [InlineData("class C<T> where T : notnull { Dictionary<T,int> map; }", 0)]
    [InlineData("class C<T,U> where T : U where U : Koto { Dictionary<T,int> map; }", 1)]
    public async Task GenericConstraintsUsePublishedContracts(string source, int count)
    {
        var diagnostics = await Analyze(Imports + source);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1004", x.Id));
    }

    [Fact]
    public async Task InvalidConstraintCyclesDoNotBreakTheAnalyzer()
    {
        var diagnostics = await CodeRulesTestHelper.Analyze(Imports + "class C<T,U> where T : U where U : T { Dictionary<T,int> map; T other; }", CodeRulesTestHelper.Options("Report"));
        Assert.DoesNotContain(diagnostics, x => x.Id == "AD0001");
        Assert.Single(diagnostics, x => x.Id == "KR9000");
    }

    [Theory]
    [InlineData("bool SupportsCall() => true;", 1)]
    [InlineData("bool SupportsCall => true;", 1)]
    [InlineData("bool Supports<T>() => true;", 1)]
    [InlineData("void M() { bool SupportsCall() => true; }", 1)]
    [InlineData("bool? SupportsCall() => true;", 0)]
    [InlineData("int SupportsCall() => 1;", 0)]
    [InlineData("bool SupportCall() => true;", 0)]
    [InlineData("bool SupportsCall;", 0)]
    [InlineData("void M() { bool SupportsCall = true; }", 0)]
    public async Task GatesAreBoolReturningOperations(string body, int count)
    {
        var diagnostics = await Analyze("class C { " + body + " }");
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1005", x.Id));
    }

    [Theory]
    [InlineData("return value is Koto;", 1)]
    [InlineData("return value is IdentifierNameKoto found;", 1)]
    [InlineData("return value is IdentifierNameKoto { };", 1)]
    [InlineData("return (value as IdentifierNameKoto) != null;", 1)]
    [InlineData("return value is not (IdentifierNameKoto);", 1)]
    [InlineData("return value is IdentifierNameKoto or NumberLiteralKoto;", 2)]
    [InlineData("return value switch { IdentifierNameKoto => true, _ => false };", 1)]
    [InlineData("switch(value) { case IdentifierNameKoto found: return true; default: return false; }", 1)]
    [InlineData("return value is string;", 0)]
    [InlineData("return (IdentifierNameKoto)value != null;", 0)]
    [InlineData("return value is var found;", 0)]
    [InlineData("return value is not (KotoKind.Invalid);", 1)]
    [InlineData("return value is KotoKind.Invalid or KotoKind.Contract;", 2)]
    [InlineData("return value is KotoKind kind;", 1)]
    [InlineData("switch(value) { case KotoKind.Invalid: return true; default: return false; }", 1)]
    public async Task SyntaxDispatchUsesSemanticTestsAndExcludesCasts(string body, int count)
    {
        var diagnostics = await Analyze(Imports + "class C { bool M(object value) { " + body + " } }", Builder);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1006", x.Id));
    }

    [Theory]
    [InlineData("return kind == KotoKind.Invalid;", 1)]
    [InlineData("return kind != KotoKind.Invalid;", 1)]
    [InlineData("return kind is KotoKind.Invalid;", 1)]
    [InlineData("return kind is not (KotoKind.Invalid or KotoKind.Contract);", 2)]
    [InlineData("return kind switch { KotoKind.Invalid => true, _ => false };", 1)]
    [InlineData("switch(kind) { case 0: return true; default: return false; }", 1)]
    [InlineData("return kind is var captured;", 0)]
    public async Task KindComparisonsAndAtomicPatternsCountOnce(string body, int count)
    {
        var diagnostics = await Analyze(Imports + "class C { bool M(KotoKind kind) { " + body + " } }", Builder);
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1006", x.Id));
    }

    [Theory]
    [InlineData("src/Kimi/Compiler/Mir/Build/Fixture.cs", 1)]
    [InlineData("src/Kimi/Compiler/Binding/Fixture.cs", 0)]
    public async Task DispatchAliasRespectsBuilderScope(string path, int count)
    {
        var diagnostics = await Analyze("using Node = Kimi.Compiler.Parsing.IdentifierNameKoto; class C { bool M(object value) => value is Node; }", path);
        Assert.Equal(count, diagnostics.Length);
    }

    [Theory]
    [InlineData("catch", 1)]
    [InlineData("catch (Exception)", 1)]
    [InlineData("catch (Exception error)", 1)]
    [InlineData("catch (InvalidOperationException)", 0)]
    [InlineData("catch (Exception error) when (error.Message.Length > 0)", 0)]
    [InlineData("catch when (DateTime.UtcNow.Year > 0)", 0)]
    public async Task CatchRequiresAnExactUnfilteredGeneralException(string header, int count)
    {
        var diagnostics = await Analyze(Imports + "class C { void M() { try { } " + header + " { } } }");
        Assert.Equal(count, diagnostics.Length);
        Assert.All(diagnostics, x => Assert.Equal("KR1008", x.Id));
    }

    [Theory]
    [InlineData("src/Kimi/Unit/Command/Fixture.cs", 0)]
    [InlineData("src/Kimi/Lsp/Fixture.cs", 0)]
    [InlineData("src/Kimi/Testing/Fixture.cs", 0)]
    [InlineData("src/Kimi/TestingElsewhere/Fixture.cs", 1)]
    public async Task CatchBoundariesUseDirectoryPrefixes(string path, int count)
    {
        var diagnostics = await Analyze("using Failure = System.Exception; class C { void M() { try { } catch(Failure) { } } }", path);
        Assert.Equal(count, diagnostics.Length);
    }

    [Fact]
    public async Task AUserExceptionNamedExceptionIsNotTheGeneralException()
    {
        Assert.Empty(await Analyze("class Exception : System.Exception { } class C { void M() { try { } catch(Exception) { } } }"));
    }

    [Fact]
    public async Task CatchAnchorIgnoresBodyAndFormattingAndUsesTheCommonBaseline()
    {
        const string before = "class C { void M() { try { } catch (System.Exception e) { throw; } } }";
        const string after = "class C { void M() { try { } catch /*comment*/ ( System.Exception e ) { System.Console.WriteLine(e); } } }";
        var observation = Assert.Single(await Analyze(before, mode: "Report"), x => x.Id == "KR9001");
        var updated = Assert.Single(await Analyze(after, mode: "Report"), x => x.Id == "KR9001");
        Assert.Equal(observation.Properties["anchor"], updated.Properties["anchor"]);
        var baseline = string.Join('\t', "KR1008", observation.Properties["scope"], observation.Properties["anchor"], "1", CodeRulesTestHelper.SourcePath);
        Assert.Empty(await Analyze(after, baseline: baseline));
    }

    [Fact]
    public async Task APatternBindingWithoutATypeOwnsItsAnchor()
    {
        var diagnostics = await Analyze(Imports + "class Holder { public Dictionary<Koto,int> Map { get; } } class C { void M(Holder value) { if (value is Holder { Map: var map }) { } } }", mode: "Report");
        var local = Assert.Single(diagnostics, x => x.Id == "KR9001" && x.Properties["scope"] == "T:C");
        Assert.Equal("map", local.Location.SourceTree!.GetText(TestContext.Current.CancellationToken).ToString(local.Location.SourceSpan));
    }

    [Fact]
    public async Task GateReportsMemberAndLocalIdentitiesAndBuilderBaselinePolicy()
    {
        var diagnostics = await Analyze("class C { bool SupportsCall() { bool SupportsValue() => true; return SupportsValue(); } }", Builder, "Report");
        var gates = diagnostics.Where(x => x.Id == "KR9001").ToArray();
        Assert.Equal(2, gates.Length);
        Assert.Contains(gates, x => x.Properties["anchor"] == "member:M:C.SupportsCall");
        Assert.Contains(gates, x => x.Properties["anchor"] == "local:M:C.SupportsCall/SupportsValue");
        Assert.All(gates, x => Assert.Equal("false", x.Properties["baselineAllowed"]));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string path = CodeRulesTestHelper.SourcePath, string mode = "Enforce", string baseline = "")
    {
        var compilation = CodeRulesTestHelper.Compilation((source, path));
        Assert.DoesNotContain(compilation.GetDiagnostics(), x => x.Severity == DiagnosticSeverity.Error);
        var diagnostics = await CodeRulesTestHelper.Analyze(compilation, CodeRulesTestHelper.Options(mode, baseline));
        if (mode == "Report")
        {
            Assert.Single(diagnostics, x => x.Id == "KR9000");
        }

        Assert.DoesNotContain(diagnostics, x => x.Id is "AD0001" or "KR9003");
        return diagnostics;
    }
}
