// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.2, 8.8.1, 8.3: an available Type-identity premise `X is U` makes X and U one Type in its scope, a
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
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

    // SPEC 8.2: an expression Type with an identity premise substituted keeps its stored fields, so a field of a substituted
    // range keeps its substituted Type in every instance.
    [Fact]
    public void SubstitutedTypesKeepTheirFields()
    {
        const string Source = "func last<A, B>(r: Range<A, B>) -> B\n    A is PrimitiveInteger\n    B is A\n    return r.end\n" +
            "func first<A, B>(r: ClosedRange<A, B>) -> A\n    A is PrimitiveInteger\n    B is A\n    let copy = r\n    return copy.start\n" +
            "require last(1..4) == 4 and last(2@u8..9@u8) == 9 and first(3@i64..=5) == 3 else => $abort(\"fields\")\nConsole.writeLine(\"ok\")";
        ScalarEmissionTest.EmitFixture("TypeIdentityFields", Source, "ok\n");
    }

    [Fact]
    public void IdentityChainsAreNotLimitedToSixteenSteps()
    {
        var parameters = string.Join(", ", Enumerable.Range(0, 24).Select(i => $"T{i}"));
        var premises = string.Join("\n", Enumerable.Range(0, 23).Select(i => $"    T{i} is T{i + 1}"));
        var c = MinimalEmissionTest.Analyze($"func same<{parameters}>(x: T0) -> T23\n{premises}\n    return x@move\n()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c));
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void IdentityClosureReusesItsScratchStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("func same<A, B, C>(b: B) -> C\n    A is B\n    A is C\n    return b@move\n()");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // SPEC 8.2.3, DECISIONS BU-72: a member or element access whose receiver's Type, through its Semantics layers, is a Type parameter
    // that the premises make a complete Type publishes that identity, which a warm rebind publishes again; an access without an identity
    // premise publishes none.
    [Theory]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: T) -> i32\n    T is Pair\n    return (s).a", "(s).a", BoundTypeKind.Nominal)]
    [InlineData("func f<T, U>(s: ref/T) -> i32\n    T is U\n    U is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: ref/T, i: isize) -> i32\n    T is Array<Pair>\n    return s[i].a", "s[i]", BoundTypeKind.Array)]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is (Pair, i32)\n    return s.0.a", "s.0", BoundTypeKind.Tuple)]
    [InlineData("func f(s: ref/Pair) -> i32\n    return s.a", "s.a", null)]
    [InlineData("func f<T>(s: ref/(T, i32)) -> i32\n    T is Owned\n    return s.1", "s.1", null)]
    public void AnAccessPublishesTheIdentityItsReceiverWasResolvedThrough(string source, string access, BoundTypeKind? identical)
    {
        var c = CompilationTestHelper.BindSuccess("struct Pair\n    public var a: i32 = 1\n    public init() => ()\n" + source + "\n()");
        var node = (BinaryKoto)KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is MemberAccessKoto or IndexKoto && x.ToString() == access);
        var premise = HirTables.IdentityPremise(node);
        if (identical is null)
        {
            Assert.Null(premise);
            return;
        }

        var receiver = HirTables.Meaning(KotoHelper.UnwrapParentheses(node.Left)) as BoundType;
        while (receiver is { Kind: BoundTypeKind.Semantics, Components.Count: 1 })
        {
            receiver = receiver.Components[0];
        }

        Assert.Equal(ConstraintKind.TypeIdentity, premise?.Kind);
        Assert.Equal((BoundTypeKind.Parameter, "T"), (premise!.Subject!.Kind, premise.Subject.Name));
        Assert.Equal(identical, premise.RequiredType!.Kind);
        Assert.Same(receiver, premise.RequiredType);
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.Same(premise, HirTables.IdentityPremise(node));
    }

    [Fact]
    public void RefutedIdentityLeavesTheMemberInapplicable()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let p = Pair<i32, i64>.init(1, 2)\nlet r = p.pick()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
