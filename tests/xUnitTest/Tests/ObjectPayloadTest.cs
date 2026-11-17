// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.7.3: ObjectPayload, the opt-out declaration and Object Target evidence.</summary>
public class ObjectPayloadTest
{
    // SPEC 8.7.3 (R7-N2d): the opt-out passes through a projected base, a chain and a fragment; each was accepted before. PLAN G602: and
    // through a conformance inherited across a projected base (two InvalidTypeFormation_Kd at the clause and struct before).
    [Theory]
    [InlineData("contract HasBase\n    associate Parent\nopen struct Root\n    Self is not ObjectPayload\n    public init() => ()\nopen struct Middle\n    Self is HasBase\n    associate HasBase.Parent is Root\n    public init() => ()\nstruct S0\n    Self is HasBase\n    associate HasBase.Parent is Middle\nopen struct Source: S0.(HasBase).Parent\n    public init(): base() => ()\nstruct D: Source.(HasBase).Parent\n    public init(): base() => ()\nfunc f(a: obj/D) => ()")]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is P\nopen struct P\n    Self is not ObjectPayload\n    public init() => ()\nstruct D: Source.(HasBase).Parent\n    public init(): base() => ()\nfunc f(a: obj/D) => ()")]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is P\nopen struct P\n    Self is not ObjectPayload\n    public init() => ()\nopen struct D: Source.(HasBase).Parent\n    public init(): base() => ()\nstruct L: D\n    public init(): base() => ()\nfunc f(a: obj/L) => ()")]
    [InlineData("contract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is Q\nopen struct Q\n    Self is not ObjectPayload\n    public init() => ()\npublic struct D\n    public init(): base() => ()\npublic struct D: Source.(HasBase).Parent\n    func g(self) -> i32 => 1\nfunc f(a: obj/D) => ()")]
    [InlineData("open struct E\n    protected internal open struct M\n        Self is not ObjectPayload\n        public init() => ()\n    public init() => ()\nopen struct D: E\n    public init() => ()\nstruct U: D.M\n    public init() => ()\nfunc f(a: obj/U) => ()")]
    public void AnOptOutPassesThroughAProjectedBase(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    // SPEC 8.7.3 (G600): an object form written in a Constraint is formed before the opt-outs are published and is judged again once
    // they are: a conformance's Contract argument, a function's and a struct's Constraint, through an own, a nominal and a projected
    // opt-out, `rc/` and a nested form. Each is one NotObjectPayload_Kd at its form and no other issue (fail-before: each was accepted).
    [Theory]
    [InlineData("contract K<T>\nstruct D\n    Self is not ObjectPayload\n    public init() => ()\nstruct W\n    Self is K<obj/D>\n", "obj/D")]
    [InlineData("contract K<T>\nopen struct P\n    Self is not ObjectPayload\n    public init() => ()\nstruct D2: P\n    public init(): base() => ()\nstruct W\n    Self is K<obj/D2>\n", "obj/D2")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is P3\nopen struct P3\n    Self is not ObjectPayload\n    public init() => ()\nstruct D3: Source.(HasBase).Parent\n    public init(): base() => ()\nstruct W\n    Self is K<obj/D3>\n", "obj/D3")]
    [InlineData("contract K<T>\nstruct D\n    Self is not ObjectPayload\n    public init() => ()\nfunc f<T>(a: T)\n    T is K<obj/D>\n    ()\n", "obj/D")]
    [InlineData("contract K<T>\nstruct D\n    Self is not ObjectPayload\n    public init() => ()\nstruct S<T>\n    T is K<obj/D>\n    var x: T\n", "obj/D")]
    [InlineData("contract K<T>\nopen struct P\n    Self is not ObjectPayload\n    public init() => ()\nstruct D2: P\n    public init(): base() => ()\nfunc f<T>(a: T)\n    T is K<obj/D2>\n    ()\n", "obj/D2")]
    [InlineData("contract K<T>\nstruct D\n    Self is not ObjectPayload\n    public init() => ()\nstruct W\n    Self is K<rc/D>\n", "rc/D")]
    [InlineData("contract K<T>\nstruct D\n    Self is not ObjectPayload\n    public init() => ()\nstruct W\n    Self is K<Option<obj/D>>\n", "obj/D")]
    [InlineData("contract K<T>\ncontract HasBase\n    associate Parent\nstruct Source\n    Self is HasBase\n    associate HasBase.Parent is P3\nopen struct P3\n    Self is not ObjectPayload\n    public init() => ()\nstruct D3: Source.(HasBase).Parent\n    public init(): base() => ()\nstruct S<T>\n    T is K<obj/D3>\n    var x: T\n", "obj/D3")]
    public void AnObjectFormInAConstraintIsJudgedOnceTheOptOutIsPublished(string source, string form)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        var issue = Assert.Single(c.Binding.Issues);
        Assert.Equal((DiagnosticCode.NotObjectPayload_Kd, form), (issue.Code, issue.Node.ToString()));
    }

    private const string Parser = "struct Parser\n    Self is not ObjectPayload\n    public var pos: i32 = 0\n";

    [Theory]
    [InlineData("obj/Parser")]
    [InlineData("rc/Parser")]
    [InlineData("arc/Parser")]
    [InlineData("objref/Parser")]
    [InlineData("objuniq/Parser")]
    public void ObjectFormsOverAnOptedOutTypeAreRejected(string type)
    {
        var c = CompilationTestHelper.Parse(Parser + $"func f(x: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    // SPEC 9.2, 8.7.3 (R7-N2b): a base clause reads the role of its binder before it is bound, so a nearer group or a Kotonoha reference name
    // leaves the struct it names, and the derived struct inherits that struct's opt-out.
    [Theory]
    [InlineData("open struct G\n    Self is not ObjectPayload\n    public init() => ()\ngroup Box\n    group G\n    public struct D: G\n        public init() => ()\nfunc f(a: obj/Box.D) => ()\n")]
    [InlineData("alias G1\nalias G2\ngroup G1\n    public open struct X\n        Self is not ObjectPayload\n        public init() => ()\ngroup G2\n    public group X\nstruct D: X\n    public init() => ()\nfunc f(a: obj/D) => ()\n")]
    [InlineData("open struct G\n    Self is not ObjectPayload\n    public init() => ()\ngroup Box\n    group G\n    public struct D: G\n        public init() => ()\ngroup Box\n    public struct D\n        func g(self) -> i32 => 1\nfunc f(a: obj/Box.D) => ()\n")]
    [InlineData("open struct G\n    Self is not ObjectPayload\n    public init() => ()\ngroup Box\n    group G\n    public struct D\n        public init() => ()\ngroup Box\n    public struct D: G\n        func g(self) -> i32 => 1\nfunc f(a: obj/Box.D) => ()\n")]
    [InlineData("alias G1\ngroup G1\n    public open struct Kimi\n        Self is not ObjectPayload\n        public init() => ()\nstruct D: Kimi\n    public init() => ()\nfunc f(a: obj/D) => ()\n")]
    public void AnOptOutPassesThroughABaseClauseUnderItsBindersRole(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("func f(x: Parser, y: ref/Parser, z: uniq/Parser, p: raw/Parser) => ()")]
    [InlineData("struct Box<T>\n    let item: T\nfunc f(x: obj/Box<Parser>, y: rc/(Parser, i32)) => ()")]
    [InlineData("func f(x: Array<Parser>, y: Option<Parser>) => ()")]
    [InlineData("struct Parser2\n    Self is not ObjectPayload\n    let inner: Parser\nfunc f(x: ref/Parser2) => ()")]
    public void TheOptOutIsShallow(string source)
    {
        var c = CompilationTestHelper.Parse(Parser + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void TheOptOutIsInheritedByDerivedStructs()
    {
        var c = CompilationTestHelper.Parse("open struct Base\n    Self is not ObjectPayload\nopen struct Middle: Base\nstruct Leaf: Middle\nfunc f(x: obj/Leaf) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("open struct Base\n    Self is not ObjectPayload\nstruct Leaf: Base\n    Self is not ObjectPayload")]
    [InlineData("open struct Base\nstruct Leaf: Base\n    Self is not ObjectPayload\nfunc f(x: obj/Base) => ()")]
    [InlineData("enum Token\n    Self is not ObjectPayload\n    End")]
    public void RestatedAndDerivedOptOutsAreAccepted(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("struct A\n    Self is ObjectPayload")]
    [InlineData("struct A\n    Self is not Sealed")]
    [InlineData("struct A\n    Self is not Copy")]
    [InlineData("struct A\n    Self is not ObjectPayload and Copy")]
    [InlineData("struct A\n    Self is Copy and not ObjectPayload")]
    [InlineData("struct A\n    Self is not ObjectPayload\n    Self is not ObjectPayload")]
    [InlineData("struct A<T>\n    Self is ObjectPayload when T is Copy")]
    public void OtherSelfClausesAboutObjectPayloadAreRejected(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidSelfClause_Kd);
    }

    [Fact]
    public void GenericObjectFormationNeedsDeclaredEvidence()
    {
        var c = CompilationTestHelper.Parse("func boxed<T>(value: T) -> obj/T\n    return Kimi.Intrinsics.makeObj(value@move)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("struct Cell\nfunc use() -> obj/Cell => boxed(Cell.init())")]
    [InlineData("open struct Cell\nfunc use() -> obj/Cell => boxed(Cell.init())")] // Open Cores are payloads too.
    public void ObjectPayloadProvesGenericObjectFormation(string source)
    {
        var c = CompilationTestHelper.Parse("func boxed<T>(value: T) -> obj/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeObj(value@move)\n" + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("func use()\n    let o = boxed(Parser.init())")]
    [InlineData("func use(p: Parser)\n    let o = Kimi.Intrinsics.makeObj(p@move)")]
    public void AnOptedOutTypeIsNeverAPayload(string source)
    {
        var c = CompilationTestHelper.Parse(Parser + "func boxed<T>(value: T) -> obj/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeObj(value@move)\n" + source);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("s is object", true)]
    [InlineData("s is objectborrow", true)]
    [InlineData("s is obj or objref", true)]
    [InlineData("s is ref", false)]
    [InlineData("s is object or ref", false)]
    public void PairEvidenceFormsObjectTypesOverTheTarget(string constraint, bool expected)
    {
        var c = CompilationTestHelper.Parse($"func f<s/T>(handle: s/T, view: objref/T)\n    {constraint}\n    ()");
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Fact]
    public void SealedAloneIsNotObjectFormationEvidence()
    {
        var c = CompilationTestHelper.Parse("func f<T>(x: objref/T)\n    T is Sealed\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("s is ref or objref", true)]
    [InlineData("s is borrow", true)]
    [InlineData("s is not (owning or raw)", true)]
    [InlineData("s is ref or obj", false)]
    [InlineData("s is reference", false)]
    public void SemanticsRequirementsAreProvenFromTheAdmittedSet(string constraint, bool expected)
    {
        var c = CompilationTestHelper.Parse($"func h<s/T>(x: s/T)\n    s is borrow\n    ()\nfunc g<s/T>(x: s/T)\n    {constraint}\n    h(x@move)");
        Assert.True(expected == c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnEmptyAdmittedSetIsContradictoryEvidence()
    {
        var c = CompilationTestHelper.Parse("func g<s/T>(x: s/T)\n    s is ref\n    s is obj\n    ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void RuntimeTestsAgainstAnOptedOutTargetAreRejected()
    {
        var c = CompilationTestHelper.Parse("open struct Base\nstruct Leaf: Base\n    Self is not ObjectPayload\nfunc f(x: objref/Base) -> bool => x is Leaf");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("obj/Text.FixedBuffer")]
    [InlineData("obj/WriteWindow")]
    [InlineData("rc/Utf8Writer")]
    public void TheLoanBoundFormattingAdaptersOptOut(string type)
    {
        var c = CompilationTestHelper.Parse($"func f(x: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Fact]
    public void AContractRequirementDerivesObjectPayloadForItsUsers()
    {
        var c = CompilationTestHelper.Parse("contract Shape\n    Self is ObjectPayload\n    func area(self: ref/Self) -> f64\nfunc total<T>(item: objref/T)\n    T is Shape\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnOptedOutTypeCannotConformToAContractRequiringObjectPayload()
    {
        const string shape = "contract Shape\n    Self is ObjectPayload\n    func area(self: ref/Self) -> f64\n";
        var ok = CompilationTestHelper.Parse(shape + "struct Circle\n    Self is Shape\n    public func area(self: ref/Self) -> f64 => 1.0");
        Assert.True(ok.Bind().IsComplete, Describe(ok));
        var bad = CompilationTestHelper.Parse(shape + "struct Nope\n    Self is not ObjectPayload\n    Self is Shape\n    public func area(self: ref/Self) -> f64 => 1.0");
        Assert.False(bad.Bind().IsComplete);
    }

    [Theory]
    [InlineData("    Self is ObjectPayload\n", true)]
    [InlineData("", false)]
    public void ObjectReceiversInAContractNeedTheObjectPayloadClause(string clause, bool expected)
    {
        var c = CompilationTestHelper.Parse("contract Shape\n" + clause + "    func area(self: objref/Self) -> f64");
        Assert.True(expected == c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("contract Payload\n    Self is ObjectPayload\ncontract Shape: Payload\n", true)]
    [InlineData("contract Payload\n    Self is ObjectPayload\ncontract Middle: Payload\ncontract Shape: Middle\n", true)]
    [InlineData("contract Payload\ncontract Shape: Payload\n", false)]
    public void AnInheritedObjectPayloadClauseHoldsForTheRefiningSelf(string contracts, bool expected)
    {
        // SPEC 8.7.3: the clause may be declared directly or inherited by refinement.
        var c = CompilationTestHelper.Parse(contracts + "    func area(self: ref/Self, other: objref/Self) -> f64");
        Assert.True(expected == c.Bind().IsComplete, Describe(c));
    }

    private static string Describe(Compilation c) => c.Binding.Result + "\n" + string.Join('\n', c.Binding.Issues);
}
