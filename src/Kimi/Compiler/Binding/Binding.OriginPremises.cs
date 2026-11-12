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

    // PLAN G778: the premise session, open while a judge that makes many requests runs (OpenPremiseSession).
    private readonly OriginPremiseSession premiseSession = new();
    private int originPremiseDepth;

    // The retained premise storage between requests (PLAN G74 U5): the environments, a premise session's included, their largest node
    // and edge capacity, and whether any of them, or the session, still references an Origin, a Type, a declaration or syntax.
    internal (int Environments, int Nodes, int Edges, bool Retains) OriginPremiseStorage
    {
        get
        {
            var (nodes, edges, retains) = (0, 0, this.premiseSession.Environments.Count != 0);
            Measure(this.originPremiseEnvironments);
            Measure(this.premiseSession.Pool);
            return (this.originPremiseEnvironments.Count + this.premiseSession.Pool.Count, nodes, edges, retains);

            void Measure(List<OriginPremiseEnvironment> environments)
            {
                foreach (var environment in environments)
                {
                    var capacity = environment.Closure.Capacity;
                    nodes = Math.Max(nodes, capacity.Nodes);
                    edges = Math.Max(edges, capacity.Edges);
                    retains |= environment.Closure.RetainsOrigins || environment.Nodes.Count != 0 || environment.Types.Count != 0 ||
                        environment.Results.Count != 0 || environment.Resolvers.Count != 0 || environment.Use is not null ||
                        environment.Scope is not null || environment.Comparison.Expected is not null || environment.Shorter is not null;
                }
            }
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

    // Whether ExtractOriginPremises reads `node`, reached from its child `previous`: an associated requirement or application (R4), a
    // signature (R2, R3; a computed or requirement Property's header reads its getter's) or a declaration whose clauses it reads (R1).
    private static bool PremiseFrame(Koto node, Koto? previous)
        => node is FunctionKoto or FunctionTypeKoto or PropertyAccessorKoto or DeclarationContainerKoto || AssociatedHead(node) is OriginApplicationKoto ||
            HeaderGetter(node, previous) is not null;

    // The signature whose premises a request at `node` has: the innermost function (an anonymous one included), Function Type or
    // accessor at or above it; null at the top level.
    private static Koto? PremiseSignature(Koto? node)
    {
        while (node is not null and not (FunctionKoto or FunctionTypeKoto or PropertyAccessorKoto))
        {
            node = node.Parent;
        }

        return node;
    }

    // Whether a request is made in a Callable comparison, whose required signatures' premises it assumes (ExtractOriginPremises).
    private static bool InComparison(in CallableInstance comparison) => comparison.Expected is not null || comparison.Outer is not null;

    // SPEC 15.3.6 (PLAN G74): whether the premises visible at `use` entail `longer outlives shorter`, both resolved at the use.
    // The structural instances of reflexivity, `static`, anchors and meets (I1-I4) answer first. Otherwise one environment
    // collects the catalog's premises (OriginPremiseRule) as the edges of a finite graph over the Origin expressions they name,
    // and a single closure from `shorter` decides the relation; its work is linear in the graph (OriginPremiseClosure).
    private bool EntailsOrigin(BoundOrigin longer, BoundOrigin shorter, Koto use, in CallableInstance comparison, ulong condition)
    {
        if (OriginOutlives(longer, shorter))
        {
            return true;
        }

        if (UnstatedRelation(longer, shorter) is not null)
        {
            return false; // Rulings S4: Refuted before any premise node is built.
        }

        // PLAN G778: in a premise session, a top-level request outside a Callable comparison reads its anchor's environment.
        if (this.premiseSession.Open && this.originPremiseDepth == 0 && !InComparison(comparison))
        {
            return this.EntailsInSession(longer, shorter, use, condition);
        }

        var environment = this.RentPremises(use) ??
            throw new InvalidOperationException($"The premise environment of `{longer} outlives {shorter}` is not ready: its construction depends on itself (PLAN G74).");
        var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
        environment.Comparison = comparison;
        environment.Shorter = shorter;
        bool entailed;
        try
        {
            var closure = environment.Closure;
            this.CollectOriginResolvers(environment);
            var target = closure.Node(shorter, out _);
            var candidate = closure.Node(longer, out _);
            this.ExtractOriginPremises(environment, use);
            entailed = closure.Entails(candidate, target, condition);
            this.OriginProofMetrics?.Construction(closure.EdgeCount, built: true);
            this.OriginProofMetrics?.Closure(closure, this.originPremiseDepth > 1, Stopwatch.GetTimestamp() - start);
        }
        finally
        {
            this.ReturnPremises(environment);
        }

        // SPEC 15.3.6, 15.6.1: in a comparison R5 depends on the request's shorter end (ExtractOriginPremises), so a meet there is judged
        // operand by operand: `(a and b) outlives c` needs `a outlives c` and `b outlives c`, and `x outlives a` proves
        // `x outlives (a and b)`.
        if (entailed || !InComparison(comparison))
        {
            return entailed;
        }

        if (longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (!this.EntailsOrigin(longer.Operands[i], shorter, use, comparison, condition))
                {
                    return false;
                }
            }

            return true;
        }

        for (var i = 0; i < shorter.Operands.Count; i++)
        {
            if (this.EntailsOrigin(longer, shorter.Operands[i], use, comparison, condition))
            {
                return true;
            }
        }

        return false;
    }

    // PLAN G778: the anchor of a request at `use`, below which its environment reads nothing: the child on the use's path of the
    // innermost node the environment reads (a premise frame, a witness bridge among them, a scope stop, a resolving declaration, a local
    // whose R5 the use is within, the root), as a Property's header and a match arm's body read their parent by child; a generated
    // function's block itself, whose statements alone bind in their source's scope (NodeScope); the use itself when it is read or is such
    // a statement. Requests outside a comparison whose uses share an anchor have one environment.
    private Koto PremiseAnchor(Koto use)
    {
        if (GeneratedStatement(use))
        {
            return use;
        }

        Koto? child = null;
        for (var node = use; ; child = node, node = node.Parent!)
        {
            if (node is VariableKoto or { Parent: null } || PremiseFrame(node, child) || this.ScopeStop(node, child) is not null || this.ResolvesAt(node))
            {
                return child is null || GeneratedStatement(child) ? node : child;
            }
        }
    }

    // PLAN G778: opens the premise session for a judge that makes many requests; null when one is open, which the judge then shares. The
    // opener closes what it opened (OriginPremiseSession.Close) in a finally block.
    private OriginPremiseSession? OpenPremiseSession()
    {
        if (this.premiseSession.Open)
        {
            return null;
        }

        this.premiseSession.Open = true;
        return this.premiseSession;
    }

    // PLAN G778: a session request. Its anchor's environment under the excluded result premise (resultPremiseExcluded) is built on first
    // use, its construction nested at depth 1 as a rented one's is, and rebuilt in place after a change of Origin state; each request
    // adds its endpoints and R5 over the nodes they introduce, which only point into them, then closes from its target.
    private bool EntailsInSession(BoundOrigin longer, BoundOrigin shorter, Koto use, ulong condition)
    {
        var session = this.premiseSession;
        var key = (this.PremiseAnchor(use), this.resultPremiseExcluded);
        if (!session.Environments.TryGetValue(key, out var environment))
        {
            if (session.Pool.Count == session.Environments.Count)
            {
                session.Pool.Add(new());
            }

            environment = session.Pool[session.Environments.Count];
            session.Environments.Add(key, environment);
        }

        var start = this.OriginProofMetrics is null ? 0 : Stopwatch.GetTimestamp();
        this.originPremiseDepth++;
        try
        {
            var closure = environment.Closure;
            var built = environment.Use is null || environment.Version != this.originStateVersion;
            if (built)
            {
                environment.Clear();
                environment.Use = key.Item1;
                environment.Version = this.originStateVersion;
                this.CollectOriginResolvers(environment);
                this.ExtractOriginPremises(environment, environment.Use);
            }

            var edges = built ? 0 : closure.EdgeCount;
            var target = closure.Node(shorter, out _);
            var candidate = closure.Node(longer, out _);
            this.AddLocalPlacePremises(environment);
            var entailed = closure.Entails(candidate, target, condition);
            this.OriginProofMetrics?.Construction(closure.EdgeCount - edges, built);
            this.OriginProofMetrics?.Closure(closure, nested: false, Stopwatch.GetTimestamp() - start);
            return entailed;
        }
        finally
        {
            this.originPremiseDepth--;
        }
    }

    // The environment of the next nesting level for a request at `use`, returned with ReturnPremises; null beyond MaxOriginPremiseDepth,
    // where the request's construction depends on itself and the requester fails. A session request's construction starts at depth 1.
    private OriginPremiseEnvironment? RentPremises(Koto use)
    {
        if (this.originPremiseDepth == MaxOriginPremiseDepth)
        {
            return null;
        }

        while (this.originPremiseEnvironments.Count <= this.originPremiseDepth)
        {
            this.originPremiseEnvironments.Add(new());
        }

        var environment = this.originPremiseEnvironments[this.originPremiseDepth];
        this.originPremiseDepth++;
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
            if (this.originDeclarations.TryGetValue(node, out var declaration) && declaration.Resolves)
            {
                environment.Resolvers.Add(declaration);
            }

            if (initializers && this.initializerOrigins.TryGetValue(node, out var inferred) && inferred.Resolves)
            {
                environment.Resolvers.Add(inferred);
            }
        }
    }

    // Whether CollectOriginResolvers may read a declaration at `node`.
    private bool ResolvesAt(Koto node) => this.originDeclarations.ContainsKey(node) || this.initializerOrigins.ContainsKey(node);

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
    // Then the premises of the comparisons the request is made in, and every borrowed complete local Place of the use's own body among
    // the graph's expressions adds its stored Origins (R5), including Places that those Origins introduce, so the closure applies R5 at
    // every expression it reaches.
    private void ExtractOriginPremises(OriginPremiseEnvironment environment, Koto? from)
    {
        Koto? previous = null;
        for (var node = from; node is not null; previous = node, node = node.Parent)
        {
            if (!PremiseFrame(node, previous))
            {
                continue;
            }

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
                this.originDeclarations.TryGetValue(node, out var declaration) && declaration.Completed)
            {
                this.AddDeclarationPremises(environment, declaration.Relations);
                if (environment.Dependence)
                {
                    this.ReadFailedClauses(environment, declaration, null);
                }
            }
        }

        // SPEC 15.3.7, the owner's (a): a request made in a Callable comparison assumes the own well-formedness of the comparison's
        // required signature and of each enclosing comparison's; no other request sees them.
        this.AddRequiredPremises(environment, environment.Comparison.Expected);
        for (var outer = environment.Comparison.Outer; outer is not null; outer = outer.Outer)
        {
            this.AddRequiredPremises(environment, outer.Required);
        }

        // R5, SPEC 15.6.1, 15.6.5: the well-formedness of a borrow of a complete local Place, the body's obligation, which its finite
        // Origin proves without premises. So it serves only a request of the body that declares the local, never its own declaration;
        // a nested body or signature (a nested or anonymous function's, a Function Type's) has only its own premises, the local's
        // Origins being fixed there (SPEC 15.2.2, 15.3.4). A comparison is judged from premises alone, except that a relation requiring
        // an Origin to outlive a Borrow of a local of the body also follows from every Origin stored in that local (SPEC 15.3.7, 15.6.1;
        // PLAN G491; the owner's 「(b)にする」 and 「(a)」): so there R5 starts only from the request's shorter end, through the Places its
        // stored Origins introduce, and EntailsOrigin judges a meet operand by operand.
        var body = PremiseSignature(environment.Use);
        var closure = environment.Closure;
        if (InComparison(environment.Comparison))
        {
            var places = environment.Places;
            if (environment.Shorter is { } shorter && closure.TryNode(shorter, out var start))
            {
                places.Add(start);
            }

            for (var i = 0; i < places.Count; i++)
            {
                if (this.LocalPlaceType(environment, places[i], body) is { } stored)
                {
                    this.AddStoredPremises(environment, stored, places[i], OriginPremiseRule.LocalPlace, false);
                    closure.AddLongerEnds(places[i], OriginPremiseRule.LocalPlace, places);
                }
            }

            return;
        }

        this.AddLocalPlacePremises(environment);
    }

    // R5 outside a comparison at each node from the first one it was not applied to, the nodes its stored Origins introduce included.
    private void AddLocalPlacePremises(OriginPremiseEnvironment environment)
    {
        var body = PremiseSignature(environment.Use);
        for (var closure = environment.Closure; environment.LocalPlaces < closure.NodeCount; environment.LocalPlaces++)
        {
            if (this.LocalPlaceType(environment, environment.LocalPlaces, body) is { } stored)
            {
                this.AddStoredPremises(environment, stored, environment.LocalPlaces, OriginPremiseRule.LocalPlace, false);
            }
        }
    }

    // R5's Place: the stored Type of the local that `node` borrows (a Borrow of a part of it has the local's Origin, PlaceOrigin), when it
    // is a local of the use's own body other than the one being declared, at an owning or borrow layer; otherwise null.
    private BoundType? LocalPlaceType(OriginPremiseEnvironment environment, int node, Koto? body)
        => environment.Closure.Origin(node) is { Kind: OriginKind.Projection, Binder: VariableKoto variable } &&
            this.symbols.TryGetValue(variable, out var local) && local.Type is { Semantics: SemanticsKind.Owner or SemanticsKind.Ref or SemanticsKind.Uniq } stored &&
            !IsWithin(environment.Use, variable) && ReferenceEquals(PremiseSignature(variable), body) ? stored : null;

    // A required signature's own well-formedness: its inputs' and its result's.
    private void AddRequiredPremises(OriginPremiseEnvironment environment, BoundType? required)
    {
        if (required is not { Components: [var inputs, var result] })
        {
            return;
        }

        for (var i = 0; i < inputs.Components.Count; i++)
        {
            this.AddTypePremises(environment, inputs.Components[i], OriginPremiseRule.Input);
        }

        this.AddResultPremises(environment, result);
    }

    // R1: an established enclosing declaration's clauses.
    private void AddDeclarationPremises(OriginPremiseEnvironment environment, ReadOnlySpan<OriginRelation> relations)
    {
        foreach (var relation in relations)
        {
            this.AddPremise(environment, relation.Longer, relation.Shorter, relation.Equality, OriginPremiseRule.Declaration);
        }
    }

    // DECISIONS BU-69: a dependence query reads a contract's failed clauses where the extraction reads that contract (R1, or a Type's
    // premises substituted with the occurrence's Origin `arguments`): one repair per failed clause (DependenceCause).
    private void ReadFailedClauses(OriginPremiseEnvironment environment, OriginDeclaration declaration, BoundOrigin[]? arguments)
    {
        foreach (var (clause, left, right) in declaration.FailedClauses)
        {
            environment.Repairs.Add((clause, Substitute(left), Substitute(right), arguments is null ? declaration.Owner : null, arguments));
        }

        BoundOrigin? Substitute(BoundOrigin? origin)
            => origin is null || arguments is null ? origin : this.SubstituteStoredOrigin(origin, declaration.Owner, arguments);
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

        if (!this.originDeclarations.TryGetValue(associated.Declaration, out var requirement) || !requirement.Completed)
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
    // Function Type adds none. Its inputs' well-formedness is that signature's premise, proven at each call (RequireInputPremises;
    // DECISIONS R4-M137). Its result is judged where it is written, under the enclosing premises and its own inputs (SPEC 15.3.4, 15.6.1;
    // DECISIONS R4-M162). Neither is a premise of the body whose input holds the Function Type.
    private void AddTypePremises(OriginPremiseEnvironment environment, BoundType type, OriginPremiseRule rule)
    {
        if (type.Kind == BoundTypeKind.Function || !environment.Types.Add(type))
        {
            return;
        }

        if (type.Symbol is { } symbol && this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.Completed)
        {
            foreach (var relation in declaration.Relations)
            {
                var a = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                var b = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])type.OriginArguments);
                this.AddPremise(environment, a, b, relation.Equality, rule);
            }

            if (environment.Dependence)
            {
                this.ReadFailedClauses(environment, declaration, (BoundOrigin[])type.OriginArguments);
            }
        }

        if (TryPairLayer(type, out var whole, out var target))
        {
            if (target.StoresOrigin && this.OuterOrigin(type) is { } slot &&
                (this.AdmittedSemantics(whole, this.PremiseScope(environment)) & SemanticsMask.Borrow) != 0 &&
                this.PairSlotCondition(type, this.PremiseScope(environment)) is { } condition)
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
    // Each distinct part is visited once per call (DECISIONS BU-19, PLAN G309): a revisit adds the same edges, which the closure reaches
    // anyway.
    private void AddStoredPremises(OriginPremiseEnvironment environment, BoundType type, int shorter, OriginPremiseRule rule, bool result, ulong condition = 0)
        => this.StoredPremisePart(environment, type, shorter, rule, result, condition, this.visitedParts.Begin(this.storageVersion));

    private void StoredPremisePart(OriginPremiseEnvironment environment, BoundType type, int shorter, OriginPremiseRule rule, bool result, ulong condition, uint query)
    {
        if ((result && type.Kind == BoundTypeKind.Function) || !type.StoresOrigin)
        {
            return;
        }

        if (this.StoredSlot(type, this.PremiseScope(environment), out var slot) is { } origin)
        {
            AddPremiseEdge(environment, this.PremiseNode(environment, origin), shorter, rule, condition | slot);
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            AddPremiseEdge(environment, this.PremiseNode(environment, type.OriginArguments[i]), shorter, rule, condition);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.FirstVisit(type.Components[i], query))
            {
                this.StoredPremisePart(environment, type.Components[i], shorter, rule, result, condition, query);
            }
        }
    }

    // The constraint scope of the environment's use, found once per environment.
    private BindingScope PremiseScope(OriginPremiseEnvironment environment) => environment.Scope ??= this.ConstraintScope(environment.Use);

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

        // The Callable comparison the request is made in (ProvesOriginOutlives), default for any other request.
        internal CallableInstance Comparison { get; set; }

        // The request's shorter end (EntailsOrigin), where R5 starts in a comparison; null for an environment without one (a body's
        // oracle, retention, a dependence query).
        internal BoundOrigin? Shorter { get; set; }

        // In a comparison, R5's Places from the shorter end: its node, then the nodes its stored Origins introduce, each once.
        internal List<int> Places { get; } = new();

        // Outside a comparison, the nodes R5 was applied to, in order (AddLocalPlacePremises).
        internal int LocalPlaces { get; set; }

        // A session environment's Origin state (originStateVersion) when it was built.
        internal int Version { get; set; }

        // Whether this request is a fit's dependence query (DECISIONS BU-69), which reads a repair of each failed clause of a contract
        // it reads, with its visible Origins' owner (R1) or occurrence arguments (a Type's premises).
        internal bool Dependence { get; set; }

        internal List<(OriginRelationKoto Clause, BoundOrigin? Left, BoundOrigin? Right, Koto? Owner, BoundOrigin[]? Arguments)> Repairs { get; } = new();

        internal void Clear()
        {
            this.Closure.Clear();
            this.Nodes.Clear();
            this.Types.Clear();
            this.Results.Clear();
            this.Resolvers.Clear();
            this.Use = null!;
            this.Scope = null;
            this.Comparison = default;
            this.Shorter = null;
            this.Places.Clear();
            this.LocalPlaces = 0;
            this.Dependence = false;
            this.Repairs.Clear();
        }
    }

    // PLAN G778: a premise session, open while a judge that makes many requests runs (the declaration judge, a well-formedness visit, an
    // Origin inference): its environments by anchor and excluded result premise, the storage they reuse, and whether it is open. An
    // environment lives until the session closes, never across passes, and is rebuilt when the Origin state changed; no opener's request
    // writes the other premise inputs (scopes, bound input Types, a Closure's classification).
    private sealed class OriginPremiseSession
    {
        internal Dictionary<(Koto Anchor, FunctionKoto? Excluded), OriginPremiseEnvironment> Environments { get; } = new();

        internal List<OriginPremiseEnvironment> Pool { get; } = new();

        internal bool Open { get; set; }

        internal void Close()
        {
            foreach (var environment in this.Environments.Values)
            {
                environment.Clear();
            }

            this.Environments.Clear();
            this.Open = false;
        }
    }
}
