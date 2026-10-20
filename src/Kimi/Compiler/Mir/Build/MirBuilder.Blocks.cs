// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Block items (SPEC 14.2, 6.5, 3.6): a statement for its effect, its temporaries ending with it, a block's scope and its result, and a
// local's declaration, each as its Op and Adjust decide.
internal sealed partial class MirBuilder
{
    // A block item for its effect, its temporaries ending with it (SPEC 3.6): a local's declaration, a form that leaves no value, or a
    // value into a temp; a declaration builds nothing here.
    private void Statement(Koto item)
    {
        this.statementStart = true;
        var temporaries = this.OpenScope(block: false);
        switch (HirTables.Op(item))
        {
            case HirOp.Syntax or HirOp.Body:
                break;
            case HirOp.Let:
                this.Let((VariableKoto)item);
                break;
            case HirOp.Require:
                this.Require((RequireKoto)item);
                break;
            case HirOp.Defer:
                this.Register(Cleanup.Defer, -1, item, this.lexical);
                break;
            case HirOp.Discard:
                this.Discard((DiscardKoto)item);
                break;
            case HirOp.Jump or HirOp.Continue:
                this.Transfer((JumpKoto)item);
                break;
            default:
                this.Into(this.Temp(item), item);
                break;
        }

        this.CloseScope(temporaries);
        this.statementStart = false;
    }

    // SPEC 14.2.4: bare acquisition of a discarded operand into a temp destroyed with the statement's temporaries.
    private void Discard(DiscardKoto discard) => this.Assign(this.Temp(discard.Operand), discard.Operand);

    // The value a body or a block's last item leaves in the destination: the item as its consumer takes it, a block's own result, or the
    // container's Unit after the item as a statement where the container completes (SPEC 7.1, 14.2). As a call's input that reserves
    // (input; MirCases.Reserves), which several bodies and exits write, the value goes through a temp of its own, moved there where the
    // container completes, as an exit's secured value is, so no Ref writes the input (A5's lent, G136; DECISIONS R5-M72).
    private void Result(MirPlace destination, Koto item, Koto container, bool input)
    {
        if (input)
        {
            var value = this.Temp(this.body.TypeOf(destination)!, item);
            this.Result(value, item, container, input: false);
            if (Completes(container))
            {
                this.Use(destination, MirOperand.Move(value), item);
            }

            return;
        }

        this.statementStart = true;
        if (HirTables.AdjustOf(item).Acquire != HirAcquire.Unset)
        {
            this.Assign(destination, item);
        }
        else if (HirTables.Op(item) == HirOp.Block)
        {
            this.Into(destination, item);
        }
        else
        {
            this.Statement(item);
            this.Leave(destination, container);
        }

        if (ReferenceEquals(item, this.constructed) && HirTables.Op(item) != HirOp.Block && Completes(item))
        {
            this.RequestExit(this.current, this.Info(this.declaration, local: true)); // An expression body completes; a block body's block.
        }
    }

    // SPEC 7.1, 14.2: the Unit a container leaves where it completes into a Unit destination; one completing into another Type falls
    // through, which Binding or control flow rejects (FunctionFallthrough_Kd), and leaves no value.
    private void Leave(MirPlace destination, Koto container)
    {
        if (HirTables.CompletionOf(container) == HirCompletion.Completes && ReferenceEquals(this.body.TypeOf(destination), BoundType.Unit))
        {
            this.Unit(destination, container);
        }
    }

    // SPEC 6.3.5: a constructor's exit request, its body's completion or a return, reads its object whole before the Scope Exit that
    // follows, so the analyses find its base and every own Field placed there: a defer cannot place one.
    private void RequestExit(int block, in SourceInfo info) => this.body.Push(block, MirStatement.Of(MirStatementKind.FakeRead, new(0), info));

    // SPEC 14.2, 16.2: a block's items in a scope of their own (an unsafe block's, SPEC 14.3), the last one its result; the locals and
    // defers it registers are cleaned up when it is left. Its last item's temporaries are its enclosing statement's (SPEC 3.6).
    private void Block(MirPlace destination, CodeBlockKoto block, MirScopeKind kind = MirScopeKind.Lexical)
    {
        var outer = this.scope;
        this.scope = (ushort)this.body.AddScope(outer, kind, SourceRef.Of(block));
        var lexical = this.OpenScope(block: true);
        var items = block.Items;
        for (var i = 0; i < items.Count - 1; i++)
        {
            this.Statement(items[i]);
        }

        if (items.Count > 0)
        {
            this.Result(destination, items[^1], block, input: false);
        }
        else
        {
            this.Leave(destination, block);
        }

        if (ReferenceEquals(block, this.constructed) && Completes(block))
        {
            this.RequestExit(this.current, this.Info(this.declaration, local: true)); // A constructor's body completes before its cleanup.
        }

        this.CloseScope(lexical);
        this.scope = outer;
    }

    // SPEC 6.5: a local of its symbol's Type, visible after its declaration, initialized as its initializer's Adjust takes it.
    private void Let(VariableKoto variable)
    {
        var symbol = HirTables.Symbol(variable)!;
        var local = this.Live(
            this.body.AddLocal(
                new(
                symbol.Type ?? BoundType.Unit,
                MirLocalKind.Var,
                this.Info(variable, local: true),
                Binding.Reassignable(symbol),
                symbol.Name)),
            this.lexical);
        if (variable.InitializerKoto is { } initializer)
        {
            this.Assign(new(local), initializer);
        }

        this.locals[symbol] = local;
    }
}
