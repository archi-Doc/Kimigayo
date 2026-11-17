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

    // PLAN G831's further forms of an implementation input's pair layer (AnImplementationInputsPairLayerIsProvenWellFormedInEachForm),
    // `{y}` the required `y2`'s Origin.
    private const string PairTargetInAField = """
        struct Box<E>
            public let item: E
            public init(item: E) => self.item = item@move
        contract M
            func put<t/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, x: t/Box<ref/i32 during j> during q2, y2: t/i32 during {y}) -> t/i32 during q2
                t is owner or ref
        struct H
            Self is M
            public func put<t/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, x: t/Box<ref/i32 during p> during q, y2: t/i32 during p) -> t/i32 during q
                t is owner or ref
                return y2@move
        """;

    private const string NestedPairLayersOfTwoBinders = """
        contract M
            func put<t/U, s/W>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, r2: ref/i32, x: t/(s/(ref/i32 during j) during q2) during r2, y2: s/i32 during {y}) -> s/i32 during q2
                t is owner or ref
                s is owner or ref
                origin k outlives r2
        struct H
            Self is M
            public func put<t/U, s/W>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, r2: ref/i32, x: t/(s/(ref/i32 during p) during q) during r, y2: s/i32 during p) -> s/i32 during q
                t is owner or ref
                s is owner or ref
                return y2@move
        """;

    private const string RenamedImplementationBinder = """
        contract M
            func put<t/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, x: t/(ref/i32 during j) during q2, y2: t/i32 during {y}) -> t/i32 during q2
                t is owner or ref
        struct H
            Self is M
            public func put<w/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, x: w/(ref/i32 during p) during q, y2: w/i32 during p) -> w/i32 during q
                w is owner or ref
                return y2@move
        """;

    // The owner's O-44, SPEC 8.8: a member names the conformance its signature needs under the premises it has: P in a block (8.8.1), its own
    // Constraints as a conditional member (7.5), none outside the block (8.8.2).
    [Theory]
    [InlineData(BlockMemberNamesItsConformance, "clean")]
    [InlineData(MemberOutsideTheBlockLacksTheCondition, "1*UnprovenConstraint_Kd")]
    [InlineData(ConditionalMemberStatesTheCondition, "clean")]
    [InlineData(RefutedConditionAtAWrittenType, "1*UnsatisfiedConstraint_Kd")]
    public void AConditionalImplementationsSignatureUsesItsPremises(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 15.3.7, 15.6.1, 15.6.5 (PLAN G831): an implementation input's pair layer is a borrow layer in its binder's borrow cases, so the
    // comparison proves its well-formedness as the definition assumes it: the solve makes `p` at most `j` and `k` and `q` the required
    // `q2`, and `p outlives q` needs `k outlives q2` in the `ref` case (accepted before G831), also below a borrow layer, when every
    // case is a borrow and for a pair target that stores the Origin in a Type argument. With `j` at `y`, the required `x` proves it.
    [Theory]
    [InlineData("x: t/(ref/i32 during j) during q2, y: t/i32 during k) -> t/i32 during q2", "x: t/(ref/i32 during p) during q, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("x: t/(ref/i32 during j) during q2, y: t/i32 during k) -> t/i32 during q2", "x: t/(ref/i32 during p) during q, y: t/i32 during p) -> t/i32 during q", "ref", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("x: t/(ref/i32 during j) during q2, y: ref/i32 during k) -> ref/i32 during q2", "x: t/(ref/i32 during p) during q, y: ref/i32 during p) -> ref/i32 during q", "ref", "return y", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("x: ref/(t/(ref/i32 during j) during q2) during r2, y: t/i32 during k) -> t/i32 during q2\n        origin k outlives r2", "x: ref/(t/(ref/i32 during p) during q) during r, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("x: t/Option<ref/i32 during j> during q2, y: t/i32 during k) -> t/i32 during q2", "x: t/Option<ref/i32 during p> during q, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("x: t/(ref/i32 during j) during q2, y: t/i32 during j) -> t/i32 during q2", "x: t/(ref/i32 during p) during q, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "clean")]
    [InlineData("x: ref/(t/(ref/i32 during j) during q2) during r2, y: t/i32 during j) -> t/i32 during q2", "x: ref/(t/(ref/i32 during p) during q) during r, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "clean")]
    [InlineData("x: t/Option<ref/i32 during j> during q2, y: t/i32 during j) -> t/i32 during q2", "x: t/Option<ref/i32 during p> during q, y: t/i32 during p) -> t/i32 during q", "owner or ref", "return y@move", "clean")]
    public void AnImplementationInputsPairLayerIsProvenWellFormedInItsBorrowCases(string required, string written, string admitted, string body, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, r2: ref/i32, {required}\n        t is {admitted}\n" +
            $"struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, j: ref/i32, k: ref/i32, q2: ref/i32, r2: ref/i32, {written}\n        t is {admitted}\n        {body}\n"));

    // SPEC 15.3.7, 15.6.1, 15.6.5 (PLAN G831): the same proof for a pair target that stores the Origin in a field, for nested pair layers
    // of two binders (the inner `s` layer's premise fails) and under a renamed implementation binder; with `j` at `y2` it holds.
    [Theory]
    [InlineData(PairTargetInAField, "k", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(PairTargetInAField, "j", "clean")]
    [InlineData(NestedPairLayersOfTwoBinders, "k", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(NestedPairLayersOfTwoBinders, "j", "clean")]
    [InlineData(RenamedImplementationBinder, "k", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(RenamedImplementationBinder, "j", "clean")]
    public void AnImplementationInputsPairLayerIsProvenWellFormedInEachForm(string source, string origin, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source.Replace("{y}", origin, StringComparison.Ordinal)));

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

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): a conformance's `when` narrows only the binder it names: `when u is ref` makes the layer
    // over `u` covariant in its target (rejected before G801); `when s is ref` leaves `u` admitting `uniq`, so its target stays invariant.
    [Theory]
    [InlineData("u", "clean")]
    [InlineData("s", "1*IncompatibleContractImplementation_Kd")]
    public void AConformanceConditionNarrowsOnlyTheBinderItNames(string named, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            "struct H<s/T, u/V> {a}\n    s is ref or uniq\n    u is ref or uniq\n" +
            $"    Self is M<u/(ref/i32 during static) during a> when {named} is ref\n" +
            "    var store: u/(ref/i32 during a) during a\n" +
            "    public func put(self: uniq/Self, value: u/(ref/i32 during a) during a) -> () => self.store = value@move\n"));

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
    // both makes the target covariant; an invariant Semantics on both, or no clause on the requirement, keeps it invariant. A signature
    // is formed under its own clauses (the owner's O-302 (a)): an implementation without the requirement's clause fails at its own
    // input Type, before the comparison.
    [Theory]
    [InlineData("        t is ref\n", "        t is ref\n", "static", "clean")]
    [InlineData("        t is ref\n", "        t is ref\n", "r", "clean")]
    [InlineData("        t is ref or uniq\n", "        t is ref or uniq\n", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("", "        t is ref\n", "static", "1*IncompatibleContractImplementation_Kd 1*UnprovenConstraint_Kd")]
    [InlineData("        t is ref\n", "", "static", "1*IncompatibleContractImplementation_Kd 1*UnprovenConstraint_Kd")]
    public void ARequirementsOwnBinderIsReadAtItsSignaturesClauses(string requirementClause, string implementationClause, string required, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M\n    func put<t/U>(self: uniq/Self, r: ref/i32, value: t/(ref/i32 during {required}) during r) -> i32\n{requirementClause}" +
            "struct H\n    Self is M\n    public func put<t/U>(self: uniq/Self, r: ref/i32, value: t/(ref/i32 during r) during r) -> i32\n" +
            $"{implementationClause}        return 0\n"));

    // SPEC 8.1.2, 8.8.2 (PLAN G801): a pair layer's outer-Origin slot is active only in the borrow cases the conformance scope admits:
    // under `when s is owner` the slots' difference is no relation (rejected before G801), inside an `Option` argument too.
    [Theory]
    [InlineData(" when s is owner", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when s is owner", "clean", "Option<", ">")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd", "Option<", ">")]
    public void ARequirementsPairSlotIsActiveOnlyInItsConformanceScopesBorrowCases(string condition, string records, string open = "", string close = "")
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            $"struct H<s/T> {{a, b}}\n    s is owner or ref\n    Self is M<{open}s/i32 during b{close}>{condition}\n" +
            $"    var store: {open}s/i32 during a{close}\n    var other: s/i32 during b\n" +
            $"    public func put(self: uniq/Self, value: {open}s/i32 during a{close}) -> () => self.store = value@move\n"));

    // SPEC 8.1.2, 8.8.2, 15.6.1, 15.3.7 (PLAN G801): an implementation input's well-formedness needs a pair layer's slot only in the
    // borrow cases the conformance scope admits: under `when s is owner` the input stores no `a`, as the layer itself or as a borrow's
    // target, so it needs no premise (rejected before G801's merge fix); without the `when` the ref case needs `p outlives a` (or
    // `a outlives p`), which no premise of the requirement gives.
    [Theory]
    [InlineData(" when s is owner", "s/(ref/i32 during static) during a", "s/(ref/i32 during p) during a", "clean")]
    [InlineData("", "s/(ref/i32 during static) during a", "s/(ref/i32 during p) during a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when s is owner", "ref/(s/(ref/i32 during static) during static) during static", "ref/(s/(ref/i32 during static) during a) during p", "clean")]
    [InlineData("", "ref/(s/(ref/i32 during static) during static) during static", "ref/(s/(ref/i32 during static) during a) during p", "1*IncompatibleContractImplementation_Kd")]
    public void AnImplementationInputsPairSlotNeedsAPremiseOnlyInTheConformanceScopesBorrowCases(string condition, string required, string written, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, p: ref/i32, value: P) -> i32\n" +
            $"struct H<s/T> {{a}}\n    s is owner or ref\n    Self is M<{required}>{condition}\n" +
            "    var store: s/(ref/i32 during static) during a\n" +
            $"    public func put(self: uniq/Self, p: ref/i32, value: {written}) -> i32\n        return 0\n"));

    // SPEC 8.1.2, 8.8.2, 15.6.1, 15.3.7 (PLAN G801): a required input's pair slot is a premise of the comparison in the borrow cases the
    // conformance scope admits: under `when s is ref` the required `w` gives `a outlives p` in every case, which the written `value`
    // needs (rejected before G801's merge fix); without the `when` the owner case has no such premise.
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void ARequiredInputsPairSlotIsAPremiseInTheConformanceScopesBorrowCases(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, p: ref/i32, w: ref/P during p, value: ref/(ref/i32 during static) during p) -> i32\n" +
            $"struct H<s/T> {{a}}\n    s is owner or ref\n    Self is M<s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during static) during a\n" +
            "    public func put(self: uniq/Self, p: ref/i32, w: ref/(s/(ref/i32 during static) during a) during p, " +
            "value: ref/(ref/i32 during a) during p) -> i32\n        return 0\n"));

    // SPEC 7.5, 8.3.4, 15.3.5 (PLAN G801): a fit in a conditional member's body reads the admitted set at its use, the member's clauses
    // and an enclosing `when` included: under `s is ref` the longer target fits the stored one, directly or through a local (rejected
    // before G801); without the clause, or under `s is uniq`, the target stays invariant; covariance never reverses the fit (the shorter
    // target into a stored longer one under `s is ref`). The clause's forms narrow alike (`not uniq`, a second clause intersecting
    // the Type's), and every position of the body reads it: a Closure, a return, a call argument, a generic caller that proves the
    // clause (one that does not fails only that proof), and a local named function, whose body reads its enclosing member's clauses
    // as it reads the enclosing `T is Copy` (SPEC 7.5, 8.2.1, 8.15.2; the Middle's C3, F's reading).
    [Theory]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        self.store = value@move\n", "clean")]
    [InlineData("    Self is K when s is ref\n        public func touch(self: ref/Self) -> i32\n            return 0\n        public func put(self: uniq/Self, value: V) -> ()\n            self.store = value@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        let held: s/(ref/i32 during a) during a = value@move\n        self.store = held@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is uniq\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        let held: s/(ref/i32 during a) during a = value@move\n        self.store = held@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    var other: V\n    public func put(self: uniq/Self, value: s/(ref/i32 during a) during a) -> ()\n        s is ref\n        self.other = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is not uniq\n        self.store = value@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is not ref\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref or owner\n        self.store = value@move\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref or uniq\n        self.store = value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        let f = func (v: V) -> s/(ref/i32 during a) during a => v@move\n        self.store = f(value@move)\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        let f = func (v: V) -> s/(ref/i32 during a) during a => v@move\n        self.store = f(value@move)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func conv(self: ref/Self, value: V) -> s/(ref/i32 during a) during a\n        s is ref\n        return value@move\n", "clean")]
    [InlineData("    public func conv(self: ref/Self, value: V) -> s/(ref/i32 during a) during a\n        return value@move\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func take(self: uniq/Self, v: s/(ref/i32 during a) during a) -> () => self.store = v@move\n    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        self.take(value@move)\n", "clean")]
    [InlineData("    public func take(self: uniq/Self, v: s/(ref/i32 during a) during a) -> () => self.store = v@move\n    public func put(self: uniq/Self, value: V) -> ()\n        self.take(value@move)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        func conv(v: V) -> s/(ref/i32 during a) during a => v@move\n        self.store = conv(value@move)\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        func conv(v: V) -> s/(ref/i32 during a) during a => v@move\n        self.store = conv(value@move)\n", "1*UnprovenOriginRelation_Kd")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        self.store = value@move\nfunc g<s/T>(h: uniq/(H<s/T> during b), v: s/(ref/i32 during static) during b) -> ()\n    s is ref\n    h.put(v@move)\n", "clean")]
    [InlineData("    public func put(self: uniq/Self, value: V) -> ()\n        s is ref\n        self.store = value@move\nfunc g<s/T>(h: uniq/(H<s/T> during b), v: s/(ref/i32 during static) during b) -> ()\n    s is ref or uniq\n    h.put(v@move)\n", "1*UnprovenConstraint_Kd")]
    public void AFitInAConditionalMembersBodyReadsTheAdmittedSetAtItsUse(string member, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract K\n    func touch(self: ref/Self) -> i32\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n    var store: s/(ref/i32 during a) during a\n" +
            member.Replace("V", "s/(ref/i32 during static) during a")));

    // SPEC 11.9.1, 11.9.2, 8.8.2, 15.3.5 (PLAN G801, the Middle's C2): a stored Field's `set` bridge is the requirement's comparison
    // too: the required input fits the Field's Type in the conformance scope, whose Semantics premises give the admitted set. A `when`
    // (or the struct's own clause) that leaves only `ref` makes the pair layer covariant; `uniq` left admitted keeps its target
    // invariant (accepted before this row's fix: the bridge compared without the scope); covariance never reverses the fit. A `when`
    // naming `owner` leaves the plain borrows (SPEC 8.1.2; the Middle's s-owner-v); without it `uniq` is admitted (s-owner-nowhen-i,
    // accepted before).
    [Theory]
    [InlineData("ref or uniq", " when s is ref", "static", "a", "clean")]
    [InlineData("ref or uniq", "", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", " when s is uniq", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", " when s is ref", "a", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref", "", "static", "a", "clean")]
    [InlineData("ref", "", "a", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", "", "a", "a", "clean")]
    [InlineData("ref or uniq or owner", " when s is owner", "static", "a", "clean")]
    [InlineData("ref or uniq or owner", "", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsSetBridgeIsComparedUnderItsConformanceScopesAdmittedSet(string admitted, string condition, string input, string field, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is {admitted}\n" +
            $"    Self is L<s/(ref/i32 during {field}) during a, s/(ref/i32 during {input}) during a>{condition}\n" +
            $"    public var item: s/(ref/i32 during {field}) during a\n"));

    // SPEC 11.9.1, 11.9.2, 8.8.2, 15.2 (PLAN G801, the Middle's C2): the `set` bridge's fit is proven from the conformance scope's
    // premises: the Type's own `origin b outlives a` lets the required input `during b` fit the Field `during a` (rejected before:
    // the bridge compared without premises); without it, or with the reverse relation, the fit is unproven.
    [Theory]
    [InlineData("    origin b outlives a\n", "b", "clean")]
    [InlineData("", "b", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("    origin a outlives b\n", "b", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("", "static", "clean")]
    [InlineData("", "a", "clean")]
    public void AStoredFieldsSetBridgeIsProvenFromItsConformanceScopesPremises(string premise, string input, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            $"struct H {{a, b}}\n{premise}    Self is L<ref/i32 during a, ref/i32 during {input}>\n" +
            "    public var item: ref/i32 during a\n    var other: ref/i32 during b\n"));

    // SPEC 11.9.1, 11.9.2, 8.8.2, 15.3.5 (PLAN G801, the Middle's C2): the Copy bridge compares the Field's Type with the required
    // result in the same conformance scope: its premises prove the fit (rejected before), and its admitted set decides the pair
    // layer's variance and Copy; the reverse fit, and a layer whose binder still admits `uniq`, stay rejected.
    [Theory]
    [InlineData("struct H {a, b}\n    origin b outlives a\n", "", "ref/i32 during a", "ref/i32 during b", "clean")]
    [InlineData("struct H {a, b}\n", "", "ref/i32 during a", "ref/i32 during b", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("struct H {a, b}\n    origin a outlives b\n", "", "ref/i32 during a", "ref/i32 during b", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("struct H<s/T> {a}\n    s is ref\n", "", "s/(ref/i32 during a) during a", "s/(ref/i32 during static) during a", "clean")]
    [InlineData("struct H<s/T> {a}\n    s is ref or uniq\n", " when s is ref", "s/(ref/i32 during a) during a", "s/(ref/i32 during static) during a", "clean")]
    [InlineData("struct H<s/T> {a}\n    s is ref\n", "", "s/(ref/i32 during static) during a", "s/(ref/i32 during a) during a", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("struct H<s/T> {a}\n    s is ref or uniq\n", "", "s/(ref/i32 during a) during a", "s/(ref/i32 during static) during a", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsCopyBridgeIsComparedInItsConformanceScope(string header, string condition, string result, string field, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract R<G>\n    property item: G has get\n" +
            $"{header}    Self is R<{result}>{condition}\n    public var item: {field}\n    var other: ref/i32 during a\n"));

    // SPEC 11.9.2, 8.3.4, 8.8.2, 15.3.5 (PLAN G801; the Front review's R8, R9): the `set` bridge reads each binder's admitted set at the
    // conformance scope: a `when` on another binder leaves the Field's own binder admitting `uniq` (accepted before C2).
    [Theory]
    [InlineData("u", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("s", "clean")]
    public void AStoredFieldsBridgeConditionNarrowsOnlyTheBinderItNames(string named, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "struct H<s/T, u/U> {a}\n    s is ref or uniq\n    u is ref or uniq\n" +
            $"    Self is L<s/(ref/i32 during a) during a, s/(ref/i32 during static) during a> when {named} is ref\n" +
            "    public var item: s/(ref/i32 during a) during a\n    var w: u/(ref/i32 during a) during a\n"));

    // SPEC 11.9.2, 8.3.4, 8.8.2, 15.3.5 (PLAN G801; the Middle's s-nest-both, s-nest-s, s-nest-u, the Front review's R10-R12): under
    // nested pair binders the `set` bridge's innermost target is covariant only when the `when` narrows both (one `when` alone was
    // accepted before C2).
    [Theory]
    [InlineData(" when s is ref, u is ref", "clean")]
    [InlineData(" when s is ref", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when u is ref", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsSetBridgeReadsEachNestedBindersAdmittedSet(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "struct H<s/T, u/V> {a}\n    s is ref or uniq\n    u is ref or uniq\n" +
            "    Self is L<s/(u/(ref/i32 during a) during a) during a, s/(u/(ref/i32 during static) during a) during a>" + condition +
            "\n    public var item: s/(u/(ref/i32 during a) during a) during a\n"));

    // SPEC 11.9.2, 8.3.4, 8.8.2, 15.3.5 (PLAN G801; the Middle's k-nest-both, k-nest-s, k-nest-u): the Copy bridge under nested pair
    // binders alike (`when s is ref` alone was accepted before C2; under `when u is ref` the fit fails before the Copy proof, so
    // the conformance's record is the fit's, where it was `UnprovenConstraint_Kd`).
    [Theory]
    [InlineData(" when s is ref, u is ref", "clean")]
    [InlineData(" when s is ref", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when u is ref", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsCopyBridgeReadsEachNestedBindersAdmittedSet(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract R<G>\n    property item: G has get\n" +
            "struct H<s/T, u/V> {a}\n    s is ref or uniq\n    u is ref or uniq\n" +
            $"    Self is R<s/(u/(ref/i32 during a) during a) during a>{condition}\n" +
            "    public var item: s/(u/(ref/i32 during static) during a) during a\n"));

    // SPEC 11.9.2, 3.8, 8.8.2, 15.3.5 (PLAN G801; the Middle's s-fn-v, s-fn-i): a Function Type through the `set` bridge reverses the
    // fit under the same admitted set: the required `(A) -> ()` fits the Field's `(S) -> ()` only while the layer is covariant
    // (accepted without the `when` before C2).
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsSetBridgeReversesAFunctionTypesInput(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n" +
            $"    Self is L<(s/(ref/i32 during static) during a) -> (), (s/(ref/i32 during a) during a) -> ()>{condition}\n" +
            "    public var item: (s/(ref/i32 during static) during a) -> ()\n"));

    // SPEC 11.9.2, 8.1.2, 8.8.2, 15.3.5 (PLAN G801; the Middle's k-owner-v, k-uniq-i): the Copy bridge reads the case its `when`
    // names: `owner` leaves the plain `ref/i32`, Copy and covariant; `uniq` is invariant and not Copy.
    [Theory]
    [InlineData("ref or uniq or owner", " when s is owner", "clean")]
    [InlineData("ref or uniq", " when s is uniq", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsCopyBridgeReadsItsConditionsCase(string admitted, string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract R<G>\n    property item: G has get\n" +
            $"struct H<s/T> {{a}}\n    s is {admitted}\n    Self is R<s/(ref/i32 during a) during a>{condition}\n" +
            "    public var item: s/(ref/i32 during static) during a\n"));

    // SPEC 11.9.2, 8.8.2, 15.2 (PLAN G801; the Middle's MR5: k-eq-when, k-eq-nowhen): where the conformance scope's `origin a == b`
    // proves the fit in any variance (rejected before C2: the bridge compared without premises), the Copy proof decides: `when s is
    // ref` proves it; without the `when` Copy is unproven, so the conformance's one record is `UnprovenConstraint_Kd` at the same
    // location (the move opposite to the invariant rows', whose fit now fails first).
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*UnprovenConstraint_Kd")]
    public void ACopyBridgeWhoseFitHoldsIsDecidedByItsCopyProof(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract R<G>\n    property item: G has get\n" +
            "struct H<s/T> {a, b}\n    s is ref or uniq\n    origin a == b\n" +
            $"    Self is R<s/(ref/i32 during a) during a>{condition}\n" +
            "    public var item: s/(ref/i32 during b) during a\n    var other: ref/i32 during b\n"));

    // SPEC 11.6, 11.9.2, 15.3.7, 15.6.1 (PLAN G801; the Middle's MR4: k-self-v, k-selfset-i): a required getter's result elided to
    // the receiver needs the Field's `a` to outlive the receiver's Origin, which the receiver's own well-formedness gives; the bridge's
    // comparison assumes it as every requirement comparison does (rejected before C2). A `set` input's independent per-call Origin has
    // no such premise into the Field.
    [Theory]
    [InlineData("get", "clean")]
    [InlineData("get, set", "1*IncompatibleContractImplementation_Kd")]
    public void AStoredFieldsBridgeAssumesTheRequiredReceiversWellFormedness(string accessors, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract V\n    property item: ref/i32 has {accessors}\n" +
            "struct H {a}\n    Self is V\n    public var item: ref/i32 during a\n"));

    // SPEC 3.3.2, 15.6.1, 15.3.7, 23.1.2 (PLAN G801; the Front review's R19, R20): a conformance argument ill formed in D keeps its
    // formation record, and the comparison, which assumes the required signature's well-formedness, adds no dependent conformance
    // record: the stored Field's bridge (which added `IncompatibleContractImplementation_Kd` before C2) reports as its computed twin.
    [Theory]
    [InlineData("    public var item: ref/(ref/i32 during a) during a\n    var other: ref/i32 during b\n")]
    [InlineData("    var store: ref/(ref/i32 during a) during a\n    var other: ref/i32 during b\n    public computed item: ref/(ref/(ref/i32 during a) during a)\n        get() -> ref/(ref/(ref/i32 during a) during a) => self.store@ref\n        set(value: ref/(ref/i32 during a) during a) -> () => self.store = value\n")]
    public void AStoredFieldsBridgeAssumesTheRequiredInputsWellFormednessAsAComputedSetterDoes(string members)
        => Assert.Equal("1*UnprovenOriginRelation_Kd", DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "struct H {a, b}\n    Self is L<ref/(ref/i32 during a) during a, ref/(ref/i32 during b) during a>\n" + members));

    // SPEC 6.2.2, 11.9.2, 8.8.2 (PLAN G801; the Middle's MR7: inh-v, inh-base-i): an inherited conformance's bridge is compared under
    // the derived Type's premises: `origin y outlives x` proves the base's fit through the path (rejected before C2); without it only
    // the base clause fails, and the Base's own valid conformance keeps no record (it had one before C2).
    [Theory]
    [InlineData("    origin y outlives x\n", "clean")]
    [InlineData("", "1*InvalidTypeFormation_Kd 1*UnprovenConstraint_Kd")]
    public void AnInheritedConformancesBridgeIsComparedUnderTheDerivedTypesPremises(string premise, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "open struct Base {a, b}\n    origin b outlives a\n    Self is L<ref/i32 during a, ref/i32 during b>\n" +
            "    public var item: ref/i32 during a\n    public var other: ref/i32 during b\n" +
            "struct D {x, y} : Base{v}\n    origin v.a == x\n    origin v.b == y\n" + premise));

    // SPEC 8.8.2, 11.9.2 (PLAN G801; the Middle's MR7: g-copy-v, g-copy-i): a generic caller of a Copy-bridge conformance proves its
    // `when` at the call; a caller whose binder still admits `uniq` fails only that proof.
    [Theory]
    [InlineData("ref", "clean")]
    [InlineData("ref or uniq", "1*UnprovenConstraint_Kd")]
    public void AGenericCallerOfACopyBridgeConformanceProvesItsCondition(string clause, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract R<G>\n    property item: G has get\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n    Self is R<s/(ref/i32 during a) during a> when s is ref\n" +
            "    public var item: s/(ref/i32 during static) during a\n" +
            "func read<X, G>(x: ref/X) -> G\n    X is R<G>\n    return x.item\n" +
            $"func g<s/T>(h: ref/(H<s/T> during b)) -> s/(ref/i32 during b) during b\n    s is {clause}\n" +
            "    return read<H<s/T> during b, s/(ref/i32 during b) during b>(h)\n"));

    // SPEC 8.8.2, 11.9.2 (PLAN G801; the Middle's MR7: g-set-v, g-set-i): the same through a `set`-bridge conformance.
    [Theory]
    [InlineData("ref", "clean")]
    [InlineData("ref or uniq", "1*UnprovenConstraint_Kd")]
    public void AGenericCallerOfASetBridgeConformanceProvesItsCondition(string clause, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract L<G, P>\n    property item: ref/G\n        get() -> ref/G\n        set(value: P) -> ()\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n" +
            "    Self is L<s/(ref/i32 during a) during a, s/(ref/i32 during static) during a> when s is ref\n" +
            "    public var item: s/(ref/i32 during a) during a\n" +
            "func put<X, G, P>(x: uniq/X, v: P) -> ()\n    X is L<G, P>\n    x.item = v@move\n" +
            $"func g<s/T>(h: uniq/(H<s/T> during b), v: s/(ref/i32 during static) during b) -> ()\n    s is {clause}\n" +
            "    put<H<s/T> during b, s/(ref/i32 during b) during b, s/(ref/i32 during static) during b>(h, v@move)\n"));

    // SPEC 8.8.2, 15.3.7, 15.3.5 (PLAN G801): a requirement's result is compared in the same conformance scope, in the result's
    // direction (the written result fits the required one): covariant under `when s is ref`, invariant without it, never reversed.
    [Theory]
    [InlineData(" when s is ref", "a", "static", "clean")]
    [InlineData("", "a", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when s is ref", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementsResultIsComparedUnderItsConformanceScopesAdmittedSet(string condition, string required, string written, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func get(self: ref/Self) -> P\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is M<s/(ref/i32 during {required}) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            $"    public func get(self: ref/Self) -> s/(ref/i32 during {written}) during a => $abort(\"x\")\n"));

    // SPEC 8.8.2, 15.3.5, 15.3.7 (PLAN G801): a Function Type input reverses the fit under the same admitted set: the written
    // `f` takes the longer target, which the required `f`'s input fits only while the layer is covariant.
    [Theory]
    [InlineData(" when s is ref", "a", "static", "clean")]
    [InlineData("", "a", "static", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when s is ref", "static", "a", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementsFunctionTypeInputIsComparedUnderItsConformanceScopesAdmittedSet(string condition, string required, string written, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func apply(self: uniq/Self, f: (P) -> ()) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is M<s/(ref/i32 during {required}) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            $"    public func apply(self: uniq/Self, f: (s/(ref/i32 during {written}) during a) -> ()) -> () => ()\n"));

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): each of two nested pair layers reads its own binder's admitted set: the innermost target
    // is covariant only when the `when` narrows both binders.
    [Theory]
    [InlineData(" when s is ref, u is ref", "clean")]
    [InlineData(" when s is ref", "1*IncompatibleContractImplementation_Kd")]
    [InlineData(" when u is ref", "1*IncompatibleContractImplementation_Kd")]
    public void NestedPairLayersEachReadTheirBindersAdmittedSetInTheConformanceScope(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            "struct H<s/T, u/V> {a}\n    s is ref or uniq\n    u is ref or uniq\n" +
            $"    Self is M<s/(u/(ref/i32 during static) during a) during a>{condition}\n" +
            "    var store: s/(u/(ref/i32 during a) during a) during a\n" +
            "    public func put(self: uniq/Self, value: s/(u/(ref/i32 during a) during a) during a) -> () => self.store = value@move\n"));

    // SPEC 8.8.2, 8.8.3, 15.3.5 (PLAN G801): a parent Contract's requirement, reached through the child's conformance, is compared in
    // that conformance's scope.
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void AParentsRequirementIsComparedUnderTheChildConformancesScope(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\ncontract N<P>: M<P>\n    func touch(self: ref/Self) -> i32\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is N<s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            "    public func put(self: uniq/Self, value: s/(ref/i32 during a) during a) -> () => self.store = value@move\n" +
            "    public func touch(self: ref/Self) -> i32 => 0\n"));

    // SPEC 8.6.3, 8.8.2, 15.3.5 (PLAN G801): one implementation compared for two conformances reads each conformance's own scope, in
    // either declaration order: only the conformance without the `when` fails.
    [Theory]
    [InlineData("M", " when s is ref", "N", "", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("N", "", "M", " when s is ref", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("M", " when s is ref", "N", " when s is ref", "clean")]
    public void OneImplementationIsComparedInEachConformancesOwnScope(string first, string firstCondition, string second, string secondCondition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\ncontract N<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n" +
            $"    Self is {first}<s/(ref/i32 during static) during a>{firstCondition}\n" +
            $"    Self is {second}<s/(ref/i32 during static) during a>{secondCondition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            "    public func put(self: uniq/Self, value: s/(ref/i32 during a) during a) -> () => self.store = value@move\n"));

    // SPEC 8.8.1, 8.8.2, 7.5, 15.3.5 (PLAN G801): a conditional block's member has the block's condition as a premise of its
    // comparison and of its body; the same body outside the block keeps its own signature (D only), so its fit stays invariant.
    [Theory]
    [InlineData("        ", "a", "clean")]
    [InlineData("        ", "static", "clean")]
    [InlineData("    ", "static", "1*UnprovenOriginRelation_Kd")]
    public void AConditionalBlocksMemberReadsTheBlocksConditionAndAnOutsideMemberDoesNot(string indent, string written, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            "struct H<s/T> {a}\n    s is ref or uniq\n    var store: s/(ref/i32 during a) during a\n" +
            "    Self is M<s/(ref/i32 during static) during a> when s is ref\n" +
            $"{indent}public func put(self: uniq/Self, value: s/(ref/i32 during {written}) during a) -> () => self.store = value@move\n"));

    // SPEC 8.6.3, 8.8.2, 7.5 (PLAN G801): an implementation's own clause `s is ref` is proven by the conformance's `when`; without the
    // `when` the conformance scope leaves `uniq` admitted and the comparison fails.
    [Theory]
    [InlineData(" when s is ref", "clean")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd")]
    public void AnImplementationsOwnClauseIsProvenByTheConformancesCondition(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is M<s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            "    public func put(self: uniq/Self, value: s/(ref/i32 during a) during a) -> ()\n        s is ref\n        self.store = value@move\n"));

    // SPEC 8.3.4, 8.8.2, 15.3.5 (PLAN G801): a requirement's own binder `t` over the conforming Type's `s` layer: `t` is read at its
    // signature's clauses and `s` at the conformance scope, so the fit needs both covariant.
    [Theory]
    [InlineData("ref", " when s is ref", "clean")]
    [InlineData("ref", "", "1*IncompatibleContractImplementation_Kd")]
    [InlineData("ref or uniq", " when s is ref", "1*IncompatibleContractImplementation_Kd")]
    public void ARequirementsOwnBinderOverTheConformingTypesLayerReadsEachBinderAtItsPoint(string admitted, string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            $"contract M<P>\n    func put<t/U>(self: uniq/Self, r: ref/i32, value: t/P during r) -> i32\n        t is {admitted}\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is M<s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            "    public func put<t/U>(self: uniq/Self, r: ref/i32, value: t/(s/(ref/i32 during a) during a) during r) -> i32\n" +
            $"        t is {admitted}\n        return 0\n"));

    // SPEC 23.1.2, 15.3.5 (PLAN G801): an invalid member keeps its record beside its Type's conformance, valid or invalid.
    [Theory]
    [InlineData(" when s is ref", "1*UnprovenOriginRelation_Kd")]
    [InlineData("", "1*IncompatibleContractImplementation_Kd 1*UnprovenOriginRelation_Kd")]
    public void AnInvalidMemberKeepsItsRecordBesideItsTypesConformance(string condition, string records)
        => Assert.Equal(records, DiagnosticCorpus.RecordProfile(
            "contract M<P>\n    func put(self: uniq/Self, value: P) -> ()\n" +
            $"struct H<s/T> {{a}}\n    s is ref or uniq\n    Self is M<s/(ref/i32 during static) during a>{condition}\n" +
            "    var store: s/(ref/i32 during a) during a\n" +
            "    public func put(self: uniq/Self, value: s/(ref/i32 during a) during a) -> () => self.store = value@move\n" +
            "    public func put2(self: uniq/Self, value: s/(ref/i32 during static) during a) -> ()\n        self.store = value@move\n"));

    // SPEC 15.6.1, 15.6.5, 15.3.7 (PLAN G809): a bound of the comparison's solve that a pair layer's slot requires exists only in its
    // binder's borrow cases, so a premise of those cases proves it: the requirement's `x` gives `p outlives q` in the `ref` (or `uniq`)
    // case, where the result's or `y`'s slot needs it (rejected before G809). Without `x`, or when every case requires the relation (no
    // pair layer at `y`), it stays unproven. A slot difference the implementation's per-call Origin solves needs no premise (the first
    // row, PLAN G801's: accepted before and after both units).
    [Theory]
    [InlineData("y: t/i32 during p) -> i32", "y: t/i32 during q) -> i32", "return 0", "clean")]
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

    // PLAN G949 S1-b1 (the review's N1; DECISIONS BU-220): a conditional block's declaring path is the path its clause registered in this
    // pass. An edit making the clause's subject T keeps the conformance's key but registers it no more (SPEC 8.6.2, 8.8.2: a conditional
    // conformance's subject is Self; InvalidConstraint_Kd): the next pass leaves the block no declaring path, and its records equal a
    // cold compile's (before: the block kept the unregistered path, which the end-of-Bind sweep then pools).
    [Fact]
    public void AnEditThatUnregistersAConditionalConformanceLeavesItsBlockNoDeclaringPath()
    {
        const string Contract = "contract K\n    associate E\n    func get(self: ref/Self) -> i32\n";
        const string Declared = "struct S<T>\n    Self is K when T is Copy\n        associate K.E is i32\n    public func get(self: ref/Self) -> i32 => 1\n";
        const string Edited = "struct S<T>\n    T is K when T is Copy\n        associate K.E is i32\n    public func get(self: ref/Self) -> i32 => 1\n";
        var c = CompilationTestHelper.ParseSuccess(Contract + Declared);
        Assert.True(c.Bind().IsComplete, Describe(c));
        var syntax = Container(c, "S").Members.OfType<SyntaxFormKoto>().Single();
        Assert.NotNull(c.Binding.ConstraintScope(syntax).ConformancePath);
        var target = (IsKoto)syntax.Operands[0];
        var donor = ((IsKoto)Container(CompilationTestHelper.ParseSuccess(Edited), "S").Members.OfType<SyntaxFormKoto>().Single().Operands[0]).Left;
        ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
        Assert.True(KotoHelper.Replace(target, target.Left, donor));
        Assert.False(c.Bind().IsComplete);
        Assert.Null(c.Binding.ConstraintScope(syntax).ConformancePath);
        var cold = CompilationTestHelper.ParseSuccess(Contract + Edited);
        Assert.False(cold.Bind().IsComplete);
        Assert.Equal(Records(cold), Records(c));
    }

    private static DeclarationContainerKoto Container(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<DeclarationContainerKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static FunctionKoto Function(Compilation c, string name, bool implementation) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name && x.IsRequirement != implementation);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));

    // A bound compilation's Binding records as `Code: node`, sorted (an edited tree holds a donor's document, so its published records
    // cannot be read by path).
    private static string[] Records(Compilation c) => [.. c.Binding.Issues.Select(static x => $"{x.Code}: {x.Node}").Order(StringComparer.Ordinal)];
}
