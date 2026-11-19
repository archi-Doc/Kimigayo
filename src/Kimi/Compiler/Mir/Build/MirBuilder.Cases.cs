// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Case branches (SPEC 8.14, 8.15.2, 4.7.1 step 3, 4.7.4; R7 plan Z23, the owner's O-446 (a)): a user index whose selection is by case
// (HirTables.IndexCaseDecider) selects indexUniq in the views where its decider acquires exclusively and index in the others. A frame's
// owner is a standing take by case (Take): the first Mode-2 index of its Place whose decision splits the views of the side being built
// locates the index's receiver and key, ends the block in a CaseSwitch on the decider and builds the exclusive side; the take's end
// builds its Place again on the shared side, which reads the located receiver and key, into the same local, and both sides join. A
// Mode-2 index whose decision the side's views already fix takes that side's call with no frame, so frames open only where a decision
// splits a side's views and a node is built at most once per view region (at most MirCases.Count, PairBinder.CaseBound). An owner
// closes only the frame it opened; two decisions meeting in one owner's own chain are a broken contract (Binding publishes one
// decider per chain): InternalInvariant_Kd.
internal sealed partial class MirBuilder
{
    private readonly MirCases decisions = new(compilation.Types); // Decision's views (MirCases.Decision), per decider of the body (decided).
    private readonly Dictionary<BoundType, ulong> decided = new(ReferenceEqualityComparer.Instance); // A builder memo per body, never IR.
    private readonly List<int> owners = []; // The takes by case being built, innermost last: the frame each opened (-1: none).
    private readonly List<CaseFrame> frames = []; // The open frames, innermost last.

    // The views the side being built is reached in: every view of the body outside a frame.
    private ulong Views => this.frames.Count == 0 ? MirCases.Every(this.body) : this.frames[^1].Region;

    // SPEC 3.5, 8.14: a take, a frame's owner where it acquires by case (its Adjust's ByCase): its Place formed and acquired (Formed).
    // Where a Mode-2 index of its Place opened its frame, the exclusive side joins, the shared side forms the take again, reading only
    // the nodes located before the branch, and joins. A take into the place its consumer gave acquires into it on both sides; a take into
    // its own temp is moved, on each side, into one result temp both sides begin, so each reserving temp has its one write (MirValidator's
    // Lent row) and the join reads one value. The frame and its located nodes end.
    private MirOperand Take(Koto node, HirAdjust adjust, BoundType? expected, bool input, bool reserves, MirPlace? into)
    {
        if (adjust is not { Acquire: HirAcquire.ByCase, Adaptation: HirAdaptation.None })
        {
            return this.Formed(node, adjust, expected, input, reserves, into);
        }

        var owner = this.owners.Count;
        this.owners.Add(-1);
        var operand = this.Formed(node, adjust, expected, input, reserves, into);
        if (this.owners[owner] is not (>= 0 and var frame))
        {
            this.owners.RemoveAt(owner);
            return operand;
        }

        var (info, open) = (this.Info(node), this.frames[frame]);
        if (operand is not { Kind: MirOperandKind.Move, Place: { Projection: 0, Root: >= 0 } first })
        {
            // A take by case forms its acquisition in a local (Acquired): a broken contract; the shared side only joins.
            this.Record(node, DiagnosticCode.InternalInvariant_Kd);
            this.body.Terminate(this.current, MirTerminator.Goto(open.Join, info));
            this.body.Terminate(open.Shared, MirTerminator.Goto(open.Join, info));
            this.current = open.Join;
            CollectionsMarshal.SetCount(this.located, open.Located);
            this.frames.RemoveAt(frame);
            this.owners.RemoveAt(owner);
            return operand;
        }

        var given = into == first;
        var result = given ? first : this.Hold(this.body.Locals[first.Root].Type, node);
        if (!given)
        {
            this.Set(result, MirRvalue.Use(this.body.AddOperand(MirOperand.Move(first))), node);
        }

        this.body.Terminate(this.current, MirTerminator.Goto(open.Join, info));
        this.frames[frame] = open with { Region = open.Parent & ~open.Decision };
        (this.current, this.owners[owner]) = (open.Shared, -2); // -2: closing; no index of its chain opens a frame.
        CollectionsMarshal.SetCount(this.located, open.Side);
        if (this.IsTemp(result))
        {
            this.body.Push(this.current, MirStatement.Of(MirStatementKind.StorageLive, result, this.body.Locals[result.Root].Source));
        }

        var again = this.Formed(node, adjust, expected, input, reserves, given ? first : null);
        if (given ? again.Place != first : again.Kind != MirOperandKind.Move)
        {
            this.Record(node, DiagnosticCode.InternalInvariant_Kd); // The shared side's acquisition has the exclusive side's form.
        }
        else if (!given)
        {
            this.Set(result, MirRvalue.Use(this.body.AddOperand(again)), node);
        }

        this.body.Terminate(this.current, MirTerminator.Goto(open.Join, info));
        this.current = open.Join;
        CollectionsMarshal.SetCount(this.located, open.Located);
        this.frames.RemoveAt(frame);
        this.owners.RemoveAt(owner);
        return given ? operand : MirOperand.Move(this.Made(result));
    }

