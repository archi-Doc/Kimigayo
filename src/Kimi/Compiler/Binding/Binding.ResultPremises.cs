// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// SPEC 15.3.7, 15.6.1, 15.6.4 step 3: a signature's intrinsic well-formedness (every Origin stored below a borrow layer outlives that
// layer's Origin, and its Types' own clauses hold) is a premise of its definition and an obligation at each use. For the
// result, a relation that the callee's inputs and declared relations already prove needs no premise; only the others, which the
// definition would otherwise fail, are carried to the uses: each call solves its Origins with them and proves them for its substituted
// result, and a function reference or a conformance proves them from the required contract. Every input relation is a premise, so each
// is carried: each call shortens its solution with them and judges them at each filled slot by what its position contributes
// (Contribution, the evidence walk's fact: Evidence where its instantiation replaced an Origin, a Signature position at every
// position but inside a slot's solution, which its contributor proves), and a whole-contract comparison proves them where its solve
// replaced a required Origin. One enumeration serves both (WellFormedPremises); each entry here consumes it.
public sealed partial class Binding
{
    // The stored relation a whole-contract comparison's premises did not prove (ProvesPremises), kept for its failure (PremiseFailure);
    // its readers clear it before the proof.
    private (BoundOrigin Longer, BoundOrigin Shorter)? failedResultPremise;

    // The failure of the last definition's solve that did not hold (ContractFits; DefinitionContractFits reads it), and the declared clause a solve's
    // CheckCallOriginRelations did not prove, with its substituted ends.
    private ComparisonFailure contractFailure;
    private (BoundOrigin Longer, BoundOrigin Shorter, bool Equality, Koto Syntax)? failedCallClause;

