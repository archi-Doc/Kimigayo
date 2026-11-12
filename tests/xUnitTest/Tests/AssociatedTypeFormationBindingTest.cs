// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Build;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;
using Xunit;

namespace XunitTest;

public class AssociatedTypeFormationBindingTest
{
    // The programs of OnlyTheParenthesizedSuffixNamesAProjectionsContract (PLAN G727).
    private const string ItemC = "contract C\n    associate Item\n";

    private const string GroupL = "group L\n    public contract C\n        associate Item\n";

    private const string NestedK = "contract K\n    associate Item\nstruct P\n    public init() => ()\n    public func make() -> i32 => 1\n" +
        "struct S\n    Self is K\n    associate K.Item is P\nfunc g() -> i32\n";

    private const string GroupP = "group P\n    public contract C\n        associate E\n        func f() -> E\nstruct S\n    Self is P.C\n" +
        "    associate P.C.E is i32\n    public func f() -> i32 => 1\n";

    private const string NestedD = "contract D\n    associate Y\ncontract C\n    associate X is D\ncontract X\n    func m()\n";

    private const string OuterK = "contract HasItem\n    associate Item\ncontract Outer\n    associate K is HasItem\ncontract K\n    associate Item\n";

    private const string BareK = "contract HasItem\n    associate Item\ncontract Outer\n    associate K\ncontract K\n    associate Item\n";

    private const string OriginC = "contract C\n    associate Item(a) is i32\nstruct S\n    Self is C\nfunc f(x: ref/i32 during source) -> ";

    // SPEC 8.5.2 (PLAN G727): only the suffix `.(C).` names a projection's Contract; `T.C.X` and `T.P.C.X` are ordinary paths (9.6).
    // Each invalid row has its valid twin and was accepted by the old unparenthesized reading, except S.C.Item(source): its Origins
    // application was rejected by a rule of that reading, and is now rejected as an ordinary path. q01 and q02 were rejected by the
    // reading, q01p faulted.
    [Theory]
    [InlineData(ItemC + "func f<T>(x: T.C.Item) -> i32\n    T is C\n    return 0", "UnresolvedBinding_Kd")]
    [InlineData(ItemC + "func f<T>(x: T.(C).Item) -> i32\n    T is C\n    return 0", null)]
    [InlineData(GroupL + "func f<T>(x: T.L.C.Item) -> i32\n    T is L.C\n    return 0", "UnresolvedBinding_Kd")]
    [InlineData(GroupL + "func f<T>(x: T.(L.C).Item) -> i32\n    T is L.C\n    return 0", null)]
    [InlineData(NestedK + "    let p: S.K.Item = P.init()\n    return 0", "UnresolvedBinding_Kd")]
    [InlineData(NestedK + "    let p: S.(K).Item = P.init()\n    return 0", null)]
    [InlineData(GroupP + "func call<T>() -> T.P.C.E\n    T is P.C\n    return T.f()", "UnresolvedBinding_Kd")]
    [InlineData(GroupP + "func call<T>() -> T.(P.C).E\n    T is P.C\n    return T.f()", null)]
    [InlineData(NestedD + "func f<T>(v: T.(C).X.Y) -> i32\n    T is C\n    return 0", null)]
    [InlineData(NestedD + "func f<T>(v: T.(C).X.(D).Y) -> i32\n    T is C\n    return 0", null)]
    [InlineData(OuterK + "func f<T>(v: T.K.Item) -> i32\n    T is Outer\n    return 0", null)]
    [InlineData(BareK + "func f<T>(v: T.K.Item) -> i32\n    T is Outer\n    T is K\n    return 0", "UnresolvedBinding_Kd")]
    [InlineData(OuterK + "func f<T>(v: T.K.Item) -> i32\n    T is Outer\n    T is K\n    T.(K).Item is i32\n    return v", "TypeMismatch_Kd")]
    [InlineData(OuterK + "func f<T>(v: T.(K).Item) -> i32\n    T is Outer\n    T is K\n    T.(K).Item is i32\n    return v", null)]
    [InlineData(OriginC + "S.C.Item(source) => 42", "UnresolvedBinding_Kd")]
    [InlineData(OriginC + "S.(C).Item(source) => 42", null)]
    public void OnlyTheParenthesizedSuffixNamesAProjectionsContract(string source, string? code)
    {
        var c = MinimalEmissionTest.Check(source, output: OutputKind.Library);
        Assert.True(c.Accepts == (code is null), MinimalEmissionTest.Describe(c));
        if (code is not null)
        {
            Assert.Contains(TestDiagnostics.Of(c), x => x.Code == code && x.Severity == DiagnosticSeverity.Error);
        }
    }

