// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5-1's positions and region variables (SPEC 15.6, 15.6.1, 15.3.5; DECISIONS R4-M24). A cell is a move path (MirMoveData) whose case Type
// (its root's, then each step's, MirBody.ProjectedType) carries Origins in its own storage: a leaf all of its Type's, an inner path those of
// each storage step without a child path and those no step stores (a Phantom Origin). A table holds a Type's distinct atoms, every unwritten
// borrow-layer Origin being its one null atom, each marked with the layers it lies behind; a meet has an edge from each operand. A holder is
// (definition, cell): the entry of an Arg's or the return local's cells, or a whole write of the cell or an ancestor (def 0 the first),
// numbered cell-major in path order, so a subtree's holders and reaching bits are one range; a cell with one holder has no bit. A holder has
// a variable per atom of its cell, a static a run of escape variables (K11). The reaching definitions run forward (pass A; pass C replays
// them with the Loans' counts).
internal sealed partial class MirBorrows
{
    private const byte Behind = 1; // An atom behind a layer (TyCtxt.Layer): an Arg's reaches caller storage through it.
    private const byte Writable = 2; // An atom behind a layer invariant in its target (TyCtxt.InvariantTarget), which a call may write.
    private readonly List<BoundOrigin?> atoms = []; // The tables: each path's, each static's, then each call's.
    private readonly List<byte> layers = []; // By atom entry: Behind and Writable.
    private readonly List<(int From, int To)> meets = []; // By table: an operand's entry and its meet's.
    private readonly List<(int Entry, int Layer)> inside = []; // An atom inside a borrow layer and the layer's Origin (a call's well-formedness).
    private readonly List<BoundOrigin?> stored = []; // Own's scratch: the atoms an inner path's steps store, then its Type's others.
    private BoundType?[] pathTypes = []; // By path: its case Type.
    private int[] atomStart = []; // By table (the paths', then the statics'): its first entry.
    private int[] meetStart = [];
    private bool[] carrying = []; // By local: whether a path of it is a cell.
    private int[] holderStart = []; // By path: its holders; a path that is no cell has none.
    private int[] holderCell = []; // By holder: its cell.
    private int[] holderVar = []; // By holder: its first variable.
    private int[] bitStart = []; // By path: the first reaching bit of its holders; only a cell with two or more has bits.
    private int[] bitHolder = []; // By reaching bit: its holder.
    private bool[] held = []; // By path: whether an entry or a whole write defines it.
    private int[] holderAt = []; // By holder: the location defining it, -1 for an entry or a def 0 nothing defines.
    private int[] replacedAt = []; // By location id: the path it replaces whole (K3, Replaced) << 1, | 1 where it defines it; -1 for none.
    private int[] defStart = []; // By location id: the holders its whole write defines, one per cell of the subtree.
    private int[] defined = [];
    private int[] staticVar = []; // By static: its first variable.
    private int[] varHolder = []; // By variable: its holder, -1 for a static's, a Loan's, a call's or a junction.
    private bool[] varEscape = [];
    private int[] edgeFrom = [];
    private int[] edgeTo = [];
    private int edgeCount;
    private int varCount;
    private int holderCount;
    private int flowBits; // The reaching bits of the flow cells, stored at heads; the chain-local ones' follow in the scratch from chainBase.
    private int bitCount;
    private bool generating; // Whether pass A's replay generates the flow graph.

    // How a Place covers its nearest path under the view (the precision rule of R4-M24).
    private enum Coverage : byte
    {
        Whole, // The Place is the path: its subtree.
        Range, // Through an Index, whose element any ConstIndex child may be: the path's subtree.
        Own, // Through a reference, an object handle or a raw pointer, below which no path lies: the path's own cell.
    }

