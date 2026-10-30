// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

// The G216 (a) acceptance tests of expression bodies (problem statement d3 §4.5; see OriginRelationDiagnosticTest.ContextualParts.cs): a
// declared result is a premise of its definition (SPEC 15.6.1), so an expression body `=> .None` or `=> .Some(x)` is judged against it
// as a statement body is and keeps dev's verdicts (probe 1: the existing `=> .None` rows and t7n). D-1 decides call arguments only, so a
// payload that does not fit the declared result keeps its record at the payload.
public partial class ResultPremiseTest
{
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
}
