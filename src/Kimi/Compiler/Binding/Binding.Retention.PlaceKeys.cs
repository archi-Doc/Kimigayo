// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// A Place's key by its path, and the layers at a Place (SPEC 5.7 Origins, Reading a Field, Writing a Field; C-189; contract in
// TyCtxt.Raw.cs). Responsibility: the one step of a Place's path (Select, Base), from a root's key (PlaceKey.Root, or a called
// function's parameter key, TyCtxt.ParameterKey), and the parts the value at a key holds at its own address with each raw layer's step at
// the Place (OwnLayers, over OwnParts: the storage walk's by-value criterion, ByValue); on a parameter path, each key's declared
// counterpart (Signature) and whether a part lies in a binding the declaration does not show (OwnLayer.Hidden, SPEC 15.6.4; PLAN G823);
// at a key whose Type is a Closure, whether a part lies in `let` environment bindings only, which no call of that Closure replaces
// (OwnLayer.Let, SPEC 15.8.2; PLAN A5-F).
// Inputs: Holdings' entries and the stored parts Binding's instances keep (Instance, WrittenAt); a path's dereference is PathOrigin's one
// rule (Dereferenced over Reached, Binding.PlaceAuthority.Raw.cs), a `during self` layer read as ReadHolder reads it (ReplaceHolder); the
// declaring function's identity classes (Binding.TypeIdentity, the one judge: View).
// Outputs: a key per Place (Types only), and per key its layers with their steps, their Hidden and Let bits and the key past each; rows
// are listed once per (Type, Written, Holder, Signature, Let), so a parameter key may list a part a root key of the same Type lists once,
// and a Closure key lists a part held in a `let` and in a `var` binding twice, once with each Let.
// Invariants: a function of the key (and of the selected part); every producer of a key (Select, Base, Leading, a Past key) steps the
// Signature by the one rule (View, then Binding.Retention.Declared.cs's pairing and Below); Hidden only where the declared counterpart, in the declaring
// function's view, is an abstract Type of that function itself; total over what Binding accepts (an unbound part: Type null, one
// Unsupported entry); allocates nothing warm. The `self` test reads the step Holdings publishes (Own; Own with Raw on a past entry),
// never the raw Origin role (DECISIONS BU-24).
// Must not: decide a pun, a covering, a designation, what a hidden layer reaches or what a call writes through a Let part (A5's), read a
// solved Origin, a call's arguments or retention (the storage walk's limit included).

/// <summary>A Place's key by its path from a root (SPEC 5.7 Origins, Reading a Field, Writing a Field; C-189): Holdings' key (the kept
/// Type there, the Type a declaration writes there, the instance holding it), the Case whose payload Tuple the Place is (-1 otherwise; its
/// components' written Types are the Case's), and <see cref="Through"/>: the last raw, borrow or pair layer the path dereferences, as the
/// path reads it (a `during self` layer with the Origin reading gives), null where it dereferences none. The path's dereference is
/// Binding's of that layer (Dereferenced, PathOrigin's one rule); the key holds Types only, as every MIR Type does. <see cref="Signature"/>:
/// on a called function's parameter path (TyCtxt.ParameterKey), the Type its declaration writes at the Place, paired by the storage walk's
/// rule and carrier convention (StorageAt.Signature, DECISIONS BU-113: DeclaredRun, SignaturePast, Below) but read in the function's view
/// (View, SPEC 8.2.3), which the storage walk does not read: below a part an identity premise gives a form, this is the form's
/// counterpart where StorageAt.Signature keeps the abstract Type. Inside the binding of an abstract Type of that declaration it is the
/// abstract Type itself, kept below (SPEC 15.6.4: the binding is hidden; PLAN G823); null on any other path.</summary>
internal readonly record struct PlaceKey(BoundType? Type, BoundType? Written, BoundType? Holder, int Case, BoundType? Through, BoundType? Signature = null)
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

