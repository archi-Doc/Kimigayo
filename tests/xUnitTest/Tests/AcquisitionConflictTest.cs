// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Arc.Unit;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 10.2.2 and 10.6: overload resolution never chooses between Copying a bare Place and newly sharing it.</summary>
public class AcquisitionConflictTest(ITestOutputHelper output)
{
    private const string Types = "struct Node\n    public var number: i32 = 0\nstruct Value\n    Self is Copy\n    public var number: i32 = 0\n";
    private const string Process = "func process(value: Value) -> i32 => 1\nfunc process(value: ref/Value) -> i32 => 2\n";
    private const string Generic = "func f<U>(value: U) -> i32 => 1\nfunc f<U>(value: ref/U) -> i32 => 2\n";
    private const string Combine = "func combine(left: Value, right: ref/Value) -> i32 => 1\nfunc combine(left: ref/Value, right: Value) -> i32 => 2\nlet a = Value.init()\nlet b = Value.init()\n";
    private const string Selection =
        "func process(value: Value) -> i32 => value.number + 10\nfunc process(value: ref/Value) -> i32 => value.number + 20\n" +
        "func inspect(value: ref/Value) -> i32 => value.number\n" +
        "func select(value: Value ! flag: bool) -> i32 => 1\nfunc select(value: ref/Value ! flag: i32) -> i32 => 2\n" +
        "func combine(left: Value, right: ref/Value) -> i32 => 1\nfunc combine(left: ref/Value, right: Value) -> i32 => 2\n" +
        "func f<U>(value: U) -> i32 => 1\nfunc f<U>(value: ref/U) -> i32 => 2\n" +
        "var value = Value.init()\nvalue.number = 3\n" +
        "require process(value@ref) == 23 else => $abort(\"ref\")\n" +
        "require process(value@copy) == 13 else => $abort(\"copy\")\n" +
        "require process(Value.init()) == 10 else => $abort(\"temporary\")\n" +
        "require inspect(value) == 3 and inspect(value@copy) == 3 else => $abort(\"inspect\")\n" +
        "require select(value, flag: true) == 1 and select(value, flag: 0) == 2 else => $abort(\"select\")\n" +
        "require combine(value@copy, value@ref) == 1 else => $abort(\"combine\")\n" +
        "require f<Value>(value@ref) == 2 and f(value@copy) == 1 else => $abort(\"generic\")\n" +
        "let r = value@ref\nrequire process(r) == 23 and process(r@follow@copy) == 13 else => $abort(\"reference\")\n" +
        "require process(value@move) == 13 else => $abort(\"move\")\n" +
        "Console.writeLine(\"ok\")";

