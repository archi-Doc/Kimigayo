// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly QualificationFailureRows qualificationFailures;
    private readonly QualificationStopRows qualificationStops;

    internal enum QualificationCause : byte
    {
        Receiver,
        Inherited,
        Case,
        TypePath, // SPEC 9.6 (F's N1c rulings 38, 41): an instance member reached through a Type path, which supplies no receiver.
    }

    private enum QualificationNamespace : byte
    {
        Type,
        Value,
    }

    // A qualification failure's facts. Cause: the cause the check wrote where it failed (F's N1c ruling 44's N1), TypePath for a Type path's
    // member (SelectMember); none for an unqualified Name's stop, whose cause its member and base clause state (Receiver, Case, Inherited).
    internal readonly record struct QualificationFact(
        BindingScope Container,
        BindingScope Scope,
        BindingSymbol Member,
        string Name,
        LookupRole Role,
        int Arity,
        Koto? At = null,
        Koto? BaseClause = null,
        QualificationCause? Cause = null)
    {
        internal bool Type => !InValueNamespace(this.Role);
    }

    private static bool IsInstanceMember(BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 || (symbol.Kind == BindingSymbolKind.Property && symbol.Scope.Owner is StructKoto or ContractKoto or EnumKoto);

    // SPEC 9.5, 18.2.1 (PLAN G632; R7-N1 c2): what an unqualified Name never selects at any stage, opened members included: a Case or a
    // member that needs a receiver (an inherited declaration is InheritedStop's).
    private static bool NeedsQualification(BindingSymbol symbol) => symbol.EnumCase is not null || IsInstanceMember(symbol);

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

    // A candidate qualified by its Type (no receiver): the repair's `Self.` or `Container.` (ReportQualification).
    private bool HasReceiverlessCandidate(BindingSymbol head, BindingScope scope, LookupRole role)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (!IsInstanceMember(candidate) && HasLookupRole(candidate, role) && this.Accessible(candidate, scope))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 9.5: a candidate an unqualified value Name may select at a Container or import stage: 9.5's stop's one predicate, read by
    // ValueStage and, through AddImport's Selectable, ImportStageEnd (the repair's question is HasReceiverlessCandidate).
    private bool HasUnqualifiedCandidate(BindingSymbol head, BindingScope scope)
    {
        for (var candidate = head; candidate is not null; candidate = candidate.Next)
        {
            if (!NeedsQualification(candidate) && this.Accessible(candidate, scope))
            {
                return true;
            }
        }

        return false;
    }

    private void RecordQualificationStop(Koto use, QualificationFact fact)
        => this.qualificationStops[(use, fact.Type)] = fact with { At = use };

    // Namespace exploration is provisional: report only if the actual binding fails, not when another path succeeds.
    private QualificationFact? QualificationStop(Koto node)
    {
        if (this.qualificationStops.TryGetValue((node, false), out var value))
        {
            return value;
        }

        if (this.qualificationStops.TryGetValue((node, true), out var type))
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

    // SPEC 9.5.1, 9.6: the resolver's miss, the one decision of a Name or Type that no lookup selected: a missing one whose lookup
    // stopped at a declaration requiring qualification (QualificationStop) fails as QualificationRequired, a definite failure that
    // carries its stop (`qualificationFailures`); any other miss fails unresolved as given.
    // Transitional, R7-E5 (R7-E2 E2-4b's O-m1 (a)): both keep the slot's Fail, fact-less: a lookup falls back here after a failure it
    // already recorded (Expressions' FailMissing after SelectMember's Access: Member returns null for both outcomes), so a Record would
    // publish a dependent miss beside it; R7-E5's per-problem inputs decide the fallback. QualificationRequired keeps its stop's one store
    // for the repairs (ReportQualification) until then.
    private BoundType? FailUnselected(Koto name, BindingFailure missing)
    {
        if (missing is BindingFailure.MissingName or BindingFailure.MissingType && this.QualificationStop(name) is { } qualification)
        {
            this.qualificationFailures[name] = qualification;
            return this.Fail(name, BindingFailure.QualificationRequired);
        }

        return this.Fail(name, missing, true);
    }

    private QualificationFact? InheritedStop(BindingScope container, BindingScope scope, Koto use, string name, LookupRole role, int arity)
    {
        if (role == LookupRole.TypeSemantics || container.Owner is not StructKoto { BaseClause: { } clause } structure)
        {
            return null;
        }

        if (IsWithin(use, clause))
        {
            return null; // A base clause never searches its own base layers, including its arguments.
        }

        return this.InheritedDeclaration(structure, name, scope, role) is { } member
            ? new(container, scope, member, name, role, arity, BaseClause: clause) : null;
    }

    private BindingSymbol? InheritedDeclaration(StructKoto structure, string name, BindingScope scope, LookupRole role)
    {
        // Declaration names only: no generic argument substitution or conditional proof selects a member.
        foreach (var layer in this.BaseLayers(structure))
        {
            if (layer.Declaration is not StructKoto baseDeclaration || !this.scopes.TryGetValue(baseDeclaration, out var members))
            {
                return null;
            }

            var names = InValueNamespace(role) ? members.Values : members.Types;
            for (var candidate = names.GetValueOrDefault(name); candidate is not null; candidate = candidate.Next)
            {
                if (HasLookupRole(candidate, role) && this.Accessible(candidate, scope, declarationOnly: true))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    // Its repairs locate their edits in the store Core's decision gives (PLAN G1083); a node that records nowhere publishes nothing.
    private void ReportQualification(Koto node, DiagnosticCollection? store, DiagnosticRequirement requirement)
    {
        if (store is null)
        {
            return;
        }

        var fact = this.qualificationFailures[node];
        var container = fact.Container.Owner.BoundSymbol!;
        if (fact.Cause == QualificationCause.TypePath)
        {
            // The TypePath cause's row (SPEC 9.6; F's N1c rulings 41, 44): the member and its declaring Type (the Container), the member's
            // declaration related; its own Note and no Label or repair (its evidence alternative is not the 9.5 stop's), since the use is qualified.
            node.ReportIn(
                store,
                requirement,
                DiagnosticCode.QualificationRequired_Kd,
                fact.Name,
                container.Name,
                note: $"'{fact.Name}' is an instance member of {container.Name} and needs a receiver, which a Type path does not supply",
                at: fact.At,
                evidence: [QualificationCause.TypePath],
                related: [("declaration", fact.Member.Declaration, "instance member")]);
            return;
        }

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
                ReferenceEquals(this.ProbePath(container.Name, fact.Scope, node, LookupRole.Qualifier), container) &&
                (fact.Type || this.ProbePath(container.Name, fact.Scope, node, LookupRole.Value) is null))
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

        foreach (var row in this.compilation.DefaultAliases(fact.At.CodeContext.Kotonoha))
        {
            AddScope(this.DefaultAliasTarget(row).Target);
        }

        var at = fact.At!;
        var nameSpace = fact.Type ? QualificationNamespace.Type : QualificationNamespace.Value;
        node.ReportIn(
            store,
            requirement,
            DiagnosticCode.QualificationRequired_Kd,
            fact.Name,
            container.Name,
            at: at,
            evidence: fact.BaseClause is null ? [nameSpace, fact.Member.EnumCase is null ? QualificationCause.Receiver : QualificationCause.Case, instance] : [nameSpace, QualificationCause.Inherited, instance, fact.Member.Scope.Owner.BoundSymbol!.Name],
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
                if (!IsInstanceMember(member) && HasLookupRole(member, fact.Role) && this.Accessible(member, fact.Scope))
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
                this.AddTypeCandidates(ref candidates, MemberTypes(member.Scope, fact.Name), fact.Scope, fact.Role, fact.Arity);
                selected = !candidates.Ambiguous && candidates.Match is not null;
                if (!selected)
                {
                    return;
                }
            }

            var verified = selected ? RepairConditionSet.Selection : RepairConditionSet.None;
            var required = (selected ? RepairConditionSet.None : RepairConditionSet.Selection) | (fact.Type ? RepairConditionSet.None : RepairConditionSet.UsageLegality);
            (repairs ??= []).Add(new(RepairKind.Qualify, [fact.Name, prefix], [store!.Edit(new(fact.At!.Span.Start, 0), prefix, fact.At.CodeContext.SourceDocument)], verified, required));
        }
    }

    /// <summary>The `qualificationFailures` family's one accessor (DECISIONS BU-220): a pass family, the qualification stop a failed
    /// Name or Type at a node required (FailUnselected), stored with its failure by the recorder (RecordedFact; a present fact always has its
    /// Container, so the column's default is none). Its members keep the names its readers use.</summary>
    private readonly struct QualificationFailureRows(Binding binding)
    {
        // The family's column in a NodeColumns, the store the recorder decides (RecordedFact, SetRecordedFact).
        internal static ref StampedColumn<QualificationFact, PassCounter> Family(ref NodeColumns columns) => ref columns.QualificationFailures;

        internal QualificationFact this[Koto node]
        {
            get => binding.RecordedFact(node, Family) is { Container: not null } fact ? fact : throw new KeyNotFoundException($"`{node}` has no qualification failure in this pass.");
            set => binding.SetRecordedFact(node, Family, value);
        }
    }

    /// <summary>The `qualificationStops` family's one accessor (DECISIONS BU-220): a working-state family, the provisional stop of a
    /// use's lookup in each namespace (RecordQualificationStop), which the resolver's miss reads (QualificationStop) and a published
    /// head drops (PublishPath); a row of two slots, Value and Type, in the use's module's NodeColumns at its SyntaxId, a slot without
    /// its Container absent. A row of an earlier pass reads none, and the library's rows are released at its freeze
    /// (NodeColumns.Freeze). Its writer refuses a frozen library use (HirTables.Of): a program pass resolves no library use, and
    /// WriteSymbol refuses that use's Selected publication alike. Its members keep the names its readers use.</summary>
    private readonly struct QualificationStopRows(Binding binding)
    {
        internal QualificationFact this[(Koto Use, bool Type) key]
        {
            set
            {
                ref var row = ref HirTables.Of(key.Use).Families.QualificationStops.Ref(key.Use.SyntaxId, binding);
                (key.Type ? ref row.Type : ref row.Value) = value;
            }
        }

        internal bool TryGetValue((Koto Use, bool Type) key, out QualificationFact fact)
        {
            var row = key.Use.SyntaxOwner is { } table ? table.Hir.Families.QualificationStops.Get(key.Use.SyntaxId, binding) : default;
            fact = key.Type ? row.Type : row.Value;
            return fact.Container is not null;
        }

        // A use without the stop is not written, so a head's publication carves no page.
        internal void Remove((Koto Use, bool Type) key)
        {
            if (this.TryGetValue(key, out _))
            {
                this[key] = default;
            }
        }
    }
}
