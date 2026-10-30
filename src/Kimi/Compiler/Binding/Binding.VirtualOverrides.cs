// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A semantic slot is the original declaration and its complete declaring binding.
    // The checked inheritance path is retained separately; physical indices belong to generation.
    internal readonly record struct VirtualSlot(FunctionKoto Original, BoundType DeclaringType);

    internal readonly record struct VirtualOverride(VirtualSlot Slot, BoundType ImplementingType, BoundMemberPath? Path);

    private readonly Dictionary<FunctionKoto, VirtualOverride> virtualOverrides = new(ReferenceEqualityComparer.Instance);
    // Single inheritance gives each original declaration one bound path per derived Type.
    // Duplicate checking and direct-base selection share this index; virtualOverrides retains the full contract.
    private readonly Dictionary<(BindingSymbol Type, FunctionKoto Original), FunctionKoto> overrideEntries = new();
    private readonly HashSet<BoundConstraint> virtualHeaderCommon = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<BoundConstraint> virtualHeaderCandidate = new(ReferenceEqualityComparer.Instance);
    private readonly ScratchBuffers<BoundConstraint> virtualHeaderScratch = new();
    // One struct's virtual and override functions in member order while its VTableDef is filled.
    private readonly List<FunctionKoto> slotMembers = new();
    private Dictionary<FunctionKoto, OverrideFailure>? overrideFailures;

    // Header names must be independent of slot selection. Only premises common to the accessible
    // virtual group can supply an unqualified associated name; a qualified name needs no guess.
    private void PrepareVirtualHeaderPremises(FunctionKoto function, BindingScope scope)
    {
        if (!function.IsOverride || function.BoundSymbol?.Scope.Owner is not StructKoto { Bases.Count: 1, BoundSymbol: { } symbol } structure ||
            this.BindType(structure.Bases[0], this.scopes[structure]) is null)
        {
            return;
        }

        var self = this.SelfType(symbol);
        if (AdtDef.Base(self) is not { } baseType)
        {
            return;
        }

        var selection = this.LookupTypeMember(baseType, function.Name, scope, self);
        if (selection.Pending || selection.DeclaringType?.Symbol?.Declaration is not { } owner)
        {
            return;
        }

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
                    if (this.ProveConstraint(facts[i], function.BoundSymbol.Scope) is ConstraintProof.Proven or ConstraintProof.Unknown)
                    {
                        this.AddConstraintFact(scope.Constraints ??= new(), facts[i]);
                    }
                }

                this.ExpandScopeContractPremises(scope);
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

        void Collect(BindingScope source)
        {
            if (source.Constraints is not { Invalid: false } environment)
            {
                return;
            }

            foreach (var fact in environment.DirectFacts)
            {
                var bound = this.SubstituteConstraint(fact, owner, (BoundType[])selection.DeclaringType.Components);
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

    // Check-time facts only (DECISIONS G89-2). ExternalName has both names, Parameter and Span; ResultType has both Types
    // and its written result, if any; Condition has At and its Unknown/Refuted proof. First is the original or first candidate,
    // when present; AmbiguousInputs/DuplicateSlot also have Second. Other payloads are unused, never substitute explanations.
    private readonly record struct OverrideFailure(OverrideFailureCause Cause, FunctionKoto? First)
    {
        public FunctionKoto? Second { get; init; }

        public Koto? At { get; init; }

        public BoundType? ExpectedType { get; init; }

        public BoundType? ActualType { get; init; }

        public string? ExpectedName { get; init; }

        public string? ActualName { get; init; }

        public int Parameter { get; init; }

        public ConstraintProof Proof { get; init; }

        public SourceSpan? Span { get; init; }
    }

    // Correspondence alone is not a verified implementation or permission to execute it.
    internal bool TryGetVirtualOverride(FunctionKoto function, out VirtualOverride result)
    {
        result = default;
        return (this.IsRunning || this.Result != default) && this.virtualOverrides.TryGetValue(function, out result);
    }

    // Closed descriptor bindings use the public premises, without assuming those premises from the body scope.
    internal ConstraintProof VirtualApplicability(VirtualSlot slot)
    {
        if (slot.DeclaringType.ContainsParameter)
        {
            return ConstraintProof.Unknown;
        }

        var original = slot.Original;
        var scope = this.ModuleScope(original);
        return CombineProof(
            this.CheckConstraints(original.TypeConstraints, original, [], scope, declaringType: slot.DeclaringType),
            this.ProveMemberConditions(original.BoundSymbol!, slot.DeclaringType, scope),
            true);
    }

    internal bool IsInapplicableVirtualBody(FunctionKoto function)
    {
        var slot = function.IsVirtual ? new VirtualSlot(function, this.SelfType(function.BoundSymbol!.Scope.Owner.BoundSymbol!))
            : function.IsOverride && this.virtualOverrides.TryGetValue(function, out var implementation) ? implementation.Slot : default;
        return slot.Original is not null && this.VirtualApplicability(slot) == ConstraintProof.Refuted;
    }

    // Called by the common call-plan setter, including instantiated calls and Function Item contexts.
    // Target and its defaults always remain the original public contract.
    internal void SelectVirtualCall(CallPlan call, FunctionKoto original)
    {
        var declaring = call.DeclaringType ?? this.SelfType(original.BoundSymbol!.Scope.Owner.BoundSymbol!);
        call.VirtualSlotType = declaring;
        call.VirtualBaseLookupType = null;
        call.VirtualImplementation = null;
        call.VirtualImplementingType = null;
        if (call.VirtualIsDirect && ReferenceTypes.ViewTarget(call.ReceiverOperation.SourceType) is { } receiver)
        {
            call.VirtualBaseLookupType = AdtDef.Base(receiver);
            for (var current = call.VirtualBaseLookupType; current is not null; current = AdtDef.Base(current))
            {
                if (current.Symbol is { } symbol && this.overrideEntries.TryGetValue((symbol, original), out var implementation) &&
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
            var baseType = AdtDef.Base(self);
            if (structure.BindingState == BindingState.Invalid || (structure.Bases.Count != 0 && baseType is null))
            {
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
                this.CompleteDependent(function, failedInput);
                continue;
            }

            var selection = baseType is null ? default : this.LookupTypeMember(baseType, function.Name, this.scopes[function], self, this.MemberPath(null, structure.Bases[0], baseType));
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
                this.FailOverride(
                    function,
                    count == 0 ? BindingFailure.MissingOverrideTarget : BindingFailure.AmbiguousOverrideTarget,
                    new(cause, first ?? selection.Member?.Declaration as FunctionKoto) { Second = second });
                continue;
            }

            var slot = new VirtualSlot(first!, selection.DeclaringType!);
            this.virtualOverrides.Add(function, new(slot, self, selection.Path));
            var key = (typeSymbol, slot.Original);
            if (this.overrideEntries.TryGetValue(key, out var previous))
            {
                this.FailOverride(function, BindingFailure.DuplicateOverride, new(OverrideFailureCause.DuplicateSlot, slot.Original) { Second = previous });
                continue;
            }

            this.overrideEntries.Add(key, function);
            if (IncompleteSignature(slot.Original) is { } prerequisite)
            {
                this.CompleteDependent(function, prerequisite);
                continue;
            }

            this.PrepareVirtualOverridePremises(function, slot);
            this.CheckVirtualOverrideSignature(function, slot);
            if (function.BindingFailure == BindingFailure.None && !this.CompleteImplementationOrigins(function, slot.Original, [], [], out var incompatible, slot.DeclaringType, self))
            {
                if (incompatible is { } contract)
                {
                    this.FailExplained(ref this.originContracts, function, BindingFailure.OriginContract, contract);
                }
                else
                {
                    this.FailOverride(function, BindingFailure.OverrideContractMismatch, new(OverrideFailureCause.OriginBinders, slot.Original));
                }
            }
        }

        this.FillVTables();
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
        if (table.Version == this.storageVersion)
        {
            return table.Resolved ? table : null;
        }

        table.Version = this.storageVersion;
        table.Resolved = false;
        var parent = structure.Bases.Count != 0 ? AdtDef.Declaration(AdtDef.Base(this.SelfType(symbol))) : null;
        var inherited = parent is null ? null : this.FillVTable(parent);
        if (structure.BindingState == BindingState.Invalid || (structure.Bases.Count != 0 && inherited is null))
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

    private void PrepareVirtualOverridePremises(FunctionKoto function, VirtualSlot slot)
    {
        var bodyScope = this.scopes[function];
        var premises = bodyScope.ImplementationPremises ??= new(function);
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
            var bound = this.SubstituteConstraint(fact, slot.DeclaringType.Symbol!.Declaration, (BoundType[])slot.DeclaringType.Components);
            // Applicability belongs to the public slot. A refuted substituted premise supplies no
            // body evidence; ordinary definition checks still run (SPEC 8.3.3). Generation keeps
            // the inapplicable slot's position without requesting its body.
            var proof = this.ProveConstraint(bound, premises);
            if (proof == ConstraintProof.Refuted)
            {
                continue;
            }

            if (proof == ConstraintProof.Error)
            {
                this.FailConstraint(function, this.FindConformanceDiagnosticCause(source.Owner, fact));
                continue;
            }

            this.AddConstraintFact(premises.Constraints!, bound);
        }
    }

    private void ValidateVirtualOverrideConditions()
    {
        foreach (var (function, implementation) in this.virtualOverrides)
        {
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

                var proof = this.ProveConstraint(bound, premises);
                if (proof != ConstraintProof.Proven)
                {
                    if (proof == ConstraintProof.Error)
                    {
                        this.CompleteDependent(function, block);
                    }
                    else
                    {
                        this.FailOverride(
                            function,
                            BindingFailure.UnprovenOverrideCondition,
                            new(OverrideFailureCause.Condition, implementation.Slot.Original) { At = condition, Proof = proof });
                    }

                    break;
                }
            }
        }
    }

    // Selection deliberately ignores results, external labels, Origins, conditions and body facts.
    private bool VirtualInputsMatch(FunctionKoto original, FunctionKoto implementation, BoundType declaring)
    {
        if (original.Parameters.Count != implementation.Parameters.Count || original.BoundSymbol!.ReceiverIndex != implementation.BoundSymbol!.ReceiverIndex)
        {
            return false;
        }

        for (var i = 0; i < original.Parameters.Count; i++)
        {
            var required = original.Parameters[i].Type.BoundType;
            var actual = implementation.Parameters[i].Type.BoundType;
            if (required is null || actual is null)
            {
                return false;
            }

            if (i == original.BoundSymbol.ReceiverIndex)
            {
                if (required.Semantics != actual.Semantics)
                {
                    return false;
                }
            }
            else if (this.MemberType(required, declaring) is not { } bound ||
                !SignatureEquals(this.ContractType(bound, this.scopes[implementation]), this.ContractType(actual, this.scopes[implementation]), original, implementation))
            {
                return false;
            }
        }

        return true;
    }

    private void CheckVirtualOverrideSignature(FunctionKoto implementation, VirtualSlot slot)
    {
        var original = slot.Original;
        for (var i = 0; i < original.Parameters.Count; i++)
        {
            if (original.Parameters[i].ExternalName != implementation.Parameters[i].ExternalName)
            {
                this.FailOverride(implementation, BindingFailure.OverrideContractMismatch, new(OverrideFailureCause.ExternalName, original)
                {
                    ExpectedName = original.Parameters[i].ExternalName,
                    ActualName = implementation.Parameters[i].ExternalName,
                    Parameter = i,
                    Span = implementation.Parameters[i].ExternalNameSpan,
                });
                return;
            }
        }

        if (original.BoundSymbol!.Type is { } required && implementation.BoundSymbol!.Type is { } actual && this.MemberType(required, slot.DeclaringType) is { } bound &&
            !SignatureEquals(this.ContractType(bound, this.scopes[implementation]), this.ContractType(actual, this.scopes[implementation]), original, implementation))
        {
            this.FailOverride(implementation, BindingFailure.OverrideContractMismatch, new(OverrideFailureCause.ResultType, original)
            {
                At = implementation.ReturnType,
                ExpectedType = bound,
                ActualType = actual,
            });
        }
    }

    private void FailOverride(FunctionKoto function, BindingFailure failure, OverrideFailure facts)
    {
        (this.overrideFailures ??= new(ReferenceEqualityComparer.Instance))[function] = facts;
        this.Fail(function, failure);
    }
}
