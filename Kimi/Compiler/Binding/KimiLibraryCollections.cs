// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 4.7.2-4: one complete bound signature per compiler-managed collection operation.
    // Element denotes T or K, and Value denotes V, by this declaration's parameter identities.
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
        new(KimiDeclarationId.DictionaryReserve, CollectionType.Unit, [CollectionType.Receiver, CollectionType.ISize]),
        new(KimiDeclarationId.DictionaryTryInsert, CollectionType.ResultPair, [CollectionType.Receiver, CollectionType.Element, CollectionType.Value]),
        new(KimiDeclarationId.DictionaryInsertOrReplace, CollectionType.OptionValue, [CollectionType.Receiver, CollectionType.Element, CollectionType.Value]),
        new(KimiDeclarationId.DictionaryRemove, CollectionType.OptionPair, [CollectionType.Receiver, CollectionType.BorrowedKey]),
        new(KimiDeclarationId.DictionaryTryGet, CollectionType.OptionBorrowedValue, [CollectionType.SharedReceiver, CollectionType.BorrowedKey]),
        new(KimiDeclarationId.DictionaryClear, CollectionType.Unit, [CollectionType.Receiver]),
        new(KimiDeclarationId.DictionaryShrinkToFit, CollectionType.Unit, [CollectionType.Receiver]),
    ];

    private enum CollectionType : byte
    {
        Unit,
        ISize,
        Index,
        Receiver,
        Element,
        OptionElement,
        Value,
        OptionValue,
        OptionPair,
        ResultPair,
        SharedReceiver,
        BorrowedKey,
        OptionBorrowedValue,
    }

    private static bool CollectionBorrow(BoundType? type, BoundType element, FunctionKoto function, int input)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Symbol: null, OriginArguments.Count: 0, Components: [var referent], Origin: { Kind: OriginKind.Input } origin } &&
            ReferenceEquals(referent, element) && ReferenceEquals(origin.Binder, function) && origin.InputIndex == input && origin.Slot == input &&
            ReferenceEquals(origin, function.Parameters[input].Type.BoundType?.Origin);

    private static bool CollectionPair(BoundType? type, BoundType key, BoundType? value)
        => type is { Kind: BoundTypeKind.Tuple, Semantics: SemanticsKind.Owner, Symbol: null, Origin: null, OriginArguments.Count: 0, Components: [var first, var second] } &&
            ReferenceEquals(first, key) && ReferenceEquals(second, value);

    private bool ValidBoundCollectionOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var dictionary = id is >= KimiDeclarationId.DictionaryReserve and <= KimiDeclarationId.DictionaryShrinkToFit;
        if (!dictionary && id is not (>= KimiDeclarationId.ArrayReserve and <= KimiDeclarationId.ArrayRemoveIndex or KimiDeclarationId.ArraySwap or KimiDeclarationId.ArrayWithCapacity))
        {
            return true;
        }

        if (symbol.Declaration is not FunctionKoto function ||
            this.GetSymbol(dictionary ? KimiDeclarationId.Dictionary : KimiDeclarationId.Array)?.Schema?.GenericSlots is not { } slots ||
            slots.Count != (dictionary ? 2 : 1) || slots[0].Symbol.Type is not { } element || (dictionary && slots[1].Symbol.Type is null))
        {
            return false;
        }

        var value = dictionary ? slots[1].Symbol.Type : null;
        foreach (var signature in CollectionSignatures)
        {
            if (signature.Id != id)
            {
                continue;
            }

            if (function.Parameters.Count != signature.Inputs.Length || !this.ValidBoundCollectionType(symbol.Type, signature.Result, function, element, value))
            {
                return false;
            }

            for (var i = 0; i < signature.Inputs.Length; i++)
            {
                if (!this.ValidBoundCollectionType(function.Parameters[i].Type.BoundType, signature.Inputs[i], function, element, value))
                {
                    return false;
                }
            }

            return true;
        }

        return false;
    }

    private bool ValidBoundCollectionType(BoundType? type, CollectionType expected, FunctionKoto function, BoundType element, BoundType? value)
        => expected switch
        {
            CollectionType.Unit => ReferenceEquals(type, BoundType.Unit),
            CollectionType.ISize => ReferenceEquals(type, BoundType.ISize),
            CollectionType.Index => ReferenceEquals(type, this.GetSymbol(KimiDeclarationId.Index)?.Type),
            CollectionType.Element => ReferenceEquals(type, element),
            CollectionType.OptionElement => type is not null && this.BoundStorageContainer(type, KimiDeclarationId.Option, element, 0),
            CollectionType.Value => value is not null && ReferenceEquals(type, value),
            CollectionType.OptionValue => type is not null && value is not null && this.BoundStorageContainer(type, KimiDeclarationId.Option, value, 0),
            CollectionType.BorrowedKey => CollectionBorrow(type, element, function, 1),
            CollectionType.OptionPair => this.CollectionResult(type, KimiDeclarationId.Option) && type!.Components is [var pair] && CollectionPair(pair, element, value),
            CollectionType.ResultPair => this.CollectionResult(type, KimiDeclarationId.Result) && type!.Components is [var success, var pair] &&
                ReferenceEquals(success, BoundType.Unit) && CollectionPair(pair, element, value),
            CollectionType.OptionBorrowedValue => this.CollectionResult(type, KimiDeclarationId.Option) && type!.Components is [var borrowed] &&
                value is not null && CollectionBorrow(borrowed, value, function, 0),
            CollectionType.Receiver or CollectionType.SharedReceiver => type is { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components: [var storage], Origin.Slot: 0 } &&
                type.Semantics == (expected == CollectionType.SharedReceiver ? SemanticsKind.Ref : SemanticsKind.Uniq) &&
                StorageInputOrigin(type.Origin, function) && this.CollectionStorage(storage, element, value),
            _ => false,
        };

    private bool CollectionStorage(BoundType type, BoundType element, BoundType? value)
        => value is null ? this.BoundStorageContainer(type, KimiDeclarationId.Array, element, 0) :
            type is { Kind: BoundTypeKind.Dictionary, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var key, var stored] } &&
            ReferenceEquals(type.Symbol, this.GetSymbol(KimiDeclarationId.Dictionary)) && ReferenceEquals(key, element) && ReferenceEquals(stored, value);

    private bool CollectionResult(BoundType? type, KimiDeclarationId id)
        => type is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0 } && ReferenceEquals(type.Symbol, this.GetSymbol(id));

    private readonly record struct CollectionSignature(KimiDeclarationId Id, CollectionType Result, CollectionType[] Inputs);
}
