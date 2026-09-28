// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class RecordLayoutValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.Index, 2)]
    [InlineData(KimiDeclarationId.IndexRange, 3)]
    [InlineData(KimiDeclarationId.ResolvedRange, 2)]
    [InlineData(KimiDeclarationId.Range, 3)]
    public void EveryConstructedFieldRequiresItsBoundType(KimiDeclarationId id, int count)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var declaration = (StructKoto)c.Library.GetSymbol(id)!.Declaration;
        var fields = declaration.Members.OfType<VariableKoto>().Where(f => f is not PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed }).ToArray();
        Assert.Equal(count, fields.Length);
        foreach (var field in fields)
        {
            var original = field.BoundSymbol!.Type!;
            field.TypeKoto!.BoundType = field.BoundSymbol.Type = ReferenceEquals(original, BoundType.Boolean) ? BoundType.ISize : BoundType.Boolean;
            Assert.False(c.Library.ValidateBoundDeclarations());
            Assert.Same(declaration, c.Library.InvalidDeclaration);
            field.TypeKoto.BoundType = field.BoundSymbol.Type = original;
            Assert.True(c.Library.ValidateDeclarations());
            Assert.True(c.Library.ValidateBoundDeclarations());
        }
    }

    [Theory]
    [InlineData(KimiDeclarationId.Option, 0, "payload")]
    [InlineData(KimiDeclarationId.Option, 0, "ordinal")]
    [InlineData(KimiDeclarationId.Option, 1, "ordinal")]
    [InlineData(KimiDeclarationId.Option, 0, "owner")]
    [InlineData(KimiDeclarationId.Result, 0, "payload")]
    [InlineData(KimiDeclarationId.Result, 1, "payload")]
    [InlineData(KimiDeclarationId.Result, 1, "swapped")]
    [InlineData(KimiDeclarationId.Result, 1, "ordinal")]
    [InlineData(KimiDeclarationId.Result, 1, "owner")]
    public void CasesKeepTheirOrderAndPayloadParameters(KimiDeclarationId id, int ordinal, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.GetSymbol(id)!;
        var declaration = (EnumKoto)symbol.Declaration;
        var form = (SyntaxFormKoto)declaration.Members[(id == KimiDeclarationId.Option ? 1 : 0) + ordinal];
        var bound = form.BoundSymbol!.EnumCase!;
        switch (mutation)
        {
            case "payload":
                bound.Payload[0].BoundType = BoundType.ISize;
                break;
            case "swapped":
                bound.Payload[0].BoundType = declaration.GenericParameterNodes[0].BoundType;
                break;
            case "ordinal":
                bound.Ordinal = 1 - ordinal;
                break;
            case "owner":
                bound.Owner = c.Library.GetSymbol(id == KimiDeclarationId.Option ? KimiDeclarationId.Result : KimiDeclarationId.Option)!;
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }
}
