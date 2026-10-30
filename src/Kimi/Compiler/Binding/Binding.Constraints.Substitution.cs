// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.3, 10.5.5, 10.8: a declaration's Constraint clauses under a substitution of its slots, and their judgment.
public sealed partial class Binding
{
    private BoundConstraint SubstituteConstraint(BoundConstraint constraint, Koto binder, ReadOnlySpan<BoundType?> arguments, ReadOnlySpan<BoundLength?> lengths = default, bool incomplete = false)
    {
        if (constraint.Kind == ConstraintKind.Not)
        {
            return this.NegateConstraint(this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths, incomplete));
        }

        if (constraint.Kind is ConstraintKind.And or ConstraintKind.Or)
        {
            return this.InternConstraint(new(constraint.Kind, left: this.SubstituteConstraint(constraint.Left!, binder, arguments, lengths, incomplete), right: this.SubstituteConstraint(constraint.Right!, binder, arguments, lengths, incomplete)));
        }

        if (constraint.Subject is null)
        {
            return constraint;
        }

        var subject = this.SubstituteType(constraint.Subject, binder, arguments, lengths);
        var required = constraint.RequiredType is null ? null : this.SubstituteType(constraint.RequiredType, binder, arguments, lengths);
        var contract = constraint.Contract;
        if (contract?.Type is { } reference && !ReferenceEquals(reference.Symbol, contract))
        {
            // SPEC 10.8: a Contract argument that holds a slot no evidence binds leaves the clause unresolved, never the callee's own.
            var substituted = this.SubstituteType(reference, binder, arguments, lengths);
            subject = substituted is null && incomplete ? null : subject;
            contract = substituted is null ? contract : this.BoundContractReference(substituted);
        }

        return subject is null || (constraint.RequiredType is not null && required is null) ? this.InternConstraint(new(incomplete ? ConstraintKind.Unresolved : ConstraintKind.Error)) : this.InternConstraint(new(constraint.Kind, subject, required, contract, constraint.Mask));
    }

    // With `skipUnresolved`, a clause whose judgment needs an unbound slot, and that is not otherwise refuted, is not judged (SPEC 10.8):
    // it contributes Proven, so only the clauses that can be judged decide.
    private ConstraintProof CheckConstraints(IReadOnlyList<Koto> clauses, Koto binder, ReadOnlySpan<BoundType?> arguments, BindingScope scope, BoundType? self = null, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengths = default, bool incomplete = false, bool skipUnresolved = false, Koto? slotOwner = null, Koto? failureUse = null)
    {
        var outer = this.BeginProof(); // A clause list is one conjunction (DECISIONS BU-22).
        var result = ConstraintProof.Proven;
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
                continue;
            }

            var substituted = this.SubstituteConstraint(bound, slotOwner ?? binder, arguments, lengths, incomplete);
            if (declaringType?.Symbol?.Declaration is { } owner)
            {
                substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaringType.Components);
            }

            // Associated identities also need normalization for calls without a receiver.
            substituted = this.ContractConstraint(substituted, scope, self);
            var proof = this.ProveConstraint(substituted, scope);
            if (skipUnresolved && proof == ConstraintProof.Unknown && substituted.HasUnresolved && !bound.HasUnresolved)
            {
                proof = ConstraintProof.Proven;
            }

            if (failureUse is not null)
            {
                this.RecordReferenceConstraint(failureUse, constraint, substituted, proof);
            }

            result = CombineProof(result, bound.HasUnresolved && proof == ConstraintProof.Proven ? ConstraintProof.Unknown : proof, true);
        }

        return this.EndProof(outer, result);
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
            if (declaringType?.Symbol?.Declaration is { } owner)
            {
                substituted = this.SubstituteConstraint(substituted, owner, (BoundType[])declaringType.Components);
            }

            if (!substituted.HasUnresolved && this.ProveConstraint(this.ContractConstraint(substituted, scope, self), scope) == ConstraintProof.Unknown)
            {
                return true;
            }
        }

        return false;
    }
}
