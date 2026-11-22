// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// Owns A4 (plan 5.5; SPEC 8.11.2, 8.11.3, 8.11.4, 15.6.4; DECISIONS R4-M22): per body view the effect summary — the classes
// Environment, Unknown, Limit, Derived and Deep (R4-M153) and atoms over the definition's abstract Types (Drop(T), a call through a Callable input T with
// the signature the call selected) — and
// the `confined` obligations Binding publishes (TyCtxt.EffectObligations).
// Input: validated bodies per MirCases view, their uses (MirUses), the blocks the runtime edges reach (MirDataflow.Begin), A2's verdicts
// and its walk of a group (MirProgram.Drops, MirDropTable.Destroyed), whether the driver has stepped a body (MirDropTable.Holds) and
// into which store (MirDropTable.LibraryHolds: the driver records a body's local facts in the store it began or continued, BeginLibrary,
// ContinueLibrary or BeginProgram, in the step that closes the body's A2 entry in A2's store of that segment, so A4's library store and A2's hold the same
// bodies; a driver that fills one without the other breaks Base), TyCtxt's
// obligations and callee facts, the Callable signature each call through a Callable value selected (SPEC 8.10, 8.11.4: a value call's
// CallPlan.DeclaredSignature, the Value callee's TypeToken; a published row's Callable input's one signature, TyCtxt.CallsSignature),
// TyCtxt.Comparison for a tuple operator's call (its distinct leaves;
// DECISIONS R5-M166; a published requirement on a composite Type keeps Binding's legacy plan, TyCtxt.Witnesses, until A-12), and the root
// range.
// Output: Run's result, whether its round was clean; IncompatibleContractImplementation_Kd,
// UnsatisfiedEffectBound_Kd, Unsupported_Kd and OwnershipDepthLimit_Kd through MirReport for the obligations whose subject is in the root
// range (the library's are the library check's, PLAN G951 E3), added by a clean round only; and for A5 the per-node summaries and a site's
// key row and classes per view (Keys, Field). A broken contract has one form per place (Broken): one Local meets at a call (a function
// without a body Binding gives no category, a comparison leaf with no answer) is InternalInvariant_Kd at the call through MirReport, its
// class Derived; one the solve meets throws (DECISIONS BU-102's form: the check ends as CheckFaulted_Kd), since no program reaches it,
// so the solve keeps no state to record it. Both only in a
// complete compilation (TyCtxt.BindingComplete); in an incomplete one either is Derived with no record, resting on Binding's records.
// The solve runs in rounds (PLAN G951): Compose is the one read of a body's nodes, and a read of a body A2 does not hold (one built since the
// driver's last step, with no local facts) misses; the round goes on to its end, so it demands every body it reads, and commits nothing,
// and the driver steps and runs the next round until one is clean.
// Atoms compose through each call's substitution into the caller (an atom concrete there is resolved there, one on a projection with a
// recursive base where it arises, SPEC 15.6.4: the fixed point is finite where the view finds the base, and a base G170 hides still ends at
// G128's bound), a call atom's signature with its Type; a call through an abstract input takes the bounds of the caller's premises with
// that signature (TyCtxt.CallableBounds), requirement calls on an abstract Self and virtual calls the bounds of the caller's own premises, a requirement on
// another Self what its witnesses run; one worklist with recorded dependencies solves
// the least fixed point; storage is pooled and reset by counts, so a warm version allocates nothing. The local facts are recorded per
// segment (PLAN G950): the library segment's nodes first, into a store of their own (Locals) kept while its library segment holds
// (BeginLibrary; ContinueLibrary appends a kept segment's newly listed bodies, PLAN G951 U2-K), then the program's, into a store cleared
// per program version (BeginProgram); each store holds its nodes' and bodies'
// entries by the node id and the ordinal themselves, and a reader takes a node's store by the library's node count (its nodes come
// first) and a body's by A2's fact (MirDropTable.LibraryHolds, PLAN G951 U2-K; no offset, no ordinal tested against LibraryCount).
// Must not read syntax or HIR, re-derive premises, bounds or obligations, select a call's Callable signature again, report a failure
// that only an Opaque site or a callee
// Binding rejected causes (derived, R4-M10), or read a body with a validator finding (Views: derived, R5-M183).

