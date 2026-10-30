// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

// PLAN G273's rows from implementation review 1 (R4's review-1.md B1, B2; BU's impl-review.md 1): a maker-returned Closure's Type carries
// its makers' images as Origin arguments, and those arguments relate as SPEC 15.3.5 derives them, never more strictly. Each row states
// the MIR side exactly and the default side D1 or D2 (AssertSides).
public partial class ClosureResultOriginTest
{
    private const string Unproven = nameof(DiagnosticCode.UnprovenOriginRelation_Kd);

    private const string NestedCell = Inspect + "struct Outer<T> {a}\n    public struct Cell\n        let value: ref/T during a\n" +
        "        public init(value: ref/T during a) => self.value = value\nfunc f(value: ref/i32 during x)\n" +
        "    let cell = (Outer<i32>{o}).Cell.init(value)\n        origin o.a == x\n    var other: i32 = 2\n    let r = other@ref\n";

    private const string CellUnsupported = "Unsupported_Kd let cell = (Outer<i32>{o}).Cell.init(value); Unsupported_Kd (Outer<i32>{o}).Cell.init(value)";

    private const string HpQ = "func h(p: ref/i32, q: ref/i32) -> ref/i32 during q\n";

    private const string HpQi32 = "func h(p: ref/i32, q: ref/i32) -> i32\n";

    private const string PoutlivesQ = "    origin p outlives q\n";

    private const string Pick = "func pick<T>(a: T, b: T) -> T => a@move\n";

    private const string HqS = "func h(q: ref/i32, s: ref/i32 during static) -> ref/i32 during ";

    private const string QthenS = InputOuter + "    var c = outer(q)\n    c = outer(s)\n    return c()\n" + MainUnit;

    private const string QthenP = InputOuter + "    var c = outer(q)\n    c = outer(p)\n    return c()\n" + MainUnit;

    private const string PthenQ = InputOuter + "    var c = outer(p)\n    c = outer(q)\n";

    private const string PickQP = InputOuter + "    let c = pick(outer(q), outer(p))\n    return c()\n" + MainUnit;

    private const string Local = "    let local: i32 = 1\n" + InputOuter + "    var c = outer(p)\n    c = outer(local@ref)\n";

    private const string StoreA = InputOuter + "    let a = outer(p)\n    var c = outer(q)\n    c = a\n";

    private const string Literal = InputOuter + "    var c = outer(p)\n    c = outer(42@ref)\n";

    private const string Two = Inspect + MainBody + "    var p: i32 = 1\n    var q: i32 = 2\n" + InputOuter + "    let arr = [outer(p@ref), outer(q@ref)]\n";

    private const string V13 = Inspect + MainBody + "    var local: i32 = 1\n    var other: i32 = 2\n    let lr = local@ref\n";

    private const string InputInner = "    let outer = func (x: ref/i32) => func (v: ref/i32 during x) -> ref/i32 during x => v\n";

    private const string V13Maker = V13 + InputInner + "    let a = outer(lr)\n";

    private const string V13Flat = V13 + "    let a = func (v: ref/i32 during lr) -> ref/i32 during lr => v\n";

    private const string ParamArg = HpQi32 + PoutlivesQ + "    let outer = func (x: ref/i32) => func (y: ref/i32 during x) -> i32 => 0\n";

    private const string PhantomTag = "struct Tag {a}\n    public init(v: ref/i32 during a) => ()\n";

    private const string BothPositions = "    let outer = func (x: ref/i32) => func (y: ref/i32 during x) -> ref/i32 during x => y\n";

    private const string Kept = Inspect + "func keepIt<T>(t: T) => ()\nfunc look<T>(t: ref/T) => ()\n" + MainBody +
        "    var local: i32 = 1\n    let lr = local@ref\n";

    private const string KeptOuter = "    let a = outer(lr)\n    local = 5\n";

    private const string KeptFlat = "    local = 5\n";

    private const string Move = "    keepIt(a@move)\n";

    private const string Look = "    look(a@ref)\n";

