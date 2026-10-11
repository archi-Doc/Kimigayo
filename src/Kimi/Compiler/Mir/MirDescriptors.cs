// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the interpretation of MIR operations, their accesses and successor enumeration.
// Input: closed MIR kinds and indexed body records. Output: descriptor rows and MirUses walks.
// Validator and analyses share these rows; operand/effect meaning is decided once.
// Must not add per-consumer copies of access or control-flow classification.

#pragma warning disable SA1402 // The descriptor table, its flags and the use enumeration belong together.

namespace Kimi.Compiler.Mir;

/// <summary>How a statement, rvalue, terminator, operand or projection uses a Place (plan 5.5).</summary>
[Flags]
internal enum MirAccess : ushort
{
    None = 0,
    Reads = 1 << 0, // A Copy, FakeRead, Discriminant or Len, or an index local.
    Writes = 1 << 1, // An Assign's or a returning Call's destination.
    Moves = 1 << 2, // A Move operand, or local 0 at a Return.
    Borrows = 1 << 3, // A Ref, of its MirRefKind.
    Defines = 1 << 4, // The Place is initialized afterwards.
    Kills = 1 << 5, // StorageLive and StorageDead end a local's value and Loans; EndBorrow ends the Loans a reference local holds.
    DropUse = 1 << 6, // A Drop destroys the value.
    CaseDependent = 1 << 7, // A pair binder's Semantics case decides the operation: a PairAcquire, or a Place through a PairLayer.
}

/// <summary>An effect of a location, the local facts of the effect summary (plan 5.5, SPEC 8.4.10.2).</summary>
[Flags]
internal enum MirEffect : ushort
{
    None = 0,
    StaticRead = 1 << 0, // A static read or shared-borrowed.
    StaticWrite = 1 << 1, // A static written, moved, destroyed or exclusively borrowed.
    StaticInit = 1 << 2, // staticEnsure: the static's first-access initialization, an edge to its StaticInit body.
    RawStaticRead = 1 << 3, // An immutable static whose value holds a raw pointer, an rc handle or a Weak of one, read.
    IntToPointer = 1 << 4,
    Environment = 1 << 5, // State outside the program.
    Abort = 1 << 6, // A failed check or an abort.
    Allocation = 1 << 7,
    Unknown = 1 << 8, // An Opaque value, caused by its site.
}

/// <summary>A row of the descriptor table: the access a kind makes, its effects, and for an intrinsic its arity (-1: its lowering
/// defines it later) and whether it diverges. A kind without a row is not <see cref="Defined"/>.</summary>
internal readonly record struct MirDescriptor(bool Defined, MirAccess Access, MirEffect Effects, sbyte Arity, bool Diverges);

/// <summary>A position in a body: the statement at an index of a block, or its terminator at the index after the last statement.</summary>
internal readonly record struct MirLocation(int Block, int Index);

/// <summary>What <see cref="MirUses.ForEach"/> reports.</summary>
internal interface IMirUses
{
    /// <summary>An operand the location evaluates, reported before the Place it names.</summary>
    /// <param name="operand">The operand.</param>
    void Operand(in MirOperand operand);

    /// <summary>A Place the location uses, an index local of a Place included.</summary>
    /// <param name="place">The Place.</param>
    /// <param name="access">Its access, CaseDependent through a PairLayer.</param>
    /// <param name="kind">A borrow's kind.</param>
    void Place(MirPlace place, MirAccess access, MirRefKind kind);

    /// <summary>Effects of the location.</summary>
    /// <param name="effects">The effects.</param>
    /// <param name="site">The Opaque site of an Unknown effect, else -1.</param>
    void Effect(MirEffect effects, int site);
}

/// <summary>The descriptor table (plan 5.4): one row per statement, rvalue, terminator, operand, projection and intrinsic kind. The analyses
/// enumerate operands and effects through it (<see cref="MirUses"/>), and <see cref="MirValidator"/> rejects a kind without a row.</summary>
internal static class MirDescriptors
{
    internal static MirDescriptor Of(MirStatementKind kind) => kind switch
    {
        MirStatementKind.Assign => Row(MirAccess.Writes | MirAccess.Defines),
        MirStatementKind.StorageLive or MirStatementKind.StorageDead or MirStatementKind.EndBorrow => Row(MirAccess.Kills),
        MirStatementKind.FakeRead => Row(MirAccess.Reads),
        _ => default,
    };

