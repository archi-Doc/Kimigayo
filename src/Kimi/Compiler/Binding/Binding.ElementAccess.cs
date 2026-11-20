// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Element access (SPEC 3.4.3, 4.6-4.8, 13.4, 15.1.3, 15.6): what a selection is as an element or a Place read in place.
// - Responsibility: the element-access predicates that Binding's Place classification and the HIR publisher read (a raw Place is
//   Binding.PlaceAuthority.Raw's): shared and exclusive elements, borrowed receivers, built-in steps and user indexes, slicing, static
//   selectors and constant indexes, the access Type of a Place read in place, and a path's owned, borrowed and writable roots.
// - Inputs: a node, its published Type, symbol and plan (HirTables), and this Binding's facts: recorded adaptations, selected index
//   and view-range calls, resolved slices, path and referent authority, object cases and pair-layer borrows (BorrowsInEveryCase), a
//   node's constraint scope, an operand's terminal Type (OperandTerminal) and the Binding's TyCtxt (Types.Reference,
//   AdtDef.FindField); never a node's own compilation (DECISIONS R8-M51).
// - Outputs: the answers; nothing here writes a fact or reports a diagnostic.
// - Invariants: the readers are Binding's own steps and the HIR publisher, which reads the Binding it publishes.
// - Prohibitions: no member fetches a Binding from a node.
public sealed partial class Binding
{
    private long reachWalkSteps;

    /// <summary>Gets the steps ReachesThroughBorrow took (one per selection it judges), which a scaling check reads.</summary>
    internal long ReachWalkWork => this.reachWalkSteps;

    // SPEC 15.1.3: a constant index's value, else -1: one nonnegative integer literal (any base) in optional grouping parentheses that
    // fits i32.
    private static int Literal(Koto key) => KotoHelper.UnwrapParentheses(key) is NumberLiteralKoto { IsInteger: true } number &&
        number.TryGetIntegerMagnitude(out var magnitude) && magnitude <= int.MaxValue ? (int)magnitude : -1;

    // One step of ReachesThroughBorrow: a selection at no range position that is no getter result; null for any other node.
    private static BinaryKoto? ReachStep(Koto source)
        => KotoHelper.UnwrapParentheses(source) is BinaryKoto selection && selection is IndexKoto { Right: not RangeKoto } or MemberAccessKoto &&
            !IsGetterResult(selection) ? selection : null;

    // SPEC 4.8.2, 4.7.1: an element of a Slice or of a referenced array, including a fixed array reached through a
    // borrowed receiver; no owned root holds it.
    private bool IsSharedElement(Koto source) => source is IndexKoto { Right: not RangeKoto } element &&
        (HirTables.TypeOf(element.Left)?.Kind == BoundTypeKind.Slice || this.AccessType(element.Left)?.SafeReferent is { Kind: BoundTypeKind.FixedArray } ||
            HirTables.TypeOf(element.Left)?.SafeReferent is { Kind: BoundTypeKind.Array or BoundTypeKind.Dictionary } ||
            (HirTables.TypeOf(element.Left)?.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray && this.ReachesThroughBorrow(element.Left)));

    // SPEC 3.4.3: the referent of an exclusive array reference offers element replacement, without requiring a mutable
    // binding for the reference itself. Shared layers on its path are checked separately by Binding.
    // An owned Array or fixed array stored below exclusive references only (`b.items[i]` with `b: uniq/Bank`) offers the same, whose
    // path authority (receiverPath, the access its receiver's path grants: PathFold) decides it rather than the stored Type (PLAN G59).
    private bool IsExclusiveArrayElement(IndexKoto element, SemanticsKind receiverPath) => element.Right is not RangeKoto && !this.IsSlicing(element) &&
        ((this.ExclusiveAccessType(element.Left, this.ReferentAuthority(element.Left, receiverPath)) is { Semantics: SemanticsKind.Uniq } receiver &&
            receiver.SafeReferent is { Kind: BoundTypeKind.Array or BoundTypeKind.FixedArray or BoundTypeKind.Dictionary }) ||
        (HirTables.TypeOf(element.Left)?.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray && this.ReachesThroughBorrow(element.Left) &&
            receiverPath == SemanticsKind.Uniq));

