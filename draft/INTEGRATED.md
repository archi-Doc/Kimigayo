# 取り込み済み記録一覧

`draft` の文書は提案・設計・実装記録である。内容が `SPEC.md` とその参照先に取り込まれた文書は、この一覧に「取り込み済み」と印付けし、以後は固定する（編集しない）。正式仕様は `draft` を参照せず、`draft` を必須挙動の根拠にも用いない（`AGENTS.md`）。取り込みの流れは一方向であり、正式仕様側の変更を `draft` に反映しない。

| 状態 | 意味 |
| --- | --- |
| 取り込み済み | 内容は `SPEC.md` と参照先に反映済み。文書は固定。 |
| 実装記録 | 仕様変更ではなく実装増分の記録。仕様への取り込み対象ではない。文書は固定。 |
| 保留 | 採用されず、仕様に反映していない。文書は固定。 |

## Changes

| 文書 | 状態 | 取り込み先・証跡 |
| --- | --- | --- |
| `2026-09-10 Core Terminology.md` | 取り込み済み | 2026-09-14 の SPEC 分割時点で反映（第 3 章） |
| `2026-09-10 Property Semantics.md` | 取り込み済み | 第 11 章（`Design/2026-09-10 Properties.md` を経由） |
| `2026-09-11 Branch Body Syntax and Match Exhaustiveness.md` | 取り込み済み | 後続の `Design/2026-09-12 Control flow.md` を経て第 14 章 |
| `2026-09-11 Enum Ownership.md` | 実装記録 | 列挙構築の所有権検証（実装増分） |
| `2026-09-11 Match Binding.md` | 実装記録 | パターン束縛と網羅性（実装増分） |
| `2026-09-11 Match Ownership.md` | 実装記録 | match の所有権検証（実装増分） |
| `2026-09-12 Automatic Dereference.md` | 保留 | 安全な `*` は追加しない（§13.3 は `*` を生ポインター専用とする） |
| `2026-09-12 Result Copy and Static Inheritance.md` | 取り込み済み | 文書自体が「SPEC.md 反映済み」と記録 |
| `2026-09-12 Runtime Type Tests.md` | 実装記録 | 実行時 `is` の Binding（実装増分） |
| `2026-09-12 Unreachable Ownership.md` | 実装記録 | 到達不能コードの検査継続（実装増分） |
| `2026-09-13 Compiler Review.md` | 実装記録 | コンパイラー見直しと生成準備 |
| `2026-09-13 Emission Preparation.md` | 実装記録 | 生成パイプライン準備 |
| `2026-09-13 Minimal Executable.md` | 実装記録 | 最小実行ファイル（Program 1） |
| `2026-09-17 Declaration Container Nesting.md` | 取り込み済み | §6.1.1、§8.4.9、名前解決（旧 PLAN_HISTORY の記録索引、`git show 32324537:PLAN_HISTORY.md`） |
| `2026-09-17 Kimi Library and Named Aliases.md` | 取り込み済み | §18.1、§9.4.1、Kimi ライブラリ（同上） |
| `2026-09-17 Whole Value Replacement.md` | 取り込み済み | Sealed、payload projection、受け手互換（同上） |
| `2026-09-19 Test Execution Profile.md` | 取り込み済み | 文書自体が「仕様反映済み」と記録（§6.5.1、§17.5、§18.8、§20） |
| `2026-09-20 Call Borrow Reservations.md` | 取り込み済み | §15.6.7（PLAN_HISTORY 2026-09-20） |
| `2026-09-20 Origin Syntax and Elision.md` | 取り込み済み | 第 15 章（PLAN_HISTORY 2026-09-20 / 2026-09-21） |
| `2026-09-20 Parameter Names and Defaults.md` | 取り込み済み | §7.2（PLAN_HISTORY 2026-09-20） |
| `2026-09-21 Named Argument Boundary.md` | 取り込み済み | §7.2、§20（PLAN_HISTORY 2026-09-22、言語版 0.0.2） |
| `2026-09-23 Explicit Transfer and Exclusive Borrow.md` | 取り込み済み | 第 2–4、6–8、10–17、22 章と付録 A/D/E/F（コミット `3dcb401d`、実装 `4bdea22d`） |
| `2026-09-23 Borrow Origin Suffix.md` | 取り込み済み | 第 1–4、6、8–9、11、13–15、21–22 章、付録 A.12/A.22/F.2/F.4/F.7（後置 `during`、結合・Optional・Adaptation・診断） |
| `2026-09-23 Implicit Exclusive Receiver.md` | 取り込み済み | SPEC.md の要約、§3.4、§4.7、§6.2.2、§7.3、§7.6.3、§8.4.1、§8.6、§8.8.2、§9.1、§9.5、§9.5.1、§10.2、§10.4、§11.2、§12.4.4、§13.1、§13.5.5.1、§13.7、§14、§15.1.5、§15.6.7、§15.7、utf8-formatting §5、付録 A/E/F（PLAN_HISTORY 2026-09-24） |
| `2026-09-23 Object Payload.md` | 取り込み済み | §3.2.2、§3.3、§3.3.6、§6.1.3.1、§6.2.2、§8.1.1、§8.1.2、§8.2、§8.4.4、§8.4.7、§8.4.7.1、§8.4.7.2（新設）、§8.5、§8.7、§8.10、§13.5.5.1、§13.5.7、§13.5.8、§13.6、§15.3、§15.8.1、§21.3.4、§22.1、utf8-formatting §1.1、付録 A.19/E/F（PLAN_HISTORY 2026-09-24） |
| `2026-09-24 Complete Types and Static Contracts.md` | 取り込み済み | `Design/2026-09-25 Places Borrowing and Iteration.md` に統合され、同文書の取り込み（2026-09-26）で §8.4、§8.4.3、§8.4.5、§8.4.9 へ反映 |
| `2026-09-26 Follow Copy and Specification Split.md` | 取り込み済み | SPEC と実装仕様の分離（`b71eba3b`）、実装 `d0acea79`（`@follow`）、`95cb6f4f`（`@copy`）、`80f74a75`（単一スロット束縛）。本書は事後の変更記録として作成 |
| `2026-09-25 Shared Place Results.md` | 取り込み済み | 同上。§7.1.1、§4.6.9、§10.3 へ反映 |
| `2026-09-28 Integer Ranges.md` | 取り込み済み | 2026-09-28 取り込み。§3.5.1、§3.5.3、§4.2、§4.6（§4.6.3 を `Range<T>`・`IndexRange`・`ResolvedRange` の 3 型へ再編、§4.6.4 を解決・評価・失敗へ改題）、§8.4.7（§8.4.7.3 `PrimitiveInteger` 新設）、§10.2、§10.8、§12.3.1（リテラル式）、§13.2–13.5、§14.6.2、§14.9.1、§17.3.1、§22.1、SPEC.md の宣言索引、付録 E、IMPL §21.5.3 と付録 A.13、STYLE |
| `2026-09-29 Redesign Ranges B.md` | 取り込み済み | 2026-09-29 取り込み（統合前の修正は `8afde300`）。§3.3.6・§3.4.1・§3.5.1、§3.5.3（Scalar read を値の読み取りへ改題、読み取り型を定義）、§4.5、§4.6.1、§4.6.2（Index を位置へ改題）、§4.6.3（`Range<S, E>`・`ClosedRange<S, E>`・`ResolvedRange`・反復へ再編）、§4.6.4、§4.6.6、§4.6.8、§4.6.9（位置の正規化）、§4.7.2、§5.3、§7.3、§8.2・§8.3（型同一性）、§8.4.7（閉じた Contract、「Intrinsic and closed contracts」へ改題）、§8.4.7.3、§8.4.8.1、§8.7、§10.2・§10.2.1（推論の例外）、§12.1–12.3.1、§13.1、§14.6.2、§14.9.1、§15.2、§17.3.1、§17.4.4（新設）、§22.1、utf8-formatting §4.2、SPEC.md の宣言索引、付録 D・E、IMPL §21.5.3 と付録 A.13、STYLE |
| `2026-09-24 Exclusive Iteration and Iterable Modes.md` | 保留 | `Obsolete/` へ移動済み。後続の `Design/2026-09-25 Places Borrowing and Iteration.md` が三つの列挙入口として置き換えた |
| `2026-09-30 Contextual Labels and Named Transfers.md` | 原案取り込み済み・改訂未反映 | 原案は別ブランチ `codex/contextual-label-spec` の `c3d9fe5b`（§1.2・§2.5.1・§12.1・§13.1・§13.5.1・§14.4–14.5、付録 F、実装仕様 §21・付録 A.15 と関連例）。2026-09-30、ユーザーの明示的な依頼で改訂：転送値の省略判定、Primary 文法、宛先と値の非隣接、括弧保存。改訂の正式仕様への反映、作業ブランチへの統合、コンパイラ実装・実行検証は未完了。STYLE の同名回避推奨のみ反映。 |
| `2026-09-29 Verification Performance.md` | 取り込み済み | 2026-09-30: §20.8.2.4–§20.8.8, SPEC index, verification workflow, compiler/toolchain and original-source milestone harnesses. |

