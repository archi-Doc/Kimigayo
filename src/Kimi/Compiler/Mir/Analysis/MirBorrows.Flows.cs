// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// A5-1's flow graph (SPEC 15.6.1, 15.6.3, 15.6.4; DECISIONS R4-M24): edges only from actual value flows. A side is the holders a Place covers
// (the precision rule): the reaching holders of each cell of it, a cell's def 0 where none reaches, or a static's escape run; a whole write's
// destination is the holders it defines, any other write's the holders its Place covers. Each relation of TyCtxt.Relate connects the source
// variables holding its longer end to the destination variables holding its shorter end, through one junction where both are several (N3);
// an end no variable of its side holds is counted (Unmapped, N2) unless it is the static Origin, a bound that holds no Loan. A borrow
// relates its Place into the reference's referent and its Loan into the reference's outer Origin, each parent's outer Origin into the Loan up
// to the first shared layer; a call with a signature (MirTypes.Parameter) its arguments into its instantiated parameters, their
// well-formedness, the callee's clauses (TyCtxt.Clauses) and its result into the destination; a call without one every argument into the
// destination and into every variable behind a writable layer of every argument.
internal sealed partial class MirBorrows
{
    private readonly List<(BoundOrigin? Longer, BoundOrigin? Shorter, bool Equal)> relations = [];
    private readonly List<Entry> entries = []; // The sides of the location generated.
    private readonly List<int> sources = []; // Connect's scratch: the variables holding a relation's ends.
    private readonly List<int> sinks = [];
    private readonly List<int> reached = []; // The reaching holders of a cell (Reached).
    private BoundType?[] parameters = []; // The instantiated parameters of the call generated.

    // SPEC 15.6.2: a layer past which only shared access reaches.
    private static bool Shared(BoundType layer) => layer.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Rc or SemanticsKind.Arc;

