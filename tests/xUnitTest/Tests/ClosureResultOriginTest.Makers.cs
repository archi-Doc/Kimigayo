// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// PLAN G273's acceptance rows (R4 notes g273\problem-draft.md with problem-check.md; WORKFLOW Full step 2, written before either
// candidate; they fail on dev by design where dev differs). A Closure value returned by a call of another Closure, its maker, is a value
// of the inner Closure Type with the Origin bindings its creation made in that call (SPEC 3.2.1, 7.7.2, 7.7.3, 15.8.2, 15.6.4): the
// maker's receiver ρ and its environment bindings stand for the receiver acquisition's Origin, its per-call input for the argument's
// Origin, and every later use reads the call signature with those bindings, in every Origin position, raw layers included (SPEC 5.7,
// 15.3.4). Each row states the MIR side exactly (MirDifferenceLedger.Sides: Binding's records and the MIR's), the default side D1 or D2
// (problem-check K2), and a verdict Binding owns exactly as published (AssertSides).
public partial class ClosureResultOriginTest
{
    private const string Document = "Hello.kimi";

    private const string MainBody = "public func main()\n";

    private const string MainUnit = "public func main() => ()\n";

    private const string ReceiverMaker = "    let n: i32 = 4\n    let outer = func [n] () => func [n@ref] () => n\n";

    private const string InputOuter = "    let outer = func (x: ref/i32) => func [x] () => x\n";

    private const string InputMaker = "    var local: i32 = 1\n" + InputOuter;

    private const string ACall = "    let a = outer()\n    let r = a()\n";

    private const string Moved = "    let moved = outer@move\n";

    private const string InspectR = "    inspect(r)\n";

    private const string Keep = "func keep<T>(x: T) -> T => x@move\n";

    private const string TwoEnv = "    let m: i32 = 3\n    let n: i32 = 4\n    let outer = func [m, n] () => func [m@ref, n@ref] () => (m, n)\n";

    private const string Wrap = "    let wrap = func [outer] () => outer()()\n    let r = wrap()\n";

    private const string N1 = "    var x: i32 = 1\n    let u = x@uniq\n    var outer = func [u] () => func [u] () => u@move\n";

    private const string Texts = "func makeText() -> string => \"a\"\nfunc consumeText(value: string) => ()\n";

    private const string ConsumingMaker = "    let outer = func [u, t@move] ()\n        consumeText(t@move)\n        return func [u] () => u@move\n";

    private const string F1i = Texts + MainBody + "    var x: i32 = 1\n    let u = x@uniq\n    let t = makeText()\n" + ConsumingMaker +
        "    let a = outer@move()\n    let pa = a@move()\n";

    private const string FieldHolder = "struct Holder<F>\n    public let f: F\n    public init(f: F) => self.f = f\n";

    private const string OptionCall = "    let o = Option.Some(outer())\n    match o\n        .Some(let c)\n            let r = c()\n";

    private const string CopyMaker = "    var local: i32 = 1\n    let p = local@ref\n    let outer = func [p] () => func [p@move] () => p\n";

    private const string CopyInputMaker = "    var local: i32 = 1\n    let other: i32 = 2\n    let p = other@ref\n" +
        "    let outer = func [p] (x: ref/i32) => func [p@move, x] () => x\n    let a = outer(local@ref)\n    let r = a()\n";

    private const string ReturnH = "func h(p: ref/i32) -> ref/i32 during p\n";

    private const string Mixed = "    var local: i32 = 1\n    let n: i32 = 4\n    let outer = func [n] (x: ref/i32) => func [n@ref, x] () => (n, x)\n" +
        "    let a = outer(local@ref)\n    let r = a()\n";

    private const string NoOrigin = "func use() -> i32\n    var local: i32 = 1\n    let outer = func (x: ref/i32) => func [x] () -> i32 => x@follow\n" +
        "    let a = outer(local@ref)\n";

    private const string Mk2Receiver = "    let n: i32 = 4\n    let mk2 = func [n] () => func [n@ref] () => func [n] () => n\n";

