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
    private const int ExpansionLimit = 65_536; // No copy takes a body past this many MIR statements (IMPL 21.3.2).
    private readonly Dictionary<DefaultDecl, int> visited = new(); // Each default's node in the omission graph.
    private readonly List<Omission> omissions = []; // The nodes in visit order.
    private readonly List<int> component = []; // The nodes whose component is open (Tarjan's stack).
    private readonly List<DefaultDecl> edges = []; // The defaults omitted in the defaults being visited, innermost last.
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
            int low;
            if (this.visited.TryGetValue(this.edges[i], out var next))
            {
                low = this.omissions[next].Open ? next : index;
            }
            else
            {
                low = this.omissions[this.Visit(this.edges[i])].Low;
            }

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

    // A default's template, MirDefKey(function, DefaultTemplate, index); one with an Opaque value or a finding failed. A default the publisher
    // did not evaluate, of an unresolved function such as one beyond the case bound, has none: its omitting calls take an Opaque value.
    private void Template(int index)
    {
        var decl = this.omissions[index].Decl;
        var findings = this.program.Findings.Count;
        if (HirTables.Symbol(decl.Function) is { } function && decl.Parameter(decl.Index) is { } type && HirTables.Op(decl.Expression) != HirOp.Syntax)
        {
            this.Body(new(function, MirBodyKind.DefaultTemplate, decl.Index), decl.Function, function, type, decl.Expression);
            ref var node = ref CollectionsMarshal.AsSpan(this.omissions)[index];
            (node.Body, node.Failed, node.Reads) = (this.body, this.body.HasOpaque || this.program.Findings.Count > findings, this.prepared);
        }
    }

    // SPEC 7.2.3: the pending slot of an explicit argument of a call that omits defaults, of its parameter's Type, which takes the argument
    // (Pending) where its acquisition did not form it there, its value made there (SPEC 3.6, 16.2.1; DECISIONS R4-M129).
    private MirPlace Slot(BoundType? type, Koto node) => this.Hold(type ?? TypeOf(node), node, MirLocalRole.PendingSlot);

    private MirOperand Pending(MirPlace slot, in MirOperand operand, Koto node)
    {
        this.Use(slot, operand, node);
        return MirOperand.Move(this.Made(slot));
    }

    // SPEC 7.2.3: the omitted defaults of a call, each into a pending slot of its parameter's Type in parameter order, from shared borrows
    // of the earlier parameters' slots (a copy, of those its template reads): a copy of its template, a call of a recursive template
    // (every slot, its arguments), or an Opaque value reading those slots where its template failed (no record), the copy would take the
    // body past the limit (the call's Resource record) or the call's substitution does not instantiate it (Instantiates, decided before a
    // copy is written, a Limit only: the call's Unsupported_Kd, SPEC 23.1.1, through Unsupported; over storage Binding rejected the copy
    // is built, as a non-generic template over it is, DECISIONS R4-M139). A default that does not complete reaches no later
    // default and no callee at run time (SPEC 15.6.7); building goes on for the checks.
    private void Defaults(CallPlan call, BindingSymbol function, Subst? subst, int start, Koto node)
    {
        foreach (ref readonly var omitted in call.DefaultArguments)
        {
            var decl = DefaultDecl.Of(omitted);
            var slot = this.Hold(omitted.ParameterType, node, MirLocalRole.PendingSlot, omitted.Parameter.Name);
            var template = this.visited.TryGetValue(decl, out var index) ? this.omissions[index] : default;
            var limit = template is { Body: { } copy, Recursive: false } && this.body.Statements.Length + copy.Statements.Length > ExpansionLimit;
            var unsupported = !limit && template is { Body: { } made, Recursive: false, Failed: false } && subst is not null &&
                !this.Instantiates(made, subst);
            var at = this.arguments.Count;
            var completes = Completes(decl.Expression);
            if (!template.Recursive && (template.Body is null || template.Failed || limit || unsupported))
            {
                this.roots.Clear();
                for (var i = 0; i < decl.Index; i++)
                {
                    this.roots.Add(this.arguments[start + MirCallee.Position(function, i)].Place.Root);
                }

                if (unsupported)
                {
                    this.Unsupported(node, slot, decl.Expression);
                }
                else
                {
                    this.Site(node, slot, completes);
                }

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

            this.arguments[start + MirCallee.Position(function, decl.Index)] = MirOperand.Move(this.Made(slot));
        }
    }

    // A node of the omission graph: its default, Tarjan's low link, whether its component is open, whether it is recursive, and its
    // template, whether that failed and the prepared parameters it reads.
    private record struct Omission(
        DefaultDecl Decl,
        int Low,
        bool Open = true,
        bool Recursive = false,
        MirBody? Body = null,
        bool Failed = false,
        ulong Reads = 0);

    // The defaults the calls a default's template builds omit, appended to the edges: none at a node whose call the builder does not build,
    // one the HIR publishes as Syntax (a part replaced, such as the body of a closure Binding failed to define, or covered; its evaluated
    // parts lie below it), Error or Unset (an Opaque value); a closure definition's body is a body of its own (DECISIONS R5-M79).
    private sealed class Edges(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            for (var i = 0; HirTables.Op(node) is not (HirOp.Syntax or HirOp.Error or HirOp.Unset) && CallPlan.Of(node, i) is { } call; i++)
            {
                foreach (ref readonly var omitted in call.DefaultArguments)
                {
                    builder.edges.Add(DefaultDecl.Of(omitted));
                }
            }

            if (node is not FunctionKoto { ClosureDefinition: not null } function)
            {
                node.VisitChildren(this);
            }
        }
    }
}