    // The access to the rvalue's Place: a Ref's, Discriminant's or Len's, or an Opaque site's roots, which the replaced syntax may have
    // read, written or borrowed. An operator or a checked Cast aborts unless Proven.
    internal static MirDescriptor Of(MirRvalueKind kind) => kind switch
    {
        MirRvalueKind.Use or MirRvalueKind.Aggregate => Row(MirAccess.None),
        MirRvalueKind.Ref => Row(MirAccess.Borrows),
        MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp or MirRvalueKind.Cast => Row(MirAccess.None, MirEffect.Abort),
        MirRvalueKind.Discriminant or MirRvalueKind.Len => Row(MirAccess.Reads),
        MirRvalueKind.Opaque => Row(MirAccess.Reads | MirAccess.Writes | MirAccess.Borrows, MirEffect.Unknown),
        _ => default,
    };

    // The access to a Call's destination, the dropped Place or local 0 at a Return; a SwitchInt's operand and a Call's arguments have their
    // operand rows, and a Call's effects are its intrinsic's or its callee's summary.
    internal static MirDescriptor Of(MirTerminatorKind kind) => kind switch
    {
        MirTerminatorKind.Goto or MirTerminatorKind.SwitchInt or MirTerminatorKind.Unreachable => Row(MirAccess.None),
        MirTerminatorKind.Call => Row(MirAccess.Writes | MirAccess.Defines),
        MirTerminatorKind.Drop => Row(MirAccess.DropUse),
        MirTerminatorKind.Return => Row(MirAccess.Moves),
        _ => default,
    };

    internal static MirDescriptor Of(MirOperandKind kind) => kind switch
    {
        MirOperandKind.Copy => Row(MirAccess.Reads),
        MirOperandKind.Move => Row(MirAccess.Moves),
        MirOperandKind.PairAcquire => Row(MirAccess.Reads | MirAccess.CaseDependent),
        MirOperandKind.Const => Row(MirAccess.None),
        _ => default,
    };

    // Index reads its local and aborts out of bounds; PairLayer makes the use case-dependent.
    internal static MirDescriptor Of(MirProjectionKind kind) => kind switch
    {
        MirProjectionKind.Deref or MirProjectionKind.Field or MirProjectionKind.Base or MirProjectionKind.ConstIndex or MirProjectionKind.Downcast =>
            Row(MirAccess.None),
        MirProjectionKind.Index => Row(MirAccess.Reads, MirEffect.Abort),
        MirProjectionKind.PairLayer => Row(MirAccess.CaseDependent),
        _ => default,
    };

    // The intrinsic table's rows (plan 5.8): arguments as the library declares them, Fail's being its SPEC 22.5.4 code as a string constant.
    internal static MirDescriptor Of(IntrinsicId id) => id switch
    {
        IntrinsicId.RawAllocate or IntrinsicId.RawRelease or IntrinsicId.TryAllocateBytes or IntrinsicId.MakeObj or IntrinsicId.MakeRc or
            IntrinsicId.MakeArc => Row(MirAccess.None, MirEffect.Allocation, 1),
        IntrinsicId.RawInitialize or IntrinsicId.RawSlice => Row(MirAccess.None, MirEffect.None, 2),
        IntrinsicId.MoveBytes => Row(MirAccess.None, MirEffect.None, 3),
        IntrinsicId.Stride => Row(MirAccess.None, MirEffect.None, 0),
        // Capacity: a handle's view.
        IntrinsicId.ReinterpretLoan or IntrinsicId.ErasedWriter or IntrinsicId.Clone or IntrinsicId.Capacity => Row(MirAccess.None, MirEffect.None, 1),
        IntrinsicId.TypeTest => Row(MirAccess.None, MirEffect.None, 2), // The payload's shared view and the target Type's token (SPEC 13.6.1).
        IntrinsicId.DestroyValues or IntrinsicId.FormatFloat or IntrinsicId.ErasedReserve or IntrinsicId.FormatInteger128 => Row(MirAccess.None),
        IntrinsicId.Abort or IntrinsicId.Fail => Row(MirAccess.None, MirEffect.Abort, 1, true),
        IntrinsicId.TestRequireAbort => Row(MirAccess.None, MirEffect.Abort, -1, true),
        IntrinsicId.WriteStdout or IntrinsicId.TestMessage or IntrinsicId.TestRecordValue => Row(MirAccess.None, MirEffect.Environment),
        IntrinsicId.TestObserve => Row(MirAccess.None, MirEffect.Environment, 2), // The condition and the site's id (plan 5.6.7).
        IntrinsicId.TestPhaseEnter or IntrinsicId.TestPhaseLeave => Row(MirAccess.None, MirEffect.Environment, 1),
        IntrinsicId.TestTempDirectory => Row(MirAccess.None, MirEffect.Environment, 0),
        IntrinsicId.StaticEnsure => Row(MirAccess.None, MirEffect.StaticInit | MirEffect.Abort, 1), // The static's slot (plan 5.6.6).
        IntrinsicId.FormatBuffer => Row(MirAccess.None, MirEffect.Allocation, 3), // The literal bytes, the values' Types' token, the direct mark.
        IntrinsicId.FormatHint => Row(MirAccess.None, MirEffect.None, 3), // The adapter, then the later literal bytes and values' Types' token.
        IntrinsicId.FormatFinish => Row(MirAccess.None, MirEffect.Allocation | MirEffect.Abort, 4), // The adapter, then FormatBuffer's operands.
        _ => default,
    };

