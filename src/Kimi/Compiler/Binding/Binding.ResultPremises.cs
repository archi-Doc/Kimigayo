// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 15.3.7, 15.6.1, 15.6.4 step 3: a signature's intrinsic well-formedness (every Origin stored below a borrow layer outlives that
// layer's Origin, and for an input its Types' own clauses hold) is a premise of its definition and an obligation at each use. For the
// result, a relation that the callee's inputs and declared relations already prove needs no premise; only the others, which the
// definition would otherwise fail, are carried to the uses: each call solves its Origins with them and proves them for its substituted
// result, and a function reference or a conformance proves them from the required contract. Every input relation is a premise, so each
// is carried: each call shortens its solution with them and judges them at each filled slot by what its position contributes
// (Contribution, the evidence walk's fact: Evidence where its instantiation replaced an Origin, a Signature position at every
// position but inside a slot's solution, which its contributor proves), and a whole-contract comparison proves them where its solve
// replaced a required Origin. One visitor serves both.
public sealed partial class Binding
{
    // The pattern parts one well-formedness visit met, and those one borrow layer's stored visit met, each beside its argument part, with
    // the substituted part it last met that pair with (VisitWellFormedPremises; DECISIONS BU-55, R4-M164 U1e).
    private readonly TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> wellFormedParts = new();
    private readonly TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> storedParts = new();

    // The callee whose result premise is set aside while a use asks whether its definition proves a relation from its other premises.
    private FunctionKoto? resultPremiseExcluded;

    // The first premise an Instance or Prove visit fails (longer, shorter).
    private (BoundOrigin Longer, BoundOrigin Shorter)? failedResultPremise;

    // The failure of the last Item solve that did not hold (ContractFits; ItemContractFits reads it), and the declared clause a solve's
    // CheckCallOriginRelations did not prove, with its substituted ends.
    private ComparisonFailure contractFailure;
    private (BoundOrigin Longer, BoundOrigin Shorter, bool Equality, Koto Syntax)? failedCallClause;

    private enum WellFormedAction : byte
    {
        // Whether any relation is carried to the uses at all.
        Detect,

        // Judged under a Callable comparison's instantiation, for the record of a failed conversion.
        Instance,

        // An obligation of the selected call for the substituted result, judged under SPEC 15.6.5 as a Type occurrence, or for an
        // instantiated input, judged at that input.
        Require,

        // Proven at the use from its premises alone (a whole-contract comparison).
        Prove,

        // A relation of the call's Origin inference, so the call's Origins satisfy it where any solution does.
        Bound,

        // Removed from the call's Origin inference again.
        Unbound,
    }

    // Whose well-formedness a visit reads: an input's, which no other premise of the definition replaces (its Types' own clauses
    // included), or a named function's result, which its definition may prove without the premise.
    private enum WellFormedSubject : byte
    {
        Input,
        NamedResult,
    }

    // The subject, with the named function for NamedResult; the use; and for Bound and Unbound the call's inference and, for a result,
    // the receiver Type whose arguments a member's container Origins project to (an input's pattern is already projected where the call
    // collects it).
    private readonly record struct WellFormedVisit(
        WellFormedSubject Subject, FunctionKoto? Named, Koto Use, WellFormedAction Action, OriginInference? Inference = null, BoundType? Owner = null)
    {
        // For Instance and Prove, the Callable comparison the visit judges under, whose premises its proofs assume.
        internal CallableInstance Comparison { get; init; }

        // The stamp of the walk's query: of wellFormedParts, or of storedParts below a borrow layer.
        internal uint Query { get; init; }

        // Below a borrow layer, the Semantics cases in which that layer exists (SPEC 15.6.1, 15.6.5): a pair layer's borrow cases at the
        // use (WellFormedLayer), 0 for a layer of every case.
        internal ulong Condition { get; init; }
    }

