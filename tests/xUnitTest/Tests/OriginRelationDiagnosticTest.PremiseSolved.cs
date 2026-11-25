// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// DECISIONS BU-99 R2' (PLAN G396 (11)): a call's per-call Origin that only well-formedness bounds, a used default's premise included
// (SPEC 15.6.4 step 3, 7.2.3), takes the meet of its upper bounds (SPEC 15.3.6), and each other bound is judged against that candidate
// (SPEC 15.6.5): an expected result's lower bound at the call's result fit (Unproven between fixed ends, Unsatisfied from a body-local
// Borrow), a written annotation's layer at its occurrence and a Type argument's layer, explicit or the receiver's, at its Signature
// position, one record for each Origin of its referent's OwnedOrigins not proven to outlive the layer's (SPEC 15.2.3, 15.6.1; every
// failing position is its own record). The G216 (a) oracle's premise-solved family (rows revision 7): 24 of its 26 rows and the four
// twins its review asked for (mixwok, annotok, methst, s3pexplok); z2d and z2okd are CallOriginRelationTest's z2 rows, and a result
// fit is A5's one record at the call (R7-O1 H; PLAN G430).
public partial class OriginRelationDiagnosticTest
{
    private const string SolvedBand = "func f(p: Option<ref/(ref/i32 during a) during v>, w: Option<ref/(ref/i32 during v) during x>, y: ref/i32 during a, z: ref/i32 during x) -> i32 => 0\n";
    private const string SolvedBand2 = "func f(p: Option<ref/(ref/i32 during a) during v>, w: Option<ref/(ref/i32 during v) during x>, y: ref/i32 during a) -> ref/i32 during x => $abort(\"f\")\n";
    private const string SolvedTuple = "func g(p: (Option<ref/(ref/i32 during a) during v>, Option<ref/(ref/i32 during v) during x>), y: ref/i32 during a) -> ref/i32 during x => $abort(\"g\")\n";
    private const string SolvedChild = "func h(p: (ref/i32 during a, Option<ref/(ref/i32 during a) during c>)) -> ref/i32 during c => $abort(\"h\")\n";
    private const string SolvedMix = "func d(w: Option<ref/(ref/i32 during b) during c>, v: ref/i32 during b, u: Option<ref/(ref/i32 during c) during e> = .None) -> ref/i32 during e => $abort(\"d\")\n";
    private const string SolvedRecv = "struct H\n    var v: i32 = 0\n    public init() => ()\n    public func m(self: ref/Self during s, w: Option<ref/(ref/i32 during s) during c>) -> ref/i32 during c => $abort(\"m\")\n";
    private const string SolvedZ2 = "func z2(v: ref/i32 during b, w: Option<ref/(ref/i32 during b) during a> = .None) -> ref/i32 during a\n    return v\n";
    private const string SolvedId = "func id<T>(v: T) -> T => v@move\n";
    private const string SolvedUse = "func use(o: Option<ref/(ref/i32 during q) during r>, x: ref/i32 during q, y: ref/i32 during r) -> i32 => 0\n";
    private const string SolvedBox = "struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\n    public func put(self: ref/Self, w: Option<ref/E during s>, x: ref/i32 during s) -> i32 => 0\n";
    private const string SolvedStaticBox = "struct Box<E>\n    public let item: E\n    public init(item: E) => self.item = item@move\n    public func put(self: ref/Self, w: Option<ref/E during static>, x: ref/i32 during s) -> i32 => 0\n";
    private const string SolvedMk2 = "func mk2<T>(w: Option<ref/T during static>) -> i32 => 0\n";
    private const string SolvedNested = "func k(q: ref/i32, r: ref/i32, b: ref/Box<ref/(ref/i32 during q) during r>) -> i32\n    return b.put(.None, r)\n";

