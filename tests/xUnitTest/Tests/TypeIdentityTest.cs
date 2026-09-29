// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
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
    public void RefutedIdentityLeavesTheMemberInapplicable()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let p = Pair<i32, i64>.init(1, 2)\nlet r = p.pick()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
