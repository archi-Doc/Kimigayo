// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
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
    private const string Callable = "group Helpers\n    public func evaluate<T, F>(sample: T, factory: ref/F, marker: T = factory()) -> T\n        F is Callable<() -> T>\n        return marker@move\n";
    private const string Quiet = "struct Quiet\n    Self is Maker\n    public var text: string\n    public init(text: string) => self.text = text@move\n    public func make() -> Self => Quiet.init(\"q\")\n";
    private const string Requirement = "group Helpers\n    public func evaluate<T>(a: T, same: bool = a.equals(a)) -> bool\n        T is Equatable\n        return same\n";
    private const string Pair = "group Helpers\n    public func evaluate<T>(sample: T, marker: i32 = label result: do\n        let pair: (Option<T>, string) = (.Some(T.make()), \"s\")\n";
    private const string GenericCall = "func copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc f<T>(x: T, y: T = label work: do\n    let copied = copy(x)\n    exit to work copied\n) -> T\n    T is Copy\n    return y\nrequire f(7) == 7 and f(true) else => $abort(\"call\")";

    private enum ReplicaShape
    {
        Body,
        Default,
        Nested,
        Forwarded,
        Copied,
        SemanticsBody,
        SemanticsDefault,
        Repetition,
    }

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
    [InlineData("Callable", Callable + "let f = func [] () -> i32 => 7\nlet g = func [] () -> string => \"seven\"\nConsole.writeLine(\"\\(Helpers.evaluate(0, f@ref))\")\nConsole.writeLine(Helpers.evaluate(\"x\", g@ref))", "7\nseven\n")]
    [InlineData("CallableForwarded", Callable + "func outer<G>(g: ref/G) -> i32\n    G is Callable<() -> i32>\n    return Helpers.evaluate(0, g)\nlet f = func [] () -> i32 => 7\nConsole.writeLine(\"\\(outer(f@ref))\")", "7\n")]
    [InlineData("CallablePayload", "group Helpers\n    public func evaluate<T, F>(sample: T, factory: ref/F, marker: i32 = label result: do\n        let pending: Option<T> = .Some(factory())\n        match pending@move\n            .Some(_) => exit to result 1\n            .None => exit to result 0\n    ) -> i32\n        F is Callable<() -> T>\n            effect confined\n        return marker\ncontract Runner\n    func run(self: ref/Self) -> i32\n        effect confined\nstruct Worker\n    Self is Runner\n    public func run(self: ref/Self) -> i32\n        let factory = func [] () -> i32 => 7\n        return Helpers.evaluate(0, factory@ref)\nlet w = Worker.init()\nConsole.writeLine(\"\\(w.run())\")", "1\n")]
    [InlineData("Requirement", Requirement + "Console.writeLine(\"\\(Helpers.evaluate(\"x\"))\")\nConsole.writeLine(\"\\(Helpers.evaluate(4))\")", "true\ntrue\n")]
    [InlineData("RequirementForwarded", Requirement + "func forward<T>(value: T) -> bool\n    T is Equatable\n    return Helpers.evaluate(value@move)\nConsole.writeLine(\"\\(forward(\"x\"))\")\nConsole.writeLine(\"\\(forward(4))\")", "true\ntrue\n")]
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

    // SPEC 7.2.3, 8.10: a default is an instantiation of its verified declaration. Each snippet is placed mechanically in an ordinary
    // generic body and in every default context shape; with an accepted declaration, every cell is accepted with the same result and
    // destructor order. Owner cells, Default cells and repetitions run natively; the coverage record lists every cell.
    [Theory]
    [InlineData("Some")]
    [InlineData("Bound")]
    [InlineData("Empty")]
    [InlineData("Transfer")]
    [InlineData("Partial")]
    [InlineData("Guard")]
    [InlineData("Tuple")]
    [InlineData("Requirement")]
    public void ReplicasAgreeWithTheirBody(string name)
    {
        var snippet = ReplicaSnippet.Find(name);
        var declaration = MinimalEmissionTest.Analyze(ReplicaSnippet.Prelude + snippet.Declare(ReplicaShape.Default) + "public func main() => ()");
        Assert.True(declaration.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(declaration, null));
        var record = new StringBuilder();
        var failures = new StringBuilder();
        foreach (var shape in ReplicaSnippet.Shapes)
        {
            foreach (var type in snippet.Types)
            {
                record.Append(name).Append('\t').Append(shape).Append('\t').Append(type).Append('\t');
                if (ReplicaSnippet.Skip(shape, type) is { } reason)
                {
                    record.Append("skipped\t").Append(reason).Append('\n');
                    continue;
                }

                var source = ReplicaSnippet.Prelude + snippet.Declare(shape) + ReplicaSnippet.Run(shape, type);
                var native = type == "Box" || shape is ReplicaShape.Default or ReplicaShape.Repetition;
                using var writer = native ? new StringWriter() : TextWriter.Null;
                if (Check(source, writer) is { } failure)
                {
                    failures.Append(shape).Append(' ').Append(type).Append(": ").Append(failure).Append('\n');
                    record.Append("failed\n");
                    continue;
                }

                if (native)
                {
                    ScalarEmissionTest.WriteFixture("GenericDefaultContext" + name + shape + type, writer.ToString()!, snippet.Expected(shape, type));
                }

                record.Append(native ? "native\n" : "checked\n");
            }
        }

        File.WriteAllText(Path.Combine(ScalarEmissionTest.FixtureDirectory(), "GenericDefaultContext" + name + ".matrix.tsv"), record.ToString());
        Assert.True(failures.Length == 0, failures.ToString());

        // An internal fault is recorded with its cell, so the remaining cells still run.
        static string? Check(string source, TextWriter writer)
        {
            try
            {
                var c = MinimalEmissionTest.Analyze(source);
                string? error = null;
                return c.Ownership.Result.IsVerified && c.Emission.WriteIr(writer, out error) ? null : MinimalEmissionTest.Describe(c, error);
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
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

        // Replica contexts are reused records: sibling expansions and nested defaults rebuild them without allocation.
        [Theory]
        [InlineData("siblings", 8)]
        [InlineData("depth", 4)]
        public void WarmDefaultContextsAllocateNothing(string axis, int count)
        {
            var c = MinimalEmissionTest.Analyze(Verification.VerificationWorkloads.GenericDefaults(axis, count));
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), MinimalEmissionTest.Describe(c, error));
            Assert.Equal(axis == "depth" ? 2 * (count + 1) : count, c.Ownership.Bodies.Max(static x => x.DefaultContexts?.Count ?? 0));
            var valid = true;
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
            Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
            Assert.True(valid);
        }
    }

    // A default's value: its block lines (relative indentation), the lines it prints, whether it destroys one made Box (`drop 5`)
    // and its result, the sample's `n` when SampleResult is set.
    private sealed record ReplicaSnippet(string Name, bool Maker, string Head, string[] Lines, string[] Printed, bool MadeDrop, bool SampleResult, int Result)
    {
        internal const string Prelude = "contract Maker\n    func make() -> Self\n    func size(self: ref/Self) -> i32\n" +
            "struct Box\n    Self is Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Box.init(5)\n    public func size(self: ref/Self) -> i32 => self.n\n    drop => Console.writeLine(\"drop \\(self.n)\")\n" +
            "struct Pt\n    Self is Copy and Maker\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func make() -> Self => Pt.init(5)\n    public func size(self: ref/Self) -> i32 => self.n\n" +
            "struct Quiet\n    Self is Maker\n    public var text: string\n    public var n: i32\n    public init(n: i32)\n        self.text = \"q\"\n        self.n = n\n    public func make() -> Self => Quiet.init(5)\n    public func size(self: ref/Self) -> i32 => self.n\n";

        internal static readonly ReplicaShape[] Shapes = Enum.GetValues<ReplicaShape>();

        private static readonly string[] MakerTypes = ["Box", "Pt", "Quiet"];
        private static readonly string[] AnyTypes = ["Box", "Pt", "Quiet", "i32", "string"];
        private static readonly string[] Arms = ["match pending@move", "    .Some(_) => exit to result 1", "    .None => exit to result 0"];
        private static readonly string[] BoundArms = ["match pending@move", "    .Some(let value) => exit to result 1", "    .None => exit to result 0"];
        private static readonly ReplicaSnippet[] All =
        [
            new("Some", true, "label result: do", ["let pending: Option<T> = .Some(T.make())", .. Arms], [], true, false, 1),
            new("Bound", true, "label result: do", ["let pending: Option<T> = .Some(T.make())", .. BoundArms], [], true, false, 1),
            new("Empty", false, "label result: do", ["let pending: Option<T> = .None", .. BoundArms], [], false, false, 0),
            new("Transfer", true, "label result: do", ["let made = T.make()", "let pending: Option<T> = .Some(made@move)", .. Arms], [], true, false, 1),
            new("Partial", true, "label result: do", ["let pair: (Option<T>, string) = (.Some(T.make()), \"s\")", "let first = pair.0@move", "Console.writeLine(pair.1)", "exit to result 1"], ["s"], true, false, 1),
            new("Guard", true, "label result: do", ["let pending: Option<T> = .Some(T.make())", "match pending@move", "    .Some(_) if true => exit to result 1", "    _ => exit to result 0"], [], true, false, 1),
            new("Tuple", true, "label result: do", ["let pending: Option<(T, i32)> = .Some((T.make(), 2))", "match pending@move", "    .Some((_, let k)) => exit to result k", "    .None => exit to result 0"], [], true, false, 2),
            new("Requirement", true, "sample.size()", [], [], false, true, 0),
        ];

        internal string[] Types => this.Maker ? MakerTypes : AnyTypes;

        internal static ReplicaSnippet Find(string name) => Array.Find(All, x => x.Name == name)!;

        internal static string? Skip(ReplicaShape shape, string type) => shape switch
        {
            ReplicaShape.Nested when type is "i32" or "string" => "the outer default makes its sample through Maker",
            ReplicaShape.Copied when type is not ("Pt" or "i32") => "requires Copy evidence",
            ReplicaShape.Repetition when type == "Box" => "pairs Box with each other Type",
            _ => null,
        };

        // The program's statements: each shape calls with a fresh sample `n = 1`; Semantics shapes also lend a held one, and
        // repetitions pair Box with the Type in both orders.
        internal static string Run(ReplicaShape shape, string type) => shape switch
        {
            ReplicaShape.Nested => Print("outer(" + Value(type, 1) + ")"),
            ReplicaShape.Forwarded => Print("forward(" + Value(type, 1) + ")"),
            ReplicaShape.Copied => Print("copied(" + Value(type, 1) + ")"),
            ReplicaShape.SemanticsBody or ReplicaShape.SemanticsDefault => Print("Helpers.evaluate(" + Value(type, 2) + ")") + "let held = " + Value(type, 1) + "\n" + Print("Helpers.evaluate(held@ref)"),
            ReplicaShape.Repetition => Print("Helpers.evaluate(Box.init(1)) + Helpers.evaluate(" + Value(type, 2) + ")") + Print("Helpers.evaluate(" + Value(type, 2) + ") + Helpers.evaluate(Box.init(1))"),
            _ => Print("Helpers.evaluate(" + Value(type, 1) + ")"),
        };

        // `Helpers.evaluate` with the snippet as its body's value or as its omitted default, then the shape's generic caller.
        internal string Declare(ReplicaShape shape)
        {
            var semantics = shape is ReplicaShape.SemanticsBody or ReplicaShape.SemanticsDefault;
            var constraints = (semantics ? "        s is owner or ref\n" : string.Empty) + (this.Maker ? "        T is Maker\n" : string.Empty);
            var text = new StringBuilder("group Helpers\n    public func evaluate").Append(semantics ? "<s/T>(sample: s/T" : "<T>(sample: T");
            if (shape is ReplicaShape.Body or ReplicaShape.SemanticsBody)
            {
                text.Append(") -> i32\n").Append(constraints).Append("        let marker: i32 = ").Append(this.Head).Append('\n');
                this.Block(text, "            ");
            }
            else
            {
                text.Append(", marker: i32 = ").Append(this.Head);
                if (this.Lines.Length == 0)
                {
                    text.Append(") -> i32\n");
                }
                else
                {
                    text.Append('\n');
                    this.Block(text, "        ");
                    text.Append("    ) -> i32\n");
                }

                text.Append(constraints);
            }

            text.Append("        return marker\n");
            var forwarded = this.Maker ? "    U is Maker\n" : string.Empty;
            return (shape switch
            {
                ReplicaShape.Nested => text.Append("func outer<V>(value: V, total: i32 = Helpers.evaluate(V.make())) -> i32\n    V is Maker\n    return total\n"),
                ReplicaShape.Forwarded => text.Append("func forward<U>(value: U) -> i32\n").Append(forwarded).Append("    return Helpers.evaluate(value@move)\n"),
                ReplicaShape.Copied => text.Append("func copied<U>(value: U) -> i32\n    U is Copy\n").Append(forwarded).Append("    return Helpers.evaluate(value)\n"),
                _ => text,
            }).ToString();
        }

        // The standard output every placement must produce: the snippet's lines and destructions, the sample's destruction where
        // the callee owns it, then the result.
        internal string Expected(ReplicaShape shape, string type)
        {
            var text = new StringBuilder();
            switch (shape)
            {
                case ReplicaShape.Nested:
                    this.Output(text, type);
                    Drop(text, type, 5);
                    Drop(text, type, 1);
                    text.Append(this.ResultOf(5)).Append('\n');
                    break;
                case ReplicaShape.SemanticsBody or ReplicaShape.SemanticsDefault:
                    this.Output(text, type);
                    Drop(text, type, 2);
                    text.Append(this.ResultOf(2)).Append('\n');
                    this.Output(text, type);
                    text.Append(this.ResultOf(1)).Append('\n');
                    Drop(text, type, 1);
                    break;
                case ReplicaShape.Repetition:
                    this.Output(text, "Box");
                    Drop(text, "Box", 1);
                    this.Output(text, type);
                    Drop(text, type, 2);
                    text.Append(this.ResultOf(1) + this.ResultOf(2)).Append('\n');
                    this.Output(text, type);
                    Drop(text, type, 2);
                    this.Output(text, "Box");
                    Drop(text, "Box", 1);
                    text.Append(this.ResultOf(2) + this.ResultOf(1)).Append('\n');
                    break;
                default:
                    this.Output(text, type);
                    Drop(text, type, 1);
                    text.Append(this.ResultOf(1)).Append('\n');
                    break;
            }

            return text.ToString();
        }

        private static string Print(string call) => "Console.writeLine(\"\\(" + call + ")\")\n";

        private static string Value(string type, int n) => type switch
        {
            "i32" => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "string" => "\"s" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"",
            _ => type + ".init(" + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        };

        private static void Drop(StringBuilder text, string type, int n)
        {
            if (type == "Box")
            {
                text.Append("drop ").Append(n).Append('\n');
            }
        }

        private int ResultOf(int n) => this.SampleResult ? n : this.Result;

        private void Output(StringBuilder text, string type)
        {
            foreach (var line in this.Printed)
            {
                text.Append(line).Append('\n');
            }

            if (this.MadeDrop)
            {
                Drop(text, type, 5);
            }
        }

        private void Block(StringBuilder text, string indent)
        {
            foreach (var line in this.Lines)
            {
                text.Append(indent).Append(line).Append('\n');
            }
        }
    }
}
