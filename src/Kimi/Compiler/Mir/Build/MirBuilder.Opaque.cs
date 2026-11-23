// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Runtime.InteropServices;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// Derived, unsupported and broken values (plan 5.4, SPEC 23.1.2): the one located Unsupported path, the one broken-HIR-contract path
// (Invariant: InternalInvariant_Kd at a node no published form takes there, PLAN G396 (3) Z1), the one path of a construction record
// made in the body being built (Record), and the gate of every derived Opaque value (DECISIONS Z17), whose site names the uses
// MirBuilder.Opaque.Uses.cs collects: the node's syntax's, a part built already, or its caller's.
// - The gate: Opaque (the node's own uses), Derived (the uses its caller collected) and DerivedValue (an Opaque temp's value) form a
//   derived value, without a record, from one published fact per producer (the table below); Site and AddSite, the one
//   MirBody.AddOpaque call, are reached only from the gate, Unsupported and Invariant (ArchitectureRulesTest pins both, and the gate's
//   callers, so a new producer needs a reviewed row).
// - The producers and the fact each reads:
//   - a variable, a failed acquisition or Pattern binding, a construct's value (Blocks, Values, Match): HirOp.Error at the node, or
//     HirAcquire.Failed at the acquired node (DECISIONS Z16), Opaque;
//   - a write whose target designates an Error (DerivedWrite): HirOp.Error at the Place the target designates (Z16), Opaque;
//   - a static use and a Field initializer's step (Places, Constructors): the declaration fact, not published Body (Z16), Opaque;
//   - a jump whose published target row is null (Transfer): lint row 9 or 10 at the jump (BU-101), Opaque, its operand located (none
//     for a constant);
//   - a Field of no stored Type or a receiver of no Type (Field) and an accessor's failed self (Body): the selected slot's, the
//     receiver's or the self slot's Type is null (PLAN G1073, D-834), Derived;
//   - a container whose published Completion completes into a destination neither Unit nor Never (Leave): lint row 12 or the guess
//     rule's Error (R5-M186), Derived;
//   - an omitted default whose template failed (Body null or Failed) or whose copy the budget refuses (Refuses' Resource record at the
//     call) (Defaults): Derived;
//   - an rvalue that would lend a Place in an Opaque temp (Unlent): the temp's site's fact (R4-M149), DerivedValue.
internal sealed partial class MirBuilder
{
    // The one way a form without a building rule is built (plan 5.4, 6.2): an Opaque value in its place, its site naming the uses of the
    // node's syntax (SiteUses), and its located record.
    private void Unsupported(Koto node, MirPlace destination)
    {
        this.SiteUses(node);
        this.Unsupported(node, destination, node);
    }

    // The one located Unsupported record with an Opaque value: the Opaque value of a value's evaluation at a node's site, naming the uses
    // its caller collected (SiteUses, or SiteUses(MirPlace?) for a part built already; a default its call does not instantiate names the
    // earlier slots, Defaults), and the node's record, merged by location, unless the value is an Error, whose error the Opaque value is
    // derived from (SPEC 23.1.2; DECISIONS Z16: the failure fact at the value).
    private void Unsupported(Koto node, MirPlace destination, Koto value)
    {
        if (HirTables.Op(value) != HirOp.Error)
        {
            this.Record(node, DiagnosticCode.Unsupported_Kd);
        }

        this.Site(node, destination, Completes(value));
    }

    // The one broken-HIR-contract path (DECISIONS R4-M61; consumers trust validated IR): at a node the builder reaches, an arm of a closed
    // HIR vocabulary or of a fact a published Op guarantees that no published form takes, InternalInvariant_Kd at the node, unless it is
    // an Error, whose error its value is derived from (SPEC 23.1.2; DECISIONS Z16); its value an Opaque in its place, its site naming the
    // uses of the node's syntax (SiteUses), as a form without a building rule has.
    private void Invariant(Koto node, MirPlace destination)
    {
        this.SiteUses(node);
        this.Invariant(node, destination, Completes(node));
    }

