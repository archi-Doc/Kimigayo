# Published Copy Verdicts Plan (R6-check Q8-3b)

Status: 一部取り込み (open): a working plan for R6-check Q8-3b, scheduled after R9 at the owner's request; no section is integrated into SPEC
or IMPL, no text is frozen; not started; adoption establishes no support.
Request: 2026-11-03. The owner decided Q8-3's cost, verbatim 「Binding の判定結果を公開して、検証器はそれを読むだけにする」, then, after the
design found the work larger than one unit, verbatim 「R9後でもいい？そしたら、draft/Proposalに計画書を書いて、今後の予定としてください。」
Owner of the plan: the front session (Syntax and Binding; DECISIONS R7-M2). Source: `claude/r6-flip` 945ff363c (dev base 3c1345d44); the
owner's choice on dev 51ca01d42 (PLAN G385). Evidence: `artifacts/verify/r6check-q8-3b/` (design, coverage probe and its hook points, cost
measurement, harness, variant patches).

## 0. 要約（日本語）

- R6-check の Q8-3 で、MIR の検証器は「`Copy` の operand の型が Copy であること」を確かめるようになった（DECISIONS BU-88）。いまは
  `TyCtxt.CopyFacts` が定義ごとに Copy を証明し直して覚える。結果は正しく、check 全体の遅れはウォームで census（77 プログラム）+4%、
  初回は変化なし。検証だけを見ると +22% で、その大半は Binding がすでに下した判定の証明し直しである。
- オーナーの判断は「Binding の判定結果を公開して、検証器はそれを読むだけにする」。設計の結果、Binding の判定は検証器の問いの 64% しか
  覆わず（判定はすべて一致）、残りには新しい公開事実が要る：型の外形だけで決まる Copy（閉じたスカラーなど、`raw/T`、`Slice<T>`）、
  Binding が値読み出しを決める場所での判定、破棄の判定が尋ねる型の事実、コード生成が尋ねる単相化済みの型の事実。
- R9 の後、その時点の基準で調査をやり直す U0 から始め、その後に単位が 4 つ（各段階 1 時間以内、Q12）。R7 の証明器のメモ（R7-C2/C6）と
  同じ判定を 2 つの表に持たないよう、U0 で表現を決める。それまで Q8-3 は 945ff363c の形で R6-check のグループに入る。

## 1. Position

- Q8-3 (Q8 §3.9; DECISIONS BU-88) is on `claude/r6-flip` b630c8d41, 62a52302b, 945ff363c, inside G:R6-check-flip (one group review at H5).
  R4 and R5 consented to its code. The rule: `Checker.Operand` (`Mir/MirValidator.Rvalues.cs`) flags a `Copy` operand of a known Type that is
  not Copy under its body definition's premises (`MirViolation.Operation`); the case-dependent sites (`caseSites`) are exempt.
- `TyCtxt.CopyFacts` (`Binding/TyCtxt.Effects.cs`, a TyCtxt view cleared in `ResetViews`, R5-M61) holds, per definition-use node, the
  premises resolved once (`EffectPremises`) and each interned Type's verdict, proven on first use. Its readers: the validator,
  `MirEffects.Solve.cs`, `MirTypes.Destruction.cs`, `ModuleCodegen.Objects.cs`.
- Cost (`artifacts/verify/r6check-q8-3b/q8-3-cost.md`; A = e8e29e60e, B = 945ff363c; pinned, interleaved, one folder):

| Workload | Check, warm | Check, cold (tiering on) | Validation, warm (with proofs) | Validation, lookups only |
| --- | --- | --- | --- | --- |
| Census (77 programs) | 1.039–1.043 | 0.963 | 1.22 | 1.045–1.053 |
| Milestones (44) | 0.990 (median 1.023) | 1.009 | 1.21 | 1.05 |
| Milestone26 | 1.00–1.02 | 0.999 | 1.20 | 1.04 |
| Hello | about 1.03 (+0.5 ms) | 0.998 | 1.21 | 1.03 |

