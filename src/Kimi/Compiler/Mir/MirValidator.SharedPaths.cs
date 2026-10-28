// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler.Mir;

// The shared layers a write, an in-place destruction or an exclusive acquisition passes (W1 rule 1; SPEC 3.3.2, 3.5.1, 4.8.1, 8.15.2,
// 13.5.5, 15.1.5; DECISIONS R5-M123, R5-M139).
// - Responsibility: a projected Place that a location writes (an Assign's or a returning Call's destination), destroys in place (a Drop),
//   borrows exclusively (a Ref Uniq or TwoPhaseUniq) or acquires in its binder's exclusive case (a PairAcquire, which Reborrows there,
//   MirCases.Reborrows), and an exclusive borrow an Adapt resolves to under a Semantics case view (MirValidator.Types' Views), passes no
//   shared layer (MirTypes.IsSharedLayer: a Deref of a ref, objref, rc or arc Type, an Index of a Slice, or a PairLayer in a shared case)
//   after its last raw Deref; else a SharedPath finding at the location.
// - Inputs: each valid Place MirUses reports, with its access and borrow kind (MirValidator.Tables' Place), and each Adapt's borrow with
//   its view's case assignment; the body's projections, locals, binders and scopes. Outputs: SharedPath findings.
// - Invariants: a pair layer is read under a case assignment: an assigned binder's layers in its case, every other binder's at its
//   admitted set, so shared where it admits a shared case (its weakest case); the empty assignment for a write, a destruction and a Ref,
//   the PairAcquire's binder in its exclusive case, an Adapt's view. The walk goes from the Place's last step to its root, and the first
//   step that decides ends it: a shared layer is the finding, a raw pointer's Deref is none (a raw Place's authority is its pointer's,
//   SPEC 5.2.1 (3)), and so is the Deref directly on a PreparedInput, the caller's slot itself (plan 5.6.2, A3 E17), whose writes and
//   exclusive uses A5-F9 reports (DefaultArgumentAccess_Kd, DefaultArgumentBorrow_Kd). A location in an inline default copy is its
//   template's check (R4-M69), and where the scopes cannot be walked (a Scope finding) nothing is checked.
// - Prohibitions: no Move (A1's Move Path rule, StaticMovePathRequired_Kd and DefaultArgumentMove_Kd); a PairAcquire keyed on its binder's
//   exclusive cases, never on its Use's marked kind (a Copy-only binder's Use is marked too); no Binding or HIR fact.
internal sealed partial class MirValidator
{
    private partial struct Checker
    {
        // The Semantics cases a binder's pair layers are read in under a case assignment: its assigned case, else its admitted set.
        private static SemanticsMask Cases(in PairBinder binder, ReadOnlySpan<PairCase> cases)
        {
            foreach (var assigned in cases)
            {
                if (ReferenceEquals(assigned.Target, binder.Target))
                {
                    return assigned.Semantics.ToMask();
                }
            }

            return binder.Admitted;
        }

        // A projected Place written, destroyed in place or borrowed exclusively by a Ref, under the empty assignment, or a PairAcquire's
        // Place, the marked Use's one case-dependent read, with its binder in its exclusive case, in which it Reborrows: a SharedPath
        // finding at the location where it passes a shared layer.
        private readonly void SharedPath(MirPlace place, MirAccess access, MirRefKind kind)
        {
            var exclusive = (access & (MirAccess.Writes | MirAccess.DropUse)) != 0 ||
                ((access & MirAccess.Borrows) != 0 && kind is MirRefKind.Uniq or MirRefKind.TwoPhaseUniq);
            var binder = !exclusive && this.acquires && (access & MirAccess.CaseDependent) != 0 ? MirCases.Binder(body, body.TypeOf(place)) : -1;
            if (exclusive ? this.IsSharedPath(place, []) : binder >= 0 && (body.Binders[binder].Admitted & (SemanticsMask.Uniq | SemanticsMask.ObjUniq)) != 0 &&
                this.IsSharedPath(place, [new(body.Binders[binder].Target, SemanticsKind.Uniq)]))
            {
                this.Report(MirViolation.SharedPath);
            }
        }

        // Whether a Place the location acquires exclusively passes a shared layer after its last raw Deref under a case assignment, the
        // Deref directly on a PreparedInput being the slot itself; never at a location in an inline default copy or in an Unscoped body.
        private readonly bool IsSharedPath(MirPlace place, ReadOnlySpan<PairCase> cases)
        {
            var projections = body.Projections;
            for (var id = this.Unscoped ? 0 : place.Projection; id != 0; id = projections[id].Prefix)
            {
                ref readonly var step = ref projections[id];
                var type = body.TypeOf(place with { Projection = step.Prefix }); // The Type of the Place the step applies to.
                if (step.Kind == MirProjectionKind.Deref && (type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Raw } ||
                    (step.Prefix == 0 && place.Root >= 0 && body.Locals[place.Root].Role == MirLocalRole.PreparedInput)))
                {
                    return false;
                }

                var admitted = step.Kind == MirProjectionKind.PairLayer && (uint)step.Arg < (uint)body.Binders.Count
                    ? Cases(body.Binders[step.Arg], cases) : default;
                if (MirTypes.IsSharedLayer(type, step.Kind, admitted))
                {
                    return body.Inlining(this.at.Scope) < 0;
                }
            }

            return false;
        }
    }
}
