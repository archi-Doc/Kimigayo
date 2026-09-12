# Design Change: Result Copy と静的継承

日付：2026-09-12。状態：設計採用・[SPEC.md](../../SPEC.md) 反映済み。コンパイラーの実装完了を示す文書ではない。

## Current Specification — 現在の仕様

ここでは議論開始時の変更前仕様を記録する。

- enum の Copy は明示 opt-in が必要だが、Core.Result には指定がなく、`Result<i32, i32>` も Non-Copy。Option だけが conditional Copy を持っていた。
- virtual/override の互換性・動的選択・metadata は定義されている一方、宣言構文は未確定だった。
- 派生の同名メンバーを hiding として扱い、Effective Type により検索層が変わり得た。継承 conformance の mapping と同名検索の関係も不明確だった。
- ObjectCompatible の検証境界が宣言・object entry・使用位置に分散し、公開状態、specialization、依存変更の扱いが統一されていなかった。
- 破棄警告の重複回避は要求されていたが、優先順位は未定義だった。

## Proposed Specification — 新しい仕様

採用した仕様は次のとおり。

1. **Result の conditional Copy。** 通常の enum 導出を使い、完全 Type としての T と E がともに Copy の場合だけ成立する。条件は Type 形成を制限せず、active Case にも依存しない。Unknown を Non-Copy と扱わず、借用 payload の Origin・Loan を保持する。

   ```kimi
   enum Result<T, E>
       Self is Copy when T is Copy, E is Copy
       Ok(T)
       Err(E)
   ```

2. **通常メンバーの静的選択。** 具体 Core の Object View 経由でも、Effective Type から選択した宣言の実装を使う。派生 struct は、その宣言位置から accessible な祖先の Value-role member と同名の宣言を追加できない。Field・Property・関数、異なる Signature、条件付きメンバーにも共通で適用する。inaccessible な同名と同一層の overload は従来の規則に従う。

3. **継承 conformance の成立条件。** requirement の Self を派生型へ置換し、保持した実装 mapping が全要件を満たす場合だけ継承する。実装側の Self は元の宣言型のままで、receiver の差だけを適法な borrowed receiver projection で補う。一経路の不成立だけでは派生宣言を不正にしない。適合が必要な位置で原因を診断し、他経路・Unknown・Refuted・Error を区別する。intrinsic 能力は固有規則を維持する。

4. **ObjectCompatible の公開保証。** 呼出し操作の Identity ごとに Proven / NotProven を一つ公開し、型置換別の状態や公開 Unknown は設けない。通常本体と閉じた全 specialization の共通保証とし、一実装の不適合は公開 NotProven にする。その理由だけで specialization を宣言エラーにはしない。基底 projection / object borrow の呼出しは Proven を必要とする。標準 Property の直接操作は Place のまま、Contract witness は呼出し操作として区別する。

5. **効果と依存検証の統一。** receiver・入力・capture/static・返却 alias の効果を合成し、再帰は有限な要約の最小固定点で検証する。全体・基底の置換、不完全にする MoveOut、無制限な排他的借用の流出を防ぎ、部分領域の完全性を保つ置換は通常条件で許可する。同名検査・継承適合・公開保証は依存内容を artifact に保持し、Name や specialization の「不存在」を前提とした判断も再検証する。

6. **警告と拡張境界。** 同じ破棄箇所では、unintended-Unit ＞ Symbol 固有 ＞ effect-free の最上位一件だけを出す。virtual/override の設計は §6.2.4 に集約し、現行機能から外す。virtual/override/abstract は宣言修飾子の位置だけで認識して専用診断で拒否し、他の位置では Name を維持する。

## Changes — 変更点

- Copy な Result は取得後も再利用できる。`Slice<Result<i32, i32>>` の `s[0]` と ref 経由の Pattern payload 読取りは、借用から値へ変わる。参照固定の要素読取りは `s[0]@ref` を使い、総称本体を再検証する。`tryGet` の固定参照結果は変えない。
- Pattern binding には借用取得の指定構文を追加しない。必要なら enum 全体の借用を受ける API へ変更する。公開 Signature の変更が必要になり得る制限を Appendix D.1 に記録する。
- 既存の accessible な同名派生宣言は移行が必要になる。Self 引数・結果や所有 receiver を持つ Contract は継承できない場合があり、同名禁止により派生側での再実装もできない。葉の型での実装や合成を検討する。
- open 基底への Name 追加・access 拡大、Signature 変更、Proven 撤回、specialization 集合変更は下流を壊し得る。上流に未知の下流の診断は求めず、古い artifact を再検証した依存側で必要なエラーを出す。変更後も要件を満たす場合は更新して受理する。
- Core の Copy 条件は Symbol Identity に基づく原子命題集合で照合する。Option は `{T is Core.Copy}`、Result は `{T is Core.Copy, E is Core.Copy}` とし、順序・括弧・重複に依存させない。FileError の例には明示 opt-in を加える。
- Object の identity、Type test、receiver adjustment、派生層から基底層への完全な動的破棄は維持する。Descriptor に現行の virtual slot を要求せず、破棄中の禁止は一般の runtime dispatch 規則に統一する。

## Rationale — 変更する理由

- Result と Option を同じ能力導出にそろえ、例外や新構文を増やさず値の再利用性を高める。
- 構文と現行意味論を対応させ、意図しない hiding と conformance mapping の差替えを防ぐ。refinement の保証はアクセス可能な既存メンバーの宣言層に限定し、overload 選択全般の不変性は主張しない。
- 公開保証と依存再検証により、私有本体の閲覧、都合のよい具体化、解析・ロード順に依存した受理を防ぐ。
- 同名禁止の例外、open 基底への常設警告、推論 Proven の specialization 継承義務を追加せず、共通規則と実際に必要な位置の診断で整理する。

## Impact on SPEC.md — SPEC.md への影響

| 対象 | 反映内容 |
| --- | --- |
| §4.6.6、§15.1.6、§17.2.2、§22.1 | Result の conditional Copy、共有読取り・移行例、Core 条件集合、FileError opt-in |
| §6.2.2、§9.5–§9.5.1、§12.4.3、§14.10.1 | 同名禁止、静的選択、receiver projection、限定した refinement 保証 |
| §8.4.4–§8.4.5、§8.5、§8.7、§11.4、§13.5.7 | 継承成立条件と mapping、witness、静的 conformance と永続する Supports の区別 |
| §8.8.2、§8.10、§12.4.4、§15.6.4 | 公開状態、実装集合の共通保証、効果合成と証明期限 |
| §18.3、§21.2–§21.3.4 | 公開検証情報、Descriptor、依存内容検証と API 互換性 |
| §2.5.1、§6.2.3–§6.2.4、§16.3–§16.4、Appendix D/F | modifier 診断、拡張設計の集約、破棄禁止の統一、Pattern 取得指定の未導入項目 |
| §17.2.3–§17.4、Appendix A/B | 破棄警告の優先順位、検証順序・受入条件・実装参照モデル |

## Decision — 最終決定

上記を採用し、SPEC.md に反映した。コンパイラー実装は別作業とし、[確定変更案・実装計画](<../Design/2026-09-12 Result Copy and Virtual Scope.md>) の段階別受入条件に従う。Result の変更は静的継承の変更から独立して実装できる。

virtual/override、runtime Contract View、Pattern の借用取得指定は今回導入しない。未実装の解析や未解決義務を Proven / NotProven に読み替えて受理せず、仕様採用と実装完了を区別する。