    [Theory]
    [InlineData(Process + "let value = Value.init()\nlet a = process(value)", "value")]
    [InlineData("func process(value: Node) -> i32 => 1\nfunc process(value: ref/Node) -> i32 => 2\nlet node = Node.init()\nlet a = process(node)", "node")]
    [InlineData("func inspect(value: i32) -> i32 => 1\nfunc inspect(value: ref/i32) -> i32 => 2\nlet n: i32 = 1\nlet a = inspect(n)", "n")]
    [InlineData(Generic + "let node = Node.init()\nlet a = f(node)", "node")]
    [InlineData(Generic + "func g<T>(x: T) -> i32\n    T is ObjectPayload\n    return f(x)", "x")]
    [InlineData(Generic + "func g<T>(x: T) -> i32\n    T is Copy\n    return f(x)", "x")]
    [InlineData("func tag<T>(value: Option<T>) -> i32 => 1\nfunc tag<T>(value: ref/Option<T>) -> i32 => 2\nfunc forward<T>(value: Option<T>) -> i32\n    return tag(value)", "value")]
    [InlineData("func pick(value: Value) -> i32 => 1\nfunc pick<T>(value: ref/T) -> i32 => 2\nlet v = Value.init()\nlet a = pick(v)", "v")]
    [InlineData(Combine + "let c = combine(a, b)", "a,b")]
    [InlineData(Combine + "let c = combine(a@copy, b)", "b")]
    [InlineData("func named(! first: Value, second: i32) -> i32 => 1\nfunc named(! first: ref/Value, second: i32) -> i32 => 2\nlet v = Value.init()\nlet a = named(second: 1, first: v)", "v")]
    [InlineData(Process + "let value = Value.init()\nlet r = value@ref\nlet a = process(r@follow)", "r@follow")]
    [InlineData(Process + "let value = Value.init()\nlet a = process((value))", "(value)")]
    [InlineData("struct Holder\n    public var value: Value = Value.init()\n" + Process + "let h = Holder.init()\nlet a = process(h.value)", "h.value")]
    [InlineData(Process + "let values: [2 of Value] = [Value.init(), Value.init()]\nlet a = process(values[0])", "values[0]")]
    [InlineData("struct Box\n    public func put(self, value: Value) -> i32 => 1\n    public func put(self, value: ref/Value) -> i32 => 2\nlet box = Box.init()\nlet v = Value.init()\nlet a = box.put(v)", "v")]
    [InlineData("struct Wrapper\n    var number: i32 = 0\n    public init(value: Value)\n        self.number = 1\n    public init(value: ref/Value)\n        self.number = 2\nlet v = Value.init()\nlet w = Wrapper.init(v)", "v")]
    [InlineData("func visit(value: Value, make: () -> i32) -> i32 => 1\nfunc visit(value: ref/Value, make: () -> i64) -> i32 => 2\nlet v = Value.init()\nlet a = visit(v, func () => 1)", "v")]
    public void ConflictsAreReportedOnceAtEachArgument(string source, string arguments)
    {
        var c = Analyze(Types + source);
        var errors = Errors(c);
        Assert.All(errors, static x => Assert.Equal(nameof(DiagnosticCode.AcquisitionRequired_Kd), x.Code));
        Assert.Equal(arguments.Split(','), errors.Select(static x => x.Text));
        Assert.DoesNotContain(c.Diagnostics.Finalize().Diagnostics, static x => x.Code == nameof(DiagnosticCode.PrerequisiteUnavailable_Kd));
    }

