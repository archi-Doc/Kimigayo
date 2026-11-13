// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 15.1.6, 14.6.1: a pattern or for binding on a shared or exclusive path is a reference; assigning a
/// value of its referent Type reports one diagnostic that names the mode.</summary>
public class ReferenceBindingAssignmentTest
{
    [Theory]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(var n)\n        n += 1\n        yield n\n    .None => 0\npublic func main() => ()", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(var n)\n        n = 2\n        yield n\n    .None => 0\npublic func main() => ()", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) => match x\n    .Some(var n) => n++\n    .None => ()\npublic func main() => ()", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values\n    v += 1", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values@uniq\n    v += 1", DiagnosticCode.ExclusiveBindingAssignment_Kd)]
    [InlineData("struct Num\n    Self is Addable<Num>\n    associate Addable<Num>.Output is Num\n    public init() => ()\n    public func added(self: ref/Self, right: ref/Num) -> Num => Num.init()\nvar items: Array<Num> = [Num.init()]\nfor v in items\n    v += Num.init()", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("var count: i32 = 1\nmatch count@uniq\n    var n => n = 5", DiagnosticCode.ExclusiveBindingAssignment_Kd)]
    public void ValueAssignmentsNameTheBindingMode(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(code, issue.Code);

        // The failed assignment reports no dependent mismatch for its right operand: the published Errors are exactly its record.
        Assert.Equal([code.ToString()], CompilationTestHelper.PublishedErrors(c).Select(static x => x.Code));
    }

    // SPEC 15.1.6, 10.2 row 2, 12.3.1 (PLAN G585; the owner's O-201 (1) (a)): a ByValue Subject binds its stored reference, an ordinary
    // `var` of `ref/i32`, so the mode diagnostic does not apply, and a literal assigned to it is a temporary borrowed for the
    // assignment. A match binding is such a `var` too: its own region slots take the borrow (PLAN G817).
    [Theory]
    [InlineData("let a = 1\nvar refs: Array<ref/i32> = [a@ref]\nfor var r in refs@move\n    r = 2", null)]
    [InlineData("let a = 1\nmatch (a@ref, 2)\n    (var r, _) => r = 3", null)]
    [InlineData("let a = 1\nlet b = 2\nmatch (a@ref, 2)\n    (var r, _) => r = b@ref", null)]
    public void OwnedReferenceBindingsTakeALiteralAsABorrowedTemporary(string source, DiagnosticCode? limit)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(limit is { } code ? [code] : [], c.Binding.Issues.Select(static x => x.Code));
    }

    // SPEC 14.8.2, 14.6, 15.1.6, 15.6.5, 15.6.6 (PLAN G817): a var Pattern binding or for slot is a local with no written Type, so its Type's
    // slots are inferred regions bounded below by the bound value's Origin, as `var r = <that value>`'s. An assignment widens the slot, never
    // the Subject's or the iterable's; a Refuted or Unknown chain through the slot is recorded where it reaches the fixed Origin, a use after a
    // temporary's or a local's death is a Loan conflict, and the slot carries the Loans of what it holds (the last four rows).
    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (form, program, records).
    [Theory]
    [InlineData("mv-lit", "func t(n: ref/i32) -> i32\n    match (n, 2)\n        (var r, _) => r = 3\n    return 0\n", "")]
    [InlineData("mv-m", "func g(n: ref/i32, m: ref/i32) -> i32\n    match (n, 2)\n        (var r, _) => r = m\n    return 0\n", "")]
    [InlineData("sh-b", "func f() -> i32\n    let t = (1, 2)\n    let b = 3\n    match t\n        (var r, _) => r = b@ref\n    return 0\n", "")]
    [InlineData("for-tmp", "func f() -> i32\n    let a = 1\n    let b = 2\n    for var r in [a@ref]\n        r = b@ref\n    return 0\n", "")]
    [InlineData("gen-y", "func g<T>(x: ref/T, y: ref/T) -> i32\n    match (x, 1)\n        (var r, _) => r = y\n    return 0\n", "")]
    [InlineData("whole-b", "func f() -> i32\n    let a = 1\n    let b = 2\n    match a@ref\n        var r => r = b@ref\n    return 0\n", "")]
    [InlineData("mv-use", "func t(n: ref/i32) -> i32\n    match (n, 2)\n        (var r, _)\n            r = 3\n            let b: i32 = r\n            return b\n    return 0\n", "ComparisonLoanConflict_Kd: r = 3")]
    [InlineData("loc-ret", "func g(n: ref/i32 during x) -> ref/i32 during x\n    let b = 1\n    match (n, 2)\n        (var r, _)\n            r = b@ref\n            return r\n    return n\n", "UnsatisfiedOriginRelation_Kd: return r")]
    [InlineData("gen-ret", "func g<T>(x: ref/T during a, y: ref/T) -> ref/T during a\n    match (x, 1)\n        (var r, _)\n            r = y\n            return r\n    return x\n", "UnprovenOriginRelation_Kd: return r")]
    [InlineData("let-asg", "func f() -> i32\n    let a = 1\n    let b = 2\n    match (a@ref, 2)\n        (let r, _) => r = b@ref\n    return 0\n", "InvalidAssignment_Kd: (let r, _) => r = b@ref")]
    [InlineData("uniq-store-bad", "func f() -> i32\n    let a = 1\n    var t = (a@ref, 2)\n    match t@uniq\n        (var r, _)\n            let u = 7\n            r@follow = u@ref\n    let v: i32 = t.0\n    return v\n", "ComparisonLoanConflict_Kd: let u = 7")]
    [InlineData("esc-out", "func f() -> i32\n    let a = 1\n    var out = a@ref\n    match (a@ref, 2)\n        (var r, _)\n            let t = 3\n            r = t@ref\n            out = r\n    let v: i32 = out\n    return v\n", "ComparisonLoanConflict_Kd: let t = 3")]
    [InlineData("loop-inner-ok", "func f() -> i32\n    let a = 1\n    var i = 0\n    var total = 0\n    match (a@ref, 2)\n        (var r, _)\n            while i < 3\n                let t = i\n                r = t@ref\n                let v: i32 = r\n                total += v\n                i += 1\n            return total\n    return 0\n", "")]
    [InlineData("loop-inner-bad", "func f() -> i32\n    let a = 1\n    var i = 0\n    match (a@ref, 2)\n        (var r, _)\n            while i < 3\n                let t = i\n                r = t@ref\n                i += 1\n            let w: i32 = r\n            return w\n    return 0\n", "ComparisonLoanConflict_Kd: let t = i")]
    public void AReassignableBindingHasItsOwnRegionSlots(string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines(form, program + "()", records);

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nfor v in values@uniq\n    v@follow += 1")]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values@move\n    v += 1")]
    [InlineData("var count: i32 = 1\nmatch count@copy\n    var n => n += 5")]
    [InlineData("let a = 1\nlet b = 2\nlet refs: [2 of ref/i32] = [a@ref, b@ref]\nfor var r in refs\n    r = refs[1]@ref")]
    public void SuggestedSpellingsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }
}
