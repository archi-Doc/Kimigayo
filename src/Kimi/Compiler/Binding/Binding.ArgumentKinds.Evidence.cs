// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // SPEC 10.8, 10.5.1: the evidence walk of a candidate's solution, by each argument part's kind at its position. A typed part is
    // evidence through the common adaptation (SPEC 10.2) and, at a Function pattern, its known call signature; a Never-typed part is
    // none. A Literal part is evidence only through the default applied to the selected candidate (SPEC 10.2.1). A waiting part is none,
    // except the parameter and result Types an anonymous function's header writes (SPEC 10.5.4). A Tuple or array literal is evidence by
    // its parts at their positions, and at a bare slot only when every part is; a mixed range by its range typing (SPEC 4.6.2). The early
    // pass takes typed parts and written headers; the late pass, after the expected result, the defaults and range typing.
    private ref partial struct SlotSolution
    {
        // The Type of a parameter as this candidate reads it: the declaring Type's slots substituted and Self replaced; null when it fails.
        internal readonly BoundType? Pattern(BoundType pattern)
            => this.Binding.CallMemberPattern(pattern, this.Function, this.DeclaringType) is { } member
                ? this.Binding.ContractType(member, this.Scope, this.Self) : null;

        // SPEC 10.8: one argument part's evidence for the slots of the pattern at its position (Pattern), in the early or the late pass.
        internal bool InferArgument(BoundType pattern, Koto part, bool late)
        {
            var binding = this.Binding;
            var node = KotoHelper.UnwrapParentheses(part);
            if (node is FunctionKoto { IsAnonymous: true } closure)
            {
                return late || this.InferAnonymous(pattern, part, closure);
            }

            switch (binding.KindOf(node))
            {
                case ArgumentKind.Composite:
                    return NeedsEnumContext(node) || this.InferComposite(pattern, part, late); // A contextual Case is resolved in the expected Type.
                case ArgumentKind.Literal:
                    return !late || binding.DefaultOf(node) is not { } literalDefault ||
                        binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null ||
                        this.InferAdapted(pattern, literalDefault, node);
                default:
                    return late || node.BoundType is not { } actual || ReferenceEquals(actual, BoundType.Never) || this.InferAdapted(pattern, actual, part);
            }
        }

        // SPEC 10.8: an argument's (or the receiver's) Type as evidence for the slots of its parameter's declared Type.
        internal bool InferInput(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
            => this.Pattern(pattern) is { } member && this.InferAdapted(member, actual, source, path, receiver);

        // SPEC 10.5.4: the parameter and result Types an anonymous function's header writes are evidence for the slots of its signature.
        internal readonly bool InferClosureHeader(BoundType signature, FunctionKoto literal)
        {
            var binding = this.Binding;
            if (this.Pattern(signature) is not { } memberSignature)
            {
                return false;
            }

            var parameterTypes = memberSignature.Components[0];
            var count = ReferenceEquals(parameterTypes, BoundType.Unit) ? 0 : parameterTypes.Components.Count;
            if (literal.Parameters.Count != count)
            {
                return false;
            }

            var headerScope = binding.scopes[literal];
            var header = new SignatureEvidence(literal, literal);
            for (var i = 0; i < count; i++)
            {
                var written = literal.Parameters[i].Type;
                if (written is not SyntaxFormKoto { Akind: KotoKind.InferredType } &&
                    (binding.BindType(written, headerScope) is not { } actual ||
                    !binding.Infer(
                        parameterTypes.Components[i], actual, this.SlotOwner, this.Types, lengths: this.Lengths, structural: true, evidence: header)))
                {
                    return false;
                }
            }

            return literal.ReturnType is null || (binding.BindType(literal.ReturnType, headerScope) is { } result &&
                binding.Infer(memberSignature.Components[1], result, this.SlotOwner, this.Types, lengths: this.Lengths, structural: true, evidence: header));
        }

        // SPEC 10.8: the Type bound to F also supplies its independently known call signature. This is evidence alongside ordinary
        // inputs, before expected results and literal defaults. An anonymous waiting body is never evidence; its written header is
        // handled separately. The signature's own Origins become open regions.
        internal readonly bool InferCallableSignatures(int[] mapping, Span<BoundArgumentOperation> operations)
        {
            var binding = this.Binding;
            var arguments = this.Call.ArgumentNodes;
            for (var i = 0; i < arguments.Count; i++)
            {
                var source = arguments[i];
                if (this.CallableSlot(mapping[i]) is not { } callableSlot ||
                    KotoHelper.UnwrapParentheses(source) is FunctionKoto { IsAnonymous: true } || binding.WasWaitingPart(source) ||
                    source.BoundType is not { } actual ||
                    !binding.TryCallable(callableSlot, binding.ConstraintScope(this.Function), out var requiredSignature, out _) ||
                    !binding.TryCallable(actual, this.Scope, out var actualSignature, out _))
                {
                    continue;
                }

                if (this.Pattern(requiredSignature) is not { } memberSignature)
                {
                    return false;
                }

                var evidence = new SignatureEvidence(CalleeBinder(actual, actualSignature), source);
                if (!binding.Infer(memberSignature, actualSignature, this.SlotOwner, this.Types, lengths: this.Lengths, structural: true, evidence: evidence))
                {
                    var required = this.Type(memberSignature) ?? memberSignature;
                    operations[i] = new(source, actualSignature, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.5.1, 10.2.1: before its default applies, a Literal fits a position that holds an unbound slot only by the slot-free
        // structure its default would have; it binds no slot.
        internal readonly bool FitsLiteralShape(BoundType pattern, BoundType shape)
        {
            var binding = this.Binding;
            if (this.Pattern(pattern) is not { } memberPattern)
            {
                return false;
            }

            var count = CallSlotCount(this.Function);
            var trial = binding.typeScratch.Rent(count);
            try
            {
                this.Types.AsSpan(0, count).CopyTo(trial);
                return binding.Infer(LiteralInputType(memberPattern), shape, this.SlotOwner, trial);
            }
            finally
            {
                binding.typeScratch.Return(trial, clearArray: true);
            }
        }

        // SPEC 10.2.1: after selection, each Literal part at a position that holds an unsolved slot takes its default (SPEC 12.3, and
        // 4.6.2 rule 3 for a literal-only range boundary), recorded for the candidate's checks to run once more. Returns the first such
        // part that its default does not fit, bound at it for its own record; `applied` tells whether any default applied.
        internal readonly Koto? DefaultArguments(int[] mapping, out bool applied)
        {
            var unsolved = this.Binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved, out _);
                applied = false;
                Koto? misfit = null;
                for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
                {
                    misfit ??= this.DefaultPart(this.Call.ArgumentNodes[i], this.Function.Parameters[mapping[i]].Type.BoundType!, unsolved, ref applied);
                }

                return misfit;
            }
            finally
            {
                this.Binding.ReturnSlotSet(unsolved);
            }
        }

        // An anonymous function: once bound, its Type at any pattern but a Function Type (where its signature meets the pattern after
        // selection); always the Types its header writes, at a pattern with a call signature.
        private bool InferAnonymous(BoundType pattern, Koto part, FunctionKoto closure)
            => (closure.BoundType is not { } actual || pattern.Kind == BoundTypeKind.Function || this.InferAdapted(pattern, actual, part)) &&
                (!this.Binding.TryCallable(pattern, this.Binding.ConstraintScope(this.Function), out var header, out _) ||
                this.InferClosureHeader(header, closure));

        // SPEC 10.8, 10.2: a Type at the pattern of its position, through the common adaptation.
        private bool InferAdapted(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
        {
            var binding = this.Binding;
            var function = this.Function;
            var inferred = this.InfersConstruction(out var target);
            var fixedOwner = inferred ? target!.BoundType : this.DeclaringType;
            var adaptationPattern = function.IsConstructor ? binding.MemberType(pattern, fixedOwner) ?? pattern : pattern;
            if (!binding.AdaptInput(
                source, adaptationPattern, actual, this.Scope, path, this.DeclaringType, out actual, out _, out _, receiver: receiver, deferAcquisition: true))
            {
                return false;
            }

            var callee = actual;
            if (pattern.Kind == BoundTypeKind.Function && binding.FunctionItemSignature(actual) is { } itemSignature)
            {
                actual = itemSignature;
            }
            else if (pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Closure &&
                actual.Symbol?.Declaration is FunctionKoto { BoundClosure: { } closure })
            {
                // SPEC 7.7.4, 10.5: a concrete Closure meets a common Function Type through its signature; the conversion itself,
                // its receiver and Owned conditions included, is judged once the call is selected.
                actual = closure.Signature;
            }

            pattern = binding.FormedApplication(pattern, this.SlotOwner, this.Types);
            var known = pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function
                ? new SignatureEvidence(CalleeBinder(callee, actual), source) : (SignatureEvidence?)null;
            var quantifier = known is { } evidence ? evidence.Binder ?? evidence.Source : null; // The argument itself binds nothing.
            binding.MatchInputOrigins(pattern, actual, function, this.Origins, this.Inputs, quantifier);
            if (pattern.CarriesOrigin && actual.CarriesOrigin)
            {
                this.OriginInference ??= binding.BeginOriginInference(this.Call, function);
                binding.CollectOriginInference(pattern, actual, this.OriginInference, known: quantifier);
                binding.CollectInputPremises(pattern, this.OriginInference, this.Call);
            }

            pattern = binding.SubstituteStoredOrigins(pattern, function, this.Origins.AsSpan(0, function.Origins.Count), this.InputSpan());
            // SPEC 10.5: a nested call checked against this candidate supplies relations for checking, never outer slot evidence.
            if (binding.WasWaitingPart(source))
            {
                return true;
            }

            var common = this.Generic is null && (!function.IsConstructor || inferred);
            var structural = pattern.Kind == BoundTypeKind.Function;
            return binding.Infer(
                pattern,
                actual,
                this.SlotOwner,
                this.Types,
                inferOrigins: true,
                lengths: this.Lengths,
                commonOrigins: common,
                structural: structural,
                evidence: known,
                relateLater: true);
        }

        // SPEC 10.8, 4.3.1, 4.6.2: a Tuple or array literal or a mixed range, part by part at their positions; at a bare slot the
        // whole literal, only when every part is evidence. A composite at `ref/T` borrows its temporary, whose Origin binds the input's.
        private bool InferComposite(BoundType pattern, Koto part, bool late)
        {
            var binding = this.Binding;
            if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref })
            {
                if (pattern.Origin is { } origin)
                {
                    binding.MatchInputOrigin(origin, binding.PlaceOrigin(part), this.Function, this.Origins, this.Inputs);
                }

                pattern = pattern.Components[0];
            }

            var source = KotoHelper.UnwrapParentheses(part);

            if (source is RangeKoto mixed && IsMixedRange(mixed) && pattern.Kind != BoundTypeKind.Parameter)
            {
                return this.InferMixedRange(pattern, mixed, late);
            }

            if (pattern.Kind == BoundTypeKind.Parameter)
            {
                if (binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is { } fixedType)
                {
                    pattern = fixedType;
                }
                else if (binding.IndependentAggregateType(source, late) is { } inferred)
                {
                    return binding.Infer(pattern, inferred, this.SlotOwner, this.Types, lengths: this.Lengths);
                }
            }

            if (source is TupleLiteralKoto tuple)
            {
                if (pattern.Kind != BoundTypeKind.Tuple || pattern.Components.Count != tuple.Elements.Count)
                {
                    return true; // Probing reports a shape mismatch once the target is fixed.
                }

                for (var i = 0; i < tuple.Elements.Count; i++)
                {
                    if (!this.InferArgument(pattern.Components[i], tuple.Elements[i], late))
                    {
                        return false;
                    }
                }

                return true;
            }

            return source is not ArrayLiteralKoto array || this.InferArrayLiteral(pattern, array, late);
        }

        // An array literal's length binds the function's length slot of a fixed-array pattern; each element is evidence at its Type.
        private bool InferArrayLiteral(BoundType pattern, ArrayLiteralKoto array, bool late)
        {
            if (pattern.Kind is not (BoundTypeKind.FixedArray or BoundTypeKind.Array))
            {
                return true; // Existing candidate-specific probing diagnoses other shapes.
            }

            if (pattern.Kind == BoundTypeKind.FixedArray && pattern.LengthExpression is { Parameter: { } parameter } &&
                ReferenceEquals(parameter.Scope.Owner, this.Function))
            {
                var length = array.FillLength is null ? this.Binding.InternLength(KotoKind.NumberLiteral, array.Elements.Count) : array.FillCount;
                if (length is null)
                {
                    return false;
                }

                if (this.Lengths[parameter.Slot] is { } previous && !ReferenceEquals(previous, length))
                {
                    return false;
                }

                this.Lengths[parameter.Slot] = length;
            }

            for (var i = 0; i < array.Elements.Count; i++)
            {
                if (!this.InferArgument(pattern.Components[0], array.Elements[i], late))
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 4.6.2: a mixed range's typed boundary is evidence first; after all other evidence, a literal-only boundary whose S or E
        // is still unknown takes the other boundary's integer Type, else i32 (rules 2 and 3).
        private readonly bool InferMixedRange(BoundType pattern, RangeKoto mixed, bool late)
        {
            var binding = this.Binding;
            if (pattern is not { Kind: BoundTypeKind.Constructed, Components.Count: 2 } ||
                !ReferenceEquals(pattern.Symbol, mixed.IsInclusive ? binding.Library.ClosedRange : binding.Library.Range))
            {
                return true; // Probing reports a shape mismatch once the target is fixed.
            }

            var startLiteral = mixed.Start is { } first && IsLiteralOnlyPosition(first);
            var endLiteral = mixed.End is { } last && IsLiteralOnlyPosition(last);
            if ((mixed.Start is not null && !startLiteral && !this.InferBoundary(pattern.Components[0], mixed.Start)) ||
                (mixed.End is not null && !endLiteral && !this.InferBoundary(pattern.Components[1], mixed.End)))
            {
                return false;
            }

            return (!startLiteral ||
                this.InferLiteralBoundary(pattern.Components[0], mixed.Start!, binding.IndependentRangeBoundary(mixed.End, binding.EndType), late)) &&
                (!endLiteral ||
                this.InferLiteralBoundary(pattern.Components[1], mixed.End!, binding.IndependentRangeBoundary(mixed.Start, binding.StartType), late));
        }

        // SPEC 4.6.2, 3.5.3: a typed range boundary contributes the read Type it supplies; a boundary is never waiting.
        private readonly bool InferBoundary(BoundType pattern, Koto boundary)
        {
            var binding = this.Binding;
            var source = KotoHelper.UnwrapParentheses(boundary);
            if (source.BoundType is not { } actual || ReferenceEquals(actual, BoundType.Never))
            {
                return true; // SPEC 10.8: a Never-typed boundary is no evidence.
            }

            actual = KotoHelper.UnwrapParentheses(source.Parent!) is RangeKoto && binding.ReadTypeReferent(actual, source) is { } read ? read : actual;
            binding.MatchInputOrigins(pattern, actual, this.Function, this.Origins, this.Inputs);
            pattern = binding.SubstituteStoredOrigins(pattern, this.Function, this.Origins.AsSpan(0, this.Function.Origins.Count), this.InputSpan());
            return binding.Infer(pattern, actual, this.SlotOwner, this.Types, inferOrigins: true, lengths: this.Lengths);
        }

        // SPEC 4.6.2: in the late pass, a literal-only boundary whose S or E no other evidence fixed takes the other boundary's integer
        // Type (rule 2), or its default once DefaultArguments applied it to the selected candidate.
        private readonly bool InferLiteralBoundary(BoundType pattern, Koto boundary, BoundType? other, bool late)
            => !late || this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null ||
                (this.Binding.DefaultOf(boundary) ?? this.Binding.RangeTyping(boundary, other)) is not { } type ||
                this.Binding.Infer(pattern, type, this.SlotOwner, this.Types, lengths: this.Lengths);

        // The defaults of a part's Literal leaves at the positions that hold an unsolved slot: the part itself, the elements of a Tuple
        // or array literal and the literal-only boundary of a mixed range.
        private readonly Koto? DefaultPart(Koto part, BoundType pattern, SlotSet unsolved, ref bool applied)
        {
            var binding = this.Binding;
            if (!MentionsSlots(pattern, this.SlotOwner, unsolved))
            {
                return null;
            }

            var source = KotoHelper.UnwrapParentheses(part);
            if (binding.KindOf(source) == ArgumentKind.Literal)
            {
                return this.Default(source, binding.PartDefault(source, this.Scope), ref applied);
            }

            Koto? misfit = null;
            if (source is TupleLiteralKoto tuple)
            {
                for (var i = 0; i < tuple.Elements.Count; i++)
                {
                    var element = pattern.Kind == BoundTypeKind.Tuple && pattern.Components.Count == tuple.Elements.Count ? pattern.Components[i] : pattern;
                    misfit ??= this.DefaultPart(tuple.Elements[i], element, unsolved, ref applied);
                }
            }
            else if (source is ArrayLiteralKoto array)
            {
                var element = pattern.Kind is BoundTypeKind.Array or BoundTypeKind.FixedArray ? pattern.Components[0] : pattern;
                for (var i = 0; i < array.Elements.Count; i++)
                {
                    misfit ??= this.DefaultPart(array.Elements[i], element, unsolved, ref applied);
                }
            }
            else if (source is RangeKoto range && IsMixedRange(range))
            {
                var ranged = pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 2 };
                misfit = this.DefaultBoundary(range.Start, range.End, ranged ? pattern.Components[0] : pattern, binding.EndType, unsolved, ref applied) ??
                    this.DefaultBoundary(range.End, range.Start, ranged ? pattern.Components[1] : pattern, binding.StartType, unsolved, ref applied);
            }

            return misfit;
        }

        // SPEC 4.6.2: a literal-only boundary still unbound takes the other boundary's integer Type, else i32 (rules 2 and 3).
        private readonly Koto? DefaultBoundary(Koto? boundary, Koto? other, BoundType pattern, BoundType omitted, SlotSet unsolved, ref bool applied)
        {
            if (boundary is null || !IsLiteralOnlyPosition(boundary) || boundary.BoundType is not null || !MentionsSlots(pattern, this.SlotOwner, unsolved))
            {
                return null;
            }

            var binding = this.Binding;
            return this.Default(boundary, binding.OtherBoundaryType(boundary, binding.IndependentRangeBoundary(other, omitted)), ref applied);
        }

        private readonly Koto? Default(Koto literal, BoundType? type, ref bool applied)
        {
            var binding = this.Binding;
            if (type is null)
            {
                return null;
            }

            binding.argumentKinds[SourceRef.Of(KotoHelper.UnwrapParentheses(literal))] = new(ArgumentKind.Literal, type);
            applied = true;
            if (binding.FitsLiteralPart(literal, type, this.Scope))
            {
                return null;
            }

            binding.RequireType(literal, this.Scope, type);
            return literal;
        }
    }
}
