// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 7.2.3, 7.6.4: a default of a closed common Function Type erases a closed Function Item, or an anonymous function whose
// call is Shared and whose entries Copy preceding parameters. Each omission evaluates the default at the call, against the
// slots that call prepared, and delivers one owned value to the parameter.
public class FunctionDefaultTest
{
    private const string Prefix = "FunctionDefault";

    private const string Steps = "func inc(value: i32) -> i32 => value + 1\nfunc dec(value: i32) -> i32 => value - 1\n";

    private const string Heap = "func runE(a: i64, b: i64, action: (i64) -> i64 = func [a, b] (x) => x + a + b) -> i64 => action(0)\n";

    private const string Scalars = "func run(k: i32, action: (i32) -> i32 = func [k] (v) => v + k) -> i32 => action(0)\nfunc runO(k: i32, action: (i32) -> i32 = func (v) => v + k) -> i32 => action(0)\n" +
        "func runB(k: bool, action: () -> bool = func [k] () => k) -> bool => action()\nfunc runN(k: i32, j: i32, action: (i32) -> i32 = func [k, j] (v) => v + k * 10 + j) -> i32 => action(0)\n";

    private const string Pairs = "func run(p: (i32, i32), action: (i32) -> i32 = func [p] (v) => v + p.0 + p.1) -> i32 => action(0)\nfunc runO(p: (i32, i32), action: (i32) -> i32 = func (v) => v + p.0 + p.1) -> i32 => action(0)\n";

    private const string Projected = "struct S\n    public let k: i32\n    public let pair: (i32, i32)\n    public init(k: i32, pair: (i32, i32))\n        self.k = k\n        self.pair = pair\n" +
        "let s = S.init(5, (3, 4))\nlet arr: [2 of (i32, i32)] = [(1, 1), (2, 5)]\nlet i: isize = 1\nlet t = ((1, 1), (2, 5))\n";

