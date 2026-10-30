// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.7.2: contextual self and a setter's value are never captured implicitly, and explicit captures of them obey the
// receiver, accessor, construction and destruction restrictions; SPEC 16.3.1: no Closure in a drop body captures the helper borrow.
public class ContextualCaptureTest
{
    private const string Counter = "struct Counter\n    public var count: i32\n    public init(count: i32) => self.count = count\n\n";

    // SPEC 16.3.1: a destructor whose body each row supplies, with Fields and helpers to pass the helper borrow to.
    private const string Destructor = "struct Buffer\n    public var n: i32 = 0\n    public init() => ()\nstruct Inner\n    public var s: string = \"\"\n    public init() => ()\n" +
        "group Audit\n    public func inspect(value: ref/Connection) => ()\n    public func observe(value: ref/Buffer) => ()\n    public func see<T>(value: ref/T) => ()\n" +
        "    public func same(value: ref/Connection) -> ref/Connection => value\n    public func run(f: () -> ()) => f()\n" +
        "struct Connection\n    var buffer: Buffer\n    var count: i32\n    var inner: Inner\n" +
        "    public init()\n        self.buffer = Buffer.init()\n        self.count = 0\n        self.inner = Inner.init()\n" +
        "    public func peek(self: ref/Self) -> i32 => self.count\n    public func me(self: ref/Self) -> ref/Self => self\n    drop\n";

    // The helper borrow captured by a Closure that calls it, f, and the records each later use of f adds.
    private const string Rejected = Destructor + "        let w = self@ref\n        let f = func [w] () => Audit.inspect(w)\n";

