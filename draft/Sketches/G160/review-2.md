# G160 plan: independent review, round 2

Reviewer: the round-1 reviewer, who wrote none of the designs. Scope:

- the rewritten `draft/Proposals/2026-10-21 G160 Raw Storage Kept by Declarations.md` (571 lines);
- the G160 row in `docs/dev/PLAN.md`;
- the new entry at the end of `draft/INTEGRATED.md`.

All three are in worktree `bin/worktrees/g160-plan`, uncommitted, base `59b78023`. I re-read §0–§11 against AGENTS, SPEC 5,
15.3.5 and 15.6, `MirTypes.Holding.cs`, `MirBorrows.Values.cs`, `.Flow.cs` and `.Calls.cs`, `AdtDef.cs`, the library's raw Fields
(`Storage.kimi`, `UniqSlice.kimi`) and plan X §4–§5.

New probes, on the dev `a1677549` binary through the same harness, are in `scratchpad/g160/review2/s1.kimi`, `s2.kimi` and
`s3.kimi`, with their `.log`/`.out` files:

| Probe | Shape | Today |
| --- | --- | --- |
| s1 `rawHolder` | `HolderR {a} {saved: ref/i32 during a}` (non-generic, no raw); `putHolder(c@uniq, x@ref)` with `c: raw/HolderR` writing `slot@follow[0].saved = v`; `x = 5` | error `x = 5` |
| s1 `rawHolderV` | the same with the raw pointer passed by value | error `x = 5` |
| s1 `boxHolder` | `BoxH {a} {p: raw/(HolderR during a), n: isize}`; `putBoxH(b@uniq, x@ref)` writing `b.p[0].saved = v`; `x = 5` | error `x = 5` |
| s2 `projField` | `HoldItem<S> {item: S.Src.Item}` with `Src.Item is raw/(ref/i32 during static)`; `h.item[0] = v` in a non-generic callee | Binding: `NotIndexable_Kd` at `h.item[0]` |
| s3 `ownRefs` | `OwnRefs {a} {data: raw/(ref/i32 during a)}`, its own allocation, safe `get(self: ref/Self) -> ref/i32 during a`; `storeO(o@uniq, x@ref)`; `x = 5`; `inspect(o.get())` | error `x = 5` (raw clause) |

## Round-1 findings

| Finding | Status | Where |
| --- | --- | --- |
| B1 walk transitions | Resolved. The Unowned walk crosses every reference layer, stops at `Unowned`, and drops raw from the guard. `uniq/(uniq/RawBox)`, `ref/(raw/…)` and `H` are pinned. A new problem is in R2-B1 below. | §4.2, §7 |
| B2 reads past a reference | Resolved: a kept step keeps the layer it has computed (`keptReadRef`, `keptReadUniq`). | §4.4 |
| B3 non-owning keeper | Resolved: X-decl is recommended, `cursorWrite` is an error under it, and K′ carries sentence 2. | §1.2, §1.3 |
| B4 direct stores | Resolved. A kept-step store goes where a call into the keeper would. Extraction gives E, consistent with X §5.2 and K′ sentence 3. `keptDirectDead` and `keptInitDead` are in §1.2. | §4.4 |
| B5 R5 presentation | Resolved. K′ is an amendment of 05:69, R-L and R-S are split, and §1.4 states R8. | §1.3, §1.4 |
| N1–N13 | Adopted as §11 says. Residues are listed below (N1, N4, N7, N8). | |

The anchors I re-checked are accurate, and the PLAN row and §1.2's "Today" column match the probes.

## Blocking findings

### R2-B1. Neither walk can tell, under a bare raw layer, whether an instance holds a Loan of its own: a `Concrete` fact is missing

