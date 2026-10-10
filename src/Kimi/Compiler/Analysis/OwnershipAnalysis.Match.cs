// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<SelectionFrame> selections = new();
    private readonly List<int> activeDecompositions = new();
    private readonly List<bool> patternStorageNeeded = new();
    private readonly List<(BindingSymbol Symbol, int Subject, int Value, int Arm, int Loan, int Position)> candidates = new();

    private static AcquisitionKind PatternAcquisitionKind(BoundPattern pattern) => pattern.Acquisition switch
    {
        PatternAcquisition.Copy or PatternAcquisition.Borrow => AcquisitionKind.Copy,
        PatternAcquisition.Move => AcquisitionKind.Move,
        PatternAcquisition.CopyOrMove => AcquisitionKind.CopyOrMove,
        _ => throw new InvalidOperationException("Pattern acquisition must be committed before ownership construction."),
    };

    private bool SupportsMatch(BoundMatch plan)
    {
        if (!plan.IsCurrent || plan.Coverage.State != MatchCoverageState.Exhaustive)
        {
            return false;
        }

        if (plan.Syntax.BoundType is not { } result || (!ReferenceEquals(result, BoundType.Never) && result.Kind != BoundTypeKind.Parameter && !this.SupportsType(result)))
        {
            return false;
        }

        for (var i = 0; i < plan.Positions.Count; i++)
        {
            var position = plan.Positions[i];
            if (position.Acquisition == PatternAcquisition.Deferred || (position.MatchedType.Kind != BoundTypeKind.Parameter && !this.SupportsType(position.MatchedType)) ||
                (position.Kind == BoundPatternKind.Binding && position.Acquisition is not (PatternAcquisition.Copy or PatternAcquisition.Borrow or PatternAcquisition.Move or PatternAcquisition.CopyOrMove)))
            {
                return false;
            }
        }

        return true;
    }

    private int Match(MatchKoto syntax)
    {
        // Never produces no Subject storage and cannot select an arm. Evaluate its
        // transfers/calls before applying the value-Type subset gate.
        if (ReferenceEquals(syntax.Expression.BoundType, BoundType.Never))
        {
            this.Expression(syntax.Expression);
            this.CheckAbandonedMatchArms(syntax);
            this.current = -1;
            return -1;
        }

        if (!this.compilation.Binding.TryGetMatch(syntax, out var plan) || !this.SupportsMatch(plan!))
        {
            this.Unsupported(syntax);
            return -1;
        }

        // SPEC 15.1.6 subject rule: a bare Place is borrowed in its mode (a bare exclusive borrow value is Reborrowed);
        // any other Subject, including a temporary, a written borrow or a transfer, is acquired as it is.
        var input = plan!.SubjectBorrow is { } borrowed ? this.BorrowStruct(syntax.Expression, borrowed) : this.Expression(syntax.Expression);
        if (this.current < 0 || !this.flow!.Nodes[syntax.Expression].CanCompleteNormally)
        {
            this.current = -1;
            return -1;
        }

        if (input < 0)
        {
            return -1;
        }

        var tempMark = this.temporaries.Count;
        var localMark = this.locals.Count;
        var output = this.ResultPlace(syntax);
        var subject = this.Place(syntax.Expression, plan.SubjectBorrow ?? syntax.Expression.BoundType, OwnershipPlaceKind.Subject, false, this.body.PlaceStorage[input].Acquisition);
        this.PrepareDecompositionSlots();
        this.Emit(OwnershipOperationKind.Declare, syntax, subject);
        this.Emit(OwnershipOperationKind.InitializeSubject, syntax, subject, input);
        this.temporaries.Add(new(subject, syntax.Expression, this.registrationSequence++, true));

        var matchIndex = this.body.MatchStorage.Count;
        var armStart = this.body.MatchArmStorage.Count;
        for (var i = 0; i < plan!.Arms.Count; i++)
        {
            this.body.MatchArmStorage.Add(default);
        }

        this.body.MatchStorage.Add(new(plan, subject, output, armStart, plan.Arms.Count));
        var dispatch = this.Emit(OwnershipOperationKind.MatchDispatch, syntax, subject);
        this.body.OperationSteps[dispatch] = matchIndex;
        var completes = this.flow.Nodes[syntax].CanCompleteNormally;
        var join = this.ResultJoin(syntax, output);
        this.selections.Add(new(syntax, output, join, localMark, tempMark, this.comparisonDepth));

        // Propagate Binding presence once, backwards through the retained preorder.
        // Both Copy and Move need real input Places along their Case paths.
        var neededStart = this.patternStorageNeeded.Count;
        for (var i = 0; i < plan.Positions.Count; i++)
        {
            this.patternStorageNeeded.Add(plan.Positions[i].Kind == BoundPatternKind.Binding);
        }

        for (var i = plan.Positions.Count - 1; i >= 0; i--)
        {
            if (this.patternStorageNeeded[neededStart + i] && plan.Positions[i].Parent >= 0)
            {
                this.patternStorageNeeded[neededStart + plan.Positions[i].Parent] = true;
            }
        }

        var previousTest = -1;
        var previousGuard = -1;
        for (var i = 0; i < plan.Arms.Count; i++)
        {
            var arm = plan.Arms[i];
            this.activeDecompositions[subject] = -1;
            this.current = previousTest < 0 ? dispatch : previousTest;
            this.current = this.New(OwnershipOperationKind.PatternTest, arm.Syntax.Pattern, subject);
            var test = this.current;
            this.body.OperationSteps[test] = armStart + i;
            // Every nonfinal Pattern can fail in the conservative verification graph,
            // even a catch-all. False guards retain their independent side effects.
            this.Connect(previousTest < 0 ? dispatch : previousTest, test, previousTest < 0 ? OwnershipEdgeKind.MatchArm : OwnershipEdgeKind.False);
            this.Connect(previousGuard, test, OwnershipEdgeKind.False);
            previousTest = test;
            previousGuard = -1;
            var guardEntry = -1;
            var guardBranch = -1;
            var guardValue = -1;
            var guardCleanupStart = -1;
            var guardLoan = -1;
            if (arm.Syntax.Guard is { } guard)
            {
                guardEntry = this.New(OwnershipOperationKind.Branch, guard);
                this.Connect(test, guardEntry, OwnershipEdgeKind.True);
                this.current = guardEntry;
                var guardDepth = this.comparisonDepth++;
                if (MatchTypes.NeedsGuardProtection(plan.Positions[arm.Pattern].MatchedType))
                {
                    // Pure tests inspect private owned Subject storage. Once guard
                    // code can observe it, retain shared protection through cleanup.
                    var subjectValue = this.Value(subject);
                    this.Emit(OwnershipOperationKind.Read, guard, subject);
                    this.placeValues[subject] = subjectValue;
                    guardLoan = this.BeginSharedLoan(subject, guard: armStart + i);
                }

                var candidateMark = this.candidates.Count;
                for (var position = arm.Pattern; position < plan.Positions[arm.Pattern].End; position++)
                {
                    if (plan.Positions[position].CandidateSymbol is { } candidate)
                    {
                        this.candidates.Add((candidate, subject, this.Value(subject), armStart + i, guardLoan, position == arm.Pattern && MatchTypes.SupportsGuard(plan.Positions[position].MatchedType) ? -1 : position));
                    }
                }

                var condition = this.Condition(guard, out guardCleanupStart);
                this.EndComparisonLoans(guardDepth, guard);
                this.comparisonDepth = guardDepth;
                guardValue = condition;
                this.candidates.RemoveRange(candidateMark, this.candidates.Count - candidateMark);
                if (condition >= 0 && this.current >= 0 && this.flow.Nodes[guard].CanCompleteNormally)
                {
                    guardBranch = this.Emit(OwnershipOperationKind.Branch, guard);
                    this.SetValue(guardBranch, OwnershipValueKind.Alias, [condition]);
                    previousGuard = guardBranch;
                }
                else
                {
                    // A guard that cannot complete normally selects no body: the body is unreachable source.
                    this.current = -1;
                }
            }

            var bodyEntry = this.New(OwnershipOperationKind.Branch, arm.Syntax.Body);
            this.Connect(arm.Syntax.Guard is null ? test : guardBranch, bodyEntry, OwnershipEdgeKind.True);
            this.current = bodyEntry;
            var decompositionStart = this.body.DecompositionStorage.Count;
            this.AcquirePattern(plan, arm.Pattern, subject, neededStart, armStart + i);
            this.body.MatchArmStorage[armStart + i] = new(matchIndex, arm.Pattern, test, decompositionStart, this.body.DecompositionStorage.Count - decompositionStart, guardEntry, guardBranch, bodyEntry, guardValue, guardCleanupStart, guardLoan);

            var secured = -1;
            if (arm.Syntax.Body is CodeBlockKoto block)
            {
                this.Block(block);
                if (this.flow.Nodes[block].CanCompleteNormally)
                {
                    secured = this.WriteResult(block, output, -1);
                }
                else
                {
                    this.current = -1;
                }
            }
            else
            {
                var value = -1;
                // An anonymous function is an expression arm: its value is the closure it creates (SPEC 7.6, 14.2).
                if (KotoHelper.IsBodyExpression(arm.Syntax.Body) && (arm.Syntax.Body is not UnitLiteralKoto || KotoHelper.IsResultRequiringSelection(syntax)))
                {
                    value = this.Expression(arm.Syntax.Body);
                }
                else
                {
                    this.Statement(arm.Syntax.Body);
                }

                if (this.flow.Nodes[arm.Syntax.Body].CanCompleteNormally)
                {
                    if (KotoHelper.IsResultRequiringSelection(syntax) && KotoHelper.IsBodyExpression(arm.Syntax.Body))
                    {
                        secured = this.WriteResult(arm.Syntax.Body, output, value);
                    }
                    else
                    {
                        secured = this.WriteResult(arm.Syntax.Body, output, -1);
                    }
                }
                else
                {
                    this.current = -1;
                }
            }

            if (this.current >= 0)
            {
                this.Cleanup(tempMark, localMark, syntax, CleanupReason.ScopeExit);
                this.ConnectResult(join, secured);
            }

            this.locals.RemoveRange(localMark, this.locals.Count - localMark);
            this.temporaries.RemoveRange(tempMark + 1, this.temporaries.Count - tempMark - 1);
        }

        this.activeDecompositions[subject] = -1;
        this.patternStorageNeeded.RemoveRange(neededStart, this.patternStorageNeeded.Count - neededStart);
        this.temporaries.RemoveRange(tempMark, this.temporaries.Count - tempMark);
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(dispatch, join, completes);
        if (!completes)
        {
            this.current = -1;
            return -1;
        }

        return this.CompleteResult(syntax, output, join);
    }

    private void CheckAbandonedMatchArms(MatchKoto syntax)
    {
        // There is no Subject value. Each arm is built as unreachable source, so an abrupt Subject
        // cannot hide unsupported operations (OwnershipBody.CheckUnreachable).
        var temps = this.temporaries.Count;
        var locals = this.locals.Count;
        var output = this.ResultPlace(syntax);
        var join = this.ResultJoin(syntax, output);
        this.selections.Add(new(syntax, output, join, locals, temps, this.comparisonDepth));
        for (var i = 0; i < syntax.Arms.Count; i++)
        {
            var arm = syntax.Arms[i];
            this.current = -1;
            if (arm.Guard is not null)
            {
                this.Unsupported(arm.Guard);
            }

            if (arm.Body is CodeBlockKoto block)
            {
                this.Block(block);
            }
            else
            {
                this.Statement(arm.Body);
            }

            this.Cleanup(temps, locals, syntax, CleanupReason.ScopeExit);
            this.locals.RemoveRange(locals, this.locals.Count - locals);
            this.temporaries.RemoveRange(temps, this.temporaries.Count - temps);
        }

        this.selections.RemoveAt(this.selections.Count - 1);
        this.current = -1;
    }

    private void AcquirePattern(BoundMatch plan, int index, int input, int neededStart, int arm)
    {
        if (!this.patternStorageNeeded[neededStart + index])
        {
            return;
        }

        var pattern = plan.Positions[index];
        if (pattern.AccessMode != PatternAccessMode.Owned)
        {
            for (var i = index; i < pattern.End; i++)
            {
                var binding = plan.Positions[i];
                if (binding.Kind != BoundPatternKind.Binding)
                {
                    continue;
                }

                var local = this.LocalPlace(binding.BodySymbol, binding.Source, binding.BodySymbol!.Type, Binding.IsMutableDeclaration(binding.Source), AcquisitionKind.Copy);
                this.Emit(OwnershipOperationKind.Declare, binding.Source, local);
                this.locals.Add(new(local, binding.Source, this.registrationSequence++));
                var acquire = this.Emit(OwnershipOperationKind.AcquirePattern, binding.Source, input, local, AcquisitionKind.Copy);
                this.body.OperationSteps[acquire] = arm;
                this.SetValue(acquire, OwnershipValueKind.PatternProjection, [], constant: i);
            }

            return;
        }

        if (pattern.Kind == BoundPatternKind.Binding)
        {
            var acquisition = PatternAcquisitionKind(pattern);
            this.CheckAcquisition(input, acquisition);
            var local = this.LocalPlace(pattern.BodySymbol, pattern.Source, pattern.MatchedType, Binding.IsMutableDeclaration(pattern.Source), acquisition);
            this.Emit(OwnershipOperationKind.Declare, pattern.Source, local);
            this.locals.Add(new(local, pattern.Source, this.registrationSequence++));
            // The bound Place resolved a committed CopyOrMove to the instance's exact effect (SPEC 21.3.1).
            this.Emit(OwnershipOperationKind.AcquirePattern, pattern.Source, input, local, this.body.PlaceStorage[local].Acquisition);
            return;
        }

        if (pattern.Kind is not (BoundPatternKind.Case or BoundPatternKind.Tuple))
        {
            throw new InvalidOperationException("Only owned Case or Tuple paths can precede a supported Pattern binding.");
        }

        var payloadStart = this.body.PlaceStorage.Count;
        var count = 0;
        for (var child = index + 1; child < pattern.End; child = plan.Positions[child].End)
        {
            var position = plan.Positions[child];
            var acquisition = position.Kind == BoundPatternKind.Binding ? PatternAcquisitionKind(position) : (AcquisitionKind?)null;
            var payload = this.Place(position.Source, position.MatchedType, OwnershipPlaceKind.Payload, false, acquisition);
            this.Emit(OwnershipOperationKind.Declare, position.Source, payload);
            count++;
        }

        var decomposition = this.body.DecompositionStorage.Count;
        this.PrepareDecompositionSlots();
        this.body.DecompositionStorage.Add(new(input, pattern.Case!, payloadStart, count));
        this.activeDecompositions[input] = decomposition;
        var open = this.Emit(OwnershipOperationKind.DecomposeCase, pattern.Source, input);
        this.body.OperationSteps[open] = decomposition;
        var element = 0;
        for (var child = index + 1; child < pattern.End; child = plan.Positions[child].End)
        {
            this.AcquirePattern(plan, child, payloadStart + element++, neededStart, arm);
        }
    }

    private int ReadCandidate(Koto source, PlaceUseKind use)
    {
        for (var i = this.candidates.Count - 1; i >= 0; i--)
        {
            var candidate = this.candidates[i];
            if (ReferenceEquals(candidate.Symbol, source.BoundSymbol) && use != PlaceUseKind.Borrow)
            {
                if (candidate.Position >= 0)
                {
                    var result = this.Place(source, source.BoundType, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
                    this.Emit(OwnershipOperationKind.Declare, source, result);
                    var inspect = this.Emit(OwnershipOperationKind.Read, source, candidate.Subject, result);
                    this.body.OperationSteps[inspect] = candidate.Arm;
                    this.SetValue(inspect, OwnershipValueKind.PatternProjection, [], constant: candidate.Position);
                    this.EnsureGuardProtection(candidate.Subject, candidate.Arm, candidate.Loan);

                    var produce = this.Emit(OwnershipOperationKind.Produce, source, result);
                    if (ScalarResult(source.BoundType!))
                    {
                        this.SetValue(produce, OwnershipValueKind.Alias, [inspect]);
                    }

                    this.placeValues[candidate.Subject] = candidate.Value;
                    return this.RegisterTemporary(result);
                }

                if (ReferenceTypes.IsString(source.BoundType))
                {
                    var reference = this.Place(source, source.BoundType, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
                    var inspect = this.Emit(OwnershipOperationKind.Read, source, candidate.Subject, reference);
                    this.body.OperationSteps[inspect] = candidate.Arm;
                    this.EnsureGuardProtection(candidate.Subject, candidate.Arm, candidate.Loan);

                    return this.RegisterTemporary(reference);
                }

                if (source.BoundType is { Kind: BoundTypeKind.Semantics, Semantics: SemanticsKind.Ref, Components.Count: 1 } candidateReference &&
                    this.body.Places[candidate.Subject].Type is var subjectType && ReferenceEquals(subjectType, this.Resolve(candidateReference.Components[0], this.Active)))
                {
                    // SPEC 14.8.3: a candidate of a whole owned Subject is a shared reference to the Subject Place; a
                    // Scalar value is materialized in the Subject's slot at the borrow.
                    var reference = this.Place(source, candidateReference, OwnershipPlaceKind.Temporary, false, AcquisitionKind.Copy);
                    var borrow = this.Emit(OwnershipOperationKind.Borrow, source, candidate.Subject, reference, loanMode: LoanRequirement.Ref);
                    this.body.OperationSteps[borrow] = candidate.Arm;
                    this.SetValue(borrow, OwnershipValueKind.Address, ScalarTypes.Supports(subjectType) ? [candidate.Value] : [], constant: candidate.Subject);
                    this.EnsureGuardProtection(candidate.Subject, candidate.Arm, candidate.Loan);
                    this.placeValues[candidate.Subject] = candidate.Value;
                    return this.RegisterTemporary(reference);
                }
            }
        }

        this.Unsupported(source);
        return -1;
    }

    private void EnsureGuardProtection(int subject, int arm, int original)
    {
        if (original < 0)
        {
            return;
        }

        for (var protection = this.CurrentLoanHead; protection >= 0; protection = this.body.ComparisonLoans[protection].Parent)
        {
            if (this.body.ComparisonLoans[protection].Guard == arm)
            {
                return;
            }
        }

        // A transfer ended the original protection. The first later read in the unreachable continuation forms
        // one fresh Loan, shared by all later reads there, so the dead operations keep a well-formed Loan stack.
        var depth = this.comparisonDepth;
        this.comparisonDepth = this.body.ComparisonLoans[original].Depth;
        this.BeginSharedLoan(subject, guard: arm);
        this.comparisonDepth = depth;
    }

    private void CleanupSubject(int place, Koto source)
    {
        if (this.activeDecompositions[place] is >= 0 and var index)
        {
            var decomposition = this.body.DecompositionStorage[index];
            for (var i = decomposition.PayloadCount - 1; i >= 0; i--)
            {
                this.CleanupSubject(decomposition.PayloadStart + i, source);
            }
        }

        this.CleanupPlace(place, this.body.PlaceStorage[place].Source, source);
    }

    private void PrepareDecompositionSlots()
    {
        // Ordinary whole-Place bodies pay no per-Place indexing cost for match state.
        while (this.activeDecompositions.Count < this.body.PlaceStorage.Count)
        {
            this.activeDecompositions.Add(-1);
        }
    }

    private bool TryGetSelection(Koto? target, out SelectionFrame frame)
    {
        for (var i = this.selections.Count - 1; i >= this.deferredSelectionBase; i--)
        {
            if (ReferenceEquals(this.selections[i].Source, target))
            {
                frame = this.selections[i];
                return true;
            }
        }

        frame = default;
        return false;
    }

    private readonly record struct SelectionFrame(Koto Source, int Result, int Join, int Locals, int Temporaries, int Comparisons);
}
