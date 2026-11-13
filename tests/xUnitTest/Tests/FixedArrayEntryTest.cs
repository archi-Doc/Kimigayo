// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1, 22.2.1, 14.6.2 (PLAN G32, G776): a fixed array has no source declaration; its iteration entries are built-in
/// conformances whose witnesses are no members, reached by `for` and through a Constraint on any `[N of E]`.</summary>
public class FixedArrayEntryTest
{
    // SPEC 8.6.4, 22.1 (PLAN G776-b): a test that holds an explicit iterator value starts it through a Constraint, as generic code does.
    internal const string Entries =
        "group E\n    public func entry<C>(c: ref/C during s) -> C.(Iterable).IteratorType(s)\n        C is Iterable\n        return c.iterate()\n" +
        "    public func entryUniq<C>(c: uniq/C during s) -> C.(UniqIterable).IteratorType(s)\n        C is UniqIterable\n        return c.iterateUniq()\n" +
        "    public func into<C>(c: C) -> C.(IntoIterable).IteratorType\n        C is IntoIterable\n        return c@move.intoIterator()\n";

    private const string Token = "struct Token\n    public let name: string\n    public init(name: string) => self.name = name@move\n    drop => Console.writeLine(self.name)\n";

    private const string Shapes = Entries +
        "let values: [3 of i32] = [1, 2, 3]\nvar total = 0\nvar it = E.into(values@move)\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\n" +
        "let units: [3 of ()] = [(), (), ()]\nvar count = 0\nfor unit in units => count += 1\n" +
        "let none: [0 of string] = []\nfor s in none => count += 10\n" +
        "require total == 6 and count == 3 else => $abort(\"shapes\")\nConsole.writeLine(\"ok\")";

    private const string RefRef = "group H\n    public func sum(rr: ref/(ref/[3 of i32] during c)) -> i32\n        var total = 0\n        for v in rr => total += v\n        return total\nlet a: [3 of i32] = [1, 2, 3]\nlet r = a@ref\nrequire H.sum(r@ref) == 6 else => $abort(\"sum\")\nConsole.writeLine(\"ok\")";
    private const string RefUniq = "group H\n    public func sum(ru: ref/(uniq/[3 of i32] during c)) -> i32\n        var total = 0\n        for v in ru => total += v\n        return total\nvar a: [3 of i32] = [1, 2, 3]\nlet u = a@uniq\nrequire H.sum(u@ref) == 6 else => $abort(\"sum\")\nConsole.writeLine(\"ok\")";
    private const string RefRefWrite = "var a: [3 of i32] = [1, 2, 3]\nlet r = a@ref\nlet rr = r@ref\nvar total = 0\nfor v in rr\n    a[0] = 9\n    total += v\nConsole.writeLine(\"ok\")";
    private const string RefUniqFollow = "group H\n    public func bump(ru: ref/(uniq/[3 of i32] during c))\n        for v in ru => v@follow += 1\nvar a: [3 of i32] = [1, 2, 3]\nlet u = a@uniq\nH.bump(u@ref)\nConsole.writeLine(\"ok\")";
    private const string UniqUniq = "group H\n    public func bump(uu: uniq/(uniq/[3 of i32] during c))\n        for v in uu => v@follow += 1\nvar a: [3 of i32] = [1, 2, 3]\nvar u = a@uniq\nH.bump(u@uniq)\nrequire a[0] == 2 else => $abort(\"bump\")\nConsole.writeLine(\"ok\")";

