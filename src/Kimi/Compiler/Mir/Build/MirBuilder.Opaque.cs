// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Derived and unsupported values (plan 5.4, SPEC 23.1.2): the one located Unsupported path, and the Opaque value that replaces a node
// Binding rejected or no rule builds, its site naming the roots the node's syntax reads, writes or borrows.
internal sealed partial class MirBuilder
{
    private readonly List<int> roots = []; // The roots of the Opaque site being formed (Site).
    private readonly List<int> kept = []; // The roots of the kept operands evaluated, innermost Error node last.
    private Roots? collector;
    private KeptOperands? keptOperands;

    // Whether the published facts say the lexer or Binding rejected a node the builder reaches: an Error, a value without a Type, a literal
    // without its constant (an invalid escape, a number that does not fit), or a name of a local whose declaration is an Error.
    private static bool Rejected(Koto node) => HirTables.Op(node) is HirOp.Error or HirOp.Literal || HirTables.Meaning(node) is null ||
        (HirTables.Symbol(node)?.Declaration is { } declaration && HirTables.Op(declaration) == HirOp.Error);

    // Whether the HIR keeps a child of an Error node in place: an operand Binding resolved of a failed compound Place (DECISIONS R5-M63).
    private static bool Kept(Koto node) => HirTables.AdjustOf(node) == new HirAdjust(HirAcquire.None, HirAdaptation.None);

    // The one way a form without a building rule is built (plan 5.4, 6.2): an Opaque value in its place and a located record, merged by
    // location, unless the node is one the lexer or Binding rejected, whose error the Opaque value is derived from (SPEC 23.1.2).
    private void Unsupported(Koto node, MirPlace destination)
    {
        if (!Rejected(node))
        {
            this.program.Report(SourceRef.Of(node));
        }

        this.Opaque(node, destination);
    }

    // An Opaque value of the destination's Type in place of a node, its site naming the locals the node's syntax reads, writes or borrows;
    // where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3). SPEC 23.1.2
    // (DECISIONS R5-M63): an Error node's kept operands are evaluated first, in syntax order, each as its Place (a value in a temp) and
    // never acquired, and the site names their roots instead of their syntax's, so a moved operand is not used again there.
    private void Opaque(Koto node, MirPlace destination)
    {
        var start = this.kept.Count;
        if (HirTables.Op(node) == HirOp.Error)
        {
            node.VisitChildren(this.keptOperands ??= new(this));
        }

        this.roots.Clear();
        for (var i = start; i < this.kept.Count; i++)
        {
            this.Root(this.kept[i]);
        }

        this.kept.RemoveRange(start, this.kept.Count - start);
        (this.collector ??= new(this)).Collect(node);
        this.Site(node, destination, Completes(node));
    }

    // A root of the Opaque site being formed, once: a kept operand's, or one its syntax names.
    private void Root(int root)
    {
        if (!this.roots.Contains(root))
        {
            this.roots.Add(root);
        }
    }

    // An Opaque value at a node's site naming the roots collected; where it cannot complete, building continues in a block only the
    // checks reach.
    private void Site(Koto node, MirPlace destination, bool completes)
    {
        var site = this.body.AddOpaque(SourceRef.Of(node), CollectionsMarshal.AsSpan(this.roots));
        this.Set(destination, MirRvalue.Opaque(this.body.TypeOf(destination) ?? BoundType.Unit, site), node);
        if (!completes)
        {
            var next = this.body.AddBlock();
            this.body.Terminate(this.current, MirTerminator.Unreachable(this.Info(node), next));
            this.current = next;
        }
    }

    // An Error node's kept operands in syntax order, each evaluated as its Place and its root kept; an operand that is an Error again
    // evaluates its own as its Opaque value is formed.
    private sealed class KeptOperands(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            if (Kept(node))
            {
                builder.kept.Add(builder.Place(node).Root);
            }
        }
    }

    // The locals, parameters, environment bindings and statics a replaced node's syntax names (their Res) or a Closure in it captures,
    // the roots of its Opaque site, but in the operands an Error node keeps, which are evaluated already.
    private sealed class Roots(MirBuilder builder) : KotoVisitor
    {
        private bool operands; // The nodes visited are an Error node's own children, whose kept ones its site names already.

        public override void Visit(Koto node)
        {
            if (this.operands && Kept(node))
            {
                return;
            }

            var operands = this.operands;
            this.operands = false;
            this.Named(node);
            node.VisitChildren(this);
            this.operands = operands;
        }

        // The roots of a node's syntax, collected after its kept operands' (Opaque): an Error node's own children but those it keeps.
        internal void Collect(Koto node)
        {
            this.Named(node);
            this.operands = HirTables.Op(node) == HirOp.Error;
            node.VisitChildren(this);
            this.operands = false;
        }

        // The roots a node names: its Res's binding or static, and a Closure's capture sources.
        private void Named(Koto node)
        {
            var symbol = HirTables.Symbol(node);
            switch (HirTables.ResOf(node))
            {
                case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture:
                    this.Add(symbol);
                    break;
                case HirRes.Static when symbol is not null:
                    builder.Root(builder.StaticRoot(symbol).Root);
                    break;
            }

            var captures = node is FunctionKoto function ? BoundClosure.Of(function)?.Captures : null; // A named function has no definition.
            for (var i = 0; i < captures?.Count; i++)
            {
                this.Add(captures[i].Source);
            }
        }

        private void Add(BindingSymbol? symbol)
        {
            if (symbol is not null && builder.Bound(symbol) is { } place)
            {
                builder.Root(place.Root);
            }
        }
    }
}
