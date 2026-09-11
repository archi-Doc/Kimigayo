# Conditional member Binding

SPEC §8.4.8.1〜8.4.8.3 に沿って、`Self is C when P` の実装blockと、通常関数callでのconditional member適用可能性を追加した。

## 宣言と条件scope

- 既存のConditionalConformance構文に省略可能な宣言blockを追加した。宣言blockのparser、構文ノード、書き出し、Tinyhand保存を再利用する。
- blockの関数・computed Propertyは外側Typeの名前空間に登録する。構文上の親はblock、signature・bodyの検証環境は条件scopeとして区別し、各memberから共有の条件宣言を参照する。
- field、enum case、constructor、deinit、nested Type、nested conformanceとenumのcomputed memberを拒否する。外側memberとの重複、条件だけ異なる同一signatureにも既存の重複検証を適用する。
- 通常の前提、外側Type／Contractの依存制約、block条件P、関数の依存制約の順に収集する。blockのsignature・accessor・bodyはD + Pで検証し、外側memberへPを追加しない。登録した適合自体を証拠として仮定しない。
- blockのassociated Type指定は対応するroot経路だけへ収集する。祖先経路ではそのrootの指定を共有し、別root・外側宣言へ束縛を公開しない。追加のassociated Type要件も証明し、既存の定義時の経路整合性検証を使用する。

## 適用可能性と意味情報

- 適合検証は既存の実装特定規則を維持し、選択した関数・Propertyの公開前提を証明する。通常関数要求自身の前提も既存のwitness環境から使用する。別blockの実装でも、そのblockのPを独立して証明する。
- 通常callはlookupで確定したgroupを評価し、外側Typeと関数自身のgeneric slotを別々に代入してからPを証明する。継承memberではlookupが保持する宣言元のbase Type引数を使う。
- 候補評価をApplicable／Inapplicable／Pending／Errorで保持する。Errorを別候補の成功で隠さず、選択に影響するUnknownがあれば代替候補に確定しない。Refutedの候補は同じgroup内で適用不可とし、外側・基底lookupを再開しない。条件の強さによる順位付けは追加していない。
- 条件付きCopy・適合経路・memberで、条件の代入と証明処理を共有する。関数参照とcomputed accessも同じmember条件の入口を使い、未対応の操作自体は未解決を維持する。
- `BoundCall`に宣言元の構築済みTypeと解決済みOrigin対応を保持する。nominal member callでは既存の要求callのOrigin対応処理を共有し、完全Typeを維持する。

## 再利用と検証

- 各候補の適用可能性を一度評価し、Type引数・引数対応・代入済みparameter Type・Origin対応をBinding専用バッファへ保存する。Best Candidate比較時・選択後の再証明をなくした。候補が一つなら代入配列の退避／復元も省略する。
- 通常の非member callにはOrigin対応の保存領域を確保しない。意味情報には専用の再利用配列へコピーし、一時バッファの参照を残さない。
- 再Bindで条件参照・scopeの経路・witness・callの有効性を再構築する。条件、member、associated Type指定の差し替えと復元をテストした。証明結果を利用環境を無視して永続キャッシュすることはない。
- 追加59ケースで、条件scope、宣言順、generic／Origin代入、別blockの前提、associated Typeの経路分離、禁止宣言、四値判定、lookupの停止、条件強度によらない曖昧性、再Bind、書き出し／保存の往復を検証した。
- 1・32・512型、associated Type・継承Contract・overload・入れ子callを含むwarm Bindで、8回の計測区間の追加割当0バイトを検証した。Benchmarkへ同じ構成の`ConditionalMembers`シナリオを追加した。スループットの比較測定は今回行っていない。
- 最終差分でRelease／Debugとも全1,905テスト成功。BenchmarkのReleaseビルドは警告・エラー0件。途中のDebug全体実行では既存の`WarmConstraintPassesReusePropositionsAndFactStorage`に6,512バイトの割当失敗が一度あり、該当ケースの単独確認と最終全体実行は成功した。閾値や検証内容は変更していない。

## 後続単位

一般member継承のreceiver対応・base pathと、Propertyを含む式の操作確定を進める。関数参照の実体化、computed accessの一般的な呼び出し計画、継承accessorのreceiver変換、CFGでのownership／lifetime・cleanup解析は後続単位とする。witnessの宣言適合はbodyのownership検証完了を意味しない。
