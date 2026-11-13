// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 8.2.2, 8.2.3: a Constraint clause's requirement, bound against its subject (BindConstraint's other half).
// Input: the requirement syntax (`and`, `or`, `not`, groups, a Contract, a Callable requirement, a complete Type or a Semantics), the
// subject's Type or none, and the clause's scope. Output: the interned proposition; a failed atom records InvalidConstraint at itself.
// Prohibitions: the subject is judged by BindConstraint, never here.
public sealed partial class Binding
{
    private BoundConstraint BindRequirement(Koto node, BoundType? subject, bool semantics, BindingScope scope)
    {
        BoundConstraint result;
        switch (node)
        {
            case ParenthesizedKoto parentheses:
                result = this.BindRequirement(parentheses.Operand, subject, semantics, scope);
                break;
            case UnaryKoto { Akind: KotoKind.Not } not:
                result = this.NegateConstraint(this.BindRequirement(not.Operand, subject, semantics, scope));
                break;
            case BinaryKoto { Akind: KotoKind.And or KotoKind.Or } binary:
                var left = this.BindRequirement(binary.Left, subject, semantics, scope);
                var right = this.BindRequirement(binary.Right, subject, semantics, scope);
                result = this.InternConstraint(new(binary.Akind == KotoKind.And ? ConstraintKind.And : ConstraintKind.Or, left: left, right: right));
                break;
            default:
                if (subject is null)
                {
                    result = this.InternConstraint(new(ConstraintKind.Error));
                }
                else if (semantics)
                {
                    var name = node is IdentifierNameKoto identifier ? identifier.IdentifierName : node is TypeSemanticsKoto { Type: null } typeName ? typeName.Identifier : string.Empty;
                    result = SemanticsMaskHelper.TryParse(name, out SemanticsMask mask)
                        ? this.InternConstraint(new(ConstraintKind.Semantics, subject, mask: mask))
                        : this.InternConstraint(new(ConstraintKind.Error));
                }
                else
                {
                    // One lookup serves both readings, a Contract and a complete Type, in its position's row: Requirement, or a
                    // conditional conformance's Contract Reference (SPEC 9.2, 8.8; no group, no stage 4).
                    var requirement = this.NamePath(node, scope);
                    var target = requirement.Symbol;
                    if (target is { IsContract: true })
                    {
                        if (target.Intrinsic == IntrinsicKind.Callable)
                        {
                            result = this.BindCallableRequirement(node, subject, scope, target);
                            if (result.Kind == ConstraintKind.Error && this.FailedSignaturePart(node) is { } part)
                            {
                                // SPEC 23.1.2: a Callable signature that failed for a part of its own syntax rests on that part.
                                (this.constraintDiagnosticCauses ??= new(ReferenceEqualityComparer.Instance))[node] = part;
                            }

                            break;
                        }

                        var bound = this.BindContractReference(node, target, scope);
                        if (bound is null && !this.PendingReference(node, target, scope))
                        {
                            return this.InternConstraint(new(ConstraintKind.Error)); // The reference's located failure is the record.
                        }

                        // A reference that formed nothing for a missing name, a Type argument's (`C<M>`, SPEC 8.6.1, 9.7.1), is pending
                        // (PendingReference) as a complete Type's is below.
                        result = bound is null ? this.InternConstraint(new(ConstraintKind.Unresolved))
                            : bound.Intrinsic == IntrinsicKind.Callable ? this.InternConstraint(new(ConstraintKind.Error))
                            : this.InternConstraint(new(ConstraintKind.Contract, subject, contract: bound));
                    }
                    else
                    {
                        // A complete Type (SPEC 8.2), formed from the requirement's own lookup (BindNamedType).
                        var type = this.BindType(node, scope);
                        result = type is null ? this.InternConstraint(new(this.HasUnresolvedConstraintSyntax(node) ? ConstraintKind.Unresolved : ConstraintKind.Error)) : this.InternConstraint(new(ConstraintKind.TypeIdentity, subject, type));
                    }
                }

                break;
        }

        if (result.Kind == ConstraintKind.Error)
        {
            // SPEC 23.1.2: a transparent wrapper is the requirement it wraps, so the failure rests on that node; one that failed already
            // keeps its own record, whatever wraps it.
            this.Fail(UnwrapTransparentType(node)!, BindingFailure.InvalidConstraint);
        }
        else if (result.HasUnresolved && node.BindingState != BindingState.Invalid)
        {
            node.BindingState = BindingState.Unresolved;
        }
        else if (node.BoundType is null)
        {
            Complete(node, BoundType.Boolean);
        }

        return result;
    }
}
