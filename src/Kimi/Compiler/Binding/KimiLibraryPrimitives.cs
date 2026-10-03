// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 15.7, 13.5.8 and 22.4: signatures of whole-value updates, object creation and basic I/O.
    private static readonly PrimitiveSignature[] PrimitiveSignatures =
    [
        new(KimiDeclarationId.Replace, PrimitiveType.Unit, [PrimitiveType.UniqParameter, PrimitiveType.Parameter], Generic: true),
        new(KimiDeclarationId.Exchange, PrimitiveType.Parameter, [PrimitiveType.UniqParameter, PrimitiveType.Parameter], Generic: true),
        new(KimiDeclarationId.Swap, PrimitiveType.Unit, [PrimitiveType.UniqParameter, PrimitiveType.UniqParameter], Generic: true),
        new(KimiDeclarationId.MakeObj, PrimitiveType.ObjectParameter, [PrimitiveType.Parameter], Generic: true),
        new(KimiDeclarationId.MakeRc, PrimitiveType.RcParameter, [PrimitiveType.Parameter], Generic: true),
        new(KimiDeclarationId.MakeArc, PrimitiveType.ArcParameter, [PrimitiveType.Parameter], Generic: true),
        new(KimiDeclarationId.WriteLine, PrimitiveType.Unit, [PrimitiveType.RefString]),
        new(KimiDeclarationId.TestTempDirectory, PrimitiveType.String, []),
    ];

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
                type.Semantics == (expected == PrimitiveType.ObjectParameter ? SemanticsKind.Obj : expected == PrimitiveType.RcParameter ? SemanticsKind.Rc : SemanticsKind.Arc),
            _ => false,
        };

    private static bool ValidBoundPrimitive(BindingSymbol symbol, KimiDeclarationId id)
    {
        foreach (var signature in PrimitiveSignatures)
        {
            if (signature.Id != id)
            {
                continue;
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
                if (!BoundPrimitiveType(function.Parameters[i].Type.BoundType, signature.Inputs[i], function, element, i))
                {
                    return false;
                }
            }

            return true;
        }

        return true; // Other catalog families have their own canonical signatures.
    }

    private readonly record struct PrimitiveSignature(KimiDeclarationId Id, PrimitiveType Result, PrimitiveType[] Inputs, bool Generic = false);
}
