// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Reflection;
using System.Text;
using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Mir;
using Xunit;

namespace XunitTest;

/// <summary>Regions and Loans on MIR (A5-1, SPEC 15.6; docs/dev/DECISIONS.md R4-M24): each rule with a valid and an invalid program, as
/// whether a Loan on a named root is active before a named statement of a body (<see cref="MirBorrows.Replay"/>, R4 shadow: the old path
/// decides), the agreement of that answer with the old path's verdicts, and the Binding views it reads (TyCtxt.Relate, TyCtxt.Clauses).</summary>
public sealed class MirBorrowTest(ITestOutputHelper output)
{
    private const string Counter = "struct Counter\n    public var value: i32 = 1\nfunc relay(p: uniq/Counter) -> uniq/Counter during p\n    return p\n";
    private const string Change = "func change(value: uniq/i32) => value@follow = 99\n";

    // The old Loan classes' sources (read from their rows, or built as those classes build them), each with the statement the old path
    // reports or the statement that writes a borrowed root, its root and the old verdict.
    public static TheoryData<string, string, string, string, bool, string> OldVerdicts()
    {
        var rows = new TheoryData<string, string, string, string, bool, string>();
        void Add(Type type, string method, int row, string statement, string root, bool conflict, string departure = "")
        {
            var data = type.GetMethod(method)!.GetCustomAttributes<InlineDataAttribute>().ElementAt(row).Data;
            var source = data.OfType<string>().Single(static x => x.Contains('\n', StringComparison.Ordinal));
            var prefix = type.GetField(type == typeof(ReturnedBorrowAncestryTest) ? "Counter" : "Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            rows.Add($"{type.Name}.{method}[{row}]", prefix?.GetRawConstantValue() + source, statement, root, conflict, departure);
        }

        var inference = typeof(LocalRegionInferenceTest);
        Add(inference, nameof(LocalRegionInferenceTest.KeepsEveryLoanReachingAJoin), 0, "a = 3", "a", true);
        Add(inference, nameof(LocalRegionInferenceTest.KeepsEveryLoanReachingAJoin), 1, "b = 3", "b", true);
        Add(inference, nameof(LocalRegionInferenceTest.AssignmentKeepsEveryLiveHolder), 0, "first = 9", "first", true);
        Add(inference, nameof(LocalRegionInferenceTest.AssignmentKeepsEveryLiveHolder), 1, "second = 9", "second", true);
        Add(inference, nameof(LocalRegionInferenceTest.UsesOneLocalTypeAndTheCurrentValue), 2, "first = 9", "first", false);
        Add(inference, nameof(LocalRegionInferenceTest.FollowsControlFlowAndEndsOnlyUnusedLoans), 2, "a = 3", "a", false);
        Add(inference, nameof(LocalRegionInferenceTest.FollowsControlFlowAndEndsOnlyUnusedLoans), 3, "a = 3", "a", false);
        var composition = typeof(LocalRegionCompositionTest);
        for (var row = 0; row < 15; row++)
        {
            Add(composition, nameof(LocalRegionCompositionTest.CompleteAndPartialReplacementEndOnlyTheOldLoans), row, "a = 3", "a", false);
        }

        string[] stored = ["b = 3", "b = 3", "b = 3", "a = 3", "b = 3", "b = 3", "b = 3", "b = 3", "a = 3", "a = 3", "a = 3", "a = 3", "a = 3"];
        for (var row = 0; row < stored.Length; row++)
        {
            Add(composition, nameof(LocalRegionCompositionTest.EveryStoredValueRetainsItsLiveLoan), row, stored[row], stored[row][..1], true);
        }

        var completion = typeof(LocalRegionCompletionTest);
        Add(completion, nameof(LocalRegionCompletionTest.CompletedRegionsComposeWithoutReopeningTheirType), 4, "n = 3", "n", false, " Q1-ref");
        Add(completion, nameof(LocalRegionCompletionTest.CompletedRegionsComposeWithoutReopeningTheirType), 5, "n = 3", "n", false);
        Add(completion, nameof(LocalRegionCompletionTest.CompletedRegionsComposeWithoutReopeningTheirType), 7, "n = 3", "n", false);
        Add(completion, nameof(LocalRegionCompletionTest.CompletedRegionsComposeWithoutReopeningTheirType), 8, "n = 3", "n", false, " Q1-ref");
        string[] completions = ["b = 5", "n = 3", "n = 3", "a = 3"];
        for (var row = 0; row < completions.Length; row++)
        {
            Add(completion, nameof(LocalRegionCompletionTest.CompletionPreservesEveryRemainingLoan), row, completions[row], completions[row][..1], true);
        }

        var finite = typeof(FiniteRegionCompositionTest);
        string[] transferred = ["b = 3", "b = 3", "n = 8", "n = 3", "x@follow = 9"];
        for (var row = 0; row < transferred.Length; row++)
        {
            Add(finite, nameof(FiniteRegionCompositionTest.EveryTransferredBorrowStillProtectsItsSource), row, transferred[row], transferred[row][..1], true);
        }

        Add(finite, nameof(FiniteRegionCompositionTest.FiniteAndInferredRegionsShareValueFlow), 2, "b = 2", "b", false);
        // StoredReferenceLoanTest builds its sources from its rows, as below.
        var reads = typeof(StoredReferenceLoanTest).GetMethod(nameof(StoredReferenceLoanTest.ExclusiveChildrenSuspendParentValueReads))!;
        foreach (var data in reads.GetCustomAttributes<InlineDataAttribute>().Select(static x => x.Data))
        {
            var (origin, read) = ((string)data[0]!, (string)data[1]!);
            var source = "func run(value: uniq/i32" + origin + ")\n    let child = value@follow@uniq\n    let snapshot: i32 = " + read + "\n    child@follow = 99";
            rows.Add("StoredReferenceLoanTest.ExclusiveChildrenSuspendParentValueReads", source, "let snapshot: i32 = " + read, "value", true, string.Empty);
        }

        var restrictions = typeof(StoredReferenceLoanTest).GetMethod(nameof(StoredReferenceLoanTest.ParentRestrictionsFollowAcquisitionAndCopies))!;
        foreach (var data in restrictions.GetCustomAttributes<InlineDataAttribute>().Select(static x => x.Data))
        {
            var (acquisition, mutation) = ((string)data[0]!, (string)data[1]!);
            var source = Change + "func run(value: uniq/i32 during a)\n    " + acquisition + "\n    " + mutation + "\n    require found == 42 else => $abort(\"value\")";
            rows.Add("StoredReferenceLoanTest.ParentRestrictionsFollowAcquisitionAndCopies", source, mutation.Split('\n')[0], "value", true, string.Empty);
        }

        var ancestry = typeof(ReturnedBorrowAncestryTest);
        Add(ancestry, nameof(ReturnedBorrowAncestryTest.ReturnedExclusiveEndsBeforeParentUse), 4, "p.value += 1", "p", false);
        for (var row = 0; row < 4; row++)
        {
            Add(ancestry, nameof(ReturnedBorrowAncestryTest.RejectsParentUseWhileReturnedLoanLives), row, row == 1 ? "let s = relay(p)" : "p.value += 1", "p", true);
        }

        var parts = typeof(ReturnedPartBorrowTest);
        Add(parts, nameof(ReturnedPartBorrowTest.ReturnedLoanStillProtectsItsInput), 2, "pair.left.value += 1", "pair", true);
        Add(parts, nameof(ReturnedPartBorrowTest.ReturnedLoanStillProtectsItsInput), 3, "pair.left = Counter.init()", "pair", true);
        // A5-closure (PLAN, U7b): a Closure's inferred relations between its signature's Origins are not in its Function Type.
        const string Closure = "func none() -> Option<ref/i32 during source> => .None\nfunc f() -> bool\n    var b = none()\n    var n = 2\n" +
            "    let store = func (slot: uniq/Option<ref/i32>, value: Option<ref/i32>) => slot@follow = value@move\n    store(b@uniq, .Some(n@ref))\n" +
            "    n = 3\n    match b\n        .Some(let value) => return value@follow == 2\n        .None => return false\n";
        rows.Add("A5-closure", Closure, "n = 3", "n", true, " A5-closure");
        return rows;
    }

    // Each rule (DECISIONS R4-M24) with a program where the Loan is active before the statement and one where it is not.
    [Theory]
    [InlineData("R1 NLL: a Loan ends after its holder's last use (SPEC 15.6)", "func f() -> i32\n    var x = 1\n    let r = x@ref\n    x = 2\n    return r@follow\n", "x = 2", "x", true)]
    [InlineData("R1", "func f() -> i32\n    var x = 1\n    let r = x@ref\n    let y = r@follow\n    x = 2\n    return y\n", "x = 2", "x", false)]
    [InlineData("R2 complete replacement ends, a copy keeps (SPEC 15.6.4)", "func f() -> i32\n    var a = 1\n    let b = 2\n    var view = a@ref\n    let saved = view\n    view = b@ref\n    let s = saved@follow\n    a = 3\n    return view@follow + s\n", "a = 3", "a", false)]
    [InlineData("R2", "func f() -> i32\n    var first = 0\n    var view = first@ref\n    let saved = view\n    let second = 5\n    view = second@ref\n    first = 9\n    return saved@follow\n", "first = 9", "first", true)]
    [InlineData("R3 joins keep every reaching Loan", "func f() -> i32\n    var a = 1\n    var b = 2\n    var view = a@ref\n    if a == 1\n        view = b@ref\n    a = 3\n    return view@follow\n", "a = 3", "a", true)]
    [InlineData("R3", "func f() -> i32\n    var a = 1\n    var b = 2\n    var view = a@ref\n    if a == 1\n        view = b@ref\n    else\n        view = b@ref\n    a = 3\n    return view@follow\n", "a = 3", "a", false)]
    [InlineData("R3 loops", "func f() -> i32\n    var a = 1\n    var b = 2\n    var view = a@ref\n    var n = 0\n    while n < 3\n        b = 3\n        n += view@follow\n        view = b@ref\n    return view@follow\n", "b = 3", "b", true)]
    [InlineData("R3", "func f() -> i32\n    var b = 2\n    var n = 0\n    while n < 3\n        let view = b@ref\n        n += view@follow\n        b = 3\n    return n\n", "b = 3", "b", false)]
    [InlineData("R4 a reborrow keeps its parent (SPEC 15.6.3)", "func run(value: uniq/i32)\n    let child = value@follow@uniq\n    let snapshot: i32 = value@follow\n    child@follow = 99\n", "let snapshot: i32 = value@follow", "value", true)]
    [InlineData("R4", Change + "func run(value: uniq/i32)\n    let found = value@follow@ref\n    require found == 42 else => $abort(\"value\")\n    change(value)\n", "change(value)", "value", false)]
    [InlineData("R4 through a dead parent", "func f() -> i32\n    var x = 1\n    let p = x@uniq\n    let c = p@follow@uniq\n    x = 2\n    return c@follow\n", "x = 2", "x", true)]
    [InlineData("R4", "func f() -> i32\n    var x = 1\n    let p = x@uniq\n    let c = p@follow@uniq\n    c@follow = 3\n    x = 2\n    return x\n", "x = 2", "x", false)]
    [InlineData("R5 a call's result keeps its input (SPEC 15.6.4)", Counter + "func f(p: uniq/Counter)\n    let r = relay(p)\n    p.value += 1\n    r.value += 1\n", "p.value += 1", "p", true)]
    [InlineData("R5", Counter + "func f(p: uniq/Counter)\n    let r = relay(p)\n    r.value += 1\n    p.value += 1\n", "p.value += 1", "p", false)]
    [InlineData("R6 a write through a reference reaches its referent", "func put<T>(slot: uniq/T, value: T) => slot@follow = value@move\nfunc none() -> Option<ref/i32 during source> => .None\nfunc f() -> bool\n    var b = none()\n    var slot = b@uniq\n    var n = 2\n    put(slot, .Some(n@ref))\n    n = 3\n    match b\n        .Some(let value) => return value@follow == 2\n        .None => return false\n", "n = 3", "n", true)]
    [InlineData("R6", "func put<T>(slot: uniq/T, value: T) => slot@follow = value@move\nfunc none() -> Option<ref/i32 during source> => .None\nfunc f()\n    var b = none()\n    var slot = b@uniq\n    var n = 2\n    put(slot, .Some(n@ref))\n    n = 3\n", "n = 3", "n", false)]
    [InlineData("R6 a substituted clause relates its inputs (SPEC 15.6.4 step 3)", "func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\nfunc f() -> i32\n    var x = 1\n    var y = 2\n    let r = two(x@ref, y@ref)\n    x = 3\n    return r@follow\n", "x = 3", "x", true)]
    [InlineData("R6", "func two(a: ref/i32, b: ref/i32) -> ref/i32 during b => b\nfunc f() -> i32\n    var x = 1\n    var y = 2\n    let r = two(x@ref, y@ref)\n    x = 3\n    return r@follow\n", "x = 3", "x", false)]
    [InlineData("R7 a result naming an Arg's Origin borrows its referent (SPEC 15.6.4 step 6)", "func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    let view = f()\n    x@follow = 9\n    return view@follow\n", "x@follow = 9", "x", true)]
    [InlineData("R7", "func use(x: uniq/i32, f: ref/(() -> ref/i32 during x)) -> i32\n    let view = f()\n    let y = view@follow\n    x@follow = 9\n    return y\n", "x@follow = 9", "x", false)]
    [InlineData("R8 a Loan in caller storage is kept to the exit (SPEC 15.6.4)", "func keep(target: uniq/Option<ref/i32 during t>, n: ref/i32 during t)\n    target@follow = .Some(n@follow@ref)\n    let after = 1\n", "let after = 1", "n", true)]
    [InlineData("R8", "func keep(n: ref/i32)\n    var slot: Option<ref/i32> = .None\n    slot = .Some(n@follow@ref)\n    let after = 1\n", "let after = 1", "n", false)]
    [InlineData("R9 an abstract Type's drop observes its Origins (SPEC 15.6.6, X1)", "func f<C>(items: C, other: C)\n    C is Iterable\n    var local = items@move\n    let it = local.iterate()\n    local = other@move\n", "local = other@move", "local", true)]
    [InlineData("R9", "func f(items: Array<i32>, other: Array<i32>)\n    var local = items@move\n    let it = local.iterate()\n    local = other@move\n", "local = other@move", "local", false)]
    [InlineData("R10 a use only the checks reach keeps no Loan in reachable code (SPEC 14.9, 14.10.3 paragraph 4)", "func f() -> i32\n    var x = 1\n    let r = x@ref\n    x = 2\n    return 0\n    return r@follow\n", "x = 2", "x", false)]
    [InlineData("R10", "func f(c: bool) -> i32\n    var x = 1\n    let r = x@ref\n    if c\n        x = 5\n        return 0\n    return r@follow\n", "x = 5", "x", false)]
    [InlineData("R10 inside code only the checks reach, its own Loans are checked as if executed", "func f() -> i32\n    return 0\n    var y = 1\n    let r = y@ref\n    y = 2\n    return r@follow\n", "y = 2", "y", true)]
    [InlineData("R10", "func f() -> i32\n    return 0\n    var y = 1\n    let r = y@ref\n    let z = r@follow\n    y = 2\n    return z\n", "y = 2", "y", false)]
    [InlineData("R10", "func f() -> i32\n    var x = 1\n    let r = x@ref\n    x = 2\n    return 0\n", "x = 2", "x", false)]
    [InlineData("R19 a part's complete replacement ends its old Loans; an untracked sibling keeps its own (cells, R4-M24, SPEC 15.6.4 :897)", "struct Pair {a, b}\n    public var x: ref/i32 during a\n    public var y: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\nfunc sum(p: Pair) -> i32 => p.x@follow + p.y@follow\nfunc f() -> i32\n    var m = 1\n    var n = 2\n    let k = 3\n    var p = Pair.init(m@ref, n@ref)\n    p.x = k@ref\n    m = 5\n    n = 6\n    return sum(p)\n", "m = 5", "m", false)]
    [InlineData("R19", "struct Pair {a, b}\n    public var x: ref/i32 during a\n    public var y: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\nfunc sum(p: Pair) -> i32 => p.x@follow + p.y@follow\nfunc f() -> i32\n    var m = 1\n    var n = 2\n    let k = 3\n    var p = Pair.init(m@ref, n@ref)\n    p.x = k@ref\n    m = 5\n    n = 6\n    return sum(p)\n", "n = 6", "n", true)]
    [InlineData("R20 a use of a path reads its subtree only: a part never read again keeps no Loan (the precision rule)", "struct Pair {a, b}\n    public var x: ref/i32 during a\n    public var y: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\nfunc f() -> i32\n    var m = 1\n    var n = 2\n    let k = 3\n    var p = Pair.init(m@ref, n@ref)\n    p.x = k@ref\n    n = 6\n    return p.x@follow\n", "n = 6", "n", false)]
    [InlineData("R21 a Phantom Origin keeps its Loan after every Field is replaced (SPEC 15.3.5)", "struct Tagged {a, t}\n    public var x: ref/i32 during a\n    public init(x: ref/i32 during a, w: ref/i32 during t) => self.x = x\nfunc use(p: Tagged) -> i32 => p.x@follow\nfunc f() -> i32\n    var m = 1\n    var n = 2\n    let k = 3\n    var p = Tagged.init(m@ref, n@ref)\n    p.x = k@ref\n    m = 5\n    n = 6\n    return use(p)\n", "n = 6", "n", true)]
    [InlineData("R21", "struct Tagged {a, t}\n    public var x: ref/i32 during a\n    public init(x: ref/i32 during a, w: ref/i32 during t) => self.x = x\nfunc use(p: Tagged) -> i32 => p.x@follow\nfunc f() -> i32\n    var m = 1\n    var n = 2\n    let k = 3\n    var p = Tagged.init(m@ref, n@ref)\n    p.x = k@ref\n    m = 5\n    n = 6\n    return use(p)\n", "m = 5", "m", false)]
    [InlineData("R13 a whole write from the written local keeps what the new value holds", "func keep(r: ref/i32) -> ref/i32 during r => r\nfunc f() -> i32\n    var a = 1\n    var x = a@ref\n    x = keep(x)\n    a = 3\n    return x@follow\n", "a = 3", "a", true)]
    [InlineData("R13", "func first(r: ref/i32, s: ref/i32) -> ref/i32 during s => s\nfunc f() -> i32\n    var a = 1\n    var b = 2\n    var x = a@ref\n    x = first(x, b@ref)\n    a = 3\n    return x@follow\n", "a = 3", "a", false)]
    [InlineData("R14 EndBorrow ends an abandoned argument's liveness only, never the Loans another holder keeps (F2)", "struct Counter\n    var count: i32 = 0\n    public func bump(self: uniq/Self, by: i32)\n        self.count = self.count + by\nfunc f(flag: bool) -> i32\n    var v = Counter.init()\n    var w = Counter.init()\n    var p = w@uniq\n    var n = 0\n    while n < 3\n        n += 1\n        p = v@uniq\n        p.bump(if flag => continue else => n)\n        return 0\n    let after = 1\n    p.bump(after)\n    return after\n", "let after = 1", "v", true)]
    [InlineData("R14", "struct Counter\n    var count: i32 = 0\n    public func bump(self: uniq/Self, by: i32)\n        self.count = self.count + by\nfunc f(flag: bool) -> i32\n    var v = Counter.init()\n    var w = Counter.init()\n    var p = w@uniq\n    var n = 0\n    while n < 3\n        n += 1\n        p = v@uniq\n        p.bump(if flag => continue else => n)\n        return 0\n    let after = 1\n    return after\n", "let after = 1", "v", false)]
    [InlineData("R18 a Drop A2 elides uses nothing, nor one only the checks reach (SPEC 15.6.6, 14.9; plan 16-4)", "func eat<I>(it: I) => ()\nfunc f<C>(items: C, other: C)\n    C is Iterable\n    var local = items@move\n    let it = local.iterate()\n    eat(it@move)\n    local = other@move\n", "local = other@move", "local", false)]
    [InlineData("R18", "func eat<I>(it: I) => ()\nfunc f<C>(items: C, other: C) -> i32\n    C is Iterable\n    var local = items@move\n    let it = local.iterate()\n    return 0\n    eat(it@move)\n    local = other@move\n", "local = other@move", "local", false)]
    [InlineData("R18", "func eat<I>(it: I) => ()\nfunc f<C>(items: C, other: C) -> i32\n    C is Iterable\n    var local = items@move\n    let it = local.iterate()\n    eat(it@move)\n    local = other@move\n    return 0\n", "local = other@move", "local", false)]
    public void ALoanIsActiveWhileAHolderIsReachingAndLive(string rule, string source, string statement, string root, bool active)
        => Assert.True(active == Active(source, statement, root), rule);

    // R11 (plan 5.6.1, SPEC 8.9): a PairAcquire Reborrows only in a view whose case makes its Place an exclusive reference.
    [Fact]
    public void APairAcquireReborrowsInTheUniqViewOnly()
    {
        const string Source = "func f<s/T>(value: s/T, x: T) -> T\n    s is owner or uniq\n    T is Copy\n    var v = value@move\n    let local = v\n" +
            "    v@follow = x\n    return local@follow\n";
        Assert.False(Active(Source, "v@follow = x", "v", view: 0));
        Assert.True(Active(Source, "v@follow = x", "v", view: 1));
    }

    // R12 (DECISIONS R4-M15a): a view whose arenas exceed the analyses' byte limit records OwnershipStorageLimit_Kd once, at the body, and
    // is not solved: N locals each borrowed and live across N branches need about N heads × N/64 words of each kind.
    [Fact]
    public void AnArenaAboveTheLimitStopsTheView()
    {
        foreach (var (size, limited) in new[] { (64, false), (13_500, true) })
        {
            var text = new StringBuilder("func f(flag: bool) -> i32\n");
            for (var n = 0; n < size; n++)
            {
                text.Append("    var v").Append(n).Append(" = ").Append(n).Append("\n    let r").Append(n).Append(" = v").Append(n).Append("@ref\n");
            }

            for (var n = 0; n < size; n++)
            {
                text.Append("    if flag => v").Append(n).Append(" = r").Append(n).Append("@follow\n");
            }

            var (c, program, body) = Body(text.Append("    return 0\n").ToString(), "f");
            var (_, report) = Analyze(c, program, body, 0);
            var problems = report.All.ToArray();
            Assert.Equal(limited ? 1 : 0, problems.Length);
            Assert.All(problems, x => Assert.Equal((DiagnosticCode.OwnershipStorageLimit_Kd, body.Source, 1UL), (x.Code, x.Subject, x.Views)));
        }
    }

    // SPEC 15.3.5: TyCtxt.Relate relates a value fit's Origin positions: a ref's outer Origin and target covariantly, a uniq's target and
    // an obj's payload invariantly, an rc's payload covariantly, a slot by its declared variance (a contravariant one reversed), nothing
    // inside a Function Type; Types of another structure do not fit, and an unknown Type fits any.
    [Fact]
    public void RelateComposesVariance()
    {
        var c = MirBuildTest.Compile("struct Holder {h}\n    public let r: ref/i32 during h\n    public init(r: ref/i32 during h) => self.r = r\n" +
            "struct Handler {h}\n    public let f: (ref/i32 during h) -> ()\nfunc f(x: ref/i32, a: ref/(ref/i32 during x), b: uniq/(ref/i32 during x), " +
            "g: (ref/i32) -> (), h1: Holder, h2: Holder, k1: Handler, k2: Handler, o1: obj/Holder, o2: obj/Holder, r1: rc/Holder, r2: rc/Holder)\n" +
            "    return\n");
        var f = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        BoundType Type(int arg) => f.Locals[arg].Type;
        BoundOrigin Slot(int arg) => (Type(arg).OriginArguments.Count > 0 ? Type(arg) : Type(arg).Components[0]).OriginArguments[0];
        (BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)[] Relations(int actual, int expected)
        {
            var relations = new List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)>();
            Assert.True(TyCtxt.Relate(Type(actual), Type(expected), invariant: false, relations));
            return [.. relations];
        }

        Assert.Equal([(Type(2).Origin, Type(2).Origin, false), (Type(1).Origin, Type(1).Origin, false)], Relations(2, 2));
        Assert.Equal([(Type(3).Origin, Type(3).Origin, false), (Type(1).Origin, Type(1).Origin, true)], Relations(3, 3));
        Assert.Empty(Relations(4, 4));
        Assert.Equal([(Slot(5), Slot(6), false)], Relations(5, 6));
        Assert.Equal([(Slot(8), Slot(7), false)], Relations(7, 8));
        Assert.Equal([(Slot(9), Slot(10), true)], Relations(9, 10));
        Assert.Equal([(Slot(11), Slot(12), false)], Relations(11, 12));
        Assert.False(TyCtxt.Relate(Type(2), Type(3), invariant: false, null));
        Assert.True(TyCtxt.InvariantTarget(Type(9), null)); // N1: an Upcast of an obj handle relates its payload both ways, of an rc one forward.
        Assert.False(TyCtxt.InvariantTarget(Type(11), null));
        Assert.True(TyCtxt.Relate(null, Type(3), invariant: false, null));
    }

