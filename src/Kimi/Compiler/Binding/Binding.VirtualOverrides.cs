// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A semantic slot is the original declaration and its complete declaring binding.
    // The checked inheritance path is retained separately; physical indices belong to generation.
    internal readonly record struct VirtualSlot(FunctionKoto Original, BoundType DeclaringType);

    internal readonly record struct VirtualOverride(VirtualSlot Slot, BoundType ImplementingType, BoundMemberPath? Path);

    private readonly VirtualOverrideRows virtualOverrides;
    // Single inheritance gives each original declaration one bound path per derived Type.
    // Duplicate checking and direct-base selection share this index; virtualOverrides retains the full contract.
    private readonly OverrideEntryRows overrideEntries;
    private readonly HashSet<BoundConstraint> virtualHeaderCommon = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BoundConstraint> virtualHeaderCandidate = new(ReferenceEqualityComparer.Instance);
    private readonly ScratchBuffers<BoundConstraint> virtualHeaderScratch = new();
    // One struct's virtual and override functions in member order while its VTableDef is filled.
    private readonly List<FunctionKoto> slotMembers = new();
    // An override problem's facts while its site records them (OverrideFacts, RecordOverride): emptied after each record, so a warm
    // rebind of a rejected program reuses it.
    private readonly List<ProblemFact> overrideFacts = [];

    // Header names must be independent of slot selection. Only premises common to the accessible
    // virtual group can supply an unqualified associated name; a qualified name needs no guess. The scope the header binds in: an
    // override's header layer holding those premises (SPEC 6.4.2 step 2), else the function's scope. The layer serves the header only:
    // the body's premises are the selected slot's, decided once at SettleVirtualOverridePremises, so a condition Refuted for the
    // instance is the override's one declaration error however it is spelled (SPEC 6.4.3; R7-C2c2 c2-4a; C2 ruling 164).
    private BindingScope PrepareVirtualHeaderPremises(FunctionKoto function, BindingScope scope)
    {
        if (!function.IsOverride || function.BoundSymbol?.Scope.Owner is not StructKoto { BaseClause: { } clause, BoundSymbol: { } symbol } structure ||
            this.BindType(clause, this.scopes[structure]) is null)
        {
            return scope;
        }

        var self = this.SelfType(symbol);
        if (AdtDef.Base(this.types, self) is not { } baseType)
        {
            return scope;
        }

        var selection = this.LookupMember(baseType, function.Name, scope, LookupRole.Value, self);
        if (selection.Pending || selection.DeclaringType?.Symbol is null)
        {
            return scope;
        }

        var header = scope.HeaderPremises ??= new(function) { Parent = scope, Function = function };
        (header.Constraints ??= new()).Reset(); // Bound again when a pending associated Type reopens the header.

        // Lookup and base binding are complete before these buffers are used. Collecting the
        // finite declared facts performs substitution only: no header/body recursion.
        var first = true;
        var common = this.virtualHeaderCommon;
        var current = this.virtualHeaderCandidate;
        try
        {
            for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsVirtual: true } original || !this.Accessible(candidate, scope, receiverType: self))
                {
                    continue;
                }

                current.Clear();
                Collect(this.scopes[original]);
                if (candidate.ConditionalDeclaration is { } block)
                {
                    Collect(this.scopes[block]);
                }

                if (first)
                {
                    foreach (var fact in current)
                    {
                        common.Add(fact);
                    }

                    first = false;
                }
                else
                {
                    common.IntersectWith(current);
                }
            }

            var count = common.Count;
            var facts = this.virtualHeaderScratch.Rent(count);
            common.CopyTo(facts);
            common.Clear();
            current.Clear();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    // Judge against the derived declaration, before assuming any slot premise.
                    if (this.ProveConstraint(facts[i], function.BoundSymbol.Scope, function) is ConstraintProof.Proven or ConstraintProof.Unknown)
                    {
                        this.AddConstraintFact(header.Constraints!, facts[i]);
                    }
                }

                this.ExpandScopeContractPremises(header);
            }
            finally
            {
                this.virtualHeaderScratch.Return(facts, clearArray: true);
            }
        }
        finally
        {
            common.Clear();
            current.Clear();
        }

        return header;

        void Collect(BindingScope source)
        {
            if (source.Constraints is not { } environment)
            {
                return;
            }

            foreach (var fact in environment.DirectFacts)
            {
                var bound = this.StoredConstraint(fact, selection.DeclaringType);
                // Closed propositions are obligations, not assumptions (SPEC 8.3.3).
                if (!bound.HasUnresolved && bound.Kind != ConstraintKind.Error && this.DependentConstraint(bound))
                {
                    current.Add(bound);
                }
            }
        }
    }

    private enum OverrideFailureCause : byte
    {
        NoBase,
        NoInheritedGroup,
        NoMatchingInputs,
        AmbiguousInputs,
        DuplicateSlot,
        ExternalName,
        ResultType,
        OriginBinders,
        Condition,
    }

    // Correspondence alone is not a verified implementation or permission to execute it.
    internal bool TryGetVirtualOverride(FunctionKoto function, out VirtualOverride result)
    {
        result = default;
        return (this.IsRunning || this.Result != default) && this.virtualOverrides.TryGetValue(function, out result);
    }

    // Closed descriptor bindings use the public premises, without assuming those premises from the body scope.
    internal ConstraintProof VirtualApplicability(VirtualSlot slot, QuerySite site)
    {
        if (slot.DeclaringType.ContainsParameter)
        {
            return ConstraintProof.Unknown;
        }

        var original = slot.Original;
        var scope = this.ModuleScope(original);
        return CombineProof(
            this.CheckConstraints(original.TypeConstraints, SubstitutionOf(original, slot.DeclaringType, [], default), scope, site),
            this.ProveMemberConditions(original.BoundSymbol!, slot.DeclaringType, scope, site),
            true);
    }

    // Called by the common call-plan setter (CallPlan.Set) on the Binding its writer passes, including instantiated calls and Function Item
    // contexts.
    // Target and its defaults always remain the original public contract.
    internal void SelectVirtualCall(CallPlan call, FunctionKoto original)
    {
        var declaring = call.DeclaringType ?? this.SelfType(original.BoundSymbol!.Scope.Owner.BoundSymbol!);
        call.VirtualSlotType = declaring;
        call.VirtualBaseLookupType = null;
        call.VirtualImplementation = null;
        call.VirtualImplementingType = null;
        if (call.VirtualIsDirect && call.ReceiverOperation.Source is { } receiverSource && this.ViewTarget(call.ReceiverOperation.SourceType, receiverSource) is { } receiver)
        {
            call.VirtualBaseLookupType = AdtDef.Base(this.types, receiver);
            for (var current = call.VirtualBaseLookupType; current is not null; current = AdtDef.Base(this.types, current))
            {
                if (current.Symbol is { } symbol && OverrideEntryRows.TryGetValue((symbol, original), out var implementation) &&
                    this.virtualOverrides.TryGetValue(implementation, out var entry) && ReferenceEquals(this.MemberType(entry.Slot.DeclaringType, current), declaring))
                {
                    // An invalid implementation remains selected; later proof checks must not fall back.
                    call.VirtualImplementation = implementation;
                    call.VirtualImplementingType = current;
                    break;
                }

                if (ReferenceEquals(current, declaring))
                {
                    call.VirtualImplementation = original;
                    call.VirtualImplementingType = declaring;
                    break;
                }
            }
        }
    }

    private void PrepareVirtualOverrides()
    {
        foreach (var function in this.virtualDeclarations)
        {
            if (!function.IsOverride || function.BindingFailure != BindingFailure.None || function.BoundSymbol?.Scope.Owner is not StructKoto { BoundSymbol: { } typeSymbol } structure)
            {
                continue;
            }

            var self = this.SelfType(typeSymbol);
            // PLAN G651 a2 (F2, BU-161; IMPL 21.3.2): the base's instance, the lookup and the inputs' match form in a site frame at the
            // override where none is open, so a Type past the limit formed there is refused at it.
            var site = this.OpenQuerySite(function);
            var baseType = AdtDef.Base(this.types, self);
            if (structure.BindingState == BindingState.Invalid || AdtDef.BaseNotStored(this.types, self))
            {
                this.CloseQuerySite(site);
                this.CompleteDependent(function, structure);
                continue;
            }

            Koto? failedInput = this.FailedSignaturePart(function);
            for (var p = 0; failedInput is null && p < function.Parameters.Count; p++)
            {
                if (function.Parameters[p].Type is { BindingState: not BindingState.Resolved } input)
                {
                    failedInput = input;
                }
            }

            if (failedInput is not null)
            {
                this.CloseQuerySite(site);
                this.CompleteDependent(function, failedInput);
                continue;
            }

            var selection = baseType is null ? default : this.LookupMember(baseType, function.Name, this.scopes[function], LookupRole.Value, self, this.MemberPath(null, structure.BaseClause!, baseType));
            FunctionKoto? first = null;
            FunctionKoto? second = null;
            var count = 0;
            var pending = selection.Pending;
            for (var candidate = selection.Member; candidate is not null; candidate = candidate.Next)
            {
                if (candidate.Declaration is not FunctionKoto { IsVirtual: true } original || !this.Accessible(candidate, this.scopes[function], receiverType: self))
                {
                    continue;
                }

                if (original.BindingFailure != BindingFailure.None)
                {
                    this.AddPrerequisite(function, original);
                    pending = true;
                    continue;
                }

                if (selection.DeclaringType is { } declaring && this.VirtualInputsMatch(original, function, declaring))
                {
                    first ??= original;
                    if (count == 1)
                    {
                        second = original;
                    }

                    count++;
                }
            }

            this.CloseQuerySite(site);
            if (pending)
            {
                this.Fail(function, BindingFailure.Unsupported, true);
                continue;
            }

            if (count != 1)
            {
                var cause = count != 0 ? OverrideFailureCause.AmbiguousInputs
                    : baseType is null ? OverrideFailureCause.NoBase
                    : selection.Member is null ? OverrideFailureCause.NoInheritedGroup : OverrideFailureCause.NoMatchingInputs;
                this.RecordOverride(
                    function,
                    count == 0 ? BindingFailure.MissingOverrideTarget : BindingFailure.AmbiguousOverrideTarget,
                    this.OverrideFacts(function, cause, first ?? selection.Member?.Declaration as FunctionKoto, second));
                continue;
            }

            var slot = new VirtualSlot(first!, selection.DeclaringType!);
            this.virtualOverrides.Add(function, new(slot, self, selection.Path));
            var key = (typeSymbol, slot.Original);
            if (OverrideEntryRows.TryGetValue(key, out var previous))
            {
                this.RecordOverride(function, BindingFailure.DuplicateOverride, this.OverrideFacts(function, OverrideFailureCause.DuplicateSlot, slot.Original, previous));
                continue;
            }

            OverrideEntryRows.Add(key, function);
            if (IncompleteSignature(slot.Original) is { } prerequisite)
            {
                this.CompleteDependent(function, prerequisite); // The settle point skips it (SettleVirtualOverridePremises).
            }
        }
    }

    // SPEC 6.4.1-6.4.3, 6.2.1: every struct's dispatch shape once slot selection is complete (VTableDef, DECISIONS R5-M62).
    private void FillVTables()
    {
        foreach (var container in this.storageDeclarations)
        {
            if (container is StructKoto structure)
            {
                this.FillVTable(structure);
            }
        }
    }

    // A struct's table, its base's first; null when its declaration, its base's table, a virtual or an override did not resolve.
    private VTableDef? FillVTable(StructKoto structure)
    {
        var symbol = structure.BoundSymbol!;
        var table = symbol.Adt!.VTable ??= new();
        if (Current(table.Version, this.storageVersion))
        {
            return table.Resolved ? table : null;
        }

        table.Version = this.storageVersion;
        table.Resolved = false;
        // PLAN G651 a2 (F2, BU-161; IMPL 21.3.2): the base's instance forms in a site frame at the struct where none is open, so a Type past
        // the limit it forms is refused at the struct whose table reads it.
        var site = this.OpenQuerySite(structure);
        var parent = structure.BaseClause is null ? null : AdtDef.Declaration(AdtDef.Base(this.types, this.SelfType(symbol)));
        this.CloseQuerySite(site);
        var inherited = parent is null ? null : this.FillVTable(parent);
        if (structure.BindingState == BindingState.Invalid || (structure.BaseClause is not null && inherited is null))
        {
            return null;
        }

        this.slotMembers.Clear();
        this.AddSlotMembers(structure.Members);
        var own = 0;
        foreach (var function in this.slotMembers)
        {
            // A failed function, or one completed as the dependent of a failed part (CompleteDependent), did not resolve.
            if (function.BindingFailure != BindingFailure.None || function.BindingState == BindingState.Unresolved)
            {
                return null;
            }

            own += function.IsVirtual ? 1 : 0;
        }

        table.Refill(inherited, own);
        var next = table.BaseCount;
        foreach (var function in this.slotMembers)
        {
            if (function.IsVirtual)
            {
                table.Originals[next] = function.BoundSymbol!;
                table.Implementations[next++] = function.BoundSymbol!;
            }
            else
            {
                // Slot selection gave every resolved override its original in a base's own range.
                table.Implementations[VTableDef.Slot(this.virtualOverrides[function].Slot.Original.BoundSymbol!)] = function.BoundSymbol!;
            }
        }

        table.Resolved = true;
        return table;
    }

    // The virtual and override functions among members in member order, a conditional-conformance Block's in its place.
    private void AddSlotMembers(IReadOnlyList<Koto> members)
    {
        for (var i = 0; i < members.Count; i++)
        {
            if (members[i] is FunctionKoto function && (function.IsVirtual || function.IsOverride))
            {
                this.slotMembers.Add(function);
            }
            else if (members[i] is SyntaxFormKoto { Akind: KotoKind.ConditionalConformance } syntax && TryConditionalBlock(syntax, out var block))
            {
                this.AddSlotMembers(block.Items);
            }
        }
    }

    // The overrides' inherited conditions are decided once, when the proofs answer (after VerifyEarlyProjections; before it every
    // conformance proof is Unknown): a condition Refuted or Error for the slot's instance is a declaration error at the override,
    // since the slot never exists for that instance (SPEC 6.4.3), and no premise; a Proven or Unknown one is the body's
    // premise. The override's signature check reads those premises (its result names complete through them:
    // `U.Element` under the slot's `T is First`), so it and the Origin completion run here, after them, and the dispatch tables,
    // which read the overrides' failures, are filled last. Each override that selected its slot (not a duplicate, its signature
    // complete) is settled once per pass.
    private void SettleVirtualOverridePremises()
    {
        // D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id (the walk's order on a cold compile).
        for (var rows = this.virtualOverrides.Values.In(this.pass); rows.MoveNext();)
        {
            var (function, implementation) = ((FunctionKoto)rows.Node, rows.Current);
            var (slot, self) = (implementation.Slot, implementation.ImplementingType);
            if (function.BoundSymbol?.Scope.Owner.BoundSymbol is not { } typeSymbol || IncompleteSignature(slot.Original) is not null ||
                !OverrideEntryRows.TryGetValue((typeSymbol, slot.Original), out var entry) || !ReferenceEquals(entry, function))
            {
                continue;
            }

            var frame = this.BeginConsultation(function); // A site frame: the override's premises are its facts.
            this.PrepareVirtualOverridePremises(function, slot);
            this.DropConsultation(frame);
            this.CheckVirtualOverrideSignature(function, slot);

            // Transitional, R7-E5 (D-924; B's B2 ruling): the Origin completion compares the slot's Types as formed for the derived instance
            // through the override's inherited premises (BindContractType: MemberType, then ContractType in the override's scope), which a
            // refuted or Error premise above may have formed, so it runs at an override with no earlier failure, as the slot's first
            // failure keeps it, until R7-E5's per-problem inputs (probes s3, s6).
            if (function.BindingFailure == BindingFailure.None && !this.CompleteImplementationOrigins(function, slot.Original, [], [], out var incompatible, slot.DeclaringType, self))
            {
                if (incompatible is { } contract)
                {
                    this.FailExplained(ref this.originContracts, function, BindingFailure.OriginContract, contract);
                }
                else
                {
                    this.RecordOverride(function, BindingFailure.OverrideContractMismatch, this.OverrideFacts(function, OverrideFailureCause.OriginBinders, slot.Original));
                }
            }
        }

        this.FillVTables();
    }

    private void PrepareVirtualOverridePremises(FunctionKoto function, VirtualSlot slot)
    {
        var bodyScope = this.scopes[function];
        var premises = bodyScope.ImplementationPremises ??= new(function, function);
        premises.Parent = function.BoundSymbol!.Scope;
        premises.Function = function;
        // ResetPass owns this shared environment. Resetting premises would also erase the body's facts.
        premises.Constraints = bodyScope.Constraints ??= new();
        this.InheritVirtualConstraints(function, this.scopes[slot.Original], slot, premises);
        if (slot.Original.BoundSymbol!.ConditionalDeclaration is { } block)
        {
            this.InheritVirtualConstraints(function, this.scopes[block], slot, premises);
        }

        this.ExpandScopeContractPremises(premises);
    }

    private void InheritVirtualConstraints(FunctionKoto function, BindingScope source, VirtualSlot slot, BindingScope premises)
    {
        if (source.Constraints is not { } environment)
        {
            return;
        }

        foreach (var fact in environment.DirectFacts)
        {
            var bound = this.StoredConstraint(fact, slot.DeclaringType);
            // SPEC 6.4.3: a substituted premise Refuted or Error for the derived Type makes the override one declaration error at the
            // override (the slot never exists for that Type; the owner's O-370); it supplies no evidence. The body's Binding-time records
            // stand (SPEC 23.1.2); a rejected definition publishes no body (DECISIONS R4-M18). R7-C3 (F's C2 rulings 313, 271 (1), 255):
            // the refuted premise is the override's failed premise, it and its conjuncts in the one Failed state (FailPremise), so a body
            // proof that reads it rests on the override's record and none of it is evidence.
            var proof = this.ProveConstraint(bound, premises, function);
            this.AddConstraintFact(premises.Constraints!, bound);
            premises.Constraints!.AddStatement(bound, source.Owner, fact); // the slot's premise as written (SPEC 6.4.2 step 3, 8.11.4)
            if (proof is ConstraintProof.Refuted or ConstraintProof.Error)
            {
                this.FailConstraint(function, this.FindConformanceDiagnosticCause(source.Owner, fact));
                FailRefuted(bound);
            }
        }

        void FailRefuted(BoundConstraint part)
        {
            this.FailPremise(premises.Constraints!, part, function);
            if (part.Kind == ConstraintKind.And)
            {
                FailRefuted(part.Left!);
                FailRefuted(part.Right!);
            }
        }
    }

    private void ValidateVirtualOverrideConditions()
    {
        // D2b-d's d8 order: the source modules by ordinal, then the library, each by ascending id (the walk's order on a cold compile).
        for (var rows = this.virtualOverrides.Values.In(this.pass); rows.MoveNext();)
        {
            var (function, implementation) = ((FunctionKoto)rows.Node, rows.Current);

            // Transitional, R7-E5 (D-924; B's C2 (b)): the slot's failure skips the containing condition, since it carries a dependence the
            // proof's premises do not: an override its block's own premise error failed (ruling 297 (b)) would add a dependent
            // UnprovenOverrideCondition_Kd. So an independent condition record beside another failure (probes o5, o5r1, o5r2) and a
            // duplicate's, which has no premises (o4), are lost until R7-E5's per-problem inputs.
            if (function.BindingFailure != BindingFailure.None || function.BoundSymbol!.ConditionalDeclaration is not { } block)
            {
                continue;
            }

            var premises = this.scopes[function].ImplementationPremises!;
            var conditions = (SyntaxFormKoto)block.Operands[1];
            for (var i = 0; i < conditions.Operands.Length; i++)
            {
                var condition = (IsKoto)conditions.Operands[i];
                if (condition.BoundConstraint is not { } bound)
                {
                    this.CompleteDependent(function, condition);
                    break;
                }

                var proof = this.ProveConstraint(bound, premises, function);
                if (proof != ConstraintProof.Proven)
                {
                    if (proof == ConstraintProof.Error)
                    {
                        this.CompleteDependent(function, block);
                    }
                    else
                    {
                        var facts = this.OverrideFacts(function, OverrideFailureCause.Condition, implementation.Slot.Original, proof: proof);
                        facts.Add(new(ProblemFactKind.Related, this.FactSite(condition), 0));
                        this.RecordOverride(function, BindingFailure.UnprovenOverrideCondition, facts);
                    }

                    break;
                }
            }
        }
    }

    // Selection deliberately ignores results, external labels, Origins, conditions and body facts.
    private bool VirtualInputsMatch(FunctionKoto original, FunctionKoto implementation, BoundType declaring)
    {
        var overridden = new FnSig(original);
        var declared = new FnSig(implementation);
        if (overridden.InputCount != declared.InputCount || overridden.ReceiverIndex != declared.ReceiverIndex)
        {
            return false;
        }

        for (var i = 0; i < overridden.InputCount; i++)
        {
            var required = overridden.Input(i);
            var actual = declared.Input(i);
            if (required is null || actual is null)
            {
                return false;
            }

            if (i == overridden.ReceiverIndex)
            {
                if (required.Semantics != actual.Semantics)
                {
                    return false;
                }
            }
            else if (this.MemberType(required, declaring) is not { } bound || !this.SameOverrideType(bound, actual, overridden.Binder, declared.Binder, implementation))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 8.2.3, 11.x (R7-C3 C-f2): a slot's Type and the override's are compared in the implementation's view, each in its form there
    // (the judge's reader, CallableContractType), then by structure with each function's own parameters corresponding (SignatureEquals).
    private bool SameOverrideType(BoundType slot, BoundType actual, Koto slotBinder, Koto actualBinder, FunctionKoto implementation)
        => SignatureEquals(this.CallableContractType(slot, this.scopes[implementation]), this.CallableContractType(actual, this.scopes[implementation]), slotBinder, actualBinder);

    // SPEC 6.4.2 (3), 6.4.4: the override's external names and result against its slot's. The names are the two declarations' own, so a
    // name record stands beside any earlier failure of the override (probes x1, s2, s5n).
    private void CheckVirtualOverrideSignature(FunctionKoto implementation, VirtualSlot slot)
    {
        var original = slot.Original;
        for (var i = 0; i < original.Parameters.Count; i++)
        {
            if (original.Parameters[i].ExternalName != implementation.Parameters[i].ExternalName)
            {
                var facts = this.OverrideFacts(implementation, OverrideFailureCause.ExternalName, original);
                var span = implementation.Parameters[i].ExternalNameSpan;
                facts.Add(new(ProblemFactKind.Span, SourceRef.Of(implementation), ((long)span.Start << 32) | (uint)span.Length));
                this.RecordOverride(implementation, BindingFailure.OverrideContractMismatch, facts);
                return;
            }
        }

        var overridden = new FnSig(original);
        var declared = new FnSig(implementation);
        if (overridden.Result is { } required && declared.Result is { } actual && this.MemberType(required, slot.DeclaringType) is { } bound &&
            !this.SameOverrideType(bound, actual, overridden.Binder, declared.Binder, implementation))
        {
            // Transitional, R7-E5 (D-924; B's B2 ruling): the slot's result is formed for the derived instance through the override's
            // inherited premises (`T.(First).Element` under a refuted `Plain is First`, probe s4), so the mismatch keeps the override's
            // first failure, as a Fail does, until R7-E5's per-problem inputs; a declared result beside a refuted premise (s1, x2) is lost
            // until then.
            if (this.RecordedFailure(implementation) != BindingFailure.None)
            {
                this.Fail(implementation, BindingFailure.OverrideContractMismatch);
                return;
            }

            var facts = this.OverrideFacts(implementation, OverrideFailureCause.ResultType, original);
            var site = this.FactSite(implementation.ReturnType!);
            facts.Add(new(ProblemFactKind.Related, site, 0));
            facts.Add(new(ProblemFactKind.Expected, site, this.report.Keep(bound)));
            facts.Add(new(ProblemFactKind.Actual, site, this.report.Keep(actual)));
            this.RecordOverride(implementation, BindingFailure.OverrideContractMismatch, facts);
        }
    }

    // SPEC 6.4.2-6.4.4: an override problem's facts (ReportOverrideFailure renders them): the closed cause and a condition's proof (its
    // Reason row), the original or first candidate (Related, role 1) and a conflicting declaration (role 2); a site adds the record's own
    // location (role 0), a renamed external name (Span) or a result's two Types (Expected, Actual). A failure path only.
    private List<ProblemFact> OverrideFacts(FunctionKoto function, OverrideFailureCause cause, FunctionKoto? first, FunctionKoto? second = null, ConstraintProof proof = default)
    {
        var facts = this.overrideFacts;
        facts.Add(new(ProblemFactKind.Reason, SourceRef.Of(function), (long)cause | ((long)proof << 8)));
        if (first is not null)
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(first), 1));
        }

        if (second is not null)
        {
            facts.Add(new(ProblemFactKind.Related, this.FactSite(second), 2));
        }

        return facts;
    }

    // Its own record beside the function's other failures (SPEC 23.1.2), numbered by the one rule (RecordNumbered): a further failure of
    // one requirement takes the next condition. The kept list empties for the next record.
    private void RecordOverride(FunctionKoto function, BindingFailure failure, List<ProblemFact> facts)
    {
        this.RecordNumbered(function, failure, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(facts));
        facts.Clear();
    }

    /// <summary>The `virtualOverrides` family's one accessor (DECISIONS BU-220): a pass family, an override's verified slot, implementing
    /// Type and path, kept in the overriding function's module's NodeColumns at its SyntaxId (a present row always has its implementing
    /// Type, so the column's default is none); absent after a pass until the column's first write. Its members keep the names its readers
    /// use.</summary>
    private readonly struct VirtualOverrideRows(Binding binding)
    {
        internal FamilyRows<VirtualOverride> Values => new(binding, static (table, b) => table.Hir.Families.VirtualOverrides.Enumerate(b));

        internal VirtualOverride this[FunctionKoto function]
            => this.TryGetValue(function, out var result) ? result : throw new KeyNotFoundException($"`{function}` has no verified override in this pass.");

        internal void Add(FunctionKoto function, VirtualOverride entry)
        {
            ref var row = ref HirTables.Of(function).Families.VirtualOverrides.Ref(function.SyntaxId, binding);
            row = row == default ? entry : throw new ArgumentException($"`{function}` already has an override in this pass.", nameof(function));
        }

        internal VirtualOverride GetValueOrDefault(FunctionKoto function) => this.TryGetValue(function, out var result) ? result : default;

        internal bool TryGetValue(FunctionKoto function, out VirtualOverride result)
            => (result = function.SyntaxOwner is { } table ? table.Hir.Families.VirtualOverrides.Get(function.SyntaxId, binding) : default) != default;
    }

    /// <summary>The `overrideEntries` family's one accessor (DECISIONS BU-220): a Type's implementations of its originals' slots, a composite
    /// key (the Type symbol's declaration, the original) kept in the declaration's module's NodeColumns at its SyntaxId as a list scanned by
    /// the original (an identity key, C-382), the list kept and emptied in place by each pass (ResetVirtualDeclarations). Its members keep
    /// the names its readers use.</summary>
    private readonly struct OverrideEntryRows(Binding binding)
    {
        internal FamilyRows<List<(FunctionKoto Original, FunctionKoto Implementation)>?> Values
            => new(binding, static (table, _) => table.Hir.Families.OverrideEntries.Enumerate());

        internal static void Add((BindingSymbol Type, FunctionKoto Original) key, FunctionKoto implementation)
            => (HirTables.Of(key.Type.Declaration).Families.OverrideEntries.Ref(key.Type.Declaration.SyntaxId) ??= []).Add((key.Original, implementation));

        internal static bool TryGetValue((BindingSymbol Type, FunctionKoto Original) key, [NotNullWhen(true)] out FunctionKoto? implementation)
        {
            var row = key.Type.Declaration.SyntaxOwner is { } table ? table.Hir.Families.OverrideEntries.Get(key.Type.Declaration.SyntaxId) : null;
            for (var i = 0; row is not null && i < row.Count; i++)
            {
                if (ReferenceEquals(row[i].Original, key.Original))
                {
                    implementation = row[i].Implementation;
                    return true;
                }
            }

            implementation = null;
            return false;
        }
    }
}
