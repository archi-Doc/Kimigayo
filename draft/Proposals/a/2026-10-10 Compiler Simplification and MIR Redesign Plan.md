# コンパイラー縮小とMIRを中心とする再設計計画

作成日: 2026-10-10（日本時間）

状態: 提案。今回の変更は調査と計画作成のみ。実装の再開・仕様への取り込み・対応範囲の変更は行っていない。

調査対象: `src/Kimi/Compiler` 全体。基準コミット: `419e8e79ee62b67f48c33c991aab37e4e3bf8810`（`dev`）。

## 1. 結論と設計方針

**肥大化の中心は、構文に結び付いた意味情報を複数の段階で解釈し直す構造にある。既存の `OwnershipBody` を、Binding・解析・コード生成が共有する小さな意味IRへ置き換え、後段の意味の再発見を削除する。** 新しいIRを既存の処理の上に積み重ねたり、巨大なpartial classを別ファイルに分けたりすることを目的にしない。

採用する方針は次の六点とする。

1. **意味の決定は一度にする。** 名前・型・候補選択・獲得方法・評価順序・結果の由来を前段で決め、後段は記録を読む。
2. **意味IRは一系統にする。** 現在の所有権用グラフを発展・置換してMIRにする。別の永続的なBound AST、所有権IR、実行意味IRを並存させない。
3. **共有するのは事実、分けるのは解析規則と結果である。** 初期化、Loan依存、権限、liveness、effectの異なる意味を一つの万能解析に押し込まない。
4. **Backendは物理表現を決める。** Bindingや構文への逆参照をなくし、LLVM生成時に言語の意味を再判定しない。
5. **pre-alphaの未対応は、位置付きの汎用 `Unsupported` 一種類にする。** 未対応専用のAdvice・修正案・説明用推論・回避用の狭い部分実装を増やさない。
6. **追加には削除を対応させる。** 新しい表・処理・境界ごとに置換対象と撤去条件を記録する。コード移動、コメント削除、行の圧縮は縮小実績に数えない。

言語機能の削除による縮小は提案しない。既に検証されている対応形は維持する。現状の限定実装を機械的に削除して既存プログラムをUnsupportedへ戻すことも、対応維持とは扱わない。

### 1.1 既存の再編との関係

[既存提案](../2026-10-09%20Compiler%20Semantic%20IR%20and%20Responsibility%20Reorganization%20Plan.md)と[現行アーキテクチャ記録](../../../docs/dev/COMPILER_ARCHITECTURE.md)を確認した。後者には2026-10-10時点で再編が停止中であり、U0–U10が未完了であることが明記されている。既存提案の冒頭にある「implementation has not started」は現状の説明としては古く、実装状況はアーキテクチャ記録を採る。本提案は停止した実装を自動的に再開するものではない。

既存再編で得られた `OwnershipBody.Calls`、`OwnershipFlow`、`BorrowLiveness`、全体値更新の操作種別、物理 `EmissionModule` の分離は活用する。一方、測定済みの削減はLF正規化したフォルダー容量で6,084 bytes、約0.097%、C#で115行にとどまる。境界の準備には意味があるが、これだけで「コンパイラーを小さくした」とは評価できない。

本提案で追加する判断は、未対応診断の明確な縮小、残存する再解釈の削除順序、MIRへの置換試行、削除量と性能による継続判定である。採用時には既存U系列との対応を一つの実施台帳に統合し、二つの独立した再編計画を走らせない。

## 2. 調査範囲と定量的な現状

### 2.1 方法と限界

`rg --files src/Kimi/Compiler` で得た465ファイルすべてについて、パス・実ファイル容量・LF正規化容量・C#行数・SHA-256を取得した。全C#ファイルに対して `Unsupported` と `Advice` の出現行も調べ、各領域の入口・モデル・大きい処理・共有ヘルパーから生産者と消費者を追った。Binding、Analysis、Emissionは分担して読み合わせた。

これは全ファイルの棚卸しと主要経路の構造調査であり、全分岐の正しさ、実行性能、実際の削減可能行数を証明する監査ではない。行数は空行・コメントを含む物理行数で、コード行数や複雑度とは異なる。文字列検索の件数も診断数・実行回数・重複量ではない。ビルド、テスト、NativeAOT、性能測定は実行していない。

再現用の全ファイル台帳は `artifacts/verify/compiler-redesign-survey-20261010/inventory.json` に保存した。この領域はGit管理外なので、実装開始時には当該コミットから再採取し、検証証拠として別途保管する。以下の表とソース参照は提案書内に残す。

### 2.2 全体の内訳

| 領域 | 全ファイル | C#ファイル | C#行数 | 全ファイル容量（bytes） | 判断 |
| --- | ---: | ---: | ---: | ---: | --- |
| Binding | 175 | 175 | 60,703 | 2,884,552 | 最優先。意味決定、構文変換、proof、effect、診断、表示が一つの状態に集まる |
| Analysis | 57 | 57 | 20,613 | 942,296 | MIR構築と解析を分離し、後段の構文解釈をなくす |
| Emission | 95 | 90 | 20,859 | 1,162,181 | 物理変換に絞り、再Binding・再分類・CFG再構築を削る |
| Parsing | 86 | 86 | 17,712 | 672,605 | 文法自体は維持。Kotoの意味状態と合成構文への依存を整理 |
| Lexing | 6 | 6 | 3,968 | 161,651 | 優先度は低い。既存の回復・Unicode・再利用を維持 |
| Documentation | 13 | 11 | 4,338 | 169,942 | 任意の文書処理として隔離し、意味解析の入口にしない |
| Helper | 7 | 7 | 3,472 | 191,670 | 生成Unicode表と小さい字句ヘルパーを肥大化と混同しない |
| Core | 10 | 10 | 1,584 | 65,838 | 寿命・世代・フェーズ調停を所有し、意味判定は所有しない |
| Compiler直下 | 16 | 16 | 1,342 | 62,617 | 型分類と構文認識と表現判定が混在するヘルパーを整理 |
| **合計** | **465** | **458** | **134,591** | **6,313,352** | C#だけの容量は6,157,948 bytes |

