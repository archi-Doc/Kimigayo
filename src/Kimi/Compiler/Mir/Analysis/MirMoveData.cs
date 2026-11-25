// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the move paths of one MIR body (SPEC 15.1.3; DECISIONS R4-M21).
// Input: a validated body, its dataflow order (MirDataflow.Begin) and whether the body constructs its object (AdtDef.IsConstructor); the
// programs' TyCtxt (MirAnalysis's), whose instances' stored Types the paths read. Output:
// the paths numbered in pre-order, so a subtree is the range [path, End), each with its step from its parent, and the path of each Place.
// A path is a local or a step from a path: Field, Base (one depth at a time), ConstIndex or Downcast, keyed by its parent, kind and
// argument; a constructor's _0 has a child per Field it places (MirBuilder.Stored, by the one projection rule MirBody.SelectsPart: of no
// Type where the Field's own slot failed; SPEC 6.3.5, 23.1.2) and its base. That relies on Binding's contract (DECISIONS BU-250): every
// constructor write into an own Field whose slot failed is that Field Place, but an update's target, a derived write until PLAN G1340
// that defines nothing, as A1's update of an Uninitialized typed Field defines nothing (PLAN G1114: the exit's record is the typed
// twin's dependent second one). A PairLayer step is transparent: it is the Place itself in the owner case, and the accesses of a reference
// case end before it (MirInit). Deref and Index end a path: an access through them is an access of the prefix's path, and a Move through
// them has no Move Path, except into a raw Place, a raw pointer's Deref however read or its TyCtxt.RawPart parts (an untracked Take; PLAN
// G672). Roots whose events all lie in one chain from their StorageLive (a StorageDead elsewhere kills whatever state arrives) are
// chain-local and numbered after the flow roots, so a head stores only the flow paths. A path admits one placement when its root is a let
// binding or it lies in a constructor's Field placed once (SPEC 15.1.1, 11.7).
// Each placing location's facts (SPEC 14.10.3; DECISIONS R4-M175), which A1 applies: an Assign's Never parts, the components of its
// destination that its construction leaves unplaced, as their operand is the Never value, and its carries, the paths of a temp it takes
// whose own state A1 copies onto the destination's counterpart on each path: the temp's tracked parts and, for a whole temp that may hold
// nothing there (Absent), the temp itself (a Use's counterpart is its destination, of whatever kind: A1 keeps its state, exact for a
// first placement, and an overwrite's Use takes a MirLocalRole.Assigned temp, placed on every path), a Fill's components being the elements a Place can
// name alone and, for the others, the destination's Unplaced lane (Elements); a Places Call's, at the construction it inserts into
// (its entry not inserted, SPEC 12.4.3). Only Move Paths are numbered: a root, a constructor's Field or base, a path a Place of the body
// resolves to, and on the source side of a carry the counterpart of the destination's nearest Move Path at or below the component
// (closed upward and backward through the carries), so a source's parts whose counterparts lie under different Move Paths have
// different owners. A fact on a component no Place names goes to its owner, the nearest Move Path above it, as "its value lacks a part"
// (A1's Unplaced lane); a fact on a Move Path is its initialization (MaybeUninit), and a Places Call's is its construction's lacking
// entry. A published fact is a path's number, or its complement for the Unplaced lane. Invariants: a carry's source lies in a run
// recorded before the carry's (a temp is defined at a location the walk visits before the one that takes it, and runs are appended) and
// differs from its destination (MirValidator: no placing location takes its own destination temp, no insertion's value holds its
// construction), so one reverse pass over the carries closes them.
// Must not decide initialization, copy a body per view, intern projections or change the body.

#pragma warning disable SA1402 // The path record belongs to the paths.

namespace Kimi.Compiler.Mir;

/// <summary>A move path by its pre-order number: its root local, its parent (-1 for a root), the end of its subtree [path, End), whether
/// it admits one placement per binding lifetime (a let binding's, or a constructor's Field placed once, <see cref="AdtDef.PlacedOnce"/>)
/// and its step from its parent, whose Type <see cref="MirBody.ProjectedType"/> gives (a root's is unused).</summary>
/// <param name="Local">The root local.</param>
/// <param name="Parent">The parent path, or -1.</param>
/// <param name="End">The path after its subtree.</param>
/// <param name="Once">Whether it admits one placement.</param>
/// <param name="Kind">The step: Field, Base (one depth), ConstIndex or Downcast.</param>
/// <param name="Arg">The step's argument.</param>
internal readonly record struct MirMovePath(int Local, int Parent, int End, bool Once, MirProjectionKind Kind, int Arg);

