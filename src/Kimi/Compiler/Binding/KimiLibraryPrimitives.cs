// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 15.7, 13.5.8 and 22.4: signatures of whole-value updates, object creation and basic I/O.
    private static readonly PrimitiveSignature?[] PrimitiveSignatures = IndexSignatures<PrimitiveSignature>(
    [
        new(KimiDeclarationId.Replace, PrimitiveType.Unit, [new(PrimitiveType.UniqParameter, "target"), new(PrimitiveType.Parameter, "value", "with", Positional: false)], Generic: true),
        new(KimiDeclarationId.Exchange, PrimitiveType.Parameter, [new(PrimitiveType.UniqParameter, "target"), new(PrimitiveType.Parameter, "value", "with", Positional: false)], Generic: true),
        new(KimiDeclarationId.Swap, PrimitiveType.Unit, [new(PrimitiveType.UniqParameter, "first"), new(PrimitiveType.UniqParameter, "second")], Generic: true),
        new(KimiDeclarationId.MakeObj, PrimitiveType.ObjectParameter, [new(PrimitiveType.Parameter, "value")], Generic: true),
        new(KimiDeclarationId.MakeRc, PrimitiveType.RcParameter, [new(PrimitiveType.Parameter, "value")], Generic: true),
        new(KimiDeclarationId.MakeArc, PrimitiveType.ArcParameter, [new(PrimitiveType.Parameter, "value")], Generic: true),
        new(KimiDeclarationId.WriteLine, PrimitiveType.Unit, [new(PrimitiveType.RefString, "text")]),
        new(KimiDeclarationId.TestTempDirectory, PrimitiveType.String, []),
    ],
    static signature => signature.Id);

    private enum PrimitiveType : byte
    {
        Unit,
        String,
        RefString,
        Parameter,
        UniqParameter,
        ObjectParameter,
        RcParameter,
        ArcParameter,
    }

    private static bool BoundPrimitiveType(BoundType? type, PrimitiveType expected, FunctionKoto function, BoundType? element, int input = -1)
        => expected switch
        {
            PrimitiveType.Unit => ReferenceEquals(type, BoundType.Unit),
            PrimitiveType.String => ReferenceEquals(type, BoundType.String),
            PrimitiveType.RefString => BoundInputBorrow(type, BoundType.String, SemanticsKind.Ref, function, input),
            PrimitiveType.Parameter => element is not null && ReferenceEquals(type, element),
            PrimitiveType.UniqParameter => element is not null && BoundInputBorrow(type, element, SemanticsKind.Uniq, function, input),
            PrimitiveType.ObjectParameter or PrimitiveType.RcParameter or PrimitiveType.ArcParameter => element is not null &&
                type is { Kind: BoundTypeKind.Semantics, Symbol: null, Origin: null, OriginArguments.Count: 0, Components: [var payload] } && ReferenceEquals(payload, element) &&
                type.Semantics == PrimitiveObjectSemantics(expected),
            _ => false,
        };

    private static bool ValidBoundPrimitive(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (PrimitiveSignatures[KimiLibraryCatalog.Index(id)] is not { } signature)
        {
            return true;
        }

        if (symbol.Declaration is not FunctionKoto function || function.Parameters.Count != signature.Inputs.Length ||
            function.GenericArguments.Count != (signature.Generic ? 1 : 0))
        {
            return false;
        }

        var element = signature.Generic ? function.GenericArguments[0].BoundType : null;
        if ((signature.Generic && (element is not { Kind: BoundTypeKind.Parameter } || !ReferenceEquals(element.Symbol, function.GenericArguments[0].BoundSymbol))) ||
            !BoundPrimitiveType(symbol.Type, signature.Result, function, element))
        {
            return false;
        }

        for (var i = 0; i < signature.Inputs.Length; i++)
        {
            if (!BoundPrimitiveType(function.Parameters[i].Type.BoundType, signature.Inputs[i].Type, function, element, i))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PrimitiveSyntaxType(Koto? node, PrimitiveType expected)
        => node is TypeSemanticsKoto { OriginName: null, OriginExpression: null, OriginArguments: null, SemanticsParameter: null } type && expected switch
        {
            PrimitiveType.String or PrimitiveType.Parameter => type is { SemanticsKind: SemanticsKind.Owner, Type: null } && type.Identifier == (expected == PrimitiveType.String ? "string" : "T"),
            PrimitiveType.RefString => type.SemanticsKind == SemanticsKind.Ref && PrimitiveSyntaxType(type.Type, PrimitiveType.String),
            PrimitiveType.UniqParameter => type.SemanticsKind == SemanticsKind.Uniq && PrimitiveSyntaxType(type.Type, PrimitiveType.Parameter),
            PrimitiveType.ObjectParameter or PrimitiveType.RcParameter or PrimitiveType.ArcParameter => type.SemanticsKind == PrimitiveObjectSemantics(expected) && PrimitiveSyntaxType(type.Type, PrimitiveType.Parameter),
            _ => false,
        };

    private static SemanticsKind PrimitiveObjectSemantics(PrimitiveType type)
        => type == PrimitiveType.ObjectParameter ? SemanticsKind.Obj : type == PrimitiveType.RcParameter ? SemanticsKind.Rc : SemanticsKind.Arc;

    private bool ValidPrimitive(BindingSymbol symbol, KimiDeclarationId id)
    {
        var index = KimiLibraryCatalog.Index(id);
        ref readonly var rule = ref KimiLibraryCatalog.Entries[index];
        var signature = PrimitiveSignatures[index]!.Value;
        var scope = rule.Container == KimiLibraryContainer.Console ? this.ConsoleScope : rule.Container == KimiLibraryContainer.Test ? this.TestScope : this.IntrinsicsScope;
        var factory = id is KimiDeclarationId.MakeObj or KimiDeclarationId.MakeRc or KimiDeclarationId.MakeArc;
        if (symbol.CompilerFunction != rule.Function || !ReferenceEquals(symbol.Scope, scope) || !ReferenceEquals(symbol.Declaration.Parent, scope.Owner) ||
            symbol.Declaration is not FunctionKoto function || function.Name != rule.Name || function.Modifier != ModifierKind.Public || function.AttributeChain is not null ||
            function.GenericArguments.Count != (signature.Generic ? 1 : 0) ||
            (signature.Generic && function.GenericArguments[0] is not GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null }) ||
            function.Origins.Count != 0 || function.Parameters.Count != signature.Inputs.Length || function.TypeConstraints.Count != (factory ? 1 : 0) ||
            function.Body is not null || function.ExpressionBody is not null || function.IsRequirement || function.IsGenerated || function.IsSpecialization ||
            ((factory || id == KimiDeclarationId.WriteLine) && function.NameBoundaryIndex >= 0))
        {
            return false;
        }

        if (factory && (function.TypeConstraints[0] is not IsKoto { IsNegated: false, IsAssociatedConstraint: false, FormationType: null, AttributeChain: null, Left: { } constrained, Right: { } required } ||
            !BareName(constrained, "T") || !BareName(required, "ObjectPayload")))
        {
            return false;
        }

        for (var i = 0; i < signature.Inputs.Length; i++)
        {
            var expected = signature.Inputs[i];
            var parameter = function.Parameters[i];
            if (parameter.InternalName != expected.Name || parameter.ExternalName != (expected.ExternalName ?? expected.Name) ||
                function.AllowsPositionalArgument(i) != expected.Positional || parameter.DefaultValue is not null || parameter.AttributeChain is not null ||
                !PrimitiveSyntaxType(parameter.Type, expected.Type))
            {
                return false;
            }
        }

        return signature.Result == PrimitiveType.Unit ? (signature.Generic ? function.ReturnType is TupleTypeKoto { ElementNodes.Count: 0 } : function.ReturnType is null) :
            PrimitiveSyntaxType(function.ReturnType, signature.Result);
    }

    private readonly record struct PrimitiveInput(PrimitiveType Type, string Name, string? ExternalName = null, bool Positional = true);

    private readonly record struct PrimitiveSignature(KimiDeclarationId Id, PrimitiveType Result, PrimitiveInput[] Inputs, bool Generic = false);
}