    // SPEC 15.3.7, 10.7: instantiate all of an Item's call Origins together, including named and result-only universals, against the
    // required contract. Repeated invariant occurrences and declared conditions constrain the same solution. An open evidence region
    // still uses the known-signature comparison until other evidence fixes it (SPEC 10.8).
    internal bool ItemContractFits(BoundType item, BoundType signature, BoundType required, Koto use, out ComparisonFailure failure)
    {
        failure = default;
        if (item.Kind != BoundTypeKind.FunctionItem || item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return false;
        }

        signature = this.ContractType(signature, this.ConstraintScope(use));
        if (function.Origins.Count == 0 || HasOpenOrigin(required, evidenceOnly: true))
        {
            if (!this.CallableSubtype(signature, required, SignatureOwner(item), out failure))
            {
                return false;
            }

            if (!this.HasOriginConditions(function))
            {
                return true;
            }
        }

        if (required is not { Kind: BoundTypeKind.Function, Components.Count: 2 } ||
            (ReferenceEquals(required.Components[0], BoundType.Unit) ? 0 : required.Components[0].Components.Count) != function.Parameters.Count)
        {
            return false;
        }

        var origins = this.originScratch.Rent(function.Origins.Count);
        var inputCount = InputOriginCount(function);
        var inputs = this.originScratch.Rent(inputCount);
        Array.Clear(origins, 0, function.Origins.Count);
        Array.Clear(inputs, 0, inputCount);
        try
        {
            var fits = this.ContractFits(use, symbol, function, required, null, item, null, default, origins, inputs);
            failure = fits ? default : this.contractFailure;
            return fits;
        }
        finally
        {
            this.originScratch.Return(inputs, clearArray: true);
            this.originScratch.Return(origins, clearArray: true);
        }
    }

