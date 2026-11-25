// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The associated-inference owner's demand side (R7-C4, PLAN G172; design Revision 2 §3.1-§3.4; the owner's O-484 (1 a, 2 x)): how the pass
// determines the inferred bindings and publishes them.
// - Inputs: discovery's components in emission order (Binding.AssociatedInference.Discovery), each inferring definition's paths.
// - Outputs: each hole's binding (AssociatedBinding.Inferred, InferenceConflict, Problem, EvidenceSources), published when its component's
//   request ends; then the paths' AssociatedStorage and InferenceStorage, each path's resolved in a site frame at its conformance where
//   none is open (OpenQuerySite; PLAN G651 a2, F2), and the inherited paths' inference.
// - The rule (SPEC 8.5.5, 8.3.3 Finality): Value(v | P) is pending when v is held; else a source position supplies its Type when the
//   collection with v held and the collection with v's definition's bindings held give it the same complete Type (HeldValue). A
//   collection under a hold is a run (a batch's run object, InferenceRun): identification, extraction and the usable positions, every
//   read of another binding of the component answered under the run's hold (ResolveAssociated), every answer Provisional.
// - Invariants: one request per component, none nested in another; a request's tables (values, runs, holds) are request-local and
//   dropped at its end (capacity kept); a state's runs go back to their batch's pool when it is answered, so a request holds about two
//   runs per open state, and a pool keeps at most InferenceRunsKept runs across requests (C4 ruling 15); a binding is written once, at
//   its request's end; problems are recorded by the definition's run (the hold of its bindings, made at publication), never by a held run.
// - Prohibitions: no held answer outside its request; no read of a later component's binding (the validator); no identification made
//   inside a request is kept (a held answer is Provisional; the pass's, made before the request, is read: IdentifyRequirement).
public sealed partial class Binding
{
    // OQ2's items limit (BU-29; the owner's O-484 (2 x); SPEC 8.3.3 Limits): a component's request holds at most this many held states.
    internal const int InferenceStateLimit = 65536;

    // C4 ruling 15 (d): a batch's pool keeps at most this many runs once its request ends, the most a request rents of one definition at
    // once while none of its states reads another binding of the component (publication: the definition's run and one binding's two),
    // so a warm rebind of such a definition allocates no run; a definition whose states nest rents up to two runs a binding.
    internal const int InferenceRunsKept = 3;

    private readonly Dictionary<AssociatedBinding, int> inferenceBindingIndex = new(ReferenceEqualityComparer.Instance);

    private readonly List<InferenceEntry> inferenceBindings = new();

    // The request's holds (HoldAdd): each its parent and its newest member's order number; interned by both, so a set is one id.
    private readonly List<(int Parent, int Top)> inferenceHolds = new();

    private readonly Dictionary<(int Parent, int Top), int> inferenceHoldIds = new();

    // Per entry its order number (when first held, else -1), and per order number its entry; HoldAdd's and HoldMeet's work list.
    private readonly List<int> inferenceHoldOrder = new();

    private readonly List<int> inferenceHoldEntry = new();

    private readonly List<(int Top, int Hold)> inferenceHoldWork = new();

    private readonly Dictionary<(AssociatedBinding Binding, int Hold), HeldAnswer> inferenceValues = new();

    // The held states the requests evaluated in this compilation (design §6, D2-C3: the cost per state is the time over this count).
    internal int InferenceStates { get; private set; }

    // Whether the open request reached InferenceStateLimit (HeldValue): no state is evaluated after it, and the component is decided at
    // the limit (DecideAtLimit).
    private bool inferenceLimited;

    // The component whose request is open, or -1.
    private int inferenceRequest = -1;

    private int inferenceEmptyHold;

