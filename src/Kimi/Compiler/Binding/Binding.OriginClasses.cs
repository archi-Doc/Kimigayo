// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SPEC 15.6.5's classes of an Origin as Binding's judge and the C1-F4 view read them: fixed by the body's contract (FixedOrigin), a
// body-local finite Origin (BodyLocalOrigin), a relation A5 judges as a row (BodyChain, 2a0-D D1), a Refuted relation
// (RefutesOriginRelation), and the view's row 6, an atom an enclosing body formed (EnclosingAtom; 2a0-D section 3, DECISIONS R4-M101 E5).
// - Inputs: an Origin as the caller has it at its use; for EnclosingAtom a body and the formation context (BoundOrigin.Body,
//   BoundClosure.EvaluatingBody, Binding.ExecutableBodies.cs).
// - Outputs: the class, read by the judge (Binding.OriginProof.cs, Binding.LocalRegions.cs), the records (Binding.OriginFits.cs,
//   Binding.OriginClauses.cs, Binding.CallRelations.cs), the old analysis's reporter and the view (Binding.OriginRows.cs, ClassifyAtom).
// - Invariants: each class is decided here once, by one pure predicate that every reader shares.
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

    // SPEC 15.6.5: a fixed Origin is fixed by the body's contract: a signature, parameter or receiver Origin, a projection of one,
    // static, or a meet of them.
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

    // SPEC 15.6.1, 15.6.5 (2a0-D D1): a relation is a body chain, a row A5 judges, when one of its ends, as the caller has it at its use,
    // is not fixed (an Unbound end counts); a fixed-fixed relation is Binding's to judge (A3 §7, until R7).
    internal static bool BodyChain(BoundOrigin longer, BoundOrigin shorter) => !FixedOrigin(longer) || !FixedOrigin(shorter);

    // SPEC 15.6.1: whether a relation is Refuted: its longer end is a body-local finite Origin and its shorter end a fixed one, or its
    // longer end is Unstated (UnstatedRelation).
    internal static bool RefutesOriginRelation(BoundOrigin longer, BoundOrigin shorter)
        => UnstatedRelation(longer, shorter) == false || (BodyLocalOrigin(longer) && FixedOrigin(shorter));

    private static bool BodyLocalOrigin(BoundOrigin origin)
    {
        if (origin.Kind is OriginKind.Projection or OriginKind.Anchor)
        {
            return true;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (BodyLocalOrigin(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }
}
