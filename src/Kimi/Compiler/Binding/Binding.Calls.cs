// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

#pragma warning disable SA1402 // The call plan accompanies its binder.

/// <summary>An omitted default evaluated after explicit arguments, in parameter order.
/// Expression and Parameter retain the declaration environment; references to preceding
/// parameters address prepared call slots, never the caller's original variables.
/// This plan is not a certificate of default ownership or cleanup verification.</summary>
public readonly record struct BoundDefaultArgument(Koto Expression, BindingSymbol Parameter, BoundType ParameterType);

/// <summary>The committed target and argument mapping; storage is reused when the same call is rebound.</summary>
public sealed class BoundCall
{
    private int[] mapping = [];
    private BoundType?[] typeArguments = [];
    private BoundLength?[] lengthArguments = [];
    private BoundOrigin[] origins = [];
    private int originCount;
    private BoundOrigin[] inputOrigins = [];
    private int inputOriginCount;
    private BoundArgumentOperation[] argumentOperations = [];
    private BoundDefaultArgument[] defaultArguments = [];

    public BoundArgumentOperation ReceiverOperation { get; private set; }

    public ReadOnlySpan<BoundArgumentOperation> ArgumentOperations => this.argumentOperations;

    /// <summary>Gets omitted defaults in parameter declaration order, after explicit acquisitions.</summary>
    public ReadOnlySpan<BoundDefaultArgument> DefaultArguments => this.defaultArguments;

    /// <summary>Gets the selected function Symbol.</summary>
    public BindingSymbol Target { get; private set; } = null!;

    /// <summary>Gets the complete return type after substitution.</summary>
    public BoundType ReturnType { get; private set; } = null!;

    /// <summary>Gets the result category committed with the selected contract.</summary>
    public FunctionResultMode ResultMode { get; internal set; }

    /// <summary>Gets the explicit receiver when member-call syntax supplies it.</summary>
    public Koto? Receiver { get; private set; }

    /// <summary>Gets the symbolic conforming Type for a definition-bound requirement call.</summary>
    public BoundType? ConformingType { get; private set; }

    /// <summary>Gets the instantiated Type declaring a nominal member, including an inherited member's base Type.</summary>
    public BoundType? DeclaringType { get; private set; }

    /// <summary>Gets the lookup path, including for type-qualified calls that do not project a receiver.</summary>
    public BoundMemberPath? BasePath { get; private set; }

    /// <summary>Gets the committed substitutions for the function's declared Origins.</summary>
    public ReadOnlySpan<BoundOrigin> Origins => this.origins.AsSpan(0, this.originCount);

    /// <summary>Gets the committed input Origins in parameter-slot order.</summary>
    public ReadOnlySpan<BoundOrigin> InputOrigins => this.inputOrigins.AsSpan(0, this.inputOriginCount);

    /// <summary>Gets source-argument index to parameter-slot mappings.</summary>
    public ReadOnlySpan<int> ArgumentToParameter => this.mapping;

    /// <summary>Gets Type substitutions in declaration-slot order; length slots are null.</summary>
    public ReadOnlySpan<BoundType?> TypeArguments => this.typeArguments;

    /// <summary>Gets length substitutions in declaration-slot order; Type slots are null.</summary>
    public ReadOnlySpan<BoundLength?> LengthArguments => this.lengthArguments;

    // Selected from source syntax, never inferred again after generic substitution.
    internal bool TupleOperator { get; set; }

    internal ConversionKoto? AdaptationSource { get; set; }

    // The Contract reference whose requirement the call selected: a bound reference (Indexable<Name>) when the Contract
    // takes Type arguments, else the Contract itself. Every instance dispatches through the conformance to that reference
    // (SPEC 8.4.9). Null for calls of other functions.
    internal BindingSymbol? RequirementContract { get; set; }

    // Semantic selection only; neither a body certificate nor a physical slot index.
    internal BoundVirtualCall? VirtualDispatch { get; private set; }

    internal void SetReceiverProjection(BoundArgumentOperation operation, BoundMemberPath path)
    {
        this.BasePath = path;
        if (this.Receiver is not null)
        {
            this.ReceiverOperation = operation;
            return;
        }

        for (var i = 0; i < this.argumentOperations.Length; i++)
        {
            if (this.argumentOperations[i].ParameterIndex == operation.ParameterIndex)
            {
                this.argumentOperations[i] = operation;
                return;
            }
        }

        throw new InvalidOperationException("A receiver projection requires an acquired receiver argument.");
    }

    // Pooled body-local calls release semantic/source references while retaining their reusable array shapes.
    internal void Clear()
    {
        this.AdaptationSource = null;
        this.Target = null!;
        this.ReturnType = null!;
        this.ResultMode = default;
        this.Receiver = null;
        this.ConformingType = null;
        this.DeclaringType = null;
        this.BasePath = null;
        this.ReceiverOperation = default;
        this.TupleOperator = false;
        this.RequirementContract = null;
        this.VirtualDispatch = null;
        Array.Clear(this.typeArguments);
        Array.Clear(this.lengthArguments);
        Array.Clear(this.origins, 0, this.originCount);
        Array.Clear(this.inputOrigins, 0, this.inputOriginCount);
        Array.Clear(this.argumentOperations);
        Array.Clear(this.defaultArguments);
        this.originCount = 0;
        this.inputOriginCount = 0;
    }

    internal void Set(BindingSymbol target, BoundType result, Koto? receiver, ReadOnlySpan<int> mapping, ReadOnlySpan<BoundType?> typeArguments, BoundType? conformingType = null, BoundType? declaringType = null, ReadOnlySpan<BoundOrigin> origins = default, ReadOnlySpan<BoundOrigin> inputOrigins = default, ReadOnlySpan<BoundArgumentOperation> operations = default, BoundArgumentOperation receiverOperation = default, BoundMemberPath? basePath = null, ReadOnlySpan<BoundDefaultArgument> defaults = default, ReadOnlySpan<BoundLength?> lengthArguments = default)
    {
        this.AdaptationSource = null;
        this.Target = target;
        this.ReturnType = result;
        this.ResultMode = target.Declaration is FunctionKoto function ? Binding.ResultModeOf(function.ReturnType) : FunctionResultMode.Value;
        this.Receiver = receiver;
        this.ConformingType = conformingType;
        this.TupleOperator = false;
        this.RequirementContract = null;
        this.DeclaringType = declaringType;
        this.BasePath = basePath;
        this.ReceiverOperation = receiverOperation;
        if (this.argumentOperations.Length != operations.Length)
        {
            this.argumentOperations = new BoundArgumentOperation[operations.Length];
        }

        operations.CopyTo(this.argumentOperations);
        if (this.defaultArguments.Length != defaults.Length)
        {
            this.defaultArguments = defaults.IsEmpty ? [] : new BoundDefaultArgument[defaults.Length];
        }

        defaults.CopyTo(this.defaultArguments);
        if (this.origins.Length < origins.Length)
        {
            this.origins = new BoundOrigin[origins.Length];
        }
        else if (this.originCount > origins.Length)
        {
            Array.Clear(this.origins, origins.Length, this.originCount - origins.Length);
        }

        if (this.inputOrigins.Length < inputOrigins.Length)
        {
            this.inputOrigins = new BoundOrigin[inputOrigins.Length];
        }
        else if (this.inputOriginCount > inputOrigins.Length)
        {
            Array.Clear(this.inputOrigins, inputOrigins.Length, this.inputOriginCount - inputOrigins.Length);
        }

        this.originCount = origins.Length;
        this.inputOriginCount = inputOrigins.Length;
        origins.CopyTo(this.origins);
        inputOrigins.CopyTo(this.inputOrigins);
        if (this.mapping.Length != mapping.Length)
        {
            this.mapping = new int[mapping.Length];
        }

        mapping.CopyTo(this.mapping);
        if (this.typeArguments.Length != typeArguments.Length)
        {
            this.typeArguments = new BoundType[typeArguments.Length];
        }

        typeArguments.CopyTo(this.typeArguments);
        if (this.lengthArguments.Length != lengthArguments.Length)
        {
            this.lengthArguments = new BoundLength?[lengthArguments.Length];
        }

        lengthArguments.CopyTo(this.lengthArguments);
        this.VirtualDispatch = target.Declaration is FunctionKoto { IsVirtual: true } original
            ? original.CodeContext.Compilation.Binding.SelectVirtualCall(this, original, this.VirtualDispatch)
            : null;
    }
}

public sealed partial class Binding
{
    private readonly ScratchBuffers<BoundDefaultArgument> defaultArgumentScratch = new();

    // SPEC 10.8, 15.3.6: the Type slots of the candidate being evaluated whose binding comes from an invariant position.
    private SlotSet invariantSlots;
    private FunctionKoto? activeInferenceFunction;

    // Set when a known call signature's result over its closure's hidden environment receiver supplied no evidence for a slot: that slot
    // is not unsolved but judged by the Callable proof or the conversion (SPEC 8.6, 7.6.4).
    private bool environmentEvidence;

    private static Koto? IncompleteSignature(FunctionKoto function)
    {
        if (function.ReturnType is { BindingState: not BindingState.Resolved } result)
        {
            return result;
        }

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (function.Parameters[i].Type is { BindingState: not BindingState.Resolved } type)
            {
                return type;
            }
        }