Binding・Analysis・Emissionの合計は102,175行、全C#行数の約75.9%。LF正規化後の全容量は6,277,454 bytes（BOMは保持）。ファイル数だけでなく、同じpartial classの総量も見る必要がある。

| 実装ファミリー | ファイル数 | C#行数 | 読み取れること |
| --- | ---: | ---: | --- |
| `Binding` / `Binding.*` | 144 | 55,331 | ファイル分割が責任・状態の分離になっていない |
| `OwnershipAnalysis` / `OwnershipAnalysis.*` | 31 | 9,602 | 構築、case/default、解析調停、診断が同じ所有者に集まる |
| `OwnershipBody` / `OwnershipBody.*` | 15 | 7,059 | IRと解析結果・作業領域が混在する |
| `BodyLowering` / `BodyLowering.*` | 37 | 10,464 | Emissionの約半分。意味再解釈を削除する主対象 |
| `LlvmModuleWriter` / `LlvmModuleWriter.*` | 25 | 4,567 | 物理命令・runtime出力という必要な量を含む |
| `Parser` / `Parser.*` | 7 | 6,594 | 単一の `Parser.cs` は4,899行。Parsing全体を小さいと決め付けない |

### 2.3 「未対応の報告が大きい」の評価

未対応の説明・回避案のために必要以上の状態と分岐を保持している箇所はある。ただし `Binding.Diagnostics.cs` の2,496行全体を未対応報告の費用と数えるのは誤りである。通常のLanguage/Proof診断、Originの原因、前提失敗、重複抑制も含まれる。

文字列 `Unsupported` を含むC#行はBinding 114行、Analysis 90行、Emission 25行で、説明生成の周辺をすべて数えた値ではない。未対応診断の統一は独立して実施すべき削減だが、それだけで10万行規模の構造問題は解決しない。

## 3. ソースから確認できる構造問題

以下の行番号は基準コミットでの目印である。参照時は記載したシンボルを優先する。「重複」とは、同じ入力に対して似たコードがあることだけでなく、既に決まった意味を再び探索・分類していることを指す。

| 箇所・目印 | 確認事項 | 置換・削除方針 |
| --- | --- | --- |
| [Binding.Calls.cs](../../../src/Kimi/Compiler/Binding/Binding.Calls.cs) `BoundCall.Set`、195行付近 | 確定呼出しの設定から `target.Declaration.CodeContext.Compilation.Binding.SelectVirtualCall` に到達する | target/dispatchを明示して確定。保持レコードからBindingへの経路をなくす |
| [BindingModel.cs](../../../src/Kimi/Compiler/Binding/BindingModel.cs) `BindingSymbol.Declaration`、[Binding.Origins.Model.cs](../../../src/Kimi/Compiler/Binding/Binding.Origins.Model.cs) `BoundOrigin.Binder` | 型・シンボル・Originの先に構文があり、後段へ渡すだけでは意味情報が閉じない | 確定契約をIDと表で保持。構文との対応は別表へ |
| [Binding.EffectBounds.cs](../../../src/Kimi/Compiler/Binding/Binding.EffectBounds.cs) `KotoVisitor`、65–93・159・214行以降 | 約2,205行のeffect関連処理が構文を歩き、所有権解析後にもcleanupを含めて更新する | MIRの明示的操作とcleanupからeffectを集計。構文走査のeffect解釈を撤去 |
| [Binding.ContainerReferences.cs](../../../src/Kimi/Compiler/Binding/Binding.ContainerReferences.cs) 14・335・475・629行付近 | qualifierの限定推論に加え、未対応理由・修正構文・Advice用の新しい名前の探索を保持する | 既存の受理判定と通常エラーに必要な部分を残し、未対応説明専用の探索・payloadを削除 |
| [OwnershipBody.Defaults.cs](../../../src/Kimi/Compiler/Analysis/OwnershipBody.Defaults.cs) 148行付近、[LocalRegions](../../../src/Kimi/Compiler/Analysis/OwnershipBody.LocalRegions.cs) 275行付近、[Updates](../../../src/Kimi/Compiler/Analysis/OwnershipBody.Updates.cs) 180行付近、[Reservations](../../../src/Kimi/Compiler/Analysis/OwnershipBody.Reservations.cs) 254行付近 | `Calls` が入力対応を保持していても、近傍の `CallEntry` やSourceの一致から対応を再構成する消費者が残る | Call ID・入力範囲・実行出現IDを参照させ、近傍走査を削除 |
| [OwnershipBody.Borrows.cs](../../../src/Kimi/Compiler/Analysis/OwnershipBody.Borrows.cs) 496・1291・1459・2691行付近 | Binding、`source.BoundType`、Kotoの投影形に依存する | 型・Place・転送・result-sourceをMIRから読む |
| [ControlFlowAnalysis.cs](../../../src/Kimi/Compiler/Analysis/ControlFlowAnalysis.cs) `InferLocalType`、1904行付近、[ControlFlowTypes.cs](../../../src/Kimi/Compiler/Analysis/ControlFlowTypes.cs) | Bindingとは別の型表現・構文分類が最終的なflow判断にも残る | Binding前に必要な構造判断と、確定型を読む最終CFG解析を分離 |
| [BodyLowering.Values.cs](../../../src/Kimi/Compiler/Emission/BodyLowering.Values.cs) `ValidateValues`、16–239行付近 | literal・変換・値種別をKotoとBindingから再判定する | MIR定数と明示的変換を物理値へ写す。意味の再検証を削除 |
| [BodyLowering.Calls.cs](../../../src/Kimi/Compiler/Emission/BodyLowering.Calls.cs) `LowerCall`、135–220行付近 | 構文から引数数・対応・型・dispatchを再構成する | 確定CallとABIの対応に限定する |
| [BodyLowering.Match.cs](../../../src/Kimi/Compiler/Emission/BodyLowering.Match.cs) 196–386行付近、[BodyLowering.Graph.cs](../../../src/Kimi/Compiler/Emission/BodyLowering.Graph.cs) `LowerGraph` | 選択済みmatchやownership操作列から実行分岐・終端を再解釈する | MIRに実行CFGとterminatorを保持し、Backendで意味CFGを作り直さない |
| [LlvmEmitter.cs](../../../src/Kimi/Compiler/Emission/LlvmEmitter.cs) 32–38・67–80行付近 | layout/destructor/helperのcallbackがBindingを呼ぶ。文字列失敗のfallbackはInternal/Resource報告へ到達する | semantic instance要求と物理計画を分離。既知の未対応、資源不足、内部欠陥を型付き結果で区別 |
| [ElementAccess.cs](../../../src/Kimi/Compiler/ElementAccess.cs) `ValueSource` / `ConversionKind` | 構文のunwrap、型、権限、indexer/property呼出し、具体化を横断している | 前段のPlace決定に集約。後段は正規化した投影と獲得を利用 |
| [Koto.cs](../../../src/Kimi/Compiler/Parsing/Koto/Koto.cs) 489–520・793行付近 | 構文自身がBinding状態・型・シンボル・失敗・意味レコードを保持する | 段階的にworkspaceの表へ移し、構文を編集・位置・回復の所有者にする |