    // A broken contract's record and Opaque value (Invariant) at a site naming the uses its caller collected: the node's syntax's, or a
    // part built already (SiteUses(MirPlace?), R5-M63, R5-M84).
    private void Invariant(Koto node, MirPlace destination, bool completes)
    {
        if (HirTables.Op(node) != HirOp.Error)
        {
            this.Record(node, DiagnosticCode.InternalInvariant_Kd);
        }

        this.Site(node, destination, completes);
    }

    // The one path of a construction record made while a body is built (MirProgram.Report): a code at a node of the body, which marks
    // the body (MirBody.HasConstructionRecord, its one writer; DECISIONS R5-M183's GR-1a line). The module's broken HIR contracts,
    // recorded before its bodies, have no body in hand (Module).
    private void Record(Koto node, DiagnosticCode code)
    {
        this.program.Report(SourceRef.Of(node), code);
        this.body.HasConstructionRecord = true;
    }

    // The gate's derived value of the destination's Type in place of a node, its site naming the uses of the node's syntax (SiteUses);
    // where the node cannot complete, its block ends and building continues in a block only the checks reach (SPEC 14.10.3).
    private void Opaque(Koto node, MirPlace destination)
    {
        this.SiteUses(node);
        this.Site(node, destination, Completes(node));
    }

    // The gate's derived value at a node whose site names the uses its caller collected: a part built already (SiteUses(MirPlace?)), none,
    // or a default's earlier slots.
    private void Derived(Koto node, MirPlace destination, bool completes) => this.Site(node, destination, completes);

    // The gate's derived value of an rvalue that would lend a Place in an Opaque temp (Unlent; DECISIONS R4-M149): an Opaque located at
    // the temp's node whose site names only the temp, undecided at its root.
    private MirRvalue DerivedValue(MirPlace temp, BoundType? type)
    {
        this.ClearUses();
        this.SiteUse(new(temp.Root));
        return MirRvalue.Opaque(type, this.AddSite(this.body.Locals[temp.Root].Source.Syntax));
    }

    // SPEC 13.7.1 step 1, 13.7.2 step 1, 23.1.2: an assignment or update whose target designates a Place Binding rejected (through its
    // groups, SPEC 3.4: Designated) is derived from that error, after its right side is evaluated, as Operand evaluates it (its value
    // where its published Acquire is Temporary, else its Place located), so its own failures are checked; the site takes the result as a
    // part located already (SiteUses: read at its own node where its Adjust acquires it, DECISIONS Z17) and names the target's roots. The
    // write's own taking, securing the value at the destination Type (its acquisition and adaptation), rests on the failure (rule W) and
    // is not built: the failed target gives no Type, and Binding decides no taking for a consumer with failure causes but a bare one
    // (DECISIONS R4-M145 C2, BU-27), so a Closure's receiver counts none of a capture's.
    private void DerivedWrite(Koto node, MirPlace destination)
    {
        var mark = this.located.Count;
        if (node is BinaryKoto { Right: { } right })
        {
            this.located.Add((right, HirTables.AdjustOf(right).Acquire == HirAcquire.Temporary ? this.Value(right) : this.Place(right)));
        }

        this.Opaque(node, destination);
        CollectionsMarshal.SetCount(this.located, mark);
    }

    // An Opaque value at a node's site naming the uses collected, of its destination's Type (none for a destination of no Type, PLAN
    // G1073); where it cannot complete, building continues in a block only the checks reach.
    private void Site(Koto node, MirPlace destination, bool completes)
    {
        var site = this.AddSite(SourceRef.Of(node));
        this.Set(destination, MirRvalue.Opaque(this.body.TypeOf(destination), site), node);
        if (!completes)
        {
            this.Diverge(node);
        }
    }

    // The site of an Opaque value at its syntax, naming the uses collected.
    private int AddSite(SourceRef source) => this.body.AddOpaque(source, CollectionsMarshal.AsSpan(this.uses), this.keptUses, 0);
}
