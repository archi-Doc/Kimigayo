# コンパイラー縮小計画：意味表・単一 MIR・汎用 Unsupported

日付：2026-10-10。状態：採用（2026-10-10、利用者の決定）。一部取り込み（R1a から。取り込んだ範囲は `draft/INTEGRATED.md`）。決定事項は §18。

調査基点：`419e8e79`（`dev`）。行数は同コミットの `wc -l`（コメントと空行を含む）。本文の `path:line` は、断りがなければ `src/Kimi/Compiler/` からの相対パスで、基点での位置を示す目印である。

前提：2026-10-10 の利用者判断 D1〜D6 を拘束条件とする（§1.3）。D1〜D6 は同じ依頼に対する兄弟計画 [2026-10-10 Compiler Reduction and MIR Plan](<../Obsolete/2026-10-10 Compiler Reduction and MIR Plan.md>) の §14 に記録されている。本計画はその本文には依存しない独立の案であり、相違点は §17.2 にまとめる。

本書は計画である。言語規則の変更、実装、ビルド、テストは行っていない。SPEC・IMPL の改訂案は §11 にまとめ、採用後に該当する段階の単位で行う。

本文の記号：肥大の要因は F1〜F10（§3）、相は P1〜P13（§5.1）、MIR 上の解析は A0〜A6（§5.5）、Binding に残す Origin の判定は OJ1〜OJ7（§5.5）、受理の差分は C1〜C6、T1〜T4、X1〜X3（付録 B）、移行の段階は R0〜R9（§12）とする。O0／O2 は最適化レベルを指す。

---

## 0. 要約

- **現状**：`src/Kimi/Compiler` は C# 458 ファイル・134,591 行に、IR テンプレート（`.ll.in`）2,335 行を加えた 136,926 行である。2026-09-29（`36dae5c5`）の 90,568 行から 11 日で約 4.4 万行増えた。同期間（`36dae5c5..419e8e79`）に Compiler を変更したコミットは 557、追加 67,122 行・削除 23,039 行である。
- **診断**：肥大の大半は、10 の構造上の要因（§3）が「1 つの形を追加する」単位の修正と組み合わさって生じた。最大の要因は、意味の決定を置く場所（構文から独立した意味表と IR）がないことである。そのため、Binding・制御フロー解析・所有権解析・コード生成がそれぞれ同じ事実を構文から導き直している。
- **方針**：次の 5 本の柱で、概念の数を減らしながら全体を組み直す。
  1. **未対応報告を 1 種類にする。** 位置だけを持つ `Unsupported_Kd` を、決められた場所だけが出す。狭い部分実装は作らない（§6）。
  2. **意味を構文から切り離す。** どの相も Koto を書き換えない。Binding の結果は `SyntaxId` をキーにした意味表と 4 種の Plan に置く（§5.3）。
  3. **単一の MIR。** 定義ごとに 1 回だけ構築し、初期化・借用・drop・効果・到達性の全解析と、単相化・コード生成の唯一の入力にする（§5.4〜§5.7）。
  4. **Binding 中核の統合。** 推論変数を持つ単一の推論器、単一の Contract ソルバー、全呼び出し形式に共通の CallPlan パイプラインにする（§8）。
  5. **アルゴリズムはライブラリへ。** Array・Dictionary・文字列・書式化の操作を Kimigayo で書く。コンパイラーが個別に知る操作は、現在の `CompilerFunctionKind` 75 種から約 28 種の intrinsic に減らす（§5.8）。
- **規模**：Compiler 配下から移設する分（Documentation 4,338 行、ツールチェーン 1,317 行）を除いた 131,271 行を、**約 84,350 行（−36%）**にする。このうち約 4,200 行（Hover 1,651 行と、ライブラリ検証 約 2,557 行）はテストと Checking への移設で、これも除いた実質の削減は約 127,000 → 84,350（−34%）である。移設後の最終の合計上限は 85,850 行（§12.2 の R9）。移設を含めた見かけの変化は 136,926 → 84,350（−38%）である（§10）。
- **進め方**：段階 R0〜R9 で進める（§12）。
  - MIR の前に、削除先行の段階（R1、R2a）で約 1.2 万行を減らす。このうち約 4 千行はテストと Checking への移設である。
  - 意味表を完成させ（R2b）、MIR を旧経路と並走させて差分を分類し（R3〜R5）、1 回で切り替えて旧経路を削除する（R6）。
  - その後、Binding の統合（R7）とライブラリへの移設（R8）を行う。
  - 並走中の最大規模は約 149,500 行と見込む。
- **利用者の判断**：D1〜D6 と §16 の 15 項目は、2026-10-10 にすべて決まった（§18）。

---

## 1. 目的・範囲・拘束条件

### 1.1 目的

- 機能・正しさ・性能を維持または改善しながら、コンパイラーを小さく、単純で、一貫した構造にする。
- 局所的なリファクタリングではなく、責務の境界とデータの流れを組み直す。
- 今後の機能追加が「意味表か Plan の行を 1 つ」と「MIR ビルダーの関数を 1 つ」で済む構造にする（§14）。

### 1.2 範囲

- 対象は `src/Kimi/Compiler` の全領域（Lexing、Parsing、Binding、Analysis、Emission、Core、Helper、Documentation、ルートの共有ファイル、`.ll.in`）。
- 新しい契約に合わせて変更する境界：
  - `src/Kimi/Diagnostics`（カタログと要件の対応、診断の主語と順序。§7）
  - `src/Kimi/Library`（表現 struct と intrinsic。§5.8）
  - `src/Kimi/Checking`（Hover の移設）、`src/Kimi/Lsp`（Hover と診断の受け取り）
  - `src/Kimi/Testing`（`TestCatalog` が Koto に書く `SiteId` と記号の参照を意味表へ、`TestRuntime` の関数表を Backend の Instance → シンボル表へ）
  - `src/Kimi/SolutionAndProject`（相の列、起動検査、テスト発見）、`src/Kimi/Unit`（Build 名前空間への追従）
  - `src/Benchmark`（計測入口の追従と、新旧で比べる段の境界。§13.3）
  - `src/backend/windows-x64` の検査スクリプト（マイルストーンハーネス、手書き IR を使う検査）
  - `tests`（§13.2）
- 対象外：新しい言語機能、CSP の通信層、別のバックエンド、非同期実行、NativeAOT。凍結中・保留中の作業（G55/G60 など）は再開しない。

### 1.3 拘束条件

| 区分 | 内容 |
| --- | --- |
| 利用者判断 D1 | 2026-10-09 の計画（`620b2a1a`）は置き換える。その境界規則は引き継ぐ：意味を一度だけ決める、解析と生成は構文に依存しない、検査専用の経路と実行時の経路を区別する、インライン既定値、Semantics ケース、ソース位置の保持。 |
| 利用者判断 D2 | 狭い部分実装は新経路へ移植しない。マイルストーンの一時的な後退は、記録すれば許容する。 |
| 利用者判断 D3 | Advice と Note を減らす仕様変更を行う。SPEC が要求する事実、関連位置、修復候補、構文の診断は残す。 |
| 利用者判断 D4 | 借用検査は SPEC §15 から作り直す。受理範囲の変化は、差分を一件ずつ SPEC に照らして分類すれば許容する。 |
| 利用者判断 D5 | 移行中は旧経路（旧 Analysis／Emission）への機能追加を凍結する。 |
| 利用者判断 D6 | Unsupported の記録は位置だけを持つ。Reason も構文の種類も付けない。 |
| 仕様 | [SPEC](../../docs/SPEC.md) と [IMPL](../../docs/IMPL.md) が要求動作を定める。弱めるのは、§11 に理由を書いた意図的な改訂に限る。 |
| 確定事項 | [SETTLED](../../docs/SETTLED.md) を守る。特に「Instance-evaluated generic defaults」（既定値はインライン複製）と「Pair-slot premises in every Semantics case」（前提は Semantics ケースごと）。前者の理由の記述が変わる箇所は §5.6.2 と §11 で扱う。 |
| 実装規則 | [AGENTS.md](../../AGENTS.md)：割り当てを最小にする、ゼロアロケーションと再利用の回帰を緩めない、コアライブラリは手書き IR ではなく Kimigayo で書く、コードは足すより消す、概念の少なさで設計を評価する。 |

---

## 2. 現状の計測

### 2.1 規模

| 領域 | 行数 | ファイル数 | 備考 |
| --- | ---: | ---: | --- |
| Binding | 60,703 | 175 | 全体の 45% |
| Emission（C#） | 20,859 | 90 | うちツールチェーン 1,317 行。ほかに `.ll.in` が 5 ファイル・2,335 行 |
| Analysis | 20,613 | 57 | |
| Parsing | 17,712 | 86 | |
| Documentation | 4,338 | 11 | コンパイラーの意味処理とは独立 |
| Lexing | 3,968 | 6 | |
| Helper | 3,472 | 7 | うち `UnicodeIdentifierData.cs` 1,687 行は生成データ |
| Core | 1,584 | 10 | |
| ルートの共有ファイル | 1,342 | 16 | `ElementAccess.cs` など |
| **合計** | **134,591**（`.ll.in` を含めて 136,926） | 458 | |

テストは `tests/xUnitTest` に追跡対象の `.cs` が 739 ファイル・115,659 行あり、`Purpose=Allocation` の印は 648 箇所に付いている。

### 2.2 増加の推移

| 日付 | コミット | C# 行数 |
| --- | --- | ---: |
| 2026-09-29 | `36dae5c5` | 90,568 |
| 2026-10-01 | `bd609a71` | 95,683 |
| 2026-10-03 | `e30fce4b` | 102,838 |
| 2026-10-05 | `4a9fc223` | 111,690 |
| 2026-10-07 | `6eaa12f6` | 123,676 |
| 2026-10-09 | `cc01af6d` | 133,066 |
| 2026-10-10 | `419e8e79` | 134,591 |

個別の増加の例：

| 対象 | 2026-09-29 | 2026-10-10 |
| --- | ---: | ---: |
| `Binding.FunctionTypes.cs` | 206 | 1,258 |
| `Binding.Diagnostics.cs` | 197 | 2,496 |
| 借用ソルバー一式（`OwnershipBody*`、`OwnershipModel`、`OwnershipFlow`、`BorrowLiveness`、`PackedAnalysisTable`、`OwnershipStorage*`） | 3,663（8 ファイル） | 8,183（21 ファイル） |
| `BodyLowering*.cs` | 9,389（33 ファイル） | 10,464（37 ファイル） |

### 2.3 構造の指標

| 指標 | 値 | 意味 |
| --- | ---: | --- |
| Koto の基底クラスにある意味スロット | 7 種 | `Parsing/Koto/Koto.cs:488-527`。`BindingState`、`BoundType`、`BoundOrigin`、`BoundSymbol`、`ErasedFunctionType`、`FormattingStorage`、`BindingFailure`。構文木が意味データベースを兼ねている |
| Binding の Koto キーの nullable 辞書 | 47 | Koto の派生型をキーとするものを含めると 52。うち 32 は失敗説明の退避表（`FailExplained`） |
| `BindingFailure` の値 | 89 | `BindingModel.cs:121`。コード対応の switch（`Binding.cs:1127-1216`）と、約 450 行の `ReportIssue`（`Binding.cs:600-1049`）が並行する |
| Analysis／Emission から Binding への問い合わせ | 約 110 種・約 280 箇所 | 下流が意味を導き直している |
| 括弧の展開（`UnwrapParentheses`） | 約 283 箇所 | Binding 164、Analysis 78、Emission 22、ルート 19 |
| 所有権 IR の操作種・値種 | 39・31 | `OwnershipModel.cs:58-99,571`。演算子は構文の `KotoKind` のまま |
| `OwnershipModel` の付随表 | 約 49 | 機能ごとの側表 |
| 物理 IR のオペコード | 49 | `EmissionModule.cs:10-75`。命令は 19 フィールドの和集合で、文字列のサブオペコードを持つ |
| Emission の文字列つき `Fail` | 448 | うち補間文字列 8。位置のない `GenerationFailed_Kd`（Internal）になる。`out string? failure` は 44 ファイル・103 箇所 |
| `CompilerFunctionKind`／`KimiDeclarationId` | 75／187 | ライブラリ操作をコンパイラーが個別に知っている |
| 解析・生成の再構築 | ケース最大 64 回、インスタンスごと | `OwnershipAnalysis.Cases.cs:138-183`、`OwnershipAnalysis.Instances.cs:49-94`（`LlvmEmitter.cs:429` から起動） |

---

## 3. 肥大化の要因

各要因の影響行数は互いに重なるので、合計しても総削減量にはならない。

| # | 要因 | 代表的な証拠 | 影響（概算） |
| --- | --- | --- | ---: |
| F1 | **意味の置き場所がない。** 結果を Koto に書き込み、構文から独立した HIR がない。下流は構文と Binding への問い合わせで事実を再導出する | `Koto.cs:488-527`。再束縛のたびに全ノードを初期化する `IndexVisitor`（`Binding.cs:1566`）。Declare の副作用を直後に戻すパッチ（`Binding.cs:1848-1855`）。`ElementAccess.cs:54` は構文から Binding を照会し、37 個のヘルパーが 257 箇所から呼ばれる。Parsing から Binding を逆に呼ぶ（`FunctionKoto.cs:106`） | 6〜8k |
| F2 | **閉じた操作語彙がない。** 脱糖を合成構文と機能別の側表で行う | 合成 Koto 4 種と合成キャッシュ 7 表（`Binding.Ranges.cs:13-20,105-145`）。下流の機能別分岐：`ControlFlowAnalysis.cs:701-768`（14 種）、所有権 IR の 39＋31 種、物理 IR の 49 種、文字列のサブオペコード約 22 種（`LlvmModuleWriter.Sequences.cs:36`） | 5〜6k |
| F3 | **Place・Loan・Call の正規の表現がない。** 各フェーズが自前の表現を作る | Place の経路を 4 層で再判定（`Binding.ArgumentOperations.cs:178-896`、`OwnershipAnalysis.Borrows.cs:172-408`、`OwnershipBody.Borrows.cs` の来歴ウォーカー約 1,150 行、`BodyLowering.StructBorrows.cs:50-70,457-490`）。Loan は 3 系統（`OwnershipModel.cs:670-672`）。流れに依存する Loan 伝播が 5〜6 種。呼び出しは Binding で 12 形態・計画型 2 種、Analysis で 2 系統、Emission で 3 系統 | 7〜9k |
| F4 | **下流が上流を信用しない。** 検証済みの事実を再検証する | Emission の `Fail` の過半が内部不変条件の再検査（`BodyLowering.cs:55-247`、検証専用の支配木 `BodyLowering.Results.cs:79-228`、全命令の二重生成 `BodyLowering.Graph.cs:289-336`）。ライブラリ検証を Bind ごとと Emission で 2 回実行（`Binding.cs:105,413`、`LlvmEmitter.cs:523`、検証コード 2,557 行）。制御フロー解析が第 2 の型検査器を持つ（`ControlFlowTypes.cs:11`）。完了性を 4 系統で計算して実行時に突き合わせる（`OwnershipBody.Checking.cs:202`） | 6〜7k |
| F5 | **判定と説明が密結合している。** | 失敗の三重ディスパッチ（`BindingFailure` → switch → `ReportIssue`）。新しい診断 1 つに 6 箇所の変更が要る。説明のための再推論・再証明（`Binding.FunctionTypes.cs:954-1032`、`Binding.Diagnostics.cs:503-655` の独自単一化器）。候補ごとに却下理由の事実を返す（`Binding.Calls.cs:1249` の `TryCandidateCore` の out 引数。失敗時の組み立ては `:775-814`）。重複報告の抑制パッチ（`OwnershipBody.Reservations.cs:9-22`。7 コミット・約 860 行） | 5〜6k |
| F6 | **未対応の表し方が多経路・多層で、狭い部分実装を支えている。** | §6.1。利用者向けの Note に「PLAN G59」が出る（`OwnershipAnalysis.Reservations.cs:26-31`）。同じ制限（i128 の除算）を 6 箇所で判定 | 2.5〜3k（部分実装を含めると＋1.5〜2.5k） |
| F7 | **Origin と効果を型検査の途中に構文の上で解き、Analysis と二重化している。** | 独立した Origin ソルバーが 5 つ（`Binding.OriginDeclarations.cs:519`、`Binding.OriginInference.cs:115`、`Binding/OriginPremiseClosure.cs:215`、`Binding.LocalRegions.cs:79`、`OwnershipBody.LocalRegions.cs:508`）。fit が副作用を持つので巻き戻し機構が要る（`Binding.OriginProof.cs:247-263`）。Emission が Origin の fit を再実行する（`BodyLowering.Updates.cs:118`）。効果は構文の第 3 の列挙器で計算し（`Binding.EffectBounds.cs:214-850`）、Binding と Ownership の間で循環呼び出しがある（`:1567` → `OwnershipAnalysis.EffectBounds.cs:46`） | 4〜5k |
| F8 | **インスタンス化を 3 層で行い、文脈ごとに構文から再構築している。** | Semantics ケースごとに最大 64 回の再構築。インスタンスごとの再解析を Emission から起動。`InterpretationContext`（型名で 72 箇所）と、文脈つきの再解決 `Resolve`／`ResolveCall`／`SubstituteDefaults`（Analysis と Emission で 143 箇所）。インスタンス収集器が 5 種（`GenericStoragePlan`、`ObjectGenerationPlan`、`VirtualGenerationPlan`、`DefaultGenerationPlan`、`CompilerFunctionAdapters`） | 3〜4k |
| F9 | **ライブラリ型をコンパイラー内で特別扱いしている**（AGENTS の方針と逆行） | Array の成長を手書き IR で持つ（`LlvmModuleWriter.Arrays.cs:8-135`）。同じアルゴリズムが Kimigayo で `src/Kimi/Library/DictionaryStorage.kimi:52-122` にある。メンバー名の綴りで判定（`Binding.Sequences.cs:13-55`、`Binding.PositionWarnings.cs:159-201`）。組み込み適合の特例が 4 機構。構造体レイアウトを 3 重に管理（`KimiLibraryFormattingLayouts.cs:11`、`LlvmModuleWriter.Formatting.cs:14,43`、`Utf8*.ll.in`） | 5〜7k |
| F10 | **推論変数・単一ソルバー・共通の走査関数がない。** | 型推論の実装が 5 つ（`Binding.ArrayInference.cs:23` は「推論変数がない」と明記）。Contract 判定の入口が 15、循環ガードが 8、不動点が 4。変性つき型走査が 8 本以上、「Origin を含むか」の述語が 14、11 引数の型再構築が 14 箇所。定数評価が 4〜6 本 | 3〜4k |

