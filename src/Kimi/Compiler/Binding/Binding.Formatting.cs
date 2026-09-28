// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<(BindingSymbol Site, BoundType Type, KimiDeclarationId Contract, string? Name), (ulong Version, BoundCall Call)> requirementCalls = new();
    private readonly Dictionary<BoundType, BoundCall> destructionCalls = new(ReferenceEqualityComparer.Instance);
    private BindingSymbol? builtinFormat;
    private BindingSymbol? builtinEquals;
    private BindingSymbol? builtinCompare;

    internal static bool HasFormattingCallback(BoundCall call)
        => call.Target.CompilerFunction is CompilerFunctionKind.TextWriter or CompilerFunctionKind.WriterWrite or CompilerFunctionKind.TextToString or CompilerFunctionKind.TextTryFormat;

    // The same finalized callback is consumed by effect checking and generation.
    // Text.writer retains its reserve callback; creating the adapter does not invoke it.
    internal bool TryResolveFormattingCallback(BoundCall site, out BoundCall? implementation)
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

    internal BoundCall? DestructionCall(BoundType type)
    {
        if (StructStorage.Destructor(type)?.BoundSymbol is not { } destructor)
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
    internal BoundCall? RequirementImplementation(BoundCall site, BoundType self, KimiDeclarationId identity, string? name = null)
        => this.RequirementImplementation(site.Target, self, identity, name);

    private BoundCall? RequirementImplementation(BindingSymbol site, BoundType self, KimiDeclarationId identity, string? name = null)
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

            var call = cached.Call ?? new BoundCall();
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

    private BindingSymbol CompilerRequirementTarget(BindingSymbol selected, BoundType? self)
    {
        if (self is not null && selected.Scope.Owner.BoundSymbol is { LibraryDeclaration: KimiDeclarationId.Equatable or KimiDeclarationId.Comparable } contract &&
            (ComparisonTypes.IsBuiltin(self, contract.LibraryDeclaration) || ComparisonTypes.IsComposite(self)))
        {
            ref var cached = ref (contract.LibraryDeclaration == KimiDeclarationId.Equatable ? ref this.builtinEquals : ref this.builtinCompare);
            cached ??= new(selected.Name, selected.Kind, selected.Declaration, selected.Scope)
            {
                CompilerFunction = contract.LibraryDeclaration == KimiDeclarationId.Equatable ? CompilerFunctionKind.BuiltinEquals : CompilerFunctionKind.BuiltinCompare,
            };
            cached.Type = selected.Type;
            cached.ReceiverIndex = selected.ReceiverIndex;
            return cached;
        }

        if (self is null || !FormattingTypes.IsBuiltin(self) ||
            selected.Scope.Owner.BoundSymbol?.LibraryDeclaration != KimiDeclarationId.Utf8Format)
        {
            return selected;
        }

        this.builtinFormat ??= new(selected.Name, selected.Kind, selected.Declaration, selected.Scope)
        {
            CompilerFunction = CompilerFunctionKind.BuiltinFormat,
        };
        this.builtinFormat.Type = selected.Type;
        this.builtinFormat.ReceiverIndex = selected.ReceiverIndex;
        return this.builtinFormat;
    }
}
