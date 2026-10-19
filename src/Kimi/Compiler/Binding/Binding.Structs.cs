// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // PLAN G128's implementation limits on storage preparation (DECISIONS BU-12): the height of a Type's storage, and the Types on the
    // storage walk's path, the query's root included, which ends a Type that grows. Refused and Capped are the walk's two failures.
    private const int StorageHeightLimit = 64;
    private const int StoragePathLimit = 256;
    private const int StorageRefused = int.MaxValue;
    private const int StorageCapped = int.MaxValue - 1;

    private readonly Dictionary<FunctionKoto, BindingSymbol> specialReceivers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Koto> inferredConstructorTargets = new(ReferenceEqualityComparer.Instance);

    // The storage walk's marks for the pass, by Type: a finished Type's height (StorageRefused when refused), or an open Type's position
    // on the walk's stack (Open), which every query leaves empty.
    private readonly Dictionary<BoundType, (int Height, int Open)> storageMarks = new(ReferenceEqualityComparer.Instance);
    private readonly List<BoundType> storageStack = new();
    private Dictionary<Koto, ConstructorAbsence>? constructorAbsences;
    private ulong storageVersion;

    internal BindingSymbol? SpecialReceiver(FunctionKoto function) => this.specialReceivers.GetValueOrDefault(function);

    // Whether a Type's storage substitutes completely within PLAN G128's limits (DECISIONS BU-12): its height is at most 64 and no walk
    // from it passes 256 Types. A refusal marks every Type left open, each of which reaches the refusing part; a capped walk unmarks
    // them, since the cap depends on the path. Instance substitution never queries storage, so the walk's stack is empty at entry.
    internal bool PrepareTypeStorage(BoundType type)
    {
        if (this.storageStack.Count != 0)
        {
            throw new InvalidOperationException($"Storage preparation of {type.Name} was queried during another storage walk.");
        }

        var height = this.StorageHeight(type, 1, out _);
        foreach (var open in this.storageStack)
        {
            if (height == StorageRefused)
            {
                this.storageMarks[open] = (StorageRefused, 0);
            }
            else
            {
                this.storageMarks.Remove(open);
            }
        }

        this.storageStack.Clear();
        return height <= StorageHeightLimit;
    }

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

    // The storage height of a Type the walk meets as the path-th Type from the query's root, one strongly connected component at a time
    // (Tarjan; DECISIONS BU-12): an edge between two members of a component counts 0, an edge to a non-member 1 + its height, so every
    // member gets its component's height, the largest exit, when the member that opened the component closes it. The answer depends on
    // the Type alone. An open Type gives 0 and its stack position as low; StorageRefused when a part does not substitute or a height
    // passes 64; StorageCapped past 256 Types on the path. It reads storage metadata only; it never rebinds or selects a body.
    private int StorageHeight(BoundType type, int path, out int low)
    {
        low = int.MaxValue;
        if (this.storageMarks.TryGetValue(type, out var mark))
        {
            low = mark.Open == 0 ? low : mark.Open;
            return mark.Open == 0 ? mark.Height : 0;
        }

        if (path > StoragePathLimit)
        {
            return StorageCapped;
        }

        var position = this.storageStack.Count + 1;
        this.storageStack.Add(type);
        this.storageMarks[type] = (0, position);
        var height = 0;
        var lowest = position;
        var failure = this.StorageParts(type, path + 1, ref height, ref lowest);
        if (failure != 0 || lowest < position)
        {
            low = lowest;
            return failure != 0 ? failure : height;
        }

        for (var i = position - 1; i < this.storageStack.Count; i++)
        {
            this.storageMarks[this.storageStack[i]] = (height, 0);
        }

        this.storageStack.RemoveRange(position - 1, this.storageStack.Count - position + 1);
        return height;
    }

    // Walks a Type's parts in order (a struct's or enum's base, Fields and Case payload Tuples, then its components): 0, or the failure
    // that ends the walk.
    private int StorageParts(BoundType type, int path, ref int height, ref int lowest)
    {
        if (AdtDef.IsStruct(type) || AdtDef.IsEnum(type))
        {
            var instance = this.Instance(type);
            if (AdtDef.IsEnum(type) && instance.Cases is null)
            {
                return StorageRefused;
            }

            var failure = instance.Base is { } parent ? this.StorageEach([parent], path, ref height, ref lowest) : 0;
            failure = failure != 0 ? failure : this.StorageEach(instance.Fields, path, ref height, ref lowest);
            failure = failure != 0 ? failure : this.StorageEach(instance.Cases, path, ref height, ref lowest);
            if (failure != 0)
            {
                return failure;
            }
        }

        return this.StorageEach((BoundType[])type.Components, path, ref height, ref lowest);
    }

    // Raises height over parts: a part that reaches an open Type is a member of the component (no level), any other an exit (1 + its
    // height); lowest takes the lowest open position a part reaches. 0, or the failure that ends the walk.
    private int StorageEach(ReadOnlySpan<BoundType?> parts, int path, ref int height, ref int lowest)
    {
        foreach (var part in parts)
        {
            if (part is null)
            {
                return StorageRefused;
            }

            var value = this.StorageHeight(part, path, out var low);
            if (value > StorageHeightLimit)
            {
                return value;
            }

            height = Math.Max(height, low == int.MaxValue ? value + 1 : value);
            lowest = Math.Min(lowest, low);
            if (height > StorageHeightLimit)
            {
                return StorageRefused;
            }
        }

        return 0;
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
