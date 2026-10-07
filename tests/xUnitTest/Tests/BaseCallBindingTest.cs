// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class BaseCallBindingTest
{
    private const string Source = "open struct Base\n    public let value: i32 = 7\n    public func read(self: ref/Self, n: i32 = 2) -> i32 => self.value + n\nstruct Derived : Base\n    public init() => ()\n    public func readAgain(self: ref/Self) -> i32 => base.read() + 1\nlet value = Derived.init()\nrequire value.readAgain() == 10 else => $abort(\"base\")";

    [Fact]
    public void TheOrdinaryInheritedCallIsAValidCounterpart()
    {
        var c = MinimalEmissionTest.Analyze(Source.Replace("base.read", "self.read", StringComparison.Ordinal));
        ScalarEmissionTest.WriteFixture("BaseCallCounterpart", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void ADirectBaseCallUsesTheExistingProjectionAndOriginalDefaults()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        ScalarEmissionTest.WriteFixture("BaseCallDefault", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void ExplicitSelfCapturesPreserveLexicalBaseLookup()
    {
        var source = Source.Replace("=> base.read() + 1", "\n        let operation = func[self]() -> i32 => base.read() + 1\n        return operation()", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        ScalarEmissionTest.WriteFixture("BaseCallCapture", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void LookupContinuesThroughAnIntermediateBase()
    {
        var source = Source.Replace("struct Derived : Base", "open struct Middle : Base\n    public init() => ()\nstruct Derived : Middle", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(source);
        ScalarEmissionTest.WriteFixture("BaseCallTransitive", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void EnclosingBindingsAndFunctionTypeArgumentsUseOrdinaryInference()
    {
        const string Generic = "open struct Base<T>\n    public func choose<U>(self: ref/Self, value: U) -> U\n        U is Copy\n        return value\nstruct Derived<V> : Base<V>\n    public init() => ()\n    public func read(self: ref/Self) -> i32 => base.choose<i32>(7)\nlet value = Derived<i64>.init()\nrequire value.read() == 7 else => $abort(\"generic\")";
        var c = MinimalEmissionTest.Analyze(Generic);
        ScalarEmissionTest.WriteFixture("BaseCallGeneric", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Fact]
    public void ParenthesizedCalleesKeepDirectBaseSelection()
    {
        var c = MinimalEmissionTest.Analyze(Source.Replace("base.read()", "(base.read)()", StringComparison.Ordinal));
        ScalarEmissionTest.WriteFixture("BaseCallParenthesized", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    [Theory]
    [InlineData("func f(self: ref/Self, n: i32 = base.read()) -> i32 => n", "instance body")]
    [InlineData("func f(self: ref/Self) -> i32 => base.value()", "instance function")]
    [InlineData("func f(self: ref/Self) -> i32 => base()", "base.name(arguments)")]
    public void HeadersAndNonInstanceTargetsDoNotAcquireBaseValues(string declaration, string expected)
    {
        var c = MinimalEmissionTest.Analyze("open struct Base\n    public let value: i32 = 7\n    public func read(self: ref/Self) -> i32 => 1\nstruct Derived : Base\n    " + declaration + "\n()");
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c), x => x.Code == "InvalidBaseCall_Kd");
        Assert.Equal("base", error.Text);
        Assert.Contains(expected, error.Label!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AChangedBaseRevokesThePreviousTargetAndPath(bool direct)
    {
        const string Types = "open struct Other\n    public func read(self: ref/Self, n: i32 = 2) -> i32 => 20 + n\n";
        var source = direct ? Source : Source.Replace("base.read", "self.read", StringComparison.Ordinal);
        var c = MinimalEmissionTest.Analyze(Types + source);
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.Method is MemberAccessKoto { Right: IdentifierNameKoto { IdentifierName: "read" } });
        var before = call.BoundCall!.Target;
        var previousPath = call.BoundCall.BasePath;
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        var donor = MinimalEmissionTest.Analyze(Types + source.Replace("Derived : Base", "Derived : Other", StringComparison.Ordinal));
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], changed.Bases[0]));
        Assert.Null(call.BoundCall);
        Assert.True(c.Bind().IsComplete);
        Assert.NotSame(before, call.BoundCall!.Target);
        Assert.NotSame(previousPath, call.BoundCall.BasePath);
        Assert.Equal("Other", call.BoundCall.DeclaringType!.Symbol!.Name);
    }

    [Trait("Purpose", "Allocation")]
    [Fact]
    public void WarmBaseCallsReuseAllPlans()
    {
        var c = MinimalEmissionTest.Analyze(Source);
        Assert.True(c.Emission.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete, iterations: 64, warmupIterations: 32));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Ownership.Analyze().IsVerified, iterations: 64, warmupIterations: 32));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Emission.WriteIr(TextWriter.Null, out _), iterations: 64, warmupIterations: 32));
        Assert.True(valid);
    }
}