    [Fact]
    public void ExplicitSharedEntryLendsEachElement()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryShared",
            Entries + "let values: [3 of i32] = [1, 2, 3]\nvar total = 0\nvar it = E.entry(values)\nloop\n    match it.next()\n        .Some(let v) => total += v\n        .None => exit\nrequire total == 6 else => $abort(\"shared\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    [Fact]
    public void SharedAndExclusiveLoopsUseTheEntries()
        => ScalarEmissionTest.EmitFixture(
            "FixedArrayEntryLoops",
            "var values: [3 of i32] = [1, 2, 3]\nvar total = 0\nfor v in values => total += v\nfor v in values@uniq => v@follow *= 2\nlet view = values@ref\nfor v in view => total += v\nrequire total == 18 and values[1] == 4 else => $abort(\"loops\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 14.6.2, 3.4.3 (PLAN G776-b commit 1): a Subject reaching a fixed array through two safe-reference layers takes the built-in
    // entry, as an Array's does; while the entry was a member call by name it was NoApplicableOverload_Kd at the loop.
    [Theory]
    [InlineData("ref", RefRef)]
    [InlineData("uniq", RefUniq)]
    public void ASubjectThroughTwoReferenceLayersTakesTheBuiltInEntry(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Accepts, $"{name}: {MinimalEmissionTest.Describe(c)}");
    }

    // The invalid twins keep their own records (SPEC 14.6.2's Loans, 3.4.3's shared path): a write while the Subject is lent, a write
    // through a shared item.
    [Theory]
    [InlineData(RefRefWrite, DiagnosticCode.ComparisonLoanConflict_Kd, "a[0] = 9")]
    [InlineData(RefUniqFollow, DiagnosticCode.SharedPathAccess_Kd, "v@follow += 1")]
    public void ATwoLayerSubjectKeepsItsTwinsRecords(string source, DiagnosticCode code, string at)
        => MirTargetSide.Check(MinimalEmissionTest.Analyze(source), (code, new SyntaxLocation(at)));

