// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Block items (SPEC 14.2, 6.5, 3.6): a statement for its effect, its temporaries ending with it, a block's scope and its result, and a
// local's declaration, each as its Op and Adjust decide; an explicit discard Binding marks (HirTables.DestroysInPlace) destroys its raw
// Place in place, transferring nothing (SPEC 14.2.4, 5.2.3).
internal sealed partial class MirBuilder
{
    // A block item for its effect, its temporaries ending with it (SPEC 3.6): a local's declaration (also one Binding rejected, Let), a
    // form that leaves no value, or a value into a temp; a declaration builds nothing here.
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
            case HirOp.Error when HirTables.Symbol(item) is { Kind: BindingSymbolKind.Local, Type: not null } local &&
                ReferenceEquals(local.Declaration, item):
                this.Let((VariableKoto)item); // A declaration Binding rejected, whose own local symbol it typed, declares its local too.
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
                this.Evaluated(item);
                break;
        }

        this.CloseScope(temporaries);
        this.statementStart = false;
    }

    // A statement's value, built into a temp of its own among the statement's temporaries (SPEC 3.6), complete where it is made.
    private void Evaluated(Koto item)
    {
        var value = this.Hold(Held(item), item);
        this.Into(value, item);
        this.Made(value);
    }

    // SPEC 14.2.4: bare acquisition or transfer of a discarded operand into a temp destroyed with the statement's temporaries. A discard
    // that destroys in place (HirTables.DestroysInPlace: a raw Place's Take, SPEC 5.2.3) transfers nothing: the node it takes, the operand
    // or a group's operand (Into's descent), designates its Place, at the address a raw borrow forms, position checks included (SPEC 5.2,
    // 4.7.4), and the value is destroyed in that Place at statement temporary cleanup, before the statement's temporaries, which is here, as
    // nothing of the statement follows its operand: by a Drop where its Type may need drop, as every Drop is built (Created).
    private void Discard(DiscardKoto discard)
    {
        if (!HirTables.DestroysInPlace(discard))
        {
            var value = this.Hold(Held(discard.Operand), discard.Operand);
            this.Assign(value, discard.Operand);
            this.Made(value);
            return;
        }

        var taken = discard.Operand;
        while (HirTables.Op(taken) == HirOp.Group && InPlace(HirTables.AdjustOf(taken)))
        {
            taken = Child(taken);
        }

        var place = this.Place(taken);
        if (this.body.TypeOf(place) is not { } type)
        {
            this.Record(taken, DiagnosticCode.InternalInvariant_Kd); // A Take Binding allowed designates a Place of its Type.
            return;
        }

        // The designation's carrier is this dead address temp, as MIR has no statement that only evaluates a Place; this site moves to
        // such a statement if one is added (its removal condition).
        var pointer = compilation.Types.Pointer(type);
        this.Set(this.Temp(pointer, taken), this.Ref(place, pointer, input: false), taken);
        if (MirTypes.MayNeedDrop(type, this.walked))
        {
            this.DropAt(place, this.Info(taken));
        }
    }

    // The value a body or a block's last item leaves in the destination: the item as its consumer takes it, a block's own result, or the
    // container's Unit after the item as a statement where the container completes (SPEC 7.1, 14.2). As a call's input that reserves
    // (input; MirCases.Reserves), which several bodies and exits write, the value goes through a temp of its own, moved there where the
    // container completes, as an exit's secured value is, so no Ref writes the input (A5's lent, G136; DECISIONS R5-M72). Whether the
    // body ends in its value rather than its end arrival (SPEC 14.9.1), which its construct's End takes.
    private bool Result(MirPlace destination, Koto item, Koto container, bool input)
    {
        if (input)
        {
            var value = this.Hold(this.body.TypeOf(destination)!, item);
            var held = this.Result(value, item, container, input: false);
            this.Made(value);
            if (Completes(container))
            {
                this.Use(destination, MirOperand.Move(value), item);
            }

            return held;
        }

        this.statementStart = true;
        var adjust = HirTables.AdjustOf(item);
        bool valued;
        if (HirTables.Op(item) == HirOp.Block && (adjust.Acquire == HirAcquire.Unset || InPlace(adjust)))
        {
            valued = this.CodeBlock(destination, (CodeBlockKoto)item); // As Assign and Into build it.
        }
        else if (adjust.Acquire != HirAcquire.Unset)
        {
            this.Assign(destination, item);
            valued = true;
        }
        else
        {
            this.Statement(item);
            this.Leave(destination, container);
            valued = false;
        }

        if (ReferenceEquals(item, this.constructed) && HirTables.Op(item) != HirOp.Block && Completes(item))
        {
            this.RequestExit(this.current, this.Info(this.declaration, local: true)); // An expression body completes; a block body's block.
        }

        return valued;
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

    // A block a route reaches as a value (Result, Into): the body its innermost target ends is that target's (TargetBlock, EndingTarget),
    // any other a block of its own (Block). Whether it ends in its last item's value.
    private bool CodeBlock(MirPlace destination, CodeBlockKoto block)
        => this.EndingTarget(block) >= 0 ? this.TargetBlock(destination, block) : this.Block(destination, block);

    // SPEC 14.2.1, 16.2: a Body no target ends (an unsafe body, SPEC 14.3; a require failure body, SPEC 14.11), its items cleaned up
    // where it completes. A target body reaching it breaks the builder's contract (TargetBlock builds those).
    private bool Block(MirPlace destination, CodeBlockKoto block, MirScopeKind kind = MirScopeKind.Lexical)
    {
        if (this.EndingTarget(block) >= 0)
        {
            this.Record(block, DiagnosticCode.InternalInvariant_Kd);
        }

        var valued = this.Last(destination, block, this.Lead(block, kind));
        this.CloseScope(this.lexical);
        return valued;
    }

    // SPEC 16.2: a target body that is a block (TargetBody's, an iteration's, a defer body), its scope left open for its owner's end to
    // leave (End). Where its route gives it the target's delivery place (Target.Ending), its last item goes to the delivery that item's
    // Completion takes, taken before it is built (after a departing exit, the Destination is a result temp); a temp of its route keeps
    // the delivery TargetBody took, as the route places the value from it.
    private bool TargetBlock(MirPlace destination, CodeBlockKoto block, MirScopeKind kind = MirScopeKind.Lexical)
    {
        var outer = this.Lead(block, kind);
        ref var target = ref CollectionsMarshal.AsSpan(this.targets)[^1];
        if (destination == target.Ending.Place)
        {
            target.Ending = target.Delivery(Completes(block.Items.Count > 0 ? block.Items[^1] : block));
            destination = target.Ending.Place;
        }

        return this.Last(destination, block, outer);
    }

    // A block's items before its last one, in its MIR scope and its scope of locals and defers, which its builder leaves (Block,
    // TargetBlock); the MIR scope around it.
    private int Lead(CodeBlockKoto block, MirScopeKind kind)
    {
        var outer = this.scope;
        this.scope = this.body.AddScope(outer, kind, SourceRef.Of(block));
        this.OpenScope(block: true);
        for (var i = 0; i < block.Items.Count - 1; i++)
        {
            this.Statement(block.Items[i]);
        }

        return outer;
    }

    // SPEC 14.2, 16.2: a block's last item, its result, or its container's Unit; the locals and defers it registers are cleaned up when
    // it is left. Its last item's temporaries are those of the Body it is (SPEC 3.6, 16.2.2 step 3): an arm's, an if clause's or a do's,
    // the function's, else the enclosing statement's. Whether it ends in that item's value; the MIR scope around it restored.
    private bool Last(MirPlace destination, CodeBlockKoto block, int outer)
    {
        var valued = false;
        if (block.Items.Count > 0)
        {
            valued = this.Result(destination, block.Items[^1], block, input: false);
        }
        else
        {
            this.Leave(destination, block);
        }

        if (ReferenceEquals(block, this.constructed) && Completes(block))
        {
            this.RequestExit(this.current, this.Info(this.declaration, local: true)); // A constructor's body completes before its cleanup.
        }

        this.scope = outer;
        return valued;
    }

    // SPEC 6.6: a local of its symbol's Type, visible after its declaration, initialized as its initializer's Adjust takes it. A
    // declaration Binding rejected, whose symbol it typed, declares its local too, so every use of the name is that one storage (SPEC
    // 3.6), and its initializer, which the HIR replaced, gives the declaration's derived value (SPEC 23.1.2; DECISIONS R4-M125), whose
    // site names no earlier build's local of the name (a defer body is built at each exit).
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
        var initializer = variable.InitializerKoto;
        if (initializer is not null && HirTables.Op(variable) == HirOp.Error)
        {
            this.locals.Remove(symbol); // The name its site's syntax declares is bound to no earlier build's local (a defer body, at each exit).
            this.Opaque(variable, new(local));
        }
        else if (initializer is not null)
        {
            this.Assign(new(local), initializer);
        }

        this.locals[symbol] = local;
    }
}
