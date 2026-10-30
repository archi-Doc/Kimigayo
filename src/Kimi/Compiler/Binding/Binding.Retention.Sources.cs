// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, the Value sources and the fits (SPEC 15.6.4 read as (iii) with (β′), 15.6.3; DECISIONS R4-M154; contract in
// TyCtxt.Retention.cs).
// Responsibility: the input fold of the one walk (StorageWalk; DECISIONS R4-M154, r2.3): the Value sources of each input
// (RetentionSources, Source, Through: a value of its Type, of a part it holds by value, or a borrow formed through it, each named by its
// layer's node with the layers it is formed through), and whether a position can hold one (CanHold, Holds, Borrows, Fits);
// Binding.Retention.Rows.cs reads them into a storage's Value kinds and pairs, and the hold walk reads Fits.
// Inputs: the contract's instantiated inputs, the walk's visits, the nodes of a Type's own layers and arguments (OwnNodes), the parts a
// value holds (HeldParts) and the pair layers' admitted cases (LayerAdmitted).
// Outputs: per computation, the sources by node (retentionSources, with their layer paths, retentionPaths); per question, whether a
// position's Type can hold a source.
// Invariants: a source is identified by its path (Revision 10): the layers it is formed through but those the premise catalog's
// well-formedness rule relates to the layer above (Related), each of which a fit needs, so a Type is walked once per path; a fit compares
// Origin-erased Types (TyCtxt.Erase), a pair layer by its admitted cases; an abstract part, a part Binding did not bind and a library Type
// keeping no part hold any; the input fold's walks and the fits stop at the depth bound (StorageBound).
// Must not: judge an Origin relation (the premise closure's), walk a value itself (the walk's), or read the call's arguments.
public sealed partial class Binding
{
    // The Value sources of the computation by node, their layer paths (each a run of retentionPaths) and the path being walked, and the
    // parts Holds has met; reused.
    private readonly List<RetentionSource> retentionSources = new();
    private readonly List<int> retentionPaths = new();
    private readonly List<int> retentionPath = new();
    private readonly Dictionary<(int Path, int Layer), int> retentionPathIds = new(); // The paths walked, each by its prefix and last layer.
    private readonly HashSet<BoundType> retentionParts = new(ReferenceEqualityComparer.Instance);

    // Whether an unconditional `Input` premise edge relates a layer's node to the layer above's (`above`, -1: none), or the two are one
    // node: then the layer adds nothing to a fit, being reached whenever the layer above is (DECISIONS R4-M154, Revision 10). Such edges
    // are the premise catalog's well-formedness rule (R2, AddTypePremises: an Origin stored below a borrow or Slice layer outlives that
    // layer's) and the relations retention writes under that rule (an erased callable's, AddErasedRelations; a carrier's layers). The
    // edges decide, never a layer's kind: a raw layer's own Origin is stored nowhere (BoundType.Origin), so it stays unless another stored
    // occurrence relates it.
    private static bool Related(OriginPremiseEnvironment environment, int node, int above)
        => above >= 0 && (node == above || environment.Closure.States(node, above, OriginPremiseRule.Input));

