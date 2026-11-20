// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The walked signature's declared counterparts (DECISIONS BU-113; PLAN G823), one rule for its three carriers: StorageAt.Signature
// (the storage walk, Binding.Retention.Storage.cs), RetentionAtom.Signature (the atom walk, Binding.Retention.Atoms.cs) and the key
// walk's (OwnParts, Binding.Retention.PlaceKeys.cs).
// Responsibility: the declared counterpart one step below a position: a component where the constructors agree (DeclaredComponent,
// Same), what an entry reaches (SignaturePast), the declared Type's parts in a run's order (DeclaredRun, DeclaredPart), and inside the
// binding of an abstract Type of the walked function's own declaration that abstract Type itself, kept below (OwnAbstract, Hides,
// Below; SPEC 15.6.4) as far as the Place is storage of that Type: its parts by value. Past a safe reference the binding holds the
// counterpart ends: "a safe reference the binding holds is no such layer, and the routes above apply to it" (15.6.4), so its referent
// is a holder its Loans name, reached by route 1 and read as the input writes it (PLAN G935 B2). So it does past a raw layer the
// binding holds, whether or not the layer states an Origin: the holder the layer addresses is one the value carries the address of,
// reached "hidden or not" (SPEC 15.6.4's routes, 5.7 Calls; the owner's O-312 (a); PLAN G935 c3-5), except for an operation whose
// writes SPEC states (StatesWrites), which writes no such holder.
// The declared counterpart is read in the declaring function's view (View; SPEC 8.2.3; PLAN G935 c3-2): an abstract Type of the
// function's own declaration that its identity class gives a constructed form, or identifies with a Type the function does not own,
// is that form, shown, and is no kept Type. Each carrier's Signature is formed in the view: the retention walks take the declared
// inputs, the result and the handed signatures viewed at every depth (Viewed; DeclareSignature, RetentionPremises), so every
// counterpart paired below them is in the view; the key walk views each step (OwnParts, Select, Base). Two own abstract Types an
// identity premise identifies (`T is V`) both stay kept: View chooses no member between them, and the fit of one to the other is
// Identical's (Binding.Retention.Rows.cs: Binding.TypeIdentity's judge), which HiddenKind, the write test and Occurs read.
// Inputs: the declared and the instantiated Types, Holdings' entries, the declaring function's identity classes (Binding.TypeIdentity).
// Outputs: the declared counterpart, or null where the declaration writes none there.
// Invariants: a function of its inputs and the declaring function's premises; a kept abstract Type gets every reader's null answer (no
// pointer, no constructor, no pair layer, no referent); the pairing allocates nothing, Viewed interns a Type where a premise replaces a part.
// Must not: choose a premise (the classes' one constructed member is taken), read retention or the storage walk's state (retentionLimit).

public sealed partial class Binding
{
    private readonly List<BoundType> viewMembers = new(); // View's scratch: an identity class's members.

    // The declared counterpart of a Type's component `index` (StorageAt.Signature, RetentionAtom.Signature): that component of the declared
    // Type where it has the same constructor; null below any other declared Type (inside a hidden binding, Below keeps the abstract Type).
    private static BoundType? DeclaredComponent(BoundType? declared, BoundType type, int index)
        => Same(declared, type) && (uint)index < (uint)declared!.Components.Count ? declared.Components[index] : null;

    // Whether a declared Type has the constructor of an instantiated one: its kind, Semantics, symbol and arity.
    private static bool Same(BoundType? declared, BoundType type)
        => declared is not null && declared.Kind == type.Kind && declared.Semantics == type.Semantics && ReferenceEquals(declared.Symbol, type.Symbol) &&
            declared.Components.Count == type.Components.Count;

    // SPEC 15.6.4, 5.7 Calls (PLAN G823): the function whose own declaration a declared counterpart is an abstract Type of (its own Type
    // parameter, or a pair target or projection rooted at one; no pair layer), null for any other Type. An enclosing Type's parameter has
    // its Type's declaration as binder: the receiver's instance completes it (DECISIONS BU-50).
    private static FunctionKoto? OwnAbstract(BoundType? declared)
    {
        var root = declared;
        while (root is { Kind: BoundTypeKind.AssociatedProjection, Components: [var subject, ..] })
        {
            root = subject;
        }

        return root is { Kind: BoundTypeKind.Parameter or BoundTypeKind.TargetProjection, Symbol.Scope.Owner: FunctionKoto function } &&
            !TryPairLayer(declared!, out _, out _) ? function : null;
    }

    // Whether a declared counterpart is an abstract Type of its function's own declaration: the binding there is one the declaration does
    // not show (SPEC 15.6.4).
    private static bool Hides(BoundType? declared) => OwnAbstract(declared) is not null;

    // The one convention of the walked signature's carriers (StorageAt.Signature, RetentionAtom.Signature, PlaceKey.Signature; DECISIONS
    // BU-113): the declared counterpart one step below a position whose counterpart is `declared`; inside a hidden binding the abstract Type
    // itself, kept below (Hides), so no reader takes it for a declared Type there.
    private static BoundType? Below(BoundType? declared, BoundType? part) => Hides(declared) ? declared : part;

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

