// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Xunit;

namespace XunitTest;

public class DisjointBorrowedProjectionTest
{
    private const string Pair = "struct Counter\n    public var value: i32 = 1\nstruct Pair\n    public var left: Counter = Counter.init()\n    public var right: Counter = Counter.init()\n";

    // SPEC 15.6.2: distinct stored fields/Tuple elements are disjoint, and a
    // Read is allowed against an existing shared Loan. SPEC 15.6.3 suspends
    // only conflicting access through a reborrowed parent.
    [Theory]
    [InlineData("Fields", Pair + "func both(p: uniq/Pair)\n    let a = p.left@uniq\n    let b = p.right@uniq\n    a.value += 1\n    b.value += 2\n    a.value += b.value\nvar pair = Pair.init()\nboth(pair@uniq)\nrequire pair.left.value == 5 and pair.right.value == 3 else => $abort(\"value\")")]
    [InlineData("Tuple", Pair + "func both(p: uniq/(Counter, Counter))\n    let a = p.0@uniq\n    let b = p.1@uniq\n    b.value += 1\n    a.value += 1\nvar pair = (Counter.init(), Counter.init())\nboth(pair@uniq)\nrequire pair.0.value == 2 and pair.1.value == 2 else => $abort(\"value\")")]
    [InlineData("SharedPair", Pair + "func same(p: uniq/Pair) -> bool\n    let a = p.left@ref\n    let b = p.left@ref\n    return a.value == b.value\nvar pair = Pair.init()\nrequire same(pair@uniq) else => $abort(\"value\")")]
    [InlineData("ParentRead", Pair + "func read(p: uniq/Counter) -> i32\n    let r = p@follow@ref\n    let n = p.value\n    return n + r.value\nvar counter = Counter.init()\nrequire read(counter@uniq) == 2 else => $abort(\"value\")")]
    [InlineData("ArrayParentRead", "var a: [1 of i32] = [1]\nlet b = a@uniq\nlet r = b@follow@ref\nlet n = b[0]\nrequire n + r[0] == 2 else => $abort(\"value\")")]
    public void AcceptsDisjointOrSharedAccess(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("DisjointBorrowedProjection" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@uniq\n    let b = p.left@uniq\n    a.value += 1")]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@ref\n    let b = p.left@uniq\n    b.value += a.value")]
    [InlineData(Pair + "func f(p: uniq/Counter)\n    let r = p@follow@ref\n    p.value = 2\n    let m = r.value")]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@uniq\n    let b = p@follow@uniq\n    a.value += 1")]
    [InlineData(Pair + "func g(p: uniq/Pair)\n    ()\nfunc f(p: uniq/Pair)\n    let a = p.left@uniq\n    g(p)\n    a.value += 1")]
    [InlineData("func f(p: uniq/(i32, i32))\n    let a = p@follow@ref\n    p.0 = 3\n    let x = a.0")]
    [InlineData("var a: [1 of i32] = [1]\nlet b = a@uniq\nlet r = b@follow@ref\nlet c = b@follow@uniq\nlet m = r[0]")]
    public void RejectsOverlappingConflicts(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    // SPEC 15.6: direct inline paths below a borrowed base are Places.
    [Theory]
    [InlineData("NestedWrite", Pair + "func f(p: uniq/Pair)\n    p.left.value = 3\n    p.right.value += 4\n    let before = p.left.value++\n    require before == 3 else => $abort(\"result\")\nvar pair = Pair.init()\nf(pair@uniq)\nrequire pair.left.value == 4 and pair.right.value == 5 else => $abort(\"value\")")]
    [InlineData("NestedRead", Pair + "func f(p: ref/Pair) -> i32 => p.left.value + p.right.value\nvar pair = Pair.init()\npair.right.value = 5\nrequire f(pair@ref) == 6 else => $abort(\"value\")")]
    [InlineData("NestedDisjoint", Pair + "func f(p: uniq/Pair)\n    let a = p.left@uniq\n    p.right.value = 4\n    a.value += p.right.value\nvar pair = Pair.init()\nf(pair@uniq)\nrequire pair.left.value == 5 and pair.right.value == 4 else => $abort(\"value\")")]
    [InlineData("NestedTuple", Pair + "func f(p: uniq/(i32, Counter))\n    p.1.value = p.0 + 3\n    p.1.value++\nvar t: (i32, Counter) = (1, Counter.init())\nf(t@uniq)\nrequire t.0 == 1 and t.1.value == 5 else => $abort(\"value\")")]
    [InlineData("NestedTupleElement", Pair + "func f(p: uniq/((i32, i32), Counter))\n    p.0.1 += 2\n    p.0.0 = p.0.1\n    let before = p.0.1--\n    p.1.value = before\nvar t: ((i32, i32), Counter) = ((1, 2), Counter.init())\nf(t@uniq)\nrequire t.0.0 == 4 and t.0.1 == 3 and t.1.value == 4 else => $abort(\"value\")")]
    public void ExecutesNestedBorrowedPaths(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Ownership.Result.IsVerified, string.Join('\n', c.Ownership.Issues));
        ScalarEmissionTest.EmitFixture("DisjointBorrowedProjection" + name, source, string.Empty);
    }

    [Theory]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@uniq\n    p.left.value = 3\n    a.value += 1")]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@ref\n    p.left.value = 3\n    let x = a.value")]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p.left@uniq\n    let x = p.left.value\n    a.value += x")]
    [InlineData(Pair + "func f(p: uniq/Pair)\n    let a = p@follow@ref\n    p.right.value = 3\n    let x = a.left.value")]
    public void RejectsOverlappingNestedPaths(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Ownership.Issues, x => x.Failure == Kimi.Compiler.OwnershipFailure.ComparisonLoanConflict);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }

    [Theory]
    [InlineData("func f(p: ref/Pair)\n    p.left.value = 3")]
    [InlineData("func f(p: ref/((i32, i32), bool))\n    p.0.1 += 1")]
    [InlineData("func f(p: ref/((i32, i32), bool))\n    p.0.1 = 1")]
    public void RejectsNestedWriteThroughSharedBase(string source)
    {
        var c = MinimalEmissionTest.Analyze(Pair + source);
        Assert.False(c.Binding.Result.IsComplete && c.Ownership.Result.IsVerified);
        Assert.False(c.Emission.WriteIr(TextWriter.Null, out _));
    }
}