/// <summary>The move paths of one body (SPEC 15.1.3, DECISIONS R4-M21), view-independent: <see cref="Build"/> collects them in one walk of
/// the body's uses, and <see cref="Find"/> gives a Place's path and whether the Place is the path itself, a raw Place below it or a Place
/// without a Move Path below it.</summary>
/// <param name="types">The Type context of the programs, whose instances' stored Types the paths read.</param>
internal sealed class MirMoveData(TyCtxt types)
{
    /// <summary>The Place is its path.</summary>
    internal const int Direct = 0;

    /// <summary>The Place is a raw Place below its path (<see cref="TyCtxt.RawPart"/>): an untracked Take (SPEC 5.2.3).</summary>
    internal const int Raw = 1;

    /// <summary>The Place is through a reference, an object handle or an index below its path: it has no Move Path.</summary>
    internal const int NoPath = 2;

    private readonly MirTable<(int Parent, MirProjectionKind Kind, int Arg)> children = new(); // Each child by creation index.
    private readonly MirTable<MirPlace> places = new(); // Each projected Place's path by creation index << 2 | access.
    private readonly MirTable<int> nevers = new(); // An Assign's Never parts' run, by statement index; a temp's newest tracked run, by ~local.
    private readonly MirTable<int> carried = new(); // An Assign's carries' run (pairs of a temp's path and the component's), by statement.
    private int[] runs = []; // Runs of paths: a count, the next run of the same temp (-1: none), then the paths.
    private int[] trail = []; // Mirror's steps from a path up to its temp.
    private int runCount;
    private int[] parents = []; // By creation index; locals first.
    private (MirProjectionKind Kind, int Arg)[] steps = []; // By creation index: the step from the parent.
    private int[] sizes = [];
    private int[] numbers = []; // Each creation index's pre-order number.
    private bool[] once = []; // By creation index: a let root or a constructor's Field placed once.
    private bool[] named = []; // By creation index: a Move Path (a root, a constructor's Field or base, a Place's path, closed).
    private int[] owners = []; // By creation index: the nearest Move Path at or above it.
    private bool[] constructions = []; // By local: a temp a Places Call inserts into.
    private int[] carryRuns = []; // The Assigns' carries' runs in recording order, each followed by its defined path (the closure).
    private int carryCount;
    private int[] indices = []; // The constant indices of the body's projections, ascending (Elements).
    private int indexCount = -1; // Their count, -1 until a Fill reads them.
    private int[] elements = []; // The components of the Fill a location builds (Elements).
    private int[] chains = []; // Each local's chain head, -1 without an event, -2 in more than one.
    private int[] placed = []; // Each local's chain where a location last placed it whole in its storage's lifetime, -1 for none.
    private bool[] live = []; // Whether a local's first event, in reverse postorder, is its StorageLive.
    private MirMovePath[] paths = [];
    private MirBody body = null!;
    private int created;
    private int count; // The Move Paths.

    /// <summary>Gets the paths by pre-order number.</summary>
    internal ReadOnlySpan<MirMovePath> Paths => this.paths.AsSpan(0, this.count);

    /// <summary>Gets the number of flow paths, numbered first: the paths whose state crosses a head.</summary>
    internal int FlowCount { get; private set; }

    /// <summary>Gets a value indicating whether a view decides an access of the body: a case-dependent access (a PairLayer step, a
    /// PairAcquire, an Adapt; <see cref="MirAccess.CaseDependent"/>) or a case branch, whose side the view selects (<see
    /// cref="MirBody.CaseSwitches"/>; a side may hold only case-free accesses, a by-case call's borrows of its callee).</summary>
    internal bool ViewDependent { get; private set; }

    /// <summary>Gets a value indicating whether the body constructs its object in _0 from fresh storage (SPEC 6.3).</summary>
    internal bool Constructing { get; private set; }

    /// <summary>Gets the most carries of one Assign (<see cref="Carries"/>).</summary>
    internal int MaxCarried { get; private set; }

    /// <summary>Gets the paths the last build created, numbered or not: its size, which a body bounds (a Fill lists its elements only
    /// where a fact needs them).</summary>
    internal int Created => this.created;