    [Theory]
    [InlineData(Process + "let value = Value.init()\nlet a = process(value@ref)\nlet b = process(value@copy)\nlet c = process(Value.init())\nlet d = process(value@move)")]
    [InlineData("func inspect(value: ref/Value) -> i32 => 2\nlet v = Value.init()\nlet a = inspect(v)\nlet b = inspect(v@copy)")]
    [InlineData("func select(value: Value ! flag: bool) -> i32 => 1\nfunc select(value: ref/Value ! flag: i32) -> i32 => 2\nlet v = Value.init()\nlet a = select(v, flag: true)\nlet b = select(v, flag: 0)")]
    [InlineData(Combine + "let c = combine(a@copy, b@ref)")]
    [InlineData(Process + "let value = Value.init()\nlet r = value@ref\nlet a = process(r)\nlet b = process(r@follow@copy)")]
    [InlineData(Generic + "let v = Value.init()\nlet a = f<Value>(v@ref)\nlet b = f(v@copy)\nfunc borrowExplicitly<T>(x: T) -> i32\n    return f<T>(x@ref)")]
    [InlineData("func route(value: obj/Node) -> i32 => 1\nfunc route(value: objref/Node) -> i32 => 2\nfunc visit(value: obj/Node, make: () -> i32) -> i32 => 1\nfunc visit(value: objref/Node, make: () -> i64) -> i32 => 2\nfunc objectCase(handle: obj/Node) -> i32\n    let first = route(handle)\n    let second = visit(handle, func () => 1)\n    return first + second")]
    [InlineData("func take(value: Value) -> i32 => 1\nlet v = Value.init()\nlet a = take(v)")]
    [InlineData("func process(value: Value) -> i32 => 1\nfunc process(value: ref/Value) -> bool => true\nlet value = Value.init()\nlet a: bool = process(value)")]
    [InlineData("func inspect(value: ref/i32) -> () => ()\nfunc inspect(value: uniq/i32) -> () => ()\nvar x: i32 = 0\ninspect(x)\ninspect(x@uniq)")]
    public void ExplicitAndSingleAcquisitionsResolve(string source)
    {
        var c = Analyze(Types + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Empty(Errors(c));
    }

    // SPEC 10.2.2: an explicit borrow does not name a candidate; both generic candidates then take ref/Node exactly.
    [Fact]
    public void ExplicitBorrowOfAGenericPairIsAnOrdinaryAmbiguity()
        => Assert.Equal(nameof(DiagnosticCode.AmbiguousBinding_Kd), Assert.Single(Errors(Analyze(Types + Generic + "let v = Value.init()\nlet a = f(v@ref)"))).Code);

    // SPEC 10.2.2 step 3: without a conflict, a held Copy that fails excludes its candidate, and a call left without one names @move.
    [Fact]
    public void FailedHeldCopyWithoutConflictRequiresTheTransfer()
    {
        var error = Assert.Single(Errors(Analyze(Types + "func take(value: Node) -> i32 => 1\nfunc take(value: i64) -> i32 => 2\nlet node = Node.init()\nlet a = take(node)")));
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
    }

    [Theory]
    [InlineData("func process(value: Node) -> i32 => 1\nfunc process(value: ref/Node) -> i32 => 2\nlet node = Node.init()\nlet a = process(node)", "Node", false, true)]
    [InlineData(Process + "let value = Value.init()\nlet a = process(value)", "Value", true, true)]
    [InlineData(Process + "let value = Value.init()\nlet r = value@ref\nlet a = process(r@follow)", "Value", true, false)]
    [InlineData("func process(value: Node) -> i32 => 1\nfunc process(value: ref/Node) -> i32 => 2\nfunc run(node: ref/Node) -> i32\n    return process(node@follow)", "Node", false, false)]
    public void ConflictStatesTheTypeCandidatesAndConditionalRepairs(string source, string type, bool copy, bool take)
    {
        var c = Analyze(Types + source);
        var error = Assert.Single(Errors(c));
        Assert.Equal($"{type} is either acquired by value or borrowed", error.Label);
        Assert.StartsWith("Append @ref to the whole argument to borrow the Place", error.Advice, StringComparison.Ordinal);
        Assert.Equal(copy, error.Advice!.Contains("@copy", StringComparison.Ordinal));
        Assert.Equal(take, error.Advice.Contains("@move", StringComparison.Ordinal));
        if (copy)
        {
            Assert.Null(error.Note);
        }
        else
        {
            Assert.Equal($"{type} is not proven Copy, so a by-value candidate cannot Copy this Place", error.Note);
        }

        var record = Assert.Single(c.Diagnostics.Finalize().Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Contains(record.Reason!, x => x.Name == "type" && x.Value == type);
        Assert.Equal(2, record.Related!.Length);
        Assert.Contains(record.Related, x => x.Label == $"process acquires it by value as {type}");
        Assert.Contains(record.Related, x => x.Label == $"process borrows it as ref/{type}");
    }

    // An independent error stays visible; an independent expected result excludes both candidates, so no conflict exists; an
    // anonymous body waiting for the call's expectation is a skipped dependent part explained by the conflict (SPEC 10.6).
    [Theory]
    [InlineData(Process + "let value = Value.init()\nlet a = process(value)\nlet wrong: i32 = true", "AcquisitionRequired_Kd,TypeMismatch_Kd")]
    [InlineData(Process + "let a = process(missing)", "UnresolvedBinding_Kd")]
    [InlineData(Process + "let value = Value.init()\nlet a: bool = process(value)", "NoApplicableOverload_Kd")]
    [InlineData("func visit(value: Value, make: () -> i32) -> i32 => 1\nfunc visit(value: ref/Value, make: () -> i64) -> i32 => 2\nlet v = Value.init()\nlet a = visit(v, func () => missing)", "AcquisitionRequired_Kd")]
    public void PrerequisitesAndIndependentErrorsRemainExplained(string source, string codes)
        => Assert.Equal(codes.Split(',').Order(StringComparer.Ordinal), Errors(Analyze(Types + source)).Select(static x => x.Code).Order(StringComparer.Ordinal));

    [Fact]
    public void CliAndLanguageServerKeepTheExplanationAndLocation()
    {
        var path = Path.GetFullPath("AcquisitionConflict.kimi");
        var c = MinimalEmissionTest.Analyze(Types + Process + "let value = Value.init()\nlet a = process(value)", path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.AcquisitionRequired_Kd), record.Code);
        var console = new DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Code, console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Label!, console.Text, StringComparison.Ordinal);
        Assert.Contains(record.Advice!, console.Text, StringComparison.Ordinal);
        Assert.Contains("process borrows it as ref/Value", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        var check = new CheckOutput(CheckOutcome.Completed, c.Binding.Result.IsComplete, TestPresence.No, result);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(check, [identity], identity, related)[identity]);
            Assert.Equal(record.Display!.Range, sent.Range);
            Assert.Equal(record.Code, sent.Code);
            Assert.Contains(record.Label!, sent.Message, StringComparison.Ordinal);
            Assert.Contains(record.Advice!, sent.Message, StringComparison.Ordinal);
            output.WriteLine(System.Text.Json.JsonSerializer.Serialize(sent));
        }

