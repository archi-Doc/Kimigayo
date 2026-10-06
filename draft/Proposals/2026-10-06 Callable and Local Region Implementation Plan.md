# 実装計画：Callable・関数参照・クロージャ結果・ローカル領域推論

日付：2026-10-06。状態：実装前の計画。N27c の Abort 位置方針は確認待ち（§3）。

対象：N25b、N26b、N27b、N27c、N24、LR。調査基点：`e7d43a1da0b16f1ff3ca01a2c0a8f5bbe7cf0fd0`。

本書は正式仕様を実装するための計画であり、対応済みという宣言ではない。今回の確認はソース・仕様・テストの静的照合であり、6項目の実装や実行検証は行っていない。正式仕様の変更は §3 の明文化候補だけを予定し、既存の要求を未実装に合わせて弱めない。

## 1. 目的と範囲

次の経路を、同じ公開契約と所有権規則で最後まで扱えるようにする。

- 複数の Callable シグネチャを持つ型パラメーターから、一意な呼び出し契約を選択する。
- Function Type、Callable、明示結果付き匿名関数でも Place 結果を保持する。
- 長さ引数を束縛した関数と、安全に値化できるコンパイラ実装関数を Function Item として参照・呼び出し・消去変換する。
- 結果注釈も固定期待結果もないブロック本体の匿名関数で、通常の結果規則に従って結果型・Origin・Loan を推論する。
- 注釈なしローカルの型構造を宣言時に固定したまま、領域変数への制約と実際の Loan を本体全体で解く。

LR の「完全」は、SPEC の既存のローカル領域規則について、単純な再代入だけでなく制御フロー、ネストした格納、呼び出し、破棄を含めて完結することを指す。関連する注釈の省略位置や結果専用 Origin の経路も同じ仕組みに接続する。一方、一般の高階 Origin 束縛、依存型、汎用 Const generics、実行時情報による推論は追加しない。

`T.compare` などの requirement reference、Origin を持つ Container の参照に残る別の制限、非同期実装、Property/object 系の未完了項目は独立した作業とする。N27c では共通のアダプター不足が原因の非ジェネリックな標準関数参照も扱う。公開標準ライブラリ API の追加は目的に含めない。

[PLAN](../../docs/dev/PLAN.md) の既存のマイルストーン順序や P26 の完了判定は変更しない。本書の順序はこの6項目を実装する場合の順序であり、今回の依頼を実装開始やマイルストーンの再開とは扱わない。

## 2. 現状と根拠

