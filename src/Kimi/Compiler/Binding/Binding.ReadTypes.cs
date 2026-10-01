// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 3.5.3: a read Type is a Scalar or a Type satisfying Position or PositionRange. Where a read Type is required, safe
// value-reference layers ending in it supply its value by the value read, including an owning receiver (SPEC 7.3).
public sealed partial class Binding
{
    /// <summary>Gets the terminal of safe value-reference layers that ends in a read Type, or null.</summary>
    /// <param name="type">The supplied Type.</param>
    /// <param name="use">The node whose scope supplies the Constraints of a generic terminal.</param>
    /// <returns>The terminal read Type, or null when <paramref name="type"/> is no such chain.</returns>
    private BoundType? ReadTypeReferent(BoundType? type, Koto use)
    {
        if (ScalarReferent(type) is { } scalar)
        {
            return scalar;
        }

        // A primitive terminal that is not a Scalar is never read; other terminals need their scope.
        return type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            ComparisonReferent(type) is { Kind: not BoundTypeKind.Primitive } terminal && this.IsReadType(terminal, this.ConstraintScope(use)) ? terminal : null;
    }

    /// <summary>Gets the terminal of safe value-reference layers that ends in a read Type, or null.</summary>
    /// <param name="type">The supplied Type.</param>
    /// <param name="scope">The scope that supplies the Constraints of a generic terminal.</param>
    /// <returns>The terminal read Type, or null when <paramref name="type"/> is no such chain.</returns>
    private BoundType? ReadTypeReferent(BoundType? type, BindingScope scope)
    {
        if (ScalarReferent(type) is { } scalar)
        {
            return scalar;
        }

        return type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
            ComparisonReferent(type) is { Kind: not BoundTypeKind.Primitive } terminal && this.IsReadType(terminal, scope) ? terminal : null;
    }

    // A non-primitive read Type: a Type parameter proven PrimitiveInteger, or a Type proven to satisfy one of the closed
    // Contracts. Only Kimi declarations conform to them, so any other nominal Type is rejected without a proof.
    private bool IsReadType(BoundType type, BindingScope scope)
    {
        if (type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed)
        {
            if (!ReferenceEquals(type.Symbol?.Declaration.CodeContext.Kotonoha, this.Library.Kotonoha))
            {
                return false;
            }
        }
        else if (type.Kind is not (BoundTypeKind.Parameter or BoundTypeKind.AssociatedProjection))
        {
            return false;
        }

        return this.IsGenericInteger(type, scope) || this.IsGenericWrapping(type, scope) || this.ProvesClosedContract(type, this.Library.Position, scope) ||
            this.ProvesClosedContract(type, this.Library.PositionRange, scope);
    }

    private bool ProvesClosedContract(BoundType type, BindingSymbol contract, BindingScope scope)
        => this.ProveConstraint(this.InternConstraint(new(ConstraintKind.Contract, type, contract: contract)), scope) == ConstraintProof.Proven;

    // SPEC 10.2.1: a parameter whose Type is a read Type under the declaration's Constraints (a Type parameter implying
    // Position, PositionRange or PrimitiveInteger, or Wrapping<T> over such a parameter) binds the terminal referent of safe
    // reference layers; only value Types are read Types, so no other binding exists.
    private bool InfersReadReferent(BoundType parameter, Koto function)
        => this.scopes.TryGetValue(function, out var scope) &&
            (this.IsGenericInteger(parameter, scope) || this.IsGenericWrapping(parameter, scope) || this.ProvesClosedContract(parameter, this.Library.Position, scope) ||
            this.ProvesClosedContract(parameter, this.Library.PositionRange, scope));
}