/// <summary>A part a value holds at its own address (<see cref="Binding.OwnLayers"/>; SPEC 5.2.1, 5.7, 15.6.4): a raw layer
/// (<see cref="Layer"/>) with its step at the Place (Holdings' under its holding instance; a `during self` layer's, where the path
/// dereferences, the step of the Origin reading it gives), or, Layer null, another part that reaches storage with its step;
/// <see cref="Past"/> the key of the Place past the step (a raw layer's pointee, a borrow's referent, an abstract part itself; Type null
/// where no Type states it).</summary>
internal readonly record struct OwnLayer(BoundType? Layer, HoldStep Step, PlaceKey Past)
{
    /// <summary>Gets a value indicating whether the part lies in the binding of an abstract Type of the called function's own declaration
    /// that its declared Type does not show (SPEC 15.6.4, 5.7 Calls; PLAN G823): only on a parameter path (TyCtxt.ParameterKey). What
    /// such a raw layer or reference reaches is the reader's (15.6.4: a raw layer adds no reach of its own; a safe reference keeps the
    /// routes).</summary>
    internal bool Hidden { get; init; }

    /// <summary>Gets a value indicating whether, at a key whose Type is a Closure (a root, Select or Past key alike), the part lies in a
    /// `let` binding of that Closure's environment and, below it, in a `let` binding of every nested Closure's environment its path
    /// enters, so that a call of that Closure cannot replace it (SPEC 15.8.2, 7.7.2: no Closure body can replace its environment; an
    /// Exclusive call may retain an argument in its `var` bindings only, and a `var` binding's value may be replaced whole, its nested
    /// `let` bindings with it; PLAN A5-F row 2 (ii)). It speaks only of a call of the key's own Closure: a call of an enclosing Closure or
    /// of any other function may replace that Closure whole through a `var` binding, a borrow or a raw layer above the key, so a reader
    /// reads it only at its Closure call's receiver referent. False at a key whose Type is no Closure and below a `var` binding. What a
    /// call writes through such a part is the reader's (A5's rule 4).</summary>
    internal bool Let { get; init; }
}

public sealed partial class Binding
{
    private readonly HashSet<(BoundType Type, BoundType? Written, BoundType? Holder, BoundType? Signature, bool Let)> ownSeen = new(); // OwnLayers' walked keys, reused.
    private readonly List<BoundType> viewMembers = new(); // View's scratch: an identity class's members.

    // SPEC 5.2.1 Retained dependencies, 5.7 Calls, 15.6.4 (PLAN G583, G598, C-189; contract in TyCtxt.Raw.cs): the parts the value at a
    // Place's key holds at its own address, by ByValue at any holder, each part as a contract reads it (ContractReading, as the storage
    // walk's ContractRun): a raw layer met there with its step at the Place (LayerStep), not gone past; any other part that reaches storage with no
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
        var payload = at.Case >= 0 ? this.View(at.Signature) : null;
        for (var j = 0; at.Case >= 0 && complete && j < at.Type.Components.Count; j++)
        {
            var signature = Below(payload, DeclaredComponent(payload, at.Type, j));
            complete = this.OwnParts(at.Type.Components[j], WrittenAt(at.Holder!, at.Case, [j], out _), at.Holder, signature, false, at, into);
        }

        return at.Case >= 0 ? complete : this.OwnParts(at.Type, at.Written, at.Holder, at.Signature, at.Type.Kind == BoundTypeKind.Closure, at, into);
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

        var declared = this.View(at.Signature);
        if (at.Case >= 0)
        {
            var signature = Below(declared, InlinePart(declared, part));
            return at with { Type = InlinePart(type, part), Written = WrittenAt(at.Holder!, at.Case, [part], out _), Case = -1, Signature = signature };
        }

        if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
        {
            var parts = this.Instance(type).Parts;
            var stored = (uint)part < (uint)parts.Length ? parts[part] : null;
            var field = Same(declared, type) && this.Instance(declared!).Parts is var written && (uint)part < (uint)written.Length ? written[part] : null;
            var signature = Below(declared, field);
            return AdtDef.IsStruct(type) ? at with { Type = stored, Written = WrittenAt(type, part, [], out _), Holder = type, Signature = signature }
                : at with { Type = stored, Written = null, Holder = type, Case = part, Signature = signature };
        }

        if (type.Kind is BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Closure)
        {
            var read = this.ContractReading(type); // A Closure's part as OwnLayers reads it (R4-M148), one reading.
            var inline = read is null ? null : InlinePart(read, part) ?? ((uint)part < (uint)read.Components.Count ? read.Components[part] : null);
            var signature = Below(declared, type.Kind != BoundTypeKind.Closure && Same(declared, type) ? InlinePart(declared, part) : null);
            return at with { Type = inline, Written = InlinePart(at.Written, part), Signature = signature };
        }

        var start = this.retentionHeld.Count;
        _ = this.Holdings(type, at.Written, at.Holder, this.retentionHeld);
        var past = new PlaceKey(null, null, null, -1, at.Through);
        for (var i = start; i < this.retentionHeld.Count; i++)
        {
            if (this.retentionHeld[i] is { Step: not HoldStep.Loan, Part: { } target } entry)
            {
                past = new(target, entry.Written, entry.Holder, -1, this.PastThrough(type, entry, at), this.SignaturePast(declared, type, entry));
                break;
            }
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        return past;
    }

