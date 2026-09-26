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

        // The failed assignment reports no dependent control-flow mismatch for its right operand.
        Assert.Empty(c.AnalyzeControlFlow().Issues);
    }

    [Theory]
    [InlineData("let a = 1\nvar refs: Array<ref/i32> = [a@ref]\nfor var r in refs@move\n    r = 2")]
    [InlineData("let a = 1\nmatch (a@ref, 2)\n    (var r, _) => r = 3")]
    public void OwnedReferenceBindingsReportTheOrdinaryMismatch(string source)
    {
        // SPEC 15.1.6: a ByValue Subject binds its stored reference; the mode diagnostic does not apply.
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code is DiagnosticCode.SharedBindingAssignment_Kd or DiagnosticCode.ExclusiveBindingAssignment_Kd);
    }

    [Theory]
    [InlineData("var values: Array<i32> = [1]\nfor v in values@uniq\n    v@follow += 1")]
    [InlineData("var values: Array<i32> = [1]\nfor var v in values@move\n    v += 1")]
    [InlineData("var count: i32 = 1\nmatch count@copy\n    var n => n += 5")]
    [InlineData("let a = 1\nlet b = 2\nlet refs: [2 of ref/i32] = [a@ref, b@ref]\nfor var r in refs\n    r = refs[1]@ref")]
    public void SuggestedSpellingsAreAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
