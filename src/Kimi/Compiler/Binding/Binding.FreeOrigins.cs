// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

using Kimi.Compiler.Parsing;

namespace Kimi.Compiler;

// Rule E (SPEC 15.6.4 steps 1 and 3, 15.3.6; DECISIONS BU-104): a judged operation's free Origins are eliminated from the input premises
// its judgment judges.
// - Responsibility: the free Origins of one judged operation, decided once from its final solution (BeginFreeOrigins), and its input
//   premises judged with them eliminated: Π, the premises the Require visit meets over every position it judges (a Signature leaf, an
//   Evidence leaf where its fit replaced an Origin, each default the call uses, a Type's own clauses).
// - Inputs: the callee and its final Origin slots; each premise of the Require visit (VisitWellFormedPremise's judging branch and
//   VisitTypeClause's Require branch) as its pattern's two ends with their instantiated values, at its position (Eliminates).
// - Outputs: the relations judged after the operation's last position (EndFreeOrigins, JudgeCallPosition): for a premise `L outlives S`
//   whose L alone is free, `X outlives S` at its position for each end X that is not free and that Π's premises with free shorter ends
//   reach upward from L; a premise whose S is free is not judged; one without a free end is judged at once, as without the rule.
// - Invariants: a free Origin is an Origin parameter of the callee that the final solution leaves without a value and its result
//   pattern does not name; an end is free only where the pattern's end is that Origin itself (a meet never is). Π and the eliminated
//   premises are kept once per entry. An operation without a free Origin collects nothing. A call within its callee's declaration has
//   none: there the callee's Origin atoms are the caller's own Origins. A Case construction and a fixed construction's re-check bind an
//   enum's or a structure's Origins, which their result names, so they have none.
// - Prohibitions: no solve, composition, row or syntax read here. Temporary, as R2' (BU-99) is, until every abstract callee Origin is a
//   fresh region of its call with every bound a constraint (option (A), SPEC 15.6.4 step 1).
public sealed partial class Binding
{
    private readonly List<FreePremise> premiseSet = new();
    private readonly List<(Koto At, FreePremise Premise, Koto? Clause, ulong Condition)> eliminatedPremises = new();
    private FunctionKoto? freeBinder;
    private SlotSet freeOrigins;
    private long premiseSetReads;

    /// <summary>Gets the premise entries Rule E read in the latest pass (Eliminates, EndFreeOrigins), which a scaling check reads.</summary>
    internal long FreePremiseWork => this.premiseSetReads;

    // A premise `Longer outlives Shorter` as the pattern's ends, with their instantiated values.
    private readonly record struct FreePremise(BoundOrigin Longer, BoundOrigin Shorter, BoundOrigin LongerValue, BoundOrigin ShorterValue)
    {
        internal FreePremise Reversed => new(this.Shorter, this.Longer, this.ShorterValue, this.LongerValue);
    }

    // The free Origins of the operation at `call` whose callee is `binder`, from its final Origin slots `origins`.
    private void BeginFreeOrigins(Koto call, Koto? binder, ReadOnlySpan<BoundOrigin> origins)
    {
        if (this.freeBinder is not null)
        {
            throw new InvalidOperationException("A judged operation's free Origins are decided once (InternalInvariant; BU-104 Rule E).");
        }

        if (binder is not FunctionKoto function || IsWithin(call, function))
        {
            return;
        }

        var result = function.BoundSymbol?.Type;
        for (var i = 0; i < function.Origins.Count; i++)
        {
            if (origins[i] is null && (result is null || !MentionsOriginSlot(result, function, i)))
            {
                if (this.freeBinder is null)
                {
                    this.freeOrigins = this.RentSlotSet(function.Origins.Count);
                    this.freeBinder = function;
                }

                this.freeOrigins.Add(i);
            }
        }
    }

    // Rule E's predicate at the Require visit: whether the premise is eliminated at `at` rather than judged as without the rule. Under a
    // free Origin every premise enters Π; one whose longer end alone is free is judged at the operation's end (EndFreeOrigins).
    private bool Eliminates(Koto at, in FreePremise premise, Koto? clause, ulong condition)
    {
        if (this.freeBinder is null)
        {
            return false;
        }

        this.premiseSetReads += this.premiseSet.Count + 1;
        if (!this.premiseSet.Contains(premise))
        {
            this.premiseSet.Add(premise);
        }

        var shorterFree = this.IsFree(premise.Shorter);
        if (shorterFree || !this.IsFree(premise.Longer))
        {
            return shorterFree;
        }

        if (!this.eliminatedPremises.Contains((at, premise, clause, condition)))
        {
            this.eliminatedPremises.Add((at, premise, clause, condition));
        }

        return true;
    }

    // Judges each eliminated premise by the ends above its free longer end, then ends the operation's free Origins.
    private void EndFreeOrigins()
    {
        if (this.freeBinder is not { } function)
        {
            return;
        }

        var reached = this.RentSlotSet(function.Origins.Count);
        try
        {
            foreach (var (at, premise, clause, condition) in this.eliminatedPremises)
            {
                reached.Clear();
                reached.Add(premise.Longer.Slot);
                this.ReachFreeOrigins(ref reached);
                foreach (var above in this.premiseSet)
                {
                    this.premiseSetReads++;
                    if (this.IsFree(above.Shorter) && reached.Contains(above.Shorter.Slot) && !this.IsFree(above.Longer))
                    {
                        var declared = clause is not null;
                        this.JudgeCallPosition(at, above.LongerValue, premise.ShorterValue, false, null, clause, declared, wellFormed: !declared, condition: condition);
                    }
                }
            }
        }
        finally
        {
            this.ReturnSlotSet(reached);
            this.ReturnSlotSet(this.freeOrigins);
            this.premiseSet.Clear();
            this.eliminatedPremises.Clear();
            this.freeBinder = null;
        }
    }

    // Adds to `reached` every free Origin above one it holds through a premise of Π whose shorter end is free.
    private void ReachFreeOrigins(ref SlotSet reached)
    {
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var above in this.premiseSet)
            {
                this.premiseSetReads++;
                if (this.IsFree(above.Shorter) && reached.Contains(above.Shorter.Slot) && this.IsFree(above.Longer) && !reached.Contains(above.Longer.Slot))
                {
                    reached.Add(above.Longer.Slot);
                    changed = true;
                }
            }
        }
    }

    private bool IsFree(BoundOrigin end)
        => end.Kind == OriginKind.Parameter && ReferenceEquals(end.Binder, this.freeBinder) && this.freeOrigins.Contains(end.Slot);
}
