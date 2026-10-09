// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstructorTypeInferenceTest
{
    private const string Box = "struct Box<T>\n    public let value: T\n    public init(value: T) => self.value = value@move\n";
    private const string UnknownPremise = "struct Box<T>\n    public var tag: i32 = 0\n    public init(value: T) => self.tag = 1\n    public init(value: T, extra: i32 = 0)\n        T is Equatable\n        self.tag = 2\nfunc make<U>(value: U) -> i32\n    let box = ";

    [Theory]
    [InlineData("let n: i32 = 42\nlet box = Box.init(n)", "i32")]
    [InlineData("let box: Box<i64> = Box.init(42)", "i64")]
    [InlineData("let box = Box.init(42)", "i32")]
    [InlineData("let box = Box.init(true)", "bool")]
    [InlineData("let box = Box.init(value: 42)", "i32")]
    public void OwnTypeSlotsUseTheCommonCallInference(string use, string name)
    {
        var c = MinimalEmissionTest.Analyze(Box + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Declaration is FunctionKoto { IsConstructor: true });
        var plan = Assert.IsType<BoundCall>(call.BoundCall);
        Assert.True(plan.TypeArguments.IsEmpty);
        Assert.Equal(name, Assert.Single(plan.DeclaringType!.Components).Name);
        Assert.Same(plan.DeclaringType, call.BoundType);
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
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
        Assert.True(c.Binding.Result.IsComplete == explicitType, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
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
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundCall?.Target.Declaration is FunctionKoto { IsConstructor: true });
        Assert.Equal(BoundTypeKind.Closure, call.BoundType!.Components[0].Kind);
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
        Assert.True(valid == c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
    }

    [Theory]
    [InlineData("let a = Box.init((1, true))")]
    [InlineData("let values = [1, 2]\nlet a = Box.init(values@move)")]
    [InlineData("let a: Box<[2 of i32]> = Box.init([1, 2])")]
    [InlineData("let n: i32 = 42\nlet a = Box.init(n@ref)")]
    [InlineData("var n: i32 = 42\nlet a = Box.init(n@uniq)")]
    [InlineData("let a = Box.init(Box.init(42))")]
    public void CompositeAndBorrowTypesFollowTheExistingStoragePath(string use)
    {
        var c = MinimalEmissionTest.Analyze(Box + use);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("raw/T")]
    [InlineData("(T, T)")]
    public void ExplicitRecursiveConstructionSubstitutesOnce(string argument)
    {
        var c = MinimalEmissionTest.Analyze($"struct Grow<T>\n    public init() => ()\n    public func make() -> Grow<{argument}> => Grow<{argument}>.init()\nlet value = Grow<i32>.init()");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>(), x => x.BoundType?.Components.FirstOrDefault()?.Kind is BoundTypeKind.Tuple or BoundTypeKind.Semantics);
        Assert.Equal(argument == "raw/T" ? "Grow<raw/T>" : "Grow<(T, T)>", Binding.DiagnosticTypeName(call.BoundType!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOriginSlotsAndStoredLoansRemainTheSame(bool explicitType)
    {
        var source = "struct View<T>{source}\n    public let value: ref/T during source\n    public init(value: ref/T during source) => self.value = value\nlet n: i32 = 42\nlet view = View" + (explicitType ? "<i32>" : string.Empty) + ".init(n@ref)\nrequire view.value@follow == 42 else => $abort(\"loan\")";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        ScalarEmissionTest.EmitFixture("ConstructorStoredLoan" + explicitType, source, string.Empty);
    }

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
            => KotoTree.Walk(compilation.Kotonoha.RootKoto).OfType<InvocationKoto>().Where(x => x.BoundCall?.Target.Declaration is FunctionKoto { IsConstructor: true })
                .Select(x => Binding.DiagnosticTypeName(x.BoundCall!.DeclaringType!) + ":" + x.BoundCall.Target.Declaration.Span.Start).ToArray();
    }

    // SPEC 8.4.8.2: the fixed construction defers on an Unknown premise only as the first selection does, so the plain init, still
    // strictly best with the conditional one ranked as applicable, is kept, as with the written Type.
    [Theory]
    [InlineData("Box")]
    [InlineData("Box<U>")]
    public void AnUnknownPremiseThatCannotAffectTheSelectionKeepsTheConstruction(string type)
    {
        var c = CompilationTestHelper.ParseSuccess(UnknownPremise + type + ".init(value@move)\n    return box.tag");
        Assert.True(c.Bind().IsComplete);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.BoundCall?.Target.Declaration is FunctionKoto { IsConstructor: true });
        Assert.Single(((FunctionKoto)call.BoundCall!.Target.Declaration).Parameters);
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
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid, MinimalEmissionTest.Describe(c, null));
    }
}
