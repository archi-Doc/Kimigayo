// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>A5-F8's coverage of collection updates and destroying calls on MIR (SPEC 4.9.3, 4.9.4, 4.9.6, 14.6.2, 15.6.4, 15.6.7,
/// 16.3.3; the A5 Step 2 plan's 2d-6, 2d-7 and 2d-10, PLAN G396 (1)): an Array or Dictionary call activates its exclusive receiver against
/// live element, view, iterator and lookup Loans; added inputs join the collection's dependencies and none subtracts them; a callee
/// destroying a by-value input and a destroying collection operation compose the destroyed parts' drops; F8's update work grows linearly
/// and allocates nothing warm. Each row's label is from SPEC; the rows passed on arrival, as coverage rows do.</summary>
public sealed partial class MirBorrowsTest
{
    // The update shapes' head: r borrows p.a shared and stays live to the end; x, q and o are the updates' other operands.
    private const string UpdateChainHead =
        "func shape()\n    var p = Pair.init()\n    var q = Pair.init()\n    var x: i32 = 0\n    var o: Option<i32> = .Some(1)\n    let r = p.a@ref\n";

    // SPEC 4.9.6 ("The exclusive receiver is reserved before later arguments and activated at entry; element and Slice Loans live at
    // activation conflict"): append, insert, remove, clear and pop meet a live element Loan at their activation (e1i-e4p), as do a view
    // (e5i, e5c: 4.9.6's example with clear) and a for Subject's Iterator in the loop body (e6i, e6r; SPEC 14.6.2); an exclusive element
    // Loan (indexUniq, tryGetUniq) is met where the receiver's reservation is formed (e7i, e11i: R4-M68). A Dictionary's tryGet result
    // Loans the whole Dictionary (e15i; e15r, an indexed replacement, which checks every Loan before writing, SPEC 4.9.4), as does
    // `map[key]` (e17i, e17c; SPEC 4.7.1), and its Iterator meets clear (e19i). Each valid twin ends the Loan first; a Copy argument
    // precedes the activation (e8v, SPEC 4.9.3's example).
    [Fact]
    public void ACollectionCallMeetsLiveElementViewAndIteratorLoans()
        => Assert.Equal(
            [
                "e1i: values.append(3) CallActivationConflict_Kd", "e2i: values.insert(0, 5) CallActivationConflict_Kd",
                "e3i: values.remove(0) CallActivationConflict_Kd", "e4i: values.clear() CallActivationConflict_Kd",
                "e4p: values.pop() CallActivationConflict_Kd", "e5i: values.append(1) CallActivationConflict_Kd",
                "e5c: values.clear() CallActivationConflict_Kd", "e6i: values.append(v) CallActivationConflict_Kd",
                "e6r: values.remove(0) CallActivationConflict_Kd", "e7i: values", "e11i: values",
                "e15i: d.tryInsert(3, 30) CallActivationConflict_Kd", "e15r: d[2] CallActivationConflict_Kd",
                "e17i: d.remove(2) CallActivationConflict_Kd", "e17c: d.clear() CallActivationConflict_Kd",
                "e19i: d.clear() CallActivationConflict_Kd",
            ],
            Problems("""
                func e1i()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    values.append(3)
                    inspect(r)
                func e1v()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    inspect(r)
                    values.append(3)
                func e2i()
                    var values: Array<i32> = [1, 2]
                    let r = values[1]@ref
                    values.insert(0, 5)
                    inspect(r)
                func e2v()
                    var values: Array<i32> = [1, 2]
                    let r = values[1]@ref
                    inspect(r)
                    values.insert(0, 5)
                func e3i()
                    var values: Array<i32> = [1, 2]
                    let r = values[1]@ref
                    _ = values.remove(0)
                    inspect(r)
                func e3v()
                    var values: Array<i32> = [1, 2]
                    let r = values[1]@ref
                    inspect(r)
                    _ = values.remove(0)
                func e4i()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    values.clear()
                    inspect(r)
                func e4v()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    inspect(r)
                    values.clear()
                func e4p()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    _ = values.pop()
                    inspect(r)
                func e5i()
                    var values: Array<i32> = [1, 2]
                    let view = values[..]
                    values.append(1)
                    inspect(view)
                func e5v()
                    var values: Array<i32> = [1, 2]
                    let view = values[..]
                    inspect(view)
                    values.append(1)
                func e5c()
                    var values: Array<i32> = [1, 2]
                    let view = values.slice()
                    values.clear()
                    let first = view[0]
                func e6i()
                    var values: Array<i32> = [1, 2]
                    for v in values
                        values.append(v)
                func e6v()
                    var values: Array<i32> = [1, 2]
                    var total: i32 = 0
                    for v in values
                        total += v
                    values.append(total)
                func e6r()
                    var values: Array<i32> = [1, 2]
                    for v in values
                        _ = values.remove(0)
                func e7i()
                    var values: Array<i32> = [1, 2]
                    let u = values[0]@uniq
                    values.append(1)
                    u@follow = 2
                func e7v()
                    var values: Array<i32> = [1, 2]
                    let u = values[0]@uniq
                    u@follow = 2
                    values.append(1)
                func e11i()
                    var values: Array<i32> = [1, 2]
                    let t = values.tryGetUniq(0)
                    _ = values.pop()
                    inspect(t)
                func e11v()
                    var values: Array<i32> = [1, 2]
                    let t = values.tryGetUniq(0)
                    inspect(t)
                    _ = values.pop()
                func e8v()
                    var values: Array<i32> = [1, 2]
                    values.append(values[0])
                    values.insert(0, values[1])
                func e15i()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d.tryGet(1)
                    _ = d.tryInsert(3, 30)
                    inspect(r)
                func e15v()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d.tryGet(1)
                    inspect(r)
                    _ = d.tryInsert(3, 30)
                func e15r()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d.tryGet(1)
                    d[2] = 5
                    inspect(r)
                func e17i()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d[1]@ref
                    _ = d.remove(2)
                    inspect(r)
                func e17v()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d[1]@ref
                    inspect(r)
                    _ = d.remove(2)
                func e17c()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    let r = d[1]@ref
                    d.clear()
                    inspect(r)
                func e19i()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    for e in d
                        d.clear()
                func e19v()
                    var d: Dictionary<i32, i32> = [1: 10, 2: 20]
                    var n: i32 = 0
                    for e in d
                        n += 1
                    d.clear()
                """));

