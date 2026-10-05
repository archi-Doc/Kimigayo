// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Text.Json;
using Kimi;
using Kimi.Checking;
using Kimi.Diagnostics;
using Kimi.Lsp;
using Xunit;

namespace XunitTest;

// SPEC 15.3.3, 15.4.4, 15.6.1, 23.3.6.5: a local's written Type binds its slots by its attached clauses, and its initializer fits that
// Type. Each slot of the fit is an Origin relation by the slot's variance, `==` at an invariant position, reported with facts at the
// initializer; it was a fact-less UnprovenConstraint_Kd at the declaration (q34), hidden whenever Binding failed elsewhere (q30). A
// slot inside a Type argument follows the generic slot's variance, also at a value call's argument. A clause of a local, Field or Case
// that does not hold is a declared relation, never InvalidOriginBinding_Kd (q31 f2): it is located at the initializer when the
// initializer supplies one of its ends and relates the clause with the role `relation`. A local annotation also proves its Type's own
// clauses, each a declared relation at the Type occurrence.
public class LocalOriginClauseTest
{
    private const string Main = "public func main() => ()\n";

    private const string Holder = "struct H {s}\n    public let item: ref/i32 during s\n    public init(item: ref/i32 during s) => self.item = item\n\n";

    // Exclusive storage of a borrow makes `s` invariant; `t` is the outer, covariant slot.
    private const string Exclusive = "struct U {s, t}\n    origin s outlives t\n    public let item: uniq/(ref/i32 during s) during t\n    public init(item: uniq/(ref/i32 during s) during t) => self.item = item@move\n\n";

    // A stored function's input makes `s` contravariant.
    private const string Callback = "struct C {s}\n    public let call: (ref/i32 during s) -> i32\n    public init(call: (ref/i32 during s) -> i32) => self.call = call@move\n\n";

    // Generic slots by variance: exclusive storage of `T` makes it invariant, a stored function's input contravariant and owned storage
    // covariant, so the slots of a Type argument follow that position (SPEC 15.3.5).
    private const string Writer = "struct W<T> {t}\n    public let slot: uniq/T during t\n    public init(slot: uniq/T during t) => self.slot = slot@move\n\n";

    private const string Reader = "struct R<T>\n    public let call: (T) -> i32\n    public init(call: (T) -> i32) => self.call = call@move\n\n";

    private const string Box = "struct B<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n\n";

    private const string Payload = "struct V<T> {source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\n\n";

    // A Type with its own clause, a premise of its definition that each use proves.
    private const string Ordered = "struct S {a, b}\n    origin a outlives b\n    public let x: ref/i32 during a\n    public let y: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.x = x\n        self.y = y\n\n";

    // Local annotations that prove S's clause by a premise and by an initializer's Type, and a meet kept as a local's Origin, executed.
    private const string TypePremises = Holder + Ordered +
        "func premise(p: ref/i32, q: ref/i32, r: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin p outlives r\n    origin r outlives q\n    let s2: S{w} = s@move\n        origin w.a == r\n        origin w.b == q\n    return s2.x@follow * 10 + s2.y@follow\n" +
        "func inferred(p: ref/i32, q: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    let s2: S = s@move\n    let s3: S{w} = s2@move\n        origin w.a == p\n        origin w.b == q\n    return s3.x@follow * 10 + s3.y@follow\n" +
        "func pass(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives q\n    return premise(p, q, p, S.init(p, q)) + inferred(p, q, S.init(p, q))\n" +
        "func view(a: uniq/i32) -> i32\n    let h0 = H.init(a@ref)\n    let h: H{x} = h0@move\n    return h.item@follow\n" +
        "let one = 1\nlet two = 2\nrequire pass(one@ref, two@ref) == 24 else => $abort(\"type clause\")\nvar four = 4\nrequire view(four@uniq) == 4 else => $abort(\"meet\")\nConsole.writeLine(\"ok\")";

    // A Type argument's slots by the generic slot's variance (Holder, Box), a Case's clauses binding its payloads, a Field clause
    // proven by its Type's premise and a Type's own clause, executed.
    private const string Nested = Holder + Box + Payload +
        "func box<T>(value: T) -> B<T> => B<T>.init(value@move)\n\n" +
        "func covariant(a: ref/i32, b: ref/i32, b0: B<H{p}>) -> i32\n    origin p.s == a\n    origin a outlives b\n    let w: B<H{x}> = b0@move\n        origin x.s == b\n    return w.value.item@follow\n" +
        "enum E {a, b}\n    Both(V<i32>{v}, V<i32>{w})\n        origin v.source == a\n        origin w.source == b\n    Neither\n\n" +
        "struct Pair {a, b}\n    origin a outlives b\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b\n    let other: ref/i32 during b\n    public init(item: V<i32> during a, other: ref/i32 during b)\n        self.item = item@move\n        self.other = other\n    public func sum(self: ref/Self) -> i32 => self.item.value@follow + self.other@follow\n\n" +
        "func both(e: E) -> i32\n    match e\n        .Both(let v, let w) => return v.value@follow * 10 + w.value@follow\n        .Neither => return 0\n" +
        "let one = 1\nlet two = 2\nrequire covariant(one@ref, two@ref, box(H.init(one@ref))) == 1 else => $abort(\"covariant\")\n" +
        "require both(E.Both(V<i32>.init(one@ref), V<i32>.init(two@ref))) == 12 else => $abort(\"case\")\n" +
        "require Pair.init(V<i32>.init(two@ref), two@ref).sum() == 4 else => $abort(\"field\")\nConsole.writeLine(\"ok\")";

