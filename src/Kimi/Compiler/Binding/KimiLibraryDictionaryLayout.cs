// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // This private unsafe primitive only exposes physical storage to the source algorithms. It acquires no entries.
    private bool ValidDictionaryLayout(BindingSymbol symbol)
        => symbol.CompilerFunction == CompilerFunctionKind.StorageDictionaryLayout &&
        symbol.Declaration is FunctionKoto { Name: "dictionaryStorage", Modifier: ModifierKind.Internal | ModifierKind.Unsafe, AttributeChain: null, Body: null, ExpressionBody: null, IsRequirement: false, IsGenerated: false, IsSpecialization: false, Origins.Count: 0 } function &&
        function.GenericArguments is [GenericParameterKoto { Identifier: "K", SemanticsParameter: null, AttributeChain: null }, GenericParameterKoto { Identifier: "V", SemanticsParameter: null, AttributeChain: null }] &&
        function.TypeConstraints is [IsKoto { IsNegated: false } constraint] &&
        function.Parameters is [{ InternalName: "value", ExternalName: "value", DefaultValue: null, AttributeChain: null, Type: TypeSemanticsKoto { SemanticsKind: SemanticsKind.Uniq, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null, Type: GenericsKoto { TypeArguments: [var key, var value] } input } }] &&
        function.ReturnType is TupleTypeKoto { ElementNodes: [TypeSemanticsKoto { SemanticsKind: SemanticsKind.Unsafe, SemanticsParameter: null, OriginName: null, OriginExpression: null, OriginArguments: null } address, var stride] } &&
        ReferenceEquals(function.Parent, this.StorageScope.Owner) &&
        BareName(constraint.Left, "K") && BareName(constraint.Right, "Equatable") && BareName(input.Identifier, "Dictionary") &&
        BareName(key, "K") && BareName(value, "V") && BareName(address.Type, "u8") &&
        BareName(stride, "isize");

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