### 3.1 残すべき複雑さ

次の差は本質的であり、コードを短くするために消してはいけない。

- Originの関係証明と、実際のLoanの同一性・保持・利用権限。
- 依存伝播、権限伝播、初期化、livenessそれぞれのjoinと収束条件。
- 実行CFGと、到達しない構文を検査するためだけの辺。
- 条件付きSemantics case、generic定義時検査とinstanceの表現確認。
- resultの確保、cleanup、resultの引き渡しの順序。
- 宣言の契約、呼出しごとの実際の引数、物理ABI。
- Language/Proof/Unsupported/Input/Resource/Internalの分類。

既存の[SETTLED](../../../docs/SETTLED.md)に従い、generic defaultのinline replica、caseごとの前提、借用経路の制約、既存Hoverスケジューリングを維持する。再編だけを理由に、defaultをinstance evaluator関数へ変更したり、全caseに同じOrigin前提を与えたりしない。

## 4. 目標構造

### 4.1 コンパイルの流れ

```text
immutable source snapshots + configuration + dependencies
    ↓
Lexing / Parsing / directive selection
    ↓
Binding workspace
    declarations → types/contracts → selection + acquisition + obligations
    ↔ 必要な構造的completionと、契約・effectの前提解決
    ↓
Semantic module = 宣言・型・Origin契約の閉じた表 + MIR bodies
    ↓
MIR上の初期化・依存・権限・liveness・effect・cleanup解析
    ↓ exact generation/configurationに対する検証済みのview
Instance coordination → Layout / ABI / Physical lowering
    ↓
閉じたEmissionModule → LLVM writer → artifact / native tools

source map + retained facts → diagnostics / Hover / documentation / CSP用投影
```

これは厳密な一回通過を強制する図ではない。再帰的effectや契約が相互依存する場合、前段内で明示的な依存と有限の収束処理を持つ。重要なのは、選択済みtargetを再選択しないことと、未解決のproofを成功と公開しないことである。

「表現が閉じている」と「契約が検証済みである」は別である。調停は必要に応じて、MIR構築 → 初期化・cleanup確定 → 呼出し・dropを含むeffect集計 → 契約/proof検証 → 依存結果の失効・再計算を回す。現行でもcleanup後のeffectによりconformanceや関連する証拠・projection certificateが無効になる。すべての必要な義務が確定した後だけverified viewを公開し、不成立をoverload再選択で救済しない。最初から一般的なタスク実行基盤を作るのではなく、この依存関係を既存の調停に明示する。

### 4.2 フォルダーと所有者

| 所有者 | 所有するもの | 所有しないもの |
| --- | --- | --- |
| `Core` | snapshot/configuration、世代、準備・失敗・無効化、workspace寿命 | 候補選択、Loan判定、LLVM命令 |
| `Lexing` / `Parsing` | token、Koto、回復、位置、書き戻し | 永続的な最終意味の正本 |
| `Binding` | 宣言索引、型/Origin解決、候補比較、proof、操作の確定、MIR構築 | 後段から呼べる万能意味サービス |
| `Semantics`（仮称） | 宣言/型/Origin/契約の閉じた表、MIR、SourceAnchor ID | Koto、Binding、Compilation、意味探索callback |
| `Analysis` | MIRを読む独立した解析の作業領域・結果・issues | 構文desugar、overload選択、ABI |
| `Emission` | instance要求の処理、表現、layout/ABI、物理IR、LLVM出力 | ソースから意味を再決定する処理 |
| `Documentation` / 投影処理 | 任意の表示、source association、既存サービス向けの切り離された結果 | コンパイルの意味を決める処理 |

フォルダー変更は境界が実際に成立した時点で行う。最初から新しいプロジェクト、interface階層、プラグイン式pass manager、サービスコンテナーを導入しない。privateな具体型・配列・IDで足りる限り、それを使う。共通処理のためだけの `IContext` / `IProvider` を増殖させない。

### 4.3 Bindingの整理

55,331行のpartial `Binding`を、単に同じ可変状態を共有する小クラスへ分けても効果は小さい。まず入力と結果を絞り、次の責任で既存処理をまとめる。

