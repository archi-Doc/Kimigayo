# R7, R8 and R9: Implementation Plan

Date: 2026-11-03

Status: Adopted working plan. On 2026-11-03 the owner approved RQ1–RQ17 as recommended (verbatim 「RQ1〜RQ17 を推奨どおりで承認します
他のエージェントに確認し、問題なければコミットしてください。」) and the R4, R5 and BU sessions checked it before the commit (§13). Its decisions
are DECISIONS R7-M1–R7-M3, its position PLAN §3 and COMPILER_ARCHITECTURE's ledger, and it is registered in `draft/INTEGRATED.md` as a
working plan, as the R5 and R6 plan was on 2026-10-18 (一部取り込み (open): no section is integrated into SPEC or IMPL, and no text is
frozen; its SPEC revisions register separately when they land). Written by a planning session on the owner's request of 2026-11-03 (a
plan for R7 Binding consolidation, R8 library algorithms in Kimigayo, R9 the front-end remainder, the moves and the final documents,
under a three-session arrangement by component). It changes no SPEC, compiler or test.

Base: `dev` at `bc68257fb` (R4, R5 and BU paused by the owner on 2026-11-03, nothing in flight). Counts are measured at
that base unless marked (H), an estimate or an inference not checked by running code.

---

## 0. 要約と判断事項（日本語）

**位置。** R4（MIR 解析、影）は出口前、R5 と R6-gen は完了、R6-check（受理の切り替えと旧 Analysis の削除）は R4 の出口待ち。
D5 の凍結は R6-check の完了まで続く。R7・R8・R9 の範囲は段階表のとおりだが、A5 と R5 が読む Binding の事実はすでに R7 の形で公開
済みで R7 の範囲外（R4-M35 OD2）。一方、R5/R6 期に R7 へ先送りされた項目（Koto 転送プロパティとその約 1,150 の読み、合成 Koto、
HIR publisher の歩き、`StorageSubst`、要件の materializer、G149 の EnclosingInstance、G128/PD30（R5・R6 計画）の StorageNesting、`RequirementImplementation`
など）と、BU の未解決行の大半（G170 C3、G302、G172、G335、G383、G375、G155、G317…）が R7 の部品に集まっている。

**設計の要点（根本解決・重複削減・低アロケーション）。** R7 は Binding を 8 つの部品に組み直す：ドライバー（宣言クエリで順序を決める）、
名前解決（`ResolvePath` 1 本）、宣言表（署名の判定を宣言が 1 回公開する）、`InferCtxt`（変数と undo ログ 1 組）、`ContractSolver`（`Prove` 1 本と
メモ、同一性の判定 1 つ）、CallPlan パイプライン（12 形態を 1 本）、Problem 記録（`BindingFailure` と文面の switch を廃し、独立した誤りを
全部残す）、Origin の残り（本体内の関係判定を A5 に譲る）。各部品は「公開された事実 1 つ＋旧経路の参照ゼロのラチェット」で閉じ、
収束しなかった過去の原因（持ち主が 2 つある規則、文字列検索による漏れ、設計文面での選択、記録選択への完璧主義、1 時間を超える単位）を
手順で塞ぐ（§8）。R8 は Array → Dictionary → 文字列と組み込み witness → Text/Buffer/Window/Writer → 補間アダプターの順に Kimigayo へ移し、
`LegacyRuntimeHelpers`（約 1,850 行）、`.ll.in` の約 1,100 行、`CompilerFunctionKind`（76）、`KimiDeclarationId` の大半、`Layout` のハンドル行を
消し、`IntrinsicId` を 28 にする。R9 は構文の残り（項目ループ、型文法の入口、属性文脈）、Documentation と ツールチェーンの切り出し、
IMPL §20.8.3 のマニフェスト、最終文書を行う。

**体制。** 前段（構文・Binding）が R7 の本線、中段（HIR・MIR 解析）が切り替え後の解析欠陥・R9・R7 の HIR 側と、dev へのまとめ役、後段
（コード生成・ライブラリ）が R8 の本線。dev への取り込みは 1 日 2〜3 回の window にまとめ、中段が merge と重い検証を行う。調整は
掲示板 1 ファイル（§9）。レビューは部品の持ち主のセッションが行い、書いた者はレビューしない。上限は実装者 2・設計/レビュー 2
（セッションごと）、重い実行 3（全体）。

**出口の計測。** R7：構造の行（Koto キーの表 0、転送プロパティ 0、合成 Koto 0、`BindingFailure` 0、`[Transitional("R7")]` 0、能力行
EnclosingInstance/StorageNesting 消滅）と、Bind の cold/warm 時間・割り当て・保持メモリが開始時以下、規模試験が線形、受理の移動が
名前付きの一覧だけ。R8：`IntrinsicId` 28、`[Transitional("R8")]` 0、O2 コーパス全カーネルが揺れの範囲、割り当て回数不変、フィクスチャ
一致、Bind の増分が閾値内（超えればライブラリの共有 L1）。R9：移設が挙動不変、構造上限の免除がなく、依存規則と文書が現状を記す。

**オーナーの判断事項**（英文の詳細は §2.2 と §11）。「推奨どおり」で一括回答できる形にしてある。

| 番号 | 問い | 選択肢 | 推奨 | いつ |
| --- | --- | --- | --- | --- |
| RQ1 | 体制の採用：部品で分けた 3 セッション、中段がまとめ役、1 日 2〜3 回の window、掲示板、持ち主レビュー、上限 | (a) 採用 (b) 段階別のセッションのまま | (a) | 今 |
| RQ2 | 本計画の採用：出口条件（§10）、RP1〜RP21、§12 の記録。R7 の出口は構造の行と計測で測り、38,000 行は使わない | (a) 採用 (b) R6-check 完了後に改訂してから採用 | (a) | 今 |
| RQ3 | R7 の開始 | (a) 設計・調査・試作・挙動不変の単位は今から、受理を変える単位は H5 の後 (b) R6-check 完了後 | (a) | 今 |
| RQ4 | R8 の開始 | (a) L0 と、旧 Analysis が受理する本体のライブラリ／Backend 単位は今から (b) R7 の後 | (a) | 今 |
| RQ5 | P42 の残りと R7 の順序 | (a) P42 の設計パッケージは中段・後段で先行し、Binding 部分は R7-N1・V2 の後の window に載せる (b) R7 の前に P42 (c) R7 の後に P42 | (a) | 今 |
| RQ6 | Full 単位の候補数 | (a) §5.5 のとおり（C2・C3・I2・P3・W2・O2 は 2 候補、他は試作先行の 1 候補で、単位ごとの例外として DECISIONS に記録） (b) すべて 2 候補 (c) すべて 1 候補 | (a) | 今 |
| RQ7 | 掲示板を hold の記録にする（PLAN §3 の hold 行をやめる、ARCH の規約改訂） | (a) 採用 (b) PLAN §3 の hold 行を続ける | (a) | 今 |
| RQ8 | 単位ごとのコスト停止規則：Bind（公開込み）B/A′ > 1.03 かつ A′/A の幅の外、または warm 割り当て増で停止；後段は O2 が揺れの外か割り当て回数の変化で停止 | (a) 全セッションに適用 (b) 揺れの幅だけで判定 | (a) | 今 |
| RQ9 | 語彙の目標：`BindingFailure` 廃止（要件は閉じた列挙）、`IntrinsicId` 28、`CompilerFunctionKind` 廃止、`KimiDeclarationId` は lang item 約 50 | (a) 承認 (b) `BindingFailure` を要件列挙として残し、文面の switch だけ廃止 | (a) | 語彙の単位の前 |
| RQ10 | SPEC 改訂の一覧（§5 の SP 行）と手順：着地前に本文をオーナーへ、SPECJ は同じ単位、INTEGRATED に 1 行 | (a) 承認 (b) 実装後に改訂 | (a) | SPEC の単位の前 |
| RQ11 | 表現 struct（`ArrayRepr` など、`#Layout("C")` の内部 lang item）と lang 属性（識別であって規則ではない）の文言 | (a) 確認（PLAN10 §5.8・§16-9 のとおり） (b) `Layout` のハンドル行を残し表現 struct を作らない | (a) | R8-R1 の前 |
| RQ12 | G128 の暫定 `Unsupported_Kd` を IMPL 21.3.2 の Resource 記録へ（`Apply` を純関数にした後） | (a) 承認 (b) P42 まで暫定を維持 | (a) | R7-T1 の前 |
| RQ13 | ライブラリのプロセス内共有（G337 O3・O2） | (a) 計測で決める：Hello の cold Bind が R8 開始比 +10% 以上か、試作の節約が小さいビルドの 20% 以上なら採用 (b) 採用しない (c) 無条件に採用 | (a) | R8-L1 |
| RQ14 | §8.4.4 継承適合の OCC の改訂時期 | (a) R7・R8 は Unsupported のまま、R9 の出口で提案 (b) R7 で改訂 | (a) | 後 |
| RQ15 | 診断区分・相の名前の改名（§16-7） | (a) 改名しない (b) R9 で改名 | (a) | 後 |
| RQ16 | `formatInteger128` の撤去段階 | (a) 計測で R9 かその後 (b) R8 で撤去（i128 の書式化を Kimigayo で） | (a) | R8-I1 |
| RQ17 | マイルストーン作業の再開点（P42 は RQ5） | (a) R7 の出口の後、前段のレーンで P38 → P39 → P41 → OCC-X → P43 (b) R7 の間に交互 | (a) | 後 |

---

## 1. Position and inputs

### 1.1 What the three stages are now

