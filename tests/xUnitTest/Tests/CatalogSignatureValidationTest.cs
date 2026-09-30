// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CatalogSignatureValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.ArrayReserve)]
    [InlineData(KimiDeclarationId.ArrayAppend)]
    [InlineData(KimiDeclarationId.ArrayInsert)]
    [InlineData(KimiDeclarationId.ArrayPop)]
    [InlineData(KimiDeclarationId.ArrayRemove)]
    [InlineData(KimiDeclarationId.ArrayClear)]
    [InlineData(KimiDeclarationId.ArraySwap)]
    [InlineData(KimiDeclarationId.ArrayShrinkToFit)]
    [InlineData(KimiDeclarationId.ArrayWithCapacity)]
    [InlineData(KimiDeclarationId.DictionaryReserve)]
    [InlineData(KimiDeclarationId.DictionaryTryInsert)]
    [InlineData(KimiDeclarationId.DictionaryInsertOrReplace)]
    [InlineData(KimiDeclarationId.DictionaryRemove)]
    [InlineData(KimiDeclarationId.DictionaryTryGet)]
    [InlineData(KimiDeclarationId.DictionaryIndex)]
    [InlineData(KimiDeclarationId.DictionaryIndexUniq)]
    [InlineData(KimiDeclarationId.StorageMissingDictionaryKey)]
    [InlineData(KimiDeclarationId.DictionaryClear)]
    [InlineData(KimiDeclarationId.DictionaryShrinkToFit)]
    [InlineData(KimiDeclarationId.Replace)]
    [InlineData(KimiDeclarationId.Exchange)]
    [InlineData(KimiDeclarationId.Swap)]
    [InlineData(KimiDeclarationId.MakeObj)]
    [InlineData(KimiDeclarationId.WriteLine)]
    [InlineData(KimiDeclarationId.TestTempDirectory)]
    public void EveryOperationChecksItsCompleteBoundSignature(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = (FunctionKoto)symbol.Declaration;
        for (var index = -1; index < function.Parameters.Count; index++)
        {
            var original = index < 0 ? symbol.Type : function.Parameters[index].Type.BoundType;
            if (index < 0)
            {
                symbol.Type = BoundType.Primitives["u8"];
            }
            else
            {
                function.Parameters[index].Type.BoundType = BoundType.Primitives["u8"];
            }

            Assert.False(c.Library.ValidateBoundDeclarations());
            Assert.Same(function, c.Library.InvalidDeclaration);
            if (index < 0)
            {
                symbol.Type = original;
            }
            else
            {
                function.Parameters[index].Type.BoundType = original;
            }

            Assert.True(c.Library.ValidateDeclarations());
            Assert.True(c.Library.ValidateBoundDeclarations());
        }
    }

    [Fact]
    public void ArrayResultsCannotUseAnUnrelatedElementParameter()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(KimiDeclarationId.ArrayPop)!;
        var original = symbol.Type!;
        symbol.Type = new(original.Name, original.Kind, original.Symbol, original.Semantics, [BoundType.String]);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    [Fact]
    public void ArrayReceiverCannotClaimStaticStorage()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(KimiDeclarationId.ArrayClear)!;
        var parameter = ((FunctionKoto)symbol.Declaration).Parameters[0].Type;
        var original = parameter.BoundType!;
        parameter.BoundType = new(original.Name, original.Kind, original.Symbol, original.Semantics, original.Components.ToArray(), origin: BoundOrigin.Static);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    [Fact]
    public void DictionaryLookupDependsOnTheReceiverRatherThanTheSearchKey()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(KimiDeclarationId.DictionaryTryGet)!;
        var function = (FunctionKoto)symbol.Declaration;
        var result = symbol.Type!;
        var inner = result.Components[0];
        var invalid = new BoundType(inner.Name, inner.Kind, inner.Symbol, inner.Semantics, inner.Components.ToArray(), origin: function.Parameters[1].Type.BoundType!.Origin);
        symbol.Type = new(result.Name, result.Kind, result.Symbol, result.Semantics, [invalid]);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.DictionaryTryInsert)]
    [InlineData(KimiDeclarationId.DictionaryRemove)]
    public void DictionaryPairResultsRetainKeyValueOrder(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var result = symbol.Type!;
        var pair = result.Components[^1];
        var reversed = new BoundType(pair.Name, pair.Kind, pair.Symbol, pair.Semantics, [pair.Components[1], pair.Components[0]]);
        var components = result.Components.ToArray();
        components[^1] = reversed;
        symbol.Type = new(result.Name, result.Kind, result.Symbol, result.Semantics, components);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    [Fact]
    public void SwapInputsKeepDistinctOriginBinders()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(KimiDeclarationId.Swap)!;
        var function = (FunctionKoto)symbol.Declaration;
        var type = function.Parameters[1].Type.BoundType!;
        function.Parameters[1].Type.BoundType = new(type.Name, type.Kind, type.Symbol, type.Semantics, type.Components.ToArray(), origin: function.Parameters[0].Type.BoundType!.Origin);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }
}
