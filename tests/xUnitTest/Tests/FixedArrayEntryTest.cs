// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1, 22.2.1 (PLAN G32): a fixed array has no source declaration; its entry members are the receiver
/// functions of the internal Kimi fixed-array group, found by member lookup on any `[N of E]`.</summary>
public class FixedArrayEntryTest
{
    private const string Token = "struct Token\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n";

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
    // SPEC 15.6.4 (R4-M170): both programs are valid, but since G264 (S) S2 A5 errs twice at count's `for` (the lent item's composed erased
    // destruction stores next()'s Loan into the iterator, a self-row; ledger 68fb94789893, a4dea13bc8f6), and generation refuses them; the
    // old analysis accepted and ran them to "ok\n" (fixtures FixedArrayEntryGenericFixed and Array). H5's (e) list, row "S2's self-row
    // family" (the owner's O-9, 「(a) 加えて着地」): PLAN G416 restores the acceptance and re-points this test back to its run.
    [Theory]
    [InlineData("Fixed", "[4 of string]")]
    [InlineData("Array", "Array<string>")]
    public void GenericIterationReachesTheIteratorStep(string name, string type)
    {
        var c = MinimalEmissionTest.Analyze("func count<C>(items: ref/C) -> i32\n    C is Iterable\n    var n = 0\n    for item in items => n += 1\n    return n\nlet values: " + type + " = [\"a\", \"b\", \"c\", \"d\"]\nrequire count(values) == 4 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete, $"{name}: {MinimalEmissionTest.Describe(c)}");
        Assert.False(c.Accepts);
        var loop = new SyntaxLocation("for item in items => n += 1");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, loop), (DiagnosticCode.ComparisonLoanConflict_Kd, loop));
        Assert.False(c.Generate(TextWriter.Null));
    }

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

    // SPEC 15.6.4 (R4-M170): valid, but since G264 (S) S2 A5 errs twice at count's `for` (ledger 9238e164f4f2), and generation refuses it;
    // the old analysis accepted and ran it to "ok\n" (fixture FixedArrayEntryOwningGeneric). H5's (e) list, row "S2's self-row family"
    // (the owner's O-9, 「(a) 加えて着地」): PLAN G416 restores the acceptance and re-points this test back to its run.
    [Fact]
    public void GenericOwningIterationReachesTheIteratorStep()
    {
        var c = MinimalEmissionTest.Analyze("func count<C>(items: C) -> i32\n    C is IntoIterable\n    var n = 0\n    for item in items@move => n += 1\n    return n\nlet values: [3 of string] = [\"a\", \"b\", \"c\"]\nrequire count(values@move) == 3 else => $abort(\"generic\")\nConsole.writeLine(\"ok\")");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.False(c.Accepts);
        var loop = new SyntaxLocation("for item in items@move => n += 1");
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, loop), (DiagnosticCode.ComparisonLoanConflict_Kd, loop));
        Assert.False(c.Generate(TextWriter.Null));
    }

    [Fact]
    public void ExplicitExclusiveEntryUpdatesEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryExclusive",
            "var values: [3 of i32] = [1, 2, 3]\nvar it = values.iterateUniq()\nloop\n    match it.next()\n        .Some(let v) => v@follow += 10\n        .None => exit\nrequire values[0] == 11 and values[2] == 13 else => $abort(\"exclusive\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.5.2, 8.5.4, 22.1 (PLAN G578): a fixed array's projection in a declaration position names its entry witness's result, as a
    // body's `let` does, and an applied family proves its formation conditions at a fixed-array subject. Each crashed or was rejected.
    [Theory]
    [InlineData("result", "group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\n        return a.iterate()\nlet a: [2 of i32] = [1, 2]\nvar it = H.f(a)\n_ = it.next()")]
    [InlineData("exclusive", "group H\n    public func f(a: uniq/[2 of i32] during s) -> [2 of i32].(UniqIterable).IteratorType(s)\n        return a.iterateUniq()\nvar a: [2 of i32] = [1, 2]\nvar it = H.f(a@uniq)\n_ = it.next()")]
    [InlineData("owning", "group H\n    public func f() -> [2 of i32].(IntoIterable).IteratorType\n        let a: [2 of i32] = [1, 2]\n        return a.intoIterator()\n_ = H.f()")]
    [InlineData("owningParameter", "group H\n    public func g(x: [2 of i32].(IntoIterable).IteratorType) -> i32\n        _ = x@move\n        return 1\nlet a: [2 of i32] = [1, 2]\n_ = H.g(a.intoIterator())")]
    [InlineData("parameter", "group H\n    public func g(a: ref/[2 of i32] during s, x: [2 of i32].(Iterable).IteratorType(s)) -> i32\n        _ = x@move\n        return 1\nlet a: [2 of i32] = [1, 2]\n_ = H.g(a, a.iterate())")]
    [InlineData("generic", "group H\n    public func f<E, length N>(a: ref/[N of E] during s) -> [N of E].(Iterable).IteratorType(s)\n        return a.iterate()\nlet a: [2 of i32] = [1, 2]\nvar it = H.f(a)\n_ = it.next()")]
    [InlineData("field", "struct W\n    public let it: [2 of i32].(IntoIterable).IteratorType\n    public init(it: [2 of i32].(IntoIterable).IteratorType) => self.it = it@move\nlet a: [2 of i32] = [1, 2]\nlet w = W.init(a.intoIterator())\n_ = w@move")]
    [InlineData("requirement", "contract K\n    func get(self: ref/Self, a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\nvar a: [2 of i32] = [1, 2]\nvar it = a.iterate()\n_ = it.next()\na[0] = 5")]
    [InlineData("formation", "group H\n    public func f(x: ref/i32 during a, y: ref/i32 during b) -> [2 of ref/i32 during b].(Iterable).IteratorType(a)\n        origin b outlives a\n        $abort(\"unused\")\nConsole.writeLine(\"ok\")")]
    public void DeclarationPositionsNameTheEntryIteratorType(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Accepts, $"{name}: {MinimalEmissionTest.Describe(c)}");
    }

    // The invalid counterparts: a header iterator Type of another element rejects the entry's result, and the formation's Origin
    // condition is required at a fixed-array subject (SPEC 8.5.4).
    [Theory]
    [InlineData(DiagnosticCode.NoApplicableOverload_Kd, "group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i64].(Iterable).IteratorType(s)\n        return a.iterate()\nConsole.writeLine(\"ok\")")]
    [InlineData(DiagnosticCode.InvalidOriginBinding_Kd, "group H\n    public func f(x: ref/i32 during a, y: ref/i32 during b) -> [2 of ref/i32 during b].(Iterable).IteratorType(a)\n        $abort(\"unused\")\nConsole.writeLine(\"ok\")")]
    public void DeclarationPositionsRejectWhatTheEntryTypeRejects(DiagnosticCode code, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal([code], c.Binding.Issues.Select(static x => x.Code));
    }

    // SPEC 15.6.1: the iterator a header's applied family names keeps the source Loan, so a write while it lives conflicts.
    [Fact]
    public void AHeaderIteratorKeepsItsSourceLoan()
    {
        var c = MinimalEmissionTest.Analyze("group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\n        return a.iterate()\nvar a: [2 of i32] = [1, 2]\nvar it = H.f(a)\na[0] = 5\n_ = it.next()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, new SyntaxLocation("a[0] = 5")));
    }
}
