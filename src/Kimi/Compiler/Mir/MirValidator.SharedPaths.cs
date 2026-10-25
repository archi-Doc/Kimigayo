// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using SemanticsKind = Kimi.Compiler.Parsing.SemanticsKind;

namespace Kimi.Compiler.Mir;

// The shared layers a write or an in-place destruction passes (W1 rule 1; SPEC 3.3.2, 4.8.1, 13.5.5, 15.1.5; DECISIONS R5-M123).
// - Responsibility: a projected Place that a location writes (an Assign's or a returning Call's destination) or destroys in place (a Drop)
//   passes no shared layer (MirTypes.IsSharedLayer: a Deref of a ref, objref, rc or arc Type, an Index of a Slice, or a PairLayer whose
//   binder admits a shared Semantics) after its last raw Deref; else a SharedPath finding at the location.
// - Inputs: each valid Place MirUses reports, with its access (MirValidator.Tables' Place); the body's projections, locals, binders and
//   scopes. Outputs: SharedPath findings.
// - Invariants: the walk goes from the Place's last step to its root, and the first step that decides ends it: a shared layer is the
//   finding, a raw pointer's Deref is none (a raw Place's authority is its pointer's, SPEC 5.2.1 (3)), and so is the Deref directly on a
//   PreparedInput, the caller's slot itself (plan 5.6.2, A3 E17), whose writes A5-F9 reports (DefaultArgumentAccess_Kd). A location in an
//   inline default copy is its template's check (R4-M69), and where the scopes cannot be walked (a Scope finding) nothing is checked.
// - Prohibitions: no Move (A1's Move Path rule, StaticMovePathRequired_Kd and DefaultArgumentMove_Kd); no exclusive borrow, PairAcquire or
//   Adapt yet (stage B, after PLAN G262, G251 (7) and the SPEC session's reading of a discarded Place); no Binding or HIR fact.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // A projected Place written or destroyed in place past a shared layer: a SharedPath finding at the location.
        private readonly void SharedPath(MirPlace place, MirAccess access)
        {
            if (place.Projection != 0 && (access & (MirAccess.Writes | MirAccess.DropUse)) != 0 && !this.Unscoped && this.PastSharedLayer(place) &&
                body.Inlining(this.at.Scope) < 0)
            {
                this.Report(MirViolation.SharedPath);
            }
        }

        // Whether a Place passes a shared layer after its last raw Deref, the Deref directly on a PreparedInput being the slot itself.
        private readonly bool PastSharedLayer(MirPlace place)
        {
            var projections = body.Projections;
            for (var id = place.Projection; id != 0; id = projections[id].Prefix)
            {
                ref readonly var step = ref projections[id];
                var type = body.TypeOf(place with { Projection = step.Prefix }); // The Type of the Place the step applies to.
                if (step.Kind == MirProjectionKind.Deref && (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ||
                    (step.Prefix == 0 && place.Root >= 0 && body.Locals[place.Root].Role == MirLocalRole.PreparedInput)))
                {
                    return false;
                }

                var admitted = step.Kind == MirProjectionKind.PairLayer && (uint)step.Arg < (uint)body.Binders.Count
                    ? body.Binders[step.Arg].Admitted : default;
                if (MirTypes.IsSharedLayer(type, step.Kind, admitted))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
