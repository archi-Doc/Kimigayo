// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler;
using Kimi.Compiler.Parsing;
using Xunit;

namespace XunitTest;

public class ContractSignatureValidationTest
{
    [Theory]
    [InlineData(KimiDeclarationId.Equatable, "self-mode")]
    [InlineData(KimiDeclarationId.Equatable, "other-origin")]
    [InlineData(KimiDeclarationId.Equatable, "other-type")]
    [InlineData(KimiDeclarationId.Equatable, "result")]
    [InlineData(KimiDeclarationId.Equatable, "requirement-identity")]
    [InlineData(KimiDeclarationId.Comparable, "self-mode")]
    [InlineData(KimiDeclarationId.Comparable, "other-origin")]
    [InlineData(KimiDeclarationId.Comparable, "result")]
    [InlineData(KimiDeclarationId.Comparable, "requirement-identity")]
    [InlineData(KimiDeclarationId.Comparable, "inherited-identity")]
    [InlineData(KimiDeclarationId.Comparable, "base-identity")]
    public void ComparisonBorrowsTwoValuesOfSelf(KimiDeclarationId id, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var symbol = c.Library.GetSymbol(id)!;
        var declaration = (ContractKoto)symbol.Declaration;
        var function = (FunctionKoto)declaration.Members[0];
        var self = function.Parameters[0].Type;
        var other = function.Parameters[1].Type;
        var requirements = symbol.Contract!.RequirementStorage;
        switch (mutation)
        {
            case "self-mode":
                self.BoundType = Copy(self.BoundType!, semantics: SemanticsKind.Uniq);
                break;
            case "other-origin":
                other.BoundType = Copy(other.BoundType!, origin: self.BoundType!.Origin);
                break;
            case "other-type":
                other.BoundType = Copy(other.BoundType!, components: [BoundType.ISize]);
                break;
            case "result":
                function.BoundSymbol!.Type = id == KimiDeclarationId.Equatable ? BoundType.Primitives["i32"] : BoundType.Boolean;
                break;
            case "requirement-identity":
                requirements[^1] = requirements[^1] with { Symbol = c.Library.GetSymbol(KimiDeclarationId.WriteLine)! };
                break;
            case "inherited-identity":
                requirements[0] = requirements[0] with { Symbol = c.Library.GetSymbol(KimiDeclarationId.WriteLine)! };
                break;
            case "base-identity":
                declaration.Bases[0].BoundSymbol = c.Library.GetSymbol(KimiDeclarationId.Iterable);
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    [Theory]
    [InlineData(KimiDeclarationId.Indexable, "receiver-mode")]
    [InlineData(KimiDeclarationId.Indexable, "receiver-key")]
    [InlineData(KimiDeclarationId.Indexable, "key-mode")]
    [InlineData(KimiDeclarationId.Indexable, "key-type")]
    [InlineData(KimiDeclarationId.Indexable, "result-mode")]
    [InlineData(KimiDeclarationId.Indexable, "result-origin")]
    [InlineData(KimiDeclarationId.Indexable, "result-element")]
    [InlineData(KimiDeclarationId.Indexable, "requirement-identity")]
    [InlineData(KimiDeclarationId.Indexable, "element-identity")]
    [InlineData(KimiDeclarationId.UniqIndexable, "receiver-mode")]
    [InlineData(KimiDeclarationId.UniqIndexable, "receiver-key")]
    [InlineData(KimiDeclarationId.UniqIndexable, "key-mode")]
    [InlineData(KimiDeclarationId.UniqIndexable, "result-mode")]
    [InlineData(KimiDeclarationId.UniqIndexable, "result-origin")]
    [InlineData(KimiDeclarationId.UniqIndexable, "result-element")]
    [InlineData(KimiDeclarationId.UniqIndexable, "requirement-identity")]
    [InlineData(KimiDeclarationId.UniqIndexable, "parent-key")]
    public void IndexingPublishesTheElementPlaceOfTheReceiver(KimiDeclarationId id, string mutation)
    {
        var c = Compilation.CreateForTest();
        Assert.True(c.Bind().IsComplete);
        Assert.True(c.Library.ValidateBoundDeclarations());
        var symbol = c.Library.GetSymbol(id)!;
        var declaration = (ContractKoto)symbol.Declaration;
        var function = (FunctionKoto)declaration.Members[^1];
        var receiver = function.Parameters[0].Type;
        var key = function.Parameters[1].Type;
        var result = function.BoundSymbol!.Type!;
        var foreignFamily = c.Library.LendingIterator.Contract!.AssociatedTypes[0];
        switch (mutation)
        {
            case "receiver-mode":
                receiver.BoundType = Copy(receiver.BoundType!, semantics: receiver.BoundType!.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "receiver-key":
                receiver.BoundType = Copy(receiver.BoundType!, components: [Copy(receiver.BoundType!.Components[0], components: [BoundType.ISize])]);
                break;
            case "key-mode":
                key.BoundType = Copy(key.BoundType!, semantics: SemanticsKind.Uniq);
                break;
            case "key-type":
                key.BoundType = Copy(key.BoundType!, components: [BoundType.ISize]);
                break;
            case "result-mode":
                function.BoundSymbol.Type = Copy(result, semantics: result.Semantics == SemanticsKind.Ref ? SemanticsKind.Uniq : SemanticsKind.Ref);
                break;
            case "result-origin":
                function.BoundSymbol.Type = Copy(result, origin: key.BoundType!.Origin);
                break;
            case "result-element":
                function.BoundSymbol.Type = Copy(result, components: [Copy(result.Components[0], symbol: foreignFamily)]);
                break;
            case "requirement-identity":
                symbol.Contract!.RequirementStorage[^1] = symbol.Contract.RequirementStorage[^1] with { Symbol = c.Library.GetSymbol(KimiDeclarationId.WriteLine)! };
                break;
            case "element-identity":
                symbol.Contract!.AssociatedStorage[0] = foreignFamily;
                break;
            case "parent-key":
                var parent = symbol.Contract!.Ancestors.Single(a => ReferenceEquals(a.Declaration, c.Library.GetSymbol(KimiDeclarationId.Indexable)!.Declaration));
                parent.Type = Copy(parent.Type!, components: [BoundType.ISize]);
                break;
        }

        Assert.False(c.Library.ValidateBoundDeclarations());
        Assert.Same(declaration, c.Library.InvalidDeclaration);
    }

    private static BoundType Copy(BoundType type, SemanticsKind? semantics = null, BindingSymbol? symbol = null, BoundType[]? components = null, BoundOrigin? origin = null)
        => new(type.Name, type.Kind, symbol ?? type.Symbol, semantics ?? type.Semantics, components ?? type.Components.ToArray(), type.Length, origin ?? type.Origin, type.OriginArguments.ToArray(), type.LengthExpression);
}
