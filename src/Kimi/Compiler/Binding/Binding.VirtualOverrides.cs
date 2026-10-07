// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A semantic slot is the original declaration and its complete declaring binding.
    // The checked inheritance path is retained separately; physical indices belong to generation.
    internal readonly record struct VirtualSlot(FunctionKoto Original, BoundType DeclaringType);

    internal readonly record struct VirtualOverride(VirtualSlot Slot, BoundType ImplementingType, BoundMemberPath? Path);

    private readonly Dictionary<FunctionKoto, VirtualOverride> virtualOverrides = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(BindingSymbol Type, VirtualSlot Slot), FunctionKoto> overridesBySlot = new();
    private Dictionary<FunctionKoto, OverrideFailure>? overrideFailures;

    private readonly record struct OverrideFailure(string Reason, FunctionKoto? First, FunctionKoto? Second = null, Koto? At = null, object? Required = null, object? Actual = null, SourceSpan? Span = null);

    // Correspondence alone is not a verified implementation or permission to execute it.
    internal bool TryGetVirtualOverride(FunctionKoto function, out VirtualOverride result)
    {
        result = default;
        return (this.IsRunning || this.Result != default) && this.virtualOverrides.TryGetValue(function, out result);
    }

    private void PrepareVirtualOverrides()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (!function.IsOverride || function.BindingFailure != BindingFailure.None || function.BoundSymbol?.Scope.Owner is not StructKoto { BoundSymbol: { } typeSymbol } structure)
            {
                continue;
            }

            var self = this.SelfType(typeSymbol);
            var baseType = this.StoredBase(self);
            if (structure.BindingState == BindingState.Invalid || (structure.Bases.Count != 0 && baseType is null))
            {
                this.CompleteDependent(function, structure);
                continue;
            }

            var selection = baseType is null ? default : this.LookupTypeMember(baseType, function.Name, this.scopes[function], self, this.MemberPath(null, structure.Bases[0], baseType));
            FunctionKoto? first = null;
            FunctionKoto? second = null;
            var count = 0;
            var pending = selection.Pending;
            for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsVirtual: true } original || !this.Accessible(candidate, this.scopes[function], receiverType: self))
                {
                    continue;
                }

                if (original.BindingFailure != BindingFailure.None)
                {
                    this.AddPrerequisite(function, original);
                    pending = true;
                    continue;
                }

                if (selection.DeclaringType is { } declaring && this.VirtualInputsMatch(original, function, declaring))
                {
                    first ??= original;
                    if (count == 1)
                    {
                        second = original;
                    }

                    count++;
                }
            }

            if (pending)
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            if (count != 1)
            {
                var reason = count != 0 ? "multiple distinct virtual declarations have matching inputs after base binding; results, labels and effects do not disambiguate them"
                    : baseType is null ? "this struct has no base from which to inherit a virtual slot"
                    : selection.Member is null ? "no accessible inherited function group has this Name"
                    : "the first accessible inherited group contains no virtual slot with the same receiver position, mode and input Types";
                this.FailOverride(function, count == 0 ? BindingFailure.MissingOverrideTarget : BindingFailure.AmbiguousOverrideTarget, reason, first ?? selection.Member?.Declaration as FunctionKoto, second);
                continue;
            }

            var slot = new VirtualSlot(first!, selection.DeclaringType!);
            this.virtualOverrides.Add(function, new(slot, self, selection.Path));
            var key = (typeSymbol, slot);
            if (this.overridesBySlot.TryGetValue(key, out var previous))
            {
                this.FailOverride(function, BindingFailure.DuplicateOverride, "this derived Type already supplies an implementation of the same bound slot", slot.Original, previous);
                continue;
            }

            this.overridesBySlot.Add(key, function);
            if (IncompleteSignature(slot.Original) is { } prerequisite)
            {
                this.CompleteDependent(function, prerequisite);
                continue;
            }

            this.CheckVirtualOverrideSignature(function, slot);
            if (function.BindingFailure == BindingFailure.None && !this.CompleteImplementationOrigins(function, slot.Original, [], [], slot.DeclaringType, self))
            {
                this.FailOverride(function, BindingFailure.OverrideContractMismatch, "Origins must preserve the original slot's binder and input/result dependencies; omitted annotations inherit that contract", slot.Original);
            }
        }
    }

    // Selection deliberately ignores results, external labels, Origins, conditions and body facts.
    private bool VirtualInputsMatch(FunctionKoto original, FunctionKoto implementation, BoundType declaring)
    {
        if (original.Parameters.Count != implementation.Parameters.Count || original.BoundSymbol!.ReceiverIndex != implementation.BoundSymbol!.ReceiverIndex)
        {
            return false;
        }

        for (var i = 0; i < original.Parameters.Count; i++)
        {
            var required = original.Parameters[i].Type.BoundType;
            var actual = implementation.Parameters[i].Type.BoundType;
            if (required is null || actual is null)
            {
                return false;
            }

            if (i == original.BoundSymbol.ReceiverIndex)
            {
                if (required.Semantics != actual.Semantics)
                {
                    return false;
                }
            }
            else if (this.MemberType(required, declaring) is not { } bound || !SignatureEquals(bound, actual, original, implementation))
            {
                return false;
            }
        }

        return true;
    }

    private void CheckVirtualOverrideSignature(FunctionKoto implementation, VirtualSlot slot)
    {
        var original = slot.Original;
        for (var i = 0; i < original.Parameters.Count; i++)
        {
            if (original.Parameters[i].ExternalName != implementation.Parameters[i].ExternalName)
            {
                this.FailOverride(implementation, BindingFailure.OverrideContractMismatch, "external parameter names must match the original slot; only internal names may change", original, required: original.Parameters[i].ExternalName, actual: implementation.Parameters[i].ExternalName, span: implementation.Parameters[i].ExternalNameSpan);
                return;
            }
        }

        if (original.BoundSymbol!.Type is { } required && implementation.BoundSymbol!.Type is { } actual && this.MemberType(required, slot.DeclaringType) is { } bound &&
            (ResultModeOf(original.ReturnType) != ResultModeOf(implementation.ReturnType) || !SignatureEquals(bound, actual, original, implementation)))
        {
            this.FailOverride(implementation, BindingFailure.OverrideContractMismatch, "the result Type and value/Place mode must match the original slot; result covariance is not allowed", original, at: implementation.ReturnType, required: bound, actual: actual);
        }
    }

    private void FailOverride(FunctionKoto function, BindingFailure failure, string reason, FunctionKoto? original, FunctionKoto? other = null, Koto? at = null, object? required = null, object? actual = null, SourceSpan? span = null)
    {
        (this.overrideFailures ??= new(ReferenceEqualityComparer.Instance))[function] = new(reason, original, other, at, required, actual, span);
        this.Fail(function, failure);
    }

    private void ReportOverrideFailure(FunctionKoto function, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var failure = this.overrideFailures![function];
        var related = failure.First is null ? null : failure.Second is null
            ? new (string, Koto, string?)[] { ("declaration", failure.First, failure.First.IsVirtual ? "original virtual slot" : "inherited function") }
            : [("declaration", failure.First, "original virtual slot"), ("declaration", failure.Second, "conflicting declaration")];
        var reason = failure.Reason;
        if (failure.Required is BoundType required && failure.Actual is BoundType actual)
        {
            reason = $"expected {ResultModeOf(failure.First!.ReturnType)} {DiagnosticTypeName(required)}, found {ResultModeOf(function.ReturnType)} {DiagnosticTypeName(actual)}; {reason}";
        }
        else if (failure.Required is string requiredName && failure.Actual is string actualName)
        {
            reason = $"expected '{requiredName}', found '{actualName}'; {reason}";
        }

        function.Report(requirement, code, at: failure.At, span: failure.Span ?? (failure.At is null ? function.DispatchModifierSpan : null), evidence: [reason], related: related);
    }
}