    private static MirDescriptor Row(MirAccess access, MirEffect effects = MirEffect.None, sbyte arity = -1, bool diverges = false) => new(
        true,
        access,
        effects,
        arity,
        diverges);
}

/// <summary>The one enumeration of what a location reads, writes, moves, borrows, defines, kills and destroys, and of its effects, by the
/// descriptor rows; the validator and the analyses share it, and a struct visitor makes it allocate nothing.</summary>
internal static class MirUses
{
    /// <summary>Reports the operands, Places and effects of a location: a statement's rvalue before its Place, a terminator's operands
    /// before its destination, which a diverging Call does not write.</summary>
    /// <typeparam name="TVisitor">The visitor's struct type.</typeparam>
    /// <param name="body">The body.</param>
    /// <param name="location">The location.</param>
    /// <param name="visitor">The visitor.</param>
    internal static void ForEach<TVisitor>(MirBody body, MirLocation location, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        ref readonly var block = ref body.Blocks[location.Block];
        if (location.Index < block.Count)
        {
            ref readonly var statement = ref body.Statements[block.Start + location.Index];
            if (statement.Kind == MirStatementKind.Assign)
            {
                Rvalue(body, statement.Rvalue, ref visitor);
            }

            Place(body, statement.Place, MirDescriptors.Of(statement.Kind).Access, default, ref visitor);
            return;
        }

        var terminator = block.Terminator;
        var access = MirDescriptors.Of(terminator.Kind).Access;
        if (terminator.Kind == MirTerminatorKind.SwitchInt || (terminator.Kind == MirTerminatorKind.Call && terminator.Callee.Kind == MirCalleeKind.Value))
        {
            Operand(body, terminator.Operand, ref visitor);
        }

        if (terminator.Kind == MirTerminatorKind.Call)
        {
            Operands(body, terminator.Start, terminator.Count, ref visitor);
            if (terminator.Callee.Kind == MirCalleeKind.Intrinsic)
            {
                Effect(MirDescriptors.Of((IntrinsicId)terminator.Callee.Arg).Effects, -1, ref visitor);
            }
        }

        if ((terminator.Kind == MirTerminatorKind.Call && terminator.Target >= 0) || terminator.Kind == MirTerminatorKind.Drop)
        {
            Place(body, terminator.Place, access, default, ref visitor);
        }
        else if (terminator.Kind == MirTerminatorKind.Return)
        {
            Place(body, new(0), access, default, ref visitor);
        }
    }

    /// <summary>Gets the number of runtime successors of a terminator; <see cref="Successor"/> gives each, and its CheckTarget is the
    /// successor only the checks reach.</summary>
    /// <param name="terminator">The terminator.</param>
    /// <returns>The number of runtime successors.</returns>
    internal static int Successors(in MirTerminator terminator) => terminator.Kind switch
    {
        MirTerminatorKind.Goto or MirTerminatorKind.Drop => 1,
        MirTerminatorKind.Call => terminator.Target >= 0 ? 1 : 0,
        MirTerminatorKind.SwitchInt => terminator.Count + 1,
        _ => 0,
    };

