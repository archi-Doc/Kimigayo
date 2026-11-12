// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    [Transitional("R7", Reason = "The legacy comparison composition's leaves (A-12, else R7).")]
    private readonly Dictionary<(BindingSymbol Site, BoundType Type, KimiDeclarationId Contract, string? Name), (ulong Version, CallPlan Call)> requirementCalls = new();
    private BindingSymbol? builtinFormat;
    private BindingSymbol? builtinEquals;
    private BindingSymbol? builtinCompare;

    // Compiler-created calls use the same verified witness and storage substitution as source calls.
    // Their input Origins remain the implementation's external inputs; no borrowed value is captured.
    [Transitional("R7", Reason = "The legacy comparison composition's leaves (A-12, else R7).")]
    private CallPlan? RequirementImplementation(BindingSymbol site, BoundType self, KimiDeclarationId identity, string? name = null)
    {
        var key = (site, self, identity, name);
        if (this.requirementCalls.TryGetValue(key, out var cached) && cached.Version == this.storageVersion)
        {
            return cached.Call;
        }

        if (this.compilation.Library.GetSymbol(identity) is not { } contract ||
            this.LeafTarget(site, self, contract, name) is not ({ Declaration: FunctionKoto function } implementation, var declaring))
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

    // SPEC 8.11.3, 22.1: what a legacy leaf calls and its declaring instance: a built-in Type's library witness, as the requirement-witness core
    // selects it, the group's function with no declaring Type and no Types (Binding.LibraryWitnesses), which is that witness's instance only
    // for a row of no parameter: String, the one Equatable or Comparable row, so a comparison row with a parameter lands only after this
    // composition goes (A-12's read of the view, else R7-P5); else the conformance's own witness, not an inherited one (DECISIONS R5-M68);
    // null where none resolves.
    [Transitional("R7", Reason = "The legacy comparison composition's leaves (A-12, else R7).")]
    private (BindingSymbol Target, BoundType? Declaring)? LeafTarget(BindingSymbol site, BoundType self, BindingSymbol contract, string? name)
    {
        if (contract.Contract?.OwnRequirement() is { } requirement &&
            this.ResolveRequirementWitness(requirement, self, contract, this.ConstraintScope(site.Declaration)) is
            { Kind: RequirementWitnessKind.LibraryWitness, Target: { } member })
        {
            return (member, null);
        }

        if (this.ResolveConformance(self, contract, site.Declaration, out var path) != ConstraintProof.Proven || path is not { IsVerified: true })
        {
            return null;
        }

        BoundWitness? witness;
        if (name is null)
        {
            witness = path.Witnesses.Count == 1 ? path.Witnesses[0] : null;
        }
        else
        {
            witness = this.FindRequirementWitness(path, contract, name);
        }

        return witness is { Implementation: { } implementation, Function: { BasePath: null } function } &&
            this.StoredType(function.DeclaringType, self) is { } declaring ? (implementation, declaring) : null;
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
        if (self is not null && ArithmeticContractSyntax.Identity(selected.Scope.Owner.BoundSymbol) is { } arithmetic &&
            ArithmeticContracts.Supports(self, arithmetic))
        {
            return Prepared(this.builtinArithmetic[ArithmeticSlot(arithmetic)]);
        }

        if (self is not null && selected.Scope.Owner.BoundSymbol is { LibraryDeclaration: KimiDeclarationId.Equatable or KimiDeclarationId.Comparable } contract &&
            ComparisonKindOf(self, contract.LibraryDeclaration, operators: false) is ComparisonKind.Composite or ComparisonKind.Builtin)
        {
            return Prepared(contract.LibraryDeclaration == KimiDeclarationId.Equatable ? this.builtinEquals : this.builtinCompare);
        }

        // The witness table (WitnessGroup) is the one statement of which built-in Types format through a library body (bool, char and Unit,
        // R8-S2a; f32 and f64, R8-S2c; string and Wrapping<T>, R8-S2d1; the integers up to 64 bits, R8-S2b1; Utf8Slice declares its own).
        // Only i128 and u128 still take builtinFormat, by the IntegerFormat row's width condition (DECISIONS R8-M30, temporary 7, removed
        // at R8-S2b2); S2d2 removes this narrowing with the shim.
        return self is not null && FormattingTypes.IsBuiltin(self) && WitnessGroup(self, KimiDeclarationId.Utf8Format) is null &&
            selected.Scope.Owner.BoundSymbol?.LibraryDeclaration == KimiDeclarationId.Utf8Format
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
