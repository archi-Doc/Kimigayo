// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi;
using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class SharedObjectBindingTest
{
    [Theory]
    [InlineData("Rc", "rc")]
    [InlineData("Arc", "arc")]
    public void StrongCreationAndExplicitHandleCloneVerify(string factory, string mode)
    {
        var c = MinimalEmissionTest.Analyze($"let first: {mode}/i32 = Kimi.Intrinsics.make{factory}(7)\nlet second: {mode}/i32 = Kimi.Intrinsics.clone(first@ref)");
        Assert.True(c.Binding.Result.IsComplete, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Ownership.Result.IsVerified, MinimalEmissionTest.Describe(c, null));
        Assert.True(c.Emission.Validate(out var issue), MinimalEmissionTest.Describe(c, issue));
    }

    [Theory]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void CreationPreservesBorrowedPayloadTypes(string factory)
    {
        var c = CompilationTestHelper.Parse($"struct Item {{source}}\n    public let value: ref/i32 during source\n    public init(value: ref/i32 during source) => self.value = value\nlet n = 7\nlet first = Kimi.Intrinsics.make{factory}(Item.init(n@ref))\nlet second = Kimi.Intrinsics.clone(first@ref)");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    [Fact]
    public void PairGenericCloneUsesItsPublishedModeConstraint()
    {
        var c = CompilationTestHelper.Parse("func duplicate<s/T>(value: ref/(s/T)) -> s/T\n    s is rc or arc\n    return Kimi.Intrinsics.clone(value)\nfunc use(value: ref/rc/i32) -> rc/i32 => duplicate(value)");
        Assert.True(c.Bind().IsComplete, string.Join('\n', c.Binding.Issues));
    }

    // SPEC 8.10, 13.5.8: without a premise that proves `s is rc or arc`, a generic clone is an ordinary unproven call.
    [Theory]
    [InlineData("func dup<T>(value: ref/T) -> T => Kimi.Intrinsics.clone(value)", "T")]
    [InlineData("func dup<s/T>(value: ref/(s/T)) -> s/T => Kimi.Intrinsics.clone(value)", "s/T")]
    [InlineData("func dup<s/T>(value: ref/(s/T)) -> s/T\n    s is object\n    return Kimi.Intrinsics.clone(value)", "s/T")]
    [InlineData("func dup<s/T>(value: ref/(s/T)) -> s/T\n    s is rc or obj\n    return Kimi.Intrinsics.clone(value)", "s/T")]
    public void GenericCloneWithoutTheModePremiseIsUnproven(string source, string subject)
    {
        var c = MinimalEmissionTest.Analyze(source);
        Assert.False(c.Binding.Result.IsComplete);
        c.Binding.ReportDiagnostics();
        var error = Assert.Single(TestDiagnostics.Of(c));
        Assert.Equal("UnprovenConstraint_Kd", error.Code);
        Assert.Equal("Kimi.Intrinsics.clone(value)", error.Text);
        Assert.Contains("requires s is rc or arc; this condition cannot be proven for " + subject, error.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void FactoriesRejectPayloadOptOuts(string factory)
    {
        var c = CompilationTestHelper.Parse($"struct Item\n    Self is not ObjectPayload\nlet value = Kimi.Intrinsics.make{factory}(Item.init())");
        Assert.False(c.Bind().IsComplete);
    }

    [Theory]
    [InlineData("func bad(value: rc/i32) => Kimi.Intrinsics.clone(value)")]
    [InlineData("func bad(value: arc/i32) => Kimi.Intrinsics.clone(value)")]
    [InlineData("func bad(value: obj/i32) => Kimi.Intrinsics.clone(value@ref)")]
    [InlineData("func bad(value: objref/i32) => Kimi.Intrinsics.clone(value@ref)")]
    [InlineData("func bad(value: i32) => Kimi.Intrinsics.clone(value@ref)")]
    public void CloneRequiresAnExplicitBorrowOfACountedHandle(string source)
    {
        var c = CompilationTestHelper.Parse(source);
        Assert.False(c.Bind().IsComplete);
    }
}
