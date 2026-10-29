// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SourceStates (PLAN G170 N6, C1 §5; DECISIONS BU-57, BU-59): the published state of each premise source's own check, its declaration's
// closed clauses, which proof reads and never runs.
// - Inputs: a declaration container's Constraint clauses (SPEC 8.2.1), bound in its scope; proof (CheckTypeConstraints for a clause's
//   subject, ProveConstraint); the pass's readiness (bound clauses; Contract headers and capabilities).
// - Outputs: SourceState (Pending = Unknown, Valid = Proven, Invalid = Refuted or Error) and SourceCause (an Unknown's limit, an Error's
//   cause) on every declaration container's own symbol; a read (ReadSourceState) joins what the state rests on (BU-22).
// - Invariants: a closed clause (ClosedClause) is neither associated, a Self clause nor dependent on the declaration's parameters, a
//   Contract's Self included; an unbound clause counts as closed (its answer Unknown). One writer, PublishSourceStates, run after
//   ExpandContractPremises and where each ValidateConformances starts; every state is Pending from the pass's start (ResetSourceStates)
//   and while its own check runs, so a check reaching its own declaration again reads no evidence (SPEC 8.3.3 Recursion). A
//   publication makes every declaration with nothing to prove Valid before any round, then runs rounds over the Pending ones until a
//   round changes none, each in a fresh capability epoch (no answer computed from a Pending read outlives its round) and in the reverse
//   of the previous round's order. Bound: a chain settles in at most three rounds in either declaration order, and a publication ends
//   within one round more than its Pending declarations (a quadratic case left is C3's, whose scheduler replaces the rounds). A state
//   still Pending after the rounds is a final Unknown.
// - Prohibitions: no check run by a reader; no report or failure here (callers decide); no state kept across passes; no mode or bool
//   parameter.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Every declaration container's own symbol, in the pass's order (ResetSourceStates).
    private readonly List<BindingSymbol> sourceDeclarations = new();

    // The checks this pass's publications ran.
    private int sourceChecks;

    /// <summary>Gets the declaration checks the latest pass's publications ran, which a scaling check reads.</summary>
    internal int SourceCheckWork => this.sourceChecks;

    private bool ClosedClause(DeclarationContainerKoto container, IsKoto clause)
        => !clause.IsAssociatedConstraint && !IsSelfConstraint(clause) &&
            (clause.BoundConstraint is not { } constraint ||
                !this.DependentConstraint(constraint, contractSelf: container is ContractKoto));

    private bool HasClosedClause(DeclarationContainerKoto container)
    {
        for (var i = 0; i < container.ConstraintNodes.Count; i++)
        {
            if (this.ClosedClause(container, container.ConstraintNodes[i]))
            {
                return true;
            }
        }

        return false;
    }

    // Every declaration container's own symbol is Pending before the pass adds any premise (BindConstraints).
    private void ResetSourceStates()
    {
        this.sourceDeclarations.Clear();
        this.sourceChecks = 0;
        foreach (var symbol in this.symbols.Values)
        {
            if (symbol.Declaration is DeclarationContainerKoto { BoundSymbol: { } own } && ReferenceEquals(own, symbol))
            {
                symbol.SourceState = ConstraintProof.Unknown;
                symbol.SourceCause = null;
                this.sourceDeclarations.Add(symbol);
            }
        }
    }

    // The least fixpoint of the checks (SPEC 8.3.3 Finality): a declaration with nothing to prove is Valid before any round, so no round
    // reads it Pending, and each round clears the capability results, so no answer from a Pending read outlives the round that read it;
    // the last round changes nothing, so its answers read final states only. The publication is one proof scope, so a check's answer
    // keeps its cause instead of becoming its own Error (EndProof).
    private void PublishSourceStates()
    {
        var declarations = this.sourceDeclarations;
        var pending = false;
        for (var i = 0; i < declarations.Count; i++)
        {
            var closed = this.HasClosedClause((DeclarationContainerKoto)declarations[i].Declaration);
            declarations[i].SourceState = closed ? ConstraintProof.Unknown : ConstraintProof.Proven;
            declarations[i].SourceCause = null;
            pending |= closed;
        }

        var outer = this.BeginProof();
        var reverse = false;
        for (var changed = pending; changed; reverse = !reverse)
        {
            this.ClearCapabilityResults();
            changed = false;
            for (var n = 0; n < declarations.Count; n++)
            {
                var symbol = declarations[reverse ? declarations.Count - 1 - n : n];
                if (symbol.SourceState != ConstraintProof.Unknown)
                {
                    continue;
                }

                this.limitRead = null;
                this.errorRead = null;
                this.sourceChecks++;
                var proof = this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)symbol.Declaration);
                symbol.SourceCause = this.ProofCause(proof);
                symbol.SourceState = proof;
                changed |= proof != ConstraintProof.Unknown;
            }
        }

        this.EndProof(outer, ConstraintProof.Proven);
    }

    // A read of a declaration's published state (a bound Contract reference reads its declaration's) in its own proof scope, as the check
    // it replaces was.
    private ConstraintProof ReadSourceState(BindingSymbol symbol)
    {
        var declaration = ((DeclarationContainerKoto)symbol.Declaration).BoundSymbol!;
        return this.EndProof(this.BeginProof(), this.ReadVerdict(declaration.SourceState, declaration.SourceCause));
    }

    private ConstraintProof CheckClosedDeclarationConstraints(DeclarationContainerKoto container)
    {
        var outer = this.BeginProof();
        var proof = ConstraintProof.Proven;
        for (var i = 0; i < container.ConstraintNodes.Count; i++)
        {
            var clause = container.ConstraintNodes[i];
            if (!this.ClosedClause(container, clause))
            {
                continue;
            }

            if (clause.BoundConstraint is not { } constraint)
            {
                proof = CombineProof(proof, ConstraintProof.Unknown, true);
                continue;
            }

            var scope = this.scopes[container];
            var subject = clause.Left.BoundType;
            var formation = subject is null ? ConstraintProof.Unknown : this.CheckTypeConstraints(subject, scope);
            proof = CombineProof(proof, CombineProof(formation, this.ProveConstraint(constraint, scope), true), true);
        }

        return this.EndProof(outer, proof);
    }
}
