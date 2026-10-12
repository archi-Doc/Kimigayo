// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A5-1, regions and Loans (SPEC 15.6; DECISIONS R4-M24), per body and Semantics case view.
// Input: a validated body, its order (MirDataflow.Begin), its move paths (MirMoveData.Build), the selected view (MirCases), A2's verdicts
// (MirDropTable, by MirBody.Ordinal) and TyCtxt's Relate and Clauses. Output: the Loans, OwnershipStorageLimit_Kd for a view over the
// arena limit, and per reached location the Loans active before it (Replay, for A5-2's checks and the tests).
// Holders are (definition, cell) (MirBorrows.Regions), statics runs of escape variables, and only actual value flows move Loans
// (MirBorrows.Flows). A Loan is active from its location while a holder that carries it is reaching and its cell live, or for good once it
// reaches caller storage or a static, until its borrowed storage ends (never through a reference). Liveness runs backward over all edges
// on path numbers (MirBorrows.Liveness); EndBorrow ends liveness only. Three bit-set passes per view (reaching definitions forward,
// liveness backward, active Loans forward with P1/P2), one depth-first walk per Loan between them; the active pass keeps each Loan's count
// of reaching and live holders exact by differences (a status change, a chain's head against the chain advanced last). Chain-local
// holders and Loans live in the scratch; the arenas are MirDataflow's, under its one limit (R4-M15a). A view no Loan can form in is not
// solved (N7). Work is Θ(size + holder pairs + Σ status changes × |carried| + heads × width/64), counted by Work (V16).
// Must not judge Origin relations (A5-3), decide conflicts (A5-2), read syntax, Koto or Binding's local regions, or copy the body per view.

#pragma warning disable SA1402 // The Loan record and the replay interface belong to the Loans.

namespace Kimi.Compiler.Mir;

/// <summary>A Loan (SPEC 15.6): where it is formed, the Place it borrows (its referent when <see cref="Referent"/>) and its kind. A Ref
/// forms one (but for an address), as do a PairAcquire the view makes a Reborrow, an Adapt the view makes a Ref and a call that keeps an
/// Arg's referent for its result (SPEC 15.6.4 step 6).</summary>
/// <param name="At">Its location.</param>
/// <param name="Place">The borrowed Place, or the reference whose referent it borrows.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Referent">Whether it borrows the referent of <see cref="Place"/>.</param>
internal readonly record struct MirLoan(MirLocation At, MirPlace Place, MirRefKind Kind, bool Referent);

/// <summary>A visitor of the Loans active before each location (<see cref="MirBorrows.Replay"/>).</summary>
internal interface IMirLoans
{
    /// <summary>The Loans active before a location.</summary>
    /// <param name="location">The location.</param>
    /// <param name="active">Bit i for Loan i of <see cref="MirBorrows.Loans"/>.</param>
    void At(MirLocation location, ReadOnlySpan<ulong> active);
}

