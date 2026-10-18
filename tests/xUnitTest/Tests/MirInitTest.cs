// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.CompilerServices;
using System.Text;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>Initialization and moves on MIR (A1, SPEC 15.1, 14.10.3; docs/dev/DECISIONS.md R4-M20, R4-M21): each rule with a valid
/// program and an invalid one, through the problems <c>Compilation.BuildMir</c> finds (R4 shadow: unpublished), each as its code, the first
/// line of its subject and its views; and the move paths a body forms, through <see cref="MirMoveData"/> itself.</summary>
public sealed class MirInitTest(ITestOutputHelper output)
{
    private const string Resource = "struct Resource\n    var name: string\nfunc make() -> Resource => Resource.init(name: \"r\")\n" +
        "func consume(r: Resource) => ()\nfunc inspect(r: ref/Resource) => ()\n";

    // SPEC 15.1.1: a use needs its Place initialized on every incoming runtime path.
    [Fact]
    public void AUseNeedsInitializationOnEveryPath()
    {
        const string Branches = "func use(n: i32) => ()\nfunc f(condition: bool)\n    var number: i32\n    if condition\n        number = 1\n";
        Assert.Empty(Problems(Branches + "    else\n        number = 2\n    use(number)\n"));
        Assert.Equal(["UninitializedPlace_Kd use(number) 1"], Problems(Branches + "    use(number)\n"));
    }

