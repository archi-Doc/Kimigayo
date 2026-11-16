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
// - The library's node columns (Binding's `symbols`, its parameters' composite column, `scopes`, `originAtoms`, `openOrigins`,
//   `associatedOrigins`, `specialReceivers`, `enumConstructions`, `copyDeclarations`, `copyByType`, `inheritedOriginTypes`,
//   `deferredConstraints`, `matches`, `adaptations`, `specializations`, `specializationsByOriginal`,
//   `originDeclarations`, `discoveredOrigins` and `originRequirementNodes`,
//   DECISIONS BU-220): the library
//   module's own columns, which no program row enters (a program's rows are its modules'), so each starts at its stated slots
//   (PagedColumn.Reserve) and has no room; its count is the slots the library's pages hold, rounded up to ColumnGranule, and its free
//   slots are its arena's.
// - Inputs: none; the counts are stated here and KimiLibraryShapeTest compares them with what a Bind of the empty program fills
//   (Binding.TableCounts), so a library change that moves a count past its Granule fails there and restates it.
// - Outputs: Stated (a library-only table starts at its count there), NodeRoom, KeyedRoom, NodeCapacity, KeyedCapacity.
// - Invariants: each stated count is the library's count rounded up to the Granule, a node column's to the ColumnGranule
//   (Tables.Rounded), so after a Bind of the library
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

    /// <summary>The unit a node column's stated slots are rounded up to: 64 pages.</summary>
    internal const int ColumnGranule = 64 * PagedColumn<object>.PageSize;

    /// <summary>The library's entries in each sized table; a library-only table starts at its count here.</summary>
    internal static readonly Tables Stated = new(
        Nodes: 16384,
        Declarations: 7680,
        Parameters: 3072,
        Scopes: 8704,
        OriginAtoms: 3072,
        OpenOrigins: 2048,
        AssociatedOrigins: 512,
        Receivers: 512,
        EnumConstructions: 1536,
        CopyDeclarations: 512,
        CopyByType: 512,
        InheritedOriginTypes: 512,
        DeferredConstraints: 1536,
        Matches: 512,
        Adaptations: 512,
        Specializations: 512,
        SpecializationsByOriginal: 512,
        OriginDeclarations: 512,
        DiscoveredOrigins: 1024,
        OriginRequirementNodes: 1536,
        TypeKeys: 3072,
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
        int Declarations, // The library's symbols column: the slots its pages hold.
        int Parameters, // The library's parameters column (the symbols' composite column): the slots its pages hold.
        int Scopes, // The library's scopes column: the slots its pages hold.
        int OriginAtoms, // The library's originAtoms column: the slots its pages hold.
        int OpenOrigins, // The library's openOrigins column: the slots its pages hold.
        int AssociatedOrigins, // The library's associatedOrigins column: the slots its pages hold.
        int Receivers, // The library's specialReceivers column: the slots its pages hold.
        int EnumConstructions, // The library's enumConstructions column: the slots its pages hold.
        int CopyDeclarations, // The library's copyDeclarations column: the slots its pages hold.
        int CopyByType, // The library's copyByType column: the slots its pages hold.
        int InheritedOriginTypes, // The library's inheritedOriginTypes column: the slots its pages hold.
        int DeferredConstraints, // The library's deferredConstraints column: the slots its pages hold.
        int Matches, // The library's matches column: the slots its pages hold.
        int Adaptations, // The library's adaptations column: the slots its pages hold.
        int Specializations, // The library's specializations column: the slots its pages hold.
        int SpecializationsByOriginal, // The library's specializationsByOriginal column: the slots its pages hold.
        int OriginDeclarations, // The library's originDeclarations column: the slots its pages hold.
        int DiscoveredOrigins, // The library's discoveredOrigins column: the slots its pages hold.
        int OriginRequirementNodes, // The library's originRequirementNodes column: the slots its pages hold.
        int TypeKeys, // TyCtxt's keys of the interned Types.
        int Constraints, // Binding's interned constraints.
        int AssociatedTypes, // Binding's associated-Type answers.
        int MemberSelections, // Binding's member selections: library-only.
        int MemberSides, // Binding's member sides: library-only.
        int CapabilityNodes, // Binding's capability nodes: library-only.
        int SubstitutedParts) // Binding's substituted parts.
    {
        /// <summary>Gets the least number among the keyed tables with a room: every keyed table but the library-only three, and no
        /// node column.</summary>
        internal int LeastKeyed
            => Math.Min(Math.Min(this.TypeKeys, this.Constraints), Math.Min(this.AssociatedTypes, this.SubstitutedParts));

        /// <summary>Gives these counts as the statement writes them.</summary>
        /// <returns>Each count rounded up to the Granule, a node column's to the ColumnGranule.</returns>
        internal Tables Rounded()
            => new(
                Round(this.Nodes, Granule),
                Round(this.Declarations, ColumnGranule),
                Round(this.Parameters, ColumnGranule),
                Round(this.Scopes, ColumnGranule),
                Round(this.OriginAtoms, ColumnGranule),
                Round(this.OpenOrigins, ColumnGranule),
                Round(this.AssociatedOrigins, ColumnGranule),
                Round(this.Receivers, ColumnGranule),
                Round(this.EnumConstructions, ColumnGranule),
                Round(this.CopyDeclarations, ColumnGranule),
                Round(this.CopyByType, ColumnGranule),
                Round(this.InheritedOriginTypes, ColumnGranule),
                Round(this.DeferredConstraints, ColumnGranule),
                Round(this.Matches, ColumnGranule),
                Round(this.Adaptations, ColumnGranule),
                Round(this.Specializations, ColumnGranule),
                Round(this.SpecializationsByOriginal, ColumnGranule),
                Round(this.OriginDeclarations, ColumnGranule),
                Round(this.DiscoveredOrigins, ColumnGranule),
                Round(this.OriginRequirementNodes, ColumnGranule),
                Round(this.TypeKeys, Granule),
                Round(this.Constraints, Granule),
                Round(this.AssociatedTypes, Granule),
                Round(this.MemberSelections, Granule),
                Round(this.MemberSides, Granule),
                Round(this.CapabilityNodes, Granule),
                Round(this.SubstitutedParts, Granule));

        private static int Round(int count, int granule) => (count + granule - 1) / granule * granule;
    }
}
