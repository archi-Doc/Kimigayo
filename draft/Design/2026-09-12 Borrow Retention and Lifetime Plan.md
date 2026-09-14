# Borrow retention の実装計画

2026-09-12。通常の借用保持と保守的な Owned 判定は [SPEC.md](../../SPEC.md) に反映済み。実装は未完了。[設計要約と受け入れ条件](2026-09-12%20Array%20Origins%20and%20Owned.md)、[レビューの採否](../Decisions/2026-09-12%20Borrow%20Retention%20Review.md) を正本として参照し、本書に規則を重複記載しない。

## 1. 採用した範囲

- Array/Dictionary と通常の aggregate、具体的 object/Closure の格納に Owned を要求しない。
- OwnedOrigins は raw pointee と未使用 Type/Origin 引数まで保守的に調べる。実際の Loan とは区別する。
- Owned を要求する規範上の境界を列挙し、API は必要な制約を明示する。
- mutable static への新しい safe borrow は有限 Origin に限定し、Owned 境界を越えさせない。
- Array の remove/clear で要素単位に Loan を解除しない。構造更新は通常の排他 Loan 規則を使う。
- checked cast は、Owned 証明で対象となった欠落 data-Origin binding のみ static として与える。
- 寿命付き型消去、一般 Type outlives の新構文、並行実行は後続設計とする。

## 2. 現在の基盤

[STATUS.md](../../STATUS.md) の Origin/制約/Copy/Owned、whole-Place CFG、enum/match/cleanup の実装を再利用する。既存の Owned 実装は raw pointee と未使用 slot を除外し、static storage 実装は借用を構造的に拒否するため、新仕様に適合していない。既存テストの成功を本改訂の実装完了と扱わない。

| 実装箇所 | 作業 |
| --- | --- |
| [Binding.Capabilities.cs](../../Kimi/Compiler/Binding/Binding.Capabilities.cs) | OwnedOrigins の完全 Type 走査、callable 境界、a : static の証明、Unknown/固定点/再検証 |
| [Binding.Storage.cs](../../Kimi/Compiler/Binding/Binding.Storage.cs) | 既存の storage description を共有。Type-level の保守的依存と実際の保持・Loan を区別 |
| [Binding.TypeOrigins.cs](../../Kimi/Compiler/Binding/Binding.TypeOrigins.cs) | static の構造的禁止を Owned/source 検査へ置換。有限な mutable-static Origin と推論 |
| [Binding.Origins.Model.cs](../../Kimi/Compiler/Binding/Binding.Origins.Model.cs)、[Binding.OriginRequirements.cs](../../Kimi/Compiler/Binding/Binding.OriginRequirements.cs) | 宣言の束縛、outlives、Loan requirement、依存と期限 |
| [OwnershipBody.cs](../../Kimi/Compiler/Analysis/OwnershipBody.cs)、[OwnershipModel.cs](../../Kimi/Compiler/Analysis/OwnershipModel.cs) | Loan provenance と CFG 生存性、Reborrow、保守的な配列依存、破棄観測 |
| [OwnershipBody.Checking.cs](../../Kimi/Compiler/Analysis/OwnershipBody.Checking.cs) | 到達不能ソースにも必要な検査を適用。実行 CFG には経路を追加しない |
| [CoreDeclaration.cs](../../Kimi/Compiler/Binding/CoreDeclaration.cs) | Array/Dictionary/Iterator の declaration と操作契約 |
| [IntrinsicCapabilityTest.cs](../../xUnitTest/Tests/IntrinsicCapabilityTest.cs) | raw/未使用 slot の旧期待を改訂。static 保持の正例と不正な source の負例に分割 |

## 3. 実装順序

| 段階 | 作業 | 終了条件 |
| --- | --- | --- |
| P0 仕様 | 設計とレビューを SPEC に統合 | 文書反映済み。実装の証明とは別 |
| P1 証明と Loan 基盤 | OwnedOrigins、a : static、local/ref/uniq、overlap、Reborrow、aggregate/cleanup の依存 | 明示された制約を定義時に証明し、不正な escape/alias/破棄を診断できる |
| P2 Array/Dictionary | 構築・literal・推論・所有値転送・consuming iteration・部分構築 cleanup | 制約なし singleton と入力由来 borrow の返却を許可し、local escape を拒否 |
| P3 更新と view | mutation API を定義してから実装。全体の排他アクセス、fixed T、相関する要素読み取り | active view との衝突と長寿命 Type への短寿命注入を拒否。remove で Loan を個別解除しない |
| P4 Object/Closure | 具体的 payload/capture、strong-owner 複製・release、既存の消去境界 | 通常保持は依存を伝播し、消去で保守的 Owned を検証 |
| P5 Static | source の可変性、有限 Origin、lazy initialization、modular effect/anchor summary、shutdown | immutable-static 保持を許可。mutable-static 借用の Owned 境界越えと破棄順違反を拒否 |
| P6 後続の型消去設計 | 保持寿命を表す view/API と必要な明示 Type outlives 構文を検討 | 別の SPEC 改訂で文法・省略・証明・cast 保証を確定。本改訂の完了条件ではない |
| P7 最終検証・実行 | artifact/invalidation、lowering gate、LLVM/runtime、実行テスト | semantic obligation が残るプログラムを出力しない。正例を実行し負例を具体的な理由で拒否 |

P2/P3 と P4 は P1 後に独立して進められる。P5 には static Property/startup の基盤も必要となる。P7 の検証項目は各段階から追加する。禁止診断を削除するだけで未検証経路を有効化しない。

## 4. 検証と移行

設計書の受け入れ条件に加え、別 module、private dependency、callable signature、空/再帰/未使用 slot、途中構築、defer、初期化と shutdown、再 Bind 後の証明失効を扱う。型に情報がない unsafe 実装を Owned で救済しない。

Binding 成功、generic 定義の証明、Loan/cleanup 検証、実行を区別する。未サポートによる拒否を compile-fail の合格にしない。対象実装を変更した段階で既存テスト・必要な新テストと既存の割当量検証を実施する。

変更した Core/language 契約と summary に応じて artifact の互換性・cache invalidation を更新する。旧 Owned 証明を新定義の証明として再利用しない。利用者が明示した Owned 制約は勝手に削除しない。

## 5. 今後も別途扱う範囲

寿命付き型消去、一般 Type outlives、interior mutability、weak owner、自己参照、unique static reference の生成方法、二段階 borrow、Send/Sync、thread/task、lending/higher-ranked Origins は本改訂から暗黙に導入しない。Rust 全体との受理範囲の一致を主張せず、必要な API ごとに検討する。