// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.5: the standard storage boundary splits Array Storage into non-overlapping regions, so the
/// standard exclusive iterator's items are independent children (SPEC 15.6.3, 22.1.2.4).</summary>
public class StorageBoundaryTest
{
    private const string Values = "var values: Array<i32> = [1, 2, 3]\n";

    [Fact]
    public void OwningStringRemainderCleansEachOwnerExactlyOnce()
    {
        var source = """
            func first(values: Array<string>) -> string
                var iterator = (values@move).intoIterator()
                match iterator.next()
                    .Some(let item) => return item@move
                    .None => $abort("empty")
            let taken = first(["first", "second", "third"])
            Console.writeLine(taken)
            """;
        var ir = ScalarEmissionTest.EmitFixture("StorageBoundaryStrings", source, "first\n");
        StringEmissionTest.WriteAuditedFixture("StorageBoundaryStrings", source, ir, "first\n", "first=1;second=1;third=1", order: [2, 1, 0]);
    }

    [Fact]
    public void AbortInRemainderCleanupStopsRemainingCleanup()
    {
        var source = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop\n        if self.id == 2 => $abort(\"stop\")\n        Console.writeLine(\"drop\")\nvar values: Array<Task> = [Task.init(1), Task.init(2), Task.init(3)]\nvar iterator = (values@move).intoIterator()\n_ = iterator.next()\nConsole.writeLine(\"stop\")";
        ScalarEmissionTest.EmitFixture("StorageBoundaryOwnedAbort", source, "drop\nstop\ndrop\n", 1, "Hello.kimi:5:28: abort KIMI_E_ABORT: stop\n");
    }

    [Theory]
    [InlineData("iterateUniq")]
    [InlineData("iterate")]
    public void ItemsOfEarlierStepsStayValid(string entry)
    {
        // Two items from the same iterator are live at once; the second step conflicts with neither the first item
        // nor the source Loan the iterator keeps.
        var c = MinimalEmissionTest.Analyze(Values + "var it = values." + entry + "()\nlet p = it.next()\nlet q = it.next()\nmatch p\n    .Some(_) => ()\n    .None => ()\nmatch q\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void TheSourceStaysProtectedWhileAnItemIsNeeded()
    {
        var c = MinimalEmissionTest.Analyze(Values + "var it = values.iterateUniq()\nlet p = it.next()\nvalues = [4]\nmatch p\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    // SPEC 22.1.2.5, 15.3.5: a remainder holds the Loan of its source slot although no field stores a safe reference, so a
    // live iterator protects its source: no replacement or growth, and no read beside an exclusive iterator.
    [Theory]
    [InlineData("var it = values.iterateUniq()\nvalues.append(4)")]
    [InlineData("var it = values.iterateUniq()\nvalues = [4]")]
    [InlineData("var it = values.iterateUniq()\nlet first = values[0]")]
    [InlineData("var it = values.iterate()\nvalues.append(4)")]
    [InlineData("var it = values.iterate()\nvalues = [4]")]
    public void ALiveIteratorKeepsTheSourceLoan(string use)
    {
        var c = MinimalEmissionTest.Analyze(Values + use + "\nmatch it.next()\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    // SPEC 15.6.3: an item is a child split from the iterator's authority, so a Reborrow through it stays in its region
    // while the iterator lives on; two Reborrows through one item still conflict with each other.
    [Theory]
    [InlineData("match it.next()\n    .Some(let v)\n        v@follow += 1\n        let w = v@follow@ref\n        require w == v else => $abort(\"reborrow\")\n    .None => ()", true)]
    [InlineData("let first = it.next()\nmatch first@move\n    .Some(let v)\n        let w = v@follow@uniq\n        w@follow += 1\n    .None => ()", true)]
    [InlineData("match it.next()\n    .Some(let v)\n        let w = v@follow@ref\n        let u = v@follow@uniq\n        let n = w\n    .None => ()", false)]
    public void AnItemIsReborrowedWithinItsRegion(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Values + "var it = values.iterateUniq()\n" + use + "\nmatch it.next()\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(valid, c.Ownership.Result.IsVerified);
    }

    // The same holds for a user Type's exclusive entry enumerated by `for`: the iterator stays live across the body.
    [Fact]
    public void AForItemIsReborrowedWhileTheIteratorLives()
    {
        const string Source = "struct Once {source}\n    Self is Iterator\n    associate Iterator.Item is uniq/i32 during source\n    var slot: Option<uniq/i32 during source>\n" +
            "    public init(r: uniq/i32 during source) => self.slot = .Some(r@move)\n    public func next(self: uniq/Self) -> Option<uniq/i32 during source>\n        return Kimi.Intrinsics.exchange(self.slot@uniq, with: .None)\n" +
            "struct Cell\n    Self is UniqIterable\n    associate UniqIterable.IteratorType(a) is Once during a\n    public var value: i32 = 1\n    public init() => ()\n" +
            "    public func iterateUniq(self: uniq/Self during a) -> Once during a\n        return Once.init(self.value@uniq)\n" +
            "var cell = Cell.init()\nfor v in cell@uniq\n    v@follow += 1\n    let w = v@follow@ref\n    require w == v else => $abort(\"reborrow\")\nrequire cell.value == 2 else => $abort(\"value\")\nConsole.writeLine(\"split\")";
        ScalarEmissionTest.EmitFixture("StorageBoundaryForReborrow", Source, "split\n");
    }

    // The iterator over an exclusive parameter keeps that reference's Loan; the parameter is not a split child of it.
    [Fact]
    public void AnIteratorOverAParameterKeepsItsLoan()
    {
        var c = MinimalEmissionTest.Analyze("func total(values: uniq/Array<i32>) -> i32\n    var it = values.iterateUniq()\n    values.append(4)\n    match it.next()\n" +
            "        .Some(let v) => return v@follow\n        .None => return 0\nvar values: Array<i32> = [1]\nlet t = total(values@uniq)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
    }

    [Fact]
    public void ASharedIteratorAllowsReads()
    {
        var c = MinimalEmissionTest.Analyze(Values + "var it = values.iterate()\nlet first = values[0]\nmatch it.next()\n    .Some(_) => ()\n    .None => ()");
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    // The Kimigayo iterators execute natively over the boundary: shared items are read in index order, exclusive items
    // are retained together and written through, and both iterators stay exhausted after None.
    [Fact]
    public void ExplicitIterationExecutes()
    {
        const string Source = "public func main()\n    var values: Array<i32> = [1, 2, 3]\n    var total: i32 = 0\n    var shared = values.iterate()\n" +
            "    loop\n        match shared.next()\n            .Some(let v) => total += v\n            .None => exit\n    require total == 6 else => $abort(\"shared\")\n" +
            "    match shared.next()\n        .Some(_) => $abort(\"shared exhausted\")\n        .None => ()\n" +
            "    var exclusive = values.iterateUniq()\n    let first = exclusive.next()\n    let second = exclusive.next()\n" +
            "    match first@move\n        .Some(let r) => r@follow += 10\n        .None => $abort(\"first\")\n" +
            "    match second@move\n        .Some(let r) => r@follow += 20\n        .None => $abort(\"second\")\n" +
            "    match exclusive.next()\n        .Some(let r) => r@follow += 30\n        .None => $abort(\"third\")\n" +
            "    match exclusive.next()\n        .Some(_) => $abort(\"exhausted\")\n        .None => ()\n" +
            "    require values[0] == 11 and values[1] == 22 and values[2] == 33 else => $abort(\"values\")\n" +
            "    var empty: Array<i32> = []\n    var none = empty.iterateUniq()\n    match none.next()\n        .Some(_) => $abort(\"empty\")\n        .None => Console.writeLine(\"boundary\")";
        var ir = ScalarEmissionTest.EmitFixture("StorageBoundaryExplicit", Source, "boundary\n");
        Assert.DoesNotContain("__kimi_array_split_", ir);
    }

    // The owning entry transfers each element out once and stays exhausted; retained items are owned values.
    [Fact]
    public void OwningIterationExecutes()
    {
        const string Source = "public func main()\n    var values: Array<i32> = [1, 2, 3]\n    var it = (values@move).intoIterator()\n    let first = it.next()\n    let second = it.next()\n    var total: i32 = 0\n" +
            "    match first\n        .Some(let n) => total += n\n        .None => $abort(\"first\")\n    match second\n        .Some(let n) => total += n\n        .None => $abort(\"second\")\n" +
            "    match it.next()\n        .Some(let n) => total += n\n        .None => $abort(\"third\")\n    match it.next()\n        .Some(_) => $abort(\"exhausted\")\n        .None => ()\n" +
            "    require total == 6 else => $abort(\"total\")\n    var empty: Array<i32> = []\n    var none = (empty@move).intoIterator()\n    match none.next()\n        .Some(_) => $abort(\"empty\")\n        .None => Console.writeLine(\"owned\")";
        var ir = ScalarEmissionTest.EmitFixture("StorageBoundaryOwned", Source, "owned\n");
        Assert.DoesNotContain("__kimi_array_take_first_", ir);
    }

    // SPEC 4.7.6, 22.1.2.5: a taken element is destroyed by its owner; the unreturned elements are destroyed with the
    // iterator in reverse index order, then the buffer is released.
    [Fact]
    public void OwningIteratorDestroysUnreturnedElements()
    {
        const string Source = "struct Tracked\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"Dropped \\(self.id)\")\n" +
            "public func main()\n    var items: Array<Tracked> = [Tracked.init(1), Tracked.init(2), Tracked.init(3), Tracked.init(4)]\n    var it = (items@move).intoIterator()\n" +
            "    match it.next()\n        .Some(let t) => require t.id == 1 else => $abort(\"first\")\n        .None => $abort(\"empty\")\n" +
            "    match it.next()\n        .Some(let t) => require t.id == 2 else => $abort(\"second\")\n        .None => $abort(\"empty\")\n    Console.writeLine(\"Stop.\")";
        var ir = ScalarEmissionTest.EmitFixture("StorageBoundaryOwnedDrop", Source, "Dropped 1\nDropped 2\nStop.\nDropped 4\nDropped 3\n");
        Assert.DoesNotContain("__kimi_array_owned_drop_", ir);
    }

    // SPEC 9.3, 22.1.2.5: the boundary is internal to the Kimi Kotonoha; no user source reaches it.
    [Theory]
    [InlineData("let r = Kimi.Storage.borrowStorage(values@ref)")]
    [InlineData("let r = Storage.borrowStorage(values@ref)")]
    [InlineData("let r = Kimi.Storage.lend(values@ref, null)")]
    [InlineData("let r = Kimi.Storage.split(values@uniq, null)")]
    [InlineData("unsafe => Kimi.Storage.release(null@raw/i32)")]
    [InlineData("func f(r: Kimi.Storage.RefRemainder<Array<i32>>) => ()")]
    public void UserSourceCannotReachTheBoundary(string use)
    {
        var c = MinimalEmissionTest.Analyze(Values + use);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    // The library's own conformances pass the Iterator effect bound: splitting off a child conflicts with no earlier
    // child, while a user iterator that reborrows its stored reference does not (IteratorOriginEffectsTest).
    [Fact]
    public void TheLibraryBindsWithItsStandardIterators()
    {
        var c = MinimalEmissionTest.Analyze("Console.writeLine(\"a\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Library.ValidateDeclarations(), "declarations: " + c.Library.InvalidDeclaration?.ToString().Split((char)10)[0]);
        Assert.True(c.Library.ValidateBoundDeclarations(), "bound: " + c.Library.InvalidDeclaration?.ToString().Split((char)10)[0]);
    }

    [Theory]
    [InlineData(KimiDeclarationId.StorageLend, false)]
    [InlineData(KimiDeclarationId.StorageSplit, false)]
    [InlineData(KimiDeclarationId.StorageLend, true)]
    [InlineData(KimiDeclarationId.StorageSplit, true)]
    public void CapabilityPrimitivesRejectChangedPointersAndOrigins(KimiDeclarationId id, bool changeOrigin)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var function = Assert.IsType<FunctionKoto>(c.Library.GetSymbol(id)!.Declaration);
        if (changeOrigin)
        {
            var result = Assert.IsType<TypeSemanticsKoto>(function.ReturnType);
            result.SetOrigin(null, null, result.Span.End);
            result.SetOrigin("static", result.Span.End);
        }
        else
        {
            function.Parameters[1].Type = function.Parameters[0].Type;
        }

        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd && ReferenceEquals(x.Node, function));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapabilityArgumentsFollowTheirNames(bool exclusive)
    {
        var semantics = exclusive ? "uniq" : "ref";
        var primitive = exclusive ? "split" : "lend";
        var c = CompilationTestHelper.ParseSuccess($$"""
            var values: Array<i32> = [7, 9]
            require Kimi.StorageProbe.first(values@{{semantics}}) == 7 else => $abort("first")
            Console.writeLine("named")
            """);
        var helper = $$"""
            public group StorageProbe
                public func first(values: {{semantics}}/Array<i32>) -> i32
                    var state = Storage.borrowStorage(values)
                    let element = state.storage
                    state.position += 1
                    state.count -= 1
                    unsafe
                        let item = Storage.{{primitive}}(element: element, state: state@{{semantics}})
                        return item
            """;
        c.Library.Kotonoha.CreateCodeContext().Parse(c.Library.Kotonoha.RootKoto, helper);
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.WriteFixture("StorageBoundaryNamed" + primitive, CompilationTestHelper.WriteIr(c), "named\n");
    }
}
