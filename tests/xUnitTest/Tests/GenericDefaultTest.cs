// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

public class GenericDefaultTest
{
    private const string GenericCapture = "func f<T>(x: T, action: () -> T = func [x] () => x) -> T\n    T is Copy and Owned\n    return action()\n";
    private const string Maker = "contract Maker\n    func make() -> Self\nstruct Box\n    Self is Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Box.init(5)\n    drop => Console.writeLine(\"drop\")\nstruct Pt\n    Self is Copy and Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Pt.init(7)\n";
    private const string Evaluate = "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let pending: Option<T> = .Some(T.make())\n        match pending@move\n            .Some(let value) => exit to result 1\n            .None => exit to result 0\n    ) -> i32\n        T is Maker\n        return marker\n";
    private const string Quiet = "struct Quiet\n    Self is Maker\n    public var text: string\n    public init(text: string) => self.text = text@move\n    public func make() -> Self => Quiet.init(\"q\")\n";
    private const string Pair = "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let pair: (Option<T>, string) = (.Some(T.make()), \"s\")\n";
    private const string GenericCall = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = label work: do\n    let copied = copy(x)\n    exit to work copied\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"call\")";

    [Theory]
    [InlineData("CopyScalar", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nrequire f(3) == 3 and f(true) else => $abort(\"copy\")")]
    [InlineData("CopyReference", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nlet n = 7\nlet r = f(n@ref)\nrequire r@follow == 7 else => $abort(\"reference\")")]
    [InlineData("IntegerLiteral", "func f<T>(value: T = 0) -> T\n    T is PrimitiveInteger\n    return value\nrequire f<i32>() == 0 and f<i64>() == 0 else => $abort(\"literal\")")]
    [InlineData("Tuple", "func f<T>(x: T, pair: (T, T) = (x, x)) -> T\n    T is Copy\n    return pair.1\nrequire f(5) == 5 else => $abort(\"tuple\")")]
    [InlineData("FunctionItem", "func identity<T>(x: T) -> T => x@move\nfunc f<T>(x: T, action: (T) -> T = identity) -> T\n    T is Owned\n    return action(x@move)\nrequire f(7) == 7 else => $abort(\"item\")")]
    [InlineData("FunctionLiteral", "func f<T>(x: T, action: (i32) -> i32 = func (n) => n + 1) -> i32\n    T is Owned\n    return action(2)\nrequire f(true) == 3 else => $abort(\"literal\")")]
    [InlineData("FunctionItemTwoTypes", "func identity<T>(x: T) -> T => x@move\nfunc f<T>(x: T, action: (T) -> T = identity) -> T\n    T is Owned\n    return action(x@move)\nrequire f(7) == 7 and f(true) else => $abort(\"types\")")]
    [InlineData("GenericCall", "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = copy(x)) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"call\")")]
    [InlineData("Forwarded", "func f<T>(x: T, y: T = x) -> T\n    T is Copy\n    return y\nfunc g<T>(x: T) -> T\n    T is Copy\n    return f(x)\nrequire g(7) == 7 and g(true) else => $abort(\"forward\")")]
    [InlineData("Capture", "func f<T>(x: T, action: () -> T = func [x] () => x) -> T\n    T is Copy and Owned\n    return action()\nrequire f(7) == 7 and f(true) else => $abort(\"capture\")")]
    [InlineData("Array", "func f<T>(x: T, values: [2 of T] = [x, x]) -> T\n    T is Copy\n    return values[1]\nrequire f(7) == 7 and f(true) else => $abort(\"array\")")]
    [InlineData("Local", "func f<T>(x: T, y: T = label work: do\n    let copied = x\n    exit to work copied\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"local\")")]
    [InlineData("AggregateJoin", "func f<T>(x: T, yes: bool, y: (T, T) = if yes => (x, x) else => (x, x)) -> T\n    T is Copy\n    return y.0\nrequire f(7, true) == 7 and f(true, false) else => $abort(\"join\")")]
    [InlineData("LocalBorrow", "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    let copied = x\n    exit to work inspect(copied@ref)\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"local borrow\")")]
    [InlineData("LocalCapture", "func f<T>(x: T, action: () -> T = label work: do\n    let copied = x\n    exit to work func [copied] () => copied\n) -> T\n    T is Copy and Owned\n    return action()\nrequire f(7) == 7 and f(true) else => $abort(\"local capture\")")]
    [InlineData("Match", "func f<T>(x: T, y: T = match x@copy\n    let captured => captured\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"match\")")]
    [InlineData("TupleCapture", GenericCapture + "let pair = f((3, true))\nrequire pair.0 == 3 and pair.1 else => $abort(\"tuple capture\")")]
    [InlineData("BorrowLocal", "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    var copied = x\n    let r = copied@ref\n    exit to work inspect(r)\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"borrow local\")")]
    public void DefaultsAreInstantiatedAfterUniversalChecking(string name, string source)
        => ScalarEmissionTest.EmitFixture("GenericDefault" + name, source, string.Empty);

    // SPEC 7.2.3, 8.10: a default's replica takes the exact acquisition of its instantiated Types, proved where the outermost
    // omitting call was written; each owner is destroyed exactly once.
    [Theory]
    [InlineData("Owner", Maker + Evaluate + "Console.writeLine(\"\\(Helpers.evaluate(Box.init(1)))\")", "drop\ndrop\n1\n")]
    [InlineData("Copy", Maker + Evaluate + "Console.writeLine(\"\\(Helpers.evaluate(Pt.init(2)))\")", "1\n")]
    [InlineData("Forwarded", Maker + Evaluate + "func forward<U>(value: U) -> i32\n    U is Maker\n    return Helpers.evaluate(value@move)\nfunc copied<U>(value: U) -> i32\n    U is Copy and Maker\n    return Helpers.evaluate(value)\nConsole.writeLine(\"\\(forward(Box.init(1)) + forward(Pt.init(2)) + copied(Pt.init(3)))\")", "drop\ndrop\n3\n")]
    [InlineData("Nested", Maker + Evaluate + "func outer<V>(value: V, total: i32 = Helpers.evaluate(V.make())) -> i32\n    V is Maker\n    return total\nConsole.writeLine(\"\\(outer(Box.init(1)) + outer(Pt.init(2)))\")", "drop\ndrop\ndrop\n2\n")]
    [InlineData("Empty", "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let pending: Option<T> = .None\n        match pending@move\n            .Some(let value) => exit to result 1\n            .None => exit to result 0\n    ) -> i32\n        return marker\nConsole.writeLine(\"\\(Helpers.evaluate(5) + Helpers.evaluate(\"s\"))\")", "0\n")]
    [InlineData("Transfer", Maker + "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let made = T.make()\n        let pending: Option<T> = .Some(made@move)\n        match pending@move\n            .Some(_) => exit to result 1\n            .None => exit to result 0\n    ) -> i32\n        T is Maker\n        return marker\nConsole.writeLine(\"\\(Helpers.evaluate(Box.init(1)) + Helpers.evaluate(Pt.init(2)))\")", "drop\ndrop\n2\n")]
    [InlineData("PartialMove", Maker + Pair + "        let first = pair.0@move\n        Console.writeLine(pair.1)\n        exit to result 1\n    ) -> i32\n        T is Maker\n        return marker\nConsole.writeLine(\"\\(Helpers.evaluate(Box.init(1)) + Helpers.evaluate(Pt.init(2)))\")", "s\ndrop\ndrop\ns\n2\n")]
    public void ReplicaPayloadsTakeTheInstantiatedAcquisition(string name, string source, string stdout)
        => ScalarEmissionTest.EmitFixture("GenericDefaultContext" + name, source, stdout);

    // SPEC 15.6.2: a replica's projection keeps its Loans; moving the whole while a part is borrowed is still a conflict.
    [Fact]
    public void AReplicaPartLoanStillConflicts()
    {
        var c = MinimalEmissionTest.Analyze(Maker + Pair + "        let r = pair.1@ref\n        let whole = pair@move\n        Console.writeLine(r)\n        exit to result 1\n    ) -> i32\n        T is Maker\n        return marker\nlet n = Helpers.evaluate(Box.init(1))");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
    }

    // SPEC 8.4.10, 8.10, 14.8: a residual Subject part of a replica contributes the exact destruction effect of its instantiated
    // Type: none for a Copy part or an effect-free owner, the destructor's effect otherwise.
    [Theory]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n            .None => exit to result 0\n", "Pt", true)]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n            .None => exit to result 0\n", "Quiet", true)]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(_) => exit to result 1\n            .None => exit to result 0\n", "Box", false)]
    [InlineData("Option<T> = .Some(T.make())", "            .Some(let value) => exit to result 1\n            .None => exit to result 0\n", "Box", false)]
    [InlineData("Option<T> = .None", "            .Some(_) if true => exit to result 1\n            _ => exit to result 0\n", "Pt", true)]
    [InlineData("Option<(T, i32)> = .Some((T.make(), 2))", "            .Some((_, let k)) => exit to result k\n            .None => exit to result 0\n", "Pt", true)]
    [InlineData("Option<(T, i32)> = .Some((T.make(), 2))", "            .Some((_, let k)) => exit to result k\n            .None => exit to result 0\n", "Box", false)]
    public void ReplicaResidualsContributeExactEffects(string pending, string arms, string type, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Maker + Quiet + "contract Runner\n    func run(self: ref/Self) -> i32\n        effect confined\n" +
            "group Helpers\n    public func evaluate<T>(sample: ref/T, marker: i32 = label result: do\n        let pending: " + pending + "\n        match pending@move\n" + arms + "    ) -> i32\n        T is Maker\n        return marker\n" +
            "struct Worker\n    Self is Runner\n    public var held: " + type + "\n    public init(held: " + type + ") => self.held = held@move\n    public func run(self: ref/Self) -> i32 => Helpers.evaluate(self.held@ref)\n" +
            "let w = Worker.init(" + type + ".make())\nlet r = w.run()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        if (valid)
        {
            Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        }
        else
        {
            MinimalEmissionTest.AssertEffectBoundRejected(c);
        }
    }

