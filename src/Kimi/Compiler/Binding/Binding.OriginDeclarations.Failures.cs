// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The failure scope of an Origin declaration (SPEC 15.3.3, 23.1.2; DECISIONS BU-62, BU-68, R4-M118; H11b-0 B1): a failed clause fails
// itself, not its owner, and the part each dependent rests on.
// - Responsibility: a clause that does not bind (FailOriginClause: a guessed operator or an unavailable operand), the first such clause,
//   `Failure`, and each one's bound Origins, `FailedClauses`; the slots its bound Origins name (RestsOnFailedClause); `PendingFailure`'s
//   one writer (RestPendingOn: path a here, path c at the Type, alias target and Case payload sites) and its reader (UnboundCause); an
//   end that rests on a failed part (Rests) and rule W's one per-conjunct split (HoldsResting; DECISIONS Q13, PLAN G739); a
//   failed fit's failed Origin environment (FailedOriginEnvironment): for a record with no Unbound end, the dependence query
//   (DependenceCause, RepairHolds; DECISIONS BU-69) over the failed clauses of the contracts the premise extraction reads at its use
//   (ReadFailedClauses).
// - Inputs: the declaration CompleteOriginDeclaration completes and its clauses' bound operands; the part that failed before completion
//   (path c); a pending slot's atom; an Unbound atom; the use and the record of an Origin fit that failed.
// - Outputs: the clause's InvalidOrigin failure or its dependence on the unavailable operand; the named slots left Unbound; the part an
//   Unbound atom rests on, or none; whether a conjunct holds by a resting end; the failed clause a failed fit rests on, or none when
//   it is independent (a resting end never reaches it: its conjunct holds). Its owner's state, slot and other
//   clauses are untouched: the declaration completes as one without the failed clause.
// - Invariants: `Failure` and `PendingFailure` hold the first failed clause or part, and these and `FailedClauses` are reset each pass;
//   RestPendingOn changes no State, so a later bind in State 1 keeps the same pending atom; a failed clause supplies no equality,
//   outlives edge, elision evidence or obligation and is no premise except as the dependence query's repair (BU-69), which only ever
//   removes a dependent report.
// - Prohibitions: no proof or judgment of its own; no write to the owner's state or slot; no failure lookup per binder (R4-M118,
//   BU-36: a part's failure is written at its failure site); FailedOriginEnvironment keeps structural Type failures and refuted
//   relations independent; no message text.
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
    // The one split of its readers, each with its own leaf: the fit (FitOrigin) and its position search (OriginFitSearch).
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
        (declaration.Forming.FailedClauses ??= new(1)).Add((clause, left, right));
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
            foreach (var (_, left, right) in failed)
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

    // Queried only after an Origin fit failed: structural Type failures and refuted lifetime relations stay independent. A record with an
    // Unbound end rests on the nearest failed declaration around its use (PLAN G206 (6), until G318 in R7); a fixed-fixed one only on a
    // failed contract whose bound clauses or a repair of whose failed clause could prove it (DependenceCause; DECISIONS BU-69).
    private Koto? FailedOriginEnvironment(Koto use, in OriginRelationFact recorded)
    {
        var longer = this.OriginAtUse(recorded.Longer, use);
        var shorter = this.OriginAtUse(recorded.Shorter, use);
        // An Unbound end that rests on a failed part never reaches a record: its conjunct holds (Rests, FitOrigin). Any other Unbound end
        // (a per-call image, PerCallImages) has no failed part, so its record is independent.
        return UnboundAtom(longer) is null && UnboundAtom(shorter) is null
            ? this.DependenceCause(longer, shorter, recorded.Equality, use, recorded.Condition) : null;
    }

    // SPEC 23.1.2 (DECISIONS BU-69): the failed clause a fixed-fixed relation that already failed rests on, or null when it is
    // independent. The premise extraction at the use reads each failed clause where it reads that contract (R1, a Type's premises,
    // substituted; ReadFailedClauses): one repair of each failed clause, whose free end ranges over the Origins visible at that clause
    // (RepairHolds); the contract's bound clauses are premises already. An `==` relation rests only when both directions do.
    private Koto? DependenceCause(BoundOrigin longer, BoundOrigin shorter, bool equality, Koto use, ulong condition)
    {
        var environment = this.RentPremises(use);
        try
        {
            if (environment is null)
            {
                throw new InvalidOperationException($"The premise environment of `{longer} outlives {shorter}` is not ready: its construction depends on itself (PLAN G74).");
            }

            var closure = environment.Closure;
            environment.Dependence = true;
            this.CollectOriginResolvers(environment);
            var target = closure.Node(shorter, out _);
            var candidate = closure.Node(longer, out _);
            this.ExtractOriginPremises(environment, use);
            for (var i = 0; i < environment.Repairs.Count; i++)
            {
                if (this.RepairHolds(environment, i, candidate, target, condition) &&
                    (!equality || this.RepairHolds(environment, i, target, candidate, condition)))
                {
                    return environment.Repairs[i].Clause;
                }
            }

            return null;
        }
        finally
        {
            if (environment is not null)
            {
                this.ReturnPremises(environment);
            }
        }
    }

    // Whether `longer outlives shorter` holds by the premises or by one repair of a failed clause (SPEC 23.1.2). The clause names a bound
    // Origin L on one side: `L outlives ?` holds when longer >= L, exact through `static`; `? outlives L` when L >= shorter and longer
    // outlives an Origin visible at the clause; `==` either way. A clause with no bound Origin or a guessed operator holds when longer
    // outlives an Origin visible at it: `static`, a declared Origin of the failed declaration or of one enclosing it, or an Origin
    // argument of the occurrence whose Type premises read it.
    private bool RepairHolds(OriginPremiseEnvironment environment, int index, int longer, int shorter, ulong condition)
    {
        var closure = environment.Closure;
        if (closure.Entails(longer, shorter, condition))
        {
            return true;
        }

        var (clause, left, right, owner, arguments) = environment.Repairs[index];
        var named = left ?? right;
        if (named is not null && !IsRecovery(clause, out _))
        {
            var node = this.PremiseNode(environment, named);
            var below = (clause.IsEquality || left is null) && closure.Entails(node, shorter, condition) && this.Below(environment, longer, owner, arguments, condition);
            return below || ((clause.IsEquality || right is null) && closure.Entails(longer, node, condition));
        }

        return this.Below(environment, longer, owner, arguments, condition);
    }

    // Whether `longer` outlives an Origin visible at a failed clause: `static`, a declared Origin (a slot, an input or a binding set's
    // slot) whose owner is the failed declaration `owner` or encloses it, or one of the occurrence's Origin `arguments`.
    private bool Below(OriginPremiseEnvironment environment, int longer, Koto? owner, BoundOrigin[]? arguments, ulong condition)
    {
        var closure = environment.Closure;
        if (closure.Entails(longer, closure.Node(BoundOrigin.Static, out _), condition))
        {
            return true;
        }

        for (var i = 0; i < (arguments?.Length ?? 0); i++)
        {
            if (closure.Entails(longer, this.PremiseNode(environment, arguments![i]), condition))
            {
                return true;
            }
        }

        for (var node = 0; owner is not null && node < closure.NodeCount; node++)
        {
            if (closure.Origin(node) is { Binder: { } binder } origin && Declared(origin) &&
                OriginOwner(binder) is { } declared && Encloses(declared, owner) && closure.Entails(longer, node, condition))
            {
                return true;
            }
        }

        return false;

        // A declared Origin: a slot or an input, or a binding set's slot; never a body atom such as a parameter Place's Borrow.
        static bool Declared(BoundOrigin origin)
            => origin.Kind is OriginKind.Parameter or OriginKind.Input or OriginKind.ConstraintSet || (origin.Kind == OriginKind.Projection && origin.Binder is TypeSemanticsKoto { BindingSetName: not null });

        static bool Encloses(Koto ancestor, Koto node)
        {
            for (var current = node; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
