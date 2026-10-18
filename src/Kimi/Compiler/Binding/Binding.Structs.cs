// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, BindingSymbol> specialReceivers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> inferredConstructorTargets = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Koto, ConstructorAbsence>? constructorAbsences;
    private ulong storageVersion;

    internal BindingSymbol? SpecialReceiver(FunctionKoto function) => this.specialReceivers.GetValueOrDefault(function);

    // Whether a Type's storage substitutes completely, nested at most 64 levels below it (PLAN G128's bound).
    internal bool PrepareTypeStorage(BoundType type) => this.StorageHeight(type, 64) >= 0;

    internal BoundType? InstantiateStorageType(BoundType type, CallPlan call) => this.InstantiateStorageType(type, this.types.StorageSubst(call));

    // A declared Type in the instance a substitution makes, which TyCtxt.Apply delegates to until R7 moves it there: its binder's slots, then
    // its stored Origins substituted, normalized in the binder's Constraint scope (a Contract's Self the conforming Type), its closures closed
    // in the substitution, its storage prepared.
    internal BoundType? InstantiateStorageType(BoundType type, Subst subst)
    {
        var binder = subst.Binder;
        var originBinder = binder is FunctionKoto { Accessor: { } accessor } ? accessor.Binder : binder;
        var result = this.MemberType(type, subst.Declaring) is { } member && this.SubstituteType(member, binder, subst.Types, subst.Lengths) is { } substituted
            ? this.SubstituteStoredOrigins(substituted, originBinder, subst.Origins, subst.Inputs) : null;
        if (result is not null)
        {
            result = this.ContractType(result, this.ConstraintScope(binder), subst.Self);
        }

        result = result is null ? null : this.CloseClosureTypes(result, subst);
        return result is not null && this.PrepareTypeStorage(result) ? result : null;
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

    // Whether an instance's storage substitutes completely, nested Types included, within the bound: the levels its walk reaches
    // below a Type (0 for a scalar; 1 + the highest base, Field, Case payload Tuple or Type component), or -1 when a part does not
    // substitute or the height exceeds the budget. A struct or enum keeps its height for the pass once all its parts passed, so the
    // answer depends on the Type, not on the order of queries (DECISIONS BU-11). It reads storage metadata only; it never rebinds or
    // selects a body.
    private int StorageHeight(BoundType type, int budget)
    {
        if (budget < 0)
        {
            return -1;
        }

        var height = 0;
        if (!AdtDef.IsStruct(type) && !AdtDef.IsEnum(type))
        {
            return this.Highest((BoundType[])type.Components, budget - 1, ref height) ? height : -1;
        }

        var instance = this.Instance(type);
        if (instance.Prepared == this.storageVersion)
        {
            return instance.Height <= budget ? instance.Height : -1;
        }

        // Stamped first at height 0, so an instance reached again through its own storage counts 0 and is not walked twice.
        instance.Prepared = this.storageVersion;
        instance.Height = 0;
        if ((AdtDef.IsEnum(type) && instance.Cases is null) || !this.StoredHeight(instance, budget - 1, ref height) ||
            !this.Highest((BoundType[])type.Components, budget - 1, ref height))
        {
            instance.Prepared = 0;
            return -1;
        }

        instance.Height = height;
        return height;
    }

    // Raises height to one above each stored Type of an instance (its base, Fields and Case payload Tuples); false when one is missing
    // or fails (StorageHeight).
    private bool StoredHeight(AdtDef.InstanceStorage instance, int budget, ref int height)
        => (instance.Base is not { } parent || this.Highest([parent], budget, ref height)) && this.Highest(instance.Fields, budget, ref height) &&
            this.Highest(instance.Cases, budget, ref height);

    // Raises height to one above each Type's storage height; false when one is missing or fails (StorageHeight).
    private bool Highest(ReadOnlySpan<BoundType?> types, int budget, ref int height)
    {
        foreach (var type in types)
        {
            var part = type is null ? -1 : this.StorageHeight(type, budget);
            if (part < 0)
            {
                return false;
            }

            height = Math.Max(height, part + 1);
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
            if (this.BaseConstructorGroup(constructor, out var parent) is { } initializer)
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

    // SPEC 6.3.3: the direct base's constructor group seen from a derived constructor, and the base Type it constructs.
    private BindingSymbol? BaseConstructorGroup(FunctionKoto constructor, out BoundType? parent)
    {
        parent = AdtDef.Base(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!));
        return parent?.Symbol?.Declaration is StructKoto declaration && this.scopes[declaration].Values.TryGetValue("init", out var group) ? group : null;
    }

    // SPEC 6.3.6, 22.1: a Type without constructors is a located absence at its construction or base call. A derived structure whose
    // omitted base clause selects no base constructor has none either; an Unknown premise that can change that selection leaves it
    // unproven, and a selection resting on another failure is derived from it. A Type parameter under an identity premise waits for
    // its substitution and remains unsupported, as does a qualifier that did not bind.
    private BoundType? FailConstructorAbsence(Koto node, BoundType? type)
    {
        var structure = type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto declaration } ? declaration : null;
        if (structure is { ConstructorAvailability: ConstructorAvailability.Eligible } && this.implicitConstructorDecisions.TryGetValue(structure, out var decision))
        {
            return decision.Outcome switch
            {
                OmittedBaseOutcome.NoBaseConstructor or OmittedBaseOutcome.NoneApplicable or OmittedBaseOutcome.Ambiguous =>
                    this.FailExplained(ref this.constructorAbsences, node, BindingFailure.MissingName, new ConstructorAbsence(type!, decision)),
                OmittedBaseOutcome.Unproven => this.FailExplained(ref this.constructorAbsences, node, BindingFailure.UnprovenConstraint, new ConstructorAbsence(type!, decision)),
                OmittedBaseOutcome.Dependent when decision.Cause is { } cause => this.CompleteDependent(node, cause),
                _ => this.Fail(node, BindingFailure.Internal),
            };
        }

        if (type is null || structure is { ConstructorAvailability: not (ConstructorAvailability.MissingInitializer or ConstructorAvailability.CompilerManaged) } ||
            (type.Kind == BoundTypeKind.Parameter && HasParameterIdentity(this.ConstraintScope(node))))
        {
            return this.Fail(node, BindingFailure.Unsupported);
        }

        return this.FailExplained(ref this.constructorAbsences, node, BindingFailure.MissingName, new ConstructorAbsence(type, null));
    }

    private void ReportConstructorAbsence(Koto node, ConstructorAbsence absence, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var type = absence.Type;
        var name = DiagnosticTypeName(type);
        if (absence.Decision is { } decision && type.Symbol?.Declaration is StructKoto { Bases: [var clause, ..] })
        {
            // The decision is made once, from the derived declaration, so its Types are the declaration's own.
            var declared = DiagnosticTypeName(this.SelfType(type.Symbol));
            var baseName = clause.BoundType is { } baseType ? DiagnosticTypeName(baseType) : clause.ToString();
            var omitted = declared == name ? "its omitted base clause" : $"the omitted base clause of {declared}";
            if (decision.Outcome == OmittedBaseOutcome.Unproven)
            {
                // SPEC 6.3.6, 8.3.3: the synthesized constructor is decided once, generically, from the derived declaration.
                var note = $"{name} has a synthesized constructor only when {omitted} selects a constructor of {baseName}; an Unknown premise of a candidate can change that selection, and the constructor is decided once for every {declared} (SPEC 6.3.6, 8.3.3)";
                if (decision.Cause is { } premise)
                {
                    node.Report(requirement, code, evidence: [name, "init", premise.ToString()], note: note, related: [("constraint", premise, "unproven premise"), ("declaration", clause, "omitted base clause")]);
                }
                else
                {
                    node.Report(requirement, code, evidence: [name, "init"], note: note, related: [("declaration", clause, "omitted base clause")]);
                }

                return;
            }

            var reason = decision.Outcome switch
            {
                OmittedBaseOutcome.NoBaseConstructor => $"{baseName} has no constructor",
                OmittedBaseOutcome.NoneApplicable => $"no constructor of {baseName} applies without arguments",
                _ => $"more than one constructor of {baseName} applies without arguments",
            };
            node.Report(requirement, code, note: $"{name} has no constructor: {omitted} selects none, since {reason} (SPEC 6.3.6)", related: [("declaration", clause, "omitted base clause")]);
        }
        else if (type is { Semantics: SemanticsKind.Owner, Symbol.Declaration: StructKoto { ConstructorAvailability: ConstructorAvailability.MissingInitializer, UninitializedField: { } field } })
        {
            node.Report(requirement, code, note: $"{name} has no constructor: its Field {field.NameKoto.IdentifierName} has no initializer, so none is synthesized (SPEC 6.3.6)", related: [("declaration", field.NameKoto, "no initializer")]);
        }
        else if (type.IsWrappingInteger || type.Symbol?.Declaration is StructKoto { ConstructorAvailability: ConstructorAvailability.CompilerManaged })
        {
            node.Report(requirement, code, note: $"{name} is a Kimi Type whose representation the compiler manages; it has only the constructors its declaration declares (SPEC 22.1)");
        }
        else
        {
            var subject = type.Kind == BoundTypeKind.Parameter ? "The Type parameter " + name : name;
            node.Report(requirement, code, note: $"{subject} declares no constructor; only a structure declares constructors (SPEC 6.3)");
        }
    }

    // A construction or base call of a Type without constructors: the Type, and for a derived structure its omitted base selection.
    private readonly record struct ConstructorAbsence(BoundType Type, OmittedBaseSelection? Decision);

    private BoundType? ConstructorType(InvocationKoto call, FunctionKoto function, BindingScope scope, ReadOnlySpan<int> mapping)
    {
        var owner = (StructKoto)function.BoundSymbol!.Scope.Owner;
        var type = call.Parent is FunctionKoto { IsConstructor: true } constructor && ReferenceEquals(constructor.BaseInitializer, call)
            ? AdtDef.Base(this.SelfType(constructor.BoundSymbol!.Scope.Owner.BoundSymbol!))!
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