    // The edges of a location: an Assign's rvalue (an Adapt as the view resolves it) into its Place, a Call's flows.
    private void Generate(MirLocation at)
    {
        this.entries.Clear();
        ref readonly var block = ref this.body.Blocks[at.Block];
        if (at.Index == block.Count)
        {
            if (block.Terminator.Kind == MirTerminatorKind.Call)
            {
                this.Call(at, block.Terminator);
            }

            return;
        }

        ref readonly var statement = ref this.body.Statements[block.Start + at.Index];
        if (statement.Kind != MirStatementKind.Assign)
        {
            return;
        }

        var rvalue = statement.Rvalue.Kind == MirRvalueKind.Adapt ? cases.Adapt(statement.Rvalue, out _) : statement.Rvalue;
        var type = this.TypeOf(statement.Place);
        var to = this.Destination(statement.Place, at);
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Use:
                this.Flow(at, this.body.Operands[rvalue.Start], type, to);
                break;
            case MirRvalueKind.Aggregate:
                for (var i = 0; i < rvalue.Count; i++)
                {
                    this.Flow(at, this.body.Operands[rvalue.Start + i], MirTypes.Component(this.body, rvalue, type, i), to);
                }

                break;
            case MirRvalueKind.Ref:
                this.Borrow(at, rvalue, type, to);
                break;
            case MirRvalueKind.Cast:
                this.Cast(rvalue, type, to);
                break;
        }
    }

    private BoundType? TypeOf(in MirOperand operand) => operand.Kind == MirOperandKind.Const ? this.body.Consts[operand.Arg].Type : this.TypeOf(operand.Place);

    // The holders a Place covers here: a static's run; else the reaching holders of each cell of what it covers, a cell's def 0 where none
    // reaches.
    private Side Covered(MirPlace place)
    {
        var first = this.entries.Count;
        if (place.IsStatic)
        {
            var table = moves.Paths.Length + ~place.Root;
            this.entries.Add(new(this.staticVar[~place.Root], this.atomStart[table], this.atomStart[table + 1]));
        }
        else if (this.carrying[place.Root])
        {
            var path = this.Cover(place, out var coverage);
            for (var cell = path; cell < (coverage == Coverage.Own ? path + 1 : moves.Paths[path].End); cell++)
            {
                this.Reached(cell, this.reached);
                if (this.reached.Count == 0 && this.holderStart[cell + 1] > this.holderStart[cell])
                {
                    this.reached.Add(this.holderStart[cell]);
                }

                foreach (var holder in this.reached)
                {
                    this.Hold(holder);
                }
            }
        }

        return new(first, this.entries.Count - first);
    }

    private void Hold(int holder)
    {
        var cell = this.holderCell[holder];
        this.entries.Add(new(this.holderVar[holder], this.atomStart[cell], this.atomStart[cell + 1]));
    }

    // Where a write puts its value: the holders a whole write defines, else the holders its Place covers (a weak write).
    private Side Destination(MirPlace place, MirLocation at)
    {
        var id = this.Id(at);
        if (this.replacedAt[id] < 0 || (this.replacedAt[id] & 1) == 0)
        {
            return this.Covered(place);
        }

        var first = this.entries.Count;
        for (var i = this.defStart[id]; i < this.defStart[id + 1]; i++)
        {
            this.Hold(this.defined[i]);
        }

        return new(first, this.entries.Count - first);
    }

    // SPEC 15.6.1: a value fit's relations (TyCtxt.Relate) from a side into a side.
    private void Relate(BoundType? actual, BoundType? expected, bool invariant, Side from, Side to)
    {
        this.relations.Clear();
        _ = TyCtxt.Relate(actual, expected, invariant, this.relations);
        this.Connect(from, to);
    }

    // Each relation from the variables holding its longer end to those holding its shorter end, both ways where equal, through one junction
    // where both are several (N3).
    private void Connect(Side from, Side to)
    {
        foreach (var (longer, shorter, equal) in this.relations)
        {
            this.Holding(longer, from, this.sources);
            this.Holding(shorter, to, this.sinks);
            var junction = this.sources.Count > 1 && this.sinks.Count > 1 ? this.Variables(1, -1) : -1;
            foreach (var source in this.sources)
            {
                for (var t = 0; t < (junction < 0 ? this.sinks.Count : 1); t++)
                {
                    this.Edges(source, junction < 0 ? this.sinks[t] : junction, equal);
                }
            }

            for (var t = 0; junction >= 0 && t < this.sinks.Count; t++)
            {
                this.Edges(junction, this.sinks[t], equal);
            }
        }
    }

    private void Edges(int from, int to, bool equal)
    {
        this.Edge(from, to);
        if (equal)
        {
            this.Edge(to, from);
        }
    }

    // The variables of a side holding an atom; none is counted (N2) unless the atom is the static Origin.
    private void Holding(BoundOrigin? origin, Side side, List<int> into)
    {
        into.Clear();
        for (var e = side.First; e < side.First + side.Count; e++)
        {
            var entry = this.entries[e];
            if (this.Find(origin, entry.Start, entry.End) is var i and >= 0)
            {
                into.Add(entry.Variable + i - entry.Start);
            }
        }

        this.Unmapped += into.Count == 0 && !ReferenceEquals(origin, BoundOrigin.Static) ? 1 : 0;
    }

    // A Loan's variable into the variables of a side holding an atom, the outer Origin of the reference holding it.
    private void Into(int loan, BoundOrigin? origin, Side to)
    {
        this.Holding(origin, to, this.sinks);
        foreach (var sink in this.sinks)
        {
            this.Edge(loan, sink);
        }
    }

    // SPEC 15.6.1, 8.14: an operand into a destination of a Type; a PairAcquire the view makes a Reborrow (MirCases.Reborrows) is also a Loan
    // on its Place's referent, into the destination's outer Origin.
    private void Flow(MirLocation at, in MirOperand operand, BoundType? expected, Side to)
    {
        if (operand.Kind == MirOperandKind.Const)
        {
            return;
        }

        this.Relate(this.TypeOf(operand.Place), expected, invariant: false, this.Covered(operand.Place), to);
        if (this.Reborrow(at, operand) is var loan and >= 0)
        {
            this.Into(loan, expected?.Origin, to);
        }
    }

    // SPEC 15.6, 15.6.3, 5.4: a borrow: the Place's atoms into the reference's referent (equal but for a shared borrow) and, but for an
    // address, a Loan into its outer Origin, held through its parents.
    private void Borrow(MirLocation at, in MirRvalue borrow, BoundType type, Side to)
    {
        var kind = (MirRefKind)borrow.Op;
        var referent = type.Components.Count == 1 ? type.Components[0] : null;
        this.Relate(this.TypeOf(borrow.Place), referent, invariant: kind != MirRefKind.Shared, this.Covered(borrow.Place), to);
        if (kind != MirRefKind.Raw)
        {
            var loan = this.Loan(at, borrow.Place, kind, referent: false);
            this.Into(loan, type.Origin, to);
            this.Parents(loan, borrow.Place);
        }
    }

    // A PairAcquire's Reborrow, a Loan on its Place's referent through the Place's own parent first; -1 for any other operand.
    private int Reborrow(MirLocation at, in MirOperand operand)
    {
        if (operand.Kind != MirOperandKind.PairAcquire || !cases.Reborrows(operand))
        {
            return -1;
        }

        var loan = this.Loan(at, operand.Place, MirRefKind.Uniq, referent: true);
        if (this.Parent(loan, operand.Place))
        {
            this.Parents(loan, operand.Place);
        }

        return loan;
    }

    // SPEC 15.6.3: a borrow through references is contained in each parent's Origin, the nearest first, up to and including the first shared
    // layer; a step following a reference under the view (MirCases.Follows) has its prefix as a parent; a raw pointer adds none.
    private void Parents(int loan, MirPlace place)
    {
        var projections = this.body.Projections;
        for (var id = place.Projection; id > 0; id = projections[id].Prefix)
        {
            if (cases.Follows(place with { Projection = id }) && !this.Parent(loan, place with { Projection = projections[id].Prefix }))
            {
                return;
            }
        }
    }

    // The edges from a reference's holders' outer Origin into a Loan through it; whether the walk goes on (a layer that is not shared).
    private bool Parent(int loan, MirPlace reference)
    {
        var type = this.TypeOf(reference);
        if (type.Origin is not null || MirTypes.IsReference(type))
        {
            this.Holding(type.Origin, this.Covered(reference), this.sources);
            foreach (var source in this.sources)
            {
                this.Edge(source, loan);
            }
        }

        return !Shared(type);
    }

    // SPEC 13.6, 5.4, 10.2: a Cast: an Upcast relates the outer Origins and the source payload's base to the target's payload, equal for a
    // handle invariant in its target (N1, TyCtxt.InvariantTarget); a numeric Cast carries nothing; any other every source variable into every
    // destination's.
    private void Cast(in MirRvalue cast, BoundType type, Side to)
    {
        var operand = this.body.Operands[cast.Start];
        var kind = (MirCastKind)cast.Op;
        if (operand.Kind == MirOperandKind.Const || kind is not (MirCastKind.Upcast or MirCastKind.Pointer or MirCastKind.IntToPointer or MirCastKind.Erasure))
        {
            return;
        }

        var from = this.Covered(operand.Place);
        if (kind != MirCastKind.Upcast)
        {
            var junction = this.Variables(1, -1);
            this.Gather(from, junction);
            this.Scatter(junction, to, writable: false);
            return;
        }

        var source = this.TypeOf(operand.Place);
        var payload = source.Components.Count == 1 ? source.Components[0] : null;
        while (payload is not null && !MirTypes.Agree(payload, type.Components[0]))
        {
            payload = AdtDef.Base(payload);
        }

        this.relations.Clear();
        if (source.Origin is not null || MirTypes.IsReference(source))
        {
            this.relations.Add((source.Origin, type.Origin, false));
        }

        _ = TyCtxt.Relate(payload, type.Components[0], invariant: TyCtxt.InvariantTarget(type, null), this.relations);
        this.Connect(from, to);
    }

    // Every variable of a side into a junction.
    private void Gather(Side side, int junction)
    {
        for (var e = side.First; e < side.First + side.Count; e++)
        {
            for (var i = 0; i < this.entries[e].End - this.entries[e].Start; i++)
            {
                this.Edge(this.entries[e].Variable + i, junction);
            }
        }
    }

    // A junction flows into every variable of a side, or only into those behind a writable layer.
    private void Scatter(int junction, Side side, bool writable)
    {
        for (var e = side.First; e < side.First + side.Count; e++)
        {
            var entry = this.entries[e];
            for (var i = entry.Start; i < entry.End; i++)
            {
                if (!writable || (this.layers[i] & Writable) != 0)
                {
                    this.Edge(junction, entry.Variable + i - entry.Start);
                }
            }
        }
    }

    // SPEC 15.6.4: a Call. With a signature (MirTypes.Parameter, case-mapped), fresh variables for the distinct atoms of its instantiated
    // parameters and result (step 1) with their well-formedness (3); each argument into its parameter (2); a caller-side Loan on an Arg's
    // referent for each of the Arg's outer Origins the signature names and no argument supplies (6); the callee's substituted clauses (3,
    // TyCtxt.Clauses, a function item's by its own substitution); the result into the destination (4, 5).
    private void Call(MirLocation at, in MirTerminator call)
    {
        var to = call.Target >= 0 ? this.Destination(call.Place, at) : default;
        var value = call.Callee.Kind == MirCalleeKind.Value ? this.TypeOf(call.Operand) : null;
        var result = MirTypes.Parameter(types, call.Callee, value, -1, out var count);
        if (count < 0)
        {
            this.Spread(at, call, to);
            return;
        }

        var side = this.Signature(call, value, count, result);
        var supplied = this.edgeCount;
        for (var i = 0; i < count; i++)
        {
            this.Flow(at, this.body.Operands[call.Start + i], this.parameters[i], side);
        }

        this.Retain(at, side, supplied);
        this.relations.Clear();
        if (call.Callee.Symbol is { } function)
        {
            types.Clauses(function, call.Callee.Subst, this.relations);
        }
        else if (MirTypes.Terminal(value) is { Kind: BoundTypeKind.FunctionItem, Symbol: { } item } callee)
        {
            types.Clauses(item, types.SubstOfItem(callee), this.relations);
        }

        this.Connect(side, side);
        if (call.Target >= 0 && result is not null)
        {
            this.Relate(cases.Type(result), this.TypeOf(call.Place), invariant: false, side, to);
        }
    }

    // The call's table and its variables: the distinct atoms of its case-mapped instantiated parameters and result, with their meet edges
    // and, inside each borrow layer, an edge from each atom into the layer's Origin (SPEC 15.6.1, 15.6.4 step 3, well-formedness).
    private Side Signature(in MirTerminator call, BoundType? value, int count, BoundType? result)
    {
        MirDataflow.Grow(ref this.parameters, count);
        var start = this.atoms.Count;
        var meets = this.meets.Count;
        this.inside.Clear();
        for (var i = 0; i < count; i++)
        {
            this.parameters[i] = MirTypes.Parameter(types, call.Callee, value, i, out _) is { } parameter ? cases.Type(parameter) : null;
            this.Positions(this.parameters[i], start, 0, -1);
        }

        this.Positions(result is null ? null : cases.Type(result), start, 0, -1);
        var first = this.Variables(this.atoms.Count - start, -1);
        this.Meets(meets, this.meets.Count, first - start);
        foreach (var (entry, layer) in this.inside)
        {
            this.Edge(first + entry - start, first + layer - start);
        }

        this.entries.Add(new(first, start, this.atoms.Count));
        return new(this.entries.Count - 1, 1);
    }

    // SPEC 15.6.4 step 6: a call variable for an Arg's outer Origin that no argument supplied (no edge into it since the arguments began) is
    // a Loan on that Arg's referent, in the Arg's mode, which the result keeps while it depends on it.
    private void Retain(MirLocation at, Side side, int supplied)
    {
        var entry = this.entries[side.First];
        for (var j = entry.Start; j < entry.End; j++)
        {
            if (this.atoms[j] is not { } atom || this.Supplied(entry.Variable + j - entry.Start, supplied))
            {
                continue;
            }

            for (var a = 1; a < this.body.Locals.Length && this.body.Locals[a].Kind == MirLocalKind.Arg; a++)
            {
                var type = this.TypeOf(new MirPlace(a));
                if (MirTypes.IsReference(type) && ReferenceEquals(type.Origin, atom))
                {
                    this.Edge(this.Loan(at, new(a), MirTypes.RefKind(type, input: false), referent: true), entry.Variable + j - entry.Start);
                }
            }
        }
    }

    private bool Supplied(int variable, int from)
    {
        for (var e = from; e < this.edgeCount; e++)
        {
            if (this.edgeTo[e] == variable)
            {
                return true;
            }
        }

        return false;
    }

    // A call without a signature (an intrinsic, a callable value of an abstract Type): every argument, the callee value and a Reborrow's Loan
    // into the destination and into every variable behind a writable layer of every argument, through one junction; sound for any callee.
    private void Spread(MirLocation at, in MirTerminator call, Side to)
    {
        var junction = this.Variables(1, -1);
        var first = this.entries.Count;
        if (call.Callee.Kind == MirCalleeKind.Value && call.Operand.Kind != MirOperandKind.Const)
        {
            this.Gather(this.Covered(call.Operand.Place), junction);
        }

        for (var i = 0; i < call.Count; i++)
        {
            var operand = this.body.Operands[call.Start + i];
            if (operand.Kind != MirOperandKind.Const)
            {
                this.Gather(this.Covered(operand.Place), junction);
            }

            if (this.Reborrow(at, operand) is var loan and >= 0)
            {
                this.Edge(loan, junction);
            }
        }

        this.Scatter(junction, to, writable: false);
        this.Scatter(junction, new(first, this.entries.Count - first), writable: true);
    }

    // An entry of a side: a holder's (or a static's, or a call's) first variable and its atom table [Start, End).
    private readonly record struct Entry(int Variable, int Start, int End);

    // A side of a relation: Count entries from First.
    private readonly record struct Side(int First, int Count);
}
