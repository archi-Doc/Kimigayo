// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintBindingTest
{
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
        var proposition = ((IsKoto)queryFunction.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(expected, c.Binding.Prove(proposition, queryFunction, [context.Parameters[0].Type.BoundType], context));
    }

    [Theory]
    [InlineData("i32", "1", true)]
    [InlineData("string", "1", false)]
    [InlineData("not string", "1", true)]
    [InlineData("i32 or string", "\"text\"", true)]
    [InlineData("i32 and string", "1", false)]
    public void CallsDischargeSubstitutedConstraints(string requirement, string argument, bool success)
    {
        var c = Parse($"func identity<T>(value: T) -> T\n    T is {requirement}\n    return value\nlet result = identity({argument})");
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
        var constraint = ((IsKoto)query.TypeConstraints[0]).BoundConstraint!;
        Assert.Equal(ConstraintProof.Unknown, c.Binding.Prove(constraint, query, [context.Parameters[0].Type.BoundType], context));
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(constraint, query, [constraint.RequiredType], context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstraintApplicabilityIsIndependentOfDeclarationOrder(bool reverse)
    {
        const string a = "func f<T>(x: T) -> i32\n    T is string\n    return 0\n";
        const string b = "func f(x: i32) -> i32 => x\n";
        var c = Parse((reverse ? b + a : a + b) + "let result = f(1)");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single();
        Assert.Empty(((FunctionKoto)call.BoundSymbol!.Declaration).GenericArguments);
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
        Assert.Equal(ConstraintProof.Error, c.Binding.Prove(((IsKoto)function.TypeConstraints[0]).BoundConstraint!, function));
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
        // The Signatures are distinct (no duplicate); the pair is rejected only by the parameter acquisition shape of SPEC 7.3.1,
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

    // SPEC 8.10: a composite clause that a caller's premises cannot prove reports the subject named by its operands.
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

    // SPEC 8.7, 10.5: a candidate refuted by a composite clause names the subject that its operands share.
    [Theory]
    [InlineData("func need<s/T>(value: ref/(s/T))\n    s is rc or arc\n    ()\nfunc outer<s/T>(value: ref/(s/T))\n    s is borrow\n    need(value)", "s is rc or arc", "s/T")]
    [InlineData("func need<T>(value: ref/T)\n    T is not Copy or Owned\n    ()\nlet x = 1\nneed(x)", "T is not Copy or Owned", "i32")]
    public void RefutedCompositeCandidatesNameTheirSubject(string source, string clause, string subject)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("NoApplicableOverload_Kd", error.Code);
        Assert.Contains($"requires {clause}; this condition is refuted for {subject}", error.Note, StringComparison.Ordinal);
    }

    // SPEC 7.4: a member function may constrain its declaring Type's parameters (a conditional member).
    [Fact]
    public void AMemberFunctionCanConstrainItsDeclaringTypeParameter()
    {
        var c = Parse("struct Box<T>\n    func f<U>(x: U)\n        T is Equatable\n        ()");
        Assert.True(c.Bind().IsComplete, string.Join(", ", c.Binding.Issues));
    }

    [Fact]
    public void CompletedConcreteConformanceAbsenceCanBeRefuted()
    {
        // SPEC 8.7: absence is Refuted only after the closed conformance judgment completes; the
        // negative requirement lives at a use, since a Type declaration cannot negate a Self clause (SPEC 8.2).
        var c = Parse("contract C\nstruct S\n    Self is C\nstruct N\nfunc absent<T>(x: T)\n    T is not C\n    ()\nfunc use() => absent(N.init())");
        Assert.True(c.Bind().IsComplete);
        var s = c.Kotonoha.RootKoto.NestedContainers.Single(x => x.Name == "S");
        Assert.Equal(ConstraintProof.Proven, c.Binding.Prove(s.ConstraintNodes[0].BoundConstraint!, s));
    }

    // SPEC 8.7, 8.4.8.4: a primitive's conformances are fixed, so its absence from any other Contract is Refuted; a negative
    // requirement holds, and a call whose only candidate needs the conformance has no applicable overload.
    [Theory]
    [InlineData("contract C\nfunc absent<T>(x: T)\n    T is not C\n    ()\nfunc use() => absent(1)", true, null)]
    [InlineData("contract C\nfunc present<T>(x: T)\n    T is C\n    ()\nfunc use() => present(1)", false, DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func present<T>(x: T)\n    T is Iterable\n    ()\nfunc use() => present(\"text\")", false, DiagnosticCode.NoApplicableOverload_Kd)]
    [InlineData("func absent<T>(x: T)\n    T is not LendingIterator\n    ()\nfunc use() => absent(true)", true, null)]
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
        Assert.Equal(ConstraintKind.Error, clause.BoundConstraint!.Kind);
        c.Kotonoha.CreateCodeContext().Parse(c.Kotonoha.RootKoto, "struct Missing");
        Assert.True(c.Bind().IsComplete, Describe(c));
        Assert.Equal(ConstraintKind.TypeIdentity, clause.BoundConstraint!.Kind);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmConstraintPassesReusePropositionsAndFactStorage()
    {
        var c = Parse("func f<T>(x: T) -> T\n    T is i32 or string\n    return x\n" + string.Join('\n', Enumerable.Range(0, 128).Select(x => $"let x{x} = f({x})")));
        for (var i = 0; i < 8; i++)
        {
            Assert.True(c.Binding.Bind(BindingMode.Final).IsComplete, Describe(c));
        }

        var proposition = ((IsKoto)Function(c, "f").TypeConstraints[0]).BoundConstraint;
        Assert.Equal(0, AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final)));
        Assert.Same(proposition, ((IsKoto)Function(c, "f").TypeConstraints[0]).BoundConstraint);
    }

    private static Compilation Parse(string source, bool allowParserErrors = false)
        => allowParserErrors ? CompilationTestHelper.Parse(source) : CompilationTestHelper.ParseSuccess(source);

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
