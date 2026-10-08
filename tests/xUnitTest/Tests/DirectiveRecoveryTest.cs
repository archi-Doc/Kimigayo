// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
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

    [Fact]
    public void CliAndLspRetainTheConditionAndItsExcludingPrefix()
    {
        const string Source = "#if linux\n#if missingName\nfunc f() => ()";
        var path = Path.GetFullPath("directive-recovery.kimi");
        var c = Parse(Source, path: path);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        AssertExclusion(record, Source, "linux");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(record.Display!.Range!.Value, Assert.Single(sent).Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
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
}
