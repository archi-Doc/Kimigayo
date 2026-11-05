// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintBindingTest
{
    // An owner whose clause its formation decides (PLAN G354 (2)).
    private const string OwnedHolder = "struct H<T>\n    T is Owned\n    public let t: T\n    public init(t: T) => self.t = t@move\n";
    private const string CopyHolder = "struct K<T>\n    T is Copy\n    public let t: T\n";
    private const string CallableHolder = "struct H<F>\n    F is Callable<(ref/i32) -> i32>\n    public let f: F\n    public init(f: F) => self.f = f@move\n";

    [Theory]
    [InlineData("T is i32", "i32", ConstraintProof.Proven)]
    [InlineData("T is not i32", "i32", ConstraintProof.Refuted)]
    [InlineData("T is not not i32", "i32", ConstraintProof.Proven)]
    [InlineData("T is (i32 and string)", "i32", ConstraintProof.Proven)]
    [InlineData("T is i32 and (string and bool)", "bool", ConstraintProof.Proven)]
    [InlineData("T is i32\n    T is string", "i32 and string", ConstraintProof.Proven)]
    [InlineData("T is i32", "i32 or string", ConstraintProof.Proven)]
    [InlineData("T is i32 or string", "i32 or string", ConstraintProof.Proven)]
    [InlineData("T is i32 or string", "i32", ConstraintProof.Unknown)]
    [InlineData("T is i32 or string\n    T is not i32", "string", ConstraintProof.Unknown)]
    [InlineData("T is not i32", "i32 and string", ConstraintProof.Refuted)]
    [InlineData("T is not i32\n    T is not string", "i32 or string", ConstraintProof.Refuted)]
    [InlineData("T is not i32", "i32 or string", ConstraintProof.Unknown)]
    [InlineData("T is not (i32 and string)", "not i32", ConstraintProof.Unknown)]
    [InlineData("T is i32\n    T is not i32", "bool", ConstraintProof.Error)]
    [InlineData("T is i32 and string\n    T is not string", "bool", ConstraintProof.Error)]
    [InlineData("T is i32", "i32 or Missing", ConstraintProof.Error)]
    [InlineData("T is not i32", "i32 and Missing", ConstraintProof.Error)]
    public void ProofUsesOnlySpecifiedRules(string assumptions, string query, ConstraintProof expected)
    {
        var c = Parse($"func context<T>(value: T)\n    {assumptions}\n    ()\nfunc query<T>(value: T)\n    T is {query}\n    ()");
        c.Bind();
        var context = Function(c, "context");
        var queryFunction = Function(c, "query");
        var proposition = ((IsKoto)queryFunction.TypeConstraints[0]).ConstraintOf()!;
        Assert.Equal(expected, c.Binding.Prove(proposition, queryFunction, [context.Parameters[0].Type.TypeOf()], context));
    }

    [Theory]
    [InlineData("i32", "1", true)]
    [InlineData("string", "1", false)]
    [InlineData("not string", "1", true)]
    [InlineData("i32 or string", "\"text\"", true)]
    [InlineData("i32 and string", "1", false)]
    public void CallsDischargeSubstitutedConstraints(string requirement, string argument, bool success)
    {
        var c = Parse($"func identity<T>(value: T) -> T\n    T is {requirement}\n    return value@move\nlet result = identity({argument})");
        Assert.Equal(success, c.Bind().IsComplete);
        if (!success)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.NoApplicableOverload_Kd);
        }
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("value", true)]
    [InlineData("owning", true)]
    [InlineData("ref", false)]
    [InlineData("borrow", false)]
    [InlineData("reference", false)]
    [InlineData("safe", false)]
    [InlineData("all", false)]
    public void SemanticsRequirementsUseTheSpecifiedClosedCategories(string requirement, bool success)
    {
        var c = Parse($"func f<s/T>(value: s/T)\n    s is {requirement}\n    ()\nf(1)");
        Assert.Equal(success, c.Bind().IsComplete);
    }

    [Fact]
    public void CallerAssumptionsProveCalleeConstraintsWithoutUsingCalleeAssumptions()
    {
        var c = Parse("func required<T>(value: T)\n    T is i32\n    ()\nfunc caller<U>(value: U)\n    U is i32\n    required(value)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var missing = Parse("func required<T>(value: T)\n    T is i32\n    ()\nfunc caller<T>(value: T)\n    required(value)");
        Assert.False(missing.Bind().IsComplete);
    }

    [Fact]
    public void ForwardedPairConstraintsRetainTargetProjectionIdentity()
    {
        var c = Parse("func required<s/T>(value: s/T)\n    T is i32\n    s is owning\n    ()\nfunc caller<r/U>(value: r/U)\n    U is i32\n    r is owning\n    required(value@move)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void SemanticsCategoriesCannotBeShadowedInRequirementRole()
    {
        var c = Parse("struct owning\nfunc f<s/T>(value: s/T)\n    s is owning\n    ()\nf(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void PropositionIdentityPreservesOrigins()
    {
        var c = Parse("func query<T>(value: T)\n    T is ref/i32 during static\n    ()\nfunc context(value: ref/i32 during a) => ()");
        c.Bind();
        var query = Function(c, "query");
        var context = Function(c, "context");
        var constraint = ((IsKoto)query.TypeConstraints[0]).ConstraintOf()!;
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(constraint, query, [context.Parameters[0].Type.TypeOf()], context));
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(constraint, query, [constraint.RequiredType], context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintApplicabilityIsIndependentOfDeclarationOrder(bool reverse)
    {
        const string a = "func f<T>(x: T) -> i32\n    T is string\n    return 0\n";
        const string b = "func f(x: i32) -> i32 => x\n";
        var c = Parse((reverse ? b + a : a + b) + "let result = f(1@i32)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.Empty(((FunctionKoto)call.SymbolOf()!.Declaration).GenericArguments);
    }

    [Fact]
    public void GenericTypeUsesMustSatisfyInputConstraints()
    {
        var good = Parse("struct Box<T>\n    T is i32\n    let value: T\nfunc f(x: Box<i32>) => ()");
        Assert.True(good.Bind().IsComplete, Describe(good));
        var bad = Parse("struct Box<T>\n    T is i32\n    let value: T\nfunc f(x: Box<string>) => ()");
        Assert.False(bad.Bind().IsComplete);
        Assert.Contains(bad.Binding.Issues, x => x.Code == DiagnosticCode.UnsatisfiedConstraint_Kd);
    }

    [Fact]
    public void InvalidGenericTypeInARequirementPoisonsItsEvidence()
    {
        var c = Parse("struct Box<T>\n    T is i32\n    let value: T\nfunc f<U>(value: U)\n    U is i32 or Box<string>\n    ()");
        Assert.False(c.Bind().IsComplete);
        var function = Function(c, "f");
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(((IsKoto)function.TypeConstraints[0]).ConstraintOf()!, function));
    }

    [Fact]
    public void ConstraintsDoNotDistinguishSignatures()
    {
        var c = Parse("func f<T>(x: T)\n    T is i32\n    ()\nfunc f<U>(y: U)\n    U is string\n    ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(2, c.Binding.Issues.Count(x => x.Code == DiagnosticCode.DuplicateBinding_Kd));
    }

    [Fact]
    public void OuterSlotsAreNotAlphaRenamedAsFunctionSlots()
    {
        // The Signatures are distinct (no duplicate); the pair is rejected only by the parameter acquisition shape of SPEC 7.4,
        // because the outer slot T and the own slot V are different bindings that may take different Semantics.
        var c = Parse("struct Box<T>\n    func f<U>(x: T) => ()\n    func f<V>(x: V) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Equal([DiagnosticCode.ParameterShapeMismatch_Kd], c.Binding.Issues.Select(static x => x.Code));
    }

    [Theory]
    [InlineData("func f<s/T>(x: T)\n    s is valueborrow\n    ()")]
    [InlineData("struct Dog\nfunc f<T>(x: obj/T)\n    T is Dog\n    ()")]
    [InlineData("func f<s/T, U>(x: s/U)\n    s is owner\n    ()")]
    public void DefinitionRolesCanBeProvedFromValidatedInputs(string source)
    {
        var c = Parse(source);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.DoesNotContain(c.Binding.Obligations, x => x.Deadline == BindingDeadline.Definition);
    }

    [Fact]
    public void UnconstrainedTargetProjectionCannotPassDefinitionChecking()
    {
        var c = Parse("func f<s/T>(x: T) => ()");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.UnprovenConstraint_Kd);
    }

    // SPEC 8.15: a composite clause that a caller's premises cannot prove reports the subject named by its operands.
    [Theory]
    [InlineData("s is ref or uniq", "s is borrow")]
    [InlineData("s is not owner and not obj", "s is owner or borrow")]
    public void CompositeClausesReportTheirSubjectAtTheCall(string required, string premise)
    {
        var source = $"func need<s/T>(value: ref/(s/T))\n    {required}\n    ()\nfunc outer<s/T>(value: ref/(s/T))\n    {premise}\n    need(value)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Equal("need(value)", error.Text);
        Assert.Contains($"requires {required}; this condition cannot be proven for s/T", error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("func need<T>(value: ref/T)\n    T is Copy or Owned\n    ()\nfunc outer<T>(value: ref/T) => need(value)", "need(value)", "T is Copy or Owned", "T")]
    [InlineData("func need<T>(value: ref/T)\n    T is Copy and Owned\n    ()\nfunc outer<T>(value: ref/T) => need(value)", "need(value)", "T is Copy and Owned", "T")]
    [InlineData("func need<T>(value: ref/T)\n    T is not Copy\n    ()\nfunc outer<T>(value: ref/T) => need(value)", "need(value)", "T is not Copy", "T")]
    [InlineData("contract Describe\n    func describe(self: ref/Self) -> i64\nstruct Box<T>\n    var value: T\n    Self is Describe when T is Copy and Owned\n        public func describe(self: ref/Self) -> i64 => 1\nfunc outer<T>(b: ref/Box<T>) -> i64 => b.describe()", "b.describe()", "T is Copy and Owned", "T")]
    public void CompositeContractClausesReportTheirSubjectAtTheUse(string source, string use, string clause, string subject)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Equal(use, error.Text);
        Assert.Contains($"requires {clause}; this condition cannot be proven for {subject}", error.Note, StringComparison.Ordinal);
    }

    // SPEC 8.3, 10.5: a candidate refuted by a composite clause names the subject that its operands share.
    [Theory]
    [InlineData("func need<s/T>(value: ref/(s/T))\n    s is rc or arc\n    ()\nfunc outer<s/T>(value: ref/(s/T))\n    s is borrow\n    need(value)", "s is rc or arc", "s/T")]
    [InlineData("func need<T>(value: ref/T)\n    T is not Copy or Owned\n    ()\nlet x = 1\nneed(x)", "T is not Copy or Owned", "i32")]
    // SPEC 8.6: a refuted operand keeps its cause.
    [InlineData("contract Same\n    func same(self: ref/Self, other: ref/Self) -> bool\nopen struct Shape\n    Self is Same\n    public init() => ()\n    public func same(self: ref/Self, other: ref/Self) -> bool => true\nstruct Circle: Shape\n    public init() => ()\nfunc need<T>(value: ref/T)\n    T is Same and Owned\n    ()\nlet c = Circle.init()\nneed(c)", "T is Same and Owned", "Circle. The retained implementation Shape.same keeps Shape as Self")]
    public void RefutedCompositeCandidatesNameTheirSubject(string source, string clause, string subject)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains($"requires {clause}; this condition is refuted for {subject}", error.Note, StringComparison.Ordinal);
    }

    // SPEC 8.3, 23.1.2 (PLAN G354 (2)): a Type's formation publishes the clause fact it decides on, so a call or a Function Item that
    // rests on an owner's or an argument's formation names that clause (the first at the highest rank) and, for a Type argument's, the
    // declaration that states it (an id<K<string>> row was InternalInvariant_Kd before); a callee's signature, proven again at each use,
    // explains none until G375 (null Note).
    [Theory]
    [InlineData(OwnedHolder + "func use(x: ref/i32) -> i32\n    let h = H.init(t: x)\n    return 0", "UnprovenConstraint_Kd", "H.init(t: x)", "Call init requires T is Owned; this condition cannot be proven for ref/i32")]
    [InlineData(CallableHolder + "func use(g: (ref/i32 during static) -> i32) -> i32\n    let h = H.init(f: g@move)\n    return 0", "UnprovenConstraint_Kd", "H.init(f: g@move)", "Call init requires F is Callable<(ref/i32) -> i32>; this condition cannot be proven for (ref/i32) -> i32")]
    [InlineData(OwnedHolder + "func mk<U>(u: U) -> H<U>\n    return H.init(t: u@move)\nfunc use(x: ref/i32) -> i32\n    let h = mk(u: x)\n    return 0", "UnprovenConstraint_Kd", "H.init(t: u@move)", "Call init requires T is Owned; this condition cannot be proven for U")]
    [InlineData(CallableHolder + "func use(g: i32) -> i32\n    let h = H.init(f: g)\n    return 0", "NoApplicableOverload_Kd", "H.init(f: g)", "init requires F is Callable<(ref/i32) -> i32>; this condition is refuted for i32")]
    [InlineData(OwnedHolder + "func id<U>(u: U) -> i32 => 0\nfunc use() -> i32\n    let f = id<H<ref/i32>>\n    return 0", "UnprovenConstraint_Kd", "id<H<ref/i32>>", "Function Item id: H requires T is Owned; this condition cannot be proven for ref/i32")]
    [InlineData("struct H<T>\n    T is Copy\n    public let t: T\nstruct K<T>\n    T is Copy\n    public let t: T\nfunc id2<U, V>(u: U, v: V) -> i32 => 0\nfunc use() -> i32\n    let f = id2<H<string>, K<Array<i32>>>\n    return 0", "UnsatisfiedConstraint_Kd", "id2<H<string>, K<Array<i32>>>", "Function Item id2: H requires T is Copy; this condition is refuted for string")]
    [InlineData(CopyHolder + "func g<U>() -> i32 => 0\nfunc use() -> i32\n    return g<K<string>>()", "NoApplicableOverload_Kd", "g<K<string>>()", "g: K requires T is Copy; this condition is refuted for string")]
    [InlineData(CopyHolder + "func id<U>(u: i32) -> i32 => 0\nfunc use() -> i32\n    let f: (i32) -> i32 = id<K<string>>\n    return 0", "UnsatisfiedConstraint_Kd", "id<K<string>>", "Function Item id: K requires T is Copy; this condition is refuted for string")]
    [InlineData(OwnedHolder + "func mk<U>(h: ref/H<U>) -> i32 => 0\nfunc use(h: ref/H<i32>) -> i32\n    return mk(h: h)", "UnprovenConstraint_Kd", "mk(h: h)", null)]
    public void ATypeFormationExplainsTheUseThatRestsOnIt(string source, string code, string use, string? note)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Text == use);
        Assert.Equal(code, error.Code);
        Assert.Equal(note, error.Note);
    }

    // The valid twins of ATypeFormationExplainsTheUseThatRestsOnIt.
    [Theory]
    [InlineData(OwnedHolder + "func use(x: i32) -> i32\n    let h = H.init(t: x)\n    return 0")]
    [InlineData(CallableHolder + "func use(g: (ref/i32) -> i32) -> i32\n    let h = H.init(f: g@move)\n    return 0")]
    [InlineData(OwnedHolder + "func id<U>(u: U) -> i32 => 0\nfunc use() -> i32\n    let f = id<H<i32>>\n    return 0")]
    public void ATypeFormationThatHoldsLeavesTheUseValid(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, string.Join(", ", c.Binding.Issues));
    }

    // SPEC 7.5: a member function may constrain its declaring Type's parameters (a conditional member).
    [Fact]
    public void AMemberFunctionCanConstrainItsDeclaringTypeParameter()
    {
        var c = Parse("struct Box<T>\n    func f<U>(x: U)\n        T is Equatable\n        ()");
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues));
    }

    [Fact]
    public void CompletedConcreteConformanceAbsenceCanBeRefuted()
    {
        // SPEC 8.3: absence is Refuted only after the closed conformance judgment completes; the
        // negative requirement lives at a use, since a Type declaration cannot negate a Self clause (SPEC 8.2).
        var c = Parse("contract C\nstruct S\n    Self is C\nstruct N\nfunc absent<T>(x: T)\n    T is not C\n    ()\nfunc use() => absent(N.init())");
        Assert.True(c.Bind().IsComplete);
        var s = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(s.ConstraintNodes[0].ConstraintOf()!, s));
    }

    // SPEC 8.3, 8.7.1: a primitive's conformances are fixed, so its absence from any other Contract is Refuted; a negative
    // requirement holds, and a call whose only candidate needs the conformance has no applicable overload. SPEC 3.1.3: Never
    // conforms to no Contract other than its intrinsic capabilities.
    [Theory]
    [InlineData("contract C\nfunc absent<T>(x: T)\n    T is not C\n    ()\nfunc use() => absent(1)", true, null)]
    [InlineData("contract C\nfunc present<T>(x: T)\n    T is C\n    ()\nfunc use() => present(1)", false, DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func present<T>(x: T)\n    T is Iterable\n    ()\nfunc use() => present(\"text\")", false, DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func absent<T>(x: T)\n    T is not LendingIterator\n    ()\nfunc use() => absent(true)", true, null)]
    [InlineData("contract C\nfunc absent<T>(x: T)\n    T is not C\n    ()\nfunc use(n: Never) => absent<Never>(n)", true, null)]
    [InlineData("contract C\nfunc present<T>(x: T)\n    T is C\n    ()\nfunc use(n: Never) => present<Never>(n)", false, DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func present<T>(a: T, b: T)\n    T is Equatable\n    ()\nfunc use(n: Never) => present<Never>(n, n)", false, DiagnosticCode.NoApplicableOverload_Kd)]
    public void APrimitiveConformsOnlyToItsFixedContracts(string source, bool complete, DiagnosticCode? code)
    {
        var c = Parse(source);
        Assert.Equal(complete, c.Bind().IsComplete);
        if (code is { } expected)
        {
            Assert.Contains(c.Binding.Issues, x => x.Code == expected);
        }
    }

    [Fact]
    public void ANegatedSelfClauseOtherThanTheOptOutIsRejected()
    {
        var c = Parse("contract C\nstruct N\n    Self is not C");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.InvalidSelfClause_Kd);
    }

    [Fact]
    public void SameSpelledContractDoesNotAcquireIntrinsicCopyRules()
    {
        var c = Parse("contract Copy\nfunc f<T>(x: T)\n    T is Copy\n    ()\nf(1)");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Node is InvocationKoto);
    }

    [Fact]
    public void ConstraintFactsAreRebuiltAfterSourceAppend()
    {
        var c = Parse("func f<T>(x: T)\n    T is Missing\n    ()");
        c.Bind();
        var clause = (IsKoto)Function(c, "f").TypeConstraints[0];
        Assert.Equal(ConstraintKind.Error, clause.ConstraintOf()!.Kind);
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct Missing");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(ConstraintKind.TypeIdentity, clause.ConstraintOf()!.Kind);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmConstraintPassesReusePropositionsAndFactStorage()
    {
        var c = Parse("func f<T>(x: T) -> T\n    T is i32 or string\n    return x@move\n" + string.Join('\n', Enumerable.Range(0, 128).Select(x => $"let x{x} = f({x})")));
        Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        var proposition = ((IsKoto)Function(c, "f").TypeConstraints[0]).ConstraintOf();
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Same(proposition, ((IsKoto)Function(c, "f").TypeConstraints[0]).ConstraintOf());
    }

    private static Compilation Parse(string source, bool allowParserErrors = false)
        => allowParserErrors ? CompilationTestHelper.Parse(source) : CompilationTestHelper.ParseSuccess(source);

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
