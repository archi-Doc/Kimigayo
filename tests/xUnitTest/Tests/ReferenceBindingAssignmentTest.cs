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
    // assignment. A match binding's rows are the located limit: its slot keeps the Subject element's Origin, which no other borrow
    // provably outlives, a local's (`b@ref`) as the literal's (STATUS).
    [Theory]
    [InlineData("let a = 1\nvar refs: Array<ref/i32> = [a@ref]\nfor var r in refs@move\n    r = 2", null)]
    [InlineData("let a = 1\nmatch (a@ref, 2)\n    (var r, _) => r = 3", DiagnosticCode.Unsupported_Kd)]
    [InlineData("let a = 1\nlet b = 2\nmatch (a@ref, 2)\n    (var r, _) => r = b@ref", DiagnosticCode.Unsupported_Kd)]
    public void OwnedReferenceBindingsTakeALiteralAsABorrowedTemporary(string source, DiagnosticCode? limit)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Equal(limit is { } code ? [code] : [], c.Binding.Issues.Select(static x => x.Code));
    }

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
