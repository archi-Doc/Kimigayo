// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.3, 8.4.8.1, 8.7: an available Type-identity premise `X is U` makes X and U one Type in its scope, a
/// function body or a conditional conformance's block, by substitution.</summary>
public class TypeIdentityTest
{
    private const string Pair =
        "contract Pick\n    func pick(self: ref/Self) -> bool\n" +
        "struct Pair<S, E>\n    S is PrimitiveInteger\n    E is PrimitiveInteger\n    let a: S\n    let b: E\n" +
        "    public init(a: S, b: E)\n        self.a = a\n        self.b = b\n" +
        "    Self is Pick when E is S\n        public func pick(self: ref/Self) -> bool\n            return self.a < self.b\n";

    private const string Uses =
        "func same<A, B>(a: A, b: B) -> A\n    B is A\n    return b@move\n" +
        "func check<P>(p: ref/P) -> bool\n    P is Pick\n    return p.pick()\n" +
        "let p = Pair<i32, i32>.init(1, 2)\n" +
        "require p.pick() and check(p@ref) and same(3, 4) == 4 else => $abort(\"identity\")\nConsole.writeLine(\"ok\")";

    [Theory]
    [InlineData("func same<A, B>(a: A, b: B) -> A\n    B is A\n    return b@move")]
    [InlineData("func same<A, B>(a: A, b: B) -> A\n    B is A\n    let x: A = b@move\n    return x@move")]
    [InlineData("func same<A, B>(a: A, b: B) -> B\n    B is A\n    let y: B = a@move\n    return y@move")]
    [InlineData("func less<A, B>(a: A, b: B) -> bool\n    A is PrimitiveInteger\n    B is A\n    return a < b")]
    [InlineData("func take<T>(x: T, y: T) -> () => ()\nfunc pass<A, B>(a: A, b: B) -> ()\n    B is A\n    A is Copy\n    take(a, b)")]
    [InlineData("func same<A, B>(b: B) -> A\n    A is B\n    B is A\n    return b@move")]
    [InlineData("func same<A, B, C>(b: B) -> C\n    A is B\n    A is C\n    return b@move")]
    [InlineData("func less<A, B, C>(b: B, c: C) -> bool\n    A is B\n    A is C\n    C is PrimitiveInteger\n    return b < c")]
    [InlineData("func less<A, B, C>(b: B, c: C) -> bool\n    A is C\n    A is B\n    B is PrimitiveInteger\n    return b < c")]
    [InlineData("func same<A, B, C>(b: Array<B>) -> Array<C>\n    A is B\n    A is C\n    return b@move")]
    public void PremiseMakesOneTypeInItsFunction(string source)
    {
        var c = MinimalEmissionTest.Analyze(source + "\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Fact]
    public void DifferentParametersStayDifferentWithoutAPremise()
    {
        var c = MinimalEmissionTest.Analyze("func same<A, B>(a: A, b: B) -> A\n    return b@move\n()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.TypeMismatch_Kd);
    }

    [Fact]
    public void ConditionalConformanceUsesItsIdentityCondition()
        => ScalarEmissionTest.EmitFixture("TypeIdentityConformance", Pair + Uses, "ok\n");

    [Fact]
    public void EquivalentBoundaryTypesKeepTheirIntegerCapability()
    {
        const string Source = "func total<A, B, C>(a: B, b: C) -> B\n" +
            "    A is B\n    A is C\n    C is PrimitiveInteger\n" +
            "    var sum: B = a\n    for i in a..=b\n        sum = sum + i\n    return sum\n" +
            "require total<i32, i32, i32>(0, 3) == 6 else => $abort(\"equivalent boundaries\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("TypeIdentityRange", Source, "ok\n");
    }

    [Fact]
    public void IdentityChainsAreNotLimitedToSixteenSteps()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 24).Select(i => $"T{i}"));
        var premises = string.Join("\n", Enumerable.Range(0, 23).Select(i => $"    T{i} is T{i + 1}"));
        var c = MinimalEmissionTest.Analyze($"func same<{parameters}>(x: T0) -> T23\n{premises}\n    return x@move\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void IdentityClosureReusesItsScratchStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("func same<A, B, C>(b: B) -> C\n    A is B\n    A is C\n    return b@move\n()");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(c.Bind().IsComplete);
        }

        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    [Fact]
    public void RefutedIdentityLeavesTheMemberInapplicable()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let p = Pair<i32, i64>.init(1, 2)\nlet r = p.pick()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
