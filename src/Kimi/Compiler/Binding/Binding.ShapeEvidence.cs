// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    private readonly Dictionary<FunctionKoto, int> shapeCallCandidates = new(ReferenceEqualityComparer.Instance);

    private static bool SameShapeBinding(BoundType a, BoundType b)
        => ReferenceEquals(a, b) || (IsSubtype(a, b) && IsSubtype(b, a));

    // SPEC 10.8, 10.3: whether CollectTypes enters a pattern node against a Type, an argument's or the expected result's: the same head
    // and arity; neither projections nor adaptations are inverted. SameArgumentEvidence's Reached follows the same rule.
    private static bool CollectionEnters(BoundType pattern, BoundType actual)
        => pattern.Kind is not (BoundTypeKind.AssociatedProjection or BoundTypeKind.TargetProjection or BoundTypeKind.SemanticsApplication) &&
            pattern.Kind == actual.Kind && pattern.Symbol == actual.Symbol && pattern.Semantics == actual.Semantics &&
            pattern.Components.Count == actual.Components.Count;

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

    // SPEC 7.4.4: whether both candidates take the same evidence for the slot; null when they do not, else whether some evidence
    // reaches it. Each argument is read at its positions by the kinds ArgumentKinds publishes, as the evidence walk reads it
    // (SlotSolution.InferPosition): a typed position other than Never by its parameter Type's occurrences and, at a callable slot,
    // its call signature's; a Literal one likewise; an aggregate or a classified join by its child positions, each candidate's
    // `ref/` layer dropped (InferComposite); an anonymous function by the Types its header writes (SPEC 10.5.4); a waiting or failed
    // position (DECISIONS BU-78) none but the callable slot it stands at (DECISIONS BU-2). A single part's own Type decides the
    // rest, as in InferPosition, which reads it the same way. `expected` is the expected result only where its fill (CollectTypes,
    // SPEC 10.8) reached the slot, and then the occurrences that fill reaches in the candidates' results are compared too (Reached).
    // Every occurrence must have the same structural path and extraction rule; an extra candidate-specific source fails.
    private bool? SameArgumentEvidence(
        InvocationKoto call, ShapePair pair, int slot, ReadOnlySpan<int> leftMap, ReadOnlySpan<int> rightMap, BoundType? expected)
    {
        var (left, right) = (pair.Left, pair.Right);
        var hits = 0;
        for (var i = 0; i < call.ArgumentNodes.Count; i++)
        {
            var a = leftMap[i] < 0 ? null : left.Parameters[leftMap[i]].Type.BoundType;
            var b = rightMap[i] < 0 ? null : right.Parameters[rightMap[i]].Type.BoundType;
            var start = this.OpenPosition(call.ArgumentNodes[i]);
            var same = Position(start, a, b);
            this.ClosePosition(start);
            if (!same)
            {
                return null;
            }
        }

        return expected is null || left.IsConstructor || Reached(left.BoundSymbol!.Type, right.BoundSymbol!.Type, expected) ? hits > 0 : null;

        bool Position(int start, BoundType? a, BoundType? b)
        {
            var end = this.positionParts.Count;
            if (end == start)
            {
                return true; // Never in every source.
            }

            var node = KotoHelper.UnwrapParentheses(this.positionParts[start]);
            BoundType? type = null;
            var kind = end - start == 1 ? this.KindOf(node) : this.PositionKind(start, end, out type);
            if (kind is PartKind.Aggregate or PartKind.Join)
            {
                return Composite(start, end, Referent(a), Referent(b));
            }

            if (end - start == 1 && kind != PartKind.Literal)
            {
                if (node is FunctionKoto { IsAnonymous: true } closure)
                {
                    return Paths(CallableSlot(a, left), CallableSlot(b, right)) && Header(closure, a, b);
                }

                (kind, type) = node.BoundType is null || this.WasDeferred(node) ? (PartKind.Call, null) : (PartKind.Typed, node.BoundType);
            }

            if (kind == PartKind.Call)
            {
                return Paths(CallableSlot(a, left), CallableSlot(b, right)); // A waiting or failed position (DECISIONS BU-78).
            }

            if (kind != PartKind.Literal && (type is null || ReferenceEquals(type, BoundType.Never)))
            {
                return true;
            }

            return Paths(a, b) && (kind == PartKind.Literal || Paths(Signature(a, left), Signature(b, right)));
        }

        // SPEC 10.8, 4.3.1: a composite by its child positions; at a bare slot the whole, only when every part is evidence.
        bool Composite(int start, int end, BoundType? a, BoundType? b)
        {
            if (a?.Kind == BoundTypeKind.Parameter || b?.Kind == BoundTypeKind.Parameter)
            {
                if (this.IndependentPositionType(start, fitLiterals: true) is not null)
                {
                    return Paths(a, b);
                }
            }

            for (var i = 0; i < this.ChildPositions(start, end); i++)
            {
                var child = this.positionParts.Count;
                var eb = this.AddChildPosition(start, end, i, b);
                this.ClosePosition(child);
                var ea = this.AddChildPosition(start, end, i, a);
                var same = Position(child, ea, eb);
                this.ClosePosition(child);
                if (!same)
                {
                    return false;
                }
            }

            return true;
        }

        bool Header(FunctionKoto closure, BoundType? a, BoundType? b)
        {
            var sa = a is null ? null : this.ExpectedCallSignature(left, a);
            var sb = b is null ? null : this.ExpectedCallSignature(right, b);
            for (var p = 0; p < closure.Parameters.Count; p++)
            {
                if (closure.Parameters[p].Type.BoundType is not null && !Paths(SignaturePart(sa, p), SignaturePart(sb, p)))
                {
                    return false;
                }
            }

            return closure.ReturnType?.BoundType is null || Paths(SignaturePart(sa, -1), SignaturePart(sb, -1));
        }

        BoundType? Signature(BoundType? pattern, FunctionKoto function)
            => CallableSlot(pattern, function) is { } slotted ? this.ExpectedCallSignature(function, slotted) : null;

        static BoundType? CallableSlot(BoundType? pattern, FunctionKoto function)
            => pattern is not null && CallableSlotType(pattern, CallSlotOwner(function)) is not null ? pattern : null;

        static BoundType? Referent(BoundType? pattern)
            => pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components: [var referent] } ? referent : pattern;

        static BoundType? SignaturePart(BoundType? signature, int index)
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
                hits++;
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

            if (IsSlot(type, function))
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

        // The slot's occurrences in both results that the expected result's fill reaches, by CollectionEnters: along the same paths.
        bool Reached(BoundType? a, BoundType? b, BoundType expected)
        {
            var x = Reaches(a, expected, left);
            var y = Reaches(b, expected, right);
            if (!x || !y || a is null || b is null)
            {
                return x == y;
            }

            if (IsSlot(a, left) || IsSlot(b, right))
            {
                hits++;
                return IsSlot(a, left) && IsSlot(b, right);
            }

            for (var p = 0; p < a.Components.Count; p++)
            {
                if (!Reached(a.Components[p], b.Components[p], expected.Components[p]))
                {
                    return false;
                }
            }

            return true;
        }

        bool Reaches(BoundType? type, BoundType expected, FunctionKoto function)
        {
            if (type is null || IsSlot(type, function))
            {
                return type is not null;
            }

            if (!CollectionEnters(type, expected))
            {
                return false;
            }

            for (var p = 0; p < type.Components.Count; p++)
            {
                if (Reaches(type.Components[p], expected.Components[p], function))
                {
                    return true;
                }
            }

            return false;
        }

        bool IsSlot(BoundType type, FunctionKoto function)
            => type.Kind == BoundTypeKind.Parameter && type.Symbol is { } parameter && ContainerSlot(CallSlotOwner(function), parameter) == slot;
    }

    // This phase reads the whole resolved group. Applicability, defaults and literal defaults
    // supply no evidence; a failure at another source position does not discard a binding here.
    // A slot not fixed in both candidates needs the same evidence (SameArgumentEvidence, the expected
    // result's included) before its collected Types or modes count: equal Types along other paths are none (SPEC 7.4.4).
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
        // Per candidate and slot: bits 1, 2 and 4 the collected modes, 8 differing witnesses, 16 the expected result's fill reached it.
        var modes = this.indexScratch.Rent(candidates.Length * slotStride);
        var bindings = this.infer.Rent(slotStride);
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
                        bindings.SetType(g, generic.TypeArguments[g].BoundType);
                    }

                    fixedSlots[(c * slotStride) + g] = bindings.Types[g] is not null;
                    if (bindings.Types[g] is { } fixedType)
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

                bindings.Types[..count].CopyTo(types.AsSpan(c * slotStride));

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
                            bindings.SetType(slot, actual);
                        }
                        else if (bindings.Types[slot] is not { } previous || !SameShapeBinding(previous, actual))
                        {
                            bindings.SetType(slot, null);
                            mode |= 8; // Keep differing witnesses; equal acquisition modes may still suffice.
                        }

                        modes[at] |= fillOnly ? mode | 16 : mode; // 16: the expected result's fill reached the slot (SPEC 10.8).
                        return;
                    }

                    if (!CollectionEnters(pattern, actual))
                    {
                        return;
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
                        if ((fixedSlots[a] && fixedSlots[b] ? true : this.SameArgumentEvidence(
                                call,
                                contract.Pair,
                                slot,
                                allMaps.AsSpan(left * call.ArgumentNodes.Count, call.ArgumentNodes.Count),
                                allMaps.AsSpan(c * call.ArgumentNodes.Count, call.ArgumentNodes.Count),
                                ((modes[a] | modes[b]) & 16) != 0 ? expected : null)) is { } occurs &&
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
            this.infer.Return(bindings);
            this.indexScratch.Return(modes);
            this.typeScratch.Return(types, clearArray: true);
        }
    }
}