    [Theory]
    [InlineData("band", SolvedBand + "func k(q: ref/i32, r: ref/i32) -> i32\n    return f(.None, .None, q, r)\n", "")]
    [InlineData("bandok", SolvedBand + "func k(q: ref/i32) -> i32\n    return f(.None, .None, q, q)\n", "")]
    [InlineData("bandsh", SolvedBand + "func k(p: ref/i32) -> i32\n    let short = 2\n    return f(.None, .None, short@ref, p)\n", "")]
    [InlineData("band2", SolvedBand2 + "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    return f(.None, .None, q)\n", "UnprovenOriginRelation_Kd f(.None, .None, q)")]
    [InlineData("band2ok", SolvedBand2 + "func k(q: ref/i32) -> ref/i32 during q\n    return f(.None, .None, q)\n", "")]
    [InlineData("band2sh", SolvedBand2 + "func k(q: ref/i32) -> ref/i32 during q\n    let short = 2\n    return f(.None, .None, short@ref)\n", "UnsatisfiedOriginRelation_Kd f(.None, .None, short@ref)")]
    [InlineData("band2t", SolvedTuple + "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    return g((.None, .None), q)\n", "UnprovenOriginRelation_Kd g((.None, .None), q)")]
    [InlineData("band2tok", SolvedTuple + "func k(q: ref/i32) -> ref/i32 during q\n    return g((.None, .None), q)\n", "")]
    [InlineData("child", SolvedChild + "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    return h((q, .None))\n", "UnprovenOriginRelation_Kd h((q, .None))")]
    [InlineData("childok", SolvedChild + "func k(q: ref/i32) -> ref/i32 during q\n    return h((q, .None))\n", "")]
    [InlineData("mixd", SolvedMix + "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    return d(.None, q)\n", "UnprovenOriginRelation_Kd d(.None, q)")]
    [InlineData("mixdok", SolvedMix + "func k(q: ref/i32) -> ref/i32 during q\n    return d(.None, q)\n", "")]
    [InlineData("mixw", SolvedMix + "func k(q: ref/i32, r: ref/i32) -> ref/i32 during r\n    return d(.None, q, .None)\n", "UnprovenOriginRelation_Kd d(.None, q, .None)")]
    [InlineData("mixwok", SolvedMix + "func k(q: ref/i32) -> ref/i32 during q\n    return d(.None, q, .None)\n", "")]
    [InlineData("recv", SolvedRecv + "func k(q: ref/i32) -> i32\n    let h = H.init()\n    let r: ref/i32 during q = h.m(.None)\n    return 0\n", "UnsatisfiedOriginRelation_Kd let r: ref/i32 during q = h.m(.None)")]
    [InlineData("recvok", SolvedRecv + "func k(q: ref/i32, h: ref/H during q) -> i32\n    let r: ref/i32 during q = h.m(.None)\n    return 0\n", "")]
    [InlineData("z2w", SolvedZ2 + "func bad(p: ref/i32) -> ref/i32 during p\n    let short = 2\n    return z2(short@ref, .None)\n", "UnsatisfiedOriginRelation_Kd z2(short@ref, .None)")]
    [InlineData("z2okw", SolvedZ2 + "func ok(p: ref/i32) -> ref/i32 during p\n    return z2(p, .None)\n", "")]
    [InlineData("z2qd", SolvedZ2 + "func bad(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    return z2(q)\n", "UnprovenOriginRelation_Kd z2(q)")]
    [InlineData("z2qw", SolvedZ2 + "func bad(p: ref/i32, q: ref/i32) -> ref/i32 during p\n    return z2(q, .None)\n", "UnprovenOriginRelation_Kd z2(q, .None)")]
    [InlineData("annot", SolvedId + "func k(q: ref/i32, r: ref/i32) -> i32\n    let y: Option<ref/(ref/i32 during q) during r> = id(.None)\n    return 0\n", "UnprovenOriginRelation_Kd ref/(ref/i32 during q) during r")]
    [InlineData("annotok", SolvedId + "func k(q: ref/i32, r: ref/i32) -> i32\n    origin q outlives r\n    let y: Option<ref/(ref/i32 during q) during r> = id(.None)\n    return 0\n", "")]
    [InlineData("idnest", SolvedId + SolvedUse + "func k(a: ref/i32, b: ref/i32) -> i32\n    return use(id(.None), a, b)\n", "")]
    [InlineData("idnestok", SolvedId + SolvedUse + "func k(a: ref/i32) -> i32\n    return use(id(.None), a, a)\n", "")]
    [InlineData("meth", SolvedBox + SolvedNested, "")]
    [InlineData("methst", SolvedStaticBox + SolvedNested, "UnprovenOriginRelation_Kd .None; UnprovenOriginRelation_Kd .None")]
    [InlineData("s3pexpl", SolvedMk2 + "func k(q: ref/i32) -> i32\n    return mk2<ref/i32 during q>(.None)\n", "UnprovenOriginRelation_Kd .None")]
    [InlineData("s3pexplok", SolvedMk2 + "func k(q: ref/i32) -> i32\n    origin q outlives static\n    return mk2<ref/i32 during q>(.None)\n", "")]
    public void APremiseSolvedOriginTakesTheMeetOfItsBounds(string form, string body, string records)
        => AssertRecords(form, body, records, null);
}
