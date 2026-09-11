# Conditional conformance Binding

SPEC §8.4.8 / §8.7 に従い、実装blockを持たない `Self is C when P` を一般Contractへ拡張した。

## 意味情報と検証

- `BoundConformance` は宣言側の Type / Contract Identity を保持し、`BoundConformancePath` が直接宣言・root Contract・条件・検証状態・associated Type・関数／Property対応を保持する。実際のType引数を含む利用時の命題は従来どおり `BoundType` Identity で区別する。
- 経路の索引は Type / Contract / 直接宣言 / root Contract。単一の `Self is B and C` から生じる別rootの経路も区別し、diamond内部の同じrootからの反復はまとめる。
- 無条件適合、条件付き適合、Copyの直接宣言とContract継承を共通の経路管理へ移した。Copyのfield／payload／base導出は既存のintrinsic処理を使用する。
- 条件の対象を先に登録し、通常の前提、projectionを使う条件、associated Type、要求の順に依存を解決する。登録は適合の証拠にしない。
- 各経路を D + P の環境で検証する。通常関数の要求前提、Origin対応、アクセス検証、Propertyの標準bridgeは既存処理を共有する。外側memberの宣言・bodyの環境へPを追加しない。
- associated Typeの候補・正規化結果はroot経路単位。同じrootの祖先経路間では共有し、別rootの条件・束縛は混ぜない。PropertyのOrigin対応と関数witnessの前提環境も経路別に保持する。
- Child経由のParentはChildのPを用いる。直接Parent宣言の条件を追加せず、Parent自身のアクセス領域を検証する。
- 同時に有効になり得る経路のassociated Type・実装対応を定義時に比較する。Propertyでは操作種別、完全Type、receiver、Origin入力対応、base pathも比較する。非両立性は明示的な条件の証明と限定された具体的Identity／Semantics比較で判断し、SATや場合分けを追加しない。

## APIと利用時の証拠

- `GetConformanceDefinition(type, contract)` は登録された定義メタデータを取得する。利用可能性を表さない。
- `ResolveConformance(type, contract, context, out path)` は利用環境で代入後の条件とType本来の制約を検証し、Provenの場合だけ経路を返す。対応は定義側のslotを保持するため、後段は呼び出しのType／Origin代入とともに使用する。
- 既存の `GetConformance` は無条件の検証済み定義に限る互換API。Type形成の前提を証明するAPIではない。
- signatureのprojectionは、条件が証明された経路、または候補経路に共通する明示的束縛から先に正規化できる。この正規化自体は証拠にならず、保持したprojection義務を最終検証で必ず証明する。
- 経路の結果は四値で扱い、Errorを別経路の成功で隠さない。進行中の同一問い合わせはUnknownで、恒久的な否定結果として保存しない。具体的な適合の不在は関連する登録・検証を確認して判断し、未解決の定義や条件はUnknownとして残す。

## アロケーションと回帰

- 一般適合の利用時証明結果は永続キャッシュしない。再利用する再入検出集合は命題のType・Contract・証明環境を区別する。定義側の経路、scope、辞書・配列容量は再Bindで再利用し、検証済み状態と対応を再構築する。
- 条件付きCopyと一般適合で条件の代入・証明を共有する。同じroot内のassociated Type候補収集・正規化を一度にまとめた。
- 追加テストは条件とType形成の分離、外側memberへの非漏洩、利用環境、標準getterのCopy証明、associated projection、経路の独立性／整合性、重複、循環、Error、アクセス、struct／enum、再Bindを検証する。
- 1・32・512型、複数経路とPropertyを含むwarm Bindを8回計測するテストでスレッド内割当0バイトを検証する。Benchmarkに `ConditionalConformances` シナリオも追加した。スループットの比較測定は今回の検証には含めていない。
- 最終差分でRelease／Debugとも全1,846テスト成功。BenchmarkのReleaseビルドも警告・エラー0件。途中の全体実行では既存の割当テストに40／8,040／8,120バイトの一過性の失敗があり、該当テストの単独確認と最終全体実行は成功した。割当の閾値や検証内容は緩めていない。
- witnessの宣言検証はbodyのownership／lifetime検証完了を意味しない。

## 次の単位

実装blockの条件scopeとconditional memberの公開前提・適用可能性。式の一般的なProperty呼び出し、継承custom accessorのreceiver変換、操作確定後のCFG ownership／lifetime解析は後続単位のままとする。
