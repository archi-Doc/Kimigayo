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
    [InlineData(KimiDeclarationId.DictionaryRefRemainder, 0)]
    [InlineData(KimiDeclarationId.DictionaryRefRemainder, 1)]
    [InlineData(KimiDeclarationId.DictionaryRefRemainder, 2)]
    [InlineData(KimiDeclarationId.DictionaryRefRemainder, 3)]
    [InlineData(KimiDeclarationId.DictionaryUniqRemainder, 0)]
    [InlineData(KimiDeclarationId.DictionaryUniqRemainder, 3)]
    [InlineData(KimiDeclarationId.DictionaryOwnedRemainder, 0)]
    [InlineData(KimiDeclarationId.DictionaryOwnedRemainder, 3)]
    [InlineData(KimiDeclarationId.DictionaryOwnedRemainder, 4)]
    public void RemainderFieldsRetainTheirBoundTypes(KimiDeclarationId id, int index)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var declaration = (StructKoto)c.Library.GetSymbol(id)!.Declaration;
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
    [InlineData(KimiDeclarationId.StorageBorrowDictionaryExclusive, "input-mode")]
    [InlineData(KimiDeclarationId.StorageBorrowDictionaryExclusive, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageBorrowDictionaryExclusive, "result-static")]
    [InlineData(KimiDeclarationId.StorageLendUniqKey, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageLendUniqKey, "result-state")]
    [InlineData(KimiDeclarationId.StorageLendUniqKey, "input-mode")]
    [InlineData(KimiDeclarationId.StorageSplitValue, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageSplitValue, "result-state")]
    [InlineData(KimiDeclarationId.StorageSplitValue, "result-mode")]
    [InlineData(KimiDeclarationId.StorageSplitValue, "pointer")]
    [InlineData(KimiDeclarationId.StorageOwnDictionary, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageKeyAt, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageKeyAt, "result-mode")]
    [InlineData(KimiDeclarationId.StorageValueAt, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageDictionaryLayout, "input-mode")]
    [InlineData(KimiDeclarationId.StorageDictionaryLayout, "input-key")]
    [InlineData(KimiDeclarationId.StorageDictionaryLayout, "result-swapped")]
    [InlineData(KimiDeclarationId.StorageDictionaryLayout, "result-static")]
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
                input.BoundType = Copy(original, semantics: original.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "result-mode":
                symbol.Type = Copy(result, semantics: result.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
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

    // SPEC 22.1.2.5: the fixed-array borrowStorage overloads keep [N of E] with their own N, the borrow mode and its Origin.
    [Theory]
    [InlineData(KimiDeclarationId.StorageBorrowFixedShared, "input-mode")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedShared, "element")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedShared, "length")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedShared, "result-static")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedExclusive, "input-mode")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedExclusive, "length")]
    [InlineData(KimiDeclarationId.StorageBorrowFixedExclusive, "result-static")]
    public void FixedArrayBorrowsKeepTheirCompleteSignatures(KimiDeclarationId id, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var symbol = c.Library.GetSymbol(id)!;
        var function = (FunctionKoto)symbol.Declaration;
        var input = function.Parameters[0].Type;
        var original = input.BoundType!;
        var array = original.Components[0];
        switch (mutation)
        {
            case "input-mode":
                input.BoundType = Copy(original, semantics: original.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "element":
                input.BoundType = Copy(original, components: [new(array.Name, array.Kind, array.Symbol, array.Semantics, [BoundType.ISize], array.Length, array.Origin, [], array.LengthExpression)]);
                break;
            case "length":
                input.BoundType = Copy(original, components: [new(array.Name, array.Kind, array.Symbol, array.Semantics, array.Components.ToArray(), array.Length, array.Origin, [], new BoundLength(KotoKind.NumberLiteral, 4, null, null, null))]);
                break;
            case "result-static":
                symbol.Type = Copy(symbol.Type!, origins: [BoundOrigin.Static]);
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData("let r = Kimi.Storage.borrowStorage(map@ref)")]
    [InlineData("unsafe => _ = Kimi.Storage.lendKey(map@ref, null@unsafe/u8)")]
    [InlineData("unsafe => _ = Kimi.Storage.dictionaryStorage(map@uniq)")]
    [InlineData("func f(r: Kimi.Storage.DictionaryRefRemainder<string, i32>) => ()")]
    public void UserSourceCannotReachTheDictionaryBoundary(string use)
    {
        var c = MinimalEmissionTest.Analyze("let map: Dictionary<string, i32> = [\"a\": 1]\n" + use);
        Assert.False(c.Binding.Result.IsComplete);
    }

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BoundType[]? components = null, BoundOrigin? origin = null, BoundOrigin[]? origins = null)
        => new(type.Name, type.Kind, type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, origins ?? type.OriginArguments.ToArray(), type.LengthExpression);
}