        return null;
    }

    // The first failed node inside a parameter Type whose outer layer resolved.
    private Koto? FailedSignaturePart(FunctionKoto function)
    {
        var visitor = this.failedSignaturePartVisitor ??= new();
        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (visitor.Find(function.Parameters[i].Type, invalidState: true) is { } part)
            {
                return part;
            }
        }

        return null;
    }

    private BindingSymbol? Member(MemberAccessKoto member, BindingScope scope, BoundType? expected = null)
    {
        if (member.Left is BaseReferenceKoto baseReference)
        {
            return this.BaseMember(member, baseReference, scope);
        }

        if (member.Right.Akind == KotoKind.ConstructorReference)
        {
            return this.ConstructorMember(member, scope);
        }

        if (member.Right is not IdentifierNameKoto right)
        {
            return null;
        }

        BindingSymbol? typeMember = null;
        BindingSymbol? valueMember = null;
        MemberSelection typeSelection = default;
        MemberSelection valueSelection = default;
        var qualifier = this.TypeName(member.Left, scope, false);
        if (qualifier?.Type is { } type && qualifier.Declaration is not ContractKoto)
        {
            typeMember = this.RequirementMember(member, scope, type, true);
        }

        if (qualifier is not null && qualifier.Declaration is not ContractKoto && this.scopes.TryGetValue(qualifier.Declaration, out var typeScope))
        {
            if (qualifier.Type is not null)
            {
                var originQualifier = qualifier.Declaration is not EnumKoto && qualifier.Schema is { Origins.Count: > 0 };
                var callQualifier = originQualifier && IsCallee(member);
                if ((qualifier.Declaration is EnumKoto ? this.EnumQualifierType(member.Left, qualifier, scope, expected)
                    : this.BindType(member.Left, scope, this.TypeContext(member.Left, scope) with { CallQualifier = callQualifier })) is not { } qualifiedType)
                {
                    return null;
                }

                typeSelection = this.LookupTypeMember(qualifiedType, right.IdentifierName, scope);
                typeMember = typeSelection.Member ?? typeMember;
                if (originQualifier && this.RejectedOriginQualifier(member, qualifiedType, callQualifier && !typeSelection.Ambiguous ? typeSelection.Member : null, scope))
                {
                    return null;
                }
            }
            else if (qualifier.Declaration is GroupKoto { IsRoot: false } &&
                this.BindContainerQualifier(member.Left, qualifier, scope, this.TypeContext(member.Left, scope)) is { } groupType)
            {
                if (groupType.OriginArguments.Count != (qualifier.Schema?.Origins.Count ?? 0) || groupType.OriginArguments.Contains(null!))
                {
                    this.Fail(member.Left, BindingFailure.InvalidOrigin);
                    return null;
                }

                typeSelection = this.LookupTypeMember(groupType, right.IdentifierName, scope);
                typeMember = typeSelection.Member;
            }
            else if (typeScope.Values.TryGetValue(right.IdentifierName, out var candidate))
            {
                typeMember = candidate;
            }
        }

        var valuePossible = this.MayBeValueQualifier(member.Left is GenericsKoto constructed ? constructed.Identifier! : member.Left, scope);
        if (valuePossible)
        {
            var receiverType = this.BindNode(member.Left, scope);
            this.ReceiverThroughLayers(member.Left, receiverType);
            this.ReceiverElement(member.Left, receiverType);
            // SPEC 3.4.1: selection continues at the referent of each safe value-reference layer and, under the
            // object view rules, at the payload of an object handle or view.
            while (true)
            {
                while (receiverType is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } && (IsBorrow(receiverType.Semantics) || IsObjectSemantics(receiverType.Semantics)))
                {
                    receiverType = receiverType.Components[0];
                }

                if (receiverType is not null)
                {
                    // A nominal member takes priority over a requirement member of the same receiver.
                    valueMember = this.RequirementMember(member, scope, receiverType, false);
                    valueSelection = this.LookupTypeMember(receiverType, right.IdentifierName, scope, receiverType);
                    valueMember = valueSelection.Member ?? valueMember;
                }

                // SPEC 3.4.1: a qualifying pair layer without the member, declared or published, continues at its target.
                if (valueMember is not null || receiverType is null || this.FollowablePair(receiverType, scope, out var pairTarget) == SemanticsMask.None)
                {
                    break;
                }

                receiverType = pairTarget;
            }
        }

        if (typeSelection.Ambiguous || valueSelection.Ambiguous || (typeMember is not null && valueMember is not null))
        {
            this.Fail(member, BindingFailure.Ambiguous, true);
            return null;
        }

        var selected = typeMember ?? valueMember;
        if (selected is null && (typeSelection.Hidden ?? valueSelection.Hidden) is { } hidden)
        {
            member.BoundSymbol = hidden;
            this.Fail(member, BindingFailure.Access);
            return null;
        }

        if (selected is not null)
        {
            if (typeMember is not null && selected.EnumCase is null && qualifier?.Declaration is EnumKoto)
            {
                // Only Case construction may infer the enum qualifier's missing arguments.
                var qualifiedType = this.BindTypeStructure(member.Left, scope, this.TypeContext(member.Left, scope));
                if (qualifiedType is null || this.CompleteOrigins(qualifiedType, member.Left as TypeSemanticsKoto, member.Left, scope, this.TypeContext(member.Left, scope)) is not { } completeType)
                {
                    return null;
                }

                typeSelection = typeSelection with { DeclaringType = completeType };
                Complete(member.Left, completeType);
            }

            if (selected.Kind != BindingSymbolKind.Function && !this.Accessible(selected, scope, receiverType: typeMember is null ? member.Left.BoundType : null))
            {
                this.Fail(member, BindingFailure.Access);
                return null;
            }

            this.memberSelections[member] = typeMember is not null ? typeSelection : valueSelection;
            if (typeMember is not null)
            {
                member.Left.BoundSymbol = qualifier;
                member.Left.BindingState = BindingState.Resolved;
            }

            member.BoundSymbol = selected;
            right.BoundSymbol = selected;
            right.BindingState = BindingState.Resolved;
        }

        return selected;
    }

    private bool MayBeValueQualifier(Koto node, BindingScope scope)
    {
        if (node is TypeKoto)
        {
            return false;
        }

        if (node is IdentifierNameKoto name)
        {
            return this.Lookup(name.IdentifierName, scope, node, false) is not null;
        }

        if (node is SyntaxFormKoto { Akind: KotoKind.RootName })
        {
            return false;
        }

        if (node is GenericsKoto generic)
        {
            return this.MayBeValueQualifier(generic.Identifier!, scope);
        }

        if (node is MemberAccessKoto member)
        {
            return this.MayBeValueQualifier(member.Left, scope) ||
                (this.TypeName(member.Left, scope, false) is { } type && this.scopes.TryGetValue(type.Declaration, out var members) && member.Right is IdentifierNameKoto right && members.Values.ContainsKey(right.IdentifierName));
        }

        return true;
    }

    private BoundType? BindCall(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        // The bound requirement reference of a candidate or winner belongs to its own call (SPEC 8.4.2): a call bound inside
        // it, such as a later argument, neither sees nor clears the enclosing call's reference.
        var enclosingRequirementContract = this.activeRequirementContract;
        var enclosingAcquisition = this.acquisitionFailure;
        this.activeRequirementContract = null;
        try
        {
            return this.BindCallCore(call, scope, expected);
        }
        finally
        {
            this.activeRequirementContract = enclosingRequirementContract;
            this.acquisitionFailure = enclosingAcquisition;
        }
    }

    // Independent argument evidence is prepared once for named and constraint-based calls.
    private bool PrepareCallArguments(InvocationKoto call, BindingScope scope, BindingSymbol? group = null, bool callableContext = false)
    {
        var unknownArgument = false;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var argument = call.ArgumentNodes[i];
            if (KotoHelper.UnwrapParentheses(argument) is FunctionKoto { IsAnonymous: true })
            {
                continue; // A common-function argument supplies its fixed signature after selection.
            }

            if (IsAggregateArgument(argument))
            {
                unknownArgument |= !this.PrepareAggregateArgument(argument, scope);
                continue;
            }

            if (NeedsEnumContext(argument))
            {
                unknownArgument |= !this.PrepareContextualEnumInputs(argument, scope);
                continue;
            }

            // A literal is fitted after selection, unless the parser kept it as a recovery: that argument fails here, so the call rests on its Error.
            if ((!IsUnfittedLiteral(argument) || argument.CodeContext.RecoveryCause(argument) is not null) && this.BindIndependentArgument(argument, scope) is null)
            {
                if (IsWaitingNestedCall(argument))
                {
                    continue; // SPEC 10.5: only a determined outer candidate supplies the missing expectation.
                }

                if (KotoHelper.UnwrapParentheses(argument) is { BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } item })
                {
                    if ((callableContext || this.TakesCallableContext(group)) && !IndependentFunctionItem(item, this.UnboundMemberReference(argument), this.ReferenceDeclaringType(argument)) &&
                        !this.ExplicitReferenceValue(argument, item, scope))
                    {
                        continue; // SPEC 10.5: a waiting reference uses the selected fixed call signature once.
                    }

                    // SPEC 10.5: a single closed declaration supplies its own Item Type as generic argument evidence.
                    // A generic Callable contract checks that Type without erasing it to a common Function handle.
                    if (this.BindFunctionItem(argument, item, scope) is not null)
                    {
                        continue;
                    }
                }

                unknownArgument = true;
            }
        }

        return !unknownArgument;
    }

    private BoundType? BindCallCore(InvocationKoto call, BindingScope scope, BoundType? expected)
    {
        call.IsValueCall = false;
        var callee = call.Method;
        var generic = callee as GenericsKoto;
        if (generic is not null)
        {
            callee = generic.Identifier!;
        }

        BindingSymbol? group;
        if (callee is BaseReferenceKoto baseReference)
        {
            this.BindNode(baseReference, scope);
            group = null;
        }
        else if (callee is IdentifierNameKoto name)
        {
            group = this.Lookup(name.IdentifierName, scope, name, false);
        }
        else if (callee is MemberAccessKoto member && member.Right is not NumberLiteralKoto)
        {
            // The callee is checked in its own frame: what its lookup consults is its prerequisite, not the call's.
            var frame = this.BeginConsultation(member);
            group = this.Member(member, scope, expected);
            this.EndConsultation(member, frame);
        }
        else if (callee is SyntaxFormKoto { Akind: KotoKind.InferredCase } inferred)
        {
            group = this.InferredCase(inferred, scope, expected);
        }
        else
        {
            this.BindNode(callee, scope);
            group = callee.BoundSymbol;
            var inner = KotoHelper.UnwrapParentheses(callee);
            var named = inner is GenericsKoto { Identifier: { } identifier } ? identifier : inner;
            if (callee is ParenthesizedKoto && named is IdentifierNameKoto or MemberAccessKoto && named.BoundSymbol is { Kind: BindingSymbolKind.Function } function)
            {
                // SPEC 12.4.2: parentheses stay part of a referenced function name, so `(f)(1)` is the call `f(1)`; a member is
                // selected again as a callee, with its receiver.
                callee = named;
                generic = inner as GenericsKoto;
                group = function;
                if (named is MemberAccessKoto calleeMember)
                {
                    var frame = this.BeginConsultation(calleeMember);
                    group = this.Member(calleeMember, scope, expected);
                    this.EndConsultation(calleeMember, frame);
                }
            }
        }

        if (group?.EnumCase is not null)
        {
            return generic is null && callee is MemberAccessKoto or SyntaxFormKoto { Akind: KotoKind.InferredCase }
                ? this.BindEnumConstruction(call, callee, group, call, scope, expected)
                : this.Fail(call, BindingFailure.NotCallable);
        }

        if (callee is InvocationKoto || group?.Kind != BindingSymbolKind.Function)
        {
            var callableType = callee is InvocationKoto || group is null ? callee.BoundType : this.BindReference(callee, group, scope);
            var several = false;
            if (callableType is not null && this.TryCallable(callableType, scope, out var signature, out var receiverKind, out several))
            {
                return this.BindValueCall(call, scope, signature, receiverKind);
            }

            if (several)
            {
                return this.SelectValueCall(call, scope, callableType!, expected);
            }

            if (callableType is not null && this.FailedCallableClause(callableType, scope) is { } failedClause)
            {
                return this.CompleteDependent(call, failedClause);
            }
        }

        var unknownArgument = !this.PrepareCallArguments(call, scope, group);

        if (group is null)
        {
            if (this.ReportUnavailableQualifier(callee, scope) is { } qualifier)
            {
                this.CompleteDependent(callee, qualifier);
            }
            else
            {
                this.Fail(callee, callee.BindingFailure == BindingFailure.Ambiguous ? BindingFailure.Ambiguous : this.MissingFailure(callee, scope, BindingFailure.MissingName), true);
            }

            return Complete(call, null);
        }

        if (group.Kind != BindingSymbolKind.Function)
        {
            return this.BindReference(callee, group, scope) is null ? Complete(call, null) : this.Fail(call, BindingFailure.NotCallable);
        }

        if (unknownArgument)
        {
            return Complete(call, null);
        }

        callee.BoundSymbol = group;
        callee.BindingState = BindingState.Resolved;
        var requirementGroup = callee is MemberAccessKoto requirementMember && this.requirementGroups.TryGetValue(requirementMember, out var foundGroup) && foundGroup.Active ? foundGroup : null;
        var imports = this.importCandidates?.GetValueOrDefault(callee);
        if (imports is { Count: > 1 } && this.CheckGatheredParameterShapes(callee, imports, aliasStage: true))
        {
            // SPEC 7.3.1, 9.4.1: a function group gathered at the alias stage from several declaration sources is checked at the use.
            return Complete(call, null);
        }

        var candidates = new CallCandidates(group, requirementGroup, imports);
        var self = requirementGroup?.Self ?? (callee as RequirementCalleeKoto)?.Self;
        var shape = MeasureCandidates(candidates, self is not null || group.Scope.Owner is StructKoto or EnumKoto);
        var candidateCount = shape.Count;
        var maxParameters = shape.Parameters;
        var maxGenerics = shape.Generics;
        var solveOrigins = shape.SolveOrigins;
        var argumentCount = call.ArgumentNodes.Count;
        var originSlots = solveOrigins ? shape.Origins : 0;
        var inputSlots = solveOrigins ? shape.InputOrigins : 0;
        var savedCandidates = candidateCount > 1 ? candidateCount : 0;
        var scratch = this.typeScratch.Rent(Math.Max(1, maxGenerics));
        var lengthArguments = this.lengthScratch.Rent(maxGenerics);
        var explicitLengths = this.lengthScratch.Rent(generic?.TypeArguments.Count ?? 0);
        var mapping = this.indexScratch.Rent(Math.Max(1, argumentCount));
        var used = this.flagScratch.Rent(Math.Max(1, maxParameters));
        var completedWaiting = this.flagScratch.Rent(argumentCount);
        var waitingContexts = this.typeScratch.Rent(argumentCount);
        completedWaiting.AsSpan(0, argumentCount).Clear();
        var origins = this.originScratch.Rent(originSlots);
        var inputs = this.originScratch.Rent(inputSlots);
        var evaluated = this.candidateScratch.Rent(candidateCount);
        var allTypes = this.typeScratch.Rent(savedCandidates * maxGenerics);
        var allLengths = this.lengthScratch.Rent(savedCandidates * maxGenerics);
        var allMaps = this.indexScratch.Rent(savedCandidates * argumentCount);
        var allOrigins = this.originScratch.Rent(savedCandidates * originSlots);
        var allInputs = this.originScratch.Rent(savedCandidates * inputSlots);
        var operationStride = argumentCount + 1;
        var operations = this.argumentOperationScratch.Rent(candidateCount * operationStride);
        BoundDefaultArgument[]? defaults = null;
        var boundStarts = this.indexScratch.Rent(candidateCount + 1);
        var boundMark = this.BeginCandidateBounds();
        var boundsSelected = false;
        try
        {
            if (generic is not null)
            {
                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    var syntax = generic.TypeArguments[i];
                    var isLength = this.IsLengthArgument(syntax, scope);
                    var argumentName = UnwrapTypeSyntax(syntax);
                    while (argumentName is ParenthesizedTypeKoto parentheses)
                    {
                        argumentName = UnwrapTypeSyntax(parentheses.Type);
                    }

                    if (isLength && this.TypeName(argumentName, scope, false) is not null)
                    {
                        var typeSlot = false;
                        var lengthSlot = false;
                        foreach (var candidate in candidates)
                        {
                            if (candidate.Declaration is FunctionKoto declaration && declaration.GenericArguments.Count == generic.TypeArguments.Count &&
                                (callee is SyntheticKoto || this.Accessible(candidate, scope, receiverType: this.CallReceiver(callee)?.BoundType)))
                            {
                                lengthSlot |= declaration.GenericArguments[i] is LengthParameterKoto;
                                typeSlot |= declaration.GenericArguments[i] is GenericParameterKoto;
                            }
                        }

                        if (typeSlot && lengthSlot)
                        {
                            // Both namespaces can supply this spelling. Do not silently choose
                            // a kind before candidate-local dual-namespace binding is available.
                            return this.Fail(call, BindingFailure.Unsupported, true);
                        }

                        isLength = !typeSlot;
                    }

                    explicitLengths[i] = isLength ? this.BindLength(syntax, scope) : null;
                    if (explicitLengths[i] is null && syntax.BindingFailure == BindingFailure.None)
                    {
                        this.BindType(syntax, scope);
                    }
                }
            }

            var evaluation = new CallEvaluation
            {
                Call = call,
                Callee = callee,
                Generic = generic,
                Scope = scope,
                Expected = expected,
                Self = self,
                Requirements = requirementGroup,
                Scratch = scratch,
                LengthArguments = lengthArguments,
                ExplicitLengths = explicitLengths,
                Mapping = mapping,
                Used = used,
                Origins = origins,
                Inputs = inputs,
                Evaluated = evaluated,
                Operations = operations,
                OperationStride = operationStride,
                BoundStarts = boundStarts,
                SavedCandidates = savedCandidates,
                AllTypes = allTypes,
                AllLengths = allLengths,
                AllMaps = allMaps,
                AllOrigins = allOrigins,
                AllInputs = allInputs,
                MaxGenerics = maxGenerics,
                OriginSlots = originSlots,
                InputSlots = inputSlots,
                Winner = -1,
                PremisesOnly = true,
            };
            this.acquisitionFailure = null;
            foreach (var candidate in candidates)
            {
                this.EvaluateCallCandidate(ref evaluation, candidate);
            }

            var count = evaluation.Count;
            var applicable = evaluation.Applicable;
            boundStarts[count] = this.candidateBounds.Count;
            if (evaluation.Error)
            {
                // A candidate in a failed declaration cannot be judged, so the selection rests on that failure (SPEC 23.3.6.4).
                return evaluation.InvalidDeclaration is { } invalidDeclaration ? this.CompleteDependent(call, invalidDeclaration) : this.Fail(call, BindingFailure.InvalidConstraint);
            }

            if (!this.CheckCallShapeContracts(call, generic, scope, expected, evaluated.AsSpan(0, count), allMaps, maxGenerics))
            {
                return Complete(call, null);
            }

            var (selection, winnerIndex) = this.ClassifySelection(ref evaluation);
            if (selection == CallSelection.FailedSignature)
            {
                // A candidate whose own signature failed, such as a nested borrow without its Origin (`ref/uniq/i32`), stays
                // pending at every call; the selection rests on that failure (SPEC 23.3.6.4).
                return this.CompleteDependent(call, evaluation.FailedPendingSignature!);
            }

            if (selection == CallSelection.Unproven)
            {
                // SPEC 8.4.8.2: an Unknown premise that can affect the selection defers it. Omitted header Types rest on the call.
                return evaluation.PendingCount == 1 && evaluation.CallableFailure is { } callable ? this.FailCallableSelection(call, callable)
                    : evaluation.PendingCount == 1 && evaluation.ConstraintFailure is { } constraint ? this.FailPendingConstraint(call, constraint)
                    : this.FailWaitingSelection(call, BindingFailure.UnprovenConstraint);
            }

            if (selection == CallSelection.IncompleteSignature)
            {
                // A candidate whose signature failed cannot be judged, so the selection rests on that failure.
                return this.CompleteDependent(call, evaluation.IncompleteSignature!);
            }

            if (selection == CallSelection.NoneApplicable)
            {
                // SPEC 15.1.5: name the missing spelling when a bare Place was the only obstacle.
                var failure = this.acquisitionFailure?.Kind ?? BindingFailure.NoApplicableCandidate;
                if (failure == BindingFailure.NoApplicableCandidate && call.BindingFailure == BindingFailure.None)
                {
                    // The candidates that were considered explain the failed selection; recorded only when it fails.
                    var rejected = new RejectedCandidate[count];
                    for (var i = 0; i < count; i++)
                    {
                        rejected[i] = new((FunctionKoto)evaluated[i].Symbol!.Declaration, null, null, ConstraintFailure: evaluated[i].ConstraintFailure);
                        var rejectedReceiver = operations[(i * operationStride) + operationStride - 1];
                        if (rejectedReceiver.SourceType is { } receiverActual && rejectedReceiver.ParameterType is { } receiverExpected && SharedObjectAuthorityMismatch(receiverActual, receiverExpected))
                        {
                            rejected[i] = rejected[i] with { Actual = receiverActual, Expected = receiverExpected, SharedReceiver = true };
                        }

                        if (evaluated[i].Symbol!.LibraryDeclaration == KimiDeclarationId.Clone && call.ArgumentNodes is [var cloneInput] &&
                            cloneInput.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [{ Semantics: SemanticsKind.Obj }] })
                        {
                            rejected[i] = rejected[i] with { ObjectClone = true };
                        }

                        if (evaluated[i].ClosureReceiver is { } receivers)
                        {
                            rejected[i] = rejected[i] with { ActualReceiver = receivers.Actual, RequiredReceiver = receivers.Required, ReceiverParameter = receivers.Parameter };
                        }

                        for (var a = 0; a < argumentCount; a++)
                        {
                            var operation = operations[(i * operationStride) + a];
                            if (operation.SourceType is { } actual && operation.ParameterType is { } parameter && DifferentRangeShapes(actual, parameter))
                            {
                                rejected[i] = rejected[i] with { Actual = actual, Expected = parameter };
                                break;
                            }

                            // Signature-refusal facts have no selected parameter operation.
                            if (operation.ParameterIndex == -1 && operation.SourceType is { Kind: BoundTypeKind.Function } signature && operation.ParameterType is { Kind: BoundTypeKind.Function } required)
                            {
                                rejected[i] = rejected[i] with { Actual = signature, Expected = required, CallableSignature = true };
                                break;
                            }
                        }
                    }

                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = rejected;
                }
                else if (this.acquisitionFailure is { } acquisition)
                {
                    return this.FailAcquisition(call, failure, acquisition.Place, acquisition.Object, true);
                }

                return this.FailWaitingSelection(call, failure);
            }

            if (selection == CallSelection.Ambiguous)
            {
                if (this.DeferNestedCall(call, expected))
                {
                    return Complete(call, null);
                }

                var remaining = new RejectedCandidate[applicable];
                var next = 0;
                var erasure = ErasureIncomparable(evaluated.AsSpan(0, count), operations, operationStride, argumentCount);
                for (var i = 0; i < count; i++)
                {
                    if (evaluated[i].State is CandidateApplicability.Applicable or CandidateApplicability.Waiting)
                    {
                        remaining[next++] = new((FunctionKoto)evaluated[i].Symbol!.Declaration, null, null, ErasureIncomparable: erasure);
                    }
                }

                (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = remaining;
                return this.FailWaitingSelection(call, BindingFailure.Ambiguous);
            }

            this.KeepCandidateBounds(boundMark, boundStarts[winnerIndex], winnerIndex + 1 < count ? boundStarts[winnerIndex + 1] : boundStarts[count], boundStarts[count]);
            boundsSelected = true;
            var winner = evaluated[winnerIndex].Symbol!;
            var selected = (FunctionKoto)winner.Declaration;
            this.activeRequirementContract = requirementGroup?.Contracts[winnerIndex] ?? (callee as RequirementCalleeKoto)?.Contract; // Reset by the finally block.
            var selectedType = evaluated[winnerIndex].DeclaringType;
            if (savedCandidates != 0)
            {
                allTypes.AsSpan(winnerIndex * maxGenerics, CallSlotCount(selected)).CopyTo(scratch);
                allLengths.AsSpan(winnerIndex * maxGenerics, CallSlotCount(selected)).CopyTo(lengthArguments);
                allMaps.AsSpan(winnerIndex * argumentCount, argumentCount).CopyTo(mapping);
                allOrigins.AsSpan(winnerIndex * originSlots, originSlots).CopyTo(origins);
                allInputs.AsSpan(winnerIndex * inputSlots, inputSlots).CopyTo(inputs);
            }

            if (evaluated[winnerIndex].Unsolved)
            {
                if (this.DeferNestedCall(call, expected))
                {
                    return Complete(call, null);
                }

                // SPEC 10.8, 10.6: the selected candidate's unsolved slot is the inference boundary; the checks that need it are derived.
                return this.FailUnboundSlots(call, selected, scope, scratch, lengthArguments, mapping, operations.AsSpan(winnerIndex * operationStride, operationStride), expected, self, origins, inputs, selectedType);
            }

            if (evaluated[winnerIndex].State == CandidateApplicability.Waiting)
            {
                // SPEC 10.5: selection precedes bodies. Complete only this candidate; a body or capture failure
                // never retries another overload, and its inferred result never supplies outer signature evidence.
                var waitingOperations = operations.AsSpan(winnerIndex * operationStride, operationStride);
                for (var i = 0; i < argumentCount; i++)
                {
                    if (IsWaitingCallable(call.ArgumentNodes[i]) || IsWaitingNestedCall(call.ArgumentNodes[i]))
                    {
                        completedWaiting[i] = true;
                        waitingContexts[i] = waitingOperations[i].ParameterType;
                        var contract = this.activeRequirementContract;
                        this.activeRequirementContract = null;
                        var pattern = selected.Parameters[mapping[i]].Type.BoundType!;
                        var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? pattern.Components[0] : pattern;
                        var concrete = slotType.Kind == BoundTypeKind.Parameter && ContainerSlot(CallSlotOwner(selected), slotType.Symbol!) >= 0;
                        var argument = call.ArgumentNodes[i];
                        // SPEC 10.5: a candidate without a fixed expected call signature for this argument supplies none; a reference
                        // is then a value only when it needs no S (FailUnfixedReference otherwise).
                        var bound = concrete && KotoHelper.UnwrapParentheses(argument) is FunctionKoto { IsAnonymous: true } closure
                            ? this.BindClosureArgument(argument, closure, scope, waitingOperations[i].ParameterType)
                            : concrete && IsWaitingFunctionReference(argument)
                                ? waitingOperations[i].ParameterType is { } signature
                                    ? this.BindFunctionReference(argument, KotoHelper.UnwrapParentheses(argument).BoundSymbol!, signature, scope, erase: false)
                                    : this.BindFunctionItem(argument, KotoHelper.UnwrapParentheses(argument).BoundSymbol!, scope)
                                : this.BindNode(argument, scope, waitingOperations[i].ParameterType);
                        this.activeRequirementContract = contract;
                        if (bound is null)
                        {
                            return Complete(call, null);
                        }
                    }
                }

                var state = this.TryCandidate(call, selected, generic, scope, scratch, lengthArguments, explicitLengths, mapping, evaluated[winnerIndex].ArgumentMap, expected, self, origins, inputs, selectedType, waitingOperations, out _, out _, out _, out _, out _, out _);
                if (state != CandidateApplicability.Applicable)
                {
                    var rejected = new RejectedCandidate(selected, null, null, Selected: true);
                    for (var i = 0; i < argumentCount; i++)
                    {
                        if (KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is FunctionKoto { BoundClosure: { } closure } &&
                            this.TryCallable(selected.Parameters[mapping[i]].Type.BoundType!, this.ConstraintScope(selected), out _, out var required) &&
                            !CallableReceiverFits(closure.Receiver, CallableReceiverMask(required)))
                        {
                            rejected = rejected with { ActualReceiver = closure.Receiver, RequiredReceiver = required };
                            break;
                        }
                    }

                    (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[call] = [rejected];
                    return this.Fail(call, state == CandidateApplicability.Pending ? BindingFailure.UnprovenConstraint : BindingFailure.NoApplicableCandidate, true);
                }
            }

            for (var i = 0; i < CallOwnSlots(selected).Count; i++)
            {
                if (CallOwnSlots(selected)[i] is LengthParameterKoto ? lengthArguments[i] is null : scratch[i] is null)
                {
                    return this.Fail(call, BindingFailure.MissingType, true);
                }
            }

            if (selected.IsConstructor && selectedType is not null)
            {
                selectedType = this.ConstructionType(selected, selectedType, scratch);
            }

            if (selected.IsConstructor && selectedType is not null && callee is MemberAccessKoto constructorMember && this.inferredConstructorTargets.Contains(constructorMember.Left) &&
                !this.ValidateFixedConstruction(call, scope, expected, selectedType, evaluated.AsSpan(0, count), winnerIndex, allMaps, mapping, scratch, lengthArguments, origins, inputs, allOrigins, allInputs, originSlots, inputSlots, operations, operationStride, completedWaiting, waitingContexts))
            {
                return Complete(call, null);
            }

            if (selected.IsConstructor && selectedType is not null && callee is MemberAccessKoto constructedMember && this.inferredConstructorTargets.Contains(constructedMember.Left))
            {
                Complete(constructedMember.Left, selectedType);
            }

            var result = winner.Type is { } returnType ? this.CallType(returnType, selected, scratch, scope, self, origins, inputs, selectedType, lengthArguments) : null;
            if (result is null)
            {
                return this.Fail(call, BindingFailure.MissingType, true);
            }

            var selectedOperations = operations.AsSpan(winnerIndex * operationStride, operationStride);
            var receiverOperation = selectedOperations[argumentCount];
            if (receiverOperation.Source is not null)
            {
                if (receiverOperation.BasePath is { } objectBase && ObjectTypes.IsBorrow(receiverOperation.ParameterType) &&
                    !this.RequireObjectErasure(call, receiverOperation.Source, ObjectTypes.ViewTarget(receiverOperation.SourceType)!, objectBase.Type))
                {
                    return null;
                }

                receiverOperation = receiverOperation with { ObjectCompatibility = receiverOperation.BasePath is null ? ConstraintProof.Proven : ProjectedReceiverProof(winner) };
                this.receiverOperations[call] = receiverOperation;
                if (receiverOperation.ObjectCompatibility != ConstraintProof.Proven)
                {
                    // Pending effect verification is an implementation boundary, not a
                    // completed public NotProven guarantee (SPEC 12.4.4.1).
                    var pendingEffects = receiverOperation.ObjectCompatibility == ConstraintProof.Unknown;
                    return this.Fail(call, pendingEffects ? BindingFailure.Unsupported : BindingFailure.UnprovenConstraint, pendingEffects);
                }
            }

            for (var i = 0; i < argumentCount; i++)
            {
                if (selectedOperations[i].MissingOrigin)
                {
                    // SPEC 15.3.6, 10.8: judged after selection; it never made the candidate inapplicable.
                    return this.FailPerCallOrigin(call, selected, scratch, call.ArgumentNodes[i]);
                }
            }

            var erasureFailed = false;
            for (var i = 0; i < argumentCount; i++)
            {
                if (call.ArgumentNodes[i].BoundType is null)
                {
                    // The argument is an expression of the caller's context, not of the selected requirement.
                    var selectedContract = this.activeRequirementContract;
                    this.activeRequirementContract = null;
                    this.RequireType(call.ArgumentNodes[i], scope, selectedOperations[i].SourceType ?? selectedOperations[i].ParameterType);
                    this.activeRequirementContract = selectedContract;
                    if (call.ArgumentNodes[i].BindingState != BindingState.Resolved)
                    {
                        return Complete(call, null);
                    }

                    selectedOperations[i] = selectedOperations[i] with { SourceType = call.ArgumentNodes[i].BoundType };
                }
                else if (call.ArgumentNodes[i].BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } closureType && selectedOperations[i].ParameterType is { Kind: BoundTypeKind.Function } erased &&
                    selectedOperations[i].Adaptation == ArgumentAdaptation.Erasure)
                {
                    if (!this.ErasesToFunction(call.ArgumentNodes[i], closureType, erased))
                    {
                        // SPEC 10.5, 7.6.4: the erasure conditions are judged after selection, at the argument: an environment that is not
                        // Owned is the Constraint record, a receiver that is not Shared a Type mismatch with its Note. The call rests on it
                        // and another candidate is never selected; every failing argument is reported (SPEC 23.3.6.4).
                        this.RecordMismatch(call.ArgumentNodes[i], call.ArgumentNodes[i], closureType, erased);
                        erasureFailed = true;
                        continue;
                    }

                    // SPEC 7.6.4: the selected parameter fixes the common Function Type the Closure value is converted to.
                    call.ArgumentNodes[i].ErasedFunctionType = erased;
                    selectedOperations[i] = selectedOperations[i] with { SourceType = erased };
                }
            }

            if (erasureFailed)
            {
                return Complete(call, null);
            }

            // SPEC 15.6.1: the selected candidate's Origin relations, never part of its applicability, are judged at their sources.
            this.JudgeSelectedCall(call, selected, selectedOperations, argumentCount, origins, inputs, selectedType);
            var defaultCount = evaluated[winnerIndex].DefaultsUsed;
            if (defaultCount != 0)
            {
                // Candidate evaluation reuses used[]; reconstruct only the winner's
                // slots from its retained mapping, without repeating selection.
                used.AsSpan(0, selected.Parameters.Count).Clear();
                for (var i = 0; i < argumentCount; i++)
                {
                    used[mapping[i]] = true;
                }

                if (receiverOperation.Source is not null)
                {
                    used[receiverOperation.ParameterIndex] = true;
                }

                defaults = this.defaultArgumentScratch.Rent(defaultCount);
                var defaultIndex = 0;
                for (var i = 0; i < selected.Parameters.Count; i++)
                {
                    if (!used[i])
                    {
                        var parameter = selected.Parameters[i];
                        if (parameter.DefaultValue is not { } expression || parameter.Type.BoundType is not { } pattern ||
                            this.CallType(pattern, selected, scratch, scope, self, origins, inputs, selectedType, lengthArguments) is not { } parameterType)
                        {
                            return this.Fail(call, BindingFailure.MissingType, true);
                        }

                        defaults[defaultIndex++] = new(expression, this.ParameterSymbol(selected, i), parameterType);
                    }
                }
            }

            this.RequireResultPremises(selected, result, call); // SPEC 15.3.7: the callee's result premises.
            callee.BoundSymbol = winner;
            callee.BindingState = BindingState.Resolved;
            if (callee is MemberAccessKoto selectedMember)
            {
                selectedMember.Right.BoundSymbol = winner;
            }

            if (generic is not null)
            {
                generic.BoundSymbol = winner;
                generic.BindingState = BindingState.Resolved;
            }

            for (var wrapper = call.Method; wrapper is ParenthesizedKoto parentheses; wrapper = parentheses.Operand)
            {
                wrapper.BoundSymbol = winner;
                wrapper.BindingState = BindingState.Resolved;
            }

            call.BoundSymbol = winner;
            if (selected.IsConstructor)
            {
                result = selectedType!;
            }

            if (this.ExecutedTarget(winner, self) is not { } executed)
            {
                return this.Fail(call, BindingFailure.Unsupported, true); // SPEC 22.1: a linked constructor whose link is not validated.
            }

            var basePath = callee is MemberAccessKoto memberCallee && this.memberSelections.TryGetValue(memberCallee, out var memberSelection) ? memberSelection.Path : null;
            (call.CallStorage ??= new()).Set(executed, result, this.CallReceiver(callee), mapping.AsSpan(0, argumentCount), scratch.AsSpan(0, selected.GenericArguments.Count), self, selectedType, origins.AsSpan(0, solveOrigins ? selected.Origins.Count : 0), inputs.AsSpan(0, solveOrigins ? InputOriginCount(selected) : 0), selectedOperations[..argumentCount], receiverOperation, basePath, defaults.AsSpan(0, defaultCount), lengthArguments.AsSpan(0, selected.GenericArguments.Count));
            call.CallStorage.ResultMode = ResultModeOf(selected.ReturnType);
            if (selected.IsRequirement)
            {
                // Retain the bound Contract selected by member lookup or by an operator.
                call.CallStorage.RequirementContract = requirementGroup is not null && this.activeRequirementContract is { } requirementContract ? requirementContract :
                    callee is RequirementCalleeKoto requirementCallee ? requirementCallee.Contract : null;
            }

            if (call.CallStorage.ResultMode != FunctionResultMode.Value && result is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } published)
            {
                // SPEC 7.1.1: the call designates the published Place; its Type is the stored Type, and the plan keeps the reference.
                return Complete(call, published.Components[0]);
            }

            return Complete(call, result);
        }
        finally
        {
            this.EndCandidateBounds(boundMark, boundsSelected);
            this.indexScratch.Return(boundStarts);
            this.activeRequirementContract = null;
            if (defaults is not null)
            {
                this.defaultArgumentScratch.Return(defaults, clearArray: true);
            }

            this.argumentOperationScratch.Return(operations, clearArray: true);
            this.originScratch.Return(allInputs, clearArray: true);
            this.originScratch.Return(allOrigins, clearArray: true);
            this.indexScratch.Return(allMaps);
            this.typeScratch.Return(allTypes, clearArray: true);
            this.lengthScratch.Return(allLengths, clearArray: true);
            this.lengthScratch.Return(explicitLengths, clearArray: true);
            this.lengthScratch.Return(lengthArguments, clearArray: true);
            this.candidateScratch.Return(evaluated, clearArray: true);
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
            this.typeScratch.Return(waitingContexts, clearArray: true);
            this.flagScratch.Return(completedWaiting);
            this.flagScratch.Return(used);
            this.indexScratch.Return(mapping);
            this.typeScratch.Return(scratch, clearArray: true);
        }
    }

    private Koto? CallReceiver(Koto callee)
        => callee is SyntheticKoto synthetic ? synthetic.Receiver : callee is MemberAccessKoto member && (this.requirementGroups.TryGetValue(member, out var group) && group.Active ? !group.TypeAccess : member.Left.BoundSymbol?.Kind is not (BindingSymbolKind.Type or BindingSymbolKind.Container)) ? member.Left : null;

    private ConstraintProof CheckCallTypeConstraints(BoundCall call, BindingScope scope)
    {
        // A linked constructor is judged as the public declaration it selected (SPEC 22.1).
        var target = this.Library.PresentedTarget(call.Target);
        if (InvalidDeclarationContext(target.Declaration))
        {
            return ConstraintProof.Error;
        }

        var proof = this.CheckTypeConstraints(call.ReturnType, scope);
        if (UnresolvedTypeDeclarationContext(target.Declaration))
        {
            proof = CombineProof(proof, ConstraintProof.Unknown, true);
        }

        if (call.DeclaringType is { } declaringType)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(declaringType, scope), true);
        }

        if (call.ConformingType is { } conformingType)
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(conformingType, scope), true);
        }

        foreach (var argument in call.TypeArguments)
        {
            if (argument is not null)
            {
                proof = CombineProof(proof, this.CheckTypeConstraints(argument, scope), true);
            }
        }

        if (target.Declaration is FunctionKoto function)
        {
            // Conditional evidence can fail after candidate selection. Recheck the committed
            // candidate's constraints without reopening lookup or overload selection.
            proof = CombineProof(proof, this.CheckConstraints(function.TypeConstraints, function, call.TypeArguments, scope, call.ConformingType, call.DeclaringType, call.LengthArguments), true);
        }

        proof = CombineProof(proof, this.ProveMemberConditions(target, call.DeclaringType, scope), true);
        proof = CombineProof(proof, this.CheckOperationTypeConstraints(call.ReceiverOperation, scope), true);
        foreach (var operation in call.ArgumentOperations)
        {
            proof = CombineProof(proof, this.CheckOperationTypeConstraints(operation, scope), true);
        }

        return proof;
    }

    private ConstraintProof CheckOperationTypeConstraints(in BoundArgumentOperation operation, BindingScope scope)
    {
        var proof = operation.SourceType is { } source ? this.CheckTypeConstraints(source, scope) : ConstraintProof.Proven;
        if (operation.ParameterType is { } parameter && !ReferenceEquals(parameter, operation.SourceType))
        {
            proof = CombineProof(proof, this.CheckTypeConstraints(parameter, scope), true);
        }

        return proof;
    }

    // The invariant slot bindings belong to one candidate; a nested call bound while it is evaluated keeps its own.
    // `unsolved` holds the required structural slots that no evidence binds (SPEC 10.8); an Applicable or Waiting candidate with such
    // slots stays rankable, and its selection is the inference-boundary record (FailUnboundSlots).
    private CandidateApplicability TryCandidate(InvocationKoto call, FunctionKoto function, GenericsKoto? generic, BindingScope scope, BoundType?[] arguments, BoundLength?[] lengths, BoundLength?[] explicitLengths, int[] mapping, CallArgumentMap argumentMap, BoundType? expected, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType, Span<BoundArgumentOperation> operations, out int defaultsUsed, out bool unsolved, out ClosureReceiverRefutation? closureReceiver, out bool independentRejection, out ReferenceConstraintFailure? constraintFailure, out bool premiseUnknown, bool fixedConstruction = false)
    {
        if (this.MeasureCallInference)
        {
            this.inferenceCandidateChecks++;
        }

        var saved = this.invariantSlots;
        var savedInferenceFunction = this.activeInferenceFunction;
        this.activeInferenceFunction = function;
        var savedEnvironment = this.environmentEvidence;
        var savedAcquisition = this.acquisitionFailure;
        this.acquisitionFailure = null;
        this.invariantSlots = this.RentSlotSet(CallSlotCount(function));
        this.environmentEvidence = false;
        try
        {
            var result = this.TryCandidateCore(call, function, generic, scope, arguments, lengths, explicitLengths, mapping, argumentMap, expected, self, origins, inputs, declaringType, operations, out defaultsUsed, out unsolved, out closureReceiver, out independentRejection, out constraintFailure, out premiseUnknown, fixedConstruction);
            if (result is CandidateApplicability.Applicable or CandidateApplicability.Waiting && this.acquisitionFailure is { } acquisition)
            {
                // Only a candidate whose other inputs, result and Constraints fit can explain the absent spelling.
                if (result == CandidateApplicability.Applicable)
                {
                    savedAcquisition = acquisition;
                }

                return CandidateApplicability.Inapplicable;
            }

            return result;
        }
        finally
        {
            this.ReturnSlotSet(this.invariantSlots);
            this.invariantSlots = saved;
            this.activeInferenceFunction = savedInferenceFunction;
            this.environmentEvidence = savedEnvironment;
            this.acquisitionFailure = savedAcquisition;
        }
    }

    private CandidateApplicability TryCandidateCore(InvocationKoto call, FunctionKoto function, GenericsKoto? generic, BindingScope scope, BoundType?[] arguments, BoundLength?[] lengths, BoundLength?[] explicitLengths, int[] mapping, CallArgumentMap argumentMap, BoundType? expected, BoundType? self, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType, Span<BoundArgumentOperation> operations, out int defaultsUsed, out bool unsolved, out ClosureReceiverRefutation? closureReceiver, out bool independentRejection, out ReferenceConstraintFailure? constraintFailure, out bool premiseUnknown, bool fixedConstruction = false)
    {
        var waiting = this.RentSlotSet(call.ArgumentNodes.Count);
        var open = this.RentSlotSet(call.ArgumentNodes.Count);
        var openSignatures = this.RentSlotSet(call.ArgumentNodes.Count);
        var unboundSlots = this.RentSlotSet(CallSlotCount(function));
        var unsolvedSlots = this.RentSlotSet(CallSlotCount(function));
        try
        {
            defaultsUsed = argumentMap.DefaultsUsed;
            closureReceiver = null;
            unsolved = false;
            independentRejection = false;
            constraintFailure = null;
            premiseUnknown = false;
            if (!argumentMap.InputsValid || (function.IsConstructor && declaringType is null))
            {
                return CandidateApplicability.Inapplicable;
            }

            // SPEC 8.8.3: specializations never enter the candidate set, so an invalid one cannot fail a call.
            if (function.IsSpecialization)
            {
                return CandidateApplicability.Inapplicable;
            }

            if (InvalidDeclarationContext(function))
            {
                return CandidateApplicability.Error;
            }

            this.InitializeCallSlots(call, function, declaringType, arguments, fixedConstruction);
            Array.Clear(lengths, 0, CallSlotCount(function));
            Array.Clear(origins, 0, function.Origins.Count);
            Array.Clear(inputs, 0, Math.Min(inputs.Length, InputOriginCount(function)));
            OriginInference? originInference = null;

            for (var i = 0; i < CallOwnSlots(function).Count; i++)
            {
                if (CallOwnSlots(function)[i] is not (GenericParameterKoto or LengthParameterKoto))
                {
                    return CandidateApplicability.Pending;
                }
            }

            if (generic is not null)
            {
                if (generic.TypeArguments.Count != CallOwnSlots(function).Count)
                {
                    return CandidateApplicability.Inapplicable;
                }

                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    if (CallOwnSlots(function)[i] is LengthParameterKoto)
                    {
                        if ((lengths[i] = explicitLengths[i]) is null)
                        {
                            return CandidateApplicability.Inapplicable;
                        }

                        continue;
                    }

                    if (explicitLengths[i] is not null)
                    {
                        return CandidateApplicability.Inapplicable;
                    }

                    arguments[i] = generic.TypeArguments[i].BoundType;
                    if (arguments[i] is null)
                    {
                        return CandidateApplicability.Pending;
                    }
                }
            }

            var receiver = this.CallReceiver(generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method));
            if (receiver is null && function.BoundSymbol!.ReceiverIndex >= 0 && (generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is not (MemberAccessKoto or RequirementCalleeKoto or FormattingKoto { Operation: FormattingOperation.Callee } or SyntheticKoto))
            {
                return CandidateApplicability.Inapplicable;
            }

            var receiverSlot = receiver is null ? -1 : function.BoundSymbol!.ReceiverIndex;
            var receiverPath = receiver is not null && (generic?.Identifier ?? KotoHelper.UnwrapParentheses(call.Method)) is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) ? selection.Path : null;
            if (receiver is not null)
            {
                if (receiverSlot < 0 || receiver.BoundType is not { } receiverType)
                {
                    return CandidateApplicability.Inapplicable;
                }

                if (function.Parameters[receiverSlot].Type.BoundType is not { } parameterType)
                {
                    return CandidateApplicability.Pending;
                }

                if (!InferInput(parameterType, receiverType, receiver, receiverPath, true))
                {
                    if (this.CallMemberPattern(parameterType, function, declaringType) is { } memberType && this.ContractType(memberType, scope, self) is { } required && SharedObjectAuthorityMismatch(receiverType, required))
                    {
                        operations[^1] = new(receiver, receiverType, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, receiverPath, receiverSlot);
                    }

                    return CandidateApplicability.Inapplicable;
                }
            }

            var contextualInputs = false;
            var contextualCallables = false;
            var waitingCallables = false;
            for (var i = 0; i < call.ArgumentNodes.Count; i++)
            {
                var slot = mapping[i];
                contextualInputs |= NeedsEnumContext(call.ArgumentNodes[i]) || IsAggregateArgument(call.ArgumentNodes[i]);
                contextualCallables |= KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is FunctionKoto { IsAnonymous: true } || IsWaitingFunctionReference(call.ArgumentNodes[i]);
                var type = function.Parameters[slot].Type.BoundType;
                if (type is null)
                {
                    return CandidateApplicability.Pending;
                }

                if (this.FormedApplication(type, CallSlotOwner(function), arguments).Kind == BoundTypeKind.SemanticsApplication)
                {
                    waiting.Add(i); // SPEC 10.2: solved together; s/U is matched once another argument fixes s.
                    continue;
                }

                if (call.ArgumentNodes[i].BoundType is { } actual &&
                    (type.Kind != BoundTypeKind.Function || KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is not FunctionKoto { IsAnonymous: true }) &&
                    !InferInput(type, actual, call.ArgumentNodes[i]))
                {
                    independentRejection = function.IsConstructor && IndependentInputPattern(type) && !actual.CarriesOrigin;
                    // Retain this failed comparison in existing candidate scratch space. It is used only if no candidate
                    // applies; a successful overload selection publishes no repair advice from rejected alternatives.
                    if (DifferentRangeShapes(actual, type))
                    {
                        operations[i] = new(call.ArgumentNodes[i], actual, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: slot);
                    }

                    return CandidateApplicability.Inapplicable;
                }

                if (KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]) is FunctionKoto { IsAnonymous: true } literal &&
                    this.TryCallable(type, this.ConstraintScope(function), out var headerPattern, out _) &&
                    !InferClosureHeader(headerPattern, literal))
                {
                    return CandidateApplicability.Inapplicable; // SPEC 10.5: the written header is evidence for the generic parameters.
                }
            }

            for (var i = 0; !waiting.IsEmpty && i < call.ArgumentNodes.Count; i++)
            {
                if (waiting.Contains(i) && call.ArgumentNodes[i].BoundType is { } actual && !InferInput(function.Parameters[mapping[i]].Type.BoundType!, actual, call.ArgumentNodes[i]))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            if (!InferCallableSignatures(operations))
            {
                return CandidateApplicability.Inapplicable;
            }

            // Supplied inputs have an independent mapping even when a required input is absent.
            // Preserve their known signature-refusal facts before rejecting the incomplete call.
            if (!argumentMap.Valid)
            {
                return CandidateApplicability.Inapplicable;
            }

            if (contextualInputs && !InferAggregateInputs(false))
            {
                return CandidateApplicability.Inapplicable;
            }

            // Established input types cannot change; expectations only fill unresolved slots.
            // SPEC 7.1.1, 10.3: a Place result publishes its stored Type; the use position borrows or reads it, so the
            // expectation is compared with the referent and the Place result's own Origin is never matched against it.
            var placeExpected = PlaceExpected(ResultModeOf(function.ReturnType), expected);
            var expectedStructure = true; // SPEC 10.8: the slot-free structure of a result that keeps an unsolved slot is still checked.
            if (placeExpected is not null && function.BoundSymbol?.Type is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } placePattern)
            {
                var stored = this.MemberType(self is null ? placePattern.Components[0] : this.ContractType(placePattern.Components[0], scope, self), declaringType)!;
                expectedStructure = this.Infer(stored, placeExpected, CallSlotOwner(function), arguments, true, lengths);
            }
            else if (expected is not null && (function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol?.Type) is { } returnPattern)
            {
                // Result expectations use the selected receiver's container Origins, just like inputs.
                // An abstract container binder must not become a rigid call-site lifetime constraint.
                returnPattern = this.CallMemberPattern(self is null ? returnPattern : this.ContractType(returnPattern, scope, self), function, declaringType)!;

                // SPEC 10.1 step 4, 15.6.1: the expected result fills the still-unbound slots by its structure; its Origin relations to
                // the completed result are judged at the destination.
                expectedStructure = this.Infer(returnPattern, expected, CallSlotOwner(function), arguments, true, lengths);
                this.MatchResultOrigins(returnPattern, expected, function, origins, inputs);
                if (returnPattern.CarriesOrigin)
                {
                    originInference ??= this.BeginOriginInference(call, function);
                    this.CollectOriginInference(returnPattern, expected, originInference, result: true);
                }
            }

            if (contextualInputs && !InferAggregateInputs(true))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (originInference is null && this.originDeclarations.GetValueOrDefault(function)?.Relations.Count > 0)
            {
                originInference = this.BeginOriginInference(call, function);
            }

            if (originInference is not null && !this.SolveCallOriginInference(function, originInference, origins, inputs, call, declaringType, select: true))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (receiverSlot >= 0)
            {
                var requiredReceiver = this.CallType(function.Parameters[receiverSlot].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType, lengths);
                if (requiredReceiver is null)
                {
                    return CandidateApplicability.Pending;
                }

                // SPEC 15.6.1: applicability uses the structural part of each fit; its Origin relations are judged after selection.
                if (!this.AdaptInput(receiver!, requiredReceiver, receiver!.BoundType!, scope, receiverPath, declaringType, out var adaptedReceiver, out var quality, out var kind, receiver: true, deferAcquisition: true) || !this.FitsStructurallyAt(adaptedReceiver, requiredReceiver, call))
                {
                    if (SharedObjectAuthorityMismatch(receiver.BoundType!, requiredReceiver))
                    {
                        operations[^1] = new(receiver, receiver.BoundType, requiredReceiver, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, receiverPath, receiverSlot);
                    }

                    return CandidateApplicability.Inapplicable;
                }

                operations[^1] = new(receiver, receiver.BoundType, requiredReceiver, kind, quality, receiverPath, receiverSlot, AdaptedType: adaptedReceiver);
            }

            var ordinaryPasses = contextualInputs ? 2 : 1;
            for (var pass = 0; pass < ordinaryPasses + (contextualCallables ? 1 : 0); pass++)
            {
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    var argument = KotoHelper.UnwrapParentheses(call.ArgumentNodes[i]);
                    if (argument is FunctionKoto { IsAnonymous: true } || IsWaitingFunctionReference(argument) ? pass != ordinaryPasses :
                        pass == ordinaryPasses || (contextualInputs && (NeedsEnumContext(argument) || IsAggregateArgument(argument)) != (pass == 1)))
                    {
                        continue;
                    }

                    var type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType, lengths);
                    if (type is null)
                    {
                        var pattern = function.Parameters[mapping[i]].Type.BoundType!;
                        var slotType = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq } ? pattern.Components[0] : pattern;
                        if (IsWaitingCallable(argument) &&
                            slotType.Kind == BoundTypeKind.Parameter && ContainerSlot(CallSlotOwner(function), slotType.Symbol!) is var slot && slot >= 0 && arguments[slot] is null)
                        {
                            BoundType? signature = null;
                            if (this.TryCallable(slotType, this.ConstraintScope(function), out var required, out _))
                            {
                                signature = this.CallType(required, function, arguments, scope, self, origins, inputs, declaringType, lengths);
                                if (signature is null)
                                {
                                    // SPEC 10.8: every other piece of evidence is in, so a slot of S that is still unbound stays unsolved; the
                                    // candidate stays applicable on its other checks, and the header parts it writes were matched by
                                    // InferClosureHeader. Anything else S lacks keeps the candidate pending.
                                    if (!MentionsSlots(required, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
                                    {
                                        return CandidateApplicability.Pending;
                                    }

                                    openSignatures.Add(i);
                                }
                                else if (argument is FunctionKoto anonymous && !this.ClosureSignatureFits(anonymous, signature))
                                {
                                    return CandidateApplicability.Inapplicable;
                                }
                            }

                            // SPEC 10.5: without a Callable Constraint on F the candidate supplies no expectation; it is compared under
                            // 10.4 without the body, and only if it is selected is the argument checked without S.
                            waitingCallables = true;
                            operations[i] = new(call.ArgumentNodes[i], null, signature, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: mapping[i]);
                            continue;
                        }

                        // Default only otherwise unconstrained literals; all established inputs were processed above.
                        if (this.LiteralDefault(argument) is not { } literalDefault)
                        {
                            if (!IsUnfittedLiteral(argument) && MentionsSlots(pattern, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
                            {
                                // SPEC 10.8: an argument that supplies no evidence, such as an omitted header at `(T) -> i32` or `.None` at
                                // Option<T>, at a parameter Type that holds a slot no evidence binds: the position stays open and the
                                // candidate stays applicable on its other checks; it is completed only if the candidate is selected.
                                open.Add(i);
                                var adaptation = pattern.Kind == BoundTypeKind.Function && IsWaitingCallable(argument) ? ArgumentAdaptation.Erasure : ArgumentAdaptation.Exact;
                                operations[i] = new(call.ArgumentNodes[i], null, null, ArgumentOperationKind.Value, adaptation, ParameterIndex: mapping[i]);
                                continue;
                            }

                            return CompleteArguments() ? CandidateApplicability.Inapplicable : CandidateApplicability.Pending;
                        }

                        if (!InferInput(function.Parameters[mapping[i]].Type.BoundType!, literalDefault, argument))
                        {
                            if (DifferentRangeShapes(literalDefault, function.Parameters[mapping[i]].Type.BoundType!))
                            {
                                operations[i] = new(call.ArgumentNodes[i], literalDefault, function.Parameters[mapping[i]].Type.BoundType!, ArgumentOperationKind.Value, ArgumentAdaptation.Literal, ParameterIndex: mapping[i]);
                            }

                            return CandidateApplicability.Inapplicable;
                        }

                        type = this.CallType(function.Parameters[mapping[i]].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType, lengths);
                        if (type is null)
                        {
                            if (MentionsSlots(pattern, CallSlotOwner(function), UnboundTypeSlots(function, arguments, ref unboundSlots)))
                            {
                                // Literal defaults cannot invert a projection or an unresolved selector.
                                open.Add(i);
                                operations[i] = new(call.ArgumentNodes[i], null, null, ArgumentOperationKind.Value, ArgumentAdaptation.Literal, ParameterIndex: mapping[i]);
                                continue;
                            }

                            return CandidateApplicability.Pending;
                        }
                    }

                    if (IsWaitingNestedCall(argument))
                    {
                        waitingCallables = true;
                        operations[i] = new(call.ArgumentNodes[i], null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (IsAggregateArgument(argument))
                    {
                        var borrow = type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref };
                        var valueType = borrow ? type.Components[0] : type;
                        var applicability = this.ProbeAggregateArgument(argument, valueType, scope);
                        if (applicability != CandidateApplicability.Applicable)
                        {
                            return applicability;
                        }

                        operations[i] = new(
                            call.ArgumentNodes[i],
                            valueType,
                            type,
                            borrow ? ArgumentOperationKind.Borrow : ArgumentOperationKind.Value,
                            borrow ? ArgumentAdaptation.CrossSemanticsBorrow : ArgumentAdaptation.Literal,
                            ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (argument is FunctionKoto { IsAnonymous: true, BoundType: null } closure)
                    {
                        if (!this.ClosureSignatureFits(closure, type))
                        {
                            return CandidateApplicability.Inapplicable;
                        }

                        operations[i] = new(call.ArgumentNodes[i], null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (argument is { BoundType: null, BindingState: BindingState.Resolved, BoundSymbol: { Kind: BindingSymbolKind.Function } group })
                    {
                        // SPEC 7.6.4: a function group argument fits when one of its functions converts to the parameter's common
                        // Function Type; the reference is bound to that function after selection.
                        var perCallOnly = false;
                        if (type.Kind != BoundTypeKind.Function || !this.FunctionGroupFits(argument, group, type, scope))
                        {
                            if (type.Kind == BoundTypeKind.Function && this.PerCallStandIn(type, call.ArgumentNodes[i], type, true) is { } instantiated &&
                                this.FunctionGroupFits(argument, group, instantiated, scope))
                            {
                                // SPEC 10.7, 10.8: the argument's per-call input is instantiated to a required input over a fixed Origin.
                                this.InstantiateOpenOrigins(arguments.AsSpan(0, CallOwnSlots(function).Count), call.ArgumentNodes[i], type);
                                type = instantiated;
                            }
                            else if (type.Kind != BoundTypeKind.Function || this.PerCallStandIn(type, call.ArgumentNodes[i], type) is not { } standIn ||
                                !this.FunctionGroupFits(argument, group, standIn, scope))
                            {
                                return CandidateApplicability.Inapplicable;
                            }
                            else
                            {
                                // SPEC 15.3.6: a fit that only the argument's own per-call Origin would satisfy is reported after selection.
                                perCallOnly = true;
                            }
                        }

                        operations[i] = new(call.ArgumentNodes[i], null, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, ParameterIndex: mapping[i], MissingOrigin: perCallOnly);
                        continue;
                    }

                    if (IsUnfittedLiteral(argument) && type is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } borrowed &&
                        (ScalarTypes.Supports(borrowed.Components[0]) || this.TakesGenericLiterals(borrowed.Components[0], scope) || this.IsSyntaxPositionType(borrowed.Components[0])) && this.FitsInputLiteral(argument, borrowed.Components[0], scope))
                    {
                        // SPEC 10.2: a literal owner temporary is fitted to T, materialized once and shared-borrowed;
                        // its Place Origin binds the parameter's input Origin like any other borrowed temporary.
                        if (!InferInput(function.Parameters[mapping[i]].Type.BoundType!, borrowed.Components[0], argument))
                        {
                            return CandidateApplicability.Inapplicable;
                        }

                        var literalBorrow = this.InternType(BoundTypeKind.Semantics, null, SemanticsKind.Ref, [borrowed.Components[0]], origin: this.PlaceOrigin(argument));
                        operations[i] = new(call.ArgumentNodes[i], borrowed.Components[0], type, ArgumentOperationKind.Borrow, ArgumentAdaptation.CrossSemanticsBorrow, ParameterIndex: mapping[i], AdaptedType: literalBorrow);
                        continue;
                    }

                    if (!this.FitsInputLiteral(argument, type, scope))
                    {
                        if (this.LiteralDefault(argument) is { } literalType && DifferentRangeShapes(literalType, type))
                        {
                            operations[i] = new(call.ArgumentNodes[i], literalType, type, ArgumentOperationKind.Value, ArgumentAdaptation.Literal, ParameterIndex: mapping[i]);
                        }

                        return CandidateApplicability.Inapplicable;
                    }

                    if (NeedsEnumContext(argument))
                    {
                        var applicability = this.ProbeContextualEnum(argument, type, scope);
                        if (applicability != CandidateApplicability.Applicable)
                        {
                            return applicability;
                        }

                        operations[i] = new(call.ArgumentNodes[i], type, type, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (argument.BoundType is { } closureType && this.ErasureSignatureFits(closureType, type, argument))
                    {
                        // SPEC 7.6.4: a concrete Closure value converts to the parameter's common Function Type, as at an
                        // initialization; the argument is bound with that expectation once the call is selected. Its receiver and
                        // Owned conditions are judged then (SPEC 10.5).
                        operations[i] = new(call.ArgumentNodes[i], closureType, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (argument.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } instantiatedValue && type.Kind == BoundTypeKind.Function &&
                        this.PerCallStandIn(type, call.ArgumentNodes[i], type, true) is { } instantiatedType && this.ErasureSignatureFits(instantiatedValue, instantiatedType, argument))
                    {
                        // SPEC 10.7, 10.8: the value's per-call input is instantiated to a required input over a fixed Origin.
                        this.InstantiateOpenOrigins(arguments.AsSpan(0, CallOwnSlots(function).Count), call.ArgumentNodes[i], type);
                        operations[i] = new(call.ArgumentNodes[i], instantiatedValue, instantiatedType, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, ParameterIndex: mapping[i]);
                        continue;
                    }

                    if (argument.BoundType is { Kind: BoundTypeKind.Closure or BoundTypeKind.FunctionItem } perCallValue && type.Kind == BoundTypeKind.Function &&
                        this.PerCallStandIn(type, call.ArgumentNodes[i], type) is { } valueStandIn && this.ErasureSignatureFits(perCallValue, valueStandIn, argument))
                    {
                        // SPEC 15.3.6: only the value's own per-call Origin would satisfy the slot; reported after selection.
                        operations[i] = new(call.ArgumentNodes[i], perCallValue, type, ArgumentOperationKind.Value, ArgumentAdaptation.Erasure, ParameterIndex: mapping[i], MissingOrigin: true);
                        continue;
                    }

                    var quality = ArgumentAdaptation.Literal;
                    var kind = ArgumentOperationKind.Value;
                    BoundType? adaptedType = null;
                    if (argument.BoundType is { } actual)
                    {
                        if (!this.AdaptInput(argument, type, actual, scope, null, null, out var adapted, out quality, out kind, deferAcquisition: true) || !this.FitsStructurallyAt(adapted, type, call))
                        {
                            return CandidateApplicability.Inapplicable;
                        }

                        adaptedType = adapted;
                    }

                    operations[i] = new(call.ArgumentNodes[i], argument.BoundType, type, kind, quality, ParameterIndex: mapping[i], AdaptedType: adaptedType);
                }
            }

            // Fitted literals can add Origin evidence after the first contextual pass.
            // Publish only the final substituted parameter Types, never preliminary binders.
            if (originInference is not null && !this.SolveCallOriginInference(function, originInference, origins, inputs, call, declaringType, select: true))
            {
                return CandidateApplicability.Inapplicable;
            }

            // SPEC 10.8: required structural slots remain rankable. An unresolved length or an
            // open position closed by a later literal default still keeps the candidate pending.
            UnsolvedSlots(call, function, arguments, lengths, mapping, ref unsolvedSlots, out var unrepresentable);
            unrepresentable |= this.environmentEvidence;
            if ((!open.IsEmpty || !openSignatures.IsEmpty) && (unrepresentable || !this.OpenPositionsHold(call, function, arguments, mapping, open, openSignatures, unsolvedSlots, scope, self, origins, inputs, declaringType, lengths)))
            {
                return CandidateApplicability.Pending;
            }

            if (unrepresentable)
            {
                unsolvedSlots.Clear();
            }

            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var completed = this.CallType(function.Parameters[i].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType, lengths);
                // SPEC 10.8: a parameter Type that holds an unsolved slot, an open position or an unused default, is no failure of its own.
                if (completed is null ? !(waitingCallables || MentionsSlots(function.Parameters[i].Type.BoundType!, CallSlotOwner(function), unsolvedSlots)) : !this.ProveTypeLengths(completed, scope.Function))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }

            CompleteOperationTypes(operations);

            var resultPattern = function.IsConstructor && declaringType is not null ? this.SelfType(declaringType.Symbol!) : function.BoundSymbol!.Type;
            var result = resultPattern is not null ? this.CallType(resultPattern, function, arguments, scope, self, origins, inputs, declaringType, lengths) : null;
            if (result is not null && !this.ProveTypeLengths(result, scope.Function))
            {
                return CandidateApplicability.Inapplicable;
            }

            if (result is not null && HasUnsubstitutedOrigin(result, function) && this.OpenResultOnlyOrigins(call, function, origins))
            {
                // SPEC 15.3.6: an Origin of the callee that nothing at the call bounds, such as the result-only `s` of `constant() ->
                // ref/i32 during s` initializing an unannotated local, is a local region of the call, never a pending candidate.
                result = this.CallType(resultPattern!, function, arguments, scope, self, origins, inputs, declaringType, lengths);
            }

            // SPEC 10.8: a result that keeps an unsolved slot is no failure of its own; the parts of its fit that do not contain the slot are
            // still checked against an expected result (SPEC 10.3).
            var openResult = result is null && resultPattern is not null && MentionsSlots(resultPattern, CallSlotOwner(function), unsolvedSlots);
            if ((result is null && !waitingCallables && !openResult) || (result is not null && HasUnsubstitutedOrigin(result, function)))
            {
                // Result-only Origin inference needs the later call-site solver. Never retain a
                // requirement's abstract binder as though it were this call's concrete Origin.
                return result is null && CompleteArguments() ? CandidateApplicability.Inapplicable : CandidateApplicability.Pending;
            }

            if (placeExpected is not null && result is { Kind: BoundTypeKind.Semantics, Components.Count: 1 } placeResult)
            {
                if (!this.FitsStructurallyAt(placeResult.Components[0], this.ContractType(placeExpected, scope), call))
                {
                    return CandidateApplicability.Inapplicable;
                }
            }
            else if (expected is not null && result is not null && !this.FitsStructurallyAt(result, this.ContractType(expected, scope), call) &&
                this.ExpectedAdaptation(call, result, this.ContractType(expected, scope), recordPair: false) is null)
            {
                return CandidateApplicability.Inapplicable;
            }
            else if (expected is not null && result is null && openResult && !expectedStructure)
            {
                return CandidateApplicability.Inapplicable;
            }

            var proof = ProveCandidate(arguments.AsSpan(0, CallOwnSlots(function).Count), waitingCallables, unsolvedSlots);
            if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown && !waitingCallables && PerCallCallables(operations))
            {
                proof = ConstraintProof.Proven;
            }

            if (waitingCallables && proof == ConstraintProof.Unknown &&
                this.ResolvedClauseUnknown(function.TypeConstraints, function, arguments.AsSpan(0, CallSlotCount(function)), scope, self, function.IsConstructor ? null : declaringType, lengths.AsSpan(0, CallOwnSlots(function).Count), slotOwner: CallSlotOwner(function)))
            {
                // SPEC 10.5, 8.7: a Constraint that no waiting argument leaves open, such as a Callable clause on an F that an independent
                // argument binds, waits on nothing; Unknown proves neither applicability nor negation, so another candidate is never
                // selected past it.
                return CandidateApplicability.Pending;
            }

            if (waitingCallables && proof is ConstraintProof.Proven or ConstraintProof.Unknown)
            {
                // Only the concrete Types of waiting anonymous arguments may remain open. A body cannot solve an outer input/result slot,
                // even if its inferred signature would happen to provide that Type: such a slot is unsolved (SPEC 10.8).
                if (unrepresentable)
                {
                    return CandidateApplicability.Pending;
                }

                unsolved = !unsolvedSlots.IsEmpty;
                return CandidateApplicability.Waiting;
            }

            if (proof == ConstraintProof.Refuted && this.ClosureReceiverMismatch(call, function, mapping) is { } mismatch)
            {
                // SPEC 7.6.3, 8.6: a closure's minimum call receiver is the candidate's failure only when the candidate applies once that
                // receiver is permitted; a rejected candidate then names both receivers (NoApplicableOverload_Kd).
                this.permitClosureReceivers = true;
                try
                {
                    closureReceiver = ProveCandidate(arguments.AsSpan(0, CallOwnSlots(function).Count), waitingCallables) == ConstraintProof.Proven ? mismatch : null;
                }
                finally
                {
                    this.permitClosureReceivers = false;
                }
            }

            // Retain only a condition actually reached after the argument/result checks, before scratch is reused.
            if (proof == ConstraintProof.Refuted && closureReceiver is null)
            {
                constraintFailure = this.CandidateConstraintFailure(function, arguments, lengths, scope, self, declaringType, ConstraintProof.Refuted);
            }

            unsolved = proof == ConstraintProof.Proven && !unsolvedSlots.IsEmpty;
            premiseUnknown = proof == ConstraintProof.Unknown; // SPEC 8.4.8.2: every argument and result check has passed.
            return proof switch
            {
                ConstraintProof.Proven => CandidateApplicability.Applicable,
                ConstraintProof.Refuted => CandidateApplicability.Inapplicable,
                ConstraintProof.Error => CandidateApplicability.Error,
                _ => CandidateApplicability.Pending,
            };
            bool InferAggregateInputs(bool fitLiterals)
            {
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    if (!IsAggregateArgument(call.ArgumentNodes[i]))
                    {
                        continue;
                    }

                    if (this.CallMemberPattern(function.Parameters[mapping[i]].Type.BoundType!, function, declaringType) is not { } pattern)
                    {
                        return false;
                    }

                    pattern = this.ContractType(pattern, scope, self);
                    if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref })
                    {
                        if (pattern.Origin is { } origin)
                        {
                            this.MatchInputOrigin(origin, this.PlaceOrigin(call.ArgumentNodes[i]), function, origins, inputs);
                        }

                        pattern = pattern.Components[0];
                    }

                    if (!this.InferAggregateCall(pattern, call.ArgumentNodes[i], function, arguments, lengths, origins, inputs, fitLiterals))
                    {
                        return false;
                    }
                }

                return true;
            }

            bool InferInput(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
            {
                if (this.CallMemberPattern(pattern, function, declaringType) is not { } memberPattern)
                {
                    return false;
                }

                pattern = this.ContractType(memberPattern, scope, self);
                var fixedOwner = !fixedConstruction && call.Method is MemberAccessKoto member && this.inferredConstructorTargets.Contains(member.Left) ? member.Left.BoundType : declaringType;
                var adaptationPattern = function.IsConstructor ? this.MemberType(pattern, fixedOwner) ?? pattern : pattern;
                if (!this.AdaptInput(source, adaptationPattern, actual, scope, path, declaringType, out actual, out _, out _, receiver: receiver, deferAcquisition: true))
                {
                    return false;
                }

                var callee = actual;
                if (pattern.Kind == BoundTypeKind.Function && this.FunctionItemSignature(actual) is { } itemSignature)
                {
                    actual = itemSignature;
                }
                else if (pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Closure &&
                    actual.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure })
                {
                    // SPEC 7.6.4, 10.5: a concrete Closure meets a common Function Type through its signature; the conversion itself,
                    // its receiver and Owned conditions included, is judged once the call is selected.
                    actual = closure.Signature;
                }

                pattern = this.FormedApplication(pattern, CallSlotOwner(function), arguments);
                var known = pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function ? new SignatureEvidence(CalleeBinder(callee, actual), source) : (SignatureEvidence?)null;
                var quantifier = known is { } evidence ? evidence.Binder ?? evidence.Source : null; // The argument itself binds nothing.
                this.MatchInputOrigins(pattern, actual, function, origins, inputs, quantifier);
                if (pattern.CarriesOrigin && actual.CarriesOrigin)
                {
                    originInference ??= this.BeginOriginInference(call, function);
                    this.CollectOriginInference(pattern, actual, originInference, known: quantifier);
                    this.CollectInputPremises(pattern, originInference, call);
                }

                pattern = this.SubstituteStoredOrigins(pattern, function, origins.AsSpan(0, function.Origins.Count), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(function))));
                // SPEC 10.5: a nested call checked against this candidate supplies relations for checking, never outer slot evidence.
                return WasWaitingNestedCall(source) || this.Infer(pattern, actual, CallSlotOwner(function), arguments, true, lengths, generic is null && (!function.IsConstructor || (!fixedConstruction && call.Method is MemberAccessKoto inferredMember && this.inferredConstructorTargets.Contains(inferredMember.Left))), structural: pattern.Kind == BoundTypeKind.Function, evidence: known, relateLater: true);
            }

            bool InferClosureHeader(BoundType signature, FunctionKoto literal)
            {
                if (this.CallMemberPattern(signature, function, declaringType) is not { } memberSignature)
                {
                    return false;
                }

                signature = this.ContractType(memberSignature, scope, self);
                var parameterTypes = signature.Components[0];
                var count = ReferenceEquals(parameterTypes, BoundType.Unit) ? 0 : parameterTypes.Components.Count;
                if (literal.Parameters.Count != count)
                {
                    return false;
                }

                var headerScope = this.scopes[literal];
                for (var i = 0; i < count; i++)
                {
                    var written = literal.Parameters[i].Type;
                    if (written is not SyntaxFormKoto { Akind: KotoKind.InferredType } &&
                        (this.BindType(written, headerScope) is not { } actual ||
                        !this.Infer(parameterTypes.Components[i], actual, CallSlotOwner(function), arguments, lengths: lengths, structural: true, evidence: new(literal, literal))))
                    {
                        return false;
                    }
                }

                return literal.ReturnType is null || (this.BindType(literal.ReturnType, headerScope) is { } result &&
                    this.Infer(signature.Components[1], result, CallSlotOwner(function), arguments, lengths: lengths, structural: true, evidence: new(literal, literal)));
            }

            bool InferCallableSignatures(Span<BoundArgumentOperation> signatureOperations)
            {
                // SPEC 10.8: the Type bound to F also supplies its independently known call signature. This is evidence
                // alongside ordinary inputs, before expected results and literal defaults. An anonymous waiting body is
                // never evidence; its written header is handled separately. The signature's own Origins become open regions.
                for (var i = 0; i < call.ArgumentNodes.Count; i++)
                {
                    var source = call.ArgumentNodes[i];
                    var pattern = function.Parameters[mapping[i]].Type.BoundType!;
                    var callableSlot = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
                        ? pattern.Components[0] : pattern;
                    if (callableSlot.Kind != BoundTypeKind.Parameter || ContainerSlot(CallSlotOwner(function), callableSlot.Symbol!) < 0 ||
                        KotoHelper.UnwrapParentheses(source) is FunctionKoto { IsAnonymous: true } || WasWaitingNestedCall(source) || source.BoundType is not { } actual ||
                        !this.TryCallable(callableSlot, this.ConstraintScope(function), out var requiredSignature, out _) ||
                        !this.TryCallable(actual, scope, out var actualSignature, out _))
                    {
                        continue;
                    }

                    if (this.CallMemberPattern(requiredSignature, function, declaringType) is not { } memberSignature)
                    {
                        return false;
                    }

                    requiredSignature = this.ContractType(memberSignature, scope, self);
                    if (!this.Infer(requiredSignature, actualSignature, CallSlotOwner(function), arguments, lengths: lengths, structural: true, evidence: new(CalleeBinder(actual, actualSignature), source)))
                    {
                        var required = this.CallType(requiredSignature, function, arguments, scope, self, origins, inputs, declaringType, lengths) ?? requiredSignature;
                        signatureOperations[i] = new(source, actualSignature, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);

                        return false;
                    }
                }

                return true;
            }

            // SPEC 15.3.6, 10.8: a Callable proof that fails only because a slot holds the open region of an argument's own per-call Origin,
            // and holds once that per-call Origin stands in for it, is no inapplicability: the argument is reported after selection.
            bool PerCallCallables(Span<BoundArgumentOperation> marks)
            {
                var count = CallOwnSlots(function).Count;
                var restored = this.typeScratch.Rent(count);
                try
                {
                    // SPEC 10.7, 10.8: first instantiate each argument's per-call input at a required input over a fixed Origin; when every
                    // Constraint then holds, that instantiation solves the slots and nothing is reported.
                    arguments.AsSpan(0, count).CopyTo(restored);
                    if (StandIns(restored.AsSpan(0, count), marks, true) && ProveCandidate(restored.AsSpan(0, count), false) == ConstraintProof.Proven)
                    {
                        restored.AsSpan(0, count).CopyTo(arguments);
                        CompleteOperationTypes(marks);
                        return true;
                    }

                    arguments.AsSpan(0, count).CopyTo(restored);
                    var marked = StandIns(restored.AsSpan(0, count), marks, false);
                    if (!marked || ProveCandidate(restored.AsSpan(0, count), false) != ConstraintProof.Proven)
                    {
                        for (var i = 0; i < call.ArgumentNodes.Count && marked; i++)
                        {
                            marks[i] = marks[i] with { MissingOrigin = false };
                        }

                        return false;
                    }

                    return true;
                }
                finally
                {
                    this.typeScratch.Return(restored, clearArray: true);
                }

                // Replaces the open regions of each argument whose Callable signature holds one, marking that argument unless `fixedOnly`.
                bool StandIns(Span<BoundType?> slots, Span<BoundArgumentOperation> targets, bool fixedOnly)
                {
                    var replaced = false;
                    for (var i = 0; i < call.ArgumentNodes.Count; i++)
                    {
                        var source = call.ArgumentNodes[i];
                        var pattern = function.Parameters[mapping[i]].Type.BoundType!;
                        var callableSlot = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } ? pattern.Components[0] : pattern;
                        if (callableSlot.Kind != BoundTypeKind.Parameter || ContainerSlot(CallSlotOwner(function), callableSlot.Symbol!) < 0 || source.BoundType is null ||
                            !this.TryCallable(callableSlot, this.ConstraintScope(function), out var requiredSignature, out _) ||
                            this.CallMemberPattern(requiredSignature, function, declaringType) is not { } memberSignature ||
                            this.CallType(this.ContractType(memberSignature, scope, self), function, arguments, scope, self, origins, inputs, declaringType, lengths) is not { } required ||
                            this.PerCallStandIn(required, source, required, fixedOnly) is null)
                        {
                            continue;
                        }

                        for (var g = 0; g < slots.Length; g++)
                        {
                            if (slots[g] is { } solution)
                            {
                                slots[g] = this.ReplaceOpenOrigins(solution, source, required, fixedOnly);
                            }
                        }

                        if (!fixedOnly)
                        {
                            targets[i] = targets[i] with { MissingOrigin = true };
                        }

                        replaced = true;
                    }

                    return replaced;
                }
            }

            void CompleteOperationTypes(Span<BoundArgumentOperation> targets)
            {
                for (var i = 0; i < targets.Length; i++)
                {
                    if (targets[i] is { Source: not null, ParameterIndex: >= 0 } operation)
                    {
                        var completed = this.CallType(function.Parameters[operation.ParameterIndex].Type.BoundType!, function, arguments, scope, self, origins, inputs, declaringType, lengths)!;
                        if (completed is not null)
                        {
                            targets[i] = operation with { ParameterType = completed };
                        }
                    }
                }
            }

            // SPEC 10.8: the Constraints and premises whose judgment needs an unsolved slot are not judged.
            ConstraintProof ProveCandidate(ReadOnlySpan<BoundType?> slots, bool incomplete, SlotSet unbound = default)
            {
                var proof = this.CheckConstraints(function.TypeConstraints, function, function.IsConstructor ? arguments.AsSpan(0, CallSlotCount(function)) : slots, scope, self, function.IsConstructor ? null : declaringType, lengths, incomplete: incomplete || !unbound.IsEmpty, skipUnresolved: !unbound.IsEmpty, slotOwner: CallSlotOwner(function));
                if (declaringType is not null)
                {
                    var closedOwner = function.IsConstructor ? this.ConstructionType(function, declaringType, arguments) : declaringType;
                    if (closedOwner is not null)
                    {
                        proof = CombineProof(proof, this.CheckTypeConstraints(closedOwner, scope), true);
                    }
                    else if (function.IsConstructor && CallSlotOwner(function) is StructKoto owner)
                    {
                        proof = CombineProof(proof, this.CheckConstraints(owner.ConstraintNodes, owner, arguments.AsSpan(0, CallSlotCount(function)), scope, incomplete: true, skipUnresolved: !unbound.IsEmpty), true);
                    }
                }

                proof = CombineProof(proof, this.CheckSignatureTypeConstraints(function), true);
                // A substitution must be a valid complete Type independently of whether
                // the function constrains or uses that slot (SPEC 8.1.3).
                for (var i = 0; i < slots.Length; i++)
                {
                    var argumentProof = CallOwnSlots(function)[i] is LengthParameterKoto
                        ? lengths[i] is not null ? ConstraintProof.Proven : ConstraintProof.Unknown
                        : slots[i] is { } argument ? this.CheckTypeConstraints(argument, scope)
                        : unbound.Contains(i) ? ConstraintProof.Proven : ConstraintProof.Unknown;
                    proof = CombineProof(proof, argumentProof, true);
                }

                return CombineProof(proof, this.ProveMemberConditions(function.BoundSymbol!, declaringType, scope), true);
            }

            bool CompleteArguments()
            {
                for (var i = 0; i < CallOwnSlots(function).Count; i++)
                {
                    if (CallOwnSlots(function)[i] is LengthParameterKoto ? lengths[i] is null : arguments[i] is null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }
        finally
        {
            this.ReturnSlotSet(unsolvedSlots);
            this.ReturnSlotSet(unboundSlots);
            this.ReturnSlotSet(openSignatures);
            this.ReturnSlotSet(open);
            this.ReturnSlotSet(waiting);
        }
    }

    // SPEC 8.1.2: an s/U whose s is already inferred is matched as the Type it forms: U for owner, one reference layer with
    // the occurrence's outer-Origin slot for ref or uniq. An application whose s is still open is returned as it is.
    private BoundType FormedApplication(BoundType pattern, Koto function, BoundType?[] arguments)
    {
        for (var depth = 0; depth < 16; depth++)
        {
            if (pattern is not { Kind: BoundTypeKind.SemanticsApplication, Symbol: { } selector, Components: [var target] } ||
                ContainerSlot(function, selector) is not (>= 0 and var slot) || slot >= arguments.Length || arguments[slot] is not { } whole)
            {
                break;
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                pattern = target;
            }
            else if (whole.Semantics is SemanticsKind.Ref or SemanticsKind.Uniq)
            {
                pattern = this.InternType(BoundTypeKind.Semantics, null, whole.Semantics, [target], origin: pattern.Origin);
                break;
            }
            else
            {
                break;
            }
        }

        return pattern;
    }

    // With `evidence`, the actual is a known call signature (SPEC 10.5, 10.8): an argument's, or the parts an anonymous header writes.
    // Its structure and Semantics are evidence, and its Origins are not compared: applicability judges no Origin part (SPEC 15.6.1),
    // and the argument fits the substituted parameter after selection. An Origin the signature itself quantifies, such as a per-call
    // input, lies beyond the call and never solves an Origin in a slot: it becomes an open region that other evidence fills.
    // With `relateLater`, the actual is a value's Type whose Origin relations to the solution are judged after selection (SPEC 15.6.1),
    // so Origin evidence never fails a structurally equal slot binding; `invariant` marks a position below an exclusive layer or an
    // invariant schema slot.
    private bool Infer(BoundType pattern, BoundType actual, Koto function, BoundType?[] arguments, bool inferOrigins = false, BoundLength?[]? lengths = null, bool commonOrigins = false, bool structural = false, SignatureEvidence? evidence = null, bool relateLater = false, bool invariant = false)
    {
        // A Never-valued expression supplies no input value; a Never Type inside a known signature is exact evidence.
        if (!structural && ReferenceEquals(actual, BoundType.Never))
        {
            return true;
        }

        if (pattern.Kind == BoundTypeKind.AssociatedProjection)
        {
            // An associated Type is not an injective constructor: infer its receiver from
            // other inputs, then check the substituted complete parameter in candidate fitting.
            return true;
        }

        if (pattern.Kind == BoundTypeKind.TargetProjection && pattern.Symbol is { } projected &&
            ContainerSlot(function, projected) is >= 0 and var projectionSlot)
        {
            // The target alone does not determine a pair's Semantics. Once another input, an explicit argument or the
            // result fixes the whole Type, match its direct target; final candidate fitting checks a still-open projection.
            return projectionSlot < arguments.Length && (arguments[projectionSlot] is not { } whole ||
                this.Infer(this.DirectTarget(whole), actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant));
        }

        if (pattern.Kind == BoundTypeKind.SemanticsApplication && pattern.Symbol is { } selector &&
            ContainerSlot(function, selector) is var selectorSlot && selectorSlot >= 0 && selectorSlot < arguments.Length)
        {
            if (arguments[selectorSlot] is not { } whole)
            {
                return true; // SPEC 10.2: solved together; the substituted parameter is checked once another argument fixes s/T.
            }

            if (whole.Semantics == SemanticsKind.Owner)
            {
                return this.Infer(pattern.Components[0], actual, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
            }

            return actual.Kind == BoundTypeKind.Semantics && actual.Semantics == whole.Semantics &&
                this.Infer(pattern.Components[0], actual.Components[0], function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant || whole.Semantics is SemanticsKind.Uniq or SemanticsKind.ObjUniq or SemanticsKind.Raw);
        }

        // SPEC 3.1.1.1: the wrapping Scalar Wrapping<u8> is the instance of the pattern Wrapping<T>, so T is inferred from
        // the Scalar's integer argument; the substituted parameter then normalizes to the same Scalar.
        if (actual.IsWrappingInteger && pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 1 } && pattern.Symbol?.LibraryDeclaration == KimiDeclarationId.Wrapping)
        {
            return this.Infer(pattern.Components[0], actual.Underlying, function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, invariant);
        }

        if (pattern.Kind == BoundTypeKind.Parameter && ContainerSlot(function, pattern.Symbol!) is var slot && slot >= 0)
        {
            if ((uint)slot >= (uint)arguments.Length)
            {
                return false;
            }

            if (HasArrayLengthHole(actual))
            {
                // An incomplete annotation cannot become a generic argument. Established argument evidence is checked
                // against it at the ordinary result fit; without such evidence the slot remains unsolved.
                return arguments[slot] is not { } establishedArray || FitsStructurally(establishedArray, actual);
            }

            // SPEC 10.2.1: a parameter constrained to Position, PositionRange or PrimitiveInteger binds the referent; the
            // argument is then value-read.
            if (!structural && actual is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 } &&
                ComparisonReferent(actual) is var referent && !ReferenceEquals(referent, actual) && this.InfersReadReferent(pattern, this.activeInferenceFunction is { } inference && ReferenceEquals(CallSlotOwner(inference), function) ? inference : function))
            {
                actual = referent;
            }

            if (evidence is { Binder: FunctionKoto { BoundClosure: not null } closure } && HasEnvironmentOrigin(actual, closure))
            {
                // SPEC 8.6, 7.6.4: a result over the closure's hidden environment receiver satisfies no Callable signature and no
                // erasure, so that part is no evidence for the slot; the Callable proof or the conversion judges it against the rest.
                this.environmentEvidence = true;
                return true;
            }

            if (evidence is { Binder: { } binder } known)
            {
                actual = this.OpenKnownOrigins(actual, binder, known.Source);
            }

            if (arguments[slot] is { } previous)
            {
                if (!ReferenceEquals(previous, actual) && (HasOpenOrigin(previous) || HasOpenOrigin(actual)))
                {
                    // SPEC 10.8: no argument fixes a slot first by traversal order. Another argument's Origin at an open position
                    // solves it, whichever comes first; two open positions keep the earlier region.
                    previous = this.FillOpenOrigins(previous, actual, false);
                    actual = this.FillOpenOrigins(actual, previous, true);
                }

                if (relateLater && !structural && !ReferenceEquals(previous, actual) && FitsStructurally(actual, previous) && FitsStructurally(previous, actual))
                {
                    // SPEC 10.8, 15.3.6: Origin evidence for a slot never makes the candidate inapplicable; each argument's relation to
                    // the solution is judged after selection (SPEC 15.6.1). An explicit Type argument is kept, an invariant binding is
                    // kept over covariant ones and the first invariant one over later ones, whatever the argument order, and covariant
                    // bindings meet.
                    if (!commonOrigins || this.invariantSlots.Contains(slot))
                    {
                        arguments[slot] = previous;
                    }
                    else if (invariant)
                    {
                        arguments[slot] = actual;
                        this.invariantSlots.Add(slot);
                    }
                    else
                    {
                        arguments[slot] = FitsType(actual, previous) ? previous : this.CommonOriginType(previous, actual) ?? previous;
                    }

                    return true;
                }

                // SPEC 10.8: structural matching compares normalized Types, so two spellings of one Function Type, whose per-call
                // inputs have distinct binders, are one binding.
                if (ReferenceEquals(previous, actual) || (!structural && inferOrigins && FitsType(actual, previous)) ||
                    (structural && previous.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function && FitsType(actual, previous) && FitsType(previous, actual)))
                {
                    arguments[slot] = previous;
                    return true;
                }

                if (commonOrigins && this.CommonOriginType(previous, actual) is { } common)
                {
                    arguments[slot] = common;
                    return true;
                }

                return false;
            }

            arguments[slot] = actual;
            if (relateLater && invariant)
            {
                this.invariantSlots.Add(slot);
            }

            return true;
        }

        if (ReferenceEquals(pattern, actual))
        {
            return true;
        }

        if (pattern.Kind != actual.Kind || pattern.ResultMode != actual.ResultMode || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics ||
            !(lengths is not null && pattern.Kind == BoundTypeKind.FixedArray ? this.InferLength(pattern, actual, function, lengths) : pattern.Length == actual.Length && ReferenceEquals(pattern.LengthExpression, actual.LengthExpression)) ||
            (!inferOrigins && evidence is null && !ReferenceEquals(pattern.Origin, actual.Origin)) || pattern.OriginArguments.Count != actual.OriginArguments.Count || pattern.Components.Count != actual.Components.Count || pattern.LengthArguments.Length != actual.LengthArguments.Length || (pattern.Components.Count == 0 && pattern.LengthArguments.Length == 0 && pattern.OriginArguments.Count == 0))
        {
            return false;
        }

        for (var i = 0; i < pattern.LengthArguments.Length; i++)
        {
            var required = pattern.LengthArguments[i];
            var supplied = actual.LengthArguments[i];
            if (!ReferenceEquals(required, supplied) && (required is null || supplied is null || lengths is null || !this.InferLength(required, supplied, function, lengths)))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.OriginArguments.Count; i++)
        {
            if (!inferOrigins && evidence is null && !ReferenceEquals(pattern.OriginArguments[i], actual.OriginArguments[i]))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            var below = invariant || IsInvariantLayer(pattern, this) ||
                (pattern.Kind == BoundTypeKind.Constructed && pattern.Symbol?.Schema is { } schema && i < schema.GenericSlots.Count && schema.GenericSlots[i].OriginVariance is OriginVariance.Invariant or OriginVariance.Unused);
            if (!this.Infer(pattern.Components[i], actual.Components[i], function, arguments, inferOrigins, lengths, commonOrigins, structural, evidence, relateLater, below))
            {
                return false;
            }
        }

        return true;
    }
}