    // A defect pin, not a rule: an exclusive Subject through `uniq/(uniq/...)` is valid (SPEC 14.6.2, 3.4.3) but stays
    // NoApplicableOverload_Kd at the loop, for fixed arrays and Arrays alike. Removal condition: PLAN G179 (selection through reference
    // layers) accepts the form and re-points this row to its run.
    [Fact]
    public void AnExclusiveSubjectThroughTwoExclusiveLayersIsStillRejected()
        => MirTargetSide.Check(MinimalEmissionTest.Analyze(UniqUniq), (DiagnosticCode.NoApplicableOverload_Kd, new SyntaxLocation("for v in uu => v@follow += 1")));

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
    [InlineData("let it = Kimi.Storage.FixedArrayEntries.iterate(values)")]
    public void TheMemberGroupIsReachableOnlyThroughAFixedArray(string use)
    {
        var c = MinimalEmissionTest.Analyze("let values: [2 of i32] = [1, 2]\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    // SPEC 22.1, 9.6 (PLAN G776-b; the owner's O-194 (a)): a fixed array's entries are built-in conformances, which add no member, so a
    // written entry call names nothing, as `a.index(k)` does; its 22.1 members and a Slice's declared entry stay.
    [Theory]
    [InlineData("    let a: [2 of i32] = [1, 2]\n    var it = a.iterate()\n    _ = it.next()", "UnresolvedBinding_Kd: a.iterate")]
    [InlineData("    var a: [2 of i32] = [1, 2]\n    var it = a.iterateUniq()\n    _ = it.next()", "UnresolvedBinding_Kd: a.iterateUniq")]
    [InlineData("    let a: [2 of i32] = [1, 2]\n    var it = a@move.intoIterator()\n    _ = it.next()", "UnresolvedBinding_Kd: a@move.intoIterator")]
    [InlineData("    let a: [2 of i32] = [1, 2]\n    var it = a[..].iterate()\n    _ = it.next()\n    require a.length == 2 and a.slice().length == 2 else => $abort(\"len\")", "")]
    public void AnEntryIsNoMemberOfAFixedArray(string body, string records)
        => Assert.Equal(records, string.Join(" | ", RawPointerSyntaxTest.Records("func f()\n" + body)));

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
            Entries + "var values: [3 of i32] = [1, 2, 3]\nvar it = E.entryUniq(values@uniq)\nloop\n    match it.next()\n        .Some(let v) => v@follow += 10\n        .None => exit\nrequire values[0] == 11 and values[2] == 13 else => $abort(\"exclusive\")\nConsole.writeLine(\"ok\")",
            "ok\n");

    // SPEC 8.5.2, 8.5.4, 22.1 (PLAN G578): a fixed array's projection in a declaration position names its entry witness's result, as a
    // body's `let` does, and an applied family proves its formation conditions at a fixed-array subject. Each crashed or was rejected.
    [Theory]
    [InlineData("result", Entries + "group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\n        return E.entry(a)\nlet a: [2 of i32] = [1, 2]\nvar it = H.f(a)\n_ = it.next()")]
    [InlineData("exclusive", Entries + "group H\n    public func f(a: uniq/[2 of i32] during s) -> [2 of i32].(UniqIterable).IteratorType(s)\n        return E.entryUniq(a)\nvar a: [2 of i32] = [1, 2]\nvar it = H.f(a@uniq)\n_ = it.next()")]
    [InlineData("owning", Entries + "group H\n    public func f() -> [2 of i32].(IntoIterable).IteratorType\n        let a: [2 of i32] = [1, 2]\n        return E.into(a@move)\n_ = H.f()")]
    [InlineData("owningParameter", Entries + "group H\n    public func g(x: [2 of i32].(IntoIterable).IteratorType) -> i32\n        _ = x@move\n        return 1\nlet a: [2 of i32] = [1, 2]\n_ = H.g(E.into(a@move))")]
    [InlineData("parameter", Entries + "group H\n    public func g(a: ref/[2 of i32] during s, x: [2 of i32].(Iterable).IteratorType(s)) -> i32\n        _ = x@move\n        return 1\nlet a: [2 of i32] = [1, 2]\n_ = H.g(a, E.entry(a))")]
    [InlineData("generic", Entries + "group H\n    public func f<T, length N>(a: ref/[N of T] during s) -> [N of T].(Iterable).IteratorType(s)\n        return E.entry(a)\nlet a: [2 of i32] = [1, 2]\nvar it = H.f(a)\n_ = it.next()")]
    [InlineData("field", Entries + "struct W\n    public let it: [2 of i32].(IntoIterable).IteratorType\n    public init(it: [2 of i32].(IntoIterable).IteratorType) => self.it = it@move\nlet a: [2 of i32] = [1, 2]\nlet w = W.init(E.into(a@move))\n_ = w@move")]
    [InlineData("requirement", Entries + "contract K\n    func get(self: ref/Self, a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\nvar a: [2 of i32] = [1, 2]\nvar it = E.entry(a)\n_ = it.next()\na[0] = 5")]
    [InlineData("formation", "group H\n    public func f(x: ref/i32 during a, y: ref/i32 during b) -> [2 of ref/i32 during b].(Iterable).IteratorType(a)\n        origin b outlives a\n        $abort(\"unused\")\nConsole.writeLine(\"ok\")")]
    public void DeclarationPositionsNameTheEntryIteratorType(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Accepts, $"{name}: {MinimalEmissionTest.Describe(c)}");
    }

    // The invalid counterparts: a header iterator Type of another element rejects the entry's result, and the formation's Origin
    // condition is required at a fixed-array subject (SPEC 8.5.4).
    [Theory]
    [InlineData(DiagnosticCode.NoApplicableOverload_Kd, Entries + "group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i64].(Iterable).IteratorType(s)\n        return E.entry(a)\nConsole.writeLine(\"ok\")")]
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
        var c = MinimalEmissionTest.Analyze(Entries + "group H\n    public func f(a: ref/[2 of i32] during s) -> [2 of i32].(Iterable).IteratorType(s)\n        return E.entry(a)\nvar a: [2 of i32] = [1, 2]\nvar it = H.f(a)\na[0] = 5\n_ = it.next()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        MirTargetSide.Check(c, (DiagnosticCode.ComparisonLoanConflict_Kd, new SyntaxLocation("a[0] = 5")));
    }
}
