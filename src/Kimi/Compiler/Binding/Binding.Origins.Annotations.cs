// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// The Origin of a layer in a Type annotation (SPEC 15.3.1, 15.4.2, 15.4.4): its Type position (TypeContext), a written Origin
// (BindOrigin, BindOriginName, through the candidate a name resolves to: OriginCandidate) or an omitted one (OmittedOrigin), and a
// binder's inputs, whose atoms the omitted Origins of its signature are. A result slot's default (SPEC 15.4.3 rule 3) whose inputs'
// Owned read a region with no answer while a body is bound is judged at Final (R7-O2 O2-F: ProveDefaultOwned, the slot's
// DeferredDefaults row and its obligation, JudgeDeferredDefault). Until then the slot's provisional Origin is its own pending atom (the
// atom PendingOrigin forms), which every reader names; Final proves it, and the atom reads as its completion at the intakes every Origin
// passes (CompletedOrigin: DeclaredAtUse and SuppliedAtUse, ReadValueIn, the C1-F4 view's InternAtom, the Hover printer), or
// refutes it, and the atom stays Unbound resting on the failed slot (UnboundCause's path b). Before Final, DeferredIn and DeferredAtomIn
// are the one reading of "names an uncompleted deferred atom" (a comparison's row, LeavesBodyRows; an erasure's deferral).
// Must not: publish `static` or any other Origin for a deferred slot before Final, or complete a refuted atom.
public sealed partial class Binding
{
    private readonly DeferredDefaultRows deferredDefaults;

    // The deferred defaults Final proved this pass (CompletedOrigin's gate: no reading changes while it is 0).
    private int completedDefaults;

    // SPEC 15.4.4 (R7-O2 O2-F): `origin` with each deferred slot's atom that Final proved read as the default it completes to, a meet
    // operand by operand; an atom not proven, and any other Origin, as it is. The one completion reading: every reader of an Origin after
    // the slot's resolution passes through it (DeclaredAtUse, SuppliedAtUse, ReadValueIn, InternAtom, HoverTypeName, SemanticQuery.OriginOf).
    internal BoundOrigin CompletedOrigin(BoundOrigin origin)
    {
        if (this.completedDefaults == 0)
        {
            return origin;
        }

        if (origin.Kind == OriginKind.Unbound)
        {
            return this.DeferredAtom(origin) is { Proven: true } row ? row.Completion : origin;
        }

        if (origin.Kind != OriginKind.Intersection)
        {
            return origin;
        }

        var result = BoundOrigin.Static;
        var changed = false;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            var operand = this.CompletedOrigin(origin.Operands[i]);
            changed |= !ReferenceEquals(operand, origin.Operands[i]);
            result = this.Meet(result, operand);
        }