- **Location.** §4.1 (`Keeping` records only Kept Fields, `Bare` and `BareParams`; "a nested instance whose own facts make it
  so"; "A nested instance in past-raw is walked as a Loan-capable part when it may hold a Loan"). §4.2 (the Unowned walk "never
  descends into a nominal Type's Fields"; "in past-raw, a Loan-capable part gives `Unowned`"). §7.
- **Problem.** Past a raw layer, both walks must know whether an instance holds a Loan-capable part through its own Fields,
  apart from its Type arguments. Round 1's `Concrete` gave that fact, and round 2 dropped it. The plan says two different
  things, and each fails:
  - **Read as "its own facts" (`Bare`/`BareParams`):** `HolderR` has no raw layer, so its facts are empty. `raw/HolderR` then
    gives no `Unowned`:
    - `rawHolder` and `rawHolderV` (s1) are errors today, through the raw clause, which this plan deletes. Under the plan they
      are accepted: an unsound regression.
    - `BoxH`'s `Keeping` stays non-`Bare`, so `boxHolder` regresses too.
  - **Read as HoldsLoans' "may hold a Loan":** the Unowned walk must then read Fields, contradicting §4.2's invariant, and it
    needs its own scratch, since `HoldsLoans` clears `seen`. Keeping's DFS evaluates the test in D's own parameters, where an
    abstract `T` counts. So `struct BoxW<T> {p: raw/(Wrap<T>)}` becomes `Bare`, and every `BoxW<i32>` gets `Unowned`. Its holders
    get a false E: `Mixed {a} {r: ref/i32 during a, w: BoxW<i32>}` makes `put(m@uniq, x@ref)` reach E, a false Language error. The
    precise answer is "Wrap holds a Loan only through `T`", so `T` joins `BareParams`, and that needs `Concrete`.
- **Evidence.** s1 output; `Holding.cs:153-156` and `Values.cs:227-230` are today's catch, which §5 deletes. §4.1 has no fact
  saying "holds a Loan-capable part other than through its parameters".
- **Correction.** Restore `Concrete(D)` in `Keeping`: D's storage, by any route, `via self` pointees included, holds a
  Loan-capable part other than through D's parameters. It is a least fixed point with the others. Then:
  - in past-raw, in both walks, an instance gives `Bare`/`Unowned` iff `Concrete`, and each argument is walked in past-raw;
  - the Unowned walk still reads no Fields.

  Add MirTypesTest rows: `raw/HolderR`, `uniq/(raw/HolderR)` and `BoxH` are `Unowned`; `BoxW<i32>` is not; `BoxW<ref/i32>` is. Add
  `rawHolder`, `rawHolderV` and `boxHolder` to §7's unchanged errors.

### R2-B2. Under X-decl, a `via self` pointee does not count toward holder eligibility or the guard, so kept stores can vanish

- **Location.** §4.2 (guard walk: "A raw layer gives nothing"), §4.1, §4.3, §4.4 (kept-step Store: carriers are the root
  holder), §1.3 (X §5 adopted as the contract).
- **Problem.** X §5.1, which §1.3 adopts, says a `via self` declaration's content "contributes as owned stored content to …
  holder eligibility". Its matrix checks a "non-generic `via self` owner whose only logical Loan-bearing content is behind a raw
  Field: the owner is a holder and retains that content". The plan's guard walk drops every raw layer. Take `OwnRefs {a} {data:
  raw/(ref/i32 during a)}` declared `via self`:
  - It holds no Loan, so it is not a holder.
  - `storeO(o@uniq, x@ref)`: the operand's Loan names no holder, so refs(S) is empty. `Keeping` makes it non-`Bare`, so there is
    no E. x's Loan lands nowhere.
  - `x = 5; inspect(o.get())`, a safe accessor, is accepted. That is unsound.
  - Without the declaration, s3 is an error today and stays one (bare, E). So the hole opens exactly for the form the recommended
    reading introduces.
  - The same holds for an in-body kept-step store: its carrier, o, is no holder.
  - A `via self` pointee that holds `uniq/…` references gives no guard, so W never steps from the keeper to their referents'
    holders.

  `MarkBox`, `KeptBox`, `Buffer` and `OwnedRemainder` avoid the hole only because a marker or a Type argument makes them holders
  anyway.
- **Evidence.** s3 (error today through the raw clause); X §5.1 and §7.1; plan §4.2.
- **Correction.** The guard walk walks a `via self` kept Field's pointee as the keeper's own storage: Loan fact and guard, in
  the current place. It decides this per Field from `Keeping`'s Kept Fields, so no memo-order hazard returns. `via loan`
  pointees are not walked; they belong to the owner, which the marker reaches. Under K′ nothing changes, because kept pointees are
  Type arguments, already walked as components. Add `OwnRefs` (declared, valid with `o` dead, error when `o.get()` is used later)
  and a `via self` pointee holding `uniq/(ref/i32)` to §7. Include holder eligibility in Q1's minimal core (see N2).

## Non-blocking findings

- **N1. K′ column versus §4.1.** §4.1 defines K′'s kept Fields as "D's raw Fields" only. `TupBox.p: (raw/T, isize)` and `Outer`'s
  `Wrap<raw/V>` are not raw Fields, so as specified K′ makes them bare: `tupKeptDead` and `outerDead` are errors. §1.2's K′ column
  says "valid". Either fix the column, or define K′'s kept layers as round 1 did (route Own, any inline path, composed through
  nested instances). The owner compares readings by this table.
- **N2. Q1's scope per option.**
  - "X §5 whole" includes Binding's typed route proof ("prove a typed storage route from U to T in the published graph", needed
    for `BufferView`'s `via loan` over `Buffer<T>`). That is part of the storage graph §6 does not adopt.
  - It also includes `via self` content in the slot properties (variance, Loan requirements, OwnedOrigins, Phantom), which is a
    TyCtxt change.
  - G160-2's "+150" fits only the minimal core. The minimal core's three bullets omit holder eligibility (R2-B2) and the slot
    properties: add them or say why not.
  - State each option's Binding scope in Q1 and §8.
- **N3. G160-2's process and records.** It adds grammar, an `AdtDef` publication and diagnostic codes: new vocabulary and a new
  representation, which need the owner's approval and a review weight per AGENTS. The plan designs only A5, so G160-2 needs its
  own design and review row. Also:
  - LIBRARY: `UniqSlice` is public, and X makes the descriptor public contract.
  - D5: say the old paths ignore the erased declaration and need no code.
- **N4. Keeping's DFS crosses references.** §4.2 says the Unowned walk crosses every reference layer. §4.1 should say the same of
  Keeping's DFS, which `H {u: uniq/RawBox}` needs.
- **N5. Issued at a kept step.** Only the value is given. Say what the parents are (today parents ∪ {Φ_E}; presumably parents ∪
  layer ∪ {Φ_E}).
- **N6. A verdict class changes.** An in-body store through an exclusive `via loan` Field now reaches its owner in W's second
  pass, so the view is held (P13). Today it reaches E and gives a Language error. No existing program has the form, but list it in
  §1.2 or §4.4.
- **N7. Projections.** s2 shows that Binding does not resolve an instance's projection-typed Field for indexing
  (`NotIndexable_Kd`), so no callee can write through a projection that resolves to a raw pointer. Cite this as the evidence for
  "classified at the declaration".
- **N8. Records.**
  - The INTEGRATED entry cites only `review-1.md`; add round 2 at commit.
  - §1.2's list of undeclared Types omits `RawBox`.
  - §11 should record round 2.

## Checks with no finding

- **X-decl verdicts.**
  - `cursorWrite`, `keptDead`, `TupBox` and `Outer` are errors, and `bufferDead` and the library iterations are valid with their
    declarations.
  - `keptDirectDead` is valid with `via self`; `keptInitDead` stays an error by extraction.
  - The costs (grammar, library annotations, undeclared boxes become E) are stated fairly.
- **The kept-step Store seeded W.** It matches a call for an inline prefix, past `ref` (a raw write past a shared layer within
  authority reaches the keeper) and past `uniq`. `refs` of the borrow layer gives E for parameter-rooted keepers, and the store is
  sound.
- **The Unowned walk on body-supplied Type arguments.** A Type argument under a bare raw layer inside a Field is covered:
  - by `BareParams`, for `D<T> {p: raw/T}`, `Wrap<raw/T>` and `S<T> {u: uniq/(raw/T)}`;
  - by the walk itself, which crosses every Type argument, for raw layers in arguments.
- **Unit order.** G160-0 (SPEC first), then G160-1 (behavior-preserving, `Raw` keeps its name), G160-2 (X-decl syntax; no A5
  change), and G160-3. This order is right.
- **Composition.** P13, 2e-D, 2c and 2f are unchanged.
- **PLAN row and INTEGRATED status.** The row states facts. The INTEGRATED status (一部取り込み, nothing integrated or frozen)
  follows the 2026-10-21 convention.

## Verdict

**Reject (blocking findings R2-B1 and R2-B2).**

- Round 1's five findings are resolved.
- The rewrite opens two soundness holes:
  - dropping `Concrete` regresses s1's three rejections, or else gives false E;
  - the recommended `via self` form does not make its owner a holder (s3's shape).

Both corrections are local to `Keeping` and the guard walk: restore `Concrete`, and walk `via self` pointees as the keeper's own
storage. The rewrite should also address N1 and N2, because the owner decides Q1 from §1.2 and §1.3.
