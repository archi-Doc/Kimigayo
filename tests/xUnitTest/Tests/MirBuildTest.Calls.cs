// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The calls of the MIR build: reservations of exclusive inputs, evaluation order and operand reads, the temporaries' destruction in
/// reverse creation order, pair acquisitions, diverging arguments and Value callees.</summary>
public sealed partial class MirBuildTest
{
    // Values with a destructor, their calls, method, getter, constructor and Contract operator (SPEC 3.6's temporaries).
    private const string Temporaries = """
        struct Noisy
            public let name: i32
            public computed twin: Noisy
                get() -> Noisy => Noisy.init(2)
            public init(name: i32) => self.name = name
            public func make(self, n: i32) -> Noisy => Noisy.init(n)
            public func bump(self: uniq/Self, other: ref/Noisy) -> Noisy => Noisy.init(4)
            drop => ()
        struct Echo
            public let n: i32
            public init(a: ref/Noisy) => self.n = a.name
            drop => ()
        struct Num
            Self is Addable<Num>
            associate Addable<Num>.Output is Num
            public let v: i32
            public init(v: i32) => self.v = v
            public func added(self: ref/Self, right: ref/Num) -> Num => Num.init(4)
            drop => ()
        func pass(a: ref/Noisy) -> Noisy => Noisy.init(2)
        func show(a: ref/Noisy) => ()
        func see(e: ref/Echo) => ()
        func sum(n: ref/Num) => ()
        func pair(t: ref/(Noisy, i32)) => ()
        func two(a: ref/Noisy, b: i32) => ()
        func both(a: Noisy, b: i32, c: i32 = 0) => ()
        func noisies(a: ref/Noisy) -> Array<Noisy> => [Noisy.init(2)]
        func keys(d: ref/Dictionary<i32, Noisy>) => ()

        """;

    // SPEC 15.6.7: every exclusive borrow that produces an invocation's argument or receiver is reserved (TwoPhaseUniq): a written @uniq,
    // also in parentheses, the Reborrow of a stored uniq at a uniq parameter and a synthesized receiver (a Dictionary literal's entry
    // layout; an Array literal is one aggregate and borrows nothing, DECISIONS R5-M129); a written @uniq on a receiver's path is not formed
    // (DECISIONS R4-M52), so the receiver has one reservation, at its lending point; a borrow saved in storage or a control-flow result is
    // live when formed (Uniq).
    [Fact]
    public void ReservedCallInputsAreTwoPhase()
    {
        var source = "func take(a: uniq/i32, b: i32) => ()\nfunc f()\n    var p: i32 = 1\n    take(p@uniq, p + 1)\n    take((p@uniq), 2)\n" +
            "    var r = p@uniq\n    take(r, 3)\n    let values = [1, 2]\n    let table = [1: 2]\n" +
            "struct Counter\n    var count: i32\n    public init() => self.count = 0\n" +
            "    public func bump(self: uniq/Self, by: i32) => self.count = self.count + by\nstruct Holder\n    public var c: Counter\n" +
            "    public init() => self.c = Counter.init()\nfunc g(h: uniq/Holder)\n    h@follow@uniq.c.bump(1)\n    h@follow@uniq.c@uniq.bump(2)\nfunc chosen()\n    var p: i32 = 1\n" +
            "    take(if p > 0 => p@uniq else => p@uniq, 4)\nstruct Bag\n    public var items: Array<i32>\n    public var count: i32\n" +
            "    public init()\n        self.items = []\n        self.count = 0\nfunc fill()\n    var bag = Bag.init()\n    bag@uniq.items.append(bag.count)\n";
        Assert.Equal(
            [MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq, MirRefKind.Uniq, MirRefKind.TwoPhaseUniq, MirRefKind.Shared, MirRefKind.Shared,
                MirRefKind.TwoPhaseUniq],
            Refs(Body(source, "f")).Select(static x => x.Kind));
        var g = Body(source, "g");
        var lent = Refs(g).ToArray();
        Assert.Equal([MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq], lent.Select(static x => x.Kind));
        Assert.All(lent, x => Assert.Equal("_1*.0", Steps(g, x.Place))); // (*h).c, each time.
        Assert.Equal([MirRefKind.Uniq, MirRefKind.Uniq], Refs(Body(source, "chosen")).Select(static x => x.Kind));
        var fill = Body(source, "fill");
        var items = Assert.Single(Refs(fill));
        Assert.Equal(MirRefKind.TwoPhaseUniq, items.Kind);
        Assert.Matches(@"^_\d+\.0$", Steps(fill, items.Place)); // bag.items, at the local.

        static IEnumerable<(MirRefKind Kind, MirPlace Place)> Refs(MirBody body) => body.Statements.ToArray()
            .Where(static x => x.Kind == MirStatementKind.Assign && x.Rvalue.Kind == MirRvalueKind.Ref).Select(static x => ((MirRefKind)x.Rvalue.Op, x.Rvalue.Place));
    }

