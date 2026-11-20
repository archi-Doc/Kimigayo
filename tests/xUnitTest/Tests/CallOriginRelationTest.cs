// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.6.1, 10.1, 10.3, 10.4: candidate applicability, expected-result filtering and ranking use only the structural part of each
// fit, so the Origin relations of a call never change its selection. The selected candidate's relations are judged after selection at
// their own sources: each argument's fit at the argument, a substituted clause at the first input that names its longer Origin, and
// the result at its destination. A chain between body Origins stays the located Unsupported_Kd limit at the argument.
public partial class CallOriginRelationTest(ITestOutputHelper output)
{
    private const string Main = "public func main() => ()\n";

    private const string NeedsStatic = "func needsStatic(x: ref/i32 during static) -> i32 => x@follow\n";

    private const string Refuted = "Return or store an owned value, or a borrow of an input, instead of a borrow of storage that ends with the body";

    private const string Box = "public struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\n";

    // q35: the clause is related with the role `relation`, and its source is `declared`.
    private const string Clause = "func needs(a: ref/i32) -> i32\n    origin a outlives static\n    return 1\n\nfunc caller(p: ref/i32) -> i32 => needs(p)\n";

    // q19: the fresh Origin `o` is fixed to x by the invariant first argument, so the second one does not fit.
    private const string Fresh = "func pair(a: uniq/(ref/i32 during o), b: uniq/(ref/i32 during o)) -> () => ()\n\nfunc caller(x: ref/i32, y: ref/i32, p: uniq/(ref/i32 during x), q: uniq/(ref/i32 during y)) -> ()\n    pair(p, q)\n";

    // G277 (1)'s callees: a defaulted `w` whose Type is well formed only when `b outlives a`, as a function, a generic one, a method, a
    // constructor, one whose `a` only the default and the result name, and one whose `a` an input also names.
    private const string Defaulted = "w: Option<ref/(ref/i32 during b) during a> = .None";

    private const string Put2 = "func put2(slot: uniq/(ref/i32 during a), v: ref/i32 during b, " + Defaulted + ")\n    slot@follow = v\n";

    private const string GenericPut = "func g<T>(slot: uniq/(ref/i32 during a), x: T, w: Option<ref/T during a> = .None) => ()\n";

    private const string MethodPut = "struct S\n    public init() => ()\n    public func put(self: ref/S, slot: uniq/(ref/i32 during a), v: ref/i32 during b, " +
        Defaulted + ")\n        slot@follow = v\n";

    private const string ConstructorPut = "struct H\n    public init(slot: uniq/(ref/i32 during a), v: ref/i32 during b, " + Defaulted + ")\n        slot@follow = v\n";

    private const string Identity = "func id(x: ref/i32) -> ref/i32 during x => x\n";

    private const string ResultOnly = "func z2(v: ref/i32 during b, " + Defaulted + ") -> ref/i32 during a\n    return v\n";

    private const string SharedInput = "func z5(x: ref/i32 during a, v: ref/i32 during b, " + Defaulted + ") -> ref/i32 during a\n    return v\n";

    // G458's headers: a generic and a plain callable whose own body uses it, `z` passed for `y`, so the callee's k is the caller's j.
    private const string OwnBodyCall = "func f<T>(t: T, y: ref/i32 during k, z: ref/i32 during j) -> (T, ref/i32 during k)\n";

    private const string OwnBodyValue = "func h(y: ref/i32 during k, z: ref/i32 during j) -> ref/i32 during k\n";

    // A getter whose own body reads its Property.
    private const string OwnBodyGetter = "struct S\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public computed view: ref/i32 during self\n" +
        "        get(self: ref/Self) -> ref/i32 during self\n";

    // PLAN G457: a method and a constructor whose own bodies call them with a Type argument naming their own k.
    private const string OwnBodyMethod = "struct Box<T>\n    public let v: i32\n    public init(v: i32) => self.v = v\n" +
        "    public func m<U>(self: ref/Self, u: U, y: ref/i32 during k, z: ref/i32 during j) -> (U, ref/i32 during k)\n";

    private const string OwnBodyConstructor = "struct Box<T>\n    public let v: T\n    public init(v: T, y: ref/i32 during k, z: ref/i32 during j)\n        self.v = v@move\n";

    // An elided input `y` named by the Type argument (its per-call image), and a projection normalized after the Origins are mapped.
    private const string OwnBodyInput = "func h<T>(t: T, y: ref/i32, z: ref/i32 during j) -> (T, ref/i32 during j)\n";

    private const string OwnBodyProjection = "contract Source\n    associate Item\nstruct Cursor {at}\n    Self is Source\n    associate Item is ref/i32 during at\n" +
        "    public let item: ref/i32 during at\n    public init(item: ref/i32 during at) => self.item = item\n" +
        "func f<T>(t: T, i: T.(Source).Item, y: ref/i32 during k, z: ref/i32 during j) -> (T.(Source).Item, ref/i32 during k)\n    T is Source\n" +
        "    let c: Cursor during k = Cursor.init(y)\n";

    // A default copying an input's referent, stored by the callee (G277 (1)'s k1).
    private const string Copying = "func put3(slot: uniq/(ref/i32 during a), v: ref/(ref/i32 during b) during c, w: ref/i32 during b = v@follow)\n" +
        "    origin b outlives a\n    slot@follow = w\n";

    // PLAN G1115's callees: a slot behind a borrow layer at both inputs, and an invariant binding beside a covariant one.
    private const string Pick = "func pick<T>(x: ref/T, y: ref/T) -> T => $abort(\"no\")\n";

    private const string PickTwo = Pick + "func two(x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> ";

    private const string PickMethod = "struct S\n    public init() => ()\n    public func pickm<U>(self: ref/Self, x: ref/U, y: ref/U) -> U => $abort(\"no\")\n";

    private const string Inv = "func inv<T>(x: uniq/T, y: ref/T) -> () => ()\nfunc h(x: uniq/(ref/i32 during a), y: ref/(ref/i32 during b)) -> ()\n";

    // PLAN G1115 (review B1): an argument whose Type stores an Origin only through an identity premise (`T is uniq/Meter during a`).
    private const string OneOf = "func iu<S>(o: uniq/One<S>, key: ref/isize) -> uniq/S during o => $abort(\"no\")\n" +
        "func ir<S>(o: ref/One<S>, key: ref/isize) -> ref/S during o => $abort(\"no\")\n" +
        "struct Meter\n    public var n: i32 = 0\n    public init() => ()\n    public func bump(self: uniq/Self) => self.n += 1\n" +
        "struct One<T>\n    Self is UniqIndexable<isize>\n    associate Element is T\n    var first: T\n    public init(first: T) => self.first = first@move\n" +
        "    public func index(self, key: ref/isize) -> ref/T during self => self.first@ref\n" +
        "    public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self => self.first@uniq\n" +
        "func g(m: uniq/Meter) => m.bump()\nfunc f<T>(one: uniq/One<T>, w: ref/i32 during a) -> ";

