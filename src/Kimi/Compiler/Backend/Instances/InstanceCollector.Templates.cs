// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the site templates of the checked set (R5 package K 4.5, 4.7 and 4.8): one per body and program version, the body's sites in
// location order, each with whether a runtime path from the body's entry reaches it, which replaying an item's template resolves.
// Input: a built, validated MirProgram; the number of root modules; the Target and Need columns of the intrinsic rows; LayoutNeeds and
// Layout.Indirect (DECISIONS R5-M149).
// Output: each body's sites, `sites` from `starts[body.Ordinal]` to `starts[body.Ordinal + 1]`, which Replay reads; and its layout needs
// past its Places' roots' storage (InstanceCollector.Layouts.cs' ListNeeds), which LayOutNeeds reads.
// Sites: a call whose callee may carry a target (a Fn, Requirement or Value callee, an intrinsic whose row names one; a Virtual call none,
// X5) or whose intrinsic lays out its Type argument; a Drop; a case adaptation (an instance's creation, DECISIONS R5-M111); an aggregate;
// an erasure to a Function Type; a function item constant; a cast or binary operation whose Types depend on slots (K 4.7, checked per
// instance); a binary operation whose operator row has a layout need (a raw pointer's displacement, K 4.8). A root-module body's
// operations take the definition-stage capability check on the way (A3b, InstanceCollector.Capabilities.cs).
// Invariants: built once per version, before any item is replayed; the runtime bit is one MirReach walk without CheckTarget edges; a
// template holds locations and the body's Places only, no substitution or target.
// Must not: resolve a target, read TyCtxt or an item, or record anything but A3b's capability gaps.

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

internal sealed partial class InstanceCollector
{
    private readonly MirReach reach = new();
    private (MirLocation At, bool Runtime)[] sites = []; // The templates: each body's sites, in body order.
    private int[] starts = []; // By body ordinal, its first site; the entry after the last body ends the sites.

    // K 4.5 rows 1-9: a call carries a target by its callee: a Fn, Requirement or Value callee, or an intrinsic whose row names one; a
    // Virtual call none, its receiver's VTable holding the entries (X5).
    private static bool Carries(in MirCallee callee) => callee.Kind switch
    {
        MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Value => true,
        MirCalleeKind.Intrinsic => IntrinsicRows.Of((IntrinsicId)callee.Arg).Target != IntrinsicTarget.None,
        MirCalleeKind.Virtual => false,
        _ => throw new InvalidOperationException($"A call has the callee kind {callee.Kind}."),
    };

    // K 4.5 rows 8 and 11-13, K 4.7 and K 4.8: a statement is a site by its rvalue: a case adaptation, which carries its payload's
    // descriptor where its instance's case creates an object (DECISIONS R5-M111); an aggregate, a by-value use whose Closure environment
    // also carries a target; an erasure to a Function Type; a function item constant; an operation whose Types depend on slots, which the
    // capability table checks per instance; an operator with a layout need (LayoutNeeds; DECISIONS R5-M149).
    private static bool IsSite(MirBody body, in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Rvalue: var rvalue } && rvalue.Kind switch
    {
        MirRvalueKind.Adapt or MirRvalueKind.Aggregate => true,
        MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.Erasure => true,
        MirRvalueKind.Cast => IsOpen(body, rvalue),
        MirRvalueKind.BinaryOp => IsOpen(body, rvalue) ||
            LayoutNeeds.Of((MirBinaryOp)rvalue.Op, OperandType(body, body.Operands[rvalue.Start])) != LayoutNeed.None,
        MirRvalueKind.Use => body.Operands[rvalue.Start] is { Kind: MirOperandKind.Const } operand && body.Consts[operand.Arg].Kind == MirConstKind.FnItem,
        _ => false,
    };

    // Whether a block's terminator is a site: a Drop (row 10), a call that may carry a target, or an intrinsic call whose row reads its Type
    // argument (a by-value use; IntrinsicRow.ReadsTypeArgument, the one rule an Item of its function follows too, Bodyless).
    private static bool IsSite(in MirTerminator terminator) => terminator.Kind == MirTerminatorKind.Drop ||
        (terminator is { Kind: MirTerminatorKind.Call, Callee: var callee } &&
            (Carries(callee) || (callee.Kind == MirCalleeKind.Intrinsic && IntrinsicRows.Of((IntrinsicId)callee.Arg).ReadsTypeArgument)));

    // Whether a step of a body's Places may leave its root's storage: a Deref, a PairLayer or an Index (of a sequence handle), the steps whose
    // prefix may be laid out without its component.
    private static bool MayLeave(MirBody body)
    {
        var projections = body.Projections;
        for (var id = 1; id < projections.Length; id++)
        {
            if (projections[id].Kind is MirProjectionKind.Deref or MirProjectionKind.PairLayer or MirProjectionKind.Index)
            {
                return true;
            }
        }

        return false;
    }

    // Each body's sites, once per version, in location order, with whether a runtime path from its entry reaches each.
    private void Templates(MirProgram program)
    {
        var bodies = program.Bodies;
        if (this.starts.Length <= bodies.Length)
        {
            this.starts = new int[Math.Max(bodies.Length + 1, this.starts.Length * 2)];
            this.needStarts = new int[this.starts.Length];
        }

        var count = 0;
        this.needCount = 0;
        foreach (var body in bodies)
        {
            (this.starts[body.Ordinal], this.needStarts[body.Ordinal]) = (count, this.needCount);
            count = this.Template(body, count);
        }

        (this.starts[bodies.Length], this.needStarts[bodies.Length]) = (count, this.needCount);
    }

    // A body's sites, appended from `count` in location order with their runtime bits; a root-module body's operations, and a root-module
    // static's declaration, are checked against the capability table on the way (A3b), and the layout needs of its Places past their roots'
    // storage are listed (K 4.8, ListNeeds) where a step may leave it. Returns the new count.
    private int Template(MirBody body, int count)
    {
        var blocks = body.Blocks;
        var root = body.Source.Module < this.rootModules;
        var leaves = MayLeave(body);
        if (root)
        {
            this.CheckStatic(body);
        }

        this.reach.Clear(blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        for (var block = 0; block < blocks.Length; block++)
        {
            var start = blocks[block].Start;
            var end = blocks[block].Count;
            for (var index = 0; index <= end; index++)
            {
                if (index < end && root)
                {
                    this.CheckOperation(body, body.Statements[start + index]);
                }

                if (index < end ? IsSite(body, body.Statements[start + index]) : IsSite(blocks[block].Terminator))
                {
                    count = this.AddSite(count, new(block, index), this.reach[block]);
                }

                if (leaves)
                {
                    this.ListNeeds(body, new(block, index));
                }
            }
        }

        return count;
    }

    private int AddSite(int count, MirLocation at, bool runtime)
    {
        if (count == this.sites.Length)
        {
            Array.Resize(ref this.sites, Math.Max(256, count * 2));
        }

        this.sites[count] = (at, runtime);
        return count + 1;
    }
}
