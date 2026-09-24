// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<SelectionFrame> selections = new();
    private readonly List<int> activeDecompositions = new();
    private readonly List<bool> patternStorageNeeded = new();
    private readonly List<(BindingSymbol Symbol, int Subject, int Value, int Arm, int Loan, int Position)> candidates = new();
    private readonly List<CheckingContinuation> unmatchedCheckingSeeds = new();

    private static AcquisitionKind PatternAcquisitionKind(BoundPattern pattern) => pattern.Acquisition switch
    {
        PatternAcquisition.Copy => AcquisitionKind.Copy,
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

        for (var i = 0; i < plan.Arms.Count; i++)
        {
            if (plan.Arms[i].Syntax.Guard is not null && !MatchTypes.SupportsGuard(plan, plan.Arms[i].Pattern))
            {
                return false;
            }
        }

        for (var i = 0; i < plan.Positions.Count; i++)
        {
            var position = plan.Positions[i];
            if ((position.Parent < 0 && position.MatchedType.Kind == BoundTypeKind.Parameter) ||
                position.Acquisition == PatternAcquisition.Deferred || (position.MatchedType.Kind != BoundTypeKind.Parameter && !this.SupportsType(position.MatchedType)) ||
                (position.Kind == BoundPatternKind.Binding && position.Acquisition is not (PatternAcquisition.Copy or PatternAcquisition.Move or PatternAcquisition.CopyOrMove)))
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

        // SPEC 15.1.6 subject rule: an owned Non-Copy Place is shared-borrowed for the match.
        var input = plan!.SharedSubject is { } borrowed ? this.BorrowStruct(syntax.Expression, borrowed) : this.Expression(syntax.Expression);
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
        var subject = this.Place(syntax.Expression, plan.SharedSubject ?? syntax.Expression.BoundType, OwnershipPlaceKind.Subject, false, this.body.PlaceStorage[input].Acquisition);
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
        var retainChecking = this.SupportsMatchChecking(syntax) && (this.scopedCheckingProof ??= new(this)).Check(syntax);
        var fork = retainChecking && this.body.CheckingRegions[this.checkingRegion].MixedTargets &&
            !this.scopedCheckingProof!.Check(syntax, false) ? this.ForkChecking(dispatch) : null;
        var terminalMark = this.terminalSeeds.Count;
        var normalMark = this.normalCheckingSeeds.Count;
        var caughtMark = this.caughtCheckingSeeds.Count;
        var unmatchedMark = this.unmatchedCheckingSeeds.Count;
        var join = this.ResultJoin(syntax, output);
        this.selections.Add(new(syntax, output, join, localMark, tempMark, this.comparisonDepth, fork is not null && completes));

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
            var region = this.checkingRegion;
            this.activeDecompositions[subject] = -1;
            this.current = previousTest < 0 ? dispatch : previousTest;
            this.current = this.New(OwnershipOperationKind.PatternTest, arm.Syntax.Pattern, subject);
            var test = this.current;
            this.body.OperationSteps[test] = armStart + i;
            // Every nonfinal Pattern can fail in the conservative verification graph,
            // even a catch-all. False guards retain their independent side effects.
            this.Connect(previousTest < 0 ? dispatch : previousTest, test, previousTest < 0 ? OwnershipEdgeKind.MatchArm : OwnershipEdgeKind.False);
            this.Connect(previousGuard, test, OwnershipEdgeKind.False);
            if (fork is not null)
            {
                if (i == 0)
                {
                    this.EnterCheckingBranch(test, fork);
                }
                else
                {
                    // Pattern failure skips the guard; guard failure preserves
                    // its effects. Join only those histories before the next test.
                    this.JoinCheckingAt(arm.Syntax.Pattern, this.unmatchedCheckingSeeds, unmatchedMark, test);
                }

                this.AddCheckingSeed(this.unmatchedCheckingSeeds, this.Continuation());
            }

            var armFork = fork is not null ? this.ForkChecking(test) : null;
            previousTest = test;
            previousGuard = -1;
            var guardEntry = -1;
            var guardBranch = -1;
            var guardValue = -1;
            var guardCleanupStart = -1;
            var guardLoan = -1;
            var guardContinuation = new CheckingContinuation(-1);
            if (arm.Syntax.Guard is { } guard)
            {
                guardEntry = this.New(OwnershipOperationKind.Branch, guard);
                this.Connect(test, guardEntry, OwnershipEdgeKind.True);
                this.EnterCheckingBranch(guardEntry, armFork);
                var guardDepth = this.comparisonDepth++;
                if (ReferenceEquals(syntax.Expression.BoundType, BoundType.String))
                {
                    // Pure tests inspect private owned Subject storage. Once guard
                    // code can observe it, retain shared protection through cleanup.
                    this.Emit(OwnershipOperationKind.Read, guard, subject);
                    guardLoan = this.BeginSharedLoan(subject, guard: armStart + i);
                }

                var candidateMark = this.candidates.Count;
                for (var position = arm.Pattern; position < plan.Positions[arm.Pattern].End; position++)
                {
                    if (plan.Positions[position].CandidateSymbol is { } candidate)
                    {
                        this.candidates.Add((candidate, subject, this.Value(subject), armStart + i, guardLoan, position == arm.Pattern ? -1 : position));
                    }
                }

                var guardTerminalMark = this.terminalSeeds.Count;
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
                    if (fork is not null)
                    {
                        this.AddCheckingSeed(this.unmatchedCheckingSeeds, this.Continuation());
                        armFork = this.ForkChecking(guardBranch);
                    }
                }
                else
                {
                    guardContinuation = this.Continuation();
                    this.current = -1;
                }

                // Keep the normal cleanup-to-branch range contiguous for checked
                // lowering. Synthetic terminal tails are separate from that range.
                if (retainChecking)
                {
                    this.EndGuardCheckingLoans(guard, guardDepth, guardTerminalMark);
                }
            }

            var checkingBody = arm.Syntax.Guard is not null && guardBranch < 0;
            if (checkingBody)
            {
                // Check the unselected body without inventing a runtime selection.
                // A transfer's seed has its operand effects and already-ended Loans,
                // but excludes scope-exit cleanup, as required by §14.10.3.
                if (retainChecking)
                {
                    // Freeze the guard's current replay, not the pre-guard fork.
                    // A fresh region prevents later body edges from changing an
                    // already-proven replay prefix or replacing transfer extents.
                    var mark = this.normalCheckingSeeds.Count;
                    this.AddCheckingSeed(this.normalCheckingSeeds, guardContinuation);
                    if (!this.CreateCheckingJoin(arm.Syntax.Body, this.normalCheckingSeeds, mark))
                    {
                        this.checkingRegion = this.body.CheckingRegions.Count;
                        this.body.CheckingRegions.Add(new(-1, -1));
                    }
                }
                else
                {
                    var seed = this.checkingRegion != region ? this.body.CheckingRegions[this.checkingRegion].Seed : guardCleanupStart - 1;
                    this.current = seed;
                    this.checkingRegion = this.body.CheckingRegions.Count;
                    this.body.CheckingRegions.Add(new(seed, -1));
                }

                // A noncompleting guard has no selected continuation. End only
                // its protection in the checking region before inspecting the body.
                if (guardLoan >= 0 && this.CurrentLoanHead >= 0)
                {
                    var end = this.EndComparisonLoans(this.comparisonDepth, arm.Syntax.Guard!);
                    if (end >= 0)
                    {
                        this.body.CheckingRegions[this.checkingRegion] = this.body.CheckingRegions[this.checkingRegion] with { Entry = end };
                    }
                }
            }

            var bodyEntry = this.New(OwnershipOperationKind.Branch, arm.Syntax.Body);
            if (checkingBody)
            {
                if (this.body.CheckingRegions[this.checkingRegion].Entry < 0)
                {
                    this.body.CheckingRegions[this.checkingRegion] = this.body.CheckingRegions[this.checkingRegion] with { Entry = bodyEntry };
                }
                else
                {
                    this.Connect(this.current, bodyEntry);
                }
            }

            this.Connect(arm.Syntax.Guard is null ? test : guardBranch, bodyEntry, OwnershipEdgeKind.True);
            this.EnterCheckingBranch(bodyEntry, checkingBody ? null : armFork);
            var decompositionStart = this.body.DecompositionStorage.Count;
            this.AcquirePattern(plan, arm.Pattern, subject, neededStart);
            this.body.MatchArmStorage[armStart + i] = new(matchIndex, arm.Pattern, test, decompositionStart, this.body.DecompositionStorage.Count - decompositionStart, guardEntry, guardBranch, bodyEntry, guardValue, guardCleanupStart, guardLoan);

            var secured = -1;
            if (arm.Syntax.Body is CodeBlockKoto block)
            {
                this.Block(block, out var continuation, retainCheckingRegion: fork is not null);
                if (retainChecking)
                {
                    this.RecordTerminalSeed(block, continuation);
                }

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
                if (arm.Syntax.Body is ExpressionKoto && (arm.Syntax.Body is not UnitLiteralKoto || KotoHelper.IsResultRequiringSelection(syntax)))
                {
                    value = this.Expression(arm.Syntax.Body);
                }
                else
                {
                    this.Statement(arm.Syntax.Body);
                }

                if (this.flow.Nodes[arm.Syntax.Body].CanCompleteNormally)
                {
                    if (KotoHelper.IsResultRequiringSelection(syntax) && arm.Syntax.Body is ExpressionKoto)
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
                    if (retainChecking)
                    {
                        this.AddTerminalSeed(this.Continuation());
                    }

                    this.current = -1;
                }
            }

            if (this.current >= 0)
            {
                this.Cleanup(tempMark, localMark, syntax, CleanupReason.ScopeExit);
                this.ConnectResult(join, secured);
                if (checkingBody && retainChecking)
                {
                    this.AddTerminalSeed(this.Continuation());
                }
                else if (fork is not null)
                {
                    this.AddCheckingSeed(this.normalCheckingSeeds, this.Continuation());
                }
            }

            this.locals.RemoveRange(localMark, this.locals.Count - localMark);
            this.temporaries.RemoveRange(tempMark + 1, this.temporaries.Count - tempMark - 1);
            this.checkingRegion = region;
        }

        this.activeDecompositions[subject] = -1;
        this.unmatchedCheckingSeeds.RemoveRange(unmatchedMark, this.unmatchedCheckingSeeds.Count - unmatchedMark);
        this.patternStorageNeeded.RemoveRange(neededStart, this.patternStorageNeeded.Count - neededStart);
        this.temporaries.RemoveRange(tempMark, this.temporaries.Count - tempMark);
        this.selections.RemoveAt(this.selections.Count - 1);
        this.body.RecordCompletion(dispatch, join, completes);
        if (fork is not null && completes)
        {
            this.CollectCaughtChecking(syntax, caughtMark);
            this.JoinNormalChecking(syntax, normalMark, join);
        }

        if (!completes)
        {
            this.current = -1;
            if (retainChecking)
            {
                this.JoinChecking(syntax, terminalMark);
            }

            return -1;
        }

        if (retainChecking)
        {
            this.FilterTerminalSeeds(syntax, terminalMark);
        }

        return this.CompleteResult(syntax, output, join);
    }

    private void EndGuardCheckingLoans(Koto guard, int depth, int mark)
    {
        // A partial terminal guard path has no normal tail on which to end its
        // protection. Extend each exported history in a checking-only region;
        // never connect that synthetic end to the aborting/divergent runtime path.
        var current = this.current;
        var region = this.checkingRegion;
        for (var i = mark; i < this.terminalSeeds.Count; i++)
        {
            var seed = this.terminalSeeds[i];
            if (seed.Seed < 0 || this.body.LoanStates.Count == 0)
            {
                continue;
            }

            var loans = this.CheckingLoanState(seed);
            if (loans < 0 || this.body.ComparisonLoans[loans].Depth <= depth)
            {
                continue;
            }

            this.current = -1;
            this.checkingRegion = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(new(seed.Seed, -1, Target: seed.Target, Replay: seed.Replay, CaughtTarget: seed.CaughtTarget));
            this.EndComparisonLoans(depth, guard);
            this.terminalSeeds[i] = new(this.current, seed.Target, CaughtTarget: seed.CaughtTarget);
        }

        this.current = current;
        this.checkingRegion = region;
    }

    private void CheckAbandonedMatchArms(MatchKoto syntax)
    {
        // There is no Subject value. Retain each arm's checking continuation so
        // an abrupt Subject cannot hide unsupported operations or invalid uses.
        var region = this.checkingRegion;
        var seed = this.current >= 0 ? this.current : region > 0 ? this.body.CheckingRegions[region].Seed : -1;
        var temps = this.temporaries.Count;
        var locals = this.locals.Count;
        var output = this.ResultPlace(syntax);
        var join = this.ResultJoin(syntax, output);
        this.selections.Add(new(syntax, output, join, locals, temps, this.comparisonDepth));
        for (var i = 0; i < syntax.Arms.Count; i++)
        {
            var arm = syntax.Arms[i];
            this.current = -1;
            this.checkingRegion = this.body.CheckingRegions.Count;
            this.body.CheckingRegions.Add(new(seed, -1));
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
        this.checkingRegion = region;
        this.current = -1;
    }

    private void AcquirePattern(BoundMatch plan, int index, int input, int neededStart)
    {
        if (!this.patternStorageNeeded[neededStart + index])
        {
            return;
        }

        var pattern = plan.Positions[index];
        if (pattern.Kind == BoundPatternKind.Binding)
        {
            var acquisition = PatternAcquisitionKind(pattern);
            this.CheckAcquisition(input, acquisition);
            var local = this.LocalPlace(pattern.BodySymbol, pattern.Source, pattern.MatchedType, pattern.Source is SyntaxFormKoto { IsMutablePattern: true }, acquisition);
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
            this.AcquirePattern(plan, child, payloadStart + element++, neededStart);
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
                    var produce = this.Emit(OwnershipOperationKind.Produce, source, result);
                    if (ScalarTypes.Supports(source.BoundType))
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
                    var protection = this.CurrentLoanHead;
                    while (protection >= 0 && this.body.ComparisonLoans[protection].Guard != candidate.Arm)
                    {
                        protection = this.body.ComparisonLoans[protection].Parent;
                    }

                    if (protection < 0)
                    {
                        // A transfer ended the original protection. A new read in
                        // its checking continuation forms a fresh Loan, not a restore.
                        var depth = this.comparisonDepth;
                        this.comparisonDepth = this.body.ComparisonLoans[candidate.Loan].Depth;
                        this.BeginSharedLoan(candidate.Subject, guard: candidate.Arm);
                        this.comparisonDepth = depth;
                    }

                    return this.RegisterTemporary(reference);
                }

                var read = this.Emit(OwnershipOperationKind.Read, source, candidate.Subject);
                this.body.OperationSteps[read] = candidate.Arm;
                if (ScalarTypes.Supports(source.BoundType))
                {
                    this.SetValue(read, OwnershipValueKind.Alias, [candidate.Value]);
                }

                this.placeValues[candidate.Subject] = candidate.Value;
                // A Copy read acquires a value, not the Subject's responsibility.
                // Its logical temporary has no scalar alloca or physical copy.
                var value = this.Temporary(source);
                if (ScalarTypes.Supports(source.BoundType))
                {
                    this.SetValue(this.Value(value), OwnershipValueKind.Alias, [read]);
                }

                return value;
            }
        }

        this.Unsupported(source);
        return -1;
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

    private readonly record struct SelectionFrame(Koto Source, int Result, int Join, int Locals, int Temporaries, int Comparisons, bool Checking = false);
}
