// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SourceStates (PLAN G170 N6, C1 §5; DECISIONS BU-57, BU-59): the published state of each premise source's own check, its declaration's
// closed clauses, which proof reads and never runs.
// - Inputs: a declaration container's Constraint clauses (SPEC 8.2.1), bound in its scope; proof (CheckTypeConstraints for a clause's
//   subject, ProveConstraint); the pass's readiness (bound clauses; Contract headers and capabilities).
// - Outputs: SourceState (Pending = Unknown, Valid = Proven, Invalid = Refuted or Error) and SourceCause (an Unknown's limit, an Error's
//   cause) on every declaration container's own symbol; a read (ReadSourceState) joins what the state rests on (BU-22) and gives its
//   Completion (W8, DECISIONS R7-M20's whitelist; R7-C2e2 e2-3a): a published Proven or Refuted is Complete within its publication
//   (it joins nothing inside a proof scope), Pending (a final Unknown included) and Error are Provisional. A publication's start
//   forgets every table the capability phase kept (ContractSolver's ForgetKeptTables), since a state is final only within one.
// - Invariants: a closed clause (ClosedClause) is neither associated, a Self clause nor dependent on the declaration's parameters, a
//   Contract's Self included; an unbound clause counts as closed (its answer Unknown). One writer, PublishSourceStates, run after
//   ExpandContractPremises and where each ValidateConformances starts; every state is Pending from the pass's start (ResetSourceStates)
//   and while its own check runs, so a check reaching its own declaration again reads no evidence (SPEC 8.3.3 Recursion). A
//   publication makes every declaration with nothing to prove Valid before any round, then runs rounds over the Pending ones until a
//   round changes no state and no limit presence (whether a Pending one rests on a limit, BU-22's L), each in a fresh capability epoch
//   (no answer computed from a Pending read outlives its round); after the last state change the limits restart from none. The first
//   round runs in declaration order, each later one over the Pending ones in the order of the Pending states the previous round's
//   checks read (OrderPendingSources: a reader after what it read), so a chain whose reads were recorded settles in one round in any
//   declaration order. Bound: at most P rounds change a state and, after them, at most P a limit, for P Pending declarations, so a
//   publication ends within 2P + 1 rounds. When every read the next round needs was recorded, acyclic reads settle in three rounds (four
//   with a limit: the round after the last state change restarts the limits; R7-C2e2 e2-6). A read the order misses costs a round: an
//   answer the round's epoch kept hides its reads, and a read can appear only after another source settles (AvailableContractPremise
//   returns at the first state not Proven), so such reads can chain; a chain of them, like a limit carried around a cycle of reads, can
//   still take a round per link (C3's scheduler). A state still Pending after the rounds is a final Unknown, resting on a limit or not,
//   order-free; which of several limits it rests on follows BU-22's first read, on which no record depends (each limit's site is needed
//   by its own clause's check).
// - Prohibitions: no check run by a reader; no report or failure here (callers decide); no state kept across passes; no mode or bool
//   parameter.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Every declaration container's own symbol, in the pass's order (ResetSourceStates).
    private readonly List<BindingSymbol> sourceDeclarations = new();

    // A round's Pending declarations in check order, then the next round's (OrderPendingSources). The order's record (this list, the
    // reads, their starts, the walk and the reader) goes with the rounds when C3's scheduler replaces them (BU-59, PLAN G170).
    private readonly List<BindingSymbol> sourceOrder = new();

    // The Pending states each check of the round read (ReadSourceState), grouped by reader in check order, and where each reader's reads begin.
    private readonly List<(BindingSymbol Reader, BindingSymbol Source)> sourceReads = new();
    private readonly Dictionary<BindingSymbol, int> sourceReadStarts = new();
    private readonly List<(BindingSymbol Symbol, int Next)> sourceWalk = new();

    // The declaration whose check the publication runs, whose reads ReadSourceState records (a check that throws leaves it set until
    // ResetSourceStates, which drops the reads it adds meanwhile).
    private BindingSymbol? sourceReader;

    // The checks this run's publications ran (BeginRun).
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

    // Every declaration container's own symbol of the pass's modules is Pending before the pass adds any premise (BindConstraints).
    private void ResetSourceStates()
    {
        this.sourceDeclarations.Clear();
        this.sourceReads.Clear(); // A publication a failure ended keeps nothing.
        this.sourceReadStarts.Clear();
        this.sourceReader = null;
        foreach (var symbol in this.symbols.Values.In(this.pass))
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
        if (this.KeepsAcrossRequests)
        {
            this.ForgetKeptTables();
        }

        var declarations = this.sourceDeclarations;
        var order = this.sourceOrder;
        order.Clear();
        for (var i = 0; i < declarations.Count; i++)
        {
            var closed = this.HasClosedClause((DeclarationContainerKoto)declarations[i].Declaration);
            declarations[i].SourceState = closed ? ConstraintProof.Unknown : ConstraintProof.Proven;
            declarations[i].SourceCause = null;
            declarations[i].SourceFact = null;
            if (closed)
            {
                order.Add(declarations[i]);
            }
        }

        this.BeginProof(QuerySite.Nested); // The publication opens no frame: each state's check runs in its declaration's site frame.
        var stateChanged = order.Count != 0;
        var limitChanged = false;
        while (stateChanged || limitChanged)
        {
            this.ClearCapabilityResults();
            for (var n = 0; stateChanged && n < order.Count; n++)
            {
                order[n].SourceCause = null;
                order[n].SourceFact = null;
            }

            stateChanged = false;
            limitChanged = false;
            for (var n = 0; n < order.Count; n++)
            {
                var symbol = order[n];
                this.ScopeReads = default;
                this.sourceChecks++;
                this.sourceReadStarts[symbol] = this.sourceReads.Count;
                this.sourceReader = symbol;
                var frame = this.BeginConsultation(symbol.Declaration); // A site frame: the state's declaration publishes it.
                var proof = this.CheckClosedDeclarationConstraints((DeclarationContainerKoto)symbol.Declaration);
                this.DropConsultation(frame);
                this.sourceReader = null;
                var cause = this.ProofCause(proof);
                stateChanged |= proof != ConstraintProof.Unknown;
                limitChanged |= (cause is null) != (symbol.SourceCause is null);
                symbol.SourceState = proof;
                // R7-C3 C3-1b (F's C2 rulings 289-290, C3): a Refuted state is an invalid declaration, so it carries its cause too: the
                // declaration's one PremiseFailure (made once, kept), which a reader of the source rests on (AvailableContractPremise).
                symbol.SourceCause = proof == ConstraintProof.Refuted ? this.RefutedCause(symbol) : cause;
                symbol.SourceFact = this.ProofFact(proof); // Payload, outside the loop's change test.
            }

            this.OrderPendingSources();
        }

        this.EndProof(ConstraintProof.Proven);
    }

    // The next round's order (R7-C2e2 e2-6): the round's Pending declarations, each after the Pending ones its check read, depth first in
    // the round's order (a read back into the walk's own path is skipped). The order only schedules: every Pending declaration is checked
    // in every round, so the fixpoint is BU-59's, and a read the order misses (an answer the epoch kept from another check, or a read that
    // appears only after another source settles) costs a round, never a result.
    private void OrderPendingSources()
    {
        var order = this.sourceOrder;
        var reads = this.sourceReads;
        var walk = this.sourceWalk;
        var count = order.Count;
        for (var n = 0; n < count; n++)
        {
            if (order[n].SourceState == ConstraintProof.Unknown && this.sourceReadStarts.Remove(order[n], out var first))
            {
                walk.Add((order[n], first));
            }

            while (walk.Count != 0)
            {
                var (symbol, next) = walk[^1];
                if (next < reads.Count && ReferenceEquals(reads[next].Reader, symbol))
                {
                    walk[^1] = (symbol, next + 1);
                    var source = reads[next].Source;
                    if (source.SourceState == ConstraintProof.Unknown && this.sourceReadStarts.Remove(source, out var start))
                    {
                        walk.Add((source, start));
                    }

                    continue;
                }

                walk.RemoveAt(walk.Count - 1);
                order.Add(symbol);
            }
        }

        order.RemoveRange(0, count);
        reads.Clear();
        this.sourceReadStarts.Clear();
    }

    // The cause a Refuted state carries (BU-57, the producer's invariant): the declaration's one PremiseFailure. A Contract is a premise
    // source (AvailableContractPremise reads it), and its scope always has an environment (its Self premises), so a Refuted Contract
    // without one is a broken contract; another declaration's Refuted state is read only as a verdict (VerifyConformance's
    // `declarationProof`), so it may carry none.
    private ICheckCause? RefutedCause(BindingSymbol symbol)
        => this.scopes[symbol.Declaration].Constraints?.Cause(symbol.Declaration) ??
            (symbol.Declaration is ContractKoto ? throw new InvalidOperationException($"A Refuted Contract `{symbol.Declaration}` has no cause to publish (InternalInvariant; R7-C3 C3-1b).") : null);

    // A read of a declaration's published state (a bound Contract reference reads its declaration's): inside a proof scope it joins that
    // scope directly, which is what a scope of its own would hand on (EndProof); outside every scope it settles in its own, as the check it
    // replaces did. A Pending state a publication's check reads orders the next round (OrderPendingSources).
    private ConstraintProof ReadSourceState(BindingSymbol symbol, QuerySite site, out Completion completion)
    {
        var declaration = ((DeclarationContainerKoto)symbol.Declaration).BoundSymbol!;
        completion = declaration.SourceState is ConstraintProof.Proven or ConstraintProof.Refuted ? Completion.Complete : Completion.Provisional;
        if (this.sourceReader is { } reader && declaration.SourceState == ConstraintProof.Unknown)
        {
            this.sourceReads.Add((reader, declaration));
        }

        if (this.proofNesting != 0)
        {
            return this.ReadVerdict(declaration.SourceState, declaration.SourceCause, declaration.SourceFact);
        }

        this.BeginProof(site);
        return this.EndProof(this.ReadVerdict(declaration.SourceState, declaration.SourceCause, declaration.SourceFact));
    }

    private ConstraintProof CheckClosedDeclarationConstraints(DeclarationContainerKoto container)
    {
        this.BeginProof(container);
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
            var formation = subject is null ? ConstraintProof.Unknown : this.CheckTypeConstraints(subject, scope, QuerySite.Nested);
            proof = CombineProof(proof, CombineProof(formation, this.ProveConstraint(constraint, scope, QuerySite.Nested), true), true);
        }

        return this.EndProof(proof);
    }
}