    // SPEC 4.9.6 ("Added and replacing inputs' dependencies join the collection's set, also when tryInsert returns Err; removed values
    // carry the stored Types' full dependencies; no operation subtracts dependencies"): a removed element (e13i) and insertOrReplace's
    // old value (e20i) keep x's Loan; clear, pop and truncate after append or insert subtract it from no Array (e14i, e14p, e14t); an Err
    // input of tryInsert joins (e16i). Each valid twin's last use precedes `x = 5`; an Array of `ref/i32` observes nothing when
    // destroyed (SPEC 15.6.6), so its storage end needs no Loan.
    [Fact]
    public void ACollectionsDependenciesJoinAndAreNeverSubtracted()
        => Assert.Equal(
            ["e13i: x = 5", "e14i: x = 5", "e14p: x = 5", "e14t: x = 5", "e16i: x = 5", "e20i: x = 5"],
            Problems("""
                func e13i()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    let r = refs.remove(0)
                    x = 5
                    inspect(r)
                func e13v()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    let r = refs.remove(0)
                    inspect(r)
                    x = 5
                func e14i()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    refs.clear()
                    x = 5
                    inspect(refs)
                func e14v()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    refs.clear()
                    inspect(refs)
                    x = 5
                func e14p()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    _ = refs.pop()
                    x = 5
                    inspect(refs)
                func e14t()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.insert(0, x@ref)
                    refs.truncate(0)
                    x = 5
                    inspect(refs)
                func e16i()
                    var x = 1
                    var d: Dictionary<i32, ref/i32> = [:]
                    var z = 2
                    d[1] = z@ref
                    _ = d.tryInsert(1, x@ref)
                    x = 5
                    inspect(d)
                func e16v()
                    var x = 1
                    var d: Dictionary<i32, ref/i32> = [:]
                    var z = 2
                    d[1] = z@ref
                    _ = d.tryInsert(1, x@ref)
                    inspect(d)
                    x = 5
                func e20i()
                    var x = 1
                    var d: Dictionary<i32, ref/i32> = [:]
                    var z = 2
                    _ = d.insertOrReplace(1, x@ref)
                    let old = d.insertOrReplace(1, z@ref)
                    x = 5
                    inspect(old)
                func e20v()
                    var x = 1
                    var d: Dictionary<i32, ref/i32> = [:]
                    var z = 2
                    _ = d.insertOrReplace(1, x@ref)
                    let old = d.insertOrReplace(1, z@ref)
                    inspect(old)
                    x = 5
                """));

