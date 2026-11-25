// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

namespace Kimi.Compiler;

// Binding.Contracts' expanding recursion (SPEC 8.3.3; PLAN G563-C, F's ruling G563C-1): a Contract's validity over the Contracts that
// refer to one another, beside BuildContract's refinement cycle.
// - Responsibility: CheckExpandingRecursion decides SPEC 8.3.3's Expanding recursion for the pass's Contracts. Each Contract reference
//   in what `Self is K` supplies to Self or a projection of Self passes each parameter of K to each parameter of the referenced Contract
//   whose argument contains it: flat where the argument is that parameter, expanding otherwise. Every strongly connected set of passes
//   with an expanding pass makes each Contract with a parameter in it erroneous (F's Q3 (a): SPEC's "Among Contracts that refer to one
//   another", linear).
// - Inputs: the pass's Contract shapes (contractShapes: a library or earlier-layer Contract never refers to a later one, so every cycle
//   lies inside one pass); each Contract's supply (ContractSupply, the one enumeration of what `Self is K` supplies, SPEC 8.3.2: its
//   ancestors and its own and inherited clauses, each a Constraint whole). SPEC 8.3.3 counts "each reference ... in what `Self is K`
//   supplies ... (an ancestor conformance or a Constraint)", so every premise about Self or a projection of Self among a supplied
//   Constraint's operands passes, whatever the connective (`and`, `or`, `not`; F's G563C-6), through every Contract reference it holds:
//   a Contract premise's reference and each projection's qualifier in the Types it holds, its subject, its required Type and its
//   Contract reference's arguments (F's G563C-8, G563C-9). The Contract's
//   parameters and each reference's arguments are read in the Contract's view as a proposition's are (NormalizedForm: SPEC's
//   "normalized under K's premises", a completed projection interpreted, 8.2.3; F's Q1 (a) and G563C-3: one interpretation). These
//   forms are Complete here: the check runs after Advance(AssociatedBindings), where every binding they read is admitted (G563C-5),
//   so their Completion is not read.
// - Outputs: one InvalidConstraint_Kd at each erroneous Contract (DECISIONS R4-M115: the 8.3.3 errors use existing codes), after
//   admission point 3, where the arguments' normalization exists (a binding is admitted only from there; F's G563C-5). Every use and
//   refiner reads the failed Contract through the failed-context read (AvailableContractPremise's InvalidDeclarationContext) and rests,
//   and ValidateConformances' publication carries it into the source states (R7-M46). The window: PrepareAssociatedBindings forms its
//   bindings before an expanding Contract fails, which only a program carrying that Contract's own record can show, so no acceptance
//   moves and the failed-context read rests every later read. Each premise that forms a pass of such a set failed in the view of the
//   Contract whose supply holds it (FailPremise), so the adapter's InvalidConstraint row relates the Contract's own clauses among them
//   (FailedPremiseClauses, F's Q2 (a)). A forming ancestor reference is a base, not a clause, and is not related (PLAN G563's open
//   issue: a "reference" role). Nor is the clause of a reference under an `or` or a `not`: its forming premise is an operand, which
//   is no fact of the environment, and FailPremise fails a fact only (the same open issue).
// - Invariants: the pass graph (a node per Contract parameter; a pass with its expanding flag and the premise that forms it) is call
//   scratch in pooled fields, built and emptied in one call (warm 0 B), never published: no IR form, no vocabulary entry. Linear in the
//   supplies' references, each supplied premise's Types' parts (each once per premise), the references' arguments' parts (each once)
//   and the passes; the strongly connected sets by an iterative Tarjan walk with no depth bound.
// - Prohibitions: no limit (SPEC 8.3.3: the error depends on the declarations alone); no read of clause syntax (bound facts only); no
//   proof; no second enumeration of the supply; no record at a Contract on no such set (a refiner or a user of an erroneous Contract
//   reads its failure and rests, SPEC 23.1.2).
public sealed partial class Binding
{
    private readonly Dictionary<BindingSymbol, int> expansionNodes = new(ReferenceEqualityComparer.Instance); // a Contract's first node
    private readonly List<int> expansionOwners = new(); // each node's Contract (its index in contractShapes)
    private readonly List<BoundType> expansionForms = new(); // the running Contract's parameters, formed in its view
    private readonly HashSet<BoundType> expansionParts = new(ReferenceEqualityComparer.Instance); // one argument's parts walked