**進め方の要因。** F1〜F10 の構造のまま「1 形状の追加」「1 ケースの報告」を単位に修正を重ねたことが、肥大を加速させた。例として、`Binding.Diagnostics.cs` の `IsDerived` には 7 個の OR 節があり、7 つのコミットが 1〜2 行ずつ追加している（`Binding.Diagnostics.cs:2120-2132`）。構造の欠陥を局所の分岐で覆う修正は、次の修正の前提を増やす。§14 の規則でこの進め方を止める。

### 3.1 横断的な重複

| 重複の束 | 主な構成要素 | 統合先 |
| --- | --- | --- |
| Place の経路と権限の分類 | `Binding.ArgumentOperations.cs:178-896`、`Binding.Expressions.cs:28-122`、`ElementAccess.cs`、`OwnershipAnalysis.Borrows.cs:172-408`、`OwnershipBody.Borrows.cs:2222,1826,2680`、`BodyLowering.StructBorrows.cs:50-70`。移動パス木 3 種 | MIR の Place（根＋射影列）と MovePath 木 1 つ |
| 呼び出し | `BoundCall`／`BoundValueCall`、合成アクセサー呼び出し、`BoundIteration`、Analysis の `Call`／`CallValue`、Emission の 3 系統 | CallPlan 1 種と MIR の `Call` 終端 |
| Loan と一時借用 | ComparisonLoans（`comparisonDepth` が 14 ファイル・67 箇所）、CallReservations、Origin 依存表、StoredBorrows、LocalRegions、Referents、BorrowLiveness | Loan 1 種と、汎用ビット集合データフロー |
| Origin と領域 | Binding の 5 ソルバー、`OwnershipBody.LocalRegions.cs`、Emission の再 fit | Binding は署名と選択後の代入、判定は MIR の領域ソルバー 1 つ（§5.5） |
| 完了性と結果型 | `StructuralCompletion`、制御フロー解析の Flow 合成、`Binding.ControlFlow.cs`、規則のずれたコピー `BindingControlFlowTypes.cs:83` | HIR の完了要約 1 つと MIR の到達性 |
| drop と cleanup | CleanupSteps、`BodyLowering.Strings.cs`（名前と違い全所有型が対象）、フラグ 2 系統、needs-drop の述語 4 つ、IR の逆順破棄ループ 4 つ | MIR の drop elaboration と DropGlue |
| match | `Binding.Patterns.cs`、`OwnershipAnalysis.Match.cs`、`BodyLowering.Match.cs:140-442`（実行用 CFG を再構築）、`CompositeMatch`、`MatchTypes.SupportsGuard`（3 相で判定） | MIR 構築時の判定連鎖 |
| 既定引数 | 既定値専用の 8 ファイル（`DefaultParameters.cs`、`InterpretationContext.cs` を含めて 1,080 行） | 既定値テンプレートの展開（§5.6.2） |
| 効果 | `EffectSummary`（構文の第 3 列挙器）、Contract／Callable／virtual の 3 系統 | MIR 事実の集約と義務 1 種（§5.5） |
| 単相化 | `Binding.Specializations.cs`、`RequirementInstances`、`AnalyzeInstance`、Emission の計画 5 種 | インターン済み Instance と収集器 1 つ |
| 型の構造と格納 | `BoundType` の可変キャッシュ `StoredFields`（Binding と Analysis の両方が書く）、`StructStorage.cs:43,95` の二乗走査 | `AdtDef` と `(AdtDef, 置換)` のメモ |
| 署名と ABI | 署名照合 4 実装、ABI 規則 6 箇所以上 | `SignatureKey` と `Abi.Of(sig)` |

---

## 4. 設計原則

| 原則 | 内容 | 対応する Kimigayo 原則 |
| --- | --- | --- |
| 意味は一度だけ決め、一か所に置く | 選択・適応・評価順・脱糖の内容は Binding が決めて意味表と Plan に書き、MIR ビルダーが一度だけ下ろす。下流はそれを読むだけにする | 1、2 |
| 語彙を閉じる | MIR の文・右辺値・終端・射影の種類を固定する。新機能はその語彙へ下ろす。語彙の追加は設計変更として扱う | 1 |
| 下流は上流を信用する | 構造の検証は `MirValidator` の 1 か所で行い、全構成で実行する。違反は `InternalInvariant_Kd` として報告し、成果物を作らない。コード生成は検証済み MIR を前提とし、独自の再検査を持たない（IMPL §21.3.5） | 2、4 |
| 判定と説明を分ける | 判定コードは型付きの事実を記録するだけにする。文面は公開側のアダプターが作る | 3、4 |
| 一般規則で書けないものは作らない | 狭い部分実装を作らず、位置付きの Unsupported にする | 1、3 |
| アルゴリズムは Kimigayo で書く | intrinsic は Kimigayo で表現できないものに限る | AGENTS |
| 割り当てを増やさない | 表とアリーナは再利用し、世代番号で無効化する。要素ごとのヒープオブジェクトを作らない | AGENTS |

---

## 5. 目標アーキテクチャ

### 5.1 パイプライン

| 相 | 層 | 処理 | 出力 |
| --- | --- | --- | --- |
| P1 | Syntax | 字句・構文解析 | 構文木（Koto）。前順の密な `SyntaxId` を振り、属性を `AttributeKind` に分類し、符号付きリテラルを 1 ノードに正規化する |
| P2 | Binding | 収集 | `DeclTable`、スコープ木、lang item 索引、intrinsic 表、ディレクティブとテストで選択済みの宣言集合 |
| P3 | Binding | 宣言クエリ（メモ化） | `FnSig`／`SignatureKey`、`AdtDef`、`ParamEnv`、`ImplTable`（実装の構造検査と一貫性検査は宣言時に 1 回）、Origin のスキーマと変性、`UniversalRelations`、`VTableDef`、`EffectBoundTable`、特殊化表、既定値の宣言 |
| P4 | Binding | 本体の型検査 | `HirTables`、Plan、Problem（§7）、Callable 境界の義務 |
| P5 | Binding | HIR lint | ラベル、unsafe、値の破棄、`while true`、§17.4.4 の位置警告、ディレクティブ。§23.3.6.9 の修復候補（`Repair.RemoveUnsafe`、`Repair.PropagateFailure`、`Repair.ExplicitDiscard`）もここで作る |
| P5s | Binding | 起動検査 | 起動の選択と main の署名（SPEC §22.2.1、§22.2.2）。テストのビルドでは test の起動 |
| P6 | Mir | MIR 構築 | 定義ごとに 1 回。脱糖はここで行う |
| P7 | Mir | 本体解析 | 構造の検証 → 初期化と Move → drop elaboration → 実行時 lint → 能力検査（定義段） |
| P8 | Mir | 効果 | 局所事実 → スキーマ単位の SCC 不動点 → 義務の判定 |
| P9 | Mir | 借用検査 | 本体 × Semantics ケースごと |
| P10 | — | 受理 | SPEC §23.3.3 のとおり、P1〜P9 と起動検査（P5s）が完了し、Error が記録されていないこと。相が未完了で Error がなければ、その相の最初の未完了の対象に fallback の `PrerequisiteUnavailable_Kd` を出す |
| P11 | Backend | 単相化 | 収集器 1 つ、shim の生成、能力検査（インスタンス段） |
| P12 | Backend | コード生成 | `Layout`／`Abi`（メモ化した純関数）と、MIR から LLVM テキストを直接書く `FunctionCodegen`。物理 IR 層は置かない |
| P13 | Build（Compiler の外） | ツールチェーン | opt、llc、lld-link。外部宣言は IR テキストを再解析せず（`NativeToolchain.cs:425-439`）、リンクマニフェストで受け取る |

**依存の許可**

| 層 | 読んでよいもの | 読んではいけないもの |
| --- | --- | --- |
| Syntax | ソース | Binding（`FunctionKoto.cs:106` の逆呼び出しは削除する） |
| Binding | 構文木、自身の表 | Mir、Backend |
| Mir のビルダー（`Mir/Build`） | `HirTables`、Plan、宣言表、`TyCtxt` の純関数、構文木の子構造、`SourceRef` | Binding の内部メソッド、構文木の意味スロット、親ノードを遡る推定 |
| Mir の解析 | MIR、宣言表、`TyCtxt` の純関数 | 構文木、HIR |
| Backend | MIR、宣言表（`ImplTable`、`VTableDef`、`AdtDef`、lang item）、`TyCtxt` | HIR、借用検査の状態、構文木 |
| Hover・LSP | `SemanticQuery` | Binding の private な状態 |

- `src/Kimi/SolutionAndProject/Project.cs:491-500` の手書きの相の順序は、`Compilation` が保持する相の列に置き換える。
- **構文木と編集。** P1 の後、どの相も Koto を書き換えない。利用者とテストの編集（`KotoHelper.Replace`）は残す。編集は `SyntaxId` を振り直し、`HirTables`、Plan、MIR、単相化の結果をすべて捨てる操作とする。相の結果は次の Bind か編集まで不変で、プールした配列はその時点で初めて再利用する。Hover の snapshot と診断の文面は、再利用の前に作り終える。これで `Compilation.cs:318` の例外と `Koto.HasCurrentBinding` は不要になる。

### 5.2 構文層

- Koto は構文、Span、親、属性だけを持つ。意味スロットとサブクラスの計画スロットは、R2a で `HirTables[SyntaxId]` へ転送するプロパティにし、R6 で削除する。対象は `Koto.cs:488-527`、`InvocationKoto.cs:19-34`、`BinaryKoto.cs:20-36,238-251`、`ForKoto` と `FunctionKoto` の計画スロット。
- 子ノードの走査は 1 本（`ForEachChildSlot`）にする。現在は `GetChildNodes`／`VisitChildrenCore`／`ReplaceChildCore` の 113 個の override（38／39／36）、計 1,323 行が同じ子の列を 3 回書いている。
- 二項・単項演算子の sealed クラス 37 個は、演算子の種類をフィールドに持つ 2 クラスにする。
- 合成構文（`EvaluatedKoto`、`SyntheticKoto`、`FormattingKoto`、`RequirementCalleeKoto`、`TryKoto` の内部アーム）は削除し、脱糖は MIR ビルダーが行う。移行期に Binding が作る合成ノードには、R2a で転送プロパティを入れるのと同時に、前順の範囲の後ろに `SyntaxId` を追加で振る。
- **`extension` と `abstract`。** SPEC §2.5 と §6.1 が本版で退ける語であり、Unsupported（仕様が許す形の未実装）ではない。構文層の Language 診断（`UnavailableFeature_Kd` と構文エラー）のまま残し、除外構文でも検査する（SPEC §19.5）。削除するのは `ExtensionKoto` クラスだけとする。`extension` 宣言は既存の `Unexpected(SyntaxForm.ExtensionDeclaration)` で退け、回復では本体を構文だけ解析して（中の構文エラーは従来どおり報告する）、宣言を `OmittedDeclarations` に記録し、束縛しない（現在は `CreateStandalone` の既定の腕で `GroupKoto` になりうる）。`Parser.cs:1611-1679` の同一行の修飾子認識は `virtual`／`override` の認識も担うので残し、bool の受け渡しだけを整理する。
- **Documentation。** `src/Kimi/Documentation` へ移す。構文層は、doc コメントの範囲と宣言への対応づけ（宣言の前置き、除外構文、属性による抑止。SPEC §2.3.3、§19.5）を不変の表として渡す。文書の選択と可視性の判定（現在の `Binding.GetDocumentation`）は `SemanticQuery` を通して行う。

### 5.3 意味層（Binding の出力）

**HIR は木を作らない。** HIR は「構文木」＋「`SyntaxId` をキーにした `HirTables`」＋「Plan」とする。新しい木を作らないので、構文木と二重に持たない。

- `HirTables` の列は `Op`、`Type`、`Res`、`Adjust`、`PlaceShape`、`Completion`、`PlanIndex`。配列はプールし、世代番号で無効化する。warm rebind では割り当てない。
- `HirOp` は閉じた列挙（約 40 値）。
  - Place と値：Error、Literal、Local、Static、FunctionItem、Field、Deref、BaseProjection、PairFollow、Call、PrimitiveOp、Borrow、Convert、Assign、CompoundAssign、Aggregate、Closure、RuntimeTypeTest。
  - 制御：Block、Let、If、Match、Loop、While、For、Do、Return、Exit、Continue、Yield、Try、Require、Defer、Unsafe、Discard、NoInit、TestVerification、Interpolation、DictionaryLiteral。
  - パターン：Pat* 系。
- `Res` は Local／Param／`PreparedParam(i)`（既定値の中の先行引数）／Decl／Field／Variant／Error。
- `Adjust` は、§10.2 の適応 1 種と、bare 取得の決定（Copy／Transfer／Reborrow／Temporary／ByCase）からなる。現在の 4 系統（`ConversionBinding`、`ExpectedAdaptationKind`、`ArgumentOperationKind`、`ArgumentAdaptation`）を統合する。
- **Plan は 4 種**
  - `CallPlan`：{Callee, Subst, Args[](source, paramIndex, Adaptation, Acquire), EvalOrder, Defaults[](paramIndex, DefaultId), Result{Ty, ResultMode}, AvailableEffects}。直接・値・要件・プロパティ・基底・コンストラクター・暗黙コンストラクター・Function Item・反復・演算子の全形式をこれで表す。
  - `PatternPlan`：Subject の取得方法、腕、ガード、束縛、網羅性。
  - `ClosureDef`：捕捉と取得方法、環境の型、受け手、署名。
  - `ExpansionPlan`：Iteration／Try／Format／DictionaryLiteral／Range／Index／CompoundAssign。どれも CallPlan の番号と序数だけの小さなレコードで、展開は MIR ビルダーが機械的に行う。Format は「直接渡された有界の補間」の印を持つ（utf8-formatting §6.1／§6.2 のスタック経路。§6.4）。
- **`TyCtxt`**：型をインターンし、構築時にフラグ（HasParam、HasOrigin、HasPairLayer、IsClosed、ContainsError、整数の幅と符号）を計算する。`StoredFields` などの可変キャッシュは廃止する。`Subst` は Type・Length・Semantics・Origin の束縛を 1 レコードにまとめる。型の写像（MapType、MapOrigins、AnyOrigin）を 1 組、変性つき走査を `Relate(actual, expected, variance, sink)` の 1 本にする。
- **Origin は 5 種**（§5.5）

  | 種類 | 対象 |
  | --- | --- |
  | `Static` | static |
  | `Universal(binder, index)` | 署名の名前、型スロット、関数型の呼び出しごとの Origin、pair の外側の o（ケース条件つき） |
  | `Finite(SourceRef)` | 本体内の借用と可変 static |
  | `Var(VarId)` | 呼び出しごとの instantiation、局所の省略、var スロット、未解決のプレースホルダー |
  | `Meet(set)` | 複数の Origin の meet |

  現在の `Open`、`IsLocalInput`、負のスロット番号の規約、可変の `BorrowCondition` は廃止する。
- **宣言表**：`AdtDef`（フィールド、単一の基底、バリアント、デストラクターの有無、レイアウト属性）、`FnSig` と `SignatureKey`、`ImplDecl`（種類：Declared／Conditional／Inherited／Builtin、状態：Verified／Error／Unsupported）、`ParamEnv`（平坦化し、主語の型で索引）、`UniversalRelations`（`ParamEnv` × ケースごとの閉包。Binding の証明器と MIR の領域ソルバーが共有する）、`VTableDef`、`EffectBoundTable`、`DefaultDecl`。

### 5.4 MIR の語彙

