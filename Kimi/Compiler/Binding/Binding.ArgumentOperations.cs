// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402, CS1591 // Shared operation vocabulary.

public enum ArgumentAdaptation : byte
{
    Exact,
    Literal,
    SameSemanticsReborrow,
    CrossSemanticsBorrow,
}

public enum ArgumentOperationKind : byte
{
    Value,
    Borrow,
    Reborrow,
    BaseBorrow,
    StorageProjection,
}

/// <summary>A selected operation. Source retains the original storage/Loan anchor; substitution never rewrites it.</summary>
public readonly record struct BoundArgumentOperation(Koto? Source, BoundType? SourceType, BoundType? ParameterType, ArgumentOperationKind Kind, ArgumentAdaptation Adaptation, BoundMemberPath? BasePath = null, int ParameterIndex = -1, ConstraintProof ObjectCompatibility = ConstraintProof.Proven);

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundArgumentOperation> argumentOperationScratch = new();
    private readonly Dictionary<Koto, BoundArgumentOperation> receiverOperations = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets a selected receiver/storage operation, including an unresolved projected-use proof obligation.</summary>
    /// <param name="use">The call or member access in the current binding pass.</param>
    /// <param name="operation">The selected operation; an Unknown proof is not permission to execute.</param>
    /// <returns>Whether this pass selected an operation for the use.</returns>
    public bool TryGetReceiverOperation(Koto use, out BoundArgumentOperation operation) => this.receiverOperations.TryGetValue(use, out operation);

    internal static Koto PlaceOriginSource(Koto source)
    {
        // Inline element projections share the owner's lifetime. Their Loans
        // retain separate place footprints for overlap checking.
        source = KotoHelper.UnwrapParentheses(source);
        while (source is MemberAccessKoto or IndexKoto)
        {
            source = KotoHelper.UnwrapParentheses(((BinaryKoto)source).Left);
            if (source.BoundType?.Origin is not null)
            {
                break;
            }
        }

        return source;
    }

    internal static Koto PlaceOriginBinder(Koto source) => source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate } symbol ? symbol.Declaration : source;

    internal static int PlaceOriginSlot(Koto source) => source.BoundSymbol is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.PatternCandidate } symbol ? symbol.Slot : 0;

    private static ConstraintProof ProjectedReceiverProof(BindingSymbol implementation)
        // Until Access Effect verification supplies callee/returned-Loan summaries, no body or signature is evidence.
        => implementation.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Unknown;

    private BoundOrigin PlaceOrigin(Koto source)
    {
        source = PlaceOriginSource(source);
        return source.BoundType?.Origin ?? this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source));
    }

    private bool BorrowablePlace(Koto source, BindingScope scope, bool exclusive)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source.BoundSymbol?.Property is { } property)
        {
            if (!property.Getter.IsStandard || !this.Accessible(property.Symbol, scope, property.Getter.Access, (source as MemberAccessKoto)?.Left.BoundType))
            {
                return false;
            }

            if (exclusive && (!property.Setter.IsStandard || !this.Accessible(property.Symbol, scope, property.Setter.Access, (source as MemberAccessKoto)?.Left.BoundType)))
            {
                return false;
            }

            if (source is MemberAccessKoto access)
            {
                return access.Left.BoundType is { Kind: BoundTypeKind.Semantics } receiver
                    ? !exclusive || receiver.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq
                    : this.BorrowablePlace(access.Left, scope, exclusive);
            }

            return true;
        }

        return source is IdentifierNameKoto && source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage && (!exclusive || Writable(source));
    }

    private bool AdaptInput(Koto source, BoundType pattern, BoundType actual, BindingScope scope, BoundMemberPath? path, BoundType? declaringType, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind)
    {
        actual = this.ContractType(actual, scope);
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        if (ReferenceEquals(actual, BoundType.Never))
        {
            return true; // A noncompleting argument forms no borrow or reference value.
        }

        var projected = path is not null;
        if (pattern.Kind != BoundTypeKind.Semantics || pattern.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq))
        {
            return !projected; // An owning receiver cannot acquire a sliced base.
        }

        var target = pattern.Semantics;
        BoundType referent;
        if (actual.Kind == BoundTypeKind.Semantics && actual.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            if (target == SemanticsKind.Uniq && actual.Semantics != SemanticsKind.Uniq)
            {
                return false;
            }

            referent = actual.Components[0];
            if (actual.Semantics == SemanticsKind.Ref && !projected)
            {
                return true;
            }

            quality = actual.Semantics == target ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Reborrow;
        }
        else if (actual.Semantics == SemanticsKind.Owner && (this.BorrowablePlace(source, scope, target == SemanticsKind.Uniq) ||
            ((source.BoundSymbol is null || KotoHelper.UnwrapParentheses(source) is InvocationKoto) &&
                KotoHelper.UnwrapParentheses(source) is not IdentifierNameKoto && source.BoundType is { } temporary && !ReferenceEquals(temporary, BoundType.Never))))
        {
            referent = actual;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
        }
        else
        {
            return false;
        }

        if (projected)
        {
            if (declaringType is null)
            {
                return false;
            }

            referent = declaringType;
            kind = ArgumentOperationKind.BaseBorrow;
        }

        adapted = this.InternType(BoundTypeKind.Semantics, null, target, [referent], origin: this.PlaceOrigin(source));
        return true;
    }
}
