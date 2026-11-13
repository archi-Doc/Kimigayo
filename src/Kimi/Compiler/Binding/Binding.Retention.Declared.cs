// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The walked signature's declared counterparts (DECISIONS BU-113; PLAN G823), one rule for its three carriers: StorageAt.Signature
// (the storage walk, Binding.Retention.Storage.cs), RetentionAtom.Signature (the atom walk, Binding.Retention.Atoms.cs) and the key
// walk's (OwnParts, Binding.Retention.PlaceKeys.cs).
// Responsibility: the declared counterpart one step below a position: a component where the constructors agree (DeclaredComponent,
// Same), what an entry reaches (SignaturePast), the declared Type's parts in a run's order (DeclaredRun, DeclaredPart), and inside the
// binding of an abstract Type of the walked function's own declaration that abstract Type itself, kept below (OwnAbstract, Hides,
// Below; SPEC 15.6.4).
// Inputs: the declared and the instantiated Types, Holdings' entries.
// Outputs: the declared counterpart, or null where the declaration writes none there.
// Invariants: a function of its inputs; a kept abstract Type gets every reader's null answer (no pointer, no constructor, no pair layer,
// no referent); allocates nothing.
// Must not: read a premise or an identity class (the key walk's View, Binding.Retention.PlaceKeys.cs), retention or the storage walk's
// state (retentionLimit).

public sealed partial class Binding
{
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

    // The declared counterpart of what a value's entry other than a part by value reaches (Below of Reach's arms; DECISIONS BU-113): a pair
    // layer's target (whatever the entry), a borrow's referent, a raw layer's pointee or a read-only own step's payload where the
    // constructors agree.
    private BoundType? SignaturePast(BoundType? declared, BoundType type, in Hold entry)
    {
        if (declared is null || Hides(declared))
        {
            return declared;
        }

        if (TryPairLayer(type, out _, out _))
        {
            return TryPairLayer(declared, out _, out var target) ? target : null;
        }

        return entry.Step is HoldStep.Shared or HoldStep.Exclusive && (entry.Flags & HoldFlags.Raw) == 0 ? this.BorrowReferent(declared, out _, out _)
            : DeclaredComponent(declared, type, 0);
    }

    // The declared counterparts of a run's parts by value, the one pairing (Parts, OwnParts; DECISIONS BU-113): the declared Type's entries
    // (Holdings) on retentionHeld after the run, in the run's order, where the declared Type has the instantiated one's constructor (Same)
    // and as many entries; none for a Closure's environment or any other declared Type. It reads Holdings only and sets no storage-walk
    // state (retentionLimit): an Unsupported entry there has the instantiated Type's constructor, so that Type's own run has one too.
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