- **宣言と索引:** scope、member、継承、公開契約のidentity。Type/Value等の探索順位は仕様どおりに一度決める。
- **型とproof:** canonical Type/Origin、置換、conformance/witness、Origin solver。構文の解決と、確定した入力の証明を別入口にする。既存の純粋なOrigin proof処理は再利用する。
- **選択:** call/reference/constructionの共通候補列挙・比較を共用し、入力獲得など本当に異なる規則は明示的に残す。未知の前提、曖昧、候補なし、選択後のproof失敗を混同しない。
- **bodyの意味構築:** expression/pattern/placeを決めた順にMIRへ書く。合成 `InvocationKoto` を後段への実行意味の伝達手段にしない。
- **投影:** diagnostics/Hover/documentationは確定した事実を読む。表示のために候補探索やName生成をやり直さない。

多層inheritance、Property、operators、indexersをそれぞれ独立した呼出し機構にしない。前段で共通のCall/Place/更新計画へ正規化し、明示receiverや宣言契約の違いだけを保持する。候補中の仮の推論はworkspaceに閉じ込め、最終表へ出さない。

具体的には、[Binding.Calls.cs](../../../src/Kimi/Compiler/Binding/Binding.Calls.cs) の `TryCandidate` 周辺で退避・復元している `invariantSlots`、`activeInferenceFunction`、`environmentEvidence`、獲得失敗状態と多数の並行配列を、ネスト可能な候補workspaceへまとめる。既存のscratch poolを利用し、候補ごとのheap allocationを追加しない。失敗した候補の情報が次候補や入れ子呼出しへ漏れないことを、候補順変更・失敗後再実行・nested inferenceで確認する。

indexerの `Binding.Indexers`、rangeの `Binding.Ranges`、iterationの `Binding.Sequences` が合成 `InvocationKoto` を作って `BindCall` へ戻す経路は、共通の選択処理を直接呼んでCallを構築する経路へ置換する。表面構文ごとの選択規則は残し、合成構文の生成・保存辞書・Active/reset管理と後段の再探索を削る。

## 5. Kimi向けMIR

### 5.1 Rustから借りるものと借りないもの