        output.WriteLine(console.Text);
    }

    [Fact]
    public void RebindingKeepsEachArgumentRecord()
    {
        var c = Analyze(Types + Combine + "let c = combine(a, b)");
        var first = Errors(c);
        Assert.Equal(2, first.Length);
        c.Bind();
        c.Binding.ReportDiagnostics();
        Assert.Equal(first, Errors(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmConflictRecordingAllocatesNothing()
    {
        // Expression statements: a failed call bound to a let costs its own derived-failure bookkeeping, unrelated to the conflict.
        var c = CompilationTestHelper.ParseSuccess(Types + "func pair(left: Value, right: ref/Value) -> () => ()\nfunc pair(left: ref/Value, right: Value) -> () => ()\nlet a = Value.init()\nlet b = Value.init()\npair(a, b)\npair(a@copy, b)");
        for (var i = 0; i < 4; i++)
        {
            Assert.False(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmExplicitSelectionAllocatesNothing()
    {
        var c = CompilationTestHelper.ParseSuccess(Types + Combine + "let c = combine(a@copy, b@ref)\n" + Process + "let d = process(a@ref) + process(a@copy) + process(Value.init())");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // SPEC 10.2.2: each explicit acquisition selects its candidate at run time, and a Copy leaves the source usable.
    [Fact]
    public void ExplicitAcquisitionsSelectTheirCandidatesAtRunTime()
        => ScalarEmissionTest.EmitFixture("AcquisitionConflictSelection", Types + Selection, "ok\n");

    private static Compilation Analyze(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        return c;
    }

    private static TestDiagnostic[] Errors(Compilation c)
        => [.. TestDiagnostics.Of(c).Where(static x => x.Severity == DiagnosticSeverity.Error)];

    private sealed class DiagnosticConsole : IConsoleService
    {
        private readonly StringBuilder text = new();

        public string Text => this.text.ToString();

        public bool KeyAvailable => false;

        public bool EnableColor { get; set; }

        public void Write(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void Write(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message);

        public void WriteLine(string? message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public void WriteLine(ReadOnlySpan<char> message, ConsoleColor color = ConsoleColor.Gray) => this.text.Append(message).Append('\n');

        public Task<InputResult> ReadLineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ConsoleKeyInfo ReadKey(bool intercept) => throw new NotSupportedException();
    }
}
