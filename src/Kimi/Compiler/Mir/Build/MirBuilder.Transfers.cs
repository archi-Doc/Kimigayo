// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Transfers (SPEC 14.5, 16.2.2): a transfer branches at run time to its target and, for the checks only, to the code after it (CheckTarget,
// SPEC 14.10.3, plan 5.6.3), its result secured where it is evaluated (Secure); the constructs a transfer may target, opened and closed
// around their bodies.
internal sealed partial class MirBuilder
{
    private readonly List<Target> targets = []; // The constructs around the code being built that a transfer may target, innermost last.

    // SPEC 14.5, 16.2.2, 15.6.7: a transfer evaluates and secures its operand, then branches: at run time it ends the borrows of the calls
    // it abandons (its operands pending since its target began), places its result and goes on through its target's drop tree to the
    // target its plan row names (a function's return block, built at its first return); for the checks it goes on to the code after it,
    // where those calls continue with their reservations. An operand that does not complete leaves no transfer.
    private void Transfer(JumpKoto jump)
    {
        var plan = HirTables.PlanOf(jump).Plan;
        var operand = jump.Expression;
        if (plan is null)
        {
            // The published target row is null exactly where Binding rejected the jump (no valid target, or a #switch in its function part
            // selected no arm; DECISIONS R5-M31, R5-M42), so the jump is derived from that record (SPEC 23.1.2): its operand is built,
            // keeping its own records, and the jump is an Opaque value of what the operand gave, without a record.
            var given = operand is null ? default : this.Operand(operand, expected: null);
            this.roots.Clear();
            if (operand is not null && given.Kind != MirOperandKind.Const)
            {
                this.roots.Add(given.Place.Root);
            }

            this.Site(jump, this.Temp(jump), Completes(jump));
            return;
        }

        var index = this.targets.Count - 1;
        while (index >= 0 && !ReferenceEquals(this.targets[index].Node, plan))
        {
            index--;
        }

        if (index < 0)
        {
            // A published target no construct around the jump opened, though every target construct opens one: a broken HIR contract.
            this.program.Report(SourceRef.Of(jump), DiagnosticCode.InternalInvariant_Kd);
            this.Unsupported(jump, derived: true);
            return;
        }

        var continues = HirTables.Op(jump) == HirOp.Continue;
        MirOperand value = default;
        if (!continues)
        {
            value = operand is null ? this.UnitOperand() : this.Operand(operand, this.body.TypeOf(this.targets[index].Destination));
        }

        if (operand is not null && !Completes(operand))
        {
            return;
        }

        value = continues ? value : this.Secure(value, operand ?? jump);
        var info = this.Info(jump);
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[index];
        if (target.Break < 0)
        {
            this.body.Terminate(target.Break = this.body.AddBlock(), MirTerminator.Return(new(SourceRef.Of(this.declaration), 0, StatementStart: false)));
        }

        target.Reached |= !continues;
        var inner = info with { StatementStart = false };
        if (!continues && !target.Delivers && this.Departs(target.At.Depth))
        {
            // SPEC 16.2.2: the secured result is delivered after the Scope Exit of the scopes left: the target's Destination becomes a
            // result temp in the target's scope, with no storage of its own, and its Break the block that delivers that temp to the old
            // Destination, which every later transfer to it takes, as they share its drop tree (DECISIONS R5-M50).
            var at = new SourceInfo(SourceRef.Of(target.Node), target.At.Scope, StatementStart: false);
            var result = new MirPlace(this.body.AddLocal(new(this.body.TypeOf(target.Destination)!, MirLocalKind.Temp, at)));
            var deliver = this.body.AddBlock();
            this.body.Push(deliver, MirStatement.Assign(target.Destination, MirRvalue.Use(this.body.AddOperand(MirOperand.Move(result))), inner));
            this.body.Terminate(deliver, MirTerminator.Goto(target.Break, inner));
            (target.Destination, target.Break, target.Delivers) = (result, deliver, true);
        }

        var (place, mark) = (target.Destination, target.At.Mark);
        var entry = this.Exit(target.At.Depth, continues ? target.Continue : target.Break);
        var runtime = continues ? -1 : this.body.AddBlock(); // The runtime edge's first block: the abandoned borrows end, the result is placed.
        for (var i = mark; i < this.arguments.Count; i++)
        {
            var pending = this.arguments[i].Place;
            if (this.arguments[i].Kind == MirOperandKind.Move && this.IsTemp(pending) && MirTypes.IsReference(this.body.TypeOf(pending)))
            {
                runtime = runtime < 0 ? this.body.AddBlock() : runtime;
                this.body.Push(runtime, MirStatement.Of(MirStatementKind.EndBorrow, pending, inner));
            }
        }

        if (!continues)
        {
            this.body.Push(runtime, MirStatement.Assign(place, MirRvalue.Use(this.body.AddOperand(value)), inner));
            if (index == 0 && this.constructed is not null)
            {
                this.RequestExit(runtime, inner); // A constructor's return.
            }
        }

        if (runtime >= 0)
        {
            this.body.Terminate(runtime, MirTerminator.Goto(entry, inner));
            entry = runtime;
        }

        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(entry, info, next));
        this.current = next;
    }

    // SPEC 16.2.2, 12.2: a value secured where it is evaluated, a transfer's result before it branches and an operand before a later
    // operand of its operation builds code (Builds): a constant or a temp as it is, else acquired into a temp.
    private MirOperand Secure(in MirOperand value, Koto node)
    {
        if (value.Kind == MirOperandKind.Const || (value.Kind == MirOperandKind.Move && this.IsTemp(value.Place)))
        {
            return value;
        }

        var temp = this.Temp(this.TypeOf(value) ?? BoundType.Unit, node);
        this.Use(temp, value, node);
        return MirOperand.Move(temp);
    }

    private bool IsTemp(MirPlace place) => place is { Projection: 0, Root: >= 0 } && this.body.Locals[place.Root].Kind == MirLocalKind.Temp;

    // Opens a construct a transfer may target, with the block after it, where its exits go.
    private int Open(Koto node, MirPlace destination, int next = -1)
    {
        var after = this.body.AddBlock();
        this.targets.Add(new(node, destination, after, next, new(this.arguments.Count, this.scopes.Count, this.scope), Reached: false));
        return after;
    }

    // Closes the innermost construct, building on after it; whether an exit or a yield reached it.
    private bool Close(int after)
    {
        var reached = this.targets[^1].Reached;
        this.targets.RemoveAt(this.targets.Count - 1);
        this.current = after;
        return reached;
    }

    // Whether leaving the scopes from a depth cleans anything up: a departing scope registered a cleanup.
    private bool Departs(int depth)
    {
        for (var index = depth; index < this.scopes.Count; index++)
        {
            if (this.scopes[index].Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // A construct a transfer may target (SPEC 14.5.2): where its result goes, the block after it (-1: the function's return block, not built
    // yet), its next iteration's block, the builder's position where it opened, and whether an exit or a yield reached it.
    private record struct Target(Koto Node, MirPlace Destination, int Break, int Continue, Opening At, bool Reached)
    {
        // Whether its Destination and Break are a result temp and the block delivering it after a Scope Exit (SPEC 16.2.2).
        internal bool Delivers { get; set; }
    }

    // The builder's position where a construct opened: the pending operands below its transfers (Mark), the open scopes around it (Depth)
    // and the MIR scope it opened in, which its delivered result temp takes (SPEC 16.2.2).
    private readonly record struct Opening(int Mark, int Depth, ushort Scope);
}