    /// <summary>Collects the move paths of a body and numbers them.</summary>
    /// <param name="body">The validated body.</param>
    /// <param name="flow">The dataflow, begun with the body.</param>
    internal void Build(MirBody body, MirDataflow flow)
    {
        this.body = body;
        var locals = body.Locals.Length;
        (this.created, this.ViewDependent, this.runCount, this.MaxCarried, this.carryCount, this.indexCount) = (0, body.CaseSwitches > 0, 0, 0, 0, -1);
        this.children.Clear();
        this.places.Clear();
        this.nevers.Clear();
        this.carried.Clear();
        MirDataflow.Grow(ref this.chains, locals);
        MirDataflow.Grow(ref this.live, locals);
        MirDataflow.Grow(ref this.constructions, locals);
        MirDataflow.Grow(ref this.placed, locals);
        Array.Fill(this.chains, -1, 0, locals);
        Array.Fill(this.placed, -1, 0, locals);
        Array.Clear(this.constructions, 0, locals);
        for (var i = 0; i < locals; i++)
        {
            var root = this.Add(-1); // Adding grows the arrays first.
            (this.once[root], this.named[root]) = (body.Locals[i] is { Kind: MirLocalKind.Var, Mutable: false }, true);
        }

        this.Constructing = body.Key.Kind == MirBodyKind.Fn && AdtDef.IsConstructor(body.Key.Decl);
        if (this.Constructing)
        {
            var site = types.OpenSite(body.Locals[0].Source.Syntax); // PLAN G651 U2b R5 (D-C4): the local's node site.
            var self = body.Locals[0].Type!; // A constructor's object, its struct's Type (MirBuilder.Body), never of no Type.
            for (var i = 0; i < AdtDef.Count(self); i++)
            {
                if (MirBody.SelectsPart(types, self, MirProjectionKind.Field, i, out _))
                {
                    var field = this.Child(0, MirProjectionKind.Field, i);
                    (this.once[field], this.named[field]) = (AdtDef.PlacedOnce(self, i), true);
                }
            }

            if (AdtDef.Base(types, self) is not null)
            {
                var layer = this.Child(0, MirProjectionKind.Base, 1); // Adding grows the arrays first.
                this.named[layer] = true;
            }

            types.CloseSite(site);
        }

        var events = new Events(this, flow);
        foreach (var block in flow.Order)
        {
            for (var i = 0; i <= body.Blocks[block].Count; i++)
            {
                var index = body.Blocks[block].Start + i;
                var statement = i < body.Blocks[block].Count;
                var site = types.OpenSite((statement ? body.Statements[index].Source : body.Blocks[block].Terminator.Source).Syntax); // D-C4
                events.At(block, statement ? body.Statements[index].Kind : MirStatementKind.Assign);
                MirUses.ForEach(body, new(block, i), ref events);
                if (statement && body.Statements[index].Kind == MirStatementKind.Assign)
                {
                    this.Nevers(index, flow.Head(block), body.Statements[index].Place, body.Statements[index].Rvalue);
                }
                else if (!statement && body.Blocks[block].Terminator.Places)
                {
                    this.Inserted(block, body.Blocks[block].Terminator);
                }

                types.CloseSite(site);
            }
        }

        this.NameCarried();
        this.Number(locals);
        for (var run = 0; run < this.runCount; run += this.runs[run] + 2)
        {
            for (var i = run + 2; i < run + 2 + this.runs[run]; i++)
            {
                var entry = this.runs[i];
                this.runs[i] = entry >= 0 && this.named[entry] ? this.numbers[entry] : ~this.numbers[this.owners[entry < 0 ? ~entry : entry]];
            }
        }
    }

    /// <summary>Gets the key of a Places Call's Never parts and carries (<see cref="NeverParts"/>, <see cref="Carries"/>), past every
    /// statement's index.</summary>
    /// <param name="block">The block the Call ends.</param>
    /// <returns>The key.</returns>
    internal int Insertion(int block) => this.body.Statements.Length + block;

    /// <summary>Gets whether a local is a construction a Places Call inserts into (SPEC 15.1.2, 12.4.3): its own lacking entry is read only
    /// where it is taken, as its literal's later lookups and insertions are its own steps.</summary>
    /// <param name="local">The local.</param>
    /// <returns>Whether it is.</returns>
    internal bool IsConstruction(int local) => this.constructions[local];

    /// <summary>Gets the Never parts of an Assign or a Places Call of the built body (SPEC 14.10.3): the facts of the components its
    /// construction places nothing into, their operand being the Never value: a Move Path's number (it holds no value), or the complement of
    /// its owner's (the owner lacks a part, A1's Unplaced lane).</summary>
    /// <param name="statement">The Assign's statement index, or the Places Call's <see cref="Insertion"/>.</param>
    /// <returns>The facts; empty for most.</returns>
    internal ReadOnlySpan<int> NeverParts(int statement)
        => this.nevers.TryGetValue(statement, out var run) ? this.runs.AsSpan(run + 2, this.runs[run]) : default;