    // SPEC 15.3.7, 10.7, 15.3.6: the contract comparison of a callable definition, a Function Item's or an anonymous function's (a
    // Closure Type's, board D-822): only the implementation's instantiable call Origins are solved, all together, including an Item's
    // named and result-only universals and every per-call Origin of a direct borrowed input, against the required contract, under the
    // use's premises (DECISIONS BU-93), also for an Item without own Origins, whose signature may name a fixed Origin of an enclosing
    // function (SPEC 15.3.4). Repeated invariant occurrences and declared conditions constrain the same solution. A required signature
    // over an open evidence region (SPEC 10.8) is compared the same way, under the same premises, row sink and well-formedness proof, for
    // both definitions (board D-822's B1: no premise-free known-signature comparison beside it).
    internal bool DefinitionContractFits(BoundType item, BoundType required, Koto use, List<BindingObligation>? rows, out ComparisonFailure failure)
    {
        failure = default;
        if (item.Kind is not (BoundTypeKind.FunctionItem or BoundTypeKind.Closure) || item.Symbol is not { Declaration: FunctionKoto function } symbol)
        {
            return false;
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
            var fits = this.ContractFits(use, symbol, function, ContractComparison(required, rows), null, item, null, default, origins, inputs);
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

    // SPEC 15.3.7, 15.6.1, 11.5 (PLAN G214): the result a definition assumes well formed in its body (R3, ExtractOriginPremises) and each
    // use proves for its instantiation (RequireResultPremises): a named function's other than a constructor's, and a getter's, since a
    // custom, computed or Contract `get` produces a function result. A getter is that definition as its accessor syntax, whose result a
    // computed or requirement Property's header is (HeaderGetter), and as the execution view each read calls (AccessorFunction).
    private static BoundType? AssumedResult(Koto definition) => definition switch
    {
        FunctionKoto { IsAnonymous: false, IsConstructor: false, BoundSymbol.Type: { } result } => result,
        PropertyAccessorKoto { AccessorKind: PropertyAccessorKind.Get, Parent: PropertyKoto { BoundSymbol.Property.Getter.Result: { } result } } => result,
        _ => null,
    };

    // The definition a call's callee stands for: a getter's accessor syntax for its execution view (its inputs and its own clauses are
    // there), else the function itself.
    private static Koto CalleeDefinition(FunctionKoto function) => (Koto?)function.Accessor?.Declaration ?? function;

    private static bool CarriesResultPremises(FunctionKoto function) => AssumedResult(CalleeDefinition(function)) is { StoresOrigin: true };

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

    // A result premise as a member's call sees it: the container's stored Origins are the receiver Type `owner`'s arguments, and a pair's
    // `o` its Type argument's slot, as the call's declared relations are (SubstituteClauseOrigin).
    private (BoundOrigin Longer, BoundOrigin Outer) AtCall(in WellFormedPremise premise, BoundType? owner)
    {
        if (owner?.Symbol is not { } container)
        {
            return (premise.Longer, premise.Outer);
        }

        var arguments = (BoundOrigin[])owner.OriginArguments;
        var types = (BoundType[])owner.Components;
        return (this.SubstituteStoredOrigin(premise.Longer, container.Declaration, arguments, types: types),
            this.SubstituteStoredOrigin(premise.Outer, container.Declaration, arguments, types: types));
    }

    // SPEC 15.6.4 steps 1-4: a call's fresh Origins satisfy its callee's result premises where a solution does, so a result over
    // body-local borrows is bounded by every Origin it holds. Otherwise the call is solved without them and the obligation at the
    // selected call reports the relation; a premise never decides applicability (SPEC 15.6.1). With `select`, candidate applicability,
    // the solve without them judges no bound either (SolveOriginInference). The callee is the inference's binder, a function.
    private bool SolveCallOriginInference(
        OriginInference inference, Span<BoundOrigin> origins, Span<BoundOrigin> inputs, Span<BoundOrigin> slotValues, BoundType? declaringType, bool select = false)
    {
        var function = (FunctionKoto)inference.Binder;
        if (!CarriesResultPremises(function))
        {
            return this.SolveOriginInference(inference, origins, inputs, slotValues, declaringType, select);
        }

        var type = function.BoundSymbol!.Type!;
        this.RemoveResultBounds(function, inference, declaringType); // Left by an earlier pass.
        var count = inference.Bounds.Count;
        var variables = inference.Variables.Count;
        foreach (var premise in new WellFormedPremises(this, type, type, null, WellFormedSubject.NamedResult, function, inference.Use, default))
        {
            var (longer, outer) = this.AtCall(premise, declaringType);
            BoundOrigins(inference, longer, outer, result: true);
        }

        if (inference.Bounds.Count != count)
        {
            if (this.SolveOriginInference(inference, origins, inputs, slotValues, declaringType))
            {
                return true;
            }

            this.RemoveResultBounds(function, inference, declaringType);
            inference.Variables.RemoveRange(variables, inference.Variables.Count - variables); // The result-only Origins they discovered.
        }

        return this.SolveOriginInference(inference, origins, inputs, slotValues, declaringType, select);
    }

    // The bounds the result premises of `function` added to a call's inference, removed again.
    private void RemoveResultBounds(FunctionKoto function, OriginInference inference, BoundType? declaringType)
    {
        var type = function.BoundSymbol!.Type!;
        foreach (var premise in new WellFormedPremises(this, type, type, null, WellFormedSubject.NamedResult, function, inference.Use, default))
        {
            var (longer, outer) = this.AtCall(premise, declaringType);
            if (inference.IndexOf((Longer: longer, Shorter: outer, LongerVariable: true, ShorterVariable: true)) is >= 0 and var index)
            {
                inference.Bounds.RemoveAt(index);
            }
        }
    }

    // SPEC 15.3.7: the selected call's obligations for its substituted result; SPEC 15.3.3, 15.6.4 step 3 (J2, PLAN G936): with them
    // the declared relations over the call's region of the result's Types, down to its slots, as rows of the call (RequireResultClause),
    // which A5 judges with the call's other rows (FormBodyRelation).
    private void RequireResultPremises(FunctionKoto function, BoundType result, Koto use)
    {
        if (CarriesResultPremises(function))
        {
            foreach (var premise in new WellFormedPremises(this, function.BoundSymbol!.Type!, result, null, WellFormedSubject.CallResult, function, use, default))
            {
                if (premise.Clause is { } clause)
                {
                    this.RequireResultClause(use, premise.Longer, premise.Outer, premise.Equality, clause);
                }
                else
                {
                    this.RequireResultOutlives(premise.Type, premise.Value, premise.Substituted, use, premise.Condition);
                }
            }
        }
    }

    // SPEC 15.3.3, 15.6.4 step 3: a clause of a selected call's result Type (a constructor's construction Type included,
    // JudgeCallClauses) is a `declared` relation at the call when it is over the call's region (CallRowRelation: J2, PLAN G936,
    // DECISIONS BU-104); every other one stays the formation proof's (CheckTypeOriginRelations). With an input Type's clauses
    // (RequireInputPremises) this is the one judge of such a relation: a Type derived from an input, a slot's solution or the result holds
    // the call's region only through them.
    private void RequireResultClause(Koto use, BoundOrigin longer, BoundOrigin shorter, bool equality, Koto clause)
    {
        if (CallRowRelation(longer, shorter))
        {
            this.JudgeCallPosition(use, longer, shorter, equality, null, clause, declared: true);
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
                this.MentionsOriginSlot(pattern, solution.Binder, slot))
            {
                return true;
            }

            for (var j = start; j < end; j++)
            {
                if (this.IsEvidenceLeaf(solution, j, end) && this.childRows[j].Pattern is { } leaf && this.MentionsOriginSlot(leaf, solution.Binder, slot))
                {
                    return true;
                }
            }
        }

        return solution.Binder is FunctionKoto { BoundSymbol.ReceiverIndex: >= 0 and var receiver } function &&
            function.Parameters[receiver].Type.BoundType is { } receiverPattern && this.MentionsOriginSlot(receiverPattern, solution.Binder, slot);
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

    // SPEC 15.3.7, 15.6.4 step 3, 15.3.6: an input's well-formedness, its parameter pattern beside the Type `instantiated` that a call
    // collects for that input, a slot's solution included (PLAN G1115), shortens the call's solution where the arguments alone would leave
    // an instantiated input ill formed (SolveOriginInference): every premise is an upper bound of its layer's Origin, and the selected
    // call requires those the argument's own Type does not satisfy (RequireInputPremises). Only a call collects it: a whole-contract
    // comparison proves it instead (ProvesInputPremises), since the required contract's input premises are no premises at its use.
    private void CollectInputPremises(BoundType pattern, BoundType instantiated, OriginInference inference, Koto use)
    {
        if (!instantiated.StoresOrigin)
        {
            return;
        }

        foreach (var premise in new WellFormedPremises(this, pattern, instantiated, null, WellFormedSubject.Input, null, use, default))
        {
            // G1128 R1.3 (b), theorem (2): over a slot's skeleton a premise shortens only its own node's Origin (a layer's premise its
            // layer's, by the enumeration; a clause relates Origin arguments of its own Type, since no SPEC form names a component's
            // Origin in a clause), so the variables one key shares have one solution (Release check).
            if (premise.Clause is not null && (Foreign(inference, premise.Type, premise.Longer) || Foreign(inference, premise.Type, premise.Outer)))
            {
                throw new InvalidOperationException("A clause over a slot's skeleton relates only its own Type's Origins (InternalInvariant).");
            }

            BoundOrigins(inference, premise.Longer, premise.Outer, result: false);
            if (premise.Equality)
            {
                BoundOrigins(inference, premise.Outer, premise.Longer, result: false);
            }
        }

        static bool Foreign(OriginInference inference, BoundType type, BoundOrigin end)
            => inference.SolvesSlotVariable(end) && Array.IndexOf((BoundOrigin[])type.OriginArguments, end) < 0;
    }

    // SPEC 15.3.7, 15.6.4 step 3: the selected call's obligations for the instantiated parameter Type of a slot it fills (SPEC 10.1 step 2),
    // judged at `at` by what the slot's position contributes (Contribution). Evidence, a written argument's or the receiver's own Type
    // `supplied`, is well formed, so only a position that the fit replaced, such as an Origin shared with another input, can fail. A
    // Signature position has every position of its Type judged: a default supplies no Type (SPEC 10.1.1) and is judged at the call (SPEC
    // 7.2.3). The enumeration walks the parameter's pattern `pattern` beside `parameter`: at a position that supplies no evidence it stops at a
    // slot of the pattern, whose solution its contributor proves, and at an Evidence position it goes on over the solution, judged where
    // the fit replaced an Origin (SPEC 10.3, 15.6.1; DECISIONS BU-99); a layer around the slot is this call's. Each premise is a row of
    // the body (JudgeCallPosition, FormBodyRelation), which A5 judges beside the input's fit rows: a call position's Origin relations never
    // fail the input, and its instantiated well-formedness is a requirement of its own (SPEC 15.6.4 step 3). As for the fit, an argument
    // that carries no Origin, such as one that transfers control, supplies none; and an input Binding already failed (its fit's
    // structure, SPEC 5.7's raw-layer rules, a located limit, or its conversion's contract: a Function value's, ConvertFunctionValue,
    // whose relations are published with it, FollowsContract) is no instance of its parameter, so its well-formedness, read from its
    // supplied Type, would rest on that failure (SPEC 23.1.2), as a premise over a variable `solved`, the call's solve, marked does
    // (OriginInference.Marked).
    private void RequireInputPremises(Koto at, BoundType pattern, BoundType parameter, BoundType? supplied, Contribution contribution, OriginInference? solved)
    {
        if (parameter.StoresOrigin && (supplied is null || (supplied.StoresOrigin && at.BindingFailure == BindingFailure.None)))
        {
            var actual = contribution == Contribution.Evidence ? supplied : null;
            foreach (var premise in new WellFormedPremises(this, pattern, parameter, actual, WellFormedSubject.Input, null, at, default))
            {
                if (solved is not null && (solved.ReadsMarked(premise.Longer) || solved.ReadsMarked(premise.Outer)))
                {
                    continue;
                }

                if (premise.Clause is { } clause)
                {
                    this.JudgeCallPosition(at, premise.Longer, premise.Outer, premise.Equality, null, clause, declared: true);
                }
                else
                {
                    this.JudgeCallPosition(at, premise.Value, premise.Substituted, equality: false, variable: null, clause: null, wellFormed: true, condition: premise.Condition);
                }
            }
        }
    }

    // SPEC 15.3.7: a whole-contract comparison proves the implementation's instantiated input well formed from the premises at `use`
    // wherever its solve replaced an Origin of the required input, whose own well-formedness covers every position it kept.
    private bool ProvesInputPremises(BoundType implementation, BoundType required, Koto use, in CallableInstance comparison)
        => ReferenceEquals(implementation, required) || !implementation.StoresOrigin ||
            this.ProvesPremises(new(this, implementation, implementation, required, WellFormedSubject.Input, null, use, comparison), use, comparison);

    // SPEC 15.3.7 (the owner's (a)): a whole-contract comparison proves the implementation's result condition for its substituted result
    // from the required contract's premises at `use`: a named function's result premises; an anonymous function's result
    // well-formedness, which is no premise of its body (SPEC 15.3.4, 15.6.1, DECISIONS R4-M162) and which every comparison judges.
    private bool ProvesResultPremises(FunctionKoto function, BoundType result, Koto use, in CallableInstance comparison)
    {
        if (!function.IsAnonymous)
        {
            return !CarriesResultPremises(function) ||
                this.ProvesPremises(new(this, function.BoundSymbol!.Type!, result, null, WellFormedSubject.NamedResult, function, use, comparison), use, comparison);
        }

        if (result.StoresOrigin && this.FailedWellFormedness(result, comparison, use) is { } failed)
        {
            this.failedResultPremise = failed; // A Type clause's too: the comparison's failure names it (ContractFits).
            return false;
        }

        return true;
    }

    // Whether the premises at `use` prove each of `premises` under `comparison`, which it consumes. The first stored relation they do not
    // prove is kept for the comparison's failure (failedResultPremise); a clause's is not, as before the enumeration.
    private bool ProvesPremises(WellFormedPremises premises, Koto use, in CallableInstance comparison)
    {
        foreach (var premise in premises)
        {
            if (!(this.ProvesOriginOutlives(premise.Value, premise.Substituted, use, comparison, premise.Condition) ||
                    this.LeavesBodyRows(premise.Value, premise.Substituted, use, comparison, premise.Condition, premise.Clause)) ||
                (premise.Equality && !(this.ProvesOriginOutlives(premise.Substituted, premise.Value, use, comparison) ||
                    this.LeavesBodyRows(premise.Substituted, premise.Value, use, comparison, 0, premise.Clause))))
            {
                if (premise.Clause is null)
                {
                    this.failedResultPremise ??= (premise.Value, premise.Substituted);
                }

                return false;
            }
        }

        return true;
    }

    // SPEC 15.3.7, 15.6.4 step 3: whether a use of a named function must prove Origin conditions beyond its inputs' Types: a declared
    // relation, or a result premise that its inputs and clauses do not prove.
    private bool HasOriginConditions(FunctionKoto function)
    {
        if (this.originDeclarations.GetValueOrDefault(function) is { Relations.Length: > 0 })
        {
            return true;
        }

        if (!CarriesResultPremises(function))
        {
            return false;
        }

        var type = function.BoundSymbol!.Type!;
        var premises = new WellFormedPremises(this, type, type, null, WellFormedSubject.NamedResult, function, function, default);
        try
        {
            return premises.MoveNext();
        }
        finally
        {
            premises.Dispose();
        }
    }

    // SPEC 15.3.7, the owner's (a): the first part of the implementation's own well-formedness, its n-th input's or its result's, that the
    // required signatures' own well-formedness and the premises at `use` do not prove under the instantiation (null when every part
    // holds), with the failing relation.
    private (int Part, BoundOrigin Longer, BoundOrigin Shorter)? FailedSignatureWellFormedness(BoundType actual, in CallableInstance instance, Koto use)
    {
        var inputs = actual.Components[0];
        for (var i = 0; i <= inputs.Components.Count; i++)
        {
            if (this.FailedWellFormedness(i < inputs.Components.Count ? inputs.Components[i] : actual.Components[1], instance, use) is { } failed)
            {
                return (i, failed.Longer, failed.Shorter);
            }
        }

        return null;
    }

    // The first relation of a signature part's own well-formedness (its stored relations and its Types' clauses) that the instance's
    // premises and those at `use` do not prove under the instantiation, null when each holds: FailedSignatureWellFormedness's per part,
    // and an anonymous function's result condition in a whole-contract comparison (ProvesResultPremises; board D-822).
    private (BoundOrigin Longer, BoundOrigin Shorter)? FailedWellFormedness(BoundType part, in CallableInstance instance, Koto use)
    {
        foreach (var premise in new WellFormedPremises(this, part, part, null, WellFormedSubject.Input, null, use, instance))
        {
            if (!InstanceOutlives(premise.Value, premise.Substituted, instance, this, use, premise.Condition) ||
                (premise.Equality && !InstanceOutlives(premise.Substituted, premise.Value, instance, this, use)))
            {
                return (premise.Value, premise.Substituted);
            }
        }

        return null;
    }

    // The failure of a premise a Prove enumeration did not prove, under `comparison` (ContractFits).
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
    // of U (Materialization) or of its own Type (a fill, a mixed range), at a root or nested row: its Place Origin is an input bound of
    // `a`, by the key of a named call's evidence walk (InferComposite's Aggregate part, InferLiteral: InferTemporary). A part of Join
    // kind at `ref/(...)`, which InferComposite borrows too, is PLAN G729's exception and no key here: whether a value call takes it
    // is the question of a temporary carried out of its arm.
    private void AddTemporaryBound(Koto part, BoundType pattern, OriginInference inference, BindingScope scope)
    {
        if (pattern is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Origin: { } origin } &&
            (this.KindOf(part) == PartKind.Aggregate || this.Materialization(part, pattern, scope) is not null))
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
                this.CollectOriginInference(parameters.Components[i], actual, inference, use);
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
                    this.CollectOriginInference(pattern, adapted, inference, use);
                }
                else if (this.childRows[j] is { Pattern: { } borrowed, Part: var temporary })
                {
                    this.AddTemporaryBound(temporary, borrowed, inference, scope);
                }
            }