| 区分 | 種類 |
| --- | --- |
| `MirBody` | キー `(DeclId, BodyKind)`。BodyKind は Fn／Closure(SyntaxId)／Getter／Setter／StaticInit／DefaultTemplate(i)／Test／Shim(kind)。ほかに Locals、Blocks、文・Place・射影の SoA（射影列はインターン）、Scopes、`PairBinders`（binder と許容ケースの集合）、フラグ `HasOpaque` |
| `LocalDecl` | `Ty`（ジェネリックのまま）、`Kind`（Return／Arg／Var／Temp／DropFlag）、`Mutable`、ソース名、`Role`（None／PreparedInput／PendingSlot／GuardCandidate／Subject。診断コードの対応づけにだけ使う）、局所注釈の Origin 関係、`SourceInfo` |
| Place | 根（Local または Static）＋射影列 |
| 射影（7） | `Deref`（ref・uniq・raw・obj・objref・objuniq・rc・arc の違いは型で決まる）、`Field(i)`、`Base(depth)`、`Index(Local)`（境界検査を含み、失敗すると Abort）、`ConstIndex(n)`（範囲内が証明済みの固定長配列）、`Downcast(variant)`、`PairLayer(binder)`（ケースによって恒等か Deref になる） |
| Operand（4） | `Copy(Place)`、`Move(Place)`、`PairAcquire(binder, Place)`（ケースによって Copy か Reborrow になる）、`Const`（整数・浮動小数・bool・Unit・文字列・FnItem・位置・型トークン） |
| Rvalue（9） | `Use`、`Ref(種類, Place, Bound)`（種類は Shared／Uniq／TwoPhaseUniq／Raw）、`BinaryOp(op, a, b, Proven)`、`UnaryOp(op, a, Proven)`、`Cast(kind, a, Ty)`、`Aggregate(Tuple／Adt／FixedArray／Closure, ops)`、`Discriminant(Place)`、`Len(Place)`、`Opaque(Ty)` |
| Statement（5） | `Assign(Place, Rvalue)`、`StorageLive`、`StorageDead`、`FakeRead(Place)`、`EndBorrow(Local)` |
| Terminator（6） | `Goto`、`SwitchInt`、`Call(Callee, args, dest, target?)`（target がなければ発散）、`Drop(Place, target)`、`Return`、`Unreachable` |
| 検査辺 | 終端の属性 `CheckTarget: BlockId?`。付けられるのは `Goto`、target のない `Call`、`Unreachable` だけで、それぞれ高々 1 本 |
| Callee（5） | `Fn(DeclId, Subst)`、`Requirement(RequirementId, Subst)`、`Virtual(SlotId, Subst)`、`Value(Operand)`、`Intrinsic(IntrinsicId, Subst)`。Abort は target のない `Intrinsic(abort)`（`$abort`）か `Intrinsic(fail)`（固定コード） |
| `SourceInfo` | `(SourceRef(ModuleId, SyntaxId), ScopeId, 文の開始の印)`。Scope は親と種類（Lexical／Unsafe／Guard／Deferred／Inlined(呼び出し位置)）を持つ。文の開始の印とソース名は、デバッグ情報（§17.3）にも使う |
| 借用検査の表（MIR の外） | `RegionVid`、`Loan{Place, 種類, RegionVid, 位置, Bound}`、`OutlivesConstraint{sup, sub, 位置, 分類, SourceInfo}`、`MoveData`（MovePath 木 1 つを初期化・drop・Loan の kill が共有する） |
| 記述子表 | 文・右辺値・終端・intrinsic の種類ごとに、読み・書き・Move・借用・定義・ケース依存・効果を 1 表にまとめる。全解析はこの表を通してオペランドと効果を列挙し、表にない種類は `MirValidator` が拒否する |

- **巻き戻しはない。** 検査に失敗したときの Abort は文の中に暗黙に置き、CFG の辺にしない（SPEC の Abort は巻き戻さない）。checked か wrapping かは型で決まる。`Proven` は IMPL §21.5.3 の検査省略を表し、ビルダーの 1 関数で判定する。
- **`Opaque(Ty)`** は次の 4 つを置き換える 1 概念である：Binding の Error ノード、MIR 構築で出会った Unsupported、宣言検査に失敗した既定値テンプレート、腕を選ばなかった `#switch`（`CompileTimeSwitchKoto`）。
  - 値を Opaque にするだけでなく、置き換えた構文が書く・読む・借用する全 Place（Binding が解決した `Res` から得る）に「状態不明」の印を付ける。印のある Place に依存する検査は、その Unsupported／Error を前提とする派生問題として抑制する（SPEC §23.3.6.4）。
  - 効果の要約には「不明」を入れ、空とはみなさない（SPEC §23 の「決まっていない事実を、ないとはみなさない」）。この不明は、原因の記録（その Unsupported／Error の検査キー）を持ち、ほかの「分類不能」と区別する。この不明に依存して決まる義務（witness、virtual、Callable、confined、静的呼び出し効果の衝突）は、その原因を前提とする派生問題として記録し、Language コードで報告しない（SPEC §23.3.6.1、§23.3.6.4）。
  - 本体の構築は中止しないので、同じ本体にある独立したエラーも報告される。
  - これで `OwnershipOperationKind.Unsupported`、`CleanupAction.Unsupported`、`HasUnsupportedIssue` による不変条件検査の抑制（`OwnershipModel.cs:525-552`）は不要になる。

### 5.5 MIR 上の解析

| # | 単位 | 解析 | 不動点 | 置き換える旧機構 |
| --- | --- | --- | --- | --- |
| A0 | 本体 | `MirValidator`（全構成。違反は `InternalInvariant_Kd`） | — | Emission の自己検証（約 450 行）、`CheckInputs` |
| A1 | 本体 × ケース | 初期化と Move（MaybeInit、MaybeUninit、EverInit）。P1 は実行時の辺だけ、P2 は実行時に到達しないブロックだけに書く（§5.6.3） | ブロック単位のビット集合ワークリスト（`OwnershipBody.Checking.cs:436-496` の `Converge` を一般化） | 5 レーンの専用ソルバー、ScopedChecking、LocalLoops、seed と replay |
| A2 | 定義 | drop elaboration（実行時の P1 だけを読む）。全ケースで初期化済みなら無条件、全ケースで未初期化なら削除、それ以外はフラグ。部分 Move はフィールド単位の drop に開く。`Drop(T)` はジェネリックのまま残す。初期化の状態がケースに依存する場合は、ケースごとのフラグ集合を持つ | — | フラグ 2 系統、needs-drop の述語 4 つ、`Binding.EffectBounds.cs:1739-1824` |
| A3 | 本体 | 実行時 lint：実行時到達性、require の失敗本体の終端、cleanup に阻まれた引き渡し。選択エラーの `#switch` を含む本体では完了の検査を派生扱いにする | 1 回の走査 | 制御フロー解析の Flow 合成、`CheckCompletion` |
| A3b | 本体 | 能力検査（定義段）：非ジェネリックの全本体（未使用の本体と、`CheckTarget` だけで到達するブロックを含む）と、型引数に依存しない演算を走査する（§5.7） | 1 回の走査 | i128 の除算などの多重ゲート |
| A4 | プログラム | 効果：局所事実（本体 × ケース）→ スキーマ単位のコールグラフ SCC（Tarjan）→ 最小の和集合不動点 → 義務（witness、virtual、Callable、OCC の使用、confined） | SCC のボトムアップ。格子が有限なので上限は不要 | `EffectSummary`、上限 1024、3 系統の違反記録 |
| A5 | 本体 × ケース | 借用検査：番号付け → 制約生成（Relate、置換した宣言節、WF、注釈）→ Origin の判定（Refuted は finite→fixed だけ、Unknown は fixed 同士）→ 生存性（全辺、drop-use を含む。抽象型の破棄は到達可能な全 Origin を観測するとみなす）→ Loan の到達 → アクセス検査（`places_conflict`）→ 予約（Call で活性化）→ 静的呼び出し効果（A4 の要約を使う）→ preserves results | 制約グラフの SCC と、局所化した Loan 伝播 | Binding の 5 ソルバー、来歴ウォーカー（約 1,150 行）、保留リスト 5 本、Place×Place の密な表 |
| A6 | — | 報告：主記録を決定的な規則で選ぶ。ケースごとの記録は `(code, 主位置, Reason)` で併合し、ケース集合を事実として持つ | — | 重複報告の抑制パッチ |

**効果の局所事実の源。** 記述子表の効果列（static の読み書きと初回初期化、不変 static から raw・rc・Weak を含む値の読み取り、整数から pointer への `Cast`）、intrinsic の効果列、`#LibraryImport` の呼び出し（環境効果。confined では違反）、本体のない未分類の呼び出し（違反）、Function Type の値の呼び出し（分類不能）、task 呼び出し（静的 Loan との比較では不明な環境効果。§8.4.10.4）、`Opaque`（原因つきの不明）。Semantics ケースによって pair 層の Drop と受け手の様態が変わるので、局所事実は本体 × ケースで集め、ケースごとの要約を持つ。呼び出し側は置換のケースベクトルで要約を選ぶ。

**順序の根拠**

- 効果の要約は借用検査の前に要る。SPEC §15.6.4 の静的呼び出し効果は、呼び出し側の Loan と callee の要約を比べるからである。
- 効果の結果は、選択・型付け・witness の選択に戻らない。SPEC §8.4.10.7、§8.7、§10.1、§23（「Effects never choose an overload」）が、Callable 境界を選択後の義務と定めている。要件の効果境界に違反する witness は、A4 が自身の結果表（適合の義務の結果）に無効として記録する。`ImplDecl` は書き換えず、受理と Backend はこの表も読む。再選択はしない（§8.4.10.6）。受理されるプログラムでは何も変わらず、変わるのは拒否されるプログラムの診断の出方だけである。
- 唯一の例外は継承適合での排他レシーバーの OCC（§8.4.4）で、これは §16-2 の判断まで汎用 Unsupported のままにする（§6.2）。
- 効果の単位は「宣言スキーマ＋パラメーター依存のアトム」とする。現在の呼び出しごとのインスタンス文脈は移植しない。インスタンス文脈のままだと停止させるのに上限が要り、有利なインスタンス化や処理順への依存も残るからである。この変更は受理を厳しくしうる（付録 B の X2）。

**ジェネリック本体はインスタンスで再解析しない。** 受理の判定は「定義 × Semantics ケース」で完結させる。SPEC §8.10、§8.9、IMPL §21.3.1 step 3 がこれを要求している。現在の `AnalyzeInstance` は、コード生成が具体型で作り直した所有権 IR を計画として消費するために存在する（コミット `869b8350` の記録）。インスタンス解析が見つける失敗は、能力不足（i128 の除算など）、記憶域の上限、Internal だけである。ただし次の 3 条件を満たす必要がある。

1. 抽象型（パラメーター、関連型の射影、pair の owner 実体）の破棄は、到達可能な全 Origin を観測するとみなす（SPEC §15.6.6）。現在の `CleanupObservesBorrows`（`OwnershipAnalysis.Cleanup.cs:9-60`）は抽象型に対して false を返し、その穴をインスタンス再解析が受け止めている（付録 B の X1）。
2. 前提集合は Semantics ケースごとに作り、和集合にしない（SETTLED）。
3. 能力の判定は A3b と単相化後の能力検査（§5.7）へ移す。

インスタンスごとに残る作業は、置換のインターンとケースベクトルの導出、callee の解決、layout と ABI、pair 射影の実体化、drop の確定、能力検査、表現と資源の義務の 7 つで、データフローは含まない。安全網は、インスタンス再解析ではなく concrete-twin 差分コーパス（`tests/diagnostics/pair-cases.json`、`GenericOwnershipCaseTest`、`PairFollowTest` の方式）を広げて置き換える。

**Origin の判定のうち Binding に残すもの**

SPEC §10.3、§10.4、§15.6.1 は Origin 関係を選択から外しているので、本体内の Origin の「判定」は MIR へ移せる。しかし次の 7 つは Binding に残す必要がある。

- OJ1：署名レベル一式（補完と省略、universal binder、変性と Loan 要件、宣言関係、継承）。
- OJ2：前提だけで判定する証明器（`UniversalRelations`）。
- OJ3：Origin が関わる Constraint（Owned、Callable、Type identity）。これらは適用性の判定に参加する。
- OJ4：契約全体の比較（関数参照の選択、Callable の互換、Contract の実装照合と特殊化）。
- OJ5：選択が済んだ呼び出しの principal 代入（§15.3.6 の表）。判定を伴わない代入で、結果を式の型と局所の型に書く。
- OJ6：未解決を表すプレースホルダー。
- OJ7：有限・固定・推論の原子の分類。

OJ5 を MIR へ移すと、`let r = first(p, q)` の `r` の型が `during static` から領域変数になり、Owned の判定、Type identity、Hover の表示が変わる。したがって移さない。Binding からは、局所領域の推論と判定（`Binding.LocalRegions.cs`、`CallRelations` の判定部、`OriginProof` の判定部、`ResultPremises` の 6 モード、候補 bound の巻き戻し）を削除する。適用性の中で Origin を判定している現在の箇所のうち、SPEC と食い違うもの（付録 B の T1〜T4）は §16-3 の判断に従って R7 で直す。

### 5.6 Kimigayo 固有の概念の扱い

#### 5.6.1 Semantics ケース

MIR は定義ごとに 1 回だけ構築し、ケースごとに複製しない。ケースによって意味が変わる箇所は、型に導かれる語彙（`PairLayer` 射影と `PairAcquire` オペランド）で表す。各ケースでは「ケースビュー」（binder → Semantics）を与えて、A1、A4 の局所事実、A5 を再実行する。文にケースマスクは付けない。マスクを付けると、既定値テンプレートを展開するときに binder 空間の写像が要るからである。

64 ケースの上限は維持する。「区別不要」によるケースの縮約は最適化として扱い、計画には入れない。縮約できるのは、操作だけでなく WF の前提と義務も同一だと示せる場合に限る。

#### 5.6.2 インライン既定値

SETTLED が守る意味（インライン複製、評価順、準備済みスロットの保護、後片付け）は変えない。変えるのは、置換を適用する時点と方法である。

- **テンプレート。** 宣言ごと・既定値ごとに、合成関数の本体を 1 つ作る。汎用引数は f の全汎用引数に、先行引数ごとの新しい Origin `q_i` を加えたもの。引数は `p_i: ref/P_i during q_i`（i < k）、結果は `P_k`。既定値の中の先行引数名は、名前解決で `PreparedParam(i)` に解決し、MIR では `*p_i` に下ろす。所有者は静的に選んだ宣言である（override や specialization ではない）。
- **宣言時の検査。** テンプレートを通常の汎用本体として、Semantics ケースごとに A1〜A5 に通す。§7.2.3 の禁止事項（Move、変更、新しい借用の保持）は、共有参照の通常規則と §15.6.3 から導かれる。根が `PreparedInput` の誤りは `DefaultArgument*` のコードへ写す（§16-12 で統合する場合、`Role` の `PreparedInput` は不要になる）。
- **呼び出し側。** 非再帰の省略は、テンプレート MIR のコピーを呼び出しの置換で 1 回 fold して展開する。Scope には `Inlined(呼び出し位置)` だけを持たせ、解釈文脈は記録しない。解釈文脈を Scope に残すと、各消費者が文脈つきの再解決をすることになり、G82 と同じ取りこぼしを生むからである。保留スロットの型は、呼び出しの置換で具体化した仮引数型とする。二相予約は Call で活性化する。
- **再帰。** 省略グラフの SCC を 1 回求め、自己ループや非自明な SCC に属する既定値は、同じテンプレートの評価関数への `Call` にする（現在の `RecursiveDefaultTest` の方針と同じ）。展開順に依存するスタック方式は使わない。
- **失敗したテンプレート。** 呼び出し側では `Opaque` に置き換え、記録を追加しない。
- **展開サイズ。** 非再帰でも展開は指数的に増えうる。本体サイズの資源上限を 1 つ設ける（§6.2、§16-10）。
- **SETTLED との関係。** 「Instance-evaluated generic defaults」の理由の前半（インライン複製が評価順・準備済みスロットの保護・後片付けを保つ）は、本設計でも成り立つ（複製を保つ）。変えるのは後半の機構（各複製の操作と Place に解釈文脈を記録する）だけである。しかし、置換を展開時に 1 回だけ適用して文脈を持たない点は、同項目が退けた案（置換済みの計画で既定値段の解釈を除く）に当たる。同項目の再検討条件（記録から文脈を得られない消費者、複数の証明環境を要する文脈、文脈の選択でなく再解析を要する欠陥、固定条件の計測の後退）が現行の機構で成り立つ証拠はなく、新設計で消費者が文脈を参照しないことは再検討の根拠にならない。したがってこの部分は SETTLED の再提起として利用者の承認を求める（§16-15）。承認された場合だけ、R6 の単位 a で同項目の Proposed fix と理由を改める。承認されない場合は、展開した複製の Scope に解釈文脈を記録する現行の方式を MIR に移し、消費者の規則をそれに合わせる（削減量は約 300 行減る）。

これで `InterpretationContext`、`OwnershipBody.Defaults`、`OwnershipAnalysis.DefaultDeclarations`、`DefaultGenerationPlan`、`LlvmEmitter.Defaults`、`BodyLowering.Defaults`、`DefaultParameters.cs` が不要になる。既定値専用の 1,080 行と散在する約 250〜300 行を削除し、約 350〜450 行を追加する。

#### 5.6.3 検査専用の継続（SPEC §14.10.3）

到達しないコードの検査は、単一 CFG と終端の属性 `CheckTarget` で表す。

- **構築。** 転送の分岐点は「オペランドの評価 → 取得 → 放棄される比較・予約・ガードの `EndBorrow` → 分岐」の順に置く。分岐点の `Goto` の実行時の後続は「結果の確保 → cleanup → 転送先」、`CheckTarget` は字句上の後続ブロックである。Never を返す呼び出しは `Call(target なし, CheckTarget=後続)` とし、宛先は初期化しない。構造的完了のない `loop` は、頭のブロックから検査出口を出す。継続の終点（腕の末尾など）は HIR の完了要約に 1 回だけ持たせ、MIR はそれを `CheckTarget` に写す。
- **解析は 2 パス。** P1 は実行時の辺だけを辿る。P2 は全辺を辿るが、状態を書き込むのは実行時に到達しないブロックだけにする。Loan の生存性は全辺で計算する。drop elaboration、配置、コード生成は P1 と実行時の後続だけを読む。
- **rustc の FalseEdge のように単一パスで全辺に状態を流してはならない。** 継続の状態が到達経路に合流し、§14.10.3 ¶4 に反する。
- **match は源泉順の判定連鎖として構築する。** 決定木への変換と定数条件の畳み込みは解析の前に行わない（§14.9.2 は経路をパターンの包含で刈らない）。決定木は必要ならコード生成側の最適化とする。

