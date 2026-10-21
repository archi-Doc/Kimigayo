#:property Nullable=disable
// Executable prototype of the SPEC §15.6 Loan rules (plan §4.1-§4.3) with the S1 kill option (plan §5.2).
// Run: dotnet run proto.cs        (-v prints every error fact per mode)
// Modes: PROV = provenance Loan places, no Loan kill (current SPEC with one static Loan ID per site);
//        S1   = Loans named from their base, killed when the base is replaced (an exchange/swap target counts as
//               replaced); Move of a base is deep (S1 as written);
//        S1P  = S1′, the plan's S1 (e32dc0ef §4.1-§4.3): `ended` only on destroyed_in_place (=, replace, end of storage);
//               base_op = Move, exchange/swap target, exclusive borrow other than as a `replace` target.
//        A    = design A3 (rounds 3-9) plus round 10 (final) = the chosen 1b design diff (choice-1b.md §1) with its design
//               review's changes, in A mode only:
//               G119 (B's rule): inside a reservation's window (formation to activation), a write or destruction (a
//               destroying activation included) of a prefix of the reserved Loan's Place, across borrow Derefs, reaches that
//               Loan in its reservation holder (no chain fact: G92b's fold); 33c the derived excuse at c2's reservations;
//               G110 no Loan past a shared Deref (layers from the last shared Deref on), no obs cut; B1 (replaces 39o's row)
//               the closure of CS(ℓ) seeded by the direct Loans of the value ℓ stores (own Loan and carried R2 parents)
//               into a holder live after ℓ as transit reads it, that of CS_call(ℓ, t) by those of σ_in(t); the Index of a
//               Slice handle is a shared borrow Deref (Case.Inline holds A3's inline encoding); GuardOrigin: γ_H, the
//               guard-local Origin's marker, in guard candidates only, and err(ended) only at a guard candidate's EndBorrow;
//               the Call reads its reservation holders; a destroying call's destruction checked as A3's err(destroy), at
//               the call on CS_call(ℓ, t_K) (seeded by σ_in(t_K)), for the Loans its activation's own kind does not hit
//               (review r2 F1; DestroyAsDrop = the earlier Drop on CS).
//               Cases: rounds 3-10, the case 23a review's 50-94 (EndBorrow, PrefixKill and its mutations), and the 1b
//               design review's 100-112b and its round 2's 120-127b. Exit code = S1′ failures (unchanged mode; it
//               models none of round 10's rules).
//        Round 11 (A mode; the core lane's group review B1, B2, B4; A3 E13, E14): E13, a Call's inputs are its argument
//               operands and a Value callee's operand. (1) The accesses they make AT the Call (Node.Input: a read or Move of
//               a direct Place operand the MIR leaves at the Call; Node.AcquireAt: an exclusive PairAcquire Reborrow left at
//               the Call) are checked on CS_call(ℓ, −): holders live into ℓ, none excluded, a reservation in its own holder
//               `ref`; the Call's destination write and stores keep CS(ℓ). (2) Every reserved input activates at the Call,
//               a Value callee's included (Node.Callee). (3) A PairAcquire Reborrow secured into a temp before its Call is a
//               TwoPhaseUniq reservation in that temp (Node.Acquire). E14: a Call whose contract writes an input into storage
//               reached through a raw pointer stores the input's value into E (Node.RawStore: Raw.initialize). Variants:
//               CalleeNotActivated (B1), InputsOnCS (B2), PairAcquireLive (the live Uniq encoding), NoRawStore (B4),
//               Implemented1b (all four, the reviewed branch), SecureAcquire (round 11's finding: every PairAcquire that a
//               later input of its Call left at the Call follows is secured into a temp; Case.Secured).
//        Round 12 (A mode; A3 E13 as the owner chose on 2026-10-18, option R1: A5 handles operand evaluation order; the E13
//               design review's cases 134a-135c and its AlwaysSecure, merged first): (1) every operation's operands left at
//               ℓ (Node.Input, in evaluation order, SPEC 12.2: a receiver or Value callee first; Node.Operation marks an
//               operation other than a Call) are checked on CS_i(ℓ) = closure(σ_in of the holders live after ℓ that ℓ does not
//               overwrite, val_in of every earlier operand (attributed to its root's holder for mode), σ_in of every later
//               operand's root, σ_in(E)); the destination write and the stores keep CS(ℓ). (3) M′: every PairAcquire is
//               secured into its own temp (Case.Always, else Case.Secured, else the case), a marked one (an invocation input)
//               a reservation there (Node.Acquire), an unmarked one (a let, a control-flow result, an aggregate operand, a
//               capture) a live Uniq Borrow. Variants: Round11 (round 11's E13 on round 11's MIR), AcquireAtOperation (E13 (1)
//               on G123's MIR), ShapeReservation, IntrinsicAllOperands, NoEarlierValues, NoLaterRoots, EarlierRoots.
//               Planning session on the round's findings: CS_i holds val_in(o_j) for j ≤ i, the operand's own value included
//               (NoOwnValue: E13 as first stated, j < i), and a single-operand rvalue's operand (a Use of a Copy or Move, a Cast,
//               a UnaryOp; not a Ref) is checked against its own value on CS(ℓ) too (OwnValueEverywhere: the term on every
//               node Read or Move, an upper bound).
//        Round 13 (A mode; A3 E13 as rewritten after its second design review, the owner's "one rule for every access"; the
//               second design review's cases and variants merged first): each access at ℓ is checked on closure(σ_in of the
//               holders live after ℓ that ℓ does not overwrite ∪ σ_in(E) ∪ T), T by the access: an operand's as round 12; an
//               activation of a reservation in t and a destroying call's destroy: the values of the operands other than t (not
//               CS_call's live_in), seeded by σ_in(t); a Ref's or Reborrowing PairAcquire's borrow of X: its R2 parents as E3
//               bounds them, ⋃_{m≤j<k} content^j(σ_in(root(X))) (m the index of the last shared Deref, else 0), whatever its
//               destination's liveness, and E5's seed (no transit); an observing destruction of X: val(X) (no transit: MIR's
//               Drop is a terminator, the store follows it); the destination write and the stores: transit and E5's seed.
//               E13 (2): the MIR leaves Place operands in evaluation order (named arguments, a setter's value; captures by the
//               same rule). THE DESIGN is design A with E8's ending (PrefixKill): Analysis(c, Mode.A, x, prefixKill: true);
//               Abl.None alone keeps A3 §3's base kill (last base). Variants: NoDestroyValue, BorrowSeedOnly (R4-M57),
//               ParentsAsTransit (the review's RefParentsInTransit with a seed whatever the liveness), ActivationLiveIn,
//               CurrentBuilder (Case.Built: today's builder), KeepCSTerms, BorrowWithoutSeed (the round's first run: the borrow
//               row without E5's seed accepts 41o), ParentsEveryLayer (⋃_{j<k}) and OnePointDrop (Case.OnePoint).
//        Round 14 (the design; group 1c's 26b, DECISIONS R4-M68): an activation, deep or destroying in place, conflicts only
//               with Loans whose mode is `ref` in the holder where it is found. An exclusive Loan that hits the reservation was
//               met where the later Borrow was formed or activated, so each (Loan, reservation) is reported once (SPEC 15.6.7);
//               a destroying call's destroy keeps every mode. Variants: ActivationEveryMode (round 13's err(activate)) and
//               NoFormationReport (rejected: the formation reports nothing, the activation every mode).
//        Round 15 (PLAN G148; round 14 of its design reviews, merged from their union prototype unchanged): a Call's store row
//               retains(c, i, κ, store j) into the holders W reaches (Node.Store, Analysis.StoreTargets), every designer's rule a
//               variant; R14G is the decided rule G (DECISIONS R4-M73, A3 E16). Its cases (r14A-…, r14B-…, r14V-…) join Cases.All(),
//               so the earlier rounds' tables run them under their own store rule, the one-layer refs(val(a_j)) (806110fc), and
//               round 13's table lists their 22 unsound acceptances there. -r15 prints the round.
//        Round 16 (PLAN G148's final review B1; the owner's route taint, 2026-10-19): on R14G, P13's interim (a value a call stores
//               only past a Case.ByArgument holder carries the marker τ13; ruling (B)) and R4-M70's default taint (Node.DefaultTaint:
//               τD:D on the value a default places into its pending slot D), with a located Unsupported_Kd (Analysis.Unsupported) for
//               a conflict whose Loan only tainted sets hold. R16Set is the unit as reviewed (a holder is clean where its own set
//               carries no taint; err(ended) unchecked); R16Route the owner's rule (a holder the closure reaches only through tainted
//               sets is tainted, with its route's first taint, and err(ended)'s γ is checked the same way); R16RouteNoGamma and
//               R16GammaOnly drop one half each. -r16 prints the round; draft/Sketches/A5 Step 2/route-taint.md records it.
//               Superseded by round 17 (its review found writes through a reference and E5's seed passing no taint).
//        Round 17 (round 16's review B1; the owner's body hold, 2026-10-19): on R14G, R4-M70's default taint as in round 16 (markers, the
//               set rule) and no P13 marker. R17Hold: a body is held where, at the fixpoint, some store row puts a non-empty value into a
//               target reached only past a Case.ByArgument holder, and every Loan problem of a held body (err(ended) included) is a
//               located Unsupported_Kd at its access; one the default taint already makes Unsupported_Kd keeps the omitting call.
//               R17PerPart: W takes no step from a ByArgument holder (the per-part guard's program), the reference for dependence and
//               cost. The review's cases (R16X, rv-r16/work/rx) join the round. -r17 prints it; A5 Step 2/body-hold.md records it.
//        Round 18 (ER-effects, A5-F5; unit 2x-P5 of the step-2 plan): on R17Hold, static roots ('#' names in Case.Statics, mutable or
//               not; a static's Loan names no holder and its whole write ends none, G136) and A4's keys and classes at a Call, a Drop or a
//               staticEnsure (Node.Key, Node.Fx, Node.StaticEnsure). R18: (R1) a key (f, write) meets a Loan on f in any mode, (f, read) a
//               uniq one; Unknown meets every mutable-static Loan; Limit alone is a located Unsupported_Kd where a mutable-static Loan is
//               met; Derived alone reports nothing (R4-M10, R4-M70); (R2) the Call's set is E13 (1)(b)'s with no holder left out, a Drop's
//               adds val(X) (E13 (1)(d)), and a reservation the Call activates counts as uniq at its effects (W6); (R3) a destruction of a
//               static's old value holds an exclusive Loan that its own key, or Unknown on a mutable static, meets (PD12 (iii)); (R4) a
//               live Loan on the ensured Field proves it initialized, which P5 cannot tell from another key's: a conflict that proof would
//               decide is a located Unsupported_Kd (PD12 (ii)). Every problem goes through Report, so the hold and the taint apply
//               (R4-M73). Variants: R18NoW6, R18LiveOnly, R18NoR3, R18Merged (G93's bool), R18EnsureExact (P42's key identity),
//               R18EnsureByField, R18EnsureIgnored, R18StaticEnds. R18G: SPEC 15.2.4's Transferable against HoldsRaw (G124). -r18 prints it.
//        Round 19 (ER-defaults, A5-F9; unit 2x-P9): on R18, a default template's own rules at its prepared slots (Case.Prepared): E15, the
//               PreparedInput's Deref is the slot itself (the inline segment '@'; today's shared Deref '*' is Case.PreparedShared); R26-borrow,
//               a Loan rooted at a PreparedInput in closure(σ(_0) ∪ σ(E)) at a Return is DefaultArgumentBorrow_Kd at its site; R26-access, a
//               write rooted at one past its slot through no raw pointer, and an exclusive Loan rooted at one R26-borrow did not report, are
//               DefaultArgumentAccess_Kd. At an omitting call: P9's Content rows for a template call (Node.UnknownRow: today's Value rows),
//               2e-D's raw-pointer row (Node.RawRow), and R-a, the call's inspection Loans end before its checks (Node.EndInspections), with
//               R4-M70's default taint deleted. R4-M69's Inlined drop at a copy's nodes (Node.Inlined). Variants: R19Taint, R19InlinedDrop,
//               R19NoEnding, R19SlotsOnly, R19ValueRows, R19NoRawRow, R19SharedStep, R19ReturnOnly, R19AccessAll, R19RawAccess. -r19 prints it.
//        Round 20 (template ownership of failures in inline default copies, PLAN G171; unit 2x-P11): on R19, a scope tree (Node.Scope, Case.Scopes:
//               every scope but the root a copy's Inlined scope, Outer < index) and each default's body built once as its template case and once per
//               omitting call as a copy under a prefix (R20.Ctx). R20Own: a failure is its template's where an Inlined scope contains its participating
//               scopes (the access's, the Loan's issuing and an activation's reservation's issuing scope; err(ended): the EndBorrow's and the guard
//               local's; R3: the Drop's), decided per candidate before the first is chosen (Analysis.Boundary, Omitted). The simulation check runs each
//               omission's template under the same rule. Variants: R20AccessOnly, R20IssueOnly, R20NoReservation, R20AfterSelect (Case.LoanOrder),
//               R20SameSpan, R20Entry (the round's repair candidate). -r20 prints it.
using System.Text;
using Fact = (string H, string L);

Console.OutputEncoding = Encoding.UTF8; // round 10: UTF-8 output on any console code page
var verbose = args.Contains("-v");
var modes = new[] { Mode.PROV, Mode.S1, Mode.S1P };
if (args.Length == 2 && args[0] == "-dump") // dotnet run proto.cs -- -dump 8b : per-node carries, liveness and Loan places
{
    foreach (var m in modes.Append(Mode.A)) Console.WriteLine($"== {m}\n" + new Analysis(Cases.All().Single(c => c.Id == args[1]), m).Dump());
    Console.WriteLine("== the design (A with PrefixKill)\n" + new Analysis(Cases.All().Single(c => c.Id == args[1]), Mode.A, prefixKill: true).Dump()); // round 13
    return 0;
}
// Round 13 (A3 E13 rewritten; the second design review's -r13 run, folded into named variants): THE DESIGN is design A with
// E8's ending (PrefixKill). Its totals, the last-base mode, and each round-12 and round-13 variant on the design; then the
// cases 130+ under the design and each round-13 variant. -r13 prints only this section.
Analysis Design(Case c, Abl x = Abl.None) => new(c, Mode.A, x, prefixKill: true);
var round13Variants = new[] { Abl.NoDestroyValue, Abl.BorrowSeedOnly, Abl.ParentsAsTransit, Abl.ActivationLiveIn, Abl.CurrentBuilder, Abl.KeepCSTerms, Abl.BorrowWithoutSeed, Abl.ParentsEveryLayer, Abl.OnePointDrop };
var round14Variants = new[] { Abl.ActivationEveryMode, Abl.NoFormationReport }; // round 14 (R4-M68): on the design's rows
void Round13()
{
    string Group(Analysis a, Case c) => Cls(c, a) is var s && s.StartsWith("(a)") ? "listed loss" : s;
    string Row(string name, Func<Case, Analysis> run)
    {
        var g = Cases.All().GroupBy(c => Group(run(c), c)).ToDictionary(x => x.Key, x => x.Select(c => c.Id).ToList());
        string N(string k, bool list) => g.TryGetValue(k, out var v) ? v.Count + (list ? " (" + string.Join(", ", v) + ")" : "") : "0";
        return $"| {name} | {N("same", false)} | {N("same verdict, error moves", false)} | {N("listed loss", false)} | {N("**(b) unlisted loss**", true)} | {N("**(c) UNSOUND**", true)} |";
    }
    Console.WriteLine($"\nRound 13 (E13 rewritten; {Cases.All().Count} cases):\n| Mode | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |\n| --- | ---: | ---: | ---: | ---: | ---: |");
    Console.WriteLine(Row("**The design** (design A with PrefixKill)", c => Design(c)));
    Console.WriteLine(Row("Design A (last base)", c => new Analysis(c, Mode.A)));
    foreach (var ab in round13Variants.Concat(round14Variants).Concat(new[] { Abl.NoOwnValue, Abl.NoEarlierValues, Abl.NoLaterRoots, Abl.EarlierRoots, Abl.AcquireAtOperation, Abl.ShapeReservation, Abl.IntrinsicAllOperands, Abl.OwnValueEverywhere, Abl.RefParentsInTransit, Abl.Round11, Abl.CalleeNotActivated, Abl.InputsOnCS, Abl.PairAcquireLive, Abl.NoRawStore, Abl.Implemented1b }))
        Console.WriteLine(Row("`" + ab + "`", c => Design(c, ab)));
    Console.WriteLine("\nRound 13 cases (130+) under the design:\n| Case | Program | SPEC | design | last base | " + string.Join(" | ", round13Variants) + " | class design |\n|" + string.Concat(Enumerable.Repeat(" --- |", round13Variants.Length + 6)));
    foreach (var c in Cases.All().Where(Cases.Round11))
    {
        string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
        var d = Design(c);
        var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
        Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(d)} | {W(new Analysis(c, Mode.A))} | {string.Join(" | ", round13Variants.Select(x => W(Design(c, x))))} | {Cls(c, d)} |");
    }
    foreach (var ab in round13Variants)
        Console.WriteLine($"The design with {ab}: " + string.Join("; ", Cases.All().Select(c => (c, a: Design(c, ab), b: Design(c))).Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes()))
            .Select(x => $"{x.c.Id}: {V(x.a)}" + (x.c.Err && x.a.Errors.Count == 0 ? " **UNSOUND**" : !x.c.Err && x.a.Errors.Count > 0 ? " (over-strict)" : "")).DefaultIfEmpty("no change")));
    if (verbose)
        foreach (var c in Cases.All().Where(Cases.Round11))
            Console.WriteLine($"{c.Id,-8} D   : " + (Design(c) is var a && a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
}
if (args.Contains("-r13")) { Round13(); return 0; }
// Round 15 (PLAN G148; round 14 of its design reviews), the design review's union: designer a's cases (r14A-…), designer b's (r14B-…) and the review's (r14V-…), every
// case under every store-row rule: 806110fc (Abl.None: refs(val(a_j)), one layer); designer a's A, A + X, exclusive-mode + X and route
// exclusion; designer b's A′ (R14B) and its variants; the review's G (any mode, designer b's Type guard, no exclusion: SPEC's letter
// bounded by 15.6.2), GX (G + X), GXS (G + X only where the target's Type cannot hold a reference into its own storage or into storage
// it reaches exclusively) and GDom (G + designer b's dominator exclusion). -r15 prints only this.
var round15Variants = new[] { Abl.R14A, Abl.R14AX, Abl.R14AUniqX, Abl.R14ARoute, Abl.R14B, Abl.R14BLiteral, Abl.R14BAllModes, Abl.R14BNoGuard, Abl.R14BTarget, Abl.R14BAllRoutes, Abl.R14BValueRows, Abl.R14G, Abl.R14GX, Abl.R14GXS, Abl.R14GXSTransitA, Abl.R14GDom };
var round15Main = new[] { Abl.R14A, Abl.R14AX, Abl.R14B, Abl.R14BLiteral, Abl.R14G, Abl.R14GX, Abl.R14GXS };
void Round15()
{
    string Group(Analysis a, Case c) => Cls(c, a) is var s && s.StartsWith("(a)") ? "listed loss" : s;
    string Row(string name, Func<Case, Analysis> run)
    {
        var g = Cases.All().GroupBy(c => Group(run(c), c)).ToDictionary(x => x.Key, x => x.Select(c => c.Id).ToList());
        string N(string k, bool list) => g.TryGetValue(k, out var v) ? v.Count + (list ? " (" + string.Join(", ", v) + ")" : "") : "0";
        return $"| {name} | {N("same", false)} | {N("same verdict, error moves", true)} | {N("listed loss", true)} | {N("**(b) unlisted loss**", true)} | {N("**(c) UNSOUND**", true)} |";
    }
    Console.WriteLine($"\nRound 15, review union (G148; {Cases.All().Count} cases):\n| Mode | Same | Earlier or moved error | Listed loss | Unlisted rejection | Unsound |\n| --- | ---: | ---: | ---: | ---: | ---: |");
    Console.WriteLine(Row("806110fc (refs(val(a_j)), one layer)", c => Design(c)));
    foreach (var ab in round15Variants) Console.WriteLine(Row("`" + ab + "`", c => Design(c, ab)));
    foreach (var ab in round15Variants)
    {
        var ch = Cases.All().Where(c => !c.Id.StartsWith("r14")).Where(c => !Design(c, ab).ErrorNodes().SequenceEqual(Design(c).ErrorNodes())).Select(c => c.Id).ToList();
        Console.WriteLine($"{ab}: earlier cases changed: " + (ch.Count == 0 ? "none" : string.Join(", ", ch)));
    }
    string W(Case c, Analysis a)
    {
        var v = a.Errors.Count == 0 ? "valid" : "error at `" + string.Join("`, `", a.ErrorNodes()) + "`";
        if (c.Err && a.Errors.Count == 0) v += " **UNSOUND**";
        else if (!c.Err && a.Errors.Count > 0) v += c.LetterErr == true ? " (letter)" : c.ALoss != null ? " (listed)" : " (over-strict)";
        return v;
    }
    void Table(Abl[] vs)
    {
        Console.WriteLine("\n| Case | Program | verdict | SPEC letter | 806110fc | " + string.Join(" | ", vs) + " |\n|" + string.Concat(Enumerable.Repeat(" --- |", vs.Length + 5)));
        foreach (var c in Cases.All().Where(c => c.Id.StartsWith("r14")))
        {
            var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
            var letter = (c.LetterErr ?? c.Err) ? "error" : "valid";
            Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {letter} | {W(c, Design(c))} | {string.Join(" | ", vs.Select(x => W(c, Design(c, x))))} |");
        }
    }
    Table(round15Main);
    Table(round15Variants.Except(round15Main).ToArray());
    if (verbose)
        foreach (var c in Cases.All().Where(c => c.Id.StartsWith("r14")))
            foreach (var x in round15Variants.Prepend(Abl.None))
                Console.WriteLine($"{c.Id,-9} {x,-16}: " + (Design(c, x) is var a && a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
}
if (args.Contains("-r15")) { Round15(); return 0; }
// Round 16 (PLAN G148's final review B1; the owner's route taint, 2026-10-19). On rule G (R14G) with P13's interim and R4-M70's default
// taint as markers, a conflict only tainted sets carry is a located Unsupported_Kd. R16Set is the unit as reviewed (a Loan is clean where
// a set without a taint holds it; err(ended) unchecked); R16Route the owner's rule (a holder the closure reaches only through tainted sets
// is tainted, and γ is checked so too); R16RouteNoGamma and R16GammaOnly each drop one half. Printed: the earlier rounds' cases under R14G
// and each taint mode (every outcome but r14B-u's, which marks P13's ByArgument iterator, must agree, and no mode may accept what R14G
// rejects), then the round's cases. -r16 prints only this; its exit code counts the round's failures.
var round16Variants = new[] { Abl.R16Set, Abl.R16Route, Abl.R16RouteNoGamma, Abl.R16GammaOnly };
int Round16()
{
    string O(Analysis a) => a.Errors.Count == 0 && a.Unsupported.Count == 0 ? "valid"
        : string.Join("; ", new[] { a.Errors.Count > 0 ? "error at `" + string.Join("`, `", a.ErrorNodes()) + "`" : null,
            a.Unsupported.Count > 0 ? "Unsupported_Kd at `" + string.Join("`, `", a.UnsupportedNodes()) + "`" : null }.Where(x => x != null));
    string Expected(Case c) => c.At.Length == 0 && c.UnsuppAt.Length == 0 ? "valid"
        : string.Join("; ", new[] { c.At.Length > 0 ? "error at `" + string.Join("`, `", c.At) + "`" : null,
            c.UnsuppAt.Length > 0 ? "Unsupported_Kd at `" + string.Join("`, `", c.UnsuppAt) + "`" : null }.Where(x => x != null));
    bool Meets(Case c, Analysis a) => a.ErrorNodes().ToHashSet().SetEquals(c.Err ? c.At : []) && a.UnsupportedNodes().ToHashSet().SetEquals(c.UnsuppAt);
    // Never an acceptance: every Loan (or γ) rule G reports is reported under the mode, as a Language problem or a located Unsupported_Kd.
    bool Keeps(Analysis g, Analysis a) => g.Errors.Select(e => e.L).ToHashSet().IsSubsetOf(a.Errors.Concat(a.Unsupported).Select(e => e.L));
    var earlier = Cases.All();
    var differ = earlier.Where(c => round16Variants.Any(v => O(Design(c, v)) != O(Design(c, Abl.R14G)))).ToList();
    var dropped = earlier.Where(c => round16Variants.Any(v => !Keeps(Design(c, Abl.R14G), Design(c, v)))).Select(c => c.Id).ToList();
    Console.WriteLine($"\nRound 16, route taint (PLAN G148 B1; {earlier.Count} earlier cases, {R16.All().Count()} new):");
    Console.WriteLine($"Earlier cases whose outcome under a taint mode differs from R14G's: {(differ.Count == 0 ? "none" : string.Join(", ", differ.Select(c => c.Id)))}");
    foreach (var c in differ) Console.WriteLine($"  {c.Id}: R14G {O(Design(c, Abl.R14G))}; " + string.Join("; ", round16Variants.Select(v => $"{v} {O(Design(c, v))}")));
    Console.WriteLine($"Earlier cases where a taint mode drops a Loan R14G reports: {(dropped.Count == 0 ? "none" : string.Join(", ", dropped))}");
    Console.WriteLine("\n| Case | Program | decided rule (R16Route) | R14G | " + string.Join(" | ", round16Variants) + " | meets |\n|" + string.Concat(Enumerable.Repeat(" --- |", round16Variants.Length + 5)));
    int fails = 0;
    foreach (var c in R16.All().Append(earlier.Single(x => x.Id == "r14B-u")))
    {
        var g = Design(c, Abl.R14G); var route = Design(c, Abl.R16Route);
        bool ok = Meets(c, route) && round16Variants.All(v => Keeps(g, Design(c, v)));
        fails += ok ? 0 : 1;
        Console.WriteLine($"| {c.Id} | {c.Title} | {Expected(c)} | {O(g)} | {string.Join(" | ", round16Variants.Select(v => O(Design(c, v))))} | {(ok ? "yes" : "**NO**")} |");
    }
    if (verbose)
        foreach (var c in R16.All())
            foreach (var x in round16Variants.Prepend(Abl.R14G))
                Console.WriteLine($"{c.Id,-24} {x,-16}: " + string.Join("; ", Design(c, x) is var a ? a.Errors.Select(e => $"{e.L} at `{a.LabelAt(e.P)}` vs {e.What}").Concat(a.Unsupported.Select(e => $"Unsupported {e.L} at `{a.LabelAt(e.P)}` vs {e.What}")) : []));
    Console.WriteLine($"\nRound 16 failures: {fails}");
    return fails;
}
if (args.Contains("-r16")) return Round16();
// Round 17 (round 16's independent review B1, the third round on P13's interim; the owner's body hold, 2026-10-19). R17Hold: rule G with
// R4-M70's default taint unchanged and no P13 marker; a body where some store row puts a non-empty value into a target reached only past
// a ByArgument holder is held, and each of its Loan problems (err(ended) included) is a located Unsupported_Kd at its access. Run on the
// 382 earlier cases, round 16's 13 and its review's 31 (rx-…), against R14G (rule G), R16Set, R16Route and R17PerPart (W stops at
// ByArgument holders: the per-part guard's program). Checks: (i) a conflict that depends on a by-argument store (R14G finds it and
// R17PerPart does not) is no Language problem; (ii) a body that is not held keeps R16Set's outcome; (iii) every (Loan, access) R14G
// reports is reported once under every mode, as a Language problem or a located Unsupported_Kd; (iv) the cost: each Language problem of
// R17PerPart (independent of the store) that R17Hold makes Unsupported_Kd only because its body is held. -r17 prints only this; its exit
// code counts the failures of (i)-(iii).
var round17Variants = new[] { Abl.R17Hold, Abl.R17PerPart };
int Round17()
{
    string O(Analysis a) => a.Errors.Count == 0 && a.Unsupported.Count == 0 ? "valid"
        : string.Join("; ", new[] { a.Errors.Count > 0 ? "error at `" + string.Join("`, `", a.ErrorNodes()) + "`" : null,
            a.Unsupported.Count > 0 ? "U at `" + string.Join("`, `", a.UnsupportedNodes()) + "`" : null }.Where(x => x != null));
    IEnumerable<(string, int, string)> Found(Analysis a) => a.Errors.Concat(a.Reclassified);
    bool SameMultiset(IEnumerable<(string, int, string)> x, IEnumerable<(string, int, string)> y)
        => x.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dx && y.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dy
            && dx.Count == dy.Count && dx.All(kv => dy.TryGetValue(kv.Key, out var n) && n == kv.Value);
    var all = Cases.All().Concat(R16.All()).Concat(R16X.All()).ToList();
    var modes = new[] { Abl.R16Set, Abl.R16Route, Abl.R16RouteNoGamma, Abl.R16GammaOnly, Abl.R17Hold };
    int fails = 0;
    var notKept = new List<string>(); var lost = new List<string>(); var cost = new List<string>(); var rows = new List<string>(); var heldIds = new List<string>();
    var defaultOnly = new List<string>();
    foreach (var c in all)
    {
        var g = Design(c, Abl.R14G); var set = Design(c, Abl.R16Set); var route = Design(c, Abl.R16Route);
        var hold = Design(c, Abl.R17Hold); var part = Design(c, Abl.R17PerPart);
        if (hold.Held) heldIds.Add(c.Id);
        // (i) a conflict R14G finds and the per-part guard's program does not depends on the by-argument store.
        var dependent = g.Errors.Where(e => !Found(part).Contains(e)).ToList();
        var asError = dependent.Where(e => hold.Errors.Contains(e)).ToList();
        if (asError.Count > 0) { fails++; lost.Add($"{c.Id}: " + string.Join(", ", asError.Select(e => $"{e.L} at `{g.LabelAt(e.P)}`"))); }
        // (ii) a body that is not held keeps R16Set's outcome.
        if (!hold.Held && O(hold) != O(set)) { fails++; notKept.Add($"{c.Id}: R16Set {O(set)}; R17Hold {O(hold)}"); }
        // (iii) per (Loan, access), every mode reports what R14G reports, each once.
        foreach (var m in modes)
            if (!SameMultiset(g.Errors, Found(Design(c, m)))) { fails++; lost.Add($"{c.Id}: {m} drops or duplicates a (Loan, access)"); }
        // (iv) the cost: a Language problem of the per-part program that the hold, not the default taint, makes Unsupported_Kd.
        var held = hold.Reclassified.Where(e => hold.Unsupported.Contains(e) && part.Errors.Contains(e)).ToList();
        if (held.Count > 0) cost.Add($"{c.Id}: " + string.Join(", ", held.Select(e => $"`{hold.LabelAt(e.P)}` ({e.L} vs {e.What})")));
        // The default taint's own limits, unchanged by the hold: a default store's dependent conflict R16Route reclassifies and R17Hold keeps.
        if (!hold.Held && c.Nodes.Any(n => n.Taints.Count > 0) && route.Errors.Count < hold.Errors.Count) defaultOnly.Add($"{c.Id}: R16Route {O(route)}; R17Hold {O(hold)}");
        if (c.Id.StartsWith("r16") || c.Id.StartsWith("rx") || hold.Held || O(hold) != O(g))
            rows.Add($"| {c.Id} | {(hold.Held ? "held" : "")} | {O(g)} | {O(set)} | {O(route)} | {O(hold)} | {O(part)} |");
    }
    Console.WriteLine($"\nRound 17, body hold (PLAN G148, round 16's review B1; {all.Count} cases: 382 earlier, {R16.All().Count()} of round 16, {R16X.All().Count()} of its review):");
    Console.WriteLine($"Held bodies: {heldIds.Count} ({string.Join(", ", heldIds)})");
    Console.WriteLine($"(i) dependent conflicts left a Language problem: {(lost.Count(x => !x.Contains("drops")) == 0 ? "none" : string.Join("; ", lost.Where(x => !x.Contains("drops"))))}");
    Console.WriteLine($"(ii) bodies not held whose outcome differs from R16Set's: {(notKept.Count == 0 ? "none" : string.Join("; ", notKept))}");
    Console.WriteLine($"(iii) a mode dropping or duplicating a (Loan, access) rule G reports: {(lost.Count(x => x.Contains("drops")) == 0 ? "none" : string.Join("; ", lost.Where(x => x.Contains("drops"))))}");
    Console.WriteLine($"(iv) cost, Language problems of the per-part program the hold makes Unsupported_Kd ({cost.Count} cases):");
    foreach (var x in cost) Console.WriteLine("  " + x);
    Console.WriteLine($"The default taint's limits, unchanged (R4-M70; route taint would reclassify): {(defaultOnly.Count == 0 ? "none" : "")}");
    foreach (var x in defaultOnly) Console.WriteLine("  " + x);
    Console.WriteLine("\n| Case | body | R14G | R16Set | R16Route | R17Hold | R17PerPart |\n| --- | --- | --- | --- | --- | --- | --- |");
    foreach (var r in rows) Console.WriteLine(r);
    Console.WriteLine($"\nRound 17 failures: {fails}");
    return fails;
}
if (args.Contains("-r17")) return Round17();
// Round 18 (ER-effects, A5-F5; unit 2x-P5): A4's static keys and classes (R1), the Call's and Drop's sets with W6 (R2), a destruction's own
// exclusive Loan (R3) and staticEnsure under PD12 (R4) on R17Hold, with G136's lemma and G124's Transferable. Checks: (i) the earlier cases
// keep R17Hold's outcome under R18; (ii) each round-18 case meets its expected outcome under R18; (iii) G136: R18 and R18StaticEnds give
// one verdict everywhere; (iv) each G124 case's Transferable gives its SPEC verdict. -r18 prints only this; its exit code counts the failures.
var round18Variants = new[] { Abl.R18NoW6, Abl.R18LiveOnly, Abl.R18NoR3, Abl.R18Merged, Abl.R18EnsureExact, Abl.R18EnsureByField, Abl.R18EnsureIgnored, Abl.R18StaticEnds, Abl.R18EnsureClosed };
var round18Modes = round18Variants.Prepend(Abl.R18).ToArray();
int Round18()
{
    string O(Analysis a) => a.Errors.Count == 0 && a.Unsupported.Count == 0 ? "valid"
        : string.Join("; ", new[] { a.Errors.Count > 0 ? "error at `" + string.Join("`, `", a.ErrorNodes()) + "`" : null,
            a.Unsupported.Count > 0 ? "U at `" + string.Join("`, `", a.UnsupportedNodes()) + "`" : null }.Where(x => x != null));
    string Expected(Case c) => c.At.Length == 0 && c.UnsuppAt.Length == 0 ? "valid"
        : string.Join("; ", new[] { c.At.Length > 0 ? "error at `" + string.Join("`, `", c.At) + "`" : null,
            c.UnsuppAt.Length > 0 ? "U at `" + string.Join("`, `", c.UnsuppAt) + "`" : null }.Where(x => x != null));
    // Against SPEC: an accepted SPEC error is unsound; a Language error of a SPEC-valid program is over-strict (a located U is neither).
    string W(Case c, Analysis a) => O(a) + (c.Err && a.Errors.Count == 0 && a.Unsupported.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    int fails = 0;
    var earlier = Cases.All().Concat(R16.All()).Concat(R16X.All()).ToList();
    var changed = earlier.Where(c => O(Design(c, Abl.R18)) != O(Design(c, Abl.R17Hold))).Select(c => c.Id).ToList();
    fails += changed.Count;
    var cases = R18.All().ToList();
    Console.WriteLine($"\nRound 18, ER-effects (A5-F5; {cases.Count} cases, {R18G.All.Length} G124 cases):");
    Console.WriteLine($"(i) earlier cases ({earlier.Count}) whose outcome under R18 differs from R17Hold's: {(changed.Count == 0 ? "none" : string.Join(", ", changed))}");
    Console.WriteLine("\n| Case | Program | SPEC | expected (R18) | R18 | " + string.Join(" | ", round18Variants) + " | meets |\n|" + string.Concat(Enumerable.Repeat(" --- |", round18Variants.Length + 6)));
    foreach (var c in cases)
    {
        var d = Design(c, Abl.R18);
        bool ok = d.ErrorNodes().ToHashSet().SetEquals(c.At) && d.UnsupportedNodes().ToHashSet().SetEquals(c.UnsuppAt) && W(c, d) == O(d);
        fails += ok ? 0 : 1;
        Console.WriteLine($"| {c.Id} | {c.Title} | {(c.Err ? "error" : "valid")} | {Expected(c)} | {O(d)} | {string.Join(" | ", round18Variants.Select(v => W(c, Design(c, v))))} | {(ok ? "yes" : "**NO**")} |");
    }
    Console.WriteLine($"(ii) failures: {fails - changed.Count}");
    // (iii) G136's lemma: the static's ending changes no verdict (accept, Language error or U), only where a Loan already met is met again.
    var verdict = cases.Where(c => Design(c, Abl.R18) is var a && Design(c, Abl.R18StaticEnds) is var b && (a.Errors.Count > 0, a.Unsupported.Count > 0) != (b.Errors.Count > 0, b.Unsupported.Count > 0)).Select(c => c.Id).ToList();
    var moved = cases.Where(c => O(Design(c, Abl.R18)) != O(Design(c, Abl.R18StaticEnds))).Select(c => $"{c.Id} (R18 {O(Design(c, Abl.R18))}; R18StaticEnds {O(Design(c, Abl.R18StaticEnds))})").ToList();
    fails += verdict.Count;
    Console.WriteLine($"(iii) G136: cases whose verdict the static's ending changes: {(verdict.Count == 0 ? "none" : string.Join(", ", verdict))}; whose locations it changes: {(moved.Count == 0 ? "none" : string.Join("; ", moved))}");
    foreach (var v in round18Variants)
        Console.WriteLine($"{v}: " + string.Join("; ", cases.Where(c => O(Design(c, v)) != O(Design(c, Abl.R18))).Select(c => $"{c.Id}: {W(c, Design(c, v))}").DefaultIfEmpty("no change")));
    Console.WriteLine("\n| Case | Static's Type (read by a confined witness) | SPEC Transferable | SPEC | HoldsRaw (today) | today |\n| --- | --- | --- | --- | --- | --- |");
    foreach (var (id, text, type, specErr) in R18G.All)
    {
        int t = R18G.Transferable(type); bool raw = R18G.HoldsRaw(type);
        if ((t != 0) != specErr) fails++;
        string Today() => raw == specErr ? "same" : specErr ? "**UNSOUND** (accepted)" : "over-strict";
        Console.WriteLine($"| {id} | {text} | {new[] { "Proven", "Unknown", "Refuted" }[t]} | {(t != 0 ? "error" : "valid")}{((t != 0) != specErr ? " **NO**" : "")} | {(raw ? "error" : "valid")} | {Today()} |");
    }
    if (verbose)
        foreach (var c in cases)
            foreach (var x in round18Modes)
                Console.WriteLine($"{c.Id,-5} {x,-17}: " + string.Join("; ", Design(c, x) is var a ? a.Errors.Select(e => $"{e.L} at `{a.LabelAt(e.P)}` vs {e.What}").Concat(a.Unsupported.Select(e => $"U {e.L} at `{a.LabelAt(e.P)}` vs {e.What}")) : []));
    Console.WriteLine($"\nRound 18 failures: {fails}");
    return fails;
}
if (args.Contains("-r18")) return Round18();
// Round 19 (ER-defaults, A5-F9; unit 2x-P9): a default template's E15, R26-borrow and R26-access, and at an omitting call P9's rows, the raw-pointer
// row and R-a, on R18. Checks: (i) every earlier case reports the same (Loan, access) multiset under R19 as under R18, and changes its outcome only
// where R4-M70's taint made a Language problem a located Unsupported_Kd; (ii) each round-19 case meets its expected outcome under R19 and none is
// unsound or over-strict against SPEC there. -r19 prints only this; its exit code counts the failures.
var round19Variants = new[] { Abl.R19Taint, Abl.R19InlinedDrop, Abl.R19NoEnding, Abl.R19SlotsOnly, Abl.R19ValueRows, Abl.R19NoRawRow, Abl.R19SharedStep,
    Abl.R19ReturnOnly, Abl.R19AccessAll, Abl.R19RawAccess };
var round19Modes = round19Variants.Prepend(Abl.R19).ToArray();
int Round19()
{
    static string Part(string what, IEnumerable<string> at) => at.Any() ? what + " at `" + string.Join("`, `", at) + "`" : null;
    string O(Analysis a) => a.Errors.Count == 0 && a.Unsupported.Count == 0 && a.DefaultProblems.Count == 0 ? "valid"
        : string.Join("; ", new[] { Part("error", a.ErrorNodes()), Part("Borrow", a.DefaultNodes("Borrow")), Part("Access", a.DefaultNodes("Access")), Part("U", a.UnsupportedNodes()) }.Where(x => x != null));
    string Expected(Case c) => c.At.Length + c.BorrowAt.Length + c.AccessAt.Length + c.UnsuppAt.Length == 0 ? "valid"
        : string.Join("; ", new[] { Part("error", c.At), Part("Borrow", c.BorrowAt), Part("Access", c.AccessAt), Part("U", c.UnsuppAt) }.Where(x => x != null));
    bool Meets(Case c, Analysis a) => a.ErrorNodes().ToHashSet().SetEquals(c.At) && a.DefaultNodes("Borrow").ToHashSet().SetEquals(c.BorrowAt)
        && a.DefaultNodes("Access").ToHashSet().SetEquals(c.AccessAt) && a.UnsupportedNodes().ToHashSet().SetEquals(c.UnsuppAt);
    // Against SPEC: a SPEC error with no A5 record is unsound unless another check reports it (Backstop) or a PLAN issue lists it (Known); a Language
    // record in a SPEC-valid body is over-strict, or a second record where the program's one error is its template's (DupOf, DIAGNOSTICS' Defaults).
    string W(Case c, Analysis a)
    {
        bool none = a.Errors.Count == 0 && a.Unsupported.Count == 0 && a.DefaultProblems.Count == 0, lang = a.Errors.Count > 0 || a.DefaultProblems.Count > 0;
        return O(a) + (c.Err && none ? c.Backstop != null ? $" ({c.Backstop})" : c.Known != null ? $" **UNSOUND** ({c.Known})" : " **UNSOUND**"
            : !c.Err && lang ? c.DupOf != null ? $" (a second record: {c.DupOf}'s error)" : " (over-strict)" : "");
    }
    IEnumerable<(string, int, string)> Found(Analysis a) => a.Errors.Concat(a.Reclassified);
    bool SameMultiset(IEnumerable<(string, int, string)> x, IEnumerable<(string, int, string)> y)
        => x.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dx && y.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dy
            && dx.Count == dy.Count && dx.All(kv => dy.TryGetValue(kv.Key, out var n) && n == kv.Value);
    int fails = 0;
    var earlier = Cases.All().Concat(R16.All()).Concat(R16X.All()).Concat(R18.All()).ToList();
    var changed = new List<string>(); var lost = new List<string>();
    foreach (var c in earlier)
    {
        var a = Design(c, Abl.R18); var b = Design(c, Abl.R19);
        if (!SameMultiset(Found(a), Found(b))) { fails++; lost.Add(c.Id); }
        if (O(a) == O(b)) continue;
        changed.Add($"{c.Id}: R18 {O(a)}; R19 {O(b)}");
        if (!c.Nodes.Any(n => n.Taints.Count > 0)) fails++;
    }
    var cases = R19.All().ToList();
    Console.WriteLine($"\nRound 19, ER-defaults (A5-F9; {cases.Count(c => c.Prepared.Count > 0)} template bodies, {cases.Count(c => c.Prepared.Count == 0)} callers):");
    Console.WriteLine($"(i) earlier cases ({earlier.Count}) whose (Loan, access) multiset differs under R19: {(lost.Count == 0 ? "none" : string.Join(", ", lost))}; whose outcome differs: "
        + (changed.Count == 0 ? "none" : $"{changed.Count}, each where R4-M70's taint made a Language problem Unsupported_Kd"));
    foreach (var x in changed) Console.WriteLine("  " + x);
    Console.WriteLine("\n| Case | Program | SPEC | expected (R19) | R19 | today (R18) | " + string.Join(" | ", round19Variants) + " | meets |\n|" + string.Concat(Enumerable.Repeat(" --- |", round19Variants.Length + 7)));
    int own = 0;
    foreach (var c in cases)
    {
        var d = Design(c, Abl.R19); var w = W(c, d);
        bool ok = Meets(c, d) && !w.Contains("over-strict") && !w.Contains("second record") && (!w.Contains("UNSOUND") || c.Known != null);
        own += ok ? 0 : 1;
        Console.WriteLine($"| {c.Id} | {c.Title} | {(c.Err ? "error" : "valid")} | {Expected(c)} | {O(d)} | {W(c, Design(c, Abl.R18))} | {string.Join(" | ", round19Variants.Select(v => W(c, Design(c, v))))} | {(ok ? "yes" : "**NO**")} |");
    }
    fails += own;
    Console.WriteLine($"(ii) failures: {own}");
    foreach (var v in round19Variants.Prepend(Abl.R18))
        Console.WriteLine($"{v}: " + string.Join("; ", cases.Where(c => O(Design(c, v)) != O(Design(c, Abl.R19))).Select(c => $"{c.Id}: {W(c, Design(c, v))}").DefaultIfEmpty("no change")));
    if (verbose)
        foreach (var c in cases)
            foreach (var x in round19Modes.Prepend(Abl.R18))
                Console.WriteLine($"{c.Id,-18} {x,-15}: " + string.Join("; ", Design(c, x) is var a ? a.Errors.Select(e => $"{e.L} at `{a.LabelAt(e.P)}` vs {e.What}")
                    .Concat(a.DefaultProblems.Select(e => $"{e.Code} {e.L} at `{a.LabelAt(e.P)}`, related {e.Param}")).Concat(a.Unsupported.Select(e => $"U {e.L} at `{a.LabelAt(e.P)}` vs {e.What}"))
                    .Concat(a.Dropped.Select(e => $"dropped {e.L} at `{a.LabelAt(e.P)}` vs {e.What}")) : []));
    Console.WriteLine($"\nRound 19 failures: {fails}");
    return fails;
}
if (args.Contains("-r19")) return Round19();
// Round 20 (template ownership of failures in inline default copies, PLAN G171; unit 2x-P11): on R19, R20Own gives a failure to a template only where
// an Inlined scope contains all its participating scopes, decided per candidate before the first is chosen. Checks: (i) every earlier case keeps R19's
// (Loan, access) multiset and outcome, except where R4-M69 dropped a failure R20Own gives the caller; (ii) each round-20 case meets its SPEC outcome
// (At, UnsuppAt, OmitAt) under R20Own, with no unsound, over-strict or extra record, unless the case records a finding, which R20Own must then
// reproduce; (iii) the simulation check (the M69 plan's M69-0 in small): each omission's template, run under the same rule, reports the omitted Loan at
// the omitted access, both relative to its boundary. -r20 prints only this (-v adds every fact); its exit code counts the failures.
var round20Variants = new[] { Abl.R20AccessOnly, Abl.R20IssueOnly, Abl.R20NoReservation, Abl.R20AfterSelect, Abl.R20SameSpan, Abl.R20Entry };
var round20Modes = round20Variants.Prepend(Abl.R20Own).ToArray();
int Round20()
{
    static string Part(string what, IEnumerable<string> at) => at.Any() ? what + " at `" + string.Join("`, `", at) + "`" : null;
    string O(Analysis a) => a.Errors.Count == 0 && a.Unsupported.Count == 0 && a.DefaultProblems.Count == 0 ? "valid"
        : string.Join("; ", new[] { Part("error", a.ErrorNodes()), Part("Borrow", a.DefaultNodes("Borrow")), Part("Access", a.DefaultNodes("Access")), Part("U", a.UnsupportedNodes()) }.Where(x => x != null));
    string O20(Analysis a) => !a.OmittedNodes().Any() ? O(a) : (O(a) == "valid" ? "" : O(a) + "; ") + Part("omitted", a.OmittedNodes());
    string Expected(Case c) => c.At.Length + c.UnsuppAt.Length + c.OmitAt.Length == 0 ? "valid"
        : string.Join("; ", new[] { Part("error", c.At), Part("U", c.UnsuppAt), Part("omitted", c.OmitAt) }.Where(x => x != null));
    // A label or Loan id relative to a copy prefix (a template's own, R20.Ctx); γ and Δ ids carry theirs after the sign.
    static string Rel(string x, string pre) => x.Length > 0 && x[0] is 'γ' or 'Δ' ? x[0] + Rel(x[1..], pre) : pre.Length > 0 && x.StartsWith(pre) ? x[pre.Length..].TrimStart() : x;
    // The subject a record takes (R4-M71's key): a copy's node lies at its template's location, so every copy prefix is stripped.
    static string Subject(string label) { while (label.StartsWith('[')) label = label[(label.IndexOf(']') + 1)..]; return label.TrimStart(); }
    // The simulation check: each omission's boundary B names its template (Case.Scopes), which, run under the same mode, reports the omitted Loan at the
    // omitted access, both relative to B, as a Language problem or (a held view) a located U for that Loan. A hidden candidate has no boundary.
    List<string> Unsimulated(Case c, Analysis a, Abl m)
    {
        var bad = new List<string>();
        foreach (var (l, p, _, b) in a.Omissions())
        {
            var at = a.LabelAt(p);
            if (b <= 0 || b >= c.Scopes.Count) { bad.Add($"{l} at `{at}` (no template: {(b <= 0 ? "the caller's" : "unmodelled")})"); continue; }
            var (pre, id) = (c.Scopes[b].Prefix, c.Scopes[b].Template);
            var t = Design(R20.Template(id), m);
            if (!t.Errors.Concat(t.Reclassified).Any(e => e.L == Rel(l, pre) && t.LabelAt(e.P) == Rel(at, pre)))
                bad.Add($"{l} at `{at}` (dt20-{id} reports no {Rel(l, pre)} at `{Rel(at, pre)}`)");
        }
        return bad;
    }
    // Against SPEC: an omission its template does not report, or a missing record, is unsound; a Language record at an omitted access is an extra
    // record (DIAGNOSTICS' Defaults paragraph: the calls that omit an invalid default add no other record); any other one is over-strict.
    string W(Case c, Analysis a, Abl m)
    {
        var found = a.ErrorNodes().Concat(a.UnsupportedNodes()).ToHashSet();
        var lang = a.ErrorNodes().Where(x => !c.At.Contains(x)).ToList();
        bool unsound = Unsimulated(c, a, m).Count > 0 || c.At.Concat(c.UnsuppAt).Any(x => !found.Contains(x));
        return O20(a) + (unsound ? " **UNSOUND**" : "") + (lang.Any(c.OmitAt.Contains) ? " (extra record)" : "") + (lang.Any(x => !c.OmitAt.Contains(x)) ? " (over-strict)" : "");
    }
    bool Meets(Case c, Analysis a) => a.ErrorNodes().ToHashSet().SetEquals(c.At) && a.UnsupportedNodes().ToHashSet().SetEquals(c.UnsuppAt)
        && a.OmittedNodes().ToHashSet().SetEquals(c.OmitAt) && a.DefaultNodes("Borrow").ToHashSet().SetEquals(c.BorrowAt) && a.DefaultNodes("Access").ToHashSet().SetEquals(c.AccessAt);
    IEnumerable<(string, int, string)> Found(Analysis a) => a.Errors.Concat(a.Reclassified);
    bool SameMultiset(IEnumerable<(string, int, string)> x, IEnumerable<(string, int, string)> y)
        => x.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dx && y.GroupBy(t => t).ToDictionary(g => g.Key, g => g.Count()) is var dy
            && dx.Count == dy.Count && dx.All(kv => dy.TryGetValue(kv.Key, out var n) && n == kv.Value);
    int fails = 0;
    // (i) The earlier cases under R20Own and each variant against R19 (R4-M69): a difference must be an R19 drop the mode reports as the caller's.
    var earlier = Cases.All().Concat(R16.All()).Concat(R16X.All()).Concat(R18.All()).Concat(R19.All()).ToList();
    var dropping = earlier.Where(c => Design(c, Abl.R19).Dropped.Count > 0).Select(c => c.Id).ToList();
    Console.WriteLine($"\nRound 20, template ownership in inline default copies (PLAN G171; {R20.All().Count(c => c.Id.StartsWith("dt20"))} template bodies, {R20.All().Count(c => c.Id.StartsWith("dc20"))} callers):");
    Console.WriteLine($"Earlier cases ({earlier.Count}) where R19 (R4-M69) drops a Loan problem: {(dropping.Count == 0 ? "none" : string.Join(", ", dropping))}");
    foreach (var m in round20Modes)
    {
        var changed = new List<string>();
        foreach (var c in earlier)
        {
            var a = Design(c, Abl.R19); var b = Design(c, m);
            if (SameMultiset(Found(a), Found(b)) && O(a) == O(b)) continue;
            var added = Found(b).Except(Found(a)).ToList();
            bool reason = !Found(a).Except(Found(b)).Any() && added.All(e => a.Dropped.Any(d => d.L == e.Item1 && d.P == e.Item2));
            if (m == Abl.R20Own && !reason) fails++;
            changed.Add($"{c.Id}: R19 {O(a)}; {m} {O(b)}" + (reason ? " (R4-M69 dropped a failure the mode gives the caller)" : " **unexplained**"));
        }
        Console.WriteLine($"(i) {m}: earlier cases whose (Loan, access) multiset or outcome differs from R19's: {(changed.Count == 0 ? "none" : string.Join("; ", changed))}");
    }
    // (ii) The round's cases.
    var cases = R20.All().ToList();
    Console.WriteLine("\n| Case | Program | SPEC | SPEC outcome (one record per failure) | R20Own | today (R19, R4-M69) | " + string.Join(" | ", round20Variants) + " | meets |\n|"
        + string.Concat(Enumerable.Repeat(" --- |", round20Variants.Length + 7)));
    int own = 0;
    foreach (var c in cases)
    {
        var d = Design(c, Abl.R20Own); var w = W(c, d, Abl.R20Own);
        bool clean = !w.Contains("UNSOUND") && !w.Contains("over-strict") && !w.Contains("extra record");
        bool ok = c.Finding != null ? w.Contains("UNSOUND") : Meets(c, d) && clean;
        own += ok ? 0 : 1;
        var spec = (c.Err ? "error" : "valid") + (c.Backstop != null ? $" ({c.Backstop})" : "") + (c.DupOf != null ? $" ({c.DupOf}'s error is its own)" : "");
        Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {Expected(c)} | {w}{(c.Finding != null ? $" (finding: {c.Finding})" : "")} | {W(c, Design(c, Abl.R19), Abl.R19)} | "
            + $"{string.Join(" | ", round20Variants.Select(v => W(c, Design(c, v), v)))} | {(ok ? "yes" : "**NO**")} |");
    }
    fails += own;
    Console.WriteLine($"(ii) failures: {own}");
    // (iii) The simulation check under R20Own; a case's own finding lists its unsimulated omissions.
    var sims = cases.SelectMany(c => Unsimulated(c, Design(c, Abl.R20Own), Abl.R20Own).Select(x => (c, x))).ToList();
    var omissions = cases.SelectMany(c => Design(c, Abl.R20Own) is var a ? a.Omissions().Select(e => $"{c.Id}: {e.L} at `{a.LabelAt(e.P)}` → dt20-{c.Scopes[e.B].Template}") : []).ToList();
    fails += sims.Count(s => s.c.Finding == null);
    Console.WriteLine($"(iii) simulation check: R20Own omits {omissions.Count} failures as their templates' ({string.Join("; ", omissions)}); unsimulated: "
        + (sims.Count == 0 ? "none" : string.Join("; ", sims.Select(s => $"{s.c.Id}: {s.x}" + (s.c.Finding != null ? " (finding)" : " **FAILURE**")))));
    // Records (R4-M71's key (code, subject, body)): failures of one body whose accesses share a subject through copies of one template.
    var shared = cases.Select(c => (c, a: Design(c, Abl.R20Own))).SelectMany(x => Found(x.a).Distinct().GroupBy(e => Subject(x.a.LabelAt(e.Item2)))
        .Where(g => g.Select(e => x.a.LabelAt(e.Item2)).Distinct().Count() > 1).Select(g => $"{x.c.Id}: {g.Count()} failures ({string.Join(", ", g.Select(e => $"{e.Item1} at `{x.a.LabelAt(e.Item2)}`"))}) share the subject `{g.Key}`")).ToList();
    Console.WriteLine($"Records sharing a subject under R20Own (one record by R4-M71's key, the first's facts kept): {(shared.Count == 0 ? "none" : string.Join("; ", shared))}");
    foreach (var v in round20Variants.Prepend(Abl.R19))
        Console.WriteLine($"{v}: " + string.Join("; ", cases.Where(c => W(c, Design(c, v), v) != W(c, Design(c, Abl.R20Own), Abl.R20Own)).Select(c => $"{c.Id}: {W(c, Design(c, v), v)}").DefaultIfEmpty("no change")));
    if (verbose)
        foreach (var c in cases)
            foreach (var x in round20Modes.Prepend(Abl.R19))
                Console.WriteLine($"{c.Id,-18} {x,-16}: " + string.Join("; ", Design(c, x) is var a ? a.Errors.Select(e => $"{e.L} at `{a.LabelAt(e.P)}` vs {e.What}")
                    .Concat(a.Unsupported.Select(e => $"U {e.L} at `{a.LabelAt(e.P)}` vs {e.What}")).Concat(a.Omissions().Select(e => $"omitted {e.L} at `{a.LabelAt(e.P)}` (boundary {e.B})")) : []));
    Console.WriteLine($"\nRound 20 failures: {fails}");
    return fails;
}
if (args.Contains("-r20")) return Round20();
var table3 = new StringBuilder("| Case | SPEC | S1′ | design A | Class |\n| --- | --- | --- | --- | --- |\n");
string V(Analysis a) => a.Errors.Count == 0 ? "valid" : "error at `" + string.Join("`, `", a.ErrorNodes()) + "`";
void VerboseA(Case c, Analysis am) // design A and its first-Deref base (AF), every error fact; round 11: SA = SecureAcquire's encoding;
{                                  // round 12: R11 = Round11, AO = AcquireAtOperation, NV = NoOwnValue (cases 130+)
    foreach (var (tag, a) in new[] { ("A", am), ("AF", new Analysis(c, Mode.A, Abl.FirstDerefBase)) }.Concat(c.Secured != null ? [("SA", new Analysis(c, Mode.A, Abl.SecureAcquire))] : [])
        .Concat(Cases.Round11(c) ? [("R11", new Analysis(c, Mode.A, Abl.Round11)), ("AO", new Analysis(c, Mode.A, Abl.AcquireAtOperation)), ("NV", new Analysis(c, Mode.A, Abl.NoOwnValue))] : []))
        Console.WriteLine($"{c.Id,-4} {tag,-4}: " + (a.Errors.Count == 0 ? "no errors" : string.Join("; ", a.Errors.Select(e => $"{e.L} live at `{a.LabelAt(e.P)}` vs {e.What}"))));
}
string Cls(Case c, Analysis a)
{
    bool ae = a.Errors.Count > 0;
    // Round 12: a round-11/12 case's other MIR form (Secured, Always, Shape, IntrinsicRow) names its own nodes, so its own
    // error location is the expected one (the same source operation); every other case keeps the case's.
    var at = (Cases.Round11(c) || a.Enc == c.OnePoint) && a.Enc.At.Length > 0 ? a.Enc.At : c.At; // round 13: OnePoint names its own
    if (ae == c.Err) return !ae || a.ErrorNodes().ToHashSet().SetEquals(at) ? "same" : "same verdict, error moves";
    if (!ae) return "**(c) UNSOUND**";
    return c.S1Err == true ? "(a) S1′ loss" : c.ALoss != null ? "(a) " + c.ALoss : "**(b) unlisted loss**";
}
if (args.Contains("-cases")) // -cases [variant ...]: one tab-separated row per case and variant (round 12: variants by name;
{                             // round 13: a name ending in +PK runs on the design, e.g. None+PK, NoDestroyValue+PK)
    var named = args.SkipWhile(x => x != "-cases").Skip(1).ToArray();
    foreach (var c in Cases.All())
    {
        foreach (var name in named.Length > 0 ? named : ["None", "PrefixKill", "FirstDerefBase"])
        {
            bool pk = name.EndsWith("+PK");
            var ab = Enum.Parse<Abl>(pk ? name[..^3] : name);
            var a = new Analysis(c, Mode.A, ab, prefixKill: pk);
            var facts = string.Join("; ", a.Errors.Select(e => $"{e.L}@{a.LabelAt(e.P)} vs {e.What}").OrderBy(x => x, StringComparer.Ordinal));
            Console.WriteLine($"{c.Id}	{name}	{(c.Err ? "error" : "valid")}	{V(a)}	{Cls(c, a)}	{facts}");
        }
    }
    return 0;
}
var table = new StringBuilder();
table.AppendLine("| Case | Program | SPEC | PROV | S1 intended | S1 as written | S1′ (final rules) |");
table.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
int finalFails = 0;
foreach (var c in Cases.All())
{
    if (Cases.NoS1P(c)) // EndBorrow and err(ended) exist in design A mode only, so S1′ does not run these (as in the reviews)
    {
        var a10 = new Analysis(c, Mode.A); table3.AppendLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | (not run) | {V(a10)} | {Cls(c, a10)} |");
        if (verbose) VerboseA(c, a10);
        continue;
    }
    var r = modes.ToDictionary(m => m, m => new Analysis(c, m));
    bool s1Err = c.S1Err ?? c.Err; var s1At = c.S1At ?? c.At;
    string Exp(bool e, string[] at) => e ? "error" + (at.Length > 0 ? " at `" + string.Join("`, `", at) + "`" : "") : "valid";
    (string Text, bool Ok) Got(Analysis a, bool e, string[] at)
    {
        var nodes = a.ErrorNodes().ToList();
        var extra = c.Extra?.Invoke(a);
        bool ok = (nodes.Count > 0) == e && (!e || at.Length == 0 || nodes.ToHashSet().SetEquals(at)) && extra == null;
        var tag = ok ? "pass" : e && nodes.Count == 0 ? "**FAIL: UNSOUND**" : "FAIL";
        var v = nodes.Count == 0 ? "valid" : "error at `" + string.Join("`, `", nodes) + "`";
        return ($"{v} ({tag}{(extra != null ? ": " + extra : "")})", ok);
    }
    var prov = Got(r[Mode.PROV], c.Err, c.At); var s1f = Got(r[Mode.S1P], s1Err, s1At);
    var s1 = c.S1POnly ? Got(r[Mode.S1], c.Err, c.At) : Got(r[Mode.S1], s1Err, s1At);
    if (!s1f.Ok) finalFails++;
    var am = new Analysis(c, Mode.A);
    table3.AppendLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {V(r[Mode.S1P])} | {V(am)} | {Cls(c, am)} |");
    if (verbose) VerboseA(c, am);
    table.AppendLine($"| {c.Id} | {c.Title} | {Exp(c.Err, c.At)} | {prov.Text} | {Exp(s1Err, s1At)}{(c.S1POnly ? " (S1′ only)" : "")} | {s1.Text} | {s1f.Text} |");
    if (verbose)
        foreach (var m in modes)
            Console.WriteLine($"{c.Id,-4} {m,-4}: " + (r[m].Errors.Count == 0 ? "no errors" :
                string.Join("; ", r[m].Errors.Select(e => $"{e.L} live at `{c.Nodes[e.P].Label}` vs {e.What}"))));
}
Console.WriteLine(table);
Console.WriteLine(finalFails == 0 ? "S1′: all cases pass." : $"S1′: {finalFails} case(s) fail.");
Console.WriteLine("\nDesign A (round 3):\n" + table3);

// Ablations: drop one refinement of the final rules (S1′) and list the cases whose verdict changes, judged by the SPEC.
Console.WriteLine("\n| Variant of S1′ | Cases that change |\n| --- | --- |");
var aVar = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, Abl.AIssueNotEnded), b: new Analysis(c, Mode.A)))
    .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}");
Console.WriteLine("Design A without the round-3 fix (issued Loan not ended at its base's destruction): " + string.Join("; ", aVar.DefaultIfEmpty("none")));
// Round 4: exclusion by Loan id (old) against exclusion by holder (new), in S1′ and A modes; design A's letter (AText).
Console.WriteLine("\nRound 4 (rows that differ, plus the loop cases):\n| Case | SPEC | S1′ by id | S1′ by holder | A by id | A text | A by holder |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(c => !Cases.Round11(c))) // S1′ models none of round 11's rules
{
    var row = new[] { new Analysis(c, Mode.S1P, Abl.IdExclusion), new Analysis(c, Mode.S1P), new Analysis(c, Mode.A, Abl.IdExclusion), new Analysis(c, Mode.A, Abl.AText), new Analysis(c, Mode.A) };
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    if (c.Id.StartsWith("20") || !row[0].ErrorNodes().SequenceEqual(row[1].ErrorNodes()) || !row[2].ErrorNodes().SequenceEqual(row[4].ErrorNodes()) || !row[3].ErrorNodes().SequenceEqual(row[4].ErrorNodes()))
        Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row.Select(W))} |");
}
var phantomVariants = new[] { Abl.PhantomAlone, Abl.PhantomKeepsReceiver, Abl.PhantomParentsAtCall };
// Round 10 (the chosen 1b design diff): each variant removes, or restores, one rule in design A mode.
var round10Variants = new[] { Abl.NoRouteReach, Abl.RouteWriteOnly, Abl.RouteAllParents, Abl.ExcuseAtCallOnly, Abl.LoanPastShared,
    Abl.ObsSharedCut, Abl.NoFreshClosure, Abl.RefParentsInTransit, Abl.SliceIndexInline, Abl.PrefixKill, Abl.EndedByLoan, Abl.GammaGuardsOnly, Abl.FreshParents, Abl.FreshOnly, Abl.DestroyAsDrop, Abl.SeedLiveOutOnly };
var reviewMutations = new[] { Abl.NoP3A, Abl.NoParentsA };
// Round 11 (E13, E14): each variant restores one behavior of the reviewed 1b branch, or applies round 11's fix (SecureAcquire).
// Round 12: the first four apply to round 12's design A; Implemented1b keeps the reviewed branch's MIR (G123); SecureAcquire and
// AlwaysSecure are round 11's E13 on round 11's M and on M′ (the E13 design review's run).
var round11Variants = new[] { Abl.CalleeNotActivated, Abl.InputsOnCS, Abl.PairAcquireLive, Abl.NoRawStore, Abl.Implemented1b, Abl.SecureAcquire, Abl.AlwaysSecure };
// Round 12 (E13 as the owner chose, R1): each variant restores an earlier rule or MIR form, or removes one clause of CS_i.
var round12Variants = new[] { Abl.Round11, Abl.AcquireAtOperation, Abl.ShapeReservation, Abl.IntrinsicAllOperands, Abl.NoEarlierValues, Abl.NoLaterRoots, Abl.EarlierRoots, Abl.NoOwnValue, Abl.OwnValueEverywhere };
foreach (var ab in new[] { Abl.KillAcrossDeref, Abl.FirstDerefBase, Abl.NoTransit }.Concat(phantomVariants).Concat(round10Variants).Concat(round11Variants).Concat(round12Variants))
{
    var aKill = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, ab), b: new Analysis(c, Mode.A)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: {V(x.a)}" + (x.c.Err && x.a.Errors.Count == 0 ? " **UNSOUND**" : !x.c.Err && x.a.Errors.Count > 0 ? " (over-strict)" : ""));
    Console.WriteLine($"Design A with {ab}: " + string.Join("; ", aKill.DefaultIfEmpty("no change")));
}
// Round 9 (G113): the F6 cases under design A, its first-Deref variant and each rejected F6 option; class counts.
Console.WriteLine("\nRound 9 (F6 cases):\n| Case | SPEC | A | A FirstDerefBase | " + string.Join(" | ", phantomVariants) + " |\n| --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(c => c.Id.StartsWith("33") || c.Id.StartsWith("36")))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    var row = new[] { Abl.None, Abl.FirstDerefBase }.Concat(phantomVariants).Select(x => W(new Analysis(c, Mode.A, x)));
    Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", row)} |");
}
// Round 10: the 1b rules' cases under design A, its first-Deref base and the ablation of each rule.
var r10cols = new[] { Abl.None, Abl.FirstDerefBase, Abl.NoRouteReach, Abl.RouteWriteOnly, Abl.LoanPastShared, Abl.FreshOnly, Abl.FreshParents, Abl.SliceIndexInline, Abl.EndedByLoan, Abl.DestroyAsDrop, Abl.SeedLiveOutOnly };
Console.WriteLine("\nRound 10 (1b rules):\n| Case | SPEC | A | A FirstDerefBase | " + string.Join(" | ", r10cols.Skip(2)) + " |\n|" + string.Concat(Enumerable.Repeat(" --- |", r10cols.Length + 2)));
foreach (var c in Cases.All().Where(c => c.Id is "10a" or "10b" or "10c" or "10d" or "13d" or "13e" or "19a" or "21a" or "26a" or "26b" or "27" || new[] { "33", "36", "38", "39", "40", "41", "42", "43", "44" }.Any(c.Id.StartsWith) || Cases.Review1b(c)))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    Console.WriteLine($"| {c.Id} | {(c.Err ? "error" : "valid")} | {string.Join(" | ", r10cols.Select(x => W(new Analysis(c, Mode.A, x))))} |");
}
// Round 11 (E13, E14): the core review's B1, B2 and B4 cases under design A, PrefixKill and each round-11 variant.
Console.WriteLine("\nRound 11 (E13, E14):\n| Case | Program | SPEC | A | A PrefixKill | " + string.Join(" | ", round11Variants) + " | class A | class PrefixKill |\n|"
    + string.Concat(Enumerable.Repeat(" --- |", round11Variants.Length + 7)));
foreach (var c in Cases.All().Where(Cases.Round11))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    var a0 = new Analysis(c, Mode.A); var pk = new Analysis(c, Mode.A, Abl.PrefixKill);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(pk)} | {string.Join(" | ", round11Variants.Select(x => W(new Analysis(c, Mode.A, x))))} | {Cls(c, a0)} | {Cls(c, pk)} |");
}
// Round 12 (E13, owner's option R1): the cases 130+ under design A, PrefixKill and each round-12 variant, with each one's class.
Console.WriteLine("\nRound 12 (E13, operand order):\n| Case | Program | SPEC | A | A PrefixKill | " + string.Join(" | ", round12Variants) + " | class A | class PrefixKill | class Round11 | class NoOwnValue |\n|"
    + string.Concat(Enumerable.Repeat(" --- |", round12Variants.Length + 9)));
foreach (var c in Cases.All().Where(Cases.Round11))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : !c.Err && a.Errors.Count > 0 ? " (over-strict)" : "");
    var a0 = new Analysis(c, Mode.A); var pk = new Analysis(c, Mode.A, Abl.PrefixKill);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(pk)} | {string.Join(" | ", round12Variants.Select(x => W(new Analysis(c, Mode.A, x))))} | {Cls(c, a0)} | {Cls(c, pk)} | {Cls(c, new Analysis(c, Mode.A, Abl.Round11))} | {Cls(c, new Analysis(c, Mode.A, Abl.NoOwnValue))} |");
}
// Case 23a review (cases 50-94 and their G110 forms): A, FirstDerefBase, PrefixKill (no base: a destroyed Move Path ends
// every Loan whose path it prefixes exactly) and round 2's KillAcrossDeref.
Console.WriteLine("\nCase 23a review cases (round-10 rules):\n| Case | Program | SPEC | A | A FirstDerefBase | A PrefixKill | A KillAcrossDeref | class A | class FirstDerefBase |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
foreach (var c in Cases.All().Where(Cases.Review23a))
{
    string W(Analysis a) => V(a) + (c.Err && a.Errors.Count == 0 ? " **UNSOUND**" : "");
    var a0 = new Analysis(c, Mode.A); var fd = new Analysis(c, Mode.A, Abl.FirstDerefBase);
    var spec = c.Err ? "error" + (c.At.Length > 0 ? " at `" + string.Join("`, `", c.At) + "`" : "") : "valid";
    Console.WriteLine($"| {c.Id} | {c.Title} | {spec} | {W(a0)} | {W(fd)} | {W(new Analysis(c, Mode.A, Abl.PrefixKill))} | {W(new Analysis(c, Mode.A, Abl.KillAcrossDeref))} | {Cls(c, a0)} | {Cls(c, fd)} |");
}
var pkDiff = Cases.All().Select(c => (c, a: new Analysis(c, Mode.A, Abl.PrefixKill), b: new Analysis(c, Mode.A, Abl.FirstDerefBase)))
    .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes())).Select(x => $"{x.c.Id}: PrefixKill {V(x.a)}, FirstDerefBase {V(x.b)}");
Console.WriteLine("PrefixKill against FirstDerefBase on all cases: " + string.Join("; ", pkDiff.DefaultIfEmpty("no difference")));
var notSubset = Cases.All().Where(c => !new Analysis(c, Mode.A, Abl.FirstDerefBase).Errors.All(e => new Analysis(c, Mode.A).Errors.Contains(e))).Select(c => c.Id);
Console.WriteLine("Cases where FirstDerefBase reports an error fact design A does not: " + string.Join(", ", notSubset.DefaultIfEmpty("none (FirstDerefBase only removes errors)")));
foreach (var ab in new[] { Abl.None, Abl.FirstDerefBase, Abl.PrefixKill })
    Console.WriteLine($"Review cases, design A{(ab == Abl.None ? "" : " with " + ab)}: " + string.Join(", ", Cases.All().Where(Cases.Review23a)
        .GroupBy(c => Cls(c, new Analysis(c, Mode.A, ab)) is var s && s.StartsWith("(a)") ? "listed loss" : s).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key} ({string.Join(", ", g.Select(c => c.Id))})")));
// Mutations (review N4): FirstDerefBase with one safeguard removed; the error cases each one newly accepts.
foreach (var ab in reviewMutations.Concat([Abl.NoTransit, Abl.IdExclusion]))
{
    var lost = Cases.All().Where(c => c.Err && new Analysis(c, Mode.A, Abl.None, true).Errors.Count > 0 && new Analysis(c, Mode.A, ab, true).Errors.Count == 0).Select(c => c.Id);
    Console.WriteLine($"FirstDerefBase with {ab}: newly UNSOUND " + string.Join(", ", lost.DefaultIfEmpty("none")));
}
Round13(); // round 13: the design (A with PrefixKill), its variants and the cases 130+
Console.WriteLine("\nSummary (all cases):");
foreach (var (ab, fd) in new[] { (Abl.None, false), (Abl.FirstDerefBase, false), (Abl.PrefixKill, false), (Abl.PhantomParentsAtCall, false),
    (Abl.EndedByLoan, false), (Abl.EndedByLoan, true), (Abl.GammaGuardsOnly, false), (Abl.GammaGuardsOnly, true), (Abl.FreshParents, false), (Abl.FreshParents, true), (Abl.FreshOnly, false), (Abl.FreshOnly, true), (Abl.DestroyAsDrop, false), (Abl.DestroyAsDrop, true), (Abl.SeedLiveOutOnly, false), (Abl.SeedLiveOutOnly, true),
    (Abl.Implemented1b, false), (Abl.Implemented1b, true), (Abl.SecureAcquire, false), (Abl.SecureAcquire, true), (Abl.AlwaysSecure, false), (Abl.AlwaysSecure, true),
    (Abl.CalleeNotActivated, false), (Abl.InputsOnCS, false), (Abl.PairAcquireLive, false), (Abl.NoRawStore, false),
    (Abl.Round11, false), (Abl.Round11, true), (Abl.AcquireAtOperation, false), (Abl.AcquireAtOperation, true), (Abl.ShapeReservation, false), (Abl.IntrinsicAllOperands, false),
    (Abl.NoEarlierValues, false), (Abl.NoLaterRoots, false), (Abl.EarlierRoots, false), (Abl.NoOwnValue, false), (Abl.NoOwnValue, true), (Abl.OwnValueEverywhere, false), (Abl.OwnValueEverywhere, true) })
    Console.WriteLine($"Design A{(fd ? " with FirstDerefBase" : "")}{(ab == Abl.None ? "" : (fd ? " and " : " with ") + ab)}: " + string.Join(", ", Cases.All()
        .GroupBy(c => Cls(c, new Analysis(c, Mode.A, ab, fd)) is var s && s.StartsWith("(a)") ? "listed loss" : s).OrderBy(g => g.Key)
        .Select(g => $"{g.Count()} {g.Key}" + (g.Key != "same" ? " (" + string.Join(", ", g.Select(c => c.Id)) + ")" : ""))));
Console.WriteLine();
foreach (var ab in Enum.GetValues<Abl>().Skip(1).Where(x => x is not (Abl.AIssueNotEnded or Abl.IdExclusion or Abl.AText or Abl.LastDerefBase or Abl.FirstDerefBase) && !phantomVariants.Contains(x) && !round10Variants.Contains(x) && !reviewMutations.Contains(x) && !round11Variants.Contains(x) && !round12Variants.Contains(x) && !round13Variants.Contains(x) && !round14Variants.Contains(x) && !round15Variants.Contains(x) && !round16Variants.Contains(x) && !round17Variants.Contains(x) && !round18Modes.Contains(x) && !round19Modes.Contains(x) && !round20Modes.Contains(x)))
{
    string At(Analysis a) => "`" + string.Join("`, `", a.ErrorNodes()) + "`";
    var changed = Cases.All().Where(c => !Cases.NoS1P(c)).Select(c => (c, a: new Analysis(c, Mode.S1P, ab), b: new Analysis(c, Mode.S1P)))
        .Where(x => !x.a.ErrorNodes().SequenceEqual(x.b.ErrorNodes()))
        .Select(x => x.c.Id + ": " + (x.a.Errors.Count == 0
            ? x.c.Err ? "**UNSOUND** (accepted)" : "accepted (SPEC-valid, a loss recovered)"
            : x.c.Err ? "error moves to " + At(x.a) : (x.c.S1Err == true ? "rejected (an intended loss) at " : "over-strict: error at ") + At(x.a)));
    Console.WriteLine($"| {ab} | {string.Join("; ", changed.DefaultIfEmpty("none"))} |");
}
return finalFails;

// One refinement removed each. NoMoveBaseOp, NoUniqBaseOp, ExchangeKills and NoReplaceException change one S1′ clause;
// BaseOpMovePathsOnly limits base_op's K to move paths (plan §4.3 read literally). S1ResolvePlaces is the alternative fix:
// no exclusive-borrow base_op, but Loan places resolved at use.
enum Abl { None, KillKeepOnly, NoMaterialize, SyntacticAccess, NoAuthority, NoClosureFeedback, FlatFlow, NoReservation,
    NoMoveBaseOp, NoUniqBaseOp, BaseOpMovePathsOnly, ExchangeKills, NoReplaceException, S1ResolvePlaces, AIssueNotEnded,
    IdExclusion, AText, // round 4: the pre-fix by-id rule (S1′ and A), and design A's letter (A mode)
    KillAcrossDeref,    // round 5: `ended` on the first move path entered (round 2), not only with no Deref between B and K
    NoTransit,          // round 5: the round-4 rule without the in-transit facts
    LastDerefBase,      // round 6: A mode with the plan's B (last Deref) instead of A2's root-most B
    NoSharedCut,        // round 6: an observing destruction reaches exclusively past shared Derefs
    SharedBaseOp,       // round 6: S1′ plus P3 (a shared borrow of the base or an ancestor is a base operation vs an exclusive child)
    P3RootMost,         // round 6: S1′ plus P3 plus the root-most kill (KillAcrossDeref)
    FirstDerefBase,     // round 8: A mode with the first-Deref B; K an exact prefix of B, any index after K (case 23a proposal)
    PhantomAlone,       // round 9: A mode with rounds 7-8's F6 encoding: the result keeps Φ_c without the receiver's Loans
    PhantomKeepsReceiver, // round 9: A mode, the result keeps the receiver's whole value, its own Loan R included
    PhantomParentsAtCall, // round 9: A mode, Φ_c issued at the call with R2's parents of recv(c), not the receiver's value
    // Round 10 (the chosen 1b design diff). Each removes, or restores, one round-10 rule in design A mode:
    NoRouteReach,       // G119 off: a reserved Loan is reached only as `hits` says (round 9's A3; B's RouteShallow)
    RouteWriteOnly,     // G119 as B's prototype wrote it: Write and Destroy kinds only, not a destroying activation (38g)
    RouteAllParents,    // G119 option B of designer A: every Loan a pending reservation holder carries is reached (not chosen)
    ExcuseAtCallOnly,   // 33c: A3's letter before the round-7 erratum (no derived excuse at the argument reservations)
    LoanPastShared,     // G110 off: A3's letter, a Loan at every Ref, past a shared Deref too, all R2 parents, and the obs cut
    ObsSharedCut,       // G110 on, but the obs cut (`throughShared`) restored: expected vacuous
    NoFreshClosure,     // B1 off: no seed; the closure starts only from entry-set Loans (A3's letter)
    RefParentsInTransit, // a reading of A3's val_in(Ref X): the Ref's R2 parents count as stored values (transit)
    SliceIndexInline,   // Slice index off: an Index of a Slice handle is an inline step (A3's letter)
    // Case 23a review: PrefixKill = no base, a destroyed Move Path K ends every Loan whose whole path K prefixes exactly;
    // mutations of FirstDerefBase: no R2 parents (NoParentsA), Moves and Borrows shallow (NoP3A).
    PrefixKill, NoParentsA, NoP3A,
    EndedByLoan,        // finding 1 off: err(ended) by the Loans issued into H for every EndBorrow (A3 §5's row); no γ_H
    GammaGuardsOnly,    // GuardOrigin, but every other EndBorrow keeps the Loan row
    FreshParents,       // B1 without the CS_call seed: a stored Ref's own Loan and R2 parents seed every check at ℓ
    FreshOnly,          // 39o's row (the chosen 1b rule before the design review): only a stored Ref's own direct Loan seeds
    DestroyAsDrop,      // a destroying call's destruction as a Drop checked on CS, with the drop's write (rounds 6-10's encoding)
    SeedLiveOutOnly,    // E5's stored value only into a holder in live_out(ℓ) or E, not one made live by the closure
    // Round 11 (E13, E14). Each restores one behavior of the reviewed 1b branch (claude/a5-1b-core 8961f577), in A mode:
    CalleeNotActivated, // B1: a Value callee's reservation is never activated (it stays `ref` in its holder)
    InputsOnCS,         // B2: the accesses a Call's inputs make at the Call are checked on CS(ℓ), the holders live after ℓ
    PairAcquireLive,    // E13 (3) off: a PairAcquire Reborrow is a live Uniq Loan wherever it is formed (no reservation)
    NoRawStore,         // B4: a raw-storing intrinsic stores nothing (A3 §4: every operand → result)
    Implemented1b,      // all four above: the reviewed branch
    SecureAcquire,      // round 11's finding: the MIR also secures a PairAcquire that a later operand left at its Call follows
    AlwaysSecure,       // reviewer (E13 M'): every PairAcquire operand is secured into its own temp (Case.Always)
    // Round 12 (A3 E13, the owner's option R1: A5 handles operand evaluation order). Design A runs E13 as stated on the round-12
    // builder's MIR (M′ and the input marker). Each variant restores an earlier rule or MIR form, or changes one clause of CS_i:
    Round11,            // round 11's E13: a Call's inputs on CS_call(ℓ, −), any other operation's operands on CS(ℓ); round 11's MIR (G123)
    AcquireAtOperation, // E13 (1) on G123's MIR: a PairAcquire an operation's later operand follows is left at ℓ as an operand (no M′)
    ShapeReservation,   // no marker: A5 derives a PairAcquire's reservation from the MIR shape `$t = acquire X` moved into a Call (Case.Shape)
    IntrinsicAllOperands, // A3 §4's intrinsic row that E14 replaced: every operand → result, Value (Case.IntrinsicRow)
    NoEarlierValues,    // CS_i without ⋃_{j<i} val_in(o_j)
    NoLaterRoots,       // CS_i without ⋃_{j>i} σ_in(root(o_j))
    EarlierRoots,       // CS_i with σ_in(root(o_j)) for j<i instead of val_in(o_j) (round 11's CS_call(ℓ, −) at every operation)
    NoOwnValue,         // E13 as first stated: CS_i with val_in(o_j) for j < i only, and no own value at a single-operand rvalue
    OwnValueEverywhere, // the own-value term on every node Read or Move (Calls, BinaryOps, discards, Discriminant and Len reads
                        // included): an upper bound for the operands rounds 1-11 encode without marking them
    // Round 13 (A3 E13 rewritten, "one rule for every access"; the second design review's flags DOV, ROP, AV, ROS+RPT folded in).
    // The design takes each term; each variant restores the clause it superseded, or tries the review's alternative:
    NoDestroyValue,     // DOV off: an observing destruction of X does not take val(X) (round 12: CS(ℓ), CS_call(ℓ, t_K))
    BorrowSeedOnly,     // ROP off: DECISIONS R4-M57, a borrow's stored value only seeds (E5), into a live destination
    ParentsAsTransit,   // ROP off, the review's other fix: the R2 parents' holders in transit (RefParentsInTransit) and the
                        // stored value seeding whatever the destination's liveness (ROS)
    ActivationLiveIn,   // AV off: an activation and a destroying call's destroy on CS_call(ℓ, t)'s live_in(ℓ) (round 12)
    CurrentBuilder,     // today's builder MIR (Case.Built): named arguments in slot order, captures without G123 or M′
    KeepCSTerms,        // a borrow and an observing Drop also take CS(ℓ)'s transit and E5's seed (the review's additive run)
    BorrowWithoutSeed,  // round 13's first run: the borrow row without E5's seed (its own Loan into a live destination): 41o accepted
    ParentsEveryLayer,  // the borrow row's parents over every layer, ⋃_{j<k}, instead of E3's bound ⋃_{m≤j<k} (round 13's first run)
    OnePointDrop,       // the one-point encodings of 24c, 29b and 91 (Case.OnePoint): a drop and a store at one location
    // Round 14 (1c's 26b, DECISIONS R4-M68): the design's activation meets only `ref` Loans in the holder found.
    ActivationEveryMode, // round 13's err(activate): an activation conflicts with a Loan of any mode
    NoFormationReport,  // rejected (the old path's choice): a reservation's formation reports nothing, its activation every mode
    // Round 15 (PLAN G148; round 14 of its design reviews), the review's union of both designers' store-row rules (Analysis.Rule). Abl.None is A5 at 806110fc.
    R14A, R14AX, R14AUniqX, R14ARoute,            // designer a: A (any mode, no guard), A + X, exclusive kind + X, route exclusion past layer 1
    R14B, R14BLiteral, R14BAllModes, R14BNoGuard, // designer b: A′ (exclusive kind, Type guard, dominator exclusion on Content rows), no exclusion,
    R14BTarget, R14BAllRoutes, R14BValueRows,     //   any mode, no guard, target's own route only, every route, Value rows too
    R14G, R14GX, R14GXS, R14GXSTransitA, R14GDom, // the review: any mode + guard; + X; + X where ¬SelfRef (also with a's transit); + dominators
    // Round 16 (PLAN G148's final review B1; the owner's route taint, 2026-10-19): rule G (R14G) with P13's interim (DECISIONS R4-M73, the
    // planning session's ruling (B): a value a call stores only past a holder in Case.ByArgument carries P13's marker τ13) and R4-M70's
    // default taint (Node.DefaultTaint: the value a default places into its pending slot D carries τD:D); a conflict whose Loan only tainted
    // sets hold is a located Unsupported_Kd (Analysis.Unsupported). Each runs the check one way:
    R16Set,           // the unit as reviewed (b335393b): a Loan is clean where a set without a taint holds it; err(ended) has no taint check
    R16Route,         // the owner's route taint: a holder the closure reaches only through tainted sets is tainted, err(ended)'s γ too
    R16RouteNoGamma,  // ablation: the route rule for Loans only, err(ended) unchecked
    R16GammaOnly,     // ablation: the set rule for Loans and for γ (err(ended) checked on the found set only)
    // Round 17 (round 16's review B1; the owner's body hold, 2026-10-19): on rule G with R4-M70's default taint as before (markers, the
    // set rule) and no P13 marker:
    R17Hold,          // the decided rule: a body where some store row puts a non-empty value into a target reached only past a ByArgument
                      // holder is held, and every Loan problem of a held body (err(ended) included) is a located Unsupported_Kd at its access
    R17PerPart,       // the reference for dependence and cost: rule G whose W takes no step from a ByArgument holder (the per-part guard's
                      // program, R4-M76), with the default taint as R17Hold
    // Round 18 (ER-effects, A5-F5): on R17Hold, the effect rules (R18); each variant removes or replaces one of them:
    R18,              // the round's rules R1-R4 (see the header), with G136's static items as the compiler has them
    R18NoW6,          // a reservation stays `ref` in its holder at its own call's effects (E13 (1) before W6)
    R18LiveOnly,      // effects on the holders live after ℓ and E only: no operand's value, no Drop's val(X)
    R18NoR3,          // no destruction's own exclusive Loan on a static (16.4 unread)
    R18Merged,        // G93's merged `bool` (R4-M38): Unknown, Limit and Derived all as Unknown
    R18EnsureExact,   // P42's exact rule: a live Loan proves its own key initialized (Case.StaticKey), so its initializer does not run
    R18EnsureByField, // P5 read as a proof: any live Loan on the ensured Field proves it initialized
    R18EnsureIgnored, // no proof: an ensure always takes its initializer's keys
    R18EnsureClosed,  // design A (2b-D): a closed Field's live Loan proves it initialized (one key); a generic Field's is R18's located U
    R18StaticEnds,    // G136 the other way: a static's whole write ends its Loans as a Move Path's does (E8's PrefixKill)
    // Round 19 (ER-defaults, A5-F9): on R18, E15, R26-borrow, R26-access, P9's rows with the raw-pointer row, and R-a without R4-M70's taint (R19); each
    // variant replaces or removes one of them:
    R19,              // the round's rules (see the header)
    R19Taint,         // a copy keeps R4-M70's taint instead of R-a (a template call's is deleted by 2e-1 in every variant)
    R19InlinedDrop,   // the audit's option (a): a copy keeps the taint's marker, and a problem found only through it is dropped, never Unsupported_Kd
    R19NoEnding,      // neither R-a nor the taint: the inspection Loans live on to the call
    R19SlotsOnly,     // R-a ends only the inspection Loans on PendingSlot roots, not those on a reserved explicit argument's temp (Case.ReservedTemps)
    R19ValueRows,     // a template call keeps the unknown-callee Value rows (R4-M57, R4-M63) instead of P9's Content rows
    R19NoRawRow,      // P9's rows without 2e-D's raw-pointer row
    R19SharedStep,    // today's step class: a PreparedInput's Deref is a shared Deref (Case.PreparedShared), so no Loan forms past it
    R19ReturnOnly,    // R26-borrow seeded by σ(_0) alone, without σ(E)
    R19AccessAll,     // R26-access also on an exclusive Loan R26-borrow reported (two codes for one failure)
    R19RawAccess,     // R26-access also on a write through a raw pointer read from a slot
    // Round 20 (PLAN G171, unit 2x-P11): on R19, whether a failed comparison inside an inline default copy is its template's. R20Own is the candidate
    // rule (the M69 plan's option 3); each variant reads other scopes or decides at another time:
    R20Own,           // participating scopes: the access's, the conflicting Loan's issuing and an activation's (or a destroying call's destroy's) reservation's
                      // issuing scope; err(ended): the EndBorrow's and the guard local's; R3: the Drop's. The deepest Inlined scope containing them is the
                      // template boundary (the template's failure, omitted); none: the current body's. Decided per candidate, before the first is chosen
    R20AccessOnly,    // the access scope alone (R4-M69's test, per candidate)
    R20IssueOnly,     // the conflicting Loan's issuing scope alone
    R20NoReservation, // R20Own without an activation's reservation scope
    R20AfterSelect,   // R20Own decided after first-candidate selection (the recorder exception): a covered first candidate (Case.LoanOrder) hides the record
    R20SameSpan,      // R20Own with the copies of one template at one nesting taken as one scope (equal source spans)
    R20Entry }        // round 20's repair candidate: a Loan carried into the boundary's entry from outside it is an earlier evaluation's, so the boundary
                      // moves out past it

record StoreRow(string Arg, string Src, bool Content, int Depth, int Hidden, bool Raw); // round 15 (G148)
enum K { Read, Write, Move, SBorrow, UBorrow, Destroy }
enum Mode { PROV, S1, S1P, A }
// SharedAt (round 10, G110): the number of borrow Derefs of Place up to and including its last shared one (ref, objref,
// a ref-case pair layer); 0 when the path has no shared Deref.
// Marker (round 10, finding 1, GuardOrigin): γ_H, the guard-local Origin g of a guard candidate H (SPEC 14.8.3); it names no
// Place and never conflicts.
// Pair (round 11, E13 (3)): a PairAcquire Reborrow secured into a temp, a reservation there.
// OpAccess (round 12, AcquireAtOperation's MIR): a Borrow left at its operation as an operand; its access is that operand's.
record Issue(string Id, string Place, bool Uniq, string Into, bool Reserve, bool Param, bool InPlace = false, bool Phantom = false, string Recv = null, int SharedAt = 0, bool Marker = false, bool Pair = false, bool OpAccess = false);
// InPlace: the exclusive borrow is a `replace` target. Input (round 11, E13 (1)): an access a Call's input makes at the Call.
// From (round 13): the Issue whose borrow access this is (a Ref's or a Reborrowing PairAcquire's borrow of its Place).
record Acc(K Kind, string Place, string Except, bool InPlace = false, bool Input = false, Issue From = null);
record Res(HashSet<string> Names, HashSet<string> Leaves, HashSet<string> Auth);

// Holder and Place paths: segments joined by '.', "*" = follow a reference, "[]" = non-path index, digits = constant index.
static class Path
{
    public static string[] Segs(string p) => p.Split('.');
    static bool IsIdx(string s) => s == "[]" || s.All(char.IsDigit);
    static bool SegEq(string a, string b) => a == b || (a == "[]" && IsIdx(b)) || (b == "[]" && IsIdx(a));
    public static bool Within(string k, string h) { var a = Segs(k); var b = Segs(h); return a.Length >= b.Length && b.Select((s, i) => SegEq(a[i], s)).All(x => x); }
    public static bool Overlap(string a, string b) => Within(a, b) || Within(b, a);
    public static string Rest(string k, string h) => string.Join('.', Segs(k).Skip(Segs(h).Length));
    public static bool DerefBelow(string k, string h) => Segs(k).Skip(Segs(h).Length).Contains("*");
    // Access a against Loan place l. Inline parts exclude referents (§15.6.2).
    public static bool Hits(string a, string l) => Within(a, l) || (Within(l, a) && !DerefBelow(l, a));
    public static bool RunsThrough(string l, string h) => Within(l, h) && DerefBelow(l, h);
    public static string Join(string t, string rest) => rest.Length == 0 ? t : t + "." + rest;
    public static string Cap(string p) => string.Join('.', Segs(p).Take(8)); // PROV widening: a prefix overlaps more
    public static string Before(string p, string seg) { var s = Segs(p); int i = Array.IndexOf(s, seg); return i < 0 ? p : string.Join('.', s.Take(i)); }
    public static bool Escaped(string h) => h.StartsWith('^') || Within(h, "$ret");
}

class Node
{
    public readonly string Label; public readonly int Ix;
    public readonly List<Issue> Issues = new(); public readonly List<(string From, string To)> Flows = new();
    public readonly List<string> Defs = new(), Uses = new(), Activates = new(), Assumed = new(), Exchanges = new(), Disturbs = new(), Excuses = new(), EndBorrows = new();
    // Case 23a review: A3's EndBorrow H (guard cleanup): a use of H, then σ(H) = ∅; err(ended): a Loan issued into H still checked.
    public Node EndBorrow(string h, bool guard = true) { EndBorrows.Add(h); if (guard) GuardEnds.Add(h); Uses.Add(h); return this; }
    public readonly List<string> GuardEnds = new(); // round 10: EndBorrows of guard candidates (MirLocalRole.GuardCandidate)
    // F6 (S2, A3 §5): Φ_c, a phantom uniq Loan on the receiver's referent carried by the result; a disturbing point
    // turns Φ_c into Φ_c^dist in every set; a later preserves call on the same receiver excuses an undisturbed Φ_c.
    // recv: the receiver's reservation Loan R. Round 9 (G113): the result keeps the receiver's value with R replaced
    // by Φ_c, so Φ_c's values carry its parent Loans, as a child's do.
    public Node Phantom(string id, string place, string into, string recv) { Issues.Add(new(id, place, true, into, false, false, false, true, recv)); return this; }
    public Node Disturb(string id) { Disturbs.Add(id); return this; }
    public Node Excuse(string id) { Excuses.Add(id); return this; }
    public readonly List<(string Place, string Src)> Throughs = new(); public readonly List<Acc> Accs = new();
    public List<string> Succ;
    public Node(string label, int ix) { Label = label; Ix = ix; }
    public Node Use(params string[] h) { Uses.AddRange(h.Select(p => Path.Before(p, "*"))); return this; }
    public Node Def(params string[] h) { Defs.AddRange(h); return this; }                        // replaced(H, P)
    public Node Flow(string from, string to) { Flows.Add((from, to)); return Use(from); }          // flow(H1, H2, P)
    public Node Borrow(string id, string place, bool uniq, string into, bool reserve = false, bool inPlace = false, int sharedAt = 0) // issued(L, place, mode, P, H)
    { Issues.Add(new(id, place, uniq, into, reserve, false, inPlace, false, null, sharedAt)); return place.Contains('*') ? Use(place) : this; }
    public Node Param(string id, string h) { Issues.Add(new(id, "^" + h, true, h, false, true)); return Def(h); } // escaped referent
    public Node A(K kind, string place) { Accs.Add(new(kind, place, null)); return place.Contains('*') ? Use(place) : this; }
    public Node Assign(string target) { Def(target); return A(K.Write, target); }               // whole write of a move path
    public Node Through(string place, string src) { Throughs.Add((place, src)); A(K.Write, place); return Use(src); } // written_through
    public Node Activate(string id) { Activates.Add(id); return this; }                          // reserved(L, _, P) ends here
    public Node Exchange(string h) { Exchanges.Add(h); return this; } // exchange/swap target: wholly replaced, old value moved out
    public Node Drop(string h) => A(K.Destroy, h);                    // destroying h's old value runs a user drop that may reach through it
    // Review r2: A3's err(destroy) at a destroying call, checked on CS_call(ℓ, t_K) (live into the call, t_K excluded, seeded by σ_in(t_K)).
    public Node DestroyAt(string h, string reservation) { Accs.Add(new(K.Destroy, h, reservation)); return this; }
    // Round 11 (E13): a Call's inputs. Input: the access an input makes AT the Call (a Copy or Move of a direct Place operand
    // left at the Call), checked on CS_call(ℓ, −); AcquireAt: an exclusive PairAcquire Reborrow left at the Call, whose
    // formation and activation coincide there; Acquire: one secured into a temp, a TwoPhaseUniq reservation issued into it
    // (activated by Activate at its Call); Callee: a Value callee's reservation, activated at the Call like an argument's.
    // Round 12 (E13 (1)): Input marks every operand the MIR leaves at its operation ℓ, a Call's or any other's, in evaluation
    // order (the order of the Input calls); a Move of a secured temp included. Operation marks an operation other than a Call
    // (an Aggregate, a Closure, a BinaryOp), whose operands Round11 checks on CS(ℓ) as before round 11.
    public Node Input(K kind, string place) { Accs.Add(new(kind, place, null, Input: true)); return Use(place); }
    public Node AcquireAt(string place) => Input(K.UBorrow, place);
    public bool NotCall;
    public Node Operation() { NotCall = true; return this; }
    // Round 12 (AcquireAtOperation's MIR): a PairAcquire left at a non-Call operation as its operand, a live Uniq Loan stored
    // into `into` (the aggregate), its access the operand's, in order.
    public Node AcquireOp(string id, string place, string into) { Issues.Add(new(id, place, true, into, false, false, OpAccess: true)); return Input(K.UBorrow, place); }
    public Node Acquire(string id, string place, string into) { Issues.Add(new(id, place, true, into, true, false, Pair: true)); return Use(place); }
    public readonly List<string> Callees = new();
    public Node Callee(string id) { Callees.Add(id); return Activate(id); }
    // Round 11 (E14): a Call whose contract writes the value of `src` into storage reached through the raw pointer `ptr`
    // stores it into E, the referent of a raw pointer (A3 §5: val(raw-derived Place) ∪= {Φ_E}; SPEC 15.6).
    public readonly List<(string Ptr, string Src)> RawStores = new();
    public Node RawStore(string ptr, string src) { RawStores.Add((ptr, src)); return Use(ptr, src); }
    // Round 15 (G148): a store row of a Call: its value ret(i, κ) (Content: content(σ(src)); Value: σ(src) ∪ content(σ(src)), src a
    // reference) into the writable storage of the reference argument held in `arg` (a_j). Depth: the layers of that storage its
    // Type positions show (1: the referent; int.MaxValue: unknown, an abstract carrier or an unknown callee); Hidden: the layers when
    // nominal Types' fields are walked (0: as Depth); Raw: a raw pointee is among them. Names: '#' a local holding no Loan, '%' a
    // raw-pointer local (no holder either); neither is a store target (A5: no slot).
    public readonly List<StoreRow> Stores = new();
    // Round 16 (R4-M70): a default's evaluation places the value it stores into its pending slot D, which carries D's default taint.
    public readonly List<string> Taints = new();
    public Node DefaultTaint(string slot) { Taints.Add(slot); return this; }
    public Node Store(string arg, string src, bool content, int depth, int hidden = 0, bool raw = false)
    { Stores.Add(new(arg, src, content, depth, hidden, raw)); return Use(arg, src); }
    // Review (G148 union): designer b's encoding of the same row (its depths served only option B).
    public Node Retain(string arg, string src, bool content, params int[] depths) => Store(arg, src, content, depths.Length == 0 ? 1 : depths.Max());
    // Round 18 (A5-F5): A4's keys at a Call, a Drop or a staticEnsure (MirEffects.Keys: each mutable static Field once, written where any
    // access writes it) and its classes (G93: Unknown, Limit, Derived); a staticEnsure names its Field and the key it ensures (P42's Type
    // arguments; "" for a non-generic group), and its keys are its initializer's.
    public readonly List<(string F, bool Write)> Keys = new(); public bool Unknown, Limit, Derived; public string Ensure, EnsureKey;
    public bool HasEffects => Keys.Count > 0 || Unknown || Limit || Derived;
    public Node Key(string f, bool write = false) { Keys.Add((f, write)); return this; }
    public Node Fx(bool unknown = false, bool limit = false, bool derived = false) { Unknown |= unknown; Limit |= limit; Derived |= derived; return this; }
    public Node StaticEnsure(string f, string key = "") { Ensure = f; EnsureKey = key; return this; }
    // Round 19 (A5-F9): a node of a default copy's Inlined scope (R4-M69 drops a Loan problem found there); a template's Return (R26-borrow); R-a's
    // ending of the omitting call's inspection Loans; a template call's unknown-callee Value rows (today; P9's Content rows are its Flows), 2e-D's
    // raw-pointer row (each source's Content into E) and R4-M70's taint on its result (deleted by 2e-1).
    public bool InScope, Returns;
    public readonly List<string> Ends = new(), RawRows = new(), TemplateTaints = new();
    public readonly List<(string From, string To)> UnknownFlows = new();
    public Node Inlined() { InScope = true; return this; }
    public Node Return() { Returns = true; return Use("_0"); }
    public Node EndInspections(params string[] ids) { Ends.AddRange(ids); return this; }
    public Node UnknownRow(string from, string to) { UnknownFlows.Add((from, to)); return Use(from); }
    public Node RawRow(params string[] srcs) { RawRows.AddRange(srcs); return Use(srcs); }
    public Node TemplateTaint(string slot) { TemplateTaints.Add(slot); return this; }
    // Round 20: the scope the node lies in (Case.Scopes; 0: the body's root scope). A round-19 node marked Inlined() lies in one implicit copy scope.
    public int Scope;
    public Node Goto(params string[] labels) { Succ = labels.ToList(); return this; }
    public Node Assume(string label) { Assumed.Add(label); return this; }                        // assumed edge (§14.10.3)
}

class Case
{
    public readonly string Id, Title; public readonly List<Node> Nodes = new();
    public bool Err; public string[] At = []; public bool? S1Err; public string[] S1At; public bool S1POnly; public Func<Analysis, string> Extra;
    public string ALoss; // the listed design-A loss that explains an extra rejection (round 3)
    public Case Inline;  // round 10: the same program with a Slice Index as an inline step (SliceIndexInline, A3's letter)
    public Case AsDrop;  // round 10: the same program with a destroying call's destruction as a Drop on CS (DestroyAsDrop)
    public Case Always;  // reviewer: M', every PairAcquire secured into its own temp (AlwaysSecure)
    public Case Secured; // round 11: the same program with a PairAcquire that a later at-Call input follows secured (SecureAcquire)
    public Case Shape;        // round 12: the same program with A5 deriving a reservation from the MIR shape (ShapeReservation)
    public Case IntrinsicRow; // round 12: the same program with A3 §4's intrinsic row, every operand → result (IntrinsicAllOperands)
    public Case Built;        // round 13: the MIR today's builder makes where E13 (2) prescribes another order (CurrentBuilder)
    public Case OnePoint;     // round 13: rounds 5-12's encoding with a drop and a store at one location (OnePointDrop)
    // Review (G148 union): holder facts the store-row variants read: designer b's SharedOnly and RawRoots; NonHolders, locals that hold
    // no Loan (designer a's '#'); SelfRef, holders whose Type may hold a reference into their own storage or into storage they reach
    // through exclusive references (the review's guard); LetterErr, SPEC 15.6.4's per-argument letter where it departs from Err.
    public readonly HashSet<string> SharedOnly = new(), RawRoots = new(), NonHolders = new(), SelfRef = new(); public bool? LetterErr; public string Rust;
    // Round 16: holders whose Type passes rule G's guard only through a Type argument, a pair layer or an abstract part (MirWrites
    // .ThroughArgument), and the nodes the round's decided rule reports a located Unsupported_Kd at.
    public readonly HashSet<string> ByArgument = new(); public string[] UnsuppAt = [];
    // Round 18: the static roots ('#' names: a static's Loan names no holder, G136) and whether each is mutable; a static Loan's key where
    // the Field is a generic container's (P5 merges keys; the exact rule needs them).
    public readonly Dictionary<string, bool> Statics = new(); public readonly Dictionary<string, string> StaticKey = new();
    // Round 19: a template body's PreparedInputs, the slot Places that hold a raw pointer (a write past one is raw access), today's encoding of its
    // PreparedInput step (R19SharedStep), a caller's reserved explicit arguments' temps (R19SlotsOnly); R19's expected DefaultArgumentBorrow_Kd and
    // DefaultArgumentAccess_Kd nodes; for a SPEC error A5 leaves to another check, that check (Backstop) or the PLAN issue that lists it (Known);
    // for a caller whose program's one error is its template's, that template (DupOf: a record at the call is a second one).
    public readonly HashSet<string> Prepared = new(), RawAt = new(), ReservedTemps = new(); public Case PreparedShared;
    public string[] BorrowAt = [], AccessAt = []; public string Backstop, Known, DupOf;
    // Round 20: the scope tree (MirBody.Scopes: each scope's Outer, Outer < index; every scope but the root an Inlined copy scope), each copy's label
    // and Loan-id prefix and its template's id (R20.Template); the compiler's candidate order where it is not the issue order; R20Own's expected
    // omissions (template-owned failures); a finding the case records (R20Own's result there is listed, not a failure).
    public readonly List<(int Outer, string Prefix, string Template)> Scopes = new(); public string[] LoanOrder, OmitAt = []; public string Finding;
    public Case(string id, string title) { Id = id; Title = title; }
    public Node N(string label) { var n = new Node(label, Nodes.Count); Nodes.Add(n); return n; }
    public Case Error(params string[] at) { Err = true; At = at; return this; }
    public Case Valid() { Err = false; return this; }
    public Case S1(bool err, params string[] at) { S1Err = err; S1At = at; return this; }
}

class Analysis
{
    readonly Mode M; readonly Abl X; readonly bool F; readonly List<Node> ns; readonly int N; // F: FirstDerefBase under another variant (review)
    readonly bool Pk; // round 13: E8's ending (PrefixKill) under another variant; the design is Mode.A with it
    readonly Dictionary<string, Issue> issue = new(); readonly Dictionary<string, int> issueAt = new(), activeAt = new();
    readonly Dictionary<string, HashSet<string>> places = new();
    readonly HashSet<string>[] excusedAt;
    readonly HashSet<string> gamma = new(); // the holders with a region marker
    HashSet<Fact>[] In; HashSet<string>[] liveIn; List<int>[] succ;
    public readonly List<(string L, int P, string What)> Errors = new();
    public readonly List<(string L, int P, string What)> Unsupported = new(); // round 16: located Unsupported_Kd, a conflict only taints carry
    public readonly List<(string L, int P, string What)> Reclassified = new(); // round 17: each Unsupported_Kd's conflict, by its access
    public readonly List<(string Code, string L, int P, string Param)> DefaultProblems = new(); // round 19: DefaultArgument{Borrow,Access}_Kd, related parameter
    public readonly List<(string L, int P, string What)> Dropped = new(); // round 19: Loan problems a drop rule removes (R4-M69's Inlined scope, R19InlinedDrop)
    public readonly List<(string L, int P, string What, int B)> Omitted = new(); // round 20: failures a mode gives a template (its boundary scope B; 0: none, hidden)
    readonly Dictionary<string, int> issueSeq = new(); // round 20: each Loan's issue order (the candidate order unless Case.LoanOrder gives one)
    public readonly Case Enc; // round 12: the MIR form analysed (the case itself, or one of its other forms)

    public Analysis(Case c, Mode m, Abl x = Abl.None, bool firstDeref = false, bool prefixKill = false)
    {
        // Round 12: the MIR form. Design A and its variants run the round-12 builder's MIR (M′: Always, else Secured, else the
        // case's own); Round11, Implemented1b and AcquireAtOperation run round 11's (G123: the case's own); SecureAcquire runs M.
        // Round 13: CurrentBuilder runs today's builder MIR where E13 (2) prescribes another one (Case.Built).
        M = m; X = x; F = firstDeref; Pk = prefixKill;
        Enc = x == Abl.R19SharedStep && c.PreparedShared != null ? c.PreparedShared : x == Abl.CurrentBuilder && c.Built != null ? c.Built : x == Abl.OnePointDrop && c.OnePoint != null ? c.OnePoint : x == Abl.SliceIndexInline && c.Inline != null ? c.Inline : x == Abl.DestroyAsDrop && c.AsDrop != null ? c.AsDrop
            : x == Abl.ShapeReservation && c.Shape != null ? c.Shape : x == Abl.IntrinsicAllOperands && c.IntrinsicRow != null ? c.IntrinsicRow
            : x == Abl.SecureAcquire ? c.Secured ?? c : x is Abl.Round11 or Abl.Implemented1b or Abl.AcquireAtOperation ? c
            : c.Always ?? c.Secured ?? c;
        ns = Enc.Nodes; N = ns.Count;
        foreach (var n in ns)
        {
            foreach (var s0 in n.Issues)
            {
                var s = x == Abl.NoReservation || s0.Pair && PairLive ? s0 with { Reserve = false } : s0; // PairLive: a live Uniq Loan
                issue[s.Id] = s; issueAt[s.Id] = n.Ix; places[s.Id] = new(); if (s.Param || m != Mode.PROV) places[s.Id].Add(s.Place);
                issueSeq.TryAdd(s.Id, issueSeq.Count);
            }
            if (x != Abl.NoReservation) foreach (var a in n.Activates) activeAt[a] = n.Ix;
        }
        if (PairLive) foreach (var a in activeAt.Keys.Where(a => issue[a].Pair).ToList()) activeAt.Remove(a); // never reserved, never activated
        foreach (var d in ns.SelectMany(n => n.Disturbs).Distinct()) // Φ_c^dist: the same phantom, no longer excusable
        { issue[d + "^dist"] = issue[d] with { Id = d + "^dist" }; issueAt[d + "^dist"] = issueAt[d]; places[d + "^dist"] = new(places[d]); }
        // Round 10, 33c (A3 §5 excused, second row, the round-7 erratum): excused(L, ℓ) also where ℓ forms an argument
        // reservation of a Call c2 with excused(L, c2). Derived from c2's activations, never stated per formation.
        // Round 10 (finding 1, planning-session decision, GuardOrigin): a guard candidate H (MirLocalRole.GuardCandidate) has
        // the marker γ_H of its guard-local Origin,
        // issued into H by every Ref and every whole Copy/Move into H, carried like a Loan, never ended, never conflicting.
        if (m == Mode.A && x != Abl.EndedByLoan)
            foreach (var h in ns.SelectMany(n => n.GuardEnds).Select(Root).Distinct())
            { gamma.Add(h); issue["γ" + h] = new("γ" + h, "γ" + h, false, h, false, false, Marker: true); places["γ" + h] = ["γ" + h]; }
        // Round 16: P13's marker and each pending slot's default taint, carried like γ (with the values that hold them), never ended, never
        // conflicting.
        if (Defaults)
            foreach (var t in ns.SelectMany(n => R19Mode ? n.Taints : n.Taints.Concat(n.TemplateTaints)).Select(d => "τD:" + d).Prepend("τ13").Distinct())
            { issue[t] = new(t, t, false, t, false, false, Marker: true); places[t] = [t]; }
        excusedAt = ns.Select(n => n.Excuses.ToHashSet()).ToArray();
        if (m == Mode.A && x != Abl.ExcuseAtCallOnly)
            foreach (var n in ns) foreach (var r in n.Activates) excusedAt[issueAt[r]].UnionWith(n.Excuses);
        BuildEdges(); Carries();
        // Round 17: a body is held where, at the fixpoint, some store row puts a non-empty value into a target reached only past a ByArgument
        // holder (the first P13 target the compiler's Writes returns; an empty value holds nothing).
        if (X == Abl.R17Hold || Effects) // round 18: on R17Hold
            held = ns.Any(n => n.Stores.Any(r => P13Store(In[n.Ix].Where(t => !EndedBy(t.L, n.Defs)).ToList(), r)));
        if (m == Mode.A) { LivenessA(); CheckA(); } else { Liveness(); Check(); }
        if (R19Mode && Enc.Prepared.Count > 0) PreparedRules();
    }

    // Round 11: the reviewed 1b branch's behaviors (each variant restores one; Implemented1b all four).
    bool CalleeOff => X is Abl.CalleeNotActivated or Abl.Implemented1b;
    bool InputsOnCS => X is Abl.InputsOnCS or Abl.Implemented1b;
    bool PairLive => X is Abl.PairAcquireLive or Abl.Implemented1b;
    bool NoRawStore => X is Abl.NoRawStore or Abl.Implemented1b;
    // Round 12: round 11's E13 (1) (a Call's inputs on CS_call(ℓ, −), any other operation's operands on CS(ℓ)), kept by the
    // variants that reproduce round 11's runs.
    bool R11Rule => X is Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure;
    // Round 12 (planning session on finding 1): an operand's value, its own included, is used by its operation after the
    // access (SPEC 15.6; rustc E0505). Off in the variants that reproduce earlier rules.
    bool OwnValue => X is not (Abl.NoOwnValue or Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure or Abl.Implemented1b);
    // Round 13 (E13 rewritten): the values each access takes. Off in the variants that reproduce rounds 11-12's runs.
    bool Earlier => X is Abl.Round11 or Abl.SecureAcquire or Abl.AlwaysSecure or Abl.Implemented1b;
    bool DestroyValue => X != Abl.NoDestroyValue && !Earlier;                                   // an observing destruction takes val(X)
    bool BorrowParents => X is not (Abl.BorrowSeedOnly or Abl.ParentsAsTransit) && !Earlier;    // a borrow takes its R2 parents
    bool ActivationOnValues => X != Abl.ActivationLiveIn && !Earlier;                           // ⋃_{o_j≠t} val(o_j), not live_in(ℓ)
    // E13 (1): a borrow and an observing Drop take no transit; a borrow keeps E5's seed (Seed), a Drop takes none. The variants
    // that restore round 12's check for one of them restore its CS(ℓ) (transit and seed) with it; KeepCSTerms keeps CS(ℓ) and adds
    // the own term (the review's additive run).
    bool OwnTerm(Acc a) => !Earlier && X != Abl.KeepCSTerms && (a.From != null ? X is not (Abl.BorrowSeedOnly or Abl.ParentsAsTransit)
        : a.Kind == K.Destroy && a.Except == null && X != Abl.NoDestroyValue);
    // Round 14 (R4-M68): the design's activation conflicts only with a Loan `ref` in the holder found. Off in the runs of earlier
    // rounds (no prefixKill) and in the variants that restore the activation's every mode.
    bool ActivationRefOnly => Pk && X is not (Abl.ActivationEveryMode or Abl.NoFormationReport);
    // Round 16: the taint modes, and which of them follow a taint along the closure's route and check err(ended)'s γ.
    bool Tainting => X is Abl.R16Set or Abl.R16Route or Abl.R16RouteNoGamma or Abl.R16GammaOnly;
    // Round 17: the default taint's markers and the set rule without P13's marker; R17Hold holds a body, R17PerPart stops W at ByArgument.
    bool Defaults => (Tainting || X is Abl.R17Hold or Abl.R17PerPart || Effects) && (!R19Mode || X is Abl.R19Taint or Abl.R19InlinedDrop);
    // Round 18: the effect rules, on R17Hold (rule G, the body hold, the default taint). Round 19 runs on them.
    bool Effects => X is Abl.R18 or Abl.R18NoW6 or Abl.R18LiveOnly or Abl.R18NoR3 or Abl.R18Merged or Abl.R18EnsureExact or Abl.R18EnsureByField
        or Abl.R18EnsureIgnored or Abl.R18StaticEnds or Abl.R18EnsureClosed || R19Mode;
    // Round 19 (A5-F9): the defaults rules; R-a's ending, the template call's rows and the raw-pointer row by variant.
    bool R19Mode => X is Abl.R19 or Abl.R19Taint or Abl.R19InlinedDrop or Abl.R19NoEnding or Abl.R19SlotsOnly or Abl.R19ValueRows or Abl.R19NoRawRow
        or Abl.R19SharedStep or Abl.R19ReturnOnly or Abl.R19AccessAll or Abl.R19RawAccess || R20Mode;
    // Round 20: template ownership by participating scopes, on R19 (R-a, no default taint).
    bool R20Mode => X is Abl.R20Own or Abl.R20AccessOnly or Abl.R20IssueOnly or Abl.R20NoReservation or Abl.R20AfterSelect or Abl.R20SameSpan or Abl.R20Entry;
    bool UnknownRows => !R19Mode || X == Abl.R19ValueRows;
    bool RawRowOn => R19Mode && X != Abl.R19NoRawRow;
    bool Inspected(Node n, string l) => R19Mode && X is not (Abl.R19Taint or Abl.R19InlinedDrop or Abl.R19NoEnding) && n.Ends.Contains(l)
        && (X != Abl.R19SlotsOnly || !Enc.ReservedTemps.Contains(Root(issue[l].Place)));
    IEnumerable<(string From, string To)> FlowsOf(Node n) => UnknownRows ? n.Flows.Concat(n.UnknownFlows) : n.Flows;
    bool held; // round 17: the body is held (R17Hold)
    public bool Held => held;
    bool RouteTaint => X is Abl.R16Route or Abl.R16RouteNoGamma;
    bool GammaTaint => X is Abl.R16Route or Abl.R16GammaOnly;
    static bool IsTaint(string l) => l.StartsWith('τ');

    int IxOf(string l) { int i = ns.FindIndex(n => n.Label == l); return i >= 0 ? i : throw new Exception("no label " + l); }

    // edge(P, Q): runtime edges, plus assumed edges into nodes no runtime path reaches (§14.10.3, DEC R4-M20).
    void BuildEdges()
    {
        succ = ns.Select(n => n.Succ?.Select(IxOf).ToList() ?? (n.Ix + 1 < N ? [n.Ix + 1] : new List<int>())).ToArray();
        var reach = new bool[N]; var st = new Stack<int>(); st.Push(0);
        while (st.Count > 0) { var i = st.Pop(); if (reach[i]) continue; reach[i] = true; foreach (var q in succ[i]) st.Push(q); }
        foreach (var n in ns) foreach (var l in n.Assumed) if (!reach[IxOf(l)]) succ[n.Ix].Add(IxOf(l));
    }

    // carries(H, L, P) on entry to P; naive forward fixpoint, union at joins (may-facts).
    void Carries()
    {
        In = Enumerable.Range(0, N).Select(_ => new HashSet<Fact>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = 0; i < N; i++) { var o = M == Mode.A ? TransferA(i) : Transfer(i); foreach (var q in succ[i]) foreach (var t in o) changed |= In[q].Add(t); }
            if (M == Mode.PROV) // provenance: a Loan's place is the set of targets its borrow path resolves to
                foreach (var (id, at) in issueAt) if (!issue[id].Param)
                    foreach (var t in Resolve(issue[id].Place, at).Leaves) changed |= places[id].Add(Path.Cap(t));
        }
    }

    HashSet<Fact> Transfer(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        bool KillH(string h) => n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r));                      // killed(K, P)
        // destroyed_in_place(K, P): =, replace, end of storage (Defs). S1 as written also counts exchange/swap targets.
        IEnumerable<string> dip = M == Mode.S1 || X == Abl.ExchangeKills ? n.Defs.Concat(n.Exchanges) : M == Mode.S1P ? n.Defs : [];
        Func<string, bool> Ended = l => EndedBy(l, dip);                                                     // ended(L, P)
        foreach (var t in In[i]) if (!KillH(t.H) && !Ended(t.L)) o.Add(t);                                    // keep
        if (X == Abl.KillKeepOnly) Ended = _ => false;      // only `keep` sees `ended`
        var inn = In[i].Where(t => !Ended(t.L)).ToList();   // an ended Loan propagates through no rule at P
        foreach (var (from, to) in n.Flows)                                                                  // part-preserving flow
            foreach (var s in !from.Contains('*') ? [from] : X == Abl.FlatFlow ? [Path.Before(from, "*")] : Resolve(from, i).Leaves)
                foreach (var t in inn)
                    if (Path.Within(t.H, s)) o.Add((Path.Join(to, Path.Rest(t.H, s)), t.L));
                    else if (Path.Within(s, t.H)) o.Add((to, t.L));                                          // ancestor's whole-value Loans
        foreach (var (place, src) in n.Throughs)                                                             // written_through + reaches
        {
            var reach = Resolve(place, i).Leaves.Select(p => Path.Before(p, "[]")).ToList();
            foreach (var t in inn) if (Path.Overlap(t.H, src)) foreach (var r in reach) o.Add((r, t.L));
        }
        foreach (var s in n.Issues)                                                                          // issue + child+ materialization
        {
            o.Add((s.Into, s.Id));
            if (!s.Param && !s.Phantom && X != Abl.NoMaterialize) foreach (var l in Resolve(s.Place, i).Auth) if (!Ended(l)) o.Add((s.Into, l));
            if (s.Phantom) // round 9 (G113): the receiver's value, R replaced by Φ_c
                foreach (var t in inn) if (t.L != s.Recv && Path.Overlap(t.H, issue[s.Recv].Into)) o.Add((s.Into, t.L));
        }
        return Disturbed(n, o);
    }

    // base(L, B) (plan fe1a5b34): the Place storing the reference the borrow follows last, i.e. the prefix before the
    // last Deref step (null for a plain Loan). r.* → r; rr.*.* → rr.*; cursor.*.children.[] → cursor.
    string Base(string l)
    {
        var s = Path.Segs(issue[l].Place); int j = Array.LastIndexOf(s, "*");
        return j < 0 ? null : string.Join('.', s.Take(j));
    }

    // ended(L, P) :- base(L, B), destroyed_in_place(K, P), within(B, K), on move paths only: no Deref step between B and
    // K. KillAcrossDeref restores round 2's rule (the first move path entered may be destroyed).
    bool EndedBy(string l, IEnumerable<string> dip)
    {
        // A Move Path takes no own step (SPEC 15.1.3; the owner kept SPEC's letter on case 75, 2026-10-18), so a K through an obj
        // or rc `payload` is no Move Path and ends nothing.
        if (X == Abl.PrefixKill || Pk) // review N1: direct Loans too; Defs are move paths, so K holds no Deref (round 13: the design)
        {
            var ps = Path.Segs(issue[l].Place);
            return dip.Select(Path.Segs).Any(ks => !ks.Contains("[]") && !ks.Contains("*") && !ks.Contains("payload") && ps.Length >= ks.Length && ps.Take(ks.Length).SequenceEqual(ks)
                && !(Effects && X != Abl.R18StaticEnds && Enc.Statics.ContainsKey(ks[0]))); // round 18 (G136): a static's write ends none
        }
        var b = Base(l);
        if (b == null) return false;
        // Both modes use the plan's kill (round 6 correction): B is a move path, K is B or an ancestor with no Deref
        // between, and Index steps only for identical constant indexes. KillAcrossDeref is the root-most kill with the
        // may-overlap Within, which lets a constant index kill a dynamic one (unsound, 34a/34b). FirstDerefBase takes the
        // same B with the definite matcher: K is a move path and an exact prefix of B, whose later steps may be any index
        // (destroying arr destroys arr[i], 34c).
        if (X is Abl.KillAcrossDeref or Abl.P3RootMost) { b = Path.Before(b, "*"); return dip.Any(k => Path.Within(b, k)); }
        if (X == Abl.FirstDerefBase || F)
        {
            var fs = Path.Segs(Path.Before(b, "*"));
            return dip.Select(Path.Segs).Any(ks => !ks.Contains("[]") && !ks.Contains("payload") && fs.Length >= ks.Length && fs.Take(ks.Length).SequenceEqual(ks));
        }
        if (b.Contains('*')) return false;
        var bs = Path.Segs(b);
        return dip.Select(Path.Segs).Any(ks => bs.Length >= ks.Length && bs.Take(ks.Length).SequenceEqual(ks) && !bs.Skip(ks.Length).Contains("[]"));
    }

    // Round 6 (review r2, finding 3): an observing destruction of K reaches exclusively only down to the first shared
    // Deref. The Deref below K on Loan place p is shared when every Loan its holder carries is a shared Loan.
    bool SharedCut(string k, string p, int i)
    {
        if (X == Abl.NoSharedCut || M == Mode.A && X is not (Abl.LoanPastShared or Abl.ObsSharedCut)) return false; // round 10: G110 deletes it
        var s = Path.Segs(p); int from = Path.Segs(k).Length;
        int j = from <= s.Length ? Array.IndexOf(s, "*", from) : -1;
        if (j < 0) return false;
        var prefix = string.Join('.', s.Take(j));
        var holders = M == Mode.A ? [Root(prefix)] : Resolve(prefix, i).Leaves;
        var f = In[i].Where(t => holders.Any(h => M == Mode.A ? t.H == h : Path.Overlap(t.H, h))).ToList();
        return f.Count > 0 && f.All(t => !issue[t.L].Uniq && !issue[t.L].Param);
    }

    // Provenance resolution of a Place through the references it follows, at P. Names: every name of the Place
    // (base-relative and resolved); Leaves: targets with no further Loan; Auth: Loans of the references followed.
    Res Resolve(string path, int i, int depth = 0)
    {
        var r = new Res(new() { path }, new(), new());
        var segs = Path.Segs(path); int j = Array.IndexOf(segs, "*");
        var loans = j < 0 ? new List<string>() :
            In[i].Where(t => Path.Overlap(t.H, string.Join('.', segs.Take(j)))).Select(t => t.L).Distinct().ToList();
        if (loans.Count == 0 || depth > 10) { r.Leaves.Add(path); return r; }
        var rest = string.Join('.', segs.Skip(j + 1));
        foreach (var l in loans)
        {
            r.Auth.Add(l);
            foreach (var t in places[l].ToList())
            { var s = Resolve(Path.Join(t, rest), i, depth + 1); r.Names.UnionWith(s.Names); r.Leaves.UnionWith(s.Leaves); r.Auth.UnionWith(s.Auth); }
        }
        return r;
    }

    IEnumerable<string> Names(string l, int i) => M == Mode.PROV ? places[l] : places[l].SelectMany(p => Resolve(p, i).Leaves);

    // live(H, P) on entry to P: backward from uses, closed through live references: live(T) :- live(R), carries(R, L), names(L, T).
    void Liveness()
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !n.Defs.Concat(n.Exchanges).Any(r => Path.Within(h, r))).ToHashSet();
                s.UnionWith(n.Uses);
                if (X != Abl.NoClosureFeedback) Close(s, i);
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
        if (X == Abl.NoClosureFeedback) for (int i = 0; i < N; i++) Close(liveIn[i], i); // closure per point only
    }

    void Close(HashSet<string> s, int i)
    {
        var work = new Queue<string>(s);
        while (work.Count > 0)
        {
            var r = work.Dequeue();
            foreach (var t in In[i]) if (Path.Overlap(t.H, r)) foreach (var x in Names(t.L, i)) if (s.Add(x)) work.Enqueue(x);
        }
    }

    // error(L, P) :- live_loan(L, P), access(P, place, kind), overlap, conflicts(kind, mode(L, P)).
    void Check()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom).Select(s => new Acc(s.Uniq && !s.Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace)));
            accs.AddRange(n.Activates.Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            if (M == Mode.S1P) accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null))); // exchanged/swapped: the old value moves out
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            bool LiveIn(string h) => Path.Escaped(h) || liveIn[i].Any(x => Path.Overlap(h, x));
            bool LiveAfter(string h) => Path.Escaped(h) || after.Any(x => Path.Overlap(h, x));
            bool Dest(string h) => n.Defs.Concat(n.Exchanges).Any(d => Path.Within(h, d));
            // Round 5: a value P stores (a flow into a holder live after P, or a write through a reference) is in transit
            // during P, so its source's entry facts count even if the source dies at P (24c).
            var transit = X == Abl.NoTransit ? [] : n.Flows.Where(f => LiveAfter(f.To))
                .SelectMany(f => f.From.Contains('*') ? Resolve(f.From, i).Leaves : [f.From])
                .Concat(n.Throughs.Where(t => Resolve(t.Place, i).Leaves.Any(r => LiveAfter(Path.Before(r, "[]")))).Select(t => t.Src)).ToList();
            foreach (var a in accs)
            {
                var res = X == Abl.SyntacticAccess ? new Res([a.Place], [a.Place], []) : Resolve(a.Place, i);
                if (X == Abl.NoAuthority) res.Auth.Clear();
                // base_op(P, K): a Move (S1, S1′), or an exclusive borrow other than as a `replace` target (S1′).
                // (H) plan: destroying an old value whose drop may reach the base is an exclusive use (K.Destroy, from Drop).
                bool baseOp = M != Mode.PROV && (a.Kind == K.Move ? X != Abl.NoMoveBaseOp
                    : a.Kind == K.Destroy ? M == Mode.S1P
                    : a.Kind == K.UBorrow && M == Mode.S1P && X is not (Abl.NoUniqBaseOp or Abl.S1ResolvePlaces) && (!a.InPlace || X == Abl.NoReplaceException));
                // Round 4, by holder: entry facts over holders live after P minus the destination's old value; an activation
                // over holders live into P (the call protects its arguments) minus the reservation's own holder.
                var facts = X == Abl.IdExclusion ? In[i].Where(t => LiveIn(t.H))
                    : a.Except != null ? In[i].Where(t => LiveIn(t.H) && !Path.Within(t.H, issue[a.Except].Into))
                    : In[i].Where(t => LiveAfter(t.H) && !Dest(t.H) || transit.Any(s => Path.Overlap(t.H, s)));
                foreach (var (h, l) in facts)
                {
                    if ((X == Abl.IdExclusion && l == a.Except) || res.Auth.Contains(l) || issue[l].Param || n.Excuses.Contains(l)) continue;
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    // SharedBaseOp: P3 for S1′, a shared borrow of the base or an ancestor while an exclusive child is live.
                    bool op = baseOp || ((X is Abl.SharedBaseOp or Abl.P3RootMost) && M == Mode.S1P && a.Kind == K.SBorrow && !a.InPlace && uniq);
                    // error(L, P) :- live_loan(L, P), base(L, B), base_op(P, K), runs_through(B, K): K is B or a prefix of B
                    // across Deref steps, syntactically; an Index step matches unless both are distinct constants.
                    if (op && Base(l) is { } bb && res.Names.Any(k => Path.Within(bb, k) && (X != Abl.BaseOpMovePathsOnly || !k.Contains('*'))
                            && !(a.Kind == K.Destroy && SharedCut(k, issue[l].Place, i))))
                    { Err(l, i, $"base_op {a.Kind} {a.Place}"); continue; }
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    var lp = X == Abl.S1ResolvePlaces ? places[l].SelectMany(p => Resolve(p, i).Names).ToHashSet() : places[l];
                    if (res.Names.Any(x => lp.Any(p => Path.Hits(x, p)))) Err(l, i, $"{a.Kind} {a.Place}");
                }
            }
        }
    }

    // ---- Design A (design-A.md §3, §5): root holders + E (P1), deep shared borrows (P3), syntactic access Places,
    // layer-blind following (P10), content = holders named by direct Loans (+ E), check-time liveness closure.
    static string Root(string h) => h.StartsWith('^') ? "E" : Path.Segs(h)[0];
    static int Steps(string p) => Path.Segs(p).Count(s => s == "*");
    bool Direct(string l) => !issue[l].Param && !issue[l].Marker && !issue[l].Place.Contains('*');
    static HashSet<string> Sigma(IEnumerable<Fact> s, string h) => s.Where(t => t.H == h).Select(t => t.L).ToHashSet();
    HashSet<string> Refs(HashSet<string> S)
    {
        var r = S.Where(Direct).Select(l => Root(issue[l].Place)).Where(Holds).ToHashSet(); // round 14: '#', '%' locals hold no Loan
        if (S.Any(l => issue[l].Param) || !S.Any(Direct)) r.Add("E"); // Φ_E, or no direct Loan
        return r;
    }
    static bool Holds(string h) => !h.StartsWith('#') && !h.StartsWith('%');

    // Round 15 (PLAN G148), the review's union of both designers' rules. A store row's targets W from val(a_j), each with its BFS layer
    // and its dominators (the holders every route of W from layer 1 to it passes, itself included). Layer 1: refs(val(a_j)) without raw
    // roots and non-holders (a raw root there reaches E). Past it, from each reached holder h other than E (under Guard, not a SharedOnly
    // h): E for Φ_E (an entry Loan), E for a direct Loan naming a raw root, the holder each direct Loan names (Any; else exclusive kind
    // only), non-holders skipped. Abl.None: refs(val(a_j)) as is (806110fc).
    enum Excl { None, Self, SelfRef, Dom, Target, AllRoutes, AllRoutesPast1 }
    (bool Walk, bool Any, bool Guard, Excl Ex, bool ContentOnly) Rule => X switch
    {
        Abl.R14A => (true, true, false, Excl.None, false),
        Abl.R14AX => (true, true, false, Excl.Self, false),
        Abl.R14AUniqX => (true, false, false, Excl.Self, false),
        Abl.R14ARoute => (true, true, false, Excl.AllRoutesPast1, false),
        Abl.R14B => (true, false, true, Excl.Dom, true),
        Abl.R14BLiteral => (true, false, true, Excl.None, true),
        Abl.R14BAllModes => (true, true, true, Excl.Dom, true),
        Abl.R14BNoGuard => (true, false, false, Excl.Dom, true),
        Abl.R14BTarget => (true, false, true, Excl.Target, true),
        Abl.R14BAllRoutes => (true, false, true, Excl.AllRoutes, true),
        Abl.R14BValueRows => (true, false, true, Excl.Dom, false),
        Abl.R14G => (true, true, true, Excl.None, false),
        Abl.R14GX => (true, true, true, Excl.Self, false),
        Abl.R14GXS or Abl.R14GXSTransitA => (true, true, true, Excl.SelfRef, false),
        Abl.R14GDom => (true, true, true, Excl.Dom, true),
        Abl.R16Set or Abl.R16Route or Abl.R16RouteNoGamma or Abl.R16GammaOnly or Abl.R17Hold or Abl.R17PerPart => (true, true, true, Excl.None, false), // rounds 16, 17: rule G
        _ when Effects => (true, true, true, Excl.None, false), // round 18: rule G
        _ => (false, false, false, Excl.None, false),
    };
    // Designer a's transit (a Content row's value read from the source's referents), corrected from its argument's to its source's.
    bool TransitA => X is Abl.R14A or Abl.R14AX or Abl.R14AUniqX or Abl.R14ARoute or Abl.R14GXSTransitA;
    bool RawRoot(string h) => h.StartsWith('%') || Enc.RawRoots.Contains(h);
    bool NonHolder(string h) => h.StartsWith('#') || Enc.NonHolders.Contains(h);
    Dictionary<string, (int Layer, HashSet<string> Dom)> StoreTargets(IEnumerable<Fact> s, StoreRow r, bool byArgument = true) // round 16: byArgument false steps from no ByArgument holder
    {
        var rule = Rule; var S = Sigma(s, Root(r.Arg));
        if (!rule.Walk) return Refs(S).ToDictionary(t => t, t => (1, new HashSet<string> { t }));
        var layer = new Dictionary<string, int>(); var edges = new List<(string From, string To)>(); var queue = new Queue<string>();
        foreach (var t in Refs(S)) if (!RawRoot(t) && !NonHolder(t)) { layer[t] = 1; queue.Enqueue(t); }
        var first = layer.Keys.ToHashSet();
        foreach (var l in S) if (Direct(l) && RawRoot(Root(issue[l].Place))) layer.TryAdd("E", 2);
        while (queue.Count > 0)
        {
            var h = queue.Dequeue();
            if (h == "E" || rule.Guard && Enc.SharedOnly.Contains(h) || (!byArgument || X == Abl.R17PerPart) && Enc.ByArgument.Contains(h)) continue;
            foreach (var l in Sigma(s, h))
            {
                string t = null;
                if (issue[l].Param) t = "E";
                else if (Direct(l))
                {
                    var root = Root(issue[l].Place);
                    if (RawRoot(root)) t = "E";
                    else if (!NonHolder(root) && (rule.Any || issue[l].Uniq)) t = root;
                }
                if (t == null) continue;
                edges.Add((h, t));
                if (layer.TryAdd(t, layer[h] + 1)) queue.Enqueue(t);
            }
        }
        var all = layer.Keys.ToHashSet();
        var dom = layer.Keys.ToDictionary(t => t, t => first.Contains(t) ? new HashSet<string> { t } : new HashSet<string>(all));
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var t in layer.Keys.Where(t => !first.Contains(t)).ToList())
            {
                HashSet<string> d = null;
                foreach (var (from, to) in edges.Where(e => e.To == t)) { var via = new HashSet<string>(dom[from]) { t }; if (d == null) d = via; else d.IntersectWith(via); }
                d ??= new HashSet<string> { t }; // reached without a holder edge (a raw root at layer 1)
                if (!d.SetEquals(dom[t])) { dom[t] = d; changed = true; }
            }
        }
        return layer.ToDictionary(x => x.Key, x => (x.Value, dom[x.Key]));
    }
    // ret(i, κ) of a store row (A3 5) into each target, without the direct Loans the variant's exclusion names.
    HashSet<Fact> StoreFacts(IEnumerable<Fact> s, StoreRow r)
    {
        var v = Sigma(s, Root(r.Src)); var ret = Content(s, v); if (!r.Content) ret.UnionWith(v);
        var T = StoreTargets(s, r); var rule = Rule; var o = new HashSet<Fact>();
        bool applies = !rule.ContentOnly || r.Content;
        // Round 16 (R4-M73, ruling (B)): a target reached only past a ByArgument holder receives the value with P13's marker; an empty value
        // stores nothing, so no marker (the final review's N1).
        var clean = Tainting ? StoreTargets(s, r, byArgument: false).Keys.ToHashSet() : null; // round 17: no P13 marker
        foreach (var (t, (layer, dom)) in T)
        {
            if (clean != null && !clean.Contains(t) && ret.Count > 0) o.Add((t, "τ13"));
            foreach (var l in ret)
            {
                var named = Direct(l) ? Root(issue[l].Place) : null;
                bool drop = applies && named != null && rule.Ex switch
                {
                    Excl.Self => layer > 1 && named == t,
                    Excl.SelfRef => layer > 1 && named == t && !Enc.SelfRef.Contains(t),
                    Excl.Dom => dom.Contains(named),
                    Excl.Target => named == t,
                    Excl.AllRoutes => T.ContainsKey(named),
                    Excl.AllRoutesPast1 => layer > 1 && T.ContainsKey(named),
                    _ => false,
                };
                if (!drop) o.Add((t, l));
            }
        }
        return o;
    }
    HashSet<string> Content(IEnumerable<Fact> s, HashSet<string> S) => Refs(S).SelectMany(t => Sigma(s, t)).ToHashSet();
    // Round 17: whether a store row puts a non-empty value into a target reached only past a ByArgument holder.
    bool P13Store(IEnumerable<Fact> s, StoreRow r)
    {
        var v = Sigma(s, Root(r.Src)); var ret = Content(s, v); if (!r.Content) ret.UnionWith(v);
        return ret.Count > 0 && StoreTargets(s, r).Keys.Except(StoreTargets(s, r, byArgument: false).Keys).Any();
    }

    HashSet<Fact> TransferA(int i)
    {
        var n = ns[i]; var o = new HashSet<Fact>();
        // ended (plan fe1a5b34), on every carrying rule; round 19: R-a's inspection Loans at their omitting call
        var inn = In[i].Where(t => !EndedBy(t.L, n.Defs) && !Inspected(n, t.L)).ToList();
        var whole = n.Defs.Concat(n.Exchanges).Concat(n.EndBorrows).Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
        foreach (var t in inn) if (!whole.Contains(t.H)) o.Add(t);   // P1: only a whole-root write, Move or storage end ends carrying
        HashSet<string> Val(string x) { var S = Sigma(inn, Root(x)); for (int k = Steps(x); k > 0; k--) S = Content(inn, S); return S; }
        foreach (var (from, to) in FlowsOf(n)) { foreach (var l in Val(from)) o.Add((Root(to), l)); if (gamma.Contains(Root(to))) o.Add((Root(to), "γ" + Root(to))); }
        if (Defaults) foreach (var d in n.Taints) o.Add((Root(d), "τD:" + d)); // round 16 (R4-M70): the default's value placed into D
        if (Defaults && !R19Mode) foreach (var d in n.TemplateTaints) o.Add((Root(d), "τD:" + d)); // round 19: a template call's (2e-1 deletes it)
        if (RawRowOn) foreach (var src in n.RawRows) foreach (var l in Val(src)) o.Add(("E", l)); // round 19: 2e-D's raw-pointer row
        foreach (var (place, src) in n.Throughs)                    // P10: every holder the carried direct Loans name
        {
            var S = Sigma(inn, Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(inn, S);
            var targets = Steps(place) == 0 ? [Root(place)] : Refs(S);
            foreach (var l in Sigma(inn, Root(src))) foreach (var t in targets) o.Add((t, l));
        }
        // Round 11 (E14): a raw store of a call stores its input's value into E, ret(i, Value) = val ∪ content(val) (the
        // cases' values are references; content is empty there). NoRawStore: the intrinsic stores nothing (B4).
        if (!NoRawStore)
            foreach (var (_, src) in n.RawStores) { var v = Sigma(inn, Root(src)); v.UnionWith(Content(inn, v)); foreach (var l in v) o.Add(("E", l)); }
        foreach (var r in n.Stores) o.UnionWith(StoreFacts(inn, r)); // round 15 (G148, the review's union): a Call's store rows
        foreach (var s in n.Issues)                                 // issue + parents of every followed layer
        {
            if (gamma.Contains(Root(s.Into))) o.Add((Root(s.Into), "γ" + Root(s.Into))); // finding 1: besides its Loan, if any
            // Round 10 (G110, SPEC 15.6.3 S1): a Borrow past a shared Deref forms no Loan of its own. It derives only from
            // the reference of its last shared Deref (layer SharedAt - 1) and those dereferenced after it, and carries
            // their Loans: content^j(σ(root)) for SharedAt - 1 ≤ j < k. Without a shared Deref (SharedAt = 0): unchanged.
            int sh = X == Abl.LoanPastShared || s.Param || s.Phantom ? 0 : s.SharedAt;
            if (sh > 0)
            {
                var D = Sigma(inn, Root(s.Place));
                for (int j = 0; j < Steps(s.Place); j++) { if (j >= sh - 1) foreach (var l in D) o.Add((Root(s.Into), l)); D = Content(inn, D); }
                continue;
            }
            // Fix found in round 3: a child Loan whose base this point destroys in place is ended here too (19a).
            if (X != Abl.AIssueNotEnded && EndedBy(s.Id, n.Defs)) { } else o.Add((Root(s.Into), s.Id));
            // Round 9 (G113): the result keeps the receiver's value with its own Loan R replaced by Φ_c, so Φ_c's
            // values carry its parent Loans (the receiver Ref's R2 parents), as a child's do.
            if (s.Phantom && X is not (Abl.PhantomAlone or Abl.PhantomParentsAtCall))
                foreach (var l in Sigma(inn, Root(issue[s.Recv].Into))) if (l != s.Recv || X == Abl.PhantomKeepsReceiver) o.Add((Root(s.Into), l));
            if (s.Param || (s.Phantom && X != Abl.PhantomParentsAtCall)) continue;
            var S = Sigma(inn, Root(s.Place));
            if (X != Abl.NoParentsA) for (int j = 0; j < Steps(s.Place); j++) { foreach (var l in S) o.Add((Root(s.Into), l)); S = Content(inn, S); }
        }
        return Disturbed(n, o);
    }

    void LivenessA() // backward over root holders; borrowing T uses T; no feedback from the closure
    {
        liveIn = Enumerable.Range(0, N).Select(_ => new HashSet<string>()).ToArray();
        for (bool changed = true; changed;)
        {
            changed = false;
            for (int i = N - 1; i >= 0; i--)
            {
                var n = ns[i];
                var kill = n.Defs.Concat(n.EndBorrows).Concat(n.Accs.Where(a => a.Kind == K.Move).Select(a => a.Place)).Where(h => !h.Contains('.')).ToHashSet();
                var s = succ[i].SelectMany(q => liveIn[q]).Where(h => !kill.Contains(h)).ToHashSet();
                s.UnionWith(n.Uses.Select(Root));
                s.UnionWith(n.Issues.Where(x => !x.Param).Select(x => Root(x.Place)));
                s.UnionWith(n.Accs.Where(a => a.Kind != K.Write && a.Except == null).Select(a => Root(a.Place))); // review r2: a call's destruction (DestroyAt) uses only its operand
                // Round 10 (encoding fix): the Call's operand reads (Moves) the reservation holder, as A3's use(H, ℓ)
                // says; earlier rounds left it out where a case named no Use, so a holder looked dead in its window.
                s.UnionWith(n.Activates.Select(r => Root(issue[r].Into)));
                foreach (var x in s) changed |= liveIn[i].Add(x);
            }
        }
    }

    void CheckA()
    {
        for (int i = 0; i < N; i++)
        {
            var n = ns[i]; var accs = n.Accs.ToList();
            // A2 keeps the `replace` exemption: a destroying Ref is a shallow read, and its activation is not deep.
            // Round 11: PairLive forms a PairAcquire as a live Uniq Loan (issue[].Reserve is false), never activated;
            // CalleeOff never activates a Value callee's reservation (B1).
            // Round 12: an OpAccess Borrow (a PairAcquire left at its operation) makes its access as that operation's operand.
            // Round 13: each such borrow access names its Issue (From), so E13 (1) gives it its R2 parents.
            accs.AddRange(n.Issues.Where(s => !s.Param && !s.Phantom && !s.OpAccess).Select(s => new Acc(s.InPlace ? K.Read : s.Uniq && !issue[s.Id].Reserve ? K.UBorrow : K.SBorrow, s.Place, null, s.InPlace, From: s)));
            accs.AddRange(n.Activates.Where(id => !(CalleeOff && n.Callees.Contains(id)) && !(PairLive && issue[id].Pair))
                .Select(id => new Acc(K.UBorrow, issue[id].Place, id, issue[id].InPlace)));
            accs.AddRange(n.Exchanges.Select(h => new Acc(K.Move, h, null)));
            var after = succ[i].SelectMany(q => liveIn[q]).ToHashSet();
            var dest = n.Defs.Concat(n.Exchanges).Where(h => !h.Contains('.')).ToHashSet();
            var outSet = X == Abl.AText ? TransferA(i) : null;
            static bool Esc(string h) => h is "E" or "$ret";
            HashSet<string> SrcHolders(string x) // the holders a flow's value is read from (k reference layers)
            {
                if (Steps(x) == 0) return [Root(x)];
                var S = Sigma(In[i], Root(x)); for (int k = Steps(x); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            HashSet<string> Targets(string place) // the holders a write through `place` reaches (P10)
            {
                if (Steps(place) == 0) return [Root(place)];
                var S = Sigma(In[i], Root(place)); for (int k = Steps(place); k > 1; k--) S = Content(In[i], S);
                return Refs(S);
            }
            // Transit clause (owner decision): σ_in of every value ℓ stores into a holder live after ℓ, into E, or through
            // a reference with a live destination.
            var transit = X == Abl.NoTransit ? [] : FlowsOf(n).Where(f => after.Contains(Root(f.To)) || Esc(Root(f.To)))
                .SelectMany(f => SrcHolders(f.From))
                .Concat(n.Throughs.Where(t => Targets(t.Place).Any(h => after.Contains(h) || Esc(h))).Select(t => Root(t.Src)))
                .Concat(NoRawStore ? [] : n.RawStores.Select(t => Root(t.Src))) // round 11 (E14): a store into E
                .Concat(RawRowOn ? n.RawRows.Select(Root) : []) // round 19: 2e-D's raw-pointer row, a store into E
                // Round 15 (G148): a store row whose output reaches a holder live after ℓ or E: its value's holders (Content: the
                // holders the argument's referent content is read from).
                .Concat(n.Stores.Where(r => StoreTargets(In[i], r).Keys.Any(h => after.Contains(h) || Esc(h)))
                    .SelectMany(r => TransitA && r.Content ? Refs(Sigma(In[i], Root(r.Src))) : new HashSet<string> { Root(r.Src) })).ToHashSet();
            // Round 13: ParentsAsTransit, the review's other fix: the stored value seeds whatever its destination's liveness (ROS),
            // and the Ref's parent holders are in transit (RefParentsInTransit, RPT).
            var stored = n.Issues.Where(s => !s.Param && !s.Phantom && (after.Contains(Root(s.Into)) || Esc(Root(s.Into)) || X == Abl.ParentsAsTransit)).ToList();
            if (X is Abl.RefParentsInTransit or Abl.ParentsAsTransit) // the holders each Ref's carried parents are read from (layers from the last shared on)
                foreach (var s in stored)
                {
                    var S = Sigma(In[i], Root(s.Place)); var hs = new HashSet<string> { Root(s.Place) };
                    for (int j = 0; j < Steps(s.Place); j++) { if (j >= Math.Max(s.SharedAt - 1, 0)) transit.UnionWith(hs); hs = Refs(S); S = Content(In[i], S); }
                }
            // Round 10 final (B1, the design review's rule replacing 39o's row; SPEC 15.6: "a value is also used wherever a
            // value that carries a Loan on a Place overlapping the first value's holder is used"): the closure of CS(ℓ) is
            // seeded with σ_in(T) for each T named by a direct Loan (E for Φ_E) of the value ℓ stores into a holder live after
            // ℓ or E: the Ref's own Loan and its R2 parents from the layers it carries (G110's start layer); the closure of
            // CS_call(ℓ, t) with those of σ_in(t). Conflict candidates are unchanged (the seeds only feed the closure).
            void Name(HashSet<string> into, string l) { if (issue[l].Param) into.Add("E"); else if (Direct(l)) into.Add(Root(issue[l].Place)); }
            HashSet<string> SeedOf(List<Issue> st)
            {
                var seed = new HashSet<string>();
                if (X != Abl.NoFreshClosure)
                    foreach (var s in st)
                    {
                        if (Direct(s.Id)) seed.Add(Root(s.Place)); // the Ref's own Loan (none past a shared Deref)
                        if (X == Abl.FreshOnly) continue;
                        var S = Sigma(In[i], Root(s.Place));
                        for (int j = 0; j < Steps(s.Place); j++) { if (j >= Math.Max(s.SharedAt - 1, 0)) foreach (var l in S) Name(seed, l); S = Content(In[i], S); }
                    }
                return seed;
            }
            var seedStore = SeedOf(stored);
            // A3 E5: "a holder live after ℓ" as transit reads it: in live_out(ℓ) or made live by the closure, so a holder named
            // by a direct Loan of a value live after ℓ (seeds included) also receives a stored value; to a fixpoint (120b).
            // SeedLiveOutOnly: live_out(ℓ) and E only.
            if (X is not (Abl.SeedLiveOutOnly or Abl.NoFreshClosure))
            {
                var live = new HashSet<string>();
                for (bool grew = true; grew;)
                {
                    grew = false;
                    var Fc = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H) || transit.Contains(t.H) || seedStore.Contains(t.H)).ToHashSet();
                    for (var work = new Queue<Fact>(Fc); work.Count > 0;)
                    {
                        var l0 = work.Dequeue().L;
                        if (!Direct(l0)) continue;
                        var T = Root(issue[l0].Place);
                        if (!after.Contains(T) && live.Add(T)) grew = true;
                        foreach (var t in In[i].Where(t => t.H == T)) if (Fc.Add(t)) work.Enqueue(t);
                    }
                    if (grew)
                    {
                        stored = n.Issues.Where(s => !s.Param && !s.Phantom && (after.Contains(Root(s.Into)) || Esc(Root(s.Into)) || live.Contains(Root(s.Into)))).ToList();
                        seedStore = SeedOf(stored);
                    }
                }
            }
            HashSet<string> Seed(Acc a) // CS_call(ℓ, t): the direct Loans of σ_in(t); FreshOnly, FreshParents: the store seed everywhere
            {
                if (a.Input && !InputsOnCS && !(R11Rule && n.NotCall)) return []; // round 11 (E13): CS_call(ℓ, −) excludes no holder, so it needs no seed; round 12: CS_i states none
                // Round 13 (E13 (1)): E5's seed belongs to the destination write, the stores and a borrow (BorrowWithoutSeed: not the
                // borrow); an observing Drop takes only val(X) (KeepCSTerms: CS(ℓ)'s seed too).
                if (OwnTerm(a) && (a.From == null || X == Abl.BorrowWithoutSeed)) return []; // a borrow keeps E5's seed (41o)
                if (a.Except == null || X is Abl.NoFreshClosure or Abl.FreshOnly or Abl.FreshParents) return seedStore;
                var t = new HashSet<string>(); foreach (var l in Sigma(In[i], issue[a.Except].Into)) Name(t, l); return t;
            }
            // Round 12: val_in(o): the value operand o reads at ℓ, from σ_in: a Copy or Move of a Place with k borrow Derefs
            // reads content^k(σ_in(root)); a Borrow left at ℓ (AcquireAtOperation) carries its R2 parents, its own Loan being
            // formed at ℓ and in no entry set.
            HashSet<string> OpVal(Acc o)
            {
                if (o.Kind is K.UBorrow or K.SBorrow) return Parents(o.Place);
                var S = Sigma(In[i], Root(o.Place));
                for (int k = Steps(o.Place); k > 0; k--) S = Content(In[i], S);
                return S;
            }
            // Round 13 (E13 (1)): a borrow's R2 parents over its k borrow Derefs, as E3 bounds them: ⋃_{m≤j<k} content^j(σ_in(root(X))),
            // m the index of the last shared Deref (SharedAt - 1), else 0. ParentsEveryLayer: m = 0, every layer.
            HashSet<string> Parents(string x, int sharedAt = 0)
            {
                int m = sharedAt > 0 && X != Abl.ParentsEveryLayer ? sharedAt - 1 : 0;
                var S = Sigma(In[i], Root(x)); var P = new HashSet<string>();
                for (int j = 0; j < Steps(x); j++) { if (j >= m) P.UnionWith(S); S = Content(In[i], S); }
                return P;
            }
            var baseSet = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H)).ToHashSet(); // live_out(ℓ) not overwritten, E
            bool marked = n.Accs.Any(o => o.Input); // the node marks its operands (rounds 11+)
            HashSet<Fact> OperandSet(Acc a)
            {
                var ops = n.Accs.Where(o => o.Input).ToList(); int at = ops.FindIndex(o => ReferenceEquals(o, a));
                var cs = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H)).ToHashSet();
                for (int j = 0; j < ops.Count; j++)
                {
                    var h = Root(ops[j].Place);
                    if (j < at && X == Abl.EarlierRoots || j > at && X != Abl.NoLaterRoots) cs.UnionWith(In[i].Where(t => t.H == h));
                    else if (j < at && X != Abl.NoEarlierValues || j == at && OwnValue) foreach (var l in OpVal(ops[j])) cs.Add((h, l));
                }
                return cs;
            }
            foreach (var h0 in n.EndBorrows)
            {
                // Round 10 (finding 1, GuardOrigin): err(ended) :- EndBorrow H at ℓ, H a guard candidate, γ_H ∈ CS(ℓ), CS over the
                // values live after ℓ other than H, transit included. Any other EndBorrow (a comparison operand, an abandoned
                // reservation) has no ended check. GammaGuardsOnly keeps the Loan row there; EndedByLoan uses it everywhere.
                bool byMarker = gamma.Contains(h0);
                if (!byMarker && X is not (Abl.GammaGuardsOnly or Abl.EndedByLoan)) continue;
                var F0 = In[i].Where(t => t.H != h0 && (Esc(t.H) || after.Contains(t.H) || byMarker && transit.Contains(t.H))).ToHashSet();
                var entries0 = F0.Select(t => t.H).ToHashSet(); // round 16: the check set's roots, before its closure
                for (var work = new Queue<Fact>(F0); work.Count > 0;)
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null) foreach (var t in In[i].Where(t => t.H == T)) if (F0.Add(t)) work.Enqueue(t);
                }
                if (!byMarker) { foreach (var (_, l) in F0) if (!issue[l].Param && !issue[l].Marker && Root(issue[l].Into) == h0) Err(l, i, "ended " + h0); }
                else if (F0.Any(t => t.L == "γ" + h0))
                {
                    // Round 16 (the owner's route taint): γ only tainted sets hold is the located Unsupported_Kd, as a Loan's conflict is.
                    var (cleanH, taintOf) = GammaTaint ? Taints(F0, entries0, X == Abl.R16Route) : (null, null);
                    var holding = F0.Where(t => t.L == "γ" + h0).Select(t => t.H).ToList();
                    // Round 20: err(ended)'s participating scopes are the EndBorrow's and the guard local's.
                    if (R20Mode && Boundary(ScopeOf(i), GuardScope(h0), -1, ["γ" + h0]) is var gb && gb > 0) Omitted.Add(("γ" + h0, i, "ended " + h0, gb));
                    else if (cleanH == null || holding.Any(cleanH.Contains)) { if (held) Unsupp("γ" + h0, i, "ended " + h0, i); else Err("γ" + h0, i, "ended " + h0); }
                    else Unsupp("γ" + h0, Located(holding, taintOf, i), "ended " + h0, i);
                }
            }
            foreach (var a in accs)
            {
                // LS facts (see Check). IdExclusion: the old by-id rule. AText: design A's letter (σ after ℓ over live_out,
                // "L not issued at ℓ", activation L ≠ R), both by Loan id.
                HashSet<Fact> F;
                if (X == Abl.IdExclusion) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H)).ToHashSet();
                // Round 13 (E13 (1), the review's AV): an activation of a reservation in t, and a destroying call's destroy, take the
                // values of the operation's operands other than t, ⋃_{o_j≠t} val(o_j), on live_out(ℓ) without t, seeded by σ_in(t)
                // (Seed, E5); this replaces CS_call(ℓ, t)'s live_in(ℓ). A node whose operands rounds 1-10 do not mark keeps live_in(ℓ),
                // whose closure bounds the operands' values from above; ActivationLiveIn keeps it everywhere.
                else if (a.Except != null && ActivationOnValues && marked)
                {
                    F = baseSet.Where(t => t.H != issue[a.Except].Into).ToHashSet();
                    foreach (var o in n.Accs.Where(o => o.Input && Root(o.Place) != issue[a.Except].Into)) foreach (var l in OpVal(o)) F.Add((Root(o.Place), l));
                }
                else if (a.Except != null) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H) && (X == Abl.AText || t.H != issue[a.Except].Into)).ToHashSet();
                // Round 11 (E13 (1)): an access a Call's input makes at the Call happens before the call, while the holders of
                // the Call's other inputs are live: CS_call(ℓ, −), every holder live into ℓ (no exclusion; a reservation in its
                // own holder stays `ref` through `Reserved`), and E. InputsOnCS: CS(ℓ), the reviewed branch (B2).
                // Round 12: round 11's rule (R11Rule) checks only a Call's inputs so; another operation's operands keep CS(ℓ).
                else if (a.Input && !InputsOnCS && R11Rule && !n.NotCall) F = In[i].Where(t => Esc(t.H) || liveIn[i].Contains(t.H)).ToHashSet();
                // Round 12 (E13 (1), the owner's option R1; SPEC 12.2): operand i is evaluated before the operation and after
                // operands 1..i-1, so it is checked on CS_i(ℓ): the holders live after ℓ that ℓ does not overwrite, E, the values
                // of the earlier operands (attributed to their roots' holders, so a reservation in its own holder stays `ref`)
                // and the holders of the later ones, the operand's own value included (j ≤ i; NoOwnValue: j < i). No transit
                // and no seed.
                else if (a.Input && !InputsOnCS && !(R11Rule && n.NotCall)) F = OperandSet(a);
                else if (X == Abl.AText) F = outSet.Where(t => Esc(t.H) || after.Contains(t.H)).ToHashSet();
                // Round 13 (E13 (1)): a borrow (From) and an observing Drop take their own term, added below, and no transit (MIR
                // never destroys and stores at one location). KeepCSTerms: CS(ℓ) as before, the own term added (the review's run).
                else if (OwnTerm(a)) F = baseSet;
                else F = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H) || transit.Contains(t.H)).ToHashSet();
                // Round 12 (planning session on finding 2): a single-operand rvalue (a Use of a Copy or Move, a Cast, a UnaryOp;
                // not a Ref, whose access is a Borrow) uses its operand's value at ℓ too, so its operand's access is also checked
                // against val_in(o) on CS(ℓ). A round 1-11 node marks no operand; the prototype takes a node's Read or Move whose
                // Place is the source of one of its flows (the value ℓ stores). OwnValueEverywhere adds the term to every node Read
                // or Move; the term only adds Loans, so it bounds every finer reading from above.
                if (OwnValue && !a.Input && a.Except == null && a.Kind is K.Read or K.Move && n.Accs.Any(o => ReferenceEquals(o, a))
                    && (X == Abl.OwnValueEverywhere || FlowsOf(n).Any(f => f.From == a.Place)))
                    foreach (var l in OpVal(a)) F.Add((Root(a.Place), l));
                // Round 13 (E13 (1), the review's DOV): an observing destruction of X, a Drop or a destroying call's destroy, takes
                // val(X) (SPEC 15.6: a Loan is live including an observation by its destruction; 15.6.6).
                if (DestroyValue && a.Kind == K.Destroy) foreach (var l in OpVal(a)) F.Add((Root(a.Place), l));
                // Round 13 (E13 (1), the review's ROP): a Ref's or Reborrowing PairAcquire's borrow of X takes its R2 parents,
                // whatever its destination's liveness (supersedes R4-M57's "a borrow's stored value only seeds": BorrowSeedOnly).
                if (BorrowParents && a.From != null) foreach (var l in Parents(a.Place, a.From.SharedAt)) F.Add((Root(a.Place), l));
                IEnumerable<Fact> basis = X == Abl.AText && a.Except == null ? outSet : In[i];
                var seed = Seed(a); F.UnionWith(basis.Where(t => seed.Contains(t.H)));
                var entries = F.Select(t => t.H).ToHashSet(); // round 16: the check set's roots, before its closure
                // Round 19: an activation's (and a destroying call's destroy's) closure leaves its reservation holder t out (E13 (1)(b), as
                // MirBorrows.Candidates' Takes does): a Loan naming t, as a default's inspection borrow of a slot, re-enters no σ(t). No earlier
                // output changes (checked).
                var own = a.Except != null ? issue[a.Except].Into : null;
                for (var work = new Queue<Fact>(F); work.Count > 0;) // a live direct Loan naming T makes σ(T) live
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null && T != own) foreach (var t in basis.Where(t => t.H == T)) if (F.Add(t)) work.Enqueue(t);
                }
                // S1′ + P3; an observing Drop is deep; not a destroying Ref. NoP3A (review mutation): Moves and Borrows shallow.
                bool deep = (a.Kind == K.Destroy || a.Kind is K.Move or K.SBorrow or K.UBorrow && X != Abl.NoP3A) && !a.InPlace;
                // G119's write or destruction: a write, a destruction, or a destroying activation (A3: "a destroying R hits as
                // write"; `replace(cur@uniq, …)` destroys cur's old value in place). RouteWriteOnly drops the last (B's letter).
                bool shallowExclusive = a.Kind is K.Write or K.Destroy || a.Except != null && a.InPlace && X != Abl.RouteWriteOnly;
                bool byId = X is Abl.IdExclusion or Abl.AText;
                var hits = new List<(string H, string L)>(); // round 16: the (holder, Loan) pairs that conflict, decided below
                foreach (var (h, l) in F)
                {
                    if ((byId && l == a.Except) || issue[l].Param || issue[l].Marker || excusedAt[i].Contains(l)) continue;
                    if (X == Abl.AText && a.Except == null && n.Issues.Any(s => s.Id == l)) continue; // "L not issued at ℓ"
                    bool uniq = issue[l].Uniq && !Reserved(h, l, i);
                    if (!uniq && a.Kind is K.Read or K.SBorrow) continue;
                    // Round 14 (R4-M68, SPEC 15.6.7): an exclusive Loan that hits a reservation was met where the later Borrow was
                    // formed or activated, so the activation meets only `ref` ones; NoFormationReport reports at the activation only.
                    if (ActivationRefOnly && a.Except != null && a.Kind == K.UBorrow && uniq) continue;
                    if (X == Abl.NoFormationReport && a.From != null && issue[a.From.Id].Reserve) continue;
                    var p = issue[l].Place; // syntactic: no resolution, no authority
                    // Review r2 F1 (A3 §5 err(destroy), corrected from ¬hits(…, uborrow), which never holds): a destroying call's
                    // destruction reports only the obs part beyond its activation, so skip a Loan the activation's own kind hits
                    // (write for an InPlace target such as `replace`, uborrow otherwise, as for `clear`).
                    if (a.Kind == K.Destroy && a.Except != null && (Path.Within(a.Place, p) || Path.Within(p, a.Place) && (!issue[a.Except].InPlace || !Path.DerefBelow(p, a.Place)))) continue;
                    // Round 10 (G110): no Loan lies past a shared Deref, so the obs cut is gone (SharedCut is false unless
                    // LoanPastShared or ObsSharedCut restores it).
                    bool reach = deep && !(a.Kind == K.Destroy && SharedCut(a.Place, p, i));
                    // Round 10 (G119, SPEC 15.6.7; B's rule): inside a reservation's window (formation to activation), a write
                    // or destruction of a prefix of the reserved Loan's Place, across borrow Derefs, reaches that Loan in its
                    // reservation holder. Checked here on σ_in, so before any `ended` the access causes.
                    if (X != Abl.NoRouteReach && shallowExclusive && (InWindow(h, l, i) || X == Abl.RouteAllParents && PendingHolder(h, i))) reach = true;
                    if (Path.Within(a.Place, p) || (Path.Within(p, a.Place) && (reach || !Path.DerefBelow(p, a.Place)))) hits.Add((h, l));
                }
                Report(i, F, entries, hits, $"{a.Kind} {a.Place}", a.Except); // rounds 16-17: the taint and the hold (Report); round 20: the reservation
            }
            // Round 18 (ER-effects, A5-F5): err(effect) at a Call, a Drop or a staticEnsure with A4's keys and classes, and a destruction's
            // own exclusive Loan (R3). No earlier case carries an effect fact.
            if (Effects && n.HasEffects)
            {
                // R2 (E13 (1)(b), (d)): closure(σ_in of live_out(ℓ) not overwritten ∪ σ_in(E) ∪ every operand's value, none left out); a
                // Drop's destroyed Place adds val(X). R18LiveOnly: live_out(ℓ) and E only. (baseSet itself may hold an access's own terms by now.)
                var F = In[i].Where(t => Esc(t.H) || after.Contains(t.H) && !dest.Contains(t.H)).ToHashSet();
                if (X != Abl.R18LiveOnly) foreach (var o in n.Accs.Where(o => o.Input || o.Kind == K.Destroy)) foreach (var l in OpVal(o)) F.Add((Root(o.Place), l));
                var entries = F.Select(t => t.H).ToHashSet();
                for (var work = new Queue<Fact>(F); work.Count > 0;)
                {
                    var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                    if (T != null) foreach (var t in In[i].Where(t => t.H == T)) if (F.Add(t)) work.Enqueue(t);
                }
                // R1 (G93): the classes apart; R18Merged is A4's `bool` today (Unknown | Limit | Derived as Unknown).
                bool merged = X == Abl.R18Merged, unknown = n.Unknown || merged && (n.Limit || n.Derived), limit = n.Limit && !merged;
                string Static(string l) => !issue[l].Marker && !issue[l].Param && Enc.Statics.ContainsKey(Root(issue[l].Place)) ? Root(issue[l].Place) : null;
                // R4 (SPEC 15.6.4, PD12 (ii)): a live Loan on the ensured Field proves it initialized, so its initializer does not run; under P5
                // the Loan's key is not known to be the ensured one, so a conflict that proof would decide is the located Unsupported_Kd.
                bool proof = n.Ensure != null && X != Abl.R18EnsureIgnored
                    && F.Any(t => Static(t.L) == n.Ensure && (X != Abl.R18EnsureExact || Enc.StaticKey.GetValueOrDefault(t.L, "") == n.EnsureKey));
                if (!(proof && (X is Abl.R18EnsureExact or Abl.R18EnsureByField || X == Abl.R18EnsureClosed && string.IsNullOrEmpty(n.EnsureKey))))
                {
                    var hits = new List<(string H, string L)>();
                    foreach (var (h, l) in F)
                    {
                        if (Static(l) is not { } f) continue;
                        // W6 (E13 (1), SPEC 15.6.7): a reservation this Call activates counts as uniq at its effects and cleanup.
                        bool uniq = issue[l].Uniq && (!Reserved(h, l, i) || X != Abl.R18NoW6 && activeAt[l] == i);
                        if (n.Keys.Any(k => k.F == f && (k.Write || uniq)) || unknown && Enc.Statics[f]) hits.Add((h, l));
                        else if (limit && Enc.Statics[f]) Unsupp(l, i, "effect (Limit)", i);
                    }
                    if (proof) foreach (var l in hits.Select(x => x.L).Distinct()) Unsupp(l, i, "effect (ensure)", i);
                    else Report(i, F, entries, hits, "effect");
                }
                // R3 (SPEC 16.4 with 16.3.1 and 16.3.3, PD12 (iii)): destroying a static's old value holds an exclusive Loan on it, which the
                // destruction's own key on that Field meets, and an Unknown effect where the Field is mutable.
                if (X != Abl.R18NoR3)
                    foreach (var f in n.Accs.Where(a => a.Kind == K.Destroy).Select(a => Root(a.Place)).Where(Enc.Statics.ContainsKey).Distinct())
                        if (n.Keys.Any(k => k.F == f) || unknown && Enc.Statics[f])
                        {
                            // Round 20: R3's Loan, if 2b-D issues it at the Drop, has the Drop's scope (the M69 plan §4's open row).
                            if (R20Mode && Boundary(ScopeOf(i), ScopeOf(i), -1, []) is var rb && rb > 0) Omitted.Add(("Δ" + f, i, "destruction", rb));
                            else if (held) Unsupp("Δ" + f, i, "destruction", i); else Err("Δ" + f, i, "destruction");
                        }
                        else if (limit && Enc.Statics[f]) Unsupp("Δ" + f, i, "destruction (Limit)", i);
            }
        }
    }

    // Round 16 (R4-M70, R4-M73; the owner's route taint): a Loan some clean holder of the check set holds is the Language problem; one only
    // tainted holders hold is the located Unsupported_Kd. Without a taint mode every holder is clean. Round 17: in a held body every Loan
    // problem is the located Unsupported_Kd at its access; one the default taint already makes Unsupported_Kd keeps the omitting call (one
    // record per problem). Round 18: err(effect) reports here too (R4-M73).
    void Report(int i, HashSet<Fact> F, HashSet<string> entries, List<(string H, string L)> hits, string what, string except = null)
    {
        var (clean, taint) = Defaults ? Taints(F, entries, RouteTaint) : (null, null);
        // Round 20 (R20AfterSelect, the recorder exception): the access's first candidate is chosen first, and a covered one hides its record.
        bool hidden = X == Abl.R20AfterSelect && hits.Count > 0 && Boundary(i, hits.Select(x => x.L).MinBy(Rank), except) > 0;
        foreach (var g in hits.GroupBy(x => x.L))
            if (R20Mode && (Boundary(i, g.Key, except) > 0 || hidden)) Omitted.Add((g.Key, i, what, Boundary(i, g.Key, except))); // round 20: its template's
            else if (!R20Mode && ns[i].InScope) Dropped.Add((g.Key, i, what)); // round 19 (R4-M69): a Loan problem inside a copy's Inlined scope is its template's
            else if (clean == null || g.Any(x => clean.Contains(x.H))) { if (held) Unsupp(g.Key, i, what, i); else Err(g.Key, i, what); }
            else if (X == Abl.R19InlinedDrop) Dropped.Add((g.Key, i, what)); // round 19: the audit's option (a), the marker as a drop
            else Unsupp(g.Key, Located(g.Select(x => x.H), taint, i), what, i);
    }

    // Round 20 (PLAN G171; the M69 plan §4-§5): the scope tree as MirBody has it (MirValidator: Outer < index, so Common is a linear walk). A round-19
    // node marked Inlined() lies in one implicit copy scope 1; every other scope but the root is a copy's Inlined scope, so Inlining(s) is s itself.
    int ScopeOf(int i) => ns[i].Scope > 0 ? ns[i].Scope : ns[i].InScope ? 1 : 0;
    int Outer(int s) => s < Enc.Scopes.Count ? Enc.Scopes[s].Outer : 0;
    // R20SameSpan: the first copy of the same template at the same nesting stands for every such copy.
    int Canon(int s) => s == 0 || s >= Enc.Scopes.Count ? s
        : Enumerable.Range(1, s).First(t => Enc.Scopes[t].Template == Enc.Scopes[s].Template && Canon(Outer(t)) == Canon(Outer(s)));
    int Up(int s) => X == Abl.R20SameSpan ? Canon(Outer(s)) : Outer(s);
    int Common(int a, int b) { if (X == Abl.R20SameSpan) { a = Canon(a); b = Canon(b); } while (a != b) { if (a > b) a = Up(a); else b = Up(b); } return a; }
    bool Within(int s, int b) { while (s > b) s = Outer(s); return s == b; }
    int IssueScope(string l) => issueAt.TryGetValue(l, out var at) ? ScopeOf(at) : 0; // an entry Loan: the root
    int GuardScope(string h) => ScopeOf(ns.First(n => n.Defs.Contains(h) || n.Issues.Any(s => s.Into == h)).Ix); // the guard local's declaration
    // The candidate order (MirBorrows.Conflict scans its candidates in order and returns the first): the case's, else the issue order.
    int Rank(string l) { int k = Enc.LoanOrder == null ? -1 : Array.IndexOf(Enc.LoanOrder, l); return k >= 0 ? k : 1000 + issueSeq.GetValueOrDefault(l); }
    int Boundary(int i, string l, string except) => except == null ? Boundary(ScopeOf(i), IssueScope(l), -1, [l]) : Boundary(ScopeOf(i), IssueScope(l), IssueScope(except), [l, except]);
    // The template boundary of a failure: the deepest Inlined scope containing the participating scopes the mode reads; 0: none (the current body's).
    // R20Entry moves it out while a participating Loan reaches its entry from outside it (an earlier evaluation's).
    int Boundary(int access, int issued, int reservation, string[] loans)
    {
        int b = X switch
        {
            Abl.R20AccessOnly => access,
            Abl.R20IssueOnly => issued,
            Abl.R20NoReservation => Common(access, issued),
            _ => reservation < 0 ? Common(access, issued) : Common(Common(access, issued), reservation),
        };
        if (X == Abl.R20Entry) while (b > 0 && EntryLoans(b) is var e && loans.Any(e.Contains)) b = Outer(b);
        return b;
    }
    // The Loans that reach the first node of scope b's subtree from a node outside it in a value live there: a holder live into it or E, closed as the
    // check sets close (a direct Loan naming T makes σ(T) live). A dead holder's set carried around a loop is no earlier evaluation's value.
    HashSet<string> EntryLoans(int b)
    {
        int entry = Enumerable.Range(0, N).First(i => Within(ScopeOf(i), b));
        var o = Enumerable.Range(0, N).Where(p => succ[p].Contains(entry) && !Within(ScopeOf(p), b)).SelectMany(p => TransferA(p)).ToHashSet();
        var live = o.Where(t => t.H is "E" or "$ret" || liveIn[entry].Contains(t.H)).ToHashSet();
        for (var work = new Queue<Fact>(live); work.Count > 0;)
        {
            var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
            if (T != null) foreach (var t in o.Where(t => t.H == T)) if (live.Add(t)) work.Enqueue(t);
        }
        return live.Select(t => t.L).ToHashSet();
    }
    // Every omission with its boundary scope, whose template the simulation check runs (R4-M69's drop names the access's own copy).
    public IEnumerable<(string L, int P, string What, int B)> Omissions() => R20Mode ? Omitted : Dropped.Select(d => (d.L, d.P, d.What, ScopeOf(d.P)));
    public IEnumerable<string> OmittedNodes() => Omissions().Select(e => ns[e.P].Label).Distinct();

    // Round 19 (ER-defaults, A5-F9; 2e-2): a default template's own rules at its prepared slots (Case.Prepared). R26-borrow: at each Return, a Loan
    // rooted at a PreparedInput in closure(σ_in(_0) ∪ σ_in(E)) is DefaultArgumentBorrow_Kd at its site (R19ReturnOnly: seeded by σ_in(_0) alone).
    // R26-access: a write rooted at a PreparedInput past its slot, through no raw pointer (Case.RawAt), is DefaultArgumentAccess_Kd at the write, and
    // so is an exclusive Loan formed on one that R26-borrow did not report, one code per failure (R19AccessAll: every one; R19RawAccess: raw writes
    // too). Moves are A1's (R4-M69). Each reports through the hold (R4-M73); the related parameter is the root (PD15 (v)'s Anchor fact).
    void PreparedRules()
    {
        bool Rooted(string p) => Enc.Prepared.Contains(Root(p)) && p.Contains('.') && (X == Abl.R19RawAccess || !Enc.RawAt.Any(r => Path.Within(p, r + ".*")));
        void Default(string code, string l, int at, string param)
        {
            if (held) Unsupp(l, at, "default " + code, at);
            else if (!DefaultProblems.Contains((code, l, at, param))) DefaultProblems.Add((code, l, at, param));
        }
        var borrowed = new HashSet<string>();
        for (int i = 0; i < N; i++)
        {
            if (!ns[i].Returns) continue;
            var F = In[i].Where(t => t.H == "_0" || t.H == "E" && X != Abl.R19ReturnOnly).ToHashSet();
            for (var work = new Queue<Fact>(F); work.Count > 0;)
            {
                var l0 = work.Dequeue().L; var T = issue[l0].Param ? "E" : Direct(l0) ? Root(issue[l0].Place) : null;
                if (T != null) foreach (var t in In[i].Where(t => t.H == T)) if (F.Add(t)) work.Enqueue(t);
            }
            foreach (var l in F.Select(t => t.L).Distinct().Order(StringComparer.Ordinal))
                if (!issue[l].Param && !issue[l].Marker && Rooted(issue[l].Place) && borrowed.Add(l)) Default("Borrow", l, issueAt[l], Root(issue[l].Place));
        }
        for (int i = 0; i < N; i++)
            foreach (var a in ns[i].Accs) if (a.Kind == K.Write && Rooted(a.Place)) Default("Access", "write " + a.Place, i, Root(a.Place));
        foreach (var s in ns.SelectMany(n => n.Issues))
            if (s.Uniq && !s.Param && !s.Phantom && !s.Marker && s.SharedAt == 0 && Rooted(s.Place) && (X == Abl.R19AccessAll || !borrowed.Contains(s.Id)))
                Default("Access", s.Id, issueAt[s.Id], Root(s.Place));
    }
    public IEnumerable<string> DefaultNodes(string code) => DefaultProblems.Where(e => e.Code == code).Select(e => ns[e.P].Label).Distinct();

    // Round 10 (G119): strictly between the reservation's formation and its activation; the Call's own effects (its
    // destination write) come after the activation (19a, 38f, 40f).
    bool InWindow(string h, string l, int i) => activeAt.TryGetValue(l, out var at) && h == issue[l].Into && issueAt[l] < i && i < at;

    // RouteAllParents (designer A's option 2, not chosen): h holds a reservation whose window contains i.
    bool PendingHolder(string h, int i) => issue.Values.Any(r => r.Reserve && r.Into == h && InWindow(h, r.Id, i));

    void Err(string l, int i, string what)
    {
        if (!R20Mode && ns[i].InScope) { if (!Dropped.Contains((l, i, what))) Dropped.Add((l, i, what)); return; } // round 19 (R4-M69): a copy's problem is its template's
        if (!Errors.Contains((l, i, what))) Errors.Add((l, i, what));
    }
    void Unsupp(string l, int i, string what, int access) { if (!Unsupported.Contains((l, i, what))) Unsupported.Add((l, i, what)); if (!Reclassified.Contains((l, access, what))) Reclassified.Add((l, access, what)); }

    // Round 16: the holders of a check set the check reads clean, and each tainted one's taint (R4-M70, R4-M73). The set rule (the unit
    // as reviewed): a holder whose own set carries no taint marker. The route rule (the owner's): a root of the check set without one, or a
    // holder a clean holder's direct Loan (or Φ_E) names and whose own set carries none; a holder reached only through tainted sets is
    // tainted with the first taint on its route. A default's taint sorts before P13's, as the per-local markers precede P13's element.
    (HashSet<string> Clean, Dictionary<string, string> Taint) Taints(HashSet<Fact> F, HashSet<string> roots, bool route)
    {
        var taint = F.Where(t => IsTaint(t.L)).GroupBy(t => t.H)
            .ToDictionary(g => g.Key, g => g.Select(t => t.L).OrderBy(x => x == "τ13" ? 1 : 0).ThenBy(x => x, StringComparer.Ordinal).First());
        var holders = F.Select(t => t.H).ToHashSet();
        if (!route) return (holders.Where(h => !taint.ContainsKey(h)).ToHashSet(), taint);
        IEnumerable<string> Named(string h) => F.Where(t => t.H == h).Select(t => issue[t.L].Param ? "E" : Direct(t.L) ? Root(issue[t.L].Place) : null).Where(x => x != null);
        var clean = new HashSet<string>(); var queue = new Queue<string>();
        foreach (var h in roots) if (!taint.ContainsKey(h) && clean.Add(h)) queue.Enqueue(h);
        while (queue.Count > 0) foreach (var t in Named(queue.Dequeue())) if (!taint.ContainsKey(t) && clean.Add(t)) queue.Enqueue(t);
        var tainted = new Queue<string>(taint.Keys);
        while (tainted.Count > 0) { var h = tainted.Dequeue(); foreach (var t in Named(h)) if (!clean.Contains(t) && taint.TryAdd(t, taint[h])) tainted.Enqueue(t); }
        return (clean, taint);
    }

    // Round 16: an Unsupported_Kd's location by its first taint's kind (Reports.cs): a default's at the node that places into its pending
    // slot (the omitting call), P13's at the conflicting access.
    int Located(IEnumerable<string> holders, Dictionary<string, string> taint, int at)
    {
        var first = holders.Where(taint.ContainsKey).Select(h => taint[h]).OrderBy(x => x == "τ13" ? 1 : 0).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault();
        return first != null && first.StartsWith("τD:") ? ns.First(n => n.Taints.Contains(first[3..]) || n.TemplateTaints.Contains(first[3..])).Ix : at;
    }
    public IEnumerable<string> UnsupportedNodes() => Unsupported.Select(e => ns[e.P].Label).Distinct();

    // disturbed at ℓ ⇒ Φ_c is replaced by Φ_c^dist in every σ (by holder, like ended).
    static HashSet<Fact> Disturbed(Node n, HashSet<Fact> o) =>
        n.Disturbs.Count == 0 ? o : o.Select(t => n.Disturbs.Contains(t.L) ? (t.H, t.L + "^dist") : t).ToHashSet();

    // A reserved Loan acts as shared until activation. Round 4: only in its own reservation holder, inside the call's
    // window; any other holder carries an earlier, activated incarnation of the site (loops). By id: by position only.
    bool Reserved(string h, string l, int i) => activeAt.TryGetValue(l, out var at) &&
        (X is Abl.IdExclusion or Abl.AText ? i <= at : h == issue[l].Into && issueAt[l] < i && i <= at);

    public IEnumerable<string> ErrorNodes() => Errors.Select(e => ns[e.P].Label).Distinct();
    public string LabelAt(int p) => ns[p].Label; // the analysed encoding's node (Inline, AsDrop and Secured have their own)
    public bool CarriesAt(string label, string h, string l) => In[IxOf(label)].Contains((h, l));
    public string Dump() => string.Join("\n", ns.Select(n => $"{n.Label,-44} carries {{{string.Join(" ", In[n.Ix].OrderBy(t => t.H).Select(t => t.H + ":" + t.L))}}} live {{{string.Join(" ", liveIn[n.Ix].Order())}}}"))
        + "\nplaces: " + string.Join("; ", places.Select(p => p.Key + " = {" + string.Join(", ", p.Value) + "}"));
}

// Hand-encoded facts. Each node is one program point; labels are the source lines; `$t` names a temporary.
static class Cases
{
    const bool Ref = false, Uniq = true;

    public static List<Case> All() => [
        Replacement(), Join(), Slot("3", copy: true, early: false), SlotReplaced(), Slot("4", copy: false, early: false), Slot("4b", copy: false, early: true),
        Index("5", nonPath: true, rewrite: false), Index("5b", nonPath: false, rewrite: true), Index("5c", nonPath: true, rewrite: true),
        Reborrow("6", write: true), Reborrow("6b", write: false), Partial("7", inspectP: false), Partial("7b", inspectP: true),
        Deepest("8a", clear: true, check: false), Deepest("8b", clear: false, check: false), Deepest("8c", clear: false, check: true),
        CopyChild(), StoreChild(), TwoBases(), Loop("9d", "x = 3"), Loop("9d'", "y = 3"), FieldBase("9e", partial: false), FieldBase("9e'", partial: true),
        ThroughCall(), Exchange(), ExchangeThenReplace(), ExchangeReplaces(), SwapBase(), StealThroughW(), SharedBase(), Nested(), MoveBase(), UniqBase(),
        Replace(), AppendRemove(), Insert(), Swap(), Unreachable(), UnreachableJoin(),
        MoveBan("12a", rewrite: true), MoveBan("12b", rewrite: false),
        RefBase("13a", shared: false, "outer"), RefBase("13b", shared: false, "inner"), RefBase("13c", shared: false, "through"),
        RefBase("13d", shared: true, "outer"), RefBase("13e", shared: true, "inner"),
        MoveAncestor(), ReplaceBase("15a", write: false), ReplaceBase("15b", write: true),
        Alias("16a", "reborrow"), Alias("16b", "copy"), Alias("16c", "aggregate"), Alias("16d", "stored"), Alias("16e", "inner"),
        DeadHolder("17a", chain: true), DeadHolder("17b", chain: false), PartialMove("18a", restore: true), PartialMove("18b", restore: false),
        InPlace("19a", parent: false), InPlace("19b", parent: true),
        LoopSite("20a", "uniq"), LoopSite("20b", "reserved"), LoopSite("20c", "refkept"), LoopSite("20d", "uniqkept"),
        LoopSite("20e", "child"), LoopSite("20f", "window"),
        TempStep("21a", clear: false), TempStep("21b", clear: true), IndexBan("22a", constant: false), IndexBan("22b", constant: true),
        OuterThenWrite(), ParamOuter(), DropBase("24a", userDrop: true), DropBase("24b", userDrop: false), DropMerged(),
        SelfStore("25a", "live"), SelfStore("25b", "dead"), SelfStore("25c", "enum"), Destroying("26a", clear: false), Destroying("26b", clear: true),
        SharedField(), Args("28a", "uniqref"), Args("28b", "refref"), Args("28c", "fields"), Args("28d", "stored"),
        CrossingKill(), EscapedTransit(), PartTransit(), LinkedList("30a", "obj"), LinkedList("30b", "uniq"), LinkedList("30c", "conflict"), LinkedList("30d", "peek"),
        SwapParams("31a", caller: false), SwapParams("31b", caller: true), CopyThrough("32a", childLive: false), CopyThrough("32b", childLive: true),
        PreservesResults("33a", "two"), PreservesResults("33b", "across"), PreservesResults("33c", "callonly"),
        IndexKill("34a", deref2: false), IndexKill("34b", deref2: true), IndexKill("34c", deref2: false, whole: true),
        Walk("35a", loop: false), Walk("35b", loop: true),
        PhantomParents("36a", direct: false, retain: false), PhantomParents("36b", direct: false, retain: true),
        PhantomParents("36c", direct: true, retain: false), PhantomParents("36d", direct: true, retain: true), SplitOuter(),
        PhantomRoot("36e"), PhantomRoot("36f"), PhantomRoot("36g"), PhantomRoot("36h"),
        Route("38a"), Route("38b"), Route("38c"), Route("38d"), Route("38e"), Route("38f"), Route("38g"), Route("38h"), Route("38i"),
        Route("38j"), Route("38k"), PreservesResults("33d", "other"), PreservesResults("33e", "inwindow"),
        SharedLayer("39a"), SharedLayer("39b"), SharedLayer("39c"), SharedLayer("39d"), SharedLayer("39e"), SharedLayer("39f"),
        SharedLayer("39g"), SharedLayer("39h"), SharedLayer("39i"), SharedLayer("39j"), SharedLayer("39k"), SharedLayer("39l"), SharedLayer("39m"), SharedLayer("39n"),
        // Round 10 (prototype round 10): G92b's fold, designer B's cases (38→40, 39→41) without the exact duplicates of A's
        // (40a = 38c, 40b = 38b, 41a = 39a, 41b = 39b, 41h = 39k, 41j = 39n), the Slice index, and G110 × guard candidates.
        Fold("38l"), Fold("38m"), Fold("38n"),
        .. new[] { "40c", "40d", "40e", "40f", "40g", "40h" }.Select(RouteWrite),
        .. new[] { "41c", "41d", "41e", "41f", "41g", "41i", "41k", "41l", "41m", "41n", "41o", "41p", "41q" }.Select(SharedPast),
        Slice("42a"), Slice("42b"), SliceCase("42c", inline: false), Slice("42d"), GuardExample(), Abandoned("44a"), Abandoned("44b"), Abandoned("44c"),
        // Case 23a review (cases 50-94, unchanged), and 92/93 with G110 step classes (92g, 93g)
        CrossingOrder("50", "reverse"), CrossingOrder("51", "recvAfter"), CrossingOrder("52", "recvBefore"), CrossingOrder("53", "arg"),
        Siblings("54", "both"), Siblings("55", "old"), Siblings("56", "conflict"),
        Conditional("57", "one"), Conditional("58", "both"), BackEdge("59"), Conditional("60", "loopOne"), Conditional("61", "loopValid"),
        Transfer2("62", "move"), Transfer2("63", "exchange"), Transfer2("64", "swap"), Transfer2("65", "replace"), Transfer2("66", "replaceThrough"),
        IndexMatch("67", "same"), IndexMatch("68", "other"), IndexMatch("69", "runtimeWrite"), IndexMatch("70", "runtimeBoth"),
        IndexMatch("71", "nestedPrefix"), IndexMatch("72", "nestedOther"), FieldMatch("73", same: true), FieldMatch("74", same: false),
        OwnStep("75", "objField"), OwnStep("76", "objWhole"), OwnStep("77", "objOld"), OwnStep("78", "rcClone"), OwnStep("79", "rcOld"), OwnStep("80", "rcDrop"),
        EscapedChild(), Stride2(), DirectStep(), ReservedRoute("84", retained: true), ReservedRoute("85", retained: false),
        AfterOuterEnd("86", "grandchild"), AfterOuterEnd("87", "oldSlot"), TempBase(), SamePoint("89", oldRoute: false), SamePoint("90", oldRoute: true), DropMerged2(), Guard("92", viaSubject: true), Guard("93", viaSubject: false), Guard("92g", viaSubject: true, g110: true), Guard("93g", viaSubject: false, g110: true), AfterOuterEnd("94", "reborrowSlot"),
        // Design review of the 1b choice (cases 100+)
        .. new[] { "100", "100b", "100c", "100d", "101", "102", "103", "104", "105", "106", "107", "108", "109", "110", "111", "112", "112b" }.Select(c => c == "105" ? ReviewSlice() : Review(c)),
        // Design review round 2 of the 1b choice (cases 120+)
        .. new[] { "120", "120b", "121", "122", "123", "123b", "123c", "124", "125", "126", "127", "127b" }.Select(Review2),
        // Round 11 (A3 E13, E14; the core lane's group review B1, B2, B4)
        .. new[] { "130a", "130b", "130c", "130d", "130e", "130f", "130g" }.Select(ClosureCall),
        .. new[] { "131a", "131b", "131c", "131d", "131e", "131f", "131g" }.Select(CallInputs),
        .. new[] { "132a", "132b", "132c", "132d", "132e", "132f", "132g", "132h", "132i", "132j", "132k", "132l", "132m", "132n" }.Select(PairInputs),
        .. new[] { "133a", "133b", "133c", "133d" }.Select(RawInit),
        .. new[] { "134a", "134b", "134c", "134d", "134e", "134f", "134g", "135a", "135b", "135c" }.Select(Reviewer.Make),
        // Round 12 (A3 E13, the owner's option R1: operand evaluation order)
        .. Round12.All(),
        // Round 13 (A3 E13 rewritten): the second design review's cases and this round's (ids r13…, family 141)
        .. R13.All(),
        // Round 15 (PLAN G148): call store rows past the argument's first layer (ids r14…, family 142)
        .. R14A.All(), .. R14B.All(), .. R14V.All()];

    // ---- Round 11 (A3 E13, E14; the core lane's group review B1, B2, B4) ----
    // E13: a Call's inputs are its argument operands and a Value callee's operand. Input: an access the MIR leaves AT the Call
    // (checked on CS_call(ℓ, −)); Callee: the Value callee's reservation, activated at the Call; Acquire: a PairAcquire
    // Reborrow secured into a temp (a reservation there); AcquireAt: one left at the Call. E14: RawStore.

    // B1: closure calls. bump = func [r] (k: i32) => r@follow += k is an Exclusive call: `$7 = &uniq two-phase bump;
    // bump-call(move $7, …)` (MIR forms the callee's receiver as a reservation); peek = func [r] () => inspect(r@follow) is a
    // Shared call: `$7 = &peek`.
    static Case ClosureCall(string id)
    {
        var c = new Case(id, "B1: " + id switch
        {
            "130a" => "`let v = bump@ref; bump(); inspect(v)` (the review's program)",
            "130b" => "`let u = bump@uniq; let v = u@ref; u(); inspect(v)`",
            "130c" => "a Shared call: `let v = peek@ref; peek(); inspect(v)`",
            "130d" => "`bump(do { let v = bump@ref; inspect(v); 1 })` (v ends before the activation)",
            "130e" => "`let v = bump@ref; bump(n); inspect(v)` (n: a direct Place operand at the Call)",
            "130f" => "`let u = bump@uniq; var keep = other@ref; u(do { keep = u@ref; 1 }); inspect(keep)` (the argument's borrow stays live)",
            _ => "`bump(value)` (`bump-call(move $7, copy value)`: the callee's holder carries r's Loan on value as a parent)",
        });
        bool shared = id == "130c";
        var f = shared ? "peek" : "bump";
        c.N("var value: i32 = 1; var n = 2; var other = 3").Def("value", "n", "other");
        c.N(shared ? "let r = value@ref" : "let r = value@uniq").Def("r").Borrow("Lr", "value", !shared, "r");
        c.N(shared ? "var peek = func [r] () => inspect(r@follow)" : "var bump = func [r] (k: i32) => r@follow += k").Def(f).A(K.Move, "r").Flow("r", f);
        string call;
        switch (id)
        {
            case "130a": case "130c": case "130e":
                c.N($"let v = {f}@ref").Def("v").Borrow("Lv", f, Ref, "v");
                if (shared) c.N("$7 = &peek (callee, Shared call)").Def("$7").Borrow("L7", "peek", Ref, "$7");
                else c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                var cn = c.N(call = id == "130e" ? "bump-call(move $7, copy n)" : $"{f}-call(move $7)").Input(K.Move, "$7");
                if (!shared) cn.Callee("L7");
                if (id == "130e") cn.Input(K.Read, "n");
                c.N("inspect(v)").A(K.Read, "v.*");
                return shared ? c.Valid() : c.Error(call);
            case "130b":
                c.N("let u = bump@uniq").Def("u").Borrow("Lu", "bump", Uniq, "u");
                c.N("let v = u@ref").Def("v").Borrow("Lv", "u", Ref, "v");
                c.N("$7 = &uniq two-phase (*u) (callee, reserved)").Def("$7").Borrow("L7", "u.*", Uniq, "$7", reserve: true);
                c.N(call = "u-call(move $7)").Input(K.Move, "$7").Callee("L7");
                c.N("inspect(v)").A(K.Read, "v.*");
                return c.Error(call);
            case "130d":
                c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                c.N("  let v = bump@ref (argument)").Def("v").Borrow("Lv", "bump", Ref, "v");
                c.N("  inspect(v)").A(K.Read, "v.*");
                c.N("  $k = 1 (end of do: StorageDead v)").Def("$k", "v");
                c.N("bump-call(move $7, move $k)").Input(K.Move, "$7").Input(K.Move, "$k").Callee("L7");
                return c.Valid();
            case "130g":
                c.N("$7 = &uniq two-phase bump (callee, reserved)").Def("$7").Borrow("L7", "bump", Uniq, "$7", reserve: true);
                c.N(call = "bump-call(move $7, copy value)").Input(K.Move, "$7").Callee("L7").Input(K.Read, "value");
                return c.Error(call);
            default: // 130f
                c.N("let u = bump@uniq").Def("u").Borrow("Lu", "bump", Uniq, "u");
                c.N("var keep = other@ref").Def("keep").Borrow("Lo", "other", Ref, "keep");
                c.N("$7 = &uniq two-phase (*u) (callee, reserved)").Def("$7").Borrow("L7", "u.*", Uniq, "$7", reserve: true);
                c.N("  keep = u@ref (argument)").Assign("keep").Borrow("Lk", "u", Ref, "keep");
                c.N("  $k = 1").Def("$k");
                c.N(call = "u-call(move $7, move $k)").Input(K.Move, "$7").Input(K.Move, "$k").Callee("L7");
                c.N("inspect(keep)").A(K.Read, "keep.*");
                return c.Error(call);
        }
    }

    // B2: the accesses a Call's own operands make AT the Call. give(a: uniq/i32, b: i32); giveR(a: ref/i32, b: i32);
    // giveS(a: uniq/i32, b: ref/i32); S {n: i32} with set(self: uniq/S, n: i32). A Place operand that no later operand's code
    // follows is read at the Call (G123, DECISIONS R4-M55).
    static Case CallInputs(string id)
    {
        var c = new Case(id, "B2: " + id switch
        {
            "131a" => "`let r = x@uniq; give(r, x)` (MIR: `$4 = &uniq two-phase (*r)`, carrying r's Loan on x; `give(move $4, copy x)`)",
            "131b" => "`let r = x@uniq; giveR(r, x)`, giveR(a: ref/i32, b: i32) (`$4 = &(*r)`; `giveR(move $4, copy x)`)",
            "131c" => "`let r = x@uniq; giveS(r, x)`, giveS(a: uniq/i32, b: ref/i32) (`$5 = &x` is formed before the Call)",
            "131d" => "`give(x@uniq, x)` (the read precedes the activation)",
            "131e" => "`let r = x@uniq; give(r, r@follow)` (a read of the reserved referent at the Call)",
            "131f" => "`s.set(s.n)` (`$r = &uniq two-phase s`; `set(move $r, copy s.n)`)",
            _ => "`Kimi.Intrinsics.replace(x@uniq, with: x)` (SPEC 15.6.7's form with the read left at the Call)",
        });
        c.N("var x = 1; var s = S(n: 1)").Def("x", "s");
        string call;
        switch (id)
        {
            case "131d":
                c.N("$4 = x@uniq (reserved)").Def("$4").Borrow("L4", "x", Uniq, "$4", reserve: true);
                c.N("give(move $4, copy x)").Input(K.Move, "$4").Activate("L4").Input(K.Read, "x");
                return c.Valid();
            case "131g":
                c.N("$t = x@uniq (reserved, replace target)").Def("$t").Borrow("Lt", "x", Uniq, "$t", reserve: true, inPlace: true);
                c.N("replace(move $t, copy x)").Input(K.Move, "$t").Activate("Lt").Input(K.Read, "x").Through("$t.*", "x");
                return c.Valid();
            case "131f":
                c.N("$r = s (set's receiver, reserved)").Def("$r").Borrow("Lr", "s", Uniq, "$r", reserve: true);
                c.N("set(move $r, copy s.n)").Input(K.Move, "$r").Activate("Lr").Input(K.Read, "s.n");
                return c.Valid();
        }
        c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        switch (id)
        {
            case "131a": case "131e":
                c.N("$4 = &uniq two-phase (*r) (reserved)").Def("$4").Borrow("L4", "r.*", Uniq, "$4", reserve: true);
                c.N(call = id == "131a" ? "give(move $4, copy x)" : "give(move $4, copy (*r))").Input(K.Move, "$4").Activate("L4").Input(K.Read, id == "131a" ? "x" : "r.*");
                return id == "131a" ? c.Error(call) : c.Valid();
            case "131b":
                c.N("$4 = &(*r)").Def("$4").Borrow("L4", "r.*", Ref, "$4");
                c.N(call = "giveR(move $4, copy x)").Input(K.Move, "$4").Input(K.Read, "x");
                return c.Error(call);
            default: // 131c
                c.N("$4 = &uniq two-phase (*r) (reserved)").Def("$4").Borrow("L4", "r.*", Uniq, "$4", reserve: true);
                c.N(call = "$5 = &x").Def("$5").Borrow("L5", "x", Ref, "$5");
                c.N("giveS(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5").Activate("L4");
                return c.Error(call);
        }
    }

    // B2 with pair layers (the Semantics view decides a PairAcquire: a Copy in the ref view, a Reborrow in the uniq view).
    // inner2<s/T>(a: ref/(s/T), b: s/T); inner3<s/T>(a: s/T, b: ref/(s/T)); inner7<s/S>(a: s/S, b: i32); bumpS<s/S>(q: s/S)
    // -> i32; inner8<s/S>(b: i32, a: s/S); inner9<s/T>(a: s/T, b: s/T); inner11<s/S>(a: s/S, b: i32); innerW<s/T>(a: s/T,
    // b: W<s/T>); s is ref or uniq; S {n: i32}; W<s/T> {a: s/T}. The caller is generic: outer<s/T>(p: s/T). G123 (R4-M55,
    // MirBuilder.Builds on claude/a5-1b-mir) secures an operand into a temp when a later operand builds code: every operand
    // builds code but a constant and a Place read as it is (Copied or Moved), so a PairAcquire (HirAcquire.ByCase) builds code
    // and a Copy or Move of a Place does not. Secured (SecureAcquire): the same program with the PairAcquire secured.
    static Case PairInputs(string id) { var c = PairInputsCase(id, false); if (id is "132j" or "132m") c.Secured = PairInputsCase(id, true); c.Always = Reviewer.PairAlways(id); return c; }

    static Case PairInputsCase(string id, bool secure)
    {
        bool uniqView = id is not ("132b" or "132d" or "132i" or "132l" or "132n");
        var c = new Case(id, "B2 pair, " + (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "132a" or "132b" => "`var v = p; inner2(v@ref, v)` (`$4 = &v`; `inner2(move $4, acquire v)`, the PairAcquire left at the Call)",
            "132c" or "132d" => "`var v = p; inner3(v, v@ref)` (G123 secures the PairAcquire: `$3 = acquire v`; `$4 = &v`)",
            "132e" => "`inner7(p, p.n + 1)` (`$3 = acquire p`, a reservation; a shared read of p.n during it)",
            "132f" => "`inner7(p, bumpS(p))` (a second exclusive acquisition of p during the reservation)",
            "132g" => "`inner8(p.n + 1, p)` (`$3 = p.n + 1`; `inner8(move $3, acquire p)`)",
            "132h" or "132i" => "`var v = p; inner9(v, v)` (the second PairAcquire builds code, so `$3 = acquire v` is secured; `inner9(move $3, acquire v)`)",
            "132j" => "`inner11(p, p.n)` (`inner11(acquire p, copy (*p).n)`: both left at the Call)",
            "132m" or "132n" => "`var v = p; let u = v@follow@ref; innerR(v, u)`, innerR<s/T>(a: s/T, b: ref/T) (`innerR(acquire v, copy u)`: both left at the Call)",
            _ => "`var w = W(a: p); innerW(w.a, w@move)` (round 13, the builder's MIR: `w@move` builds code, so `$4 = acquire w.a; $5 = move w; innerW(move $4, move $5)`; round 12: the valid spelling of round 11's `innerW(w.a, w)`, a TransferRequired_Kd)",
        } + (secure ? " [secured: SecureAcquire]" : ""));
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        string call;
        Node AtCall(Node n, string place) => uniqView ? n.AcquireAt(place + ".*") : n.Input(K.Read, place); // PairAcquire
        switch (id)
        {
            case "132a": case "132b":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                AtCall(c.N(call = "inner2(move $4, acquire v)").Input(K.Move, "$4"), "v");
                return uniqView ? c.Error(call) : c.Valid();
            case "132c": case "132d":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                if (uniqView) c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3");
                else c.N("$3 = acquire v (secured: a Copy)").Def("$3").A(K.Read, "v").Flow("v", "$3");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                var n3 = c.N(call = "inner3(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4");
                if (uniqView) n3.Activate("L3");
                return uniqView ? c.Error(call) : c.Valid();
            case "132e": case "132f":
                c.N("$3 = acquire p (secured: a reservation)").Def("$3").Acquire("L3", "p.*", "$3");
                if (id == "132e") c.N("$4 = p.n + 1").Def("$4").A(K.Read, "p.*.n");
                else c.N(call = "$4 = bumpS(acquire p)").Def("$4").AcquireAt("p.*");
                c.N("inner7(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L3");
                return id == "132e" ? c.Valid() : c.Error("$4 = bumpS(acquire p)");
            case "132g":
                c.N("$3 = p.n + 1").Def("$3").A(K.Read, "p.*.n");
                c.N("inner8(move $3, acquire p)").Input(K.Move, "$3").AcquireAt("p.*");
                return c.Valid();
            case "132h": case "132i":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                call = "Call inner9(v, v)";
                if (uniqView) c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3");
                else c.N("$3 = acquire v (secured: a Copy)").Def("$3").A(K.Read, "v").Flow("v", "$3");
                var n9 = AtCall(c.N(call).Input(K.Move, "$3"), "v");
                if (uniqView) n9.Activate("L3");
                return uniqView ? c.Error(call) : c.Valid();
            case "132j":
                call = "Call inner11(p, p.n)";
                if (secure) { c.N("$3 = acquire p (secured: a reservation)").Def("$3").Acquire("L3", "p.*", "$3"); c.N(call).Input(K.Move, "$3").Activate("L3").Input(K.Read, "p.*.n"); }
                else c.N(call).AcquireAt("p.*").Input(K.Read, "p.*.n");
                return c.Valid();
            case "132m": case "132n": // a Copy after an at-Call PairAcquire: its Loans sit in its source holder, live into ℓ
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
                call = "Call innerR(v, u)";
                if (secure) { c.N("$3 = acquire v (secured: a reservation)").Def("$3").Acquire("L3", "v.*", "$3"); c.N(call).Input(K.Move, "$3").Activate("L3").Input(K.Read, "u"); }
                else AtCall(c.N(call), "v").Input(K.Read, "u");
                return uniqView ? c.Error(call) : c.Valid();
            default: // 132k, 132l
                if (uniqView) c.N("var w = W(a: p) (a Reborrow of *p, stored)").Def("w").Borrow("Lw", "p.*", Uniq, "w.a");
                else c.N("var w = W(a: p) (a Copy of p)").Def("w").A(K.Read, "p").Flow("p", "w.a");
                // Round 13 (the second design review's MIR dump p1/a132k): an explicit `@move` builds code, so G123 secures the
                // PairAcquire before it, and the Move is its own Use (rounds 11-12 left both at the Call).
                if (uniqView) c.N("$4 = acquire w.a (secured: a reservation)").Def("$4").Acquire("L4", "w.a.*", "$4");
                else c.N("$4 = acquire w.a (secured: a Copy)").Def("$4").A(K.Read, "w.a").Flow("w.a", "$4");
                c.N(call = "$5 = move w").Def("$5").A(K.Move, "w").Flow("w", "$5");
                var nw = c.N("innerW(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5"); if (uniqView) nw.Activate("L4");
                return uniqView ? c.Error(call) : c.Valid();
        }
    }

    // B4 (E14): Raw.initialize(storage: raw/T, value: T) stores value into the referent of a raw storage operand, E (SPEC
    // 15.6: storage the body does not own, "a value stored there may be used after every point"). p[0] reads E.
    static Case RawInit(string id)
    {
        var c = new Case(id, "B4: " + id switch
        {
            "133a" => "`let p: raw/(ref/i32) = Raw.allocate(1); Raw.initialize(p, x@ref); x = 2; let q = p[0]; inspect(q)` (the review's program)",
            "133b" => "133a with `x = 2` before the initialize",
            "133c" => "133a without the write: `…; let q = p[0]; inspect(q); inspect(x)`",
            _ => "`let p: raw/(uniq/i32) = …; Raw.initialize(p, x@uniq); inspect(x)` (the reservation is retained into E)",
        });
        c.N("var x = 1").Def("x");
        c.N("let p = Raw.allocate(1)").Def("p");
        if (id == "133b") c.N("x = 2").Assign("x");
        if (id == "133d") c.N("$a = x@uniq (reserved)").Def("$a").Borrow("La", "x", Uniq, "$a", reserve: true);
        else c.N("$a = x@ref").Def("$a").Borrow("La", "x", Ref, "$a");
        var init = c.N("Raw.initialize(copy p, move $a)").Input(K.Read, "p").Input(K.Move, "$a").RawStore("p", "$a");
        if (id == "133d") { init.Activate("La"); c.N("inspect(x)").A(K.Read, "x"); return c.Error("inspect(x)"); }
        if (id == "133a") c.N("x = 2").Assign("x");
        c.N("let q = p[0]").Def("q").Flow("p.*.0", "q");
        c.N("inspect(q)").A(K.Read, "q.*");
        if (id == "133c") c.N("inspect(x)").A(K.Read, "x");
        return id == "133a" ? c.Error("x = 2") : c.Valid();
    }

    static Case Review2(string id) { var c = Review2Case(id, false); if (id is "123" or "123c") c.AsDrop = Review2Case(id, true); return c; }

    // ---- Design review round 2 (cases 120+): the own-value seed (A3 E5) against a store into a holder live only by the
    // closure, writeback through a reference, a retaining store, a destroying call, a loop, γ and the prefix kill.
    // S3 {r: ref/i32, n: i32}; H {a: i32, b: uniq/S3}; S {r: uniq/i32, q: ref/i32} with a drop that writes through self.r.
    static Case Review2Case(string id, bool asDrop)
    {
        var c = new Case(id, "review r2: " + id switch
        {
            "120" => "100 stored into a part: `let p = h.a@ref; h.b = r@follow@uniq; inspect(p@follow)` (h used after only through p's Loan; MIR: value temp, then Move)",
            "120b" => "120 with the Reborrow formed directly into h.b (no MIR form today)",
            "121" => "writeback through a reference: `let r = s@uniq; r.r = r.n@ref; r.n += 1` (r and s dead after; Rust E0506)",
            "122" => "retaining store: `stash(into: h@uniq, item: r@follow@uniq)` after `r.r = r.n@ref` (stash stores item into (*into).b)",
            "123" => "destroying call on a self-referential holder: `s.q = s.r@follow@ref; replace(s@uniq, with: S(…))`, s dead after (A3's err(destroy); DestroyAsDrop: a Drop on CS)",
            "123b" => "123 with the destruction checked as A3's err(destroy): CS_call(ℓ, $t) seeded by σ_in($t) (the r2 review's form; 123 equals it since round 10 final)",
            "123c" => "123 with s used after the replace (`inspect(s)`)",
            "124" => "loop: `while c { let q = r@follow@uniq; q.n = 5; inspect(q.r@follow); r.r = r.n@ref }` (second iteration)",
            "125" => "loop, valid: 124 with a shared Reborrow, `let q = r@follow@ref; inspect(q.r@follow)`",
            "126" => "guard: the candidate stored into a part of a holder used after only through p: `let p = holder.a@ref; … if (do { holder.saved = d; true }) …; use(p)`",
            "127" => "prefix kill: `var r = s@uniq; r.r = r.n@ref; r = s2@uniq; s.n = 5; inspect(s.r@follow)` (Lc ended with r; s.r keeps Lc's parent Lr)",
            _ => "127 without the last read (s dead after `s.n = 5`): valid",
        });
        void Self(Case k) { k.N("var z = 0; var s = S3(r: z@ref, n: 1)").Def("z", "s").Borrow("Lz", "z", Ref, "s.r"); }
        switch (id)
        {
            case "120": case "120b":
                Self(c); c.N("var w = 0; var s2 = S3(r: w@ref, n: 1)").Def("w", "s2").Borrow("Lw", "w", Ref, "s2.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("var h = H(a: 0, b: s2@uniq)").Def("h").Borrow("Ls2", "s2", Uniq, "h.b");
                c.N("let p = h.a@ref").Def("p").Borrow("Lha", "h.a", Ref, "p");
                string at;
                if (id == "120") { c.N(at = "$v = r@follow@uniq").Def("$v").Borrow("Lq", "r.*", Uniq, "$v"); c.N("h.b = move $v").A(K.Write, "h.b").A(K.Move, "$v").Flow("$v", "h.b"); }
                else c.N(at = "h.b = r@follow@uniq").A(K.Write, "h.b").Borrow("Lq", "r.*", Uniq, "h.b");
                c.N("inspect(p@follow)").A(K.Read, "p.*");
                return c.Error(at);
            case "121":
                Self(c);
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("$w = TwoPhaseUniq((*r).n) (writeback)").Def("$w").Borrow("Lw", "r.*.n", Uniq, "$w", reserve: true);
                c.N("$v = (*$w) + 1").Def("$v").A(K.Read, "$w.*");
                c.N("(*$w) = $v (activates)").Activate("Lw").Through("$w.*", "$v");
                return c.Error("(*$w) = $v (activates)");
            case "122":
                Self(c); c.N("var w = 0; var s2 = S3(r: w@ref, n: 1)").Def("w", "s2").Borrow("Lw", "w", Ref, "s2.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("var h = H(a: 0, b: s2@uniq)").Def("h").Borrow("Ls2", "s2", Uniq, "h.b");
                c.N("$a = h@uniq (into, reserved)").Def("$a").Borrow("La", "h", Uniq, "$a", reserve: true);
                c.N("$b = r@follow@uniq (item, reserved)").Def("$b").Borrow("Lb", "r.*", Uniq, "$b", reserve: true);
                c.N("stash($a, $b) (stores item into (*into).b)").Activate("La").Activate("Lb").Through("$a.*.b", "$b");
                c.N("h.b@follow.n = 5").A(K.Write, "h.b.*.n"); c.N("inspect(h.b@follow.r@follow)").A(K.Read, "h.b.*.r.*");
                return c.Error("stash($a, $b) (stores item into (*into).b)");
            case "123": case "123b": case "123c":
                c.N("var x = 1; var y = 2; var z = 0").Def("x", "y", "z");
                c.N("var s = S(r: x@uniq, q: z@ref)").Def("s").Borrow("L1", "x", Uniq, "s.r").Borrow("Lz", "z", Ref, "s.q");
                c.N("s.q = s.r@follow@ref").Assign("s.q").Borrow("Lq", "s.r.*", Ref, "s.q");
                c.N("$t = s@uniq (replace target, reserved; destroying)").Def("$t").Borrow("Lt", "s", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$v = S(r: y@uniq, q: z@ref)").Def("$v").Borrow("Ly", "y", Uniq, "$v.r").Borrow("Lz2", "z", Ref, "$v.q");
                const string rep = "replace($t, with: $v)";
                var n = c.N(rep).Activate("Lt").Through("$t.*", "$v");
                if (!asDrop) n.DestroyAt("s", "Lt"); else n.Drop("s").A(K.Write, "s.r.*"); // AsDrop: 26a's old encoding, a Drop on CS and the drop's write
                if (id == "123c") c.N("inspect(s)").Use("s");
                return c.Error(rep);
            case "124": case "125":
                Self(c);
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("while c").Goto("  let q = r@follow@uniq", "()");
                c.N("  let q = r@follow@uniq").Def("q").Borrow("Lq", "r.*", id == "124", "q");
                if (id == "124") c.N("  q.n = 5").A(K.Write, "q.*.n");
                c.N("  inspect(q.r@follow)").A(K.Read, "q.*.r.*");
                c.N("  $t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("  r.r = $t").Through("r.*.r", "$t").Goto("while c");
                c.N("()");
                return id == "124" ? c.Error("  let q = r@follow@uniq") : c.Valid();
            case "126":
                c.N("var fallback = Data(); var packet: Packet = .Data(Data())").Def("fallback", "packet");
                c.N("var holder = H(a: 0, saved: fallback@ref)").Def("holder").Borrow("Lf", "fallback", Ref, "holder.saved");
                c.N("let p = holder.a@ref").Def("p").Borrow("Lha", "holder.a", Ref, "p");
                c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
                c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
                c.N("guard: holder.saved = d").Assign("holder.saved").Flow("d", "holder.saved");
                const string end = "guard cleanup: EndBorrow d";
                c.N(end).EndBorrow("d");
                c.N("use(p)").A(K.Read, "p.*");
                return c.Error(end);
            default: // 127, 127b
                Self(c); c.N("var s2 = S3(r: z@ref, n: 2)").Def("s2").Borrow("Lz3", "z", Ref, "s2.r");
                c.N("var r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                c.N("r = s2@uniq").Assign("r").Borrow("Lr2", "s2", Uniq, "r");
                c.N("s.n = 5").A(K.Write, "s.n");
                if (id == "127b") return c.Valid();
                c.N("inspect(s.r@follow)").A(K.Read, "s.r.*");
                return c.Error("s.n = 5");
        }
    }

    // ---- Design review of the 1b choice (cases 100+) ----
    // S3 {r: ref/i32, n: i32}. 100-101: a holder used after ℓ only through the value ℓ stores or activates (39o's family):
    // the closure at ℓ must see it. 100: a Reborrow whose R2 parent names s; 100d: a retained reservation; 101: a direct
    // reservation of a self-referential s at an argument, s dead after the call.
    static Case Review(string id)
    {
        var c = new Case(id, "review: " + id switch
        {
            "100" => "`let r = s@uniq; r.r = r.n@ref; let q = r@follow@uniq; q.n = 5; inspect(q.r@follow)` (s used after only through q's parent Loan)",
            "100b" => "100 with `let q = r@follow@ref; inspect(q.r@follow)`",
            "100c" => "100 with `let q = r@move`",
            "100d" => "100 through a retaining call: `let q = keep(r@follow@uniq); q.n = 5; inspect(q.r@follow)`",
            "101" => "`s.r = s.n@ref; setN(s@uniq, 5)` (s not used after the call; setN writes s.n)",
            "102" => "guard stores the candidate through a live reference: `let w = holder@uniq; match packet .Data(let d) if (do { w.saved = d; true }) => (); use(w)`",
            "103" => "guard copies a Copy value out of the candidate: `if (do { n = d@follow.count; true })`; `use(n)`",
            "104" => "guard candidate stored into tmp, overwritten before use, in a loop: `while c { match … if (do { tmp = d; true }) => (); tmp = other@ref }; use(tmp)`",
            "106" => "`first = cur.inner.takeWith(do { let old = exchange(cur@uniq, with: b); 0 })`",
            "109" => "writeback of a self-referential holder's part: `s.r = s.n@ref; s.n += 1` (s not used after; SPEC 15.6's \"also used\" rule; rustc E0506)",
            "110" => "`x = y@ref; f(x@uniq)` (x holds a reference to y; x dead after)",
            "111" => "`s.r = s.n@ref; setM(s.m@uniq)` (a sibling of the self-referenced part)",
            "112" => "G94 guard: `return values[reset(values@uniq)]` (the receiver is modified while the index is evaluated)",
            "112b" => "G94 guard: `return values[peek(values@ref)]` (a read during the index)",
            "107" => "route through a parameter: `p@follow.inner.takeWith(do { p@follow = o; 0 })`, p: uniq/(uniq/H)",
            _ => "guard returns the candidate: `.Data(let d) if (do { return d })`",
        });
        switch (id)
        {
            case "100": case "100b": case "100c": case "100d":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("let r = s@uniq").Def("r").Borrow("Lr", "s", Uniq, "r");
                c.N("$t = r.n@ref").Def("$t").Borrow("Lc", "r.*.n", Ref, "$t");
                c.N("r.r = $t").Through("r.*.r", "$t");
                string at;
                if (id == "100") c.N(at = "let q = r@follow@uniq").Def("q").Borrow("Lq", "r.*", Uniq, "q");
                else if (id == "100b") { c.N("let q = r@follow@ref").Def("q").Borrow("Lq", "r.*", Ref, "q"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*"); return c.Valid(); }
                else if (id == "100c") c.N(at = "let q = r@move").Def("q").A(K.Move, "r").Flow("r", "q");
                else
                {
                    c.N("$a = r@follow@uniq (keep's argument, reserved)").Def("$a").Borrow("La", "r.*", Uniq, "$a", reserve: true);
                    c.N(at = "let q = keep($a) (retains)").Activate("La").Def("q").Flow("$a", "q");
                }
                c.N("q.n = 5").A(K.Write, "q.*.n"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*");
                return c.Error(at);
            case "101":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$a = s@uniq (setN's argument, reserved)").Def("$a").Borrow("La", "s", Uniq, "$a", reserve: true);
                c.N("setN($a, 5)").Activate("La").Through("$a.*.n", "z");
                return c.Error("setN($a, 5)");
            case "102": case "103": case "104": case "108":
            {
                c.N("var fallback = Data(); var other = Data(); var packet: Packet = .Data(Data()); var n = 0").Def("fallback", "other", "packet", "n");
                if (id == "102") { c.N("var holder = H(saved: fallback@ref)").Def("holder").Borrow("Lf", "fallback", Ref, "holder.saved"); c.N("let w = holder@uniq").Def("w").Borrow("Lw", "holder", Uniq, "w"); }
                if (id == "104") { c.N("var tmp = other@ref").Def("tmp").Borrow("Lo", "other", Ref, "tmp"); c.N("while c").Goto("  $s = packet@ref (Subject, shared mode)", "use(tmp)"); }
                var ind = id == "104" ? "  " : "";
                c.N(ind + "$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
                c.N(ind + "d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
                var end = ind + "guard cleanup: EndBorrow d";
                if (id == "102") { c.N("guard: w.saved = d").Through("w.*.saved", "d"); c.N(end).EndBorrow("d"); c.N("use(w)").Use("w"); return c.Error(end); }
                if (id == "103") { c.N("guard: n = d@follow.count").Assign("n").A(K.Read, "d.*.count").Flow("d.*.count", "n"); c.N(end).EndBorrow("d"); c.N("use(n)").Use("n"); return c.Valid(); }
                if (id == "108") { c.N("guard: _0 = d (return d)").Flow("d", "$ret"); c.N(end).EndBorrow("d"); c.N("return").Use("$ret"); return c.Error(end); }
                c.N("  guard: tmp = d").Assign("tmp").Flow("d", "tmp"); c.N(end).EndBorrow("d");
                c.N("  tmp = other@ref").Assign("tmp").Borrow("Lo2", "other", Ref, "tmp").Goto("while c");
                c.N("use(tmp)").Use("tmp");
                return c.Valid();
            }
            case "109":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$w = TwoPhaseUniq(s.n) (writeback)").Def("$w").Borrow("Lw", "s.n", Uniq, "$w", reserve: true);
                c.N("$v = (*$w) + 1").Def("$v").A(K.Read, "$w.*");
                c.N("(*$w) = $v (activates)").Activate("Lw").Through("$w.*", "$v");
                return c.Error("(*$w) = $v (activates)");
            case "110": case "111":
                c.N("var y = 0; var z = 0").Def("y", "z");
                if (id == "110")
                {
                    c.N("var x = y@ref").Def("x").Borrow("Ly", "y", Ref, "x");
                    c.N("$a = x@uniq (reserved)").Def("$a").Borrow("La", "x", Uniq, "$a", reserve: true);
                    c.N("f($a)").Activate("La");
                    return c.Valid();
                }
                c.N("var s = S3(r: z@ref, n: 1, m: 2)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                c.N("$a = s.m@uniq (reserved)").Def("$a").Borrow("La", "s.m", Uniq, "$a", reserve: true);
                c.N("setM($a)").Activate("La").Through("$a.*", "z");
                return c.Valid();
            case "112": case "112b":
                c.N("var values: [3 of i32] = [1, 2, 3]").Def("values");
                c.N("$g = values@ref (G94 guard, where located)").Def("$g").Borrow("Lg", "values", Ref, "$g");
                var call = id == "112" ? "reset($a) (index)" : "peek($a) (index)";
                c.N("$a = values@" + (id == "112" ? "uniq (reserved)" : "ref")).Def("$a").Borrow("La", "values", id == "112", "$a", reserve: id == "112");
                c.N(call).Def("$i").Use("$a"); if (id == "112") ns(c).Activate("La");
                c.N("EndBorrow $g (index evaluated)").EndBorrow("$g", guard: false);
                c.N("_0 = values[$i]").A(K.Read, "values.[]").Use("$i");
                return id == "112" ? c.Error(call) : c.Valid();
            case "106":
                c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$t2 = cur@uniq (exchange target, reserved)").Def("$t2").Borrow("Lt2", "cur", Uniq, "$t2", reserve: true);
                c.N("$u = b (Reborrow at a value parameter, reserved)").Def("$u").Borrow("Lu", "b.*", Uniq, "$u", reserve: true);
                const string x = "do { let old = exchange($t2, with: $u); 0 } (argument)";
                c.N(x).Activate("Lt2").Activate("Lu").Def("old").Through("$t2.*", "$u");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first");
                c.N("return first").Flow("first", "$ret");
                return c.Error(x);
            default: // 107
                c.N("func f(p: uniq/(uniq/H), o: uniq/H)").Param("Lp", "p").Param("Lo", "o");
                c.N("$r1 = p@follow.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "p.*.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$o = o (Reborrow)").Def("$o").Borrow("Lo2", "o.*", Uniq, "$o");
                const string w = "do { p@follow = $o; 0 } (argument)";
                c.N(w).A(K.Move, "$o").Through("p.*", "$o");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first");
                c.N("return first").Flow("first", "$ret");
                return c.Error(w);
        }
    }

    static Node ns(Case c) => c.Nodes[^1];

    // 105: a Copy read of a Slice element that stores a reference carries the element's Loans (the Slice index as a shared
    // borrow Deref: content of the handle's backing), not the backing Loan alone (the inline step).
    static Case ReviewSlice() { var c = ReviewSliceCase(false); c.Inline = ReviewSliceCase(true); return c; }

    static Case ReviewSliceCase(bool inline)
    {
        var c = new Case("105", "review: Slice element Copy: `var arr = [x@ref, y@ref]; var s = arr[..]; let e = s[i]; x = 5; inspect(e)`");
        var el = inline ? "$sc.[]" : "$sc.*.[]";
        c.N("var x = 1; var y = 2; let i: isize = pick()").Def("x", "y", "i");
        c.N("var arr: [2 of ref/i32] = [x@ref, y@ref]").Def("arr").Borrow("Lx", "x", Ref, "arr.0").Borrow("Ly", "y", Ref, "arr.1");
        c.N("var s = arr[..]").Def("s").Borrow("La", "arr", Ref, "s");
        c.N("$sc = s (G94 copy)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
        c.N("let e = $sc[i]").Def("e").A(K.Read, el).Flow(el, "e");
        c.N("StorageDead $sc").A(K.Write, "$sc").Def("$sc");
        c.N("x = 5").Assign("x"); c.N("inspect(e)").Use("e");
        return c.Error("x = 5");
    }

    static int Num(Case c) => c.Id.StartsWith("r14") ? 142 : c.Id.StartsWith("r13") ? 141 : char.IsDigit(c.Id[0]) ? int.Parse(new string(c.Id.TakeWhile(char.IsDigit).ToArray())) : 0; // round 13: r13… is family 141
    public static bool NoS1P(Case c) => Num(c) >= 50 || c.Nodes.Any(n => n.EndBorrows.Count > 0); // reviews' cases: design A only
    public static bool Review23a(Case c) => Num(c) is >= 50 and < 100;
    public static bool Review1b(Case c) => Num(c) is >= 100 and < 130;
    public static bool Round11(Case c) => Num(c) >= 130;

    // ---- Round 10 (1b rules, designer A) ----
    // G119 (SPEC 15.6.7): during a call reservation its Place, owner and the references it was reached through are
    // protected; a write, Move, destruction or exclusive operation on them is an error, shared reads coexist.
    // H {var inner: uniq/S}; takeWith(self: uniq/S, f: i32) is a concrete call here (its F6 phantom changes nothing).
    static Case Route(string id)
    {
        var c = new Case(id, id switch
        {
            "38a" => "`Kimi.Intrinsics.replace(r@follow@uniq, with: do { r = y@uniq; 5 })`: writes the reference r the target was reached through",
            "38b" => "`first = cur.inner.takeWith(do { cur.inner = s2; 0 })`: writes the Place holding the reached reference",
            "38c" => "`first = cur.inner.takeWith(do { cur@follow = H(inner: s2); 0 })`: writes the owner of that Place",
            "38d" => "`h@follow@uniq.c.bump(do { h = y@uniq; 1 })`, G92b folded: one reservation $c on `(*h).c`, none on `h.*`",
            "38e" => "`var cur = a; first = cur.inner.takeWith(do { a = b; 0 })`: a is not on the route (precision)",
            "38f" => "21a with indexUniq's receiver reservation: `$r = cursor.children; _t = indexUniq($r, 0); cursor = move _t`",
            "38g" => "review R1: `first = cur.inner.takeWith(do { replace(cur@uniq, with: b); 0 })`",
            "38h" => "`replace(arr[0]@follow@uniq, with: do { arr[1] = z@uniq; 5 })`: distinct constant indices",
            "38j" => "alias: `let pc = cur@uniq; first = cur.inner.takeWith(do { pc@follow = b; 0 })`",
            "38k" => "another name: `var rr = hh@uniq; first = rr.inner.takeWith(do { hh.inner = s2; 0 })`",
            _ => "`replace(arr[i]@follow@uniq, with: do { arr[j] = z@uniq; 5 })`: indices may be equal",
        });
        switch (id)
        {
            case "38a":
                c.N("var x = 1; var y = 2").Def("x", "y");
                c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
                c.N("$t = r@follow@uniq (replace target, reserved)").Def("$t").Borrow("Lt", "r.*", Uniq, "$t", reserve: true, inPlace: true);
                c.N("do { r = y@uniq; 5 } (argument)").Assign("r").Borrow("Ly", "y", Uniq, "r");
                c.N("$five = 5").Def("$five"); c.N("replace($t, with: $five)").Activate("Lt").Through("$t.*", "$five");
                c.N("inspect(r)").Use("r");
                return c.Error("do { r = y@uniq; 5 } (argument)");
            case "38b": case "38c": case "38e": case "38g":
                c.N("func f(a: uniq/H, b: uniq/H, s2: uniq/S)").Param("La", "a").Param("Lb", "b").Param("Ls", "s2");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                string w;
                if (id == "38b") { c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2", "s2.*", Uniq, "$s"); w = "do { cur.inner = $s; 0 } (argument)"; c.N(w).Through("cur.*.inner", "$s"); }
                else if (id == "38c")
                {
                    c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2", "s2.*", Uniq, "$s");
                    c.N("$h = H(inner: $s)").Def("$h").A(K.Move, "$s").Flow("$s", "$h.inner");
                    w = "do { cur@follow = $h; 0 } (argument)"; c.N(w).A(K.Move, "$h").Through("cur.*", "$h");
                }
                else if (id == "38e") { w = null; c.N("do { a = b; 0 } (argument)").Assign("a").Borrow("Lb2", "b.*", Uniq, "a"); }
                else
                {
                    c.N("$t2 = cur@uniq (replace target, reserved)").Def("$t2").Borrow("Lt2", "cur", Uniq, "$t2", reserve: true, inPlace: true);
                    c.N("$u = b (Reborrow at a value parameter, reserved)").Def("$u").Borrow("Lu", "b.*", Uniq, "$u", reserve: true);
                    w = "do { replace($t2, with: $u); 0 } (argument)"; c.N(w).Activate("Lt2").Activate("Lu").Through("$t2.*", "$u");
                }
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return w == null ? c.Valid() : c.Error(w);
            case "38d":
                c.N("var x = C(n: 0); var y = C(n: 1)").Def("x", "y");
                c.N("var h = x@uniq").Def("h").Borrow("L1", "x", Uniq, "h");
                c.N("$c = TwoPhaseUniq((*h).c) (receiver of bump)").Def("$c").Borrow("Rc", "h.*.c", Uniq, "$c", reserve: true);
                c.N("do { h = y@uniq; 1 } (argument)").Assign("h").Borrow("Ly", "y", Uniq, "h");
                c.N("bump($c, 1)").Activate("Rc");
                return c.Error("do { h = y@uniq; 1 } (argument)");
            case "38j":
                c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
                c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
                c.N("let pc = cur@uniq").Def("pc").Borrow("Lpc", "cur", Uniq, "pc");
                c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$b = b (Reborrow)").Def("$b").Borrow("Lb2", "b.*", Uniq, "$b");
                c.N("do { pc@follow = $b; 0 } (argument)").A(K.Move, "$b").Through("pc.*", "$b");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return c.Error("$r1 = cur.inner (receiver, reserved)");
            case "38k":
                c.N("func f(s1: uniq/S, s2: uniq/S)").Param("Ls1", "s1").Param("Ls2", "s2");
                c.N("var hh = H(inner: s1)").Def("hh").Borrow("Li", "s1.*", Uniq, "hh.inner");
                c.N("var rr = hh@uniq").Def("rr").Borrow("Lrr", "hh", Uniq, "rr");
                c.N("$r1 = rr.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "rr.*.inner.*", Uniq, "$r1", reserve: true);
                c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls2b", "s2.*", Uniq, "$s");
                c.N("do { hh.inner = $s; 0 } (argument)").Assign("hh.inner").A(K.Move, "$s").Flow("$s", "hh.inner");
                c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "rr.*.inner.*", "first", "Lr1");
                c.N("return first").Flow("first", "$ret");
                return c.Error("do { hh.inner = $s; 0 } (argument)");
            case "38f":
                c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
                c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
                c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  $r = cursor.children (indexUniq receiver, reserved)", "cursor.value = 1");
                c.N("  $r = cursor.children (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "cursor.*.children", Uniq, "$r", reserve: true);
                c.N("  _t = indexUniq($r, 0) (retains $r)").Activate("Lr").Def("_t").Flow("$r", "_t");
                c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
                c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
                return c.Valid();
            default:
                bool constant = id == "38h";
                c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
                c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
                c.N($"$t = arr[{(constant ? "0" : "i")}]@follow@uniq (replace target, reserved)").Def("$t").Borrow("Lt", constant ? "arr.0.*" : "arr.[].*", Uniq, "$t", reserve: true, inPlace: true);
                var write = $"do {{ arr[{(constant ? "1" : "j")}] = z@uniq; 5 }} (argument)";
                if (constant) c.N(write).Assign("arr.1").Borrow("Lz", "z", Uniq, "arr.1");
                else { c.N("$z = z@uniq").Def("$z").Borrow("Lz", "z", Uniq, "$z"); c.N(write).Through("arr.[]", "$z"); }
                c.N("$five = 5").Def("$five"); c.N("replace($t, with: $five)").Activate("Lt").Through("$t.*", "$five");
                c.N("inspect(arr)").Use("arr");
                return constant ? c.Valid() : c.Error(write);
        }
    }

    // G110 (SPEC 15.6.3, S1): a Borrow past a shared Deref forms no Loan; it carries the Loans of its last shared reference
    // and of the references dereferenced after it. S {r: ref/i32, n: i32}; S3 {rs: ref/(uniq/i32)}; Item an obj class.
    static Case SharedLayer(string id)
    {
        var c = new Case(id, id switch
        {
            "39a" => "PLAN G110: `let v = s.r@follow@ref; let t = s@move; inspect(v)`",
            "39b" => "39a, then `x = 5` before `inspect(v)`",
            "39c" => "shared layer under an exclusive parent: `var p = s@uniq; let v = p.r@follow@ref; s.n = 1; inspect(v)`",
            "39d" => "39c with `x = 5` instead of `s.n = 1`",
            "39e" => "`let v = s.r@follow@ref; s.r = y@ref; inspect(v)`: the stored shared reference is written",
            "39f" => "39e, then `x = 5`",
            "39g" => "objref: `let o = item@objref; let v = o.name@ref; o = other@objref; inspect(v)`",
            "39h" => "objref: `let o = item@objref; let v = o.name@ref; let t = item@move; inspect(v)`",
            "39i" => "pair layer `p: s/i32`, ref view: `let v = s.p@follow@ref; let t = s@move; inspect(v)`",
            "39j" => "pair layer `p: s/i32`, uniq view: the same program",
            "39k" => "activation: `g(s@uniq, s.r@follow@ref)`",
            "39n" => "S1 §2.1: `var s = S(p: t@uniq); let q = s@ref; let e = q.p@follow@ref; t = [4]; inspect(e@follow)`",
            "39l" => "S3 under an exclusive parent: `var p = s@uniq; let v = p.rs@follow@follow@ref; slot@follow = 5; inspect(v)`",
            _ => "39l with `s.rs = other@ref` instead of the write through slot",
        });
        if (id == "39n") // S {o} has var p: uniq/Array<i32> during o; past the shared q, e carries q's Loan on s and s.p's on t
        {
            c.N("var t = [1, 2, 3]").Def("t");
            c.N("var s = S(p: t@uniq)").Def("s").Borrow("Lt", "t", Uniq, "s.p");
            c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q");
            c.N("let e = q.p@follow@ref").Def("e").Borrow("Le", "q.*.p.*", Ref, "e", sharedAt: 1);
            c.N("t = [4]").Assign("t");
            c.N("inspect(e@follow)").A(K.Read, "e.*");
            return c.Error("t = [4]");
        }
        if (id is "39g" or "39h")
        {
            c.N("var item = Item.init(); var other = Item.init()").Def("item", "other");
            c.N("let o = item@objref").Def("o").Borrow("Lo", "item", Ref, "o");
            c.N("let v = o.name@ref").Def("v").Borrow("Lv", "o.*.name", Ref, "v", sharedAt: 1);
            if (id == "39g") c.N("o = other@objref").Assign("o").Borrow("Lo2", "other", Ref, "o");
            else c.N("let t = item@move").Def("t").A(K.Move, "item").Flow("item", "t");
            c.N("inspect(v)").Use("v");
            return id == "39g" ? c.Valid() : c.Error("let t = item@move");
        }
        if (id is "39l" or "39m")
        {
            c.N("var x = 1; var o = 2").Def("x", "o");
            c.N("var slot = x@uniq").Def("slot").Borrow("L1", "x", Uniq, "slot");
            c.N("var s = S3(rs: slot@ref)").Def("s").Borrow("Lo", "slot", Ref, "s.rs");
            c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
            c.N("let v = p.rs@follow@follow@ref").Def("v").Borrow("Lv", "p.*.rs.*.*", Ref, "v", sharedAt: 2);
            if (id == "39l") c.N("slot@follow = 5").A(K.Write, "slot.*");
            else c.N("s.rs = o@ref").Assign("s.rs").Borrow("Lo3", "o", Ref, "s.rs");
            c.N("inspect(v)").Use("v");
            return id == "39l" ? c.Error("slot@follow = 5") : c.Valid();
        }
        bool pairUniq = id == "39j";
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N(pairUniq ? "var s = S(p: x@uniq, n: 0)" : "var s = S(r: x@ref, n: 0)").Def("s").Borrow("Lx", "x", pairUniq, "s.r");
        if (id == "39k")
        {
            c.N("$1 = s@uniq (reserved)").Def("$1").Borrow("L1", "s", Uniq, "$1", reserve: true);
            c.N("$2 = s.r@follow@ref").Def("$2").Borrow("L2", "s.r.*", Ref, "$2", sharedAt: 1);
            c.N("g($1, $2)").Activate("L1").Use("$2");
            return c.Valid();
        }
        if (id is "39c" or "39d")
        {
            c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
            c.N("let v = p.r@follow@ref").Def("v").Borrow("Lv", "p.*.r.*", Ref, "v", sharedAt: 2);
            if (id == "39c") c.N("s.n = 1").Assign("s.n"); else c.N("x = 5").Assign("x");
            c.N("inspect(v)").Use("v");
            return id == "39c" ? c.Valid() : c.Error("x = 5");
        }
        c.N(id is "39i" or "39j" ? "let v = s.p@follow@ref" : "let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: pairUniq ? 0 : 1);
        if (id is "39e" or "39f") c.N("s.r = y@ref").Assign("s.r").Borrow("Ly", "y", Ref, "s.r");
        else c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
        if (id is "39b" or "39f") c.N("x = 5").Assign("x");
        c.N("inspect(v)").Use("v");
        if (id is "39b" or "39f") return c.Error("x = 5");
        return pairUniq ? c.Error("let t = s@move") : c.Valid();
    }

    // ---- Round 10 (prototype round 10 of the chosen 1b design diff) ----
    // G92b (b2) fold (choice §2): on a preparation path a written exclusive borrow the path continues through is not formed;
    // the input has one reservation at its lending point. Holder {var items: Array<i32>; var count: isize}.
    static Case Fold(string id)
    {
        var c = new Case(id, "G92b fold: " + id switch
        {
            "38l" => "`holder@uniq.items.append(holder.count)`: one reservation on `holder.items`, a shared read of `holder.count` in it",
            "38m" => "`let r = holder.count@ref; holder@uniq.items.append(1); inspect(r)` ((b1) would reject it)",
            _ => "`let v = holder.items@ref; holder@uniq.items.append(1); inspect(v)`",
        });
        c.N("var holder = Holder(items: [], count: 0)").Def("holder");
        if (id == "38m") c.N("let r = holder.count@ref").Def("r").Borrow("Lc", "holder.count", Ref, "r");
        if (id == "38n") c.N("let v = holder.items@ref").Def("v").Borrow("Lv", "holder.items", Ref, "v");
        c.N("$ra = TwoPhaseUniq(holder.items) (append receiver; no Ref of holder)").Def("$ra").Borrow("Lr", "holder.items", Uniq, "$ra", reserve: true);
        if (id == "38l") { c.N("$n = holder.count").Def("$n").A(K.Read, "holder.count"); c.N("append($ra, $n)").Activate("Lr").Use("$n"); return c.Valid(); }
        const string call = "append($ra, 1)";
        c.N(call).Activate("Lr");
        c.N(id == "38m" ? "inspect(r)" : "inspect(v)").Use(id == "38m" ? "r" : "v");
        return id == "38m" ? c.Valid() : c.Error(call);
    }

    // Slice index (choice §1; MIR-B finding (ii)): the Index of a Slice handle is a shared borrow
    // Deref, since a Slice element is the handle's referent (SPEC 4.8.1: element references retain the backing and "do not
    // borrow the handle variable"). A range's value carries its backing borrow (R4-M40). G94 copies a Slice receiver into a
    // temp where it is located ($sc); its StorageDead at the end of the statement is a shallow destroy (A3: StorageDead →
    // destroy), encoded as a Write. c.Inline: the same program with the Index as an inline step. UniqSlice has no built-in
    // Index (review N1): its selections are `index`/`indexUniq` calls (42c).
    static Case Slice(string id) { var c = SliceCase(id, inline: false); c.Inline = SliceCase(id, inline: true); return c; }

    static Case SliceCase(string id, bool inline)
    {
        var c = new Case(id, "Slice index: " + id switch
        {
            "42a" => "`var s = arr[..]; let e = s[i]@ref; s = other; arr[0] = 5; inspect(e)`",
            "42b" => "`var s = arr[..]; let e = s[i]@ref; s = other; inspect(e)`",
            "42c" => "UniqSlice (review N1: `index`/`indexUniq` calls, UniqSlice.kimi:36,42): `var u = arr.sliceUniq(); let m = u[i]@uniq; inspect(u[j]); m@follow = 1`",
            _ => "SPEC 4.8.2: `func first<T>(s: Slice<T>) -> ref/T during s.source => return s[0]@ref`",
        });
        string El(string handle, string index) => handle + (inline ? "." : ".*.") + index; // the element Place
        int sh = inline ? 0 : 1;                                                              // a Slice handle's Deref is shared
        switch (id)
        {
            case "42a": case "42b":
                c.N("var arr = [1, 2, 3]; var arr2 = [4]; let i: isize = pick()").Def("arr", "arr2", "i");
                c.N("var s = arr[..]").Def("s").Borrow("La", "arr", Ref, "s");
                c.N("var other = arr2[..]").Def("other").Borrow("Lb", "arr2", Ref, "other");
                c.N("$sc = s (G94: the handle copied where located)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
                c.N("let e = $sc[i]@ref").Def("e").Borrow("Le", El("$sc", "[]"), Ref, "e", sharedAt: sh);
                c.N("StorageDead $sc (end of the statement)").A(K.Write, "$sc").Def("$sc");
                c.N("s = other").Assign("s").A(K.Read, "other").Flow("other", "s");
                if (id == "42a") c.N("arr[0] = 5").Assign("arr.0");
                c.N("inspect(e)").Use("e");
                return id == "42a" ? c.Error("arr[0] = 5") : c.Valid();
            case "42c": // a call lowering, no Slice index: the same nodes under SliceIndexInline
                c.N("var arr = [1, 2, 3]; let i: isize = pick(); let j: isize = pick()").Def("arr", "i", "j");
                c.N("var u = arr.sliceUniq()").Def("u").Borrow("Lu", "arr", Uniq, "u");
                c.N("$r = u@uniq (indexUniq's receiver, reserved)").Def("$r").Borrow("Lr", "u", Uniq, "$r", reserve: true);
                c.N("let m = indexUniq($r, i) (retains its receiver)").Activate("Lr").Def("m").Flow("$r", "m");
                c.N("inspect(u[j]): $q = u@ref (index's receiver)").Def("$q").Borrow("Lq", "u", Ref, "$q");
                c.N("inspect(index($q, j)) (retains its receiver)").Def("$e").Flow("$q", "$e").Use("$e");
                c.N("m@follow = 1").A(K.Write, "m.*");
                return c.Error("inspect(u[j]): $q = u@ref (index's receiver)");
            default:
                c.N("func first<T>(s: Slice<T>)").Param("Ls", "s");
                c.N("$sc = s (G94 copy)").Def("$sc").A(K.Read, "s").Flow("s", "$sc");
                c.N("_0 = $sc[0]@ref").Borrow("Le", El("$sc", "0"), Ref, "$ret", sharedAt: sh);
                c.N("StorageDead $sc (end of the statement)").A(K.Write, "$sc").Def("$sc");
                c.N("return").Goto();
                return c.Valid();
        }
    }

    // Finding 1 checks: the builder's other EndBorrow, a pending reference argument temp ended on a transfer edge
    // (MirBuilder.Control.cs:144-152): an abandoned reservation, an abandoned comparison operand, and 38f's retained
    // reservation whose index argument may break (the earlier iterations' values flowed out of $r through its Call).
    static Case Abandoned(string id)
    {
        var c = new Case(id, "abandoned EndBorrow: " + id switch
        {
            "44a" => "`f(x@uniq, do { if p => return 0; 1 }); inspect(x)` (reservation)",
            "44b" => "`let same = x == do { if p => return 0; y }; x = 3; inspect(same)` (comparison operand)",
            _ => "38f with `cursor = indexUniq(cursor.children, do { if stop() => break; 0 })` (retained reservation in a loop)",
        });
        switch (id)
        {
            case "44a":
                c.N("var x = 1").Def("x");
                c.N("$r = x@uniq (f's argument, reserved)").Def("$r").Borrow("Lr", "x", Uniq, "$r", reserve: true);
                c.N("if p (in the next argument)").Goto("  return 0: EndBorrow $r", "f($r, 1)");
                c.N("  return 0: EndBorrow $r").EndBorrow("$r", guard: false).Goto("exit");
                c.N("f($r, 1)").Activate("Lr");
                c.N("inspect(x)").A(K.Read, "x");
                c.N("exit").Goto();
                return c.Valid();
            case "44b":
                c.N("var x = 1; var y = 2").Def("x", "y");
                c.N("$a = x@ref (comparison operand)").Def("$a").Borrow("La", "x", Ref, "$a");
                c.N("if p (in the right operand)").Goto("  return 0: EndBorrow $a", "$b = y@ref");
                c.N("  return 0: EndBorrow $a").EndBorrow("$a", guard: false).Goto("exit");
                c.N("$b = y@ref").Def("$b").Borrow("Lb", "y", Ref, "$b");
                c.N("let same = eq($a, $b)").Def("same").Use("$a", "$b");
                c.N("x = 3").Assign("x");
                c.N("inspect(same)").Use("same");
                c.N("exit").Goto();
                return c.Valid();
            default:
                c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
                c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
                c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  $r = cursor.children (indexUniq receiver, reserved)", "cursor.value = 1");
                c.N("  $r = cursor.children (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "cursor.*.children", Uniq, "$r", reserve: true);
                c.N("  if stop() (in the index argument)").Goto("  break: EndBorrow $r", "  _t = indexUniq($r, 0) (retains $r)");
                c.N("  break: EndBorrow $r").EndBorrow("$r", guard: false).Goto("cursor.value = 1");
                c.N("  _t = indexUniq($r, 0) (retains $r)").Activate("Lr").Def("_t").Flow("$r", "_t");
                c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
                c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
                return c.Valid();
        }
    }

    // G110 × guard candidates: SPEC 14.8.3's example. A shared-mode Subject is a shared borrow ($s: ref/Packet,
    // MirBuilder.Match.cs:27-28), and each candidate is a Ref of a Place selected under it (:82-84, :104), so the
    // candidate's Ref lies past a shared Deref and, under G110, forms no Loan for err(ended) to find at its EndBorrow.
    // keep(slot: uniq/(ref/Data during s), value: ref/Data during s) stores value into slot's referent.
    static Case GuardExample()
    {
        var c = new Case("43a", "SPEC 14.8.3: `var saved = fallback@ref; match packet .Data(let d) if keep(saved@uniq, d) => (); use(saved)`");
        c.N("var fallback = Data(); var packet: Packet = .Data(Data())").Def("fallback", "packet");
        c.N("var saved = fallback@ref").Def("saved").Borrow("Lf", "fallback", Ref, "saved");
        c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("$a = saved@uniq (reserved)").Def("$a").Borrow("La", "saved", Uniq, "$a", reserve: true);
        c.N("keep($a, d) (stores d into saved)").Activate("La").Through("$a.*", "d");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        c.N("use(saved)").Use("saved");
        return c.Error(end);
    }

    // ---- Round 10 (designer B's cases, renumbered 38->40, 39->41) ----
    // G119, SPEC 15.6.7: during a reservation, its Place, owner and the references it was reached through are protected.
    // H {var inner: uniq/S; var other: i32}. 36h's shape with other writes in the `do` argument (40a-40d); 40e reserves
    // through a reborrow cur2, so cur is not on the route; 40f writes the receiver's route as the Call's destination.
    static Case RouteWrite(string id)
    {
        var c = new Case(id, "G119: " + id switch
        {
            "40a" => "`first = cur.inner.takeWith(do { cur@follow = H(inner: s2, other: 0); 0 })`: writes the owner",
            "40b" => "`first = cur.inner.takeWith(do { cur.inner = s2; 0 })`: writes the reference last dereferenced",
            "40c" => "`first = cur.inner.takeWith(do { cur.other = 5; 0 })`: a sibling Field",
            "40d" => "`first = cur.inner.takeWith(do { inspect(cur@ref); 0 })`: a shared Borrow of the route",
            "40e" => "`var cur2 = cur; first = cur2.inner.takeWith(do { cur = b; 0 })`: cur is not on the route",
            "40f" => "`var node = head; node = node.next(); node.value = 1`, next(self: uniq/Node) -> uniq/Node during self",
            "40g" => "`refs[i]@follow.push(do { refs[j] = s3; 0 })` (a concrete call, no F6): the route through an index",
            _ => "40g with `refs[0]` and `refs[1]`",
        });
        if (id is "40g" or "40h")
        {
            var (ci, cj) = id == "40g" ? ("[]", "[]") : ("0", "1");
            c.N("func f(r1: uniq/S, r2: uniq/S, s3: uniq/S); let i, j = pick()").Param("L1", "r1").Param("L2", "r2").Param("L3", "s3").Def("i", "j");
            c.N("var refs: [2 of uniq/S] = [r1, r2]").Def("refs").Borrow("Lr1", "r1.*", Uniq, "refs.0").Borrow("Lr2", "r2.*", Uniq, "refs.1");
            c.N($"$r = refs[{(ci == "[]" ? "i" : ci)}]@follow (receiver, reserved)").Def("$r").Borrow("Lr", $"refs.{ci}.*", Uniq, "$r", reserve: true);
            c.N("$s = s3 (Reborrow)").Def("$s").Borrow("Ls", "s3.*", Uniq, "$s");
            var w = $"refs[{(cj == "[]" ? "j" : cj)}] = move $s";
            c.N(w).Through($"refs.{cj}", "$s").A(K.Move, "$s");
            c.N("push($r, 0)").Activate("Lr").Use("$r");
            c.N("inspect(refs)").Use("refs");
            return id == "40g" ? c.Error(w) : c.Valid();
        }
        if (id == "40f")
        {
            c.N("func walk(head: uniq/Node)").Param("Lh", "head");
            c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
            c.N("$r = node (receiver, reserved)").Def("$r").Borrow("Lr", "node.*", Uniq, "$r", reserve: true);
            c.N("node = next($r)").Activate("Lr").Assign("node").Flow("$r", "node");
            c.N("node.value = 1").A(K.Write, "node.*.value");
            return c.Valid();
        }
        c.N("func f(a: uniq/H, b: uniq/H, s2: uniq/S)").Param("La", "a").Param("Lb", "b").Param("Ls2", "s2");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        var holder = "cur";
        if (id == "40e") { c.N("var cur2 = cur").Def("cur2").Borrow("Lc2", "cur.*", Uniq, "cur2"); holder = "cur2"; }
        c.N($"$r1 = {holder}.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", holder + ".*.inner.*", Uniq, "$r1", reserve: true);
        string write = null;
        switch (id)
        {
            case "40a":
                c.N("$h = H(inner: s2, other: 0)").Def("$h").Borrow("Lh", "s2.*", Uniq, "$h");
                write = "cur@follow = move $h"; c.N(write).Through("cur.*", "$h").A(K.Move, "$h"); break;
            case "40b":
                c.N("$s = s2 (Reborrow)").Def("$s").Borrow("Ls", "s2.*", Uniq, "$s");
                write = "cur.inner = move $s"; c.N(write).Through("cur.*.inner", "$s").A(K.Move, "$s"); break;
            case "40c": c.N("cur.other = 5").A(K.Write, "cur.*.other"); break;
            case "40d": c.N("$q = cur@ref").Def("$q").Borrow("Lq", "cur", Ref, "$q"); c.N("inspect($q)").Use("$q"); break;
            default: c.N("do { cur = b; 0 } (argument)").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur"); break;
        }
        c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", holder + ".*.inner.*", "first", "Lr1");
        c.N("return first").Flow("first", "$ret");
        return write != null ? c.Error(write) : c.Valid();
    }

    // G110, SPEC 15.6.3 (S1): a Borrow past a shared Deref forms no Loan; it carries the Loans of the reference of its
    // last shared Deref and of those it dereferences after it. S {var r: ref/i32}; S2 {var p: uniq/Array<i32>};
    // S3 {var r: ref/i32; var n: i32}; H {var view: objref/Item}.
    // Round 10: each declaration has its own node before the Borrow of the declared variable (B combined them; PrefixKill,
    // which also ends direct Loans, then ended a Loan at its own issue because the same node declared its Place).
    static Case SharedPast(string id)
    {
        var c = new Case(id, "G110: " + id switch
        {
            "41a" => "`let v = s.r@follow@ref; let t = s@move; inspect(v)` (S1 §9)",
            "41b" => "41a, then `x = 5` before `inspect(v)`",
            "41c" => "`var p = s@uniq; let v = p.r@follow@ref; let t = s@move; inspect(v)` (shared layer under an exclusive parent)",
            "41d" => "`var p = s@uniq; let v = p.r@follow@ref; p.r = y@ref; x = 5; inspect(v); inspect(p)`",
            "41e" => "41d without `x = 5`: the stored shared reference is written while v lives",
            "41f" => "`var r = x@ref; let v = r@follow@ref; let w = r@uniq; w@follow = y@ref; inspect(v)`",
            "41g" => "41f, then `x = 5` before `inspect(v)`",
            "41h" => "`g(s@uniq, s.r@follow@ref)`",
            "41i" => "`g(x@uniq, s.r@follow@ref)` with `s.r = x@ref`",
            "41j" => "S1 §2.1: `let q = s@ref; let e = q.p@follow@ref; t = [4]; inspect(e@follow)`",
            "41k" => "41j with `s.p = t2@uniq` instead of `t = [4]`",
            "41l" => "objref: `let f = h.view@follow.count@ref; let g = h@move; inspect(f)`",
            "41m" => "objref: `let f = h.view@follow.count@ref; item.count = 5; inspect(f)`",
            "41n" => "self-reference: `s.r = s.n@ref; let v = s.r@follow@ref; s.n = 5; inspect(v)`",
            "41o" => "self-reference at formation: `s.r = s.n@ref; let p = s@uniq; p.n = 5; inspect(p.r@follow)`",
            "41q" => "self-reference, shared: `s.r = s.n@ref; let q = s@ref; inspect(q.r@follow)`",
            _ => "loop: `var keep = z@ref; while c { keep = r@follow@ref; r = y@ref }; x = 5; inspect(keep)`",
        });
        switch (id)
        {
            case "41a": case "41b":
                c.N("var x = 1").Def("x"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N("let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: 1);
                c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
                if (id == "41b") { c.N("x = 5").Assign("x"); c.N("inspect(v)").Use("v"); return c.Error("x = 5"); }
                c.N("inspect(v)").Use("v"); return c.Valid();
            case "41c": case "41d": case "41e":
                c.N("var x = 1; var y = 2").Def("x", "y"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N("var p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
                c.N("let v = p.r@follow@ref").Def("v").Borrow("Lv", "p.*.r.*", Ref, "v", sharedAt: 2);
                if (id == "41c") { c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t"); c.N("inspect(v)").Use("v"); return c.Valid(); }
                c.N("$t = y@ref").Def("$t").Borrow("Ly", "y", Ref, "$t");
                c.N("p.r = $t").Through("p.*.r", "$t");
                if (id == "41d") c.N("x = 5").Assign("x");
                c.N("inspect(v)").Use("v"); c.N("inspect(p)").Use("p");
                return id == "41d" ? c.Error("x = 5") : c.Valid();
            case "41f": case "41g":
                c.N("var x = 1; var y = 2").Def("x", "y"); c.N("var r = x@ref").Def("r").Borrow("Lx", "x", Ref, "r");
                c.N("let v = r@follow@ref").Def("v").Borrow("Lv", "r.*", Ref, "v", sharedAt: 1);
                c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
                c.N("$t = y@ref").Def("$t").Borrow("Ly", "y", Ref, "$t");
                c.N("w@follow = $t").Through("w.*", "$t");
                if (id == "41g") { c.N("x = 5").Assign("x"); c.N("inspect(v)").Use("v"); return c.Error("x = 5"); }
                c.N("inspect(v)").Use("v"); return c.Valid();
            case "41h": case "41i":
                c.N("var x = 1").Def("x"); c.N("var s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
                c.N(id == "41h" ? "$1 = s@uniq (reserved)" : "$1 = x@uniq (reserved)").Def("$1").Borrow("L1", id == "41h" ? "s" : "x", Uniq, "$1", reserve: true);
                c.N("$2 = s.r@follow@ref").Def("$2").Borrow("L2", "s.r.*", Ref, "$2", sharedAt: 1);
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return id == "41h" ? c.Valid() : c.Error("g($1, $2)");
            case "41j": case "41k":
                c.N("var t = [1, 2, 3]; var t2 = [5]").Def("t", "t2"); c.N("var s = S2(p: t@uniq)").Def("s").Borrow("Lt", "t", Uniq, "s.p");
                c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q");
                c.N("let e = q.p@follow@ref").Def("e").Borrow("Le", "q.*.p.*", Ref, "e", sharedAt: 1);
                var w = id == "41j" ? "t = [4]" : "s.p = t2@uniq";
                if (id == "41j") c.N(w).Assign("t"); else c.N(w).Assign("s.p").Borrow("Lt2", "t2", Uniq, "s.p");
                c.N("inspect(e@follow)").A(K.Read, "e.*");
                return c.Error(w);
            case "41l": case "41m":
                c.N("var item = makeItem()").Def("item"); c.N("var h = H(view: item@objref)").Def("h").Borrow("Li", "item", Ref, "h.view");
                c.N("let f = h.view@follow.count@ref").Def("f").Borrow("Lf", "h.view.*.count", Ref, "f", sharedAt: 1);
                if (id == "41l") { c.N("let g = h@move").Def("g").A(K.Move, "h").Flow("h", "g"); c.N("inspect(f)").Use("f"); return c.Valid(); }
                c.N("item.count = 5").A(K.Write, "item.count"); c.N("inspect(f)").Use("f");
                return c.Error("item.count = 5");
            case "41n": case "41o": case "41q":
                c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
                c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
                if (id == "41n")
                {
                    c.N("let v = s.r@follow@ref").Def("v").Borrow("Lv", "s.r.*", Ref, "v", sharedAt: 1);
                    c.N("s.n = 5").Assign("s.n"); c.N("inspect(v)").Use("v");
                    return c.Error("s.n = 5");
                }
                if (id == "41q") { c.N("let q = s@ref").Def("q").Borrow("Lq", "s", Ref, "q"); c.N("inspect(q.r@follow)").A(K.Read, "q.*.r.*"); return c.Valid(); }
                c.N("let p = s@uniq").Def("p").Borrow("Lp", "s", Uniq, "p");
                c.N("p.n = 5").A(K.Write, "p.*.n"); c.N("inspect(p.r@follow)").A(K.Read, "p.*.r.*");
                return c.Error("let p = s@uniq");
            default:
                c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
                c.N("var r = x@ref; var keep = z@ref").Def("r", "keep").Borrow("Lx", "x", Ref, "r").Borrow("Lz", "z", Ref, "keep");
                c.N("while c").Goto("  keep = r@follow@ref", "x = 5");
                c.N("  keep = r@follow@ref").Assign("keep").Borrow("Lk", "r.*", Ref, "keep", sharedAt: 1);
                c.N("  r = y@ref").Assign("r").Borrow("Ly", "y", Ref, "r").Goto("while c");
                c.N("x = 5").Assign("x"); c.N("inspect(keep)").Use("keep");
                return c.Error("x = 5");
        }
    }


    // ---- Case 23a review (independent review of the case 23a proposal, 2026-10-17): copied unchanged; 92g/93g added ----
    // S {var p: uniq/i32}: 29a in the reverse order, and through a retaining receiver or argument (proposal §6 row 2).
    static Case CrossingOrder(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "reverse" => "29a reversed: `w = q@follow@ref; c = q.p@follow@uniq; q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            "recvAfter" => "`c = q.p@follow@uniq; w = q.view()` (shared receiver retained); `q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            "recvBefore" => "`w = q.view()` (shared receiver retained); `c = q.p@follow@uniq; q = o@uniq; c@follow = 5; inspect(w.p@follow)`",
            _ => "`c = q.p@follow@uniq; w = pass(q)` (Reborrow at a uniq parameter, retained); `q = o@uniq; w.p@follow = 9; c@follow = 5`",
        });
        c.N("var t = 1; var u = 2").Def("t", "u");
        c.N("var s = S(p: t@uniq); var o = S(p: u@uniq)").Def("s", "o").Borrow("Lt", "t", Uniq, "s.p").Borrow("Lu", "u", Uniq, "o.p");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        string[] err;
        switch (kind)
        {
            case "reverse":
                c.N("let w = q@follow@ref").Def("w").Borrow("Lw", "q.*", Ref, "w");
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                err = ["let c = q.p@follow@uniq"]; break;
            case "recvAfter":
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                c.N("$r = q (receiver of view, shared)").Def("$r").Borrow("Lr", "q.*", Ref, "$r");
                c.N("let w = view($r) (retains its receiver)").Def("w").Flow("$r", "w");
                err = ["$r = q (receiver of view, shared)"]; break;
            case "recvBefore":
                c.N("$r = q (receiver of view, shared)").Def("$r").Borrow("Lr", "q.*", Ref, "$r");
                c.N("let w = view($r) (retains its receiver)").Def("w").Flow("$r", "w");
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                err = ["let c = q.p@follow@uniq"]; break;
            default:
                c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
                c.N("$a = q (Reborrow at a uniq parameter, reserved)").Def("$a").Borrow("La", "q.*", Uniq, "$a", reserve: true);
                c.N("let w = pass($a) (retains it)").Activate("La").Def("w").Flow("$a", "w");
                err = ["$a = q (Reborrow at a uniq parameter, reserved)", "let w = pass($a) (retains it)"]; break;
        }
        c.N("q = o@uniq").Assign("q").Borrow("Lo", "o", Uniq, "q");
        if (kind == "arg") { c.N("w.p@follow = 9").A(K.Write, "w.*.p.*"); c.N("c@follow = 5").A(K.Write, "c.*"); }
        else { c.N("c@follow = 5").A(K.Write, "c.*"); c.N("inspect(w.p@follow)").A(K.Read, "w.*.p.*"); }
        return c.Error(err);
    }

    // S2 {var a: uniq/i32, var b: uniq/i32}: two children of q share the first base q and end together (§6 row 5).
    static Case Siblings(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "both" => "`c1 = q.a@follow@uniq; c2 = q.b@follow@uniq; q = t@uniq; q.a@follow = 7; q.b@follow = 8; c1@follow = 1; c2@follow = 2`",
            "old" => "54 without the writes through q; `s.a@follow = 9` (old route to c1's x) before `c1@follow = 1`",
            _ => "conflicting siblings: `c1 = q.a@follow@uniq; c2 = q.a@follow@uniq; q = t@uniq; c1@follow = 1; c2@follow = 2`",
        });
        c.N("var x = 1; var y = 2; var z = 3; var w = 4").Def("x", "y", "z", "w");
        c.N("var s = S2(a: x@uniq, b: y@uniq); var t = S2(a: z@uniq, b: w@uniq)").Def("s", "t")
            .Borrow("Lx", "x", Uniq, "s.a").Borrow("Ly", "y", Uniq, "s.b").Borrow("Lz", "z", Uniq, "t.a").Borrow("Lw", "w", Uniq, "t.b");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        c.N("let c1 = q.a@follow@uniq").Def("c1").Borrow("L1", "q.*.a.*", Uniq, "c1");
        var second = kind == "conflict" ? "let c2 = q.a@follow@uniq" : "let c2 = q.b@follow@uniq";
        c.N(second).Def("c2").Borrow("L2", kind == "conflict" ? "q.*.a.*" : "q.*.b.*", Uniq, "c2");
        c.N("q = t@uniq").Assign("q").Borrow("Lt", "t", Uniq, "q");
        if (kind == "both") { c.N("q.a@follow = 7").A(K.Write, "q.*.a.*"); c.N("q.b@follow = 8").A(K.Write, "q.*.b.*"); }
        if (kind == "old") c.N("s.a@follow = 9").A(K.Write, "s.a.*");
        c.N("c1@follow = 1").A(K.Write, "c1.*");
        c.N("c2@follow = 2").A(K.Write, "c2.*");
        if (kind == "both") { c.ALoss = "23a family (siblings past the first base)"; return c.Valid(); }
        return c.Error(kind == "old" ? "s.a@follow = 9" : second);
    }

    // 23a's replacement on one branch, on both, conditionally inside a loop, or before and inside a loop (§6 row 6).
    static Case Conditional(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "one" => "`c = rr@follow@follow@uniq; if cond => rr = slot2@uniq`; `rr@follow@follow = 7; c@follow = 1`",
            "both" => "`c = rr@follow@follow@uniq; if cond => rr = slot2@uniq else => rr = slot3@uniq`; `rr@follow@follow = 7; c@follow = 1`",
            "loopOne" => "`c = …; while cond: c@follow = 1; if p => rr = slot2@uniq`; after the loop `rr@follow@follow = 7; c@follow = 2`",
            _ => "`c = …; rr = slot2@uniq; while cond: rr@follow@follow = 7; c@follow = 1; rr = slot3@uniq`; `c@follow = 2`",
        });
        c.N("var x = 1; var z = 3; var v = 4").Def("x", "z", "v");
        c.N("var slot = x@uniq; var slot2 = z@uniq; var slot3 = v@uniq").Def("slot", "slot2", "slot3")
            .Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2").Borrow("Lv", "v", Uniq, "slot3");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        const string w = "rr@follow@follow = 7";
        switch (kind)
        {
            case "one":
                c.N("if cond").Goto("  rr = slot2@uniq", w);
                c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error(w);
            case "both":
                c.N("if cond").Goto("  rr = slot2@uniq", "  rr = slot3@uniq");
                c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto(w);
                c.N("  rr = slot3@uniq").Assign("rr").Borrow("Lo3", "slot3", Uniq, "rr");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 1").A(K.Write, "c.*");
                c.ALoss = "23a family (replacement on both branches)"; return c.Valid();
            case "loopOne":
                c.N("while cond").Goto("  c@follow = 1", w);
                c.N("  c@follow = 1").A(K.Write, "c.*");
                c.N("  if p").Goto("    rr = slot2@uniq", "while cond");
                c.N("    rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto("while cond");
                c.N(w).A(K.Write, "rr.*.*"); c.N("c@follow = 2").A(K.Write, "c.*");
                return c.Error(w);
            default:
                c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N("while cond").Goto("  " + w, "c@follow = 2");
                c.N("  " + w).A(K.Write, "rr.*.*");
                c.N("  c@follow = 1").A(K.Write, "c.*");
                c.N("  rr = slot3@uniq").Assign("rr").Borrow("Lo3", "slot3", Uniq, "rr").Goto("while cond");
                c.N("c@follow = 2").A(K.Write, "c.*");
                c.ALoss = "23a family (replacement before and inside a loop)"; return c.Valid();
        }
    }

    // A value from an earlier iteration (keep) is live across the back edge; rr is re-pointed at slot2 every iteration,
    // so from the third iteration c and keep would both reach z (§6 row 6).
    static Case BackEdge(string id)
    {
        var c = new Case(id, "loop: `c = rr@follow@follow@uniq; keep@follow = 1; keep = c; rr = slot2@uniq`");
        c.N("var x = 1; var z = 3; var v = 0").Def("x", "z", "v");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq; var keep = v@uniq").Def("rr", "keep").Borrow("Lo", "slot", Uniq, "rr").Borrow("Lv", "v", Uniq, "keep");
        c.N("while cond").Goto("  let c = rr@follow@follow@uniq", "exit");
        c.N("  let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("  keep@follow = 1").A(K.Write, "keep.*");
        c.N("  keep = c").Assign("keep").Flow("c", "keep");
        c.N("  rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr").Goto("while cond");
        c.N("exit").Goto();
        return c.Error("  rr = slot2@uniq");
    }

    // 23a's child, then the outer holder is Moved into a call, exchanged, swapped, replaced, or replaced through rr (§6 row 8).
    static Case Transfer2(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "move" => "`c = rr@follow@follow@uniq; stash(rr@move)` (keeps and uses it); `rr = slot2@uniq; c@follow = 1`",
            "exchange" => "`c = rr@follow@follow@uniq; old = exchange(rr@uniq, with: slot2@uniq); old@follow@follow = 7; c@follow = 1`",
            "swap" => "`c = rr@follow@follow@uniq; swap(rr@uniq, rr2@uniq); rr2@follow@follow = 7; c@follow = 1`",
            "replace" => "`c = rr@follow@follow@uniq; replace(rr@uniq, with: slot2@uniq); rr@follow@follow = 7; c@follow = 1`",
            _ => "`c = rr@follow@follow@uniq; replace(rr@follow@uniq, with: z@uniq)` (target behind a Deref); `rr@follow@follow = 7; c@follow = 1` (rustc accepts)",
        });
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        if (kind == "swap") c.N("var rr2 = slot2@uniq").Def("rr2").Borrow("Lo2", "slot2", Uniq, "rr2");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        switch (kind)
        {
            case "move":
                c.N("stash(rr@move): Move rr").Def("$v").A(K.Move, "rr").Flow("rr", "$v");
                c.N("stash(rr@move): callee keeps v").A(K.UBorrow, "$v.*.*").Flow("$v", "^stash");
                c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("stash(rr@move): Move rr");
            case "exchange":
                c.N("$t = rr@uniq (reserved)").Def("$t").Borrow("Lr", "rr", Uniq, "$t", reserve: true);
                c.N("$u = slot2@uniq (reserved)").Def("$u").Borrow("Lu", "slot2", Uniq, "$u", reserve: true);
                c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Lu").Def("old").Flow("rr", "old").Exchange("rr").Flow("$u", "rr");
                c.N("old@follow@follow = 7").A(K.Write, "old.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("$t = rr@uniq (reserved)", "let old = exchange($t, with: $u)");
            case "swap":
                c.N("$a = rr@uniq (reserved)").Def("$a").Borrow("La", "rr", Uniq, "$a", reserve: true);
                c.N("$b = rr2@uniq (reserved)").Def("$b").Borrow("Lb", "rr2", Uniq, "$b", reserve: true);
                c.N("swap($a, $b)").Activate("La").Activate("Lb").Flow("rr", "rr2").Flow("rr2", "rr").Exchange("rr").Exchange("rr2");
                c.N("rr2@follow@follow = 7").A(K.Write, "rr2.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("$a = rr@uniq (reserved)", "swap($a, $b)");
            case "replace":
                c.N("$t = rr@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "rr", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$u = slot2@uniq (reserved)").Def("$u").Borrow("Lu", "slot2", Uniq, "$u", reserve: true);
                c.N("replace($t, with: $u)").Activate("Lr").Activate("Lu").Def("rr").Flow("$u", "rr");
                c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                c.ALoss = "23a family (`replace` of the outer holder)"; return c.Valid();
            default:
                c.N("$t = rr@follow@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "rr.*", Uniq, "$t", reserve: true, inPlace: true);
                c.N("$u = z@uniq (reserved)").Def("$u").Borrow("Lu2", "z", Uniq, "$u", reserve: true);
                c.N("replace($t, with: $u)").Activate("Lr").Activate("Lu2").Through("rr.*", "$u");
                c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
                c.N("c@follow = 1").A(K.Write, "c.*");
                return c.Error("rr@follow@follow = 7"); // both SPEC texts: a target behind a Deref is not a Move Path
        }
    }

    // [2 of uniq/uniq/i32] and [2 of [2 of uniq/uniq/i32]]: the matcher on constant and runtime indexes (§6 row 9).
    static Case IndexMatch(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "same" => "`c = arr[0]@follow@follow@uniq; arr[0] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1` (rustc rejects)",
            "other" => "`c = arr[0]@follow@follow@uniq; arr[1] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1`",
            "runtimeWrite" => "`c = arr[0]@follow@follow@uniq; arr[j] = s3@uniq; arr[0]@follow@follow = 7; c@follow = 1`",
            "runtimeBoth" => "`c = arr[i]@follow@follow@uniq; arr[j] = s3@uniq; arr[i]@follow@follow = 7; c@follow = 1`",
            "nestedPrefix" => "`c = m[0][i]@follow@follow@uniq; m[0] = [s5@uniq, s6@uniq]; m[0][i]@follow@follow = 7; c@follow = 1`",
            _ => "`c = m[i][0]@follow@follow@uniq; m[0] = [s5@uniq, s6@uniq]; m[i][0]@follow@follow = 7; c@follow = 1`",
        });
        c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
        c.N("var s1 = x@uniq; var s2 = y@uniq; var s3 = z@uniq").Def("s1", "s2", "s3").Borrow("Lx", "x", Uniq, "s1").Borrow("Ly", "y", Uniq, "s2").Borrow("Lz", "z", Uniq, "s3");
        string place, src;
        if (kind.StartsWith("nested"))
        {
            c.N("var s4 = …; var s5 = …; var s6 = …").Def("s4", "s5", "s6");
            c.N("var m: [2 of [2 of uniq/uniq/i32]] = [[s1@uniq, s2@uniq], [s3@uniq, s4@uniq]]").Def("m")
                .Borrow("M00", "s1", Uniq, "m.0.0").Borrow("M01", "s2", Uniq, "m.0.1").Borrow("M10", "s3", Uniq, "m.1.0").Borrow("M11", "s4", Uniq, "m.1.1");
            (place, src) = kind == "nestedPrefix" ? ("m.0.[]", "m[0][i]") : ("m.[].0", "m[i][0]");
            c.N($"let c = {src}@follow@follow@uniq").Def("c").Borrow("L2", place + ".*.*", Uniq, "c");
            c.N("m[0] = [s5@uniq, s6@uniq]").Assign("m.0").Borrow("M5", "s5", Uniq, "m.0.0").Borrow("M6", "s6", Uniq, "m.0.1");
        }
        else
        {
            c.N("var arr: [2 of uniq/uniq/i32] = [s1@uniq, s2@uniq]").Def("arr").Borrow("A0", "s1", Uniq, "arr.0").Borrow("A1", "s2", Uniq, "arr.1");
            (place, src) = kind == "runtimeBoth" ? ("arr.[]", "arr[i]") : ("arr.0", "arr[0]");
            c.N($"let c = {src}@follow@follow@uniq").Def("c").Borrow("L2", place + ".*.*", Uniq, "c");
            if (kind == "same") c.N("arr[0] = s3@uniq").Assign("arr.0").Borrow("A3", "s3", Uniq, "arr.0");
            else if (kind == "other") c.N("arr[1] = s3@uniq").Assign("arr.1").Borrow("A3", "s3", Uniq, "arr.1");
            else { c.N("$t = s3@uniq").Def("$t").Borrow("A3", "s3", Uniq, "$t"); c.N("arr[j] = $t").Through("arr.[]", "$t"); }
        }
        var write = $"{src}@follow@follow = 7";
        c.N(write).A(K.Write, place + ".*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (kind is "same" or "nestedPrefix") { c.ALoss = "23a family (an exact constant-index or Field prefix)"; return c.Valid(); }
        return c.Error(write);
    }

    // P {var a: uniq/uniq/i32, var b: uniq/uniq/i32}: the matcher on Fields.
    static Case FieldMatch(string id, bool same)
    {
        var w = same ? "p.a = sz@uniq" : "p.b = sz@uniq";
        var c = new Case(id, $"`c = p.a@follow@follow@uniq; {w}; p.a@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var sa = x@uniq; var sb = y@uniq; var sz = z@uniq").Def("sa", "sb", "sz").Borrow("Lx", "x", Uniq, "sa").Borrow("Ly", "y", Uniq, "sb").Borrow("Lz", "z", Uniq, "sz");
        c.N("var p = P(a: sa@uniq, b: sb@uniq)").Def("p").Borrow("La", "sa", Uniq, "p.a").Borrow("Lb", "sb", Uniq, "p.b");
        c.N("let c = p.a@follow@follow@uniq").Def("c").Borrow("L2", "p.a.*.*", Uniq, "c");
        c.N(w).Assign(same ? "p.a" : "p.b").Borrow("Lp", "sz", Uniq, same ? "p.a" : "p.b");
        c.N("p.a@follow@follow = 7").A(K.Write, "p.a.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (same) { c.ALoss = "23a family (an exact constant-index or Field prefix)"; return c.Valid(); }
        return c.Error("p.a@follow@follow = 7");
    }

    // H {var r: uniq/uniq/i32}: own steps (`payload` of obj and rc; A3 §3: handle storage) before the first borrow Deref.
    // rc payloads are shared-only, so the child is `ref`; 80's H has a drop that writes through self.r (§16.3.3).
    // 75 is an error by SPEC's letter (owner, 2026-10-18; PLAN G147 (6)): a Loan ends only where a Move Path is destroyed in place
    // (§15.6.3), and the payload is an own step that no Move Path takes (§15.1.3, §15.6.2), so replacing `h@follow.r` ends nothing.
    static Case OwnStep(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "objField" => "`h: obj/H`; `c = h@follow.r@follow@follow@uniq; h@follow.r = slot2@uniq; h@follow.r@follow@follow = 7; c@follow = 1`",
            "objWhole" => "`h: obj/H`; `c = h@follow.r@follow@follow@uniq; h = obj(H(r: slot2@uniq)); h@follow.r@follow@follow = 7; c@follow = 1`",
            "objOld" => "`h: obj/H`; `c = …@uniq; h = obj(H(r: slot2@uniq)); slot@follow = 5; c@follow = 1`",
            "rcClone" => "`h: rc/H; h2 = h` (clone); `c = h@follow.r@follow@follow@ref; h = rc(H(r: slot2@uniq)); inspect(h2@follow.r@follow@follow); inspect(c@follow)`",
            "rcOld" => "`h: rc/H`; `c = h@follow.r@follow@follow@ref; h = rc(H(r: slot2@uniq)); slot@follow = 5; inspect(c@follow)`",
            _ => "`h: rc/H`, H's drop writes through self.r; `h2 = h; c = …@ref; h = rc(H(r: slot2@uniq))` (the release may be final); `inspect(c@follow)`",
        });
        bool rc = kind.StartsWith("rc");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N($"var h = {(rc ? "rc" : "obj")}(H(r: slot@uniq))").Def("h").Borrow("Lo", "slot", Uniq, "h.payload.r");
        if (kind is "rcClone" or "rcDrop") c.N("var h2 = h (clone)").Def("h2").A(K.Read, "h").Flow("h", "h2");
        c.N($"let c = h@follow.r@follow@follow@{(rc ? "ref" : "uniq")}").Def("c").Borrow("L2", "h.payload.r.*.*", !rc, "c");
        string err = null;
        switch (kind)
        {
            case "objField":
                c.N("h@follow.r = slot2@uniq").Assign("h.payload.r").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                err = "h@follow.r@follow@follow = 7"; c.N(err).A(K.Write, "h.payload.r.*.*"); break;
            case "objWhole":
                c.N("h = obj(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("h@follow.r@follow@follow = 7").A(K.Write, "h.payload.r.*.*"); break;
            case "objOld": case "rcOld":
                c.N($"h = {(rc ? "rc" : "obj")}(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                err = "slot@follow = 5"; c.N(err).A(K.Write, "slot.*"); break;
            case "rcClone":
                c.N("h = rc(H(r: slot2@uniq))").Assign("h").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("inspect(h2@follow.r@follow@follow)").A(K.Read, "h2.payload.r.*.*"); break;
            default:
                err = "h = rc(H(r: slot2@uniq)) (the old release may be final: drop writes self.r@follow@follow)";
                c.N(err).Assign("h").Drop("h").A(K.Write, "h.payload.r.*.*").Borrow("Lo2", "slot2", Uniq, "h.payload.r");
                c.N("inspect(h2)").Use("h2"); break;
        }
        c.N(rc ? "inspect(c@follow)" : "c@follow = 1").A(rc ? K.Read : K.Write, "c.*");
        if (err != null) return c.Error(err);
        if (kind != "rcClone") c.ALoss = "23a family (own steps before the first borrow Deref)";
        return c.Valid();
    }

    // 23b with the child stored into the caller's storage (E) before the outer holder is replaced.
    static Case EscapedChild()
    {
        var c = new Case("81", "`q: uniq/(uniq/i32), out: uniq/(uniq/i32)`; `q2 = q; c = q2@follow@follow@uniq; out@follow = c@move; q2 = s2@uniq; q@follow@follow = 5`");
        c.N("func f(q: uniq/(uniq/i32), out: uniq/(uniq/i32))").Param("Lq", "q").Param("Lout", "out");
        c.N("var o = 5; var s2 = o@uniq").Def("o", "s2").Borrow("Ls2", "o", Uniq, "s2");
        c.N("var q2 = q").Def("q2").Borrow("Lq2", "q.*", Uniq, "q2");
        c.N("let c = q2@follow@follow@uniq").Def("c").Borrow("L2", "q2.*.*", Uniq, "c");
        c.N("out@follow = c@move").A(K.Move, "c").Through("out.*", "c");
        c.N("q2 = s2@uniq").Assign("q2").Borrow("Lq3", "s2", Uniq, "q2");
        c.N("q@follow@follow = 5").A(K.Write, "q.*.*");
        return c.Error("q@follow@follow = 5");
    }

    // ll2 with a stride of two links (three borrow Derefs): 35a's shape.
    static Case Stride2()
    {
        var c = new Case("82", "ll2 stride 2: `node = node.next…next…; b = node.value@uniq; node = node.next…next…; b@follow = 1`");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("_t = node.next.some@follow.next.some@follow@uniq").Def("_t").Borrow("Ln1", "node.*.next.some.*.next.some.*", Uniq, "_t");
        c.N("node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
        c.N("let b = node.value@uniq").Def("b").Borrow("Lb", "node.*.value", Uniq, "b");
        c.N("_t2 = node.next.some@follow.next.some@follow@uniq").Def("_t2").Borrow("Ln2", "node.*.next.some.*.next.some.*", Uniq, "_t2");
        c.N("node = move _t2").Assign("node").A(K.Move, "_t2").Flow("_t2", "node");
        c.N("b@follow = 1").A(K.Write, "b.*");
        c.ALoss = "23a family (stride-2 walk)"; return c.Valid();
    }

    // The direct form: the Loan is issued into its own first base, ended at once (19a), and only parents remain.
    static Case DirectStep()
    {
        var c = new Case("83", "`node = node.next…next…@uniq` (issued into its own first base); `head@follow…value = 9` while node is live");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("node = node.next.some@follow.next.some@follow@uniq").Assign("node").Borrow("Ln", "node.*.next.some.*.next.some.*", Uniq, "node");
        const string w = "head@follow.next.some@follow.next.some@follow.value = 9";
        c.N(w).A(K.Write, "head.*.next.some.*.next.some.*.value");
        c.N("node.value = 1").A(K.Write, "node.*.value");
        return c.Error(w);
    }

    // 36h through two borrow Derefs: SPEC 15.6.7 rejects the write in the argument (G119). With the receiver retained,
    // design A rejects later only because the reservation's Loan never ends; the first-dereference base ends it there.
    static Case ReservedRoute(string id, bool retained)
    {
        var c = new Case(id, "`res = rr@follow@follow.keepWith(do { rr = slot2@uniq; 0 })`" + (retained ? " (retains the receiver); `rr@follow@follow = 7; inspect(res@follow)`" : "; `rr@follow@follow = 7`"));
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("$r = rr@follow@follow (receiver, reserved)").Def("$r").Borrow("Lr", "rr.*.*", Uniq, "$r", reserve: true);
        const string arg = "do { rr = slot2@uniq; 0 } (argument)";
        c.N(arg).Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        var call = c.N("let res = keepWith($r, …)").Activate("Lr").Def("res");
        if (retained) call.Flow("$r", "res");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        if (retained) c.N("inspect(res@follow)").A(K.Read, "res.*");
        return c.Error(arg);
    }

    // A guard stores a candidate-derived reference and replaces the subject's outer holder (SPEC 14.8.3: a value that
    // depends on g cannot leave the guard; A3 encodes g's end as EndBorrow + err(ended)). 92: the candidate is reached
    // through the internal Subject Place $s (SPEC 15.1.6); 93: a lowering that names the candidate through rr.
    static Case Guard(string id, bool viaSubject, bool g110 = false)
    {
        var c = new Case(id, "`match rr@follow@follow .Data(let d) if (do { saved = d@follow@ref; rr = slot2@uniq; true }) => ()`; `inspect(saved)`"
            + (viaSubject ? " (candidate through the Subject Place $s)" : " (candidate Loan named through rr)")
            + (g110 ? "; round-10 step classes: $s and d are shared references, so the Refs through them are past a shared Deref (G110)" : ""));
        c.N("var x: E = .Data(n: 1); var z: E = .Empty; var other = 0").Def("x", "z", "other");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq; var saved = other@ref").Def("rr", "saved").Borrow("Lo", "slot", Uniq, "rr").Borrow("Lsv", "other", Ref, "saved");
        if (viaSubject)
        {
            c.N("$s = rr@follow@follow (Subject Place, shared)").Def("$s").Borrow("Lsub", "rr.*.*", Ref, "$s");
            c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: g110 ? 1 : 0);
        }
        else c.N("d = rr@follow@follow.data (candidate)").Def("d").Borrow("Ld", "rr.*.*.data", Ref, "d");
        c.N("guard: saved = d@follow@ref").Assign("saved").Borrow("Ld2", "d.*", Ref, "saved", sharedAt: g110 ? 1 : 0);
        c.N("guard: rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        c.N("inspect(saved)").Use("saved");
        return c.Error(end);
    }

    // After 23a's outer end: a child of c, a write through the old slot, or a reborrow of it (§6 row 4, §3.3).
    static Case AfterOuterEnd(string id, string kind)
    {
        var c = new Case(id, "23a, then " + (kind == "grandchild" ? "`d = c@follow@uniq; c@follow = 1; d@follow = 2`"
            : kind == "oldSlot" ? "`slot@follow = 5; c@follow = 1` (rustc E0506)" : "`t = slot@follow@uniq; t@follow = 5; c@follow = 1`"));
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        if (kind == "grandchild")
        {
            c.N("let d = c@follow@uniq").Def("d").Borrow("Ld", "c.*", Uniq, "d");
            c.N("c@follow = 1").A(K.Write, "c.*");
            c.N("d@follow = 2").A(K.Write, "d.*");
            return c.Error("c@follow = 1");
        }
        if (kind == "reborrowSlot")
        {
            c.N("let t = slot@follow@uniq").Def("t").Borrow("Lt", "slot.*", Uniq, "t");
            c.N("t@follow = 5").A(K.Write, "t.*");
            c.N("c@follow = 1").A(K.Write, "c.*");
            return c.Error("let t = slot@follow@uniq");
        }
        c.N("slot@follow = 5").A(K.Write, "slot.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error("slot@follow = 5");
    }

    // Array<uniq/i32>: the element reference lives in a temporary first base that dies; c keeps the receiver Loan.
    static Case TempBase()
    {
        var c = new Case("88", "`Array<uniq/i32>`: `$e = indexUniq(arr, i); c = $e@follow@follow@uniq; StorageDead $e; $e2 = indexUniq(arr, j); $e2@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "i", "j");
        c.N("var arr: Array<uniq/i32> = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr").Borrow("Ly", "y", Uniq, "arr");
        c.N("$r = arr (indexUniq receiver, reserved)").Def("$r").Borrow("Lr", "arr", Uniq, "$r", reserve: true);
        c.N("$e = indexUniq($r, i) (retains the receiver)").Activate("Lr").Def("$e").Flow("$r", "$e");
        c.N("let c = $e@follow@follow@uniq").Def("c").Borrow("Lc", "$e.*.*", Uniq, "c");
        c.N("StorageDead $e").Def("$e");
        const string second = "$r2 = arr (indexUniq receiver, reserved)";
        c.N(second).Def("$r2").Borrow("Lr2", "arr", Uniq, "$r2", reserve: true);
        c.N("$e2 = indexUniq($r2, j)").Activate("Lr2").Def("$e2").Flow("$r2", "$e2");
        c.N("$e2@follow@follow = 7").A(K.Write, "$e2.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error(second, "$e2 = indexUniq($r2, j)");
    }

    // 24c through two borrow Derefs: T's drop writes through self.r@follow@follow; the old value's drop and the store of
    // a value carrying the child happen at one point, so the check (transit) precedes the first-dereference ending.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case DropMerged2() { var c = DropMerged2Case(false); c.OnePoint = DropMerged2Case(true); return c; }

    static Case DropMerged2Case(bool onePoint)
    {
        var c = new Case("91", "`t = T(r: slot2@uniq, c: t.r@follow@follow@uniq)`, T's drop writes through self.r@follow@follow (" + (onePoint ? "drop and store at one point)" : "MIR: `drop t`, then `t = move $v`; the case 23a review: one point)"));
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var t = T(r: slot@uniq, c: y@uniq)").Def("t").Borrow("Lo", "slot", Uniq, "t.r").Borrow("Ly", "y", Uniq, "t.c");
        c.N("$c = t.r@follow@follow@uniq").Def("$c").Borrow("L2", "t.r.*.*", Uniq, "$c");
        c.N("$v = T(r: slot2@uniq, c: $c)").Def("$v").Borrow("Lo2", "slot2", Uniq, "$v.r").Flow("$c", "$v.c");
        string st;
        if (onePoint) c.N(st = "t = move $v (drops old t)").Assign("t").Drop("t").A(K.Write, "t.r.*.*").A(K.Move, "$v").Flow("$v", "t");
        else { c.N(st = "drop t (old value)").Drop("t").A(K.Write, "t.r.*.*"); c.N("t = move $v").Assign("t").A(K.Move, "$v").Flow("$v", "t"); }
        c.N("inspect(t)").Use("t");
        return c.Error(st);
    }

    // T {var r: uniq/uniq/i32, var c: uniq/i32}: the child flows into the holder whose destruction ends it, at that point.
    static Case SamePoint(string id, bool oldRoute)
    {
        var c = new Case(id, "`t = T(r: slot2@uniq, c: t.r@follow@follow@uniq)` (one store); " + (oldRoute ? "`slot@follow = 5; t.c@follow = 1`" : "`t.r@follow@follow = 7; t.c@follow = 1`"));
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var t = T(r: slot@uniq, c: y@uniq)").Def("t").Borrow("Lo", "slot", Uniq, "t.r").Borrow("Ly", "y", Uniq, "t.c");
        c.N("$c = t.r@follow@follow@uniq").Def("$c").Borrow("L2", "t.r.*.*", Uniq, "$c");
        c.N("$v = T(r: slot2@uniq, c: $c)").Def("$v").Borrow("Lo2", "slot2", Uniq, "$v.r").Flow("$c", "$v.c");
        c.N("t = move $v").Assign("t").A(K.Move, "$v").Flow("$v", "t");
        if (oldRoute) { c.N("slot@follow = 5").A(K.Write, "slot.*"); c.N("t.c@follow = 1").A(K.Write, "t.c.*"); return c.Error("slot@follow = 5"); }
        c.N("t.r@follow@follow = 7").A(K.Write, "t.r.*.*");
        c.N("t.c@follow = 1").A(K.Write, "t.c.*");
        c.ALoss = "23a family (the child stored into its own outer holder)"; return c.Valid();
    }

    static Case Replacement()
    {
        var c = new Case("1", "§15.6 example: `r = b@ref` ends the Loan on a");
        c.N("var a = 1; var b = 2").Def("a", "b");
        c.N("var r = a@ref").Def("r").Borrow("L1", "a", Ref, "r");
        c.N("r = b@ref").Assign("r").Borrow("L2", "b", Ref, "r");
        c.N("a = 3").Assign("a");
        c.N("inspect(r)").Use("r");
        return c.Valid();
    }

    static Case Join()
    {
        var c = new Case("2", "§14.9.2 example: a joined result carries both Loans");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("if ready").Goto("  => x@ref", "  => y@ref");
        c.N("  => x@ref").Def("r").Borrow("Lx", "x", Ref, "r").Goto("x = 3");
        c.N("  => y@ref").Def("r").Borrow("Ly", "y", Ref, "r");
        c.N("x = 3").Assign("x");
        c.N("use(r)").Use("r");
        return c.Error("x = 3");
    }

    // 3: copy of the referent's reference; 4: read through two layers; 4b: the Move precedes the borrow of the slot.
    static Case Slot(string id, bool copy, bool early)
    {
        var c = new Case(id, copy ? "`let copy = r@follow` takes slot's Loan, not r's" : early ? "4 with `consume` before `let r = slot@ref`" : "`inspect(r@follow@follow)`: liveness through references");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (early) c.N("consume(data@move)").A(K.Move, "data");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        if (copy) c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        if (!early) c.N("consume(data@move)").A(K.Move, "data");
        if (copy) c.N("inspect(copy)").Use("copy"); else c.N("inspect(r@follow@follow)").A(K.Read, "r.*.*");
        return c.Error("consume(data@move)");
    }

    // §15.7.3: "a Copy of a stored ref/Data keeps only its external Loan", so replacing slot is valid.
    static Case SlotReplaced()
    {
        var c = new Case("3b", "`let copy = r@follow; slot = other@ref; inspect(copy)`");
        c.N("var data = makeData(); var other = makeData()").Def("data", "other");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        c.N("let r = slot@ref").Def("r").Borrow("Ls", "slot", Ref, "r");
        c.N("let copy = r@follow").Def("copy").A(K.Read, "r.*").Flow("r.*", "copy");
        c.N("slot = other@ref").Assign("slot").Borrow("Lo", "other", Ref, "slot");
        c.N("inspect(copy)").Use("copy");
        return c.Valid();
    }

    static Case Index(string id, bool nonPath, bool rewrite)
    {
        var target = nonPath ? "refs.[]" : "refs.0"; var spelled = nonPath ? "refs[n]" : "refs[0]";
        var c = new Case(id, $"`{spelled} = a@ref`" + (rewrite ? $"; `{spelled} = z@ref`" : "") + "; `a = 3`; `inspect(refs)`");
        c.N("var z = 0; var a = 1; let n: isize = pick()").Def("z", "a", "n");
        c.N("var refs: [2 of ref/i32] = [z@ref, z@ref]").Def("refs").Borrow("Lz0", "z", Ref, "refs.0").Borrow("Lz1", "z", Ref, "refs.1");
        foreach (var (src, loan) in rewrite ? new[] { ("a", "La"), ("z", "Lz2") } : [("a", "La")])
            if (nonPath) { c.N($"$t = {src}@ref ({loan})").Def("$t").Borrow(loan, src, Ref, "$t"); c.N($"{spelled} = $t ({loan})").Through(target, "$t"); }
            else c.N($"{spelled} = {src}@ref").Assign(target).Borrow(loan, src, Ref, target);
        c.N("a = 3").Assign("a");
        c.N("inspect(refs)").Use("refs");
        c.ALoss = "P1 (listed in A §2: prototype 5b)"; return nonPath ? c.Error("a = 3") : c.Valid();
    }

    static Case Reborrow(string id, bool write)
    {
        var c = new Case(id, "reborrow `c = r@follow@uniq`; `r = y@uniq`" + (write ? "; `x = 3`" : "") + "; `inspect(c)`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return write ? c.Error("x = 3") : c.Valid();
    }

    // struct Pair {left: ref/i32, right: ref/i32} with `Self is Copy` (§3.5.2).
    static Case Partial(string id, bool inspectP)
    {
        var c = new Case(id, $"`var q = p; q.left = c@ref; a = 3; inspect({(inspectP ? "p" : "q")})`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = Pair{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("var q = p").Def("q").Flow("p", "q");
        c.N("q.left = c@ref").Assign("q.left").Borrow("Lc", "c", Ref, "q.left");
        c.N("a = 3").Assign("a");
        c.N(inspectP ? "inspect(p)" : "inspect(q)").Use(inspectP ? "p" : "q");
        c.ALoss = "P1 (listed in A §2: prototype 7)"; return inspectP ? c.Error("a = 3") : c.Valid();
    }

    // Plan §5.2. `cursor = next` is a bare acquisition of a uniq Place, so it Reborrows (§3.5).
    static Case Deepest(string id, bool clear, bool check)
    {
        var c = new Case(id, check ? "8b: the returned cursor still carries root's Loan L0" : clear ? "deepest with `cursor.children.clear()`" : "deepest as written");
        c.N("func deepest(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  let next = cursor.children[0]@uniq", "return cursor");
        c.N("  let next = cursor.children[0]@uniq").Def("next").Borrow("Ln", "cursor.*.children.[]", Uniq, "next");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = next").Assign("cursor").Borrow("L5", "next.*", Uniq, "cursor");
        c.N("  (end of body: StorageDead next)").Def("next").Goto("while cursor.children.length > 0");
        c.N("return cursor").Flow("cursor", "$ret");
        c.N("exit").Goto();
        if (check) c.Extra = a => a.CarriesAt("exit", "$ret", "L0") ? null : "$ret lacks L0";
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // ---- 9: adversarial cases for S1's soundness hypothesis ----
    static Case CopyChild()
    {
        var c = new Case("9a", "copy a shared child, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@ref").Def("c").Borrow("L2", "r.*", Ref, "c");
        c.N("let d = c").Def("d").Flow("c", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case StoreChild()
    {
        var c = new Case("9b", "store the child in an aggregate, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("var box = Box{item: c@move}").Def("box").A(K.Move, "c").Flow("c", "box.item");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(box)").Use("box");
        return c.Error("x = 3");
    }

    static Case TwoBases()
    {
        var c = new Case("9c", "reborrow through r into s, through s into d; replace s, then r");
        c.N("var x = 1; var y = 2; var z = 4").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = r@follow@uniq").Def("s").Borrow("L2", "r.*", Uniq, "s");
        c.N("let d = s@follow@uniq").Def("d").Borrow("L3", "s.*", Uniq, "d");
        c.N("s = z@uniq").Assign("s").Borrow("Lz", "z", Uniq, "s");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    static Case Loop(string id, string probe)
    {
        var c = new Case(id, $"replace the base inside a loop; `{probe}` after it");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("while cond").Goto("  inspect(c)", probe);
        c.N("  inspect(c)").Use("c");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r").Goto("while cond");
        c.N(probe).Assign(probe[..1]);
        c.N("inspect(c)").Use("c");
        return probe == "x = 3" ? c.Error(probe) : c.Valid();
    }

    // struct H {link: uniq/i32, n: i32}
    static Case FieldBase(string id, bool partial)
    {
        var c = new Case(id, partial ? "reborrow through `h.link`; `h.n = 5`; write through `h.link`" : "reborrow through `h.link`, then replace the ancestor `h`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        if (partial) { c.N("h.n = 5").Assign("h.n"); c.N("h.link@follow = 7").A(K.Write, "h.link.*"); }
        else { c.N("h = H{link: y@uniq, n: 1}").Assign("h").Borrow("Ly", "y", Uniq, "h.link"); c.N("x = 3").Assign("x"); }
        c.N("inspect(c)").Use("c");
        return c.Error(partial ? "h.link@follow = 7" : "x = 3");
    }

    // func pass(v: uniq/i32) -> uniq/i32 during v: the result retains v (a call flow).
    static Case ThroughCall()
    {
        var c = new Case("9f", "pass the child through a retaining call, then replace the base");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = c (Reborrow at a uniq parameter, reserved)").Def("$a").Borrow("L3", "c.*", Uniq, "$a", reserve: true);
        c.N("let d = pass($a)").Activate("L3").Def("d").Flow("$a", "d");
        c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(d)").Use("d");
        return c.Error("x = 3");
    }

    // exchange<T>(target: uniq/T ! with: T) -> T: the result is the old contents, the target receives `with`.
    static Case Exchange()
    {
        var c = new Case("9g", "`exchange(r@uniq, with: y@uniq)` while the child is live; write through the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // Coordinator's case: exchange lowered as a complete replacement of the move path r whose old value moves to `old`.
    static Case ExchangeReplaces()
    {
        var c = new Case("9n", "`old = exchange(r@uniq, with: y@uniq)` as a complete replacement of r; `old@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("r", "old").Exchange("r").Flow("$u", "r");
        c.N("old@follow = 3").A(K.Write, "old.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("old@follow = 3").S1(true, "let old = exchange($t, with: $u)");
    }

    // swap(first: uniq/T, second: uniq/T): both targets wholly replaced, old values exchanged.
    static Case SwapBase()
    {
        var c = new Case("9o", "`swap(r@uniq, s@uniq)` while c is live; `s@follow = 3; c@follow = 4`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("var s = y@uniq").Def("s").Borrow("Ls", "y", Uniq, "s");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$a = r@uniq (reserved)").Def("$a").Borrow("La", "r", Uniq, "$a", reserve: true);
        c.N("$b = s@uniq (reserved)").Def("$b").Borrow("Lb", "s", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Flow("r", "s").Flow("s", "r").Exchange("r").Exchange("s");
        c.N("s@follow = 3").A(K.Write, "s.*");
        c.N("c@follow = 4").A(K.Write, "c.*");
        return c.Error("s@follow = 3").S1(true, "swap($a, $b)");
    }

    // 9g, then a real replacement of the base kills L2 before the old value is used.
    static Case ExchangeThenReplace()
    {
        var c = new Case("9m", "9g, then `r = z@uniq` before writing through the old value");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lr").Activate("Ly").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("r = z@uniq").Assign("r").Borrow("Lz", "z", Uniq, "r");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // func steal(slot: uniq/(uniq/i32 during a), with: uniq/i32 during a) -> uniq/i32 during a => exchange(slot, with: with)
    static Case StealThroughW()
    {
        var c = new Case("9h", "child through `w = r@uniq`; a user call exchanges `w@follow` and returns the old value");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("let c = w@follow@follow@uniq").Def("c").Borrow("L2", "w.*.*", Uniq, "c");
        c.N("$t = w (Reborrow at a uniq parameter, reserved)").Def("$t").Borrow("Lt", "w.*", Uniq, "$t", reserve: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("let old = steal($t, with: $u)").Activate("Lt").Activate("Ly").Def("old").Flow("$t.*", "old").Flow("$u", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return c.Error("old@follow = 7").S1(true, "let old = steal($t, with: $u)");
    }

    static Case SharedBase()
    {
        var c = new Case("9i", "shared borrow of the base; read through it while the child is live");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let z = r@ref").Def("z").Borrow("Lz", "r", Ref, "z");
        c.N("inspect(z@follow@follow)").A(K.Read, "z.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("inspect(z@follow@follow)");
    }

    static Case Nested()
    {
        var c = new Case("9j", "reborrow through two references `r@follow@follow@uniq`; replace the outer base");
        c.N("var x = 1; var o = 5").Def("x", "o");
        c.N("var slot = x@uniq").Def("slot").Borrow("L1", "x", Uniq, "slot");
        c.N("var r = slot@uniq").Def("r").Borrow("Lr", "slot", Uniq, "r");
        c.N("let c = r@follow@follow@uniq").Def("c").Borrow("L2", "r.*.*", Uniq, "c");
        c.N("r = o@uniq").Assign("r").Borrow("Lo", "o", Uniq, "r");
        c.N("x = 3").Assign("x");
        c.N("inspect(c)").Use("c");
        return c.Error("x = 3");
    }

    static Case MoveBase()
    {
        var c = new Case("9k", "Move the base while its child is live (the loss S1 lists)");
        c.N("var x = 1").Def("x");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let q = r@move").Def("q").A(K.Move, "r").Flow("r", "q");
        c.N("inspect(c)").Use("c");
        c.N("inspect(q)").Use("q");
        return c.Valid().S1(true, "let q = r@move");
    }

    // Valid by the current SPEC: the write replaces r's reference value, not x (§15.6.3, §15.7.3). S1′ rejects it.
    static Case UniqBase()
    {
        var c = new Case("9l", "`let w = r@uniq; w@follow = y@uniq` while the child is live (the loss the fix adds)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("let w = r@uniq").Def("w").Borrow("Lw", "r", Uniq, "w");
        c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t");
        c.N("w@follow = $t").Through("w.*", "$t");
        c.N("inspect(c)").Use("c");
        var k = c.Valid().S1(true, "let w = r@uniq"); k.S1POnly = true; return k;
    }

    // ---- Round 2 ----
    // stash(v: uniq/i32) keeps v in storage the body does not own (^stash); a uniq parameter lets the callee use v
    // exclusively during the call (§15.6.4: a uniq requirement creates an exclusive Loan).
    static Case MoveBan(string id, bool rewrite)
    {
        var after = rewrite ? "r = y@uniq" : "c@follow = 1";
        var c = new Case(id, "`if cond => stash(r@move)`" + (rewrite ? "; `r = y@uniq`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("if cond").Goto("  stash(r@move): Move r", after);
        c.N("  stash(r@move): Move r").Def("$v").A(K.Move, "r").Flow("r", "$v");
        c.N("  stash(r@move): callee uses v, keeps it").A(K.UBorrow, "$v.*").Flow("$v", "^stash").Goto(after);
        if (rewrite) c.N("r = y@uniq").Assign("r").Borrow("Ly", "y", Uniq, "r");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Error("  stash(r@move): callee uses v, keeps it").S1(true, "  stash(r@move): Move r");
    }

    // The parent is reached through another reference: c = o@follow@follow@m, base o (the first move path entered).
    static Case RefBase(string id, bool shared, string kind)
    {
        var o = shared ? "rs" : "rr"; var m = shared ? "ref" : "uniq";
        var write = kind == "outer" ? $"{o} = slot2@{m}" : kind == "inner" ? "slot = y@uniq" : $"{o}@follow = y@uniq";
        var c = new Case(id, $"`var {o} = slot@{m}; let c = {o}@follow@follow@{m}`; `{write}`; use c");
        c.N("var x = 1; var y = 2; var z = 3").Def("x", "y", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N($"var {o} = slot@{m}").Def(o).Borrow("Lo", "slot", !shared, o);
        c.N($"let c = {o}@follow@follow@{m}").Def("c").Borrow("L2", o + ".*.*", !shared, "c", sharedAt: shared ? 1 : 0);
        if (kind == "outer") c.N(write).Assign(o).Borrow("Lo2", "slot2", !shared, o);
        else if (kind == "inner") c.N(write).Assign("slot").Borrow("Ly", "y", Uniq, "slot");
        else { c.N("$t = y@uniq").Def("$t").Borrow("Ly", "y", Uniq, "$t"); c.N(write).Through(o + ".*", "$t"); }
        c.N(shared ? "inspect(c@follow)" : "c@follow = 1").A(shared ? K.Read : K.Write, "c.*");
        return kind == "inner" ? c.Error(write) : c.Valid();
    }

    static Case MoveAncestor()
    {
        var c = new Case("14", "`let g = h@move` (an ancestor of base `h.link`) while c is live");
        c.N("var x = 1").Def("x");
        c.N("var h = H{link: x@uniq, n: 0}").Def("h").Borrow("L1", "x", Uniq, "h.link");
        c.N("let c = h.link@follow@uniq").Def("c").Borrow("L2", "h.link.*", Uniq, "c");
        c.N("let g = h@move").Def("g").A(K.Move, "h").Flow("h", "g");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.N("inspect(g)").Use("g");
        return c.Valid().S1(true, "let g = h@move");
    }

    // replace<T>(target: uniq/T ! with: T): the old value is destroyed in place, so `ended` applies and no base_op.
    static Case ReplaceBase(string id, bool write)
    {
        var c = new Case(id, "`Kimi.Intrinsics.replace(r@uniq, with: y@uniq)` while c is live" + (write ? "; `x = 3`" : "") + "; `c@follow = 1`");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
        c.N("$t = r@uniq (reserved, replace target)").Def("$t").Borrow("Lr", "r", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$u = y@uniq (reserved)").Def("$u").Borrow("Ly", "y", Uniq, "$u", reserve: true);
        c.N("replace($t, with: $u)").Activate("Lr").Activate("Ly").Def("r").Flow("$u", "r");
        if (write) c.N("x = 3").Assign("x");
        c.N("c@follow = 1").A(K.Write, "c.*");
        c.ALoss = "A §2 counts a replace target as an exclusive borrow; plan e32dc0ef exempts it"; return write ? c.Error("x = 3") : c.Valid();
    }

    // ---- Round 3: adversarial cases for design A ----
    // Syntactic access without authority: aliases formed through a reborrow, a Copy, an aggregate, a write through a reference.
    static Case Alias(string id, string how)
    {
        var c = new Case(id, how switch
        {
            "reborrow" => "`let a = r` (Reborrow); `let c = a@follow@uniq`; `r@follow = 5`; `inspect(c)`",
            "copy" => "`let z = r@follow@ref; let z2 = z`; `let c = r@follow@uniq`; `inspect(z2)`",
            "aggregate" => "`let c = r@follow@uniq; var h = H{link: c}`; `r@follow = 5`; `inspect(h)`",
            "stored" => "`let w = s@uniq; w@follow = r` (stores a Reborrow of r in s); `r@follow = 5`; `inspect(s@follow)`",
            _ => "`var rr = slot@uniq; let c = rr@follow@follow@uniq`; `slot@follow = 5`; `inspect(c)`",
        });
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        var probe = "r@follow = 5";
        switch (how)
        {
            case "reborrow":
                c.N("let a = r").Def("a").Borrow("La", "r.*", Uniq, "a");
                c.N("let c = a@follow@uniq").Def("c").Borrow("L2", "a.*", Uniq, "c");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(c)").Use("c"); break;
            case "copy":
                c.N("let z = r@follow@ref").Def("z").Borrow("Lz", "r.*", Ref, "z");
                c.N("let z2 = z").Def("z2").Flow("z", "z2");
                probe = "let c = r@follow@uniq"; c.N(probe).Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("inspect(z2)").Use("z2"); c.N("inspect(c)").Use("c"); break;
            case "aggregate":
                c.N("let c = r@follow@uniq").Def("c").Borrow("L2", "r.*", Uniq, "c");
                c.N("var h = H{link: c}").Def("h").Borrow("Lh", "c.*", Uniq, "h.link");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(h)").Use("h"); break;
            case "stored":
                c.N("var s = y@uniq").Def("s").Borrow("Ly", "y", Uniq, "s");
                c.N("let w = s@uniq").Def("w").Borrow("Lw", "s", Uniq, "w");
                c.N("$t = r (Reborrow)").Def("$t").Borrow("La", "r.*", Uniq, "$t");
                c.N("w@follow = $t").Through("w.*", "$t");
                c.N(probe).A(K.Write, "r.*"); c.N("inspect(s@follow)").A(K.Read, "s.*"); break;
            default:
                c.N("var slot = x@uniq").Def("slot").Borrow("Ls", "x", Uniq, "slot");
                c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
                c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
                probe = "slot@follow = 5"; c.N(probe).A(K.Write, "slot.*"); c.N("inspect(c)").Use("c"); break;
        }
        return c.Error(probe);
    }

    // The closure needs no feedback: references held by holders that are dead at the conflicting access.
    static Case DeadHolder(string id, bool chain)
    {
        var c = new Case(id, chain ? "`mid = slot@ref; top = mid@ref`; `consume(data@move)`; read through `top` (slot and mid otherwise dead)"
                                   : "`w = slot@uniq`; `consume(data@move)`; `t = w@follow@ref` formed after it; read through `t`");
        c.N("var data = makeData()").Def("data");
        c.N("var slot: ref/Data = data@ref").Def("slot").Borrow("Ld", "data", Ref, "slot");
        if (chain)
        {
            c.N("var mid = slot@ref").Def("mid").Borrow("Ls", "slot", Ref, "mid");
            c.N("var top = mid@ref").Def("top").Borrow("Lm", "mid", Ref, "top");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("inspect(top@follow@follow@follow)").A(K.Read, "top.*.*.*");
        }
        else
        {
            c.N("var w = slot@uniq").Def("w").Borrow("Lw", "slot", Uniq, "w");
            c.N("consume(data@move)").A(K.Move, "data");
            c.N("let t = w@follow@ref").Def("t").Borrow("Lt", "w.*", Ref, "t");
            c.N("inspect(t@follow@follow)").A(K.Read, "t.*.*");
        }
        return c.Error("consume(data@move)");
    }

    // P1 and a partial Move: struct P2 {left: ref/i32, right: ref/i32}, not Copy.
    static Case PartialMove(string id, bool restore)
    {
        var c = new Case(id, restore ? "`let l = p.left@move; p.left = c@ref; a = 3; inspect(p)`" : "`let l = p.left@move; a = 3; inspect(l)`");
        c.N("var a = 1; var b = 2; var c = 3").Def("a", "b", "c");
        c.N("var p = P2{left: a@ref, right: b@ref}").Def("p").Borrow("La", "a", Ref, "p.left").Borrow("Lb", "b", Ref, "p.right");
        c.N("let l = p.left@move").Def("l").A(K.Move, "p.left").Flow("p.left", "l");
        if (restore) c.N("p.left = c@ref").Assign("p.left").Borrow("Lc", "c", Ref, "p.left");
        c.N("a = 3").Assign("a");
        c.N(restore ? "inspect(p)" : "inspect(l)").Use(restore ? "p" : "l");
        if (restore) { c.ALoss = "P1 (a partial write ends no Loan)"; return c.Valid(); }
        return c.Error("a = 3");
    }

    // A Reborrow stored back into its own base (the in-place traversal); 19b also writes through the parent root.
    static Case InPlace(string id, bool parent)
    {
        var c = new Case(id, "`cursor = cursor.children[0]@uniq` in a loop" + (parent ? "; `root.value = 2`" : "") + "; `cursor.value = 1`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  cursor = cursor.children[0]@uniq", parent ? "root.value = 2" : "cursor.value = 1");
        c.N("  cursor = cursor.children[0]@uniq").Assign("cursor").Borrow("Ln", "cursor.*.children.[]", Uniq, "cursor").Goto("while cursor.children.length > 0");
        if (parent) c.N("root.value = 2").A(K.Write, "root.*.value");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return parent ? c.Error("root.value = 2") : c.Valid();
    }

    // ---- Round 4: one Loan id covers every iteration of a borrow site ----
    static Case LoopSite(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniq" => "loop: `let cur = v@uniq; inspect(prev); prev = cur`",
            "child" => "loop: `let cur = r@follow@uniq; inspect(prev); prev = cur`",
            "reserved" => "loop: `let k2 = f(v@uniq)` (reserved; f retains); `inspect(keep); keep = k2`",
            "window" => "loop: reserved `$t = v@uniq`, then `$n = v + 0` in the window; `keep = f($t, $n)`",
            "refkept" => "loop: `let u = v@uniq; u@follow = 1; inspect(keep); let a = v@ref; keep = a`",
            _ => "loop: `let s = v@ref; inspect(s); inspect(keep); let a = v@uniq; keep = a`",
        });
        c.N("var v = 1; var w = 2; var z = 0").Def("v", "w", "z");
        var head = "loop";
        switch (kind)
        {
            case "uniq": case "child":
                if (kind == "child") c.N("var x = 1; var r = x@uniq").Def("x", "r").Borrow("L1", "x", Uniq, "r");
                c.N("var prev = w@uniq").Def("prev").Borrow("Lw", "w", Uniq, "prev");
                c.N(head);
                var site = kind == "uniq" ? "  let cur = v@uniq" : "  let cur = r@follow@uniq";
                c.N(site).Def("cur").Borrow("Lc", kind == "uniq" ? "v" : "r.*", Uniq, "cur");
                c.N("  inspect(prev)").Use("prev");
                c.N("  prev = cur").Assign("prev").Flow("cur", "prev").Goto(head);
                return c.Error(site);
            case "reserved": case "window":
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  $t = v@uniq (reserved)").Def("$t").Borrow("Lt", "v", Uniq, "$t", reserve: true);
                if (kind == "window") c.N("  $n = v + 0").Def("$n").A(K.Read, "v");
                c.N("  let k2 = f($t)").Activate("Lt").Def("k2").Flow("$t", "k2");
                c.N("  inspect(keep)").Use("keep");
                c.N("  keep = k2").Assign("keep").Flow("k2", "keep").Goto(head);
                return c.Error();
            case "refkept":
                c.N("var keep: ref/i32 = z@ref").Def("keep").Borrow("Lz", "z", Ref, "keep");
                c.N(head);
                c.N("  let u = v@uniq").Def("u").Borrow("Lu", "v", Uniq, "u");
                c.N("  u@follow = 1").A(K.Write, "u.*");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@ref").Def("a").Borrow("La", "v", Ref, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error(); // `let u` and the write through u both conflict with the earlier shared Loan
            default:
                c.N("var keep = w@uniq").Def("keep").Borrow("Lw", "w", Uniq, "keep");
                c.N(head);
                c.N("  let s = v@ref").Def("s").Borrow("Ls", "v", Ref, "s");
                c.N("  inspect(s)").Use("s");
                c.N("  inspect(keep)").Use("keep");
                c.N("  let a = v@uniq").Def("a").Borrow("La", "v", Uniq, "a");
                c.N("  keep = a").Assign("keep").Flow("a", "keep").Goto(head);
                return c.Error("  let s = v@ref");
        }
    }

    // ---- Round 5 (plan fe1a5b34) ----
    // MIR's Temp shape of `cursor = cursor.children[0]@uniq`: the child is issued into _t, then moved into cursor.
    static Case TempStep(string id, bool clear)
    {
        var c = new Case(id, "Temp shape: `_t = Ref(cursor.children[0])`" + (clear ? "; `cursor.children.clear()`" : "") + "; `cursor = move _t`");
        c.N("func descend(root: uniq/Tree)").Param("Lroot", "root");
        c.N("var cursor = root").Def("cursor").Borrow("L0", "root.*", Uniq, "cursor");
        c.N("while cursor.children.length > 0").A(K.Read, "cursor.*.children").Goto("  _t = Ref(cursor.children[0])", "cursor.value = 1");
        c.N("  _t = Ref(cursor.children[0])").Def("_t").Borrow("Ln", "cursor.*.children.[]", Uniq, "_t");
        if (clear) c.N("  cursor.children.clear()").A(K.UBorrow, "cursor.*.children");
        c.N("  cursor = move _t").Assign("cursor").A(K.Move, "_t").Flow("_t", "cursor").Goto("while cursor.children.length > 0");
        c.N("cursor.value = 1").A(K.Write, "cursor.*.value");
        return clear ? c.Error("  cursor.children.clear()") : c.Valid();
    }

    // var arr: [2 of uniq/i32] = [x@uniq, y@uniq]; a child through arr[i], an exchange of arr[j] (§4.1, §15.1.3).
    static Case IndexBan(string id, bool constant)
    {
        var (ci, cj) = constant ? ("0", "1") : ("[]", "[]");
        var (si, sj) = constant ? ("0", "1") : ("i", "j");
        var c = new Case(id, $"`c = arr[{si}]@follow@uniq; old = exchange(arr[{sj}]@uniq, with: z@uniq); old@follow = 7; inspect(c)`");
        c.N("var x = 1; var y = 2; var z = 3; let i: isize = pick(); let j: isize = pick()").Def("x", "y", "z", "i", "j");
        c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
        c.N($"let c = arr[{si}]@follow@uniq").Def("c").Borrow("L2", $"arr.{ci}.*", Uniq, "c");
        c.N($"$t = arr[{sj}]@uniq (reserved)").Def("$t").Borrow("Lt", $"arr.{cj}", Uniq, "$t", reserve: true);
        c.N("$u = z@uniq (reserved)").Def("$u").Borrow("Lz", "z", Uniq, "$u", reserve: true);
        c.N("let old = exchange($t, with: $u)").Activate("Lt").Activate("Lz").Def("old").Flow("$t.*", "old").Through("$t.*", "$u");
        c.N("old@follow = 7").A(K.Write, "old.*");
        c.N("inspect(c)").Use("c");
        return constant ? c.Valid() : c.Error("old@follow = 7").S1(true, "let old = exchange($t, with: $u)");
    }

    // The outer holder is replaced; the child's base is rr.*, which `ended` (no Deref between B and K) never ends.
    static Case OuterThenWrite()
    {
        var c = new Case("23a", "`c = rr@follow@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let c = rr@follow@follow@uniq").Def("c").Borrow("L2", "rr.*.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid().S1(true, "rr@follow@follow = 7"); // listed loss of the plan's kill (round 6 correction)
    }

    // Round 6 correction (plan review): a kill across a Deref ends both children of q. S {var p: uniq/i32}.
    static Case CrossingKill()
    {
        var c = new Case("29a", "(coordinator's 25a) `q = s@uniq; c = q.p@follow@uniq; w = q@follow@ref; q = o@uniq; a = w.p@follow; c@follow = 5; inspect(w.p@follow)`");
        c.N("var t = 1; var u = 2").Def("t", "u");
        c.N("var s = S(p: t@uniq); var o = S(p: u@uniq)").Def("s", "o").Borrow("Lt", "t", Uniq, "s.p").Borrow("Lu", "u", Uniq, "o.p");
        c.N("var q = s@uniq").Def("q").Borrow("Lq", "s", Uniq, "q");
        c.N("let c = q.p@follow@uniq").Def("c").Borrow("Lc", "q.*.p.*", Uniq, "c");
        c.N("let w = q@follow@ref").Def("w").Borrow("Lw", "q.*", Ref, "w");
        c.N("q = o@uniq").Assign("q").Borrow("Lo", "o", Uniq, "q");
        c.N("let a = w.p@follow").Def("a").A(K.Read, "w.*.p.*");
        c.N("c@follow = 5").A(K.Write, "c.*");
        c.N("inspect(w.p@follow)").A(K.Read, "w.*.p.*");
        return c.Error();
    }

    // Transit into an escaped destination: p: uniq/S; S's drop writes through self.r; drop and store at one point.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case EscapedTransit() { var c = EscapedTransitCase(false); c.OnePoint = EscapedTransitCase(true); return c; }

    static Case EscapedTransitCase(bool onePoint)
    {
        var c = new Case("29b", "`p: uniq/S`; `p@follow = S(r: p.r@follow@uniq)`, S's drop writes through self.r (" + (onePoint ? "one point)" : "MIR: `drop (*p)`, then `(*p) = move $v`; rounds 6-12: one point)"));
        c.N("func f(p: uniq/S)").Param("Lp", "p");
        c.N("$t = p.r@follow@uniq").Def("$t").Borrow("L2", "p.*.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        string st;
        if (onePoint) c.N(st = "p@follow = move $v (drops old *p)").Through("p.*", "$v").Drop("p.*").A(K.Write, "p.*.r.*").A(K.Move, "$v");
        else { c.N(st = "drop (*p) (old value)").Drop("p.*").A(K.Write, "p.*.r.*"); c.N("p@follow = move $v").Through("p.*", "$v").A(K.Move, "$v"); }
        return c.Error(st);
    }

    // Linked-list traversal (Temp shape). struct Node {var value: i32; var next: Option<L>}; `.some` is the payload
    // a match selects. ll1: L = obj/Node, whose payload is an owned step (part of the handle's storage, A2 §3), so the
    // place has one borrow Deref and B = node. ll2: L = uniq/Node, so B = node.*.next.some, and `node = …` ends nothing.
    static Case LinkedList(string id, string kind)
    {
        bool obj = kind == "obj"; bool conflict = kind == "conflict";
        var c = new Case(id, (obj ? "ll1 (obj links)" : conflict ? "ll3 (uniq links, write through the old link)" : kind == "peek" ? "ll2 + read through the new node's link" : "ll2 (uniq links)")
            + ": loop `_t = node.next.some@follow@uniq; node = move _t`" + (conflict ? " with `node.next@follow.value = 1` while _t is live" : ""));
        c.N("func last(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  _t = node.next.some@follow@uniq", "node.value = 1");
        c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", obj ? "node.*.next.some.payload" : "node.*.next.some.*", Uniq, "_t");
        if (conflict) c.N("  node.next@follow.value = 1").A(K.Write, "node.*.next.some.*.value");
        var step = c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
        if (kind == "peek") c.N("  inspect(node.next@follow.value)").A(K.Read, "node.*.next.some.*.value").Goto("while node.next is .Some");
        else step.Goto("while node.next is .Some");
        c.N("node.value = 1").A(K.Write, "node.*.value");
        if (kind == "peek") c.ALoss = "23a family: the plan's within kill (stale base name through a Deref)";
        return conflict ? c.Error("  node.next@follow.value = 1") : c.Valid();
    }

    // ---- Round 7 (design A3 unit 0b) ----
    // swap with two reference parameters: the caller's activation keeps them disjoint (S15:614). 31b: the caller.
    static Case SwapParams(string id, bool caller)
    {
        var c = new Case(id, caller ? "caller of `f(a, b) => swap(a, b)`: `f(x@uniq, x@uniq)`" : "`func f(a: uniq/T, b: uniq/T) => Kimi.Intrinsics.swap(a, b)`");
        if (caller)
        {
            c.N("var x = make()").Def("x");
            c.N("$1 = x@uniq (reserved)").Def("$1").Borrow("L1", "x", Uniq, "$1", reserve: true);
            c.N("$2 = x@uniq (reserved)").Def("$2").Borrow("L2", "x", Uniq, "$2", reserve: true);
            c.N("f($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
            return c.Error("f($1, $2)");
        }
        c.N("func f(a: uniq/T, b: uniq/T)").Param("La", "a").Param("Lb", "b");
        c.N("$1 = a (Reborrow at a uniq parameter, reserved)").Def("$1").Borrow("L1", "a.*", Uniq, "$1", reserve: true);
        c.N("$2 = b (Reborrow at a uniq parameter, reserved)").Def("$2").Borrow("L2", "b.*", Uniq, "$2", reserve: true);
        c.N("swap($1, $2)").Activate("L1").Activate("L2").Use("$1", "$2");
        return c.Valid();
    }

    // A Copy read through a reference stored into a holder (BinaryOp: the value carries no Loan).
    static Case CopyThrough(string id, bool childLive)
    {
        var c = new Case(id, childLive ? "`var h = x@uniq; let c = h@follow@uniq; y = h@follow + 1; c@follow = 2`" : "`let h = x@ref; y = h@follow + 1; x = 3; inspect(y)`");
        c.N("var x = 1; var y = 0").Def("x", "y");
        if (childLive)
        {
            c.N("var h = x@uniq").Def("h").Borrow("Lh", "x", Uniq, "h");
            c.N("let c = h@follow@uniq").Def("c").Borrow("Lc", "h.*", Uniq, "c");
            c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
            c.N("y = $v + 1").Assign("y").Use("$v");
            c.N("c@follow = 2").A(K.Write, "c.*");
            return c.Error("$v = h@follow");
        }
        c.N("let h = x@ref").Def("h").Borrow("Lh", "x", Ref, "h");
        c.N("$v = h@follow").Def("$v").A(K.Read, "h.*").Flow("h.*", "$v");
        c.N("y = $v + 1").Assign("y").Use("$v");
        c.N("x = 3").Assign("x");
        c.N("inspect(y)").Use("y");
        return c.Valid();
    }

    // §8.11.3 takeTwo (valid) and the plan's takeAcross (error). `take` is abstract and its result descends from the
    // receiver, so it carries Φ_c on `source.*`. 33c: the excuse only at the Call, not at the receiver's reservation.
    static Case PreservesResults(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "across" => "`takeAcross`: `first = source.take(); source@follow = fresh@move; second = source.take()`",
            "callonly" => "`takeTwo`, the excuse stated at the Call only; round 10 derives it at the reservation (A3's erratum)",
            "other" => "`first = source.take(); let n = source.other(); return first` (`other` is another requirement)",
            "inwindow" => "`first = source.take(); second = source.takeWith(do { source@follow = fresh@move; 0 })`",
            _ => "`takeTwo<S: StableTake>`: `first = source.take(); second = source.take()` (§8.11.3)",
        });
        c.N("func f(source: uniq/S, fresh: S)").Param("Ls", "source").Def("fresh");
        c.N("$r1 = source (receiver, reserved)").Def("$r1").Borrow("Lr1", "source.*", Uniq, "$r1", reserve: true);
        c.N("let first = source.take()").Activate("Lr1").Def("first").Phantom("Phi1", "source.*", "first", "Lr1");
        if (kind == "other") // round 10: the derived excuse never covers a call that is not excused (33c's derivation)
        {
            c.N("$r2 = source (receiver of other, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
            c.N("let n = source.other()").Activate("Lr2").Def("n");
            c.N("return first").Flow("first", "$ret");
            return c.Error();
        }
        if (kind == "inwindow") // round 10: a disturbance inside c2's argument; the excuse holds at the formation only
        {
            c.N("$r2 = source (receiver, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
            c.N("do { source@follow = fresh@move; 0 } (argument)").A(K.Move, "fresh").Through("source.*", "fresh").Disturb("Phi1");
            c.N("let second = source.takeWith($r2, …)").Activate("Lr2").Def("second").Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
            c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
            return c.Error("do { source@follow = fresh@move; 0 } (argument)", "let second = source.takeWith($r2, …)");
        }
        if (kind == "across") c.N("source@follow = fresh@move").A(K.Move, "fresh").Through("source.*", "fresh").Disturb("Phi1");
        var r2 = c.N("$r2 = source (receiver, reserved)").Def("$r2").Borrow("Lr2", "source.*", Uniq, "$r2", reserve: true);
        if (kind != "callonly") r2.Excuse("Phi1");
        c.N("let second = source.take()").Activate("Lr2").Def("second").Phantom("Phi2", "source.*", "second", "Lr2").Excuse("Phi1");
        c.N("return (first@move, second@move)").Flow("first", "$ret").Flow("second", "$ret");
        return kind == "across" ? c.Error() : c.Valid();
    }

    // ---- Round 8 (case 23a proposal review) ----
    // A child through arr[i] (one or two borrow Derefs); arr[0] (34a/34b) or the whole arr (34c) is replaced; a write
    // through arr[i], the old route unless the whole array was replaced.
    static Case IndexKill(string id, bool deref2, bool whole = false)
    {
        var f = deref2 ? "@follow@follow" : "@follow";
        var c = new Case(id, $"`c = arr[i]{f}@uniq; {(whole ? "arr = [z@uniq, w@uniq]" : "arr[0] = …")}; arr[i]{f} = 7; c@follow = 1`");
        c.N("var x = 1; var y = 2; var z = 3; var w = 4; let i: isize = pick()").Def("x", "y", "z", "w", "i");
        if (deref2)
        {
            c.N("var s1 = x@uniq; var s2 = y@uniq; var s3 = z@uniq").Def("s1", "s2", "s3").Borrow("Lx", "x", Uniq, "s1").Borrow("Ly", "y", Uniq, "s2").Borrow("Lz", "z", Uniq, "s3");
            c.N("var arr: [2 of uniq/uniq/i32] = [s1@uniq, s2@uniq]").Def("arr").Borrow("L0", "s1", Uniq, "arr.0").Borrow("L1", "s2", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@follow@uniq").Def("c").Borrow("L2", "arr.[].*.*", Uniq, "c");
            c.N("arr[0] = s3@uniq").Assign("arr.0").Borrow("L3", "s3", Uniq, "arr.0");
        }
        else
        {
            c.N("var arr: [2 of uniq/i32] = [x@uniq, y@uniq]").Def("arr").Borrow("Lx", "x", Uniq, "arr.0").Borrow("Ly", "y", Uniq, "arr.1");
            c.N("let c = arr[i]@follow@uniq").Def("c").Borrow("L2", "arr.[].*", Uniq, "c");
            if (whole) c.N("arr = [z@uniq, w@uniq]").Assign("arr").Borrow("Lz", "z", Uniq, "arr.0").Borrow("Lw", "w", Uniq, "arr.1");
            else c.N("arr[0] = z@uniq").Assign("arr.0").Borrow("Lz", "z", Uniq, "arr.0");
        }
        c.N($"arr[i]{f} = 7").A(K.Write, deref2 ? "arr.[].*.*" : "arr.[].*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        if (!whole) return c.Error($"arr[i]{f} = 7");
        c.ALoss = "the plan's within: no end across a non-path index after K (narrower than S1's text)";
        return c.Valid();
    }

    // 23a split through a named intermediate: t's Loan has base rr, so rr = slot2@uniq ends it under every base rule.
    static Case SplitOuter()
    {
        var c = new Case("37", "23a through a named intermediate: `t = rr@follow@uniq; c = t@follow@uniq; rr = slot2@uniq; rr@follow@follow = 7; c@follow = 1`");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var slot = x@uniq; var slot2 = z@uniq").Def("slot", "slot2").Borrow("L1", "x", Uniq, "slot").Borrow("Lz", "z", Uniq, "slot2");
        c.N("var rr = slot@uniq").Def("rr").Borrow("Lo", "slot", Uniq, "rr");
        c.N("let t = rr@follow@uniq").Def("t").Borrow("Lt", "rr.*", Uniq, "t");
        c.N("let c = t@follow@uniq").Def("c").Borrow("L2", "t.*", Uniq, "c");
        c.N("rr = slot2@uniq").Assign("rr").Borrow("Lo2", "slot2", Uniq, "rr");
        c.N("rr@follow@follow = 7").A(K.Write, "rr.*.*");
        c.N("c@follow = 1").A(K.Write, "c.*");
        return c.Valid();
    }

    // uniq-linked walk (ll2): a Borrow of the new node's Field outlives the next step. 35a is straight-line; 35b keeps
    // each iteration's Borrow in one holder used after the loop.
    static Case Walk(string id, bool loop)
    {
        var c = new Case(id, loop ? "ll2 loop: `keep = node.value@uniq; node = node.next…@uniq`, keep used after the loop"
            : "`node = node.next…@uniq; b = node.value@uniq; node = node.next…@uniq; b@follow = 1`");
        c.N("func f(head: uniq/Node)").Param("Lh", "head");
        c.N("var node = head").Def("node").Borrow("L0", "head.*", Uniq, "node");
        if (loop)
        {
            c.N("var keep = none").Def("keep");
            c.N("while node.next is .Some").A(K.Read, "node.*.next").Goto("  keep = node.value@uniq", "keep@follow = 1");
            c.N("  keep = node.value@uniq").Assign("keep").Borrow("Lb", "node.*.value", Uniq, "keep");
            c.N("  _t = node.next.some@follow@uniq").Def("_t").Borrow("Ln", "node.*.next.some.*", Uniq, "_t");
            c.N("  node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node").Goto("while node.next is .Some");
            c.N("keep@follow = 1").A(K.Write, "keep.*");
        }
        else
        {
            c.N("_t = node.next.some@follow@uniq").Def("_t").Borrow("Ln1", "node.*.next.some.*", Uniq, "_t");
            c.N("node = move _t").Assign("node").A(K.Move, "_t").Flow("_t", "node");
            c.N("let b = node.value@uniq").Def("b").Borrow("Lb", "node.*.value", Uniq, "b");
            c.N("_t2 = node.next.some@follow@uniq").Def("_t2").Borrow("Ln2", "node.*.next.some.*", Uniq, "_t2");
            c.N("node = move _t2").Assign("node").A(K.Move, "_t2").Flow("_t2", "node");
            c.N("b@follow = 1").A(K.Write, "b.*");
        }
        c.ALoss = "23a family: the plan's within kill (a Borrow through the new node while a kept value carries the stale Loan)";
        return c.Valid();
    }

    // F6 phantom through a replaced holder. H {var inner: uniq/S}; Φ_c on cur.*.inner.* is carried by first. cur moves to
    // b; then 36a/36b clear a's iterator through cur = a again, 36c/36d through a itself. retain: first also keeps the
    // receiver Loan Lr1 (A3 `out` with Value retention); otherwise first keeps Φ_c and, from round 9, its parent Loans.
    static Case PhantomParents(string id, bool direct, bool retain)
    {
        var c = new Case(id, "F6: `first = cur.inner.take(); cur = b; " + (direct ? "a.inner.clear()" : "cur = a; cur.inner.clear()")
            + "; return first`" + (retain ? ", first retains the receiver" : ""));
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        var call = c.N("let first = cur.inner.take()").Activate("Lr1").Def("first");
        if (retain) call.Flow("$r1", "first");
        call.Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        c.N("cur = b").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur").Disturb("Phi1");
        if (!direct) c.N("cur = a").Assign("cur").Borrow("La2", "a.*", Uniq, "cur");
        var recv = direct ? "a.*.inner.*" : "cur.*.inner.*";
        c.N("$r2 = " + (direct ? "a" : "cur") + ".inner (receiver of clear)").Def("$r2").Borrow("Lr2", recv, Uniq, "$r2");
        c.N("clear($r2)").Use("$r2");
        c.N("return first").Flow("first", "$ret");
        return c.Error();
    }

    // Round 9 (G113), from the review. 36e/36h: a `do` argument assigns cur during the receiver's reservation, which
    // SPEC 15.6.7 rejects and A3's shallow write misses (36h, a gap outside G113); 36e then shows that Φ_c's parents
    // come from the receiver's value (a's), not from recv(c) at the call (b's). 36f: the cost P12, a write to another
    // part of the parent referent while the result is live (SPEC and rustc accept). 36g: plain Take; `it = a` ends Φ_c
    // under the plan base, so only the parent Loans reject the reborrow.
    static Case PhantomRoot(string id)
    {
        var c = new Case(id, "F6: " + id switch
        {
            "36e" => "`first = cur.inner.takeWith(do { cur = b; 0 }); a.inner.clear(); return first`",
            "36f" => "`var h = H(inner: src, other: 0); var r = h@uniq; first = r.inner.take(); h.other = 5; return first`",
            "36g" => "`var it = a; first = it.take(); it = a; second = it.take()` (Take only)",
            _ => "`first = cur.inner.takeWith(do { cur = b; 0 }); return first`",
        });
        if (id == "36f")
        {
            c.N("func f(src: uniq/S)").Param("Ls", "src");
            c.N("var h = H(inner: src, other: 0)").Def("h").Borrow("Lsrc", "src.*", Uniq, "h.inner");
            c.N("var r = h@uniq").Def("r").Borrow("Lh", "h", Uniq, "r");
            c.N("$r1 = r.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "r.*.inner.*", Uniq, "$r1", reserve: true);
            c.N("let first = r.inner.take()").Activate("Lr1").Def("first").Phantom("Phi1", "r.*.inner.*", "first", "Lr1");
            c.N("h.other = 5").Assign("h.other");
            c.N("return first").Flow("first", "$ret");
            c.ALoss = "P12 (G113): a descending abstract-call result keeps the receiver's parent Loans";
            return c.Valid();
        }
        if (id == "36g")
        {
            c.N("func f(a: uniq/S)").Param("La", "a");
            c.N("var it = a").Def("it").Borrow("L0", "a.*", Uniq, "it");
            c.N("$r1 = it (receiver, reserved)").Def("$r1").Borrow("Lr1", "it.*", Uniq, "$r1", reserve: true);
            c.N("let first = it.take()").Activate("Lr1").Def("first").Phantom("Phi1", "it.*", "first", "Lr1");
            c.N("it = a").Assign("it").Borrow("L1", "a.*", Uniq, "it").Disturb("Phi1");
            c.N("$r2 = it (receiver, reserved)").Def("$r2").Borrow("Lr2", "it.*", Uniq, "$r2", reserve: true);
            c.N("let second = it.take()").Activate("Lr2").Def("second").Phantom("Phi2", "it.*", "second", "Lr2");
            c.N("return (first, second)").Flow("first", "$ret").Flow("second", "$ret");
            return c.Error();
        }
        c.N("func f(a: uniq/H, b: uniq/H)").Param("La", "a").Param("Lb", "b");
        c.N("var cur = a").Def("cur").Borrow("L0", "a.*", Uniq, "cur");
        c.N("$r1 = cur.inner (receiver, reserved)").Def("$r1").Borrow("Lr1", "cur.*.inner.*", Uniq, "$r1", reserve: true);
        c.N("do { cur = b; 0 } (argument)").Assign("cur").Borrow("Lb2", "b.*", Uniq, "cur");
        c.N("let first = takeWith($r1, …)").Activate("Lr1").Def("first").Phantom("Phi1", "cur.*.inner.*", "first", "Lr1");
        if (id == "36e")
        {
            c.N("$r2 = a.inner (receiver of clear)").Def("$r2").Borrow("Lr2", "a.*.inner.*", Uniq, "$r2");
            c.N("clear($r2)").Use("$r2");
        }
        c.N("return first").Flow("first", "$ret");
        return id == "36h" ? c.Error("do { cur = b; 0 } (argument)") : c.Error(); // 36e: SPEC errs first at the write, as in 36h
    }

    // Path-exact transit: the store goes to s.inner, while only s is used later.
    static Case PartTransit()
    {
        var c = new Case("29c", "`s.inner = f(s.inner@ref)` (retains); only `s` used later");
        c.N("var s = S(inner: make(), other: 0)").Def("s");
        c.N("$a = s.inner@ref").Def("$a").Borrow("Li", "s.inner", Ref, "$a");
        c.N("$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("s.inner = move $r").Assign("s.inner").A(K.Move, "$r").Flow("$r", "s.inner");
        c.N("inspect(s)").Use("s");
        return c.Error("s.inner = move $r");
    }

    // Soundness probe for a kill across a Deref: the outer holder is a reborrow of a parameter's referent.
    static Case ParamOuter()
    {
        var c = new Case("23b", "`q: uniq/(uniq/i32)`; `var q2 = q; c = q2@follow@follow@uniq; q2 = s2@uniq; q@follow@follow = 5; inspect(c)`");
        c.N("func f(q: uniq/(uniq/i32))").Param("Lq", "q");
        c.N("var o = 5; var s2 = o@uniq").Def("o", "s2").Borrow("Ls2", "o", Uniq, "s2");
        c.N("var q2 = q").Def("q2").Borrow("Lq2", "q.*", Uniq, "q2");
        c.N("let c = q2@follow@follow@uniq").Def("c").Borrow("L2", "q2.*.*", Uniq, "c");
        c.N("q2 = s2@uniq").Assign("q2").Borrow("Lq3", "s2", Uniq, "q2");
        c.N("q@follow@follow = 5").A(K.Write, "q.*.*");
        c.N("inspect(c)").Use("c");
        return c.Error("q@follow@follow = 5");
    }

    // struct S {r: uniq/i32}; 24a: S has a `drop` writing through self.r. `s = S(r: s.r@follow@uniq)` in MIR order:
    // the right side into Temps, the old value destroyed (its drop runs), then the store.
    static Case DropBase(string id, bool userDrop)
    {
        var c = new Case(id, "`s = S(r: s.r@follow@uniq)`" + (userDrop ? ", S's drop writes through self.r" : ", S has no drop"));
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        var d = c.N("destroy old s" + (userDrop ? " (drop writes self.r@follow)" : "")).Def("s");
        if (userDrop) d.Drop("s").A(K.Write, "s.r.*");
        c.N("s = move $v").Assign("s").A(K.Move, "$v").Flow("$v", "s");
        c.N("inspect(s)").Use("s");
        return userDrop ? c.Error("destroy old s (drop writes self.r@follow)") : c.Valid();
    }

    // 24a with the destruction and the store at one point: the new value is in transit from the dying $v.
    // Round 13: as MIR builds it, the Drop terminator (MirTerminatorKind.Drop), then the store at the next location (the second
    // design review's dump fD2: `drop _2 -> bb3; _2 = move _7`); rounds 5-12's one-point encoding is Case.OnePoint (OnePointDrop).
    static Case DropMerged() { var c = DropMergedCase(false); c.OnePoint = DropMergedCase(true); return c; }

    static Case DropMergedCase(bool onePoint)
    {
        var c = new Case("24c", "24a with the old value's drop and the store " + (onePoint ? "at one point" : "as MIR builds them: `drop s`, then `s = move $v` (rounds 5-12: at one point)"));
        c.N("var x = 1").Def("x");
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("$t = s.r@follow@uniq").Def("$t").Borrow("L2", "s.r.*", Uniq, "$t");
        c.N("$v = S(r: $t)").Def("$v").Flow("$t", "$v.r");
        string st;
        if (onePoint) c.N(st = "s = move $v (drops old s)").Assign("s").Drop("s").A(K.Write, "s.r.*").A(K.Move, "$v").Flow("$v", "s");
        else { c.N(st = "drop s (old value; its drop writes self.r@follow)").Drop("s").A(K.Write, "s.r.*"); c.N("s = move $v").Assign("s").A(K.Move, "$v").Flow("$v", "s"); }
        c.N("inspect(s)").Use("s");
        return c.Error(st);
    }

    // ---- Round 6 (review r2) ----
    // f1: `x = f(x@ref)`, f retains its input in its result; x's Type needs no drop (no Drop statement in MIR).
    static Case SelfStore(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "dead" => "f1 dead store: `x = f(x@ref)`, x unused afterwards",
            "enum" => "f1 enum: `var x: E = .A(n: 42); x = pin(x@ref); match x .B(let r) => inspect(r@follow)`",
            _ => "f1: `x = f(x@ref)` (retains; no drop); `inspect(x)`",
        });
        c.N(kind == "enum" ? "var x: E = .A(n: 42)" : "var x = make()").Def("x");
        c.N("$a = x@ref").Def("$a").Borrow("Lx", "x", Ref, "$a");
        c.N(kind == "enum" ? "$r = pin($a)" : "$r = f($a)").Def("$r").Flow("$a", "$r");
        c.N("x = move $r").Assign("x").A(K.Move, "$r").Flow("$r", "x");
        if (kind == "enum") { c.N("match x .B(let r)").Def("r").A(K.Read, "x").Flow("x", "r"); c.N("inspect(r@follow)").A(K.Read, "r.*"); }
        else if (kind != "dead") c.N("inspect(x)").Use("x");
        return kind == "dead" ? c.Valid() : c.Error("x = move $r");
    }

    // f2: S {r: uniq/Array<i32>} whose drop clears self.r. 26a: `replace(s@uniq, with: …)`; 26b: `arr.clear()` on Array<S>.
    // The destroying call is a destroy access of its target, deep and exclusive (the Type observes), before `ended`.
    // Round 10 final (review r2 F1): the destroying call's destruction is A3's err(destroy) at the call (DestroyAt);
    // AsDrop holds rounds 6-10's encoding, a Drop on CS plus the drop's write through self.r.
    static Case Destroying(string id, bool clear) { var c = DestroyingCall(id, clear, false); c.AsDrop = DestroyingCall(id, clear, true); return c; }

    static Case DestroyingCall(string id, bool clear, bool asDrop)
    {
        var c = new Case(id, clear ? "f2: `c = arr[0].r@follow@uniq; arr.clear()` (Array<S>, S's drop writes self.r); `inspect(c@follow)`"
                                   : "f2: `c = s.r@follow[0]@uniq; replace(s@uniq, with: S(r: y@uniq))` (old s's drop clears self.r); `inspect(c@follow)`");
        c.N("var x = [1, 2]; var y = [3]").Def("x", "y");
        if (clear)
        {
            c.N("var arr: Array<S> = [S(r: x@uniq)]").Def("arr").Borrow("L1", "x", Uniq, "arr");
            c.N("let c = arr[0].r@follow@uniq").Def("c").Borrow("L2", "arr.[].r.*", Uniq, "c");
            c.N("$t = arr (clear receiver, reserved; destroying)").Def("$t").Borrow("Lt", "arr", Uniq, "$t", reserve: true, inPlace: true);
            var clr = c.N("arr.clear()").Activate("Lt");
            if (asDrop) clr.Drop("arr.[]").A(K.Write, "arr.[].r.*"); else clr.DestroyAt("arr.[]", "Lt");
            c.N("inspect(c@follow)").A(K.Read, "c.*");
            return c.Error("arr.clear()");
        }
        c.N("var s = S(r: x@uniq)").Def("s").Borrow("L1", "x", Uniq, "s.r");
        c.N("let c = s.r@follow[0]@uniq").Def("c").Borrow("L2", "s.r.*.[]", Uniq, "c");
        c.N("$t = s@uniq (reserved, replace target)").Def("$t").Borrow("Lt", "s", Uniq, "$t", reserve: true, inPlace: true);
        c.N("$v = S(r: y@uniq)").Def("$v").Borrow("Ly", "y", Uniq, "$v.r");
        var rpl = c.N("replace($t, with: $v)").Activate("Lt").Def("s");
        if (asDrop) rpl.Drop("s").A(K.Write, "s.r.*"); else rpl.DestroyAt("s", "Lt");
        rpl.Flow("$v", "s");
        c.N("inspect(c@follow)").A(K.Read, "c.*");
        return c.Error("replace($t, with: $v)");
    }

    // f3 (valid): S {r: ref/i32} has a drop; a shared reborrow through the shared field outlives s.
    static Case SharedField()
    {
        var c = new Case("27", "f3: `var v = other@ref; do: let s = S(r: x@ref); v = s.r@follow@ref` (S has a drop); `inspect(v)`");
        c.N("var x = 1; var other = 2").Def("x", "other");
        c.N("var v = other@ref").Def("v").Borrow("Lo", "other", Ref, "v");
        c.N("  let s = S(r: x@ref)").Def("s").Borrow("Lx", "x", Ref, "s.r");
        c.N("  v = s.r@follow@ref").Assign("v").Borrow("L2", "s.r.*", Ref, "v", sharedAt: 1);
        c.N("  (end of do: drop s)").Drop("s").Def("s");
        c.N("inspect(v)").Use("v");
        return c.Valid();
    }

    // f9: arguments of one call. An exclusive argument borrow is a reservation (§15.6.7); 28d is a stored borrow.
    static Case Args(string id, string kind)
    {
        var c = new Case(id, kind switch
        {
            "uniqref" => "f9: `g(a@uniq, a@ref)`",
            "refref" => "f9: `g(a@ref, a@ref)`",
            "fields" => "f9: `g(a.x@uniq, a.y@ref)`",
            _ => "f9: `let r = a@uniq; g(r, a@ref)` (r stored, not a reservation)",
        });
        c.N("var a = A(x: 1, y: 2)").Def("a");
        switch (kind)
        {
            case "uniqref":
                c.N("$1 = a@uniq (reserved)").Def("$1").Borrow("L1", "a", Uniq, "$1", reserve: true);
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Error("g($1, $2)");
            case "refref":
                c.N("$1 = a@ref").Def("$1").Borrow("L1", "a", Ref, "$1");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g($1, $2)").Use("$1", "$2");
                return c.Valid();
            case "fields":
                c.N("$1 = a.x@uniq (reserved)").Def("$1").Borrow("L1", "a.x", Uniq, "$1", reserve: true);
                c.N("$2 = a.y@ref").Def("$2").Borrow("L2", "a.y", Ref, "$2");
                c.N("g($1, $2)").Activate("L1").Use("$1", "$2");
                return c.Valid();
            default:
                c.N("let r = a@uniq").Def("r").Borrow("Lr", "a", Uniq, "r");
                c.N("$2 = a@ref").Def("$2").Borrow("L2", "a", Ref, "$2");
                c.N("g(r, $2)").Use("r", "$2");
                return c.Error("$2 = a@ref");
        }
    }

    // ---- 10: call reservations (§15.6.7) ----
    static Case Replace()
    {
        var c = new Case("10a", "`Kimi.Intrinsics.replace(p@uniq, with: p + 1)`");
        c.N("var p: i32 = 1").Def("p");
        c.N("$t = p@uniq (reserved)").Def("$t").Borrow("Lp", "p", Uniq, "$t", reserve: true);
        c.N("$w = p + 1").Def("$w").A(K.Read, "p");
        c.N("replace($t, with: $w)").Activate("Lp").Use("$t").Through("$t.*", "$w");
        return c.Valid();
    }

    static Case AppendRemove()
    {
        var c = new Case("10b", "`tasks.append(tasks.remove(0))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$ra = tasks (append receiver, reserved)").Def("$ra").Borrow("La", "tasks", Uniq, "$ra", reserve: true);
        c.N("$rr = tasks (remove receiver, reserved)").Def("$rr").Borrow("Lr", "tasks", Uniq, "$rr", reserve: true);
        c.N("$v = remove($rr, 0)").Activate("Lr").Use("$rr").Def("$v");
        c.N("append($ra, $v)").Activate("La").Use("$ra", "$v");
        return c.Error("$v = remove($rr, 0)");
    }

    static Case Insert()
    {
        var c = new Case("10c", "`tasks.insert(tasks.length, Task.init(9))`");
        c.N("var tasks: Array<Task> = []").Def("tasks");
        c.N("$r = tasks (insert receiver, reserved)").Def("$r").Borrow("Li", "tasks", Uniq, "$r", reserve: true);
        c.N("$n = tasks.length").Def("$n").A(K.Read, "tasks");
        c.N("$task = Task.init(9)").Def("$task");
        c.N("insert($r, $n, $task)").Activate("Li").Use("$r", "$n", "$task");
        return c.Valid();
    }

    static Case Swap()
    {
        var c = new Case("10d", "`Kimi.Intrinsics.swap(p@uniq, p@uniq)` (§15.7.1)");
        c.N("var p: i32 = 0").Def("p");
        c.N("$a = p@uniq (reserved)").Def("$a").Borrow("La", "p", Uniq, "$a", reserve: true);
        c.N("$b = p@uniq (reserved)").Def("$b").Borrow("Lb", "p", Uniq, "$b", reserve: true);
        c.N("swap($a, $b)").Activate("La").Activate("Lb").Use("$a", "$b");
        return c.Error("swap($a, $b)");
    }

    // ---- 11: unreachable code (§14.10.3) ----
    static Case Unreachable()
    {
        var c = new Case("11", "Loan conflict after `return` (assumed path)");
        c.N("var x = 1").Def("x");
        c.N("let r = x@uniq").Def("r").Borrow("L1", "x", Uniq, "r");
        c.N("return 0").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }

    static Case UnreachableJoin()
    {
        var c = new Case("11b", "two assumed predecessors disagree (union reading; intersection would accept)");
        c.N("var x = 1; var y = 2").Def("x", "y");
        c.N("var r = y@uniq").Def("r").Borrow("Ly", "y", Uniq, "r");
        c.N("if c").Goto("  r = x@uniq", "  r = y@uniq");
        c.N("  r = x@uniq").Assign("r").Borrow("Lx", "x", Uniq, "r");
        c.N("  return 0").Goto("exit").Assume("x = 3");
        c.N("  r = y@uniq").Assign("r").Borrow("Ly2", "y", Uniq, "r");
        c.N("  return 1").Goto("exit").Assume("x = 3");
        c.N("x = 3").Assign("x");
        c.N("inspect(r)").Use("r").Goto("exit");
        c.N("exit").Goto();
        return c.Error("x = 3");
    }
}


// ---- Reviewer (E13/E14 design review): M' encodings of the 132 family and new probes 134-135 ----
// Round 12: the operands of every operation are listed in evaluation order (Node.Input; Node.Operation for a non-Call). A case
// whose reviewer encoding is a form round 12 rejects keeps it as its other form: 134a and 134e the G123 MIR (Always: M′, which
// design A runs), 134d the shape-derived reservation (Shape), 135b A3 §4's intrinsic row (IntrinsicRow).
static class Reviewer
{
    const bool Uniq = true, Ref = false;

    // M': every PairAcquire is secured into its own temp where it is evaluated (a reservation there in the uniq view, a Copy in the
    // ref view); null where the case already has that form (132c-e; round 13: 132k, 132l), and 132j/m use their Secured encoding.
    public static Case PairAlways(string id)
    {
        if (id is "132c" or "132d" or "132e" or "132j" or "132k" or "132l" or "132m") return null; // round 13: 132k, 132l are built so
        bool uniqView = id is not ("132b" or "132i" or "132l" or "132n");
        var c = new Case(id, "M' " + id);
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        void Acq(string tmp, string place, string loan)
        {
            if (uniqView) c.N($"{tmp} = acquire {place} (secured: a reservation)").Def(tmp).Acquire(loan, place + ".*", tmp);
            else c.N($"{tmp} = acquire {place} (secured: a Copy)").Def(tmp).A(K.Read, place).Flow(place, tmp);
        }
        string call;
        switch (id)
        {
            case "132a": case "132b":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("$4 = v@ref").Def("$4").Borrow("L4", "v", Ref, "$4");
                Acq("$5", "v", "L5");
                var na = c.N(call = "inner2(move $4, move $5)").Input(K.Move, "$4").Input(K.Move, "$5"); if (uniqView) na.Activate("L5");
                return uniqView ? c.Error(call) : c.Valid();
            case "132f":
                Acq("$3", "p", "L3");
                Acq("$5", "p", "L5");
                c.N(call = "$4 = bumpS(move $5)").Def("$4").Input(K.Move, "$5").Activate("L5");
                c.N("inner7(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L3");
                return c.Error(call);
            case "132g":
                c.N("$3 = p.n + 1").Def("$3").A(K.Read, "p.*.n");
                Acq("$4", "p", "L4");
                c.N("inner8(move $3, move $4)").Input(K.Move, "$3").Input(K.Move, "$4").Activate("L4");
                return c.Valid();
            case "132h": case "132i":
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                Acq("$3", "v", "L3"); Acq("$4", "v", "L4");
                var nh = c.N(call = "Call inner9(v, v)").Input(K.Move, "$3").Input(K.Move, "$4"); if (uniqView) nh.Activate("L3").Activate("L4");
                return uniqView ? c.Error(call) : c.Valid();
            default: // 132n
                c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
                c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: 1);
                Acq("$3", "v", "L3");
                c.N("Call innerR(v, u)").Input(K.Move, "$3").Input(K.Read, "u");
                return c.Valid();
        }
    }

    public static Case Make(string id) => id switch
    {
        "134a" => With(Z1(id, false), always: Z1(id, true)),
        "134b" => Z1(id, true),
        "134c" => X2(id, false),
        "134d" => With(X2(id, false), shape: X2(id, true)),
        "134e" => With(V1(id, false), always: V1(id, true)),
        "134f" => V1(id, true),
        "134g" => W1(id),
        "135b" => With(Clone(id, false), row: Clone(id, true)),
        _ => Clone(id, false),
    };

    static Case With(Case c, Case always = null, Case shape = null, Case row = null) { c.Always = always; c.Shape = shape; c.IntrinsicRow = row; return c; }

    // 134a, 134b: z1, `var v = p; let u = v@follow@ref; _ = (v, u)`, uniq view; $t is dead after (a discard's shallow drop of a
    // non-observing Type is no use, A3 §5 use(H, ℓ), so no node follows). G123 leaves the PairAcquire at the Aggregate:
    // `$t = (acquire v, copy u)`; M′ secures it, unmarked (an aggregate operand): `$1 = acquire v` (a live Uniq), `$t = (move $1, copy u)`.
    static Case Z1(string id, bool m2)
    {
        var c = new Case(id, id == "134b" ? "z1 under M' for every operation: `_ = (v, u)` -> `$1 = acquire v; $t = (move $1, copy u)`"
            : "z1, uniq view: `var v = p; let u = v@follow@ref; _ = (v, u)` (G123's MIR `$t = (acquire v, copy u)`; $t dead; design A builds 134b's M′)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u");
        string at;
        if (m2)
        {
            c.N(at = "$1 = acquire v (secured: live Uniq)").Def("$1").Borrow("L1", "v.*", Uniq, "$1");
            c.N("$t = (move $1, copy u)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "u").Flow("u", "$t").Operation();
        }
        else c.N(at = "$t = (acquire v, copy u)").Def("$t").AcquireOp("Lt", "v.*", "$t").Input(K.Read, "u").Flow("u", "$t").Operation();
        return c.Error(at);
    }

    // 134c, 134d: x2, `inner11(do => v, peek(v@follow@ref))`, uniq view: `$a = acquire v; $b = &(*v); $c = peek(move $b);
    // inner11(move $a, move $c)`. The builder leaves a control-flow result's PairAcquire unmarked (a live Uniq, SPEC 15.6.7);
    // shape: A5 reads `$a = acquire v` moved into a Call as a reservation (x1's shape, 139a), the rejected option.
    static Case X2(string id, bool shape)
    {
        var c = new Case(id, (id == "134d" ? "x2, reservation derived from the MIR shape (design A: the marker, as 134c; ShapeReservation: the shape): "
            : "x2 (SPEC 15.6.7: a control-flow result is live): ")
            + "`inner11(do => v, peek(v@follow@ref))` (`$a = acquire v; $b = &(*v); $c = peek(move $b); inner11(move $a, move $c)`)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        if (shape) c.N("$a = acquire v (do => v; read as a reservation)").Def("$a").Acquire("La", "v.*", "$a");
        else c.N("$a = acquire v (do => v; live Uniq)").Def("$a").Borrow("La", "v.*", Uniq, "$a");
        c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
        c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
        var n = c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c"); if (shape) n.Activate("La");
        return c.Error("$b = &(*v)");
    }

    // 134e, 134f: v1, `let t = (v, v@follow); inspect(t)`, uniq view, T Copy; t live. G123: `t = (acquire v, copy (*v))`; M′,
    // unmarked: `$1 = acquire v` (a live Uniq), `t = (move $1, copy (*v))`.
    static Case V1(string id, bool m2)
    {
        var c = new Case(id, id == "134f" ? "v1 under M' for every operation: `$1 = acquire v; t = (move $1, copy (*v))`"
            : "v1, uniq view: `let t = (v, v@follow); inspect(t)` (G123's MIR `t = (acquire v, copy (*v))`; design A builds 134f's M′)");
        c.N("func outer<s/T>(p: s/T), uniq view").Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        string at;
        if (m2)
        {
            c.N("$1 = acquire v (secured: live Uniq)").Def("$1").Borrow("L1", "v.*", Uniq, "$1");
            c.N(at = "t = (move $1, copy (*v))").Def("t").Input(K.Move, "$1").Flow("$1", "t").Input(K.Read, "v.*").Flow("v.*", "t").Operation();
        }
        else c.N(at = "t = (acquire v, copy (*v))").Def("t").AcquireOp("Lt", "v.*", "t").Input(K.Read, "v.*").Flow("v.*", "t").Operation();
        c.N("inspect(t)").A(K.Read, "t");
        return c.Error(at);
    }

    // 134g: w1, `_ = (x@uniq, x)`: `$1 = &uniq x; $t = (move $1, copy x)`, $t dead (the old path rejects it at the read of x).
    static Case W1(string id)
    {
        var c = new Case(id, "w1: `_ = (x@uniq, x)` (`$1 = &uniq x; $t = (move $1, copy x)`; $t dead)");
        c.N("var x = 1").Def("x");
        c.N("$1 = x@uniq").Def("$1").Borrow("L1", "x", Uniq, "$1");
        c.N("$t = (move $1, copy x)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
        return c.Error("$t = (move $1, copy x)");
    }

    // 135a-c: Kimi.Intrinsics.clone<s/T>(value: ref/(s/T)) -> s/T, s is rc or arc. E14: the declaration retains the payload's
    // Loans (Content); A3 §4's intrinsic row (every operand -> result, Value) is 135b's IntrinsicRow form.
    static Case Clone(string id, bool valueRow)
    {
        var c = new Case(id, id switch
        {
            "135a" => "clone, Content row (its declaration): `let owner = makeRc(S(r: x@ref)); let c = clone(owner@ref); _ = owner@move; inspect(c)`",
            "135b" => "135a with A3 4's intrinsic row (every operand -> result, Value): c carries the borrow of owner (design A: E14's Content row, as 135a; IntrinsicAllOperands: the row)",
            _ => "135a's Content row is sound: `...; let c = clone(owner@ref); x = 5; inspect(c)`",
        });
        c.N("var x = 1").Def("x");
        c.N("let owner = makeRc(S(r: x@ref))").Def("owner").Borrow("Lx", "x", Ref, "owner");
        c.N("$t = owner@ref").Def("$t").Borrow("Lo", "owner", Ref, "$t");
        var cl = c.N("let c = clone(move $t)").Def("c").Input(K.Move, "$t");
        if (valueRow) cl.Flow("$t", "c"); else cl.Flow("$t.*", "c");
        if (id == "135c") { c.N("x = 5").Assign("x"); c.N("inspect(c)").A(K.Read, "c"); return c.Error("x = 5"); }
        c.N("_ = owner@move").A(K.Move, "owner");
        c.N("inspect(c)").A(K.Read, "c");
        return c.Valid();
    }
}

// ---- Round 12 (A3 E13 as the owner chose on 2026-10-18, option R1: A5 handles operand evaluation order) ----
// E13 (1): the operands an operation leaves at ℓ (Node.Input, in evaluation order: SPEC 12.2, a receiver or Value callee first)
// are checked on CS_i(ℓ); Node.Operation marks an operation other than a Call. E13 (3), M′: the builder secures every PairAcquire
// into its own temp; Acquire is a marked one (it produces an invocation input: a reservation in its temp, activated at its
// Call), a Uniq Borrow an unmarked one (a let, a control-flow result, an aggregate operand, a capture). G123: a Place operand is
// secured into a temp when a later operand of the same operation builds code; a Ref, a PairAcquire and a Call build code.
// give2(a: i32, b: i32); give2T<T>(a: T, b: i32); inner11<s/T>(a: s/T, b: i32); peek<T>(x: ref/T) -> i32; run<F>(f: F,
// k: i32); contract Plus { Self is Copy; func plus(self: Self, other: Self) -> i32 }; S3<T> {r: T, n: i32} with T = ref/i32.
static class Round12
{
    const bool Uniq = true, Ref = false;

    public static IEnumerable<Case> All() => [
        .. new[] { "136a", "136b", "136c", "136d", "136e", "136f", "136g", "136h" }.Select(Order),
        .. new[] { "137a", "137b", "137c", "137d", "137e" }.Select(Aggregate),
        .. new[] { "138a", "138b", "138c", "138d" }.Select(c => { var k = Capture(c); k.Built = CaptureBuilt(c); return k; }),
        .. new[] { "139a", "139b", "139c", "139d" }.Select(Marker),
        .. new[] { "140a", "140b", "140c", "140d", "140e", "140f", "140g" }.Select(OwnValue)];

    // 136: evaluation order (the E13 review's o1-o4, both orders; a by-value receiver first).
    static Case Order(string id)
    {
        bool rFirst = id is "136a" or "136c" or "136e" or "136h";
        var (op, dest) = id switch
        {
            "136a" or "136b" => ("give2", (string)null), "136c" or "136d" => ("t = ", "t"), "136e" or "136f" => ("s = Add", "s"), _ => ("k = plus", "k"),
        };
        var args = rFirst ? "copy (*r), copy x" : "copy x, copy (*r)";
        var label = dest == "t" ? $"t = ({args})" : $"{op}({args})";
        var c = new Case(id, id switch
        {
            "136a" => "o1: `let r = x@uniq; give2(r@follow, x)` (SPEC 12.2: r is not used after its operand; the old path accepts)",
            "136b" => "o2: `let r = x@uniq; give2(x, r@follow)` (x is read while r is still to be read; old path: ComparisonLoanConflict_Kd at x)",
            "136c" => "o3: `let r = x@uniq; let t = (r@follow, x); inspect(t)` (t live)",
            "136d" => "o3 reversed: `let t = (x, r@follow); inspect(t)`",
            "136e" => "o4: `let r = x@uniq; let s = r@follow + x`",
            "136f" => "o4 reversed: `let s = x + r@follow`",
            "136g" => "receiver first: `var x = x0; let r = x@uniq; let k = x.plus(r@follow)`, T is Plus (a Copy receiver)",
            _ => "receiver first: `var x = x0; let r = x@uniq; let k = r@follow.plus(x)`",
        } + $" (`{label}`)");
        c.N(id is "136g" or "136h" ? "var x = x0 (T is Plus, Copy)" : "var x = 1").Def("x");
        c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        var n = c.N(label);
        if (rFirst) n.Input(K.Read, "r.*").Input(K.Read, "x"); else n.Input(K.Read, "x").Input(K.Read, "r.*");
        if (dest != null) n.Def(dest); // an i32 result: plus and give2 retain nothing, an Add carries no Loan
        if (dest is "t" or "s") n.Operation();
        if (dest == "t") { n.Flow("r.*", "t").Flow("x", "t"); c.N("inspect(t)").A(K.Read, "t"); }
        return rFirst ? c.Valid() : c.Error(label);
    }

    // 137: aggregates (the E13 review's w1-w3; 134g is w1 itself).
    static Case Aggregate(string id)
    {
        var c = new Case(id, id switch
        {
            "137a" => "w1's counterpart: `_ = (x, x@uniq)` (G123 secures x: `$1 = copy x; $2 = &uniq x; $t = (move $1, move $2)`; SPEC 12.2: x is read before the Borrow, and its i32 Copy carries no Loan)",
            "137b" => "w1 with the tuple used: `let t = (x@uniq, x); inspect(t)` (`$1 = &uniq x; t = (move $1, copy x)`)",
            "137c" => "w3: `let r = x@uniq; _ = (r, x)` (`$1 = &uniq (*r)`, a bare acquisition live when formed, SPEC 15.6.7; `$t = (move $1, copy x)`)",
            "137d" => "w3's counterpart, a dead aggregate that reads only: `let r = x@ref; _ = (r, x)` (`$t = (copy r, copy x)`)",
            _ => "`let r = x@uniq; _ = (x, r)` (G123 secures x: `$1 = copy x; $2 = &uniq (*r); $t = (move $1, move $2)`)",
        });
        c.N("var x = 1").Def("x");
        string at = null;
        switch (id)
        {
            case "137a":
                c.N("$1 = copy x").Def("$1").A(K.Read, "x").Flow("x", "$1");
                c.N("$2 = &uniq x").Def("$2").Borrow("L2", "x", Uniq, "$2");
                c.N("$t = (move $1, move $2)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Move, "$2").Flow("$2", "$t").Operation();
                break;
            case "137b":
                c.N("$1 = &uniq x").Def("$1").Borrow("L1", "x", Uniq, "$1");
                c.N(at = "t = (move $1, copy x)").Def("t").Input(K.Move, "$1").Flow("$1", "t").Input(K.Read, "x").Flow("x", "t").Operation();
                c.N("inspect(t)").A(K.Read, "t");
                break;
            case "137c":
                c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
                c.N("$1 = &uniq (*r)").Def("$1").Borrow("L1", "r.*", Uniq, "$1");
                c.N(at = "$t = (move $1, copy x)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
                break;
            case "137d":
                c.N("let r = x@ref").Def("r").Borrow("Lr", "x", Ref, "r");
                c.N("$t = (copy r, copy x)").Def("$t").Input(K.Read, "r").Flow("r", "$t").Input(K.Read, "x").Flow("x", "$t").Operation();
                break;
            default: // 137e
                c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
                c.N(at = "$1 = copy x").Def("$1").A(K.Read, "x").Flow("x", "$1");
                c.N("$2 = &uniq (*r)").Def("$2").Borrow("L2", "r.*", Uniq, "$2");
                c.N("$t = (move $1, move $2)").Def("$t").Input(K.Move, "$1").Flow("$1", "$t").Input(K.Move, "$2").Flow("$2", "$t").Operation();
                break;
        }
        return at == null ? c.Valid() : c.Error(at);
    }

    // 138: Closure captures with a PairAcquire (SPEC 7.7: explicit entries execute left to right; a capture's PairAcquire is
    // unmarked, a live Uniq in the uniq view, SPEC 15.6.7: no reservation passes through captures).
    static Case Capture(string id)
    {
        bool uniqView = id != "138b";
        var c = new Case(id, (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "138a" => "z3: `var v = p; let u = v@follow@ref; let c = func [v, u] () => ()` (`$1 = acquire v`, unmarked; `c = Closure(move $1, copy u)`)",
            "138b" => "z3: `var v = p; let u = v@follow@ref; let c = func [v, u] () => ()` (`$1 = acquire v`: a Copy)",
            "138c" => "the other capture order: `let c = func [u, v] () => ()` (G123 secures u: `$1 = copy u; $2 = acquire v; c = Closure(move $1, move $2)`)",
            _ => "a capture inside an invocation input: `run(func [v] () => (), peek(v@follow@ref))` (`$1 = acquire v`, unmarked; `$c = Closure(move $1)`; `$b = &(*v)`; compare 139a)",
        });
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        void Acq(string tmp, string loan)
        {
            if (uniqView) c.N($"{tmp} = acquire v (unmarked: a capture; live Uniq)").Def(tmp).Borrow(loan, "v.*", Uniq, tmp);
            else c.N($"{tmp} = acquire v (a Copy)").Def(tmp).A(K.Read, "v").Flow("v", tmp);
        }
        if (id == "138d")
        {
            Acq("$1", "L1");
            c.N("$c = Closure(move $1)").Def("$c").Input(K.Move, "$1").Flow("$1", "$c").Operation();
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
            c.N("$r = peek(move $b)").Def("$r").Input(K.Move, "$b");
            c.N("run(move $c, move $r)").Input(K.Move, "$c").Input(K.Move, "$r");
            return c.Error("$b = &(*v)");
        }
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
        if (id == "138c")
        {
            c.N("$1 = copy u").Def("$1").A(K.Read, "u").Flow("u", "$1");
            Acq("$2", "L2");
            c.N("c = Closure(move $1, move $2)").Def("c").Input(K.Move, "$1").Flow("$1", "c").Input(K.Move, "$2").Flow("$2", "c").Operation();
            return c.Error("$2 = acquire v (unmarked: a capture; live Uniq)");
        }
        Acq("$1", "L1");
        c.N("c = Closure(move $1, copy u)").Def("c").Input(K.Move, "$1").Flow("$1", "c").Input(K.Read, "u").Flow("u", "c").Operation();
        return uniqView ? c.Error("$1 = acquire v (unmarked: a capture; live Uniq)") : c.Valid();
    }

    // Round 13: today's builder builds captures without G123 or M′ (the second design review's MIR dump p1/a138: outerA
    // `Closure(acquire b0 _2, copy _3)`, outerC `Closure(copy _3, acquire b0 _2)`): the PairAcquire stays at the Closure as its
    // operand, a live Uniq formed there in the uniq view (AcquireOp), a Copy in the ref view. Case 138's own nodes are E13 (2)/(4)'s.
    static Case CaptureBuilt(string id)
    {
        bool uniqView = id != "138b";
        var c = new Case(id, "today's builder: captures at the Closure");
        c.N("func outer<s/T>(p: s/T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
        Node Acq(Node n, string into, string loan) => uniqView ? n.AcquireOp(loan, "v.*", into) : n.Input(K.Read, "v").Flow("v", into);
        if (id == "138d")
        {
            Acq(c.N("$c = Closure(acquire v)").Def("$c"), "$c", "L1").Operation();
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b");
            c.N("$r = peek(move $b)").Def("$r").Input(K.Move, "$b");
            c.N("run(move $c, move $r)").Input(K.Move, "$c").Input(K.Move, "$r");
            return c.Error("$b = &(*v)");
        }
        c.N("let u = v@follow@ref").Def("u").Borrow("Lu", "v.*", Ref, "u", sharedAt: uniqView ? 0 : 1);
        string at;
        if (id == "138c") Acq(c.N(at = "c = Closure(copy u, acquire v)").Def("c").Input(K.Read, "u").Flow("u", "c"), "c", "L2").Operation();
        else Acq(c.N(at = "c = Closure(acquire v, copy u)").Def("c"), "c", "L1").Input(K.Read, "u").Flow("u", "c").Operation();
        return uniqView ? c.Error(at) : c.Valid();
    }

    // 139: the marker (E13 (3)): x1 (x2 is 134c) and an explicit case adaptation at an input, against its unmarked `let` form.
    static Case Marker(string id)
    {
        bool uniqView = id != "139b";
        var c = new Case(id, (uniqView ? "uniq view: " : "ref view: ") + id switch
        {
            "139a" or "139b" => "x1: `var v = p; inner11(v, peek(v@follow@ref))` (`$a = acquire v`, marked; `$b = &(*v)`; `$c = peek(move $b)`; `inner11(move $a, move $c)`)",
            "139c" => "an explicit case adaptation at an input: `var x = item@move; inner11(x@(s/T), peek(x@ref))` (`$a = x@(s/T)`, marked)",
            _ => "the adaptation stored first: `let a = x@(s/T); let k = peek(x@ref); inner11(a, k)` (`a = x@(s/T)`, unmarked: a let, live when formed)",
        });
        c.N("func outer<s/T>(p: s/T, item: T), " + (uniqView ? "uniq view" : "ref view")).Param("Lp", "p");
        if (id is "139a" or "139b")
        {
            c.N("var v = p").Def("v").A(K.Move, "p").Flow("p", "v");
            if (uniqView) c.N("$a = acquire v (marked: a reservation)").Def("$a").Acquire("La", "v.*", "$a");
            else c.N("$a = acquire v (a Copy)").Def("$a").A(K.Read, "v").Flow("v", "$a");
            c.N("$b = &(*v)").Def("$b").Borrow("Lb", "v.*", Ref, "$b", sharedAt: uniqView ? 0 : 1);
            c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
            var n = c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c"); if (uniqView) n.Activate("La");
            return c.Valid();
        }
        c.N("var x = item@move").Def("x");
        if (id == "139c")
        {
            c.N("$a = x@(s/T) (marked: a reservation)").Def("$a").Acquire("La", "x", "$a");
            c.N("$b = &x").Def("$b").Borrow("Lb", "x", Ref, "$b");
            c.N("$c = peek(move $b)").Def("$c").Input(K.Move, "$b");
            c.N("inner11(move $a, move $c)").Input(K.Move, "$a").Input(K.Move, "$c").Activate("La");
            return c.Valid();
        }
        c.N("let a = x@(s/T) (unmarked: a let; live Uniq)").Def("a").Borrow("La", "x", Uniq, "a");
        c.N("$b = &x").Def("$b").Borrow("Lb", "x", Ref, "$b");
        c.N("let k = peek(move $b)").Def("k").Input(K.Move, "$b");
        c.N("$a2 = acquire a (marked: a reservation)").Def("$a2").Acquire("La2", "a.*", "$a2");
        c.N("inner11(move $a2, copy k)").Input(K.Move, "$a2").Input(K.Read, "k").Activate("La2");
        return c.Error("$b = &x");
    }

    // 140: the operand's own value (round 12, finding 1). `var z = 0; var s = S3(r: z@ref, n: 1); s.r = s.n@ref` makes s carry
    // its own Loan Ln on s.n; Moving s as an operand moves a value the operation then uses (SPEC 15.6: checked against the Loans
    // of the values used after the Move; rustc E0505). The old path rejects 140a-140c, 140e and 140f at `s@move`. 140e-140g
    // (planning session on finding 2): a single-operand Assign `let t = s@move` uses s's value at ℓ, t dead or not; 140g's s
    // carries a Loan on another root only (`let r2 = y@ref; s.r = r2`).
    static Case OwnValue(string id)
    {
        var c = new Case(id, "own value: `var s = S3(r: z@ref, n: 1); s.r = s.n@ref; " + id switch
        {
            "140a" => "give2T(s@move, 1)` (round 13, the builder's MIR: `$7 = move s; give2T(move $7, const 1)`; give2T retains nothing)",
            "140b" => "_ = (s@move, 1)` (round 13: `$7 = move s; $t = (move $7, const 1)`; $t dead)",
            "140c" => "let t = (s@move, 1); inspect(t)` (round 13: `$7 = move s; t = (move $7, const 1)`, t live)",
            "140d" => "give2(s.n, s.n)` (`give2(copy s.n, copy s.n)`: reads only)",
            "140e" => "let t = s@move` (`t = move s`, a single-operand Use; t dead)",
            "140f" => "let t = s@move; inspect(t)` (t used)",
            _ => "…` with `let r2 = y@ref; s.r = r2; let t = s@move` instead (s carries Ly on another root)",
        });
        c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
        if (id == "140g")
        {
            c.N("var y = 0").Def("y"); c.N("let r2 = y@ref").Def("r2").Borrow("Ly", "y", Ref, "r2");
            c.N("s.r = r2").Assign("s.r").A(K.Read, "r2").Flow("r2", "s.r");
            c.N("let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t");
            return c.Valid();
        }
        c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
        string at;
        switch (id)
        {
            // Round 13 (the second design review's MIR dump p2/b140): an explicit `@move` builds code, so the Move is its own Use
            // into a temp the operation reads (round 12 left `move s` at the operation; the review's r13m140a/b are these forms).
            case "140a": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("give2T(move $7, const 1)").Input(K.Move, "$7"); break;
            case "140b": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("$t = (move $7, const 1)").Def("$t").Input(K.Move, "$7").Flow("$7", "$t").Operation(); break;
            case "140c": c.N(at = "$7 = move s").Def("$7").A(K.Move, "s").Flow("s", "$7"); c.N("t = (move $7, const 1)").Def("t").Input(K.Move, "$7").Flow("$7", "t").Operation(); c.N("inspect(t)").A(K.Read, "t"); break;
            case "140e": case "140f": c.N(at = "let t = s@move").Def("t").A(K.Move, "s").Flow("s", "t"); if (id == "140f") c.N("inspect(t)").A(K.Read, "t"); break;
            default: c.N("give2(copy s.n, copy s.n)").Input(K.Read, "s.n").Input(K.Read, "s.n"); return c.Valid();
        }
        return c.Error(at);
    }
}


// ---- Round 13 (A3 E13 rewritten after its second design review; the owner's "one rule for every access") ----
// The second design review's cases (its r13 ids, taken as written except n1 and n2, which run E13 (2)'s order with the review's
// MIR as Case.Built; its m140a and m140b are 140a and 140b as corrected below), and this round's d3, s1 and s2. Old-path
// verdicts are the review's (Kimi.dll check, legacy pipeline; d3 from its ledger record). S3<T> {r: T, n: i32}, S4 = S3;
// Q2 {a} {r: uniq/i32 during a, n: i32} whose drop writes `self.r@follow = 0`; S7 {a} {r: uniq/i32, q: ref/i32 during a};
// give(a: uniq/i32, b: i32); give2(a: i32, b: i32); G {level: i32 with set(value: i32)}; S5 {r: ref/i32}; W5 {a} {p: uniq/S5,
// q: uniq/(ref/i32) during a}. b1 and b2 are this round's too (the planning session's E3 bound).
static class R13
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [D1(), D2(), D3(), RF1(), RF2(), U1(), U2(), N1(), N2(), A1(), A2(), C1(), Setter("r13s1", false), Setter("r13s2", true), Past("r13b1", false), Past("r13b2", true)];

    // a1, a2: an activation takes the values of the other operands, not their holders (E13 (1); CS_call's live_in(ℓ) rejects a1).
    static Case A(string id, bool used)
    {
        var c = new Case(id, "activation after a shared read through a dead reference: `let r = y@ref; give(y@uniq, r@follow)`" + (used ? "; inspect(r)" : "") + (used ? " (error)" : " (valid; old path valid)"));
        c.N("var y = 1").Def("y");
        c.N("let r = y@ref").Def("r").Borrow("Lr", "y", Ref, "r");
        c.N("$4 = &uniq two-phase y").Def("$4").Borrow("L4", "y", Uniq, "$4", reserve: true);
        c.N("give(move $4, copy (*r))").Input(K.Move, "$4").Input(K.Read, "r.*").Activate("L4");
        if (used) c.N("inspect(r)").A(K.Read, "r");
        return used ? c.Error("give(move $4, copy (*r))") : c.Valid();
    }
    static Case A1() => A("r13a1", false);
    static Case A2() => A("r13a2", true);

    // c1: j = i at a Closure (a capture's own value): today's builder and E13 (2) both build `Closure(move s)`.
    static Case C1()
    {
        var c = new Case("r13c1", "capture Move of a self-ref holder: `s.r = s.n@ref; let c = func [s@move] () => ()` (MIR c = Closure(move s), c dead; old path error)");
        c.N("var z = 0").Def("z"); c.N("var s = S3(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
        c.N("s.r = s.n@ref").Assign("s.r").Borrow("Ln", "s.n", Ref, "s.r");
        c.N("c = Closure(move s)").Def("c").Input(K.Move, "s").Flow("s", "c").Operation();
        return c.Error("c = Closure(move s)");
    }

    // d1-d3: an observing destruction of X takes val(X) (SPEC 15.6, 15.6.6).
    static Case D1()
    {
        var c = new Case("r13d1", "self-ref observing drop: `var q = Q2(r: z@uniq); q.r = q.n@uniq` (scope end: drop q writes through q.r); old path: error");
        c.N("var z = 0").Def("z");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = q.n@uniq").Assign("q.r").Borrow("Ln", "q.n", Uniq, "q.r");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Error("drop q (scope end)");
    }
    static Case D2()
    {
        var c = new Case("r13d2", "d1's counterpart: `q.r = y@uniq` (another root), drop q at scope end");
        c.N("var z = 0").Def("z"); c.N("var y = 0").Def("y");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = y@uniq").Assign("q.r").Borrow("Ly", "y", Uniq, "q.r");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Valid();
    }
    // d3 (the review's probe d3; MIR `drop _2; _2 = move _7`): the old value's drop at a whole assignment, q dead in between.
    static Case D3()
    {
        var c = new Case("r13d3", "d1 by reassignment: `q.r = q.n@uniq; q = Q2(r: z2@uniq)` (`drop q; q = move $7`: the old value's drop writes through q.r); old path: valid (wrong)");
        c.N("var z = 0; var z2 = 0").Def("z", "z2");
        c.N("var q = Q2(r: z@uniq)").Def("q").Borrow("Lz", "z", Uniq, "q.r");
        c.N("q.r = q.n@uniq").Assign("q.r").Borrow("Ln", "q.n", Uniq, "q.r");
        c.N("$7 = Q2(r: z2@uniq)").Def("$7").Borrow("L7", "z2", Uniq, "$7");
        c.N("drop q (old value)").Drop("q").A(K.Write, "q.r.*");
        c.N("q = move $7").Assign("q").A(K.Move, "$7").Flow("$7", "q");
        c.N("drop q (scope end)").Drop("q").A(K.Write, "q.r.*");
        return c.Error("drop q (old value)");
    }

    // rf1, rf2: a Ref's borrow of X takes its R2 parents whatever its destination's liveness (supersedes R4-M57's seed).
    static Case RF(string id, bool live)
    {
        var c = new Case(id, "Ref through a self-ref holder: `var s = S7(r: z@uniq, q: w@ref); s.q = s.r@follow@ref; let t = s.r@follow@uniq`" + (live ? "; inspect(t)" : " (t dead)") + "; old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var w = 0").Def("w");
        c.N("var s = S7(r: z@uniq, q: w@ref)").Def("s").Borrow("Lz", "z", Uniq, "s.r").Borrow("Lw", "w", Ref, "s.q");
        c.N("s.q = s.r@follow@ref").Assign("s.q").Borrow("Lq", "s.r.*", Ref, "s.q");
        c.N("let t = s.r@follow@uniq").Def("t").Borrow("Lt", "s.r.*", Uniq, "t");
        if (live) c.N("inspect(t)").A(K.Read, "t.*");
        return c.Error("let t = s.r@follow@uniq");
    }
    static Case RF1() => RF("r13rf1", false);
    static Case RF2() => RF("r13rf2", true);

    // u1, u2: j = i at a Call and at a single-operand Use (MIR `give2(copy _2.1, const 1)`, `_6 = copy _2.1`).
    static Case U1()
    {
        var c = new Case("r13u1", "Copy operand of a self-ref holder at the Call: `s.r = s.n@uniq; give2(s.n, 1)` (MIR give2(copy s.n, const 1)); old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var s = S4(r: z@uniq, n: 1)").Def("s").Borrow("Lz", "z", Uniq, "s.r");
        c.N("s.r = s.n@uniq").Assign("s.r").Borrow("Ln", "s.n", Uniq, "s.r");
        c.N("give2(copy s.n, const 1)").Input(K.Read, "s.n");
        return c.Error("give2(copy s.n, const 1)");
    }
    static Case U2()
    {
        var c = new Case("r13u2", "single-operand Copy of a self-ref holder: `s.r = s.n@uniq; let t = s.n` (t dead); old path: valid (wrong)");
        c.N("var z = 0").Def("z"); c.N("var s = S4(r: z@uniq, n: 1)").Def("s").Borrow("Lz", "z", Uniq, "s.r");
        c.N("s.r = s.n@uniq").Assign("s.r").Borrow("Ln", "s.n", Uniq, "s.r");
        c.N("let t = s.n").Def("t").A(K.Read, "s.n").Flow("s.n", "t");
        return c.Error("let t = s.n");
    }

    // n1, n2: named arguments in another order (SPEC 10.1.1). E13 (2): a Place operand evaluated before an operand that precedes it
    // in operand order is secured where it is evaluated. Built: today's builder passes them by slot (`give2(copy x, copy (*r))`).
    static Case N(string id)
    {
        bool n1 = id == "r13n1";
        var c = new Case(id, n1 ? "`let r = x@uniq; give2(b: r@follow, a: x)`: evaluated r.*, then x (valid, old path valid); E13 (2): `$1 = copy (*r); give2(copy x, move $1)`"
            : "`let r = x@uniq; give2(b: x, a: r@follow)`: evaluated x, then r.* (error at x, old path error); E13 (2): `$1 = copy x; give2(copy (*r), move $1)`");
        c.N("var x = 1").Def("x"); c.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        if (n1) { c.N("$1 = copy (*r) (b, secured)").Def("$1").A(K.Read, "r.*").Flow("r.*", "$1"); c.N("give2(copy x, move $1)").Input(K.Read, "x").Input(K.Move, "$1"); c.Valid(); }
        else { c.N("$1 = copy x (b, secured)").Def("$1").A(K.Read, "x").Flow("x", "$1"); c.N("give2(copy (*r), move $1)").Input(K.Read, "r.*").Input(K.Move, "$1"); c.Error("$1 = copy x (b, secured)"); }
        var b = new Case(id, "today's builder, by slot");
        b.N("var x = 1").Def("x"); b.N("let r = x@uniq").Def("r").Borrow("Lr", "x", Uniq, "r");
        if (n1) { b.N("give2(copy x, copy (*r))").Input(K.Read, "x").Input(K.Read, "r.*"); b.Valid(); }
        else { b.N("give2(copy (*r), copy x)").Input(K.Read, "r.*").Input(K.Read, "x"); b.Error("give2(copy (*r), copy x)"); }
        c.Built = b;
        return c;
    }
    static Case N1() => N("r13n1");
    static Case N2() => N("r13n2");

    // b1, b2: a borrow past a shared Deref takes its R2 parents as E3 bounds them (SPEC 15.6.3: it derives only from the
    // reference of its last shared Deref, s.r, and carries its Loans). Reaching s.r reads w.p, but w's value is not used
    // after ℓ in b1, so w's Lq (a uniq borrow of s.r, held in w.q) is not checked; in b2 w is used after ℓ. ParentsEveryLayer
    // takes w's Loans too (layer 0) and rejects b1.
    static Case Past(string id, bool used)
    {
        var c = new Case(id, "Ref past a shared Deref: `var s = S5(r: y@ref); var w = W5(p: s@uniq, q: o@uniq); w.q = w.p@follow.r@uniq; let t = w.p@follow.r@follow@ref; inspect(t)"
            + (used ? "; inspect(w.q)`" : "` (w dead after t)") + "; old path not probed");
        c.N("var y = 1; var o: ref/i32 = y@ref").Def("y", "o").Borrow("Ly0", "y", Ref, "o");
        c.N("var s = S5(r: y@ref)").Def("s").Borrow("Ly", "y", Ref, "s.r");
        c.N("var w = W5(p: s@uniq, q: o@uniq)").Def("w").Borrow("Ls", "s", Uniq, "w.p").Borrow("Lo", "o", Uniq, "w.q");
        c.N("w.q = w.p@follow.r@uniq").Assign("w.q").Borrow("Lq", "w.p.*.r", Uniq, "w.q");
        c.N("let t = w.p@follow.r@follow@ref").Def("t").Borrow("Lt", "w.p.*.r.*", Ref, "t", sharedAt: 2);
        c.N("inspect(t)").Use("t");
        if (!used) return c.Valid();
        c.N("inspect(w.q)").A(K.Read, "w.q");
        return c.Error("let t = w.p@follow.r@follow@ref");
    }

    // s1, s2: a setter's value is evaluated before its receiver (SPEC 13.7.1), so E13 (2) secures a Place value before the
    // receiver's reservation: `$1 = copy (*r).level; $5 = &uniq two-phase g; level.Set(move $5, move $1)`. Today's builder for a
    // Place value is not probed (a Call value is built first: the review's bsetter probe), so there is no Built form.
    static Case Setter(string id, bool used)
    {
        var c = new Case(id, "setter value first: `var g = G(); let r = g@uniq; g.level = r@follow.level`" + (used ? "; inspect(r)" : "") + " (E13 (2): the value secured before the receiver's reservation)");
        c.N("var g = G()").Def("g");
        c.N("let r = g@uniq").Def("r").Borrow("Lr", "g", Uniq, "r");
        c.N("$1 = copy (*r).level (value, secured)").Def("$1").A(K.Read, "r.*.level").Flow("r.*.level", "$1");
        c.N("$5 = &uniq two-phase g (receiver)").Def("$5").Borrow("L5", "g", Uniq, "$5", reserve: true);
        c.N("level.Set(move $5, move $1)").Input(K.Move, "$5").Input(K.Move, "$1").Activate("L5");
        if (!used) return c.Valid();
        c.N("inspect(r)").A(K.Read, "r");
        return c.Error("$5 = &uniq two-phase g (receiver)", "level.Set(move $5, move $1)");
    }
}

// ---- Round 15 (PLAN G148, designer a): a Call's store row retains(c, i, κ, store j) stores ret(i, κ) into the writable storage of
// input j, which TyCtxt.Retains defines as every invariant position below the input's first writable one, past any layer (DECISIONS
// BU-1), so storage behind several exclusive layers. A5 at 806110fc stores into refs(val(a_j)) only (the design here). Callees:
// put2(p: uniq/(uniq/(ref/i32 during a) during c), y: ref/i32 during a) writes y at **p, rows `0 Content store 0, 1 Value store 0`;
// put3 one layer more; put(slot: uniq/(ref/i32 during a), value: ref/i32 during a) writes *slot; putS(p: uniq/(ref/(ref/i32 during
// a) during c), y: ref/i32 during a) (its rows by its contract; past the shared layer nothing is writable); mkU(r: ref/(ref/i32 during
// a) during c) -> uniq/(ref/i32 during a) during c (retains r, Value); WQ2 {a, c} {q: uniq/(ref/i32 during a) during c}, setW(wv:
// uniq/WQ2{k}, y: ref/i32 during k.a) writes wv.q@follow, touchW(wv: uniq/WQ2) (its self-row only); putR(p: uniq/(raw/(ref/i32 during
// a)), y: ref/i32 during a) initializes p's pointee with y; get2/get3 return the value at the bottom (a Content result row). '#' locals
// hold no Loan, '%' is a raw-pointer local; neither is a holder. SPEC verdicts: SPEC 15.6 (a call writes the storage its contract
// lets it write like a write through a reference: every holder that layer's reference reaches), 15.6.2 (past a shared layer only
// reads), 7.2.3 (a default only inspects a preceding exclusive slot).
static class R14A
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => Raw().Select(R14Ann.A);
    static IEnumerable<Case> Raw() => [Repro("r14a"), Repro("r14b"), Repro("r14c"), Repro("r14d"), Repro("r14e"), Shared("r14f"), Shared("r14g"),
        Shared("r14h"), Hidden("r14i"), Hidden("r14j"), RawPointee("r14k"), Unknown("r14l"), Input("r14m"), Input("r14n"), Input("r14o"), Within("r14p"), Within("r14q")];

    // a: the reproducer; b: its direct twin; c: put3 (depth 3); d: put (depth 1, the control); e: a without `b = 5` (option X's probe).
    static Case Repro(string id)
    {
        var c = new Case("r14A-" + id[3..], id switch
        {
            "r14a" => "G148: `var w = x@ref; var s1 = w@uniq; put2(s1@uniq, b@ref); b = 5; inspect(w)`",
            "r14b" => "direct twin: `…; var s2 = s1@uniq; s2@follow@follow = b@ref; b = 5; inspect(w)`",
            "r14c" => "depth 3: `…; var s2 = s1@uniq; put3(s2@uniq, b@ref); b = 5; inspect(w)`",
            "r14d" => "depth 1 (control): `var w = x@ref; put(w@uniq, b@ref); b = 5; inspect(w)`",
            _ => "r14a without the write: `put2(s1@uniq, b@ref); inspect(w); w = x@ref; inspect(w)` (SPEC's per-argument letter would make w carry s1's Loan on w)",
        });
        c.N("var x = 1").Def("#x"); c.N("var b = 2").Def("#b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        if (id == "r14d")
        {
            c.N("$p = &uniq two-phase w").Def("$p").Borrow("Lp", "w", Uniq, "$p", reserve: true);
            c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
            c.N("put(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, 1).Store("$p", "$y", false, 1);
        }
        else
        {
            c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
            if (id is "r14b" or "r14c") c.N("var s2 = s1@uniq").Def("s2").Borrow("Ls2", "s1", Uniq, "s2");
            if (id == "r14b")
            {
                c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
                c.N("s2@follow@follow = move $y").Through("s2.*.*", "$y");
            }
            else
            {
                var (arg, depth, f) = id == "r14c" ? ("s2", 3, "put3") : ("s1", 2, "put2");
                c.N($"$p = &uniq two-phase {arg}").Def("$p").Borrow("Lp", arg, Uniq, "$p", reserve: true);
                c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
                c.N($"{f}(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, depth).Store("$p", "$y", false, depth);
            }
        }
        if (id == "r14e")
        {
            c.N("inspect(copy w)").Input(K.Read, "w");
            c.N("w = x@ref").Assign("w").Borrow("Lx2", "#x", Ref, "w");
            c.N("inspect(copy w) (again)").Input(K.Read, "w");
            return c.Valid();
        }
        c.N("b = 5").Assign("#b");
        c.N("inspect(copy w)").Input(K.Read, "w");
        return c.Error("b = 5");
    }

    // f: putS through a shared layer (option A's over-strictness probe); g: the same with a shared reborrow through a uniq (its parent
    // Loan is exclusive: the mode gate's over-strictness); h: put2 on a uniq whose only direct Loan is shared (mkU), which reaches w
    // by SPEC 15.6's rule (the mode gate's unsound acceptance).
    static Case Shared(string id)
    {
        var c = new Case("r14A-" + id[3..], id switch
        {
            "r14f" => "shared layer: `var w = x@ref; var s = w@ref; putS(s@uniq, b@ref); b = 5; inspect(w)` (w lies past a shared layer)",
            "r14g" => "shared reborrow: `var u = w@uniq; var s = u@follow@ref; putS(s@uniq, b@ref); b = 5; inspect(w)` (s carries u's uniq Loan on w)",
            _ => "uniq from shared: `var u = mkU(w@ref); put2(u@uniq, b@ref); b = 5; inspect(w)` (u's only direct Loan is shared and names w)",
        });
        c.N("var x = 1").Def("#x"); c.N("var b = 2").Def("#b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        string arg;
        if (id == "r14f") { c.N("var s = w@ref").Def("s").Borrow("Ls", "w", Ref, "s"); arg = "s"; }
        else if (id == "r14g")
        {
            c.N("var u = w@uniq").Def("u").Borrow("Lu", "w", Uniq, "u");
            c.N("var s = u@follow@ref").Def("s").Borrow("Lsc", "u.*", Ref, "s"); arg = "s";
        }
        else
        {
            c.N("$r = w@ref").Def("$r").Borrow("Lr", "w", Ref, "$r");
            c.N("var u = mkU(move $r) (retains r)").Def("u").Input(K.Move, "$r").Flow("$r", "u").Flow("$r.*", "u"); arg = "u";
        }
        c.N($"$p = &uniq two-phase {arg}").Def("$p").Borrow("Lp", arg, Uniq, "$p", reserve: true);
        c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
        var depth = id == "r14h" ? 2 : 1;
        c.N((id == "r14h" ? "put2" : "putS") + "(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, depth).Store("$p", "$y", false, depth);
        c.N("b = 5").Assign("#b");
        c.N("inspect(copy w)").Input(K.Read, "w");
        return id == "r14h" ? c.Error("b = 5") : c.Valid();
    }

    // i: a nominal Type hides the second exclusive layer (WQ2's field q); j: its self-row alone (touchW), then w is read.
    static Case Hidden(string id)
    {
        var c = new Case("r14A-" + id[3..], id == "r14i"
            ? "hidden layer: `var hw = WQ2.init(q: w@uniq); setW(hw@uniq, b@ref); b = 5; inspect(w)` (setW writes wv.q@follow)"
            : "hidden self-row: `var hw = WQ2.init(q: w@uniq); touchW(hw@uniq); inspect(w)` (SPEC's per-argument letter would make w carry hw's Loan on w)");
        c.N("var x = 1").Def("#x"); c.N("var b = 2").Def("#b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        c.N("var hw = WQ2.init(q: w@uniq)").Def("hw").Borrow("Lw", "w", Uniq, "hw");
        c.N("$p = &uniq two-phase hw").Def("$p").Borrow("Lp", "hw", Uniq, "$p", reserve: true);
        if (id == "r14j")
        {
            c.N("touchW(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 1, hidden: 2);
            c.N("inspect(copy w)").Input(K.Read, "w");
            return c.Valid();
        }
        c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
        c.N("setW(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, 1, hidden: 2).Store("$p", "$y", false, 1, hidden: 2);
        c.N("b = 5").Assign("#b");
        c.N("inspect(copy w)").Input(K.Read, "w");
        return c.Error("b = 5");
    }

    // k: the writable storage behind the exclusive layer is a raw pointee (E, SPEC 15.6), the pointer a local that holds no Loan.
    static Case RawPointee(string id)
    {
        var c = new Case("r14A-" + id[3..], "raw pointee: `var rp: raw/(ref/i32) = Raw.allocate(1); putR(rp@uniq, b@ref); b = 5; let q = rp[0]; inspect(q)`");
        c.N("var b = 2").Def("#b");
        c.N("var rp = Raw.allocate(1)").Def("%rp");
        c.N("$p = &uniq two-phase rp").Def("$p").Borrow("Lp", "%rp", Uniq, "$p", reserve: true);
        c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
        c.N("putR(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, 1, raw: true).Store("$p", "$y", false, 1, raw: true);
        c.N("b = 5").Assign("#b");
        c.N("let q = rp[0]").Def("q").Flow("%rp.*.0", "q");
        c.N("inspect(copy q)").Input(K.Read, "q");
        return c.Error("b = 5");
    }

    // l: a default template's call (until F9): A5's unknown-callee rows, every input → result and → each exclusive input's referents,
    // Value, at no Type depth. SPEC 7.2.3: the template only inspects the preceding exclusive slot, so it writes nothing there.
    static Case Unknown(string id)
    {
        var c = new Case("r14A-" + id[3..], "unknown callee: `var s1 = w@uniq; rf#0(s1@uniq, b@ref)` (a recursive default's template, unknown rows); `b = 5; inspect(w)`");
        c.N("var x = 1").Def("#x"); c.N("var b = 2").Def("#b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
        c.N("$p = &uniq two-phase s1").Def("$p").Borrow("Lp", "s1", Uniq, "$p", reserve: true);
        c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
        c.N("rf#0(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", false, int.MaxValue).Store("$p", "$y", false, int.MaxValue);
        c.N("b = 5").Assign("#b");
        c.N("inspect(copy w)").Input(K.Read, "w");
        return c.Valid();
    }

    // m, o: a result retaining an input's referent content two or three layers down (Content, ret = content(val(a_0)): the first
    // layer's state only); the closure from the result reaches the deeper states. n: a link of that chain is replaced while the result
    // lives: the Loan naming it is in the closure, so the replacement conflicts first (SPEC's per-argument letter; Rust reports `x = 5`).
    static Case Input(string id)
    {
        var c = new Case("r14A-" + id[3..], id switch
        {
            "r14m" => "input side: `let r = get2(s1@uniq); x = 5; inspect(r)`",
            "r14n" => "input side, a link replaced: `let r = get3(s2@uniq); w = z@ref; x = 5; inspect(r)`",
            _ => "input side, depth 3: `let r = get3(s2@uniq); x = 5; inspect(r)`",
        });
        c.N("var x = 1").Def("#x"); c.N("var z = 3").Def("#z");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
        var arg = "s1";
        if (id != "r14m") { c.N("var s2 = s1@uniq").Def("s2").Borrow("Ls2", "s1", Uniq, "s2"); arg = "s2"; }
        c.N($"$p = &uniq two-phase {arg}").Def("$p").Borrow("Lp", arg, Uniq, "$p", reserve: true);
        c.N((id == "r14m" ? "let r = get2" : "let r = get3") + "(move $p) (Content)").Def("r").Input(K.Move, "$p").Activate("Lp").Flow("$p.*", "r");
        if (id == "r14n") c.N("w = z@ref").Assign("w").Borrow("Lz", "#z", Ref, "w");
        c.N("x = 5").Assign("#x");
        c.N("inspect(copy r)").Input(K.Read, "r");
        return id == "r14n" ? c.Error("w = z@ref") : c.Error("x = 5");
    }

    // p: the callee moves a value within the argument's own chain (f(p: uniq/(ref/i32 during a, uniq/(ref/i32 during a) during c))
    // writes `p.1@follow = p.0`): only the self-row (`0 Content store 0`) at layer 2 carries x's Loan into w. q: a rotation of a
    // linked chain (rot(p: uniq/(uniq/Node)) moves n2's link into *p and n1's reference into n2.next, Node {next: Option<uniq/Node>}):
    // n2 then holds a uniq reference to n1, so the Loan naming n1 it receives is real (the route exclusion's unsound acceptance).
    static Case Within(string id)
    {
        if (id == "r14p")
        {
            var c = new Case("r14A-" + id[3..], "move within the chain: `var w = z@ref; var t = (x@ref, w@uniq); f(t@uniq); x = 5; inspect(w)` (f writes p.1@follow = p.0)");
            c.N("var x = 1").Def("#x"); c.N("var z = 3").Def("#z");
            c.N("var w = z@ref").Def("w").Borrow("Lz", "#z", Ref, "w");
            c.N("var t = (x@ref, w@uniq)").Def("t").Borrow("Lx", "#x", Ref, "t").Borrow("Lw", "w", Uniq, "t");
            c.N("$p = &uniq two-phase t").Def("$p").Borrow("Lp", "t", Uniq, "$p", reserve: true);
            c.N("f(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 2);
            c.N("x = 5").Assign("#x");
            c.N("inspect(copy w)").Input(K.Read, "w");
            return c.Error("x = 5");
        }
        var q = new Case("r14A-" + id[3..], "rotation: `var n1 = Node(next: n2@uniq); var s1 = n1@uniq; rot(s1@uniq); n1.value = 5; inspect(n2)` (n2.next now refers to n1)");
        q.N("var n2 = Node()").Def("n2");
        q.N("var n1 = Node(next: n2@uniq)").Def("n1").Borrow("Ln2", "n2", Uniq, "n1");
        q.N("var s1 = n1@uniq").Def("s1").Borrow("Ln1", "n1", Uniq, "s1");
        q.N("$p = &uniq two-phase s1").Def("$p").Borrow("Lp", "s1", Uniq, "$p", reserve: true);
        q.N("rot(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 2, hidden: int.MaxValue);
        q.N("n1.value = 5").Assign("n1.value");
        q.N("inspect(copy n2)").Input(K.Read, "n2");
        return q.Error("n1.value = 5");
    }
}

// ---- Round 15 (PLAN G148: a Call's store rows into writable storage past the first layer) ----
// Binding's retention row retains(c, i, κ, store j) names the writable storage of input j, which TyCtxt.Retention's contract defines
// past every exclusive or raw layer (and behind a nominal Type's hidden fields); A3 5 and A5 at 806110fc store ret(i, κ) into
// refs(val(a_j)) only. Node.Retain encodes each row of the callee's TyCtxt.Retains (probed on the a5-1b5 build for put, put1, put3,
// putW, putR, take, get, next); its depths are the Type-walk depths option B would publish. Callees (Kimigayo):
//   put(p: uniq/(uniq/(ref/i32 during a) during c), y: ref/i32 during a) => p@follow@follow = y          rows (0 C 0) (1 V 0)
//   put1(p: uniq/(ref/i32 during a), y: ref/i32 during a) => p@follow = y                              rows (0 C 0) (1 V 0)
//   put3(p: uniq/(uniq/(uniq/(ref/i32 during a) during c) during d), y) => p@follow@follow@follow = y   rows (0 C 0) (1 V 0)
//   putW(p: uniq/(W<ref/i32 during a> during c), y), W<T>{c} has r: uniq/T during c => p.r@follow = y   rows (0 C 0) (1 V 0)
//   putR(p: uniq/(raw/(ref/i32 during a)), y) => unsafe p@follow[0] = y                                rows (0 C 0) (1 V 0)
//   put4(p: uniq/(ref/(ref/i32 during a) during c), y: ref/i32 during a) (can write only p's referent)   rows (0 C 0) (1 V 0)
//   mv(p: uniq/((uniq/(ref/i32 during a) during c), ref/i32 during a)) => p@follow.0@follow = p@follow.1 rows (0 C 0)
//   next(self: uniq/ArrayUniqIterator<E>{source}) or next(self: uniq/ArrayIterator<E>{source})          rows (0 C result) (0 C 0)
//   swap<T>(first: uniq/T, second: uniq/T), T = uniq/(ref/i32 during a) during c                       rows (0 C 0) (1 C 0) (0 C 1) (1 C 1)
//   mk(p: uniq/(S during a) during a) => p@follow.r = p@follow.n@ref (S {a}: r: ref/i32 during a, n)     rows (0 V 0)
//   rot(p: uniq/(Option<uniq/N during c>, Option<uniq/N during c>)) moves .1 into (*.0).next, N {c}     rows (0 C 0)
//   f(p: uniq/Two{c}) moves p.a into (*p.b), a: uniq/Array<i32> during c, b: uniq/(uniq/Array<i32> during c)  rows (0 C 0)
static class R14B
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => Raw().Select(R14Ann.B);
    static IEnumerable<Case> Raw() => [Repro("r14a1", true), Repro("r14a2", false), Direct(), Depth3(), Depth1("r14d1", true), Depth1("r14d2", false),
        Nominal(), RawPointee(), Unknown(), Move("r14m1", true), Move("r14m2", false), LocalIter(), ParamIter("r14i1", false), ParamIter("r14i2", true), SharedIter(), SharedParent(),
        SharedHolder(), SwapRefs(), ParamRoute(), ValueSelf(), Rotation(), TwoForeign(), LoanFreeReferent(), SharedViewIntoE(), LocalEscape()];

    // The measurement build's full-suite ledger (2 signatures, 3 rows): a shared iterator whose Type may hold exclusive references only
    // through a Type argument or an abstract Type carries Φ_E (a parameter-derived element), so W reaches E, and the self-row's Content
    // there keeps the iterator's shared route La to the local arr: arr's storage end errs. Listed (proposed P14). NoLocalIntoE accepts
    // it, and LocalEscape shows what it gives up.
    static Case SharedViewIntoE()
    {
        var c = new Case("r14B-u", "shared view into E: `func f(value: uniq/i32) { var child = value; var arr = [child@move]; for e in arr => () }`, arr's end (the ledger's q1 shape)");
        c.N("entry (value: uniq/i32 during a)").Param("Lv", "value");
        c.N("var child = value (Reborrow)").Def("child").Borrow("Lc", "value.*", Uniq, "child");
        c.N("var arr = [child@move]").Def("arr").A(K.Move, "child").Flow("child", "arr");
        c.N("$1 = &arr").Def("$1").Borrow("La", "arr", Ref, "$1");
        c.N("it = iterate(move $1)").Def("it").Input(K.Move, "$1").Flow("$1", "it").Flow("$1.*", "it");
        c.N("$2 = &uniq two-phase it").Def("$2").Borrow("Lt", "it", Uniq, "$2", reserve: true);
        c.N("next(move $2)").Input(K.Move, "$2").Activate("Lt").Retain("$2", "$2", true, 1);
        c.N("StorageDead it").A(K.Write, "it").Def("it");
        c.N("StorageDead arr").A(K.Write, "arr").Def("arr");
        c.ALoss = "E keeps a self-row's local Loans (proposed P14)";
        return c.Valid();
    }

    // A self-row moving a local borrow into storage the body does not own (mv2 moves p.1 into *p.0): b's borrow escapes, so b's storage
    // end errs (the Origin stratum also refutes the relation, SPEC 15.6.5). NoLocalIntoE leaves Lb out of E: UNSOUND in the Loan stratum.
    static Case LocalEscape()
    {
        var c = new Case("r14B-v", "local escape by a self-row: `func g(q: uniq/(ref/i32 during a)) { var b = 2; var t = (q@move, b@ref); mv2(t@uniq) }`, b's end");
        c.N("entry (q: uniq/(ref/i32 during a))").Param("Lq", "q");
        c.N("var b = 2").Def("b");
        c.N("var t = (q@move, b@ref)").Def("t").A(K.Move, "q").Flow("q", "t").Borrow("Lb", "b", Ref, "t.1");
        c.N("$1 = &uniq two-phase t").Def("$1").Borrow("L1", "t", Uniq, "$1", reserve: true);
        c.N("mv2(move $1)").Input(K.Move, "$1").Activate("L1").Retain("$1", "$1", true, 1, 2);
        c.N("StorageDead b").A(K.Write, "b").Def("b");
        return c.Error("StorageDead b");
    }

    // x, b, w = x@ref, s1 = w@uniq: the reproducer's prefix.
    static void Prefix(Case c, bool s1 = true)
    {
        c.N("var x = 1; var b = 2").Def("x", "b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "x", Ref, "w");
        c.SharedOnly.Add("w"); // w: ref/i32
        if (s1) c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
    }

    // The arguments `$1 = &uniq two-phase arg; $2 = &b`, then the call; its rows follow.
    static Node Call(Case c, string arg, string label)
    {
        c.N("$1 = &uniq two-phase " + arg).Def("$1").Borrow("L1", arg, Uniq, "$1", reserve: true);
        c.N("$2 = &b").Def("$2").Borrow("Lb", "b", Ref, "$2");
        return c.N(label + "(move $1, move $2)").Input(K.Move, "$1").Input(K.Move, "$2").Activate("L1");
    }

    // a1 (the G148 reproducer), a2 (its control without `b = 5`: SPEC 15.6.4 read literally gives w the route Loan Lw through the
    // row (0 C 0), so its reads err; Rust and the old path accept it).
    static Case Repro(string id, bool write)
    {
        var c = new Case("r14B-" + id[3..], (write ? "G148 reproducer" : "control") + ": `var s1 = w@uniq; put(s1@uniq, b@ref);" + (write ? " b = 5;" : "") + " inspect(w@follow)` twice (old path: valid; A5 806110fc: valid)");
        Prefix(c);
        Call(c, "s1", "put").Retain("$1", "$1", true, 1, 2).Retain("$1", "$2", false, 1, 2);
        if (write) c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        c.N("inspect(copy (*w)) again").Input(K.Read, "w.*");
        return write ? c.Error("b = 5") : c.Valid();
    }

    // The direct twin: the place walk follows every layer (refs^2).
    static Case Direct()
    {
        var c = new Case("r14B-b", "direct twin: `var s2 = s1@uniq; s2@follow@follow = b@ref; b = 5; inspect(w@follow)` (old path and A5: error)");
        Prefix(c);
        c.N("var s2 = s1@uniq").Def("s2").Borrow("Ls1", "s1", Uniq, "s2");
        c.N("$2 = &b").Def("$2").Borrow("Lb", "b", Ref, "$2");
        c.N("(*(*s2)) = move $2").Through("s2.*.*", "$2").A(K.Move, "$2");
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Error("b = 5");
    }

    static Case Depth3()
    {
        var c = new Case("r14B-c", "depth 3: `var s2 = s1@uniq; put3(s2@uniq, b@ref); b = 5; inspect(w@follow)` (old path and A5 806110fc: valid)");
        Prefix(c);
        c.N("var s2 = s1@uniq").Def("s2").Borrow("Ls1", "s1", Uniq, "s2");
        Call(c, "s2", "put3").Retain("$1", "$1", true, 1, 2, 3).Retain("$1", "$2", false, 1, 2, 3);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Error("b = 5");
    }

    static Case Depth1(string id, bool write)
    {
        var c = new Case("r14B-" + id[3..], "depth-1 control: `put1(w@uniq, b@ref);" + (write ? " b = 5;" : "") + " inspect(w@follow)` (old path and A5: " + (write ? "error" : "valid") + ")");
        Prefix(c, s1: false);
        Call(c, "w", "put1").Retain("$1", "$1", true, 1).Retain("$1", "$2", false, 1);
        if (write) c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return write ? c.Error("b = 5") : c.Valid();
    }

    // A nominal Type hides the layer: Binding's walk sees a only as W's Type argument (depth 1), so option B stores into h alone.
    static Case Nominal()
    {
        var c = new Case("r14B-e", "nominal hidden layer: `var h = W(r: w@uniq); putW(h@uniq, b@ref); b = 5; inspect(w@follow)`, W<T>{c} with r: uniq/T during c");
        Prefix(c, s1: false);
        c.N("var h = W(r: w@uniq)").Def("h").Borrow("Lw", "w", Uniq, "h.r");
        Call(c, "h", "putW").Retain("$1", "$1", true, 1).Retain("$1", "$2", false, 1);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Error("b = 5");
    }

    // A raw pointee past an exclusive layer: rp holds no Loan (a raw pointer), so refs(val(a_0)) = {rp} reaches nothing; its pointee is
    // storage the body does not own (SPEC 15.6, E14). Probed: A5 806110fc accepts; its direct twin `s@follow[0] = b@ref` errs at b = 5.
    static Case RawPointee()
    {
        var c = new Case("r14B-f", "raw pointee at depth 2: `var rp = w@raw; putR(rp@uniq, b@ref); b = 5; inspect(w@follow)` (old path and A5 806110fc: valid)");
        Prefix(c, s1: false);
        c.N("var rp = w@raw").Def("rp");
        c.RawRoots.Add("rp");
        Call(c, "rp", "putR").Retain("$1", "$1", true, 1, 2).Retain("$1", "$2", false, 2);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Error("b = 5");
    }

    // A5's unknown-callee rows (a default template, until F9): every input into each exclusive input's storage, Value. The builder never
    // passes an exclusive input to a template (its inputs are `ref/…` PreparedInputs, SPEC 7.2.3; probe p08c), so this is the row set's
    // behavior, not a reachable program; its Value self-row makes w carry Lw and L1, so w's read errs too.
    static Case Unknown()
    {
        var c = new Case("r14B-g", "unknown-callee rows on the reproducer's arguments: `tmpl(s1@uniq, b@ref); b = 5; inspect(w@follow)` (rows: every input, Value, into input 0)");
        Prefix(c);
        Call(c, "s1", "tmpl").Retain("$1", "$1", false, 1).Retain("$1", "$2", false, 1);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Error("b = 5");
    }

    // A self-row that moves a value from depth 1 to depth 2: w must carry Lz, never its route Lw.
    static Case Move(string id, bool write)
    {
        var c = new Case("r14B-" + id[3..], "self-row across depths: `var t = (w@uniq, z@ref); mv(t@uniq);" + (write ? " z = 5;" : "") + " inspect(w@follow)` twice (mv: p@follow.0@follow = p@follow.1)");
        c.N("var x = 1; var z = 3").Def("x", "z");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "x", Ref, "w");
        c.SharedOnly.Add("w");
        c.N("var t = (w@uniq, z@ref)").Def("t").Borrow("Lw", "w", Uniq, "t.0").Borrow("Lz", "z", Ref, "t.1");
        c.N("$1 = &uniq two-phase t").Def("$1").Borrow("L1", "t", Uniq, "$1", reserve: true);
        c.N("mv(move $1)").Input(K.Move, "$1").Activate("L1").Retain("$1", "$1", true, 1, 2);
        if (write) c.N("z = 5").Assign("z");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        c.N("inspect(copy (*w)) again").Input(K.Read, "w.*");
        return write ? c.Error("z = 5") : c.Valid();
    }

    // Exclusive iteration over a local Array of references: next's self-row (0 C 0) reaches arr through the iterator's Loan La.
    static Case LocalIter()
    {
        var c = new Case("r14B-h", "local exclusive iteration: `var arr: Array<ref/i32> = [x@ref]; for e in arr@uniq => (); inspect(arr@ref)` (old path, A5 806110fc: valid)");
        c.N("var x = 1").Def("x");
        c.N("var arr = [x@ref]").Def("arr").Borrow("Lx", "x", Ref, "arr");
        c.SharedOnly.Add("arr"); // Array<ref/i32>
        c.N("$1 = &uniq two-phase arr").Def("$1").Borrow("La", "arr", Uniq, "$1", reserve: true);
        c.N("it = iterateUniq(move $1)").Def("it").Input(K.Move, "$1").Activate("La").Flow("$1", "it").Flow("$1.*", "it");
        c.N("$2 = &uniq two-phase it").Def("$2").Borrow("Lt", "it", Uniq, "$2", reserve: true);
        c.N("next(move $2)").Input(K.Move, "$2").Activate("Lt").Retain("$2", "$2", true, 1);
        c.N("$3 = &arr").Def("$3").Borrow("L3", "arr", Ref, "$3");
        c.N("inspect(move $3)").Input(K.Move, "$3");
        return c.Valid();
    }

    // Exclusive iteration over a uniq parameter, then a use of it. With Loan-free elements (i1, values: uniq/Array<i32>) the iterator's
    // Type holds an exclusive reference only to Loan-free storage (UniqRemainder<i32>'s Loan<uniq/i32>), so W does not descend from it.
    // With elements that may hold Loans (i2, Array<ref/i32>) the iterator carries Lc (values.*) and Φ_E, so W holds E, which keeps the
    // whole value (E is one target): a listed loss. RouteIntoE accepts i2, and TwoForeign shows that unsound.
    static Case ParamIter(string id, bool loans)
    {
        var c = new Case("r14B-" + id[3..], "exclusive iteration over a uniq parameter, then a use: `func bump(values: uniq/Array<" + (loans ? "ref/i32" : "i32") + ">) { for v in values => (); values.append(…) }` (old path, A5 806110fc, Rust: valid)");
        c.N("entry (values: uniq/Array<" + (loans ? "ref/i32" : "i32") + ">)").Param("Lv", "values");
        c.N("$1 = &uniq two-phase (*values)").Def("$1").Borrow("Lc", "values.*", Uniq, "$1", reserve: true);
        c.N("it = iterateUniq(move $1)").Def("it").Input(K.Move, "$1").Activate("Lc").Flow("$1", "it").Flow("$1.*", "it");
        if (!loans) c.SharedOnly.Add("it"); // ArrayUniqIterator<i32>
        c.N("$2 = &uniq two-phase it").Def("$2").Borrow("Lt", "it", Uniq, "$2", reserve: true);
        c.N("next(move $2)").Input(K.Move, "$2").Activate("Lt").Retain("$2", "$2", true, 1);
        c.N("$3 = &uniq two-phase (*values)").Def("$3").Borrow("L3", "values.*", Uniq, "$3", reserve: true);
        c.N("append(move $3, …)").Input(K.Move, "$3").Activate("L3");
        if (loans) c.ALoss = "E is one target (proposed P13)";
        return c.Valid();
    }

    // c73 of MirBorrowsTest (found by the measurement build): a constructor's Value rows into the storage past a parameter's reborrow
    // t1: uniq/i32, whose referent holds no Loan. The design does not descend from t1 (SharedOnly); NoTypeGuard reaches E through t1's
    // Φ_E and keeps L2 there, so t2's storage end errs.
    static Case LoanFreeReferent()
    {
        var c = new Case("r14B-t", "Loan-free referent: `func f(s1, s2: uniq/i32 during a) { var t1 = s1; var t2 = s2; var p = PA(pa: t1@uniq, pb: t2@uniq) }`, scope end (c73's prefix)");
        c.N("entry (s1, s2: uniq/i32 during a)").Param("Ls1", "s1").Param("Ls2", "s2");
        c.N("var t1 = s1 (Reborrow)").Def("t1").Borrow("Lt1", "s1.*", Uniq, "t1");
        c.N("var t2 = s2 (Reborrow)").Def("t2").Borrow("Lt2", "s2.*", Uniq, "t2");
        c.SharedOnly.UnionWith(["t1", "t2"]); // uniq/i32: an exclusive reference to Loan-free storage
        c.N("$1 = &uniq two-phase t1").Def("$1").Borrow("L1", "t1", Uniq, "$1", reserve: true);
        c.N("$2 = &uniq two-phase t2").Def("$2").Borrow("L2", "t2", Uniq, "$2", reserve: true);
        c.N("p = PA.init(move $1, move $2)").Def("p").Input(K.Move, "$1").Input(K.Move, "$2").Activate("L1").Activate("L2")
            .Retain("$1", "$1", false, 1).Retain("$1", "$2", false, 1).Retain("$2", "$1", false, 1).Retain("$2", "$2", false, 1).Flow("$1", "p").Flow("$2", "p");
        c.N("StorageDead t2").A(K.Write, "t2").Def("t2");
        c.N("StorageDead t1").A(K.Write, "t1").Def("t1");
        return c.Valid();
    }

    // Shared iteration over a field of a uniq parameter, then a write of it: ArrayIterator holds no exclusive reference (SharedOnly), so
    // its Φ_E reaches no E. NoTypeGuard puts Lc into E and rejects it.
    static Case SharedIter()
    {
        var c = new Case("r14B-j", "shared iteration, then a write: `func bump(self: uniq/Bag) { for v in self.items => (); self.items.append(0) }` (old path, A5 806110fc: valid)");
        c.N("entry (self: uniq/Bag)").Param("Ls", "self");
        c.N("$1 = &(*self).items").Def("$1").Borrow("Lc", "self.*.items", Ref, "$1");
        c.N("it = iterate(move $1)").Def("it").Input(K.Move, "$1").Flow("$1", "it").Flow("$1.*", "it");
        c.SharedOnly.Add("it");
        c.N("$2 = &uniq two-phase it").Def("$2").Borrow("Lt", "it", Uniq, "$2", reserve: true);
        c.N("next(move $2)").Input(K.Move, "$2").Activate("Lt").Retain("$2", "$2", true, 1);
        c.N("$3 = &uniq two-phase (*self).items").Def("$3").Borrow("L3", "self.*.items", Uniq, "$3", reserve: true);
        c.N("append(move $3, const 0)").Input(K.Move, "$3").Activate("L3");
        return c.Valid();
    }

    // A shared Reborrow carries its exclusive parent Lw (R2): r's Type holds no exclusive reference, so W does not descend from it;
    // NoTypeGuard follows Lw and rejects `b = 5`.
    static Case SharedParent()
    {
        var c = new Case("r14B-k", "shared Reborrow's parent: `var s = w@uniq; var r = s@follow@ref; put4(r@uniq, b@ref); b = 5; inspect(w@follow)` (put4 can write only r's storage)");
        Prefix(c, s1: false);
        c.N("var s = w@uniq").Def("s").Borrow("Lw", "w", Uniq, "s");
        c.N("var r = s@follow@ref").Def("r").Borrow("Lr", "s.*", Ref, "r");
        c.SharedOnly.Add("r"); // r: ref/(ref/i32)
        Call(c, "r", "put4").Retain("$1", "$1", true, 1).Retain("$1", "$2", false, 1);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*w))").Input(K.Read, "w.*");
        return c.Valid();
    }

    // A holder behind a shared layer: W stops at w's shared Loan; AllModes follows it into xr and rejects `b = 5`.
    static Case SharedHolder()
    {
        var c = new Case("r14B-l", "behind a shared layer: `var xr = z@ref; var w = xr@ref; var s1 = w@uniq; put(s1@uniq, b@ref); b = 5; inspect(xr@follow)` (w dead)");
        c.N("var z = 1; var b = 2").Def("z", "b");
        c.N("var xr = z@ref").Def("xr").Borrow("Lz", "z", Ref, "xr");
        c.N("var w = xr@ref").Def("w").Borrow("Lxr", "xr", Ref, "w");
        c.SharedOnly.UnionWith(["xr", "w"]); // ref/i32, ref/(ref/i32)
        c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
        Call(c, "s1", "put").Retain("$1", "$1", true, 1, 2).Retain("$1", "$2", false, 1, 2);
        c.N("b = 5").Assign("b");
        c.N("inspect(copy (*xr))").Input(K.Read, "xr.*");
        return c.Valid();
    }

    // swap of two exclusive references: per argument (SPEC 15.6.4) w1, storage of `first` past its referent, retains `second`'s
    // Content, σ(s2) = {Lw2}; route exclusion does not remove it (Lw2 is not w1's route). Rust accepts (by reasoning).
    static Case SwapRefs()
    {
        var c = new Case("r14B-n", "swap of references: `s1 = w1@uniq; s2 = w2@uniq; swap(s1@uniq, s2@uniq); w2 = x1@ref; inspect(w1@follow)` (SPEC per argument: error; Rust: valid)");
        c.N("var x1 = 1; var x2 = 2").Def("x1", "x2");
        c.N("var w1 = x1@ref").Def("w1").Borrow("L1x", "x1", Ref, "w1");
        c.N("var w2 = x2@ref").Def("w2").Borrow("L2x", "x2", Ref, "w2");
        c.SharedOnly.UnionWith(["w1", "w2"]);
        c.N("var s1 = w1@uniq").Def("s1").Borrow("Lw1", "w1", Uniq, "s1");
        c.N("var s2 = w2@uniq").Def("s2").Borrow("Lw2", "w2", Uniq, "s2");
        c.N("$1 = &uniq two-phase s1").Def("$1").Borrow("La", "s1", Uniq, "$1", reserve: true);
        c.N("$2 = &uniq two-phase s2").Def("$2").Borrow("Lc", "s2", Uniq, "$2", reserve: true);
        c.N("swap(move $1, move $2)").Input(K.Move, "$1").Input(K.Move, "$2").Activate("La").Activate("Lc")
            .Retain("$1", "$1", true, 1, 2).Retain("$1", "$2", true, 1, 2).Retain("$2", "$1", true, 1, 2).Retain("$2", "$2", true, 1, 2);
        c.N("w2 = x1@ref").Assign("w2").Borrow("Lq", "x1", Ref, "w2");
        c.N("inspect(copy (*w1))").Input(K.Read, "w1.*");
        return c.Error("w2 = x1@ref");
    }

    // A parameter's exclusive reference moved into a local: its referent is storage the body does not own (E).
    static Case ParamRoute()
    {
        var c = new Case("r14B-o", "parameter route: `func g(q: uniq/(ref/i32 during a)) { var b = 2; var s1 = q@move; put(s1@uniq, b@ref); b = 5 }`");
        c.N("entry (q: uniq/(ref/i32 during a))").Param("Lq", "q");
        c.N("var b = 2").Def("b");
        c.N("var s1 = q@move").Def("s1").A(K.Move, "q").Flow("q", "s1");
        Call(c, "s1", "put").Retain("$1", "$1", true, 1, 2).Retain("$1", "$2", false, 1, 2);
        c.N("b = 5").Assign("b");
        return c.Error("b = 5");
    }

    // A genuine self-reference through the argument's own route: mk's outer Origin is a, so the row is (0 V 0) and s keeps L1.
    static Case ValueSelf()
    {
        var c = new Case("r14B-p", "Value self-row: `var s = S(r: z@ref, n: 1); mk(s@uniq); s.n = 5; inspect(s.r@follow)` (mk: p@follow.r = p@follow.n@ref, p: uniq/(S during a) during a)");
        c.N("var z = 0").Def("z");
        c.N("var s = S(r: z@ref, n: 1)").Def("s").Borrow("Lz", "z", Ref, "s.r");
        c.SharedOnly.Add("s"); // S {a}: r: ref/i32 during a, n: i32
        c.N("$1 = &uniq two-phase s").Def("$1").Borrow("L1", "s", Uniq, "$1", reserve: true);
        c.N("mk(move $1)").Input(K.Move, "$1").Activate("L1").Retain("$1", "$1", false, 1);
        c.N("s.n = 5").Assign("s.n");
        c.N("inspect(copy (*s.r))").Input(K.Read, "s.r.*");
        return c.Error("s.n = 5", "inspect(copy (*s.r))"); // s keeps L1, as Rust's `&'a mut S<'a>` keeps s borrowed
    }

    // A route moved into another target: x1 must keep Lx2 (only a target's own route is left out).
    static Case Rotation()
    {
        var c = new Case("r14B-q", "route into another target: `var t = (.Some(x1@uniq), .Some(x2@uniq)); rot(t@uniq); x2.v = 5; inspect(x1)` (rot moves t.1 into (*t.0).next)");
        c.N("var x1 = N(); var x2 = N()").Def("x1", "x2");
        c.N("var t = (x1@uniq, x2@uniq)").Def("t").Borrow("Lx1", "x1", Uniq, "t.0").Borrow("Lx2", "x2", Uniq, "t.1");
        c.N("$1 = &uniq two-phase t").Def("$1").Borrow("L1", "t", Uniq, "$1", reserve: true);
        c.N("rot(move $1)").Input(K.Move, "$1").Activate("L1").Retain("$1", "$1", true, 1, 2);
        c.N("x2.v = 5").Assign("x2.v");
        c.N("inspect(x1)").A(K.Read, "x1");
        return c.Error("x2.v = 5");
    }

    // Two references into storage the body does not own, one moved into the other's referent (a premise `origin values outlives o`
    // lets the caller form both): E must keep Lc, so values' later use errs. RouteIntoE leaves Lc out of E: UNSOUND.
    static Case TwoForeign()
    {
        var c = new Case("r14B-r", "two foreign routes: `var both = Two(a: values (reborrow), b: other@follow@uniq); f(both@uniq); values.append(0)` (f moves both.a into *both.b)");
        c.N("entry (values: uniq/Array<i32>, other: uniq/(uniq/Array<i32> during o))").Param("Lv", "values").Param("Lo", "other");
        c.N("var both = Two(a: values, b: other@follow@uniq)").Def("both").Borrow("Lc", "values.*", Uniq, "both.a").Borrow("Lq", "other.*", Uniq, "both.b");
        c.N("$1 = &uniq two-phase both").Def("$1").Borrow("L1", "both", Uniq, "$1", reserve: true);
        c.N("f(move $1)").Input(K.Move, "$1").Activate("L1").Retain("$1", "$1", true, 1, 2);
        c.N("$3 = &uniq two-phase (*values)").Def("$3").Borrow("L3", "values.*", Uniq, "$3", reserve: true);
        c.N("append(move $3, const 0)").Input(K.Move, "$3").Activate("L3");
        return c.Error("$3 = &uniq two-phase (*values)", "append(move $3, const 0)");
    }
}

// ---- Round 14 review (PLAN G148): annotations of both designers' cases for the union, and the review's cases (ids r14V-…) ----
static class R14Ann
{
    // Designer a's cases ('#' locals hold no Loan, '%' are raw roots, its encoding): designer b's Type-guard facts (w: ref/i32, and
    // s: ref/(ref/i32) in f and g, hold no exclusive reference), SelfRef (n1, n2: Node holds uniq/Node), and the letter where a's
    // verdict departs from it (its †: e and j).
    public static Case A(Case c)
    {
        c.SharedOnly.Add("w");
        if (c.Id is "r14A-f" or "r14A-g") c.SharedOnly.Add("s");
        if (c.Id == "r14A-q") c.SelfRef.UnionWith(["n1", "n2"]);
        if (c.Id is "r14A-e" or "r14A-j") { c.LetterErr = true; c.Rust = "valid"; }
        return c;
    }

    // Designer b's cases: its i32 locals hold no Loan (NonHolders, for the any-mode walks), SelfRef (S {r: ref/i32, n: i32}; N holds
    // uniq/N; Array<uniq/i32> holds two exclusive element slots), and its "SPEC as written" column where it departs from its verdict.
    public static Case B(Case c)
    {
        string[] nh = c.Id switch
        {
            "r14B-a1" or "r14B-a2" or "r14B-b" or "r14B-c" or "r14B-d1" or "r14B-d2" or "r14B-e" or "r14B-f" or "r14B-g" or "r14B-k" => ["x", "b"],
            "r14B-m1" or "r14B-m2" => ["x", "z"],
            "r14B-h" => ["x"],
            "r14B-l" => ["z", "b"],
            "r14B-n" => ["x1", "x2"],
            "r14B-o" or "r14B-v" => ["b"],
            "r14B-p" => ["z"],
            _ => [],
        };
        c.NonHolders.UnionWith(nh);
        if (c.Id == "r14B-p") c.SelfRef.Add("s");
        if (c.Id == "r14B-q") c.SelfRef.UnionWith(["x1", "x2"]);
        if (c.Id == "r14B-u") { c.SelfRef.Add("arr"); c.ByArgument.Add("it"); c.UnsuppAt = ["StorageDead arr"]; } // round 16: P13 (ArrayIterator<T>)
        if (c.Id is "r14B-a2" or "r14B-h" or "r14B-i2" or "r14B-m2") { c.LetterErr = true; c.Rust = "valid"; }
        return c;
    }
}

// The review's cases. Callees (Kimigayo; s1, s1c and s2 probed on the a5-1b5 build and designer b's measurement build, rv-g148/probes):
//   mk3(p: uniq/(Option<uniq/(S during a) during a>) during d): takes *p out (exchange with .None) and stores q.r = q.n@ref, S {a} with
//     r: ref/i32 during a, n: i32; row (0 C 0). mk0: the same contract, an empty body.
//   mvsib(p: uniq/(uniq/(uniq/(ref/i32 during a) during c, uniq/(ref/i32 during a) during c) during e) during f): copies *(**p).1 into
//     *(**p).0 (probed through a helper mv1); row (0 C 0).
//   kid(p: uniq/(uniq/(T during a) during a, ref/i32 during a)): stores p@follow.1 into (*p.0).r, T {a} with n: i32, r: ref/i32 during a.
//   cross(p: uniq/(Option<uniq/i32 during o>, uniq/(ref/i32 during q) during c)) with `origin o outlives q`: takes *p.0 out and stores a
//     shared reborrow through it into *p.1.
//   tryWrite(writer: uniq/Utf8Writer): Milestone24's write through a writer that holds buffer@uniq; FixedBuffer holds bytes@uniq.
static class R14V
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [SelfRef("r14V-s1", true), SelfRef("r14V-s1c", false), Sibling("r14V-s2", true), Sibling("r14V-s2c", false),
        Kid(), Cross(), Depth3Control(), Writer()];

    // s1: a genuine self-reference past layer 1 through a Content self-row (X and the dominator exclusion drop Ls at s). s1c: the same
    // contract with an empty body; no rule that reads only the contract can tell them apart (Rust: error too, `&'a mut S<'a>`).
    static Case SelfRef(string id, bool body)
    {
        var c = new Case(id, body ? "self-reference past layer 1: `var s = S(r: z@ref, n: 1); var s1 = .Some(s@uniq); mk3(s1@uniq); s.n = 5; inspect(s.r@follow)` (mk3 takes *p out, then q.r = q.n@ref)"
            : "the same contract, empty body: `…; mk0(s1@uniq); s.n = 5; inspect(s.r@follow)` (Rust: error, `&'a mut S<'a>`)");
        c.N("var z = 0").Def("#z");
        c.N("var s = S(r: z@ref, n: 1)").Def("s").Borrow("Lz", "#z", Ref, "s");
        c.N("var s1 = .Some(s@uniq)").Def("s1").Borrow("Ls", "s", Uniq, "s1");
        c.N("$p = &uniq two-phase s1").Def("$p").Borrow("Lp", "s1", Uniq, "$p", reserve: true);
        c.N((body ? "mk3" : "mk0") + "(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 2);
        c.N("s.n = 5").Assign("s.n");
        c.N("inspect(copy (*s.r))").Input(K.Read, "s.r.*");
        c.SharedOnly.Add("s"); c.SelfRef.Add("s");
        if (!body) c.Rust = "error";
        return c.Error("s.n = 5");
    }

    // s2: a value moved between siblings at depth 3; w1's only path to x2's Loan is u's state, through Lu, which names w1's dominator u
    // (the dominator exclusion drops it). s2c: its control (Rust: valid; SPEC's letter: the route cycle w1 → Lu → σ(u) ∋ Lw1).
    static Case Sibling(string id, bool write)
    {
        var c = new Case(id, "depth-3 sibling copy: `var u = (w1@uniq, w2@uniq); var s = u@uniq; mvsib(s@uniq);" + (write ? " x2 = 5;" : "") + " inspect(w1@follow)` (mvsib copies *(**p).1 into *(**p).0)");
        c.N("var x1 = 1; var x2 = 2").Def("#x1", "#x2");
        c.N("var w1 = x1@ref").Def("w1").Borrow("Lx1", "#x1", Ref, "w1");
        c.N("var w2 = x2@ref").Def("w2").Borrow("Lx2", "#x2", Ref, "w2");
        c.N("var u = (w1@uniq, w2@uniq)").Def("u").Borrow("Lw1", "w1", Uniq, "u").Borrow("Lw2", "w2", Uniq, "u");
        c.N("var s = u@uniq").Def("s").Borrow("Lu", "u", Uniq, "s");
        c.N("$p = &uniq two-phase s").Def("$p").Borrow("Lp", "s", Uniq, "$p", reserve: true);
        c.N("mvsib(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 3);
        if (write) c.N("x2 = 5").Assign("#x2");
        c.N("inspect(copy (*w1))").Input(K.Read, "w1.*");
        c.SharedOnly.UnionWith(["w1", "w2"]); c.SelfRef.Add("u");
        if (!write) { c.LetterErr = true; c.Rust = "valid"; }
        return write ? c.Error("x2 = 5") : c.Valid();
    }

    // s3: the caller places a child of p2.0 (a shared reborrow of t.n through t's reference) in the sibling slot p2.1; the callee moves it
    // into (*p2.0).r. t must keep Lt (the child's parent; the child Loan itself is rooted at p2), which X drops at t.
    static Case Kid()
    {
        var c = new Case("r14V-s3", "a caller's child moved into its parent's referent: `var p2 = (t@uniq, z@ref); p2.1 = p2.0@follow.n@ref; kid(p2@uniq); t.n = 5; inspect(t.r@follow)` (kid stores p.1 into (*p.0).r)");
        c.N("var z = 0").Def("#z");
        c.N("var t = T(n: 1, r: z@ref)").Def("t").Borrow("Lz", "#z", Ref, "t");
        c.N("var p2 = (t@uniq, z@ref)").Def("p2").Borrow("Lt", "t", Uniq, "p2").Borrow("Lz2", "#z", Ref, "p2");
        c.N("p2.1 = p2.0@follow.n@ref").Assign("p2.1").Borrow("Lc", "p2.0.*.n", Ref, "p2.1");
        c.N("$p = &uniq two-phase p2").Def("$p").Borrow("Lp", "p2", Uniq, "$p", reserve: true);
        c.N("kid(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 2);
        c.N("t.n = 5").Assign("t.n");
        c.N("inspect(copy (*t.r))").Input(K.Read, "t.r.*");
        c.SharedOnly.Add("t"); c.SelfRef.Add("t");
        return c.Error("t.n = 5");
    }

    // s4: two parts of one holder reached through two sibling references; the callee stores a reborrow of one into the other. Both
    // Loans name t, so X drops both.
    static Case Cross()
    {
        var c = new Case("r14V-s4", "parts of one holder through two references: `var u = (.Some(t.f@uniq), t.g@uniq); cross(u@uniq); t.f = 5; inspect(t.g@follow)` (cross stores a reborrow of *u.0 into *u.1)");
        c.N("var z = 0").Def("#z");
        c.N("var t = T2(f: 1, g: z@ref)").Def("t").Borrow("Lz", "#z", Ref, "t");
        c.N("var u = (.Some(t.f@uniq), t.g@uniq)").Def("u").Borrow("Lf", "t.f", Uniq, "u").Borrow("Lg", "t.g", Uniq, "u");
        c.N("$p = &uniq two-phase u").Def("$p").Borrow("Lp", "u", Uniq, "$p", reserve: true);
        c.N("cross(move $p)").Input(K.Move, "$p").Activate("Lp").Store("$p", "$p", true, 2);
        c.N("t.f = 5").Assign("t.f");
        c.N("inspect(copy (*t.g))").Input(K.Read, "t.g.*");
        c.SharedOnly.Add("t"); c.SelfRef.Add("t");
        return c.Error("t.f = 5");
    }

    // c3: designer a's r14c without `b = 5` (Rust: valid; SPEC's letter and X: the route cycle at w).
    static Case Depth3Control()
    {
        var c = new Case("r14V-c3", "depth-3 control: `var s1 = w@uniq; var s2 = s1@uniq; put3(s2@uniq, b@ref); inspect(w)` (r14A-c without `b = 5`)");
        c.N("var x = 1").Def("#x"); c.N("var b = 2").Def("#b");
        c.N("var w = x@ref").Def("w").Borrow("Lx", "#x", Ref, "w");
        c.N("var s1 = w@uniq").Def("s1").Borrow("Lw", "w", Uniq, "s1");
        c.N("var s2 = s1@uniq").Def("s2").Borrow("Ls2", "s1", Uniq, "s2");
        c.N("$p = &uniq two-phase s2").Def("$p").Borrow("Lp", "s2", Uniq, "$p", reserve: true);
        c.N("$y = b@ref").Def("$y").Borrow("Lb", "#b", Ref, "$y");
        c.N("put3(move $p, move $y)").Input(K.Move, "$p").Input(K.Move, "$y").Activate("Lp").Store("$p", "$p", true, 3).Store("$p", "$y", false, 3);
        c.N("inspect(copy w)").Input(K.Read, "w");
        c.SharedOnly.Add("w"); c.LetterErr = true; c.Rust = "valid";
        return c.Valid();
    }

    // w: Milestone24's shape. The writer's self-row reaches buffer through Lbu; with no exclusion buffer carries Lbu and its later
    // borrows err (designer b's corpus: Milestone24, 6 problems).
    static Case Writer()
    {
        var c = new Case("r14V-w", "Milestone24's writer: `var buffer = Text.fixed(bytes@uniq); var writer = Text.writer(buffer@uniq); tryWrite(writer@uniq); buffer.text(); buffer.clear()`");
        c.N("var bytes = [64 of 0]").Def("#bytes");
        c.N("var buffer = Text.fixed(bytes@uniq)").Def("buffer").Borrow("Lby", "#bytes", Uniq, "buffer");
        c.N("$1 = &uniq two-phase buffer").Def("$1").Borrow("Lbu", "buffer", Uniq, "$1", reserve: true);
        c.N("var writer = Text.writer(move $1)").Def("writer").Input(K.Move, "$1").Activate("Lbu").Flow("$1", "writer").Flow("$1.*", "writer");
        c.N("$2 = &uniq two-phase writer").Def("$2").Borrow("Lw", "writer", Uniq, "$2", reserve: true);
        c.N("tryWrite(move $2)").Input(K.Move, "$2").Activate("Lw").Store("$2", "$2", true, 1);
        c.N("$3 = &buffer").Def("$3").Borrow("L3", "buffer", Ref, "$3");
        c.N("text(move $3)").Input(K.Move, "$3");
        c.N("$4 = &uniq two-phase buffer").Def("$4").Borrow("L4", "buffer", Uniq, "$4", reserve: true);
        c.N("clear(move $4)").Input(K.Move, "$4").Activate("L4");
        c.SharedOnly.Add("buffer");
        c.LetterErr = true; c.Rust = "valid";
        return c.Valid();
    }
}

// ---- Round 16 (PLAN G148's final review B1: P13's taint read only on the found set; the owner's route taint, 2026-10-19) ----
// Callees (Kimigayo; compiler probes at b335393b in g148p/r2, the review's rv-g148f/probes):
//   storeL(l: uniq/Loan<uniq/(ref/(ref/i32 during p) during q) during a>, v: ref/(ref/i32 during p) during q) => (): rows v → l and l's
//     self-row (Content). Loan<T> is a library marker of a held borrow: its Type argument is the only exclusive reference, so a holder of
//     it passes rule G's guard only by argument (MirWrites.ThroughArgument, MirTypesTest's `loan` row).
//   storeD(l: uniq/Loan<uniq/(ref/Data during q) during a>, v: ref/Data during q) -> bool: the same with Data.
//   viaD(x: ref/(ref/i32 during p) during q, y: ref/(ref/i32 during p) during q = x) -> …: y's default copies x into its pending slot.
//   saveDef(slot: uniq/(ref/Data during s), x: ref/Data during s, v: ref/Data during s = x) -> bool: rows x → slot and v → slot.
static class R16
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [ClosureRoute("r16-closureRoute", Tail.Write), ClosureRoute("r16-closureRouteB", Tail.WriteB),
        ClosureRoute("r16-closureRouteClean", Tail.Live), GuardP13("r16-guardP13", live: true, clean: false), GuardP13("r16-guardP13Dead", live: false, clean: false),
        GuardP13("r16-guardP13Clean", live: true, clean: true), DefaultRoute("r16-closureRouteD", join: false, live: false),
        DefaultRoute("r16-closureRouteDJoin", join: true, live: false), DefaultRoute("r16-closureRouteDLive", join: true, live: true),
        GuardDefault("r16-guardDefault", join: true, live: true), GuardDefault("r16-guardDefaultOnly", join: false, live: true),
        GuardDefault("r16-guardDefaultDead", join: true, live: false), CleanRoute()];

    enum Tail { Write, WriteB, Live }

    // B1 (1), the review's closure3: lh holds w@uniq in a Loan<…>, so storeL's stores reach w and z only past lh (P13's taint); w is live
    // and holds the stored Lb, whose closure reaches b, whose own set is clean and holds Ly. `y = 5` conflicts with Ly only along that
    // tainted route (closureRoute); `b = x@ref` with Lb in tainted sets only (closureRouteB); with b used later, b is a root of the check
    // set and clean, so `y = 5` stays the Language problem (closureRouteClean). `inspect(w)` meets w's own Lw, which the self-row stored
    // into w past lh: Unsupported_Kd in every taint mode.
    static Case ClosureRoute(string id, Tail tail)
    {
        var c = new Case(id, "`var w = z@ref; var lh = Loan<uniq/(ref/(ref/i32))>.init(w@uniq); var b = y@ref; storeL(lh@uniq, b@ref); "
            + (tail == Tail.WriteB ? "b = x@ref" : "y = 5") + "; inspect(w)" + (tail == Tail.Live ? "; inspect(b)" : "") + "`");
        c.N("var x = 1").Def("#x"); c.N("var y = 2").Def("#y");
        c.N("var z = x@ref").Def("z").Borrow("Lx", "#x", Ref, "z");
        c.N("var w = z@ref").Def("w").Borrow("Lz", "z", Ref, "w");
        c.N("$1 = &uniq two-phase w").Def("$1").Borrow("Lw", "w", Uniq, "$1", reserve: true);
        c.N("var lh = Loan.init(move $1)").Def("lh").Input(K.Move, "$1").Activate("Lw").Flow("$1", "lh").Flow("$1.*", "lh");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$p = &uniq two-phase lh").Def("$p").Borrow("Lp", "lh", Uniq, "$p", reserve: true);
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        c.N("storeL(move $p, move $v)").Input(K.Move, "$p").Input(K.Move, "$v").Activate("Lp").Store("$p", "$p", true, 1).Store("$p", "$v", false, 1);
        var at = tail == Tail.WriteB ? "b = x@ref" : "y = 5";
        if (tail == Tail.WriteB) c.N(at).Assign("b").Borrow("Lx2", "#x", Ref, "b"); else c.N(at).Assign("#y");
        c.N("inspect(w)").Input(K.Read, "w");
        if (tail == Tail.Live) c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["z", "w", "b"]); c.ByArgument.Add("lh");
        if (tail == Tail.Live) { c.UnsuppAt = ["inspect(w)"]; return c.Error("y = 5"); }
        c.UnsuppAt = [at, "inspect(w)"];
        return c.Valid();
    }

    // B1 (2), the review's guard: inside a match guard, storeD stores the candidate d past lh (a Loan<uniq/(ref/Data)>) into saved, which
    // is used after the match, so γ_d reaches saved only along P13's taint: err(ended) at d's cleanup is the located Unsupported_Kd
    // (guardP13); with saved dead, nothing escapes (guardP13Dead); a concrete store of d into a live `direct` keeps the Language problem
    // (guardP13Clean). `inspect(saved)` meets saved's own Ls, which the self-row stored past lh.
    static Case GuardP13(string id, bool live, bool clean)
    {
        var c = new Case(id, "`var saved = other@ref; var lh = Loan<uniq/(ref/Data)>.init(saved@uniq); match packet .Data(let d) if storeD(lh@uniq, d)"
            + (clean ? " and keep(direct@uniq, d)" : "") + " => ()" + (live ? "; inspect(saved)" : "") + (clean ? "; inspect(direct)" : "") + "`");
        c.N("var other = Data(); var packet: Packet = .Data(Data())").Def("#other", "packet");
        c.N("var saved = other@ref").Def("saved").Borrow("Lo", "#other", Ref, "saved");
        if (clean) c.N("var direct = other@ref").Def("direct").Borrow("Lo2", "#other", Ref, "direct");
        c.N("$1 = &uniq two-phase saved").Def("$1").Borrow("Ls", "saved", Uniq, "$1", reserve: true);
        c.N("var lh = Loan.init(move $1)").Def("lh").Input(K.Move, "$1").Activate("Ls").Flow("$1", "lh").Flow("$1.*", "lh");
        c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("$p = &uniq two-phase lh").Def("$p").Borrow("Lp", "lh", Uniq, "$p", reserve: true);
        c.N("storeD(move $p, copy d)").Input(K.Move, "$p").Input(K.Read, "d").Activate("Lp").Store("$p", "$p", true, 1).Store("$p", "d", false, 1);
        if (clean)
        {
            c.N("$q = &uniq two-phase direct").Def("$q").Borrow("Lq", "direct", Uniq, "$q", reserve: true);
            c.N("keep(move $q, copy d) (stores d into direct)").Input(K.Move, "$q").Input(K.Read, "d").Activate("Lq").Through("$q.*", "d");
        }
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        if (live) c.N("inspect(saved)").Input(K.Read, "saved");
        if (clean) c.N("inspect(direct)").Input(K.Read, "direct");
        c.SharedOnly.UnionWith(["saved", "d", "direct"]); c.ByArgument.Add("lh");
        if (!live) return c.Valid();
        c.UnsuppAt = clean ? ["inspect(saved)"] : [end, "inspect(saved)"];
        return clean ? c.Error(end) : c.Valid();
    }

    // The default taint's analog of B1 (1) (R4-M70): y's default copies x into its pending slot $d, which carries $d's taint, and the
    // result r keeps $d's value (closureRouteD; the compiler's retention also keeps x's, so r joins a clean and a tainted value,
    // closureRouteDJoin, the T2 join R4-M70 accepts: one set, tainted). `y = 5` conflicts with Ly in b's clean set, reached only from r:
    // R16Set gives the Language problem, the route rule the located Unsupported_Kd at the omitting call; with b used later
    // (closureRouteDLive) b is a clean root. The compiler (g148p/r2/d1.kimi) gives the Language problem today in all three.
    static Case DefaultRoute(string id, bool join, bool live)
    {
        var c = new Case(id, "`var b = y@ref; let r = viaD(b@ref); y = 5; inspect(r)" + (live ? "; inspect(b)" : "") + "`" + (join ? "" : " (r keeps only y's value)"));
        c.N("var y = 2").Def("#y");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$x = b@ref").Def("$x").Borrow("Lb", "b", Ref, "$x");
        const string omit = "viaD omits y: its default copies x into $d";
        c.N(omit).Def("$d").Flow("$x", "$d").DefaultTaint("$d");
        var call = c.N("let r = viaD(move $x, move $d)").Def("r").Input(K.Move, "$x").Input(K.Move, "$d").Flow("$d", "r").Flow("$d.*", "r");
        if (join) call.Flow("$x", "r").Flow("$x.*", "r");
        c.N("y = 5").Assign("#y");
        c.N("inspect(r)").Input(K.Read, "r");
        if (live) c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["b", "r"]);
        if (live) return c.Error("y = 5");
        c.UnsuppAt = [omit];
        return c.Valid();
    }

    // The default taint's analog of B1 (2): saveDef(saved@uniq, d) omits v, whose default copies x = d into $v, and stores x and v into
    // saved (guardDefault: saved joins the clean and the tainted value, T2) or v only (guardDefaultOnly); used after the match, saved
    // keeps γ_d only in a tainted set: err(ended) is the located Unsupported_Kd at the omitting call under the route rule (R4-M70 recorded
    // it as a limit); with saved dead, nothing escapes (guardDefaultDead).
    static Case GuardDefault(string id, bool join, bool live)
    {
        var c = new Case(id, "`var saved = other@ref; match packet .Data(let d) if saveDef(saved@uniq, d) => ()" + (live ? "; inspect(saved)" : "") + "`"
            + (join ? "" : " (saveDef stores only v)"));
        c.N("var other = Data(); var packet: Packet = .Data(Data())").Def("#other", "packet");
        c.N("var saved = other@ref").Def("saved").Borrow("Lo", "#other", Ref, "saved");
        c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("$a = &uniq two-phase saved").Def("$a").Borrow("La", "saved", Uniq, "$a", reserve: true);
        const string omit = "saveDef omits v: its default copies x = d into $v";
        c.N(omit).Def("$v").Flow("d", "$v").DefaultTaint("$v");
        var call = c.N("saveDef(move $a, copy d, move $v)").Input(K.Move, "$a").Input(K.Read, "d").Input(K.Move, "$v").Activate("La").Through("$a.*", "$v");
        if (join) call.Through("$a.*", "d");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        if (live) c.N("inspect(saved)").Input(K.Read, "saved");
        c.SharedOnly.UnionWith(["saved", "d"]);
        if (live) c.UnsuppAt = [omit];
        return c.Valid();
    }

    // MirBorrowsTest's cleanRoute (unchanged by the round): a generic iterator over a local Array<T>, both by-argument holders; next's
    // self-row stores the iterator's shared Lxs into xs and, by the Φ_E it read through Lxs, into E, past it (tainted), while it keeps
    // its own value (clean): the append's activation meets Lxs in the live iterator, a Language problem; xs's end meets it only in E.
    static Case CleanRoute()
    {
        var c = new Case("r16-cleanRoute", "`func f<T>(a: T, b: T) { var xs: Array<T> = []; xs.append(a); var it = xs.iterate(); _ = it.next(); xs.append(b); _ = it.next() }`");
        c.N("entry (a: T, b: T)").Param("Lpa", "a").Param("Lpb", "b");
        c.N("var xs: Array<T> = []").Def("xs");
        c.N("$1 = &uniq two-phase xs").Def("$1").Borrow("L1", "xs", Uniq, "$1", reserve: true);
        c.N("xs.append(move $1, copy a)").Input(K.Move, "$1").Input(K.Read, "a").Activate("L1").Store("$1", "a", false, 1);
        c.N("$2 = &xs").Def("$2").Borrow("Lxs", "xs", Ref, "$2");
        c.N("var it = xs.iterate(move $2)").Def("it").Input(K.Move, "$2").Flow("$2", "it").Flow("$2.*", "it");
        c.N("$3 = &uniq two-phase it").Def("$3").Borrow("L3", "it", Uniq, "$3", reserve: true);
        c.N("_ = it.next(move $3)").Input(K.Move, "$3").Activate("L3").Store("$3", "$3", true, 1);
        c.N("$4 = &uniq two-phase xs").Def("$4").Borrow("L4", "xs", Uniq, "$4", reserve: true);
        const string append = "xs.append(move $4, copy b)";
        c.N(append).Input(K.Move, "$4").Input(K.Read, "b").Activate("L4").Store("$4", "b", false, 1);
        c.N("$5 = &uniq two-phase it").Def("$5").Borrow("L5", "it", Uniq, "$5", reserve: true);
        c.N("_ = it.next(move $5)").Input(K.Move, "$5").Activate("L5").Store("$5", "$5", true, 1);
        c.N("StorageDead it").A(K.Write, "it").Def("it");
        c.N("StorageDead xs").A(K.Write, "xs").Def("xs");
        c.ByArgument.UnionWith(["it", "xs"]);
        c.UnsuppAt = ["StorageDead xs"];
        return c.Error(append);
    }
}

// ---- Review of round 16 (its independent review's cases, rv-r16/work/rx, copied unchanged for round 17): the reviewer's counterexamples
// (R4-M76: a conflict that depends on a store past a ByArgument holder is a located Unsupported_Kd; its clean twin a Language problem; its
// no-store twin, the per-part guard's program, valid). UnsuppAt is round 16's decided outcome (route taint), which round 17 supersedes.
static class R16X
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [
        Chain("rx-chain", clean: false), Chain("rx-chainClean", clean: true),
        Loop("rx-loop", clean: false), Loop("rx-loopClean", clean: true),
        JoinCfg("rx-joinCfg"), JoinClosure("rx-joinClosure"),
        ERoute("rx-E", clean: false), ERoute("rx-EClean", clean: true),
        GuardRoute("rx-guardRoute", clean: false, loop: false), GuardRoute("rx-guardRouteClean", clean: true, loop: false),
        GuardRoute("rx-guardLoop", clean: false, loop: true),
        WriteThrough("rx-write", store: true, clean: false), WriteThrough("rx-writeNoStore", store: false, clean: false),
        StoreThrough("rx-storeThrough", store: true), StoreThrough("rx-storeThroughNoStore", store: false),
        ReadThrough("rx-read", store: true), ReadThrough("rx-readNoStore", store: false),
        DChain("rx-dChain", clean: false), DChain("rx-dChainClean", clean: true),
        DLoop("rx-dLoop"), DGuardRoute("rx-dGuardRoute", loop: true),
        DWrite("rx-dWriteOnly"),
        Mixed("rx-mixed"),
        Seed("rx-seed", perPart: false), Seed("rx-seedPerPart", perPart: true),
        WriteLv("rx-writeLv", perPart: false), WriteLv("rx-writeLvPerPart", perPart: true),
        EJoin("rx-eJoin", perPart: false), EJoin("rx-eJoinPerPart", perPart: true), WritePerPart("rx-writePerPart")];

    static void Prefix(Case c, bool uniqW = false, bool lh = true)
    {
        c.N("var x = 1").Def("#x"); c.N("var y = 2").Def("#y");
        c.N("var z = x@ref").Def("z").Borrow("Lx", "#x", Ref, "z");
        c.N(uniqW ? "var w = z@uniq" : "var w = z@ref").Def("w").Borrow("Lz", "z", uniqW, "w");
        if (lh) Lh(c);
    }

    static void Lh(Case c)
    {
        c.N("$1 = &uniq two-phase w").Def("$1").Borrow("Lw", "w", Uniq, "$1", reserve: true);
        c.N("var lh = Loan.init(move $1)").Def("lh").Input(K.Move, "$1").Activate("Lw").Flow("$1", "lh").Flow("$1.*", "lh");
    }

    static Node StoreL(Case c, string src, bool store = true)
    {
        c.N("$p = &uniq two-phase lh").Def("$p").Borrow("Lp", "lh", Uniq, "$p", reserve: true);
        var call = c.N($"storeL(move $p, move {src})").Input(K.Move, "$p").Input(K.Move, src).Activate("Lp");
        if (store) call.Store("$p", "$p", true, 1).Store("$p", src, false, 1);
        return call;
    }

    // Two closure steps past the tainted set: w holds Lb and Lc2 (the stored value and its content), c2 names c3, whose own set holds Ly.
    static Case Chain(string id, bool clean)
    {
        var c = new Case(id, "closureRoute with b = c2@ref, c2 = c3@ref, c3 = y@ref: `storeL(lh@uniq, b@ref); y = 5; inspect(w)`" + (clean ? "; inspect(c3)" : ""));
        Prefix(c);
        c.N("var c3 = y@ref").Def("c3").Borrow("Ly", "#y", Ref, "c3");
        c.N("var c2 = c3@ref").Def("c2").Borrow("Lc3", "c3", Ref, "c2");
        c.N("var b = c2@ref").Def("b").Borrow("Lc2", "c2", Ref, "b");
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        StoreL(c, "$v");
        c.N("y = 5").Assign("#y");
        c.N("inspect(w)").Input(K.Read, "w");
        if (clean) c.N("inspect(c3)").Input(K.Read, "c3");
        c.SharedOnly.UnionWith(["z", "w", "b", "c2", "c3"]); c.ByArgument.Add("lh");
        c.UnsuppAt = clean ? ["inspect(w)"] : ["y = 5", "inspect(w)"];
        return clean ? c.Error("y = 5") : c.Valid();
    }

    // The store inside a loop; the conflict at the head of the next iteration, reached across the back edge (lh and b fresh each turn).
    static Case Loop(string id, bool clean)
    {
        var c = new Case(id, "`loop { y = 5; var b = y@ref; var lh = Loan.init(w@uniq); storeL(lh@uniq, b@ref); inspect(w) }`" + (clean ? " with keep = b@ref used after the head" : ""));
        Prefix(c, lh: false);
        const string head = "loop head: y = 5";
        c.N(head).Assign("#y");
        if (clean) c.N("inspect(keep)").Input(K.Read, "keep");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        if (clean) c.N("var keep = b@ref").Def("keep").Borrow("Lk", "b", Ref, "keep");
        Lh(c);
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        StoreL(c, "$v");
        c.N("inspect(w)").Input(K.Read, "w").Goto(head, "end");
        c.N("end");
        c.SharedOnly.UnionWith(["z", "w", "b", "keep"]); c.ByArgument.Add("lh");
        c.UnsuppAt = clean ? ["inspect(w)"] : [head, "inspect(w)"];
        return clean ? c.Error(head) : c.Valid();
    }

    // A CFG join of a tainted and a clean value in w (T2: w's set carries the taint, so the clean branch's genuine error is Unsupported_Kd).
    static Case JoinCfg(string id)
    {
        var c = new Case(id, "`if c { storeL(lh@uniq, b@ref) } else { w = b@ref }; y = 5; inspect(w)` (T2 join: accepted cost)");
        Prefix(c);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("if c").Goto("$p = &uniq two-phase lh", "w = b@ref");
        StoreL(c, "$v0");
        c.Nodes[^2].Def("$v0").Borrow("Lb", "b", Ref, "$v0"); // $v0 = b@ref formed with $p (one node)
        c.Nodes[^1].Goto("y = 5");
        c.N("w = b@ref").Assign("w").Borrow("Lb2", "b", Ref, "w");
        c.N("y = 5").Assign("#y");
        c.N("inspect(w)").Input(K.Read, "w");
        c.SharedOnly.UnionWith(["z", "w", "b"]); c.ByArgument.Add("lh");
        c.UnsuppAt = ["y = 5", "inspect(w)"];
        return c.Valid();
    }

    // A closure-level join: a clean live holder also names b, so b is read clean and `y = 5` stays the Language problem.
    static Case JoinClosure(string id)
    {
        var c = new Case(id, "closureRoute with `var keep = b@ref` used after `y = 5`");
        Prefix(c);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("var keep = b@ref").Def("keep").Borrow("Lk", "b", Ref, "keep");
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        StoreL(c, "$v");
        c.N("y = 5").Assign("#y");
        c.N("inspect(w)").Input(K.Read, "w");
        c.N("inspect(keep)").Input(K.Read, "keep");
        c.SharedOnly.UnionWith(["z", "w", "b", "keep"]); c.ByArgument.Add("lh");
        c.UnsuppAt = ["inspect(w)"];
        return c.Error("y = 5");
    }

    // Through E: lh is a parameter (its set holds Φ_E), so the store past lh reaches E, which is tainted and names b.
    static Case ERoute(string id, bool clean)
    {
        var c = new Case(id, "`func f(lh: uniq/Loan<uniq/(ref/(ref/i32))>) { var b = y@ref; storeL(lh@uniq, b@ref); y = 5 }`" + (clean ? "; inspect(b)" : ""));
        c.N("entry (lh: uniq/Loan<…>)").Param("Lpa", "lh");
        c.N("var y = 2").Def("#y");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        StoreL(c, "$v");
        c.N("y = 5").Assign("#y");
        if (clean) c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["b"]); c.ByArgument.Add("lh");
        if (clean) return c.Error("y = 5");
        c.UnsuppAt = ["y = 5"];
        return c.Valid();
    }

    // γ_d two closure steps past the tainted saved: storeD stores qq@ref (its content Lq), q = copy d holds γ_d.
    static Case GuardRoute(string id, bool clean, bool loop)
    {
        var c = new Case(id, "`match packet .Data(let d) if { let q = d; let qq = q@ref; storeD(lh@uniq, qq@ref) } => ()" + (clean ? "; inspect(q)" : "") + "; inspect(saved)`" + (loop ? " in a loop" : ""));
        c.N("var other = Data(); var packet: Packet = .Data(Data())").Def("#other", "packet");
        c.N("var saved = other@ref").Def("saved").Borrow("Lo", "#other", Ref, "saved");
        const string head = "$1 = &uniq two-phase saved";
        c.N(head).Def("$1").Borrow("Ls", "saved", Uniq, "$1", reserve: true);
        c.N("var lh = Loan.init(move $1)").Def("lh").Input(K.Move, "$1").Activate("Ls").Flow("$1", "lh").Flow("$1.*", "lh");
        c.N("$s = packet@ref (Subject, shared mode)").Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("let q = copy d").Def("q").Input(K.Read, "d").Flow("d", "q");
        c.N("let qq = q@ref").Def("qq").Borrow("Lq", "q", Ref, "qq");
        c.N("$v = qq@ref").Def("$v").Borrow("Lqq", "qq", Ref, "$v");
        c.N("$p = &uniq two-phase lh").Def("$p").Borrow("Lp", "lh", Uniq, "$p", reserve: true);
        c.N("storeD(move $p, move $v)").Input(K.Move, "$p").Input(K.Move, "$v").Activate("Lp").Store("$p", "$p", true, 1).Store("$p", "$v", false, 1);
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        if (clean) c.N("inspect(q)").Input(K.Read, "q");
        var last = c.N("inspect(saved)").Input(K.Read, "saved");
        if (loop) { last.Goto(head, "after"); c.N("after"); }
        c.SharedOnly.UnionWith(["saved", "d", "q", "qq"]); c.ByArgument.Add("lh");
        c.UnsuppAt = clean ? ["inspect(saved)"] : loop ? [end, "inspect(saved)", head] : [end, "inspect(saved)"];
        return clean ? c.Error(end) : c.Valid();
    }

    // A write through the tainted w (`w@follow = c@ref`): its targets are refs(σ(w)), so b, which w names only through the stored Lbu,
    // receives Lc in a clean set; `c = 5` with b used later. Without the store (the per-part guard's program) the write reaches only z.
    static Case WriteThrough(string id, bool store, bool clean)
    {
        var c = new Case(id, "`var w = z@uniq; var lh = Loan<uniq/(uniq/(ref/i32))>.init(w@uniq); var b = y@ref; storeU(lh@uniq, b@uniq); w@follow = c@ref; c = 5; inspect(b)`" + (store ? "" : " (no store row past lh)"));
        Prefix(c, uniqW: true);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@uniq").Def("$v").Borrow("Lbu", "b", Uniq, "$v");
        StoreL(c, "$v", store);
        c.N("var c = 3").Def("#c");
        c.N("$c = c@ref").Def("$c").Borrow("Lc", "#c", Ref, "$c");
        c.N("w@follow = move $c").Input(K.Move, "$c").Through("w.*", "$c");
        c.N("c = 5").Assign("#c");
        c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["z", "b"]); c.ByArgument.Add("lh");
        if (!store) return c.Valid();
        c.UnsuppAt = ["w@follow = move $c", "c = 5"];
        return c.Valid();
    }

    // A second call's store row through the tainted w: rule G's W steps from w (Through) along the stored Lbu into b, clean.
    static Case StoreThrough(string id, bool store)
    {
        var c = new Case(id, "`… storeU(lh@uniq, b@uniq); put(w@uniq, c@ref); c = 5; inspect(b)` (put stores p@follow@follow = v)" + (store ? "" : " (no store row past lh)"));
        Prefix(c, uniqW: true);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@uniq").Def("$v").Borrow("Lbu", "b", Uniq, "$v");
        StoreL(c, "$v", store);
        c.N("var c = 3").Def("#c");
        c.N("$s = &uniq two-phase w").Def("$s").Borrow("Ls2", "w", Uniq, "$s", reserve: true);
        c.N("$c = c@ref").Def("$c").Borrow("Lc", "#c", Ref, "$c");
        c.N("put(move $s, move $c)").Input(K.Move, "$s").Input(K.Move, "$c").Activate("Ls2").Store("$s", "$c", false, 2);
        c.N("c = 5").Assign("#c");
        c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["z", "b"]); c.ByArgument.Add("lh");
        if (!store) return c.Valid();
        c.UnsuppAt = ["put(move $s, move $c)", "c = 5"];
        return c.Valid();
    }

    // A read through the tainted w: `let v = w@follow` reads content(σ(w)).
    static Case ReadThrough(string id, bool store)
    {
        var c = new Case(id, "closureRoute, then `let v = w@follow; y = 5; inspect(v)`" + (store ? "" : " (no store row past lh)"));
        Prefix(c);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        StoreL(c, "$v", store);
        c.N("let v = w@follow").Def("v").Input(K.Read, "w.*").Flow("w.*", "v");
        c.N("y = 5").Assign("#y");
        c.N("inspect(v)").Input(K.Read, "v");
        c.SharedOnly.UnionWith(["z", "w", "b", "v"]); c.ByArgument.Add("lh");
        if (!store) return c.Valid();
        c.UnsuppAt = ["let v = w@follow", "y = 5"];
        return c.Valid();
    }

    // The default taint's analogs (the compiler's join form: r keeps x's and the default's values).
    static Case DChain(string id, bool clean)
    {
        var c = new Case(id, "`var c3 = y@ref; var c2 = c3@ref; var b = c2@ref; let r = viaD(b@ref); y = 5; inspect(r)`" + (clean ? "; inspect(c3)" : ""));
        c.N("var y = 2").Def("#y");
        c.N("var c3 = y@ref").Def("c3").Borrow("Ly", "#y", Ref, "c3");
        c.N("var c2 = c3@ref").Def("c2").Borrow("Lc3", "c3", Ref, "c2");
        c.N("var b = c2@ref").Def("b").Borrow("Lc2", "c2", Ref, "b");
        c.N("$x = b@ref").Def("$x").Borrow("Lb", "b", Ref, "$x");
        const string omit = "viaD omits y: its default copies x into $d";
        c.N(omit).Def("$d").Flow("$x", "$d").DefaultTaint("$d");
        c.N("let r = viaD(move $x, move $d)").Def("r").Input(K.Move, "$x").Input(K.Move, "$d").Flow("$d", "r").Flow("$d.*", "r").Flow("$x", "r").Flow("$x.*", "r");
        c.N("y = 5").Assign("#y");
        c.N("inspect(r)").Input(K.Read, "r");
        if (clean) c.N("inspect(c3)").Input(K.Read, "c3");
        c.SharedOnly.UnionWith(["b", "r", "c2", "c3"]);
        if (clean) return c.Error("y = 5");
        c.UnsuppAt = [omit];
        return c.Valid();
    }

    static Case DLoop(string id)
    {
        var c = new Case(id, "`loop { y = 5; var b = y@ref; r = viaD(b@ref); inspect(r) }`");
        c.N("var y = 2").Def("#y");
        const string head = "loop head: y = 5";
        c.N(head).Assign("#y");
        c.N("inspect(r) (the previous turn's)").Input(K.Read, "r");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$x = b@ref").Def("$x").Borrow("Lb", "b", Ref, "$x");
        const string omit = "viaD omits y: its default copies x into $d";
        c.N(omit).Def("$d").Flow("$x", "$d").DefaultTaint("$d");
        c.N("r = viaD(move $x, move $d)").Def("r").Input(K.Move, "$x").Input(K.Move, "$d").Flow("$d", "r").Flow("$d.*", "r").Flow("$x", "r").Flow("$x.*", "r");
        c.N("inspect(r)").Input(K.Read, "r").Goto(head, "end");
        c.N("end");
        c.SharedOnly.UnionWith(["b", "r"]);
        c.UnsuppAt = [omit];
        return c.Valid();
    }

    // guardDefault with γ_d two steps past saved, in a loop.
    static Case DGuardRoute(string id, bool loop)
    {
        var c = new Case(id, "`match packet .Data(let d) if { let q = d; let qq = q@ref; saveDef(saved@uniq, qq@ref) } => (); inspect(saved)`" + (loop ? " in a loop" : ""));
        c.N("var other = Data(); var packet: Packet = .Data(Data())").Def("#other", "packet");
        c.N("var saved = other@ref").Def("saved").Borrow("Lo", "#other", Ref, "saved");
        const string head = "$s = packet@ref (Subject, shared mode)";
        c.N(head).Def("$s").Borrow("Lsub", "packet", Ref, "$s");
        c.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        c.N("let q = copy d").Def("q").Input(K.Read, "d").Flow("d", "q");
        c.N("let qq = q@ref").Def("qq").Borrow("Lq", "q", Ref, "qq");
        c.N("$x = qq@ref").Def("$x").Borrow("Lqq", "qq", Ref, "$x");
        c.N("$a = &uniq two-phase saved").Def("$a").Borrow("La", "saved", Uniq, "$a", reserve: true);
        const string omit = "saveDef omits v: its default copies x into $v";
        c.N(omit).Def("$v").Flow("$x", "$v").DefaultTaint("$v");
        c.N("saveDef(move $a, move $x, move $v)").Input(K.Move, "$a").Input(K.Move, "$x").Input(K.Move, "$v").Activate("La").Through("$a.*", "$v").Through("$a.*", "$x");
        const string end = "guard cleanup: EndBorrow d";
        c.N(end).EndBorrow("d");
        var last = c.N("inspect(saved)").Input(K.Read, "saved");
        if (loop) { last.Goto(head, "after"); c.N("after"); }
        c.SharedOnly.UnionWith(["saved", "d", "q", "qq"]);
        c.UnsuppAt = [omit];
        return c.Valid();
    }

    // The default's "only" form (not expressible in the compiler): r keeps only the default's value; a write through r reaches b.
    static Case DWrite(string id)
    {
        var c = new Case(id, "`var b = y@ref; let r = viaU(b@uniq) (r keeps only the default's value); r@follow = c@ref; c = 5; inspect(b)`");
        c.N("var y = 2").Def("#y");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$x = b@uniq").Def("$x").Borrow("Lbu", "b", Uniq, "$x");
        const string omit = "viaU omits y: its default moves x into $d";
        c.N(omit).Def("$d").Flow("$x", "$d").DefaultTaint("$d");
        c.N("let r = viaU(move $d)").Def("r").Input(K.Move, "$d").Flow("$d", "r").Flow("$d.*", "r");
        c.N("var c = 3").Def("#c");
        c.N("$c = c@ref").Def("$c").Borrow("Lc", "#c", Ref, "$c");
        c.N("r@follow = move $c").Input(K.Move, "$c").Through("r.*", "$c");
        c.N("c = 5").Assign("#c");
        c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["b"]);
        c.UnsuppAt = [omit];
        return c.Valid();
    }

    // Two kinds on one route: w (P13's τ13 only) names rr, which names r (its own set carries the default's τD), which names b (Ly).
    static Case Mixed(string id)
    {
        var c = new Case(id, "`var b = y@ref; let r = viaD(b@ref); var rr = r@ref; storeL(lh@uniq, rr@ref); y = 5; inspect(w)`");
        Prefix(c);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$x = b@ref").Def("$x").Borrow("Lb", "b", Ref, "$x");
        const string omit = "viaD omits y: its default copies x into $d";
        c.N(omit).Def("$d").Flow("$x", "$d").DefaultTaint("$d");
        c.N("let r = viaD(move $x, move $d)").Def("r").Input(K.Move, "$x").Input(K.Move, "$d").Flow("$d", "r").Flow("$d.*", "r").Flow("$x", "r").Flow("$x.*", "r");
        c.N("var rr = r@ref").Def("rr").Borrow("Lr", "r", Ref, "rr");
        c.N("$v = rr@ref").Def("$v").Borrow("Lrr", "rr", Ref, "$v");
        StoreL(c, "$v");
        c.N("y = 5").Assign("#y");
        c.N("inspect(w)").Input(K.Read, "w");
        c.SharedOnly.UnionWith(["z", "w", "b", "r", "rr"]); c.ByArgument.Add("lh");
        c.UnsuppAt = [omit, "inspect(w)"];
        return c.Valid();
    }
    // E5's seed (the compiler's seed.kimi): storeU's l → v row (E14, layer 1) puts lh's Lw into b; the P13 row makes w name b (Lbu, tainted);
    // `let r = w@follow@follow@ref` borrows through w, so b is a seed holder of the stored r, read clean, and the borrow meets Lw there.
    // perPart: the per-part guard's program (W stops at lh), every row kept.
    static Case Seed(string id, bool perPart)
    {
        var c = new Case(id, "`var w = z@uniq; var lh = Loan.init(w@uniq); var b = y@ref; storeU(lh@uniq, b@uniq); let r = w@follow@follow@ref; inspect(r)`" + (perPart ? " (per-part guard)" : ""));
        Prefix(c, uniqW: true);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@uniq").Def("$v").Borrow("Lbu", "b", Uniq, "$v");
        StoreL(c, "$v").Store("$v", "$p", true, 1).Store("$v", "$v", true, 1);
        c.N("let r = w@follow@follow@ref").Def("r").Borrow("Lr", "w.*.*", Ref, "r", sharedAt: 2);
        c.N("inspect(r)").Input(K.Read, "r");
        c.SharedOnly.UnionWith(["z", "b", "r"]);
        if (perPart) { c.SharedOnly.Add("lh"); return c.Valid(); }
        c.ByArgument.Add("lh");
        c.UnsuppAt = ["let r = w@follow@follow@ref"];
        return c.Valid();
    }

    // The compiler's writeRoute (write.kimi) with storeU's l → v and v → v rows; perPart: the per-part guard's program.
    static Case WriteLv(string id, bool perPart)
    {
        var c = new Case(id, "`… storeU(lh@uniq, b@uniq); w@follow = c@ref; c = 5; inspect(b)` with storeU's rows into v" + (perPart ? " (per-part guard)" : ""));
        Prefix(c, uniqW: true);
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$v = b@uniq").Def("$v").Borrow("Lbu", "b", Uniq, "$v");
        StoreL(c, "$v").Store("$v", "$p", true, 1).Store("$v", "$v", true, 1);
        c.N("var c = 3").Def("#c");
        c.N("$c = c@ref").Def("$c").Borrow("Lc", "#c", Ref, "$c");
        c.N("w@follow = move $c").Input(K.Move, "$c").Through("w.*", "$c");
        c.N("c = 5").Assign("#c");
        c.N("inspect(b)").Input(K.Read, "b");
        c.SharedOnly.UnionWith(["z", "b"]);
        if (perPart) { c.SharedOnly.Add("lh"); return c.Valid(); }
        c.ByArgument.Add("lh");
        c.UnsuppAt = ["c = 5"];
        return c.Valid();
    }
    // E's T2 join through the route: b's reference escaped into raw storage (E, clean); an unrelated store past lh (a parameter) taints E.
    static Case EJoin(string id, bool perPart)
    {
        var c = new Case(id, "`func f(lh: uniq/Loan<…>, p: raw) { var b = y@ref; Raw.initialize(p, b@ref); storeL(lh@uniq, u@ref); y = 5 }`" + (perPart ? " (per-part guard)" : ""));
        c.N("entry (lh: uniq/Loan<…>, p)").Param("Lpa", "lh");
        c.N("var y = 2; var u = 3").Def("#y", "#u");
        c.N("var b = y@ref").Def("b").Borrow("Ly", "#y", Ref, "b");
        c.N("$r = b@ref").Def("$r").Borrow("Lb", "b", Ref, "$r");
        c.N("Raw.initialize(p, move $r)").Input(K.Move, "$r").RawStore("%p", "$r");
        c.N("$v = u@ref").Def("$v").Borrow("Lu", "#u", Ref, "$v");
        StoreL(c, "$v");
        c.N("y = 5").Assign("#y");
        c.SharedOnly.UnionWith(["b"]);
        if (perPart) c.SharedOnly.Add("lh"); else c.ByArgument.Add("lh");
        return c.Error("y = 5");
    }
    // rx-write's per-part twin: every row kept, W stops at lh (the per-part guard takes no step past Loan<T>).
    static Case WritePerPart(string id)
    {
        var c = WriteThrough(id, store: true, clean: false);
        c.ByArgument.Clear(); c.SharedOnly.Add("lh"); c.UnsuppAt = [];
        return c.Valid();
    }
}

// ---- Round 18 (ER-effects, A5-F5; unit 2x-P5): statics are '#' roots (Case.Statics), effects A4's keys and classes at a Call, a Drop or a
// staticEnsure. Err is SPEC's verdict; At and UnsuppAt are R18's expected outcome. An explicit static Borrow (`State.count@ref`) is Binding's
// InvalidAssignment_Kd until P42 (G100, PD12 (i)), so a case that needs one says P42; the others use implicit static Loans (a writeback, a
// static receiver). A staticEnsure whose initializer has no keys is left out.
static class R18
{
    const bool Uniq = true, Ref = false;
    const string Count = "#State:count", Log = "#State:log", Counter = "#State:counter", HolderS = "#State:holder", Action = "#State:action",
        Text = "#State:text", Lim = "#State:limit", Xs = "#State:x", CacheX = "#Cache:x";
    public static IEnumerable<Case> All() => [Compound("e1"), Compound("e2"), Compound("e3"), LiveUniq(), Receiver("e4", read: true),
        Receiver("e5", read: false), MovedArgument(), TwoFailures(), DropHolder("e6", write: true), DropHolder("e7", write: false),
        OwnDrop("e8"), OwnDrop("e8b"), OwnDrop("e8c"), Erased("e9"), Erased("e9b"), Erased("e9s"),
        Ensure("e10a"), Ensure("e10b"), Ensure("e10c"), Ensure("e10d"), Ensure("e10e"), EnsureJoin(), Run("e11", both: true), Run("e11b", both: false),
        Classes("e12a"), Classes("e12b"), Classes("e12c"), Classes("e13a"), Classes("e13b"), Classes("e14"),
        Generic("e15", copy: false), Generic("e15b", copy: true), Lemma("e16a"), Lemma("e16b"), Lemma("e16c"), Held()];

    static Case S(string id, string title, params string[] mutableStatics)
    {
        var c = new Case(id, title);
        foreach (var s in mutableStatics) c.Statics[s] = true;
        return c;
    }

    // e1-e3 (SPEC 15.6.4, 15.6.7): `State.counter.add(f())` reserves its receiver State.counter in $w, located first, and activates Lw at
    // add's call; the argument's call's effects meet Lw, shared in its holder: g's unknown effects and w's write conflict with it, m's read
    // does not. Corrected by unit 2b-3: these cases first modelled `State.count += f()` with the writeback's reservation pending during
    // f(), an evaluation-order error, since SPEC 13.7.2 secures the right operand before it locates and reserves the target, so that form
    // meets nothing (valid).
    static Case Compound(string id)
    {
        var (call, what) = id switch { "e1" => ("$v = g()", "g a common Function value: unknown effects"), "e2" => ("$v = m()", "m reads State.counter"), _ => ("$v = w()", "w writes State.counter") };
        var c = S(id, $"`State.counter.add({call[5..]})` ({what})", Counter);
        c.N("$w = TwoPhaseUniq(State.counter) (receiver)").Def("$w").Borrow("Lw", Counter, Uniq, "$w", reserve: true);
        var n = c.N(call).Def("$v");
        if (id == "e1") n.Fx(unknown: true); else n.Key(Counter, write: id == "e3");
        c.N("add($w, $v) (activates Lw)").A(K.Read, "$w.*").Activate("Lw").Through("$w.*", "$v");
        return id == "e2" ? c.Valid() : c.Error(call);
    }

    // e2u: a read key meets a live uniq Loan.
    static Case LiveUniq()
    {
        var c = S("e2u", "`var u = State.count@uniq; m(); u@follow = 1` (m reads State.count; P42)", Count);
        c.N("var u = State.count@uniq").Def("u").Borrow("Lu", Count, Uniq, "u");
        c.N("m()").Key(Count);
        c.N("u@follow = 1").A(K.Write, "u.*");
        return c.Error("m()");
    }

    // e4, e5 (W6; SPEC 15.6.7: every reservation becomes live at once with the call's effects on static storage): a method on a static
    // receiver, whose reservation Lr the Call activates, so its effects meet Lr as uniq.
    static Case Receiver(string id, bool read)
    {
        var c = S(id, $"`State.counter.bump()` (bump(self: uniq/Counter) {(read ? "reads State.counter through its group" : "touches no static")})", Counter);
        c.N("$r = &uniq two-phase State.counter (receiver, reserved)").Def("$r").Borrow("Lr", Counter, Uniq, "$r", reserve: true);
        var n = c.N("bump(move $r)").Input(K.Move, "$r").Activate("Lr");
        if (read) n.Key(Counter);
        return read ? c.Error("bump(move $r)") : c.Valid();
    }

    // e4m (E13 (1)(b): an operand's value, its holder dead after the Call): writer receives s, which holds a borrow of State.count.
    static Case MovedArgument()
    {
        var c = S("e4m", "`var s = Holder(r: State.count@ref); writer(s@move)` (writer writes State.count; P42)", Count);
        c.N("var s = Holder(r: State.count@ref)").Def("s").Borrow("Lc", Count, Ref, "s");
        c.N("writer(move s)").Input(K.Move, "s").Key(Count, write: true);
        return c.Error("writer(move s)");
    }

    // e4c (2b-3: an activation and an effect on one Loan are two failures): the activation meets Lc, the effect Lc and Lr (W6).
    static Case TwoFailures()
    {
        var c = S("e4c", "`let c = State.counter@ref; State.counter.add(1); inspect(c)` (add writes State.counter through its group; P42)", Counter);
        c.N("let c = State.counter@ref").Def("c").Borrow("Lc", Counter, Ref, "c");
        c.N("$r = &uniq two-phase State.counter (receiver, reserved)").Def("$r").Borrow("Lr", Counter, Uniq, "$r", reserve: true);
        c.N("add(move $r, 1)").Input(K.Move, "$r").Activate("Lr").Key(Counter, write: true);
        c.N("inspect(c)").Input(K.Read, "c");
        return c.Error("add(move $r, 1)");
    }

    // e6, e7 (E13 (1)(d); SPEC 15.6: a Loan is live up to an observation by its destruction): s's own drop runs while s holds Lc.
    static Case DropHolder(string id, bool write)
    {
        var c = S(id, $"`var s = Holder(r: State.count@ref); inspect(s)`; at scope end Holder's drop {(write ? "writes" : "reads")} State.count (P42)", Count);
        c.N("var s = Holder(r: State.count@ref)").Def("s").Borrow("Lc", Count, Ref, "s");
        c.N("inspect(s)").Input(K.Read, "s");
        c.N("scope end: drop s").Drop("s").Key(Count, write);
        return write ? c.Error("scope end: drop s") : c.Valid();
    }

    // e8, e8b, e8c (R3; SPEC 16.4, S16:220: destruction holds an exclusive Loan on the destroyed storage, and any other access to it, through
    // a static Field included, conflicts): replacing a static's value destroys the old one, whose drop reads that static by name (e8), touches
    // no static (e8b), or touches none but lies past A4's Limit (e8c: the located Unsupported_Kd).
    static Case OwnDrop(string id)
    {
        var what = id switch { "e8" => "reads State.holder through its group", "e8b" => "touches no static", _ => "touches no static, past A4's Limit" };
        var c = S(id, $"`State.holder = Holder.init()` (the old value's drop {what})", HolderS);
        const string drop = "drop State.holder (old value)";
        c.N("$n = Holder.init()").Def("$n");
        var d = c.N(drop).Drop(HolderS);
        if (id == "e8") d.Key(HolderS); else if (id == "e8c") d.Fx(limit: true);
        c.N("State.holder = move $n").Input(K.Move, "$n").Assign(HolderS);
        if (id == "e8c") c.UnsuppAt = [drop];
        return id == "e8" ? c.Error(drop) : c.Valid();
    }

    // e9 (R3 with SPEC 16.3.3, PD12 (iii)): the old environment of a mutable static of a common Function Type is an erased destruction,
    // whose unknown effects meet its own exclusive Loan; e9b: `exchange` moves the old value out, and its later erased destruction meets no
    // live mutable-static Loan; e9s: SPEC 16.3.3's example.
    static Case Erased(string id)
    {
        if (id == "e9s")
        {
            var s = S(id, "SPEC 16.3.3: `var action: () -> () = func [resource@move] () => (); let view = State.text@ref; action = func [] () => (); inspect(view)`", Text);
            s.N("var action = func [resource@move] () => ()").Def("action");
            s.N("let view = State.text@ref").Def("view").Borrow("Lv", Text, Ref, "view");
            s.N("$f = func [] () => ()").Def("$f");
            s.N("drop action (old environment: erased)").Drop("action").Fx(unknown: true);
            s.N("action = move $f").Input(K.Move, "$f").Assign("action");
            s.N("inspect(view)").Input(K.Read, "view");
            return s.Error("drop action (old environment: erased)");
        }
        var c = S(id, id == "e9" ? "`State.action = func [] () => ()` (State.action: () -> (), mutable)"
            : "`let old = exchange(State.action@uniq, with: func [] () => ())`; scope end drops old (erased)", Action);
        c.N("$f = func [] () => ()").Def("$f");
        if (id == "e9")
        {
            c.N("drop State.action (old environment: erased)").Drop(Action).Fx(unknown: true);
            c.N("State.action = move $f").Input(K.Move, "$f").Assign(Action);
            return c.Error("drop State.action (old environment: erased)");
        }
        c.N("$t = &uniq two-phase State.action (reserved)").Def("$t").Borrow("Lt", Action, Uniq, "$t", reserve: true);
        c.N("let old = exchange(move $t, move $f)").Def("old").Input(K.Move, "$t").Input(K.Move, "$f").Activate("Lt");
        c.N("scope end: drop old (erased)").Drop("old").Fx(unknown: true);
        return c.Valid();
    }

    // e10a-e10d (R4; SPEC 15.6.4: a live Loan anchored to a Field proves only that Field initialized; PD12 (ii)): a staticEnsure of x takes
    // x's initializer's keys (it writes State.log); a live Loan on x proves x initialized, so the initializer does not run, but under P5 A5
    // does not know that Loan's key is the ensured one (e10d: a Loan on Cache<i32>'s key proves nothing for Cache<string>'s), so where the
    // proof decides, the ensure is a located Unsupported_Kd. e10b: no proof; e10c: nothing for the initializer to meet. P42 for lx and ll.
    static Case Ensure(string id)
    {
        bool proof = id != "e10b", other = id != "e10c", generic = id is "e10d" or "e10e", same = id == "e10e";
        string f = generic ? CacheX : Xs, xi = generic && !same ? "Cache<i32>.S.x" : generic ? "Cache<string>.S.x" : "State.x", xe = generic ? "Cache<string>.S.x" : "State.x";
        var c = S(id, (proof ? $"`var lx = {xi}@ref; " : "`") + (other ? "var ll = State.log@ref; " : "") + $"let v = {xe}; inspect(…)` (x's initializer writes State.log)", f, Log);
        if (proof)
        {
            c.N($"staticEnsure({xi})").StaticEnsure(f, generic ? (same ? "string" : "i32") : "").Key(Log, write: true);
            c.N($"var lx = {xi}@ref").Def("lx").Borrow("Lx", f, Ref, "lx");
            if (generic) c.StaticKey["Lx"] = same ? "string" : "i32";
        }
        if (other) c.N("var ll = State.log@ref").Def("ll").Borrow("Ll", Log, Ref, "ll");
        var ensure = $"staticEnsure({xe}) for `let v`";
        c.N(ensure).StaticEnsure(f, generic ? "string" : "").Key(Log, write: true);
        c.N($"let v = {xe}").Def("v").A(K.Read, f);
        c.N("inspect(lx), inspect(ll)").Use(new[] { proof ? "lx" : null, other ? "ll" : null }.Where(x => x != null).ToArray());
        if (proof && other) c.UnsuppAt = [ensure];
        return id == "e10b" ? c.Error(ensure) : id == "e10d" ? c.Error() : c.Valid();
    }

    // Reviewer's join case (2b-D package check): lx carries x's Loan on one path only; on the else path State.x is never accessed, so
    // the ensure's initializer may run there and write State.log while ll lives (a may-live Loan proves nothing on a path it is absent from).
    static Case EnsureJoin()
    {
        var c = S("e10f", "`var o = 1; var lx = o@ref; if k => (lx = State.x@ref); var ll = State.log@ref; let v = State.x; inspect(…)` (x's initializer writes State.log)", Xs, Log);
        c.N("var o = 1").Def("o");
        c.N("var lx = o@ref").Def("lx").Borrow("Lo", "o", Ref, "lx");
        c.N("if k").Goto("staticEnsure(State.x) for `lx =`", "var ll = State.log@ref");
        c.N("staticEnsure(State.x) for `lx =`").StaticEnsure(Xs, "").Key(Log, write: true);
        c.N("lx = State.x@ref").Def("lx").Borrow("Lx", Xs, Ref, "lx");
        c.N("var ll = State.log@ref").Def("ll").Borrow("Ll", Log, Ref, "ll");
        var ensure = "staticEnsure(State.x) for `let v`";
        c.N(ensure).StaticEnsure(Xs, "").Key(Log, write: true);
        c.N("let v = State.x").Def("v").A(K.Read, Xs);
        c.N("inspect(lx), inspect(ll)").Use("lx", "ll");
        c.UnsuppAt = [ensure];
        return c.Error();
    }

    // e11, e11b: SPEC 15.6.4's `run`: g's unknown effects meet c's Loan on the mutable State.count, never l's on the immutable State.limit.
    static Case Run(string id, bool both)
    {
        var c = S(id, both ? "SPEC 15.6.4 `run`: `let c = State.count@ref; let l = State.limit@ref; g(); inspect(c); inspect(l)`"
            : "`run` with l alone: `let l = State.limit@ref; g(); inspect(l)`", Count);
        c.Statics[Lim] = false;
        if (both) c.N("let c = State.count@ref").Def("c").Borrow("Lc", Count, Ref, "c");
        c.N("let l = State.limit@ref").Def("l").Borrow("Ll", Lim, Ref, "l");
        c.N("g()").Fx(unknown: true);
        if (both) c.N("inspect(c)").Input(K.Read, "c");
        c.N("inspect(l)").Input(K.Read, "l");
        return both ? c.Error("g()") : c.Valid();
    }

    // e12a-e14 (R1, G93): a call while c lives. e12a: A4's Limit (f's summary past A4's bound; f only reads State.count) is a located
    // Unsupported_Kd, never a Language error; e12b: with c dead, nothing; e12c: Limit and Unknown, Unknown decides. e13a: f's only unknown is
    // Derived (a callee resting on an error, an Opaque site, R4-M10): A5 reports nothing, the program's error being that site's, outside A5;
    // e13b: Derived and a write key, whose conflict stays. e14: Unknown with c dead.
    static Case Classes(string id)
    {
        var c = S(id, id switch
        {
            "e12a" => "`let c = State.count@ref; f(); inspect(c)` (f: A4's Limit; f reads State.count)",
            "e12b" => "`let c = State.count@ref; inspect(c); f()` (f: A4's Limit)",
            "e12c" => "`let c = State.count@ref; f(); inspect(c)` (f: Limit and Unknown)",
            "e13a" => "`let c = State.count@ref; f(); inspect(c)` (f: Derived only)",
            "e13b" => "`let c = State.count@ref; f(); inspect(c)` (f: Derived, and it writes State.count)",
            _ => "`let c = State.count@ref; inspect(c); g()` (g: unknown effects)",
        } + " (P42)", Count);
        bool dead = id is "e12b" or "e14";
        var call = id == "e14" ? "g()" : "f()";
        c.N("let c = State.count@ref").Def("c").Borrow("Lc", Count, Ref, "c");
        if (dead) c.N("inspect(c)").Input(K.Read, "c");
        var n = c.N(call).Fx(unknown: id is "e12c" or "e14", limit: id.StartsWith("e12"), derived: id.StartsWith("e13"));
        if (id is "e12a" or "e13b") n.Key(Count, write: id == "e13b");
        if (!dead) c.N("inspect(c)").Input(K.Read, "c");
        if (id == "e12a") c.UnsuppAt = [call];
        return id is "e12c" or "e13b" ? c.Error(call) : c.Valid();
    }

    // e15, e15b (SPEC 16.3.3: in a generic body, destroying an abstract non-Copy Type is unknown for the body's own checks): sink<U>(v: U)
    // destroys v, a parameter-dependent effect f composes with its own T.
    static Case Generic(string id, bool copy)
    {
        var c = S(id, $"`func f<T>(x: T){(copy ? " where T is Copy" : "")} => State.count += sink(x)` (sink<U>(v: U) destroys v: {(copy ? "nothing for a Copy T" : "unknown in f")})", Count);
        c.N("entry (x: T)").Param("Lpx", "x");
        c.N("$w = TwoPhaseUniq(State.count) (writeback)").Def("$w").Borrow("Lw", Count, Uniq, "$w", reserve: true);
        var n = c.N("$v = sink(move x)").Def("$v").Input(K.Move, "x");
        if (!copy) n.Fx(unknown: true);
        c.N("(*$w) = (*$w) + $v (activates Lw)").A(K.Read, "$w.*").Activate("Lw").Through("$w.*", "$v");
        return copy ? c.Valid() : c.Error("$v = sink(move x)");
    }

    // e16a-e16c (G136's lemma, PD12 (iv)): a static's whole write hits every live Loan on it, so whether it also ends them changes no
    // verdict: e16a errs at the write either way; e16b's second write meets Lc again only where the first ends nothing; e16c: c dead.
    static Case Lemma(string id)
    {
        var c = S(id, id switch
        {
            "e16a" => "`var c = State.count@ref; State.count = 5; inspect(c)`",
            "e16b" => "`var c = State.count@ref; State.count = 5; State.count = 6; inspect(c)`",
            _ => "`var c = State.count@ref; inspect(c); State.count = 5`",
        } + " (P42)", Count);
        c.N("var c = State.count@ref").Def("c").Borrow("Lc", Count, Ref, "c");
        if (id == "e16c") c.N("inspect(c)").Input(K.Read, "c");
        c.N("State.count = 5").Assign(Count);
        if (id == "e16b") c.N("State.count = 6").Assign(Count);
        if (id != "e16c") c.N("inspect(c)").Input(K.Read, "c");
        return id switch { "e16a" => c.Error("State.count = 5"), "e16b" => c.Error("State.count = 5", "State.count = 6"), _ => c.Valid() };
    }

    // e17 (R4-M73: err(effect) reports through the common rule): e11's conflict in a held body, where storeL stores past lh, a ByArgument
    // holder (round 16's closureRoute prefix), is the located Unsupported_Kd.
    static Case Held()
    {
        var c = S("e17", "`var z = x@ref; var w = z@ref; var lh = Loan.init(w@uniq); var b = x@ref; storeL(lh@uniq, b@ref); let c = State.count@ref; g(); inspect(c)` (held; P42)", Count);
        c.N("var x = 1").Def("#x");
        c.N("var z = x@ref").Def("z").Borrow("Lx", "#x", Ref, "z");
        c.N("var w = z@ref").Def("w").Borrow("Lz", "z", Ref, "w");
        c.N("$1 = &uniq two-phase w").Def("$1").Borrow("Lw", "w", Uniq, "$1", reserve: true);
        c.N("var lh = Loan.init(move $1)").Def("lh").Input(K.Move, "$1").Activate("Lw").Flow("$1", "lh").Flow("$1.*", "lh");
        c.N("var b = x@ref").Def("b").Borrow("Lb0", "#x", Ref, "b");
        c.N("$p = &uniq two-phase lh").Def("$p").Borrow("Lp", "lh", Uniq, "$p", reserve: true);
        c.N("$v = b@ref").Def("$v").Borrow("Lb", "b", Ref, "$v");
        c.N("storeL(move $p, move $v)").Input(K.Move, "$p").Input(K.Move, "$v").Activate("Lp").Store("$p", "$p", true, 1).Store("$p", "$v", false, 1);
        c.N("let c = State.count@ref").Def("c").Borrow("Lc", Count, Ref, "c");
        c.N("g()").Fx(unknown: true);
        c.N("inspect(c)").Input(K.Read, "c");
        c.SharedOnly.UnionWith(["z", "w", "b"]); c.ByArgument.Add("lh");
        c.UnsuppAt = ["g()"];
        return c.Error();
    }
}

// ---- Round 18 (G124): SPEC 15.2.4's Transferable for a confined witness's read of an immutable static (SPEC 8.11.2: an environment effect
// unless the static's Type is proven Transferable), against the compiler's MirTypes.HoldsRaw (RawStaticRead: a raw, rc or Weak part at any
// depth, a Kimi collection's own raw pointers included; no Function Type, view or abstract rule). K: scalar; raw, rc, weak (of an rc); fn,
// a common Function Type; view, an object view of an open base or a runtime Contract (Name: its declared fields, if any); obj, a handle of a
// Sealed View Target; coll, a Kimi collection or standard iterator (Parts: its elements); abstract (Name "Transferable": a premise); struct
// and enum (Name: Decl's fields or payloads, recursion allowed).
record Ty(string K, string Name, Ty[] Parts)
{
    public static Ty Of(string k, string name = null, params Ty[] parts) => new(k, name, parts);
}

static class R18G
{
    static readonly Ty I32 = Ty.Of("scalar"), RcItem = Ty.Of("rc", null, Ty.Of("struct", "Item"));
    static readonly Dictionary<string, Ty[]> Decl = new()
    {
        ["Base"] = [I32], ["Item"] = [I32],
        ["Hook"] = [Ty.Of("enum", "Option<obj/Hook>"), Ty.Of("fn")], ["Option<obj/Hook>"] = [Ty.Of("obj", "Hook")],
        ["Chain"] = [Ty.Of("enum", "Option<obj/Chain>"), I32], ["Option<obj/Chain>"] = [Ty.Of("obj", "Chain")],
    };
    public static readonly (string Id, string Text, Ty Type, bool SpecErr)[] All = [
        ("g1", "`obj/Base` (a base-object view)", Ty.Of("view", "Base"), true),
        ("g2", "`obj/Shape` (a runtime-Contract view)", Ty.Of("view", "Shape"), true),
        ("g3", "`() -> ()` (a common Function Type)", Ty.Of("fn"), true),
        ("g4", "abstract `T` without a premise", Ty.Of("abstract"), true),
        ("g5", "`Array<i32>` (the collections' exception)", Ty.Of("coll", "Array", I32), false),
        ("g6", "`rc/Item` (SPEC 8.11.2's `Shared.node`)", RcItem, true),
        ("g7", "`i32` (SPEC 8.11.2's `Shared.limit`)", I32, false),
        ("g8", "`Array<rc/Item>` (an element refutes)", Ty.Of("coll", "Array", RcItem), true),
        ("g9", "`T` where `T is Transferable`", Ty.Of("abstract", "Transferable"), false),
        ("g10", "`Hook {next: Option<obj/Hook>, f: () -> ()}` (a cycle hides nothing)", Ty.Of("struct", "Hook"), true),
        ("g11", "`Chain {next: Option<obj/Chain>, v: i32}` (a cycle ends)", Ty.Of("struct", "Chain"), false)];

    // 0 Proven, 1 Unknown, 2 Refuted: the worst over OwnedOrigins' components, concrete object payloads included.
    public static int Transferable(Ty t, HashSet<string> seen = null)
    {
        seen ??= new();
        return t.K switch
        {
            "rc" or "weak" or "raw" or "fn" or "view" => 2,
            "abstract" => t.Name == "Transferable" ? 0 : 1,
            "obj" or "struct" or "enum" => seen.Add(t.Name) ? Decl[t.Name].Max(p => Transferable(p, seen)) : 0,
            _ => t.Parts.Select(p => Transferable(p, seen)).DefaultIfEmpty(0).Max(), // a collection's own raw pointers are exempt
        };
    }

    public static bool HoldsRaw(Ty t, HashSet<string> seen = null)
    {
        seen ??= new();
        return t.K switch
        {
            "rc" or "weak" or "raw" or "coll" => true,
            "fn" or "abstract" => false,
            "view" or "obj" or "struct" or "enum" => Decl.ContainsKey(t.Name) && seen.Add(t.Name) && Decl[t.Name].Any(p => HoldsRaw(p, seen)),
            _ => t.Parts.Any(p => HoldsRaw(p, seen)),
        };
    }
}

// ---- Round 19 (ER-defaults, A5-F9; unit 2x-P9): defaults. A template body ("dt-") is one default's body; its PreparedInputs p_i: ref/P_i reference the
// slots an omitting call prepared (Case.Prepared). It is built twice: in E15's encoding, where a PreparedInput's Deref is the slot itself (the inline
// segment '@': p_x.@ is x's slot, and a Loan on it is rooted at p_x), and in today's (Case.PreparedShared: a shared Deref '*', past which a Borrow forms
// no Loan, SPEC 15.6.3), for R19SharedStep. sh gives a Borrow's SharedAt from the count of the slot's own Derefs up to its last shared one; today's
// step puts one shared Deref before them. A caller ("dc-") holds an omitting call: its explicit arguments in pending slots, or a reservation in its own
// temp (Case.ReservedTemps); the prologue's shared borrows of the slots a default reads (the call's inspection Loans); a copy of a non-recursive default
// (Node.Inlined, its scope) or a call of a recursive one (P9's Content rows as Flows, today's Value rows as Node.UnknownRow, 2e-D's raw-pointer row as
// Node.RawRow); the node that places the default's value into its pending slot, labelled by the omitting call (R4-M70's taint: DefaultTaint for a copy,
// TemplateTaint for a template call); R-a's ending node; the call. Err is SPEC's verdict on the body's own code; At, BorrowAt, AccessAt and UnsuppAt
// are R19's expected outcome.
static class R19
{
    const bool Uniq = true, Ref = false;
    public static IEnumerable<Case> All() => [T(Label), T(Bad), T(Fa), T(Fb), T(Fc), T(Fd), T(Let), T(Pair), T(SharedCall), T(Choose), T(Read), T(CopyRef),
        T(Share), T(Local), T(SemUniq), T(SemCopy), T(SemLocal), T((s, sh) => Capture(s, sh, uniq: false)), T((s, sh) => Capture(s, sh, uniq: true)),
        T(RawCopy), T(RawBorrow), T(Twice), T((s, sh) => Nested(s, sh, bad: false)), T((s, sh) => Nested(s, sh, bad: true)), T(Recursive),
        P15(), Fe(), P14(), P06(live: false), P06(live: true), ReservedRecursive(), ReservedInspect(), ReservedBad(), J1(joined: true), J1(joined: false),
        Joined(both: true), Joined(both: false),
        TwoView(bad: true, uniq: true), TwoView(bad: true, uniq: false), TwoView(bad: false, uniq: true), TwoView(bad: false, uniq: false), P13(), OmittedFa(),
        RawCopyCall(), RawRecursive(), RawHidden(omitted: true), RawHidden(omitted: false)];

    static Case T(Func<string, Func<int, int>, Case> build) { var c = build("@", k => k); c.PreparedShared = build("*", k => k + 1); return c; }

    // A template body: its entry gives each PreparedInput its parameter's Φ_E; raw names the PreparedInputs whose slots hold a raw pointer.
    static Case Tb(string id, string title, string s, string[] prepared, params string[] raw)
    {
        var c = new Case(id, title);
        var n = c.N("entry (" + string.Join(", ", prepared.Select(p => p + ": ref/P")) + ")");
        foreach (var p in prepared) { n.Param("L" + p, p); c.Prepared.Add(p); }
        foreach (var p in raw) c.RawAt.Add($"{p}.{s}");
        return c;
    }
    static Case Ret(Case c) { c.N("return _0").Return(); return c; }

    // SPEC 7.2.3's label: the default borrows x's slot; toString's result retains nothing.
    static Case Label(string s, Func<int, int> sh)
    {
        var c = Tb("dt-label", "SPEC 7.2.3 `label(x: string, y: string = Text.toString(x))`", s, ["p_x"]);
        c.N("$b = &(*p_x) (x@ref of the owned slot)").Def("$b").Borrow("Lb", $"p_x.{s}", Ref, "$b", sharedAt: sh(0));
        c.N("_0 = Text.toString(move $b) (retains nothing)").Def("_0").Input(K.Move, "$b");
        return Ret(c).Valid();
    }

    // SPEC 7.2.3's bad Moves the slot: A1's DefaultArgumentMove_Kd (R4-M69); A5 finds nothing.
    static Case Bad(string s, Func<int, int> sh)
    {
        var c = Tb("dt-bad", "SPEC 7.2.3 `bad(x: string, y: string = x)`", s, ["p_x"]);
        c.N("_0 = move (*p_x)").Def("_0").Input(K.Move, $"p_x.{s}").Flow($"p_x.{s}", "_0");
        c.Backstop = "A1's DefaultArgumentMove_Kd, R4-M69";
        return Ret(c).Error();
    }

    // DefaultAccessTest :25 (1c p05's fa): the reservation of x's referent for increment is exclusive access to the slot.
    static Case Fa(string s, Func<int, int> sh)
    {
        var c = Tb("dt-fa", "DefaultAccessTest :25 `fa(x: uniq/i32, y: i32 = increment(x))`", s, ["p_x"]);
        const string at = "$a = &uniq two-phase (*(*p_x)) (increment's argument)";
        c.N(at).Def("$a").Borrow("La", $"p_x.{s}.*", Uniq, "$a", reserve: true, sharedAt: sh(0));
        c.N("_0 = increment(move $a)").Def("_0").Input(K.Move, "$a").Activate("La");
        c.AccessAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :26 (1c p05's fb, p13): a write through the slot.
    static Case Fb(string s, Func<int, int> sh)
    {
        var c = Tb("dt-fb", "DefaultAccessTest :26 `fb(x: uniq/i32, y: i32 = label work: do x@follow = 9; exit to work 1)`", s, ["p_x"]);
        c.N("(*(*p_x)) = 9").A(K.Write, $"p_x.{s}.*");
        c.N("_0 = 1").Def("_0");
        c.AccessAt = ["(*(*p_x)) = 9"];
        return Ret(c).Error();
    }

    // DefaultAccessTest :27 (1c p05's fc; audit-calls row 26 f): y keeps a shared Reborrow of x.
    static Case Fc(string s, Func<int, int> sh)
    {
        var c = Tb("dt-fc", "DefaultAccessTest :27 `fc(x: uniq/i32 during a, y: ref/i32 during a = x)`", s, ["p_x"]);
        const string at = "_0 = &(*(*p_x)) (a shared Reborrow of x)";
        c.N(at).Def("_0").Borrow("Lr", $"p_x.{s}.*", Ref, "_0", sharedAt: sh(0));
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :28 (1c p05's fd): past x's shared Deref no Loan forms (SPEC 15.6.3), so y derives from x: valid (PD15 (i)); the old path's
    // DefaultArgumentBorrow_Kd is over-strict, a (d) row.
    static Case Fd(string s, Func<int, int> sh)
    {
        var c = Tb("dt-fd", "DefaultAccessTest :28 `fd(x: ref/i32 during a, y: ref/i32 during a = x@follow@ref)` (the old path: Borrow)", s, ["p_x"]);
        c.N("_0 = &(*(*p_x)) (past x's shared Deref)").Def("_0").Borrow("Lr", $"p_x.{s}.*", Ref, "_0", sharedAt: sh(1));
        return Ret(c).Valid();
    }

    // DefaultAccessTest :29: the Reborrow kept through a local.
    static Case Let(string s, Func<int, int> sh)
    {
        var c = Tb("dt-let", "DefaultAccessTest :29 `y = label work: do let shared: ref/i32 during a = x; exit to work shared` (x: uniq/i32)", s, ["p_x"]);
        const string at = "shared = &(*(*p_x))";
        c.N(at).Def("shared").Borrow("Lr", $"p_x.{s}.*", Ref, "shared", sharedAt: sh(0));
        c.N("_0 = copy shared").Def("_0").Input(K.Read, "shared").Flow("shared", "_0");
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :30: the Reborrow kept through an aggregate.
    static Case Pair(string s, Func<int, int> sh)
    {
        var c = Tb("dt-tuple", "DefaultAccessTest :30 `y: (ref/i32 during a, i32) = (x, 1)` (x: uniq/i32)", s, ["p_x"]);
        const string at = "$t = &(*(*p_x))";
        c.N(at).Def("$t").Borrow("Lr", $"p_x.{s}.*", Ref, "$t", sharedAt: sh(0));
        c.N("_0 = (move $t, 1)").Def("_0").Input(K.Move, "$t").Flow("$t", "_0").Operation();
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :31: shared(x: ref/i32 during a) -> ref/i32 during a retains the Reborrow.
    static Case SharedCall(string s, Func<int, int> sh)
    {
        var c = Tb("dt-shared", "DefaultAccessTest :31 `y = shared(x)` (x: uniq/i32; shared's result retains its argument)", s, ["p_x"]);
        const string at = "$t = &(*(*p_x)) (shared's argument)";
        c.N(at).Def("$t").Borrow("Lr", $"p_x.{s}.*", Ref, "$t", sharedAt: sh(0));
        c.N("_0 = shared(move $t)").Def("_0").Input(K.Move, "$t").Flow("$t", "_0");
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :32: choose's result retains x's Reborrow and a copy of z's stored shared reference.
    static Case Choose(string s, Func<int, int> sh)
    {
        var c = Tb("dt-choose", "DefaultAccessTest :32 `y = choose(true, x, z)` (x: uniq/i32, z: ref/i32)", s, ["p_x", "p_z"]);
        const string at = "$t = &(*(*p_x))";
        c.N(at).Def("$t").Borrow("Lr", $"p_x.{s}.*", Ref, "$t", sharedAt: sh(0));
        c.N("$z = copy (*p_z) (the stored shared reference)").Def("$z").Input(K.Read, $"p_z.{s}").Flow($"p_z.{s}", "$z");
        c.N("_0 = choose(true, move $t, move $z)").Def("_0").Input(K.Move, "$t").Input(K.Move, "$z").Flow("$t", "_0").Flow("$z", "_0");
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // DefaultAccessTest :16 ReadReserved: a read through the reserved input.
    static Case Read(string s, Func<int, int> sh)
    {
        var c = Tb("dt-read", "DefaultAccessTest :16 `y: i32 = x` (x: uniq/i32)", s, ["p_x"]);
        c.N("_0 = copy (*(*p_x))").Def("_0").Input(K.Read, $"p_x.{s}.*");
        return Ret(c).Valid();
    }

    // DefaultAccessTest :17 CopyStoredReference and :44: a copy of the stored reference copies its existing dependency (DIAGNOSTICS).
    static Case CopyRef(string s, Func<int, int> sh)
    {
        var c = Tb("dt-copyref", "DefaultAccessTest :17, :44 `y: ref/i32 during a = x` (x: uniq/(ref/i32 during a))", s, ["p_x"]);
        c.N("_0 = copy (*(*p_x)) (the stored reference)").Def("_0").Input(K.Read, $"p_x.{s}.*").Flow($"p_x.{s}.*", "_0");
        return Ret(c).Valid();
    }

    // DefaultAccessTest :18 ShareReserved (also SemanticsDefaultTest's inspection, s = uniq): a shared Reborrow that ends inside the default.
    static Case Share(string s, Func<int, int> sh)
    {
        var c = Tb("dt-share", "DefaultAccessTest :18 `y: i32 = label read: do let r: ref/i32 during a = x; exit to read r@follow` (x: uniq/i32)", s, ["p_x"]);
        c.N("r = &(*(*p_x))").Def("r").Borrow("Lr", $"p_x.{s}.*", Ref, "r", sharedAt: sh(0));
        c.N("_0 = copy (*r)").Def("_0").Input(K.Read, "r.*");
        return Ret(c).Valid();
    }

    // DefaultAccessTest :19's form with an input: a template local's exclusive Loan is no access to a slot.
    static Case Local(string s, Func<int, int> sh)
    {
        var c = Tb("dt-local", "DefaultAccessTest :19's form: `y: i32 = label work: do var local = x; exit to work increment(local@uniq)` (x: i32)", s, ["p_x"]);
        c.N("local = copy (*p_x)").Def("local").Input(K.Read, $"p_x.{s}");
        c.N("$a = &uniq two-phase local").Def("$a").Borrow("La", "local", Uniq, "$a", reserve: true);
        c.N("_0 = increment(move $a)").Def("_0").Input(K.Move, "$a").Activate("La");
        return Ret(c).Valid();
    }

    // SemanticsDefaultTest ExclusiveCase, s = uniq (DefaultAccessTest's grab): an exclusive Reborrow saved in the result, live when formed: one failure,
    // R26-borrow's code alone.
    static Case SemUniq(string s, Func<int, int> sh)
    {
        var c = Tb("dt-sem-u", "SemanticsDefaultTest ExclusiveCase, view s = uniq: `f<s/T>(x: s/T, y: s/T = x)`", s, ["p_x"]);
        const string at = "_0 = &uniq (*(*p_x)) (a Reborrow kept by the result)";
        c.N(at).Def("_0").Borrow("Lr", $"p_x.{s}.*", Uniq, "_0", sharedAt: sh(0));
        c.BorrowAt = [at];
        return Ret(c).Error();
    }

    // ExclusiveCase's views s = owner and s = ref (audit-calls row 26 g's form): `y = x` copies the slot, a Copy value or a stored shared reference.
    static Case SemCopy(string s, Func<int, int> sh)
    {
        var c = Tb("dt-sem-or", "SemanticsDefaultTest ExclusiveCase, views s = owner and s = ref: `y: s/T = x` copies the slot", s, ["p_x"]);
        c.N("_0 = copy (*p_x)").Def("_0").Input(K.Read, $"p_x.{s}").Flow($"p_x.{s}", "_0");
        return Ret(c).Valid();
    }

    // SemanticsDefaultTest DeclarationChecksDefaultLocalLoans: the template's own Loan checks give ComparisonLoanConflict_Kd at the second write.
    static Case SemLocal(string s, Func<int, int> sh)
    {
        var c = Tb("dt-sem-local", "SemanticsDefaultTest: `var local = x@follow; let r = local@ref; local = x@follow; exit to work r@follow`", s, ["p_x"]);
        c.N("local = copy (*(*p_x))").Def("local").Input(K.Read, $"p_x.{s}.*");
        c.N("r = &local").Def("r").Borrow("Ll", "local", Ref, "r");
        const string at = "local = copy (*(*p_x)) (while r lives)";
        c.N(at).Input(K.Read, $"p_x.{s}.*").Assign("local");
        c.N("_0 = copy (*r)").Def("_0").Input(K.Read, "r.*");
        return Ret(c).Error(at);
    }

    // 2e-2's captures, in a closure the default calls at once: `[x@ref]` inspects the slot, `[x@uniq]` borrows it exclusively (DIAGNOSTICS' exclusive
    // entry). A closure kept by the result needs a storable Function Type, whose erased environment Binding requires Owned.
    static Case Capture(string s, Func<int, int> sh, bool uniq)
    {
        var e = uniq ? "x@uniq" : "x@ref";
        var c = Tb(uniq ? "dt-cap-uniq" : "dt-cap-ref", $"`y: i32 = label w: do let c = func [{e}] () => x@follow; exit to w c()` (x: i32)", s, ["p_x"]);
        var at = $"$c = &{(uniq ? "uniq " : "")}(*p_x) (the capture entry [{e}])";
        c.N(at).Def("$c").Borrow("Lc", $"p_x.{s}", uniq, "$c", sharedAt: sh(0));
        c.N("c = Closure(move $c)").Def("c").Input(K.Move, "$c").Flow("$c", "c").Operation();
        c.N("_0 = c()").Def("_0").Input(K.Read, "c");
        if (!uniq) return Ret(c).Valid();
        c.AccessAt = [at];
        return Ret(c).Error();
    }

    // A raw-pointer input a default copies and writes through (G148's open issue 1; SPEC 5.2.1, 7.2.3): p and x are Copied, and the write goes through p
    // by raw authority into storage the body does not own, not into p's slot.
    static Case RawCopy(string s, Func<int, int> sh)
    {
        var c = Tb("dt-raw-copy", "`put(p: raw/(ref/i32 during a), x: ref/i32 during a, y: i32 = unsafe { p[0] = x; 0 })`", s, ["p_p", "p_x"], "p_p");
        c.N("$v = copy (*p_x)").Def("$v").Input(K.Read, $"p_x.{s}").Flow($"p_x.{s}", "$v");
        c.N("(*(*p_p))[0] = move $v (a raw write)").Input(K.Move, "$v").Through($"p_p.{s}.*.0", "$v");
        c.N("_0 = 0").Def("_0");
        return Ret(c).Valid();
    }

    // A slot borrow stored into raw storage (PD15 (iv)): R26-borrow finds it in σ(E); A5-F4's Origin stratum also refutes the fit (PD11 keeps this code).
    static Case RawBorrow(string s, Func<int, int> sh)
    {
        var c = Tb("dt-raw-borrow", "`keep(x: i32, p: raw/(ref/i32 during a), y: i32 = unsafe { p[0] = x@ref; 0 })`", s, ["p_x", "p_p"], "p_p");
        const string at = "$b = &(*p_x)";
        c.N(at).Def("$b").Borrow("Lb", $"p_x.{s}", Ref, "$b", sharedAt: sh(0));
        c.N("(*(*p_p))[0] = move $b (a raw write)").Input(K.Move, "$b").Through($"p_p.{s}.*.0", "$b");
        c.N("_0 = 0").Def("_0");
        c.BorrowAt = [at]; c.Backstop = "A5-F4's Origin relation, not modelled";
        return Ret(c).Error();
    }

    // DIAGNOSTICS: a separate mutation keeps its Access beside the Borrow: the write through x is an Access, and the borrow of z's slot it stores into
    // storage the body does not own is a Borrow.
    static Case Twice(string s, Func<int, int> sh)
    {
        var c = Tb("dt-twice", "`f(z: i32, x: uniq/(ref/i32 during a), y: i32 = label w: do x@follow = z@ref; exit to w 0)`", s, ["p_z", "p_x"]);
        const string b = "$b = &(*p_z)", w = "(*(*p_x)) = move $b";
        c.N(b).Def("$b").Borrow("Lb", $"p_z.{s}", Ref, "$b", sharedAt: sh(0));
        c.N(w).Input(K.Move, "$b").Through($"p_x.{s}.*", "$b");
        c.N("_0 = 0").Def("_0");
        c.BorrowAt = [b]; c.AccessAt = [w];
        return Ret(c).Error();
    }

    // A copy inside a template: ReceiverDefaultTest NestedOrigins (valid), and outer keeping x's Reborrow through inner's result. The inner copy's
    // prologue borrows inner's own slot, and R-a ends that Loan at inner's call.
    static Case Nested(string s, Func<int, int> sh, bool bad)
    {
        var c = Tb(bad ? "dt-nested-bad" : "dt-nested", bad ? "`outer(x: uniq/i32 during q, y: ref/i32 during q = inner(x))`, `inner(a: ref/i32 during q, b: ref/i32 during q = a)`"
            : "ReceiverDefaultTest NestedOrigins: `f(a: ref/Meter, value: i32 = leaf(a))`, `leaf(m: ref/Meter, value: i32 = m.read())`", s, [bad ? "p_x" : "p_a"]);
        const string slot = "$a = &(*(*p_x)) (inner's slot a: a shared Reborrow of x)";
        if (bad) c.N(slot).Def("$a").Borrow("Lr", $"p_x.{s}.*", Ref, "$a", sharedAt: sh(0));
        else c.N("$a = copy (*p_a) (leaf's slot m)").Def("$a").Input(K.Read, $"p_a.{s}").Flow($"p_a.{s}", "$a");
        Prologue(c, "$a");
        if (bad) Into(c, "inner", "b", "copy (*$5)", "$b").Input(K.Read, "$5.*").Flow("$5.*", "$b");
        else
        {
            c.N("$r = &(*(*$5)) (read's receiver, in leaf's copy)").Def("$r").Borrow("Lt", "$5.*.*", Ref, "$r", sharedAt: 2).Inlined();
            Into(c, "leaf", "value", "read(move $r)", "$b").Input(K.Move, "$r");
        }
        Ra(c, bad ? "inner" : "leaf");
        var call = c.N(bad ? "_0 = inner(move $a, move $b) (retains a and b)" : "_0 = leaf(move $a, move $b)").Def("_0").Input(K.Move, "$a").Input(K.Move, "$b");
        if (!bad) return Ret(c).Valid();
        call.Flow("$a", "_0").Flow("$b", "_0");
        c.BorrowAt = [slot];
        return Ret(c).Error();
    }

    // 1c p06's template body (T is Copy): a template call inside the template; its inputs borrow the template's own slots.
    static Case Recursive(string s, Func<int, int> sh)
    {
        var c = Tb("dt-recur", "1c p06's template: `value: T = if n == 0 => x else => f(n - 1, x)` (T is Copy)", s, ["p_n", "p_x"]);
        const string slots = "$sn = (*p_n) - 1; $sx = copy (*p_x) (f's slots)";
        c.N("if n == 0").Input(K.Read, $"p_n.{s}").Goto("_0 = copy (*p_x)", slots);
        c.N("_0 = copy (*p_x)").Def("_0").Input(K.Read, $"p_x.{s}").Flow($"p_x.{s}", "_0").Goto("return _0");
        c.N(slots).Def("$sn", "$sx").Input(K.Read, $"p_n.{s}").Input(K.Read, $"p_x.{s}").Flow($"p_x.{s}", "$sx");
        Call(c, "f", "value", ["$sn", "$sx"], (n, d) => n.Flow("$sx", "_0").Flow(d, "_0"), "_0");
        return Ret(c).Valid();
    }

    // A copy of a non-recursive default at an omitting call: the prologue's shared borrow of the slot it reads (the call's inspection Loan Ls), the
    // copy's store into its pending slot, labelled by the omitting call (R4-M70's taint), inside its Inlined scope, and R-a's ending.
    static void Prologue(Case c, string slot) => c.N($"$5 = &{slot} (prologue)").Def("$5").Borrow("Ls", slot, Ref, "$5");
    static Node Into(Case c, string f, string param, string what, string dest = "$y") => c.N($"{f} omits {param}: its copy stores {dest} = {what}").Def(dest).DefaultTaint(dest).Inlined();
    static void Ra(Case c, string f, params string[] loans) => c.N($"R-a: {f}'s inspection Loans end").EndInspections(loans.Length == 0 ? ["Ls"] : loans);

    // A recursive default's template call at an omitting call: its inputs (shared borrows of every slot, the call's inspection Loans), the call with P9's
    // Content rows, today's Value rows, R4-M70's taint on its result and, where an input's slot holds a raw pointer, 2e-D's raw-pointer row; R-a's
    // ending; the call, with its own rows.
    static Node Call(Case c, string f, string param, string[] slots, Action<Node, string> rows, string into, bool raw = false)
    {
        var ins = slots.Select((x, k) => (Slot: x, Input: $"$i{k + 1}", Loan: $"Ls{k + 1}")).ToList();
        var n = c.N(string.Join("; ", ins.Select(x => $"{x.Input} = &{x.Slot}")) + " (the template call's inputs)");
        foreach (var x in ins) n.Def(x.Input).Borrow(x.Loan, x.Slot, Ref, x.Input);
        const string d = "$d";
        var t = c.N($"{f} omits {param}: {d} = {f}#{param}({string.Join(", ", ins.Select(x => "move " + x.Input))})").Def(d).TemplateTaint(d);
        foreach (var x in ins) t.Input(K.Move, x.Input).Flow(x.Input + ".*", d).UnknownRow(x.Input, d);
        if (raw) t.RawRow(ins.Select(x => x.Input + ".*").ToArray());
        Ra(c, f, ins.Select(x => x.Loan).ToArray());
        var call = c.N($"{(into == null ? "" : into + " = ")}{f}({string.Join(", ", slots.Select(x => "move " + x))}, move {d})");
        if (into != null) call.Def(into);
        foreach (var x in slots) call.Input(K.Move, x);
        call.Input(K.Move, d);
        rows(call, d);
        return call;
    }

    // 1c p15: dt-fc's copy keeps the prologue's Loan on x's slot in its value, so the call's Move of the slot meets it there. DIAGNOSTICS: the call
    // adds no record (R-a); today the taint makes it Unsupported_Kd at the omitting call (1c-7).
    static Case P15()
    {
        var c = new Case("dc-p15", "1c p15: `var n = 3; fc(n@uniq)` (dt-fc's copy keeps x's Reborrow)");
        c.N("var n = 3").Def("#n");
        c.N("$x = &uniq two-phase n (fc's slot x)").Def("$x").Borrow("Ln", "#n", Uniq, "$x", reserve: true);
        Prologue(c, "$x");
        Into(c, "fc", "y", "&(*(*$5))").Borrow("Ly", "$5.*.*", Ref, "$y", sharedAt: 1);
        Ra(c, "fc");
        c.N("fc(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y").Activate("Ln");
        c.DupOf = "dt-fc";
        return c.Valid();
    }

    // 1c p15's valid twin: fe(x: uniq/i32, y: i32 = x@follow) reads through the reserved slot.
    static Case Fe()
    {
        var c = new Case("dc-fe", "1c p15: `var m = 4; fe(m@uniq)` (fe(x: uniq/i32, y: i32 = x@follow))");
        c.N("var m = 4").Def("#m");
        c.N("$x = &uniq two-phase m (fe's slot x)").Def("$x").Borrow("Lm", "#m", Uniq, "$x", reserve: true);
        Prologue(c, "$x");
        Into(c, "fe", "y", "copy (*(*$5))").Input(K.Read, "$5.*.*");
        Ra(c, "fe");
        c.N("fe(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y").Activate("Lm");
        return c.Valid();
    }

    // 1c p14's inspectOk: a valid default beside the caller's own independent error (a Move of s while r lives).
    static Case P14()
    {
        var c = new Case("dc-p14", "1c p14: `var s = \"c\"; let r = s@uniq; inspectOk(s); r@follow = \"d\"` (inspectOk(x: string, y: i32 = x.length))");
        c.N("var s = \"c\"").Def("#s");
        c.N("let r = s@uniq").Def("r").Borrow("Lr", "#s", Uniq, "r");
        const string at = "$x = move s (inspectOk's slot x)";
        c.N(at).Def("$x").A(K.Move, "#s").Flow("#s", "$x");
        Prologue(c, "$x");
        Into(c, "inspectOk", "y", "(*$5).length").Input(K.Read, "$5.*");
        Ra(c, "inspectOk");
        c.N("inspectOk(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y");
        c.N("r@follow = \"d\"").A(K.Write, "r.*");
        return c.Error(at);
    }

    // 1c p06 (RecursiveDefaultTest Reference) and RecursiveReferenceResultKeepsTheCallersLoan: with r dead the program is valid; with `_ = r@follow`,
    // `k = 4` errs, the value carrying k's Loan (P9). Today's Value rows give the value the inspection Loans, which the call's Moves of the slots
    // meet: Unsupported_Kd by the taint (1c-7), as is k's conflict in r.
    static Case P06(bool live)
    {
        var c = new Case(live ? "dc-ref-live" : "dc-p06", live ? "RecursiveReferenceResultKeepsTheCallersLoan: `var k = 3; let r = f(3, k@ref); k = 4; _ = r@follow`"
            : "1c p06: `var k = 3; let r = f(3, k@ref); k = 4` (f<T>(n: i32, x: T, value: T = if n == 0 => x else => f(n - 1, x)))");
        c.N("var k = 3").Def("#k");
        c.N("$n = 3; $x = &k (f's slots)").Def("$n", "$x").Borrow("Lk", "#k", Ref, "$x");
        Call(c, "f", "value", ["$n", "$x"], (n, d) => n.Flow("$x", "r").Flow(d, "r"), "r");
        c.N("k = 4").Assign("#k");
        if (!live) return c.Valid();
        c.N("_ = r@follow").Input(K.Read, "r.*");
        return c.Error("k = 4");
    }

    // A recursive default with a reserved input and a Loan-capable defaulted Type (P9 with R4-M68): `rr(x: uniq/i32 during a, z: ref/i32 during a,
    // n: i32, y: ref/i32 during a = if n == 0 => z else => label w: do var local = 0; rr(local@uniq, z, n - 1); exit to w z)`. The value carries x's
    // reservation itself, which its activation does not meet: an activation meets ref-mode Loans only (R4-M68; H: SPEC checks no activation against its
    // own Loan).
    static Case ReservedRecursive()
    {
        var c = new Case("dc-reserved-rec", "`var v = 1; var w = 2; rr(v@uniq, w@ref, 3)` (rr(x: uniq/i32 during a, z: ref/i32 during a, n: i32, y: ref/i32 during a = if n == 0 => z else => …))");
        c.N("var v = 1; var w = 2").Def("#v", "#w");
        c.N("$x = &uniq two-phase v; $z = &w; $n = 3 (rr's slots)").Def("$x", "$z", "$n").Borrow("Lv", "#v", Uniq, "$x", reserve: true).Borrow("Lw", "#w", Ref, "$z");
        Call(c, "rr", "y", ["$x", "$z", "$n"], (n, d) => n.Activate("Lv"), null);
        return c.Valid();
    }

    // An inline default inspecting a reserved explicit argument (ReceiverDefaultTest ReservedMethod; SPEC 7.2.3, 15.6.7): the reservation is formed in
    // its own temp, which the prologue borrows shared.
    static Case ReservedInspect()
    {
        var c = new Case("dc-reserved-inspect", "ReceiverDefaultTest ReservedMethod: `var m = Meter.init(); f(m@uniq)` (f(m: uniq/Meter, value: i32 = m.read()))");
        c.N("var m = Meter.init()").Def("#m");
        c.N("$r = &uniq two-phase m (f's argument, reserved in its own temp)").Def("$r").Borrow("Lm", "#m", Uniq, "$r", reserve: true);
        c.ReservedTemps.Add("$r");
        Prologue(c, "$r");
        c.N("$t = &(*(*$5)) (read's receiver, in the copy)").Def("$t").Borrow("Lt", "$5.*.*", Ref, "$t", sharedAt: 1).Inlined();
        Into(c, "f", "value", "read(move $t)", "$v").Input(K.Move, "$t");
        Ra(c, "f");
        c.N("f(move $r, move $v)").Input(K.Move, "$r").Input(K.Move, "$v").Activate("Lm");
        return c.Valid();
    }

    // dc-p15 with the argument reserved in its own temp (an implicit exclusive acquisition, formed where it is reserved): R-a ends the inspection Loan on
    // the reserved argument too.
    static Case ReservedBad()
    {
        var c = new Case("dc-reserved-bad", "`g(counter)` (g(c: uniq/Counter during a, d: ref/Counter during a = c): dt-fc's form, the reservation in its own temp)");
        c.N("var counter = Counter()").Def("#k");
        c.N("$r = &uniq two-phase counter (g's argument, reserved in its own temp)").Def("$r").Borrow("Lk", "#k", Uniq, "$r", reserve: true);
        c.ReservedTemps.Add("$r");
        Prologue(c, "$r");
        Into(c, "g", "d", "&(*(*$5))", "$d").Borrow("Ld", "$5.*.*", Ref, "$d", sharedAt: 1);
        Ra(c, "g");
        c.N("g(move $r, move $d)").Input(K.Move, "$r").Input(K.Move, "$d").Activate("Lk");
        c.DupOf = "dt-fc";
        return c.Valid();
    }

    // The j1-shaped holder (group 1c's review r06; PD15 (ii)): pick(a: ref/i32 during p, b: ref/i32 during p = a) -> ref/i32 during p => b. h joins x's
    // Loan (else) and the copy's value (then), so on the else path `x = 2` is an independent error. The taint's every-set rule makes it Unsupported_Kd
    // and the audit's drop accepts it. The clean twin keeps the copy's value in g.
    static Case J1(bool joined)
    {
        var dst = joined ? "h" : "g";
        var c = new Case(joined ? "dc-j1" : "dc-j1-clean", $"j1-shaped: `let r = x@ref; var h = r; if c => {dst} = pick(o@ref); x = 2; inspect(h)`" + (joined ? "" : "; inspect(g)"));
        c.N("var x = 1; var o = 2").Def("#x", "#o");
        c.N("let r = x@ref").Def("r").Borrow("Lx", "#x", Ref, "r");
        c.N("var h = copy r").Def("h").Input(K.Read, "r").Flow("r", "h");
        c.N("if c").Goto("$a = &o (pick's slot a)", "x = 2");
        c.N("$a = &o (pick's slot a)").Def("$a").Borrow("Lo", "#o", Ref, "$a");
        Prologue(c, "$a");
        Into(c, "pick", "b", "copy (*$5)", "$b").Input(K.Read, "$5.*").Flow("$5.*", "$b");
        Ra(c, "pick");
        c.N($"{dst} = pick(move $a, move $b) (retains a and b)").Input(K.Move, "$a").Input(K.Move, "$b").Def(dst).Flow("$a", dst).Flow("$b", dst);
        c.N("x = 2").Assign("#x");
        c.N("inspect(h)").Input(K.Read, "h");
        if (!joined) c.N("inspect(g)").Input(K.Read, "g");
        return c.Error("x = 2");
    }

    // MirBorrowsTest ADefaultsTaintMakesItsConflictsUnsupported's `joined` (R4-M70's T2 join): same(x: ref/i32 during a, y: ref/i32 during a = x) ->
    // ref/i32 during a => y; r joins x's value and the copy's in one holder, so k's conflict is found only through a tainted set. `copied`'s form with
    // the result retaining y alone (onlyY(x: ref/i32 during q, y: ref/i32 during p = x) -> ref/i32 during p, q outlives p; H: retention per Origin):
    // the conflict lies only on a valid default's copy of an existing shared borrow, which the audit's drop accepts with no join at all.
    static Case Joined(bool both)
    {
        var f = both ? "same" : "onlyY";
        var c = new Case(both ? "dc-joined" : "dc-copied", $"MirBorrowsTest `{(both ? "joined" : "copied")}`'s form: `var k = 1; let r = {f}(k@ref); k = 2; inspect(r)`"
            + (both ? " (r joins x and the copy of x)" : " (r retains y alone, the copy of x)"));
        c.N("var k = 1").Def("#k");
        c.N($"$x = &k ({f}'s slot x)").Def("$x").Borrow("Lk", "#k", Ref, "$x");
        Prologue(c, "$x");
        Into(c, f, "y", "copy (*$5)").Input(K.Read, "$5.*").Flow("$5.*", "$y");
        Ra(c, f);
        var call = c.N($"r = {f}(move $x, move $y)").Def("r").Input(K.Move, "$x").Input(K.Move, "$y").Flow("$y", "r");
        if (both) call.Flow("$x", "r");
        c.N("k = 2").Assign("#k");
        c.N("inspect(r)").Input(K.Read, "r");
        return c.Error("k = 2");
    }

    // A two-view generic caller (PD15 (ii), 2e-3): `func g<s/T>(v: s/T) => f(v)` with s ref or uniq, each view its own body (R4-M71). Invalid default:
    // SemanticsDefaultTest's f (dt-sem-u: the template's uniq view reports); valid: probe<s/T>(a: s/T, n: i32 = size(a)).
    static Case TwoView(bool bad, bool uniq)
    {
        var f = bad ? "f" : "probe"; var p = bad ? "y" : "n";
        var c = new Case($"dc-gen2-{(bad ? "bad" : "ok")}-{(uniq ? "u" : "r")}", $"`g<s/T>(v: s/T) => {f}(v)`, view s = {(uniq ? "uniq" : "ref")} ("
            + (bad ? "f<s/T>(x: s/T, y: s/T = x))" : "probe<s/T>(a: s/T, n: i32 = size(a)))"));
        c.N($"entry (v: {(uniq ? "uniq" : "ref")}/T)").Param("Lpv", "v");
        if (uniq) c.N($"$x = &uniq two-phase (*v) ({f}'s slot)").Def("$x").Borrow("Lx", "v.*", Uniq, "$x", reserve: true);
        else c.N($"$x = copy v ({f}'s slot)").Def("$x").Input(K.Read, "v").Flow("v", "$x");
        Prologue(c, "$x");
        if (bad && uniq) Into(c, f, p, "&uniq (*(*$5))").Borrow("Ly", "$5.*.*", Uniq, "$y", sharedAt: 1);
        else if (bad) Into(c, f, p, "copy (*$5)").Input(K.Read, "$5.*").Flow("$5.*", "$y");
        else
        {
            c.N("$t = &(*(*$5)) (size's argument, in the copy)").Def("$t").Borrow("Lt", "$5.*.*", Ref, "$t", sharedAt: uniq ? 1 : 2).Inlined();
            Into(c, f, p, "size(move $t)").Input(K.Move, "$t");
        }
        Ra(c, f);
        var call = c.N($"{f}(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y");
        if (uniq) call.Activate("Lx");
        if (bad && uniq) c.DupOf = "dt-sem-u";
        return c.Valid();
    }

    // 1c p13: dt-fb's copy writes past the prologue's shared borrow, so it meets no caller Loan (R4-M69's premise); the template reports.
    static Case P13()
    {
        var c = new Case("dc-p13", "1c p13: `var n = 3; fb(n@uniq)` (dt-fb's copy writes x@follow)");
        c.N("var n = 3").Def("#n");
        c.N("$x = &uniq two-phase n (fb's slot x)").Def("$x").Borrow("Ln", "#n", Uniq, "$x", reserve: true);
        Prologue(c, "$x");
        c.N("(*(*$5)) = 9 (in fb's copy)").A(K.Write, "$5.*.*").Inlined();
        Into(c, "fb", "y", "1");
        Ra(c, "fb");
        c.N("fb(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y").Activate("Ln");
        c.DupOf = "dt-fb";
        return c.Valid();
    }

    // DefaultAccessTest InvalidDefaultIsCheckedOnceWhetherOmittedOrSupplied: dt-fa's copy's reservation lies past the prologue's shared borrow and forms
    // no Loan; the template's Access is the one record.
    static Case OmittedFa()
    {
        var c = new Case("dc-fa", "DefaultAccessTest: `var n = 1; fa(n@uniq)` omitting dt-fa's default (checked once)");
        c.N("var n = 1").Def("#n");
        c.N("$x = &uniq two-phase n (fa's slot x)").Def("$x").Borrow("Ln", "#n", Uniq, "$x", reserve: true);
        Prologue(c, "$x");
        c.N("$a = &uniq two-phase (*(*$5)) (increment's argument, in fa's copy)").Def("$a").Borrow("La", "$5.*.*", Uniq, "$a", reserve: true, sharedAt: 1).Inlined();
        Into(c, "fa", "y", "increment(move $a)").Input(K.Move, "$a").Activate("La");
        Ra(c, "fa");
        c.N("fa(move $x, move $y)").Input(K.Move, "$x").Input(K.Move, "$y").Activate("Ln");
        c.DupOf = "dt-fa";
        return c.Valid();
    }

    // A raw-pointer input at an omitting call (G148's open issue 1; SPEC 5.2.1): dt-raw-copy's copy writes x's value through the copied pointer into E,
    // and so does put1's own row (rule G's raw clause: a raw local holding no Loan), so `n = 5` errs either way.
    static Case RawCopyCall()
    {
        var c = new Case("dc-raw-copy", "`var rp = Raw.allocate(1); put1(rp, n@ref); n = 5; inspect(rp[0])` (put1: dt-raw-copy's default, copied)");
        c.N("var n = 1; var rp: raw/(ref/i32) = Raw.allocate(1)").Def("#n", "%rp");
        c.N("$p = copy rp; $x = &n (put1's slots)").Def("%p", "$x").Borrow("Ln", "#n", Ref, "$x");
        c.N("$5 = &$p; $6 = &$x (prologue)").Def("$5", "$6").Borrow("Ls", "%p", Ref, "$5").Borrow("Ls6", "$x", Ref, "$6");
        c.N("$v = copy (*$6) (in put1's copy)").Def("$v").Input(K.Read, "$6.*").Flow("$6.*", "$v").Inlined();
        c.N("(*(*$5))[0] = move $v (a raw write, in put1's copy)").Input(K.Move, "$v").Through("$5.*.*.0", "$v").Inlined();
        Into(c, "put1", "y", "0");
        Ra(c, "put1", "Ls", "Ls6");
        c.N("put1(move $p, move $x, move $y) (its row x → p's pointee: E)").Input(K.Move, "%p").Input(K.Move, "$x").Input(K.Move, "$y").Store("%p", "$x", false, 1, raw: true);
        c.N("n = 5").Assign("#n");
        c.N("inspect(rp[0])").Input(K.Read, "%rp.*.0");
        return c.Error("n = 5");
    }

    // The same through a template call (putr's y is recursive): the template call's rows store nothing into E without 2e-D's raw row, but putr's own row
    // reaches E (rule G's raw clause), so `v = 5` errs in every variant.
    static Case RawRecursive()
    {
        var c = new Case("dc-raw-rec", "`putr(rp, v@ref, 3); v = 5; inspect(rp[0])` (putr(p: raw/(ref/i32 during a), x: ref/i32 during a, n: i32, y: i32 = if n == 0 => unsafe { p[0] = x; 0 } else => putr(p, x, n - 1)))");
        c.N("var v = 1; var rp: raw/(ref/i32) = Raw.allocate(1)").Def("#v", "%rp");
        c.N("$p = copy rp; $x = &v; $n = 3 (putr's slots)").Def("%p", "$x", "$n").Borrow("Lv", "#v", Ref, "$x");
        Call(c, "putr", "y", ["%p", "$x", "$n"], (n, d) => n.Store("%p", "$x", false, 1, raw: true), null, raw: true);
        c.N("v = 5").Assign("#v");
        c.N("inspect(rp[0])").Input(K.Read, "%rp.*.0");
        return c.Error("v = 5");
    }

    // G160 (a)'s shape at an omitting call: RawBox {a} holds p: raw/(ref/i32 during a) and r: ref/i32 during a; putb(b: ref/RawBox, x: ref/i32 during a,
    // n: i32, y: i32 = if n == 0 => unsafe { b.p[0] = x; 0 } else => putb(b, x, n - 1)). putb's own row stores x into box, the holder W names, not into
    // the raw pointee (G160 (a)), and box is dead after: only 2e-D's raw row puts x into E. With y supplied, putb's contract alone may store x there,
    // which G160 leaves unchecked whatever F9 does.
    static Case RawHidden(bool omitted)
    {
        var c = new Case(omitted ? "dc-raw-hid" : "dc-raw-hid-x", (omitted ? "`putb(box@ref, v@ref, 3)`" : "`putb(box@ref, v@ref, 3, 0)` (y supplied)")
            + " after `var box = RawBox(p: Raw.allocate(1), r: z@ref); let q = box.p`; then `v = 5; inspect(q[0])`");
        c.N("var z = 0; var v = 1").Def("#z", "#v");
        c.N("var box = RawBox(p: Raw.allocate(1), r: z@ref)").Def("box").Borrow("Lz", "#z", Ref, "box");
        c.N("let q = box.p (a copy of the raw pointer)").Def("%q");
        c.N("$b = &box; $x = &v; $n = 3 (putb's slots)").Def("$b", "$x", "$n").Borrow("Lbox", "box", Ref, "$b").Borrow("Lv", "#v", Ref, "$x");
        Action<Node, string> row = (n, d) => n.Store("$b", "$x", false, 1, raw: true);
        if (omitted) Call(c, "putb", "y", ["$b", "$x", "$n"], row, null, raw: true);
        else row(c.N("$y = 0; putb(move $b, move $x, move $n, move $y)").Def("$y").Input(K.Move, "$b").Input(K.Move, "$x").Input(K.Move, "$n").Input(K.Move, "$y"), "$y");
        c.N("v = 5").Assign("#v");
        c.N("inspect(q[0])").Input(K.Read, "%q.*.0");
        if (!omitted) { c.Known = "PLAN G160 (a)"; return c.Error(); }
        return c.Error("v = 5");
    }
}

// ---- Round 20 (template ownership of failures in inline default copies, PLAN G171; unit 2x-P11). A default's body is one Tpl, built once as its template
// case ("dt20-": the root scope, its value into _0, the slots it reads as PreparedInputs) and once per omitting call as a copy in a caller ("dc20-"): a new
// Inlined scope (Case.Scopes) whose labels, locals and Loan ids carry the copy's prefix ("[fn/param]", nested copies' prefixes concatenated, a tag for
// copies of one template), its value into the pending slot, with the prologue's shared borrows of the slots it reads and R-a's ending around it. So a
// copy's nodes and Loans are its template's under the prefix, which the simulation check strips. Statics: State.counter, other and holder, all mutable; a
// group's unqualified borrow of its own storage is what Binding admits (PLAN G171's witness); (H) marks a form whose Binding acceptance is unverified.
// Err is SPEC's verdict on the body's own code. At, UnsuppAt and OmitAt are its SPEC outcome with one record per failure: the body's own failures (a held
// view's as located U), and the failures its copies' templates own, which it omits.
static class R20
{
    const bool Uniq = true, Ref = false;
    const string Counter = "#State:counter", Other = "#State:other", Holder = "#State:holder";
    static readonly Dictionary<string, Tpl> templates = new();
    static readonly Dictionary<string, Case> built = new();

    sealed record Tpl(string Id, string Fn, string Param, string Title, Action<Ctx> Body, Func<Case, Case> Expect, string[] Slots, string[] Prepared);

    // One evaluation of a default (or the caller's own code at the root): its scope, its prefix, where its value goes, how it reads a slot, and (the model
    // form of dt20-act) a reservation the caller formed that a copy's call activates.
    sealed class Ctx(Case c, int scope, string prefix, string dest, Func<string, string> slot, (string H, string L)? res = null)
    {
        public readonly Case C = c; public readonly string Dest = dest; public readonly (string H, string L)? Res = res;
        public string Lab(string label) => prefix.Length == 0 ? label : prefix + " " + label;
        public Node N(string label) { var n = C.N(Lab(label)); n.Scope = scope; return scope > 0 ? n.Inlined() : n; }
        public string L(string local) => prefix.Length == 0 ? local : local[0] is '#' or '%' ? local[0] + prefix + local[1..] : prefix + local;
        public string Id(string loan) => prefix + loan;
        public string Slot(string p) => slot(p);
        // The node that places the evaluation's value (a template's _0; a copy's pending slot, with R4-M70's taint, unused on R19).
        public Node Place(Node n) { n.Def(Dest); return scope > 0 ? n.DefaultTaint(Dest) : n; }
        // An omitting call here: the prologue's shared borrows of the slots the default reads (its inspection Loans), the copy in a new Inlined scope,
        // and R-a's ending of those Loans.
        public void Copy(Tpl t, string into, string tag = "", (string H, string L)? reservation = null, params string[] slotLocals)
        {
            var pro = (t.Slots ?? []).Select((q, k) => (Q: q, Local: L("$5" + q), Loan: Id("Ls" + q), Slot: slotLocals[k])).ToList();
            foreach (var x in pro) N($"{x.Local} = &{x.Slot} (prologue)").Def(x.Local).Borrow(x.Loan, x.Slot, Ref, x.Local);
            var s = C.Scopes.Count; var p = prefix + "[" + t.Fn + "/" + t.Param + tag + "]";
            C.Scopes.Add((scope, p, t.Id));
            t.Body(new Ctx(C, s, p, into, q => pro.Single(x => x.Q == q).Local + ".*", reservation));
            if (pro.Count > 0) N($"R-a: {t.Fn}'s inspection Loans end").EndInspections(pro.Select(x => x.Loan).ToArray());
        }
        // The same evaluation as an explicit argument: the body in this scope, no copy.
        public void Here(Tpl t, string into) => t.Body(new Ctx(C, scope, prefix, into, slot));
    }

    static Tpl T(string id, string fn, string param, string title, Action<Ctx> body, Func<Case, Case> expect, string[] slots = null, string[] prepared = null)
    { var t = new Tpl(id, fn, param, title, body, expect, slots, prepared); templates[id] = t; return t; }
    public static Case Template(string id) => built.TryGetValue(id, out var c) ? c : built[id] = Build(templates[id]);
    static Case Build(Tpl t)
    {
        var c = S("dt20-" + t.Id, $"`{t.Title}`");
        var prepared = (t.Slots ?? []).Concat(t.Prepared ?? []).ToList();
        if (prepared.Count > 0)
        {
            var n = c.N("entry (" + string.Join(", ", prepared.Select(p => $"p_{p}: ref/P")) + ")");
            foreach (var p in prepared) { n.Param("Lp_" + p, "p_" + p); c.Prepared.Add("p_" + p); }
        }
        t.Body(new Ctx(c, 0, "", "_0", q => $"p_{q}.@"));
        c.N("return _0").Return();
        return t.Expect(c);
    }
    static Case S(string id, string title) { var c = new Case(id, title); c.Statics[Counter] = true; c.Statics[Other] = true; c.Statics[Holder] = true; c.Scopes.Add((-1, "", null)); return c; }
    static Ctx Root(Case c) => new(c, 0, "", null, _ => throw new InvalidOperationException("a caller reads no slot"));
    static Case Valid(Case c) => c.Valid();
    static Case Omit(Case c, params string[] at) { c.OmitAt = at; return c; }

    public static IEnumerable<Case> All() => templates.Keys.Select(Template).Concat(Callers());

    // ---- Template bodies.
    static string Nm(string s) => s == Counter ? "counter" : "other";
    // poke(s@uniq): s reserved for poke's argument, and that reservation's activation at poke's call (labelled `call`).
    static Node Poke(Ctx k, string call, string s = Counter)
    {
        k.N($"$p = &uniq two-phase {Nm(s)} (poke's argument)").Def(k.L("$p")).Borrow(k.Id("Lp"), s, Uniq, k.L("$p"), reserve: true);
        return k.N(call).Input(K.Move, k.L("$p")).Activate(k.Id("Lp"));
    }
    static void LetR(Ctx k) => k.N("r = &counter").Def(k.L("r")).Borrow(k.Id("Lr"), Counter, Ref, k.L("r"));
    static Node UseR(Ctx k, string label) => k.Place(k.N(label).Input(K.Read, k.L("$v")).Input(K.Read, k.L("r") + ".*"));
    static void Retain(Ctx k, string into, string fn, string arg) => k.N($"{into} = {fn}(move {arg}) (retains its argument)").Def(k.L(into)).Input(K.Move, k.L(arg)).Flow(k.L(arg), k.L(into));

    // PLAN G171: add's default borrows the group's counter exclusively for poke; the template alone meets nothing. Twins: another static; a shared borrow.
    static readonly Tpl Add = T("add", "add", "x", "add(c: uniq/Counter, x: i32 = State.poke(counter@uniq))", k => k.Place(Poke(k, "x := poke(move $p)")), Valid);
    static readonly Tpl AddOther = T("add-other", "add", "x", "add(c: uniq/Counter, x: i32 = State.poke(other@uniq))", k => k.Place(Poke(k, "x := poke(move $p)", Other)), Valid);
    static readonly Tpl AddRead = T("add-read", "add", "x", "add(c: uniq/Counter, x: i32 = State.peek(counter@ref))", k =>
    {
        k.N("$q = &counter (peek's argument)").Def(k.L("$q")).Borrow(k.Id("Lq"), Counter, Ref, k.L("$q"));
        k.Place(k.N("x := peek(move $q)").Input(K.Move, k.L("$q")));
    }, Valid);
    // The M69 plan §6's source: the default's call carries A4's key on counter (R18's err(effect)). Twins: a key on other; a read key.
    static readonly Tpl Touch = T("touch", "add", "x", "Counter.add(self: uniq/Self, x: i32 = State.touch())` (touch writes counter.count",
        k => k.Place(k.N("x := touch() (A4's key: counter, write)").Key(Counter, write: true)), Valid);
    static readonly Tpl TouchOther = T("touch-other", "add", "x", "Counter.add(self: uniq/Self, x: i32 = State.touchOther())` (touchOther writes other.count",
        k => k.Place(k.N("x := touchOther() (A4's key: other, write)").Key(Other, write: true)), Valid);
    static readonly Tpl TouchRead = T("touch-read", "add", "x", "Counter.add(self: uniq/Self, x: i32 = State.peekCount())` (peekCount reads counter.count",
        k => k.Place(k.N("x := peekCount() (A4's key: counter, read)").Key(Counter)), Valid);
    // The M69 plan §4's nested relations: O an outer default whose body omits an inner one, I.
    static readonly Tpl G = T("g", "g", "x", "g(x: i32 = State.poke(counter@uniq))", k => k.Place(Poke(k, "x := poke(move $p)")), Valid);
    static readonly Tpl F1 = T("f1", "f", "y", "f(y: i32 = State.g())", k =>
    {
        k.Copy(G, k.L("$gx"));
        k.Place(k.N("y := g(move $gx)").Input(K.Move, k.L("$gx")));
    }, Valid);
    static readonly Tpl F2 = T("f2", "f2", "y", "f2(y: i32 = label w: do let r = counter@ref; exit to w State.g() + r.count)", k =>
    {
        LetR(k);
        k.Copy(G, k.L("$gx"));
        k.N("$v = g(move $gx)").Def(k.L("$v")).Input(K.Move, k.L("$gx"));
        UseR(k, "y := $v + r.count");
    }, c => c.Error("[g/x] x := poke(move $p)"));
    static readonly Tpl Keep = T("keep", "keep", "x", "keep(x: ref/Counter during s = counter@ref) -> ref/Counter during s => x` (H",
        k => k.Place(k.N("x := &counter").Borrow(k.Id("Li"), Counter, Ref, k.Dest)), Valid);
    static readonly Tpl F3 = T("f3", "f3", "y", "f3(y: i32 = label w: do let k = State.keep(); counter.count = 1; exit to w k.count)", k =>
    {
        k.Copy(Keep, k.L("$kx"));
        Retain(k, "k", "keep", "$kx");
        k.N("counter.count = 1").A(K.Write, Counter + ".count");
        k.Place(k.N("y := k.count").Input(K.Read, k.L("k") + ".*"));
    }, c => c.Error("counter.count = 1"));
    static readonly Tpl HA = T("h-a", "h", "a", "h(a: ref/Counter during s = counter@ref, b: i32 = State.poke(counter@uniq))` (a's default; H",
        k => k.Place(k.N("a := &counter").Borrow(k.Id("La"), Counter, Ref, k.Dest)), Valid);
    static readonly Tpl HB = T("h-b", "h", "b", "h(a: ref/Counter during s = counter@ref, b: i32 = State.poke(counter@uniq))` (b's default",
        k => k.Place(Poke(k, "b := poke(move $p)")), Valid);
    static readonly Tpl F4 = T("f4", "f4", "y", "f4(y: i32 = State.h())", k =>
    {
        k.Copy(HA, k.L("$ha"));
        k.Copy(HB, k.L("$hb"));
        k.Place(k.N("y := h(move $ha, move $hb)").Input(K.Move, k.L("$ha")).Input(K.Move, k.L("$hb")));
    }, c => c.Error("[h/b] b := poke(move $p)"));
    static void RPoke(Ctx k)
    {
        LetR(k);
        Poke(k, "$v = poke(move $p)").Def(k.L("$v"));
        UseR(k, "x := $v + r.count");
    }
    static readonly Tpl G5 = T("g5", "g5", "x", "g5(x: i32 = label w: do let r = counter@ref; exit to w State.poke(counter@uniq) + r.count)", RPoke, c => c.Error("$v = poke(move $p)"));
    static readonly Tpl F5 = T("f5", "f5", "y", "f5(y: i32 = State.g5())", k =>
    {
        k.Copy(G5, k.L("$gx"));
        k.Place(k.N("y := g5(move $gx)").Input(K.Move, k.L("$gx")));
    }, c => Omit(c.Valid(), "[g5/x] $v = poke(move $p)"));
    static readonly Tpl Keep2 = T("keep2", "keep2", "x", "keep2(x: ref/Counter during s = label w: do State.poke(counter@uniq); exit to w counter@ref) -> ref/Counter during s => x` (H", k =>
    {
        Poke(k, "poke(move $p)");
        k.Place(k.N("x := &counter").Borrow(k.Id("L"), Counter, Ref, k.Dest));
    }, Valid);
    // The candidate order and independent conflicts: a template-owned conflict at the access a caller-owned one shares, or on the Loan another access meets.
    static readonly Tpl Add5 = T("add5", "add5", "x", "add5(c: uniq/Counter, x: i32 = label w: do let r = counter@ref; exit to w State.poke(counter@uniq) + r.count)", RPoke, c => c.Error("$v = poke(move $p)"));
    static readonly Tpl KeepI = T("keepi", "keepi", "x", "keepi(x: ref/Counter during s = label w: do let r = counter@ref; State.poke(counter@uniq); exit to w r) -> ref/Counter during s => x` (H", k =>
    {
        LetR(k);
        Poke(k, "poke(move $p)");
        k.Place(k.N("x := r").Input(K.Read, k.L("r")).Flow(k.L("r"), k.Dest));
    }, c => c.Error("poke(move $p)"));
    // The M69 plan §4's external reservation (model form; no source form known): in a copy the call activates a reservation the caller formed; in the
    // template it Moves its slot, A1's DefaultArgumentMove_Kd.
    static readonly Tpl Act = T("act", "act", "x", "act(c: uniq/Counter, x: i32 = label w: do let r = counter@ref; exit to w consume(c) + r.count)` (model form", k =>
    {
        LetR(k);
        var n = k.N("$v = consume(c) (activates c's reservation)").Def(k.L("$v"));
        if (k.Res is { } res) n.Input(K.Move, res.H).Activate(res.L); else n.Input(K.Move, k.Slot("c"));
        UseR(k, "x := $v + r.count");
    }, c => { c.Backstop = "A1's DefaultArgumentMove_Kd, R4-M69"; return c.Error(); }, prepared: ["c"]);
    // Guards (err(ended), E7): a guard inside the default whose candidate escapes into its value (model form, as case 108); a slot copy (dt-copyref).
    static readonly Tpl Gd = T("gd", "gd", "y", "gd(y: ref/Data during s = match counter@ref { case .Data(d) if save(d) => … })` (model form: the guard stores its candidate into the value", k =>
    {
        k.N("$s = &counter (Subject)").Def(k.L("$s")).Borrow(k.Id("Lsub"), Counter, Ref, k.L("$s"));
        k.N("d = $s.data (candidate)").Def(k.L("d")).Borrow(k.Id("Ld"), k.L("$s") + ".*.data", Ref, k.L("d"), sharedAt: 1);
        k.Place(k.N("guard: y := d").Flow(k.L("d"), k.Dest));
        k.N("guard cleanup: EndBorrow d").EndBorrow(k.L("d"));
    }, c => c.Error("guard cleanup: EndBorrow d"));
    static readonly Tpl Kd = T("kd", "keepd", "b", "keepd(a: ref/Data during p, b: ref/Data during p = a) -> ref/Data during p` (DefaultAccessTest :17's form",
        k => k.Place(k.N("b := copy (*a)").Input(K.Read, k.Slot("a")).Flow(k.Slot("a"), k.Dest)), Valid, slots: ["a"]);
    // A two-view generic callee (PD15 (ii)): G171's default in addg.
    static readonly Tpl AddG = T("addg", "addg", "x", "addg<s/T>(a: s/T, c: uniq/Counter, x: i32 = State.poke(counter@uniq))", k => k.Place(Poke(k, "x := poke(move $p)")), Valid);
    // A recursive default (a template call, no copy): its inner call reserves counter for c, then calls the template, whose summary writes counter.
    const string TemplateCall = "x omitted: $d = addr#x(move $i) (its summary writes counter: A4's key)";
    static Node Recur(Ctx k, string call)
    {
        k.N("$i = &$n (the template call's input)").Def(k.L("$i")).Borrow(k.Id("Ls"), k.L("#n"), Ref, k.L("$i"));
        k.N(TemplateCall).Def(k.L("$d")).Input(K.Move, k.L("$i")).Flow(k.L("$i") + ".*", k.L("$d")).Key(Counter, write: true);
        k.N("R-a: addr's inspection Loans end").EndInspections(k.Id("Ls"));
        return k.N(call).Input(K.Move, k.L("$c")).Input(K.Move, k.L("#n")).Input(K.Move, k.L("$d")).Activate(k.Id("Lc"));
    }
    static readonly Tpl Addr = T("addr", "addr", "x", "addr(c: uniq/Counter, n: i32, x: i32 = if n == 0 => State.poke(counter@uniq) else => State.addr(counter@uniq, n - 1))", k =>
    {
        k.N("if n == 0").Input(K.Read, k.Slot("n")).Goto("$p = &uniq two-phase counter (poke's argument)", "$c = &uniq two-phase counter; $n = (*n) - 1 (addr's slots)");
        Poke(k, "x := poke(move $p)").Def(k.Dest).Goto("return _0");
        k.N("$c = &uniq two-phase counter; $n = (*n) - 1 (addr's slots)").Def("$c", "#n").Input(K.Read, k.Slot("n")).Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        Recur(k, "x := addr(move $c, move $n, move $d)").Def(k.Dest);
    }, c => c.Error(TemplateCall), prepared: ["n"]);
    // Incomplete templates: P13's hold (e17's store prefix: storeL stores past lh, a ByArgument holder) with G5's conflict; A4's Limit at the default's call.
    static readonly Tpl Hold = T("hold", "addh", "x", "addh(c: uniq/Counter, x: i32 = label w: do (e17's prefix: storeL(lh@uniq, b@ref)); let r = counter@ref; exit to w State.poke(counter@uniq) + r.count)` (held", k =>
    {
        k.N("var x0 = 1").Def(k.L("#x0"));
        k.N("var z = x0@ref").Def(k.L("z")).Borrow(k.Id("Lx"), k.L("#x0"), Ref, k.L("z"));
        k.N("var w = z@ref").Def(k.L("w")).Borrow(k.Id("Lz"), k.L("z"), Ref, k.L("w"));
        k.N("$1 = &uniq two-phase w").Def(k.L("$1")).Borrow(k.Id("Lw"), k.L("w"), Uniq, k.L("$1"), reserve: true);
        k.N("var lh = Loan.init(move $1)").Def(k.L("lh")).Input(K.Move, k.L("$1")).Activate(k.Id("Lw")).Flow(k.L("$1"), k.L("lh")).Flow(k.L("$1") + ".*", k.L("lh"));
        k.N("var b = x0@ref").Def(k.L("b")).Borrow(k.Id("Lb0"), k.L("#x0"), Ref, k.L("b"));
        k.N("$q = &uniq two-phase lh").Def(k.L("$q")).Borrow(k.Id("Lq"), k.L("lh"), Uniq, k.L("$q"), reserve: true);
        k.N("$v0 = b@ref").Def(k.L("$v0")).Borrow(k.Id("Lb"), k.L("b"), Ref, k.L("$v0"));
        k.N("storeL(move $q, move $v0)").Input(K.Move, k.L("$q")).Input(K.Move, k.L("$v0")).Activate(k.Id("Lq")).Store(k.L("$q"), k.L("$q"), true, 1).Store(k.L("$q"), k.L("$v0"), false, 1);
        k.C.SharedOnly.UnionWith([k.L("z"), k.L("w"), k.L("b")]); k.C.ByArgument.Add(k.L("lh"));
        RPoke(k);
    }, c => { c.UnsuppAt = ["$v = poke(move $p)"]; return c.Error(); });
    // R3 inside a default (R18's e8 in a copy): replacing State.holder destroys its old value, whose drop reads State.holder (its own key).
    static readonly Tpl SetD = T("setd", "setd", "x", "setd(x: i32 = label w: do holder = Holder.init(); exit to w 0)` (the old value's drop reads State.holder", k =>
    {
        k.N("$n = Holder.init()").Def(k.L("$n"));
        k.N("drop State.holder (old value; its drop reads State.holder)").Drop(Holder).Key(Holder);
        k.N("State.holder = move $n").Input(K.Move, k.L("$n")).Assign(Holder);
        k.Place(k.N("x := 0"));
    }, c => c.Error("drop State.holder (old value; its drop reads State.holder)"));
    static readonly Tpl Lim = T("lim", "addl", "x", "addl(c: uniq/Counter, x: i32 = State.lim())` (lim's summary lies past A4's bound: Limit",
        k => k.Place(k.N("x := lim() (A4's Limit)").Fx(limit: true)), Valid);
    // A loop around an omitting call of keep2: prev keeps the last iteration's value, which the next iteration's copy meets.
    static void Loop(Ctx k)
    {
        k.N("var prev = other@ref").Def(k.L("prev")).Borrow(k.Id("Lo"), Other, Ref, k.L("prev"));
        k.N("while c").Goto(k.Lab("loop body: let k = State.keep2()"), k.Lab("after the loop: inspect(prev)"));
        k.N("loop body: let k = State.keep2()");
        k.Copy(Keep2, k.L("$x"));
        Retain(k, "k", "keep2", "$x");
        k.N("inspect(prev)").Input(K.Read, k.L("prev"));
        k.N("prev = move k").Def(k.L("prev")).Input(K.Move, k.L("k")).Flow(k.L("k"), k.L("prev")).Goto(k.Lab("while c"));
        k.N("after the loop: inspect(prev)").Input(K.Read, k.L("prev"));
    }
    const string Evaluations = "the issue scope names a site, not an evaluation";
    static readonly Tpl LoopT = T("loop", "f6", "y", "f6(y: i32 = label w: do var prev = other@ref; while c => (let k = State.keep2(); inspect(prev); prev = k); exit to w 0)` (H", k =>
    {
        Loop(k);
        k.Place(k.N("y := 0"));
    }, c => { c.Finding = Evaluations; return c.Error("[keep2/x] poke(move $p)"); });

    // ---- Callers.
    static IEnumerable<Case> Callers() => [G171(""), G171("disjoint"), G171("read"), G171("explicit"), Effect(""), Effect("disjoint"), Effect("read"), Effect("explicit"),
        N1(), N2(), N3(), N3Top(), N4(), Siblings(), N5(), Twice(), TwiceBoth(), ActOut(), First(rev: false), First(rev: true), Independent(), GuardIn(), GuardAcross(),
        Generic(uniq: true), Generic(uniq: false), Recursive(), Held(), Limit(), DropIn(), LoopCaller(), LoopNested(), LoopOwn()];

    // PLAN G171 (R4's fact check of 2026-10-21; eval-1020's m69y-default, m69y-explicit): `State.add(counter@uniq)` in group State. The caller reserves
    // counter in add's slot c; add's default, copied after it, borrows counter exclusively for poke, and that activation meets the reservation (R4-M68:
    // shared in its holder): the caller's failure, CallReservationConflict_Kd in the explicit twin (12:37). Twins: another static, a shared borrow.
    static Case G171(string twin)
    {
        var t = twin switch { "disjoint" => AddOther, "read" => AddRead, _ => Add };
        var c = S("dc20-g171" + (twin == "" ? "" : "-" + twin), twin == "explicit" ? "PLAN G171's explicit twin: `State.add(counter@uniq, State.poke(counter@uniq))`"
            : $"{(twin == "" ? "PLAN G171" : "G171's " + twin + " twin")}: `State.add(counter@uniq)` omitting dt20-{t.Id}'s default");
        var k = Root(c);
        k.N("$c = &uniq two-phase counter (add's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        if (twin == "explicit") k.Here(t, "$x"); else k.Copy(t, "$x");
        k.N("_ = add(move $c, move $x)").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        return twin switch { "disjoint" or "read" => c.Valid(), "explicit" => c.Error("x := poke(move $p)"), _ => c.Error("[add/x] x := poke(move $p)") };
    }

    // G171's effect form (the M69 plan §6: receiver-group-default.kimi): `_ = counter.add()` reserves counter for the receiver; the copied default's call
    // writes counter (A4's key), an err(effect) against that reservation (R18's R1, any mode). Twins: a key on other, a read key, the explicit argument.
    static Case Effect(string twin)
    {
        var t = twin switch { "disjoint" => TouchOther, "read" => TouchRead, _ => Touch };
        var c = S("dc20-fx" + (twin == "" ? "" : "-" + twin), twin == "explicit" ? "G171's effect form, explicit: `counter.add(State.touch())`"
            : $"G171's effect form{(twin == "" ? "" : ", " + twin + " twin")}: `_ = counter.add()` omitting dt20-{t.Id}'s default");
        var k = Root(c);
        k.N("$r = &uniq two-phase counter (add's receiver)").Def("$r").Borrow("Lr", Counter, Uniq, "$r", reserve: true);
        if (twin == "explicit") k.Here(t, "$x"); else k.Copy(t, "$x");
        k.N("_ = add(move $r, move $x)").Input(K.Move, "$r").Input(K.Move, "$x").Activate("Lr");
        return twin switch
        {
            "disjoint" or "read" => c.Valid(),
            "explicit" => c.Error("x := touch() (A4's key: counter, write)"),
            _ => c.Error("[add/x] x := touch() (A4's key: counter, write)"),
        };
    }

    // §4 row 1: the caller's Loan, the access in I (O: f's default, an explicit argument's evaluation; I: g's inside it).
    static Case N1()
    {
        var c = S("dc20-n1", "§4 row 1 (the caller's Loan, the access in I): `State.add(counter@uniq, State.f())`, f omitting dt20-f1's default (O), which omits dt20-g's (I)");
        var k = Root(c);
        k.N("$c = &uniq two-phase counter (add's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(F1, "$fy");
        k.N("$x = f(move $fy)").Def("$x").Input(K.Move, "$fy");
        k.N("_ = add(move $c, move $x)").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        return c.Error("[f/y][g/x] x := poke(move $p)");
    }

    // A caller of one default: `_ = State.fn()` omitting the template's default.
    static Case Calls(string id, string title, Tpl t, Func<Case, Case> expect)
    {
        var c = S(id, title);
        var k = Root(c);
        k.Copy(t, "$y");
        k.N($"_ = {t.Fn}(move $y)").Input(K.Move, "$y");
        c.DupOf = "dt20-" + t.Id;
        return expect(c);
    }
    // §4 rows 2, 3, the siblings' and 5: O's Loan / access in I; I's Loan / access in O after I; siblings in O; I / I. Each is O's (I's) template's.
    static Case N2() => Calls("dc20-n2", "§4 row 2 (O's Loan, the access in I): `_ = State.f2()`", F2, c => Omit(c.Valid(), "[f2/y][g/x] x := poke(move $p)"));
    static Case N3() => Calls("dc20-n3", "§4 row 3 (I's Loan, the access in O after I): `_ = State.f3()`", F3, c => Omit(c.Valid(), "[f3/y] counter.count = 1"));
    static Case N4() => Calls("dc20-n4", "§4 row 4 (sibling copies in O): `_ = State.f4()`", F4, c => Omit(c.Valid(), "[f4/y][h/b] b := poke(move $p)"));
    static Case N5() => Calls("dc20-n5", "§4 row 5 (I / I, I nested in O): `_ = State.f5()`", F5, c => Omit(c.Valid(), "[f5/y][g5/x] $v = poke(move $p)"));
    static Case DropIn() => Calls("dc20-r3", "R3 inside a copy: `_ = State.setd()` (R3's Loan, if 2b-D issues it at the Drop, lies in the Drop's scope)", SetD,
        c => Omit(c.Valid(), "[setd/x] drop State.holder (old value; its drop reads State.holder)"));
    static Case GuardIn() => Calls("dc20-guard-in", "a guard ending inside a copy, its candidate escaping in the value: `_ = State.gd()`", Gd, c => Omit(c.Valid(), "[gd/y] guard cleanup: EndBorrow d"));

    // Row 3 at the top level: I's Loan, the caller's access after I.
    static Case N3Top()
    {
        var c = S("dc20-n3top", "§4 row 3 at the top level (I's Loan, the caller's access): `let k = State.keep(); counter.count = 1; inspect(k)`");
        var k = Root(c);
        k.Copy(Keep, "$x");
        Retain(k, "k", "keep", "$x");
        k.N("counter.count = 1").A(K.Write, Counter + ".count");
        k.N("inspect(k)").Input(K.Read, "k.*");
        return c.Error("counter.count = 1");
    }

    // Row 4 at the top level: sibling copies, neither template invalid, conflict in the caller's evaluation.
    static Case Siblings()
    {
        var c = S("dc20-sib", "§4 row 4 at the top level (sibling copies): `_ = State.h()` omitting dt20-h-a's and dt20-h-b's defaults");
        var k = Root(c);
        k.Copy(HA, "$ha");
        k.Copy(HB, "$hb");
        k.N("_ = h(move $ha, move $hb)").Input(K.Move, "$ha").Input(K.Move, "$hb");
        return c.Error("[h/b] b := poke(move $p)");
    }

    // §4 row 6: the first top-level copy's Loan, the second's access, equal syntax.
    static Case Twice()
    {
        var c = S("dc20-twice", "§4 row 6 (two top-level copies, equal syntax): `let k1 = State.keep2(); let k2 = State.keep2(); inspect(k1)`");
        var k = Root(c);
        k.Copy(Keep2, "$x1", "#1");
        Retain(k, "k1", "keep2", "$x1");
        k.Copy(Keep2, "$x2", "#2");
        Retain(k, "k2", "keep2", "$x2");
        k.N("inspect(k1)").Input(K.Read, "k1.*");
        return c.Error("[keep2/x#2] poke(move $p)");
    }

    // Record identity (the M69 plan §5, U11): two copies sharing a source node, each meeting another caller Loan.
    static Case TwiceBoth()
    {
        var c = S("dc20-twice-both", "record identity: `State.add(counter@uniq); State.add(counter@uniq)` (two copies of dt20-add's default, each meeting its own call's reservation)");
        var k = Root(c);
        foreach (var x in new[] { "1", "2" })
        {
            k.N($"$c{x} = &uniq two-phase counter (add's slot c, call {x})").Def("$c" + x).Borrow("Lc" + x, Counter, Uniq, "$c" + x, reserve: true);
            k.Copy(Add, "$x" + x, "#" + x);
            k.N($"_ = add(move $c{x}, move $x{x})").Input(K.Move, "$c" + x).Input(K.Move, "$x" + x).Activate("Lc" + x);
        }
        return c.Error("[add/x#1] x := poke(move $p)", "[add/x#2] x := poke(move $p)");
    }

    // The external reservation (model form): the copy's call activates the caller's reservation while the copy's own Loan r lives.
    static Case ActOut()
    {
        var c = S("dc20-act-out", "an activation in a copy of a reservation the caller formed (model form), against the copy's own Loan r: `State.act(counter@uniq)`");
        var k = Root(c);
        k.N("$c = &uniq two-phase counter (act's slot c, reserved before the copy)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(Act, "$x", reservation: ("$c", "Lc"));
        k.N("_ = act(move $x) (model form)").Input(K.Move, "$x");
        return c.Error("[act/x] $v = consume(c) (activates c's reservation)");
    }

    // A covered first candidate (the copy's r, I-owned) and a later caller-owned one (the reservation Lc) at one access; and the Loan ids reversed.
    static Case First(bool rev)
    {
        var c = S(rev ? "dc20-first-rev" : "dc20-first", $"one access, two candidates: `State.add5(counter@uniq)` (the copy's r, then the caller's reservation; candidate order {(rev ? "Lc first" : "r first")})");
        var k = Root(c);
        k.N("$c = &uniq two-phase counter (add5's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(Add5, "$x");
        k.N("_ = add5(move $c, move $x)").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        c.LoanOrder = rev ? ["Lc", "[add5/x]Lr"] : ["[add5/x]Lr", "Lc"];
        return Omit(c.Error("[add5/x] $v = poke(move $p)"), "[add5/x] $v = poke(move $p)");
    }

    // Independent conflicts on one Loan: the copy's r meets the copy's poke (the template's) and the caller's later write (the caller's).
    static Case Independent()
    {
        var c = S("dc20-indep", "one Loan, two accesses: `let k = State.keepi(); counter.count = 1; inspect(k)`");
        var k = Root(c);
        k.Copy(KeepI, "$x");
        Retain(k, "k", "keepi", "$x");
        k.N("counter.count = 1").A(K.Write, Counter + ".count");
        k.N("inspect(k)").Input(K.Read, "k.*");
        return Omit(c.Error("counter.count = 1"), "[keepi/x] poke(move $p)");
    }

    // A guard across a copy: the copy, inside the caller's guard, copies the candidate's value into the result the caller keeps past the guard.
    static Case GuardAcross()
    {
        var c = S("dc20-guard-across", "a guard ending across a copy: `match counter@ref { case .Data(d) if (w = keepd(d)) … }; use(w)` (model form; keepd omits dt20-kd's default)");
        var k = Root(c);
        k.N("$s = &counter (Subject)").Def("$s").Borrow("Lsub", Counter, Ref, "$s");
        k.N("d = $s.data (candidate)").Def("d").Borrow("Ld", "$s.*.data", Ref, "d", sharedAt: 1);
        k.N("$a = copy d (keepd's slot a)").Def("$a").Input(K.Read, "d").Flow("d", "$a");
        k.Copy(Kd, "$b", slotLocals: ["$a"]);
        k.N("w = keepd(move $a, move $b) (retains a and b)").Def("w").Input(K.Move, "$a").Input(K.Move, "$b").Flow("$a", "w").Flow("$b", "w");
        k.N("guard cleanup: EndBorrow d").EndBorrow("d");
        k.N("use(w)").Use("w");
        return c.Error("guard cleanup: EndBorrow d");
    }

    // A two-view generic caller (round 19's dc-gen2 shapes) of G171's form: each view is its own body (R4-M71) with its own copy.
    static Case Generic(bool uniq)
    {
        var c = S($"dc20-gen-{(uniq ? "u" : "r")}", $"`g<s/T>(v: s/T) => State.addg(v, counter@uniq)`, view s = {(uniq ? "uniq" : "ref")} (dt20-addg's default copied)");
        var k = Root(c);
        k.N($"entry (v: {(uniq ? "uniq" : "ref")}/T)").Param("Lpv", "v");
        if (uniq) k.N("$a = &uniq two-phase (*v) (addg's slot a)").Def("$a").Borrow("La", "v.*", Uniq, "$a", reserve: true);
        else k.N("$a = copy v (addg's slot a)").Def("$a").Input(K.Read, "v").Flow("v", "$a");
        k.N("$c = &uniq two-phase counter (addg's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(AddG, "$x");
        var call = k.N("_ = addg(move $a, move $c, move $x)").Input(K.Move, "$a").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        if (uniq) call.Activate("La");
        return c.Error("[addg/x] x := poke(move $p)");
    }

    // A recursive default (dt20-addr): the caller's template call has no copy, so its effect against the caller's reservation lies in the caller's scope.
    static Case Recursive()
    {
        var c = S("dc20-recur", "`State.addr(counter@uniq, 3)` (dt20-addr's recursive default: a template call)");
        var k = Root(c);
        k.N("$c = &uniq two-phase counter; $n = 3 (addr's slots)").Def("$c", "#n").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        Recur(k, "_ = addr(move $c, move $n, move $d)");
        return c.Error(TemplateCall);
    }

    // The caller's own failure, independent of every default: y0 written while r0 lives.
    static void OwnFailure(Ctx k, bool before)
    {
        k.N("var y0 = 1").Def("#y0");
        k.N("let r0 = y0@ref").Def("r0").Borrow("Lr0", "#y0", Ref, "r0");
        if (!before) return;
        k.N("y0 = 2").Assign("#y0");
        k.N("inspect(r0)").Input(K.Read, "r0");
    }

    // A P13-held template copied: the copy's store holds the caller's view, so the caller's own failures, the independent one and G171's, are located U.
    static Case Held()
    {
        var c = S("dc20-held", "`var y0 = 1; let r0 = y0@ref; y0 = 2; inspect(r0); State.addh(counter@uniq)` (dt20-hold's default copied)");
        var k = Root(c);
        OwnFailure(k, before: true);
        k.N("$c = &uniq two-phase counter (addh's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(Hold, "$x");
        k.N("_ = addh(move $c, move $x)").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        c.UnsuppAt = ["y0 = 2", "[addh/x] $v = poke(move $p)"];
        return Omit(c.Error(), "[addh/x] $v = poke(move $p)");
    }

    // A4's Limit inside a copy (a limit record, kept by R4-M69) beside the caller's independent failure.
    static Case Limit()
    {
        var c = S("dc20-limit", "`var y0 = 1; let r0 = y0@ref; State.addl(counter@uniq); y0 = 2; inspect(r0)` (dt20-lim's default copied)");
        var k = Root(c);
        OwnFailure(k, before: false);
        k.N("$c = &uniq two-phase counter (addl's slot c)").Def("$c").Borrow("Lc", Counter, Uniq, "$c", reserve: true);
        k.Copy(Lim, "$x");
        k.N("_ = addl(move $c, move $x)").Input(K.Move, "$c").Input(K.Move, "$x").Activate("Lc");
        k.N("y0 = 2").Assign("#y0");
        k.N("inspect(r0)").Input(K.Read, "r0");
        c.UnsuppAt = ["[addl/x] x := lim() (A4's Limit)"];
        return c.Error("y0 = 2");
    }

    // A caller loop around a copy: the copy's Loan from the last iteration, kept in prev, meets the next iteration's copy. Both lie in one scope.
    static Case LoopCaller()
    {
        var c = S("dc20-loop", "a caller loop around a copy: `var prev = other@ref; while c => (let k = State.keep2(); inspect(prev); prev = k); inspect(prev)`");
        Loop(Root(c));
        c.Finding = Evaluations;
        return c.Error("[keep2/x] poke(move $p)");
    }

    // A caller loop around a copy whose failure is its template's: r, the copy's local, is dead at the copy's entry, so no earlier evaluation's
    // value reaches it.
    static Case LoopOwn()
    {
        var c = S("dc20-loop-own", "a caller loop around a copy whose failure is its template's: `while c => _ = State.g5()`");
        var k = Root(c);
        k.N("while c").Goto("loop body: _ = State.g5()", "after the loop");
        k.N("loop body: _ = State.g5()");
        k.Copy(G5, "$x");
        k.N("_ = g5(move $x)").Input(K.Move, "$x").Goto("while c");
        k.N("after the loop");
        c.DupOf = "dt20-g5";
        return Omit(c.Valid(), "[g5/x] $v = poke(move $p)");
    }

    // The same loop inside a template (dt20-loop), copied: the failure is the outer template's, not keep2's.
    static Case LoopNested()
    {
        var c = Calls("dc20-loop-nested", "a template's loop around a nested copy: `_ = State.f6()`", LoopT, c => Omit(c.Valid(), "[f6/y][keep2/x] poke(move $p)"));
        c.Finding = Evaluations + ": the boundary is f6's, whose template reports it, not keep2's";
        return c;
    }
}
