// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

#pragma warning disable SA1402 // The obligation row belongs to the facts that list it.

namespace Kimi.Compiler;

// An obligation of `confined` that Binding decided and A4 judges (DECISIONS R4-M22, R4-M23), reported as Code at Subject under Requirement
// (DECISIONS R5-M23): a conformance's witness (SPEC 8.11.3, at the `Self is C` clause; EffectBound), a virtual function under its slot's
// bound (virtual-dispatch 6; VirtualEffectBound) or a selected Callable binding (SPEC 8.11.4, at the argument or Type argument;
// CallableEffectBound). Definition: the definition whose body owes, or whose premises judge the Callable (null at a module's top-level
// statements); without a Callable, null where the premises already decide a failure. A Callable binding is its subject Type called with
// Receiver; Owner: its owner acquisition also destroys the Closure's remaining environment.
internal readonly record struct EffectObligation(
    SourceRef Subject, DiagnosticCode Code, DiagnosticRequirement Requirement, BindingSymbol? Definition, BoundType? Callable, SemanticsKind Receiver,
    bool Owner);

public sealed partial class Binding
{
    /// <summary>Lists the <c>confined</c> obligations of the latest pass (DECISIONS R4-M22): each witness of a conformance whose Contract
    /// or an ancestor declares <c>confined</c> for its requirement, each virtual function whose slot declares it, and each selected use of a
    /// Callable clause declaring it whose premises do not prove it. A conformance or virtual function that the effect checks rejected
    /// keeps its obligation; a holding premise adds none.</summary>
    /// <param name="rows">The caller-owned list to append to.</param>
    internal void EffectObligations(List<EffectObligation> rows)
    {
        foreach (var path in this.activeConformancePaths)
        {
            // R6: the old effect checks' violations do not void the obligation; once R6 deletes them, it follows the path's validity.
            if ((!path.IsVerified && this.effectViolations?.ContainsKey(path.Use) != true) || path.Contract.Contract is not { } shape)
            {
                continue;
            }

            this.EnsureEffectBounds(shape);
            for (var w = 0; shape.HasEffectBounds && w < path.WitnessStorage.Count; w++)
            {
                var witness = path.WitnessStorage[w];
                if (witness.Requirement.Declaration is not FunctionKoto || this.DeclaredEffectBound(shape, witness.Identity) is null)
                {
                    continue;
                }

                // A virtual witness dispatches: its slot's bound decides, never its own body (virtual-dispatch 6).
                var slot = witness.Implementation.Declaration as FunctionKoto;
                var dispatched = slot is { IsVirtual: true };
                if (!dispatched || !this.AvailableEffectBounds(slot!, null, path.Scope))
                {
                    var target = dispatched ? null : witness.Implementation;
                    var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.EffectBound);
                    var code = DiagnosticCode.IncompatibleContractImplementation_Kd;
                    rows.Add(new(SourceRef.Of(path.Use), code, requirement, target, null, default, Owner: false));
                }
            }
        }

