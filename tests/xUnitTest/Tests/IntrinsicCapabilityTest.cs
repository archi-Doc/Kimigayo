// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IntrinsicCapabilityTest
{
    private const string GrowingCopy = "contract Has\n    associate Out\nstruct G<T>\n    Self is Copy when T is Has, T.(Has).Out is Copy\n";

    // G's Copy rests on its argument's Out (R7-C6 a): W grows it without end; W1 to W3 grow it three times and end.
    private const string Endless = "struct W<X>\n    Self is Has\n    associate Has.Out is G<W<(X, X)>>\n";

    private const string Keep = "func keep(v: ref/G<W<i32>>) => ()\n";

    private const string CopyOptIn = "struct S\n    Self is Copy\n    var g: G<W<i32>>\n";

    private const string Reads = "func inspect(x: G<W<i32>>) -> ()\n    let a = x\n    let b = x\n    return\n";

    private const string GrowingThree = "struct W1<X>\n    Self is Has\n    associate Has.Out is G<W2<(X, X)>>\nstruct W2<X>\n    Self is Has\n" +
        "    associate Has.Out is G<W3<(X, X)>>\nstruct W3<X>\n    Self is Has\n    associate Has.Out is i32\n";

    private const string Dup = "func dup(x: G<W1<i32>>) -> (G<W1<i32>>, G<W1<i32>>)\n    return (x, x)\n";

    private const string DupSecond = "func dup2(x: G<W2<(i32, i32)>>) -> (G<W2<(i32, i32)>>, G<W2<(i32, i32)>>)\n    return (x, x)\n";

    // A Contract with an associated Type that B3 conforms to only where its argument is Copy (R7-C6 a, R3.8.7's rows).
    private const string ConditionalItem = "contract Has3\n    associate Item\nstruct B3<T>\n    Self is Has3 when T is Copy\n    associate Has3.Item is i32\n    var v: i32\n";

    // An associated Type inferred from a witness's result (SPEC 8.6.2), the projection over the endless Type.
    private const string InferredItem = "contract C\n    associate Item\n    func read() -> Self.Item\nstruct S\n    Self is C\n    public func read() -> B3<G<W<i32>>>.(Has3).Item => 1\n";

    // An Owned chain through a projection whose conformance binding uses itself (the deleted guard's Owned case; SPEC 11.8, 15.3.5).
    private const string OwnedChain = "contract Has\n    associate Out\nstruct W<X>\n    Self is Has\n    associate Has.Out is Box<W<(X, X)>>\nstruct Box<T>\n    T is Has\n    var f: Array<T.(Has).Out>\n";

    [Theory]
    [InlineData("i32", true, true)]
    [InlineData("bool", true, true)]
    [InlineData("char", true, true)]
    [InlineData("f64", true, true)]
    [InlineData("()", true, true)]
    [InlineData("string", false, true)]
    [InlineData("ref/i32 during static", true, true)]
    [InlineData("raw/i32", true, true)]
    [InlineData("(i32, string)", false, true)]
    [InlineData("(i32, bool)", true, true)]
    [InlineData("[0 of string]", false, true)]
    [InlineData("[16 of i32]", true, true)]
    [InlineData("(i32) -> i32", false, true)]
    [InlineData("Never", true, true)]
    [InlineData("(i32, Never)", true, true)]
    [InlineData("(string, Never)", false, true)]
    [InlineData("[2 of Never]", true, true)]
    [InlineData("Option<Never>", true, true)]
    [InlineData("Array<Never>", false, true)]
    public void ConcreteCapabilitiesFollowTheTypeRules(string type, bool copy, bool owned)
    {
        var c = CompilationTestHelper.ParseSuccess($"func inspect(x: {type}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var function = Function(c, "inspect");
        var bound = function.Parameters[0].Type.TypeOf()!;
        Assert.Equal(copy ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveCopy(bound, function));
        Assert.Equal(owned ? ConstraintProof.Proven : ConstraintProof.Refuted, c.Binding.ProveOwned(bound, function));
    }

    // SPEC 3.1.3: Never is Copy and is not Sealed, ObjectPayload, PrimitiveInteger or Callable, so a call whose only candidate requires
    // `T is not Copy` or one of those has no applicable overload.
    [Theory]
    [InlineData("Sealed")]
    [InlineData("ObjectPayload")]
    [InlineData("PrimitiveInteger")]
    [InlineData("Callable<() -> ()>")]
    [InlineData("not Copy")]
    public void NeverRefutesTheOtherIntrinsicCapabilities(string capability)
    {
        var c = CompilationTestHelper.ParseSuccess($"func need<T>(x: T)\n    T is {capability}\n    ()\nfunc use(n: Never) => need<Never>(n)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, static x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
    }

    [Theory]
    [InlineData("ref/i32", ConstraintProof.Proven)]
    [InlineData("uniq/i32", ConstraintProof.Refuted)]
    [InlineData("ref/(uniq/i32 during a) during b", ConstraintProof.Proven)]
    [InlineData("uniq/(ref/i32 during a) during b", ConstraintProof.Refuted)]
    [InlineData("obj/S", ConstraintProof.Refuted)]
    [InlineData("rc/S", ConstraintProof.Refuted)]
    [InlineData("arc/S", ConstraintProof.Refuted)]
    [InlineData("objref/S", ConstraintProof.Proven)]
    [InlineData("objuniq/S", ConstraintProof.Refuted)]
    public void CopyUsesOnlyTheOuterSemantics(string type, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct S\nfunc inspect(x: {type}) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Theory]
    [InlineData("struct S\n    let x: i32", ConstraintProof.Refuted)]
    [InlineData("struct S\n    Self is Copy\n    let x: i32", ConstraintProof.Proven)]
    [InlineData("enum S\n    A\n    B", ConstraintProof.Refuted)]
    [InlineData("enum S\n    Self is Copy\n    A\n    B(i32)", ConstraintProof.Proven)]
    public void NominalCopyRequiresExplicitOptIn(string declaration, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess(declaration + "\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Theory]
    [InlineData("struct S\n    Self is Copy\n    let x: string")]
    [InlineData("enum S\n    Self is Copy\n    A(string)")]
    [InlineData("struct S<T>\n    Self is Copy\n    let x: T")]
    [InlineData("struct S\n    Self is Copy\n    drop\n        ()")]
    public void InvalidCopyPromisesFailDefinitionChecking(string source)
    {
        var c = CompilationTestHelper.ParseSuccess(source);
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code is DiagnosticCode.UnsatisfiedConstraint_Kd or DiagnosticCode.UnprovenConstraint_Kd);
    }

    [Fact]
    public void GenericDerivationUsesDeclaredInputs()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Box<T>\n    T is Copy\n    Self is Copy\n    let value: T\nfunc inspect(x: Box<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void InferredStoredTypesAreReadyBeforeNominalCapabilityQueries()
    {
        var c = CompilationTestHelper.ParseSuccess("group G\n    public func value<T>(x: T) -> T\n        T is Copy\n        return x\nstruct S\n    Self is Copy\n    let x = G.value(1)\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Theory]
    [InlineData("T is i32", ConstraintProof.Proven)]
    [InlineData("T is string", ConstraintProof.Refuted)]
    public void CapabilitiesFollowExplicitTypeIdentity(string assumption, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"func inspect<T>(x: T)\n    {assumption}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void IdentityCyclesRetainIndependentFiniteEvidence()
    {
        var c = CompilationTestHelper.ParseSuccess("func inspect<T, U>(x: T)\n    T is U\n    U is T\n    U is i32\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Theory]
    [InlineData("ref", ConstraintProof.Proven)]
    [InlineData("object", ConstraintProof.Refuted)]
    [InlineData("borrow", ConstraintProof.Unknown)]
    [InlineData("raw", ConstraintProof.Proven)]
    public void PairCapabilityUsesDeclaredSemantics(string semantics, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"func inspect<s/T>(x: s/T)\n    s is {semantics}\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void OwnerPairReconstructionUsesTheTargetsCapabilities()
    {
        var c = CompilationTestHelper.ParseSuccess("func inspect<s/T>(x: s/T)\n    s is owner\n    T is Copy and Owned\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void DirectBaseParticipatesInCopyDerivation()
    {
        var c = CompilationTestHelper.ParseSuccess("open struct Base\n    Self is Copy\n    let x: i32\nstruct Derived: Base\n    Self is Copy\n    let y: bool\nfunc inspect(x: Derived) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        var bad = CompilationTestHelper.ParseSuccess("open struct Base\n    let text: string\nstruct Derived: Base\n    Self is Copy");
        Assert.False(bad.Bind().IsComplete);
        Assert.Contains(bad.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    [Fact]
    public void ComputedMembersDoNotContributeStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    Self is Copy\n    computed text: string\n        get(self: ref/Self) -> string => \"text\"\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void UnknownOriginsDoNotProveNegatedOwned()
    {
        var c = CompilationTestHelper.ParseSuccess("func reject<T>(x: T)\n    T is not Owned\n    ()\nfunc inspect(x: ref/i32 during a)\n    reject(x)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is InvocationKoto);
    }

    [Fact]
    public void CopyCycleTerminatingAtAPointerIsProven()
    {
        var c = CompilationTestHelper.ParseSuccess("struct A\n    Self is Copy\n    let b: B\nstruct B\n    Self is Copy\n    let a: raw/A\nfunc inspect(x: A) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void RecursiveOwnedPropagatesAReachableOrigin()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Node {source}\n    let next: obj/Node{node}\n        origin node.source == source\n    let value: ref/i32 during source\nfunc inspect(x: Node) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    // SPEC 15.3.5 (DECISIONS BU-22; OQ-F5-2 (b), PLAN G174): Owned is computed once per declaration in its own parameters, so a field
    // obj/Chain<(T, T)> needs no instance of its storage and Chain<i32> is Owned; its arguments and Origins still decide.
    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("ref/i32 during a", ConstraintProof.Unknown)]
    [InlineData("Text.FixedBuffer during a", ConstraintProof.Refuted)]
    public void NonRegularRecursionIsOwnedPerDeclaration(string argument, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"struct Chain<T>\n    let next: obj/Chain<(T, T)>\nfunc inspect(x: Chain<{argument}>) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    // An invalid stored Field is Error through every declaration that stores its declaration (SPEC 8.7.3: an object form over an opted-out
    // Core).
    [Fact]
    public void InvalidStorageIsErrorThroughAComponent()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Parser\n    Self is not ObjectPayload\nstruct Bad\n    let p: obj/Parser\n" +
            "struct Uses<T>\n    let b: Bad\n    let v: T\n" +
            "func inspect(x: Uses<i32>) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Error, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void FiniteNestedInstancesOfOneDeclarationAreNotTreatedAsExpandingRecursion()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Box<T>\n    Self is Copy when T is Copy\n    let value: T\nfunc inspect(x: Box<Box<i32>>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void RecursiveGenericStorageCanStabilizeAtAFixedInstance()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Node<T>\n    let next: obj/Node<(i32, i32)>\nfunc inspect(x: Node<i32>) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void MultipleConditionalPremisesAreConjoined()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Pair<T, U>\n    Self is Copy when T is Copy, U is Copy and Owned\n    let first: T\n    let second: U\nfunc inspect(x: Pair<i32, bool>, y: Pair<i32, string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(f.Parameters[1].Type.TypeOf()!, f));
    }

    [Fact]
    public void EnumPayloadsParticipateInStaticStorageValidation()
    {
        var c = CompilationTestHelper.ParseSuccess("enum E<T>\n    Value(T)\ngroup Globals\n    var value: E<ref/i32 during static> = make()\n    func make() -> E<ref/i32 during static> => $abort(\"unused\")");
        Assert.False(c.Bind().IsComplete);
        // Owned is a language capability; retained static-borrow initialization remains an implementation boundary.
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("Unsupported_Kd", record.Code);
        Assert.Equal("var value: E<ref/i32 during static> = make()", record.Text);
    }

    [Fact]
    public void OwnedDoesNotPermitStoredStaticBorrowsInGlobalStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("struct View {source}\n    let value: ref/i32 during source\nfunc inspect(x: View)\n    origin x.source == static\n    ()\ngroup Globals\n    var value: View{v} = make()\n        origin v.source == static\n    func make() -> View during static => $abort(\"unused\")");
        Assert.False(c.Bind().IsComplete);
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    // S opts in to Copy, so the proof reads its incomplete storage: without the opt-in, S is Refuted by its declaration before any
    // Field is read (SPEC 3.5.2; G1220-1 O2, as G1220-2 rules for TypeDagScalingTest.AFailedPartOfASharedTupleFailsItsConstraint).
    [Fact]
    public void IncompleteStorageDoesNotSupplyNegativeCopyEvidence()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    Self is Copy\n    let x: Missing\nfunc inspect(x: S) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    // s22b (PLAN G949 Stage 2 U3, B4; changed by decision, B's U3 ruling 30): a compilation's library is fixed when its Binding is made,
    // so Prepare after a read of the core identities is the internal-invariant form, in both overloads, and leaves the compilation as it
    // was. Stage 1's row read them before Prepare and kept them across it, which B4 removes.
    [Fact]
    public void PrepareAfterTheCoreIdentitiesAreReadIsRefused()
    {
        var c = Compilation.CreateForTest();
        var library = c.Library;
        var copy = library.Copy;
        Assert.Contains("(InternalInvariant; PLAN G949 Stage 2 U3)", Assert.Throws<InvalidOperationException>(() => c.Prepare("x86_64-pc-windows-msvc")).Message);
        Assert.Contains("(InternalInvariant; PLAN G949 Stage 2 U3)", Assert.Throws<InvalidOperationException>(() => c.Prepare("x86_64-pc-windows-msvc", new DependencyPartition([]))).Message);
        Assert.Equal(Kimi.Compiler.Target.TargetTriple.Invalid, c.TargetTriple);
        Assert.Same(library, c.Library);
        Assert.Same(copy, c.Library.Copy);
    }

    [Fact]
    public void CallableIdentityDoesNotAcceptAnUnspecifiedRequirementShape()
    {
        var c = CompilationTestHelper.ParseSuccess("func inspect<T>(x: T)\n    T is Callable\n    ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConditionalCopyDoesNotRestrictTypeFormation(string argument, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"enum Option<T>\n    Self is Copy when T is Copy\n    Some(T)\n    None\nfunc inspect(x: Option<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(expected, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void ConditionalCopyAtMemberPositionUsesSeparatePremises()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Box<T>\n    let value: T\n    Self is Copy when T is Copy\nfunc inspect(x: Box<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(Function(c, "inspect").Parameters[0].Type.TypeOf()!, Function(c, "inspect")));
    }

    // SPEC F.6.2 ConformanceRequirement, 8.8.1 (PB3, O1 (a)): a conformance condition joins its atoms with "and", so "or" and "not" are
    // Parsing's record at the condition's requirement (ConformanceRequirement).
    [Theory]
    [InlineData("T is Copy or Owned", "Copy or Owned")]
    [InlineData("T is not Copy", "not Copy")]
    public void ConditionalCopyRejectsNonPositivePremises(string premise, string requirement)
    {
        var source = $"struct Box<T>\n    Self is Copy when {premise}\n    let value: T";
        var record = Assert.Single(TestDiagnostics.Of(CompilationTestHelper.Parse(source, "Hello.kimi"), "Hello.kimi"));
        Assert.Equal("ExpectedSyntax_Kd", record.Code);
        Assert.Contains("Expected conformance requirement", record.Message, StringComparison.Ordinal);
        Assert.Equal(source.IndexOf(requirement, StringComparison.Ordinal), record.Span.Start);
        Assert.Equal(requirement.Length, record.Span.Length);
    }

    [Fact]
    public void UnusedTypeArgumentsAndPointerPointeesRetainOwnedDependencies()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Phantom<T>\nstruct Pointer<T>\n    let value: raw/T\nfunc inspect(x: Phantom<ref/i32 during a>, y: Pointer<ref/i32 during a>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.All(f.Parameters, p => Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(p.Type.TypeOf()!, f)));
    }

    [Fact]
    public void FunctionOwnedClosesOnlyItsOwnQuantifiedOrigins()
    {
        var c = CompilationTestHelper.ParseSuccess("func inspect(callback: (ref/i32) -> ref/i32, fixed: (ref/i32 during a) -> (), seed: ref/i32 during a) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        var signature = f.Parameters[0].Type.TypeOf()!;
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(signature, f));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(signature.Components[0].Components[0], f));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(signature.Components[1], f));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[1].Type.TypeOf()!, f));
    }

    [Fact]
    public void OwnedSubstitutesStoredGenericTypesAndNamedOrigins()
    {
        var c = CompilationTestHelper.ParseSuccess("struct View<T> {source}\n    let value: ref/T during source\nfunc inspect(x: View<i32>, y: View<i32>)\n    origin x.source == static\n    ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveOwned(f.Parameters[1].Type.TypeOf()!, f));
    }

    [Fact]
    public void StaticOuterBorrowRequiresTheInnerDependencyToBeStatic()
    {
        var c = CompilationTestHelper.ParseSuccess("func inspect(x: ref/(ref/i32 during a) during static) => ()");
        c.Bind();
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void RecursiveCopyIsAnInvalidInlineLayout()
    {
        // A Copy proof can recurse only through by-value storage, which SPEC 21.3.2 rejects as an
        // invalid inline layout; the cyclic declarations never prove themselves Copy.
        var c = CompilationTestHelper.ParseSuccess("struct A\n    Self is Copy\n    let b: B\nstruct B\n    Self is Copy\n    let a: A");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == DiagnosticCode.InvalidInlineLayout_Kd));
        Assert.DoesNotContain(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd && x.Node is not Kimi.Compiler.Parsing.IsKoto);
        Assert.Contains(c.Binding.Issues, x => x.Node is Kimi.Compiler.Parsing.IsKoto);
    }

    [Fact]
    public void RecursiveOwnedUsesReachableStorage()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Node\n    let next: obj/Node\nfunc inspect(x: Node) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    [Fact]
    public void IntrinsicCallsUseOrdinaryConstraintApplicability()
    {
        var c = CompilationTestHelper.ParseSuccess("func copy<T>(x: T) -> T\n    T is ::Kimi.Copy and Kimi.Owned\n    return x\nlet x = copy(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var bad = CompilationTestHelper.ParseSuccess("func copy<T>(x: T) -> T\n    T is Copy\n    return x\nlet x = copy(\"text\")");
        Assert.False(bad.Bind().IsComplete);
    }

    [Fact]
    public void QualifiedIntrinsicIdentitySurvivesLocalShadowingAndAliasLookup()
    {
        var c = CompilationTestHelper.ParseSuccess("alias Core\ncontract Copy\ngroup Core\n    public contract Copy\nfunc f<T>(x: T)\n    T is ::Kimi.Copy\n    ()\nf(1)");
        c.Bind();
        var f = Function(c, "f");
        Assert.Same(c.Library.Copy, ((IsKoto)f.TypeConstraints[0]).ConstraintOf()!.Contract);
        Assert.Equal(IntrinsicKind.None, c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "Copy").SymbolOf()!.Intrinsic);
    }

    [Fact]
    public void RebindingInvalidatesSuccessfulDerivationAfterStorageAppend()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S\n    Self is Copy\n    let x: i32\nfunc inspect(x: S) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var core = c.Library.Copy;
        var s = c.Kotonoha.RootKoto.NestedContainers.Single();
        c.Kotonoha.CreateCodeContext().Parse(s, "let text: string");
        Assert.False(c.Bind().IsComplete);
        Assert.Same(core, c.Library.Copy);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmCapabilityBindingReusesWorkStorage(bool storedTypes)
    {
        var declarations = storedTypes ? "struct Box<T>\n    Self is Copy when T is Copy\n    let value: T\nvar input: Box<i32>\n" : "struct S\n    Self is Copy\n    let x: i32\n";
        var c = CompilationTestHelper.ParseSuccess(declarations + "func id<T>(x: T) -> T\n    T is Copy and Owned\n    return x\n" + string.Join('\n', Enumerable.Range(0, 128).Select(i => $"let x{i} = id({(storedTypes ? "input" : i.ToString())})")));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
    }

    // Rows as CompilationTestHelper.AssertRecordsAtLines reads them: (unit, form, program, records), the verdict from SPEC. A unit other than
    // guard is the plan's unit that makes the row pass: the row is Explicit until that unit lands and removes the marker.
    [Theory]
    // SPEC 8.3.3 Limits: a proof growing without end is a resource failure, never Unknown or Refuted, reported once at the declaration that reaches the
    // limit, at its first read (DECISIONS BU-134's site); a second function reaches it by its own judgment, never by the order of checks
    // (one record each, as the round bound's, O-321 (b)); finite bindings prove Copy. A second reader rests on that record; an explicit
    // move needs no Copy; an independent error keeps its own record (SPEC 23.1.2).
    [InlineData("C6", "g200-copy", GrowingCopy + Endless + Reads, "Resource: let a = x")]
    [InlineData("C6", "g200-two", GrowingCopy + Endless + Reads + "func again(y: G<W<i32>>) -> ()\n    let c = y\n    let d = y\n    return\n", "Resource: let a = x | Resource: let c = y")]
    [InlineData("C6", "g200-move", GrowingCopy + Endless + "func take(x: G<W<i32>>) -> G<W<i32>> => x@move\n", "")]
    [InlineData("C6", "g200-nouse", GrowingCopy + Endless + "func inspect(x: G<W<i32>>) => ()\n", "")]
    [InlineData("C6", "g200-discard", GrowingCopy + Endless + "func look(x: G<W<i32>>) -> ()\n    x\n    return\n", "Resource: x")]
    [InlineData("C6", "g200-bad", GrowingCopy + Endless + Reads + "func bad() -> i32 => true\n", "Resource: let a = x | TypeMismatch_Kd: func bad() -> i32 => true")]
    // SPEC 7.7.2, 23.1.2 (F's C8 ruling 3, F3): an omitted capture list captures by Copy, so a closure whose capture's Copy reaches the
    // limit is the limit's record at the closure; a second closure of the same definition reads the refused proof and keeps no record
    // of its own (one record a declaration, a closure being its definition's); a finite growth captures (the form's valid twin). The
    // capture's TransferRequired_Kd beside the limit's record rests on it (Q13 class 4, F's R7-E4 rulings 34 and 40): it stays until
    // ruling 34's open issue, IsDerived reading each numbered problem's own cause instead of the Closure node's one prerequisite.
    [InlineData("C6", "g200-cap", GrowingCopy + Endless + Keep + "func hold(x: G<W<i32>>) -> ()\n    let f = func () => keep(x@ref)\n    return\n", "Resource: let f = func () => keep(x@ref) | TransferRequired_Kd: let f = func () => keep(x@ref)")]
    [InlineData("C6", "g200-cap2", GrowingCopy + Endless + Keep + "func hold(x: G<W<i32>>) -> ()\n    let f = func () => keep(x@ref)\n    let g = func () => keep(x@ref)\n    return\n", "Resource: let f = func () => keep(x@ref) | TransferRequired_Kd: let f = func () => keep(x@ref)")]
    // F's ruling 216's reading for a block (SPEC 8.3.3, BindingScope.Declaration): a read in an `if` body is its definition's, so it
    // rests on the definition's one record. A block and a closure are each their own capability scope, which evaluates the chain again:
    // the height limit inside a proof is the declaration's refusal (RefuseProof, F's C8 ruling 5 (b1)), so the later scope rests on it.
    [InlineData("C6", "g200-blk", GrowingCopy + Endless + "func inspect(x: G<W<i32>>) -> ()\n    let a = x\n    if true\n        let b = x\n    return\n", "Resource: let a = x")]
    [InlineData("C6", "g200-capfin", GrowingCopy + GrowingThree + "func keep(v: ref/G<W1<i32>>) => ()\nfunc hold(x: G<W1<i32>>) -> ()\n    let f = func () => keep(x@ref)\n    let g = func () => keep(x@ref)\n    return\n", "")]
    [InlineData("guard", "g200-fin", GrowingCopy + "struct V\n    Self is Has\n    associate Has.Out is i32\nstruct W<X>\n    Self is Has\n    associate Has.Out is G<V>\nfunc inspect(x: G<W<i32>>) -> ()\n    let a = x\n    let b = x\n    return\n", "")]
    // SPEC 8.3.3 Limits and Finality (R7-C6 a): a growth that ends is proven, whichever proof meets the larger instance first; the
    // proof's own guard answered Unknown at the second growth step, by the order in which the proofs met the instances.
    [InlineData("C6", "g200-grow3", GrowingCopy + GrowingThree + Dup, "")]
    [InlineData("C6", "g200-grow3-ab", GrowingCopy + GrowingThree + DupSecond + Dup, "")]
    [InlineData("C6", "g200-grow3-ba", GrowingCopy + GrowingThree + Dup + DupSecond, "")]
    // SPEC 3.5, 8.3.3 (F's C8 ruling 7: a Copy query is asked only where a judgment needs its answer, from that judgment's site): a
    // moved enum payload needs no Copy, so no judgment rests on the limit (valid); a Copy opt-in over the endless Type is the limit's
    // record at its clause (SPEC 3.5.2: its stored Types must be Copy), and a function reading the Type keeps its own (one a
    // declaration, in either order).
    [InlineData("C6", "s-opt-V", GrowingCopy + Endless + "func f(x: G<W<i32>>) -> Option<G<W<i32>>>\n    return .Some(x@move)\n", "")]
    // SPEC 3.5 (B1; F's C8 ruling 29 C2): a moved payload needs no Copy, so a construction over a moved payload whose Type's Copy opt-in
    // failed keeps no Language record of its own; the opt-in keeps its record (dev added InvalidConstraint_Kd at the construction).
    [InlineData("C6", "e-move-bad-I", "struct V2\n    Self is Copy\n    public var s: string = \"\"\n    public init() => ()\nfunc f(b: V2) -> Option<V2>\n    return .Some(b@move)\n", "UnsatisfiedConstraint_Kd: Self is Copy")]
    [InlineData("C6", "r-epay-V", GrowingCopy + Endless + "enum E\n    A(G<W<i32>>)\n    B\nfunc mk(x: G<W<i32>>) -> E\n    return .A(x@move)\n", "")]
    [InlineData("C6", "r-scopy-R", GrowingCopy + Endless + CopyOptIn, "Resource: Self is Copy")]
    [InlineData("C6", "r-ecopy-R", GrowingCopy + Endless + "enum E\n    Self is Copy\n    A(G<W<i32>>)\n", "Resource: Self is Copy")]
    [InlineData("C6", "r-sfun-R", GrowingCopy + Endless + CopyOptIn + Reads, "Resource: Self is Copy | Resource: let a = x")]
    [InlineData("C6", "r-funs-R", GrowingCopy + Endless + Reads + CopyOptIn, "Resource: Self is Copy | Resource: let a = x")]
    // SPEC 3.5, 8.3.3, 23.1.2 (BU-134's dependents rule): `@copy` needs proven Copy, so its judgment rests on the limit's record and
    // keeps no Language record of its own.
    // SPEC 14.2.2, 17.5.3, 8.3.3: a discarded construction's lint asks whether its Type is proven Copy after the pass; the question is
    // no judgment, so its limit records nothing (valid; the Type is not proven Copy).
    [InlineData("C6", "s-dlint-V", GrowingCopy + Endless + "func f(x: G<W<i32>>) -> ()\n    Option.Some(x@move)\n    return\n", "")]
    [InlineData("C6", "r-copyx-R", GrowingCopy + Endless + "func f(x: G<W<i32>>) -> ()\n    let a = x@copy\n    return\n", "Resource: let a = x@copy")]
    // SPEC 8.3.3 Limits, 23.1.2 (F's C8 ruling 9 D1): a judgment's proof reaching the limit records at the judgment's own site and its
    // Language records rest on it: K's `T is Copy` at a Field's Type; a group's stored Property's Owned (SPEC 11.8), whose projection grows
    // through a conformance binding that uses itself.
    [InlineData("C6", "t-kfield-R", GrowingCopy + Endless + "struct K<T>\n    T is Copy\n    var v: i32\nstruct M\n    var k: K<G<W<i32>>>\n", "Resource: var k: K<G<W<i32>>>")]
    [InlineData("C6", "t-static-R", OwnedChain + "group Config\n    public var b: Option<Box<W<i32>>> = .None\n", "Resource: public var b: Option<Box<W<i32>>> = .None")]
    // SPEC 15.1.6 (F's C8 ruling 9 D2): a part of an owned Subject or iteration item is transferred with its responsibility, so no Copy
    // is asked and nothing rests on the limit (valid); a Place Subject or a borrowed iteration is borrowed (the twins, valid).
    [InlineData("C6", "t-patmove-V", GrowingCopy + Endless + "func f(o: Option<G<W<i32>>>) -> ()\n    match o@move\n        .Some(let v) => ()\n        _ => ()\n    return\n", "")]
    [InlineData("C6", "t-trymove-V", GrowingCopy + Endless + "func f(o: Option<G<W<i32>>>) -> Option<i32>\n    let v = try o@move\n    return .None\n", "")]
    [InlineData("C6", "t-formove-V", GrowingCopy + Endless + "func f(arr: Array<G<W<i32>>>) -> ()\n    for v in arr@move\n        let n = 1\n    return\n", "")]
    [InlineData("C6", "t-patref-V", GrowingCopy + Endless + "func f(o: Option<G<W<i32>>>) -> ()\n    match o\n        .Some(let v) => ()\n        _ => ()\n    return\n", "")]
    [InlineData("C6", "t-forref-V", GrowingCopy + Endless + "func f(arr: Array<G<W<i32>>>) -> ()\n    for v in arr\n        let n = 1\n    return\n", "")]
    // SPEC 3.5.2, 8.7.1, 23.1.2 (F's C8 ruling 9 C1): a Contract requiring Copy, conformed by a Copy opt-in the limit refused: the
    // conformance rests on the opt-in's record and keeps no Language record of its own.
    [InlineData("C6", "t-dupc-R", GrowingCopy + Endless + "contract Dup\n    Self is Copy\nstruct V2\n    Self is Copy\n    Self is Dup\n    var g: G<W<i32>>\n", "Resource: Self is Copy")]
    // SPEC 8.3.3 Limits, IMPL 21.3.2 (R3.8.7): the projection B3<G<W<i32>>>.(Has3).Item reached first by a written Type or a stored Type's
    // kept part, one record at the written Type; with no growth an associated Type inferred from a witness naming it is accepted (the
    // limit's control).
    [InlineData("C6", "r38-walk-R", GrowingCopy + Endless + ConditionalItem + "func f(p: B3<G<W<i32>>>.(Has3).Item) -> ()\n    return\n", "Resource: func f(p: B3<G<W<i32>>>.(Has3).Item) -> ()")]
    [InlineData("C6", "r38-keep-R", GrowingCopy + Endless + ConditionalItem + "struct K3<T>\n    T is Has3\n    var f: T.(Has3).Item\nstruct M4\n    var k: K3<B3<G<W<i32>>>>\n", "Resource: var k: K3<B3<G<W<i32>>>>")]
    [InlineData("C6", "r38-ctl-V", GrowingCopy + "struct W<X>\n    Self is Has\n    associate Has.Out is i32\n" + ConditionalItem + InferredItem, "")]
    // SPEC 10.1, 10.4, 23.1.2 (the C6a-1 review's N4): a candidate whose trial reaches the limit and is not selected (step 3 prefers the
    // candidate with no generic parameters) leaves no record: its Unknown judgments are no obligations; selected, it is the call's limit.
    [InlineData("C6", "n4-unsel-V", GrowingCopy + Endless + "func h<T>(x: ref/T) -> ()\n    T is Copy\n    return\nfunc h(x: ref/G<W<i32>>) -> ()\n    return\nfunc caller(p: ref/G<W<i32>>) -> ()\n    h(p)\n    return\n", "")]
    [InlineData("C6", "n4-sel-R", GrowingCopy + Endless + "func h<T>(x: ref/T) -> ()\n    T is Copy\n    return\nfunc caller(p: ref/G<W<i32>>) -> ()\n    h(p)\n    return\n", "Resource: h(p)")]
    public void AProofGrowingWithoutEndIsAResourceFailure(string unit, string form, string program, string records)
        => CompilationTestHelper.AssertRecordsAtLines($"{form} [{unit}]", program + "()", records);

    // SPEC 15.3.5, 8.3.3 (F's C8 ruling 29 C5; the deleted guard's Owned control): G<W<i32>> is Owned though its Copy grows without
    // end: Owned's structure reads no Copy premise, so the endless projection is never formed for it.
    [Fact]
    public void AnEndlessCopyGrowthLeavesOwnedProven()
    {
        var c = CompilationTestHelper.ParseSuccess(GrowingCopy + Endless + "func inspect(x: G<W<i32>>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "inspect");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveOwned(f.Parameters[0].Type.TypeOf()!, f));
    }

    // SPEC 8.3.3 Limits, 23.1.2 (R7-M38, R7-M94; C8 ruling 30, PLAN G651 U2b (5)): after the pass a capability work's own height refusal
    // is the work's, the fourth after-run owner, as s-dlint-V's lint inside the lint walk's site: a Copy question no judgment of the pass
    // asked, asked after Bind with no after-run site open, answers Unknown with no record, never a broken contract (no owner: R13.1's
    // throw) or a mark at an enclosing owner.
    [Fact]
    public void AnEndlessCopyGrowthAskedAfterThePassIsTheWorksUnknown()
    {
        var c = CompilationTestHelper.ParseSuccess(GrowingCopy + Endless + "func f(x: G<W<i32>>) => ()\n");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var f = Function(c, "f");
        Assert.Equal(ConstraintProof.Unknown, c.Binding.ProveCopy(f.Parameters[0].Type.TypeOf()!, f));
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
    }

    // SPEC 8.3.3 Limits, IMPL A.4 (R7-C6 a): a growth that ends at the limit's edge. W1 to W20 wrap their argument in 400 Option layers
    // a level, W21 in `last`, W22's Out is i32: G's Copy forms G<W22<Option^(8,000 + last)<i32>>>, under the height limit at 100 (Proven)
    // and past it at 300 (the limit's one Resource record at the first read; the second read on its line rests on it).
    [Theory]
    [InlineData(100, "")]
    [InlineData(300, "Resource: return (x, x)")]
    public void AGrowthThatEndsIsProvenUnderTheHeightLimit(int last, string records)
    {
        var levels = new System.Text.StringBuilder(GrowingCopy);
        for (var k = 1; k <= 21; k++)
        {
            var layers = k < 21 ? 400 : last;
            levels.Append($"struct W{k}<X>\n    Self is Has\n    associate Has.Out is G<W{k + 1}<{string.Concat(Enumerable.Repeat("Option<", layers))}X{new string('>', layers)}>>\n");
        }

        levels.Append("struct W22<X>\n    Self is Has\n    associate Has.Out is i32\n").Append(Dup);
        CompilationTestHelper.AssertRecordsAtLines($"edge-{last}", levels + "()", records);
    }

    // SPEC 8.3.3 Limits, DECISIONS BU-19 (R7-C6 a, F's C8 ruling 5): a refused chain is linear in its works. A1 to A20 wrap their argument
    // in 400 Option layers a level, A21 in `extra`, so E's endless chain starts near the height limit and reaches it after about
    // 190 - extra works; each work's Type has every part of the one before it, so the operand contexts' walk decides a few new parts a
    // work (a part found clear is kept for the capability request), never the whole Type again (about 8,000 parts a work before).
    [Fact]
    public void ARefusedChainDecidesEachPartOnce()
    {
        var counts = new[] { 150, 110, 30 }.Select(Chain).ToArray();
        var text = string.Join(", ", counts.Select(static x => $"{x.Works} capability works: {x.Parts} parts decided"));
        TestContext.Current.TestOutputHelper?.WriteLine(text);
        var works = counts[2].Works - counts[0].Works;
        Assert.True(works >= 30 && counts[2].Parts - counts[0].Parts <= 32L * works, text);

        static (int Works, int Parts) Chain(int extra)
        {
            var source = new System.Text.StringBuilder(GrowingCopy);
            for (var k = 1; k <= 21; k++)
            {
                var layers = k < 21 ? 400 : extra; // At most 400 a written Type: the syntax nesting limit is 1,024 (IMPL A.4).
                var next = k < 21 ? $"A{k + 1}" : "E";
                source.Append($"struct A{k}<X>\n    Self is Has\n    associate Has.Out is G<{next}<{string.Concat(Enumerable.Repeat("Option<", layers))}X{new string('>', layers)}>>\n");
            }

            source.Append("struct E<X>\n    Self is Has\n    associate Has.Out is G<E<(X, X)>>\nfunc inspect(x: G<A1<i32>>) -> ()\n    let a = x\n    return\n");
            var c = CompilationTestHelper.ParseSuccess(source.ToString());
            c.Bind();
            Assert.Contains("TypeHeightLimit_Kd", Describe(c));
            return (c.Binding.CapabilityEvaluationWork, c.Binding.OperandContextWork);
        }
    }

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(f => f.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
