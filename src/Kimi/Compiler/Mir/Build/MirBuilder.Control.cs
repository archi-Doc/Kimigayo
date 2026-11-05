// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Control flow (SPEC 14): selections, iterations, do and require, their conditions, the bodies whose values they take and the joins
// after them; where a body's continuation ends is its HIR Completion, and where a construct's non-value end goes on is whether it is
// a Never form (SPEC 14.10.3). Transfers, their targets and the joins' edges are MirBuilder.Transfers.cs's.
internal sealed partial class MirBuilder
{
    // Whether a node's evaluation can complete normally (its HIR Completion).
    private static bool Completes(Koto node) => HirTables.CompletionOf(node) != HirCompletion.Diverges;

    // SPEC 14.10.3: a Never form, an expression of Type Never, which does not complete (its HIR Completion and Binding's Type); a node
    // with no Completion row (a Pattern binding) is none.
    private static bool IsNeverForm(Koto node)
        => HirTables.CompletionOf(node) == HirCompletion.Diverges && ReferenceEquals(TypeOf(node), BoundType.Never);

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
            this.Arm(conditional.Branches[i].Body, input);
            this.Enter(next, test);
        }

        if (conditional.ElseBody is { } other)
        {
            this.Arm(other, input);
        }
        else
        {
            this.Implicit(destination, after, conditional, conditional.Branches[^1].Condition);
        }

        this.Close(after);
    }

    // SPEC 14.6.3: the body repeats from the head, as a continue does, and only an exit completes the loop. The head is its non-value
    // end: the code after a loop that is a Never form, one no exit completes, is checked from the head (plan 5.6.3).
    private void Loop(MirPlace destination, LoopKoto loop)
    {
        var head = this.body.AddBlock();
        var start = this.body.AddBlock();
        this.body.Terminate(this.current, MirTerminator.Goto(head, this.Info(loop)));
        var after = this.Open(loop, destination, head);
        this.current = start;
        this.Iterate(loop.Body);
        var check = this.Close(after);
        this.body.Terminate(head, MirTerminator.Goto(start, this.Info(loop), check));
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
        this.Iterate(loop.Body);
        this.Enter(done, test);
        this.Implicit(destination, after, loop, loop.Condition);
        this.Close(after);
    }

    // SPEC 14.3: the body once into the destination, where a named exit to the do places its value too; as a call's input, the body's
    // value as Result places it (input).
    private void Do(MirPlace destination, DoKoto scoped, bool input)
    {
        var after = this.Open(scoped, destination);
        this.Arm(scoped.Body, input);
        this.Close(after);
    }

    // SPEC 14.11: the failure body runs where the condition is false and has no successor, at run time or for the checks (SPEC 14.10.3);
    // its row says no runtime path from its entry may reach its end (SPEC 14.11), which A3 checks. The failure body, a single item or a
    // block, is the scope its defers register in (SPEC 14.2, 16.1), cleaned up before its end, as a function's or a match arm's body is.
    // Its row names the nodes Binding guessed its completion rests on (DECISIONS R5-M186), asked only where it can complete: its HIR
    // Completion is the guess rule's own structural completion, which rests on no guess where it cannot complete.
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
        var start = this.body.GuessedSites.Length;
        foreach (var guessed in Completes(require.ElseBody) ? compilation.Binding.GuessedCompletionNodes(require.ElseBody, require.ElseBody) : default)
        {
            this.body.AddGuessedSite(SourceRef.Of(guessed));
        }

        this.body.AddMustNotComplete(new(fail, this.current, SourceRef.Of(require.ElseBody), start, this.body.GuessedSites.Length - start));
        this.Enter(pass, test);
    }

    // SPEC 14.2.3, 3.6: evaluate a condition once, clean its temporaries, then branch; Enter ends the bool's storage in each successor.
    private MirPlace Test(Koto condition, int whenFalse, int whenTrue)
    {
        var tested = this.OpenScope(block: false);
        var test = this.Hold(BoundType.Boolean, condition);
        var temporaries = this.OpenScope(block: false);
        var mark = this.arguments.Count;
        this.Assign(test, condition);
        this.arguments.RemoveRange(mark, this.arguments.Count - mark);
        this.CloseScope(temporaries);
        this.Made(test);
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

    // An if clause's or a do's body whose value its construct takes, as a call's input as Result places it (input), in a scope of its
    // own, as a match arm's: the body is its own Body, so the temporaries of its result end with it (SPEC 14.2.1, 16.2.2 step 3); then its
    // end, which leaves that scope (End).
    private void Arm(Koto body, bool input)
    {
        var scope = this.temporaries = this.OpenScope(block: true);
        this.End(body, this.TargetBody(body, input));
        this.Pop(scope);
    }

    // SPEC 14.2.1, 16.2: a body its innermost target ends and takes the value of (an if clause's or else body, a do's, a match arm's or a
    // function's; an iteration's is a statement, Iterate), set as that target's Body while it is built: its value placed as Result
    // places it, at the delivery its Completion takes (Target.Ending), and a block of it, on any route Result takes (its own, a call
    // input's temp, or the temp its Adjust is acquired from, SPEC 16.2.2), built as the target's (TargetBlock, EndingTarget). Whether it
    // ends in its value.
    private bool TargetBody(Koto body, bool input)
    {
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[^1];
        (target.Body, target.Ending) = (body, target.Delivery(Completes(body)));
        var valued = this.Result(target.Ending.Place, body, body, input);
        this.SetBody(null);
        return valued;
    }

    // Sets the body the innermost target's owner is building, none after it (Target.Body).
    private void SetBody(Koto? body) => CollectionsMarshal.AsSpan(this.targets)[^1].Body = body;

    // SPEC 16.2, 14.9.1, 14.10.3 (DECISIONS R4-M176): a target body's end, after its value (a value end) or its end arrival, leaves the
    // scopes the target opened around it through the drop tree of where it goes on, as a transfer of the same kind does (Inline): an
    // iteration's to its next iteration (Continue); one that completes, or a value end, to its delivery (Target.Ending; the edge Joins),
    // a Never value for the checks only (R4-M62); an end arrival that does not complete to the target's Check for the checks only. Where
    // it goes on to no block, it is cleaned up inline: a function's end that completes before a return built its return block (Reach)
    // returns there, and any other is a non-value end its owner ends (Close; a function's, Body). The scopes stay open for the body's
    // owner to remove (Pop).
    private void End(Koto body, bool value)
    {
        var target = this.targets[^1];
        var joins = target.Continue < 0 && (Completes(body) || value);
        var next = target.Continue >= 0 ? target.Continue : joins ? target.Ending.Continuation : target.Check;
        var entry = this.Inline(target.At.Depth, next);
        if (next < 0 && Completes(body))
        {
            this.body.Terminate(this.current, MirTerminator.Return(this.Info(body)));
        }
        else if (next < 0)
        {
            this.ends.Add((this.current, this.Info(body)));
        }
        else if (joins)
        {
            this.Joins(this.Edge(entry, body));
        }
        else
        {
            this.Join(entry, body);
        }
    }

    // SPEC 14.9.1: a construct's implicit completion with Unit (an omitted else, a while's false path), at its source, after the
    // condition that leads there: where that completes, the Unit placed and the join, else a non-value end that Close ends.
    private void Implicit(MirPlace destination, int after, Koto construct, Koto condition)
    {
        if (!Completes(condition))
        {
            this.ends.Add((this.current, this.Info(construct)));
            return;
        }

        this.Unit(destination, construct);
        this.Joins(MirTerminator.Goto(after, this.Info(construct)));
    }

    // An iteration: the body for its effect, a statement's value (Evaluated) set as its target's Body, then its end to the next iteration
    // (End). Its statement's temporaries are a scope of its own, not Statement's, as its end leaves them through the tree.
    private void Iterate(Koto body)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(block: false);
        this.SetBody(body);
        this.Evaluated(body);
        this.SetBody(null);
        this.statementStart = false;
        this.End(body, value: false);
        this.Pop(temporaries);
    }

    // The end of a body: a runtime edge to the block where the body completes, else only its check-only continuation ends there (plan
    // 5.6.3: the body's Completion decides).
    private void Join(int block, Koto body) => this.body.Terminate(this.current, this.Edge(block, body));

    // That edge (Join).
    private MirTerminator Edge(int block, Koto body)
    {
        var info = this.Info(body);
        return Completes(body) ? MirTerminator.Goto(block, info) : MirTerminator.Unreachable(info, block);
    }
}
