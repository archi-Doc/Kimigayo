// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, int> shapeCallCandidates = new(ReferenceEqualityComparer.Instance);

    private static bool SameShapeBinding(BoundType a, BoundType b)
        => ReferenceEquals(a, b) || (IsSubtype(a, b) && IsSubtype(b, a));

    private static bool MatchingConstructionExpectation(BoundType pattern, BoundType expected, int own)
    {
        if (pattern.Symbol != expected.Symbol || expected.Semantics != SemanticsKind.Owner || pattern.Components.Count != expected.Components.Count)
        {
            return false;
        }

        for (var i = own; i < pattern.Components.Count; i++)
        {
            if (!SameShapeBinding(pattern.Components[i], expected.Components[i]))
            {
                return false;
            }
        }

        return true;
    }

    // SPEC 7.4.4: whether both candidates take the same evidence for the slot from the call's arguments, by argument index. Each
    // part gives it by its kind (SPEC 10.5.1): a waiting part, an anonymous function included, only the callable slot it stands at,
    // and an anonymous function also the parts its header writes (SPEC 10.5.4); a Tuple or array literal element by element; any
    // other part its parameter Type's occurrences and, at a callable slot, its call signature's. Every occurrence must have the same structural path and extraction rule; an extra candidate-specific source fails.
    // `occurs`: some argument gives the slot evidence in both candidates.
    private bool SameArgumentEvidence(InvocationKoto call, FunctionKoto left, FunctionKoto right, int slot, ReadOnlySpan<int> leftMap, ReadOnlySpan<int> rightMap, out bool occurs)
    {
        var found = false;
        occurs = false;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var a = leftMap[i] < 0 ? null : left.Parameters[leftMap[i]].Type.BoundType;
            var b = rightMap[i] < 0 ? null : right.Parameters[rightMap[i]].Type.BoundType;
            if (!Part(call.ArgumentNodes[i], a, b))
            {
                return false;
            }
        }

        occurs = found;
        return true;

        bool Part(Koto part, BoundType? a, BoundType? b)
        {
            var core = KotoHelper.UnwrapParentheses(part);
            var literal = core as FunctionKoto;
            if (literal is { IsAnonymous: true } || this.IsWaiting(core))
            {
                // A waiting part binds only the callable slot it stands at (SPEC 10.5.4, DECISIONS BU-2).
                if (!Paths(CallableSlot(a, left), CallableSlot(b, right)))
                {
                    return false;
                }

                if (literal is not { IsAnonymous: true })
                {
                    return true;
                }

                var sa = a is null ? null : this.ExpectedCallSignature(left, a);
                var sb = b is null ? null : this.ExpectedCallSignature(right, b);
                for (var p = 0; p < literal.Parameters.Count; p++)
                {
                    if (literal.Parameters[p].Type.BoundType is not null && !Paths(Header(sa, p), Header(sb, p)))
                    {
                        return false;
                    }
                }

                return literal.ReturnType?.BoundType is null || Paths(Header(sa, -1), Header(sb, -1));
            }

            if (CallableParts(core) is { Count: > 0 } elements)
            {
                // A borrowed literal's elements are read below the reference layer both candidates share.
                var (ea, eb) = a is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var ra] } &&
                    b is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var rb] } ? (ra, rb) : (a, b);
                if (ChildPattern(core, 0, ea) is not null && ChildPattern(core, 0, eb) is not null)
                {
                    for (var e = 0; e < elements.Count; e++)
                    {
                        if (!Part(elements[e], ChildPattern(core, e, ea), ChildPattern(core, e, eb)))
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            return Paths(a, b) && Paths(Signature(a, left), Signature(b, right));
        }

        BoundType? Signature(BoundType? pattern, FunctionKoto function)
            => CallableSlot(pattern, function) is { } slotted ? this.ExpectedCallSignature(function, slotted) : null;

        static BoundType? CallableSlot(BoundType? pattern, FunctionKoto function)
            => pattern is not null && CallableSlotType(pattern, CallSlotOwner(function)) is not null ? pattern : null;

        static BoundType? Header(BoundType? signature, int index)
            => signature is { Kind: BoundTypeKind.Function, Components: [var parameters, var returns] }
                ? (index < 0 ? returns : index < parameters.Components.Count ? parameters.Components[index] : null) : null;

        bool Paths(BoundType? a, BoundType? b)
        {
            var x = Occurs(a, left);
            var y = Occurs(b, right);
            if (!x && !y)
            {
                return true;
            }

            if (!x || !y || a!.Kind != b!.Kind || a.Semantics != b.Semantics || a.Components.Count != b.Components.Count)
            {
                return false;
            }

            if (a.Kind == BoundTypeKind.Parameter)
            {
                found = true;
                return this.InfersReadReferent(a, left) == this.InfersReadReferent(b, right);
            }

            if (a.Symbol != b.Symbol)
            {
                return false;
            }

            for (var p = 0; p < a.Components.Count; p++)
            {
                if (!Paths(a.Components[p], b.Components[p]))
                {
                    return false;
                }
            }

            return true;
        }

        bool Occurs(BoundType? type, FunctionKoto function)
        {
            if (type is null || type.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication)
            {
                return false;
            }

            if (type.Kind == BoundTypeKind.Parameter && type.Symbol is { } parameter && ContainerSlot(CallSlotOwner(function), parameter) == slot)
            {
                return true;
            }

            for (var p = 0; p < type.Components.Count; p++)
            {
                if (Occurs(type.Components[p], function))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // This phase reads the whole resolved group. Applicability, defaults and literal defaults
    // supply no evidence; a failure at another source position does not discard a binding here.
    // A slot fixed in neither candidate needs the same argument evidence (SameArgumentEvidence)
    // before its collected Types or modes count: equal Types along other paths are none (SPEC 7.4.4).
    private bool CheckCallShapeContracts(InvocationKoto call, GenericsKoto? generic, BindingScope scope, BoundType? expected, ReadOnlySpan<EvaluatedCandidate> candidates, int[] allMaps, int slotStride)
    {
        var needed = false;
        this.shapeCallCandidates.Clear();
        for (var i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].Accessible)
            {
                continue;
            }

            var function = (FunctionKoto)candidates[i].Symbol!.Declaration;
            this.shapeCallCandidates[function] = i;
            needed |= this.shapeContractHeads.ContainsKey(function);
        }

        if (!needed || candidates.Length < 2)
        {
            return true;
        }

        var types = this.typeScratch.Rent(candidates.Length * slotStride);
        var modes = this.indexScratch.Rent(candidates.Length * slotStride);
        var bindings = this.typeScratch.Rent(slotStride);
        var fixedSlots = this.flagScratch.Rent(candidates.Length * slotStride);
        var savedAcquisition = this.acquisitionFailure;
        try
        {
            modes.AsSpan(0, candidates.Length * slotStride).Clear();
            for (var c = 0; c < candidates.Length; c++)
            {
                if (!candidates[c].Accessible)
                {
                    continue;
                }

                var function = (FunctionKoto)candidates[c].Symbol!.Declaration;
                var owner = CallSlotOwner(function);
                var count = CallSlotCount(function);
                var declaring = function.IsConstructor && call.Method is MemberAccessKoto member ? member.Left.BoundType : candidates[c].DeclaringType;
                this.InitializeCallSlots(call, function, declaring, bindings);
                fixedSlots.AsSpan(c * slotStride, slotStride).Clear();
                for (var g = 0; g < count; g++)
                {
                    if (!function.IsConstructor && generic is not null && g < generic.TypeArguments.Count)
                    {
                        bindings[g] = generic.TypeArguments[g].BoundType;
                    }

                    fixedSlots[(c * slotStride) + g] = bindings[g] is not null;
                    if (bindings[g] is { } fixedType)
                    {
                        modes[(c * slotStride) + g] = (int)this.ShapeOf(fixedType, function, true).Modes;
                    }
                }

                for (var a = 0; a < call.ArgumentNodes.Count; a++)
                {
                    var mapped = allMaps[(c * call.ArgumentNodes.Count) + a];
                    if (mapped >= 0 && function.Parameters[mapped].Type.BoundType is { } pattern && this.CallMemberPattern(pattern, function, declaring) is { } memberPattern)
                    {
                        CollectArgument(memberPattern, call.ArgumentNodes[a]);
                    }
                }

                var result = function.IsConstructor ? declaring : function.BoundSymbol!.Type;
                if (expected is not null && result is not null && (!function.IsConstructor || MatchingConstructionExpectation(result, expected, CallOwnSlots(function).Count)))
                {
                    CollectTypes(result, expected, fillOnly: true);
                }

                bindings.AsSpan(0, count).CopyTo(types.AsSpan(c * slotStride));

                void CollectArgument(BoundType pattern, Koto source)
                {
                    source = KotoHelper.UnwrapParentheses(source);
                    if (source is FunctionKoto { IsAnonymous: true } literal)
                    {
                        if (this.ExpectedCallSignature(function, pattern) is { Kind: BoundTypeKind.Function, Components: [var parameters, var returns] })
                        {
                            for (var p = 0; p < literal.Parameters.Count && p < parameters.Components.Count; p++)
                            {
                                if (literal.Parameters[p].Type.BoundType is { } written)
                                {
                                    CollectTypes(parameters.Components[p], written, false);
                                }
                            }

                            if (literal.ReturnType?.BoundType is { } writtenResult)
                            {
                                CollectTypes(returns, writtenResult, false);
                            }
                        }

                        return;
                    }

                    if (source.BoundType is { } actual)
                    {
                        if (this.AdaptInput(source, pattern, actual, scope, null, declaring, out var adapted, out _, out _, deferAcquisition: true))
                        {
                            actual = adapted;
                        }

                        actual = this.SignatureAtFunction(pattern, actual);
                        CollectTypes(pattern, actual, false);
                        if (CallableSlotType(pattern, CallSlotOwner(function)) is not null &&
                            this.ExpectedCallSignature(function, pattern) is { } requiredSignature &&
                            this.TryCallable(actual, scope, out var actualSignature, out _))
                        {
                            CollectTypes(requiredSignature, actualSignature, false);
                        }

                        return;
                    }

                    if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var referent] })
                    {
                        pattern = referent;
                    }

                    if (source is TupleLiteralKoto tuple && TupleShapeAt(tuple, pattern))
                    {
                        for (var e = 0; e < tuple.Elements.Count; e++)
                        {
                            CollectArgument(pattern.Components[e], tuple.Elements[e]);
                        }
                    }
                    else if (source is ArrayLiteralKoto array && pattern.Kind is BoundTypeKind.FixedArray or BoundTypeKind.Array)
                    {
                        for (var e = 0; e < array.Elements.Count; e++)
                        {
                            CollectArgument(pattern.Components[0], array.Elements[e]);
                        }
                    }
                }

                void CollectTypes(BoundType pattern, BoundType actual, bool fillOnly)
                {
                    if (pattern.Kind == BoundTypeKind.Parameter && pattern.Symbol is { } parameter && ContainerSlot(owner, parameter) is var slot && slot >= 0 && slot < count)
                    {
                        var at = (c * slotStride) + slot;
                        if (fixedSlots[at] || (fillOnly && modes[at] != 0))
                        {
                            return;
                        }

                        if (!fillOnly && this.InfersReadReferent(pattern, function))
                        {
                            actual = ComparisonReferent(actual);
                        }

                        var mode = (int)this.ShapeOf(actual, function, true).Modes;
                        if (modes[at] == 0)
                        {
                            bindings[slot] = actual;
                        }
                        else if (bindings[slot] is not { } previous || !SameShapeBinding(previous, actual))
                        {
                            bindings[slot] = null;
                            mode |= 8; // Keep differing witnesses; equal acquisition modes may still suffice.
                        }

                        modes[at] |= mode;
                        return;
                    }

                    if (pattern.Kind is BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication ||
                        pattern.Kind != actual.Kind || pattern.Symbol != actual.Symbol || pattern.Semantics != actual.Semantics || pattern.Components.Count != actual.Components.Count)
                    {
                        return; // Neither projections nor adaptations are inverted.
                    }

                    for (var p = 0; p < pattern.Components.Count; p++)
                    {
                        CollectTypes(pattern.Components[p], actual.Components[p], fillOnly);
                    }
                }
            }

            for (var c = 0; c < candidates.Length; c++)
            {
                if (!candidates[c].Accessible)
                {
                    continue;
                }

                var function = (FunctionKoto)candidates[c].Symbol!.Declaration;
                for (var at = this.shapeContractHeads.GetValueOrDefault(function, -1); at >= 0;)
                {
                    var contract = this.shapeContracts[at];
                    at = contract.Next;
                    if (!this.shapeCallCandidates.TryGetValue(contract.Pair.Left, out var left))
                    {
                        continue;
                    }

                    for (var d = 0; d < contract.Count; d++)
                    {
                        if (this.MeasureCallInference)
                        {
                            this.inferenceCorrelations++;
                        }

                        var slot = this.shapeDependencies[contract.Start + d];
                        var a = (left * slotStride) + slot;
                        var b = (c * slotStride) + slot;
                        var ma = (AcquisitionModes)(modes[a] & 7);
                        var mb = (AcquisitionModes)(modes[b] & 7);
                        var occurs = false;
                        if (((fixedSlots[a] && fixedSlots[b]) || this.SameArgumentEvidence(call, contract.Pair.Left, function, slot, allMaps.AsSpan(left * call.ArgumentNodes.Count, call.ArgumentNodes.Count), allMaps.AsSpan(c * call.ArgumentNodes.Count, call.ArgumentNodes.Count), out occurs)) &&
                            ((contract.ModeOnly && ma == mb && IsSingleMode(ma)) ||
                            (types[a] is { } ta && types[b] is { } tb && SameShapeBinding(ta, tb)) ||
                            (modes[a] == 0 && modes[b] == 0 && occurs)))
                        {
                            continue;
                        }

                        this.FailExplained(ref this.callInferenceFailures, call, BindingFailure.UnprovenAcquisitionCorrelation, new CallInferenceFailure(contract.Pair.Left, function, null, ConstructionCheck.AcquisitionCorrelation, slot, types[a], types[b]), true);
                        return false;
                    }
                }
            }

            return true;
        }
        finally
        {
            this.acquisitionFailure = savedAcquisition;
            this.flagScratch.Return(fixedSlots);
            this.typeScratch.Return(bindings, clearArray: true);
            this.indexScratch.Return(modes);
            this.typeScratch.Return(types, clearArray: true);
        }
    }
}