    private const string Mk2Mid = "    let n: i32 = 4\n    let mk2 = func [n] () => func [n] () => func [n@ref] () => n\n    let b = mk2()\n    let a = b()\n" +
        "    let r = a()\n";

    private const string Mk2Input = "    var local: i32 = 1\n    let mk2 = func (x: ref/i32) => func [x] () => func [x] () => x\n    let r = mk2(local@ref)()()\n";

    private const string TwoMakers = "    let m: i32 = 3\n    let n: i32 = 4\n" +
        "    let mk = func [m, n] () => func [m@ref, n] () => func [m, n@ref] () => (m, n)\n    let b = mk()\n    let a = b()\n    let r = a()\n";

    private const string CallableRef = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    return 0\n";

    private const string CallableI32 = "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> i32>\n    return 0\n";

    private const string ApplyRef = "    let r = apply(a@ref)\n";

    private const string RawInput = "    let outer = func (x: ref/i32) => func [x] () => x@follow@raw\n";

    private const string RawReceiver = "    let n: i32 = 1\n    let outer = func [n] () => func [n@ref] () => n@follow@raw\n";

    private const string RawReturnH = "func h(q: ref/i32) -> raw/i32 during q\n";

    private const string RawOut = "func h(q: ref/i32, out: uniq/(raw/i32 during q))\n";

    private const string RawRetain = "func usep(p: raw/i32) => ()\n" + MainBody + "    var local: i32 = 1\n    let lr = local@ref\n" +
        "    let outer = func (x: ref/i32) => func [x] (slot: uniq/(raw/i32 during x)) => slot@follow = x@follow@raw\n    let a = outer(lr)\n" +
        "    var t: raw/i32 during lr = null\n    a(t@uniq)\n";

    private const string InnerInMaker = MainBody + "    var x: i32 = 1\n    let u = x@uniq\n    var outer = func [u] () -> i32\n        let i = func [u] () => u@move\n" +
        "        let p = i@move()\n        p@follow = 2\n        return 0\n    _ = outer()\n";

    private const string MakerBody = Inspect + MainBody + "    var local: i32 = 1\n    let outer = func (x: ref/i32) -> i32\n        let inner = func [x] () => x\n" +
        "        return inner()@follow\n    inspect(outer(local@ref)@ref)\n";

    private const string MidInOuter = "func use(p: ref/i32) -> i32\n    let outer = func (x: ref/i32) -> i32\n        let mid = func [x] () => func [x] () => x\n" +
        "        let a = mid()\n        return a()@follow\n    return outer(p)\n" + MainUnit;

    private const string InputResult = "func apply<F>(f: ref/F, v: ref/i32) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    return 0\n" + Use +
        "    let n: i32 = 1\n    let k: i32 = 2\n    let outer = func [n] () => func [n@ref] (y: ref/i32) => y\n    let a = outer()\n    let r = apply(a@ref, k@ref)\n" + MainUnit;

    private const string RawStore = "struct Holder\n    var p: raw/i32\n    public init(p: raw/i32) => self.p = p\nfunc use() -> i32\n    var local: i32 = 1\n" +
        RawInput + "    let a = outer(local@ref)\n    let h = Holder.init(a())\n    local = 2\n    return 0\n" + MainUnit;

    private const string RawInRefStore = "struct Holder {s}\n    var p: ref/(raw/i32 during s) during s\n    public init(p: ref/(raw/i32 during s) during s) => self.p = p\n" +
        "func use() -> i32\n    var local: i32 = 1\n" + RawInput + "    let a = outer(local@ref)\n    let p = a()\n    let h = Holder.init(p@ref)\n    return 0\n" + MainUnit;

    private const string Conflict = nameof(DiagnosticCode.ComparisonLoanConflict_Kd);

