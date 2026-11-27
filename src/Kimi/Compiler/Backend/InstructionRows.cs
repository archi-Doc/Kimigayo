// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the machine instructions of the Kimi.X86 family (IMPL 21.6; the Tatara plan's decisions 36-43, C-457 4.3-4.4): one row per member
// of IntrinsicId.Instruction, the member a library declaration's `#Intrinsic("Instruction", "<member>")` names (IntrinsicDisposition's
// Member): its LLVM inline-asm template, its constraints and its routine, `__kimi_x86_<member>`, which every call of the member calls (a
// direct call, an Item's Value call and an adapter's entry, through FunctionCodegen's Routine) and the module defines once on request,
// `alwaysinline` (ModuleCodegen.Frame's Instructions).
// Input: a member name. Output: its row's index (Find) and the row (Row).
// Invariants: one row per member, at most 64 (the module's request set); every row's constraints end with the one suffix (Clobbers),
// which the one definition writer appends; a routine's parameters are its declaration's, in order, without a location, under the
// inline attribute (Abi.Flags.Inline).
// Must not: read Binding beyond the disposition's member, keep a per-row clobber, or write call text (FunctionCodegen's one call writer).

namespace Kimi.Compiler.Backend;

/// <summary>A machine instruction: its member, its inline-asm template, its constraints before <see cref="InstructionRows.Clobbers"/>,
/// and its routine.</summary>
/// <param name="Member">The member its declarations name.</param>
/// <param name="Template">The LLVM inline-asm template, its instructions separated by <c>\0A</c>.</param>
/// <param name="Constraints">The result's and the operands' constraints, in the routine's order; empty for none.</param>
/// <param name="Routine">The routine every call calls and the module defines on request.</param>
internal readonly record struct InstructionRow(string Member, string Template, string Constraints, FunctionAbi Routine);

/// <summary>The machine instruction table of the Backend: one row per member of <see cref="IntrinsicId.Instruction"/>.</summary>
internal static class InstructionRows
{
    /// <summary>The suffix every row's constraints end with (Clang's x86 default): the compiler barrier and x86's direction flag, x87
    /// status and flags, none of which LLVM's backend adds, and which some templates change.</summary>
    internal const string Clobbers = "~{memory},~{dirflag},~{fpsr},~{flags}";

    private static readonly AbiParameter Port = new("i16", "port", AbiParameterKind.Value, 0);

    private static readonly InstructionRow[] Rows = Complete(
    [
        Define("pause", "pause", string.Empty, "void"),
        Define("rdtsc", "rdtsc\\0Ashlq $$32, %rdx\\0Aorq %rdx, %rax", "={ax},~{dx}", "i64"),
        Define("hlt", "hlt", string.Empty, "void"),
        Define("cli", "cli", string.Empty, "void"),
        Define("sti", "sti", string.Empty, "void"),
        Define("inb", "inb %dx, %al", "={ax},{dx}", "i8", Port),
        Define("inw", "inw %dx, %ax", "={ax},{dx}", "i16", Port),
        Define("inl", "inl %dx, %eax", "={ax},{dx}", "i32", Port),
        Define("outb", "outb %al, %dx", "{dx},{ax}", "void", Port, new("i8", "value", AbiParameterKind.Value, 1)),
        Define("outw", "outw %ax, %dx", "{dx},{ax}", "void", Port, new("i16", "value", AbiParameterKind.Value, 1)),
        Define("outl", "outl %eax, %dx", "{dx},{ax}", "void", Port, new("i32", "value", AbiParameterKind.Value, 1)),
    ]);

    /// <summary>Gets the index of a member's row.</summary>
    /// <param name="member">The member a declaration's disposition names, if any.</param>
    /// <returns>The row's index, or -1 when the member names no row.</returns>
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
    /// <param name="index">The row's index (<see cref="Find"/>).</param>
    /// <returns>The row.</returns>
    internal static InstructionRow Row(int index) => Rows[index];

    private static InstructionRow Define(string member, string template, string constraints, string result, params AbiParameter[] parameters)
        => new(member, template, constraints, new("__kimi_x86_" + member, result, parameters) { Flags = Abi.Flags.Inline });

    // The table, its members distinct and at most 64 rows (the module's request set, one bit per row); otherwise a broken contract.
    private static InstructionRow[] Complete(InstructionRow[] rows)
    {
        var complete = rows.Length <= 64;
        for (var i = 0; complete && i < rows.Length; i++)
        {
            complete = Array.FindIndex(rows, x => x.Member == rows[i].Member) == i;
        }

        return complete ? rows : throw new InvalidOperationException("The instruction table needs one row per member, at most 64.");
    }
}
