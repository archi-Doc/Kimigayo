// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns the checked set (SPEC 8.15.3, IMPL 21.3.1; R5 package K 4.3-4.5; DECISIONS R5-M35): every body instance a root requires through
// runtime or check-only edges, and the one resolution of each call site's callee that code generation reads.
// Input: a built, validated MirProgram; the number of root modules (a range of Source.Module, not a mode); TyCtxt (Instantiates,
// Implementation); the Target column of the intrinsic rows.
// Output: the CheckedSet of the program version: the roots, in MirProgram.Bodies order, then the items a FIFO worklist admits, each with one
// target per resolved site of its body, in location order, with whether a runtime path reaches the site.
// Roots (K 4.4): each body of a root module whose definition does not instantiate, a module's top-level statements included.
// Edges (K 4.5): one template per body and version lists its call sites that may carry a target (Fn, Requirement and Value callees and the
// intrinsics the Target column names; a Virtual call none, X5), each with its runtime bit from one MirReach walk without CheckTarget edges.
// Replaying an item's template admits, for now, each closed callee: a Fn callee's body (a default template's, else the implementation a
// closed key selects, as its function's, getter's or setter's body) and the body of a Closure of a closed definition. K3 and K4a bring the
// erased keys, composition and limits for generic callees, K5-K8 the other rows, K4b the records.
// Invariants: deterministic; one item per key; a repeated run of a version is a no-op; a program with validator findings has an empty set;
// a warm run of a program as large as an earlier one allocates nothing.
// Must not: read syntax, HIR or plans; depend on the MIR analyses' results or edit their driver; decide acceptance or record a problem
// yet (K4b); add an IntrinsicId, CompilerFunctionKind or BindingFailure (PD21).

using Kimi.Compiler.Mir;

namespace Kimi.Compiler.Backend;

/// <summary>Collects the checked set of a program version (K package 4.3).</summary>
/// <param name="types">The Type context of the programs.</param>
internal sealed class InstanceCollector(TyCtxt types)
{
    private readonly MirReach reach = new();
    private (int Block, bool Runtime)[] sites = []; // The templates: each body's sites, in body order.
    private int[] starts = []; // By body ordinal, its first site; the entry after the last body ends the sites.

    /// <summary>Gets the checked set of the latest run.</summary>
    internal CheckedSet Set { get; } = new();

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
        if (program.Findings.Count > 0)
        {
            return set;
        }

        this.Templates(program);
        foreach (var body in program.Bodies)
        {
            if (body.Source.Module < rootModules && (body.Key.Decl is null || !types.Instantiates(body.Key.Decl)))
            {
                set.Admit(new(MonoKind.Body, new(body.Key, null)), body.Ordinal);
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

    // An item's template replayed: the target of each site whose callee resolves.
    private void Replay(MirProgram program, int item)
    {
        var set = this.Set;
        var body = program.Bodies[set.Items[item].BodyOrdinal];
        for (var i = this.starts[body.Ordinal]; i < this.starts[body.Ordinal + 1]; i++)
        {
            var (block, runtime) = this.sites[i];
            var end = body.Blocks[block];
            if (this.Callee(program, body, end.Terminator) is { } callee)
            {
                set.Target(item, new(set.Admit(new(MonoKind.Body, new(callee.Key, null)), callee.Ordinal), new(block, end.Count), runtime));
            }
        }
    }

    // K 4.5 rows 1 and 5 for closed definitions: a Fn callee's body (a default template's, the original's, SPEC 8.13.2; else the
    // implementation a closed key selects, SPEC 8.13.3) and a Closure's. Null for a callee without a body, one that instantiates (K4a, K5)
    // and the rows of later units (Requirement callees, function items and intrinsics: K5-K8).
    private MirBody? Callee(MirProgram program, MirBody body, in MirTerminator call)
    {
        var callee = call.Callee;
        var target = callee.Kind switch
        {
            MirCalleeKind.Fn when callee.Arg == 0 && types.Implementation(callee.Symbol!, callee.Subst, 0) is { } selected &&
                !ReferenceEquals(selected, callee.Symbol) => program.Callee(new(MirCalleeKind.Fn, selected)),
            MirCalleeKind.Fn => program.Callee(callee),
            MirCalleeKind.Value when Callable(body, call.Operand) is { Kind: BoundTypeKind.Closure, Symbol: { } closure }
                => program[new(closure, MirBodyKind.Closure)],
            _ => null,
        };
        return target?.Key.Decl is { } decl && types.Instantiates(decl) ? null : target;
    }
}
