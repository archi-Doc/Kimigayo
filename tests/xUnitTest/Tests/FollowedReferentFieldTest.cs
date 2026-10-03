// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 13.5.5.1, 15.6.2: a field or Tuple element of an explicitly selected referent (`p@follow.x`) is reached through the
// reference as `p.x` is, and a borrow of it depends on that reference (SPEC 13.5.5). It was UnsupportedOwnership_Kd, and
// Binding gave `p@follow.x@ref` an Origin unrelated to `p`.
public class FollowedReferentFieldTest
{
    private const string S = "struct S\n    public var x: i32\n    public var inner: (i32, i32)\n    public init(x: i32)\n        self.x = x\n        self.inner = (x + 1, x + 2)\n    drop => ()\n";

    [Theory]
    [InlineData("ReadWrite", "func read(p: ref/S) -> i32 => p@follow.x\nfunc deep(p: ref/S) -> i32 => p@follow.inner.1\nfunc write(p: uniq/S) => p@follow.x = 9\nfunc bump(p: uniq/S) => p@follow.inner.0 += 10\nvar s = S.init(4)\nrequire read(s@ref) == 4 and deep(s@ref) == 6 else => $abort(\"read\")\nwrite(s@uniq)\nbump(s@uniq)\nrequire s.x == 9 and s.inner.0 == 15 else => $abort(\"write\")")]
    [InlineData("ReturnedBorrow", "func look(p: ref/S) -> ref/i32 during p => p@follow.x@ref\nvar s = S.init(4)\nlet r = look(s@ref)\nrequire r == 4 else => $abort(\"look\")")]
    [InlineData("SharedReads", "func f(p: ref/S) -> i32\n    let view = p@follow.x@ref\n    let y = p@follow.x\n    return view + y\nvar s = S.init(4)\nrequire f(s@ref) == 8 else => $abort(\"read\")")]
    [InlineData("Uses", "func bump(v: uniq/i32) => v@follow += 1\nfunc same(a: ref/S, b: ref/S) -> bool => a@follow.x == b@follow.x\nfunc g(p: uniq/S) -> i32\n    bump(p@follow.x@uniq)\n    let t = p@follow.inner\n    let c = p@follow.inner.0 < p@follow.x\n    p@follow.inner = (1, 2)\n    return t.0 + p@follow.x\nvar s = S.init(4)\nvar s2 = S.init(5)\nrequire g(s@uniq) == 10 and same(s@ref, s2@ref) and s.inner.1 == 2 else => $abort(\"uses\")")]
    [InlineData("Match", "func h(p: uniq/S) -> i32\n    match p@follow.inner\n        (let a, let b) => return a + b\nvar s = S.init(4)\nrequire h(s@uniq) == 11 else => $abort(\"match\")")]
    [InlineData("Tuple", "func read(p: ref/(i32, i32)) -> i32 => p@follow.1\nlet t = (1, 2)\nrequire read(t@ref) == 2 else => $abort(\"tuple\")")]
    public void AFollowedReferentFieldIsReachedThroughItsReference(string name, string body)
        => ScalarEmissionTest.EmitFixture("FollowedReferentField" + name, S + body, string.Empty);

    [Theory]
    [InlineData("    let view = p@follow.x@ref\n    p@follow.x = 3\n    return view", "p")]
    [InlineData("    let view = p@follow.x@ref\n    p.x = 3\n    return view", "p.x")]
    [InlineData("    let view = p.x@ref\n    p@follow.x = 3\n    return view", "p")]
    public void AWriteWhileAFollowedFieldIsBorrowedConflicts(string body, string conflict)
    {
        var c = MinimalEmissionTest.Analyze(S + "func f(p: uniq/S) -> i32\n" + body + "\npublic func main() => ()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == conflict);
        Assert.False(c.Emission.Validate(out _));
    }

    [Fact]
    public void AWriteThroughASharedReferenceStaysRejected()
    {
        var error = Assert.Single(DiagnosticCorpus.Check(S + "func write(p: ref/S) => p@follow.x = 9\npublic func main() => ()\n").Diagnostics);
        Assert.Equal(nameof(DiagnosticCode.SharedPathAccess_Kd), error.Code);
    }
}
