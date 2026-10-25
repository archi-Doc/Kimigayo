// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Derived and unsupported values (plan 5.4, SPEC 23.1.2): the one located Unsupported path, and the Opaque value that replaces a node
// Binding rejected or no rule builds, its site naming the roots the node's syntax reads, writes or borrows (MirBuilder.Opaque.Roots.cs).
internal sealed partial class MirBuilder
{
    // Whether the published facts say the lexer or Binding rejected a node the builder reaches: an Error, a value without a Type (a name of
    // a local whose rejected declaration has none too), or a literal without its constant (an invalid escape, a number that does not fit).
    // A name of a local whose rejected declaration has a Type is its local's (Let).
    private static bool Rejected(Koto node) => HirTables.Op(node) is HirOp.Error or HirOp.Literal || HirTables.Meaning(node) is null;

    // The one way a form without a building rule is built (plan 5.4, 6.2): an Opaque value in its place, its site naming the roots the
    // node's syntax reads, writes or borrows (SiteRoots), and its located record.
    private void Unsupported(Koto node, MirPlace destination)
    {
        this.SiteRoots(node);
        this.Unsupported(node, destination, node);
    }

    // The one located Unsupported record with an Opaque value: the Opaque value of a value's evaluation at a node's site, naming the roots
    // its caller collected (Site; a default its call does not instantiate names the earlier slots, Defaults), and the node's record,
    // merged by location, unless the value is one the lexer or Binding rejected, whose error the Opaque value is derived from (SPEC 23.1.2).
    private void Unsupported(Koto node, MirPlace destination, Koto value)
    {
        if (!Rejected(value))
        {
            this.program.Report(SourceRef.Of(node));
        }

        this.Site(node, destination, Completes(value));
    }

    // An Opaque value of the destination's Type in place of a node, its site naming the locals the node's syntax reads, writes or borrows
    // (SiteRoots); where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3).
    private void Opaque(Koto node, MirPlace destination)
    {
        this.SiteRoots(node);
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
}
