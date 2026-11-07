// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The storage a value of a Type holds, SPEC 3.3.4's table (G416's unit S writes it; fact.md rows 1-21) one level at a time (DECISIONS BU-41, BU-46, BU-58, BU-91).
// Responsibility: the one reading of a held part's step, flags and stater, each row a value of Hold.
// Inputs: the Type and, for a stored part, the Type its declaration writes there and the holding instance (the keys a reader's memo holds).
// Outputs: a run appended to the caller's list, in stack order; false at Binding's Limit (a struct's or enum's storage past its limits),
// whose entries stay published: a reader with a bound of its own reads them, a fold reads false first.
// Invariants: total, every form has a row and an unlisted form one Unsupported entry; a function of (type, written, holder) only, so each
// reader's memo stays sound (R4-M151); O(entries); allocates nothing when warm. A stored raw layer's step is DeclaredStep's, the one
// function StoredStep's path query reads, so the per-level and per-path answers cannot disagree.
// Must not: decide a fit, a row, a Loan or a write fact; read a call's argument, a solved Origin, the premise closure or a signature's
// scope (an output fold steps every raw layer by RawStepAt, the cap of a contract's walk, DECISIONS BU-113);
// read a raw Origin itself. No reader tests `Semantics`, `Kind`, `IsAbstract` or a layer's Origin to decide a step, a flag or a stater.

/// <summary>A held part's path kind and its access (SPEC 15.6.2, 5.2.1 condition 3, 5.7; DECISIONS BU-24, BU-113). The five values
/// a raw layer takes come first, in the order SignatureStep's join reads; the step of a stored raw layer is an over-approximation that the safe
/// analyses read and no diagnostic reads (<see cref="Binding.StepOf"/>, <see cref="Binding.StoredStep"/>,
/// <see cref="Binding.RawStepAt"/>).</summary>
internal enum HoldStep : byte
{
    /// <summary>A raw layer stating no Origin: the pointer reaches what its value carries, storage the layer does not state.</summary>
    Address,

    /// <summary>An own step past a layer: `during self` in a stored shape, an owning handle's payload, a collection's elements.</summary>
    Own,

    /// <summary>`during static`: static storage.</summary>
    Static,

    /// <summary>A shared dereference. A raw layer has read authority only: a Field's or Case payload's layer stating a slot whose Loan
    /// requirement is `ref` in every case its holder selects, a layer the walked signature declares whose Origin its declared inputs state
    /// only by shared borrows and `ref` slots, or a Type argument's layer whose Origin its binder states so.</summary>
    Shared,

    /// <summary>An exclusive dereference. At a raw layer, any other Origin: a write through the pointer may be defined.</summary>
    Exclusive,

    /// <summary>A part at the value's own address.</summary>
    Inline,

    /// <summary>A dependency with no Places and no access (SPEC 15.3.5): a Type argument.</summary>
    Loan,

    /// <summary>An environment certified at erasure, reached only through its callable.</summary>
    Erased,

    /// <summary>What a binding hides (SPEC 8.15.1).</summary>
    Abstract,

    /// <summary>A form with no row: each reader's undecided outcome.</summary>
    Unsupported,
}

/// <summary>What the storage past a held part can hold (SPEC 15.6.4, 5.7).</summary>
internal enum HoldContent : byte
{
    /// <summary>Values of the part's Type, read on through that Type's entries.</summary>
    Its,

    /// <summary>Any value: storage a raw layer does not state, or a part Binding did not bind.</summary>
    Any,

    /// <summary>Any value whose Origins are in the holder's set.</summary>
    AnyOf,

    /// <summary>Any value of an `Owned` Type (SPEC 15.2.3).</summary>
    Owned,

    /// <summary>What the Referent Types of the borrows the layer's Origin belongs to admit (SPEC 5.7 Origins).</summary>
    Members,

    /// <summary>SPEC 15.6.4's abstract-Type sentence.</summary>
    Premised,

    /// <summary>Nothing a reader tracks.</summary>
    Nothing,
}

/// <summary>A held part's marks, each with its readers.</summary>
[Flags]
internal enum HoldFlags : byte
{
    /// <summary>No mark.</summary>
    None = 0,

    /// <summary>A raw layer states the entry: Places past it are of any Type whose bytes fit (SPEC 5.2).</summary>
    Raw = 1,

    /// <summary>An rc or arc view: no mutation through it but the destruction its final release runs (SPEC 15.6.2, 16.3.3).</summary>
    ReadOnly = 2,

    /// <summary>Elements whose number may be other than one.</summary>
    Repeated = 4,

    /// <summary>On an <see cref="HoldContent.AnyOf"/> entry: some Origin of its set may keep an exclusive Loan.</summary>
    ExclusiveOrigin = 8,
}