    // SPEC 15.6.4 ("a callee that destroys a by-value input or the elements of its receiver includes that cleanup in its own contract"),
    // 16.3.3, 4.9.6's destroying rows, beside APublishedDestroyingCallComposesItsRowsDestruction: a callee destroying a by-value Writer or
    // a by-value Array of them (k1i, k2i), a discarded pop or Dictionary remove result (k3i, k7i), an Array indexed replacement (k4i) and a
    // Dictionary clear (k5i) compose the destroyed Writer's drop, which stores q (addressing w, which carries y's Loan) into slot, so
    // `y@follow = 5` errs while slot is used below; each null twin's drop stores nothing that carries y. clear's activation meets a live
    // element Loan whatever the drop (k8i). A Dictionary's indexed replacement of a dropping value is not here: on dev its valid twin is
    // rejected at the Dictionary's storage end, as a write through an exclusive reference destroying such a value is (Z12b's gap).
    [Fact]
    public void ADestroyingCallComposesTheDropsOfWhatItDestroys()
    {
        const string Shapes = "func eat(w: Writer) => ()\nfunc eatAll(ws: Array<Writer>) => ()\n";
        const string Bad = "    let w = y@follow@ref\n    var slot: raw/u8 = null\n    var x = Writer.init(q: w@raw@raw/u8)\n    x.p = slot@raw\n";
        const string Null = "    let w = y@follow@ref\n    var slot: raw/u8 = null\n    var x = Writer.init(q: null)\n    x.p = slot@raw\n";
        const string Tail = "    y@follow = 5\n    usep(slot)\n";
        (string Name, string Body)[] forms =
        [
            ("k1", "    eat(x@move)\n"),
            ("k2", "    var ws: Array<Writer> = []\n    ws.append(x@move)\n    eatAll(ws@move)\n"),
            ("k3", "    var ws: Array<Writer> = []\n    ws.append(x@move)\n    _ = ws.pop()\n"),
            ("k4", "    var ws: Array<Writer> = []\n    ws.append(x@move)\n    ws[0] = Writer.init(q: null)\n"),
            ("k5", "    var d: Dictionary<i32, Writer> = [:]\n    _ = d.insertOrReplace(1, x@move)\n    d.clear()\n"),
            ("k7", "    var d: Dictionary<i32, Writer> = [:]\n    _ = d.insertOrReplace(1, x@move)\n    _ = d.remove(1)\n"),
        ];
        var text = new System.Text.StringBuilder(DropShapes).Append(Shapes);
        foreach (var (name, body) in forms)
        {
            text.Append("func ").Append(name).Append("i(y: uniq/i32)\n").Append(Bad).Append(body).Append(Tail)
                .Append("func ").Append(name).Append("v(y: uniq/i32)\n").Append(Null).Append(body).Append(Tail);
        }

        text.Append("func k8i(y: uniq/i32)\n").Append(Null).Append("    var ws: Array<Writer> = []\n    ws.append(x@move)\n    let r = ws[0]@ref\n")
            .Append("    ws.clear()\n    inspect(r)\n")
            .Append("func k8v(y: uniq/i32)\n").Append(Null).Append("    var ws: Array<Writer> = []\n    ws.append(x@move)\n    let r = ws[0]@ref\n")
            .Append("    inspect(r)\n    ws.clear()\n");
        var c = MirBuildTest.Compile(text.ToString());
        Assert.Equal(
            [
                "k1i: y@follow = 5", "k2i: y@follow = 5", "k3i: y@follow = 5", "k4i: y@follow = 5", "k5i: y@follow = 5", "k7i: y@follow = 5",
                "k8i: ws.clear() CallActivationConflict_Kd",
            ],
            Problems(c, Analyze(c), [.. LoanCodes, DiagnosticCode.Unsupported_Kd]));
    }