現在の seed、region、replay、target の追跡、MixedTargets、Coalesce、構文の allowlist による 3 種の証明、`LocalLoopProof`、Unsupported のフォールバックは不要になる。削除は約 1,600〜1,800 行、追加は約 100〜150 行。ただし旧実装は SPEC に書かれていない規則（保留した終端履歴の外側での合流、転送先ごとの replay など）に依存しているので、§14.10.3 の改訂が要り（§11）、受理の差分 C1〜C6（付録 B）が出る見込みである。

#### 5.6.4 defer と cleanup

defer の本体は、出口の行き先ごとの drop 木として 1 回だけ構築し、`(scope, 登録数, 行き先)` で接尾辞を共有する（rustc の drop tree と同じ）。出口ごとの経路を合流させないので、§14.9.2 の経路と正確に対応する。出口を選択子で 1 本に合流させる方式は、経路を混ぜて偽陽性を生むので採らない。現在の出口ごとのインライン展開と、その展開上限（`DeferredOperationLimit`）は削除する。

#### 5.6.5 オブジェクト、ビュー、共有ハンドル

`obj`／`objref`／`objuniq`／`rc`／`arc` の違いは型で決まり、MIR では `Deref` 射影と `Call`（生成、clone、実行時型テスト）で表す。ハンドルの drop は DropGlue に含める。virtual 呼び出しは `Callee.Virtual(slot)` とし、スロット番号は Binding の `VTableDef` が決める。現在の `VirtualGenerationPlan.cs:41-54` は、スロット順をソースパスと位置の並べ替えで再計算しているが、これは不要になる。

#### 5.6.6 静的記憶域とプログラムの入口

- static を根とする Place を読む・借用する・書く前に、MIR ビルダーが `Call(Intrinsic(staticEnsure), [slot])` を置く。これが SPEC §22.2.3 の状態検査、初回の初期化、`KIMI_E_STATIC_CYCLE`／`KIMI_E_STATIC_SHUTDOWN` の Abort（`SourceInfo` は修飾の先頭）を担う。記述子表はこの Call を初期化本体（`MirBody(StaticInit)`）への辺として A4 に渡す。
- プログラムの入口（`__kimi_start`）は `Shim(Entry)`（実行時の初期化 → 本体 → shutdown → 終了）とし、shutdown は初期化の逆順の登録表を実行時に持つ（`var` を先、`let` を後）。テストのビルドでは、まず製品の根（有効な main の閉包と、Library の公開生成根）で単相化を固定し、そのうえで `#Test` 関数と `Shim(TestEntry)` を根に加える。出力するのはテストの実行根からの閉包だけとする（IMPL §21.3.7。製品側の検証は省かない）。
- 現在の static の実装は、閉じた非ジェネリックの不変スカラーに限る狭い実装（`StaticScalar.IsDynamic`、PLAN の P36 は IN_PROGRESS）である。新経路は上の一般の語彙で下ろし、一般規則で追加の実装なしに扱える形はそのまま扱う。それ以外（ジェネリックな囲みの鍵 §22.2.4、可変・借用・集成体の static の shutdown など）は P36 の再開まで Unsupported とする（D2、R0 の一覧に記録）。

#### 5.6.7 テストの検証文

`TestVerification` は、`Call(Intrinsic(testObserve), [cond, Const(siteId)])` と、失敗時の `testMessage`／`testRequireAbort` の呼び出しに下ろす。`siteId` は Koto の可変スロットではなく `TestCatalog` の表から引く（現在は `src/Kimi/Testing/TestCatalog.cs:82` が `TestVerificationKoto` に書き込む）。相（本体、cleanup、初期化、shutdown）は実行時の状態とする（SPEC §22.6.1、testing-profile）。テストのビルドでは、A2 が確定した `Drop` と defer の drop 木、`staticEnsure` による初期化、`Shim(TestEntry)` の shutdown を、相の退避・設定・復元（`testPhaseEnter`／`testPhaseLeave`）で囲む。現在の `TestPhaseEnter`／`Leave`（`BodyLowering.Graph.cs:298-314`、`LlvmModuleWriter.cs:499-508`）と同じ意味である。一時値の cleanup の後の require-Abort の順序は、ビルダーが呼び出しの位置で保つ。

### 5.7 バックエンド

- **単相化。** `Instance = intern(MirDefKey, Origin を消去した置換)`。ケースベクトルは置換の Semantics 引数から導く（型の形から推定しない）。MonoItem は Fn（任意の `MirBody` のインスタンス）、DropGlue(Ty)、VTable(Ty)、ObjectDescriptor(Ty)、Shim(kind)、StaticInit。再帰既定値の評価関数は Fn(DefaultTemplate) として扱う。ワークリストは 1 本、上限（深さ、集合の大きさ、成長キー）の判定は 1 か所で、要求元の `SourceInfo` を Resource 診断に付ける。
- **shim。** DropGlue（ユーザー drop を派生から基底へ、フィールドを逆順に）、クロージャの消去、Function Item のアダプター、vtable の受け手適合、Tuple と参照の比較、Property witness、Entry、TestEntry。
- **能力検査。** 演算子表・intrinsic 表・Layout の能力表を 1 つにし、2 段で引く。
  - 定義段（A3b）：非ジェネリックの全本体（未使用の本体と `CheckTarget` だけで到達するブロックを含む）と、型引数に依存しない演算を、受理（P10）の前に走査する（IMPL §21.3.5、§21.4.1、§21.5.3、SPEC §23.3.3）。check と build は同じ記録を出す。
  - インスタンス段：単相化の後に、各インスタンスの全ブロック（`CheckTarget` だけで到達するブロックを含む）を走査し、型引数で初めて決まる組み合わせ（ジェネリック本体の i128 の除算など。SPEC §8.4.7.3、IMPL §21.4.1、§21.5.3）だけを報告する。これは現在と同じく生成時の結果である。
  - どちらも `Unsupported(SourceRef)` を位置ごとに 1 回だけ報告する。i128 の除算・剰余の判定 6 箇所は、この 1 関数になる。
- **Layout と ABI。** `Layout.Of(消去済みの型)` をメモ化した純関数にする。管理 Type のレイアウトは、R5 では現在の規則を移植して使い、R8 で内部の表現 struct からの導出に替える（`AggregateLayout.cs:293` のフィールド数の直書きを廃止）。破棄の方法は DropGlue に分ける。ABI は `Abi.Of(FnSig のインスタンス)` 1 本にし、宣言の種類で分ける：`#LibraryImport` は SPEC §22.3.2 の C ABI、それ以外は内部 ABI。§22.3.2 の型の部分集合の検査は、宣言時に Binding で 1 回行う。
- **コード生成。** `FunctionCodegen` は検証済み MIR から LLVM テキストを直接書く。ローカルは原則 alloca にして mem2reg に任せ、phi と支配木の検証は持たない。`CheckTarget` だけで到達するブロックは出力しない（能力検査は、定義段とインスタンス段でそれより前に済んでいる）。定数プールと位置表は既存のものを整理して残す。デバッグ情報（§17.3）は `SourceInfo` とローカルのソース名から出す。
- **物理 IR 層は置かない。** 現在の `EmissionModule` と `LlvmModuleWriter` の分離は、物理 IR が機能別のオペコードを持つために必要だった。MIR の語彙が均一になれば、1 段で書ける。`GenerationFailed_Kd` は削除し、生成時の不変条件の違反は `InternalInvariant_Kd` にする。

### 5.8 ライブラリ優先と intrinsic

- **表現は管理 Type のまま、レイアウトだけを Kimigayo で定義する。** Array／Dictionary／Slice／UniqSlice／string は、型意味論の上ではコンパイラー管理のままにする。記憶レイアウトだけを、lang item として指定した `internal` の表現 struct（`#Layout("C")`）と同一と定義する。

  ```kimi
  #Layout("C") struct ArrayRepr<T> { data: raw/T, length: isize, capacity: isize }
  #Layout("C") struct SliceRepr<T> { data: raw/T, length: isize }
  #Layout("C") struct StringRepr { data: raw/u8, length: isize, releaseKind: u8 }
  ```

  Dictionary の slot は generic な `Slot<K, V> { links: Links, key: K, value: V }` とする。Slot は `#Layout("C")` を付けない Kimigayo レイアウトの struct とし、オフセットは `Layout.Of` が導出する（C レイアウトは大きさ 0 の直接のフィールドを拒否するので、`Dictionary<K, ()>` などが作れなくなる。IMPL §21.1.3）。ライブラリは SPEC §5.4 の通常の raw 変換で表現に到達する。
- **通常の struct にする案は採らない。** 理由は 2 つある。不変 static から raw を含む値を読むと環境効果になること（§8.4.10.2。§15.3.5 の `raw/()` と `Loan` の構成で変性を保っても、raw のフィールドは残る）。§22.1.2.5 が「Array と Dictionary の表現はコンパイラー管理のまま」と定めていること。SETTLED に不採用として追記する（§11）。
- **intrinsic（約 28。現在の `CompilerFunctionKind` 75 種を置き換える）**：

  | 群 | intrinsic | 根拠の節 |
  | --- | --- | --- |
  | メモリ（6） | `Raw.allocate`、`Raw.release`、`Raw.initialize`、`Raw.slice`、`Storage.tryAllocateBytes`、`Storage.moveBytes`（重なりを許す） | SPEC §5.6、§22.1.2.5 |
  | 型の事実（2） | `stride<T>`、`destroyValues<T>`（cleanup-free な T では何も生成せず、§4.7.7 の計算量を最適化レベルによらず満たす） | §22.1.2.5、IMPL §21.2.2 |
  | Loan の再解釈（1 種 2 宣言） | `shareLoan`、`lendLoan` | §22.1.2.5 |
  | 失敗（2） | `abort(message)`（`$abort`）、`fail(code)`（固定の §22.5.4 コード） | §17.3、§22.1.2.5、§22.5.4 |
  | 出力（1） | `writeStdout` | §22.4、§22.5 |
  | 書式化（3＋暫定 1） | `formatFloat`（Ryu）、`Text.writer` の消去エントリ、消去済み reserve。i128 の書式化が Kimigayo で書けるまで `formatInteger128` を暫定で残す | utf8-formatting §1 |
  | ハンドル（5） | `makeObj`、`makeRc`、`makeArc`、`clone`、`typeTest`（実行時型テスト。Weak の操作は P35 で加える） | IMPL §21.2.3、SPEC §13.6 |
  | テスト（7） | `testObserve`、`testMessage`、`testRequireAbort`、値の記録、`testPhaseEnter(kind)`、`testPhaseLeave`、`Test.tempDirectory` | §22.6.1、testing-profile |
  | static（1） | `staticEnsure` | §22.2.3 |

  - `replace`／`exchange`／`swap` は raw の take と initialize を使う Kimigayo の本体にする。§15.7 の完全性と非重複の検査は、lang の同一性で借用検査が認識する。
  - 文字列の比較（`compareBytes` 相当）は Kimigayo で書く。memcmp 相当の性能が要ると計測で示せた場合だけ intrinsic にする。
  - Abort の中で checked 算術が再帰しうることと、Alloc の起動の問題があるので、OS シムは IR のまま残す。
- **コード生成に残すもの**：rc／arc／obj／Weak の実行時処理（IMPL §21.2.3 がヘッダー、記述子、CAS の順序を固定し、生成 IR の検査を要求している）、static の状態機械と shutdown 表、OS シム 6 演算（`WindowsRuntime.ll.in`）、Ryu（`Utf8FloatRyu.ll.in` 776 行）と浮動小数の書式化の入口（`Utf8FloatRuntime.ll.in` の `__kimi_format_float`）。
- **識別。** 同梱ライブラリでだけ認識する実装属性 `#Lang("…")`、`#Intrinsic("…")`、`#CallerLocation`、`#Unsupported` で宣言を識別する。利用者のソースでは未知の属性として扱う。規則ではなく識別であることを §11 の SPEC 改訂に書く（§16-9）。`KimiDeclarationId`（187）は lang item 約 40〜60 に縮める。署名専用のパースモードと、カタログの Overload／Owner 文字列は廃止する。形状の検証は xUnit の 1 クラスへ移し、製品コードには「lang item が過不足なく 1 回ずつ解決される」ことの確認だけを残す。
- **移す順序**：Array の 8 操作 → Dictionary（Slot）→ 文字列の比較・破棄・書式化と組み込み witness → Text／Buffer／Window／Writer → `ResolvedRange` 専用の for の廃止。
- **計測のゲート**：移す前に、現行 IR 版で O0／O2 の実行時間・割り当て・コンパイル時間を記録する（`src/Kimi/Library/README.md` の Text 方針が要求している）。O2 で後退した操作は IR の組み込みのまま残す（§18 P1、P5）。各段で O2 の A/B 比較、ゼロアロケーション回帰、`DynamicArrayCostTest` と utf8-formatting §6.2 の確保回数（直接渡された有界の補間は 33 バイトのスタック領域でヒープ確保なし）を確認する。

### 5.9 診断と Hover の投影

- 診断の文面は公開側のアダプター（`src/Kimi/Diagnostics`）だけが作る。判定コードは Problem（§7）を記録するだけにする。
- Hover は `SemanticQuery` 経由で `src/Kimi/Checking/Hover` へ移す。現在は Binding の private な `nodes` と `symbols` を全走査している（`Binding.Hover.cs:130`、`Binding.HoverVariables.cs:17`）。型の表示（`TypePrinter`）は診断と Hover で共有する。

---

## 6. 未対応報告の一本化

### 6.1 現状

| 層 | 箇所 | 仕組み |
| --- | --- | --- |
| Binding | `BindingFailure.Unsupported` の Fail 約 67 箇所（33 ファイル） | `UnsupportedBinding_Kd`（`reason:Text` の自由文）。switch の既定腕が `UnsupportedBinding_Kd`（`Binding.cs:1215`）。専用の説明表 4 つ（originQualifierLimits、staticStorageLimits、pendingExclusiveLimits、payloadCallees）。OCC-X の待機機構（9 ファイル・約 180 行）。`MissingFailure` による再分類（`Binding.Diagnostics.cs:2273`）。`MarkUnsupportedTree` |
| Analysis | 約 69 | `UnsupportedOwnership_Kd`（feature か case を evidence に持つ）、`UnsupportedIntegerOperation_Kd`、`OwnershipFeature` と利用者向け Note、CFG に入れるプレースホルダー（`OwnershipOperationKind.Unsupported`、`CleanupAction.Unsupported`）、不変条件検査の抑制、`Supports*` ゲート |
| Emission | 文字列つき `Fail` 448、`CheckInputs` 9 | 位置のない `GenerationFailed_Kd`（Internal。`LlvmEmitter.cs:77` はプロジェクトファイルの位置で報告する）。`UnsupportedEmission_Kd` は定義だけで未使用。本物の機能ギャップは約 12 種・明示的な未対応の文言は約 49 で、残りは不変条件の再検査 |
| 横断 | — | `Supports*` の名前を持つゲートが 15（Analysis 8、Binding 1、ルート 5、Emission 1）。同じ制限を多重に判定している。i128 の除算は 6 箇所（`Binding.Expressions.cs:1622`、`OwnershipAnalysis.cs:1620`、`OwnershipAnalysis.Values.cs:135`、`BodyLowering.BuiltinArithmetic.cs:26`、`BodyLowering.Graph.cs:632`、`NumericArithmetic.cs:12`）、match のガードは 3 相 |
| 誤分類 | — | DIAGNOSTICS §11 には、仕様が許す形の実装制限を Language コードで報告している例がある（フロー精製、予約中の静的分離パス、フィールド経路の予約中の共有読み取り。`docs/dev/DIAGNOSTICS.md:251-254`）。Unsupported の名前を持つが実際は Language 区分のコードも 2 つある（`UnsupportedEscape_Kd`、`UnsupportedImportSignature_Kd`） |

### 6.2 規則

1. **コードは 1 つ。** `Unsupported_Kd`（Error、区分 Unsupported）。固定のメッセージと主位置だけを持ち、Reason、Note、Advice、関連位置、修復候補、構文の種類を持たない（D6）。API は `Unsupported(SourceRef)` の 1 本で、同じ位置の未対応は 1 記録に併合する（SPEC §23.3.6.4 の例外として §11 で明記する）。
2. **報告できる場所は次に限る。**
   - Binding：意味規則が未実装の形。例：`objuniq` の virtual、OCC-X、generic static field、`#Unsupported` 付きのライブラリ宣言の使用。OCC 状態が未計算のときは、継承適合の witness は実装（適合宣言）の位置で 1 回報告し、`ImplDecl` の状態を Unsupported にする（使用は派生として抑制する）。適合を経ない使用（投影呼び出し、排他オブジェクトのアクセサー、直接呼ばれた継承 setter）は使用の位置で 1 回報告する。
   - MIR 構築：その構文を `Opaque` に置き換えて構築を続け、`HasOpaque` を立てる。新しいコードでの目標は 0 件。
   - 能力検査の定義段（A3b）とインスタンス段（§5.7）。