    // SPEC 4.7.1: a receiver whose selected Place is reached by a dynamic key or through a reference: a Slice, a dynamic
    // Array, a Dictionary or a borrow.
    private bool IsBorrowedReceiver(Koto receiverNode)
        => this.AccessType(receiverNode) is var receiver &&
            (receiver is { Kind: BoundTypeKind.Slice or BoundTypeKind.Array or BoundTypeKind.Dictionary } || this.BorrowsInEveryCase(receiver, receiverNode));

    // SPEC 4.8.2, 4.7.1: whether a selection reaches its Place through a Slice or a borrow, so that no owned root holds it.
    private bool ReachesThroughBorrow(Koto source)
    {
        while (ReachStep(source) is { } selection)
        {
            this.reachWalkSteps++;
            if (this.BorrowsAt(selection))
            {
                return true;
            }

            source = selection.Left;
        }

        return false;
    }

    // Whether a selection's receiver is a Slice or borrows in every case, so that the selection reaches its Place through it.
    private bool BorrowsAt(BinaryKoto selection)
        => this.AccessType(selection.Left) is var receiver && (receiver?.Kind == BoundTypeKind.Slice || this.BorrowsInEveryCase(receiver, selection.Left));

    private bool IsSyntax(Koto source) => source is IndexKoto index ? !this.IsUserIndex(index) && this.ViewRangeCall(index) is null : source is MemberAccessKoto { Right: NumberLiteralKoto } ||
        (source is MemberAccessKoto member && HirTables.Symbol(member) is { Property.IsStored: true } && AdtDef.IsStruct(HirTables.TypeOf(member.Left)));

    // SPEC 3.4, 4.7.1, 15.6.2: an inline part, the one statement of a built-in step into an owned value (NextPathStep's InlinePart): a
    // built-in selection (IsSyntax) whose receiver's owned Type has the part (TryType): a stored Field of a struct, a custom getter's
    // result included (IsSyntax admits every stored Property, so NextPathStep steps it here where its Member case stops, DECISIONS
    // BU-24), a Tuple element in range, or an index no user indexer or view range resolves, of a fixed array or Array whatever its key
    // (a range included) or of a Dictionary. A reader of a narrower set states its further condition beside the call.
    private bool IsInlinePart(BinaryKoto part) => this.IsSyntax(part) && this.TryType(part, out _, out _);

    // SPEC 4.7.1: receiver[key] resolved through a user Indexable conformance: the Binding synthesized its index call, and
    // its indexUniq call where the use may update or borrow exclusively. The expression designates the referent of the reference that
    // call returns, not an element projection.
    private bool IsUserIndex(Koto source) => KotoHelper.UnwrapParentheses(source) is IndexKoto index && this.IndexerCall(index, false) is not null;

    // SPEC 4.8.2: a range selection, written with a range or applied through a ResolvedRange key, produces a Slice value,
    // not a Place.
    private bool IsSlicing(Koto source)
        => source is IndexKoto slice && HirTables.TypeOf(slice)?.Kind == BoundTypeKind.Slice && (slice.Right is RangeKoto || this.IsResolvedSlice(slice));

    // SPEC 15.1.3: literal-only recognition; never use folded values or named constants. A fixed-array element's Move Path: a constant
    // index below the length of a fixed array the receiver's own Type is.
    private int StaticSelector(BinaryKoto source)
    {
        if (source is MemberAccessKoto && this.TryType(source, out _, out var position))
        {
            return position;
        }

        return source is IndexKoto && HirTables.TypeOf(source.Left) is { Kind: BoundTypeKind.FixedArray, Length: >= 0 } array &&
            Literal(source.Right) is >= 0 and var element && element < array.Length ? element : -1;
    }

    // SPEC 15.1.3, 15.6.2 (D-910): a constant index on a fixed-array element however the selection reaches the array (directly, below its
    // ref, uniq or qualifying pair layers, of a length parameter), in range or not: its value, which Place comparison reads; -1 for any
    // other key or receiver (an Array's or Slice's element has none: its access borrows the whole receiver, SPEC 4.7.1).
    private int ConstantIndex(IndexKoto index)
        => Literal(index.Right) is >= 0 and var element && HirTables.TypeOf(index.Left) is { } receiver &&
            this.OperandTerminal(index.Left, receiver).Kind == BoundTypeKind.FixedArray ? element : -1;

