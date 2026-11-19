// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The failure scope of an Origin declaration (SPEC 15.3.3, 23.1.2; DECISIONS BU-62, BU-68, R4-M118; H11b-0 B1): a failed clause fails
// itself, not its owner, and the part each dependent rests on.
// - Responsibility: a clause that does not bind (FailOriginClause: a guessed operator or an unavailable operand), the first such clause,
//   `Failure`, and each one's bound Origins (FailedClauses); the slots its bound Origins name (RestsOnFailedClause); `PendingFailure`'s
//   one writer (RestPendingOn: path a here, path c at the Type, alias target and Case payload sites) and its reader (UnboundCause); an
//   end that rests on a failed part (Rests) and rule W's one per-conjunct split (HoldsResting; DECISIONS Q13, PLAN G739). A fixed-fixed
//   relation only a failed clause's repair could prove is a body row A5 judges (R7-O1 H; BU-159's counted dependent until H11b-0 U3).
// - Inputs: the declaration CompleteOriginDeclaration completes and its clauses' bound operands; the part that failed before completion
//   (path c); a pending slot's atom; an Unbound atom.
// - Outputs: the clause's InvalidOrigin failure or its dependence on the unavailable operand; the named slots left Unbound; the part an
//   Unbound atom rests on, or none; whether a conjunct holds by a resting end. Its owner's state, slot and other clauses are untouched:
//   the declaration completes as one without the failed clause.
// - Invariants: `Failure` and `PendingFailure` hold the first failed clause or part, and these and `FailedClauses` are reset each pass;
//   RestPendingOn changes no State, so a later bind in State 1 keeps the same pending atom; a failed clause supplies no equality,
//   outlives edge, elision evidence, obligation or premise.
// - Prohibitions: no proof or judgment of its own; no write to the owner's state or slot; no failure lookup per binder (R4-M118,
//   BU-36: a part's failure is written at its failure site); no message text.
public sealed partial class Binding
{
    // One conjunct's ordinary answer, the leaf HoldsResting fits each operand that does not rest with.
    private interface IOriginConjunct
    {
        bool Holds(BoundOrigin longer, BoundOrigin shorter, ulong condition);
    }

    // SPEC 23.1.2 (DECISIONS BU-62): the one writer of PendingFailure. A declaration that ends without completing its pending Origins leaves them
    // unbound, resting on the part that failed: its failed clause (FailOriginDeclaration), or, when completion is skipped, its Type,
    // alias target or payload operand (path c). No State change: a later bind in State 1 keeps the same pending atom.
    private static void RestPendingOn(OriginDeclaration? declaration, Koto part)
    {
        if (declaration is { Forming.State: 1 })
        {
            declaration.Forming.PendingFailure ??= part;
        }
    }

    // The part an Unbound atom rests on: its declaration's PendingFailure (paths a and c), else its binder, the slot whose own omission
    // failed (OmittedOrigin, path b). The declaration is the atom's binder's owner (PendingOrigin made it there).
    // Null when nothing failed (a per-call image, PerCallImages; a pending atom of a declaration still in State 1): such an atom rests
    // on nothing and its relation is independent.
    private Koto? UnboundCause(BoundOrigin atom)
        => atom.Binder is not { } binder ? null :
            OriginOwner(binder) is { } owner && this.originDeclarations.TryGetValue(owner, out var declaration) &&
            declaration.PendingCause is { } failure ? failure : binder.BindingFailure != BindingFailure.None ? binder : null;

    // SPEC 23.1.2 (DECISIONS Q13 rule W; PLAN G739): whether an end holds an Unbound atom that rests on a failed part (UnboundCause).
    private bool Rests(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Unbound)
        {
            return this.UnboundCause(origin) is not null;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (this.Rests(origin.Operands[i]))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.3.6, 23.1.2 (DECISIONS Q13 rule W; PLAN G739): whether `longer outlives shorter`, whose ordinary answer is false, holds by
    // an end that rests. A resting shorter end holds whole (`x outlives a` proves `x outlives (a and b)`); a meet at the longer end
    // outlives an Origin exactly when each operand does, so a resting operand's conjunct holds and every other operand's is `leaf`'s.
    // The one split of its readers, each with its own leaf: the fit (FitOrigin) and the contract
    // comparison's decisions, an implementation's claims (ClaimHolds) and the Origin inference's bounds (SolveOriginInference).
    private bool HoldsResting<T>(BoundOrigin longer, BoundOrigin shorter, ulong condition, in T leaf)
        where T : struct, IOriginConjunct
        => this.Rests(shorter) || (this.Rests(longer) && this.OperandsHold(longer, shorter, condition, leaf));

    private bool OperandsHold<T>(BoundOrigin longer, BoundOrigin shorter, ulong condition, in T leaf)
        where T : struct, IOriginConjunct
    {
        if (longer.Kind != OriginKind.Intersection)
        {
            return this.Rests(longer) || leaf.Holds(longer, shorter, condition);
        }

        for (var i = 0; i < longer.Operands.Count; i++)
        {
            if (!this.OperandsHold(longer.Operands[i], shorter, condition, leaf))
            {
                return false;
            }
        }

        return true;
    }

    // A clause with a guessed operator or an unavailable operand (`left`, `right`: its operands' Origins, null when unavailable) fails
    // itself, not its owner (SPEC 15.3.3, 23.1.2): it supplies no equality, outlives edge, elision evidence or obligation, the Origins it
    // does name are kept (FailedClauses), and the pending slots they name rest on it (RestPendingOn, RestsOnFailedClause).
    private void FailOriginClause(OriginDeclaration declaration, OriginRelationKoto clause, BoundOrigin? left, BoundOrigin? right)
    {
        (declaration.Forming.FailedClauses ??= new(1)).Add((left, right));
        if (IsRecovery(clause, out _))
        {
            this.Fail(clause, BindingFailure.InvalidOrigin);
        }
        else
        {
            this.CompleteDependent(clause, left is null ? clause.Left : clause.Right);
        }

        declaration.Forming.Failure ??= clause;
        RestPendingOn(declaration, clause);
    }

    // Whether a pending slot's atom rests on a failed clause: a bound Origin of the clause names it, through the declaration's
    // equalities. The atom then stays Unbound, resting on PendingFailure (path a).
    private bool RestsOnFailedClause(OriginDeclaration declaration, BoundOrigin atom)
    {
        if (declaration.Forming.FailedClauses is { } failed)
        {
            foreach (var (left, right) in failed)
            {
                if ((left is not null && ContainsOrigin(this.ResolveOrigin(left, declaration), atom)) ||
                    (right is not null && ContainsOrigin(this.ResolveOrigin(right, declaration), atom)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