    /// <summary>Gets a runtime successor of a terminator: a SwitchInt's case targets in order, then its otherwise target.</summary>
    /// <param name="body">The body.</param>
    /// <param name="terminator">The terminator.</param>
    /// <param name="index">The successor's index, below <see cref="Successors"/>.</param>
    /// <returns>The successor block.</returns>
    internal static int Successor(MirBody body, in MirTerminator terminator, int index)
        => terminator.Kind == MirTerminatorKind.SwitchInt && index < terminator.Count ? body.Cases[terminator.Start + index].Target : terminator.Target;

    private static void Rvalue<TVisitor>(MirBody body, in MirRvalue rvalue, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        var row = MirDescriptors.Of(rvalue.Kind);
        Operands(body, rvalue.Start, rvalue.Count, ref visitor);
        switch (rvalue.Kind)
        {
            case MirRvalueKind.Ref or MirRvalueKind.Discriminant or MirRvalueKind.Len:
                Place(body, rvalue.Place, row.Access, (MirRefKind)rvalue.Op, ref visitor);
                break;
            case MirRvalueKind.BinaryOp or MirRvalueKind.UnaryOp when !rvalue.Proven:
                Effect(row.Effects, -1, ref visitor);
                break;
            case MirRvalueKind.Cast:
                Effect(
                    (MirCastKind)rvalue.Op switch
                {
                    MirCastKind.IntToPointer => MirEffect.IntToPointer,
                    MirCastKind.Integer or MirCastKind.Floating or MirCastKind.Numeric when !rvalue.Proven => row.Effects,
                    _ => MirEffect.None,
                },
                    -1,
                    ref visitor);
                break;
            case MirRvalueKind.Opaque:
                Effect(row.Effects, rvalue.Arg, ref visitor);
                var site = body.OpaqueSites[rvalue.Arg];
                foreach (var root in body.OpaqueRoots.Slice(site.Start, site.Count))
                {
                    Place(body, new(root), row.Access, default, ref visitor);
                }

                break;
        }
    }

    private static void Operands<TVisitor>(MirBody body, int start, int count, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        for (var i = start; i < start + count; i++)
        {
            Operand(body, body.Operands[i], ref visitor);
        }
    }

    private static void Operand<TVisitor>(MirBody body, in MirOperand operand, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        visitor.Operand(operand);
        if (operand.Kind != MirOperandKind.Const)
        {
            Place(body, operand.Place, MirDescriptors.Of(operand.Kind).Access, default, ref visitor);
        }
    }

    // The Place, after the index locals of its chain; a chain id out of range ends the walk, which the validator reports.
    private static void Place<TVisitor>(MirBody body, MirPlace place, MirAccess access, MirRefKind kind, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        var projections = body.Projections;
        for (var id = place.Projection; id > 0 && id < projections.Length; id = projections[id].Prefix)
        {
            var row = MirDescriptors.Of(projections[id].Kind);
            access |= row.Access & MirAccess.CaseDependent;
            if (projections[id].Kind == MirProjectionKind.Index)
            {
                visitor.Place(new(projections[id].Arg), row.Access, default);
                Effect(row.Effects, -1, ref visitor);
            }
        }

        visitor.Place(place, access, kind);
        if (place.IsStatic && ~place.Root < body.Statics.Length)
        {
            ref readonly var entry = ref body.Statics[~place.Root];
            var writes = (access & (MirAccess.Writes | MirAccess.Moves | MirAccess.DropUse | MirAccess.Kills)) != 0 ||
                ((access & MirAccess.Borrows) != 0 && kind is MirRefKind.Uniq or MirRefKind.TwoPhaseUniq);
            MirEffect effect;
            if (writes)
            {
                effect = MirEffect.StaticWrite;
            }
            else
            {
                effect = entry.RawHolding && !entry.Mutable ? MirEffect.StaticRead | MirEffect.RawStaticRead : MirEffect.StaticRead;
            }

            Effect(effect, -1, ref visitor);
        }
    }

    private static void Effect<TVisitor>(MirEffect effects, int site, ref TVisitor visitor)
        where TVisitor : struct, IMirUses
    {
        if (effects != MirEffect.None)
        {
            visitor.Effect(effects, site);
        }
    }
}
