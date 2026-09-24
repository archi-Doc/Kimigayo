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
    PayloadProjection,
    CopyRead,
}

/// <summary>A selected operation. Source retains the original storage/Loan anchor; substitution never rewrites it.</summary>
public readonly record struct BoundArgumentOperation(Koto? Source, BoundType? SourceType, BoundType? ParameterType, ArgumentOperationKind Kind, ArgumentAdaptation Adaptation, BoundMemberPath? BasePath = null, int ParameterIndex = -1, ConstraintProof ObjectCompatibility = ConstraintProof.Proven);

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundArgumentOperation> argumentOperationScratch = new();
    private readonly Dictionary<Koto, BoundArgumentOperation> receiverOperations = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> referentReads = new(ReferenceEqualityComparer.Instance);

    /// <summary>Gets whether an expression of Type <c>ref/T</c> or <c>uniq/T</c> is read as its Copy referent where a <c>T</c> is expected (SPEC 3.3).</summary>
    /// <param name="node">The bound expression; its BoundType remains the reference Type.</param>
    /// <returns>Whether the expression's value is the copied referent.</returns>
    public bool ReadsReferent(Koto node) => this.referentReads.Contains(node);

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

    internal BoundType PreparedBorrowType(Koto source, BoundType parameter)
        => this.InternType(parameter.Kind, parameter.Symbol, parameter.Semantics, [parameter.Components[0]], origin: this.PlaceOrigin(source));

    private static ConstraintProof ProjectedReceiverProof(BindingSymbol implementation)
        // Until Access Effect verification supplies callee/returned-Loan summaries, no body or signature is evidence.
        => implementation.Declaration.BindingState == BindingState.Invalid ? ConstraintProof.Error : ConstraintProof.Unknown;

    // SPEC 15.6.2: access through a shared reference cannot grant exclusive
    // authority, even to an exclusive reference stored below it.
    private static bool ReachedThroughShared(Koto source) => PathAuthority(source) == SemanticsKind.Ref;

    // SPEC 3.4: the access path of a Place. Owner means a direct path (a local, parameter or static and
    // their inline parts), Uniq a path through an exclusive reference and Ref a path through a shared one.
    // The path bounds every borrow of the Place; it does not change the Place's Type.
    private static SemanticsKind PathAuthority(Koto source)
    {
        var authority = SemanticsKind.Owner;
        source = KotoHelper.UnwrapParentheses(source);
        for (var depth = 0; depth < 64; depth++)
        {
            var root = source switch
            {
                MemberAccessKoto member => ElementAccess.BorrowedPathRoot(member),
                IndexKoto index when ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType) => index.Left,
                _ => null,
            };
            if (root is null)
            {
                return authority;
            }

            root = KotoHelper.UnwrapParentheses(root);
            switch (root.BoundType?.Semantics)
            {
                case SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc:
                    return SemanticsKind.Ref;
                case SemanticsKind.Uniq or SemanticsKind.ObjUniq:
                    authority = SemanticsKind.Uniq;
                    break;
            }

            source = root;
        }

        return authority;
    }

    // SPEC 3.5: a bare Place, as opposed to a Temporary Value or an explicit @ operation. Only a Place's
    // acquisition is restricted by the lending rule; a temporary transfers its ownership freely.
    private static bool IsBarePlace(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        return source switch
        {
            IdentifierNameKoto => source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture or BindingSymbolKind.PatternCandidate,
            MemberAccessKoto member => (member.BoundSymbol?.Property is { Getter.IsStandard: true } && StructStorage.IsStruct(member.Left.BoundType?.Kind == BoundTypeKind.Semantics ? member.Left.BoundType.Components[0] : member.Left.BoundType)) ||
                ReferenceTypes.IsTuple(member.Left.BoundType) || member.Left.BoundType?.Kind == BoundTypeKind.Tuple,
            IndexKoto index => index.Left.BoundType?.Kind == BoundTypeKind.FixedArray || ReferenceTypes.IsArray(index.Left.BoundType),
            _ => false,
        };
    }

    // Set while candidates are evaluated: the reason an otherwise fitting bare Place was not applicable,
    // so a call without applicable candidates names the required spelling (SPEC 15.1.5).
    private bool transferRequired;
    private bool lendingRequired;

    private BoundOrigin PlaceOrigin(Koto source)
    {
        if (this.ReadsReferent(source))
        {
            return this.OriginAtom(source, OriginKind.Projection, 0);
        }

        source = PlaceOriginSource(source);
        return source.BoundType?.Origin ?? this.OriginAtom(PlaceOriginBinder(source), OriginKind.Projection, PlaceOriginSlot(source));
    }

    // SPEC 3.3: one ref/T or uniq/T layer is read as its referent T when T is proved Copy; a Non-Copy
    // referent is never extracted through a reference.
    private BoundType? Referent(BoundType? type, Koto context)
        => type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            this.ProveCopy(type.Components[0], context) == ConstraintProof.Proven ? type.Components[0] : null;

    // An operand denotes its Copy referent (SPEC 13.4); the node keeps its reference Type.
    private BoundType? ReadReferent(Koto node, BoundType? type)
    {
        if (this.Referent(type, node) is not { } referent)
        {
            return type;
        }

        this.referentReads.Add(node);
        return referent;
    }

    // The type an argument presents to adaptation: a node already read where its parameter Type was
    // expected adapts from its own reference Type, so the plan records the Copy read once.
    private BoundType ArgumentType(Koto source, BoundType actual) => this.referentReads.Contains(source) ? source.BoundType ?? actual : actual;

    private bool BorrowablePlace(Koto source, BindingScope scope, bool exclusive)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (source is IndexKoto { Left.BoundType.Kind: BoundTypeKind.Slice })
        {
            return !exclusive;
        }

        if (source is IndexKoto index && (ReferenceTypes.IsArray(index.Left.BoundType) || ReferenceTypes.IsDynamicArray(index.Left.BoundType)))
        {
            return !exclusive || index.Left.BoundType!.Semantics == SemanticsKind.Uniq;
        }

        if (source is MemberAccessKoto element && ReferenceTypes.IsTuple(element.Left.BoundType))
        {
            return ElementAccess.TryBorrowedTupleElement(element, out _, out _) &&
                (!exclusive || element.Left.BoundType!.Semantics == SemanticsKind.Uniq);
        }

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

        if (source is BinaryKoto part && ElementAccess.IsSyntax(part) && ElementAccess.TryType(part, out _, out _))
        {
            return this.BorrowablePlace(part.Left, scope, exclusive);
        }

        // A closure environment binding is a Place of its own (SPEC 7.6.2), borrowable like a local.
        return source is IdentifierNameKoto && source.BoundSymbol?.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Storage or BindingSymbolKind.Capture && (!exclusive || Writable(source));
    }

    /// <summary>
    /// Adapts an input to a parameter Type. <paramref name="receiver"/> marks a Receiver Expression, which SPEC 7.3
    /// acquires implicitly: a new exclusive borrow of an owned Place or temporary needs no spelling there, whereas
    /// every other position requires <c>@uniq</c>/<c>@objuniq</c> whatever the access path (SPEC 15.1.5).
    /// </summary>
    private bool AdaptInput(Koto source, BoundType pattern, BoundType actual, BindingScope scope, BoundMemberPath? path, BoundType? declaringType, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool explicitBorrow = false, bool receiver = false)
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
        if (ObjectTypes.IsBorrow(pattern))
        {
            return !projected && this.AdaptObjectBorrow(source, pattern, actual, scope, explicitBorrow, out adapted, out quality, out kind, receiver);
        }

        if (pattern.Kind != BoundTypeKind.Semantics || pattern.Semantics is not (SemanticsKind.Ref or SemanticsKind.Uniq))
        {
            if (projected)
            {
                return false; // An owning receiver cannot acquire a sliced base.
            }

            // SPEC 10.2: where the reference does not fit but its Copy referent does, the referent is read.
            if (!this.FitsTypeAt(actual, pattern, source) && this.Referent(actual, source) is { } read && this.FitsTypeAt(read, pattern, source))
            {
                adapted = read;
                quality = ArgumentAdaptation.CrossSemanticsBorrow;
                kind = ArgumentOperationKind.CopyRead;
            }
            else if (actual.Semantics is SemanticsKind.Owner or SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc && IsBarePlace(source) && this.ProveCopy(actual, source) != ConstraintProof.Proven)
            {
                // SPEC 3.5, 10.2: a bare Place never Moves, so a Non-Copy or Copy-unproven Place is not
                // applicable by value; overload selection never transfers a bare Place.
                this.transferRequired = true;
                return false;
            }

            return true;
        }

        // A comparison's Copy read is a temporary value, even though its syntax retains ref/T.
        // Borrow that snapshot; borrowing the original storage would change left-to-right semantics.
        if (!projected && pattern.Semantics == SemanticsKind.Ref && this.ReadsReferent(source) &&
            this.Referent(actual, source) is { } snapshot && this.FitsTypeAt(snapshot, pattern.Components[0], source))
        {
            adapted = this.PreparedBorrowType(source, pattern);
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Borrow;
            return true;
        }

        if (!projected && declaringType is not null && this.TryPayloadProjection(source, pattern, actual, scope, out adapted))
        {
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.PayloadProjection;
            return true;
        }

        var target = pattern.Semantics;
        BoundType referent;
        if (actual.Kind == BoundTypeKind.Semantics && actual.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
        {
            if (target == SemanticsKind.Uniq && (actual.Semantics != SemanticsKind.Uniq || ReachedThroughShared(source)))
            {
                return false;
            }

            referent = actual.Components[0];
            if (actual.Semantics == SemanticsKind.Ref && !projected)
            {
                // An explicit shared string borrow (text@ref) is prepared at the call like the implicit one (SPEC 22.4).
                if (ReferenceEquals(referent, BoundType.String) && KotoHelper.UnwrapParentheses(source) is ConversionKoto { ConversionBinding: ConversionBinding.Borrow })
                {
                    kind = ArgumentOperationKind.Borrow;
                }

                return true;
            }

            quality = actual.Semantics == target ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
            kind = ArgumentOperationKind.Reborrow;
        }
        else if (actual.Semantics == SemanticsKind.Owner)
        {
            var exclusive = target == SemanticsKind.Uniq;
            var unwrapped = KotoHelper.UnwrapParentheses(source);
            if (this.BorrowablePlace(source, scope, exclusive))
            {
                // SPEC 15.1.5 lending rule: an owned Place is lent exclusively by @uniq at every position other
                // than a Receiver Expression, whatever its access path; a receiver is acquired implicitly (SPEC 7.3).
                if (exclusive && !explicitBorrow && !receiver)
                {
                    this.lendingRequired = true;
                    return false;
                }
            }
            else if (!((source.BoundSymbol is null || unwrapped is InvocationKoto) &&
                (!exclusive || ((explicitBorrow || receiver) && !(unwrapped is BinaryKoto stored && ElementAccess.IsSyntax(stored)))) &&
                !(unwrapped is MemberAccessKoto tupleElement && ReferenceTypes.IsTuple(tupleElement.Left.BoundType)) &&
                unwrapped is not IdentifierNameKoto && source.BoundType is { } temporary && !ReferenceEquals(temporary, BoundType.Never)) &&
                !(target == SemanticsKind.Ref && IsUnfittedLiteral(source)))
            {
                return false;
            }

            // SPEC 10.2: an owner temporary, including a defaulted literal, may be shared-borrowed; its
            // exclusive borrow is explicit or implicit for a receiver (SPEC 3.6.2, 7.3). A getter result
            // is a MemberAccessKoto with a Symbol and is never exclusively acquired (SPEC 11.2.3).
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

    private bool AdaptObjectBorrow(Koto source, BoundType pattern, BoundType actual, BindingScope scope, bool explicitOwner, out BoundType adapted, out ArgumentAdaptation quality, out ArgumentOperationKind kind, bool receiver = false)
    {
        adapted = actual;
        quality = ArgumentAdaptation.Exact;
        kind = ArgumentOperationKind.Value;
        var exclusive = pattern.Semantics == SemanticsKind.ObjUniq;
        if (ObjectTypes.IsBorrow(actual))
        {
            if (exclusive && (actual.Semantics != SemanticsKind.ObjUniq || ReachedThroughShared(source)))
            {
                return false;
            }

            if (actual.Semantics == SemanticsKind.ObjRef)
            {
                return true;
            }

            kind = ArgumentOperationKind.Reborrow;
            quality = exclusive ? ArgumentAdaptation.SameSemanticsReborrow : ArgumentAdaptation.CrossSemanticsBorrow;
        }
        else if ((actual.Semantics == SemanticsKind.Obj || (explicitOwner && !exclusive && actual.Semantics is SemanticsKind.Rc or SemanticsKind.Arc)) &&
            this.BorrowablePlace(source, scope, exclusive))
        {
            if (exclusive && !explicitOwner && !receiver)
            {
                // SPEC 15.1.5 lending rule: an owned handle is lent exclusively by @objuniq except as a receiver (SPEC 7.3).
                this.lendingRequired = true;
                return false;
            }

            kind = ArgumentOperationKind.Borrow;
            quality = ArgumentAdaptation.CrossSemanticsBorrow;
        }
        else
        {
            return false;
        }

        adapted = this.InternType(BoundTypeKind.Semantics, null, pattern.Semantics, [actual.Components[0]], origin: this.PlaceOrigin(source));
        return true;
    }
}
