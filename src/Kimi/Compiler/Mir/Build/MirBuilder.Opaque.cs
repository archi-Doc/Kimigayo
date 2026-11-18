// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Derived and unsupported values (plan 5.4, SPEC 23.1.2): the one located Unsupported path, the one path of a construction record made
// in the body being built (Record), and the Opaque value that replaces a node Binding rejected, a form no rule builds, or a default its
// call does not instantiate (Defaults; SPEC 23.1.1, DECISIONS R4-M127), its site naming the roots the node's syntax reads, writes or
// borrows (MirBuilder.Opaque.Roots.cs) or a default's earlier slots; the derived value of a write whose target Binding rejected follows
// its right side, built first (DerivedWrite, PLAN G1022).
internal sealed partial class MirBuilder
{
    // Whether the published facts say the lexer or Binding rejected a node the builder reaches: an Error (a literal the lexer could not
    // read among them, Binding.Rejected) or a value without a Type (a name of a local whose rejected declaration has none too). A name of
    // a local whose rejected declaration has a Type is its local's (Let).
    private static bool Rejected(Koto node) => HirTables.Op(node) == HirOp.Error || HirTables.Meaning(node) is null;

    // The one way a form without a building rule is built (plan 5.4, 6.2): an Opaque value in its place, its site naming the roots the
    // node's syntax reads, writes or borrows (SiteRoots), and its located record.
    private void Unsupported(Koto node, MirPlace destination)
    {
        this.SiteRoots(node);
        this.Unsupported(node, destination, node);
    }

    // The one located Unsupported record with an Opaque value: the Opaque value of a value's evaluation at a node's site, naming the roots
    // its caller collected (SiteRoots, or SiteRoots(MirPlace?) for a part built already; a default its call does not instantiate names the
    // earlier slots, Defaults), and the node's record, merged by location, unless the value is one the lexer or Binding rejected, whose
    // error the Opaque value is derived from (SPEC 23.1.2).
    private void Unsupported(Koto node, MirPlace destination, Koto value)
    {
        if (!Rejected(value))
        {
            this.Record(node, DiagnosticCode.Unsupported_Kd);
        }

        this.Site(node, destination, Completes(value));
    }

    // The one path of a construction record made while a body is built (MirProgram.Report): a code at a node of the body, which marks
    // the body (MirBody.HasConstructionRecord, its one writer; DECISIONS R5-M183's GR-1a line). The module's broken HIR contracts,
    // recorded before its bodies, have no body in hand (Module).
    private void Record(Koto node, DiagnosticCode code)
    {
        this.program.Report(SourceRef.Of(node), code);
        this.body.HasConstructionRecord = true;
    }

    // An Opaque value of the destination's Type in place of a node, its site naming the locals the node's syntax reads, writes or borrows
    // (SiteRoots); where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3).
    private void Opaque(Koto node, MirPlace destination)
    {
        this.SiteRoots(node);
        this.Site(node, destination, Completes(node));
    }

    // SPEC 13.7.1 step 1, 13.7.2 step 1, 23.1.2: an assignment or update whose target Binding rejected is derived from that error, after
    // its right side, published with its facts and resting on no failure of the target, is evaluated by its Adjust and secured, as the
    // accepted update's is (an accepted assignment holds it at the destination Type, which a rejected target does not give: here at its
    // own), so its own failures are checked; the site names its root as a part located already (SiteRoots) and the target's. A right
    // side no consumer takes (its acquisition Unset: Binding left it to the failed target, DECISIONS BU-103, R4-M145) rests on that
    // failure and is not built: the site names its syntax roots, as for any derived value (rule W).
    private void DerivedWrite(Koto node, MirPlace destination)
    {
        var mark = this.located.Count;
        if (node is BinaryKoto { Right: { } right } && HirTables.AdjustOf(right).Acquire != HirAcquire.Unset &&
            this.Secure(this.Operand(right, expected: null), right) is { Kind: MirOperandKind.Move } value)
        {
            this.located.Add((right, value.Place));
        }

        this.Opaque(node, destination);
        CollectionsMarshal.SetCount(this.located, mark);
    }

    // An Opaque value at a node's site naming the roots collected; where it cannot complete, building continues in a block only the
    // checks reach.
    private void Site(Koto node, MirPlace destination, bool completes)
    {
        var site = this.body.AddOpaque(SourceRef.Of(node), CollectionsMarshal.AsSpan(this.roots));
        this.Set(destination, MirRvalue.Opaque(this.body.TypeOf(destination) ?? BoundType.Unit, site), node);
        if (!completes)
        {
            this.Diverge(node);
        }
    }
}
