// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SPEC 15.6.5's classes of an Origin in a body B, as Binding's judge and the C1-F4 view read them: fixed by the contract (FixedOrigin), fixed
// in B (FixedIn), a region B judges through its sources (RegionIn, HasRegionIn for a meet's operands), an Origin B judges through the
// sources the region graph records (SourcesIn) or may bound (BoundableIn), a finite Origin local to B (BodyLocalIn), a relation A5 judges
// as a row (BodyChain, 2a0-D D1), a Refuted relation (RefutesOriginRelation) and a pending atom (PendingAtom). They share one rule, the
// view's row 6 (EnclosingAtom; 2a0-D section 3, DECISIONS R4-M101 E5, BU-28): in a Closure body the Origins of its captures, atoms an
// enclosing body formed, are fixed; in a Closure k's body and the bodies nested in it, that row also fixes k's receiver Origin ρ (SPEC
// 15.6.5: a receiver Origin), and until k is classified ρ and k's environment atoms are pending, neither fixed nor finite (DECISIONS
// R4-M126 P5). The view's row 5 (OwnCallAtom), B's own call receiver and call result, is another such shared rule.
// - Inputs: an Origin as the caller has it at its use, and the body that formed the relation (an obligation's Body, or the forming body
//   while binding; no body after binding); the formation context (BoundOrigin.Body, BoundClosure.EvaluatingBody, Binding.ExecutableBodies.cs)
//   and whether a Closure is classified (BoundClosure.Classified).
// - Outputs: the class, read by the judge (Binding.OriginProof.cs, Binding.LocalRegions.cs), the records (Binding.OriginFits.cs,
//   Binding.OriginClauses.cs, Binding.CallRelations.cs), the old analysis's reporter and the view (Binding.OriginRows.Atoms.cs, ClassifyAtom).
// - Invariants: each class is decided here once, by one predicate that every reader shares; only SourcesIn reads state, the region graph's;
//   a pending atom is tested before any other class.
// - Prohibitions: no proof, judgment, record or state; no class derived from syntax.

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The C1-F4 view's row 6 (2a0-D section 3): an atom of a body that B's evaluation reaches, B's Closure's evaluating body and so on
    // outward, by the body that formed it or its slot's role (RootOf).
    internal static bool EnclosingAtom(BoundOrigin atom, ExecutableBody body)
    {
        var role = RoleOf(atom);
        for (var outer = EvaluatingBody(body); outer.IsBody; outer = EvaluatingBody(outer))
        {
            if (RootOf(atom, role, outer))
            {
                return true;
            }
        }

        return false;
    }

    // The C1-F4 view's row 5 (2a0-D section 3; DECISIONS R4-M152): B's own call receiver ρ, once it is not pending, and B's call result:
    // a Closure body's receiver Origin and the call result its published result may name.
    internal static bool OwnCallAtom(BoundOrigin atom, ExecutableBody body)
        => RoleOf(atom) is ProjectionRole.ClosureValue or ProjectionRole.CallResult && DeclaredBy(atom, body) && !PendingAtom(atom);

    // SPEC 15.6.5: an Origin fixed by the contract: a signature, parameter or receiver Origin, a projection of one, static, or a meet of them.
    internal static bool FixedOrigin(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input;
    }

    // SPEC 15.6.5: an Origin fixed in `body`: by the contract, as `body`'s own call receiver or call result (the view's row 5), or as an
    // enclosing body's atom (a Closure's captures, its receiver Origin ρ and call result in nested bodies) once it is not pending; a meet
    // when each operand is.
    internal static bool FixedIn(BoundOrigin origin, ExecutableBody body)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedIn(origin.Operands[i], body))
                {
                    return false;
                }
            }

            return true;
        }

        return FixedOrigin(origin) || OwnCallAtom(origin, body) || (EnclosingAtom(origin, body) && !PendingAtom(origin));
    }

    // SPEC 15.8.2, 15.6.5 (DECISIONS R4-M126 P5): a Closure's receiver Origin ρ or an environment binding's own atom while the Closure
    // is not classified: its receiver mode, which resolves them (Binding.OriginProof's OriginAtUse), is not known yet, so it is neither
    // fixed nor finite and a relation naming it waits for the classification (Binding.OriginProof's pending relations).
    internal static bool PendingAtom(BoundOrigin atom)
        => ReceiverAtom(atom) && atom.Binder is Parsing.FunctionKoto { ClosureStorage: { Classified: false } };

    // SPEC 15.8.2 (DECISIONS R4-M126 P2): an atom of a Closure's call receiver, the Closure value ρ or an environment binding's own atom,
    // which the receiver resolution reads and a contract record shows as the closure's call receiver.
    internal static bool ReceiverAtom(BoundOrigin atom)
        => atom is { Kind: OriginKind.Projection, Binder: Parsing.FunctionKoto { IsAnonymous: true } } &&
            RoleOf(atom) is ProjectionRole.ClosureValue or ProjectionRole.Environment;

    // SPEC 15.3.6, 15.6.5: a local region that `body` judges through its sources; an enclosing body's region is fixed in it.
    internal static bool RegionIn(BoundOrigin origin, ExecutableBody body) => IsLocalRegion(origin) && !EnclosingAtom(origin, body);

    // RegionIn of an Origin or of an operand of its meet.
    internal static bool HasRegionIn(BoundOrigin origin, ExecutableBody body)
    {
        if (RegionIn(origin, body))
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (HasRegionIn(origin.Operands[i], body))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.5: an Origin a relation formed in `body` may bound in the region graph: a local region, a Borrow's Origin or a meet, unless
    // it is an enclosing body's atom, fixed in `body`.
    internal static bool BoundableIn(BoundOrigin origin, ExecutableBody body)
        => (IsLocalRegion(origin) || origin.Kind is OriginKind.Projection or OriginKind.Anchor or OriginKind.Intersection) && !EnclosingAtom(origin, body);

    // SPEC 15.6.1, 15.6.5 (2a0-D D1): a relation is a body chain, a row A5 judges, when one of its ends, as the caller has it at its use,
    // is not fixed in `body` (an Unbound end counts); a fixed-fixed relation is Binding's to judge (A3 §7, until R7).
    internal static bool BodyChain(BoundOrigin longer, BoundOrigin shorter, ExecutableBody body) => !FixedIn(longer, body) || !FixedIn(shorter, body);

    // SPEC 15.6.1: whether a relation is Refuted in `body`: its longer end is a finite Origin local to it and its shorter end fixed in it, or
    // its longer end is Unstated (UnstatedRelation).
    internal static bool RefutesOriginRelation(BoundOrigin longer, BoundOrigin shorter, ExecutableBody body)
        => UnstatedRelation(longer, shorter) == false || (BodyLocalIn(longer, body) && FixedIn(shorter, body));

    // SPEC 15.6.5: an Origin that `body` judges through the sources the region graph records for it; an enclosing body's atom, fixed in
    // `body`, is never followed.
    internal bool SourcesIn(BoundOrigin origin, ExecutableBody body) => this.HasRegionBounds(origin) && !EnclosingAtom(origin, body);

    // SPEC 15.6.5: a finite Origin local to `body` (a Borrow's), or a meet with one; an enclosing body's Borrow is fixed in it, a pending
    // atom neither.
    private static bool BodyLocalIn(BoundOrigin origin, ExecutableBody body)
    {
        if (origin.Kind is OriginKind.Projection or OriginKind.Anchor)
        {
            return !EnclosingAtom(origin, body) && !PendingAtom(origin);
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (BodyLocalIn(origin.Operands[i], body))
            {
                return true;
            }
        }

        return false;
    }
}