    // SPEC 8.2.3 ("identity is closed under replacement inside any Type"; PLAN G935 c3-2): a declared Type as its declaring function's
    // body sees it at every depth: View at the Type and at each component of what it gives, again inside each replacement, never
    // inside a projection, whose class View reads whole. A valid premise set gives every Type a finite expansion (SPEC 8.3); the depth
    // bound stops an erroneous one. A Type that names no Type parameter is itself. Each (part, depth) once per call (viewedParts, DECISIONS
    // BU-64's per-query rule: the view's identity classes are fixed while it runs), so a premised DAG costs its distinct parts, at most
    // StorageBound + 1 entries each, not its paths.
    private BoundType? Viewed(BoundType? declared) => this.Viewed(declared, 0, this.viewedParts.Begin(this.storageVersion));

    private BoundType? Viewed(BoundType? declared, int depth, uint query)
    {
        if (declared is null || !declared.ContainsParameter || depth > StorageBound)
        {
            return declared;
        }

        if (this.viewedParts.TryGet((declared, depth), query, out var known))
        {
            return known;
        }

        var shown = this.View(declared)!;
        var count = shown.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection ? 0 : shown.Components.Count;
        var viewed = shown;
        if (count > 0)
        {
            var components = this.RentTypes(count);
            try
            {
                var changed = false;
                for (var i = 0; i < count; i++)
                {
                    components[i] = this.Viewed(shown.Components[i], depth + 1, query)!;
                    changed |= !ReferenceEquals(components[i], shown.Components[i]);
                }

                viewed = changed ? this.types.Rebuild(shown, components.AsSpan(0, count), shown.Origin, (BoundOrigin[])shown.OriginArguments) : shown;
            }
            finally
            {
                this.typeScratch.Return(components, clearArray: true);
            }
        }

        this.viewedParts.Set((declared, depth), query, viewed);
        return viewed;
    }

    // The declared counterpart of what a value's entry other than a part by value reaches (Below of Reach's arms; DECISIONS BU-113): a pair
    // layer's target (whatever the entry), a borrow's referent, a raw layer's pointee or a read-only own step's payload where the
    // constructors agree. Inside a kept binding the kept Type itself, except past a safe reference or a raw layer the binding holds,
    // where it ends (null): the referent or pointee is a holder the value names by its Loans or addresses, no storage of the kept Type
    // (the file's contract; an operation whose writes SPEC states keeps the Type past a raw layer, since it writes no such holder).
    private BoundType? SignaturePast(BoundType? declared, BoundType type, in Hold entry)
    {
        if (declared is null)
        {
            return null;
        }

        var reference = entry.Step is HoldStep.Shared or HoldStep.Exclusive && (entry.Flags & HoldFlags.Raw) == 0;
        if (Hides(declared))
        {
            return reference || this.HiddenLayer(declared, entry) ? null : declared;
        }

        if (TryPairLayer(type, out _, out _))
        {
            return TryPairLayer(declared, out _, out var target) ? target : null;
        }

        return reference ? this.BorrowReferent(declared, out _, out _) : DeclaredComponent(declared, type, 0);
    }

    // SPEC 15.6.4, 5.7 Calls' last sentence (PLAN G964): whether an entry is a raw layer the binding of an abstract Type of the walked
    // function's own declaration holds, where the declared counterpart ends (SignaturePast): the holder it addresses is reached by route
    // 1 and read by its own Type, and past that holder a generic body writes only by the routes (StorageAt.Routed). An operation whose
    // writes SPEC states keeps the Type there (StatesWrites).
    private bool HiddenLayer(BoundType? declared, in Hold entry)
        => (entry.Flags & HoldFlags.Raw) != 0 && OwnAbstract(declared) is { } function && !this.StatesWrites(function);

    // SPEC 5.7 Calls' last sentence, 15.6.4 ("its calls and destruction store within that reach"; PLAN G964, F's ruling 4): whether the
    // walk is Routed past the holder a hidden layer addresses: only a generic body acts there, by the routes. The declaring code's own
    // actions there act by the declaration's statement: a `drop` of a struct of the kept binding holding the layer by value (Dropped)
    // and a callable the callee's declaration hands its body that writes the kept Type (HandWrites, OutputRetention.Handed's test).
    // Past the holder no declaration's statement applies otherwise, the holder's own Type's included (F's ruling 7, F1).
    private bool Routes(in StorageAt at, in Hold entry)
        => this.HiddenLayer(at.Signature, entry) && !at.Dropped && !this.HandWrites(at.Signature!);

    // The declared counterparts of a run's parts by value, the one pairing (Parts, OwnParts; DECISIONS BU-113): the declared Type's entries
    // (Holdings) on retentionHeld after the run, in the run's order, where the declared Type has the instantiated one's constructor (Same)
    // and as many entries; none for a Closure's environment or any other declared Type. It reads Holdings only and sets no storage-walk
    // state (retentionLimit).
    private HeldRange DeclaredRun(BoundType? declared, BoundType type, HeldRange run)
    {
        var start = this.retentionHeld.Count;
        if (type.Kind != BoundTypeKind.Closure && Same(declared, type) &&
            (!this.Holdings(declared!, null, null, this.retentionHeld) || this.retentionHeld.Count - start != run.End - run.Start))
        {
            this.retentionHeld.RemoveRange(start, this.retentionHeld.Count - start);
        }

        return new(start, this.retentionHeld.Count);
    }

    // A part's declared counterpart (DeclaredRun's entry for run entry `i`), kept inside a hidden binding (Below).
    private BoundType? DeclaredPart(BoundType? declared, HeldRange paired, HeldRange run, int i)
        => Below(declared, paired.Start < paired.End ? this.retentionHeld[paired.Start + i - run.Start].Part : null);
}
