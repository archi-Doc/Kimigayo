// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The associated-inference owner's demand side (R7-C4, PLAN G172; design Revision 2 §3.1-§3.4; the owner's O-484 (1 a, 2 x)): how the pass
// determines the inferred bindings and publishes them.
// - Inputs: discovery's components in emission order (Binding.AssociatedInference.Discovery), each inferring definition's paths.
// - Outputs: each hole's binding (AssociatedBinding.Inferred, InferenceConflict, Problem, EvidenceSources), published when its component's
//   request ends; then the paths' AssociatedStorage and InferenceStorage, and the inherited paths' inference.
// - The rule (SPEC 8.5.5, 8.3.3 Finality): Value(v | P) is pending when v is held; else a source position supplies its Type when the
//   collection with v held and the collection with v's definition's bindings held give it the same complete Type (HeldValue). A
//   collection under a hold is a run (a batch's run object, InferenceRun): identification, extraction and the usable positions, every
//   read of another binding of the component answered under the run's hold (ResolveAssociated), every answer Provisional.
// - Invariants: one request per component, none nested in another; a request's tables (values, runs, holds) are request-local and
//   dropped at its end (capacity kept); a binding is written once, at its request's end; problems are recorded by the root's frozen
//   run (the hold of the definition's bindings, today's batch hold), never by a held run.
// - Prohibitions: no held answer outside its request; no read of a later component's binding (the validator); no identification cached
//   inside a request (a held answer is Provisional).
public sealed partial class Binding
{
    private readonly Dictionary<AssociatedBinding, int> inferenceBindingIndex = new(ReferenceEqualityComparer.Instance);

    private readonly List<InferenceEntry> inferenceBindings = new();

    private readonly List<int> inferenceHoldArena = new();

    private readonly List<HoldKey> inferenceHolds = new();

    private readonly Dictionary<(AssociatedBinding Binding, int Hold), HeldAnswer> inferenceValues = new();

    private readonly Dictionary<(AssociatedInference Batch, int Hold), AssociatedInference> inferenceRuns = new();

    private Dictionary<HoldKey, int>? inferenceHoldIds;

    // The held states the requests evaluated in this compilation (design §6, D2-C3: the cost per state is the time over this count).
    internal int InferenceStates { get; private set; }

    // The component whose request is open, or -1.
    private int inferenceRequest = -1;

    private int inferenceEmptyHold;

