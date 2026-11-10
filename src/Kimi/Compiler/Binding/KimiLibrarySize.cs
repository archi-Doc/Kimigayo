// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// KimiLibrarySize: the one statement of the embedded library's size, by which every compilation sizes the tables that hold the
// library's entries beside the program's.
// - Responsibility: the library's entry count in each sized table (Stated), the room a program has in a table of each kind (NodeRoom
//   in the node table, KeyedRoom in a keyed table, none in a library-only table) and the capacity a table starts at (NodeCapacity,
//   KeyedCapacity; a library-only table starts at its stated count). A per-compilation table is sized when, grown by doubling from
//   empty, the doubling that follows the library's fill would allocate more than 64 KB (its new slots times its bytes per slot):
//   three such doublings together stay inside the smallest margin of the cold scaling rows. A sized table is library-only when no
//   cold scaling row's program adds an entry to it.
// - Inputs: none; the counts are stated here and KimiLibraryShapeTest compares them with what a Bind of the empty program fills
//   (Binding.TableCounts), so a library change that moves a count past its Granule fails there and restates it.
// - Outputs: Stated, NodeRoom, KeyedRoom, NodeCapacity, KeyedCapacity.
// - Invariants: each stated count is the library's count rounded up to the Granule (Tables.Rounded), so after a Bind of the library
//   alone each table with a room keeps at least that room free (Binding.TableFree; KimiLibraryShapeTest): a program that adds fewer
//   entries than the room never resizes the table, whatever the library holds, and a change of the library moves no resize into such
//   a program. The rooms size the tables and bound no program: a program past a room, or one that fills a library-only table's
//   free slots, doubles that table once more.
// - Prohibitions: no count measured at run time and no mutable static; no table states a capacity or a room of its own.
// - Removal condition: R8-L1, if RQ13 adopts a frozen library (the program's tables then hold the program's entries only).
internal static class KimiLibrarySize
{
    /// <summary>The room of the node table: a program of up to this many indexed nodes does not resize it.</summary>
    internal const int NodeRoom = 8192;

    /// <summary>The room of a keyed table: a program that adds up to this many entries to one does not resize it. A sizing fact, not a
    /// contract: by its shape a program adds from none to one entry per node to a keyed table, and past the room it doubles that table.</summary>
    internal const int KeyedRoom = 1024;

    /// <summary>The unit the stated counts are rounded up to.</summary>
    internal const int Granule = 1024;

    /// <summary>The library's entries in each sized table.</summary>
    internal static readonly Tables Stated = new(
        Nodes: 14336,
        Declarations: 2048,
        Scopes: 2048,
        TypeKeys: 2048,
        Constraints: 2048,
        AssociatedTypes: 2048,
        MemberSelections: 1024,
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
        int CapabilityNodes, // Binding's capability nodes: library-only.
        int SubstitutedParts) // Binding's substituted parts.
    {
        /// <summary>Gets the least number among the keyed tables with a room: every keyed table but the library-only two.</summary>
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
                Round(this.CapabilityNodes),
                Round(this.SubstitutedParts));

        private static int Round(int count) => (count + Granule - 1) / Granule * Granule;
    }
}
