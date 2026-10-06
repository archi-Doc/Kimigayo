// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    private static bool ValidBoundUniqSlice(BindingSymbol symbol)
        => symbol.Declaration is StructKoto { GenericParameterNodes: [{ BoundType: { Kind: BoundTypeKind.Parameter } element }] } declaration &&
            StorageField(declaration, 0) is { BoundSymbol.Type: { } pointer } && BoundStoragePointer(pointer, element) &&
            StorageField(declaration, 1) is { BoundSymbol.Type: { } length } && ReferenceEquals(length, BoundType.ISize) &&
            BoundLoanField(declaration, 2, SemanticsKind.Uniq, symbol, out var lent) && ReferenceEquals(lent, element);

    // Only the storage projection is a compiler boundary; all view operations are ordinary Kimi bodies.
    private bool ValidUniqSlice(BindingSymbol symbol)
        => symbol.Intrinsic == IntrinsicKind.None && ReferenceEquals(symbol.Scope, this.Scope) &&
            symbol.Declaration is StructKoto { Name: "UniqSlice", HasIncompatibleBindingHeader: false, GenericParameterNodes: [GenericParameterKoto { Identifier: "T", SemanticsParameter: null, AttributeChain: null }], OriginNames: ["source"], Bases.Count: 0, NestedContainers.Count: 0, Modifier: ModifierKind.Public, AttributeChain: null } declaration &&
            ReferenceEquals(declaration.Parent, this.Kotonoha.RootKoto) &&
            ValidStorageField(declaration, 0, VariableKind.Let, "storage", "T", true) &&
            StorageField(declaration, 1) is { VariableKind: VariableKind.Let, Modifier: ModifierKind.Public, InitializerKoto: null, AttributeChain: null } length &&
            length.NameKoto.IdentifierName == "length" && BareName(length.TypeKoto, "isize") &&
            ValidLoanField(declaration, 2, SemanticsKind.Uniq, "T") && StorageField(declaration, 3) is null;
}
