// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IntrinsicCapabilityTest
{
    [Theory]
    [InlineData("i32", true, true)]
    [InlineData("bool", true, true)]
    [InlineData("char", true, true)]
    [InlineData("f64", true, true)]
    [InlineData("()", true, true)]
    [InlineData("string", false, true)]
    [InlineData("ref/i32 from static", true, true)]
    [InlineData("unsafe/i32", true, true)]
    [InlineData("(i32, string)", false, true)]
    [InlineData("(i32, bool)", true, true)]
    [InlineData("[0 of string]", false, true)]
    [InlineData("[16 of i32]", true, true)]
    [InlineData("(i32) -> i32", false, true)]
    public void ConcreteCapabilitiesFollowTheTypeRules(string type, bool copy, bool owned)
    {
        var c = Parse($"func inspect(x: {type}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var function = Function(c, "inspect");
        var bound = function.Parameters[0].Type.BoundType!;
        Assert.Equal(copy ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveCopy(bound, function));
        Assert.Equal(owned ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveOwned(bound, function));
    }

    [Theory]
    [InlineData("ref/i32", ConstraintProof.Proven)]
    [InlineData("uniq/i32", ConstraintProof.Refuted)]
    [InlineData("ref/(uniq/i32 from a) from b", ConstraintProof.Proven)]
    [InlineData("uniq/(ref/i32 from a) from b", ConstraintProof.Refuted)]
    [InlineData("obj/S", ConstraintProof.Refuted)]
    [InlineData("rc/S", ConstraintProof.Refuted)]
    [InlineData("arc/S", ConstraintProof.Refuted)]
    [InlineData("objref/S", ConstraintProof.Proven)]
    [InlineData("objuniq/S", ConstraintProof.Refuted)]
    public void CopyUsesOnlyTheOuterSemantics(string type, ConstraintProof expected)
    {
        var c = Parse($"struct S\nfunc inspect origin a, b(x: {type}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Theory]
    [InlineData("struct S\n    let x: i32", ConstraintProof.Refuted)]
    [InlineData("struct S\n    Self is Copy\n    let x: i32", ConstraintProof.Proven)]
    [InlineData("enum S\n    A\n    B", ConstraintProof.Refuted)]
    [InlineData("enum S\n    Self is Copy\n    A\n    B(i32)", ConstraintProof.Proven)]
    public void NominalCopyRequiresExplicitOptIn(string declaration, ConstraintProof expected)
    {
        var c = Parse(declaration + "\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Theory]
    [InlineData("struct S\n    Self is Copy\n    let x: string")]
    [InlineData("enum S\n    Self is Copy\n    A(string)")]
    [InlineData("struct S<T>\n    Self is Copy\n    let x: T")]
    [InlineData("struct S\n    Self is Copy\n    deinit => ()")]
    public void InvalidCopyPromisesFailDefinitionChecking(string source)
    {
        var c = Parse(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Fact]
    public void GenericDerivationUsesDeclaredInputs()
    {
        var c = Parse("struct Box<T>\n    T is Copy\n    Self is Copy\n    let value: T\nfunc inspect(x: Box<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void InferredStoredTypesAreReadyBeforeNominalCapabilityQueries()
    {
        var c = Parse("group G\n    public func value<T>(x: T) -> T\n        T is Copy\n        return x\nstruct S\n    Self is Copy\n    let x = G.value(1)\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Theory]
    [InlineData("T is i32", ConstraintProof.Proven)]
    [InlineData("T is string", ConstraintProof.Refuted)]
    public void CapabilitiesFollowExplicitTypeIdentity(string assumption, ConstraintProof expected)
    {
        var c = Parse($"func inspect<T>(x: T)\n    {assumption}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void IdentityCyclesRetainIndependentFiniteEvidence()
    {
        var c = Parse("func inspect<T, U>(x: T)\n    T is U\n    U is T\n    U is i32\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Theory]
    [InlineData("ref", ConstraintProof.Proven)]
    [InlineData("object", ConstraintProof.Refuted)]
    [InlineData("borrow", ConstraintProof.Unknown)]
    [InlineData("unsafe", ConstraintProof.Proven)]
    public void PairCapabilityUsesDeclaredSemantics(string semantics, ConstraintProof expected)
    {
        var c = Parse($"func inspect<s/T>(x: s/T)\n    s is {semantics}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void OwnerPairReconstructionUsesTheTargetsCapabilities()
    {
        var c = Parse("func inspect<s/T>(x: s/T)\n    s is owner\n    T is Copy and Owned\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void DirectBaseParticipatesInCopyDerivation()
    {
        var c = Parse("open struct Base\n    Self is Copy\n    let x: i32\nstruct Derived: Base\n    Self is Copy\n    let y: bool\nfunc inspect(x: Derived) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
        var bad = Parse("open struct Base\n    let text: string\nstruct Derived: Base\n    Self is Copy");
        Assert.False(bad.Bind().IsComplete);
        Assert.Contains(bad.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    [Fact]
    public void ComputedMembersDoNotContributeStorage()
    {
        var c = Parse("struct S\n    Self is Copy\n    computed text: string\n        get(self: ref/Self) -> string => \"text\"\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void UnknownOriginsDoNotProveNegatedOwned()
    {
        var c = Parse("func reject<T>(x: T)\n    T is not Owned\n    ()\nfunc inspect origin a(x: ref/i32 from a)\n    reject(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is InvocationKoto);
    }

    [Fact]
    public void CopyCycleTerminatingAtAPointerIsProven()
    {
        var c = Parse("struct A\n    Self is Copy\n    let b: B\nstruct B\n    Self is Copy\n    let a: unsafe/A\nfunc inspect(x: A) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void RecursiveOwnedPropagatesAReachableOrigin()
    {
        var c = Parse("struct Node origin source\n    let next: obj/Node from source\n    let value: ref/i32 from source\nfunc inspect origin a(x: Node from a) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void ExpandingGenericRecursionRemainsUnknownWithoutUnboundedExpansion()
    {
        var c = Parse("struct Chain<T>\n    let next: obj/Chain<(T, T)>\nfunc inspect(x: Chain<i32>) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void FiniteNestedInstancesOfOneDeclarationAreNotTreatedAsExpandingRecursion()
    {
        var c = Parse("struct Box<T>\n    Self is Copy when T is Copy\n    let value: T\nfunc inspect(x: Box<Box<i32>>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void RecursiveGenericStorageCanStabilizeAtAFixedInstance()
    {
        var c = Parse("struct Node<T>\n    let next: obj/Node<(i32, i32)>\nfunc inspect(x: Node<i32>) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void MultipleConditionalPremisesAreConjoined()
    {
        var c = Parse("struct Pair<T, U>\n    Self is Copy when T is Copy, U is Copy and Owned\n    let first: T\n    let second: U\nfunc inspect(x: Pair<i32, bool>, y: Pair<i32, string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(f.Parameters[1].Type.BoundType!, f));
    }

    [Fact]
    public void EnumPayloadsParticipateInStaticStorageValidation()
    {
        var c = Parse("enum E<T>\n    Value(T)\ngroup Globals\n    var value: E<ref/i32 from static>");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidTypeFormation_Kd);
    }

    [Fact]
    public void OwnedDoesNotPermitStoredStaticBorrowsInGlobalStorage()
    {
        var c = Parse("struct View origin source\n    let value: ref/i32 from source\nfunc inspect(x: View from static) => ()\ngroup Globals\n    var value: View from static");
        Assert.False(c.Bind().IsComplete);
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void IncompleteStorageDoesNotSupplyNegativeCopyEvidence()
    {
        var c = Parse("struct S\n    let x: Missing\nfunc inspect(x: S) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void CoreIdentityCanBeReadBeforePreparation()
    {
        var c = Compilation.CreateForTest();
        var copy = c.Core.Copy;
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        Assert.Same(copy, c.Core.Copy);
        Assert.Same(c.Core.Kotonoha, copy.Declaration.CodeContext.Kotonoha);
    }

    [Fact]
    public void IncompatibleCompilerCoreCannotPassBinding()
    {
        var c = Parse("func inspect(x: i32) => ()");
        c.Core.Kotonoha.CreateCodeContext().Parse((ContractKoto)c.Core.Copy.Declaration, "func userCode() -> i32");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidCoreIntrinsics_Kd);
    }

    [Fact]
    public void CallableIdentityDoesNotAcceptAnUnspecifiedRequirementShape()
    {
        var c = Parse("func inspect<T>(x: T)\n    T is Callable\n    ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConditionalCopyDoesNotRestrictTypeFormation(string argument, ConstraintProof expected)
    {
        var c = Parse($"enum Option<T>\n    Self is Copy when T is Copy\n    Some(T)\n    None\nfunc inspect(x: Option<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void ConditionalCopyAtMemberPositionUsesSeparatePremises()
    {
        var c = Parse("struct Box<T>\n    let value: T\n    Self is Copy when T is Copy\nfunc inspect(x: Box<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(Function(c, "inspect").Parameters[0].Type.BoundType!, Function(c, "inspect")));
    }

    [Theory]
    [InlineData("T is Copy or Owned")]
    [InlineData("T is not Copy")]
    public void ConditionalCopyRejectsNonPositivePremises(string premise)
    {
        var c = Parse($"struct Box<T>\n    Self is Copy when {premise}\n    let value: T");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void UnusedTypeArgumentsAndPointerPointeesAreNotStoredDependencies()
    {
        var c = Parse("struct Phantom<T>\nstruct Pointer<T>\n    let value: unsafe/T\nfunc inspect origin a(x: Phantom<ref/i32 from a>, y: Pointer<ref/i32 from a>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.All(f.Parameters, p => Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(p.Type.BoundType!, f)));
    }

    [Fact]
    public void OwnedSubstitutesStoredGenericTypesAndNamedOrigins()
    {
        var c = Parse("struct View<T> origin source\n    let value: ref/T from source\nfunc inspect origin a(x: View<i32> from static, y: View<i32> from a) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[1].Type.BoundType!, f));
    }

    [Fact]
    public void StaticOuterBorrowDoesNotEraseAnInnerDependency()
    {
        var c = Parse("func inspect origin a(x: ref/(ref/i32 from a) from static) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void RecursiveCopyDoesNotProveItself()
    {
        var c = Parse("struct A\n    Self is Copy\n    let b: B\nstruct B\n    Self is Copy\n    let a: A");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Fact]
    public void RecursiveOwnedUsesReachableStorage()
    {
        var c = Parse("struct Node\n    let next: obj/Node\nfunc inspect(x: Node) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.BoundType!, f));
    }

    [Fact]
    public void IntrinsicCallsUseOrdinaryConstraintApplicability()
    {
        var c = Parse("func copy<T>(x: T) -> T\n    T is ::Core.Copy and Core.Owned\n    return x\nlet x = copy(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var bad = Parse("func copy<T>(x: T) -> T\n    T is Copy\n    return x\nlet x = copy(\"text\")");
        Assert.False(bad.Bind().IsComplete);
    }

    [Fact]
    public void QualifiedIntrinsicIdentitySurvivesLocalShadowingAndAliasLookup()
    {
        var c = Parse("alias Core\ncontract Copy\ngroup Core\n    public contract Copy\nfunc f<T>(x: T)\n    T is ::Core.Copy\n    ()\nf(1)");
        c.Bind();
        var f = Function(c, "f");
        Assert.Same(c.Core.Copy, ((IsKoto)f.TypeConstraints[0]).BoundConstraint!.Contract);
        Assert.Equal(IntrinsicKind.None, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Copy").BoundSymbol!.Intrinsic);
        Assert.False(c.Core.IsCompleteLibrary);
    }

    [Fact]
    public void RebindingInvalidatesSuccessfulDerivationAfterStorageAppend()
    {
        var c = Parse("struct S\n    Self is Copy\n    let x: i32\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var core = c.Core.Copy;
        var s = c.Kotonoha.RootKoto.NestedContainers.Single();
        c.Kotonoha.CreateCodeContext().Parse(s, "let text: string");
        Assert.False(c.Bind().IsComplete);
        Assert.Same(core, c.Core.Copy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmCapabilityBindingReusesWorkStorage(bool storedTypes)
    {
        var declarations = storedTypes ? "struct Box<T>\n    Self is Copy when T is Copy\n    let value: T\nvar input: Box<i32>\n" : "struct S\n    Self is Copy\n    let x: i32\n";
        var c = Parse(declarations + "func id<T>(x: T) -> T\n    T is Copy and Owned\n    return x\n" + string.Join('\n', Enumerable.Range(0, 128).Select(i => $"let x{i} = id({(storedTypes ? "input" : i.ToString())})")));
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 8; i++)
        {
            c.Binding.Bind(BindingMode.Final);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static Compilation Parse(string source)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Prepare("x86_64-pc-windows-msvc"));
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, source);
        Assert.Empty(c.Kotonoha.DiagnosticCollection.GetArray());
        return c;
    }

    private static FunctionKoto Function(Compilation c, string name) => Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(f => f.Name == name);

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
