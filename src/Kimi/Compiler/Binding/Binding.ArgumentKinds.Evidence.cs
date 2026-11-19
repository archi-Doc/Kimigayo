// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // The scratch of a typed position's adapted part Types (SlotSolution.InferAdaptedParts), its one user: nothing reached from AdaptPart
    // (FixedPattern.Of, AdaptInput) runs an evidence walk, so no InferAdaptedParts re-enters between its Add calls and SelectCommonType.
    private readonly List<BoundType> adaptedPartTypes = new();

    // SPEC 10.5.1, 10.3, 15.6.4 step 3 (DECISIONS R4-M164): what a filled slot's position contributes to its selected call, which
    // decides where the call proves the position's instantiated parameter Type well formed (RequireInputPremises). At a Signature
    // position whose pattern is a slot, the slot's solution is its contributor's (the premise visits stop there; DECISIONS BU-99); at an
    // Evidence position the visits go on over the solution, judged where the fit replaced an Origin.
    private enum Contribution : byte
    {
        // An independently typable part, well formed where it was formed (SPEC 15.6.1): judged only where the fit replaced an Origin.
        Evidence,

        // A position whose Type the callee's instantiated parameter Type gives, such as a default: every position is judged.
        Signature,
    }

    // SPEC 10.8 (PLAN G492-a (ii)): an argument part's Type as its call reads it, once per call: its evidence and the operation the
    // selected call forms from it (FitArgument, the completion), so the operation's later readers (BU-99's replaced test, step 5's
    // formation proof, MIR) read the Origins the solve read; a `let` region of the part's body is read as its value (RegionValuesAt).
    private BoundType? ArgumentTypeAt(Koto part) => part.BoundType is { } type ? this.RegionValuesAt(type, part) : null;

    // The contribution of a part by its producer's record (RecordPart), which its completion keeps, unlike KindOf: a typed part is
    // Evidence, and a part recorded as waiting (Case, Call, Function, Reference) or Literal a Signature position. An aggregate (by its
    // form, ClassifyPart) is a Signature position whose child positions contribute each by its parts (G216 (a) U2.2 U2, the child
    // rows, ComposePositionRows; `[]` has none). A control-flow part is Evidence: one position with a Type of its own, read only where
    // its root has no child rows (R4', DECISIONS BU-104); a join whose results are positions is judged by its leaves. A receiver is
    // Evidence; a default is a Signature position.
    private Contribution PartContribution(Koto part)
    {
        var core = KotoHelper.UnwrapParentheses(part);
        return ClassifyPart(core) == PartKind.Aggregate ? Contribution.Signature : this.RecordedPart(core).Kind switch
        {
            PartKind.Typed or PartKind.Aggregate or PartKind.Join => Contribution.Evidence,
            _ => Contribution.Signature,
        };
    }

    // SPEC 10.8, 10.5.1: the evidence walk of a candidate's solution, by each argument part's kind at its position. A typed part is
    // evidence through the common adaptation (SPEC 10.2) and, at a Function pattern, its known call signature; a Never-typed part is
    // none. A Literal part is evidence only through the default applied to the selected candidate (SPEC 10.2.1). A waiting part is none,
    // except the parameter and result Types an anonymous function's header writes (SPEC 10.5.4). A Tuple or array literal is evidence by
    // its parts at their positions, and at a bare slot only when every part is; a mixed range by its range typing (SPEC 4.6.2). The early
    // pass takes typed parts and written headers; the late pass, after the expected result, the defaults and range typing. Each
    // argument's contribution to the call (Contribution) is this walk's fact, written to its root row (PositionRow, Contribute).
    private ref partial struct SlotSolution
    {
        // F1 (R4-M164): each argument's root row after the late evidence pass (SolveLate): its parameter pattern as the candidate reads it
        // (Pattern), the pattern's instantiation by the slots solved so far and its part's contribution (PartContribution), a function of
        // the part and the pattern, so every run of the candidate writes the same contribution (DECISIONS BU-99). A Case construction's is
        // the same rule over its payloads (ContributePayloads).
        internal readonly void Contribute(int[] mapping)
        {
            for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
            {
                var part = this.Call.ArgumentNodes[i];
                var pattern = this.Function.Parameters[mapping[i]].Type.BoundType is { } parameter ? this.Pattern(parameter) : null;
                var type = pattern is null ? null : this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths);
                this.Rows[i] = new(i, -1, 0, part, pattern, type, this.Binding.PartContribution(part));
            }
        }

        // F1 of a Case construction (R4-M164 U4): the call's rule over its payloads, each payload's declared Type its pattern, once its
        // evidence and defaults have solved the slots.
        internal readonly void ContributePayloads(ReadOnlySpan<Koto> payload)
        {
            for (var i = 0; i < payload.Length; i++)
            {
                var part = this.Call.ArgumentNodes[i];
                var type = payload[i].BoundType is { } pattern ? this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) : null;
                this.Rows[i] = new(i, -1, 0, part, payload[i].BoundType, type, this.Binding.PartContribution(part));
            }
        }

        // The Type of a parameter as this candidate reads it: the declaring Type's slots substituted and Self replaced; null when it fails.
        internal readonly BoundType? Pattern(BoundType pattern)
            => this.Binding.CallMemberPattern(pattern, this.Binder, this.DeclaringType) is { } member
                ? this.Binding.ContractType(member, this.Scope, this.Self) : null;

        // SPEC 10.8: one argument part's evidence for the slots of the pattern at its position (Pattern), in the early or the late pass.
        internal bool InferArgument(BoundType pattern, Koto part, bool late)
        {
            var start = this.Binding.OpenPosition(part);
            var inferred = this.InferPosition(pattern, start, late);
            this.Binding.ClosePosition(start);
            return inferred;
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
                        parameterTypes.Components[i], actual, this.Solve, this.Frame, lengths: true, structural: true, evidence: header)))
                {
                    return false;
                }
            }

            return literal.ReturnType is null || (binding.BindType(literal.ReturnType, headerScope) is { } result &&
                binding.Infer(memberSignature.Components[1], result, this.Solve, this.Frame, lengths: true, structural: true, evidence: header));
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
                if (this.Function.Parameters[mapping[i]].Type.BoundType is not { } parameter || this.CallableSlot(parameter) is not { } callableSlot ||
                    binding.KindOf(source) == PartKind.Function || binding.WasDeferred(source) ||
                    this.Binding.ArgumentTypeAt(source) is not { } actual ||
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
                if (!binding.Infer(memberSignature, actualSignature, this.Solve, this.Frame, lengths: true, structural: true, evidence: evidence))
                {
                    var required = this.Type(memberSignature) ?? memberSignature;
                    operations[i] = new(source, actualSignature, required, ArgumentOperationKind.Value, ArgumentAdaptation.Exact);
                    return false;
                }
            }

            return true;
        }

        // SPEC 10.5.1, 10.2.1: before its default applies, a Literal fits a position that holds an unbound slot only by the slot-free
        // structure its default would have; it binds no slot. `met` is the pattern the Literal meets there, whose outer layer decides the
        // operation the position is ranked by (SPEC 10.2.2's Literal row; PLAN G915).
        internal readonly bool FitsLiteralShape(BoundType pattern, BoundType shape, out BoundType? met)
        {
            var binding = this.Binding;
            met = null;
            if (this.Pattern(pattern) is not { } memberPattern)
            {
                return false;
            }

            var trial = binding.infer.Snapshot();
            try
            {
                met = memberPattern;
                return binding.Infer(binding.ExpectedTypeOf(met), shape, this.Solve, this.Frame);
            }
            finally
            {
                binding.infer.Rollback(trial);
            }
        }

        // SPEC 10.2.1, 10.5.1 (the owner's O-341 (a), O-408 (a)): after selection, one step of OwnTypeSteps: each part of `kind` at a
        // position that holds a slot still unsolved takes its own Type (OwnType: a Literal its default, SPEC 12.3, and 4.6.2 rule 3 for a
        // literal-only range boundary; a waiting nested call its resolution), recorded for the candidate's checks to run once more. Each
        // part that its default does not fit is bound at it for its own record (SPEC 23.1.2), and one of them is returned; `applied`
        // tells whether any own Type applied.
        internal readonly Koto? DefaultArguments(int[] mapping, PartKind kind, out bool applied)
        {
            var unsolved = this.Binding.RentSlotSet(CallSlotCount(this.Function));
            try
            {
                this.Binding.UnsolvedSlots(this.Call, this.Function, this.Types, this.Lengths, mapping, ref unsolved);
                applied = false;
                Koto? misfit = null;
                for (var i = 0; i < this.Call.ArgumentNodes.Count; i++)
                {
                    var pattern = this.Function.Parameters[mapping[i]].Type.BoundType!;
                    misfit = this.DefaultPart(this.Call.ArgumentNodes[i], pattern, unsolved, kind, ref applied) ?? misfit;
                }

                return misfit;
            }
            finally
            {
                this.Binding.ReturnSlotSet(unsolved);
            }
        }

        // SPEC 10.2.1, 6.5.2, 10.5.1: a Case construction's step of OwnTypeSteps, after its evidence: each payload's parts of `kind` at
        // positions that hold an unsolved slot take their own Types. Each part that its default does not fit is bound at it for its own
        // record, and one of them is returned.
        internal readonly Koto? DefaultPayloads(ReadOnlySpan<Koto> payload, SlotSet unsolved, PartKind kind)
        {
            var applied = false;
            Koto? misfit = null;
            for (var i = 0; i < payload.Length; i++)
            {
                misfit = this.DefaultPart(this.Call.ArgumentNodes[i], payload[i].BoundType!, unsolved, kind, ref applied) ?? misfit;
            }

            return misfit;
        }

        // SPEC 10.8: the evidence of the parts at a position (Binding.ArgumentKinds.Positions.cs) for the slots of its pattern.
        private bool InferPosition(BoundType pattern, int start, bool late)
        {
            var binding = this.Binding;
            var end = binding.positionParts.Count;
            if (end - start != 1)
            {
                return this.InferParts(pattern, start, end, late);
            }

            var part = binding.positionParts[start];
            var node = KotoHelper.UnwrapParentheses(part);
            var kind = binding.KindOf(node);
            if (kind == PartKind.Function && node is FunctionKoto closure)
            {
                return late || this.InferAnonymous(pattern, part, closure);
            }

            switch (kind)
            {
                case PartKind.Aggregate or PartKind.Join:
                    return this.InferComposite(pattern, start, end, late);
                case PartKind.Literal:
                    return this.InferLiteral(pattern, node, late);
                case PartKind.Typed when binding.DefaultOf(node) is not null:
                    return !late || binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null || this.InferOwnType(pattern, node);
                default:
                    if (this.Binding.ArgumentTypeAt(node) is not { } actual || ReferenceEquals(actual, BoundType.Never))
                    {
                        return true;
                    }

                    // SPEC 14.9.2 step 3: a part that awaits its position is evidence as a Literal's default is, only for slots no other
                    // evidence binds, in the late pass.
                    return binding.AwaitsPosition(node)
                        ? !late || binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null ||
                            this.InferAdapted(pattern, actual, part)
                        : late || this.InferAdapted(pattern, actual, part);
            }
        }

        // An anonymous function: once bound, its Type at any pattern but a Function Type (where its signature meets the pattern after
        // selection); always the Types its header writes, at a pattern with a call signature.
        private bool InferAnonymous(BoundType pattern, Koto part, FunctionKoto closure)
            => (closure.BoundType is not { } actual || pattern.Kind == BoundTypeKind.Function || this.InferAdapted(pattern, actual, part)) &&
                (!this.Binding.TryCallable(pattern, this.Binding.ConstraintScope(this.Binder), out var header, out _) ||
                this.InferClosureHeader(header, closure));

        // SPEC 10.8, 10.2: a Type at the pattern of its position, through the common adaptation.
        private bool InferAdapted(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
            => this.AdaptPart(pattern, actual, source, path, receiver) is { } adapted &&
                this.InferFrom(pattern, adapted, new(source, this.Binding.PartContribution(source), this.Binding.WasDeferred(source)));

        // SPEC 10.2.2 step 1, 10.8, 6.5.2 step 2: one argument part's Type adapted at its pattern under the bindings fixed before any
        // evidence (FixedPattern); a slot that evidence binds stays a slot, so no argument's adaptation follows another's binding. Null
        // where it does not adapt.
        private readonly BoundType? AdaptPart(BoundType pattern, BoundType actual, Koto source, BoundMemberPath? path = null, bool receiver = false)
        {
            var fixedPattern = FixedPattern.Of(this.Binding, pattern, this.Fixed);
            return this.Binding.AdaptInput(
                source, fixedPattern, actual, this.Scope, path, this.DeclaringType, out var adapted, out _, out _, receiver: receiver, deferAcquisition: true)
                ? adapted : null;
        }

        // SPEC 10.5.1, 14.9.2 step 2, 10.2 (DECISIONS BU-43 as amended by G729): a typed position of several parts is one control-flow
        // argument whose sources are its parts. Each typed part (those PositionType takes; a part awaiting its call has no supplied Type,
        // so none is deferred) adapts at the pattern as an argument part does (AdaptPart), and the adapted parts' common Type
        // (SelectCommonType, 14.9.2 step 2) is evidence once (InferFrom): a borrow's Origin is the region covering every part's Loan.
        private bool InferAdaptedParts(BoundType pattern, int start, int end)
        {
            var binding = this.Binding;
            var adapted = binding.adaptedPartTypes;
            adapted.Clear();
            for (var i = start; i < end; i++)
            {
                var part = binding.positionParts[i];
                if (binding.TypedPositionPart(part) is { } actual)
                {
                    if (this.AdaptPart(pattern, actual, part) is not { } type)
                    {
                        adapted.Clear();
                        return false;
                    }

                    adapted.Add(type);
                }
            }

            var common = binding.SelectCommonType(adapted, binding.ConstraintScope(binding.positionParts[start]), out _);
            adapted.Clear();
            return common is not null && this.InferFrom(pattern, common, new(null, Contribution.Evidence, Relations: false));
        }

        // SPEC 10.8: the evidence of an adapted Type at the pattern of its position. `part` carries the facts the adapted part gives: its
        // node, whose known call signature binds a Function pattern (none for a position of several parts), its contribution, and
        // whether it is a nested call checked against this candidate, which supplies relations for checking, never outer slot evidence.
        private bool InferFrom(BoundType pattern, BoundType actual, PartEvidence part)
        {
            var binding = this.Binding;
            var binder = this.Binder;
            var callee = actual;
            actual = binding.SignatureAtFunction(FixedPattern.Of(binding, pattern, this.Fixed).Type, actual);
            pattern = binding.FormedApplication(pattern, this.SlotOwner, this.Types);
            var known = part.Node is { } node && pattern.Kind == BoundTypeKind.Function && actual.Kind == BoundTypeKind.Function
                ? new SignatureEvidence(CalleeBinder(callee, actual), node) : (SignatureEvidence?)null;
            var quantifier = known is { } evidence ? evidence.Binder ?? evidence.Source : null; // The argument itself binds nothing.
            binding.MatchInputOrigins(pattern, actual, binder, this.Origins, this.Inputs, quantifier);
            if (pattern.CarriesOrigin && actual.CarriesOrigin)
            {
                this.OriginInference ??= binding.BeginOriginInference(this.Call, binder);
                binding.CollectOriginInference(pattern, actual, this.OriginInference, known: quantifier);
                if (part.Contribution == Contribution.Evidence)
                {
                    // N2 (U2b): a completed Signature part's premises are SolveLate's, over the instantiated parameter Type, alone.
                    binding.CollectInputPremises(pattern, pattern, this.OriginInference, this.Call);
                }
            }

            pattern = binding.SubstituteStoredOrigins(pattern, binder, this.Origins.AsSpan(0, this.OriginCount), this.InputSpan());
            if (part.Relations)
            {
                return true;
            }

            var structural = pattern.Kind == BoundTypeKind.Function;
            return binding.Infer(
                pattern,
                actual,
                this.Solve,
                this.Frame,
                inferOrigins: true,
                lengths: true,
                commonOrigins: !this.Fixed.Own, // SPEC 10.8: a fixed binding is kept; open slots' covariant bindings meet.
                structural: structural,
                evidence: known,
                relateLater: true);
        }

        // SPEC 10.5.1 (the owner, 2026-10-26; O-408 (a)), 14.9.2: a join's position of several parts is evidence once, by its kind: a
        // Typed one by its typed parts each adapted at the pattern (InferAdaptedParts), in the early pass; a Literal one by its first
        // literal's default in the late pass, as a Call one whose literals took their defaults (they bind first, DefaultParts); an
        // Aggregate one by its child positions; any other Call one and one Never in every source none.
        private bool InferParts(BoundType pattern, int start, int end, bool late)
        {
            var binding = this.Binding;
            switch (binding.PositionKind(start, end, out var type))
            {
                case PartKind.Aggregate:
                    return this.InferComposite(pattern, start, end, late);
                case PartKind.Literal:
                    return this.InferLiteral(pattern, binding.FirstLiteral(start, end) ?? throw new InvalidOperationException("A Literal position holds a literal (InternalInvariant)."), late);
                case PartKind.Call when binding.FirstLiteral(start, end) is { } literal && binding.DefaultOf(literal) is not null:
                    return this.InferLiteral(pattern, literal, late);
                case PartKind.Typed when type is not null && !late:
                    return this.InferAdaptedParts(pattern, start, end);
                default:
                    return true;
            }
        }

        // SPEC 10.2.1: a Literal is evidence only through its default, applied to the selected candidate, for slots no other evidence binds;
        // at a fixed `ref/U` it is a borrowed temporary (InferTemporary).
        private bool InferLiteral(BoundType pattern, Koto literal, bool late)
        {
            if (!late)
            {
                return true;
            }

            if (this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is { } fixedType)
            {
                if (this.Binding.Materialization(literal, fixedType, this.Scope) is not null)
                {
                    this.InferTemporary(pattern, literal);
                }

                return true;
            }

            return this.InferOwnType(pattern, literal);
        }

        // SPEC 10.2.1 (O-341 (a)): a part's own Type after selection (DefaultOf: a literal's default, a nested call's resolution) is
        // evidence, in the late pass, for slots no other evidence binds; a nested call's is no relation-only input then.
        private bool InferOwnType(BoundType pattern, Koto part)
            => this.Binding.DefaultOf(part) is not { } own ||
                (this.AdaptPart(pattern, own, part) is { } adapted && this.InferFrom(pattern, adapted, new(part, this.Binding.PartContribution(part), false)));

        // SPEC 10.2 row 2, 15.3.6: a temporary borrowed at `ref/U during a` (a Literal fitted at U, a Tuple or array literal or a mixed range)
        // supplies its Place Origin as an input of `a`, at the root and at every nested position: matched (MatchInputOrigin) and a bound of
        // the call's Origin solve, as a typed input's Origin is (InferAdapted).
        private void InferTemporary(BoundType pattern, Koto part)
        {
            if (pattern.Origin is not { } origin)
            {
                return;
            }

            var binding = this.Binding;
            var place = binding.PlaceOrigin(part);
            binding.MatchInputOrigin(origin, place, this.Binder, this.Origins, this.Inputs);
            this.OriginInference ??= binding.BeginOriginInference(this.Call, this.Binder);
            AddInputBound(this.OriginInference, origin, place);
        }

        // SPEC 10.8, 4.3.1, 4.6.2: a Tuple or array literal or a mixed range, part by part at their positions; at a bare slot the
        // whole, recorded for WholeEvidence (BindWholes). A composite at `ref/T` borrows its temporary, whose Origin binds the input's.
        // A join its call classifies, and a join position of aggregates, go by their child positions likewise (SPEC 10.5.1, 14.9.2). A
        // position of several parts holds a join's sources: its value is the join's (PLAN G924 rule B, R8), so its temporary is the
        // join's, the outermost result target of its parts, never a source's.
        private bool InferComposite(BoundType pattern, int start, int end, bool late)
        {
            var binding = this.Binding;
            var part = binding.positionParts[start];
            var owner = part;
            while (end - start != 1 && ResultTarget(owner) is { } target)
            {
                owner = target;
            }

            if (binding.Materialization(owner, pattern, this.Scope) is { } temporary)
            {
                this.InferTemporary(pattern, owner);
                pattern = temporary;
            }

            var source = KotoHelper.UnwrapParentheses(part);

            if (source is RangeKoto mixed && IsMixedRange(mixed) && pattern.Kind != BoundTypeKind.Parameter)
            {
                return this.InferMixedRange(pattern, mixed, late);
            }

            if (pattern.Kind == BoundTypeKind.Parameter)
            {
                // SPEC 10.5.1, 10.8 Solving (WholeEvidence): a whole at a slot no fixed binding solves is gathered with the slot's
                // other wholes and evidence and bound once, at the pass's end (BindWholes); an earlier argument's binding never shapes it.
                // SPEC 4.3.1 Places, 10.8: in the late pass only a slot that steps 3 and 4 left unbound (Left) takes its literals
                // whole; a slot they bound gives the parts their expected Type, as a fixed binding does.
                var fixedType = FixedPattern.Of(binding, pattern, this.Fixed).Type;
                var solved = ReferenceEquals(fixedType, pattern) ? binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) : fixedType;
                if (!ReferenceEquals(solved, pattern) && ReferenceEquals(fixedType, pattern) &&
                    (solved is null || !late || this.Left.Contains(OwnSlot(pattern, this.SlotOwner))))
                {
                    for (var i = start; i < end; i++)
                    {
                        binding.wholes.Add((pattern, binding.positionParts[i]));
                    }

                    return true;
                }

                pattern = solved!;
            }

            if (source is ArrayLiteralKoto array && !this.InferArrayLength(pattern, array))
            {
                return false;
            }

            for (var i = 0; i < binding.ChildPositions(start, end); i++)
            {
                var child = binding.positionParts.Count;
                var element = binding.AddChildPosition(start, end, i, pattern);
                var inferred = element is null || this.InferPosition(element, child, late);
                binding.ClosePosition(child);
                if (element is null || !inferred)
                {
                    return inferred; // A shape mismatch is no evidence: probing reports it once the target is fixed.
                }
            }

            return true;
        }

        // An array literal's length binds the function's length slot of a fixed-array pattern.
        private readonly bool InferArrayLength(BoundType pattern, ArrayLiteralKoto array)
        {
            if (pattern.Kind != BoundTypeKind.FixedArray || pattern.LengthExpression is not { Parameter: { } parameter } ||
                !ReferenceEquals(parameter.Scope.Owner, this.Binder))
            {
                return true;
            }

            var length = array.FillLength is null ? this.Binding.InternLength(LengthOperation.Constant, array.Elements.Count) : array.FillCount;
            if (length is null || (this.Lengths[parameter.Slot] is { } previous && !ReferenceEquals(previous, length)))
            {
                return false;
            }

            this.Frame.SetLength(parameter.Slot, length);
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
            if (this.Binding.ArgumentTypeAt(source) is not { } actual || ReferenceEquals(actual, BoundType.Never))
            {
                return true; // SPEC 10.8: a Never-typed boundary is no evidence.
            }

            actual = KotoHelper.UnwrapParentheses(source.Parent!) is RangeKoto && binding.ReadTypeReferent(actual, source) is { } read ? read : actual;
            binding.MatchInputOrigins(pattern, actual, this.Binder, this.Origins, this.Inputs);
            pattern = binding.SubstituteStoredOrigins(pattern, this.Binder, this.Origins.AsSpan(0, this.OriginCount), this.InputSpan());
            return binding.Infer(pattern, actual, this.Solve, this.Frame, inferOrigins: true, lengths: true);
        }

        // SPEC 4.6.2: in the late pass, a literal-only boundary whose S or E no other evidence fixed takes the other boundary's integer
        // Type (rule 2), or its default once DefaultArguments applied it to the selected candidate.
        private readonly bool InferLiteralBoundary(BoundType pattern, Koto boundary, BoundType? other, bool late)
            => !late || this.Binding.SubstituteType(pattern, this.SlotOwner, this.Types, this.Lengths) is not null ||
                (this.Binding.DefaultOf(boundary) ?? this.Binding.RangeTyping(boundary, other)) is not { } type ||
                this.Binding.Infer(pattern, type, this.Solve, this.Frame, lengths: true);

        // The own Types of a part's leaves of `kind` at the positions that hold an unsolved slot: the part itself, the elements of a
        // Tuple or array literal (a whole a slot takes included: its place is a hole until every part has one, BindWholes) and the
        // literal-only boundary of a mixed range.
        private readonly Koto? DefaultPart(Koto part, BoundType pattern, SlotSet unsolved, PartKind kind, ref bool applied)
        {
            var start = this.Binding.OpenPosition(part);
            var misfit = this.DefaultPosition(start, pattern, unsolved, kind, ref applied);
            this.Binding.ClosePosition(start);
            return misfit;
        }

        // `kind` is OwnTypeSteps' row, a filter of the parts that take their own Types now; the rows are a closed order of two (a
        // literal's default, then a waiting nested call's own Type; O-408 (a)), so the single-part case below names both.
        private readonly Koto? DefaultPosition(int start, BoundType pattern, SlotSet unsolved, PartKind kind, ref bool applied)
        {
            var binding = this.Binding;
            var end = binding.positionParts.Count;
            if (!MentionsSlots(pattern, this.SlotOwner, unsolved))
            {
                return null;
            }

            if (end - start != 1)
            {
                return this.DefaultParts(start, end, pattern, unsolved, kind, ref applied);
            }

            var source = KotoHelper.UnwrapParentheses(binding.positionParts[start]);
            var own = binding.KindOf(source);
            if (own is PartKind.Literal or PartKind.Call)
            {
                return own == kind ? this.Default(source, binding.OwnType(source, this.Scope), ref applied) : null;
            }

            if (source is RangeKoto range && IsMixedRange(range))
            {
                // SPEC 4.6.2: a literal-only boundary is a Literal part; a waiting boundary takes no own Type.
                if (kind != PartKind.Literal)
                {
                    return null;
                }

                var ranged = pattern is { Kind: BoundTypeKind.Constructed, Components.Count: 2 };
                var misfit = this.DefaultBoundary(range.Start, range.End, ranged ? pattern.Components[0] : pattern, binding.EndType, unsolved, ref applied);
                return this.DefaultBoundary(
                    range.End, range.Start, ranged ? pattern.Components[1] : pattern, binding.StartType, unsolved, ref applied) ?? misfit;
            }

            return this.DefaultChildren(start, end, pattern, unsolved, kind, ref applied);
        }

        // SPEC 10.5.1, 10.2.1 (the owner, 2026-10-26; O-341 (a), O-408 (a)): one join rule for literals: in the literal step a position's
        // literals take its first literal's default, which each must fit (its own record otherwise), at a Literal position (its Never
        // parts with them, SPEC 14.9.2 step 3) and at one that waits for its call beside its literals alike; in the call step each
        // waiting nested call at a waiting position takes its own Type (OwnType); an Aggregate position goes by its child positions; the
        // others take none.
        private readonly Koto? DefaultParts(int start, int end, BoundType pattern, SlotSet unsolved, PartKind kind, ref bool applied)
        {
            var binding = this.Binding;
            Koto? misfit = null;
            var position = binding.PositionKind(start, end, out _);
            switch (position)
            {
                case PartKind.Literal or PartKind.Call when kind == PartKind.Literal:
                    if (binding.FirstLiteral(start, end) is not { } first)
                    {
                        return position == PartKind.Call ? null : throw new InvalidOperationException("A Literal position holds a literal (InternalInvariant).");
                    }

                    var type = binding.PartDefault(first, this.Scope);
                    for (var i = start; i < end; i++)
                    {
                        if (position == PartKind.Literal || binding.KindOf(binding.positionParts[i]) == PartKind.Literal)
                        {
                            misfit = this.Default(binding.positionParts[i], type, ref applied) ?? misfit;
                        }
                    }

                    return misfit;
                case PartKind.Aggregate:
                    return this.DefaultChildren(start, end, pattern, unsolved, kind, ref applied);
                case PartKind.Call:
                    for (var i = start; i < end; i++)
                    {
                        if (binding.KindOf(binding.positionParts[i]) == PartKind.Call)
                        {
                            misfit = this.Default(binding.positionParts[i], binding.OwnType(binding.positionParts[i], this.Scope), ref applied) ?? misfit;
                        }
                    }

                    return misfit;
                default:
                    return null;
            }
        }

        private readonly Koto? DefaultChildren(int start, int end, BoundType pattern, SlotSet unsolved, PartKind kind, ref bool applied)
        {
            var binding = this.Binding;
            Koto? misfit = null;
            for (var i = 0; i < binding.ChildPositions(start, end); i++)
            {
                var child = binding.positionParts.Count;
                var element = binding.AddChildPosition(start, end, i, pattern) ?? pattern;
                misfit = this.DefaultPosition(child, element, unsolved, kind, ref applied) ?? misfit;
                binding.ClosePosition(child);
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

        // SPEC 10.2.1 (O-341 (a)): records the own Type a part takes after selection (OwnType) in its Default column, keeping its kind (a
        // Literal does not await its call: RecordPart's row). A literal its default does not fit is bound at it for its own record, on
        // which the call rests; a nested call is already resolved. A part without an own Type takes none: one whose own binding failed
        // (a failed resolution, a failed name beside a waiting part) keeps its own record and is no misfit, the slots at its position
        // resting on it (SPEC 23.1.2; FailUnboundSlots, RestSlots).
        private readonly Koto? Default(Koto part, BoundType? type, ref bool applied)
        {
            var binding = this.Binding;
            if (type is null)
            {
                return null;
            }

            var kind = binding.KindOf(part) == PartKind.Literal ? PartKind.Literal : PartKind.Call;
            ref var record = ref binding.RecordPart(part, kind);
            record = record with { Default = type };
            applied = true;
            if (kind == PartKind.Call || binding.FitsLiteralPart(part, type, this.Scope))
            {
                return null;
            }

            binding.RequireType(part, this.Scope, type);
            return part;
        }
    }

    // SPEC 7.7.4, 10.5: a Function Item or a concrete Closure meets a common Function Type pattern through its call signature, as
    // evidence; the conversion itself is judged by applicability and once the call is selected (ErasureSignatureFits). Any other Type,
    // or any Type at another pattern, as it is.
    private BoundType SignatureAtFunction(BoundType pattern, BoundType actual)
        => pattern.Kind != BoundTypeKind.Function ? actual
            : this.FunctionItemSignature(actual) ?? BoundClosure.Of(actual)?.Signature ?? actual;

    // What an adapted Type's evidence reads of the part it comes from (SlotSolution.InferFrom): the node whose known call signature binds
    // a Function pattern (none for a position of several parts), its contribution (Contribution), and whether it only supplies relations
    // (a nested call checked against the candidate, WasDeferred).
    private readonly record struct PartEvidence(Koto? Node, Contribution Contribution, bool Relations);
}