    // 2d-10's records (DIAGNOSTICS A6's table): an activation meeting a shared element Loan is CallActivationConflict_Kd at the call,
    // with the Loan's Borrow, its holder's next use and the receiver's reservation (e1i, k8i); an exclusive element Loan the reservation
    // meets where it is formed is ComparisonLoanConflict_Kd at the receiver, with no reservation fact (e7i, R4-M68); a write meeting a
    // Loan a collection or a destroyed part's store keeps is ComparisonLoanConflict_Kd at the write, witnessed by the next use of the
    // value that keeps it: the removed element's call, the Dictionary's implicit Borrow at `inspect(d)`, the slot's call (e13i, e16i, k1i).
    [Fact]
    public void AnF8ProblemsRecordsFollowA6()
        => Assert.Equal(
            [
                "values.append(3): Anchor values[0]@ref, Use inspect(r), Reservation values", "values: Anchor values[0]@uniq, Use u@follow = 2",
                "x = 5: Anchor x@ref, Use inspect(r)", "x = 5: Anchor x@ref, Use d", "y@follow = 5: Anchor y@follow@ref, Use usep(slot)",
                "ws.clear(): Anchor ws[0]@ref, Use inspect(r), Reservation ws",
            ],
            Witnessed(DropShapes + """
                func eat(w: Writer) => ()
                func e1i()
                    var values: Array<i32> = [1, 2]
                    let r = values[0]@ref
                    values.append(3)
                    inspect(r)
                func e7i()
                    var values: Array<i32> = [1, 2]
                    let u = values[0]@uniq
                    values.append(1)
                    u@follow = 2
                func e13i()
                    var x = 1
                    var refs: Array<ref/i32> = []
                    refs.append(x@ref)
                    let r = refs.remove(0)
                    x = 5
                    inspect(r)
                func e16i()
                    var x = 1
                    var d: Dictionary<i32, ref/i32> = [:]
                    var z = 2
                    d[1] = z@ref
                    _ = d.tryInsert(1, x@ref)
                    x = 5
                    inspect(d)
                func k1i(y: uniq/i32)
                    let w = y@follow@ref
                    var slot: raw/u8 = null
                    var x = Writer.init(q: w@raw@raw/u8)
                    x.p = slot@raw
                    eat(x@move)
                    y@follow = 5
                    usep(slot)
                func k8i()
                    var ws: Array<Writer> = [Writer.init(q: null)]
                    let r = ws[0]@ref
                    ws.clear()
                    inspect(r)
                """));

    // V16 "F8 updates" (A3 6.3; the A5 Step 2 plan's 2d-7): N updates under a live shared Loan of a disjoint Field (N = 10, 20, 30), each
    // variant one F8 form: compound assignment, increment, a whole-value replace, a swap of two disjoint Fields and a guarded match whose
    // arm updates. Each update meets the same few live holders, so the work grows by equal steps: linear in N. A guard, not a regression:
    // no problem arises.
    [Theory]
    [InlineData("compound", "    x += {1}\n    p.b += x\n")]
    [InlineData("increment", "    x++\n    p.b++\n")]
    [InlineData("replace", "    Kimi.Intrinsics.replace(p.b@uniq, with: x + {0})\n")]
    [InlineData("swap", "    Kimi.Intrinsics.swap(q.a@uniq, q.b@uniq)\n    p.b += q.a\n")]
    [InlineData("match", "    match o\n        .Some(let n) if n > {0} => p.b += 1\n        _ => ()\n")]
    public void AnF8UpdatesWorkGrowsLinearly(string variant, string each)
    {
        var shape = UpdateChainHead + "«N:" + each + "»    inspect(r)\n";
        long[] work = [.. new[] { 10, 20, 30 }.Select(n => Measured(Prelude + Expand(shape, n, 0)).Work)];
        output.WriteLine($"{variant}: N 10, 20, 30: work {string.Join(", ", work)}");
        Assert.True(Grows(work, 1.25), $"{variant}: work {string.Join(", ", work)}");
    }

    // V16: A5 on a program version it checked before allocates nothing at F8's updates: each variant of AnF8UpdatesWorkGrowsLinearly.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmF8UpdateAllocatesNothing()
    {
        var c = MirBuildTest.Compile(Prelude + UpdateChainHead + "    x += 1\n    p.b += x\n    x++\n    p.b++\n    Kimi.Intrinsics.replace(p.b@uniq, with: x)\n" +
            "    Kimi.Intrinsics.swap(q.a@uniq, q.b@uniq)\n    match o\n        .Some(let n) if n > 0 => p.b += 1\n        _ => ()\n    inspect(r)\n");
        var program = c.BuildMir();
        var stratum = new Stratum(c.Types);
        Assert.Equal(0, AllocationMeasurement.Measure(() => stratum.Run(program)));
        Assert.Empty(Problems(c, program, LoanCodes));
    }
}