    private const string Executed = Holder +
        "func same(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == a\n    return h.item\n" +
        "func premise(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == b\n    return h.item\n" +
        "func meet(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin b outlives x.s\n    return h.item@follow\n" +
        "let one = 1\nlet two = 2\nrequire same(one@ref, two@ref)@follow == 1 and premise(one@ref, two@ref)@follow == 1 and meet(one@ref, two@ref) == 1 else => $abort(\"local clause\")\nConsole.writeLine(\"ok\")";

    [Theory]
    // q30 f: `x.s == b` binds the slot, so the initializer's `a` must outlive it.
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == b\n    return 0\n", "outlives", "requires a outlives b, which is not proven")]
    // q34: the same with a use; it was a fact-less UnprovenConstraint_Kd at the whole declaration.
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == b\n    return h.item\n", "outlives", "requires a outlives b, which is not proven")]
    // A contravariant slot reverses the relation.
    [InlineData(Callback + "func f(a: ref/i32, b: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s == b\n    return 0\n", "outlives", "requires b outlives a, which is not proven")]
    // An invariant slot is one `==` relation. `b outlives slot` proves U's own clause for the local's Type, U<b, slot>, which is an
    // independent problem otherwise (ATypeClauseAndASlotFitAreIndependentProblems).
    [InlineData(Exclusive + "func f(a: ref/i32, b: ref/i32, slot: uniq/(ref/i32 during a)) -> i32\n    origin b outlives slot\n    let h0 = U.init(slot)\n    let h: U{x} = h0@move\n        origin x.s == b\n    return 0\n", "==", "requires a == b, which is not proven")]
    // With `a outlives b` proven, only the reverse direction fails, and the record shows that direction.
    [InlineData(Exclusive + "func f(a: ref/i32, b: ref/i32, slot: uniq/(ref/i32 during a)) -> i32\n    origin a outlives b\n    origin b outlives slot\n    let h0 = U.init(slot)\n    let h: U{x} = h0@move\n        origin x.s == b\n    return 0\n", "==", "requires b == a, which is not proven")]
    public void ASlotFitIsARelationAtTheInitializer(string body, string relation, string label)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), DiagnosticCategory.Proof), (error.Code, error.Category));
        Assert.Equal(("h0@move", label), (Text(source, error.Span), error.Label));
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal((relation, "fit"), (reason[0].Value, reason[3].Value));
        Assert.All(reason[1..3], static x => Assert.Equal(("expression", DiagnosticValueKind.Origin), (x.Origin, x.Kind)));
        Assert.Null(error.Related);
        Assert.Contains(relation == "==" ? "Use one Origin at both positions" : "add 'origin ", error.Advice, StringComparison.Ordinal);
    }

    // SPEC 15.3.5, 15.6.1: a Type argument's slots follow the variance of the generic slot that holds it, composed with the enclosing
    // position: `==` under an invariant one (W stores `uniq/T`) and reversed under a contravariant one (R reads `(T) -> i32`), at a
    // local annotation and at a value call's argument alike. They were judged covariantly, so these were accepted (the value calls
    // under a premise, the rest outright), and a write of a shorter H through W failed only at generation.
    [Theory]
    [InlineData(Writer + "func f(a: ref/i32, b: ref/i32, w0: W<H{p}>) -> i32\n    origin p.s == a\n    let w: W<H{x}> = w0@move\n        origin x.s == b\n    return 0\n", "w0@move", "==", "requires a == b, which is not proven")]
    [InlineData(Writer + "func f(a: ref/i32, b: ref/i32, w0: W<H{p}>) -> i32\n    origin p.s == a\n    origin a outlives b\n    var w: W<H{x}> = w0@move\n        origin x.s == b\n    w.slot@follow = H.init(b)\n    return 0\n", "w0@move", "==", "requires b == a, which is not proven")]
    [InlineData(Reader + "func f(a: ref/i32, b: ref/i32, c0: R<H{p}>) -> i32\n    origin p.s == a\n    origin a outlives b\n    let c: R<H{x}> = c0@move\n        origin x.s == b\n    return (c.call)(H.init(b))\n", "c0@move", "outlives", "requires b outlives a, which is not proven")]
    // An exclusive target under a contravariant slot is invariant: the reversed outer `q outlives p` holds, and the target's slot
    // needs `==`. Its fit is no obligation (CheckTypeUse needs an identical exclusive target) and goes to RecordMismatch, which named
    // the covariant "p outlives q", a relation the position does not need, with that clause as Advice (p2e).
    [InlineData(Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/H{hw} during p>) -> i32\n    origin hw.s == a\n    origin a outlives b\n    origin q outlives p\n    origin b outlives q\n    let r: R<uniq/H{x} during q> = r0@move\n        origin x.s == b\n    return 0\n", "r0@move", "==", "requires a == b, which is not proven")]
    // A value call's argument: covariant (it was a fact-less UnprovenConstraint_Kd), invariant and contravariant.
    [InlineData("func use(a: ref/i32, b: ref/i32, h0: H{p}, k: (H{q}) -> i32) -> i32\n    origin p.s == a\n    origin q.s == b\n    return k(h0@move)\n", "h0@move", "outlives", "requires a outlives b, which is not proven")]
    [InlineData(Writer + "func use(a: ref/i32, b: ref/i32, w0: W<H{p}> during b, k: (W<H{q}> during b) -> i32) -> i32\n    origin p.s == a\n    origin q.s == b\n    origin a outlives b\n    return k(w0@move)\n", "w0@move", "==", "requires b == a, which is not proven")]
    [InlineData(Reader + "func use(a: ref/i32, b: ref/i32, c0: R<H{p}>, k: (R<H{q}>) -> i32) -> i32\n    origin p.s == a\n    origin q.s == b\n    origin a outlives b\n    return k(c0@move)\n", "c0@move", "outlives", "requires b outlives a, which is not proven")]
    public void ATypeArgumentsSlotFollowsItsPosition(string body, string at, string relation, string label)
    {
        var source = Holder + body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), DiagnosticCategory.Proof), (error.Code, error.Category));
        Assert.Equal((at, label), (Text(source, error.Span), error.Label));
        var reason = error.Reason!;
        Assert.Equal(["relation", "longer", "shorter", "source", "destination"], reason.Select(static x => x.Name));
        Assert.Equal((relation, "fit"), (reason[0].Value, reason[3].Value));
        Assert.Contains(relation == "==" ? "Use one Origin at both positions" : "add 'origin ", error.Advice, StringComparison.Ordinal);
    }

    // The whole program behind the write through W: a caller passes a shorter `b`, so `f` stores a borrow of `two` where its caller's
    // Type promises `one`'s Origin. Only `f`'s relation is reported; it was accepted and failed at generation.
    [Fact]
    public void AWriteThroughAnInvariantTypeArgumentIsNotAccepted()
    {
        var source = Holder + Writer + "func wrap<T>(slot: uniq/T) -> W<T>\n    return W<T>.init(slot)\n\n" +
            "func f(a: ref/i32, b: ref/i32, w0: W<H{hw}>) -> i32\n    origin hw.s == a\n    origin a outlives b\n    var w: W<H{x}> = w0@move\n        origin x.s == b\n    w.slot@follow = H.init(b)\n    return 0\n\n" +
            "func g(a: ref/i32, w0: W<H{hw}>) -> i32\n    origin hw.s == a\n    let two = 222\n    return f(a, two@ref, w0@move)\n\n" +
            "public func main()\n    let one = 1\n    var holder = H.init(one@ref)\n    let r = g(one@ref, wrap(holder@uniq))\n    Console.writeLine(\"\\(holder.item@follow) \\(r)\")\n";
        var output = DiagnosticCorpus.Check(source);
        var error = Assert.Single(output.Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "w0@move", "requires b == a, which is not proven"), (error.Code, Text(source, error.Span), error.Label));
        Assert.Equal(source.IndexOf("var w: W<H{x}> = w0@move", StringComparison.Ordinal) + 17, error.Span!.Value.Start);
    }

    // q30: a bare initializer that must be moved and the slot relation are independent problems at the same value.
    [Fact]
    public void ATransferAndTheSlotRelationAreBothReported()
    {
        var source = Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0\n        origin x.s == b\n    return 0\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal([nameof(DiagnosticCode.TransferRequired_Kd), nameof(DiagnosticCode.UnprovenOriginRelation_Kd)], errors.Select(static x => x.Code));
        Assert.All(errors, x => Assert.Equal("h0", Text(source, x.Span)));
    }

    [Theory]
    // q31 f2: the inferred slot is the initializer's `a`, which the clause requires to outlive `b`.
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h0@move", "requires a outlives b, which is not proven", null, "origin x.s outlives b", "add 'origin a outlives b' to the clauses of the enclosing function")]
    // An invariant inferred slot equals the initializer's Origin, so an upper bound must outlive it.
    [InlineData(Exclusive + "func f(a: ref/i32, b: ref/i32, slot: uniq/(ref/i32 during a)) -> i32\n    let h0 = U.init(slot)\n    let h: U{x} = h0@move\n        origin b outlives x.s\n    return 0\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h0@move", "requires b outlives a, which is not proven", null, "origin b outlives x.s", "add 'origin b outlives a' to the clauses of the enclosing function")]
    // A body-local Borrow can never outlive a fixed Origin.
    [InlineData(Holder + "func f(a: ref/i32) -> i32\n    let local = 3\n    let h: H{x} = H.init(local@ref)\n        origin x.s outlives a\n    return h.item@follow\n", nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), "H.init(local@ref)", "requires the borrow local@ref outlives a, which is false", "local@ref", "origin x.s outlives a", "Return or store an owned value")]
    // A Field's clause has no initializer: it is located at the clause, which names the Type's slots as written there.
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nstruct Wrong {a, b}\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b\n\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "origin a outlives b", "requires a outlives b, which is not proven", null, "origin a outlives b", "add 'origin a outlives b' to the clauses of the enclosing Type")]
    // So is a Case's clause, which binds its payload's set like a Field's (it was MissingOriginBinding_Kd at the payload).
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nenum E {a, b}\n    One(V<i32>{v})\n        origin v.source == a\n        origin a outlives b\n    Two(ref/i32 during b)\n\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "origin a outlives b", "requires a outlives b, which is not proven", null, "origin a outlives b", "add 'origin a outlives b' to the clauses of the enclosing Type")]
    // A method's local clause names the receiver's slots through `self`, which the method's own clauses can relate.
    [InlineData(Holder + "struct S {a, b}\n    public let first: ref/i32 during a\n    public let second: ref/i32 during b\n    public func view(self: ref/Self) -> i32\n        let h0 = H.init(self.second)\n        let h: H{x} = h0@move\n            origin x.s outlives self.a\n        return h.item@follow\n\n", nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h0@move", "requires self.b outlives self.a, which is not proven", null, "origin x.s outlives self.a", "add 'origin self.b outlives self.a' to the clauses of the enclosing function")]
    public void AClauseThatDoesNotHoldIsADeclaredRelation(string body, string code, string at, string label, string? borrow, string clause, string advice)
        => AssertDeclared(body + Main, code, at, label, borrow, clause, advice);

    // SPEC 7.6.1, 15.3.3: an anonymous function has no origin clauses, so a local clause over its own Origins is offered the bound on
    // its input, and one over an enclosing function's captured Origins that function's clauses; neither suggests an impossible clause.
    [Theory]
    [InlineData(Holder + "public func main()\n    let one = 1\n    let two = 2\n    let g = func (a: ref/i32, b: ref/i32) -> i32\n        let h0 = H.init(a)\n        let h: H{x} = h0@move\n            origin x.s outlives b\n        return 0\n    Console.writeLine(\"\\(g(one@ref, two@ref))\")\n", "requires a outlives b, which is not proven", "origin x.s outlives b", "An anonymous function has no origin clauses; write the input as 'a: ref/i32 during b' so that it accepts only borrows that outlive b, or remove this clause")]
    [InlineData(Holder + "func outer(p: ref/i32, q: ref/i32) -> i32\n    let g = func [p, q] () -> i32\n        let h0 = H.init(p)\n        let h: H{x} = h0@move\n            origin x.s outlives q\n        return 0\n    return g()\n" + Main, "requires p outlives q, which is not proven", "origin x.s outlives q", "If p always outlives q, add 'origin p outlives q' to the clauses of the enclosing function 'outer', which changes its public contract, or remove this clause")]
    public void AClauseInAnAnonymousFunctionIsOfferedOnlyApplicableAdvice(string source, string label, string clause, string advice)
    {
        var error = AssertDeclared(source, nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h0@move", label, null, clause, advice);
        Assert.Equal(advice, error.Advice);
    }

    // An `==` clause is offered an `==` premise where one can be stated, and in an anonymous function only its removal, since an input
    // bound states one direction; it was offered "bind b to a where it is introduced", which a Type slot or parameter cannot do.
    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i32) -> i32\n    let x: ref/i32 during a = a\n        origin a == b\n    return x@follow\n" + Main, "If a and b are always equal, add 'origin a == b' to the clauses of the enclosing function, which changes its public contract, or remove this clause")]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nstruct Wrong<T> {a, b}\n    let item: V<T>{v}\n        origin v.source == a\n        origin a == b\n\n" + Main, "If a and b are always equal, add 'origin a == b' to the clauses of the enclosing Type, which changes its public contract, or remove this clause")]
    [InlineData("public func main()\n    let one = 1\n    let g = func (a: ref/i32, b: ref/i32) -> i32\n        let x: ref/i32 during a = a\n            origin a == b\n        return x@follow\n    Console.writeLine(\"\\(g(one@ref, one@ref))\")\n", "An anonymous function has no origin clauses that could establish this relation; remove this clause")]
    public void ADeclaredEqualityIsOfferedAnEqualityPremise(string source, string advice)
    {
        var error = AssertDeclared(source, nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "origin a == b", "requires a == b, which is not proven", null, "origin a == b", advice);
        Assert.Equal(advice, error.Advice);
    }

    // SPEC 15.3.6, 15.4.4: the Milestone 13 and 28 clause shapes, a slot equal to its initializer's or proven by a premise, a covariant
    // inferred slot bounded by both the initializer and a clause (the meet of its upper bounds), and a contravariant slot whose
    // relation a premise proves.
    [Theory]
    [InlineData("struct Cursor<T> {source}\n    let values: Slice<T>{slice}\n        origin slice.source == source\n    public init(values: Slice<T>)\n        origin values.source == source\n        self.values = values\n\n")]
    [InlineData("struct View<T> {source}\n    let values: Slice<T>{slice}\n        origin slice.source == source\n    public init(values: Slice<T>)\n        origin values.source == source\n        self.values = values\n    public func take(self: Self) -> Slice<T>{result}\n        origin result.source == source\n        let local: Slice<T>{v} = self.values\n            origin v.source == source\n        return local\n\n")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> ref/i32 during a\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == a\n    return h.item\n")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> ref/i32 during b\n    origin a outlives b\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s == b\n    return h.item\n")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin b outlives x.s\n    return h.item@follow\n")]
    [InlineData(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    origin a outlives b\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return h.item@follow\n")]
    [InlineData(Callback + "func f(a: ref/i32, b: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    origin b outlives a\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s == b\n    return 0\n")]
    // A Type argument at an invariant position with equal slots, at a contravariant one under the reversed premise, and at value
    // call arguments of both.
    [InlineData(Holder + Writer + "func f(a: ref/i32, b: ref/i32, w0: W<H{p}>) -> i32\n    origin p.s == a\n    var w: W<H{x}> = w0@move\n        origin x.s == a\n    w.slot@follow = H.init(a)\n    return w.slot@follow.item@follow\n")]
    [InlineData(Holder + Reader + "func f(a: ref/i32, b: ref/i32, c0: R<H{p}>) -> i32\n    origin p.s == a\n    origin b outlives a\n    let c: R<H{x}> = c0@move\n        origin x.s == b\n    return (c.call)(H.init(b))\n")]
    [InlineData(Holder + Writer + "func use(a: ref/i32, w0: W<H{p}> during a, k: (W<H{q}> during a) -> i32) -> i32\n    origin p.s == a\n    origin q.s == a\n    return k(w0@move)\n")]
    [InlineData(Holder + Reader + "func use(a: ref/i32, c0: R<H{p}>, k: (R<H{q}>) -> i32) -> i32\n    origin p.s == a\n    origin q.s == a\n    return k(c0@move)\n")]
    // An exclusive target under a contravariant slot with equal target slots and the reversed outer premise (p2e's counterpart).
    [InlineData(Holder + Reader + "func f(a: ref/i32, b: ref/i32, p: ref/i32, q: ref/i32, r0: R<uniq/H{hw} during p>) -> i32\n    origin hw.s == a\n    origin a == b\n    origin q outlives p\n    origin b outlives q\n    let r: R<uniq/H{x} during q> = r0@move\n        origin x.s == b\n    return 0\n")]
    // The Advice of an anonymous function's clause applied: the input bounds itself, or the enclosing function states the premise.
    [InlineData(Holder + "func f() -> i32\n    let g = func (a: ref/i32 during b, b: ref/i32) -> i32\n        let h0 = H.init(a)\n        let h: H{x} = h0@move\n            origin x.s outlives b\n        return h.item@follow\n    return 0\n")]
    [InlineData(Holder + "func outer(p: ref/i32, q: ref/i32) -> i32\n    origin p outlives q\n    let g = func [p, q] () -> i32\n        let h0 = H.init(p)\n        let h: H{x} = h0@move\n            origin x.s outlives q\n        return h.item@follow\n    return g()\n")]
    // The `==` premise applied to a local's clause.
    [InlineData("func f(a: ref/i32, b: ref/i32) -> i32\n    origin a == b\n    let x: ref/i32 during a = a\n        origin a == b\n    return x@follow\n")]
    public void AProvenLocalRelationIsAccepted(string body)
    {
        var output = DiagnosticCorpus.Check(body + Main);
        Assert.True(output.Accepted, string.Join("\n", output.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 15.3.3: the Advice of a Field's or Case's clause applied: the Type states the premise in its Constraint region. A Type's
    // own clause was a fallback PrerequisiteUnavailable_Kd in every program without another Error, since control flow read it as an
    // untyped expression; a Phantom slot's clause is accepted too.
    [Theory]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nstruct Right {a, b}\n    origin a outlives b\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b\n    let other: ref/i32 during b\n\n")]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nenum E {a, b}\n    origin a outlives b\n    One(V<i32>{v})\n        origin v.source == a\n        origin a outlives b\n    Two(ref/i32 during b)\n\n")]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nstruct Wrong {a, b}\n    origin a outlives b\n    let item: V<i32>{v}\n        origin v.source == a\n        origin a outlives b\n\n")]
    [InlineData("struct V<T> {source}\n    public let value: ref/T during source\n\nstruct Wrong<T> {a, b}\n    origin a == b\n    let item: V<T>{v}\n        origin v.source == a\n        origin a == b\n\n")]
    [InlineData(Exclusive)]
    [InlineData("struct S {a, b}\n    origin a outlives b\n    public let first: ref/i32 during a\n\n")]
    [InlineData("enum E {a, b}\n    origin a outlives b\n    One(ref/i32 during a)\n    Two\n\n")]
    // A Case's set bound by its clauses, with one and with two payloads.
    [InlineData(Payload + "enum E {a}\n    One(V<i32>{v})\n        origin v.source == a\n    Two\n\n")]
    [InlineData(Payload + "enum E {a, b}\n    Both(V<i32>{v}, V<i32>{w})\n        origin v.source == a\n        origin w.source == b\n    Neither\n\n")]
    public void ATypeLevelClauseIsAccepted(string body)
    {
        var output = DiagnosticCorpus.Check(body + Main);
        Assert.True(output.Accepted, string.Join("\n", output.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // A Type's own clause stays a premise that every formation of the Type must prove.
    [Fact]
    public void ATypePremiseIsRequiredWhereTheTypeIsFormed()
    {
        var source = "struct S {a, b}\n    origin a outlives b\n    public let first: ref/i32 during a\n    public let second: ref/i32 during b\n    public init(first: ref/i32 during a, second: ref/i32 during b)\n        self.first = first\n        self.second = second\n\n" +
            "func make(p: ref/i32, q: ref/i32) -> S{s}\n    origin s.a == p\n    origin s.b == q\n    return S.init(p, q)\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnprovenConstraint_Kd), "S.init(p, q)"), (error.Code, Text(source, error.Span)));
    }

    // SPEC 15.3.3, 15.6.1: a local annotation proves its Type's own clauses substituted with the Origins it binds; each that does not
    // hold is a declared relation at the Type occurrence, relating the Type's clause, also for a Type nested in a Type argument and in
    // a method, whose receiver slots are shown through `self`. The local's Type is never a premise of the body. These were accepted
    // once a Type's own clause stopped being a fallback PrerequisiteUnavailable_Kd (tp1).
    [Theory]
    [InlineData("func f(p: ref/i32, q: ref/i32, r: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin p outlives r\n    let s2: S{w} = s@move\n        origin w.a == r\n        origin w.b == q\n    return s2.x@follow + s2.y@follow\n", "S{w}", "requires r outlives q, which is not proven", "If r always outlives q, add 'origin r outlives q' to the clauses of the enclosing function, which changes its public contract, or give this S Origins that satisfy its clause")]
    [InlineData("func f(p: ref/i32, q: ref/i32, r: ref/i32, s: Option<S{u}>) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin p outlives r\n    let s2: Option<S{w}> = s@move\n        origin w.a == r\n        origin w.b == q\n    return 0\n", "Option<S{w}>", "requires r outlives q, which is not proven", "If r always outlives q, add 'origin r outlives q' to the clauses of the enclosing function, which changes its public contract, or give this S Origins that satisfy its clause")]
    [InlineData("struct T {c, d}\n    origin c outlives d\n    public let s: S{z}\n        origin z.a == c\n        origin z.b == c\n    public let e: ref/i32 during d\n    public func take(self: owner/Self) -> i32\n        let s2: S{w} = self.s@move\n            origin w.a == self.d\n            origin w.b == self.c\n        return 0\n\n", "S{w}", "requires self.d outlives self.c, which is not proven", "If self.d always outlives self.c, add 'origin self.d outlives self.c' to the clauses of the enclosing function, which changes its public contract, or give this S Origins that satisfy its clause")]
    public void ATypesClauseIsADeclaredRelationWhereALocalNamesIt(string body, string at, string label, string advice)
    {
        var source = Ordered + body + Main;
        var error = AssertDeclared(source, nameof(DiagnosticCode.UnprovenOriginRelation_Kd), at, label, null, "origin a outlives b", advice);
        Assert.Equal(advice, error.Advice);
        Assert.Equal(source.IndexOf("let s2: " + at, StringComparison.Ordinal) + 8, error.Span!.Value.Start);
        Assert.Equal(source.IndexOf("origin a outlives b", StringComparison.Ordinal), error.Related![0].Span!.Value.Start);
    }

    // An `==` clause of the Type is an `==` relation at the occurrence.
    [Fact]
    public void ATypesEqualityClauseIsAnEqualityAtTheOccurrence()
    {
        var source = Ordered.Replace("origin a outlives b", "origin a == b", StringComparison.Ordinal) +
            "func f(p: ref/i32, q: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == p\n    origin p outlives q\n    let s2: S{w} = s@move\n        origin w.a == p\n        origin w.b == q\n    return 0\n" + Main;
        var error = AssertDeclared(source, nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "S{w}", "requires q == p, which is not proven", null, "origin a == b", "If q and p are always equal, add 'origin q == p' to the clauses of the enclosing function, which changes its public contract, or give this S Origins that satisfy its clause");
        Assert.Equal(source.IndexOf("let s2: S{w}", StringComparison.Ordinal) + 8, error.Span!.Value.Start);
    }

    // SPEC 15.6.1: a failed well-formedness of a destination Type and a failed fit into it are independent problems: U's own clause
    // needs `b outlives slot` for the local's U<b, slot>, and its invariant slot needs `a == b`.
    [Fact]
    public void ATypeClauseAndASlotFitAreIndependentProblems()
    {
        var source = Exclusive + "func f(a: ref/i32, b: ref/i32, slot: uniq/(ref/i32 during a)) -> i32\n    let h0 = U.init(slot)\n    let h: U{x} = h0@move\n        origin x.s == b\n    return 0\n" + Main;
        var errors = DiagnosticCorpus.Check(source).Diagnostics;
        Assert.Equal(
            [(nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "U{x}", "requires b outlives slot, which is not proven", "declared"), (nameof(DiagnosticCode.UnprovenOriginRelation_Kd), "h0@move", "requires a == b, which is not proven", "fit")],
            errors.Select(x => (x.Code, Text(source, x.Span), x.Label!, x.Reason![3].Value)));
        Assert.Equal(source.IndexOf("let h: U{x}", StringComparison.Ordinal) + 7, errors[0].Span!.Value.Start);
    }

    // The Advice applied, the premise in the enclosing function or in the method, other Origins for the Type, Origins inferred from
    // an initializer whose Type proves the clause, and the meet of a parameter with its Borrow, kept as the local's Origin.
    [Theory]
    [InlineData("func f(p: ref/i32, q: ref/i32, r: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin p outlives r\n    origin r outlives q\n    let s2: S{w} = s@move\n        origin w.a == r\n        origin w.b == q\n    return s2.x@follow + s2.y@follow\n")]
    [InlineData("func f(p: ref/i32, q: ref/i32, r: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin p outlives r\n    let s2: S{w} = s@move\n        origin w.a == p\n        origin w.b == q\n    return s2.x@follow + s2.y@follow\n")]
    [InlineData("func f(p: ref/i32, q: ref/i32, s: S{u}) -> i32\n    origin u.a == p\n    origin u.b == q\n    let s2: S = s@move\n    let s3: S{w} = s2@move\n        origin w.a == p\n        origin w.b == q\n    return s3.x@follow + s3.y@follow\n")]
    [InlineData("struct T {c, d}\n    origin c outlives d\n    public let s: S{z}\n        origin z.a == c\n        origin z.b == c\n    public let e: ref/i32 during d\n    public func take(self: owner/Self) -> i32\n        origin self.d outlives self.c\n        let s2: S{w} = self.s@move\n            origin w.a == self.d\n            origin w.b == self.c\n        return 0\n\n")]
    [InlineData(Holder + "func f(a: uniq/i32) -> i32\n    let h0 = H.init(a@ref)\n    let h: H{x} = h0@move\n    return h.item@follow\n")]
    public void ATypesClauseThatALocalProvesIsAccepted(string body)
    {
        var output = DiagnosticCorpus.Check(Ordered + body + Main);
        Assert.True(output.Accepted, string.Join("\n", output.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // Those forms run: the premise and the inferred Origins each read 1 and 2, and the meet reads 4.
    [Fact]
    public void ATypesClauseThatALocalProvesExecutes()
        => ScalarEmissionTest.EmitFixture("LocalOriginClauseTypePremise", TypePremises, "ok\n");

    // SPEC 15.3.3, 23.3.6.4: a Case whose payload Type failed leaves its clauses unbound, as a Field's does, so they add no record
    // derived from that failure (k1 added a Language InvalidOriginBinding_Kd at `v.source`), also beside a payload that bound.
    [Theory]
    [InlineData("enum E {a}\n    One(Missing<i32>{v})\n        origin v.source == a\n    Two\n\n")]
    [InlineData(Payload + "enum E {a}\n    One(V<i32>{w}, Missing<i32>{v})\n        origin v.source == a\n        origin w.source == a\n    Two\n\n")]
    public void AFailedPayloadTypeAddsNoClauseRecord(string body)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnresolvedBinding_Kd), "Missing", source.IndexOf("Missing", StringComparison.Ordinal)), (error.Code, Text(source, error.Span), error.Span!.Value.Start));
    }

    // SPEC 23.3.6.5: a meet's body-local operand is shown by the Borrow that supplies it, found through the local the value reads or
    // moves; it was the text of the whole function that binds the borrowed parameter's Place. A declared relation keeps the meet whole;
    // a fit names the failing operand that decides its judgment, the Refuted Borrow before the Unknown `a` (SPEC 15.3.6, 15.6.5).
    [Theory]
    [InlineData("func f(a: uniq/i32) -> i32\n    let h0 = H.init(a@ref)\n    let h: H{x} = h0@move\n        origin x.s outlives static\n    return 0\n", "h0@move", "(a and a@ref)")]
    [InlineData("func f(a: uniq/i32) -> ref/i32 during static\n    let h0 = H.init(a@ref)\n    return h0.item\n", "h0.item", "a@ref")]
    public void AMeetShowsTheBorrowOfItsBodyLocalOperand(string body, string at, string longer)
    {
        var source = Holder + body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), DiagnosticCategory.Language), (error.Code, error.Category));
        var shown = longer.StartsWith('(') ? longer : "the borrow " + longer;
        Assert.Equal((at, $"requires {shown} outlives static, which is false"), (Text(source, error.Span), error.Label));
        Assert.Equal(source.IndexOf(at, source.IndexOf("let h0", StringComparison.Ordinal) + 6, StringComparison.Ordinal), error.Span!.Value.Start);
        Assert.Equal((longer, "static"), (error.Reason![1].Value, error.Reason[2].Value));
    }

    // The accepted forms run: each returns the value behind `a`.
    [Fact]
    public void LocalSlotClausesExecute()
        => ScalarEmissionTest.EmitFixture("LocalOriginClause", Executed, "ok\n");

    // A covariant Type argument's slot, a Case's two payload sets and a Field clause under its Type's premise run.
    [Fact]
    public void NestedAndTypeLevelClausesExecute()
        => ScalarEmissionTest.EmitFixture("LocalOriginClauseNested", Nested, "ok\n");

    // SPEC 15.6.5: the meet keeps the clause's Borrow, so its Loan stays live while the local is.
    [Fact]
    public void AnUpperBoundClauseKeepsItsLoan()
    {
        var source = Holder + "func f() -> i32\n    let v = 1\n    var w = 2\n    let r = w@ref\n    let h: H{x} = H.init(v@ref)\n        origin r outlives x.s\n    w = 5\n    return h.item@follow\n" + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.ComparisonLoanConflict_Kd), "w = 5"), (error.Code, Text(source, error.Span)));
    }

    [Theory]
    // A slot chain between two body Borrows needs region inference: the located limit at the value, never accepted or a Proof record.
    [InlineData(Holder + "func f() -> i32\n    let v = 1\n    let w = 2\n    let r = w@ref\n    let h0 = H.init(v@ref)\n    let h: H{x} = h0@move\n        origin x.s == r\n    return h.item@follow\n", nameof(DiagnosticCode.UnsupportedOwnership_Kd), "h0@move")]
    // So is a clause between two body Borrows, at the clause; `h` keeps only `v@ref`'s Loan, so writing `w` is no conflict.
    [InlineData(Holder + "func f() -> i32\n    let v = 1\n    var w = 2\n    let r = w@ref\n    let h: H{x} = H.init(v@ref)\n        origin x.s outlives r\n    w = 5\n    return h.item@follow\n", nameof(DiagnosticCode.UnsupportedOwnership_Kd), "origin x.s outlives r")]
    // A contravariant slot's principal solution is a lower bound outliving every other, which local inference does not choose.
    [InlineData(Callback + "func f(a: ref/i32, b: ref/i32, g: (ref/i32 during a) -> i32) -> i32\n    let h0 = C.init(g@move)\n    let h: C{x} = h0@move\n        origin x.s outlives b\n    return 0\n", nameof(DiagnosticCode.UnsupportedBinding_Kd), "origin x.s outlives b")]
    public void AnUnrepresentableLocalRelationIsALocatedLimit(string body, string code, string at)
    {
        var source = body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((code, at), (error.Code, Text(source, error.Span)));
    }

    // Without an initializer an outlives clause cannot determine the slot (SPEC 15.4.4); its relation rests on that failure and adds
    // no record of its own (it was a second InvalidOriginBinding_Kd at the clause).
    [Theory]
    [InlineData("func f(x: ref/i32)\n    var pending: V<i32>{p}\n        origin p.source outlives x\n")]
    [InlineData("func f(x: V<i32>, y: V<i32>)\n    var pending: V<i32>{p}\n        origin x.source outlives p.source\n")]
    public void AnUndeterminedSlotIsOneRecord(string body)
    {
        var source = "struct V<T> {source}\n    public let value: ref/T during source\n\n" + body + Main;
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal((nameof(DiagnosticCode.MissingOriginBinding_Kd), "V<i32>{p}"), (error.Code, Text(source, error.Span)));
    }

    // p31: a call initializer whose result slot the clause binds is still rejected; its relation is never dropped.
    [Fact]
    public void ACallInitializerBoundByAClauseIsNotAccepted()
    {
        var output = DiagnosticCorpus.Check(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h: H{x} = H.init(a)\n        origin x.s == b\n    return 0\n" + Main);
        Assert.False(output.Accepted);
        Assert.NotEmpty(output.Diagnostics);
    }

    // SPEC 23.3.6.8: the console, the language server and JSON carry the declared source and the clause's related location.
    [Fact]
    public void EveryOutputCarriesTheDeclaredClause()
    {
        var source = Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin x.s outlives b\n    return 0\n" + Main;
        var output = DiagnosticCorpus.Check(source);
        var record = Assert.Single(output.Diagnostics);
        var result = new DiagnosticResult(output.Diagnostics, output.Sources);
        var console = new DiagnosticContractTest.DiagnosticConsole();
        new Kimigayo(console).Render(result, string.Empty);
        Assert.Contains("UnprovenOriginRelation_Kd", console.Text, StringComparison.Ordinal);
        Assert.Contains("requires a outlives b, which is not proven", console.Text, StringComparison.Ordinal);
        Assert.Contains(" = relation: ", console.Text, StringComparison.Ordinal);

        var identity = SourceIdentity.FromPath(output.Sources[record.Source].Path);
        var plain = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, false)[identity]);
        Assert.Equal((record.Code, record.Display!.Range), (plain.Code, plain.Range));
        var related = Assert.Single(WorkspaceCheck.Place(output, [identity], identity, true)[identity]);
        Assert.Equal(record.Related![0].Range, Assert.Single(related.RelatedInformation!).Location.Range);

        var json = JsonSerializer.Serialize(result, DiagnosticJsonContext.Default.DiagnosticResult);
        Assert.Contains("{\"name\":\"source\",\"kind\":\"Text\",\"value\":\"declared\",\"elided\":false}", json, StringComparison.Ordinal);
        Assert.Contains("\"related\":[{\"role\":\"relation\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"name\":\"destination\"", json, StringComparison.Ordinal);
        Assert.Equal(result, JsonSerializer.Deserialize(json, DiagnosticJsonContext.Default.DiagnosticResult));
    }

    // A warm rebind of locals whose clauses read inferred slots, with an upper-bound meet, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmLocalClauseBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Holder + "func f(a: ref/i32, b: ref/i32) -> i32\n    let h0 = H.init(a)\n    let h: H{x} = h0@move\n        origin b outlives x.s\n    let k: H{y} = H.init(a)\n        origin y.s outlives a\n    return h.item@follow + k.item@follow\n" + Main);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Local clause binding failed.");
            }
        }));
    }

    // A warm rebind of locals whose Types' own clauses become obligations, nested in a Type argument too, allocates nothing.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmTypeClauseBindingAllocatesNothing()
    {
        var c = MinimalEmissionTest.Analyze(Ordered + "func f(p: ref/i32, q: ref/i32, r: ref/i32, s: S{u}, o: Option<S{v}>) -> i32\n    origin u.a == p\n    origin u.b == q\n    origin v.a == p\n    origin v.b == q\n    origin p outlives r\n    origin r outlives q\n    let s2: S{w} = s@move\n        origin w.a == r\n        origin w.b == q\n    let o2: Option<S{z}> = o@move\n        origin z.a == r\n        origin z.b == q\n    return s2.x@follow\n" + Main);
        for (var i = 0; i < 100; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Type clause binding failed.");
            }
        }));
    }

    private static CheckDiagnostic AssertDeclared(string source, string code, string at, string label, string? borrow, string clause, string advice)
    {
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(code, error.Code);
        Assert.Equal(code == nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) ? DiagnosticCategory.Language : DiagnosticCategory.Proof, error.Category);
        Assert.Equal((at, label), (Text(source, error.Span), error.Label));
        var reason = error.Reason!;
        // SPEC 23.3.6.5: a declared relation names no destination; it relates its clause instead.
        Assert.Equal(["relation", "longer", "shorter", "source"], reason.Select(static x => x.Name));
        Assert.Equal((label.Contains(" == ", StringComparison.Ordinal) ? "==" : "outlives", "declared"), (reason[0].Value, reason[3].Value));
        Assert.Contains(advice, error.Advice, StringComparison.Ordinal);
        var related = error.Related!.Select(x => (x.Role, Text(source, x.Span))).ToArray();
        Assert.Equal(borrow is null ? [("relation", clause)] : [("origin", borrow), ("relation", clause)], related);
        return error;
    }

    private static string Text(string source, SourceSpan? span) => span is { } value ? source.Substring(value.Start, value.Length) : string.Empty;
}