    // SPEC 15.3.5, 15.3.6, 15.6.1 (R4 B1, BU 1): a Closure Type's Origin argument takes the variance of its occurrences in the definition's
    // signature, so an argument that only the inner environment and result name is covariant. A value of one image fits a holder of
    // another where the first outlives it: reassigned through a `var` under a premise (v50, x3) or from `static` (v51), joined by a
    // generic `pick` (v53), never called (y1, y4 with a local, y5 from a `let`, y7 with a temporary's borrow), and as two Array elements
    // (v6); each valid form gets no Language record on the MIR side (dev's F1 Unsupported_Kd included) and D2 on the default. The invalid twins
    // are the review's v50_nopremise_bad and the test author's one-change twins: the premise removed (x3, also y1 returning its value
    // `during q`; v53), the result `during static` (v51), the value returned `during` a parameter its other image does not outlive (y4: a
    // local's borrow, Refuted; y5: Unproven; y7: a temporary's borrow, Refuted, whose storage ends while `c` lives), and v6's first
    // element's Place written while the Array lives (SPEC 15.6: the Array carries both captures' Loans). Each twin's records are its flat
    // twin's on dev, with plain references in place of the Closures.
    [Theory]
    [InlineData("v50_premise_ok", HpQ + PoutlivesQ + QthenP, "")]
    [InlineData("v50_nopremise_bad", HpQ + QthenP, Unproven + " return c()")]
    [InlineData("v51_static_ok", HqS + "q\n" + QthenS, "")]
    [InlineData("v51_static_bad", HqS + "static\n" + QthenS, Unproven + " return c()")]
    [InlineData("v53_pick_premise_ok", Pick + HpQ + PoutlivesQ + PickQP, "")]
    [InlineData("v53_pick_nopremise_bad", Pick + HpQ + PickQP, Unproven + " return c()")]
    [InlineData("x3_two_params_ok", HpQ + PoutlivesQ + PthenQ + "    return c()\n" + MainUnit, "")]
    [InlineData("x3_nopremise_bad", HpQ + PthenQ + "    return c()\n" + MainUnit, Unproven + " return c()")]
    [InlineData("y1_params_nocall", HpQi32 + PthenQ + "    return 0\n" + MainUnit, "")]
    [InlineData("y4_param_and_local", "func h(p: ref/i32) -> i32\n" + Local + "    return 0\n" + MainUnit, "")]
    [InlineData("y4_ret_bad", "func h(p: ref/i32) -> ref/i32 during p\n" + Local + "    return c()\n" + MainUnit, Unsatisfied + " return c()")]
    [InlineData("y5_store_into_param_var", HpQi32 + StoreA + "    return 0\n" + MainUnit, "")]
    [InlineData("y5_ret_bad", HpQ + StoreA + "    return c()\n" + MainUnit, Unproven + " return c()")]
    [InlineData("y7_static_and_param", "func h(p: ref/i32) -> i32\n" + Literal + "    return 0\n" + MainUnit, "")]
    [InlineData(
        "y7_ret_bad",
        "func h(p: ref/i32) -> ref/i32 during p\n" + Literal + "    return c()\n" + MainUnit,
        Conflict + " c = outer(42@ref); " + Unsatisfied + " return c()")]
    [InlineData("v6_array", Two, "")]
    [InlineData("v6_array_bad", Two + "    p = 3\n    inspect(arr[1]())\n", Conflict + " p = 3")]
    public void AClosuresOriginArgumentTakesTheVarianceOfItsOccurrences(string form, string source, string mir)
        => AssertSides(form, source, mir, null);

    // SPEC 15.3.5 "Function parameters reverse polarity" (BU's re-review 1, finding 6): an argument that only the inner parameter names is
    // contravariant, so a holder of a longer image takes a value of a shorter one (c1, reassigned; c3, created) and the call may pass what
    // outlives the image, but not what the image outlives (c2, Unproven at the call). An argument named by both the parameter and the result
    // is invariant (c4): `var c` holds images `p` and `q`, which must be equal, and `origin p outlives q` does not prove it. Dev accepted c4
    // on both sides because its Type carried no Origin argument; UnprovenOriginRelation_Kd is SPEC 15.3.5's verdict for the invariant
    // argument, a new rejection of a form dev accepted, at the line of its flat twin's record on dev (a Phantom slot, invariant).
    [Theory]
    [InlineData("c1_param_arg_reassign_ok", ParamArg + "    var c = outer(q)\n    c = outer(p)\n    return c(p)\n" + MainUnit, "")]
    [InlineData("c2_param_arg_call_short_bad", ParamArg + "    let c = outer(p)\n    return c(q)\n" + MainUnit, Unproven + " return c(q)")]
    [InlineData("c3_param_arg_call_long_ok", ParamArg + "    let c = outer(q)\n    return c(p)\n" + MainUnit, "")]
    [InlineData(
        "c4_both_positions_reassign",
        HpQi32 + PoutlivesQ + BothPositions + "    var c = outer(p)\n    c = outer(q)\n    return 0\n" + MainUnit,
        Unproven + " var c = outer(p)")]
    [InlineData(
        "c4_phantom_ctl_bad",
        PhantomTag + HpQi32 + PoutlivesQ + "    var c = Tag.init(p)\n    c = Tag.init(q)\n    return 0\n" + MainUnit,
        Unproven + " var c = Tag.init(p)")]
    public void AClosuresOriginArgumentInAParameterReversesPolarity(string form, string source, string mir)
        => AssertSides(form, source, mir, null);