    // The distinct atoms of a Type (a step's or a phantom's) into a list: its outer Origin, null for an unwritten borrow layer's, its Origin
    // arguments and its components'.
    private static void Collect(BoundType? type, List<BoundOrigin?> into)
    {
        if (type is null)
        {
            return;
        }

        if ((type.Origin is not null || MirTypes.IsReference(type)) && !into.Contains(type.Origin))
        {
            into.Add(type.Origin);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (!into.Contains(type.OriginArguments[i]))
            {
                into.Add(type.OriginArguments[i]);
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            Collect(type.Components[i], into);
        }
    }

    // The id of a location: a statement's index in the body, or a terminator's after them.
    private int Id(MirLocation at)
    {
        ref readonly var block = ref this.body.Blocks[at.Block];
        return at.Index < block.Count ? block.Start + at.Index : this.body.Statements.Length + at.Block;
    }

    // Each path's case Type and own atoms, a path with own atoms being a cell; then each static's table.
    private void Cells()
    {
        var paths = moves.Paths;
        var tables = paths.Length + this.body.Statics.Length;
        MirDataflow.Grow(ref this.pathTypes, paths.Length);
        MirDataflow.Grow(ref this.atomStart, tables + 1);
        MirDataflow.Grow(ref this.meetStart, tables + 1);
        MirDataflow.Grow(ref this.carrying, this.body.Locals.Length);
        Array.Clear(this.carrying, 0, this.body.Locals.Length);
        this.atoms.Clear();
        this.layers.Clear();
        this.meets.Clear();
        this.inside.Clear();
        for (var p = 0; p < paths.Length; p++)
        {
            var path = paths[p];
            var type = path.Parent < 0 ? this.TypeOf(new MirPlace(path.Local)) : this.body.ProjectedType(this.pathTypes[path.Parent], path.Kind, path.Arg);
            (this.pathTypes[p], this.atomStart[p], this.meetStart[p]) = (type, this.atoms.Count, this.meets.Count);
            this.Own(p, type);
            this.carrying[path.Local] |= this.atoms.Count > this.atomStart[p];
        }

        for (var s = 0; s < this.body.Statics.Length; s++)
        {
            (this.atomStart[paths.Length + s], this.meetStart[paths.Length + s]) = (this.atoms.Count, this.meets.Count);
            this.Positions(this.TypeOf(new MirPlace(~s)), this.atoms.Count, 0, -1);
        }

        (this.atomStart[tables], this.meetStart[tables]) = (this.atoms.Count, this.meets.Count);
    }

    // A path's own atoms: a leaf's are its Type's; an inner path's those of each storage step without a child path, and those of its Type
    // no step stores (a Phantom Origin, SPEC 15.3.5). The steps are the Fields and the enum cases while their Types exist, the base, and a
    // fixed array's element, which its ConstIndex children cover only when they are all of its elements.
    private void Own(int path, BoundType? type)
    {
        var start = this.atoms.Count;
        if (moves.Paths[path].End == path + 1)
        {
            this.Positions(type, start, 0, -1);
            return;
        }

        this.stored.Clear();
        foreach (var kind in (ReadOnlySpan<MirProjectionKind>)[MirProjectionKind.Field, MirProjectionKind.Downcast, MirProjectionKind.Base])
        {
            for (var arg = kind == MirProjectionKind.Base ? 1 : 0; arg < 2 || kind != MirProjectionKind.Base; arg++)
            {
                if (this.body.ProjectedType(type, kind, arg) is not { } part)
                {
                    break;
                }

                this.Step(path, part, kind, arg, start);
            }
        }

        if (type is { Kind: BoundTypeKind.FixedArray, Components: [var element] })
        {
            this.Step(path, element, MirProjectionKind.ConstIndex, -1, start);
        }

        var steps = this.stored.Count;
        Collect(type, this.stored);
        for (var i = steps; i < this.stored.Count; i++)
        {
            this.Atom(this.stored[i], start, 0);
        }
    }

    // A storage step of an inner path: its atoms are stored, and its own where no child path covers it (a fixed array's element, arg -1,
    // where fewer ConstIndex children than its elements exist).
    private void Step(int path, BoundType part, MirProjectionKind kind, int arg, int start)
    {
        Collect(part, this.stored);
        var paths = moves.Paths;
        var covered = 0;
        for (var child = path + 1; child < paths[path].End; child = paths[child].End)
        {
            covered += paths[child].Kind == kind && (arg < 0 || paths[child].Arg == arg) ? 1 : 0;
        }

        if (covered == 0 || covered < (arg < 0 ? this.pathTypes[path]!.Length : 1))
        {
            this.Positions(part, start, 0, -1);
        }
    }

    // K2, the one walk of a Type's Origin positions: its distinct atoms into the table from start, each marked with the layers it lies
    // behind (Behind, Writable); an atom inside a borrow layer adds a row (its entry, the layer's Origin entry), a call's well-formedness.
    private void Positions(BoundType? type, int start, byte behind, int layer)
    {
        if (type is null)
        {
            return;
        }

        var reference = MirTypes.IsReference(type);
        var own = type.Origin is not null || reference ? this.Atom(type.Origin, start, behind) : -1;
        if (own >= 0 && layer >= 0)
        {
            this.inside.Add((own, layer));
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            var entry = this.Atom(type.OriginArguments[i], start, behind);
            if (layer >= 0)
            {
                this.inside.Add((entry, layer));
            }
        }

        _ = TyCtxt.Layer(type, out var referent, out _);
        var below = !ReferenceEquals(referent, type);
        behind |= (byte)((below ? Behind : 0) | (below && TyCtxt.InvariantTarget(type, null) ? Writable : 0));
        for (var i = 0; i < type.Components.Count; i++)
        {
            this.Positions(type.Components[i], start, behind, reference ? own : layer);
        }
    }

    // An atom's entry in the table from start, marked, added at its first occurrence; a meet's operands too, each with a meet edge (K5).
    private int Atom(BoundOrigin? origin, int start, byte behind)
    {
        var entry = this.Find(origin, start, this.atoms.Count);
        if (entry < 0)
        {
            entry = this.atoms.Count;
            this.atoms.Add(origin);
            this.layers.Add(0);
            for (var i = 0; origin is not null && i < origin.Operands.Count; i++)
            {
                this.meets.Add((this.Atom(origin.Operands[i], start, behind), entry));
            }
        }

        this.layers[entry] |= behind;
        return entry;
    }

    // An atom's entry in the table [start, end), or -1; null is the table's unwritten borrow-layer Origin.
    private int Find(BoundOrigin? origin, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            if (ReferenceEquals(this.atoms[i], origin))
            {
                return i;
            }
        }

        return -1;
    }