    public static TheoryData<string, string> Executed => new()
    {
        { "Item", Steps + "func run(value: i32, action: (i32) -> i32 = inc) -> i32 => action(value)\nrequire run(1) == 2 else => $abort(\"item\")" },
        { "Anonymous", "func run(value: i32, action: (i32) -> i32 = func (x) => x * 2) -> i32 => action(value)\nrequire run(3) == 6 else => $abort(\"anonymous\")" },
        { "GenericItem", "func identity<T>(value: T) -> T => value@move\nfunc run(value: i32, action: (i32) -> i32 = identity) -> i32 => action(value)\nrequire run(4) == 4 else => $abort(\"generic\")" },
        { "Overload", "func pick(value: i32) -> i32 => value + 1\nfunc pick(value: i64) -> i64 => value + 2\nfunc run(value: i32, action: (i32) -> i32 = pick) -> i32 => action(value)\nrequire run(1) == 2 else => $abort(\"overload\")" },
        { "GroupMember", "group Ops\n    public func inc(value: i32) -> i32 => value + 1\nfunc run(value: i32, action: (i32) -> i32 = (Ops.inc)) -> i32 => action(value)\nrequire run(1) == 2 else => $abort(\"group\")" },
        { "Capture", "func run(offset: i32, value: i32, action: (i32) -> i32 = func [offset] (x) => x + offset) -> i32 => action(value)\nrequire run(10, 1) == 11 else => $abort(\"capture\")" },
        { "OmittedList", "func run(offset: i32, action: (i32) -> i32 = func (x) => x + offset) -> i32 => action(1)\nrequire run(10) == 11 else => $abort(\"omitted\")" },
        { "HeapCapture", Heap + "require runE(1, 2) == 3 else => $abort(\"heap\")" },
        { "TwoDefaults", Steps + "func run(a: (i32) -> i32 = inc, b: (i32) -> i32 = dec) -> i32 => b(a(10))\nrequire run() == 10 else => $abort(\"two\")\nrequire run(b: inc) == 12 else => $abort(\"named\")" },
        { "NameBoundary", Steps + "func run(action: (i32) -> i32 = inc ! value: i32) -> i32 => action(value)\nrequire run(value: 1) == 2 else => $abort(\"boundary\")" },
        { "Method", "struct Counter\n    public let value: i32\n    public init(value: i32) => self.value = value\n    public func inc(value: i32) -> i32 => value + 1\n    public func apply(self, action: (i32) -> i32 = inc) -> i32 => action(self.value)\nlet c = Counter.init(1)\nrequire c.apply() == 2 else => $abort(\"method\")" },
        { "Constructor", "struct Handler\n    public let action: (i32) -> i32\n    public init(action: (i32) -> i32 = func (x) => x + 1) => self.action = action@move\n    public func run(self, value: i32) -> i32 => self.action(value)\nlet h = Handler.init()\nrequire h.run(1) == 2 else => $abort(\"constructor\")" },
        { "Supplied", Steps + "func run(value: i32, action: (i32) -> i32 = inc) -> i32 => action(value)\nrequire run(1, dec) == 0 else => $abort(\"supplied\")" },
        { "Uncalled", Steps + "func run(value: i32, action: (i32) -> i32 = func [value] (x) => x + value) -> i32 => action(value)\nrequire inc(1) == 2 else => $abort(\"uncalled\")" },
        { "GenericCallee", Steps + "func run<T>(v: T, action: (i32) -> i32 = inc) -> i32\n    T is Owned\n    return action(1)\nrequire run(true) == 2 and run(4) == 2 else => $abort(\"callee\")" },
        { "RepeatedOmission", "func run(offset: i32, value: i32, action: (i32) -> i32 = func [offset] (x) => x + offset) -> i32 => action(value)\nrequire run(10, 1) == 11 and run(20, 2) == 22 else => $abort(\"repeated\")\nlet o: i32 = 30\nrequire run(o, 3) == 33 else => $abort(\"local\")" },
        { "EarlierDefaultSlot", "func run(a: i32 = 5, g: (i32) -> i32 = func [a] (x) => x + a) -> i32 => g(1)\nrequire run() == 6 and run(7) == 8 else => $abort(\"earlier\")" },
        { "Recursive", "func countDown(n: i32, step: (i32) -> i32 = func [n] (x) => x + n) -> i32\n    if n == 0 => return step(0)\n    return countDown(n - 1) + step(0)\nrequire countDown(2) == 3 else => $abort(\"recursive\")" },
        { "ExclusiveReceiver", "struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n    public func inc(value: i32) -> i32 => value + 1\n    public func apply(self: uniq/Self, action: (i32) -> i32 = inc) -> i32\n        self.value = action(self.value)\n        return self.value\nvar c = Counter.init(1)\nrequire c.apply() == 2 and c.apply() == 3 else => $abort(\"exclusive\")" },

        // SPEC 14.2, 14.9.1: selections, `do` bodies and their transfers deliver erased sources; conditions stay scalar.
        { "Selection", Steps + "func run(up: bool, action: (i32) -> i32 = if up => inc else => dec) -> i32 => action(10)\nrequire run(true) == 11 and run(false) == 9 else => $abort(\"selection\")" },
        { "Match", Steps + "func run(n: i32, action: (i32) -> i32 = match n\n    0 => inc\n    _ => dec\n) -> i32 => action(10)\nrequire run(0) == 11 and run(1) == 9 else => $abort(\"match\")" },
        { "MatchYield", Steps + "func run(n: i32, action: (i32) -> i32 = match n\n    0\n        let k = 2\n        yield inc\n    _ => dec\n) -> i32 => action(10)\nrequire run(0) == 11 and run(1) == 9 else => $abort(\"yield\")" },
        { "LabeledDo", Steps + "func run(up: bool, action: (i32) -> i32 = label pick: do\n    if up => exit to pick inc\n    exit to pick dec\n) -> i32 => action(10)\nrequire run(true) == 11 and run(false) == 9 else => $abort(\"do\")" },
        { "LoopExit", Steps + "func run(n: i32, action: (i32) -> i32 = (loop => exit (if n > 0 => inc else => dec))) -> i32 => action(10)\nrequire run(1) == 11 and run(0) == 9 else => $abort(\"loop\")" },
        { "CapturingArm", Steps + "func run(up: bool, k: i32, action: (i32) -> i32 = if up => func [k] (x) => x + k else => dec) -> i32 => action(10)\nrequire run(true, 5) == 15 and run(false, 5) == 9 else => $abort(\"arm\")" },

        // Copy scalar-Tuple entries, anonymous match arms (also of a `let`), recursion through a closure body, and slots that a
        // receiver, an earlier default, named arguments or a later default's call surround.
        { "TupleCapture", "func run(p: (i32, i32), action: (i32) -> i32 = func [p] (v) => v + p.0 + p.1) -> i32 => action(1)\nrequire run((2, 3)) == 6 else => $abort(\"tuple\")" },
        { "TupleOmittedList", "func run(p: (i32, i32), action: (i32) -> i32 = func (v) => v + p.0 + p.1) -> i32 => action(1)\nlet t = (4, 5)\nrequire run((2, 3)) == 6 and run(t) == 10 else => $abort(\"omitted tuple\")" },
        { "MatchAnonymousArm", Steps + "func run(mode: i32, action: (i32) -> i32 = match mode\n    0 => inc\n    _ => func (x) => x\n) -> i32 => action(10)\nrequire run(0) == 11 and run(3) == 10 else => $abort(\"match arm\")" },
        { "MatchCapturingArms", "func run(k: i32, j: i32, mode: i32, action: (i32) -> i32 = match mode\n    0 => func [k] (x) => x + k\n    1 => func [j] (x) => x * j\n    _ => func (x) => x\n) -> i32 => action(10)\nrequire run(1, 2, 0) == 11 and run(1, 2, 1) == 20 and run(1, 2, 3) == 10 else => $abort(\"capturing arms\")" },
        { "LetMatchAnonymousArm", Steps + "func run(mode: i32) -> i32\n    let action: (i32) -> i32 = match mode\n        0 => inc\n        _ => func (x) => x\n    return action(10)\nrequire run(0) == 11 and run(3) == 10 else => $abort(\"let arm\")" },
        { "RecursiveClosureBody", "func run(n: i32, f: (i32) -> i32 = func [n] (x) => if x > 0 => run(x - 1) + n else => n) -> i32 => f(n)\nrequire run(3) == 6 else => $abort(\"closure recursion\")" },
        { "MutualRecursion", "func ping(n: i32, f: (i32) -> i32 = func (x) => if x > 0 => pong(x - 1) else => 0) -> i32 => f(n)\nfunc pong(n: i32, g: (i32) -> i32 = func (x) => if x > 0 => ping(x - 1) + 1 else => 1) -> i32 => g(n)\nrequire ping(3) == 2 else => $abort(\"mutual\")" },
        { "ReceiverDefaultSlot", "struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n    public func apply(self: uniq/Self, k: i32, action: (i32) -> i32 = func [k] (x) => x + k) -> i32\n        self.value = action(self.value)\n        return self.value\n    public func view(self, k: i32 = self.value, action: (i32) -> i32 = func [k] (x) => x * k) -> i32 => action(2)\nvar c = Counter.init(1)\nrequire c.apply(5) == 6 and c.apply(10) == 16 and c.view() == 32 else => $abort(\"receiver slot\")" },
        { "NamedDefaultSlots", "func run(a: i32 = 3, b: i32 = 4, action: (i32) -> i32 = func [a, b] (x) => x * a + b, n: i32 = 1) -> i32 => action(n)\nrequire run(b: 1, a: 2) == 3 and run(b: 5) == 8 and run(n: 2) == 10 and run(n: 10, a: 0) == 4 else => $abort(\"named slots\")" },
        { "LaterDefaultCall", "func twice(v: i32) -> i32 => v * 2\nfunc run(k: i32, a: (i32) -> i32 = func [k] (x) => x + k, b: i32 = twice(k)) -> i32 => a(b)\nfunc runP(p: (i32, i32), a: (i32) -> i32 = func [p] (v) => v + p.0 + p.1, b: i32 = twice(p.0)) -> i32 => a(b)\nrequire run(3) == 9 and runP((2, 3)) == 9 else => $abort(\"later call\")" },
        { "StaticReferenceCapture", "func leak(p: raw/i32) -> ref/i32 during static\n    unsafe => return (*p)@ref\nfunc run(r: ref/i32 during static, action: () -> i32 = func [r] () => r@follow) -> i32 => action()\nvar n = 7\nlet p: raw/i32 = n@raw\nrequire run(leak(p)) == 7 else => $abort(\"static\")" },
        { "StaticReferenceJoin", "func leak(p: raw/i32) -> ref/i32 during static\n    unsafe => return (*p)@ref\nfunc run(r: ref/i32 during static, action: () -> i32 = func () => r@follow) -> i32 => action()\nvar n = 7\nlet p: raw/i32 = n@raw\nlet c = true\nlet t = (leak(p), 1)\nrequire run(if c => leak(p) else => t.0) == 7 and run(t.0) == 7 else => $abort(\"static join\")" },
        { "Projected", Scalars + Projected + "require run(s.k) == 5 and run(arr[1].0) == 2 and run(t.1.0) == 2 else => $abort(\"projected\")" },

        // SPEC 7.2.3: an entry reads the slot its call prepared, whatever expression supplied it: a selection, `do`, loop exit or
        // short-circuit join, a field, element or Tuple part, a named argument or an earlier default. Each row also omits the list.
        { "JoinArguments", Scalars + "let c = true\nlet a = 2\nlet x: i32 = 4\nlet m = run(match a\n    2 => 20\n    _ => 0\n)\nrequire run(if c => 1 else => 2) == 1 and runO(if c => 6 else => 2) == 6 and m == 20 else => $abort(\"if\")\nrequire run(do => 5) == 5 and runO(loop => exit 6) == 6 and runO(label w: do => exit to w 7) == 7 else => $abort(\"do\")\nrequire runB(x > 1 and x < 9) and runB(x > 9 or x == 4) and not runB(not (x > 1)) else => $abort(\"short circuit\")\nrequire runN(j: if c => 3 else => 4, k: if c => 5 else => 6) == 53 else => $abort(\"named\")" },
        { "JoinDefaults", "func run(a: i32 = if true => 5 else => 6, g: (i32) -> i32 = func [a] (x) => x + a) -> i32 => g(1)\nfunc runO(a: i32 = five(), g: (i32) -> i32 = func (x) => x + a) -> i32 => g(1)\nfunc runC(k: i32, c: bool = k > 2 and k < 9, a: () -> bool = func [c] () => c) -> bool => a()\nfunc five() -> i32 => 5\nlet c = true\nrequire run() == 6 and run(if c => 1 else => 2) == 2 and runO() == 6 and runO(if c => 2 else => 3) == 3 else => $abort(\"earlier\")\nrequire runC(3) and not runC(10) else => $abort(\"short circuit\")" },
        { "TupleJoinArguments", Pairs + "let c = true\nlet a = 2\nlet m = run(match a\n    2 => (3, 4)\n    _ => (0, 0)\n)\nrequire run(if c => (1, 1) else => (2, 2)) == 2 and runO(if c => (2, 2) else => (1, 1)) == 4 and m == 7 else => $abort(\"if\")\nrequire run(do => (1, 2)) == 3 and runO(loop => exit (2, 3)) == 5 else => $abort(\"do\")" },
        { "ProjectedTupleArguments", Projected + Pairs + "require run(s.pair) == 7 and run(arr[1]) == 7 and run(arr[i]) == 7 and run(t.1) == 7 else => $abort(\"bare\")\nrequire runO(s.pair) == 7 and runO(arr[i]) == 7 and runO(t.1) == 7 and runO(if true => s.pair else => (0, 0)) == 7 else => $abort(\"omitted\")" },
        { "HeapJoinsAndProjections", "func run(p: (i32, i32), j: i32, action: (i32) -> i32 = func [p, j] (v) => v + p.0 + p.1 + j) -> i32 => action(0)\nlet c = true\nlet arr: [2 of (i32, i32)] = [(1, 1), (2, 5)]\nrequire run(arr[1], if c => 1 else => 0) == 8 and run(if c => arr[0] else => (0, 0), 0) == 2 else => $abort(\"heap\")" },

        // The scalar default reads the same prepared projections (SPEC 7.2.3).
        { "ScalarDefaultProjections", Projected + "func runY(p: (i32, i32), y: i32 = p.0 + p.1) -> i32 => y\nrequire runY(s.pair) == 7 and runY(arr[1]) == 7 and runY(arr[i]) == 7 and runY(t.1) == 7 else => $abort(\"scalar default\")" },

        // SPEC 7.2.3, 14.8.3: a match default borrows its subject, the slot the call prepared, also when a selection, `do`,
        // loop exit or short-circuit join supplied it, as an explicit argument or an earlier default; guards read it as well.
        { "MatchJoinSubjects", Steps + "func run(a: i32 = if true => 0 else => 1, action: (i32) -> i32 = match a\n    0 => inc\n    _ => dec\n) -> i32 => action(10)\nfunc runB(b: bool, action: (i32) -> i32 = match b\n    true => inc\n    false => dec\n) -> i32 => action(10)\nfunc runG(n: i32, action: (i32) -> i32 = match n\n    let m if m > 0 => inc\n    _ => dec\n) -> i32 => action(10)\nfunc runK(b: bool, k: i32, action: (i32) -> i32 = match k\n    0 => func [b] (x) => if b => x else => 0\n    _ => func [k] (x) => x + k\n) -> i32 => action(10)\nlet c = true\nlet x: i32 = 4\nrequire run() == 11 and run(if c => 1 else => 0) == 9 and run(do => 0) == 11 else => $abort(\"if\")\nrequire runB(x > 1 and x < 9) == 11 and runB(x > 9 or x < 0) == 9 else => $abort(\"bool\")\nrequire runG(match c\n    true => 1\n    false => 0\n) == 11 and runG(if c => 0 else => 1) == 9 else => $abort(\"guard\")\nrequire runK(true, loop => exit 3) == 13 and runK(true, do => 0) == 10 and runK(false, if c => 0 else => 1) == 0 else => $abort(\"arms\")" },
        { "ScalarMatchJoinSubjects", "func runM(k: i32, a: i32 = match k\n    1 => 70\n    _ => 80\n) -> i32 => a + k\nfunc runS(a: i32 = if true => 0 else => 1, b: i32 = match a\n    0 => 5\n    _ => 6\n) -> i32 => b\nlet c = true\nrequire runM(if c => 1 else => 2) == 71 and runM(do => 2) == 82 and runS() == 5 and runS(label w: do => exit to w 1) == 6 else => $abort(\"scalar match\")" },
    };