| Stage | ARCH ledger scope | What moved in or out since PLAN10 |
| --- | --- | --- |
| R7 | Binding consolidation: resolver, inference context, Contract solver (premise supply already `Binding.ContractSolver`, BU-29), CallPlan pipeline, Problem records | Out: every Binding fact A5 and R5 read, published in R7's target shape (R4-M35 OD2): `TyCtxt.OriginRows`, `Retains`, `RawStep`, `ArgumentKinds`, `ComponentSummaries`, `SourceStates`, `VTableDef`, `Witness`, `Comparison`, `Instantiates`, `Erase`. In: Binding's reads of the 36 Koto forwarding properties and the properties (R5-M9; about 1,150 reads: `.BoundSymbol` 475, `.BoundType` 408, `.BindingState` 204, `.ConversionBinding` 37, `.BoundCall` 22), the synthetic Kotos (R5-M9; 13 Binding files), the HIR publisher walk (R2b-D2, BU-81, R4-M142), `TyCtxt.StorageSubst` (R5-M36), `InstantiateRequirementCall` and `RequirementImplementation` (the R5 and R6 plan's Q8, G186), the legacy comparison composition (R5-M68), `EnclosingInstance` (G149, R5-M43, R5-M146, R5-M157), `StorageNesting` (G128, the R5 and R6 plan's PD30, BU-12), G157 (5) and (16), R1e, `MirDefKey` by declaration (R3-6), the three declaration-table views still waiting for readers (R2b-D3: `ImplTable`, `EffectBoundTable`, `AvailableBounds`; `VTableDef` and `UniversalRelations` exist), case enumeration in `PairBinder`, the Koto-keyed tables (ARCH code rules: 107 sites in 31 Binding files), the root helpers' Binding reads (PLAN10 §9: `ReferenceTypes` 29 files, `ElementAccess` 17, `ScalarTypes` 14, `ComparisonTypes` 5, `FormattingTypes` 4, `DefaultParameters` 2, `SlotTypes`, `SharedReadTypes`, `StaticScalar`, `StrictBestCandidate`), and BU's open families (§5.1's mapping) |
| R8 | Library algorithms in Kimigayo, about 28 intrinsics | In: `LegacyRuntimeHelpers` (1,850 lines; 20 of the 29 `[Transitional("R8")]` declarations at the base, 28 in all at R6-gen's record), `Layout`'s handle rows (R5-M19), `MonoKind.LegacyRow` (R5-M121), `KimiLibrary.DictionaryStorage` and `TyCtxt.DictionaryStorage` (R5-M28, R5-M124), the `BodylessWitness` capability row (R5-M146), `BuiltinForm.Capacity` and the four temporary intrinsics (R3-9, R3-10, R5-M137: `IntrinsicId` 32 of 34, 28 after R8), `FormatKind`/`WriterKind`/`FormatBound`/`FormatEstimate` (R5-M128, R5-M130, R5-M131), G150, G157 (2) and (31), G337 O2/O3 (after R7, the owner's 「(b) 先送り」), §16-8 |
| R9 | Remaining front-end cleanup, Documentation and toolchain moved out, final documentation | In: the toolchain, publication and profile files in `C/Emission` (1,364 lines; `WindowsProfile`'s target half already in Core, R5-M174), their structure-limit exemption (ARCH), IMPL §20.8.3 (`NativeToolchain.ReadExternalDeclarations` re-parses IR text), G345, PLAN10 §9's front-end rows (item loops, type grammar entries, the attribute context, keyword sets, `CompilerHelper`), the final records |

### 1.2 Sizes at the base (lines)

| Area | Lines | Files | Note |
| --- | ---: | ---: | --- |
| Binding | 77,274 | 270 | By concern (this plan's grouping, `artifacts/verify/r7-d0/` re-measures): calls, inference and typing 23,227; Origins and retention 13,817; Contracts and capabilities 12,039; Types, `TyCtxt`, `AdtDef` 10,034; driver and resolver 6,349; diagnostics 3,680; places and HIR 3,199; effects 2,635; library catalog 1,946 |
| Old Analysis | 16,020 | 50 | Deleted at R6-check (H10) |
| Mir (model, build, validator, analyses) | 18,627 | 93 | The middle session's area |
| Backend (root, Codegen, Instances) | 14,837 | 76 | Codegen 7,167, root 5,351, Instances 2,319; plus 2,335 lines of `.ll.in` |
| Parsing and Lexing | 20,429 | — | `Parser.cs` 4,832 lines, over the token limit (25,781 tokens at R6-gen's record) |
| Documentation | 4,388 | 12 | Moves at R9 |
| Emission (toolchain, publication) | 1,364 | 8 | Moves at R9 |
| Kimi library (`.kimi`) | 2,205 | 31 | Grows at R8 (PLAN10: about 3,200) |

### 1.3 Start conditions and the freeze

- R4 exited on 2026-11-03 under the owner's 90 % rule (DECISIONS R4-M165; dev `db7f80af7`); its exceptions and the post-exit fix list are
  PLAN G396 (A5-F7's last units 2c-6 and 2c-7, A5-F8, A5-F10, A6's 2g units with G105, G103, G106, G124, G126, G147 (2), G160's remainder,
  G263, G216 (a)'s unjudged forms G388–G392 and U3 as a proposed common fix, G216 (b), G215, the ledger's per-row closure X-1a, the 25
  `Unsupported_Kd` producers without a removal stage, X-5's N1–N11). R6-check is in flight under H0 (R5-M182): Q19's second reading, IC,
  H3-1 and H4 are done (R4's window r4w12, dev `d57b32ce2`), H13 is confirmed (its two Minor builder fixes wait for a back window), G157 (1)
  is done (R5-M183), H3-2 is the back's (U0 landed with `Core/Compilation.Publication.cs`; U1a–U3 follow; it does not wait for 2g-1), the
  flip group on `claude/r6-flip` holds H1b, H1 (BU-84), H1-tests and Q8-3 (BU-88; its cost an open owner question) and takes H5's Binding
  and Core hunks, merged once after the group review; then H3b's classification and the flip group's window (the back's, as H0 assigns
  them), H6–H15. PLAN10 §12.2 starts R7 after R6 and allows R8's library-only work after R6; the owner decided the start timing here
  (RQ3, RQ4).
- D5 holds until R6-check completes: the old Analysis decides acceptance on the default pipeline. So until H5 a Binding change may
  not make a valid program newly accepted on the default pipeline, and a Kimigayo library body must be accepted by the old
  Analysis. Record-only changes (dependent records, independent records on rejected programs; DECISIONS Q13) and behavior-preserving
  restructurings are allowed any time; deletions of what the old Analysis or Parsing still read wait for H10 and H14 (RP4, RP5).
- The shared-file hold protocol (R5-M7, R5-M148, R5-M167) continues until R4's exit; this plan moves the live hold record to the board
  (RP10, RQ7).

---

## 2. Decisions

### 2.1 Planning-session decisions (PD)

Each is recorded in DECISIONS by the commit that first implements it (AGENTS "Record decisions"); RP8, RP10, RP16, RP18 and RP20 need the owner (RQ8, RQ7, RQ2, RQ9), and RP21 (§5.0, the Full-core rule) is part of RQ6.

| ID | Decision | Reason | Deletes or avoids |
| --- | --- | --- | --- |
| RP1 | `BoundType` stays the Type term through R7; `TyCtxt` keeps interning and the flags computed at construction (Height, ContainsParameter and the like arrive incrementally); R7 unifies the walks (`Relate`, the memo) and removes the remaining mutable caches, but builds no new Type representation | No measured need; a representation swap under every A5 and R5 fact would be a second migration | A second Type term beside `BoundType`; a `Subst` conversion layer |
| RP2 | `Binding.ArgumentKinds` (BU-2, BU-74, BU-79) stays the owner of SPEC 10.5.1's kinds, the fit and evidence walks and rule W; `InferCtxt` is the one variable store (Type, length, literal and Origin variables, union-find, undo log, snapshot and rollback); `SlotSolution` becomes a view over it, `ResultPropagation`'s join states become constraints with one published `Awaiting` fact per join (G335 U1) | Two solve states (SlotSolution's arrays and ResultPropagation's holders) are the cause of G335, G383, G390 and G391; the kinds are SPEC's and already canonical | A second owner of 10.5.1's kinds; the waited-join state (`SourcesAwait`, `HoldsWaitedResult`, `BindWaitedSources`) |
| RP3 | Problem records land beside `BindingFailure` with a one-way mapping (snapshot 0), migrate by requirement family, and `BindingFailure`, the code switches and the consultation frames are deleted after H10; the record shares its typed facts with `MirFact` and converts to Core's `Problem(Code, SourceRef)` at publication (E1 decides one type or two over one fact vocabulary) | The old Analysis reads Binding's failure causes until H10 (D5); a family at a time keeps each unit within an hour; three problem records would be three owners of one meaning | A flag-day change of 464 `Fail(` sites; a third record shape |
| RP4 | A deletion of what the old Analysis or Parsing still reads (the Koto forwarding properties, the synthetic Kotos, the root helpers, `BindingFailure`) lands after H10 and H14; Binding's own reads move before that, behavior-preserving, under an ArchitectureRulesTest count that falls to 0 by R7's exit. R6-check's own deletions are not R7's: H1 on `claude/r6-flip` deletes the old analysis's TransferRequired sites with the back's consent and lands with the flip group at H5 | D5 and the hold protocol; the reads are the cost, the declarations are a day's deletion | Double edits of frozen files |
| RP5 | An R7 unit that changes acceptance on the default pipeline (T1–T4, the valid forms G179, G164, G203, G331 and O2 recover, local slot regions) lands after H5; until H5 R7 lands behavior-preserving units and record-only changes within Q13's non-regression classes. RP5 binds R7's units only: R6-check's and the post-exit units land their named acceptance moves on dev before H5 as their own records say ((b2)'s b5, c2 and a2e; G180's moves) | The old Analysis would accept nothing new (D5) and the ledger would carry the difference | A D5 exception per unit |
| RP6 | A unit that replaces an owner of a rule adds, in the same commit, an ArchitectureRulesTest zero-reference row (or a falling count) for the replaced path | Text censuses missed sites in G302 (three rounds), 2c-5 (readers a census missed) and revision B; a structural guard closes the family | A fourth round on a missed reader |
| RP7 | Every Full unit has a problem statement and acceptance tests written first by a test author of neither candidate (WORKFLOW); a behavior-preserving Full unit's tests are the bp triple (diagnostic snapshot, semantic snapshot with HIR and MIR, fixture IR), the cost rows (RP8) and the scaling classes | Q11; the choice is made on results, never on design text (G216 (a) revision 2's lesson) | Choosing between texts |
| RP8 | Cost stop rules per unit (the owner's 「Relax to 1.03」 of 2026-11-02 for G216 (a)'s landing, PLAN's G216 row, generalized; RQ8): a front or middle unit stops for the owner when Bind with publication is B/A′ > 1.03 and outside the same measurement's A′/A spread (the census harness, cold and warm, Hello and Milestone26) or when warm allocation grows (0 B is deterministic); cold allocation and retained memory are recorded per unit (Q3 allows an allocation that makes work faster) and compared at the stage exit (§10.1 item 3); a back unit stops when an O2 kernel moves beyond the A=A variance (the address-matched control first, R5-M147) or an allocation count changes; O0 is recorded (P1, P3) | Performance found late made G216 (a)'s landing an owner question; the rule makes it a unit gate | Tolerances; a final performance unit |
| RP9 | Primitive arithmetic and comparison witnesses stay Backend shims over the operator table (one instruction each, no `IntrinsicId`, no body); the formatting witnesses, string comparison and destruction move to Kimigayo; `formatFloat` (Ryu) and `formatInteger128` (interim) stay intrinsics | PLAN10 §5.8's table has no shim intrinsic; a Kimigayo body for `i32 == i32` would be the MIR operator again | A body per primitive operator; a vocabulary entry |
| RP10 | The board (§9) is the live record of windows, holds, heavy runs, quiet time and cross-session questions; PLAN §3 keeps area ownership and the standing rules only (an amendment of R5-M7's "hold line in PLAN §3 on dev"; RQ7) | A hold recorded on dev costs a commit per unit and is stale between pushes; the board is read before every heavy action | Hold commits; conflicting PLAN edits |
| RP11 | Windows: two or three a day, run by the middle session in its merge worktree; a unit enters a window when its review is ACCEPT, its unit checks passed on a base at most two windows old, and its files overlap no other unit of the same window (overlaps serialize in order of readiness); the window runs the heavy set once (§7.3), pushes dev, publishes the shared base and posts CLOSED on the board; a failing unit is dropped and the window re-run; no input-changing push outside a window (PLAN, draft and board pushes are free) | The owner's instruction; the heavy set is paid once per window, not per unit | Per-unit Sessions on dev |
| RP12 | Reviews: the owning session of the files reviews (front: Parsing, Binding, Hover's SemanticQuery readers, the diagnostics adapter; middle: Mir, the HIR columns' builder readers; back: Backend, Library, Testing's runtime, Benchmark's kernels); a unit touching two areas gets both reviewers on their hunks; a reviewer never authored the unit's design or code; a consent is a board row, not a message | The owner's instruction | Serial whole-unit reviews by another session |
| RP13 | Prototype first, every unit: a Major or Full unit's design names a throwaway prototype (a `proto/…` branch, deleted after the unit) and what it showed; a Minor or deletion unit's prototype is the reproducer that establishes its failing condition (WORKFLOW Design step 1) | The owner's rule (全体の決まり); G264 U1's cause (V16 on both candidates) was visible in a prototype | A design whose premise fails at implementation |
| RP14 | A unit that changes an A5 verdict, and a unit that adds or changes a library body (the shadow A1–A5 judge it, and a library rejection blocks R4's exit, ARCH "Difference ledger"), runs the full mir ledger before its review and adds no blocking row (until H10); after H10 it runs the generated gate (843 programs), the A5 class set (`MirBorrowsTest`, `MirOriginsTest`, the four scaling classes) and `MirCensusTest` | The owner's rule (the WK lesson) | An unsound acceptance found in round 2 |
| RP15 | Agent state: every agent keeps a state file (progress, the Q12 working-time log, next step) in its session's notes folder; each planning session writes a handoff at every pause with the board's tables copied in; the restart prompt is kept current | The owner's rule; the sessions have been paused and resumed daily | Lost context at a restart |
| RP16 | R7's exit is the structure rows and the measurements of §10.1, not PLAN10's 38,000-line target (Q5: no caps); each commit still states its net size per area and the exit records the areas | Q5 | A size gate |
| RP17 | A unit implemented by a session other than its files' owner holds the files on the board and is reviewed by the owner: R9's Parsing, Helper and Misc units (the middle implements, the front reviews), R7-Y1–Y5's edits of `Binding.Hir*.cs` and `HirTables.cs` (the middle implements, the front reviews), R7-P7's and T1's collector hunks (the front implements, the back reviews), R8's `tests/mir` golden changes (the back implements, the middle reviews) | The owner assigned R9 to the middle session and Syntax to the front; one implementer per unit, one owner per file | A second owner of a file; a unit split by session boundary |
| RP18 | A vocabulary reduction (`IntrinsicId` 34 → 28, `ExpansionKind`'s Range row, `BindingFailure`'s deletion) needs no owner approval beyond this plan's adoption; an addition still does (ARCH) | ARCH's rule | Approvals for deletions |
| RP19 | D3 (Advice and Note reduction, adopted 2026-10-10) applies in the Problem migrations: a snapshot difference that removes a Note or Advice is classified by code in the commit, never argued | PLAN10 §7; Q13's counting | Per-Note arguments |
| RP20 | `CompilerFunctionKind` is deleted at R8's end: a library declaration is a Kimigayo body unless it carries `#Intrinsic` (an `IntrinsicId`) or is a Backend shim (`#Lang` names the four shim witnesses); `IntrinsicDisposition.Of` goes with it; the published rows (`KimiLibraryCatalog.Row`, SPEC 4.9.6) stay, keyed by lang item, and H4's successor verifies each body's A4 summary against its row | PLAN10 §5.8 ("約 28。現在の `CompilerFunctionKind` 75 種を置き換える"); the rows are SPEC facts, the bodies must agree | A kind per library function |

### 2.2 Owner decisions (OQ)

Three packets; "Blocks" names the first unit that cannot start. Each is answerable "as recommended".

**Packet 1: now.**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| RQ1 | Adopt the arrangement (§3) ((b) sessions by stage, as today): three planning sessions by component (front: Syntax and Binding, R7's main line; middle: HIR and MIR analyses, the post-flip defects, R9, R7's HIR side, the merge coordinator; back: code generation and the library, R8's main line); windows two or three a day by the middle session; the board; reviews by the owning session; the caps (two implementers and two designers or reviewers per session, three heavy runs in all); the common rules (prototype first, the ledger or gate before an A5-changing review, agent state files). Today's R4, R5 and BU sessions become the middle, back and front sessions and carry their work in flight (§3.4) | Adopt | Every unit |
| RQ2 | Adopt this plan as the formal plan for R7, R8 and R9 with its exit gates (§10), RP1–RP20 and the records of §12 (DECISIONS, PLAN, ARCH, INTEGRATED); R7's exit measured without PLAN10's 38,000-line figure (RP16); (b) revise and adopt after R6-check's exit | Adopt | Every unit |
| RQ3 | R7's start: (a) design, censuses, prototypes and behavior-preserving units start now beside R4's exit and R6-check, acceptance-changing units after H5 (RP4, RP5); (b) R7 waits for R6-check's exit (PLAN10 §12.2's condition) | (a); the front session's queue is otherwise revision B, B3a, G354-1b and the H1 split alone | R7-D0, N0, C0, P0, E0 |
| RQ4 | R8's start: (a) R8-L0 (baselines, census, prototypes) now, and library or Backend-only units (R8-R1, A1, K1, S1) as soon as their bodies are accepted by the old Analysis and their O2 A/B holds; units needing MIR-only forms after H5 (the back's expectation: S1 is safe before H5; A1 and K1 probably wait for H5, since the old Analysis reports `TransferRequired_Kd` on bare raw and element Place acquisitions until H1 lands; L0's prototype decides); (b) R8 after R7 | (a) | R8-L0 |
| RQ5 | P42's remainder (the first feature work after H5, R5-M13) against R7: (a) P42's full-process design package runs in the middle and back sessions during R7's first units, its Binding part lands in a front window after R7-N1 and R7-V2 (one static slot key owned by `TyCtxt`, R4-M103, is a declaration-table row); (b) P42 before any R7 unit; (c) P42 after R7's exit | (a) | P42's Binding unit |
| RQ6 | Full units and their candidates (§5.5's table): two candidates (Q11) for R7-C2 (Enumerate), R7-C3 (identity), R7-I2 (InferCtxt), R7-P3 (the pipeline) and R8-W2 (the interpolation adapter); one candidate, prototype-first with a design note and one review (the owner's exception as for U2.2), for R7-D2 (the driver), R7-E1, R7-V2, R7-Y2, R7-T1, R7-P7 and R8-L1, each recorded in DECISIONS with the unit as a per-unit owner exception (no WORKFLOW revision); R7-O2 keeps two (the owner's OQ-1 (c)); (b) two candidates for every Full unit; (c) one for every Full unit | As listed | Each Full unit |
| RQ7 | The board as the live hold record (RP10): R5-M7's PLAN §3 hold lines end; PLAN §3 keeps area ownership; (b) keep the PLAN §3 hold lines | Adopt | The first window |
| RQ8 | The cost stop rules (RP8) for every unit of the three sessions; (b) judge by the measurement's spread alone, without the 1.03 bound | Adopt | The first unit |

**Packet 2: before the first vocabulary or SPEC unit of its stage.**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| RQ9 | Vocabulary targets (ARCH table): `BindingFailure` (88 members, limit 89) → deleted (R7-E5), its requirements the closed enumeration in `DiagnosticRequirement.tinyhand` with the stable `Binding.*` names; `IntrinsicId` 34 → 28 (R8-I1); `CompilerFunctionKind` 76 → deleted (R8-G1, RP20); `KimiDeclarationId` 191 → the lang-item set (about 50, H; R8-G1); `HirOp`, `HirRes`, `HirAcquire`, `HirAdaptation`, `HirPlaceKind`, `HirCompletion`, `ExpansionKind` unchanged or reduced; the MIR vocabularies unchanged; (b) keep `BindingFailure` as the requirement enumeration and delete only the switches | Approve the targets; each unit's commit records the count | R7-E1, R8-G1, R8-I1 |
| RQ10 | The SPEC revisions of PLAN10 §11 that fall in these stages (§5.1 R7-SP1–SP2, §5.2 R8-SP1–SP4) and the procedure: each text drafted by the owning session, shown to the owner before it lands (as revision B), SPECJ in a follow-up commit within the unit, an INTEGRATED row per intake, examples and tests in the same commit; (b) revise after the implementation lands | Approve the list and the procedure | R7-SP1, R8-SP1 |
| RQ11 | Representation structs (PLAN10 §5.8, approved as recommended 2026-10-10): `internal` lang-item structs `ArrayRepr<T>`, `SliceRepr<T>`, `StringRepr` with `#Layout("C")`, the Dictionary `Slot<K, V>` in Kimigayo layout, reached by SPEC §5.4's raw conversion (the punnable list of Appendix E gains the pairs); the managed Types keep their Type semantics (SPEC §22.1's text R8-S1); the SETTLED entry rejecting ordinary structs; the lang attributes `#Lang`, `#Intrinsic`, `#CallerLocation`, `#Unsupported` as identification only (§16-9); (b) keep `Layout`'s handle rows and no representation structs (PLAN10 rejected it) | Confirm; the texts come under RQ10 | R8-R1, R8-G1 |
| RQ12 | G128's interim ends (BU-12, the R5 and R6 plan's PD30): with `TyCtxt.Apply` pure (R7-T1) a Type past the storage height or walk bound is the collector's Resource record under R5-M170's growth rule (IMPL 21.3.2), not `Unsupported_Kd`; `StorageNesting`'s capability row is deleted; (b) keep the interim until P42 | Approve | R7-T1 |

**Packet 3: later.**

| ID | Question | Recommendation | Blocks |
| --- | --- | --- | --- |
| RQ13 | One frozen library per process (§16-8, G337 O3 with O2): decide at R8-L1's measurement: adopt when the library's growth raises cold Bind of Hello by 10 % or more over R8's start, or when the prototype's saving is 20 % or more of a fresh small build; otherwise record and keep per-compilation binding | Decide by the measurement with these thresholds | R8-L1 |
| RQ14 | SPEC §8.4.4 inherited-conformance OCC (§16-2): keep `Unsupported_Kd` through R7 and R8; propose the revision to a post-selection obligation at R9's exit, with OCC-X; (b) revise at R7 | Keep; propose at R9 | R7-SP3 |
| RQ15 | Diagnostic partition and phase names (§16-7): keep `Binding`, `ControlFlow`, `Ownership`, `Emission` and `Syntax.<Form>` as lasting names (R5-M167 row 14 already made `Emission` lasting); no rename; (b) rename at R9 | No rename | R7-E6 |
| RQ16 | `formatInteger128`'s removal stage: R9 if R8-W2's Kimigayo formatter reaches i128 within variance, else a post-reduction unit | R9 or after, by measurement | R8-I1 |
| RQ17 | Where milestone work resumes (Q1 deferred "the sequencing of R7 and R8 with milestone work"): P42 by RQ5; P38 → P39 → P41 → OCC-X → P43 after R7's exit in the front session's lane, alternating timed sessions with the shared-object track's P40 (PLAN §3), beside R8 and R9; (b) interleave milestone units with R7's | After R7's exit | The milestone order |

---

## 3. The arrangement

### 3.1 Sessions by component

| Session | Area (files) | Main line | Also |
| --- | --- | --- | --- |
| Front (前段) | `Compiler/Lexing/**`, `Compiler/Parsing/**`, `Compiler/Helper/**`, `src/Kimi/Misc/**`, `Compiler/Binding/**` (the library index files `KimiLibrary*.cs`, `KimiDeclaration.cs` are the back's; `Binding.Hir*.cs`, `HirTables.cs` and `HirVocabulary.cs` are edited by the middle's H units under RP17), the root helpers `Compiler/*.cs` (T2 deletes them; the back's reads move with its review), `Checking/**` and `Lsp/**` as SemanticQuery readers, `src/Kimi/Diagnostics/**` (the adapter) | R7 | R6-check's Binding side (H1 split, H5's Binding hunk, H7, H11, H14; R5-M182's division); revision B (A), B3a, G354-1b; after R7: the milestone order (RQ17) |
| Middle (中段) | `Compiler/Mir/**` (model, build, validator, analyses), the HIR columns' builder readers, the mir ledger until H10, `tests/mir` (the goldens follow the builder: a back unit that changes them holds them and the middle reviews the golden diff), `Compiler/Documentation/**` (R9-M1's owner; the lexer's and parser's association hunks reviewed by the front) | The post-flip analysis defects (§5.4), R9, the merge windows | R7's HIR side (R7-Y1–H5), R7-V3; R6-check's MIR side (H3-1, IC, H4, H3-2, Q19) and the coordination of H3b, H5's window, H8–H10, H12, H13, H15 (the units keep their R5-M182 owners) |
| Back (後段) | `Compiler/Backend/**`, `Backend/*.ll.in`, `src/Kimi/Library/**` with the library index files, `src/Kimi/Testing/**` (the test runtime), `Compiler/Analysis/**` (the old Analysis: frozen under D5, deleted at H10b) and `Compiler/Emission/**` (until R9-M2 moves it) | R8 | R6-check's Core side (H5's Core hunk, H9, H10b's deletion commit, H12, H13, H15 as units), R5's open rows (G121's residuals, G153 (2), G157's items, G168 N1, G205, G311's builder fact, G337), the review of the collector hunks of R7-P7 and R7-T1 (the front implements them under a hold), R9-B1's codegen half |
| Core and shared | `Compiler/Core/**`, `SolutionAndProject/**`, `Unit/**`, `scripts/**`, `tests/scripts/**`, `ArchitectureRulesTest` | — | Any session edits with a board hold; the middle session reviews Core, the back session `Compilation.Generation.cs` and `Compilation.Switch.cs` |
| Tests and benchmarks | `tests/xUnitTest/**`, `src/Benchmark/**`, `tests/mir/**`, `tests/milestones/**`, `src/backend/windows-x64/*.ps1` | — | A test file follows the file it tests; a Benchmark series follows the component it measures (`BindingBenchmark` the front, `CompilerPipelineBenchmark` the middle, the O2 corpus and `KimiLibraryBenchmark` the back); the milestone harnesses the back |
| Docs | `docs/**`, `draft/**` | — | The session whose unit changes a fact edits the row (AGENTS Documentation); PLAN §3's session paragraphs by each session; DECISIONS entries by the deciding session |

### 3.2 Windows (まとめて入れる window)

- The middle session opens two or three windows a day at announced times (board §Windows), each in its merge worktree from the newest
  shared base. (The owner's list names "sel": the selected code generator went with R6-gen's G4, DECISIONS R5-M160, so the heavy set is
  the default Session with MIR code generation, the diagnostic snapshot, the mir ledger until H10, the selected native families and
  milestones, and the O2 kernels.) A window takes every queued unit that meets RP11, merges them in order of readiness, runs the heavy set once (§7.3: the
  default Session with the affected native families and milestones, the diagnostic snapshot against the base, the mir ledger until H10,
  the O2 kernels when a Backend or library unit is in the window), pushes dev, publishes `artifacts/verify/shared-base/<sha>/`, and posts
  CLOSED with the dev sha. The window is one heavy-run slot of the three.
- A failing unit leaves the window (bisected by unit); its session fixes it on its branch; the window re-runs with the rest.
- R6-check's windows through the flip group's (H5 and H6) stay the back session's, as H0 assigns them (R5-M182); the middle session runs
  every window after it (DECISIONS R7-M2 amends R5-M182 there). A window's heavy run never starts inside a booked quiet slot. Merge
  conflicts in appended DECISIONS lines or entries are resolved by keeping both, in order.
- Each loaded unit's owner session reviews the window's merge of its hunks when a conflict was resolved by hand; a conflict-free merge
  needs no second look.
- Between windows the sessions build and test on their branches (unit verification on their own bases); timing runs book quiet time
  on the board (§9) and pause the others' builds.

### 3.3 Caps and rules

- Per session: implementers at most two, one per lane, their files disjoint, at most one unit changing a shared representation at a
  time; designers and reviewers at most two (R4-M119, R5-M113). A Full unit's two candidates count as two implementers.
- Heavy runs at most three across the sessions (`artifacts/heavy-runs/*.run` stays the lock; the board lists the plan). A window holds
  one. Timing needs an empty registry and a booked quiet slot.
- Every unit's design and implementation phases stay within one hour of agent working time (Q12); sizing before start; a stop writes
  the record and the split plan, and the split units start only on the owner's GO (WORKFLOW "Approval").
- A review is one per unit; a fix after REVISE is the same reviewer's repeated review; two reviews sharing a cause or a third non-ACCEPT
  suspend the unit for the owner (Q11); record selection is the planning session's (Q13).
- Prototype first (RP13); an A5-changing unit runs the ledger or the gate before review (RP14); state files and handoffs (RP15).
- Messages between sessions only when a board row needs an answer (a consent, a ruling, a timing hold); the app pauses cross-session
  sends after ten without the owner, so routine acknowledgements stay on the board.

### 3.4 Mapping the work in flight

| Today | Session | Carries |
| --- | --- | --- |
| BU | Front | Revision B and B3a are held by the owner (PLAN 5b1777f34, 「根本的な解決は後回しにする…」; `held/bu104-specb-A` 5ee090855, `held/bu111-b3a-A` ea0187d75, B3a done and ACCEPTED there): absorbed into R7-I2, whose tests (I1) take their rows and probes and whose design takes revision B's group review (Re-check 2) as evidence; G354-1b landed (BU-86), G335 U1 landed (BU-87; G399 remains), the H1 split is done ((b2) on dev, H1-tests and H1 on `claude/r6-flip`, BU-84; Q8-3 there too, BU-88, its cost an open owner question); in flight: G354-1c (G354 (1)'s R1), G181 (2) (due); next: H5's Binding hunk in the flip group, H7, H11, H14; the rows the owner gave to R4 (G179 (a), G181 (1), G180, G354 (2), G351) are R4's; still open for R7: G51, G377, G365, G369, G179's selection through the layer, G393, G394, G395, G397, G399, G400, G401, G402 (§5.1) |
| R4 | Middle | R4 exited (R4-M165); G216 (a) closed with a boundary, so U3, U2j-a/b and G388–G392 are a proposed common fix in G216's row, not queued units; r4w12 landed IC, H3-1, H4, G179 (a) and G181 (1); r4w13 landed G354 (2) and G180 (a) (dev `fe0a14d86`); next, in the owner's order (verbatim 「(A) exit の後に直す一覧（G396）を、オーナーが以前決めた順に進める」): G216 (b), G215, G264 U1, G349, G338, G350 and G396's items (§5.4); H3-2's U1a and U1b are written by the back's implementer in `Mir/Analysis` with R4's consent; the windows after the flip group's are the middle's |
| R5 | Back | R6-check in flight: Q19 done, IC, H3-1 and H4 landed, H13 confirmed (V1 `MirBuilder.Blocks.cs:24`, V2 `MirBuilder.Selections.cs:68`, Minor, reviewed, waiting for a back window), G157 (1) done (R5-M183), H3-2 U0 landed (`Core/Compilation.Publication.cs`, new), U1a–U3 follow; H3b's rehearsal and classification and the flip group's window stay the back's as H0 assigns them; R8-L0 starts now under RQ4 (its `Array.append` prototype decides whether A1 and K1 can land before H5: the old Analysis reports `TransferRequired_Kd` on bare raw and element Place acquisitions until H1 lands at H5, so A1 and K1 probably wait, S1 looks safe); G337 O2/O3 deferred; G157's staged items |

---

## 4. Structure and order

### 4.1 Phases

| Phase | Window | Content |
| --- | --- | --- |
| A | Now → the flip group's window (H5) | Front: G354-1c, G181 (2), H5's Binding hunk in the flip group; R7-D0, N0, C0, P0, E0 (censuses and prototypes), R7-D2a, R7-N5 and T2's Binding-read moves (bp), R7-D1, R7-D3, R7-C8, R7-V1a, R7-E1. Middle: G216 (b), G215, G264 U1, G349, G338, G350 and G396's items (§5.4). Back: H3-2's U1a–U3, H13's V1 and V2, H3b's classification and the flip group's window (H0 assigns them to the back), R8-L0, R8-R1's design and prototype, the A1 and S1 prototypes under the old Analysis |
| B | R6-check after H5 (H6 → H15) | Front: H7, H11, H14; R7-I1, C1, P1 (test authors), R7-P2, R7-E2, R7-SP1 (text), R7-V1b, R7-V2, R7-N6, R7-I4, R7-N1–N4 as the lanes allow. Middle: every window from here (RP11), R7-Y1, the defect stream. Back: H9, H10b, H12, H15 as units; R8-R1, R8-A1 (a–c), R8-K1a |
| C | R7 main line, R8 families | Front: R7-D2 (d–g), N1–N4 where B left them, I2–I3, C2–C7, P3–P10, E3–E4, T1, T3, V4, SP1's implementation (P4), O3's non-acceptance parts. Middle: R7-Y2, Y3, V3, Y5; the post-flip defects; R9-F1–F5 (with the front's review). Back: R8-K1b–c, S1, S2, W1, W2, U1, G2; P42's design package (with the middle) |
| D | R7 and R8 exits, R9 | Front: R7-P11, E5, O1–O2, T2's deletions, H4's property deletion, X1–X3; the milestone order resumes (RQ17). Middle: R9-M1, M2, B1, B2, C1, X1. Back: R8-G1, I1, L1, X1; R9-B1's codegen half |

### 4.2 Critical path

```
Front:  G354-1c ─► G181 (2) ─► H5's Binding hunk (the flip group) ─► H7 ─► H11 ─► H14
        D0/N0/C0/P0/E0 ─► D2a ─► D1 ─► V1 ─► (I1,C1,P1 tests) ─► I2 ─► P2 ─► P3 ─► P4(T1–T4) ─► P5 ─► P6 ─► P11
                              └► C8 ─► C2 ─► C3 ─► C4 ─► C9          E1 ─► E2 ─► E3 ─► E4 ─► E5 (after H10)
                              └► N1 ─► N2 ─► N3 ─► N4 ─► V2 ─► D2 ─► T1 ─► T3 ─► O1 ─► O2 ─► X1 ─► X2 ─► X3
Middle: G216(b) ─► G215 ─► G264 U1 ─► G349 ─► G338 ─► G350 ─► G396's items (the defect stream, §5.4); the windows from H6 on
        (after the flip group) Y1 ─► Y3 ─► Y2 ─► V3 ─► Y5(identity places with C3)        R9-F1 … F5 ─► M1 ─► M2 ─► B1 ─► C1 ─► X1
Back:   H3-2 U1a–U3 ─► V1/V2 ─► H3b ─► H5's window (the flip group) ─► L0 ─► R1 ─► A1a ─► A1b ─► A1c ─► K1a ─► K1b ─► K1c ─► S1 ─► S2 ─► W1a ─► W1b ─► W1c ─► W2 ─► W3 ─► U1 ─► G1 ─► G2 ─► I1 ─► L1 ─► X1
```

- R7's longest chain: D0 → D2a → D1 → V1 → C1 → C2 → C3 → C4 → C9 → P3 (P3 needs I2 and C2) → P4 → P5 → P6 → P11 → X1 (about 14 units, each
  split into hour-sized phases). R8's: L0 → R1 → A1 → K1 → S1 → W1 → W2 → G1 → I1 → X1 (about 20 units). R9 follows R8-G1 (the lang
  attributes need R7-N4's `AttributeKind`) and R7-T2 (the root files gone before the final records).
- Gates from the other stages: H5 (the flip group's window) before every
  acceptance-changing R7 unit (RP5); H10 before E5, P11, T2's deletions and Y4's property deletion; H14 before the forwarding properties
  go; the flip group's window before R7-Y1 (R6-check's H1 edits the same publication code); R7-N4 before R8-G1 and R9-F3; R7-T1 before
  R8's collector changes to `Instance`; R7-P7 shares the collector with the back; R8-W2 shares the builder's interpolation expansion with
  the middle; R9-B1 shares codegen. R9-F1, F2, F4, F5, M1, M2, B1 and B2 need no R7 or R8 unit; R9-F3 needs R7-N4; R9-C1 and X1 follow
  R7-X2 and R8-X1.

### 4.3 Expected windows per unit

The board's queue is the record; this is the expected order. A unit loads into the first window after its review's ACCEPT and its
unit checks on a current base (RP11); within a lane the order is its stream table's. Windows are numbered per phase (A1, A2, …);
two or three open a day, so a phase's numbers are not days. "F", "M", "B" name the session that loads the unit.

| Window | Front | Middle | Back |
| --- | --- | --- | --- |
| A1 | G354-1c (G354 (1)'s R1) | G216 (b) | H3-2 U1a (`Mir/Analysis/MirReport*.cs`, R4's consent); R8-L0's Strings kernel (a Benchmark file) |
| A2 | G181 (2) | G215 | H3-2 U1b, U2; R8-L0 evidence (no code) |
| A3 | R7-D2a (the `Binding.cs` split), R7-N5 batch 1 | G264 U1 (after G342's rows) | H3-2 U3; H13's V1 and V2 |
| A4 | R7-D1, R7-D3 | G349 | H3b's classification (evidence); R8-R1's design and prototype (no code) |
| A5 | H5's Binding hunk (in the flip group) | G338, G339's row | the flip group's window, the back's: H1, H1b, Q8-3, H5 (Binding and Core hunks), H6, one merge after the group review |
| B1 | H7, H11; R7-C8, R7-V1a | R7-Y1 (inline writes); the windows are the middle's from here | H9; R8-A1a (the old Analysis no longer decides) |
| B2 | H14; R7-E1, R7-N5 batch 2 | G350; G396's items (§5.4) | H10b; R8-A1b |
| B3 | I1, C1, P1 (test authors; test-only commits); R7-P2 | G396's items | H12, H13's row; R8-A1c |
| B4 | R7-E2, SP1's text (a doc push), R7-V1b, R7-V2, R7-N6, R7-I4, R7-T2 reads batch 1 | H15's record | R8-K1a; R8-R1 |
| B5–B7 | R7-N1 (a–f); N2 | R7-Y3; V3; the defect stream | R8-K1b (with the middle's G255 C2); K1c; R8-S1 |
| C1–C4 | R7-D2 (d–g); N3; N4 | R7-Y2; the defect stream | R8-S2; R8-W1a; R8-W1b; R8-U1 |
| C5–C9 | R7-I2 (FP2's chosen core, then its Owner parts); I3 one rule per window | Y5 (identity places, with C3); the defect stream (§5.4) | R8-W1c; R8-W2t; R8-W2 (FP2's chosen core, then its parts); R8-W3 |
| C10–C16 | R7-C2; C3; C4; C5; C6a–c; C7; C9 | the defect stream; R9-F1 (split), R9-F1 (loop) | R8-G2; P42's design package (doc) |
| C17–C24 | R7-P3; P4 (T1, T2, T3, T4 one each); P5; P6; P7 | the defect stream; R9-F2, F3 | P42's MIR and A4/A5 parts (with the middle) |
| C25–C30 | P8, P9, P10 one rule per window; E3; E4; T1; T3; V4 | R9-F4, F5 | R8-L1's measurement (evidence), then L1 if adopted |
| D1–D6 | P11; E5 (a–e); O1 (a–f); O2; O3; T2's deletions | Y4 (the properties); R9-M1; R9-M2 | R8-G1; R8-I1; R8-X1 |
| D7–D10 | X1; X2; X3; the milestone order resumes (RQ17) | R9-B1 (with the back), B2; R9-C1; R9-X1 | R9-B1's codegen half |

### 4.4 Estimate in working sessions (H)

| Chain | Sessions |
| --- | --- |
| Phase A (the front's queue, R6-check through the flip group, R7's censuses and first bp units) | 4–8 |
| R6-check's remainder after the flip group (H6 → H15) | 3–6 |
| R7 after H5 (about 55 families, about 110 hour-sized units, two lanes; the FP2 cores C2a, I2a–b and P3a take both implementer slots in turn, so the front's three chains serialize during them while the middle and back continue) | 30–45 |
| R8 (about 25 units, two lanes, beside R7) | 15–25 |
| R9 (about 15 units, beside R8's tail) | 8–12 |
| The post-flip defect stream (the middle's lane) | runs through C and D |

Windows at two or three a day absorb about four to eight units a day across the sessions (H); the chains above, not the windows, bound
the calendar.

---

## 5. Units

Conventions as the R5 and R6 plan: Verification **Doc** / **Comp** (unit checks of the named classes plus a Session at the window) /
**Bench** / **Evid**; Review **R** one review, **D+R** design review then review, **FP2** Full with two candidates, **FP1** Full with one
prototype-first candidate (RQ6), **Owner** an Owner change under a recorded design, **G:name** a group review; Class **Minor / Major /
Full / Owner**; Size `+added/−removed` (H); Phase A–D (§4.1); the session is the stream's unless the unit id carries (M) or (B+M). Every unit that creates or moves a component writes its
ARCH canonical-component row, CODEMAP row and entry-file contract in its commit; every unit that replaces an owner adds its ratchet row
(RP6); every unit names the open issues it closes. Ids are unique within a stage and written with the stage prefix across stages (R7-C1,
R9-C1); this plan's decisions are RP1–RP21 and its owner questions RQ1–RQ17, so they never collide with the R5 and R6 plan's PD and OQ
numbers, with the A5 step 2 plan's PD numbers or with DECISIONS' Q entries, which are always written with their plan's name here.

### 5.0 Sizing under Q12

Every unit below is sized by WORKFLOW's working-time limit: each design and each implementation phase is at most one hour of agent
time. A unit whose Size column exceeds what an hour writes is a **family** whose parts are listed here (or in its row); each part is
classified anew (WORKFLOW "Split plan"), has its own completion condition, and lands in its own window. The rule for a Full family
(RP21): the Full flow (problem statement, tests, candidates, choice, review) covers only its **core**, the shared representation and
the first consumer that proves it, sized to one hour per phase per candidate; the chosen design is recorded in DECISIONS, and the
remaining parts are Owner-class units under it (one review each, or one group review `G:<family>`), never a second candidate. A
census unit (D0, N0, C0, P0, E0) is a script kept in `artifacts/verify/r7-<stream>/census.py` that runs in minutes; its agent hour is the
prototype, and the planning session's reading of the output is its own hour.

| Family | Core (the Full or first unit) | Parts (each ≤ 1 h per phase; class) |
| --- | --- | --- |
| D2 | d: the declaration steps call through `DeclQuery` by demand (one step group), with g: the stage order deleted (FP1) | a: `Binding.cs` split by responsibility (bp; Major; Phase A, before D1); b: generation-stamped pooled tables for `ResetPass`'s 27 Clears (bp; Major); c: `IndexVisitor` → `SyntaxTable` pre-order and collection (bp; Major); d1–d4: step groups (Types and containers; signatures; conformances; bodies' prerequisites) through `DeclQuery` (Owner under D1's design); e: the second validation run removed; f: `InvalidateProjectionCertificates`' re-validation loop removed; g: the four stages (Owner under the chosen core) |
| N1 | a: `ResolvePath` for Type names (Major, D+R) | b: value names; c: member lookup; d: qualification; e: aliases and modules; f: `Self` and generic parameters as scope symbols (the string tests go) — Owner under a's design |
| N5 | — | batches of at most eight files per unit (Major each), the driver's and resolver's first, the ArchitectureRulesTest count per batch |
| V1 | a: `SignatureVerdict` as a query behind the existing re-proofs (bp; Major; Phase A) | b: the callers read it and `CheckSignatureTypeConstraints` goes (records only before H5; any acceptance move waits for H5); c: G373's one owner; d: G354 (2)'s fact (BU's patch); e: G382's r08 |
| I2 | a: the variable store, undo log and snapshot (new, unused) with b: `SlotSolution` as a view over it (bp) — FP2 on I1's rows | c: candidate evaluation through snapshot and rollback (bp); d: join states as constraints with the `Awaiting` fact (G335 U1's rows); e: `ArrayInference`'s holes; f: `ShapeEvidence` and `InferenceBoundaries`; g: `ResultSurvey` and `ResultEvidence`; h: Diagnostics' unifier; i: the waited-join state deleted (B3c) — Owner under the chosen design, `G:R7-infer` |
| C2 | a: `Enumerate` for conformance facts (one goal kind) with the memo and the one cycle rule — FP2 on C1's rows | b: capabilities and Owned through the same memo (with C6); c: the three eager-expansion functions deleted; d: the availability routing deleted; e: the rounds' quadratic case and `ResolveConformance`'s staging removed; f: the limits in IMPL and the Resource record — Owner under the chosen design, `G:R7-solver` |
| C3 | a: the one identity judge over decision 10's classes (bu65 r5 the design-first candidate) — FP2 on C1's identity rows | b: the declaration-frame crossing (G302); c: the ArchitectureRulesTest guard; d: `cyclic` reported at the declaration; e: identity Places published (with Y5) — Owner under the chosen design |
| C9 | — | a: `BoundContracts` folded; b: `ContractAgreement`; c: `ConditionalConformances` and `InheritedConformances` into `ImplTable` rows; d: `Constraints`/`ConstraintValidation` restructured — Major each |
| P3 | a: the one pipeline over `CallContext` for direct and value calls (two forms), normalized to `FnSig` — FP2 on P1's rows | b–l: one form per part (requirement; property; base; constructor; implicit constructor with the silent selections and rollback; Function Item; iteration; operator; enum Case construction and `Binding.Enums`' path; object creation; virtual; `ValidateFixedConstruction`'s second pass) — Owner under the chosen design, `G:R7-pipeline` |
| E2–E4 | — | per `Fail*` kind within the family (`FailExplained`'s 64 sites in two parts, `FailMismatch`'s 32 in one, the rest grouped to about 30 sites per part); Major each |
| E5 | — | a: `BindingFailure` deleted (no reader left); b: the switches and `ReportIssue`; c: the consultation frames and `IsDerived`; d: `DiagnosticKey`'s subject a `SourceRef`; e: the requirement enumeration in the tinyhand — Major each |
| O1 | — | a: `LocalRegions`; b: `CallRelations`' judging part; c: `OriginProof`'s judging part; d: `ResultPremises`' six modes → one enumerator; e: `OriginPremises`/`OriginPremiseClosure` per-request rebuild → one closure per body; f: the candidate bound Begin/Keep/End and `originStateVersion` — Owner (A5 plan §7) each |
| T2 | — | one helper per unit; `ReferenceTypes` (29 files) and `ElementAccess` (17) in two batches each by concern |
| T3 | a: `Relate` as the one structural walk (Major) | b: the fits' shared parts; c: the mutable caches removed |
| W2 | a: one formattable's bound declaration and the fixed-buffer expansion for the direct `Console.writeLine` form — FP2 on W2t's rows | b–e: the other writer forms; the `HeapBuffer` fallback; `FormatBound`/`FormatEstimate` deleted; the intrinsics deleted — Owner under the chosen design, `G:R8-format` |
| R8-A1, K1, W1 | — | the a–c parts in their rows, each one operation group |

### 5.1 R7 Binding consolidation (front, unless marked)

**Open issues absorbed by R7 units** (each closes as a consequence of the unit's rule, never as its own clause):

| Unit | Closes or re-records |
| --- | --- |
| D1–D3 | G144 (depth and the cleared completion cache), G348 |
| N1–N6 | G317, G324, G325, G52's consumers, G236, G155's "each requirement its own record" (with E1) |
| V1–V4 | G375 (remainder, sigref1), G373, G354 (1)'s R2 (its R1 is G354-1c, in flight; (2) landed in r4w13), G382, G214 |
| I0–I4 | revision B and B3a (held by the owner, PLAN 5b1777f34; absorbed into I2), G383 (held with them), G394, G399 (G335 U1's follow-up), G159 (2) (3), G152, G356, G378, G388, G390, G391, G360, G361, G275 (2), G159's SPEC questions reported |
| C2–C9 | G170 C3/C4 and the identity unit, G302, G294, G180's remainder G402, G397 (the identity unit), G172, G234, G207, G200, G201, G202, G208, G365, G369, G327, G328, G187, G355 (with Y5), G401 (the pair target's fact), G400's Binding half (closes with `EffectSummary` at H5, else C7), G309-R, G140 U3 |
| P2–P11 | G379, G396 (9)'s function-reference route (G351 closed), G377, G393, G395 (the enum payload's case plan), G265, G267, G386, G274, G343, G51, G179's selection through the layer ((a) landed), G164, G203, G331, G323, G322, G181 (2) (due), G296, G149, G300, G157 (5) (16), R1e, G242's `TyCtxt.Instance` note |
| E1–E6 | G155, G319, G333, G239's display, G343's adapter, DIAGNOSTICS §11's Binding items (each closed or re-recorded) |
| O1–O3 | G194, G195, G257, G230 (A), G216 (b) and G215 (with the middle), STATUS's "Binding still judges a relation between fixed Origins until R7" |
| T1–T3 | G128 (RQ12), the R5 and R6 plan's PD30, G242, G289 (1) |
| Y1–Y5 (middle) | G251 (1) (3), G147 (2), G233, BU-72's `IdentityPremise`, R4-M142's `PublisherUses`, G355's builder half |

**Stream D: driver and queries**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D0 | Census and prototype (no commit) | `artifacts/verify/r7-d0/`: the pass order (`BindDeclarations` 29 steps, `BindBodies` 4, `ResetPass` 27 Clears), the two validation runs (`ValidateBoundDeclarations`, `PublishSourceStates` rounds), `IndexVisitor`'s recursion, the 107 Koto-keyed tables and 1,150 forwarding reads by file and concern, Hover and LSP readers of Binding state; prototype: one declaration query (function signatures) memoized behind the current order, Bind cold/warm and 0 B measured | RQ3 | Evid | 0 | — | A |
| D1 | Declaration query state | `DeclQuery(declaration, kind)` with NotStarted/Running/Done/Error and cycle detection, generalizing `SourceState` (BU-57, BU-59); the existing steps call through it, order unchanged (bp); pooled per pass | D0 | Comp: `ConformanceConvergenceTest`, `ConstraintBindingTest`, `CoreCatalogTest` (allocation), the four scaling classes; bp; cost rows | +250/−150 | Major (Binding-internal: one owning component, the pass contract unchanged), D+R (the query vocabulary) | A |
| D2 | The four-stage driver (a family: §5.0) | Collect → declaration queries forced by demand → bodies → finalize; `ResetPass`'s Clears replaced by generation-stamped pooled tables; `IndexVisitor` replaced by the `SyntaxTable` pre-order and collection; the 29-step order, the second validation run and the whole re-validation loop (`InvalidateProjectionCertificates` in `Binding.ConstraintValidation.cs`) deleted; `Binding.cs` (1,638 lines, over the token limit) split by responsibility first | D2a–c, D1, N1, V2 | Comp: Session; bp triple; cost rows; scaling classes | +300/−900 | Full, FP1 (RQ6) | C |
| D3 | Bounded walks | One depth rule for Binding's recursive walks, chosen by what the walk is over: a syntax walk iterates with an explicit stack (`IndexVisitor`'s successor), a Type walk is bounded by `BoundType.Height` (BU-71) with a located Resource record past the bound (IMPL A.4; `OperandContexts`, `TypeOrigins`); the 250-deep `Box<…>` and the 260-deep written Type end in a record, never a crash | D0 | Comp: new depth rows in `TypeDagScalingTest`; `InstanceCollectorTest.StorageNestingIsAPropertyOfTheComponent` in Debug | +120/−40 | Major, R | A |

**Stream N: resolver**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| N0 | Census (no commit) | The base walks (PLAN10 counted six), the access checks (`Binding.Access.cs` and `ContractMatching`'s parallel copy), the `"Self"` string tests, the attribute string sites (`Binding.Attributes.cs`, `Binding.cs`, `UnaryKoto.cs`, `Parser.cs`), qualification repair sites, alias and module lookups; prototype: `ResolvePath` for Type names only, bp | RQ3 | Evid | 0 | — | A |
| N1 | `ResolvePath` (a family: §5.0) | One lookup with SPEC §9.4's stage sequence; `Self` and generic parameters as scope symbols; the stage-stop `QualificationRequired_Kd` (SETTLED) a stage rule; Types, Names, MemberLookup, Qualification, Aliases, Modules call it | N0, D1 | Comp: `QualificationRequiredTest`, `NamedAliasTest`, `InheritedNameBindingTest`, `RefinementNameBindingTest`, `ApiAccessBindingTest` (the census completes the list), the diagnostic snapshot 0; cost rows | +400/−900 | Major (Binding-internal; the resolution results unchanged), D+R (scope symbols) | B |
| N2 | Base layers once | One `BaseLayers(decl)` declaration query (`AdtDef`'s base) read by InheritedConformances, VirtualOverrides, OriginInheritance, Structs, Access; the other walks deleted | N1 | Comp: `InheritedStorageTest`, `VirtualOverrideBindingTest`, `InheritedConformanceTest`; bp | +80/−350 | Major, R | B |
| N3 | Visibility as one predicate | `Visible(scope, declaration)`; ContractMatching's copy deleted; repair candidates (`self.`, `Self.`, `C.`) made by the adapter from the Problem's facts | N1, E1 | Comp: `ApiAccessBindingTest`, `PropertyApiAccessBindingTest`, `QualificationRequiredTest`; snapshot 0 | +60/−250 | Major, R | B |
| N4 | `AttributeKind` | The parser classifies attributes (Layout, LibraryImport, Test, the R8 lang family, Unknown); Binding reads the kind; the string sites and `TokenContext.AttributeKoto`'s Binding-side reads go (R9-F3 removes the reader state) | N0 | Comp: `UnknownAttributeCertificateBindingTest`, `TestAttributeCertificateBindingTest`, `LayoutAttributeBindingTest`, the syntax corpus; bp | +90/−140 | Major, R (front reviews the parser hunk too) | B |
| N5 | Koto-keyed tables → `SyntaxId` | The 107 sites in 31 files, converted by concern (this unit: the driver's and resolver's; E units: Diagnostics' 15; others with their component), with the ArchitectureRulesTest count falling to 0 by X1 | — | Comp per file's classes; bp | ±200 per batch | Major (one mechanical family in batches of at most eight files; a search finds every site, so no part is Minor), R per batch | A→D |
| N6 | Every part bound, a use rests on the first failed part | Capture-list entries as nodes (G317), an associated application binds every argument (G324), a base constructor rests on its failed base clause (G325), import attributes' independent requirements (G155, with E1): one continuation rule in the resolver | N1, E1 | Comp: `CaptureDiagnosticTest`, `AssociatedTypeFormationBindingTest`, `InheritedNameBindingTest`, `LibraryImportTargetBindingTest`, `ForeignBorrowImportTest`; snapshot classified (added records on rejected programs, Q13) | +120/−60 | Major, R | B |

**Stream V: declaration queries and views**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V1 | Published signature verdict (a family: §5.0) | `SignatureVerdict` is a declaration query; calls, function references and contract matching read it (`CheckSignatureTypeConstraints` deleted, G375); one owner of a Function Type's result clause (G373); a judgment publishes its fact where it decides (G354 (2), BU's `g354-2.patch`; G382 r08) | D1 | Comp: `ApplicabilityChainRegressionTest`, `CallConstraintFailureTest`, `ConstraintBindingTest`, `FunctionTypeConstraintBindingTest`; the generated gate (named moves only); cost rows | +150/−300 | Major, D+R (G375's column stopped because a nested named function binds its header with its enclosing body, so one publication before the bodies did not cover its readers, and the owner accepted the remaining cost, PLAN G375 and G382; D1's demand-driven query computes the verdict when it is first read, which removes that reason: the unit's DECISIONS entry supersedes with it) | A (a), B (b–e) |
| V2 | `FnSig`, `SignatureKey`, `ImplDecl` and `ImplTable` | The declaration table rows built once per pass by their queries; `ImplTable` (Declared/Conditional/Inherited/Builtin; Verified/Error/Unsupported) the one conformance record read by the solver, `VTableDef`, `ResolveRequirementWitness`, Hover; `EffectBoundTable` and `AvailableBounds` complete the R2b views (C7) | N2, D1 | Comp: `ConformanceConvergenceTest`, `InheritedConformanceTest`, `ConditionalConformanceBindingTest`, `VTableDefTest`; bp | +300/−400 | Full, FP1 (the table every Binding component and Hover read; RQ6) | B/C |
| V3 (M) | `MirDefKey` by declaration id (R3-6) | The table numbers definitions densely per module; `MirDefKey(BindingSymbol)` → `MirDefKey(DeclId)`; `MirProgram`, the collector's `Instantiation` follow | V2 | Comp: `MirBuildTest`, `MirModelTest`, `InstanceCollectorTest`; goldens equal | +60/−40 | Owner (R3-6 fixes the contract), R (front reviews the Binding hunk) | C |
| V4 | Accessor result premise | A getter's result well-formedness is a premise of its body only with the read site's result-premise check (G214, BU-26) | V1 | Comp: `PropertyBindingTest` rows two-leak-use, f1-computed | +60/−20 | Major, R | C |

**Stream I: inference**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| I0 | Revision B and B3a (held) | Held by the owner (PLAN 5b1777f34, 2026-11-03, 「根本的な解決は後回しにする…」; `held/bu104-specb-A` 5ee090855, `held/bu111-b3a-A` ea0187d75, B3a done and ACCEPTED there) after revision B's (A) round did not converge (its third review REVISE, R2-1 and R2-2); absorbed into I2: I1 takes their rows and probes (`bu\spec-b\review-probes\`, `review-recheck\`, BU104's gate list), I2's design takes the group review's Re-check 2 as evidence; the pushed `claude/` branches stay unmerged | the owner's hold | — | — | — (absorbed) | A |
| I1 | Problem statement and acceptance tests (test author) | SPEC 10.5, 10.8, 14.9.2 rows from the ArgumentKinds and ResultPropagation classes, revision B's list, G335's depth theory (8/16/32 linear), G383's counters (ClassifiedByCall 790, ConflictWaits 53, the Literal survey 31, PartDefault 18 → 0), G159 (2) (3), G152, G356, G378, G388, G390, G391, G360, G361, G275 (2), each with its twin; cost rows (Calls 32/512 cold and warm, warm 0 B) | I0 | Comp (fail-first where behavior changes) | t+400 | — (the planning session checks the tests) | B |
| I2 | `InferCtxt` (a family: §5.0) | TyVar, LenVar, IntLitVar, FloatLitVar, OriginVar (evidence only), union-find with an undo log, `Snapshot`/`Rollback`; `SlotSolution` a view; join states as constraints with one `Awaiting` fact per join; deletes the private states of `ArrayInference`'s holes, `ShapeEvidence`, `InferenceBoundaries`, `ResultSurvey`, `ResultEvidence`, Diagnostics' unifier, `SourcesAwait`/`HoldsWaitedResult`/`BindWaitedSources`; SPEC rules kept as constraints (`_` holes, mixed Range, §10.8.1's fixed-binding check, §7.3.1's correlation) | I1, D1 | I1's tests; bp where no row moves; cost rows; `InferenceSlotScalingTest` | +850/−2,500 | Full, FP2 (RQ6) | C |
| I3 | Positions through `InferCtxt` | One rule per unit (each ≤ 1 h): Result-position aggregate children (G388), nested argument joins after B3c (G390), one owner of evidence-bound slots for calls and constructions (G391), the solution as the meet of covariant bindings whatever the traversal order (G378), an Item at a slot an explicit Type argument or expectation fixes (G356; a SPEC reading for it1 first) | I2 | I1's rows per unit; the generated gate | +150/−200 | Major each, R | C |
| I4 | `ExpansionPlan.Evaluated` from its producer | The evaluated argument position recorded by PropertyCalls and Ranges; `ExpansionPlan` and `Binding.Interpolation` stop testing `EvaluatedKoto` (precedes P11) | — | Comp: `MirBuildTest`, `HirColumnsTest`; bp | +40/−60 | Major, R | B |

**Stream C: Contract solver**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C0 | Census and prototype (no commit) | G170 C2's `census-c2.tsv` re-measured: judgment entries (PLAN10 counted 15), cycle guards (8), fixed points (4), premise readers, identity comparisons outside the judge (G302's 127 sites), proof scopes (BU-22), per-request environment rebuilds; the limits (4,096 rounds, 65,536 items) on the scaling classes; prototype: `Prove(goal, env)` memo over the existing supply for one goal kind, warm Bind and the scaling rows measured | RQ3 | Evid | 0 | — | A |
| C1 | Problem statement and acceptance tests (test author) | G170's `(value, value)` and S5Probe rows; G302's list (G294 s1–s4, fa1/fa3/fa5, ar15, an1/an1c, rq1/ad1, pr1, pi1/pi3, k1–k4c, sh1/sh3, ef1–ef3, oi1, o1/o2 with twins; sp2, x2/x4, x11); G180 stale0–2, h4, h6; G234's effects row; G172's PA/PB in both orders and `alt`; G207's three readers; G200/G201/G202/G208; G365 hl5; G369 mx2 and mx2-call; G327; G328; G187; G355's identity Places (with the middle's builder row); SPEC 8.2.3's `cyclic`; the scaling classes and BindingBenchmark's Contracts, Capabilities, ConditionalConformances scenarios; cost rows | C0 | Comp (fail-first) | t+500 | — | B |
| C2 | `Enumerate` (G170 C3; a family: §5.0) | Demand-driven derivation over `ViewOf(scope)` with memo and one cycle rule (nominal conformance inductive Unknown, structural capabilities coinductive); deletes the eager expansion (`ExpandContractPremises`, `ExpandScopeContractPremises`, `AddContractPremises`), the availability routing (`AvailableConstraintFact`, `AvailableContractPremise`), the rounds' quadratic case and `ResolveConformance`'s on-demand verification staging, `Evidence.Invalid`; the limits in IMPL 21.3.2 and A.4 (C1's OQ2 numbers confirmed by this unit's gate) as one Resource record with the limit as an argument (C1's OQ1; a Problem code after E1) | C1, C8 | C1's tests; cost rows; `OriginProofScalingTest`, `TypeDagScalingTest` | +500/−900 | Full, FP2 | C |
| C3 | Type identity (a family: §5.0) | One judge (`NamesReference`/`SameConformanceType` under decision 10's closed completion, TypeIdentity's classes; bu65's round 5 design is the design-first candidate), the declaration-frame crossing (G302: one instance-and-selection member replacing `ConformanceByDeclaration`), the ArchitectureRulesTest guard over identity comparisons outside the judge (RP6), SPEC 8.2.3's `cyclic` reported at the declaration (G170 OQ-a), identity Places published to HIR (G355; Y5: the middle's builder reads the resolved receiver Type and deletes `MirBuilder.Identical`'s `Unsupported_Kd`; `HirTables.IdentityPremise` goes, BU-72) | C2 | C1's identity rows; snapshot classified; the generated gate; `b-identity-places` ledger row closed | +400/−300 | Full, FP2 | C |
| C4 | Associated value inference (G172) | The G172 plan's design C: one value identity, evaluation on demand through C2's view, frozen views' "depends" (B's Q3), order independence; `InferAssociatedTypes`' batches and `ResolveAssociated`'s withholding deleted | C2 | C1's G172 rows (both orders); `AssociatedInferenceTest`; Bench `--associated-inference` | +300/−500 | Major, D+R (the G172 plan §4 is the design) | C |
| C5 | Proof scopes | Each reader combining proof scopes (a call's re-check, a function Item reference's proof, conformance verification) is a scope; a limit settles where no longer combined (G207); `Binding.Calls.cs` and `Binding.FunctionTypes.cs` split first where over a limit (after P2) | C2, P2 | C1's G207 rows | +80/−60 | Major, R | C |
| C6 | Capabilities and summaries through the memo | a: the growth guard compares `Height` and ends in a Resource record (G200; BU-71); b: projections and owner Types (G201, G202, G208, G365, G327); c: summaries' inherited arm published as a source path (G328) and the exact re-entry test (G369, c″'s settle exception removed) | C2 | C1's rows; `ComponentSummaryTest`, `IntrinsicCapabilityTest` | +200/−250 | Major each, R | C |
| C7 | `AvailableBounds` as a pure query | `AvailableBounds(identity, type, env)` reads premises only (G234); `EffectDeclarations` keep; `EffectObligations` reads conformance validity (R4-M23, landed by H5); after H5 | H5, V2 | Comp: `EffectBoundDeclarationTest`, `EffectBoundImplementationTest`, `CallableEffectBoundTest`; snapshot | +60/−150 | Major, R | C |
| C8 | Walk sharing and depth | One rule: a walk over an Origin-carrying DAG enters each distinct (part, mode) once, with a per-query arena so a nested query evicts nothing (G309-R, BU-64's limit, R4's bu90 N1); `TypeMemo` adopted where a reproducer reaches a shared part (G140 U3); recursion on Type height bounded (G348, with D3); done when n = 40 rows for the three forms are linear | C0 | `TypeDagScalingTest` n = 40 rows; cost rows | +150/−100 | Major, R | A/B |
| C9 | Deletions after C2–C4 (a family: §5.0) | The 15 entries become `Prove`, the 8 cycle guards one, the 4 fixed points the memo; `BoundContracts`, `ContractAgreement`, `ConditionalConformances`, `InheritedConformances` fold into `ImplTable` rows (V2) and the solver; `Constraints`/`ConstraintValidation` restructured by responsibility | C2–C4, V2 | Comp: Session; bp | −1,500 | Major, R | C |

**Stream P: CallPlan pipeline**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| P0 | Census and prototype (no commit) | The 12 call forms and their entries, the 8 implicit state fields, `TryCandidateCore`'s 12 parameters and 6 outs, `ValidateFixedConstruction`'s second pass, the implicit constructors' three silent selections and rollback, `StrictBestCandidate`, winner re-evaluation sites, the 13 synthetic-Koto files; prototype: direct calls through `CallContext` and `CandidateResult[]`, BindingBenchmark Calls 32/512 | RQ3 | Evid | 0 | — | A |
| P1 | Problem statement and acceptance tests (test author) | The call classes (the census names them), `ApplicabilityChainRegressionTest`, G379's clsel/itsel/clselctl1/itselctl1/clfv, G351, G377 w2/w6, G265's value-call rows, G386 cl5, G274 failsrc, G51's Never operands, G356 gt1/it1, R1e's `overload-parameter-type` mutation, the T1–T4 examples from S1's text, the generated gate list; cost rows | P0, SP1 (text) | Comp (fail-first) | t+500 | — | B |
| P2 | `CallContext` and `CandidateResult[]` | The 8 implicit fields one explicit argument; candidates evaluated once into an array; the winner's result reused; rejection facts computed on the failure path by a separate function (bp) | P0 | P1's classes; bp; cost rows | +200/−300 | Major, R | B |
| P3 | One pipeline for the 12 forms (a family: §5.0) | Normalize each form to a signature view (`FnSig`) → prepare arguments once (ArgumentKinds) → evaluate side-effect-free (I2's snapshot) → rank (`StrictBestCandidate` absorbed) → complete waiting arguments → fixed-binding check → §7.3.1 correlation → `CallPlan`; absorbs `BindCallCore`, `ValidateFixedConstruction`'s second pass, the implicit constructors' silent selections and rollback, `Binding.Enums`' own path (a synthesized constructor signature), the PropertyCalls/BaseCalls/ContractCalls/ValueCalls/CallableSelection/ObjectCreation/BoundIteration entries | P2, I2, C2, V2 | P1's tests; bp where no row moves; cost rows; `CallableRegionScalingTest` | +900/−3,000 | Full, FP2 | C |
| P4 | Origins out of applicability (T1–T4) | After SP1 lands: T1 declared Origin clauses of Type arguments judged after selection; T2 erasure and Function-value fits structural-only before selection (G379: `ErasureSignatureFits`, `FunctionReferenceApplicability`; the per-call Origin inference split out of the fit); T3 expected-result inference without an Origin fit; T4 the whole-contract comparison symmetric for body inference variables; one unit each | P3, SP1, H5 | P1's T rows; the generated gate (named moves); snapshot classified | +200/−400 | Major (inside P3's recorded design; the rule is SP1's), R | C |
| P5 | Requirement calls through `TyCtxt.Witness` | The materializer `InstantiateRequirementCall` and `RequirementInstances`' copies deleted (G157 (5)); `SelectedRequirements` and `RequirementItems` read the view; `RequirementImplementation` and the legacy comparison composition deleted (G186, R5-M68; after H11); `ComparisonPlan(CallPlan)` → `TyCtxt.Comparison` by Type, `CallPlan.TupleOperator` kept as the builder's fact (the R5 and R6 plan's RP15); G157 (16) | P3, H11 | Comp: comparison, format, iterator and fixed-array families; `MirBuildTest`; bp | +80/−500 | Major, R (middle reviews the builder's read) | C |
| P6 | `StorageSubst` → `SubstOf` (R5-M36) | The CallPlan form of `InstantiateStorageType` applies the call's `Subst`; readers: requirement instantiation, forwarded calls, formatting implementations; `[Transitional("R7")]` removed | P5 | Comp: closure and storage classes; bp | +20/−120 | Major, R | C |
| P7 | Enclosing-instance chain (G149, R5-M157) | A call's `Subst` carries the enclosing instance chain (each enclosing generic scope's binder: function, Closure context, declaring Type, a Field initializer's Type); `TyCtxt.EnclosingFunction` gives every nested definition its holder; the collector's `CarriesInstance` reads it; the `EnclosingInstance` capability row deleted; G300's per-use opening by the same chain | P3 | Comp: `InstanceCollectorTest`, `GenericClosureIdentityTest`, `NestedGeneric*` families native; STATUS rows | +250/−150 | Full, FP1 (back reviews the collector hunk) | C |
| P8 | Function references and closures | the function reference's remaining route through `RequireConstraint`'s path (G396 (9); G351 is closed); a Closure whose returned value fails binds its result Error (G274); a parenthesized Closure at a Function-typed parameter (G386); an Item at a slot fixed by an explicit Type argument or expectation (G356); the adapter prints a Closure Type's differing parts (G343; with E) | P3 | P1's rows | +120/−150 | Major each, R | C |
| P9 | Operators, elements and updates | One inline-part writability rule from the walk (G164; `WritableRoot` by `LayerAuthority`); a user index through a pair layer selects through the layer (G179); a temporary's inline part and raw adaptation targets (G203); the shared-path rule reaches setter and compound forms (G331); an update through a computed Property with another input Type (G323, G322); `LayerCases` shares the per-layer function (G181 (2)); property rows only for custom accessors (G296); the operator result context propagates (G51); valid forms the rule does not yet cover end in a located `Unsupported_Kd`, never a Language error | P3, H5 | P1's rows; the generated gate | +200/−300 | Major each (G296 Minor), R | C |
| P10 | Value calls publish their argument operations | A value call records its implicit Borrow arguments as a named call does (G265; the middle's `BorrowSource` reads them); G267's items (Capture's one Type-source fact per symbol, Dictionary literal twins, `takeDI`, the `let` Closure's hidden conflict) | P3 | P1's rows; `MirBorrowsTest` witness rows | +80/−60 | Major, R | C |
| P11 | Synthetic Kotos deleted (R5-M9) | `EvaluatedKoto`, `SyntheticKoto`, `FormattingKoto`, `RequirementCalleeKoto`, `TryKoto`'s arms; Binding's 13 files read plan rows; Parsing −400 (H) | P3, P5, I4, H10 | Comp: Session; semantic snapshot HIR and MIR equal | +100/−600 | Major, R | D |

**Stream E: Problem records**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| E0 | Census and prototype (no commit) | The 464 `Fail(` sites by family (`FailExplained` 64, `FailMismatch` 32, `FailWrite` 14, …), the 17 `BindingFailure` mapping sites in `Binding.Diagnostics.cs`, the consultation frames, the 15 Koto-keyed diagnostic tables, `BindingIssue`, the old Analysis's readers (`FailureCauses`, `IsDerived`), Hover and LSP readers, the `Binding.*` names in `DiagnosticRequirement.tinyhand`; prototype: a `Problem` recorder for `TypeMismatch` beside `BindingFailure`, snapshot 0 | RQ3 | Evid | 0 | — | A |
| E1 | `Problem` and its recorder | `Problem(SourceRef Subject, Requirement, Condition, Context, Code, typed facts, related, repair kind)`; a node's own problem makes its value Type `Error`; several independent problems per node by condition number (ends "first failure only": G155, G317, G324, G319, G333); dependence by rule W through `DerivedFrom` keys (Q13); the adapter renders from facts; lands beside `BindingFailure` with a one-way mapping (snapshot 0); one representation by meaning: the record is DIAGNOSTICS §4.1's model (check key: Subject, Requirement, Condition, Context), its typed facts `MirFact`'s representation (one typed-fact vocabulary for Binding and the MIR analyses), and it converts to Core's published `Problem(Code, SourceRef)` as `MirReport` does; the design decides whether `MirProblem` and Binding's record are one type with a phase payload (Body, Views) or two types over one fact vocabulary, and says why | E0 | Comp: `DiagnosticContractTest`, `DiagnosticOwnerTest`, `DiagnosticPrecisionTest`; snapshot 0; cost rows (warm 0 B) | +400/−100 | Full, FP1 (a representation read by the adapter, Hover, LSP and, until H10, the old Analysis; RQ6) | A/B |
| E2 | Family 1: names, Types, declarations | Their `Fail*` sites record Problems; explanation rows become typed facts or go (D3: no re-proof for Notes: `SimilarNames`, `GenericDefaultNote`, `SomeBindingFits`); snapshot differences classified by code (RP19) | E1 | Comp: the family's classes; snapshot classified | +100/−500 | Major, R | B |
| E3 | Family 2: calls, arguments, conversions | As E2; `FailMismatch`'s `ComparisonFailure` fact (R4-M155) is the Problem's fact | E1, P2 | as E2 | +100/−500 | Major, R | C |
| E4 | Family 3: Origins, acquisition, lints | As E2; `OriginDiagnostics`' migration hints go (D3); `ContainerReferences`' Advice goes | E1, O1 | as E2 | +100/−500 | Major, R | C |
| E5 | Delete `BindingFailure` and the switches (a family: §5.0) | `BindingFailure`, the code switches, `ReportIssue`, the consultation frames, `IsDerived`'s special cases; `DiagnosticKey`'s subject a `SourceRef` (no Koto reference identity, `DiagnosticFact.cs`); the requirement enumeration closed in `DiagnosticRequirement.tinyhand`; after H10 | E2–E4, H10 | Comp: Session; snapshot 0; ArchitectureRulesTest zero-reference row | −900 | Major, R | D |
| E6 | Partition names (§16-7) | No rename (RQ15); a DIAGNOSTICS and SPEC §23 wording check only | RQ15 | Doc | 0 | Doc | D |

**Stream O: Origins residue (after H11 and G318)**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| O1 | Binding stops judging body relations (a family: §5.0) | The C1-F4 view rows are Binding's product; delete `LocalRegions` (two files), `CallRelations`' judging part, `OriginProof`'s judging part (the premise oracle `UniversalRelations` and one WF enumerator stay), `ResultPremises`' six modes, the per-request rebuilds of `OriginPremises`/`OriginPremiseClosure` (one closure per body), the candidate bound Begin/Keep/End and `originStateVersion`; OJ1–OJ7 stay (signature set, oracle, Origin Constraints, whole-contract comparison, principal assignment of selected calls, placeholders, atom classes); STATUS's R7 sentence closes | H11, G318, H5 | Comp: Session; the generated gate; the A5 class set (RP14); snapshot classified | −2,500 | Owner (A5 plan §7, R4-M35 OD2), R (middle reviews A5's side) | D |
| O2 | Local slot regions (G194) | A `let` slot's unannotated Origin is an inferred region constrained by later uses (SPEC 15.6.5, 15.4.4) through I2's OriginVar; `SolveLocalBounds`' declaration-time candidate goes; the census of `let` borrow locals in the library and milestones first (the owner's OQ-1 (c)) | I2, O1 | Comp: `LocalOriginClauseTest`, the audit-origins S3 demo; the generated gate | +150/−300 | Full, FP2 (the owner's OQ-1 (c)) | D |
| O3 | Meets, pending ends, declaring bodies | A callee parameter Type's meet of its own Origins judged with fresh regions at the call (G195); a fit through a meet with a pending atom judged at classification (G257); a declaring body extends `EnclosingAtom`'s chain (G230 (A), BU-56's interim removed); a result-only call Origin's display (G239); G216 (b) and G215 with the middle's A5 rows | O1 | Comp: the named rows; the gate | +200/−150 | Major each, R (middle for the A5 side) | D |

**Stream T: TyCtxt**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | `Apply` pure (G128, the R5 and R6 plan's PD30) | Substitution without storage preparation; the storage verdict (Prepared/Rejected/Limit) a declaration query over the instantiated storage, published by `TyCtxt.Instance` (BU-36, BU-38 readers unchanged); the `StorageNesting` capability row deleted; a Type past the bound is the collector's Resource record (R5-M170; RQ12); G242's `TyCtxt.Instance` documented | RQ12, V2 | Comp: `TyCtxtTest`, `InstanceCollectorTest`, `GenericStorageEmissionTest`; the G128 reproducer; cost rows | +100/−300 | Full, FP1 (back reviews the collector) | C |
| T2 | Root helpers into `TyCtxt` and `AdtDef` | Each of `ReferenceTypes`, `ScalarTypes`, `ElementAccess` (Binding's reads), `ComparisonTypes`, `FormattingTypes`, `SlotTypes`, `SharedReadTypes`, `DefaultParameters`, `StaticScalar` becomes a `TyCtxt` predicate or an `AdtDef` fact with one owner; the Backend's reads (R5-M65, R5-M148 rows) move to the same predicates (ARCH dependency rows updated); `StrictBestCandidate` with P3, `MatchTypes` with H11; the root files deleted after H10 and H14; G289 (1)'s rename | — (reads) / H10, H14 (deletions) | Comp per helper's classes; bp | +300/−1,000 | Major per helper (Minor where one owner exists), R (back reviews its reads) | A/B (reads), D (deletions) |
| T3 | One Type walk | `Relate(actual, expected, variance, sink)` as the one structural walk (TypeRelations and `.Walk`, the fits' shared parts), the memo of C8; the remaining mutable caches on `BoundType` (census) removed | C8, P3 | Comp: `PairVarianceTest`, `GenericAdaptationOriginTest`, the fit classes; bp; cost rows | +150/−400 | Major, R | C |

**Stream Y: HIR columns and places (middle, front reviews)**

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Y1 | Inline column writes (R2b-D2, BU-81) | `AdjustChildren` writes `Op`, `Res`, `Completion`, `Adjust` and PlaceShape at the node's bind in the Final pass; the publisher walk and `PublisherUses` (R4-M142) deleted; `HasFacts` checked at the write; `HirTables.Broken` unchanged; the lint walk reads columns | — | Comp: `HirColumnsTest`, `MirBuildTest`, goldens; semantic snapshot HIR equal; cost rows (publication about 2 % of warm Bind, R4-M142) | +150/−400 | Major (the publication contract is lasting and unchanged, R2b-D2; only the walk goes), R | B (after the flip group's window: R6-check's H1 edits `Binding.Check.cs` and the publication) |
| Y2 | One place-use fact (G251 (3)) | `Binding.PlaceUse` (Shared/Exclusive/Consuming per Place use, a column) read by `ClosureEffects.NeedsExclusively` and HIR's `ExclusiveUse` (and `EffectBounds.PlaceAccess` until H5); the divergences (view upcasts, `AdaptationBorrow`, raw Places, G251 (1)'s raw-pointer Exclusive rule) closed by the one fact | Y1 | Comp: `ClosureReceiverClassificationTest`, `HirColumnsTest`, `MirBorrowsTest` closure rows; the gate | +120/−200 | Full, FP1 | C |
| Y3 | Adaptation modes and the discard's Type | `HirAdaptation`'s PayloadProjection, BaseBorrow, StorageProjection, ReferenceRead state their mode (G147 (2)); a discard's adapted Type published as a `PlanRow` column (G233) | Y1 | Comp: `MirBuildTest`, `MirBorrowsTest` discard rows | +60/−80 | Major, D+R (both are columns of the Adjust row, no new `HirAdaptation` member, ARCH's vocabulary unchanged; a design that needs a member stops for the owner) | C |
| Y4 | Koto forwarding properties deleted (CR-A7, R5-M9) | The 36 properties and `HirTables.HasCurrentBinding`'s forwarding after H14 and the Binding reads' move (N5, T2, E); ARCH's Syntax restriction extends to Binding; `UnwrapParentheses` restricted to Parsing and Binding | H14, X1's reads at 0 | Comp: Session; bp; ArchitectureRulesTest row | −300 | Major, R | D |
| Y5 | Identity places (with C3) | The builder reads the identity-resolved receiver Type; `MirBuilder.Identical`'s `Unsupported_Kd` and R5-M84's interim go; the ledger row `b-identity-places` closes; STATUS | C3 | Comp: `MirBuildTest`, `TypeIdentityTest`, `IdentityAcquisitionEmissionTest` and `GenericIdentityAdaptationTest` native | +30/−60 | Major, R | C |

**Stream SP: SPEC revisions (texts to the owner, RQ10)**

| ID | Unit | Change | Prereq | Verification | Class | Phase |
| --- | --- | --- | --- | --- | --- | --- |
| SP1 | T1–T4 | SPEC §10.1 step 5 or §8.1.3, §10.8, §15.3.4: declared Origin relations of Type arguments judged after selection; erasure and Function-value fits structural-only in applicability; local annotation inference variables as constraints in whole-contract comparison; examples, tests, SPECJ, INTEGRATED row | RQ10 | Doc + tests | Explicit revision | B (text), C (P4) |
| SP2 | SPEC 8.2.3 identity | Verify the text covers `cyclic` and decision 10's closed completion as C3 implements them; else a report, not a rewrite | C3 | Doc | Explicit revision if needed | C |
| SP3 | §8.4.4 OCC | Keep `Unsupported_Kd`; the revision proposed at R9's exit (RQ14) | RQ14 | Doc | — | D |
| SP4 | §23 names | None (RQ15) | RQ15 | Doc | — | D |

**Stream X: R7 exit**

| ID | Unit | Change | Prereq | Verification | Phase |
| --- | --- | --- | --- | --- | --- |
| X1 | Deletion sweep | `[Transitional("R7")]` 0 (12 at the base; about 48 after H14 re-marks the 36 forwarding properties from R6 to R7, R5-M9); ArchitectureRulesTest zero-reference rows: the Koto forwarding properties, the synthetic Kotos, `BindingFailure`, `HirPublisher`, `StorageSubst`, `InstantiateRequirementCall`, the root helpers in Binding, Koto-keyed tables in Binding, `Supports*` gates; capability rows `EnclosingInstance` and `StorageNesting` gone | every R7 unit | Comp: Session | D |
| X2 | Measurements and records | Bind cold, warm and retained against R7's start and the R2a/R5/R6 series; the scaling rows; `artifacts/benchmarks/r7-exit-areas.md` (sizes, hotspots, duplication); exceptions and Transitional declarations reviewed with the owner; STATUS, CODEMAP, ARCH, DIAGNOSTICS; the end-to-end Binding review (one reviewer reads each component's contract against AGENTS' structure rules) | X1 | Doc + Bench | D |
| X3 | The acceptance list | The generated gate's named moves of R7 (T1–T4 and the recovered valid forms) as one list to the owner (the OD13 pattern) | X1 | Evid | D |

### 5.2 R8 library algorithms in Kimigayo (back, unless marked)

Each family lands in its order (PLAN10 §5.8): Array → Dictionary → strings and built-in witnesses → Text, Buffer, Window, Writer →
the interpolation adapter → the `ResolvedRange` `for`. Common to every family unit: the body is accepted by the old Analysis until H5
(else the unit waits), the O2 kernel covering it stays within variance (an address-matched control first, R5-M147), the allocation-count
tests hold (`DynamicArrayCostTest`, `DictionaryCostTest`, `SliceCostTest`, `Utf8CompilationCostTest`, `WorkloadCostTest`,
`NativeAllocationAudit`), the family's native fixtures are equal, warm 0 B holds, the A4 summary of each body agrees with its published row
(H4's successor), `KimiLibraryShapeTest` and `KimiLibraryBenchmark` run, and the Bind cost rows are recorded (the library grows).

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| L0 | Baselines and census (no code but kernels) | O2 corpus A=A on R8's start sha; a `Strings` kernel (comparison, destruction, formatting of integers) added if the E7 kernels do not exist; the allocation-count inventory; Bind cold and warm with the library (BindingBenchmark, `KimiLibraryBenchmark`, the census harness); the table of the 53 Body-disposition kinds (legacy row, library callers, generated shape, covering kernel, the Kimigayo form planned); the 30 `.ll.in` defines with their disposition (delete: Buffer 5, Format 15; keep: Ryu 2, float entry 2 (reduced), Windows 6); the SPEC and utf8-formatting texts to revise; prototype: `Array.append` in Kimigayo on a branch, both pipelines, O2 A/B against the legacy row | RQ4 | Evid, Bench | t+150 | — (R for the kernel) | A |
| R1 | Representation structs | `internal` lang-item structs `ArrayRepr<T> { data, length, capacity }`, `SliceRepr<T>`, `StringRepr` with `#Layout("C")`; the Dictionary handle's seven words already `DictionaryStorage.Handle` in Kimigayo; `Layout.Of` derives the handle rows from the structs' placement (`Layout.Rows`' Slice/Array/Dictionary rows and `LengthWord` deleted); the library reaches a representation by SPEC §5.4's raw conversion (Appendix E's pairs); the managed Types keep their Type semantics (R8-S1's text) | RQ11, SP1's text | Comp: `LayoutTest` goldens (size, alignment, stride and word offsets unchanged), `KimiLibraryShapeTest`; the handle's IR type text changes once (a declared `.ll` change, the goldens updated), every native family's outcomes equal | +150 kimi / −150 C# | Owner (PLAN10 §5.8, approved 2026-10-10 and recorded with its adoption): design → implementation → review, the prototype per RP13 | B |
| A1 | Array operations | a: `reserve`, `append`, `setArrayLength` (growth) over `ArrayRepr<T>` with `Raw.*`, `Storage.tryAllocateBytes`/`transferBytes` (the library's name; the intrinsic is `MoveBytes`), `stride<T>` (new; PLAN10 §5.8's name); b: `insert`, `remove`, `pop`, `swap` (moves); c: `clear`, `shrinkToFit`, the Array glue (`destroyValues<T>`, the first producer of `IntrinsicId.DestroyValues`, G126) and `capacity` as a Field read (`IntrinsicId.Capacity` deleted: 32 members → 31); `LegacyRuntimeHelpers.Arrays.cs` (719) and the Array `LegacyRow` items deleted; `ArrayBuffer`'s one-allocation literal path (R5-M129) keeps its aggregate row over the struct | R1 | Array and ArrayLiteral kernels; `DynamicArrayCostTest`; fam Array*; `MirEffectsTest` rows | +250 kimi / −800 C# | Major each, G:R8-array (back + middle for the A4/A5 rows) | B/C |
| K1 | Dictionary | a: the storage kinds (`StorageBorrowDictionary`, `…Exclusive`, `StorageOwnDictionary`, `StorageKeyAt`, `StorageValueAt`, `StorageDictionaryLayout`) as Kimigayo over `Slot<K, V>`; b: `placeEntry` and the literal (the middle's G255 C2: each entry placed by one call over the literal's borrow); c: `clearEntries` and the glue; `KimiLibrary.DictionaryStorage.cs`, `TyCtxt.DictionaryStorage`, `LegacyRuntimeHelpers.Dictionaries.cs` and the Storage kernels deleted | A1 | Dictionary kernel; `DictionaryCostTest`, `DictionaryLibraryTest`; fam Dictionary* | +200 kimi / −450 C# | Major each, G:R8-dictionary (middle for K1b) | B/C |
| S1 | Strings | `compareBytes` (new; PLAN10 §5.8's name) in Kimigayo (intrinsic only if the Strings kernel regresses, PLAN10), `string is Equatable/Comparable` witnesses as bodies, the string's destruction through the runtime's release kinds unchanged; `LegacyRuntimeHelpers`' string routines and the operator table's string column (`FunctionCodegen.Strings`) deleted | L0 | Strings kernel; `StringComparisonEmissionTest`; fam String* | +120 kimi / −300 C# | Major, G:R8-strings | C |
| S2 | Built-in formatting witnesses | Integer, char and bool formatting in Kimigayo (`__kimi_format_integer`, `__kimi_decimal_div10` go), i128 through `formatInteger128` (interim, RQ16), floats through `formatFloat`; `BuiltinFormat`'s shim and `FormatKind` deleted; arithmetic and comparison shims stay (RP9) | S1 | Interpolation kernel; `Utf8ConsoleTest`, `Utf8FormatTest`; fam Utf* | +150 kimi / −250 C#, −200 ll | Major, G:R8-format | C |
| W1 | Text, Buffer, Window, Writer | a: `FixedBuffer`/`HeapBuffer` (12 kinds; `reserve` bodies end the `BodylessWitness` row's buffer half); b: `WriteWindow` and `Utf8Writer` (6 kinds; `TextWriter` stays `ErasedWriter`); c: `Text` conversions (7 kinds) and `WriteLineUtf8`; `Utf8BufferRuntime.ll.in` (431) and `Utf8FormatRuntime.ll.in`'s routines not needed by W2 deleted; `WriterKind` deleted | S2 | `Utf8CompilationCostTest`, `Utf8TryWriteTest`, the writer classes; fam Utf*; Interpolation kernel | +400 kimi / −700 C# and ll | Major each, G:R8-text | C |
| W2t | Acceptance tests for W2 (test author) | `Utf8ConsoleTest`'s 33-byte stack and no-heap rows, utf8-formatting §6.2's counts per form, the Interpolation kernel's checksum, the bounded and unbounded forms, `$tryWrite` into a cleared buffer; fail-first where the adapter changes a count | W1 | Comp | t+120 | — | C |
| W2 | The owning interpolation adapter (a family: §5.0) | `FormatBuffer`, `FormatSegment`, `FormatFinish` replaced by a library adapter: each formattable declares its bound (a Contract constant or associated length; the design decides), the builder's expansion (middle) sizes a fixed buffer when bounded and within 1,024 bytes, else a `HeapBuffer`; `Console.writeLine` a Kimigayo body over `writeStdout`; `FunctionCodegen.Interpolation`, `ModuleCodegen.Formatting`, `FormatBound`, `FormatEstimate` deleted; `IntrinsicId` 31 → 28 | W1, W2t | `Utf8ConsoleTest`'s 33-byte stack and no-heap rows, utf8-formatting §6.2 counts; Interpolation kernel; semantic snapshot MIR classified | +300 kimi / −600 C#, −400 ll | Full, FP2 (middle reviews the expansion) | C |
| W3 | `ResolvedRange` `for` | Range iteration through the ordinary Iterable path once the Range iterators are Kimigayo; the `ExpansionKind` Range row goes (RP18) | W2 | Iterators kernel; `RangeValueTest`, `IntegerRangeTest`; goldens | −150 | Major, R (middle) | C |
| U1 | `replace`, `exchange`, `swap` as bodies (R8-SP3) | Raw take and `Raw.initialize` bodies in `Intrinsics`; `FunctionCodegen.Updates` deleted; borrow checking recognizes them by lang identity (the published rows and `ReferentStores`, BU-42) | SP3's text | `WholeValueTest`, `MirBorrowsTest` update rows; fam Whole* | +60 kimi / −200 C# | Major, R (middle for A5) | C |
| G1 | Lang identification and the catalog | `#Lang`, `#Intrinsic`, `#CallerLocation`, `#Unsupported` recognized only in the embedded library (R7-N4's `AttributeKind`; the one fact "this module is the embedded library" is the mark `KimiLibrarySources` sets on the modules it loads, which the resolver reads); `KimiDeclarationId` (191) → the lang-item set; the catalog's Overload/Owner strings and the signature-only parse mode deleted; shape validation stays in `KimiLibraryShapeTest`; `CompilerFunctionKind` (76) and `IntrinsicDisposition.Of` deleted (RP20); the published rows keyed by lang item | A1, K1, S1, W1, R7-N4, SP2's text | Comp: `KimiLibraryShapeTest`, `CoreCatalogTest` (allocation), Session | +200/−900 | Owner (§16-9, approved 2026-10-10): design → implementation → review, the prototype per RP13 | D |
| G2 | Import witnesses | The collector publishes a `#LibraryImport` function as a requirement call's target (R5-M146's removal condition); `BodylessWitness` deleted | W1 | `ForeignEmissionTest`; fam Foreign* | +40/−60 | Major, R | D |
| I1 | The intrinsic table | `IntrinsicId` 28 (ARCH limit 34 → 28), `IntrinsicRows` for the 28, `LayoutNeed` rows updated, `FormatInteger128` kept interim (RQ16) | W2, A1c | Comp: `IntrinsicCapabilityTest`, `TestProtocolTest`, the Raw* and Abort* families; Session | ±60 | Major, R | D |
| L1 | One frozen library per process (G337 O3 with O2; §16-8) | Measured after W1 (RQ13's thresholds); if adopted: the library's semantic tables and MIR bodies built once per process, read-only (a thread-safety audit; `src/Kimi/Library/README.md` policy); removes the library's per-compilation HIR publication and Binding's library share | W1, R7-D2 (front), RQ13 | Bench: `--compiler-pipeline`, `--analyze-cost`, the census harness; `CoreCatalogTest`; Session | +500/−300 | Full, FP1 (front + middle review) | D |
| SP1–SP4 | Texts to the owner (RQ10) | SP1: SPEC §22.1 (representation structs identical to the managed Types' layout; private primitives and intrinsics as RP9's table), §22.4 (`writeLine` a body), §22.5.5, utf8-formatting §1.1, testing-profile, IMPL §21.2.2 and §21.5.3; SP2: §6.5 and §22.1 (the lang attributes as identification); SP3: §15.7 and §15.7.1 (the whole-value updates as bodies with the unsafe obligation); SP4: SETTLED's new entry; STYLE, LIBRARY, README conventions | RQ10, RQ11 | Doc | docs | Explicit revisions | B–D |
| X1 | Exit sweep and records | Session with `'*.ll'` at O0 and O2, the harnessed milestones and the CLI harnesses; O2 corpus every kernel against R8's start and the `49cb2b5d` chain; O0 recorded; the allocation-count tests; `[Transitional("R8")]` 0; `LegacyRuntimeHelpers*` deleted (zero-reference row); `.ll.in` Buffer and Format gone, Float reduced; the handle rows gone; `CompilerFunctionKind` gone; `IntrinsicId` 28; `artifacts/benchmarks/r8-exit-areas.md`; LIBRARY, STATUS, README, STYLE; the owner's review of exceptions | every R8 unit | Doc + Session + Bench | docs | — | D |

### 5.3 R9 remainder, moves and final documents (middle, unless marked)

| ID | Unit | Change | Prereq | Verification | Size | Class / Review | Phase |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F1 | One item loop | `Parser.cs` split by responsibility first (declarations, expressions, Types, blocks, recovery; behavior-preserving; it is over the token limit), then one item loop with a closed block-kind context replaces `ParseExecutableBlockItems`, `ParseExecutableItems`, `ParseBlockItem`, `ParseSingleBodyItem`, `ParseRequiredBody`, `ParseRequiredBlock`, `ParseFunctionBlock`, `ParseDeclarationDirectiveBody`, `ParseRequirementBody`, `ParsePropertyAccessorBlock`'s loops | — | Comp: the syntax corpus, `SyntaxDiagnosticTest`, parse classes; snapshot 0; `ParseBenchmark` pinned A/B | ±300 then −300 | Major, R (front reviews, RP17) | C |
| F2 | One type grammar entry | `ParseType(reader, context)` with one closed grammar context (declaration slot, constraint subject, argument, Origin-bearing position) replacing the two `ParseType`s with three bools, `ParseTypeOrigin`, `ParseTypeConstraint`, `ParseConstraintSubject`, `ParseTypeArgument` | F1 | as F1; `ArrayTypeRecoveryTest`, `ConstraintRecoveryTest` | −150 | Major, R (front) | C |
| F3 | Attribute context | `TokenContext.AttributeKoto`'s implicit chain replaced by attributes attached by the declaration parser (R7-N4's `AttributeKind`); `UnavailableFeature_Kd` and the same-line modifier recognition kept | N4 | as F1; `UnknownAttributeCertificateBindingTest` | −120 | Major, R (front) | C |
| F4 | Lexing and helper duplicates | One keyword table for the Tokenizer and the Parser; `CompilerHelper`'s unused predicates; `IdentifierHelper`; `Misc/Target` after G363 | — | Comp: `KeywordClassificationTest`, the lexer classes; bp | −100 | Minor/Major, R (front) | C |
| F5 | Phase sequence in `Compilation` | `Project.cs`'s hand-written phase order folded into `Compilation`'s phase list (census first; Pipeline and Generation already own most) | — | Comp: `PipelineSwitchTest`'s successor, CLI harnesses | −80 | Major, R (back for Generation) | C |
| M1 | Documentation out of the compiler | `src/Kimi/Documentation` (namespace `Kimi.Documentation`): the Markdown parser, document, HTML, links and Unicode data (about 3,700 lines); the association (`DocumentationSource`, `.Transfer`, `Kotonoha.Documentation`, `CodeContext.Documentation`) stays in Core as the compiler's own input record, which Documentation reads (the design settles the direction: Documentation reads the compiler, never the reverse); about 33 files reference its types; README and CODEMAP | — | Comp: documentation classes, `DocumentationMarkdownBoundaryTest`, Hover classes; `DocumentationMarkdownBenchmark`; bp | moves 4,388; +50/−30 | Major, D+R | D |
| M2 | Toolchain out of the compiler | `src/Kimi/Build` (namespace `Kimi.Build`): `NativeToolchain`, `.Verification`, `ToolchainResolver`, `ToolchainIdentity`, `EmissionArtifacts` (publication), `ArtifactFiles`, `ArtifactPaths`, `Kernel32Imports`; `Compilation.Generation.cs` reads no Build type (Build calls `Compilation.Generate`); `RuntimeAbi`'s and `WindowsProfile`'s references checked; the structure-limit exemption ends: members split by responsibility in the move; `DependencyLock` and the commands re-pointed | — | Comp: `NativeToolchainTest`, `EmissionArtifactsTest`, `test-cli.ps1`, `test-manual-build.ps1`; bp | moves 1,364; +80/−40 | Major, D+R (back reviews Generation) | D |
| B1 (B+M) | Link manifest external declarations (IMPL §20.8.3) | `ModuleCodegen.Imports` publishes the module's external declarations (symbol, dllimport) with the generation result; publication writes them into the link manifest; `NativeToolchain.ReadExternalDeclarations`'s IR re-parse deleted; IMPL §20.8.3 revised (RQ10's procedure) | M2 | Comp: `NativeToolchainTest`, `ForeignEmissionTest`; `test-manual-build.ps1` | +80/−40 | Major, R | D |
| B2 | Toolchain failure classification (G345) | `IsToolchainFailure`'s exception list replaced by the boundary's rule: a compiler exception outside generation under `Project.Build` is `CheckFaulted_Kd` with its fault path (G330 §1.2) | M2 | Comp: `NativeToolchainTest` fault rows | +30/−20 | Major, R | D |
| C1 | Final documents | COMPILER_ARCHITECTURE (ledger R0–R9 closed, "Rules during the migration" retired, the canonical components and dependency rows current, the vocabulary table final), CODEMAP (owners, entry points, representative tests; the main-path sentence: Syntax → Binding → MIR → Backend → Build), DIAGNOSTICS (the Problem model; §4.1's "analysis-specific failure enumerations remain" revised; §11's findings closed or re-recorded), VERIFICATION (fixture policy, the shared-base section; `-Pipeline` gone at H12), STATUS (boundaries after the reduction; D2 regressions recovered or recorded), PLAN (the reduction section closes; the milestone order resumes), PLAN_HISTORY (the R0–R9 summary, explicit under this plan's adoption), LIBRARY and README (R8), the permanent zero-reference rows replacing ratchets that reached 0 | every unit | Doc; each document's own review rule | docs | Doc | D |
| X1 | R9 exit | ArchitectureRulesTest's final rows (dependency rows for Documentation and Build, no `[Transitional]`, structure limits hold for the moved files); the final area record for every area; the whole-solution Session with `'*.ll'`, the milestones and the CLI harnesses; O2 corpus (no change expected); the compile-time series against the pre-R5 baselines (PLAN10-P3's final record); the owner's final review; this plan's INTEGRATED closure | C1 | Doc + Session + Bench | docs | — | D |

### 5.4 The middle session's defect stream (not R7–R9; the same windows)

After H5 the MIR verdicts are published. The middle session runs these rows from PLAN in its lane, each under RP14: R4's exit
conditions (G103, G105, G106, G124/G126, G147, G160's remainder, G263); G264 U1 after G342's rows; G349 (A4's growth bound, with G332's
rule); G338 and G339 (BU's row question); G311 (return sites, with the back's builder fact); G220, G222; G255 C2 (with K1b); G254; G257
(with O3); G298; G272's N2–N5; G387–G392; G384; G386's triage; G136's pooled tables; the per-case callee acquisition (ARCH's Loan-stratum row names it "PLAN G13", which has no row, G396 (8); the A5 step 2 plan's RP13, the view hold);
RZ3 (the HIR discard classification and the builder; the back's in-place Drop); G318 (the chain core below both phases, with the front,
at R6-check); G258's W at R6-check; P42's MIR, A4 and A5 parts (RQ5); G396's items in the owner's order (R4-M165): A5-F7's last units
2c-6 and 2c-7, A5-F8 and A5-F10 (their SPEC cases unchecked on the MIR), A6's 2g units with G105 (2g-1 also makes H3-2's interim records
derived), X-1a (the ledger's per-row identity; its metric stays 105 until then), the 25 `Unsupported_Kd` producers without a removal
stage (DEC O4), X-5's N1–N11, G400's A2 half (an Array emptied by pops), G398; BU's G397, G401 and G402 are R7's (§5.1). The post-flip
rows that R6-check's H3b names class 2 or 5 come first.

### 5.5 Full units and their candidates (RQ6)

| Unit | Shared representation | Candidates | Why |
| --- | --- | --- | --- |
| R7-C2 Enumerate (its core a, §5.0) | The premise supply and its view | Two (design-first: PlanB C's §4 as revised; prototype-first) | G170's five rounds; the choice must be made on C1's rows and the scaling classes |
| R7-C3 identity (its core a) | The identity judge | Two (design-first: bu65 round 5) | Four non-ACCEPT reviews; the round-5 design exists and can be judged against a prototype |
| R7-I2 InferCtxt (its core a–b) | The solve state | Two | Revision B's non-convergence lies in this state |
| R7-P3 pipeline (its core a) | CallPlan's producer | Two | `TryCandidateCore`'s history; G216 (a)'s U2.x units all lived here |
| R8-W2 interpolation (its core a) | The bound and the buffer placement across builder, library and codegen | Two | R3-10's temporaries and the 1,024-byte rule; allocation counts decide |
| R7-D2 driver (its core d+g, §5.0) | The pass model | One, prototype-first | A second whole-driver candidate costs a full rewrite; D1's query state is the design, the prototype proves the order |
| R7-O2 local slot regions | The `let` slot's inferred region | Two (the owner's OQ-1 (c) asked a full-process design; R4-M94) | G194's census first; the candidates differ in where the region's constraints are collected |
| R7-E1, R7-V2, R7-Y2, R7-T1, R7-P7, R8-L1 | One record, one table or one fact each | One, prototype-first (an owner exception per unit, recorded in DECISIONS with the unit as U2.2's 「候補 1 つで進める」 was; no WORKFLOW revision) | The representation is decided (a Problem record, a declaration table, a published fact, a pure `Apply`, a chain, a snapshot); the risk is cost, which the prototype measures |
| R8-R1, R8-G1 | The representation structs; the lang items | None: Owner class (PLAN10 §5.8 and §16-9, approved 2026-10-10): design → implementation → review | WORKFLOW's Owner change has no candidates |

Every Full unit has a test author (RP7): the FP2 units' are I1, C1, P1 and R8-W2t; an FP1 unit's first phase is its test author's hour, writing the rows its Verification column names (the bp triple and the cost rows for a behavior-preserving unit) before the candidate starts.

### 5.6 Files and holds: cross-area units and overlaps

| Unit | Files outside the implementer's area, or shared with another unit | Rule |
| --- | --- | --- |
| R7-Y1–Y5 (M) | `Binding/Binding.Hir*.cs`, `HirTables.cs`, `HirVocabulary.cs`, `Binding/Binding.Lints.cs` (the lint walk) | RP17: hold; the front reviews; Y1 after the flip group (H1 edits the same publication code) |
| R7-V3 (M) | `Mir/MirDefKey.cs`, `MirProgram.cs`, `Backend/Instances/Instantiation.cs`; Binding's declaration numbering | Hold on the Backend file; the back reviews it |
| R7-P5 | `Mir/Build/MirBuilder.Calls*.cs` (the comparison expansion's read of `TyCtxt.Comparison`) | Hold; the middle reviews |
| R7-P7, R7-T1 | `Backend/Instances/InstanceCollector.Admit.cs`, `.Callees.cs`, `Instantiation.cs`, `Backend/Capability.cs` | Hold; the back reviews (RP17) |
| R7-T2 | `Backend/Abi.cs`, `Backend/Layout*.cs`, `Mir/MirTypes*.cs`, `Mir/MirValidator.Types.cs` (the `ScalarTypes`, `SlotTypes`, `ReferenceTypes` reads) | One helper per unit; hold; the back and the middle review their reads |
| R7-E1, E5 | `src/Kimi/Diagnostics/**`, `Checking/Hover/**`, `Lsp/**` (readers), `Compiler/Analysis/**` until H10 (readers of `FailureCauses`) | The front's area but for Analysis: no Analysis edit before H10 (RP3) |
| R7-D2a against D1, V1, N1 | `Binding.cs` | D2a (the split) lands first, in Phase A; D1, V1 and N1 then edit the split files, one file each |
| R7-N3 against C5, C9 | `Binding.ContractMatching.cs` (`AccessCovers`) | N3 lands before C9; C5 does not touch the access members |
| R7-N3, P8, E2–E4 | `Binding.Diagnostics*.cs`, `src/Kimi/Diagnostics/**` | N3's repair candidates and P8's Closure display land as rows of E2 and E3 (one owner of the adapter change per family) |
| R7-N5, T2's reads (sweeps) | up to 31 and 29 front files per family | Sweep batches run when no other front unit holds their files (between units, or during a candidate's review); a batch names its files on the queue row and never overlaps a unit in flight |
| R7-P2, P3 against C5 | `Binding.Calls.cs`, `Binding.CallSelection*.cs`, `Binding.FunctionTypes.cs` | P2 before C5 (C5 splits `Binding.Calls.cs` and `Binding.FunctionTypes.cs` by responsibility only where P2 has not); P3 after C5 and after I2's and C2's choice |
| R7-I2 against P3 | `Binding.ArgumentKinds*.cs`, `Binding.ResultPropagation*.cs` | I2 before P3; P3 reads `InferCtxt` through `SlotSolution`'s view |
| R7-C3 against Y5 (M) | `Mir/Build/MirBuilder.Places.cs` (`Identical`) | Y5 lands in C3's window or the next |
| R8-K1b (B) | `Mir/Build/MirBuilder.Expansions.cs` (the Dictionary literal; G255 C2) | Hold; the middle implements its half or reviews |
| R8-W2 (B) | `Mir/Build/MirBuilder.Formatting.cs` (the expansion's buffer sizing) | The middle implements the expansion half; one group review |
| R8-W3 (B) | `Binding/ExpansionPlan.cs` (the Range row), `Mir/Build/MirBuilder.Control.cs` | Hold; the front and the middle review |
| R8-A1, K1, W1 (B) | `tests/mir/*.golden` | RP17: the middle reviews the golden diff |
| H3-2 U1a, U1b and H13's V1, V2 (B, in flight) | `Mir/Analysis/MirReport*.cs`; `Mir/Build/MirBuilder.Blocks.cs`, `MirBuilder.Selections.cs` | R4's consent given; the back's units in the middle's area |
| H3-2, H5 (B) | `Core/Compilation.Publication.cs` (new) | The back's, with its H3-2 and H5 Core hunks |
| Q8-3 (F, on `claude/r6-flip`) | `Mir/Analysis/MirEffects.Solve.cs`, `Mir/MirTypes.Destruction.cs`; `Binding/TyCtxt.Effects.cs`'s `CopyFacts`, read by `MirEffects` (middle) and `ModuleCodegen` (back) | R4's and R5's consent given; `CopyFacts` is a cross-area fact with one writer (front) |
| `claude/r6-flip` (the flip group) | H1b, H1, H1-tests, Q8-3, then H5's hunks | One writer, the front, until the back announces H5; merge only, never rebase |
| R9-F1–F5 (M) | `Compiler/Parsing/**`, `Compiler/Lexing/**`, `Compiler/Helper/**`, `Core/Compilation*.cs`, `SolutionAndProject/Project.cs` | RP17: the front reviews F1–F4; the back reviews F5's Generation hunk |
| R9-M1 (M) | `Compiler/Documentation/**` → `src/Kimi/Documentation/**`; `Core/Kotonoha.Documentation.cs`, `CodeContext.cs`, `Lexing/Tokenizer.cs`, `TokenReader.cs`, `Parsing/Parser.Directives.cs`, `Binding/Binding.Documentation.cs`, `Checking/**`, `Lsp/**` | The front reviews the lexer, parser and Binding hunks |
| R9-M2, B1, B2 (M, B) | `Compiler/Emission/**` → `src/Kimi/Build/**`; `Core/Compilation.Generation.cs`; `Backend/Codegen/ModuleCodegen.Imports.cs` (B1) | The back reviews Generation and implements B1's codegen half |
| P42's Binding part (F) against R7-N1, V2 | `Binding/TyCtxt*.cs` (the static slot key), `Binding.PlaceAuthority*.cs` (BU-25) | Lands after N1 and Q2 (RQ5) |

The front's three chains (D/N/Q, I/P, C) touch disjoint files except the rows above; within one window a unit of each chain may load
together when the table names no overlap, else they serialize in the order the Rule column gives.

---

## 6. New representations, vocabulary and components, and what they delete

| New | Stage | Deletes or replaces (lines, H) |
| --- | --- | --- |
| `DeclQuery` state and the four-stage driver | R7-D1, D2 | The 29-step order, 27 Clears, `IndexVisitor`, the second validation run and the re-validation loop: about −900 |
| `ResolvePath`, `BaseLayers`, `Visible`, `AttributeKind` | R7-N1–N4 | Six base walks, two access implementations, string tests for `Self` and attributes: about −1,600 |
| `FnSig`, `SignatureKey`, `ImplDecl`/`ImplTable`, `SignatureVerdict`, `MirDefKey(DeclId)` | R7-V1–V3 | `CheckSignatureTypeConstraints`, the per-use re-proofs, the conformance records of four files folded (with C9): about −700 |
| `InferCtxt` (variables, undo log, snapshot) and the `Awaiting` fact | R7-I2 | Five inference engines' private states and the waited-join state: about −2,500 for +850 |
| `Prove(goal, env)` with its memo, `Enumerate`, the identity judge, one value identity | R7-C2–C4 | Eager expansion, 15 entries, 8 cycle guards, 4 fixed points, batches and withholding: about −1,700 for +1,200; C9's folds −1,500 |
| `CallContext`, `CandidateResult[]`, the one pipeline | R7-P2, P3 | Twelve entries, the second selection pass, silent selections, `Enums`' path, winner re-evaluation: about −3,300 for +1,100 |
| The enclosing-instance chain in `Subst` | R7-P7 | The `EnclosingInstance` capability row and R5-M157's branch, G300's interim: about −150 for +250 |
| `Problem` record and the requirement enumeration | R7-E1–E5 | `BindingFailure` (88 members), the code switches, `ReportIssue`, `FailExplained`'s table, the consultation frames, `IsDerived`: about −2,400 for +500 |
| Binding's body-relation judgment removed (OJ1–OJ7 stay) | R7-O1 | `LocalRegions`, judging parts of `CallRelations` and `OriginProof`, six result-premise modes, per-request closures: about −2,500 |
| `Apply` pure with the storage verdict as a query | R7-T1 | Storage preparation inside substitution, the `StorageNesting` row: about −300 for +100 |
| `TyCtxt` predicates and `AdtDef` facts for the root helpers; one `Relate` | R7-T2, T3 | The eleven root files (1,032 lines, `MatchTypes.cs` among them, H11's) and the duplicated walks: about −1,400 for +450 |
| Inline column writes, `PlaceUse`, adaptation modes | R7-Y1–Y3 | The publisher walk, `PublisherUses`, three capability decisions: about −680 for +330 |
| Forwarding properties and synthetic Kotos gone | R7-Y4, P11 | Parsing about −700, Binding reads replaced earlier |
| `ArrayRepr`, `SliceRepr`, `StringRepr`, `Slot<K, V>`; `Layout` from placement | R8-R1 | `Layout.Rows`' handle rows and `LengthWord`: about −150 |
| Kimigayo bodies for 53 kinds; `compareBytes`; the formatters; the adapter | R8-A1–W2, U1 | `LegacyRuntimeHelpers*` 1,850, `FunctionCodegen.Strings/Updates/Interpolation/Arrays/Dictionaries/Formatting` parts, `ModuleCodegen.Formatting/Arrays/Dictionaries` parts, `.ll.in` about 1,100, `KimiLibrary.DictionaryStorage`, `TyCtxt.DictionaryStorage`: about −3,500 C# and ll for +1,300 `.kimi` |
| Lang items; `IntrinsicId` 28; no `CompilerFunctionKind` | R8-G1, I1 | `CompilerFunctionKind` (76), `IntrinsicDisposition.Of`, `KimiDeclarationId`'s 140 non-lang members, the signature-only parse mode, the catalog strings: about −900 for +200 |
| One frozen library per process (if RQ13 adopts) | R8-L1 | The library's per-compilation publication and share: about −300 for +500, and the measured Bind saving |
| One item loop, one type grammar entry, attributes by the parser, one keyword table | R9-F1–F4 | About −650 |
| `Kimi.Documentation`, `Kimi.Build`; the manifest's external declarations | R9-M1, M2, B1 | The IR re-parse; the compiler's folders shrink by 5,752 lines of moves |

Totals (H): R7 about −17,000 lines of Binding and Parsing against about +5,000 new (Binding from 77,274 toward about 62,000 after
R6-check's own −3,400); R8 about −4,400 C# and ll against +1,500 `.kimi`; R9 about −650 and 5,752 moved. PLAN10's 38,000-line Binding
figure is not reached by R7 as scoped (RP16): the A5 and R5 facts (about 16,000 lines) stay by design, and the remaining distance is in
families this plan does not schedule.

---

## 7. Measurement plan

### 7.1 Instruments

| Measure | Instrument | Conditions |
| --- | --- | --- |
| Bind time (cold, warm) with publication | The census harness (Hello, Milestone26; R4's `perf-u21` protocol): A, B and A′ interleaved from one folder, CPU pinned, `DOTNET_TieredCompilation=0`, High priority, the median of rounds; `Benchmark --compiler-pipeline` stage 2 (Scalar, Branching, Generic at 1/32/64/128); `BindingBenchmark` scenarios (Calls, Origins, Capabilities, Contracts, Properties, ConditionalConformances, ConditionalMembers, InheritedReceivers, Enums, RuntimeTypeTests at 32 and 512) | A quiet slot booked on the board; the registry empty |
| Allocation | `Purpose=Allocation` tests (1,183 at the base; warm 0 B), `AllocationMeasurement` rows per component, cold allocation per compilation, retained memory (BU-48's harness) | Every unit's class set; the Session |
| Scaling | `TypeDagScalingTest`, `OriginProofScalingTest`, `InferenceSlotScalingTest`, `CallableRegionScalingTest` (n = 40 rows added by C8, I1); V16's `Work` rows in A5's classes | Every front and middle unit's class set |
| MIR and collection | `--compiler-pipeline` stage 3, `--analyze-cost`, `--collection-cost` | R8 units (library bodies raise MIR build and collection), L1 |
| Generated code | `benchmark-o2-corpus.ps1`: the nine kernels plus L0's Strings kernel, 15 or more samples, address-matched control (R5-M147); O0 recorded; `compare-fixtures.ps1 -O2Metrics` | Every R8 family; R7-P7 and T1 (collector changes); R9-M2 (no change expected) |
| Native allocations | `DynamicArrayCostTest`, `DictionaryCostTest`, `SliceCostTest`, `Utf8CompilationCostTest`, `WorkloadCostTest`, `NativeAllocationAudit`'s zero-expects, utf8-formatting §6.2's counts, `Utf8ConsoleTest`'s 33-byte stack | Every R8 family |
| Acceptance | The diagnostic snapshot against the shared base, the generated gate (843 programs: moved programs named by class), `MirCensusTest`, the mir ledger until H10 | Every unit; RP14 |
| Size and structure | The area script lineage (`x4-areas.ps1`, `g11-areas.ps1`): lines, tokens, hotspots, line clones; ArchitectureRulesTest counts | Each commit's net size; each exit's area record |

### 7.2 Baselines

- R7's start: the dev sha of the first R7 unit's window (the census units record it); R6-check's deletions (H10's 16,000 lines) change
  retained memory and compile time in between, so the stage comparison of §10.1 is made against both R7's start sha and the H15 sha,
  each measured with the same instruments, and the per-unit comparisons are RP8's A/A′/B pairs; comparisons also run against the R2a
  (Bind cost accepted 2026-10-10), R5 (`66c856d81`) and R6-gen (`df7ae4c45`) records where the same instrument exists.
- R8's start: L0's O2 A=A run and the allocation inventory; the pre-R5 chain `49cb2b5d` (the clone `Kimigayo-baseline-49cb2b5d`) stays the
  long baseline for O2.
- Every unit measures against the shared base of the commit it starts from (VERIFICATION "Shared heavy runs and bases").

### 7.3 The window's heavy set

The default Session (26,000+ tests) with the native families the window's units name and `'*.ll'` when a Backend or library unit is in the
window; the diagnostic snapshot against the base; the mir ledger (the owner's "mir" and "差分台帳": one `-Pipeline mir -TestPurpose Functional` run with `KIMI_MIR_DIFF`, until H10) when a Binding or MIR unit is in the window; the harnessed
milestones when codegen, the builder or the library changed; the O2 kernels the Backend or library units name; `verify-commit.ps1` per
landed commit; the shared base published. About 15–40 minutes per window (H).

### 7.4 Stop rules

RP8 per unit. Per stage: the owner is consulted after two consecutive working sessions of a stage that land no unit (the R5 rule); a
unit not landed within two working sessions is re-split or becomes an owner question; a kernel regression beyond variance blocks the
family's merge and is fixed in the library or codegen, never a tolerance; a scaling row that leaves linear is a defect of the unit.

---

## 8. Risks and the units that may not converge

### 8.1 What did not converge, and why (DECISIONS Q11, Q12, Q13)

| Unit | Rounds | Cause one level up | This plan's countermeasure |
| --- | --- | --- | --- |
| G216 (a) revision 2 | Five proposal revisions, three implementation reviews, a redesign review | Checks added beside the canonical components; the choice made between design texts while the defects showed in code | RP6 (ratchets), RP7 (tests first, choice on results), RP13 (prototype first) |
| bu65, G170's identity | Four non-ACCEPT reviews | An order-dependent withholding; a per-premise rank instead of the per-parameter-class choice; a text census of readers | R7-C3 as FP2 with C1's rows; the ArchitectureRulesTest guard over identity comparisons |
| G159 (2), bu47 | Five reviews, then rebuilt one level up (a join tree judged once at its root) | Per-form rules at the parts | R7-I2: the join is one constraint; I3 one rule per unit |
| WK, G272, 2c-5 | Fix round 2 "一段上でもう 1 回" after reviews found readers ranking or deciding themselves | Two owners of one outcome (readers re-ranking `MirOutcome`; `IsFixed`'s kind test) | RP6; "one answer only `Join` ranks" is the model for `PlaceUse` (Y2), `SignatureVerdict` (V1), `Awaiting` (I2) |
| G264 U1 | Neither candidate qualified | A cost cause (Θ(N³) Work) outside both candidates | RP13 and the scaling rows in every acceptance set; the cause fixed first (G342's pattern) |
| Revision B (BU104) | Group review and re-check share a cause | The position's common-Type formation decided in two places (`FormedByElement`, `OneFormation`) | I0 under the owner's (A); I2 gives formation one predicate |
| G271, G300, G293 | Fix rounds 3–6 | Record-selection exactness; heuristics refined per form | Q13 (record selection is the planning session's; a heuristic broken twice becomes a representation or a located `Unsupported_Kd`) |
| G216 (a)'s landing cost | Bind +8 to +10 %, found at the landing | Cost measured after the unit | RP8 per unit |

### 8.2 Units at risk and their response

| Unit | Risk | Response |
| --- | --- | --- |
| R7-D2 driver | Every declaration depends on the order; a demand cycle that the order hid | D1 first (queries behind the order, cycle detection reporting, not failing); the prototype runs the whole suite with the order randomized within a stage (H: a cheap oracle for hidden dependencies); FP1 with the bp triple |
| R7-C2, C3 | G170's history; the limits' values; the D5 exception's condition | C0's prototype measures before design; C1's rows written by a third agent; FP2; the limits confirmed by C2's gate; before H5 the unit lands only where C0 shows the proof alone changes (BU-29's OQ4), else it waits |
| R7-I2, P3 | Large; revision B's territory; acceptance moves | Tests first (I1, P1); the gate names every move; FP2 on the core only (§5.0: I2a–b, P3a), the other parts Owner units under the chosen design; I3 and P4–P10 are separate units |
| R7-E1–E5 | 464 sites; the old Analysis's readers; Notes removed | RP3's staging; snapshot 0 at E1; D3 counted by code (RP19); E5 after H10 |
| R7-O1 | The twin's deletion changes which record a program reports | After H11 and G318; the gate and the A5 class set (RP14); Q13's classes |
| R8-K1, W1 | Unsafe Kimigayo bodies: double free, use after release | The empty-representation write-back rule (PLAN10 §15-7); exact free counts through `NativeAllocationAudit`; the kernels; a body stays legacy until its family's counts hold |
| R8-S1 | `compareBytes` slower than memcmp | The Strings kernel decides; an intrinsic only on a measured regression (PLAN10) |
| R8-W2 | Allocation counts and the 1,024-byte rule | FP2 on the §6.2 counts; `Utf8ConsoleTest`'s rows are the acceptance |
| R8 Bind cost | The library grows (about +1,300 lines) | L0 records; L1's threshold (RQ13); H1a's rule: time Bind with publication on both sides |
| R9-M1, M2 | 76 and 10 referencing files; the dependency direction | bp; D+R on the direction; the structure limits checked in the move |
| Contention | Front and middle both edit Binding (H units) and the HIR files; back edits the library index in Binding | The board's holds; RP11's overlap rule; RP17 |

---

## 9. The board (掲示板)

One file in the main checkout, `C:\Users\bwff1\repos\archi-Doc\Kimigayo\artifacts\board\BOARD.md` (ignored by Git, shared by every
session on the machine, copied into each handoff; the heavy-run registry `artifacts/heavy-runs/*.run` stays the lock). On adoption these
rules move to COMPILER_ARCHITECTURE "Rules during the migration" (§12). Rules: every row has an id (W-, Q-, H-, R-, T-, C-, O- plus a
number) and carries `[yyyy-MM-dd HH:mm session]`; a session edits its own rows and answers others' consent rows only; a row changes state
and never disappears until the coordinator archives it into the log seven days after its last change; the file is rewritten atomically
(write a temp file, then rename) and re-read immediately before every write, so two sessions never lose each other's rows, and the log is
append-only; a session re-reads the board before opening a window, starting a heavy run, taking a hold or booking quiet time; a row older
than 24 hours without a state change is stale and the coordinator flags it; a message is sent only to point at a row that needs an answer.

```markdown
# Board — read before any heavy action. Rules: docs/dev/... (this plan §9)

## 1. Windows (middle session)
| id | opens | base | units (Q- ids: branch@sha, session, review) | heavy set | state | dev sha | shared base |
| W-12 | 11-04 10:00 | 0d4074056 | Q-31 bu104@…, F, ACCEPT r3; Q-33 r4-u22-u4@…, M, ACCEPT | Session+snapshot+ledger, fam Closure* | announced | — | — |

## 2. Queue (units ready for a window; a row's state ends as `loaded W-nn`)
| id | unit | session | branch@sha | base | files or globs | review | unit checks | A5 verdict change (ledger or gate run) | cost record | state |

## 3. Holds (live; replaces PLAN §3 hold lines)
| id | files or globs | session | unit | branch | since | expected release | state |

## 4. Heavy runs (the queue: FIFO by planned start, a window first; the registry is the lock; `running n/3` in the heading)
| id | session | run | kind | planned start | est. duration | state |

## 5. Quiet time (timing; one at a time, at most two hours, booked 30 minutes ahead; others hold builds and heavy runs)
| id | session | from | to | purpose | state |

## 6. Consents and questions between sessions
| id | from | to | row it concerns | question | needed by | answer [stamp] |

## 7. Owner outbox (open owner questions, one list)
| id | session | date | question | options | recommendation | state |

## 8. Log (append-only, newest last)
- [11-04 09:55 M] W-12 announced: base 0d4074056, opens 10:00, units Q-31, Q-33.
```

Entry rules per table: a window row is announced at least 30 minutes before it opens, names its base and heavy set, and ends CLOSED
with the dev sha and the shared base, or FAILED with the dropped unit; a queue row is added when the unit's review is ACCEPT and its
state becomes `loaded W-nn` when a window takes it (`dropped W-nn` when the window drops it); a hold row is taken before the first edit of a shared file and released in the window that lands the unit; a
heavy-run row precedes the registry file; a quiet-time row is booked at least 30 minutes ahead, one at a time, at most two hours; a consent
row names the hunk and the rule it needs consent under, and its answer is quoted into the unit's commit; an owner-outbox row is added when
a session asks the owner and closed with the verbatim answer and where it is recorded.

---

## 10. Exit gates

### 10.1 R7

1. Session green with the milestones and the fixture families; the diagnostic snapshot differences classified; the generated gate's moves
   named by class in X3's list and accepted by the owner.
2. ArchitectureRulesTest zero-reference rows: the Koto forwarding properties, the synthetic Kotos, Koto-keyed tables in Binding,
   `BindingFailure`, `HirPublisher`, `StorageSubst`, `InstantiateRequirementCall`, the root helpers in Binding, `Supports*` gates; no
   `[Transitional("R7")]`; the capability rows `EnclosingInstance` and `StorageNesting` gone; ARCH's Syntax restriction holds for Binding.
3. Bind with publication: cold and warm time not above R7's start (the census harness, `--compiler-pipeline` stage 2, BindingBenchmark),
   warm 0 B, cold allocation and retained memory not above the start; the four scaling classes linear at their n = 40 rows.
4. The families of §5.1's table closed or re-recorded with a new owner; STATUS's R7 sentences removed; SP1 landed with examples and tests.
5. Records: `r7-exit-areas.md`, the owner's review of exceptions and Transitional declarations, CODEMAP, ARCH, DIAGNOSTICS, the
   end-to-end Binding review.

### 10.2 R8

1. Session with `'*.ll'` at O0 and O2, the harnessed milestones and the CLI harnesses green; every native outcome equal to R8's start
   except decided deviations with item and clause.
2. O2 corpus: every kernel within variance against R8's start and the `49cb2b5d` chain (address-matched control where needed); O0 recorded;
   the allocation-count tests and audits unchanged; warm 0 B; utf8-formatting §6.2's counts.
3. `IntrinsicId` 28; no `[Transitional("R8")]`; `LegacyRuntimeHelpers*`, `Layout`'s handle rows, `KimiLibrary.DictionaryStorage`,
   `TyCtxt.DictionaryStorage`, `MonoKind.LegacyRow`, `BodylessWitness`, `CompilerFunctionKind`, `IntrinsicDisposition` gone; `.ll.in` Buffer
   and Format gone, Float reduced to its entry; each body's A4 summary agrees with its published row.
4. Bind and MIR build cost with the grown library recorded; L1 adopted or declined under RQ13.
5. SPEC §22.1, §15.7, §6.5, utf8-formatting and IMPL texts landed; SETTLED's entry; LIBRARY, STATUS, README, STYLE; `r8-exit-areas.md`;
   the owner's review of exceptions.

### 10.3 R9

1. The moves behavior-preserving (bp); the dependency rows for `Kimi.Documentation` and `Kimi.Build` hold; no structure-limit exemption
   remains; no `[Transitional]` at all; IMPL §20.8.3 implemented (no IR re-parse).
2. The front-end remainders: one item loop, one type grammar entry, no implicit attribute context, one keyword table; the syntax corpus and
   snapshot unchanged; `ParseBenchmark` within variance.
3. The final documents (§5.3 C1) current; the ratchets that reached 0 permanent; the final area record; the compile-time series against the
   pre-R5 baselines; the owner's final review; this plan closed in INTEGRATED.

---

## 11. Owner decisions, with options and recommendations

See §2.2 for the questions. The reasoning behind the recommendations that are not self-evident:

- **RQ1 (the arrangement).** The alternative, sessions by stage, puts R7 and R6-check's Binding side in different sessions editing the
  same files, which the hold protocol can only serialize; by component the front session owns both. The middle session as coordinator
  keeps the heavy set and the merge in the session that already reads every analysis result (the ledger, the census, the gate).
- **RQ3 (start now).** Phase A's R7 content is censuses, prototypes and behavior-preserving units (D2a, D1, D3, C8, V1a, N5, T2's reads, E1,
  H1) whose evidence is the bp triple; none changes acceptance, so D5 is kept. Waiting for R6-check idles the front session's second lane
  for the whole of R6-check (the "keep the second lane busy" rule).
- **RQ4 (R8 now).** R6-gen is done, so a library body changes only MIR code generation's input; the old Analysis still checks the body,
  which is the constraint. L0 finds which bodies it accepts; A1 and K1 are expected to (the library already states its raw Fields'
  Origins, G160 §4), W1 and W2 may not (the writer forms), and wait.
- **RQ5 (P42).** P42 needs a full-process design across Binding, MIR, A4/A5, codegen and the runtime (R4-M103); its Binding part is a
  static slot key in `TyCtxt` and the static Borrow rules (BU-25). After R7-N1 and V2 those land in the declaration table as rows; before
  them they land in code R7 rewrites. The design package does not need the front session.
- **RQ6 (candidates).** Two candidates cost both implementer slots and a test author (Q11's cost); the plan spends them where the history
  shows the design is the open question (C2, C3, I2, P3, W2) and not where the representation is decided and the risk is cost.
- **RQ7 (the board).** The ARCH protocol's hold line was designed for two streams; with three sessions and two or three windows a day,
  a PLAN commit per hold is more than the windows' own commits.
- **RQ12 (G128).** BU-12 chose `Unsupported_Kd` as an interim because `Apply` could not tell a genuine limit from unresolved storage; with
  `Apply` pure and the verdict a query (BU-36, BU-38 already give Prepared/Rejected/Limit), the limit is IMPL 21.3.2's Resource case.
- **RQ13 (the frozen library).** The owner deferred O2 with O3 to after R7; R8 is when the cost moves, so the threshold is set now and the
  decision made by the number.
- **RQ15 (names).** A rename changes every stable requirement name and §23.3.6.6's order for no reader's benefit; R5-M167 already kept
  `Emission` lasting.

---

## 12. Records on adoption

| Document | Change |
| --- | --- |
| DECISIONS | A new section "R7, R8 and R9 (adopted …)" with the OQ answers, RP1–RP20 (those not waiting for their first unit), the arrangement (RQ1) superseding R4-M119's and R5-M113's session names, and RQ7's amendment of R5-M7 |
| COMPILER_ARCHITECTURE | The ledger's R7, R8 and R9 rows (state Active with this plan as the plan; the exit gates by reference); "Rules during the migration": the hold record moves to the board (RQ7); the vocabulary table's R8 line (`IntrinsicId` 28) and the `BindingFailure` row's fate (RQ9); the Syntax restriction's R7 clause unchanged |
| PLAN | §3's position: the three sessions, their lanes and the window rule; the R7, R8, R9 paragraphs with their first units; the open-issue rows that §5.1 absorbs gain their R7 unit in the Owner column; §6 next actions; within 300 lines (the R4, R5 and BU paragraphs condensed into the session paragraphs, their history to PLAN_HISTORY on the owner's instruction) |
| INTEGRATED | This plan as 一部取り込み (a working plan; no text frozen); each SPEC revision of §5 registered when it lands |
| VERIFICATION | No change until a unit changes a procedure (the window's heavy set is the existing Session and bases) |
| Memory of the sessions | Each session's resume prompt names this plan, its session and its first units |

Findings to record with the adoption (facts found while planning):

| Finding | Where | Proposed owner |
| --- | --- | --- |
| The E7 Strings and Arithmetic kernels of the R5 plan are not in `O2Corpus.md`'s table; R8 needs a Strings kernel | `src/Benchmark/O2Corpus.md` | R8-L0 |
| `Binding.cs` and `Binding.EffectBounds.cs` are over the token limit (R6-gen's record); `Parser.cs` at 25,781 tokens | the files | R7-D2 (split first), H5 (EffectBounds goes), R9-F1 |
| Binding holds 107 Koto-keyed tables in 31 files and about 1,150 forwarding-property reads; ARCH asks both at 0 by R7's exit | ARCH code rules | R7-N5, T2, E, H4 |
| `NativeToolchain.ReadExternalDeclarations` re-parses the IR text for external declarations, which PLAN10 §5.1 and IMPL §20.8.3's revision replace | `Emission/NativeToolchain.cs:439-453` | R9-B1 |
| The root helper `ReferenceTypes` is read by 29 Binding files, `ElementAccess` by 17, `ScalarTypes` by 14; their deletion waits for H10 and H14 but Binding's reads do not | `src/Kimi/Compiler/*.cs` | R7-T2 |
| `IntrinsicId.DestroyValues` has no producer (G126) until the Array and Dictionary glue bodies | `MirBuilder`, R8-A1c, K1c | R8 |
| The Dictionary handle is already a Kimigayo struct (`DictionaryStorage.Handle`, seven words); Array, Slice and string handles are `Layout.Rows` constants | `Backend/Layout.Rows.cs` | R8-R1 |

---

## 13. The sessions' check (2026-11-03)

On the owner's instruction (「他のエージェントに確認し、問題なければコミットしてください。」) the R4, R5 and BU sessions read the approved draft
before its commit. None objected to the direction; their corrections, applied above:

- **R4 (the middle session):** R4 exited on 2026-11-03 (R4-M165, dev `db7f80af7`); G216 (a) closed with a boundary, so U3, U2j-a/b and
  G388–G392 are a proposed common fix, not units; r4w12 landed IC, H3-1, H4, G179 (a) and G181 (1), r4w13 G354 (2) and G180 (a) (dev `fe0a14d86`);
  G351 and G145 are closed; the post-exit order is the owner's (G216 (b), G215, G264 U1, G349, G338, G350, G396's items); the middle's
  area is also edited by the back's H3-2 U1a/U1b and H13's V1/V2 and by the front's Q8-3, each with R4's consent (§5.6); §5.4 names
  G396's items, G400, G397, G401, G402 and G398; "PLAN G13" has no row (G396 (8)); the window rule gains "never inside a booked quiet
  slot" and the DECISIONS merge rule (§3.2); R4-M165–R4-M167 are the newest entries.
- **R5 (the back session):** G157 (1), Q19 and H13 are done, H3-2 is the back's (U0 landed, U1a–U3 follow, no wait for 2g-1), so
  "nothing in flight" no longer held (§3.4); H0 gives the back H3b's rehearsal and classification and R6-check's windows: the plan keeps
  them with the back through the flip group's window and makes the middle the coordinator from the window after it (R7-M2 amends
  R5-M182 accordingly); the `.ll.in` dispositions, the nine kernels and the six allocation-count classes are confirmed; the 53 Body-
  disposition kinds and `Layout`'s handle rows are restated by L0's census; A1 and K1 probably wait for H5 under RQ4 (S1 is safe), L0's
  prototype decides; `Core/Compilation.Publication.cs` is the back's; the board is preferred to messages for routine rows.
- **BU (the front session):** G354-1b landed (BU-86); revision B's (A) round did not converge and the owner held revision B and B3a
  (PLAN 5b1777f34), so both are absorbed into I2 (I0's row); U2j-a and U3 were cancelled with G216 (a); the H1 split is done ((b2) on dev,
  H1-tests and H1 on `claude/r6-flip`, Q8-3 there with its cost an open owner question); G335 U1 landed (BU-87, G399 remains); in flight
  G354-1c and G181 (2); the rows the owner gave to R4 (G179, G180, G181 (1), G354 (2), G351) are R4's, with G179's selection through the
  layer, G377, G365, G369, G51, G401, G402, G397 open for R7; §5.1's mapping is updated with G393–G402; RP4 excepts R6-check's own deletions
  (H1) and RP5 binds R7's units only; Q8-3's `CopyFacts` is a cross-area fact (§5.6); the plan was committed from a worktree, leaving the
  main checkout clean.
