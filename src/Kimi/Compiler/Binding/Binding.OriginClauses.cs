// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Binding's own judgment of declared Origin relations (SPEC 15.3.3, 15.3.6, 15.6.1, 15.6.5; DECISIONS BU-26, R4-M101, R4-M108): once
// per pass, after the bodies, the clauses of each completed declaration that proof alone does not establish, then the declaration rows.
// - Responsibility: ValidateOriginRelations judges a clause by its owner's part, a local's (JudgeLocalClause) or a Field's, Case's,
//   alias's or associated formation's (JudgeDeclaredRelation), both over FailedDeclaredRelation; JudgeDeclarationRows is the one judge
//   of the Origin rows Binding formed with no executable body (DeclarationAtom, UnboundAtom), through R4-M118's chain rule for a row with
//   an Unbound end (Binding.OriginClauses.Chains.cs; DECISIONS BU-62).
// - Inputs: the completed Origin declarations, the pass's obligations, and the proof and relation judge (Binding.OriginProof.cs,
//   JudgeCallPosition).
// - Outputs: Origin relation records (originRelations; a declaration row's and a chain's through PublishCallRelations), the derived
//   Unsupported failure of a row with an Unbound end (UnboundCause), obligations for the clauses the judge leaves to region inference,
//   and a local clause's row and twin when its record is a body chain (DECISIONS R4-M108, R4-M111).
// - Invariants: a clause is judged by its owner's part only, and FailedDeclaredRelation takes no steering flag; a clause's rows are its
//   owner's body's (a local's, or none for a declaration's); a judged declaration row leaves the obligations, and a row whose ends are
//   not declaration atoms stays for the view's validation (R4-M101 E2).
// - Prohibitions: no proof or judgment of its own (the proof and the judge decide); no message text (the diagnostics adapter writes it).
public sealed partial class Binding
{
    private readonly List<(BindingObligation Obligation, BoundOrigin Longer, BoundOrigin Shorter)> fixedRows = new();

