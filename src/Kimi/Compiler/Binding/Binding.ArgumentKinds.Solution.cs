// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // A candidate's solution from its call's evaluation: the call's slot arrays, scope, Self and explicit Type arguments.
    private SlotSolution Solution(in CallEvaluation evaluation, FunctionKoto function, BoundType? declaringType)
        => new()
        {
            Binding = this,
            Call = evaluation.Call,
            Function = function,
            Generic = evaluation.Generic,
            Scope = evaluation.Scope,
            Self = evaluation.Self,
            DeclaringType = declaringType,
            Types = evaluation.Scratch,
            Lengths = evaluation.LengthArguments,
            Origins = evaluation.Origins,
            Inputs = evaluation.Inputs,
        };

    // The SPEC 10.8 solve state of one candidate (DECISIONS BU-2): its Type, length and Origin slots and its input Origins, with what
    // substitution reads (scope, Self, declaring Type), its explicit Type arguments, whether a construction's inferred Type is checked
    // fixed (ValidateFixedConstruction) and the Origin inference its evidence begins. The slots belong to the function, or to the
    // structure for a constructor (SlotOwner); the function binds the Origins. Its operations add an argument part's evidence to the
    // slots, substitute the solution into a pattern and judge the candidate's Constraints over it.
    private ref struct SlotSolution
    {
        internal Binding Binding;
        internal InvocationKoto Call;
        internal FunctionKoto Function;
        internal GenericsKoto? Generic;
        internal BindingScope Scope;
        internal BoundType? Self;
        internal BoundType? DeclaringType;
        internal BoundType?[] Types;
        internal BoundLength?[] Lengths;
        internal BoundOrigin[] Origins;
        internal BoundOrigin[] Inputs;
        internal bool FixedConstruction;
        internal OriginInference? OriginInference;

        internal readonly Koto SlotOwner => CallSlotOwner(this.Function);

        // The pattern with the solution substituted, or null while it holds an unbound slot.
        internal readonly BoundType? Type(BoundType pattern)
            => this.Binding.CallType(pattern, this.Function, this.Types, this.Scope, this.Self, this.Origins, this.Inputs, this.DeclaringType, this.Lengths);

        // Whether every own slot is bound.
        internal readonly bool IsComplete()
        {
            var own = CallOwnSlots(this.Function);
            for (var i = 0; i < own.Count; i++)
            {
                if (own[i] is LengthParameterKoto ? this.Lengths[i] is null : this.Types[i] is null)
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.5.1: whether the result holds a Type or Semantics slot that no evidence binds; a constructor's result holds all its
        // slots. Origin slots are not counted.
        internal readonly bool ResultHoldsUnsolved(int[] mapping)
        {
            var unsolved = this.Binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved, out _);
                return !unsolved.IsEmpty &&
                    (this.Function.IsConstructor || (this.Function.BoundSymbol?.Type is { } result && MentionsSlots(result, this.SlotOwner, unsolved)));
            }
            finally
            {
                this.Binding.ReturnSlotSet(unsolved);
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

        // SPEC 10.5.1, 10.2.1: before its default applies, a Literal fits a position that holds an unbound slot only by the slot-free
        // structure its default would have; it binds no slot.
        internal readonly bool FitsLiteralShape(BoundType pattern, BoundType shape)
        {
            var binding = this.Binding;
            if (binding.CallMemberPattern(pattern, this.Function, this.DeclaringType) is not { } memberPattern)
            {
                return false;
            }

            var count = CallSlotCount(this.Function);
            var trial = binding.typeScratch.Rent(count);
            try
            {
                this.Types.AsSpan(0, count).CopyTo(trial);
                return binding.Infer(LiteralInputType(binding.ContractType(memberPattern, this.Scope, this.Self)), shape, this.SlotOwner, trial);
            }
            finally
            {
                binding.typeScratch.Return(trial, clearArray: true);
            }
        }

        // SPEC 10.8: an argument's (or the receiver's) Type as evidence for the slots of its parameter's pattern.
        internal bool InferInput(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
        {
            var binding = this.Binding;
            var function = this.Function;
            if (binding.CallMemberPattern(pattern, function, this.DeclaringType) is not { } memberPattern)
            {
                return false;
            }

            pattern = binding.ContractType(memberPattern, this.Scope, this.Self);
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
            if (binding.WasWaitingNestedCall(source))
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

        // SPEC 10.5.4: the parameter and result Types an anonymous function's header writes are evidence for the slots of its signature.
        internal readonly bool InferClosureHeader(BoundType signature, FunctionKoto literal)
        {
            var binding = this.Binding;
            if (binding.CallMemberPattern(signature, this.Function, this.DeclaringType) is not { } memberSignature)
            {
                return false;
            }

            signature = binding.ContractType(memberSignature, this.Scope, this.Self);
            var parameterTypes = signature.Components[0];
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
                binding.Infer(signature.Components[1], result, this.SlotOwner, this.Types, lengths: this.Lengths, structural: true, evidence: header));
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
                    KotoHelper.UnwrapParentheses(source) is FunctionKoto { IsAnonymous: true } || binding.WasWaitingNestedCall(source) ||
                    source.BoundType is not { } actual ||
                    !binding.TryCallable(callableSlot, binding.ConstraintScope(this.Function), out var requiredSignature, out _) ||
                    !binding.TryCallable(actual, this.Scope, out var actualSignature, out _))
                {
                    continue;
                }

                if (binding.CallMemberPattern(requiredSignature, this.Function, this.DeclaringType) is not { } memberSignature)
                {
                    return false;
                }

                requiredSignature = binding.ContractType(memberSignature, this.Scope, this.Self);
                var evidence = new SignatureEvidence(CalleeBinder(actual, actualSignature), source);
                if (!binding.Infer(requiredSignature, actualSignature, this.SlotOwner, this.Types, lengths: this.Lengths, structural: true, evidence: evidence))
                {
                    var required = this.Type(requiredSignature) ?? requiredSignature;
                    operations[i] = new(source, actualSignature, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.8, 4.3.1: each Composite argument's evidence for the slots of its parameter's pattern; `fitLiterals` adds its literal
        // parts' defaults where no other evidence fixed the position.
        internal readonly bool InferAggregateInputs(int[] mapping, bool fitLiterals)
        {
            var binding = this.Binding;
            var arguments = this.Call.ArgumentNodes;
            for (var i = 0; i < arguments.Count; i++)
            {
                if (!IsAggregateArgument(arguments[i]))
                {
                    continue;
                }

                if (binding.CallMemberPattern(this.Function.Parameters[mapping[i]].Type.BoundType!, this.Function, this.DeclaringType) is not { } pattern)
                {
                    return false;
                }

                pattern = binding.ContractType(pattern, this.Scope, this.Self);
                if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref })
                {
                    if (pattern.Origin is { } origin)
                    {
                        binding.MatchInputOrigin(origin, binding.PlaceOrigin(arguments[i]), this.Function, this.Origins, this.Inputs);
                    }

                    pattern = pattern.Components[0];
                }

                if (!this.InferAggregateCall(pattern, arguments[i], fitLiterals))
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.8, 4.3.1, 4.6.2: a Composite argument's evidence for the slots of its parameter's pattern, part by part: a typed part
        // at its position, and with `fitLiterals` a literal part's default where no other evidence fixed the position.
        internal readonly bool InferAggregateCall(BoundType pattern, Koto source, bool fitLiterals)
        {
            var binding = this.Binding;
            source = KotoHelper.UnwrapParentheses(source);
            if (source is RangeKoto mixed && IsMixedRange(mixed) && pattern.Kind != BoundTypeKind.Parameter)
            {
                return this.InferMixedRange(pattern, mixed, fitLiterals);
            }

            if (pattern.Kind == BoundTypeKind.Parameter)
            {
                if (binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is { } fixedType)
                {
                    pattern = fixedType;
                }
                else if (binding.IndependentAggregateType(source, fitLiterals) is { } inferred)
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
                    if (!this.InferAggregateCall(pattern.Components[i], tuple.Elements[i], fitLiterals))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (source is ArrayLiteralKoto array)
            {
                return this.InferArrayLiteral(pattern, array, fitLiterals);
            }

            if (source.BoundType is { } actual)
            {
                // A typed range boundary contributes the read Type it supplies (SPEC 3.5.3).
                actual = KotoHelper.UnwrapParentheses(source.Parent!) is RangeKoto && binding.ReadTypeReferent(actual, source) is { } read ? read : actual;
                binding.MatchInputOrigins(pattern, actual, this.Function, this.Origins, this.Inputs);
                pattern = binding.SubstituteStoredOrigins(pattern, this.Function, this.Origins.AsSpan(0, this.Function.Origins.Count), this.InputSpan());
                return binding.Infer(pattern, actual, this.SlotOwner, this.Types, inferOrigins: true, lengths: this.Lengths);
            }

            if (!fitLiterals || binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null)
            {
                return true;
            }

            return binding.DefaultOf(source) is not { } literalDefault ||
                binding.Infer(pattern, literalDefault, this.SlotOwner, this.Types, lengths: this.Lengths);
        }

        // SPEC 15.3.6, 10.8: a Callable proof that fails only because a slot holds the open region of an argument's own per-call Origin,
        // and holds once that per-call Origin stands in for it, is no inapplicability: the argument is reported after selection.
        internal readonly bool PerCallCallables(int[] mapping, Span<BoundArgumentOperation> marks)
        {
            var count = CallOwnSlots(this.Function).Count;
            var restored = this.Binding.typeScratch.Rent(count);
            try
            {
                // SPEC 10.7, 10.8: first instantiate each argument's per-call input at a required input over a fixed Origin; when every
                // Constraint then holds, that instantiation solves the slots and nothing is reported.
                this.Types.AsSpan(0, count).CopyTo(restored);
                if (this.StandIns(mapping, restored.AsSpan(0, count), marks, true) && this.Prove(restored.AsSpan(0, count), false) == ConstraintProof.Proven)
                {
                    restored.AsSpan(0, count).CopyTo(this.Types);
                    this.CompleteOperationTypes(marks);
                    return true;
                }

                this.Types.AsSpan(0, count).CopyTo(restored);
                var marked = this.StandIns(mapping, restored.AsSpan(0, count), marks, false);
                if (!marked || this.Prove(restored.AsSpan(0, count), false) != ConstraintProof.Proven)
                {
                    for (var i = 0; i < this.Call.ArgumentNodes.Count && marked; i++)
                    {
                        marks[i] = marks[i] with { MissingOrigin = false };
                    }

                    return false;
                }

                return true;
            }
            finally
            {
                this.Binding.typeScratch.Return(restored, clearArray: true);
            }
        }

        // Each operation's parameter Type with the solution substituted, where it is complete.
        internal readonly void CompleteOperationTypes(Span<BoundArgumentOperation> operations)
        {
            for (var i = 0; i < operations.Length; i++)
            {
                if (operations[i] is { Source: not null, ParameterIndex: >= 0 } operation &&
                    this.Type(this.Function.Parameters[operation.ParameterIndex].Type.BoundType!) is { } completed)
                {
                    operations[i] = operation with { ParameterType = completed };
                }
            }
        }

        // SPEC 10.8: the Constraints and premises whose judgment needs an unsolved slot are not judged.
        internal readonly ConstraintProof Prove(ReadOnlySpan<BoundType?> slots, bool incomplete, SlotSet unbound = default)
        {
            var binding = this.Binding;
            var function = this.Function;
            var constructor = function.IsConstructor;
            var arguments = constructor ? this.Types.AsSpan(0, CallSlotCount(function)) : slots;
            var proof = binding.CheckConstraints(
                function.TypeConstraints,
                function,
                arguments,
                this.Scope,
                this.Self,
                constructor ? null : this.DeclaringType,
                this.Lengths,
                incomplete: incomplete || !unbound.IsEmpty,
                skipUnresolved: !unbound.IsEmpty,
                slotOwner: this.SlotOwner);
            if (this.DeclaringType is { } declaringType)
            {
                var closedOwner = constructor ? binding.ConstructionType(function, declaringType, this.Types) : declaringType;
                if (closedOwner is not null)
                {
                    proof = CombineProof(proof, binding.CheckTypeConstraints(closedOwner, this.Scope), true);
                }
                else if (constructor && this.SlotOwner is StructKoto owner)
                {
                    var ownerSlots = this.Types.AsSpan(0, CallSlotCount(function));
                    var ownerProof = binding.CheckConstraints(
                        owner.ConstraintNodes, owner, ownerSlots, this.Scope, incomplete: true, skipUnresolved: !unbound.IsEmpty);
                    proof = CombineProof(proof, ownerProof, true);
                }
            }

            proof = CombineProof(proof, binding.CheckSignatureTypeConstraints(function), true);
            // A substitution must be a valid complete Type independently of whether the function constrains or uses that slot (SPEC 8.1.3).
            for (var i = 0; i < slots.Length; i++)
            {
                proof = CombineProof(proof, this.SlotProof(slots, i, unbound), true);
            }

            return CombineProof(proof, binding.ProveMemberConditions(function.BoundSymbol!, this.DeclaringType, this.Scope), true);
        }

        // SPEC 4.6.2: a mixed range's typed boundary is evidence first; after all other evidence, a literal-only boundary whose S or E
        // is still unknown takes the other boundary's integer Type, else i32 (rules 2 and 3).
        private readonly bool InferMixedRange(BoundType pattern, RangeKoto mixed, bool fitLiterals)
        {
            var binding = this.Binding;
            if (pattern is not { Kind: BoundTypeKind.Constructed, Components.Count: 2 } ||
                !ReferenceEquals(pattern.Symbol, mixed.IsInclusive ? binding.Library.ClosedRange : binding.Library.Range))
            {
                return true; // Probing reports a shape mismatch once the target is fixed.
            }

            var startLiteral = mixed.Start is { } first && IsLiteralOnlyPosition(first);
            var endLiteral = mixed.End is { } last && IsLiteralOnlyPosition(last);
            if ((mixed.Start is not null && !startLiteral && !this.InferAggregateCall(pattern.Components[0], mixed.Start, fitLiterals)) ||
                (mixed.End is not null && !endLiteral && !this.InferAggregateCall(pattern.Components[1], mixed.End, fitLiterals)))
            {
                return false;
            }

            return (!startLiteral ||
                this.InferLiteralBoundary(pattern.Components[0], mixed.Start!, binding.IndependentRangeBoundary(mixed.End, binding.EndType), fitLiterals)) &&
                (!endLiteral ||
                this.InferLiteralBoundary(pattern.Components[1], mixed.End!, binding.IndependentRangeBoundary(mixed.Start, binding.StartType), fitLiterals));
        }

        // An array literal's length binds the function's length slot of a fixed-array pattern; each element is evidence at its Type.
        private readonly bool InferArrayLiteral(BoundType pattern, ArrayLiteralKoto array, bool fitLiterals)
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
                if (!this.InferAggregateCall(pattern.Components[0], array.Elements[i], fitLiterals))
                {
                    return false;
                }
            }

            return true;
        }

        // SPEC 4.6.2: with `fitLiterals`, a literal-only boundary whose S or E no other evidence fixed takes the other boundary's integer
        // Type (rule 2), or its default once DefaultArguments applied it to the selected candidate.
        private readonly bool InferLiteralBoundary(BoundType pattern, Koto boundary, BoundType? other, bool fitLiterals)
            => !fitLiterals || this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null ||
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
                return this.Default(source, binding.LiteralDefault(source), ref applied);
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
            if (binding.FitsInputLiteral(literal, type, this.Scope))
            {
                return null;
            }

            binding.RequireType(literal, this.Scope, type);
            return literal;
        }

        // Whether the call infers a construction's Type from its arguments (SPEC 10.8.1), and its target.
        private readonly bool InfersConstruction(out Koto? target)
        {
            target = !this.FixedConstruction && this.Call.Method is MemberAccessKoto member && this.Binding.inferredConstructorTargets.Contains(member.Left)
                ? member.Left : null;
            return target is not null;
        }

        private readonly Span<BoundOrigin> InputSpan() => this.Inputs.AsSpan(0, Math.Min(this.Inputs.Length, InputOriginCount(this.Function)));

        // The slot F of a parameter `F`, `ref/F` or `uniq/F`; null for any other parameter.
        private readonly BoundType? CallableSlot(int parameter)
        {
            var pattern = this.Function.Parameters[parameter].Type.BoundType!;
            var slot = pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref or SemanticsKind.Uniq, Components.Count: 1 }
                ? pattern.Components[0] : pattern;
            return slot.Kind == BoundTypeKind.Parameter && ContainerSlot(this.SlotOwner, slot.Symbol!) >= 0 ? slot : null;
        }

        // A slot's own validity: a length is Unknown while unbound; a Type slot is checked when bound and Proven when it is unbound only
        // because it is unsolved.
        private readonly ConstraintProof SlotProof(ReadOnlySpan<BoundType?> slots, int slot, SlotSet unbound)
        {
            if (CallOwnSlots(this.Function)[slot] is LengthParameterKoto)
            {
                return this.Lengths[slot] is not null ? ConstraintProof.Proven : ConstraintProof.Unknown;
            }

            if (slots[slot] is { } argument)
            {
                return this.Binding.CheckTypeConstraints(argument, this.Scope);
            }

            return unbound.Contains(slot) ? ConstraintProof.Proven : ConstraintProof.Unknown;
        }

        // Replaces the open regions of each argument whose Callable signature holds one, marking that argument unless `fixedOnly`.
        private readonly bool StandIns(int[] mapping, Span<BoundType?> slots, Span<BoundArgumentOperation> targets, bool fixedOnly)
        {
            var binding = this.Binding;
            var replaced = false;
            for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
            {
                var source = this.Call.ArgumentNodes[i];
                if (this.CallableSlot(mapping[i]) is not { } callableSlot || source.BoundType is null ||
                    !binding.TryCallable(callableSlot, binding.ConstraintScope(this.Function), out var requiredSignature, out _) ||
                    binding.CallMemberPattern(requiredSignature, this.Function, this.DeclaringType) is not { } memberSignature ||
                    this.Type(binding.ContractType(memberSignature, this.Scope, this.Self)) is not { } required ||
                    binding.PerCallStandIn(required, source, required, fixedOnly) is null)
                {
                    continue;
                }

                for (var g = 0; g < slots.Length; g++)
                {
                    if (slots[g] is { } solution)
                    {
                        slots[g] = binding.ReplaceOpenOrigins(solution, source, required, fixedOnly);
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
}