    [Fact]
    public void ReplicaPlacesResolveCopyOrMoveExactly()
    {
        var c = MinimalEmissionTest.Analyze(Maker + Evaluate + "let a = Helpers.evaluate(Box.init(1))\nlet b = Helpers.evaluate(Pt.init(2))");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var places = c.Ownership.Bodies.SelectMany(static x => x.Places).Where(static x => x.DefaultContext >= 0 && x.Type.Name is "Box" or "Pt").ToArray();
        Assert.Contains(places, static x => x.Type.Name == "Box");
        Assert.All(places, static x => Assert.Equal(x.Type.Name == "Pt" ? AcquisitionKind.Copy : AcquisitionKind.Move, x.Acquisition));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void GenericEnvironmentIsAllocatedAndFreedOnce()
        => NativeAllocationAudit.WriteFixture("GenericDefaultEnvironment", GenericCapture + "let value = f((1, 2, 3))\nrequire value.2 == 3 else => $abort(\"environment\")", 1, 1, 12);

    [Fact]
    public void RebindingAndReloadKeepCallContexts()
    {
        var c = MinimalEmissionTest.Analyze(GenericCall);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(c, error));
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        Assert.True(restored.Emission.WriteIr(TextWriter.Null, out error), MinimalEmissionTest.Describe(restored, error));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\nf(7, 1)\nf(true, false)")]
    public void DefaultLocalLoansAreCheckedEvenWithoutEvaluation(string calls)
    {
        const string Declaration = "func inspect<T>(r: ref/T) -> T\n    T is Copy\n    return r@follow\nfunc f<T>(x: T, y: T = label work: do\n    var copied = x\n    let r = copied@ref\n    copied = x\n    exit to work inspect(r)\n) -> T\n    T is Copy\n    return y\n";
        var source = Declaration + (calls.Length == 0 ? "public func main() => ()" : calls);
        var path = Path.GetFullPath("generic-default.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, issue => issue.Failure == OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.Validate(out _));
        c.Ownership.ReportDiagnostics();
        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), DiagnosticSeverity.Error, DiagnosticCategory.Language), (record.Code, record.Severity, record.Category));
        Assert.Equal(source.IndexOf("copied = x\n    exit", StringComparison.Ordinal), record.Span!.Value.Start);
        Assert.Contains(record.Related!, related => related.Role == "loan" && related.Span is { } span && source.Substring(span.Start, span.Length) == "let r = copied@ref");
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains(record.Message, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains(record.Message, sent.Message, StringComparison.Ordinal);
        }
    }

    [TestClass(DisableParallelization = true)]
    [Trait("Purpose", "Allocation")]
    public class AllocationTests
    {
        [Theory]
        [InlineData(GenericCapture + "require f(7) == 7 and f(true) else => $abort(\"capture\")")]
        [InlineData(GenericCall)]
        public void WarmGenericDefaultsAllocateNothing(string source)
        {
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }
}
