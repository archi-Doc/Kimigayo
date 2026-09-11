// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalConformanceBindingTest
{
    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConditionsControlEvidenceWithoutRestrictingFormation(string argument, ConstraintProof expected)
    {
        var c = Parse($"contract C\n    func f()\nstruct S<T>\n    Self is C when T is Copy\n    public func f() => ()\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var type = use.Parameters[0].Type.BoundType!;
        var contract = Container(c, "C").BoundSymbol!;
        var definition = c.Binding.GetConformanceDefinition(type, contract)!;
        Assert.True(definition.IsVerified);
        Assert.Single(definition.Paths);
        Assert.Null(c.Binding.GetConformance(type, contract));
        Assert.Equal(expected, c.Binding.ResolveConformance(type, contract, use, out var path));
        Assert.Equal(expected == ConstraintProof.Proven, path is not null);
        if (path is not null)
        {
            Assert.Equal("f", Assert.Single(path.Witnesses).Implementation.Name);
        }
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void GenericCallDischargesSubstitutedConditions(string argument, bool valid)
    {
        var c = Parse($"contract C\nstruct S<T>\n    Self is C when T is Copy\nfunc take<T>(x: T)\n    T is C\n    ()\nfunc use(x: S<{argument}>) => take(x)");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void TheSameGenericTypeUsesTheCallersProofEnvironment()
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C when T is Copy\nfunc yes<T>(x: S<T>)\n    T is Copy\n    ()\nfunc unknown<T>(x: S<T>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var contract = Container(c, "C").BoundSymbol!;
        foreach (var name in new[] { "yes", "unknown", "yes", "unknown" })
        {
            var use = Function(c, name);
            Assert.Equal(name == "yes" ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, contract, use, out _));
        }
    }

    [Fact]
    public void StandardGetterCanUseTheConformanceCondition()
    {
        var c = Parse("struct Box<T>\n    Self is Copy when T is Copy\n    var value: T\ncontract C\n    associate E\n    property item: E has get\nstruct S<T>\n    Self is C when T is Copy\n    associate C.E is Box<T>\n    public var item: Box<T>\nfunc use(x: S<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "C").BoundSymbol!, use, out var path));
        Assert.Equal(PropertyWitnessKind.StorageCopy, Assert.Single(path!.PropertyWitnesses).Kind);
    }

    [Theory]
    [InlineData("public func f(x: T) -> T => copy(x)")]
    [InlineData("public var item: T\n        get(self: ref/Self) -> T => storage")]
    public void ConditionsDoNotLeakIntoOutsideMemberDeclarationsOrBodies(string member)
    {
        var c = Parse($"contract C\nfunc copy<U>(x: U) -> U\n    U is Copy\n    return x\nstruct S<T>\n    Self is C when T is Copy\n    {member}");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildPathDoesNotRequireTheDirectParentsCondition(bool reversed)
    {
        var parent = "    Self is Parent when T is Copy\n";
        var child = "    Self is Child when T is Owned\n";
        var c = Parse("contract Parent\n    func f()\ncontract Child: Parent\nstruct S<T>\n" + (reversed ? child + parent : parent + child) + "    public func f() => ()\nfunc use(x: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "Parent").BoundSymbol!, use, out var path));
        Assert.Equal("Child", path!.RootContract.Name);
        Assert.Equal(2, c.Binding.GetConformanceDefinition(use.Parameters[0].Type.BoundType!, Container(c, "Parent").BoundSymbol!)!.Paths.Count);
    }

    [Theory]
    [InlineData("T is Copy", "T is Owned")]
    [InlineData("T is i32", "T is string")]
    public void DuplicateDirectDeclarationsAreRejectedRegardlessOfConditions(string a, string b)
    {
        var c = Parse($"contract C\nstruct S<T>\n    Self is C when {a}\n    Self is C when {b}");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void DifferentInheritedAssociatedBindingsAreRejectedAtDefinition()
    {
        var c = Parse("contract A\n    associate E\ncontract B: A\n    Self.A.E is i32\ncontract C: A\n    Self.A.E is string\nstruct S<T>\n    Self is B when T is Copy\n    Self is C when T is Owned");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void ConjoinedDirectContractsRetainDistinctRootEvidence(string second, bool valid)
    {
        var c = Parse($"contract A\n    associate E\ncontract B: A\n    Self.A.E is i32\ncontract C: A\n    Self.A.E is {second}\nstruct S\n    Self is B and C");
        Assert.Equal(valid, c.Bind().IsComplete);
        var identity = c.Binding.GetConformanceDefinition(Container(c, "S").BoundType!, Container(c, "A").BoundSymbol!)!;
        Assert.Equal(2, identity.Paths.Count);
        Assert.NotSame(identity.Paths[0].RootContract, identity.Paths[1].RootContract);
    }

    [Fact]
    public void InvalidConditionsAreCheckedEvenWithoutUses()
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C when T is Missing");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void AssociatedProjectionRequiresAProvenConditionalPath(string argument, bool valid)
    {
        var c = Parse($"contract C\n    associate E is i32\nstruct S<T>\n    Self is C when T is Copy\nfunc use(x: S<{argument}>.C.E) -> i32 => x");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("T is C, T.C.E is Copy")]
    [InlineData("T.C.E is Copy, T is C")]
    public void ConditionsCanDependOnAssociatedProjections(string conditions)
    {
        var c = Parse($"contract C\n    associate E\ncontract D\nstruct V\n    Self is C\n    associate C.E is i32\nstruct S<T>\n    Self is D when {conditions}\nfunc use(x: S<V>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "D").BoundSymbol!, use, out _));
    }

    [Fact]
    public void DirectAndUnconditionalDeclarationsAreDuplicatesAfterFragmentMerging()
    {
        var c = Parse("contract C\nstruct S<T>\n    Self is C\nstruct S\n    Self is C when T is Copy");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void APrivateChildDoesNotNarrowAConditionalParentsAccess()
    {
        var c = Parse("public contract A\n    func f()\ncontract B: A\npublic struct S<T>\n    Self is B when T is Copy\n    internal func f() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CircularDeclarationsCannotVerifyOneAnother(bool reverse)
    {
        var a = "struct A<T>\n    Self is C when T is Copy\n    associate C.E is B<T>\n";
        var b = "struct B<T>\n    Self is C when T is Copy\n    associate C.E is A<T>\n";
        var c = Parse("contract C\n    associate E is C\n" + (reverse ? b + a : a + b));
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteEvidenceIsIndependentOfDeclarationOrder(bool reverse)
    {
        var a = "struct A<T>\n    Self is C when T is Copy\n    associate C.E is B<T>\n";
        var b = "struct B<T>\n    Self is C when T is Copy\n    associate C.E is Leaf\n";
        var c = Parse("contract C\n    associate E\nstruct Leaf\n" + (reverse ? b + a : a + b) + "func use(x: A<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnInvalidAlternativeIsNotHiddenByASuccessfulPath()
    {
        var c = Parse("contract A\ncontract B: A\nstruct S<T>\n    Self is A when T is Copy\n    Self is B when T is Missing\nfunc use(x: S<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Error, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "A").BoundSymbol!, use, out var path));
        Assert.Null(path);
    }

    [Fact]
    public void OrdinaryConstraintsStillRestrictFormation()
    {
        var c = Parse("contract C\nstruct S<T>\n    T is Copy\n    Self is C when T is Owned\nfunc use(x: S<string>) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void RequirementPremisesAndConformanceConditionsAreCombined()
    {
        var c = Parse("contract C\n    func f<U>(x: U)\n        U is Copy\nstruct S<T>\n    Self is C when T is Owned\n    public func f<V>(x: V)\n        V is Copy\n        ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void ConditionalRefinementRetainsIntrinsicCopyDerivation()
    {
        var c = Parse("contract C: Copy\nstruct S<T>\n    Self is C when T is Copy\n    let value: T\nfunc use(x: S<i32>, y: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(use.Parameters[0].Type.BoundType!, use));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(use.Parameters[1].Type.BoundType!, use));
    }

    [Fact]
    public void DuplicateConditionalCopyUsesTheSameIdentityRule()
    {
        var c = Parse("struct S<T>\n    Self is Copy when T is Copy\n    Self is Copy when T is Owned");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData("s is owner", "i32", ConstraintProof.Proven)]
    [InlineData("s is ref", "i32", ConstraintProof.Refuted)]
    [InlineData("T is i32", "i32", ConstraintProof.Proven)]
    public void ConditionsAcceptEnclosingSemanticsAndTargetProjections(string condition, string argument, ConstraintProof expected)
    {
        var c = Parse($"contract C\nstruct S<s/T>\n    Self is C when {condition}\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(expected, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "C").BoundSymbol!, use, out _));
    }

    [Fact]
    public void TypeSubstitutionsAreComparedUnderTheOverlapConditions()
    {
        var c = Parse("contract A\n    associate E\ncontract B: A\n    Self.A.E is i32\nstruct S<T>\n    Self is A when T is i32\n    Self is B when T is i32\n    associate A.E is T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AssociatedRequirementsCannotBeInferredFromMembers()
    {
        var c = Parse("contract C\n    associate E\n    func read() -> E\nstruct S<T>\n    Self is C when T is Copy\n    public func read() -> i32 => 1");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void IdenticalTypeArgumentsDoNotShareProofBetweenConditionalAndOuterScopes()
    {
        var c = Parse("contract C\ncontract D\nstruct S<T>\n    Self is C when T is Copy\nstruct Outer<T>\n    Self is D when T is Copy\n    public func inspect(x: S<T>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var inspect = Function(c, "inspect");
        var type = inspect.Parameters[0].Type.BoundType!;
        var conditional = Container(c, "Outer").Members.OfType<SyntaxFormKoto>().Single();
        var contract = Container(c, "C").BoundSymbol!;
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(type, contract, conditional, out _));
            Assert.Equal(ConstraintProof.Unknown, c.Binding.ResolveConformance(type, contract, inspect, out _));
        }
    }

    [Fact]
    public void AConditionalTypeIdentityCanJustifyTheStorageCopyBridge()
    {
        var c = Parse("contract C\n    associate E\n    property item: E has get\nstruct S<T>\n    Self is C when T is i32\n    associate C.E is T\n    public var item: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var path = Assert.Single(c.Binding.GetConformanceDefinition(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!)!.Paths);
        Assert.Equal(PropertyWitnessKind.StorageCopy, Assert.Single(path.PropertyWitnesses).Kind);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("Copy")]
    public void ChangedConditionsInvalidateEarlierSuccessfulAndFailedQueries(string contractName)
    {
        var c = Parse($"contract C\nstruct S<T>\n    Self is {contractName} when T is Copy\nfunc use(x: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var syntax = Container(c, "S").Members.OfType<SyntaxFormKoto>().Single();
        var original = ((IsKoto)((SyntaxFormKoto)syntax.Operands[1]).Operands[0]).Right;
        var fragment = Parse("struct R<T>\n    Self is Copy when T is Owned");
        var replacement = ((IsKoto)((SyntaxFormKoto)Container(fragment, "R").Members.OfType<SyntaxFormKoto>().Single().Operands[1]).Operands[0]).Right;
        var parent = original.Parent!;
        var use = Function(c, "use");
        var type = use.Parameters[0].Type.BoundType!;
        var contract = contractName == "Copy" ? c.Binding.Core.Copy : Container(c, "C").BoundSymbol!;
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ResolveConformance(type, contract, use, out _));
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(type, contract, use, out _));
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ResolveConformance(type, contract, use, out _));
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void GenericEnumsUseTheSameConditionalConformanceValidation(string argument, ConstraintProof expected)
    {
        var c = Parse($"contract C\n    func f()\nenum S<T>\n    Self is C when T is Copy\n    None\n    Some(T)\n    public func f() => ()\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(expected, c.Binding.ResolveConformance(use.Parameters[0].Type.BoundType!, Container(c, "C").BoundSymbol!, use, out _));
    }

    [Fact]
    public void DisjointEvidenceMayRetainDifferentAssociatedBindings()
    {
        var c = Parse("contract A\n    associate E\ncontract B: A\n    Self.A.E is i32\ncontract C: A\n    Self.A.E is string\nstruct S<T>\n    Self is B when T is i32\n    Self is C when T is string\nfunc use(x: S<i32>, y: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var a = Container(c, "A").BoundSymbol!;
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[i].Type.BoundType!, a, use, out var path));
            Assert.Equal(i == 0 ? "i32" : "string", Assert.Single(path!.AssociatedTypes).Value.Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebindingInvalidatesAndRestoresEvidence(bool condition)
    {
        var c = Parse("contract C\n    func f()\nstruct S<T>\n    Self is C when T is Copy\n    public func f() => ()\nfunc use(x: S<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var contract = Container(c, "C").BoundSymbol!;
        var type = use.Parameters[0].Type.BoundType!;
        var identity = c.Binding.GetConformanceDefinition(type, contract)!;
        var path = Assert.Single(identity.Paths);
        var fragment = Parse("contract C\nstruct Replacement<T>\n    Self is C when T is Missing\n    private func f() => ()");
        Koto original = condition ? ((IsKoto)path.Premises!.Operands[0]).Right : Function(c, "f", implementation: true);
        Koto replacement = condition
            ? ((IsKoto)((SyntaxFormKoto)Walk(fragment.Kotonoha.RootKoto).OfType<SyntaxFormKoto>().First(x => x.Akind == KotoKind.ConditionalConformance && x.Parent is DeclarationContainerKoto).Operands[1]).Operands[0]).Right
            : Function(fragment, "f");
        var parent = original.Parent!;
        Assert.True(KotoHelper.Replace(parent, original, replacement));
        Assert.False(c.Binding.Bind(BindingMode.Final).IsComplete);
        Assert.False(identity.IsVerified);
        Assert.Equal(ConstraintProof.Error, c.Binding.ResolveConformance(type, contract, use, out _));
        Assert.True(KotoHelper.Replace(parent, replacement, original));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Same(identity, c.Binding.GetConformanceDefinition(type, contract));
        Assert.Same(path, Assert.Single(identity.Paths));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    [InlineData(512)]
    public void WarmBindingReusesConditionalPathsWithoutAllocations(int count)
    {
        var source = new System.Text.StringBuilder("contract A\n    associate E is i32\n    property item: E has get\ncontract B: A\n");
        for (var i = 0; i < count; i++)
        {
            source.Append("struct S").Append(i).Append("<T>\n    Self is A when T is Copy\n    Self is B when T is Owned\n    public var item: i32\n");
        }

        var c = Parse(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name) => Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name, bool implementation) => Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name && x.IsRequirement != implementation);

    private static IEnumerable<Koto> Walk(Koto node)
    {
        yield return node;
        foreach (var child in node.ChildNodes)
        {
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
