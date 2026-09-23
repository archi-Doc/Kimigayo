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

## Design

| 文書 | 状態 | 取り込み先・証跡 |
| --- | --- | --- |
| `2026-09-13 Dependencies and Artifacts.md` | 取り込み済み | 第 18 章、§20.8、§21.3（旧 PLAN_HISTORY の記録索引） |
| `2026-09-13 Testing.md` | 取り込み済み | §6.5.1、§17.5、§18.8、§20（同上） |
| `2026-09-17 Documentation Comments.md` | 取り込み済み | §2.3.1、§2.3.6、付録 A.21（同上） |
| `2026-09-19 Documentation Markdown.md` | 取り込み済み | §2.3.4、付録 A.21（同上） |
| `2026-09-21 Origin System Redesign.md` | 取り込み済み | 第 15 章（PLAN_HISTORY 2026-09-21） |
| `2026-09-22 Optional Types Try and Explicit Discard.md` | 取り込み済み | §14.2.4、第 17 章（PLAN §3、STATUS §3.1） |
| その他の 2026-09-10 〜 2026-09-20 の設計文書 | 取り込み済み | 2026-09-14 の SPEC 分割時点で反映。個別の証跡は git 履歴（`git log -- SPEC.md spec/`） |

## Decisions と Obsolete

`Decisions` は判断の記録、`Obsolete` は廃止済みの文書であり、いずれも固定。仕様への取り込み対象ではない。

## 新しい文書の扱い

1. 提案は `draft/Changes` または `draft/Design` に日付付きで作成する。
2. 確定した内容を `SPEC.md` と参照先に取り込む。
3. 取り込んだら、この一覧に文書と取り込み先（章・節、コミット）を追記し、文書を固定する。
