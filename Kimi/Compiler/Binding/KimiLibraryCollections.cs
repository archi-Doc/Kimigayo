// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 4.7.2/4.7.4: one complete bound signature per compiler-managed Array operation.
    // Element denotes this declaration's parameter identity, never a type accepted by its spelling.
    private static readonly CollectionSignature[] CollectionSignatures =
    [
        new(KimiDeclarationId.ArrayWithCapacity, CollectionType.Unit, [CollectionType.ISize]),
        new(KimiDeclarationId.ArrayReserve, CollectionType.Unit, [CollectionType.Receiver, CollectionType.ISize]),
        new(KimiDeclarationId.ArrayAppend, CollectionType.Unit, [CollectionType.Receiver, CollectionType.Element]),
        new(KimiDeclarationId.ArrayInsert, CollectionType.Unit, [CollectionType.Receiver, CollectionType.ISize, CollectionType.Element]),
        new(KimiDeclarationId.ArrayInsertIndex, CollectionType.Unit, [CollectionType.Receiver, CollectionType.Index, CollectionType.Element]),
        new(KimiDeclarationId.ArrayPop, CollectionType.OptionElement, [CollectionType.Receiver]),
        new(KimiDeclarationId.ArrayRemove, CollectionType.Element, [CollectionType.Receiver, CollectionType.ISize]),
        new(KimiDeclarationId.ArrayRemoveIndex, CollectionType.Element, [CollectionType.Receiver, CollectionType.Index]),
        new(KimiDeclarationId.ArrayClear, CollectionType.Unit, [CollectionType.Receiver]),
        new(KimiDeclarationId.ArraySwap, CollectionType.Unit, [CollectionType.Receiver, CollectionType.ISize, CollectionType.ISize]),
        new(KimiDeclarationId.ArrayShrinkToFit, CollectionType.Unit, [CollectionType.Receiver]),
    ];

    private enum CollectionType : byte
    {
        Unit,
        ISize,
        Index,
        Receiver,
        Element,
        OptionElement,
    }

    private bool ValidBoundCollectionOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (id is not (>= KimiDeclarationId.ArrayReserve and <= KimiDeclarationId.ArrayRemoveIndex or KimiDeclarationId.ArraySwap or KimiDeclarationId.ArrayWithCapacity))
        {
            return true;
        }

        if (symbol.Declaration is not FunctionKoto function || this.GetSymbol(KimiDeclarationId.Array)?.Schema?.GenericSlots is not [var slot] || slot.Symbol.Type is not { } element)
        {
            return false;
        }

        foreach (var signature in CollectionSignatures)
        {
            if (signature.Id != id)
            {
                continue;
            }

            if (function.Parameters.Count != signature.Inputs.Length || !this.ValidBoundCollectionType(symbol.Type, signature.Result, function, element))
            {
                return false;
            }

            for (var i = 0; i < signature.Inputs.Length; i++)
            {
                if (!this.ValidBoundCollectionType(function.Parameters[i].Type.BoundType, signature.Inputs[i], function, element))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private bool ValidBoundCollectionType(BoundType? type, CollectionType expected, FunctionKoto function, BoundType element)
        => expected switch
        {
            CollectionType.Unit => ReferenceEquals(type, BoundType.Unit),
            CollectionType.ISize => ReferenceEquals(type, BoundType.ISize),
            CollectionType.Index => ReferenceEquals(type, this.GetSymbol(KimiDeclarationId.Index)?.Type),
            CollectionType.Element => ReferenceEquals(type, element),
            CollectionType.OptionElement => type is not null && this.BoundStorageContainer(type, KimiDeclarationId.Option, element, 0),
            CollectionType.Receiver => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Symbol: null, OriginArguments.Count: 0, Components: [var array], Origin.Slot: 0 } &&
                StorageInputOrigin(type.Origin, function) && this.BoundStorageContainer(array, KimiDeclarationId.Array, element, 0),
            _ => false,
        };

    private readonly record struct CollectionSignature(KimiDeclarationId Id, CollectionType Result, CollectionType[] Inputs);
}
