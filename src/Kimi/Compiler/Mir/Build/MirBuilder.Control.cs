// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Control flow (SPEC 14): selections, iterations, do, require and transfers. A transfer branches at run time to its target and, for the
// checks only, to the code after it (CheckTarget, SPEC 14.10.3, plan 5.6.3); where a body's continuation ends is its HIR Completion.
internal sealed partial class MirBuilder
{
    private readonly List<Target> targets = []; // The constructs around the code being built that a transfer may target, innermost last.

    // Whether a node's evaluation can complete normally (its HIR Completion).
    private static bool Completes(Koto node) => HirTables.CompletionOf(node) != HirCompletion.Diverges;

    // SPEC 14.7.2: the conditions in order, the first true clause's body into the destination, else the else body or, omitted, Unit.
    private void If(MirPlace destination, IfKoto conditional)
    {
        var after = this.Open(conditional, destination);
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var (then, next) = (this.body.AddBlock(), this.body.AddBlock());
            var test = this.Test(conditional.Branches[i].Condition, next, then);
            this.Enter(then, test);
            this.Arm(destination, conditional.Branches[i].Body, after);
            this.Enter(next, test);
        }

        if (conditional.ElseBody is { } other)
        {
            this.Arm(destination, other, after);
        }
        else
        {
            this.Unit(destination, conditional);
            this.body.Terminate(this.current, MirTerminator.Goto(after, this.Info(conditional)));
        }