    private IdentifierNameKoto? WritableRoot(Koto source)
    {
        source = KotoHelper.UnwrapParentheses(source);
        if (!this.IsSyntax(source))
        {
            return null;
        }

        while (source is BinaryKoto element && this.IsInlinePart(element))
        {
            source = KotoHelper.UnwrapParentheses(element.Left);
        }

        // Pattern body bindings have their own local identity; a var pattern
        // permits writes to its acquired value just like a var declaration.
        // Guard candidates remain excluded by their distinct symbol kind.
        if (source is not IdentifierNameKoto root || HirTables.Symbol(root) is not { Kind: BindingSymbolKind.Local or BindingSymbolKind.Capture } symbol)
        {
            return null;
        }

        return Reassignable(symbol) ? root : null;
    }

    /// <summary>
    /// SPEC 3.4.3, 13.4: the Type through which a Place expression read in place, a receiver or a string comparison
    /// operand, is accessed. Through several reference layers, or for a shared element Place, it is the one reference
    /// that Binding recorded as the expression's adaptation.
    /// </summary>
    /// <param name="node">The expression read in place.</param>
    /// <returns>The access Type.</returns>
    private BoundType? AccessType(Koto node)
        => this.TryGetAdaptation(node, out var adaptation) && adaptation.Kind is ExpectedAdaptationKind.ReferenceRead or ExpectedAdaptationKind.SharedBorrow
            ? adaptation.Type : HirTables.TypeOf(node);

    // SPEC 3.4.3, 4.7.1, 15.6.2: the access Type of a Place a projection requiring exclusive access selects from. A synthesized shared
    // access does not decide the later projection's capability; the final acquisition does. An exclusive use takes an indexer's bound
    // exclusive entry, or a synthesized access exclusively when `referent`, the access its real path continued through the Place's own
    // layers grants (ReferentAuthority), is exclusive, with the same Origin; a shared layer of the Place's own Type never does. The
    // caller reads `referent` from its path fold (PathFold, D-941), never by a new query on the Place's path.
    private BoundType? ExclusiveAccessType(Koto node, SemanticsKind referent)
        => this.TryGetAdaptation(node, out var adaptation) && adaptation.Kind == ExpectedAdaptationKind.SharedBorrow && !HasSharedLayer(HirTables.TypeOf(node)) &&
            (this.IndexerCall(node, true) is not null || (this.IsSynthesizedAccess(node) && referent == SemanticsKind.Uniq))
            ? this.Types.Reference(SemanticsKind.Uniq, adaptation.Type.Components[0], adaptation.Type.Origin) : this.AccessType(node);

    // SPEC 3.4.3: a shared borrow that Binding synthesized to read a Place in place, of the Place's own complete stored Type or of
    // the Type its safe reference layers reach (Binding.ElementRead), is a representation of the selection rather than a reference
    // layer on its path.
    private bool IsSynthesizedAccess(Koto node)
        => this.TryGetAdaptation(node, out var adaptation) &&
            adaptation is { Kind: ExpectedAdaptationKind.SharedBorrow, Type.Components: [var read] } && HirTables.TypeOf(node) is { } type &&
            (ReferenceEquals(read, type) || ReferenceEquals(read, ComparisonReferent(type)));

    // SPEC 15.6: a direct field/Tuple path whose base is a borrowed struct or
    // Tuple reference; nested levels must be inline stored parts. Returns the
    // reference-typed base, or null for other forms.
    // The reference whose struct or Tuple referent `reference@follow` selects, or null.
    private Koto? FollowedReference(Koto receiver)
        => KotoHelper.UnwrapParentheses(receiver) is ConversionKoto followed && HirTables.PlanOf(followed).Conversion == ConversionBinding.Follow &&
            this.AccessType(followed.Left)?.SafeReferent is { } referent && (AdtDef.IsStruct(referent) || referent.Kind == BoundTypeKind.Tuple) ? followed.Left : null;

