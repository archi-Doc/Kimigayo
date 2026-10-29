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

    // SPEC 6.4.4 per the owner's reading (DECISIONS BU-53): an instance getter's body is a derived instance body; its base call takes the
    // getter's self and the original default.
    [Fact]
    public void AGetterBodyIsADerivedInstanceBody()
    {
        var c = MinimalEmissionTest.Analyze(Accessors("get() -> i32 => base.read() + 1"));
        ScalarEmissionTest.WriteFixture("BaseCallGetter", CompilationTestHelper.WriteIr(c), string.Empty);
    }

    // SPEC 6.4.4, 11.3.1 (DECISIONS BU-53): a setter's body takes its uniq/Self as lexical self, a Closure in a getter's body through its
    // explicit self capture.
    [Theory]
    [InlineData("get() -> i32 => 0\n        set(value: i32) -> ()\n            let n = base.read(value)")]
    [InlineData("get() -> i32\n            let operation = func[self]() -> i32 => base.read() + 1\n            return operation()")]
    public void AccessorBodiesTakeTheAccessorsSelf(string accessors)
    {
        var c = MinimalEmissionTest.Analyze(Accessors(accessors));
        Assert.True(c.Analysis.IsVerified, MinimalEmissionTest.Describe(c, null));
        c.Binding.ReportDiagnostics();
        Assert.Empty(TestDiagnostics.Of(c));
        var call = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<InvocationKoto>().Single(x => x.Method is MemberAccessKoto { Left: BaseReferenceKoto });
        Assert.Equal("Base", call.CallOf()!.DeclaringType!.Symbol!.Name);
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
    [InlineData("group Settings\n        computed limit: i32\n            get() -> i32 => base.read()", "derived instance function")]
    [InlineData("computed again: i32\n        get() -> i32\n            let operation = func() -> i32 => base.read()\n            return operation()", "explicit self capture")]
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
        var before = call.CallOf()!.Target;
        var previousPath = call.CallOf()!.BasePath;
        var derived = KotoTree.Walk(c.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        var donor = MinimalEmissionTest.Analyze(Types + source.Replace("Derived : Base", "Derived : Other", StringComparison.Ordinal));
        var changed = KotoTree.Walk(donor.Kotonoha.RootKoto).OfType<StructKoto>().Single(x => x.Name == "Derived");
        Assert.True(KotoHelper.Replace(derived, derived.Bases[0], ParseTestHelper.Detach(changed.Bases[0], new CodeBlockKoto(derived.CodeContext))));
        Assert.Null(call.CallOf());
        Assert.True(c.Bind().IsComplete);
        Assert.NotSame(before, call.CallOf()!.Target);
        Assert.NotSame(previousPath, call.CallOf()!.BasePath);
        Assert.Equal("Other", call.CallOf()!.DeclaringType!.Symbol!.Name);
    }

    [Trait("Purpose", "Allocation")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmBaseCallsReuseAllPlans(bool getter)
    {
        var c = MinimalEmissionTest.Analyze(getter ? Accessors("get() -> i32 => base.read() + 1") : Source);
        Assert.True(c.WriteIr(TextWriter.Null, out var issue), MinimalEmissionTest.Describe(c, issue));
        var valid = true;
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Bind().IsComplete));
        c.Binding.CheckStartup(OutputKind.Application);
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.Analyze().IsVerified));
        Assert.Equal(0, AllocationMeasurement.Measure(() => valid &= c.WriteIr(TextWriter.Null, out _)));
        Assert.True(valid);
    }

    // Source with readAgain as the computed member `again` declaring the given accessors.
    private static string Accessors(string accessors)
        => Source.Replace("func readAgain(self: ref/Self) -> i32 => base.read() + 1", "computed again: i32\n        " + accessors, StringComparison.Ordinal)
            .Replace("value.readAgain()", "value.again", StringComparison.Ordinal);
}
