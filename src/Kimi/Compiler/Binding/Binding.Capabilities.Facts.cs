// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The verdict a capability work records (SPEC 8.3.3, 15.2.3; DECISIONS BU-22, R4-M155, R4-M161), a part of the capability engine.
// - Responsibility: a work's state in the fixpoint (CapabilityWork: its proposition's Type, intrinsic and scope, its Result and Cause,
//   its queue state, its dependents) and the payload its verdict carries: the U fact, and for Owned the first Origin it rests on and each
//   part that is not Proven with its member and Origin (CapabilityPart); the judgment of one OwnedOrigins Origin against `static`
//   (StaticOrigin); the payload's one reader, an Owned query's rest (ProveOwned with OwnedRest, read by the erasure).
// - Inputs: the engine's evaluations (Binding.Capabilities: RequestCapability's dequeue loop writes a work's verdict and payload; the
//   Origin slot of the proof scope in progress, ProofReads.Origin of the channel-owned stack through ScopeReads,
//   Binding.ConstraintValidation.Proofs).
// - Outputs: a work's Result and Cause, which the fixpoint compares, and its payload, which no decision reads (the selection of a record
//   reads it). A part's Origin is kept only when the part is not Proven, so premises that prove a part leave none.
// - Determinism: a dependent is queued again whenever a part's Result or Cause changes, so the final evaluation sees each part's final
//   verdict; a part's Origin can still change without a re-queue, and a Result only rises in the information order (every work starts
//   Unknown; Binding.Capabilities), so a copied Origin still fails (it may not be the first), as for the U fact; the queue order is
//   deterministic.
// - Invariants: a payload is the final evaluation's, written beside the Result and outside the fixpoint's equality; Reset returns a work
//   to its initial verdict for a new pass; an OwnedOrigins Origin that is null or not proven to outlive `static` is Unknown, and each
//   caller keeps its own absence rule (a missing outer Origin is Proven).
// - Prohibitions: no proof decision reads a payload; nothing here schedules or evaluates a work (Binding.Capabilities).
public sealed partial class Binding
{
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

    // SPEC 15.2.3: an Origin of a Type's OwnedOrigins is Owned when it is proven to outlive `static`; a null Origin argument and an
    // unproven Origin are Unknown.
    private ConstraintProof StaticOrigin(BoundOrigin? origin, BindingScope scope)
    {
        if (origin is not null && (this.ProvesOriginOutlives(origin, BoundOrigin.Static, scope.Owner) || this.SolvedStatic(origin)))
        {
            return ConstraintProof.Proven;
        }

        this.ScopeReads.Origin ??= origin;
        return ConstraintProof.Unknown;
    }

    // SPEC 15.2.3, 15.3.6, 8.3.3 (DECISIONS: G572 U2a): whether a local region's solved value, in `origin` or a meet's operand, is
    // `static`, the oracle's one reading of a region (UniversalRelations.Outlives); its bounds are complete once no body is being bound,
    // and until then it has no answer.
    private bool SolvedStatic(BoundOrigin origin)
    {
        if (this.formingBody.IsBody || RegionOperand(origin) is not { } region)
        {
            return false;
        }

        using var relations = this.UniversalRelationsOf(region.Body);
        relations.Close(BoundOrigin.Static, 0);
        return relations.Outlives(origin);
    }

    // An Owned query that also returns its subject's rest (R4-M161): the parts its work found not Proven; none without a work (a context
    // that is not resolved returns before one exists, `Judged` false).
    private ConstraintProof ProveOwned(BoundType type, Koto context, out OwnedRest rest)
    {
        var proposition = this.InternConstraint(new(ConstraintKind.Contract, type, contract: this.Library.Owned));
        var scope = this.ConstraintScope(context);
        var proof = this.ProveConstraint(proposition, scope);
        rest = this.capabilityNodes.TryGetValue((proposition, scope, false), out var work) ? new(true, work.Parts) : default;
        return proof;
    }

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

        // Whether its last evaluation read an open obligation, or a work that did (SPEC 8.3.3 Finality): no fact past the outermost
        // request, which returns it to its initial state (Binding.ForgetPendingCapabilities).
        internal bool Pending { get; set; }

        internal CapabilityWork? Parent { get; set; }

        // What its Unknown (a limit, L) or Error rests on (DECISIONS BU-22): a component summary's site or Error, or a child's it read.
        internal ICheckCause? Cause { get; set; }

        // The U fact its Unknown rests on, from the final iteration; never compared (design-r2 Amendment 4).
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
            this.Result = ConstraintProof.Unknown;
            this.Assumed = false;
            this.Evaluated = false;
            this.Queued = false;
            this.Pending = false;
            this.Parent = null;
            this.Cause = null;
            this.Origin = null;
            this.Parts?.Clear();
            this.Dependents.Clear();
        }

        // A part read by an Owned judgment: one that is not Proven is kept with its member and Origin; returns the work's Origin so far.
        internal BoundOrigin? Part(int member, ConstraintProof proof, BoundOrigin? origin, BoundOrigin? first)
        {
            if (proof == ConstraintProof.Proven || this.Intrinsic.Intrinsic != IntrinsicKind.Owned)
            {
                return first;
            }

            (this.Parts ??= new()).Add(new(member, origin));
            return first ?? origin;
        }
    }

    // An Owned part that is not Proven: its member (a Components index; a requirement reference's arguments are member 1; a Closure's
    // call signature is member Components.Count, which the erasure, judging the environment alone, never reads) and Origin.
    private readonly record struct CapabilityPart(int Member, BoundOrigin? Origin);

    // An Owned query's subject rest (ProveOwned): whether a work judged it, and the parts it found not Proven.
    private readonly record struct OwnedRest(bool Judged, List<CapabilityPart>? Parts);
}
