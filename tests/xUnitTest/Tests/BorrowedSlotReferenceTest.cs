// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 3.3.6, 15.6.2: `p.0@ref` of a borrowed root `p` whose element holds a reference borrows that element's slot in the
// caller's storage. It was lowered as a borrow of a temporary copy of the stored reference, so a result `during p`
// pointed into the callee's frame (non-generic), or failed generation in a generic instance with a reference argument.
public class BorrowedSlotReferenceTest
{
    // Reuses the stack below the caller after the call, so a reference into the callee's frame reads overwritten storage.
    private const string Clobber = "func clobber(n: i64, m: i64) -> i64\n    let x = (n + 7, m + 9, n * m, 77@i64)\n    let y = x.0 * 3 + x.1 + x.2 + x.3\n    return y\n";

    [Theory]
    [InlineData("Shared", "func pick(p: ref/(ref/i32 during a, i32)) -> ref/(ref/i32 during a) during p => p.0@ref\nvar value = 1\nlet t = (value@ref, 2)\nlet r = pick(t@ref)\nlet z = clobber(1234567, 7654321)\nrequire z > 0 and r == 1 else => $abort(\"slot\")")]
    [InlineData("Exclusive", "func slot(p: uniq/(uniq/i32 during a, i32)) -> uniq/(uniq/i32 during a) during p => p.0@uniq\nvar value = 1\nvar t = (value@uniq, 2)\nlet s = slot(t@uniq)\nlet z = clobber(1234567, 7654321)\ns@follow@follow = 5\nrequire z > 0 and value == 5 else => $abort(\"slot\")")]
    [InlineData("GenericShared", "func pick<A, B>(p: ref/(A, B)) -> ref/A during p => p.0@ref\nvar value = 1\nlet t = (value@ref, 2)\nlet r = pick(t@ref)\nlet z = clobber(1234567, 7654321)\nrequire z > 0 and r == 1 else => $abort(\"slot\")")]
    [InlineData("GenericExclusive", "func pick<A, B>(p: ref/(A, B)) -> ref/A during p => p.0@ref\nvar value = 1\nlet t = (value@uniq, 2)\nlet r = pick(t@ref)\nlet z = clobber(1234567, 7654321)\nrequire z > 0 and r@follow@follow == 1 else => $abort(\"slot\")")]
    // SPEC 15.3.5: the result's omitted inner Origin is `p`; the stored `a` outlives `p`, so the slot fits by covariance. Control
    // flow judged the result without the scope's premises (IncompatibleResult_Kd) and lowering required the exact Type.
    [InlineData("StructShortened", "struct H {a}\n    public let item: ref/i32 during a\n    public let extra: i32\n    public init(item: ref/i32 during a, extra: i32)\n        self.item = item\n        self.extra = extra\nfunc pick(p: ref/H) -> ref/(ref/i32) during p => p.item@ref\nvar value = 1\nlet h = H.init(value@ref, 2)\nlet r = pick(h@ref)\nlet z = clobber(1234567, 7654321)\nrequire z > 0 and r == 1 else => $abort(\"slot\")")]
    [InlineData("TupleShortened", "func pick(p: ref/(ref/i32 during a, i32)) -> ref/(ref/i32) during p => p.0@ref\nvar value = 1\nlet t = (value@ref, 2)\nlet r = pick(t@ref)\nlet z = clobber(1234567, 7654321)\nrequire z > 0 and r == 1 else => $abort(\"slot\")")]
    [InlineData("GenericValue", "func pick<A, B>(p: ref/(A, B)) -> ref/A during p => p.0@ref\nlet t = ((1, 3), 2)\nlet r = pick(t@ref)\nrequire r.0 == 1 and r.1 == 3 else => $abort(\"slot\")")]
    public void AReferenceElementIsBorrowedInItsSlot(string name, string source)
        => ScalarEmissionTest.EmitFixture("BorrowedSlotReference" + name, Clobber + source, string.Empty);

    [Fact]
    public void TheSlotIsBorrowedInPlace()
    {
        var c = MinimalEmissionTest.Analyze("func pick(p: ref/(ref/i32 during a, i32)) -> ref/(ref/i32 during a) during p => p.0@ref\nvar value = 1\nlet t = (value@ref, 2)\nlet r = pick(t@ref)\nrequire r == 1 else => $abort(\"slot\")");
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        var pick = Assert.Single(c.Ownership.Bodies, static body => body.Function.Name == "pick");
        Assert.Contains(pick.Operations, static operation => operation.Kind == OwnershipOperationKind.Borrow && operation.Source.ToString() == "p.0");
        // No temporary holds a copy of the stored `ref/i32` for the borrow.
        Assert.DoesNotContain(pick.Places, static place => place.Kind == OwnershipPlaceKind.Temporary && place.Type is { Kind: BoundTypeKind.Semantics, Components: [{ Kind: BoundTypeKind.Primitive }] });
    }

    // A stored Origin that is not known to outlive the result's stays a mismatch at the result.
    [Fact]
    public void AnUnrelatedInnerOriginIsNotShortened()
    {
        var source = "struct H {a}\n    public let item: ref/i32 during a\n    public init(item: ref/i32 during a) => self.item = item\nfunc pick(p: ref/H, q: ref/i32) -> ref/(ref/i32 during q) during p => p.item@ref\npublic func main() => ()\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics);
        Assert.Equal(nameof(Kimi.DiagnosticCode.UnprovenOriginRelation_Kd), error.Code);
        Assert.Equal("p.item@ref", source.Substring(error.Span!.Value.Start, error.Span.Value.Length));
    }

    [Theory]
    [InlineData("var t = (value@ref, 2)\nlet r = pick(t@ref)\nlet w = t@uniq\nrequire r == 1 else => $abort(\"slot\")", "t")]
    [InlineData("var t = (value@ref, 2)\nlet r = pick(t@ref)\nt.1 = 9\nrequire r == 1 else => $abort(\"slot\")", "t.1 = 9")]
    public void TheCallerKeepsTheRootLoan(string body, string conflict)
    {
        var c = MinimalEmissionTest.Analyze("func pick(p: ref/(ref/i32 during a, i32)) -> ref/(ref/i32 during a) during p => p.0@ref\nvar value = 1\n" + body);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.DoesNotContain(c.Ownership.Issues, static x => x.Failure == OwnershipFailure.Unsupported);
        Assert.Contains(c.Ownership.Issues, x => x.Failure == OwnershipFailure.ComparisonLoanConflict && x.Source.ToString() == conflict);
        Assert.False(c.Emission.Validate(out _));
    }
}
