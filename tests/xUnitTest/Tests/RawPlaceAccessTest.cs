// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 5.2.3: raw Places, and their Fields and elements, offer Take without tracked state. (*p)@move takes the value and leaves
// the storage Uninitialized, _ = (*p)@move destroys it in place, and a bare acquisition follows the rule for every Place: a
// Copy value is Copied and a Non-Copy or Copy-unproven value needs @move (SPEC 3.5).
public class RawPlaceAccessTest
{
    private const string Resource = "struct Resource\n    public var value: i32\n    public init(value: i32) => self.value = value\n    drop => Console.writeLine(\"drop\")\n";
    private const string View = "struct View {source}\n    public let item: ref/Resource during source\n    public init(item: ref/Resource during source) => self.item = item\n";

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

        // SPEC 5.2.3, 23.3.6.9: a raw Place takes with @move, around the whole dereference so that it does not apply to the pointer.
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void AnExplicitTakeMovesAndDestroys()
    {
        var source = Resource + """
            group Native
                #LibraryImport("kernel32", "VirtualAlloc")
                public unsafe func allocate(address: raw/u8, size: u64, kind: u32, protect: u32) -> raw/u8
                #LibraryImport("kernel32", "VirtualFree")
                public unsafe func free(address: raw/u8, size: u64, kind: u32) -> i32
            public func main()
                unsafe
                    let bytes = Native.allocate(null, 4096, 12288, 4)
                    require bytes != null else => $abort("allocate")
                    let storage = bytes@raw/Resource
                    (*storage).value = 1
                    storage[1].value = 2
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
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
}