        foreach (var function in this.virtualDeclarations)
        {
            var original = function.IsVirtual ? function : this.virtualOverrides.GetValueOrDefault(function).Slot.Original;
            var bounded = original is not null && this.virtualEffectBounds.GetValueOrDefault(original) is not null;

            // R6: likewise the old virtual effect check's violations, until R6 deletes it.
            if (bounded && function.BoundSymbol is { } symbol &&
                (function.BindingFailure == BindingFailure.None || this.virtualEffectViolations?.ContainsKey(function) == true))
            {
                var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.VirtualEffectBound);
                rows.Add(new(SourceRef.Of(function), DiagnosticCode.UnsatisfiedEffectBound_Kd, requirement, symbol, null, default, Owner: false));
            }
        }

        foreach (var use in this.callableEffectUses)
        {
            for (var e = 0; e < use.Clause.EffectBounds.Count; e++)
            {
                var bound = use.Clause.EffectBounds[e];
                if (this.AppliesBound(bound))
                {
                    this.AddCallableObligation(rows, use);
                }
            }
        }
    }

    // SPEC 8.11.3 (X2: the calling definition's own premises, never an instance's): the bounds a requirement or virtual call may use,
    // every bound for a requirement of a closed Contract, else the requirement's or the virtual slot's available bounds; null for a callee
    // that does not dispatch. The contract is the requirement's dispatching Contract reference, null for its own Contract.
    internal bool? DispatchBounds(BindingSymbol? definition, BindingSymbol callee, BoundType? self, BindingSymbol? contract)
    {
        if (callee.Declaration is not FunctionKoto function || (!function.IsRequirement && !function.IsVirtual))
        {
            return null;
        }

        // SPEC 8.7: every implementation of a closed Contract reads only its boundaries and the length and has no other effect.
        return callee.Scope.Owner.BoundSymbol?.LibraryDeclaration is KimiDeclarationId.Position or KimiDeclarationId.PositionRange
            ? true : this.AvailableEffectBounds(function, self, this.EffectPremises(definition), reference: contract);
    }

    // SPEC 8.11.4: the bounds a call through an abstract Callable input may use under the calling definition's premises, those its one
    // Callable signature declares for the receiver (TryCallable, the selection rule of the call); null where the input has several
    // signatures, among which only the call's arguments chose (MIR does not keep that choice).
    internal bool? CallableBounds(BindingSymbol? definition, BoundType input, SemanticsKind receiver)
    {
        var scope = this.EffectPremises(definition);
        return this.TryCallable(input, scope, out var signature, out _) ? this.AvailableCallableEffects(input, signature, receiver, scope) : null;
    }

    // SPEC 8.10: the receiver access of a Callable input's one signature under a definition's premises (TryCallable), which a published
    // row's Callable input must declare (TyCtxt.Published); null where it has several signatures or none.
    internal SemanticsKind? CallableReceiver(BindingSymbol definition, BoundType input)
        => this.TryCallable(input, this.EffectPremises(definition), out _, out var receiver) ? receiver : null;

    /// <summary>Gets whether an old Binding record of a conformance, Callable use or virtual function was made by an effect check: a bound
    /// violation or an effect-limit Unsupported, which the MIR difference ledger compares with A4 (DECISIONS R4-M23).</summary>
    /// <param name="node">The record's node.</param>
    /// <returns>Whether an effect check recorded it.</returns>
    [Transitional("R6", Reason = "The ledger's old-side effect subset; R6 deletes it with the old effect checks.")]
    internal bool IsEffectRecord(Koto node) => this.effectViolations?.ContainsKey(node) == true || this.IsEffectLimit(node);

    // SPEC 8.11.3, virtual-dispatch 6: the premises a definition's effects are judged under (null: a module's top-level statements),
    // an override's inherited implementation premises where present.
    internal BindingScope EffectPremises(BindingSymbol? definition)
    {
        var scope = this.ConstraintScope(this.DefinitionUse(definition));
        return scope.ImplementationPremises ?? scope;
    }

    // The node a definition's premises are read at: its declaration, or for a module's top-level statements (null) the library root,
    // which holds none.
    internal Koto DefinitionUse(BindingSymbol? definition) => definition?.Declaration ?? this.Library.Kotonoha.RootKoto;

    // SPEC 8.11.4: the obligation of a confined bound at a selected use, under the premises of the use's definition: an abstract subject
    // by its Callable premises over the selected signature (a holding premise adds no row); any other A4 judges as a call through it.
    private void AddCallableObligation(List<EffectObligation> rows, in CallableEffectUse use)
    {
        var type = CallableCore(use.Subject)!;
        var definition = this.ConstraintScope(use.Use).Function?.BoundSymbol;
        var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.CallableEffectBound);
        if (!type.IsAbstract)
        {
            var owner = use.Receiver == SemanticsKind.Owner && BoundClosure.Of(type) is { Receiver: not SemanticsKind.Owner };
            rows.Add(new(SourceRef.Of(use.At), DiagnosticCode.UnsatisfiedEffectBound_Kd, requirement, definition, type, use.Receiver, owner));
        }
        else if (!this.AvailableCallableEffects(type, use.Signature, use.Receiver, this.EffectPremises(definition)))
        {
            rows.Add(new(SourceRef.Of(use.At), DiagnosticCode.UnsatisfiedEffectBound_Kd, requirement, null, null, default, Owner: false));
        }
    }
}
