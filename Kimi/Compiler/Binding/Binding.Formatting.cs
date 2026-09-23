// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private BindingSymbol? builtinFormat;
    private BindingSymbol? builtinEquals;
    private BindingSymbol? builtinCompare;

    // Compiler-created calls use the same verified witness and storage substitution as source calls.
    // Their input Origins remain the implementation's external inputs; no borrowed value is captured.
    internal BoundCall? FormattingImplementation(BoundCall site, BoundType self, KimiDeclarationId identity)
    {
        if (this.compilation.Library.GetSymbol(identity) is not { } contract ||
            this.ResolveConformance(self, contract, site.Target.Declaration, out var path) != ConstraintProof.Proven ||
            path is not { IsVerified: true, Witnesses.Count: 1 } ||
            path.Witnesses[0] is not { Implementation: { Declaration: FunctionKoto function } implementation, Function.BasePath: null } witness ||
            this.StoredType(witness.Function!.DeclaringType, self) is not { } declaring)
        {
            return null;
        }

        var inputs = new BoundOrigin[InputOriginCount(function)];
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i] = i < function.Parameters.Count ? this.OriginAtom(function, OriginKind.Input, i) : implementation.AggregateInputOrigins![i - function.Parameters.Count];
        }

        var origins = new BoundOrigin[implementation.Schema?.Origins.Count ?? 0];
        for (var i = 0; i < origins.Length; i++)
        {
            origins[i] = implementation.Schema!.Origins[i].Origin;
        }

        var call = new BoundCall();
        call.Set(implementation, implementation.Type!, null, [], [], declaringType: declaring, origins: origins, inputOrigins: inputs);
        if (this.InstantiateStorageType(implementation.Type!, call) is not { } result)
        {
            return null;
        }

        call.Set(implementation, result, null, [], [], declaringType: declaring, origins: origins, inputOrigins: inputs);
        return call;
    }

    private BindingSymbol FormatTarget(BindingSymbol selected, BoundType? self)
    {
        if (self is not null && selected.Scope.Owner.BoundSymbol is { LibraryDeclaration: KimiDeclarationId.Equatable or KimiDeclarationId.Comparable } contract &&
            ComparisonTypes.IsBuiltin(self, contract.LibraryDeclaration))
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