3. **報告しない場所。** コード生成は Unsupported を出さない。自己検査の不一致（`Binding.Diagnostics.cs:1534`、`Binding.ImplicitConstructors.cs:304-315`）は Internal にする。
4. **依存する部分は記録しない。** `Opaque` と Error 型の伝播で派生扱いにする。
5. **Language コードで報告しない。** 仕様が許す形を実装できないときに、保守的な衝突など Language 区分の記録を出してはならない（SPEC §23.3.6.1）。例えば SPEC §15.6.2 が非重複と定める形（別々の Tuple 要素など）に Loan の衝突を出さない。
6. **資源の上限は、判定を 1 か所に集めた Resource 診断として残す。** 残すもの：単相化の上限、ケース数の上限、借用検査の作業領域の上限（`OwnershipStorageLimit_Kd` を MIR の Loan × 位置の集合の上限に置き換える。IMPL §21.3.5 を改める）、`InterpolationNestingLimit_Kd`（IMPL 付録 A.6）、本計画で新設する既定値展開の本体サイズの上限（IMPL §21.3.5 に追記する）。削除するもの：`DeferredExpansionLimit_Kd`（defer の展開上限 `DeferredOperationLimit`。drop 木では不要）と `ForBindingLimit_Kd`（for の束縛の可変性を束縛ごとのフラグで持てば不要）。どちらも SPEC・IMPL に記述はない。
7. **狭い部分実装を作らない。** 一般規則で実装できない形は Unsupported にする。`Supports*` のようなゲートや、形のホワイトリストは追加しない。

### 6.3 削除・変更するもの

- **削除するコード**：`UnsupportedBinding_Kd`、`UnsupportedOwnership_Kd`、`UnsupportedIntegerOperation_Kd`、`UnsupportedEmission_Kd`、`GenerationFailed_Kd`（`InternalInvariant_Kd` へ）、`DeferredExpansionLimit_Kd`、`ForBindingLimit_Kd`（§6.2 規則 6。STATUS の記述も同じ単位で直す）。
- **残すコード**：`UnavailableFeature_Kd`（Language。§5.2）、`LegacyBorrowOrigin_Kd`（SPEC §15.3.1 の拒否を報告する構文の診断で、IMPL 付録 A:227 が説明を要求する。Advice だけを削る）、`BorrowOriginKeyword_Kd` と `Repair.ReplaceToken`（§3.3.6、§23.3.6.9 の構文の診断と修復候補）。
- **改名**：`UnsupportedEscape_Kd` → `InvalidEscape_Kd`、`UnsupportedImportSignature_Kd` → `InvalidImportSignature_Kd`。
- **仕組み**：
  - 説明表 4 つ
  - PendingExclusive 一式
  - `OwnershipFeature` と `FeatureNote`／`FeatureLabel`
  - `OmittedBaseOutcome` の区別
  - `MarkUnsupportedTree`
  - `MissingFailure` の再分類
  - `Supports*` ゲート（能力表と一般規則に置き換える）
  - Unknown を Unsupported に読み替える処理
  - 実装上の限界を `EffectViolation` に流用する処理
  - `OwnershipResult.UnsupportedCount`（消費者はテストだけ）
  - Emission の `Fail` 文字列と `out string? failure` の連鎖（R6 で旧 Emission とともに削除）
- **狭い部分実装の代表**（D2。R0 の一覧で依存を調べてから処分する）：
  - 修飾子の Origin スロット推論（ローカル初期化子の中、非ジェネリックに限る。`Binding.ContainerReferences.cs:334-417`）と、その Advice 生成
  - virtual は objref の受け手だけ（`Binding.VirtualDeclarations.cs:110-125`）
  - match のガードは Subject が scalar／Unit／string のときだけ（`MatchTypes.cs:84-106`）。新経路ではガードを Subject の型によらず一般に扱う
  - payload callee は 3 つの形だけ（`Binding.ValueCalls.cs:389-405`）
  - クロージャ結果の Origin は 4 つの形だけ（`Binding.FunctionTypes.cs:47-174`）
  - 要素借用の Loan は String 要素だけ（`OwnershipBody.Comparisons.cs:376-404`）
  - 到達しないコードの検査の構文 allowlist 3 種（`OwnershipAnalysis.ScopedChecking.cs:469-593`、`OwnershipAnalysis.LocalLoops.cs:13-143`）
  - static は閉じた非ジェネリックの不変スカラーだけ（`StaticScalar.IsDynamic`。§5.6.6）
  - Sealed の直接解放の最適化（`AggregateLayout.cs:99-143`）

  削除するものには、一般規則で自然に得られる精度（Tuple の分解の独立性、Slice 経路など）が多い。MIR の `places_conflict` と領域推論で回復できるものは回復し、回復できないものは Unsupported にする（規則 5）。§6.1 の誤分類の例も R0 の一覧に入れ、Unsupported か一般化に振り分ける。

### 6.4 影響

- 未対応専用の仕組みを直接削除すると、内訳の合計は約 3,600 行（Emission 約 1,200、Binding 約 1,100、Analysis 約 450、ゲートの集約 約 350、フロントエンド 約 250、カタログと対応 switch 約 270）になる。内訳には重複があり、Emission の約 1,200 は R6 で旧 Emission とともに消える分なので、未対応の一本化だけの効果は約 2,000〜2,400 行と見積もる。部分実装の削除でさらに約 1,500〜2,500 行。ガード自体は `Unsupported(x)` の 1 行として残る。
- テストの書き換え：`UnsupportedBinding_Kd` は 27 ファイル・55 箇所、`UnsupportedOwnership_Kd` は 7 ファイル、feature の事実は 2 ファイル・11 箇所、インスタンス失敗の Note（`GenericGenerationDiagnosticTest`）。STATUS の Unsupported コード名の記述（`UnsupportedBinding_Kd` 19 箇所、`UnsupportedOwnership_Kd` 11 箇所）も同じ単位で直す。
- 仕様上の規則なので消してはならないもの：`_` 穴の推論、混合 Range のリテラル規則、IMPL §21.5.3 の検査省略、§17.4.4 の位置警告、§12.3.4 の辞書キー重複、§22.3.2 の ABI 部分集合、utf8-formatting §6.1／§6.2 の writeLine に直接渡された有界の補間のスタック領域（必須コスト。`BodyLowering.Interpolation.cs:122-124`）。これらは統一した推論器、MIR 構築、`ExpansionPlan(Format)` へ移すだけにする。

---

## 7. 診断の縮小と分離（D3）

- **失敗の地点で Problem を直接記録する。** Problem は (主語 `SourceRef`, 要件, 条件番号, 文脈, code, 型付きの事実, 関連位置, 修復の種) で、式の型を Error にする（rustc の ErrorGuaranteed に相当）。Error を読んだ検査は報告せず、派生の記録が必要ならその原因の記録番号を引くだけにする。
- **削除するもの**：`BindingFailure`（89 値）、コード対応の switch、`ReportIssue`（約 450 行）、`FailExplained` と説明表、consultation frame、`IsDerived` の特例。`OwnershipModel` 側の `OwnershipFailure` → コードの switch と、21 フィールドの `OwnershipIssue` も、種類ごとの型付き事実に置き換える。
- **Advice と Note の削減**（D3）：`Binding.Diagnostics.cs` の Advice・Note 生成と、説明のための再証明（`GenericDefaultNote`、`SomeBindingFits`、`SimilarNames`）、`Binding.ContainerReferences.cs` の Advice、`Binding.OriginDiagnostics.cs` の移行ヒント、制御フロー解析の警告の Advice（修復候補で表せない文面）を削除する。
- **残すもの**：SPEC が要求する Reason の事実、関連位置、§23.3.6.9 の修復候補（制御フロー解析の `Repair.RemoveUnsafe`、`Repair.PropagateFailure`、`Repair.ExplicitDiscard` は HIR lint へ移して残す）、構文の診断。
- **要件とコードの対応。** 要件（検査の識別）はコードから独立に持つ（SPEC §23.3.6.4、DIAGNOSTICS §4.1）。`BindingFailure`／`OwnershipFailure` の代わりに、判定点が記録する閉じた要件の列挙を `DiagnosticRequirement.tinyhand` に置く。安定名は現行の `Binding.*`、`Ownership.*`、`Syntax.<Form>` を引き継ぐ。派生の記録は、未決の検査の要件をこの列挙で直接名指しし、コードから逆引きしない。カタログには、各コードが取りうる要件の集合を整合検査のためにだけ書く。複数の相の要件にまたがる既存のコード（`IncompatibleContractImplementation_Kd`、`UnsatisfiedEffectBound_Kd`、`UnprovenConstraint_Kd`、構文コード）の扱いは R0 の監査表で決める。これで並行する switch 2 本を削除する。
- **診断の主語と順序。** `DiagnosticKey` の主語は `SourceRef`（インライン展開の呼び出し位置を文脈に持つ）とし、Koto の参照の同一性（`DiagnosticFact.cs:63-90,112-120`）を使わない。§23.3.6.6 の主語の順序は、同じ主位置の中で深さ（浅いものが先）、次に `SyntaxId` の前順で比べる。移行期の合成ノード（R2a〜R6）は、元の構文の `SyntaxId`、変換の役割、変換内の序数（オペランドの源泉順）で比べ、付与順を使わない（「Order never depends on arrival」）。
- **相と区分の対応。** P1 → Syntax、P2〜P5 → Binding、起動検査（P5s）→ Startup、P6 と P7（A0〜A3b）→ ControlFlow、P8〜P9 → Ownership、P11〜P12 → Emission とする。A1・A2 の初期化・Move・Transfer の要件は現在 Ownership 区分にある（`OwnershipModel.cs:141-158`）。これを ControlFlow へ移すと要件の安定名と §23.3.6.6 の順序が変わるので、移すかどうかは区分の改名とともに §16-7 で判断し、R6 の差分台帳では (d) とする。
- **SPEC の監査は R0 で行う。** SPEC 本文、utf8-formatting、arithmetic-contracts、associated-type-inference、IMPL 付録 A にある Advice／Note／提案の記述（約 55 箇所）と SETTLED を 1 件ずつ表にし、「Reason の事実として残す」「関連位置として残す」「修復候補として残す」「Advice として残す」「削除」に振り分ける。例：§23.3.6.5 の Origin 表示は Reason に残す。§13.3 の補間リテラルの提示（SETTLED「A string concatenation operator」の根拠）は意図の提案なので、Advice として残すか、§23.3.6.9 に修復候補の種類を追加する仕様改訂として扱う。改訂そのものは R1 で行う。

---

## 8. Binding の再構成

1. **ドライバー**：収集 → 宣言クエリの強制 → 本体の型検査 → 確定、の 4 段にする。削除するもの：`IndexVisitor`（`Binding.cs:1566-1592`）、`ResetPass` の約 40 個の Clear、手書きの 35 段と 25 段の順序（`Binding.cs:458-571`）、検証の 2 回実行、全体の再検証ループ（`Binding.ConstraintValidation.cs:205-216`）。
2. **名前解決**：`ResolvePath` 1 本に、§9.4 の段の列を持たせる。Self とジェネリック引数はスコープのシンボルとして宣言し、文字列 "Self" の判定を廃止する。基底は 1 回だけ解決する（基底走査の 6 実装を廃止）。アクセス判定は可視領域の包含比較 1 つにする（`Binding.ContractMatching.cs:32-140` の並行実装を統合）。修飾の修復候補は公開アダプター側で作る。属性はパーサーで `AttributeKind` に分類し、文字列照合の多重実装（`Binding.cs:1463-1487,1592-1680`、`Binding.Attributes.cs:10-21`、`Parser.cs:2986-2997`）をなくす。
3. **`InferCtxt`（約 850 行）**：変数は TyVar、LenVar、IntLitVar、FloatLitVar、OriginVar。OriginVar は証拠を集めるだけで、不一致にはしない（§10.8）。実装は union-find と undo ログで、Snapshot／Rollback によって候補評価を副作用のない関数にする。統合するもの：推論器 5 本、`ArrayInference` の `_` 穴、`ArrayArguments` の 4 つの走査、`ResultEvidence`、`Binding.Diagnostics.cs` の独自単一化器。仕様の規則は制約の投入として残す（`_` 穴、混合 Range、§10.8.1 の固定束縛の検査、§7.3.1 の呼び出し時の相関）。
4. **Origin**（§5.5 の OJ1〜OJ7）：適用性と順位付けは構造部分だけの `FitsStructuralPart` 1 本で行う。契約全体の比較の Origin 部分は前提だけで証明する。選択が済んだ呼び出しにだけ principal 代入を行い、型に書く。削除：候補 bound の Begin／Keep／End、`originStateVersion` とその例外、本体内の判定、Emission からの Origin の再 fit。縮小して残す：`OriginInference`（select 解だけ）、`OpenOrigins`（プレースホルダーと呼び出しごとの Origin）、初期化子の principal 推論（純関数にする）。
5. **`ContractSolver`**：`Prove(Goal, ParamEnvId)` を §8.7 の 4 値で返し、メモする。循環の規則は 1 か所に置く（名目適合は帰納的に Unknown、構造的な能力は余帰納）。実装の構造検査は宣言時に 1 回だけ行い、使用側は選択と前提の証明だけをする。組み込み適合は、同梱ライブラリの lang 宣言から `ImplTable` の通常の行として読む。一貫性検査は 1 本にする（現在は 2 系統）。判定の入口 15、循環ガード 8 種、不動点 4 種を置き換える。
6. **CallPlan パイプライン**：12 形態を、署名ビューへの正規化 → 引数の準備（1 回だけ分類）→ 副作用のない評価 → 順位付け（現在の `StrictBestCandidate` を吸収）→ 待ち引数 → 固定束縛の検査 → §7.3.1 の相関 → CallPlan、の 1 本で扱う。`CandidateResult` を配列に置き、勝者を再評価しない。暗黙の状態 8 個（`activeRequirementContract` など）は引数 `CallContext` に置き換える。却下理由の事実はホットパスで集めず、失敗時だけ別関数で再評価する。現在の `TryCandidateCore`（約 900 行、21 引数・6 out）と `BindCallCore`（約 640 行、借用配列 22 個）、`ValidateFixedConstruction` の第 2 選択パス、暗黙コンストラクターの「無音選択」の 3 回実行と巻き戻しは、この 1 本に吸収する。
7. **型検査（Typeck）**：期待型つきの双方向検査で行う。部品は演算子表（組み込み数値・Contract 選択・組み込み witness の 3 経路を 1 つに）、変換の選択（`(source, target, 書かれた操作) → 変換の種類` の純関数 1 つ）、共通型の選択（ReadTypeUnification を含む 1 実装）、完了要約（Never の型付けと継続の終点）、クロージャの Place 使用の分類器。Access Effect と Call Receiver Requirement は選択の前に要るので Binding に残す。列挙型の構築は、合成コンストラクター署名を経由して通常の呼び出し束縛で扱う（`Binding.Enums.cs:234-424` の独自パイプラインを廃止）。
8. **効果（Binding 側）**：宣言の検証と境界表だけを持つ。現在の `AvailableEffectBounds` を、前提だけを読む純粋な照会 `AvailableBounds(identity, type, env)` にし、適合の検証状態は読まない（読むと処理順に依存する）。Callable の義務を記録し、`Binding.ContractMatching.cs:578` の前提検査を残す。
9. **診断**：§7。
10. **Hover**：§5.9。

---

## 9. 領域別の処分表

段階は §12 を参照。R0 で、`src/Kimi/Compiler` の全 458 ファイルと `.ll.in` 5 ファイルについて、移す先の段階、置き換え先（§10.3 の区分）、削除・縮小・移設の別を 1 行ずつ書いた処分台帳を作る（§12.2）。下の表はその骨格である。

