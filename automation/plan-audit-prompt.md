# Implementation Plan Audit

このプロジェクトの `IMPLEMENTATION_PLAN.md` を独立した立場から監査してください。

あなたは今回の実行における **Plan Audit Agent** です。

目的は実装を進めることではなく、

**仕様・現在の実装・テストと `IMPLEMENTATION_PLAN.md` を照合し、完成までの計画が正確・十分・実行可能であることを保証すること**

です。

既存の計画を正しいものと仮定しないでください。

---

# 1. 調査対象

最低限、以下を確認してください。

* `AGENTS.md`
* `IMPLEMENTATION_PLAN.md`
* `SPEC.md`
* `STATUS.md`
* 仕様書
* 設計文書
* ソースコード
* テストコード
* `.codex-loop/verification.log` が存在する場合はその内容

必要に応じてリポジトリ全体から以下も検索してください。

* `TODO`
* `FIXME`
* `HACK`
* `NotImplemented`
* stub
* placeholder
* 仮実装
* disabled / skipped tests
* 空実装
* 未使用の暫定コード
* 「後で実装する」ことを示すコメント

ドキュメントだけで判断せず、実際の実装を確認してください。

---

# 2. 監査姿勢

現在の `IMPLEMENTATION_PLAN.md` は仮説として扱ってください。

特に、

* `[x]` は本当に完成しているのか
* `[ ]` 以外にも未実装が存在しないか
* Milestone の Completion Criteria は十分か
* STATUS.md の報告は実装と一致しているか

を疑って確認してください。

以前の Codex の判断をそのまま引き継がないでください。

---

# 3. 仕様カバレッジ監査

仕様書の主要機能について、対応する実装計画が存在するか確認してください。

仕様に存在するが `IMPLEMENTATION_PLAN.md` に存在しない機能を発見した場合は追加してください。

逆に、

* 仕様から削除済み
* 設計変更により不要
* 別項目へ統合済み

などの古い計画が残っている場合は整理してください。

---

# 4. Compiler Pipeline Coverage

コンパイラープロジェクトの場合、それぞれの機能について必要に応じて以下を横断的に確認してください。

* Lexer / Tokenizer
* Parser / Syntax
* Symbol model
* Binder / Name resolution
* Type system
* Semantic analysis
* Constraint solving
* Ownership / lifetime / origin analysis
* Diagnostics
* Lowering
* Intermediate representation
* Code generation / Emit
* Link
* Runtime interaction
* Unit tests
* Negative tests
* Integration tests
* End-to-End tests

全機能ですべての層が必要とは限りません。

しかし必要な層が計画から抜けていないか確認してください。

特に、

> Parser はあるが Binder がない

> 正常系はあるが invalid case の Diagnostics がない

> Emit はあるが End-to-End test がない

といった抜けを積極的に探してください。

---

# 5. Vertical Slice 監査

Implementation Milestones が Vertical Slice として適切に構成されているか確認してください。

良い Milestone は、可能な限り、

入力
→ parse
→ semantic processing
→ lowering
→ code generation
→ execution / observable result

まで到達できるものです。

以下を確認してください。

* Milestone が大きすぎないか
* Milestone が細かすぎないか
* End-to-End に到達しない水平分割だけになっていないか
* 前の Milestone が次の Milestone の土台になっているか
* 実装順序が依存関係と一致しているか
* 後工程まで実装できない機能を不必要に早く導入していないか

必要であれば Milestone を分割・統合・並べ替えてください。

---

# 6. Completion Criteria 監査

各重要項目の Completion Criteria が客観的に判定可能か確認してください。

以下のような曖昧な項目は改善してください。

悪い例:

* Generics を実装する
* Parser を完成させる
* Diagnostics を改善する

良い Completion Criteria は、

* 何が可能になるか
* 何が拒否されるべきか
* 何のテストが成功すべきか
* End-to-End で何を確認するか

が判断できるものです。

---

# 7. `[x]` の再検証

`[x]` の項目を無条件に信用しないでください。

重要な完了項目について、実装・テスト・必要なら build を確認してください。

Completion Criteria を満たしていない場合は `[ ]` に戻してください。

部分完成なら、必要に応じて項目を分割してください。

---

# 8. Build / Test

監査の根拠として有益であれば build や test を実行してください。

ただし今回の目的は製品コードを修正することではありません。

失敗を発見した場合は、その失敗を隠したり修正して監査を通したりせず、

**未完了事項として `IMPLEMENTATION_PLAN.md` に反映してください。**

---

# 9. 実装コードを変更しない

Plan Audit では原則として製品コードを変更しないでください。

変更してよい主な対象は、

* `IMPLEMENTATION_PLAN.md`
* 必要であれば `STATUS.md`

です。

テストやソースコードに問題を発見しても、この実行では修正せず、実装タスクとして計画へ記録してください。

これにより Implementation Agent と Reviewer の責務を分離します。

---

# 10. STATUS.md

計画変更が今後の作業順序へ影響する場合は `STATUS.md` も必要最小限更新してください。

例えば、

* 現在の Milestone が変更された
* 完了扱いだった Milestone が再オープンされた
* 新しい優先タスクが追加された

場合です。

作業日記を追加する必要はありません。

---

# 11. blocked の扱い

単にコード量が多い、仕様が複雑、監査に時間がかかるという理由で `blocked` にしないでください。

可能な範囲でリポジトリを調査し、監査を完遂してください。

`blocked` は、監査に不可欠な情報が存在せず、合理的な判断が不可能な場合だけ使用してください。

---

# 12. 最終判定

最終応答は指定された Output Schema に厳密に従ってください。

`status` は次の基準で決定してください。

## `plan_ok`

以下の場合のみ使用してください。

* 重大な不足項目がない
* `[x]` の状態に重大な誤りがない
* 依存関係が妥当
* Milestone 順序が妥当
* Completion Criteria が十分
* 仕様と計画が合理的に対応している
* 計画変更が不要だった

## `plan_updated`

以下のいずれかを行った場合に使用してください。

* 新規項目を追加
* 不正な `[x]` を `[ ]` に戻した
* Completion Criteria を修正
* Milestone を再構成
* 依存関係を修正
* 不要項目を削除・統合
* 実装順序を修正

## `blocked`

外部要因により計画監査自体を合理的に完了できなかった場合だけ使用してください。

`summary` には、

* 何を監査したか
* 主な発見事項
* 計画を変更した場合は主要変更点
* 次の Implementation Agent が特に注意すべき点

を簡潔に記載してください。

**計画を承認することが目的ではありません。完成までの計画を正しくすることが目的です。**