Rust MIRの公式説明にある、CFG、入れ子式の除去、明示的な型、local/place/operandという整理を参考にする。[Rust Compiler Development Guide: MIR](https://rustc-dev-guide.rust-lang.org/mir/index.html)（2026-10-10確認）。

Kimiは既に `OwnershipBody` と `OwnershipOperation` を持つため、ゼロからRust相当の全IR群を導入する必要はない。KimiのOrigin・Semantics・準備済み引数・cleanupを表現できるよう既存の骨格を置き換える。Rustのborrow checker、unwind、trait solver、最適化passをそのまま移植しない。SSA化や汎用最適化フレームワークも初期目標にしない。

### 5.2 最小データモデル

以下は責任を示す概念名であり、すべてを別classにする要求ではない。

| 要素 | 保持する内容 |
| --- | --- |
| Module tables | declaration、Type、Origin binder、契約、witness、定数、proof環境の正本 |
| Body | local/temp、Place、block、operation、call、解釈contextのindexed tables |
| Place | root + projection列 + Type + 必要な経路情報。field、tuple、deref、index、base投影を構造化 |
| Operand | 定数または評価済みValue ID。副作用のある獲得を入れ子にしない |
| Operation | 型付き計算、load/store、Acquire、call準備、更新、cleanupの意味。AcquireがPlaceとCopy/Move/Borrow/Reborrowの獲得計画を持つ |
| Terminator | goto / branch / switch / return / abort等。実際に存在する実行継続だけを列挙 |
| Call | target、dispatch、receiver投影、評価順序、parameter対応、獲得、default、result-source、effect契約 |
| Source map | snapshot + span、宣言、展開元、呼出し元。意味判断に利用しない |
| Analysis results | 初期化、Loan依存、権限、liveness、effect、cleanup等。MIRの可変フィールドに混ぜない |

operationが別operationの式ツリーを所有しない。値とblockはIDで結ぶ。一つのSourceSpanからdefaultやdeferの複数の実行出現が生まれるため、SourceAnchor IDとOperation/Occurrence IDを分ける。

獲得はAcquire操作を正本とし、Operandにも同じborrowやMoveを持たせる第二の形式は作らない。symbolicな型には、現在の `CopyOrMove` 等に相当する有限の条件付き獲得計画を保持できる。宣言の獲得契約とadmitted Semantics caseに従って具体化し、ソースの暗黙Moveや新たな候補選択を許すものにはしない。

初期段階では小さいreadonly structと再利用可能な連続領域を優先する。任意長の引数・投影は共通領域の範囲で持ち、ノードごとのListや辞書を避ける。すべてのデータを無理に圧縮することはせず、実測したhot pathから手当てする。

### 5.3 一つのPlaceと一つの操作契約

構文にあるPlaceと機械アドレスは同じものではない。前段がcanonical Placeの経路を決め、解析はその経路からalias/Loanを扱い、Backendは同じ経路を物理アドレスへ落とす。

- 定数indexと動的indexを区別する。Loanの重なり判定に必要な精度を失わない。
- 「ownerの要素」「参照経由の要素」「raw referent」「PropertyやPlace result」をソース形の再走査で判別しない。
- 添字やreceiverなど副作用のある計算は一度だけ評価し、得たOperandを共有する。
- accessの可否と時点ごとのLoan状態を混ぜない。Placeに永続的な `CanBorrow=true` を付けてflowを省略しない。
- 各operationの入力・出力・転送対応を `OwnershipFlow` から発展させた一つの定義で公開する。分類のdefault fallbackで未知操作を安全と扱わない。

### 5.4 呼出し・更新・cleanup

呼出しのreceiver予約、引数の準備、default評価、activation、実行、結果の由来を記録する。引数の評価順とparameter順は別々の情報である。named引数、RHS先行の複合更新、短絡、defaultを単純なparameter順に並べ直してはいけない。

`replace` / `exchange` / `swap` は共通のoperand形を利用しても、異なる転送規則を保持する。通常の代入へ早期に潰して、同時更新・古い値の保持・dropを後段で再推定させない。

cleanupは登録と実行を区別し、通常終了・return・break/exit・failure伝播等の正しい辺に結び付ける。結果はcleanup前に確保し、cleanup後に引き渡す。検査専用の辺から実行上のpredecessor、phi入力、cleanup実行回数を作らない。必要なら同じbody内の別の辺表とし、別の構文ベースCFGには戻らない。

Abort、非復帰呼出し、値としてのResult、`try`による転送を一つの「例外」にまとめない。MIRの継続はKimiの規則から決める。

### 5.5 generic・default・proofの閉包

型が明示的であることは、すべて具体型であることを意味しない。generic bodyはsymbolicなType/Originと宣言前提を明示的に持つ。caseごとの存在条件とproof contextを保持し、instance化はその契約への置換と必要な表現準備を行う。instance化でoverload選択や定義時の不正をやり直さない。

具体化後に必要な所有権・操作・execution profileの検査は、MIR上の同等の義務で覆えたことを確認するまで削除しない。現行 `AnalyzeInstance` と `GenericGenerationDiagnosticTest` が扱う具体型依存の拒否も維持する。抽象的な獲得計画のUnknownと、閉じた具体入力で必要な証拠が欠ける内部不整合を区別する。

defaultは既存のinline replicaの意味を維持する。宣言で確定した操作を複製する場合も、一つの合成されたinterpretation context、準備済みslotのidentity、評価順、cleanupを保持する。新たなinstance evaluator方式への変更は含めない。[SETTLEDの該当判断](../../../docs/SETTLED.md#instance-evaluated-generic-defaults)を再検討するには、そこに列挙された不具合または測定上の根拠が別途必要である。

通常defaultのinline経路とは別に、既に存在する上限付きの再帰default用 `DefaultGenerationPlan` 経路は維持する。新方式を追加しないことと、既存の再帰処理を撤去することを混同しない。この経路も同じ意味契約とcontextを消費する形へ移す。

後段の「閉じた入力」は推移的に確認する。`TypeId → Type → Symbol → Koto` や、Bindingを呼ぶdelegateが残れば閉包していない。Kotoをreadonly wrapperで覆うだけでも不十分である。

### 5.6 未完了状態と無効化

最低限、次の状態を区別する。

1. target/operandsは選択済みだがproofが未完了。
2. 指定された解析に必要な契約が閉じた状態。
3. 必要な解析が成功し、loweringに利用できる状態。
4. invalid / unsupported / resource limit / internal failure。

状態をすべてのノードに巨大なenumとして付ける必要はない。body/契約単位の完了表と依存で表し、未完了のものを成功viewに含めない。再帰effect等の限定的な解析は明示された前提だけで進め、OCC等の未完成proofを既成事実にしない。

IDは所有moduleとgenerationの範囲内で有効とする。source編集、設定・target・依存変更で関連する検証済み状態を失効させる。配列容量を再利用できても、前回の成功を再利用できるとは限らない。CLI/LSP向け結果はworkspaceの可変表から切り離す。

## 6. AnalysisとEmissionを小さくする具体策

### 6.1 Analysis

MIR builderは意味の記録までを担当し、解析は記録された操作から結果を計算する。現行 `OwnershipBody.Borrows` に集まった作業領域を、依存/権限、liveness、content/Loan検査、issue記録に分ける。既に独立している `BorrowLiveness` は再利用する。

依存・権限・保持は現在の連成収束を維持し、必要な結果更新だけを明示して渡す。機械的な「依存を一回、権限を一回」への変更はしない。その収束後にlivenessやrootごとの検査を行う。共通化の対象はoperand探索、worklist/bitsetなど意味が同じ仕組みであり、異なるjoinをstrategy化して一つに見せることではない。

`ControlFlowAnalysis` 全体を直ちに削除することもしない。構文回復や、Binding中の期待型・結果推論に必要な `StructuralCompletion` は前段に残す。最終的な到達性、型付きcompletion、cleanupを含む経路はMIRに集約し、`ControlFlowType`による第二の最終型判断を撤去する。

effect summaryは同じMIRのread/write/call/drop/cleanupから導出し、再帰的呼出しの収束を明示する。Bindingの構文visitorと所有権側で独立にeffectを再発見する方式を終える。

cleanup後のeffectで前提が変わる場合は§4.1の調停へ戻し、既存のconformance証拠などを失効させる。単にeffect解析をパイプラインの最後へ移して、既に発行した証明をそのまま残すことはしない。

### 6.2 Emission

`BodyLowering` の仕事を、検証済みMIRから物理命令・格納場所・ABIへの変換に限定する。literalの言語上のfitting、overload、Placeの意味、matchの網羅性、型推論を移管して削除する。一方、ABI一致、layoutの有限性、物理operand型、terminator、未解決関数参照の検査はBackend固有の責任として残す。

`GenericStoragePlan` のinstance要求と重複除去を基盤に、destructor、Dictionary helper、virtual witness等の要求を型付きで収集する。物理plannerは「このinstance/helperが必要」と要求できるが、自分でBindingの証明や名前探索を行わない。対象依存のCopy/Drop/layout判断に必要な契約は前段から供給する。

`EmissionModule` が物理情報だけを保持する現在の境界を維持する。新しい第三の物理IRは追加しない。publication時に未解決要求と不正CFG/ABIがないことを確認し、失敗時には以前の成功moduleを公開不能にする。writerが単純なシリアライザーでいられる境界を完成させる。

### 6.3 runtimeとライブラリー

Emissionの大きさにはLLVM命令出力だけでなく、五つの `.ll.in` と生成Ryuコード等が含まれる。それらをフォルダー外へ移すだけでは削減ではない。

Iterator、Dictionary等のアルゴリズムと通常のライブラリー操作は、既存方針どおりKimi実装を正本にする。compilerに残すのは表現・allocation/free・atomic・memcpy・ABI・必要な数値primitive等、現在のKimiから直接記述できない狭い操作である。移管時は同じアルゴリズムを二重実装しない。

Dictionaryの検索・成長等やIterator adaptersは既にKimi側にあるため、これから全面移管するとは扱わない。[ライブラリー実装方針](../../../src/Kimi/Library/README.md)を起点に、残るtyped placement/layout bridgeと、`LlvmModuleWriter.Arrays` のgrowth/reserve/shrink等を個別に評価する。Kimi libraryのID・intrinsic signature・effects・ABIの重複表は共通descriptorへ寄せるが、sourceとcompiler期待契約の独立照合は残す。両方を同じ入力から生成して自己照合にすることは検証の削除である。

ただし、Ryu全体の書き直しやcompiler-managed constructorの無理なKimi化を再編の前提にしない。既存の[constructor判断](../../../docs/SETTLED.md#constructor-delegation-whole-self-placement-or-a-factory-for-library-constructors)を守り、runtime出力・ABI・heap allocation・速度が保てる移管だけを別unitにする。

## 7. pre-alphaのUnsupportedを一種類にする

### 7.1 公開する契約

**対象は「仕様上は有効だが実装していない形」に対する診断である。** 公開codeを `Unsupported_Kd`（仮称）の一種類、severityをError、categoryをUnsupportedに統一する。固定メッセージと原因となるsourceの位置を持ち、未対応専用のReason payload、Note、Advice、修正候補を作らない。

統合対象は現行catalogでcategoryがUnsupportedの四種類である。

| 現在のcode | 移行後 |
| --- | --- |
| `UnsupportedBinding_Kd` | `Unsupported_Kd` |
| `UnsupportedOwnership_Kd` | `Unsupported_Kd` |
| `UnsupportedIntegerOperation_Kd` | `Unsupported_Kd` |
| `UnsupportedEmission_Kd` | `Unsupported_Kd` |

`UnsupportedLanguageVersion_Kd` / `UnsupportedTarget_Kd` はInput、`UnsupportedEscape_Kd` / `UnsupportedImportSignature_Kd` はLanguageであり、名前の文字列だけで統合しない。通常のLanguage/Proof診断のAdviceや、既存の構造化修正機構全体の廃止は本提案の対象外とする。今回の「Adviceを生成しない」は未対応報告に適用する。

### 7.2 簡素化しても残す保証

- 未対応を判定できる最初の所有者で一度記録し、その操作に依存する後続処理を止める。別の独立した宣言や問題まで隠さない。
- public codeが一種類でも、内部のcause/obligation identityは保持する。同じ位置だから同じ原因とは扱わず、同じ障害を各フェーズから重ねて報告しない。
- 主位置は不変snapshot上の有効なspanを使う。展開・instance・合成処理は既存source mapから利用箇所へ帰属させ、架空の位置を作らない。
- `check`が行う前段検査と、`generate`が追加して行う実行profile検査の境界は維持する。診断統一のためだけにcheckでBackendを起動しない。
- 資源上限はResource、閉じたIRの不整合や予期しない例外はInternalのままとする。「何でもUnsupportedにして安全に見せる」ことを禁止する。
- unsupportedが起きたbody/instanceに成功証明や実行可能moduleを与えない。失敗後の再準備で古いfailureも成功も残さない。

feature名、Semantics case、instanceの説明用文字列を公開しないため、その目的だけに存在する整形・候補探索・名前生成・保持フィールドを実際に削除する。出力時にAdviceを隠すだけでは完了としない。内部ログに必要な短い識別子がある場合も、公開診断の代替となる巨大な説明モデルにはしない。

### 7.3 仕様との関係

現在の[SPEC §23.3.6](../../../docs/spec/23-compiler-services.md#2336-diagnostics)は、requirementごとのcode、型付きReason、caseの説明、前提失敗等を規定する。汎用Unsupportedは単なるリネームではなく、**pre-alphaのUnsupportedに限ったサービス契約の意図的な簡素化**として先に規定する。

採用時の同一unitで、§23の例外、関連する実装文書、catalog、当該テストを更新する。言語として受理すべき形は変えず、STATUS/PLANに未対応範囲を残す。位置・snapshot・causeによる追跡は維持し、複数codeと助言生成の負担をなくすことでPrinciple 1を強め、未対応を明示してPrinciples 2・3を守る。CSPの検証可能な入力同一性も維持する。

取り込み時だけ `draft/INTEGRATED.md` に本提案の採用節と残項目を記録する。今回の提案書作成を仕様取り込み済みとは扱わない。言語versionの更新やmigration文書は不要である。

### 7.4 狭い形だけの部分実装を増やさない

新しい機能は「一つの構文例を通す」単位でなく、意味上の能力単位で実装する。例えばPlace借用なら、根・投影・獲得・結果の保持・更新/cleanupの組合せを共通表現で扱えることを完成条件にする。未対応時に「local初期化子でnongenericのこの形だけ」を追加して、その外側のAdviceを増やす方式を避ける。

これは巨大な一括実装を要求しない。内部の置換は小さなunitで進められるが、未完成機能を公開受理して範囲を拡大しない。既存の限定対応は移行中も保ち、一般経路が同じ挙動を覆った時点で限定経路を削除する。未対応の全言語機能を再編中に完成させることも求めない。

## 8. 削除台帳と実施順序

### 8.1 削除台帳

| ID | 現在の処理 | 正本・置換先 | 完了時に消すもの |
| --- | --- | --- | --- |
| D1 | phase別Unsupportedと説明生成 | 一つの位置付き診断 | 四codeの旧経路、未対応専用Advice/修正案/文字列payload |
| D2 | call入力・result-sourceの再構成 | MIR Call + indexed inputs | Source一致・近傍CallEntry走査、二重argument mapping |
| D3 | source-backedな型/契約/dispatch | Semantic module tables | 後段からのKoto/Binding参照と意味探索callback |
| D4 | syntax/ownership/emissionごとのPlace分類 | canonical Place/projection | 後段のElementAccess分類、二重unwrapと権限経路探索 |
| D5 | body構築と最終flowの重複 | MIR builder + MIR CFG | 第二の最終型体系、実行CFGの再推定、重複match/値分類 |
| D6 | Binding構文visitorのeffect解釈 | MIR effect summary | 構文からのeffect再探索。契約の宣言検査は残す |
| D7 | default/case/instanceでの構文再解釈 | symbolic MIR + explicit context | 移行済み領域のsource replayと補助的な再Binding |
| D8 | lowering中の意味検査 | verified MIR → physical module | literal fitting、言語上のcall/match判定、helper用Binding callback |
| D9 | 構文に保持する最終意味状態 | workspace表 + detached projections | Kotoの移行済みsemantic field/resetと重複cache |
| D10 | 候補ごとの状態をBinding全体で退避復元 | ネスト可能な再利用candidate workspace | 選択専用global scratchの退避復元と並行配列の個別管理 |

一つの行全体のコードが不要になるという意味ではない。例えばD5で構造的completion、D6でeffect契約のチェック、D8で物理整合性チェックは残る。削減量は新しい正本・adapter・テスト補助まで含む差分で数える。

### 8.2 実施unit

| 順序 | 作業 | 削除・完了ゲート | 依存 |
| --- | --- | --- | --- |
| P0 | baseline、受理/拒否/未対応matrix、依存・状態の台帳を固定。旧U台帳と統合 | 実装対象と中止条件が明確。停止中の別機能を巻き込まない | なし |
| P1 | Unsupportedの契約変更と実装簡素化 | D1完了。未対応専用Advice生成なし。位置・分類・失敗後再準備を検証 | P0 |
| P2 | 呼出し・更新を対象に閉じた契約とMIRへの置換を試行 | D2と当該経路のD3/D8。旧経路を削除し、pureな後段で同じ実行を確認 | P0。P1とは独立可能 |
| P3 | Place/acquisition/型付き値を共通化し、body構築を移管 | D4、値/literal/変換の再解釈撤去。既存のsupportedな投影を維持 | P2の採否判定 |
| P4 | 分岐・match・completion・cleanupを同じMIR CFGへ統合 | D5。実行辺と検査辺、結果保護、deferの一致 | P3 |
| P5 | 解析結果と作業領域を分離し、effectをMIRへ移管 | D6。連成収束とlivenessの順序、effect/cleanup契約を維持 | P4 |
| P6 | generic/case/default/instanceの閉包を完成 | D7。全消費者が明示contextで動き、overload再選択なし | P2から準備、完了はP5後 |
| P7 | Binding責任・AST意味状態・投影を整理し、Backend境界を完成 | D3/D8/D9残項、D10とadapter撤去。writerまで推移的な依存検査を通す | P3–P6 |
| P8 | 全体再測定と対応matrixの照合 | 削除台帳、性能、機能、source/configuration証拠が揃う | P7 |

P1は他の構造置換から独立して先行できるが、case統合・前提抑制・公開契約に触れるため、その検証は必要である。主な構造削減はP2以降になる。P7まで既存Binding全体を作り直す必要はない。各経路に必要な閉じた契約を先行して用意する。genericやdefaultはP6まで無視せず、P2から互換性の難しい例を含める。

各Pは一つの巨大commitではなく、表の完了条件を共有する複数の検証unitである。たとえばP2はcall入力の残存consumer、閉じたtarget/type契約、物理引数対応の順に分け、unitごとに旧処理を撤去する。別agentや別セッションで並列作業する場合も、同じ契約の定義・変更は一つの所有者が調停する。

### 8.3 最初の置換試行と継続判定

最初の対象は新しいscalar専用backendではなく、既存のCall/更新経路とする。`OwnershipBody.Calls` と `OwnershipFlow` が既にあり、残る消費者とloweringを置換して削除効果を測れるためである。

試行には、direct/value呼出し、named引数、inline default、返却borrow、汎用型の具体化、全体値更新、失敗途中のcleanupを含める。単純な整数加算だけが通る新IRを作って成功とはしない。移行対象でない機能は既存実装で動かし、productionで同じ操作を二つの意味経路に流さない。比較実行が必要なら検証専用とし、撤去期限をP2の完了に置く。

移行済みの消費者は閉じた契約だけを受け取る。暫定adapterは未移行の前段から事実を作る側に限定し、Koto/Bindingを後段へ通す穴にはしない。そのadapterの費用も削減量へ含め、P7までに全撤去する。

P2完了時に、関連コードの純減、再解釈箇所の減少、warm allocation、保持容量、compile時間、native出力を比較する。新しい基盤の費用で純減しない場合は、その理由とP3で削除する具体的な旧処理を示す。解消の根拠がなければ横展開を止め、表現をさらに小さくする。最終的な削減率を調査段階で断言しない。

## 9. 検証と評価

### 9.1 維持する振る舞い

受理、Language/Proof拒否、Unsupported、Resource、Internalを別々に比較する。意図した差分はP1の未対応診断だけとし、受理済みのプログラムを未対応に戻す差分を見逃さない。既存実装にも既知の欠陥があるため、旧実装との一致だけを正しさの根拠にせず、SPECの期待を正本にする。

| 対象 | 主な既存テストの入口 | 必要な観測 |
| --- | --- | --- |
| 呼出し・更新 | `CallArgumentMappingTest`, `ReturnedBorrowAncestryTest`, `WholeValueTest` | 評価順、引数対応、Loan由来、更新前後、cleanup |
| 共通flow | `SharedEngineTotalityTest`, `OwnershipAnalysisTest`, `OwnershipJoinTest`, `UnreachableOwnershipTest` | operation網羅性、異なるjoin、検査辺と実行辺 |
| completion/CFG | `ControlFlowAnalysisTest`, `ControlFlowConformanceTest`, `CurrentControlFlowTest` | Never、分岐、転送、result、回復中の構造判断 |
| generic/default | `GenericDefaultTest`, `DefaultOwnershipTest`, `GenericOwnershipCaseTest`, `GenericGenerationDiagnosticTest` | context合成、prepared slot、caseごとの前提、失敗したinstance |
| effect/契約 | `EffectBoundImplementationTest`, `CallableEffectBoundTest`, `ConformanceConvergenceTest`, `IteratorOriginEffectsTest` | cleanup後に不成立となるconformance、再帰effect、証拠の失効 |
| 候補状態 | `ConstructorInferenceReuseTest`, `AnonymousArgumentInferenceTest`, `InferenceSlotScalingTest` | 入れ子、候補順、失敗後の再利用、allocation |
| library契約 | `CatalogSignatureValidationTest`, `FormattingSignatureValidationTest` | sourceとcompiler側期待契約の独立照合 |
| Backend | `MinimalEmissionTest`, `DictionaryLibraryTest` と対象機能のemission/native fixture | 推移的な閉包、物理ABI/CFG、失敗後の再準備、O0/O2実行 |
| 診断 | `DiagnosticContractTest` と変更codeを所有する既存クラス | 一種類のUnsupported、位置、分類、独立原因、CLI/JSON/LSPの同一記録 |
| proof/performance | `OriginProofScalingTest`、変更経路のallocation/reuseテスト | 結果の世代整合、上限、固定条件でのallocationと保持容量 |

これは入口であり、テストの網羅一覧ではない。対象unitごとに[CODEMAP](../../../docs/dev/CODEMAP.md)から所有クラスを選び、機能を観測する最も低い層へ追加する。MIR内部の実装手順を写しただけのテストを大量に作らない。

### 9.2 構造上の検証

次の性質は通常の機能テストだけでは見逃すため、既存の閉包・totality検査を拡張する。

- MIRと後段の保持型を推移的に辿ってもKoto/Binding/Compilationへ到達しない。delegateやhelper経由もレビューする。
- 全operationについて必要なoperand・転送・terminator情報が定義される。不明なkindは成功にしない。
- public結果は元workspaceを再利用・破棄しても変化しない。古い世代のID・証明を拒否する。
- 宣言/useの編集、target/configuration/依存変更、失敗後の再成功でcacheが正しく失効する。
- 物理moduleの全参照、ABI、block終端、必要なcleanupが閉じる。LLVMが受理するだけで意味の正しさを代用しない。

### 9.3 サイズ・性能の測定

baselineと同じ再帰範囲で、全ファイル容量、LF正規化容量、C#行数、ファイル数を測る。Compiler外へ移したproductionコードと追加runtimeも別欄に集計し、フォルダー移動による見かけの削減を除く。生成コード、コメント、テスト、計測支援の増減も区別する。

さらに、後段の意味再探索箇所、同じ事実の表の数、一時adapter数を台帳で追う。最終条件はadapterゼロ、後段からの意味探索ゼロ、旧経路の撤去であり、検索語の件数ゼロだけで達成とはしない。

性能はcold/warmのコンパイル時間、phase時間、GC allocation、保持容量、instance数・MIR操作数に対する増加を測る。既存の [CompilerPipeline](../../../src/Benchmark/CompilerPipeline.md)、[GenericDefaults](../../../src/Benchmark/GenericDefaults.md)、[OriginProof](../../../src/Benchmark/OriginProof.md)、[ElementPlaces](../../../src/Benchmark/ElementPlaces.md)、[BorrowStorage](../../../src/Benchmark/BorrowStorage.md)、[CallablePlans](../../../src/Benchmark/CallablePlans.md) 等の関連workloadを使う。

warmのゼロallocation保証を緩めない。固定warm-upと同じ入力・反復・構成・toolchainで比較し、失敗後の追加warm-upで通った値に差し替えない。実行性能はnative O0/O2の結果、heap allocation、IR/実行ファイル容量、関連する時間計測で確認する。測定誤差を超える悪化には原因と修正を求め、コード行数の削減だけで採用しない。

### 9.4 実装時の正式検証

[VERIFICATION](../../../docs/dev/VERIFICATION.md)どおり、reproducer・実装・focused testを一unitとし、関連Unit検証後にcommitする。セッション終了時は一回のSession検証を行う。最終Sessionが最後のUnitに必要なnative fixture・milestone等も含めるなら重複実行しない。fixtureとmilestoneは必要なものを明示選択する。

allocation/reuse回帰を含め、性能変更には関連測定を追加する。検証中はsourceを編集しない。失敗を含む証拠を `artifacts/verify/`、計測を `artifacts/benchmarks/` に残し、通過した同じsource/configurationのcommitだけをoriginへpushする。NativeAOTは実行しない。

## 10. 完了条件と今回の成果

再編全体は、次を満たした時点で完了とする。

1. 現行の検証済み機能と性能保証を維持し、既知の未対応範囲を正確に残している。
2. 未対応は一種類の位置付き診断になり、専用のAdvice・回避案生成が削除されている。
3. 意味IRは既存の所有権表現を置換し、Binding・解析・Backend間で同じ操作を解釈し直さない。
4. Analysisの入力と結果の所有者が明確で、coupledな収束、case、cleanupの保証を保っている。
5. Backendは閉じた意味契約から物理表現を生成し、Bindingや構文へ戻らない。
6. 一時adapter・旧経路が残らず、Compiler外への移動分も含めてproductionコードが純減している。
7. 変更に対応するCODEMAP、アーキテクチャ台帳、PLANを更新し、実証した対応境界の変更だけをSTATUSへ記録している。仕様変更は正式文書とINTEGRATEDへ同時に記録している。

今回の成果はこの提案書と調査台帳である。コンパイラー実装、正式仕様、STATUS、停止中の実施計画、既存draftの本文は変更していない。文書の差分・参照を検証し、実装用のビルド・テストは行わない。
