// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Derived and unsupported values (plan 5.4, SPEC 23.1.2): the one located Unsupported path, and the Opaque value that replaces a node
// Binding rejected or no rule builds, its site naming the roots the node's syntax reads, writes or borrows.
internal sealed partial class MirBuilder
{
    private readonly List<int> roots = [];
    private Roots? collector;

    // Whether the published facts say the lexer or Binding rejected a node the builder reaches: an Error, a value without a Type, a literal
    // without its constant (an invalid escape, a number that does not fit), or a name of a local whose declaration is an Error.
    private static bool Rejected(Koto node) => HirTables.Op(node) is HirOp.Error or HirOp.Literal || HirTables.Meaning(node) is null ||
        (HirTables.Symbol(node)?.Declaration is { } declaration && HirTables.Op(declaration) == HirOp.Error);

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
    // where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3).
    private void Opaque(Koto node, MirPlace destination)
    {
        this.roots.Clear();
        (this.collector ??= new(this)).Visit(node);
        this.Site(node, destination, Completes(node));
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

    // The locals, parameters, environment bindings and statics a replaced node's syntax names (their Res) or a Closure in it captures,
    // the roots of its Opaque site.
    private sealed class Roots(MirBuilder builder) : KotoVisitor
    {
        public override void Visit(Koto node)
        {
            var symbol = HirTables.Symbol(node);
            switch (HirTables.ResOf(node))
            {
                case HirRes.Local or HirRes.Param or HirRes.PreparedParam or HirRes.Capture:
                    this.Add(symbol);
                    break;
                case HirRes.Static when symbol is not null:
                    this.Add(builder.StaticRoot(symbol).Root);
                    break;
            }

            var captures = node is FunctionKoto { IsAnonymous: true } closure ? BoundClosure.Of(closure)?.Captures : null;
            for (var i = 0; i < captures?.Count; i++)
            {
                this.Add(captures[i].Source);
            }

            node.VisitChildren(this);
        }

        private void Add(BindingSymbol? symbol)
        {
            if (symbol is not null && builder.Bound(symbol) is { } place)
            {
                this.Add(place.Root);
            }
        }

        private void Add(int root)
        {
            if (!builder.roots.Contains(root))
            {
                builder.roots.Add(root);
            }
        }
    }
}
