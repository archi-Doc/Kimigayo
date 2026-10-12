// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the indexed storage of one MIR body and its projection chains.
// Input: builder records. Output: reusable arrays of locals, blocks, statements and associated records.
// References use body-local indices; projection chains are interned and ProjectedType owns their Type rule.
// Must not resolve syntax or introduce a parallel representation for the same body.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

#pragma warning disable SA1402 // The row types of the body belong to it.

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
/// index in <see cref="MirBody.Binders"/>), or a constant (<see cref="Arg"/>: its id in <see cref="MirBody.Consts"/>).</summary>
internal readonly record struct MirOperand(MirOperandKind Kind, MirPlace Place, int Arg = 0)
{
    internal static MirOperand Copy(MirPlace place) => new(MirOperandKind.Copy, place);

    internal static MirOperand Move(MirPlace place) => new(MirOperandKind.Move, place);

    internal static MirOperand PairAcquire(int binder, MirPlace place) => new(MirOperandKind.PairAcquire, place, binder);

    internal static MirOperand Const(int id) => new(MirOperandKind.Const, default, id);
}

/// <summary>An rvalue. Its operands are <see cref="Count"/> entries of <see cref="MirBody.Operands"/> from <see cref="Start"/>;
/// <see cref="Op"/> is a Ref's <see cref="MirRefKind"/>, an operator's <see cref="MirBinaryOp"/> or <see cref="MirUnaryOp"/>, a Cast's
/// <see cref="MirCastKind"/>, an aggregate's <see cref="MirAggregateKind"/> or an Adapt's 1 for the Semantics shorthand;
/// <see cref="Arg"/> is an Adt aggregate's Case ordinal or an Opaque's site; <see cref="Place"/> is a Ref's, Discriminant's or Len's
/// Place or an Adapt's payload; <see cref="Type"/> is a Ref's reference Type, a Cast's or an Adapt's target Type or an aggregate's or
/// Opaque's Type.</summary>
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
    internal static MirRvalue Use(int operand) => new(MirRvalueKind.Use, Start: operand, Count: 1);

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
    internal static MirRvalue Adapt(int operands, MirPlace payload, BoundType target, bool shorthand) => new(
        MirRvalueKind.Adapt,
        (byte)(shorthand ? 1 : 0),
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
/// its index plus 1 in <see cref="Arg"/>; a Virtual one's slot in <see cref="Arg"/>; a Requirement's dispatching Contract reference in
/// <see cref="Contract"/>, null for its requirement's own Contract) or an Intrinsic's <see cref="IntrinsicId"/> in <see cref="Arg"/>; a
/// Value callee is the terminator's operand.</summary>
internal readonly record struct MirCallee(MirCalleeKind Kind, BindingSymbol? Symbol = null, Subst? Subst = null, int Arg = 0, BindingSymbol? Contract = null)
{
    internal static MirCallee Intrinsic(IntrinsicId id, Subst? subst = null) => new(MirCalleeKind.Intrinsic, null, subst, (int)id);

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
internal readonly record struct MirConst(MirConstKind Kind, BoundType Type, Int128 Bits = default, object? Value = null);

/// <summary>An interned projection step: its <see cref="Prefix"/>, the chain it extends (0: the root), its kind and argument (a Field index,
/// a base depth, an index local, a constant position, a Case ordinal or a pair binder's index) and the Type it selects
/// (null when its base does not admit it).
/// Chains are independent of their roots, so equal prefixes share ids.</summary>
internal readonly record struct MirProjection(int Prefix, MirProjectionKind Kind, int Arg, BoundType? Type);

internal readonly record struct MirSwitchCase(long Value, int Target);

/// <summary>A static the body's Places root at: its symbol and Type, whether it is mutable and whether its value holds a raw pointer, an
/// rc handle or a Weak of one at any depth (SPEC 8.4.10.2).</summary>
internal readonly record struct MirStatic(BindingSymbol Symbol, BoundType Type, bool Mutable, bool RawHolding);

/// <summary>The site of an Opaque value: the syntax it replaces and the roots that syntax reads, writes or borrows, <see cref="Count"/>
/// entries of <see cref="MirBody.OpaqueRoots"/> from <see cref="Start"/> (plan 5.4).</summary>
internal readonly record struct MirOpaque(SourceRef Source, int Start, int Count);

/// <summary>A failure body that must not complete (SPEC 14.11.2): entered at <see cref="Entry"/>, its end <see cref="End"/> an
/// Unreachable block without a check target that no runtime path from <see cref="Entry"/> may reach; <see cref="Subject"/> the failure
/// body.</summary>
internal readonly record struct MirMustNotComplete(int Entry, int End, SourceRef Subject);

/// <summary>The MIR of one body (plan 5.4): struct arrays that the builder appends to (<see cref="AddLocal"/>, <see cref="AddBlock"/>,
/// <see cref="Push"/>, <see cref="Terminate"/>, <see cref="AddOperand"/>, <see cref="Const"/>, <see cref="Project"/>,
/// <see cref="AddMustNotComplete"/> and the other adders)
/// and that a reset keeps, so a pooled body rebuilt to the same size allocates nothing.</summary>
internal sealed class MirBody
{
    private static readonly MirTerminator Unterminated = new(MirTerminatorKind.Goto, default);
    private readonly Dictionary<MirProjection, int> projectionIds = new();
    private readonly Dictionary<MirConst, int> constIds = new();
    private MirLocal[] locals = [];
    private MirBlock[] blocks = [];
    private MirStatement[] statements = [];
    private MirOperand[] operands = [];
    private MirSwitchCase[] cases = [];
    private MirConst[] consts = [];
    private MirProjection[] projections = [];
    private MirScope[] scopes = [];
    private MirStatic[] statics = [];
    private MirOpaque[] opaques = [];
    private int[] opaqueRoots = [];
    private MirMustNotComplete[] ends = [];
    private int localCount;
    private int blockCount;
    private int statementCount;
    private int operandCount;
    private int caseCount;
    private int constCount;
    private int projectionCount;
    private int scopeCount;
    private int staticCount;
    private int opaqueCount;
    private int opaqueRootCount;
    private int endCount;

    internal MirDefKey Key { get; private set; }

    /// <summary>Gets the syntax of the body, where a finding without a statement is located.</summary>
    internal SourceRef Source { get; private set; }

    /// <summary>Gets the pair binders in scope of the body (<see cref="PairBinder.Collect"/>), which PairLayer and PairAcquire name by index.</summary>
    internal List<PairBinder> Binders { get; } = [];

    internal ReadOnlySpan<MirLocal> Locals => this.locals.AsSpan(0, this.localCount);

    internal ReadOnlySpan<MirBlock> Blocks => this.blocks.AsSpan(0, this.blockCount);

    internal ReadOnlySpan<MirStatement> Statements => this.statements.AsSpan(0, this.statementCount);

    internal ReadOnlySpan<MirOperand> Operands => this.operands.AsSpan(0, this.operandCount);

    internal ReadOnlySpan<MirSwitchCase> Cases => this.cases.AsSpan(0, this.caseCount);

    internal ReadOnlySpan<MirConst> Consts => this.consts.AsSpan(0, this.constCount);

    internal ReadOnlySpan<MirProjection> Projections => this.projections.AsSpan(0, this.projectionCount);

    internal ReadOnlySpan<MirScope> Scopes => this.scopes.AsSpan(0, this.scopeCount);

    internal ReadOnlySpan<MirStatic> Statics => this.statics.AsSpan(0, this.staticCount);

    internal ReadOnlySpan<MirOpaque> OpaqueSites => this.opaques.AsSpan(0, this.opaqueCount);

    internal ReadOnlySpan<int> OpaqueRoots => this.opaqueRoots.AsSpan(0, this.opaqueRootCount);

    /// <summary>Gets the failure bodies of the requires built in this body, each added where its require is finished, so one inside
    /// another's failure body precedes it; a default copy has none, A3 judges its template (SPEC 7.2.3).</summary>
    internal ReadOnlySpan<MirMustNotComplete> MustNotComplete => this.ends.AsSpan(0, this.endCount);

    /// <summary>Gets a value indicating whether a value of the body is Opaque: an error or Unsupported form replaced it.</summary>
    internal bool HasOpaque => this.opaqueCount > 0;

    /// <summary>Gets the Type of a Place: its last projection's, or its root's.</summary>
    /// <param name="place">A Place of the body.</param>
    /// <returns>The Type, or null when a projection is not admitted.</returns>
    internal BoundType? TypeOf(MirPlace place)
    {
        if (place.Projection != 0)
        {
            return this.projections[place.Projection].Type;
        }

        return place.Root >= 0 ? this.locals[place.Root].Type : this.statics[~place.Root].Type;
    }

    /// <summary>Gets the Type a projection selects from a Place of a Type (plan 5.4), the one rule the builder interns projections by and the
    /// validator checks them against.</summary>
    /// <param name="type">The Type of the Place projected.</param>
    /// <param name="kind">The projection.</param>
    /// <param name="arg">Its argument.</param>
    /// <returns>The selected Type, or null when the Type does not admit the projection.</returns>
    internal BoundType? ProjectedType(BoundType? type, MirProjectionKind kind, int arg)
    {
        switch (kind)
        {
            case MirProjectionKind.Deref or MirProjectionKind.PairLayer:
                // TyCtxt.Layer: a pair layer names its binder and a reference or object handle has its referent below it; a raw pointer
                // is followed only explicitly (SPEC 5.4).
                _ = TyCtxt.Layer(type, out var below, out var binder);
                if (kind == MirProjectionKind.PairLayer)
                {
                    return this.Binds(arg, binder) ? below : null;
                }

                if (binder is null && !ReferenceEquals(below, type))
                {
                    return below;
                }

                return type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw, Components: [var pointee] } ? pointee : null;
            case MirProjectionKind.Field:
                if (type?.Kind is BoundTypeKind.Tuple or BoundTypeKind.Closure)
                {
                    return (uint)arg < (uint)type.Components.Count ? type.Components[arg] : null;
                }

                return AdtDef.IsStruct(type) && (uint)arg < (uint)AdtDef.Count(type!) ? AdtDef.FieldType(type!, arg) : null;
            case MirProjectionKind.Base:
                for (var depth = arg; depth > 0 && type is not null; depth--)
                {
                    type = AdtDef.Base(type);
                }

                return arg > 0 ? type : null;
            case MirProjectionKind.Index:
                return type?.Kind is BoundTypeKind.Array or BoundTypeKind.Slice or BoundTypeKind.FixedArray && (uint)arg < (uint)this.localCount &&
                    ReferenceEquals(this.locals[arg].Type, BoundType.ISize) ? type.Components[0] : null;
            case MirProjectionKind.ConstIndex:
                return type is { Kind: BoundTypeKind.FixedArray, LengthExpression: null } && arg >= 0 && arg < type.Length ? type.Components[0] : null;
            case MirProjectionKind.Downcast:
                return type is not null && AdtDef.CaseTypes(type) is { } payloads && (uint)arg < (uint)payloads.Length ? payloads[arg] : null;
            default:
                return null;
        }
    }

    /// <summary>Gets whether a binder of the body, which a PairLayer or PairAcquire names, is the pair layer's binder (<see cref="TyCtxt.Layer"/>).</summary>
    /// <param name="binder">The index in <see cref="Binders"/>.</param>
    /// <param name="target">The pair layer's binder.</param>
    /// <returns>Whether the index names that binder.</returns>
    internal bool Binds(int binder, BindingSymbol? target) => target is not null && (uint)binder < (uint)this.Binders.Count &&
        ReferenceEquals(this.Binders[binder].Target, target);

    /// <summary>Starts the body over for a definition: every table empty but the body's scope and the empty projection chain.</summary>
    /// <param name="key">The definition.</param>
    /// <param name="source">The body's syntax.</param>
    internal void Reset(MirDefKey key, SourceRef source)
    {
        (this.Key, this.Source) = (key, source);
        this.localCount = this.blockCount = this.statementCount = this.operandCount = this.caseCount = this.constCount = 0;
        this.projectionCount = this.scopeCount = this.staticCount = this.opaqueCount = this.opaqueRootCount = this.endCount = 0;
        this.projectionIds.Clear();
        this.constIds.Clear();
        this.Binders.Clear();
        Add(ref this.projections, ref this.projectionCount, default);
        Add(ref this.scopes, ref this.scopeCount, new MirScope(-1, MirScopeKind.Lexical, source));
    }

    internal int AddLocal(in MirLocal local) => Add(ref this.locals, ref this.localCount, local);

    internal int AddBlock() => Add(ref this.blocks, ref this.blockCount, new MirBlock(this.statementCount, 0, Unterminated));

    /// <summary>Appends a statement to a block, which must be empty or the block a statement was appended to last.</summary>
    /// <param name="block">The block.</param>
    /// <param name="statement">The statement.</param>
    internal void Push(int block, in MirStatement statement)
    {
        ref var target = ref this.blocks[block];
        if (target.Count == 0)
        {
            target.Start = this.statementCount;
        }
        else if (target.Start + target.Count != this.statementCount)
        {
            throw new InvalidOperationException("A MIR block's statements are appended together.");
        }

        Add(ref this.statements, ref this.statementCount, statement);
        target.Count++;
    }

    internal void Terminate(int block, in MirTerminator terminator) => this.blocks[block].Terminator = terminator;

    internal int AddOperand(in MirOperand operand) => Add(ref this.operands, ref this.operandCount, operand);

    internal int AddCase(long value, int target) => Add(ref this.cases, ref this.caseCount, new MirSwitchCase(value, target));

    internal int AddScope(int outer, MirScopeKind kind, SourceRef source) => Add(ref this.scopes, ref this.scopeCount, new MirScope(outer, kind, source));

    /// <summary>Gets the id of a constant, interning it.</summary>
    /// <param name="constant">The constant.</param>
    /// <returns>The id the body's equal constants share.</returns>
    internal int Const(in MirConst constant)
    {
        ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(this.constIds, constant, out var exists);
        if (!exists)
        {
            id = Add(ref this.consts, ref this.constCount, constant);
        }

        return id;
    }

    /// <summary>Gets a Place extended by one projection, interning the chain with the Type <see cref="ProjectedType"/> gives it.</summary>
    /// <param name="place">The Place.</param>
    /// <param name="kind">The projection.</param>
    /// <param name="arg">Its argument.</param>
    /// <returns>The projected Place; its Type is null when the projection is not admitted, which the validator reports.</returns>
    internal MirPlace Project(MirPlace place, MirProjectionKind kind, int arg)
    {
        var row = new MirProjection(place.Projection, kind, arg, this.ProjectedType(this.TypeOf(place), kind, arg));
        ref var id = ref CollectionsMarshal.GetValueRefOrAddDefault(this.projectionIds, row, out var exists);
        if (!exists)
        {
            id = Add(ref this.projections, ref this.projectionCount, row);
        }

        return place with { Projection = id };
    }

    /// <summary>Gets the root Place of a static, adding it at its first use.</summary>
    /// <param name="entry">The static's symbol, its Type and whether it is mutable and holds raw authority.</param>
    /// <returns>The static's Place.</returns>
    internal MirPlace Static(in MirStatic entry)
    {
        var index = 0;
        while (index < this.staticCount && !ReferenceEquals(this.statics[index].Symbol, entry.Symbol))
        {
            index++;
        }

        return new(~(index < this.staticCount ? index : Add(ref this.statics, ref this.staticCount, entry)));
    }

    /// <summary>Adds the site of an Opaque value.</summary>
    /// <param name="source">The syntax it replaces.</param>
    /// <param name="roots">The roots that syntax reads, writes or borrows: locals, or complements of static indexes.</param>
    /// <returns>The site.</returns>
    internal int AddOpaque(SourceRef source, ReadOnlySpan<int> roots)
    {
        var start = this.opaqueRootCount;
        foreach (var root in roots)
        {
            Add(ref this.opaqueRoots, ref this.opaqueRootCount, root);
        }

        return Add(ref this.opaques, ref this.opaqueCount, new MirOpaque(source, start, roots.Length));
    }

    internal void AddMustNotComplete(in MirMustNotComplete row) => Add(ref this.ends, ref this.endCount, row);

    private static int Add<T>(ref T[] items, ref int count, in T item)
    {
        if (count == items.Length)
        {
            Array.Resize(ref items, Math.Max(8, count * 2));
        }

        items[count] = item;
        return count++;
    }
}
