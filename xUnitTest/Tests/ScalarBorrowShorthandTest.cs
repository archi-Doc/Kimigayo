// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class ScalarBorrowShorthandTest
{
    private const string P = "struct P\n    public var tag: i32 = 7\n    public var flag: bool = true\nfunc bump(n?: uniq/i32)\n    ()\nfunc read(n?: ref/i32) -> bool => true\n";

    // SPEC 15.6.3: `v@uniq` / `v@ref` on scalar Places, including the chapter's own example.
    // SPEC 3.6.2 and 13.5.5.2: owned scalar temporaries are materialized and may be borrowed explicitly.
    [Theory]
    [InlineData("Spec", "func bump(n?: uniq/i32)\n    ()\nvar v = 0\nbump(v@uniq)\nbump(v@uniq)")]
    [InlineData("Shared", "func read(n?: ref/i32) -> i32 => 0\nlet v = 0\nlet r = read(v@ref)\nrequire r == 0 else => $abort(\"value\")")]
    [InlineData("Field", "struct P\n    public var tag: i32 = 7\nfunc bump(n?: uniq/i32)\n    ()\nvar p = P.init()\nbump(p.tag@uniq)\nrequire p.tag == 7 else => $abort(\"value\")")]
    [InlineData("Bool", "func bump(n?: uniq/bool)\n    ()\nvar v = true\nbump(v@uniq)\nrequire v else => $abort(\"value\")")]
    [InlineData("Temporary", "func read(n?: ref/i32) -> bool => true\nrequire read(1@ref) else => $abort(\"value\")")]
    [InlineData("CallTemporary", "func read(n?: ref/i32) -> bool => true\nfunc one() -> i32 => 41\nrequire read(one()@ref) else => $abort(\"value\")")]
    [InlineData("ExclusiveTemporary", "func bump(n?: uniq/i32)\n    ()\nfunc one() -> i32 => 41\nbump(1@uniq)\nbump(one()@uniq)")]
    [InlineData("Statement", "1.25@ref")]
    [InlineData("BorrowedBase", P + "func f(p?: uniq/P)\n    bump(p.tag@uniq)\n    bump(p.tag)\n    let t = p.tag@uniq\n    p.flag = false\n    bump(t)\nvar o = P.init()\nf(o@uniq)\nrequire not o.flag and o.tag == 7 else => $abort(\"value\")")]
    [InlineData("SharedBase", P + "func f(p?: ref/P) -> bool => read(p.tag)\nlet o = P.init()\nrequire f(o@ref) else => $abort(\"value\")")]
    [InlineData("TupleElement", P + "var t: (i32, bool) = (1, true)\nbump(t.0@uniq)\nbump(t.0)\nrequire t.1 else => $abort(\"value\")")]
    public void ExecutesScalarShorthandBorrows(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ScalarBorrowShorthand" + name, source, string.Empty);
    }

    [Theory]
    [InlineData("func bump(n?: uniq/i32)\n    ()\nvar v = 0\nlet a = v@uniq\nlet b = v@uniq\nbump(a)")]
    [InlineData("func bump(n?: uniq/i32)\n    ()\nvar v = 0\nlet a = v@uniq\nlet x = v\nbump(a)")]
    [InlineData("func one() -> i32 => 41\nlet r = one()@ref\nlet s = r")]
    public void RejectsOverlappingScalarBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("func bump(n?: uniq/i32)\n    ()\nlet v = 0\nbump(v@uniq)")]
    [InlineData(P + "func f(p?: ref/P)\n    bump(p.tag@uniq)")]
    public void RejectsExclusiveBorrowOfImmutableScalar(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
