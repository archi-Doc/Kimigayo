// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstraintBindingTest
{
    private const string LateClause = """
        contract Z
            func z(self: ref/Self) -> i32
        struct S
            i32 is Z
            public init() => ()
        contract D
            func n(self: ref/Self, s: ref/S) -> i32
        struct U
            Self is D
            public init() => ()
            public func n(self: ref/Self, s: ref/S) -> i32 => 1
        func use(u: ref/U, s: ref/S) -> i32 => u.n(s)
        """;

    private const string LateClauseValid = """
        contract Z
            func z(self: ref/Self) -> i32
        struct S
            public init() => ()
        contract D
            func n(self: ref/Self, s: ref/S) -> i32
        struct U
            Self is D
            public init() => ()
            public func n(self: ref/Self, s: ref/S) -> i32 => 1
        func use(u: ref/U, s: ref/S) -> i32 => u.n(s)
        """;

    private const string LateAfterItsReaders = """
        contract D
            func n(self: ref/Self, s: ref/S) -> i32
        struct U
            Self is D
            public init() => ()
            public func n(self: ref/Self, s: ref/S) -> i32 => 1
        func use1(u: ref/U, s: ref/S) -> i32 => u.n(s)
        func f(s: ref/S) -> i32 => 1
        func use2(s: ref/S) -> i32 => f(s)
        func use3(s: ref/S) -> i32 => f(s)
        struct S
            i32 is Z
            public init() => ()
        contract Z
            func z(self: ref/Self) -> i32
        """;

    private const string LateThroughAClosedClause = """
        contract Z
            func z(self: ref/Self) -> i32
        struct X
            i32 is Z
            public init() => ()
        struct Box<T>
            public init() => ()
        contract C
            func m(self: ref/Self, b: ref/Box<X>) -> i32
        struct U
            Self is C
            public init() => ()
            public func m(self: ref/Self, b: ref/Box<X>) -> i32 => 1
        struct W
            U is C
            public init() => ()
        func use(u: ref/U, b: ref/Box<X>, w: ref/W) -> i32 => u.m(b)
        """;

    private const string LateWithUses = """
        contract Z
            func z(self: ref/Self) -> i32
        struct S
            i32 is Z
            public init() => ()
            public func get(self: ref/Self) -> i32 => 1
        contract D
            func n(self: ref/Self, s: ref/S) -> S
        struct U
            Self is D
            public init() => ()
            public func n(self: ref/Self, s: ref/S) -> S => S.init()
        func use<T>(t: ref/T, s: ref/S) -> i32
            T is D
            return t.n(s).get()
        func use2(u: ref/U, s: ref/S) -> i32
            let f: (ref/U, ref/S) -> S = U.n
            return f(u, s).get() + u.n(s).get()
        """;

    private const string LateAssociated = """
        contract Z
            func z(self: ref/Self) -> i32
        contract C
            associate Item
            func get(self: ref/Self) -> Self.Item
        struct S
            i32 is Z
            public init() => ()
        struct H
            Self is C
            associate C.Item is S
            public init() => ()
            public func get(self: ref/Self) -> S => S.init()
        func read<T>(t: ref/T) -> T.Item
            T is C
            return t.get()
        func use(h: ref/H) -> S => read(h)
        """;

    private const string BoundRequirement = """
        contract Src<X>
            associate Item is Copy
        contract P<E>: Src<E>
            func read(self: ref/Self, v: ref/Box<Self.(Src<E>).Item>) -> i32
        struct Box<T>
            T is Copy
            public init() => ()
        struct S
            Self is P<i32>
            Self is P<string>
            associate Src<i32>.Item is i32
            associate Src<string>.Item is i64
            public init() => ()
            public func read(self: ref/Self, v: ref/Box<i32>) -> i32 => 1
            public func read(self: ref/Self, v: ref/Box<i64>) -> i32 => 2
        func via<T>(t: ref/T, b: ref/Box<T.(Src<i32>).Item>) -> i32
            T is P<i32>
            return t.read(b)
        func via2<T>(t: ref/T, b: ref/Box<T.(Src<string>).Item>) -> i32
            T is P<string>
            return t.read(b)
        """;

    private const string BoundRequirementNoPremise = """
        contract P<E>
            func read(self: ref/Self, v: ref/Box<E>) -> i32
        struct Box<T>
            T is Copy
            public init() => ()
        struct S
            Self is P<i32>
            Self is P<string>
            public init() => ()
            public func read(self: ref/Self, v: ref/Box<i32>) -> i32 => 1
            public func read(self: ref/Self, v: ref/Box<string>) -> i32 => 2
        func via<T>(t: ref/T, b: ref/Box<i32>) -> i32
            T is P<i32>
            return t.read(b)
        func via2<T>(t: ref/T, b: ref/Box<string>) -> i32
            T is P<string>
            return t.read(b)
        """;

    // An owner whose clause its formation decides (PLAN G354 (2)).
    private const string OwnedHolder = "struct H<T>\n    T is Owned\n    public let t: T\n    public init(t: T) => self.t = t@move\n";
    private const string CopyHolder = "struct K<T>\n    T is Copy\n    public let t: T\n";
    private const string CallableHolder = "struct H<F>\n    F is Callable<(ref/i32) -> i32>\n    public let f: F\n    public init(f: F) => self.f = f@move\n";

    // PLAN G671's requirements, each with a clause over the Contract's parameter T, and the group its calls are written in.
    private const string Put = "contract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is T\n";
    private const string PutPair = "contract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is (T, T)\n";
    private const string Make = "contract Make<T>\n    func make<U>(u: U) -> Self\n        U is T\n";
    private const string PutShown = "contract Show<T>\n    func show(self: ref/Self) -> T\ncontract Put<T>\n    func put<U>(self: uniq/Self, u: U)\n        U is Show<T>\n";
    private const string PutGet = Put + "contract Get<T>\n    func get<U>(self: ref/Self, u: U) -> T\n        U is T\n";
    private const string Run = "group M\n    public func run";

    // The owner's O-44: a conformance reads no signature's formation, so a declaration that fails after the bodies keeps its own record and fails
    // no conformance naming it. LateAssociated's goes through the binding's own formation (R7-V5's S3). DECISIONS Q13 for the removed records.
    [Theory]
    [InlineData(LateClause, "1*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    [InlineData(LateClauseValid, "clean")]
    [InlineData(LateAfterItsReaders, "3*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    [InlineData(LateThroughAClosedClause, "1*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    [InlineData(LateWithUses, "2*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    [InlineData(LateAssociated, "2*InvalidConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    public void ALateFailureStaysAtItsDeclaration(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // PLAN G375: a requirement's call is proven under its reference, not as its declaration, so it neither reads nor writes the kept Proven: `P<i32>`'s
    // call must not answer `P<string>`'s.
    [Theory]
    [InlineData(BoundRequirement, "clean")]
    [InlineData(BoundRequirementNoPremise, "1*NoApplicableOverload_Kd 1*UnprovenConstraint_Kd 2*UnsatisfiedConstraint_Kd")]
    public void ARequirementsCallIsOutsideTheKeptProven(string source, string records) => Assert.Equal(records, DiagnosticCorpus.RecordProfile(source));

    // SPEC 8.6.4, 10.1 step 5 (PLAN G671): a requirement's clauses read the Contract's parameters at the reference the call goes through, as its
    // signature does, in the trial and in the selected call's obligation: under `C is Put<i32>`, put<U>'s `U is T` is `U is i32` (a direct,
    // explicit, structural, Type function, inherited, Contract-premise, premise-given, second-binding, nested and refined-with-arguments call).
    // The invalid twins keep dev's records, a second reference in scope included, except that the last names its failing clause (dev: `U is T`).
    [Theory]
    [InlineData(Put, "<C>(c: uniq/C)\n        C is Put<i32>\n        c.put(5)\n", "clean")]
    [InlineData(Put, "<C>(c: uniq/C)\n        C is Put<i32>\n        c.put<i32>(5)\n", "clean")]
    [InlineData(PutPair, "<C>(c: uniq/C)\n        C is Put<i32>\n        c.put((1, 2))\n", "clean")]
    [InlineData(Make, "<C>() -> C\n        C is Make<i32>\n        return C.make(5)\n", "clean")]
    [InlineData(Put + "contract Child: Put<i32>\n", "<C>(c: uniq/C)\n        C is Child\n        c.put(5)\n", "clean")]
    [InlineData(PutShown, "<C, W>(c: uniq/C, w: W)\n        C is Put<i32>\n        W is Show<i32>\n        c.put(w@move)\n", "clean")]
    [InlineData(Put, "<C, V>(c: uniq/C)\n        C is Put<V>\n        V is i32\n        c.put(5)\n", "clean")]
    [InlineData(Put, "<C>(c: uniq/C)\n        C is Put<i32>\n        C is Put<bool>\n        let x: i32 = 5\n        c.put(x)\n", "clean")]
    // P1i asserts dev's record kind, which is PLAN G680's open question, not a SPEC reading; it moves under DECISIONS Q13 when G680 lands.
    [InlineData(Put, "<C>(c: uniq/C)\n        C is Put<bool>\n        c.put(5)\n", "NoApplicableOverload_Kd@c.put(5) [candidates=1]")]
    [InlineData(Put, "<C, V>(c: uniq/C, v: V)\n        C is Put<V>\n        c.put(5)\n", "UnprovenConstraint_Kd@c.put(5) [subject=i32; member=put; constraint=U is T]")]
    [InlineData(PutShown, "<C, W>(c: uniq/C, w: W)\n        C is Put<i32>\n        W is Show<bool>\n        c.put(w@move)\n", "UnprovenConstraint_Kd@c.put(w@move) [subject=W; member=put; constraint=U is Show<T>]")]
    [InlineData(Put, "<C, D, V, W>(c: uniq/C, d: uniq/D, v: V, w: W)\n        C is Put<V>\n        D is Put<W>\n        c.put(w@move)\n", "UnprovenConstraint_Kd@c.put(w@move) [subject=W; member=put; constraint=U is T]")]
    [InlineData(Put + "contract Other<T>\n    func other(self: ref/Self)\n", "<C, V, W>(c: uniq/C, x: W)\n        C is Put<V>\n        C is Other<W>\n        c.put(x@move)\n", "UnprovenConstraint_Kd@c.put(x@move) [subject=W; member=put; constraint=U is T]")]
    [InlineData(PutGet, "<C, D, V>(c: uniq/C, d: ref/D, x: V)\n        C is Put<V>\n        D is Get<V>\n        c.put(d.get(x@move))\n", "clean")]
    [InlineData(
        PutGet,
        "<C, D, V, W>(c: uniq/C, d: ref/D, x: V)\n        C is Put<V>\n        D is Get<W>\n        c.put(d.get(x@move))\n",
        "UnprovenConstraint_Kd@c.put(d.get(x@move)) [subject=W; member=put; constraint=U is T] | UnprovenConstraint_Kd@d.get(x@move) [subject=V; member=get; constraint=U is T]")]
    [InlineData(Put + "contract Child<X>: Put<(X, X)>\n", "<C>(c: uniq/C, p: (i32, i32))\n        C is Child<i32>\n        c.put(p)\n", "clean")]
    [InlineData(Put + "contract Child<X>: Put<(X, X)>\n", "<C>(c: uniq/C, p: (i32, bool))\n        C is Child<i32>\n        c.put(p)\n", "NoApplicableOverload_Kd@c.put(p) [candidates=1]")]
    [InlineData(Put, "<C>(c: uniq/C)\n        C is Put<i32>\n        C is Put<bool>\n        c.put(5)\n", "AmbiguousBinding_Kd@c.put(5) [candidates=2]")]
    [InlineData(
        "contract Pair<T>\n    func pair<U, X>(self: uniq/Self, u: U, x: X)\n        U is T\n        X is (T, U)\n",
        "<C, V, W>(c: uniq/C, a: V, p: (V, W))\n        C is Pair<V>\n        c.pair(a@move, p@move)\n",
        "UnprovenConstraint_Kd@c.pair(a@move, p@move) [subject=(V, W); member=pair; constraint=X is (T, U)]")]
    public void ARequirementsClausesReadItsContractReference(string declarations, string run, string records)
    {
        var source = declarations + Run + run;
        var output = DiagnosticCorpus.Check(source, "Library");
        var actual = output.Diagnostics.Select(x => $"{x.Code}@{source.Substring(x.Span!.Value.Start, x.Span.Value.Length)} " +
            $"[{string.Join("; ", (x.Reason ?? []).Select(static y => $"{y.Name}={y.Value}"))}]");
        Assert.Equal((records == "clean", records), (output.Accepted, output.Diagnostics.Length == 0 ? "clean" : string.Join(" | ", actual)));
    }

    // PLAN G375: a kept Proven is valid in its pass (Binding.DeclQuery), so an edit that fails the signature's Type is seen by the next pass's call.
    [Fact]
    public void NoKeptProvenCrossesAPass()
    {
        const string Valid = "contract C\n    func m(self: ref/Self) -> i32\nstruct S\n    Self is C\n    public init() => ()\n    public func m(self: ref/Self) -> i32 => 1\n" +
            "struct Box<T>\n    T is C\n    public init() => ()\nfunc take() -> Box<S>\n    return Box<S>.init()\nfunc use() -> i32\n    let b = take()\n    return 1";
        var failing = Valid.Replace("public func m(", "public func n(", StringComparison.Ordinal);
        var c = CompilationTestHelper.ParseSuccess(Valid);
        Assert.True(c.Bind().IsComplete, Describe(c));
        Edit(failing, "m", "n");
        Assert.False(c.Bind().IsComplete);
        var fresh = CompilationTestHelper.ParseSuccess(failing);
        fresh.Bind();
        Assert.Equal(fresh.Binding.Issues.Select(static x => x.Code).Order(), c.Binding.Issues.Select(static x => x.Code).Order());
        Edit(Valid, "n", "m");
        Assert.True(c.Bind().IsComplete, Describe(c));

        void Edit(string source, string removed, string added)
        {
            var old = Implementation(c, removed);
            var donor = Implementation(CompilationTestHelper.ParseSuccess(source), added);
            ParseTestHelper.Detach(donor, new CodeBlockKoto(donor.CodeContext));
            Assert.True(KotoHelper.Replace(old.Parent!, old, donor));
        }

        static FunctionKoto Implementation(Compilation compilation, string name)
            => KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Parent is StructKoto && x.Name == name);
    }

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
    // explains none while PLAN G375's sigref1 stays open (null Note).
    [Theory]
    [InlineData(OwnedHolder + "func use(x: ref/i32) -> i32\n    let h = H.init(t: x)\n    return 0", "UnprovenConstraint_Kd", "H.init(t: x)", null)] // G572 U2a (Q13): the Final record without the clause fact (U2a-2)
    [InlineData(CallableHolder + "func use(g: (ref/i32 during static) -> i32) -> i32\n    let h = H.init(f: g@move)\n    return 0", "UnprovenConstraint_Kd", "g@move", "F is Callable<(ref/i32) -> i32> compares whole contracts and is not proven: the 1st parameter requires the omitted Origin of ref/i32 outlives static; its Origin part is judged from the premises alone (SPEC 8.10, 10.7, 15.6.1)")] // G572 U2a (Q13): the Callable record at its argument (U2a-2)
    [InlineData(OwnedHolder + "func mk<U>(u: U) -> H<U>\n    return H.init(t: u@move)\nfunc use(x: ref/i32) -> i32\n    let h = mk(u: x)\n    return 0", "InvalidConstraint_Kd", "H.init(t: u@move)", null)] // G572 U2a (Q13): the call rests on mk's failed H<U>
    [InlineData(CallableHolder + "func use(g: i32) -> i32\n    let h = H.init(f: g)\n    return 0", "NoApplicableOverload_Kd", "H.init(f: g)", "init requires F is Callable<(ref/i32) -> i32>; this condition is refuted for i32")]
    [InlineData(OwnedHolder + "func id<U>(u: U) -> i32 => 0\nfunc use() -> i32\n    let f = id<H<ref/i32>>\n    return 0", "UnprovenConstraint_Kd", "id<H<ref/i32>>", "Function Item id: H requires T is Owned; this condition cannot be proven for ref/i32")]
    [InlineData("struct H<T>\n    T is Copy\n    public let t: T\nstruct K<T>\n    T is Copy\n    public let t: T\nfunc id2<U, V>(u: U, v: V) -> i32 => 0\nfunc use() -> i32\n    let f = id2<H<string>, K<Array<i32>>>\n    return 0", "UnsatisfiedConstraint_Kd", "id2<H<string>, K<Array<i32>>>", "Function Item id2: H requires T is Copy; this condition is refuted for string")]
    [InlineData(CopyHolder + "func g<U>() -> i32 => 0\nfunc use() -> i32\n    return g<K<string>>()", "NoApplicableOverload_Kd", "g<K<string>>()", "g: K requires T is Copy; this condition is refuted for string")]
    [InlineData(CopyHolder + "func id<U>(u: i32) -> i32 => 0\nfunc use() -> i32\n    let f: (i32) -> i32 = id<K<string>>\n    return 0", "UnsatisfiedConstraint_Kd", "id<K<string>>", "Function Item id: K requires T is Copy; this condition is refuted for string")]
    [InlineData(OwnedHolder + "func mk<U>(h: ref/H<U>) -> i32 => 0\nfunc use(h: ref/H<i32>) -> i32\n    return mk(h: h)", "UnprovenConstraint_Kd", "H<U>", null)] // G572 U2a (Q13): the declaration's record; the call rests on it (sigref1)
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

    // SPEC 10.1, 8.3.3 (DECISIONS: G572 U2a): a field's Type that holds with a judgment held pending does not depend on it. The unproven
    // tagOf<Entry>() is selected and its result i32 holds, so Entry is Owned is then proven from tag: i32 (entry, and entry-i-two, whose
    // unproven candidate is strictly best). Entry<T> is Owned needs T is Owned, unproven, so that obligation fails after selection. The
    // first row was MirBuildTest.ValuesBindingDidNotFormAreDerived's (Q13: a valid program rejected before, a corrected label).
    [Theory]
    [InlineData("group Tags\n    public func tagOf<T>() -> i32\n        T is Owned\n        return 0\nstruct Entry\n    var tag = Tags.tagOf<Entry>()\n", null)]
    [InlineData("group Tags\n    public func tagOf<T>() -> i32\n        T is Owned\n        return 0\n    public func tagOf<T>(extra: i32 = 0) -> i64\n        return 0\nstruct Entry\n    var tag = Tags.tagOf<Entry>()\n", null)]
    [InlineData("group Tags\n    public func tagOf<T>() -> i32\n        T is Owned\n        return 0\nstruct Entry<T>\n    var tag = Tags.tagOf<Entry<T>>()\n    public let v: T\n", "Tags.tagOf<Entry<T>>()")]
    public void AFieldTypeHoldingWithItsObligationPendingIsNoCycle(string source, string? rejectedAt)
    {
        var check = DiagnosticCorpus.Check(source + "public func main() => ()\n");
        if (rejectedAt is null)
        {
            Assert.True(check.Accepted, string.Join("; ", check.Diagnostics.Select(static x => x.Code)));
            return;
        }

        var error = Assert.Single(check.Diagnostics);
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
    }

    // SPEC 23.1.2, 14.6.2, 10.1 (DECISIONS: G572 U2a): a loop over a conditional IntoIterable whose condition no premise proves selects its
    // entry call under the rule, and that call's obligation fails after selection. The loop rests on its entry call, so the one fact is one
    // record at the loop (it was the loop's own record beside the call's).
    [Fact]
    public void ALoopRestsOnItsEntryCallsObligation()
    {
        var source = "struct Counter\n    Self is Iterator\n    associate Iterator.Item is i32\n    var n: i32 = 0\n    public func next(self: uniq/Self) -> Option<i32>\n        require self.n < 3 else => return .None\n        self.n += 1\n        return .Some(self.n)\nstruct Bag<T>\n    let value: T\n    Self is IntoIterable when T is Owned\n        associate IntoIterable.IteratorType is Counter\n        public func intoIterator(self: Self) -> Counter => Counter.init()\nfunc f<U>(b: Bag<U>) -> i32\n    var n: i32 = 0\n    for x in b@move => n += x\n    return n\n";
        var error = Assert.Single(DiagnosticCorpus.Check(source, "Library").Diagnostics);
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
    }

    // SPEC 8.5, 8.6.1, F.5 (G621): a requirement waits for the conformances by what its Types contain, except a conformance declaration,
    // which binds before they register. So a premise's Contract argument that names a declared Type's projection is resolved (k1-t7),
    // under every operator, and a qualified projection resolves in a conformance declaration too (q9: the qualifier's Contract is
    // searched). An unqualified projection over a declared Type in a conformance declaration takes its candidates from the conformance
    // table, which fills only at registration: an analysis limit, one located Unsupported_Kd (k9, u1), never a missing member. Each
    // invalid twin keeps dev's records (k2-u1n).
    [Theory]
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T is Sink<(Cursor during y).Item>\n    return 0\n", "")] // k1
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is Sink<(Cursor during y).Item>\n    return 0\n", "")] // k3
    [InlineData("struct Holder<T, V> {h}\n    origin s.at == h\n    T is Sink<(Cursor{s}).Item>\n    public let w: ref/i32 during s.at\n", "")] // k5
    [InlineData("struct Holder<T> {h}\n    T is Sink<(Cursor during h).Item>\n    public let w: ref/i32 during h\n", "")] // p2
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is not Zbox<(Cursor during y).Item>\n    return 0\n", "")] // t1
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is Zbox<(Cursor during y).Item> or Marker\n    return 0\n", "")] // t2
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T is Sink<(Cursor during y).Item> or Zbox<i32>\n    return 0\n", "")] // t3
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T is Source and Sink<(Cursor during y).Item>\n    return 0\n", "")] // t7
    [InlineData("struct Window {w}\n    Self is Sink<(Cursor during w).(Source).Item>\n    public let p: ref/i32 during w\n", "")] // q9
    [InlineData("struct Window {w}\n    Self is Sink<(Cursor during w).Item>\n    public let p: ref/i32 during w\n", "Unsupported_Kd@(Cursor during w).Item")] // k9
    [InlineData("struct W\n    Self is Sink<Src.Item>\n", "Unsupported_Kd@Src.Item")] // u1
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T is Sink<(Cursor during y).Nope>\n    return 0\n", "InvalidConstraint_Kd UnresolvedBinding_Kd@(Cursor during y).Nope")] // k2
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is Sink<(Cursor during y).Nope>\n    return 0\n", "InvalidConstraint_Kd InvalidConstraint_Kd@T.(Source).Item UnresolvedBinding_Kd@(Cursor during y).Nope")] // k4
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T is Sink<Nosuch>\n    return 0\n", "InvalidConstraint_Kd UnresolvedBinding_Kd@Nosuch")] // t6
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is not Zbox<(Cursor during y).Nope>\n    return 0\n", "InvalidConstraint_Kd InvalidConstraint_Kd@T.(Source).Item InvalidConstraint_Kd@Zbox<(Cursor during y).Nope> UnresolvedBinding_Kd@(Cursor during y).Nope")] // n1
    [InlineData("func peek<T>(value: ref/T, y: ref/i32) -> i32\n    T is Source\n    T.(Source).Item is Zbox<(Cursor during y).Nope> or Marker\n    return 0\n", "InvalidConstraint_Kd InvalidConstraint_Kd@T.(Source).Item InvalidConstraint_Kd@Zbox<(Cursor during y).Nope> UnresolvedBinding_Kd@(Cursor during y).Nope")] // n2
    [InlineData("struct Window {w}\n    Self is Sink<(Cursor during w).Nope>\n    public let p: ref/i32 during w\n", "UnresolvedBinding_Kd@(Cursor during w).Nope")] // k9n
    [InlineData("struct W\n    Self is Sink<Src.Nope>\n", "UnresolvedBinding_Kd@Src.Nope")] // u1n
    public void AProjectionInARequirementWaitsUnlessItsClauseDeclaresConformance(string declaration, string expected)
    {
        const string Prelude = "contract Source\n    associate Item\ncontract Marker\ncontract Sink<E>\nstruct Cursor {at}\n    Self is Source\n    associate Item is ref/i32 during at\nstruct Zbox<T>\nstruct Src\n    Self is Source\n    associate Item is i32\n";
        var records = ContextualCaptureTest.PublishedRecords(Prelude + declaration + "public func main() => ()\n");
        Assert.Equal(expected, string.Join(' ', records.Select(static x => x.Text is { } text && !text.Contains('\n') ? x.Code + "@" + text : x.Code)));
    }

    // G621: the limit's early reads belong to one pass (Binding.ResetPass clears them, as it clears the other early reads), so every Bind
    // of k9, a Provisional one between, reports what a fresh Compilation reports.
    [Fact]
    public void EveryBindOfTheProjectionLimitReportsWhatAFreshBindReports()
    {
        const string Source = "contract Source\n    associate Item\ncontract Sink<E>\nstruct Cursor {at}\n    Self is Source\n    associate Item is ref/i32 during at\nstruct Window {w}\n    Self is Sink<(Cursor during w).Item>\n    public let p: ref/i32 during w\npublic func main() => ()\n";
        var fresh = Records(MinimalEmissionTest.Analyze(Source));
        Assert.Contains("Unsupported_Kd", fresh);
        var c = MinimalEmissionTest.Analyze(Source);
        for (var i = 0; i < 4; i++)
        {
            if (i == 2)
            {
                c.Binding.Bind(BindingMode.Provisional);
            }

            Assert.False(c.Bind().IsComplete);
            Assert.Equal(fresh, Records(c));
        }

        static string Records(Compilation c) => string.Join(" | ", c.Binding.Issues.Select(static x => $"{x.Code}@{x.Node.Akind}").Order());
    }

    private static Compilation Parse(string source, bool allowParserErrors = false)
        => allowParserErrors ? CompilationTestHelper.Parse(source) : CompilationTestHelper.ParseSuccess(source);

    private static FunctionKoto Function(Compilation c, string name) => KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Single(x => x.Name == name);

    private static string Describe(Compilation c) => string.Join("\n", c.Binding.Issues.Select(x => $"{x.Code}: {x.Node}"));
}
