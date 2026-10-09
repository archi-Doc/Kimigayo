// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, BindingSymbol> specialReceivers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> inferredConstructorTargets = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Koto, BoundType>? constructorAbsences;
    private ulong storageVersion;

    internal BindingSymbol? SpecialReceiver(FunctionKoto function) => this.specialReceivers.GetValueOrDefault(function);

    internal bool PrepareTypeStorage(BoundType type) => this.PrepareInstantiatedStorage(type, 0);

    internal BoundType? StoredBase(BoundType type)
        => StructStorage.Declaration(type) is { Bases.Count: 1 } declaration ? this.StoredType(declaration.Bases[0], type) : null;

    internal BoundType? InstantiateStorageType(BoundType type, BoundCall call)
    {
        var result = this.MemberType(type, call.DeclaringType) is { } member &&
            this.SubstituteType(member, call.Target.Declaration, call.TypeArguments, call.LengthArguments) is { } substituted
            ? this.SubstituteStoredOrigins(substituted, call.Target.Declaration is FunctionKoto { Accessor: { } accessor } ? accessor.Binder : call.Target.Declaration, call.Origins, call.InputOrigins) : null;
        if (result is not null)
        {
            result = this.ContractType(result, this.ConstraintScope(call.Target.Declaration));
        }

        result = result is null ? null : this.CloseClosureTypes(result, call);
        return result is not null && this.PrepareInstantiatedStorage(result, 0) ? result : null;
    }

    private static bool IsSpecialField(Koto node, out FunctionKoto function)
    {
        if (node is MemberAccessKoto { Left: IdentifierNameKoto { BoundSymbol: { Name: "self", Declaration: FunctionKoto receiver } } } &&
            (receiver.IsConstructor || receiver.IsDestructor))
        {
            function = receiver;
            return true;
        }

        function = null!;
        return false;
    }

    // Complete physical field descriptions from already-bound declarations. This
    // substitutes storage metadata only; it never rebinds or selects a body.
    private bool PrepareInstantiatedStorage(BoundType type, int depth)
    {
        if (depth > 64)
        {
            return false;
        }

        if (StructStorage.IsStruct(type))
        {
            if (type.StoredFields is not null && type.StorageVersion == this.storageVersion)
            {
                return true;
            }

            var count = StructStorage.Count(type);
            if (type.StoredFields?.Length != count)
            {
                type.StoredFields = new BoundType[count];
            }

            type.StorageVersion = this.storageVersion;
            type.StoredBase = this.StoredBase(type);
            if (type.StoredBase is { } parent && !this.PrepareInstantiatedStorage(parent, depth + 1))
            {
                type.StoredFields = null;
                return false;
            }

            for (var i = 0; i < type.StoredFields.Length; i++)
            {
                var field = this.StoredType(StructStorage.Field(type, i), type);
                if (field is null || !this.PrepareInstantiatedStorage(field, depth + 1))
                {
                    type.StoredFields = null;
                    return false;
                }

                type.StoredFields[i] = field;
            }
        }
        else if (Compiler.EnumStorage.IsEnum(type))
        {
            if (type.StoredCases is not null && type.StorageVersion == this.storageVersion)
            {
                return true;
            }

            type.StorageVersion = this.storageVersion;
            if (!this.PrepareEnumCases(type))
            {
                // A stamped version must never publish partially substituted cases.
                type.StoredCases = null;
                return false;
            }

            foreach (var payload in type.StoredCases!)
            {
                if (!this.PrepareInstantiatedStorage(payload, depth + 1))
                {
                    type.StoredCases = null;
                    return false;
                }
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (!this.PrepareInstantiatedStorage(type.Components[i], depth + 1))
            {
                return false;
            }
        }

        return true;
    }

    private void IndexSpecialReceiver(FunctionKoto function, BindingScope scope)
    {
        if (!function.IsConstructor && !function.IsDestructor)
        {
            return;
        }

        if (!this.specialReceivers.TryGetValue(function, out var receiver))
        {
            this.specialReceivers.Add(function, receiver = new("self", BindingSymbolKind.Parameter, function, scope));
        }

        receiver.Scope = scope;
        receiver.Type = scope.Parent?.Owner.BoundSymbol?.Type;
        if (!scope.Values.TryAdd("self", receiver))
        {
            this.Fail(function, BindingFailure.Duplicate);
        }
    }

    private BindingSymbol? ConstructorMember(MemberAccessKoto member, BindingScope scope)
    {
        if (member.Parent is not InvocationKoto invocation || !ReferenceEquals(invocation.Method, member))
        {
            this.Fail(member, BindingFailure.NotCallable);
            return null;
        }

        var qualifier = this.TypeName(member.Left, scope, false, arity: -1);
        if (qualifier is null)
        {
            // A qualifier that names no Type is reported by the call as missing or inaccessible (BindCall).
            return null;
        }

        var type = this.ConstructorQualifierType(member.Left, qualifier, scope);
        if (type is not { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto declaration } ||
            !this.scopes[declaration].Values.TryGetValue("init", out var constructor))
        {
            this.FailConstructorAbsence(member, type);
            return null;
        }

        member.Right.BindingState = BindingState.Resolved;
        member.Right.BoundSymbol = constructor;
        member.BoundSymbol = constructor;
        return constructor;
    }

    private BoundType? ConstructorQualifierType(Koto syntax, BindingSymbol symbol, BindingScope scope)
    {
        var unwrapped = UnwrapTypeSyntax(syntax);
        while (unwrapped is ParenthesizedTypeKoto grouped)
        {
            unwrapped = UnwrapTypeSyntax(grouped.Type);
        }

        if (unwrapped is SyntaxFormKoto { Akind: KotoKind.RootName, Operands.Length: 1 } root)
        {
            unwrapped = UnwrapTypeSyntax(root.Operands[0]);
        }

        if (unwrapped is GenericsKoto || TypeSpelling(unwrapped) == "Self" ||
            symbol.Declaration is not StructKoto { GenericParameterNodes.Count: > 0 } declaration)
        {
            return this.EnumQualifierType(syntax, symbol, scope, null);
        }

        var pattern = this.SelfType(symbol);
        var type = this.BindContainerReference(syntax, symbol, scope, this.TypeContext(syntax, scope), ((BoundType[])pattern.Components).AsSpan(0, declaration.GenericParameterNodes.Count));
        if (type is not null)
        {
            this.inferredConstructorTargets.Add(syntax);
        }

        syntax.BoundSymbol = symbol;
        return Complete(syntax, type);
    }

    private BoundType? BindBaseConstructor(SyntaxFormKoto node, BindingScope scope)
    {
        if (node.Parent is InvocationKoto { Parent: FunctionKoto { IsConstructor: true } constructor } call &&
            ReferenceEquals(call.Method, node) && ReferenceEquals(constructor.BaseInitializer, call))
        {
            var parent = this.StoredBase(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!));
            if (parent?.Symbol?.Declaration is StructKoto declaration && this.scopes[declaration].Values.TryGetValue("init", out var initializer))
            {
                return this.BindReference(node, initializer, scope);
            }

            if (parent is not null)
            {
                return this.FailConstructorAbsence(node, parent);
            }
        }

        return this.Fail(node, BindingFailure.InvalidTypeFormation);
    }

    // SPEC 6.2.3.6, 22.1: a Type without constructors is a located absence at its construction or base call. A derived structure that
    // admits a synthesized constructor waits for the omitted base selection, and a Type parameter under an identity premise for its
    // substitution; both remain unsupported, as does a qualifier that did not bind.
    private BoundType? FailConstructorAbsence(Koto node, BoundType? type)
    {
        if (type is null || (type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto structure } &&
            structure.ConstructorAvailability is not (ConstructorAvailability.MissingInitializer or ConstructorAvailability.CompilerManaged)) ||
            (type.Kind == BoundTypeKind.Parameter && HasParameterIdentity(this.ConstraintScope(node))))
        {
            return this.Fail(node, BindingFailure.Unsupported);
        }

        return this.FailExplained(ref this.constructorAbsences, node, BindingFailure.MissingName, type);
    }

    private void ReportConstructorAbsence(Koto node, BoundType type, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var name = DiagnosticTypeName(type);
        if (type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto { ConstructorAvailability: ConstructorAvailability.MissingInitializer, UninitializedField: { } field } })
        {
            node.Report(requirement, code, note: $"{name} has no constructor: its Field {field.NameKoto.IdentifierName} has no initializer, so none is synthesized (SPEC 6.2.3.6)", related: [("declaration", field.NameKoto, "no initializer")]);
        }
        else if (type.IsWrappingInteger || type.Symbol?.Declaration is StructKoto { ConstructorAvailability: ConstructorAvailability.CompilerManaged })
        {
            node.Report(requirement, code, note: $"{name} is a Kimi Type whose representation the compiler manages; it has only the constructors its declaration declares (SPEC 22.1)", advice: type.Symbol?.LibraryDeclaration == KimiDeclarationId.Dictionary ? "Write [:] for an empty Dictionary" : null);
        }
        else
        {
            var subject = type.Kind == BoundTypeKind.Parameter ? "The Type parameter " + name : name;
            node.Report(requirement, code, note: $"{subject} declares no constructor; only a structure declares constructors (SPEC 6.2.3)");
        }
    }

    private BoundType? ConstructorType(InvocationKoto call, FunctionKoto function, BindingScope scope, ReadOnlySpan<int> mapping)
    {
        var owner = (StructKoto)function.BoundSymbol!.Scope.Owner;
        var type = call.Parent is FunctionKoto { IsConstructor: true } constructor && ReferenceEquals(constructor.BaseInitializer, call)
            ? this.StoredBase(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!))!
            : ((MemberAccessKoto)call.Method).Left.BoundType!;
        var count = owner.BoundSymbol!.Schema!.Origins.Count;
        if (count == 0)
        {
            return type;
        }

        var origins = this.originScratch.Rent(count);
        Array.Clear(origins, 0, count);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            origins[i] = type.OriginArguments[i];
        }

        try
        {
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var slot = mapping[i];
                if (function.Parameters[slot].Type.BoundType is { } pattern && call.ArgumentNodes[i].BoundType is { } actual &&
                    this.AdaptInput(call.ArgumentNodes[i], pattern, actual, scope, null, null, out var adapted, out _, out _))
                {
                    this.MatchInputOrigins(this.StoredType(pattern, type)!, adapted, owner, origins, []);
                }
            }

            for (var i = 0; i < count; i++)
            {
                if (origins[i] is null)
                {
                    return null;
                }
            }

            return this.WithOrigins(type, null, origins.AsSpan(0, count));
        }
        finally
        {
            this.originScratch.Return(origins, clearArray: true);
        }
    }
}