    // One supplied premise's Types' parts walked. Its one writer is AddQualifierPasses; AddPremisePasses empties it after each premise
    // (and CheckExpandingRecursion on every exit).
    private readonly HashSet<BoundType> expansionTypeParts = new(ReferenceEqualityComparer.Instance);

    private readonly List<(int From, int To, bool Expanding, BoundConstraint? Premise)> expansionPasses = new();
    private readonly List<int> expansionFirst = new(); // each node's first pass (expansionPasses sorted by From), and the count last
    private readonly List<int> expansionOrder = new();
    private readonly List<int> expansionLow = new();
    private readonly List<int> expansionComponent = new(); // each node's strongly connected set, named by its root node
    private readonly List<bool> expansionGrowing = new(); // by root node: the set holds an expanding pass
    private readonly List<int> expansionOpen = new();
    private readonly List<(int Node, int At)> expansionStack = new();

    // SPEC 8.3.3's Expanding recursion over the pass's Contracts (the contract above), once per BindConstraints, after every Contract
    // clause is bound, every Contract view expanded (ExpandContractPremises) and the associated bindings admitted (point 3).
    private void CheckExpandingRecursion()
    {
        try
        {
            for (var c = 0; c < this.contractShapes.Count; c++)
            {
                var symbol = this.contractShapes[c].Symbol;
                if (symbol.Schema is not { GenericSlots.Count: > 0 } schema)
                {
                    continue;
                }

                this.expansionNodes[symbol] = this.expansionOwners.Count;
                for (var i = schema.GenericSlots.Count; i > 0; i--)
                {
                    this.expansionOwners.Add(c);
                }
            }

            if (this.expansionOwners.Count == 0)
            {
                return;
            }

            for (var c = 0; c < this.contractShapes.Count; c++)
            {
                this.AddContractPasses(this.contractShapes[c]);
            }

            this.MarkGrowingSets();
            this.RecordGrowingSets();
        }
        finally
        {
            // Call scratch, emptied on every exit, as RecursiveContracts' search is.
            this.expansionNodes.Clear();
            this.expansionOwners.Clear();
            this.expansionForms.Clear();
            this.expansionParts.Clear();
            this.expansionTypeParts.Clear();
            this.expansionPasses.Clear();
            this.expansionFirst.Clear();
            this.expansionOrder.Clear();
            this.expansionLow.Clear();
            this.expansionComponent.Clear();
            this.expansionGrowing.Clear();
            this.expansionOpen.Clear();
            this.expansionStack.Clear();
        }
    }

    // The passes from a Contract's parameters (its own reference's components, SelfType's, in its view): each ancestor of its supply,
    // and each premise about Self or a projection of Self among a supplied Constraint's operands (AddPremisePasses).
    private void AddContractPasses(BoundContract shape)
    {
        if (shape.Symbol.Schema is not { GenericSlots.Count: > 0 })
        {
            return;
        }

        var parameters = this.SelfType(shape.Symbol).Components;
        var scope = this.scopes[shape.Symbol.Declaration];
        var self = this.ContractSelfType(shape.Symbol);
        var from = this.expansionNodes[shape.Symbol];
        for (var i = 0; i < parameters.Count; i++)
        {
            this.expansionForms.Add(this.NormalizedForm(parameters[i], scope, out _));
        }

        foreach (var (ancestor, declaration, premise, _) in new ContractSupply(this, shape, self, scope))
        {
            if (premise is not null)
            {
                this.AddPremisePasses(premise, self, scope, from);
            }
            else if (ancestor >= 0)
            {
                this.AddReferencePasses(declaration.Symbol, scope, from, null);
            }
        }

        this.expansionForms.Clear();
    }