    // SPEC 4.7.1 step 3, 8.14, 4.7.4: the selected call of a user index by case under the side being built: index where the side's views
    // are outside its decider's decision, indexUniq where they are within it; else its receiver and key located first (a Mode-2 receiver
    // of the same decision may open the owner's frame there), then, where the views still split, the owner's frame opened (Open) and
    // indexUniq on its exclusive side. Two decisions in one owner's chain, or a Mode-2 index outside a take by case, break Binding's
    // contract (one decider per chain): InternalInvariant_Kd.
    private MirPlace Selected(Koto node, in ExpansionPlan plan, BoundType decider)
    {
        var decision = this.Decision(decider);
        if ((this.Views & decision) == 0)
        {
            return this.Shared(node, plan.Shared!);
        }

        if ((this.Views & ~decision) != 0)
        {
            var index = (IndexKoto)node;
            var mark = this.located.Count;
            this.located.Add((index.Left, this.Place(index.Left, input: true)));
            if (index.Right is { } key && HirTables.Op(key) != HirOp.Literal)
            {
                this.located.Add((key, InPlace(HirTables.AdjustOf(key)) ? this.Value(key) : this.Place(key)));
            }

            if ((this.Views & ~decision) != 0 && (this.owners.Count == 0 || this.owners[^1] != -1))
            {
                this.Record(node, DiagnosticCode.InternalInvariant_Kd);
            }
            else if ((this.Views & ~decision) != 0)
            {
                this.Open(node, decider, decision, mark);
            }
        }

        return this.Referent(node, plan.Second!);
    }

    // The index call under the shared side: its receiver taken by the receiver's plan-row Adjust where the two calls' inputs differ.
    private MirPlace Shared(Koto node, CallPlan call)
    {
        var receiver = ((IndexKoto)node).Left;
        var adjust = HirTables.PlanOf(receiver).Adjust;
        var parameter = call.ReceiverOperation.AdaptedType ?? call.ReceiverOperation.ParameterType;
        foreach (ref readonly var argument in call.ArgumentOperations)
        {
            parameter = ReferenceEquals(argument.Source, receiver) ? argument.AdaptedType ?? argument.ParameterType : parameter;
        }

        var value = this.Operand(receiver, adjust == default ? HirTables.AdjustOf(receiver) : adjust, parameter, input: true);
        var result = this.Hold(call.ReturnType, node);
        this.Call(result, call, node, first: null, receiver, value);
        return this.body.Project(compilation.Types, this.Made(result), MirProjectionKind.Deref, 0);
    }

    // The views in which a decider selects the exclusive side (MirCases.Decision), once per decider of the body.
    private ulong Decision(BoundType decider)
    {
        if (!this.decided.TryGetValue(decider, out var decision))
        {
            this.decided[decider] = decision = this.decisions.Decision(this.body, decider);
        }

        return decision;
    }

    // SPEC 8.14: the innermost owner's frame opened at an index whose receiver and key are located (from mark): the CaseSwitch on the
    // decider, the exclusive side being built.
    private void Open(Koto node, BoundType decider, ulong decision, int mark)
    {
        var region = this.Views;
        var (exclusive, shared, join) = (this.body.AddBlock(), this.body.AddBlock(), this.body.AddBlock());
        var operand = MirOperand.Const(this.body.Const(new(MirConstKind.TypeToken, decider)));
        this.body.Terminate(this.current, MirTerminator.CaseSwitch(operand, this.body.AddCase(1, exclusive), shared, this.Info(node)));
        this.frames.Add(new(decision, region, region & decision, shared, join, mark, this.located.Count));
        this.owners[^1] = this.frames.Count - 1;
        this.current = exclusive;
    }

    // A case branch's frame: its decision, the views of the side it was opened in (Parent) and of the side being built (Region), its
    // shared side's and join's blocks, and the located nodes' count before its own (Located) and after them (Side): a node a side
    // located is that side's.
    private readonly record struct CaseFrame(ulong Decision, ulong Parent, ulong Region, int Shared, int Join, int Located, int Side);
}
