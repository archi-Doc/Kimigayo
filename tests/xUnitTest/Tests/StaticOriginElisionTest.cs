// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class StaticOriginElisionTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData("ref/i32", "")]
    [InlineData("(ref/i32)", "")]
    [InlineData("ref/i32 during static", "")]
    [InlineData("objref/Cell", "struct Cell\n")]
    [InlineData("View", "struct View {source}\n    public let value: ref/i32 during source\n")]
    public void SharedStaticOriginsCompleteBeforeTheRuntimeSupportGate(string type, string declarations)
    {
        var source = declarations + "group Values\n    public let view: " + type + " = make()\n    func make() -> " + type + " => $abort(\"unused\")\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(source);
        var field = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(static x => x.BoundSymbol?.Name == "view");
        Assert.NotNull(field.TypeOf());
        var origin = field.TypeOf()!.Origin ?? Assert.Single(field.TypeOf()!.OriginArguments);
        Assert.Same(BoundOrigin.Static, origin);
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(field.TypeOf()!, field));
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("Unsupported_Kd", record.Code);
    }

    [Fact]
    public void AStaticAggregateWithNoBorrowRequirementDefaultsItsSlot()
    {
        const string Source = "struct Empty {source}\ngroup Values\n    public let value: Empty = make()\n    func make() -> Empty => $abort(\"unused\")\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var field = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<PropertyKoto>().Single(static x => x.BoundSymbol?.Name == "value");
        Assert.Same(BoundOrigin.Static, Assert.Single(field.TypeOf()!.OriginArguments));
    }

    [Theory]
    [InlineData("uniq/i32", "", "MissingOriginBinding_Kd")]
    [InlineData("View", "struct View {source}\n    public let value: uniq/i32 during source\n", "InvalidOriginBinding_Kd")]
    public void ExclusiveStaticOriginsAreRejected(string type, string declarations, string code)
    {
        var c = MinimalEmissionTest.Analyze(declarations + "group Values\n    public var view: " + type + "\nlet present = 1");
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(code, Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("ref", false)]
    [InlineData("uniq", false)]
    [InlineData("raw", true)]
    public void WarmStaticOriginChecksReuseStorage(string semantics, bool valid)
    {
        var source = "group Values\n    public let value: " + semantics + "/i32 = make()\n    func make() -> " + semantics + "/i32 during static => $abort(\"unused\")\nlet present = 1";
        if (semantics == "raw")
        {
            source = source.Replace(" during static", string.Empty, StringComparison.Ordinal);
        }

        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal(valid, c.Binding.Result.IsComplete);
    }

    [Fact]
    public void ReplacingTheStoredTypeRemovesTheObsoleteSupportLimit()
    {
        const string Source = "group Values\n    public let view: ref/i32 = make()\n    func make() -> ref/i32 => $abort(\"unused\")\nlet present = 1";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        var group = c.Kotonoha.RootKoto.NestedDeclarationContainers.OfType<GroupKoto>().Single();
        var old = KotoTree.Walk(group).OfType<PropertyKoto>().Single();
        var donor = CompilationTestHelper.ParseSuccess("group Values\n    public let view: i32 = 7");
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<PropertyKoto>().Single();
        Assert.True(KotoHelper.Replace(group, old, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
    }

    [Fact]
    public void EqualInvalidTypesAtIndependentAnnotationsRemainSeparateFailures()
    {
        const string Source = "struct View {source}\n    public let value: uniq/i32 during source\nfunc make() -> View during static => $abort(\"unused\")\nlet value: View during static = make()\nlet wrong: i32 = true";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        Assert.Equal(["InvalidOriginBinding_Kd", "InvalidOriginBinding_Kd", "TypeMismatch_Kd"], TestDiagnostics.Of(c).Select(static x => x.Code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicDiagnosticsDistinguishTheLanguageRuleFromTheImplementationLimit(bool exclusive)
    {
        var declarations = exclusive ? "struct View {source}\n    public let value: uniq/i32 during source\n" : string.Empty;
        var field = exclusive ? "public var view: View" : "public let view: ref/i32 = make()\n    func make() -> ref/i32 => $abort(\"unused\")";
        var source = declarations + "group Values\n    " + field + "\nlet wrong: i32 = true";
        var path = Path.GetFullPath("static-origin-elision.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var code = exclusive ? "InvalidOriginBinding_Kd" : "Unsupported_Kd";
        Assert.Equal([code, "TypeMismatch_Kd"], result.Diagnostics.Select(static x => x.Code));
        var record = result.Diagnostics[0];
        Assert.Equal(exclusive ? DiagnosticCategory.Language : DiagnosticCategory.Unsupported, record.Category);
        if (exclusive)
        {
            const string Reason = "static cannot supply uniq for Origin slot source";
            Assert.Equal(Reason, record.Label);
            Assert.Equal(Reason, Assert.Single(record.Reason!, static x => x.Name == "reason").Value);
            Assert.Contains("requires uniq", record.Note);
            Assert.Equal(new SourceSpan(source.LastIndexOf("View", StringComparison.Ordinal), 4), record.Span);
            var related = Assert.Single(record.Related!);
            Assert.Equal("requires uniq", related.Label);
            Assert.Equal(new SourceSpan(source.IndexOf("source", StringComparison.Ordinal), 6), related.Span);
        }
        else
        {
            Assert.Equal(new SourceSpan(source.IndexOf("let view", StringComparison.Ordinal), "let view: ref/i32 = make()".Length), record.Span);
            Assert.Equal((null, null, null), (record.Reason, record.Note, record.Label));
        }

        Assert.Empty(record.Repairs ?? []);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var records = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity];
            Assert.Equal(code, records[0].Code);
            Assert.Equal(record.Display!.Range!.Value, records[0].Range);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(records));
        }
    }
}
