// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScalarBorrowShorthandTest
{
    // SPEC 15.6.3: `v@uniq` / `v@ref` on scalar Places, including the chapter's own example.
    [Theory]
    [InlineData("Spec", "func bump(n: uniq/i32)\n    ()\nvar v = 0\nbump(v@uniq)\nbump(v@uniq)")]
    [InlineData("Shared", "func read(n: ref/i32) -> i32 => 0\nlet v = 0\nlet r = read(v@ref)\nrequire r == 0 else => $abort(\"value\")")]
    [InlineData("Field", "struct P\n    public var tag: i32 = 7\nfunc bump(n: uniq/i32)\n    ()\nvar p = P.init()\nbump(p.tag@uniq)\nrequire p.tag == 7 else => $abort(\"value\")")]
    [InlineData("Bool", "func bump(n: uniq/bool)\n    ()\nvar v = true\nbump(v@uniq)\nrequire v else => $abort(\"value\")")]
    public void ExecutesScalarShorthandBorrows(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ScalarBorrowShorthand" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("func bump(n: uniq/i32)\n    ()\nvar v = 0\nlet a = v@uniq\nlet b = v@uniq\nbump(a)")]
    [InlineData("func bump(n: uniq/i32)\n    ()\nvar v = 0\nlet a = v@uniq\nlet x = v\nbump(a)")]
    public void RejectsOverlappingScalarBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RejectsExclusiveBorrowOfImmutableScalar()
    {
        var c = MinimalEmissionTest.Analyze("func bump(n: uniq/i32)\n    ()\nlet v = 0\nbump(v@uniq)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
