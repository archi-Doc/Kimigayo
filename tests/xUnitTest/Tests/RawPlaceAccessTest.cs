// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 5.2.3: raw Places, and their Fields and elements, offer Take without tracked state. (*p)@move takes the value and leaves
// the designated Place holding no value, _ = (*p)@move destroys it in place, and a bare acquisition follows the rule for every Place: a
// Copy value is Copied and a Non-Copy or Copy-unproven value needs @move (SPEC 3.5).
public class RawPlaceAccessTest
{
    // SPEC 5.7's Lens, whose slot keeps a shared Loan, its exclusive twin ULens, a struct pointee and a `during self` holder.
    internal const string Lenses = "struct P\n    public var f: i32\n    public init() => self.f = 0\n" +
        "struct Lens<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "    internal init(target: ref/T during source, at: raw/T during source)\n        self.at = at\n        self.loan = Loan.init(target)\n" +
        "struct ULens<T> {source}\n    internal let loan: Loan<uniq/T during source>\n    internal let at: raw/T during source\n" +
        "    internal init(target: uniq/T during source, at: raw/T during source)\n        self.at = at\n        self.loan = Loan.init(target)\n" +
        "struct B\n    internal var data: raw/i32 during self\n    public init() => self.data = null\n";

    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";
    private const string View = "struct View {source}\n    public let item: ref/Resource during source\n    public init(item: ref/Resource during source) => self.item = item\n";

    // SPEC 5.7's example as written.
    private const string SpecLens = "struct Lens<T> {source}\n    internal let loan: Loan<ref/T during source>\n    internal let at: raw/T during source\n" +
        "    internal init(target: ref/T during source)\n        self.at = target@follow@raw\n        self.loan = Loan.init(target)\n" +
        "func read(lens: ref/Lens<i32>) -> i32\n    unsafe => return *lens.at\nfunc clear(lens: uniq/Lens<i32>)\n    unsafe => *lens.at = 0\n" +
        "func adopt(pointer: raw/i32) -> raw/i32 during static\n    unsafe => return pointer@(raw/i32 during static)";

    // A destroyed Res prints whether it was destroyed in place, its address equal to its `home`.
    private const string Res = "struct Res\n    public var home: usize = 0\n    drop\n" +
        "        if self@raw@usize == self.home => Console.writeLine(\"in place\") else => Console.writeLine(\"moved\")\n" +
        "struct Tracer\n    drop => Console.writeLine(\"tracer\")\n";

