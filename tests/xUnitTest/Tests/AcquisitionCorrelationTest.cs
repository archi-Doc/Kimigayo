// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Xunit;

namespace XunitTest;

public class AcquisitionCorrelationTest
{
    private const string Tuple = "struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\n";
    private const string Crossed = Tuple + "let x: (i64, i32) = (1, 2)\n";
    private const string Body = "func k() -> ()\n";
    private const string Wrappers = "struct Tag<T>\n    public init() => ()\nstruct Box<T>\n    public init() => ()\n";
    private const string Pairs = "struct P<A, B>\n    public init() => ()\nstruct C<T>\n    public init(value: P<T, i32>, code: i32) => ()\n    public init(value: ref/P<i64, T>, name: string) => ()\n";
    private const string Methods = "struct S\n    public init() => ()\n    public func m<U>(self: ref/Self, value: (U, i32), code: i32) => ()\n    public func m<U>(self: ref/Self, value: ref/(i64, U), name: string) => ()\n";
    private const string Waiting = "struct Item<T>\n    public init(value: T, code: i32, f: (T) -> ()) => ()\n    public init(value: T, name: string, f: () -> ()) => ()\n";
    private const string Signatures = "func h<T, F>(value: T, code: i32, action: F) -> ()\n    F is Callable<(T) -> ()>\n    return\nfunc h<T, F>(value: T, name: string, action: F) -> ()\n";
    private const string Elements = "struct C<T>\n    public init(value: T, code: i32, w: ((T, i32), i32)) => ()\n    public init(value: T, name: string, w: ((i64, T), i64)) => ()\n";
    private const string Results = "func f<T>(value: T, code: i32) -> (T, i32) => (value@move, 0)\nfunc f<T>(value: T, name: string) -> (i64, T) => (0, value@move)\n";
    private const string Joins = "func g<T>(x: Option<T>, y: T, z: Option<i64>) -> i32 => 0\nfunc g<U>(x: Option<i64>, y: U, z: Option<bool>) -> i32 => 1\nfunc k(c: bool, v: i64, b: Option<bool>) -> i32\n";
    private const string Never = "func fail() -> Never => $abort(\"no value\")\n";
    private const string Applied = "    return\nfunc g(v: i32) -> () => ()\n" + Body + "    let n: i32 = 42\n    h(n, 0, g)";

    [Theory]
    [InlineData("let c = C.init(x, 0)")]
    [InlineData("let c = C.init(x, code: 0)")]
    [InlineData("let c = C.init(x)")]
    [InlineData("let c: C<i64> = C.init(x, 0)")]
    public void WholeGroupCorrelationPrecedesApplicability(string call)
    {
        var c = MinimalEmissionTest.Analyze(Crossed + call);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
        Assert.DoesNotContain(c.Binding.Issues, x => x.Failure == BindingFailure.ParameterShapeMismatch);
    }