        this.Close(after);
    }

    // SPEC 14.6.3: the body repeats from the head, as a continue does, and only an exit completes the loop. Where no exit completes it,
    // the code after it is checked from the head (plan 5.6.3).
    private void Loop(MirPlace destination, LoopKoto loop)
    {
        var (head, start) = (this.body.AddBlock(), this.body.AddBlock());
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, head);
        this.current = start;
        this.Iterate(loop.Body, head);
        var exits = this.Close(after) && Completes(loop);
        this.body.Terminate(head, MirTerminator.Goto(start, this.Info(loop), exits ? -1 : after));
    }

    // SPEC 14.6.1: the condition before each iteration; a false one completes the while with Unit, as an exit does.
    private void While(MirPlace destination, WhileKoto loop)
    {
        var (head, start, done) = (this.body.AddBlock(), this.body.AddBlock(), this.body.AddBlock());
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, head);
        this.current = head;
        var test = this.Test(loop.Condition, done, start);
        this.Enter(start, test);
        this.Iterate(loop.Body, head);
        this.Enter(done, test);
        this.Unit(destination, loop);
        this.body.Terminate(done, MirTerminator.Goto(after, this.Info(loop)));
        this.Close(after);
    }

    // SPEC 14.3.2: the body once into the destination, where a named exit to the do places its value too.
    private void Do(MirPlace destination, DoKoto scoped)
    {
        var after = this.Open(scoped, destination);
        this.Arm(destination, scoped.Body, after);
        this.Close(after);
    }

    // SPEC 14.11: the failure body runs where the condition is false and has no successor, at run time or for the checks (SPEC 14.10.3).
    private void Require(RequireKoto require)
    {
        var (fail, pass) = (this.body.AddBlock(), this.body.AddBlock());
        var test = this.Test(require.Condition, fail, pass);
        this.Enter(fail, test);
        this.Statement(require.ElseBody);
        this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(require)));
        this.Enter(pass, test);
    }

    // SPEC 14.5, 16.2.2, 15.6.7: a transfer evaluates and secures its operand, ends the borrows of the calls it abandons (its operands
    // pending since its target began), then branches: at run time to its result's placement and on through its target's drop tree to the
    // target its plan row names (a function's return block, built at its first return), for the checks to the code after it. An operand
    // that does not complete leaves no transfer.
    private void Transfer(MirPlace destination, JumpKoto jump)
    {
        var (plan, index) = (HirTables.PlanOf(jump).Plan, this.targets.Count - 1);
        while (index >= 0 && !ReferenceEquals(this.targets[index].Node, plan))
        {
            index--;
        }

        if (index < 0)
        {
            this.Unsupported(jump, destination); // A target not built yet: a for, a match or a defer.
            return;
        }

        var (operand, continues) = (jump.Expression, jump is ContinueKoto);
        var value = continues ? default : operand is null ? this.UnitOperand() : this.Operand(operand, this.body.TypeOf(this.targets[index].Destination));
        if (operand is not null && !Completes(operand))
        {
            return;
        }

        value = continues ? value : this.Secure(value, operand ?? jump);
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[index];
        for (var i = target.Mark; i < this.arguments.Count; i++)
        {
            var pending = this.arguments[i].Place;
            if (this.arguments[i].Kind == MirOperandKind.Move && this.IsTemp(pending) && MirTypes.IsReference(this.body.TypeOf(pending)))
            {
                this.body.Push(this.current, MirStatement.Of(MirStatementKind.EndBorrow, pending, this.Info(jump)));
            }
        }

        if (target.Break < 0)
        {
            this.body.Terminate(target.Break = this.body.AddBlock(), MirTerminator.Return(new(SourceRef.Of(this.declaration), 0, false)));
        }

        target.Reached |= !continues;
        var (info, place) = (this.Info(jump), target.Destination);
        var entry = this.Exit(target.Depth, continues ? target.Continue : target.Break);
        if (!continues)
        {
            var placement = this.body.AddBlock();
            this.body.Push(placement, MirStatement.Assign(place, MirRvalue.Use(this.body.AddOperand(value)), info with { StatementStart = false }));
            this.body.Terminate(placement, MirTerminator.Goto(entry, info with { StatementStart = false }));
            entry = placement;
        }

        var next = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(entry, info, next));
        this.current = next;
    }

    // SPEC 16.2.2: a result secured before its transfer branches: a constant or a temp as it is, else acquired into a temp.
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

    // SPEC 14.2.3, 3.6.2: a condition evaluated once into a bool, its temporaries cleaned up, then the branch on the bool, whose storage
    // ends in each successor (Enter). A match guard's candidates (SPEC 14.8.3) are shared borrows of the Places its Pattern selected among
    // its temporaries, pending so that a transfer ends them, and the Subject is read after their cleanup: it stays protected until then.
    private MirPlace Test(Koto condition, int whenFalse, int whenTrue, BoundMatch? plan = null, int pattern = 0, int start = 0)
    {
        var tested = this.OpenScope(false);
        var test = this.Temp(BoundType.Boolean, condition);
        var (temporaries, mark) = (this.OpenScope(false), this.arguments.Count);
        for (var i = pattern; plan is not null && i < plan.Positions[pattern].End; i++)
        {
            if (plan.Positions[i] is { CandidateSymbol: { Type: { } type } candidate, Source: var source })
            {
                var reference = this.Temp(type, source, MirLocalRole.GuardCandidate, candidate.Name);
                this.Set(reference, MirRvalue.Ref(MirRefKind.Shared, this.Follow(this.selected[start + i], 16, type.Components[0]), type), source);
                this.arguments.Add(MirOperand.Move(reference));
                this.locals[candidate] = reference.Root;
            }
        }

        this.Assign(test, condition);
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        this.CloseScope(temporaries);
        if (plan is not null)
        {
            this.body.Push(this.current, MirStatement.Of(MirStatementKind.FakeRead, this.selected[start + pattern], this.Info(condition)));
        }

        this.Branch(test, condition, whenFalse, whenTrue);
        this.CloseScope(tested, false);
        return test;
    }

    // The branch on a tested bool; a condition that does not complete has no value, and the checks continue into both successors.
    private void Branch(MirPlace test, Koto condition, int whenFalse, int whenTrue)
    {
        var info = this.Info(condition);
        this.body.Terminate(this.current, Completes(condition) ? MirTerminator.SwitchInt(MirOperand.Copy(test), this.body.AddCase(0, whenFalse), 1, whenTrue, info) : MirTerminator.Goto(whenTrue, info, whenFalse));
    }

    // A body whose value its construct takes, then the join after the construct.
    private void Arm(MirPlace destination, Koto body, int after)
    {
        this.Result(destination, body, body);
        this.Join(after, body);
    }

    // An iteration: the body for its effect, then back to the head, through the cleanup of the iteration's scopes as a continue goes.
    private void Iterate(Koto body, int head)
    {
        this.Statement(body);
        this.Join(this.Exit(this.targets[^1].Depth, head), body);
    }

    // The end of a body: a runtime edge to the block where the body completes, else only its check-only continuation ends there (plan
    // 5.6.3: the body's Completion decides).
    private void Join(int block, Koto body)
    {
        var info = this.Info(body);
        this.body.Terminate(this.current, Completes(body) ? MirTerminator.Goto(block, info) : MirTerminator.Unreachable(info, block));
    }

    // Opens a construct a transfer may target, with the block after it, where its exits go.
    private int Open(Koto node, MirPlace destination, int next = -1)
    {
        var after = this.body.AddBlock();
        this.targets.Add(new(node, destination, after, next, this.arguments.Count, this.scopes.Count, false));
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

    // A construct a transfer may target (SPEC 14.5.2): where its result goes, the block after it (-1: the function's return block, not built
    // yet), its next iteration's block, the pending operands below its transfers, the open scopes around it, and whether an exit or a
    // yield reached it.
    private record struct Target(Koto Node, MirPlace Destination, int Break, int Continue, int Mark, int Depth, bool Reached);
}