    [Theory]
    [InlineData("Box<string>")]
    [InlineData("(Box<string>, i32)")]
    [InlineData("[2 of Box<string>]")]
    [InlineData("(raw/Box<string>, i32)")]
    public void InvalidAssociatedDefinitionCannotCertifyConformance(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + type);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.False(Definition(c).IsVerified);
        Assert.False(c.Bind().IsComplete);
        Assert.False(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.False(restored.Bind().IsComplete);
        Assert.False(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("Box<i32>")]
    [InlineData("(Box<i32>, i32)")]
    [InlineData("[2 of Box<i32>]")]
    public void ValidAssociatedDefinitionsStillCertify(string type)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is " + type);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(Definition(c).IsVerified);
        var restored = CompilationTestHelper.Reload(c);
        Assert.True(restored.Bind().IsComplete);
        Assert.True(Definition(restored).IsVerified);
    }

    [Theory]
    [InlineData("contract C\n    associate Item is Box<string>\nstruct S\n    Self is C")]
    [InlineData("contract Parent\n    associate Item is Box<string>\ncontract C: Parent\nstruct S\n    Self is C")]
    [InlineData("contract C\n    associate Item\nstruct S<U>\n    Self is C when U is Copy\n        associate C.Item is Box<string>")]
    public void FixedInheritedAndConditionalDefinitionsValidateFormation(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\n" + declaration);
        Assert.Empty(TestDiagnostics.Of(c, "Hello.kimi"));
        Assert.False(c.Binding.Result.IsComplete);
        Assert.All(Definition(c).Paths, path => Assert.False(path.IsVerified));
    }

    [Theory]
    [InlineData("struct S<U>\n    U is i32\n    Self is C\n    associate C.Item is Box<U>")]
    [InlineData("struct S<U>\n    Self is C when U is i32\n        associate C.Item is Box<U>")]
    public void DependentDefinitionsUseTheirOwnConformanceEvidence(string declaration)
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\n" + declaration);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.All(Definition(c).Paths, path => Assert.True(path.IsVerified));
    }

    [Fact]
    public void InvalidUnrelatedDefinitionDoesNotPoisonValidConformance()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct Invalid\n    Self is C\n    associate C.Item is Box<string>\nstruct S\n    Self is C\n    associate C.Item is Box<i32>");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.True(Definition(c).IsVerified);
    }

    [Fact]
    public void ReplacingDefinitionRestoresSameCertificate()
    {
        const string source = "struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Box<i32>";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete);
        var definition = Definition(c);
        var clause = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint);
        var original = clause.Right;
        var donor = MinimalEmissionTest.Analyze(source.Replace("Box<i32>", "Box<string>", StringComparison.Ordinal));
        var replacement = donor.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S").Members.OfType<IsKoto>().Single(x => x.IsAssociatedConstraint).Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        Assert.True(KotoHelper.Replace(clause, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.False(definition.IsVerified);
        Assert.True(KotoHelper.Replace(clause, replacement, original));
        Assert.True(c.Bind().IsComplete);
        Assert.Same(definition, Definition(c));
        Assert.True(definition.IsVerified);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmAssociatedFormationChecksAllocateNothing()
    {
        var c = MinimalEmissionTest.Analyze("struct Box<T>\n    T is i32\ncontract C\n    associate Item\nstruct S\n    Self is C\n    associate C.Item is Box<i32>");
        Assert.True(c.Bind().IsComplete);
        Assert.Equal(0, AllocationMeasurement.Measure(() =>
        {
            if (!c.Bind().IsComplete)
            {
                throw new InvalidOperationException("Associated Type formation failed.");
            }
        }));
    }

    private static BoundConformance Definition(Compilation c)
    {
        var type = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        var contract = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "C");
        return Assert.IsType<BoundConformance>(c.Binding.GetConformanceDefinition(type.TypeOf()!, contract.SymbolOf()!));
    }
}
