// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 14.8.1, 10.2: a binding selected through several reference layers depends on the Origins those layers
/// grant: a ref layer restarts the dependency, and each uniq layer below it adds its own.</summary>
public class PatternLayerOriginTest
{
    private const string Parameter = "r: ref/(uniq/Option<i32> during b) during a";

    [Fact]
    public void AnExclusiveLayerBelowASharedOneKeepsTheOuterDependency()
    {
        var c = MinimalEmissionTest.Analyze("func f(" + Parameter + ") -> ref/i32 during b\n    match r\n        .Some(let v) => return v\n        .None => $abort(\"none\")");
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal(DiagnosticCode.TypeMismatch_Kd, issue.Code);
        Assert.Empty(c.AnalyzeControlFlow().Issues); // The failed return reports no dependent result mismatch.
    }

    [Theory]
    [InlineData("func f(" + Parameter + ") -> ref/i32 during (a and b)\n    match r\n        .Some(let v) => return v\n        .None => $abort(\"none\")")]
    [InlineData("func f(r: ref/(ref/Option<i32> during b) during a) -> ref/i32 during b\n    match r\n        .Some(let v) => return v@follow@ref\n        .None => $abort(\"none\")")]
    public void TheMeetOfTheGrantingLayersIsAccepted(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }
}
