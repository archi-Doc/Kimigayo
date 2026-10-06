// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

// SPEC 3.5, 8.9, 8.10, 13.5.5.1: the remaining bare positions of a pair-layer Place. A bare argument at a by-value parameter and a
// bare result or arm are acquired by each Semantics case as the case Type is; a write through a pair element of a writable Tuple
// has the owner case's Write; a bare element whose case Type is an exclusive reference is a located limit, as the concrete element.
public sealed class PairBarePositionTest
{
    [Theory]
    [InlineData("func keep<F>(f: F) -> F => f@move\nfunc f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let kept = keep(value)\n    _ = kept@follow\n")]
    [InlineData("func f<s/T>(value: s/T) -> s/T\n    s is owner or uniq\n    T is Copy\n    return value\n")]
    [InlineData("func f<s/T>(value: s/T, c: bool) -> s/T\n    s is owner or uniq\n    T is Copy\n    return if c => value else => value\n")]
    [InlineData("func f<s/T>(value: s/T, x: T) -> ()\n    s is owner or uniq\n    T is Copy\n    var p = (value@move, 1)\n    p.0@follow = x\n    _ = p.0@follow\n")]
    public void ABarePositionIsAcquiredPerCase(string definition)
    {
        var c = MinimalEmissionTest.Analyze(definition + "public func main() -> ()\n    var n: i32 = 1\n    _ = f(n" + (definition.Contains("x: T", StringComparison.Ordinal) ? ", 9" : definition.Contains("c: bool", StringComparison.Ordinal) ? ", true" : string.Empty) + ")\n");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void ABareElementOfAnExclusiveCaseIsALocatedLimit()
    {
        var source = "func f<s/T>(value: s/T, other: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    var arr: [2 of s/T] = [value@move, other@move]\n    let e = arr[0]\n    _ = e@follow\npublic func main() -> ()\n    var n: i32 = 1\n    var m: i32 = 2\n    f(n, m)\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Severity == DiagnosticSeverity.Error);
        Assert.Equal((nameof(DiagnosticCode.UnsupportedOwnership_Kd), "arr[0]"), (error.Code, source.Substring(error.Span!.Value.Start, error.Span.Value.Length)));
        var fact = Assert.Single(error.Reason!);
        Assert.Equal(("case", "s = uniq"), (fact.Name, fact.Value));
    }

    // SPEC 11.6 of plan C: `@s` on a pair binding does not bind today (STATUS).
    [Fact]
    public void ASemanticsOperationOnAPairBindingDoesNotBind()
    {
        var source = "func f<s/T>(value: s/T) -> ()\n    s is owner or uniq\n    T is Copy\n    let v = value@s\n    _ = v@follow\npublic func main() -> ()\n    var n: i32 = 1\n    f(n)\n";
        Assert.Contains(DiagnosticCorpus.Check(source).Diagnostics, static x => x.Code == nameof(DiagnosticCode.InvalidTypeFormation_Kd));
    }
}