    // SPEC 5.7 (C-189): the key of a struct's base Place (MIR's Base, once per depth): its kept base (StoredParts' parent), inline, the
    // path's dereference kept, the declared base where the constructors agree (Select's rule); Type null for any other Type.
    internal PlaceKey Base(PlaceKey at)
    {
        var (parent, declared) = ((BoundType?)null, this.View(at.Signature));
        BoundType? written = null;
        if (at.Type is { } type && AdtDef.IsStruct(type))
        {
            parent = this.Instance(type).PartsBase;
            written = Same(declared, type) ? this.Instance(declared!).PartsBase : null;
        }

        return at with { Type = parent, Written = null, Holder = null, Case = -1, Signature = Below(declared, written) };
    }

    // OwnLayers from a part (Type, Written, Holder) and the Type the called function's declaration writes there (Signature, read in the
    // function's view, View; paired by the storage walk's rule, DeclaredRun and SignaturePast; kept inside a hidden binding, Below), each
    // (Type, Written, Holder, Signature, Let) once per call; retentionHeld is read as a stack (each run removed). `let`: the part lies in
    // `let` bindings at every Closure environment from the key's Closure down (OwnLayer.Let; a Closure's entries are its bindings in
    // capture order, Holdings' InlineParts). It reads Holdings only, never HeldRun, so it sets no storage-walk state (retentionLimit).
    private bool OwnParts(BoundType type, BoundType? written, BoundType? holder, BoundType? signature, bool let, in PlaceKey at, List<OwnLayer> into)
    {
        signature = this.View(signature);
        if (!this.ownSeen.Add((type, written, holder, signature, let)))
        {
            return true;
        }

        var (start, read, hidden) = (this.retentionHeld.Count, this.ContractReading(type), Hides(signature));
        var complete = read is not null && this.Holdings(read, written, holder, this.retentionHeld);
        var run = new HeldRange(start, this.retentionHeld.Count);
        var raw = start < run.End && (this.retentionHeld[start].Flags & HoldFlags.Raw) != 0 && !TryPairLayer(type, out _, out _);
        if (raw && complete)
        {
            var layer = this.retentionHeld[start];
            var past = new PlaceKey(layer.Part, layer.Written, layer.Holder, -1, this.PastThrough(type, layer, at), this.SignaturePast(signature, type, layer));
            into.Add(new(type, this.LayerStep(layer.Step, at), past) { Hidden = hidden, Let = let });
        }

        var declared = raw || !complete ? default : this.DeclaredRun(signature, type, run);
        var captures = type.Kind == BoundTypeKind.Closure ? BoundClosure.Of(type)?.Captures : null;
        for (var i = start; !raw && complete && i < run.End; i++)
        {
            var entry = this.retentionHeld[i];
            if (this.ByValue(type, entry) && entry.Part is { } part)
            {
                var fixedPart = let && (type.Kind != BoundTypeKind.Closure || (captures is { } bound && !bound[i - start].Environment.MutableCapture));
                complete = this.OwnParts(part, entry.Written, entry.Holder, this.DeclaredPart(signature, declared, run, i), fixedPart, at, into);
            }
            else if (entry.Step != HoldStep.Loan || entry.Part is null)
            {
                var (through, reached) = (this.PastThrough(type, entry, at), this.SignaturePast(signature, type, entry));
                into.Add(new(null, entry.Step, new(entry.Part, entry.Written, entry.Holder, -1, through, reached)) { Hidden = hidden, Let = let });
            }
        }

        this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        return complete;
    }

    // SPEC 8.2.3 (PLAN G823 revision 1): a declared counterpart as its declaring function's body sees it. Identity is closed under
    // replacement inside any Type, so an abstract Type of the function's own declaration takes the form its identity class in the
    // function's view fixes (Binding.TypeIdentity, the one judge): a constructed member (then paired as any declared Type: `S.Item is
    // raw/u8` shows the layer), else a member that is no abstract Type of the function (an enclosing Type's parameter: shown), else itself.
    // Any other Type is itself. A class's constructed members are one Type, so the first in index order is taken.
    private BoundType? View(BoundType? signature)
    {
        if (OwnAbstract(signature) is not { } function)
        {
            return signature;
        }

        this.viewMembers.Clear();
        this.IdentityMembers(signature!, this.ConstraintScope(function), this.viewMembers);
        var shown = signature;
        for (var i = 0; i < this.viewMembers.Count; i++)
        {
            var member = this.viewMembers[i];
            if (member.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection))
            {
                return member;
            }

            shown = ReferenceEquals(shown, signature) && OwnAbstract(member) is null ? member : shown;
        }

        return shown;
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
