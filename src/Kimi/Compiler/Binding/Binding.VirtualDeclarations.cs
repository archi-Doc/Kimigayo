// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<FunctionKoto> virtualDeclarations = new();
    private Dictionary<FunctionKoto, VirtualDeclarationCause>? virtualDeclarationFailures;

    // SPEC 6.4.1's eligibility and inherited-header rules, in check order; the adapter explains each (DECISIONS BU-111).
    private enum VirtualDeclarationCause : byte
    {
        Combined,
        NotStructFunction,
        Unsafe,
        OwnGenericParameters,
        Receiver,
        NoBody,
        NotOpen,
        Private,
        RedeclaredHeader,
        RedeclaredParameter,
    }

    private static VirtualDeclarationCause? VirtualDeclarationFailure(FunctionKoto function, BindingSymbol symbol)
    {
        if (function.IsVirtual && function.IsOverride)
        {
            return VirtualDeclarationCause.Combined;
        }

        if (symbol.Scope.Owner is not StructKoto structure || function.IsConstructor || function.IsDestructor || function.IsRequirement || function.IsAnonymous || function.IsSpecialization)
        {
            return VirtualDeclarationCause.NotStructFunction;
        }

        if ((function.Modifier & ModifierKind.Unsafe) != 0)
        {
            return VirtualDeclarationCause.Unsafe;
        }

        if (function.GenericArguments.Count != 0)
        {
            return VirtualDeclarationCause.OwnGenericParameters;
        }

        if (symbol.ReceiverIndex < 0 || ReceiverShape(symbol) is not (SemanticsKind.ObjRef or SemanticsKind.ObjUniq))
        {
            return VirtualDeclarationCause.Receiver;
        }

        if (function.Body is null && function.ExpressionBody is null && !function.MissingBody)
        {
            return VirtualDeclarationCause.NoBody;
        }

        if (function.IsVirtual)
        {
            if ((structure.Modifier & ModifierKind.Open) == 0)
            {
                return VirtualDeclarationCause.NotOpen;
            }

            if (function.Modifier.ExtractAccessibilityModifiers() is ModifierKind.NoModifier or ModifierKind.Private)
            {
                return VirtualDeclarationCause.Private;
            }
        }
        else
        {
            if (function.Modifier != ModifierKind.Override || function.AttributeChain is not null || function.NameBoundaryIndex >= 0 ||
                function.TypeConstraints.Count != 0)
            {
                return VirtualDeclarationCause.RedeclaredHeader;
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (parameter.DefaultValue is not null || parameter.AttributeChain is not null)
                {
                    return VirtualDeclarationCause.RedeclaredParameter;
                }
            }
        }

        return null;
    }

    private void ResetVirtualDeclarations()
    {
        this.virtualDeclarations.Clear();
        this.virtualEffectBounds.Clear();
        this.virtualDeclarationFailures?.Clear();
        this.virtualOverrides.Clear();
        this.overrideEntries.Clear();
        this.overrideFailures?.Clear();
        this.baseCallFailures?.Clear();
    }

    // SPEC 6.4: declaration eligibility precedes slot matching and body proofs.
    private void ValidateVirtualDeclarations()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not FunctionKoto { BoundSymbol: { } symbol } function || (!function.IsVirtual && !function.IsOverride))
            {
                continue;
            }

            this.virtualDeclarations.Add(function);
            if (VirtualDeclarationFailure(function, symbol) is { } cause)
            {
                (this.virtualDeclarationFailures ??= new(ReferenceEqualityComparer.Instance))[function] = cause;
                this.Fail(function, BindingFailure.VirtualDeclaration);
            }
        }
    }

    // Shared receivers preserve completeness by their authority (SPEC 12.7.1) and proceed
    // through ordinary ownership checking. Exclusive OCC still needs its fixed point.
    private void GuardPendingVirtualDeclarations()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (function.BindingFailure == BindingFailure.None && ReceiverShape(function.BoundSymbol!) != SemanticsKind.ObjRef)
            {
                this.FailUnsupportedAt(function, function.DispatchModifierSpan, unresolved: true);
            }
            else if (function.BindingFailure == BindingFailure.Unsupported)
            {
                // Binding a body can complete its result Type without discharging the earlier subset gate.
                this.MarkUnresolved(function);
            }
        }
    }
}
