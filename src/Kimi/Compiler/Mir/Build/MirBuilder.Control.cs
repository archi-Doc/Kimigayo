// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Control flow (SPEC 14): selections, iterations, do and require, their conditions, the bodies whose values they take and the joins
// after them; where a body's continuation ends is its HIR Completion. Transfers and their targets are MirBuilder.Transfers.cs's.
internal sealed partial class MirBuilder
{
    // Whether a node's evaluation can complete normally (its HIR Completion).
    private static bool Completes(Koto node) => HirTables.CompletionOf(node) != HirCompletion.Diverges;

    // SPEC 14.7: the conditions in order, the first true clause's body into the destination, else the else body or, omitted, Unit; as a
    // call's input, each body's value as Result places it (input).
    private void If(MirPlace destination, IfKoto conditional, bool input)
    {
        var after = this.Open(conditional, destination);
        for (var i = 0; i < conditional.Branches.Count; i++)
        {
            var then = this.body.AddBlock();
            var next = this.body.AddBlock();
            var test = this.Test(conditional.Branches[i].Condition, next, then);
            this.Enter(then, test);
            this.Arm(destination, conditional.Branches[i].Body, after, input);
            this.Enter(next, test);
        }

        if (conditional.ElseBody is { } other)
        {
            this.Arm(destination, other, after, input);
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
        var head = this.body.AddBlock();
        var start = this.body.AddBlock();
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
        var head = this.body.AddBlock();
        var start = this.body.AddBlock();
        var done = this.body.AddBlock();
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

    // SPEC 14.3: the body once into the destination, where a named exit to the do places its value too; as a call's input, the body's
    // value as Result places it (input).
    private void Do(MirPlace destination, DoKoto scoped, bool input)
    {
        var after = this.Open(scoped, destination);
        this.Arm(destination, scoped.Body, after, input);
        this.Close(after);
    }

    // SPEC 14.11: the failure body runs where the condition is false and has no successor, at run time or for the checks (SPEC 14.10.3);
    // its row says no runtime path from its entry may reach its end (SPEC 14.11), which A3 checks. The failure body, a single item or a
    // block, is the scope its defers register in (SPEC 14.2, 16.1), cleaned up before its end, as a function's or a match arm's body is.
    private void Require(RequireKoto require)
    {
        var fail = this.body.AddBlock();
        var pass = this.body.AddBlock();
        var test = this.Test(require.Condition, fail, pass);
        this.Enter(fail, test);
        var failure = this.OpenScope(block: true);
        this.Statement(require.ElseBody);
        this.CloseScope(failure);
        this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(require)));
        this.body.AddMustNotComplete(new(fail, this.current, SourceRef.Of(require.ElseBody)));
        this.Enter(pass, test);
    }

    // SPEC 14.2.3, 3.6: evaluate a condition once, clean its temporaries, then branch; Enter ends the bool's storage in each successor.
    private MirPlace Test(Koto condition, int whenFalse, int whenTrue)
    {
        var tested = this.OpenScope(block: false);
        var test = this.Temp(BoundType.Boolean, condition);
        var temporaries = this.OpenScope(block: false);
        var mark = this.arguments.Count;
        this.Assign(test, condition);
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        this.CloseScope(temporaries);
        this.Branch(test, condition, whenFalse, whenTrue);
        this.CloseScope(tested, clean: false);
        return test;
    }

    // The branch on a tested bool; a condition that does not complete has no value, and the checks continue into both successors.
    private void Branch(MirPlace test, Koto condition, int whenFalse, int whenTrue)
    {
        var info = this.Info(condition);
        var terminator = Completes(condition)
            ? MirTerminator.SwitchInt(MirOperand.Copy(test), this.body.AddCase(0, whenFalse), 1, whenTrue, info)
            : MirTerminator.Goto(whenTrue, info, whenFalse);
        this.body.Terminate(this.current, terminator);
    }

    // A body whose value its construct takes, as a call's input as Result places it (input), then the join after the construct.
    private void Arm(MirPlace destination, Koto body, int after, bool input)
    {
        this.Result(destination, body, body, input);
        this.Join(after, body);
    }

    // An iteration: the body for its effect, then back to the head, through the cleanup of the iteration's scopes as a continue goes.
    private void Iterate(Koto body, int head)
    {
        this.Statement(body);
        this.Join(this.Exit(this.targets[^1].At.Depth, head), body);
    }

    // The end of a body: a runtime edge to the block where the body completes, else only its check-only continuation ends there (plan
    // 5.6.3: the body's Completion decides).
    private void Join(int block, Koto body)
    {
        var info = this.Info(body);
        this.body.Terminate(this.current, Completes(body) ? MirTerminator.Goto(block, info) : MirTerminator.Unreachable(info, block));
    }
}
