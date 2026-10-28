// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the indexed storage of one MIR body and its projection chains.
// Input: the forms the builder appends (MirForms.cs). Output: reusable arrays of locals, blocks, statements and associated records.
// References use body-local indices; projection chains are interned and ProjectedType owns their Type rule.
// Must not resolve syntax or introduce a parallel representation for the same body.

using System.Runtime.InteropServices;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

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

    /// <summary>Gets or sets the body's position in its program (<see cref="MirProgram.Bodies"/>), set where the program begins it.</summary>
    internal int Ordinal { get; set; }

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

    /// <summary>Gets the subject of a finding or problem at a location: the syntax its source names, else the body's.</summary>
    /// <param name="source">The source of a statement, a terminator or a local.</param>
    /// <returns>The syntax.</returns>
    internal SourceRef Subject(in SourceInfo source) => source.Syntax.Id != 0 ? source.Syntax : this.Source;

    /// <summary>Gets the Inlined scope a scope lies in, itself included: the inline default copy whose source is its call (DECISIONS
    /// R4-M69).</summary>
    /// <param name="scope">A scope of the body.</param>
    /// <returns>The innermost Inlined scope enclosing it, or -1 when it lies in none.</returns>
    internal int Inlining(int scope) => this.Enclosing(scope, MirScopeKind.Inlined);

    /// <summary>Gets the scope of a kind a scope lies in, itself included: an Inlined scope's copy (<see cref="Inlining"/>), a Deferred
    /// scope's defer body (SPEC 16.1).</summary>
    /// <param name="scope">A scope of the body.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The innermost scope of the kind enclosing it, or -1 when it lies in none.</returns>
    internal int Enclosing(int scope, MirScopeKind kind)
    {
        while (scope >= 0 && this.scopes[scope].Kind != kind)
        {
            scope = this.scopes[scope].Outer;
        }

        return scope;
    }

    /// <summary>Gets the innermost scope enclosing two scopes, either included: a scope's Outer precedes it, scope 0 enclosing every
    /// scope (MirValidator), so the later of two scopes is never the other's Outer.</summary>
    /// <param name="left">A scope of the body.</param>
    /// <param name="right">Another scope of the body.</param>
    /// <returns>Their innermost common scope.</returns>
    internal int Common(int left, int right)
    {
        while (left != right)
        {
            (left, right) = left > right ? (this.scopes[left].Outer, right) : (left, this.scopes[right].Outer);
        }

        return left;
    }

    /// <summary>Gets the omitting call of the outermost inline default copy a scope lies in: the source of the outermost Inlined scope
    /// enclosing it, the call in this body's own source (SPEC 15.6, 23.1.1; DECISIONS R4-M97).</summary>
    /// <param name="scope">A scope of the body.</param>
    /// <returns>The call, or null when the scope lies in no copy.</returns>
    internal SourceRef? OmittingCall(int scope)
    {
        SourceRef? call = null;
        for (var copy = this.Inlining(scope); copy >= 0; copy = this.Inlining(this.scopes[copy].Outer))
        {
            call = this.scopes[copy].Source;
        }

        return call;
    }

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
