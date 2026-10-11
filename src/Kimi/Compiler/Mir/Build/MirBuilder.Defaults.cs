// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Default templates (plan 5.6.2, SPEC 7.2.3). Each default of a declaration is one template body, built and validated like any generic body
// of its function (Body): its parameters p_i: ref/P_i reference the slots a call prepared for the earlier parameters, and its result is the
// parameter's value. A call that omits defaults acquires its explicit arguments into pending slots in source order, then evaluates each
// omitted default in parameter order into its slot: a copy of its template's MIR folded once under the call's substitution, in a scope
// Inlined at the call that records no interpretation context; or, where the template reaches itself through the defaults its calls omit (a
// component of the omission graph, found once by Tarjan's algorithm), a call of the template.
internal sealed partial class MirBuilder
{
    private const int ExpansionLimit = 65_536; // No copy takes a body past this many MIR statements (IMPL 21.3.5).
    private readonly Dictionary<DefaultDecl, int> visited = new(); // Each default's node in the omission graph.
    private readonly List<Omission> omissions = []; // The nodes in visit order.
    private readonly List<int> component = []; // The nodes whose component is open (Tarjan's stack).
    private readonly List<DefaultDecl> edges = []; // The defaults omitted in the defaults being visited, innermost last.
    private readonly List<int> folded = []; // A copy's scopes, locals and blocks in this body.
    private ulong prepared; // The prepared parameters the template being built reads (bit i: p_i; past 64, all).
    private Edges? walker;

    // Tarjan's algorithm over the omission graph: a default, then each default a call in it omits. A component is built where it closes,
    // after every component it reaches, so a copy finds its template built; a default in a component of more nodes than one, or omitted
    // in itself, is recursive.
    private int Visit(DefaultDecl decl)
    {
        var (index, start) = (this.omissions.Count, this.edges.Count);
        this.visited.Add(decl, index);
        this.omissions.Add(new(decl, index));
        this.component.Add(index);
        (this.walker ??= new(this)).Visit(decl.Expression);
        for (int i = start, end = this.edges.Count; i < end; i++)
        {
            var low = this.visited.TryGetValue(this.edges[i], out var next) ? (this.omissions[next].Open ? next : index) : this.omissions[this.Visit(this.edges[i])].Low;
            ref var node = ref CollectionsMarshal.AsSpan(this.omissions)[index];
            (node.Low, node.Recursive) = (Math.Min(node.Low, low), node.Recursive || this.edges[i] == decl);
        }

        this.edges.RemoveRange(start, this.edges.Count - start);
        if (this.omissions[index].Low == index)
        {
            var first = this.component.LastIndexOf(index);
            for (var i = first; i < this.component.Count; i++)
            {
                ref var member = ref CollectionsMarshal.AsSpan(this.omissions)[this.component[i]];
                (member.Open, member.Recursive) = (false, member.Recursive || this.component.Count - first > 1);
            }

            for (var i = first; i < this.component.Count; i++)
            {
                this.Template(this.component[i]);
            }

            this.component.RemoveRange(first, this.component.Count - first);
        }

        return index;
    }

    // A default's template, MirDefKey(function, DefaultTemplate, index); one with an Opaque value or a finding failed.
    private void Template(int index)
    {
        var (decl, findings) = (this.omissions[index].Decl, this.program.Findings.Count);
        if (HirTables.Symbol(decl.Function) is { } function && decl.Parameter(decl.Index) is { } type)
        {
            this.Body(new(function, MirBodyKind.DefaultTemplate, decl.Index), decl.Function, function, type, decl.Expression);
            ref var node = ref CollectionsMarshal.AsSpan(this.omissions)[index];
            (node.Body, node.Failed, node.Reads) = (this.body, this.body.HasOpaque || this.program.Findings.Count > findings, this.prepared);
        }
    }

    // SPEC 7.2.3: the pending slot of an explicit argument of a call that omits defaults, of its parameter's Type, which takes the argument
    // (Pending) where its acquisition did not form it there.
    private MirPlace Slot(BoundType? type, Koto node) => this.Temp(type ?? TypeOf(node), node, MirLocalRole.PendingSlot);

    private MirOperand Pending(MirPlace slot, in MirOperand operand, Koto node)
    {
        this.Use(slot, operand, node);
        return MirOperand.Move(slot);
    }