    // Each cell's holders, as (cell, location) pairs indexed by cell (MirDataflow.Index): the entries of an Arg's and the return local's
    // cells, then each whole write of a cell or an ancestor in location order, then def 0 of a cell nothing defines; a cell with one holder
    // has no reaching bit; then the holders each location defines.
    private void Holders()
    {
        var paths = moves.Paths;
        var ids = this.body.Statements.Length + this.body.Blocks.Length;
        var pairs = 0;
        MirDataflow.Grow(ref this.held, paths.Length);
        MirDataflow.Grow(ref this.bitStart, paths.Length + 1);
        MirDataflow.Grow(ref this.replacedAt, ids);
        Array.Clear(this.held, 0, paths.Length);
        for (var local = 0; local < this.body.Locals.Length && this.body.Locals[local].Kind is MirLocalKind.Return or MirLocalKind.Arg; local++)
        {
            pairs = this.Defines(moves.Find(new(local), out _), -1, pairs);
        }

        for (var id = 0; id < ids; id++)
        {
            var path = this.Replaced(id, out var defines);
            var mark = defines ? 1 : 0;
            this.replacedAt[id] = path < 0 ? -1 : (path << 1) | mark;
            pairs = path >= 0 && defines ? this.Defines(path, id, pairs) : pairs;
        }

        for (var p = 0; p < paths.Length; p++)
        {
            pairs = this.held[p] ? pairs : this.Pair(p, -1, pairs);
        }

        MirDataflow.Index(this.keys.AsSpan(0, pairs), this.values.AsSpan(0, pairs), paths.Length, ref this.holderStart, ref this.holderAt);
        (this.holderCount, this.bitCount) = (pairs, 0);
        for (var p = 0; p < paths.Length; p++)
        {
            this.bitStart[p] = this.bitCount;
            this.bitCount += this.holderStart[p + 1] - this.holderStart[p] > 1 ? this.holderStart[p + 1] - this.holderStart[p] : 0;
        }

        this.bitStart[paths.Length] = this.bitCount;
        this.flowBits = this.bitStart[moves.FlowCount];
        MirDataflow.Grow(ref this.holderCell, this.holderCount);
        MirDataflow.Grow(ref this.bitHolder, this.bitCount);
        for (var p = 0; p < paths.Length; p++)
        {
            for (var h = this.holderStart[p]; h < this.holderStart[p + 1]; h++)
            {
                this.holderCell[h] = p;
                if (this.Bit(h) is var bit and >= 0)
                {
                    this.bitHolder[bit] = h;
                }
            }
        }

        for (var h = 0; h < pairs; h++)
        {
            (this.keys[h], this.values[h]) = (this.holderAt[h], h);
        }

        MirDataflow.Index(this.keys.AsSpan(0, pairs), this.values.AsSpan(0, pairs), ids, ref this.defStart, ref this.defined);
    }

    // The holders an entry (location -1) or a whole write of a path defines, one per cell of its subtree; returns the pairs.
    private int Defines(int path, int id, int pairs)
    {
        for (var p = path; p < moves.Paths[path].End; p++)
        {
            pairs = this.Pair(p, id, pairs);
        }

        return pairs;
    }

