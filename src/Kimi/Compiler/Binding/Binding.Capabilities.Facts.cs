// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The verdict a capability work records (SPEC 8.3.3, 15.2.3; DECISIONS BU-22, R4-M155, R4-M161), a part of the capability engine.
// - Responsibility: a work's state in the fixpoint (CapabilityWork: its proposition's Type, intrinsic and scope, its Result and Cause,
//   its queue state, its dependents) and the payload its verdict carries: the U fact, and for Owned the first Origin it rests on and each
//   part that is not Proven with its member, Origin and Type (CapabilityPart, recorded through CapabilityWork.Part on every path that
//   combines an Owned verdict); the judgment of one OwnedOrigins Origin against `static` (StaticOrigin); the payload's one reader, an
//   Owned query's rest (ProveOwned with OwnedRest: the subject's work, whose parts the erasure walks through each part's own work,
//   OwnedWork).
// - Inputs: the engine's evaluations (Binding.Capabilities: RequestCapability's dequeue loop writes a work's verdict and payload; the
//   Origin slot of the proof scope in progress, ProofReads.Origin of the channel-owned stack through ScopeReads,
//   Binding.ConstraintValidation.Proofs).
// - Outputs: a work's Result, which the fixpoint compares, its Cause, settled after the verdicts as a function of them, and its payload,
//   which the fixpoint never compares: the erasure's Owned owner reads it, for a call's applicability (SPEC 10.1 step 5,
//   ErasedArgumentsEnvironment) and for the conversion's record (OwnedMember). A part is kept only when it is not Proven, so premises
//   that prove a part leave none.
// - Determinism: a dependent is queued again whenever a part's Result changes, so the final evaluation sees each part's final
//   verdict; a part's Origin can still change without a re-queue, and a Result only rises in the information order (every work starts
//   Unknown; Binding.Capabilities), so a copied Origin still fails (it may not be the first), as for the U fact; the queue order is
//   deterministic.
// - Invariants: a payload is the final evaluation's, written beside the Result and outside the fixpoint's equality; Reset returns a work
//   to its initial verdict for a new pass; an OwnedOrigins Origin that is null or not proven to outlive `static` is Unknown, and each
//   caller keeps its own absence rule (a missing outer Origin is Proven).
// - Prohibitions: no proof of the engine reads a payload; nothing here schedules or evaluates a work (Binding.Capabilities).
public sealed partial class Binding
{
    // The node a judgment in `scope` is made at: its owner, or a function's body for the function's own scope (its literal is a node of the
    // enclosing body, so a reading there would take the enclosing body's `let` regions as values).
    private static Koto JudgedAt(BindingScope scope)
        => scope.Owner is FunctionKoto function && ((Koto?)function.Body ?? function.ExpressionBody) is { } body ? body : scope.Owner;

    // The first local region of an Origin or of its meet's operands.
    private static BoundOrigin? RegionOperand(BoundOrigin origin)
    {
        if (IsLocalRegion(origin))
        {
            return origin;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (RegionOperand(origin.Operands[i]) is { } region)
            {
                return region;
            }
        }

        return null;
    }

    // SPEC 15.2.3: an Origin of a Type's OwnedOrigins is Owned when it is proven to outlive `static`, each region the judgment's body reads
    // as its value read so (ValueIn: a `let` region of that body, a `var` region of it once it is bound, a region the body carries; R7-O2
    // A1+A2), a region a nested signature names staying the region (SPEC 15.3.4); a null Origin argument and an unproven Origin are Unknown.
    // An Unknown whose reading met a region with no answer yet (ValueIn's deferral fact) is no verdict: it depends on the body's binding
    // (SPEC 8.3.3 Finality: ReadBodyBinding), and the erasure that asked judges its Owned part once the body is bound (JudgeErasedOwned;
    // the owner's O-471 (a), O-481 (b)); the fact stays set for that asker. A function's scope judges in its body (JudgedAt): its literal
    // stands in the enclosing body, whose `let` regions are not its values (D-862's e-owsin).
    private ConstraintProof StaticOrigin(BoundOrigin? origin, BindingScope scope)
    {
        var held = this.readsUnboundedRegion;
        this.readsUnboundedRegion = false;
        var read = origin is null ? null : this.ValueIn(origin, scope.Function);
        if (read is not null && (this.ProvesOriginOutlives(read, BoundOrigin.Static, JudgedAt(scope)) || this.SolvedStatic(read, scope.Function)))
        {
            this.readsUnboundedRegion = held;
            return ConstraintProof.Proven;
        }

        if (this.readsUnboundedRegion)
        {
            this.ReadBodyBinding();
        }

        this.readsUnboundedRegion |= held;
        this.ScopeReads.Origin ??= origin;
        return ConstraintProof.Unknown;
    }