    // The Value sources of the contract's inputs (SPEC 15.6.4 read as (iii), 15.6.3; DECISIONS R4-M154), by node: a value of an input's
    // Type and of each part it holds by value (a borrowed input's part is its layer; a by-value input's, its parts by value), named by the
    // nodes of its own layer or arguments, and the borrows formed through it (its referent's parts are reads or borrows formed through
    // it): through a borrow layer among those parts (shared or exclusive), a borrow of its referent or of a part the referent holds by
    // value, and through each exclusive layer reached from it before another shared one, its referent's (SPEC 15.6.3: a Ref's region lies
    // within each layer from its last step outward up to the first shared one), each named by the layer's node. A raw input has none
    // here (its own value is the hold walk's, Held). Each input's are the input fold of the one walk from its own value (Source).
    private void RetentionSources(OriginPremiseEnvironment environment, ReadOnlySpan<BoundType> inputs)
    {
        this.retentionSources.Clear();
        this.retentionPaths.Clear();
        this.retentionPathIds.Clear();
        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
            {
                this.storageSeen.Clear();
                this.StorageWalk(environment, inputs[i], StorageAt.Input(i));
            }
        }
    }

    // The input fold at a Type the walk visits (StorageWalk): the sources of the input there, by node, each with its path (retentionPath,
    // Revision 10): the nodes of the layers it is formed through (a reborrow's region lies within each, SPEC 15.6.3; a borrow of a raw
    // place within the raw layer's Origin, 5.2.2) but those Related to the layer above (`at.Source.Above`). A Type held by value from the
    // input is a source of its own Type; a Type reached through a layer is one only as an exclusive layer, of the borrows formed through it
    // (its referent, Through), as a shared layer held by value is too. Returns the node past which a source goes through the Type's
    // layer (-1: none, also below a shared layer reached through another): each path forms its own sources (form (b)), the walk visiting
    // a Type once per path (`at.Source.Path`, an interned id), so one reached through two layers forms the borrows of both.
    private int Source(OriginPremiseEnvironment environment, BoundType type, StorageAt at)
    {
        var referent = Abstract(type) ? null : this.BorrowTarget(type);
        var exclusive = referent is not null && (IsInvariantLayer(type, this) || type.Symbol?.LibraryDeclaration == KimiDeclarationId.UniqSlice);
        if (!at.Source.Held && !exclusive)
        {
            return -1;
        }

        var layer = -1;
        foreach (var node in this.OwnNodes(environment, type))
        {
            var start = this.retentionPaths.Count;
            this.retentionPaths.AddRange(this.retentionPath);
            if (!Related(environment, node, at.Source.Above))
            {
                this.retentionPaths.Add(node);
            }

            var own = at.Source.Held ? type : null;
            this.retentionSources.Add(new(node, at.Source.Input, own, referent, (start, this.retentionPaths.Count - start)));
            layer = node;
        }

        return referent is null ? -1 : layer;
    }

    // The input fold past a layer (Past, Admitted): what a source goes through the layer at node `layer` to (-1: none), reached, with the
    // layer on its path unless Related to the layer above.
    private void Through(OriginPremiseEnvironment environment, BoundType referent, int layer, StorageAt at)
    {
        if (at.Source.Input < 0 || layer < 0)
        {
            return;
        }

        var kept = !Related(environment, layer, at.Source.Above);
        if (kept)
        {
            this.retentionPath.Add(layer);
        }

        var path = kept ? this.PathId(at.Source.Path, layer) : at.Source.Path;
        this.StorageWalk(environment, referent, at with { Source = at.Source with { Held = false, Above = layer, Path = path } });
        this.retentionPath.RemoveRange(this.retentionPath.Count - (kept ? 1 : 0), kept ? 1 : 0);
    }

    // The id of a path (Through): its prefix's id with one more layer, interned for the computation (0: the empty path).
    private int PathId(int prefix, int layer)
    {
        if (!this.retentionPathIds.TryGetValue((prefix, layer), out var id))
        {
            id = this.retentionPathIds.Count + 1;
            this.retentionPathIds.Add((prefix, layer), id);
        }

        return id;
    }

    // SPEC 15.6.4 read as (iii) (DECISIONS R4-M154): whether a position can hold a source's value: an abstract part, or a position of no
    // Type (Admitted), any value whose Origin reaches it; else a value of the input's own part, the Origin-erased Types one (a pair layer by
    // an admitted case; an abstract part, any Type an instance makes it, SPEC 15.3.5), or a borrow formed through the source's layer, the
    // position a borrow whose referent's Type is one of the parts the layer's referent holds by value.
    private bool CanHold(BoundType? position, in RetentionSource source)
    {
        this.retentionParts.Clear();
        if (position is null || Abstract(position) || (source.Own is { } own && (Abstract(own) || this.Holds(position, own, carried: true, 0))))
        {
            return true;
        }

        return source.Through is { } referent && this.Borrows(position, referent, 0);
    }

    // Whether a position, or a part it carries (Holds), is a borrow of a part a referent holds by value: one formed through a source's
    // layer.
    private bool Borrows(BoundType? position, BoundType referent, int depth)
    {
        if (position is null || depth > StorageBound)
        {
            return false;
        }

        if (this.BorrowTarget(position) is { } target)
        {
            this.retentionParts.Clear();
            return this.Holds(referent, target, carried: false, 0);
        }

        if (Abstract(position) || IsCallable(position))
        {
            return false;
        }

        _ = this.HeldParts(position, null, null, carried: true, out var run);
        var borrows = false;
        for (var i = run.Start; !borrows && i < run.End; i++)
        {
            borrows = this.retentionHeld[i].Addressed && this.Borrows(this.retentionHeld[i].Type, referent, depth + 1);
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        return borrows;
    }

    // A library Type whose storage the compiler implements: a nominal Type that keeps no part, no base and no Type argument (HeapBuffer).
    private bool Opaque(BoundType type) => type is { Symbol.LibraryDeclaration: not null, Components.Count: 0 } && this.KeepsNoPart(type);

    // A struct or enum whose instance keeps no part and no base (Instance): a compiler-managed collection, whose Type arguments are its
    // elements (SPEC 4.5, 4.9), or, with none, storage the compiler implements (Opaque).
    private bool KeepsNoPart(BoundType type)
        => (AdtDef.IsStruct(type) || AdtDef.IsEnum(type)) && this.Readable(type, this.PrepareTypeStorage(type)) &&
            this.Instance(type) is { Parts.Length: 0, PartsBase: null };

    // What a borrow-shaped Type refers to, through which a borrow of it is formed (SPEC 15.6.3): a borrow, Slice or UniqSlice
    // (BorrowReferent), a pair layer admitting a borrow, or a raw layer stating an Origin, which retains as the borrow that Origin belongs
    // to would (SPEC 5.7); null for any other.
    private BoundType? BorrowTarget(BoundType type)
    {
        if (TryPairLayer(type, out _, out var target))
        {
            return (this.LayerAdmitted(type) & SemanticsMask.Borrow) != 0 ? target : null;
        }

        var stated = type.Semantics == SemanticsKind.Raw && OwnedOrigin(type) is { Kind: not (OriginKind.Static or OriginKind.Unstated) };
        return stated ? type.Components[0] : this.BorrowReferent(type, out _, out _);
    }

    // Whether a storage holds a value of a Type as itself or a part (HeldParts; `carried`: also a shared handle's payload, which a new value
    // built there carries); an abstract part, a part Binding did not bind (Rejected storage keeps the rows that hold whatever it is, BU-36)
    // and a library Type keeping no part (compiler-implemented storage, Opaque) hold any (a referent an abstract part is may hold any part,
    // as an implementation of its Contract may).
    private bool Holds(BoundType? storage, BoundType value, bool carried, int depth)
    {
        if (storage is null)
        {
            return true;
        }

        if (depth > StorageBound || !this.retentionParts.Add(storage))
        {
            return false;
        }

        if (Abstract(storage) || this.Fits(storage, value) || this.Opaque(storage))
        {
            return true;
        }

        if (IsCallable(storage) || this.BorrowShaped(storage) is not null)
        {
            return false;
        }

        _ = this.HeldParts(storage, null, null, carried, out var run);
        var holds = false;
        for (var i = run.Start; !holds && i < run.End; i++)
        {
            holds = this.retentionHeld[i].Addressed && this.Holds(this.retentionHeld[i].Type, value, carried, depth + 1);
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        return holds;
    }

    // Whether an abstract part can hold a raw input's own value (SPEC 15.6.4's abstract-Type sentence; DECISIONS BU-66): only where a
    // callable the call may run may retain it there (rule 3), a fill of the input (retentionFills) at any part or at a carrier that,
    // Origins erased, is the part or occurs in it (an associated projection's base); no premise makes an abstract part storage that can
    // hold it (rule 2's one test, Storages: a carrier's closure reaches only the relations of those callables).
    private bool HoldsRaw(BoundType part, int input)
    {
        foreach (var (filled, at) in this.retentionFills)
        {
            if (filled == input && (at is null || this.Occurs(part, at, 0)))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a carrier, Origins erased, is a Type or occurs among its components, to the depth bound.
    private bool Occurs(BoundType? type, BoundType carrier, int depth)
    {
        if (type is null || depth > StorageBound)
        {
            return false;
        }

        var occurs = ReferenceEquals(this.types.Erase(type), this.types.Erase(carrier));
        for (var i = 0; !occurs && i < type.Components.Count; i++)
        {
            occurs = this.Occurs(type.Components[i], carrier, depth + 1);
        }

        return occurs;
    }

    // Whether a position's Type and a value's are one, Origins erased (TyCtxt.Erase), a pair layer by one of its admitted cases.
    private bool Fits(BoundType position, BoundType value)
        => ReferenceEquals(this.types.Erase(position), this.types.Erase(value)) || this.CaseFits(position, value) || this.CaseFits(value, position);

    // Whether one admitted case of a pair layer (SPEC 8.1.1's table, Form) is another Type, Origins erased.
    private bool CaseFits(BoundType pair, BoundType other)
    {
        if (!TryPairLayer(pair, out _, out var target))
        {
            return false;
        }

        var (admitted, erased) = (this.LayerAdmitted(pair), this.types.Erase(other));
        foreach (var semantics in PairCases)
        {
            if ((admitted & semantics.ToMask()) != 0 && ReferenceEquals(this.types.Erase(this.Form(semantics, target, null)), erased))
            {
                return true;
            }
        }

        return false;
    }

    // A Value source of an input (RetentionSources): its node, the part of its own value it is (null: one reached through an exclusive
    // layer), the referent of the borrow layer it is, through which it forms borrows (null: none), and its path, the nodes every one of
    // which a position's closure must reach (retentionPaths: the layers it is formed through but those Related to the layer above, its own
    // last unless Related).
    private readonly record struct RetentionSource(int Node, int Input, BoundType? Own, BoundType? Through, (int Start, int Count) Path);
}
