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

    [Theory]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return *pointer\n", false)]
    [InlineData("func take<E>(pointer: raw/E) -> E\n    unsafe => return (*pointer)@move\n", true)]
    [InlineData("func take(pointer: raw/i32) -> i32\n    unsafe => return *pointer\n", true)]
    public void ACopyUnprovenTakeNeedsMove(string function, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(function + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Equal(valid, c.Analysis.IsVerified);
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
    // Origin is fitted to the expected Type, result or storage destination, and other Places are not compared with it.
    [Theory]
    [InlineData("func get(pointer: raw/Resource, owner: ref/Resource) -> ref/Resource during owner\n    unsafe => return (*pointer)@ref\n")]
    [InlineData("func get(pointer: raw/Resource, owner: uniq/Resource) -> uniq/i32 during owner\n    unsafe => return pointer[1].value@uniq\n")]
    [InlineData("func get(pointer: raw/Resource) -> ref/Resource\n    unsafe => return pointer[0]@ref/Resource\n")]
    [InlineData("struct View {source}\n    public let item: ref/Resource during source\n    public init(item: ref/Resource during source) => self.item = item\nfunc view(pointer: raw/Resource, owner: ref/Resource) -> View during owner\n    unsafe => return View.init((*pointer)@ref)\n")]
    public void ARawPlaceBorrowFitsItsDestination(string function)
    {
        var c = MinimalEmissionTest.Analyze(Resource + function + "public func main() => ()\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    // A returned borrow takes the declared result's Origin, in concrete and generic functions.
    [Fact]
    public void AReturnedRawPlaceBorrowTakesTheResultOrigin()
        => ScalarEmissionTest.EmitFixture("RawPlaceBorrowResult", "func get(p: raw/i32, owner: ref/i32) -> ref/i32 during owner\n    unsafe => return (*p)@ref\nfunc generic<T>(p: raw/T, owner: ref/T) -> ref/T during owner\n    unsafe => return (*p)@ref\nvar number: i32 = 5\nlet r = get(number@raw, number@ref)\nlet g = generic(number@raw, number@ref)\nrequire r == 5 and g == 5 else => $abort(\"value\")\nConsole.writeLine(\"returned\")", "returned\n");

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
    // a borrowed parameter's referent (PLAN G45).
    [Theory]
    [InlineData("let shared = item@follow@ref\n        item.value = 3\n        return shared.value\n", "let shared")]
    [InlineData("let inner = item@follow@uniq\n        let seen = item.value\n        inner.value = 4\n        return seen\n", "let inner")]
    [InlineData("let view = View.init(item@follow@ref)\n        item.value = 5\n        return view.item.value\n", "let view")]
    public void TheAnchorChecksLoansDerivedFromTheBorrow(string body, string holder)
    {
        var source = Resource + View + "func run(pointer: raw/Resource) -> i32\n    unsafe\n        let item = (*pointer)@uniq\n        " + body + "public func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal("ComparisonLoanConflict_Kd", error.Code);
        Assert.Equal(source.IndexOf("item.value", StringComparison.Ordinal), error.Span!.Value.Start);
        var retained = Assert.Single(error.Related!);
        Assert.Equal(source.IndexOf(holder, StringComparison.Ordinal), retained.Span!.Value.Start);
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
    // Unsupported_Kd; one admitting only borrows states its slot (NoRawOriginDeniesAnAccess). The default pipeline's old analysis reads
    // no Field through a pair layer, raw or not: its located Unsupported_Kd, which an access error no longer hides.
    [Theory]
    [InlineData("func f<s/T>(x: s/B)\n    s is ref or uniq\n    unsafe => *x.data = 1", "Unsupported_Kd: data")]
    [InlineData("func f<s/T>(x: s/B) -> raw/i32\n    s is value or ref\n    return x.data", "Unsupported_Kd: x.data")]
    [InlineData("func f<s/T>(x: s/T) -> raw/T\n    s is value or valueborrow\n    return x@follow@raw", "Unsupported_Kd: x@follow@raw")]
    public void APairFollowReadIsUnsupportedWhereStated(string declarations, string record)
        => Assert.Equal([record], RawPointerSyntaxTest.Records(Lenses + declarations));
}
