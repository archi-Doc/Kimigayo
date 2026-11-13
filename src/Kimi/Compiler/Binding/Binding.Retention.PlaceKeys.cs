// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A Place's key by its path, and the layers at a Place (SPEC 5.7 Origins, Reading a Field, Writing a Field; C-189; contract in
// TyCtxt.Raw.cs). Responsibility: the one step of a Place's path (Select, TyCtxt.Base), from a root's key (PlaceKey.Root), and the parts
// the value at a key holds at its own address with each raw layer's step at the Place (OwnLayers, over the storage walk's OwnParts).
// Inputs: Holdings' entries and the stored parts Binding's instances keep (Instance, WrittenAt); a path's dereference is PathOrigin's one
// rule (Dereferenced over Reached, Binding.PlaceAuthority.Raw.cs), a `during self` layer read as ReadHolder reads it (ReplaceHolder).
// Outputs: a key per Place (Types only), and per key its layers with their steps and the key past each.
// Invariants: a function of the key (and of the selected part); total over what Binding accepts (an unbound part: Type null, one
// Unsupported entry); allocates nothing warm. The `self` test reads the step Holdings publishes (Own; Own with Raw on a past entry),
// never the raw Origin role (DECISIONS BU-24).
// Must not: decide a pun, a covering or a designation (A5's), read a solved Origin or a call's arguments.

/// <summary>A Place's key by its path from a root (SPEC 5.7 Origins, Reading a Field, Writing a Field; C-189): Holdings' key (the kept
/// Type there, the Type a declaration writes there, the instance holding it), the Case whose payload Tuple the Place is (-1 otherwise; its
/// components' written Types are the Case's), and <see cref="Through"/>: the last raw, borrow or pair layer the path dereferences, as the
/// path reads it (a `during self` layer with the Origin reading gives), null where it dereferences none. The path's dereference is
/// Binding's of that layer (Dereferenced, PathOrigin's one rule); the key holds Types only, as every MIR Type does.</summary>
internal readonly record struct PlaceKey(BoundType? Type, BoundType? Written, BoundType? Holder, int Case, BoundType? Through)
{
    /// <summary>Gets a root's key: a local's, a parameter's or a call input's Type, reached through no dereference.</summary>
    /// <param name="type">The root's Type.</param>
    /// <returns>The key.</returns>
    internal static PlaceKey Root(BoundType type) => new(type, null, null, -1, null);
}

/// <summary>A raw Place directly within a raw Place (SPEC 5.2; TyCtxt.RawPlaces): its key, whether it is at the raw Place's own
/// address where IMPL 21.1 fixes it there, and whether it is a fixed array's element (every index, a grid of its stride).</summary>
/// <param name="Key">The part's key (Select's; Leading's for the element).</param>
/// <param name="Lead">At the raw Place's own address (Leading's step): a C-layout first Field; an element row always (element 0 is
/// there; whether a displaced Position stays on the element grid is the reader's step rule); false for a base's rows.</param>
/// <param name="Element">A fixed array's element.</param>
internal readonly record struct RawPlace(PlaceKey Key, bool Lead, bool Element);

/// <summary>What <c>TyCtxt.RawPlaces</c> answers about the raw Places within a raw Place (SPEC 5.2; the owner's O-228 (a), O-231 (a)).</summary>
internal enum RawPlacesAnswer : byte
{
    /// <summary>The rows are every raw Place within (a struct, Tuple or fixed array).</summary>
    Listed,

    /// <summary>Nothing within is a raw Place (an enum, a Closure, a form holding nothing inline); no row.</summary>
    Empty,

    /// <summary>Not known: the rows published (a struct, Tuple or fixed array at Binding's Limit or with an unbound part) may not be all,
    /// and any other undecided form publishes none; the reader keeps its own reading there (O-231 (a)), never "nothing within".</summary>
    Undecided,
}

public sealed partial class Binding
{
    // SPEC 5.2.1 Retained dependencies, 5.7 Calls, 15.6.4 (PLAN G583, G598, C-189; contract in TyCtxt.Raw.cs): the parts the value at a
    // Place's key holds at its own address, by ByValue at any holder, each part as the body sees it (BodySees, as the storage walk's
    // BodyRun): a raw layer met there with its step at the Place (LayerStep), not gone past; any other part that reaches storage with no
    // layer; nothing for a part with no entry or a Type argument. A Case key walks the payload's components with the Case's written Types,
    // as Holdings does. False at Binding's Limit, the storage walk's.
    internal bool OwnLayers(PlaceKey at, List<OwnLayer> into)
    {
        this.ownSeen.Clear();
        if (at.Type is null)
        {
            into.Add(new(null, HoldStep.Unsupported, default)); // A part Binding did not bind (BU-36).
            return true;
        }

        var complete = true;
        for (var j = 0; at.Case >= 0 && complete && j < at.Type.Components.Count; j++)
        {
            complete = this.OwnParts(at.Type.Components[j], WrittenAt(at.Holder!, at.Case, [j], out _), at.Holder, at, into);
        }

        return at.Case >= 0 ? complete : this.OwnParts(at.Type, at.Written, at.Holder, at, into);
    }