    private const string Unsatisfied = nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd);

    private const string Constraint = nameof(DiagnosticCode.UnsatisfiedConstraint_Kd);

    // The chains of the scaling rows (problem-draft §4.3): each shape at each depth D.
    public static TheoryData<string, int> Chains
    {
        get
        {
            var data = new TheoryData<string, int>();
            foreach (var shape in (string[])["s_recv", "s_recv_bad", "s_in", "s_in_bad"])
            {
                foreach (var depth in (int[])[2, 4, 8, 16, 32])
                {
                    data.Add(shape, depth);
                }
            }

            return data;
        }
    }

    // The maker's receiver (problem-draft §4.1; SPEC 7.7.3, 15.8.2 rows 1-3, 15.6.6): ρ and the environment bindings that resolve to it
    // stand for the maker call's receiver acquisition, so the returned Closure's call binds and its result keeps the maker's receiver Loan:
    // a later Move of the maker conflicts at the moved Place while the result lives, whether the Closure is kept in a local, called at once,
    // called inside another Closure (f1h), through a `ref/F` (Reborrowed), through a generic `T`, from a struct Field or an Option payload;
    // n1's second Exclusive call conflicts at its callee (R4-M123 F3: the meet with ρ), as does a second call through a `uniq/F` (x1); a
    // Consuming maker (f1a, f1i: 15.8.2's `take`) keeps the captured reference's Origin; a Consuming maker that is Copy is called through a
    // Copy, its receiver owned (c1). r6_bad is the test author's twin of r6.
    [Theory]
    [InlineData("r1_local", MainBody + ReceiverMaker + ACall + "    let s = r@follow\n", "")]
    [InlineData("r1_local_bad", Inspect + MainBody + ReceiverMaker + ACall + Moved + InspectR, Conflict + " let moved = outer@move")]
    [InlineData("r5_moved_closure_bad", Inspect + MainBody + ReceiverMaker + "    let a = outer()\n" + Moved + "    let r = a()\n" + InspectR, Conflict + " let moved = outer@move")]
    [InlineData("r2_once", MainBody + ReceiverMaker + "    let r = outer()()\n    let s = r@follow\n", "")]
    [InlineData("r2_once_bad", Inspect + MainBody + ReceiverMaker + "    let r = outer()()\n" + Moved + InspectR, Conflict + " let moved = outer@move")]
    [InlineData("r3_wrap", MainBody + ReceiverMaker + Wrap, "")]
    [InlineData("r3_wrap_bad", Inspect + MainBody + ReceiverMaker + Wrap + "    let moved = wrap@move\n" + InspectR, Conflict + " let moved = wrap@move")]
    [InlineData("r4_ret", "func make(p: ref/i32) -> i32\n    let n: i32 = p@follow\n    let outer = func [n] () => func [n@ref] () => n\n    let a = outer()\n    return a()@follow\n" + MainUnit, "")]
    [InlineData("r6_two_env", Inspect + MainBody + TwoEnv + ACall + "    inspect(r.0)\n    inspect(r.1)\n", "")]
    [InlineData("r6_two_env_bad", Inspect + MainBody + TwoEnv + ACall + Moved + "    inspect(r.1)\n", Conflict + " let moved = outer@move")]
    [InlineData("r10_through_ref", Inspect + MainBody + ReceiverMaker + "    let ro = outer@ref\n    let a = ro()\n    let r = a()\n" + InspectR, "")]
    [InlineData("r10_through_ref_bad", Inspect + MainBody + ReceiverMaker + "    let ro = outer@ref\n    let a = ro()\n    let r = a()\n" + Moved + InspectR, Conflict + " let moved = outer@move")]
    [InlineData("g1_generic", Keep + MainBody + ReceiverMaker + "    let a = keep(outer())\n    let r = a()\n    let s = r@follow\n", "")]
    [InlineData("g1_generic_bad", Inspect + Keep + MainBody + ReceiverMaker + "    let a = keep(outer())\n    let r = a()\n" + Moved + InspectR, Conflict + " let moved = outer@move")]
    [InlineData(
        "r7_n1",
        MainBody + N1 + "    let a = outer()\n    let b = outer()\n    let pa = a@move()\n    let pb = b@move()\n    pa@follow = 1\n    pb@follow = 2\n",
        Conflict + " let b = outer()")]
    [InlineData("r7_n1ok", MainBody + N1 + "    let a = outer()\n    let pa = a@move()\n    pa@follow = 1\n", "")]
    [InlineData("r7_n1ok_bad", MainBody + N1 + "    let a = outer()\n    let pa = a@move()\n    let m = outer@move\n    pa@follow = 1\n", Conflict + " let m = outer@move")]
    [InlineData("x1_uniq", MainBody + N1 + "    let uo = outer@uniq\n    let a = uo()\n    let pa = a@move()\n    pa@follow = 1\n", "")]
    [InlineData(
        "x1_uniq_bad",
        MainBody + N1 + "    let uo = outer@uniq\n    let a = uo()\n    let b = uo()\n    let pa = a@move()\n    let pb = b@move()\n    pa@follow = 1\n    pb@follow = 2\n",
        Conflict + " let b = uo()")]
    [InlineData(
        "r8_f1a",
        Texts + "func f(u: uniq/i32) -> uniq/i32 during u\n    let t = makeText()\n" + ConsumingMaker + "    let a = outer@move()\n    return a@move()\n" + MainUnit,
        "")]
    [InlineData("r8_f1i", F1i + "    pa@follow = 2\n", "")]
    [InlineData("r8_f1i_bad", F1i + "    x = 5\n    pa@follow = 2\n", Conflict + " x = 5")]
    [InlineData("c1_copy", Inspect + MainBody + CopyMaker + ACall + Moved + InspectR + "    local = 2\n", "")]
    [InlineData("c1_copy_bad", Inspect + MainBody + CopyMaker + ACall + "    local = 2\n" + InspectR, Conflict + " local = 2")]
    [InlineData("s1_field", FieldHolder + Inspect + MainBody + ReceiverMaker + "    let h = Holder.init(f: outer())\n    let r = h.f()\n" + InspectR, "")]
    [InlineData(
        "s1_field_bad",
        FieldHolder + Inspect + MainBody + ReceiverMaker + "    let h = Holder.init(f: outer())\n    let r = h.f()\n" + Moved + InspectR,
        Conflict + " let moved = outer@move")]
    [InlineData("s2_option", Inspect + MainBody + ReceiverMaker + OptionCall + "            inspect(r)\n        .None => ()\n", "")]
    [InlineData(
        "s2_option_bad",
        Inspect + MainBody + ReceiverMaker + OptionCall + "            let moved = outer@move\n            inspect(r)\n        .None => ()\n",
        Conflict + " let moved = outer@move")]
    [InlineData("r9_inner_in_maker", InnerInMaker, "")]
    public void AReturnedClosuresCallReadsItsMakersReceiverImage(string form, string source, string mir)
        => AssertSides(form, source, mir, null);

    // The maker's per-call input (problem-draft §4.2; SPEC 15.6.4, 15.6.5, 15.8.2 row 2): the input stands for the argument's Origin, so
    // the returned Closure's result keeps the argument's Loan (f1in, f1inok, f1inbad, through a temporary maker literal, beside ρ, through
    // a generic `T`, and under a Consuming maker called through a Copy, c2), a result over a local is Refuted at the returned value against
    // a fixed input (i1_bad, the test author's i5_bad), and the maker's own calls and a result with no Origin are unchanged counterparts.
    [Theory]
    [InlineData("i1_f1in", ReturnH + InputOuter + "    let a = outer(p)\n    return a()\n" + MainUnit, "", null)]
    [InlineData(
        "i1_f1in_bad",
        ReturnH + "    let local: i32 = 1\n" + InputOuter + "    let a = outer(local@ref)\n    return a()\n" + MainUnit,
        Unsatisfied + " return a()",
        Unsatisfied + " a()")]
    [InlineData("i2_f1inok", Inspect + MainBody + InputMaker + "    let a = outer(local@ref)\n    let r = a()\n" + InspectR + "    local = 2\n", "", null)]
    [InlineData("i2_f1inbad", Inspect + MainBody + InputMaker + "    let a = outer(local@ref)\n    let r = a()\n    local = 2\n" + InspectR, Conflict + " local = 2", null)]
    [InlineData("i3_once_ok", Inspect + MainBody + InputMaker + "    let r = outer(local@ref)()\n" + InspectR + "    local = 2\n", "", null)]
    [InlineData("i3_once_bad", Inspect + MainBody + InputMaker + "    let r = outer(local@ref)()\n    local = 2\n" + InspectR, Conflict + " local = 2", null)]
    [InlineData("i4_closure_kept_bad", Inspect + MainBody + InputMaker + "    let a = outer(local@ref)\n    local = 2\n    let r = a()\n" + InspectR, Conflict + " local = 2", null)]
    [InlineData("i5_literal_outer", ReturnH + "    return (func (x: ref/i32) => func [x] () => x)(p)()\n" + MainUnit, "", null)]
    [InlineData(
        "i5_literal_outer_bad",
        ReturnH + "    let local: i32 = 1\n    return (func (x: ref/i32) => func [x] () => x)(local@ref)()\n" + MainUnit,
        Unsatisfied + " return (func (x: ref/i32) => func [x] () => x)(local@ref)()",
        Unsatisfied + " (func (x: ref/i32) => func [x] () => x)(local@ref)()")]
    [InlineData("i6_maker_body", MakerBody, "", null)]
    [InlineData("i7_mid_in_outer", MidInOuter, "", null)]
    [InlineData("i8_mixed", Inspect + MainBody + Mixed + "    inspect(r.0)\n    inspect(r.1)\n    local = 2\n", "", null)]
    [InlineData("i8_mixed_bad", Inspect + MainBody + Mixed + "    local = 2\n    inspect(r.1)\n", Conflict + " local = 2", null)]
    [InlineData("i9_noorigin", NoOrigin + "    return a()\n" + MainUnit, "", null)]
    [InlineData("i9_noorigin_bad", NoOrigin + "    local = 2\n    return a()\n" + MainUnit, Conflict + " local = 2", null)]
    [InlineData("g2_generic_input", Inspect + Keep + MainBody + InputMaker + "    let a = keep(outer(local@ref))\n    let r = a()\n" + InspectR + "    local = 2\n", "", null)]
    [InlineData(
        "g2_generic_input_bad",
        Inspect + Keep + MainBody + InputMaker + "    let a = keep(outer(local@ref))\n    let r = a()\n    local = 2\n" + InspectR,
        Conflict + " local = 2",
        null)]
    [InlineData("c2_copy_input", Inspect + MainBody + CopyInputMaker + InspectR + "    local = 2\n", "", null)]
    [InlineData("c2_copy_input_bad", Inspect + MainBody + CopyInputMaker + "    local = 2\n" + InspectR, Conflict + " local = 2", null)]
    [InlineData(
        "p8_param_ok",
        Use + "    let outer = func (x: i32) => func (y: i32) => func [y] () => y\n    let m = outer(3)\n    let a = m(4)\n    let r = a()\n" + MainUnit,
        "",
        null)]
    public void AReturnedClosuresCallReadsItsMakersInputImage(string form, string source, string mir, string? published)
        => AssertSides(form, source, mir, published);

    // Depth two or more (problem-draft §4.3): a maker returning a maker; the innermost result names the outermost receiver (d2_receiver),
    // the middle one (d2_mid_receiver), the outermost input (d2_input) or two makers' receivers (d3), and each image is its own call's.
    [Theory]
    [InlineData("d2_receiver", Inspect + MainBody + Mk2Receiver + "    let b = mk2()\n    let a = b()\n    let r = a()\n" + InspectR, "")]
    [InlineData("d2_receiver_bad", Inspect + MainBody + Mk2Receiver + "    let r = mk2()()()\n    let moved = mk2@move\n" + InspectR, Conflict + " let moved = mk2@move")]
    [InlineData("d2_mid_receiver", Inspect + MainBody + Mk2Mid + InspectR, "")]
    [InlineData("d2_mid_receiver_bad", Inspect + MainBody + Mk2Mid + "    let moved = b@move\n" + InspectR, Conflict + " let moved = b@move")]
    [InlineData("d2_input", Inspect + MainBody + Mk2Input + InspectR + "    local = 2\n", "")]
    [InlineData("d2_input_bad", Inspect + MainBody + Mk2Input + "    local = 2\n" + InspectR, Conflict + " local = 2")]
    [InlineData("d3_two_makers", Inspect + MainBody + TwoMakers + "    inspect(r.0)\n    inspect(r.1)\n", "")]
    [InlineData("d3_two_makers_bad", Inspect + MainBody + TwoMakers + "    let moved = b@move\n    inspect(r.1)\n", Conflict + " let moved = b@move")]
    [InlineData("d3_two_makers_bad0", Inspect + MainBody + TwoMakers + "    let moved = mk@move\n    inspect(r.0)\n", Conflict + " let moved = mk@move")]
    public void AReturnedClosuresCallReadsEachMakersImageAtAnyDepth(string form, string source, string mir)
        => AssertSides(form, source, mir, null);

    // The scaling rows' verdicts (problem-draft §4.3; A5's work: MirBorrowsTest.AMakerChainsWorkStaysLinearInItsDepth): D + 1 makers
    // called D + 1 times, the innermost result naming the outermost receiver (s_recv) or input (s_in); one record for each invalid twin.
    [Theory]
    [MemberData(nameof(Chains))]
    public void AMakerChainReadsTheOutermostImageAtEachDepth(string shape, int depth)
    {
        var receiver = shape.StartsWith("s_recv", StringComparison.Ordinal);
        var nested = string.Concat(Enumerable.Repeat(receiver ? "func [n] () => " : "func [x] () => ", depth - 1));
        var calls = string.Concat(Enumerable.Repeat("()", depth));
        var chain = Inspect + MainBody + (receiver
            ? $"    let n: i32 = 4\n    let mk = func [n] () => func [n@ref] () => {nested}n\n    let r = mk(){calls}\n"
            : $"    var local: i32 = 1\n    let mk = func (x: ref/i32) => func [x] () => {nested}x\n    let r = mk(local@ref){calls}\n");
        var (tail, mir) = shape switch
        {
            "s_recv" => (InspectR, string.Empty),
            "s_recv_bad" => ("    let moved = mk@move\n" + InspectR, Conflict + " let moved = mk@move"),
            "s_in" => (InspectR + "    local = 2\n", string.Empty),
            _ => ("    local = 2\n" + InspectR, Conflict + " local = 2"),
        };
        AssertSides($"{shape}_d{depth}", chain + tail, mir, null);
    }

    // The returned Closure at a Callable proof or an erasure (problem-draft §4.4; SPEC 8.10, 10.7, 15.4.3 rule 2, 7.7.4): its result keeps
    // its own input or no Origin (valid), while a result over the maker's ρ or input cannot fit `Callable<() -> ref/i32>`, whose omitted
    // result Origin is `static`: a Refuted chain, UnsatisfiedConstraint_Kd at the argument (problem-check K1, replacing the owner's interim
    // 「暫定として認める」 for q2 and q6, whose premise was a valid form; dev: UnprovenConstraint_Kd). An erasure needs an Owned
    // environment (q10).
    [Theory]
    [InlineData(
        "q1_noorigin",
        CallableI32 + Use + "    var local: i32 = 1\n    let outer = func (x: ref/i32) => func [x] () => x@follow\n    let a = outer(local@ref)\n" + ApplyRef + MainUnit,
        "",
        null)]
    [InlineData(
        "q6_follow",
        CallableI32 + Use + "    let n: i32 = 1\n    let outer = func [n] () => func [n@ref] () => n@follow\n    let a = outer()\n" + ApplyRef + MainUnit,
        "",
        null)]
    [InlineData("q6_input_result", InputResult, "", null)]
    [InlineData("q4_erased", Apply + Use + "    let outer = func (x: i32) => func [x] () => x\n    let a = outer(3)\n    let r = apply(a)\n" + MainUnit, "", null)]
    [InlineData(
        "q10_erased_receiver",
        Apply + Use + "    let n: i32 = 1\n    let outer = func [n] () => func [n@ref] () => n@follow\n    let a = outer()\n    let r = apply(a)\n" + MainUnit,
        Constraint + " let r = apply(a)",
        Constraint + " a")]
    [InlineData(
        "q2_nested",
        CallableRef + Use + "    let outer = func (x: i32) => func [x] () => func [x@ref] () => x\n    let m = outer(3)\n    let a = m()\n" + ApplyRef + MainUnit,
        Constraint + " let r = apply(a@ref)",
        Constraint + " a@ref")]
    [InlineData("q3_input", CallableRef + Use + InputMaker + "    let a = outer(local@ref)\n" + ApplyRef + MainUnit, Constraint + " let r = apply(a@ref)", Constraint + " a@ref")]
    [InlineData(
        "q6_receiver",
        CallableRef + Use + "    let n: i32 = 1\n    let outer = func [n] () => func [n@ref] () => n\n    let a = outer()\n" + ApplyRef + MainUnit,
        Constraint + " let r = apply(a@ref)",
        Constraint + " a@ref")]
    [InlineData(
        "q9_called",
        "func apply<F>(f: ref/F) -> i32\n    F is Callable<() -> ref/i32>\n    _ = f()\n    return 0\n" + Use + InputMaker + "    let a = outer(local@ref)\n" + ApplyRef + MainUnit,
        Constraint + " let r = apply(a@ref)",
        Constraint + " a@ref")]
    public void AReturnedClosureKeepsItsImagesAtACallableProofOrAnErasure(string form, string source, string mir, string? published)
        => AssertSides(form, source, mir, published);

    // Raw-layer positions (problem-draft §4.5; SPEC 5.7, 15.6.5): an Origin a raw layer obtains by substituting the maker's input or
    // receiver belongs to what that Origin comes from, so returning or storing it fits a written Origin it outlives (a17, a18_ok) and is
    // Refuted where it is a local's borrow (a17_bad, a17_static_bad, a18_bad, a20); with no Origin stated every Origin fits (a16, a18, a19,
    // a21). Through a raw layer whose Origin is the maker's input a call stores what that Origin's borrow admits, and the storage retains as
    // the borrow would (SPEC 5.7 Calls): w1's `t` keeps lr's Loan while it is used (problem-check, rows to add: the soundness row).
    [Theory]
    [InlineData("a16_raw_input", "func use() -> i32\n    var local: i32 = 1\n" + RawInput + "    let a = outer(local@ref)\n    let p = a()\n    return 0\n" + MainUnit, "", null)]
    [InlineData("a17_raw_input_ret", RawReturnH + RawInput + "    let a = outer(q)\n    return a()\n" + MainUnit, "", null)]
    [InlineData(
        "a17_bad",
        RawReturnH + "    let local: i32 = 1\n" + RawInput + "    let a = outer(local@ref)\n    return a()\n" + MainUnit,
        Unsatisfied + " return a()",
        Unsatisfied + " a()")]
    [InlineData(
        "a17_static_bad",
        "func h() -> raw/i32 during static\n    let local: i32 = 1\n" + RawInput + "    let a = outer(local@ref)\n    return a()\n" + MainUnit,
        Unsatisfied + " return a()",
        Unsatisfied + " a()")]
    [InlineData("a18_raw_input_store", RawStore, "", null)]
    [InlineData("a18_ok", RawOut + RawInput + "    let a = outer(q)\n    out@follow = a()\n" + MainUnit, "", null)]
    [InlineData(
        "a18_bad",
        RawOut + "    let local: i32 = 1\n" + RawInput + "    let a = outer(local@ref)\n    out@follow = a()\n" + MainUnit,
        Unsatisfied + " out@follow = a()",
        Unsatisfied + " a()")]
    [InlineData("a19_raw_receiver", "func use() -> i32\n" + RawReceiver + "    let a = outer()\n    let p = a()\n    return 0\n" + MainUnit, "", null)]
    [InlineData("a20_raw_receiver_ret_bad", RawReturnH + RawReceiver + "    let a = outer()\n    return a()\n" + MainUnit, Unsatisfied + " return a()", Unsatisfied + " a()")]
    [InlineData("a21_raw_in_ref_store", RawInRefStore, "", null)]
    [InlineData("w1_raw_retain", RawRetain + "    usep(t)\n    local = 2\n", "", null)]
    [InlineData("w1_raw_retain_bad", RawRetain + "    local = 2\n    usep(t)\n", Conflict + " local = 2", null)]
    public void AReturnedClosureKeepsItsImagesInARawLayer(string form, string source, string mir, string? published)
        => AssertSides(form, source, mir, published);

    // A program's two sides (MirDifferenceLedger.Sides) after Binding and the old analysis, each program parse-clean. The MIR side,
    // Binding's records and the MIR's, is exactly `mir`: each Error record as its code and the trimmed source line its subject starts on,
    // in source order. The default side, Binding's records and the old analysis's (published on both pipelines until R6-check, DECISIONS
    // R5-M156), rejects an invalid form (D1) and gives a valid one no record but a located Unsupported_Kd (D2). A verdict Binding owns, an
    // Origin relation at a returned or stored value or a Callable or erasure constraint, holds on both sides: the published records are
    // exactly `published`, code and span.
    private static void AssertSides(string form, string source, string mir, string? published)
    {
        var c = VerifiedSpecExampleTest.Bind(source);
        c.Analyze();
        var (common, old, passed, records) = MirDifferenceLedger.Sides(c, c.BuildMir());
        Assert.True(mir == Lines(source, [.. common, .. records]), $"{form}: MIR side\n  expected: {mir}\n  observed: {Lines(source, [.. common, .. records])}");
        var defaults = Errors([.. common, .. old, .. passed]);
        Assert.True(
            mir.Length == 0 ? defaults.All(static x => x.Code == DiagnosticCode.Unsupported_Kd && x.Node.CodeContext?.SourceDocument?.Path == Document) : defaults.Length > 0,
            $"{form}: default side {(mir.Length == 0 ? "D2" : "D1")}: {Lines(source, defaults)}");
        if (published is not null)
        {
            Assert.True(published == Records(source), $"{form}: published\n  expected: {published}\n  observed: {Records(source)}");
        }
    }

    // A side's Error records as their codes and the trimmed source lines their subjects start on (a record outside the program as its
    // location), in source order.
    private static string Lines(string source, IEnumerable<MirDifferenceLedger.Entry> side)
        => string.Join("; ", Errors(side).OrderBy(static x => x.Node.Span.Start).Select(x => x.Node.CodeContext?.SourceDocument?.Path == Document
            ? $"{x.Code} {Line(source, x.Node.Span.Start)}" : $"{x.Code} {LegacyRecords.Location(x.Node)}"));

    private static MirDifferenceLedger.Entry[] Errors(IEnumerable<MirDifferenceLedger.Entry> side)
        => [.. side.Where(static x => DiagnosticEntries.TryGet(x.Code, out var entry) && entry.Severity == DiagnosticSeverity.Error)];

    // The trimmed text of the source line holding an offset.
    private static string Line(string source, int offset)
    {
        var start = offset == 0 ? 0 : source.LastIndexOf('\n', offset - 1) + 1;
        var end = source.IndexOf('\n', offset);
        return source[start..(end < 0 ? source.Length : end)].Trim();
    }
}