    // SPEC 15.6.7 (DECISIONS R4-M54): the preparation path of an input that starts a reservation is one preparation even when it contains
    // written exclusive borrows, so the three spellings of one call build the same MIR.
    [Theory]
    [InlineData("holder@uniq.items.append(x)", "holder.items.append(x)")]
    [InlineData("holder.items@uniq.append(x)", "holder.items.append(x)")]
    [InlineData("(holder@uniq).items.append(x)", "holder.items.append(x)")]
    public void AWrittenBorrowOnAPreparationPathIsThePlaceItLends(string written, string path)
    {
        Assert.Equal(Built(path), Built(written));

        static string Built(string statement)
        {
            var program = Compile(Holder + $"    {statement}\n").BuildMir();
            Assert.Empty(program.Findings);
            Assert.Empty(program.Records);
            var text = new StringBuilder();
            MirDump.Write(program, 0, text);
            return string.Join('\n', text.ToString().Split('\n').Select(static x => x.Split(" @")[0]));
        }
    }

    // SPEC 15.6.7, 7.3.3 (DECISIONS R4-M54): on the path of an input that starts no reservation, a shared one, and off input paths, a
    // written exclusive borrow is a live exclusive borrow of its operand (holder@uniq.items.length lends holder exclusively, then reads).
    [Theory]
    [InlineData("    inspect(holder@uniq.items)\n")]
    [InlineData("    let r = holder.items[0]@ref\n    let n = holder@uniq.items.length\n    see(r)\n")]
    public void AWrittenBorrowOffAReservationIsLive(string statements)
    {
        var body = Body(Holder + statements, "f");
        var holder = Enumerable.Range(0, body.Locals.Length).Single(x => body.Locals[x].Name == "holder");
        Assert.Contains(body.Statements.ToArray(), x => x is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Ref, Place.Projection: 0 } } &&
            x.Rvalue.Place.Root == holder && x.Rvalue.Op == (byte)MirRefKind.Uniq);
    }

    // SPEC 12.2 (DECISIONS R4-M55): operands are evaluated once, left to right, so a Place operand is read where it is evaluated: where a
    // later operand of its operation builds code, it is secured into a temp first, and on the runtime path no read of the variable follows
    // that operand's write or borrow of it: a call's argument, a tuple's element, an operator's left operand, an Array literal's element,
    // a moved argument and the receiver of a Slice or raw element selection.
    [Theory]
    [InlineData("x", "    _ = take(x, do => x = 5)")]
    [InlineData("x", "    pair((x, do => x = 5))")]
    [InlineData("x", "    _ = x + reset(x@uniq)")]
    [InlineData("x", "    _ = [x, reset(x@uniq)]")]
    [InlineData("s", "    var s = \"a\"\n    consume(s@move, do => s = \"b\")")]
    [InlineData("v", "    var v = view\n    _ = v[touch(v@uniq)]")]
    [InlineData("p", "    var p = raw\n    unsafe => _ = p[jump(p@uniq)]")]
    public void AnOperandIsReadWhereItIsEvaluated(string variable, string statements)
    {
        var body = Body(Operations + statements + "\n", "f");
        var local = Enumerable.Range(0, body.Locals.Length).Single(x => body.Locals[x].Name == variable);
        var (writes, late) = (0, false);
        var uses = new Uses(local);
        foreach (var at in RuntimePath(body))
        {
            (uses.Reads, uses.Writes) = (false, false);
            MirUses.ForEach(body, at, ref uses);
            late |= uses.Reads && writes > 1;
            writes += uses.Writes ? 1 : 0;
        }

        Assert.True(writes > 1);
        Assert.False(late, statements);
    }

    // SPEC 12.2: a Place operand that no later operand of its operation builds code after is read by its operation, unsecured.
    [Fact]
    public void AnOperandBeforeNoCodeIsReadByItsOperation()
    {
        var body = Body(Operations + "    let y: i32 = 2\n    _ = take(x, ())\n    pair((x, ()))\n    _ = x + y\n    _ = [x, y]\n    _ = view[y]\n", "f");
        var read = Enumerable.Range(0, body.Locals.Length).Where(x => body.Locals[x].Name is "x" or "view").ToArray();
        Assert.Equal(2, read.Length);
        Assert.DoesNotContain(
            body.Statements.ToArray(),
            x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } && body.Locals[x.Place.Root].Kind == MirLocalKind.Temp &&
                body.Operands[x.Rvalue.Start] is { Kind: MirOperandKind.Copy, Place.Projection: 0 } copy && read.Contains(copy.Place.Root));
    }

    // SPEC 12.2, 10.1.1, 7.3.4, 13.7.1, 7.7.1 (A3 E13 (2)): operands are read in their evaluation order, so a Place operand that an operand
    // evaluated after it precedes in the operation's operand order is secured where it is evaluated, as one before a later operand that
    // builds code is (R4-M55): a named argument, an argument before an unbound reference's receiver supplied after it, a setter's value and
    // a capture before one whose acquisition builds code; an operand in order is read by its operation (tests/mir/order shows each form).
    [Theory]
    [InlineData("    give2(b: x, a: y)\n", true)]
    [InlineData("    give2(a: x, b: y)\n", false)]
    [InlineData("    Box.put(x, box)\n", true)]
    [InlineData("    box.put(x)\n", false)]
    [InlineData("    gauge.level = x\n", true)]
    [InlineData("    let c = func [x, y@uniq] () => ()\n", true)]
    [InlineData("    let c = func [y@uniq, x] () => ()\n", false)]
    public void AnOperandIsReadInItsEvaluationOrder(string statement, bool secured)
    {
        var body = Body(Order + statement, "f");
        Assert.Equal(secured, body.Statements.ToArray().Any(x => x is { Kind: MirStatementKind.Assign, Place.Projection: 0, Rvalue.Kind: MirRvalueKind.Use } &&
            body.Locals[x.Place.Root].Kind == MirLocalKind.Temp &&
            body.Operands[x.Rvalue.Start] is { Kind: MirOperandKind.Copy, Place: { Root: 1, Projection: 0 } }));
    }

    // SPEC 12.2 (R4-M37's Open): a pair-layer argument is acquired by case where it is evaluated, before a later argument's code, not at its
    // Call.
    [Fact]
    public void APairLayerArgumentIsAcquiredWhereItIsEvaluated()
    {
        const string Source = "func pass<s/T>(a: s/T, b: i32)\n    s is owner or ref\n    T is Copy\n    _ = b\nfunc one() -> i32 => 1\n" +
            "func pending<s/T>(x: s/T)\n    s is owner or ref\n    T is Copy\n    pass(x, one())\n";
        var body = Body(Source, "pending");
        var path = RuntimePath(body).ToArray();
        var one = Array.FindIndex(path, at => at.Index == body.Blocks[at.Block].Count && body.Blocks[at.Block].Terminator.Callee.Symbol?.Name == "one");
        Assert.InRange(Array.FindIndex(path, Acquires), 0, one - 1);

        bool Acquires(MirLocation at) => at.Index < body.Blocks[at.Block].Count &&
            body.Statements[body.Blocks[at.Block].Start + at.Index] is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } statement &&
            body.Operands[statement.Rvalue.Start].Kind == MirOperandKind.PairAcquire;
    }

    // A3 E13 (4): a pair-layer acquisition is always the whole rvalue of its own Assign to a local, marked with the Reborrow its exclusive
    // case makes: TwoPhaseUniq where it produces an invocation's input, else Uniq (a local's initializer, a tuple's element, a capture);
    // an Adapt that produces an input has its input bit, so its exclusive case is a two-phase borrow there (MirCases.Adapt).
    [Fact]
    public void APairLayerAcquisitionIsMarkedWithItsBorrow()
    {
        var c = Compile("func inner<s/T>(a: s/T, b: i32) -> ()\n    s is ref or uniq\n    return\nfunc pairs<s/T>(p: s/T, item: T)\n    s is ref or uniq\n" +
            "    var v = p\n    inner(v, 1)\n    let t = (v, 1)\n    let c = func [v] () => ()\n    var x = item@move\n    inner(x@(s/T), 2)\n" +
            "    let a = x@(s/T)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn, Decl.Name: "pairs" });
        var assigns = body.Statements.ToArray().Where(static x => x.Kind == MirStatementKind.Assign).ToArray();
        Assert.Equal(
            [MirRefKind.Uniq, MirRefKind.TwoPhaseUniq, MirRefKind.Uniq, MirRefKind.Uniq],
            assigns.Where(x => x.Rvalue.Kind == MirRvalueKind.Use && body.Operands[x.Rvalue.Start].Kind == MirOperandKind.PairAcquire)
                .Select(static x => (MirRefKind)x.Rvalue.Op));
        var adapts = assigns.Where(static x => x.Rvalue.Kind == MirRvalueKind.Adapt).Select(static x => x.Rvalue).ToArray();
        Assert.Equal(new[] { (int)MirRvalue.AdaptInput, 0 }, adapts.Select(static x => x.Op & MirRvalue.AdaptInput));
        var cases = new MirCases(c.Types);
        var uniq = Enumerable.Range(0, (int)MirCases.Count(body)).Single(x => cases.Select(body, x)[0].Semantics == SemanticsKind.Uniq);
        cases.Select(body, uniq);
        Assert.Equal([MirRefKind.TwoPhaseUniq, MirRefKind.Uniq], adapts.Select(x => (MirRefKind)cases.Adapt(x, out _).Op));
    }

    // SPEC 15.6.7, 14.10.3: a call whose argument does not complete is still built where that argument's code continues for the checks,
    // so its activation is checked there, and the reservation the transfer abandons ends on the transfer's runtime edge only. Each argument
    // is built as at run time (DECISIONS R4-M62): a Tuple with a Never element is formed, of its expected Type (SPEC 3.8), and a continued
    // inner call's result reaches the outer call.
    [Fact]
    public void ADivergingArgumentContinuesItsCallForTheChecks()
    {
        var source = "func three(a: uniq/i32, b: i32, c: uniq/i32) => ()\nfunc f() -> i32\n    var p: i32 = 1\n    three(p@uniq, (return 1), p@uniq)\n" +
            "    return 2\nfunc pair(x: (i32, i32)) => ()\nfunc g() -> i32\n    pair((1, (return 1)))\n    return 2\n" +
            "func keep(a: uniq/i32, b: i32) -> uniq/i32 during a => a\nfunc both(x: uniq/i32, y: uniq/i32) => ()\nfunc h() -> i32\n" +
            "    var p: i32 = 1\n    both(keep(p@uniq, (return 1)), p@uniq)\n    return 0\n";
        static MirTerminator Called(MirBody body, string name) => Assert.Single(
            body.Blocks.ToArray(),
            x => x.Terminator is { Kind: MirTerminatorKind.Call } call && call.Callee.Symbol?.Name == name).Terminator;
        var paired = Body(source, "g");
        var tuple = paired.Operands[Called(paired, "pair").Start].Place;
        Assert.Equal("(i32, i32)", TyCtxt.Name(paired.TypeOf(tuple)!));
        Assert.Contains(paired.Statements.ToArray(), x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Aggregate } && x.Place == tuple);
        var kept = Body(source, "h");
        Assert.Equal(Called(kept, "keep").Place, kept.Operands[Called(kept, "both").Start].Place);
        var body = Body(source, "f");
        var reach = new MirReach();
        reach.Clear(body.Blocks.Length);
        reach.Walk(body, 0, checks: false);
        var blocks = body.Blocks.ToArray();
        var call = Assert.Single(
            Enumerable.Range(0, blocks.Length),
            b => blocks[b].Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "three" });
        Assert.False(reach[call]);
        var ends = Enumerable.Range(0, blocks.Length).Where(b => body.Statements.Slice(blocks[b].Start, blocks[b].Count).ToArray()
            .Any(static x => x.Kind == MirStatementKind.EndBorrow)).ToArray();
        Assert.NotEmpty(ends);
        Assert.All(ends, b => Assert.True(reach[b]));
        var transfers = Enumerable.Range(0, blocks.Length).Where(b => blocks[b].Terminator is { Kind: MirTerminatorKind.Goto, CheckTarget: >= 0 }).ToArray();
        Assert.NotEmpty(transfers);
        Assert.DoesNotContain(transfers, b => ends.Contains(b)); // The branching block, which the checks' edge leaves too, ends nothing.
    }

    // SPEC 3.8, 14.10.3 (DECISIONS R4-M62): where only the checks continue, any acquisition takes the Never value as it is (a comparison's
    // borrow, a tuple operator's leaves, R5-M75), an increment's one is of the old value's Type, and a rejected node of Type Never keeps
    // its Opaque value: each body validates.
    [Theory]
    [InlineData("func f() -> bool\n    let s = \"a\"\n    return s == (return false)\n")]
    [InlineData("func f(b: (string, i32)) -> bool\n    return (return false) < b\n")]
    [InlineData("func f() -> i32\n    var a: [1 of i32] = [0]\n    ++a[(return 42)]\n    return 0\n")]
    [InlineData("func f() -> i32\n    return true\n")]
    public void TheNeverValueValidatesWhereOnlyTheChecksContinue(string source) => _ = Body(source, "f");

    // SPEC 13.4, package F 4.8 (DECISIONS R5-M75): a tuple operator whose leaf's storage nests past Binding's bound has no comparison (a
    // Limit of TyCtxt.Comparison): one located Unsupported_Kd at the operator, with its Opaque value (fail-before: none, the tuple's call).
    [Fact]
    public void ATupleOperatorWithoutItsComparisonIsUnsupported()
    {
        var c = Compile(Unbuilt.Declarations + $"func f(a: ref/{Unbuilt.Tuple}, b: ref/{Unbuilt.Tuple}) -> bool => a == b\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var record = Assert.Single(program.Records, static x => x.Subject.Module == 0);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, "a == b"), (record.Code, c.SyntaxOf(record.Subject).ToString()));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f").HasOpaque);
    }

    // SPEC 13.4, 23.1.2 (DECISIONS BU-38, R5-M166): a tuple operator whose leaf's storage rests on a part Binding rejected, inline or
    // through a handle, has its comparison, since what the leaf's witness runs does not depend on that part: its comparison call (equals
    // under the operator mode) builds, with no record of the builder's (fail-before: the Limit of the twin above, Unsupported_Kd at the
    // operator).
    [Theory]
    [InlineData("struct Bad\n    Self is Equatable\n    public var m: Missing\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(a: ref/Bad, b: ref/Bad) -> bool => (a, 1) == (b, 1)\n")]
    [InlineData("struct Bad\n    public var m: Missing\nstruct Sx\n    Self is Equatable\n    public var x: rc/Bad\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\nfunc f(a: ref/Sx, b: ref/Sx) -> bool => (a, 1) == (b, 1)\n")]
    public void ATupleOperatorOverRejectedStorageBuildsItsComparison(string source)
    {
        var c = Compile(source);
        Assert.Equal(["UnresolvedBinding_Kd"], CompilationTestHelper.BindingErrors(c).Select(static x => x.Code));
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        Assert.DoesNotContain(program.Records, static x => x.Subject.Module == 0);
        var f = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        Assert.False(f.HasOpaque);
        Assert.Contains(f.Blocks.ToArray(), static x => x.Terminator is
        {
            Kind: MirTerminatorKind.Call, Callee: { Kind: MirCalleeKind.Requirement, Symbol.Name: "equals", Arg: (int)ComparisonMode.Operator },
        });
    }

    // SPEC 7.7.3, 15.6.4 (DECISIONS R4-M41): a Value callee names the Callable signature its callee declares, a TypeToken constant: the
    // Function Type with its own Origins, not the call's, whose result is bound to the argument.
    [Fact]
    public void ValueCalleesNameTheirSignature()
    {
        var body = Body("func apply(f: (ref/i32) -> ref/i32, x: ref/i32) -> ref/i32 during x => f(x)\n", "apply");
        var call = Assert.Single(body.Blocks.ToArray(), static x => x.Terminator is { Kind: MirTerminatorKind.Call, Callee.Kind: MirCalleeKind.Value });
        var signature = body.Consts[call.Terminator.Callee.Arg];
        Assert.Equal(MirConstKind.TypeToken, signature.Kind);
        Assert.Same(body.TypeOf(new MirPlace(1)), signature.Type);
    }

    // SPEC 3.6, 16.2.1, 7.2.3, 14.6.2 (DECISIONS R4-M129): a temporary is destroyed before every temporary created while it was evaluated
    // (a call's result before its receiver's and arguments' temporaries, an aggregate before its operands', a Subject or iterator before its
    // expression's, a pending slot before its argument's), on the normal end and on an exit that interrupts the statement: a cleanup chain
    // after a Drop of the first drops the second, and none after a Drop of the second drops the first. A binding is destroyed by
    // declaration, after its initializer's temporaries, and before an earlier binding initialized after it (fail-before: each temp's Drop
    // registered where its storage began, so the second was destroyed first, but for the bindings).
    [Theory]
    [InlineData("show(pass(Noisy.init(1)))", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("show(Noisy.init(1).make(2))", "Noisy.init(1).make(2)", "Noisy.init(1)")]
    [InlineData("show(pass(Noisy.init(1)).bump(Noisy.init(3)))", "pass(Noisy.init(1)).bump(Noisy.init(3))", "pass(Noisy.init(1))")]
    [InlineData("show(Noisy.init(1).twin)", "Noisy.init(1).twin", "Noisy.init(1)")]
    [InlineData("see(Echo.init(Noisy.init(1)))", "Echo.init(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("sum(Num.init(1) + Num.init(3))", "Num.init(1) + Num.init(3)", "Num.init(3)")]
    [InlineData("pair((pass(Noisy.init(1)), 5))", "(pass(Noisy.init(1)), 5)", "Noisy.init(1)")]
    [InlineData("show(pass(pass(Noisy.init(1))))", "pass(pass(Noisy.init(1)))", "pass(Noisy.init(1))")]
    [InlineData("_ = pass(Noisy.init(1))", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("pass(Noisy.init(1))", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("match pass(Noisy.init(1))\n        _ => ()", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("for x in noisies(Noisy.init(1))@ref => ()", "noisies(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("for x in noisies(Noisy.init(1)) => ()", "ArrayOwningIterator<Noisy>", "Noisy.init(1)")]
    [InlineData("two(pass(Noisy.init(1)), try n)", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("both(pass(Noisy.init(1)), try n)", "pass(Noisy.init(1))", "Noisy.init(1)")]
    [InlineData("let kept = pass(Noisy.init(1))", "Noisy.init(1)", "kept")]
    [InlineData("let a: Noisy\n    let b = Noisy.init(2)\n    a = Noisy.init(1)", "b", "a")]
    public void ATemporaryIsDestroyedBeforeTheTemporariesOfItsEvaluation(string statement, string first, string then)
    {
        var (c, body) = Temporaried(statement);
        var (a, b) = (Dropped(c, body, first), Dropped(c, body, then));
        Assert.True(Precedes(body, a, b));
        Assert.False(Precedes(body, b, a));
    }

    // SPEC 14.2.1, 16.2.2 step 3 (DECISIONS R4-M129): an if clause's body is its own Body, as a match arm's is, so the temporaries of its
    // result end with it, before the call that takes the if's value (fail-before: they were the enclosing statement's, destroyed after the
    // call).
    [Fact]
    public void AnIfClausesTemporariesEndWithIt()
    {
        var (c, body) = Temporaried("show(if true => pass(Noisy.init(1)) else => Noisy.init(3))");
        var blocks = body.Blocks.ToArray();
        var argument = Dropped(c, body, "Noisy.init(1)");
        var all = Enumerable.Range(0, blocks.Length);
        var drop = Assert.Single(all, x => blocks[x].Terminator is { Kind: MirTerminatorKind.Drop } d && d.Place.Root == argument);
        var call = Assert.Single(all, x => blocks[x].Terminator is { Kind: MirTerminatorKind.Call, Callee.Symbol.Name: "show" });
        Assert.True(Reaches(body, drop, call));
        Assert.False(Reaches(body, call, drop));
    }

    // SPEC 12.4.3, 16.2.1, 3.6 (DECISIONS R4-M129): a dictionary literal places its entries in a value of its own, which an exit from an
    // entry destroys after the temporaries created since, and moves it to the literal's value after the last entry, where that is created,
    // so it is destroyed before its entries' temporaries (fail-before: the entries placed in the literal's value, held before them).
    [Fact]
    public void ADictionaryLiteralsEntriesArePlacedInAValueOfItsOwn()
    {
        var (c, body) = Temporaried("keys([1: pass(Noisy.init(1)), 2: Noisy.init(try n)])");
        var moved = Assert.Single(
            body.Statements.ToArray(),
            x => x is { Kind: MirStatementKind.Assign, Rvalue.Kind: MirRvalueKind.Use } && TyCtxt.Name(body.TypeOf(x.Place)!) == "Dictionary<i32, Noisy>");
        var (literal, entries, argument) = (moved.Place.Root, body.Operands[moved.Rvalue.Start].Place.Root, Dropped(c, body, "Noisy.init(1)"));
        Assert.True(Precedes(body, literal, argument));
        Assert.False(Precedes(body, argument, literal));
        Assert.True(Precedes(body, argument, entries));
    }

    // SPEC 22.5.1 (DECISIONS R5-M144, R5-M145; PLAN G157 (32)): a failure of a Dictionary literal entry's insertion, its allocation included,
    // reports the entry's key expression, as its duplicate key does: each entry's lookup, its literal's storage handle and its placement are
    // located at its key (fail-before: the handle and the placement at its value).
    [Fact]
    public void ADictionaryLiteralsEntryInsertionIsLocatedAtItsKey()
    {
        var c = Compile("func key() -> i32 => 1\nfunc f() -> Dictionary<i32, i32> => [key(): 10, 2: 20]\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f");
        var steps = body.Blocks.ToArray().Select(static x => x.Terminator)
            .Where(static x => x.Kind == MirTerminatorKind.Call && x.Callee.Symbol?.Name is "findEntry" or "dictionaryStorage" or "placeEntry")
            .Select(x => x.Callee.Symbol!.Name + " " + c.SyntaxOf(x.Source.Syntax));
        Assert.Equal(
            ["findEntry key()", "dictionaryStorage key()", "placeEntry key()", "findEntry 2", "dictionaryStorage 2", "placeEntry 2"],
            steps);
    }

    // SPEC 22.10.4, 22.10.1 (PLAN G157 (35), DECISIONS R5-M131): a $tryWrite evaluates an embedded expression only after a test that its
    // adapter is healthy: a first segment that is an expression follows the adapter's status, and a text's write returns the recorded failure
    // itself, so a text first needs none (fail-before: next() ran first, on an adapter that may have failed already).
    [Theory]
    [InlineData("\\(next())x", new[] { "status", "next", "write" })]
    [InlineData("x\\(next())", new[] { "write", "next", "write" })]
    public void ATryWritesFirstExpressionFollowsItsAdaptersStatus(string literal, string[] calls)
    {
        var body = Body("func next() -> i32 => 1\nfunc f(w: uniq/Utf8Writer) -> Result<(), BufferFull> => $tryWrite(w, \"" + literal + "\")\n", "f");
        Assert.Equal(calls, HealthyPath(body, calls.Length));

        // The first test's failure arm reaches the result's status without the expression (SPEC 22.10.4).
        var failed = new List<string>();
        var first = Array.FindIndex(body.Blocks.ToArray(), static x => x.Terminator.Kind == MirTerminatorKind.SwitchInt);
        for (var block = body.Blocks[first].Terminator.Target; block >= 0 && body.Blocks[block].Terminator.Kind != MirTerminatorKind.Return;)
        {
            var terminator = body.Blocks[block].Terminator;
            if (terminator.Kind == MirTerminatorKind.Call)
            {
                failed.Add(terminator.Callee.Symbol!.Name);
            }

            block = terminator.Target;
        }

        Assert.Equal(["status"], failed);
    }

    // SPEC 22.10.3, 22.10.5 (DECISIONS R5-M133): an owning interpolation is one FormatSegment step per write position, the later literal bytes
    // and values' Types its hint: a literal segment's text is its step's operand and no write, and so is an embedded string literal's (as if
    // written: a constant has no effect); a value's position has the empty text and its write follows. The block a failed write goes to exists
    // only where a write is tested, so text alone builds without validator findings (fail-before: each literal was a write<string> after
    // FormatHint; then text alone left that block unreachable). A $tryWrite's text stays a write (above).
    [Theory]
    [InlineData(
        "func f(n: i32) -> string => \"n=\\(n)!\"\n",
        new[] { "FormatBuffer", "ErasedWriter", "FormatSegment \"n=\" 1 1", "FormatSegment \"\" 1 0", "write", "FormatSegment \"!\" 0 0", "FormatFinish" })]
    [InlineData(
        "func f(n: i32) -> string => \"\\(\"lit\")\\(n)\"\n",
        new[] { "FormatBuffer", "ErasedWriter", "FormatSegment \"lit\" 0 1", "FormatSegment \"\" 0 0", "write", "FormatFinish" })]
    [InlineData("func f() -> string => \"\\(\"only\")\"\n", new[] { "FormatBuffer", "ErasedWriter", "FormatSegment \"only\" 0 0", "FormatFinish" })]
    [InlineData(
        "func f() -> string => \"x\\(\"y\")z\"\n",
        new[] { "FormatBuffer", "ErasedWriter", "FormatSegment \"x\" 2 0", "FormatSegment \"y\" 1 0", "FormatSegment \"z\" 0 0", "FormatFinish" })]
    public void AnOwningInterpolationsLiteralTextIsItsSegmentStep(string source, string[] steps)
        => Assert.Equal(steps, HealthyPath(Body(source, "f"), int.MaxValue));

    // DECISIONS R5-M133: an interpolation of text alone leaves the program analyzable, so a true error elsewhere in it is still reported
    // (fail-before: its unreachable failure block was a validator finding, and the analyses ran on no body).
    [Fact]
    public void TextAloneKeepsTheProgramsOtherErrors()
    {
        var program = Compile(
            "func touch(v: uniq/i32) => ()\nfunc f() -> string\n    var x = 1\n    let r = x@uniq\n    let s = \"a\\(x)b\"\n    touch(r)\n    return s\n" +
            "func only() -> string => \"\\(\"only\")\"\n").BuildMir();
        Assert.Empty(program.Findings);
        Assert.Contains(program.Problems.All.ToArray(), static x => x.Code == DiagnosticCode.ComparisonLoanConflict_Kd);
    }

    // The calls on a body's healthy path from its entry, at most count: a call's, a goto's or a Drop's target and a Result test's Ok arm
    // continue it. An intrinsic is named by its id, a FormatSegment with its text, later bytes and later values' count; another call by its
    // function's name.
    private static List<string> HealthyPath(MirBody body, int count)
    {
        var path = new List<string>();
        for (var block = 0; block >= 0 && path.Count < count;)
        {
            var terminator = body.Blocks[block].Terminator;
            if (terminator.Kind == MirTerminatorKind.Call)
            {
                path.Add(Step(body, terminator));
            }

            block = terminator.Kind switch
            {
                MirTerminatorKind.SwitchInt => Assert.Single(body.Cases.Slice(terminator.Start, terminator.Count).ToArray(), static x => x.Value == 0).Target,
                MirTerminatorKind.Call or MirTerminatorKind.Goto or MirTerminatorKind.Drop => terminator.Target,
                _ => -1,
            };
        }

        return path;

        static string Step(MirBody body, in MirTerminator call)
        {
            if (call.Callee.Kind != MirCalleeKind.Intrinsic)
            {
                return call.Callee.Symbol!.Name;
            }

            var id = (IntrinsicId)call.Callee.Arg;
            var operands = body.Operands.Slice(call.Start, call.Count);
            return id != IntrinsicId.FormatSegment ? id.ToString()
                : $"{id} \"{body.Consts[operands[1].Arg].Value}\" {body.Consts[operands[2].Arg].Bits} {body.Consts[operands[3].Arg].Type.Components.Count}";
        }
    }

    // A function f of the temporaries' program that runs a statement, built without validator findings.
    private static (Compilation C, MirBody Body) Temporaried(string statement)
    {
        var c = Compile(Temporaries + "func f(n: Option<i32>) -> Option<i32>\n    " + statement + "\n    return .Some(0)\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (c, program.Bodies.ToArray().Single(static x => x.Key is { Kind: MirBodyKind.Fn } && x.Key.Decl?.Name == "f"));
    }

    // The one local a key names among those a body drops whole: a binding by its name, else a temp by its syntax or its Type.
    private static int Dropped(Compilation c, MirBody body, string key)
    {
        var dropped = body.Blocks.ToArray().Where(static x => x.Terminator is { Kind: MirTerminatorKind.Drop, Place.Projection: 0 })
            .Select(static x => x.Terminator.Place.Root).Distinct().ToArray();
        return dropped.Any(x => body.Locals[x].Name == key) ? Assert.Single(dropped, x => body.Locals[x].Name == key)
            : Assert.Single(dropped, x => TyCtxt.Name(body.Locals[x].Type) == key || c.SyntaxOf(body.Locals[x].Source.Syntax).ToString() == key);
    }

    // Whether a body drops a local and, in the cleanup chain after some Drop of it (along Drops and Gotos), another; an assignment's Drop
    // of the old value, which the new value's write follows, starts no chain (SPEC 13.7.1).
    private static bool Precedes(MirBody body, int first, int then)
    {
        var blocks = body.Blocks.ToArray();
        foreach (var start in blocks.Where(x => x.Terminator is { Kind: MirTerminatorKind.Drop } drop && drop.Place.Root == first &&
            (body.Statements.Slice(blocks[drop.Target].Start, blocks[drop.Target].Count).ToArray() is not [{ Kind: MirStatementKind.Assign } write, ..]
            || write.Place != drop.Place)))
        {
            var seen = new HashSet<int>();
            for (var block = start.Terminator.Target; block >= 0 && seen.Add(block) && blocks[block].Terminator.Kind is MirTerminatorKind.Drop or
                MirTerminatorKind.Goto; block = blocks[block].Terminator.Target)
            {
                if (blocks[block].Terminator is { Kind: MirTerminatorKind.Drop } drop && drop.Place.Root == then)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether a body's runtime edges lead from one block to another.
    private static bool Reaches(MirBody body, int from, int to)
    {
        var reach = new MirReach();
        reach.Clear(body.Blocks.Length);
        reach.Walk(body, from, checks: false);
        return reach[to];
    }

    // Whether a location reads (Copies or Moves) or writes (assigns or borrows) a Place rooted at a local.
    private struct Uses(int local) : IMirUses
    {
        public bool Reads { get; set; }

        public bool Writes { get; set; }

        public readonly void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.Root == local)
            {
                this.Reads |= (access & (MirAccess.Reads | MirAccess.Moves)) != 0;
                this.Writes |= (access & (MirAccess.Writes | MirAccess.Borrows)) != 0;
            }
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }
    }
}
