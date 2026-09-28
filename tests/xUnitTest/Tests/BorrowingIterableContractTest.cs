// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class BorrowingIterableContractTest
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void BorrowingEntryReturnsAnOriginBoundIterator(bool exclusive, bool generic, bool conflict)
    {
        var entry = exclusive ? "UniqIterable" : "Iterable";
        var method = exclusive ? "iterateUniq" : "iterate";
        var mode = exclusive ? "uniq" : "ref";
        var source = "struct Cursor {source}\n    Self is LendingIterator\n    associate LentItem(step) is i32\n    let value: ref/i32 during source\n    var done: bool = false\n    public init(value: ref/i32 during source) => self.value = value\n    public func next(self: uniq/Self during step) -> Option<i32>\n        if self.done => return .None\n        self.done = true\n        return .Some(self.value)\nstruct Values\n    Self is " + entry + "\n    associate " + entry + ".IteratorType(source) is Cursor during source\n    var value: i32 = 42\n    public init() => ()\n    public func " + method + "(self: " + mode + "/Self during source) -> Cursor during source => Cursor.init(self.value@ref)\nfunc start<T>(values: " + mode + "/T during source) -> T.(" + entry + ").IteratorType(source)\n    T is " + entry + "\n    return values." + method + "()\nvar values = Values.init()\nvar cursor = " + (generic ? "start(values@" + mode + ")" : "values." + method + "()") + "\nmatch cursor.next()\n    .Some(let item) => require item == 42 else => $abort(\"entry\")\n    .None => $abort(\"empty\")\nmatch cursor.next()\n    .Some(_) => $abort(\"exhaustion\")\n    .None => ()\nConsole.writeLine(\"entry\")";
        if (conflict)
        {
            source = source.Replace("\nmatch cursor.next()", "\nvalues = Values.init()\nmatch cursor.next()", StringComparison.Ordinal);
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
            Assert.False(c.Ownership.Result.IsVerified);
        }
        else
        {
            ScalarEmissionTest.EmitFixture("AssociatedBorrowingEntry" + mode + (generic ? "Generic" : "Direct"), source, "entry\n");
        }
    }
}