    [Theory]
    [MemberData(nameof(Executed))]
    public void ErasedFunctionDefaultsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture(Prefix + name, source + "\nConsole.writeLine(\"done\")", "done\n");

    // SPEC 7.2.3: generation validates a default closure's entry against the argument that its call prepared for that
    // parameter; another prepared argument of the same Type is not that slot.
    [Fact]
    public void ACaptureOfAnotherPreparedArgumentFailsGeneration()
    {
        var c = MinimalEmissionTest.Analyze("func run(k: i32, j: i32, action: (i32) -> i32 = func [k] (x) => x + k) -> i32 => action(j)\nrequire run(1, 2) == 3 else => $abort(\"k\")");
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
        var body = c.Ownership.Bodies.Single(static x => x.Operations.Any(static o => o.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && o.Source is FunctionKoto { IsAnonymous: true }));
        var capture = body.OperationStorage.FindIndex(static x => x.Kind is OwnershipOperationKind.Read or OwnershipOperationKind.Consume && x.Source is FunctionKoto { IsAnonymous: true });
        var call = body.OperationStorage.FindIndex(static x => x.Kind == OwnershipOperationKind.Call && x.Source is InvocationKoto { BoundCall.DefaultArguments.IsEmpty: false });

        // The call acquires k, then j, then the default. The entry is redirected, with its SSA input, to j's prepared value.
        Assert.Equal(body.Operations[call - 3].Place, body.Operations[capture].Place);
        var other = body.Operations[call - 2].Place;
        var produced = body.OperationStorage.FindIndex(x => x.Kind == OwnershipOperationKind.Produce && x.Place == other);
        body.OperationStorage[capture] = body.Operations[capture] with { Place = other };
        body.ValueOperands[body.Values[capture].Start] = produced;
        Assert.False(c.Emission.Validate(out _));
        Assert.True(c.Ownership.Analyze().IsVerified);
        Assert.True(c.Emission.Validate(out error), error);
    }

