// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC 4.7.2-4: one complete bound signature per compiler-managed collection operation.
    // Element denotes T or K, and Value denotes V, by this declaration's parameter identities.
    private static readonly CollectionSignature?[] CollectionSignatures = IndexSignatures<CollectionSignature>(
    [
        new(KimiDeclarationId.ArrayWithCapacity, CollectionType.Unit, [(CollectionType.ISize, "capacity")]),
        new(KimiDeclarationId.ArrayReserve, CollectionType.Unit, [(CollectionType.Receiver, "self"), (CollectionType.ISize, "additional")]),
        new(KimiDeclarationId.ArrayAppend, CollectionType.Unit, [(CollectionType.Receiver, "self"), (CollectionType.Element, "value")]),
        new(KimiDeclarationId.ArrayInsert, CollectionType.Unit, [(CollectionType.Receiver, "self"), (CollectionType.ISize, "index"), (CollectionType.Element, "value")]),
        new(KimiDeclarationId.ArrayPop, CollectionType.OptionElement, [(CollectionType.Receiver, "self")]),
        new(KimiDeclarationId.ArrayRemove, CollectionType.Element, [(CollectionType.Receiver, "self"), (CollectionType.ISize, "index")]),
        new(KimiDeclarationId.ArrayClear, CollectionType.Unit, [(CollectionType.Receiver, "self")]),
        new(KimiDeclarationId.ArraySwap, CollectionType.Unit, [(CollectionType.Receiver, "self"), (CollectionType.ISize, "first"), (CollectionType.ISize, "second")]),
        new(KimiDeclarationId.ArrayShrinkToFit, CollectionType.Unit, [(CollectionType.Receiver, "self")]),
        new(KimiDeclarationId.DictionaryReserve, CollectionType.Unit, [(CollectionType.Receiver, "self"), (CollectionType.ISize, "additional")]),
        new(KimiDeclarationId.DictionaryTryInsert, CollectionType.ResultPair, [(CollectionType.Receiver, "self"), (CollectionType.Element, "key"), (CollectionType.Value, "value")]),
        new(KimiDeclarationId.DictionaryInsertOrReplace, CollectionType.OptionValue, [(CollectionType.Receiver, "self"), (CollectionType.Element, "key"), (CollectionType.Value, "value")]),
        new(KimiDeclarationId.DictionaryRemove, CollectionType.OptionPair, [(CollectionType.Receiver, "self"), (CollectionType.BorrowedKey, "key")]),
        new(KimiDeclarationId.DictionaryTryGet, CollectionType.OptionBorrowedValue, [(CollectionType.SharedReceiver, "self"), (CollectionType.BorrowedKey, "key")]),
        new(KimiDeclarationId.DictionaryClear, CollectionType.Unit, [(CollectionType.Receiver, "self")]),
        new(KimiDeclarationId.DictionaryShrinkToFit, CollectionType.Unit, [(CollectionType.Receiver, "self")]),
        new(KimiDeclarationId.DictionaryIndex, CollectionType.BorrowedValue, [(CollectionType.SharedReceiver, "self"), (CollectionType.BorrowedKey, "key")]),
        new(KimiDeclarationId.DictionaryIndexUniq, CollectionType.ExclusiveBorrowedValue, [(CollectionType.Receiver, "self"), (CollectionType.BorrowedKey, "key")]),
    ],
    static signature => signature.Id);

    private enum CollectionType : byte
    {
        Unit,
        ISize,
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
        BorrowedValue,
        ExclusiveBorrowedValue,
    }

    private static bool BoundInputBorrow(BoundType? type, BoundType element, SemanticsKind semantics, FunctionKoto function, int input)
        => type is { Kind: BoundTypeKind.Semantics, Symbol: null, OriginArguments.Count: 0, Components: [var referent], Origin: { Kind: OriginKind.Input } origin } && type.Semantics == semantics &&
            ReferenceEquals(referent, element) && ReferenceEquals(origin.Binder, function) && origin.InputIndex == input && origin.Slot == input &&
            ReferenceEquals(origin, function.Parameters[input].Type.BoundType?.Origin);

    private static bool CollectionPair(BoundType? type, BoundType key, BoundType? value)
        => type is { Kind: BoundTypeKind.Tuple, Semantics: SemanticsKind.Owner, Symbol: null, Origin: null, OriginArguments.Count: 0, Components: [var first, var second] } &&
            ReferenceEquals(first, key) && ReferenceEquals(second, value);

    private static bool CollectionSyntaxType(Koto? type, CollectionType expected, bool dictionary)
    {
        var element = dictionary ? "K" : "T";
        var generic = BareType(type) as GenericsKoto;
        return expected switch
        {
            CollectionType.Unit => type is null,
            CollectionType.ISize => BareName(type, "isize"),
            CollectionType.Element => BareName(type, element),
            CollectionType.Value => BareName(type, "V"),
            CollectionType.Receiver or CollectionType.SharedReceiver => Borrow(type, expected == CollectionType.SharedReceiver ? SemanticsKind.Ref : SemanticsKind.Uniq, "Self"),
            CollectionType.BorrowedKey => Borrow(type, SemanticsKind.Ref, "K"),
            CollectionType.BorrowedValue or CollectionType.ExclusiveBorrowedValue => Borrow(type, expected == CollectionType.BorrowedValue ? SemanticsKind.Ref : SemanticsKind.Uniq, "V", result: true),
            CollectionType.OptionElement or CollectionType.OptionValue or CollectionType.OptionPair or CollectionType.OptionBorrowedValue =>
                generic is { TypeArguments.Count: 1 } && BareName(generic.Identifier, "Option") && (expected == CollectionType.OptionPair ? Pair(generic.TypeArguments[0]) :
                    CollectionSyntaxType(generic.TypeArguments[0], expected == CollectionType.OptionElement ? CollectionType.Element : expected == CollectionType.OptionValue ? CollectionType.Value : CollectionType.BorrowedValue, dictionary)),
            CollectionType.ResultPair => generic is { TypeArguments.Count: 2 } && BareName(generic.Identifier, "Result") &&
                BareType(generic.TypeArguments[0]) is TupleTypeKoto { ElementNodes.Count: 0 } && Pair(generic.TypeArguments[1]),
            _ => false,
        };

        static bool Pair(Koto type) => BareType(type) is TupleTypeKoto { ElementNodes.Count: 2 } tuple && BareName(tuple.ElementNodes[0], "K") && BareName(tuple.ElementNodes[1], "V");
        static bool Borrow(Koto? type, SemanticsKind semantics, string name, bool result = false)
            => type is TypeSemanticsKoto { SemanticsParameter: null, OriginArguments: null, Type: { } target } borrow && borrow.SemanticsKind == semantics &&
                (result ? borrow.OriginExpression is IdentifierNameKoto { IdentifierName: "self" } : borrow.OriginName is null && borrow.OriginExpression is null) && BareName(target, name);
    }

    private bool ValidCollectionOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var index = KimiLibraryCatalog.Index(id);
        ref readonly var rule = ref KimiLibraryCatalog.Entries[index];
        var signature = CollectionSignatures[index]!.Value;
        var dictionary = rule.Container == KimiLibraryContainer.Dictionary;
        var constructor = id == KimiDeclarationId.ArrayWithCapacity;
        if (symbol.CompilerFunction != rule.Function || symbol.Declaration is not FunctionKoto function ||
            !ReferenceEquals(function.Parent, dictionary ? this.DictionaryScope.Owner : this.ArrayScope.Owner) ||
            function.Name != rule.Name || (constructor ? !function.IsConstructor || function.NameBoundaryIndex != 0 : function.NameBoundaryIndex >= 0) ||
            function.Modifier != (id is KimiDeclarationId.ArrayInsert or KimiDeclarationId.ArrayRemove or KimiDeclarationId.ArraySwap ? ModifierKind.Internal : ModifierKind.Public) ||
            function.GenericArguments.Count != 0 || function.Origins.Count != 0 || function.TypeConstraints.Count != 0 ||
            (function.Body is not null || function.ExpressionBody is not null) != rule.SourceFunction || function.AttributeChain is not null ||
            function.IsRequirement || function.IsGenerated || function.IsSpecialization || function.Parameters.Count != signature.Inputs.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Inputs.Length; i++)
        {
            var parameter = function.Parameters[i];
            var expected = signature.Inputs[i];
            if (parameter.DefaultValue is not null || parameter.AttributeChain is not null || parameter.InternalName != expected.Name || parameter.ExternalName != expected.Name ||
                !CollectionSyntaxType(parameter.Type, expected.Type, dictionary))
            {
                return false;
            }
        }

        var result = function.ReturnType;
        if (id is KimiDeclarationId.DictionaryIndex or KimiDeclarationId.DictionaryIndexUniq)
        {
            if (result is not PlaceResultKoto place)
            {
                return false;
            }

            result = place.Type;
        }

        return CollectionSyntaxType(result, signature.Result, dictionary);
    }

    private bool ValidBoundCollectionOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        if (CollectionSignatures[KimiLibraryCatalog.Index(id)] is not { } signature)
        {
            return true;
        }

        var dictionary = KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Container == KimiLibraryContainer.Dictionary;
        if (symbol.Declaration is not FunctionKoto function ||
            this.GetSymbol(dictionary ? KimiDeclarationId.Dictionary : KimiDeclarationId.Array)?.Schema?.GenericSlots is not { } slots ||
            slots.Count != (dictionary ? 2 : 1) || slots[0].Symbol.Type is not { } element || (dictionary && slots[1].Symbol.Type is null))
        {
            return false;
        }

        var value = dictionary ? slots[1].Symbol.Type : null;
        if (function.Parameters.Count != signature.Inputs.Length || !this.ValidBoundCollectionType(symbol.Type, signature.Result, function, element, value))
        {
            return false;
        }

        for (var i = 0; i < signature.Inputs.Length; i++)
        {
            if (!this.ValidBoundCollectionType(function.Parameters[i].Type.BoundType, signature.Inputs[i].Type, function, element, value))
            {
                return false;
            }
        }

        return true;
    }

    private bool ValidBoundCollectionType(BoundType? type, CollectionType expected, FunctionKoto function, BoundType element, BoundType? value)
        => expected switch
        {
            CollectionType.Unit => ReferenceEquals(type, BoundType.Unit),
            CollectionType.ISize => ReferenceEquals(type, BoundType.ISize),
            CollectionType.Element => ReferenceEquals(type, element),
            CollectionType.OptionElement => type is not null && this.BoundStorageContainer(type, KimiDeclarationId.Option, element, 0),
            CollectionType.Value => value is not null && ReferenceEquals(type, value),
            CollectionType.OptionValue => type is not null && value is not null && this.BoundStorageContainer(type, KimiDeclarationId.Option, value, 0),
            CollectionType.BorrowedKey => BoundInputBorrow(type, element, SemanticsKind.Ref, function, 1),
            CollectionType.BorrowedValue or CollectionType.ExclusiveBorrowedValue => value is not null &&
                BoundInputBorrow(type, value, expected == CollectionType.BorrowedValue ? SemanticsKind.Ref : SemanticsKind.Uniq, function, 0),
            CollectionType.OptionPair => this.CollectionResult(type, KimiDeclarationId.Option) && type!.Components is [var pair] && CollectionPair(pair, element, value),
            CollectionType.ResultPair => this.CollectionResult(type, KimiDeclarationId.Result) && type!.Components is [var success, var pair] &&
                ReferenceEquals(success, BoundType.Unit) && CollectionPair(pair, element, value),
            CollectionType.OptionBorrowedValue => this.CollectionResult(type, KimiDeclarationId.Option) && type!.Components is [var borrowed] &&
                value is not null && BoundInputBorrow(borrowed, value, SemanticsKind.Ref, function, 0),
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

    private readonly record struct CollectionSignature(KimiDeclarationId Id, CollectionType Result, (CollectionType Type, string Name)[] Inputs);
}
