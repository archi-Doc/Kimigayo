// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class DirectiveRecoveryTest(ITestOutputHelper output)
{
    private const string Windows = "x86_64-pc-windows-msvc";
    private const string Linux = "x86_64-unknown-linux-gnu";

    [Theory]
    [InlineData(Windows, false, "debug")]
    [InlineData(Windows, true, "linux")]
    [InlineData(Linux, false, "debug")]
    [InlineData(Linux, true, null)]
    public void StackedConditionsBelongToThePreviousExclusion(string target, bool debug, string? excluding)
    {
        const string Source = "#if linux\n#if debug\n#if missingName\nfunc f(x:) => ()";
        var records = Records(Parse(Source, target, debug));
        Assert.Equal(["UnknownCompileTimeName_Kd", "ExpectedSyntax_Kd"], records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("missingName", StringComparison.Ordinal), 11), records[0].Span);
        AssertExclusion(records[0], Source, excluding);
        AssertExclusion(records[1], Source, "missingName");
        var selected = Records(Parse(Source, Linux, true));
        Assert.Equal(selected.Select(static x => (x.Code, x.Severity, x.Span, x.Message)), records.Select(static x => (x.Code, x.Severity, x.Span, x.Message)));
    }

    [Theory]
    [InlineData("abstract func f() => ()", "UnavailableFeature_Kd")]
    [InlineData("public", "MissingSyntax_Kd")]
    public void ADiscardedHeaderRetainsItsExcludingPrefix(string header, string code)
    {
        var source = "#if linux\n" + header;
        var excluded = Assert.Single(Records(Parse(source)));
        var selected = Assert.Single(Records(Parse(source, Linux)));
        Assert.Equal(code, excluded.Code);
        AssertExclusion(excluded, source, "linux");
        AssertExclusion(selected, source, null);
        Assert.Equal((selected.Code, selected.Span, selected.Message), (excluded.Code, excluded.Span, excluded.Message));
    }

    [Fact]
    public void ADiscardedTargetDoesNotExcludeTheFollowingItem()
    {
        const string Source = "#if linux\nabstract func f() => ()\nlet pending =";
        var records = Records(Parse(Source));
        Assert.Equal(["UnavailableFeature_Kd", "MissingSyntax_Kd"], records.Select(static x => x.Code));
        AssertExclusion(records[0], Source, "linux");
        AssertExclusion(records[1], Source, null);
    }

    [Fact]
    public void AConditionNeverExcludesItsOwnMissingOperand()
    {
        const string Source = "#if linux\n#if\nfunc f(x:) => ()";
        var records = Records(Parse(Source));
        Assert.Equal(2, records.Length);
        AssertExclusion(records[0], Source, "linux");
        Assert.Equal(new SourceSpan(Source.IndexOf("#if\n", StringComparison.Ordinal) + 3, 0), Assert.Single(records[1].Related ?? [], static x => x.Role == "excludedBy").Span);
    }

    [Theory]
    [InlineData("func outer()")]
    [InlineData("struct Box")]
    public void EnclosingOwnersShareTheSamePrefixIntervals(string owner)
    {
        var source = owner + "\n    #if linux\n    #if missingName\n    func f(x:) => ()";
        var records = Records(Parse(source));
        Assert.Equal(["UnknownCompileTimeName_Kd", "ExpectedSyntax_Kd"], records.Select(static x => x.Code));
        AssertExclusion(records[0], source, "linux");
        AssertExclusion(records[1], source, "missingName");
    }

    [Fact]
    public void StackedPrefixesKeepSelectedDeclarationsAndDocumentation()
    {
        const string Source = "#if false\n#if true\n/// hidden\nfunc hidden() => ()\n/// kept\nfunc kept() => ()";
        var docs = Assert.Single(DocumentationCommentTest.Parse(Source).DocumentationSources);
        Assert.Equal(["kept"], docs.Comments.Where(static x => x.IsSelected && x.Declaration is not null).Select(static x => x.GetText().Text));
        Assert.Empty(docs.GetDiagnostics());
    }

    [Fact]
    public void ValidPrefixesDoNotRegisterExcludedDeclarations()
    {
        const string Source = """
            #if false
            #if true
            func hidden() => unknown
            #if true
            #if true
            func answer() -> i32 => 42
            func run()
                #if false
                #if true
                let hidden = unknown
                #if true
                #if true
                let n = answer()
                Console.writeLine("\(n)")
            run()
            """;
        ScalarEmissionTest.EmitFixture("DirectivePrefixRecovery", Source, "42\n");
    }

    [Theory]
    [InlineData("#switch\n    let missing =\n    #case _\n        ()", "MissingSyntax_Kd")]
    [InlineData("#switch\n    #case true\n        ()\n    let missing =\n    #case _\n        ()", "MissingSyntax_Kd")]
    [InlineData("#switch\n    #case _\n        ()\n    let missing =", "MissingSyntax_Kd")]
    [InlineData("func f()\n    #switch\n        let missing =\n        #case _\n            ()", "MissingSyntax_Kd")]
    [InlineData("struct S\n    #switch\n        func missing(x:) => ()\n        #case _\n            func kept() => ()", "ExpectedSyntax_Kd")]
    [InlineData("enum E\n    #switch\n        Broken(ref/)\n        #case _\n            Good\n    Present", "ExpectedSyntax_Kd")]
    public void InvalidSwitchItemsKeepTheirOwnersSourceChecks(string source, string grammarCode)
    {
        var c = MinimalEmissionTest.Analyze(source + "\nlet wrong: i32 = true");
        c.Binding.ReportDiagnostics();
        var errors = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        output.WriteLine(string.Join("\n", errors.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        Assert.Equal(["InvalidCompileTimeSwitchItem_Kd", grammarCode, "TypeMismatch_Kd"], errors.Select(static x => x.Code));
        Assert.DoesNotContain(errors[1].Related ?? [], static x => x.Role == "excludedBy");
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
        ParseTestHelper.VerifyParents(c.Kotonoha.RootKoto);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(errors, c.Diagnostics.Finalize(rejected: true).Diagnostics);
    }

    [Theory]
    [InlineData("#if missingName", "UnknownCompileTimeName_Kd")]
    [InlineData("#Tag", null)]
    public void InvalidPrefixesCannotConsumeTheNextCase(string prefix, string? conditionCode)
    {
        var source = "#switch\n    " + prefix + "\n    #case _\n        let missing =\nlet pending =";
        var records = Records(Parse(source));
        output.WriteLine(string.Join("\n", records.Select(static x => x.Code + " " + x.Span + " " + x.Message)));
        string[] expected = conditionCode is null
            ? ["InvalidCompileTimeSwitchItem_Kd", "MissingSyntax_Kd", "MissingSyntax_Kd"]
            : ["InvalidCompileTimeSwitchItem_Kd", conditionCode, "MissingSyntax_Kd", "MissingSyntax_Kd"];
        Assert.Equal(expected, records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(source.IndexOf("missing =", StringComparison.Ordinal) + 9, 0), records[^2].Span);
        Assert.Equal(new SourceSpan(source.Length, 0), records[^1].Span);
    }

    [Fact]
    public void InvalidSwitchItemsEstablishNoDeclarationsOrSemanticChecks()
    {
        const string Source = "#switch\n    func hidden(x: Missing) => unknown\n    #case _\n        ()\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(["InvalidCompileTimeSwitchItem_Kd", "TypeMismatch_Kd"], c.Diagnostics.Finalize(rejected: true).Diagnostics.Select(static x => x.Code));
        Assert.DoesNotContain(ParseTestHelper.GetChildren(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), static x => x.Name == "hidden");
    }

    [Fact]
    public void InvalidSwitchMembersKeepTheirGenericHeaderAndSourceOrder()
    {
        const string Source = "struct S<T>\n    #switch\n        func hidden(x: T)\n            T is Copy\n            return\n        #case _\n            let value: T\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        c.Binding.ReportDiagnostics();
        Assert.Equal(["InvalidCompileTimeSwitchItem_Kd", "DeclarationOrderWarning_Kd", "TypeMismatch_Kd"], c.Diagnostics.Finalize(rejected: true).Diagnostics.Select(static x => x.Code));
        var type = Assert.Single(c.Kotonoha.RootKoto.NestedDeclarationContainers);
        Assert.DoesNotContain(type.Members, static x => x is FunctionKoto or PropertyKoto);
    }

    [Fact]
    public void InvalidSwitchItemsKeepOnlyAnActualEnclosingExclusion()
    {
        const string Source = "#if linux\n#switch\n    let missing =\n    #case _\n        ()";
        var excluded = Records(Parse(Source));
        var selected = Records(Parse(Source, Linux));
        Assert.Equal(["InvalidCompileTimeSwitchItem_Kd", "MissingSyntax_Kd"], excluded.Select(static x => x.Code));
        Assert.All(excluded, x => AssertExclusion(x, Source, "linux"));
        Assert.All(selected, x => AssertExclusion(x, Source, null));
        Assert.Equal(selected.Select(static x => (x.Code, x.Span, x.Message)), excluded.Select(static x => (x.Code, x.Span, x.Message)));
    }

    [Fact]
    public void ValidCaseGroupsExecuteAfterRecoveryChecks()
    {
        const string Source = """
            #switch
                #case false
                    func answer() -> i32 => unknown
                #case _
                    func answer() -> i32
                        #switch
                            #case true
                                return 42
                            #case _
                                return unknown
            Console.writeLine("\(answer())")
            """;
        ScalarEmissionTest.EmitFixture("DirectiveSwitchRecovery", Source, "42\n");
    }

    [Fact]
    public void CliAndLspShowTheInvalidItemAndItsIndependentGrammarError()
    {
        const string Source = "#switch\n    let missing =\n    #case _\n        ()\nlet wrong: i32 = true";
        var path = Path.GetFullPath("switch-recovery.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        var records = c.Diagnostics.Finalize(rejected: true).Diagnostics;
        Assert.Equal(["InvalidCompileTimeSwitchItem_Kd", "MissingSyntax_Kd", "TypeMismatch_Kd"], records.Select(static x => x.Code));
        Assert.Equal(new SourceSpan(Source.IndexOf("missing =", StringComparison.Ordinal) + 9, 0), records[1].Span);
        this.AssertAdapters(c, path);
    }

    [Fact]
    public void CliAndLspRetainTheConditionAndItsExcludingPrefix()
    {
        const string Source = "#if linux\n#if missingName\nfunc f() => ()";
        var path = Path.GetFullPath("directive-recovery.kimi");
        var c = Parse(Source, path: path);
        var record = Assert.Single(Records(c));
        AssertExclusion(record, Source, "linux");
        this.AssertAdapters(c, path);
    }

    private static void AssertExclusion(CheckDiagnostic record, string source, string? name)
    {
        if (name is null)
        {
            Assert.DoesNotContain(record.Related ?? [], static x => x.Role == "excludedBy");
        }
        else
        {
            Assert.Equal(new SourceSpan(source.IndexOf(name, StringComparison.Ordinal), name.Length), Assert.Single(record.Related ?? [], static x => x.Role == "excludedBy").Span);
        }
    }

    private static Compilation Parse(string source, string target = Windows, bool debug = false, string path = "directive-recovery.kimi")
    {
        var c = Compilation.CreateForTest();
        c.Project.KimiOptions.Debug = debug;
        Assert.True(c.Prepare(target));
        c.Kotonoha.AddSource(new SourceDocument(path, source));
        return c;
    }

    private static CheckDiagnostic[] Records(Compilation c)
        => c.Diagnostics.Finalize(DiagnosticPartition.Input, DiagnosticPartition.Emission).Diagnostics;

    private void AssertAdapters(Compilation c, string path)
    {
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(result.Diagnostics.Select(static x => x.Display!.Range!.Value), sent.Select(static x => x.Range));
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