    [Theory]
    [InlineData(NeedsStatic + "func caller(p: ref/i32) -> i32 => needsStatic(p)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven", null)]
    [InlineData(NeedsStatic + "func caller() -> i32\n    let local = 3\n    return needsStatic(local)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local", "requires the borrow local outlives static, which is false", "local")]
    [InlineData(NeedsStatic + "func caller() -> i32\n    let local = 3\n    return needsStatic(local@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives static, which is false", "local@ref")]
    [InlineData(NeedsStatic + "func caller() -> i32 => needsStatic(5)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "5", "requires the borrow 5 outlives static, which is false", "5")]
    [InlineData(NeedsStatic + "func make() -> i32 => 4\nfunc caller() -> i32 => needsStatic(make())\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "make()", "requires the borrow make() outlives static, which is false", "make()")]
    [InlineData("func takePair(x: (ref/i32 during static, i32)) -> i32 => 1\nfunc caller(p: ref/i32) -> i32 => takePair((p, 1))\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "(p, 1)", "requires p outlives static, which is not proven", null)]
    [InlineData("func store(x: ref/i32, target: uniq/Array<ref/i32 during x>) -> ()\n    let local: i32 = 1\n    target@follow.append(local@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives x, which is false", "local@ref")]
    public void AnArgumentRelationIsJudgedAfterSelection(string body, string code, string text, string label, string? origin)
    {
        var source = body + Main;
        var check = DiagnosticCorpus.Check(source);
        var error = Assert.Single(check.Diagnostics);
        Assert.Equal((code, text, label), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? DiagnosticCategory.Language : DiagnosticCategory.Proof, error.Category);
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal(("outlives", "fit"), (reason[0].Value, reason[3].Value));
        if (origin is null)
        {
            Assert.Null(error.Related);
        }
        else
        {
            // A5's record relates the borrow that created the Loan as `borrow` beside its `origin` (R6-check H5; H3b T49, as A5 publishes
            // them until A6).
            Assert.Equal([("borrow", origin), ("origin", origin)], error.Related!.Select(x => (x.Role, Text(source, x.Span))));
        }
    }

    // SPEC 8.15.2, 15.6.5 (PLAN G809): a call's solve reads each bound's Semantics cases: `y`'s slot bounds put's `q` only in the `ref`
    // case, where the caller's `x` proves `p outlives q`, so put's `q` is the caller's `q`, valid in each case (rejected before G809);
    // without `x` the relation stays unproven.
    [Theory]
    [InlineData("x: t/(ref/i32 during p) during q, ", "clean")]
    [InlineData("", "1*UnprovenOriginRelation_Kd")]
    public void ACallsBoundOfABorrowCaseIsMetByAPremiseOfThatCase(string premise, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "func put<t/U>(q: ref/i32, y: t/U during q, r: ref/i32 during q) -> ref/i32 during q\n    t is owner or ref\n    return r\n" +
            $"func call<t/U>(q: ref/i32, p: ref/i32, {premise}y: t/U during p, r: ref/i32 during q) -> ref/i32 during q\n    t is owner or ref\n    return put(q, y@move, r)\n"));

    // SPEC 8.15.2 (PLAN G809's limit, the open issue "a call in a generic body is solved once for all Semantics cases"): valid in each
    // Semantics case, as put's `a` there is the meet of the bounds that case has. The call's one solve for every case leaves `l1` in its
    // candidate where `s` alone is a borrow, and `w` does not hold there: rejected, sound but incomplete. This row pins today's record
    // (dev's too).
    [Fact]
    public void ACallInAGenericBodyIsSolvedOnceForEveryCase()
        => Assert.Equal("1*UnprovenOriginRelation_Kd", DiagnosticCorpus.RecordProfile(
            "func put<t/U, s/W>(a: ref/i32, b: t/U during a, c: s/W during a) -> s/W during a\n    t is owner or ref\n    s is owner or ref\n    return c@move\n" +
            "func call<t/U, s/W>(u: ref/i32, l1: ref/i32, l2: ref/i32, f: ref/i32, w: t/(ref/i32 during l1) during l2, x2: s/(ref/i32 during l2) during f, " +
            "y2: ref/(ref/i32 during u) during f, a: ref/i32 during u, b: t/U during l1, c: s/W during l2) -> s/W during f\n" +
            "    t is owner or ref\n    s is owner or ref\n    return put(a, b@move, c@move)\n"));

    // SPEC 15.6.1: every failed chain is reported, each at the argument that does not fit.
    [Fact]
    public void EveryFailingArgumentIsItsOwnRecord()
    {
        var source = "func both(x: ref/i32 during static, y: ref/i32 during static) -> i32 => 1\nfunc caller(p: ref/i32, q: ref/i32) -> i32 => both(p, q)\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([("p", "requires p outlives static, which is not proven"), ("q", "requires q outlives static, which is not proven")], errors.Select(x => (Text(source, x.Span), x.Label)));
    }

    // SPEC 15.6.4 step 3, 15.3.6, 10.8 (PLAN G1115): an input's well-formedness over a slot's solution bounds the per-call Origin of the
    // layer above it, so `pick(x, y)` with `T = ref/i32 during (a and b)` (the covariant meet) or an invariant binding beside a longer
    // covariant one is valid, and an invalid twin keeps only its own failure, never a well-formedness record against the argument's own
    // outer Origin (`b outlives x`). Every premise bounds, whatever the argument's own Type keeps: `iu(one, 0)` under `T is uniq/Meter
    // during a` takes `a` from OwnedOrigins(One<T>) into o's per-call Origin, `(one and a)`, as its receiver twin `one.indexUniq(0)` does.
    [Theory]
    [InlineData(PickTwo + "i32\n    let r = pick(x, y)\n    return 0\n", "clean")]
    [InlineData(PickTwo + "i32\n    let r = pick(y, x)\n    return 0\n", "clean")]
    [InlineData(PickTwo + "ref/i32 during (a and b)\n    return pick(x, y)\n", "clean")]
    [InlineData(PickTwo + "ref/i32 during a\n    return pick(x, y)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData(PickTwo + "ref/i32 during a\n    return pick(y, x)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("func pick1<T>(x: ref/T, y: T) -> T => $abort(\"no\")\nfunc two(x: ref/(ref/i32 during a), y: ref/i32 during b) -> i32\n    let r = pick1(x, y)\n    return 0\n", "clean")]
    [InlineData(Pick + "func two(o: Option<ref/i32 during a>, p: ref/(Option<ref/i32 during b>)) -> i32\n    let r = pick(o, p)\n    return 0\n", "clean")]
    [InlineData(Pick + "func two(c: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    let r = pick(if c => x else => x, y)\n    return 0\n", "clean")]
    [InlineData(PickMethod + "func two(s: ref/S, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    let r = s.pickm(x, y)\n    return 0\n", "clean")]
    [InlineData(Inv + "    origin b outlives a\n    inv(x, y)\n", "clean")]
    [InlineData(Inv + "    inv(x, y)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("func inv2<T>(y: ref/T, x: uniq/T) -> () => ()\nfunc h(x: uniq/(ref/i32 during a), y: ref/(ref/i32 during b)) -> ()\n    inv2(y, x)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("func inv3<T>(x: uniq/T, z: uniq/T) -> () => ()\nfunc h(x: uniq/(ref/i32 during a), z: uniq/(ref/i32 during b)) -> ()\n    inv3(x, z)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData(OneOf + "i32\n    T is uniq/Meter during a\n    let e = iu(one, 0)\n    return 0\n", "clean")]
    [InlineData(OneOf + "i32\n    T is uniq/Meter during a\n    let e = ir(one, 0)\n    return 0\n", "clean")]
    [InlineData(OneOf + "i32\n    T is uniq/Meter during a\n    g(iu(one, 0)@follow)\n    return 0\n", "clean")]
    [InlineData(OneOf + "ref/T during w\n    T is uniq/Meter during a\n    return ir(one, 0)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData(OneOf + "uniq/T during w\n    T is uniq/Meter during a\n    return iu(one, 0)\n", "1*UnprovenOriginRelation_Kd")]
    // R7-C3 (F's C2 ruling 307 (2); SPEC 8.2.3, 15.6.1): Type identity decides the structure, the Origin relation the Origins. Under the
    // identity premise the result's Type is T's, so an Origin difference stays the relation's record (`during a`: `(one and a)` needs
    // `one outlives a`), it holds under `origin a outlives one`, and only a structure that differs is a Type mismatch. R7-O1 H: the
    // mismatch's outer layer is a position of its own, `(one and a)` into `one`, which A5 judges at the value (SPEC 23.1.2: its own
    // failure; a record added on a rejected program, DECISIONS Q13).
    [InlineData(OneOf + "ref/T during a\n    T is uniq/Meter during a\n    return ir(one, 0)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData(OneOf + "ref/T during one\n    T is uniq/Meter during a\n    origin a outlives one\n    return ir(one, 0)\n", "clean")]
    [InlineData(OneOf + "ref/i32 during one\n    T is uniq/Meter during a\n    let r = ir(one, 0)\n    return r\n", "1*TypeMismatch_Kd 1*UnprovenOriginRelation_Kd")]
    // R7-C3 (F's C2 ruling 315; SPEC 8.2.3, 15.6.1): the walk's root pair is formed once, at its intake, and published with its parts,
    // so CheckTypeUse's decomposition reads the pair the walk walked: a Closure's input under the identity premise fits the argument's
    // structure and keeps only the Origin relation's record (fail-before: CheckFaulted_Kd; dev a585b50a5 adds NoApplicableOverload_Kd at
    // the call), as the twin without the premise does on every side.
    [InlineData(OneOf + "i32\n    T is uniq/Meter during a\n    let k = func (x: ref/T during w) -> i32 => 0\n    return k(ir(one, 0))\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData(OneOf + "i32\n    let k = func (x: ref/T during w) -> i32 => 0\n    return k(ir(one, 0))\n", "1*UnprovenOriginRelation_Kd")]
    public void AnInputsWellFormednessOverASlotsSolutionBoundsItsLayer(string source, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 15.6.1: a contextual payload that fits its expected payload Type only structurally is an Origin relation at the payload,
    // whether the expected enum comes from a selected parameter or an annotation, never a Type mismatch: A5's one record at the borrowed
    // payload (R7-O1 H; its twin at the same span, R6-check H5, R4-M108, H3b T50, went with Binding's record).
    [Theory]
    [InlineData("func needs(o: Option<ref/i32 during static>) -> i32 => 1\nfunc caller(p: ref/i32) -> i32\n    let local: i32 = 3\n    return needs(.Some(local@ref)) + needs(.Some(p))\n")]
    [InlineData("func caller(p: ref/i32) -> i32\n    let local: i32 = 3\n    let o: Option<ref/i32 during static> = .Some(local@ref)\n    let q: Option<ref/i32 during static> = .Some(p)\n    return 1\n")]
    public void AContextualPayloadRelationIsJudgedAtThePayload(string body)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        var borrow = (nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "local@ref", "requires the borrow local@ref outlives static, which is false");
        Assert.Equal(
            [borrow, (nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven")],
            errors.Select(x => (x.Code, Text(source, x.Span), x.Label)));
        Assert.All(errors, static x => Assert.Equal(("fit", "ref/i32 during static"), (x.Reason![3].Value, x.Reason[4].Value)));
    }

    // SPEC 23.1: at an inner position of a Borrow argument, the failing Origin is the stored Borrow, not the argument's own one: the
    // record names that Borrow and relates it, and the destination shows only the Origin at the failed position. A shorter end that an
    // equality fixed to another argument's stored Borrow is shown and related the same way, and never written as `during` a borrow.
    [Theory]
    [InlineData("func needs(x: uniq/(ref/i32 during static)) -> () => ()\nfunc g() -> i32\n    let local: i32 = 1\n    var y = local@ref\n    needs(y@uniq)\n    return 1\n", "y@uniq", "requires the borrow local@ref outlives static, which is false", "uniq/(ref/i32 during static)")]
    [InlineData("func needs(x: ref/Option<ref/i32 during static>) -> i32 => 1\nfunc g() -> i32\n    let local: i32 = 1\n    let o: Option<ref/i32> = .Some(local@ref)\n    return needs(o@ref)\n", "o@ref", "requires the borrow local@ref outlives static, which is false", "ref/Option<ref/i32 during static>")]
    [InlineData("func assign<T>(a: uniq/T, b: uniq/T) -> () => ()\nfunc g(p: ref/i32) -> ref/i32 during p\n    var x = p\n    let local: i32 = 1\n    var y = local@ref\n    assign(x@uniq, y@uniq)\n    return x\n", "x", "requires the borrow local@ref outlives p, which is false", "ref/i32 during p")]
    [InlineData("func assign<T>(a: uniq/T, b: uniq/T) -> () => ()\nfunc h(p: ref/i32) -> ref/i32 during p\n    var x = p\n    let local: i32 = 1\n    var y = local@ref\n    assign(y@uniq, x@uniq)\n    return x\n", "x", "requires the borrow local@ref outlives p, which is false", "ref/i32 during p")]
    public void AnInnerBorrowIsNamedAndRelated(string body, string text, string label, string destination)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), text, label), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(("fit", destination), (error.Reason![3].Value, error.Reason[4].Value));
        Assert.Contains(error.Related!, x => x.Role == "origin" && (Text(source, x.Span) == "local@ref" || Text(source, x.Span) == "local"));
    }

    // A valid counterpart: the stored value's Origin fits the inner position, so the call is accepted. (The exclusive forms with a
    // fitting stored reference meet the existing CallActivationConflict_Kd limit of ownership, as on the base.)
    [Fact]
    public void AnInnerBorrowThatFitsIsAccepted()
    {
        var valid = DiagnosticCorpus.Check("func needs(x: ref/Option<ref/i32 during p>, p: ref/i32) -> i32 => 1\nfunc g(p: ref/i32) -> i32\n    let o: Option<ref/i32 during p> = .Some(p)\n    return needs(o@ref, p)\n" + Main);
        Assert.True(valid.Accepted, string.Join("\n", valid.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 10.4 step 2, 15.6.1: Function-typed parameters are compared by their structural part too, so a generic candidate whose
    // substituted result Origin is static is not better than the nongeneric one, and step 3 prefers the nongeneric candidate (it
    // selected the generic one, printing 1).
    [Fact]
    public void FunctionTypedParametersRankByStructure()
        => ScalarEmissionTest.EmitFixture("CallOriginRelationFunctionRank", "func make<T>(action: (ref/i32) -> T) -> i32 => 1\nfunc make(action: (ref/i32) -> ref/i32) -> i32 => 2\nfunc pickStatic(n: ref/i32) -> ref/i32 during static => $abort(\"never\")\nrequire make(pickStatic) == 2 else => $abort(\"ranked\")", string.Empty);

    [Fact]
    public void ADeclaredRelationRelatesItsClause()
    {
        var source = Clause + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p", "requires p outlives static, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("declared", error.Reason![3].Value);

        // SPEC 23.1: `destination` is a fact of `fit` only; a declared relation relates its clause instead.
        Assert.Equal(["relation", "longer", "shorter", "source"], error.Reason.Select(static x => x.Name));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("relation", "origin a outlives static"), (related.Role, Text(source, related.Span)));
    }

    // SPEC 15.6.1, 15.6.4 step 3, 23.1.2 (R7-O1 b1; the Middle's ruling of 2026-11-19): the fresh Origin's `==` is one record at the second
    // input, relating the input that fixed it; the well-formedness of that input's instantiated parameter Type is an independent relation,
    // its own record at the same input (Q13 rule 3: an independent record added to a rejected program, which HasCallRelation hid before).
    [Fact]
    public void AFreshEqualityRelatesTheInputThatFixedIt()
    {
        var source = Fresh + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(2, errors.Length);
        var error = Assert.Single(errors, static x => x.Reason![0].Value == "==");
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "q", "requires y == x, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        var related = Assert.Single(error.Related!);
        Assert.Equal(("relation", "p"), (related.Role, Text(source, related.Span)));
        var wellFormed = Assert.Single(errors, static x => x.Reason![0].Value != "==");
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "q", "wellFormed"), (wellFormed.Code, Text(source, wellFormed.Span), wellFormed.Reason![3].Value));
    }

    // SPEC 10.3, 15.6.1: an expected result never filters by Origins; the result's relation is one record at its destination, and a
    // meet at its longer end names the failing operand (SPEC 15.3.6). A constructor call whose local's clause fixes its slot is that
    // record at the call (it was NoApplicableOverload_Kd at the call, and a fact-less UnprovenConstraint_Kd before local slots were
    // judged by variance). A local annotation's fit is A5's at its initializer (H11b D5, R7-O1 H), and a returned call's record is A5's
    // one at the call (its twin at the return statement, H0b §5.4, R4-M108, H3b T50, went with Binding's record there).
    [Theory]
    [InlineData("struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n\nfunc f(a: ref/i32, b: ref/i32) -> i32\n    let h: H{x} = H.init(a)\n        origin x.s == b\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "H.init(a)", "requires a outlives b, which is not proven", null)]
    [InlineData("func take(x: ref/i32) -> ref/i32 during x => x\nfunc f(x: ref/i32) -> ref/i32 during x\n    let local: i32 = 5\n    return take(local)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "take(local)", "requires the borrow local outlives x, which is false", "local")]
    [InlineData("func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during q => two(p, q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "two(p, q)", "requires p outlives q, which is not proven", null)]
    [InlineData("func first<T>(a: T, b: T) -> T => a@move\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during p => first(p, q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "first(p, q)", "requires q outlives p, which is not proven", null)]
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc caller(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let r: ref/i32 during p = id(q)\n    return r\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "id(q)", "requires q outlives p, which is not proven", null)]
    // A join's fit is recorded at the source that supplies its longer end (R7-O1 H, D5's Site for a join).
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc caller(p: ref/i32, q: ref/i32, c: bool) -> ref/i32 during p\n    return if c => p else => id(q)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "id(q)", "requires q outlives p, which is not proven", null)]
    [InlineData("func id(a: ref/i32) -> ref/i32 during a => a\nfunc keep(x: ref/i32 during static) -> i32 => 1\nfunc caller(q: ref/i32) -> i32 => keep(id(q))\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "id(q)", "requires q outlives static, which is not proven", null)]
    [InlineData("func pick2(a: ref/i32, b: ref/i32) -> ref/i32 during b => b\nfunc f(p: ref/i32) -> ref/i32 during p\n    let short = 2\n    return pick2($abort(\"x\"), short@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "pick2($abort(\"x\"), short@ref)", "requires the borrow short@ref outlives p, which is false", "short@ref")]
    [InlineData(OwnBodyCall + "    let p: (i32, ref/i32 during k) = f<i32>(1, z, z)\n    return (t@move, y)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "f<i32>(1, z, z)", "requires j outlives k, which is not proven", null)]
    [InlineData(OwnBodyValue + "    let g = h\n    let p: ref/i32 during k = g(z, z)\n    return y\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "g(z, z)", "requires j outlives k, which is not proven", null)]
    [InlineData(OwnBodyValue + "    let e: (ref/i32 during j, ref/i32 during j) -> ref/i32 during k = h\n    return y\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd), "h", "the result requires j outlives k, which is not proven", null)]
    [InlineData(OwnBodyCall + "    let e: (i32, ref/i32 during j, ref/i32 during j) -> (i32, ref/i32 during k) = f<i32>\n    return (t@move, y)\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd), "f<i32>", "the result requires j outlives k, which is not proven", null)]
    [InlineData(OwnBodyGetter + "            let v: ref/i32 during static = self.view\n            return self.n@ref\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "self.view", "requires self outlives static, which is not proven", null)]
    [InlineData("enum Holder {at}\n    Some(ref/i32 during at)\n    Empty\n    public func remake(self: ref/Self, v: ref/i32) -> Holder during at => .Some(v)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "v", "requires v outlives self.at, which is not proven", null)]
    [InlineData(OwnBodyCall + "    let p: (ref/i32 during j, ref/i32 during j) = f<ref/i32 during k>(y, z, z)\n    return (t@move, y)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "f<ref/i32 during k>(y, z, z)", "requires k outlives j, which is not proven", null)]
    [InlineData(OwnBodyConstructor + "        let b: Box<ref/i32 during j> = Box<ref/i32 during k>.init(y, z, z)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "Box<ref/i32 during k>.init(y, z, z)", "requires k outlives j, which is not proven", null)]
    [InlineData(OwnBodyCall + "    let p: (ref/i32 during j, ref/i32 during j) = f(y, z, z)\n    return (t@move, y)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "f(y, z, z)", "requires k outlives j, which is not proven", null)]
    [InlineData(OwnBodyMethod + "        let p: (ref/i32 during j, ref/i32 during j) = self.m<ref/i32 during k>(y, z, z)\n        return (u@move, y)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "self.m<ref/i32 during k>(y, z, z)", "requires k outlives j, which is not proven", null)]
    [InlineData(OwnBodyInput + "    let p: (ref/i32 during j, ref/i32 during j) = h<ref/i32 during y>(y, z, z)\n    return (t@move, z)\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h<ref/i32 during y>(y, z, z)", "requires y outlives j, which is not proven", null)]
    public void TheResultRelationIsJudgedAtItsDestination(string body, string code, string text, string label, string? origin)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([(code, text, label)], errors.Select(x => (x.Code, Text(source, x.Span), x.Label)));
        if (origin is not null)
        {
            // A5's record relates the borrow that created the Loan, where it finds one, beside the origin (DIAGNOSTICS; R7-O1 H).
            (string, string)[] related = code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? [("borrow", origin), ("origin", origin)] : [("origin", origin)];
            Assert.All(errors, x => Assert.Equal(related, x.Related!.Select(y => (y.Role, Text(source, y.Span)))));
        }
    }

    // SPEC 10.4 step 2, 15.6.1: Origin bindings never rank candidates, so a candidate whose Origin relation fails is still selected.
    [Fact]
    public void SelectionIgnoresOriginParts()
    {
        var literal = "func h(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc h(x: ref/i32, y: i64) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => h(p, 1)\nfunc staticCaller(p: ref/i32 during static) -> i32 => h(p, 1)\n" + Main;
        Assert.Equal([nameof(DiagnosticCode.AmbiguousBinding_Kd), nameof(DiagnosticCode.AmbiguousBinding_Kd)], DiagnosticCorpus.Check(literal).Diagnostics.Select(static x => x.Code));

        // The nongeneric candidate wins by step 3; its relation is then Unproven (it was the generic one, printing 2).
        var generic = "func k(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc k<T>(x: ref/i32, y: T) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => k(p, 1@i32)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(generic).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "p"), (error.Code, Text(generic, error.Span)));
    }

    [Theory]
    [InlineData("Ranked", "func m<T>(x: ref/i32 during static, y: T) -> i32 => 1\nfunc m(x: ref/i32, y: i32) -> i32 => 2\nfunc caller(p: ref/i32) -> i32 => m(p, 1@i32)\nlet v: i32 = 5\nrequire caller(v@ref) == 2 else => $abort(\"ranked\")")]
    [InlineData("Explicit", "func k(x: ref/i32 during static, y: i32) -> i32 => 1\nfunc k<T>(x: ref/i32, y: T) -> i32 => 2\nfunc localCaller(p: ref/i32) -> i32 => k<i32>(p, 1)\nlet v: i32 = 5\nrequire localCaller(v@ref) == 2 else => $abort(\"explicit\")")]
    public void AStructuralSelectionRuns(string name, string source)
        => ScalarEmissionTest.EmitFixture("CallOriginRelation" + name, source, string.Empty);

    // SPEC 15.6.5: selected call contracts constrain the local region and retain potential stored Loans.
    // The same contract is accepted while all sources live and rejected at destruction when one escapes its scope.
    [Theory]
    [InlineData("func put(anchor: ref/i32, target: uniq/(ref/i32 during anchor)) -> () => ()\nfunc f() -> ()\n    let a = 1\n    let c = 3\n    var slot: ref/i32 = c@ref\n    put(a@ref, slot@uniq)\n    Console.writeLine(\"\\(slot@follow)\")\n", "a@ref")]
    [InlineData("func put<T>(target: uniq/T, value: T) -> () => ()\nfunc f() -> ()\n    let a = 1\n    let b = 2\n    var slot = a@ref\n    put(slot@uniq, b@ref)\n    Console.writeLine(\"\\(slot@follow)\")\n", "b@ref")]
    [InlineData("func put<T>(target: uniq/T, value: T) -> () => ()\nfunc f() -> ()\n    let a = 1\n    var slot = a@ref\n    if true\n        let b = 2\n        put(slot@uniq, b@ref)\n    Console.writeLine(\"\\(slot@follow)\")\n", "b@ref")]
    [InlineData("func put(target: uniq/(ref/i32 during o), value: ref/i32 during o) -> () => ()\nfunc g(x: ref/i32) -> i32\n    let local = 1\n    var slot = local@ref\n    put(slot@uniq, x)\n    return slot@follow\n", "x")]
    public void CallsThatStoreIntoBodyOriginsKeepTheirLoans(string body, string text)
    {
        var source = body + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        if (body.Contains("    if true", StringComparison.Ordinal))
        {
            // A5's record is at the destroyed local's declaration (R6-check H5, DECISIONS R5-M185; H3b T44), relates the stored borrow
            // and the holder `slot`, whose next use lies past the `if` block, at its declaration (DIAGNOSTICS A6's `Store` witness; the
            // U-T2 amendment of DECISIONS R4-M57), and has no Note ("`b` is destroyed here"; T49, until A6 publishes it).
            var error = Assert.Single(errors);
            Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "let b = 2", (string?)null), (error.Code, Text(source, error.Span), error.Note));
            Assert.Equal(
                [("borrow", text, "borrow that created the loan"), ("loan", "var slot = a@ref", "value retaining the conflicting loan")],
                error.Related!.Select(x => (x.Role, Text(source, x.Span), x.Label)));
        }
        else
        {
            Assert.Empty(errors);
            var c = MinimalEmissionTest.Analyze(source);
            Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
        }

        Assert.Contains(text, source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func two(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    return a\nfunc caller(p: ref/i32, q: ref/i32) -> i32 => two(p, q)@follow\n")]
    [InlineData("func needs(a: ref/i32) -> i32\n    origin a outlives static\n    return 1\nfunc staticCaller(p: ref/i32 during static) -> i32 => needs(p)\n")]
    [InlineData("struct S\n    public var n: i32\n    public init(n: i32) => self.n = n\n    public func takes(self: ref/Self, x: ref/i32 during self) -> i32 => 1\n    public func view(self: ref/Self) -> ref/i32 during self => self.n@ref\nfunc viaReceiver(s: ref/S, x: ref/i32) -> i32 => s.takes(x)\nfunc viaReceiverOk(s: ref/S, x: ref/i32) -> i32\n    origin x outlives s\n    return s.takes(x)\nfunc viaReceiverSame(s: ref/S, x: ref/i32 during s) -> i32 => s.takes(x)\nfunc viaResult(s: ref/S) -> ref/i32 during s => s.view()\n")]
    public void AProvenCallRelationIsAccepted(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 15.6.1: Constraints whose proof involves Origins, such as Owned, still take part in applicability.
    [Fact]
    public void ConstraintsKeepTakingPartInApplicability()
    {
        var source = "func hold<T>(x: T, n: i32) -> i32\n    T is Owned\n    return 1\n\nfunc hold<T>(x: T, n: i64) -> i32 => 2\n\nfunc caller(p: ref/i32) -> i32 => hold(p, 1)\n\nfunc staticCaller(p: ref/i32 during static) -> i32 => hold(p, 1)\n" + Main;
        // SPEC 10.1 (DECISIONS: G572 U2a): an unproven candidate is compared as though applicable, so with neither strictly best the call is
        // ambiguous, as its Proven twin is (Q13: the first call was UnprovenConstraint_Kd by the deferral).
        Assert.Equal([nameof(DiagnosticCode.AmbiguousBinding_Kd), nameof(DiagnosticCode.AmbiguousBinding_Kd)], DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));
    }

    // SPEC 23.3: the console, the language server and JSON carry the call relation with its related location; Fresh's second record, its
    // input's well-formedness (SPEC 23.1.2, R7-O1 b1), relates nothing of the call (AFreshEqualityRelatesTheInputThatFixedIt).
    [Theory]
    [InlineData(Clause, "relation", "origin a outlives static")]
    [InlineData(Fresh, "relation", "p")]
    public void EveryOutputCarriesTheCallRelation(string body, string role, string related)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        var record = Assert.Single(check.Diagnostics, x => x.Related?.Any(r => r.Role == role) == true);
        var result = new DiagnosticResult(check.Diagnostics, check.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        output.WriteLine(console.Text);
        Assert.Contains("UnprovenOriginRelation_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains($" = {role}: ", console.Text, StringComparison.Ordinal);
        Assert.Equal(related, Text(body + Main, record.Related![0].Span));

        var identity = SourceIdentity.FromPath(check.Sources[record.Source].Path);
        var index = Array.IndexOf(check.Diagnostics, record);
        var placed = WorkspaceCheck.Place(check, [identity], identity, false)[identity];
        Assert.Equal(check.Diagnostics.Length, placed.Length);
        var plain = placed[index];
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var withRelated = WorkspaceCheck.Place(check, [identity], identity, true)[identity][index];
        Assert.Equal(record.Related![0].Range, Assert.Single(withRelated.RelatedInformation!).Location.Range);

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // SPEC 15.3.7, 15.6.4 step 3: the call proves its inputs' instantiated Types well formed. Where `c` cannot shorten, being fixed by
    // the invariant third input, the input whose Type is not well formed is the record, with the shared Origin's Borrow related.
    [Theory]
    [InlineData("    f(m@ref, q, w)\n")]
    [InlineData("    let h = f\n    h(m@ref, q, w)\n")]
    public void AnInputsWellFormednessIsJudgedAtThatInput(string call)
    {
        var source = Box + "func f(x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c, z: uniq/(ref/i32 during c)) -> ()\n    z@follow = x\n" +
            "func g(q: ref/Box<ref/i32 during s> during t, w: uniq/(ref/i32 during t)) -> ()\n    let m: i32 = 5\n" + call + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "q", "requires the borrow m@ref outlives t, which is false"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal("wellFormed", error.Reason![3].Value);
        Assert.Equal(["relation", "longer", "shorter", "source"], error.Reason.Select(static x => x.Name));

        // A5's record relates the shared Origin's Borrow as `borrow` beside `origin` (R6-check H5; H3b T49, until A6).
        Assert.Equal([("borrow", "m@ref"), ("origin", "m@ref")], error.Related!.Select(x => (x.Role, Text(source, x.Span))));
    }

    // SPEC 15.6.4 step 3: an Origin shared with an input's inner position bounds the call's solution, so the result over it is shortened
    // to the borrow and fails at its destination (PLAN G85: these programs read a dead local before): A5's one record at the call (R7-O1
    // H; its twin at the return statement, R6-check H5, R4-M108, H3b T50, went with Binding's record there).
    [Theory]
    [InlineData("func f(x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c\n    return x\n", "", "f(m@ref, q)")]
    [InlineData("func f(x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c\n    return x\n", "    let h = f\n", "h(m@ref, q)")]
    [InlineData("func f<s/T>(x: s/T during a, y: ref/Box<s/T during a> during c) -> ref/T during c\n    s is ref\n    return x@follow@ref\n", "", "f(m@ref, q)")]
    public void ASharedInputOriginBoundsTheResult(string callee, string prefix, string call)
    {
        var source = Box + callee + "func g(q: ref/Box<ref/i32 during s> during t) -> ref/i32 during t\n    let m: i32 = 5\n" + prefix + "    return " + call + "\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        const string Label = "requires the borrow m@ref outlives t, which is false";
        Assert.Equal(
            [(nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), call, Label)],
            errors.Select(x => (x.Code, Text(source, x.Span), x.Label)));
        Assert.All(errors, static x => Assert.Equal("fit", x.Reason![3].Value));
    }

    // The valid twins: a shared Origin that every input supplies, an owner instance whose pair slot does not exist, a generic caller
    // whose own input premise proves the shortened result, and a conformance whose requirement shares the Origin too.
    [Theory]
    [InlineData("func f(x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c\n    return x\nfunc g() -> i32\n    let n: i32 = 3\n    let b = Box.init(n@ref)\n    return f(n@ref, b@ref)@follow\n")]
    [InlineData("func f<s/T>(x: s/T during a, y: ref/Box<s/T during a> during c, z: ref/i32 during a) -> i32\n    s is owner or ref\n    return 1\nfunc g(q: ref/Box<i32>) -> i32\n    let m: i32 = 5\n    let k: i32 = 6\n    return f(k, q, m@ref)\n")]
    [InlineData("func f<E>(x: ref/E during a, y: ref/Box<ref/E during a> during c) -> ref/E during c\n    return x\nfunc g<E>(q: ref/Box<ref/E during s> during t, v: ref/E during s) -> ref/E during t\n    return f(v, q)\n")]
    [InlineData("contract Picker\n    func pick(self: ref/Self, x: ref/i32 during p, y: ref/Box<ref/i32 during p> during r) -> ref/i32 during r\nstruct Chooser\n    Self is Picker\n    public init() => ()\n    public func pick(self: ref/Self, x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c => x\n")]
    public void AWellFormedInstanceIsAccepted(string body)
    {
        var check = DiagnosticCorpus.Check(Box + body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's instantiated inputs well formed wherever its solve replaced a
    // required Origin, here one the required contract keeps apart from the shared one: a Function Item argument, a conversion and a
    // conformance witness.
    [Theory]
    [InlineData("func apply(h: (ref/i32, ref/Box<ref/i32 during s> during t) -> ref/i32 during t, y: ref/Box<ref/i32 during s> during t) -> ref/i32 during t\n    let m: i32 = 5\n    return h(m@ref, y)\nfunc g(q: ref/Box<ref/i32 during s> during t) -> ref/i32 during t => apply(f, q)\n", nameof(DiagnosticCode.NoApplicableOverload_Kd))]
    [InlineData("func g(q: ref/Box<ref/i32 during s> during t) -> ref/i32 during t\n    let h: (ref/i32, ref/Box<ref/i32 during s> during t) -> ref/i32 during t = f\n    let m: i32 = 5\n    return h(m@ref, q)\n", nameof(DiagnosticCode.UnprovenOriginContract_Kd))]
    [InlineData("contract Picker\n    func pick(self: ref/Self, x: ref/i32 during p, y: ref/Box<ref/i32 during q> during r) -> ref/i32 during r\nstruct Chooser\n    Self is Picker\n    public init() => ()\n    public func pick(self: ref/Self, x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c => x\n", nameof(DiagnosticCode.IncompatibleContractImplementation_Kd))]
    public void AComparisonProvesTheImplementationsInputs(string body, string code)
    {
        var source = Box + "func f(x: ref/i32 during a, y: ref/Box<ref/i32 during a> during c) -> ref/i32 during c\n    return x\n" + body + Main;
        Assert.Equal([code], DiagnosticCorpus.Check(source).Diagnostics.Select(static x => x.Code));
    }

    // SPEC 15.6.4 step 3, 7.2.3, 10.1.1 (DECISIONS BU-51): a slot that a default fills is an input too, so its instantiated Type's
    // well-formedness bounds the call's solution and is judged at the call, every position. A Finite end at a fixed one is Unsatisfied, two
    // fixed ends Unproven, a result the premise shortens fails its own fit at its destination, and local ends are a Loan conflict where
    // the borrowed local is destroyed. Each was accepted before (PLAN G277 (1)). z2's `a`, which only the default and the result name,
    // is solved by the default's premise (DECISIONS BU-99, superseding BU-51's region for it), so its record is the result's fit, as
    // z5's, A5's one record at the call (R7-O1 H; the return statement's re-record, a dependent twin, PLAN G430, R4-M108, went with
    // Binding's record).
    [Theory]
    [InlineData(Put2 + "func bad(p: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    let sr = short@ref\n    put2(p, sr)\n    return 0\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "put2(p, sr)", "wellFormed")]
    [InlineData(Put2 + "func bad(p: uniq/(ref/i32 during q), sr: ref/i32 during r) -> i32\n    put2(p, sr)\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "put2(p, sr)", "wellFormed")]
    [InlineData(GenericPut + "func bad(p: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    g(p, short@ref)\n    return 0\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "g(p, short@ref)", "wellFormed")]
    [InlineData(MethodPut + "func bad(p: uniq/(ref/i32 during q)) -> i32\n    let s = S.init()\n    let short = 2\n    s.put(p, short@ref)\n    return 0\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "s.put(p, short@ref)", "wellFormed")]
    [InlineData(ConstructorPut + "func bad(p: uniq/(ref/i32 during q)) -> i32\n    let short = 2\n    let h = H.init(p, short@ref)\n    return 0\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "H.init(p, short@ref)", "wellFormed")]
    [InlineData(ResultOnly + "func bad(p: ref/i32) -> ref/i32 during p\n    let short = 2\n    return z2(short@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "z2(short@ref)", "fit")]
    [InlineData(SharedInput + "func bad(p: ref/i32 during q) -> ref/i32 during q\n    let short = 2\n    return z5(p, short@ref)\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "z5(p, short@ref)", "fit")]
    [InlineData(Put2 + "func outer() -> i32\n    let long = 1\n    var keep = long@ref\n    if true\n        let short = 2\n        put2(keep@uniq, short@ref)\n    return keep@follow\n", nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "let short = 2", null)]
    public void ADefaultedInputsWellFormednessIsJudgedAtTheCall(string body, string code, string text, string? source)
    {
        // Since H5 A5 records the Loan conflict at the destroyed local's declaration (H3b T44).
        var program = body + Main;
        var errors = DiagnosticCorpus.Check(program).Diagnostics;
        Assert.Equal([(code, text)], errors.Select(x => (x.Code, Text(program, x.Span))));
        if (source is not null)
        {
            Assert.All(errors, x => Assert.Equal(source, x.Reason![3].Value));
        }
    }

    // The valid twins: inputs that make every default's Type well formed (an equal Origin, a clause, a longer local), an Origin that only
    // defaults, or only a default and the result, name (a region of the call; r2ok and r2d were a false UnprovenConstraint_Kd before), a
    // result the premise shortens read inside the body, a default copying an input's referent, and an omitted base clause's base call.
    [Theory]
    [InlineData(Put2 + "func ok(p: uniq/(ref/i32 during q), sr: ref/i32 during q) -> i32\n    put2(p, sr)\n    return 0\n")]
    [InlineData(Put2 + "func ok(p: uniq/(ref/i32 during q), sr: ref/i32 during r) -> i32\n    origin r outlives q\n    put2(p, sr)\n    return 0\n")]
    [InlineData(Put2 + "func outer() -> i32\n    let long = 1\n    let other = 3\n    var keep = long@ref\n    if true\n        put2(keep@uniq, other@ref)\n    return keep@follow\n")]
    [InlineData(GenericPut + "func ok(p: uniq/(ref/i32 during q), r: ref/i32 during q) -> i32\n    g(p, r)\n    return 0\n")]
    [InlineData(MethodPut + "func outer() -> i32\n    let s = S.init()\n    let long = 1\n    let other = 3\n    var keep = long@ref\n    if true\n        s.put(keep@uniq, other@ref)\n    return keep@follow\n")]
    [InlineData(ConstructorPut + "func outer() -> i32\n    let long = 1\n    let other = 3\n    var keep = long@ref\n    if true\n        let h = H.init(keep@uniq, other@ref)\n    return keep@follow\n")]
    [InlineData(ResultOnly + "func ok(p: ref/i32) -> ref/i32 during p\n    return z2(p)\n")]
    [InlineData(ResultOnly + "func free() -> i32\n    let x = 1\n    let r = z2(x@ref)\n    return r@follow\n")]
    [InlineData(ResultOnly + "func free() -> i32\n    let x = 1\n    let n = z2(x@ref)@follow\n    return n\n")]
    [InlineData("func z0(" + Defaulted + ") => ()\nfunc free() -> i32\n    z0()\n    return 0\n")]
    [InlineData(SharedInput + "func ok(p: ref/i32 during q) -> i32\n    let short = 2\n    let r = z5(p, short@ref)\n    return r@follow\n")]
    [InlineData(Copying + "func outer() -> i32\n    var x = 1\n    let y = 2\n    var keep = y@ref\n    let inner = x@ref\n    put3(keep@uniq, inner@ref)\n    let n = keep@follow\n    x = 5\n    return n\n")]
    [InlineData("public open struct B\n    public init(" + Defaulted + ") => ()\npublic struct D : B\n    public init(x: i32) => ()\nfunc use() -> i32\n    let d = D.init(1)\n    return 0\n")]
    public void AWellFormedDefaultIsAccepted(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 3.8, 10.8, 15.3.6, 15.6.4 steps 1 and 3 (PLAN G410): a Never argument fits its parameter and supplies no Origin, so the input
    // Origin its parameter elides, which the result names, has no candidate and is a region of the call, as a named Origin is: valid
    // after `$abort`, a transfer and a value call (UnprovenConstraint_Kd at the named calls, Unsupported_Kd at the value call before),
    // and so are the twins with a real argument. So are an omitted Never default (SPEC 7.2.3: no evidence), an aggregate input
    // (`s.source`), a Contract requirement, a Closure, and value calls through a Function Type, a Callable bound and an erased value,
    // whose binder's per-call inputs are its call's regions too (Unsupported_Kd before). The invalid twin, whose real argument still
    // bounds the result, is TheResultRelationIsJudgedAtItsDestination's pick2 row.
    [Theory]
    [InlineData("func g(x: ref/i32 = $abort(\"d\")) -> ref/i32 during x => x\nfunc f() -> i32\n    let r = g()\n    return r@follow\n")]
    [InlineData("func fs(s: Slice<i32>) -> Slice<i32> during s.source => s\nfunc f() -> i32\n    let r = fs($abort(\"x\"))\n    return 0\n")]
    [InlineData("contract Getter\n    func get(self: ref/Self, x: ref/i32) -> ref/i32 during x\nfunc use<T>(t: ref/T) -> i32\n    T is Getter\n    let r = t.get($abort(\"x\"))\n    return 0\n")]
    [InlineData("func f() -> i32\n    let c = func [] (x: ref/i32) -> ref/i32 during x => x\n    let r = c($abort(\"x\"))\n    return 0\n")]
    [InlineData("func h(g: (ref/i32) -> ref/i32) -> i32\n    let r = g($abort(\"x\"))\n    return 0\n")]
    [InlineData("func h(g: (ref/i32) -> ref/i32) -> i32\n    let v: i32 = 1\n    let r = g(v)\n    return r@follow\n")]
    [InlineData("func app<F>(f: ref/F) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    let r = f($abort(\"x\"))\n    return 0\n")]
    [InlineData("func app<F>(f: ref/F) -> i32\n    F is Callable<(ref/i32) -> ref/i32>\n    let v: i32 = 1\n    let r = f(v)\n    return r@follow\n")]
    [InlineData(Identity + "func f() -> i32\n    let g: (ref/i32) -> ref/i32 = id\n    let r = g($abort(\"x\"))\n    return 0\n")]
    [InlineData(Identity + "func f() -> i32\n    let r = id($abort(\"x\"))\n    return 0\n")]
    [InlineData(Identity + "func f() -> i32\n    let r = id(do => return 7)\n    return 0\n")]
    [InlineData("func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a => a\nfunc f() -> i32\n    let v: i32 = 1\n    let r = pick($abort(\"x\"), v)\n    return 0\n")]
    [InlineData(Identity + "func f() -> i32\n    let r = id($abort(\"x\"))\n    return r@follow\n")]
    [InlineData(Identity + "func f() -> i32\n    let g = id\n    let r = g($abort(\"x\"))\n    return 0\n")]
    [InlineData(Identity + "func f() -> i32\n    let v: i32 = 1\n    let r = id(v)\n    return r@follow\n")]
    [InlineData(Identity + "func f() -> i32\n    let v: i32 = 1\n    let g = id\n    let r = g(v)\n    return r@follow\n")]
    public void ANeverArgumentLeavesItsInputOriginARegionOfTheCall(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 8.15.1, 8.15.3, 15.3.4, 15.3.6, 7.7.4 (PLAN G458): in a callable's own body its Origins are the caller's, and a call, an erasure,
    // a value call or a Property read of it instantiates them as from any other body, though each image is then one of its own atoms:
    // valid as from another function (UnprovenConstraint_Kd at the call before G410, InternalInvariant_Kd at the erasure, TypeMismatch_Kd
    // at the generic erasure, Unsupported_Kd at the value call and the getter's read before). The invalid twins, and a Case construction
    // in its enum's own method, are TheResultRelationIsJudgedAtItsDestination's rows. A Type
    // argument naming the callee's own Origin or input is mapped once, with the callee's Origins and inputs, and normalized after them (PLAN
    // G457: two or three UnprovenOriginRelation_Kd before).
    [Theory]
    [InlineData(OwnBodyCall + "    let p: (i32, ref/i32 during j) = f<i32>(1, z, z)\n    return (t@move, y)\n")]
    [InlineData(OwnBodyValue + "    let e: (ref/i32 during j, ref/i32 during j) -> ref/i32 during j = h\n    return y\n")]
    [InlineData(OwnBodyCall + "    let e: (i32, ref/i32 during j, ref/i32 during j) -> (i32, ref/i32 during j) = f<i32>\n    return (t@move, y)\n")]
    [InlineData(OwnBodyValue + "    let g = h\n    let p: ref/i32 during j = g(z, z)\n    return y\n")]
    [InlineData(OwnBodyGetter + "            let v: ref/i32 during self = self.view\n            return self.n@ref\n")]
    [InlineData(OwnBodyCall + "    let p: (ref/i32 during k, ref/i32 during j) = f<ref/i32 during k>(y, z, z)\n    return (t@move, y)\n")]
    [InlineData(OwnBodyCall + "    let p: (ref/i32 during k, ref/i32 during j) = f(y, z, z)\n    return (t@move, y)\n")]
    [InlineData(OwnBodyMethod + "        let p: (ref/i32 during k, ref/i32 during j) = self.m<ref/i32 during k>(y, z, z)\n        return (u@move, y)\n")]
    [InlineData(OwnBodyConstructor + "        let b: Box<ref/i32 during k> = Box<ref/i32 during k>.init(y, z, z)\n")]
    [InlineData(OwnBodyInput + "    let p: (ref/i32 during y, ref/i32 during j) = h<ref/i32 during y>(y, z, z)\n    return (t@move, z)\n")]
    [InlineData(OwnBodyProjection + "    let p: (ref/i32 during k, ref/i32 during j) = f<Cursor during k>(c@move, y, z, z)\n    return (i@move, y)\n")]
    public void ACallableUsedInItsOwnBodyIsJudgedAsFromAnotherBody(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 7.2.3 (PLAN G410): a default is checked at its declaration and is no evidence, so an omitted one leaves its input a region of
    // the call, and the call has no failure of its own: the declaration's record stands alone (dev added a spurious
    // UnprovenConstraint_Kd at `g(v)`; nothing independent is lost).
    [Fact]
    public void AnOmittedDefaultFailsOnlyAtItsDeclaration()
    {
        var source = "func g(a: ref/i32, x: ref/i32 = a) -> ref/i32 during x => x\nfunc f() -> i32\n    let v: i32 = 1\n    let r = g(v)\n    return r@follow\n" + Main;
        Assert.Equal([(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "a")], DiagnosticCorpus.Check(source).Diagnostics.Select(x => (x.Code, Text(source, x.Span))));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmRelationFreeOverloadSelectionAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze("func pick(x: ref/i32, y: i32) -> i32 => 1\nfunc pick(x: ref/i32, y: i64) -> i32 => 2\nfunc view(a: ref/i32, b: ref/i32) -> ref/i32 during a => a\nlet v: i32 = 3\nlet w: i32 = 4\nrequire pick(v@ref, 1@i32) + pick(w@ref, 2@i64) + view(v@ref, w@ref)@follow == 6 else => $abort(\"warm\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        Assert.True(c.Binding.CheckStartup(OutputKind.Application).IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }

    // PLAN G647 (SPEC 15.6.1, 15.6.4): an Origin relation is judged at every nested position of the related Types, past the 32 and 64
    // components the relation walks once counted. x and y differ in their innermost Origin (c, d) alone: x returned as its own Type is
    // accepted, and as y's Type needs c: d, unproven, at the returned expression: directly, through a call's result, through a generic
    // identity and through an annotated local. The identity's valid form holds x by value, which the storage height limit (64, DECISIONS
    // BU-12) refuses past 64 layers, so only its invalid twin is read there; the call's valid twin is read at 33 alone (15 seconds at 67:
    // the Origin proof's work, PLAN G647's design section 7).
    [Theory]
    [InlineData("deep", 33, true)]
    [InlineData("deep", 67, true)]
    [InlineData("call", 33, true)]
    [InlineData("call", 67, false)]
    [InlineData("let", 33, true)]
    [InlineData("let", 67, true)]
    [InlineData("id", 33, true)]
    [InlineData("id", 64, true)]
    [InlineData("id", 67, false)]
    public void ARelationIsJudgedAtEveryNestedLayer(string form, int layers, bool valid)
    {
        var (c, d) = (TypeDagScalingTest.RefLayers(layers, "i32", "c"), TypeDagScalingTest.RefLayers(layers, "i32", "d"));
        foreach (var result in valid ? new[] { c, d } : [d])
        {
            var (declarations, body, at) = form switch
            {
                "deep" => (string.Empty, "    return x\n", "x"),
                "call" => ($"func first(x: {c}, y: {d}) -> {c}\n    return x\n", "    return first(x, y)\n", "first(x, y)"),
                "id" => ("func id<T>(v: T) -> T\n    return v@move\n", "    return id(x)\n", "id(x)"),
                _ => (string.Empty, $"    let v: {result} = x\n    return v\n", "x"), // At the initializer (H11b D5, R7-O1 H).
            };
            var source = declarations + $"func f(x: {c}, y: {d}) -> {result}\n" + body + Main;
            var records = DiagnosticCorpus.Check(source).Diagnostics;
            if (ReferenceEquals(result, c))
            {
                Assert.Empty(records.Select(static x => x.Code));
            }
            else
            {
                var record = Assert.Single(records);
                Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), at), (record.Code, Text(source, record.Span)));
            }
        }
    }

    // J2 (SPEC 10.5.1, 10.8, 15.3.6, 15.2.2; the owner's O-358 (a) and O-362 (a); PLAN G936): a callee variable that covariant evidence
    // binds and a waiting argument also names, a Type argument's Origin at a covariant position or a written Origin parameter, is a region
    // of the call that both bound, T = ref/i32 during (R_a and R_b): over two `var` captures in a Closure (rc3v; rc5v in an array literal,
    // tp1 in a Tuple literal), the arguments swapped (j2b), two evidence (e2v, e2m) or two waiting arguments (w2v), the result kept (k1v)
    // or nested as evidence (k2v), two unrelated inputs (j2p), with a second overload (ov1), a written Origin parameter (o2v, o4v), and
    // the body chain and Loans that the old relation hid (k4v, l1v: Unsupported_Kd before). A Function Type T keeps its contravariant
    // Origin (fn1v: the contravariant row's candidate p, F's ruling 3), and a slot a Constraint reads keeps the evidence's binding (rv17
    // `T is Owned`, rv28 `F is Callable<() -> T>`, rv34; O-368 (a)). Rejected (UnprovenOriginRelation_Kd at the waiting part) on dev
    // 45c9fab40 and G913 7b652b1f3, but fn1v, rv17, rv28 and rv34.
    [Theory]
    // rc3v
    [InlineData("func same3<T>(x: Option<ref/T during c>, y: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        same3(.Some(a@ref), b@ref)\n    f()\n    return 0\n")]
    // rc5v
    [InlineData("func same5<T>(x: [2 of Option<ref/T during c>], y: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        same5([.Some(a@ref), .None], b@ref)\n    f()\n    return 0\n")]
    // tp1
    [InlineData("func tup<T>(x: (Option<ref/T during c>, i32), y: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        tup((.Some(a@ref), 1), b@ref)\n    f()\n    return 0\n")]
    // j2b
    [InlineData("func same3s<T>(y: ref/T during c, x: Option<ref/T during c>) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        same3s(b@ref, .Some(a@ref))\n    f()\n    return 0\n")]
    // e2v
    [InlineData("func same3e<T>(x: Option<ref/T during c>, y: ref/T during c, z: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var d = p\n    let f = func [a, b, d] () -> ()\n        same3e(.Some(a@ref), b@ref, d@ref)\n    f()\n    return 0\n")]
    // e2m
    [InlineData("func same3m<T>(y: ref/T during c, x: Option<ref/T during c>, z: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var d = p\n    let f = func [a, b, d] () -> ()\n        same3m(b@ref, .Some(a@ref), d@ref)\n    f()\n    return 0\n")]
    // w2v
    [InlineData("func same3t<T>(x: Option<ref/T during c>, y: ref/T during c, z: Option<ref/T during c>) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var d = p\n    let f = func [a, b, d] () -> ()\n        same3t(.Some(a@ref), b@ref, .Some(d@ref))\n    f()\n    return 0\n")]
    // k1v
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        let r = keep(.Some(a@ref), b@ref)\n    f()\n    return 0\n")]
    // k2v
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc id<V>(v: V) -> V => v@move\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        let r = id(keep(.Some(a@ref), b@ref))\n    f()\n    return 0\n")]
    // j2p
    [InlineData("func same3<T>(x: Option<ref/T during c>, y: ref/T during c) => ()\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    let a = p\n    let b = q\n    same3(.Some(a@ref), b@ref)\n    return 0\n")]
    // ov1
    [InlineData("func pick<T>(x: Option<ref/T during c>, y: ref/T during c) -> i32 => 1\nfunc pick<T>(x: Option<ref/T during c>, y: ref/T during c, z: i32) -> i32 => 2\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        let n = pick(.Some(a@ref), b@ref)\n    f()\n    return 0\n")]
    // o2v
    [InlineData("func w2(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        w2(.Some(a@ref), b@ref)\n    f()\n    return 0\n")]
    // o4v
    [InlineData("func w4(x: Option<ref/i32 during c>, y: ref/i32 during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    let f = func [a, b] () -> ()\n        w4(.Some(a), b)\n    f()\n    return 0\n")]
    // k4v
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let a = p\n    let b = p\n    return keep(.Some(a@ref), b@ref)\n")]
    // l1v
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use() -> i32\n    var x = 5\n    var y = 6\n    let a = x@ref\n    let b = y@ref\n    let r = keep(.Some(a@ref), b@ref)\n    let s = r\n    y = 7\n    return 0\n")]
    // fn1v
    [InlineData("func fun<T>(x: Option<T>, y: T) => ()\nfunc use(p: ref/i32) -> i32\n    let g: (ref/i32 during p) -> () = func (v) => ()\n    let h: (ref/i32 during p) -> () = func (v) => ()\n    fun(.Some(h@move), g@move)\n    return 0\n")]
    // hole9_ok (MirOriginsTest's, valid, the owner's (i)): `d`, named by the evidence x and the waiting w, is a region of the call
    [InlineData("func mk(p: Option<ref/(ref/i32 during a) during b>, u: Option<ref/(ref/i32 during e) during a>, w: Option<ref/(ref/i32 during d) during b>, y: ref/i32 during b, z: ref/i32 during e, x: ref/i32 during (a and d), g: (ref/i32 during b) -> i32, f: (ref/i32 during a, bool) -> i32) -> i32 => 0\nfunc k(c: bool, s: ref/i32 during static) -> i32\n    let short = 2\n    let sr = short@ref\n    return mk(if c => .None else => .None, if c => .None else => .None, if c => .None else => .None, s, s, s, func [] (t: ref/i32) => 0, func [s] (v, c2) => mk(if c2 => .None else => .None, if c2 => .None else => .None, if c2 => .None else => .None, s, s, v, func [] (t: ref/i32 during static) => 0, func [] (v2, c3) => 0))\n")]
    // rv17
    [InlineData("func keepO<T>(x: Option<T>, y: T) -> i32\n    T is Owned\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use() -> i32\n    return keepO(.Some(one()), one())\n")]
    // rv28
    [InlineData("func app4<T, F>(g: ref/F, y: T, x: Option<T>) -> i32\n    F is Callable<() -> T>\n    return 0\nfunc use(q: ref/i32) -> i32\n    return app4(func [q] () => q, q, .Some(q))\n")]
    // rv34
    [InlineData("func app4<T, F>(g: ref/F, y: T, x: Option<T>) -> i32\n    F is Callable<() -> T>\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use() -> i32\n    return app4(one, one(), .Some(one()))\n")]
    // k3d (PLAN G1127 R1; D-882's cw_k3d): the evidence `q`'s inner Origin is an anonymous function parameter's omitted nested Origin,
    // fixed in the Closure body (SPEC 15 Nested signatures), so it opens as rc3v's capture does: T = `ref/i32 during (i and region)`.
    // k3i below is its invalid twin, the result returned `during q`.
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    let f = func [a] (q: ref/(ref/i32)) -> i32\n        let r = keep(.Some(a@ref), q)\n        return 0\n    return 0\n")]
    // A join argument whose sources wait (PLAN G1127 R2; SPEC 10.5.1: each source is classified as it would be as that argument): its
    // Evidence root is read through its Signature leaves, so T and c open as for one leading-dot Case. j1, j5 (a match), j8 (`a
    // outlives b` stated: valid under either reading, it failed on the per-call c alone) and j10 (in a Closure); j7, j13 below.
    // j1
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc two(flag: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    let r = keep(if flag => .Some(x) else => .None, y)\n    return 0\n")]
    // j5
    [InlineData("func keepr<T>(y: ref/T during c, x: Option<ref/T during c>) -> T => $abort(\"no\")\nfunc two(flag: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    let r = keepr(y, match flag\n        true => .Some(x)\n        false => .None\n    )\n    return 0\n")]
    // j8
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc two(flag: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    origin a outlives b\n    let r = keep(if flag => .Some(x) else => .None, y)\n    return 0\n")]
    // ja2 (the Middle's RP17 C2, its label): a joined payload borrowing a local beside a local's evidence, the local written after the
    // result's last use; the base gave BU-156's located Unsupported_Kd at the payload (T unopened, the payload misfit by Origins alone).
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc sink(v: ref/i32) -> i32 => 0\nfunc run(flag: bool) -> i32\n    var m: i32 = 1\n    var n: i32 = 2\n    let pm = m@ref\n    let pn = n@ref\n    let r = keep(if flag => .Some(pm@ref) else => .None, pn@ref)\n    let k = sink(r)\n    m = 5\n    return k\n")]
    // j10
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc two(flag: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> i32\n    let f = func [flag, x, y] () -> i32\n        let r = keep(if flag => .Some(x) else => .None, y)\n        return 0\n    return 0\n")]
    public void AWaitingArgumentAlsoBoundsTheVariableItsEvidenceBinds(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // J2's invalid twins keep every other bound of the region: a use it cannot outlive (k3i, k4i, k5i, o2i, o3i, o4i: the record moves
    // to the call, whose result needs it), an invariant evidence binding (u1i, o2u) or waiting position (w1i, o2w), a Loan of either
    // operand (l1i, l2i: ComparisonLoanConflict_Kd at the write, Unsupported_Kd before), a Function Type T's contravariant Origin without
    // a candidate (fn1i), T named by a Callable Constraint, outside the rule (af2), and a slot `T is Owned` reads, which keeps the
    // evidence's binding (rv18; O-368 (a)). Each record: its code and the source at its span.
    [Theory]
    // k3i
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    let f = func [a] (q: ref/(ref/i32)) -> ref/i32 during q\n        return keep(.Some(a@ref), q)\n    return 0\n", "UnprovenOriginRelation_Kd@keep(.Some(a@ref), q|UnprovenOriginRelation_Kd@a@ref), q)")]
    // k4i
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let a = p\n    let b = q\n    return keep(.Some(a@ref), b@ref)\n", "UnprovenOriginRelation_Kd@keep(.Some(a@ref), b")]
    // k5i
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    var a = p\n    var b = q\n    let f = func [a, b] () -> ()\n        let r = keep(.Some(a@ref), b@ref)\n    f()\n    return keep(.Some(a@ref), b@ref)\n", "UnprovenOriginRelation_Kd@keep(.Some(a@ref), b")]
    // o2i
    [InlineData("func w2r(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) -> ref/i32 during k => $abort(\"no\")\nfunc use(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let a = q\n    let b = p\n    return w2r(.Some(a@ref), b@ref)\n", "UnprovenOriginRelation_Kd@w2r(.Some(a@ref), b@|UnprovenOriginRelation_Kd@a@ref), b@ref)")]
    // o3i
    [InlineData("func w2k(x: Option<ref/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) -> ref/i32 during k => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    var a = p\n    let f = func [a] (q: ref/(ref/i32)) -> ref/i32 during q\n        return w2k(.Some(a@ref), q)\n    return 0\n", "UnprovenOriginRelation_Kd@w2k(.Some(a@ref), q)|UnprovenOriginRelation_Kd@a@ref), q)")]
    // o4i
    [InlineData("func w4r(x: Option<ref/i32 during c>, y: ref/i32 during c) -> ref/i32 during c => $abort(\"no\")\nfunc use(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    let a = q\n    let b = p\n    return w4r(.Some(a), b)\n", "UnprovenOriginRelation_Kd@w4r(.Some(a), b)|UnprovenOriginRelation_Kd@a), b)")]
    // u1i
    [InlineData("func same3u<T>(x: Option<ref/T during c>, y: uniq/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var f = func [a, var b] () -> ()\n        same3u(.Some(a@ref), b@uniq)\n    f()\n    return 0\n", "UnprovenOriginRelation_Kd@a@ref), b@uniq)")]
    // o2u
    [InlineData("func w2u(x: Option<ref/(ref/i32 during k) during c>, y: uniq/(ref/i32 during k) during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var f = func [a, var b] () -> ()\n        w2u(.Some(a@ref), b@uniq)\n    f()\n    return 0\n", "UnprovenOriginRelation_Kd@a@ref), b@uniq)")]
    // w1i
    [InlineData("func same3w<T>(x: Option<uniq/T during c>, y: ref/T during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var f = func [var a, b] () -> ()\n        same3w(.Some(a@uniq), b@ref)\n    f()\n    return 0\n", "UnprovenOriginRelation_Kd@a@uniq), b@ref)|UnprovenOriginRelation_Kd@a@uniq), b@ref)")]
    // o2w
    [InlineData("func w2w(x: Option<uniq/(ref/i32 during k) during c>, y: ref/(ref/i32 during k) during c) => ()\nfunc use(p: ref/i32) -> i32\n    var a = p\n    var b = p\n    var f = func [var a, b] () -> ()\n        w2w(.Some(a@uniq), b@ref)\n    f()\n    return 0\n", "UnprovenOriginRelation_Kd@a@uniq), b@ref)|UnprovenOriginRelation_Kd@a@uniq), b@ref)")]
    // l1i
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use() -> i32\n    var x = 5\n    var y = 6\n    let a = x@ref\n    let b = y@ref\n    let r = keep(.Some(a@ref), b@ref)\n    y = 7\n    let s = r\n    return 0\n", "ComparisonLoanConflict_Kd@y = 7")]
    // l2i
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc use() -> i32\n    var x = 5\n    var y = 6\n    let a = x@ref\n    let b = y@ref\n    let r = keep(.Some(a@ref), b@ref)\n    x = 7\n    let s = r\n    return 0\n", "ComparisonLoanConflict_Kd@x = 7")]
    // fn1i
    [InlineData("func fun<T>(x: Option<T>, y: T) => ()\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    let g: (ref/i32 during p) -> () = func (v) => ()\n    let h: (ref/i32 during q) -> () = func (v) => ()\n    fun(.Some(h@move), g@move)\n    return 0\n", "UnprovenOriginContract_Kd@h@move), g@move)")]
    // af2
    [InlineData("func app<T, F>(g: F, y: T) -> i32\n    F is Callable<() -> T>\n    return 0\nfunc use(p: ref/i32, q: ref/i32) -> i32\n    let a = p\n    return app(func [a] () => a, q)\n", "UnprovenOriginRelation_Kd@a, q)")]
    // f4i, f5i (PLAN G1127 R1's Fill, the invalid twins of ALocalInputOriginIsNeverFilledByAnotherArgument's rows): the meet returned
    // `during b` needs `i outlives b` once, at the returned value.
    [InlineData("func pick3<T>(x: T, y: T) -> T => $abort(\"no\")\nfunc use(p: ref/(ref/i32 during b)) -> i32\n    let f = func [p] (q: ref/(ref/i32)) -> ref/i32 during b\n        let r = pick3(q, p)\n        return r@follow\n    return 0\n", "UnprovenOriginRelation_Kd@r@follow")]
    [InlineData("func pick3<T>(x: T, y: T) -> T => $abort(\"no\")\nfunc use(p: ref/(ref/i32 during b)) -> i32\n    let f = func [p] (q: ref/(ref/i32)) -> ref/i32 during b\n        let r = pick3(p, q)\n        return r@follow\n    return 0\n", "UnprovenOriginRelation_Kd@r@follow")]
    // j7 (PLAN G1127 R2, j1's invalid twin): the result returned `during b` needs `a outlives b` once (the region lies between b and a);
    // the call and the payload each record it, as for one leading-dot Case (G1115's S3: SPEC counts one, the Middle's R7-O1 area).
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc two(flag: bool, x: ref/(ref/i32 during a), y: ref/(ref/i32 during b)) -> ref/i32 during b\n    return keep(if flag => .Some(x) else => .None, y)\n", "UnprovenOriginRelation_Kd@keep(if flag => .Som|UnprovenOriginRelation_Kd@x) else => .None, y)")]
    // ja1 (the Middle's RP17 C2, ja2's invalid twin): `m = 5` while r, carrying the payload's Loan on m through the opened region, is
    // used: A5's conflict at the write (the base: BU-156's located Unsupported_Kd at the payload).
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc sink(v: ref/i32) -> i32 => 0\nfunc run(flag: bool) -> i32\n    var m: i32 = 1\n    var n: i32 = 2\n    let pm = m@ref\n    let pn = n@ref\n    let r = keep(if flag => .Some(pm@ref) else => .None, pn@ref)\n    m = 5\n    return sink(r)\n", "ComparisonLoanConflict_Kd@m = 5")]
    // j13 (R2's structure twin): a waiting source of another structure is its own Type mismatch, whatever the region.
    [InlineData("func keep<T>(x: Option<ref/T during c>, y: ref/T during c) -> T => $abort(\"no\")\nfunc two(flag: bool, y: ref/(ref/i32 during b), n: ref/(i64)) -> i32\n    let r = keep(if flag => .Some(n) else => .None, y)\n    return 0\n", "TypeMismatch_Kd@.Some(n) else => .No")]
    // rv18
    [InlineData("func keepO<T>(x: Option<T>, y: T) -> i32\n    T is Owned\n    return 0\nfunc one() -> ref/i32 during static => $abort(\"no\")\nfunc use(p: ref/i32) -> i32\n    return keepO(.Some(p), one())\n", "UnprovenOriginRelation_Kd@p), one())")]
    public void AWaitedVariableKeepsItsOtherBounds(string body, string records)
    {
        var source = body + Main;
        var check = DiagnosticCorpus.Check(source);
        Assert.Equal(records, string.Join("|", check.Diagnostics.Select(x => x.Code + "@" + At(source, x.Span))));
    }

    // PLAN G1127 R1's Fill (the code review's B1): an anonymous function parameter's omitted nested Origin is fixed in the Closure body
    // (SPEC 15 Nested signatures), so another argument's Origin never fills it at an open position (FillOpenOrigins fills only a
    // stand-in): the two covariant bindings of `pick3<T>(x: T, y: T)` meet in either order (SPEC 10.8: traversal order never chooses), as
    // their named twin's do. f4, f5; f4i and f5i above are their invalid twins.
    [Theory]
    // f4
    [InlineData("func pick3<T>(x: T, y: T) -> T => $abort(\"no\")\nfunc use(p: ref/(ref/i32 during b)) -> i32\n    let f = func [p] (q: ref/(ref/i32)) -> i32\n        let r = pick3(q, p)\n        return 0\n    return 0\n")]
    // f5
    [InlineData("func pick3<T>(x: T, y: T) -> T => $abort(\"no\")\nfunc use(p: ref/(ref/i32 during b)) -> i32\n    let f = func [p] (q: ref/(ref/i32)) -> i32\n        let r = pick3(p, q)\n        return 0\n    return 0\n")]
    public void ALocalInputOriginIsNeverFilledByAnotherArgument(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // The source from a span's start to the end of its line, at most 20 characters.
    private static string At(string source, SourceSpan? span)
    {
        var start = span?.Start ?? 0;
        var end = source.IndexOf('\n', start);
        return span is null ? string.Empty : source[start..Math.Min(end < 0 ? source.Length : end, start + 20)];
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
