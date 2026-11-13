// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConditionalConformanceBindingTest
{
    private const string BorrowCasePremise = "x: t/(ref/i32 during p) during q, ";
    private const string PinnedQ = "z: uniq/(ref/i32 during q), ";

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

    // SPEC 15.6.1, 15.6.5, 15.3.7 (PLAN G809): a bound of the comparison's solve that a pair layer's slot requires exists only in its
    // binder's borrow cases, so a premise of those cases proves it: the requirement's `x` gives `p outlives q` in the `ref` (or `uniq`)
    // case, where the result's or `y`'s slot needs it (rejected before G809). Without `x`, or when every case requires the relation (no
    // pair layer at `y`), it stays unproven.
    [Theory]
    [InlineData(BorrowCasePremise + "y: t/i32 during p) -> t/i32 during q", BorrowCasePremise + "y: t/i32 during p) -> t/i32 during p", "return y@move", "clean")]
    [InlineData(PinnedQ + BorrowCasePremise + "y: t/i32 during p) -> i32", PinnedQ + BorrowCasePremise + "y: t/i32 during q) -> i32", "return 0", "clean")]
    [InlineData(PinnedQ + BorrowCasePremise + "y: t/i32 during p) -> i32", PinnedQ + BorrowCasePremise + "y: t/i32 during q) -> i32", "return 0", "clean", "uniq")]
    [InlineData("y: t/i32 during p) -> t/i32 during q", "y: t/i32 during p) -> t/i32 during p", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(PinnedQ + "y: t/i32 during p) -> i32", PinnedQ + "y: t/i32 during q) -> i32", "return 0", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(PinnedQ + "y: t/i32 during p) -> i32", PinnedQ + "y: t/i32 during q) -> i32", "return 0", "1*IncompatibleContractImplementation_Kd", "uniq")]
    [InlineData(BorrowCasePremise + "y: ref/i32 during p) -> ref/i32 during q", BorrowCasePremise + "y: ref/i32 during p) -> ref/i32 during p", "return y", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(PinnedQ + BorrowCasePremise + "y: ref/i32 during p) -> i32", PinnedQ + BorrowCasePremise + "y: ref/i32 during q) -> i32", "return 0", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementBindersBorrowCasePremiseProvesTheBoundsOfThatCase(string required, string written, string body, string records, string borrow = "ref")
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, {required}\n        t is owner or {borrow}\n" +
            $"struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, p: ref/i32, q: ref/i32, {written}\n        t is owner or {borrow}\n        {body}\n"));

    // SPEC 15.3.6 (PLAN G809, the owner's O-233 (C)): the written `a`'s candidate is the meet of its unconditional upper bound `u` and of
    // each conditional bound (`l1` in `t`'s borrow cases, `l2` in `s`'s) not proven, in its cases, to outlive `u`; the floor `f` (`s`'s
    // borrow cases) then needs `l1 outlives f` there, which only `v2` gives. The verdict does not depend on the order of `b` and `c`.
    [Theory]
    [InlineData("", false, "1*IncompatibleContractImplementation_Kd")]
    [InlineData("", true, "1*IncompatibleContractImplementation_Kd")]
    [InlineData("v2: s/(ref/i32 during l1) during f, ", false, "clean")]
    [InlineData("v2: s/(ref/i32 during l1) during f, ", true, "clean")]
    public void AConditionalUpperBoundIsJudgedAgainstTheUnconditionalMeet(string premise, bool swapped, string records)
    {
        var premises = "u: ref/i32, l1: ref/i32, l2: ref/i32, f: ref/i32, w: t/(ref/i32 during l1) during l2, " +
            "x2: s/(ref/i32 during l2) during f, y2: ref/(ref/i32 during u) during f, ";
        var required = swapped ? "c: s/i32 during l2, b: t/i32 during l1" : "b: t/i32 during l1, c: s/i32 during l2";
        var written = swapped ? "c: s/i32 during a, b: t/i32 during a" : "b: t/i32 during a, c: s/i32 during a";
        Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U, s/W>(self: uniq/Self, {premises}{premise}a: ref/i32 during u, {required}) -> s/i32 during f\n" +
            "        t is owner or ref\n        s is owner or ref\n" +
            $"struct H\n    Self is M\n    public func put<t/U, s/W>(self: uniq/Self, {premises}{premise}a: ref/i32, {written}) -> s/i32 during a\n" +
            "        t is owner or ref\n        s is owner or ref\n        return c@move\n"));
    }

    // SPEC 15.3.6, 15.6.5 (PLAN G809, the owner's O-233 (C)): the bound `l outlives a`, required by `b` in `t`'s borrow cases and by `c`
    // in `s`'s (or `t`'s), exists in the union of those cases, not in every case, so it is no limit of the every-case meet `u`: neither
    // `l` nor `l2` (`t`'s) is proven there to outlive `u`, both join the candidate, and the every-case floor `f` needs `l2 outlives f`,
    // which no premise gives. Rejected for both binders of `c` (accepted before by a merge that read "every case").
    [Theory]
    [InlineData("s")]
    [InlineData("t")]
    public void ABoundRequiredUnderTwoBindersExistsInTheUnionOfTheirCases(string binder)
    {
        var inputs = "u: ref/i32, l: ref/i32, l2: ref/i32, f: ref/i32, w: t/(ref/i32 during l2) during l, " +
            "y2: ref/(ref/i32 during u) during f, x3: ref/(ref/i32 during l) during f, ";
        Assert.Equal("1*IncompatibleContractImplementation_Kd", DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U, s/W>(self: uniq/Self, {inputs}a: ref/i32 during u, b: t/i32 during l, c: {binder}/i32 during l, " +
            "d: t/i32 during l2) -> ref/i32 during f\n        t is owner or ref\n        s is owner or ref\n" +
            $"struct H\n    Self is M\n    public func put<t/U, s/W>(self: uniq/Self, {inputs}a: ref/i32, b: t/i32 during a, c: {binder}/i32 during a, " +
            "d: t/i32 during a) -> ref/i32 during a\n        t is owner or ref\n        s is owner or ref\n        return a\n"));
    }

    // SPEC 15.3.6, 15.6.5 (PLAN G809, the owner's O-233 (C)): the bound `l outlives a`, required by `b` in `t`'s borrow cases and by `c`
    // in `s`'s, is proven in each set of cases from that set's premises: `w` gives `l outlives u` in `t`'s and `x2` in `s`'s, so `l` does
    // not join the every-case meet `u`, and the floor `f` needs only `u outlives f` (`y2`; rejected before G809). Without `x2` nothing
    // proves it in `s`'s cases: `l` joins, and `f` needs `l outlives f`, which no premise gives.
    [Theory]
    [InlineData("x2: s/(ref/i32 during l) during u, ", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void ABoundRequiredInSeveralCaseSetsIsProvenInEachFromItsPremises(string premise, string records)
    {
        var inputs = $"u: ref/i32, l: ref/i32, f: ref/i32, w: t/(ref/i32 during l) during u, {premise}y2: ref/(ref/i32 during u) during f, ";
        Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U, s/W>(self: uniq/Self, {inputs}a: ref/i32 during u, b: t/i32 during l, c: s/i32 during l) -> ref/i32 during f\n" +
            "        t is owner or ref\n        s is owner or ref\n" +
            $"struct H\n    Self is M\n    public func put<t/U, s/W>(self: uniq/Self, {inputs}a: ref/i32, b: t/i32 during a, c: s/i32 during a) -> ref/i32 during a\n" +
            "        t is owner or ref\n        s is owner or ref\n        return a\n"));
    }

    // SPEC 15.3.6 (PLAN G809, the owner's O-233 (C)): with no upper bound in every case, every conditional upper bound joins the
    // candidate: the written covariant `a` is `l` (`b`, `t`'s borrow cases), and the floor `f` (the result, `t`'s borrow cases) holds by
    // `w` there (rejected before G809). Without `w` nothing proves `l outlives f`.
    [Theory]
    [InlineData("w: t/(ref/i32 during l) during f, ", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void WithoutAnEveryCaseUpperBoundEveryConditionalOneJoins(string premise, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, l: ref/i32, f: ref/i32, {premise}b: t/i32 during l) -> t/i32 during f\n        t is owner or ref\n" +
            $"struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, l: ref/i32, f: ref/i32, {premise}b: t/i32 during a) -> t/i32 during a\n" +
            "        t is owner or ref\n        return b@move\n"));

    // SPEC 15.3.6 (PLAN G809): the written `a` sits at `b`'s covariant pair slot and at `g`'s contravariant Function Type input, so it
    // takes the invariant row's candidate, and no equality is proven: rejected with or without a pair layer at `b`.
    [Fact]
    public void AnOriginOfBothPolaritiesNeedsAnEqualityProof()
        => Assert.Equal("1*IncompatibleContractImplementation_Kd", DiagnosticCorpus.RecordProfile(
            "contract M\n    func put<t/U>(self: uniq/Self, l: ref/i32, f: ref/i32, w: t/(ref/i32 during l) during f, b: t/i32 during l, " +
            "g: (ref/i32 during f) -> i32) -> i32\n        t is owner or ref\n" +
            "struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, l: ref/i32, f: ref/i32, w: t/(ref/i32 during l) during f, " +
            "b: t/i32 during a, g: (ref/i32 during a) -> i32) -> i32\n        t is owner or ref\n        return 0\n"));

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
