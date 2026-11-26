// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

#pragma warning disable SA1402 // The obligation row belongs to the facts that list it.

namespace Kimi.Compiler;

// The rule an obligation is judged under, which names the effect classes that fail it (A4's one table, MirEffects.Failing).
internal enum EffectRule : byte
{
    Confined, // SPEC 8.11.2: a `confined` bound; an environment or unknown effect fails it.
    StaticFree, // SPEC 22.4.5 (the Tatara plan's decision 45): a platform member; a static access or an unknown effect fails it.
}

// An obligation under Rule that Binding decided and A4 judges (DECISIONS R4-M22, R4-M23), reported as Code at Subject under Requirement
// (DECISIONS R5-M23). Confined: a conformance's witness (SPEC 8.11.3, at the `Self is C` clause; EffectBound), a virtual function under
// its slot's bound (virtual-dispatch 6; VirtualEffectBound) or a selected Callable binding (SPEC 8.11.4, at the argument or Type
// argument; CallableEffectBound). StaticFree: a platform candidate's witness (SPEC 22.4.5, at its declaration; PlatformStatic).
// Definition: the definition whose body owes, or whose premises judge the Callable (null at a module's top-level statements); without a
// Callable, null where the premises already decide a failure. A Callable binding is its subject Type called with Receiver; Owner: its
// owner acquisition also destroys the Closure's remaining environment.
internal readonly record struct EffectObligation(
    SourceRef Subject, DiagnosticCode Code, DiagnosticRequirement Requirement, BindingSymbol? Definition, BoundType? Callable, SemanticsKind Receiver,
    bool Owner, EffectRule Rule);

public sealed partial class Binding
{
    /// <summary>Lists the effect obligations of the latest pass (DECISIONS R4-M22). Confined: each witness of a conformance whose Contract
    /// or an ancestor declares <c>confined</c> for its requirement, each virtual function whose slot declares it, and each selected use of a
    /// Callable clause declaring it whose premises do not prove it; a holding premise adds none. StaticFree (SPEC 22.4.5, the Tatara plan's
    /// decision 45): each witness of every platform candidate whose conformance is verified, from the platform candidates of the latest
    /// <c>CheckStartup</c>, none before it and none in a test build (<c>CheckTestStartup</c> selects no platform).</summary>
    /// <param name="rows">The caller-owned list to append to.</param>
    internal void EffectObligations(List<EffectObligation> rows)
    {
        foreach (var path in this.impls.Paths)
        {
            if (!path.IsVerified || path.Contract.Contract is not { } shape)
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
                if (!dispatched || !this.AvailableEffectBounds(slot!, null, path.Scope, QuerySite.AfterPass))
                {
                    var target = dispatched ? null : witness.Implementation;
                    var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.EffectBound);
                    var code = DiagnosticCode.IncompatibleContractImplementation_Kd;
                    rows.Add(new(SourceRef.Of(path.Use), code, requirement, target, null, default, Owner: false, EffectRule.Confined));
                }
            }
        }

        foreach (var function in this.virtualDeclarationsView)
        {
            var original = function.IsVirtual ? function : this.virtualOverrides.GetValueOrDefault(function).Slot.Original;
            var bounded = original is not null && this.virtualEffectBounds.GetValueOrDefault(original) is not null;

            if (bounded && function.BoundSymbol is { } symbol && function.BindingFailure == BindingFailure.None)
            {
                var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.VirtualEffectBound);
                var code = DiagnosticCode.UnsatisfiedEffectBound_Kd;
                rows.Add(new(SourceRef.Of(function), code, requirement, symbol, null, default, Owner: false, EffectRule.Confined));
            }
        }

        foreach (var use in this.callableEffectUsesView)
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

        // The rule reads no startup record: a hosted candidate and each of several owe it; a failed conformance owes nothing (its record).
        foreach (var (_, path) in this.platformCandidates)
        {
            for (var i = 0; path.IsVerified && PlatformWitness(path, i) is { } witness; i++)
            {
                var requirement = DiagnosticRequirement.Ownership(OwnershipRequirement.PlatformStatic);
                var code = DiagnosticCode.UnsatisfiedEffectBound_Kd;
                rows.Add(new(SourceRef.Of(witness.Declaration), code, requirement, witness, null, default, Owner: false, EffectRule.StaticFree));
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

        // SPEC 8.7.5: every `tryResolve` implementation, the one requirement a closed Contract declares, reads only its boundaries and
        // the length and has no other effect. The `format` they inherit is Utf8Format's and takes its bound from their effect item.
        return callee.Scope.Owner.BoundSymbol?.LibraryDeclaration is KimiDeclarationId.Position or KimiDeclarationId.PositionRange
            ? true : this.AvailableEffectBounds(function, self, this.EffectPremises(definition), QuerySite.AfterPass, reference: contract);
    }

    // SPEC 8.11.4: the bounds a call through an abstract Callable input may use under the calling definition's premises, those of the
    // premises with the call's signature and an equal or stronger receiver: the signature its selection chose (CallPlan.DeclaredSignature,
    // a published row's OneCallable), never chosen again here.
    internal bool CallableBounds(BindingSymbol? definition, BoundType input, BoundType signature, SemanticsKind receiver)
        => this.AvailableCallableEffects(input, signature, receiver, this.EffectPremises(definition));

    // SPEC 8.10: a Callable input's one signature and its receiver access under a definition's premises (TryCallable), which a published
    // row's Callable input must declare (TyCtxt.Published); null where it has several signatures or none.
    internal (BoundType Signature, SemanticsKind Receiver)? OneCallable(BindingSymbol definition, BoundType input)
        => this.TryCallable(input, this.EffectPremises(definition), out var signature, out var receiver) ? (signature, receiver) : null;

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
        var code = DiagnosticCode.UnsatisfiedEffectBound_Kd;
        if (!type.IsAbstract)
        {
            var owner = use.Receiver == SemanticsKind.Owner && BoundClosure.Of(type) is { Receiver: not SemanticsKind.Owner };
            rows.Add(new(SourceRef.Of(use.At), code, requirement, definition, type, use.Receiver, owner, EffectRule.Confined));
        }
        else if (!this.AvailableCallableEffects(type, use.Signature, use.Receiver, this.EffectPremises(definition)))
        {
            rows.Add(new(SourceRef.Of(use.At), code, requirement, null, null, default, Owner: false, EffectRule.Confined));
        }
    }
}