    // A supplied premise, a Constraint SPEC 8.3.3 counts whole: "each reference ... in what `Self is K` supplies ... (an ancestor
    // conformance or a Constraint)" (F's G563C-6, G563C-8, G563C-9). A premise about Self or a projection of Self (its subject or its
    // required Type, OfSearchSelf) passes through every Contract reference in it: a Contract premise's reference, and each projection's
    // qualifier in the three Types it holds, its subject, its required Type and its Contract reference's Type, whose components are the
    // reference's arguments (AddQualifierPasses: `... is Self.(Tree).Child.(R).W.(HasY<Wrap<A>>).Y`, or the same qualifier inside
    // `Mark<...>`). One naming no Self is a reference input condition, not a supply (SPEC 6.1.4), and passes nothing. Every operand (Left, and Right
    // where present) is walked whatever the connective: `and`, `or` and `not` alike, since the Disjunction and Negation rows decide proof
    // results, not what is supplied.
    private void AddPremisePasses(BoundConstraint premise, BoundType self, BindingScope scope, int from)
    {
        var required = premise.RequiredType;
        if (premise.Subject is { } subject && (OfSearchSelf(subject, self) || (required is not null && OfSearchSelf(required, self))))
        {
            if (premise is { Kind: ConstraintKind.Contract, Contract: { } reference })
            {
                this.AddReferencePasses(reference, scope, from, premise);
            }

            this.AddQualifierPasses(subject, scope, from, premise);
            this.AddQualifierPasses(required, scope, from, premise);
            this.AddQualifierPasses(premise.Contract?.Type, scope, from, premise);
            this.expansionTypeParts.Clear();
        }

        if (premise.Left is { } left)
        {
            this.AddPremisePasses(left, self, scope, from);
        }

        if (premise.Right is { } right)
        {
            this.AddPremisePasses(right, self, scope, from);
        }
    }

    // One supplied reference, formed in the supplying Contract's view: each argument position of the referenced Contract receives the
    // parameters its argument contains. A Contract of no node here (another pass's) lies on no cycle of this pass.
    private void AddReferencePasses(BindingSymbol reference, BindingScope scope, int from, BoundConstraint? premise)
    {
        // The reference's declaration symbol (a bound reference's Type's head, or the declaration's own symbol) keys its nodes. Only a
        // Contract of the pass with parameters has nodes; any other (a library's, one without parameters and maybe without the schema
        // ContractReferenceType's SelfType needs) receives no pass, so the test comes before the reference's Type is formed.
        if (this.expansionNodes.TryGetValue(IsBoundContractReference(reference) ? reference.Type!.Symbol! : reference, out var to))
        {
            this.AddReferencePasses(this.ContractReferenceType(reference), to, scope, from, premise);
        }
    }

    // A reference's Type (ContractReferenceType's, or a projection's qualifier, the same representation: ProjectionContract) to the
    // Contract whose first node is `to`, its arguments normalized in the supplying Contract's view.
    private void AddReferencePasses(BoundType reference, int to, BindingScope scope, int from, BoundConstraint? premise)
    {
        var formed = this.NormalizedForm(reference, scope, out _);
        for (var j = 0; j < formed.Components.Count; j++)
        {
            this.AddArgumentPasses(formed.Components[j], formed.Components[j], from, to + j, premise);
            this.expansionParts.Clear();
        }
    }

    // A supplied premise's Type (none where the premise holds none), each part once (expansionTypeParts: AddReferencePasses empties
    // expansionParts per argument): a projection names its Contract reference (SPEC 8.6.1; ProjectionContract's, its second component),
    // so a projection whose qualifier is a Contract with nodes is a reference the premise holds, and passes as AddReferencePasses'
    // reference does.
    private void AddQualifierPasses(BoundType? part, BindingScope scope, int from, BoundConstraint premise)
    {
        if (part is not { HasAbstractPart: true } || !this.expansionTypeParts.Add(part))
        {
            return;
        }

        if (part is { Kind: BoundTypeKind.AssociatedProjection, Components: [_, { Symbol: { } contract } reference] } &&
            this.expansionNodes.TryGetValue(contract, out var to))
        {
            this.AddReferencePasses(reference, to, scope, from, premise);
        }

        for (var i = 0; i < part.Components.Count; i++)
        {
            this.AddQualifierPasses(part.Components[i], scope, from, premise);
        }
    }

    // An argument's parts, each once (Types are shared), through every component (a projection's subject and its Contract's arguments
    // too: SPEC 8.3.3's "contains"): a part that is a parameter's form passes that parameter to `to`, flat where it is the argument itself.
    private void AddArgumentPasses(BoundType part, BoundType argument, int from, int to, BoundConstraint? premise)
    {
        if (!part.HasAbstractPart || !this.expansionParts.Add(part))
        {
            return;
        }

        for (var i = 0; i < this.expansionForms.Count; i++)
        {
            if (ReferenceEquals(part, this.expansionForms[i]))
            {
                this.expansionPasses.Add((from + i, to, !ReferenceEquals(part, argument), premise));
            }
        }

        for (var i = 0; i < part.Components.Count; i++)
        {
            this.AddArgumentPasses(part.Components[i], argument, from, to, premise);
        }
    }