    /// <summary>Gets the carries of an Assign or a Places Call of the built body (SPEC 14.10.3): pairs of a source fact, at or below a temp
    /// Place it takes, a part a construction may have left unplaced or the whole temp, which may hold nothing, and the destination fact that
    /// takes the source's own state on each path (the same path below the component, the component itself for the whole temp, a Use's
    /// being its destination; a Places Call's construction), each a number or a complement as <see cref="NeverParts"/>'.</summary>
    /// <param name="statement">The Assign's statement index, or the Places Call's <see cref="Insertion"/>.</param>
    /// <returns>The pairs, source then destination; empty for most.</returns>
    internal ReadOnlySpan<int> Carries(int statement)
        => this.carried.TryGetValue(statement, out var run) ? this.runs.AsSpan(run + 2, this.runs[run]) : default;

    /// <summary>Gets the path of a Place of the built body and how the Place is below it.</summary>
    /// <param name="place">A local's Place.</param>
    /// <param name="access"><see cref="Direct"/>, <see cref="Raw"/> or <see cref="NoPath"/>.</param>
    /// <returns>The path's number.</returns>
    internal int Find(MirPlace place, out int access)
    {
        var found = place.Projection == 0 ? place.Root << 2 : this.places[place];
        access = found & 3;
        return this.numbers[found >> 2];
    }

    private int Add(int parent)
    {
        var index = this.created++;
        MirDataflow.Grow(ref this.parents, this.created);
        MirDataflow.Grow(ref this.once, this.created);
        MirDataflow.Grow(ref this.named, this.created);
        MirDataflow.Grow(ref this.steps, this.created);
        (this.parents[index], this.once[index], this.named[index], this.steps[index]) = (parent, false, false, default);
        return index;
    }

    // The child of a path by a step, added at its first Place.
    private int Child(int parent, MirProjectionKind kind, int arg)
    {
        ref var child = ref this.children.Entry((parent, kind, arg), out var exists);
        if (!exists)
        {
            child = this.Add(parent);
            this.steps[child] = (kind, arg);
        }

        return child;
    }

    // The path of a Place by creation index << 2 | access, its prefix's first: a Base of depth d is d steps of depth 1, a PairLayer is its
    // prefix's path, and Deref and Index end the path. A Deref's access is its pointer's, whatever its prefix's: Raw through a raw pointer
    // (SPEC 5.2: `*p` is a raw Place however `p` is read; PLAN G222), else NoPath (a referent or payload, also inside a raw Place, never
    // offers Take, SPEC 15.1.5). Below a Raw Place a step keeps Raw only into a raw Place's part, Binding's one rule (TyCtxt.RawPart:
    // PLAN G672), else NoPath (an Array's or view's element is a referent, SPEC 4.7.1); below a NoPath Place any step keeps NoPath.
    private int Resolve(MirPlace place)
    {
        if (place.Projection == 0)
        {
            return place.Root << 2;
        }

        if (this.places.TryGetValue(place, out var found))
        {
            return found;
        }

        var row = this.body.Projections[place.Projection];
        var prefix = place with { Projection = row.Prefix };
        found = this.Resolve(prefix);
        found = row.Kind switch
        {
            MirProjectionKind.Deref => (found & ~3) | (this.body.TypeOf(prefix) is { IsPointer: true } ? Raw : NoPath),
            _ when (found & 3) == Raw && row.Kind != MirProjectionKind.PairLayer && !TyCtxt.RawPart(this.body.TypeOf(prefix)) => (found & ~3) | NoPath,
            _ when (found & 3) != Direct || row.Kind == MirProjectionKind.PairLayer => found,
            MirProjectionKind.Index => found | NoPath,
            MirProjectionKind.Base => this.Bases(found >> 2, row.Arg) << 2,
            _ => this.Child(found >> 2, row.Kind, row.Arg) << 2,
        };

        this.places.Add(place, found);
        this.named[found >> 2] = true;
        return found;
    }

    // The base subobject at a depth, one Base step per depth.
    private int Bases(int path, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            path = this.Child(path, MirProjectionKind.Base, 1);
            this.named[path] = true;
        }

