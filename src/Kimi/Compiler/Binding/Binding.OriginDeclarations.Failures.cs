// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The failure scope of an Origin declaration (SPEC 15.3.3, 23.1.2; DECISIONS BU-62, R4-M118): what a failed clause makes unavailable,
// and the part each dependent rests on.
// - Responsibility: a clause that does not bind (FailOriginClause: a guessed operator or an unavailable operand) and the first such
//   clause, `Failure`; the completion of a declaration with a failed clause (FailOriginDeclaration); `PendingFailure`'s one writer
//   (RestPendingOn: path a here, path c at the Type, alias target and Case payload sites) and its reader (UnboundCause); a failed fit's
//   failed Origin environment (FailedOriginEnvironment).
// - Inputs: the declaration CompleteOriginDeclaration completes and its clauses' bound operands; the part that failed before completion
//   (path c); an Unbound atom; the use of an Origin fit that failed.
// - Outputs: the clause's InvalidOrigin failure or its dependence on the unavailable operand, and, unless the owner already failed, the
//   owner's InvalidOrigin failure resting on that clause; a failed declaration's contract made unavailable (its bound clauses kept in `RejectedRelations`, `Relations`
//   cleared, State 3) with each pending use a dependent of the failed clause; the part an Unbound atom rests on; the failed clause a
//   failed fit's use rests on.
// - Invariants: `Failure` and `PendingFailure` hold the first failed clause or part and are reset each pass; RestPendingOn changes no
//   State, so a later bind in State 1 keeps the same pending atom; a failed contract supplies no equality, outlives edge or elision
//   evidence and is no premise except the declaration judge's (RejectedRelations, BU-62).
// - Prohibitions: no proof or judgment of its own; no failure lookup per binder (R4-M118, BU-36: a part's failure is written at its
//   failure site); FailedOriginEnvironment keeps structural Type failures and refuted relations independent; no message text.
public sealed partial class Binding
{
    // SPEC 23.1.2 (DECISIONS BU-62): the one writer of PendingFailure. A declaration that ends without completing its pending Origins leaves them
    // unbound, resting on the part that failed: its failed clause (FailOriginDeclaration), or, when completion is skipped, its Type,
    // alias target or payload operand (path c). No State change: a later bind in State 1 keeps the same pending atom.
    private static void RestPendingOn(OriginDeclaration? declaration, Koto part)
    {
        if (declaration is { State: 1 })
        {
            declaration.PendingFailure ??= part;
        }
    }

    // The part an Unbound atom rests on: its declaration's PendingFailure (paths a and c), else its binder, the slot whose own omission
    // failed (OmittedOrigin, path b). The declaration is the atom's binder's owner (PendingOrigin made it there).
    private Koto UnboundCause(BoundOrigin atom)
        => OriginOwner(atom.Binder!) is { } owner && this.originDeclarations.TryGetValue(owner, out var declaration) &&
            declaration.PendingFailure is { } failure ? failure : atom.Binder!;

    // A clause with a guessed operator or an unavailable operand (`left`: the left operand's Origin, null when unavailable) supplies no
    // equality, outlives edge or elision evidence.
    private void FailOriginClause(OriginDeclaration declaration, OriginRelationKoto clause, BoundOrigin? left)
    {
        if (IsRecovery(clause, out _))
        {
            this.Fail(clause, BindingFailure.InvalidOrigin);
        }
        else
        {
            this.CompleteDependent(clause, left is null ? clause.Left : clause.Right);
        }

        if (declaration.Owner.BindingFailure == BindingFailure.None)
        {
            this.Fail(declaration.Owner, BindingFailure.InvalidOrigin);
            this.AddPrerequisite(declaration.Owner, clause);
        }

        declaration.Failure ??= clause;
    }

    private void FailOriginDeclaration(OriginDeclaration declaration, Koto failure)
    {
        RestPendingOn(declaration, failure);

        // The contract is unavailable. In particular, its missing relations cannot justify defaulting a result to
        // static, or supply premises through the valid-looking subset of the same rejected declaration; only the
        // declaration judge reads that subset (RejectedRelations).
        (declaration.RejectedRelations ??= new(declaration.Relations.Count)).AddRange(declaration.Relations);
        declaration.Relations.Clear();
        declaration.State = 3;
        this.originStateVersion++;
        foreach (var pending in declaration.Pending)
        {
            this.CompleteDependent(pending.Use, failure);
        }
    }

    // Queried only after an Origin fit failed: structural Type failures and refuted lifetime relations stay independent.
    private Koto? FailedOriginEnvironment(Koto use)
    {
        for (var owner = use; owner is not null; owner = owner.Parent)
        {
            if (this.originDeclarations.TryGetValue(owner, out var declaration) && declaration.Failure is { } failure)
            {
                return failure;
            }
        }

        return null;
    }
}
