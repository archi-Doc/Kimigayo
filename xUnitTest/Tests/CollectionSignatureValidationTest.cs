// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class CollectionSignatureValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.ArrayReserve)]
    [InlineData(KimiDeclarationId.ArrayAppend)]
    [InlineData(KimiDeclarationId.ArrayInsert)]
    [InlineData(KimiDeclarationId.ArrayInsertIndex)]
    [InlineData(KimiDeclarationId.ArrayPop)]
    [InlineData(KimiDeclarationId.ArrayRemove)]
    [InlineData(KimiDeclarationId.ArrayRemoveIndex)]
    [InlineData(KimiDeclarationId.ArrayClear)]
    [InlineData(KimiDeclarationId.ArraySwap)]
    [InlineData(KimiDeclarationId.ArrayShrinkToFit)]
    [InlineData(KimiDeclarationId.ArrayWithCapacity)]
    public void EveryArrayOperationChecksItsCompleteBoundSignature(KimiDeclarationId id)
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
}
