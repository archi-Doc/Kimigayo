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
//   round changes no state and no limit presence (whether a Pending one rests on a limit, BU-22's L), each in a fresh capability epoch
//   (no answer computed from a Pending read outlives its round) and in the reverse of the previous round's order; after the last state
//   change the limits restart from none. Bound: at most P rounds change a state and, after them, at most P a limit, for P Pending
//   declarations, so a publication ends within 2P + 1 rounds; a chain settles in at most five in either declaration order (three
//   without a limit). A quadratic case left is C3's, whose scheduler replaces the rounds. A state still Pending after the rounds is a
//   final Unknown, resting on a limit or not, order-free; which of several limits it rests on follows BU-22's first read, on which no
//   record depends (each limit's site is needed by its own clause's check).
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
                symbol.SourceFact = null;
                this.sourceDeclarations.Add(symbol);
            }
        }
    }

    // The least fixpoint of the checks (SPEC 8.3.3 Finality) over each state and whether a Pending one rests on a limit (BU-22's L, which
    // a reader tells apart from a bare Unknown). A declaration with nothing to prove is Valid before any round, so no round reads it
    // Pending, and each round clears the capability results, so no answer from a Pending read outlives the round that read it. A state
    // only leaves Pending and an answer reads states, not limits, so once a round changes no state none changes again; the round after
    // one that did restarts the limits from none (a limit read before that change could otherwise keep itself through a cycle), and from
    // there a limit only appears. The last round changes neither, so its answers read final states and limits only. The publication is
    // one proof scope, so a check's answer keeps its cause instead of becoming its own Error (EndProof).
    private void PublishSourceStates()
    {
        var declarations = this.sourceDeclarations;
        var stateChanged = false;
        for (var i = 0; i < declarations.Count; i++)
        {
            var closed = this.HasClosedClause((DeclarationContainerKoto)declarations[i].Declaration);
            declarations[i].SourceState = closed ? ConstraintProof.Unknown : ConstraintProof.Proven;
            declarations[i].SourceCause = null;
            declarations[i].SourceFact = null;
            stateChanged |= closed;
        }

        var outer = this.BeginProof();
        var reverse = false;
        var limitChanged = false;
        while (stateChanged || limitChanged)
        {
            this.ClearCapabilityResults();
            for (var n = 0; stateChanged && n < declarations.Count; n++)
            {
                if (declarations[n].SourceState == ConstraintProof.Unknown)
                {
                    declarations[n].SourceCause = null;
                    declarations[n].SourceFact = null;
                }
            }

            stateChanged = false;
            limitChanged = false;
            for (var n = 0; n < declarations.Count; n++)
            {
                var symbol = declarations[reverse ? declarations.Count - 1 - n : n];
                if (symbol.SourceState != ConstraintProof.Unknown)
                {
                    continue;
                }

                this.limitRead = null;
                this.errorRead = null;
                this.unknownRead = null;
                this.sourceChecks++;
                var proof = this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)symbol.Declaration);
                var cause = this.ProofCause(proof);
                stateChanged |= proof != ConstraintProof.Unknown;
                limitChanged |= (cause is null) != (symbol.SourceCause is null);
                symbol.SourceState = proof;
                symbol.SourceCause = cause;
                symbol.SourceFact = this.ProofFact(proof); // Payload, outside the loop's change test.
            }

            reverse = !reverse;
        }

        this.EndProof(outer, ConstraintProof.Proven);
    }

    // A read of a declaration's published state (a bound Contract reference reads its declaration's) in its own proof scope, as the check
    // it replaces was.
    private ConstraintProof ReadSourceState(BindingSymbol symbol)
    {
        var declaration = ((DeclarationContainerKoto)symbol.Declaration).BoundSymbol!;
        return this.EndProof(this.BeginProof(), this.ReadVerdict(declaration.SourceState, declaration.SourceCause, declaration.SourceFact));
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