            this.CollectInputPremises(parameters.Components[i], parameters.Components[i], inference, use);
        }

        // J2 (SPEC 15.3.6, 15.2.2; PLAN G936; R7-O1 b1, the Middle's ruling of 2026-11-19): the direct call's rule, one member for both
        // paths (SlotSolution.OpenWaited): an Origin parameter that a waiting part's position and an evidence position both name is a
        // variable the waiting part's check bounds too, solved as `(E and region)` with the call's region, so the waiting part is completed
        // over the region and its Type's clauses are the call's rows (CallRowRelation), never a second judgment of its formation. Only a
        // function binds Origin parameters (a Function Type's Origins are per-call inputs, SPEC 15.3.4), and a value call's Type slots
        // are fixed at its callee's reference, so only Origin parameters open.
        var function = binder as FunctionKoto;
        var waited = solution with { Binder = binder, OriginInference = inference, FirstSolve = true };
        if (function is not null)
        {
            waited.OpenWaited();
            waited.OpenWaitedSlots();
        }

        _ = function is not null
            ? this.SolveCallOriginInference(inference, origins, inputs, [], null, select: true)
            : this.SolveOriginInference(inference, origins, inputs, [], select: true);
        if (function is not null)
        {
            this.RecordOpenings(waited.Call, waited);
        }

        // SPEC 15.3.6, 15.6.4 step 1: an Origin without a candidate is a local region of the call, direct or indirect: a Function Item's,
        // or a Function Type's per-call inputs, a Callable bound's or an erased value's (PLAN G410). Selected-call clauses are judged at
        // their supplying arguments, never as applicability.
        this.OpenUnsolvedOrigins(use, origins.AsSpan(0, function?.Origins.Count ?? 0), inputs.AsSpan(0, Math.Min(inputs.Length, InputOriginCount(binder))));
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
