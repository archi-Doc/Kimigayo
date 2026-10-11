// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly List<FunctionKoto> virtualDeclarations = new();
    private Dictionary<FunctionKoto, string>? virtualDeclarationFailures;

    private static string? VirtualDeclarationFailure(FunctionKoto function, BindingSymbol symbol)
    {
        if (function.IsVirtual && function.IsOverride)
        {
            return "virtual and override cannot be combined";
        }

        if (symbol.Scope.Owner is not StructKoto structure || function.IsConstructor || function.IsDestructor || function.IsRequirement || function.IsAnonymous || function.IsSpecialization)
        {
            return "virtual and override require a direct struct instance function";
        }

        if ((function.Modifier & ModifierKind.Unsafe) != 0)
        {
            return "virtual and override functions must be safe";
        }

        if (function.GenericArguments.Count != 0)
        {
            return "a virtual slot has no function-owned generic parameters; enclosing Type parameters remain allowed";
        }

        if (symbol.ReceiverIndex < 0 || ReceiverShape(symbol) is not (SemanticsKind.ObjRef or SemanticsKind.ObjUniq))
        {
            return "the receiver must be objref/Self or objuniq/Self; bare self means ref/Self";
        }

        if (function.Body is null && function.ExpressionBody is null && !function.MissingBody)
        {
            return "a virtual or override function requires a body";
        }

        if (function.IsVirtual)
        {
            if ((structure.Modifier & ModifierKind.Open) == 0)
            {
                return "an original virtual slot requires an open struct";
            }

            if (function.Modifier.ExtractAccessibilityModifiers() is ModifierKind.NoModifier or ModifierKind.Private)
            {
                return "an original virtual slot cannot be private; omitted accessibility is private";
            }
        }
        else
        {
            if (function.Modifier != ModifierKind.Override || function.AttributeChain is not null || function.NameBoundaryIndex >= 0 || function.TypeConstraints.Count != 0 || function.EffectBounds.Count != 0)
            {
                return "an override inherits access, attributes, the named-argument boundary, constraints and effects; do not redeclare them";
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                if (parameter.DefaultValue is not null || parameter.AttributeChain is not null)
                {
                    return "an override inherits parameter defaults and attributes; do not redeclare them";
                }
            }
        }

        return null;
    }

    private void ResetVirtualDeclarations()
    {
        this.virtualDeclarations.Clear();
        this.virtualEffectBounds.Clear();
        this.virtualEffectViolations?.Clear();
        this.virtualDeclarationFailures?.Clear();
        this.virtualOverrides.Clear();
        this.overrideEntries.Clear();
        this.overrideFailures?.Clear();
        this.baseCallFailures?.Clear();
    }

    // SPEC 6.2.4: declaration eligibility precedes slot matching and body proofs.
    private void ValidateVirtualDeclarations()
    {
        for (var i = 0; i < this.nodes.Count; i++)
        {
            if (this.nodes[i] is not FunctionKoto { BoundSymbol: { } symbol } function || (!function.IsVirtual && !function.IsOverride))
            {
                continue;
            }

            this.virtualDeclarations.Add(function);
            var reason = VirtualDeclarationFailure(function, symbol);
            if (reason is not null)
            {
                (this.virtualDeclarationFailures ??= new(ReferenceEqualityComparer.Instance))[function] = reason;
                this.Fail(function, BindingFailure.VirtualDeclaration);
            }
        }
    }

    // Shared receivers preserve completeness by their authority (SPEC 12.4.4.2) and proceed
    // through ordinary ownership checking. Exclusive OCC still needs its fixed point.
    private void GuardPendingVirtualDeclarations()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (function.BindingFailure == BindingFailure.None && ReceiverShape(function.BoundSymbol!) != SemanticsKind.ObjRef)
            {
                this.FailExplained(ref this.unsupportedSpans, function, BindingFailure.Unsupported, function.DispatchModifierSpan, true);
            }
            else if (function.BindingFailure == BindingFailure.Unsupported)
            {
                // Binding a body can complete its result Type without discharging the earlier subset gate.
                function.BindingState = BindingState.Unresolved;
            }
        }
    }

    private void PrepareVirtualEffectBounds()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (!function.IsVirtual || function.BindingFailure != BindingFailure.None)
            {
                continue;
            }

            EffectBoundKoto? confined = null;
            EffectBoundKoto? preserves = null;
            for (var i = 0; i < function.EffectBounds.Count; i++)
            {
                var effect = function.EffectBounds[i];
                ref var earlier = ref (effect.Bound == EffectBoundKind.Confined ? ref confined : ref preserves);
                if (this.ValidateDeclaredEffectBound(effect, function.BoundSymbol!, this.scopes[function], earlier))
                {
                    earlier = effect;
                }
            }

            this.virtualEffectBounds[function] = (confined, preserves);
        }
    }
}