    // SPEC 15.2.3, 15.3.6, 8.3.3 (DECISIONS: G572 U2a, R7-O2 A1+A2): whether a call's open region of the judgment's body (a region with no
    // slot value: J2's waited regions, BU-136's), in `origin` or a meet's operand, is `static`, the oracle's reading of it through its sources
    // (UniversalRelations.Outlives, SourcesOutlive), once its bounds are complete (RegionBounded: its body is bound, or its call completed
    // it, R1-a); a region slot is read through ValueIn alone (its
    // candidate in a body that reads it so; a region a body only names stays the region, e-ownf). Removed with PLAN G936's late solve.
    private bool SolvedStatic(BoundOrigin origin, Koto? function)
    {
        if (RegionOperand(origin) is not { Binder: { } binder } region || SlotRegion(region) || !ReferenceEquals(BodyOwner(binder), function) ||
            !this.RegionBounded(region))
        {
            return false;
        }

        using var relations = this.UniversalRelationsOf(region.Body);
        relations.Close(BoundOrigin.Static, 0);
        return relations.Outlives(origin);
    }

    // An Owned query that also returns its subject's rest (R4-M161): the work that judged it, whose parts are those it found not Proven;
    // none without a work (a context that is not resolved returns before one exists).
    private ConstraintProof ProveOwned(BoundType type, Koto context, out OwnedRest rest)
    {
        var proposition = this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned));
        var scope = this.ConstraintScope(context);
        var proof = this.ProveConstraint(proposition, scope, context);
        rest = new(this.OwnedWork(type, scope), scope);
        return proof;
    }

    // The work of Owned over `type` in `scope`, whose rest a part's walk reads (OwnedMember), read by the key its judge judged
    // (JudgedCapabilityKey, R7-C3 C7); none if no work judged it.
    private CapabilityWork? OwnedWork(BoundType type, BindingScope scope)
        => this.capabilityNodes.TryGetValue(this.JudgedCapabilityKey(this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned)), scope), out var work) ? work : null;

    private sealed class CapabilityWork(BoundType type, BindingSymbol intrinsic, BindingScope scope, bool derivation)
    {
        internal BoundType Type { get; } = type;

        internal BindingSymbol Intrinsic { get; } = intrinsic;

        internal BindingScope Scope { get; } = scope;

        internal bool Derivation { get; } = derivation;

        // Every work starts with no evidence (SPEC 8.3.3: no coinduction is assumed); its value only rises in the information order.
        internal ConstraintProof Result { get; set; } = ConstraintProof.Unknown;

        // Owned of a Type with structure (SPEC 15.3.5): a cycle of such works alone is coinductive (Binding.AssumeResidual).
        internal bool Coinductive { get; } = intrinsic.Intrinsic == IntrinsicKind.Owned && !type.IsAbstract;

        // In the coinductive step, read as Proven by the other assumed works (Binding.AssumeResidual).
        internal bool Assumed { get; set; }

        internal bool Evaluated { get; set; }

        internal bool Queued { get; set; }

        // Whether its last evaluation's answer depends on an open obligation (Binding.EndAnswer), or it read a work that does (SPEC
        // 8.3.3 Finality): no fact past the outermost request, which returns it to its initial state but keeps this flag until its next
        // evaluation (Binding.ForgetDependentCapabilities), so the erasure's Owned owner reads a forgotten subject as Final (OwnedMember).
        internal bool Depends { get; set; }

        // Whether that dependence is the bound body's binding (Binding.BodyBinding: its evaluation read a local region with no answer yet,
        // R7-O2 A1+A2, Revision 3.2, or a program path's Unknown before the pass's final conformance decision, R7-E3 E3e ruling 20, itself
        // or through a work it read): forgotten past the outermost request with its parts kept, only its
        // verdict not cached (Forget), so the erasure's Owned owner classifies its parts as before (OwnedMember: a refuted leaf first) and
        // its obligation is judged again once the body is bound (JudgeErasedOwned).
        internal bool ReadsBodyBinding { get; set; }

        // What its Unknown (a limit, L) or Error rests on (DECISIONS BU-22): a component summary's site or Error, or a child's it read;
        // settled after the verdicts (Binding.SettleCauses), the least by CauseKey of its own and those of the works it read at its verdict.
        internal ICheckCause? Cause { get; set; }

        // The Cause its last evaluation read from anything but a work; and the works it read (Binding.SettleCauses' inputs). The list is
        // kept across passes, so a warm rebind allocates nothing.
        internal ICheckCause? OwnCause { get; set; }

        internal List<CapabilityWork> Reads { get; } = new();

        // The U fact its Unknown rests on, from the final iteration; never compared (design-r2 Amendment 4). Reset empties it, so a
        // consumer that reads the work before its next evaluation reads none, as in its first epoch (PLAN G949 S1-e).
        internal CallableUnknown? Fact { get; set; }

        // For Owned, the first Origin not proven `static` that a verdict other than Proven rests on, and the parts that are not Proven,
        // from the final iteration; never compared (R4-M161). The list is kept across passes, so a warm rebind allocates nothing.
        internal BoundOrigin? Origin { get; set; }

        internal List<CapabilityPart>? Parts { get; private set; }

        // The limit site its own judgment answers where a Closure's signature is unreadable (CallSignature), its Use the literal (DECISIONS
        // BU-73); kept across passes, so a warm rebind allocates nothing.
        internal ComponentSite? Site { get; set; }

        internal List<CapabilityWork> Dependents { get; } = new();

        internal void Reset()
        {
            this.Forget();
            this.Depends = false;
            this.ReadsBodyBinding = false;
            this.Origin = null;
            this.Parts?.Clear();
        }

        // Its verdict and the facts that rest on it are not kept (Binding.ForgetDependentCapabilities); a work that read the body's binding
        // keeps its parts and Origin, which its next evaluation replaces.
        internal void Forget()
        {
            this.Result = ConstraintProof.Unknown;
            this.Assumed = false;
            this.Evaluated = false;
            this.Queued = false;
            this.Cause = null;
            this.OwnCause = null;
            this.Fact = null;
            this.Reads.Clear();
            this.Dependents.Clear();
            if (!this.ReadsBodyBinding)
            {
                this.Origin = null;
                this.Parts?.Clear();
            }
        }

        // A part read by an Owned judgment, on every path that combines one (G379 R5: a layer's own Origin, a structure's components,
        // Origin parts and call signature, a declaration's Type and Origin arguments and projections, an abstract Type's evidence): one
        // that is not Proven is kept with its member, Origin and Type; returns the work's Origin so far.
        internal BoundOrigin? Part(int member, ConstraintProof proof, BoundOrigin? origin, BoundOrigin? first, BoundType? type)
        {
            if (proof == ConstraintProof.Proven || this.Intrinsic.Intrinsic != IntrinsicKind.Owned)
            {
                return first;
            }

            (this.Parts ??= new()).Add(new(member, origin, type));
            return first ?? origin;
        }
    }

    // An Owned part that is not Proven: its member (a Components index; a requirement reference's arguments are member 1; an Origin part
    // follows the components, a layer's own Origin first; a Closure's call signature is member Components.Count, which the erasure,
    // judging the environment alone, never reads), its first Origin, and its Type, whose own work holds its parts (OwnedWork); an Origin
    // part and a limit have none.
    private readonly record struct CapabilityPart(int Member, BoundOrigin? Origin, BoundType? Type);

    // An Owned query's subject rest (ProveOwned): the work that judged it (none when no work did) and its scope.
    private readonly record struct OwnedRest(CapabilityWork? Work, BindingScope Scope);
}