| 項目 | 正式仕様 | 確認した入口・残存制限 | 主な既存テスト |
| --- | --- | --- | --- |
| N25b | [§8.6](../../docs/spec/08-generics-constraints-and-contracts.md#86-callable-constraints)、[§10.1–10.5](../../docs/spec/10-overload-resolution-and-inference.md) | `Binding.ValueCalls.TryCallable` は異なるシグネチャを検出すると `several` を返す。複数契約の転送や同一シグネチャの受け手統合は別に成立するが、複数候補の呼び出し選択は未対応。 | `CallableSignatureSelectionTest.SeveralSignaturesAreOneLocatedUnsupported`、`CallableSignatureInferenceTest` |
| N26b | [§7.1.1](../../docs/spec/07-functions-and-callable-values.md#711-place-results)、§10.7、[IMPL §21.2.4](../../docs/impl/21-layout-runtime-and-code-generation.md#2124-value-borrow-storage) | `Binding.Types.BindTypeStructure` の `PlaceResultKoto` 分岐が名前付き関数・requirement 以外を拒否する。既存の直接呼び出しは宣言構文から Place を判定するため、拒否解除だけでは共通関数値に結果区分を保持できない。 | `PlaceResultTest.PlaceResultSignaturesAreOneUnsupportedRecord`、`PlaceResultParseTest`、`PublishedPlaceProjectionTest` |
| N27b | [§4.4](../../docs/spec/04-arrays-indexing-and-slices.md#44-function-length-parameters)、§7.6.4、§10.5 | `Binding.FunctionTypes.UnsupportedReference` が `GenericParameterKoto` 以外のスロットを拒否する。`FunctionItemType` の束縛引数表現と参照の推論処理は現在 Type 引数中心で、通常呼び出しの `BoundCall.LengthArguments` と接続が必要。 | `GenericFunctionReferenceTest`、`ReferenceDiagnosticTest`、`LengthCallBindingTest`、`LengthStorageEmissionTest` |
| N27c | §7.6.4、[§22.5.1](../../docs/spec/22-core-execution-and-foreign-functions.md#2251-operations-and-source-context)、IMPL §21.4.2 | `UnsupportedReference` がジェネリックな `Intrinsic` / `CompilerFunction` を拒否する。`OwnershipAnalysis.ExpressionCore` の Item 消去変換と `BodyLowering.Closures` にも、本体や実行入口の不在・caller location に関する制限がある。 | `ReferenceDiagnosticTest.AnUnimplementedReferenceIsOneLocatedUnsupportedRecord` の `Raw.allocate<u8>`、`FunctionDefaultTest`、`LibraryAbortContextTest` |
| N24 | [§7.6.1](../../docs/spec/07-functions-and-callable-values.md#761-syntax-and-inference)、[§14.9](../../docs/spec/14-control-flow.md#149-result-validation)、§15.8.2 | `Binding.Closures.BindConcreteClosure` が、ブロック本体で `symbol.Type` が未確定なら Unsupported とする。式本体、注釈付き・固定期待結果付きの経路は既に存在する。 | `ConcreteClosureTest`、`ContextualClosureTest`、`AnonymousArgumentInferenceTest`、`ClosureResultOriginTest` |
| LR | [§15.4.4](../../docs/spec/15-ownership-and-lifetime-analysis.md#1544-locals-and-independent-type-expressions)、§15.6.1–15.6.6 | `BindVariableCore` は注釈なしの場合に初期値の型を採用する。`JudgeOriginRelation` は保持側の型だけでは Loan を運べない有限 Origin／推論領域間の関係を `Unrepresentable` とする。`OwnershipAnalysis.Borrows` にもその制限の報告がある。 | `OwnershipJoinTest.ReassigningABorrowLocalToAnotherBorrowIsUnsupported`、`ResultOnlyOriginTest`、`LocalOriginClauseTest`、`OriginRelationDiagnosticTest` |

ナビゲーションは [CODEMAP](../../docs/dev/CODEMAP.md)、対応境界は [STATUS](../../docs/STATUS.md) に従う。[SETTLED](../../docs/SETTLED.md) も照合済み。結果専用 Origin の禁止、署名 Origin の明示宣言必須化など、決定済みの非採用案を再提案しない。

## 3. 確認事項：N27c の Abort 位置

**D1：確認待ち。推奨は、関数値を実際に呼び出した式の位置。**

```kimi
let allocateBytes = Raw.allocate<u8> // 参照を作った位置
let storage = allocateBytes(-1)      // 推奨する Abort 報告位置
```

| 方針 | 意味と実装上の違い |
| --- | --- |
| 呼び出し位置（推奨） | Function Item、Callable、共通 Function 値を通じても、その標準操作を実行した呼び出しを示す。間接呼び出しの私的 ABI から標準関数アダプターへ診断位置を渡す。 |
| 参照生成位置 | 参照を作った場所を保持して報告する。移動・格納・転送後の実際の実行箇所とは異なり、同じ宣言・束縛引数を持つ Item の値ごとに位置を保持する方式の検討が必要。 |

推奨案なら、通常の標準関数呼び出しと同じ説明ができ、参照の生成箇所によって Function Item の型同一性や空の環境表現を変えずに済む。SPEC §22.5.1 の原操作の位置を保つ方針とも整合する。ABI 内の位置は診断用情報であり、ソースの引数、オーバーロード比較、Callable の型同一性には加えない。

決定後、N27c の実装より先に次の観測可能な規則を SPEC §7.6.4／§22.5.1 と必要な IMPL 節へ明文化する。現在ある `Context` パラメーターを動的な呼び出し位置と同一視せず、用途を確認して最小の ABI 変更を設計する。

- `Raw.allocate<u8>(n)`、`item(n)`、`erased(n)` は、それぞれの実際の呼び出し式を示す。同じ値を別の位置で呼べば位置も変わる。
- ユーザーの `invoke(f)` の本体が `f(n)` を実行するなら、標準操作の位置はその `f(n)`。ユーザー関数の境界を越えて外側の `invoke(f)` に自動的に差し替えない。
- 標準操作の内部 helper では既存規則どおり原操作の位置を転送する。ユーザー callback、`drop`、明示 `$abort` はそれぞれ自身の位置を保つ。
- 消去変換時の環境確保失敗と、関数値の呼び出し中の失敗を区別する。変換時の失敗は変換の位置を保つ。
- O0/O2、格納・Move・再利用、ジェネリック実体化によって論理パス・行・列を変えない。

ここで仕様を明文化する際は [INTEGRATED](../INTEGRATED.md) に取り込み範囲を記録する。本書の未解決部分を取り込んだ扱いにはしない。D1 の回答までは、その決定に依存しない作業だけを進める。

## 4. 共通の実装方針

1. **意味を一つの計画に保持する。** 呼び出しの選択結果に、署名、受け手取得、結果区分、束縛 Type／長さ引数、Origin 契約、effect 義務を保持する。Ownership と Emission が構文から選び直さない。
2. **候補の試行と確定を分離する。** 適用可能性の検査は取得・Loan・既定引数を実行せず、選択後に一度だけ確定する。匿名本体や捕捉を候補ごとに再解析しない。Loan 不成立や結果の Origin 不成立を理由に別候補へ戻らない。
3. **結果区分と値の型を区別する。** Function の契約に Value／PlaceRef／PlaceUniq に相当する明示情報を持たせる。Place を通常の Type 引数や新しい Semantics として表現しない。`ref/T` と `place ref/T` は物理 ABI が等しくても同じ型契約ではない。
4. **型推論と領域解決を分離する。** ローカル宣言で構造・Semantics・領域変数の同一性を固定する。後続の制約はその変数に追加し、既に決めた型構造や候補を再推論しない。有限／固定 Origin の判定と CFG 上の Loan 生存性も区別する。
5. **現在の再利用方針を維持する。** interning、scratch buffer、既存の packed table／worklist を再利用する。全候補×全匿名本体の探索や、使用箇所ごとの全 CFG 再計算を避ける。invalidate／再 Bind 後に古い選択・領域・ABI を再利用しない。
6. **標準処理の本体を複製しない。** N27c のアダプターは既存の source entry または compiler primitive に転送する。Kimigayo で記述されたライブラリのアルゴリズムを手書き LLVM IR に移さない。

## 5. 実装単位と順序

各単位で再現例・実装・関連回帰をそろえ、正式 Verify を通してから commit／push する。部分実装の単位では検証できた境界だけを STATUS に記載し、未接続経路の Unsupported を残す。項目全体の完了は §7 の全条件で判断する。

| 順序 | 単位 | 内容 | 前提・完了時の境界 |
| --- | --- | --- | --- |
| 0 | U0 | 6項目の最小再現、正例・負例、診断期待、共通計画の変更点を確定 | 現在の Unsupported と、実装後に成功／Language／Proof となるケースを分けて記録。現在のテスト期待を無条件に成功へ変更しない。 |
| 1 | U1 / N27b | 長さ付き Function Item の同一性・推論・実体化・消去変換 | 既存の長さ推論・specialization を利用。普通の Type 引数だけの参照回帰も合格。 |
| 2 | U2 / N24 | ブロック匿名関数を共通結果検証へ接続 | LR 未接続の経路を明示し、それ以外を Binding から native まで検証。 |
| 3 | U3 / N25b | Callable 候補集合・形状検査・選択・選択契約の保持 | 通常の値結果で完成。Place との組み合わせは U4/U5 で完成。 |
| 4 | U4 / N26b | 結果区分の共通表現、匿名関数・Item・具体 Callable の Place | 直接／具体 callable 経路の Loan と生成まで検証。共通 Function の未接続部分は制限を保持。 |
| 5 | U5 / N26b | 共通 Function の Place 結果、アダプター、間接呼び出し | U4。入力由来の結果 Loan と格納・借用・破棄まで一貫。 |
| 6 | U6 / N27c | D1 の明文化、標準関数 Item の実行入口と消去アダプター | D1、U1、U5。必要なら直接 Item と共通 Function に分割し、それぞれ正式検証。 |
| 7 | U7 / LR 基盤 | 領域変数・制約グラフ・Loan 対応を導入し単純な再代入を完成 | 型の構造を固定し、受理する経路では実際の依存を最後まで保持。拒否条件だけは外さない。 |
| 8 | U8 / LR 制御フロー | 分岐・ループ・上書き・破棄・Reborrow の領域解決 | U7。生存期間が終わった旧 Loan と、別の保持先に残る Loan を区別。 |
| 9 | U9 / LR 構成 | 集約・格納・generic・Callable・Place を通る制約と依存 | U2–U8。LR と他5項目の組み合わせを完了。 |
| 10 | U10 | 横断回帰、測定、文書の境界監査、Session 検証 | 6項目の完了判定。未解決が残れば対応完了としない。 |

この順序は、小さい独立項目から進め、署名表現と間接呼び出しを安定させてから最も広い LR へ入るための推奨である。N24 の基本実装や N25b の値結果は LR 全体を前提としない。一方、N24 の推論結果を再代入するケースなどは U9 まで完了しない。

### 5.1. U1：N27b 長さジェネリック関数の参照

主な変更先：`Binding.FunctionTypes`、`Binding.FunctionItems`、`Binding.Lengths`、型 interning／置換、`OwnershipAnalysis.Instances`、`GenericStoragePlan`、`BodyLowering.Closures`。

- 通常呼び出しと同じ宣言順のスロット種別を使い、Type と `BoundLength` を区別して束縛する。長さをダミー Type として埋め込まない。
- `keep<3, i32>` のような全明示参照と、固定期待署名の `[3 of i32]` から `N`／`T` を決める参照を扱う。固定 Callable 期待も同じ選択経路にする。
- Item の同一性、ハッシュ、署名取得、`FunctionItemContext`、実体化キー、specialization 選択に長さ束縛を含める。同じ宣言・同じ正規化長さは再利用し、異なる長さを混同しない。
- 外側の既知の長さ変数の転送と置換を保持する。`N * 2` の逆算、後続の呼び出しからの逆推論、部分明示引数リストは導入しない。
- `ValidLength` と依存する署名形成条件を既存の期限で確認する。不正な長さ、kind／個数違い、矛盾する証拠、未確定スロット、表現資源限界を区別する。

受入例：`func keep<length N, T>(value: [N of T]) -> [N of T] => value@move` の明示参照、固定 Function Type への変換、Callable 引数、generic 内の長さ転送が同じ実体を呼ぶ。0長、Non-Copy 要素、複数の長さ、明示 specialization、結果側からだけ決まる長さも含める。

### 5.2. U2：N24 ブロック本体のクロージャ結果推論

主な変更先：`Binding.Closures.BindConcreteClosure`、`Binding.ControlFlow` の `BeginResult`／`InferResultExpected`／`FinishResult`、`Binding.Expressions`、`StructuralCompletion`、`ClosureResultOriginTest`。

- 匿名関数用に独立した Function Boundary と結果収集先を用意し、既存の結果型統合を共用する。パラメーターは本体に入る前に確定し、捕捉取得を再実行しない。
- その関数への全 `return` と構造上の終端到達を収集する。ネストした関数・`defer` 内の転送や別ターゲットへの `exit`／`yield` を混ぜない。
- ブロック末尾の通常式は破棄し、構造上の fall-through は Unit。到達不能な明示結果も型制約として検査し、Never は SPEC §14.9 の二条件で決める。定数条件で経路を消して成功させない。
- 独立した結果の証拠を集めてから期待を伝え、数値デフォルトは最後に適用する。`try` の失敗側を型候補にせず、期待依存の結果は固定後に検査する。
- 推論するのは匿名関数自身の通常値結果だけ。Place 結果、外側の未確定 generic スロット、共通 Function への自動的な型統合は推論しない。
- 結果の Origin meet とすべての Loan、環境依存／入力依存／固定結果を保つ。呼び出し内のローカルや消費される環境への借用は、返す値に正しい Origin 診断を出す。

受入条件：全経路 `return`、Unit、全 Never、未到達の不整合、部分的 fall-through、複数 return の順序交換、入れ子の選択、所有値・借用結果を検証する。名前付き関数の結果省略＝Unit は変更しない。

### 5.3. U3：N25b 複数 Callable シグネチャの選択

主な変更先：`Binding.ValueCalls`、`Binding.Calls`、`Binding.CandidateEvaluation`、`Binding.ParameterShapes`、`Binding.CallableEffects`／`CallableProofs`、`BoundValueCall` とその Ownership／Emission 消費側。

- `TryCallable` の「唯一の既知署名を問い合わせる」役割と、「呼び出し候補を列挙する」役割を分ける。異なる複数署名から推論用の固定期待署名を勝手に作らない（SPEC §10.5）。
- 利用可能な Constraints と identity premises を正規化し、同じ契約の署名をまとめる。同じ署名の受け手は `ref` → `uniq` → `owner` の最も弱い要求を採用する。Origin の束縛名の違いを別署名と誤認しない。
- 引数の適用可能性で絞る前に、候補集合の receiver、task-slot shape、parameter acquisition shape の一貫性を検査する。不整合は言語エラーであり、都合のよい署名だけを残さない。非同期の実行対応をこの単位で追加しない。
- §10.1 の適用可能性と §10.4 の比較を通常呼び出しと共用する。引数は位置指定のみ。期待結果は候補除外には使えても順位付けには使わず、Constraints の強さや宣言順でも順位付けしない。
- 選んだ宣言契約、置換後の署名、受け手取得、Origin と effect 義務を保持する。実体化時はその選択を実装へ対応させ、候補を選び直さない。

受入条件：型付き数値での一意選択、整数リテラルの曖昧さ、適用候補なし、同一署名の受け手統合、集合の形状違反、identity による重複統合、generic 転送、候補順交換を確認する。実行例はすべての Callable 制約を満たす型で構成し、一部だけ満たす引数は正しく拒否する。既存の Unsupported ケースは、正例と本来の言語エラーへ個別に振り分ける。

### 5.4. U4/U5：N26b Place 結果の全 callable 経路

主な変更先：`Binding.Types`、`BindingModel` と型同一性・置換・互換性、`Binding.FunctionTypes`／`FunctionItems`／`ValueCalls`、`Binding.ArgumentOperations`、`ElementAccess`、`OwnershipAnalysis.Closures`、`BodyLowering.SlotResults`／`Closures`、`FunctionAbiPool`、`LlvmModuleWriter.Closures`。

- 結果区分を Function 契約の interning、等価性、表示、置換、known-signature inference、Callable 証明、消去変換で保持する。署名型が同じポインター表現を持つだけで、通常の参照結果と Place 結果を相互変換しない。
- 匿名関数の明示 Place 結果は、名前付き関数と同じ「値を取得せず Place を返す」検査へ接続する。`if`／`match`／`do` の値を Place にせず、各枝から直接返す。Unit の Place でも fall-through を許さない。
- value call の選択後に、公開された格納型と capability を持つ Place を作る。Copy 読出し、明示借用、固定期待型での借用、投影、Subject、代入・複合代入を既存の Place 経路へ渡す。呼び出しや位置計算は一度だけ行う。
- 外側の Place Origin と格納値内部の Origin を分けて保持する。入力由来の Loan、排他的な親子関係、ネストした参照を、共通 Function 経由でも失わない。隠れた環境 receiver を借りる結果を Callable／共通 Function へ消去しない。
- 物理結果は対応する `ref/T`／`uniq/T` と同じ ABI にする。結果のポインターを一時的な値スロットへの参照に置き換えず、ゼロサイズでも論理 Place と Loan の同一性を保持する。

受入条件：Function Item、具体クロージャ、Callable、共通 Function の各経路で共有読出しと排他更新、Non-Copy 格納、`place ref/(ref/T)`、0サイズ、長さ generic と組み合わせる。Take、共有経路からの更新・排他借用、通常値との結果区分不一致、ローカルへの返却、競合する再呼び出しを拒否する。

### 5.5. U6：N27c コンパイラ実装関数の参照

主な変更先：`Binding.FunctionTypes.UnsupportedReference`、`KimiLibraryCatalog` と compiler function の登録、`OwnershipAnalysis.ExpressionCore`、`GenericStoragePlan`、`FunctionAbi`／`FunctionAbiPool`、`BodyLowering.Closures`／`Calls`、`LlvmModuleWriter.Closures`。

- 値化可能な既存の安全な関数を、source body、compiler function、intrinsic、caller-location-required の軸で棚卸しし、署名と実行入口の対応表を作る。まず `Raw.allocate<u8>` で縦に接続し、同じ原因の他の入口も漏れなく検証する。
- Function Item の参照・保管・直接呼び出し・Callable 転送・共通 Function への消去に、束縛済み引数から一意に準備した実行入口を提供する。本体の存在だけを実行可能性の判定にしない。
- アダプターは元の primitive または source entry を呼ぶ。戻り値、Never、zero-sized 引数、aggregate、Place、caller context を同じ物理計画で扱い、IR writer には閉じた計画だけを渡す。
- D1 の方針を直接 Item、具体 Callable、共通 Function、既定引数の関数値に適用する。生成位置と呼び出し位置が異なるケースを必須にする。
- Unsafe 関数と `drop` の値化禁止は維持する。`Raw.release`／`initialize`／`slice` を `allocate` と一括して許可しない。成功時の確保資源はテスト内の適切な unsafe 操作で解放する。

受入条件：`Raw.allocate<u8>` の参照だけ、成功呼び出し、負数・サイズ過大による決定的な Abort、消去変換、Move 後の呼び出し、generic 内の型束縛を検証する。`Console.writeLine` など非ジェネリックで共通原因の値化も含める。実メモリー枯渇を試して失敗を作らず、既存の失敗注入がある場合はそれを利用する。

### 5.6. U7–U9：LR 完全なローカル領域推論

主な変更先：`Binding.Expressions.BindVariableCore`、`Binding.OriginDeclarations`／`OriginInference`／`OpenOrigins`／`OriginProof`／`CallRelations`、`BindingObligation`、`OwnershipAnalysis.Borrows`、`OwnershipBody`／`OwnershipBody.Borrows`、格納・呼び出し・cleanup の依存伝播。

**U7：型の契約と領域の制約。** 注釈なしローカルの省略された Origin 位置に、宣言を所有者とする安定した領域変数を割り当てる。初期値・代入・引数適合・結果・well-formedness・明示関係から、分散に従う outlives／等値の制約を記録する。注釈や束縛済み完全型で固定された Origin は勝手に開き直さず、per-call の全称 Origin もローカル変数へ置き換えない。

制約グラフには証拠となる値と宣言位置を保持する。有限→推論領域→固定 Origin のような間接経路も判定し、body-local が本体外へ届く要求は到達可能性に関係なく Refuted とする。固定同士の未証明関係は Proof エラーのまま。有限／推論領域間の関係は領域解決へ渡し、`Unrepresentable` を単に Proven に変えて Loan を捨てない。

**U8：CFG と実際の保持先。** 領域をプログラム点の集合として解き、実際の値・格納先・取得ごとの Loan と対応づける。旧値の上書き・Move・破棄、分岐で届く各値、ループの back edge、反復内で作る借用を扱う。型スロットの領域を共有することと、各代入値が持つ Loan を混同しない。

- 上書き後に保持先がなくなった旧 Loan は必要以上に延長しない。Copy された参照や他の集約に残る旧 Loan は消さない。
- 分岐の合流では可能な依存をすべて保持する。ループは単調な worklist で固定点へ到達させ、構文の走査順で結果を変えない。
- Reborrow の親子関係、親の停止・再開、排他性、部分投影・分離済み領域、call reservation／activation を維持する。
- 必要な使用点に加え、観測可能な destructor と `defer` の使用点を含める。スコープの字面だけで寿命を決めず、Move や破棄との競合はその操作で報告する。

**U9：構成と期限。** 同じ制約・Loan の経路を Tuple、固定配列、struct、enum、Array、Dictionary、Slice、closure capture、格納された参照、間接呼び出し結果へ接続する。既存の Copy／Move／再借用／上書きの意味を保ち、ネストした不変位置を共変として短縮しない。generic 定義は公開契約で検証し、実体化で新たな意味上の制約を発見して受理を取り消す方式にしない。

必須の最小正例は既存の再代入テストである。

```kimi
let first = 0
var view = first@ref
let second = 5
view = second@ref
require view@follow == 5 else => $abort("view")
```

これに、再代入前後の使用、別保持先に残る参照、内側ブロックを越える借用、両枝で異なる借用、ループ、排他的再借用、`.None` から `.Some(local@ref)` への更新、generic な間接書込み、N24 の推論結果、N26b の Place からの借用を加える。後続の使用から型構造を変える例、未初期化ローカルの未確定位置、固定 Origin への不正な流出は引き続き拒否する。

## 6. 検証計画

### 6.1. 項目別の回帰と生成確認

| 対象 | 既存の回帰選択の入口 | 追加する観測 |
| --- | --- | --- |
| N27b | `GenericFunctionReferenceTest`、`ContextualFunctionReferenceTest`、`ReferenceDiagnosticTest`、`LengthCallBindingTest`、`LengthStorageEmissionTest` | 長さを含む Item／実体化の同一性、宣言順、specialization、0長・Non-Copy、O0/O2 |
| N24 | `ConcreteClosureTest`、`ContextualClosureTest`、`AnonymousArgumentInferenceTest`、`ScalarResultUnificationTest`、`ClosureResultOriginTest` | 全結果ソースと完了判定、独立した capture、Unit/Never、借用・破棄 |
| N25b | `CallableSignatureSelectionTest`、`CallableSignatureInferenceTest`、`ParameterShapeTest`、`CallableEffectBoundTest`、`CallableConstraintRecordTest` | 一意／曖昧／不適用、集合の形状、選択契約・effect、再順序化 |
| N26b | `PlaceResultTest`、`PlaceResultParseTest`、`PublishedPlaceProjectionTest`、`InputDependentValueCallTest`、`AggregateValueCallTest`、`FunctionItemCallableTest` | 結果区分の同一性、間接 ABI、更新先の一回評価、借用・禁止操作 |
| N27c | `ReferenceDiagnosticTest`、`FunctionDefaultTest`、`LibraryAbortContextTest`、`GenericCallbackEmissionTest`、`UnsafeFunctionValueBindingTest` | Item／消去／default、位置とコードを含む Abort、成功時の解放 |
| LR | `OwnershipJoinTest`、`LocalOriginClauseTest`、`OriginRelationDiagnosticTest`、`ResultOnlyOriginTest`、`CallableSignatureInferenceTest`、`CallOriginRelationTest` | 制約鎖、CFG、保持先別 Loan、破棄、ネストした不変性、generic |

表は入口であり完全なコマンド一覧ではない。変更した共通処理の呼び出し側・格納・無効化テストを追加選択する。新規クラスを設ける場合も fixture 名と Verify 選択が実在することを確認する。

診断は [DIAGNOSTICS §10](../../docs/dev/DIAGNOSTICS.md#10-diagnostic-development-workflow-detail) に従い、独立に書いた公開レコード期待を用意する。コード・カテゴリ・一次範囲・Reason・関連位置・Note／Advice・repair 条件を点検し、CLI テキスト／JSON と LSP の代表出力を読む。Unsupported の削除だけを改善の証拠とせず、前提失敗の派生診断、独立エラー、順序交換、出力制限を組み合わせる。今回対象外の Unsupported は、その正しい境界で残す。

### 6.2. 性能・再利用

- 既存の warm Bind／Ownership／Emission のゼロ割当アサーションを維持する。候補バッファ、length key、result context、region worklist の反復利用と invalidate 後の再解析を検証する。
- 候補数、結果ソース数、領域変数数・辺数、CFG のループ／合流数を増やした固定 workload を設ける。保持容量・storage bounds の回帰と、時間・スケーリングの任意測定を分ける。
- callable は既存の [Callable plan measurements](../../src/Benchmark/CallablePlans.md) を基準に測定し、LR の測定は `src/Benchmark` に追加する。測定条件を事前に固定し、失敗後の追加 warming や閾値の緩和をしない。
- native では余分な環境確保、aggregate の複製、二重呼び出し、不要なアダプター生成を確認する。確認していない速度向上や一律のゼロ実行コストは主張しない。

### 6.3. 正式検証と記録

[VERIFICATION](../../docs/dev/VERIFICATION.md) に従う。編集途中は incremental build と選択メソッドの直接実行を使う。単位の完了は Verify の non-incremental Release build、関連 functional／allocation 回帰、選択した native O0/O2 fixtures で確認する。

```powershell
# U3 の入口例。実装時に関連クラスと新規 fixture の実名を補う。
./scripts/verify.ps1 -Class CallableSignatureSelectionTest,CallableSignatureInferenceTest,CallableEffectBoundTest

# 各実装セッションの最後に一度。必要な fixture と既存 harness を併せて指定する。
./scripts/verify.ps1 -Mode Session

# 検証済み入力を含む commit を作成した後、push の前に実行する。
./scripts/verify-commit.ps1 -Evidence artifacts/verify/<successful-run>
```

コード生成を変更する単位では `-Fixtures` を省略したまま完了としない。P20（長さ・推論）、P26（Callable）、P27（Place）、P37（統合）などの既存プログラムは影響に応じて原ソースの harness を回帰として実行し、新規のマイルストーン実装目標にはしない。Benchmark 変更を含む単位は whole-solution Session build の成功も必要。NativeAOT は実行しない。

検証中はソースを編集しない。失敗も `artifacts/verify/`、測定は `artifacts/benchmarks/` に保存する。exact input／設定／診断の変更範囲を commit に記録し、その単位のファイルだけを stage して、関連検証と commit association が成功したものを current branch から origin へ push する。

## 7. 完了条件と文書更新

6項目それぞれについて、次をすべて満たしたときだけ完了とする。

1. §5／§6 の正例が Binding、Ownership、生成、native O0/O2 を通り、意図した実行結果・更新・破棄を確認できる。
2. 本来の不正例は Unsupported ではなく、その原因に合った Language／Proof 診断で拒否され、位置と説明が独立に理解できる。
3. 既存の対応済み経路、allocation／reuse、無効化と再 Bind、必要な測定、セッション末尾の Session 検証が完了している。
4. 残存する拒否条件と関連 Unsupported テストを検索し、単にコードを通さず別のフェーズの Unsupported／GenerationFailed に移しただけのケースがない。
5. N25b×N26b、N27b×N26b、N27c×消去／既定引数、N24×LR、Place×LR の交差ケースを確認する。既存の独立した制限に阻まれる場合は、その再現・理由・次の作業を残し、該当範囲を完了扱いにしない。

実装時の文書更新は英語で行う。

- `docs/STATUS.md`：正式検証した対応境界だけ更新する。P26 周辺の過去の広い制限記述も具体的なテストと照合し、今回確認していない周辺機能まで対応済みにしない。
- `docs/dev/PLAN.md`：開始した単位・現在位置・残件を200行以内で更新する。`PLAN_HISTORY.md` はセッションごとに数行とし、詳細証拠は commit と artifacts に置く。
- `docs/dev/CODEMAP.md`：新しい候補選択、結果区分、長さ付き Item、領域解決の責務・入口・代表テストを同じ commit で更新する。
- SPEC／IMPL：D1 の明文化、または実装中に発見した真の矛盾・不健全性の修正だけを、根拠・Principles との整合とともに変更する。既存要求の実装だけなら仕様を変更しない。
- `docs/LIBRARY.md`／`STYLE.md`：公開宣言や採用するライブラリ規約を変更した場合だけ更新する。
- `draft/INTEGRATED.md`：仕様取り込みを行う commit で本書の該当節と正式仕様の対象を対応づける。一部取り込みはその範囲だけ凍結する。全項目の処置が記録されるまでは本書を閉じない。既存の凍結文書は変更しない。

## 8. リスクと着手時の判定

| リスク | 先に確認すること | 失敗時の扱い |
| --- | --- | --- |
| 長さの欠けた Item キーによる実体の混同 | 同一宣言・異なる長さ・外側長さ転送・specialization | U1 の時点で型同一性と実体化を直す。後段の特例で補わない。 |
| 候補試行が本体・capture・effect を変更する | 候補順／引数順の交換、失敗後の再 Bind | 選択前の観測と確定処理を分離する。 |
| Place 区分が参照型に埋没する | 同じ物理型の value／Place を混ぜた変換・呼び出し | U4 の共通表現を先に直し、構文からの後付け判定を増やさない。 |
| N27c の生成入口と Abort 文脈が不一致 | 同じ値を2か所で呼ぶ、共通値への消去、ユーザー callback | D1 と契約を確定し、アダプター・私的 ABI を一緒に検証する。 |
| LR が依存を消す／過剰に延長する | 上書きで終了する Loan と、別保持先に残る Loan の対 | 生存性・制約・取得ごとの Loan を区別し、正例と負例を同時に保つ。 |
| 局所推論が全体探索になる | waiting body、未確定 slot、循環、CFG の成長測定 | 既存の推論境界を維持し、有限グラフの固定点と明示期限で完結させる。 |

現時点で利用者の決定が必要なのは D1。その他は既存仕様に従う実装上の選択として上記の方針で進められる。新たな公開意味論の選択が必要になった場合は、最小例・現行規則・選択肢・影響を示して追加確認する。
