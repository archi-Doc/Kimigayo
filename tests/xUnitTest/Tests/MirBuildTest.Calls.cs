// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>The calls of the MIR build: reservations of exclusive inputs, evaluation order and operand reads, pair acquisitions, diverging
/// arguments and Value callees.</summary>
public sealed partial class MirBuildTest
{
    // SPEC 15.6.7: every exclusive borrow that produces an invocation's argument or receiver is reserved (TwoPhaseUniq): a written @uniq,
    // also in parentheses, the Reborrow of a stored uniq at a uniq parameter and a synthesized receiver (an Array literal's reserve and
    // appends); a written @uniq on a receiver's path is not formed (DECISIONS R4-M52), so the receiver has one reservation, at its lending
    // point; a borrow saved in storage or a control-flow result is live when formed (Uniq).
    [Fact]
    public void ReservedCallInputsAreTwoPhase()
    {
        var source = "func take(a: uniq/i32, b: i32) => ()\nfunc f()\n    var p: i32 = 1\n    take(p@uniq, p + 1)\n    take((p@uniq), 2)\n" +
            "    var r = p@uniq\n    take(r, 3)\n    let values = [1, 2]\nstruct Counter\n    var count: i32\n    public init() => self.count = 0\n" +
            "    public func bump(self: uniq/Self, by: i32) => self.count = self.count + by\nstruct Holder\n    public var c: Counter\n" +
            "    public init() => self.c = Counter.init()\nfunc g(h: uniq/Holder)\n    h@follow@uniq.c.bump(1)\n    h@follow@uniq.c@uniq.bump(2)\nfunc chosen()\n    var p: i32 = 1\n" +
            "    take(if p > 0 => p@uniq else => p@uniq, 4)\nstruct Bag\n    public var items: Array<i32>\n    public var count: i32\n" +
            "    public init()\n        self.items = []\n        self.count = 0\nfunc fill()\n    var bag = Bag.init()\n    bag@uniq.items.append(bag.count)\n";
        Assert.Equal(
            [MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq, MirRefKind.Uniq, MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq,
                MirRefKind.TwoPhaseUniq, MirRefKind.TwoPhaseUniq],
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
    // borrow, a tuple operator's leaves, R5-M75), an increment's one is of the old value's Type, not of its Never-typed target's, and a
    // rejected node of Type Never keeps its Opaque value: each body validates.
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
        var deep = string.Concat(Enumerable.Repeat("Wrap<", 80)) + "i32" + new string('>', 80);
        var c = Compile("struct Wrap<V>\n    Self is Equatable\n    public init() => ()\n" +
            "    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n" +
            $"func f(a: ref/({deep}, i32), b: ref/({deep}, i32)) -> bool => a == b\n");
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var record = Assert.Single(program.Records, static x => x.Subject.Module == 0);
        Assert.Equal((DiagnosticCode.Unsupported_Kd, "a == b"), (record.Code, c.SyntaxOf(record.Subject).ToString()));
        Assert.True(program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f").HasOpaque);
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