    // Each omission creates and releases its own environment, and only the selected arm allocates.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("Heap", Heap + "require runE(1, 2) == 3 else => $abort(\"heap\")", 1, 16)]
    [InlineData("HeapNamed", "func runE(a: i64, b: i64, action: (i64) -> i64 = func [a, b] (x) => x + a + b, n: i64 = 0) -> i64 => action(n)\nrequire runE(1, 2) == 3 else => $abort(\"e\")\nrequire runE(1, 2, n: 4) == 7 else => $abort(\"n\")", 2, 32)]
    [InlineData("HeapArm", "func runH(up: bool, a: i64, b: i64, action: (i64) -> i64 = if up => func [a, b] (x) => x + a + b else => func (x) => x) -> i64 => action(10)\nrequire runH(true, 1, 2) == 13 and runH(false, 1, 2) == 10 else => $abort(\"h\")", 1, 16)]
    [InlineData("Inline", "func run(offset: i32, action: (i32) -> i32 = func [offset] (x) => x + offset) -> i32 => action(1)\nrequire run(10) == 11 else => $abort(\"inline\")", 0, 0)]
    [InlineData("HeapJoin", Heap + "let c = true\nlet xs: [2 of i64] = [1, 2]\nrequire runE(if c => xs[0] else => xs[1], xs[1]) == 3 and runE(xs[0], do => xs[1]) == 3 else => $abort(\"heap join\")", 2, 32)]
    public void OmittedEnvironmentsAreReleasedOnce(string name, string source, int count, long bytes)
        => NativeAllocationAudit.WriteFixture(Prefix + "Audit" + name, source + "\nConsole.writeLine(\"done\")", count, count, bytes, "done\n");

