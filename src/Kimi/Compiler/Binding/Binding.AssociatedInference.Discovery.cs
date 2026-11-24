// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The associated-inference owner's discovery (R7-C4, PLAN G172; design Revision 2 §3.2-§3.3; the owner's O-484 (1 a)): once per pass, before
// any inferred binding is read, what each inferring definition can read and the components the inferring definitions form under it.
// - Inputs: the pass's inferring definitions (the batches with paths, PrepareAssociatedInference) and their members' Types as written: each
//   function's result and parameters, in the definition's container and in each conditional block (SPEC 8.5.5: the declared results and
//   the identification keys are a source's reads), read from their syntax through each use's one lookup (NamePath), never a bound header.
// - Outputs: per batch its Heads (the definitions its written Types name, at any depth) and Reads (the inferring definitions it can read:
//   a projection's subject definition; for a value-dependent projection, one whose subject is itself a projection, every inferring
//   definition among the Heads of the definition that subject reads and of what that definition reads, closed: SPEC 8.5.5 Extraction
//   reads the subject's value first); the components of the inferring definitions under Reads in Tarjan's emission order (each after
//   every component it reads), and each batch's component (AssociatedInference.Component).
// - Invariants: a function of the declarations, whichever order Tarjan visits; no inferred binding, normalization, identification or
//   proof is read; Reads only grows to its fixpoint. A generic definition's parameter names no definition: its argument's definitions are
//   among the subject's Heads (D2-B1). The walks keep their work on the batches' lists and this owner's stack (no host recursion per
//   definition: a chain of 10,000 definitions is 10,000 steps).
// - Prohibitions: no read of an inferred value; no state but the batches' discovery fields and the lists below, reset here each pass.
// Every batch present after PrepareAssociatedInference drops the inactive ones has paths (an active batch's paths include the one
// that made it active), so a batch found by its Type infers.
// - The binding a written projection or a requirement's Type names is decided by identity, directly or through the definition's one
//   published equality (NamesBinding; C4 rulings 13 (1), 17 B1): the source list and Reach's edges read the same fact, never a spelled
//   name. The validator (ValidateInferenceRead) makes a read outside Reach a broken contract (InternalInvariant_Kd), never a count.
public sealed partial class Binding
{
    // The inferring definitions of the pass in Tarjan's emission order (each component after every component it reads).
    private readonly List<AssociatedInference> inferenceComponents = new();

    // Discovery's work stack (Tarjan's stack, then the Reach walk's), kept with its capacity across passes.
    private readonly List<AssociatedInference> inferenceWork = new();

    private readonly Stack<AssociatedInference> inferenceTarjan = new();

    // Per binding of the component under request, by its entry (DiscoverValues; C4 ruling 11): its sources and its direct reads (canonical
    // entries) in two arenas, the strongly connected components of the bindings under those reads (Tarjan's state per entry), and each such
    // component's Reach as sorted entries in an arena, made at its first need. Request-local, capacity kept.
    private readonly List<(BoundConformancePath Path, BoundRequirement Requirement)> inferenceValueSources = new();

    private readonly List<int> inferenceEdges = new();

    private readonly List<(int Sources, int SourceCount, int Edges, int EdgeCount)> inferenceValueFacts = new();

    private readonly List<int> inferenceValueIndex = new();

    private readonly List<int> inferenceValueLow = new();

    private readonly List<int> inferenceValueScc = new();

    private readonly List<int> inferenceValueVisit = new();

    private readonly List<(int Entry, int Cursor)> inferenceValueWork = new();

    private readonly List<int> inferenceValueStack = new();

    private readonly List<int> inferenceReachArena = new();

    private readonly List<(int Start, int Count)> inferenceReachOf = new();

    // The published equalities one NamesIdentity walk has read.
    private readonly List<AssociatedBinding> inferenceEqualities = new();

    private int inferenceVisit;

    // The component's canonical entries (ConnectValues), so a Reach of that many covers it.
    private int inferenceCanonicalCount;

