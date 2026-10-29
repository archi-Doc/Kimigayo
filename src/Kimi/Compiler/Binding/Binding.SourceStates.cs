// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SourceStates (PLAN G170 N6, C1 §5; DECISIONS BU-57): the own check of a premise source, its declaration's closed clauses.
// - Inputs: a declaration container's Constraint clauses (SPEC 8.2.1), bound in its scope; proof (CheckTypeConstraints for a clause's
//   subject, ProveConstraint).
// - Outputs: the declaration's ConstraintProof over its closed clauses, an unbound one Unknown; its proof scope (BeginProof, EndProof)
//   keeps the cause the answer rests on (BU-22).
// - Invariants: a closed clause (ClosedClause) is neither associated, a Self clause nor dependent on the declaration's parameters, a
//   Contract's Self included. Until N6's U1 the check runs at every read, unmemoized: premise availability (AvailableContractPremise,
//   a Contract and each ancestor) and conformance verification (VerifyConformance); a check reaching its own declaration again
//   through a premise reads no evidence (SPEC 8.3.3 Recursion; AvailableContractPremise's contractPremiseQueries).
// - Prohibitions: no report or failure here (callers decide); no mode or bool parameter. U1 publishes each declaration's state here,
//   which proof reads and never runs.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private static bool ClosedClause(DeclarationContainerKoto container, IsKoto clause)
        => !clause.IsAssociatedConstraint && !IsSelfConstraint(clause) &&
            (clause.BoundConstraint is not { } constraint || !DependentConstraint(constraint, contractSelf: container is ContractKoto));

    private ConstraintProof CheckClosedDeclarationConstraints(DeclarationContainerKoto container)
    {
        var outer = this.BeginProof();
        var proof = ConstraintProof.Proven;
        for (var i = 0; i < container.ConstraintNodes.Count; i++)
        {
            var clause = container.ConstraintNodes[i];
            if (!ClosedClause(container, clause))
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