    // SPEC 4.7.1: an owned fixed-array element selected at a runtime index, which has no static Move Path.
    private bool IsRuntimeFixedElement(Koto selection)
        => KotoHelper.UnwrapParentheses(selection) is IndexKoto element && this.IsSyntax(element) && !this.IsSlicing(element) && !IsGetterResult(element) &&
            HirTables.TypeOf(element.Left)?.Kind == BoundTypeKind.FixedArray && this.StaticSelector(element) < 0;

    // SPEC 3.4.3: the borrowed base an inline Tuple level is selected from (Writes' borrowed-base rule). It takes one step: its one
    // caller reaches it only where the inline-part rule did not answer, so TryType of the node is false, and a second iteration needs
    // it true (the Front's D-932 read).
    private Koto? BorrowedPathRoot(MemberAccessKoto field)
    {
        while (true)
        {
            if (IsGetterResult(field))
            {
                return null;
            }

            var receiver = this.AccessType(field.Left);
            if (AdtDef.IsStruct(receiver?.SafeReferent) || receiver?.SafeReferent is { Kind: BoundTypeKind.Tuple } ||
                (receiver is not null && this.ObjectCases(receiver, this.ConstraintScope(field.Left), out _) != SemanticsMask.None))
            {
                return field.Left;
            }

            // SPEC 13.5.5: an explicitly selected referent (`p@follow.x`) is reached through its reference, as `p.x` is.
            if (this.FollowedReference(field.Left) is { } reference)
            {
                return reference;
            }

            if (field.Left is not MemberAccessKoto parent || !this.TryType(field, out _, out _))
            {
                return null;
            }

            field = parent;
        }
    }

    // SPEC 15.6: a direct inline field/Tuple/static-array path whose root is an owned local
    // or parameter Name. Returns that Name, or null for other forms.
    private IdentifierNameKoto? OwnedPathRoot(BinaryKoto field)
    {
        while (true)
        {
            if (IsGetterResult(field) || !this.IsSyntax(field) || this.StaticSelector(field) < 0)
            {
                return null;
            }

            var receiver = KotoHelper.UnwrapParentheses(field.Left);
            if (receiver is IdentifierNameKoto root &&
                HirTables.Symbol(root) is { Kind: BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture })
            {
                return root;
            }

            if (receiver is not BinaryKoto parent)
            {
                return null;
            }

            field = parent;
        }
    }

    private bool TryBorrowedTupleElement(BinaryKoto source, out BoundType? element, out int position)
    {
        element = null;
        position = -1;
        var receiver = this.AccessType(source.Left);
        if (receiver?.SafeReferent is { Kind: BoundTypeKind.Tuple } tuple && source is MemberAccessKoto { Right: NumberLiteralKoto number } &&
            number.IsInteger && number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)tuple.Components.Count)
        {
            position = (int)magnitude;
            element = tuple.Components[position];
            return true;
        }

        return false;
    }

    private bool TryType(BinaryKoto source, out BoundType? element, out int position)
    {
        element = null;
        position = -1;
        var type = HirTables.TypeOf(source.Left);
        if (type?.Semantics != SemanticsKind.Owner)
        {
            return false;
        }

        if (source is MemberAccessKoto && HirTables.Symbol(source) is { Property.IsStored: true } && AdtDef.IsStruct(type))
        {
            return AdtDef.FindField(this.Types, type, HirTables.Symbol(source), out element, out position);
        }

        if (source is IndexKoto && type.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array && type.Components.Count == 1)
        {
            element = type.Components[0];
            return true;
        }

        if (source is IndexKoto && type.Kind == BoundTypeKind.Dictionary && type.Components.Count == 2)
        {
            element = type.Components[1];
            return true;
        }

        if (source is MemberAccessKoto { Right: NumberLiteralKoto number } && type.Kind == BoundTypeKind.Tuple &&
            number.IsInteger && number.TryGetIntegerMagnitude(out var magnitude) && magnitude < (ulong)type.Components.Count)
        {
            position = (int)magnitude;
            element = type.Components[position];
            return true;
        }

        return false;
    }
}