    private void PrepareAssociatedInference()
    {
        foreach (var path in this.impls.InPass.Paths)
        {
            foreach (var identity in path.Contract.Contract!.AssociatedStorage)
            {
                if (this.InferenceHole(path, identity))
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
        this.inferenceRuns.Clear();
        this.inferenceBindingIndex.Clear();
        this.inferenceBindings.Clear();
        this.inferenceHoldArena.Clear();
        this.inferenceHolds.Clear();
        (this.inferenceHoldIds ??= new(new HoldComparer(this.inferenceHoldArena))).Clear();
        for (var b = start; b < end; b++)
        {
            var batch = this.inferenceComponents[b];
            foreach (var path in batch.Paths)
            {
                if (path != path.RootPath || path.InheritedFrom is not null)
                {
                    continue;
                }

                foreach (var identity in path.Contract.Contract!.AssociatedStorage)
                {
                    if (this.InferenceHole(path, identity) && this.AssociatedBindingOf(path, identity) is { } binding &&
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
        }

        this.BindBeforeRequest(start, end);
        this.inferenceRequest = component;
        var empty = this.InternHold(this.inferenceHoldArena.Count);
        try
        {
            for (var i = 0; i < this.inferenceBindings.Count; i++)
            {
                this.HeldValue(this.inferenceBindings[i].Binding, empty);
            }

            for (var i = 0; i < this.inferenceBindings.Count; i++)
            {
                this.PublishInferred(this.inferenceBindings[i], empty);
            }
        }
        finally
        {
            this.inferenceRequest = -1;
            for (var b = start; b < end; b++)
            {
                var batch = this.inferenceComponents[b];
                batch.Published = true;
                batch.RunsRented = 0;
            }
        }
    }

    // Value(v | P) (design §3.1): pending when v is held; else a source position supplies its Type when the run with v held and the run
    // with v's definition's bindings held give it the same complete Type; none: missing; two Types: conflict; else the Type.
    private HeldAnswer HeldValue(AssociatedBinding binding, int hold)
    {
        var index = this.inferenceBindingIndex[binding];
        if (this.HoldContains(hold, index))
        {
            return new(null, HeldState.Pending);
        }

        if (this.inferenceValues.TryGetValue((binding, hold), out var known))
        {
            return known;
        }

        var entry = this.inferenceBindings[index];
        var own = this.HoldWith(hold, index, null);
        var frozen = this.HoldWith(hold, -1, entry.Batch);
        var ownRun = this.InferenceRun(entry.Batch, own);
        var frozenRun = this.InferenceRun(entry.Batch, frozen);
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
            foreach (var path in this.inferenceComponents[b].Paths)
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

    // A collection of `batch`'s evidence under `hold` (a run), once per request: identification, extraction and each hole's usable
    // positions (SPEC 8.5.5 Completion: the source path's premises proven under the run's hold), with every read of another binding of
    // the component answered under the hold (ResolveAssociated).
    private AssociatedInference InferenceRun(AssociatedInference batch, int hold)
    {
        if (this.inferenceRuns.TryGetValue((batch, hold), out var run))
        {
            return run;
        }

        run = batch.RentRun();
        run.Hold = hold;
        run.Records = hold == this.HoldWith(this.inferenceEmptyHold, -1, batch); // the root's frozen run records the problems

        this.inferenceRuns[(batch, hold)] = run;
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
                hasHole |= this.InferenceHole(path, identity);
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
                if (identity.Contract != path.Contract || identity.Symbol.Declaration is not FunctionKoto requirement)
                {
                    continue;
                }

                this.BindHeader(identity.Symbol);
                var match = this.IdentifyRequirement(path, identity);
                if (match.State != RequirementMatchState.Unique && identity.Symbol.Type is { } waitingResult)
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

                required = this.ContractType(required, path.Scope, self);
                var written = this.MemberType(this.AsWritten(implementation.ReturnType, this.scopes[implementation], actual), match.Selection.DeclaringType)!;
                actual = this.ContractType(this.MemberType(actual, match.Selection.DeclaringType)!, path.Scope);
                run.Pairs.Clear();
                run.Source = implementation;
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
                if (!this.InferenceHole(path, identity) || !run.Evidence.TryGetValue(identity.Symbol, out var evidence))
                {
                    continue;
                }

                // Per binding (its root path and identity), never per associated symbol: `C<i32> and C<bool>` are two bindings.
                var binding = this.AssociatedBindingOf(path, identity)!;
                if (!run.Usable.TryGetValue(binding, out var usable))
                {
                    run.Usable.Add(binding, usable = new());
                }

                foreach (var source in evidence)
                {
                    // SPEC 8.5.5 Completion: evidence from another path is usable when the destination's premises prove the source's; the
                    // proof is one of the source's reads, under the run's hold (design §3.1, B3).
                    if (this.SameConformanceType(source.Identity.Contract.Type!, identity.Contract.Type!, path.Scope) &&
                        this.ProveConformanceConditions(source.Path, this.SelfType(path.Type), path.Scope) == ConstraintProof.Proven)
                    {
                        usable.Add((source, this.ContractType(source.Type, path.Scope)));
                    }
                }
            }
        }
    }

    // Publication (the request's end): the binding is Value(v | {}); its evidence sources the root's frozen run's positions supplying it.
    private void PublishInferred(InferenceEntry entry, int empty)
    {
        var binding = entry.Binding;
        var answer = this.inferenceValues[(binding, empty)];
        var frozen = this.InferenceRun(entry.Batch, this.HoldWith(empty, -1, entry.Batch));
        var own = this.InferenceRun(entry.Batch, this.HoldWith(empty, this.inferenceBindingIndex[binding], null));
        if (frozen.Usable.TryGetValue(binding, out var usable) && own.Usable.TryGetValue(binding, out var owned))
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

        if (answer.State == HeldState.Conflict && !binding.InferenceConflict)
        {
            throw new InvalidOperationException("A conflict of Value(v | {}) has no two supplying positions (InternalInvariant; R7-C4).");
        }
    }

    // Holds: sorted lists of the component's binding indices interned in the request's arena; a hold's id indexes inferenceHolds.
    private int InternHold(int start)
    {
        var length = this.inferenceHoldArena.Count - start;
        var key = new HoldKey(start, length);
        if (this.inferenceHoldIds!.TryGetValue(key, out var id))
        {
            this.inferenceHoldArena.RemoveRange(start, length);
            return id;
        }

        id = this.inferenceHolds.Count;
        this.inferenceHolds.Add(key);
        this.inferenceHoldIds.Add(key, id);
        if (length == 0)
        {
            this.inferenceEmptyHold = id;
        }

        return id;
    }

    // `hold` with the binding `index` (when >= 0) and every binding of `batch` (when given), interned.
    private int HoldWith(int hold, int index, AssociatedInference? batch)
    {
        var arena = this.inferenceHoldArena;
        var start = arena.Count;
        var (from, length) = this.inferenceHolds[hold];
        for (var i = 0; i < length; i++)
        {
            arena.Add(arena[from + i]);
        }

        if (index >= 0)
        {
            arena.Add(index);
        }

        if (batch is not null)
        {
            for (var i = 0; i < this.inferenceBindings.Count; i++)
            {
                if (ReferenceEquals(this.inferenceBindings[i].Batch, batch))
                {
                    arena.Add(this.inferenceBindingIndex[this.inferenceBindings[i].Binding]);
                }
            }
        }

        arena.Sort(start, arena.Count - start, null);
        var write = start;
        for (var read = start; read < arena.Count; read++)
        {
            if (write == start || arena[write - 1] != arena[read])
            {
                arena[write++] = arena[read];
            }
        }

        arena.RemoveRange(write, arena.Count - write);
        return this.InternHold(start);
    }

    private bool HoldContains(int hold, int index)
    {
        var (start, length) = this.inferenceHolds[hold];
        return this.inferenceHoldArena.BinarySearch(start, length, index, null) >= 0;
    }

    internal enum HeldState : byte
    {
        Type,
        Pending,
        Missing,
        Conflict,
    }

    // A held state's answer (design §3.1): a Type, or pending, missing or a conflict.
    internal readonly record struct HeldAnswer(BoundType? Type, HeldState State);

    // A binding of the component under request, numbered for holds: its batch, root path and identity.
    internal readonly record struct InferenceEntry(AssociatedBinding Binding, AssociatedInference Batch, BoundConformancePath Path, BoundRequirement Identity);

    internal readonly record struct HoldKey(int Start, int Length);

    // A hold's identity is its sorted indices in the arena.
    private sealed class HoldComparer(List<int> arena) : IEqualityComparer<HoldKey>
    {
        public bool Equals(HoldKey x, HoldKey y)
        {
            if (x.Length != y.Length)
            {
                return false;
            }

            for (var i = 0; i < x.Length; i++)
            {
                if (arena[x.Start + i] != arena[y.Start + i])
                {
                    return false;
                }
            }

            return true;
        }

        public int GetHashCode(HoldKey key)
        {
            var hash = key.Length;
            for (var i = 0; i < key.Length; i++)
            {
                hash = (hash * 31) + arena[key.Start + i];
            }

            return hash;
        }
    }
}