    [Fact]
    public void ExplicitConstructionFixesTheCorrelation()
    {
        var c = MinimalEmissionTest.Analyze(Crossed + "let c = C<i64>.init(x, 0)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let item = Item.init(42, 0)")]
    [InlineData("let n: i64 = 42\nlet item = Item.init(n, 0)")]
    [InlineData("let item: Item<i64> = Item.init(42, 0)")]
    public void SharedIndependentOrUnresolvedInputsEstablishTheContract(string use)
    {
        var c = MinimalEmissionTest.Analyze("struct Item<T>\n    public init(value: T, code: i32) => ()\n    public init(value: T, name: string) => ()\n" + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAdditionalWitnessCannotMasqueradeAsACommonUnresolvedSymbol(bool constructor)
    {
        var group = constructor
            ? "struct Item<T>\n    public init(value: T, witness: Tag<T>) => ()\n    public init(value: T, witness: Tag<ref/T>) => ()\n"
            : "func pick<T>(value: T, witness: Tag<T>) => ()\nfunc pick<T>(value: T, witness: Tag<ref/T>) => ()\n";
        var source = "struct Tag<T>\n    public init() => ()\n" + group + "let witness = Tag<ref/i32>.init()\n" + (constructor ? "let item = Item.init" : "pick") + "(42, witness)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
    }

    // SPEC 7.4.4: an equal Type that a typed argument gives along another structural path, or through an extra source, is no
    // correlation evidence: a Tuple element, a struct's Type argument, an Option payload, a generic structure, a method's and a
    // function's own slot, an expected result beside the argument, a ModeOnly pair's witness, a function reference, an anonymous
    // header's written Type (SPEC 10.5.4) and a call signature (G512).
    [Theory]
    [InlineData(Tuple + Body + "    let x: (i32, i32) = (1, 2)\n    let c = C.init(x, 0)")]
    [InlineData(Pairs + Body + "    let c = C.init(P<i32, i32>.init(), 0)")]
    [InlineData("struct C<T>\n    public init(value: (T, Option<i32>), code: i32) => ()\n    public init(value: ref/(i64, Option<T>), name: string) => ()\n" + Body + "    let o: Option<i32> = .Some(2)\n    let x: (i32, Option<i32>) = (1, o)\n    let c = C.init(x, 0)")]
    [InlineData(Wrappers + "struct C<T>\n    public init(value: Box<(T, i32)>, code: i32) => ()\n    public init(value: ref/Box<(i64, T)>, name: string) => ()\n" + Body + "    let c = C.init(Box<(i32, i32)>.init(), 0)")]
    [InlineData(Methods + Body + "    let s = S.init()\n    let x: (i32, i32) = (1, 2)\n    s.m(x, 0)")]
    [InlineData("func f<U>(value: (U, i32), code: i32) => ()\nfunc f<U>(value: ref/(i64, U), name: string) => ()\n" + Body + "    let x: (i32, i32) = (1, 2)\n    f(x, 0)")]
    [InlineData(Tuple + Body + "    let x: (i32, i32) = (1, 2)\n    let c: C<i32> = C.init(x, 0)")]
    [InlineData(Wrappers + "struct Item<T>\n    public init(value: T, code: i32, witness: Tag<T>) => ()\n    public init(value: T, name: string, witness: Tag<Box<T>>) => ()\n" + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, Tag<i32>.init())")]
    [InlineData(Waiting + "func g(v: i32) -> () => ()\n" + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, g)")]
    [InlineData("struct Item<T>\n    public init(value: T, code: i32, f: (T) -> ()) => ()\n    public init(value: T, name: string, f: (i64) -> ()) => ()\n" + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, func [] (v: i32) => ())")]
    [InlineData(Signatures + "    F is Callable<((T, i32)) -> ()>\n" + Applied)]
    [InlineData(Elements + Body + "    let n: i32 = 42\n    let p: (i32, i32) = (1, 2)\n    let c = C.init(n, 0, (p, 1))")]
    [InlineData("func identity<T>(value: T) -> T => value@move\n" + Results + Body + "    let r: (i32, i32) = f(identity(1), 0)")]
    [InlineData(Results + Body + "    let r: (i32, i32) = f(42, 0)")]
    public void EvidenceAlongAnotherPathIsNoCorrelation(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
    }

    // SPEC 10.5.1, 10.8: a waiting part, whole, as a Tuple element or as a join's results, and a Never part are never evidence, so a
    // parameter that only they reach does not break the correlation, for a Literal or a typed source alike.
    [Theory]
    [InlineData(Waiting + Body + "    let item = Item.init(42, 0, func [] (v) => ())")]
    [InlineData(Waiting + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, func [] (v) => ())")]
    [InlineData("struct Item<T>\n    public init(value: T, code: i32, w: (i32, (T) -> ())) => ()\n    public init(value: T, name: string, w: (i32, () -> ())) => ()\n" + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, (1, func [] (v) => ()))")]
    [InlineData(Joins + "    return g(if c => .None else => .None, v, b)")]
    [InlineData(Never + "struct C<T>\n    public init(value: (T, i32), code: i32, w: T) => ()\n    public init(value: ref/(i64, T), name: string, w: T) => ()\n" + Body + "    let n: i32 = 42\n    let c = C.init(fail(), 0, n)")]
    [InlineData(Never + Elements + Body + "    let n: i32 = 42\n    let c = C.init(n, 0, (fail(), 1))")]
    public void WaitingAndNeverPartsGiveNoEvidence(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    // SPEC 23.1.2 (DECISIONS BU-78 site 3): a failed part makes its join's position wait, so it is no evidence and adds no record.
    [Fact]
    public void AFailedPartIsNoEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Joins + "    return g(if c => undefinedName else => .None, v, b)");
        c.Binding.ReportDiagnostics();
        Assert.Equal("UnresolvedBinding_Kd", Assert.Single(TestDiagnostics.Of(c)).Code);
    }

    // SPEC 7.4.4, 10.8, 10.5.1: the same witness or call-signature path, a Tuple element at its own position below a `ref/` layer
    // one candidate has, an expected result that fills nothing an argument binds, and a fixed explicit binding establish the contract.
    [Theory]
    [InlineData("struct C<T>\n    public init(value: T, code: i32, w: ref/(T, i32)) => ()\n    public init(value: T, name: string, w: (T, i64)) => ()\n" + Body + "    let n: i32 = 42\n    let c = C.init(n, 0, (n, 1))")]
    [InlineData(Wrappers + "struct Item<T>\n    public init(value: T, code: i32, witness: Tag<T>) => ()\n    public init(value: T, name: string, witness: Tag<T>) => ()\n" + Body + "    let n: i32 = 42\n    let item = Item.init(n, 0, Tag<i32>.init())")]
    [InlineData(Signatures + "    F is Callable<(T) -> ()>\n" + Applied)]
    [InlineData(Results + Body + "    let n: i32 = 42\n    let r: (i32, i32) = f(n, 0)")]
    [InlineData("func f<T>(value: T, code: i32) -> (T, i32) => (value@move, 0)\nfunc f<T>(value: T, name: string) -> (T, i32) => (value@move, 0)\n" + Body + "    let n: i32 = 42\n    let r: (i32, i32) = f(n, 0)")]
    [InlineData(Methods + Body + "    let s = S.init()\n    let x: (i32, i32) = (1, 2)\n    s.m<i32>(x, 0)")]
    [InlineData(Pairs + Body + "    let c = C<i32>.init(P<i32, i32>.init(), 0)")]
    public void TheSameEvidenceOrAFixedBindingEstablishesTheContract(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void RejectedCorrelationReusesContractsAndFacts()
    {
        var c = MinimalEmissionTest.Analyze(Crossed + "let c = C.init(x, 0)");
        var rejected = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => rejected &= !c.Bind().IsComplete));
        Assert.True(rejected);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnprovenAcquisitionCorrelation);
    }
}
