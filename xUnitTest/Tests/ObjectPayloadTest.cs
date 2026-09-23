// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 8.4.7.2: ObjectPayload, the opt-out declaration and Object Target evidence.</summary>
public class ObjectPayloadTest
{
    private const string Parser = "struct Parser\n    Self is not ObjectPayload\n    public var pos: i32 = 0\n";

    [Theory]
    [InlineData("obj/Parser")]
    [InlineData("rc/Parser")]
    [InlineData("arc/Parser")]
    [InlineData("objref/Parser")]
    [InlineData("objuniq/Parser")]
    public void ObjectFormsOverAnOptedOutTypeAreRejected(string type)
    {
        var c = Parse(Parser + $"func f(x: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("func f(x: Parser, y: ref/Parser, z: uniq/Parser, p: unsafe/Parser) => ()")]
    [InlineData("struct Box<T>\n    let item: T\nfunc f(x: obj/Box<Parser>, y: rc/(Parser, i32)) => ()")]
    [InlineData("func f(x: Array<Parser>, y: Option<Parser>) => ()")]
    [InlineData("struct Parser2\n    Self is not ObjectPayload\n    let inner: Parser\nfunc f(x: ref/Parser2) => ()")]
    public void TheOptOutIsShallow(string source)
    {
        var c = Parse(Parser + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void TheOptOutIsInheritedByDerivedStructs()
    {
        var c = Parse("open struct Base\n    Self is not ObjectPayload\nopen struct Middle: Base\nstruct Leaf: Middle\nfunc f(x: obj/Leaf) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("open struct Base\n    Self is not ObjectPayload\nstruct Leaf: Base\n    Self is not ObjectPayload")]
    [InlineData("open struct Base\nstruct Leaf: Base\n    Self is not ObjectPayload\nfunc f(x: obj/Base) => ()")]
    [InlineData("enum Token\n    Self is not ObjectPayload\n    End")]
    public void RestatedAndDerivedOptOutsAreAccepted(string source)
    {
        var c = Parse(source);
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
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidSelfClause_Kd);
    }

    [Fact]
    public void GenericObjectFormationNeedsDeclaredEvidence()
    {
        var c = Parse("func boxed<T>(value: T) -> obj/T\n    return Kimi.Intrinsics.makeObj(value@move)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("struct Cell\nfunc use() -> obj/Cell => boxed(Cell.init())")]
    [InlineData("open struct Cell\nfunc use() -> obj/Cell => boxed(Cell.init())")] // Open Cores are payloads too.
    public void ObjectPayloadProvesGenericObjectFormation(string source)
    {
        var c = Parse("func boxed<T>(value: T) -> obj/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeObj(value@move)\n" + source);
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("func use() => ()\n    let o = boxed(Parser.init())")]
    [InlineData("func use(p: Parser) => ()\n    let o = Kimi.Intrinsics.makeObj(p@move)")]
    public void AnOptedOutTypeIsNeverAPayload(string source)
    {
        var c = Parse(Parser + "func boxed<T>(value: T) -> obj/T\n    T is ObjectPayload\n    return Kimi.Intrinsics.makeObj(value@move)\n" + source);
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
        var c = Parse($"func f<s/T>(handle: s/T, view: objref/T)\n    {constraint}\n    ()");
        Assert.Equal(expected, c.Bind().IsComplete);
    }

    [Fact]
    public void SealedAloneIsNotObjectFormationEvidence()
    {
        var c = Parse("func f<T>(x: objref/T)\n    T is Sealed\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Theory]
    [InlineData("s is ref or objref", true)]
    [InlineData("s is borrow", true)]
    [InlineData("s is not (owning or unsafe)", true)]
    [InlineData("s is ref or obj", false)]
    [InlineData("s is reference", false)]
    public void SemanticsRequirementsAreProvenFromTheAdmittedSet(string constraint, bool expected)
    {
        var c = Parse($"func h<s/T>(x: s/T)\n    s is borrow\n    ()\nfunc g<s/T>(x: s/T)\n    {constraint}\n    h(x@move)");
        Assert.True(expected == c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnEmptyAdmittedSetIsContradictoryEvidence()
    {
        var c = Parse("func g<s/T>(x: s/T)\n    s is ref\n    s is obj\n    ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void RuntimeTestsAgainstAnOptedOutTargetAreRejected()
    {
        var c = Parse("open struct Base\nstruct Leaf: Base\n    Self is not ObjectPayload\nfunc f(x: objref/Base) -> bool => x is Leaf");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Theory]
    [InlineData("obj/Text.FixedBuffer")]
    [InlineData("obj/WriteWindow")]
    [InlineData("rc/Utf8Writer")]
    public void TheLoanBoundFormattingAdaptersOptOut(string type)
    {
        var c = Parse($"func f(x: {type}) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NotObjectPayload_Kd);
    }

    [Fact]
    public void AContractRequirementDerivesObjectPayloadForItsUsers()
    {
        var c = Parse("contract Shape\n    Self is ObjectPayload\n    func area(self: ref/Self) -> f64\nfunc total<T>(item: objref/T)\n    T is Shape\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnOptedOutTypeCannotConformToAContractRequiringObjectPayload()
    {
        const string shape = "contract Shape\n    Self is ObjectPayload\n    func area(self: ref/Self) -> f64\n";
        var ok = Parse(shape + "struct Circle\n    Self is Shape\n    public func area(self: ref/Self) -> f64 => 1.0");
        Assert.True(ok.Bind().IsComplete, Describe(ok));
        var bad = Parse(shape + "struct Nope\n    Self is not ObjectPayload\n    Self is Shape\n    public func area(self: ref/Self) -> f64 => 1.0");
        Assert.False(bad.Bind().IsComplete);
    }

    [Theory]
    [InlineData("    Self is ObjectPayload\n", true)]
    [InlineData("", false)]
    public void ObjectReceiversInAContractNeedTheObjectPayloadClause(string clause, bool expected)
    {
        var c = Parse("contract Shape\n" + clause + "    func area(self: objref/Self) -> f64");
        Assert.True(expected == c.Bind().IsComplete, Describe(c));
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare(WindowsProfile.Target));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        return c;
    }

    private static string Describe(Compilation c) => c.Binding.Result + "\n" + string.Join('\n', c.Binding.Issues);
}
