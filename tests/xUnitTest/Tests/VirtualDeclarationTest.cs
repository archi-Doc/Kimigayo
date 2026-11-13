// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;
using static XunitTest.ParseTestHelper;

namespace XunitTest;

public class VirtualDeclarationTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1")]
    [InlineData("struct Derived : Base\n    override func read(self: objref/Self) -> i32 => 2")]
    [InlineData("open struct Base<T>\n    public virtual func read(self: objref/Self, value: ref/T) -> i32 => 1")]
    public void ContextualFunctionModifiersRoundTrip(string source)
    {
        var tree = ParseSuccess(source);
        AssertValid(Parse(Unparse(tree)));
        Assert.Contains(source.Contains("override", StringComparison.Ordinal) ? "override" : "virtual", Unparse(tree), StringComparison.Ordinal);
        Assert.All(KotoTree.Walk(tree.RootKoto).Where(x => !ReferenceEquals(x, tree.RootKoto)), x => Assert.NotNull(x.Parent));
        var restored = Tinyhand.TinyhandSerializer.Deserialize<Kotonoha>(Tinyhand.TinyhandSerializer.Serialize(tree))!;
        restored.OnDeserialized(Compilation.CreateForTest());
        AssertValid(restored);
        Assert.Equal(Unparse(tree), Unparse(restored));
    }

    [Fact]
    public void OrdinaryNamesDoNotBecomeModifiers()
    {
        var tree = ParseSuccess("func virtual() -> i32 => 1\nfunc override() -> i32 => virtual()\nlet value = override()");
        AssertValid(Parse(Unparse(tree)));
    }

    [Theory]
    [InlineData("objref")]
    [InlineData("objuniq")]
    public void ValidDeclarationsAreRetainedWithoutStaticExecution(string receiver)
    {
        var source = $"open struct Base<T>\n    public virtual func read(self: {receiver}/Self, value: ref/T) -> i32 => 1\nstruct Derived : Base<i32>\n    override func read(self: {receiver}/Self, value: ref/i32) -> i32 => 2\n()";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.All(c.Binding.Issues, x => Assert.Equal(DiagnosticCode.Unsupported_Kd, x.Code));
        Assert.Equal(receiver == "objref" ? 0 : 2, c.Binding.Issues.Count);
        Assert.Equal(receiver == "objref", c.Binding.Result.IsComplete);
        Assert.Equal(receiver == "objref", c.Generate(TextWriter.Null));
        Assert.Equal(2, KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Count(x => x.IsVirtual || x.IsOverride));
    }

    // DECISIONS BU-111: the Reason is the closed cause; its explanation is the whole Note, never a bounded Reason value.
    [Theory]
    [InlineData("virtual func read() => ()", "NotStructFunction", "direct struct")]
    [InlineData("group G\n    virtual func read() => ()", "NotStructFunction", "direct struct")]
    [InlineData("func outer()\n    virtual func read() => ()", "NotStructFunction", "direct struct")]
    [InlineData("struct Base\n    public virtual func read(self: objref/Self) => ()", "NotOpen", "open struct")]
    [InlineData("open struct Base\n    virtual func read(self: objref/Self) => ()", "Private", "private")]
    [InlineData("open struct Base\n    private virtual func read(self: objref/Self) => ()", "Private", "private")]
    [InlineData("open struct Base\n    public virtual func read(self) => ()", "Receiver", "objref/Self")]
    [InlineData("open struct Base\n    public virtual func read() => ()", "Receiver", "objref/Self")]
    [InlineData("open struct Base\n    public virtual func read<T>(self: objref/Self) => ()", "OwnGenericParameters", "enclosing Type parameters remain allowed")]
    [InlineData("open struct Base\n    #LibraryImport(\"codec\", \"touch\")\n    public virtual func read(self: objref/Self) -> i32", "NoBody", "requires a body")]
    [InlineData("struct Derived<T>\n    override func read(self: objref/Self)\n        T is Copy\n        ()", "RedeclaredConstraints", "inherits constraints")]
    public void InvalidHeadersExplainTheRuleAtTheModifier(string source, string cause, string fact)
    {
        var c = MinimalEmissionTest.Analyze(source + "\n()");
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, x => x.Code == "InvalidVirtualDeclaration_Kd");
        var expected = source.IndexOf("virtual", StringComparison.Ordinal);
        if (expected < 0)
        {
            expected = source.IndexOf("override", StringComparison.Ordinal);
        }

        Assert.Equal(expected, record.Span!.Value.Start);
        Assert.Equal(new DiagnosticValue("cause", DiagnosticValueKind.Enumeration, cause), Assert.Single(record.Reason!));
        Assert.Null(record.Label);
        Assert.Contains(fact, record.Note!, StringComparison.Ordinal);
        Assert.DoesNotContain("effects", record.Note!, StringComparison.Ordinal); // An override's effect item is its own record (DECISIONS G223).
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.Unsupported_Kd);
        Assert.False(c.Generate(TextWriter.Null));
        output.WriteLine(record.Message + "\n" + record.Note);
    }

    // SPEC F.6.3 and 19.4: an OverrideDeclaration takes no Access, other modifier, Type parameters, "!" boundary, parameter default or
    // parameter attribute; each is a grammar error with the same record in selected and excluded syntax, and Binding judges none again.
    [Theory]
    [InlineData("public override func read(self: objref/Self, n: i32) -> i32 => n", "public")]
    [InlineData("unsafe override func read(self: objref/Self, n: i32) -> i32 => n", "override")]
    [InlineData("virtual override func read(self: objref/Self, n: i32) -> i32 => n", "override")]
    [InlineData("override func read<T>(self: objref/Self, n: i32) -> i32 => n", "read<")]
    [InlineData("override func read(self: objref/Self ! n: i32) -> i32 => n", "read(")]
    [InlineData("override func read(self: objref/Self, n: i32 = 1) -> i32 => n", "read(")]
    [InlineData("override func read(self: objref/Self, #Test n: i32) -> i32 => n", "read(")]
    public void OverrideHeadersAreGrammarInEverySelection(string header, string at)
    {
        foreach (var excluded in new[] { false, true })
        {
            var source = "open struct Base\n    public virtual func read(self: objref/Self, n: i32) -> i32 => n\nstruct Derived : Base\n" +
                (excluded ? "    #if false\n        " + header + "\n    override func read(self: objref/Self, n: i32) -> i32 => n\n" : "    " + header + "\n") +
                "public func main() => ()\n";
            var c = MinimalEmissionTest.Analyze(source);
            c.Binding.ReportDiagnostics();
            var records = c.Diagnostics.Finalize().Diagnostics;
            var record = Assert.Single(records, x => x.Code == "MisplacedSyntax_Kd");
            Assert.Equal(source.IndexOf(header, StringComparison.Ordinal) + header.IndexOf(at, StringComparison.Ordinal), record.Span!.Value.Start);
            Assert.DoesNotContain(records, x => x.Code == "InvalidVirtualDeclaration_Kd");
        }
    }

    // SPEC F.3.3 and 19.4: a function header writes its access specification, then at most one of unsafe, virtual and override. A
    // second modifier is a grammar error at the second word, and an access word after a modifier one at that word (the first of a
    // compound; the declaration keeps its access): the same record in selected and excluded syntax, and Binding judges the virtual
    // declaration no further.
    [Theory]
    [InlineData("public virtual unsafe func read(self: objref/Self) => ()", "unsafe", "second function modifier")]
    [InlineData("public unsafe virtual func read(self: objref/Self) => ()", "virtual", "second function modifier")]
    [InlineData("unsafe override func read(self: objref/Self) => ()", "override", "second function modifier")]
    [InlineData("unsafe virtual override func read(self: objref/Self) => ()", "virtual", "second function modifier")]
    [InlineData("virtual public func read(self: objref/Self) => ()", "public", "access word after a modifier")]
    [InlineData("virtual private protected func read(self: objref/Self) => ()", "private", "access word after a modifier")]
    [InlineData("virtual protected internal func read(self: objref/Self) => ()", "protected", "access word after a modifier")]
    [InlineData("unsafe public func read(self: objref/Self) => ()", "public", "access word after a modifier")]
    [InlineData("unsafe internal func read(self: objref/Self) => ()", "internal", "access word after a modifier")]
    [InlineData("unsafe private protected func read(self: objref/Self) => ()", "private", "access word after a modifier")]
    [InlineData("unsafe protected internal func read(self: objref/Self) => ()", "protected", "access word after a modifier")]
    public void AFunctionHeaderOutsideItsOrderIsGrammarInEverySelection(string header, string at, string phrase)
    {
        foreach (var excluded in new[] { false, true })
        {
            var source = "open struct Base\n" + (excluded ? "    #if false\n        " : "    ") + header + "\npublic func main() => ()\n";
            var c = MinimalEmissionTest.Analyze(source);
            c.Binding.ReportDiagnostics();
            var records = c.Diagnostics.Finalize().Diagnostics;
            var record = Assert.Single(records);
            Assert.Equal("MisplacedSyntax_Kd", record.Code);
            Assert.Contains(phrase, record.Message, StringComparison.Ordinal);
            Assert.Equal(source.IndexOf(header, StringComparison.Ordinal) + header.IndexOf(at, StringComparison.Ordinal), record.Span!.Value.Start);
            Assert.Equal(at.Length, record.Span!.Value.Length);
        }
    }

    // The valid twins: each access specification before its one modifier has no record in either selection.
    [Theory]
    [InlineData("public virtual")]
    [InlineData("private protected virtual")]
    [InlineData("protected internal virtual")]
    [InlineData("public unsafe")]
    [InlineData("internal unsafe")]
    [InlineData("private protected unsafe")]
    [InlineData("protected internal unsafe")]
    public void AFunctionHeaderInItsOrderHasNoRecordInEverySelection(string words)
    {
        foreach (var excluded in new[] { false, true })
        {
            var c = MinimalEmissionTest.Analyze("open struct Base\n" + (excluded ? "    #if false\n        " : "    ") + words + " func read(self: objref/Self) => ()\npublic func main() => ()\n");
            c.Binding.ReportDiagnostics();
            Assert.Empty(c.Diagnostics.Finalize().Diagnostics);
        }
    }

    [Fact]
    public void ExcludedDeclarationsAndDuplicateModifiersKeepOrdinarySyntaxRules()
    {
        AssertValid(Parse("#if false\nvirtual func excluded() => ()\nlet value = 1"));
        const string Source = "open struct Base\n    public virtual virtual func read(self: objref/Self) => ()\nstruct Following";
        var tree = Parse(Source);
        var error = Assert.Single(TestDiagnostics.Of(tree));
        Assert.Equal("DuplicateModifier_Kd", error.Code);
        Assert.Equal(Source.LastIndexOf("virtual", StringComparison.Ordinal), error.Span.Start);
        Assert.Contains(tree.RootKoto.NestedContainers, x => x.Name == "Following");
    }

    [Theory]
    [InlineData("effect confined\n        return 1", false)]
    [InlineData("effect confined\n        effect confined\n        return 1", true)]
    public void EffectClausesUseTheCommonDeclarationChecks(string body, bool rejected)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32\n        " + body + "\n()");
        Assert.Equal(rejected, c.Binding.Issues.Any(x => x.Code == DiagnosticCode.InvalidEffectBound_Kd));
        Assert.Equal(!rejected, c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.DoesNotContain(TestDiagnostics.Of(c), x => x.Code == "CheckFaulted_Kd");
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void RepeatedDeclarationCheckingReusesStorage()
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public virtual func read(self: objref/Self) -> i32 => 1\n()");
        var complete = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => complete &= c.Bind().IsComplete));
        Assert.True(complete);
        Assert.Empty(c.Binding.Issues);
    }

    [Fact]
    public void ModifierReasonSurvivesCliAndLspAndIndependentErrors()
    {
        const string Source = "open struct Base\n    public virtual func read(self) => ()\nlet independent = missing";
        var path = Path.GetFullPath("virtual-declaration.kimi");
        var c = MinimalEmissionTest.Analyze(Source, path);
        c.Binding.ReportDiagnostics();
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, x => x.Code == "InvalidVirtualDeclaration_Kd");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("objref/Self", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity], x => x.Code == record.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Contains("objref/Self", sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }
}
