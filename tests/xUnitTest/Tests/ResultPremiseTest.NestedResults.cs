// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of nested results (see OriginRelationDiagnosticTest.NestedSignatures.cs): under DECISIONS R4-M162 a nested
// signature's result is judged where it is written, never a premise of a body or an obligation of a call.
public partial class ResultPremiseTest
{
    private const string StaticClause = "struct T {a}\n    origin a outlives static\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    // A clause every Origin satisfies (SPEC 15.2.3: static outlives every Origin).
    private const string AllClause = "struct Q {a}\n    origin static outlives a\n    public let x: ref/i32 during a\n    public init(x: ref/i32 during a) => self.x = x\n\n";

    private const string NestedResult = "func k(q: ref/i32, r: ref/i32, f: () -> Option<ref/(ref/i32 during q) during r>) -> ";

    private const string ResultCall = "func k(q: ref/i32, f: () -> Option<T during q>) -> i32\n";

    // PLAN G464: a Function result whose nested input names T at r's own Origin, and its twin whose nested result does too.
    private const string FunctionResult = "func r(q: ref/i32) -> (Option<T during q>) -> i32 => $abort(\"no\")\nfunc host(s: ref/i32) -> i32\n";

    private const string FunctionBothResult = "func r(q: ref/i32) -> (Option<T during q>) -> Option<T during q> => $abort(\"no\")\nfunc host(s: ref/i32) -> i32\n";

    // An enclosing body's local whose value Origin a named nested signature names (BU-56's unannotated class).
    private const string LocalR = "func leak(a: ref/i32) -> i32\n    var arr: Array<ref/i32> = []\n    arr.append(a)\n    let r = arr[0]\n";