## 2. Required meaning

- Q8 §3.9: the validator reads Copy "from Binding's published proof under the body definition's premises; no new proof". SPEC 3.5.2 gives
  the Copy table; Binding is the judge of Copy and of TransferRequired (R6-check H0; DECISIONS BU-84).
- AGENTS: each IR has one validator, owned by its producer; consumers trust validated IR; a broken contract is `InternalInvariant_Kd`
  without recovery; no try-then-fallback chain. So the validator, MirEffects, MirTypes.Destruction and code generation read published
  verdicts and prove nothing, and an absent verdict is a producer's broken contract.
- Out of scope: `TyCtxt.Sealed` proves on demand, without a memo, in the same readers (`MirEffects.Solve.cs:326`,
  `MirTypes.Destruction.cs:183`, `DropGlue.cs:123`); it is its own open issue (PLAN G406's second item).

## 3. Evidence (the coverage probe, 945ff363c, census, check path; `artifacts/verify/r6check-q8-3b/`)

- 31,238 distinct Copy questions keyed by (definition, Type): 31,226 the validator's, 12 MirTypes.Destruction's. For 20,083 (64.3%), Binding decided Copy for the same Type under some
  `DeclarationKoto` ancestor of the decision's node, with the same verdict; the probe credited a decision to every ancestor, not to the one
  key a publisher writes (§4.1), so this overstates what a per-definition publisher covers. Publishing changes no verdict.
- 11,155 (35.7%) have no such decision:

| Group | Count | Proposed owner |
| --- | --- | --- |
| (a) Closed Types the builder makes and copies itself: `bool` tests (6,266), `isize` positions and discriminants (1,833), closed `raw/T` (929), other scalars (419: u64 231, i64 155, i32 31, u8 1, f32 1) | 9,447 | the form fact (§4.3) |
| (b) Copy by outer form for every `T`: `raw/T` with an abstract `T` (924), `Slice<T>` (309), one closed Slice | 1,234 | the form fact (§4.3) |
| (c1) A Type parameter read in library conformance blocks: `RangeIterator<S>.starting(self.start, self.end)` and `ClosedRangeIterator<S>.starting` under `when S is PrimitiveInteger` (`Core.kimi:223–248, 297–322`; 3 × 154 = 462), and one more unnamed site; decided as a ReferentRead adaptation without a ProveCopy | 463 | Binding's ReferentRead verdict (§4.4) |
| (c2) MirTypes.Destruction's Drop questions on `T` (10) and one TargetProjection | 11 | destruction facts (§4.5) |
| (d) Code generation: `Copies(null).Copy(payload)` on monomorphized payloads (`ModuleCodegen.Objects.cs:86`) | not measured: the probe's path generates nothing | §4.6 |

- Probe hook points (the recorder has none and the instrumentation was reverted): `Record` in `Binding.ProveCopy(type, Koto)` during Bind;
  `Question` on a miss in `CopyFacts.Copy`, with `Reader` set by each reader (`q8-3b-probe.cs`; `q8-3-ab/patches/probe.diff` holds the cost probe's hooks instead).

## 4. Design

1. **Publisher, key and scope.** While its pass runs (`IsRunning`; not `SemanticQuery.ProveCopy`, not the old OwnershipAnalysis callers),
   Binding publishes each Copy decision into the enclosing definition's table, keyed by (DefinitionUse node, interned Type), the key the
   readers use. A decision is published under the key only when it was proven under the definition's premises (the scope `EffectPremises`
   returns: `ConstraintScope(DefinitionUse).ImplementationPremises ?? scope`). A decision under another scope (a match arm's pattern scope,
   SPEC 14.8.2; a property-witness bridge, SPEC 11.9.2; an override whose ImplementationPremises differ) is not published; for that decision's Type,
   Binding proves it under `EffectPremises` when it publishes, and U0 prices those proofs. A second write with a different
   verdict is the publisher's `InternalInvariant_Kd`.
2. **Lifetime.** The table's lifetime is the Bind pass that writes it: `ResetPass` clears it, pooled so that a warm pass allocates 0 B. It is
   then no longer a TyCtxt view memo, so TyCtxt's view contract (`TyCtxt.cs:234–236`) and R5-M61's view list change. (Keeping it across
   passes would be safe only if every pass, Provisional passes and reused library bindings included, republished every key MIR reads with
   identical verdicts.)
3. **Copy by form.** One flag on the interned Type, computed at Intern (RP1), for the SPEC 3.5.2 rows fixed by the outer form, independent of
   premises: the scalars, Unit, Never, `ref/T`, `objref/T`, `raw/T`, `Slice<T>{s}`, function items, the third row's Types (FromEnd<T>,
   Start, End, Range, ClosedRange, ResolvedRange) or a stated reason why the solver keeps them, and the Refuted rows `TryLeafCapability`
   decides (Array, Dictionary, FixedBuffer, WriteWindow, Utf8Writer, SPEC 3.5.2's "No" rows). U1 states whether closed `Wrapping<T>` is
   among the scalars. `ProveCopy`'s leaf reads the same flag, so the rule exists once; it reads the Scalar predicate that R7-T2 moves into
   TyCtxt (`ScalarTypes`, ARCH row "MIR builder"). The builder's own Copy choices (tests, positions, discriminants, Selections' Move-to-Copy)
   already rest on `ScalarTypes` and so on the flag; U1 says whether the builder changes at all.
