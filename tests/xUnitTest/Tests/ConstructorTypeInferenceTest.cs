// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstructorTypeInferenceTest
{
    private const string Box = "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n";
    private const string UnknownPremise = "struct Box<T>\n    public var tag: i32 = 0\n    public init(value: T) => self.tag = 1\n    public init(value: T, extra: i32 = 0)\n        T is Equatable\n        self.tag = 2\nfunc make<U>(value: U) -> i32\n    let box = ";

    // Own Origin slots: one a borrowed input names, one an optional input names (with a method), one no storage uses, one invariant.
    private const string Held = "struct H {a}\n    public let r: ref/i32 during a\n    public init(x: ref/i32 during a) => self.r = x\n";
    private const string Optional = "struct H2 {a}\n    public let r: Option<ref/i32 during a>\n    public init(o: Option<ref/i32 during a>) => self.r = o@move\n    public func has(self: ref/Self) -> bool\n        return match self.r\n            .Some(_) => true\n            .None => false\n";
    private const string Phantom = "struct P {a}\n    public let n: i32\n    public init() => self.n = 1\n    public func get(self: ref/Self) -> i32 => self.n\n";
    private const string Invariant = "struct U {a}\n    public let r: Option<uniq/(ref/i32 during a) during a>\n    public init(o: Option<uniq/(ref/i32 during a) during a>) => self.r = o@move\n";
    private const string Main = "public func main() => ()\n";

    [Theory]
    [InlineData("let n: i32 = 42\nlet box = Box.init(n)", "i32")]
    [InlineData("let box: Box<i64> = Box.init(42)", "i64")]
    [InlineData("let box = Box.init(42)", "i32")]
    [InlineData("let box = Box.init(true)", "bool")]
    [InlineData("let box = Box.init(value: 42)", "i32")]
    public void OwnTypeSlotsUseTheCommonCallInference(string use, string name)
    {
        var c = MinimalEmissionTest.Analyze(Box + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallOf()?.Target.Declaration is FunctionKoto { IsConstructor: true });
        var plan = Assert.IsType<CallPlan>(call.CallOf());
        Assert.True(plan.TypeArguments.IsEmpty);
        Assert.Equal(name, Assert.Single(plan.DeclaringType!.Components).Name);
        Assert.Same(plan.DeclaringType, call.TypeOf());
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IndependentInputsAreNotAdaptedToAnEarlierInference(bool explicitType, bool reverse)
    {
        var source = "struct Pair<T>\n    public init(first: T, second: T) => ()\nlet n: i32 = 42\nlet pair = Pair" + (explicitType ? "<i32>" : string.Empty) + ".init(" + (reverse ? "n@ref, n" : "n, n@ref") + ")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete == explicitType, MinimalEmissionTest.Describe(c));
        if (!explicitType)
        {
            Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.NoApplicableCandidate);
        }
    }

    [Fact]
    public void ExpectedResultNeverChangesIndependentInputEvidence()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let n: i32 = 42\nlet box: Box<i64> = Box.init(n)");
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.NoApplicableCandidate);
    }

    [Theory]
    [InlineData("let empty: Empty<i64> = Empty.init()", true)]
    [InlineData("let empty = Empty.init()", false)]
    public void IndependentExpectationFillsOtherwiseUnboundSlots(string use, bool valid)
    {
        var c = MinimalEmissionTest.Analyze("struct Empty<T>\n    public init() => ()\n" + use);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        if (!valid)
        {
            Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.UnboundTypeArgument);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAndInferredConstructionExecuteTheSamePlan(bool explicitType)
        => ScalarEmissionTest.EmitFixture("ConstructorTypeInference" + explicitType, Box + "let box = Box" + (explicitType ? "<i32>" : string.Empty) + ".init(42)\nrequire box.value == 42 else => $abort(\"construction\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedBindingRestoresAnInferenceRejectedCandidate(bool reverse)
    {
        const string A = "    public init(x: ref/i32, y: ref/T) => ()\n";
        const string B = "    public init(x: ref/T, y: ref/T) => ()\n";
        var source = "struct C<T>\n" + (reverse ? B + A : A + B) + "let n: i32 = 42\nlet r = n@ref\nlet c = C.init(r@ref, n@ref)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.ConstructorSelectionChanged);
        var explicitCall = MinimalEmissionTest.Analyze(source.Replace("C.init", "C<i32>.init", StringComparison.Ordinal));
        Assert.Contains(explicitCall.Binding.Issues, x => x.Failure == BindingFailure.Ambiguous);
    }

    [Theory]
    [InlineData("let box = Box.init(func (n: i32) => n + 1)")]
    [InlineData("let box = Box.init(func () => 42)")]
    public void OneWaitingConstructorKeepsItsConcreteClosure(string use)
    {
        var c = MinimalEmissionTest.Analyze(Box + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.CallOf()?.Target.Declaration is FunctionKoto { IsConstructor: true });
        Assert.Equal(BoundTypeKind.Closure, call.TypeOf()!.Components[0].Kind);
    }

    [Theory]
    [InlineData("struct A\n    public init(value: i32) => ()\nstruct A<T>\n    public init(value: T) => ()\nlet a = A.init(42)", true)]
    [InlineData("struct A<T>\n    public init(value: T) => ()\nstruct A<T, U>\n    public init(value: T) => ()\nlet a: A<i32> = A.init(42)", false)]
    [InlineData("struct Outer<A>\n    public struct Inner<B>\n        public init(value: B, other: A) => ()\nlet a = Outer<i32>.Inner.init(true, 42)", true)]
    [InlineData("struct Outer<A>\n    public struct Inner<B>\n        public init(value: B, other: A) => ()\nlet a = Outer.Inner.init(true, 42)", false)]
    [InlineData("alias H => Outer<i32>.Helpers\nstruct Outer<A>\n    public group Helpers\n        public struct Inner<B>\n            public init(value: B, other: A) => ()\nlet a = H.Inner.init(true, 42)", true)]
    [InlineData("struct A<T>\n    public init(value: T) => ()\n    public func make(value: T) -> Self => Self.init(value@move)\nlet a = A<i32>.make(42)", true)]
    public void LookupCommitsTheDeclarationAndOuterBindings(string source, bool valid)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("let a = Box.init((1, true))")]
    [InlineData("let values = [1, 2]\nlet a = Box.init(values@move)")]
    [InlineData("let a: Box<[2 of i32]> = Box.init([1, 2])")]
    [InlineData("let n: i32 = 42\nlet a = Box.init(n@ref)")]
    [InlineData("var n: i32 = 42\nlet a = Box.init(n@uniq)")]
    [InlineData("let a = Box.init(Box.init(42@i32))")]
    public void CompositeAndBorrowTypesFollowTheExistingStoragePath(string use)
    {
        var c = MinimalEmissionTest.Analyze(Box + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Generate(TextWriter.Null), MinimalEmissionTest.Describe(c));
    }

    [Theory]
    [InlineData("raw/T")]
    [InlineData("(T, T)")]
    public void ExplicitRecursiveConstructionSubstitutesOnce(string argument)
    {
        var c = MinimalEmissionTest.Analyze($"struct Grow<T>\n    public init() => ()\n    public func make() -> Grow<{argument}> => Grow<{argument}>.init()\nlet value = Grow<i32>.init()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.TypeOf()?.Components.FirstOrDefault()?.Kind is BoundTypeKind.Tuple or BoundTypeKind.Semantics);
        Assert.Equal(argument == "raw/T" ? "Grow<raw/T>" : "Grow<(T, T)>", Binding.DiagnosticTypeName(call.TypeOf()!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOriginSlotsAndStoredLoansRemainTheSame(bool explicitType)
    {
        var source = "struct View<T>{source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\nlet n: i32 = 42\nlet view = View" + (explicitType ? "<i32>" : string.Empty) + ".init(n@ref)\nrequire view.value@follow == 42 else => $abort(\"loan\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c));
        Assert.True(c.Analyze().IsVerified, MinimalEmissionTest.Describe(c));
        ScalarEmissionTest.EmitFixture("ConstructorStoredLoan" + explicitType, source, string.Empty);
    }

    // SPEC 10.8, 10.8.1, 15.3.6 (PLAN G463): an own Origin slot that no argument fixes is no mismatch: the expected result fills it by
    // direct match, and otherwise (a Never or waiting argument, or none) it is a region of the call, which its uses bound
    // (NoApplicableOverload_Kd before). Rows: Never, `do => return`, `.None` with a method, `.Some(v@ref)`, a Phantom Origin with a method,
    // two slots, a generic structure with explicit and inferred T, and the expected-only slot at a `let`, at results and at an invariant slot.
    // The last row's candidate with the unfixed slot now applies and wins by fewer defaults (10.4 step 4), so `v` is not held by `h`
    // (ComparisonLoanConflict_Kd before, from the storing candidate).
    [Theory]
    [InlineData(Held + "func f() -> i32\n    let h = H.init($abort(\"x\"))\n    return 0\n")]
    [InlineData(Held + "func f() -> i32\n    let h = H.init(do => return 7)\n    return 0\n")]
    [InlineData(Optional + "func f() -> bool\n    let h = H2.init(.None)\n    return h.has()\n")]
    [InlineData(Optional + "func f() -> i32\n    let v: i32 = 1\n    let h = H2.init(.Some(v@ref))\n    return 0\n")]
    [InlineData(Phantom + "func f() -> i32\n    let p = P.init()\n    return p.get()\n")]
    [InlineData("struct T2 {a, b}\n    public let r: ref/i32 during a\n    public let s: ref/i32 during b\n    public init(x: ref/i32 during a, y: ref/i32 during b)\n        self.r = x\n        self.s = y\nfunc f() -> i32\n    let v: i32 = 1\n    let t = T2.init(v, $abort(\"x\"))\n    return 0\n")]
    [InlineData("struct G<T> {a}\n    public let r: ref/T during a\n    public init(x: ref/T during a) => self.r = x\nfunc f() -> i32\n    let g = G<i32>.init($abort(\"x\"))\n    return 0\n")]
    [InlineData("struct G<T> {a}\n    public let r: ref/i32 during a\n    public let t: T\n    public init(x: ref/i32 during a, y: T)\n        self.r = x\n        self.t = y@move\nfunc f() -> i32\n    let n: i64 = 2\n    let g = G.init($abort(\"x\"), n)\n    return 0\n")]
    [InlineData(Held + "func f(p: ref/i32) -> i32\n    let h: H during p = H.init($abort(\"x\"))\n    return 0\n")]
    [InlineData(Optional + "func g(q: ref/i32) -> H2 during q => H2.init(.Some(q))\n")]
    [InlineData(Phantom + "func g(q: ref/i32) -> P during q => P.init()\n")]
    [InlineData(Invariant + "func g(p: ref/i32) -> U during p => U.init(.None)\n")]
    [InlineData("struct HS {a}\n    public let r: Option<ref/i32 during a>\n    public init(x: ref/i32 during a, y: i32 = 0) => self.r = .Some(x)\n    public func has(self: ref/Self) -> bool\n        return match self.r\n            .Some(_) => true\n            .None => false\n    public init(x: ref/i32) => self.r = .None\nfunc f() -> bool\n    var v: i32 = 1\n    let h = HS.init(v@ref)\n    v = 2\n    return h.has()\n")]
    public void AnOwnOriginSlotNoArgumentFixesKeepsTheCandidateApplicable(string body)
    {
        var check = DiagnosticCorpus.Check(body + Main);
        Assert.True(check.Accepted, string.Join("\n", check.Diagnostics.Select(static x => $"{x.Code}: {x.Label}")));
    }

    // SPEC 10.4, 10.8, 10.8.1, 15.6, 6.5.2 (PLAN G463): the invalid twins keep their own records (each NoApplicableOverload_Kd before): two
    // such candidates are ambiguous (10.4 step 2), an unbound T is the inference boundary, a Loan the region holds conflicts with a write
    // or with the local's end where the region outlives the result, and a local's borrow at a slot the expected result fills fails that
    // relation, as at a slot an argument fixes (also through an invariant slot). Unchanged: a mismatched other argument, and a Case
    // construction, whose Origins the expected Type and payloads must determine (6.5.2).
    [Theory]
    [InlineData("struct H {a}\n    public let n: i32\n    public init(x: ref/i32 during a) => self.n = 1\n    public init(x: ref/i64 during a) => self.n = 2\nfunc f() -> i32\n    let h = H.init($abort(\"x\"))\n    return 0\n", new[] { nameof(DiagnosticCode.AmbiguousBinding_Kd) })]
    [InlineData("struct G<T> {a}\n    public let r: ref/T during a\n    public init(x: ref/T during a) => self.r = x\nfunc f() -> i32\n    let g = G.init($abort(\"x\"))\n    return 0\n", new[] { nameof(DiagnosticCode.UnboundTypeArgument_Kd) })]
    [InlineData(Optional + "func f() -> bool\n    var v: i32 = 1\n    let h = H2.init(.Some(v@ref))\n    v = 2\n    return h.has()\n", new[] { nameof(DiagnosticCode.ComparisonLoanConflict_Kd) })]
    [InlineData(Optional + "func g(q: ref/i32) -> H2 during q\n    let v: i32 = 1\n    return H2.init(.Some(v@ref))\n", new[] { nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) })]
    // A pre-existing record shape (PLAN G526): by SPEC 15.6.5 the conflict at `let v` rests on the Refuted relation (rule W), as
    // for the qualified `Option.Some` twin, which gives two UnsatisfiedOriginRelation_Kd; a function's waiting argument gives this row's
    // pair on dev (`mk(o: Option<ref/i32 during s>)`).
    [InlineData(Optional + "func g(q: ref/i32) -> H2 during q\n    let v: i32 = 1\n    let h = H2.init(.Some(v@ref))\n    return h@move\n", new[] { nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) })]
    [InlineData(Optional + "func g(p: ref/i32) -> H2 during p\n    let v: i32 = 1\n    let h: H2 during p = H2.init(.Some(v@ref))\n    return h@move\n", new[] { nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) })]
    [InlineData(Invariant + "func g(p: ref/i32) -> U during p\n    let v: i32 = 1\n    var w = v@ref\n    return U.init(.Some(w@uniq))\n", new[] { nameof(DiagnosticCode.ComparisonLoanConflict_Kd), nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd), nameof(DiagnosticCode.UnsatisfiedOriginRelation_Kd) })]
    [InlineData("struct H {a}\n    public let r: ref/i32 during a\n    public init(x: ref/i32 during a, y: i32) => self.r = x\nfunc f() -> i32\n    let h = H.init($abort(\"x\"), true)\n    return 0\n", new[] { nameof(DiagnosticCode.NoApplicableOverload_Kd) })]
    [InlineData("enum E {a}\n    A(ref/i32 during a)\n    B\nfunc f() -> i32\n    let e = E.A($abort(\"x\"))\n    return 0\n", new[] { nameof(DiagnosticCode.MissingOriginBinding_Kd) })]
    public void TheTwinsOfAnUnfixedOwnOriginSlotKeepTheirRecords(string body, string[] codes)
        => Assert.Equal(codes, DiagnosticCorpus.Check(body + Main).Diagnostics.Select(static x => x.Code));

    [Theory]
    [InlineData("struct Box<T>\n    public init(value: T) => ()\nlet n: i32 = 42\nlet box = Box.init(n)")]
    [InlineData("struct Box<T>\n    public init(value: T, extra: i32 = 0) => ()\nlet n: i32 = 42\nlet box = Box.init(n)")]
    [InlineData("struct Box<T>\n    public init(value: T) => ()\nlet box = Box.init(42)")]
    [InlineData("struct Box<T>\n    public init(value: T) => ()\nlet box = Box.init(func () => 42)")]
    [InlineData("struct C<T>\n    public init(x: ref/i32, y: ref/T) => ()\n    public init(x: ref/T, y: ref/T) => ()\nlet n: i32 = 42\nlet r = n@ref\nlet c = C.init(r@ref, n@ref)")]
    [InlineData("struct C<T>\n    public init(value: (T, i32), code: i32) => ()\n    public init(value: ref/(i64, T), name: string) => ()\nlet x: (i64, i32) = (1, 2)\nlet c = C.init(x, 0)")]
    [InlineData(UnknownPremise + "Box.init(value@move)\n    return box.tag")]
    public void DifferentialValidationAgreesWithTheFullReference(string source)
    {
        var fast = MinimalEmissionTest.Analyze(source);
        var reference = MinimalEmissionTest.Analyze(source);
        reference.Binding.UseConstructorReferenceCheck = true;
        Assert.Equal(fast.Binding.Result.IsComplete, reference.Bind().IsComplete);
        Assert.Equal(fast.Binding.Issues.Select(x => x.Failure), reference.Binding.Issues.Select(x => x.Failure));
        Assert.Equal(Constructions(fast), Constructions(reference));
        fast.Binding.ReportDiagnostics();
        reference.Binding.ReportDiagnostics();
        Assert.Equal(TestDiagnostics.Of(fast).Select(x => (x.Code, x.Label, x.Note)), TestDiagnostics.Of(reference).Select(x => (x.Code, x.Label, x.Note)));

        static string[] Constructions(Compilation compilation)
            => KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.CallOf()?.Target.Declaration is FunctionKoto { IsConstructor: true })
                .Select(x => Binding.DiagnosticTypeName(x.CallOf()!.DeclaringType!) + ":" + x.CallOf()!.Target.Declaration.Span.Start).ToArray();
    }

    // SPEC 8.3.3: the fixed construction defers on an Unknown premise only as the first selection does, so the plain init, still
    // strictly best with the conditional one ranked as applicable, is kept, as with the written Type.
    [Theory]
    [InlineData("Box")]
    [InlineData("Box<U>")]
    public void AnUnknownPremiseThatCannotAffectTheSelectionKeepsTheConstruction(string type)
    {
        var c = CompilationTestHelper.ParseSuccess(UnknownPremise + type + ".init(value@move)\n    return box.tag");
        Assert.True(c.Bind().IsComplete);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.CallOf()?.Target.Declaration is FunctionKoto { IsConstructor: true });
        Assert.Single(((FunctionKoto)call.CallOf()!.Target.Declaration).Parameters);
    }

    [Fact]
    public void InferredConcreteClosureExecutesWithoutErasure()
        => ScalarEmissionTest.EmitFixture("ConstructorClosureInference", Box + "let box = Box.init(func (value: i32) => value + 1)\nrequire box.value(41) == 42 else => $abort(\"closure\")\nConsole.writeLine(\"ok\")", "ok\n");

    [Fact]
    [Trait("Purpose", "Allocation")]
    public void WarmInferredConstructionReusesEveryPhase()
    {
        var c = MinimalEmissionTest.Analyze(Box + "let box = Box.init(42)\nrequire box.value == 42 else => $abort(\"construction\")");
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Generate(TextWriter.Null)));
        Assert.True(valid, MinimalEmissionTest.Describe(c));
    }
}
