// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Call retention, the Value sources and the fits (SPEC 15.6.4 read as (iii) with (β′), 15.6.3; DECISIONS R4-M154; contract in
// TyCtxt.Retention.cs).
// Responsibility: the input fold of the one walk (StorageWalk; DECISIONS R4-M154, r2.3): the Value sources of each input
// (RetentionSources, Source, Through: a value of its Type, of a part it holds by value, or a borrow formed through it, each named by its
// layer's node with the layers it is formed through), whether a position can hold one (CanHold, Holds, Borrows, Fits), and whether an
// abstract part may keep an input by a callable's fill (Binding.Retention.Rows.cs, Filled; DECISIONS BU-66, PLAN G935 R1.3);
// Binding.Retention.Rows.cs reads them into a storage's Value kinds and pairs.
// Inputs: the contract's instantiated inputs, the walk's visits, the nodes of a Type's own layers and arguments (OwnNodes), the parts a
// value holds (Holdings) and the pair layers' admitted cases (LayerAdmitted).
// Outputs: per computation, the sources by node (retentionSources, with their layer paths, retentionPaths); per question, whether a
// position's Type can hold a source.
// Invariants: a source is identified by its path (Revision 10): the layers it is formed through but those the premise catalog's
// well-formedness rule relates to the layer above (Related), each of which a fit needs, so a Type is walked once per path; a fit compares
// Origin-erased Types (TyCtxt.Erase), a pair layer by its admitted cases; an abstract part, a part Binding did not bind and raw storage a
// value holds (DECISIONS BU-91) hold any; the input fold's walk stops at the depth bound (StorageBound), entering a state at the
// shallowest depth it is met at (Enters), and a state past it that asks leaves the input's sources undecided, the input its subject
// (retentionPast, G408B-17); the fits, asked at a position the output walk names, and Occurs have no bound: each enters a
// state once and ends with the Type (IMPL A.4's height).
// Must not: judge an Origin relation (the premise closure's), walk a value itself (the walk's), or read the call's arguments.
public sealed partial class Binding
{
    // The Value sources of the computation by node, their layer paths (each a run of retentionPaths) and the path being walked, and the
    // parts Holds has met, reused. retentionParts serves two queries, Holds' and Instantiation's, each part once (value 0), never in one
    // query.
    private readonly List<RetentionSource> retentionSources = new();
    private readonly List<int> retentionPaths = new();
    private readonly List<int> retentionPath = new();
    private readonly Dictionary<(int Path, int Layer), int> retentionPathIds = new(); // The paths walked, each by its prefix and last layer.
    private readonly Dictionary<BoundType, int> retentionParts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BoundType Type, Hold Asked), bool> retentionHoldAnswers = new(); // Holds' answers within one output's fits (HoldsFrom), reused.
    private readonly Dictionary<(BoundType Type, BoundType? Written, BoundType? Holder, BoundType Referent), bool> retentionBorrowAnswers = new(); // Borrows', alike.
    private readonly Dictionary<(BoundType Type, BoundType? Written, BoundType? Holder), int> retentionWalked = new(); // Borrows' and Occurs' states, once each, reused.
    private readonly Dictionary<(BoundType Type, HoldStep Reach), int> retentionReaches = new(); // WrittenThrough's (Type, reach) depths, reused.

    // The one visit rule of the walks that stop at a depth bound and key their visits (G416, PLAN G571, G408). The bound counts the parts a
    // walk descends over a contract Type, and Binding and TyCtxt have four such walks, each counting its own steps against its bound: three
    // at StorageBound, which counts Holdings entries (the input fold's StorageWalk, HiddenKind, WrittenThrough), and TyCtxt.Handed at
    // HandedBound, which counts Type parts. A walk over a storage's Type takes the output walk's bound, which is none (G408B-9 R1, G408B-16):
    // the output's StorageWalk counts depth 0, and ResultLeaves, the fits asked at the positions it names (Holds, Borrows) and MayCarry
    // enter each state once. Viewed, a Type map memoized by depth, is no visit walk. A walk enters a state at the shallowest depth within the bound it is
    // met at, and again where it is met shallower. So what it reaches below a state is a function of the state, not of the route that met
    // it first: within the bound it is the routes' answer, in at most bound + 1 entries per state (G416: a branching recursion has
    // 2^(depth/2) routes). A meeting past the bound enters nothing and marks a state not entered within it (its depth, past the bound):
    // where the walk asks there (its own test at the meeting, or a step below it), the walk is undecided, so unless its answer is a decided
    // yes its top question adds the missing finding's subject to retentionPast where Past(table) holds (the argument for the input fold and
    // HiddenKind, `position` for WrittenThrough, and the unentered parts themselves, which TyCtxt.HandedCallables publishes, for
    // Handed; G408B-17): the contract's Deep beside its rows (CallRetention.IsDeep) where a holder they name and a subject can carry a
    // Loan (HolderCarries, AnyCarries, G408B-16; G408, R4-M153's Join; A5 records it as OwnershipDepthLimit_Kd at the call, R4-M51),
    // elsewhere no verdict reads the undecided answer (G408B-6 (3)).
    // `shallowest` is the state's entry in its walk's own table (`met`: it had one; on return, it was entered within the bound), a
    // reference the caller reads no more once its walk may insert (a resize moves the entry). Each walk keys its own state, all that
    // decides what lies below it: the handed-callable walk's Type (TyCtxt.Handed, handedSeen), and the storage walk's StorageAt, which
    // leaves out the layer above, the Written Type and the holder (StorageWalk): meetings that differ only in those count as one state (the
    // layer above changes no fit, Revision 10; the other two steer raw steps, and a shallower meeting walks below with its own: PLAN G571's
    // design, N1). The fits key theirs alike and enter each once: the borrow fit's (Type, Written, Holder) (Borrows, retentionWalked) and
    // the hold fit's storage Type, its asked entry fixed per query and its run read with neither (Holds, retentionParts). Each keeps its
    // answers for the positions of one output, Holds' per (Type, asked entry), Borrows' per state and referent (HoldsFrom, BorrowsFrom;
    // G408B-10, G408B-12). Holds runs inside Borrows' walk, so their tables stay apart.
    internal static bool Enters(ref int shallowest, ref bool met, int depth, int bound)
    {
        met &= shallowest <= bound;
        if (depth > bound)
        {
            shallowest = met ? shallowest : depth;
            return false;
        }

        if (met && shallowest <= depth)
        {
            return false;
        }

        shallowest = depth;
        return true;
    }

    // Whether a walk's table holds a state met past the bound and never entered within it (Enters): the walk is undecided there.
    internal static bool Past<TKey>(Dictionary<TKey, int> table, int bound)
        where TKey : notnull
    {
        foreach (var depth in table.Values)
        {
            if (depth > bound)
            {
                return true;
            }
        }

        return false;
    }

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
            if (inputs[i] is not { IsPointer: true })
            {
                this.storageSeen.Clear();
                var site = this.OpenRetentionSite(this.retentionCallee, i); // PLAN G651 U2b R11 OP1: the input's storage walk.
                this.StorageWalk(environment, inputs[i], StorageAt.Input(i) with { Signature = this.SignatureInput(i) });
                this.Restore(site);
                foreach (var (depth, _) in this.storageSeen.Values)
                {
                    if (depth > StorageBound)
                    {
                        this.retentionPast.Add(inputs[i]); // A state past the bound that asks (Asks): the input's sources undecided (Enters).
                        break; // Its subject is the argument, never the part past the bound: retention is per argument (SPEC 15.6.4).
                    }
                }
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
    private int Source(OriginPremiseEnvironment environment, BoundType type, StorageAt at, HeldRange run)
    {
        var referent = this.Referent(run, out _)?.Part;
        if (!this.Asks(at, run))
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
            this.retentionSources.Add(new(node, at.Source.Input, own, referent, at.Signature, (start, this.retentionPaths.Count - start)));
            layer = node;
        }

        // R (PLAN G935; the owner's O-359 (a), O-364 (a) (2)): a part held by value that names no Origin and holds no part by value is a source
        // of its own value with no node, matched by CanHold alone at every position of a storage R's Origin test lets keep its input (MayRetain:
        // where the input's Type states an Origin, a storage that as a whole may retain one of them; O-435 (a), F's G935 ruling 81); a raw
        // layer is no such source (the hold walk's).
        if (layer < 0 && at.Source.Held && referent is null && !type.IsPointer &&
            this.Leaf(type, run))
        {
            this.retentionSources.Add(new(-1, at.Source.Input, type, null, at.Signature, (this.retentionPaths.Count, 0)));
        }

        return referent is null ? -1 : layer;
    }

    // Whether the input fold asks at a state (Source; its past test, StorageWalk): at a part held by value, a source of its own Type or,
    // through the parts it holds by value, of theirs (an Origin-free leaf among them, R); and at an exclusive layer, through which borrows
    // are formed (past a shared layer, only there, SPEC 15.6.3; G408B-4 C-M2).
    private bool Asks(StorageAt at, HeldRange run)
    {
        this.Referent(run, out var exclusive);
        return at.Source.Held || exclusive;
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
        if (position is null || Abstract(position) ||
            (source.Own is { } own && (Abstract(own) || this.HoldsFrom(position, new(own, HoldStep.Inline, HoldContent.Its, HoldFlags.None, null, null)))))
        {
            return true;
        }

        return source.Through is { } referent && this.BorrowsFrom(position, referent);
    }

    // Whether a position, or a part it carries (Holds), is a borrow of a part a referent holds by value: one formed through a source's
    // layer.
    private bool Borrows(BoundType? position, BoundType? written, BoundType? holder, BoundType referent)
    {
        if (position is null)
        {
            return false;
        }

        if (this.retentionBorrowAnswers.TryGetValue((position, written, holder, referent), out var known))
        {
            return known;
        }

        if (!this.retentionWalked.TryAdd((position, written, holder), 0))
        {
            return false;
        }

        this.termWalks++;
        var run = this.BodyRun(position, written, holder, out _);
        var borrows = false;
        if (this.Referent(run, out _) is { } target)
        {
            borrows = this.HoldsFrom(referent, target); // The layer's entry asks: a Place, or a view's run (Repeated).
        }
        else
        {
            for (var i = run.Start; !borrows && i < run.End; i++)
            {
                var part = this.retentionHeld[i];
                borrows = (this.ByValue(position, part) || RawStorage(part) || (part.Flags & HoldFlags.ReadOnly) != 0) &&
                    this.Borrows(part.Part, part.Written, part.Holder, referent);
            }
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        if (borrows)
        {
            this.retentionBorrowAnswers[(position, written, holder, referent)] = true; // A route to a borrow: a yes at every state on it.
        }

        return borrows;
    }

    // One Borrows query (CanHold's borrow fit) from a fresh visit set, kept as HoldsFrom keeps Holds': a no is every state's it met. The
    // answer of a state reads the state and the referent alone (its run, ByValue, its parts, the layer entry's HoldsFrom), never the
    // position that asked (G408B-12).
    private bool BorrowsFrom(BoundType position, BoundType referent)
    {
        this.retentionWalked.Clear();
        if (this.Borrows(position, null, null, referent))
        {
            return true;
        }

        foreach (var (met, written, holder) in this.retentionWalked.Keys)
        {
            this.retentionBorrowAnswers[(met, written, holder, referent)] = false;
        }

        return false;
    }

    // Whether a storage holds what an entry asks, as itself or a part (its entries): an Inline entry a value of its Part (a value stored
    // there, also as a shared handle's payload, which a new value built there carries), a dereference (Referent's) a Place of its Part or,
    // with Repeated, a view's run of it (SPEC 4.8.1: also an empty one); an Empty part answers at its own level (PartHolds). An abstract
    // part, a part Binding did not bind (Rejected storage keeps the rows that hold whatever it is, BU-36) and raw storage a value holds
    // (RawStorage; SPEC 5.7, 5.2, DECISIONS BU-91) hold any (a referent an abstract part is may hold any part, as an implementation of its
    // Contract may), so a borrow formed within raw storage, punned or not, is one formed through its holder.
    private bool Holds(BoundType? storage, in Hold asked)
    {
        if (storage is null)
        {
            return true;
        }

        if (this.retentionHoldAnswers.TryGetValue((storage, asked), out var known))
        {
            return known;
        }

        if (!this.retentionParts.TryAdd(storage, 0))
        {
            return false;
        }

        this.termWalks++;
        var holds = this.Fits(storage, asked.Part!);
        var stored = asked.Step == HoldStep.Inline;
        var run = holds ? default : this.BodyRun(storage, null, null, out _);
        for (var i = run.Start; !holds && i < run.End; i++)
        {
            var part = this.retentionHeld[i];
            holds = part.Step == HoldStep.Abstract || RawStorage(part) ||
                ((this.ByValue(storage, part) || (stored && (part.Flags & HoldFlags.ReadOnly) != 0)) && this.PartHolds(part, asked));
        }

        this.retentionHeld.RemoveRange(run.Start, run.End - run.Start);
        if (holds)
        {
            this.retentionHoldAnswers[(storage, asked)] = true; // A route to a fit: a yes at every route.
        }

        return holds;
    }

    // One Holds query (CanHold's own value, Borrows' layer entry) from a fresh visit set. A no is every state's it met: with no yes to
    // stop it, the walk met each state below them, and none fits. So a later position's query reads the answers, not the visits (a visit
    // set shared across positions would answer no where a route holds: G408B-9 R2).
    private bool HoldsFrom(BoundType storage, in Hold asked)
    {
        this.retentionParts.Clear();
        if (this.Holds(storage, asked))
        {
            return true;
        }

        foreach (var met in this.retentionParts.Keys)
        {
            this.retentionHoldAnswers[(met, asked)] = false;
        }

        return false;
    }

    // Holds at a part held by value: an Empty one (SPEC 4.1: no element) holds no Place and no run below it; a view's run of its own Type
    // (4.8.1) and, for a stored value, what lies below it (4.3.3: a fill's value keeps its Loans).
    private bool PartHolds(in Hold part, in Hold asked) => (part.Flags & HoldFlags.Empty) == 0 || asked.Step == HoldStep.Inline
        ? this.Holds(part.Part, asked)
        : (asked.Flags & HoldFlags.Repeated) != 0 && part.Part is { } element && this.Fits(element, asked.Part!);

    // Whether a carrier is a Type or occurs among its components, by the one Type test of the retention walks (Identical: Origins
    // erased, and under the identity premises of the reading's view; SPEC 8.2.3: under `T is U` a handed `(E) -> U` returns a T, under
    // `E is D` a parameter D takes an E): a walk of the Type's parts, each entered once (retentionWalked), which ends with the Type
    // (IMPL A.4's height) and has no bound of its own.
    private bool Occurs(BoundType? type, BoundType carrier, BindingScope? view)
    {
        if (type is null || !this.retentionWalked.TryAdd((type, null, null), 0))
        {
            return false;
        }

        this.termWalks++;
        var occurs = this.Identical(type, carrier, view);
        for (var i = 0; !occurs && i < type.Components.Count; i++)
        {
            occurs = this.Occurs(type.Components[i], carrier, view);
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
    // layer), the referent of the borrow layer it is, through which it forms borrows (null: none), the walked signature's declared Type
    // where it was met (StorageAt.Signature, DECISIONS BU-113, BU-202: the input's declared Type at the walk's root; null where none
    // pairs), and its path, the nodes every one of which a position's closure must reach (retentionPaths: the layers it is formed through
    // but those Related to the layer above, its own last unless Related).
    private readonly record struct RetentionSource(int Node, int Input, BoundType? Own, BoundType? Through, BoundType? Signature, (int Start, int Count) Path);
}
