// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    [Transitional("R7", Reason = "The legacy comparison composition's leaves (A-12, else R7) and TryResolveFormattingCallback's (H11).")]
    private readonly Dictionary<(BindingSymbol Site, BoundType Type, KimiDeclarationId Contract, string? Name), (ulong Version, CallPlan Call)> requirementCalls = new();
    private readonly Dictionary<BoundType, CallPlan> destructionCalls = new(ReferenceEqualityComparer.Instance);
    private BindingSymbol? builtinFormat;
    private BindingSymbol? builtinEquals;
    private BindingSymbol? builtinCompare;

    internal static bool HasFormattingCallback(CallPlan call)
        => call.Target.CompilerFunction is CompilerFunctionKind.TextWriter or CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat;

    // The finalized callback is consumed by effect checking.
    // Text.writer retains its reserve callback; creating the adapter does not invoke it.
    internal bool TryResolveFormattingCallback(CallPlan site, out CallPlan? implementation)
    {
        implementation = null;
        if (!HasFormattingCallback(site) || site.TypeArguments.Length != (site.Target.CompilerFunction == CompilerFunctionKind.TextTryFormat ? 2 : 1) ||
            site.TypeArguments[0] is not { } self)
        {
            return false;
        }

        var writer = site.Target.CompilerFunction == CompilerFunctionKind.TextWriter;
        if (writer ? self.Symbol?.LibraryDeclaration is KimiDeclarationId.FixedBuffer or KimiDeclarationId.HeapBuffer : FormattingTypes.IsBuiltin(self))
        {
            return true;
        }

        implementation = this.RequirementImplementation(site, self, writer ? KimiDeclarationId.BufferWriter : KimiDeclarationId.Utf8Format);
        return implementation is not null;
    }

    internal CallPlan? DestructionCall(BoundType type)
    {
        if (AdtDef.Destructor(type)?.BoundSymbol is not { } destructor)
        {
            return null;
        }

        if (!this.destructionCalls.TryGetValue(type, out var call))
        {
            this.destructionCalls.Add(type, call = new());
        }

        call.Set(destructor, BoundType.Unit, null, [], [], declaringType: type);
        return call;
    }

    // Compiler-created calls use the same verified witness and storage substitution as source calls.
    // Their input Origins remain the implementation's external inputs; no borrowed value is captured.
    [Transitional("R6", Reason = "H11 deletes it with TryResolveFormattingCallback.")]
    internal CallPlan? RequirementImplementation(CallPlan site, BoundType self, KimiDeclarationId identity, string? name = null)
        => this.RequirementImplementation(site.Target, self, identity, name);

    [Transitional("R7", Reason = "The legacy comparison composition's leaves (A-12, else R7) and TryResolveFormattingCallback's (H11).")]
    private CallPlan? RequirementImplementation(BindingSymbol site, BoundType self, KimiDeclarationId identity, string? name = null)
    {
        var key = (site, self, identity, name);
        if (this.requirementCalls.TryGetValue(key, out var cached) && cached.Version == this.storageVersion)
        {
            return cached.Call;
        }

        if (this.compilation.Library.GetSymbol(identity) is not { } contract ||
            this.ResolveConformance(self, contract, site.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true } ||
            (name is null ? path.Witnesses.Count == 1 ? path.Witnesses[0] : null : this.FindRequirementWitness(path, contract, name)) is not { Implementation: { Declaration: FunctionKoto function } implementation, Function.BasePath: null } witness ||
            this.StoredType(witness.Function!.DeclaringType, self) is not { } declaring)
        {
            return null;
        }

        var inputCount = InputOriginCount(function);
        var originCount = implementation.Schema?.Origins.Count ?? 0;
        var scratch = this.originScratch.Rent(inputCount + originCount);
        try
        {
            var inputs = scratch.AsSpan(0, inputCount);
            var origins = scratch.AsSpan(inputCount, originCount);
            for (var i = 0; i < inputs.Length; i++)
            {
                inputs[i] = i < function.Parameters.Count ? this.OriginAtom(function, OriginKind.Input, i) : implementation.AggregateInputOrigins![i - function.Parameters.Count];
            }

            for (var i = 0; i < origins.Length; i++)
            {
                origins[i] = implementation.Schema!.Origins[i].Origin;
            }

            var call = cached.Call ?? new CallPlan();
            call.Set(implementation, implementation.Type!, null, [], [], declaringType: declaring, origins: origins, inputOrigins: inputs);
            if (this.InstantiateStorageType(implementation.Type!, call) is not { } result)
            {
                return null;
            }

            call.Set(implementation, result, null, [], [], declaringType: declaring, origins: origins, inputOrigins: inputs);
            this.requirementCalls[key] = (this.storageVersion, call);
            return call;
        }
        finally
        {
            this.originScratch.Return(scratch, clearArray: true);
        }
    }

    private BoundWitness? FindRequirementWitness(BoundConformancePath path, BindingSymbol contract, string name)
        => contract.Contract is { } shape && shape.MembersByName.TryGetValue(name, out var members) && members.Count == 1
            ? path.WitnessMap.GetValueOrDefault(members[0]) : null;

    // SPEC 22.1: the declaration a selected call executes. A linked Kimi constructor runs its validated internal implementation,
    // which has the same parameters and premise; null when the link is not validated. Otherwise the compiler requirement target.
    private BindingSymbol? ExecutedTarget(BindingSymbol selected, BoundType? self)
        => selected.LibraryDeclaration is { } id && KimiLibraryCatalog.ImplementationOf(id) is not null
            ? this.Library.ConstructorImplementation(selected)
            : this.CompilerRequirementTarget(selected, self);

    private BindingSymbol CompilerRequirementTarget(BindingSymbol selected, BoundType? self)
    {
        if (self is not null && ArithmeticContracts.Identity(selected.Scope.Owner.BoundSymbol) is { } arithmetic && ArithmeticContracts.Supports(self, arithmetic))
        {
            return Prepared(this.builtinArithmetic[ArithmeticSlot(arithmetic)]);
        }

        if (self is not null && selected.Scope.Owner.BoundSymbol is { LibraryDeclaration: KimiDeclarationId.Equatable or KimiDeclarationId.Comparable } contract &&
            ComparisonKindOf(self, contract.LibraryDeclaration, operators: false) is ComparisonKind.Composite or ComparisonKind.Builtin)
        {
            return Prepared(contract.LibraryDeclaration == KimiDeclarationId.Equatable ? this.builtinEquals : this.builtinCompare);
        }

        return self is not null && FormattingTypes.IsBuiltin(self) && selected.Scope.Owner.BoundSymbol?.LibraryDeclaration == KimiDeclarationId.Utf8Format
            ? Prepared(this.builtinFormat) : selected;

        // A built-in target exists from the first pass's PrepareBuiltinTargets on; no requirement is resolved before it.
        static BindingSymbol Prepared(BindingSymbol? target)
            => target ?? throw new InvalidOperationException("A built-in target is read before PrepareBuiltinTargets.");
    }

    // F fact 11a: a built-in target is its Contract's one own requirement executed by the compiler, with that requirement's name,
    // declaration, result Type and receiver slot, taken once per pass after headers, so resolving a requirement writes nothing.
    private void PrepareBuiltinTargets()
    {
        Prepare(ref this.builtinEquals, KimiDeclarationId.Equatable, CompilerFunctionKind.BuiltinEquals);
        Prepare(ref this.builtinCompare, KimiDeclarationId.Comparable, CompilerFunctionKind.BuiltinCompare);
        Prepare(ref this.builtinFormat, KimiDeclarationId.Utf8Format, CompilerFunctionKind.BuiltinFormat);
        for (var id = KimiDeclarationId.Addable; id <= KimiDeclarationId.Negatable; id++)
        {
            if (!ArithmeticContracts.IsLeft(id))
            {
                Prepare(ref this.builtinArithmetic[ArithmeticSlot(id)], id, CompilerFunctionKind.BuiltinArithmetic);
            }
        }

        void Prepare(ref BindingSymbol? target, KimiDeclarationId id, CompilerFunctionKind kind)
        {
            if (this.Library.GetSymbol(id)!.Contract!.OwnRequirement() is { } requirement)
            {
                target ??= new(requirement.Name, requirement.Kind, requirement.Declaration, requirement.Scope) { CompilerFunction = kind };
                target.Type = requirement.Type;
                target.ReceiverIndex = requirement.ReceiverIndex;
            }
        }
    }
}