    // A nested result relating Origins its inputs do not relate is the declaration's record where it is written, whether or not the
    // body calls it (z1 beside `return q`, r3, r3_call, v2, v3; DECISIONS R4-M162); t7 is ResultPremiseTest's Type-clause half. An
    // enclosing body's Origin the declaration names is a fixed Origin of it (SPEC 15.3.4; DECISIONS BU-89): r3loc and rootloc (a
    // document-local) are Binding's record, and a relation of it with another enclosing Origin needs the declaration's clause, in either
    // direction (encloc, encloc_rev; dev accepted both, and gave rootloc UnsatisfiedOriginRelation_Kd).
    [Theory]
    [InlineData("t7", StaticClause + ResultCall + "    let o = f()\n    return 0\n", "UnprovenOriginRelation_Kd Option<T during q>")]
    [InlineData("t7_c", StaticClause + ResultCall + "    origin q outlives static\n    let o = f()\n    return 0\n", "")]
    [InlineData("t7n", StaticClause + "func h(x: ref/i32) -> Option<T during x>\n    origin x outlives static\n    return .None\nfunc k(q: ref/i32) -> i32\n    let o = h(q)\n    return 0\n", "UnprovenConstraint_Kd h(q); UnprovenOriginRelation_Kd q")]
    [InlineData("t7l", StaticClause + "func k(q: ref/i32) -> i32\n    let o: Option<T during q> = .None\n    return 0\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd Option<T during q>")] // + the MIR's record, published directly (R5-M184, H3b T51)
    [InlineData("z1", NestedResult + "ref/i32 during r\n    return q\nfunc bad() -> i32\n    let long = 1\n    let lr = long@ref\n    var keep = lr\n    if true\n        let short = 2\n        let sq = short@ref\n        keep = k(sq, lr, func [] () => Option.None)\n    return keep@follow\n", "UnprovenOriginRelation_Kd q; UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("z1_c", NestedResult + "ref/i32 during r\n    origin q outlives r\n    return q\nfunc good() -> i32\n    let long = 1\n    let lr = long@ref\n    let short = 2\n    let sr = short@ref\n    let keep = k(lr, sr, func [] () => Option.None)\n    return keep@follow\n", "")]
    [InlineData("r3", "func g(q: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("r3_call", "func g(q: ref/i32, x: ref/i32, cb: (ref/i32) -> Option<ref/(ref/i32 during q)>) -> i32\n    let o = cb(x)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("v2", NestedResult + "i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("v3", NestedResult + "i32\n    let o = f()\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("v3_c", NestedResult + "i32\n    origin q outlives r\n    let o = f()\n    return 0\n", "")]
    [InlineData("r3loc", LocalR + "    func k(cb: (ref/i32) -> Option<ref/(ref/i32 during r)>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r)")]
    [InlineData("encloc", LocalR + "    func k(cb: () -> Option<ref/(ref/i32 during r) during a>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r) during a")]
    [InlineData("encloc_rev", LocalR + "    func k(cb: () -> Option<ref/(ref/i32 during a) during r>) -> i32 => 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during a) during r")]
    [InlineData("rootloc", "var arr: Array<ref/i32> = []\nlet z: i32 = 0\narr.append(z@ref)\nlet r = arr[0]\nfunc k(cb: (ref/i32) -> Option<ref/(ref/i32 during r)>) -> i32 => 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during r)")]
    // PLAN G962 (the owner's O-397 (A); SPEC 15.3.4, 15.6.1, 15.3.3): an anonymous function's written result is formed where it is
    // written as a Function Type's is (FormNestedResult), each clause under the enclosing premises and the signature's own inputs, for
    // every choice of its per-call Origins: whether or not the literal is called (cl2, cl2n), in a component (cl2o), over an enclosing
    // Origin (cl2e, cl2e_c), a nested Closure's (cl2r), a generic body's (cl2g, once), at a failed header fit (cl2m) and over a value
    // call's region (cl2v, J2's cl1, its input `Option<ref/i32>` a body variable); a premise of k gives no per-call Origin (pc1); a
    // body-local Borrow's Origin (esc1) is the walk's body-chain side; a clause every per-call Origin satisfies (cl2q, `static outlives
    // a`; r10l on the Function Type side, newly accepted) or one the signature's inputs entail (cl2yr, over k's p) forms no row. No input
    // names the signature's own per-call Origin (PLAN G972, the owner's O-403 (b)), so a per-call clause the inputs entail has no such
    // spelling: cl2y's input is invalid at `x`. cl2, cl2n, cl2o, cl2g, cl2m, cl2v and pc1 were A5's located Unsupported_Kd at the written
    // result (F-a, the owner's O-2) until R7-O3's U-O3 formed the per-call relation as the nested signature's declaration row (below).
    [InlineData("cl2", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "UnprovenOriginRelation_Kd T during x")]
    [InlineData("cl2n", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    return 0\n", "UnprovenOriginRelation_Kd T during x")]
    [InlineData("cl2o", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> Option<T during x> => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "UnprovenOriginRelation_Kd Option<T during x>")]
    [InlineData("cl2e", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] () -> T during p => $abort(\"no\")\n    let t = c()\n    return 0\n", "UnprovenOriginRelation_Kd T during p")]
    [InlineData("cl2e_c", StaticClause + "func k(p: ref/i32) -> i32\n    origin p outlives static\n    let c = func [] () -> T during p => $abort(\"no\")\n    let t = c()\n    return 0\n", "")]
    [InlineData("cl2s", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> T during static => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "")]
    [InlineData("cl2q", AllClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32) -> Q during x => $abort(\"no\")\n    return 0\n", "")]
    [InlineData("cl2y", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (x: ref/i32, y: ref/(ref/i32 during x) during static) -> T during x => $abort(\"no\")\n    return 0\n", "InvalidOriginBinding_Kd x")]
    [InlineData("cl2yr", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (y: ref/(ref/i32 during p) during static) -> T during p => $abort(\"no\")\n    return 0\n", "")]
    [InlineData("cl2r", StaticClause + "func k(p: ref/i32) -> i32\n    let c = func [] (y: ref/i32) -> i32\n        let d = func [] () -> T during y => $abort(\"no\")\n        return 0\n    let n = c(p)\n    return 0\n", "UnprovenOriginRelation_Kd T during y")]
    [InlineData("cl2g", StaticClause + "func mk2<U>(u: ref/U) -> i32\n    let c = func [] (x: ref/U) -> T during x => $abort(\"no\")\n    return 0\n" + "func k(p: ref/i32) -> i32\n    let a = mk2(p)\n    let bv: bool = true\n    let b = mk2(bv@ref)\n    return 0\n", "UnprovenOriginRelation_Kd T during x")]
    [InlineData("cl2m", StaticClause + "func k(p: ref/i32) -> i32\n    let g: (ref/i32) -> i32 = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    return 0\n", "TypeMismatch_Kd func [] (x: ref/i32) -> T during x => $abort(\"no\"); UnprovenOriginRelation_Kd T during x")]
    [InlineData("cl2v", StaticClause + "func k() -> i32\n    let v: i32 = 5\n    let c = func [] (x: ref/i32, y: Option<ref/i32>) -> T during x => $abort(\"no\")\n    let t = c($abort(\"n\"), .Some(v@ref))\n    return 0\n", "UnprovenOriginRelation_Kd T during x")]
    [InlineData("pc1", StaticClause + "func k(p: ref/i32) -> i32\n    origin p outlives static\n    let c = func [] (x: ref/i32) -> T during x => $abort(\"no\")\n    let t = c(p)\n    return 0\n", "UnprovenOriginRelation_Kd T during x")]
    [InlineData("esc1", StaticClause + "func k(p: ref/i32) -> i32\n    let v: i32 = 1\n    let r = v@ref\n    let c = func [] () -> T during r => $abort(\"no\")\n    return 0\n", "UnsatisfiedOriginRelation_Kd T during r")]
    [InlineData("r10l", AllClause + "func k(p: ref/i32) -> i32\n    let g: Option<(ref/i32) -> Q> = .None\n    return 0\n", "")]
    // PLAN G962 C1 (SPEC 15.6.1, 15.3.3): a definition assumes its result Type well formed, its Types' own clauses included (each call
    // proves them), so every reader of k's premises sees `p outlives static` from `T during p`: an anonymous function's written result
    // (kr1; generic, passed to a call, in a method: kr1g, kr1n, kr1m; beside a returned `.None`: kr1b), a local and a Function Type
    // annotation (kr1l, kr1f), the body's own fit and a call in it (kr1s, kr1q: PLAN G967's result half). A clause the result Type does
    // not give stays the literal's record (kr1x, kr1y), and a call proves the clause (kr1i: the call's record alone).
    [InlineData("kr1", StaticClause + "func k(p: ref/i32) -> T during p\n    let c = func [] () -> T during p => $abort(\"no\")\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1g", StaticClause + "func k<U>(p: ref/U) -> T during p\n    let c = func [] () -> T during p => $abort(\"no\")\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1n", StaticClause + "func keep<V>(v: V) -> i32 => 0\nfunc k(p: ref/i32) -> T during p\n    let n = keep(func [] () -> T during p => $abort(\"no\"))\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1m", StaticClause + "struct H\n    public func mk(self: ref/Self, p: ref/i32) -> T during p\n        let c = func [] () -> T during p => $abort(\"no\")\n        return $abort(\"no\")\n", "")]
    [InlineData("kr1b", StaticClause + "func k(p: ref/i32) -> Option<T during p>\n    let c = func [] () -> T during p => $abort(\"no\")\n    return .None\n", "")]
    [InlineData("kr1l", StaticClause + "func k(p: ref/i32) -> T during p\n    let o: Option<T during p> = .None\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1f", StaticClause + "func k(p: ref/i32) -> T during p\n    let g: Option<() -> T during p> = .None\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1s", StaticClause + "func k(p: ref/i32) -> T during p\n    let s: ref/i32 during static = p\n    return $abort(\"no\")\n", "")]
    [InlineData("kr1q", StaticClause + "func mk(x: Option<ref/i32 during q>, y: ref/i32 during q) -> T during q => $abort(\"no\")\nfunc k(p: ref/i32) -> T during p\n    let t = mk(.None, p)\n    return t@move\n", "")]
    [InlineData("kr1x", StaticClause + "func k(p: ref/i32, q: ref/i32) -> T during p\n    let c = func [] () -> T during q => $abort(\"no\")\n    return $abort(\"no\")\n", "UnprovenOriginRelation_Kd T during q")]
    [InlineData("kr1y", StaticClause + AllClause + "func k(p: ref/i32) -> Q during p\n    let c = func [] () -> T during p => $abort(\"no\")\n    return $abort(\"no\")\n", "UnprovenOriginRelation_Kd T during p")]
    // PLAN G464 (SPEC 15.6.1, 15.3.4, 15.6.4 step 3): a call's instantiated Function result is well formed by the clauses the call
    // proves: its nested input is that signature's premise, proven at its calls (o6i, o6ri: the value call's record at `.None`, beside
    // the construction's own UnprovenConstraint_Kd there, PLAN's open issue), and its nested result was judged where r wrote it, under
    // r's nested input (o6r). Dev's UnprovenConstraint_Kd at `r(s)` goes: acceptance moves (o6v, o6r) and a false record (o6i, o6ri).
    [InlineData("o6v", StaticClause + FunctionResult + "    let f = r(s)\n    return 0\n", "")]
    [InlineData("o6r", StaticClause + FunctionBothResult + "    let f = r(s)\n    return 0\n", "")]
    [InlineData("o6i", StaticClause + FunctionResult + "    let f = r(s)\n    return f(.None)\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd .None")]
    [InlineData("o6ri", StaticClause + FunctionBothResult + "    let f = r(s)\n    let o = f(.None)\n    return 0\n", "UnprovenConstraint_Kd .None; UnprovenOriginRelation_Kd .None")]
    [InlineData("o6s", StaticClause + FunctionResult + "    origin s outlives static\n    let f = r(s)\n    return f(.None)\n", "")]
    [InlineData("kr1i", StaticClause + "func k(p: ref/i32) -> T during p\n    let c = func [] () -> T during p => $abort(\"no\")\n    return $abort(\"no\")\nfunc m(q: ref/i32) -> i32\n    let t = k(q)\n    return 0\n", "UnprovenConstraint_Kd k(q)")]
    // PLAN G216 (b), R7-O3 U-O3 (SPEC 15.3.4: a nested signature's result is well formed under the declaration's premises and its own
    // well-formed inputs for every choice of its per-call Origins; a per-call relation must follow from those inputs): a relation with a
    // per-call end is the nested signature's declaration row, judged by Binding where it is written, not a row of the evaluating body (A5's
    // located Unsupported_Kd before, valid or not). A Closure's and a local Function Type's result during the input's per-call Origin
    // (u1, u2), over a body local's Origin (u1loc), by the enclosing clause's chain (u4), the result's meet of two inputs (umeet, SPEC
    // 15.4.3 rule 1 and 15.3.6), a result over a body local's region (usl: no bound of the region), a Closure in a Closure body chaining
    // both inputs (uin_chain), its own input (uin_okin) or neither (uin_bad), and a Function value input (ufn): each valid one accepted.
    [InlineData("u1", "func k(q: ref/i32) -> i32\n    let h = func [q] (p: ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> => Option.Some(p)\n    let o = h(q@ref)\n    return 0\n", "")]
    [InlineData("u1_n", "func k(q: ref/i32) -> i32\n    let n = 1\n    let h = func [q] (p: ref/i32) -> Option<ref/(ref/i32 during q)> => Option.None\n    let o = h(n@ref)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("u2", "func k(q: ref/i32) -> i32\n    var f: (ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> = func (p) => Option.Some(p)\n    let o = f(q@ref)\n    return 0\n", "")]
    [InlineData("u2_n", "func k(q: ref/i32) -> i32\n    let n = 1\n    var f: (ref/i32) -> Option<ref/(ref/i32 during q)> = func (p) => Option.None\n    let o = f(n@ref)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("u1loc", "func k() -> i32\n    let x = 5\n    let q = x@ref\n    let h = func [q] (p: ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> => Option.Some(p)\n    let o = h(q@ref)\n    return 0\n", "")]
    [InlineData("u1loc_n", "func k() -> i32\n    let x = 5\n    let q = x@ref\n    let n = 1\n    let h = func [q] (p: ref/i32) -> Option<ref/(ref/i32 during q)> => Option.None\n    let o = h(n@ref)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("u4", "func k(q: ref/i32, r: ref/i32) -> i32\n    origin q outlives r\n    let h = func [] (p: ref/(ref/i32 during r)) -> Option<ref/(ref/i32 during q)> => Option.None\n    let o = h(r@ref)\n    return 0\n", "")]
    [InlineData("u4_n", "func k(q: ref/i32, r: ref/i32) -> i32\n    origin r outlives q\n    let h = func [] (p: ref/(ref/i32 during r)) -> Option<ref/(ref/i32 during q)> => Option.None\n    let o = h(r@ref)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("umeet", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q), s: ref/(ref/i32 during q)) -> Option<ref/(ref/i32 during q)> => Option.None\n    return 0\n", "")]
    [InlineData("umeet_n", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/i32, s: ref/i32) -> Option<ref/(ref/i32 during q)> => Option.None\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q)")]
    [InlineData("usl_n", "func k() -> i32\n    let x = 5\n    let q = x@ref\n    let h = func [] (p: ref/i32) -> Option<ref/(ref/i32 during p) during q> => Option.None\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during p) during q")]
    [InlineData("uin_chain", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q)) -> i32\n        let h2 = func [] (r: ref/(ref/i32 during p)) -> Option<ref/(ref/i32 during q)> => Option.None\n        return 0\n    return 0\n", "")]
    [InlineData("uin_okin", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q)) -> i32\n        let h2 = func [] (r: ref/(ref/i32 during p)) -> Option<ref/(ref/i32 during p)> => Option.Some(r)\n        return 0\n    return 0\n", "")]
    [InlineData("uin_bad", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q)) -> i32\n        let h2 = func [] (r: ref/i32) -> Option<ref/(ref/i32 during p)> => Option.None\n        return 0\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during p)")]
    [InlineData("ufn", "func k(q: ref/i32) -> i32\n    let h = func [] (p: ref/(ref/i32 during q), g: (ref/i32) -> ref/i32 during q) -> Option<ref/(ref/i32 during q)> => Option.Some(p)\n    return 0\n", "")]
    public void ANestedResultIsJudgedWhereItIsWritten(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // An Item whose declared clause a conversion target would give (cl1): the target's result relates its per-call Origin to `static`
    // beyond its input, so it is recorded where it is written (DECISIONS R4-M162); the erased Item of ItemUses stays valid when called (rk4).
    // cl1 was A5's located Unsupported_Kd at the target's written relation (F-a, H5's (e) list) until R7-O3's U-O3 moved the Language
    // record back to Binding at the same site.
    [Theory]
    [InlineData("cl1", "func pin1(q: ref/i32) -> Option<ref/(ref/i32 during q) during static>\n    origin q outlives static\n    return .None\nfunc mk() -> i32\n    let g: (ref/i32) -> Option<ref/(ref/i32) during static> = pin1\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32) during static")]
    [InlineData("rk4", "func leak(p: ref/i32, q: ref/i32) -> (Option<ref/(ref/i32 during q) during p>, ref/i32 during p)\n    return (.None, q)\nfunc use(x: ref/i32) -> i32\n    let local: i32 = 5\n    let g: (ref/i32, ref/i32) -> (Option<ref/(ref/i32)>, ref/i32) = leak\n    return g(x, local@ref).1@follow\n", "")]
    public void AnItemsClauseHoldsByItsTargetClausesTheTargetGives(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);
}
