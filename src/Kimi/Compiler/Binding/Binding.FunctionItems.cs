// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;
using Kimi.Diagnostics;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<BoundType, BoundCall> functionItemContexts = new(ReferenceEqualityComparer.Instance);
    private Dictionary<Koto, ReferenceConstraintFailure>? referenceConstraints;

    private readonly record struct ReferenceConstraintFailure(IsKoto Clause, BoundConstraint Constraint, ConstraintProof Proof, BindingSymbol? Declaration = null);

    // Generation-only context of an already selected declaration. An override never becomes a public Item.
    internal BoundCall? ImplementationContext(FunctionKoto function, BoundType declaring)
        => this.FunctionItemContext(this.FunctionItemType(function.BoundSymbol!, [], declaring));

    // A declaration and its bound generic arguments are the complete identity of its zero-sized Item. Its signature is a
    // call contract, not stored environment data; in particular, per-call input Origins do not make the Item borrow anything.
    internal BoundType? FunctionItemSignature(BoundType type)
    {
        if (type.Kind != BoundTypeKind.FunctionItem || type.Symbol is not { Type: { } result, Declaration: FunctionKoto function })
        {
            return null;
        }

        var parameters = this.RentTypes(function.Parameters.Count);
        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                if (function.Parameters[i].Type.BoundType is not { } parameter || this.ItemType(parameter, type, function) is not { } bound)
                {
                    return null;
                }

                parameters[i] = bound;
            }

            if (this.ItemType(result, type, function) is not { } boundResult)
            {
                return null;
            }

            var inputs = function.Parameters.Count == 0 ? BoundType.Unit : this.InternType(BoundTypeKind.Tuple, null, SemanticsKind.Owner, parameters.AsSpan(0, function.Parameters.Count));
            return this.InternType(BoundTypeKind.Function, null, SemanticsKind.Owner, [inputs, boundResult], resultMode: ResultModeOf(function.ReturnType));
        }
        finally
        {
            this.typeScratch.Return(parameters, clearArray: true);
        }
    }

    // The call context of an Item: generation requires closed arguments, while public effect checks also inspect
    // symbolic contracts. One context per Item Type lets repeated preparation reuse both operands and substitutions.
    internal BoundCall? FunctionItemContext(BoundType type, bool requireClosed = true)
    {
        if (type.Kind != BoundTypeKind.FunctionItem || (requireClosed && type.ContainsParameter) ||
            type.Symbol is not { Type: { } result, Declaration: FunctionKoto function })
        {
            return null;
        }

        if (this.functionItemContexts.TryGetValue(type, out var context))
        {
            return context;
        }

        if (this.ItemType(result, type, function) is not { } boundResult)
        {
            return null;
        }

        // The entry's result keeps the Item's per-call Origins, so no body binds that Type itself: its storage, such as the cases of an
        // Option or the fields of a struct over a borrow, is completed here, as an instantiated parameter's is. A Type whose storage
        // cannot be completed keeps no representation, and the entry reports that.
        this.PrepareInstantiatedStorage(boundResult, 0);
        if (function.IsRequirement)
        {
            var requirement = this.RequirementItemContext(type, function, boundResult);
            if (requirement is not null)
            {
                this.functionItemContexts.Add(type, requirement);
            }

            return requirement;
        }

        var created = new BoundCall();
        var own = this.typeScratch.Rent(function.GenericArguments.Count);
        try
        {
            CopyItemArguments(type, function, own);
            // Compiler entries and virtual Items consume already-acquired logical parameters. Their public
            // contract operands serve generation and effect checks without inventing a source invocation.
            var operations = type.Symbol.CompilerFunction == CompilerFunctionKind.None && !function.IsVirtual ? [] : new BoundArgumentOperation[function.Parameters.Count];
            for (var i = 0; i < operations.Length; i++)
            {
                var parameter = this.ItemType(function.Parameters[i].Type.BoundType!, type, function)!;
                operations[i] = new(function.Parameters[i].Type, parameter, parameter, ArgumentOperationKind.Value, ArgumentAdaptation.Exact, ParameterIndex: i);
            }

            created.Set(type.Symbol, boundResult, null, [], own.AsSpan(0, function.GenericArguments.Count), declaringType: ItemDeclaringType(type, function), operations: operations, lengthArguments: type.LengthArguments);
        }
        finally
        {
            this.typeScratch.Return(own, clearArray: true);
        }

        this.functionItemContexts.Add(type, created);
        return created;
    }

    private static bool IsWaitingFunctionReference(Koto node)
        => KotoHelper.UnwrapParentheses(node) is not FunctionKoto and { BoundType: null, BindingState: BindingState.Resolved, BoundSymbol.Kind: BindingSymbolKind.Function };

    private static bool IsWaitingCallable(Koto node)
        => KotoHelper.UnwrapParentheses(node) is FunctionKoto { IsAnonymous: true, BoundType: null } || IsWaitingFunctionReference(node);

    // A specialization supplies the selected Item's implementation, never an additional overload candidate.
    private static BindingSymbol? ReferenceCandidate(BindingSymbol? candidate)
    {
        while (candidate?.Declaration is FunctionKoto { IsSpecialization: true })
        {
            candidate = candidate.Next;
        }

        return candidate;
    }

    private static bool IndependentFunctionItem(BindingSymbol symbol, bool unbound, BoundType? declaringType)
        => symbol.Next is null && symbol.Declaration is FunctionKoto { GenericArguments.Count: 0, IsDestructor: false } function &&
            (function.TypeConstraints.Count == 0 || ContainerBound(symbol, declaringType)) &&
            (symbol.ReceiverIndex < 0 || unbound) && symbol.Scope.Owner.BoundSymbol?.Schema is not { Origins.Count: > 0 } &&
            (symbol.Scope.Owner.BoundSymbol?.Schema is not { GenericSlots.Count: > 0 } || ContainerBound(symbol, declaringType)) &&
            symbol.Intrinsic == IntrinsicKind.None && symbol.Scope.Owner is not ContractKoto;

    // SPEC 7.3, 10.5: a member of a generic container is referenced through a Type that binds the container's slots.
    private static bool ContainerBound(BindingSymbol symbol, BoundType? declaringType)
        => declaringType is not null && ReferenceEquals(declaringType.Symbol, symbol.Scope.Owner.BoundSymbol);

    // The declaring Type of a member of a generic container follows the bound function arguments as the last Component.
    private static BoundType? ItemDeclaringType(BoundType item, FunctionKoto function)
        => !function.IsRequirement && item.Components.Count > ItemTypeArgumentCount(function) ? item.Components[^1] : null;

    private static int ItemTypeArgumentCount(FunctionKoto function)
    {
        var count = 0;
        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            count += function.GenericArguments[i] is LengthParameterKoto ? 0 : 1;
        }

        return count;
    }

    private static void CopyItemArguments(BoundType item, FunctionKoto function, BoundType?[] arguments)
    {
        var component = 0;
        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            arguments[i] = function.GenericArguments[i] is LengthParameterKoto ? null : item.Components[component++];
        }
    }

    private static bool ReferenceSlotsComplete(FunctionKoto function, BoundType?[] arguments, BoundLength?[] lengths)
    {
        for (var i = 0; i < function.GenericArguments.Count; i++)
        {
            if (function.GenericArguments[i] is LengthParameterKoto ? lengths[i] is null : arguments[i] is null)
            {
                return false;
            }
        }

        return true;
    }

    // The referenced Name, inside parentheses and an explicit Type-argument list.
    private static Koto ReferenceName(Koto use)
        => KotoHelper.UnwrapParentheses(use) is GenericsKoto { Identifier: { } name } ? name : KotoHelper.UnwrapParentheses(use);

    // Components hold Type slots followed by a generic member's declaring Type; LengthArguments retain declaration-slot
    // positions. Substitution uses the same aligned argument lists as an ordinary call; per-call Origins stay in place.
    private BoundType? ItemType(BoundType type, BoundType item, FunctionKoto function)
        // Substitution can close an associated projection. Signature consumers and entry ABI
        // preparation must see the same normalized Type as an ordinary selected call.
        => this.SubstitutedItemType(type, item, function) is { } bound ? this.ContractType(bound, this.ConstraintScope(function)) : null;

    private BoundType? SubstitutedItemType(BoundType type, BoundType item, FunctionKoto function)
    {
        if (function.IsRequirement && item.Components is [var self, var reference])
        {
            return this.ContractType(this.SubstituteContractReference(type, this.BoundContractReference(reference)), this.ConstraintScope(function), self);
        }

        if (item.Components.Count == 0 && item.LengthArguments.Length == 0)
        {
            return type;
        }

        if (ItemDeclaringType(item, function) is { } declaring)
        {
            if (this.MemberType(type, declaring) is not { } member)
            {
                return null;
            }

            type = member;
        }

        if (item.LengthArguments.Length == 0)
        {
            return function.GenericArguments.Count == 0 ? type : this.SubstituteType(type, function, ((BoundType[])item.Components).AsSpan(0, function.GenericArguments.Count));
        }

        var arguments = this.typeScratch.Rent(function.GenericArguments.Count);
        try
        {
            CopyItemArguments(item, function, arguments);
            return this.SubstituteType(type, function, arguments.AsSpan(0, function.GenericArguments.Count), item.LengthArguments);
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
        }
    }

    // The Type through which a member reference names its container, when member lookup established it.
    private BoundType? ReferenceDeclaringType(Koto use)
        => ReferenceName(use) is MemberAccessKoto member && this.memberSelections.TryGetValue(member, out var selection) ? selection.DeclaringType : null;

    // The declaring Type that binds the slots of a generic container whose member is referenced.
    private BoundType? ReferenceContainer(Koto use, BindingSymbol candidate)
        => this.ReferenceDeclaringType(use) is { } declaring && candidate.Scope.Owner.BoundSymbol?.Schema is { GenericSlots.Count: > 0 } && ContainerBound(candidate, declaring) ? declaring : null;

    // A fixed Function or Callable signature can select a function reference after the outer candidate is determined.
    private bool TakesCallableContext(BindingSymbol? group)
    {
        for (var candidate = group; candidate is not null; candidate = candidate.Next)
        {
            if (candidate.Declaration is FunctionKoto function)
            {
                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (function.Parameters[i].Type.BoundType is { } type && this.TryCallable(type, this.ConstraintScope(function), out _, out _))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    // SPEC 7.3: a Type-qualified instance function reference is unbound; its self is an ordinary parameter.
    private bool UnboundMemberReference(Koto use)
        => ReferenceName(use) is MemberAccessKoto member && this.CallReceiver(member) is null;

    // SPEC 7.3: value.method without invocation forms no bound-method value.
    private bool BoundMethodReference(Koto use, BindingSymbol symbol)
        => symbol.ReceiverIndex >= 0 && ReferenceName(use) is MemberAccessKoto member && this.CallReceiver(member) is not null;

    private BoundType? BindFunctionItem(Koto use, BindingSymbol symbol, BindingScope scope)
    {
        if (this.ReferenceRequirements(use) is { } requirements)
        {
            return this.BindRequirementItem(use, requirements, scope);
        }

        if (this.BoundMethodReference(use, symbol))
        {
            return this.Fail(use, BindingFailure.BoundMethodValue);
        }

        if (KotoHelper.UnwrapParentheses(use) is GenericsKoto explicitReference)
        {
            return this.BindExplicitFunctionItem(use, explicitReference, symbol, scope);
        }

        var unbound = this.UnboundMemberReference(use);
        var declaring = this.ReferenceDeclaringType(use);
        if (!IndependentFunctionItem(symbol, unbound, declaring))
        {
            return this.FailUnfixedReference(use, symbol, unbound, declaring);
        }

        var container = this.ReferenceContainer(use, symbol);
        var proof = this.ReferenceArgumentProof((FunctionKoto)symbol.Declaration, [], scope, container, failureUse: use);
        if (proof != ConstraintProof.Proven)
        {
            this.RequireReferenceConstraint(use, proof);
            return null;
        }

        return this.CompleteFunctionItem(use, symbol, default, container);
    }

    // SPEC 10.5: without a fixed expected call signature, a reference is a value only when one candidate remains and that
    // candidate has no unbound own slots. Forms whose references are not yet selected stay explicitly unsupported.
    private BoundType? FailUnfixedReference(Koto use, BindingSymbol symbol, bool unbound, BoundType? declaring)
    {
        var count = 0;
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is FunctionKoto unsupported && UnsupportedReference(candidate, unsupported, unbound, declaring))
            {
                return this.Fail(use, BindingFailure.Unsupported); // No later context makes the form a value (SPEC 23.3.6.1).
            }

            if (candidate.Declaration is not FunctionKoto function || function.IsDestructor ||
                candidate.Intrinsic != IntrinsicKind.None || (function.GenericArguments.Count == 0 && function.TypeConstraints.Count != 0 && !ContainerBound(candidate, declaring)))
            {
                return this.Fail(use, BindingFailure.Unsupported, true);
            }

            count++;
        }

        if (count == 1)
        {
            return this.Fail(use, BindingFailure.UnboundTypeArgument);
        }

        var rejected = new RejectedCandidate[count];
        var next = 0;
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            var item = this.InternType(BoundTypeKind.FunctionItem, candidate, SemanticsKind.Owner, []);
            rejected[next++] = new((FunctionKoto)candidate.Declaration, this.FunctionItemSignature(item), null, UnfixedReference: true);
        }

        (this.rejectedCandidates ??= new(ReferenceEqualityComparer.Instance))[use] = rejected;
        return this.Fail(use, BindingFailure.Ambiguous);
    }

    // SPEC 10.5: a reference whose explicit Type arguments select exactly one candidate does not need the call's signature; it
    // is an independently typable argument whose Item signature is evidence (SPEC 10.8). Nothing is reported here.
    private bool ExplicitReferenceValue(Koto argument, BindingSymbol symbol, BindingScope scope)
    {
        if (KotoHelper.UnwrapParentheses(argument) is not GenericsKoto explicitReference)
        {
            return false;
        }

        var count = explicitReference.TypeArguments.Count;
        var candidates = 0;
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is FunctionKoto function && function.GenericArguments.Count == count && this.ReferenceArgumentKinds(explicitReference, function, scope) && this.Accessible(candidate, scope))
            {
                candidates++;
            }
        }

        return candidates == 1;
    }

    // SPEC 10.5: without a fixed expected call signature, a reference with explicit Type arguments is a value when exactly one
    // candidate takes those arguments; its Constraints are proven for them.
    private BoundType? BindExplicitFunctionItem(Koto use, GenericsKoto explicitReference, BindingSymbol symbol, BindingScope scope)
    {
        var unbound = this.UnboundMemberReference(use);
        var declaring = this.ReferenceDeclaringType(use);
        var count = explicitReference.TypeArguments.Count;
        BindingSymbol? selected = null;
        var candidates = 0;
        for (var candidate = ReferenceCandidate(symbol); candidate is not null; candidate = ReferenceCandidate(candidate.Next))
        {
            this.BindHeader(candidate);
            if (candidate.Declaration is not FunctionKoto function || function.GenericArguments.Count != count || !this.ReferenceArgumentKinds(explicitReference, function, scope) || !this.Accessible(candidate, scope))
            {
                continue;
            }

            if (UnsupportedReference(candidate, function, unbound, declaring))
            {
                return this.Fail(use, BindingFailure.Unsupported);
            }

            selected = candidate;
            candidates++;
        }

        if (candidates != 1)
        {
            return this.Fail(use, candidates == 0 ? BindingFailure.NoApplicableCandidate : BindingFailure.Ambiguous);
        }

        var target = (FunctionKoto)selected!.Declaration;
        if ((target.Modifier & ModifierKind.Unsafe) != 0)
        {
            return this.Fail(use, BindingFailure.UnsafeFunctionValue);
        }

        var arguments = this.typeScratch.Rent(count);
        var lengths = this.lengthScratch.Rent(count);
        try
        {
            if (!this.ExplicitReferenceArguments(explicitReference, target, scope, arguments, lengths))
            {
                return this.Fail(use, BindingFailure.NoApplicableCandidate, true);
            }

            var container = this.ReferenceContainer(use, selected);
            var proof = this.ReferenceArgumentProof(target, arguments, scope, container, lengths, use);
            if (proof != ConstraintProof.Proven)
            {
                this.RequireReferenceConstraint(use, proof);
                return null;
            }

            return this.CompleteFunctionItem(use, selected, arguments.AsSpan(0, count), container, lengths.AsSpan(0, count));
        }
        finally
        {
            this.typeScratch.Return(arguments, clearArray: true);
            this.lengthScratch.Return(lengths, clearArray: true);
        }
    }

    // The same declaration slots as a direct call: each slot holds a Type or a checked length, never a dummy Type.
    private bool ReferenceArgumentKinds(GenericsKoto reference, FunctionKoto function, BindingScope scope)
    {
        for (var i = 0; i < reference.TypeArguments.Count; i++)
        {
            if ((function.GenericArguments[i] is LengthParameterKoto) != this.IsLengthArgument(reference.TypeArguments[i], scope))
            {
                return false;
            }
        }

        return true;
    }

    private bool ExplicitReferenceArguments(GenericsKoto explicitReference, FunctionKoto function, BindingScope scope, BoundType?[] arguments, BoundLength?[] lengths)
    {
        for (var i = 0; i < explicitReference.TypeArguments.Count; i++)
        {
            var syntax = explicitReference.TypeArguments[i];
            var length = function.GenericArguments[i] is LengthParameterKoto;
            arguments[i] = length ? null : syntax.BoundType;
            lengths[i] = length ? this.BindLength(syntax, scope) : null;
            if (length ? lengths[i] is null : arguments[i] is null)
            {
                return false;
            }
        }

        return true;
    }

    private BoundType CompleteFunctionItem(Koto use, BindingSymbol symbol, ReadOnlySpan<BoundType?> typeArguments = default, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengthArguments = default)
        => this.CompleteFunctionItem(use, this.FunctionItemType(symbol, typeArguments, declaringType, lengthArguments));

    private BoundType CompleteFunctionItem(Koto use, BoundType type)
    {
        var symbol = type.Symbol!;
        while (use is ParenthesizedKoto parentheses)
        {
            use.BoundSymbol = symbol;
            Complete(use, type);
            use = parentheses.Operand;
        }

        use.BoundSymbol = symbol;
        var name = use is GenericsKoto { Identifier: { } identifier } ? identifier : use;
        if (!ReferenceEquals(name, use))
        {
            name.BoundSymbol = symbol;
            Complete(name, type);
        }

        if (name is MemberAccessKoto member)
        {
            member.Right.BoundSymbol = symbol;
            Complete(member.Right, type);
        }

        Complete(use, type);
        return type;
    }

    private BoundType FunctionItemType(BindingSymbol symbol, ReadOnlySpan<BoundType?> typeArguments, BoundType? declaringType = null, ReadOnlySpan<BoundLength?> lengthArguments = default)
    {
        if (typeArguments.IsEmpty && declaringType is null && lengthArguments.IsEmpty)
        {
            return this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, []);
        }

        var components = this.RentTypes(typeArguments.Length + (declaringType is null ? 0 : 1));
        var count = 0;
        var hasLength = false;
        try
        {
            for (var i = 0; i < typeArguments.Length; i++)
            {
                if (typeArguments[i] is { } argument)
                {
                    components[count++] = argument;
                }

                hasLength |= i < lengthArguments.Length && lengthArguments[i] is not null;
            }

            if (declaringType is not null)
            {
                components[count++] = declaringType;
            }

            return this.InternType(BoundTypeKind.FunctionItem, symbol, SemanticsKind.Owner, components.AsSpan(0, count), lengthArguments: hasLength ? lengthArguments : default);
        }
        finally
        {
            this.typeScratch.Return(components, clearArray: true);
        }
    }

    // SPEC 12.4.4.1: a Function Item clause that only an OCC-X witness leaves unproven is that located limit, as at a call.
    private void RequireReferenceConstraint(Koto use, ConstraintProof proof)
    {
        if (proof == ConstraintProof.Unknown && this.capabilityMode == BindingMode.Final && this.referenceConstraints?.TryGetValue(use, out var fact) == true)
        {
            this.FailUnprovenReference(use, fact, false);
            return;
        }

        this.RequireConstraint(use, proof, this.capabilityMode);
    }

    private void RecordReferenceConstraint(Koto use, IsKoto clause, BoundConstraint constraint, ConstraintProof proof)
    {
        if (proof is ConstraintProof.Refuted or ConstraintProof.Unknown)
        {
            var failures = this.referenceConstraints ??= new(ReferenceEqualityComparer.Instance);
            // A refuted clause, then an Unknown one that does not wait only for OCC-X, is the reported cause (SPEC 12.4.4.1).
            if (!failures.TryGetValue(use, out var previous) || (previous.Proof == ConstraintProof.Unknown && proof == ConstraintProof.Refuted) ||
                (previous.Proof == ConstraintProof.Unknown && proof == ConstraintProof.Unknown && this.PendingExclusiveConformance(previous.Constraint, this.ConstraintScope(use)) is not null &&
                    this.PendingExclusiveConformance(constraint, this.ConstraintScope(use)) is null))
            {
                failures[use] = new(clause, constraint, proof);
            }
        }
    }

    private void ReportReferenceConstraint(Koto use, ReferenceConstraintFailure fact, DiagnosticRequirement requirement, DiagnosticCode code)
    {
        var call = fact.Declaration is not null;
        var member = fact.Declaration?.Name ?? use.BoundSymbol?.Name ?? "the referenced function";
        var subject = ClauseSubject(fact.Constraint) is { } type ? DiagnosticTypeName(type) : null;
        var clause = fact.Clause.ToString();
        var outcome = fact.Proof == ConstraintProof.Refuted ? "is refuted" : "cannot be proven";
        use.Report(
            requirement,
            code,
            evidence: subject is null ? null : [subject, member, clause],
            note: $"{(call ? "Call" : "Function Item")} {member} requires {clause}; this condition {outcome}" + (subject is null ? " under the supplied bindings" : $" for {subject}"),
            related: [("constraint", fact.Clause, call ? "required by the called declaration" : "required by the referenced declaration")]);
    }
}