    [Theory]
    [InlineData(Counter + "    public func peek(self: ref/Self) -> i32\n        let read = func () => self.count\n        return read()\n", "self")]
    [InlineData(Counter + "    public func bump(self: uniq/Self) -> ()\n        var step = func () => self.count += 1\n        step()\n", "self")]
    [InlineData(Counter + "    public func take(self) -> i32\n        let read = func () => self.count\n        return read()\n", "self")]
    [InlineData(Counter + "    public func reader(self: ref/Self) -> () -> i32\n        return func () => self.count\n", "self")]
    [InlineData(Counter + "    public func nested(self: ref/Self) -> i32\n        let outer = func () => (func () => self.count)()\n        return outer()\n", "self")]
    [InlineData("struct Box\n    public var item: i32\n        set(self: uniq/Self, value: i32) -> ()\n            let read = func () => value + 1\n            storage = read()\n    public init(item: i32) => self.item = item\n", "value")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop\n        let show = func () => self.id\n        Console.writeLine(\"\\(show())\")\n", "self")]
    public void AnOmittedCaptureListNeverCapturesAContextualBinding(string declarations, string name)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(declarations, path);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.Equal(name, error.Text); // Located at the use.
        Assert.Contains("never captured implicitly", error.Note, StringComparison.Ordinal);
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticCategory.Language, record.Category);
        Assert.Equal("declaration", Assert.Single(record.Related!).Role);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("never captured implicitly", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity]);
            Assert.Equal(record.Code, sent.Code);
            Assert.Equal(record.Display!.Range, sent.Range);
        }
    }

    [Theory]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32)\n        self.id = id\n        let peek = func [self] () => self.id\n        _ = peek()\n")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32)\n        self.id = id\n        let peek = func [self@move] () => self.id\n        _ = peek@move()\n")]
    [InlineData("struct Res\n    public var id: i32\n    public init(id: i32) => self.id = id\n    drop\n        let show = func [self@ref] () => self.id\n        _ = show()\n")]
    public void AnExplicitCaptureOfSelfObeysConstructionAndDestruction(string declarations)
    {
        var c = MinimalEmissionTest.Analyze(declarations);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.StartsWith("self", error.Text, StringComparison.Ordinal); // Located at the capture entry.
        Assert.Contains("reached only through its Fields", error.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitCapturesOfSelfAndAnOrdinaryValueRun()
    {
        const string Source = Counter + "    public func bump(self: uniq/Self) -> i32\n        var step = func [self] () => self.count += 1\n        step()\n        step()\n        return self.count\n\n" +
            "    public func peek(self: ref/Self) -> i32\n        let read = func [self] () => self.count\n        return read()\n\n" +
            "    public func outer(self: ref/Self) -> i32\n        let read = func [self] () => (func () => self.count)()\n        return read()\n\n" +
            "var c = Counter.init(1)\nConsole.writeLine(\"\\(c.bump())\")\nConsole.writeLine(\"\\(c.peek())\")\nConsole.writeLine(\"\\(c.outer())\")\nlet value = 4\nlet add = func () => value + 1\nConsole.writeLine(\"\\(add())\")";
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("ContextualCaptureExplicit", Source, "3\n3\n3\n5\n");
    }

    // SPEC 16.3.1, 15.6, 15.6.5: a Closure created in a drop body captures neither the helper borrow self@ref nor a value that carries
    // it: a Copy, a retaining call result, an aggregate, a Borrow through it, a Pattern binding of the whole receiver or a var whose
    // region it bounds, judged without flow. The record is at the written entry or at the binding's first occurrence in the Closure.
    [Theory]
    [InlineData("        let w = self@ref\n        let f = func [w] () => Audit.inspect(w)\n        f()\n", "w] ()")]
    [InlineData("        let w = self@ref\n        let f = func () => Audit.inspect(w)\n        f()\n", "w)\n")]
    [InlineData("        let w = Audit.same(self)\n        let f = func [w] () => Audit.inspect(w)\n        f()\n", "w] ()")]
    [InlineData("        let w = Audit.same(self@ref)\n        let f = func () => Audit.inspect(w)\n        f()\n", "w)\n")]
    [InlineData("        let w = self.me()\n        let f = func [w] () => Audit.inspect(w)\n        f()\n", "w] ()")]
    [InlineData("        let w = self@ref\n        let f = func [w@ref] () => Audit.see(w)\n        f()\n", "w@ref] ()")]
    [InlineData("        let w = self@ref\n        let f = func [w@move] () => Audit.inspect(w)\n        f()\n", "w@move] ()")]
    [InlineData("        let w = self@ref\n        var f = func [var w] () => Audit.inspect(w)\n        f()\n", "var w] ()")]
    [InlineData("        let w: ref/Connection = self@ref\n        let f = func [w] () => Audit.inspect(w)\n        f()\n", "w] ()")]
    [InlineData("        var v: ref/Connection = self@ref\n        let f = func [v] () => Audit.inspect(v)\n        f()\n", "v] ()")]
    [InlineData("        let t = (self@ref, 1)\n        let f = func [t] () => Audit.inspect(t.0)\n        f()\n", "t] ()")]
    [InlineData("        let w = self@ref\n        let b = w.buffer@ref\n        let f = func [b] () => Audit.observe(b)\n        f()\n", "b] ()")]
    [InlineData("        let w = self@ref\n        let r = w@follow@ref\n        let f = func [r] () => Audit.inspect(r)\n        f()\n", "r] ()")]
    [InlineData("        var v = self.buffer@ref\n        let w = self@ref\n        v = w.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n", "v] ()")]
    [InlineData("        let w = self@ref\n        let f = func [w] () => ()\n        f()\n", "w] ()")]
    [InlineData("        let w = self@ref\n        let f = func () => (func [w] () => Audit.inspect(w))()\n        f()\n", "w] ()")]
    [InlineData("        let w = self@ref\n        let f = func () => (func () => Audit.inspect(w))()\n        f()\n", "w))()")]
    [InlineData("        match self\n            let x => (func [x] () => Audit.inspect(x))()\n", "x] ()")]
    [InlineData("        var v = self.buffer@ref\n        let w = self@ref\n        v = if self.count > 0 => w.buffer@ref else => self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n", "v] ()")]
    [InlineData("        let mk = func (x: ref/Connection) => func [x] () => Audit.inspect(x)\n        let g = mk(self@ref)\n        let h = func [g] () => g()\n        h()\n", "g] ()")]
    public void AClosureInADropBodyNeverCapturesTheHelperBorrow(string body, string at)
    {
        var source = Destructor + body;
        var errors = PublishedRecords(source);
        var error = Assert.Single(errors);
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
        Assert.Contains("helper borrow", error.Note, StringComparison.Ordinal);
    }

    // SPEC 16.3.1, 7.7.2: the helper borrow passed to helpers, used by a defer, held in a local aggregate (stored means beyond the body),
    // passed to a Closure's parameter or held by a helper's Closure result, and a Field borrow captured, in a drop or a constructor, are
    // valid (the owner's readings OQ3 and N5).
    [Theory]
    [InlineData(Destructor + "        let whole = self@ref\n        Audit.inspect(whole)\n        let k = self.peek()\n        let w = self.me()\n        Audit.inspect(w)\n")]
    [InlineData(Destructor + "        defer => Audit.inspect(self)\n        let w = self@ref\n        defer => Audit.inspect(w)\n")]
    [InlineData(Destructor + "        let b = self.buffer@ref\n        let f = func [b] () => Audit.observe(b)\n        let g = func () => Audit.observe(b)\n        f()\n        g()\n")]
    [InlineData(Destructor + "        let t = (self.buffer@ref, 1)\n        let s = self.inner.s@ref\n        let f = func [t, s] () => Audit.see(s)\n        f()\n")]
    [InlineData(Destructor + "        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        v = self.buffer@ref\n        Audit.observe(v)\n")]
    [InlineData(Destructor + "        let f = func (x: ref/Connection) => Audit.inspect(x)\n        f(self@ref)\n")]
    [InlineData(Destructor + "        var arr: Array<ref/Connection> = []\n        arr.append(self@ref)\n        let t = (self@ref, 1)\n")]
    [InlineData(Destructor + "        let mk = func (x: ref/Connection) => func [x] () => Audit.inspect(x)\n        let g = mk(self@ref)\n        g()\n")]
    [InlineData(Destructor + "        let k = self.peek()\nstruct Counter\n    var count: i32\n    public init()\n        self.count = 0\n        let b = self.count@ref\n        let f = func [b] () => Audit.see(b)\n        f()\n")]
    public void TheHelperBorrowOutsideACaptureAndACapturedFieldBorrowAreValid(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 23.1: each record is a Language error with the helper borrow's Note and the captured binding's declaration related; a capture
    // site relates the value that brought the helper borrow into its var's region (borrow) unless that is the var's initializer, and a
    // late source relates every capture it reaches (capture); console and LSP place them.
    [Theory]
    [InlineData("        let w = self@ref\n        let f = func [w] () => Audit.inspect(w)\n        f()\n", 1, "declaration")]
    [InlineData("        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        let w = self@ref\n        v = w.buffer@ref\n", 1, "capture,declaration")]
    [InlineData("        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        let g = func () => Audit.observe(v)\n        f()\n        g()\n        let w = self@ref\n        v = w.buffer@ref\n", 1, "capture,capture,declaration")]
    [InlineData("        var v = self.buffer@ref\n        let w = self@ref\n        v = w.buffer@ref\n        let f = func () => Audit.observe(v) + Audit.observe(v)\n", 2, "borrow,declaration")]
    public void TheHelperBorrowCaptureRecordIsPublished(string body, int count, string roles)
    {
        var path = Path.GetFullPath("Hello.kimi");
        var c = MinimalEmissionTest.Analyze(Destructor + body, path);
        c.Binding.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize(rejected: true);
        Assert.Equal(count, result.Diagnostics.Length);
        foreach (var record in result.Diagnostics)
        {
            Assert.Equal((DiagnosticCategory.Language, nameof(DiagnosticCode.InvalidCaptureBinding_Kd)), (record.Category, record.Code));
            Assert.Equal(roles, string.Join(',', record.Related!.Select(static x => x.Role).Order(StringComparer.Ordinal)));
            Assert.Contains("helper borrow", record.Note, StringComparison.Ordinal);
        }

        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("helper borrow", console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var capability in new[] { false, true })
        {
            var sent = WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, capability)[identity].ToArray();
            Assert.Equal(count, sent.Length);
            for (var i = 0; i < count; i++)
            {
                Assert.Equal((result.Diagnostics[i].Display!.Range, result.Diagnostics[i].Code), (sent[i].Range, sent[i].Code));
            }
        }
    }

    // A Closure rejected where it captures the helper borrow is a failure its later uses rest on: called, copied, passed on or captured
    // by another Closure, each pipeline publishes exactly its one record, and the MIR records no internal fault.
    [Theory]
    [InlineData("        f()\n")]
    [InlineData("        let h = f\n        h()\n")]
    [InlineData("        Audit.see(f@ref)\n")]
    [InlineData("        let g = func [f] () => f()\n        g()\n")]
    public void ALaterUseOfARejectedClosureAddsNoRecord(string uses)
    {
        var error = Assert.Single(PublishedRecords(Rejected + uses));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.StartsWith("w] ()", (Rejected + uses)[error.Span.Start..], StringComparison.Ordinal);
    }

    // SPEC 15.6.5, 16.3.1: a var captured before a later value brings the helper borrow into its region (OQ2: judged on Origins, without
    // flow, in a loop too) is one record, after the body is bound, at the value whose bound enters the helper borrow into the region graph
    // that reaches the var: an assignment, a meet, a call result, or another var's earlier initializer; the Closure stays a resolved value.
    [Theory]
    [InlineData("        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        let w = self@ref\n        v = if self.count > 0 => w.buffer@ref else => self.buffer@ref\n        Audit.observe(v)\n", "if self.count > 0 => w.buffer@ref")]
    [InlineData("        let other = Connection.init()\n        var v = other@ref\n        let t = if self.count > 0 => v else => other@ref\n        let f = func [t] () => Audit.inspect(t)\n        f()\n        v = Audit.same(self)\n", "Audit.same(self)\n")]
    [InlineData("        let w = self@ref\n        var u = w.buffer@ref\n        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        v = u\n        Audit.observe(v)\n", "w.buffer@ref\n        var v")]
    [InlineData("        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        var u = self@ref\n        v = u.buffer@ref\n", "self@ref\n        v = u")]
    [InlineData("        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        let w = self@ref\n        v = w.buffer@ref\n        Audit.observe(v)\n", "w.buffer@ref\n")]
    [InlineData("        var v = self.buffer@ref\n        let f = func () => Audit.observe(v)\n        f()\n        let w = self@ref\n        v = w.buffer@ref\n", "w.buffer@ref\n")]
    [InlineData("        var v = self.buffer@ref\n        var n = 0\n        while n < 2\n            let f = func [v] () => Audit.observe(v)\n            f()\n            let w = self@ref\n            v = w.buffer@ref\n            n += 1\n", "w.buffer@ref\n")]
    public void AVarCapturedBeforeTheHelperBorrowReachesItFailsAtThatValue(string body, string at)
    {
        var source = Destructor + body;
        var error = Assert.Single(PublishedRecords(source));
        Assert.Equal(nameof(DiagnosticCode.InvalidCaptureBinding_Kd), error.Code);
        Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
        Assert.Contains("which a Closure captures", error.Note, StringComparison.Ordinal);

        var c = MinimalEmissionTest.Analyze(source);
        Assert.All(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Where(static x => x.IsAnonymous), static x => Assert.NotNull(x.ClosureOf()));
    }

    // SPEC 16.3.1, 23.1.2 (DECISIONS R4-M152): the helper-capture judgment reads a Closure's definition, so in a drop body a Closure whose
    // erasure to a common Function Type fails (its creation failed) still has its capture judged: a later helper-borrow value of the
    // captured var is InvalidCaptureBinding_Kd, the independent record its created twin has (BU's zz3_h2 and zz3_h2ok).
    [Theory]
    [InlineData(
        "        var v = self.buffer@ref\n        Audit.run(func [v] () => Audit.observe(v))\n        let w = self@ref\n        v = w.buffer@ref\n",
        "UnprovenConstraint_Kd func [v] () => Audit.observe(v); InvalidCaptureBinding_Kd w.buffer@ref")]
    [InlineData(
        "        var v = self.buffer@ref\n        let f = func [v] () => Audit.observe(v)\n        f()\n        let w = self@ref\n        v = w.buffer@ref\n",
        "InvalidCaptureBinding_Kd w.buffer@ref")]
    public void AClosureWhoseCreationFailedHasItsCapturesJudged(string body, string records)
    {
        var source = Destructor + body;
        var found = PublishedRecords(source).OrderBy(static x => x.Span.Start)
            .Select(x => $"{x.Code} {source.Substring(x.Span.Start, x.Span.Length)}");
        Assert.Equal(records, string.Join("; ", found));
    }

    // One failure per node: a Closure argument rejected at its capture is not judged again at its erasure to a common Function Type,
    // which still rejects the Closure argument whose capture is valid (a Field borrow is not Owned, SPEC 7.7.4).
    [Theory]
    [InlineData("        let w = self@ref\n        Audit.run(func [w] () => Audit.inspect(w))\n", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "w] ()")]
    [InlineData("        let b = self.buffer@ref\n        Audit.run(func [b] () => Audit.observe(b))\n", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "func [b]")]
    public void AClosureArgumentHasOneFailure(string body, string code, string at)
    {
        var source = Destructor + body;
        var c = MinimalEmissionTest.Analyze(source);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal(code, error.Code);
        Assert.StartsWith(at, source[error.Span.Start..], StringComparison.Ordinal);
    }

    // SPEC 7.7.2 (DECISIONS R4-M117, item 11): a static Property's storage is static storage, so a Closure or a nested function in its
    // accessor reads and writes it as a static access, never a capture, which Binding accepts (the old analysis's located Unsupported_Kd
    // at the storage Place is the accessor's own, P42); a Borrow of it there has a static root, Binding's located Unsupported_Kd until P42
    // (only the accessor's own Borrow binds, DECISIONS BU-25); an instance Property's storage is never captured.
    [Theory]
    [InlineData("group Totals\n    public let limit: i32 = 5\n        get() -> i32\n            let read = func () -> i32 => storage\n            return read()\n", null, null)]
    [InlineData("group Totals\n    public var level: i32 = 0\n        get() -> i32 => 1\n        set(value: i32) -> ()\n            var write = func [] () => storage = 3\n            write()\n", null, null)]
    [InlineData("group Totals\n    public var count: i32 = 0\n        get() -> i32\n            func helper() -> i32 => storage\n            return helper()\n", null, null)]
    [InlineData("group Totals\n    public var count: i32 = 0\n        get() -> i32\n            let read = func () -> i32\n                let r = storage@ref\n                return r@follow\n            return read()\n", nameof(BindingFailure.Unsupported), "storage@ref")]
    [InlineData("struct S\n    public init() => ()\n    public var w: i32 = 0\n        get() -> i32\n            let read = func () -> i32 => storage\n            return read()\n", nameof(BindingFailure.Capture), "storage")]
    public void AStaticPropertysStorageIsAStaticAccessInsideItsAccessor(string declarations, string? failure, string? at)
    {
        var c = CompilationTestHelper.ParseSuccess(declarations + "public func main() => ()\n", "Hello.kimi");
        c.Bind();
        Assert.Equal(failure is null ? [] : [(Enum.Parse<BindingFailure>(failure), at!)], c.Binding.Issues.Select(static x => (x.Node.FailureOf(), x.Node.ToString())));
    }

    // The Binding and analysis records a program publishes, which records no internal fault; every pipeline publishes the same (DECISIONS
    // R5-M156), so one compilation of the process's pipeline judges them (PLAN G314).
    internal static TestDiagnostic[] PublishedRecords(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.AddSource(new SourceDocument("Hello.kimi", source));
        c.Bind();
        c.Binding.CheckStartup(OutputKind.Application);
        c.Analyze();
        c.Binding.ReportDiagnostics();
        c.ReportAnalysis();
        Assert.DoesNotContain(c.Problems, static x => x.Code == DiagnosticCode.InternalInvariant_Kd);
        return TestDiagnostics.Of(c);
    }

    [Collection<AllocationIsolationCollection>]
    [Trait("Purpose", "Allocation")]
    public sealed class AllocationTests
    {
        // The judgment walks a drop body that forms the helper borrow and reads a var's region on every pass; a warm rebind allocates nothing.
        [Fact]
        public void JudgingHelperCapturesAllocatesNothingWarm()
        {
            var c = CompilationTestHelper.ParseSuccess(Destructor + "        let whole = self@ref\n        Audit.inspect(whole)\n        var v = self.buffer@ref\n        let b = self.buffer@ref\n        let f = func [b, v] () => Audit.observe(v)\n        f()\n");
            Assert.True(c.Bind().IsComplete);
            Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        }
    }
}