using Kimi.Diagnostics;
using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

/// <summary>A4, the effect summaries (plan P8, R4 shadow): <see cref="Local"/> records each body view's local facts in the segment
/// <see cref="BeginLibrary"/> or <see cref="BeginProgram"/> began, <see cref="Run"/> solves the summaries of the program version and
/// judges the obligations.</summary>
/// <param name="types">The Type context, whose effect facts Binding published.</param>
/// <param name="cases">The Semantics case views.</param>
/// <param name="flow">The dataflow, begun with the body whose views <see cref="Local"/> records: the blocks the runtime edges reach.</param>
internal sealed partial class MirEffects(TyCtxt types, MirCases cases, MirDataflow flow)
{
    /// <summary>A class of a summary: an effect that cannot be classified, which may access any key (SPEC 15.6.4).</summary>
    internal const byte Unknown = 2;

    /// <summary>A class of a summary: an effect this implementation cannot classify yet (a located <c>Unsupported_Kd</c>).</summary>
    internal const byte Limit = 4;

    /// <summary>A class of a summary: an effect only an Opaque site, a callee whose definition rests on a recorded error (Binding rejected
    /// it or the parser recorded its Body missing: TyCtxt.Bodyless's Dependent), a body with a validator finding or a broken contract
    /// recorded at its call (Broken) causes, a dependent of that error (R4-M10, R5-M183).</summary>
    internal const byte Derived = 8;

    /// <summary>A class of a summary: a destroyed part past the destruction walk's bound, whose effects the walk does not follow (the depth
    /// limit, <c>OwnershipDepthLimit_Kd</c>; DECISIONS R4-M153).</summary>
    internal const byte Deep = 16;

    /// <summary>An atom's kind: the destruction of a value of its Type; any other kind but <see cref="Requires"/> is 1 + the receiver of
    /// a call through it.</summary>
    internal const byte Drop = 0;

    /// <summary>An atom's kind: a call through its Type with a shared borrow, as a published row's Callable input is called.</summary>
    internal const byte Shared = 1 + (int)SemanticsKind.Ref;

    /// <summary>An atom's kind: a published requirement on its Type (SPEC 4.9.6).</summary>
    internal const byte Requires = byte.MaxValue;

    private const byte Environment = 1; // The class of an environment effect (SPEC 8.11.2).

    private readonly HashSet<ComparisonNode> compared = new(ReferenceEqualityComparer.Instance); // A comparison call's distinct nodes (Compared).
    private readonly List<MirDropStep> destroyed = []; // MirDropTable.Destroyed's steps of the Drop being read (Dropped).
    private readonly MirReach reach = new(); // The blocks a body with a case branch reaches under the view (Local).
    private readonly Locals libraryLocals = new(); // The library segment's local facts, kept while the segment holds.
    private readonly Locals programLocals = new(); // The program segment's, cleared per program version.
    private Locals writing = null!; // The segment Local records into, which a begin call names.
    private byte found; // The classes the current evaluation or local walk found.
    private (MirReport Problems, SourceRef Subject, int Body, ulong Views) site; // The call Local reads (Terminator), where Broken records.

    /// <summary>Gets the one answer of classes (DECISIONS R4-M153): the Join of their outcomes, an <see cref="Environment"/> or
    /// <see cref="Unknown"/> effect decided, <see cref="Limit"/> the Limit and <see cref="Deep"/> Deep. A4's obligations and A5's effect
    /// rule record by it.</summary>
    /// <param name="classes">Classes of a summary or a site.</param>
    /// <returns>The classes' outcome.</returns>
    internal static MirOutcome Outcome(byte classes)
    {
        var decided = (classes & (Environment | Unknown)) != 0 ? MirOutcome.Decided : MirOutcome.None;
        return decided.Join((classes & Limit) != 0 ? MirOutcome.Limit : MirOutcome.None).Join((classes & Deep) != 0 ? MirOutcome.Deep : MirOutcome.None);
    }

