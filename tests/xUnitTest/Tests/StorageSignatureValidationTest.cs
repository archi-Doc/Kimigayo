// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class StorageSignatureValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.RefRemainder, 0)]
    [InlineData(KimiDeclarationId.RefRemainder, 1)]
    [InlineData(KimiDeclarationId.RefRemainder, 2)]
    [InlineData(KimiDeclarationId.UniqRemainder, 0)]
    [InlineData(KimiDeclarationId.UniqRemainder, 1)]
    [InlineData(KimiDeclarationId.UniqRemainder, 2)]
    [InlineData(KimiDeclarationId.OwnedRemainder, 0)]
    [InlineData(KimiDeclarationId.OwnedRemainder, 1)]
    [InlineData(KimiDeclarationId.OwnedRemainder, 2)]
    [InlineData(KimiDeclarationId.OwnedRemainder, 3)]
    public void RemainderFieldsRetainTheirBoundStorageTypes(KimiDeclarationId id, int index)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var declaration = (StructKoto)c.Library.GetSymbol(id)!.Declaration;
        var field = declaration.Members.OfType<VariableKoto>().ElementAt(index);
        field.BoundSymbol!.Type = BoundType.Primitives["u8"];
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.StorageBorrowShared)]
    [InlineData(KimiDeclarationId.StorageBorrowExclusive)]
    [InlineData(KimiDeclarationId.StorageOwn)]
    [InlineData(KimiDeclarationId.StorageLend)]
    [InlineData(KimiDeclarationId.StorageSplit)]
    [InlineData(KimiDeclarationId.StorageRelease)]
    public void EveryOperationRequiresItsOwnElementParameter(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = (FunctionKoto)symbol.Declaration;
        var parameter = function.Parameters[^1];
        var type = parameter.Type.BoundType!;
        parameter.Type.BoundType = ReplaceElement(type, function.GenericArguments[0].BoundType!);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.StorageBorrowShared)]
    [InlineData(KimiDeclarationId.StorageBorrowExclusive)]
    [InlineData(KimiDeclarationId.StorageOwn)]
    [InlineData(KimiDeclarationId.StorageLend)]
    [InlineData(KimiDeclarationId.StorageSplit)]
    public void ResultsRetainTheCompleteElementType(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var function = (FunctionKoto)symbol.Declaration;
        symbol.Type = ReplaceElement(symbol.Type!, function.GenericArguments[0].BoundType!);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(function, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.StorageBorrowShared)]
    [InlineData(KimiDeclarationId.StorageBorrowExclusive)]
    [InlineData(KimiDeclarationId.StorageLend)]
    [InlineData(KimiDeclarationId.StorageSplit)]
    public void ResultsCannotClaimStaticStorage(KimiDeclarationId id)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var type = symbol.Type!;
        symbol.Type = new(type.Name, type.Kind, type.Symbol, type.Semantics, type.Components.ToArray(), type.Length, type.Origin is null ? null : BoundOrigin.Static, type.Origin is null ? [BoundOrigin.Static] : type.OriginArguments.ToArray(), type.LengthExpression);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
    }

    [Fact]
    public void OwningRemainderMustKeepItsDestructor()
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var declaration = (StructKoto)c.Library.GetSymbol(KimiDeclarationId.OwnedRemainder)!.Declaration;
        declaration.Members.OfType<FunctionKoto>().Single(x => x.IsDestructor).IsDestructor = false;
        Assert.False(c.Library.ValidateDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    private static BoundType ReplaceElement(BoundType type, BoundType element)
        => ReferenceEquals(type, element) ? BoundType.Primitives["u8"] : new(type.Name, type.Kind, type.Symbol, type.Semantics, type.Components.Select(x => ReplaceElement(x, element)).ToArray(), type.Length, type.Origin, type.OriginArguments.ToArray(), type.LengthExpression);
}
