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
    [InlineData("struct N\n    Self is PrimitiveInteger\n()")]
    // A user Contract that refines a closed Contract grants no conformance to it either (SPEC 8.4.7).
    [InlineData("contract MyPos: Position\nstruct S\n    Self is Copy\n    Self is MyPos\n    public func tryResolve(self: Self, length: isize) -> Option<isize> => .Some(0)\n    public func equals(self: ref/Self, other: ref/Self) -> bool => true\n    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => .Ok(())\n    public init() => ()\nlet values: Array<i32> = [1, 2, 3]\nlet b = values[S.init()]")]
    [InlineData("contract MyRange: PositionRange\nstruct W<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n    Self is Copy when T is Copy\n    Self is MyRange when T is Copy and Owned\n        public func tryResolve(self: Self, length: isize) -> Option<ResolvedRange> => (0..1).tryResolve(length)\n        public func equals(self: ref/Self, other: ref/Self) -> bool => true\n        public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull> => .Ok(())\nlet values: Array<i32> = [1, 2, 3]\nlet b = values[W<i32>.init(value: 3)]")]
    public void UserTypesCannotConform(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.ClosedContractConformance_Kd);
    }

    // A user Contract may refine a closed one as a Constraint; the closed Types still satisfy it only through their own conformance.
    [Fact]
    public void ARefiningContractIsAnOrdinaryConstraint()
    {
        var c = MinimalEmissionTest.Analyze("contract MyPos: Position\nfunc f<P>(p: P) -> bool\n    P is MyPos\n    return p == p\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void IntegersGainNoPositionMembers()
    {
        var c = MinimalEmissionTest.Analyze("let x: i32 = 3\nlet r = x.tryResolve(5)");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnresolvedBinding_Kd);
    }
}
