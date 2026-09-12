// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // Retained operation and its binder.

/// <summary>A runtime Supports test. Types retain identity, Semantics and Origins; this is not a Loan certificate.</summary>
/// <param name="OperandType">The complete operand Type, including Never for a non-producing operand.</param>
/// <param name="TargetType">The resolved struct Core queried against the original Dynamic Type.</param>
public readonly record struct BoundRuntimeTypeTest(BoundType OperandType, BoundType TargetType)
{
    /// <summary>Gets a value indicating whether evaluation requires shared access, without acquiring an owned operand value.</summary>
    public bool RequiresSharedAccess => true;
}

public sealed partial class Binding
{
    private static bool IsRuntimeStructCore(BoundType type)
        => type.Kind is BoundTypeKind.Nominal or BoundTypeKind.Constructed &&
            type.Semantics == SemanticsKind.Owner && type.Symbol?.Declaration is StructKoto;

    private static bool HasRuntimeTypeDependency(BoundType type)
    {
        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication or BoundTypeKind.AssociatedProjection ||
            type.LengthExpression is not null)
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (HasRuntimeTypeDependency(type.Components[i]))
            {
                return true;
            }
        }

        return false;
    }

    private BoundType? BindRuntimeTypeTest(IsKoto test, BindingScope scope)
    {
        Debug.Assert(test.IsRuntimeTest && test.BoundConstraint is null);
        var operand = this.BindNode(test.Left, scope);
        // Use ordinary Type binding, including aliases, argument access and invalidation.
        // There is no expected Type from the operand and no fallback to a Requirement Test.
        var target = this.BindType(test.Right, scope);
        if (operand is null || target is null)
        {
            return Complete(test, null);
        }

        if (HasRuntimeTypeDependency(target) || test.Right.BoundSymbol?.Kind == BindingSymbolKind.AssociatedType)
        {
            return Fail(test, BindingFailure.Unsupported, true);
        }

        if (!IsRuntimeStructCore(target) || target.Origin is not null || target.OriginArguments.Count != 0)
        {
            return Fail(test, BindingFailure.InvalidTypeFormation);
        }

        // SPEC 3.8: Never fits a valid operand position, but supplies no value or Boolean exit.
        // Do not invent an object Type for it or skip the target/transfer checks above.
        if (!ReferenceEquals(operand, BoundType.Never))
        {
            if (HasRuntimeTypeDependency(operand))
            {
                return Fail(test, BindingFailure.Unsupported, true);
            }

            if (operand.Kind != BoundTypeKind.Semantics ||
                operand.Semantics is not (SemanticsKind.Obj or SemanticsKind.Rc or SemanticsKind.Arc or SemanticsKind.ObjRef or SemanticsKind.ObjUniq) ||
                !IsRuntimeStructCore(operand.Components[0]))
            {
                return Fail(test, BindingFailure.TypeMismatch);
            }
        }

        test.BoundRuntimeTest = new(operand, target);
        return Complete(test, BoundType.Boolean);
    }
}