    // A holder of a cell as a pair of the cell and its location, the path marked held; a path that is no cell has none.
    private int Pair(int path, int id, int pairs)
    {
        if (this.atomStart[path + 1] == this.atomStart[path])
        {
            return pairs;
        }

        MirDataflow.Grow(ref this.keys, pairs + 1);
        MirDataflow.Grow(ref this.values, pairs + 1);
        (this.keys[pairs], this.values[pairs], this.held[path]) = (path, id, true);
        return pairs + 1;
    }

    // K3, the one location decoder: the path a location (by id) replaces whole, an Assign or a returning Call writing it (defines), or
    // ends, a StorageLive, a StorageDead or a Drop of it; -1 for any other, a write through a reference or an index or of a static included.
    private int Replaced(int id, out bool defines)
    {
        var place = new MirPlace(-1);
        var statement = id < this.body.Statements.Length ? this.body.Statements[id] : default;
        var terminator = id < this.body.Statements.Length ? default : this.body.Blocks[id - this.body.Statements.Length].Terminator;
        if (id < this.body.Statements.Length && statement.Kind is not (MirStatementKind.EndBorrow or MirStatementKind.FakeRead))
        {
            (place, defines) = (statement.Place, statement.Kind == MirStatementKind.Assign);
        }
        else if (terminator is { Kind: MirTerminatorKind.Drop } or { Kind: MirTerminatorKind.Call, Target: >= 0 })
        {
            (place, defines) = (terminator.Place, terminator.Kind == MirTerminatorKind.Call);
        }
        else
        {
            defines = false;
        }

        return !place.IsStatic && this.Cover(place, out var coverage) is var path && coverage == Coverage.Whole ? path : -1;
    }

    // The nearest path of a Place of a local under the view and how the Place covers it (the precision rule): the step nearest the root that
    // ends a path, a Deref, an Index or a step following a reference under the view (MirCases.Follows), decides.
    private int Cover(MirPlace place, out Coverage coverage)
    {
        var projections = this.body.Projections;
        var path = place;
        coverage = Coverage.Whole;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            var kind = projections[id].Kind;
            if (kind is MirProjectionKind.Deref or MirProjectionKind.Index || cases.Follows(place with { Projection = id }))
            {
                (path, coverage) = (place with { Projection = projections[id].Prefix }, kind == MirProjectionKind.Index ? Coverage.Range : Coverage.Own);
            }
        }

