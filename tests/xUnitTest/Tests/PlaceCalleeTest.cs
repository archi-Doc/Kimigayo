// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 7.7.3, 4.7.1: a common Function value stored in a user-Indexable element is called through a shared borrow of that Place,
// and one returned as a reference (`first(xs@ref)(5)`) through a Reborrow of it; either stays lent while the call's arguments run.
public class PlaceCalleeTest
{
    private const string Inc = "func inc(v: i32) -> i32 => v + 1\n";

    [Theory]
    [InlineData("Dictionary", Inc + "var m: Dictionary<i32, (i32) -> i32> = [:]\nlet f: (i32) -> i32 = inc\n_ = m.tryInsert(1, f@move)\nrequire m[1](5) == 6 and m[1](6) == 7 else => $abort(\"m\")")]
    [InlineData("ReturnedReference", Inc + "func first(values: ref/Array<(i32) -> i32>) -> ref/((i32) -> i32) during values\n    return values[0]@ref\nlet f: (i32) -> i32 = inc\nvar xs: Array<(i32) -> i32> = []\nxs.append(f@move)\nrequire first(xs@ref)(5) == 6 else => $abort(\"first\")")]
    public void APublishedFunctionPlaceIsCalledThroughABorrow(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("PlaceCallee" + name, source, string.Empty);
    }

    [Fact]
    public void TheLentPlaceCannotChangeDuringTheArguments()
    {
        var c = MinimalEmissionTest.Analyze(Inc + "func clear(m: uniq/Dictionary<i32, (i32) -> i32>) -> i32\n    m.clear()\n    return 1\nvar m: Dictionary<i32, (i32) -> i32> = [:]\nlet f: (i32) -> i32 = inc\n_ = m.tryInsert(1, f@move)\nlet r = m[1](clear(m@uniq))");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.Contains(c.Problems, x => x.Code.IsLoanConflict());
        Assert.False(c.Generate(TextWriter.Null));
    }
}
