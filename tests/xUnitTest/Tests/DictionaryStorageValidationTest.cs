// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

/// <summary>SPEC 22.1.2.5: the Dictionary storage family is internal, distinct from the contiguous remainders, and its
/// compiler-implemented operations keep their complete bound signatures.</summary>
public class DictionaryStorageValidationTest
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RemainderFieldsRetainTheirBoundTypes(int index)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var declaration = (StructKoto)c.Library.GetSymbol(KimiDeclarationId.DictionaryRefRemainder)!.Declaration;
        var field = declaration.Members.OfType<VariableKoto>().ElementAt(index);
        field.TypeKoto!.BoundType = field.BoundSymbol!.Type = index == 0 ? BoundType.ISize : BoundType.Primitives["u8"];
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.StorageBorrowDictionary, "input-mode")]
    [InlineData(KimiDeclarationId.StorageBorrowDictionary, "input-key")]
    [InlineData(KimiDeclarationId.StorageBorrowDictionary, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageBorrowDictionary, "result-static")]
    [InlineData(KimiDeclarationId.StorageLendKey, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageLendKey, "result-state")]
    [InlineData(KimiDeclarationId.StorageLendKey, "result-static")]
    [InlineData(KimiDeclarationId.StorageLendKey, "pointer")]
    [InlineData(KimiDeclarationId.StorageLendValue, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageLendValue, "result-state")]
    [InlineData(KimiDeclarationId.StorageLendValue, "input-mode")]
    public void OperationsKeepTheirCompleteSignatures(KimiDeclarationId id, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = (FunctionKoto)symbol.Declaration;
        var input = function.Parameters[0].Type;
        var original = input.BoundType!;
        var result = symbol.Type!;
        var key = function.GenericArguments[0].BoundType!;
        var value = function.GenericArguments[1].BoundType!;
        switch (mutation)
        {
            case "input-mode":
                input.BoundType = Copy(original, semantics: SemanticsKind.Uniq);
                break;
            case "input-key":
                input.BoundType = Copy(original, components: [Copy(original.Components[0], components: [value, value])]);
                break;
            case "result-swapped":
                symbol.Type = result.Kind == BoundTypeKind.Semantics ? Copy(result, components: [ReferenceEquals(result.Components[0], key) ? value : key]) : Copy(result, components: [value, key]);
                break;
            case "result-static":
                symbol.Type = result.Kind == BoundTypeKind.Semantics ? Copy(result, origin: BoundOrigin.Static) : Copy(result, origins: [BoundOrigin.Static]);
                break;
            case "result-state":
                symbol.Type = Copy(result, origin: original.Origin);
                break;
            case "pointer":
                function.Parameters[1].Type.BoundType = Copy(function.Parameters[1].Type.BoundType!, components: [BoundType.Primitives["i32"]]);
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData("let r = Kimi.Storage.borrowStorage(map@ref)")]
    [InlineData("unsafe => _ = Kimi.Storage.lendKey(map@ref, null@unsafe/u8)")]
    [InlineData("func f(r: Kimi.Storage.DictionaryRefRemainder<string, i32>) => ()")]
    public void UserSourceCannotReachTheDictionaryBoundary(string use)
    {
        var c = MinimalEmissionTest.Analyze("let map: Dictionary<string, i32> = [\"a\": 1]\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BoundType[]? components = null, BoundOrigin? origin = null, BoundOrigin[]? origins = null)
        => new(type.Name, type.Kind, type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, origins ?? type.OriginArguments.ToArray(), type.LengthExpression);
}
