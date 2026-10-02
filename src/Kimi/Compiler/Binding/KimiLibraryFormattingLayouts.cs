// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class KimiLibrary
{
    // SPEC UTF-8 formatting 1.1: the runtime accesses these fields directly; the adapters keep the Loan their raw pointers
    // cannot state in a trailing zero-sized Loan Field (SPEC 15.3.5).
    private static readonly FormattingLayout[] FormattingLayouts =
    [
        new(KimiDeclarationId.BufferFull, [], LoanRequirement.None),
        new(KimiDeclarationId.InvalidUtf8, [], LoanRequirement.None),
        new(KimiDeclarationId.FixedBuffer, [FormattingFieldType.Pointer, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Loan], LoanRequirement.Uniq),
        new(KimiDeclarationId.HeapBuffer, [FormattingFieldType.Pointer, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Size], LoanRequirement.None),
        new(KimiDeclarationId.WriteWindow, [FormattingFieldType.Pointer, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Loan], LoanRequirement.Uniq),
        new(KimiDeclarationId.Utf8Writer, [FormattingFieldType.Pointer, FormattingFieldType.Pointer, FormattingFieldType.Size, FormattingFieldType.Boolean, FormattingFieldType.Size, FormattingFieldType.Pointer, FormattingFieldType.Size, FormattingFieldType.Size, FormattingFieldType.Loan], LoanRequirement.Uniq),
        new(KimiDeclarationId.Utf8Slice, [FormattingFieldType.ByteSlice], LoanRequirement.Ref),
    ];

    private enum FormattingFieldType : byte
    {
        Pointer,
        Size,
        Boolean,
        ByteSlice,
        Loan,
    }

    private bool ValidBoundFormattingLayout(BindingSymbol symbol, KimiDeclarationId id)
    {
        foreach (var layout in FormattingLayouts)
        {
            if (layout.Id != id)
            {
                continue;
            }

            if (symbol.Declaration is not StructKoto declaration || symbol.Schema is not { } schema ||
                schema.Origins.Count != (layout.Loan == LoanRequirement.None ? 0 : 1))
            {
                return false;
            }

            BoundOrigin? source = null;
            if (schema.Origins.Count == 1)
            {
                var parameter = schema.Origins[0];
                source = parameter.Origin;
                if (parameter.LoanRequirement != layout.Loan || parameter.Variance != OriginVariance.Covariant ||
                    source.Kind != OriginKind.Parameter || source.Slot != 0 || !ReferenceEquals(source.Binder, declaration))
                {
                    return false;
                }
            }

            for (var i = 0; i < layout.Fields.Length; i++)
            {
                if (StorageField(declaration, i) is not { BoundSymbol.Type: { } type } field || !ReferenceEquals(field.TypeKoto?.BoundType, type) ||
                    !this.BoundFormattingField(type, layout.Fields[i], source))
                {
                    return false;
                }
            }

            return true;
        }

        return true; // Functions and Contracts retain their separate signature checks.
    }

    private bool BoundFormattingField(BoundType type, FormattingFieldType expected, BoundOrigin? source)
        => expected switch
        {
            FormattingFieldType.Pointer => BoundStoragePointer(type, BoundType.Primitives["u8"]),
            FormattingFieldType.Size => ReferenceEquals(type, BoundType.ISize),
            FormattingFieldType.Boolean => ReferenceEquals(type, BoundType.Boolean),
            FormattingFieldType.ByteSlice => type is { Kind: BoundTypeKind.Slice, Semantics: SemanticsKind.Owner, OriginArguments.Count: 0, Components: [var element] } &&
                ReferenceEquals(type.Symbol, this.Slice) && ReferenceEquals(element, BoundType.Primitives["u8"]) && source is not null && ReferenceEquals(type.Origin, source),
            FormattingFieldType.Loan => type is { Kind: BoundTypeKind.Constructed, Semantics: SemanticsKind.Owner, Components: [{ Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Uniq, Components: [var target] } lent] } &&
                type.Symbol?.LibraryDeclaration == KimiDeclarationId.Loan && ReferenceEquals(target, BoundType.Primitives["u8"]) && source is not null && ReferenceEquals(lent.Origin, source),
            _ => false,
        };

    private readonly record struct FormattingLayout(KimiDeclarationId Id, FormattingFieldType[] Fields, LoanRequirement Loan);
}
