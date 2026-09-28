// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // Canonical complete signatures over the declaration's own E. Compare the ordinary binder's
    // identities and Origins, not just the spelling of Array, E or the remainder names.
    private static readonly StorageSignature[] StorageSignatures =
    [
        new(KimiDeclarationId.StorageBorrowShared, KimiDeclarationId.Array, SemanticsKind.Ref, KimiDeclarationId.RefRemainder),
        new(KimiDeclarationId.StorageBorrowExclusive, KimiDeclarationId.Array, SemanticsKind.Uniq, KimiDeclarationId.UniqRemainder),
        new(KimiDeclarationId.StorageOwn, KimiDeclarationId.Array, SemanticsKind.Owner, KimiDeclarationId.OwnedRemainder),
        new(KimiDeclarationId.StorageLend, KimiDeclarationId.RefRemainder, SemanticsKind.Ref, Capability: true),
        new(KimiDeclarationId.StorageSplit, KimiDeclarationId.UniqRemainder, SemanticsKind.Uniq, Capability: true),
        new(KimiDeclarationId.StorageRelease, null, SemanticsKind.Unsafe),
    ];

    private static bool BoundStoragePointer(BoundType? type, BoundType element)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Unsafe, Symbol: null, Origin: null, OriginArguments.Count: 0, Components: [var pointee] } && ReferenceEquals(pointee, element);

    private static bool StorageInputOrigin(BoundOrigin? origin, FunctionKoto function)
        => origin is { Kind: OriginKind.Input, InputIndex: 0 } && ReferenceEquals(origin.Binder, function);

    // The boundary primitives write these records directly, so the ordinary bound field identities
    // must agree with the checked source layout as well as each operation's signature.
    private static bool ValidBoundRemainder(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (symbol.Declaration is not StructKoto { GenericParameterNodes: [var parameter] } declaration ||
            parameter.BoundType is not { Kind: BoundTypeKind.Parameter } element)
        {
            return false;
        }

        var count = id == KimiDeclarationId.OwnedRemainder ? 4 : 3;
        for (var i = 0; i < count; i++)
        {
            if (StorageField(declaration, i) is not { BoundSymbol.Type: { } type } field ||
                !ReferenceEquals(field.TypeKoto?.BoundType, type) ||
                (i == 0 ? !BoundStoragePointer(type, element) : !ReferenceEquals(type, BoundType.ISize)))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidBoundStorageOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (id is >= KimiDeclarationId.RefRemainder and <= KimiDeclarationId.OwnedRemainder)
        {
            return ValidBoundRemainder(symbol, id);
        }

        if (id is >= KimiDeclarationId.DictionaryRefRemainder and <= KimiDeclarationId.StorageLendValue or >= KimiDeclarationId.DictionaryUniqRemainder and <= KimiDeclarationId.StorageValueAt)
        {
            return this.ValidBoundDictionaryStorage(symbol, id);
        }

        if (id is < KimiDeclarationId.StorageBorrowShared or > KimiDeclarationId.StorageRelease)
        {
            return true;
        }

        foreach (var signature in StorageSignatures)
        {
            if (signature.Id == id)
            {
                return this.ValidBoundStorageOperation(symbol, signature);
            }
        }

        return true; // Other catalog families have their own contracts.
    }

    private bool ValidBoundStorageOperation(BindingSymbol symbol, in StorageSignature signature)
    {
        if (symbol.Declaration is not FunctionKoto { GenericArguments.Count: 1 } function ||
            function.GenericArguments[0].BoundType is not { Kind: BoundTypeKind.Parameter } element ||
            function.Parameters.Count != (signature.Capability ? 2 : 1) || symbol.Type is not { } result ||
            function.Parameters[0].Type.BoundType is not { } input)
        {
            return false;
        }

        if (signature.Parameter is not { } parameterId)
        {
            return BoundStoragePointer(input, element) && ReferenceEquals(result, BoundType.Unit);
        }

        var storage = input;
        if (signature.Semantics != SemanticsKind.Owner)
        {
            if (input.Kind != BoundTypeKind.Semantics || input.Semantics != signature.Semantics || input.Symbol is not null ||
                input.Components.Count != 1 || input.OriginArguments.Count != 0)
            {
                return false;
            }

            storage = input.Components[0];
        }

        if (!this.BoundStorageContainer(storage, parameterId, element, signature.Capability ? 1 : 0))
        {
            return false;
        }

        if (signature.Capability)
        {
            return StorageInputOrigin(input.Origin, function) && StorageInputOrigin(storage.OriginArguments[0], function) &&
                !ReferenceEquals(input.Origin, storage.OriginArguments[0]) &&
                BoundStoragePointer(function.Parameters[1].Type.BoundType, element) &&
                result.Kind == BoundTypeKind.Semantics && result.Semantics == signature.Semantics && result.Symbol is null &&
                result.Components is [var referent] && ReferenceEquals(referent, element) && result.OriginArguments.Count == 0 &&
                ReferenceEquals(result.Origin, storage.OriginArguments[0]);
        }

        var borrowing = signature.Semantics != SemanticsKind.Owner;
        return signature.Result is { } resultId && this.BoundStorageContainer(result, resultId, element, borrowing ? 1 : 0) &&
            (!borrowing || (input.Origin is { Kind: OriginKind.Parameter, Slot: 0 } origin && ReferenceEquals(origin.Binder, function) &&
                ReferenceEquals(origin, result.OriginArguments[0])));
    }

    // SPEC 22.1.2.5: the Dictionary family over the declaration's own K and V. The remainder's source slot is the shared
    // Dictionary Loan; lendKey and lendValue lend for that slot, never for the state borrow.
    private bool ValidBoundDictionaryStorage(BindingSymbol symbol, KimiDeclarationId id)
    {
        var u8 = BoundType.Primitives["u8"];
        var remainderId = id >= KimiDeclarationId.DictionaryOwnedRemainder ? KimiDeclarationId.DictionaryOwnedRemainder :
            id >= KimiDeclarationId.DictionaryUniqRemainder ? KimiDeclarationId.DictionaryUniqRemainder : KimiDeclarationId.DictionaryRefRemainder;
        if (id is KimiDeclarationId.DictionaryRefRemainder or KimiDeclarationId.DictionaryUniqRemainder or KimiDeclarationId.DictionaryOwnedRemainder)
        {
            var fields = id == KimiDeclarationId.DictionaryOwnedRemainder ? 5 : 4;
            if (symbol.Declaration is not StructKoto declaration || StorageField(declaration, fields) is not null)
            {
                return false;
            }

            for (var i = 0; i < fields; i++)
            {
                if (StorageField(declaration, i) is not { BoundSymbol.Type: { } type } field || !ReferenceEquals(field.TypeKoto?.BoundType, type) ||
                    (i == 0 ? !BoundStoragePointer(type, u8) : !ReferenceEquals(type, BoundType.ISize)))
                {
                    return false;
                }
            }

            return true;
        }

        if (id is >= KimiDeclarationId.StorageOwnDictionary and <= KimiDeclarationId.StorageValueAt)
        {
            return this.ValidBoundOwnedDictionaryStorage(symbol, id, u8);
        }

        if (symbol.Declaration is not FunctionKoto { GenericArguments: [var keyParameter, var valueParameter] } function ||
            keyParameter.BoundType is not { Kind: BoundTypeKind.Parameter } key || valueParameter.BoundType is not { Kind: BoundTypeKind.Parameter } value ||
            symbol.Type is not { } result || function.Parameters[0].Type.BoundType is not { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components: [var source] } input ||
            input.Semantics != (id is KimiDeclarationId.StorageBorrowDictionaryExclusive or KimiDeclarationId.StorageSplitValue ? SemanticsKind.Uniq : SemanticsKind.Ref))
        {
            return false;
        }

        if (id is KimiDeclarationId.StorageBorrowDictionary or KimiDeclarationId.StorageBorrowDictionaryExclusive)
        {
            return source is { Kind: BoundTypeKind.Dictionary, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var storedKey, var storedValue] } &&
                ReferenceEquals(source.Symbol, this.GetSymbol(KimiDeclarationId.Dictionary)) && ReferenceEquals(storedKey, key) && ReferenceEquals(storedValue, value) &&
                input.Origin is { Kind: OriginKind.Parameter, Slot: 0 } origin && ReferenceEquals(origin.Binder, function) &&
                this.DictionaryRemainder(result, remainderId, key, value) && ReferenceEquals(result.OriginArguments[0], origin);
        }

        var split = id == KimiDeclarationId.StorageSplitValue;
        return this.DictionaryRemainder(source, remainderId, key, value) && StorageInputOrigin(input.Origin, function) && StorageInputOrigin(source.OriginArguments[0], function) &&
            !ReferenceEquals(input.Origin, source.OriginArguments[0]) && BoundStoragePointer(function.Parameters[1].Type.BoundType, u8) &&
            result is { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components: [var lent] } && result.Semantics == (split ? SemanticsKind.Uniq : SemanticsKind.Ref) &&
            ReferenceEquals(lent, id is KimiDeclarationId.StorageLendKey or KimiDeclarationId.StorageLendUniqKey ? key : value) && ReferenceEquals(result.Origin, source.OriginArguments[0]);
    }

    // ownStorage takes the Dictionary by value; keyAt and valueAt address one slot's key or value with no Loan.
    private bool ValidBoundOwnedDictionaryStorage(BindingSymbol symbol, KimiDeclarationId id, BoundType u8)
    {
        if (symbol.Declaration is not FunctionKoto { GenericArguments: [var keyParameter, var valueParameter] } function ||
            keyParameter.BoundType is not { Kind: BoundTypeKind.Parameter } key || valueParameter.BoundType is not { Kind: BoundTypeKind.Parameter } value ||
            symbol.Type is not { } result || function.Parameters[0].Type.BoundType is not { } input)
        {
            return false;
        }

        if (id == KimiDeclarationId.StorageOwnDictionary)
        {
            return input is { Kind: BoundTypeKind.Dictionary, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var storedKey, var storedValue] } &&
                ReferenceEquals(input.Symbol, this.GetSymbol(KimiDeclarationId.Dictionary)) && ReferenceEquals(storedKey, key) && ReferenceEquals(storedValue, value) &&
                this.DictionaryRemainder(result, KimiDeclarationId.DictionaryOwnedRemainder, key, value, 0);
        }

        return function.Parameters.Count == 1 && BoundStoragePointer(input, u8) && BoundStoragePointer(result, id == KimiDeclarationId.StorageKeyAt ? key : value);
    }

    private bool DictionaryRemainder(BoundType type, KimiDeclarationId id, BoundType key, BoundType value, int origins = 1)
        => type is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Origin: null, Components: [var storedKey, var storedValue] } && type.OriginArguments.Count == origins &&
            ReferenceEquals(type.Symbol, this.GetSymbol(id)) && ReferenceEquals(storedKey, key) && ReferenceEquals(storedValue, value);

    private bool BoundStorageContainer(BoundType type, KimiDeclarationId id, BoundType element, int origins)
        => type.Kind == (id == KimiDeclarationId.Array ? BoundTypeKind.Array : BoundTypeKind.Constructed) &&
            type.Semantics == SemanticsKind.Owner && type.Origin is null && type.OriginArguments.Count == origins &&
            ReferenceEquals(type.Symbol, this.GetSymbol(id)) && type.Components is [var argument] && ReferenceEquals(argument, element);

    private readonly record struct StorageSignature(KimiDeclarationId Id, KimiDeclarationId? Parameter, SemanticsKind Semantics, KimiDeclarationId? Result = null, bool Capability = false);
}
