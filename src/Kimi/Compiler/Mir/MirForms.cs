// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the MIR's forms (plan 5.4): the value records a body's tables hold (Places, operands, rvalues, statements, callees, terminators,
// blocks, locals, scopes, constants, projection steps, switch cases, statics, Opaque sites and failure bodies) and their constructors.
// Input: the kinds of MirVocabulary and body-local indices. Output: the values MirBody stores and its readers take from it.
// A form names its body's rows only by index (a static's root by its complement) and a run of rows by its start and count.
// Must not hold storage or decide what MirBody decides (interning, the Type of a Place) or what MirValidator checks.

#pragma warning disable SA1402 // The forms of a body belong together.

namespace Kimi.Compiler.Mir;

/// <summary>A Place (plan 5.4): a root, a local from 0 or a static (<c>~index</c> into <see cref="MirBody.Statics"/>), and its
/// projection chain, an id in <see cref="MirBody.Projections"/> (0: none).</summary>
/// <param name="Root">The local, or the complement of the static's index.</param>
/// <param name="Projection">The interned projection chain.</param>
internal readonly record struct MirPlace(int Root, int Projection = 0)
{
    internal bool IsStatic => this.Root < 0;
}

/// <summary>An operand: a Place copied, moved or acquired as a pair binder's Semantics case decides (<see cref="Arg"/>: the binder's
/// index in <see cref="MirBody.Binders"/>; only as the operand of its own Use, <see cref="MirRvalue.Acquire"/>), or a constant
/// (<see cref="Arg"/>: its id in <see cref="MirBody.Consts"/>).</summary>
internal readonly record struct MirOperand(MirOperandKind Kind, MirPlace Place, int Arg = 0)
{
    internal static MirOperand Copy(MirPlace place) => new(MirOperandKind.Copy, place);

    internal static MirOperand Move(MirPlace place) => new(MirOperandKind.Move, place);

    internal static MirOperand PairAcquire(int binder, MirPlace place) => new(MirOperandKind.PairAcquire, place, binder);

    internal static MirOperand Const(int id) => new(MirOperandKind.Const, default, id);
}

/// <summary>An rvalue. Its operands are <see cref="Count"/> entries of <see cref="MirBody.Operands"/> from <see cref="Start"/>;
/// <see cref="Op"/> is a Ref's <see cref="MirRefKind"/>, an operator's <see cref="MirBinaryOp"/> or <see cref="MirUnaryOp"/>, a Cast's
/// <see cref="MirCastKind"/>, an aggregate's <see cref="MirAggregateKind"/>, an Adapt's <see cref="AdaptShorthand"/> and
/// <see cref="AdaptInput"/> bits, or a Use's 0 but for a PairAcquire, which is always the whole rvalue of an Assign to a local and whose
/// Use holds the <see cref="MirRefKind"/> of the Reborrow its exclusive case makes (Uniq, TwoPhaseUniq for an invocation's input; design
/// A3 E13 (4)); <see cref="Arg"/> is an Adt aggregate's Case ordinal or an Opaque's site; <see cref="Place"/> is a Ref's, Discriminant's
/// or Len's Place or an Adapt's payload; <see cref="Type"/> is a Ref's reference Type, a Cast's or an Adapt's target Type or an
/// aggregate's or Opaque's Type.</summary>
internal readonly record struct MirRvalue(
    MirRvalueKind Kind,
    byte Op = 0,
    MirPlace Place = default,
    int Start = 0,
    int Count = 0,
    int Arg = 0,
    BoundType? Type = null,
    bool Proven = false)
{
    /// <summary>An Adapt's bit for the Semantics shorthand (SPEC 8.14).</summary>
    internal const byte AdaptShorthand = 1;

    /// <summary>An Adapt's bit for an invocation's input, whose exclusive borrow is two-phase (SPEC 15.6.7).</summary>
    internal const byte AdaptInput = 2;

    internal static MirRvalue Use(int operand) => new(MirRvalueKind.Use, Start: operand, Count: 1);

    // A PairAcquire's Use, with the borrow its exclusive case makes.
    internal static MirRvalue Acquire(int operand, MirRefKind kind) => new(MirRvalueKind.Use, (byte)kind, Start: operand, Count: 1);

    internal static MirRvalue Ref(MirRefKind kind, MirPlace place, BoundType type) => new(MirRvalueKind.Ref, (byte)kind, place, Type: type);

    // The two operands from the first.
    internal static MirRvalue Binary(MirBinaryOp op, int operands, bool proven) => new(
        MirRvalueKind.BinaryOp,
        (byte)op,
        Start: operands,
        Count: 2,
        Proven: proven);

    internal static MirRvalue Unary(MirUnaryOp op, int operand, bool proven) => new(MirRvalueKind.UnaryOp, (byte)op, Start: operand, Count: 1, Proven: proven);

    internal static MirRvalue Cast(MirCastKind kind, int operand, BoundType type) => new(MirRvalueKind.Cast, (byte)kind, Start: operand, Count: 1, Type: type);

    internal static MirRvalue Aggregate(MirAggregateKind kind, int start, int count, BoundType type, int ordinal = 0) => new(
        MirRvalueKind.Aggregate,
        (byte)kind,
        default,
        start,
        count,
        ordinal,
        type);

    internal static MirRvalue Discriminant(MirPlace place) => new(MirRvalueKind.Discriminant, Place: place);

    internal static MirRvalue Len(MirPlace place) => new(MirRvalueKind.Len, Place: place);

    internal static MirRvalue Opaque(BoundType type, int site) => new(MirRvalueKind.Opaque, Arg: site, Type: type);

    // From the first, the operand acquired by value and the token of Binding's source Type; the payload is the Place one implicit layer
    // below the operand's, which an object borrow borrows.
    internal static MirRvalue Adapt(int operands, MirPlace payload, BoundType target, bool shorthand, bool input) => new(
        MirRvalueKind.Adapt,
        (byte)((shorthand ? AdaptShorthand : 0) | (input ? AdaptInput : 0)),
        payload,
        operands,
        2,
        Type: target);
}

