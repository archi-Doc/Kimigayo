// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.3, 10.5.5, 10.8: a declaration's Constraint clauses under a substitution of its slots, and their judgment.
public sealed partial class Binding
{
    // SPEC 6.2.2, 15.3.1, 9.7.2 (PLAN G425): a declaration's clause at an occurrence of its Type, under the occurrence's Type and Origin
    // arguments in one pass, as StoredType substitutes a Field's Type.
    private BoundConstraint StoredConstraint(BoundConstraint constraint, BoundType owner)
    {
        Span<BoundOrigin> single = [BoundOrigin.Static];
        return this.SubstituteConstraint(constraint, owner.Symbol!.Declaration, (BoundType[])owner.Components, origins: OccurrenceOrigins(owner, single));
    }

    // A clause under its binder's substitution (SubstituteType): its Type, length and, given their images, declared Origin slots, which a
    // call's own slots do not have yet (PLAN G417).
    private BoundConstraint SubstituteConstraint(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths = default, bool incomplete = false, ReadOnlySpan<BoundOrigin> origins = default)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths, incomplete, origins));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths, incomplete, origins), right: this.SubstituteConstraint(constraint.Right!, binder, arguments, lengths, incomplete, origins)));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.SubstituteType(constraint.Subject, binder, arguments, lengths, origins);
        var required = constraint.RequiredType is null ? null : this.SubstituteType(constraint.RequiredType, binder, arguments, lengths, origins);
        var contract = constraint.Contract;
        if (contract?.Type is { } reference && !ReferenceEquals(reference.Symbol, contract))
        {
            // SPEC 10.8: a Contract argument that holds a slot no evidence binds leaves the clause unresolved, never the callee's own.
            var substituted = this.SubstituteType(reference, binder, arguments, lengths, origins);
            subject = substituted is null && incomplete ? null : subject;
            contract = substituted is null ? contract : this.BoundContractReference(substituted);
        }

        return subject is null || (constraint.RequiredType is not null && required is null) ? this.InternConstraint(new(incomplete ? ConstraintKind.Unresolved : ConstraintKind.Error)) : this.InternConstraint(new(constraint.Kind, subject, required, contract, constraint.Mask));
    }

    // With `skipUnresolved`, a clause whose judgment needs an unbound slot, and that is not otherwise refuted, is not judged (SPEC 10.8):
    // it contributes Proven, so only the clauses that can be judged decide.
    private ConstraintProof CheckConstraints(IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BindingScope scope, BoundType? self = null, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengths = default, bool incomplete = false, bool skipUnresolved = false, Koto? slotOwner = null, ReadOnlySpan<BoundOrigin> origins = default)
    {
        this.BeginProof(); // A clause list is one conjunction (DECISIONS BU-22).
        var result = ConstraintProof.Proven;
        ClauseFact? best = null;
        for (var i = 0; i < clauses.Count; i++)
        {
            var constraint = (IsKoto)clauses[i];
            // A bound Contract reference supplies its parameters; its associated requirements
            // are obligations of a conforming Self, not of forming the reference (SPEC 8.4.3).
            if (binder is ContractKoto && constraint.IsAssociatedConstraint)
            {
                continue;
            }

            if (binder is DeclarationContainerKoto &&
                constraint.Left.BoundSymbol?.Kind is not
                    (BindingSymbolKind.TypeParameter or BindingSymbolKind.SemanticsTarget or BindingSymbolKind.SemanticsParameter) &&
                constraint.Left.BoundType?.Kind != BoundTypeKind.AssociatedProjection && !this.IsDependentConstraint(constraint))
            {
                continue;
            }

            if (constraint.BoundConstraint is not { } bound)
            {
                result = ConstraintProof.Error;
                best = FirstAtVerdict(best, new(constraint, null, ConstraintProof.Error, Limit: false));
                continue;
            }

            var substituted = this.SubstituteConstraint(bound, slotOwner ?? binder, arguments, lengths, incomplete, origins);
            if (declaringType?.Symbol is not null)
            {
                substituted = this.StoredConstraint(substituted, declaringType);
            }

            // Associated identities also need normalization for calls without a receiver.
            substituted = this.ContractConstraint(substituted, scope, self);
            var unknown = this.ScopeReads.Unknown;
            var limit = this.ScopeReads.Limit;
            var proof = this.ProveConstraint(substituted, scope);
            if (skipUnresolved && proof == ConstraintProof.Unknown && substituted.HasUnresolved && !bound.HasUnresolved)
            {
                proof = ConstraintProof.Proven;
                this.ScopeReads.Unknown = unknown; // A clause that is not judged contributes no U fact.
            }
            else if (this.ScopeReads.Unknown is { } fact && !ReferenceEquals(fact, unknown))
            {
                fact.Clause = constraint; // The outermost clause list's stamp is the last (design-r2 Amendment 4).
            }

            var outcome = bound.HasUnresolved && proof == ConstraintProof.Proven ? ConstraintProof.Unknown : proof;
            if (outcome != ConstraintProof.Proven)
            {
                var onLimit = outcome == ConstraintProof.Unknown && this.ScopeReads.Limit is { } read && !ReferenceEquals(read, limit);
                best = FirstAtVerdict(best, new(constraint, substituted, outcome, onLimit));
            }

            result = CombineProof(result, outcome, true);
        }

        var verdict = this.EndProof(result);
        this.LeaveClause(best, result);
        return verdict;
    }

    // SPEC 10.5, 8.3: whether a clause that no unbound slot leaves unresolved is Unknown. Such a proof waits on no argument, so it proves
    // neither applicability nor negation even while a waiting argument is still open.
    private bool ResolvedClauseUnknown(IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BindingScope scope, BoundType? self, BoundType? declaringType, ReadOnlySpan<BoundLength?> lengths, Koto? slotOwner = null)
    {
        for (var i = 0; i < clauses.Count; i++)
        {
            if (clauses[i] is not IsKoto { BoundConstraint: { HasUnresolved: false } bound })
            {
                continue;
            }

            var substituted = this.SubstituteConstraint(bound, slotOwner ?? binder, arguments, lengths, true);
            if (declaringType?.Symbol is not null)
            {
                substituted = this.StoredConstraint(substituted, declaringType);
            }

            if (!substituted.HasUnresolved && this.ProveConstraint(this.ContractConstraint(substituted, scope, self), scope) == ConstraintProof.Unknown)
            {
                return true;
            }
        }

        return false;
    }
}