    private void PrepareAssociatedInference()
    {
        foreach (var path in this.impls.InPass.Paths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.InferenceHole(path, identity, path.Use))
                {
                    InferenceBatches.Batch(path.Type).Active = true;
                    break;
                }
            }
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            if (this.associatedInference.TryGetValue(path.Type, out var batch) && batch.Active)
            {
                batch.Paths.Add(path);
            }
        }

        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            if (batches.Current is { Active: false } inactive)
            {
                InferenceBatches.Remove(inactive);
            }
        }

        this.DiscoverInferenceComponents();
        this.associatedInferenceReady = true;
        this.InferComponents();

        // Explicit equations may point at a newly completed binding. Normalize their values again,
        // preserving their original candidates for final formation/projection verification.
        foreach (var path in this.impls.InPass.Paths)
        {
            if (path.AssociatedBindings is { } bindings)
            {
                foreach (var (_, binding) in bindings)
                {
                    binding.State = 0;
                }
            }
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            // PLAN G651 a2 (F2, BU-161; IMPL 21.3.2): the conformance (PrepareAssociatedBindings' site, R7-M38) is the site of a Type its
            // bindings form past the limit, in a site frame where none is open.
            var site = this.OpenQuerySite(path.Declaration);
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.ResolveAssociated(path, identity, out _) is { } result && !this.PendingAssociatedType(result))
                {
                    path.AssociatedStorage[identity] = result;
                    if (this.AssociatedBindingOf(path.RootPath, identity) is { } binding)
                    {
                        foreach (var evidence in binding.EvidenceSources)
                        {
                            path.InferenceStorage.Add(new(identity, result, evidence.Path, evidence.Source));
                            this.AddInferenceConsumer(evidence.Path, path);
                        }
                    }
                }
            }

            this.CloseQuerySite(site);
        }

        foreach (var path in this.impls.InPass.Paths)
        {
            this.PublishInheritedInference(path);
        }
    }

    private void AddInferenceConsumer(BoundConformancePath source, BoundConformancePath target)
    {
        if (ReferenceEquals(source, target))
        {
            return;
        }

        var consumers = source.InferenceConsumers ??= new();
        if (consumers.Count == 0)
        {
            this.inferenceSources.Add(source);
        }

        if (!consumers.Contains(target))
        {
            consumers.Add(target);
        }
    }

    private void PublishInheritedInference(BoundConformancePath path)
    {
        if (path.InheritedFrom is null || path.InferencePublished)
        {
            return;
        }

        path.InferencePublished = true;

        for (var i = 0; i < path.InheritedSourceCount; i++)
        {
            var source = path.InheritedSource(i);
            this.PublishInheritedInference(source);
            this.AddInferenceConsumer(source, path);
            foreach (var evidence in source.InferenceStorage)
            {
                var identity = new BoundRequirement(evidence.Identity.Symbol, this.SubstituteRequirementContract(evidence.Identity.Contract, path.InheritedBase!));
                if (path.AssociatedStorage.TryGetValue(identity, out var type))
                {
                    path.InferenceStorage.Add(new(identity, type, source, evidence.Source));
                }
            }
        }
    }

    // SPEC 8.5.5, 8.3.3 Finality (R7-C4; the owner's O-484 (2 x)): the inferring definitions' components in Tarjan's emission order, one
    // request each; a component's bindings are published when its request ends, so a later component reads them as Complete.
    private void InferComponents()
    {
        var start = 0;
        while (start < this.inferenceComponents.Count)
        {
            var component = this.inferenceComponents[start].Component;
            var end = start;
            while (end < this.inferenceComponents.Count && this.inferenceComponents[end].Component == component)
            {
                end++;
            }

            this.RunComponent(component, start, end);
            start = end;
        }
    }

    // One component's request: every binding's root state Value(v | {}) and the held states they reach, in request-local tables; then
    // publication. Nothing but the published bindings outlives the request.
    private void RunComponent(int component, int start, int end)
    {
        this.inferenceValues.Clear();
        this.inferenceBindingIndex.Clear();
        this.inferenceBindings.Clear();
        this.inferenceHolds.Clear();
        this.inferenceHoldIds.Clear();
        this.inferenceHoldOrder.Clear();
        this.inferenceHoldEntry.Clear();
        for (var b = start; b < end; b++)
        {
            var batch = this.inferenceComponents[b];
            batch.FirstEntry = this.inferenceBindings.Count;
            foreach (var path in batch.Paths)
            {
                if (path != path.RootPath || path.InheritedFrom is not null)
                {
                    continue;
                }

                foreach (var identity in path.Contract.Contract!.AssociatedStorage)
                {
                    if (this.InferenceHole(path, identity, path.Use) && this.AssociatedBindingOf(path, identity) is { } binding &&
                        !this.inferenceBindingIndex.ContainsKey(binding))
                    {
                        // One value per definition and identity (SPEC 8.5.5: `Self is C and P` with C: P conforms to P once): the root
                        // paths' bindings of one identity share its hold index, the first such binding's.
                        var index = this.inferenceBindings.Count;
                        for (var i = 0; i < this.inferenceBindings.Count && index == this.inferenceBindings.Count; i++)
                        {
                            if (ReferenceEquals(this.inferenceBindings[i].Batch, batch) && this.inferenceBindings[i].Identity == identity)
                            {
                                index = i;
                            }
                        }

                        this.inferenceBindingIndex.Add(binding, index);
                        this.inferenceBindings.Add(new(binding, batch, path, identity));
                    }
                }
            }

            batch.EntryEnd = this.inferenceBindings.Count;
        }

        this.BindBeforeRequest(start, end);
        this.DiscoverValues(component);
        this.inferenceRequest = component;
        for (var i = 0; i < this.inferenceBindings.Count; i++)
        {
            this.inferenceHoldOrder.Add(-1);
        }

        var empty = this.inferenceEmptyHold = this.HoldNode(-1, -1);
        try
        {
            for (var i = 0; i < this.inferenceBindings.Count; i++)
            {
                this.HeldValue(this.inferenceBindings[i].Binding, empty);
            }

            if (this.inferenceLimited)
            {
                this.DecideAtLimit();
            }
            else
            {
                for (var b = start; b < end; b++)
                {
                    var batch = this.inferenceComponents[b];
                    if (batch.FirstEntry == batch.EntryEnd)
                    {
                        continue;
                    }

                    var definition = this.InferenceRun(batch, this.HoldWith(empty, -1, batch), -1);
                    for (var i = batch.FirstEntry; i < batch.EntryEnd; i++)
                    {
                        this.PublishInferred(this.inferenceBindings[i], definition, empty);
                    }

                    batch.ReturnRun(definition);
                }
            }
        }
        finally
        {
            this.inferenceRequest = -1;
            this.inferenceLimited = false;
            for (var b = start; b < end; b++)
            {
                var batch = this.inferenceComponents[b];
                batch.Published = true;
                batch.KeepRuns();
            }
        }
    }

    // Value(v | P) (design §3.1): pending when v is held; else a source position supplies its Type when the run with v held and the run
    // with v's definition's bindings held give it the same complete Type; none: missing; two Types: conflict; else the Type. The state is
    // (v, P n Reach(v)) (design §3.3; C4 ruling 11): v's answer reads nothing outside its Reach, so the rest of the hold is not its key.
    private HeldAnswer HeldValue(AssociatedBinding binding, int hold)
    {
        var index = this.inferenceBindingIndex[binding];
        if (this.HoldContains(hold, index))
        {
            return new(null, HeldState.Pending);
        }

        hold = this.HoldMeet(hold, index);
        if (this.inferenceValues.TryGetValue((binding, hold), out var known))
        {
            // Each step of a request adds its binding to the hold, and a binding that reads v keeps v in its Reach: a state open here was
            // reached by a read outside a Reach (the validator's broken contract).
            return known.State != HeldState.Open ? known : throw new InvalidOperationException("A held state reads itself: a read lies outside its reader's Reach (InternalInvariant; R7-C4).");
        }

        if (this.inferenceLimited || this.inferenceValues.Count == InferenceStateLimit)
        {
            // SPEC 8.3.3 Limits: past OQ2's items no state is evaluated; the open runs unwind with pending answers, which nothing keeps
            // (the component is decided at the limit, DecideAtLimit).
            this.inferenceLimited = true;
            return new(null, HeldState.Pending);
        }

        this.inferenceValues[(binding, hold)] = new(null, HeldState.Open);
        var entry = this.inferenceBindings[index];
        var ownRun = this.InferenceRun(entry.Batch, this.HoldWith(hold, index, null), index);
        var frozenRun = ownRun.Frozen!;
        BoundType? found = null;
        var conflict = false;
        if (ownRun.Usable.TryGetValue(binding, out var owned) && frozenRun.Usable.TryGetValue(binding, out var held))
        {
            for (var i = 0; i < owned.Count; i++)
            {
                var type = owned[i].Type;
                if (!this.SuppliesInBoth(owned, i, held))
                {
                    continue;
                }

                if (found is null)
                {
                    found = type;
                }
                else if (!this.SameCompleteType(found, type))
                {
                    conflict = true;
                }
            }
        }

        var answer = conflict ? new HeldAnswer(null, HeldState.Conflict) : found is null ? new HeldAnswer(null, HeldState.Missing) : new HeldAnswer(found, HeldState.Type);
        this.inferenceValues[(binding, hold)] = answer;
        this.InferenceStates++;

        // C4 ruling 15 (a): the state's runs go back to the pool now it is answered. The own run serves this state only (its hold is the
        // state's with v); the frozen run serves only v's states, which never nest (each run of v holds v), so a later state of v
        // collects its frozen run again, and the two are the last runs rented of the batch.
        entry.Batch.ReturnRun(ownRun);
        return answer;
    }

    // Whether `own[at]` supplies the same complete Type in the other run. A position's identity across runs is hold-free (design R4 (c)):
    // its path, its source member and the requirement's part there, with its ordinal among that source's positions at the part; never
    // a formed Type's reference.
    private bool SuppliesInBoth(List<(AssociatedEvidence Source, BoundType Type)> own, int at, List<(AssociatedEvidence Source, BoundType Type)> other)
    {
        var (source, type) = own[at];
        var ordinal = 0;
        for (var i = 0; i < at; i++)
        {
            ordinal += SamePosition(own[i].Source, source) ? 1 : 0;
        }

        foreach (var (position, held) in other)
        {
            if (SamePosition(position, source) && ordinal-- == 0)
            {
                return this.SameCompleteType(held, type);
            }
        }

        return false;

        static bool SamePosition(AssociatedEvidence a, AssociatedEvidence b)
            => ReferenceEquals(a.Path, b.Path) && ReferenceEquals(a.Source, b.Source) && ReferenceEquals(a.Required, b.Required);
    }

    // Design R4 (ruling 6 (ii)): before a component's request, with every earlier component published, the headers its runs read are bound
    // as dev's batch binds them (each path's function requirements and IdentifyRequirement's candidates): a header naming no value of the
    // component completes, one naming one is bound partially and abandoned (BindFunctionHeader), its projections kept for the runs to
    // normalize under their holds. No run binds a header (BindHeader) or keeps a formed binding (ResolveAssociated); the header loop and
    // the reset loop after the requests bind and form them finally.
    private void BindBeforeRequest(int start, int end)
    {
        for (var b = start; b < end; b++)
        {
            var batch = this.inferenceComponents[b];
            foreach (var path in batch.Paths)
            {
                if (path.InheritedFrom is not null)
                {
                    continue;
                }

                foreach (var identity in path.Contract.Contract!.RequirementStorage)
                {
                    if (identity.Contract == path.Contract && identity.Symbol.Declaration is FunctionKoto)
                    {
                        this.BindHeader(identity.Symbol);
                        this.IdentifyRequirement(path, identity);
                    }
                }
            }
        }
    }

    // A collection of `batch`'s evidence under `hold` (a run, rented for the one state or publication it serves and given back when that
    // is answered; C4 ruling 15 (a)): identification, extraction and each hole's usable positions (SPEC 8.5.5 Completion: the source
    // path's premises proven under the run's hold), with every read of another binding of the component answered under the hold
    // (ResolveAssociated). A held state's two runs (`owner`, its binding's entry; C4 rulings 11 and 12) collect only that binding's
    // sources (DiscoverValues), as the rule's judgment reads them; the requirements' Types are read in the run of the hold with the
    // definition's bindings held (as dev's batch held them), rented first. The definition's run (-1), made only at publication, collects
    // every source under the definition's bindings: it records the problems and gives the evidence sources.
    private AssociatedInference InferenceRun(AssociatedInference batch, int hold, int owner)
    {
        var held = this.HoldWith(hold, -1, batch);
        var frozen = held == hold ? null : this.InferenceRun(batch, held, owner);
        var run = batch.RentRun();
        run.Hold = hold;
        run.Owner = owner;
        run.Frozen = frozen ?? run;
        run.Records = owner < 0 && hold == this.HoldWith(this.inferenceEmptyHold, -1, batch); // the root's frozen run records the problems

        var previous = this.collectingAssociated;
        this.collectingAssociated = run;
        var formation = this.BeginFormation(); // SPEC 8.5.5, 8.6.3: the inferred bindings are formed from the signatures, whatever the reader.
        try
        {
            this.CollectRun(batch, run);
        }
        finally
        {
            this.EndFormation(formation);
            this.collectingAssociated = previous;
            this.pendingAssociatedTypes.Clear();
        }

        return run;
    }

    // The batch driver's collection, per run: identification, extraction, problems (the recording run only), then each hole's usable
    // positions with their normal Types.
    private void CollectRun(AssociatedInference batch, AssociatedInference run)
    {
        var hasHole = false;
        foreach (var path in batch.Paths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                hasHole |= this.InferenceHole(path, identity, path.Use);
            }
        }

        if (!hasHole)
        {
            return;
        }

        foreach (var path in batch.Paths)
        {
            if (path.InheritedFrom is not null)
            {
                continue;
            }

            var self = this.ConformanceSelf(path);
            foreach (var identity in path.Contract.Contract!.RequirementStorage)
            {
                if (identity.Contract != path.Contract || identity.Symbol.Declaration is not FunctionKoto requirement ||
                    (run.Owner >= 0 && !this.SourceOf(run.Owner, path, identity.Symbol)))
                {
                    continue;
                }

                this.BindHeader(identity.Symbol);
                var match = this.IdentifyRequirement(path, identity);
                if (match.State != RequirementMatchState.Unique && run.Records && identity.Symbol.Type is { } waitingResult)
                {
                    run.Pairs.Clear();
                    Koto? input = null;
                    if (match.State == RequirementMatchState.Pending)
                    {
                        for (var i = 0; i < requirement.Parameters.Count; i++)
                        {
                            var syntax = requirement.Parameters[i].Type;
                            if (syntax.BoundType is not { } parameter || this.PendingAssociatedType(this.ContractType(parameter, path.Scope, self)))
                            {
                                input = syntax;
                                break;
                            }
                        }
                    }

                    this.RecordInferenceProblem(path, this.ContractType(waitingResult, path.Scope, self), match.State == RequirementMatchState.Pending ? InferenceProblem.Input : match.State == RequirementMatchState.Ambiguous ? InferenceProblem.Ambiguous : InferenceProblem.Missing, requirement, input);
                }

                if (match.State != RequirementMatchState.Unique || match.Member!.Declaration is not FunctionKoto implementation ||
                    requirement.BoundSymbol!.Type is not { } required || match.Member.Type is not { } actual)
                {
                    continue;
                }

                this.collectingAssociated = run.Frozen; // the requirement's Type reads none of the definition's bindings (C4 ruling 11)
                required = this.ContractType(required, path.Scope, self);
                this.collectingAssociated = run;
                var written = this.MemberType(this.AsWritten(implementation.ReturnType, this.scopes[implementation], actual), match.Selection.DeclaringType)!;
                actual = this.ContractType(this.MemberType(actual, match.Selection.DeclaringType)!, path.Scope);
                run.Pairs.Clear();
                run.Source = implementation;
                run.Requirement = identity.Symbol;
                this.ExtractAssociatedEvidence(run, path, required, actual, written, null);
            }
        }

        foreach (var path in batch.Paths)
        {
            if (path != path.RootPath || path.InheritedFrom is not null)
            {
                continue;
            }

            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (!this.InferenceHole(path, identity, path.Use) || !run.Evidence.TryGetValue(identity.Symbol, out var evidence))
                {
                    continue;
                }

                // Per binding (its root path and identity), never per associated symbol: `C<i32> and C<bool>` are two bindings.
                var binding = this.AssociatedBindingOf(path, identity)!;
                var index = this.inferenceBindingIndex[binding];
                if (run.Owner >= 0 && index != run.Owner)
                {
                    continue;
                }

                if (!run.Usable.TryGetValue(binding, out var usable))
                {
                    run.Usable.Add(binding, usable = new());
                }

                foreach (var source in evidence)
                {
                    if (run.Owner < 0 && !this.SourceOf(index, source.Path, source.Requirement))
                    {
                        // The source list's contract (DiscoverValues): a position of the definition's run outside its binding's sources is
                        // one no own run of the binding collects.
                        throw new InvalidOperationException("A position lies outside its binding's sources (InternalInvariant; R7-C4).");
                    }

                    // SPEC 8.5.5 Completion: evidence from another path is usable when the destination's premises prove the source's; the
                    // proof is one of the source's reads, under the run's hold (design §3.1, B3).
                    if (this.SameConformanceType(source.Identity.Contract.Type!, identity.Contract.Type!, path.Scope, path.Use) &&
                        this.ProveConformanceConditions(source.Path, this.SelfType(path.Type), path.Scope, path.Use) == ConstraintProof.Proven)
                    {
                        usable.Add((source, this.ContractType(source.Type, path.Scope)));
                    }
                }
            }
        }
    }

    // Publication (the request's end): the binding is Value(v | {}); its evidence sources the positions of the definition's run (made after
    // every root state, C4 ruling 12) supplying it, read against its root state's own run, collected again (ruling 15 (a)).
    private void PublishInferred(InferenceEntry entry, AssociatedInference definition, int empty)
    {
        var binding = entry.Binding;
        var answer = this.inferenceValues[(binding, empty)];
        var index = this.inferenceBindingIndex[binding];
        var own = this.InferenceRun(entry.Batch, this.HoldWith(empty, index, null), index);
        if (definition.Usable.TryGetValue(binding, out var usable) && own.Usable.TryGetValue(binding, out var owned))
        {
            for (var i = 0; i < owned.Count; i++)
            {
                var (source, type) = owned[i];
                if (!this.SuppliesInBoth(owned, i, usable))
                {
                    continue;
                }

                if (binding.Inferred is not null && !this.SameCompleteType(binding.Inferred, type))
                {
                    binding.InferenceConflict = true;
                    binding.Problem = InferenceProblem.Conflict;
                    binding.ProblemSource = source.Source;
                    binding.ProblemDetail = null;
                }

                binding.Inferred ??= type;
                binding.InferenceSource ??= source.Source;
                binding.EvidenceSources.Add(source);
            }
        }

        entry.Batch.ReturnRun(own);
        if (answer.State == HeldState.Conflict && !binding.InferenceConflict)
        {
            throw new InvalidOperationException("A conflict of Value(v | {}) has no two supplying positions (InternalInvariant; R7-C4).");
        }
    }

    // SPEC 8.3.3 Limits ("the Proven and Error results it gave stand"; design §3.4; the owner's O-484 (2 x)): past OQ2's items the
    // component is decided without its held states, each binding by its own run under the hold of every binding of the component, where
    // a source reading the component is pending and supplies nothing. A binding whose Reach holds no other binding of the component is its
    // value there (a Type, missing or a conflict, as Value(v | {}) gives it); another is a conflict when two supplying sources differ (an
    // Error that stands), else a Resource failure (InferenceProblem.Limit). The component's one Resource record is decided here, where
    // the limit is reached (C4 ruling 17 B2): at the conformance use of its least definition by full name (SPEC 8.3.3: "may depend on
    // declaration names"), every Resource binding's ProblemDetail; VerifyConformance writes it at its single exit, on every path of that
    // conformance's check (C4 ruling 19; the use is the conformance's declaration: a record written here would fail it before its
    // verification, and the conformance's own Error would be lost), every Resource binding's use rests on it, and a standing binding's
    // Error stays its conformance's record (SPEC 23.1.2). A run records no problem here; every state's runs are back in the pools
    // (ruling 15 (a)), and each binding's run goes back after it.
    private void DecideAtLimit()
    {
        // The hold of every binding, formed in order-number order (each member newer than the last, one node each; in entry order each
        // older member formed every newer node again: O(m^2) nodes, 13.6 GB on the 10,000-definition ring), the never-held ones last.
        var all = this.inferenceEmptyHold;
        var held = this.inferenceHoldEntry.Count;
        for (var o = 0; o < held; o++)
        {
            all = this.HoldAdd(all, this.inferenceHoldEntry[o]);
        }

        for (var i = 0; i < this.inferenceBindings.Count; i++)
        {
            if (this.inferenceBindingIndex[this.inferenceBindings[i].Binding] is var index && this.inferenceHoldOrder[index] < 0)
            {
                all = this.HoldAdd(all, index);
            }
        }

        BoundConformancePath? least = null;
        string? leastName = null;
        for (var i = 0; i < this.inferenceBindings.Count; i++)
        {
            var (binding, batch, path, _) = this.inferenceBindings[i];
            var index = this.inferenceBindingIndex[binding];
            var run = this.InferenceRun(batch, all, index);
            if (run.Usable.TryGetValue(binding, out var usable))
            {
                for (var u = 0; u < usable.Count; u++)
                {
                    var (source, type) = usable[u];
                    if (!this.SuppliesInBoth(usable, u, usable))
                    {
                        continue;
                    }

                    if (binding.Inferred is not null && !this.SameCompleteType(binding.Inferred, type))
                    {
                        binding.InferenceConflict = true;
                        binding.Problem = InferenceProblem.Conflict;
                        binding.ProblemSource = source.Source;
                        binding.ProblemDetail = null;
                    }

                    binding.Inferred ??= type;
                    binding.InferenceSource ??= source.Source;
                    binding.EvidenceSources.Add(source);
                }
            }

            batch.ReturnRun(run);
            if (!binding.InferenceConflict && this.ReachesAnotherBinding(index))
            {
                binding.Inferred = null;
                binding.InferenceSource = null;
                binding.EvidenceSources.Clear();
                binding.Problem = InferenceProblem.Limit;
                var name = FullName(path.RootPath.Type.Declaration);
                if (least is null || (StringComparer.Ordinal.Compare(name, leastName) is var order && (order < 0 ||
                    (order == 0 && StringComparer.Ordinal.Compare(path.Type.Declaration.CodeContext.Kotonoha.Name, least.Type.Declaration.CodeContext.Kotonoha.Name) < 0))))
                {
                    (least, leastName) = (path.RootPath, name);
                }
            }
        }

        if (least is null)
        {
            return; // every binding is decided (a value, missing or a conflict): no judgment depends on the analysis past the limit
        }

        for (var i = 0; i < this.inferenceBindings.Count; i++)
        {
            if (this.inferenceBindings[i].Binding is { Problem: InferenceProblem.Limit } limited)
            {
                limited.ProblemDetail = least.Use; // the use holding the component's one record (VerifyConformance)
            }
        }

        // A definition's full name: the enclosing declarations' names from the outermost, joined by `.` (below every identifier
        // character, so the ordinal order is the names' order part by part, a shorter path first): `GA.R0` before `GB.R0` (C4 ruling 17
        // C1), whichever is declared first; the module's name breaks a tie.
        static string FullName(Koto declaration)
        {
            var name = ((DeclarationContainerKoto)declaration).Name;
            for (var outer = declaration.Parent; outer is not null; outer = outer.Parent)
            {
                if (outer is DeclarationContainerKoto { Parent: not null, Name.Length: > 0 } enclosing)
                {
                    name = enclosing.Name + "." + name;
                }
            }

            return name;
        }
    }

    // Holds (C4 ruling 13 (3); one owner of the hold form): a hold is a set of the component's canonical entries, stored as its parent plus
    // its newest member, the parent being the hold without that member. A member's order number is given when it is first held
    // (inferenceHoldOrder), and each hold is interned by (parent, newest), so a set is one id whichever order formed it (the key compares
    // ids) and a hold that grows by a binding held for the first time, or by one newer than its members, is one node over its parent
    // (structural sharing). Adding an older member, or dropping members (HoldMeet), forms again only the nodes of the newer members.
    private int HoldNode(int parent, int top)
    {
        if (!this.inferenceHoldIds.TryGetValue((parent, top), out var id))
        {
            id = this.inferenceHolds.Count;
            this.inferenceHolds.Add((parent, top));
            this.inferenceHoldIds.Add((parent, top), id);
        }

        return id;
    }

    // `hold` with the binding `index` (when >= 0) and every binding of `batch` (when given).
    private int HoldWith(int hold, int index, AssociatedInference? batch)
    {
        if (index >= 0)
        {
            hold = this.HoldAdd(hold, index);
        }

        for (var x = batch?.FirstEntry ?? 0; batch is not null && x < batch.EntryEnd; x++)
        {
            hold = this.HoldAdd(hold, this.inferenceBindingIndex[this.inferenceBindings[x].Binding]);
        }

        return hold;
    }

    private int HoldAdd(int hold, int index)
    {
        var order = this.inferenceHoldOrder[index];
        if (order < 0)
        {
            this.inferenceHoldOrder[index] = order = this.inferenceHoldEntry.Count;
            this.inferenceHoldEntry.Add(index);
        }

        var work = this.inferenceHoldWork;
        work.Clear();
        var at = hold;
        while (this.inferenceHolds[at].Top > order)
        {
            work.Add((this.inferenceHolds[at].Top, at));
            at = this.inferenceHolds[at].Parent;
        }

        if (this.inferenceHolds[at].Top == order)
        {
            return hold;
        }

        at = this.HoldNode(at, order);
        for (var i = work.Count - 1; i >= 0; i--)
        {
            at = this.HoldNode(at, work[i].Top);
        }

        return at;
    }

    // `hold` n Reach(`index`) (design §3.3: the key of a held state): the members below the lowest one outside Reach stay shared.
    private int HoldMeet(int hold, int index)
    {
        if (this.ReachCoversComponent(index))
        {
            return hold;
        }

        var work = this.inferenceHoldWork;
        work.Clear();
        var dropped = -1;
        for (var at = hold; this.inferenceHolds[at].Top >= 0; at = this.inferenceHolds[at].Parent)
        {
            var top = this.inferenceHolds[at].Top;
            dropped = this.ValueReaches(index, this.inferenceHoldEntry[top]) ? dropped : work.Count;
            work.Add((top, at));
        }

        if (dropped < 0)
        {
            return hold;
        }

        var result = this.inferenceHolds[work[dropped].Hold].Parent;
        for (var i = dropped - 1; i >= 0; i--)
        {
            if (this.ValueReaches(index, this.inferenceHoldEntry[work[i].Top]))
            {
                result = this.HoldNode(result, work[i].Top);
            }
        }

        return result;
    }

    private bool HoldContains(int hold, int index)
    {
        var order = this.inferenceHoldOrder[index];
        var at = hold;
        while (order >= 0 && this.inferenceHolds[at].Top > order)
        {
            at = this.inferenceHolds[at].Parent;
        }

        return order >= 0 && this.inferenceHolds[at].Top == order;
    }

    internal enum HeldState : byte
    {
        Type,
        Pending,
        Missing,
        Conflict,

        // A state under evaluation (HeldValue's entry): read again only by a read outside its reader's Reach.
        Open,
    }

    // A held state's answer (design §3.1): a Type, or pending, missing or a conflict.
    internal readonly record struct HeldAnswer(BoundType? Type, HeldState State);

    // A binding of the component under request, numbered for holds: its batch, root path and identity.
    internal readonly record struct InferenceEntry(AssociatedBinding Binding, AssociatedInference Batch, BoundConformancePath Path, BoundRequirement Identity);
}
