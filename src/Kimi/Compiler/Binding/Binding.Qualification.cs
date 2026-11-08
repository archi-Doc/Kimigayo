// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private Dictionary<(Koto Use, bool Type), QualificationFact>? qualificationStops;
    private Dictionary<Koto, QualificationFact>? qualificationFailures;

    private enum QualificationNamespace : byte
    {
        Type,
        Value,
    }

    private enum QualificationCause : byte
    {
        Receiver,
        Inherited,
    }

    private readonly record struct QualificationFact(
        BindingScope Container,
        BindingScope Scope,
        BindingSymbol Member,
        string Name,
        bool Type,
        TypeLookupRole Role,
        int Arity,
        Koto? At = null,
        Koto? BaseClause = null);

    private static bool IsInstanceMember(BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 || (symbol.Kind == BindingSymbolKind.Property && symbol.Scope.Owner is StructKoto or ContractKoto or EnumKoto);

    private string? RootQualifier(BindingScope scope, QualificationFact fact)
    {
        List<string>? names = null;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Owner is DeclarationContainerKoto { IsRoot: true })
            {
                var module = current.Owner.CodeContext.Kotonoha;
                if (!ReferenceEquals(module, fact.At!.CodeContext.Kotonoha))
                {
                    string? reference = ReferenceEquals(this.ModuleReference(fact.At, "Kimi")?.Declaration, current.Owner) ? "Kimi" : null;
                    if (reference is null && this.compilation.References(fact.At.CodeContext.Kotonoha) is { } references)
                    {
                        foreach (var entry in references)
                        {
                            if (ReferenceEquals(entry.Value, module) && this.TestReferenceAllowed(fact.At, entry.Key))
                            {
                                reference = entry.Key;
                                break;
                            }
                        }
                    }

                    if (reference is null)
                    {
                        return null;
                    }

                    (names ??= []).Add(reference);
                }

                names?.Reverse();
                return names is null ? "::" : "::" + string.Join('.', names) + ".";
            }

            if (current.Owner is not DeclarationContainerKoto { GenericParameterNodes.Count: 0, OriginNames.Count: 0 } declaration ||
                declaration.BoundSymbol is not { } symbol || !this.Accessible(symbol, fact.Scope))
            {
                return null;
            }

            (names ??= []).Add(declaration.Name);
        }

        return null;
    }

    private bool HasReceiverlessCandidate(BindingSymbol head, BindingScope scope, TypeLookupRole role)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (!IsInstanceMember(candidate) && HasTypeLookupRole(candidate, role) && this.Accessible(candidate, scope))
            {
                return true;
            }
        }

        return false;
    }

    private void RecordQualificationStop(Koto use, bool type, QualificationFact fact)
        => (this.qualificationStops ??= new())[(use, type)] = fact with { At = use };

    // Namespace exploration is provisional: report only if the actual binding fails, not when another path succeeds.
    private QualificationFact? QualificationStop(Koto node)
    {
        if (this.qualificationStops?.TryGetValue((node, false), out var value) == true)
        {
            return value;
        }

        if (this.qualificationStops?.TryGetValue((node, true), out var type) == true)
        {
            return type;
        }

        return node switch
        {
            GenericsKoto generic => this.QualificationStop(generic.Identifier!),
            MemberAccessKoto member => this.QualificationStop(member.Left),
            _ => null,
        };
    }

    private QualificationFact? InheritedStop(BindingScope container, BindingScope scope, Koto use, string name, bool type, TypeLookupRole role, int arity)
    {
        if (role == TypeLookupRole.Semantics || container.Owner is not StructKoto { BaseClause: { } clause } structure)
        {
            return null;
        }

        if (IsWithin(use, clause))
        {
            return null; // A base clause never searches its own base layers, including its arguments.
        }

        return this.InheritedDeclaration(structure, name, scope, type, role) is { } member
            ? new(container, scope, member, name, type, role, arity, BaseClause: clause) : null;
    }

    private BindingSymbol? InheritedDeclaration(StructKoto structure, string name, BindingScope scope, bool type, TypeLookupRole role)
    {
        // Declaration names only: no generic argument substitution or conditional proof selects a member.
        foreach (var layer in this.BaseLayers(structure))
        {
            if (layer.Declaration is not StructKoto baseDeclaration || !this.scopes.TryGetValue(baseDeclaration, out var members))
            {
                return null;
            }

            for (var candidate = (type ? members.Types : members.Values).GetValueOrDefault(name); candidate is not null; candidate = candidate.Next)
            {
                if (!IsGenericParameter(candidate) && HasTypeLookupRole(candidate, role) && this.Accessible(candidate, scope, declarationOnly: true))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private void ReportQualification(Koto node, DiagnosticRequirement requirement)
    {
        var fact = this.qualificationFailures![node];
        var container = fact.Container.Owner.BoundSymbol!;
        var instance = false;
        for (var member = fact.Member; member is not null; member = member.Next)
        {
            instance |= IsInstanceMember(member) && this.Accessible(member, fact.Scope, declarationOnly: true);
        }

        string? qualifier = null;
        string? receiverQualifier = null;
        if (instance)
        {
            for (var scope = fact.Scope; scope is not null && ReferenceEquals(scope.Function, fact.Scope.Function); scope = scope.Parent)
            {
                if (scope.Values.GetValueOrDefault("self")?.Type is { } self && ReferenceEquals(EffectiveCore(self).Symbol, container) &&
                    (fact.Scope.Function is not { IsConstructor: true } constructor || (ReferenceEquals(fact.Member.Scope, fact.Container) && fact.Member.Property is { IsStored: true } && (constructor.Body ?? constructor.ExpressionBody) is { } body && IsWithin(node, body))))
                {
                    receiverQualifier = "self.";
                    break;
                }
            }
        }

        if (this.HasReceiverlessCandidate(fact.Member, fact.Scope, fact.Role))
        {
            for (var scope = fact.Scope; scope is not null; scope = scope.Parent)
            {
                if (scope.Owner is DeclarationContainerKoto and not GroupKoto)
                {
                    if (ReferenceEquals(scope.Owner.BoundSymbol, container))
                    {
                        qualifier = "Self.";
                    }

                    break;
                }
            }

            if (qualifier is null && container.Declaration is DeclarationContainerKoto { GenericParameterNodes.Count: 0, OriginNames.Count: 0 } &&
                ReferenceEquals(this.ProbePath(container.Name, fact.Scope, node, TypeLookupRole.Any), container) &&
                (fact.Type || this.Lookup(container.Name, fact.Scope, node) is null))
            {
                qualifier = container.Name + ".";
            }
        }

        List<DiagnosticRepairFact>? repairs = null;
        Add(receiverQualifier, fact.Member);
        Add(qualifier, fact.Member);
        // Later lexical Containers are diagnostic alternatives only; they never become fallback binding targets.
        for (var scope = fact.Container.Parent; scope is not null; scope = scope.Parent)
        {
            AddScope(scope);
        }

        if (fact.At!.CodeContext.SourceDocument is { } source && this.aliasesByDocument.TryGetValue(source, out var aliases))
        {
            for (var i = 0; i < aliases.Count; i++)
            {
                AddScope(this.AliasTarget(aliases[i].Declaration!));
            }
        }

        foreach (var path in this.compilation.DefaultAliases(fact.At.CodeContext.Kotonoha))
        {
            AddScope(this.DefaultAliasTarget(fact.At, path));
        }

        var at = fact.At!;
        var nameSpace = fact.Type ? QualificationNamespace.Type : QualificationNamespace.Value;
        node.Report(
            requirement,
            DiagnosticCode.QualificationRequired_Kd,
            fact.Name,
            container.Name,
            at: at,
            evidence: fact.BaseClause is null ? [nameSpace, QualificationCause.Receiver, instance] : [nameSpace, QualificationCause.Inherited, instance, fact.Member.Scope.Owner.BoundSymbol!.Name],
            related: fact.BaseClause is null ? [("declaration", fact.Member.Declaration, "declaration requiring qualification")] : [("declaration", fact.Member.Declaration, "inherited declaration"), ("base", fact.BaseClause, "base clause of " + container.Name)],
            repairs: repairs?.ToArray());

        void AddScope(BindingScope? scope)
        {
            if (scope?.Owner is not DeclarationContainerKoto || this.RootQualifier(scope, fact) is not { } path)
            {
                return;
            }

            for (var member = (fact.Type ? scope.Types : scope.Values).GetValueOrDefault(fact.Name); member is not null; member = member.Next)
            {
                if (!IsInstanceMember(member) && !IsGenericParameter(member) && HasTypeLookupRole(member, fact.Role) && this.Accessible(member, fact.Scope))
                {
                    Add(path, member);
                    break;
                }
            }
        }

        void Add(string? prefix, BindingSymbol member)
        {
            if (prefix is null)
            {
                return;
            }

            for (var i = 0; repairs is not null && i < repairs.Count; i++)
            {
                if (Equals(repairs[i].Facts![1], prefix))
                {
                    return;
                }
            }

            var selected = member.Kind == BindingSymbolKind.Property && fact.At?.Parent is not InvocationKoto;
            if (fact.Type)
            {
                var candidates = default(TypeCandidates);
                this.AddTypeCandidates(ref candidates, member.Scope.Types.GetValueOrDefault(fact.Name), fact.Scope, fact.Role, fact.Arity);
                selected = !candidates.Ambiguous && candidates.Match is not null;
                if (!selected)
                {
                    return;
                }
            }

            var verified = selected ? RepairConditionSet.Selection : RepairConditionSet.None;
            var required = (selected ? RepairConditionSet.None : RepairConditionSet.Selection) | (fact.Type ? RepairConditionSet.None : RepairConditionSet.UsageLegality);
            (repairs ??= []).Add(new(RepairKind.Qualify, [fact.Name, prefix], [node.Edit(new(fact.At!.Span.Start, 0), prefix)], verified, required));
        }
    }
}
