// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BoundConstraint Proposition, BindingScope Scope, bool Derivation), CapabilityWork> capabilityNodes = new(
        KimiLibrarySize.Stated.CapabilityNodes);

    private readonly HashSet<(CapabilityWork Source, CapabilityWork Consumer)> capabilityEdges = new();
    private readonly Queue<CapabilityWork> capabilityQueue = new();
    private readonly List<CapabilityWork> pendingCapabilities = []; // The works left Pending (a temporary carrier; removal: R7-C2e2 e2-5b).
    private readonly List<CapabilityWork> capabilityEvaluated = []; // The outermost request's evaluations: the coinductive step's candidates.
    private readonly Queue<CapabilityWork> assumptionQueue = new();
    private bool assuming; // The coinductive step is evaluating (AssumeResidual).
    private bool assumptionVoid; // It read a work never evaluated.
    private int assumptionDepth; // ObligationDepth when its evaluation began: an assumption is not read through a newer obligation.
    private CapabilityWork? capabilityConsumer;
    private bool capabilitiesReady;
    private BindingMode capabilityMode;

    /// <summary>Gets the capability evaluations (both steps) run in this compilation, which the scaling rows read.</summary>
    internal int CapabilityEvaluationWork { get; private set; }

    /// <summary>Gets the coinductive steps (AssumeResidual with a residual) run in this compilation, which the scaling rows read.</summary>
    internal int CoinductiveStepWork { get; private set; }

    /// <summary>Queries compiler-intrinsic Copy for a complete Type under lexical Constraints.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result, without choosing any acquisition operation.</returns>
    public ConstraintProof ProveCopy(BoundType type, Koto context) => this.ProveCopy(type, this.ConstraintScope(context));

    /// <summary>Queries the recursive Owned guarantee, independently of heap/global borrow restrictions.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result; unresolved Origins remain Unknown.</returns>
    public ConstraintProof ProveOwned(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned)), this.ConstraintScope(context));

    /// <summary>Queries compiler-intrinsic PrimitiveInteger (SPEC 8.7.4) under lexical Constraints. Its reader, WrappingIntegerTest,
    /// observes through it that a wrapping integer Type satisfies no PrimitiveInteger (SPEC 3.1.1); no program of that class reaches
    /// the Contract with one.</summary>
    /// <param name="type">The complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The proof result; a proven Type is an owner integer scalar in every instance.</returns>
    public ConstraintProof ProvePrimitiveInteger(BoundType type, Koto context)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.PrimitiveInteger)), this.ConstraintScope(context));

    /// <summary>Queries complete owner Core evidence without inspecting stored fields.</summary>
    /// <param name="type">The normalized complete Type.</param>
    /// <param name="context">The use site providing assumptions.</param>
    /// <returns>The four-valued Sealed proof.</returns>
    public ConstraintProof ProveSealed(BoundType type, Koto context) => this.ProveSealed(type, this.ConstraintScope(context));

    // The Copy and Sealed queries under a scope's premises, such as a definition's effect premises (EffectPremises).
    internal ConstraintProof ProveCopy(BoundType type, BindingScope scope)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Copy)), scope);

    internal ConstraintProof ProveSealed(BoundType type, BindingScope scope) => this.RequestCapability(type, this.Library.Sealed, scope);

    private static bool TryLeafCapability(BoundType type, IntrinsicKind kind, out ConstraintProof result)
    {
        result = ConstraintProof.Unknown;
        if (kind == IntrinsicKind.PrimitiveInteger)
        {
            // SPEC 8.7.4: exactly the twelve owner integer Cores; a symbolic Type is decided by its premises.
            if (type.IsAbstract)
            {
                return false;
            }

            result = type.Kind == BoundTypeKind.Primitive && type.Semantics == SemanticsKind.Owner && type.IsInteger ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (kind == IntrinsicKind.ObjectPayload)
        {
            if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                result = ConstraintProof.Error;
                return true;
            }

            // SPEC 8.7.3: symbolic targets, a Contract's Self among them, are decided by their premises.
            if (type.IsAbstract)
            {
                return false;
            }

            // The judgment is shallow: outer owner Semantics, a Core other than Never, and no inherited opt-out.
            result = type.Semantics != SemanticsKind.Owner || ReferenceEquals(type, BoundType.Never) || type.Symbol?.ObjectPayloadOptOut is not null
                ? ConstraintProof.Refuted : ConstraintProof.Proven;
            return true;
        }

        if (kind == IntrinsicKind.Sealed)
        {
            if (type.Symbol?.Declaration.BindingState == BindingState.Invalid)
            {
                result = ConstraintProof.Error;
                return true;
            }

            if (type.IsAbstract)
            {
                return false;
            }

            result = type.Semantics != SemanticsKind.Owner || ReferenceEquals(type, BoundType.Never) ||
                (type.Symbol?.Declaration is StructKoto structure && (structure.Modifier & ModifierKind.Open) != 0)
                ? ConstraintProof.Refuted : ConstraintProof.Proven;
            return true;
        }

        if (type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary && kind == IntrinsicKind.Copy)
        {
            result = ConstraintProof.Refuted; // SPEC 4.5: Array is Non-Copy for every element Type.
            return true;
        }

        if (type.Semantics == SemanticsKind.Owner && type.Symbol?.LibraryDeclaration is KimiDeclarationId.FixedBuffer or KimiDeclarationId.WriteWindow or KimiDeclarationId.Utf8Writer)
        {
            result = ConstraintProof.Refuted; // Verified raw storage retains an exclusive external dependency.
            return true;
        }

        if (type.Kind == BoundTypeKind.Slice && kind == IntrinsicKind.Copy)
        {
            result = ConstraintProof.Proven;
            return true;
        }

        if (type.Kind == BoundTypeKind.FunctionItem && kind == IntrinsicKind.Copy)
        {
            // SPEC 7.7.4: an Item stores nothing, so it is Copy; it is Owned when its bound arguments and its call signature are, which
            // the structural proof decides (CallSignature).
            result = ConstraintProof.Proven;
            return true;
        }

        if (type.Kind == BoundTypeKind.Primitive || (type.Kind == BoundTypeKind.Function && kind == IntrinsicKind.Copy))
        {
            result = kind == IntrinsicKind.Owned || (type.Kind == BoundTypeKind.Primitive && type.Name != "string") ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        if (type.Kind == BoundTypeKind.Semantics && kind == IntrinsicKind.Copy)
        {
            result = type.Semantics is SemanticsKind.Ref or SemanticsKind.ObjRef or SemanticsKind.Raw ? ConstraintProof.Proven : ConstraintProof.Refuted;
            return true;
        }

        return false;
    }

    // Whether a binder (an atom's, or a Type parameter's declaration) is inherited by `function`, nested in `body`: it is outside the
    // function and is no Container that the function is a member of below the body.
    private static bool Inherited(Koto binder, FunctionKoto function, Koto body)
        => !IsWithin(binder, function) && !(IsWithin(function, binder) && !IsWithin(body, binder));

    // The body a declaration is nested in: its nearest enclosing function or accessor, the execution scope's generated function for a root
    // function (which lies there with the top-level bindings, SPEC 6.1.1); null outside every body (a Container's member).
    private static Koto? EnclosingBody(Koto declaration)
    {
        for (var current = declaration.Parent; current is not null; current = current.Parent)
        {
            if (current is FunctionKoto or PropertyAccessorKoto)
            {
                return current;
            }
        }

        return null;
    }

    // SPEC 15.2.3, 15.3.4 (DECISIONS BU-73): a callable's call signature as an Owned part, with the binder of its own per-call Origins: a
    // Function Type that quantifies an input is its own, under its binder (BoundType.Binder), at every instance; a Closure's is its value's (ClosureSignature), under its literal,
    // which every Closure Type names (ClosureType); a Function Item's, for a function nested in a body, root functions included
    // (EnclosingBody), is what it inherits (InheritedEnvironment), read from its declaration, and none for any other function, whose bound
    // arguments are its whole dependence.
    // A signature is unreadable, none, where its header did not bind (an Item's function, the Type BindConcreteClosure starts with) or
    // BU-60's view withholds a Closure's held instance (an Origin position closed in no instance, PLAN G300).
    private (BoundType? Signature, Koto Binder)? CallSignature(BoundType type)
    {
        if (type.Kind == BoundTypeKind.Function)
        {
            return type.Binder is { } binder ? (type, binder) : null;
        }

        if (type.Kind is not (BoundTypeKind.Closure or BoundTypeKind.FunctionItem))
        {
            return null;
        }

        if (type.Symbol?.Declaration is not FunctionKoto definition)
        {
            throw new InvalidOperationException($"Callable Type `{type}` names no function (InternalInvariant; BU-73).");
        }

        if (type.Kind == BoundTypeKind.FunctionItem)
        {
            return EnclosingBody(definition) is { } body ? (this.InheritedEnvironment(definition, body), definition) : null;
        }

        return (type.InstanceContext is not null && TyCtxt.NestedInstance(type) is null ? null : this.ClosureSignature(type), definition);
    }

    private void ResetCapabilities(BindingMode mode)
    {
        this.capabilitiesReady = false;
        this.capabilityMode = mode;
        this.readings.Clear();
        this.ClearClosure();
        this.ResetIdentityClasses();
        this.ClearNeededSites();
        this.ClearProofScopes();
        this.ClearCapabilityResults();
    }

    private void ClearCapabilityResults()
    {
        this.summaryEpoch++; // Component summaries follow the capability results' lifecycle.
        this.ClearKeptRows(); // So do Enumerate's kept rows (ContractSolver).
        this.capabilityQueue.Clear();
        this.capabilityEdges.Clear();
        this.pendingCapabilities.Clear();
        this.capabilityEvaluated.Clear();
        this.assumptionQueue.Clear();
        this.assuming = false;
        this.capabilityConsumer = null;
        foreach (var work in this.capabilityNodes.Values)
        {
            work.Reset();
        }
    }

    // R7-C3 (C7): a capability work is keyed by the subject its judge judged (CapabilityKey): the proposition in its form in the view
    // (ProveProposition's normalization, J1), its subject read as its class's Member (RequestCapability). A reader of the work the judge
    // formed for a proposition it asked reads that key, never the proposition as written (rv.tp3-u: an anonymous function's header
    // under `U is W` forms the erasure subject).
    private (BoundConstraint Proposition, BindingScope Scope, bool Derivation) JudgedCapabilityKey(BoundConstraint asked, BindingScope scope)
    {
        var judged = this.NormalizeProofConstraint(asked, scope, out _);
        return this.CapabilityKey(this.MemberOf(judged.Subject!, scope, out _), judged.Contract!, scope, derivation: false);
    }

    // The one key of capability works: the judged subject (its class's Member) and the intrinsic Contract in the view.
    private (BoundConstraint Proposition, BindingScope Scope, bool Derivation) CapabilityKey(BoundType member, BindingSymbol intrinsic, BindingScope scope, bool derivation)
        => (this.InternConstraint(new(ConstraintKind.Contract, member, contract: intrinsic)), scope, derivation);

    private ConstraintProof RequestCapability(BoundType type, BindingSymbol intrinsic, BindingScope scope, bool derivation = false)
    {
        var contexts = this.OperandContexts(type);
        if (contexts.Unresolved)
        {
            return contexts.Invalid ? ConstraintProof.Error : ConstraintProof.Unknown;
        }

        type = this.MemberOf(type, scope, out _); // R7-C2c2 c2-4 (design R1.1): the capability judge's subject is its class's Member

        if (!this.capabilitiesReady)
        {
            if (this.running && !derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var concrete))
            {
                return this.WithClassPremises(concrete, type, intrinsic, scope);
            }

            return ConstraintProof.Unknown;
        }

        if (!derivation && TryLeafCapability(type, intrinsic.Intrinsic, out var leaf))
        {
            return this.WithClassPremises(leaf, type, intrinsic, scope);
        }

        // A proof can re-enter a declaration with ever larger arguments: Copy's conditional premise over a projection, and Owned's
        // projection normalized at the use through a conformance binding that uses itself (SPEC 8.3.3, DECISIONS BU-22); the second
        // growth step stays Unknown. Removal condition: SPEC 8.3.3's proof-depth limit is a resource failure (PLAN G200).
        if (!derivation && type.Symbol is not null)
        {
            var growing = false;
            var depth = type.Height;
            for (var ancestor = this.capabilityConsumer; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor.Type.Symbol, type.Symbol) && !ReferenceEquals(ancestor.Type, type))
                {
                    var previous = ancestor.Type.Height;
                    if (depth > previous)
                    {
                        if (growing)
                        {
                            return ConstraintProof.Unknown;
                        }

                        growing = true;
                        depth = previous;
                    }
                }
            }
        }

        var key = this.CapabilityKey(type, intrinsic, scope, derivation);
        if (!this.capabilityNodes.TryGetValue(key, out var work))
        {
            work = new(type, intrinsic, scope, derivation);
            this.capabilityNodes.Add(key, work);
        }

        if (!work.Evaluated && !work.Queued)
        {
            work.Parent = this.capabilityConsumer;
            work.Queued = true;
            this.capabilityQueue.Enqueue(work);
        }

        if (this.capabilityConsumer is { } consumer)
        {
            if (this.capabilityEdges.Add((work, consumer)))
            {
                work.Dependents.Add(consumer);
            }

            consumer.Reads.Add(work);
            if (this.assuming)
            {
                if (!work.Evaluated)
                {
                    this.assumptionVoid = true; // Queued above: the inductive step evaluates it, then the step runs again.
                }
                else if (work.Assumed && consumer.Assumed && this.ObligationDepth == this.assumptionDepth)
                {
                    return ConstraintProof.Proven;
                }
            }

            // The verdict alone: what the reader's own Cause rests on is settled after the verdicts (SettleCauses).
            this.ScopeReads.Origin ??= work.Result == ConstraintProof.Proven ? null : work.Origin;
            return this.ReadVerdict(work.Result, null, work.Fact);
        }

        this.BeginProof();
        try
        {
            do
            {
                this.EvaluateCapabilities();
            }
            while (this.AssumeResidual());
            this.SettleCauses();
        }
        finally
        {
            this.capabilityConsumer = null;
            this.assuming = false;
            this.capabilityEvaluated.Clear();
        }

        this.ScopeReads = default;
        this.ScopeReads.Origin = work.Result == ConstraintProof.Proven ? null : work.Origin;
        var verdict = this.EndProof(this.ReadVerdict(work.Result, work.Cause, work.Fact));
        this.ForgetPendingCapabilities();
        return verdict;
    }

    // The inductive step (SPEC 8.3.3): the worklist from the committed verdicts. Every work starts Unknown and each evaluation is
    // monotone in the information order (Unknown below Proven and Refuted, Error above both), so a verdict only rises, at most twice.
    private void EvaluateCapabilities()
    {
        while (this.capabilityQueue.TryDequeue(out var next))
        {
            next.Queued = false;
            next.Evaluated = true;
            this.capabilityEvaluated.Add(next);
            this.capabilityConsumer = next;
            this.ScopeReads = default;
            next.Parts?.Clear();
            next.Reads.Clear();
            var reads = this.reentryReads;
            this.CapabilityEvaluationWork++;
            var result = this.ComputeCapability(next);
            next.Pending = this.reentryReads != reads;
            if (next.Pending)
            {
                this.pendingCapabilities.Add(next);
            }

            next.OwnCause = this.ProofCause(result); // Payload of the final iteration, outside the fixpoint's equality, as Fact.
            next.Fact = this.ProofFact(result);
            next.Origin = result == ConstraintProof.Proven ? null : this.ScopeReads.Origin;
            if (result == next.Result)
            {
                continue;
            }

            next.Result = result;
            this.RequeueDependents(next);
        }

        this.capabilityConsumer = null;
    }

    // The nominal obligations open (the open set's and the conformance verifications in progress, the two guards of SPEC 8.3.3
    // Recursion): an assumption is not read through one opened during the evaluation that reads it (AssumeResidual).
    private int ObligationDepth => this.openObligations.Count + this.conformanceChecks;

    // SPEC 8.3.3 Limits, 23.1.1 (DECISIONS BU-22): each Unknown or Error work of the request rests on the least Cause by CauseKey among
    // its own (what its last evaluation read from anything but a work) and those of the works it read that settled at its verdict.
    // The verdicts are settled, so this is a function of them: every work starts at its own Cause and only falls, at most once per
    // Cause it can reach, and the least reachable one is the same in any order. A change re-queues only this pass's readers.
    private void SettleCauses()
    {
        var evaluated = this.capabilityEvaluated;
        for (var i = 0; i < evaluated.Count; i++)
        {
            var work = evaluated[i];
            var rests = work.Result is ConstraintProof.Unknown or ConstraintProof.Error;
            work.Cause = rests ? work.OwnCause : null;
            if (rests && !work.Queued)
            {
                work.Queued = true;
                this.capabilityQueue.Enqueue(work);
            }
        }

        while (this.capabilityQueue.TryDequeue(out var next))
        {
            next.Queued = false;
            var cause = next.Cause;
            for (var i = 0; i < next.Reads.Count; i++)
            {
                var read = next.Reads[i];
                if (read.Result == next.Result && CauseKey.Less(read.Cause, cause))
                {
                    cause = read.Cause;
                }
            }

            if (ReferenceEquals(cause, next.Cause))
            {
                continue;
            }

            next.Cause = cause;
            for (var i = 0; i < next.Dependents.Count; i++)
            {
                if (next.Dependents[i] is { Queued: false } reader && reader.Result == next.Result)
                {
                    reader.Queued = true;
                    this.capabilityQueue.Enqueue(reader);
                }
            }
        }
    }

    // A fixed total order on Causes (SettleCauses): by the source of the syntax a record rests on, its position, its syntax and the kind
    // of Cause, then by the same key along what a record lists beside it, its next candidates and its re-entry site (DECISIONS BU-76), so
    // two hop chains of one path differ by key; a missing Cause is above every Cause. Causes equal by key rest on the same nodes.
    private static class CauseKey
    {
        internal static bool Less(ICheckCause? left, ICheckCause? right) => Compare(left, right) < 0;

        private static int Compare(ICheckCause? left, ICheckCause? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null || right is null)
            {
                return left is null ? 1 : -1;
            }

            var order = string.CompareOrdinal(left.Use.CodeContext.SourceName, right.Use.CodeContext.SourceName);
            order = order != 0 ? order : left.Use.Span.Start.CompareTo(right.Use.Span.Start);
            order = order != 0 ? order : left.Use.SyntaxId.CompareTo(right.Use.SyntaxId);
            order = order != 0 ? order : Kind(left).CompareTo(Kind(right));
            order = order != 0 ? order : Compare(left.Next, right.Next);
            return order != 0 ? order : Compare(left.ReentrySite, right.ReentrySite);
        }

        private static int Kind(ICheckCause cause)
            => cause switch { ComponentSite => 0, ComponentSummary => 1, BoundConformancePath => 2, ResolutionHops => 3, _ => 4 };
    }

    private void RequeueDependents(CapabilityWork work)
    {
        for (var i = 0; i < work.Dependents.Count; i++)
        {
            var dependent = work.Dependents[i];
            if (!dependent.Queued)
            {
                dependent.Queued = true;
                this.capabilityQueue.Enqueue(dependent);
            }
        }
    }

    // The coinductive step (SPEC 15.3.5: Owned's structure is a greatest fixed point; SPEC 8.3.3: a nominal or Copy judgment assumes no
    // coinduction). The residual, this request's coinductive works the inductive step left Unknown, is assumed Proven: each is evaluated
    // again with the residual's verdicts read as Proven by residual works alone, never through another work or an obligation opened in
    // the evaluation; one that is not Proven so leaves the residual, and its residual readers are evaluated again. What stays is the
    // greatest such set, whatever the order (a Proven verdict survives more assumptions); it is committed Proven, an information rise,
    // and the inductive step resumes. A read of a work never evaluated voids the step: the inductive step evaluates it, then the step
    // runs again. Returns whether the inductive step must run; the rises bound the rounds, the works the voids.
    private bool AssumeResidual()
    {
        var evaluated = this.capabilityEvaluated;
        for (var i = 0; i < evaluated.Count; i++)
        {
            var work = evaluated[i];
            if (work.Coinductive && work.Result == ConstraintProof.Unknown && !work.Pending && !work.Assumed && work.Evaluated)
            {
                work.Assumed = true;
                work.Queued = true;
                this.assumptionQueue.Enqueue(work);
            }
        }

        if (this.assumptionQueue.Count == 0)
        {
            return false;
        }

        this.assuming = true;
        this.assumptionVoid = false;
        this.CoinductiveStepWork++;
        while (!this.assumptionVoid && this.assumptionQueue.TryDequeue(out var next))
        {
            next.Queued = false;
            if (!next.Assumed)
            {
                continue;
            }

            this.capabilityConsumer = next;
            this.ScopeReads = default;
            next.Parts?.Clear();
            next.Reads.Clear();
            var reads = this.reentryReads;
            this.assumptionDepth = this.ObligationDepth;
            this.CapabilityEvaluationWork++;
            var result = this.ComputeCapability(next);
            this.capabilityConsumer = null;
            if (result != ConstraintProof.Proven || this.reentryReads != reads)
            {
                next.Assumed = false;
                for (var i = 0; i < next.Dependents.Count; i++)
                {
                    if (next.Dependents[i] is { Assumed: true, Queued: false } reader)
                    {
                        reader.Queued = true;
                        this.assumptionQueue.Enqueue(reader);
                    }
                }
            }
        }

        this.assuming = false;
        var committed = false;
        foreach (var work in this.assumptionQueue)
        {
            work.Queued = false;
        }

        this.assumptionQueue.Clear();
        for (var i = 0; i < evaluated.Count; i++)
        {
            var work = evaluated[i];
            if (!work.Coinductive || work.Result != ConstraintProof.Unknown || work.Pending)
            {
                continue;
            }

            if (work.Assumed && !this.assumptionVoid)
            {
                work.Assumed = false;
                work.Result = ConstraintProof.Proven;
                work.Cause = null;
                work.Origin = null;
                work.Fact = null;
                committed = true;
                this.RequeueDependents(work);
                continue;
            }

            // Its payload is the step's: the inductive step judges it again from the committed verdicts.
            work.Assumed = false;
            if (!work.Queued)
            {
                work.Queued = true;
                this.capabilityQueue.Enqueue(work);
            }
        }

        if (!committed && !this.assumptionVoid)
        {
            // Nothing holds by coinduction: the residual is final at Unknown; one more inductive pass restores its payloads.
            this.EvaluateCapabilities();
            return false;
        }

        return true;
    }

    // SPEC 8.3.3 Finality (R7-C2e2 e2-5a): an evaluation that read an open obligation (reentryReads) supplied no evidence there, and
    // that obligation's proof may still hold; so it, and every work that read its verdict, is no fact once that proof ends. None is kept
    // past the outermost request, which has answered its requester inside that proof: each returns to its initial state and is
    // evaluated again when next requested. A work whose last evaluation read none stays, unless it read one that did. A forgotten work
    // stays in its sources' Dependents and edges: a later change of a source evaluates it again with no Parent, which only the growth
    // guard reads (no verdict depends on it). Temporary carrier with reentryReads; removal: R7-C2e2 e2-5b, with e2-3's Completion.
    private void ForgetPendingCapabilities()
    {
        var pending = this.pendingCapabilities;
        for (var i = 0; i < pending.Count; i++)
        {
            var work = pending[i];
            if (!work.Pending)
            {
                continue;
            }

            foreach (var dependent in work.Dependents)
            {
                this.capabilityEdges.Remove((work, dependent));
                if (dependent.Evaluated && !dependent.Pending)
                {
                    dependent.Pending = true;
                    pending.Add(dependent);
                }
            }

            work.Reset();
        }

        pending.Clear();
    }

    private ConstraintProof ComputeCapability(CapabilityWork work)
        => this.WithClassPremises(this.CapabilityByStructure(work), work.Type, work.Intrinsic, work.Scope);

    // R7-C2c2 c2-4 (SPEC 8.2.3, 8.3.2): a constructed Type's premise rows are its identity class's (ClassKey), joined with its closed
    // world by PremiseJoin; every answer of the capability judge for a constructed Type passes here (the leaf shortcuts and the
    // computed work). An abstract Type reads its premises in SymbolicCapability.
    private ConstraintProof WithClassPremises(ConstraintProof proof, BoundType type, BindingSymbol intrinsic, BindingScope scope)
        => PremiseJoin(proof != ConstraintProof.Error && !type.IsAbstract && HasTypeIdentity(scope) && this.PremiseRefines(type, intrinsic, scope), proof);

    private ConstraintProof CapabilityByStructure(CapabilityWork work)
    {
        var type = work.Type;
        if (this.OperandContexts(type).Invalid)
        {
            return ConstraintProof.Error;
        }

        if (TryLeafCapability(type, work.Intrinsic.Intrinsic, out var leaf))
        {
            return leaf;
        }

        if (type.Kind == BoundTypeKind.SemanticsAdaptation)
        {
            var result = ConstraintProof.Unknown;
            for (var i = 0; i < type.Components.Count; i++)
            {
                var child = this.RequestCapability(type.Components[i], work.Intrinsic, work.Scope);
                if (child == ConstraintProof.Error)
                {
                    return child;
                }

                result = i == 0 || result == child ? child : ConstraintProof.Unknown;
            }

            return result;
        }

        if (type.IsAbstract)
        {
            // A Contract's Self, a Type parameter, has no structure of its own; its ObjectPayload evidence is the Contract's clause
            // (SPEC 8.7.3).
            return this.SymbolicCapability(work);
        }

        if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy)
        {
            if (type.Symbol?.Declaration is DeclarationContainerKoto container)
            {
                if (work.Derivation)
                {
                    if (container.BoundSymbol?.Adt is not { } shape)
                    {
                        return ConstraintProof.Unknown;
                    }

                    return CombineProof(shape.DestructorKoto is not null ? ConstraintProof.Refuted : ConstraintProof.Proven, this.StoredCapability(work, shape), true);
                }

                return this.InstanceCapability(work, this.NominalCopy(work, container));
            }
        }
        else
        {
            if (work.Derivation)
            {
                return this.StorageFormation(work, type.Symbol!);
            }

            var origin = OwnedOrigin(type) is not { } owned ? ConstraintProof.Proven : this.StaticOrigin(owned, work.Scope);
            return this.InstanceCapability(work, CombineProof(origin, this.StructuralCapability(work), true));
        }

        return this.InstanceCapability(work, this.StructuralCapability(work));
    }

    // The capability engine stays coinductive over structure (SPEC 15.3.5); the formation of an instance it reads is a
    // nominal judgment, read inductively through the solver's re-entry rule (InstanceFormation, SPEC 8.6.1, 8.3.3).
    private ConstraintProof InstanceCapability(CapabilityWork work, ConstraintProof result)
        => result == ConstraintProof.Proven ? CombineProof(result, this.InstanceFormation(work.Type, work.Scope), true) : result;

    private ConstraintProof NominalCopy(CapabilityWork work, DeclarationContainerKoto container)
    {
        if (container.BoundSymbol?.Adt is not { } shape)
        {
            return ConstraintProof.Unknown;
        }

        for (var i = 0; i < shape.Types.Count; i++)
        {
            if (shape.Types[i].BindingState == BindingState.Invalid)
            {
                return ConstraintProof.Error;
            }

            if (shape.Types[i].BoundType is null)
            {
                return ConstraintProof.Unknown;
            }
        }

        if (!this.copyByType.TryGetValue(container, out var declarations) || declarations.Count == 0)
        {
            return this.capabilityMode == BindingMode.Final ? ConstraintProof.Refuted : ConstraintProof.Unknown;
        }

        var result = ConstraintProof.Refuted;
        for (var i = 0; i < declarations.Count; i++)
        {
            var declaration = declarations[i];
            var validation = this.RequestCapability(this.SelfType(container.BoundSymbol!), this.Library.Copy, declaration.Scope, derivation: true);
            if (declaration.Clause.BindingState == BindingState.Invalid || validation is ConstraintProof.Error or ConstraintProof.Refuted)
            {
                return ConstraintProof.Error;
            }

            var condition = this.ProveConditionalPremises(declaration.Premises, work.Type, work.Scope);

            result = CombineProof(result, CombineProof(validation, condition, true), false);
        }

        return result;
    }

    private ConstraintProof StructuralCapability(CapabilityWork work)
    {
        // A Function Type is its call signature: only its own quantified Origins are closed, so querying a component separately still
        // observes its free lifetime dependency.
        var call = work.Intrinsic.Intrinsic == IntrinsicKind.Owned ? this.CallSignature(work.Type) : null;
        if (call is { Signature: { } own, Binder: var binder } && ReferenceEquals(own, work.Type))
        {
            return this.SignatureCapability(work, own, binder);
        }

        if (work.Type.Kind is BoundTypeKind.Array or BoundTypeKind.Dictionary)
        {
            // SPEC 4.5: collection ownership follows every stored Type, even when empty.
            if (work.Intrinsic.Intrinsic != IntrinsicKind.Owned)
            {
                return work.Intrinsic.Intrinsic == IntrinsicKind.Copy ? ConstraintProof.Refuted : ConstraintProof.Unknown;
            }

            var owned = ConstraintProof.Proven;
            foreach (var (part, _, _) in new CapabilityParts(work.Type, BoundType.Unit))
            {
                owned = part is null ? owned : CombineProof(owned, this.RequestCapability(part, work.Intrinsic, work.Scope), true);
            }

            return owned;
        }

        if (JudgedByDeclaration(work.Type))
        {
            return this.SummaryCapability(work, (DeclarationContainerKoto)work.Type.Symbol!.Declaration);
        }

        // Owned records each part that is not Proven with its member and Origin (CapabilityWork.Parts, R4-M161).
        var result = ConstraintProof.Proven;
        // A nested definition's inherited arguments are OwnedOrigins' parts (SPEC 15.2.3), so Owned reads them; Copy reads a Closure's
        // captures only (SPEC 3.2.1). An unreadable instance (InheritedArguments) answers BU-22's limit with the definition as the site.
        var inherited = work.Intrinsic.Intrinsic == IntrinsicKind.Owned ? this.InheritedArguments(work.Type) : BoundType.Unit;
        if (inherited is null)
        {
            var saved = this.ScopeReads.Origin;
            this.ScopeReads.Origin = null;
            var proof = this.ReadVerdict(ConstraintProof.Unknown, work.Site ??= new(this.neededSites) { Use = work.Type.Symbol!.Declaration }, null);
            result = CombineProof(result, proof, true);
            this.ScopeReads.Origin = work.Part(work.Type.Components.Count, proof, this.ScopeReads.Origin, saved);
        }

        var parts = new CapabilityParts(work.Type, inherited ?? BoundType.Unit);
        while (parts.MoveNext())
        {
            var (part, origin, argument) = parts.Current;
            var saved = this.ScopeReads.Origin;
            this.ScopeReads.Origin = null;
            var proof = part is null ? (work.Intrinsic.Intrinsic == IntrinsicKind.Owned ? this.StaticOrigin(origin, work.Scope) : ConstraintProof.Proven)
                : argument ? this.RequestCapability(part, work.Intrinsic, work.Scope)
                : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, part, contract: work.Intrinsic)), work.Scope);
            result = CombineProof(result, proof, true);
            this.ScopeReads.Origin = work.Part(parts.Member, proof, this.ScopeReads.Origin, saved);
        }

        // A Closure's call signature, or what a Function Item inherits (InheritedEnvironment), is a part after its captures or bound
        // arguments, member Components.Count; an unreadable one (CallSignature) answers BU-22's limit with its definition as the site, on
        // which a failed definition's use rests (DECISIONS BU-73).
        if (call is { } closure)
        {
            var saved = this.ScopeReads.Origin;
            this.ScopeReads.Origin = null;
            var proof = closure.Signature is { } signature ? this.SignatureCapability(work, signature, closure.Binder)
                : this.ReadVerdict(ConstraintProof.Unknown, work.Site ??= new(this.neededSites) { Use = closure.Binder }, null);
            result = CombineProof(result, proof, true);
            this.ScopeReads.Origin = work.Part(work.Type.Components.Count, proof, this.ScopeReads.Origin, saved);
        }

        return result;
    }

    // SPEC 15.2.3, 15.8.2 (DECISIONS BU-73): a callable signature is Owned when it is with its own per-call Origins closed to static: its
    // direct inputs, its receiver and its call result (PerCallImages); every other Origin it names is fixed.
    private ConstraintProof SignatureCapability(CapabilityWork work, BoundType signature, Koto binder)
    {
        var count = InputOriginCount(binder);
        var inputs = this.originScratch.Rent(count);
        try
        {
            inputs.AsSpan(0, count).Fill(BoundOrigin.Static);
            var images = new PerCallImages(inputs.AsSpan(0, count), BoundOrigin.Static, BoundOrigin.Static);
            return this.RequestCapability(this.SubstituteStoredOrigins(signature, binder, [], images), work.Intrinsic, work.Scope);
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
        }
    }

    // SPEC 15.3.4 (last bullet), 15.2.3: what a named function nested in a body (a root function in the execution scope's) depends on by
    // naming it, beyond its inherited parameters (CapabilityParts' environment parts, SPEC 6.1.3), read from its declaration as its Item is
    // formed: the Origins its declared signature names whose binder lies outside it and is no Container it is a member of below that body,
    // an enclosing Container's Origin slot excepted (an inherited parameter), as one environment Tuple (EnvironmentTuple): an enclosing
    // function's Origins (SPEC 15.3: no function Origin parameter list) and a local's. Atoms bound inside its declaration (its own Origins,
    // a Constraint set's slot, a nested Function Type's inputs) are per-call, and a Type a projection or a bound argument derives from them
    // is never judged itself (15.2.3 counts the arguments). Null when a declared Type did not bind.
    private BoundType? InheritedEnvironment(FunctionKoto function, Koto body)
    {
        if (function.BoundSymbol?.Type is not { } result)
        {
            return null;
        }

        var count = function.Parameters.Count;
        var declared = this.RentTypes(count + 1);
        try
        {
            for (var i = 0; i < count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter)
                {
                    return null;
                }

                declared[i] = parameter;
            }

            declared[count] = result;
            var signature = this.types.Tuple(declared.AsSpan(0, count + 1));
            var summary = this.SummaryOf(signature);
            var origins = this.originScratch.Rent(summary.Nodes.Length + summary.RawNodes.Length);
            try
            {
                var o = InheritedOrigins(summary.Nodes, function, body, origins, 0);
                o = InheritedOrigins(summary.RawNodes, function, body, origins, o);
                return this.EnvironmentTuple([], origins.AsSpan(0, o));
            }
            finally
            {
                this.originScratch.Return(origins, clearArray: true);
            }
        }
        finally
        {
            this.typeScratch.Return(declared, clearArray: true);
        }

        static int InheritedOrigins(BoundOrigin[] nodes, FunctionKoto function, Koto body, BoundOrigin[] into, int count)
        {
            for (var i = 0; i < nodes.Length; i++)
            {
                if (nodes[i] is { Kind: not (OriginKind.Static or OriginKind.Intersection), Binder: { } binder } atom && Inherited(binder, function, body) &&
                    !(atom.Kind == OriginKind.Parameter && binder is DeclarationContainerKoto))
                {
                    into[count++] = atom;
                }
            }

            return count;
        }
    }

    // Copy's derivation over the stored Types of a declaration's Self (SPEC 3.5.2); Owned reads component summaries.
    private ConstraintProof StoredCapability(CapabilityWork work, AdtDef shape)
    {
        var result = ConstraintProof.Proven;
        for (var i = 0; i < shape.Types.Count; i++)
        {
            var type = this.StoredType(shape.Types[i], work.Type);
            var proof = shape.Types[i].BindingState == BindingState.Invalid ? ConstraintProof.Error : type is null ? ConstraintProof.Unknown : this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: work.Intrinsic)), work.Scope);
            if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy && type is not null && this.HoldsHolder(type))
            {
                proof = ConstraintProof.Refuted; // SPEC 5.7: a `during self` layer makes its Type non-Copy.
            }

            result = CombineProof(result, proof, true);
        }

        return result;
    }

    private ConstraintProof SymbolicCapability(CapabilityWork work)
    {
        var negative = false;
        var subject = work.Type; // its class's Member and rows reach it through RequestCapability's MemberOf and the supply's keys
        var positive = this.PremiseRefines(subject, work.Intrinsic, work.Scope); // SPEC 8.3.2's refinement row (ContractSolver).
        for (var scope = work.Scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Constraints is not { } environment)
            {
                continue;
            }

            if (environment.Invalid)
            {
                return this.InvalidEnvironment(environment);
            }

            foreach (var available in environment.Facts)
            {
                if (!this.AvailableConstraintFact(environment, available, out _))
                {
                    continue;
                }

                var fact = available;
                var appliedSemantics = fact.Kind == ConstraintKind.Semantics && work.Type.Kind == BoundTypeKind.SemanticsApplication && ReferenceEquals(fact.Subject, work.Type.Symbol?.WholeType);
                if (!this.FactStates(fact, subject, out var stated) && !appliedSemantics)
                {
                    continue;
                }

                var evidence = ConstraintProof.Unknown;
                if (fact.Kind == ConstraintKind.Semantics)
                {
                    const SemanticsMask copy = SemanticsMask.Ref | SemanticsMask.ObjRef | SemanticsMask.Raw;
                    const SemanticsMask nonCopy = SemanticsMask.Uniq | SemanticsMask.ObjUniq | SemanticsMask.Object;
                    if (fact.Mask == SemanticsMask.Owner)
                    {
                        var target = appliedSemantics ? work.Type.Components[0] : work.Type.Symbol?.Type;
                        if (target is not null && !ReferenceEquals(target, work.Type))
                        {
                            evidence = this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, target, contract: work.Intrinsic)), work.Scope);
                        }
                    }
                    else if (work.Intrinsic.Intrinsic == IntrinsicKind.Copy)
                    {
                        evidence = (fact.Mask & ~copy) == 0 ? ConstraintProof.Proven : (fact.Mask & ~nonCopy) == 0 ? ConstraintProof.Refuted : ConstraintProof.Unknown;
                    }
                    else if (work.Intrinsic.Intrinsic is IntrinsicKind.Sealed or IntrinsicKind.ObjectPayload && (fact.Mask & SemanticsMask.Owner) == 0)
                    {
                        evidence = ConstraintProof.Refuted;
                    }
                    else if (work.Intrinsic.Intrinsic == IntrinsicKind.Owned && fact.Mask == SemanticsMask.Raw)
                    {
                        var target = appliedSemantics ? work.Type.Components[0] : work.Type.Symbol?.Type;
                        if (target is not null && !ReferenceEquals(target, work.Type))
                        {
                            evidence = this.RequestCapability(target, work.Intrinsic, work.Scope);
                        }
                    }
                }

                if (evidence == ConstraintProof.Error)
                {
                    return evidence;
                }

                positive |= evidence == ConstraintProof.Proven;
                negative |= evidence == ConstraintProof.Refuted;
            }
        }

        return positive && negative ? ConstraintProof.Error : positive ? ConstraintProof.Proven : negative ? ConstraintProof.Refuted : ConstraintProof.Unknown;
    }
}