/// <summary>Regions and Loans (A5-1, SPEC 15.6): <see cref="Run"/> analyzes a body under the selected view, <see cref="Replay"/> gives each
/// reached location the Loans active before it.</summary>
/// <param name="flow">The dataflow, begun with the body.</param>
/// <param name="moves">The move paths, built for the body.</param>
/// <param name="cases">The Semantics case views, a view of the body selected.</param>
/// <param name="types">The Type context: the callees' declared relations and callable signatures.</param>
internal sealed partial class MirBorrows(MirDataflow flow, MirMoveData moves, MirCases cases, TyCtxt types)
{
    private readonly List<int> queued = []; // Pass C: the Loans whose count reached 0 at the location advanced.
    private MirLoan[] loans = [];
    private MirLoan[] sorted = [];
    private int[] loanVar = [];
    private bool[] escaped = []; // By Loan: it reaches caller storage or a static.
    private int[] keys = []; // A table's keys and values (MirDataflow.Index); by Loan as generated, its group and storage bucket.
    private int[] values = [];
    private int[] order = []; // By Loan as generated: its number.
    private int[] pairLoan = []; // The holder relation, as (Loan, holder) pairs of the Loans that do not escape.
    private int[] pairHolder = [];
    private int[] edgeStart = []; // The flow graph by source variable.
    private int[] targets = [];
    private int[] carriedStart = []; // By holder: the Loans it carries.
    private int[] carried = [];
    private int[] bucketStart = []; // By group and storage path: its first Loan (R4-M24: flow then chain-local, each by the path borrowed).
    private int[] byOrder = []; // By number: the Loan as generated.
    private int[] startStart = []; // By location id: the Loans it forms.
    private int[] starts = [];
    private int[] marks = []; // By variable: the Loan walked last, plus 1.
    private int[] holderMarks = [];
    private int[] stack = [];
    private int[] counts = []; // Pass C: by Loan, its holders that are reaching and live.
    private ulong[] zero = []; // Pass C: the Loans that do not escape and have no such holder.
    private ulong[] seen = []; // Pass C: the flow holders' reaching bits at the exit of the chain advanced last.
    private ulong[] live = []; // The live paths before the location advanced (pass C); all clear in pass A.
    private ulong[] state = [];
    private MirBody body = null!;
    private MirDropTable drops = null!;
    private long view;
    private int loanCount;
    private int flowLoans;
    private int pairCount;
    private int width;
    private int loanBase; // The scratch: [flow holder bits | Loans | chain-local holder bits] from words 0, loanBase and chainBase.
    private int chainBase;
    private bool solved;

    /// <summary>Gets the Loans of the body and view analyzed: the flow Loans first, then the chain-local ones, each by the path they borrow.
    /// </summary>
    internal ReadOnlySpan<MirLoan> Loans => this.loans.AsSpan(0, this.loanCount);

    /// <summary>Gets the words a head stores in the arenas of the view analyzed: reaching holders and flow Loans forward, the flow paths'
    /// liveness backward.</summary>
    internal int StoredWords { get; private set; }

    /// <summary>Gets the work of the view analyzed (V16): the walks' variables and edges, the counts' changes and the liveness events.
    /// </summary>
    internal long Work { get; private set; }

    /// <summary>Gets the word work of the view analyzed (V16's dense terms): the heads' compared words, the storage ends' Loan words and the
    /// reaching-bit words read and cleared.</summary>
    internal long Dense { get; private set; }

    /// <summary>Gets the relation ends of the view analyzed that are no atom of their side and not the static Origin (N2); 0 by contract.
    /// </summary>
    internal int Unmapped { get; private set; }

    /// <summary>Analyzes a body under the selected view.</summary>
    /// <param name="body">The body.</param>
    /// <param name="view">The selected view.</param>
    /// <param name="drops">A2's verdicts of the program version.</param>
    /// <param name="report">The problems of the program version.</param>
    internal void Run(MirBody body, long view, MirDropTable drops, MirReport report)
    {
        (this.body, this.view, this.drops) = (body, view, drops);
        (this.loanCount, this.solved, this.Work, this.Dense, this.Unmapped) = (0, false, 0, 0, 0);
        if (!this.Sources())
        {
            return; // N7: no Loan can form.
        }

        this.Cells();
        this.Holders();
        this.Variables();
        if (!this.Generate() || (this.loanCount > 0 && !this.Solve()))
        {
            report.Add(new(DiagnosticCode.OwnershipStorageLimit_Kd, body.Source, 1UL << (int)view)); // R4-M15a: the view is not analyzed.
        }
    }

    /// <summary>Gives each reached location of the body and view analyzed the Loans active before it, chain by chain in reverse postorder;
    /// nothing for a view without a Loan or one over the limit.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="visitor">The visitor.</param>
    internal void Replay<TVisitor>(ref TVisitor visitor)
        where TVisitor : struct, IMirLoans
    {
        if (this.solved)
        {
            this.Reset();
            var active = new Active<TVisitor>(this, visitor);
            flow.Replay(ref active);
            visitor = active.Visitor;
        }
    }