        return moves.Find(path, out _);
    }

    // The reaching bit of a holder, -1 for its cell's only holder, which always reaches.
    private int Bit(int holder)
    {
        var cell = this.holderCell[holder];
        return this.holderStart[cell + 1] - this.holderStart[cell] > 1 ? this.bitStart[cell] + holder - this.holderStart[cell] : -1;
    }

    // A reaching bit's position in the scratch: the flow cells' bits from word 0, the chain-local ones' from chainBase.
    private int Position(int bit) => bit < this.flowBits ? bit : (this.chainBase << 6) + bit - this.flowBits;

    // Each holder's variables, an Arg's entry's atoms behind a layer escaping (caller storage, SPEC 15.6.4), and each static's run of escape
    // variables (K11), each with its table's meet edges.
    private void Variables()
    {
        (this.varCount, this.edgeCount) = (0, 0);
        MirDataflow.Grow(ref this.holderVar, this.holderCount);
        MirDataflow.Grow(ref this.staticVar, this.body.Statics.Length);
        for (var h = 0; h < this.holderCount; h++)
        {
            var cell = this.holderCell[h];
            var entry = h == this.holderStart[cell] && this.body.Locals[moves.Paths[cell].Local].Kind == MirLocalKind.Arg;
            this.holderVar[h] = this.TableVariables(cell, h);
            for (var i = this.atomStart[cell]; entry && i < this.atomStart[cell + 1]; i++)
            {
                this.varEscape[this.holderVar[h] + i - this.atomStart[cell]] = (this.layers[i] & Behind) != 0;
            }
        }

        for (var s = 0; s < this.body.Statics.Length; s++)
        {
            var table = moves.Paths.Length + s;
            this.staticVar[s] = this.TableVariables(table, -1);
            Array.Fill(this.varEscape, true, this.staticVar[s], this.atomStart[table + 1] - this.atomStart[table]);
        }
    }

    // The variables of a table's atoms, with its meet edges (K5); returns the first.
    private int TableVariables(int table, int holder)
    {
        var first = this.Variables(this.atomStart[table + 1] - this.atomStart[table], holder);
        this.Meets(this.meetStart[table], this.meetStart[table + 1], first - this.atomStart[table]);
        return first;
    }

    // A run of new variables of a holder (-1: none's); returns the first.
    private int Variables(int count, int holder)
    {
        var first = this.varCount;
        this.varCount += count;
        MirDataflow.Grow(ref this.varHolder, this.varCount);
        MirDataflow.Grow(ref this.varEscape, this.varCount);
        Array.Fill(this.varHolder, holder, first, count);
        Array.Fill(this.varEscape, false, first, count);
        return first;
    }

    // The meet edges [first, end), from each operand's variable to its meet's, entries offset by the variables' base.
    private void Meets(int first, int end, int offset)
    {
        for (var m = first; m < end; m++)
        {
            this.Edge(offset + this.meets[m].From, offset + this.meets[m].To);
        }
    }

    private void Edge(int from, int to)
    {
        if (from != to)
        {
            MirDataflow.Grow(ref this.edgeFrom, this.edgeCount + 1);
            MirDataflow.Grow(ref this.edgeTo, this.edgeCount + 1);
            (this.edgeFrom[this.edgeCount], this.edgeTo[this.edgeCount++]) = (from, to);
        }
    }

    // The reaching definitions on the scratch: the entered holders at the entry; a location ending or replacing a path kills its subtree's
    // holders, a whole write then reaches with its own (pass C changes the counts of those whose cells are live).
    private void Enter()
    {
        Array.Clear(this.state, 0, this.width);
        for (var local = 0; local < this.body.Locals.Length && this.body.Locals[local].Kind is MirLocalKind.Return or MirLocalKind.Arg; local++)
        {
            var root = moves.Find(new(local), out _);
            for (var p = root; p < moves.Paths[root].End; p++)
            {
                if (this.holderStart[p + 1] > this.holderStart[p])
                {
                    this.Gain(this.holderStart[p]);
                }
            }
        }
    }

    private void Define(int id)
    {
        if (this.replacedAt[id] < 0)
        {
            return;
        }

        var path = this.replacedAt[id] >> 1;
        var first = this.bitStart[path];
        var end = this.bitStart[moves.Paths[path].End];
        this.Reaching(first, end, this.reached);
        MirDataflow.Apply(this.state, 1, this.Position(first), this.Position(first) + end - first, set: 0, clear: 1);
        foreach (var holder in this.reached)
        {
            this.Status(holder, -1);
        }

        for (var i = this.defStart[id]; i < this.defStart[id + 1]; i++)
        {
            this.Gain(this.defined[i]);
        }
    }

    // The holders of the set reaching bits [first, end), one subtree's or cell's, into a list, by words (Dense).
    private void Reaching(int first, int end, List<int> into)
    {
        into.Clear();
        var low = this.Position(first);
        var high = low + end - first;
        for (var w = low >> 6; low < high && w << 6 < high; w++, this.Dense++)
        {
            var bits = this.state[w] & (w == low >> 6 ? ulong.MaxValue << (low & 63) : ulong.MaxValue);
            for (bits &= w == (high - 1) >> 6 ? ulong.MaxValue >> (63 - ((high - 1) & 63)) : ulong.MaxValue; bits != 0; bits &= bits - 1)
            {
                into.Add(this.bitHolder[(w << 6) + System.Numerics.BitOperations.TrailingZeroCount(bits) - low + first]);
            }
        }
    }

    // The reaching holders of a cell into a list: those of its set reaching bits, or its only holder, which always reaches.
    private void Reached(int cell, List<int> into)
    {
        this.Reaching(this.bitStart[cell], this.bitStart[cell + 1], into);
        if (this.bitStart[cell] == this.bitStart[cell + 1] && this.holderStart[cell + 1] > this.holderStart[cell])
        {
            into.Add(this.holderStart[cell]);
        }
    }

    // A holder starts reaching: its bit is set (one without a bit always reaches).
    private void Gain(int holder)
    {
        if (this.Bit(holder) is var bit and >= 0 && !Test(this.state, this.Position(bit)))
        {
            this.state[this.Position(bit) >> 6] |= 1UL << this.Position(bit);
            this.Status(holder, 1);
        }
    }

    // Pass A on a block: the edges of each location (in the replay), then its reaching definitions.
    private void Reach(int block)
    {
        for (var i = 0; i <= this.body.Blocks[block].Count; i++)
        {
            if (this.generating)
            {
                this.Generate(new(block, i));
            }

            this.Define(this.Id(new(block, i)));
        }
    }

    // Pass A's transfer: the reaching definitions, and in the replay the flow graph.
    private readonly struct Definitions(MirBorrows owner) : IMirFlow
    {
        public void Entry() => owner.Enter();

        public void Block(int block) => owner.Reach(block);
    }
}
