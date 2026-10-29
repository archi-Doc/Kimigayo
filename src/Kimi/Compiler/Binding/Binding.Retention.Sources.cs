// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, the Value sources and the fits (SPEC 15.6.4 read as (iii) with (β′), 15.6.3; DECISIONS R4-M154; contract in
// TyCtxt.Retention.cs).
// Responsibility: the Value sources of each input (RetentionSources: a value of its Type, of a part it holds by value, or a borrow formed
// through it, each named by its layer's node with the layers it is formed through) and whether a position can hold one (CanHold, Holds,
// Borrows, Fits); Binding.Retention.Rows.cs reads them into a storage's Value kinds and pairs, and the hold walk reads Fits.
// Inputs: the contract's instantiated inputs, the nodes of its own layers and arguments (OwnNodes), the parts a value holds
// (HeldParts) and the pair layers' admitted cases (LayerAdmitted).
// Outputs: per computation, the sources by node (retentionSources, with their layer paths, retentionPaths); per question, whether a
// position's Type can hold a source.
// Invariants: a source is identified by its path (Revision 10): the layers it is formed through but those the premise catalog's
// well-formedness rule relates to the layer above (Related), each of which a fit needs, so a Type is walked once per path; a fit compares
// Origin-erased Types (TyCtxt.Erase), a pair layer by its admitted cases; an abstract part, a part Binding did not bind and a library Type
// keeping no part hold any; the walks stop at the depth bound (StorageBound).
// Must not: judge an Origin relation (the premise closure's), walk a storage (the storage walk's), or read the call's arguments.
public sealed partial class Binding
{
    // The Value sources of the computation by node, their layer paths (each a run of retentionPaths) and the path being walked, Sources'
    // visits, and the parts Holds has met; reused.
    private readonly List<RetentionSource> retentionSources = new();
    private readonly List<int> retentionPaths = new();
    private readonly List<int> retentionPath = new();
    private readonly HashSet<(int Path, bool Own, BoundType Type)> retentionVisits = new(); // Sources' visits, by path.
    private readonly Dictionary<(int Path, int Layer), int> retentionPathIds = new(); // The paths walked, each by its prefix and last layer.
    private readonly HashSet<BoundType> retentionParts = new(ReferenceEqualityComparer.Instance);

    // Whether the premise catalog's well-formedness rule (R2, AddTypePremises: an Origin stored below a borrow or Slice layer outlives
    // that layer's) relates a layer's node to the layer above's (`above`, -1: none), in every case, or the two are one node: then the
    // layer adds nothing to a fit (DECISIONS R4-M154, Revision 10). The rule's edges decide, never a layer's kind: a raw layer's own
    // Origin is stored nowhere (BoundType.Origin), so it stays unless another stored occurrence relates it.
    private static bool Related(OriginPremiseEnvironment environment, int node, int above)
        => above >= 0 && (node == above || environment.Closure.States(node, above, OriginPremiseRule.Input));