    // The number of nodes recorded: the library segment's, then the program's.
    private int NodeCount => this.writing.End;

    /// <summary>Begins the library segment's local facts for a new library segment (driver step 0): both segments' facts and every Field
    /// number cleared; <see cref="Local"/> records into the library's store until <see cref="BeginProgram"/>.</summary>
    internal void BeginLibrary()
    {
        this.libraryLocals.Clear(0);
        this.programLocals.Clear(0);
        this.fieldIds.Clear();
        this.initIds.Clear();
        this.fixedIds.Clear();
        this.writing = this.libraryLocals;
    }

    /// <summary>Continues the library segment's local facts in a later version of a kept library segment (driver step 0, PLAN G951 U2-K):
    /// the program's facts and the Field numbers it gave cleared; <see cref="Local"/> records into the library's store after its nodes,
    /// so the library's nodes stay the first, until <see cref="BeginProgram"/>.</summary>
    internal void ContinueLibrary()
    {
        this.BeginProgram();
        this.writing = this.libraryLocals;
    }

    /// <summary>Begins the program segment's local facts for a program version over the library segment's, which stay: the program's facts
    /// and the Field numbers it gave cleared; <see cref="Local"/> records into the program's store.</summary>
    internal void BeginProgram()
    {
        foreach (var field in this.programLocals.Fields)
        {
            this.fieldIds.Remove(field);
        }

        foreach (var field in this.programLocals.Initialized)
        {
            this.initIds.Remove(field);
            this.fixedIds.Remove(field);
        }

        this.programLocals.Clear(this.libraryLocals.End);
        this.writing = this.programLocals;
    }

    /// <summary>Records the local facts of a body view (driver step 2, after the body's A1 and A2, the dataflow begun with the body) in the
    /// segment begun (<see cref="BeginLibrary"/>, <see cref="BeginProgram"/>): its effects and static accesses, its calls with a body as
    /// edges into the callee views their substitution selects, and the parts its Drops destroy (by A2's verdicts) and the Callable inputs
    /// it calls through as inputs of its summary; a body's views are recorded in order. In a body with a case branch only the blocks
    /// the view reaches are read (MirReach under the view): the other side's calls run in no instance of the view.</summary>
    /// <param name="program">The program.</param>
    /// <param name="body">The body, whose view is selected.</param>
    /// <param name="view">The selected view.</param>
    internal void Local(MirProgram program, MirBody body, long view)
    {
        var node = this.writing.Open(body.Ordinal, view);
        this.found = 0;
        var uses = new Uses(this, body);
        if (body.CaseSwitches > 0)
        {
            this.reach.Clear(body.Blocks.Length);
            this.reach.Walk(body, 0, checks: true, cases);
        }

        for (var block = 0; block < body.Blocks.Length; block++)
        {
            if (body.CaseSwitches > 0 && !this.reach[block])
            {
                continue;
            }

            for (var index = 0; index <= body.Blocks[block].Count; index++)
            {
                MirUses.ForEach(cases, new(block, index), ref uses);
            }

            this.Terminator(program, body, block, view, body.Blocks[block].Terminator);
        }

        // SPEC 15.6.4, 12.7.2 (the owner's O-109 (a)): a function's summary, computed once per declaration, includes every implementation
        // of its closed explicit-specialization set: whatever body a call's key selects, a call enters the declaration (SPEC 8.13.3:
        // specializations are never candidates). A specialization fixes only its own slots (SPEC 8.13.1), so it is entered at the
        // declaration's own substitution, which binds its Container's parameters to themselves: the view of the same Container case.
        for (var i = 0; body.Key is { Kind: MirBodyKind.Fn, Decl: { } declaration } && types.Implementation(declaration, null, i) is { } implementation; i++)
        {
            if (!ReferenceEquals(implementation, declaration) && program.Callee(new(MirCalleeKind.Fn, implementation)) is { } specialization)
            {
                this.AddEdge(specialization, types.Identity(implementation.Declaration));
            }
        }

        this.writing.Close(node, body.Ordinal, this.found);
    }