    // The strongly connected sets of the passes, each set named by its root node, and whether a set holds an expanding pass between two
    // of its nodes (a pass inside a set lies on a cycle).
    private void MarkGrowingSets()
    {
        var nodes = this.expansionOwners.Count;
        this.expansionPasses.Sort(static (x, y) => x.From.CompareTo(y.From));
        for (int n = 0, at = 0; n <= nodes; n++)
        {
            while (at < this.expansionPasses.Count && this.expansionPasses[at].From < n)
            {
                at++;
            }

            this.expansionFirst.Add(at);
            this.expansionOrder.Add(-1);
            this.expansionLow.Add(0);
            this.expansionComponent.Add(-1);
            this.expansionGrowing.Add(false);
        }

        var visits = 0;
        for (var start = 0; start < nodes; start++)
        {
            if (this.expansionOrder[start] < 0)
            {
                this.WalkPasses(start, ref visits);
            }
        }

        foreach (var (from, to, expanding, _) in this.expansionPasses)
        {
            var root = this.expansionComponent[from];
            this.expansionGrowing[root] |= expanding && root == this.expansionComponent[to];
        }
    }

    // An iterative Tarjan walk from `start` (as IdentityClasses.MarkCycles): a node's sets close when its low link is its own order.
    private void WalkPasses(int start, ref int visits)
    {
        this.EnterPassNode(start, ref visits);
        while (this.expansionStack.Count != 0)
        {
            var (node, at) = this.expansionStack[^1];
            if (at < this.expansionFirst[node + 1])
            {
                this.expansionStack[^1] = (node, at + 1);
                var target = this.expansionPasses[at].To;
                if (this.expansionOrder[target] < 0)
                {
                    this.EnterPassNode(target, ref visits);
                }
                else if (this.expansionComponent[target] < 0)
                {
                    this.expansionLow[node] = Math.Min(this.expansionLow[node], this.expansionOrder[target]);
                }

                continue;
            }

            this.expansionStack.RemoveAt(this.expansionStack.Count - 1);

            // A root closes its set: each member is popped off the open stack (the iterator clause pops the member just named) until the
            // root itself, which the open stack holds lowest of the set, is popped last.
            for (var member = -1; this.expansionLow[node] == this.expansionOrder[node] && member != node; this.expansionOpen.RemoveAt(this.expansionOpen.Count - 1))
            {
                member = this.expansionOpen[^1];
                this.expansionComponent[member] = node;
            }

            if (this.expansionStack.Count != 0)
            {
                var up = this.expansionStack[^1].Node;
                this.expansionLow[up] = Math.Min(this.expansionLow[up], this.expansionLow[node]);
            }
        }
    }

    private void EnterPassNode(int node, ref int visits)
    {
        this.expansionOrder[node] = this.expansionLow[node] = visits++;
        this.expansionOpen.Add(node);
        this.expansionStack.Add((node, this.expansionFirst[node]));
    }

    // Each premise forming a pass inside a growing set fails in the view of the Contract whose supply holds it, and each Contract with a
    // node in such a set has its record, once.
    private void RecordGrowingSets()
    {
        foreach (var (from, to, _, premise) in this.expansionPasses)
        {
            var root = this.expansionComponent[from];
            var owner = this.contractShapes[this.expansionOwners[from]].Symbol.Declaration;
            if (premise is not null && this.expansionGrowing[root] && root == this.expansionComponent[to] && this.scopes[owner].Constraints is { } environment)
            {
                this.FailPremise(environment, premise, owner);
            }
        }

        for (int n = 0, last = -1; n < this.expansionOwners.Count; n++)
        {
            if (this.expansionOwners[n] != last && this.expansionGrowing[this.expansionComponent[n]])
            {
                last = this.expansionOwners[n];
                this.Record(this.contractShapes[last].Symbol.Declaration, BindingFailure.InvalidConstraint, []);
            }
        }
    }
}