    // With `discover`, a signature Origin that no bound mentions yet is known too, and becomes a variable (BoundOrigins).
    private static bool KnownToInference(OriginInference inference, BoundOrigin origin, bool discover = true)
    {
        if (origin is { Kind: OriginKind.Input, Binder: FunctionTypeKoto })
        {
            return false; // A nested Function Type's per-call input is bound by that Type, never by this call (SPEC 15.3.4).
        }

        if (origin is { Kind: OriginKind.Parameter, Slot: < 0 })
        {
            return false; // A pair's outer Origin `o` is bound by its Type argument, never solved by a call (SPEC 8.1.1).
        }

        if (ReferenceEquals(origin.Binder, inference.Binder) && origin.Kind is OriginKind.Parameter or OriginKind.Input)
        {
            if (origin.Kind == OriginKind.Parameter && discover)
            {
                return true; // A signature Origin, also a result-only one, is solved as under a declared relation (Discover).
            }

            for (var i = 0; i < inference.Variables.Count; i++)
            {
                if (ReferenceEquals(inference.Variables[i].Origin, origin))
                {
                    return true;
                }
            }

            return false;
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (!KnownToInference(inference, origin.Operands[i], discover))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CarriesResultPremises(FunctionKoto function)
        => !function.IsConstructor && !function.IsAnonymous && function.BoundSymbol?.Type is { StoresOrigin: true };

    // As SolveOriginInference does for a declared relation, a result premise's relation is a bound, and a signature Origin becomes a
    // variable, also a result-only one. An input's relation shortens only Origins that the call already solves (OriginInference.WellFormed).
    // A relation over an Origin that this inference does not solve is no bound.
    private static void BoundOrigins(OriginInference inference, BoundOrigin longer, BoundOrigin shorter, bool result)
    {
        if (OriginOutlives(longer, shorter) || !KnownToInference(inference, longer, result) || !KnownToInference(inference, shorter, result))
        {
            return;
        }

        if (!result)
        {
            if (!inference.WellFormed.Contains((longer, shorter)))
            {
                inference.WellFormed.Add((longer, shorter));
            }

            return;
        }

        inference.Discover(longer, 0);
        inference.Discover(shorter, 0);
        inference.Add(longer, shorter);
    }

    private static bool SameLayer(BoundType actual, BoundType result)
        => actual.Kind == result.Kind && actual.Components.Count == result.Components.Count && actual.OriginArguments.Count == result.OriginArguments.Count;

    // Whether a query of VisitWellFormedPremises meets a pattern part beside these parts for the first time; a leaf, or a part that
    // stores no Origin, is cheaper to visit again than to record.
    private static bool FirstMet(TypeMemo<(BoundType Pattern, BoundType? Argument), BoundType> parts, BoundType pattern, BoundType result, BoundType? actual, uint query)
    {
        if (pattern.Components.Count == 0 || !pattern.StoresOrigin)
        {
            return true;
        }

        if (parts.TryGet((pattern, actual), query, out var met) && ReferenceEquals(met, result))
        {
            return false;
        }

        parts.Set((pattern, actual), query, result);
        return true;
    }

    // SPEC 15.6.4 steps 1-4: a call's fresh Origins satisfy its callee's result premises where a solution does, so a result over
    // body-local borrows is bounded by every Origin it holds. Otherwise the call is solved without them and the obligation at the
    // selected call reports the relation; a premise never decides applicability (SPEC 15.6.1). With `select`, candidate applicability,
    // the solve without them judges no bound either (SolveOriginInference).
    private bool SolveCallOriginInference(FunctionKoto function, OriginInference inference, BoundOrigin[] origins, BoundOrigin[] inputs, BoundType? declaringType, bool select = false)
    {
        var use = inference.Use;
        if (!CarriesResultPremises(function))
        {
            return this.SolveOriginInference(inference, origins, inputs, declaringType, select);
        }

        var result = function.BoundSymbol!.Type!;
        var unbound = new WellFormedVisit(WellFormedSubject.NamedResult, function, use, WellFormedAction.Unbound, inference, declaringType);
        this.VisitWellFormedPremises(result, result, null, unbound); // Left by an earlier pass.
        var count = inference.Bounds.Count;
        var variables = inference.Variables.Count;
        this.VisitWellFormedPremises(result, result, null, new(WellFormedSubject.NamedResult, function, use, WellFormedAction.Bound, inference, declaringType));
        if (inference.Bounds.Count != count)
        {
            if (this.SolveOriginInference(inference, origins, inputs, declaringType))
            {
                return true;
            }

            this.VisitWellFormedPremises(result, result, null, unbound);
            inference.Variables.RemoveRange(variables, inference.Variables.Count - variables); // The result-only Origins they discovered.
        }

        return this.SolveOriginInference(inference, origins, inputs, declaringType, select);
    }

    // SPEC 15.3.7: the selected call's obligations for its substituted result.
    private void RequireResultPremises(FunctionKoto function, BoundType result, Koto use)
    {
        if (CarriesResultPremises(function))
        {
            var visit = new WellFormedVisit(WellFormedSubject.NamedResult, function, use, WellFormedAction.Require);
            this.VisitWellFormedPremises(function.BoundSymbol!.Type!, result, null, visit);
        }
    }

    // Whether a position with a Type of its own names the callee's Origin parameter `slot` in its pattern: an Evidence leaf row's, an
    // Evidence root's without child rows, or the receiver's (DiscoverPremiseOrigins; R4', DECISIONS BU-104). A root with child rows is
    // read only through its leaves.
    private bool EvidenceNames(in SlotSolution solution, int slot)
    {
        for (var i = 0; i < solution.Rows.Length; i++)
        {
            this.ChildGroup(solution, i, out var start, out var end);
            this.evidenceNameReads += 1 + end - start;
            if (start == end && solution.Rows[i] is { Contribution: Contribution.Evidence, Pattern: { } pattern } &&
                MentionsOriginSlot(pattern, solution.Binder, slot))
            {
                return true;
            }

            for (var j = start; j < end; j++)
            {
                if (this.IsEvidenceLeaf(solution, j, end) && this.childRows[j].Pattern is { } leaf && MentionsOriginSlot(leaf, solution.Binder, slot))
                {
                    return true;
                }
            }
        }

        return solution.Binder is FunctionKoto { BoundSymbol.ReceiverIndex: >= 0 and var receiver } function &&
            function.Parameters[receiver].Type.BoundType is { } receiverPattern && MentionsOriginSlot(receiverPattern, solution.Binder, slot);
    }

    // R2' (SPEC 15.3.6; DECISIONS BU-99): a position that gives no evidence, a Signature leaf row or a default the call uses, makes each
    // Origin of the callee that its instantiated Type `type` names and no evidence position's pattern names (EvidenceNames) a variable of
    // the solve, published as premise-solved (OriginInference.PremiseSolved): its premise then bounds it from above (SolveOriginInference's
    // WellFormed step). An Origin that evidence names, also one it fixes after this solve, keeps the step's own rule.
    private void DiscoverPremiseOrigins(in SlotSolution solution, OriginInference inference, BoundType type)
    {
        if (!type.CarriesOrigin)
        {
            return;
        }

        DiscoverPremiseOrigin(solution, inference, type.Origin);
        for (var i = 0; i < type.OriginArguments.Count; i++)
        {
            DiscoverPremiseOrigin(solution, inference, type.OriginArguments[i]);
        }

        for (var i = 0; i < type.Components.Count; i++)
        {
            this.DiscoverPremiseOrigins(solution, inference, type.Components[i]);
        }

        static void DiscoverPremiseOrigin(in SlotSolution solution, OriginInference inference, BoundOrigin? origin)
        {
            if (origin is null)
            {
                return;
            }

            for (var i = 0; i < origin.Operands.Count; i++)
            {
                DiscoverPremiseOrigin(solution, inference, origin.Operands[i]);
            }

            if (origin.Kind == OriginKind.Parameter && ReferenceEquals(origin.Binder, inference.Binder) && !inference.PremiseSolved.Contains(origin) &&
                !solution.EvidenceNamed.Contains(origin.Slot))
            {
                inference.Discover(origin, 0);
                inference.PremiseSolved.Add(origin);
            }
        }
    }

    // SPEC 15.3.7, 15.6.4 step 3: an input's well-formedness, its parameter pattern beside the Type `instantiated` that a call collects
    // for that input, shortens the call's solution where the arguments alone would leave an instantiated input ill formed
    // (SolveOriginInference). Only a call collects it: a whole-contract comparison proves it instead (ProvesInputPremises), since the
    // required contract's input premises are no premises at its use.
    private void CollectInputPremises(BoundType pattern, BoundType instantiated, OriginInference inference, Koto use)
    {
        if (instantiated.StoresOrigin)
        {
            this.VisitWellFormedPremises(pattern, instantiated, null, new(WellFormedSubject.Input, null, use, WellFormedAction.Bound, inference));
        }
    }

    // SPEC 15.3.7, 15.6.4 step 3: the selected call's obligations for the instantiated parameter Type of a slot it fills (SPEC 10.1 step 2),
    // judged at `at` by what the slot's position contributes (Contribution). Evidence, a written argument's or the receiver's own Type
    // `supplied`, is well formed, so only a position that the fit replaced, such as an Origin shared with another input, can fail. A
    // Signature position has every position of its Type judged: a default supplies no Type (SPEC 10.1.1) and is judged at the call (SPEC
    // 7.2.3). The visit walks the parameter's pattern `pattern` beside `parameter`: at a position that supplies no evidence it stops at a
    // slot of the pattern, whose solution its contributor proves, and at an Evidence position it goes on over the solution, judged where
    // the fit replaced an Origin (SPEC 10.3, 15.6.1; DECISIONS BU-99); a layer around the slot is this call's. As for the fit, an
    // argument that carries no Origin, such as one that transfers control, supplies none; and an input that already failed, its fit or
    // its conversion's contract (a Function value's, FailsFunctionConversion, whose relations are published with it, FollowsContract), is
    // no instance of its parameter, so its well-formedness, read from its supplied Type, would only restate that failure.
    private void RequireInputPremises(Koto at, BoundType pattern, BoundType parameter, BoundType? supplied, Contribution contribution)
    {
        if (parameter.StoresOrigin &&
            (supplied is null || (supplied.StoresOrigin && at.BindingFailure == BindingFailure.None && !this.HasCallRelation(at))))
        {
            var actual = contribution == Contribution.Evidence ? supplied : null;
            this.VisitWellFormedPremises(pattern, parameter, actual, new(WellFormedSubject.Input, null, at, WellFormedAction.Require));
        }
    }

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's instantiated input well formed from the premises at `use`
    // wherever its solve replaced an Origin of the required input, whose own well-formedness covers every position it kept.
    private bool ProvesInputPremises(BoundType implementation, BoundType required, Koto use, in CallableInstance comparison)
        => ReferenceEquals(implementation, required) || !implementation.StoresOrigin ||
            this.VisitWellFormedPremises(
                implementation, implementation, required, new(WellFormedSubject.Input, null, use, WellFormedAction.Prove) { Comparison = comparison });

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's result premises for its substituted result from the
    // required contract's premises at `use`.
    private bool ProvesResultPremises(FunctionKoto function, BoundType result, Koto use, in CallableInstance comparison)
        => !CarriesResultPremises(function) ||
            this.VisitWellFormedPremises(
                function.BoundSymbol!.Type!,
                result,
                null,
                new(WellFormedSubject.NamedResult, function, use, WellFormedAction.Prove) { Comparison = comparison });

    // SPEC 15.3.7, 15.6.4 step 3: whether a use of a named function must prove Origin conditions beyond its inputs' Types: a declared
    // relation, or a result premise that its inputs and clauses do not prove.
    private bool HasOriginConditions(FunctionKoto function)
        => this.originDeclarations.GetValueOrDefault(function) is { Relations.Length: > 0 } ||
            (CarriesResultPremises(function) && !this.VisitWellFormedPremises(
                function.BoundSymbol!.Type!,
                function.BoundSymbol!.Type!,
                null,
                new(WellFormedSubject.NamedResult, function, function, WellFormedAction.Detect)));

    // SPEC 15.3.7, the owner's (a): the first part of the implementation's own well-formedness, its n-th input's or its result's, that the
    // required signatures' own well-formedness and the premises at `use` do not prove under the instantiation (null when every part
    // holds), with the failing relation.
    private (int Part, BoundOrigin Longer, BoundOrigin Shorter)? FailedSignatureWellFormedness(BoundType actual, in CallableInstance instance, Koto use)
    {
        try
        {
            var inputs = actual.Components[0];
            var visit = new WellFormedVisit(WellFormedSubject.Input, null, use, WellFormedAction.Instance) { Comparison = instance };
            for (var i = 0; i <= inputs.Components.Count; i++)
            {
                var part = i < inputs.Components.Count ? inputs.Components[i] : actual.Components[1];
                this.failedResultPremise = null;
                if (!this.VisitWellFormedPremises(part, part, null, visit) && this.failedResultPremise is { } failed)
                {
                    return (i, failed.Longer, failed.Shorter);
                }
            }

            return null;
        }
        finally
        {
            this.failedResultPremise = null;
        }
    }

    // The failure of a premise a Prove visit did not prove, under `comparison` (ContractFits).
    private ComparisonFailure PremiseFailure(ComparisonFailureKind kind, in CallableInstance comparison)
        => this.failedResultPremise is { } failed ? new(kind, failed.Longer, failed.Shorter, false, comparison) : default;

    private OriginContractFact ConditionFact(Koto at, string member, BoundOrigin longer, BoundOrigin shorter, bool equality, in CallableInstance instance, Koto use)
    {
        if (!equality && longer.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < longer.Operands.Count; i++)
            {
                if (!InstanceOutlives(longer.Operands[i], shorter, instance, this, use))
                {
                    longer = longer.Operands[i];
                    break;
                }
            }
        }

        var a = this.Instantiated(longer, instance);
        var b = this.Instantiated(shorter, instance);
        return new(at, member, a, b, equality);
    }

    // An Origin as the comparison sees it: every per-call input of the implementation replaced by the required input at its position.
    private BoundOrigin Instantiated(BoundOrigin origin, in CallableInstance instance)
    {
        if (origin.Kind != OriginKind.Intersection)
        {
            return instance.Instantiate(origin);
        }

        var meet = BoundOrigin.Static;
        for (var i = 0; i < origin.Operands.Count; i++)
        {
            meet = this.Meet(meet, this.Instantiated(origin.Operands[i], instance));
        }

        return meet;
    }

    // SPEC 10.2 row 2, 15.3.6 (PLAN G585): a part that waits at `ref/U during a` for a value call's solve is a borrowed owner temporary,
    // of U (TemporaryTarget) or of its own Type (a fill, a mixed range), at a root or nested row: its Place Origin is an input bound of
    // `a`, by the key of a named call's evidence walk (InferComposite's Aggregate part, InferLiteral: InferTemporary). A part of Join
    // kind at `ref/(...)`, which InferComposite borrows too, is PLAN G729's exception and no key here: whether a value call takes it
    // is the question of a temporary carried out of its arm.
    private void AddTemporaryBound(Koto part, BoundType pattern, OriginInference inference, BindingScope scope)
    {
        if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Origin: { } origin } &&
            (this.KindOf(part) == PartKind.Aggregate || this.TemporaryTarget(part, pattern, scope) is not null))
        {
            AddInputBound(inference, origin, this.PlaceOrigin(part));
        }
    }

    // A value call instantiates the same nested input/signature Origins and result premises as an ordinary call.
    // The selected contract supplies structure; Origin relations are checked after solving, at their supplying values. A waiting part,
    // which has no actual yet, and a failed one (rule W, DECISIONS BU-79) are no evidence (SPEC 10.5.1). An argument is read by its rows
    // (R3, DECISIONS BU-104; PLAN G504): a root without child rows by its completed Type; a Tuple or array literal or a join its call
    // classifies, which waits, by each Evidence leaf whose part does not wait for the solve, adapted at the leaf's pattern as the named
    // call's evidence walk adapts a typed part (InferAdapted; SPEC 10.5.1, 14.9.2). A selection's solve judges no Origin relation and
    // always holds (SPEC 15.6.1; SolveOriginInference with `select`).
    private void SolveValueCallOrigins(
        Koto binder,
        BoundType parameters,
        ReadOnlySpan<BoundType> actuals,
        BoundOrigin[] origins,
        BoundOrigin[] inputs,
        Koto use,
        in SlotSolution solution,
        BindingScope scope)
    {
        var inference = this.BeginOriginInference(use, binder);
        for (var i = 0; i < actuals.Length; i++)
        {
            this.ChildGroup(solution, i, out var start, out var end);
            if (start == end && actuals[i] is { } actual)
            {
                this.CollectOriginInference(parameters.Components[i], actual, inference);
            }
            else if (solution.Rows[i].Part is { } root)
            {
                this.AddTemporaryBound(root, parameters.Components[i], inference, scope);
            }

            for (var j = start; j < end; j++)
            {
                if (this.IsEvidenceLeaf(solution, j, end) && this.childRows[j] is { Pattern: { } pattern, Part: var part } &&
                    KotoHelper.UnwrapParentheses(part).BoundType is { } typed && !ReferenceEquals(typed, BoundType.Never) &&
                    !this.WaitsForValueCallSolve(part, pattern) &&
                    this.AdaptInput(part, FixedPattern.Of(this, pattern, solution.Fixed), this.ArgumentType(part, typed), scope, null, null, out var adapted, out _, out _, deferAcquisition: true))
                {
                    this.CollectOriginInference(pattern, adapted, inference);
                }
                else if (this.childRows[j] is { Pattern: { } borrowed, Part: var temporary })
                {
                    this.AddTemporaryBound(temporary, borrowed, inference, scope);
                }
            }

            this.CollectInputPremises(parameters.Components[i], parameters.Components[i], inference, use);
        }

        var function = binder as FunctionKoto;
        _ = function is not null
            ? this.SolveCallOriginInference(function, inference, origins, inputs, null, select: true)
            : this.SolveOriginInference(inference, origins, inputs, select: true);

        // SPEC 15.3.6, 15.6.4 step 1: an Origin without a candidate is a local region of the call, direct or indirect: a Function Item's,
        // or a Function Type's per-call inputs, a Callable bound's or an erased value's (PLAN G410). Selected-call clauses are judged at
        // their supplying arguments, never as applicability.
        this.OpenUnsolvedOrigins(use, origins.AsSpan(0, function?.Origins.Count ?? 0), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(binder))));
    }

    // Visits each borrow layer of a well-formed Type pattern beside the same layer of `result`, the pattern itself or its substitution
    // at a use; false only when Prove fails. The visit stops at a nested Function Type, as the premise does (AddResultPremises). An
    // input's visit also covers its Types' own clauses (SPEC 15.3.3), as the input premise does (AddTypePremises); beside `actual`, the
    // argument's own Type, which is well formed, it covers only the positions that the fit replaced. Each entry call is one query of
    // wellFormedParts, each borrow layer's stored visit one of storedParts: a part met again beside the same parts only repeats what its
    // first visit added (each record, obligation and bound is kept once), and a false ends the query. A pattern part is visited at most
    // once per distinct argument part beside it (TypeMemo's pair) and change of its substituted part between consecutive meets of that
    // pair; each skip prunes a revisit whose first visit completed in the query. Whether that bounds a form whose argument parts alternate
    // beside a shared pattern part is unshown (PLAN G387).
    private bool VisitWellFormedPremises(BoundType pattern, BoundType result, BoundType? actual, in WellFormedVisit visit)
        => this.WellFormedPart(pattern, result, actual, visit with { Query = this.wellFormedParts.Begin(this.storageVersion) });

    private bool WellFormedPart(BoundType pattern, BoundType result, BoundType? actual, in WellFormedVisit visit)
    {
        // R1 (DECISIONS BU-99): a slot's solution is its contributor's at a position without evidence; beside an Evidence position's
        // own Type the visit goes on over the solution, judged where the fit replaced an Origin (SPEC 15.6.1).
        if (pattern.Kind == BoundTypeKind.Parameter)
        {
            if (actual is null)
            {
                return true;
            }

            pattern = result;
        }

        if (!pattern.StoresOrigin || pattern.Kind == BoundTypeKind.Function || pattern.Kind != result.Kind || pattern.Components.Count != result.Components.Count)
        {
            return true;
        }

        if (actual is not null && !SameLayer(actual, result))
        {
            actual = null; // Every position is covered when the shapes differ.
        }

        if (visit.Subject == WellFormedSubject.Input && result.Symbol is { } symbol && result.OriginArguments.Count != 0 &&
            !SameOriginArguments(result, actual) &&
            this.originDeclarations.TryGetValue(symbol.Declaration, out var declaration) && declaration.Completed)
        {
            foreach (var relation in declaration.Relations)
            {
                var longer = this.SubstituteStoredOrigin(relation.Longer, symbol.Declaration, (BoundOrigin[])result.OriginArguments);
                var shorter = this.SubstituteStoredOrigin(relation.Shorter, symbol.Declaration, (BoundOrigin[])result.OriginArguments);
                if (!this.VisitTypeClause(longer, shorter, relation.Equality, relation.Syntax, visit))
                {
                    return false;
                }
            }
        }

        // The layer the substituted part forms at the use (WellFormedLayer), its pair layer's slot in the borrow cases its binder has there.
        if (pattern.Components.Count != 0 && pattern.Origin is { } outer && this.WellFormedLayer(result, visit.Use) is { } layer)
        {
            var stored = visit with { Query = this.storedParts.Begin(this.storageVersion), Condition = RequiredCondition(layer.Condition) };
            if (!this.VisitStoredPremises(pattern.Components[0], layer.Target, actual?.Components[0], outer, layer.Outer, stored))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            var argument = actual?.Components[i];
            if (FirstMet(this.wellFormedParts, pattern.Components[i], result.Components[i], argument, visit.Query) &&
                !this.WellFormedPart(pattern.Components[i], result.Components[i], argument, visit))
            {
                return false;
            }
        }

        return true;

        static bool SameOriginArguments(BoundType result, BoundType? actual)
        {
            if (actual is null)
            {
                return false;
            }

            for (var i = 0; i < result.OriginArguments.Count; i++)
            {
                if (!ReferenceEquals(result.OriginArguments[i], actual.OriginArguments[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool VisitStoredPremises(BoundType pattern, BoundType result, BoundType? actual, BoundOrigin outer, BoundOrigin substituted, in WellFormedVisit visit)
    {
        // SPEC 8.1.1, 15.3.7: a layer stores every Origin its stored Type holds, a slot's solution included (DECISIONS BU-99).
        pattern = pattern.Kind == BoundTypeKind.Parameter ? result : pattern;

        // A pair layer of the pattern stores its slot whatever its substitution is; any other layer only beside the same shape.
        var shaped = pattern.Kind == result.Kind && pattern.Components.Count == result.Components.Count;
        if (!result.StoresOrigin || pattern.Kind == BoundTypeKind.Function || (!shaped && !TryPairLayer(pattern, out _, out _)))
        {
            return true;
        }

        if (actual is not null && !SameLayer(actual, result))
        {
            actual = null;
        }

        // SPEC 8.1.1, 8.1.2, 15.6.5: a pair layer's slot, the implicit `o` of an original `s/T` included, is stored only in its binder's
        // borrow cases. At a use it is the slot its substitution stores: a borrow's Origin, a caller's pair slot in that pair's borrow
        // cases, and none for a value binding, which leaves the relation vacuous.
        if (this.OuterOrigin(pattern) is { } origin && this.StoredSlot(result, visit.Use, out var slot) is { } value &&
            !this.VisitWellFormedPremise(origin, outer, result, value, actual is null ? null : this.OuterOrigin(actual), substituted, visit, RequiredCondition(slot)))
        {
            return false;
        }

        if (!shaped)
        {
            return true;
        }

        for (var i = 0; i < Math.Min(pattern.OriginArguments.Count, result.OriginArguments.Count); i++)
        {
            if (!this.VisitWellFormedPremise(pattern.OriginArguments[i], outer, result, result.OriginArguments[i], actual?.OriginArguments[i], substituted, visit))
            {
                return false;
            }
        }

        for (var i = 0; i < pattern.Components.Count; i++)
        {
            var argument = actual?.Components[i];
            if (FirstMet(this.storedParts, pattern.Components[i], result.Components[i], argument, visit.Query) &&
                !this.VisitStoredPremises(pattern.Components[i], result.Components[i], argument, outer, substituted, visit))
            {
                return false;
            }
        }

        return true;
    }

    // `kept` is the argument's Origin at the same position, which satisfies the relation already when the fit kept it: the argument's
    // Type is well formed, and the fit, judged on its own, makes its outer Origin outlive the substituted one.
    private bool VisitWellFormedPremise(BoundOrigin longer, BoundOrigin outer, BoundType result, BoundOrigin value, BoundOrigin? kept, BoundOrigin substituted, in WellFormedVisit visit, ulong condition = 0)
    {
        condition |= visit.Condition;
        if (OriginOutlives(longer, outer) || OriginOutlives(value, substituted) || ReferenceEquals(value, kept))
        {
            return true;
        }

        if (visit.Named is { } function)
        {
            var excluded = this.resultPremiseExcluded;
            this.resultPremiseExcluded = function;
            try
            {
                if (this.ProvesOriginOutlives(longer, outer, function, condition))
                {
                    return true; // The definition proves it without the premise.
                }
            }
            finally
            {
                this.resultPremiseExcluded = excluded;
            }
        }

        if ((visit.Action is WellFormedAction.Bound or WellFormedAction.Unbound) && visit.Owner?.Symbol is { } container)
        {
            // As the call's declared relations are: the container's stored Origins are the receiver Type's arguments, and a pair's `o`
            // its Type argument's slot.
            longer = this.SubstituteStoredOrigin(longer, container.Declaration, (BoundOrigin[])visit.Owner.OriginArguments, types: (BoundType[])visit.Owner.Components);
            outer = this.SubstituteStoredOrigin(outer, container.Declaration, (BoundOrigin[])visit.Owner.OriginArguments, types: (BoundType[])visit.Owner.Components);
        }

        switch (visit.Action)
        {
            case WellFormedAction.Detect:
                return false;
            case WellFormedAction.Instance:
                if (InstanceOutlives(value, substituted, visit.Comparison, this, visit.Use, condition))
                {
                    return true;
                }

                this.failedResultPremise = (value, substituted);
                return false;
            case WellFormedAction.Bound:
                BoundOrigins(visit.Inference!, longer, outer, visit.Subject != WellFormedSubject.Input);
                return true;
            case WellFormedAction.Unbound:
                if (visit.Inference!.IndexOf((Longer: longer, Shorter: outer, LongerVariable: true, ShorterVariable: true)) is >= 0 and var index)
                {
                    visit.Inference!.Bounds.RemoveAt(index);
                }

                return true;
            case WellFormedAction.Prove:
                if (this.ProvesOriginOutlives(value, substituted, visit.Use, visit.Comparison, condition))
                {
                    return true;
                }

                this.failedResultPremise ??= (value, substituted);
                return false;
            default:
                if (visit.Subject == WellFormedSubject.Input)
                {
                    this.JudgeCallPosition(visit.Use, value, substituted, false, null, null, wellFormed: true, condition: condition);
                }
                else
                {
                    this.RequireResultOutlives(result, value, substituted, visit.Use, condition);
                }

                return true;
        }
    }

    // An input Type's own clause, substituted with the Origins of its occurrence: a bound of the call's inference, at a selected call a
    // `declared` relation at the input, or proven by a whole-contract comparison.
    private bool VisitTypeClause(BoundOrigin longer, BoundOrigin shorter, bool equality, Koto clause, in WellFormedVisit visit)
    {
        if (visit.Action == WellFormedAction.Require)
        {
            this.JudgeCallPosition(visit.Use, longer, shorter, equality, null, clause, declared: true);
            return true;
        }

        if (visit.Action == WellFormedAction.Prove)
        {
            return this.ProvesOriginOutlives(longer, shorter, visit.Use, visit.Comparison) &&
                (!equality || this.ProvesOriginOutlives(shorter, longer, visit.Use, visit.Comparison));
        }

        if (visit.Action == WellFormedAction.Instance)
        {
            if (InstanceOutlives(longer, shorter, visit.Comparison, this, visit.Use) &&
                (!equality || InstanceOutlives(shorter, longer, visit.Comparison, this, visit.Use)))
            {
                return true;
            }

            this.failedResultPremise = (longer, shorter);
            return false;
        }

        if (visit.Action == WellFormedAction.Bound)
        {
            BoundOrigins(visit.Inference!, longer, shorter, false);
            if (equality)
            {
                BoundOrigins(visit.Inference!, shorter, longer, false);
            }
        }

        return true;
    }

    // A meet outlives an Origin exactly when each operand does, so each failing operand is its own chain (SPEC 15.6.1 Identity).
    private void RequireResultOutlives(BoundType result, BoundOrigin value, BoundOrigin substituted, Koto use, ulong condition)
    {
        if (value.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < value.Operands.Count; i++)
            {
                this.RequireResultOutlives(result, value.Operands[i], substituted, use, condition);
            }
        }
        else if (!OriginOutlives(value, substituted))
        {
            this.AddObligation(new(BindingObligationKind.OriginOutlives, use, BindingDeadline.BodyOrigins, result, value, substituted, WellFormed: true, Condition: condition));
        }
    }
}
