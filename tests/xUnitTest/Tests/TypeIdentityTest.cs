// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.2, 8.8.1, 8.3: an available Type-identity premise `X is U` makes X and U one Type in its scope, a
/// function body or a conditional conformance's block, by substitution.</summary>
public partial class TypeIdentityTest
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

    private const string Getter = "contract Getter\n    func get(self: ref/Self) -> i32\n";

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

    // SPEC 8.2.3, 3.4.3, 8.5.2, 8.6.4: `U is S` makes S one Type with U, so name lookup on S reads U's Contracts (identity Constraints
    // provide declarations): beside S's own premise a second same-named associated Type or requirement is ambiguous (r18, r21: accepted
    // before R7-C2c2 c2-3a's name discovery read Enumerate's rows), whichever side the identity names first (r19) and for two
    // instances of one generic Contract (n-r18-other-n: SPEC 8.5.1); alone U's requirement is S's (x1, the counterpart).
    [Theory]
    [InlineData("contract Named2\n    associate Element\nfunc g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    U is S\n    S is Named2\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData(Getter + "contract Getter2\n    func get(self: ref/Self) -> i32\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    S is Getter2\n    return v.get()", "AmbiguousBinding_Kd")]
    [InlineData(Getter + "func g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    return v.get()", "")]
    [InlineData("contract Named2\n    associate Element\nfunc g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    S is U\n    S is Named2\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData("func g<S, U>(items: ref/S, e: ref/S.Element) -> i32\n    U is Indexable<isize>\n    U is S\n    S is Indexable<i32>\n    return 0", "AmbiguousBinding_Kd")]
    public void NameLookupReadsTheIdenticalTypesContracts(string source, string codes)
    {
        var c = MinimalEmissionTest.Check(source + "\n()");
        Assert.Equal(codes, string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)));
    }

    // SPEC 8.2.3, 8.5.1, 8.6.4: identity is closed under replacing a Type inside Contract arguments, so under `U is S` the references
    // `Getter<U>` and `Getter<S>` are one and name one requirement or associated Type (v2, v3; v1 the non-generic control); `Boxed<i32>`
    // and `Boxed<i64>` stay two (v11: accepted before R7-C2c2 c2-3a). So are two written spellings under `U is i32` (w1, w2), a
    // projection argument a premise completes (w3, fa1, ar15) and arguments a decomposed identity joins (z1): PLAN G302's false
    // AmbiguousBinding_Kd before c2-3a published one candidate per Contract (ContractSolver's PremiseRows).
    [Theory]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter<U>\n    U is S\n    S is Getter<S>\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<S, U>(items: ref/S, e: ref/S.Item) -> i32\n    U is Boxed<U>\n    U is S\n    S is Boxed<S>\n    return 0", "")]
    [InlineData(Getter + "func g<S, U>(v: ref/S) -> i32\n    U is Getter\n    U is S\n    S is Getter\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<S, U>(items: ref/S, e: ref/S.Item) -> i32\n    U is Boxed<i32>\n    U is S\n    S is Boxed<i64>\n    return 0", "AmbiguousBinding_Kd")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<T, U>(v: ref/T) -> i32\n    T is Getter<U>\n    U is i32\n    T is Getter<i32>\n    return v.get()", "")]
    [InlineData("contract Boxed<X>\n    associate Item\nfunc g<T, U>(e: ref/T.Item) -> i32\n    T is Boxed<U>\n    U is i32\n    T is Boxed<i32>\n    return 0", "")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\ncontract K\n    associate Y\nfunc g<S, U>(v: ref/S) -> i32\n    U is Getter<i32>\n    U is S\n    S is K\n    S.(K).Y is i32\n    S is Getter<S.(K).Y>\n    return v.get()", "")]
    [InlineData("contract Getter<X>\n    func get(self: ref/Self) -> i32\nfunc g<T, U, S>(first: ref/U during a, second: ref/S during b, v: ref/S) -> i32\n    origin a == b\n    T is ref/U during a\n    T is ref/S during b\n    S is Getter<U>\n    S is Getter<S>\n    return v.get()", "")]
    [InlineData("contract HasItem\n    associate Item\ncontract Iter<T>\n    associate Tag\nfunc f<T, U>(x: ref/T, t: T.Tag) -> ()\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Iter<i32>\n    T is Iter<U.(HasItem).Item>\n    ()", "")]
    [InlineData("contract HasItem\n    associate Item\nfunc f<T, U>(a: T, b: i32) -> T.Output\n    U is HasItem\n    U.(HasItem).Item is i32\n    T is Addable<i32>\n    T is Addable<U.(HasItem).Item>\n    return a + b", "")]
    public void IdenticalContractReferencesNameOneCandidate(string source, string codes)
    {
        var c = MinimalEmissionTest.Check(source + "\n()");
        Assert.Equal(codes, string.Join(" ", TestDiagnostics.Of(c).Select(static x => x.Code)));
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
    // premise, or whose receiver's Type has the parameter only inside it, publishes none.
    [Theory]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: T) -> i32\n    T is Pair\n    return (s).a", "(s).a", BoundTypeKind.Nominal)]
    [InlineData("func f<T, U>(s: ref/T) -> i32\n    T is U\n    U is Pair\n    return s.a", "s.a", BoundTypeKind.Nominal)]
    [InlineData("func f<T>(s: ref/T, i: isize) -> i32\n    T is Array<Pair>\n    return s[i].a", "s[i]", BoundTypeKind.Array)]
    [InlineData("func f<T>(s: ref/T) -> i32\n    T is (Pair, i32)\n    return s.0.a", "s.0", BoundTypeKind.Tuple)]
    [InlineData("func f(s: ref/Pair) -> i32\n    return s.a", "s.a", null)]
    [InlineData("contract Pick\n    func pick(self: ref/Self) -> bool\nfunc f<T>(s: ref/T) -> bool\n    T is Pick\n    return s.pick()", "s.pick", null)]
    [InlineData("func f<T>(xs: ref/Array<T>, i: isize) -> i32\n    T is Pair\n    return xs[i].a", "xs[i]", null)]
    [InlineData("func f<T>(xs: ref/Array<T>, i: isize) -> i32\n    T is Pair\n    return xs[i].a", "xs[i].a", null)]
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

        var receiver = HirTables.TypeOf(KotoHelper.UnwrapParentheses(node.Left));
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

    // DECISIONS BU-72: a warm rebind publishes an access's identity premise again without allocating, a chain's derived one included.
    [Trait("Purpose", "Allocation")]
    [Fact]
    public void AWarmRebindRepublishesAnAccesssIdentityWithoutAllocating()
    {
        var c = CompilationTestHelper.BindSuccess(
            "struct Pair\n    public var a: i32 = 1\n    public init() => ()\n" +
            "func f<T, U>(s: ref/T, i: isize) -> i32\n    T is U\n    U is Array<Pair>\n    return s[i].a\n()");
        var node = KotoTree.Walk(c.Kotonoha.RootKoto).Single(x => x is IndexKoto && x.ToString() == "s[i]");
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Equal(BoundTypeKind.Array, HirTables.IdentityPremise(node)?.RequiredType?.Kind);
    }

    [Fact]
    public void RefutedIdentityLeavesTheMemberInapplicable()
    {
        var c = MinimalEmissionTest.Analyze(Pair + "let p = Pair<i32, i64>.init(1, 2)\nlet r = p.pick()");
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }
}