    // SPEC 4.9.6: the Type below a callee's parameter under the call's substitution in the view, what a published row runs on or
    // destroys (A4's, and A5's MirBorrows.Destructions'): the declared Type's instance as every reader reads one (TyCtxt.ReadableInstance,
    // DECISIONS R4-M139), Rejected storage included; null where the substitution does not instantiate it or its storage is a Limit.
    internal BoundType? Below(BindingSymbol callee, Subst? subst, int slot)
    {
        var declared = types.Signature(callee, null, slot, out _);
        var parameter = declared is null || subst is null ? declared : types.ReadableInstance(declared, subst);
        _ = TyCtxt.Layer(parameter is null ? null : cases.Type(parameter), out var below, out _);
        return below;
    }

    // SPEC 8.11.2: the class of a call of a function without a MIR body, by Binding's category of it (TyCtxt.Bodyless): an intrinsic's
    // from MIR's own row of it (MirDescriptors: an environment effect where the row has one, else its inputs alone), a shim's its
    // inputs, an import's an environment effect, a dependent's Derived; none for Unclassified, a broken contract (BodylessKind: every
    // other declaration with a Body disposition has a MIR body, built on demand while the program's demands are open, PLAN G951).
    private static byte? Class(BindingSymbol function) => TyCtxt.Bodyless(function) switch
    {
        BodylessKind.Intrinsic => (MirDescriptors.Of(function.Definition.Id).Effects & MirEffect.Environment) != 0 ? Environment : (byte)0,
        BodylessKind.Shim => 0,
        BodylessKind.Import => Environment,
        BodylessKind.Dependent => Derived,
        _ => null,
    };

    // SPEC 8.11.2, 8.11.3, 8.11.4: what a terminator adds. A published operation adds its row through the call's substitution, never
    // its body (SPEC 4.9.6); a call of another function with a body enters its declaration's summary at the call's substitution (SPEC
    // 15.6.4: its specializations included, Local), one without a body is what Binding classifies (TyCtxt.Bodyless). A tuple operator's call runs its parts'
    // comparisons (Compared). A requirement call that selects a built-in
    // target or whose Self is not abstract (an inline default copy's instance, SPEC 7.2.3) runs what its Self's witnesses run under the
    // Contract reference the call recorded (Require); one on an abstract Self or a dynamic virtual call has the bounds of the caller's
    // premises (X2) and may destroy what it is given by value (rule 1); a call through a value calls its Callable input; staticEnsure is
    // the static's first-access initialization (Initialize); a Drop destroys what A2's verdicts leave (Dropped).
    private void Terminator(MirProgram program, MirBody body, int block, long view, in MirTerminator terminator)
    {
        var callee = terminator.Callee;
        if (terminator.Kind == MirTerminatorKind.Drop)
        {
            this.Dropped(program, body, block, view, cases.TypeOf(terminator.Place));
            return;
        }

        if (terminator.Kind != MirTerminatorKind.Call)
        {
            return;
        }

        this.site = (program.Problems, body.Subject(terminator.Source), body.Ordinal, 1UL << (int)view);
        if (callee.Kind == MirCalleeKind.Fn)
        {
            this.Function(program, callee);
        }
        else if (callee is { Kind: MirCalleeKind.Requirement, Arg: not 0 })
        {
            var contract = TyCtxt.Contract(callee.Symbol!);
            this.compared.Clear();
            this.Compared(program, body, contract, types.Comparison(contract, callee.Subst!.Self!, operators: true)); // The builder's root.
        }
        else if (callee is { Kind: MirCalleeKind.Requirement, Subst: { Self: { } written } subst } && cases.Type(written) is var self &&
            (callee.Symbol!.Definition.Kind != DispositionKind.Body || !self.IsAbstract))
        {
            // Binding's built-in target for its Self, or what a closed Self's witnesses run under the call's Contract reference at the call's
            // own Types and lengths (SPEC 8.6.3): the item keeps the call's substitution in the view, here alone its Self and its
            // reference taking their case Types.
            var reference = subst.Declaring is { } declaring ? cases.Type(declaring) : null;
            this.Item(self, Requires, callee.Symbol, callee.Contract, types.InView(subst, self, reference));
        }
        else if (callee.Kind is MirCalleeKind.Requirement or MirCalleeKind.Virtual)
        {
            var subject = callee.Kind == MirCalleeKind.Requirement ? callee.Subst?.Self : null;
            this.found |= types.DispatchBounds(body.Key.Decl, callee.Symbol!, subject, callee.Contract) == true ? (byte)0 : Unknown;
            this.Given(body, terminator);
        }
        else if (callee.Kind == MirCalleeKind.Value)
        {
            var type = cases.TypeOf(terminator.Operand);
            _ = TyCtxt.Layer(type, out var callable, out _);
            var signature = body.Consts[callee.Arg].Type; // The signature the call selected (MirCallee), as Binding states it.
            this.Item(callable, (byte)(1 + (int)(type.SafeReferent is not null ? type.Semantics : SemanticsKind.Owner)), signature: signature);
            if (callable is { IsAbstract: true })
            {
                this.Given(body, terminator);
            }
        }
        else if (callee.Arg == (int)IntrinsicId.StaticEnsure)
        {
            this.Initialize(program.StaticInit(body, body.Operands[terminator.Start])!); // The validator checks it has one (G191).
        }
        else if (callee.Arg == (int)IntrinsicId.DestroyValues)
        {
            this.Item(cases.Type(callee.Subst!.Types[0]!), Drop); // SPEC 4.9.7: the values of its Type argument.
        }
    }

