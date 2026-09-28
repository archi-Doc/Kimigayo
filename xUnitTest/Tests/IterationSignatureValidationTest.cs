// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class IterationSignatureValidationTest
{
    [Theory]
    [InlineData("receiver-mode")]
    [InlineData("receiver-self")]
    [InlineData("receiver-origin")]
    [InlineData("result-element")]
    [InlineData("result-origin")]
    [InlineData("result-family")]
    [InlineData("result-self")]
    [InlineData("formation-mode")]
    [InlineData("formation-origin")]
    [InlineData("formation-self")]
    [InlineData("requirement-identity")]
    [InlineData("family-identity")]
    public void LendingStepKeepsItsCompleteFamilyContract(string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        var symbol = c.Library.LendingIterator;
        var declaration = (ContractKoto)symbol.Declaration;
        var family = (SyntaxFormKoto)declaration.Members[0];
        var next = (FunctionKoto)declaration.Members[1];
        var receiver = next.Parameters[0].Type;
        var original = receiver.BoundType!;
        var result = next.BoundSymbol!.Type!;
        var item = result.Components[0];
        var formation = family.Operands[1];
        var foreignFamily = c.Library.GetSymbol(KimiDeclarationId.UniqIterable)!.Contract!.AssociatedTypes[0];
        switch (mutation)
        {
            case "receiver-mode":
                receiver.BoundType = Copy(original, semantics: SemanticsKind.Ref);
                break;
            case "receiver-self":
                receiver.BoundType = Copy(original, components: [BoundType.ISize]);
                break;
            case "receiver-origin":
                receiver.BoundType = Copy(original, origin: BoundOrigin.Static);
                break;
            case "result-element":
                next.ReturnType!.BoundType = next.BoundSymbol.Type = Copy(result, components: [BoundType.ISize]);
                break;
            case "result-origin":
                next.ReturnType!.BoundType = next.BoundSymbol.Type = Copy(result, components: [Copy(item, origins: [formation.BoundType!.Origin!])]);
                break;
            case "result-family":
                next.ReturnType!.BoundType = next.BoundSymbol.Type = Copy(result, components: [Copy(item, symbol: foreignFamily)]);
                break;
            case "result-self":
                next.ReturnType!.BoundType = next.BoundSymbol.Type = Copy(result, components: [Copy(item, components: [BoundType.ISize])]);
                break;
            case "formation-mode":
                formation.BoundType = Copy(formation.BoundType!, semantics: SemanticsKind.Ref);
                break;
            case "formation-origin":
                formation.BoundType = Copy(formation.BoundType!, origin: original.Origin);
                break;
            case "formation-self":
                formation.BoundType = Copy(formation.BoundType!, components: [BoundType.ISize]);
                break;
            case "requirement-identity":
                symbol.Contract!.RequirementStorage[0] = c.Library.GetSymbol(KimiDeclarationId.WriteLine)!;
                break;
            case "family-identity":
                symbol.Contract!.AssociatedStorage[0] = foreignFamily;
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BindingSymbol? symbol = null, BoundType[]? components = null, BoundOrigin? origin = null, BoundOrigin[]? origins = null)
        => new(type.Name, type.Kind, symbol ?? type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, origins ?? type.OriginArguments.ToArray(), type.LengthExpression);
}
