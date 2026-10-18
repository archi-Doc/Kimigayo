// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the checked set (SPEC 8.15.3, IMPL 21.3.1; R5 package K 4.3-4.9; DECISIONS R5-M35, R5-M40, R5-M43, R5-M44, R5-M53, R5-M54): every body
// instance and type-driven item a root requires through runtime or check-only edges, its instance-stage records, and the one resolution of
// each site's target that code generation reads.
// Input: a built, validated MirProgram; the number of root modules (a range of Source.Module, not a mode); TyCtxt (Instantiates, Erase,
// Compose, Apply, Implementation, SubstOfClosure, SubstOfItem); MirCases (Views); the Target and Layout columns of the intrinsic rows; the
// capability table; the compilation's Layout, whose outcomes the collector alone maps (R5-M37).
// Output: the CheckedSet of the program version: the roots, in MirProgram.Bodies order, then the items a FIFO worklist admits, each with one
// target per resolved site of its body, in location order, with whether a runtime path reaches the site; and its records, which
// Compilation.MirRecords publishes under MIR code generation (K4b).
// Roots (K 4.4): each body of a root module whose definition does not instantiate, a module's top-level statements included.
// Edges (K 4.5, InstanceCollector.Edges.cs): one template per body and version lists its sites that may carry a target (Fn, Requirement and
// Value calls and the intrinsics the Target column names, a Virtual call none, X5; a Closure environment, an erasure and a function item
// constant), each with its runtime bit from one MirReach walk without CheckTarget edges. Replaying an item's template resolves each site
// under the item's representative; a type-driven item's targets follow from its representative Type. K6-K8 add the other rows.
// Capability (K 4.7, InstanceCollector.Capabilities.cs): the definition-stage check (A3b) of every operation of a root-module body whose
// Types are closed, every block included, once per definition and location; Binding's refusals are Opaque values, so it never repeats them.
// An operation whose Types depend on slots is a template site, checked in each instance with its Types there, once per location.
// Layout obligations (K 4.8, InstanceCollector.Layouts.cs): each item's by-value uses (its body's locals but drop flags, its aggregates and
// the Type argument of an intrinsic whose row names one) and each type-driven item's Type take one Layout verdict per erased Type: a limit is
// GenerationResourceLimit_Kd at the first use, Rejected storage derives from a Binding error, and a type-driven item needs an Ok verdict.
// Admission (K 4.6, X19): a new key is admitted once; its representative is the first composed substitution or Type, erased only into the
// key (KD3). A call of a nested function whose substitution does not carry its enclosing instance (DECISIONS R5-M43) and a substitution TyCtxt
// cannot make are the capability table's gaps; depth past 128, 16 ancestors of the same definition and 1,024 items of one definition are
// GenerationResourceLimit_Kd; a representative that is open or leaves its body open is InternalInvariant_Kd. A refused edge is dropped
// and collection continues.
// Location (K 4.9): a record's subject is the cause's site, moved out of inlined library defaults to the outermost omitting call, else the
// root-module site the admission chain first reached.
// Invariants: deterministic; one item per key; every representative is closed and closes its body (K 4.3); at most one target per site; a
// repeated run of a version is a no-op; a program with validator findings has an empty set; a warm run of a program as large as an earlier
// one allocates nothing.
// Must not: read syntax, HIR or plans; depend on the MIR analyses' results or edit their driver; decide acceptance or publish its records
// (Compilation does); prove anything on an erased value; add an IntrinsicId, CompilerFunctionKind or BindingFailure (PD21).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Collects the checked set of a program version (K package 4.3).</summary>
internal sealed partial class InstanceCollector
{
    private readonly TyCtxt types;
    private readonly MirCases cases;
    private readonly MirReach reach = new();
    private readonly Layout layout;
    private readonly HashSet<BoundType> limited = new(); // The erased Types whose verdict is a limit, each reported once (K 4.8).
    private readonly Dictionary<MirDefKey, int> counts = new(); // Items per definition (X19).
    private (MirLocation At, bool Runtime)[] sites = []; // The templates: each body's sites, in body order.
    private int[] starts = []; // By body ordinal, its first site; the entry after the last body ends the sites.
    private int rootModules;

    /// <summary>Initializes a new instance of the <see cref="InstanceCollector"/> class.</summary>
    /// <param name="types">The Type context of the programs.</param>
    /// <param name="layout">The layout of the programs' compilation.</param>
    internal InstanceCollector(TyCtxt types, Layout layout)
    {
        this.types = types;
        this.layout = layout;
        this.cases = new(types);
        this.cases.Deselect(); // A representative is closed, so the views it selects need no caller view.
    }

    /// <summary>Gets the checked set of the latest run.</summary>
    internal CheckedSet Set { get; } = new();

    /// <summary>Gets the work of the latest run: the sites replayed and the items admitted, which grow linearly with the instances.</summary>
    internal int Work { get; private set; }

