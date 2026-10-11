// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 11.2, 11.4: an accessor receiver has the shape of its operation, ref/Self for an instance get and uniq/Self for an instance set.</summary>
public sealed class AccessorReceiverShapeTest(ITestOutputHelper output)
{
    private const string Meter = "struct Meter\n    var hits: i32 = 0\n    public computed reading: i32\n        get(self: uniq/Self) -> i32\n            self.hits += 1\n            return self.hits\n";
    private const string Point = "struct Point\n    Self is Copy\n    var raw: i32 = 0\n    public computed x: i32\n        get() -> i32 => self.raw\n        set(self: Self, value: i32) -> () => ()\n";

    [Theory]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: Self) -> i32 => 1", "get", "Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: uniq/Self) -> i32 => 1", "get", "uniq/Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: obj/Self) -> i32 => 1", "get", "obj/Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: rc/Self) -> i32 => 1", "get", "rc/Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: arc/Self) -> i32 => 1", "get", "arc/Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: objref/Self) -> i32 => 1", "get", "objref/Self", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: objuniq/Self) -> i32 => 1", "get", "objuniq/Self", "ref/Self")]
    [InlineData("struct Api<T> {source}\n    public computed item: i32\n        get(self: uniq/Self during a) -> i32 => 1", "get", "uniq/Self during a", "ref/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(self: Self, value: i32) -> () => ()", "set", "Self", "uniq/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(self: ref/Self, value: i32) -> () => ()", "set", "ref/Self", "uniq/Self")]
    [InlineData("struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(self: objuniq/Self, value: i32) -> () => ()", "set", "objuniq/Self", "uniq/Self")]
    [InlineData("struct Api\n    public var item: i32 = 0\n        get(self: uniq/Self) -> i32 => storage", "get", "uniq/Self", "ref/Self")]
    [InlineData("struct Api\n    public var item: i32 = 0\n        set(self: ref/Self, value: i32) -> () => ()", "set", "ref/Self", "uniq/Self")]
    [InlineData("contract Api\n    property item: i32\n        get(self: Self) -> i32", "get", "Self", "ref/Self")]
    [InlineData("contract Api\n    property item: i32\n        get(self: ref/Self) -> i32\n        set(self: Self, value: i32) -> ()", "set", "Self", "uniq/Self")]
    public void TheWrittenReceiverIsRejectedAtTheDeclaration(string source, string accessor, string written, string required)
    {
        var c = Analyze(source);
        var error = Assert.Single(Errors(c));
        Assert.Equal(nameof(DiagnosticCode.AccessorReceiverShape_Kd), error.Code);
        Assert.Equal(written, error.Text);
        Assert.Equal($"a {accessor} receiver is {required}", error.Label);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal([("accessor", accessor), ("written", written), ("required", required)], record.Reason!.Select(static x => (x.Name, x.Value)));
        var related = Assert.Single(record.Related!);
        Assert.Equal("property", related.Role);
        Assert.Equal("item is read through ref/Self and written through uniq/Self", related.Label);
        Assert.DoesNotContain(result.Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Theory]
    [InlineData("struct Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(value: i32) -> () => ()")]
    [InlineData("struct Api\n    public computed item: i32\n        get(self: ref/Self) -> i32 => 1\n        set(self: uniq/Self, value: i32) -> () => ()")]
    [InlineData("struct Api<T> {source}\n    public computed item: i32\n        get(self: ref/Self during static) -> i32 => 1")]
    [InlineData("struct Api\n    public var item: i32 = 0\n        get(self: ref/Self) -> i32 => storage\n        set(self: uniq/Self, value: i32) -> () => storage = value")]
    [InlineData("contract Api\n    property item: i32 has get, set")]
    [InlineData("contract Api\n    property item: i32\n        get(self: ref/Self) -> i32\n        set(self: uniq/Self, value: i32) -> ()")]
    [InlineData("group Api\n    public computed item: i32\n        get() -> i32 => 1\n        set(value: i32) -> () => ()")]
    public void TheFixedShapesAreAccepted(string source)
    {
        var c = Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(Errors(c));
    }

    // SPEC 11.2: the body is checked with the written receiver and the Property stays in member lookup. A use that only the
    // written shape rejects (a let or shared receiver of a uniq/Self getter, an owning setter receiver) rests on the declaration;
    // a use the fixed shape also rejects, or that fails for another reason, is reported on its own.
    [Theory]
    [InlineData(Meter + "var meter = Meter.init()\nlet a = meter.reading", "")]
    [InlineData(Meter + "let fixed = Meter.init()\nlet c = fixed.reading", "")]
    [InlineData(Meter + "func read(meter: ref/Meter) -> i32 => meter.reading", "")]
    [InlineData(Meter + "let fixed = Meter.init()\nlet wrong: bool = fixed.reading", "")]
    [InlineData(Meter + "var meter = Meter.init()\nlet wrong: bool = meter.reading", "TypeMismatch_Kd")]
    [InlineData(Meter + "var meter = Meter.init()\nlet a = meter.missing", "UnresolvedBinding_Kd")]
    [InlineData(Point + "var point = Point.init()\npoint.x = 10", "")]
    [InlineData(Point + "let point = Point.init()\nlet x = point.x", "")]
    public void UsesRestOnTheDeclarationOnlyWhereTheWrittenShapeAloneRejectsThem(string source, string others)
    {
        var c = Analyze(source);
        var codes = Errors(c).Select(static x => x.Code).ToArray();
        Assert.Equal(nameof(DiagnosticCode.AccessorReceiverShape_Kd), codes[0]);
        Assert.Equal(others.Length == 0 ? [] : others.Split(','), codes[1..]);
        Assert.DoesNotContain(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Fact]
    public void CliAndLanguageServerShowTheShapeAndTheProperty()
    {
        var path = Path.GetFullPath("AccessorReceiverShape.kimi");
        var c = MinimalEmissionTest.Analyze(Meter, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AccessorReceiverShape_Kd), record.Code);
        Assert.Equal(new SourceRange(new(3, 18), new(3, 27)), record.Display!.Range);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("AccessorReceiverShape.kimi:4:19", console.Text, StringComparison.Ordinal);
        Assert.Contains("^^^^^^^^^ a get receiver is ref/Self", console.Text, StringComparison.Ordinal);
        Assert.Contains("reading is read through ref/Self and written through uniq/Self", console.Text, StringComparison.Ordinal);
        output.WriteLine(console.Text);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, false, TestPresence.No, result);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Display.Range, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains(record.Label!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }
    }

    [Fact]
    public void RebindingAndReloadKeepTheShapeError()
    {
        var c = Analyze(Meter + "let fixed = Meter.init()\nlet c = fixed.reading");
        var first = Errors(c);
        Assert.Single(first);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(first, Errors(c));
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.Equal(c.Binding.Issues.Select(static x => x.Code), restored.Binding.Issues.Select(static x => x.Code));
    }

    // Expression statements: a derived read bound to a let costs its own derived-failure bookkeeping, unrelated to the shape check.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmShapeRejectionAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess(Meter + Point + "let fixed = Meter.init()\nfixed.reading\nvar meter = Meter.init()\nmeter.reading\nvar point = Point.init()\npoint.x = 10");
        for (var i = 0; i < 4; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];
}