/// <summary>One part or layer a value of a Type holds: read on through <see cref="Binding.Holdings"/> with (Part, Written, Holder).</summary>
/// <param name="Part">The Type past the step; null for storage no Type states (AnyOf) and a part Binding did not bind.</param>
/// <param name="Step">The path kind and access.</param>
/// <param name="Holds">What the storage past the step can hold.</param>
/// <param name="Flags">The entry's marks.</param>
/// <param name="Written">The Type a declaration writes there.</param>
/// <param name="Holder">The instance holding the stored part.</param>
internal readonly record struct Hold(BoundType? Part, HoldStep Step, HoldContent Holds, HoldFlags Flags, BoundType? Written, BoundType? Holder);

public sealed partial class Binding
{
    // SPEC 3.3.4: what a value of a Type holds, one level: its entries appended to `into`, stated by the Type or, for a stored part, by the
    // declaration writing it there. False at Binding's Limit.
    internal bool Holdings(BoundType? type, BoundType? written, BoundType? holder, List<Hold> into)
    {
        if (type is null)
        {
            into.Add(new(null, HoldStep.Inline, HoldContent.Any, HoldFlags.None, written, holder)); // A part Binding did not bind (BU-36).
            return true;
        }

        if (TryPairLayer(type, out _, out var target))
        {
            this.PairHoldings(type, target, into);
            return true;
        }

        if (type.IsAbstract)
        {
            into.Add(new(type, HoldStep.Abstract, HoldContent.Premised, HoldFlags.None, written, holder));
            return true;
        }

        switch (type.Kind)
        {
            case BoundTypeKind.Semantics when type is { Semantics: SemanticsKind.Raw, Components: [var pointee] }:
                this.RawHoldings(type, pointee, written, holder, into);
                return true;
            case BoundTypeKind.Semantics when type.Components is [var referent]:
                Layer(type.Semantics, referent, HoldFlags.None, holder, into);
                return true;
            case BoundTypeKind.Tuple:
                InlineParts(type, written, holder, HoldFlags.None, into);
                return true;
            case BoundTypeKind.FixedArray:
                InlineParts(type, written, holder, type is { LengthExpression: null, Length: 1 } ? HoldFlags.None : HoldFlags.Repeated, into);
                return true;
            case BoundTypeKind.Closure:
                InlineParts(type, null, null, HoldFlags.None, into); // A function of the given Type; a fold asks with the body's environment.
                return true;
            case BoundTypeKind.Function:
                into.Add(new(null, HoldStep.Erased, HoldContent.Nothing, HoldFlags.None, null, null)); // Owned at U5 (G416 D6).
                return true;
            case BoundTypeKind.Slice when type.Components is [var elements]:
                Layer(SemanticsKind.Ref, elements, HoldFlags.Repeated, null, into);
                return true;
            case BoundTypeKind.Array when type.Components is [var element]:
                into.Add(new(element, HoldStep.Own, HoldContent.Its, HoldFlags.Repeated, null, holder));
                return true;
            case BoundTypeKind.Dictionary when type.Components is [var key, var value]:
                into.Add(new(this.types.Tuple([key, value]), HoldStep.Own, HoldContent.Its, HoldFlags.Repeated, null, holder));
                return true;
            case BoundTypeKind.Primitive when TyCtxt.CoreContent(type) is { } content:
                into.Add(new(content, HoldStep.Own, HoldContent.Its, HoldFlags.Repeated, null, holder));
                return true;
            case BoundTypeKind.Primitive or BoundTypeKind.Range or BoundTypeKind.FunctionItem:
                return true;
            case BoundTypeKind.Nominal or BoundTypeKind.Constructed:
                return this.NominalHoldings(type, holder, into);
            default:
                into.Add(new(type, HoldStep.Unsupported, HoldContent.Nothing, HoldFlags.None, written, holder));
                return true;
        }
    }

    // SPEC 8.1.1, 3.3.1: a layer's entry by its Semantics (a pair layer's case by the same table), the reference and view partition.
    // An object view of an abstract or open Target adds the remainder at U5 (G416 §0).
    private static void Layer(SemanticsKind semantics, BoundType target, HoldFlags flags, BoundType? holder, List<Hold> into)
    {
        var (step, marks) = semantics switch
        {
            SemanticsKind.Owner => (HoldStep.Inline, HoldFlags.None),
            SemanticsKind.Ref or SemanticsKind.ObjRef => (HoldStep.Shared, HoldFlags.None),
            SemanticsKind.Uniq or SemanticsKind.ObjUniq => (HoldStep.Exclusive, HoldFlags.None),
            SemanticsKind.Obj => (HoldStep.Own, HoldFlags.None),
            SemanticsKind.Rc or SemanticsKind.Arc => (HoldStep.Own, HoldFlags.ReadOnly),
            SemanticsKind.Raw => (HoldStep.Exclusive, HoldFlags.Raw), // A pair's raw case, unstepped until U3b: not Holds' Members.
            _ => (HoldStep.Unsupported, HoldFlags.None),
        };
        into.Add(new(target, step, HoldContent.Its, marks | flags, null, holder));
    }

    // A Tuple's components, a fixed array's element, a Closure's environment bindings: each at the value's own address, by the Type the
    // declaration writes at its inline position.
    private static void InlineParts(BoundType value, BoundType? written, BoundType? holder, HoldFlags flags, List<Hold> into)
    {
        for (var i = 0; i < value.Components.Count; i++)
        {
            into.Add(new(value.Components[i], HoldStep.Inline, HoldContent.Its, flags, InlinePart(written, i), holder));
        }
    }