    // SPEC 5.7 (C-189): the key of the Place `part` selects from `at`'s Place, one step of a path, `part` numbered as MIR numbers it (a
    // struct's Field, an enum's Case, a Tuple's or Closure's component; read nowhere else). Inline parts keep the path's dereference: a
    // struct's Field (its kept part: a slot is replaced by the holding value's binding), an enum's Case payload (Case), a component, a fixed
    // array's element. Any other Type has one Place at or past its own address, which Holdings names (a raw pointer's pointee, a borrow's
    // or view's referent, an owning handle's payload, a collection's element, a pair layer's target), reached with PastThrough.
    internal PlaceKey Select(PlaceKey at, int part)
    {
        if (at.Type is not { } type)
        {
            return at with { Case = -1 };
        }

        if (at.Case >= 0)
        {
            return at with { Type = InlinePart(type, part), Written = WrittenAt(at.Holder!, at.Case, [part], out _), Case = -1 };
        }

        if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
        {
            var parts = this.Instance(type).Parts;
            var stored = (uint)part < (uint)parts.Length ? parts[part] : null;
            return AdtDef.IsStruct(type) ? at with { Type = stored, Written = WrittenAt(type, part, [], out _), Holder = type }
                : at with { Type = stored, Written = null, Holder = type, Case = part };
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure)
        {
            var sees = this.BodySees(type); // A Closure's part as OwnLayers reads it (R4-M148), one reading.
            var inline = sees is null ? null : InlinePart(sees, part) ?? ((uint)part < (uint)sees.Components.Count ? sees.Components[part] : null);
            return at with { Type = inline, Written = InlinePart(at.Written, part) };
        }

        var start = this.retentionHeld.Count;
        _ = this.Holdings(type, at.Written, at.Holder, this.retentionHeld);
        var past = new PlaceKey(null, null, null, -1, at.Through);
        for (var i = start; i < this.retentionHeld.Count; i++)
        {
            if (this.retentionHeld[i] is { Step: not HoldStep.Loan, Part: { } target } entry)
            {
                past = new(target, entry.Written, entry.Holder, -1, this.PastThrough(type, entry, at));
                break;
            }
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        return past;
    }

    // SPEC 5.7 Reading a Field (C-189): the path's last dereferenced layer past a value of `prefix`, whose Holdings entry is `entry`. A
    // `during self` layer (Holdings steps it Own with Raw: DeclaredStep's `self`, review B5) is read with the path's Origin (ReplaceHolder,
    // as ReadHolder reads it; through a U2 path the path stays U2); any other value that dereferences (Dereferenced, PathOrigin's one
    // rule) is the new last layer; past any other (an owning handle's payload, a collection's element) the path's stays.
    private BoundType? PastThrough(BoundType prefix, in Hold entry, in PlaceKey at)
    {
        if (entry.Step != HoldStep.Own || (entry.Flags & HoldFlags.Raw) == 0)
        {
            return this.Dereferenced(prefix) is null ? at.Through : prefix;
        }

        var path = this.PathAt(at);
        return path.Dereferences is null ? at.Through : this.ReplaceHolder(prefix, path.Origin);
    }

    // SPEC 5.7 (C-189): a key's path dereference, PathOrigin's values: none at a root, else what its last dereferenced layer gives.
    private RawPath PathAt(in PlaceKey at) => at.Through is { } layer && this.Dereferenced(layer) is { } reached ? reached : new(false, null);

    // SPEC 5.7 Writing a Field (C-189): a raw layer's step at its Place, from its Holdings step `declared` (DeclaredStep under its holding
    // instance; Own exactly for a `during self` layer, review B5): through a path that dereferences, a `during self` layer has the step of the
    // Origin reading it gives (StepOf, BU-24's cap-free kind: past a `ref` it is Exclusive, an over-approximation of write authority, review
    // N12); through none it stays Own; through a U2 path Unsupported, which no reader takes for a stated Origin.
    private HoldStep LayerStep(HoldStep declared, in PlaceKey at)
        => declared != HoldStep.Own ? declared : this.PathAt(at) switch
        {
            { Dereferences: true, Origin: var origin } => StepOf(origin),
            { Dereferences: false } => declared,
            _ => HoldStep.Unsupported,
        };
}
