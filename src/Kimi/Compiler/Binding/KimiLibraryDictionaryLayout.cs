// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool ValidBoundDictionaryPlacement(BindingSymbol symbol)
    {
        if (symbol.Declaration is not FunctionKoto { GenericArguments: [var key, var value] } function ||
            key.BoundType is not { Kind: BoundTypeKind.Parameter } keyType || value.BoundType is not { Kind: BoundTypeKind.Parameter } valueType ||
            function.Parameters.Count != 3 ||
            !BoundStoragePointer(function.Parameters[0].Type.BoundType, BoundType.Primitives["u8"]) ||
            (symbol.Type is not null && !ReferenceEquals(symbol.Type, BoundType.Unit)))
        {
            return false;
        }

        return ReferenceEquals(function.Parameters[2].Type.BoundType, valueType) && ReferenceEquals(function.Parameters[1].Type.BoundType, keyType);
    }

    private static bool PlacementInput(FunctionParameterKoto parameter, string name)
        => parameter is { DefaultValue: null, AttributeChain: null } && parameter.InternalName == name && parameter.ExternalName == name;

    private bool ValidMissingDictionaryKey(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.StorageMissingDictionaryKey &&
        symbol.Declaration is FunctionKoto { Name: "missingDictionaryKey", Modifier: ModifierKind.Internal, AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0, GenericArguments.Count: 0, Parameters.Count: 0, TypeConstraints.Count: 0 } function &&
        ReferenceEquals(function.Parent, this.StorageScope.Owner) && BareName(function.ReturnType, "Never");

    // This private unsafe primitive only exposes physical storage to the source algorithms. It acquires no entries.
    private bool ValidDictionaryLayout(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.StorageDictionaryLayout &&
        symbol.Declaration is FunctionKoto { Name: "dictionaryStorage", Modifier: ModifierKind.Internal | ModifierKind.Unsafe, AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0 } function &&
        function.GenericArguments is [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] &&
        function.TypeConstraints is [IsKoto { IsNegated: false } constraint] &&
        function.Parameters is [{ InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: GenericsKoto { TypeArguments: [var key, var value] } input } }] &&
        function.ReturnType is TupleTypeKoto { ElementNodes: [TypeSemanticsKoto { SemanticsKind: SemanticsKind.Raw, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } address, var stride] } &&
        ReferenceEquals(function.Parent, this.StorageScope.Owner) &&
        BareName(constraint.Left, "K") && BareName(constraint.Right, "Equatable") && BareName(input.Identifier, "Dictionary") &&
        BareName(key, "K") && BareName(value, "V") && BareName(address.Type, "u8") &&
        BareName(stride, "isize");

    // These private unsafe primitives transfer acquired values into physical slots: placeEntry appends one slot for a key
    // and value after the source proves absence; placeValue refills a live slot whose value was moved out.
    private bool ValidDictionaryPlacement(BindingSymbol symbol)
    {
        if (symbol.CompilerFunction != CompilerFunctionKind.StoragePlaceDictionaryEntry ||
            symbol.Declaration is not FunctionKoto { Modifier: ModifierKind.Internal | ModifierKind.Unsafe, AttributeChain: null, Body: null, ExpressionBody: null, ReturnType: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0, TypeConstraints.Count: 0 } function ||
            function.Name != "placeEntry" || !ReferenceEquals(function.Parent, this.StorageScope.Owner) ||
            function.GenericArguments is not [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] ||
            function.Parameters.Count != 3 || !PlacementInput(function.Parameters[0], "handle") ||
            function.Parameters[0].Type is not TypeSemanticsKoto { SemanticsKind: SemanticsKind.Raw, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } address || !BareName(address.Type, "u8"))
        {
            return false;
        }

        return PlacementInput(function.Parameters[2], "value") && BareName(function.Parameters[2].Type, "V") &&
            PlacementInput(function.Parameters[1], "key") && BareName(function.Parameters[1].Type, "K");
    }

    // SPEC 5.6: the bodiless public Kimi.Raw operations over one Type parameter T, which the compiler implements.
    private bool ValidRawOperation(BindingSymbol symbol, KimiDeclarationId id)
    {
        var (name, modifier, parameters) = id switch
        {
            KimiDeclarationId.RawAllocate => ("allocate", ModifierKind.Public, 1),
            KimiDeclarationId.RawRelease => ("release", ModifierKind.Public | ModifierKind.Unsafe, 1),
            _ => ("initialize", ModifierKind.Public | ModifierKind.Unsafe, 2),
        };
        if (symbol.CompilerFunction != KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function ||
            symbol.Declaration is not FunctionKoto { AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0, TypeConstraints.Count: 0 } function ||
            !ReferenceEquals(function.Parent, this.RawScope.Owner) || function.Name != name || function.Modifier != modifier ||
            function.GenericArguments is not [GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null }] ||
            function.Parameters.Count != parameters)
        {
            return false;
        }

        var first = function.Parameters[0];
        if (id == KimiDeclarationId.RawAllocate)
        {
            return PlacementInput(first, "count") && BareName(first.Type, "isize") &&
                function.ReturnType is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Raw, SemanticsParameter: null, OriginName: null, OriginExpression: null } result && BareName(result.Type, "T");
        }

        return function.ReturnType is null && PlacementInput(first, "storage") &&
            first.Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Raw, SemanticsParameter: null, OriginName: null, OriginExpression: null } storage && BareName(storage.Type, "T") &&
            (id == KimiDeclarationId.RawRelease || (PlacementInput(function.Parameters[1], "value") && BareName(function.Parameters[1].Type, "T")));
    }

    // These private capacity bridges keep the Kimigayo growth and shrink decisions; the compiler constructs the platform
    // callbacks and forwards the standard operation's caller location.
    private bool ValidDictionaryCapacity(BindingSymbol symbol, KimiDeclarationId id)
    {
        var reserve = id == KimiDeclarationId.StorageReserveDictionary;
        return symbol.CompilerFunction == KimiLibraryCatalog.Entries[KimiLibraryCatalog.Index(id)].Function &&
            symbol.Declaration is FunctionKoto { Modifier: ModifierKind.Internal, AttributeChain: null, Body: null, ExpressionBody: null, ReturnType: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0 } function &&
            function.Name == (reserve ? "reserveEntries" : "shrinkEntries") && ReferenceEquals(function.Parent, this.StorageScope.Owner) &&
            function.GenericArguments is [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] &&
            function.TypeConstraints is [IsKoto { IsNegated: false } constraint] && BareName(constraint.Left, "K") && BareName(constraint.Right, "Equatable") &&
            function.Parameters.Count == (reserve ? 2 : 1) && PlacementInput(function.Parameters[0], "value") &&
            function.Parameters[0].Type is TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: GenericsKoto { TypeArguments: [var key, var value] } input } &&
            BareName(input.Identifier, "Dictionary") && BareName(key, "K") && BareName(value, "V") &&
            (!reserve || (PlacementInput(function.Parameters[1], "additional") && BareName(function.Parameters[1].Type, "isize")));
    }

    private bool ValidBoundDictionaryCapacity(BindingSymbol symbol, KimiDeclarationId id)
        => symbol.Declaration is FunctionKoto { GenericArguments: [var key, var value] } function &&
        key.BoundType is { Kind: BoundTypeKind.Parameter } keyType && value.BoundType is { Kind: BoundTypeKind.Parameter } valueType &&
        function.Parameters.Count == (id == KimiDeclarationId.StorageReserveDictionary ? 2 : 1) &&
        function.Parameters[0].Type.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Symbol: null, OriginArguments.Count: 0, Components: [var dictionary] } input &&
        StorageInputOrigin(input.Origin, function) &&
        dictionary is { Kind: BoundTypeKind.Dictionary, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var storedKey, var storedValue] } &&
        ReferenceEquals(dictionary.Symbol, this.GetSymbol(KimiDeclarationId.Dictionary)) && ReferenceEquals(storedKey, keyType) && ReferenceEquals(storedValue, valueType) &&
        (function.Parameters.Count == 1 || ReferenceEquals(function.Parameters[1].Type.BoundType, BoundType.ISize)) &&
        (symbol.Type is null || ReferenceEquals(symbol.Type, BoundType.Unit));

    private bool ValidBoundDictionaryLayout(BindingSymbol symbol)
        => symbol.Declaration is FunctionKoto { GenericArguments: [var key, var value], Parameters: [var parameter] } function &&
        key.BoundType is { Kind: BoundTypeKind.Parameter } keyType && value.BoundType is { Kind: BoundTypeKind.Parameter } valueType &&
        parameter.Type.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Symbol: null, OriginArguments.Count: 0, Components: [var dictionary] } input &&
        StorageInputOrigin(input.Origin, function) &&
        dictionary is { Kind: BoundTypeKind.Dictionary, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var storedKey, var storedValue] } &&
        ReferenceEquals(dictionary.Symbol, this.GetSymbol(KimiDeclarationId.Dictionary)) && ReferenceEquals(storedKey, keyType) && ReferenceEquals(storedValue, valueType) &&
        symbol.Type is { Kind: BoundTypeKind.Tuple, Semantics: SemanticsKind.Owner, Origin: null, OriginArguments.Count: 0, Components: [var address, var stride] } &&
        BoundStoragePointer(address, BoundType.Primitives["u8"]) && ReferenceEquals(stride, BoundType.ISize);
}
