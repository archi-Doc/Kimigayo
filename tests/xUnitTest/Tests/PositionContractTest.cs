// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 4.6.2, 8.4.7, 8.4.7.3: Position and PositionRange are closed Contracts. Every integer Type conforms to
/// Position through a built-in conformance without gaining members, PrimitiveInteger implies Position, and the Contracts'
/// refinement and Self clauses supply Copy, Owned, Equatable and Utf8Format.</summary>
public class PositionContractTest
{
    private const string Resolve =
        "func f<P>(p: P, n: isize) -> isize\n    P is Position\n    match p.tryResolve(n)\n        .Some(let q) => return q\n        .None => return -1\n" +
        "func g<T>(t: T, n: isize) -> isize\n    T is PrimitiveInteger\n    return f(t, n)\n";

    private const string Cases =
        "require f(3, 5) == 3 and f(5, 5) == 5 and f(6, 5) == -1 and f(-1, 5) == -1 and f(0, -1) == -1 else => $abort(\"i32\")\n" +
        "require f(3@u8, 5) == 3 and f(300@u16, 400) == 300 and f(18446744073709551615@u64, 5) == -1 and f(2@i128, 2) == 2 else => $abort(\"wide\")\n" +
        "require f(4@usize, 4) == 4 and f(4@isize, 3) == -1 and f(-9223372036854775808@i64, 1) == -1 and g(2@i8, 3) == 2 else => $abort(\"other\")\n" +
        "Console.writeLine(\"ok\")";

    [Fact]
    public void IntegersResolveThroughTheBuiltInConformance()
        => ScalarEmissionTest.EmitFixture("PositionIntegers", Resolve + Cases, "ok\n");

    [Fact]
    public void ThePositionRequirementSuppliesItsCapabilities()
    {
        var c = MinimalEmissionTest.Analyze("func twice<P>(p: P) -> bool\n    P is Position\n    let a = p\n    let b = p\n    Console.writeLine(\"\\(a)\")\n    return a == b\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("struct S\n    Self is Position\n    public func tryResolve(self: Self, length: isize) -> Option<isize> => .None\n()")]
    [InlineData("struct W<T>\n    let value: T\n    Self is PositionRange when T is Copy\n()")]
    public void UserTypesCannotConform(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidSelfClause_Kd);
    }

    [Fact]
    public void IntegersGainNoPositionMembers()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = 3\nlet r = x.tryResolve(5)");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }
}
