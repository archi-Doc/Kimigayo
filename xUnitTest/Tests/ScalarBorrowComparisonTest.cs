// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

// SPEC 13.4: safe borrows of one scalar Type compare their referent values.
public class ScalarBorrowComparisonTest
{
    private const string Source = "func same(a: ref/i32, b: ref/i32) -> bool => a == b\nfunc less(a: ref/i64, b: ref/i64) -> bool => a < b\nfunc flag(a: ref/bool, b: ref/bool) -> bool => a != b\n" +
        "let x: i32 = 7\nlet y: i32 = 7\nlet z: i32 = 8\nlet p: i64 = 1\nlet q: i64 = 2\nlet t = true\nlet f = false\n" +
        "require same(x@ref, y@ref) and not same(x@ref, z@ref) and less(p@ref, q@ref) and not less(q@ref, p@ref) and flag(t@ref, f@ref) and not flag(t@ref, t@ref) else => $abort(\"referents\")\n" +
        "require x@ref == y@ref and z@ref >= x@ref and 7@ref == x@ref else => $abort(\"temporaries\")";

    [Fact]
    public void ComparesReferentsThroughSharedBorrows()
        => ScalarEmissionTest.EmitFixture("ScalarBorrowComparison", Source, string.Empty);

    [Theory]
    [InlineData("Local", "let x: i32 = 7\nlet r = x@ref\nrequire r == 7@ref else => $abort(\"local\")")]
    [InlineData("Block", "let x: i32 = 7\ndo\n    let r = x@ref\n    require r == 7@ref else => $abort(\"block\")\n    Console.writeLine(\"ok\")")]
    [InlineData("Returned", "func first(a: ref/[2 of i32]) -> ref{a}/i32 => a[0]@ref/i32\nlet values: [2 of i32] = [7, 8]\ndo\n    let r = first(values@ref)\n    require r == 7@ref else => $abort(\"returned\")\n    Console.writeLine(\"ok\")")]
    public void ComparesReferentsOfReferenceLocals(string name, string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}")));
        Assert.True(c.Ownership.Result.IsVerified, string.Join("\n", c.Ownership.Issues.Select(x => $"{x.Failure}: {x.Source}")));
        Assert.True(c.Emission.TryPrepare(out _, out var error), MinimalEmissionTest.Describe(c, error));
        ScalarEmissionTest.EmitFixture("ScalarBorrowComparison" + name, source, name == "Local" ? string.Empty : "ok\n");
    }

    [Theory]
    [InlineData("func f(a: ref/i32, b: ref/i64) -> bool => a == b")]
    [InlineData("func f(a: ref/i32, b: i32) -> bool => a == b")]
    [InlineData("func f(a: ref/bool, b: ref/bool) -> bool => a < b")]
    [InlineData("func f(a: ref/string, b: ref/i32) -> bool => a == b")]
    public void RejectsMismatchedOrUnorderedReferents(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == Kimi.DiagnosticCode.TypeMismatch_Kd);
    }
}
