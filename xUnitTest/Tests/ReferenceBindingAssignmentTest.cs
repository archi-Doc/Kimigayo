// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 15.1.6, 14.6.1: a pattern or for binding on a shared or exclusive path is a reference; assigning a
/// value of its referent Type reports one diagnostic that names the mode.</summary>
public class ReferenceBindingAssignmentTest
{
    [Theory]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(var n)\n        n += 1\n        yield n\n    .None => 0", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) -> i32 => match x\n    .Some(var n)\n        n = 2\n        yield n\n    .None => 0", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("func f(x: Option<i32>) => match x\n    .Some(var n) => n++\n    .None => ()", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values\n    v += 1", DiagnosticCode.SharedBindingAssignment_Kd)]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values@uniq\n    v += 1", DiagnosticCode.ExclusiveBindingAssignment_Kd)]
    [InlineData("var count: i32 = 1\nmatch count@uniq\n    var n => n = 5", DiagnosticCode.ExclusiveBindingAssignment_Kd)]
    public void ValueAssignmentsNameTheBindingMode(string source, DiagnosticCode code)
    {
        var c = MinimalEmissionTest.Analyze(source);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(code, issue.Code);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nfor v in values@uniq\n    v@deref += 1")]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values@move\n    v += 1")]
    [InlineData("var count: i32 = 1\nmatch count@owner\n    var n => n += 5")]
    [InlineData("let a = 1\nlet b = 2\nlet refs: [2 of ref/i32] = [a@ref, b@ref]\nfor var r in refs\n    r = refs[1]@ref")]
    public void SuggestedSpellingsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