    // SPEC 15.1.1, 15.1.2: a Move leaves its Place Moved until a reinitialization; a use where it may have been moved is MovedPlace. A
    // Field written into a moved value does not construct it again: the write and a whole use after it are both MovedPlace.
    [Fact]
    public void AMovedPlaceIsUsableAgainOnlyAfterReinitialization()
    {
        Assert.Empty(Problems(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    x = make()\n    consume(x@move)\n"));
        Assert.Equal(["MovedPlace_Kd x 1"], Problems(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    inspect(x)\n"));
        Assert.Equal(
            ["MovedPlace_Kd x 1"],
            Problems(Resource + "func f(flag: bool)\n    var x = make()\n    if flag\n        consume(x@move)\n    inspect(x)\n"));
        Assert.Equal(
            ["MovedPlace_Kd x.name = \"n\" 1", "MovedPlace_Kd x 1"],
            Problems(Resource + "func f()\n    var x = make()\n    consume(x@move)\n    x.name = \"n\"\n    inspect(x)\n"));
    }

    // SPEC 15.1.1: a let binding is initialized once per binding lifetime: a loop body's let starts a lifetime per iteration, and neither
    // a Move nor a destruction resets the first initialization.
    [Fact]
    public void ALetIsInitializedOncePerBindingLifetime()
    {
        Assert.Empty(Problems(Resource + "func f(c: bool)\n    while c\n        let s = make()\n        consume(s@move)\n"));
        Assert.Equal(
            ["ReassignedLet_Kd resource = make() 1"],
            Problems(Resource + "func f()\n    let resource: Resource\n    resource = make()\n    consume(resource@move)\n    resource = make()\n"));
    }

    // SPEC 15.1.2, 15.1.3: a Partial Move leaves the value incomplete for whole-value uses until the missing path is reinitialized.
    [Fact]
    public void APartialMoveLeavesTheValueIncomplete()
    {
        const string Pair = "func see(p: (string, i32)) => ()\nfunc f()\n    var pair: (string, i32) = (\"Alice\", 30)\n    let name = pair.0@move\n" +
            "    let age = pair.1\n";
        Assert.Empty(Problems(Pair + "    pair.0 = \"Bob\"\n    see(pair)\n"));
        Assert.Equal(["MovedPlace_Kd see(pair) 1"], Problems(Pair + "    see(pair)\n"));
    }

    // SPEC 6.3, 15.1.2: a constructor builds its object from fresh storage: a base and every own Field placed before it completes
    // (an implicit constructor and an empty struct's included); a missing Field is reported at the constructor. Writing a part of an
    // aggregate that was never constructed is reported at the write.
    [Fact]
    public void AConstructorPlacesEveryField()
    {
        const string Valid = "open struct Base\n    public var count: i32\n    public let name: string\n    protected init(name: string)\n" +
            "        self.name = name@move\n        self.count = 1\nstruct Leaf : Base\n    public var extra: i32 = 7\n" +
            "    public init(name: string, extra: i32) : base(name@move)\n        self.extra = extra\nstruct Plain\n    public var x: i32 = 1\n" +
            "struct Empty\n    public init()\n        ()\nfunc make() -> i32\n    let leaf = Leaf.init(\"a\", 2)\n    let plain = Plain.init()\n" +
            "    let empty = Empty.init()\n    return leaf.extra + plain.x\n";
        Assert.Empty(Problems(Valid));
        Assert.Equal(
            ["UninitializedPlace_Kd public init(a: i32) 1", "UninitializedPlace_Kd p.0 = 1 1"],
            Problems("struct Missing\n    public var a: i32\n    public var b: i32\n    public init(a: i32)\n        self.a = a\n" +
                "func f()\n    var p: (i32, i32)\n    p.0 = 1\n"));
    }

    // SPEC 11.7: a constructor places a let Field or one with a custom set once; a var with the standard set may be replaced.
    [Fact]
    public void AConstructorPlacesALetOrCustomSetFieldOnce()
    {
        Assert.Empty(Problems("struct S\n    var value: i32\n    let name: i32\n    public init()\n        self.value = 1\n        self.value = 2\n" +
            "        self.name = 3\nlet s = S.init()\n"));
        Assert.Equal(
            ["ReassignedLet_Kd self.value = 2 1", "ReassignedLet_Kd self.level = 2 1"],
            Problems("struct S\n    let value: i32\n    public init()\n        self.value = 1\n        self.value = 2\nstruct C\n    public var level: i32\n" +
                "        set(self: uniq/Self, value: i32) -> ()\n            storage = value\n    public init()\n        self.level = 1\n" +
                "        self.level = 2\n"));
    }

    // SPEC 6.3.5: a constructor's object is complete at each exit it requests, its body's end or a return, before the Scope Exit that runs
    // its defers, which cannot supply a missing Field: the problem is at the return and at the constructor, once for an early return; an
    // exit request only the checks reach delivers nothing (SPEC 14.10.3).
    [Fact]
    public void AConstructorIsCompleteWhereItRequestsItsExit()
    {
        Assert.Empty(Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        defer => self.value = 2\n        self.value = 1\n" +
            "        if flag => return\ngroup G\n    public func stop() -> Never\n        loop => ()\nstruct T\n    var value: i32\n    public init()\n" +
            "        G.stop()\n        return\n"));
        Assert.Equal(
            ["UninitializedPlace_Kd return 1", "UninitializedPlace_Kd public init(flag: bool) 1"],
            Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        defer => self.value = 1\n        if flag => return\n"));
        Assert.Equal(
            ["UninitializedPlace_Kd return 1"],
            Problems("struct S\n    var value: i32\n    public init(flag: bool)\n        if flag => return\n        self.value = 1\n"));
    }

    // SPEC 15.1.3, 15.1.5: a Move needs a Move Path: a constant index of a fixed array has one, a runtime index of an Array has none
    // (StaticMovePathRequired_Kd), and such a Move changes no state, so a second one is not MovedPlace; a raw pointer's Take is untracked
    // (SPEC 5.2.3), and a Place a function publishes never offers Take, whatever root holds its reference.
    [Fact]
    public void AMoveNeedsAMovePath()
    {
        Assert.Empty(Problems("func take(s: string) => ()\nfunc f()\n    var arr: [2 of string] = [\"a\", \"b\"]\n    take(arr[0]@move)\n" +
            "    arr[0] = \"c\"\n    let whole = arr@move\n"));
        Assert.Empty(Problems("func take(s: string) => ()\nfunc f(pointer: raw/string)\n    unsafe\n        let first = (*pointer)@move\n" +
            "        let second = (*pointer)@move\n        take(first@move)\n        take(second@move)\n"));
        Assert.Equal(
            ["StaticMovePathRequired_Kd values[i] 1", "StaticMovePathRequired_Kd values[i] 1"],
            Problems("func take(s: string) => ()\nfunc f(values: Array<string>, i: isize)\n    take(values[i]@move)\n    take(values[i]@move)\n"));
        Assert.Equal(
            ["StaticMovePathRequired_Kd first(names)@follow 1"],
            Problems("func first(values: ref/Array<string>) -> ref/string during values => values[0]@ref\nfunc f(names: Array<string>)\n" +
                "    let bare = first(names)@follow\n"));
    }

    // SPEC 15.1.3: a Move of a constant index forms the referenced element's Move Path; the paths follow the accesses, never the declared
    // length (an input-size property of MirMoveData).
    [Fact]
    public void AnElementMoveAddsOnlyItsOwnPath()
    {
        var c = MirBuildTest.Compile("func f()\n    var a: [1000000 of string]\n    let taken = a[42]@move\n");
        var body = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var (flow, moves) = (new MirDataflow(new MirReach()), new MirMoveData());
        flow.Begin(body);
        moves.Build(body, flow);
        var array = body.Locals.ToArray().ToList().FindIndex(static x => x.Name == "a");
        Assert.Equal(2, moves.Paths.ToArray().Count(x => x.Local == array));
    }

    // SPEC 4.7.4 (DECISIONS R4-M56): a selection's receiver is checked once, where it is located: at its guard, the selection, so an
    // access through a runtime index into an uninitialized array is one problem whatever consumes the element.
    [Theory]
    [InlineData("values[i] = 9")]
    [InlineData("use(values[i])")]
    [InlineData("see(values[i]@ref)")]
    [InlineData("values[i] += 1")]
    [InlineData("let x = values[i]")]
    public void ASelectionChecksItsReceiverOnce(string statement)
    {
        var source = $"func use(n: i32) => ()\nfunc see(v: ref/i32) => ()\nfunc f(i: isize)\n    var values: [3 of i32]\n    {statement}\n";
        Assert.Equal(["UninitializedPlace_Kd values[i] 1"], Problems(source));
    }

    // SPEC 4.7.4, 15.1, 23.1 (DECISIONS R4-M56): the access through the index checks the receiver again under the selection's subject, so a
    // receiver Moved while its own index is evaluated is MovedPlace at the selection, independent of the Move's conflict with the guard (A5).
    [Fact]
    public void AReceiverMovedInItsIndexIsMovedAtTheSelection()
    {
        const string Source = "func take(a: (string, [1 of i32])) => ()\nfunc f() -> i32\n    let a: (string, [1 of i32]) = (\"a\", [1])\n" +
            "    let n = a.1[(label work: do\n        take(a@move)\n        exit to work 0\n    )]\n    return n\n";
        Assert.Equal(["MovedPlace_Kd a.1[( 1"], Problems(Source));
    }

    // SPEC 14.10.3: code only the checks reach is checked with the state after the transfer's operand, which is never merged into a runtime
    // path: a value consumed before a return out of a do is usable after the do (plan Appendix B C1), a default that does not complete
    // delivers nothing at its template's Return, an index whose value only the checks reach leaves its compiler temporary unwritten (not a
    // Place of the program), a value moved before a return is MovedPlace after it, and a value whose initializer or assignment diverged is
    // UninitializedPlace where only the checks read it (plan Appendix B C3): the Never value is not placed, nor is the old value dropped
    // (DECISIONS R4-M62); a call whose argument diverged moves its other arguments for the checks (SPEC 14.10.3).
    [Fact]
    public void CheckOnlyCodeKeepsItsOwnState()
    {
        const string Box = "struct Box\n    var value: string\nfunc consume(box: Box) => ()\nfunc stop() -> Never\n    loop => ()\n";
        Assert.Empty(Problems(Box + "func consumedThenReturned(flag: bool, box: Box) -> i32\n    do\n        if flag\n            consume(box@move)\n" +
            "            return 1\n        return 2\n    consume(box@move)\n    return 3\nfunc peek(flag: bool, value: i32 = stop()) -> i32 => value\n" +
            "func caller() -> i32 => peek(true)\nfunc index()\n    var a: (string, [1 of i32]) = (\"held\", [0])\n    a.1[label work: do\n" +
            "        defer => loop => ()\n        exit to work 0\n    ] = 42\nfunc replaced(box: Box) -> i32\n    var b = box@move\n    b = stop()\n" +
            "    consume(b@move)\n    return 1\n"));
        Assert.Equal(
            ["MovedPlace_Kd box 1", "UninitializedPlace_Kd x 1", "UninitializedPlace_Kd y 1", "MovedPlace_Kd box 1"],
            Problems(Box + "func afterReturn(box: Box) -> i32\n    consume(box@move)\n    return 1\n    consume(box@move)\n    return 2\n" +
                "func diverged() -> i32\n    let x: i32 = stop()\n    return x\nfunc assigned() -> i32\n    var y: i32\n    y = stop()\n    return y\n" +
                "func take(box: Box, n: i32) => ()\nfunc continued(box: Box) -> i32\n    take(box@move, (return 1))\n    consume(box@move)\n    return 2\n"));
    }

    // SPEC 17.6.3 (PD22): a verification's records read the temps its comparison's operands were read into once, initialized and not yet
    // moved: made Places of the program (A1 reports a Var's state, never a temp's), they have no problem, where a record reading an
    // operand after the comparison's Move would be MovedPlace.
    [Fact]
    public void RecordsReadOperandsTheComparisonHasNotMoved()
    {
        var c = MirBuildTest.Compile(
            "func helper(x: i32) -> i32 => x + 1\n#Test\nfunc sample()\n    let a = 3\n    $expect(helper(1) == a)\n    $expect((a < 4))\n",
            test: true);
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key.Kind == MirBodyKind.Test);
        var read = MirBuildTest.Records(body).Select(x => body.Operands[x.Start + 1]).Where(static x => x.Kind == MirOperandKind.Copy).ToArray();
        Assert.Equal(3, read.Length);
        foreach (var operand in read)
        {
            ref readonly var local = ref body.Locals[operand.Place.Root];
            Unsafe.AsRef(in local) = local with { Kind = MirLocalKind.Var };
        }

        new MirAnalysis(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
    }

    // SPEC 3.1.3 (DECISIONS R5-M38): a named Place of Type Never takes the acquisition Binding publishes, Copy for a bare read (Never is
    // Copy, BU-4), into its node's Never temp, so a Never parameter read twice is no MovedPlace.
    [Fact]
    public void ANeverPlaceIsAcquiredAsBindingPublishes()
    {
        const string Source = "func g(a: Never, b: Never) => ()\nfunc absurd(x: Never) => g(x, x)\n";
        var c = MirBuildTest.Compile(Source);
        Assert.Empty(c.BuildMir().Findings);
        var reads = KotoTree.Walk(c.Kotonoha.RootKoto).Where(static x => x.ToString() == "x" && x.Parent is InvocationKoto);
        Assert.Equal([HirAcquire.Copy, HirAcquire.Copy], reads.Select(static x => HirTables.AdjustOf(x).Acquire));
        Assert.Empty(Problems(Source));
    }

    // SPEC 12.2 (DECISIONS R5-M38): a named Never Place read before a later operand builds code is acquired into its node's Never temp,
    // which secures it and the call then moves: made a Place of the program, the temp has no problem, where an unwritten one is
    // uninitialized.
    [Fact]
    public void ASecuredNeverPlaceIsWritten()
    {
        var c = MirBuildTest.Compile("func k() -> i32 => 1\nfunc g2(a: Never, b: i32) => ()\nfunc absurd(x: Never) => g2(x, k())\n");
        var program = c.BuildMir();
        var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "absurd");
        var call = body.Blocks.ToArray().Select(static x => x.Terminator).Single(static x => x.Callee.Symbol?.Name == "g2");
        var secured = body.Operands[call.Start];
        Assert.Equal((MirOperandKind.Move, MirLocalKind.Temp), (secured.Kind, body.Locals[secured.Place.Root].Kind));
        ref readonly var local = ref body.Locals[secured.Place.Root];
        Unsafe.AsRef(in local) = local with { Kind = MirLocalKind.Var };
        new MirAnalysis(c.Types).Run(program);
        Assert.DoesNotContain(program.Problems.All.ToArray(), static x => x.Subject.Module == 0);
    }

    // SPEC 14.10.3, 3.8 (DECISIONS R4-M62, R5-M38; BU-6): a read of a named Never Place supplies no normal completion, so its assumed
    // completion initializes nothing it is placed into: the Place a let, an assignment or a Never var takes from it stays uninitialized,
    // each later read of it an UninitializedPlace.
    [Theory]
    [InlineData("func absurd(x: Never) -> i32\n    let y: i32 = x\n    return y\n", "UninitializedPlace_Kd y 1")]
    [InlineData("func absurd(x: Never) -> i32\n    let y: i32\n    y = x\n    return y\n", "UninitializedPlace_Kd y 1")]
    [InlineData(
        "func g(a: Never) => ()\nfunc absurd(x: Never)\n    var y: Never = x\n    g(y)\n    g(y)\n",
        "UninitializedPlace_Kd y 1 | UninitializedPlace_Kd y 1")]
    public void ANeverReadPlacesNothing(string source, string expected)
        => Assert.Equal(expected.Split(" | "), Problems(source));

    // Plan 5.6.1, SPEC 13.5.5: a PairLayer is the Place itself in the owner case and a Deref in a reference case, so writing through an
    // unset pair layer is a problem in the uniq view only; a body whose accesses no view decides is analyzed once, for every view.
    [Fact]
    public void ViewsDecidePairLayers()
    {
        Assert.Equal(
            ["UninitializedPlace_Kd v@follow = x 2", "UninitializedPlace_Kd y 3"],
            Problems("func write<s/T>(x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var v: s/T\n    v@follow = x\n" +
                "func plain<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    let y: T\n    return y\n"));
        Assert.Empty(Problems(File.ReadAllText(DiagnosticCorpus.RepositoryPath("tests", "mir", "cases.kimi"))));
    }

    // R4-M15a: a body whose state arena would exceed the analyses' byte limit (64 MiB) records OwnershipStorageLimit_Kd once, at the body,
    // and is not analyzed: N locals live across N branches need about N heads × 5 lanes × N/64 words, so the unset read of y is reported at
    // N = 64 and not at N = 13,500.
    [Fact]
    public void AnArenaAboveTheLimitStopsTheBody()
    {
        foreach (var (size, expected) in new[] { (64, "UninitializedPlace_Kd y 1"), (13_500, "OwnershipStorageLimit_Kd func f(flag: bool) -> i32 1") })
        {
            var text = new StringBuilder("func f(flag: bool) -> i32\n    let y: i32\n");
            for (var n = 0; n < size; n++)
            {
                text.Append("    var v").Append(n).Append(" = ").Append(n).Append('\n');
            }

            for (var n = 0; n < size; n++)
            {
                text.Append("    if flag => v").Append(n).Append(" = 1\n");
            }

            Assert.Equal([expected], Problems(text.Append("    return y\n").ToString()));
        }
    }

    // SPEC 4.3.4, 4.9.5: noinit and an empty collection literal construct initialized values.
    [Fact]
    public void ConstructedAggregatesAreInitialized()
        => Assert.Empty(Problems("func noinit() -> i32\n    unsafe\n        var values: [4 of i32] = noinit\n        values[1] = 2\n" +
            "        return values[0]\nfunc keep(values: Array<i32>) => ()\nfunc empty()\n    let values: Array<i32> = []\n    keep(values@move)\n"));

    // V16 (plan "New V-checks"): A1 is linear in the body: N 16/32/64 groups of a straight move, a branch that moves on both arms and a loop
    // that moves and reinitializes grow the allocation of the body's cold analysis (fresh components, the driver's per-body steps with A2,
    // which A1's checks replay feeds; a fresh driver's pools would hold the widest library body) linearly, with no problem; the output gives
    // the dense arena's words (R4-M20).
    // MirBuildTest.WarmRebuildAllocatesNothing covers the warm re-analysis, which BuildMir runs.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("    let v{0} = text({0})\n    keep(v{0}@move)\n")]
    [InlineData("    let v{0} = text({0})\n    if flag => keep(v{0}@move) else => other(v{0}@move)\n")]
    [InlineData("    var v{0} = text({0})\n    while flag\n        keep(v{0}@move)\n        v{0} = text({0})\n")]
    public void InitializationScalesLinearly(string group)
    {
        var (sizes, work, allocated, arena) = (new[] { 16, 32, 64 }, new long[3], new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var text = new StringBuilder("func text(n: i32) -> string => \"\\(n)\"\nfunc keep(s: string) => ()\nfunc other(s: string) => ()\n");
            text.Append("func work(flag: bool)\n");
            for (var n = 0; n < sizes[i]; n++)
            {
                text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, group, n);
            }

            var c = MirBuildTest.Compile(text.ToString());
            var program = c.BuildMir();
            Assert.Empty(program.Problems.All.ToArray());
            var body = program.Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "work");
            work[i] = body.Blocks.Length + body.Statements.Length;
            var (flow, moves, cases, sink) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirProgram());
            var drops = new MirDrops(moves, cases);
            var init = new MirInit(flow, moves, cases, drops);
            var before = GC.GetAllocatedBytesForCurrentThread();
            flow.Begin(body);
            moves.Build(body, flow);
            drops.Begin(body, sink);
            cases.Select(body, 0);
            Assert.True(init.Run(body, 0, sink.Problems));
            drops.Judge(0);
            drops.End();
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, sink.Problems.All.Length);
            arena[i] = ArenaWords(body, flow, moves);
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: MIR size {string.Join(", ", work)}, cold analysis allocation {string.Join(", ", allocated)}, " +
            $"arena words {string.Join(", ", arena)}");
        Assert.True(work[2] - work[1] <= (2 * (work[1] - work[0])) + 8, $"MIR size {string.Join(", ", work)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
    }

    // R4-M20: the words of A1's state arena for a body begun and built: its heads × the stored words of its lanes.
    internal static long ArenaWords(MirBody body, MirDataflow flow, MirMoveData moves)
        => (body.Blocks.ToArray().Select((_, b) => flow.Head(b)).Max() + 1) * (long)MirInit.Lanes * ((moves.FlowCount + 63) / 64);

    // The problems of a source's own bodies: code, the first line of the subject, and the views.
    internal static string[] Problems(string source)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return [.. program.Problems.All.ToArray().Where(static x => x.Subject.Module == 0)
            .Select(x => $"{x.Code} {c.SyntaxOf(x.Subject).ToString().Split('\n')[0].Trim()} {x.Views}")];
    }
}
