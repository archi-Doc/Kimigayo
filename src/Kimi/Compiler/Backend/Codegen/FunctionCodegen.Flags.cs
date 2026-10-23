// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Mir;
using static Kimi.Compiler.Backend.LlvmModuleText;

namespace Kimi.Compiler.Backend;

// The drop flags of FunctionCodegen (R5 plan C4b, package C 3.2 I2 and I6; DECISIONS R4-M6a, R5-M99): A2's flags of the body
// (MirDropTable.FlagsOf), each a bool in bool's representation (IMPL 21.1.3: an i8 slot %f<k> of the entry block, computed as i1), cleared
// at entry, since flags start clear; the item's view's flag points (MirDropTable.PointsOf) written where their location completes: the
// entry's in the prologue, a statement's after it, a terminator's after its own effect and before its transfer (Branch, Switch), where it
// holds on each runtime successor (A1 applies a location once, on every runtime edge), none for a terminator without one; and the test of a
// destruction under a flag (a Flag verdict, and under Open the own bit, C4c): the flag read, the destruction on its set path, the paths
// joined. Must not decide a flag's value or which Drop tests it (A2's), split an edge or write another view's points.
internal sealed partial class FunctionCodegen
{
    private static readonly ValueLowering Flag = Layout.Row(BoundType.Boolean); // A flag's representation, bool's.

    private int point; // The body's first flag point not passed yet.

    // The flags' slots of the entry block (I2), each cleared.
    private void FlagSlots()
    {
        var flags = this.program.Drops.FlagsOf(this.body.Ordinal);
        for (var i = 0; i < flags; i++)
        {
            this.WriteSlot("  %f", i, Flag.Layout);
        }

        for (var i = 0; i < flags; i++)
        {
            this.StoreAt(new('f', i), Flag.Layout, new('\0', 0));
        }
    }

    // The item's view's flag points at a location, each storing its Set into each flag of its range. The body's points are ordered by
    // block, each view's in location order (R4-M6a), so one forward cursor passes another view's and those of a block not written, and stops
    // at a later block or at a later location of the view's in the same block.
    private void Points(MirLocation at)
    {
        var points = this.program.Drops.PointsOf(this.body.Ordinal);
        var view = 1UL << (int)this.collector.Set.Items[this.item].View;
        for (; this.point < points.Length; this.point++)
        {
            var change = points[this.point];
            var viewed = (change.Views & view) != 0;
            if (change.At.Block > at.Block || (change.At.Block == at.Block && viewed && change.At.Index > at.Index))
            {
                return;
            }

            for (var flag = change.First; viewed && change.At == at && flag < change.First + change.Count; flag++)
            {
                this.StoreAt(new('f', flag), Flag.Layout, new('\0', change.Set ? 1 : 0));
            }
        }
    }

    // The test of a destruction under a flag (-1: none, which writes nothing): the flag read as a bool, then its guard (Guard); the guard
    // Join closes, or -1.
    private int Test(int flag)
    {
        if (flag < 0)
        {
            return -1;
        }

        var id = this.next++;
        this.Guard(id, this.Narrow(Flag, this.LoadAt(new('f', flag), Flag.Layout)));
        return id;
    }

    // A destruction's guard numbered id: its set path, where an i1 value holds, the block drop<id>; Join(id) closes it.
    private void Guard(int id, Value set)
    {
        var output = this.output;
        output.Write("  br i1 ");
        this.WriteValue(set);
        Name(output, ", label %drop", id);
        Name(output, ", label %dropped", id);
        output.Write('\n');
        Name(output, "drop", id);
        output.Write(":\n");
    }

    // The end of a destruction under a flag: its set path joins the other at dropped<id>; nothing without a guard.
    private void Join(int guard)
    {
        if (guard < 0)
        {
            return;
        }

        Name(this.output, "  br label %dropped", guard);
        this.output.Write('\n');
        Name(this.output, "dropped", guard);
        this.output.Write(":\n");
    }
}
