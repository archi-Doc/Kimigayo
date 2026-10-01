// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

/// <summary>Excluded syntax receives the source checks of selected syntax and no semantic checks (SPEC 19.5).</summary>
public class ExcludedSyntaxTest
{
    private const string Windows = "x86_64-pc-windows-msvc";
    private const string Linux = "x86_64-unknown-linux-gnu";
    private const string Linux32 = "i686-unknown-linux-gnu";

    [Theory]
    [InlineData("#if windows\n    alias Kimi.Console\nlet a = 1")]
    [InlineData("#if windows\n    rootgroup Sample.Inner\nlet a = 1")]
    [InlineData("#switch\n    #case windows\n        alias Kimi.Console\n    #case _\n        alias Kimi.Console\nlet a = 1")]
    public void SelectedRootDirectiveBodiesKeepTheRootItemGrammar(string source)
        => Assert.Empty(TestDiagnostics.Of(Parse(source)));

    [Fact]
    public void SelectedFunctionArmsKeepTheLeadingConstraintRegion()
    {
        var c = Bind("func inspect<T>(value: T) -> ()\n    #switch\n        #case debug\n            T is Comparable\n        #case _\n            T is Copy\n    return");
        Assert.Empty(TestDiagnostics.Of(c));
        var function = c.Kotonoha.RootKoto.Members.OfType<FunctionKoto>().Concat(c.Kotonoha.GeneratedFunction!.Body!.Items.OfType<FunctionKoto>()).Single(x => x.Name == "inspect");
        Assert.Single(function.TypeConstraints);
    }

    [Fact]
    public void SelectedEnumArmsKeepTheirCases()
    {
        var c = Bind("enum Platform\n    #switch\n        #case windows\n            Windows\n        #case _\n            Other\nlet platform = Platform.Windows");
        Assert.Empty(TestDiagnostics.Of(c));
        var platform = Assert.IsType<EnumKoto>(Assert.Single(c.Kotonoha.RootKoto.NestedDeclarationContainers));
        Assert.Single(platform.Members);
    }

    [Theory]
    [InlineData(Windows)]
    [InlineData(Linux)]
    [InlineData(Linux32)]
    public void IndependentIfsAndSwitchesWithTheSameSelectionAcceptTheSameSource(string target)
    {
        const string Ifs = "#if linux\n    let pending =\n#if not linux\n    ()";
        const string Switch = "#switch\n    #case linux\n        let pending =\n    #case _\n        ()";
        Assert.Equal([nameof(DiagnosticCode.MissingSyntax_Kd)], Codes(Parse(Ifs, target)));
        Assert.Equal([nameof(DiagnosticCode.MissingSyntax_Kd)], Codes(Parse(Switch, target)));
    }

    [Fact]
    public void SourceDiagnosticsAreTheSameInEveryCompilationOfTheModule()
    {
        const string Source = """
            #if windows
            func helper() => ()
            alias Kimi.Console
            #if linux
                #if useDirectWirte
                    let pending =
            #switch
                #case debug
                    static func f() => ()
                #case _
                    ()
            func process<T>(value: T) -> ()
                #if debug
                    ()
                T is Copy
            """;
        var expected = Facts(Parse(Source, Windows));
        Assert.Equal(5, expected.Length);
        foreach (var target in new[] { Windows, Linux, Linux32 })
        {
            foreach (var debug in new[] { false, true })
            {
                Assert.Equal(expected, Facts(Parse(Source, target, debug)));
            }
        }
    }

    [Theory]
    [InlineData(Windows, false)]
    [InlineData(Linux, true)]
    public void SelectionErrorsAreReportedOnlyWhereSelectionHappens(string target, bool reported)
    {
        var codes = Codes(Parse("#switch\n    #case linux\n        #switch\n            #case arch == \"aarch64\"\n                ()\n    #case _\n        ()", target));
        Assert.Equal(reported ? [nameof(DiagnosticCode.NonExhaustiveCompileTimeCase_Kd)] : [], codes);
    }

