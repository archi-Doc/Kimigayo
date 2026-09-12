// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private readonly List<SelectionFrame> selections = new();
    private readonly List<int> activeDecompositions = new();
    private readonly List<bool> patternStorageNeeded = new();

    private static AcquisitionKind PatternAcquisitionKind(BoundPattern pattern) => pattern.Acquisition switch
    {
        PatternAcquisition.Copy => AcquisitionKind.Copy,
        PatternAcquisition.Move => AcquisitionKind.Move,
        _ => throw new InvalidOperationException("Pattern acquisition must be committed before ownership construction."),
    };

    private bool SupportsMatch(BoundMatch plan)
    {
        if (!plan.IsCurrent || plan.Coverage.State is not (MatchCoverageState.Exhaustive or MatchCoverageState.NonExhaustive))
        {
            return false;
        }

        if (plan.Syntax.BoundType is not { } result || (!ReferenceEquals(result, BoundType.Never) && !this.SupportsType(result)))
        {
            return false;
        }

        for (var i = 0; i < plan.Arms.Count; i++)
        {
            if (plan.Arms[i].Syntax.Guard is not null)
            {
                return false;
            }
        }

        for (var i = 0; i < plan.Positions.Count; i++)
        {
            var position = plan.Positions[i];
            if (position.AccessMode != PatternAccessMode.Owned || position.ImplicitDeref != PatternImplicitDeref.None ||
                position.Acquisition == PatternAcquisition.Deferred || !this.SupportsType(position.MatchedType) ||
                (position.Kind == BoundPatternKind.Binding && position.Acquisition is not (PatternAcquisition.Copy or PatternAcquisition.Move)))
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
            this.current = -1;
            return -1;
        }

        if (!this.compilation.Binding.TryGetMatch(syntax, out var plan) || !this.SupportsMatch(plan!))
        {
            this.Unsupported(syntax);
            return -1;
        }

        var input = this.Expression(syntax.Expression);
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
        var output = this.Place(syntax, syntax.BoundType, OwnershipPlaceKind.Result, true);
        this.Emit(OwnershipOperationKind.Declare, syntax, output);
        var subject = this.Place(syntax.Expression, syntax.Expression.BoundType, OwnershipPlaceKind.Subject, false, this.body.PlaceStorage[input].Acquisition);
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
        var join = this.New(OwnershipOperationKind.Branch, syntax);
        this.selections.Add(new(syntax, output, join, localMark, tempMark));

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

        for (var i = 0; i < plan.Arms.Count; i++)
        {
            var arm = plan.Arms[i];
            this.activeDecompositions[subject] = -1;
            this.current = this.New(OwnershipOperationKind.PatternTest, arm.Syntax.Pattern, subject);
            var test = this.current;
            this.body.OperationSteps[test] = armStart + i;
            // This is the SPEC verification graph, not runtime first-match dispatch.
            // Warning-covered arms receive the same intact Subject state too.
            this.Connect(dispatch, test, OwnershipEdgeKind.MatchArm);
            var decompositionStart = this.body.DecompositionStorage.Count;
            this.AcquirePattern(plan, arm.Pattern, subject, neededStart);
            this.body.MatchArmStorage[armStart + i] = new(matchIndex, arm.Pattern, test, decompositionStart, this.body.DecompositionStorage.Count - decompositionStart);

            if (arm.Syntax.Body is CodeBlockKoto block)
            {
                this.Block(block);
                if (this.flow.Nodes[block].CanCompleteNormally)
                {
                    this.Emit(OwnershipOperationKind.Produce, block, output);
                }
                else
                {
                    this.current = -1;
                }
            }
            else
            {
                var value = -1;
                if (arm.Syntax.Body is ExpressionKoto && (arm.Syntax.Body is not UnitLiteralKoto || KotoHelper.IsValueContext(syntax)))
                {
                    value = this.Expression(arm.Syntax.Body);
                }
                else
                {
                    this.Statement(arm.Syntax.Body);
                }

                if (this.flow.Nodes[arm.Syntax.Body].CanCompleteNormally)
                {
                    if (KotoHelper.IsValueContext(syntax) && arm.Syntax.Body is ExpressionKoto)
                    {
                        this.Emit(OwnershipOperationKind.Write, arm.Syntax.Body, output, value);
                    }
                    else
                    {
                        this.Emit(OwnershipOperationKind.Produce, arm.Syntax.Body, output);
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
                this.Connect(this.current, join);
            }

            this.locals.RemoveRange(localMark, this.locals.Count - localMark);
            this.temporaries.RemoveRange(tempMark + 1, this.temporaries.Count - tempMark - 1);
        }

        this.activeDecompositions[subject] = -1;
        if (plan.Coverage.State == MatchCoverageState.NonExhaustive)
        {
            this.current = this.New(OwnershipOperationKind.Branch, syntax);
            this.Connect(dispatch, this.current, OwnershipEdgeKind.Unmatched);
            this.Cleanup(tempMark, localMark, syntax, CleanupReason.ScopeExit);
            this.Emit(OwnershipOperationKind.Produce, syntax, output);
            this.Connect(this.current, join);
        }

        this.patternStorageNeeded.RemoveRange(neededStart, this.patternStorageNeeded.Count - neededStart);
        this.temporaries.RemoveRange(tempMark, this.temporaries.Count - tempMark);
        this.selections.RemoveAt(this.selections.Count - 1);
        if (!this.flow.Nodes[syntax].CanCompleteNormally)
        {
            this.current = -1;
            return -1;
        }

        this.current = join;
        return this.RegisterTemporary(output);
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
            var local = this.Place(pattern.Source, pattern.MatchedType, OwnershipPlaceKind.Local, pattern.Source is SyntaxFormKoto { IsMutablePattern: true }, acquisition);
            this.body.SymbolPlaces[pattern.BodySymbol!] = local;
            this.Emit(OwnershipOperationKind.Declare, pattern.Source, local);
            this.locals.Add(new(local, pattern.Source, this.registrationSequence++));
            this.Emit(OwnershipOperationKind.AcquirePattern, pattern.Source, input, local, acquisition);
            return;
        }

        if (pattern.Kind != BoundPatternKind.Case)
        {
            throw new InvalidOperationException("Only owned Case paths can precede a supported Pattern binding.");
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
        for (var i = this.selections.Count - 1; i >= 0; i--)
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

    private readonly record struct SelectionFrame(Koto Source, int Result, int Join, int Locals, int Temporaries);
}
