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

    [Theory]
    [InlineData("Rc")]
    [InlineData("Arc")]
    public void FactoriesRejectPayloadOptOuts(string factory)
    {
        var c = CompilationTestHelper.Parse($"struct Item\n    Self is not ObjectPayload\nlet value = Kimi.Intrinsics.make{factory}(Item.init())");
        Assert.False(c.Bind().IsComplete);
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
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
        Assert.DoesNotContain(c.Binding.Issues, static x => x.Code == DiagnosticCode.InvalidKimiLibrary_Kd);
    }

    [Theory]
    [InlineData(KimiDeclarationId.MakeRc)]
    [InlineData(KimiDeclarationId.MakeArc)]
    [InlineData(KimiDeclarationId.Clone)]
    public void SignatureChangesInvalidateTheLibrary(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var function = Assert.IsType<FunctionKoto>(c.Library.GetSymbol(id)!.Declaration);
        function.Parameters[0].Type = ((FunctionKoto)c.Library.Replace.Declaration).Parameters[0].Type;
        Assert.False(c.Bind().IsComplete);
        Assert.Equal(KimiDeclarationState.Invalid, c.Library.GetDeclarationState(id));
        Assert.False(c.Library.IsCompleteStrongOwnershipFamily);
    }
}