    // SPEC 7.2.3: an earlier default that does not complete ends the preparation, so the later closure is never created.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void NoncompletingEarlierDefaultCreatesNoClosure()
    {
        const string Source = "func runE(x: i32, y: i32 = x + 1, a: i64 = 1, b: i64 = 2, action: (i64) -> i64 = func [a, b] (v) => v + a + b) -> i64 => action(0)\nConsole.writeLine(\"begin\")\nlet r = runE(2147483647)\nConsole.writeLine(\"bad\")";
        var column = Source.IndexOf("x + 1", StringComparison.Ordinal) + 1;
        NativeAllocationAudit.WriteFixture(Prefix + "AuditNoncompleting", Source, 0, 0, 0, "begin\n", exit: 1, stderr: $"Hello.kimi:1:{column}: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    // SPEC 7.6.4, IMPL 21.2.5.3: the environment allocation of an omitted default reports the default expression.
    [Fact]
    public void AllocationFailureIsLocatedAtTheDefault()
    {
        const string Source = Heap + "require runE(1, 2) == 3 else => $abort(\"heap\")";
        var column = Source.IndexOf("func [a, b]", StringComparison.Ordinal) + 1;
        var c = MinimalEmissionTest.Analyze(Source);
        var ir = CompilationTestHelper.WriteIr(c).Replace("call ptr @HeapAlloc(", "call ptr @fail_closure_alloc(", StringComparison.Ordinal) +
            "\ndefine internal ptr @fail_closure_alloc(ptr %heap, i32 %flags, i64 %bytes) {\nentry:\n  ret ptr null\n}\n";
        ScalarEmissionTest.WriteFixture(Prefix + "AllocationFailure", ir, string.Empty, 1, $"Hello.kimi:1:{column}: abort KIMI_E_ALLOC: Failed to allocate memory\n");
    }

    // SPEC 7.2.3, 7.6.2: an omitted capture list only Copies. A non-Copy preceding argument is the default's own error, at
    // the closure, and the pending call still owns its prepared argument.
    [Fact]
    public void NonCopyCaptureOfAPreparedArgumentIsOneTransferRequired()
    {
        const string Source = "func runT(text: string, action: () -> bool = func () => text == \"a\") -> bool => action()\npublic func main() -> ()\n    let a = runT(\"a\")\n    let b = runT(\"b\")\n";
        var error = Assert.Single(DiagnosticCorpus.Check(Source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), error.Code);
        Assert.Equal("func () => text == \"a\"", Source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    // SPEC 7.2.3: a default is checked for every binding, as a generic body is, and binds no slot. In a default, a capture of a
    // preceding argument can neither move it nor keep a borrow of it, and the erased environment must be Owned (SPEC 7.6.4), so no candidate
    // proposes @move, @ref, @uniq, a Reborrow of self, or keeping the concrete closure. Outside a default the records are kept.
    // The anchor is unique in the source; every row is one record without repair candidates, a Proof record for an unproven
    // Constraint and a Language record otherwise.
    [Theory]
    [InlineData(Steps + "func run<F>(value: i32, action: F = inc) -> i32\n    F is Callable<(i32) -> i32>\n    return action(value)\npublic func main() => ()\n", "F = inc", "inc", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func run<F>(value: i32, action: F = func (x: i32) -> i32 => x + 1) -> i32\n    F is Callable<(i32) -> i32>\n    return action(value)\npublic func main() => ()\n", "F = func", "func (x: i32) -> i32 => x + 1", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData(Steps + "func run<F>(value: i32, action: F = inc) -> i32\n    F is Callable<(i64) -> i64>\n    return 0\npublic func main() => ()\n", "F = inc", "inc", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData(Steps + "func run<F>(value: i32, action: F = inc) -> i32\n    F is Callable<(i32) -> i32>\n    F is Callable<(i64) -> i64>\n    return 0\npublic func main() => ()\n", "F = inc", "inc", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func run<F>(value: i32, action: F = func (a, b) => a) -> i32\n    F is Callable<(i32) -> i32>\n    return 0\npublic func main() => ()\n", "F = func", "func (a, b) => a", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func g<T>(x: T, y: T = 0) -> () => ()\npublic func main() => ()\n", "T = 0", "0", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func g<T>(x: T, y: T = \"s\") -> ()\n    T is PrimitiveInteger\n    return ()\npublic func main() => ()\n", "T = \"s\"", "\"s\"", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func h<T>(x: T, y: (T, i32) = 5) -> () => ()\npublic func main() => ()\n", "= 5", "5", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func q<T>(x: (i32, i32), y: (T, i32) = x) -> () => ()\npublic func main() => ()\n", "= x)", "x", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func q<T>(x: (bool, i32), y: (T, i32) = x) -> ()\n    T is PrimitiveInteger\n    return ()\npublic func main() => ()\n", "= x)", "x", nameof(DiagnosticCode.TypeMismatch_Kd), null)]

    // The rule decides an arm or a Tuple element as it decides the whole default, for the Type parameters of the declaration and
    // of its container under their Constraints.
    [InlineData("struct Box<T>\n    T is PrimitiveInteger\n    public let v: T\n    public init(v: T) => self.v = v\n    public func set(self, w: T = \"s\") -> () => ()\npublic func main() => ()\n", "T = \"s\"", "\"s\"", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("struct Box<T>\n    public let v: T\n    public init(v: T) => self.v = v\n    public func put(self, w: T = 0) -> () => ()\npublic func main() => ()\n", "T = 0", "0", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func n<T>(x: T, y: T = x@ref) -> () => ()\npublic func main() => ()\n", "= x@ref", "x@ref", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func a<T>(b: bool, x: T, y: T = if b => x else => 0) -> () => ()\npublic func main() => ()\n", "else => 0", "0", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func a<T>(b: bool, x: T, y: T = if b => x else => \"s\") -> ()\n    T is PrimitiveInteger\n    return ()\npublic func main() => ()\n", "= if b", "if b => x else => \"s\"", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func c<T>(x: T, y: (T, i32) = (0, 1)) -> () => ()\npublic func main() => ()\n", "(0, 1)", "0", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func m<T, U>(x: T, y: (T, U) = (1, true)) -> () => ()\npublic func main() => ()\n", "(1, true)", "1", nameof(DiagnosticCode.TypeMismatch_Kd), null)]
    [InlineData("func runT(text: string, action: () -> bool = func () => text == \"a\") -> bool => action()\npublic func main() -> ()\n    let a = runT(\"a\")\n", "= func () => text", "func () => text == \"a\"", nameof(DiagnosticCode.TransferRequired_Kd), "The omitted capture list captures text only by Copy; it never infers a Move, a borrow or a Reborrow, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData(Steps + "func run(a: (i32) -> i32 = inc, b: (i32) -> i32 = func [a] (x) => a(x)) -> i32 => b(a(1))\npublic func main() => ()\n", "func [a]", "a", nameof(DiagnosticCode.TransferRequired_Kd), "The bare capture entry a initializes its environment binding as let a = a would; (i32) -> i32 is not proven Copy, and a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func runT(text: string, action: () -> bool = func [text@ref] () => text@follow == \"a\") -> bool => action()\npublic func main() => ()\n", "= func [text@ref]", "func [text@ref] () => text@follow == \"a\"", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture text depends on the borrow text@ref, which is not static; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func run(k: i32, f: () -> i32 = func [k@ref] () => k@follow) -> i32 => f()\npublic func main() => ()\n", "= func [k@ref]", "func [k@ref] () => k@follow", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture k depends on the borrow k@ref, which is not static; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func runT(text: ref/string, action: () -> bool = func () => text@follow == \"a\") -> bool => action()\npublic func main() => ()\n", "= func ()", "func () => text@follow == \"a\"", nameof(DiagnosticCode.UnprovenConstraint_Kd), "Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture text depends on text, which is not static; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n" + "func bump(c: uniq/Counter, g: () -> i32 = func [c] () => c.value) -> i32 => g()\npublic func main() => ()\n", "= func [c]", "func [c] () => c.value", nameof(DiagnosticCode.UnprovenConstraint_Kd), "Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture c depends on c, which is not static; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func f(k: i32, g: () -> i32 = func [k@uniq] () => k@follow) -> i32 => g()\npublic func main() => ()\n", "[k@uniq]", "k@uniq", nameof(DiagnosticCode.InvalidAssignment_Kd), "The capture entry k@uniq borrows the slot of the let binding k exclusively, as let k = k@uniq would; a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n" + "    public func bump(self: uniq/Self, f: () -> i32 = func () => self.value) -> i32 => f()\npublic func main() => ()\n", "=> self.value)", "self", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2); a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("struct Counter\n    Self is Copy\n    public var value: i32\n    public init(value: i32) => self.value = value\n    public func peek(self: Self, f: () -> i32 = func () => self.value) -> i32 => f()\npublic func main() => ()\n", "=> self.value)", "self", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2); a default can neither move a preceding argument nor keep a borrow of it (SPEC 7.2.3)")]
    [InlineData("func run(text: string) -> bool\n    let action = func () => text == \"a\"\n    return action()\npublic func main() => ()\n", "= func () => text", "func () => text == \"a\"", nameof(DiagnosticCode.TransferRequired_Kd), "The omitted capture list captures text only by Copy; it never infers a Move, a borrow or a Reborrow")]
    [InlineData("func runT(text: string) -> bool\n    let action: () -> bool = func [text@ref] () => text@follow == \"a\"\n    return action()\npublic func main() => ()\n", "= func [text@ref]", "func [text@ref] () => text@follow == \"a\"", nameof(DiagnosticCode.UnsatisfiedConstraint_Kd), "Common Function conversion requires an Owned environment (SPEC 7.6.4, 15.2.3); the capture text depends on the borrow text@ref, which is not static")]
    [InlineData("func f(k: i32) -> i32\n    let g = func [k@uniq] () => k@follow\n    return g()\npublic func main() => ()\n", "[k@uniq]", "k@uniq", nameof(DiagnosticCode.InvalidAssignment_Kd), "The capture entry k@uniq borrows the slot of the let binding k exclusively, as let k = k@uniq would")]
    [InlineData("struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n" + "    public func bump(self: uniq/Self) -> i32\n        let f = func () => self.value\n        return f()\npublic func main() => ()\n", "=> self.value\n", "self", nameof(DiagnosticCode.InvalidCaptureBinding_Kd), "Contextual self is never captured implicitly; an anonymous function without a capture list captures only ordinary bindings (SPEC 7.6.2)")]
    public void DefaultDiagnosticsStateTheDeclarationRule(string source, string anchor, string text, string code, string? note)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        var start = source.IndexOf(anchor, StringComparison.Ordinal) + anchor.IndexOf(text[0], StringComparison.Ordinal);
        var category = code == nameof(DiagnosticCode.UnprovenConstraint_Kd) ? DiagnosticCategory.Proof : DiagnosticCategory.Language;
        Assert.Equal((code, category, start, text.Length), (error.Code, error.Category, error.Span!.Value.Start, error.Span.Value.Length));
        Assert.Equal(text, source.Substring(start, text.Length));
        Assert.Equal(note, error.Note);
        Assert.True(error.Repairs is null or { Length: 0 });
    }

    // SPEC 8.4.7.3, 7.2.3: a literal default fits every binding of a PrimitiveInteger T and is checked even without an instance.
    [Fact]
    public void GenericLiteralDefaultUnderItsConstraintIsNoMismatch()
    {
        const string Source = "func g<T>(y: T = 0) -> T\n    T is PrimitiveInteger\n    return y\npublic func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(Source).Diagnostics);
    }

    // The default Note reaches the console and both language-server placements unchanged (SPEC 7.2.3).
    [Theory]
    [InlineData("func runT(text: string, action: () -> bool = func () => text == \"a\") -> bool => action()\nlet a = runT(\"a\")", false)]
    [InlineData("func runT(text: string, action: () -> bool = func [text@ref] () => text@follow == \"a\") -> bool => action()\nConsole.writeLine(\"x\")", true)]
    [InlineData("struct Counter\n    public var value: i32\n    public init(value: i32) => self.value = value\n    public func bump(self: uniq/Self, f: () -> i32 = func () => self.value) -> i32 => f()\nConsole.writeLine(\"x\")", true)]
    public void DefaultDiagnosticsReachTheCliAndTheLanguageServer(string source, bool binding)
    {
        var path = Path.GetFullPath("function-default.kimi");
        var c = MinimalEmissionTest.Analyze(source, path);
        if (binding)
        {
            c.Binding.ReportDiagnostics();
        }
        else
        {
            c.Ownership.ReportDiagnostics();
        }

        c.Diagnostics.AddInput(c.Diagnostics.FindDocument(path)!, c.Kotonoha);
        var result = c.Diagnostics.Finalize();
        var record = Assert.Single(result.Diagnostics);
        Assert.NotNull(record.Note);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("Note: " + record.Note, console.Text, StringComparison.Ordinal);
        var identity = SourceIdentity.FromPath(path);
        foreach (var related in new[] { false, true })
        {
            var sent = Assert.Single(WorkspaceCheck.Place(new(CheckOutcome.Completed, false, TestPresence.No, result), [identity], identity, related)[identity]);
            Assert.Equal((record.Display!.Range, record.Code), (sent.Range, sent.Code));
            Assert.Contains("note: " + record.Note, sent.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ReanalysisAndReloadKeepOneDefaultCaptureError()
    {
        var c = MinimalEmissionTest.Analyze("func runT(text: string, action: () -> bool = func () => text == \"a\") -> bool => action()\nlet a = runT(\"a\")");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(c.Bind().IsComplete);
            Assert.False(c.Ownership.Analyze().IsVerified);
            Assert.Single(c.Ownership.Issues, x => x.Failure == OwnershipFailure.TransferRequired);
            Assert.DoesNotContain(c.Ownership.Issues, x => x.Failure is OwnershipFailure.Unsupported or OwnershipFailure.Internal or OwnershipFailure.PossiblyMovedUse);
        }

        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.False(restored.Ownership.Analyze().IsVerified);
        Assert.Single(restored.Ownership.Issues, x => x.Failure == OwnershipFailure.TransferRequired);
    }

    // Generic defaults are checked in the declaration without requiring any concrete call.
    [Theory]
    [InlineData("func identity<T>(value: T) -> T => value@move\nfunc run<T>(value: T, action: (T) -> T = identity) -> T\n    T is Owned\n    return action(value@move)\n", "identity")]
    [InlineData("func run<T>(v: T, action: (i32) -> i32 = func (x) => x + 1) -> i32\n    T is Owned\n    return action(1)\n", "(x)")]
    public void GenericFunctionDefaultsAreCheckedWithoutAnInstance(string declaration, string text)
    {
        var source = declaration + "public func main() => ()\n";
        Assert.Contains(text, source, StringComparison.Ordinal);
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
        var c = MinimalEmissionTest.Analyze(declaration);
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Ownership.Issues));
    }

    [Theory]
    [InlineData("Default", "func both(text: string, show: (ref/string) -> () = Console.writeLine) -> () => show(text@ref)\npublic func main() -> ()\n    both(\"hi\")\n")]
    [InlineData("Expression", "let show: (ref/string) -> () = Console.writeLine\nshow(\"hi\")")]
    public void CompilerFunctionDefaultsExecute(string name, string source)
        => ScalarEmissionTest.EmitFixture("CompilerFunctionDefault" + name, source, "hi\n");

    [Fact]
    public void RebindingAndReloadRebuildFunctionDefaultPlans()
    {
        var c = MinimalEmissionTest.Analyze(Steps + Heap + "func run(offset: i32, a: (i32) -> i32 = inc, b: (i32) -> i32 = func [offset] (x) => x + offset) -> i32 => b(a(1))\nrequire run(10) == 12 and runE(1, 2) == 3 else => $abort(\"plans\")");
        using var original = new StringWriter();
        Assert.True(c.Emission.WriteIr(original, out var error), MinimalEmissionTest.Describe(c, error));
        Assert.True(c.Bind().IsComplete);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified);
        using var rebound = new StringWriter();
        Assert.True(c.Emission.WriteIr(rebound, out error), error);
        Assert.Equal(original.ToString(), rebound.ToString());

        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        restored.Binding.CheckStartup(OutputKind.Application);
        Assert.True(restored.Ownership.Analyze().IsVerified);
        using var reloaded = new StringWriter();
        Assert.True(restored.Emission.WriteIr(reloaded, out error), error);
        Assert.Equal(original.ToString(), reloaded.ToString());
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmFunctionDefaultPlansReuseStorage()
    {
        var c = MinimalEmissionTest.Analyze(Steps + Heap + "func run(offset: i32, a: (i32) -> i32 = inc, b: (i32) -> i32 = func [offset] (x) => x + offset) -> i32 => b(a(1))\nrequire run(10) == 12 and runE(1, 2) == 3 else => $abort(\"warm\")");
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Ownership.Analyze().IsVerified || !c.Emission.WriteIr(TextWriter.Null, out _))
            {
                throw new InvalidOperationException("Function default emission failed.");
            }
        }));
    }
}
