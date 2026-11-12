// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalConformanceBindingTest
{
    private const string BlockMemberNamesItsConformance = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S<T>
            public init() => ()
            Self is C when T is Copy
                public func m(self: ref/Self, other: ref/Box<S<T>>) -> i32 => 1
        func use(s: ref/S<i32>, b: ref/Box<S<i32>>) -> i32 => s.m(b)
        """;

    private const string MemberOutsideTheBlockLacksTheCondition = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S<T>
            Self is C when T is Copy
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S<T>>) -> i32 => 1
        """;

    private const string ConditionalMemberStatesTheCondition = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct S<T>
            Self is C when T is Copy
            public init() => ()
            public func m(self: ref/Self, other: ref/Box<S<T>>) -> i32
                T is Copy
                return 1
        """;

    private const string RefutedConditionAtAWrittenType = """
        contract C
            func m(self: ref/Self, other: ref/Box<Self>) -> i32
        struct Box<T>
            T is C
            public init() => ()
        struct N
            public init() => ()
        struct S<T>
            public init() => ()
            Self is C when T is Copy
                public func m(self: ref/Self, other: ref/Box<S<T>>) -> i32 => 1
        func use(b: ref/Box<S<N>>) -> i32 => 1
        """;

    // The owner's O-44, SPEC 8.8: a member names the conformance its signature needs under the premises it has: P in a block (8.8.1), its own
    // Constraints as a conditional member (7.5), none outside the block (8.8.2).
    [Theory]
    [InlineData(BlockMemberNamesItsConformance, "clean")]
    [InlineData(MemberOutsideTheBlockLacksTheCondition, "1*UnprovenConstraint_Kd")]
    [InlineData(ConditionalMemberStatesTheCondition, "clean")]
    [InlineData(RefutedConditionAtAWrittenType, "1*UnsatisfiedConstraint_Kd")]
    public void AConditionalImplementationsSignatureUsesItsPremises(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    [Theory]
    [InlineData("i32", ConstraintProof.Proven)]
    [InlineData("string", ConstraintProof.Refuted)]
    public void ConditionsControlEvidenceWithoutRestrictingFormation(string argument, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func f()\nstruct S<T>\n    Self is C when T is Copy\n    public func f() => ()\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var type = use.Parameters[0].Type.TypeOf()!;
        var contract = Container(c, "C").SymbolOf()!;
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
        var c = CompilationTestHelper.ParseSuccess($"contract C\nstruct S<T>\n    Self is C when T is Copy\nfunc take<T>(x: T)\n    T is C\n    ()\nfunc use(x: S<{argument}>) => take(x@move)");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Fact]
    public void TheSameGenericTypeUsesTheCallersProofEnvironment()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\nstruct S<T>\n    Self is C when T is Copy\nfunc yes<T>(x: S<T>)\n    T is Copy\n    ()\nfunc unknown<T>(x: S<T>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var contract = Container(c, "C").SymbolOf()!;
        foreach (var name in new[] { "yes", "unknown", "yes", "unknown" })
        {
            var use = Function(c, name);
            Assert.Equal(name == "yes" ? ConstraintProof.Proven : ConstraintProof.Unknown, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, contract, use, out _));
        }
    }

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): a requirement is compared in its conformance scope, whose Semantics premises (D and P) give
    // the admitted set: a `when` that leaves only covariant Semantics makes the pair layer covariant in its target, so the written input
    // accepts the required one (rejected before G801); an invariant Semantics left admitted keeps the target invariant, and covariance
    // never reverses the fit.
    [Theory]
    [InlineData("ref or uniq", " when s is ref", "static", "a", "clean")]
    [InlineData("ref", "", "static", "a", "clean")]
    [InlineData("ref or uniq", "", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", " when s is uniq", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", " when s is ref", "a", "static", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementIsComparedUnderItsConformanceScopesAdmittedSet(string admitted, string condition, string required, string written, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is {admitted}\n    Self is M<s/(ref/i32 during {required}) during a>{condition}\n" +
            $"    var store: s/(ref/i32 during {written}) during a\n" +
            $"    public func put(self: uniq/Self, value: s/(ref/i32 during {written}) during a) -> () => self.store = value@move\n"));

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): a property setter's requirement is compared in the same conformance scope: under
    // `when s is ref` the written setter accepts the required input (rejected before G801); without the `when` it stays invariant.
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementsSetterIsComparedUnderItsConformanceScopesAdmittedSet(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is L<s/(ref/i32 during a) during a, s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n    public computed item: ref/(s/(ref/i32 during a) during a)\n" +
            "        get() -> ref/(s/(ref/i32 during a) during a) => self.store@ref\n" +
            "        set(value: s/(ref/i32 during a) during a) -> () => self.store = value@move\n"));

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): a requirement's own pair binder, and the implementation's own, are not in scope of the
    // conformance scope's chain; each is read at its own signature's clauses, as before G801 (these rows keep that answer). `ref` on
    // both makes the target covariant; an invariant Semantics on both, or no clause on the requirement, keeps it invariant.
    [Theory]
    [InlineData("        t is ref\n", "        t is ref\n", "static", "clean")]
    [InlineData("        t is ref\n", "        t is ref\n", "r", "clean")]
    [InlineData("        t is ref or uniq\n", "        t is ref or uniq\n", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("", "        t is ref\n", "static", "1*IncompatibleContractImplementation_Kd 1*UnprovenConstraint_Kd")]
    public void ARequirementsOwnBinderIsReadAtItsSignaturesClauses(string requirementClause, string implementationClause, string required, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, r: ref/i32, value: t/(ref/i32 during {required}) during r) -> i32\n{requirementClause}" +
            "struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, r: ref/i32, value: t/(ref/i32 during r) during r) -> i32\n" +
            $"{implementationClause}        return 0\n"));

    // SPEC 8.1.2, 15.3.7 (PLAN G801): a requirement binder that admits `owner or ref` keeps its answers in the comparison, as before G801:
    // a slot difference the implementation's per-call Origin solves is accepted, and one that needs the required `p` to outlive the
    // required `q` is rejected. Neither shows the slot's case condition (its bit): that has no acceptance witness yet (PLAN G809).
    [Theory]
    [InlineData("y: t/i32 during p) -> i32", "y: t/i32 during q) -> i32", "return 0", "clean")]
    [InlineData("y: t/i32 during p) -> t/i32 during q", "y: t/i32 during p) -> t/i32 during p", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementBindersConditionalSlotKeepsItsAnswerInTheComparison(string required, string written, string body, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, {required}\n        t is owner or ref\n" +
            $"struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, {written}\n        t is owner or ref\n        {body}\n"));

    // SPEC 8.1.2, 8.8.2 (PLAN G801): a pair layer's outer-Origin slot is active only in the borrow cases the conformance scope admits:
    // under `when s is owner` the slots' difference is no relation (rejected before G801).
    [Theory]
    [InlineData(" when s is owner", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementsPairSlotIsActiveOnlyInItsConformanceScopesBorrowCases(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            $"struct H<s/T> {{a, b}}\n    s is owner or ref\n    Self is M<s/i32 during b>{condition}\n" +
            "    var store: s/i32 during a\n    var other: s/i32 during b\n" +
            "    public func put(self: uniq/Self, value: s/i32 during a) -> () => self.store = value@move\n"));

    // SPEC 7.5, 8.3.4, 15.3.5 (PLAN G801): a fit in a conditional member's body reads the admitted set at its use, the member's clauses
    // and an enclosing `when` included: under `s is ref` the longer target fits the stored one, directly or through a local (rejected
    // before G801); without the clause, or under `s is uniq`, the target stays invariant.
    [Theory]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        self.store = value@move\n", "clean")]
    [InlineData("    Self is K when s is ref\n        public func touch(self: ref/Self) -> i32\n            return 0\n        public func put(self: uniq/Self, value: V) -> ()\n            self.store = value@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        let held: s/(ref/i32 during a) during a = value@move\n        self.store = held@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is uniq\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        let held: s/(ref/i32 during a) during a = value@move\n        self.store = held@move\n", "1*UnprovenOriginRelation_Kd")]
    public void AFitInAConditionalMembersBodyReadsTheAdmittedSetAtItsUse(string member, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract K\n    func touch(self: ref/Self) -> i32\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n    var store: s/(ref/i32 during a) during a\n" +
            member.Replace("V", "s/(ref/i32 during static) during a")));

    [Fact]
    public void StandardGetterCanUseTheConformanceCondition()
    {
        var c = CompilationTestHelper.ParseSuccess("struct Box<T>\n    Self is Copy when T is Copy\n    var value: T\ncontract C\n    associate E\n    property item: E has get\nstruct S<T>\n    Self is C when T is Copy\n    associate C.E is Box<T>\n    public var item: Box<T>\nfunc use(x: S<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "C").SymbolOf()!, use, out var path));
        Assert.Equal(PropertyWitnessKind.StorageCopy, Assert.Single(path!.PropertyWitnesses).Kind);
    }

    [Theory]
    [InlineData("public func f(x: T) -> T => copy(x)")]
    [InlineData("public var item: T\n        get(self: ref/Self) -> T => storage")]
    public void ConditionsDoNotLeakIntoOutsideMemberDeclarationsOrBodies(string member)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\nfunc copy<U>(x: U) -> U\n    U is Copy\n    return x\nstruct S<T>\n    Self is C when T is Copy\n    {member}");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildPathDoesNotRequireTheDirectParentsCondition(bool reversed)
    {
        var parent = "    Self is Parent when T is Copy\n";
        var child = "    Self is Child when T is Owned\n";
        var c = CompilationTestHelper.ParseSuccess("contract Parent\n    func f()\ncontract Child: Parent\nstruct S<T>\n" + (reversed ? child + parent : parent + child) + "    public func f() => ()\nfunc use(x: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "Parent").SymbolOf()!, use, out var path));
        Assert.Equal("Child", path!.RootContract.Name);
        Assert.Equal(2, c.Binding.GetConformanceDefinition(use.Parameters[0].Type.TypeOf()!, Container(c, "Parent").SymbolOf()!)!.Paths.Count);
    }

    [Theory]
    [InlineData("T is Copy", "T is Owned")]
    [InlineData("T is i32", "T is string")]
    public void DuplicateDirectDeclarationsAreRejectedRegardlessOfConditions(string a, string b)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\nstruct S<T>\n    Self is C when {a}\n    Self is C when {b}");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void DifferentInheritedAssociatedBindingsAreRejectedAtDefinition()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate E\ncontract B: A\n    Self.(A).E is i32\ncontract C: A\n    Self.(A).E is string\nstruct S<T>\n    Self is B when T is Copy\n    Self is C when T is Owned");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.IncompatibleContractImplementation_Kd);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void ConjoinedDirectContractsRetainDistinctRootEvidence(string second, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract A\n    associate E\ncontract B: A\n    Self.(A).E is i32\ncontract C: A\n    Self.(A).E is {second}\nstruct S\n    Self is B and C");
        Assert.Equal(valid, c.Bind().IsComplete);
        var identity = c.Binding.GetConformanceDefinition(Container(c, "S").TypeOf()!, Container(c, "A").SymbolOf()!)!;
        Assert.Equal(2, identity.Paths.Count);
        Assert.NotSame(identity.Paths[0].RootContract, identity.Paths[1].RootContract);
    }

    [Theory]
    [InlineData("struct")]
    [InlineData("enum")]
    public void InvalidConditionsAreCheckedEvenWithoutUses(string kind)
    {
        var source = $"contract C\n{kind} S<T>\n    Self is C when T is Missing" + (kind == "enum" ? "\n    None" : string.Empty);
        var c = CompilationTestHelper.ParseSuccess(source, "ConditionalPremise.kimi");
        Assert.False(c.Bind().IsComplete);
        // SPEC 23.1.2: an independent missing Name remains visible when its premise environment is invalid.
        var diagnostic = Assert.Single(CompilationTestHelper.BindingErrors(c), x => x.Code == nameof(DiagnosticCode.UnresolvedBinding_Kd));
        Assert.Equal("Missing", diagnostic.Text);
        Assert.Equal(source.IndexOf("Missing", StringComparison.Ordinal), diagnostic.Span.Start);
    }

    [Theory]
    [InlineData("i32", true)]
    [InlineData("string", false)]
    public void AssociatedProjectionRequiresAProvenConditionalPath(string argument, bool valid)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    associate E is i32\nstruct S<T>\n    Self is C when T is Copy\nfunc use(x: S<{argument}>.(C).E) -> i32 => x");
        Assert.Equal(valid, c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("T is C, T.(C).E is Copy")]
    [InlineData("T.(C).E is Copy, T is C")]
    public void ConditionsCanDependOnAssociatedProjections(string conditions)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    associate E\ncontract D\nstruct V\n    Self is C\n    associate C.E is i32\nstruct S<T>\n    Self is D when {conditions}\nfunc use(x: S<V>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "D").SymbolOf()!, use, out _));
    }

    [Fact]
    public void DirectAndUnconditionalDeclarationsAreDuplicatesAfterFragmentMerging()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\nstruct S<T>\n    Self is C\nstruct S<T>\n    Self is C when T is Copy");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Fact]
    public void APrivateChildDoesNotNarrowAConditionalParentsAccess()
    {
        var c = CompilationTestHelper.ParseSuccess("public contract A\n    func f()\ncontract B: A\npublic struct S<T>\n    Self is B when T is Copy\n    internal func f() => ()");
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
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E is C\n" + (reverse ? b + a : a + b));
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteEvidenceIsIndependentOfDeclarationOrder(bool reverse)
    {
        var a = "struct A<T>\n    Self is C when T is Copy\n    associate C.E is B<T>\n";
        var b = "struct B<T>\n    Self is C when T is Copy\n    associate C.E is Leaf\n";
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\nstruct Leaf\n" + (reverse ? b + a : a + b) + "func use(x: A<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AnInvalidAlternativeIsNotHiddenByASuccessfulPath()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\ncontract B: A\nstruct S<T>\n    Self is A when T is Copy\n    Self is B when T is Missing\nfunc use(x: S<i32>) => ()");
        Assert.False(c.Bind().IsComplete);
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Error, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "A").SymbolOf()!, use, out var path));
        Assert.Null(path);
    }

    [Fact]
    public void OrdinaryConstraintsStillRestrictFormation()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\nstruct S<T>\n    T is Copy\n    Self is C when T is Owned\nfunc use(x: S<string>) => ()");
        Assert.False(c.Bind().IsComplete);
    }

    [Fact]
    public void RequirementPremisesAndConformanceConditionsAreCombined()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f<U>(x: U)\n        U is Copy\nstruct S<T>\n    Self is C when T is Owned\n    public func f<V>(x: V)\n        V is Copy\n        ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void ConditionalCopyOptInSatisfiesAConditionalRefinementOfCopy()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C: Copy\nstruct S<T>\n    Self is Copy when T is Copy\n    Self is C when T is Copy\n    let value: T\nfunc use(x: S<i32>, y: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(ConstraintProof.Proven, c.Binding.ProveCopy(use.Parameters[0].Type.TypeOf()!, use));
        Assert.Equal(ConstraintProof.Refuted, c.Binding.ProveCopy(use.Parameters[1].Type.TypeOf()!, use));
    }

    [Fact]
    public void DuplicateConditionalCopyUsesTheSameIdentityRule()
    {
        var c = CompilationTestHelper.ParseSuccess("struct S<T>\n    Self is Copy when T is Copy\n    Self is Copy when T is Owned");
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Code == DiagnosticCode.DuplicateBinding_Kd);
    }

    [Theory]
    [InlineData("s is owner", "i32", ConstraintProof.Proven)]
    [InlineData("s is ref", "i32", ConstraintProof.Refuted)]
    [InlineData("T is i32", "i32", ConstraintProof.Proven)]
    public void ConditionsAcceptEnclosingSemanticsAndTargetProjections(string condition, string argument, ConstraintProof expected)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\nstruct S<s/T>\n    Self is C when {condition}\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(expected, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "C").SymbolOf()!, use, out _));
    }

    [Fact]
    public void TypeSubstitutionsAreComparedUnderTheOverlapConditions()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate E\ncontract B: A\n    Self.(A).E is i32\nstruct S<T>\n    Self is A when T is i32\n    Self is B when T is i32\n    associate A.E is T");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void AssociatedRequirementsUseDeclaredResultsUnderPathPremises()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\n    func read() -> E\nstruct S<T>\n    Self is C when T is Copy\n    public func read() -> i32 => 1");
        Assert.True(c.Bind().IsComplete, Describe(c));
    }

    [Fact]
    public void IdenticalTypeArgumentsDoNotShareProofBetweenConditionalAndOuterScopes()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\ncontract D\nstruct S<T>\n    Self is C when T is Copy\nstruct Outer<T>\n    Self is D when T is Copy\n    public func inspect(x: S<T>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var inspect = Function(c, "inspect");
        var type = inspect.Parameters[0].Type.TypeOf()!;
        var conditional = Container(c, "Outer").Members.OfType<SyntaxFormKoto>().Single();
        var contract = Container(c, "C").SymbolOf()!;
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(type, contract, conditional, out _));
            Assert.Equal(ConstraintProof.Unknown, c.Binding.ResolveConformance(type, contract, inspect, out _));
        }
    }

    [Fact]
    public void AConditionalTypeIdentityCanJustifyTheStorageCopyBridge()
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    associate E\n    property item: E has get\nstruct S<T>\n    Self is C when T is i32\n    associate C.E is T\n    public var item: T");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var path = Assert.Single(c.Binding.GetConformanceDefinition(Container(c, "S").TypeOf()!, Container(c, "C").SymbolOf()!)!.Paths);
        Assert.Equal(PropertyWitnessKind.StorageCopy, Assert.Single(path.PropertyWitnesses).Kind);
    }

    // SPEC 8.5.1, 8.8.2 (PLAN G695): a Contract clause naming the Contract's Self is read with the path's Self normalized in its
    // conformance scope, as the specification is, so under an identity condition `E is Option<Self>` agrees with
    // `associate K.E is Option<Self>`; Option<i32> does not. `T is Copy` is the control.
    [Theory]
    [InlineData("T is Option<i32>", "Option<Self>", "clean")]
    [InlineData("T is Copy", "Option<Self>", "clean")]
    [InlineData("T is Option<i32>", "Option<i32>", "1*InvalidAssociatedType_Kd")]
    public void AContractClauseNamingSelfIsReadUnderTheConditionsIdentity(string condition, string associated, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile("contract K\n    associate E\n    E is Option<Self>\n    func get(self: ref/Self) -> i32\nstruct S<T>\n    Self is K when " + condition + "\n        associate K.E is " + associated + "\n    public func get(self: ref/Self) -> i32 => 1\n"));

    [Theory]
    [InlineData("C")]
    [InlineData("Copy")]
    public void ChangedConditionsInvalidateEarlierSuccessfulAndFailedQueries(string contractName)
    {
        var c = CompilationTestHelper.ParseSuccess($"contract C\nstruct S<T>\n    Self is {contractName} when T is Copy\nfunc use(x: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var syntax = Container(c, "S").Members.OfType<SyntaxFormKoto>().Single();
        var original = ((IsKoto)((SyntaxFormKoto)syntax.Operands[1]).Operands[0]).Right;
        var fragment = CompilationTestHelper.ParseSuccess("struct R<T>\n    Self is Copy when T is Owned");
        var replacement = ((IsKoto)((SyntaxFormKoto)Container(fragment, "R").Members.OfType<SyntaxFormKoto>().Single().Operands[1]).Operands[0]).Right;
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
        var parent = original.Parent!;
        var use = Function(c, "use");
        var type = use.Parameters[0].Type.TypeOf()!;
        var contract = contractName == "Copy" ? c.Binding.Library.Copy : Container(c, "C").SymbolOf()!;
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
        var c = CompilationTestHelper.ParseSuccess($"contract C\n    func f()\nenum S<T>\n    Self is C when T is Copy\n    None\n    Some(T)\n    public func f() => ()\nfunc use(x: S<{argument}>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        Assert.Equal(expected, c.Binding.ResolveConformance(use.Parameters[0].Type.TypeOf()!, Container(c, "C").SymbolOf()!, use, out _));
    }

    [Fact]
    public void DisjointEvidenceMayRetainDifferentAssociatedBindings()
    {
        var c = CompilationTestHelper.ParseSuccess("contract A\n    associate E\ncontract B: A\n    Self.(A).E is i32\ncontract C: A\n    Self.(A).E is string\nstruct S<T>\n    Self is B when T is i32\n    Self is C when T is string\nfunc use(x: S<i32>, y: S<string>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var a = Container(c, "A").SymbolOf()!;
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ConstraintProof.Proven, c.Binding.ResolveConformance(use.Parameters[i].Type.TypeOf()!, a, use, out var path));
            Assert.Equal(i == 0 ? "i32" : "string", Assert.Single(path!.AssociatedTypes).Value.Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebindingInvalidatesAndRestoresEvidence(bool condition)
    {
        var c = CompilationTestHelper.ParseSuccess("contract C\n    func f()\nstruct S<T>\n    Self is C when T is Copy\n    public func f() => ()\nfunc use(x: S<i32>) => ()");
        Assert.True(c.Bind().IsComplete, Describe(c));
        var use = Function(c, "use");
        var contract = Container(c, "C").SymbolOf()!;
        var type = use.Parameters[0].Type.TypeOf()!;
        var identity = c.Binding.GetConformanceDefinition(type, contract)!;
        var path = Assert.Single(identity.Paths);
        var fragment = CompilationTestHelper.ParseSuccess("contract C\nstruct Replacement<T>\n    Self is C when T is Missing\n    private func f() => ()");
        Koto original = condition ? ((IsKoto)path.Premises!.Operands[0]).Right : Function(c, "f", implementation: true);
        Koto replacement = condition
            ? ((IsKoto)((SyntaxFormKoto)KotoTree.Walk(fragment.Kotonoha.RootKoto).OfType<SyntaxFormKoto>().First(x => x.Akind == KotoKind.ConditionalConformance && x.Parent is DeclarationContainerKoto).Operands[1]).Operands[0]).Right
            : Function(fragment, "f");
        ParseTestHelper.Detach(replacement, new CodeBlockKoto(replacement.CodeContext));
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

    [Trait("Purpose", "Allocation")]
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

        var c = CompilationTestHelper.ParseSuccess(source.ToString());
        Assert.True(c.Bind().IsComplete, Describe(c));
        var allocated = AllocationMeasurement.Measure(() => c.Binding.Bind(BindingMode.Final));
        Assert.True(c.Binding.Result.IsComplete, Describe(c));
        Assert.Equal(0, allocated);
    }

    [Theory]
    [InlineData("struct", "PrimitiveInteger", false, DiagnosticCode.ClosedContractConformance_Kd)]
    [InlineData("struct", "PrimitiveInteger", true, DiagnosticCode.ClosedContractConformance_Kd)]
    [InlineData("enum", "PrimitiveInteger", false, DiagnosticCode.ClosedContractConformance_Kd)]
    [InlineData("enum", "PrimitiveInteger", true, DiagnosticCode.ClosedContractConformance_Kd)]
    [InlineData("struct", "ObjectPayload", false, DiagnosticCode.InvalidSelfClause_Kd)]
    [InlineData("struct", "ObjectPayload", true, DiagnosticCode.InvalidSelfClause_Kd)]
    [InlineData("enum", "ObjectPayload", false, DiagnosticCode.InvalidSelfClause_Kd)]
    [InlineData("enum", "ObjectPayload", true, DiagnosticCode.InvalidSelfClause_Kd)]
    [InlineData("struct", "Sealed", false, DiagnosticCode.InvalidConstraint_Kd)]
    [InlineData("struct", "Sealed", true, DiagnosticCode.InvalidConstraint_Kd)]
    [InlineData("enum", "Sealed", false, DiagnosticCode.InvalidConstraint_Kd)]
    [InlineData("enum", "Sealed", true, DiagnosticCode.InvalidConstraint_Kd)]
    public void IntrinsicSelfClausesReportOneErrorAtTheClause(string kind, string contract, bool conditional, DiagnosticCode expected)
    {
        var clause = $"Self is {contract}";
        var condition = conditional ? " when T is Copy" : string.Empty;
        var source = $"{kind} S<T>\n    {clause}{condition}" + (kind == "enum" ? "\n    None" : string.Empty);
        var c = CompilationTestHelper.ParseSuccess(source, "IntrinsicSelf.kimi");
        Assert.False(c.Bind().IsComplete);
        var diagnostic = Assert.Single(CompilationTestHelper.BindingErrors(c));
        Assert.Equal(expected.ToString(), diagnostic.Code);
        Assert.Equal(clause, diagnostic.Text);
        Assert.Equal(source.IndexOf(clause, StringComparison.Ordinal), diagnostic.Span.Start);
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name, bool implementation) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name && x.IsRequirement != implementation);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