| 現行 | 処分 | 段階 |
| --- | --- | --- |
| `Parsing/Koto/Koto.cs:488-527` の意味スロット、`InvocationKoto`／`BinaryKoto`／`ConversionKoto`／`ForKoto`／`IsKoto`／`FunctionKoto` の計画スロット | `HirTables` への転送プロパティにし、後で削除する | R2a、R6 |
| `EvaluatedKoto`、`SyntheticKoto`、`FormattingKoto`、`RequirementCalleeKoto`、`TryKoto` の内部アーム | 削除（脱糖は MIR ビルダーへ） | R6 |
| 演算子クラス 37 個、子ノード走査の 3 重実装、項目ループの 3 重実装、型文法の 4 つの入口、`TokenReader` の属性の暗黙文脈、`ExtensionKoto` | 統合・削除（`UnavailableFeature_Kd` と同一行の修飾子認識は残す） | R2a、R9 |
| Lexing、Helper（`UnicodeIdentifierData` は生成データ）、Core | Tokenizer とパーサーで重複するキーワード集合の共有、`CompilerHelper` の未使用述語の削除、Core の設定検証と解析手順の重複の解消、`Compilation` の相の列と無効化（§5.1） | R2a、R9 |
| `Documentation/` | `src/Kimi/Documentation` へ移す（§5.2） | R9 |
| ルートの `ElementAccess`、`MatchTypes`、`StaticScalar`、`ScalarTypes`、`ReferenceTypes`、`SlotTypes`、`ComparisonTypes`、`FormattingTypes`、`SharedReadTypes`、`DefaultParameters` | R6 で旧 Analysis／Emission からの利用を消し、Binding からの利用は R7 で `TyCtxt`、`AdtDef`、MIR の Place へ移してから削除する | R6、R7 |
| `StrictBestCandidate` | CallPlan パイプラインの順位付けに統合する | R7 |
| `StructStorage`、`EnumStorage`、`ObjectTypes`、`AbstractTypes`、`FloatingTypes` | `AdtDef` と型の事実に吸収 | R2a |
| `OwnershipAnalysis*`（31 ファイル）、`OwnershipBody*`（15）、`OwnershipModel`、`OwnershipFlow`、`BorrowLiveness`、`PackedAnalysisTable`、`OwnershipStorage*`、`InterpretationContext`、`StructuralCompletion`、`MatchCoverage.FromSyntax` | 全削除し、`Mir/*` で置き換える | R6 |
| `ControlFlowAnalysis.cs`、`ControlFlowTypes.cs` | 型の流れは R1 で削除する。構文上の検査と修復候補は HIR lint へ、到達性は A3 へ移す | R1、R6 |
| `BodyLowering*`（37 ファイル）、`EmissionModule`、`LlvmModuleWriter*`（25 ファイル）、`*GenerationPlan`、`GenericStoragePlan*`、`CompilerFunctionAdapters`、`NumericArithmetic`、`StaticScalarEntry` | 削除。Array と書式化の IR 補助関数（約 1.5k 行）は、R5 で Backend の暫定クラス（`LegacyRuntimeHelpers`）へ移して R8 まで残す | R5、R6、R8 |
| `AggregateLayout`、`FunctionAbi*`、`WindowsLowering*`、`LlvmEmitter*` | `Layout`、`Abi`、ドライバーに置き換える | R5、R6 |
| `LlvmConstantPool`、`SourceLocationTable` | 整理して残す | R5 |
| `NativeToolchain*`、`EmissionArtifacts`、`Kernel32Imports`、`ToolchainResolver`、`Artifact*`、`WindowsProfile`（計 1,317 行） | `src/Kimi/Build` へ移す（IMPL §20.8.3 のマニフェストに外部宣言を記録） | R9 |
| `.ll.in` | `Utf8BufferRuntime`（431）と `Utf8FormatRuntime`（607）の大半を削除する。`Utf8FloatRuntime`（217）は書式化の外側を Kimigayo へ移し、`formatFloat` の入口だけを残す。`Utf8FloatRyu`（776）と `WindowsRuntime`（304）は残す | R8 |
| Binding の診断（`ReportIssue`、`BindingFailure`、`Binding.Diagnostics` の大半、`OriginDiagnostics`、`ArithmeticDiagnostics`、`CaseLimits`、`DuplicateDiagnostics`、`PositionWarnings` の独自評価器） | Problem と公開アダプターに置き換える | R1、R7 |
| `KimiLibraryValidation` と署名・レイアウトの mini-DSL 5 種、`KimiLibraryCatalog`、`KimiLibraryKinds`、`KimiDeclaration`、`KimiLibrarySources` の署名専用モード | テストへ移し（R1）、lang の索引に置き換える（R8）。IR の固定オフセット（`LlvmModuleWriter.Formatting.cs:14,43`、`LlvmModuleWriter.DictionaryIteration.cs:79`）をフィールド名の参照にしてから検証を外す | R1、R8 |
| `Binding.EffectBounds`（2,205 行）、`CallableEffects`・`VirtualEffects` の検査部、`OwnershipAnalysis.EffectBounds`、`OwnershipBody.CleanupEffects` | `Mir/Effects` に置き換える。`EffectDeclarations` と `AvailableEffectBounds`（§8.8 の純粋な照会にして残す） | R6 |
| `OriginInference`、`OpenOrigins`、`OriginDeclarations` | 縮小して残す（OJ5、OJ6） | R7 |
| `Binding.LocalRegions`、`CallRelations` の判定部、`OriginProof` の判定部、`OriginProofMetrics`、`ResultPremises` の 6 モード、`OriginPremises`／`OriginPremiseClosure` の要求ごとの環境再構築 | 削除。WF の列挙器 1 本と body ごとの閉包 1 つを残す | R6、R7 |
| `ArrayInference`、`ArrayArguments`、`ShapeEvidence`、`ShapeContracts`、`InferenceBoundaries`、`ConstructorValidation`、`NestedCalls`、`*Metrics` | `InferCtxt` と固定束縛の検査に置き換える | R7 |
| `Calls`、`CallSelection`、`CandidateEvaluation`、`ValueCalls`、`CallableSelection`、`ArgumentOperations`、`Enums` の独自経路、`BoundVirtualCall`、`BoundIteration`、`ObjectCreation`、`ImplicitConstructors`、`PropertyCalls`、`BaseCalls`、`ContractCalls` | CallPlan の型は R2a で統合し、パイプラインは R7 で置き換える | R2a、R7 |
| `Contracts`、`ConditionalConformances`、`ContractMatching`、`InheritedConformances`、`ContractAgreement`、`Capabilities`、`FixedArrays`、`IntegerPositions`、`BuiltinArithmetic`、`AssociatedTypes`、`AssociatedInference`、`Specializations`、`RequirementInstances` | `ContractSolver`、`ImplTable`、実装検査、一貫性検査に置き換える（関連型推論の扱いは §16-6） | R7 |
| そのほかの Binding（`Types`、`TypeRelations`、`Lengths`、`Conversions`、`Constraints`、`Closures`、`FunctionTypes`、`FunctionItems`、`Properties*`、`Patterns`、`Ranges`、`Sequences`、`Expressions`、`ControlFlow`、`Attributes`、`Startup`、`Documentation` など） | §10.3 の各区分（型、型検査の式、Callables、収集と名前解決）へ縮小・統合する。区分ごとの対応は R0 の処分台帳に書く | R7 |
| `Binding.Hover*`、`Binding.VirtualHover`、`Binding.EffectHover` | `src/Kimi/Checking/Hover` へ移す | R2a |
| `DiagnosticCode.tinyhand`、`DiagnosticRequirement.tinyhand` | §6.3 のとおり削除と追加を行う。要件の閉じた列挙を `DiagnosticRequirement.tinyhand` に置き、各コードが取りうる要件の集合を整合検査のために書く（§7） | R1 |

---

## 10. 規模の目標

### 10.1 見積もりの方法

- 調査の各報告が自己申告した削減量に、即時に削除できる種類（診断、未対応、自己検証、dead code）は 85%、再設計に依存する種類は 60% を掛けた。
- 検証の結果として残すことになった分を加えた：Origin の保持（OJ1〜OJ7）＋900、既定値テンプレート＋400、§7.3.1 の呼び出し時相関＋150。
- 新設するコードは明示的に加えた。Documentation とツールチェーンの移設は基準から除く。テストと Checking への移設（約 4,200 行）は基準に含めたまま、§0 に内数として示す。
- 自己申告の目標は楽観的と見て、借用検査 3,300 行、コード生成 3,300 行とし、領域ごとに約 10% の上限余裕を持たせた。

### 10.2 領域ごとの目標

| 領域 | 現行 | 目標 | 上限 | 主な手段 |
| --- | ---: | ---: | ---: | --- |
| Binding | 60,703 | 38,000 | 40,000 | 内訳は §10.3 |
| Analysis → Mir | 20,613 | 12,650 | 14,100 | モデル（検証器とダンプを含む）1,500、構築 5,000（脱糖 600、テンプレート 400 を含む）、データフロー 1,100、drop 450、借用検査 3,300、効果 1,000、lint と能力検査 300 |
| Emission → Backend（C#、ツールチェーンを除く） | 19,542 | 7,500 | 8,500 | 単相化 1,200、Layout 800、コード生成 3,300、実行時の結線 1,000、能力表とドライバー 500、import 300、予備 400 |
| `.ll.in` | 2,335 | 1,300 | 1,450 | Ryu 776、OS シム 304、浮動小数の書式化入口。`formatInteger128` は暫定で約 +110 |
| Lexing・Parsing・Helper | 25,152 | 23,000 | 23,500 | 子ノード走査 −750、演算子 −550、意味スロット −650、ExtensionKoto ほか −250 |
| Core・ルート | 2,926 | 1,900 | 2,200 | ヘルパーを吸収 |
| **小計（移設対象を除く Compiler 配下）** | **131,271** | **84,350（−36%）** | **89,750** | |
| Documentation | 4,338 | 移設 | — | `src/Kimi/Documentation` |
| ツールチェーン | 1,317 | 移設 | — | `src/Kimi/Build` |
| 現行の Compiler 配下の合計 | 136,926 | — | — | |

`CompilerSizeBudgetTest` は移設後の Compiler 配下（`.ll.in` を含む）を数える。89,750 行は領域ごとの上限の和で、各領域の上限の初期値として使う。合計の上限は §12.2 の段階ごとの値で、R9 を出るときは 85,850 行とする。Compiler の外では、Kimi ライブラリが 2,109 行から約 3,200 行に増え、`src/Kimi/Diagnostics` は約 5,500 行を維持し（公開アダプターを含む）、Hover の約 1,200 行が Checking に入る。

**兄弟計画との比較。** 兄弟計画の 82,000 行は、C# だけ（`.ll.in` を除き、Documentation とツールチェーンを含む）の値である。同じ基準では本計画は約 88,700 行で、差は約 6,700 行ある。§16-5 と §16-6 の簡素化（計 −900 行）を採っても約 87,800 行にとどまる。82,000 行に揃えるには、Binding の目標を約 32,000 行にするなど、追加の大きな削減が要る（§16-1）。

### 10.3 Binding 38,000 行の内訳

| 区分 | 目標 |
| --- | ---: |
| 収集、名前解決、宣言表、アクセス、属性、起動 | 5,000 |
| 宣言クエリ | 3,000 |
| 型（`TyCtxt`、Subst、Relate、Lengths、定数評価、リテラルの適合、型の事実） | 2,500 |
| Contracts（関連型推論を含む） | 6,000 |
| Origin（署名と OJ5） | 5,500 |
| 型検査の式と HIR lint | 6,000 |
| 呼び出しと `InferCtxt` | 5,200 |
| Callables | 2,300 |
| `HirTables` と Plan | 1,000 |
| ライブラリの索引 | 600 |
| Problem と型の表示 | 900 |

### 10.4 予算のラチェット

`CompilerSizeBudgetTest` が領域ごとの上限を検査する。

- 削減の段階（R1、R2a、R6〜R9）を出るときに、上限を「実測＋2%」へ下げる。
- 並走の段階（R2b〜R5）は、§12.2 の合計上限と、新設領域（Mir、Backend）の上限だけを検査する。
- §12.2 の上限は本計画の採用をもって承認されたものとする。それ以上に上げてよいのは、利用者の明示の指示があるときだけとする。ただし §18 P5 により、O2 の性能とゼロアロケーションを守るために必要な超過は認め、理由をコミットと PLAN に記録する。

---

## 11. 仕様・文書の改訂

いずれも採用後に、該当する段階の単位のコミットで行う。理由と Kimigayo 原則との整合をコミットに書き、影響する章・例・テスト・マイルストーンを同じコミットで直す。最初の仕様取り込みの時点で、本計画を `draft/INTEGRATED.md` に一部取り込みとして登録し、取り込んだ範囲を凍結する。

| 対象 | 改訂 | 段階 |
| --- | --- | --- |
| SPEC §23.3.6.1、§23.3.6.2 | Unsupported 区分を「単一コード、主位置だけ、Reason・Note・Advice・修復候補なし、最初に扱えない相が 1 回報告する、依存部分は記録しない」と定義する。「要件が違えば別コード」の規則に Unsupported の例外を明記する。§23.3.6.2（:160。インスタンスや条件の違いは区別する引数で説明する）に、能力不足の Unsupported はインスタンス引数を記録に含めないと明記する | R1 |
| SPEC §23.3.6.4（:176、:178） | 能力不足の Unsupported はインスタンスごとに分けず位置ごとに 1 記録とし、同じ位置の未対応は 1 記録に併合する | R1 |
| SPEC §8.10 Diagnostics（:1280）、IMPL §21.3.5（:611-615） | インスタンスの能力不足は位置だけの Unsupported とする（D6）。表現・資源の義務の診断は §8.10 の内容（定義位置、引数、失敗した条件）を保つ。生成できない表現の結果の区別を改める | R1、R5 |
| SPEC 各章、utf8-formatting、arithmetic-contracts、associated-type-inference、IMPL 付録 A の Advice・Note の記述（約 55 箇所） | §7 の監査表に従い、MAY にするか削除する（D3） | R1 |
| IMPL 付録 A §A.8（:137） | OCC の状態が未計算のとき、継承適合の witness は実装の位置、適合を経ない使用は使用の位置で、位置だけの Unsupported を 1 回報告する。実装名・射影経路・未完了の検証の名指しを求めない（§6.2） | R1 |
| IMPL §21（:603、:605、:698、:702）、付録 B（:18、:22、:30、非規範） | 生成時の未対応は `Unsupported_Kd` で報告する。所有権解析の記憶表の上限（:603）を、MIR の借用検査の作業領域の上限に改める。インスタンスの所有権表の上限（:605）と、インスタンス化の後の具体解析の記述を、「ケースが選んだ検証済み計画の置換、drop elaboration、表現・能力の検証」に改める。既定値展開の本体サイズの上限を追記する | R1、R6 |
| IMPL 付録 A §A.3（:57-69） | 段の列を、効果の不動点を MIR の P8 に置き、適合の構造は Binding、要件境界の witness 検査は P8 の義務として行う順序に改める | R6 |
| SPEC §14.10.3（¶2〜¶4）、§15.6.1 の Liveness 行、§14.9.2 の defer の例、IMPL 付録 A:153 | 継続の始点・終点、腕の末尾、発散、Never の値の扱いを明記する（§5.6.3）。付録 A:153 に正例と負例を追加する。差分の分類は差分台帳で行う | R6（単位 a） |
| SPEC §10.1 step 5 または §8.1.3、§10.8、§15.3.4 | 型引数に代入された宣言 Origin 関係は選択後に判定すると明記する。erasure と Function 値の fit は構造部分だけを適用性に入れる。局所注釈の本体推論変数は契約全体の比較で制約として扱う（§16-3 の判断による。付録 B の T1〜T4） | R7 |
| SPEC §8.4.4、§8.7、§8.5 | 継承適合の排他 witness の OCC を、選択後の使用ごとの義務に改める（§16-2 の判断による。それまでは汎用 Unsupported を維持し、SPEC は変えない） | R7 以降 |
| SPEC §8.4.10.4 または §8.4.5、§15.6.4 | callee の効果要約はその宣言を自分の前提の下で見たものであり、要件呼び出しは callee 自身の利用可能な境界を使う、と明記する | R6 |
| SPEC §8.4.10.7（:926）、§23（:295）の相の名前 | 区分を改名する場合に直す | §16-7 の判断の段階 |
| SPEC §6.5、§22.1、IMPL §21（Kimi の宣言の識別） | 同梱ソースでだけ認識する実装属性 `#Lang`、`#Intrinsic`、`#CallerLocation`、`#Unsupported` を定義する。利用者のソースでは未知の属性とし、規則ではなく識別であることを書く（§16-9） | R8 |
| SPEC §22.1.2.5（:211、:219、:243-247）、§22.4（:738）、§22.5.5（:865）、utf8-formatting §1.1、testing-profile、IMPL §21.2.2、§21.5.3 | 管理 Type の記憶レイアウトを内部の表現 struct と同一と定義する。私的プリミティブと intrinsic を §5.8 の表と一致させ、それぞれ所属する節に書く。writeLine を Kimigayo の本体にする | R8 |
| IMPL §20.8.3 | リンクマニフェストに外部宣言（シンボルと dllimport の別）を記録する | R9 |
| SETTLED「Instance-evaluated generic defaults」 | §16-15 で承認された場合、Proposed fix と理由の機構記述（解釈文脈と proof anchor）を、テンプレート展開と再帰 SCC の評価関数に書き換え、却下の対象を「全既定値を out-of-line の評価関数にする案」に限る。承認されない場合は変えない | R6（単位 a） |
| SPEC §15.7、§15.7.1 | `replace`／`exchange`／`swap` を、宣言の同一性で借用検査が認識する Kimigayo の本体（raw の take と `Raw.initialize`）と定義し直す。転送が利用者のコード・破棄・Abort を実行しないことは、本体の unsafe 義務として書く | R8 |
| SETTLED（追加） | 「Array／Dictionary／string を raw フィールドを持つ通常の struct にする」案を不採用として記録する | R8 |
| `docs/dev/DIAGNOSTICS.md` | rule 3（未対応の機能を Reason に書く）、rule 6（構造による全順序）、§3 と §4.1（要件と主語、鍵）、§4 の該当段落（修飾子の Advice、OCC の詳細）、§4.2 の相と区分、§11／§12 の資源境界の記述（:264、:286）を改める | R1、R6 |
| `docs/dev/COMPILER_ARCHITECTURE.md` | U0〜U10 の台帳を本計画の段階 R0〜R9 に置き換え、依存規則と MIR の語彙を記す | R0 |
| `docs/STYLE.md`、`docs/LIBRARY.md`、`src/Kimi/Library/README.md` | 同梱ライブラリの属性、表現 struct、Kimigayo で書く操作の規約 | R8 |
| `docs/dev/VERIFICATION.md`、`docs/dev/HOVER.md` | 共有ヘルパー（`MinimalEmissionTest.Analyze` など旧 Emission に依存するもの）とフィクスチャの方針（R3、R6）、Hover の移設（R2a） | R2a、R3、R6 |
| `docs/dev/CODEMAP.md`、`docs/STATUS.md`、`docs/dev/PLAN.md`、`docs/dev/PLAN_HISTORY.md` | 各段階の同じコミットで更新する。STATUS は支持境界が変わったときだけ更新する。D2 による後退は、それを生じた段階（R1 か R6）の単位で STATUS と PLAN に記録する | 各段階 |

---

## 12. 移行計画

### 12.1 共通の規則

