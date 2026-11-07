// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class DynamicArrayBorrowTest
{
    private const string Task = "struct Task\n    public let id: i32\n    public init(id: i32) => self.id = id\n    drop => Console.writeLine(\"drop\")\n";

    [Theory]
    [InlineData("Owner", "let item = values[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("SharedHandle", "let handle = values@ref\nlet item = handle[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("ExclusiveHandle", "let handle = values@uniq\nlet item = handle[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    [InlineData("Slice", "let view = values[..]\nlet item = view[0]@ref\nrequire item.id == 42 else => $abort(\"value\")")]
    public void BorrowsTheElementWithoutAcquiringOwnership(string name, string access)
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrow" + name, Task + "var values: Array<Task> = [Task.init(42)]\n" + access + "\nvalues@uniq.clear()\nConsole.writeLine(\"done\")", "drop\ndone\n");

    [Fact]
    public void BorrowsCopyStorageAndReadsItsReferent()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowScalar", "var values: Array<i32> = [42]\nlet item = values[0]@ref\nrequire item == 42 else => $abort(\"value\")\nvalues[0] = 7", string.Empty);

    [Fact]
    public void AnImmediateHandleBorrowCanBeIndexedAndSliced()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowImmediateHandle", "var values: Array<i32> = [20, 22]\nrequire (values@ref)[0] + (values@uniq)[1] == 42 else => $abort(\"value\")\nrequire (values@ref)[..].length == 2 else => $abort(\"view\")", string.Empty);

    [Fact]
    public void ATemporaryReceiverLivesThroughTheCall()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowTemporary", Task + "func make() -> Array<Task> => [Task.init(42)]\nfunc inspect(item: ref/Task) => require item.id == 42 else => $abort(\"value\")\ninspect(make()[0]@ref)\nConsole.writeLine(\"done\")", "drop\ndone\n");

    [Fact]
    public void ReturnedBorrowKeepsTheCallerStorageOrigin()
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowReturned", Task + "func first(values: ref/Array<Task>) -> ref/Task during values => values[0]@ref\nvar values: Array<Task> = [Task.init(42)]\nlet item = first(values@ref)\nrequire item.id == 42 else => $abort(\"value\")\nvalues@uniq.clear()", "drop\n");

    [Theory]
    [InlineData("Negative", "-1")]
    [InlineData("End", "1")]
    [InlineData("Maximum", "9223372036854775807@isize")]
    public void BoundsAreCheckedBeforeFormingTheReference(string name, string index)
        => ScalarEmissionTest.EmitFixture("DynamicArrayBorrowBounds" + name, "let values: Array<i32> = [42]\nlet item = values[" + index + "]@ref", string.Empty, 1, "Hello.kimi:2:12: abort KIMI_E_INDEX_BOUNDS: Index out of bounds\n");

    [Theory]
    [InlineData("func make() -> Array<i32> => [1]\nlet item = make()[0]@ref\nlet n: i32 = item")]
    [InlineData("var values: Array<i32> = [1]\nlet item = values[(label index: do\n    values@uniq.clear()\n    exit to index 0\n)]@ref\nlet n: i32 = item")]
    [InlineData("var values: Array<i32> = [1]\nlet handle = values@uniq\nlet item = handle[0]@ref\nhandle@follow@uniq.clear()\nlet n: i32 = item")]
    public void RejectsExpiredAndConflictingDependencies(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }

    // SPEC 4.8.4, 15.6.3, 22.2 (R8-A1d): a view keeps the Loan its function's `during a` states, also over an empty Array or `[0 of E]`, where no
    // element backs it, reborrowed, in a generic body and through an owning iterator; each program labelled from SPEC.
    [Theory]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    var v = a.sliceUniq()\n    a.append(1)\n    Console.writeLine(\"\\(v.length)\")\n", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    var it = a.iterate()\n    a.append(1)\n    _ = it.next()\n", "CallActivationConflict_Kd")]
    [InlineData("public func main()\n    var a: [0 of i32] = []\n    var v = a.sliceUniq()\n    let n = a.length\n    let w = a.sliceUniq()\n    Console.writeLine(\"\\(v.length) \\(n) \\(w.length)\")\n", "ComparisonLoanConflict_Kd ComparisonLoanConflict_Kd")]
    [InlineData("func f(a: uniq/Array<i32>) -> uniq/i32 during a\n    var it = a.iterateUniq()\n    match it.next()\n        .Some(let first) => return first@move\n        .None => $abort(\"none\")\npublic func main()\n    var a: Array<i32> = [1]\n    let r = f(a@uniq)\n    a.append(2)\n    r@follow = 3\n", "ComparisonLoanConflict_Kd")]
    [InlineData("func f() -> Slice<i32>\n    var a: Array<i32> = [1]\n    return a[..]\npublic func main()\n    Console.writeLine(\"\\(f().length)\")\n", "UnsatisfiedOriginRelation_Kd UnsatisfiedOriginRelation_Kd")]
    [InlineData("func f(n: i32) -> UniqSlice<i32>\n    var a: Array<i32> = [n]\n    return a.sliceUniq()\npublic func main()\n    _ = f(1)\n", "InvalidOriginBinding_Kd UnsatisfiedOriginRelation_Kd")]
    [InlineData("func f() -> ArrayIterator<i32>\n    var a: Array<i32> = [1]\n    return a.iterate()\npublic func main()\n    _ = f()\n", "UnsatisfiedOriginRelation_Kd UnsatisfiedOriginRelation_Kd")]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    var v = a.sliceUniq()\n    var w = a.sliceUniq()\n    Console.writeLine(\"\\(v.length) \\(w.length)\")\n", "ComparisonLoanConflict_Kd")]
    [InlineData("func f() -> isize\n    var keep: Array<i32> = [1]\n    var v = keep.sliceUniq()\n    if v.length > 0\n        var inner: Array<i32> = []\n        v = inner.sliceUniq()\n    return v.length\npublic func main()\n    _ = f()\n", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    let p = a.tryGetPairUniq(first: 0, second: 1)\n    a.append(1)\n    match p\n        .Some(let q) => q.0@follow = 2\n        .None => ()\n", "ComparisonLoanConflict_Kd SharedPathAccess_Kd")]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    let p = a.tryGetPairUniq(first: 0, second: 1)\n    let r = a.tryGetPairUniq(first: 0, second: 1)\n    match p\n        .Some(let q) => q.0@follow = 2\n        .None => ()\n    match r\n        .Some(let q) => q.0@follow = 2\n        .None => ()\n", "ComparisonLoanConflict_Kd SharedPathAccess_Kd SharedPathAccess_Kd")]
    [InlineData("func f<T>(a: uniq/Array<T>) -> isize\n    var it = a.iterate()\n    a.clear()\n    _ = it.next()\n    return 0\npublic func main()\n    var a: Array<i32> = []\n    Console.writeLine(\"\\(f(a@uniq))\")\n", "CallActivationConflict_Kd")]
    [InlineData("func f<T>() -> UniqSlice<T>\n    var a: Array<T> = []\n    return a.sliceUniq()\npublic func main()\n    _ = f<i32>()\n", "InvalidOriginBinding_Kd InvalidOriginBinding_Kd UnsatisfiedOriginRelation_Kd")]
    [InlineData("func f<T>(a: uniq/Array<T>) -> isize\n    var v = a.sliceUniq()\n    var w = a.sliceUniq()\n    return v.length + w.length\npublic func main()\n    var a: Array<i32> = []\n    Console.writeLine(\"\\(f(a@uniq))\")\n", "ComparisonLoanConflict_Kd")]
    [InlineData("func f<T>(a: uniq/Array<T>, item: T) -> isize\n    var v = a.sliceUniq()\n    a.append(item@move)\n    return v.length\npublic func main()\n    var a: Array<i32> = []\n    Console.writeLine(\"\\(f(a@uniq, 1))\")\n", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var x: i32 = 1\n    var a: Array<ref/i32> = []\n    if x > 0\n        a.append(x@ref)\n    var it = a@move.intoIterator()\n    x = 2\n    _ = it.next()\n", "ComparisonLoanConflict_Kd")]
    [InlineData("public func main()\n    var a: Array<i32> = []\n    var v = a.sliceUniq()\n    var w = v.sliceUniq()\n    Console.writeLine(\"\\(v.length)\")\n    Console.writeLine(\"\\(w.length)\")\n", "ComparisonLoanConflict_Kd")]
    public void AViewKeepsTheBorrowItsSignatureStates(string source, string codes)
        => Assert.Equal(codes.Split(' '), DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code).Order(StringComparer.Ordinal));

    // Their valid counterparts: the view's last use precedes the conflicting access, or nothing conflicts.
    [Theory]
    [InlineData("struct H<T>\n    public var v: T\n    public init(v: T) => self.v = v@move\npublic func main()\n    var a: Array<i32> = []\n    let h = H.init(a.sliceUniq())\n    Console.writeLine(\"\\(h.v.length)\")\n    a.append(1)\n")]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> i32\n        effect confined\nstruct M\n    Self is Maker\n    public init() => ()\n    public func make(self: ref/Self) -> i32\n        var a: [2 of i32] = [1, 2]\n        var s: i32 = 0\n        for x in a@uniq\n            s += x\n        for x in a@move\n            s += x\n        return s\npublic func main()\n    return\n")]
    [InlineData("contract Maker\n    func make(self: ref/Self) -> i32\n        effect confined\nstruct M\n    Self is Maker\n    public init() => ()\n    public func make(self: ref/Self) -> i32\n        var a: Array<i32> = []\n        var v = a.sliceUniq()\n        let p = a.tryGetPairUniq(first: 0, second: 1)\n        return 0\npublic func main()\n    return\n")]
    public void AViewsLoanEndsWithItsLastUse(string source)
        => Assert.Empty(DiagnosticCorpus.Check(source).Diagnostics);

    [Theory]
    [InlineData("values@uniq.clear()")]
    [InlineData("values@uniq.reserve(0)")]
    [InlineData("values[0] = Task.init(7)")]
    [InlineData("let moved = values@move")]
    public void RejectsInvalidationWhileAnElementBorrowIsLive(string mutation)
    {
        var c = MinimalEmissionTest.Analyze(Task + "var values: Array<Task> = [Task.init(42)]\nlet item = values[0]@ref\n" + mutation + "\nlet id = item.id");
        Assert.True(c.Binding.Result.IsComplete);
        Assert.Contains(c.Problems, static x => x.Code.IsLoanConflict());
        using var writer = new StringWriter();
        Assert.False(c.Generate(writer));
        Assert.Empty(writer.ToString());
    }
}
