// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, bool> pendingAssociatedTypes = new(ReferenceEqualityComparer.Instance);

    private readonly List<ProblemFact> inferenceFacts = []; // An inference hole's facts, kept across passes (InferenceFacts).

    // The pass's inference sources, each path whose consumer list (BoundConformancePath.InferenceConsumers) this pass filled, in the order
    // first filled: InvalidateAssociatedEvidence's order, and the lists ResetAssociatedInference empties.
    private readonly List<BoundConformancePath> inferenceSources = new();
    private readonly Queue<BoundConformancePath> inferenceInvalidation = new();
    private AssociatedInference? collectingAssociated;
    private bool associatedInferenceReady;

    // The scaling rows' measure: the steps 8.5.5's walks of an inferred binding took past their memos in this compilation: the
    // extraction's positions (ExtractAssociatedEvidence), an evidence's pairs (InferredEvidence) and its joins (JoinEvidence).
    internal int InferredEvidenceWork { get; private set; }

    // Symbolic parameter projections are complete public Types; an unresolved projection on a nominal
    // declaration still needs a Type value. This query never searches bodies or introduces a proof. KimiLibraryShapeTest states with it
    // that no projection of the library's Type syntax is pending (PLAN G949 S1-r2, rule (c)).
    internal bool PendingAssociatedType(BoundType type)
    {
        if (this.pendingAssociatedTypes.TryGetValue(type, out var pending))
        {
            return pending;
        }

        pending = type.Kind == BoundTypeKind.AssociatedProjection && type.Components[0].Symbol?.Declaration is StructKoto or EnumKoto;
        for (var i = 0; !pending && i < type.Components.Count; i++)
        {
            pending = this.PendingAssociatedType(type.Components[i]);
        }

        this.pendingAssociatedTypes[type] = pending;
        return pending;
    }

    private static bool InferenceBinderInScope(Koto binder, BindingSymbol owner)
    {
        // A formed Function Type binds its own per-call Origins; these do not escape its method.
        if (binder is FunctionTypeKoto)
        {
            return true;
        }

        for (var current = owner.Declaration; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, binder))
            {
                return true;
            }
        }

        return false;
    }

    private static bool InferenceOriginInScope(BoundOrigin? origin, BindingSymbol owner, out Koto? invalid)
    {
        invalid = null;
        if (origin is null)
        {
            return true;
        }

        if (origin.Kind is OriginKind.Unbound or OriginKind.Inference || (origin.Binder is { } binder && !InferenceBinderInScope(binder, owner)))
        {
            invalid = origin.Binder;
            return false;
        }

        foreach (var operand in (BoundOrigin[])origin.Operands)
        {
            if (!InferenceOriginInScope(operand, owner, out invalid))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 8.5.5 Extraction: whether the walk follows a requirement's part into an implementation's part of the same structure: same
    // nominal constructions and Tuples (corresponding arguments), fixed arrays of equal length, concrete Semantics layers of the same
    // layer; never a Function Type, a projection or a Semantics application. The walk's one structural test, read by the walk
    // (ExtractAssociatedEvidence) and by an evidence's pairing below a written part (InferredEvidence).
    private static bool ExtractionFollows(BoundType required, BoundType actual)
        => required.Kind == actual.Kind && required.Symbol == actual.Symbol && required.Semantics == actual.Semantics && required.Components.Count == actual.Components.Count &&
            required.Kind is BoundTypeKind.Constructed or BoundTypeKind.Tuple or BoundTypeKind.FixedArray or BoundTypeKind.Semantics or BoundTypeKind.Array or BoundTypeKind.Dictionary or BoundTypeKind.Slice &&
            (required.Kind != BoundTypeKind.FixedArray || (required.Length == actual.Length && ReferenceEquals(required.LengthExpression, actual.LengthExpression)));

    private static bool InferenceLengthInScope(BoundLength? length, BindingSymbol owner, out Koto? invalid)
    {
        invalid = null;
        if (length is null)
        {
            return true;
        }

        if (length.Parameter is { } parameter && !InferenceBinderInScope(parameter.Scope.Owner, owner))
        {
            invalid = parameter.Declaration;
            return false;
        }

        return InferenceLengthInScope(length.Left, owner, out invalid) && InferenceLengthInScope(length.Right, owner, out invalid);
    }

    // The function a declaration result belongs to: the result Type's function, or the function itself (InferenceFacts' site).
    private static FunctionKoto DeclaringFunction(Koto at)
    {
        for (var node = at; ; node = node.Parent!)
        {
            if (node is FunctionKoto function)
            {
                return function;
            }
        }
    }

    private void ResetAssociatedInference()
    {
        this.associatedInferenceReady = false;
        this.collectingAssociated = null;
        this.completeTypeIdentities.Clear();
        this.inferenceInvalidation.Clear();
        foreach (var source in this.inferenceSources)
        {
            source.InferenceConsumers!.Clear();
        }

        this.inferenceSources.Clear();

        this.pendingAssociatedTypes.Clear();
        for (var batches = this.associatedInference.Values.In(this.pass); batches.MoveNext();)
        {
            var batch = batches.Current!;
            batch.State = 0;
            batch.Active = false;
            batch.Paths.Clear();
            batch.Normalized.Clear();
            batch.Holes.Clear();
            batch.Pairs.Clear();
            batch.CheckedTypes.Clear();
            batch.ProblemTypes.Clear();
            foreach (var evidence in batch.Evidence.Values)
            {
                evidence.Clear();
            }
        }
    }

    private bool InferenceHole(BoundConformancePath path, BoundRequirement identity)
        => path.InheritedFrom is null && this.AssociatedParameters(identity.Symbol.Declaration).Length == 0 &&
        this.AssociatedBindingOf(path.RootPath, identity) is { Candidates.Count: 0 } &&
        this.SpecificationPart(path, identity) is null;

    // SPEC 15.3.3, 15.4.4, 23.1.2: the explicit specification of an associated identity in the conformance's declaration or its
    // conditional block that fixes its Type, or that did not bind. An identity so specified is never an inference hole, a missing
    // binding rests on the clause, or on its specified Type when that fails instead (SettleSpecificationLinks), and a binding on an
    // inline cycle is reported there (SPEC 6.2, DECISIONS BU-128).
    private IsKoto? SpecificationPart(BoundConformancePath path, BoundRequirement identity)
    {
        var root = path.RootPath;
        if (root.Declaration.Parent is SyntaxFormKoto syntax && TryConditionalBlock(syntax, out var block))
        {
            for (var i = 0; i < block.Items.Count; i++)
            {
                if (Failed(block.Items[i]) is { } conditional)
                {
                    return conditional;
                }
            }
        }

        var container = (DeclarationContainerKoto)root.Type.Declaration;
        for (var i = 0; i < container.Members.Count; i++)
        {
            if (Failed(container.Members[i]) is { } member)
            {
                return member;
            }
        }

        return null;

        // A clause that fixes the Type (SPEC 8.5.1: conjunction elimination yields its identity atom, AssociatedIdentityType; `is i32 and
        // Copy` specifies i32), or one that did not bind and so cannot be told apart; a capability-only clause (`is Copy`) fixes nothing
        // and leaves the identity to inference.
        IsKoto? Failed(Koto item)
            => item is IsKoto { IsAssociatedConstraint: true } clause && this.SpecifiesContract(clause, root.RootContract) &&
                ReferenceEquals(clause.Left.BoundSymbol ?? AssociatedHead(clause)?.BoundSymbol, identity.Symbol) &&
                (clause.BindingFailure != BindingFailure.None || clause.BindingState != BindingState.Resolved ||
                    clause.BoundConstraint is null or { Kind: ConstraintKind.Error or ConstraintKind.Unresolved } ||
                    AssociatedIdentityType(clause.BoundConstraint) is not null)
                ? clause : null;
    }

    // SPEC 23.1.2: a conformance resting on a specification is derived from what has failed by publication: the clause, or its
    // specified Type when only that failed, such as a formation proven at the Definition deadline.
    private void SettleSpecificationLinks()
    {
        for (var i = 0; this.specificationLinks is { } links && i < links.Count; i++)
        {
            if (this.partPrerequisites.TryGetValue(links[i], out var part) && part is IsKoto { BindingFailure: BindingFailure.None, BindingState: not BindingState.Invalid, Right: { BindingState: BindingState.Invalid } specified })
            {
                this.partPrerequisites[links[i]] = specified;
            }
        }
    }

    // SPEC 8.5.5 Extraction (PLAN G1001): the one walk of an implementation's result, `actual` its normal form paired with the requirement's
    // `required`, `written` the same part as written (AsWritten). While `written` has `actual`'s shape it follows it component by component;
    // below a written part of another shape (a written projection normalized here to a structure) `written` stays that part and `anchor` the
    // requirement's part there. Each hole position reached is one evidence: the hole's normal part, with what the source wrote at the
    // position the evidence reads it from (Written) and the requirement's part there (Required: the hole itself, or the anchor's).
    private void ExtractAssociatedEvidence(AssociatedInference batch, BoundConformancePath path, BoundType required, BoundType actual, BoundType written, BoundType? anchor)
    {
        if (!this.ContainsInferenceHole(batch, path, required) || !batch.Pairs.Add((required, actual, written, anchor)))
        {
            return;
        }

        this.InferredEvidenceWork++;
        var source = batch.Source!;
        if (this.InferenceHoleOf(path, required) is { } identity)
        {
            batch.CheckedTypes.Clear();
            if (this.PendingAssociatedType(actual))
            {
                this.RecordInferenceProblem(path, required, InferenceProblem.Dependency, source);
                return;
            }

            if (!this.InferenceTypeInScope(actual, path.Type, batch, out var invalid))
            {
                this.RecordInferenceProblem(path, required, InferenceProblem.Scope, source, invalid);
                return;
            }

            if (!batch.Evidence.TryGetValue(identity.Symbol, out var evidence))
            {
                batch.Evidence.Add(identity.Symbol, evidence = new());
            }

            evidence.Add(new(path, identity, actual, source, written, anchor ?? required));
            return;
        }

        if (!ExtractionFollows(required, actual))
        {
            this.RecordInferenceProblem(path, required, InferenceProblem.Shape, source);
            return;
        }

        var aligned = anchor is null && written.Kind == actual.Kind && written.Symbol == actual.Symbol && written.Components.Count == actual.Components.Count;
        for (var i = 0; i < required.Components.Count; i++)
        {
            this.ExtractAssociatedEvidence(batch, path, required.Components[i], actual.Components[i], aligned ? written.Components[i] : written, aligned ? null : anchor ?? required);
        }
    }

    // SPEC 8.5.5, 8.6.3, 8.3.3 (PLAN G1001): an inferred binding read by a requirement's evidence is the same contract as the equal explicit
    // specification: every extraction position of every source (EvidenceSources) gives the binding's part as that source wrote it, formed in
    // this evidence (a projection whose route rests on a held obligation stays at its position, HeldPending): its written part formed, then
    // the requirement's part there paired with it down to the hole (the walk's own pairing and hole test, once per pair in this answer). A
    // position below a projection left in place gives none (pending). The image joins every position's part, identity with the binding's
    // part first (JoinEvidence), so neither the order of sources nor of positions matters; none from every position: pending.
    private BoundType? InferredEvidence(AssociatedBinding binding, AssociatedInference batch, out bool pending)
    {
        var normal = binding.Inferred!;
        BoundType? image = null;
        HashSet<(BoundType Required, BoundType Formed)>? read = null;
        Dictionary<(BoundType Left, BoundType Right), BoundType>? joined = null;
        foreach (var source in binding.EvidenceSources)
        {
            Read(source.Path, source.Identity, source.Required, this.ContractType(source.Written, source.Path.Scope));
        }

        pending = image is null;
        return image;

        void Read(BoundConformancePath path, BoundRequirement identity, BoundType required, BoundType formed)
        {
            if (!(read ??= new()).Add((required, formed)))
            {
                return;
            }

            this.InferredEvidenceWork++;
            if (this.InferenceHoleOf(path, required) is { } hole)
            {
                if (hole == identity)
                {
                    image = image is null ? formed : this.JoinEvidence(image, formed, normal, joined ??= new());
                }

                return;
            }

            if (ExtractionFollows(required, formed))
            {
                for (var i = 0; i < required.Components.Count; i++)
                {
                    if (this.ContainsInferenceHole(batch, path, required.Components[i]))
                    {
                        Read(path, identity, required.Components[i], formed.Components[i]);
                    }
                }
            }
        }
    }

    // SPEC 8.5.5: two positions' images of one inferred binding (`normal`, the binding) together, once per pair in an answer (`joined`):
    // a part identical to the binding's is the binding's, from either side; only then a part not of the binding's shape (a projection left
    // in place) is pending as a whole, and two shaped parts join by components.
    private BoundType JoinEvidence(BoundType left, BoundType right, BoundType normal, Dictionary<(BoundType Left, BoundType Right), BoundType> joined)
    {
        if (ReferenceEquals(left, normal) || ReferenceEquals(right, normal) || ReferenceEquals(left, right))
        {
            return ReferenceEquals(right, normal) ? right : left;
        }

        if (!Shaped(left) || !Shaped(right))
        {
            return Shaped(left) ? left : right;
        }

        if (joined.TryGetValue((left, right), out var known))
        {
            return known;
        }

        this.InferredEvidenceWork++;
        var count = normal.Components.Count;
        var components = this.RentTypes(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                components[i] = this.JoinEvidence(left.Components[i], right.Components[i], normal.Components[i], joined);
            }

            return joined[(left, right)] = this.types.Rebuild(left, components.AsSpan(0, count), left.Origin, (BoundOrigin[])left.OriginArguments);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }

        bool Shaped(BoundType part)
            => part.Kind == normal.Kind && part.Kind != BoundTypeKind.AssociatedProjection && part.Symbol == normal.Symbol && part.Components.Count == normal.Components.Count;
    }

    // The inference hole `type` names for `path` (SPEC 8.5.5: a missing associated Type of the definition, by its identity), or none: the
    // walk's, ContainsInferenceHole's and the evidence's one hole test.
    private BoundRequirement? InferenceHoleOf(BoundConformancePath path, BoundType type)
        => type.Kind == BoundTypeKind.AssociatedProjection && ReferenceEquals(type.Components[0].Symbol, path.Type) &&
            this.AssociatedInPath(path.RootPath, type.Components[0], type.Symbol!, type.Components.Count == 2 ? type.Components[1] : null, path.Scope) is { } identity &&
            this.InferenceHole(path, identity) ? identity : null;

    private bool InferenceTypeInScope(BoundType type, BindingSymbol owner, AssociatedInference batch, out Koto? invalid)
    {
        invalid = null;
        if (!batch.CheckedTypes.Add(type))
        {
            return true;
        }

        if (type.Kind is BoundTypeKind.Parameter or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication &&
            type.Symbol is { } parameter && !InferenceBinderInScope(parameter.Scope.Owner, owner))
        {
            invalid = parameter.Declaration;
            return false;
        }

        if (!InferenceOriginInScope(type.Origin, owner, out invalid) || !InferenceLengthInScope(type.LengthExpression, owner, out invalid))
        {
            return false;
        }

        foreach (var origin in (BoundOrigin[])type.OriginArguments)
        {
            if (!InferenceOriginInScope(origin, owner, out invalid))
            {
                return false;
            }
        }

        foreach (var length in type.LengthArguments)
        {
            if (!InferenceLengthInScope(length, owner, out invalid))
            {
                return false;
            }
        }

        foreach (var component in (BoundType[])type.Components)
        {
            if (!this.InferenceTypeInScope(component, owner, batch, out invalid))
            {
                return false;
            }
        }

        return true;
    }

    private void RecordInferenceProblem(BoundConformancePath path, BoundType required, InferenceProblem problem, FunctionKoto source, Koto? detail = null)
    {
        if (!this.collectingAssociated!.Records)
        {
            return; // R7-C4: only the root's frozen run records (a held run's problems are its hold's).
        }

        this.collectingAssociated.ProblemTypes.Clear();
        this.RecordInferenceProblemCore(path, required, problem, source, detail);
    }

    private bool ContainsInferenceHole(AssociatedInference batch, BoundConformancePath path, BoundType type)
    {
        var key = (path.RootPath, type);
        if (batch.Holes.TryGetValue(key, out var found))
        {
            return found;
        }

        found = this.InferenceHoleOf(path, type) is not null;
        for (var i = 0; !found && i < type.Components.Count; i++)
        {
            found = this.ContainsInferenceHole(batch, path, type.Components[i]);
        }

        batch.Holes[key] = found;
        return found;
    }

    private void RecordInferenceProblemCore(BoundConformancePath path, BoundType required, InferenceProblem problem, FunctionKoto source, Koto? detail)
    {
        if (!this.collectingAssociated!.ProblemTypes.Add(required))
        {
            return;
        }

        if (this.InferenceHoleOf(path, required) is { } identity)
        {
            var binding = this.AssociatedBindingOf(path.RootPath, identity)!;
            if (problem > binding.Problem)
            {
                binding.Problem = problem;
                binding.ProblemSource = source;
                binding.ProblemDetail = detail;
            }

            return;
        }

        // This diagnostic-only walk follows the required graph, including excluded structural positions.
        foreach (var component in (BoundType[])required.Components)
        {
            this.RecordInferenceProblemCore(path, component, problem, source, detail);
        }
    }

    private void InvalidateAssociatedEvidence()
    {
        foreach (var source in this.inferenceSources)
        {
            if (!source.IsVerified)
            {
                this.inferenceInvalidation.Enqueue(source);
            }
        }

        while (this.inferenceInvalidation.TryDequeue(out var source))
        {
            if (source.InferenceConsumers is not { } consumers)
            {
                continue;
            }

            foreach (var consumer in consumers)
            {
                if (consumer.IsVerified)
                {
                    consumer.IsVerified = false;
                    consumer.Invalid = true;
                    this.Invalidated(); // Under its source's own use it fails no node (the rounds' change signal).
                    consumer.Identity.IsVerified = false;
                    if (!ReferenceEquals(consumer.Use, source.Use))
                    {
                        // Transitional, R7-E5 (D-924): an owner over its failed part spelled as a failure (ruling 16 B1 (a)): the consumer's
                        // use rests on its invalidated source's use, its part prerequisite, until R7-E5's per-problem inputs.
                        this.AddPrerequisite(consumer.Use, source.Use);
                        this.Fail(consumer.Use, BindingFailure.UnprovenConstraint);
                    }

                    this.inferenceInvalidation.Enqueue(consumer);
                }
            }
        }
    }

    // SPEC 8.6.4: an inference hole's problem: its reason (Reason, an InferenceProblem row), the associated Type's declaration (Related,
    // role 0) with the identity's Contract instance (Instance), the declaration result it read (role 1), the undetermined input or the
    // binder outside the conformance scope (role 2) and another declaration result (role 3). The list is kept, and starts empty for each
    // hole, so a warm pass allocates none and no earlier hole's facts stay in front.
    private ReadOnlySpan<ProblemFact> InferenceFacts(BoundRequirement identity, AssociatedBinding binding)
    {
        var facts = this.inferenceFacts;
        facts.Clear();
        var at = this.FactSite(identity.Symbol.Declaration);
        facts.Add(new(ProblemFactKind.Reason, at, (long)binding.Problem));
        facts.Add(new(ProblemFactKind.Related, at, 0));
        facts.Add(new(ProblemFactKind.Instance, at, this.report.Keep(identity.Contract.Type!)));
        var source = binding.ProblemSource ?? binding.InferenceSource;
        if (source is not null)
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(source.ReturnType ?? source), 1));
        }

        if (binding.ProblemDetail is { } detail)
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(detail), 2));
        }

        if (binding.InferenceSource is { } first && !ReferenceEquals(first, source))
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(first.ReturnType ?? first), 3));
        }

        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(facts);
    }

    // An inference hole's record from its facts: the associated Type's identity and the reason as evidence, its related locations by role.
    private void ReportAssociatedInference(Koto use, Kimi.Diagnostics.DiagnosticCollection? store, int index, in BindingProblem problem)
    {
        var facts = this.report.Facts(index);
        var kind = (InferenceProblem)facts[0].Value;
        var reason = kind switch
        {
            InferenceProblem.Input => "the implementation input key depends on an undetermined associated Type",
            InferenceProblem.Shape => "the declared results have no permitted structural correspondence",
            InferenceProblem.Scope => "the declared result contains a Type, length or Origin binder outside the conformance scope",
            InferenceProblem.Dependency => "the declared result depends on an undetermined associated Type in this inference unit or a Type-value cycle",
            InferenceProblem.Conflict => "declaration results supply conflicting complete Types",
            _ => "no eligible declaration result determines this associated Type",
        };
        var declaration = this.compilation.SyntaxOf(facts[1].Site);
        var name = this.AssociatedIdentityText(facts[2], declaration);
        var related = new (string Role, Koto At, string? Label)[facts.Length - 2];
        related[0] = ("associated", declaration, name);
        for (var f = 3; f < facts.Length; f++)
        {
            var at = this.compilation.SyntaxOf(facts[f].Site);
            related[f - 2] = facts[f].Value switch
            {
                1 => ("result", at, "declared result of " + DeclaringFunction(at).Name),
                2 => (kind == InferenceProblem.Input ? "input" : "binder", at, kind == InferenceProblem.Input ? "undetermined input Type" : "binder outside the conformance scope"),
                _ => ("result", at, "other declaration result"),
            };
        }

        use.ReportIn(store, problem.Requirement, problem.Code, evidence: [name, reason], related: related, condition: problem.Condition);
    }

    // The pass's identification of a requirement in a path, kept on the path (BoundConformancePath.RequirementMatches) until the pass
    // ends; a pending one is identified again.
    private RequirementMatch IdentifyRequirement(BoundConformancePath path, BoundRequirement identity)
    {
        var identified = path.RequirementMatches ??= new();
        var cached = this.inferenceRequest < 0; // R7-C4: inside a request a key may read a held answer (Provisional): never cached.
        if (cached && MatchIndex(identified, identity) is var known and >= 0 && identified[known].Match.State != RequirementMatchState.Pending)
        {
            return identified[known].Match;
        }

        var self = this.ConformanceSelf(path);
        var selection = this.LookupMember(self, identity.Symbol.Name, path.Scope, LookupRole.Value, self);
        var requirement = (FunctionKoto)identity.Symbol.Declaration;
        BindingSymbol? selected = null;
        var matches = 0;
        var pending = selection.Pending;
        for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is not FunctionKoto implementation || !this.Accessible(candidate, path.Scope, receiverType: self))
            {
                continue;
            }

            this.BindHeader(candidate);
            var match = this.MatchesRequirement(requirement, implementation, self, path.Scope, selection);
            if (match is null && IncompleteSignature(implementation) is { } incomplete)
            {
                this.Consulted(incomplete); // SPEC 23.1.2: a pending match read the implementation's incomplete signature (IsDerived).
            }

            pending |= match is null;
            if (match == true)
            {
                matches++;
                selected = candidate;
            }
        }

        var state = selection.Ambiguous || matches > 1 ? RequirementMatchState.Ambiguous : pending ? RequirementMatchState.Pending : selected is null ? RequirementMatchState.Missing : RequirementMatchState.Unique;
        var result = new RequirementMatch(state, selected, selection);
        if (!cached)
        {
            return result;
        }

        if (MatchIndex(identified, identity) is var index and >= 0)
        {
            identified[index] = (identity, result); // The identification above may have stored one for the same requirement.
        }
        else
        {
            identified.Add((identity, result));
        }

        return result;

        static int MatchIndex(List<(BoundRequirement Requirement, RequirementMatch Match)> list, BoundRequirement identity)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Requirement == identity)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal enum RequirementMatchState : byte
    {
        Unique,
        Missing,
        Ambiguous,
        Pending,
    }

    internal enum InferenceProblem : byte
    {
        None,
        Shape,
        Input,
        Dependency,
        Scope,
        Conflict,
        Missing,
        Ambiguous,
    }

    internal readonly record struct RequirementMatch(RequirementMatchState State, BindingSymbol? Member, MemberSelection Selection);

    // An inference source, one extraction position (ExtractAssociatedEvidence): the hole's normal part (Type) of Source's result, what Source
    // wrote at the position the evidence reads it from (Written: at the hole, or the written part of another shape the hole lies in) and
    // the requirement's part there (Required).
    internal readonly record struct AssociatedEvidence(BoundConformancePath Path, BoundRequirement Identity, BoundType Type, FunctionKoto Source, BoundType Written, BoundType Required);

    // The `associatedInference` family's one accessor (DECISIONS BU-220): a conforming Type's batch is kept in its Type's declaration's
    // module at the declaration's SyntaxId (SPEC 8.6.1: a conformance is declared in its Type's declaration, ImplTable's store rule),
    // across passes: ResetAssociatedInference resets each in place, and PrepareAssociatedInference drops one no inference hole needs.
    internal readonly struct InferenceBatches(Binding binding)
    {
        // Every kept batch in G949 D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id.
        internal FamilyRows<AssociatedInference?> Values => new(binding, static (table, _) => table.Hir.Families.AssociatedInference.Enumerate());

        // The slots the family's columns hold over the compilation's modules (AssociatedMetrics' retained storage).
        internal int Capacity
        {
            get
            {
                var capacity = Slots(binding.Library.Kotonoha.Syntax);
                foreach (var module in binding.compilation.SourceModules)
                {
                    capacity += Slots(module.Syntax);
                }

                return capacity;

                static int Slots(SyntaxTable table) => table.Hir.Families.AssociatedInference.Used + table.Hir.Families.AssociatedInference.Free;
            }
        }

        // A conforming Type's batch, made at its first need.
        internal static AssociatedInference Batch(BindingSymbol type)
            => Own(HirTables.Of(type.Declaration).Families.AssociatedInference.Ref(type.Declaration.SyntaxId) ??= new(type), type)!;

        // An enumerated batch's row: present, so the write carves nothing.
        internal static void Remove(AssociatedInference batch) => HirTables.Of(batch.Type.Declaration).Families.AssociatedInference.Ref(batch.Type.Declaration.SyntaxId) = null;

        internal bool TryGetValue(BindingSymbol type, [NotNullWhen(true)] out AssociatedInference? batch)
            => (batch = Own(type.Declaration.SyntaxOwner is { } table ? table.Hir.Families.AssociatedInference.Get(type.Declaration.SyntaxId) : null, type)) is not null;

        // A declaration's row is its Type symbol's, the one a path names (a container's BoundSymbol): any other symbol of the declaration
        // reaching it is a broken contract (DECISIONS BU-220's symbol rule).
        private static AssociatedInference? Own(AssociatedInference? batch, BindingSymbol type)
            => batch is null || ReferenceEquals(batch.Type, type) ? batch
                : throw new InvalidOperationException($"`{type.Name}` reads the inference batch of `{batch.Type.Name}` (InternalInvariant; DECISIONS BU-220).");
    }

    internal sealed class AssociatedInference(BindingSymbol type)
    {
        internal BindingSymbol Type { get; } = type;

        internal byte State { get; set; }

        internal bool Active { get; set; }

        internal List<BoundConformancePath> Paths { get; } = new();

        internal Dictionary<BindingSymbol, List<AssociatedEvidence>> Evidence { get; } = new(ReferenceEqualityComparer.Instance);

        internal Dictionary<(BoundType Type, BindingScope Scope, BoundType? Self, bool Normalize, BindingSymbol? Contract), (BoundType Image, ResolutionHops? Hops)> Normalized { get; } = new();

        internal Dictionary<(BoundConformancePath Path, BoundType Type), bool> Holes { get; } = new();

        // The walk's visited positions (ExtractAssociatedEvidence): what each record keeps but the hole's normal part, the anchor included.
        internal HashSet<(BoundType Required, BoundType Actual, BoundType Written, BoundType? Anchor)> Pairs { get; } = new();

        // The implementation whose result the extraction in progress walks (AssociatedEvidence.Source).
        internal FunctionKoto? Source { get; set; }

        internal HashSet<BoundType> CheckedTypes { get; } = new(ReferenceEqualityComparer.Instance);

        internal HashSet<BoundType> ProblemTypes { get; } = new(ReferenceEqualityComparer.Instance);

        // Discovery (Binding.AssociatedInference.Discovery): the definitions the batch's written Types name, the inferring definitions it can
        // read, the definitions whose value its value-dependent projections read, and its Tarjan state and component.
        internal List<BindingSymbol> Heads { get; } = new();

        internal List<AssociatedInference> Reads { get; } = new();

        internal List<AssociatedInference> Dependent { get; } = new();

        internal int Index { get; set; }

        internal int Low { get; set; }

        internal int Cursor { get; set; }

        internal int Visit { get; set; }

        internal bool OnStack { get; set; }

        internal int Component { get; set; } = -1;

        // A run (R7-C4, Binding.AssociatedInference.Demand): the hold it collects under, whether it records the problems (the root's
        // frozen run), each hole's usable positions with their normal Types; the batch's pool of runs, rented per request.
        internal int Hold { get; set; }

        internal bool Records { get; set; }

        internal Dictionary<AssociatedBinding, List<(AssociatedEvidence Source, BoundType Type)>> Usable { get; } = new(ReferenceEqualityComparer.Instance);

        internal List<AssociatedInference> RunPool { get; } = new();

        internal int RunsRented { get; set; }

        // A run of this batch, from its pool (kept across passes), empty.
        internal AssociatedInference RentRun()
        {
            if (this.RunsRented == this.RunPool.Count)
            {
                this.RunPool.Add(new AssociatedInference(this.Type));
            }

            var run = this.RunPool[this.RunsRented++];
            run.State = 0;
            run.Normalized.Clear();
            run.Holes.Clear();
            run.Pairs.Clear();
            run.CheckedTypes.Clear();
            run.ProblemTypes.Clear();
            run.Source = null;
            run.Component = this.Component;
            foreach (var evidence in run.Evidence.Values)
            {
                evidence.Clear();
            }

            foreach (var usable in run.Usable.Values)
            {
                usable.Clear();
            }

            return run;
        }
    }
}
