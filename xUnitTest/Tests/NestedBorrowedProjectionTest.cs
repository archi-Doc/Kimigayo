// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class NestedBorrowedProjectionTest
{
    private const string Outer = "struct Counter\n    public var value: i32 = 1\nstruct Inner\n    public var c: Counter = Counter.init()\n    public var d: Counter = Counter.init()\nstruct Outer\n    public var tag: i32 = 7\n    public var inner: Inner = Inner.init()\n";

    // SPEC 15.6: explicit borrows of nested inline Places below a borrowed base.
    [Theory]
    [InlineData("Exclusive", Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    a.value += 1\n    p.tag = a.value + 7\nvar o = Outer.init()\nf(o@uniq)\nrequire o.inner.c.value == 2 and o.tag == 9 else => $abort(\"value\")")]
    [InlineData("Shared", Outer + "func f(p: ref/Outer) -> i32\n    let a = p.inner.d@ref\n    let b = p.inner.d@ref\n    return a.value + b.value + p.tag\nvar o = Outer.init()\nrequire f(o@ref) == 9 else => $abort(\"value\")")]
    [InlineData("Siblings", Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    let b = p.inner.d@uniq\n    b.value += 2\n    a.value += b.value\n    p.tag = 0\nvar o = Outer.init()\nf(o@uniq)\nrequire o.inner.c.value == 4 and o.inner.d.value == 3 and o.tag == 0 else => $abort(\"value\")")]
    [InlineData("Tuple", Outer + "func f(p: uniq/(i32, (Counter, Counter)))\n    let a = p.1.0@uniq\n    let b = p.1.1@uniq\n    a.value += 1\n    b.value += p.0\nvar t: (i32, (Counter, Counter)) = (5, (Counter.init(), Counter.init()))\nf(t@uniq)\nrequire t.1.0.value == 2 and t.1.1.value == 6 else => $abort(\"value\")")]
    [InlineData("SiblingUpdate", Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    p.tag += a.value\n    p.inner.d.value += a.value\n    p.tag++\n    a.value += p.tag\nvar o = Outer.init()\nf(o@uniq)\nrequire o.tag == 9 and o.inner.d.value == 2 and o.inner.c.value == 10 else => $abort(\"value\")")]
    [InlineData("SelfUpdate", Outer + "func f(p: uniq/Outer)\n    p.inner.c.value += p.inner.c.value\nvar o = Outer.init()\nf(o@uniq)\nrequire o.inner.c.value == 2 else => $abort(\"value\")")]
    public void ExecutesNestedExplicitBorrows(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("NestedBorrowedProjection" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    let b = p.inner@uniq\n    a.value += 1")]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    p.inner.c.value = 2\n    a.value += 1")]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner@ref\n    let b = p.inner.c@uniq\n    b.value += a.d.value")]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    let b = p.inner.c@ref\n    a.value += b.value")]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner.c@uniq\n    p.inner.c.value += 1\n    a.value += 1")]
    [InlineData(Outer + "func f(p: uniq/Outer)\n    let a = p.inner@ref\n    p.inner.d.value++\n    let x = a.c.value")]
    public void RejectsOverlappingNestedBorrows(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Fact]
    public void RejectsExclusiveNestedBorrowThroughSharedBase()
    {
        var c = MinimalEmissionTest.Analyze(Outer + "func f(p: ref/Outer)\n    let a = p.inner.c@uniq\n    a.value += 1");
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