    // SPEC 7.2.3: the omitted defaults of a call, each into a pending slot of its parameter's Type in parameter order, from shared borrows
    // of the earlier parameters' slots (a copy, of those its template reads): a copy of its template, a call of a recursive template
    // (every slot, its arguments), or an Opaque value reading those slots where
    // its template failed (no record) or the copy would take the body past the limit (the call's Resource record). A default that does not
    // complete reaches no later default and no callee (SPEC 15.6.7): false, building going on for the checks only.
    private bool Defaults(CallPlan call, BindingSymbol function, Subst? subst, int start, Koto node)
    {
        foreach (ref readonly var omitted in call.DefaultArguments)
        {
            var decl = DefaultDecl.Of(omitted);
            var slot = this.Temp(omitted.ParameterType, node, MirLocalRole.PendingSlot, omitted.Parameter.Name);
            var template = this.visited.TryGetValue(decl, out var index) ? this.omissions[index] : default;
            var limit = template is { Body: { } copy, Recursive: false } && this.body.Statements.Length + copy.Statements.Length > ExpansionLimit;
            var (at, completes) = (this.arguments.Count, Completes(decl.Expression));
            if (!template.Recursive && (template.Body is null || template.Failed || limit))
            {
                this.roots.Clear();
                for (var i = 0; i < decl.Index; i++)
                {
                    this.roots.Add(this.arguments[start + MirCallee.Position(function, i)].Place.Root);
                }

                this.Site(node, slot, completes);
                if (limit)
                {
                    this.program.Report(SourceRef.Of(node), DiagnosticCode.DefaultExpansionLimit_Kd);
                }
            }
            else
            {
                for (var i = 0; i < decl.Index; i++)
                {
                    var read = template.Recursive || (template.Reads & (1UL << Math.Min(i, 63))) != 0;
                    this.arguments.Add(read ? this.Borrow(SemanticsKind.Ref, this.arguments[start + MirCallee.Position(function, i)].Place, node) : default);
                }

                if (template.Recursive)
                {
                    this.Invoke(slot, new(MirCalleeKind.Fn, HirTables.Symbol(decl.Function), subst, decl.Index + 1), at, node, !completes);
                }
                else
                {
                    this.Inline(template.Body!, subst, at, slot, node);
                }
            }

            if (!completes)
            {
                return false;
            }

            this.arguments[start + MirCallee.Position(function, decl.Index)] = MirOperand.Move(slot);
        }

        return true;
    }