    // SPEC 8.1.1: a pair layer holds one entry per admitted case.
    private void PairHoldings(BoundType type, BoundType target, List<Hold> into)
    {
        var admitted = this.LayerAdmitted(type);
        foreach (var semantics in PairCases)
        {
            if ((admitted & semantics.ToMask()) != 0)
            {
                Layer(semantics, target, HoldFlags.None, null, into);
            }
        }
    }

    // SPEC 5.7, 5.2: what a raw layer reaches, by its step (DeclaredStep): `self` the pointee and, by the holder's Origin arguments, any
    // value of them, else its Type arguments (SelfAdmitted); `static` the pointee; any other Origin what the borrows it belongs to admit;
    // none the pointee, or, where a declaration wrote the layer at any depth of a kept part (TyCtxt.RawStated, BU-46), storage it does not
    // state.
    private void RawHoldings(BoundType layer, BoundType pointee, BoundType? written, BoundType? holder, List<Hold> into)
    {
        switch (this.DeclaredStep(layer, written, holder))
        {
            case HoldStep.Own:
                into.Add(new(pointee, HoldStep.Own, HoldContent.Its, HoldFlags.Raw, null, null));
                if (SelfAdmitted(holder, out var arguments))
                {
                    into.Add(new(null, HoldStep.Own, HoldContent.AnyOf, HoldFlags.Raw | HoldFlags.ExclusiveOrigin, null, holder));
                }

                for (var i = 0; i < arguments.Count; i++)
                {
                    into.Add(new(arguments[i], HoldStep.Own, HoldContent.Its, HoldFlags.Raw, null, null));
                }

                break;
            case HoldStep.Static:
                into.Add(new(pointee, HoldStep.Static, HoldContent.Its, HoldFlags.Raw, null, null));
                break;
            case HoldStep.Address:
                into.Add(new(pointee, HoldStep.Address, TyCtxt.RawStated(layer) ? HoldContent.Any : HoldContent.Its, HoldFlags.Raw, null, null));
                break;
            case var step:
                into.Add(new(pointee, step, HoldContent.Members, HoldFlags.Raw, written, holder));
                break;
        }
    }

    // A nominal Type: a UniqSlice as a view; HeapBuffer by SPEC 22.9 (its declared Fields are not read); else a struct or enum.
    private bool NominalHoldings(BoundType type, BoundType? holder, List<Hold> into)
    {
        if (type is { Symbol.LibraryDeclaration: KimiDeclarationId.UniqSlice, Components: [var elements], OriginArguments: [_] })
        {
            Layer(SemanticsKind.Uniq, elements, HoldFlags.Repeated, null, into);
            return true;
        }

        if (type.Symbol?.LibraryDeclaration == KimiDeclarationId.HeapBuffer)
        {
            into.Add(new(BoundType.Primitives["u8"], HoldStep.Own, HoldContent.Its, HoldFlags.Repeated, null, null));
            return true;
        }

        if (!AdtDef.IsStruct(type) && !AdtDef.IsEnum(type))
        {
            into.Add(new(type, HoldStep.Unsupported, HoldContent.Nothing, HoldFlags.None, null, null));
            return true;
        }

        return this.StoredHoldings(type, holder, into);
    }

    // A struct's or enum's Type arguments (SPEC 15.3.5: a dependency, no Places), base, Fields and every substituted Case's payloads, in the
    // instance's kept parts, each holding through the Type its declaration writes there.
    private bool StoredHoldings(BoundType type, BoundType? holder, List<Hold> into)
    {
        var readable = this.Readable(type, this.PrepareTypeStorage(type));
        for (var i = 0; i < type.Components.Count; i++)
        {
            into.Add(new(type.Components[i], HoldStep.Loan, HoldContent.Its, HoldFlags.None, null, holder));
        }

        var (stored, adt) = (this.Instance(type), type.Symbol!.Adt!);
        if (stored.PartsBase is { } parent)
        {
            into.Add(new(parent, HoldStep.Inline, HoldContent.Its, HoldFlags.None, null, null));
        }

        for (var i = 0; AdtDef.IsStruct(type) && i < adt.Fields.Length; i++)
        {
            Stored(stored.Parts[i], WrittenType(adt.Fields[i]), type, into);
        }

        for (var i = 0; AdtDef.IsEnum(type) && i < stored.Parts.Length; i++)
        {
            for (var j = 0; stored.Parts[i] is { } payloads && adt.Cases[i] is { } selected && j < selected.Payload.Length; j++)
            {
                Stored(payloads.Components[j], WrittenType(selected.Payload[j]), type, into);
            }
        }

        return readable;

        static void Stored(BoundType? part, BoundType? written, BoundType holder, List<Hold> into)
            => into.Add(new(part, HoldStep.Inline, part is null ? HoldContent.Any : HoldContent.Its, HoldFlags.None, written, holder));
    }
}