    // SPEC 4.7.1, 15.1.3, 15.1.5 (PLAN G672): a Take of a part of a raw Place that its storage does not hold itself (TyCtxt.RawPart).
    // An Array element, generic or not, directly, in a Field or Tuple element, holding a fixed array, Tuple or Field, or inside a
    // fixed-array element, taken as a discard, a binding, an assignment source or in a Closure, is the referent of `index`, which
    // forms no Move Path: A1's StaticMovePathRequired_Kd, as for a local Array's element. A part past a reference stored in raw storage
    // is a referent: Binding's ExclusivePathTake_Kd (MIR's record before G672 rests on it, a dependent one).
    public static TheoryData<string, string> NoRawPartTakes => new()
    {
        { "func arrayTake(p: raw/Array<Res>)\n    unsafe => _ = (*p)[0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        { "func arrayTake(p: raw/Array<Res>)\n    unsafe => _ = p[0][0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        { "func take<T>(p: raw/Array<T>)\n    unsafe => _ = (*p)[0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        {
            "struct Holder\n    public var a: Array<Res>\npublic func main()\n    let p = Raw.allocate<Holder>(1)\n    unsafe\n" +
            "        _ = (*p).a[0]@move\n        Raw.release(p)\n", "StaticMovePathRequired_Kd"
        },
        {
            "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    unsafe\n        let r = (*p)[0]@move\n        Raw.release(p)\n",
            "StaticMovePathRequired_Kd"
        },
        {
            "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    var x = Res.init()\n    unsafe\n        x = (*p)[0]@move\n" +
            "        Raw.release(p)\n", "StaticMovePathRequired_Kd"
        },
        {
            "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    let f = func [p] () => unsafe => _ = (*p)[0]@move\n" +
            "    unsafe => Raw.release(p)\n", "StaticMovePathRequired_Kd"
        },
        {
            "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    unsafe => Raw.initialize(p, [Res.init()])\n" +
            "    var f = func [p] () => unsafe => _ = (*p)[0]@move\n    f()\n    unsafe => Raw.release(p)\n", "StaticMovePathRequired_Kd"
        },
        { "func nested(p: raw/Array<[2 of Res]>)\n    unsafe => _ = (*p)[0][1]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        { "func nested(p: raw/Array<[2 of [2 of Res]]>)\n    unsafe => _ = (*p)[0][1][0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        {
            "func nested(p: raw/Array<Box>)\n    unsafe => _ = (*p)[0].r@move\nstruct Box\n    public var r: Res\n" +
            "    public init(r: Res) => self.r = r@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd"
        },
        {
            "struct Holder\n    public var a: Array<(Res, i32)>\nfunc nested(p: raw/Holder)\n    unsafe => _ = p[0].a[0].0@move\n" +
            "public func main() => ()\n", "StaticMovePathRequired_Kd"
        },
        { "func nested(p: raw/(i32, Array<Res>))\n    unsafe => _ = (*p).1[0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        { "func nested(p: raw/[2 of Array<Res>])\n    unsafe => _ = (*p)[1][0]@move\npublic func main() => ()\n", "StaticMovePathRequired_Kd" },
        {
            "struct Link\n    public var q: raw/Array<Res>\nfunc take(p: raw/Link)\n    unsafe => _ = (*(*p).q)[0]@move\n" +
            "public func main() => ()\n", "StaticMovePathRequired_Kd"
        },
        { "public func main()\n    let p: raw/(uniq/[2 of Res]) = Raw.allocate(1)\n    unsafe => _ = (*p)[0]@move\n", "ExclusivePathTake_Kd" },
        { "public func main()\n    let p: raw/(uniq/(Res, i32)) = Raw.allocate(1)\n    unsafe => _ = (*p).0@move\n", "ExclusivePathTake_Kd" },
        {
            "struct Box\n    public var r: Res\n    public init(r: Res) => self.r = r@move\npublic func main()\n" +
            "    let p: raw/(uniq/Box) = Raw.allocate(1)\n    unsafe => _ = (*p).r@move\n", "ExclusivePathTake_Kd"
        },
    };

    // SPEC 5.2, 4.7.1, 15.1.5, 6.4 (PLAN G672): the parts a raw Place's storage holds itself offer an untracked Take: fixed-array
    // elements at a runtime, constant or from-end position, also generic and nested, a raw pointer's own elements, Tuple elements, a
    // fixed array in a Tuple element, own and base Fields, a whole Array as a fixed-array element or as a Field, the pointee of a raw
    // pointer stored in a raw Place's Field, and a Take in a Closure; an Array element of a raw Place is still written, borrowed (each
    // borrow a fresh anchor, SPEC 5.2.2) and addressed, also through `p[n]`.
    public static TheoryData<string> RawPartTakes => new()
    {
        "func at(q: raw/[2 of Res], i: isize)\n    unsafe\n        _ = (*q)[i]@move\n        _ = (*q)[0]@move\npublic func main()\n" +
            "    let q = Raw.allocate<[2 of Res]>(1)\n    unsafe => Raw.initialize(q, [Res.init(), Res.init()])\n    at(q, 1)\n" +
            "    unsafe => Raw.release(q)\n    Console.writeLine(\"end\")\n",
        "public func main()\n    let p = Raw.allocate<Res>(2)\n    unsafe\n        Raw.initialize(p, Res.init())\n" +
            "        Raw.initialize(p + 1, Res.init())\n        _ = p[1]@move\n        let r = p[0]@move\n        Raw.release(p)\n" +
            "    Console.writeLine(\"end\")\n",
        "public func main()\n    let p = Raw.allocate<(Res, Res)>(1)\n    unsafe\n        Raw.initialize(p, (Res.init(), Res.init()))\n" +
            "        _ = (*p).0@move\n        _ = (*p).1@move\n        Raw.release(p)\n    Console.writeLine(\"end\")\n",
        "struct Box\n    public var r: Res\n    public init(r: Res) => self.r = r@move\npublic func main()\n" +
            "    let p = Raw.allocate<(i32, [2 of Res])>(1)\n    let b = Raw.allocate<Box>(1)\n    unsafe\n" +
            "        Raw.initialize(p, (1, [Res.init(), Res.init()]))\n        _ = (*p).1[0]@move\n        _ = (*p).1[1]@move\n" +
            "        Raw.initialize(b, Box.init(Res.init()))\n        _ = (*b).r@move\n        Raw.release(p)\n        Raw.release(b)\n" +
            "    Console.writeLine(\"end\")\n",
        "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    unsafe\n        Raw.initialize(p, [Res.init()])\n" +
            "        (*p)[0] = Res.init()\n        let r = (*p)[0]@ref\n        require r.home == 0 else => $abort(\"r\")\n" +
            "        let q = (*p)[0]@raw\n        (*q).home = q@usize\n        p[0][0].home = 0\n        let u = (*p)[0]@uniq\n" +
            "        u.home = 0\n        let a = (*p)@move\n        Raw.release(p)\n    Console.writeLine(\"end\")\n",
        "public func main()\n    let p = Raw.allocate<[2 of Array<Res>]>(1)\n    unsafe\n        Raw.initialize(p, [[Res.init()], []])\n" +
            "        _ = (*p)[0]@move\n        _ = (*p)[1]@move\n        Raw.release(p)\n    Console.writeLine(\"end\")\n",
        "public func main()\n    let p = Raw.allocate<Array<Res>>(1)\n    unsafe\n        Raw.initialize(p, [Res.init()])\n" +
            "        let a = (*p)[0]@uniq\n        let b = (*p)[0]@uniq\n        a.home = 1\n        b.home = 2\n        a.home = 3\n",
        "public func main()\n    var p = Raw.allocate<Array<Res>>(1)\n    let keep = p\n    unsafe\n        Raw.initialize(p, [Res.init()])\n" +
            "        let a = (*p)[0]@uniq\n        p = null\n        a.home = 0\n        let whole = (*keep)@move\n        Raw.release(keep)\n" +
            "    Console.writeLine(\"end\")\n",
        "func look(r: ref/Res) -> usize => r.home\nfunc at(q: raw/Res) => unsafe => (*q).home = 7\npublic func main()\n" +
            "    let p = Raw.allocate<Array<Res>>(1)\n    unsafe\n        Raw.initialize(p, [Res.init()])\n        at(p[0][0]@raw)\n" +
            "        require look(p[0][0]@ref) == 7 else => $abort(\"look\")\n        let whole = p[0]@move\n        Raw.release(p)\n" +
            "    Console.writeLine(\"end\")\n",
        "func take(q: raw/[2 of Res])\n    unsafe => _ = (*q)[^1]@move\npublic func main() => ()\n",
        "func take<T>(p: raw/[2 of T])\n    unsafe => _ = (*p)[1]@move\npublic func main()\n    let q = Raw.allocate<[2 of Res]>(1)\n" +
            "    unsafe => Raw.initialize(q, [Res.init(), Res.init()])\n    take(q)\n    unsafe\n        _ = (*q)[0]@move\n" +
            "        Raw.release(q)\n    Console.writeLine(\"end\")\n",
        "struct Holder\n    public var a: Array<Res>\n    public init(a: Array<Res>) => self.a = a@move\npublic func main()\n" +
            "    let p = Raw.allocate<[2 of [2 of Res]]>(1)\n    let h = Raw.allocate<Holder>(1)\n    let i: isize = 1\n    unsafe\n" +
            "        Raw.initialize(p, [[Res.init(), Res.init()], [Res.init(), Res.init()]])\n        _ = (*p)[1][0]@move\n" +
            "        _ = (*p)[i][i]@move\n        _ = p[0][0]@move\n        let last = (*p)[0][i]@move\n" +
            "        Raw.initialize(h, Holder.init([Res.init()]))\n        let whole = h[0].a@move\n        Raw.release(p)\n" +
            "        Raw.release(h)\n    Console.writeLine(\"end\")\n",
        "struct Link\n    public var q: raw/[2 of Res]\nfunc take(p: raw/Link)\n    unsafe => _ = (*(*p).q)[1]@move\npublic func main() => ()\n",
        "public func main()\n    let p = Raw.allocate<[1 of Res]>(1)\n    unsafe => Raw.initialize(p, [Res.init()])\n" +
            "    var f = func [p] () => unsafe => _ = (*p)[0]@move\n    f()\n    unsafe => Raw.release(p)\n    Console.writeLine(\"end\")\n",
        "open struct Animal\n    public var r: Res\nstruct Dog : Animal\n    public var s: Res\nfunc take(p: raw/Dog)\n    unsafe\n" +
            "        _ = (*p).r@move\n        _ = (*p).s@move\npublic func main() => ()\n",
    };

    [Theory]
    [MemberData(nameof(NoRawPartTakes))]
    public void APartARawPlaceDoesNotHoldOffersNoTake(string source, string code)
        => Assert.Equal([code], DiagnosticCorpus.Check(Res + source).Diagnostics.Select(static x => x.Code));

    [Theory]
    [MemberData(nameof(RawPartTakes))]
    public void ARawPlacesOwnPartsOfferTake(string source)
    {
        var c = MinimalEmissionTest.Analyze(Res + source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let taken = *pointer")]
    [InlineData("let taken = pointer[0]")]
    [InlineData("_ = *pointer")]
    [InlineData("consume(*pointer)")]
    public void ABareNonCopyTakeIsRejected(string take)
    {
        var source = Resource + "func consume(value: Resource) => ()\nfunc run(pointer: raw/Resource)\n    unsafe\n        " + take + "\npublic func main() => ()\n";
        var diagnostic = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.TransferRequired_Kd), diagnostic.Code);

        // SPEC 5.2.3, 23.1.3: a raw Place takes with @move, around the whole dereference so that it does not apply to the pointer.
        var repair = Assert.Single(diagnostic.Repairs!);
        Assert.Equal("Repair.Transfer", repair.Kind);
        Assert.Equal([RepairCondition.Take], repair.Verified);
        var repaired = UnnecessaryUnsafeBlockTest.Apply(source, repair.Edits);
        Assert.Contains(take.Contains('*') ? take.Replace("*pointer", "(*pointer)@move", StringComparison.Ordinal) : take.Replace("pointer[0]", "pointer[0]@move", StringComparison.Ordinal), repaired, StringComparison.Ordinal);
        Assert.Empty(DiagnosticCorpus.Check(repaired).Diagnostics);
    }

    // SPEC 15.1.6, 3.4.1, 5.3: a raw Place Subject, `*p` or `p[n]`, is a bare Place, borrowed in place and never acquired by value; with
    // shared access its payload binds `ref/T`, also for a Copy T, so a `var` copy of the binding is a `ref/i32` that 7 does not fit.
    [Theory]
    [InlineData("func f(p: raw/Option<string>)\n    unsafe\n        match (*p)\n            .Some(let s) => ()\n            .None => ()\n", null)]
    [InlineData("func f(p: raw/Array<string>) -> usize\n    var n: usize = 0\n    unsafe\n        for s in (*p)\n            n += 1\n    return n\n", null)]
    [InlineData("func f(p: raw/Option<Resource>) -> i32\n    unsafe\n        match p[1]\n            .Some(let x) => return x.value\n            .None => return 0\n", null)]
    [InlineData("func f(p: raw/Option<i32>) -> i32\n    unsafe\n        match (*p)\n            .Some(let x) => return x@follow\n            _ => return 0\n", null)]
    [InlineData("func f(p: raw/Option<i32>) -> i32\n    unsafe\n        match (*p)\n            .Some(let x)\n                var y = x\n                y = 7\n                return y\n            _ => return 0\n", "TypeMismatch_Kd: 7")]
    public void ARawPlaceSubjectIsBorrowedInPlace(string function, string? record)
        => Assert.Equal(record is null ? [] : [record], RawPointerSyntaxTest.Records(Resource + function));

    [Theory]
    // The bare Take of a Copy-unproven raw Place is Binding's TransferRequired_Kd (SPEC 3.5, 5.2.3; R6-check H1, DECISIONS BU-84).
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return *pointer\n", "TransferRequired_Kd *pointer")]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\n", "")]
    [InlineData("func take(pointer: raw/i32) -> i32\n    unsafe => return *pointer\n", "")]
    public void ACopyUnprovenTakeNeedsMove(string function, string records)
    {
        var c = MinimalEmissionTest.Analyze(function + "public func main() => ()\n");
        Assert.Equal(records, string.Join("|", c.Binding.Issues.Select(static x => $"{x.Code} {x.Node}")));
        Assert.True(records.Length != 0 || c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    public void AnExplicitTakeMovesAndDestroys()
    {
        var source = Resource + $$"""
            {{ForeignEmissionTest.Pages()}}
            public func main()
                unsafe
                    let bytes = Native.allocate(null, 4096, 12288, 4)
                    require bytes != null else => $abort("allocate")
                    let storage = bytes@raw/Resource
                    Raw.initialize(storage, Resource.init(1))
                    Raw.initialize(storage + 1, Resource.init(2))
                    let taken = (*storage)@move
                    require taken.value == 1 else => $abort("take")
                    _ = storage[1]@move
                    Console.writeLine("taken")
                    require Native.free(bytes, 0, 32768) != 0 else => $abort("free")
                Console.writeLine("done")
            """;
        ScalarEmissionTest.EmitFixture("RawPlaceTake", source, "drop\ntaken\ndrop\ndone\n");
    }

    // SPEC 5.2.2: @ref and @uniq borrow a raw Place, a Field or an element of one. The referent is a fresh anchor, so the result
    // Origin is fitted to the expected Type, result or storage destination, and other Places are not compared with it. An implicit
    // borrow at a fixed `ref/U` (SPEC 10.2) is the same borrow (PLAN G688).
    [Theory]
    [InlineData("func get(pointer: raw/Resource, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return *pointer\n")]
    [InlineData("func get(p: raw/Array<Resource>, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*p)[0]\n")]
    [InlineData("struct Pair\n    public var r: Resource\nfunc get(p: raw/Pair, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*p).r\n")]
    [InlineData("func get(p: raw/Resource) -> ref/Resource during static\n    unsafe => return p[0]\n")]
    [InlineData("func pick(anchor: ref/Resource during a) -> ref/Resource during a\n    let p = anchor@follow@raw\n    unsafe => return *p\n")]
    [InlineData("func first(a: ref/Array<Resource>) -> ref/Resource during a => a[0]\nfunc get(p: raw/Array<Resource>, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return first(*p)\n")]
    [InlineData("func get(p: raw/[2 of Resource], owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*p)[^1]\n")]
    [InlineData("func get(p: raw/[2 of Resource], owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*p)[^1]@ref\n")]
    [InlineData("func get(pointer: raw/Resource, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*pointer)@ref\n")]
    [InlineData("func get(pointer: raw/Resource, owner: uniq/Resource) -> uniq/i32 during owner\n    unsafe => return pointer[1].value@uniq\n")]
    [InlineData("func get(pointer: raw/Resource) -> ref/Resource\n    unsafe => return pointer[0]@ref/Resource\n")]
    [InlineData("struct View {source}\n    public let item: ref/Resource during source\n    public init(item: ref/Resource during source) => self.item = item\nfunc view(pointer: raw/Resource, owner: ref/Resource) -> View during owner\n    unsafe => return View.init((*pointer)@ref)\n")]
    // An Array element of a raw Place is borrowed through its receiver, the raw Place (SPEC 4.7.1): the anchor, which offers no Take
    // (PLAN G672), is fitted to a result's Origin, `static`, or the pointer's stated Origin.
    [InlineData("func get(p: raw/Array<Resource>, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*p)[0]@ref\n")]
    [InlineData("func get(p: raw/Array<Resource>, owner: uniq/Resource) -> uniq/Resource during owner\n    unsafe => return (*p)[0]@uniq\n")]
    [InlineData("func get(p: raw/Array<Resource>) -> ref/Resource during static\n    unsafe => return p[0][0]@ref\n")]
    [InlineData("func pick(anchor: ref/Array<Resource> during a) -> ref/Resource during a\n    let p = anchor@follow@raw\n    unsafe => return (*p)[0]@ref\n")]
    public void ARawPlaceBorrowFitsItsDestination(string function)
    {
        var c = MinimalEmissionTest.Analyze(Resource + function + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A returned borrow of a raw Place, explicit or implicit (SPEC 10.2), is the anchor fitted to the declared result, in concrete
    // and generic functions; its emission secures the return like any other borrow's.
    [Fact]
    public void AReturnedRawPlaceBorrowTakesTheResultOrigin()
        => ScalarEmissionTest.EmitFixture("RawPlaceBorrowResult", "func get(p: raw/i32, owner: ref/i32) -> ref/i32 during owner\n    unsafe => return (*p)@ref\nfunc generic<T>(p: raw/T, owner: ref/T) -> ref/T during owner\n    unsafe => return (*p)@ref\nfunc implicit(p: raw/i32, owner: ref/i32) -> ref/i32 during owner\n    unsafe => return *p\nfunc implicitGeneric<T>(p: raw/T, owner: ref/T) -> ref/T during owner\n    unsafe => return *p\nvar number: i32 = 5\nlet r = get(number@raw, number@ref)\nlet g = generic(number@raw, number@ref)\nlet i = implicit(number@raw, number@ref)\nlet j = implicitGeneric(number@raw, number@ref)\nrequire r == 5 and g == 5 and i == 5 and j == 5 else => $abort(\"value\")\nConsole.writeLine(\"returned\")", "returned\n");

    // SPEC 5.2.2, 15.6.7: an exclusive borrow of a raw Place, its Field or element, passed at an exclusive argument or as an
    // exclusive receiver is reserved and Reborrowed for the call like any other exclusive argument; the callee's writes reach
    // the storage.
    [Fact]
    public void ARawPlaceBorrowIsAReservedArgument()
    {
        const string Source = """
            struct Counter
                public var value: i32
                public init(value: i32) => self.value = value
                public func bump(self: uniq/Self, by: i32) => self.value += by
            func change(item: uniq/i32, by: i32) => item@follow += by
            func reset(counter: uniq/Counter) => counter.value = 10
            func run(number: raw/i32, counter: raw/Counter)
                unsafe
                    change((*number)@uniq, 2)
                    reset((*counter)@uniq)
                    change((*counter).value@uniq, 3)
                    (*counter).bump(4)
                    change(number[0]@uniq, 1)
            var number: i32 = 5
            var counter = Counter.init(0)
            run(number@raw, counter@raw)
            require number == 8 and counter.value == 17 else => $abort("value")
            Console.writeLine("changed")
            """;
        ScalarEmissionTest.EmitFixture("RawPlaceReservedArgument", Source, "changed\n");
    }

    // Two borrows of raw Places are not compared: their non-overlap is the unsafe obligation.
    [Fact]
    public void FreshAnchorsAreNotCompared()
    {
        var source = Resource + "func run(pointer: raw/Resource)\n    unsafe\n        let first = (*pointer)@uniq\n        let second = pointer[1]@uniq\n        first.value = 1\n        second.value = 2\npublic func main() => ()\n";
        Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);
    }

    // A borrow that is not returned carries its own anchor Origin, which fits every destination like static; a returned one takes
    // the declared result's Origin.
    [Fact]
    public void AnUnreturnedRawPlaceBorrowCarriesItsAnchor()
    {
        var c = MinimalEmissionTest.Analyze(Resource + "func run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        return item.value\npublic func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete && c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
        var borrow = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ConversionKoto>().Single(static x => x.ConversionOf().Kind == ConversionBinding.Borrow);
        Assert.Equal(OriginKind.Anchor, borrow.TypeOf()!.Origin!.Kind);
        Assert.Same(borrow, borrow.TypeOf()!.Origin!.Binder);
    }

    // SPEC 5.2.2: accesses through the result, Reborrows from it and their children are checked under the anchor, like those of
    // a borrowed parameter's referent (PLAN G45). The record is A5's (R6-check H5; H3b T49, class 3): it relates the borrow that created
    // the Loan and the later use of the value retaining it, as A5 publishes them, not the holder's declaration, and has no Note.
    [Theory]
    [InlineData("let shared = item@follow@ref\n        item.value = 3\n        return shared.value\n", "item@follow@ref", "shared.value")]
    [InlineData("let inner = item@follow@uniq\n        let seen = item.value\n        inner.value = 4\n        return seen\n", "item@follow@uniq", "inner.value = 4")]
    [InlineData("let view = View.init(item@follow@ref)\n        item.value = 5\n        return view.item.value\n", "item@follow@ref", "view.item.value")]
    public void TheAnchorChecksLoansDerivedFromTheBorrow(string body, string borrow, string use)
    {
        var source = Resource + View + "func run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        " + body + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(source.IndexOf("item.value", StringComparison.Ordinal), error.Span!.Value.Start);
        Assert.Equal(
            [("borrow", borrow, "borrow that created the loan"), ("loan", use, "later use of the value retaining the conflicting loan")],
            error.Related!.Select(x => (x.Role, source.Substring(x.Span!.Value.Start, x.Span.Value.Length), x.Label)));
        Assert.Null(error.Note);
    }

    [Theory]
    [InlineData("func run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        item.value = 3\n        let shared = item@follow@ref\n        return shared.value\n")]
    [InlineData("func run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        item.value = 2\n        let view = View.init(item@follow@ref)\n        return view.item.value\n")]
    [InlineData("func run(pointer: raw/Resource, count: isize) -> i32\n    var total: i32 = 0\n    var i: isize = 0\n    while i < count\n        unsafe\n            let item = pointer[i]@uniq\n            item.value += 1\n            total += item.value\n        i += 1\n    return total\n")]
    public void AccessesAfterTheDerivedLoansEndAreValid(string function)
        => Assert.Empty(DiagnosticCorpus.Check(Resource + View + function + "public func main() => ()\n").Diagnostics);

    // SPEC 5.7 (Authority; DECISIONS BU-24, the owner's 「静的な権限判定をやめる (推奨)」): no access through a raw pointer is a
    // compile-time error for the Origin it states. A write, Take, in-place destruction or exclusive borrow through a pointer whose Origin
    // keeps only shared Loans (Lens.source, a `ref` input's Origin, `choose`'s meet, a typed path through a shared borrow, a pair follow
    // admitting `ref`) is undefined behavior by condition 3 of SPEC 5.2.1, not an error; reads, shared borrows and exclusive Origins
    // stay valid as before.
    [Theory]
    [InlineData("func f(lens: uniq/Lens<i32>)\n    unsafe => *lens.at = 0")]
    [InlineData("func f(lens: uniq/Lens<i32>)\n    unsafe => *lens.at += 1")]
    [InlineData("func f(lens: uniq/Lens<P>)\n    unsafe => (*lens.at).f = 1")]
    [InlineData("func f(lens: uniq/Lens<P>)\n    unsafe => _ = (*lens.at)@move")]
    [InlineData("func f(lens: uniq/Lens<P>) -> uniq/P during lens\n    unsafe => return (*lens.at)@uniq")]
    [InlineData("func f(lens: uniq/Lens<obj/P>)\n    unsafe => _ = (*lens.at)@objuniq")]
    [InlineData("func dataOf(b: ref/Lens<i32>) -> raw/i32 during b => b.at\nfunc f(y: uniq/Lens<i32>)\n    unsafe => *dataOf(y) = 1")]
    [InlineData("func choose(c: bool, a: ref/B, b: ref/B) -> ref/B => if c => a else => b\nfunc f(c: bool, x: ref/B, y: ref/B)\n    unsafe => *choose(c, x, y).data = 1")]
    [InlineData("func f(x: ref/i32)\n    unsafe => *(x@follow@raw) = 1")]
    [InlineData("func f(y: uniq/i32)\n    let r = y@follow@ref\n    unsafe => *(r@follow@raw) = 3")]
    [InlineData("func choose(c: bool, a: ref/i32, b: ref/i32) -> ref/i32 => if c => a else => b\nfunc f(c: bool, x: ref/i32, y: ref/i32)\n    unsafe => *(choose(c, x, y)@follow@raw) = 1")]
    [InlineData("func f<s/T>(x: s/i32)\n    s is ref or uniq\n    let p = x@follow@raw\n    unsafe => *p = 1")]
    [InlineData("func f(lens: ref/Lens<i32>) -> i32\n    unsafe => return *lens.at")]
    [InlineData("func f(lens: uniq/Lens<P>) -> ref/P during lens\n    unsafe => return (*lens.at)@ref")]
    [InlineData("func f(lens: ref/ULens<i32>)\n    unsafe => *lens.at = 0")]
    [InlineData("func f(lens: uniq/ULens<P>)\n    unsafe => _ = (*lens.at)@move")]
    [InlineData("func f(y: uniq/B)\n    unsafe => *y.data = 1")]
    [InlineData("func choose(c: bool, a: uniq/B, b: uniq/B) -> uniq/B => if c => a else => b\nfunc f(c: bool, x: uniq/B, y: uniq/B)\n    unsafe => *choose(c, x, y).data = 1")]
    [InlineData("func f()\n    var number: i32 = 1\n    let r = number@uniq\n    let q = r@follow@raw\n    unsafe => *q = 2\n    r@follow += 1")]
    [InlineData("func f(y: uniq/i32)\n    unsafe => *(y@follow@raw) = 3")]
    [InlineData("func choose(c: bool, a: uniq/i32, b: uniq/i32) -> uniq/i32 => if c => a else => b\nfunc f(c: bool, x: uniq/i32, y: uniq/i32)\n    unsafe => *(choose(c, x, y)@follow@raw) = 1")]
    [InlineData("func f<s/T>(x: s/T) -> raw/T\n    s is ref or uniq\n    return x@follow@raw")]
    [InlineData("func f<s/T>(x: s/i32)\n    s is uniq\n    let p = x@follow@raw\n    unsafe => *p = 1")]
    [InlineData("func f<s/T>(x: s/i32) -> i32\n    s is ref or uniq\n    let p = x@follow@raw\n    unsafe => return *p")]
    public void NoRawOriginDeniesAnAccess(string declarations)
        => Assert.Empty(RawPointerSyntaxTest.Records(Lenses + declarations));

    // SPEC 5.7's example: `clear`'s write is undefined behavior, no compile-time error.
    [Fact]
    public void TheSpecLensHasNoError()
        => Assert.Empty(RawPointerSyntaxTest.Records(SpecLens));

    // SPEC 15.6.2: a safe shared layer inside a pointee still grants Read only: the write through the pointee's `ref` Field is denied by
    // that layer, not by a raw Origin.
    [Fact]
    public void ASafeSharedLayerInsideAPointeeStillDenies()
        => Assert.Equal(
            ["SharedPathAccess_Kd: (*p).r@follow"],
            RawPointerSyntaxTest.Records("struct Q {s}\n    public let r: ref/i32 during s\n    public init(r: ref/i32 during s) => self.r = r\n" +
                "func f(a: ref/i32, p: raw/(Q during a))\n    unsafe => (*p).r@follow = 1"));

    // SPEC 5.7, 13.5.5 (U2): a `during self` read or a typed path through a pair follow admitting a non-borrow case is a located
    // Unsupported_Kd; one admitting only borrows states its slot (NoRawOriginDeniesAnAccess, APairFollowWriteAdmittingOnlyBorrowsIsAccepted).
    [Theory]
    [InlineData("func f<s/T>(x: s/B) -> raw/i32\n    s is value or ref\n    return x.data", "Unsupported_Kd: x.data")]
    [InlineData("func f<s/T>(x: s/T) -> raw/T\n    s is value or valueborrow\n    return x@follow@raw", "Unsupported_Kd: x@follow@raw")]
    public void APairFollowReadIsUnsupportedWhereStated(string declarations, string record)
        => Assert.Equal([record], RawPointerSyntaxTest.Records(Lenses + declarations));

    // A `during self` Field written through a pair follow admitting only borrows. The old analysis read no Field through a pair layer
    // (its located Unsupported_Kd at `data`); that went with the MIR deciding acceptance (R6-check H5; H3b T15, 1a, the owner
    // 2026-11-03): check accepts the program with no record, and a Library build whose main reaches every function generates f's
    // instance for each case.
    [Fact]
    public void APairFollowWriteAdmittingOnlyBorrowsIsAccepted()
    {
        const string Declarations = Lenses + "func f<s/T>(x: s/B)\n    s is ref or uniq\n    unsafe => *x.data = 1";
        Assert.Empty(RawPointerSyntaxTest.Records(Declarations));
        const string Main = "\nfunc g(y: uniq/B) => f<uniq/i32 during y>(y)\nfunc h(y: ref/B) => f<ref/i32 during y>(y)\npublic func main() -> ()\n" +
            "    var b = B.init()\n    g(b@uniq)\n    h(b@ref)\n" +
            "    var n: i32 = 1\n    let lens = Lens<i32>.init(n@ref, null)\n    let ulens = ULens<i32>.init(n@uniq, null)\n";
        Assert.Equal(2, MinimalEmissionTest.GeneratedBodies(Declarations + Main, OutputKind.Library, accepted: true).Count(static x => x == "f"));
    }
}