    private void Function(MirProgram program, in MirCallee call)
    {
        var symbol = call.Symbol!;
        var subst = call.Subst;
        if (call.Arg == 0 && types.Published(symbol) is { } row)
        {
            if (row.Runs is { } requirement)
            {
                this.Item(this.Below(symbol, subst, row.On), Requires, requirement, TyCtxt.Contract(requirement));
            }

            if (row.Destroys >= 0)
            {
                this.Item(this.Below(symbol, subst, row.Destroys), Drop);
            }

            if (row.Calls >= 0)
            {
                // SPEC 4.9.6, 8.11.4: the row's Callable input, called through a shared borrow of the actual argument with its one
                // signature and then destroyed.
                var callable = this.Below(symbol, subst, row.Calls);
                var signature = this.CallsSignature(symbol, subst, row.Calls);
                this.Item(signature is null ? null : callable, Shared, signature: signature);
                this.Item(callable, Drop);
            }

            return;
        }

        if (program.Callee(call) is { } target)
        {
            this.AddEdge(target, subst); // SPEC 15.6.4: the declaration, its specializations included (Local); a default is the original's (SPEC 8.13).
            return;
        }

        // A function without a body: a published row's, else Binding's category of it, none a broken contract at the call.
        if (types.Published(symbol) is null && Class(symbol) is null)
        {
            this.found |= this.Broken();
            return;
        }

        this.Enter(null, subst, symbol);
    }

    // SPEC 4.9.6, 8.11.4: the one signature a published row's Callable input is called with (TyCtxt.CallsSignature) at the call's
    // substitution, as Binding states it; none where its instance does not form (the Limit of an instance that does not form, as Below's).
    private BoundType? CallsSignature(BindingSymbol callee, Subst? subst, int slot)
    {
        var declared = types.CallsSignature(callee, slot);
        return subst is null ? declared : types.ReadableInstance(declared, subst);
    }

    // A broken contract Local meets at the call it reads (the class header's one form with a site; DECISIONS R4-M148's path, as A5's
    // MirBorrows.Reports' Internal): InternalInvariant_Kd at the call in its view under Ownership.Internal, in a complete compilation
    // (TyCtxt.BindingComplete); Derived, so every obligation over it is its dependent and records nothing (SPEC 23.1.2: the other bodies'
    // failures stay reported). In an incomplete one the missing body or answer rests on Binding's records, no record of its own (BU-102:
    // the state rests on a Binding failure; R5-M183: a Closure in a declaration Binding rejected has no body, the collector's Report alike).
    private byte Broken()
    {
        if (types.BindingComplete)
        {
            var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.Internal);
            _ = this.site.Problems.Add(new(DiagnosticCode.InternalInvariant_Kd, requirement, this.site.Subject, this.site.Body, this.site.Views));
        }

