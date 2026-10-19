// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Default copies (plan 5.6.2, SPEC 7.2.3): where a call omits a default whose template is not recursive and did not fail (Defaults), its
// value is a copy of the template's MIR in the calling body, folded once under the call's substitution.
internal sealed partial class MirBuilder
{
    private readonly List<int> folded = []; // A copy's scopes, locals and blocks in this body.

    // Plan 5.6.2: a copy of a template's MIR in this body, folded once under the call's substitution: its scopes in a scope Inlined at the
    // call, its locals after this body's (its result the slot, its parameters the borrows from at), its entry block the current one (the
    // builder targets no entry block) and its returns going on after it; each Type applied and each callee's substitution composed
    // (TyCtxt.Compose), each Place projected again, so that a pair layer the substitution decides is its case's layer and a case-dependent
    // acquisition its case's (Acquire), each static and constant added again, and a Drop the instance's Type does not need left out. A Type
    // or callee the call does not instantiate is unsupported at the call. No MustNotComplete row is copied: a copy's verdict is its
    // template's, which A3 judges once (SPEC 7.2.3).
    private void Inline(MirBody template, Subst? subst, int at, MirPlace slot, Koto node)
    {
        var map = this.folded;
        var failed = false;
        var after = this.body.AddBlock();
        map.Clear();
        foreach (ref readonly var scope in template.Scopes)
        {
            map.Add(
                map.Count == 0 ? this.body.AddScope(this.scope, MirScopeKind.Inlined, SourceRef.Of(node)) : this.body.AddScope(
                map[scope.Outer],
                scope.Kind,
                scope.Source));
        }

        var locals = map.Count;
        for (var i = 0; i < template.Locals.Length; i++)
        {
            var local = template.Locals[i];
            int mapped;
            if (i == 0)
            {
                mapped = slot.Root;
            }
            else if (local.Kind == MirLocalKind.Arg)
            {
                mapped = this.arguments[at + i - 1].Place.Root;
            }
            else
            {
                mapped = this.body.AddLocal(local with { Type = FoldType(local.Type), Source = FoldInfo(local.Source) });
            }

            map.Add(mapped);
        }

        var blocks = map.Count;
        for (var i = 0; i < template.Blocks.Length; i++)
        {
            map.Add(i == 0 ? this.current : this.body.AddBlock());
        }

        for (var b = 0; b < template.Blocks.Length; b++)
        {
            var block = template.Blocks[b];
            var start = this.arguments.Count;
            this.current = map[blocks + b];
            foreach (ref readonly var statement in template.Statements.Slice(block.Start, block.Count))
            {
                var copy = statement with { Place = FoldPlace(statement.Place), Source = FoldInfo(statement.Source) };
                if (statement is { Kind: MirStatementKind.Assign, Rvalue: { Kind: MirRvalueKind.Use, Op: not 0 } acquire })
                {
                    // A PairAcquire, acquired into its local as the instance's case decides it, by the borrow its marker names.
                    var acquired = this.Acquire(
                        new(HirAcquire.ByCase, HirAdaptation.None),
                        FoldPlace(template.Operands[acquire.Start].Place),
                        null,
                        acquire.Op == (byte)MirRefKind.TwoPhaseUniq,
                        node,
                        copy.Place);
                    this.Use(copy.Place, acquired, node);
                    continue;
                }

                this.body.Push(this.current, statement.Kind == MirStatementKind.Assign ? copy with { Rvalue = FoldRvalue(statement.Rvalue) } : copy);
            }

            var terminator = block.Terminator;
            var call = terminator.Kind == MirTerminatorKind.Call;
            var operand = terminator.Kind == MirTerminatorKind.SwitchInt || terminator.Callee.Kind == MirCalleeKind.Value
                ? FoldOperand(terminator.Operand) : default;
            for (var i = 0; call && i < terminator.Count; i++)
            {
                var argument = template.Operands[terminator.Start + i]; // staticEnsure's operand is a static's index in its body.
                this.arguments.Add(
                    terminator.Callee is { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure }
                    ? MirOperand.Const(
                        this.body.Const(
                        new(
                        MirConstKind.Integer,
                        BoundType.ISize,
                        ~FoldPlace(new(~(int)template.Consts[argument.Arg].Bits)).Root))) : FoldOperand(argument));
            }

            var first = call ? this.Flush(start) : this.body.Cases.Length;
            for (var i = 0; terminator.Kind == MirTerminatorKind.SwitchInt && i < terminator.Count; i++)
            {
                this.body.AddCase(template.Cases[terminator.Start + i].Value, Target(template.Cases[terminator.Start + i].Target));
            }

            var composed = terminator.Callee.Subst is { } inner && subst is not null ? compilation.Types.Compose(inner, subst) : terminator.Callee.Subst;
            failed |= composed is null && terminator.Callee.Subst is not null;
            var place = call || terminator.Kind == MirTerminatorKind.Drop ? FoldPlace(terminator.Place) : default;
            var callee = terminator.Callee with { Subst = composed ?? terminator.Callee.Subst };
            if (callee.Kind == MirCalleeKind.Value)
            {
                callee = callee with { Arg = FoldOperand(MirOperand.Const(callee.Arg)).Arg }; // Its signature, a constant (C8).
            }

            var copied = terminator with
            {
                Source = FoldInfo(terminator.Source), Target = Target(terminator.Target), CheckTarget = Target(terminator.CheckTarget),
                Operand = operand, Start = first, Place = place, Callee = callee,
            };
            // The copy keeps a Drop where its instance's Type may need one (MirTypes.MayNeedDrop, the rule every Drop is built by).
            var kept = terminator.Kind != MirTerminatorKind.Drop || MirTypes.MayNeedDrop(this.body.TypeOf(place), this.walked);
            var continuation = copied;
            if (terminator.Kind == MirTerminatorKind.Return)
            {
                continuation = MirTerminator.Goto(after, copied.Source);
            }
            else if (!kept)
            {
                continuation = MirTerminator.Goto(copied.Target, copied.Source);
            }

            this.body.Terminate(this.current, continuation);
        }

        this.current = after;
        this.arguments.RemoveRange(at, this.arguments.Count - at);
        if (failed)
        {
            this.Unsupported(node, slot);
        }

        int Target(int block) => block < 0 ? block : map[blocks + block];

        SourceInfo FoldInfo(in SourceInfo info) => info with { Scope = (ushort)map[info.Scope] };

        BoundType FoldType(BoundType type)
        {
            var applied = subst is null ? type : compilation.Types.Apply(type, subst);
            failed |= applied is null;
            return applied ?? type;
        }

        MirPlace FoldPlace(MirPlace place)
        {
            if (place.Projection == 0)
            {
                return place.IsStatic ? this.body.Static(template.Statics[~place.Root]) : new(map[locals + place.Root]);
            }

            var row = template.Projections[place.Projection];
            var prefix = FoldPlace(place with { Projection = row.Prefix });
            return row.Kind switch
            {
                MirProjectionKind.PairLayer => TyCtxt.Layer(this.body.TypeOf(prefix), out _, out _) is var kind and not HirPlaceKind.Value
                    ? this.Layer(prefix, kind) : prefix,
                MirProjectionKind.Index => this.body.Project(prefix, row.Kind, map[locals + row.Arg]),
                _ => this.body.Project(prefix, row.Kind, row.Arg),
            };
        }

        MirOperand FoldOperand(in MirOperand operand) => operand.Kind == MirOperandKind.Const
            ? MirOperand.Const(this.body.Const(template.Consts[operand.Arg] with { Type = FoldType(template.Consts[operand.Arg].Type) }))
            : operand with { Place = FoldPlace(operand.Place) };

        // A template that did not fail has no Opaque value.
        MirRvalue FoldRvalue(in MirRvalue rvalue)
        {
            var start = this.arguments.Count;
            for (var i = 0; i < rvalue.Count; i++)
            {
                this.arguments.Add(FoldOperand(template.Operands[rvalue.Start + i]));
            }

            var place = rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Discriminant or MirRvalueKind.Len or MirRvalueKind.Adapt
                ? FoldPlace(rvalue.Place) : default;
            return rvalue with { Place = place, Start = this.Flush(start), Type = rvalue.Type is { } type ? FoldType(type) : null };
        }
    }
}