/// <summary>A statement: an Assign of its rvalue to its Place, or StorageLive, StorageDead, FakeRead or EndBorrow of its Place.</summary>
internal readonly record struct MirStatement(MirStatementKind Kind, MirPlace Place, MirRvalue Rvalue, SourceInfo Source)
{
    internal static MirStatement Assign(MirPlace place, MirRvalue rvalue, SourceInfo source) => new(MirStatementKind.Assign, place, rvalue, source);

    internal static MirStatement Of(MirStatementKind kind, MirPlace place, SourceInfo source) => new(kind, place, default, source);
}

/// <summary>What a Call calls: a Fn, Requirement or Virtual callee's function under its Subst (a Fn one's default template, plan 5.6.2, as
/// its index plus 1 in <see cref="Arg"/>; a Virtual one's slot by the slot's function; a Requirement's dispatching Contract reference in
/// <see cref="Contract"/>, null for its requirement's own Contract; a Requirement's <see cref="ComparisonMode"/> in <see cref="Arg"/>, 0 but
/// for a tuple operator's call, DECISIONS R5-M166) or an Intrinsic's <see cref="IntrinsicId"/> in <see cref="Arg"/>, its
/// <see cref="Symbol"/> the library declaration of the compiler function it serves, under its Subst, whose parameters its first operands
/// pass (those the builder adds follow them, MirDescriptors), or null for one without a library declaration, whose result holds no Loan
/// (A3 E14, DECISIONS R4-M63); a Value callee is the terminator's operand, the Callable signature its callee declares the TypeToken
/// constant at <see cref="Arg"/> (DECISIONS R4-M41).</summary>
internal readonly record struct MirCallee(MirCalleeKind Kind, BindingSymbol? Symbol = null, Subst? Subst = null, int Arg = 0, BindingSymbol? Contract = null)
{
    internal static MirCallee Intrinsic(IntrinsicId id, BindingSymbol? declaration = null, Subst? subst = null)
        => new(MirCalleeKind.Intrinsic, declaration, subst, (int)id);

    // SPEC 7.3: a call passes its callee's parameters receiver first (the self parameter at its written slot), then the others in slot order:
    // the slot an argument position passes, and its inverse.
    internal static int Slot(BindingSymbol function, int position)
    {
        if (function.ReceiverIndex is var receiver && (receiver < 0 || position > receiver))
        {
            return position;
        }

        return position == 0 ? receiver : position - 1;
    }

    internal static int Position(BindingSymbol? function, int slot)
    {
        if ((function?.ReceiverIndex ?? -1) is var receiver && (receiver < 0 || slot > receiver))
        {
            return slot;
        }

        return slot == receiver ? 0 : slot + 1;
    }
}

