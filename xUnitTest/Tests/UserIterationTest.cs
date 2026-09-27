// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class UserIterationTest
{
    private const string Drain =
        "struct Drain<T>\n    Self is Iterator\n    associate Iterator.Item is T\n    var value: Option<T>\n" +
        "    public init(value: T) => self.value = .Some(value@move)\n" +
        "    public func next(self: uniq/Self) -> Option<T> => Kimi.Intrinsics.exchange(self.value@uniq, with: .None)\n" +
        "struct Batch<T>\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Drain<T>\n    let value: T\n" +
        "    public init(value: T) => self.value = value@move\n    public func intoIterator(self: Self) -> Drain<T> => Drain<T>.init(self.value@move)\n";

    private const string Counter =
        "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var n: i32 = 0\n" +
        "    public func next(self: uniq/Self) -> Option<i32>\n        require self.n < 3 else => return .None\n        self.n += 1\n        return .Some(self.n)\n";

    private const string Three =
        "struct Three\n    Self is IntoIterable\n    associate IntoIterable.IteratorType is Counter\n" +
        "    public func intoIterator(self: Self) -> Counter => Counter.init()\n";

    [Fact]
    public void OwnedEntryBindsItsDeclaredItem()
    {
        var c = MinimalEmissionTest.Analyze(Counter + Three + "for item in Three.init()\n    let value: i32 = item");
        var loop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>());
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Equal(BoundType.I32, loop.Bindings[0].BoundType);
    }

    [Fact]
    public void ReplacingTheSubjectRebuildsTheEntryAndItemType()
    {
        var c = MinimalEmissionTest.Analyze(Counter + Three + Drain + "for item in Three.init() => ()");
        var loop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>());
        var replacement = MinimalEmissionTest.Analyze(Drain + "for item in Batch<bool>.init(true) => ()");
        var changed = Assert.Single(KotoTree.Walk(replacement.Kotonoha.RootKoto).OfType<ForKoto>());
        Assert.True(KotoHelper.Replace(loop, loop.Iterable, changed.Iterable));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(BoundType.Boolean, loop.Bindings[0].BoundType);
        Assert.Same(loop.Iterable, Assert.IsType<MemberAccessKoto>(loop.EntryCall!.Method).Left);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Fact]
    public void ReplacingTheBodyRetiresItsSyntheticMatchArm()
    {
        var c = MinimalEmissionTest.Analyze(Counter + Three + "for item in Three.init() => ()");
        var loop = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<ForKoto>());
        var replacement = MinimalEmissionTest.Analyze(Counter + Three + "for item in Three.init() => Console.writeLine(\"changed\")");
        var changed = Assert.Single(KotoTree.Walk(replacement.Kotonoha.RootKoto).OfType<ForKoto>());
        Assert.True(KotoHelper.Replace(loop, loop.Body, changed.Body));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(loop.Body, loop.Iteration!.Decomposition.Arms[0].Syntax.Body);
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        using var output = new StringWriter();
        Assert.True(c.Emission.WriteIr(output, out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Fact]
    public void ConcreteEntryExecutes()
        => ScalarEmissionTest.EmitFixture("UserIterationCounter", Counter + Three + "var total: i32 = 0\nfor item in Three.init()\n    total += item\nrequire total == 6 else => $abort(\"total\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void WarmUserIterationCompilationReusesItsPlans()
    {
        const string Source = Counter + Three + "func count<B>(batch: B) -> isize\n    B is IntoIterable\n    var count: isize = 0\n    for _ in batch@move => count += 1\n    return count\nrequire count(Three.init()) == 3 else => $abort(\"count\")";
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
            c.Binding.CheckStartup(OutputKind.Application);
            Assert.True(c.Ownership.Analyze().IsVerified);
            Assert.True(c.Emission.WriteIr(TextWriter.Null, out var error), error);
        }

        var success = true;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Bind().IsComplete;
        }

        var bindingBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        c.Binding.CheckStartup(OutputKind.Application);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Ownership.Analyze().IsVerified;
        }

        var ownershipBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            success &= c.Emission.WriteIr(TextWriter.Null, out _);
        }

        var generationBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(success);
        Assert.Equal(0, bindingBytes);
        Assert.Equal(0, ownershipBytes);
        Assert.Equal(0, generationBytes);
        NativeAllocationAudit.WriteFixture("UserIterationCost", Source, 0, 0, 0);
    }

    [Fact]
    public void EntryConstraintDispatchesInAGenericBody()
        => ScalarEmissionTest.EmitFixture("UserIterationConstraint", Counter + Three + "func count<B>(batch: B) -> isize\n    B is IntoIterable\n    var count: isize = 0\n    for _ in batch@move => count += 1\n    return count\nrequire count(Three.init()) == 3 else => $abort(\"count\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void EntryConstraintPreservesOwnedAndBorrowedItems()
        => ScalarEmissionTest.EmitFixture("UserIterationConstraintItems", Drain + "struct Tracked\n    deinit => Console.writeLine(\"item\")\nfunc count<B>(batch: B) -> isize\n    B is IntoIterable\n    var count: isize = 0\n    for _ in batch@move => count += 1\n    return count\nfunc inspect(n: ref/i32 during a)\n    require count(Batch<ref/i32 during a>.init(n)) == 1 else => $abort(\"borrow\")\nrequire count(Batch<Tracked>.init(Tracked.init())) == 1 else => $abort(\"owned\")\nvar n: i32 = 7\ninspect(n@ref)\nn = 8\nConsole.writeLine(\"ok\")", "item\nok\n");

    [Fact]
    public void GenericLoopTransfersItsProjectedItemIntoTheResult()
    {
        const string First = "func first<B>(batch: B) -> Option<B.IteratorType.Item>\n    B is IntoIterable\n    B.IteratorType is Iterator\n    for item in batch@move => return .Some(item@move)\n    return .None\n";
        ScalarEmissionTest.EmitFixture("UserIterationProjectedResult", Counter + Three + Drain + First + "match first(Three.init())\n    .Some(let n) => require n == 1 else => $abort(\"first\")\n    .None => $abort(\"empty\")\nmatch first(Batch<string>.init(\"ok\"))\n    .Some(let text) => Console.writeLine(text)\n    .None => $abort(\"empty\")", "ok\n");
    }

    [Fact]
    public void ReturnedBorrowedItemOutlivesItsIterator()
        => ScalarEmissionTest.EmitFixture("UserIterationBorrowedResult", Drain + "func first(n: ref/i32 during a) -> ref/i32 during a\n    for item in Batch<ref/i32 during a>.init(n) => return item\n    $abort(\"empty\")\nvar n: i32 = 7\nlet result = first(n@ref)\nrequire result == 7 else => $abort(\"borrow\")\nn = 8\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void MissingEntryConstraintIsRejected()
    {
        var c = MinimalEmissionTest.Analyze("func count<B>(batch: B)\n    for _ in batch@move => ()");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnsupportedBinding_Kd);
    }

    [Fact]
    public void EntryConstraintStillConsumesItsSubject()
    {
        var c = MinimalEmissionTest.Analyze("func count<B>(batch: B)\n    B is IntoIterable\n    for _ in batch@move => ()\n    let again = batch@move");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Code == DiagnosticCode.MovedPlace_Kd);
    }

    [Fact]
    public void ConstructedEntryInsideAGenericBodyExecutes()
        => ScalarEmissionTest.EmitFixture("UserIterationConstructed", Drain + "func count<T>(batch: Batch<T>) -> isize\n    var count: isize = 0\n    for _ in batch@move => count += 1\n    return count\nrequire count(Batch<i32>.init(7)) == 1 else => $abort(\"count\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void OwnedTupleItemsExecute()
        => ScalarEmissionTest.EmitFixture("UserIterationTuple", Drain + "for (var number, flag) in Batch<(i32, bool)>.init((7, true))\n    number += 1\n    require number == 8 and flag else => $abort(\"tuple\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void UnchangedMilestoneExecutes()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../milestones/Milestone28.kimi"));
        ScalarEmissionTest.EmitFixture("UserIterationMilestone28", File.ReadAllText(path), "Owned iterator acquired.\nContinue after item 1.\nItem 1 destroyed.\nExit after item 2.\nItem 2 destroyed.\nItem 4 destroyed.\nItem 3 destroyed.\nOwned iteration finished.\nBorrowed iterator acquired.\nExternal first is 10; remaining total is 50.\nBorrowed iterator acquired.\nGeneral iteration finished.\n");
    }

    [Fact]
    public void GenericBorrowedItemKeepsItsExternalSource()
        => ScalarEmissionTest.EmitFixture("UserIterationBorrowed", Drain + "func inspect(n: ref/i32 during a)\n    for item in Batch<ref/i32 during a>.init(n)\n        require item == 7 else => $abort(\"borrow\")\nvar n: i32 = 7\ninspect(n@ref)\nn = 8\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void SharedTupleItemBorrowsItsComponents()
        => ScalarEmissionTest.EmitFixture("UserIterationSharedTuple", Drain + "func inspect(row: ref/(i32, bool) during a)\n    for (number, flag) in Batch<ref/(i32, bool) during a>.init(row)\n        require number == 7 and flag else => $abort(\"tuple\")\nlet row = (7, true)\ninspect(row@ref)\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    public void ExclusiveTupleItemLendsSeparateComponents()
        => ScalarEmissionTest.EmitFixture("UserIterationExclusiveTuple", Drain + "func update(row: uniq/(i32, i32) during a)\n    for (first, second) in Batch<uniq/(i32, i32) during a>.init(row@move)\n        first@follow += 1\n        second@follow += first\nvar row = (7, 2)\nupdate(row@uniq)\nrequire row.0 == 8 and row.1 == 10 else => $abort(\"tuple\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData("ref", "ref")]
    [InlineData("ref", "uniq")]
    [InlineData("uniq", "uniq")]
    public void TupleItemFollowsEverySafeReferenceLayer(string outer, string inner)
    {
        var type = outer + "/(" + inner + "/(i32, i32) during a) during b";
        var update = outer == "uniq" ? "\n        first@follow += 1\n        second@follow += first" : string.Empty;
        ScalarEmissionTest.EmitFixture("UserIterationLayers" + outer + inner, Drain + "func inspect(row: " + type + ")\n    for (first, second) in Batch<" + type + ">.init(row" + (outer == "uniq" ? "@move" : string.Empty) + ")\n        require first == 7 and second == 2 else => $abort(\"tuple\")" + update + "\nvar row = (7, 2)\nvar view = row@" + inner + "\ninspect(view@" + outer + ")\nrequire row.0 == " + (outer == "uniq" ? "8 and row.1 == 10" : "7 and row.1 == 2") + " else => $abort(\"result\")\nConsole.writeLine(\"ok\")", "ok\n");
    }

    [Fact]
    public void SharedLayerStillBoundsExclusiveTupleComponents()
    {
        var c = MinimalEmissionTest.Analyze(Drain + "func inspect(row: ref/(uniq/(i32, i32) during a) during b)\n    for (first, second) in Batch<ref/(uniq/(i32, i32) during a) during b>.init(row)\n        first@follow = 9");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.SharedPathAccess_Kd);
    }

    [Theory]
    [InlineData("a", false)]
    [InlineData("(a and b)", true)]
    public void TupleComponentRetainsEveryGrantingOrigin(string resultOrigin, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(Drain + "func first(row: ref/(uniq/(i32, i32) during a) during b) -> ref/i32 during " + resultOrigin + "\n    for (value, _) in Batch<ref/(uniq/(i32, i32) during a) during b>.init(row) => return value\n    $abort(\"empty\")");
        Assert.Equal(valid, c.Binding.Result.IsComplete);
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        }
    }

    [Theory]
    [InlineData("first@follow = 9", DiagnosticCode.SharedPathAccess_Kd)]
    [InlineData("first = 9", DiagnosticCode.SharedBindingAssignment_Kd)]
    public void SharedTupleComponentsKeepTheirCapabilities(string update, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(Drain + "func inspect(row: ref/(i32, i32) during a)\n    for (first, second) in Batch<ref/(i32, i32) during a>.init(row)\n        " + update);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == code);
    }

    [Theory]
    [InlineData("Fallthrough", "Console.writeLine(\"body\")", "body\nitem\niterator\ndone\n")]
    [InlineData("Continue", "Console.writeLine(\"body\")\n    continue", "body\nitem\niterator\ndone\n")]
    [InlineData("Exit", "Console.writeLine(\"body\")\n    exit", "body\nitem\niterator\ndone\n")]
    [InlineData("Unnamed", "Console.writeLine(\"body\")\n    exit", "body\nitem\niterator\ndone\n")]
    public void IteratorAndItemHaveSeparateCleanup(string name, string body, string stdout)
    {
        var declarations = Drain.Replace("    public func next(self:", "    deinit => Console.writeLine(\"iterator\")\n    public func next(self:", StringComparison.Ordinal);
        var item = "struct Tracked\n    deinit => Console.writeLine(\"item\")\n";
        ScalarEmissionTest.EmitFixture("UserIterationCleanup" + name, declarations + item + "for " + (name == "Unnamed" ? "_" : "item") + " in Batch<Tracked>.init(Tracked.init())\n    " + body + "\nConsole.writeLine(\"done\")", stdout);
    }

    [Fact]
    public void ReturnTransfersTheItemBeforeIteratorCleanup()
    {
        var declarations = Drain.Replace("    public func next(self:", "    deinit => Console.writeLine(\"iterator\")\n    public func next(self:", StringComparison.Ordinal);
        var item = "struct Tracked\n    deinit => Console.writeLine(\"item\")\n";
        ScalarEmissionTest.EmitFixture("UserIterationReturn", declarations + item + "func take() -> Tracked\n    for item in Batch<Tracked>.init(Tracked.init()) => return item@move\n    $abort(\"empty\")\nlet value = take()\nConsole.writeLine(\"done\")", "iterator\ndone\nitem\n");
    }

    [Theory]
    [InlineData("continue")]
    [InlineData("exit")]
    public void NestedTransfersCleanEachScopeInOrder(string transfer)
    {
        var declarations = Drain.Replace("    public func next(self:", "    deinit => Console.writeLine(\"iterator\")\n    public func next(self:", StringComparison.Ordinal);
        var item = "struct Tracked\n    deinit => Console.writeLine(\"item\")\n";
        ScalarEmissionTest.EmitFixture("UserIterationNested" + transfer, declarations + item + "outer: for first in Batch<Tracked>.init(Tracked.init())\n    defer => Console.writeLine(\"outer defer\")\n    for second in Batch<Tracked>.init(Tracked.init())\n        defer => Console.writeLine(\"inner defer\")\n        " + transfer + " to outer\nConsole.writeLine(\"done\")", "inner defer\nitem\niterator\nouter defer\nitem\niterator\ndone\n");
    }

    [Fact]
    public void OutwardResultSecuresTheItemBeforeDeferredCleanup()
    {
        var declarations = Drain.Replace("    public func next(self:", "    deinit => Console.writeLine(\"iterator\")\n    public func next(self:", StringComparison.Ordinal);
        var item = "struct Tracked\n    deinit => Console.writeLine(\"item\")\n";
        ScalarEmissionTest.EmitFixture("UserIterationOutward", declarations + item + "let value = result: do\n    for item in Batch<Tracked>.init(Tracked.init())\n        defer => Console.writeLine(\"defer\")\n        exit to result: item@move\n    $abort(\"empty\")\nConsole.writeLine(\"done\")", "defer\niterator\ndone\nitem\n");
    }

    [Fact]
    public void FirstNoneEndsIterationWithoutProbingAgain()
    {
        var iterator = Counter.Replace("        require self.n < 3 else => return .None", "        Console.writeLine(\"next\")\n        if self.n == 0\n            self.n = 1\n            return .None", StringComparison.Ordinal);
        var entry = Three.Replace("=> Counter.init()", "\n        Console.writeLine(\"entry\")\n        return Counter.init()", StringComparison.Ordinal);
        ScalarEmissionTest.EmitFixture("UserIterationFirstNone", iterator + entry + "for _ in Three.init() => Console.writeLine(\"unexpected\")\nConsole.writeLine(\"done\")", "entry\nnext\ndone\n");
    }

    [Fact]
    public void AbortDoesNotRunLoopCleanup()
    {
        var declarations = Drain.Replace("    public func next(self:", "    deinit => Console.writeLine(\"iterator\")\n    public func next(self:", StringComparison.Ordinal);
        var item = "struct Tracked\n    deinit => Console.writeLine(\"item\")\n";
        var source = declarations + item + "for item in Batch<Tracked>.init(Tracked.init())\n    defer => Console.writeLine(\"defer\")\n    Console.writeLine(\"body\")\n    var n = 2147483647\n    n += 1\nConsole.writeLine(\"done\")";
        var line = source[..source.IndexOf("    n += 1", StringComparison.Ordinal)].Count(x => x == '\n') + 1;
        ScalarEmissionTest.EmitFixture("UserIterationAbort", source, "body\n", 1, $"Hello.kimi:{line}:5: abort KIMI_E_INT_OVERFLOW: Integer overflow\n");
    }

    [Theory]
    [InlineData("let values = Three.init()\nfor item in values@move => ()\nlet again = values@move")]
    [InlineData("var values = Three.init()\nlet view = values@ref\nfor item in values@move => ()\nlet again = view")]
    public void ConsumingEntryPreservesMoveAndLoanChecks(string source)
    {
        var c = MinimalEmissionTest.Analyze(Counter + Three + source);
        Assert.False(c.Ownership.Result.IsVerified);
        Assert.Contains(c.Ownership.Issues, x => x.Code is DiagnosticCode.MovedPlace_Kd or DiagnosticCode.ComparisonLoanConflict_Kd);
        using var output = new StringWriter();
        Assert.False(c.Emission.WriteIr(output, out _));
        Assert.Empty(output.ToString());
    }

    [Theory]
    [InlineData("i32", "7", "item", "let value: i32 = item")]
    [InlineData("(i32, bool)", "(7, true)", "(number, flag)", "let value: i32 = number\n    let yes: bool = flag")]
    public void GenericEntrySubstitutesTheCompleteItem(string type, string value, string binding, string body)
    {
        var c = MinimalEmissionTest.Analyze(Drain + "var n: i32 = 7\nfor " + binding + " in Batch<" + type + ">.init(" + value + ")\n    " + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void ItemKeepsItsExternalOrigin()
    {
        var c = MinimalEmissionTest.Analyze(Drain + "func inspect(n: ref/i32 during a)\n    for item in Batch<ref/i32 during a>.init(n)\n        let value: ref/i32 during a = item");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData(Counter + "for item in Counter.init() => ()")]
    [InlineData(Counter + "struct Fake\n    public func intoIterator(self: Self) -> Counter => Counter.init()\nfor item in Fake.init() => ()")]
    [InlineData(Counter + Three + "let values = Three.init()\nfor item in values => ()")]
    [InlineData(Counter + Three + "var values = Three.init()\nfor item in values@uniq => ()")]
    [InlineData(Counter + Three + "for (one, two) in Three.init() => ()")]
    public void MissingEntriesAndWrongBindingShapesAreRejected(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.NotEmpty(c.Binding.Issues);
    }
}