    private static bool Test(ulong[] bits, int bit) => (bits[bit >> 6] & (1UL << bit)) != 0;

    private BoundType TypeOf(MirPlace place) => cases.Type(this.body.TypeOf(place)!);

    // N7: whether a Loan can form in the view: a borrow but an address, a PairAcquire the view makes a Reborrow, an Adapt the view makes a
    // borrow, or a reference Arg, whose referent a call's result may keep (SPEC 15.6.4 step 6).
    private bool Sources()
    {
        for (var a = 1; a < this.body.Locals.Length && this.body.Locals[a].Kind == MirLocalKind.Arg; a++)
        {
            if (MirTypes.IsReference(this.TypeOf(new MirPlace(a))))
            {
                return true;
            }
        }

        foreach (ref readonly var statement in this.body.Statements)
        {
            var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
            if (statement.Kind == MirStatementKind.Assign && rvalue.Kind == MirRvalueKind.Ref && (MirRefKind)rvalue.Op != MirRefKind.Raw)
            {
                return true;
            }
        }

        foreach (ref readonly var operand in this.body.Operands)
        {
            if (operand.Kind == MirOperandKind.PairAcquire && cases.Reborrows(operand))
            {
                return true;
            }
        }

        return false;
    }

    // A Loan and its variable.
    private int Loan(MirLocation at, MirPlace place, MirRefKind kind, bool referent)
    {
        MirDataflow.Grow(ref this.loans, this.loanCount + 1);
        MirDataflow.Grow(ref this.loanVar, this.loanCount + 1);
        this.loans[this.loanCount] = new(at, place, kind, referent);
        return this.loanVar[this.loanCount++] = this.Variables(1, -1);
    }

    // Sizes the arenas (R4-M15a): forward [flow holder bits | Loans | chain-local holder bits], of which a head stores the flow holders'
    // and the flow Loans' words; backward the paths, of which a head stores the flow paths'. False over the limit.
    private bool Prepare(int loanWords, int storedLoans)
    {
        var holders = (this.flowBits + 63) >> 6;
        var paths = (moves.Paths.Length + 63) >> 6;
        (this.loanBase, this.chainBase) = (holders, holders + loanWords);
        this.width = Math.Max(this.chainBase + ((this.bitCount - this.flowBits + 63) >> 6), paths);
        this.StoredWords = holders + storedLoans + ((moves.FlowCount + 63) >> 6);
        if (flow.Prepare(holders + storedLoans, this.width, (moves.FlowCount + 63) >> 6) is not { } scratch)
        {
            return false;
        }

        this.state = scratch;
        MirDataflow.Grow(ref this.live, paths);
        Array.Clear(this.live, 0, paths);
        return true;
    }

    // Pass A: the reaching definitions, then, in their replay, the flow graph. False over the limit.
    private bool Generate()
    {
        if (!this.Prepare(loanWords: 0, storedLoans: 0))
        {
            return false;
        }

        var reaching = new Definitions(this);
        flow.Forward(ref reaching);
        this.generating = true;
        flow.Replay(ref reaching);
        this.generating = false;
        return true;
    }

    // The holder relation, liveness (pass B) and the active Loans (pass C). False over the limit.
    private bool Solve()
    {
        this.Relation();
        if (!this.Prepare((this.loanCount + 63) >> 6, (this.flowLoans + 63) >> 6))
        {
            return false;
        }

        this.Liveness();
        var active = new Active<Solving>(this, default);
        flow.Forward(ref active);
        return this.solved = true;
    }

