// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConcreteClosureTest
{
    [Theory]
    [InlineData("ScalarMove", "let n = 7", "n", "@move", SemanticsKind.Owner)]
    [InlineData("TupleMove", "let n = (3, 4)", "n.0 + n.1", "@move", SemanticsKind.Owner)]
    [InlineData("ScalarCopy", "let n = 7", "n", "", SemanticsKind.Ref)]
    [InlineData("TupleCopy", "let n = (3, 4)", "n.0 + n.1", "", SemanticsKind.Ref)]
    public void NestedAcquisitionKeepsTheWrittenMoveRequirement(string name, string declaration, string result, string acquisition, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(declaration + "\nlet outer = func [n] () => func [n" + acquisition + "] () => " + result + "\nlet first = outer()\nlet second = outer()\nrequire first() == 7 and second() == 7 else => $abort(\"nested\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var outer = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous && x.ClosureOf()!.Captures[0].Source.Kind == BindingSymbolKind.Local);
        Assert.Equal(receiver, outer.ClosureOf()!.Receiver);
        ScalarEmissionTest.WriteFixture("ConcreteClosureNested" + name, CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Theory]
    [InlineData("let x: i32 = 4\nlet f = func [x] (y: i32) => x + y\nf(2)", SemanticsKind.Ref)]
    [InlineData("let x: i32 = 4\nvar f = func [var x] () -> i32\n    x += 1\n    return x\nf@uniq()", SemanticsKind.Uniq)]
    [InlineData("func take(text: string) => Console.writeLine(text)\nlet text = \"owned\"\nlet f = func [text@move] () => take(text@move)\nf@move()", SemanticsKind.Owner)]
    public void InfersConcreteEnvironmentAndReceiver(string source, SemanticsKind receiver)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var closure = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>(), x => x.IsAnonymous).ClosureOf()!;
        Assert.Equal(BoundTypeKind.Closure, closure.EnvironmentType!.Kind);
        Assert.Equal(receiver, closure.Receiver);
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var outputIr = CompilationTestHelper.WriteIr(c);
    }

    [Fact]
    public void BindsExclusiveCallableContract()
    {
        var c = MinimalEmissionTest.Analyze("func apply<F>(f: uniq/F) -> i32\n    F is Callable<uniq, () -> i32>\n    return f()\nlet n: i32 = 0\nvar f = func [var n] () -> i32\n    n += 1\n    return n\napply(f@uniq)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("Snapshot", "let n: i32 = 2\nvar f = func [var n] () => ++n\nrequire f@uniq() == 3 and f@uniq() == 4 and n == 2 else => $abort(\"snapshot\")", "")]
    [InlineData("Nested", "let n: i32 = 9\nlet f = func [n] () => func [n] (x: i32) => n + x\nlet g = f()\nlet h: (i32) -> i32 = g\nrequire h(1) == 10 and g(2) == 11 else => $abort(\"nested\")", "")]
    [InlineData("Consume", "func take(text: string) => Console.writeLine(text)\nlet text = \"owned\"\nlet f = func [text@move] () => take(text@move)\nf@move()", "owned\n")]
    [InlineData("UnusedOwned", "let text = \"unused\"\nlet f = func [text@move] () => ()\nf()\nf()", "")]
    [InlineData("Empty", "let f = func () => 17\nlet g: () -> i32 = f\nrequire f() == 17 and g() == 17 else => $abort(\"empty\")", "")]
    [InlineData("Borrowed", "let n: i32 = 0\nvar f = func [var n] () => ++n\nlet borrowed = f@uniq\nrequire borrowed() == 1 and borrowed() == 2 else => $abort(\"borrow\")", "")]
    [InlineData("InspectOwned", "let text = \"owned\"\nlet f = func [text@move] () => text == \"owned\"\nrequire f() and f() else => $abort(\"shared\")", "")]
    [InlineData("Order", "let a = \"first\"\nlet b = \"second\"\nlet f = func [a@move, b@move] () -> ()\n    defer => Console.writeLine(\"defer\")\n    Console.writeLine(a)\n    Console.writeLine(b)\nf()", "first\nsecond\ndefer\n")]

    // An anonymous function in a discarded position or a match arm is still created, and its environment is released.
    [InlineData("DiscardedArm", "let s = \"x\"\nlet n = 1\nmatch n\n    0 => func [s@move] () => ()\n    _ => ()\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("DiscardedHeap", "let a: i64 = 1\nlet b: i64 = 2\nif a == 1\n    func [a, b] (x: i64) -> i64 => x + a + b\nConsole.writeLine(\"ok\")", "ok\n")]
    [InlineData("MovingArm", "let s = \"x\"\nlet n = 0\nlet f: () -> bool = match n\n    0 => func [s@move] () => s == \"x\"\n    _ => func () => false\nrequire f() else => $abort(\"arm\")\nConsole.writeLine(\"ok\")", "ok\n")]

    // A discarded closure keeps the storage its creation writes in a function with patterns (match, for), which prunes unused
    // storage: an inline scalar, two-scalar or wide environment, a block or yield arm, a loop body and a later statement.
    [InlineData("DiscardedScalarArm", "let a: i32 = 1\nlet i: i32 = 0\nmatch i\n    0 => func [a] () => a\n    _ => ()\nConsole.writeLine(\"\\(i)\")", "0\n")]
    [InlineData("DiscardedBlockArms", "let a: i32 = 1\nlet i: i32 = 0\nmatch i\n    0\n        func [a] () => a\n        (func [a] () => a)\n    _ => ()\nlet r = match i\n    0\n        func [a] () => a\n        yield 5\n    _ => 2\nmatch i\n    0 => (func [a] () => a)\n    _ => ()\nConsole.writeLine(\"\\(r)\")", "5\n")]
    [InlineData("DiscardedWideArm", "let a: i64 = 1\nlet b: i64 = 2\nlet i: i32 = 0\nmatch i\n    0 => func [a, b] () => a + b\n    _ => ()\nConsole.writeLine(\"\\(i)\")", "0\n")]
    [InlineData("DiscardedLoopBodies", "let a: i32 = 1\nvar i: i32 = 0\nwhile i < 2\n    match i\n        0 => i += 1\n        _ => func [a] () => a\n    i += 1\nlet xs: [2 of i32] = [1, 2]\nfor x in xs\n    func [a, x] () => a + x\n    func [x] () => x\nConsole.writeLine(\"\\(i)\")", "2\n")]
    [InlineData("DiscardedAfterMatch", "let a: i32 = 1\nlet i: i32 = 0\nmatch i\n    0 => Console.writeLine(\"zero\")\n    _ => ()\nfunc [a] () => a\nConsole.writeLine(\"\\(a)\")", "zero\n1\n")]
    [InlineData("DiscardedInFunction", "func f(i: i32, a: i32) -> i32\n    match i\n        0 => func [a] () => a\n        _ => ()\n    return a\nConsole.writeLine(\"\\(f(0, 3))\")", "3\n")]
    public void EmitsConcreteClosures(string name, string source, string output)
        => ScalarEmissionTest.EmitFixture("ConcreteClosure" + name, source, output);

    [Theory]
    [InlineData("func take(text: string) => ()\nlet text = \"owned\"\nlet f = func [text@move] () => take(text@move)\nf@move()\nf@move()")]
    [InlineData("let text = \"owned\"\nlet f = func [text@move] () => ()\nConsole.writeLine(text)")]
    [InlineData("let text = \"owned\"\nlet f = func [text] () => ()")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => ++n\nf()")]
    [InlineData("let text = \"owned\"\nlet f = func () => Console.writeLine(text)")]
    [InlineData("let n: i32 = 0\nvar f = func [var n] (x: i32) => ++n + x\nf(f(1))")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => (n) = 1\nf()")]
    [InlineData("let n: i32 = 0\nlet f = func [var n] () => ++n\n(f)()")]
    [InlineData("func take(text: string) => ()\nlet text = \"owned\"\nlet f = func [text@move] () => take(text@move)\nlet borrowed = f@ref\nborrowed()")]
    public void RejectsInvalidAcquisition(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete && c.Analysis.IsVerified);
        using var output = new StringWriter();
        Assert.False(c.Generate(output));
        Assert.Empty(output.ToString());
    }

    // SPEC 7.7.2: creating a closure acquires its entries even where its value is discarded, as a statement or a statement
    // match arm, so a later use of a moved entry is MovedPlace_Kd at that use.
    [Theory]
    [InlineData("public func main() -> ()\n    let s = \"x\"\n    let n = 1\n    match n\n        0 => func [s@move] () => ()\n        _ => ()\n    Console.writeLine(s)\n")]
    [InlineData("public func main() -> ()\n    let s = \"x\"\n    let n = 1\n    if n == 0\n        func [s@move] () => ()\n    Console.writeLine(s)\n")]
    public void DiscardedClosuresAcquireTheirEntries(string source)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        var use = source.LastIndexOf("(s)", StringComparison.Ordinal) + 1;
        Assert.Equal((nameof(DiagnosticCode.MovedPlace_Kd), use, 1), (error.Code, error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Theory]
    [InlineData("let x = 0\nlet f = func [var x] () -> i32\n    x += 1\n    return x\nf()")]
    [InlineData("var x = 0\nvar f = func [x] () => x = 1")]
    [InlineData("let x = 0\nlet f = func [x, x] () => x")]
    [InlineData("let x = 0\nlet f = func [] () => x")]
    public void RejectsInvalidConcreteBinding(string source)
        => Assert.False(MinimalEmissionTest.Analyze(source).Binding.Result.IsComplete);

    // SPEC 15.2.3 (DECISIONS BU-73, PLAN G353): a concrete Closure is Owned when its captures are and its fixed signature Origins are
    // proven to outlive `static`. Per-call are only its direct borrowed parameters' omitted Origins, its receiver and its call result;
    // an enclosing function's Origin (written, through a maker's call, a generic pass, an Option or the result), a Type argument's and a
    // nested or aggregate parameter's Origin are fixed. Each such Closure is UnprovenConstraint_Kd at the use (e's
    // only record is Binding's); the twins (per-call, static, `T is Owned`) and the capture controls (g, i) keep dev's verdicts.
    [Theory]
    [InlineData("j", "func f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    return need(inner)", "UnprovenConstraint_Kd: need(inner)")]
    [InlineData("a", "func host() -> ()\n    let outer = func (r: ref/i32) -> i32\n        let inner = func (y: ref/i32 during r) => ()\n        return need(inner)\n    return", "UnprovenConstraint_Kd: need(inner)")]
    [InlineData("b", "func host() -> ()\n    let outer = func (r: ref/i32) -> i32\n        let inner = func (y: ref/i32) => ()\n        return need(inner)\n    return", null)]
    [InlineData("k", "func f(r: ref/i32 during static) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    return need(inner)", null)]
    [InlineData("e", "func h(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func (y: ref/i32 during x) -> i32 => 0\n    let c = outer(p)\n    return need(c@move)", "UnprovenConstraint_Kd: need(c@move)")]
    [InlineData("h", "func h(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func (y: ref/i32) -> i32 => 0\n    let c = outer(p)\n    return need(c@move)", null)]
    [InlineData("g", "func h(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func [x] () -> i32 => 0\n    let c = outer(p)\n    return need(c@move)", "UnprovenConstraint_Kd: need(c@move)")]
    [InlineData("i", "func h(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func [x] (y: ref/i32 during x) -> i32 => 0\n    let c = outer(p)\n    return need(c@move)", "UnprovenConstraint_Kd: need(c@move)")]
    [InlineData("p3b", "struct View {source}\n    let data: ref/i32 during source\n\nfunc g(v: ref/View) -> i32\n    let inner = func (y: ref/i32 during v.source) => ()\n    return need(inner)", "UnprovenConstraint_Kd: need(inner)")]
    [InlineData("p4", "func f(r: ref/i32) -> i32\n    let outer = func (x: ref/i32) => func (y: ref/i32 during r) -> i32 => 0\n    let c = outer(r)\n    return need(c@move)", "UnprovenConstraint_Kd: need(c@move)")]
    [InlineData("p5", "func pass<T>(value: T) -> T\n    return value@move\n\nfunc f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    return need(pass(inner))", "UnprovenConstraint_Kd: need(pass(inner))")]
    [InlineData("p6", "func f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    return need(Option.Some(inner))", "UnprovenConstraint_Kd: need(Option.Some(inner))")]
    [InlineData("p7", "func f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) -> ref/i32 during r => y\n    return need(inner)", "UnprovenConstraint_Kd: need(inner)")]
    [InlineData("p8", "func f(r: ref/i32) -> i32\n    let inner = func (y: ref/i32 during r) => ()\n    inner(r)\n    return 1", null)]
    [InlineData("p10", "func g<T>(v: T) -> i32\n    let c = func (x: T) => ()\n    return need(c)", "UnprovenConstraint_Kd: need(c)")]
    [InlineData("p10b", "func g<T>(v: T) -> i32\n    T is Owned\n    let c = func (x: T) => ()\n    return need(c)", null)]
    [InlineData("q1", "func f() -> i32\n    let c = func (y: ref/(ref/i32)) => ()\n    return need(c)", "UnprovenConstraint_Kd: need(c)")]
    [InlineData("q1s", "func f() -> i32\n    let c = func (y: ref/(ref/i32 during static)) => ()\n    return need(c)", null)]
    [InlineData("q2", "struct S {s}\n    public let data: ref/i32 during s\n\nfunc f() -> i32\n    let c = func (v: ref/S) => ()\n    return need(c)", "UnprovenConstraint_Kd: need(c)")]
    [InlineData("q2s", "struct S {s}\n    public let data: ref/i32 during s\n\nfunc f() -> i32\n    let c = func (v: ref/(S during static)) => ()\n    return need(c)", null)]
    public void AClosureIsOwnedWhenItsFixedSignatureOriginsAreStatic(string id, string declarations, string? record)
        => Assert.Equal(record is null ? [id] : [id, record], [id, .. RawPointerSyntaxTest.Records(NeedOwned + declarations)]);

    // DECISIONS BU-73: a warm rebind of Closures' Owned proofs over their call signatures, of an erasure of a Closure's environment and of
    // stored Properties whose inferred Types hold Closures (FixedAtoms' walk) reuses its storage.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void ClosureSignaturesAndFieldClosuresRebindWithoutAllocating()
    {
        var c = MinimalEmissionTest.Analyze(NeedOwned + """
            struct View {source}
                public let data: ref/i32 during source
                public let cb = (func (y: ref/i32 during source) => (), 1)
                public let mk = func () => func (y: ref/i32 during source) => ()
                public init(data: ref/i32 during source) => self.data = data

            func f(r: ref/i32) -> i32
                let view = View.init(r)
                let inner = func (y: ref/i32 during r) => ()
                let g: (ref/i32 during r) -> () = inner
                let outer = func (x: ref/i32) => func (y: ref/i32 during x) -> i32 => 0
                let c = outer(r)
                let k = need(view.cb.0) + need(view.mk)
                return need(inner) + need(c@move)

            func g<T>(v: T) -> i32
                let c = func (x: T) => ()
                return need(c)

            public func main() => ()

            """);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // A function requiring Owned of its argument (SPEC 15.2.3), the subject of the Owned rows here and in PropertyBindingTest.
    internal const string NeedOwned = "func need<T>(value: T) -> i32\n    T is Owned\n    return 1\n\n";
}
