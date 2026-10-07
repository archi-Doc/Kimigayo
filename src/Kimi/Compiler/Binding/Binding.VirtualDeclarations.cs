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

            foreach (var parameter in function.Parameters)
            {
                if (parameter.DefaultValue is not null || parameter.AttributeChain is not null)
                {
                    return "an override inherits parameter defaults and attributes; do not redeclare them";
                }
            }
        }

        return null;
    }

    // SPEC 6.2.4: declaration eligibility precedes slot matching and body proofs.
    private void ValidateVirtualDeclarations()
    {
        this.virtualDeclarations.Clear();
        this.virtualDeclarationFailures?.Clear();
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

    // V1 retains valid declarations without accepting their bodies as statically dispatched code.
    // Remove this boundary only after slot correspondence, public guarantees and generation agree.
    private void GuardPendingVirtualDeclarations()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (function.BindingFailure == BindingFailure.None)
            {
                this.Fail(function, BindingFailure.Unsupported, true);
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
            foreach (var effect in function.EffectBounds)
            {
                ref var earlier = ref (effect.Bound == EffectBoundKind.Confined ? ref confined : ref preserves);
                if (this.ValidateDeclaredEffectBound(effect, function.BoundSymbol!, this.scopes[function], earlier))
                {
                    earlier = effect;
                }
            }
        }
    }

    private void ReportVirtualDeclaration(FunctionKoto function, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        if (code == DiagnosticCode.UnsupportedBinding_Kd)
        {
            function.Report(requirement, code, span: function.DispatchModifierSpan, note: "Virtual declarations are retained, but slot correspondence, receiver-completeness proofs and dynamic generation are not yet implemented; no static-call fallback is emitted");
        }
        else
        {
            function.Report(requirement, code, span: function.DispatchModifierSpan, evidence: [this.virtualDeclarationFailures![function]], related: [("declaration", function, "the virtual or override declaration")]);
        }
    }
}