- 各単位は「再現 → 実装 → 焦点テスト」で進め、Unit 検証を通してコミットし、プッシュする。各作業セッションの終わりに Session 検証を 1 回走らせる（段階の終わりのセッションを含む。[VERIFICATION](../../docs/dev/VERIFICATION.md)）。
- 証跡は `artifacts/verify/reduction-<段階>`、差分台帳は `artifacts/verify/mir-diff`、計測は `artifacts/benchmarks` に置く。
- **新経路の切替。** R5 までは、既定の経路を旧経路のままにする。新経路は、R3 で加える切替（`Compilation` の内部プロパティ）でだけ動かし、R3〜R5 の間だけ環境変数 `KIMI_PIPELINE=mir` でその既定値を変える。xUnit、ネイティブフィクスチャ、マイルストーンハーネスはこの環境変数で新経路を選ぶ。経路（`legacy`／`mir`）は実効構成の一部として扱い、`kimi check` の JSON の `compiler` 識別とテスト成果物の ArtifactId の manifest に含める（SPEC §23.3.1、§23.3.6.8、testing-profile）。言語サーバーは起動時の値を固定して使う。切替と環境変数は R6 の単位 b で削除する。
- **差分ハーネス。** 入力は、xUnit が作る全コンパイル、`DiagnosticCorpus` と構文コーパス、同梱ライブラリ、全マイルストーン、SPEC の ```kimi の例。比べる項目は、受理、(code, 主位置, Reason の事実)、ネイティブの stdout と exit code。台帳は 1 差分 1 行とし、全件を次の 5 つに分類する。
  - (a) 旧実装の誤り
  - (b) 新実装の誤り（修正する）
  - (c) SPEC の曖昧さ（明確化する）
  - (d) D4 などによる意図した変化（根拠の条項と付録 B の番号を付ける）
  - (e) D2 による不移植（STATUS と PLAN に記録する）
- **凍結（D5）。** R0 の開始から R6 の完了まで、旧 Analysis／Emission に受理範囲や生成機能を加えない。狭い形を受理するための修正もしない。許す変更は次に限る。
  - 削除
  - 誤コンパイルの修正（新経路にも同じ単位で反映する）
  - §12.2 の作業欄が明示する変更への機械的な追従。受理範囲と生成結果は変えない。例：R1 の未対応の一本化と固定オフセットの名前参照化。R2a の CallPlan 型、転送プロパティ、`AdtDef` への吸収（`StructStorage` ほか 5 ファイル）、演算子クラスと子ノード走査の統合。R5 の IR 補助関数の移設（`LegacyRuntimeHelpers`）と共有部品の整理
- 共有作業ツリーで並行する作業とは、ファイルの所有を分け、明示したパスだけをステージする。R3〜R6 は単独で進める。

### 12.2 段階

合計の上限は Compiler 配下（`.ll.in` を含む）の行数。R9 までは移設前なので、Documentation とツールチェーン（計 5,655 行）を含む。R1 と R2a の上限には、テストと Checking への移設（約 4 千行）も含む。

| 段階 | 作業 | 開始条件 | 終了ゲートと削除ゲート | 合計の上限 |
| --- | --- | --- | --- | ---: |
| **R0 基準と凍結** | 基準の記録：全テスト、マイルストーン、ネイティブフィクスチャの受理・診断・出力のスナップショット、Binding と Benchmark の計測（§13.3）。`CompilerSizeBudgetTest` と `ArchitectureRulesTest` の導入（規則の有効化は段階ごと。§14）。全ファイルの処分台帳（§9）。狭い部分実装と、DIAGNOSTICS §11 の Language コードの実装制限の一覧、それらとマイルストーン・ライブラリの依存の表。SPEC の Advice／Note の監査表（§7）。Benchmark の計測の分類（§13.3）。抽象型の破棄の穴（付録 B の X1）の再現。D5 の凍結開始。10-09 計画の正式な処分（D1） | 利用者の開始指示と §16-1・§16-14 の判断 | 証跡がそろい、処分台帳と一覧に未分類が残っていない | 136,926 |
| **R1 削除先行** | 未対応の一本化（§6）。旧 Analysis の位置付きのゲートは `Unsupported_Kd` にし、旧 Emission の位置のない機能ギャップは R6 までの既知の食い違いとして STATUS に記録する。D3 の仕様改訂と Advice／Note の削除（§7）。IR の固定オフセットを名前参照にした後で、ライブラリ検証をテストへ移す。dead code、計測フック（読む Benchmark とその .md を同じコミットで更新）、旧 API の削除。制御フロー解析の型の流れの削除。OCC-X の待機機構の削除。R0 で「依存なし」とした狭い部分実装の削除 | R0、§16-12 の判断 | Session が緑。受理の変化は、削除した狭い部分実装の Unsupported 化だけで、全件を (e) として STATUS と PLAN に記録済み。それ以外の期待値の更新は、コード名、Reason（D6）、Advice／Note（D3）の分だけ。テストから削除済みの仕組みへの参照が 0 | 127,000 |
| **R2a 表の器と純化** | `SyntaxId`（Binding が作る合成ノードへの追加の番号を含む）、`HirTables`（Koto の転送プロパティ経由）、`AdtDef`、CallPlan の型の統合（旧経路の消費者は機械的に追従させる）、fit の純化と選択後の bound 収集（選択が変わらない範囲。T1〜T4 は含めない）、子ノード走査と演算子クラスの統合、Hover の移設。テストの意味スロット参照を `SemanticQuery` のテスト用ヘルパーへ移す（§13.2） | R1 | 選択とテストの期待値が不変。warm rebind のゼロアロケーション。Bind の warm／cold の時間を記録する（後退は許容。§18 P3） | 125,000 |
| **R2b 意味表と宣言表の完成** | `TyCtxt` と `Subst`（旧 `BoundType` からの変換層を含む）。`HirTables` の `Op`／`Adjust`／`PlaceShape`／`Completion` 列と `PatternPlan`・`ClosureDef`・`ExpansionPlan` を、旧経路向けの合成 Koto と並べて書く。`DefaultDecl` と `PreparedParam(i)`。`ImplTable`・`VTableDef`・`UniversalRelations`・`EffectBoundTable`・`AvailableBounds` の読み取り専用ビュー（旧 Binding の状態から構築）。intrinsic 表（`CompilerFunctionKind` 75 種との対応表） | R2a、§16-3 の判断 | 旧経路の期待値が不変。ライブラリと全マイルストーンで、MIR ビルダーが読む全列が埋まっていること（検査テスト）。Mir 層から Binding 内部への参照が 0 | 129,000 |
| **R3 MIR の核と構築（影）** | モデル、記述子表、`MirValidator`、MIR ダンプ、ビルダー（テンプレートの展開、ケースビュー、`CheckTarget`、drop 木、判定連鎖、`Opaque`、`staticEnsure`、テストの検証文）。新経路の切替。テストの `c.Ownership.*`／`c.Emission.*` の利用を、切替に従う `Compilation` の公開入口へ移す（§13.2）。§5.6.2 の SETTLED の条件の確認 | R2b、§16-10・§16-15 の判断 | ライブラリと全マイルストーンの全本体が Validator の指摘なしに構築でき、`HasOpaque` の本体は R0 の一覧にある D2 の後退だけ。代表的な MIR ダンプのゴールデン。Allocation テスト | 135,500 |
| **R4 MIR 解析（影）** | A1〜A6 と差分ハーネス。本体の構造を読むテストを MIR ダンプのゴールデンへ移す | R3、§16-4 の判断 | 未分類の差分が 0、(b) が 0。同じワークロードで「MIR 構築＋A1〜A6」と旧「制御フロー解析＋所有権解析」の時間を記録する（§18 P3） | 142,000 |
| **R5 単相化と生成（影）** | 単相化、shim、Layout（管理 Type は現在の規則を移植）、Abi、コード生成、能力表、intrinsic 表（旧 IR 補助関数は `LegacyRuntimeHelpers` へ移し、暫定の行として呼ぶ）、デバッグ情報の設計（§17.3） | R4、§16-11 の判断 | 全フィクスチャの O0／O2 とマイルストーンが一致するか、(e) として記録済み。LLVM verifier が緑。O2 の実行時間が揺れの範囲内（§18 P1、P4）。O0 の実行時間の変化を記録済み | 149,500 |
| **R6 切替と削除** | 単位 a で既定を MIR にし、§14.10.3 と SETTLED の改訂を同じコミットで行って Session を回す。単位 b で旧 Analysis／Emission、Binding の下流向けの照会、`EffectSummary`、本体内の Origin 判定、ルートヘルパーの下流利用、転送プロパティ、切替を削除する | R3〜R5 のゲート | Session が緑。`ArchitectureRulesTest` が、`OwnershipAnalysis`、`OwnershipBody`、`BodyLowering`、`EmissionModule`、`LlvmModuleWriter` と転送プロパティへの参照が src と tests に 0 件であることを確かめる（`LegacyRuntimeHelpers` は R8 まで除外）。D2 の後退を記録済み | 105,000 |
| **R7 Binding の統合** | Resolver、`InferCtxt`、`ContractSolver`（R2b のビューの内部を置き換え、公開形は変えない）、CallPlan パイプライン、Problem と Error の伝播、宣言クエリ、ルートヘルパーの残りの削除、T1〜T4 の修正 | R6、§16-2・§16-5・§16-6・§16-7 の判断 | 単位ごとに Unit 検証と受理差分の分類。Binding の計測を記録する | 95,000 |
| **R8 ライブラリ優先** | §5.8 の順で移し、IR 補助関数と `.ll.in` の該当部分を削除する。管理 Type の Layout を表現 struct からの導出に替える | R7（`src/Kimi/Library/*.kimi`、`.ll.in`、Backend の intrinsic 表と DropGlue だけの作業は R6 の後に並行してよい）、§16-8・§16-9 の判断 | O2 の A/B、ゼロアロケーション、utf8-formatting §6.2 の確保回数、フィクスチャの一致 | 91,500 |
| **R9 仕上げ** | フロントエンドの残り（項目ループ、型文法の入口、属性の文脈）、Documentation とツールチェーンの移設、IMPL §20.8.3、CODEMAP、DIAGNOSTICS、PLAN、PLAN_HISTORY、COMPILER_ARCHITECTURE | R7 と R8 | 規則試験と予算の確定 | 85,850（目標 84,350） |

### 12.3 稼働の維持と中止

- R5 までの各コミットは既存のスイートで検証される。新経路は切替でだけ動く。
- R6 では、単位 a の後に戻す手段を残し、単位 b で二重経路を閉じる。二重経路を持つのは R3〜R6 の集中期間だけとする。
- R4 で未分類の差分が残れば R5 に進まない。R4・R5 の各セッションの終わりに未分類の差分の件数を報告し、2 回続けて減らなければ利用者に相談して中止を判断する（§18 P9）。
- 中止する場合は `Mir/` と切替を削除して戻る。R1〜R2b の成果はそのまま残る。D5 の凍結を解き、R2a の転送プロパティを Koto の直接のフィールドに戻すかどうかは利用者が決める。

---

## 13. 検証と計測

### 13.1 ゲート

| ゲート | 内容 |
| --- | --- |
| G-Build | `verify.ps1` が警告をエラーとして通ること |
| G-Alloc | ゼロアロケーションと再利用の回帰が、アサーションを緩めずに通ること。warm-up を足して通さない |
| G-MS | `src/backend/windows-x64/test-milestone<N>.ps1` と `tests/milestones/stage-baselines.json` の結果を R0 の行列と比べる。後退は、原因と回復の見込みを PLAN と STATUS に記録したものだけ認める（D2） |
| G-Corpus | `DiagnosticCorpus` と構文コーパスのスナップショット差分を全件分類すること |
| G-Diff | 差分台帳に未分類が 0 件であること（R4〜R6） |
| G-Budget | 行数予算の検査（§10.4） |
| G-Arch | アーキテクチャ規則の検査（§14） |
| G-Perf | 移設や性能に関わる段階で、§13.3 の固定条件の A/B を取ること |

### 13.2 テストの方針

テストは削除する内部 API に強く結びついている（基点での概数）。

| 結びつき | 規模 | 置き換える段階 |
| --- | --- | --- |
| Koto の意味スロット（`.BoundType`、`.BoundSymbol`、`.BoundCall`、`.BindingState`） | 約 220 ファイル | R2a：`SemanticQuery` のテスト用ヘルパー（型、記号、選択した呼び出し）へ機械的に移す |
| `Compilation.Ownership.*`（`Analyze`、`Issues`、`Result`） | 約 400 ファイル・約 2,000 箇所 | R3：切替に従う `Compilation` の公開入口（受理、型付き Problem）へ移す |
| `Compilation.Emission.*`（`WriteIr`、`Validate`、`TryPrepare`） | 約 380 ファイル | R3：公開入口（IR の出力）へ移す |
| `c.Ownership.Bodies` の構造 | 約 270 箇所 | R4：MIR ダンプのゴールデンへ移す |
| 構文の編集 API（`KotoHelper.Replace`） | 約 107 ファイル | 残す（§5.1） |
| 制御フロー解析の型フィールド、`HasCheckingState` などの検査 API、`__kimi_array_` などの IR 名 | 約 15 ファイル、14 箇所、7 ファイル | R1、R4、R8 |

- 削除した仕組みのテストは、消すか観測できる振る舞いの検査に書き換える。新たに加えるのは、ライブラリの形状テスト、MIR のゴールデンダンプ、借用検査の規則テスト、concrete-twin 差分コーパスの族（抽象型の破棄の観測、Copy や needs_drop だけが違うインスタンス）。
- 既定値の判定基準は `GenericDefaultTest.ReplicasAgreeWithTheirBody`（8 スニペット × 8 配置）、`RecursiveDefaultTest`、`*DefaultTest` の Allocation タグ。
- R6 の単位 b の前に、旧型への参照がテストに残っていないことを `ArchitectureRulesTest` で確かめる。
- NativeAOT のテストは行わない。

### 13.3 性能と計測

- **計測の基準。** R0 で、`src/Benchmark` の各計測を「残す（入口だけ追従）」「MIR 版に置き換える」「削除する」に分類する。新旧で比べる固定ワークロードと段の境界（Bind／HIR 完了、MIR 構築、A1〜A6、単相化＋生成、opt／llc）を `CompilerPipelineMeasurements` に定義し、R0 の基準をこの境界で記録する。揺れの幅は同条件の反復で求める。
- 意味表、CallPlan、MIR のアリーナ、ケースビュー、インスタンスのローカル型キャッシュはすべてプールし、warm rebind で割り当てない。
- Loan の伝播は疎なビット集合を使い、生存区間の中でだけ伝播させる。現在の Place×Place の密な表（`OwnershipBody.Borrows.cs:767,1062`）は不要になる。最悪の場合は二次で増えうるので、作業領域の上限は Resource 診断として残す（§6.2）。
- O0 は opt パイプラインを通さないので、alloca と小さなライブラリ関数の呼び出しで遅くなりうる。添字と length は MIR の射影に残して極端な劣化を避ける。O0 の速度の後退は許容する（§18 P1）。変化は記録する。
- コンパイラ自身の処理時間の後退は許容する（§18 P3）。ただし二乗の挙動は規模の試験（§14-10）で検出して直す。
- ライブラリを Kimigayo へ移すと Bind のコストが増えうる（warm Bind 約 7.9 ms はライブラリが支配している）。ライブラリ検証の 2 回実行の削除で一部を相殺し、必要ならライブラリの意味表と MIR をプロセス内で共有する（§16-8）。

---

## 14. 再肥大化を防ぐ規則

採用後は、通常の実装手順に次の規則を加える。

1. **予算のラチェット。** §10.4。
2. **アーキテクチャ規則の試験。** `ArchitectureRulesTest` が次を検査する。各規則は、成り立つ段階から有効にする。
   - Mir の解析と Backend から、Koto、`KotoKind`、Binding の内部への参照がない。構文木を読めるのは MIR ビルダー（`Mir/Build`）だけ（R3 から）。
   - 語彙の数が固定されている：文 5、右辺値 9、終端 6、射影 7、Callee 5、Unsupported のコード 1（R3 から）。
   - 合成 Koto がない（R6 から）。
   - Binding に `Dictionary<Koto, …>` がない（R7 から）。
3. **新機能の追加手順。** 新機能は「`HirOp` か Plan の行を 1 つ」「ビルダー関数を 1 つ」「必要なら記述子表と演算子表か intrinsic 表の行」で足す。解析とコード生成は、新しい語彙が要らない限り変更しない。MIR の語彙を増やすには、SPEC の根拠と利用者の承認が要る。
4. **狭い部分実装を作らない。** 一般規則で実装できなければ Unsupported にする。`Supports*` のゲートや形のホワイトリストを追加しない。仕様が許す形に Language コードを出さない。
5. **下流で再検証しない。** 構造の検証は `MirValidator` の 1 か所だけ（全構成）。コード生成は独自の再検査を持たない。
6. **判定のコードは文面を作らない。** Advice は Diagnostic Development Workflow の明示の指示があるときだけ作る。
7. **アルゴリズムは Kimigayo で書く。** intrinsic は Kimigayo で表現できないものか、計測で必要性を示したものに限る。
8. **割り当ての規則。** 要素ごとのヒープオブジェクトを作らない。Allocation テストの条件を緩めない。
9. **修正の単位。** 「1 つの形を受理する」「1 つのケースの報告を直す」ための分岐を足す前に、その形がどの段の一般規則から外れているかを特定し、一般規則の側を直す。直せないなら Unsupported のままにする。レビューでは「影響を受ける解析とバックエンドは、公開された意味表と MIR だけから新しい形を処理できるか。各判断は 1 か所で行われているか」を確認する。
10. **規模に対する性能の試験。** Bind、MIR 構築、借用検査、単相化について、入力を 2 倍・4 倍にしたときの時間と割り当てを測る試験を置き、二乗の挙動を検出する。
11. **増減の報告。** コミットメッセージに領域ごとの純増減を書く。1 単位で 300 行を超えて増える場合は、置き換えたものと消したものを書く。
12. **簡素化のレビュー。** 単位の終わりに、実装者とは別のレビューで再利用・重複・効率を確認する。
13. **正規の部品。** CODEMAP に正規の部品（型の写像、Relate、Place の射影、CallPlan、能力表、Problem の記録など）を挙げ、新しいヘルパーを書く前にそれを探す。
14. **定期監査。** マイルストーンの完了ごとに、領域別の行数、変更の集中、重複の候補を計測し、`artifacts/benchmarks` に推移を残す。何度も修正されて増え続けるファイルは、次の修正の前に構造を見直す。
15. **テストの結合度。** テストは公開の入口（受理、診断、IR、MIR ダンプ）で検査する。内部の状態を読むテストは、その部品の規則テストに限る。

---

## 15. リスクと対応

| # | リスク | 対応 |
| --- | --- | --- |
| 1 | 受理の変化（D4、付録 B の C1〜C6・T1〜T4・X1・X2） | 差分台帳で全件を条項付きで分類する。未分類が残る間は切り替えない |
| 2 | ゼロアロケーションの後退（Allocation 印 648 箇所） | 表、アリーナ、ケースビュー、インスタンスのキャッシュをプールする。R2a〜R5 のゲートで確認する。既知の計測上の幻（ワーカースレッドの割り当てコンテキスト）を考慮する |
| 3 | 性能（Loan 伝播、ケースの再実行、O0 の alloca、ライブラリ関数の呼び出し） | 疎なビット集合、生存区間での伝播、§13.3 の固定条件の A/B、O0 は記録 |
| 4 | 既定値の展開の爆発と、診断の一意性 | 本体サイズの資源上限、SCC、展開部では lint を出さない、`GenericDefaultTest` の行列 |
| 5 | §14.10.3 の改訂で、共有 cleanup の検査範囲が変わる | 差分として記録する。早期終了の条件を保つ |
| 6 | 効果のスキーマ単位化で受理が厳しくなる（`Binding.EffectBounds.cs:1245` の有利なインスタンス化への依存がなくなる） | §8.4.10.4 に照らして分類する（X2） |
| 7 | ライブラリを Kimigayo へ移した後の unsafe コードの誤り、二重解放 | 空の表現の書き戻しを規則にする。ライブラリを新経路の最初の適合対象にし、使う構文を最小の部分集合に限る |
| 8 | 並走期間の長期化と二重保守 | D5 の凍結、R3〜R6 の集中、中止条件（§12.3） |
| 9 | テストの書き換え量（§13.2） | R2a〜R4 に分けて行う。内部のテストは公開入口と MIR ダンプへ集約する |
| 10 | 過去の設計判断の撤回（2026-10-07 `6255d91f` で局所領域を Binding に置いた判断、効果の検査相） | コミットに理由（原則 2、判定を 1 か所に、循環の除去）を書く。DIAGNOSTICS §4.2 を更新する |
| 11 | 抽象型の破棄の扱いが今は穴になっている疑い（`CleanupObservesBorrows`） | R0 で再現を試み、R4 の差分で分類する（付録 B の X1）。再現した場合は旧経路の既知の欠陥として別に記録する |
| 12 | 旧 Emission の位置のない機能ギャップが、R1 の SPEC 改訂から R6 まで §23.3.6.1 と食い違う | STATUS に既知の食い違いとして記録し、R6 で解消する |

---

## 16. 利用者の判断が必要な事項

D1〜D6 で決まっていないものだけを挙げる。各項目に推奨と、判断が要る段階を付ける。2026-10-10 に、すべて推奨どおりに決まった。ただし 11 は「O0 の後退を許容する」に決まった（§18）。

1. **予算の基準（R0）。** 推奨は「移設後の Compiler 配下で目標 84,350 行、合計上限 85,850 行（領域ごとの上限の和は 89,750 行）」。兄弟計画の基準（C# のみ、Documentation とツールチェーンを含む）で 82,000 行以下を採る場合は、基準を揃えると約 6,700 行の追加削減が要り、5 と 6 の簡素化だけでは届かない。
2. **継承適合の OCC（SPEC §8.4.4。R7）。** 選択後の義務へ改訂するか（代替の overload がある場合に拒否が増える）、汎用 Unsupported のままにするか。推奨は R7 まで Unsupported を維持し、その後で改訂する。
3. **適用性の中での Origin の判定時期（R2b、実施は R7）。** 付録 B の T1〜T4。いずれも §15 の外の受理の変化を伴う。推奨は採用。
4. **SPEC §14.10.3 の書き換え（R4）。** AGENTS 上は承認不要の改訂だが、付録 B の C1〜C6 の受理の差分が出るので確認を求める。推奨は採用。
5. **§7.3.1 の呼び出し時の相関（R7）。** 維持するか（推奨。約 150 行）、宣言時だけにするか（−450 行、拒否が増える）。
6. **関連型推論（R7）。** 維持するか（推奨。R7 の後で見直す）、明示の associate に限るか（−450 行）。
7. **診断区分と名前空間の改名（R7）。** Binding、ControlFlow＋Ownership、Emission をどう改名するか、時期はいつか。推奨は R7 で一括し、LSP の表示は互換に保つ。
8. **同梱ライブラリの意味表と MIR の共有（R8）。** プロセス内のスナップショットとして共有するか。`src/Kimi/Library/README.md` の「検証状態はコンパイルごと」方針の変更にあたり、スレッド安全性の確認が要る。推奨は R8 の計測を見て判断。
9. **lang 属性と SETTLED の関係（R8）。** `#Lang` などの属性が、SETTLED「Constructor delegation…」の「ライブラリだけの言語規則」に当たらないこと（識別であって規則ではない）の確認。
10. **既定値の展開サイズ（R3）。** 資源上限で止めるか（推奨）、評価関数へ自動で切り替えるか。後者は SETTLED「Instance-evaluated generic defaults」と D1 のインライン既定値の境界規則に触れる。
11. **O0 の性能の後退の許容幅（R5）。**
12. **`DefaultArgument*_Kd` の扱い（R1）。** 一般のコードへ統合するか、既定値専用のコードとして残すか。推奨は残す（主位置と根の対応づけだけで足り、コストが小さい）。
13. **兄弟計画との関係（採用前）。** 同日付の兄弟計画と本計画のどちらを採るか、または統合するか。採用しない方は、取り込みの記録とともに閉じる。
14. **凍結中のマイルストーン作業（R0 の前）。** D5 の凍結中、PLAN の実装順（P25 → P33 → P35 → P36 → OCC-X → P38、共有オブジェクトの U7 の残り）を止めるか、新経路の完成後に回すか。推奨は、誤コンパイルの修正（`P34_REMAINING.md` の誤コード系の根本原因など）だけを凍結の例外として続け、ほかは R6 の後に新経路で行う。
15. **既定値の文脈の扱い（R3 の前）。** SETTLED「Instance-evaluated generic defaults」が退けた案のうち、インライン複製を保ったまま置換を展開時に適用する部分を採るか（推奨。§5.6.2）、Scope に解釈文脈を記録する現行の方式を MIR に移すか。前者は SETTLED の再提起にあたる。

---

## 17. 既存の計画・文書との関係

### 17.1 2026-10-09 の計画と、その途中成果

- D1 により置き換える。境界規則（§1.3）は本計画の §4〜§5 に引き継いだ。
- 途中成果のうち、whole-value update の共通化、呼び出し入力の記録、`OwnershipFlow` による転送の共有、`BorrowLiveness` の分離、`EmissionModule` の記憶域の整理は、旧経路の中の改善である。R6 で旧経路とともに置き換える。得られた知見（呼び出し入力の順序と評価順の分離、転送の事実と各解析の規則の分離）は、CallPlan の `EvalOrder` と記述子表に引き継ぐ。
- `docs/dev/COMPILER_ARCHITECTURE.md` の U0〜U10 の台帳は、R0 で本計画の段階に置き換える。
- 10-09 計画の正式な処分（状態と移動先、`draft/INTEGRATED.md` の記録）は R0 で行う。

### 17.2 兄弟計画との相違

同じ依頼に対する兄弟計画 [2026-10-10 Compiler Reduction and MIR Plan](<../Obsolete/2026-10-10 Compiler Reduction and MIR Plan.md>) とは、目的、MIR を一度だけ構築する方針、未対応の一本化、D1〜D6 を共有する。本計画の検証で根拠を確かめた相違点は次のとおりである。

| 論点 | 兄弟計画 | 本計画 | 理由 |
| --- | --- | --- | --- |
| Semantics ケース | 文にケースマスクを付ける（§7.1、§7.3） | 型に導かれる語彙（`PairLayer`、`PairAcquire`）とケースビュー | 既定値テンプレートの展開でマスクの写像が要らない |
| インライン既定値 | Scope に解釈文脈を記録する（§7.1） | 展開時に置換を fold し、Scope には呼び出し位置だけを持たせる。再帰は SCC で評価関数にする | 解釈文脈が残ると各消費者が再解決し、G82 と同じ取りこぼしを生む |
| 到達しないコード | `CheckOnly` 辺で続きのブロックへつなぐ（§7.3） | 終端の属性 `CheckTarget` と 2 パス解析 | 転送は実行時の後続と検査の後続を同時に持ち、Never を返す呼び出しにも検査の後続が要る。単一パスで全辺に状態を流すと §14.10.3 ¶4 に反する |
| 呼び出しの中断 | `Call` に中断先を持たせる（§7.2） | 中断先を持たない。放棄は転送の実行時経路の `EndBorrow` と drop 木で表す | 放棄は Call の前に起きる（§7.2.3、§15.6.7）。Abort は巻き戻さないので辺にしない |
| 規模の目標 | C# のみ・Documentation とツールチェーンを含めて 82,000 行以下 | 同じ基準で約 88,700 行（本計画の基準では目標 84,350 行、合計上限 85,850 行） | §10.2 |

### 17.3 そのほかの文書と作業

- **PLAN。** 採用に合わせて R0 で現在位置と実装順を改めた。凍結中は誤コンパイルの修正だけを続ける（§16-14）。
- **STATUS。** 本計画は支持境界を広げない。D2 による後退は、それを生じた段階（R1 か R6）の単位で記録する。
- **Kimi Debug Adapter 計画。** [2026-10-09 Kimi Debug Adapter Plan](<2026-10-09 Kimi Debug Adapter Plan.md>)（方針決定済み）が求めるデバッグ情報（文の開始、変数の置き場所とデバッグ名、スコープ、生成補助関数の artificial 印）は、MIR の `SourceInfo`（文の開始の印）と `LocalDecl`（ソース名）、`FunctionCodegen` の出力として R5 で設計に入れる。KD1 以降の実装は本計画の終了後に新しいバックエンドの上で行い、旧 Emission には加えない（D5、§18 P8）。

---

## 18. 決定事項

2026-10-10 に利用者が決めた。

| # | 事項 | 決定 |
| --- | --- | --- |
| D1〜D6 | §1.3 | 維持する |
| §16 | 1〜15 | すべて推奨どおり。ただし 11 は P1 のとおり |
| P1 | 生成したプログラムの性能 | O2 の実行時間の後退は許容しない。O0 の後退は許容し、変化を記録する |
| P2 | ゼロアロケーション | 後退は許容しない。コンパイラの warm 処理のゼロ割り当てと、生成したプログラムの割り当て回数（`DynamicArrayCostTest`、utf8-formatting §6.2 など）の両方を対象とする。コンパイラ側は P11 で変更した |
| P3 | コンパイラ自身の処理時間 | 後退は許容する。計測して記録する |
| P4 | 後退の判定 | 同じ条件で繰り返し計測して求めた揺れを超える悪化を後退とみなす |
| P5 | 行数予算 | P1 と P2 を守るためなら超えてよい。超えた分は理由を記録する |
| P6 | 兄弟計画 | 2 つとも閉じる（`draft/Obsolete`、`draft/INTEGRATED.md`） |
| P7 | 2026-10-09 の計画 | `3f6897ab` で削除したまま、復元しない |
| P8 | 並行する他の提案 | Semantic Tokens、LSP Completion、Kimi Debug Adapter は、本計画の終了後に扱う |
| P9 | 中止判断の基準 | R4・R5 の各セッションの終わりに未分類の差分の件数を報告し、2 回続けて減らなければ利用者に相談する（§12.3） |
| P10 | 再発防止 | §14 の 10〜15 を加え、AGENTS.md に反映する |
| P11 | 割り当ての方針（2026-10-10 変更） | コンパイラは、コンパイルが速くなるなら割り当てを許容する。速さに寄与しない割り当ては足さない。コンパイラのゼロ割り当てのテストは、計測で速くなると示した変更に限って改めてよい。生成したネイティブコードの割り当ては控え、割り当て回数の後退は許容しない（P2 の後半は維持） |

## 付録 A：調査の方法と証跡

- **調査**：`src/Kimi/Compiler` を 15 の観点に分けて並列に読んだ。観点は Binding 7（宣言と型、呼び出し、Callable と効果、式、Origin、Contract、診断・Hover・ライブラリ）、Analysis 3（構築、ソルバー、制御フローと Core とルート）、Emission 2（lowering、計画と writer）、フロントエンド 1、未対応報告の横断 1、機能の横断重複 1。そのうえで横断の統合を行い、主要な件数を再計測した。
- **設計**：独立した 3 案（MIR 中心、削除先行、Binding 先行）を作った。調査で意見が割れた 6 つの論点（既定値、本体内の Origin、ジェネリック本体の 1 回検査、検査専用の継続、効果、ライブラリ優先）を SPEC とコードに照らして検証した。最後に、3 案を採点して統合した。
- **レビュー**：本書の初稿を、根拠の正確さ、仕様・確定事項との整合、内部の一貫性、完全性の 4 つの観点で独立に検査し、指摘を反映した。
- **証跡**：各報告と検証の全文は、Git 管理外の `artifacts/verify/ultra-compiler-reduction-2026-10-10/`（`survey/` と `design/`）に保存した。本文の行数と件数は `419e8e79` での再計測値である。
- **確度**：本書は静的な調査に基づく。付録 B の受理の差分は推定であり、R0 の再現と R4 の差分ハーネスで確認する。

## 付録 B：受理の差分の一覧（推定）

### 検査専用の継続（§5.6.3、SPEC §14.10.3）

| # | 内容 | 該当する形 | 予想される変化 |
| --- | --- | --- | --- |
| C1 | 保留中の終端履歴を外側で合流させる旧規則をやめる | 完了しない `do` の中で、ある枝が値を消費してから `return` し、別の枝も `return` した後、`do` の後ろで同じ値を使う形 | 旧はエラー（推定）、新は受理。`do` で包まない同じ列は旧でも受理される。§14.10.3 ¶4 と一貫する |
| C2 | MixedTargets、replay、転送先による濾過をやめる | ラベル付きの `do` の中で `return` の後に `exit to` が続き、`do` の後ろに文が続く形 | `exit` の cleanup 前の状態も後続に合流し、新のほうが保守的になる |
| C3 | 旧の Unsupported 境界を撤去する | 効果を伴う発散、等しくない Loan の合流、deferred cleanup、反復の replay、`for`、`defer`、未対応の match の形、一般の逆辺と continue のガード | 一般規則で検査されるようになる |
| C4 | 発散する cleanup の後の状態 | `defer` の本体の中の `loop` や Never を返す呼び出し | 旧は `defer` の入口の状態、新は発散点から残りの cleanup と引き渡しを経た状態 |
| C5 | Never を返す呼び出しを引数に持つ呼び出し | 引数の評価が発散する呼び出し | 旧は検査領域で呼び出しの効果を適用し、新は callee に入らない（§15.6.7） |
| C6 | 構造的完了のない `while` の条件 | 条件の評価が正常に完了しない `while` | 旧は本体局所の証明が成り立つときだけ扱い、新は常に 0 回反復の状態を使う |

### 適用性の中の Origin（§5.5、§16-3）

| # | 内容 | 予想される変化 |
| --- | --- | --- |
| T1 | 型引数の宣言 Origin 節を適用性の中で判定し、Unknown なら候補を保留にしている（`Binding.Calls.cs:2120` → `Binding.ConstraintValidation.cs:173`） | 選択後の判定に移すと、有限 Origin を含む値を節つきの型でジェネリック関数へ渡す形が受理されうる |
| T2 | erasure と Function 値の fit の Origin 部を適用性で判定している（`Binding.Expressions.cs:396-402`、`Binding.TypeRelations.cs:21-23`） | §10.8 のとおり構造部分だけを適用性に入れると、選択が変わりうる |
| T3 | 期待結果の推論が、Origin を考慮する fit で失敗しうる（`Binding.Calls.cs:2306-2316`、`:1797`） | Origin だけが原因の不適用がなくなる |
| T4 | 契約全体の比較で本体の推論変数の扱いが非対称（短い端なら bound を付けて成功、長い端なら失敗。`Binding.OriginProof.cs:213-224`） | 両端とも「満たせる」に揃えると、関数参照の選択が変わりうる |

### そのほか

| # | 内容 | 予想される変化 |
| --- | --- | --- |
| X1 | 抽象型の破棄を、到達可能な全 Origin を観測するとみなす（SPEC §15.6.6） | ジェネリック本体で新たな拒否が出うる（例：コンテナを共有借用して得た関連型のイテレーターを保持したまま、そのコンテナを置き換える形）。現在はインスタンス再解析が具体型で受け止めている |
| X2 | 効果をスキーマ単位で計算する（§5.5） | 呼び出し側の具体型が持つより強い Contract の境界を拾えなくなり、拒否が増えうる。上限 1024 による偽の違反はなくなる |
| X3 | 借用検査を SPEC §15 から作り直す（利用者判断 D4） | 個別の差分は R4 の差分台帳で分類する |
