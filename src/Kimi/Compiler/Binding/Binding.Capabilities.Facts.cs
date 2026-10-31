// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// The verdict a capability work records (SPEC 8.3.3, 15.2.3; DECISIONS BU-22, R4-M155, R4-M161), a part of the capability engine.
// - Responsibility: a work's state in the fixpoint (CapabilityWork: its proposition's Type, intrinsic and scope, its Result and Cause,
//   its queue state, its dependents) and the payload its verdict carries (the U fact); the judgment of one OwnedOrigins Origin against
//   `static` (StaticOrigin).
// - Inputs: the engine's evaluations (Binding.Capabilities: RequestCapability's dequeue loop writes a work's verdict and payload).
// - Outputs: a work's Result and Cause, which the fixpoint compares, and its payload, which no decision reads (the selection of a record
//   reads it).
// - Invariants: a payload is the final evaluation's, written beside the Result and outside the fixpoint's equality; Reset returns a work
//   to its initial verdict for a new pass; an OwnedOrigins Origin that is null or not proven to outlive `static` is Unknown, and each
//   caller keeps its own absence rule (a missing outer Origin is Proven).
// - Prohibitions: no proof decision reads a payload; nothing here schedules or evaluates a work (Binding.Capabilities).
public sealed partial class Binding
{
    // SPEC 15.2.3: an Origin of a Type's OwnedOrigins is Owned when it is proven to outlive `static`; a null Origin argument and an
    // unproven Origin are Unknown.
    private ConstraintProof StaticOrigin(BoundOrigin? origin, BindingScope scope)
        => origin is not null && this.ProvesOriginOutlives(origin, BoundOrigin.Static, scope.Owner) ? ConstraintProof.Proven : ConstraintProof.Unknown;

    private sealed class CapabilityWork(BoundType type, BindingSymbol intrinsic, BindingScope scope, bool derivation)
    {
        internal BoundType Type { get; } = type;

        internal BindingSymbol Intrinsic { get; } = intrinsic;

        internal BindingScope Scope { get; } = scope;

        internal bool Derivation { get; } = derivation;

        // Recursive Owned structure starts optimistically; everything else starts unknown.
        internal ConstraintProof Result { get; set; } = InitialResult(intrinsic, type);

        internal bool Evaluated { get; set; }

        internal bool Queued { get; set; }

        internal CapabilityWork? Parent { get; set; }

        // What its Unknown (a limit, L) or Error rests on (DECISIONS BU-22): a component summary's site or Error, or a child's it read.
        internal ICheckCause? Cause { get; set; }

        // The U fact its Unknown rests on, from the final iteration; never compared (design-r2 Amendment 4).
        internal CallableUnknown? Fact { get; set; }

        internal List<CapabilityWork> Dependents { get; } = new();

        internal void Reset()
        {
            this.Result = InitialResult(this.Intrinsic, this.Type);
            this.Evaluated = false;
            this.Queued = false;
            this.Parent = null;
            this.Cause = null;
            this.Dependents.Clear();
        }

        private static ConstraintProof InitialResult(BindingSymbol intrinsic, BoundType type)
            => intrinsic.Intrinsic == IntrinsicKind.Owned && !type.IsAbstract
                ? ConstraintProof.Proven
                : ConstraintProof.Unknown;
    }
}