    // R4-M101 E2: an end of a declaration's row: a parameter, input, static or Unbound atom, or a meet of them.
    private static bool DeclarationAtom(BoundOrigin origin)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input or OriginKind.Unbound;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!DeclarationAtom(origin.Operands[i]))
            {
                return false;
            }
        }

        return true;
    }

    // The first Unbound atom of an end, an Origin its declaration left pending at its binder, the Type occurrence that omits it.
    private static BoundOrigin? UnboundAtom(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Unbound)
        {
            return origin;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (UnboundAtom(origin.Operands[i]) is { } atom)
            {
                return atom;
            }
        }

        return null;
    }

    // SPEC 15.3.3: the clauses of each completed declaration that proof alone does not establish, in declaration order, judged by
    // their owner's part: a local's (a FieldKoto, the parser's local declaration) by the local part, JudgeLocalClause, and any other
    // owner's, a Field's, Case's, alias's or associated formation's, by the declaration part, JudgeDeclaredRelation: a declaration's
    // own clause, which Binding alone judges. Then the declaration's rows, JudgeDeclarationRows.
    private void ValidateOriginRelations()
    {
        foreach (var declaration in this.originDeclarations.Values)
        {
            if (declaration.State != 3 || declaration.Owner is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto || IsAssociatedRequirement(declaration.Owner))
            {
                continue;
            }

            // A clause's rows are its owner's body's: a local's, or none for a declaration's (DECISIONS R4-M101 E1).
            var saved = this.EnterBody(declaration.Body);
            try
            {
                this.ValidateOriginRelations(declaration);
            }
            finally
            {
                this.formingBody = saved;
            }
        }

        this.JudgeDeclarationRows();
    }

    // SPEC 15.3.2, 15.3.4, 15.6.1, 15.6.5 (DECISIONS BU-26, BU-62): the one judge of a declaration's rows, the Origin relations Binding
    // formed with no executable body (a Field's, payload's or base's declared Type, a signature's well-formedness, a clause's unrepresentable
    // end), which consumes each in every pass. A row each call discharges (CallBorrows) is left to the calls, and one the published
    // premises at its use entail is its declaration's premise, not an obligation (CollectDeclarationRows). Every other row goes to R4-M118's
    // rule: a fixed-fixed one, the rule's Fixed -> Fixed edge, is judged by the position judge (each failing operand of a meet its own
    // record), and one with an Unbound end is never judged itself, only the chains through it (JudgeChains), and rests on the part its
    // Unbound atom rests on (UnboundCause). The rule judges from the premises at the use and a failed enclosing declaration's cleared
    // clauses (RejectedRelations). A row whose ends are not declaration atoms (R4-M101 E2) stays, a broken contract the view reports.
    private void JudgeDeclarationRows()
    {
        this.callRelationScratch.Clear();
        this.CollectDeclarationRows();
        this.judgingDeclarationRows = true;
        try
        {
            foreach (var (row, longer, shorter) in this.fixedRows)
            {
                this.JudgeCallPosition(row.Use, longer, shorter, row.Equality, null, null, wellFormed: row.Clause is null, condition: row.Condition);
            }

            this.JudgeChains();
            this.PublishCallRelations();
        }
        finally
        {
            this.judgingDeclarationRows = false;
        }

        foreach (var row in this.unboundRows)
        {
            this.AddPrerequisite(row.Obligation.Use, this.UnboundCause(row.Atom));
            this.Fail(row.Obligation.Use, BindingFailure.Unsupported);
        }

        this.unboundRows.Clear();
        this.fixedRows.Clear();
    }

    // Consumes the pass's declaration rows. One whose relation the published premises at its use entail (a signature's well-formed input,
    // BU-26 E3) is Binding's direct proof and forms no edge, as a relation Binding proves forms no row for A5 (2a0-D D1); region inference
    // and the rejected clauses never decide a row. A fixed-fixed row waits to be judged at once, one with an Unbound end for JudgeChains.
    private void CollectDeclarationRows()
    {
        var remaining = 0;
        for (var i = 0; i < this.obligations.Count; i++)
        {
            var obligation = this.obligations[i];
            if (obligation is not { Kind: BindingObligationKind.OriginOutlives, Body.IsBody: false, Longer: { } longer, Shorter: { } shorter } ||
                !DeclarationAtom(longer = this.OriginAtUse(longer, obligation.Use)) || !DeclarationAtom(shorter = this.OriginAtUse(shorter, obligation.Use)))
            {
                this.obligations[remaining++] = obligation;
                continue;
            }

            this.obligationSet.Remove(obligation);
            if (obligation.Discharge == OriginDischarge.CallBorrows || this.PublishedPremise(obligation, longer, shorter))
            {
                continue;
            }

            if ((UnboundAtom(longer) ?? UnboundAtom(shorter)) is { Binder: not null } unbound)
            {
                this.unboundRows.Add((obligation, longer, shorter, unbound));
            }
            else
            {
                this.fixedRows.Add((obligation, longer, shorter));
            }
        }

        this.obligations.RemoveRange(remaining, this.obligations.Count - remaining);
    }

    // Whether the published premises at the row's use entail its relation, both ways for an equality.
    private bool PublishedPremise(BindingObligation row, BoundOrigin longer, BoundOrigin shorter)
        => this.ProvesOriginOutlives(longer, shorter, row.Use, row.Condition) &&
            (!row.Equality || this.ProvesOriginOutlives(shorter, longer, row.Use, row.Condition));

    // The clauses of one completed declaration that proof alone does not establish, judged by their owner's part.
    private void ValidateOriginRelations(OriginDeclaration declaration)
    {
        foreach (var relation in declaration.Relations)
        {
            if (!this.ProvesOriginOutlives(relation.Longer, relation.Shorter, relation.Syntax) ||
                (relation.Equality && !this.ProvesOriginOutlives(relation.Shorter, relation.Longer, relation.Syntax)))
            {
                if (declaration.IsLocal)
                {
                    this.JudgeLocalClause(declaration, relation);
                }
                else
                {
                    this.JudgeDeclaredRelation(relation);
                }
            }
        }
    }

    // SPEC 15.3.3, 15.6.1, 15.6.5: a declared clause is checked against established evidence and assumes no new facts. A chain the judge
    // leaves to region inference is an obligation, which ownership reports as a located limit; a Refuted or Unknown relation is the
    // Origin relation record returned, with the source `declared` and the clause related, located at the clause.
    private OriginRelationFact? FailedDeclaredRelation(OriginRelation relation)
    {
        var use = relation.Syntax;
        var obligation = new BindingObligation(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, relation.Longer, relation.Shorter, Equality: relation.Equality);
        obligation = obligation with { Body = this.formingBody }; // Judged in the body that forms it (SPEC 15.6.5; DECISIONS BU-28).
        var resolvedLonger = this.OriginAtUse(relation.Longer, use);
        var resolvedShorter = this.OriginAtUse(relation.Shorter, use);
        if (HasRegionIn(resolvedLonger, this.formingBody) || !FixedIn(resolvedShorter, this.formingBody))
        {
            this.AddObligation(obligation with { Longer = resolvedLonger, Shorter = resolvedShorter, Clause = use });
            return null;
        }

        var judgment = this.JudgeOriginObligation(obligation, out var reversed);
        if (judgment == OriginJudgment.Proven)
        {
            return null;
        }

        if (judgment == OriginJudgment.Unrepresentable)
        {
            this.AddObligation(obligation);
            return null;
        }

        var written = reversed ? relation.Shorter : relation.Longer;
        var longer = this.OriginAtUse(written, use);
        var shorter = this.OriginAtUse(reversed ? relation.Longer : relation.Shorter, use);
        var refuted = RefutesOriginRelation(longer, shorter, this.formingBody);
        return new OriginRelationFact(use, longer, this.SuppliedAtUse(written, use), shorter, relation.Equality, null, refuted, use);
    }

    // The declaration part: a Field's, Case's, alias's or associated formation's own clause, which Binding alone judges, is recorded
    // at the clause.
    private void JudgeDeclaredRelation(OriginRelation relation)
    {
        if (this.FailedDeclaredRelation(relation) is { } failed)
        {
            this.FailExplained(ref this.originRelations, relation.Syntax, BindingFailure.OriginRelation, failed);
        }
    }

    // The local part (SPEC 15.3.6): a local's clause is judged as declared, and a relation is located at the initializer when the
    // initializer supplies one of its ends, an Origin the local's Type omits, as the value that does not fit. An omitted contravariant
    // slot with no principal lower bound is a local region (SolveLocalBounds), so its clause is a row, judged through the region's
    // sources, never a record here (DECISIONS R4-M112).
    private void JudgeLocalClause(OriginDeclaration local, OriginRelation relation)
    {
        if (this.FailedDeclaredRelation(relation) is not { } failed)
        {
            return;
        }

        var use = relation.Syntax;
        var supplied = InferredEnd(relation.Longer) is not null || InferredEnd(relation.Shorter) is not null;
        var record = supplied && local.Owner is VariableKoto { InitializerKoto: { } initializer } ? failed with { At = initializer } : failed;
        this.FailExplained(ref this.originRelations, use, BindingFailure.OriginRelation, record);

        // 2a0-D §5.1 (DECISIONS R4-M108): a record at a body chain (a finite end over a fixed one) is also the clause's row, for A5, and
        // the clause is a twin (R4-M111).
        if (BodyChain(failed.Longer, failed.Shorter, this.formingBody))
        {
            (this.originTwins ??= new(ReferenceEqualityComparer.Instance)).Add(use);
            var row = new BindingObligation(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, null, failed.Longer, failed.Shorter);
            this.AddObligation(row with { Equality = failed.Equality, Clause = use });
        }

        // The variance of an Origin the local's Type omits and its initializer supplies, or null for any other end.
        OriginVariance? InferredEnd(BoundOrigin end)
        {
            if (local.Inferred is not { } slots)
            {
                return null;
            }

            if (slots.TryGetValue(end, out var position))
            {
                return position;
            }

            for (var i = 0; i < end.Operands.Count; i++)
            {
                if (slots.TryGetValue(end.Operands[i], out position))
                {
                    return position;
                }
            }

            return null;
        }
    }
}