    private void DiscoverInferenceComponents()
    {
        this.inferenceComponents.Clear();
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { } batch && batch.Paths.Count > 0)
            {
                batch.Heads.Clear();
                batch.Reads.Clear();
                batch.Dependent.Clear();
                batch.MemberReads.Clear();
                batch.Index = -1;
                batch.Component = -1;
                var container = (DeclarationContainerKoto)batch.Type.Declaration;
                for (var i = 0; i < container.Members.Count; i++)
                {
                    this.DiscoverMember(batch, container.Members[i]);
                }
            }
        }

        // A value-dependent projection reads every inferring definition among the Heads its subject's definition can name, and what that
        // definition reads, to the fixpoint (Reads only grows; the inferring definitions are finite).
        for (var changed = true; changed;)
        {
            changed = false;
            for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
            {
                if (batches.Current is { } batch && batch.Paths.Count > 0)
                {
                    for (var i = 0; i < batch.Dependent.Count; i++)
                    {
                        changed |= this.AddHeadReads(batch, batch.Dependent[i]);
                    }
                }
            }
        }

        var counter = 0;
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { } batch && batch.Paths.Count > 0 && batch.Index < 0)
            {
                this.ConnectInference(batch, ref counter);
            }
        }
    }

    private void DiscoverMember(AssociatedInference batch, Koto member)
    {
        switch (member)
        {
            case FunctionKoto function when this.scopes.TryGetValue(function, out var scope):
                if (function.ReturnType is { } result)
                {
                    this.DiscoverWritten(batch, function, result, scope, false);
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    this.DiscoverWritten(batch, function, function.Parameters[i].Type, scope, false);
                }

                break;
            case SyntaxFormKoto syntax when TryConditionalBlock(syntax, out var block):
                for (var i = 0; i < block.Items.Count; i++)
                {
                    this.DiscoverMember(batch, block.Items[i]);
                }

                break;
        }
    }

    // A member's Type syntax as written, walked with the binder's one lookup of each use (NamePath, published on the use as the Type binder's
    // first call of it publishes it): discovery runs before the members' headers are bound (the batches bind them, IdentifyRequirement), and
    // binding a header here would complete a pending one before `associatedInferenceReady` (BindFunctionHeader) or, after it, read an
    // inferred binding. Every named definition is a Head; a projection reads its receiver's definition; one inside another projection's
    // receiver (`subject`: `S.(EC).W.(FC).U`, `Option<S.(EC).W>.(FC).U`) makes the reader value-dependent on it. The walk takes the Type
    // forms' own Type parts (a length or an Origin argument is no Type) and looks up only the uses the Type binder looks up, in its order (a
    // path, then a projection's receiver). Each projection on an inferring definition is also kept as the member's read (MemberReads, the
    // definition's own included: a binding's Reach reads it), with the associated Type it names (ProjectedAssociated). Returns the
    // definition a name, generic or path use selects (a projection's receiver), or none.
    private BindingSymbol? DiscoverWritten(AssociatedInference batch, FunctionKoto source, Koto? syntax, BindingScope scope, bool subject)
    {
        var use = syntax is null ? null : UseNode(syntax);
        if (use is MemberAccessKoto { Right: not ParenthesizedTypeKoto } or GenericsKoto || (use is not null && TypeSpelling(use) is not null))
        {
            var path = this.NamePath(use!, scope);
            if (use is MemberAccessKoto member && path.End == PathEnd.Projection)
            {
                var receiver = member.Left is MemberAccessKoto { Right: ParenthesizedTypeKoto } qualified ? qualified.Left : member.Left;
                var read = this.InferringDefinition(this.DiscoverWritten(batch, source, receiver, scope, true));
                if (read is not null)
                {
                    batch.MemberReads.Add(new(source, read, this.ProjectedAssociated(read, member, scope), subject));
                }

                if (read is not null && !ReferenceEquals(read, batch))
                {
                    AddOnce(batch.Reads, read);
                    if (subject)
                    {
                        AddOnce(batch.Dependent, read);
                    }
                }

                // The qualifier's Contract use writes Types of its own (`S.(K<E.(EC).V>).Out`, SPEC 8.6.1): its arguments select the
                // conformance the projection reads, so they are the source's reads.
                if (member.Left is MemberAccessKoto { Right: ParenthesizedTypeKoto contract } && WrittenReference(contract) is { } written &&
                    ContractApplication(UnwrapTransparentType(written)!) is { } arguments)
                {
                    for (var i = 0; i < arguments.TypeArguments.Count; i++)
                    {
                        this.DiscoverWritten(batch, source, arguments.TypeArguments[i], scope, subject);
                    }
                }

                return null;
            }

            // The definition the use names (SPEC 6.1.5, 9.1): a written `Self` selects the declaration's Self symbol, which names it (Named).
            var named = path.Symbol?.Named;
            this.AddHead(batch, named);
            for (var i = 0; use is GenericsKoto generic && i < generic.TypeArguments.Count; i++)
            {
                this.DiscoverWritten(batch, source, generic.TypeArguments[i], scope, subject);
            }

            // A nested Type's qualifiers write Types of their own (`Outer<E.(EC).W>.Inner`), each bound by BindContainerReference: their
            // generic arguments are the path's reads.
            for (var qualifier = use is MemberAccessKoto nested ? UseNode(nested.Left) : null; qualifier is not null;
                qualifier = qualifier is MemberAccessKoto outer ? UseNode(outer.Left) : null)
            {
                for (var i = 0; qualifier is GenericsKoto written && i < written.TypeArguments.Count; i++)
                {
                    this.DiscoverWritten(batch, source, written.TypeArguments[i], scope, subject);
                }
            }

            return named;
        }

        switch (use)
        {
            case TupleTypeKoto tuple:
                for (var i = 0; i < tuple.ElementNodes.Count; i++)
                {
                    this.DiscoverWritten(batch, source, tuple.ElementNodes[i], scope, subject);
                }

                break;
            case FunctionTypeKoto function:
                this.DiscoverWritten(batch, source, function.Parameters, scope, subject);
                this.DiscoverWritten(batch, source, function.ReturnType, scope, subject);
                break;
            case OptionalTypeKoto optional:
                this.DiscoverWritten(batch, source, optional.Type, scope, subject);
                break;
            case FixedArrayTypeKoto array:
                this.DiscoverWritten(batch, source, array.ElementType, scope, subject);
                break;
            case OriginApplicationKoto application:
                this.DiscoverWritten(batch, source, application.Type, scope, subject);
                break;
            case TypeSemanticsKoto { Type: { } layer }:
                this.DiscoverWritten(batch, source, layer, scope, subject);
                break;
        }

        return null;

        static void AddOnce(List<AssociatedInference> list, AssociatedInference item)
        {
            if (!list.Contains(item))
            {
                list.Add(item);
            }
        }
    }

    // The associated Type a written projection on the inferring definition `read` names (SPEC 8.5.2), by the binder's one search
    // (SearchAssociated, recording nothing) over the definition's conformances; a `.(C)` qualifier is not looked up (discovery's one
    // lookup of a use is NamePath's), so a name two of its Contracts declare is ambiguous here. None when the name is ambiguous or names
    // none: the read then reads every binding of the definition (Reach over-approximates; it never misses).
    private BindingSymbol? ProjectedAssociated(AssociatedInference read, MemberAccessKoto projection, BindingScope scope)
        => TypeSpelling(projection.Right) is { } name && this.SearchAssociated(this.SelfType(read.Type), scope, name, null) is { Alternative: null, Found: { } found }
            ? found : null;

    private void AddHead(AssociatedInference batch, BindingSymbol? symbol)
    {
        if (symbol is { Declaration: StructKoto or EnumKoto } && !batch.Heads.Contains(symbol))
        {
            batch.Heads.Add(symbol);
        }
    }

    // The inferring definition a Type name selects, or none.
    private AssociatedInference? InferringDefinition(BindingSymbol? symbol)
        => symbol is { Declaration: StructKoto or EnumKoto } && this.associatedInference.TryGetValue(symbol, out var batch) && batch.Paths.Count > 0 ? batch : null;

    // Adds to `batch`'s Reads the inferring definitions among `source`'s Heads and Reads; whether any was new.
    private bool AddHeadReads(AssociatedInference batch, AssociatedInference source)
    {
        var added = false;
        for (var i = 0; i < source.Heads.Count; i++)
        {
            if (this.associatedInference.TryGetValue(source.Heads[i], out var head) && !ReferenceEquals(head, batch) &&
                !batch.Reads.Contains(head))
            {
                batch.Reads.Add(head);
                added = true;
            }
        }

        for (var i = 0; i < source.Reads.Count; i++)
        {
            if (!ReferenceEquals(source.Reads[i], batch) && !batch.Reads.Contains(source.Reads[i]))
            {
                batch.Reads.Add(source.Reads[i]);
                added = true;
            }
        }

        if (!ReferenceEquals(source, batch) && !batch.Reads.Contains(source))
        {
            batch.Reads.Add(source);
            added = true;
        }

        return added;
    }

    // Tarjan's algorithm over the inferring definitions under Reads, iterative on this owner's work list (a batch's Cursor is its next
    // read); a component is emitted after every component it reads.
    private void ConnectInference(AssociatedInference root, ref int counter)
    {
        var stack = this.inferenceWork;
        stack.Clear();
        var tarjan = this.inferenceTarjan;
        Enter(root, ref counter);
        while (stack.Count > 0)
        {
            var batch = stack[^1];
            if (batch.Cursor < batch.Reads.Count)
            {
                var read = batch.Reads[batch.Cursor++];
                if (read.Index < 0)
                {
                    Enter(read, ref counter);
                }
                else if (read.OnStack)
                {
                    batch.Low = Math.Min(batch.Low, read.Index);
                }

                continue;
            }

            stack.RemoveAt(stack.Count - 1);
            if (stack.Count > 0)
            {
                stack[^1].Low = Math.Min(stack[^1].Low, batch.Low);
            }

            if (batch.Low == batch.Index)
            {
                var component = this.inferenceComponents.Count == 0 ? 0 : this.inferenceComponents[^1].Component + 1;
                AssociatedInference member;
                do
                {
                    member = tarjan.Pop();
                    member.OnStack = false;
                    member.Component = component;
                    this.inferenceComponents.Add(member);
                }
                while (!ReferenceEquals(member, batch));
            }
        }

        void Enter(AssociatedInference entered, ref int next)
        {
            entered.Index = entered.Low = next++;
            entered.Cursor = 0;
            entered.OnStack = true;
            tarjan.Push(entered);
            stack.Add(entered);
        }
    }

    // Per binding of a component, before its request (the owner's O-484 (1 a); C4 ruling 11; BindBeforeRequest has bound the requirement
    // headers): its sources, the function requirements of the definition's paths whose required Type names the binding (SPEC 8.5.5
    // Extraction: a source is a requirement's identified implementation; NamesBinding), and its direct reads, the component's bindings
    // its sources' candidates' written Types name (the candidates identification looks up, RequirementCandidates; each read's binding by
    // NamesBinding; a value-dependent read reads every binding of the definitions its subject's definition can name, as Reads does); then
    // the bindings' components (Tarjan, iterative), each after those it reads. Reach(v) is v's binding component and every one it reads
    // (ValueReaches). A requirement whose header is not bound names every binding.
    private void DiscoverValues(int component)
    {
        var entries = this.inferenceBindings;
        this.inferenceValueSources.Clear();
        this.inferenceEdges.Clear();
        this.inferenceValueFacts.Clear();
        this.inferenceReachArena.Clear();
        this.inferenceReachOf.Clear();
        for (var e = 0; e < entries.Count; e++)
        {
            var (sources, edges) = (this.inferenceValueSources.Count, this.inferenceEdges.Count);
            if (this.inferenceBindingIndex[entries[e].Binding] == e)
            {
                this.DiscoverSources(entries[e], edges, component);
            }

            this.inferenceValueFacts.Add((sources, this.inferenceValueSources.Count - sources, edges, this.inferenceEdges.Count - edges));
        }

        this.ConnectValues();
    }

    private void DiscoverSources(InferenceEntry entry, int edges, int component)
    {
        var batch = entry.Batch;
        for (var p = 0; p < batch.Paths.Count; p++)
        {
            var path = batch.Paths[p];
            if (path.InheritedFrom is not null)
            {
                continue;
            }

            foreach (var identity in path.Contract.Contract!.RequirementStorage)
            {
                if (identity.Contract != path.Contract || identity.Symbol.Declaration is not FunctionKoto requirement)
                {
                    continue;
                }

                this.inferenceEqualities.Clear();
                if (requirement.BoundSymbol?.Type is not { } required || this.NamesIdentity(path.RootPath, required, entry.Identity.Symbol))
                {
                    this.inferenceValueSources.Add((path, identity));
                    this.AddCandidateReads(batch, path, identity, edges, component);
                }
            }
        }
    }

    // Whether `type` names the associated Type `associated` (SPEC 8.5.5 Extraction: a position of the binding in a source's required Type),
    // by identity, directly or through the definition's published equalities (C4 ruling 13 (1)): a projection on another associated Type of
    // the root path names what that Type's binding is specified as, its Candidates (PrepareAssociatedBindings, the one published equality:
    // Iterator's `associate LendingIterator.LentItem(step) is Item` gives LendingIterator's `next` as Item's source). Each equality is read
    // once (inferenceEqualities, cleared by the caller), so a cycle of specifications ends.
    private bool NamesIdentity(BoundConformancePath root, BoundType type, BindingSymbol associated)
    {
        if (type.Kind == BoundTypeKind.AssociatedProjection && type.Symbol is { } symbol && this.NamesBinding(root, symbol, associated))
        {
            return true;
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.NamesIdentity(root, type.Components[i], associated))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the associated Type `projected` names the binding of `associated` on `root` (the one member for "the binding a written
    // projection names", C4 ruling 17 B1): itself, or what root's binding of `projected` is specified as (its Candidates, the one published
    // equality: a contract's `associate B is A` or a definition's `associate K.B is Self.(K).A` gives B as A), read by NamesIdentity.
    private bool NamesBinding(BoundConformancePath root, BindingSymbol projected, BindingSymbol associated)
    {
        if (ReferenceEquals(projected, associated))
        {
            return true;
        }

        for (var i = 0; root.AssociatedBindings is { } bindings && i < bindings.Count; i++)
        {
            var binding = bindings[i].Binding;
            if (!ReferenceEquals(bindings[i].Associated.Symbol, projected) || this.inferenceEqualities.Contains(binding))
            {
                continue;
            }

            this.inferenceEqualities.Add(binding);
            for (var c = 0; c < binding.Candidates.Count; c++)
            {
                if (this.NamesIdentity(root, binding.Candidates[c], associated))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // The reads of a source's candidates: the members of `batch` identification looks up for the requirement (RequirementCandidates).
    private void AddCandidateReads(AssociatedInference batch, BoundConformancePath path, BoundRequirement requirement, int edges, int component)
    {
        var self = this.ConformanceSelf(path);
        for (var candidate = this.RequirementCandidates(path, requirement, self).Member; candidate is not null; candidate = candidate.Next)
        {
            for (var r = 0; r < batch.MemberReads.Count; r++)
            {
                if (ReferenceEquals(batch.MemberReads[r].Member, candidate.Declaration))
                {
                    this.AddValueReads(batch.MemberReads[r], edges, component);
                }
            }
        }
    }

    private void AddValueReads(WrittenRead read, int edges, int component)
    {
        if (!read.Dependent)
        {
            this.AddValueEdges(read.Read, read.Associated, edges, component);
            return;
        }

        this.AddValueEdges(read.Read, null, edges, component);
        for (var i = 0; i < read.Read.Heads.Count; i++)
        {
            if (this.associatedInference.TryGetValue(read.Read.Heads[i], out var head))
            {
                this.AddValueEdges(head, null, edges, component);
            }
        }

        for (var i = 0; i < read.Read.Reads.Count; i++)
        {
            this.AddValueEdges(read.Read.Reads[i], null, edges, component);
        }
    }

    // The bindings of `batch` the associated Type `associated` names (NamesBinding; every one for none) in the component under request,
    // as canonical entries, once each.
    private void AddValueEdges(AssociatedInference batch, BindingSymbol? associated, int edges, int component)
    {
        for (var x = batch.FirstEntry; batch.Component == component && x < batch.EntryEnd; x++)
        {
            var entry = this.inferenceBindings[x];
            var canonical = this.inferenceBindingIndex[entry.Binding];
            this.inferenceEqualities.Clear();
            if ((associated is null || this.NamesBinding(entry.Path, associated, entry.Identity.Symbol)) && this.inferenceEdges.IndexOf(canonical, edges) < 0)
            {
                this.inferenceEdges.Add(canonical);
            }
        }
    }

    // Tarjan's algorithm over the canonical entries under their direct reads, iterative on the work list; the binding components are
    // numbered in emission order (each after every one it reads).
    private void ConnectValues()
    {
        var (index, low, scc) = (this.inferenceValueIndex, this.inferenceValueLow, this.inferenceValueScc);
        index.Clear();
        low.Clear();
        scc.Clear();
        for (var e = 0; e < this.inferenceBindings.Count; e++)
        {
            index.Add(-1);
            low.Add(-1);
            scc.Add(-1);
        }

        var (work, stack) = (this.inferenceValueWork, this.inferenceValueStack);
        var counter = 0;
        for (var root = 0; root < this.inferenceBindings.Count; root++)
        {
            if (index[root] >= 0 || this.inferenceBindingIndex[this.inferenceBindings[root].Binding] != root)
            {
                continue;
            }

            Enter(root, ref counter);
            while (work.Count > 0)
            {
                var (e, cursor) = work[^1];
                var facts = this.inferenceValueFacts[e];
                if (cursor < facts.EdgeCount)
                {
                    work[^1] = (e, cursor + 1);
                    var x = this.inferenceEdges[facts.Edges + cursor];
                    if (index[x] < 0)
                    {
                        Enter(x, ref counter);
                    }
                    else if (scc[x] == -2)
                    {
                        low[e] = Math.Min(low[e], index[x]);
                    }

                    continue;
                }

                work.RemoveAt(work.Count - 1);
                if (work.Count > 0)
                {
                    low[work[^1].Entry] = Math.Min(low[work[^1].Entry], low[e]);
                }

                if (low[e] == index[e])
                {
                    var id = this.inferenceReachOf.Count;
                    this.inferenceReachOf.Add((-1, 0));
                    int member;
                    do
                    {
                        member = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        scc[member] = id;
                    }
                    while (member != e);
                }
            }
        }

        this.inferenceCanonicalCount = counter;

        void Enter(int entry, ref int next)
        {
            index[entry] = low[entry] = next++;
            scc[entry] = -2; // on Tarjan's stack
            stack.Add(entry);
            work.Add((entry, 0));
        }
    }

    // Whether the binding `to` (a canonical entry) is in `from`'s Reach: the same binding component, or one it reads (an earlier one, by
    // Tarjan's emission order), from the reading component's Reach made at its first need.
    private bool ValueReaches(int from, int to)
    {
        var (a, b) = (this.inferenceValueScc[from], this.inferenceValueScc[to]);
        if (a == b)
        {
            return true;
        }

        var (start, count) = b > a ? (0, 0) : this.ReachOf(a);
        return count > 0 && this.inferenceReachArena.BinarySearch(start, count, to, null) >= 0;
    }

    // Whether `index`'s Reach is every binding of the component (one binding component, as a ring's, or one reading all the others):
    // a hold's meet with it is the hold (HoldMeet).
    private bool ReachCoversComponent(int index)
        => this.inferenceReachOf.Count == 1 || this.ReachOf(this.inferenceValueScc[index]).Count == this.inferenceCanonicalCount;

    // Whether `index`'s Reach holds another binding of the component (design §3.4: its value reads the component; DecideAtLimit).
    private bool ReachesAnotherBinding(int index)
    {
        if (this.inferenceReachOf.Count == 1)
        {
            return this.inferenceCanonicalCount > 1;
        }

        var (start, count) = this.ReachOf(this.inferenceValueScc[index]);
        for (var i = start; i < start + count; i++)
        {
            if (this.inferenceBindingIndex[this.inferenceBindings[this.inferenceReachArena[i]].Binding] != index)
            {
                return true;
            }
        }

        return false;
    }

    // The binding component `a`'s Reach as sorted entries in the arena, made at its first need.
    private (int Start, int Count) ReachOf(int a)
    {
        var (start, count) = this.inferenceReachOf[a];
        if (start >= 0)
        {
            return (start, count);
        }

        var (arena, work, visit) = (this.inferenceReachArena, this.inferenceValueStack, this.inferenceValueVisit);
        while (visit.Count < this.inferenceBindings.Count)
        {
            visit.Add(0);
        }

        var stamp = ++this.inferenceVisit;
        start = arena.Count;
        for (var e = 0; e < this.inferenceBindings.Count; e++)
        {
            if (this.inferenceValueScc[e] == a)
            {
                visit[e] = stamp;
                work.Add(e);
            }
        }

        while (work.Count > 0)
        {
            var e = work[^1];
            work.RemoveAt(work.Count - 1);
            arena.Add(e);
            var facts = this.inferenceValueFacts[e];
            for (var i = 0; i < facts.EdgeCount; i++)
            {
                var x = this.inferenceEdges[facts.Edges + i];
                if (visit[x] != stamp)
                {
                    visit[x] = stamp;
                    work.Add(x);
                }
            }
        }

        count = arena.Count - start;
        arena.Sort(start, count, null);
        this.inferenceReachOf[a] = (start, count);
        return (start, count);
    }

    // Whether `owner`'s sources include the requirement `requirement` on `path` (the own run's filter, the definition run's validator).
    private bool SourceOf(int owner, BoundConformancePath path, BindingSymbol requirement)
    {
        var (sources, count) = (this.inferenceValueFacts[owner].Sources, this.inferenceValueFacts[owner].SourceCount);
        for (var i = sources; i < sources + count; i++)
        {
            if (ReferenceEquals(this.inferenceValueSources[i].Path, path) && ReferenceEquals(this.inferenceValueSources[i].Requirement.Symbol, requirement))
            {
                return true;
            }
        }

        return false;
    }

    // The validator (design §3.2; C4 ruling 17 B1), ResolveAssociated's one site: a run reads an inferring definition's binding only within
    // its reader's Reach as discovery found it. In the component under request, the own run of a binding reads that binding's Reach, or a
    // binding its own additions hold (C4 ruling 14: itself, or in the run of its definition's bindings, Frozen being the run itself, a
    // binding of its definition; such a read answers pending whatever the outer hold, so the state's key keeps it out); a definition's run
    // (publication) reads under the definition's hold. Another component's binding is one the reader's Reads reach (an earlier component,
    // published). A read outside is a broken contract of the key or of the components' order: InternalInvariant_Kd, never a count.
    private void ValidateInferenceRead(AssociatedInference read, AssociatedBinding binding)
    {
        if (this.collectingAssociated is not { } run)
        {
            return;
        }

        var within = this.inferenceRequest >= 0 && read.Component == this.inferenceRequest && this.inferenceBindingIndex.TryGetValue(binding, out var index)
            ? run.Owner < 0 || index == run.Owner || this.ValueReaches(run.Owner, index) || (ReferenceEquals(run.Frozen, run) && ReferenceEquals(read.Type, run.Type))
            : ReferenceEquals(read.Type, run.Type) || (this.associatedInference.TryGetValue(run.Type, out var reader) && this.Reaches(reader, read));
        if (!within)
        {
            throw new InvalidOperationException($"A run of `{run.Type.Name}` reads `{read.Type.Name}`'s binding outside its Reach (InternalInvariant; R7-C4).");
        }
    }

    // Whether `to` is in `from`'s Reach: Reads closed transitively, walked on the work list with a visit stamp (no allocation).
    private bool Reaches(AssociatedInference from, AssociatedInference to)
    {
        var visit = ++this.inferenceVisit;
        var work = this.inferenceWork;
        work.Clear();
        work.Add(from);
        from.Visit = visit;
        while (work.Count > 0)
        {
            var batch = work[^1];
            work.RemoveAt(work.Count - 1);
            for (var i = 0; i < batch.Reads.Count; i++)
            {
                var read = batch.Reads[i];
                if (ReferenceEquals(read, to))
                {
                    return true;
                }

                if (read.Visit != visit)
                {
                    read.Visit = visit;
                    work.Add(read);
                }
            }
        }

        return false;
    }
}