        return path;
    }

    // SPEC 14.10.3 (DECISIONS R4-M175): an Assign's Never parts and carries, as paths created below its destination's, so a whole use sees
    // them. A Never part is a component of its aggregate whose operand is the Never value (a temp of Type Never), each of a Fill's
    // (Elements). A carry pairs each tracked path below the temp Place an operand or a Use takes (whole or a part) with the same path at
    // the component, so A1 copies the temp's state there on each path, and a whole temp that may hold nothing (Absent) with the component
    // itself: a form without a value on an assumed path places nothing, into every element of a Fill (SPEC 4.3.3). A Use's component is
    // its destination, whatever it is, which then keeps its state (MirInit): a first placement's, a destination the value's construct
    // cannot touch (a temp, which then may hold nothing too, so the Use does not place it for its chain, a let or var initialization or
    // binding, the return local), as an overwrite's Use is reached only by the paths that placed its value (MirBuilder.Replace,
    // MirLocalRole.Assigned, which MirInit checks); no kind of destination is read here. A temp's tracked paths are those its
    // Never parts and carries created below it (its runs): structure only; a Closure environment has no component path. An Array
    // literal's element has one, as a fixed array's, which no Place names (an Array's Index ends the path), so a whole use or an indexed
    // one sees it (SPEC 15.1.2, 15.1.3).
    private void Nevers(int statement, int chain, MirPlace destination, in MirRvalue rvalue)
    {
        var kind = (MirAggregateKind)rvalue.Op;
        var found = destination.IsStatic ? NoPath : this.Resolve(destination);
        if ((found & 3) != Direct || rvalue.Kind is not (MirRvalueKind.Aggregate or MirRvalueKind.Use) ||
            (rvalue.Kind == MirRvalueKind.Aggregate && kind == MirAggregateKind.Closure))
        {
            return;
        }

        var fill = rvalue.Kind == MirRvalueKind.Aggregate && kind == MirAggregateKind.Fill;
        var whole = destination.Projection == 0 && this.body.Locals[destination.Root].Kind == MirLocalKind.Temp;
        var copies = fill ? -1 : 1; // A Fill's components, listed where a fact first needs them (Elements).
        var parts = this.Open();
        for (var i = 0; i < rvalue.Count; i++)
        {
            var taken = this.Taken(rvalue, i);
            var never = taken >= 0 && taken < this.body.Locals.Length && this.Never(taken);
            copies = never && copies < 0 ? this.Elements(found >> 2, rvalue) : copies;
            for (var k = 0; never && k < copies; k++)
            {
                this.Push(fill ? this.elements[k] : rvalue.Kind == MirRvalueKind.Aggregate ? this.Component(found >> 2, rvalue, i) : found >> 2);
            }
        }

        parts = this.Close(parts);
        var carries = this.Open();
        for (var i = 0; i < rvalue.Count; i++)
        {
            var taken = this.Taken(rvalue, i);
            var temp = taken < 0 ? -1 : this.body.Operands[rvalue.Start + i].Place.Root;
            var absent = temp >= 0 && this.Absent(temp, taken, chain);
            var carried = temp >= 0 && (absent || this.nevers.TryGetValue(~temp, out _));
            copies = carried && copies < 0 ? this.Elements(found >> 2, rvalue) : copies;
            for (var k = 0; carried && k < copies; k++)
            {
                var component = fill ? this.elements[k] : rvalue.Kind == MirRvalueKind.Aggregate ? this.Component(found >> 2, rvalue, i) : found >> 2;
                if (this.Mirrored(temp, taken, component) && absent)
                {
                    this.Push(taken);
                    this.Push(component);
                }
            }
        }

        carries = this.Close(carries);
        this.Publish(statement, parts, carries);
        if (carries >= 0)
        {
            MirDataflow.Grow(ref this.carryRuns, this.carryCount + 2);
            (this.carryRuns[this.carryCount], this.carryRuns[this.carryCount + 1]) = (carries, found >> 2);
            this.carryCount += 2;
        }

        if ((parts >= 0 || carries >= 0) && whole)
        {
            var tracked = this.Open();
            for (var at = parts + 2; parts >= 0 && at < parts + 2 + this.runs[parts]; at++)
            {
                this.Push(this.runs[at]);
            }

            for (var at = carries + 3; carries >= 0 && at < carries + 2 + this.runs[carries]; at += 2)
            {
                if (this.runs[at] != found >> 2)
                {
                    this.Push(this.runs[at]);
                }
                else
                {
                    this.placed[destination.Root] = -1; // Its own path is not tracked: its own bit, which its taker then carries.
                }
            }

            tracked = this.Close(tracked);
            if (tracked >= 0)
            {
                this.Track(destination.Root, tracked);
            }
        }
    }

    // Whether the temp Place an operand takes is a whole temp that may hold nothing where its location, in a chain, takes it (SPEC
    // 14.10.3): not the Never value, whose component is a Never part, and not placed whole earlier in the chain, a straight line, on which
    // it then holds a value on every path.
    private bool Absent(int temp, int taken, int chain) => this.parents[taken] < 0 && this.placed[temp] != chain && !this.Never(temp);

    // Pushes the carries of a temp's tracked facts at or below the Place an operand takes, each with the same path at the component (a
    // complement, its Unplaced lane, as the same lane), or, for a component that is a complement (a Fill's elements no Place names), with
    // that owner's Unplaced lane, and gives whether none of them is that Place's own path, which its own carry would repeat.
    private bool Mirrored(int temp, int taken, int component)
    {
        var apart = true;
        for (var run = this.nevers.TryGetValue(~temp, out var newest) ? newest : -1; run >= 0; run = this.runs[run + 1])
        {
            for (var at = run + 2; at < run + 2 + this.runs[run]; at++)
            {
                var tracked = this.runs[at];
                var path = tracked < 0 ? ~tracked : tracked;
                var above = path;
                while (above >= 0 && above != taken)
                {
                    above = this.parents[above];
                }

                if (above == taken)
                {
                    var mirror = component < 0 ? ~component : this.Mirror(path, taken, component);
                    this.Push(tracked);
                    this.Push(tracked < 0 || component < 0 ? ~mirror : mirror);
                    apart &= tracked != taken;
                }
            }
        }

        return apart;
    }

    // SPEC 12.4.3, 14.10.3: the Never parts and carries of a Places Call, at the construction it inserts into (MirBody.Construction):
    // each operand after the borrow is a part of the entry, which is not inserted while a part has no value. An operand that is the
    // Never value is a Never part; another carries its temp's whole state (a temp that holds nothing holds the Never value) and each
    // tracked fact below it. All are the construction's lacking entry: its own Unplaced lane, which its own steps do not read
    // (IsConstruction), and a construction that is a whole temp tracks it, so the location that takes it carries it to its taker.
    private void Inserted(int block, in MirTerminator call)
    {
        var construction = this.body.Construction(block)!.Value; // The validator's Placed.
        var found = construction.IsStatic ? NoPath : this.Resolve(construction);
        if ((found & 3) != Direct)
        {
            return;
        }

        var own = ~(found >> 2);
        var operands = this.body.Operands.Slice(call.Start + 1, call.Count - 1);
        var parts = this.Open();
        foreach (ref readonly var operand in operands)
        {
            if (this.Never(operand.Place.Root))
            {
                this.Push(own);
            }
        }

        parts = this.Close(parts);
        var carries = this.Open();
        foreach (ref readonly var operand in operands)
        {
            var temp = operand.Place.Root;
            if (this.Never(temp))
            {
                continue;
            }

            this.Push(temp);
            this.Push(own);
            for (var run = this.nevers.TryGetValue(~temp, out var below) ? below : -1; run >= 0; run = this.runs[run + 1])
            {
                for (var at = run + 2; at < run + 2 + this.runs[run]; at++)
                {
                    this.Push(this.runs[at]);
                    this.Push(own);
                }
            }
        }

        this.Publish(this.Insertion(block), parts, this.Close(carries));
        if (construction.Projection == 0 && this.body.Locals[construction.Root].Kind == MirLocalKind.Temp && !this.constructions[construction.Root])
        {
            this.constructions[construction.Root] = true;
            var tracked = this.Open();
            this.Push(own);
            this.Track(construction.Root, this.Close(tracked));
        }
    }

    // Whether a local is the Never value, a temp of Type Never: an operand that places nothing (SPEC 14.10.3).
    private bool Never(int local) => ReferenceEquals(this.body.Locals[local].Type, BoundType.Never);

    // A placing location's Never parts and carries by its key (runs, -1 for none).
    private void Publish(int location, int parts, int carries)
    {
        if (parts >= 0)
        {
            this.nevers.Add(location, parts);
        }

        if (carries >= 0)
        {
            this.carried.Add(location, carries);
            this.MaxCarried = Math.Max(this.MaxCarried, this.runs[carries] >> 1);
        }
    }

    // Closes the Move Paths backward through an Assign's carries, the latest first (the invariant above): a carry's source and destination
    // take the same steps from the temp Place taken, a Move Path (Resolve), and from the component, so the source counterpart of the
    // destination's nearest Move Path at or below the component is one too, with its prefixes. A part a Place observes then keeps its
    // own state across the carry, and a source's parts whose counterparts lie under different Move Paths never share an owner (the
    // lacking part of one sibling never reaches another). A carry onto the defined path itself names nothing: a Use's source there is the
    // Place taken, and a Fill's elements no Place names give their facts to it as their owner, with no Move Path at or below them.
    private void NameCarried()
    {
        for (var c = this.carryCount - 2; c >= 0; c -= 2)
        {
            var (run, defined) = (this.carryRuns[c], this.carryRuns[c + 1]);
            for (var at = run + 2; at < run + 2 + this.runs[run]; at += 2)
            {
                var (source, destination) = (this.runs[at] < 0 ? ~this.runs[at] : this.runs[at], this.runs[at + 1] < 0 ? ~this.runs[at + 1] : this.runs[at + 1]);
                if (destination == defined)
                {
                    continue;
                }

                while (!this.named[source] && !this.named[destination])
                {
                    (source, destination) = (this.parents[source], this.parents[destination]);
                }

                for (; !this.named[source]; source = this.parents[source])
                {
                    this.named[source] = true;
                }
            }
        }
    }

    // A run of paths joins a temp's tracked runs, the newest first.
    private void Track(int temp, int run)
    {
        ref var newest = ref this.nevers.Entry(~temp, out var exists);
        (this.runs[run + 1], newest) = (exists ? newest : -1, run);
    }

    // The path of the temp's Place, whole or a part, that an aggregate's or a Use's operand at a position takes, -1 for any other operand.
    private int Taken(in MirRvalue rvalue, int i)
    {
        ref readonly var operand = ref this.body.Operands[rvalue.Start + i];
        if (operand.Kind == MirOperandKind.Const || operand.Place.IsStatic || this.body.Locals[operand.Place.Root].Kind != MirLocalKind.Temp)
        {
            return -1;
        }

        var found = this.Resolve(operand.Place);
        return (found & 3) == Direct ? found >> 2 : -1;
    }

    // A run of paths: its count and next run (-1) reserved; Close fills the count, or drops an empty run and gives -1.
    private int Open()
    {
        var run = this.runCount;
        this.Push(0);
        this.Push(-1);
        return run;
    }

    private int Close(int run)
    {
        this.runs[run] = this.runCount - run - 2;
        if (this.runs[run] > 0)
        {
            return run;
        }

        this.runCount = run;
        return -1;
    }

    // The path of an aggregate's component (its operand at position i; a Fill's are its Elements): a Tuple's Field, an array's ConstIndex,
    // an enum Case's payload Field under its Downcast (the aggregate's ordinal), a struct's base (its first operand where it has one) or own
    // Field.
    private int Component(int path, in MirRvalue aggregate, int i) => (MirAggregateKind)aggregate.Op switch
    {
        MirAggregateKind.Tuple => this.Child(path, MirProjectionKind.Field, i),
        MirAggregateKind.FixedArray or MirAggregateKind.Array => this.Child(path, MirProjectionKind.ConstIndex, i),
        _ when AdtDef.IsEnum(aggregate.Type) => this.Child(this.Child(path, MirProjectionKind.Downcast, aggregate.Arg), MirProjectionKind.Field, i),
        _ when AdtDef.Base(types, aggregate.Type!) is not null => this.Child(path, i == 0 ? MirProjectionKind.Base : MirProjectionKind.Field, i == 0 ? 1 : i - 1),
        _ => this.Child(path, MirProjectionKind.Field, i),
    };

    // SPEC 4.3.3, 15.1.3: the components of a Fill into a path (their count; the facts in elements): the elements below its length a
    // Place can name alone, at the constant indices of the body's projections, and, for the others, the path's Unplaced lane (its
    // complement), as their owner: no use tells them apart (a whole or a runtime-index use reads the path; a Move, a Drop and a definition
    // apply to it), so they have no path, and a Fill's facts stay bounded by the body. An open length names no element for every binding
    // (SPEC 4.4.2), so all of its elements are the others. Read only where a fact needs the list (Nevers).
    private int Elements(int path, in MirRvalue fill)
    {
        if (fill.Type is not { Kind: BoundTypeKind.FixedArray } type)
        {
            return 0;
        }

        var closed = type.LengthExpression is null;
        if (closed && this.indexCount < 0)
        {
            this.indexCount = 0;
            foreach (ref readonly var row in this.body.Projections)
            {
                if (row.Kind == MirProjectionKind.ConstIndex)
                {
                    MirDataflow.Grow(ref this.indices, this.indexCount + 1);
                    this.indices[this.indexCount++] = row.Arg;
                }
            }

            this.indices.AsSpan(0, this.indexCount).Sort();
        }

        var count = 0;
        for (var at = 0; closed && at < this.indexCount && this.indices[at] < type.Length; at++)
        {
            if (at == 0 || this.indices[at] != this.indices[at - 1])
            {
                MirDataflow.Grow(ref this.elements, count + 1);
                this.elements[count++] = this.Child(path, MirProjectionKind.ConstIndex, this.indices[at]);
            }
        }

        if (!closed || count < type.Length)
        {
            MirDataflow.Grow(ref this.elements, count + 1);
            this.elements[count++] = ~path;
        }

        return count;
    }

    // The path below another that a path is below a path above it (top): its steps from top, taken again from the other.
    private int Mirror(int path, int top, int at)
    {
        var depth = 0;
        for (var c = path; c != top; c = this.parents[c])
        {
            MirDataflow.Grow(ref this.trail, depth + 1);
            this.trail[depth++] = c;
        }

        while (depth > 0)
        {
            var step = this.steps[this.trail[--depth]];
            at = this.Child(at, step.Kind, step.Arg);
        }

        return at;
    }

    private void Push(int value)
    {
        MirDataflow.Grow(ref this.runs, this.runCount + 1);
        this.runs[this.runCount++] = value;
    }

    // Numbers the Move Paths in pre-order without a stack: subtree sizes in reverse creation order (a child is created after its parent),
    // flow roots then chain-local roots by their sizes, then each child at its parent's cursor; a path no Place names gets no number and
    // its owner, the nearest Move Path above it (a Move Path's prefixes are Move Paths).
    private void Number(int locals)
    {
        MirDataflow.Grow(ref this.sizes, this.created);
        MirDataflow.Grow(ref this.numbers, this.created);
        MirDataflow.Grow(ref this.owners, this.created);
        MirDataflow.Grow(ref this.paths, this.created);
        for (var c = 0; c < this.created; c++)
        {
            (this.sizes[c], this.owners[c]) = (this.named[c] ? 1 : 0, this.named[c] ? c : this.owners[this.parents[c]]);
        }

        for (var c = this.created - 1; c >= locals; c--)
        {
            this.sizes[this.parents[c]] += this.sizes[c];
        }

        var next = 0;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var local = 0; local < locals; local++)
            {
                if ((pass == 1) == (this.chains[local] >= 0 && this.live[local]))
                {
                    (this.numbers[local], next) = (next, next + this.sizes[local]);
                }
            }

            this.FlowCount = pass == 0 ? next : this.FlowCount;
        }

        this.count = next;
        var cursors = this.sizes; // Reused: a node's size is read before it becomes its cursor.
        for (var c = 0; c < this.created; c++)
        {
            if (!this.named[c])
            {
                continue;
            }

            var parent = this.parents[c];
            var number = parent < 0 ? this.numbers[c] : cursors[parent];
            var end = number + this.sizes[c];
            if (parent >= 0)
            {
                (this.numbers[c], cursors[parent]) = (number, cursors[parent] + this.sizes[c]);
            }

            this.once[c] |= parent >= 0 && this.once[parent];
            var local = parent < 0 ? c : this.paths[this.numbers[parent]].Local;
            this.paths[number] = new(local, parent < 0 ? -1 : this.numbers[parent], end, this.once[c], this.steps[c].Kind, this.steps[c].Arg);
            cursors[c] = number + 1;
        }
    }

    // The events of a body's locals in reverse postorder (their chain and whether the first is their StorageLive; a StorageDead is none),
    // and its Places' paths, an Opaque site's undecided use's at its own Place, which A1 checks (DECISIONS Z17).
    private struct Events(MirMoveData data, MirDataflow flow) : IMirUses
    {
        private int chain;
        private MirStatementKind kind;

        public readonly void Operand(in MirOperand operand)
        {
        }

        public readonly void Effect(MirEffect effects, int site)
        {
        }

        public readonly void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic)
            {
                return;
            }

            _ = data.Resolve(place);
            data.ViewDependent |= (access & MirAccess.CaseDependent) != 0;
            if (this.kind is MirStatementKind.StorageLive or MirStatementKind.StorageDead)
            {
                data.placed[place.Root] = -1;
            }
            else if (place.Projection == 0 && (access & MirAccess.Defines) != 0)
            {
                data.placed[place.Root] = this.chain;
            }

            ref var chain = ref data.chains[place.Root];
            if (this.kind == MirStatementKind.StorageDead)
            {
                return;
            }

            if (chain == -1)
            {
                data.live[place.Root] = this.kind == MirStatementKind.StorageLive;
            }

            chain = chain == -1 || chain == this.chain ? this.chain : -2;
        }

        // A location of a block; a terminator's kind is Assign.
        internal void At(int block, MirStatementKind kind) => (this.chain, this.kind) = (flow.Head(block), kind);
    }
}
