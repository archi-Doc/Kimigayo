// Copyright (c) All contributors. All rights reserved. Licensed under the MIT license.

// SPEC 15.6.5's classes of an Origin in a body B, as Binding's judge and the C1-F4 view read them: fixed by the contract (FixedOrigin), fixed
// in B (FixedIn), a region B judges through its sources (RegionIn), an enclosing body's region B reads as its value (CarriedRegion, from
// the capture formation's fact, in turn through the regions' values), an Origin B judges through the sources the region graph records
// (SourcesIn) or may bound (BoundableIn), a finite Origin local to B (BodyLocalIn), a relation that takes part in region inference
// (ConstrainsRegions, RegionChain), a Refuted relation (RefutesOriginRelation) and a pending atom (PendingAtom). They share one rule, the
// view's row 6 (EnclosingAtom; 2a0-D section 3, DECISIONS R4-M101 E5, BU-28): in a Closure body the Origins of its captures, atoms an
// enclosing body formed, are fixed; in a Closure k's body and the bodies nested in it, that row also fixes k's receiver Origin ρ (SPEC
// 15.6.5: a receiver Origin), and until k is classified ρ and k's environment atoms are pending, neither fixed nor finite (DECISIONS
// R4-M126 P5); in the declaration judge's frame (ExecutableBody.Declarations) every body's atoms are, an enclosing body's Origin a
// declaration names being a fixed Origin of it (SPEC 15.3.4; DECISIONS BU-89); in a named function's body, through its declaring body
// (BindingSymbol.DeclaringBody; a root function's is its module's top-level statements), the atoms of the bodies that declare it are
// fixed with no premise. The view's row 5 (OwnCallAtom), B's own call receiver and call result, is another such shared rule.
// - Inputs: an Origin as the caller has it at its use, and the body that formed the relation (an obligation's Body, or the forming body
//   while binding; no body after binding); the formation context (BoundOrigin.Body, BindingSymbol.DeclaringBody, Binding.ExecutableBodies.cs)
//   and whether a Closure is classified (BoundClosure.Classified).
// - Outputs: the class, read by the judge (Binding.OriginProof.cs, Binding.LocalRegions.cs), the row former (FormBodyRelation,
//   Binding.Obligations.cs), the declaration judge's records (Binding.OriginClauses.cs) and the view (Binding.OriginRows.Atoms.cs,
//   ClassifyAtom).
// - Invariants: each class is decided here once, by one predicate that every reader shares; only SourcesIn and CarriedRegion read state,
//   the region graph's, and the capture formation's with the regions' values (RegionValue), CarriedRegion's walk formed once per Closure
//   per region-graph version once no body is being bound (BoundClosure.Walk, its one memo), and while a body is bound its admission over
//   the own entries alone (AdmittedEntry, shared with the walk); a pending atom is tested before any other class.
// - Prohibitions: no proof, judgment or record, and no state but the walk's memo; no class derived from syntax; no walk while a body is
//   being bound (a carried candidate is complete only once its body is bound, RegionBounded).

namespace Kimi.Compiler;

public sealed partial class Binding
{
    // CarriedWalk's answer for a body that carries nothing, never written.
    private static readonly List<BoundOrigin> NoCarriedRegions = new();

    // The C1-F4 view's row 6 (2a0-D section 3): an atom of a body that B's evaluation reaches, B's Closure's evaluating body and so on
    // outward, by the body that formed it or its slot's role (RootOf); in the declaration judge's frame, any body's atom (BU-89).
    internal static bool EnclosingAtom(BoundOrigin atom, ExecutableBody body)
    {
        if (body == ExecutableBody.Declarations)
        {
            return atom.Body.IsBody;
        }

        var role = RoleOf(atom);
        for (var outer = EvaluatingBody(body); outer.IsBody; outer = EvaluatingBody(outer))
        {
            if (RootOf(atom, role, outer))
            {
                return true;
            }
        }

        return false;
    }

