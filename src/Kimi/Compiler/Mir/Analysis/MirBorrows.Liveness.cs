// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler.Mir;

// A5-1's liveness (SPEC 15.6.1; DECISIONS R4-M24) on MirMoveData's path numbers, backward over all edges (MirDataflow.Backward); a root
// without a cell is skipped. A use makes live what its Place covers (the precision rule: the subtree, or through a reference the nearest
// path's own cell); a whole write, StorageLive, StorageDead and EndBorrow end what they cover; a weak write through an index does neither;
// a Drop whose destruction may observe an Origin (MirTypes.Observes, SPEC 15.6.6) uses what A2 keeps of its Place under the view, and one only
// the checks reach nothing (SPEC 14.9, 14.10.3: a continuation destroys nothing; the edges are MirDataflow.Exit's). Then one
// backward walk of each chain from its exit records the events pass C replays: each cell whose liveness differs before and after a location.
internal sealed partial class MirBorrows
{
    private readonly List<(int First, int End)> kills = [];
    private readonly List<(int First, int End)> uses = [];
    private int[] eventId = []; // The events, each chain's from its tail: a location, a cell and its liveness after the location.
    private int[] eventCell = [];
    private bool[] eventAfter = [];
    private int[] eventStart = []; // By head: its chain's events.
    private int[] eventEnd = [];
    private int[] pathMarks = []; // By path: the location recorded last, plus 1.
    private int eventCount;
    private int cursor; // Pass C: the events of the chain advanced, from cursor down to cursorEnd.
    private int cursorEnd;
    private bool recording;

    // Pass B: liveness backward over all edges, then one backward walk of each chain from its exit recording its events.
    private void Liveness()
    {
        var transfer = new Living(this);
        flow.Backward(ref transfer);
        (this.recording, this.eventCount) = (true, 0);
        MirDataflow.Grow(ref this.eventStart, this.body.Blocks.Length);
        MirDataflow.Grow(ref this.eventEnd, this.body.Blocks.Length);
        MirDataflow.Grow(ref this.pathMarks, moves.Paths.Length);
        Array.Clear(this.pathMarks, 0, moves.Paths.Length);
        for (var b = 0; b < this.body.Blocks.Length; b++)
        {
            if (flow.Previous(b) >= 0)
            {
                continue;
            }

            var head = flow.Head(b);
            this.eventStart[head] = this.eventCount;
            for (var block = flow.Exit(head); block >= 0; block = flow.Previous(block))
            {
                this.Live(block);
            }

            this.eventEnd[head] = this.eventCount;
        }

        this.recording = false;
    }

    private void Live(int block)
    {
        for (var i = this.body.Blocks[block].Count; i >= 0; i--)
        {
            this.Live(new MirLocation(block, i));
        }
    }

    // A location backward: the paths live before it are those live after it that it does not end, and those it uses; recording, each cell
    // whose liveness differs is an event.
    private void Live(MirLocation at)
    {
        this.kills.Clear();
        this.uses.Clear();
        var visitor = new Uses(this, at);
        MirUses.ForEach(cases, at, ref visitor);
        var first = this.eventCount;
        foreach (var (start, end) in this.kills)
        {
            this.Candidates(this.Id(at), start, end);
        }

        foreach (var (start, end) in this.uses)
        {
            this.Candidates(this.Id(at), start, end);
        }

        foreach (var (start, end) in this.kills)
        {
            MirDataflow.Apply(this.state, 1, start, end, set: 0, clear: 1);
        }

        foreach (var (start, end) in this.uses)
        {
            MirDataflow.Apply(this.state, 1, start, end, set: 1, clear: 0);
        }

        var kept = first;
        for (var e = first; e < this.eventCount; e++)
        {
            if (Test(this.state, this.eventCell[e]) != this.eventAfter[e])
            {
                (this.eventId[kept], this.eventCell[kept], this.eventAfter[kept++]) = (this.eventId[e], this.eventCell[e], this.eventAfter[e]);
            }
        }

        this.eventCount = kept;
    }

    // Recording, the cells of the paths [start, end) a location touches, each once with its liveness after the location.
    private void Candidates(int id, int start, int end)
    {
        for (var path = start; this.recording && path < end; path++)
        {
            if (this.holderStart[path + 1] > this.holderStart[path] && this.pathMarks[path] != id + 1)
            {
                this.pathMarks[path] = id + 1;
                MirDataflow.Grow(ref this.eventId, this.eventCount + 1);
                MirDataflow.Grow(ref this.eventCell, this.eventCount + 1);
                MirDataflow.Grow(ref this.eventAfter, this.eventCount + 1);
                (this.eventId[this.eventCount], this.eventCell[this.eventCount]) = (id, path);
                this.eventAfter[this.eventCount++] = Test(this.state, path);
            }
        }
    }

    // How a location uses a Place, by its access (MirUses): a whole write of it, StorageLive, StorageDead and EndBorrow end what it covers; a
    // read, Move or borrow, and any access through a reference, use it; a write through an index neither; a Drop is Dropped. A static or a
    // root without a cell has none.
    private void Use(MirPlace place, MirAccess access, int block)
    {
        if (place.IsStatic || !this.carrying[place.Root])
        {
            return;
        }

        var path = this.Cover(place, out var coverage);
        var end = coverage == Coverage.Own ? path + 1 : moves.Paths[path].End;
        if ((access & MirAccess.Kills) != 0 || ((access & MirAccess.Defines) != 0 && coverage == Coverage.Whole))
        {
            this.kills.Add((path, end));
        }
        else if ((access & MirAccess.DropUse) != 0)
        {
            this.Dropped(place, path, end, block);
        }
        else if ((access & (MirAccess.Reads | MirAccess.Moves | MirAccess.Borrows)) != 0 || coverage == Coverage.Own)
        {
            this.uses.Add((path, end));
        }
    }

    // A Drop uses what A2 keeps of its Place under the view where its destruction may observe an Origin (SPEC 15.6.6, plan Appendix B X1;
    // R4-M6a): an Elide row nothing, an Open row its own cell and then its child rows by their verdicts (each row's path, A2's), any other
    // verdict what it covers; a Drop without a group, which only the checks reach, nothing (SPEC 14.9, plan 16-4).
    private void Dropped(MirPlace place, int path, int end, int block)
    {
        if (!MirTypes.Observes(this.TypeOf(place)))
        {
            return;
        }

        var rows = this.drops.RowsOf(this.body.Ordinal);
        var group = this.drops.Group(this.body.Ordinal, block, this.view);
        for (var row = group; group >= 0 && row < rows.Length && (row == group || rows[row].Depth > 0); row++)
        {
            var at = rows[row].Path < 0 ? path : rows[row].Path; // The dropped Place itself at Depth 0: what it covers.
            var covered = rows[row].Path < 0 ? end : moves.Paths[at].End;
            if (rows[row].Verdict != MirDropVerdict.Elide)
            {
                this.uses.Add((at, rows[row].Verdict == MirDropVerdict.Open ? at + 1 : covered));
            }
        }
    }

    // Pass B's transfer: liveness backward.
    private readonly struct Living(MirBorrows owner) : IMirBlocks
    {
        public void Block(int block) => owner.Live(block);
    }

    // A location's uses, for liveness.
    private readonly struct Uses(MirBorrows owner, MirLocation at) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind) => owner.Use(place, access, at.Block);

        public void Effect(MirEffect effects, int site)
        {
        }
    }
}