    // SPEC 15.6, 15.6.4 (R4 B2): a Closure value whose Type's Origin argument a maker's input images, named only in the inner signature,
    // holds no reference and carries no Loan, so writing the input's Place while it lives is valid (v13_param_kept_ok, D2 on the default,
    // as its flat twin), and so is the write while the call's result lives (v13_param_bad_local, the review's name: the result retains only
    // its argument `other@ref`, as dev accepts the flat twin). The invalid twin passes the input itself, which the result then carries.
    // R4's review 2 (R2-B1): the same holds for a value that is never called, moved (v60) or borrowed (v63) after the write, and for one
    // whose result names no Origin (v64, `x` named by the parameter only). Their invalid twins capture the input, so the value holds lr's
    // reference and carries its Loan (SPEC 15.6.4): the review's v60_kept_env_bad, and `[x]` added to v63's and v64's inner literal.
    [Theory]
    [InlineData("v60_kept_move_ok", Kept + InputInner + KeptOuter + Move, "")]
    [InlineData("v60_kept_move_ctl_ok", Kept + "    let a = func (v: ref/i32 during lr) -> ref/i32 during lr => v\n" + KeptFlat + Move, "")]
    [InlineData("v60_kept_env_bad", Kept + InputOuter + KeptOuter + Move, Conflict + " local = 5")]
    [InlineData("v60_kept_env_ctl_bad", Kept + "    let a = func [lr] () => lr\n" + KeptFlat + Move, Conflict + " local = 5")]
    [InlineData("v63_kept_look_ok", Kept + InputInner + KeptOuter + Look, "")]
    [InlineData("v63_kept_look_ctl_ok", Kept + "    let a = func (v: ref/i32 during lr) -> ref/i32 during lr => v\n" + KeptFlat + Look, "")]
    [InlineData(
        "v63_kept_env_look_bad",
        Kept + "    let outer = func (x: ref/i32) => func [x] (v: ref/i32 during x) -> ref/i32 during x => v\n" + KeptOuter + Look,
        Conflict + " local = 5")]
    [InlineData(
        "v63_kept_env_look_ctl_bad",
        Kept + "    let a = func [lr] (v: ref/i32 during lr) -> ref/i32 during lr => v\n" + KeptFlat + Look,
        Conflict + " local = 5")]
    [InlineData("v64_kept_param_only_ok", Kept + "    let outer = func (x: ref/i32) => func (v: ref/i32 during x) -> i32 => v@follow\n" + KeptOuter + Look, "")]
    [InlineData("v64_kept_param_only_ctl_ok", Kept + "    let a = func (v: ref/i32 during lr) -> i32 => v@follow\n" + KeptFlat + Look, "")]
    [InlineData(
        "v64_kept_param_env_bad",
        Kept + "    let outer = func (x: ref/i32) => func [x] (v: ref/i32 during x) -> i32 => v@follow\n" + KeptOuter + Look,
        Conflict + " local = 5")]
    [InlineData(
        "v64_kept_param_env_ctl_bad",
        Kept + "    let a = func [lr] (v: ref/i32 during lr) -> i32 => v@follow\n" + KeptFlat + Look,
        Conflict + " local = 5")]
    [InlineData("v13_param_kept_ok", V13Maker + "    local = 5\n    let r = a(other@ref)\n    inspect(r)\n", "")]
    [InlineData("v13_param_kept_ctl_ok", V13Flat + "    local = 5\n    let r = a(other@ref)\n    inspect(r)\n", "")]
    [InlineData("v13_param_bad_local", V13Maker + "    let r = a(other@ref)\n    local = 5\n    inspect(r)\n", "")]
    [InlineData("v13_param_ctl_bad_local", V13Flat + "    let r = a(other@ref)\n    local = 5\n    inspect(r)\n", "")]
    [InlineData("v13_param_lr_bad", V13Maker + "    let r = a(lr)\n    local = 5\n    inspect(r)\n", Conflict + " local = 5")]
    [InlineData("v13_param_lr_ctl_bad", V13Flat + "    let r = a(lr)\n    local = 5\n    inspect(r)\n", Conflict + " local = 5")]
    public void AKeptClosureWhoseSignatureNamesItsMakersInputCarriesNoLoan(string form, string source, string mir)
        => AssertSides(form, source, mir, null);

    // R4's review 3 (R3-B1; SPEC 6.1.3, 23.1.2): a nested struct's reference carries its enclosing declaration's Origin, which its symbol
    // declares, so the old analysis's stop (Origin arguments a symbol does not declare, DECISIONS R4-M159) does not apply to it: dev's
    // published records stay, SupportsType's located Unsupported_Kd (an old-path limit) and the invalid form's independent Loan conflict.
    [Theory]
    [InlineData("n01_inherited_ok", NestedCell + "    inspect(r)\n    other = 3\n" + MainUnit, "", CellUnsupported)]
    [InlineData(
        "n01_inherited_bad", NestedCell + "    other = 3\n    inspect(r)\n" + MainUnit, Conflict + " other = 3", CellUnsupported + "; " + Conflict + " other = 3")]
    public void ANestedStructsInheritedOriginIsOneItsSymbolDeclares(string form, string source, string mir, string published)
        => AssertSides(form, source, mir, published);
}
