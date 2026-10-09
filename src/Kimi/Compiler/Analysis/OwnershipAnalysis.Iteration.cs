// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

public sealed partial class OwnershipAnalysis
{
    private void IterateUser(ForKoto source, BoundIteration protocol)
    {
        var plan = protocol.Decomposition;
        if (!this.SupportsMatch(plan))
        {
            this.Unsupported(source);
            return;
        }

        var localMark = this.locals.Count;
        var tempMark = this.temporaries.Count;
        var input = this.Call(source.EntryCall!);
        if (input < 0)
        {
            return;
        }

        var iterator = this.LocalPlace(protocol.Iterator.BoundSymbol, protocol.Iterator, protocol.Iterator.BoundType, true);
        this.locals.Add(new(iterator, protocol.Iterator, this.registrationSequence++));
        this.Emit(OwnershipOperationKind.Declare, source, iterator);
        this.Emit(OwnershipOperationKind.Write, source, iterator, input);

        // SPEC 3.6.2: the iteration source's temporaries last for its required use, as a match Subject's do; they are destroyed after
        // the iterator, which registered later, when the loop is left.
        var loopTempMark = this.temporaries.Count;
        var head = this.Emit(OwnershipOperationKind.Branch, source);
        var exit = this.New(OwnershipOperationKind.Branch, source);
        var bindingMark = this.locals.Count;
        this.loops.Add(new(source, head, exit, bindingMark, loopTempMark, Comparisons: this.comparisonDepth));
        var next = this.Call(protocol.Next);
        if (next < 0)
        {
            this.loops.RemoveAt(this.loops.Count - 1);
            return;
        }

        var subject = this.Place(protocol.Next, protocol.Next.BoundType, OwnershipPlaceKind.Subject, false, this.body.Places[next].Acquisition);
        this.PrepareDecompositionSlots();
        this.Emit(OwnershipOperationKind.Declare, protocol.Match, subject);
        this.Emit(OwnershipOperationKind.InitializeSubject, protocol.Match, subject, next);
        this.temporaries.Add(new(subject, protocol.Next, this.registrationSequence++, true));
        var matchIndex = this.body.MatchStorage.Count;
        var armStart = this.body.MatchArmStorage.Count;
        this.body.MatchStorage.Add(new(plan, subject, -1, armStart, 2));
        this.body.MatchArmStorage.Add(default);
        this.body.MatchArmStorage.Add(default);
        var dispatch = this.Emit(OwnershipOperationKind.MatchDispatch, protocol.Match, subject);
        this.body.OperationSteps[dispatch] = matchIndex;
        var neededStart = this.patternStorageNeeded.Count;
        for (var i = 0; i < plan.Positions.Count; i++)
        {
            // Every delivered binding, including _, has a lifetime and cleanup responsibility.
            this.patternStorageNeeded.Add(i < plan.Arms[1].Pattern);
        }

        var test = this.New(OwnershipOperationKind.PatternTest, plan.Arms[0].Syntax.Pattern, subject);
        this.body.OperationSteps[test] = armStart;
        this.Connect(dispatch, test, OwnershipEdgeKind.MatchArm);
        var enter = this.New(OwnershipOperationKind.Branch, source.Body);
        this.Connect(test, enter, OwnershipEdgeKind.True);
        this.current = enter;
        var decomposition = this.body.DecompositionStorage.Count;
        this.AcquirePattern(plan, 0, subject, neededStart, armStart);
        this.body.MatchArmStorage[armStart] = new(matchIndex, 0, test, decomposition, this.body.DecompositionStorage.Count - decomposition, BodyEntry: enter);
        var seeds = this.terminalSeeds.Count;
        this.Block(source.Body, out var continuation);
        this.RecordTerminalSeed(source.Body, continuation);
        this.FilterTerminalSeeds(source, seeds);
        this.Cleanup(loopTempMark, bindingMark, source, CleanupReason.ScopeExit);
        this.Connect(this.current, head, OwnershipEdgeKind.Back);
        this.locals.RemoveRange(bindingMark, this.locals.Count - bindingMark);

        // None owns no payload; its path ends the step before destroying the iterator remainder.
        this.activeDecompositions[subject] = -1;
        var none = plan.Arms[1];
        var exhausted = this.New(OwnershipOperationKind.PatternTest, none.Syntax.Pattern, subject);
        this.body.OperationSteps[exhausted] = armStart + 1;
        this.Connect(test, exhausted, OwnershipEdgeKind.False);
        var done = this.New(OwnershipOperationKind.Branch, none.Syntax.Body);
        this.Connect(exhausted, done, OwnershipEdgeKind.True);
        this.current = done;
        this.body.MatchArmStorage[armStart + 1] = new(matchIndex, none.Pattern, exhausted, this.body.DecompositionStorage.Count, 0, BodyEntry: done);
        this.Cleanup(loopTempMark, bindingMark, source, CleanupReason.ScopeExit);
        this.Connect(this.current, exit);
        this.temporaries.RemoveRange(loopTempMark, this.temporaries.Count - loopTempMark);
        this.patternStorageNeeded.RemoveRange(neededStart, this.patternStorageNeeded.Count - neededStart);
        this.loops.RemoveAt(this.loops.Count - 1);
        this.current = exit;
        this.Cleanup(tempMark, localMark, source, CleanupReason.ScopeExit);
        this.temporaries.RemoveRange(tempMark, this.temporaries.Count - tempMark);
        this.locals.RemoveRange(localMark, this.locals.Count - localMark);
    }
}