        return Derived;
    }

    // SPEC 13.4, 8.11.3 (DECISIONS R5-M166): a tuple operator's call runs, once per distinct node of its Self's comparison under source
    // operators (the validator checked it composes), what that node's leaf runs: a Witness leaf its witness (Function), a Requirement leaf
    // the requirement under the caller's premises (X2; its operands are shared borrows, so it destroys nothing), a Builtin leaf and a
    // Composite's own node nothing. A None leaf, no answer, is a broken contract at the call (Broken; the collector records it as
    // InternalInvariant_Kd at the item), never a silent empty summary.
    private void Compared(MirProgram program, MirBody body, BindingSymbol contract, ComparisonNode node)
    {
        if (!this.compared.Add(node))
        {
            return;
        }

        foreach (var part in node.Parts)
        {
            this.Compared(program, body, contract, part);
        }

        switch (node.Kind)
        {
            case ComparisonKind.Composite or ComparisonKind.Builtin:
                break;
            case ComparisonKind.Witness:
                this.Function(program, new(MirCalleeKind.Fn, node.Target, node.Subst));
                break;
            case ComparisonKind.Requirement:
                this.found |= types.DispatchBounds(body.Key.Decl, node.Target!, node.Subst!.Self, contract) == true ? (byte)0 : Unknown;
                break;
            case ComparisonKind.None:
                this.found |= this.Broken();
                break;
            default:
                throw new InvalidOperationException($"The comparison kind {node.Kind} has no effect row.");
        }
    }

    // R4-M6a: what a Drop destroys in the view, by A2's verdict group (MirDropTable: what a group destroys). One only the checks reach runs
    // no destructor, whatever its group (drop elaboration and the destructor rule apply to runtime paths only, SPEC 14.10.3 as R4-M6a reads
    // it), though the check-only statements' own effects count; a runtime Drop without a row (a body over the analyses' arena limit,
    // R4-M15a) destroys its Place's case Type.
    private void Dropped(MirProgram program, MirBody body, int block, long view, BoundType type)
    {
        if (!flow.Runtime(block))
        {
            return;
        }

        var group = program.Drops.Group(body.Ordinal, block, view);
        if (group < 0)
        {
            this.Item(type, Drop);
            return;
        }

        // Each part the group destroys whole (MirDropTable.Destroyed: a Keep or Flag row's, a part's without a row, a fixed array's
        // run of elements). The items are a set: the walk's order and an array's several Runs of one element Type read alike. An Open
        // level has no drop of its own, an Elide row no step.
        program.Drops.Destroyed(types, body.Ordinal, group, type, this.destroyed);
        foreach (var step in this.destroyed)
        {
            if (step.Kind is MirDropStepKind.Whole or MirDropStepKind.Run)
            {
                this.Item(step.Type, Drop);
            }
        }

        this.destroyed.Clear();
    }

    // SPEC 8.11.3 rule 1: a call the caller's premises bound may destroy each value moved into it.
    private void Given(MirBody body, in MirTerminator terminator)
    {
        foreach (var operand in body.Operands.Slice(terminator.Start, terminator.Count))
        {
            if (operand.Kind == MirOperandKind.Move)
            {
                this.Item(cases.TypeOf(operand), Drop);
            }
        }
    }

    private void Item(BoundType? type, byte kind, BindingSymbol? requirement = null, BindingSymbol? contract = null, Subst? call = null, BoundType? signature = null)
    {
        if (type is null)
        {
            this.found |= Limit;
            return;
        }

        this.writing.Add(new Atom(type, kind, requirement, contract, call, signature));
    }

    private void AddEdge(MirBody callee, Subst? subst) => this.writing.Add(new Edge(callee.Ordinal, this.Views(callee, subst), subst));

    // The store of a node's local facts: the library segment's below its node count (R8-L1 M-B1).
    private Locals Of(int node) => node < this.libraryLocals.End ? this.libraryLocals : this.programLocals;

    // Where a node's local facts begin in its store: the previous node's ends there, none for a segment's first node.
    private NodeFacts Start(Locals store, int node) => node == 0 || node == this.libraryLocals.End ? default : store[node - 1];

    // The first node of a body of the last Run's program: its store's, the library's where A2's library store holds the body
    // (MirDropTable.LibraryHolds, the one fact; the driver's library step wrote both).
    private int Base(int body) => (this.program.Drops.LibraryHolds(body) ? this.libraryLocals : this.programLocals).Base(body);

    // The views of a body a call, a destruction or an obligation enters that its substitution selects; none for a body with a finding,
    // which no consumer reads (R5-M183): what rests on it is that finding's dependent (Derived, R4-M10).
    private ulong Views(MirBody body, Subst? subst)
    {
        if (body.HasFinding)
        {
            this.found |= Derived;
            return 0;
        }

        return cases.Views(body, subst);
    }

    /// <summary>An atom of a summary or a local input: a Type and a kind (<see cref="Drop"/>, 1 + a call's receiver, or
    /// <see cref="Requires"/> with its requirement and the Contract reference its call recorded).</summary>
    /// <param name="Type">The Type.</param>
    /// <param name="Kind">The kind.</param>
    /// <param name="Requirement">The requirement of a <see cref="Requires"/> atom.</param>
    /// <param name="Contract">The Contract reference the requirement's call recorded (<see cref="MirCallee.Contract"/>; a published row's
    /// requirement's own).</param>
    /// <param name="Call">The call's substitution in the view for a local input whose Self is not abstract (<see cref="TyCtxt.InView"/>): its
    /// Self, the Type, its Contract reference (<see cref="Subst.Declaring"/>) and the requirement's own Types and lengths, at which its
    /// witness runs; null for a summary's atom, whose abstract Self names no conformance, and for a published row's requirement, which has
    /// no own slot.</param>
    /// <param name="Signature">The signature of a call through its Type (a kind 1 + a receiver; SPEC 8.11.4): the one the call selected
    /// (<see cref="MirCallee"/>'s TypeToken) or a published row's Callable input's (<see cref="TyCtxt.CallsSignature"/>), whose premises'
    /// bounds the call uses on an abstract Type; null for any other kind.</param>
    internal readonly record struct Atom(BoundType Type, byte Kind, BindingSymbol? Requirement, BindingSymbol? Contract = null, Subst? Call = null, BoundType? Signature = null);

    // A call of a body: the callee's ordinal, the views its substitution selects under the caller's view and the substitution: the call's;
    // null for a body entered in every view (the initializer of a static with several slots, Initialize); the identity (TyCtxt.Identity) for the callee's own Types read in the
    // caller's view (a specialization of the caller's declaration, Local).
    private readonly record struct Edge(int Body, ulong Views, Subst? Subst);

    // A node's entry in its store: its body (an ordinal), the classes of its local facts and the ends of its local inputs, calls, keys and
    // initialization entries in the store.
    private readonly record struct NodeFacts(int Owner, byte Classes, int Items, int Edges, int Keys, int Inits);

    // A segment's local facts in record order (PLAN G950, the Back's C-378 (1)(a)), data only, so the library segment's are kept while the
    // segment holds: its nodes' entries by node id and its bodies' first nodes by ordinal, each the id or ordinal itself (the
    // program's after the library's, no offset), its nodes' local inputs, calls, keys and initialization entries, and the Fields and
    // statics it numbered (BeginProgram removes the program's numbers).
    private sealed class Locals
    {
        private Atom[] items = [];
        private Edge[] edges = [];
        private int[] keys = [];
        private int[] inits = [];
        private NodeFacts[] nodes = []; // By node id, its nodes from its first.
        private int[] bases = []; // By body ordinal, the first node of each of its bodies: a node is a body view.

        // The node id after its last node.
        internal int End { get; private set; }

        internal int ItemCount { get; private set; }

        internal int EdgeCount { get; private set; }

        internal int KeyCount { get; private set; }

        internal int InitCount { get; private set; }

        internal List<BindingSymbol> Fields { get; } = [];

        internal List<BindingSymbol> Initialized { get; } = [];

        internal ReadOnlySpan<Atom> Items => this.items.AsSpan(0, this.ItemCount);

        internal ReadOnlySpan<Edge> Edges => this.edges.AsSpan(0, this.EdgeCount);

        internal ReadOnlySpan<int> Keys => this.keys.AsSpan(0, this.KeyCount);

        internal ReadOnlySpan<int> Inits => this.inits.AsSpan(0, this.InitCount);

        // A node's entry.
        internal NodeFacts this[int node] => this.nodes[node];

        // The first node of one of its bodies.
        internal int Base(int body) => this.bases[body];

        // Opens the next node, a body's view: its id, the body's first node for view 0.
        internal int Open(int body, long view)
        {
            var node = this.End++;
            MirDataflow.Grow(ref this.nodes, this.End);
            MirDataflow.Grow(ref this.bases, body + 1);
            if (view == 0)
            {
                this.bases[body] = node;
            }

            return node;
        }

        // Closes a node with its body and its local classes: its inputs, calls, keys and entries are those added since it was opened.
        internal void Close(int node, int body, byte classes)
            => this.nodes[node] = new(body, classes, this.ItemCount, this.EdgeCount, this.KeyCount, this.InitCount);

        internal void Add(in Atom item)
        {
            MirDataflow.Grow(ref this.items, this.ItemCount + 1);
            this.items[this.ItemCount++] = item;
        }

        internal void Add(in Edge edge)
        {
            MirDataflow.Grow(ref this.edges, this.EdgeCount + 1);
            this.edges[this.EdgeCount++] = edge;
        }

        internal void AddKey(int key)
        {
            MirDataflow.Grow(ref this.keys, this.KeyCount + 1);
            this.keys[this.KeyCount++] = key;
        }

        internal void AddInit(int init)
        {
            MirDataflow.Grow(ref this.inits, this.InitCount + 1);
            this.inits[this.InitCount++] = init;
        }

        // Drops the inputs, calls and entries added past the given counts (a site's, which are not a node's).
        internal void Truncate(int items, int edges, int inits) => (this.ItemCount, this.EdgeCount, this.InitCount) = (items, edges, inits);

        // Removes its facts, keeping the storage: its next node has the id first.
        internal void Clear(int first)
        {
            this.End = first;
            this.ItemCount = this.EdgeCount = this.KeyCount = this.InitCount = 0;
            this.Fields.Clear();
            this.Initialized.Clear();
        }
    }

    // The local facts of a location: an environment effect (an access to a mutable static, Console output, an integer made a pointer, a
    // raw pointer read from an immutable static) or an Opaque site's unknown effect, a dependent of the error that made it (R4-M10).
    private readonly struct Uses(MirEffects owner, MirBody body) : IMirUses
    {
        public void Operand(in MirOperand operand)
        {
        }

        public void Place(MirPlace place, MirAccess access, MirRefKind kind)
        {
            if (place.IsStatic && body.Statics[~place.Root].Mutable)
            {
                owner.found |= Environment;
            }
        }

        public void Effect(MirEffect effects, int site)
        {
            owner.found |= (effects & (MirEffect.Environment | MirEffect.IntToPointer | MirEffect.RawStaticRead)) != 0 ? Environment : (byte)0;
            owner.found |= (effects & MirEffect.Unknown) != 0 ? Derived : (byte)0;
            if ((effects & (MirEffect.StaticRead | MirEffect.StaticWrite)) != 0 && body.Statics[site].Mutable)
            {
                owner.AddKey(body.Statics[site].Symbol, (effects & MirEffect.StaticWrite) != 0);
            }
        }
    }
}
