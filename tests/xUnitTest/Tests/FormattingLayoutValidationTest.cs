// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class FormattingLayoutValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.FixedBuffer, 4)]
    [InlineData(KimiDeclarationId.HeapBuffer, 4)]
    [InlineData(KimiDeclarationId.WriteWindow, 4)]
    [InlineData(KimiDeclarationId.Utf8Writer, 8)]
    [InlineData(KimiDeclarationId.Utf8Slice, 1)]
    public void EveryRuntimeFieldRequiresItsCompleteBoundType(KimiDeclarationId id, int count)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var declaration = (StructKoto)c.Library.GetSymbol(id)!.Declaration;
        for (var index = 0; index < count; index++)
        {
            var field = declaration.Members.OfType<VariableKoto>().ElementAt(index);
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
    [InlineData(KimiDeclarationId.FixedBuffer)]
    [InlineData(KimiDeclarationId.WriteWindow)]
    [InlineData(KimiDeclarationId.Utf8Writer)]
    [InlineData(KimiDeclarationId.Utf8Slice)]
    public void AdapterOriginAuthorityAndVarianceAreFixed(KimiDeclarationId id)
    {
        foreach (var variance in new[] { false, true })
        {
            var c = Compilation.CreateForTest();
            Assert.True(c.Bind().IsComplete);
            var symbol = c.Library.GetSymbol(id)!;
            var origin = Assert.Single(symbol.Schema!.Origins);
            if (variance)
            {
                origin.Variance = OriginVariance.Invariant;
            }
            else
            {
                origin.LoanRequirement = LoanRequirement.None;
            }

            Assert.False(c.Library.ValidateBoundDeclarations());
            Assert.Same(symbol.Declaration, c.Library.InvalidDeclaration);
        }
    }

    [Theory]
    [InlineData("element")]
    [InlineData("static")]
    [InlineData("foreign")]
    public void Utf8ViewRetainsByteElementsAndItsOwnSource(string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var declaration = (StructKoto)c.Library.GetSymbol(KimiDeclarationId.Utf8Slice)!.Declaration;
        var field = declaration.Members.OfType<VariableKoto>().First();
        var original = field.BoundSymbol!.Type!;
        var origin = mutation == "static" ? BoundOrigin.Static : mutation == "foreign" ? c.Library.GetSymbol(KimiDeclarationId.FixedBuffer)!.Schema!.Origins[0].Origin : original.Origin;
        field.TypeKoto!.BoundType = field.BoundSymbol.Type = new(original.Name, original.Kind, original.Symbol, original.Semantics, [mutation == "element" ? BoundType.ISize : BoundType.Primitives["u8"]], origin: origin);
        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }
}
