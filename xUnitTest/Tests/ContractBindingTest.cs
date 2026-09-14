// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ContractBindingTest
{
    [Fact]
    public void VerifiedMappingUsesDeclarationIdentities()
    {
        var c = Parse("contract C\n    func read(self: ref/Self) -> i32\nstruct S\n    Self is C\n    public func read(self: ref/Self) -> i32 => 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var contract = Container(c, "C").BoundSymbol!;
        var type = Container(c, "S").BoundType!;
        var mapping = Assert.IsType<BoundConformance>(c.Binding.GetConformance(type, contract));
        var witness = Assert.Single(mapping.Witnesses);
        Assert.Same(Assert.Single(contract.Contract!.Requirements), witness.Requirement);
        Assert.Equal("read", witness.Implementation.Name);
        Assert.NotSame(witness.Requirement, witness.Implementation);
    }

    [Theory]
    [InlineData("public func read(self: ref/Self) -> i32 => 1", true)]
    [InlineData("public func read(self: ref/Self) -> string => \"x\"", false)]
    [InlineData("public func read(self: ref/Self from static) -> i32 => 1", false)]
    [InlineData("private func read(self: ref/Self) -> i32 => 1", false)]
    [InlineData("public unsafe func read(self: ref/Self) -> i32 => 1", false)]
    [InlineData("public func read() -> i32 => 1", false)]
    [InlineData("public func other(self: ref/Self) -> i32 => 1", false)]
    public void ImplementationContractIsCheckedAfterIdentification(string member, bool valid)
    {
        var c = Parse($"contract C\n    func read(self: ref/Self) -> i32\nstruct S\n    Self is C\n    {member}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("    U is Copy\n", true)]
    [InlineData("    U is Owned\n", false)]
    public void RequirementPremisesMustProveImplementationConstraints(string premise, bool valid)
    {
        var c = Parse($"contract C\n    func apply<T>(value: T)\n        T is Copy\nstruct S\n    Self is C\n    public func apply<U>(value: U)\n{premise.Replace("    ", "        ")}        ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void DiamondRefinementRetainsOneRequirementIdentity()
    {
        var c = Parse("contract A\n    func f()\ncontract B: A\ncontract C: A\ncontract D: B, C\nstruct S\n    Self is D\n    Self is A\n    public func f() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var d = Container(c, "D").BoundSymbol!.Contract!;
        Assert.Single(d.Requirements);
        Assert.Equal(3, d.Ancestors.Count);
        Assert.Same(Container(c, "A").BoundSymbol!.Contract!.Requirements[0], d.Requirements[0]);
    }

    [Theory]
    [InlineData("contract A: B\ncontract B: A")]
    [InlineData("contract A: A")]
    [InlineData("struct S\ncontract A: S")]
    [InlineData("contract A\n    Self is A")]
    public void InvalidRefinementAndContractHeadersFail(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("associate Element is i32", "i32", true)]
    [InlineData("associate C.Element is i32", "i32", true)]
    [InlineData("associate C.Element is string", "i32", false)]
    [InlineData("", "i32", false)]
    [InlineData("associate C.Element is ref/i32 from static", "i32", false)]
    public void AssociatedIdentityIsExplicitAndPrecedesMatching(string specification, string result, bool valid)
    {
        var c = Parse($"contract C\n    associate Element\n    func read() -> Element\nstruct S\n    Self is C\n    {specification}\n    public func read() -> {result} => 1");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal("i32", Assert.Single(c.Binding.GetConformance(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!)!.AssociatedTypes).Value.Name);
        }
    }

    [Fact]
    public void ChildIdentityConstraintDeterminesInheritedAssociatedType()
    {
        var c = Parse("contract A\n    associate Element\n    func read() -> Element\ncontract B: A\n    Self.A.Element is i32\nstruct S\n    Self is B\n    public func read() -> i32 => 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("Element", false)]
    [InlineData("A.Element", true)]
    public void IndependentSameNamedAssociatedTypesRequireQualification(string name, bool valid)
    {
        var c = Parse($"contract A\n    associate Element\ncontract B\n    associate Element\nstruct S\n    Self is A\n    Self is B\n    associate {name} is i32\n    associate B.Element is string");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void ContradictoryAssociatedSpecificationsFail()
    {
        var c = Parse("contract C\n    associate Element is i32\nstruct S\n    Self is C\n    associate C.Element is string");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void VerifiedConformanceDischargesGenericCalls()
    {
        var c = Parse("contract C\n    func f()\nstruct S\n    Self is C\n    public func f() => ()\nfunc use<T>(value: T)\n    T is C\n    ()\nfunc caller(value: S) => use(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void GenericDefinitionCannotRelyOnFavorableTypeArguments()
    {
        var c = Parse("contract C\n    func f()\nstruct S<T>\n    Self is C\n    public func f<U>(value: U)\n        U is Copy\n        ()\nfunc caller(value: S<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void ProjectionUsesRequireConformanceEvidence()
    {
        var c = Parse("contract C\n    associate Element\nfunc f<T>(value: T.C.Element) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void SignatureProjectionsSeeAllLeadingConstraints()
    {
        var c = Parse("contract C\n    associate Element\nfunc f<T>(value: T.C.Element) -> i32\n    T.C.Element is i32\n    T is C\n    return value");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void GenericTypeRequirementCallRetainsSelfAndRequirementIdentity()
    {
        var c = Parse("contract C\n    func empty() -> Self\nfunc make<T>() -> T\n    T is C\n    return T.empty()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.BoundCall);
        Assert.Equal(BoundTypeKind.Parameter, call.BoundCall.ConformingType!.Kind);
        Assert.Same(Container(c, "C").BoundSymbol!.Contract!.Requirements[0], call.BoundCall.Target);
        Assert.Same(call.BoundCall.ConformingType, call.BoundType);
        Assert.Null(call.BoundCall.Receiver);
    }

    [Theory]
    [InlineData("T.C.Element", "T is C")]
    [InlineData("i32", "T is C\n    T.C.Element is i32")]
    [InlineData("i32", "T.C.Element is i32\n    T is C")]
    public void GenericInstanceRequirementCallsSubstituteAssociatedTypes(string result, string constraints)
    {
        var c = Parse($"contract C\n    associate Element\n    func read(self: ref/Self) -> Element\nfunc readOne<T>(source: ref/T) -> {result}\n    {constraints}\n    return source.read()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.NotNull(call.BoundCall!.Receiver);
        Assert.Equal(BoundTypeKind.Parameter, call.BoundCall.ConformingType!.Kind);
    }

    [Fact]
    public void GenericCallsDeduplicateDiamondPathsButNotIndependentRequirements()
    {
        var good = Parse("contract A\n    func f()\ncontract B: A\ncontract C: A\nfunc call<T>()\n    T is B and C\n    T.f()");
        Assert.True(good.Bind().IsComplete, Describe(good));
        var bad = Parse("contract A\n    func f()\ncontract B\n    func f()\nfunc call<T>()\n    T is A and B\n    T.f()");
        Assert.False(bad.Bind().IsComplete);
        Assert.Contains(bad.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void ContractNameCannotSupplyAnImplementationType()
    {
        var c = Parse("contract C\n    func f() -> i32\nlet value = C.f()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void GenericRequirementCallsCheckTheirOwnConstraints()
    {
        var c = Parse("contract C\n    func echo<T>(value: T) -> T\n        T is Copy\nfunc call<S, U>(value: U) -> U\n    S is C\n    U is Copy\n    return S.echo(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var bad = Parse("contract C\n    func echo<T>(value: T) -> T\n        T is Copy\nfunc call<S, U>(value: U) -> U\n    S is C\n    return S.echo(value)");
        Assert.False(bad.Bind().IsComplete);
    }

    [Theory]
    [InlineData("origin a(x: ref/i32 from a) -> ref/i32 from a", true)]
    [InlineData("(x: ref/i32) -> ref/i32 from x", true)]
    [InlineData("(x: ref/i32 from static) -> ref/i32 from static", false)]
    public void OriginCorrespondencePreservesInputAndResultContracts(string signature, bool valid)
    {
        var c = Parse($"contract C\n    func f origin r(x: ref/i32 from r) -> ref/i32 from r\nstruct S\n    Self is C\n    public func f {signature} => x");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void WeakerResultLifetimeFailsEvenThoughInputKeyMatches()
    {
        var c = Parse("contract C\n    func f(x: ref/i32, y: ref/i32) -> ref/i32 from x\nstruct S\n    Self is C\n    public func f(x: ref/i32, y: ref/i32) -> ref/i32 from y => y");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void SharedInputOriginsCanMeetAtARequirementCall()
    {
        var c = Parse("contract C\n    func f origin a(x: ref/i32 from a, y: ref/i32 from a) -> ref/i32 from a\nfunc call<T>(x: ref/i32, y: ref/i32) -> ref/i32 from x and y\n    T is C\n    return T.f(x, y)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        Assert.Equal(OriginKind.Intersection, call.BoundType!.Origin!.Kind);
    }

    [Fact]
    public void DistinctGenericAssociatedConstraintsCannotChooseAnArbitraryIdentity()
    {
        var c = Parse("contract C\n    associate E\nfunc f<T>(value: T.C.E)\n    T is C\n    T.C.E is i32\n    T.C.E is string\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void RefinementRejectsIncompatibleLabelsWithoutAnImplementation()
    {
        var c = Parse("contract A\n    func f(x: i32)\ncontract B\n    func f(y: i32)\ncontract C: A, B");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "C").BindingState);
    }

    [Fact]
    public void RefinementRejectsContradictoryFixedAssociatedTypesWithoutUses()
    {
        var c = Parse("contract A\n    associate E is i32\ncontract B: A\n    Self.A.E is string");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(BindingState.Invalid, Container(c, "B").BindingState);
    }

    [Fact]
    public void ARequirementCannotCaptureEnclosingTypeParameters()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct Outer<T>\n    contract C\n        func f(x: T)");
        Assert.NotEmpty(c.Kotonoha.DiagnosticCollection.GetArray());
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("T", false)]
    [InlineData("ref/i32 from static", false)]
    public void GenericAssociatedBindingsMustProveTheirCoreRole(string binding, bool valid)
    {
        var c = Parse($"contract C\n    associate E\nstruct S<T>\n    Self is C\n    associate C.E is {binding}");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void OrdinaryConstraintsCanProveAGenericAssociatedCoreBinding()
    {
        var c = Parse("contract C\n    associate E\nstruct S<T>\n    T is i32\n    Self is C\n    associate C.E is T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("public", "public", "internal", false)]
    [InlineData("internal", "public", "internal", true)]
    [InlineData("public", "internal", "internal", true)]
    [InlineData("public", "public", "public", true)]
    public void WitnessAccessCoversTheEffectiveConformanceDomain(string typeAccess, string contractAccess, string memberAccess, bool valid)
    {
        var c = Parse($"{contractAccess} contract C\n    func f()\n{typeAccess} struct S\n    Self is C\n    {memberAccess} func f() => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void WarmContractBindingReusesMetadataAndCallStorage()
    {
        var source = "contract A\n    associate E\n    func read(self: ref/Self) -> E\ncontract B: A\n    Self.A.E is i32\nstruct S\n    Self is B\n    public func read(self: ref/Self) -> i32 => 1\nfunc use<T>(x: ref/T)\n    T is B\n";
        source += string.Concat(Enumerable.Repeat("    x.read()\n", 128));
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void RebindingChecksNewMembersInsteadOfReusingAVerifiedMapping()
    {
        var c = Parse("contract C\n    func f()\nstruct S\n    Self is C\n    public func f() => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var mapping = c.Binding.GetConformance(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!)!;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public func f() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.False(mapping.IsVerified);
    }

    [Fact]
    public void QualifiedContractNamesUseTheSameRegistrationAndProjectionRules()
    {
        var c = Parse("group P\n    public contract C\n        associate E\n        func f() -> E\nstruct S\n    Self is P.C\n    associate P.C.E is i32\n    public func f() -> i32 => 1\nfunc call<T>() -> T.P.C.E\n    T is P.C\n    return T.f()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AssociatedTypeNamesCannotBeSilentlyHiddenByAChildDeclaration()
    {
        var c = Parse("contract A\n    associate E\ncontract B: A\n    associate E\n    func f() -> E");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.AmbiguousBinding_Kd);
    }

    [Fact]
    public void AssociatedIdentityCyclesCannotValidateAnImplementation()
    {
        var c = Parse("contract C\n    associate E\n    associate F\nstruct S\n    Self is C\n    associate C.E is S.C.F\n    associate C.F is S.C.E");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidAssociatedType_Kd);
    }

    [Fact]
    public void PublicConformanceCannotExposePrivateAssociatedTypes()
    {
        var c = Parse("struct Hidden\npublic contract C\n    associate E\npublic struct S\n    Self is C\n    associate C.E is Hidden");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    [Fact]
    public void PublicRequirementsCannotExposePrivateTypes()
    {
        var c = Parse("struct Hidden\npublic contract C\n    func f() -> Hidden");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InaccessibleBinding_Kd);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void RefiningCopyStillRequiresItsIntrinsicDerivation(string field, bool valid)
    {
        var c = Parse($"contract C: Copy\nstruct S\n    Self is C\n    let value: {field}");
        Assert.Equal(valid, c.Bind().IsComplete);
        if (valid)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(Container(c, "S").BoundType!, Container(c, "S")));
        }
    }

    [Fact]
    public void RefinementPremisesProveIntrinsicRequirementsInGenericBodies()
    {
        var c = Parse("contract C: Copy\nfunc copy<T>(x: T) -> T\n    T is Copy\n    return x\nfunc use<T>(x: T) -> T\n    T is C\n    return copy(x)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Theory]
    [InlineData("value => left", "value => right", true)]
    [InlineData("left", "right", false)]
    public void IdentificationUsesExternalLabelsButNotInternalNames(string a, string b, bool valid)
    {
        var c = Parse($"contract C\n    func f({a}: i32)\nstruct S\n    Self is C\n    public func f({b}: i32) => ()");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void GenericSlotKindsMatterToWitnessIdentification()
    {
        var c = Parse("contract C\n    func f<s/T>(x: s/T)\nstruct S\n    Self is C\n    public func f<T>(x: T) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.MissingContractImplementation_Kd);
    }

    [Fact]
    public void ImplementationDefaultsDoNotChangeTheRequirementCallContract()
    {
        var c = Parse("contract C\n    func f(x: i32) -> i32\nstruct S\n    Self is C\n    public func f(x: i32 = 1) -> i32 => x\nfunc call<T>() -> i32\n    T is C\n    return T.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.NotNull(c.Binding.GetConformance(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!));
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Fact]
    public void ResultOnlyOriginsCannotEscapeAsUnsubstitutedRequirementBinders()
    {
        var c = Parse("contract C\n    func f origin a() -> ref/i32 from a\nfunc call<T>()\n    T is C\n    T.f()");
        Assert.False(c.Bind().IsComplete);
        Assert.Null(Assert.Single(Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>()).BoundCall);
    }

    [Fact]
    public void GeneratedMembersCanCompleteAProvisionalConformance()
    {
        var c = Parse("contract C\n    func f()\nstruct S\n    Self is C");
        c.Binding.Bind(BindingMode.Provisional);
        var shape = Container(c, "C").BoundSymbol!.Contract;
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct S\n    public func f() => ()");
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Same(shape, Container(c, "C").BoundSymbol!.Contract);
        var mapping = c.Binding.GetConformance(Container(c, "S").BoundType!, Container(c, "C").BoundSymbol!)!;
        Assert.Same(mapping.Witnesses[0].Implementation, mapping.GetImplementation(mapping.Witnesses[0].Requirement));
    }

    [Fact]
    public void ReferencedNestedAssociatedContractsSupplyTheirFixedIdentities()
    {
        var c = Parse("contract D\n    associate F is i32\ncontract C\n    associate E is D\nfunc f<T>(value: T.C.E.D.F) -> i32\n    T is C\n    return value");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void APrivateChildCannotNarrowItsPublicAncestorsConformanceDomain()
    {
        var c = Parse("public contract A\n    func f()\ncontract B: A\npublic struct S\n    Self is B\n    internal func f() => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Fact]
    public void ConditionalCopyUsesTheSameRefinementPremiseExpansion()
    {
        var c = Parse("contract C: Copy\nstruct S<T>\n    Self is Copy when T is C\n    let value: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void WitnessPremisesUseRefinementWithoutLeakingToTheImplementationBody()
    {
        var c = Parse("contract A: Copy\ncontract C\n    func f<T>(x: T)\n        T is A\nstruct S\n    Self is C\n    public func f<U>(x: U)\n        U is Copy\n        ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
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

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code} ({x.Node.GetType().Name}, {x.Node.Akind}, {x.Node.Parent?.GetType().Name}): {x.Node}"));
}
