// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// KimiLibrarySize (DECISIONS R8-M28): the one statement of the embedded library's size, by which every compilation sizes the tables
// that hold the library's entries beside the program's.
// - Responsibility: the library's entry count in each sized table (Stated), the room a program has in a table of each kind (NodeRoom
//   in the node table, KeyedRoom in a keyed table, none in a library-only table) and the capacity a table starts at (NodeCapacity,
//   KeyedCapacity; a library-only table starts at its stated count). A per-compilation table is sized when, grown by doubling from
//   empty, the doubling that follows the library's fill would allocate more than 64 KB (its new slots times its bytes per slot):
//   three such doublings together stay inside the smallest margin of the cold scaling rows.
// - Limit, the library-only kind (member selections and sides, capability nodes): chosen because no cold scaling row's program adds an
//   entry to any of them, which is its reason and no guarantee. Programs fill them: a member access adds one member selection and one
//   side, a Copy struct declaration two capability nodes. So each table's next doubling (139,980 B, about 76 KB and 102,652 B) sits
//   where the library's fill puts it, and a row over such a form can take it in one window: a member-read row of the tests' shape
//   (cold Bind bytes at 128, 256 and 512 reads, factor 3) measures 1.100 on the capacities before this statement and 3.118 on these
//   (before the member sides). The RP8 exception
//   (the owner's O-136 (a) and O-139 (a)) covers this until the removal condition holds.
// - Inputs: none; the counts are stated here and KimiLibraryShapeTest compares them with what a Bind of the empty program fills
//   (Binding.TableCounts), so a library change that moves a count past its Granule fails there and restates it.
// - Outputs: Stated (a library-only table starts at its count there), NodeRoom, KeyedRoom, NodeCapacity, KeyedCapacity.
// - Invariants: each stated count is the library's count rounded up to the Granule (Tables.Rounded), so after a Bind of the library
//   alone each table with a room keeps at least that room free (Binding.TableFree; KimiLibraryShapeTest): a program that adds fewer
//   entries than the room never resizes the table, whatever the library holds, and a change of the library moves no resize into such
//   a program. The rooms size the tables and bound no program: a program past a room, or one that fills a library-only table's
//   free slots, may double that table.
// - Prohibitions: no count measured at run time and no mutable static; no table states a capacity or a room of its own.
// - Removal condition: R8-L1 (a frozen library: no compilation rebuilds tables holding the library's entries), if RQ13 adopts it
//   (the plan's RQ13 thresholds). Until R8-L1 removes it, this statement, its two tests and the RP8 exception stand.
internal static class KimiLibrarySize
{
    /// <summary>The room of the node table: a program of up to this many indexed nodes does not resize it.</summary>
    internal const int NodeRoom = 8192;

    /// <summary>The room of a keyed table: a program that adds up to this many entries to one does not resize it. A sizing fact, not a
    /// contract: by its shape a program adds from none to one entry per node to a keyed table, and past the room it may double that table.</summary>
    internal const int KeyedRoom = 1024;

    /// <summary>The unit the stated counts are rounded up to.</summary>
    internal const int Granule = 1024;

    /// <summary>The library's entries in each sized table; a library-only table starts at its count here.</summary>
    internal static readonly Tables Stated = new(
        Nodes: 15360,
        Declarations: 2048,
        Scopes: 2048,
        TypeKeys: 2048,
        Constraints: 2048,
        AssociatedTypes: 2048,
        MemberSelections: 1024,
        MemberSides: 1024,
        CapabilityNodes: 1024,
        SubstitutedParts: 1024);

    /// <summary>Gives the capacity the node table starts at.</summary>
    /// <param name="stated">The library's stated nodes.</param>
    /// <returns>The stated count and the node room.</returns>
    internal static int NodeCapacity(int stated) => stated + NodeRoom;

    /// <summary>Gives the capacity a keyed table with a room starts at.</summary>
    /// <param name="stated">The library's stated entries in the table.</param>
    /// <returns>The stated count and the keyed room.</returns>
    internal static int KeyedCapacity(int stated) => stated + KeyedRoom;

    /// <summary>One number per sized table: the statement, a Binding's entries (Binding.TableCounts) or its free slots (Binding.TableFree).</summary>
    internal readonly record struct Tables(
        int Nodes, // Binding's nodes: the node table.
        int Declarations, // Binding's symbols.
        int Scopes, // Binding's scopes.
        int TypeKeys, // TyCtxt's keys of the interned Types.
        int Constraints, // Binding's interned constraints.
        int AssociatedTypes, // Binding's associated-Type answers.
        int MemberSelections, // Binding's member selections: library-only.
        int MemberSides, // Binding's member sides: library-only.
        int CapabilityNodes, // Binding's capability nodes: library-only.
        int SubstitutedParts) // Binding's substituted parts.
    {
        /// <summary>Gets the least number among the keyed tables with a room: every keyed table but the library-only three.</summary>
        internal int LeastKeyed
            => Math.Min(
                Math.Min(Math.Min(this.Declarations, this.Scopes), Math.Min(this.TypeKeys, this.Constraints)),
                Math.Min(this.AssociatedTypes, this.SubstitutedParts));

        /// <summary>Gives these counts as the statement writes them.</summary>
        /// <returns>Each count rounded up to the Granule.</returns>
        internal Tables Rounded()
            => new(
                Round(this.Nodes),
                Round(this.Declarations),
                Round(this.Scopes),
                Round(this.TypeKeys),
                Round(this.Constraints),
                Round(this.AssociatedTypes),
                Round(this.MemberSelections),
                Round(this.MemberSides),
                Round(this.CapabilityNodes),
                Round(this.SubstitutedParts));

        private static int Round(int count) => (count + Granule - 1) / Granule * Granule;
    }
}