/// <summary>A terminator. <see cref="Target"/> is a Goto's, Drop's or Call's successor (-1: the Call diverges) or a SwitchInt's
/// otherwise, whose value-target pairs are <see cref="Count"/> entries of <see cref="MirBody.Cases"/> from <see cref="Start"/>, as a
/// Call's arguments are of <see cref="MirBody.Operands"/>. <see cref="Operand"/> is a SwitchInt's operand or a Value callee,
/// <see cref="Place"/> a Call's destination or the dropped Place, and <see cref="CheckTarget"/> the block only the checks reach
/// (-1: none). A new block's terminator is a Goto without a target until it is terminated.</summary>
internal readonly record struct MirTerminator(
    MirTerminatorKind Kind,
    SourceInfo Source,
    int Target = -1,
    int CheckTarget = -1,
    MirOperand Operand = default,
    MirPlace Place = default,
    int Start = 0,
    int Count = 0,
    MirCallee Callee = default)
{
    internal static MirTerminator Goto(int target, SourceInfo source, int check = -1) => new(MirTerminatorKind.Goto, source, target, check);

    internal static MirTerminator SwitchInt(MirOperand operand, int start, int count, int otherwise, SourceInfo source) =>
        new(MirTerminatorKind.SwitchInt, source, otherwise, Operand: operand, Start: start, Count: count);

    internal static MirTerminator Call(
        MirCallee callee,
        int start,
        int count,
        MirPlace destination,
        int target,
        SourceInfo source)
        => new(MirTerminatorKind.Call, source, target, Place: destination, Start: start, Count: count, Callee: callee);

    internal static MirTerminator Drop(MirPlace place, int target, SourceInfo source) => new(MirTerminatorKind.Drop, source, target, Place: place);

    internal static MirTerminator Return(SourceInfo source) => new(MirTerminatorKind.Return, source);

    internal static MirTerminator Unreachable(SourceInfo source, int check = -1) => new(MirTerminatorKind.Unreachable, source, CheckTarget: check);
}

/// <summary>A basic block: <see cref="Count"/> statements of <see cref="MirBody.Statements"/> from <see cref="Start"/>, then its terminator.</summary>
internal record struct MirBlock(int Start, int Count, MirTerminator Terminator);

/// <summary>A local: local 0 is the Return local, the Arg locals follow it (the receiver first), then Var, Temp and DropFlag locals.</summary>
internal readonly record struct MirLocal(
    BoundType Type,
    MirLocalKind Kind,
    SourceInfo Source,
    bool Mutable = false,
    string? Name = null,
    MirLocalRole Role = MirLocalRole.None);

/// <summary>A scope of the body; scope 0 is the body's own, and the scope a scope is in (<see cref="Outer"/>) precedes it. An Inlined scope's source
/// is its call.</summary>
internal readonly record struct MirScope(int Outer, MirScopeKind Kind, SourceRef Source);

/// <summary>A constant; interned, so equal constants share one id.</summary>
internal readonly record struct MirConst(MirConstKind Kind, BoundType Type, Int128 Bits = default, object? Value = null)
{
    /// <summary>Gets a Location constant's site, the SourceRef its bits hold as <c>Module &lt;&lt; 32 | Id</c>.</summary>
    internal SourceRef Site => new((ushort)(this.Bits >> 32), (int)(this.Bits & uint.MaxValue));
}

/// <summary>An interned projection step: its <see cref="Prefix"/>, the chain it extends (0: the root), its kind and argument (a Field index,
/// a base depth, an index local, a constant position, a Case ordinal or a pair binder's index) and the Type it selects
/// (null when its base does not admit it).
/// Chains are independent of their roots, so equal prefixes share ids.</summary>
internal readonly record struct MirProjection(int Prefix, MirProjectionKind Kind, int Arg, BoundType? Type);

internal readonly record struct MirSwitchCase(long Value, int Target);

/// <summary>A static the body's Places root at: its symbol and Type, whether it is mutable and whether its value holds a raw pointer, an
/// rc handle or a Weak of one at any depth (SPEC 8.11.2).</summary>
internal readonly record struct MirStatic(BindingSymbol Symbol, BoundType Type, bool Mutable, bool RawHolding);

/// <summary>The site of an Opaque value: the syntax it replaces and the roots that syntax reads, writes or borrows, <see cref="Count"/>
/// entries of <see cref="MirBody.OpaqueRoots"/> from <see cref="Start"/> (plan 5.4).</summary>
internal readonly record struct MirOpaque(SourceRef Source, int Start, int Count);

/// <summary>A failure body that must not complete (SPEC 14.11): entered at <see cref="Entry"/>, its end <see cref="End"/> an
/// Unreachable block without a check target that no runtime path from <see cref="Entry"/> may reach; <see cref="Subject"/> the failure
/// body.</summary>
internal readonly record struct MirMustNotComplete(int Entry, int End, SourceRef Subject);
