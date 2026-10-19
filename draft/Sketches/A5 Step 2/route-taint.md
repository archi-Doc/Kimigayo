# Route taint: prototype round 16 (PLAN G148's final review, B1)

Status (2026-10-19): **superseded by the owner's body hold (2026-10-19)**, [body-hold.md](body-hold.md): round 16's independent
review found writes through a reference and E5's seed passing no taint (its B1). Kept as record. Working record, not authority.

## The defect and the owner's decision

The G148 unit (branch `claude/a5-g148` at `b335393b`) gives P13's interim as a set taint: a value a call stores only past a holder
whose Type passes rule G's guard only through a Type argument, a pair layer or an abstract part carries P13's marker, and a conflict
whose Loan only sets with a taint hold is a located `Unsupported_Kd` (DECISIONS R4-M73, the planning session's ruling (B)). The final
review's B1 found two routes the taint does not follow:

1. **The closure.** The gather reaches the holder a found direct Loan names (`MirBorrows.Candidates.cs:72-75`) and scans its state
   with no taint (`Scan` resets it, `:27`), so a holder that enters the check set only because a tainted set names it is read clean.
2. **err(ended).** γ_H is checked without a taint (R4-M73 and R4-M70 record it as a limit), so a guard-local Origin that reaches a
   live holder only past a by-argument holder is a Language problem.

R4-M76 asks that "a conflict that depends on such a store is a located `Unsupported_Kd`". The owner chose route taint (2026-10-19,
verbatim: 「汚染を経路で追跡 (推奨)」), one generalization of R4-M70's taint for both kinds:

- a holder reached only through Loans of tainted sets is scanned tainted; a later clean reach rescans it clean;
- per γ marker, the check records whether a clean set held it, and an err(ended) whose γ only tainted sets hold is the located
  `Unsupported_Kd` (located by its taint kind, as today: a default's at the omitting call, P13's at the access);
- no per-kind branch beyond the existing location rule; it closes R4-M70's err(ended) limit and the default taint's analog of the
  closure route.

## The rule

The check set of an access (A3 E13 (1)) is its roots (the holders live after ℓ that ℓ does not overwrite, E, the values the access
takes, transit, E5's seed) closed through the holders their direct Loans name. A set **carries** a taint when it holds a taint
marker (R4-M70's default taint of a pending slot, P13's marker). A holder of the check set is **read clean** when its own set
carries no taint and it is a root or a clean holder's direct Loan (or Φ_E) names it; otherwise it is **read tainted**, with the
first taint on its route (its own, else the route's; a default's before P13's, as the per-local markers precede P13's element).
A conflicting Loan some clean holder holds is the Language problem; one only tainted holders hold is the located `Unsupported_Kd`,
located by its taint's kind. err(ended) reads γ_H the same way: γ_H a clean holder holds is the Language problem, γ_H only tainted
holders hold the located `Unsupported_Kd`.

Soundness: the rule changes no state and no conflict, only how a found conflict is reported (a Language problem or a located
`Unsupported_Kd`); it never accepts. The prototype checks it: no taint mode drops a Loan or γ that rule G reports, in any of the
395 cases.

## The prototype round

`draft/Sketches/A5 Step 0/A5RulesPrototype.cs`, `dotnet run proto.cs -- -r16` (`-v` adds every fact). The in-tree prototype had
rounds 1-14; G148's round, its design reviews' union prototype (`artifacts/handoff/2026-10-18-pause/r4-scratch/rv-g148/proto/`,
main checkout), is merged in unchanged as round 15 (`-r15`: on round 14's design every variant's listed-loss, unlisted and unsound
columns equal the review's run; R4-M68 moves 4 errors, 5 under the G variants, to the formation). Round 16 adds the minimal model:

- `Case.ByArgument`: holders that pass rule G's guard only by argument (`MirWrites.ThroughArgument`). A store row's targets reached
  only past one receive the value with P13's marker `τ13` (an empty value stores nothing, so no marker: the review's N1).
- `Node.DefaultTaint(D)`: the value a default's evaluation places into its pending slot D carries `τD:D` (R4-M70).
- Markers flow with the values that hold them, never end and never conflict, as γ does.
- Modes, all on rule G (`R14G`): `R16Set`, the unit as reviewed (a holder is clean where its own set carries no taint;
  err(ended) unchecked); `R16Route`, the rule above; `R16RouteNoGamma` and `R16GammaOnly`, each without one half.

`R16Set` reproduces the compiler at `b335393b` in every case below the compiler can express. The two "only" forms cannot be
expressed, since the compiler's retention ties x and the default by Origin. The compiler probes ran through a scratch
`MirBorrowsTest` harness and are not tracked; the review's sources are `rv-g148f/probes/closure3.kimi` and `guard.kimi`, and the
default ones use `viaD` and `saveDef` below. In the table, U is the located `Unsupported_Kd` and "error" a Language problem.

| Case | Program | R16Set (the unit, the compiler today) | R16Route (decided) | NoGamma | GammaOnly |
| --- | --- | --- | --- | --- | --- |
| closureRoute | `var w = z@ref; var lh = Loan<uniq/(ref/(ref/i32))>.init(w@uniq); var b = y@ref; storeL(lh@uniq, b@ref); y = 5; inspect(w)` | error `y = 5`; U `inspect(w)` | U `y = 5`, `inspect(w)` | = Route | = Set |
| closureRouteB | the same with `b = x@ref` | U both | U both | U both | U both |
| closureRouteClean | closureRoute, then `inspect(b)` (b a live, clean root) | error `y = 5`; U `inspect(w)` | the same | the same | the same |
| guardP13 | `var lh = Loan<uniq/(ref/Data)>.init(saved@uniq); match packet .Data(let d) if storeD(lh@uniq, d) => (); inspect(saved)` | error at d's cleanup; U `inspect(saved)` | U at d's cleanup, `inspect(saved)` | = Set | = Route |
| guardP13Dead | without `inspect(saved)` | valid | valid | valid | valid |
| guardP13Clean | plus `keep(direct@uniq, d)` with `direct` used later | error at d's cleanup; U `inspect(saved)` | the same | the same | the same |
| closureRouteD | `var b = y@ref; let r = viaD(b@ref); y = 5; inspect(r)`, r keeping only y's (the default's) value | error `y = 5` | U at the omitting call | = Route | = Set |
| closureRouteDJoin | the same, r keeping x's and y's values (the compiler's retention) | error `y = 5` | U at the omitting call | = Route | = Set |
| closureRouteDLive | closureRouteDJoin, then `inspect(b)` | error `y = 5` | the same | the same | the same |
| guardDefault | `match packet .Data(let d) if saveDef(saved@uniq, d) => (); inspect(saved)`, saveDef storing x and its default v | error at d's cleanup | U at the omitting call | = Set | = Route |
| guardDefaultOnly | the same, saveDef storing v only | error at d's cleanup | U at the omitting call | = Set | = Route |
| guardDefaultDead | without `inspect(saved)` | valid | valid | valid | valid |
| cleanRoute | MirBorrowsTest's: a live generic iterator keeps the Array's shared Loan clean | error at the append; U at xs's end | the same | the same | the same |
| r14B-u (round 15) | P13's shape (`for e in arr` with arr holding a parameter-derived child), its iterator marked ByArgument | U at arr's end | the same | the same | the same |

Callees: `storeL(l: uniq/Loan<uniq/(ref/(ref/i32 during p) during q) during a>, v: ref/(ref/i32 during p) during q)`,
`storeD(l: uniq/Loan<uniq/(ref/Data during q) during a>, v: ref/Data during q) -> bool`, `viaD(x: ref/(ref/i32 during p) during q,
y: ref/(ref/i32 during p) during q = x) -> ref/(ref/i32 during p) during q => y`, `saveDef(slot: uniq/(ref/Data during s), x:
ref/Data during s, v: ref/Data during s = x) -> bool`.

## Results

- **Round 16: 0 failures.** `R16Route` gives each case the outcome the decided rule names.
- **Earlier cases.** Of the 382, every outcome under every taint mode equals `R14G`'s except r14B-u: rule G's listed-loss rejection
  becomes the located `Unsupported_Kd` (P13's interim, as R4-M76 asks). No taint mode drops a Loan or γ that `R14G` reports.
- **Both halves are needed.** `R16RouteNoGamma` keeps guardP13's and guardDefault's err(ended) as Language problems;
  `R16GammaOnly` keeps closureRoute's and closureRouteD's `y = 5`.
- **The default taint's analog exists.** closureRouteD, closureRouteDJoin and guardDefault reproduce B1 for R4-M70's taint. The
  compiler at `b335393b` gives each a Language problem: `viaD`'s result r and `saveDef`'s slot join the explicit argument's clean
  value with the default's tainted one (R4-M70's T2 join), so their sets carry the taint, but b's own set is clean (closureRouteD),
  and err(ended) has no check (guardDefault). Under route taint each is the located `Unsupported_Kd` at the omitting call.
- **Cost of the default analog.** In the compiler's form the explicit argument's own value takes the same route, so per value the
  Language problem would be right. The route rule makes it `Unsupported_Kd` through the T2 join, the imprecision R4-M70 accepts
  until A5-F9. The "only" forms (closureRouteD, guardDefaultOnly) are the cases where the Language problem is wrong.

## Cost (V16)

The gather scans each holder's state once per pass (A3 E13 (5)). Under the route rule a holder first reached through tainted sets
is scanned tainted. A later clean reach rescans it clean; a clean scan is never followed by a tainted one, since clean dominates.
So each holder is scanned at most twice per pass and check, a constant factor on V16's per-access term, and no new term. The taint
a scan starts with is the reaching set's (one int per reach). For γ, the stamps `markSeen`/`markVia` already record the first holder;
one bit per marker records whether a clean set held it. Memory: one bit or int per slot and marker, in the existing stamped arrays.
The prototype does not measure Work.

## Implementation sketch (step 2, after this round's review)

- `MirBorrows.Candidates.cs` / `.CheckSets.cs`: `Reach` from a direct Loan found in a tainted set records the reaching taint on
  the named slot (a stamped per-slot taint). A slot reached clean scans clean; one reached only tainted scans with that taint
  (`Scan` starts from it instead of None). A slot scanned tainted and reached clean later is rescanned clean; its candidates' clean
  tags are added.
- `Consider` (markers): per marker, a clean bit beside `markSeen`/`markVia`, set where a clean set (or a clean-scanned slot)
  holds it.
- `MirBorrows.Checks.cs` `Ended`: γ_H with no clean holder is the located `Unsupported_Kd` by the first taint's location rule
  (`MirBorrows.Reports.cs`). This is a change in a due file (Checks.cs, R4's hotspot list) and adds no table row; the planning session
  decides whether it fits as is or waits for 2x-R2.
- Tests: closureRoute and guardP13 (with their clean twins) and the default analogs closureRouteD (the compiler's `viaD` form) and
  guardDefault assert the located `Unsupported_Kd`; cleanRoute keeps the Language problem.

## Not verified

The compiler implementation of the rule (step 2); the compiler's locations under the route rule for the default analogs; Work and
timing; whether a seed (E5) holder of a tainted stored value needs the route rule too (the prototype treats seeds as roots, read
clean when their own sets carry no taint, as the gather does today).
