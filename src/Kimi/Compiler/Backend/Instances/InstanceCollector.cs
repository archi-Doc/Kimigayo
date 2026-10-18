// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the checked set (SPEC 8.15.3, IMPL 21.3.1; R5 package K 4.3-4.6, 4.9; DECISIONS R5-M35, R5-M40): every body instance a root requires
// through runtime or check-only edges, its instance-stage records, and the one resolution of each call site's callee that code generation
// reads.
// Input: a built, validated MirProgram; the number of root modules (a range of Source.Module, not a mode); TyCtxt (Instantiates, Erase,
// Compose, Apply, Implementation); MirCases (Views); the Target column of the intrinsic rows; the capability table.
// Output: the CheckedSet of the program version: the roots, in MirProgram.Bodies order, then the items a FIFO worklist admits, each with one
// target per resolved site of its body, in location order, with whether a runtime path reaches the site; and its records, which
// Compilation.MirRecords publishes under MIR code generation (K4b).
// Roots (K 4.4): each body of a root module whose definition does not instantiate, a module's top-level statements included.
// Edges (K 4.5): one template per body and version lists its call sites that may carry a target (Fn, Requirement and Value callees and the
// intrinsics the Target column names; a Virtual call none, X5), each with its runtime bit from one MirReach walk without CheckTarget edges.
// Replaying an item's template composes each site's substitution with the item's representative and admits the callee: a Fn callee's body
// (a default template's, else the implementation the composed key selects, as its function's, getter's or setter's body), keyed by its
// definition and, when it instantiates, its erased substitution (KD1), and the body of a Closure of a closed definition. K5-K8 add the
// other rows.
// Admission (K 4.6, X19): a new key is admitted once; its representative is the first composed substitution, erased only into the key
// (KD3). A call of a nested function that does not carry its enclosing instance (until K5) and a substitution TyCtxt cannot make are the
// capability table's gaps; depth past 128, 16 ancestors of the same definition and 1,024 items of one definition are
// GenerationResourceLimit_Kd; a representative that is open or leaves its body open is InternalInvariant_Kd. A refused edge is dropped
// and collection continues.
// Location (K 4.9): a record's subject is the cause's site, moved out of inlined library defaults to the outermost omitting call, else the
// root-module site the admission chain first reached.
// Invariants: deterministic; one item per key; every representative is closed and closes its body (K 4.3); a repeated run of a version is
// a no-op; a program with validator findings has an empty set; a warm run of a program as large as an earlier one allocates nothing.
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
    private readonly Dictionary<MirDefKey, int> counts = new(); // Items per definition (X19).
    private (int Block, bool Runtime)[] sites = []; // The templates: each body's sites, in body order.
    private int[] starts = []; // By body ordinal, its first site; the entry after the last body ends the sites.
    private int rootModules;

    /// <summary>Initializes a new instance of the <see cref="InstanceCollector"/> class.</summary>
    /// <param name="types">The Type context of the programs.</param>
    internal InstanceCollector(TyCtxt types)
    {
        this.types = types;
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

    // K 4.5: a call carries a target by its callee: a Fn, Requirement or Value callee, or an intrinsic whose row names one; a Virtual call
    // none, its receiver's VTable holding the entries (X5).
    private static bool Carries(in MirCallee callee) => callee.Kind switch
    {
        MirCalleeKind.Fn or MirCalleeKind.Requirement or MirCalleeKind.Value => true,
        MirCalleeKind.Intrinsic => IntrinsicRows.Of((IntrinsicId)callee.Arg).Target != IntrinsicTarget.None,
        MirCalleeKind.Virtual => false,
        _ => throw new InvalidOperationException($"A call has the callee kind {callee.Kind}."),
    };

    // What a Value callee calls: its operand's Type below a reference, a Closure being called through a borrow in its call's mode (SPEC
    // 7.7.3).
    private static BoundType? Callable(MirBody body, in MirOperand operand)
    {
        _ = TyCtxt.Layer(operand.Kind == MirOperandKind.Const ? body.Consts[operand.Arg].Type : body.TypeOf(operand.Place)!, out var callable, out _);
        return callable;
    }

    // Each body's sites, once per version, with whether a runtime path from its entry reaches each.
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
            var blocks = body.Blocks;
            this.reach.Clear(blocks.Length);
            this.reach.Walk(body, 0, checks: false);
            for (var block = 0; block < blocks.Length; block++)
            {
                if (blocks[block].Terminator is { Kind: MirTerminatorKind.Call } call && Carries(call.Callee))
                {
                    if (count == this.sites.Length)
                    {
                        Array.Resize(ref this.sites, Math.Max(256, count * 2));
                    }

                    this.sites[count++] = (block, this.reach[block]);
                }
            }
        }

        this.starts[bodies.Length] = count;
    }

    // An item's template replayed under its representative: the target of each site whose callee resolves and is admitted.
    private void Replay(MirProgram program, int item)
    {
        var set = this.Set;
        var body = program.Bodies[set.Items[item].BodyOrdinal];
        for (var i = this.starts[body.Ordinal]; i < this.starts[body.Ordinal + 1]; i++)
        {
            this.Work++;
            var (block, runtime) = this.sites[i];
            var end = body.Blocks[block];
            if (this.Edge(program, item, body, end.Terminator) is >= 0 and var target)
            {
                set.Target(item, new(target, new(block, end.Count), runtime));
            }
        }
    }

    // K 4.5 rows 1 and 5: a Fn callee's body under the site's substitution composed with the item's representative (TyCtxt.Compose, whose
    // declaring Type is the site's own, so a nested function's call carries its enclosing instance only as Admit checks; a default template's,
    // the original's, SPEC 8.13.2; else the implementation the composed key selects, SPEC 8.13.3) and the body of a Closure of a closed
    // definition (K5 brings closures of instances). The admitted item, or -1 for a callee without a body, a refused edge or a later row.
    private int Edge(MirProgram program, int item, MirBody body, in MirTerminator call)
    {
        var callee = call.Callee;
        if (callee.Kind == MirCalleeKind.Value)
        {
            return Callable(body, call.Operand) is { Kind: BoundTypeKind.Closure, Symbol: { } closure } && !this.types.Instantiates(closure) &&
                program[new(closure, MirBodyKind.Closure)] is { } target ? this.Admit(item, body, call.Source, target, null) : -1;
        }

        if (callee.Kind != MirCalleeKind.Fn)
        {
            return -1; // Requirement callees and intrinsics: K6-K8.
        }

        var representative = this.Set.Items[item].Representative;
        var subst = callee.Subst is { } inner && representative is not null ? this.types.Compose(inner, representative) : callee.Subst;
        if (subst is null && callee.Subst is not null)
        {
            this.Set.Report(Capability.Code(CapabilityGap.StorageNesting), this.Subject(item, body, call.Source));
            return -1;
        }

        var function = callee.Arg == 0 ? this.types.Implementation(callee.Symbol!, subst, 0) : callee.Symbol;
        var found = ReferenceEquals(function, callee.Symbol) ? program.Callee(callee) : program.Callee(new(MirCalleeKind.Fn, function));
        return found is null ? -1 : this.Admit(item, body, call.Source, found, subst);
    }
}