    // SPEC 15.6.4 step 3: TyCtxt.Clauses gives a callee's declared relations under a call's substitution, at the instantiated parameters'
    // Origins.
    [Fact]
    public void ClausesAreSubstitutedAtTheCall()
    {
        var c = MirBuildTest.Compile("func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\n" +
            "func f(x: ref/i32, y: ref/i32) -> i32 => two(x, y)@follow\n");
        var f = c.BuildMir().Bodies.ToArray().Single(static x => x.Key.Decl?.Name == "f");
        var call = f.Blocks.ToArray().Single(static x => x.Terminator.Kind == MirTerminatorKind.Call).Terminator.Callee;
        var relations = new List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)>();
        c.Types.Clauses(call.Symbol!, call.Subst, relations);
        var (longer, shorter, equal) = Assert.Single(relations);
        Assert.Same(TyCtxt.Signature(call.Symbol!, call.Subst, 0, out _)!.Origin, longer);
        Assert.Same(TyCtxt.Signature(call.Symbol!, call.Subst, 1, out _)!.Origin, shorter);
        Assert.False(equal);
    }

    // The old path's verdict at a source's mutation (or reported) statement equals whether a Loan on the root that statement writes is active
    // before it: every listed source of the old Loan classes, the conflicts and the valid programs, a part replaced in place included (cells,
    // R4-M24). The open departures are marked and assert the disagreement: Q1-ref (PLAN, U8b), a complete replacement through a reference
    // keeps the referent's earlier Loans (a sound over-approximation); A5-closure (PLAN, U7b), a Closure's call misses a relation Binding
    // inferred inside it.
    [Theory]
    [MemberData(nameof(OldVerdicts))]
    public void TheOldVerdictsAgree(string origin, string source, string statement, string root, bool conflict, string departure)
    {
        var active = Active(source, statement, root);
        output.WriteLine($"{origin}: {statement} {root} active {active}, old {(conflict ? "conflict" : "valid")}{departure}");
        Assert.Equal(departure.Length > 0 ? !conflict : conflict, active);
    }

    // N2 (R4-M24): every relation end is an atom of its side, but the static Origin, a bound that holds no Loan: none is unmapped in any view
    // of any body of the library, the milestones and the diagnostic corpus (SemanticSnapshotTest.CaseNames).
    [Fact]
    public void EveryRelationEndIsAnAtomOfItsSide()
    {
        var unmapped = new List<string>();
        foreach (var name in SemanticSnapshotTest.CaseNames())
        {
            var (c, _) = SemanticSnapshotTest.Bind(name);
            var program = c.BuildMir();
            var (flow, moves, cases, report) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirReport());
            var borrows = new MirBorrows(flow, moves, cases, c.Types);
            for (var b = 0; program.Findings.Count == 0 && b < program.Bodies.Length; b++)
            {
                var body = program.Bodies[b];
                if (name != "library" && body.Source.Module != 0)
                {
                    continue; // The library's bodies, once.
                }

                flow.Begin(body);
                moves.Build(body, flow);
                for (var view = 0L; view < MirCases.Count(body); view++)
                {
                    cases.Select(body, view);
                    borrows.Run(body, view, program.Drops, report);
                    if (borrows.Unmapped > 0)
                    {
                        unmapped.Add($"{name} {body.Key.Decl?.Name} {body.Key.Kind} view {view}: {borrows.Unmapped}");
                    }
                }
            }
        }

        Assert.True(unmapped.Count == 0, string.Join('\n', unmapped));
    }

    // V16 (R4-M24, PLAN): A5-1's work is bounded by the body, its output (the Loans active before each location) and its dense word terms.
    // MirBorrows.Work, the walks, the counts' changes and the events, is linear in the body on N borrows live across N statements, N
    // two-phase calls, N copies of one Loan read in line or each in its own branch, N Loans on one root then N whole writes of it and one
    // local reassigned N times, and within a constant of size + output on N branches reassigning one var, on their Loans passed through N
    // copies (u7-decision Q4) and on 16 arms of a match each keeping its own N/16 holders live (consecutive chains with large, disjoint live
    // sets: the difference form's case). MirBorrows.Dense, the words the heads compare, the storage ends clear and the reaching bits take, is
    // within a constant of the locations times the stored and Loan words. N 64/128/256.
    [Theory]
    [InlineData("    var v{0} = {0}\n    let r{0} = v{0}@ref\n", "    total += r{0}@follow\n", true)]
    [InlineData("    var c{0} = Counter.init()\n    c{0}.bump({0})\n", "", true)]
    [InlineData("    let c{0} = r\n", "    total += c{0}@follow\n", true)]
    [InlineData("    let c{0} = r\n", "    if flag => total += c{0}@follow\n", true)]
    [InlineData("    let r{0} = x@ref\n    total += r{0}@follow\n", "    x = {0}\n", true)]
    [InlineData("    view = v{0}@ref\n    total += view@follow\n", "", true)]
    [InlineData("arms", "", false)]
    [InlineData("    if flag => view = v{0}@ref\n", "", false)]
    [InlineData("    if flag => view = v{0}@ref\n", "    let c{1} = c{0}\n", false)]
    public void WorkIsBoundedByTheBodyAndItsOutput(string group, string tail, bool linear)
    {
        var (sizes, work, measure, dense, words) = (new[] { 64, 128, 256 }, new long[3], new long[3], new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var (c, program, body) = Body(group == "arms" ? Arms(sizes[i]) : Shape(group, tail, sizes[i]), "work");
            var (borrows, report) = Analyze(c, program, body, 0);
            Assert.Equal(0, report.All.Length);
            (work[i], dense[i]) = (borrows.Work, borrows.Dense);
            var counter = default(Output);
            borrows.Replay(ref counter);
            var size = body.Blocks.Length + body.Statements.Length;
            measure[i] = size + (linear ? 0 : counter.Active);
            words[i] = size * (1 + borrows.StoredWords + ((borrows.Loans.Length + 63) >> 6));
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: work {string.Join(", ", work)}, {(linear ? "size" : "size + output")} {string.Join(", ", measure)}; " +
            $"dense {string.Join(", ", dense)}, locations x words {string.Join(", ", words)}");
        for (var i = 0; i < sizes.Length; i++)
        {
            Assert.True(work[i] <= 8 * measure[i], $"work {work[i]} above 8 x {measure[i]} at N {sizes[i]}");
            Assert.True(dense[i] <= 8 * words[i], $"dense {dense[i]} above 8 x {words[i]} at N {sizes[i]}");
        }
    }

    // V16: the cold allocation of a body's analysis (fresh components) grows linearly on the linear shapes; the output gives the stored words
    // per head. MirBuildTest.WarmRebuildAllocatesNothing covers the warm re-analysis, which BuildMir runs.
    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData("    var v{0} = {0}\n    let r{0} = v{0}@ref\n", "    total += r{0}@follow\n")]
    [InlineData("    var c{0} = Counter.init()\n    c{0}.bump({0})\n", "")]
    [InlineData("    let c{0} = r\n", "    total += c{0}@follow\n")]
    public void ColdAllocationIsLinear(string group, string tail)
    {
        var (sizes, allocated, words) = (new[] { 16, 32, 64 }, new long[3], new long[3]);
        for (var i = 0; i < sizes.Length; i++)
        {
            var (c, program, body) = Body(Shape(group, tail, sizes[i]), "work");
            var before = GC.GetAllocatedBytesForCurrentThread();
            var (borrows, report) = Analyze(c, program, body, 0);
            allocated[i] = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, report.All.Length);
            words[i] = borrows.StoredWords;
        }

        output.WriteLine($"N {string.Join(", ", sizes)}: cold analysis allocation {string.Join(", ", allocated)}, stored words per head {string.Join(", ", words)}");
        Assert.True(allocated[2] - allocated[1] <= 3 * (allocated[1] - allocated[0]), $"cold analysis allocation {string.Join(", ", allocated)}");
    }

    // A V16 shape: N locals, a group of statements per local, then a tail per local, in one body.
    internal static string Shape(string group, string tail, int size)
    {
        var text = new StringBuilder("struct Counter\n    var count: i32 = 0\n    public func bump(self: uniq/Self, by: i32) => self.count = self.count + by\n");
        text.Append("func work(flag: bool) -> i32\n    var total = 0\n    var x = 1\n    let r = x@ref\n");
        for (var n = 0; n < size; n++)
        {
            text.Append("    var v").Append(n).Append(" = ").Append(n).Append('\n');
        }

        text.Append("    var view = x@ref\n");
        for (var n = 0; n < size; n++)
        {
            text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, group, n);
        }

        text.Append("    let c0 = view\n");
        for (var n = 0; n < size; n++)
        {
            text.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, tail, n, n + 1);
        }

        return text.Append("    return total + view@follow\n").ToString();
    }

    // A5-1 of a body under a view with fresh components, as the driver runs it, A2's verdicts the program's.
    internal static (MirBorrows Borrows, MirReport Report) Analyze(Compilation c, MirProgram program, MirBody body, long view)
    {
        var (flow, moves, cases, report) = (new MirDataflow(new MirReach()), new MirMoveData(), new MirCases(c.Types), new MirReport());
        var borrows = new MirBorrows(flow, moves, cases, c.Types);
        flow.Begin(body);
        moves.Build(body, flow);
        cases.Select(body, view);
        borrows.Run(body, view, program.Drops, report);
        return (borrows, report);
    }

    // The first line of the subject of a location's source.
    internal static string Subject(Compilation c, MirBody body, in SourceInfo source) => c.SyntaxOf(body.Subject(source)).ToString().Split('\n')[0].Trim();

    // A V16 shape: N borrows read in 16 arms of a match, each arm its own N/16.
    private static string Arms(int size)
    {
        var text = new StringBuilder("func work(flag: bool) -> i32\n    var total = 0\n");
        for (var n = 0; n < size; n++)
        {
            text.Append("    var v").Append(n).Append(" = ").Append(n).Append("\n    let r").Append(n).Append(" = v").Append(n).Append("@ref\n");
        }

        text.Append("    match total\n");
        for (var n = 0; n < size; n++)
        {
            if (n % (size / 16) == 0)
            {
                text.Append("        ").Append(n / (size / 16)).Append('\n');
            }

            text.Append("            total += r").Append(n).Append("@follow\n");
        }

        return text.Append("        _ => ()\n    return total\n").ToString();
    }

    // Whether a Loan on a root (a local of that name, or the referent of an Arg of that name) is active before a statement: at the first
    // location the replay reaches whose subject is that statement.
    private static bool Active(string source, string statement, string root, long view = 0)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        var body = program.Bodies.ToArray().Last(x => x.Source.Module == 0 && Subjects(c, x).Contains(statement)); // Helpers come first.
        var (borrows, _) = Analyze(c, program, body, view);
        var recorder = new Recorder(c, body, statement);
        borrows.Replay(ref recorder);
        var loans = borrows.Loans;
        for (var i = 0; recorder.Found && i < loans.Length; i++)
        {
            var name = loans[i].Place.IsStatic ? body.Statics[~loans[i].Place.Root].Symbol.Name : body.Locals[loans[i].Place.Root].Name;
            if ((recorder.Active[i >> 6] & (1UL << i)) != 0 && name == root)
            {
                return true;
            }
        }

        return false;
    }

    // The first line of the subject of each location of a body.
    private static IEnumerable<string> Subjects(Compilation c, MirBody body)
    {
        foreach (var block in body.Blocks.ToArray())
        {
            for (var i = 0; i <= block.Count; i++)
            {
                yield return Subject(c, body, i < block.Count ? body.Statements[block.Start + i].Source : block.Terminator.Source);
            }
        }
    }

    // The body of a name.
    private static (Compilation Compilation, MirProgram Program, MirBody Body) Body(string source, string name)
    {
        var c = MirBuildTest.Compile(source);
        var program = c.BuildMir();
        Assert.Empty(program.Findings);
        return (c, program, program.Bodies.ToArray().Single(x => x.Source.Module == 0 && x.Key.Kind == MirBodyKind.Fn && x.Key.Decl?.Name == name));
    }

    // The output of a replay: the Loans active before each location, summed.
    private struct Output : IMirLoans
    {
        internal long Active { get; private set; }

        public void At(MirLocation location, ReadOnlySpan<ulong> active)
        {
            foreach (var word in active)
            {
                this.Active += System.Numerics.BitOperations.PopCount(word);
            }
        }
    }

    // The Loans active before the first location of a statement the replay reaches.
    private struct Recorder(Compilation c, MirBody body, string statement) : IMirLoans
    {
        internal ulong[] Active { get; private set; } = [];

        internal bool Found { get; private set; }

        public void At(MirLocation location, ReadOnlySpan<ulong> active)
        {
            ref readonly var block = ref body.Blocks[location.Block];
            var source = location.Index < block.Count ? body.Statements[block.Start + location.Index].Source : block.Terminator.Source;
            if (!this.Found && Subject(c, body, source) == statement)
            {
                (this.Found, this.Active) = (true, active.ToArray());
            }
        }
    }
}