    // Plan 5.6.2: a copy of a template's MIR in this body, folded once under the call's substitution: its scopes in a scope Inlined at the
    // call, its locals after this body's (its result the slot, its parameters the borrows from at), its entry block the current one (the
    // builder targets no entry block) and its returns going on after it; each Type applied and each callee's substitution composed
    // (TyCtxt.Compose), each Place projected again, so that a pair layer the substitution decides is its case's layer and a case-dependent
    // acquisition its case's (Acquire), each static and constant added again, and a Drop the instance's Type does not need left out. A Type
    // or callee the call does not instantiate is unsupported at the call.
    private void Inline(MirBody template, Subst? subst, int at, MirPlace slot, Koto node)
    {
        var (map, failed, after) = (this.folded, false, this.body.AddBlock());
        map.Clear();
        foreach (ref readonly var scope in template.Scopes)
        {
            map.Add(map.Count == 0 ? this.body.AddScope(this.scope, MirScopeKind.Inlined, SourceRef.Of(node)) : this.body.AddScope(map[scope.Outer], scope.Kind, scope.Source));
        }

        var locals = map.Count;
        for (var i = 0; i < template.Locals.Length; i++)
        {
            var local = template.Locals[i];
            map.Add(i == 0 ? slot.Root : local.Kind == MirLocalKind.Arg ? this.arguments[at + i - 1].Place.Root : this.body.AddLocal(local with { Type = FoldType(local.Type), Source = FoldInfo(local.Source) }));
        }

        var blocks = map.Count;
        for (var i = 0; i < template.Blocks.Length; i++)
        {
            map.Add(i == 0 ? this.current : this.body.AddBlock());
        }

        for (var b = 0; b < template.Blocks.Length; b++)
        {
            var (block, start) = (template.Blocks[b], this.arguments.Count);
            this.current = map[blocks + b];
            foreach (ref readonly var statement in template.Statements.Slice(block.Start, block.Count))
            {
                var copy = statement with { Place = FoldPlace(statement.Place), Source = FoldInfo(statement.Source) };
                this.body.Push(this.current, statement.Kind == MirStatementKind.Assign ? copy with { Rvalue = FoldRvalue(statement.Rvalue) } : copy);
            }

            var terminator = block.Terminator;
            var (call, operand) = (terminator.Kind == MirTerminatorKind.Call, terminator.Kind == MirTerminatorKind.SwitchInt || terminator.Callee.Kind == MirCalleeKind.Value ? FoldOperand(terminator.Operand) : default);
            for (var i = 0; call && i < terminator.Count; i++)
            {
                var argument = template.Operands[terminator.Start + i]; // staticEnsure's operand is a static's index in its body.
                this.arguments.Add(terminator.Callee is { Kind: MirCalleeKind.Intrinsic, Arg: (int)IntrinsicId.StaticEnsure }
                    ? MirOperand.Const(this.body.Const(new(MirConstKind.Integer, BoundType.ISize, ~FoldPlace(new(~(int)template.Consts[argument.Arg].Bits)).Root))) : FoldOperand(argument));
            }

            var first = call ? this.Flush(start) : this.body.Cases.Length;
            for (var i = 0; terminator.Kind == MirTerminatorKind.SwitchInt && i < terminator.Count; i++)
            {
                this.body.AddCase(template.Cases[terminator.Start + i].Value, Target(template.Cases[terminator.Start + i].Target));
            }

            var composed = terminator.Callee.Subst is { } inner && subst is not null ? compilation.Types.Compose(inner, subst) : terminator.Callee.Subst;
            failed |= composed is null && terminator.Callee.Subst is not null;
            var place = call || terminator.Kind == MirTerminatorKind.Drop ? FoldPlace(terminator.Place) : default;
            var copied = terminator with
            {
                Source = FoldInfo(terminator.Source), Target = Target(terminator.Target), CheckTarget = Target(terminator.CheckTarget), Operand = operand, Start = first,
                Place = place, Callee = terminator.Callee with { Subst = composed ?? terminator.Callee.Subst },
            };
            // The copy keeps a Drop where its instance's Type may need one (MirTypes.MayNeedDrop, the rule every Drop is built by).
            var kept = terminator.Kind != MirTerminatorKind.Drop || MirTypes.MayNeedDrop(this.body.TypeOf(place));
            this.body.Terminate(this.current, terminator.Kind == MirTerminatorKind.Return ? MirTerminator.Goto(after, copied.Source) : kept ? copied : MirTerminator.Goto(copied.Target, copied.Source));
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
                MirProjectionKind.PairLayer => TyCtxt.Layer(this.body.TypeOf(prefix), out _, out _) is var kind and not HirPlaceKind.Value ? this.Layer(prefix, kind) : prefix,
                MirProjectionKind.Index => this.body.Project(prefix, row.Kind, map[locals + row.Arg]),
                _ => this.body.Project(prefix, row.Kind, row.Arg),
            };
        }

        MirOperand FoldOperand(in MirOperand operand) => operand.Kind switch
        {
            MirOperandKind.Const => MirOperand.Const(this.body.Const(template.Consts[operand.Arg] with { Type = FoldType(template.Consts[operand.Arg].Type) })),
            MirOperandKind.PairAcquire => this.Acquire(new(HirAcquire.ByCase, HirAdaptation.None), FoldPlace(operand.Place), null, false, node),
            _ => operand with { Place = FoldPlace(operand.Place) },
        };

        // A template that did not fail has no Opaque value.
        MirRvalue FoldRvalue(in MirRvalue rvalue)
        {
            var start = this.arguments.Count;
            for (var i = 0; i < rvalue.Count; i++)
            {
                this.arguments.Add(FoldOperand(template.Operands[rvalue.Start + i]));
            }

            var place = rvalue.Kind is MirRvalueKind.Ref or MirRvalueKind.Discriminant or MirRvalueKind.Len ? FoldPlace(rvalue.Place) : default;
            return rvalue with { Place = place, Start = this.Flush(start), Type = rvalue.Type is { } type ? FoldType(type) : null };
        }
    }

    // A node of the omission graph: its default, Tarjan's low link, whether its component is open, whether it is recursive, and its
    // template, whether that failed and the prepared parameters it reads.
    private record struct Omission(DefaultDecl Decl, int Low, bool Open = true, bool Recursive = false, MirBody? Body = null, bool Failed = false, ulong Reads = 0);

    // The defaults the calls of a default omit, appended to the edges; a Closure's body is a body of its own.
    private sealed class Edges(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            for (var i = 0; CallPlan.Of(node, i) is { } call; i++)
            {
                foreach (ref readonly var omitted in call.DefaultArguments)
                {
                    builder.edges.Add(DefaultDecl.Of(omitted));
                }
            }

            if (node is not FunctionKoto { IsAnonymous: true })
            {
                node.VisitChildren(this);
            }
        }
    }
}
