// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // Requests nested in an environment's construction, such as a Type normalized while its premises are extracted, take the
    // next environment; a deeper nesting means the construction depends on itself and is never a negative answer.
    private const int MaxOriginPremiseDepth = 16;

    // One reusable premise environment per active proof request (PLAN G74).
    private readonly List<OriginPremiseEnvironment> originPremiseEnvironments = new();
    private int originPremiseDepth;

    // The retained premise storage between requests (PLAN G74 U5): the environments, their largest node and edge capacity, and
    // whether any of them still references an Origin, a Type, a declaration or syntax.
    internal (int Environments, int Nodes, int Edges, bool Retains) OriginPremiseStorage
    {
        get
        {
            var (nodes, edges, retains) = (0, 0, false);
            foreach (var environment in this.originPremiseEnvironments)
            {
                var capacity = environment.Closure.Capacity;
                nodes = Math.Max(nodes, capacity.Nodes);
                edges = Math.Max(edges, capacity.Edges);
                retains |= environment.Closure.RetainsOrigins || environment.Nodes.Count != 0 || environment.Types.Count != 0 || environment.Results.Count != 0 ||
                    environment.Resolvers.Count != 0 || environment.Use is not null || environment.Scope is not null;
            }

            return (this.originPremiseEnvironments.Count, nodes, edges, retains);
        }
    }

    // The one writer of the premise graph's edges, by node: `longer` outlives `shorter` under `rule` in the Semantics cases
    // `condition` (none: every case). The catalog's premises (AddPremise, AddStoredPremises) and retention's carrier and erased-callable
    // relations (Binding.Retention.cs) all write through it.
    private static void AddPremiseEdge(OriginPremiseEnvironment environment, int longer, int shorter, OriginPremiseRule rule, ulong condition = 0)
        => environment.Closure.AddEdge(longer, shorter, rule, condition);

    // SPEC 11.4, 11.5, 15.6.1: the getter whose result a computed or requirement Property's header is, reached from that header; a use
    // in the header has the getter's input premises (R2), which its body assumes too (DECISIONS BU-26).
    private static PropertyAccessorKoto? HeaderGetter(Koto node, Koto? previous)
        => node is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement, TypeKoto: { } header } property &&
            ReferenceEquals(header, previous) && property.BoundSymbol?.Property is not null ? property.GetAccessor(PropertyAccessorKind.Get) : null;

    // SPEC 15.3.6 (PLAN G74): whether the premises visible at `use` entail `longer outlives shorter`, both resolved at the use.
    // The structural instances of reflexivity, `static`, anchors and meets (I1-I4) answer first. Otherwise one environment
    // collects the catalog's premises (OriginPremiseRule) as the edges of a finite graph over the Origin expressions they name,
    // and a single closure from `shorter` decides the relation; its work is linear in the graph (OriginPremiseClosure).
    private bool EntailsOrigin(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition = 0)
    {
        if (OriginOutlives(longer, shorter))
        {
            return true;
        }

        if (UnstatedRelation(longer, shorter) is not null)
        {
            return false; // Rulings S4: Refuted before any premise node is built.
        }

        var environment = this.RentPremises(use) ??
            throw new InvalidOperationException($"The premise environment of `{longer} outlives {shorter}` is not ready: its construction depends on itself (PLAN G74).");
        var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
        try
        {
            var closure = environment.Closure;
            this.CollectOriginResolvers(environment);
            var target = closure.Node(shorter, out _);
            var candidate = closure.Node(longer, out _);
            this.ExtractOriginPremises(environment, use);
            var entailed = closure.Entails(candidate, target, condition);
            this.OriginProofMetrics?.Closure(closure, this.originPremiseDepth > 1, Stopwatch.GetTimestamp() - start);
            return entailed;
        }
        finally
        {
            this.ReturnPremises(environment);
        }
    }

    // The environment of the next nesting level for a request at `use`, returned with ReturnPremises; null beyond MaxOriginPremiseDepth,
    // where the request's construction depends on itself and the requester fails.
    private OriginPremiseEnvironment? RentPremises(Koto use)
    {
        if (this.originPremiseDepth == MaxOriginPremiseDepth)
        {
            return null;
        }

        if (this.originPremiseDepth == this.originPremiseEnvironments.Count)
        {
            this.originPremiseEnvironments.Add(new());
        }

        var environment = this.originPremiseEnvironments[this.originPremiseDepth++];
        environment.Use = use;
        return environment;
    }

    private void ReturnPremises(OriginPremiseEnvironment environment)
    {
        environment.Clear();
        this.originPremiseDepth--;
    }

    // The declarations whose published substitutions resolve an Origin at the use, in the order OriginAtUse applies them.
    private void CollectOriginResolvers(OriginPremiseEnvironment environment)
    {
        var initializers = this.initializerOrigins.Count != 0;
        for (var node = environment.Use; node is not null; node = node.Parent)
        {
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State >= 2)
            {
                environment.Resolvers.Add(declaration);
            }

            if (initializers && this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.State >= 2)
            {
                environment.Resolvers.Add(inferred);
            }
        }
    }

    // A premise endpoint's node: the endpoint resolved at the use (I5 resolves `==` substitutions before proof), once per endpoint.
    private int PremiseNode(OriginPremiseEnvironment environment, BoundOrigin origin)
    {
        if (environment.Nodes.TryGetValue(origin, out var node))
        {
            return node;
        }

        var resolved = origin;
        for (var i = 0; i < environment.Resolvers.Count; i++)
        {
            resolved = this.ResolveOrigin(resolved, environment.Resolvers[i]);
        }

        node = environment.Closure.Node(resolved, out _);
        environment.Nodes.Add(origin, node);
        return node;
    }

    private void AddPremise(OriginPremiseEnvironment environment, BoundOrigin longer, BoundOrigin shorter, bool equality, OriginPremiseRule rule)
    {
        var a = this.PremiseNode(environment, longer);
        var b = this.PremiseNode(environment, shorter);
        AddPremiseEdge(environment, a, b, rule);
        if (equality)
        {
            AddPremiseEdge(environment, b, a, rule);
        }
    }

    // The catalog's context-dependent premises from `from` outward (for a request, its use): associated formation and requirements
    // (R4), the inputs (R2, a Closure's call receiver among them) and result (R3) of each enclosing signature, a computed or
    // requirement Property's header being its getter's result (SPEC 11.4), and the clauses of each established enclosing declaration (R1).
    // Then every borrowed complete local Place among the graph's expressions adds its stored Origins (R5), including Places
    // that those Origins introduce, so the closure applies R5 at every expression it reaches.
    private void ExtractOriginPremises(OriginPremiseEnvironment environment, Koto? from)
    {
        Koto? previous = null;
        for (var node = from; node is not null; previous = node, node = node.Parent)
        {
            var requirement = IsAssociatedRequirement(node);
            if (requirement && AssociatedFormationType(node) is { } formation)
            {
                this.AddTypePremises(environment, formation, OriginPremiseRule.Associated);
            }

            this.AddAssociatedRequirementPremises(environment, node);
            if ((node is FunctionKoto or FunctionTypeKoto or PropertyAccessorKoto ? node : HeaderGetter(node, previous)) is { } signature)
            {
                // A Function Type quantifies well-formed inputs just as a declaration does. Their intrinsic
                // relations are assumptions only while checking that signature, never in the enclosing body.
                for (var i = 0; i < InputCount(signature); i++)
                {
                    if (this.BoundInputType(signature, i) is { } input)
                    {
                        this.AddTypePremises(environment, input, OriginPremiseRule.Input);
                    }
                }

                if (signature is FunctionKoto { IsAnonymous: true, ClosureStorage: { } plan } literal)
                {
                    this.AddReceiverPremises(environment, literal, plan);
                }

                // SPEC 15.3.7: the result Type's intrinsic well-formedness is a premise of the definition (Binding.ResultPremises.cs).
                if (!ReferenceEquals(signature, this.resultPremiseExcluded) &&
                    signature is FunctionKoto { IsAnonymous: false, IsConstructor: false, BoundSymbol.Type: { } result })
                {
                    this.AddResultPremises(environment, result);
                }
            }

            // Only declaration contracts are assumptions. A field or local relation being checked must never prove itself.
            if ((node is FunctionKoto or PropertyAccessorKoto or DeclarationContainerKoto || requirement) &&
                this.originDeclarations.TryGetValue(node, out var declaration) && declaration.State == 3)
            {
                this.AddDeclarationPremises(environment, declaration.Relations);

                // DECISIONS BU-62: a declaration's rows are judged with the clauses its failure cleared, so a row they establish rests on it.
                if (this.judgingDeclarationRows && declaration.RejectedRelations is { } rejected)
                {
                    this.AddDeclarationPremises(environment, rejected);
                }
            }
        }

        var closure = environment.Closure;
        for (var node = 0; node < closure.NodeCount; node++)
        {
            // R5: a borrow of a complete local Place is usable only while every stored dependency is valid; ownership verifies
            // that availability at each use. It is a premise of borrowing the Place, never of the annotation being checked.
            if (closure.Origin(node) is { Kind: OriginKind.Projection, Binder: VariableKoto variable } &&
                this.symbols.TryGetValue(variable, out var local) && local.Type is { Semantics: SemanticsKind.Owner or SemanticsKind.Ref or SemanticsKind.Uniq } stored &&
                !IsWithin(environment.Use, variable))
            {
                this.AddStoredPremises(environment, stored, node, OriginPremiseRule.LocalPlace, false);
            }
        }
    }

    // R1: an established enclosing declaration's clauses.
    private void AddDeclarationPremises(OriginPremiseEnvironment environment, List<OriginRelation> relations)
    {
        foreach (var relation in relations)
        {
            this.AddPremise(environment, relation.Longer, relation.Shorter, relation.Equality, OriginPremiseRule.Declaration);
        }
    }

    // SPEC 8.5.4: an associated application's inherited formation and its requirement's clauses, substituted with its
    // arguments; the requirement's own declaration is not a premise of itself.
    private void AddAssociatedRequirementPremises(OriginPremiseEnvironment environment, Koto node)
    {
        if (AssociatedHead(node) is not OriginApplicationKoto || node.BoundSymbol is not { Kind: BindingSymbolKind.AssociatedType } associated ||
            ReferenceEquals(associated.Declaration, node))
        {
            return;
        }

        if (this.InheritedAssociatedFormation(node) is { } formation)
        {
            this.AddTypePremises(environment, formation, OriginPremiseRule.Associated);
        }

        if (!this.originDeclarations.TryGetValue(associated.Declaration, out var requirement) || requirement.State != 3)
        {
            return;
        }

        var arguments = this.AssociatedParameters(node);
        foreach (var relation in requirement.Relations)
        {
            var a = this.SubstituteStoredOrigin(relation.Longer, associated.Declaration, arguments);
            var b = this.SubstituteStoredOrigin(relation.Shorter, associated.Declaration, arguments);
            this.AddPremise(environment, a, b, relation.Equality, OriginPremiseRule.Associated);
        }
    }

    // SPEC 15.6.1, 15.8.2 (DECISIONS R4-M152, PLAN G270): R2 of a Closure's call receiver `R/Env during ρ`, input 0 of its contract
    // (R4-M148): its body may assume the receiver's Type well formed. Each environment binding's Type is (AddTypePremises), and under a
    // borrowed receiver every Origin stored in one outlives ρ; under `owner/Self` the receiver is the environment by value, with no layer.
    // The bindings are the components EnvironmentType interns, read here because BindConcreteClosure publishes EnvironmentType only
    // after it judges the pending relations, the first judge that needs them. Before classification ρ is pending (DECISIONS R4-M133),
    // so a relation naming it waits and no edge is needed; a Borrow of a binding, `env_i`, resolves to ρ at its use (PremiseNode).
    private void AddReceiverPremises(OriginPremiseEnvironment environment, FunctionKoto literal, BoundClosure closure)
    {
        var receiver = closure.Classified && closure.Receiver != SemanticsKind.Owner
            ? this.PremiseNode(environment, this.OriginAtom(literal, OriginKind.Projection, ClosureValueSlot)) : -1;
        for (var i = 0; i < closure.Captures.Count; i++)
        {
            if (closure.Captures[i].Environment.Type is not { } binding)
            {
                continue;
            }

            this.AddTypePremises(environment, binding, OriginPremiseRule.Input);
            if (receiver >= 0)
            {
                this.AddStoredPremises(environment, binding, receiver, OriginPremiseRule.Input, false);
            }
        }
    }

    // SPEC 15.3.4, 15.3.7, 8.1.1: a well-formed input Type's own clauses substituted with its Origin arguments, and the Origins stored
    // below each borrow or slice layer outliving that layer's Origin. In the admitted borrow cases a pair layer is a borrow of its
    // target within its outer-Origin slot, whose well-formedness makes the target's Origins outlive that slot; in the value cases no
    // Type denotes the slot, so those edges hold only in the borrow cases (SPEC 15.6.5, PairSlotCondition). The stored Origins include
    // the implicit `o` of an original pair layer below a borrow layer (StoresOrigin). Each Type is visited once per environment. A nested
    // Function Type's inputs add none: their well-formedness is that signature's premise, proven at each call (RequireInputPremises), never
    // one of the body whose input holds the Function Type (SPEC 15.3.4, 15.6.1; DECISIONS R4-M137); its result is PLAN G216's open half.
    private void AddTypePremises(OriginPremiseEnvironment environment, BoundType type, OriginPremiseRule rule)
    {
        if (!environment.Types.Add(type))
        {
            return;
        }

        if (type.Kind == BoundTypeKind.Function)
        {
            this.AddTypePremises(environment, type.Components[1], rule);
            return;
        }

        if (type.Symbol is { } symbol && this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.State == 3)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                this.AddPremise(environment, a, b, relation.Equality, rule);
            }
        }

        if (TryPairLayer(type, out var whole, out var target))
        {
            if (target.StoresOrigin && this.OuterOrigin(type) is { } slot &&
                (this.AdmittedSemantics(whole, environment.Scope ??= this.ConstraintScope(environment.Use)) & SemanticsMask.Borrow) != 0 &&
                this.PairSlotCondition(type, environment.Use) is { } condition)
            {
                this.AddStoredPremises(environment, target, this.PremiseNode(environment, slot), rule, false, condition);
            }

            this.AddTypePremises(environment, target, rule);
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if (inner.StoresOrigin && (IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer)
            {
                this.AddStoredPremises(environment, inner, this.PremiseNode(environment, outer), rule, false);
            }

            this.AddTypePremises(environment, inner, rule);
        }
    }

    // SPEC 15.6.1 well-formedness as a premise: every Origin stored below a borrow layer of the result outlives that layer's Origin.
    // A nested Function Type ends the premise: its per-call Origins cannot leave their binder (SPEC 15.3.4), so the well-formedness
    // inside it stays an obligation of the definition.
    private void AddResultPremises(OriginPremiseEnvironment environment, BoundType type)
    {
        if (!type.StoresOrigin || type.Kind == BoundTypeKind.Function || !environment.Results.Add(type))
        {
            return;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            var inner = type.Components[i];
            if ((IsBorrow(type.Semantics) || type.Kind == BoundTypeKind.Slice) && type.Origin is { } outer)
            {
                this.AddStoredPremises(environment, inner, this.PremiseNode(environment, outer), OriginPremiseRule.Result, true);
            }

            this.AddResultPremises(environment, inner);
        }
    }

    // Every Origin stored in `type` outlives the node `shorter`; with `result`, Origins inside a nested Function Type are not stored.
    // `condition` is the Semantics case of the storage itself; a pair layer's own slot, the implicit `o` of an original `s/T` included
    // (SPEC 8.1.1), is stored only in its binder's borrow cases, while its target is stored in every case (SPEC 15.6.5).
    private void AddStoredPremises(OriginPremiseEnvironment environment, BoundType type, int shorter, OriginPremiseRule rule, bool result, ulong condition = 0)
    {
        if ((result && type.Kind == BoundTypeKind.Function) || !type.StoresOrigin)
        {
            return;
        }

        if (this.StoredSlot(type, environment.Use, out var slot) is { } origin)
        {
            AddPremiseEdge(environment, this.PremiseNode(environment, origin), shorter, rule, condition | slot);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            AddPremiseEdge(environment, this.PremiseNode(environment, type.OriginArguments[i]), shorter, rule, condition);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.AddStoredPremises(environment, type.Components[i], shorter, rule, result, condition);
        }
    }

    // The storage of one proof request: its graph and closure, the resolved endpoint of each premise Origin, the Types already
    // visited and the resolving declarations. Clear releases every reference and keeps the capacity.
    private sealed class OriginPremiseEnvironment
    {
        internal OriginPremiseClosure Closure { get; } = new();

        internal Dictionary<BoundOrigin, int> Nodes { get; } = new(ReferenceEqualityComparer.Instance);

        internal HashSet<BoundType> Types { get; } = new(ReferenceEqualityComparer.Instance);

        internal HashSet<BoundType> Results { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<OriginDeclaration> Resolvers { get; } = new();

        internal Koto Use { get; set; } = null!;

        internal BindingScope? Scope { get; set; }

        internal void Clear()
        {
            this.Closure.Clear();
            this.Nodes.Clear();
            this.Types.Clear();
            this.Results.Clear();
            this.Resolvers.Clear();
            this.Use = null!;
            this.Scope = null;
        }
    }
}