    /// <summary>Collects the checked set of a program version unless this version already was.</summary>
    /// <param name="program">The built and validated program.</param>
    /// <param name="rootModules">The number of root modules: the bodies of modules below it are roots.</param>
    /// <returns>The checked set.</returns>
    internal CheckedSet Run(MirProgram program, int rootModules)
    {
        var set = this.Set;
        if (program.Version == set.Version)
        {
            return set;
        }

        set.Clear(program.Version);
        this.counts.Clear();
        this.limited.Clear();
        (this.rootModules, this.Work) = (rootModules, 0);
        if (program.Findings.Count > 0)
        {
            return set;
        }

        this.Templates(program);
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module < rootModules && (body.Key.Decl is null || !this.types.Instantiates(body.Key.Decl)))
            {
                set.Add(new() { Key = new(MonoKind.Body, new(body.Key, null)), BodyOrdinal = body.Ordinal, Parent = -1, Site = body.Source });
            }
        }

        set.EndRoots();
        for (var item = 0; item < set.Items.Length; item++)
        {
            this.Replay(program, item);
        }

        return set;
    }

    // K 4.5 rows 1-9: a call carries a target by its callee: a Fn, Requirement or Value callee, or an intrinsic whose row names one; a
    // Virtual call none, its receiver's VTable holding the entries (X5).
    private static bool Carries(in MirCallee callee) => callee.Kind switch
    {
        MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Value => true,
        MirCalleeKind.Intrinsic => IntrinsicRows.Of((IntrinsicId)callee.Arg).Target != IntrinsicTarget.None,
        MirCalleeKind.Virtual => false,
        _ => throw new InvalidOperationException($"A call has the callee kind {callee.Kind}."),
    };

    // K 4.5 rows 11-13, K 4.7 and K 4.8: a statement is a site by its rvalue: an aggregate, a by-value use whose Closure environment also
    // carries a target; an erasure to a Function Type; a function item constant; an operation whose Types depend on slots, which the
    // capability table checks per instance.
    private static bool IsSite(MirBody body, in MirStatement statement)
        => statement is { Kind: MirStatementKind.Assign, Rvalue: var rvalue } && rvalue.Kind switch
    {
        MirRvalueKind.Aggregate => true,
        MirRvalueKind.Cast when (MirCastKind)rvalue.Op == MirCastKind.Erasure => true,
        MirRvalueKind.Cast or MirRvalueKind.BinaryOp => IsOpen(body, rvalue),
        MirRvalueKind.Use => body.Operands[rvalue.Start] is { Kind: MirOperandKind.Const } operand && body.Consts[operand.Arg].Kind == MirConstKind.FnItem,
        _ => false,
    };

    // Whether a block's terminator is a site: a call that may carry a target, or an intrinsic call whose Type argument is a by-value use.
    private static bool IsSite(in MirTerminator terminator) => terminator is { Kind: MirTerminatorKind.Call, Callee: var callee } &&
        (Carries(callee) || (callee.Kind == MirCalleeKind.Intrinsic && IntrinsicRows.Of((IntrinsicId)callee.Arg).LaysOutType));

    // Each body's sites, once per version, in location order, with whether a runtime path from its entry reaches each.
    private void Templates(MirProgram program)
    {
        var bodies = program.Bodies;
        if (this.starts.Length <= bodies.Length)
        {
            this.starts = new int[Math.Max(bodies.Length + 1, this.starts.Length * 2)];
        }

        var count = 0;
        foreach (var body in bodies)
        {
            this.starts[body.Ordinal] = count;
            count = this.Template(body, count);
        }

        this.starts[bodies.Length] = count;
    }

    // A body's sites, appended from `count` in location order with their runtime bits; a root-module body's operations are checked against
    // the capability table on the way (A3b). Returns the new count.
    private int Template(MirBody body, int count)
    {
        var blocks = body.Blocks;
        var root = body.Source.Module < this.rootModules;
        this.reach.Clear(blocks.Length);
        this.reach.Walk(body, 0, checks: false);
        for (var block = 0; block < blocks.Length; block++)
        {
            var (start, end) = (blocks[block].Start, blocks[block].Count);
            for (var index = 0; index <= end; index++)
            {
                if (index < end && root)
                {
                    this.CheckOperation(body, body.Statements[start + index]);
                }

                if (index < end ? IsSite(body, body.Statements[start + index]) : IsSite(blocks[block].Terminator))
                {
                    count = this.AddSite(count, new(block, index), this.reach[block]);
                }
            }
        }

        return count;
    }

    private int AddSite(int count, MirLocation at, bool runtime)
    {
        if (count == this.sites.Length)
        {
            Array.Resize(ref this.sites, Math.Max(256, count * 2));
        }

        this.sites[count] = (at, runtime);
        return count + 1;
    }

    // An item's targets: a body item's template replayed under its representative, the target of each site that resolves and is admitted;
    // a type-driven item's from its representative Type.
    private void Replay(MirProgram program, int item)
    {
        var set = this.Set;
        if (set.Items[item].BodyOrdinal < 0)
        {
            if (this.Expand(program, item) is >= 0 and var expanded)
            {
                set.Target(item, new(expanded, default, Runtime: true));
            }

            return;
        }

        var body = program.Bodies[set.Items[item].BodyOrdinal];
        this.LayOutLocals(item, body);
        for (var i = this.starts[body.Ordinal]; i < this.starts[body.Ordinal + 1]; i++)
        {
            this.Work++;
            var (at, runtime) = this.sites[i];
            var block = body.Blocks[at.Block];
            var target = at.Index == block.Count
                ? this.Edge(program, item, body, block.Terminator)
                : this.Edge(program, item, body, body.Statements[block.Start + at.Index]);
            if (target >= 0)
            {
                set.Target(item, new(target, at, runtime));
            }
        }
    }
}
