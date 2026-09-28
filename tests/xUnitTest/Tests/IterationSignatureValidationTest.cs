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

    [Theory]
    [InlineData(KimiDeclarationId.Iterable, "receiver-mode")]
    [InlineData(KimiDeclarationId.Iterable, "receiver-origin")]
    [InlineData(KimiDeclarationId.Iterable, "receiver-self")]
    [InlineData(KimiDeclarationId.Iterable, "result-family")]
    [InlineData(KimiDeclarationId.Iterable, "result-origin")]
    [InlineData(KimiDeclarationId.Iterable, "result-self")]
    [InlineData(KimiDeclarationId.Iterable, "formation-mode")]
    [InlineData(KimiDeclarationId.Iterable, "formation-origin")]
    [InlineData(KimiDeclarationId.Iterable, "required-contract")]
    [InlineData(KimiDeclarationId.Iterable, "requirement-identity")]
    [InlineData(KimiDeclarationId.Iterable, "family-identity")]
    [InlineData(KimiDeclarationId.UniqIterable, "receiver-mode")]
    [InlineData(KimiDeclarationId.UniqIterable, "result-origin")]
    [InlineData(KimiDeclarationId.UniqIterable, "formation-mode")]
    [InlineData(KimiDeclarationId.UniqIterable, "formation-origin")]
    [InlineData(KimiDeclarationId.UniqIterable, "required-contract")]
    [InlineData(KimiDeclarationId.IntoIterable, "receiver-self")]
    [InlineData(KimiDeclarationId.IntoIterable, "result-family")]
    [InlineData(KimiDeclarationId.IntoIterable, "result-self")]
    [InlineData(KimiDeclarationId.IntoIterable, "required-contract")]
    [InlineData(KimiDeclarationId.IntoIterable, "requirement-identity")]
    public void EntryKeepsItsCompleteFamilyContract(KimiDeclarationId id, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var symbol = c.Library.GetSymbol(id)!;
        var declaration = (ContractKoto)symbol.Declaration;
        var clause = declaration.ConstraintNodes[0];
        var entry = (FunctionKoto)declaration.Members[0];
        var receiver = entry.Parameters[0].Type;
        var original = receiver.BoundType!;
        var result = entry.BoundSymbol!.Type!;
        var formation = clause.FormationType;
        var foreignFamily = c.Library.LendingIterator.Contract!.AssociatedTypes[0];
        var foreignConstraint = ((ContractKoto)c.Library.GetSymbol(KimiDeclarationId.Iterator)!.Declaration).ConstraintNodes[0].BoundConstraint;
        switch (mutation)
        {
            case "receiver-mode":
                receiver.BoundType = Copy(original, semantics: original.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "receiver-origin":
                receiver.BoundType = Copy(original, origin: BoundOrigin.Static);
                break;
            case "receiver-self":
                receiver.BoundType = id == KimiDeclarationId.IntoIterable ? BoundType.ISize : Copy(original, components: [BoundType.ISize]);
                break;
            case "result-family":
                entry.ReturnType!.BoundType = entry.BoundSymbol.Type = Copy(result, symbol: foreignFamily);
                break;
            case "result-origin":
                entry.ReturnType!.BoundType = entry.BoundSymbol.Type = Copy(result, origins: [formation!.BoundType!.Origin!]);
                break;
            case "result-self":
                entry.ReturnType!.BoundType = entry.BoundSymbol.Type = Copy(result, components: [BoundType.ISize]);
                break;
            case "formation-mode":
                formation!.BoundType = Copy(formation.BoundType!, semantics: formation.BoundType!.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "formation-origin":
                formation!.BoundType = Copy(formation.BoundType!, origin: original.Origin);
                break;
            case "required-contract":
                clause.BoundConstraint = foreignConstraint;
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

    [Theory]
    [InlineData("base-identity")]
    [InlineData("item-identity")]
    [InlineData("refined-family")]
    [InlineData("refined-step")]
    [InlineData("refined-item")]
    [InlineData("refined-identity")]
    public void IteratorFixesTheLentItemFamilyToItem(string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var symbol = c.Library.GetSymbol(KimiDeclarationId.Iterator)!;
        var declaration = (ContractKoto)symbol.Declaration;
        var refinement = declaration.ConstraintNodes[0];
        var item = (SyntaxFormKoto)declaration.Members[0];
        var identity = refinement.BoundConstraint!;
        var into = (ContractKoto)c.Library.GetSymbol(KimiDeclarationId.IntoIterable)!.Declaration;
        switch (mutation)
        {
            case "base-identity":
                declaration.Bases[0].BoundSymbol = c.Library.GetSymbol(KimiDeclarationId.Iterable);
                break;
            case "item-identity":
                item.BoundSymbol = into.ConstraintNodes[0].BoundSymbol;
                break;
            case "refined-family":
                refinement.BoundSymbol = into.ConstraintNodes[0].BoundSymbol;
                break;
            case "refined-step":
                refinement.BoundConstraint = new(new ConstraintKey(identity.Kind, Copy(identity.Subject!, origins: [BoundOrigin.Static]), identity.RequiredType));
                break;
            case "refined-item":
                refinement.BoundConstraint = new(new ConstraintKey(identity.Kind, identity.Subject, Copy(identity.RequiredType!, symbol: into.ConstraintNodes[0].BoundSymbol)));
                break;
            case "refined-identity":
                refinement.BoundConstraint = into.ConstraintNodes[0].BoundConstraint;
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BindingSymbol? symbol = null, BoundType[]? components = null, BoundOrigin? origin = null, BoundOrigin[]? origins = null)
        => new(type.Name, type.Kind, symbol ?? type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, origins ?? type.OriginArguments.ToArray(), type.LengthExpression);
}