        return changed ? result : origin;
    }

    private static int InputCount(Koto owner) => owner is FunctionKoto f ? f.Parameters.Count : owner is PropertyAccessorKoto ? 2 : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes.Count : 1 : 0;

    private static int InputOriginCount(Koto owner) => InputCount(owner) + (owner.BoundSymbol?.AggregateInputOrigins?.Count ?? 0);

    private static Koto? InputType(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].Type : owner is PropertyAccessorKoto a ? index == 0 ? a.ReceiverType : a.ValueType : owner is FunctionTypeKoto t ? t.Parameters is TupleTypeKoto tuple ? tuple.ElementNodes[index] : t.Parameters : null;

    private static string InputName(Koto owner, int index) => owner is FunctionKoto f ? f.Parameters[index].InternalName : index == 0 ? "self" : "value";

    // SPEC 15.3.4: the slot a projection's name selects in the Type of its value past that Type's borrow layers (`type` becomes that
    // Type); -1 when the Type declares none.
    private static int ProjectedSlot(ref BoundType? type, string name)
    {
        while (type is { Kind: BoundTypeKind.Semantics } && IsBorrow(type.Semantics))
        {
            type = type.Components[0];
        }

        for (var i = 0; type?.Symbol?.Schema is { } schema && i < schema.Origins.Count; i++)
        {
            if (schema.Origins[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    // SPEC 15.3.1: the Origin a written name selects in its candidate's Type: a projection's (`use` a member access) declared slot past
    // the Type's borrow layers (`type` becomes that Type; `slot` -1 when it declares none), a name's borrow layer's own Origin.
    private static BoundOrigin? SelectedOrigin(Koto use, ref BoundType? type, out int slot)
    {
        if (use is not MemberAccessKoto { Right: IdentifierNameKoto target })
        {
            slot = -1;
            return type is { Origin: { } origin } && IsBorrow(type.Semantics) ? origin : null;
        }

        slot = ProjectedSlot(ref type, target.IdentifierName);
        return slot < 0 ? null : type!.Kind == BoundTypeKind.Slice ? type.Origin : slot < type.OriginArguments.Count ? type.OriginArguments[slot] : null;
    }

    // SPEC 8.13.1: whether a node is one of a `specialize func` header's Type arguments.
    private static bool IsSpecializationArgument(FunctionKoto function, Koto node)
    {
        for (var i = 0; function.IsSpecialization && i < function.GenericArguments.Count; i++)
        {
            if (ReferenceEquals(function.GenericArguments[i], node))
            {
                return true;
            }
        }

        return false;
    }

    // Whether a node lies in a `specialize func` header's Type argument (the nearest enclosing function's).
    private static bool InSpecializationArgument(Koto node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            if (parent is FunctionKoto function)
            {
                return IsSpecializationArgument(function, current);
            }
        }

        return false;
    }

    private BoundType? BoundInputType(Koto owner, int index)
        => ReferenceEquals(owner, this.closureHeader) && InputType(owner, index) is SyntaxFormKoto { Akind: KotoKind.InferredType }
            ? this.closureHeaderInputs!.Components[index]
            : owner is PropertyAccessorKoto accessor ? index == 0 ? Accessor(accessor).Receiver : Accessor(accessor).Input : InputType(owner, index)?.BoundType;

    private TypeBindingContext TypeContext(Koto syntax, BindingScope scope)
    {
        for (var current = syntax; current.Parent is { } parent; current = parent)
        {
            if (parent is PropertyAccessorKoto accessor)
            {
                if (ReferenceEquals(accessor.ReturnType, current))
                {
                    return new(TypePosition.Result, accessor);
                }

                return ReferenceEquals(accessor.ReceiverType, current) || ReferenceEquals(accessor.ValueType, current)
                    ? new(TypePosition.Parameter, accessor, ReferenceEquals(accessor.ReceiverType, current) ? 0 : 1, true)
                    : new(TypePosition.Explicit, accessor);
            }

            if (parent is FunctionKoto function)
            {
                if (ReferenceEquals(function.ReturnType, current))
                {
                    return new(TypePosition.Result, function);
                }

                for (var i = 0; i < function.Parameters.Count; i++)
                {
                    if (ReferenceEquals(function.Parameters[i].Type, current))
                    {
                        return new(TypePosition.Parameter, function, i, true);
                    }
                }

                // SPEC 8.13.2, 15.4.2 ("Accessor or specialization: the inherited contract first"): a specialization's Type argument
                // is the original's T, whose Origins each call supplies: an omitted aggregate slot is the header's universal input
                // Origin, which the restated contract then inherits (CompleteImplementationOrigins); none is written
                // (PrepareSpecializations' Unwritten). It is no parameter: InputIndex stays -1.
                return IsSpecializationArgument(function, current) ? new(TypePosition.Parameter, function, -1) : new(TypePosition.Explicit, function);
            }

            if (parent is VariableKoto variable)
            {
                if (variable is PropertyKoto { DeclarationKind: PropertyDeclarationKind.Computed or PropertyDeclarationKind.Requirement } property && property.GetAccessor(PropertyAccessorKind.Get) is { } getter)
                {
                    return new(TypePosition.Result, getter);
                }

                return new(variable is PropertyKoto ? scope.Owner is GroupKoto ? TypePosition.StaticStorage : TypePosition.InstanceStorage : TypePosition.Local, variable);
            }
        }

        return new(TypePosition.Explicit, syntax);
    }

    private BoundOrigin? BindOriginName(string name, Koto use, BindingScope scope)
    {
        if (name == "static")
        {
            return BoundOrigin.Static;
        }

        if (this.OriginCandidate(name, use, scope, out var scalar, out var carrier, out var repeated))
        {
            if (repeated is not null)
            {
                this.RestOnRepeatedSet(use, repeated);
                return null;
            }

            if (scalar is not null)
            {
                return scalar;
            }

            if (SelectedOrigin(use, ref carrier, out _) is { } origin)
            {
                return origin;
            }

            if (use.BoundSymbol is { Slot: ReceiverSlot } receiver)
            {
                return this.ReceiverOrigin(receiver); // SPEC 5.7, 15.2.1: in a constructor or drop, `self` is the receiver's Origin.
            }

            this.Record(use, BindingFailure.InvalidOrigin, []);
            return null;
        }

        this.Record(use, BindingFailure.MissingOrigin, [], unresolved: true);
        return null;
    }

    // SPEC 15.3.1, 23.1.2: a written Origin expression. A parenthesis or meet whose operand did not resolve has no Origin for that
    // reason alone and rests on the operand (CompleteDependent), which reports the cause; only a failure of its own is its record. It binds
    // as the slot it names (SlotAtUse): a `let` region's value is read at each use (PLAN G492-a (ii); Binding.LocalRegions.cs's contract).
    private BoundOrigin? BindOrigin(Koto syntax, BindingScope scope)
    {
        if (IsRecovery(syntax, out _))
        {
            this.Record(syntax, BindingFailure.InvalidOrigin, []);
            return null;
        }

        BoundOrigin? result = null;
        Koto? failedOperand = null;
        if (syntax is IdentifierNameKoto name)
        {
            result = this.BindOriginName(name.IdentifierName, syntax, scope);
        }
        else if (syntax is ParenthesizedKoto parentheses)
        {
            result = this.BindOrigin(parentheses.Operand, scope);
            failedOperand = FailedOperand(parentheses.Operand);
        }
        else if (syntax is BinaryKoto binary && binary.Akind == KotoKind.And)
        {
            var left = this.BindOrigin(binary.Left, scope);
            var right = this.BindOrigin(binary.Right, scope);
            failedOperand = FailedOperand(binary.Left) ?? FailedOperand(binary.Right);
            if (left is not null && right is not null)
            {
                result = this.Meet(left, right);
            }
        }
        else if (syntax is MemberAccessKoto member && member.Left is IdentifierNameKoto input && member.Right is IdentifierNameKoto target)
        {
            if (this.OriginCandidate(input.IdentifierName, syntax, scope, out _, out var type, out var repeated))
            {
                result = SelectedOrigin(syntax, ref type, out var slot);
                var schema = type?.Symbol?.Schema;
                var declared = slot >= 0;
                if (repeated is not null && (declared || schema is null))
                {
                    // The slot of a repeated set is unknown; a slot name its Type does not declare is still its own problem.
                    this.RestOnRepeatedSet(syntax, repeated);
                    result = null;
                }
                else if (result is not null)
                {
                    input.BindingState = target.BindingState = BindingState.Resolved;
                    target.BoundOrigin = result;
                }
                else if (type?.Symbol is { Schema: not null } symbol)
                {
                    // SPEC 15.3.2: a slot the Type does not declare, with the Type's declaration related (role 1, "header" or "type").
                    this.Record(syntax, BindingFailure.InvalidOrigin, [new(ProblemFactKind.Related, this.FactSite(symbol.Declaration!), 1)]);
                }
            }
        }

        if (result is not null)
        {
            result = this.SlotAtUse(result, syntax);
            syntax.BindingState = BindingState.Resolved;
            syntax.BoundOrigin = result;
        }

        if (result is null || !OriginVisible(result, syntax))
        {
            // Transitional, R7-E5 (E4c, B1): a null result reads BindOriginName's or the absent slot's record at this node, or the
            // operand's (OriginCandidate's InvalidOrigin, its MissingOrigin, an absent slot's), so it keeps `Fail`, which adds no record
            // there, until R7-E5's per-problem inputs; only an invisible Origin is its own failure.
            _ = failedOperand is null ? this.Fail(syntax, BindingFailure.InvalidOrigin) : this.CompleteDependent(syntax, failedOperand);
        }

        return result;

        Koto? FailedOperand(Koto operand) => this.IsUnresolvedPrerequisite(operand) ? operand : null;
    }

    // A name that resolves to a binding set that failed as a repeat yields that set as repeated (RestOnRepeatedSet) beside its Type.
    // SPEC 15.3.4, 15.4.4 (PLAN G230-H, rule D): a name of a local, its value or its set, outside that local's own declaration reads the
    // local's declared Type, so it demands the declaration as a value use does (QueryValueType); the local forms in its declaring body
    // (FormingBodyOf), so a header bound before that body (R4-M101 E3) selects the Type and the regions the body forms. A local's own
    // declaration (its Type and clauses) reads its declared Type as it forms and is never demanded from inside it (SPEC 9.3). Asked once,
    // where the name's candidate is found (G230H-6, G230H-8, G230H-11 C-2): a name that a not-ready literal itself declares (its
    // parameter, a body local, set or Origin, a written entry not yet formed), for a header bound ahead of that literal's environment
    // (AheadOfEnvironment), rests. The name selects nothing, so the header fails there and is bound again at the function's statement
    // (BindFunction); a clause keeps that failure (Q-B's class). A formed written entry selects its environment binding, whose Type its
    // entry fixes (SPEC 7.7.2, C-1), and a name of a named function's scope is selected normally (SPEC 9.3, 15.3.4).
    private bool OriginCandidate(string name, Koto use, BindingScope scope, out BoundOrigin? scalar, out BoundType? carrier, out TypeSemanticsKoto? repeated)
    {
        scalar = null;
        carrier = null;
        repeated = null;
        var owner = OriginOwner(use);
        if (owner is not null && !ReferenceEquals(owner, scope.Owner) &&
            this.originDeclarations.GetValueOrDefault(owner)?.Set(name) is { } localSet)
        {
            carrier = this.BindOriginSetType(localSet, scope);
            repeated = RepeatedSet(localSet) ? localSet : null;
            return true;
        }

        for (var current = scope; current is not null; current = current.Parent)
        {
            var visibleValue = current.Values.TryGetValue(name, out var competing) &&
                competing.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture && Visible(competing);
            var set = this.originDeclarations.GetValueOrDefault(current.Owner)?.Set(name);

            // SPEC 15.3.4's Local set row: a local set is visible in its Type, its clauses and the following scope.
            var visibleSet = current.OriginSets?.TryGetValue(name, out var local) == true && OriginOwner(local) is { } localOwner &&
                (ReferenceEquals(localOwner, owner) || localOwner.Span.End <= use.Span.Start) ? local : null;
            var entry = current.Owner is FunctionKoto { IsAnonymous: true } literal && CaptureEntryOf(literal, name) is not null;
            if ((entry || visibleValue || set is not null || visibleSet is not null || current.Origins?.ContainsKey(name) == true) &&
                this.AheadOfEnvironment(owner, current) && !(entry && current.Values.GetValueOrDefault(name) is { Kind: BindingSymbolKind.Capture }))
            {
                return true; // G230H-6: the demand rests; nothing the environment forms is read before it is ready (a formed entry is, C-1).
            }

            if (visibleValue && (current.Origins?.ContainsKey(name) == true || set is not null))
            {
                this.Fail(use, BindingFailure.Duplicate);
                return true;
            }

            if (set is not null)
            {
                carrier = this.BindOriginSetType(set, current);
                repeated = RepeatedSet(set) ? set : null;
                return true;
            }

            if (visibleSet is not null)
            {
                if (OriginOwner(visibleSet) is { } setOwner && !ReferenceEquals(setOwner, owner) && setOwner.BoundSymbol is { } setLocal)
                {
                    this.QueryValueType(setLocal);
                }

                carrier = this.BindOriginSetType(visibleSet, current);
                repeated = RepeatedSet(visibleSet) ? visibleSet : null;
                return true;
            }

            if (current.Origins?.TryGetValue(name, out scalar) == true)
            {
                return true;
            }

            // SPEC 7.2.3 with 15.3.4 (the owner's D-1236 (A)): a parameter the position may not name, a later one in its function's
            // default, is no candidate, as for a value (ParameterVisible).
            if (competing is not null && !ParameterVisible(competing, use))
            {
                continue;
            }

            var count = InputCount(current.Owner);
            for (var i = 0; i < count; i++)
            {
                if (InputName(current.Owner, i) == name)
                {
                    var nameNode = use is MemberAccessKoto member ? member.Left : use;
                    if (current.Values.TryGetValue(name, out var parameter))
                    {
                        nameNode.BoundSymbol = parameter;
                    }

                    carrier = this.BoundInputType(current.Owner, i) ?? (InputType(current.Owner, i) is { } syntax ? this.BindType(syntax, current) : null);
                    return true;
                }
            }

            if (current.Values.TryGetValue(name, out var symbol) &&
                symbol.Kind is BindingSymbolKind.Local or BindingSymbolKind.Parameter or BindingSymbolKind.Capture)
            {
                // Read again: the environment may have formed it since visibleValue (AheadOfEnvironment's demand, C-1).
                if (!Visible(symbol))
                {
                    continue;
                }

                (use is MemberAccessKoto member ? member.Left : use).BoundSymbol = symbol;
                if (!ReferenceEquals(symbol.Declaration, owner))
                {
                    this.QueryValueType(symbol);
                }

                carrier = symbol.Type ?? (symbol.Declaration is VariableKoto { TypeKoto: { } type } ? this.BindType(type, current) : null);
                return true; // An unsuitable nearest candidate never falls through.
            }
        }

        return false;

        // The lookup's visibility (VisibleAt; SPEC 15.8.2: the literal's own header never sees its environment bindings), and SPEC 15.4.4's
        // Origin-name rule: a local's own attached clauses and annotation name its declared Type's slots ("in them, `view.source` names the
        // declared Type's slot").
        bool Visible(BindingSymbol candidate)
            => (candidate.Kind == BindingSymbolKind.Local && ReferenceEquals(candidate.Declaration, owner)) || this.VisibleAt(candidate, use);
    }

    private BoundOrigin? OmittedOrigin(Koto use, BindingScope scope, TypeBindingContext context, LoanRequirement requirement = LoanRequirement.Ref, int aggregateSlot = -1, BindingSymbol? borrowCondition = null)
    {
        if (this.inheritedOriginTypes.TryGetValue(use, out var inherited))
        {
            // A Slice's aggregate slot 0 is its Origin (CompleteOrigins).
            var origin = aggregateSlot < 0 || inherited.Kind == BoundTypeKind.Slice ? inherited.Origin
                : aggregateSlot < inherited.OriginArguments.Count ? inherited.OriginArguments[aggregateSlot] : null;
            if (origin is not null)
            {
                return origin;
            }
        }

        if (this.PendingOrigin(use, scope, context, requirement, aggregateSlot, borrowCondition) is { } pending)
        {
            return pending;
        }

        if (context.Position == TypePosition.StaticStorage && requirement != LoanRequirement.Uniq)
        {
            // SPEC 15.4.2: after explicit relations, shared layers and none/ref aggregate slots default to static.
            // Owned and the static-source rules remain separate obligations; this never grants an exclusive loan.
            return BoundOrigin.Static;
        }

        if (aggregateSlot >= 0 && context.Position == TypePosition.Parameter &&
            context.Owner is FunctionKoto { IsAnonymous: false } or PropertyAccessorKoto)
        {
            var slots = context.Owner.BoundSymbol!.AggregateInputOrigins ??= new();
            foreach (var slot in slots)
            {
                if (ReferenceEquals(slot.Occurrence, use) && slot.TargetSlot == aggregateSlot)
                {
                    return slot;
                }
            }

            var origin = new BoundOrigin(OriginKind.Input, context.Owner, InputCount(context.Owner) + slots.Count)
            {
                InputIndex = context.Slot,
                Occurrence = use,
                TargetSlot = aggregateSlot,
            };
            slots.Add(origin);
            return origin;
        }

        if (context is { Position: TypePosition.Parameter, Direct: false, Owner: FunctionKoto { IsSpecialization: true } })
        {
            // SPEC 8.13.2, 15.4.2 (PLAN G863): an omitted borrow layer in a specialization's Type argument, or nested in a restated
            // input (where T's binding or the original's written layer puts one, which this probe cannot read before the target), is
            // the inherited contract's, which the input row above cannot give (an input's nested layer is an error): a located limit
            // until a Type argument position of its own completes it. A restated input whose target is found inherits the layer
            // instead (CompleteImplementationOrigins rebinds it).
            this.Fail(use, BindingFailure.Unsupported);
            return null;
        }

        if (context.Position == TypePosition.Parameter && context.Direct)
        {
            var origin = this.OriginAtom(context.Owner, OriginKind.Input, context.Slot);
            origin.BorrowCondition = null;
            return origin;
        }

        if (context.Position == TypePosition.Parameter && (context.Owner is FunctionKoto { IsAnonymous: true } ||
            (context.Owner is FunctionTypeKoto && OriginOwner(context.Owner) is VariableKoto { InitializerKoto: not null, TypeKoto: { } annotation } && IsWithin(context.Owner, annotation))))
        {
            // SPEC 15.3.4: nested anonymous/local Function input slots are body regions fixed once, never per-call inputs.
            return this.OpenOrigin(use, aggregateSlot, localInput: true);
        }

        if (context.Position == TypePosition.Local && context.Owner is VariableKoto { InitializerKoto: { } initializer } local)
        {
            var origin = this.OriginAtom(use, OriginKind.Inference, aggregateSlot);
            this.AddObligation(new(BindingObligationKind.OriginInference, use, BindingDeadline.BodyOrigins, Longer: origin));

            // The declaration that resolves the Origin (DECISIONS R4-M112): the initializer's when the initializer is bound
            // (ResolveInitializerOrigins), the local's when its Type is fixed (InferLocalOrigins).
            _ = IsWithin(use, initializer) ? this.InitializerDeclaration(local) : this.OriginDeclarationFor(local);
            return origin;
        }

        if (context.Position == TypePosition.Result)
        {
            if (this.deferredDefaults[use] is not null)
            {
                this.deferredDefaults[use] = null; // This resolution is the slot's last so far (ProveDefaultOwned).
            }

            BoundOrigin? meet = null;
            var guaranteedBorrow = false;
            var inputCount = InputCount(context.Owner);
            for (var i = 0; i < inputCount; i++)
            {
                var type = this.BoundInputType(context.Owner, i);
                if (type?.Origin is not { } input || (!IsBorrow(type.Semantics) && type.Kind != BoundTypeKind.SemanticsApplication))
                {
                    continue;
                }

                meet = meet is null ? input : this.Meet(meet, input);
                guaranteedBorrow |= IsBorrow(type.Semantics) || (borrowCondition is not null && ReferenceEquals(type.Symbol, borrowCondition)) ||
                    (type.Symbol?.WholeType is { } whole && this.HasSemanticsRole(whole, SemanticsMask.ValueBorrow | SemanticsMask.ObjRef | SemanticsMask.ObjUniq, scope));
            }

            if (meet is not null)
            {
                if (!guaranteedBorrow && requirement == LoanRequirement.Uniq)
                {
                    this.Record(use, BindingFailure.MissingOrigin, []);
                    return null;
                }

                return !guaranteedBorrow && aggregateSlot >= 0 && OwnedWithoutConditionalBorrows(meet) is not ConstraintProof.Proven and var unowned
                    ? this.MissingOrigin(use, unowned) : this.deferredDefaults[use]?.Atom ?? meet;
            }

            if (InSpecializationArgument(context.Owner))
            {
                // SPEC 8.13.2, 15.4.2 (PLAN G863): a Function Type result in a specialization's Type argument that no input of its own
                // gives is the call's T-Origin, not a default (the key excludes Origins): the borrow layer's located limit above. Removed
                // with it when the Type argument has a TypePosition of its own (PLAN G863's remainder), which TypeContext decides once
                // instead of this syntax walk (InSpecializationArgument).
                this.Fail(use, BindingFailure.Unsupported);
                return null;
            }

            if (requirement != LoanRequirement.Uniq)
            {
                if (aggregateSlot >= 0)
                {
                    this.failedCause = null;
                    for (var i = 0; i < inputCount; i++)
                    {
                        if (this.BoundInputType(context.Owner, i) is { } input && this.ProveDefaultOwned(input, use, aggregateSlot, BoundOrigin.Static) is not ConstraintProof.Proven and var proof)
                        {
                            return this.MissingOrigin(use, proof);
                        }
                    }
                }

                return this.deferredDefaults[use]?.Atom ?? BoundOrigin.Static;
            }
        }

        if (context.CallQualifier)
        {
            // SPEC 15.4.4: the call infers the slot; Member reports the qualifier when Binding cannot (RejectedOriginQualifier).
            return this.OriginAtom(use, OriginKind.Inference, aggregateSlot);
        }

        if (aggregateSlot >= 0 && context.Position == TypePosition.Explicit && this.ConstraintSet(use) && use is TypeSemanticsKoto { BindingSetName: { } set })
        {
            // SPEC 15.3.4 (DECISIONS BU-100): a set named in a Constraint that no relation equates quantifies every choice of its
            // Origins, so its slot's omission is a fixed Origin no caller substitutes and nothing solves: the occurrence's own atom.
            return this.OriginAtom(use, OriginKind.ConstraintSet, aggregateSlot, set);
        }

        if (use is TypeSemanticsKoto repeated && RepeatedSet(repeated))
        {
            return null; // SPEC 15.3.1, 23.1.2: a set that failed as a repeat declares nothing; its slot's omission rests on that DuplicateBinding_Kd (RestOnRepeatedSet's rule).
        }

        this.Record(use, BindingFailure.MissingOrigin, []);
        return null;

        ConstraintProof OwnedWithoutConditionalBorrows(BoundOrigin completion)
        {
            this.failedCause = null;
            var count = InputCount(context.Owner);
            for (var i = 0; i < count; i++)
            {
                var input = this.BoundInputType(context.Owner, i);
                if (input?.Kind == BoundTypeKind.SemanticsApplication)
                {
                    input = input.Components[0];
                }

                if (input is not null && this.ProveDefaultOwned(input, use, aggregateSlot, completion) is not ConstraintProof.Proven and var proof)
                {
                    return proof;
                }
            }

            return ConstraintProof.Proven;
        }
    }

    // SPEC 15.4.3 rule 3, 15.4.4, 8.3.3 (R7-O2 O2-F): an input's Owned for a result slot's default `completion` (static, or the
    // borrowed inputs' meet). A proof that is not Proven and whose reading met ValueIn's deferral fact (a local region of a body being
    // bound, or a deferred slot's atom not yet completed) while a body is bound is no verdict: the slot takes its own pending atom, the
    // input joins the slot's row and the slot's obligation (one per slot and body, Discharge ResultDefault) has JudgeObligations judge
    // the row at Final, in a candidate trial as at a commitment. Each resolution of the slot clears the row where it starts, so the
    // row is the slot's last resolution's: a commitment after trials replaces theirs, and a literal a trial reads and nothing commits keeps
    // the trial's. This rests on one order, which call selection keeps: within a pass no resolution of a slot follows its literal's
    // commitment (a probe after it would make the probe's inputs, for a literal with inferred parameters, the judged row). An input two
    // parameters share may be appended twice; its one verdict is judged twice at most.
    private ConstraintProof ProveDefaultOwned(BoundType input, Koto use, int slot, BoundOrigin completion)
    {
        var held = this.readsUnboundedRegion;
        this.readsUnboundedRegion = false;
        var proof = this.ProveOwned(input, use);
        var deferred = proof == ConstraintProof.Unknown && this.readsUnboundedRegion && this.formingBody.IsBody;
        this.readsUnboundedRegion = held;
        if (!deferred)
        {
            return proof;
        }

        var row = this.deferredDefaults[use] ??= new(this.OriginAtom(use, OriginKind.Unbound, slot), completion);
        row.Inputs.Add(input);
        this.AddObligation(new(BindingObligationKind.Owned, use, BindingDeadline.BodyOrigins, Discharge: OriginDischarge.ResultDefault));
        return ConstraintProof.Proven;
    }

    // SPEC 15.4.4, 8.3.3, 23.1.2 (R7-O2 O2-F): at Final, a slot whose last default resolution deferred (its row) completes as a
    // pending Origin does: proven Owned over complete candidates, its atom reads as the default (CompletedOrigin); not, it records its
    // missing Origin as at the header and its atom stays Unbound, resting on the failed slot (UnboundCause, path b), so a relation or
    // Type that names it rests by what it names (Rests, rule W; A5's Dependent end).
    private void JudgeDeferredDefault(in BindingObligation obligation)
    {
        var slot = obligation.Use;
        if (slot.BindingFailure != BindingFailure.None || this.deferredDefaults[slot] is not { } row)
        {
            return;
        }

        foreach (var input in row.Inputs)
        {
            this.failedCause = null;
            if (this.ProveOwned(input, slot) is not ConstraintProof.Proven and var proof)
            {
                _ = this.MissingOrigin(slot, proof);
                return;
            }
        }

        row.Proven = true;
        this.completedDefaults++;
        this.valueInVersion = -1; // ValueIn's reads of the atom so far are stale.
    }

    // The first deferred slot's atom that Final has not completed in `type`'s Origins (the result's, its arguments', its components'), or
    // null: before Final every deferred atom, at Final a refuted one.
    private BoundOrigin? DeferredAtomIn(BoundType type)
    {
        if (!type.CarriesAnyOrigin)
        {
            return null;
        }

        if (type.Origin is { } origin && this.DeferredIn(origin) is { } atom)
        {
            return atom;
        }

        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            if (this.DeferredIn(type.OriginArguments[i]) is { } argument)
            {
                return argument;
            }
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            if (this.DeferredAtomIn(type.Components[i]) is { } component)
            {
                return component;
            }
        }

        return null;
    }

    // The first deferred atom Final has not completed in an Origin, itself or a meet's operand, or null.
    private BoundOrigin? DeferredIn(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Unbound)
        {
            return this.DeferredAtom(origin) is { Proven: false } ? origin : null;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (this.DeferredIn(origin.Operands[i]) is { } operand)
            {
                return operand;
            }
        }

        return null;
    }

    // The row whose provisional Origin `atom` is (a deferred slot's own pending atom), or null.
    private DeferredDefault? DeferredAtom(BoundOrigin atom)
        => atom is { Kind: OriginKind.Unbound, Binder: { } slot } && this.deferredDefaults[slot] is { } row && ReferenceEquals(row.Atom, atom) ? row : null;

    // SPEC 15.4.2, 23.1.2: a result Origin needs its inputs Owned; one that is not is a missing Origin, unless its Error rests on a cause.
    private BoundOrigin? MissingOrigin(Koto use, ConstraintProof unowned)
    {
        if (unowned == ConstraintProof.Error && this.TakeFailedCause().Use is { } cause)
        {
            this.CompleteDependent(use, cause);
        }
        else
        {
            this.Record(use, BindingFailure.MissingOrigin, []);
        }

        return null;
    }

    /// <summary>The `deferredDefaults` family's one accessor (DECISIONS BU-220): a pass family, a result slot's deferred default
    /// (ProveDefaultOwned), kept in its node's module's NodeColumns at its SyntaxId; absent after a pass until the column's first write.</summary>
    private readonly struct DeferredDefaultRows(Binding binding)
    {
        internal DeferredDefault? this[Koto slot]
        {
            get => slot.SyntaxOwner is { } table ? table.Hir.Families.DeferredDefaults.Get(slot.SyntaxId, binding) : null;
            set => HirTables.Of(slot).Families.DeferredDefaults.Ref(slot.SyntaxId, binding) = value;
        }
    }

    /// <summary>A result slot's last default resolution when it deferred (R7-O2 O2-F): the slot's own pending atom, its provisional
    /// Origin every reader names until Final; the default it completes to (static, or the borrowed inputs' meet); the inputs whose Owned
    /// it deferred; and whether Final proved them (then CompletedOrigin reads the atom as the default).</summary>
    internal sealed class DeferredDefault(BoundOrigin atom, BoundOrigin completion)
    {
        internal BoundOrigin Atom => atom;

        internal BoundOrigin Completion => completion;

        internal List<BoundType> Inputs { get; } = new(1);

        internal bool Proven { get; set; }
    }
}