    // The Value sources of the contract's inputs (SPEC 15.6.4 read as (iii), 15.6.3; DECISIONS R4-M154), by node: a value of an input's
    // Type and of each part it holds by value (a borrowed input's part is its layer; a by-value input's, its parts by value), named by the
    // nodes of its own layer or arguments, and the borrows formed through it (its referent's parts are reads or borrows formed through
    // it): through a borrow layer among those parts (shared or exclusive), a borrow of its referent or of a part the referent holds by
    // value, and through each exclusive layer reached from it before another shared one, its referent's (SPEC 15.6.3: a Ref's region lies
    // within each layer from its last step outward up to the first shared one), each named by the layer's node. A raw input has none
    // here (its own value is the hold walk's, Held).
    private void RetentionSources(OriginPremiseEnvironment environment, ReadOnlySpan<BoundType> inputs)
    {
        this.retentionSources.Clear();
        this.retentionPaths.Clear();
        this.retentionPathIds.Clear();
        for (var i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] is not { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw })
            {
                this.retentionVisits.Clear();
                this.Sources(environment, inputs[i], i, own: true, (-1, 0), 0);
            }
        }
    }

    // An input's sources at a part of its own value (`own`) or at a part a referent of it holds by value, past its own layer and exclusive
    // ones (else), each with its path (retentionPath, Revision 10): the nodes of the layers it is formed through (a reborrow's region lies
    // within each, SPEC 15.6.3; a borrow of a raw place within the raw layer's Origin, 5.2.2) but those Related to the layer above
    // (`at.Above`, the node of the layer whose referent the walk is in, -1: none). Each path forms its own sources (form (b)): a Type is
    // walked once per path (`at.Path`, an interned id), so one reached through two layers forms the borrows of both.
    private void Sources(OriginPremiseEnvironment environment, BoundType? type, int input, bool own, (int Above, int Path) at, int depth)
    {
        if (type is null || depth > StorageBound || !this.retentionVisits.Add((at.Path, own, type)))
        {
            return;
        }

        var referent = Abstract(type) ? null : this.BorrowTarget(type);
        var exclusive = referent is not null && (IsInvariantLayer(type, this) || type.Symbol?.LibraryDeclaration == KimiDeclarationId.UniqSlice);
        if (own || exclusive)
        {
            var layer = -1;
            foreach (var node in this.OwnNodes(environment, type))
            {
                var start = this.retentionPaths.Count;
                this.retentionPaths.AddRange(this.retentionPath);
                if (!Related(environment, node, at.Above))
                {
                    this.retentionPaths.Add(node);
                }

                this.retentionSources.Add(new(node, input, own ? type : null, referent, (start, this.retentionPaths.Count - start)));
                layer = node;
            }

            if (referent is not null && layer >= 0)
            {
                var kept = !Related(environment, layer, at.Above);
                if (kept)
                {
                    this.retentionPath.Add(layer);
                }

                this.Sources(environment, referent, input, own: false, (layer, kept ? this.PathId(at.Path, layer) : at.Path), depth + 1);
                this.retentionPath.RemoveRange(this.retentionPath.Count - (kept ? 1 : 0), kept ? 1 : 0);
                return;
            }
        }

        if (referent is not null || Abstract(type) || IsCallable(type) || type.Semantics == SemanticsKind.Raw)
        {
            return;
        }

        var (from, to) = this.OwnParts(type, carried: false);
        for (var i = from; i < to; i++)
        {
            this.Sources(environment, this.retentionHeld[i].Type, input, own, at, depth + 1);
        }

        this.retentionHeld.RemoveRange(from, to - from);
    }

    // The id of a path (Sources): its prefix's id with one more layer, interned for the computation (0: the empty path).
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

        var (start, end) = this.OwnParts(position, carried: true);
        var borrows = false;
        for (var i = start; !borrows && i < end; i++)
        {
            borrows = this.Borrows(this.retentionHeld[i].Type, referent, depth + 1);
        }

        this.retentionHeld.RemoveRange(start, end - start);
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

    // Whether a storage holds a value of a Type as itself or a part (OwnParts; `carried`: also a shared handle's payload, which a new value
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

        var (start, end) = this.OwnParts(storage, carried);
        var holds = false;
        for (var i = start; !holds && i < end; i++)
        {
            holds = this.Holds(this.retentionHeld[i].Type, value, carried, depth + 1);
        }

        this.retentionHeld.RemoveRange(start, end - start);
        return holds;
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

    // The parts a value of a Type holds by value (SPEC 15.6.3), appended to retentionHeld as a run (Start, End) its caller removes: a raw
    // layer's pointee where it states no Origin (the value owns it), a pair layer's target in an owning case and, `carried`, a shared
    // handle's payload (which a new value built there carries), else the parts HeldParts gives an address, with what each `during self`
    // layer among them admits (SelfAdmitted: its pointee and the holding instance's Type arguments by their actual Types).
    private (int Start, int End) OwnParts(BoundType type, bool carried)
    {
        var parts = this.retentionHeld;
        var start = parts.Count;
        if (this.OwnLayer(type, carried, out var owned))
        {
            parts.AddRange(owned is null ? [] : [new(owned, null, null, true)]);
            return (start, parts.Count);
        }

        _ = this.HeldParts(type, null, null, out var run);
        var kept = run.Start;
        for (var i = run.Start; i < run.End; i++)
        {
            var part = parts[i];
            parts[kept] = part;
            kept += part.Addressed ? 1 : 0;
            if (part is { Type: { Semantics: SemanticsKind.Raw, Components: [var held] } raw, Holder: { } holder } &&
                this.DeclaredStep(raw, part.Written, holder) == RawStep.Own)
            {
                _ = SelfAdmitted(holder, out var arguments);
                parts.Add(new(held, null, null, true));
                foreach (var argument in arguments)
                {
                    parts.Add(new(argument, null, null, true));
                }
            }
        }

        parts.RemoveRange(kept, run.End - kept);
        return (start, parts.Count);
    }

    // OwnParts' layers: whether the Type is a raw, pair or shared handle layer, and the part it holds by value (`owned`, null for none): a
    // raw layer's pointee where it states no Origin, a pair layer's target in an owning case, `carried` a shared handle's payload too.
    private bool OwnLayer(BoundType type, bool carried, out BoundType? owned)
    {
        owned = null;
        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] })
        {
            owned = OwnedOrigin(type) is null or { Kind: OriginKind.Unstated } ? pointee : null;
            return true;
        }

        if (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Rc or SemanticsKind.Arc, Components: [var payload] })
        {
            owned = carried ? payload : null;
            return true;
        }

        if (!TryPairLayer(type, out _, out var target))
        {
            return false;
        }

        var shared = carried ? SemanticsMask.Rc | SemanticsMask.Arc : SemanticsMask.None;
        owned = (this.LayerAdmitted(type) & (SemanticsMask.Owner | SemanticsMask.Obj | shared)) != 0 ? target : null;
        return true;
    }

    // A Value source of an input (RetentionSources): its node, the part of its own value it is (null: one reached through an exclusive
    // layer), the referent of the borrow layer it is, through which it forms borrows (null: none), and its path, the nodes every one of
    // which a position's closure must reach (retentionPaths: the layers it is formed through but those Related to the layer above, its own
    // last unless Related).
    private readonly record struct RetentionSource(int Node, int Input, BoundType? Own, BoundType? Through, (int Start, int Count) Path);
}