    // The holder relation: one depth-first walk per Loan over the flow graph gives the holders it reaches and whether it escapes; then the
    // Loans numbered (Order), the Loans each holder carries and the Loans each location forms.
    private void Relation()
    {
        MirDataflow.Index(this.edgeFrom.AsSpan(0, this.edgeCount), this.edgeTo.AsSpan(0, this.edgeCount), this.varCount, ref this.edgeStart, ref this.targets);
        MirDataflow.Grow(ref this.marks, this.varCount);
        MirDataflow.Grow(ref this.holderMarks, this.holderCount);
        MirDataflow.Grow(ref this.escaped, this.loanCount);
        MirDataflow.Grow(ref this.keys, this.loanCount);
        Array.Clear(this.marks, 0, this.varCount);
        Array.Clear(this.holderMarks, 0, this.holderCount);
        this.pairCount = 0;
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            this.Walk(loan);
        }

        this.Order();
        var holders = this.pairHolder.AsSpan(0, this.pairCount);
        MirDataflow.Index(holders, this.pairLoan.AsSpan(0, this.pairCount), this.holderCount, ref this.carriedStart, ref this.carried);
        MirDataflow.Grow(ref this.values, this.loanCount);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            (this.keys[loan], this.values[loan]) = (this.Id(this.loans[loan].At), loan);
        }

        var ids = this.body.Statements.Length + this.body.Blocks.Length;
        MirDataflow.Index(this.keys.AsSpan(0, this.loanCount), this.values.AsSpan(0, this.loanCount), ids, ref this.startStart, ref this.starts);
    }

    // One Loan's walk: each holder its variable reaches carries it, unless it reaches an escape, which holds it for good; its bucket is its
    // group, flow where it escapes or a flow cell holds it, and the path whose storage it borrows.
    private void Walk(int loan)
    {
        var epoch = loan + 1;
        var top = 0;
        var first = this.pairCount;
        var flowing = false;
        this.escaped[loan] = false;
        MirDataflow.Grow(ref this.stack, 1);
        this.stack[top++] = this.loanVar[loan];
        this.marks[this.loanVar[loan]] = epoch;
        while (top > 0)
        {
            var variable = this.stack[--top];
            this.Work++;
            this.escaped[loan] |= this.varEscape[variable];
            if (this.varHolder[variable] is var holder and >= 0 && this.holderMarks[holder] != epoch)
            {
                this.holderMarks[holder] = epoch;
                MirDataflow.Grow(ref this.pairLoan, this.pairCount + 1);
                MirDataflow.Grow(ref this.pairHolder, this.pairCount + 1);
                (this.pairLoan[this.pairCount], this.pairHolder[this.pairCount++]) = (loan, holder);
                flowing |= this.holderCell[holder] < moves.FlowCount;
            }

            for (var e = this.edgeStart[variable]; e < this.edgeStart[variable + 1]; e++, this.Work++)
            {
                if (this.marks[this.targets[e]] != epoch)
                {
                    this.marks[this.targets[e]] = epoch;
                    MirDataflow.Grow(ref this.stack, top + 1);
                    this.stack[top++] = this.targets[e];
                }
            }
        }

        this.pairCount = this.escaped[loan] ? first : this.pairCount; // An escaped Loan is counted by none.
        this.keys[loan] = (flowing || this.escaped[loan] ? 0 : moves.Paths.Length + 1) + this.Storage(this.loans[loan]);
    }

    // The path whose storage a Loan borrows, not through a reference (SPEC 15.6.2), or the paths' count for none: a referent's Loan, a
    // static's, or one through a reference or a raw pointer.
    private int Storage(in MirLoan loan)
    {
        if (loan.Referent || loan.Place.IsStatic)
        {
            return moves.Paths.Length;
        }

        var path = this.Cover(loan.Place, out var coverage);
        return coverage == Coverage.Own ? moves.Paths.Length : path;
    }

    // The Loans numbered by bucket (MirDataflow.Index, stable): flow first (they cross heads), then chain-local, each by its storage path,
    // so a subtree's storage end is one range per group.
    private void Order()
    {
        MirDataflow.Grow(ref this.values, this.loanCount);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            this.values[loan] = loan;
        }

        var buckets = 2 * (moves.Paths.Length + 1);
        MirDataflow.Index(this.keys.AsSpan(0, this.loanCount), this.values.AsSpan(0, this.loanCount), buckets, ref this.bucketStart, ref this.byOrder);
        this.flowLoans = this.bucketStart[moves.Paths.Length + 1];
        MirDataflow.Grow(ref this.order, this.loanCount);
        MirDataflow.Grow(ref this.sorted, this.loanCount);
        for (var number = 0; number < this.loanCount; number++)
        {
            var loan = this.byOrder[number];
            (this.order[loan], this.sorted[number], this.values[number]) = (number, this.loans[loan], this.escaped[loan] ? 1 : 0);
        }

        (this.loans, this.sorted) = (this.sorted, this.loans);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            this.escaped[loan] = this.values[loan] != 0;
        }

        for (var i = 0; i < this.pairCount; i++)
        {
            this.pairLoan[i] = this.order[this.pairLoan[i]];
        }
    }

    // Pass C starts with no holder reaching or live: every count 0, every Loan that does not escape in zero.
    private void Reset()
    {
        var words = (this.loanCount + 63) >> 6;
        MirDataflow.Grow(ref this.counts, this.loanCount);
        MirDataflow.Grow(ref this.zero, words);
        MirDataflow.Grow(ref this.seen, (this.flowBits + 63) >> 6);
        Array.Clear(this.counts, 0, this.loanCount);
        Array.Clear(this.zero, 0, words);
        Array.Clear(this.seen, 0, (this.flowBits + 63) >> 6);
        Array.Clear(this.live, 0, (moves.Paths.Length + 63) >> 6);
        for (var loan = 0; loan < this.loanCount; loan++)
        {
            this.zero[loan >> 6] |= this.escaped[loan] ? 0 : 1UL << loan;
        }
    }

    // Pass C on a block, the visitor seeing the Loans active before each location; a chain's head first takes the difference to the chain
    // advanced last, and its tail keeps its flow holders' reaching bits.
    private void Activate<TVisitor>(int block, ref TVisitor visitor)
        where TVisitor : struct, IMirLoans
    {
        if (flow.Previous(block) < 0)
        {
            this.Head(flow.Head(block));
        }

        for (var i = 0; i <= this.body.Blocks[block].Count; i++)
        {
            var at = new MirLocation(block, i);
            visitor.At(at, this.state.AsSpan(this.loanBase, (this.loanCount + 63) >> 6));
            this.Advance(at);
        }

        if (flow.Tail(block))
        {
            this.state.AsSpan(0, (this.flowBits + 63) >> 6).CopyTo(this.seen);
        }
    }

    // A chain's head (R4-M24): the chain-local Loans end; each flow holder whose reaching bit differs from the exit of the chain advanced
    // last, then each holder of a flow path whose liveness differs from that exit (live at the head's entry, MirDataflow.AtEntry), changes
    // its Loans' counts; the arriving Loans that no holder keeps end.
    private void Head(int head)
    {
        MirDataflow.Apply(this.state, 1, (this.loanBase << 6) + this.flowLoans, (this.loanBase << 6) + this.loanCount, set: 0, clear: 1);
        (this.cursor, this.cursorEnd) = (this.eventEnd[head], this.eventStart[head]);
        for (var w = 0; w < (this.flowBits + 63) >> 6; w++, this.Dense++)
        {
            for (var differ = this.seen[w] ^ this.state[w]; differ != 0; differ &= differ - 1)
            {
                var holder = this.bitHolder[(w << 6) + System.Numerics.BitOperations.TrailingZeroCount(differ)];
                this.Status(holder, (this.state[w] & differ & (~differ + 1)) != 0 ? 1 : -1);
            }

            this.seen[w] = this.state[w];
        }

        var entry = flow.AtEntry(head);
        for (var w = 0; w < entry.Length; w++, this.Dense++)
        {
            for (var differ = this.live[w] ^ entry[w]; differ != 0; differ &= differ - 1)
            {
                this.Liven((w << 6) + System.Numerics.BitOperations.TrailingZeroCount(differ), (entry[w] & differ & (~differ + 1)) != 0);
            }
        }

        for (var w = 0; w < (this.loanCount + 63) >> 6; w++)
        {
            this.state[this.loanBase + w] &= ~this.zero[w];
        }
    }

    // A location forward (R4-M24): the Loans on the storage it ends or replaces end; its reaching definitions change, then its liveness
    // events, each holder whose status (reaching and its cell live) changes changing its Loans' counts; a Loan whose count reached 0 ends
    // unless it escaped; then the Loans it forms start, ending at once where no holder keeps them.
    private void Advance(MirLocation at)
    {
        var id = this.Id(at);
        this.queued.Clear();
        if (this.replacedAt[id] >= 0)
        {
            var path = this.replacedAt[id] >> 1;
            this.EndStorage(path, moves.Paths[path].End);
        }

        this.Define(id);
        for (; this.cursor > this.cursorEnd && this.eventId[this.cursor - 1] == id; this.Work++)
        {
            this.cursor--;
            this.Liven(this.eventCell[this.cursor], this.eventAfter[this.cursor]);
        }

        foreach (var loan in this.queued)
        {
            this.state[this.loanBase + (loan >> 6)] &= ~(this.zero[loan >> 6] & (1UL << loan));
        }

        for (var i = this.startStart[id]; i < this.startStart[id + 1]; i++)
        {
            var loan = this.starts[i];
            this.state[this.loanBase + (loan >> 6)] |= ~this.zero[loan >> 6] & (1UL << loan);
        }
    }

    // The Loans on the storage of the paths [first, end) end, one range per group (Order).
    private void EndStorage(int first, int end)
    {
        var group = moves.Paths.Length + 1;
        for (var g = 0; g < 2 * group; g += group)
        {
            var from = this.bucketStart[g + first];
            var to = this.bucketStart[g + end];
            MirDataflow.Apply(this.state, 1, (this.loanBase << 6) + from, (this.loanBase << 6) + to, set: 0, clear: 1);
            this.Dense += (to - from + 63) >> 6;
        }
    }

    // A path's liveness changes: each reaching holder of it, a cell's, changes its status.
    private void Liven(int path, bool live)
    {
        this.live[path >> 6] ^= Test(this.live, path) == live ? 0 : 1UL << path;
        this.Reached(path, this.reached);
        foreach (var holder in this.reached)
        {
            this.Change(holder, live ? 1 : -1);
        }
    }

    // A holder's reaching status changes; its status, reaching and live, changes where its cell is live.
    private void Status(int holder, int delta)
    {
        if (Test(this.live, this.holderCell[holder]))
        {
            this.Change(holder, delta);
        }
    }

    // A holder's status changed: each Loan it carries gains or loses it; a Loan left with none joins zero and is queued (it ends at the
    // location's end unless it regains one there).
    private void Change(int holder, int delta)
    {
        for (var i = this.carriedStart[holder]; i < this.carriedStart[holder + 1]; i++, this.Work++)
        {
            var loan = this.carried[i];
            this.counts[loan] += delta;
            if (this.counts[loan] == 0)
            {
                this.zero[loan >> 6] |= 1UL << loan;
                this.queued.Add(loan);
            }
            else
            {
                this.zero[loan >> 6] &= ~(1UL << loan);
            }
        }
    }

    // The visitor of a solve, which sees nothing.
    private readonly struct Solving : IMirLoans
    {
        public void At(MirLocation location, ReadOnlySpan<ulong> active)
        {
        }
    }

    // Pass C's transfer: the active Loans, shown to a visitor before each location; each run starts from no reaching or live holder.
    private struct Active<TVisitor>(MirBorrows owner, TVisitor first) : IMirFlow
        where TVisitor : struct, IMirLoans
    {
        private TVisitor visitor = first;

        internal readonly TVisitor Visitor => this.visitor;

        public readonly void Entry()
        {
            owner.Reset();
            owner.Enter();
        }

        public void Block(int block) => owner.Activate(block, ref this.visitor);
    }
}