    [Fact]
    public void AnInvalidConditionStillParsesItsTarget()
    {
        var c = Parse("#if missingName\n    let pending =\nlet a = 1");
        Assert.Equal([nameof(DiagnosticCode.MissingSyntax_Kd), nameof(DiagnosticCode.UnknownCompileTimeName_Kd)], Codes(c).Order());
        Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items);
    }

    [Fact]
    public void ACaseOutsideASwitchValidatesItsConditionAndBody()
    {
        var c = Parse("#case missingName\n    let pending =\nlet a = 1");
        Assert.Equal(
            [nameof(DiagnosticCode.CompileTimeCaseOutsideSwitch_Kd), nameof(DiagnosticCode.MissingSyntax_Kd), nameof(DiagnosticCode.UnknownCompileTimeName_Kd)],
            Codes(c).Order());
        Assert.Equal("a", Assert.IsType<FieldKoto>(Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items)).NameKoto.IdentifierName);
    }

    [Theory]
    [InlineData("#Inline\n#if windows\n    func f() => ()")]
    [InlineData("#Inline\n#switch\n    #case _\n        func f() => ()")]
    public void AttributesBeforeDirectiveBlocksAreDangling(string source)
        => Assert.NotEmpty(TestDiagnostics.Of(Parse(source)));

    [Fact]
    public void AnAttributeBeforeAnIfAttachesToItsSingleDeclaration()
    {
        var c = Parse("#Inline\n#if windows\nfunc f() => ()");
        Assert.Empty(TestDiagnostics.Of(c));
        var function = Assert.IsType<FunctionKoto>(Assert.Single(c.Kotonoha.GeneratedFunction!.Body!.Items));
        Assert.NotNull(function.AttributeChain);
    }

    [Fact]
    public void ExcludedDeclarationsRegisterNothing()
    {
        var c = Parse("""
            struct Box
                var a: i32
                #if linux
                    var b: i32
            #if linux
                rootgroup Hidden
                func hidden() => ()
                let value = 1
            struct Box
                var c: i32
            """);
        Assert.Empty(TestDiagnostics.Of(c));
        var box = Assert.Single(c.Kotonoha.RootKoto.NestedDeclarationContainers);
        Assert.Equal(["a", "c"], box.Members.OfType<PropertyKoto>().Select(x => x.NameKoto.IdentifierName));
        Assert.Null(c.Kotonoha.GeneratedFunction);
    }

    [Theory]
    [InlineData("#if windows\nfunc helper() => ()\nalias Kimi.Console")]
    [InlineData("#if linux\nfunc helper() => ()\nalias Kimi.Console")]
    public void AliasPlacementCountsExcludedItems(string source)
        => Assert.Equal([nameof(DiagnosticCode.TopLevelKeywordAfterCode_Kd)], Codes(Parse(source)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheConstraintPrefixCountsExcludedItems(bool debug)
        => Assert.NotEmpty(TestDiagnostics.Of(Parse("func process<T>(value: T) -> ()\n    #if debug\n        ()\n    T is Copy\n    return", Windows, debug)));

    [Fact]
    public void AliasesInsideDirectivesDoNotEndTheAliasRegion()
        => Assert.Empty(TestDiagnostics.Of(Parse("#if windows\n    alias Kimi.Console\n#if linux\n    alias Kimi.Console\nalias Kimi.Console\nlet a = 1")));

    [Fact]
    public void UniquenessIsJudgedAfterSelection()
    {
        var c = Bind("#switch\n    #case windows\n        func platformName() -> string => \"windows\"\n    #case _\n        func platformName() -> string => \"other\"\nlet name = platformName()");
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Fact]
    public void ExcludedDiagnosticsNameTheExcludingDirective()
    {
        const string Source = "#if linux\n    let pending =";
        var excluded = Assert.Single(Records(Parse(Source, Windows)));
        var related = Assert.Single(excluded.Related ?? [], x => x.Role == "excludedBy");
        Assert.Equal(new SourceSpan(4, 5), related.Span);

        var selected = Assert.Single(Records(Parse(Source, Linux)));
        Assert.DoesNotContain(selected.Related ?? [], x => x.Role == "excludedBy");
        Assert.Equal((excluded.Code, excluded.Span, excluded.Message), (selected.Code, selected.Span, selected.Message));
    }

    [Fact]
    public void TheInnermostExcludingDirectiveIsNamed()
    {
        const string Source = "#if linux\n    #if debug\n        let pending =";
        var record = Assert.Single(Records(Parse(Source, Windows)));
        Assert.Equal(new SourceSpan(18, 5), Assert.Single(record.Related ?? [], x => x.Role == "excludedBy").Span);
    }

    [Fact]
    public void ANestedConditionErrorNamesTheEnclosingDirective()
    {
        // The nested #if is itself inside linux's target; its own exclusion covers only its target.
        const string Source = "#if linux\n    #if useDirectWirte\n        let x = 1\n    let pending =";
        var records = Records(Parse(Source, Windows));
        Assert.Equal(2, records.Length);
        Assert.All(records, x => Assert.Equal(new SourceSpan(4, 5), Assert.Single(x.Related ?? [], y => y.Role == "excludedBy").Span));
    }

    private static Compilation Parse(string source, string target = Windows, bool debug = false)
    {
        var c = Compilation.CreateForTest();
        c.Project.KimiOptions.Debug = debug;
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument("excluded.kimi", source));
        return c;
    }

    private static Compilation Bind(string source)
    {
        var c = Parse(source);
        _ = c.Bind();
        return c;
    }

    private static string[] Codes(Compilation c)
        => TestDiagnostics.Of(c).Select(x => x.Code).ToArray();

    private static (string Code, int Start, int Length)[] Facts(Compilation c)
        => TestDiagnostics.Of(c).Select(x => (x.Code, x.Span.Start, x.Span.Length)).ToArray();

    private static CheckDiagnostic[] Records(Compilation c)
        => c.Diagnostics.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission).Diagnostics;
}
