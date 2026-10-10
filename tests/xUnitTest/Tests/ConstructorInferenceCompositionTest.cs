// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ConstructorInferenceCompositionTest
{
    private const string Associated = "contract C\n    associate Element\nstruct Key\n    Self is C\n    associate C.Element is i32\nstruct Tag<T>\n    public init() => ()\nstruct Holder<T>\n    T is C\n    public init(value: T.Element, witness: Tag<T>) => ()\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExamplePreservesEvaluationAndCleanupOrder(bool explicitType)
    {
        var source = File.ReadAllText(DiagnosticCorpus.RepositoryPath("docs", "examples", "ConstructorInference", "ConstructorInference.kimi"));
        if (explicitType)
        {
            source = source.Replace("Pair.init", "Pair<Token>.init", StringComparison.Ordinal);
        }

        ScalarEmissionTest.EmitFixture("ConstructorInferenceExample" + explicitType, source, "inference ok\ncreate 1\ncreate 2\ndrop 2\ndrop 1\n");
    }

    [Theory]
    [InlineData("struct Pair<s/T>\n    public init(value: s/T) => ()\nlet n: i32 = 42\nlet a = Pair.init(n)\nlet b = Pair.init(n@ref)")]
    [InlineData("struct Box<T>\n    public init(value: T)\n        T is PrimitiveInteger\n        ()\nlet n: i32 = 42\nlet a = Box.init(n@ref)")]
    [InlineData("struct Box<T, F>\n    public init(action: F)\n        F is Callable<() -> T>\n        ()\nfunc answer() -> i32 => 42\nlet a = Box.init(answer)")]
    [InlineData("struct Box<T, F>\n    public init(action: F)\n        F is Callable<() -> T>\n        ()\nlet a = Box.init(func () -> i32 => 42)")]
    [InlineData(Associated + "let witness = Tag<Key>.init()\nlet h = Holder.init(42, witness@move)")]
    [InlineData("struct Outer<A>\n    A is Copy\n    public struct Inner<B>\n        public init(first: A, second: B) => ()\nlet v = Outer<i32>.Inner.init(42, true)")]
    [InlineData("open struct Base<T>\n    public init(value: T) => ()\nstruct Derived<T>: Base<T>\n    public init(value: T): base(value@move) => ()\nlet d = Derived.init(42)")]
    public void ConstructorSlotsComposeWithExistingStructuralEvidence(string source)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Analyze().IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var error), MinimalEmissionTest.Describe(c, error));
    }

    [Theory]
    [InlineData("let n: i32 = 42\nlet h = Holder.init(n)")]
    [InlineData("let h = Holder.init(42)")]
    public void AProjectionCannotInferItsReceiverBackward(string use)
    {
        var c = MinimalEmissionTest.Analyze(Associated.Replace("value: T.Element, witness: Tag<T>", "value: T.Element", StringComparison.Ordinal) + use);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var record = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnboundTypeArgument_Kd", record.Code);
        Assert.Contains("T", record.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void EditingAParameterInvalidatesTheGroupCorrelation()
    {
        const string source = "struct Item<T>\n    public init(value: T, code: i32) => ()\n    public init(value: T, name: string) => ()\nlet item = Item.init(42, 0)";
        var c = MinimalEmissionTest.Analyze(source);
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        var function = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.IsConstructor);
        var original = function.Parameters[0].Type;
        var donor = MinimalEmissionTest.Analyze(source.Replace("value: T, name", "value: ref/T, name", StringComparison.Ordinal));
        var replacement = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<FunctionKoto>().Last(x => x.IsConstructor).Parameters[0].Type;
        Assert.True(KotoHelper.Replace(function, original, replacement));
        Assert.False(c.Bind().IsComplete);
        Assert.Contains(c.Binding.Issues, x => x.Failure == BindingFailure.ParameterShapeMismatch);
        Assert.True(KotoHelper.Replace(function, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }

    [Fact]
    public void EditingTheInputInvalidatesTheConstructionAndItsContracts()
    {
        const string source = "struct Box<T>\n    public init(value: T) => ()\nlet b = Box.init(42)";
        var c = MinimalEmissionTest.Analyze(source);
        var call = Assert.Single(KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>());
        var original = call.ArgumentNodes[0];
        var donor = MinimalEmissionTest.Analyze(source.Replace("42", "true", StringComparison.Ordinal));
        var replacement = Assert.Single(KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<InvocationKoto>()).ArgumentNodes[0];
        Assert.True(KotoHelper.Replace(call, original, replacement));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(BoundType.Boolean, call.CallOf()!.DeclaringType!.Components[0]);
        Assert.True(KotoHelper.Replace(call, replacement, original));
        Assert.True(c.Bind().IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.Same(BoundType.I32, call.CallOf()!.DeclaringType!.Components[0]);
        Assert.True(CompilationTestHelper.Reload(c).Bind().IsComplete);
    }
}