    // The C1-F4 view's row 5 (2a0-D section 3; DECISIONS R4-M152): B's own call receiver ρ, once it is not pending, and B's call result:
    // a Closure body's receiver Origin and the call result its published result may name.
    internal static bool OwnCallAtom(BoundOrigin atom, ExecutableBody body)
        => RoleOf(atom) is ProjectionRole.ClosureValue or ProjectionRole.CallResult && DeclaredBy(atom, body) && !PendingAtom(atom);

    // SPEC 15.6.5: an Origin fixed by the contract: a signature, parameter or receiver Origin, a projection of one, static, or a meet of them.
    internal static bool FixedOrigin(BoundOrigin origin)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedOrigin(origin.Operands[i]))
                {
                    return false;
                }
            }

            return true;
        }

        return origin.Kind is OriginKind.Static or OriginKind.Parameter or OriginKind.Input or OriginKind.ConstraintSet;
    }

    // SPEC 15.6.5: an Origin fixed in `body`: by the contract, as `body`'s own call receiver or call result (the view's row 5), or as an
    // enclosing body's atom (a Closure's captures, its receiver Origin ρ and call result in nested bodies) once it is not pending; a meet
    // when each operand is.
    internal static bool FixedIn(BoundOrigin origin, ExecutableBody body)
    {
        if (origin.Kind == OriginKind.Intersection)
        {
            for (var i = 0; i < origin.Operands.Count; i++)
            {
                if (!FixedIn(origin.Operands[i], body))
                {
                    return false;
                }
            }

            return true;
        }

        return FixedOrigin(origin) || OwnCallAtom(origin, body) || (EnclosingAtom(origin, body) && !PendingAtom(origin));
    }

    // SPEC 15.8.2, 15.6.5 (DECISIONS R4-M126 P5): a Closure's receiver Origin ρ or an environment binding's own atom while the Closure
    // is not classified: its receiver mode, which resolves them (Binding.OriginProof's OriginAtUse), is not known yet, so it is neither
    // fixed nor finite and a relation naming it is a row like any other (ConstrainsRegions; DECISIONS R7-M66).
    internal static bool PendingAtom(BoundOrigin atom)
        => ReceiverAtom(atom) && atom.Binder is Parsing.FunctionKoto { ClosureStorage: { Classified: false } };

    // SPEC 15.8.2 (DECISIONS R4-M126 P2): an atom of a Closure's call receiver, the Closure value ρ or an environment binding's own atom,
    // which the receiver resolution reads and a contract record shows as the closure's call receiver.
    internal static bool ReceiverAtom(BoundOrigin atom)
        => atom is { Kind: OriginKind.Projection, Binder: Parsing.FunctionKoto { IsAnonymous: true } } &&
            RoleOf(atom) is ProjectionRole.ClosureValue or ProjectionRole.Environment;

    // SPEC 15.3.6, 15.6.5: a local region that `body` judges through its sources; an enclosing body's region is fixed in it.
    internal static bool RegionIn(BoundOrigin origin, ExecutableBody body) => IsLocalRegion(origin) && !EnclosingAtom(origin, body);

    // SPEC 15.6.5: an Origin a relation formed in `body` may bound in the region graph: a local region, a Borrow's Origin or a meet, unless
    // it is an enclosing body's atom, fixed in `body`.
    internal static bool BoundableIn(BoundOrigin origin, ExecutableBody body)
        => (IsLocalRegion(origin) || origin.Kind is OriginKind.Projection or OriginKind.Anchor or OriginKind.Intersection) && !EnclosingAtom(origin, body);

    // SPEC 15.6.5: whether a relation in `body` takes part in region inference: an end, as the caller has it, not fixed in `body` (finite,
    // an inferred region, a pending atom, or Unbound, whose chains A5 judges through it, R4-M118) or a region at either end (RegionChain);
    // it is a row whatever the premises answer, and any other relation is between fixed Origins, decided by the premises alone (SPEC
    // 15.6.1).
    internal static bool ConstrainsRegions(BoundOrigin longer, BoundOrigin shorter, ExecutableBody body)
        => !FixedIn(longer, body) || !FixedIn(shorter, body) || RegionChain(longer, shorter, body);

    // SPEC 15.3.6, 15.6.5: a relation in `body` with a local region at either end, an enclosing body's, fixed there, included: the region's
    // value is known only once every body is bound, so A5 alone reads it (UniversalRelations: a region `body` carries equals its value)
    // and Binding leaves a row; the judge answers Unknown for it (JudgeOriginAtoms).
    internal static bool RegionChain(BoundOrigin longer, BoundOrigin shorter, ExecutableBody body)
        => body.IsBody && (HasLocalRegion(longer) || HasLocalRegion(shorter));

    // SPEC 15.6.1: whether a relation is Refuted in `body`: its longer end is a finite Origin local to it and its shorter end fixed in it, or
    // its longer end is Unstated (UnstatedRelation).
    internal static bool RefutesOriginRelation(BoundOrigin longer, BoundOrigin shorter, ExecutableBody body)
        => UnstatedRelation(longer, shorter) == false || (BodyLocalIn(longer, body) && FixedIn(shorter, body));

    // SPEC 15.6.5: an Origin that `body` judges through the sources the region graph records for it; an enclosing body's atom, fixed in
    // `body`, is never followed.
    internal bool SourcesIn(BoundOrigin origin, ExecutableBody body) => this.HasRegionBounds(origin) && !EnclosingAtom(origin, body);

    // SPEC 15.6.5 (its Closure sentence and last clause; DECISIONS R7-O2 A1), 7.7.2, 15.3.6 (O-466 (b); D-993's bullet: a region reached
    // through a carried region's candidate is read in turn): whether the body of `function`, a Closure, carries `region`, a local region
    // of an enclosing body, and what it reads it as (`value`, its RegionValue; null while it has no answer yet, RegionBounded). The one
    // reading of carried regions, read once no body is being bound (Final, A5; the owner's O-471 (a)): the Closure's walk (CarriedWalk).
    // While a body is bound no walk is read, so no reader sees a set formed from incomplete candidates: an own entry's region answers when
    // the walk's admission admits it (AdmittedEntry: in turn through the evaluating Closures' own entries, each recorded before its header,
    // R7-O2 O2-H, the owner's O-487 (a)), which reads no candidate; its `let` value is complete at its declaration (SPEC 15.4.4) and its
    // `var` candidate has no answer yet. A region reached in turn through a candidate waits for the walk. A Closure that captures no
    // binding naming the region carries nothing, though it is evaluated in one that does, and a region a nested signature only names is
    // not carried by that name: either stays a fixed Origin with no premise (SPEC 15.3.4). Readers: the view's row 6 Value, StaticOrigin
    // (ValueIn; a Closure's header and body, O-443 (a)) and the walk's admission.
    internal bool CarriedRegion(BoundOrigin region, Parsing.Koto? function, out BoundOrigin? value)
    {
        value = null;
        if (!SlotRegion(region) || function is not Parsing.FunctionKoto { IsAnonymous: true, ClosureStorage: { } plan } ||
            !(this.formingBody.IsBody ? plan.Carried.Contains(region) && this.AdmittedEntry(this.symbols[function].DeclaringBody, region) : this.CarriedWalk(function).Contains(region)))
        {
            return false;
        }

        value = this.RegionValue(region);
        return true;
    }

    // The regions the body of `function` carries (CarriedRegions), formed once per Closure per region-graph version (BoundClosure.Walk,
    // WalkVersion) and read by CarriedRegion and the body's oracle (D3: one walk); none for a body that is no Closure's, and none while a
    // body is being bound (no walk is read then).
    internal List<BoundOrigin> CarriedWalk(Parsing.Koto? function)
    {
        if (this.formingBody.IsBody || function is not Parsing.FunctionKoto { IsAnonymous: true, ClosureStorage: { } plan })
        {
            return NoCarriedRegions;
        }

        if (plan.WalkVersion != this.regionVersion)
        {
            this.CarriedRegions(plan, this.symbols[function].DeclaringBody, plan.Walk);
            plan.WalkVersion = this.regionVersion;
            this.OriginProofMetrics?.CarriedWalk();
        }

        return plan.Walk;
    }

    // PLAN G936's J2: whether `value` is, or has as a meet operand, a local region that is no region slot (a call's open region), whose
    // bounds the waiting part gives after selection: a carried region with such a value is read as it (A5's split meets that region as
    // the located limit, MirOrigins.NestedLimit) but no region is carried in turn through it, and the body's oracle does not equate it
    // (UniversalRelations.Value), so that no premise proves what the limit leaves open, until G936's late solve.
    private static bool NamesOpenRegion(BoundOrigin value)
    {
        for (var i = 0; i < (value.Kind == OriginKind.Intersection ? value.Operands.Count : 1); i++)
        {
            var atom = value.Kind == OriginKind.Intersection ? value.Operands[i] : value;
            if (IsLocalRegion(atom) && !SlotRegion(atom))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.5: a finite Origin local to `body` (a Borrow's), or a meet with one; an enclosing body's Borrow is fixed in it, a pending
    // atom neither.
    private static bool BodyLocalIn(BoundOrigin origin, ExecutableBody body)
    {
        if (origin.Kind is OriginKind.Projection or OriginKind.Anchor)
        {
            return !EnclosingAtom(origin, body) && !PendingAtom(origin);
        }

        for (var i = 0; i < origin.Operands.Count; i++)
        {
            if (BodyLocalIn(origin.Operands[i], body))
            {
                return true;
            }
        }

        return false;
    }

    // SPEC 15.6.5 (the binding captured in the region's own body or in a Closure body that carries the region in turn): whether the walk
    // admits `region`, a region an own entry of a Closure names whose evaluating body is `evaluating`: its body is the evaluating body, or
    // the evaluating body's Closure carries it (CarriedRegion of that Closure: while a body is bound its own admission, at Final its walk).
    // The walk's answer is a superset of the while-bound one, so a while-bound "carried" is never withdrawn at Final.
    private bool AdmittedEntry(ExecutableBody evaluating, BoundOrigin region)
    {
        this.OriginProofMetrics?.CarriedAdmission();
        return region.Body == evaluating || this.CarriedRegion(region, evaluating.Definition?.Declaration, out _);
    }

    // CarriedRegion's walk, into `carried` (cleared first), read only once no body is being bound (CarriedWalk): each region its Closure's
    // own entries name (BoundClosure.Carried, the capture formation's fact) that the walk admits (AdmittedEntry), at read time, when every
    // candidate it reads is complete; then, in turn, each region slot named by the value of one (RegionValue), unless that value names a
    // call's open region (NamesOpenRegion); each region once.
    private void CarriedRegions(BoundClosure plan, ExecutableBody evaluating, List<BoundOrigin> carried)
    {
        carried.Clear();
        for (var i = 0; i < plan.Carried.Count; i++)
        {
            if (this.AdmittedEntry(evaluating, plan.Carried[i]))
            {
                carried.Add(plan.Carried[i]);
            }
        }

        for (var i = 0; i < carried.Count; i++)
        {
            if (this.RegionValue(carried[i]) is not { } value || NamesOpenRegion(value))
            {
                continue;
            }

            for (var j = 0; j < (value.Kind == OriginKind.Intersection ? value.Operands.Count : 1); j++)
            {
                var atom = value.Kind == OriginKind.Intersection ? value.Operands[j] : value;
                if (SlotRegion(atom) && !carried.Contains(atom))
                {
                    carried.Add(atom);
                }
            }
        }
    }
}