4. **ReferentRead's verdict (c1).** Where Binding decides a ReferentRead adaptation, it publishes the verdict obtained by `ProveCopy` under the
   definition's premises at that site (or SPEC 3.5.3's "read Types are Copy" read from the one flag): never a second Copy rule.
5. **Destruction facts (c2).** Binding publishes the Copy (no-drop) verdict for each Type MIR drops, or the destruction query reads a published
   destruction fact; U3a's design picks one owner.
6. **Code generation (d).** The owner of closed instantiated Types' Copy is either the instance's published facts (`TyCtxt.Instance`, after
   R7-T1) or a premise-free closed-Type verdict Binding publishes when the instance is formed. The form fact covers only the Types whose Copy their outer form fixes; a user
   `Self is Copy` struct or `Wrapper<Point>` under `Self is Copy when T is Copy` needs this owner. U3b's design picks one (D+R).
7. **Readers.** From U2 the validator reads a published-only accessor, and an absent verdict is `InternalInvariant_Kd`. The on-demand proof
   stays for the other readers as a temporary path with its removal condition (U3b) until U3a and U3b move them; CopyFacts' proof is then
   deleted.
8. **Expected effect.** Validation B/A toward the lookups-only figure (1.045–1.053 on the census). The whole check: Hello and Milestone26 about
   1.01–1.02; the census about 1.03, since its remaining +70–80 ms lies outside validation and is unattributed (U3b measures it). Bind adds
   one insert per distinct published decision (H: the count of Binding's distinct decisions was not printed; U0 counts it); warm 0 B.

## 5. Units (each design and implementation phase at most one hour, Q12; one review per unit; a test author per RP7 for a Full core)

| Unit | Content | Class, review | Checks |
| --- | --- | --- | --- |
| U0 | On the post-R9 base: re-run the coverage probe on the check path **and on a generating path** (the milestones with generation, the native families); record each decision's scope and its innermost definition key; count Binding's distinct decisions; check R7-C2's Prove memo and R7-T2's `ScalarTypes`; define A and A′ on that base; decide §6's representation question | evidence only, no commit; the planning session decides | stop if a new group appears, a reader cannot be served without a proof, or two tables of one verdict would remain |
| U1 | The form flag at Intern; `ProveCopy`'s leaf reads it (behavior-preserving) | Major, R | the Copy and Binding classes, MirValidatorTest; snapshot 0; warm 0 B; Bind B/A′ within the RP8 line |
| U2 | Binding publishes its Copy decisions (with §4.1's scope rule) and the ReferentRead verdicts, per pass (§4.2); the validator reads a published-only accessor | Full core (FP1 under RQ6, a test author per RP7), or Owner if U1's commit records this plan's §4 in DECISIONS as the reviewed design | U1's checks plus MirBuildTest; warm rebind, and edit then rebind, validate every body with no `InternalInvariant_Kd`; a `Purpose=Allocation` 0 B row; validation A/B |
| U3a | Destruction facts; MirEffects and MirTypes.Destruction read only | Major, D+R (the options differ in owning component) | U2's checks plus MirEffectsTest |
| U3b | Code generation's payload verdict (§4.6); CopyFacts' proof deleted; an absent verdict is `InternalInvariant_Kd`; RP6's ArchitectureRulesTest row: zero references to `ProveCopy` from `TyCtxt.Effects`, `Mir/**` and `Backend/**` | Major, D+R | U3a's checks plus the native families codegen touches; the whole-check A/B (§1's workloads) |

- U2 lands alone, in the interim state of §4.7; U3a and U3b may share a window. Each unit is queued for a window as a front unit (R7-M2; the windows are the middle session's). After R9 the instruments are RP14's
  post-H10 set: the generated gate, the A5 class set and `MirCensusTest` (the mir ledger ends at H10).
- Board holds (RP17; R7 plan §9): the `Mir/**` files (the middle reviews) `Backend/Codegen/ModuleCodegen.Objects.cs` (the back reviews) and `ArchitectureRulesTest` (Core and shared; U3b's RP6 row).
- DECISIONS BU-88 gets a dated amendment per unit, naming the reason that no longer holds (the on-demand proof) where it departs.

## 6. Schedule and dependencies

- **After R9** (the owner). Until then Q8-3 stays as on 945ff363c, inside G:R6-check-flip; this plan changes nothing on the branch.
- R7, R8 and R9 may move the code this plan names (TyCtxt views, ProveCopy's callers, the library's conformance blocks, the builder). U0
  therefore re-runs the probe and re-reads the owners before any code; the plan's rule (one published verdict, read only) stays, the site
  names may change.
- R7-C2 (b) and C6 route Copy through the solver's `Prove(goal, env)` memo. U0 decides whether the published verdict is that memo's entry
  under the definition's env, with CopyFacts as its read-only face or deleted; it measures whether the memo alone brings validation near the
  lookups-only figure; and it stops if the plan would keep two tables of one verdict. An R7 unit that changes Copy's judgment, TyCtxt's views
  or `Intern` records the effect on this plan here (front session).

## 7. Risks and stop conditions

- A group the probe did not see (a new form after R7–R9, or a generating path's question outside (d)): U0 stops and reports.
- A published verdict that differs from today's proof: a stop (Q13: a verdict move), with the form.
- A reader that cannot be served by a published fact without a proof: a stop; never a fallback proof.
- RP8: a unit stops for the owner when Bind with publication is B/A′ > 1.03 outside the A′/A spread, or when warm allocation grows. Each unit
  records cold allocation and retained memory (the published tables are retained per definition and pass).

## 8. Records

- DECISIONS: BU-88 amendments per unit (publisher, key and scope rule, lifetime, form flag, ReferentRead verdict, destruction facts, codegen's
  owner, deletions).
- COMPILER_ARCHITECTURE: the "MIR validation" row, TyCtxt's view row (§4.2) and the ScalarTypes row when their contracts change.
- INTEGRATED: a dated entry in the commit that adds this file: 一部取り込み (open), a working plan, the owner's two verbatim answers, no SPEC or
  IMPL mapping, PLAN G406.
- PLAN: G406 records this plan until U3b lands (and `TyCtxt.Sealed`'s on-demand proof as its second item).