## Design

| 文書 | 状態 | 取り込み先・証跡 |
| --- | --- | --- |
| `2026-09-20 UTF-8 Formatting and String Interpolation.md` | 取り込み済み | `spec/utf8-formatting.md`、第 2、4、7–8、12–13、15、17、21–22 章、付録 A/D/E/F、SPEC 宣言索引。原稿は固定。 |
| `2026-09-13 Dependencies and Artifacts.md` | 取り込み済み | 第 18 章、§20.8、§21.3（旧 PLAN_HISTORY の記録索引） |
| `2026-09-13 Testing.md` | 取り込み済み | §6.5.1、§17.5、§18.8、§20（同上） |
| `2026-09-17 Documentation Comments.md` | 取り込み済み | §2.3.1、§2.3.6、付録 A.21（同上） |
| `2026-09-19 Documentation Markdown.md` | 取り込み済み | §2.3.4、付録 A.21（同上） |
| `2026-09-21 Origin System Redesign.md` | 取り込み済み | 第 15 章（PLAN_HISTORY 2026-09-21） |
| `2026-09-22 Optional Types Try and Explicit Discard.md` | 取り込み済み | §14.2.4、第 17 章（PLAN §3、STATUS §3.1） |
| `2026-09-25 Places Borrowing and Iteration.md` | 取り込み済み | 2026-09-26 取り込み。§2.5.1、§3.2.3、§3.3.6、§3.4（§3.4.1 新設）、§3.5（§3.5.3 新設）、§3.6.2、§3.8、§4.5、§4.6.1、§4.6.3–4.6.4、§4.6.6–4.6.8、§4.6.9（新設）、§4.7.3、§4.7.7、§6.1、§6.1.2、§7.1（§7.1.1 新設）、§7.3、§7.6.2、§8.4、§8.4.3（§8.4.3.1–2 新設）、§8.4.4–8.4.5、§8.4.7、§8.4.9、§8.9–8.10、§9.5、§9.6.1.2、§10.2（§10.2.1 新設）、§10.3、§10.6、§10.8–10.9、§11.1、§11.2.3、§12.2、§12.4.1、§12.4.4、§13.1、§13.4、§13.5.1、§13.5.3、§13.5.5（§13.5.5.1 Dereference 新設）、§13.5.7、§13.7、§14.6.1–14.6.2、§14.8、§14.8.1–14.8.3、§15.1.3、§15.1.5–15.1.6、§15.6.3、§15.6.7、§15.7、§15.9、§16.3.2、§18.7.2、§21.1.5、§21.2.4、§21.3.3.1、§21.4.4、§21.5.5、§22.1（§22.1.2 新設）、utf8-formatting §4、付録 A.4/A.10/A.13/A.19/A.22/A.23（新設）、D、E、F、SPEC.md 索引。設計資料 `2026-09-25 Storage Provision and Projection.md`、`2026-09-26 Places Borrowing and Iteration Review.md`、`Changes/2026-09-24 Complete Types and Static Contracts.md`、`Changes/2026-09-25 Shared Place Results.md` は本文書経由で反映済み（固定） |
| `2026-09-26 Semantics-Generic Follow.md` | 取り込み済み | 2026-09-27 取り込み。§3.4.1、§3.5.3、§7.3、§8.1.2、§8.9、§10.2、§13.4、§13.5.5.1（Pair layers 新設）、§14.6.2、§14.8.1、§15.1.6、付録 A.23、D、E、SPEC.md 索引。原稿は固定。 |
| `2026-09-29 Diagnostics.md` | 取り込み済み | 2026-09-30 取り込み（旧 `Plan/DIAGNOSTICS_PLAN.md`、D0）。§23.2、§23.3.2–§23.3.4、§23.3.6（新設）、§23.4.7、§23.5、SPEC.md 索引、付録 D・E、`docs/dev/DIAGNOSTICS.md`（新設：内部モデル・共通規則・移行規則・評価・改善手順の詳細）、AGENTS.md（Diagnostic Development Workflow）、`docs/dev/prompts/implementation-execution.md`、PLAN の Diagnostics track（D0–D4）。取り込み時にユーザーが採用した修正：仕様先行と D0 での凍結、表示方針に依存しない公開診断、フェーズ単位の一つの退避規則、Faulted を `CheckFaulted_Kd` 一件で説明、JSON は準備のみで公開出力しない、D2a/D2b 中の P31 停止、要求（Requirement）への用語統一、Context と順序の規則。実装は D1 以降。原稿は固定。 |
| `2026-09-28 Language Server and Compiler Services.md` | 取り込み済み | 2026-09-28 取り込み（旧 `LSP_PLAN.md`）。第 23 章（新設）、SPEC.md 索引、§20（コンパイル構成）、付録 D、E（`7d4bae75`）。実装は `68e91287`、`b330c039`、`39663352`。原稿は固定。 |
| その他の 2026-09-10 〜 2026-09-20 の設計文書 | 取り込み済み | 2026-09-14 の SPEC 分割時点で反映。個別の証跡は git 履歴（`git log -- SPEC.md spec/`） |

## Decisions と Obsolete

`Decisions` は判断の記録、`Obsolete` は廃止済みの文書であり、いずれも固定。仕様への取り込み対象ではない。

## 新しい文書の扱い

1. 提案は `draft/Changes` または `draft/Design` に日付付きで作成する。
2. 確定した内容を `SPEC.md` と参照先に取り込む。
3. 取り込んだら、この一覧に文書と取り込み先（章・節、コミット）を追記し、文書を固定する。
