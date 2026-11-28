// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the interrupt entries of the Kimi.X86 family (IMPL 21.6; design C-457 C4, the owner's O-489): one row per member of
// IntrinsicId.InterruptEntry, the member a library declaration's `#Intrinsic("InterruptEntry", "<member>")` names (IntrinsicDisposition's
// Member): the lang item of the Contract whose `handle` the entry's thunk runs, and whether the processor pushes an error code, which the
// thunk passes to `handle` after the frame (`ptr byval(<frame>) %frame`, then `i64 %code`). A row's index is its variant, the
// MonoKey.Variant of the MonoKind.InterruptEntry item an entry call targets (InstanceCollector.Edges' Entry), whose thunk's signature
// (ModuleCodegen.Thunk) takes the code where Code holds.
// Input: a member name, or a variant. Output: the member's variant (Find) and the row (Row).
// Invariants: one row per member; each row's Contract has one requirement, `handle`, whose parameters are the frame and, where Code
// holds, the code (KimiLibraryShapeTest).
// Must not: read Binding beyond the disposition's member and the row's lang item, or write code (code generation's).

namespace Kimi.Compiler.Backend;

/// <summary>An interrupt entry: its member, the Contract whose <c>handle</c> its thunk runs and whether the processor pushes an error
/// code.</summary>
/// <param name="Member">The member its declaration names.</param>
/// <param name="Contract">The lang item of the handler's Contract.</param>
/// <param name="Code">Whether the processor pushes an error code, the thunk's second parameter.</param>
internal readonly record struct InterruptEntryRow(string Member, KimiDeclarationId Contract, bool Code);

/// <summary>The interrupt entry table of the Backend: one row per member of <see cref="IntrinsicId.InterruptEntry"/>.</summary>
internal static class InterruptEntries
{
    private static readonly InterruptEntryRow[] Rows =
    [
        new("plain", KimiDeclarationId.InterruptHandler, Code: false), // X86.entry.
        new("code", KimiDeclarationId.ErrorCodeHandler, Code: true), // X86.errorCodeEntry.
    ];

    /// <summary>Gets the variant of a member, its row's index.</summary>
    /// <param name="member">The member a declaration's disposition names, if any.</param>
    /// <returns>The variant, or -1 when the member names no row.</returns>
    internal static int Find(string? member)
    {
        for (var i = 0; i < Rows.Length; i++)
        {
            if (Rows[i].Member == member)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets a row.</summary>
    /// <param name="variant">The row's variant (<see cref="Find"/>).</param>
    /// <returns>The row.</returns>
    internal static InterruptEntryRow Row(int variant) => Rows[variant];
}
