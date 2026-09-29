// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1, 22.1.2.2 (PLAN G32): a fixed array has no source declaration; its entry members are the receiver
/// functions of the internal Kimi fixed-array group, found by member lookup on any `[N of E]`.</summary>
public class FixedArrayEntryTest
{
    private const string Token = "struct Token\n    public let name: string\n    public init(name: string) => self.name = name@move\n    deinit => Console.writeLine(self.name)\n";

    private const string Shapes =
        "let values: [3 of i32] = [1, 2, 3]\nvar total = 0\nvar it = values.intoIterator()\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\n" +
        "let units: [3 of ()] = [(), (), ()]\nvar count = 0\nfor unit in units => count += 1\n" +
        "let none: [0 of string] = []\nfor s in none => count += 10\n" +
        "require total == 6 and count == 3 else => $abort(\"shapes\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void ExplicitSharedEntryLendsEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryShared",
            "let values: [3 of i32] = [1, 2, 3]\nvar total = 0\nvar it = values.iterate()\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\nrequire total == 6 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void SharedAndExclusiveLoopsUseTheEntries()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryLoops",
            "var values: [3 of i32] = [1, 2, 3]\nvar total = 0\nfor v in values => total += v\nfor v in values@uniq => v@follow *= 2\nlet view = values@ref\nfor v in view => total += v\nrequire total == 18 and values[1] == 4 else => $abort(\"loops\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // Dispatch through `C.IteratorType` reaches the iterator's `next` only through the entry's result Type, for a fixed
    // array and for a declared collection alike.
    [Theory]
    [InlineData("Fixed", "[4 of string]")]
    [InlineData("Array", "Array<string>")]
    public void GenericIterationReachesTheIteratorStep(string name, string type)
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryGeneric" + name,
            "func count<C>(items: ref/C) -> i32\n    C is Iterable\n    var n = 0\n    for item in items => n += 1\n    return n\nlet values: " + type + " = [\"a\", \"b\", \"c\", \"d\"]\nrequire count(values) == 4 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Theory]
    [InlineData("let it = Kimi.Storage.FixedArray.iterate(values)")]
    [InlineData("let it = ::Kimi.Storage.FixedArray.iterate(values)")]
    [InlineData("let n: i32 = 1\nlet it = n.iterate()")]
    public void TheMemberGroupIsReachableOnlyThroughAFixedArray(string use)
    {
        var c = MinimalEmissionTest.Analyze("let values: [2 of i32] = [1, 2]\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    // PLAN G33: an owning loop moves each element out of the remainder's inline storage in order; the loop binding is
    // destroyed at the end of its iteration.
    [Fact]
    public void OwningLoopMovesEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryOwning",
            Token + "let tokens: [3 of Token] = [Token.init(name: \"a\"), Token.init(name: \"b\"), Token.init(name: \"c\")]\nfor token in tokens@move => Console.writeLine(\"item\")\nConsole.writeLine(\"done\")",
            "item\na\nitem\nb\nitem\nc\ndone\n");

    // Leaving early destroys the taken element, then the iterator destroys the untaken elements from the last.
    [Fact]
    public void EarlyExitDestroysTheUntakenElements()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryOwningExit",
            Token + "let tokens: [4 of Token] = [Token.init(name: \"a\"), Token.init(name: \"b\"), Token.init(name: \"c\"), Token.init(name: \"d\")]\nfor token in tokens@move\n    Console.writeLine(\"item\")\n    if token.name == \"b\" => exit\nConsole.writeLine(\"done\")",
            "item\na\nitem\nb\nd\nc\ndone\n");

    [Fact]
    public void ExplicitOwningEntryYieldsScalarsAndZeroSizedElements()
        => ScalarEmissionTest.EmitFixture("FixedArrayEntryOwningShapes", Shapes, "ok\n");

    [Fact]
    public void GenericOwningIterationReachesTheIteratorStep()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryOwningGeneric",
            "func count<C>(items: C) -> i32\n    C is IntoIterable\n    var n = 0\n    for item in items@move => n += 1\n    return n\nlet values: [3 of string] = [\"a\", \"b\", \"c\"]\nrequire count(values@move) == 3 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void ExplicitExclusiveEntryUpdatesEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryExclusive",
            "var values: [3 of i32] = [1, 2, 3]\nvar it = values.iterateUniq()\nloop\n    match it.next()\n        .Some(let v) => v@follow += 10\n        .None => exit\nrequire values[0] == 11 and values[2] == 13 else => $abort(\"exclusive\")\nConsole.writeLine(\"ok\")",
            "ok\n");
}
