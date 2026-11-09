// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of expression bodies (problem statement d3 §4.5; see OriginRelationDiagnosticTest.ContextualParts.cs): a
// declared result is a premise of its definition (SPEC 15.6.1), so an expression body `=> .None` or `=> .Some(x)` is judged against it
// as a statement body is and keeps dev's verdicts (probe 1: the existing `=> .None` rows and t7n). D-1 decides call arguments only, so a
// payload that does not fit the declared result keeps its record at the payload.
public partial class ResultPremiseTest
{
    private const string H2Field = "struct H2 {a}\n    public let r: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.r = o@move\n";
    private const string MakeH2 = "func mk(o: Option<ref/i32 during s>) -> H2 during s => H2.init(o@move)\n";
    private const string WrapH2 = "struct W {c}\n    public var h: H2 during c\n    public init(h: H2 during c) => self.h = h@move\n";
    private const string Getter = "struct G {a, b}\n    public let q: ref/i32 during a\n    public let p: ref/i32 during b\n    public init(q: ref/i32 during a, p: ref/i32 during b)\n        self.q = q\n        self.p = p\n    public computed v: Option<ref/i32 during a>\n";

    // Each valid body has a twin whose payload does not outlive the declared result's Origin (Unknown between fixed Origins).
    [Theory]
    [InlineData("u1", "func f() -> Option<ref/i32 during static> => .None\n", "")]
    [InlineData("u1_n", "func f(x: ref/i32) -> Option<ref/i32 during static> => .Some(x)\n", "UnprovenOriginRelation_Kd x")]
    [InlineData("u2", "func f(x: ref/i32) -> Option<ref/i32 during x> => .Some(x)\n", "")]
    [InlineData("u2_n", "func f(x: ref/i32, y: ref/i32) -> Option<ref/i32 during x> => .Some(y)\n", "UnprovenOriginRelation_Kd y")]
    [InlineData("u6", "func none() -> Option<ref/i32 during s> => .None\n", "")]
    [InlineData("u6_n", "func some(x: ref/i32) -> Option<ref/i32 during s> => .Some(x)\n", "UnprovenOriginRelation_Kd x")]
    public void AnExpressionBodyAssumesItsDeclaredResult(string form, string body, string records)
        => OriginRelationDiagnosticTest.AssertRecords(form, body, records, null);

    // SPEC 14.2.2, 10.2, 15.3.6, 15.6.4 step 1 (PLAN G517): a function's or a `get`'s single-item body returns its value, so it is fitted
    // as its `return` twin is: a call region reaching the result through a Field read or an enclosing call's argument is solved by that
    // use (each expression form Unsupported_Kd before), and an invalid twin keeps its relation's record in both forms.
    [Theory]
    [InlineData("field", MakeH2, "func g(q: ref/i32) -> Option<ref/i32 during q>", "mk(.Some(q)).r", "")]
    [InlineData("init", "", "func g(q: ref/i32) -> Option<ref/i32 during q>", "H2.init(.Some(q)).r", "")]
    [InlineData("nested", MakeH2 + "struct O {a}\n    public let h: H2 during a\n    public init(h: H2 during a) => self.h = h@move\nfunc mo(o: Option<ref/i32 during s>) -> O during s => O.init(mk(o@move))\n", "func g(q: ref/i32) -> Option<ref/i32 during q>", "mo(.Some(q)).h.r", "")]
    [InlineData("method", "struct M\n    public let n: i32\n    public init(n: i32) => self.n = n\n    public func mk(self, o: Option<ref/i32 during s>) -> H2 during s => H2.init(o@move)\n", "func g(m: ref/M, q: ref/i32) -> Option<ref/i32 during q>", "m.mk(.Some(q)).r", "")]
    [InlineData("never", "struct H {a}\n    public let r: ref/i32 during a\n    public init(x: ref/i32 during a) => self.r = x\nfunc mkh(x: ref/i32 during s) -> H during s => H.init(x)\n", "func g(q: ref/i32) -> ref/i32 during q", "mkh($abort(\"x\")).r", "")]
    [InlineData("argument", WrapH2, "func g(q: ref/i32) -> W during q", "W.init(H2.init(.None))", "")]
    [InlineData("function argument", MakeH2 + WrapH2 + "func wrapw(h: H2 during c) -> W during c => W.init(h@move)\n", "func g(q: ref/i32) -> W during q", "wrapw(mk(.None))", "")]
    [InlineData("get", Getter, "        get() -> Option<ref/i32 during a>", "H2.init(.Some(self.q)).r", "")]
    [InlineData("other", MakeH2, "func g(q: ref/i32, p: ref/i32) -> Option<ref/i32 during q>", "mk(.Some(p)).r", "UnprovenOriginRelation_Kd mk(.Some(p)).r")]
    [InlineData("local", MakeH2, "func g(n: i32, q: ref/i32) -> Option<ref/i32 during q>", "mk(.Some(n@ref)).r", "UnsatisfiedOriginRelation_Kd mk(.Some(n@ref)).r")]
    [InlineData("get other", Getter, "        get() -> Option<ref/i32 during a>", "H2.init(.Some(self.p)).r", "UnprovenOriginRelation_Kd H2.init(.Some(self.p)).r")]
    public void ASingleItemBodyFitsItsResultAsItsReturnTwin(string form, string declarations, string signature, string value, string records)
    {
        var indent = new string(' ', signature.Length - signature.TrimStart().Length + 4);
        OriginRelationDiagnosticTest.AssertRecords(form + " (return)", H2Field + declarations + signature + "\n" + indent + "return " + value + "\n", records, null);
        OriginRelationDiagnosticTest.AssertRecords(form, H2Field + declarations + signature + " => " + value + "\n", records, null);
    }
}
