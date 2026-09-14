# Implementation Plan

> 現行方針: 正式な根拠はSPEC.md本文。doc/は検索・読込・参照・仕様統合・実装/監査の根拠から除外し、採用済み・優先というリンクも辿らない。今回のPlan再開で旧要件・依存・指摘を本文へ照合した。除外したIDと詳細の履歴は§13.2に保存し、実装済み扱いにはしていない。

更新日: 2026-09-14（Asia/Tokyo）、Plan cycle 2再開（0007-plan）。対象HEAD: `ff053be41aa4ea70ec34a0d43df63a1988bf9da7`。保護対象変更で未受理となった0006-planの部分作業と開始前の外部差分を保持して照合した。今回の結果は§2.7、前試行は§2.6、仕様境界/分割履歴は§13、次の2枠は§14.6–14.8。§14.1–14.5は完了済み/部分実装済みの過去契約であり、現在の次操作ではない。

## 1. 目的・対象範囲

SPEC.md 本文の確定済み規則を、字句・構文解析、Binding、所有権検証、コード生成、通常native実行まで接続し、既存CLI・LSP・エディター拡張を含むプロジェクトの完了条件を定める。

- 根拠は [AGENTS.md](AGENTS.md)、[SPEC.md](SPEC.md)、実ソースと実行したテスト。 [STATUS.md](STATUS.md) は調査の索引に使い、実装の証拠とは区別する。
- doc/以下を検索・読込・参照せず、SPECのリンクも辿らない。本文に記載された規則だけが有効で、外部参照から詳細を補完しない。Design/の旧例も規範にしない。
- SPECに詳細がなく外部文書への参照だけがある契約は、対応SPEC分割IDに本文の不足・質問・影響・解除条件を記録する。確定規則は再承認を求めず、未設計API/言語仕様は創作しない。
- 初期の実行対象は SPEC §21.5 の windows-x64-v1。ほかの出力profile、runtime Contract Views、source concurrency、未導入の演算子拡張、re-export、動的generic格納、永続object cache等、SPECが明示的に将来事項とする機能は今回の必須完了範囲に含めない。未実装と未設計を区別する。
- 旧計画作成時の成果物はこのファイルだけ。今回のPlanでは IMPLEMENTATION_PLAN.md、STATUS.md、AUDIT_FINDINGS.md を更新し、検証証拠をCurrent evidence prefixへ保存する。製品ソース・SPEC・設定・fixture原本・lockは変更しない。これはPlanの編集範囲であり、Implementationへ持ち越さない。
- AGENTS.mdに従い、性能・割り当てを各段階で確認する。NativeAOTのpublish／テストは実行していない。将来の実行も明示的な指定がある場合に限る。

## 2. 調査結果

### 2.1 旧計画の全体調査範囲と方法

bin/、obj/、toolchain/、生成済みartifacts、依存ライブラリー本体を製品の手書きソースから分離し、次の全体を列挙・横断検索した。コンパイラー入口から各段階の実装・拒否条件を追跡し、対応テストの成功条件・未対応を期待する条件も確認した。全ソリューションの再ビルドと全managedテストで、検索だけでは判断できない接続を検証した。

| 範囲 | 確認対象 |
| --- | --- |
| Kimi | 233 C#ファイル。Lexing、Parser/Koto、Binding、Analysis、Emission、runtimeテンプレート、Project/Solution、CLI、LSP、diagnostics、target情報。プロジェクト等を含む調査入力240ファイル |
| xUnitTest | 82 C#ファイル（Tests配下80ファイルと共通helper2ファイル）。正常系、診断、拒否境界、破損計画、再解析、割り当て、IR/native fixture生成 |
| Benchmark / Playground | 15 / 1 C#ファイル、各プロジェクト。Benchmark登録・測定構成、実験コードと製品経路の分離 |
| backend/windows-x64 | 24入力ファイル。ASM、Cテスト、profile、シンボル供給、PowerShellビルド・検証スクリプト |
| KimiCode | extension.js、package.json、lock。構文確認、起動経路、設定・テストscriptの有無 |
| ルート・配布・例 | Solution、props、global.json、.editorconfig、GitHub workflows、examplesのソース・プロジェクト。旧Design例は参考仕様から除外 |

静的な未使用調査は、参照横断検索と IDE0051/IDE0052 の変更なし解析を併用した。reflection、ソース生成、外部利用まで含む到達可能性の完全証明ではない。字句出現回数だけで削除対象を決めない。

### 2.2 前回の検証記録（今回の実行とは区別）

環境: Windows x64、.NET SDK 10.0.401。既存のローカル依存を使い、restoreや製品公開は行っていない。

| 検証 | 旧計画に保存されていた結果 |
| --- | --- |
| Release全Solution Rebuild | 成功、警告0、エラー0 |
| Debug全Solution Rebuild | 成功、警告0、エラー0 |
| Release全managedテスト | 3,813成功、失敗0、スキップ0 |
| Debug全managedテスト | 3,813成功、失敗0、スキップ0 |
| 通常native runtime/emission（Release） | 68実行成功 |
| 全scalar系fixtureのLLVM検証・O0/O2実行 | 930 fixtureのLLVM検証と1,860回のO0/O2通常native実行が成功 |
| managedコンパイラーCLI統合 | test-cli.ps1成功。NativeCompiler引数なし |
| managedコンパイラーLSP通信 | initialize、document通知、未知method、shutdownの既存script成功 |
| artifactパス・toolchain判定・kernel32生成 | test-artifact-paths.ps1、test-toolchain.ps1、test-kernel32.ps1が成功 |
| IDE0051/IDE0052解析 | verify-no-changesで完了、診断出力なし |
| KimiCode JavaScript構文 | node --check 成功。VS Code上の拡張統合テストは未実行 |
| NativeAOT・新規性能benchmark | 未実行 |

再現コマンド（PowerShell）:

~~~powershell
dotnet build Kimigayo.slnx -c Release --no-restore -t:Rebuild -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore
dotnet build Kimigayo.slnx -c Debug --no-restore -t:Rebuild -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet format analyzers Kimigayo.slnx --verify-no-changes --no-restore --severity info --diagnostics IDE0051 IDE0052
./backend/windows-x64/test-emission.ps1 -Configuration Release
./backend/windows-x64/test-scalars.ps1
./backend/windows-x64/test-cli.ps1 -Configuration Release
./backend/windows-x64/test-lsp.ps1 -CompilerPath Kimi/bin/Release/net10.0/Kimi.dll
./backend/windows-x64/test-artifact-paths.ps1
./backend/windows-x64/test-toolchain.ps1
./backend/windows-x64/test-kernel32.ps1
node --check KimiCode/extension.js
~~~

調査中、追加report引数を付けた最初のテスト起動はexit 5・実行0件となった。上記の標準コマンドで再実行し、全件成功を確認したため、これを製品テストの失敗件数に含めていない。LLVMの最初の実行はsandboxの実行権限制限で停止したが、許可された通常native検証で再実行した。いずれもソース修正で解消した問題ではない。

ログは作業環境の一時ディレクトリ kimi-plan-audit-a4c13b3708824cfe9b2fe8a750d5388e に保存した。nativeの既存レポートは bin/emission-native/Release/verification.json、CLIは bin/cli-tests/ 配下。将来の検証は今回の数値を流用せず再実行する。

警告0は現在の設定下の結果である。.editorconfigはCS1591、CS1998、CS1573等をsilentにしており、「不要コードや未接続処理がない」という証明ではない。

### 2.3 ソースで確認した実装境界

| 証拠ID | 実装・テスト | 確認した事実 |
| --- | --- | --- |
| E01 | [Compilation.cs](Kimi/Compiler/Core/Compilation.cs)、[Project.cs](Kimi/SolutionAndProject/Project.cs) | Prepareに外部Kotonoha読込予定のコメントが残る。BindはfinalのみでMod実行がない。CLIは実際にBinding→startup→ownership→emissionを通る |
| E02 | [LlvmEmitter.cs](Kimi/Compiler/Emission/LlvmEmitter.cs)、[FunctionAbi.cs](Kimi/Compiler/Emission/FunctionAbi.cs) | Application以外、外部module、宣言containerを拒否。anonymous、specialization、generic、明示Origin、default引数等も拒否。ABIはscalar／Unit／owned stringとref/string引数の範囲 |
| E03 | [ScalarTypes.cs](Kimi/Compiler/ScalarTypes.cs)、[FloatingTypes.cs](Kimi/Compiler/FloatingTypes.cs)、[ReferenceTypes.cs](Kimi/Compiler/ReferenceTypes.cs) | 0004/0005でchar・f32/f64・初期128bit subsetを値計画/ABI/既存aggregate/通常nativeまで接続。整数12×12変換、float literal fitting・f32→f64・float同型は実装済み。実行時f64→f32と整数↔float・整数literal→floatはBinding/検証gateに残る（§2.6）。参照の実行形は引き続きref/stringの限定範囲 |
| E04 | [AggregateLayout.cs](Kimi/Compiler/Emission/AggregateLayout.cs)、[BodyLowering.Aggregates.cs](Kimi/Compiler/Emission/BodyLowering.Aggregates.cs)、[AggregateEmissionTest.cs](xUnitTest/Tests/AggregateEmissionTest.cs) | owned tuple／固定配列の構築・全体Copy/Move・破棄を実装。要素射影、集約signature/resultは未対応。実装上限は深さ64・size/count int.MaxValueで明示拒否 |
| E05 | [OwnershipAnalysis.cs](Kimi/Compiler/Analysis/OwnershipAnalysis.cs)、[BodyLowering.References.cs](Kimi/Compiler/Emission/BodyLowering.References.cs) | capture、default評価、依存する借用結果、一般のmember/index等に拒否経路。参照保存・結果が未実装 |
| E06 | [Binding.Origins.cs](Kimi/Compiler/Binding/Binding.Origins.cs)、[SpecReviewTest.cs](xUnitTest/Tests/SpecReviewTest.cs) | Origin名・bound対象は解決するがbound証明は未実装で認証しない。テストもその拒否を期待する |
| E07 | [Binding.Expressions.cs](Kimi/Compiler/Binding/Binding.Expressions.cs)、[Binding.Calls.cs](Kimi/Compiler/Binding/Binding.Calls.cs) | constructor／destructor／explicit specializationの未対応、specialization候補・length slotのPending経路を確認。宣言構文の存在は実装選択や実行の完成ではない |
| E08 | [CoreIntrinsics.cs](Kimi/Compiler/Binding/CoreIntrinsics.cs)、[CoreCatalogTest.cs](xUnitTest/Tests/CoreCatalogTest.cs) | catalog18枠中6宣言がValidated、残り12枠はMissing。Array/Slice/Dictionary/比較・反復等はカタログ名だけ。Option/Resultの宣言があってもgeneric runtimeは未完成 |
| E09 | [Binding.Properties.cs](Kimi/Compiler/Binding/Binding.Properties.cs)、[PropertyBindingTest.cs](xUnitTest/Tests/PropertyBindingTest.cs)、[ContractBindingTest.cs](xUnitTest/Tests/ContractBindingTest.cs) | property／contract／継承の宣言検証・witness等は部分実装。get/set/init、receiver、cleanup、生成まで完成したと扱えない |
| E10 | [LspServer.cs](Kimi/Lsp/LspServer.cs)、[extension.js](KimiCode/extension.js) | document更新からコンパイラー診断への接続がない。PublishDiagnostics呼出はコメント。拡張は隣接Debug DLLとDebugWait=trueを固定使用 |
| E11 | [DefaultCommand.cs](Kimi/Unit/Command/DefaultCommand.cs)、[LspServer.cs](Kimi/Lsp/LspServer.cs)、[Playground/Program.cs](Playground/Program.cs) | DefaultCommand.unitContextは代入のみ。LSPのdumpは生成するがRequestがコメント化され、callbackの出力もコメント。実験コード・不要using等の整理候補あり |
| E12 | [.github/workflows/test.yml](.github/workflows/test.yml)、[publish.yml](.github/workflows/publish.yml) | LinuxでReleaseビルド後、構成未指定のテストを実行。Windows native検証・pull request時の検証・KimiCode統合検証が現workflowにない |
| E13 | [WindowsRuntime.ll.in](Kimi/Compiler/Emission/WindowsRuntime.ll.in)、[backend](backend/windows-x64/README.md) | Windows出力・割当・解放・AbortとASM helperは実在し通常native検証がある。object/Weak/汎用metadata runtime完成の証拠にはならない |

追加の注意:

- STATUSの古いC.3等には、現在は部分実装されているBinding/Origin/aggregateを未実装とする過去の記述がある。直近のC.56も全言語の完成ではない。本計画ではE01–E13と現在のテストを優先した。
- 製品ソースにNotImplementedExceptionを投げる形のstubが見当たらなくても、Unsupported、Pending、Missing、入力gateで未実装機能を表現している。
- Core.writeLineのbodyなし宣言は識別済みのcompiler lowering hookであり、単なる未実装stubではない。abstract/requirement宣言、ソース生成属性を持つGeneratorOption、framework登録型も未使用と即断しない。
- 今回、修正対象として再現したmanagedテスト失敗はない。未対応を正しく拒否するテストが成功していることと、対象機能が使用できることを区別する。

### 2.4 前の未受理Plan試行の保存結果（0001-plan、今回の実行ではない）

- 開始HEADは冒頭のとおり。`git status --short`、staged/unstaged diffは空。旧計画初出の `671a9b3` から現HEADまで、Kimi / xUnitTest / backend / KimiCode / Benchmark / Playground / examples / .github / SPEC / global.json / Directory.Build.props の差分も空。旧調査を作り直さずE01–E13のgate・登録・テストと仕様境界を優先した。
- AUDIT_FINDINGS.mdは開始時に存在せず、前回Completion Auditの入力もない。今回は初回台帳を作成した。未読の旧監査を読了したとは扱わない。
- 前試行のtracked入力hashはKimi 239、xUnitTest 83、backend 27、KimiCode 3、Benchmark 17、Playground 2、examples 64、workflows 2ファイル等を記録した。前試行の外部文書採用主張は撤回し、今回は当該文書とadopted-inputs.jsonを読んでいない。製品ソース検証の再利用条件は§2.5で別途確認した。
- 現行値計画はbool/10整数型が対象。WindowsLoweringにchar/float/i128の配置recordがあっても、ScalarTypes.Supports、OwnershipAnalysis.RecordValue、BodyLowering.Values、FunctionAbi、matchのliteral gateが実行を制限する。Coreの6 validated/18 catalogもCoreCatalogTestと生成宣言の検査経路で確認し、12 Missingを実装済みに数えない。disabled-test-scanは明示skip/explicit属性に一致0件だが、未対応を期待するテストは残る。

この前試行のログはすべて次のprefix配下: `.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0001-plan/`（以下P）。ソース一覧・SHA-256は `P/source-inputs.json`、実行前状態は `P/initial-status.txt`、HEADは `P/head.txt`。

| 前試行のコマンド・構成 | 保存されている結果 | ログ/限界 |
| --- | --- | --- |
| `dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal` | exit 0、警告0、エラー0 | P/build-debug.log |
| `dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore` | exit 0、3,813成功、失敗0、skip0 | P/test-debug.log。上記build後のDLL |
| `dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal` | exit 0、警告0、エラー0 | P/build-release.log |
| `dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore` | exit 0、3,813成功、失敗0、skip0 | P/test-release.log。Debugと直列 |
| `dotnet format analyzers Kimigayo.slnx --verify-no-changes --no-restore --severity info --diagnostics IDE0051 IDE0052` | exit 0、出力診断0 | P/analyzers.log / analyzers.result.txt。登録・reflectionの到達可能性全証明ではない |
| `dotnet Kimi/bin/Release/net10.0/Kimi.dll emit-llvm <Pのprobe.kimiproj>` ×3 | i32対照はexit 0、char/f64は各exit 1 | P/emission-probes.json、各probe/emit.log。合法なchar/f64の既知実装不足を再現。値flow計画不一致。nativeは実行していない |
| repository helperによる `C:/App/llvm/bin` のLLVM identity事前確認 | 8 toolのversion/hash条件成立、llvm-libも存在 | P/llvm-preflight.json。LLVM 22.1.8。checkoutのtoolchain/backendは未設置で、native成功の証拠ではない |
| `node --check KimiCode/extension.js` | 起動不能、process exitなし、0件 | nodeがPATHになく既定Program Filesパスも不在。P/node-check.result.json。拡張の製品失敗とは区別 |
| `dotnet build-server shutdown`（検証後の後処理） | exit 1。MSBuild停止成功、compiler server停止処理はNuGet.Configのアクセス拒否 | P/build-server-shutdown.log / cleanup.json。CIM照会もアクセス拒否のためGet-Processで確認し、dotnet/MSBuild/VBCSCompilerは各0プロセス（not-found）。全exec sessionは完了。製品テストの失敗には数えない |

旧§2.2のTempログとnative reportは今回の環境に存在しなかった。BASE-01–06/08は今回のmanaged/静的証拠で更新し、BASE-07だけを検証待ちへ戻した。過去の68/930/1,860件を今回の成功へ流用しない。NativeAOT、通常native suite、VS Code host統合、新規benchmark、CI実workflow、restore/pack/publishは今回未実行。検証で追跡対象fixture/lock/設定は再生成していない。

### 2.5 今回の再開照合と新規再現（0002-plan）

- 2026-09-14 00:04:29 UTC開始、上限90分、hard deadline 01:34:13 UTC、最終整理の予約開始01:29:13 UTC。開始時は計画/STATUSおよびautomation 5ファイルの未コミット変更、未追跡台帳が存在。automation差分は保護し、runner状態を変更/再起動していない。
- 前試行結果0001-plan.result.jsonは存在するがrunnerに未受理。完成/承認の証拠ではない。前回Completion Auditの成果物は今回の入力にない。保存済み計画/台帳の内容を再照合し、doc由来の要件/依存を§13で修正した。既存のMarkdownリンク行をタスクと誤認したrunner修正は開始前の変更であり今回の修正ではない。現行計画では当該参照表自体を本文対応表へ置換する。
- 前試行のsource-inputs.jsonの445入力を現在のSHA-256と照合し差異0。Kimi/xUnitTest/backend/KimiCode/Benchmark/Playground/examples/workflows/SPEC/build設定を含む。検証DLL 4件も保存hashと一致。各build/testのlogとexit_code=0記録を確認し、§2.4のDebug/Release各3,813成功/失敗0/skip0・build警告0/エラー0を過去の証拠として再利用。今回全suite/buildを再実行したとは主張しない。ソース/設定/入力が変わった次のImplementationでは改めてbuildと必要テストを行う。
- 今回の読み直しはSPECの§18/19/20/21.3.4/B.7.3、冒頭/文法境界/§15.7、次の数値契約と、それに関係するCompilation/Project/Parser/ScalarTypes/Ownership/Emission/tests/CLI/LSP/拡張/CIを優先。初回全体調査の不変領域を作り直していない。AGENTS.mdはrootの実在ファイルと上位候補の有無を確認し、除外指定付き列挙で追加の適用ファイルなし。doc/とDesign/の旧例は根拠にしていない。
- 新規E14/AF-0010: SPEC §19.2/20.4と反対にCompilation.csのVariables/PrepareがOrdinalIgnoreCaseを使用。CompilationSpecificationTest.csのDirectiveConstantNamesIgnoreCaseIndependentlyOfCulture / SettingNamesDifferingOnlyInCaseFailPreparationが誤った挙動を期待しており、全suite成功で仕様適合を認定できない。CompileTimeConditionEvaluatorはCompilation.TryResolveValueへ接続しており未使用コードではない。

今回の証拠prefix **Q**: `.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0002-plan/`。**P**は§2.4の前試行prefixのまま区別する。

| 今回の検証 | 結果・限界 | 証拠 |
| --- | --- | --- |
| `git status --short` / `git diff --binary` / HEAD、SHA-256照合 | 開始時変更を保存。前試行445入力・DLL 4件は差異0 | Q/initial-status.txt、initial.diff、head.txt、initial-inputs.json、prior-input-comparison.json、prior-binary-comparison.json |
| `pwsh -NoProfile -File Q/probe-directive.ps1` | harness exit 0、Release CLI emit-llvmを5回実行。SPEC一致1、不一致4。native実行0。製品成功5件という意味ではない | Q/directive-probes.json、各入力/emit.log。コマンド・期待/実exit・source/project/compiler SHA-256を保存 |
| 上記の小文字windows対照 | 期待exit 0、実exit 0 | Q/lowercase-control/ |
| 未設定WINDOWS、true or WINDOWS | 期待は各exit 1のUnknownCompileTimeName診断、実際は各exit 0でIR生成 | Q/uppercase-unknown/、short-circuit-unknown/ |
| Feature/FEATUREの別設定、WINDOWSの独立設定 | 期待は各exit 0、実際は各exit 1のInvalidCompileTimeSetting | Q/distinct-settings/、uppercase-setting/ |
| `pwsh -NoProfile -File Q/validate-plan.ps1` | exit 0。104 ID、HEADの91 ID保持、必須103 IDの最終依存閉包、必須未完了96・任意1、指摘11行/未解消必須9件、引継ぎ1件。実runnerの読取専用parser/Assert-DurableResultも成功 | Q/plan-validation.json |
| 開始時SHA-256との最終照合、`git diff --check` | 463入力中の変更は指定3文書のみ、automation既存変更を保持。空白検査exit 0 | Q/scope-check.json、diff-check.log、diff-check.result.txt |

NativeAOT、通常native、VS Code host、node、新規benchmark、CI workflow、restore/pack/publishは今回は未実行。前試行で未設置だったtoolchain/backend、node環境を準備済みと扱わない。入力gateはchar/floatの既知未実装のまま（AF-0004）。通常native証拠不足AF-0001はplannedを維持する。今回の製品不一致はFRONT-02へ計画化し、製品ソース/テストの修正はImplementationへ渡す。

補助検査の初回は配列join比較式の括弧不足によりexit 1となった（Q/validation-first-attempt.json）。同じID集合を誤って不一致とした検査scriptを修正し、上記exit 0を得た。runner/製品の失敗ではなく、製品検証を再実行した結果でもない。

### 2.6 前回の未受理Plan試行（0006-plan、cycle 2）

証拠 **T** は `.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0006-plan/`、前Implementationの **S** は同runの`0005-implementation/`、その前の **E** は`0004-implementation/`。今回の編集は計画・STATUS・台帳の3文書のみ。SPEC本文、現行要約/索引/関連節、git status/diff、AGENTS、verification-guide、前のvalidated resultを確認した。Completion Auditの新しい入力はなく、Plan Audit 0003の指摘とImplementation 0004/0005の採否を引き継ぐ。

- `T/capture-inputs.ps1`（exit 0）の`initial-comparison.json`: 前枠最終470入力のうちsource/config等466入力と計画/STATUS/台帳が一致。5,731 fixture入力とRelease compiler/test DLL 2件のSHA-256差異0。未追跡`autoimpl/Design.md`だけ前枠後に変化しており、内容を仕様として読まず保護。今回の開始/終了snapshotはT/initial-inputs.json・initial.diff、final-inputs.json・final.diff。
- **最終照合時の環境変化:** 02:55:19Zの`capture-inputs.ps1 -Final`はexit 1。開始時に照合できたroot `bin/`（fixture5,731入力）がなくなり、autoimpl/Design.mdも欠落した。原因/実行主体は未確認で、このPlanには削除/移動操作がない。source/config等466入力とRelease DLL2件は引き続き一致。T/final-observed-change.json・generated-path-observations.jsonへ欠落と実際の終了コードを保存した。これは製品失敗ではなく生成物の利用可能性の変化であり、開始時に検証済みのSの実行/入力証拠は残っている。次枠は現sourceのmanaged build/testで空の生成領域を再生成し、旧manifestとのhash比較から再開する。現在もfixture一致と報告せず、再生成前のnative起動/成功認定を行わない。
- **過去の実行を入力一致で再利用:** S/float-convert-checks.jsonと各logのDebug/Release `dotnet build Kimigayo.slnx -c <構成> --no-restore -v:minimal`は各exit 0、警告0/エラー0。`dotnet test --project xUnitTest/xUnitTest.csproj -c <構成> --no-build --no-restore`は各4,123成功・失敗0・skip0。S/scalars-result.jsonの全1,009 fixture/2,018 O0/O2、wide-native-result.jsonの追加118/236、float-convert-checks.jsonの追加18/36はいずれもexit 0。最終1,145 scalar/2,290実行をS/final-native-coverage.jsonで対応付け、現入力のhash一致を今回確認した。過去のruntime68、CLI8 scenario群、LSP基本通信の成功も限定範囲で維持する。今回build/test/nativeを再実行したとは扱わない。
- **完了主張の照合:** FRONT-02はCompilation/ProjectFileのOrdinal・Dictionary集約前の検査とCompilationSpecificationTest、NUM-03/02/01はScalarTypes/FloatingTypes・Binding/Ownership/BodyLowering/WriterとChar/Float/WideInteger/ConversionEmissionTest、BASE-07は上記ログ/fixtureへ対応する。既存[x]12行を維持し、新しい製品完了は追加しない。BASE行の旧suite件数は限定した基盤記録であり現在の全仕様の完成ではない。
- **残実装の接続位置:** Binding.Conversions.csの整数literal→float拒否、f32/同型だけのFloating許可、BodyLowering.Values.ValidScalarConversion、BodyLowering.Conversions.PlanConversionのfpext固定、BodyLowering.Graph.ClassifyCheckのfloat結果一律None、LlvmModuleWriter.Conversionsのicmp専用境界、WindowsLowering.Abortのinteger専用理由を確認。FloatConversionEmissionTest.RemainingConversionsAreNotMistakenForWideningが合法な未実装4例と明示Semantics未対応を拒否期待にしている。新しい同問題の指摘は重複登録せず、NUM-04の分割へ対応付ける。
- **既存製品の必須範囲:** LSP open/changeのコメント化された診断配信、拡張のDebug DLL/DebugWait固定、CIの構成/対象未指定、Compilationの外部load/Mod予定とLlvmEmitterのmodule/Library gateは存続。AF-0005/0006/0008をplanned、AF-0002/0007/0011の公開契約不足をblockedに維持する。CLI/LSP基本通信の成功で診断配信/拡張host/CI・配布全体を完成扱いしない。
- **計画変更:** NUM-04を維持し、NUM-04-FLOATとNUM-04-INTEGERを必須の独立実装単位として新設。一般Type完成を待たずplain数値変換を進める。未対応の一般同型取得・明示Semantics/略記はNUM-04に残す。合計106 ID、[x]12、必須未完了93、任意未実行1。構造/必須依存閉包/台帳全件はT/validate-plan.ps1・plan-validation.jsonで検査する。

公開API/言語仕様は追加せず、doc/だけの旧4 IDは§13.2の対象外履歴を維持。NativeAOT、CI実workflow、VS Code host、pack/外部公開は今回未実行。Planのdeadlineは2026-09-14 04:08:05Z、04:03:05Zから保存/整理用。次段階は自身のdeadlineと実行時間上限を読み直す。

### 2.7 保護対象変更エラーからの再開（0007-plan、cycle 2）

証拠 **U** は `.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0007-plan/`。本段階の90分上限・UTC deadline `2026-09-14T04:48:25.1781003Z` を確認し、04:43:25Z以後を保存/引継ぎ用とした。現在の3文書、AGENTS、SPEC本文の索引と関連節、verification-guide、git status/diff、validated result 0005と未受理result 0006を確認した。新たなCompletion Audit入力はない。

- **再開時の保護境界:** runnerは0006-planを`autoimpl/Design.md`の保護対象変更で拒否した。前試行本文の「削除/移動操作なし」はその時点の報告であり、原因・実行主体の確定証拠ではない。U/initial-comparison.jsonでは同pathは開始時から欠落、0006最終snapshot以後の追加は未追跡`autoimpl2/`の14入力だけ。これらはpath/hashのみ照合し、内容を仕様・実行指示として読まず、復元/移動/編集/起動しない。root `bin/`の既知fixture5,731入力も開始時から欠落している。
- **既存作業の一致:** `pwsh -NoProfile -File U/capture.ps1`と`U/review.ps1`はexit 0。前Implementation 0005のsource/config等466入力とRelease compiler/test DLL2件のSHA-256差異0。指定3文書も0006最終保存内容と一致し、分割済み106 IDをそのまま再利用できる。Uの照合成功は現在のfixture/native成功を意味しない。
- **証拠と完了状態:** S/float-convert-checks.jsonと実logの両構成build各exit 0・警告0/エラー0、managed各4,123成功・失敗0・skip0を過去検証として確認。S/final-native-coverage.jsonの5,731対応行は全件Matches=trueで、当時の1,145 scalar/2,290 O0/O2等への対応を保存している。U/review-summary.jsonに元コマンド・UTC・終了コード・log/hashを記録した。現source一致を根拠に既存[x]12行とAF-0001等の解消済み状態を維持するが、今回は製品build/test/nativeを実行せず新しい完了を追加しない。
- **次範囲の実ソース照合:** SPEC §2.6/13.5.1–4/17.3/21.5.3–4/A.6/A.14と、Binding.Conversions、BodyLoweringのConversion/Values/Graph、LlvmModuleWriter.Conversions、WindowsLowering.Abort、FloatConversionEmissionTestを再確認。合法なf64→f32・整数↔float・整数literal→floatは依然Unsupported期待であり、NUM-04-FLOAT/INTEGERの必須条件を維持する。fpext固定・float結果無検査・整数専用比較/理由を型付き変換とチェックへ接続する。一般同型取得・明示Semantics等はNUM-04の未完了範囲に残す。
- **必須製品と外部待ち:** 現CLI/LSP基本通信は過去の限定証拠。LSP open/change診断、拡張host、CI構成/対象・Windows検証、external module/Mod/Libraryの未接続はU/product-gates.txtへ対応付け、AF-0005/0006/0008のplannedを維持。AF-0002/0007/0011は§13.3の契約別質問/影響/解除条件を維持し、数値2枠や内部snapshot作業を止めない。docだけの旧4 IDの対象外履歴も維持する。
- **次2枠と検証回復:** 第1枠は§14.6のfixture再生成・照合からNUM-04-FLOAT、第2枠は回帰/残検証を優先した後にNUM-04-INTEGER。欠落している出力には退避/削除操作は不要。再生成前に現状を確認し、出力が再出現していた場合だけ安全な生成領域の退避手順を適用する。旧manifestと異なる入力へ過去のnative成功を転用しない。§14.8の時間見積りと独立作業への切替を維持する。

`U/validate-plan.ps1`はexit 0。106一意ID、ACCEPT-03の必須105 ID閉包、12指摘の表行・完了状態の維持、STATUSの単一引継ぎ、git diff --checkを確認し、不整合0。03:29:25Zの`U/capture.ps1 -Final`はexit 1で、本実行が編集していないautoimpl2/lib/stages.ps1の変更とautoimpl2/codex-loop.ps1・test-codex-loop.ps1の追加を検出した。U/first-final-observed-changes.json・external-change-observations.jsonへpath/hash/時刻を保存。製品source/config466入力とRelease DLL2件は不変だが、全体の保存検査を成功に読み替えない。実行主体は未特定で、追加物を実行・上書きせず、最終snapshotをU/final-comparison.jsonへ残す。製品・設定・保護対象への修正、fixture再生成、NativeAOT、CI実workflow、VS Code host、pack/公開、runner再起動は本Planで行わない。Plan Auditへ提出する準備であり、計画承認や完成認定ではない。

## 3. 計画の読み方・共通完了条件

IDは一意で、表の上から依存関係順に並べる。同一段階でも先行IDを参照する。依存欄は直接の前提であり、その依存先の前提も引き継ぐ。独立項目は順序を入れ替えてよい。

自動実装では状態・IDの表形式を維持し、任意・条件付き項目だけOPTIONAL-接頭辞を使う。runnerは必須[ ]が残る完成報告や存在しないtask_idsを拒否する。計画・監査は全体を毎回作り直さず、着手済み作業、前回の指摘、次の2実装枠の依存範囲を優先する。現在の操作はSTATUS先頭の引継ぎに保存する。検証起動の注意は[verification-guide](automation/verification-guide.md)を参照する。

[x] は記載した限定範囲を実ソースと対応する検証証拠で確認済み。[ ] は部分実装・検証待ちを含む未完了。一つの関数ではなく、独立した受け入れ条件を持つ機能単位で分割する。過去のnative件数だけでは今回の入力一致を認証しない（BASE-07、AF-0001）。

各未完了項目は、個別条件に加えて次を満たすこと:

1. 対象の正常系を通し、不正使用を所定位置で診断する。合法機能のUnsupported解除だけで完了としない。
2. 関連する未使用body・到達不能枝・再解析・失敗後再実行でも検証が成立する。
3. runtimeを変更する場合はIR verifierとO0/O2実行で値、stdout/stderr、終了状態、評価・cleanup順を比較する。借用・所有権は失効、部分初期化、二重破棄、Abort／非終了を含める。
4. 既存の近傍テストと必要な統合テストを通し、設定上の警告・失敗を増やさない。コード量、割り当て、ピークメモリに関係する変更は代表測定を行う。
5. ImplementationはSPEC/STATUSを必要に応じ更新する。PlanはSTATUSの現在の引継ぎを更新する。公開仕様の不足は根拠付きの決定を待ち、doc由来の本文統合を次段階へ指示しない。

## 4. 既存の確認済み基盤

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [x] BASE-01 | .NET 10の4プロジェクトをDebug/Releaseでビルド | 両構成で警告0・エラー0。対象ソースhashを保存 | なし | §2.4の前試行のbuild-debug.log / build-release.log（各exit 0） |
| [x] BASE-02 | 現在のmanaged回帰suiteとfixture生成 | 両構成3,813成功、失敗0・skip0。native成功とは区別 | BASE-01 | §2.4の前試行のtest-debug.log / test-release.log（各exit 0） |
| [x] BASE-03 | 現行のUTF-8/source identity、token、indent、directive選択、Koto round-trip基盤 | 既存Lexing/Parser/serializationテストが成立。全仕様の意味解析までを含めない | BASE-02 | UnicodeIdentifier、SourceEncoding、DirectiveConditionValidation、ParserRegression、KotonohaSerialization各テスト |
| [x] BASE-04 | 現行のname/type/call/contract/property/startupの限定Binding | 既存の正常・不正・再Bindケースが成立 | BASE-03 | Binding、TypeBinding、ContractBinding、PropertyBinding、StartupBinding各テスト |
| [x] BASE-05 | bool・10整数型・Unit、通常直接call、制御フロー、owned string・比較・guard・一時ref/string引数 | 対応範囲のIR、借用・cleanup計画と実行fixtureが生成できる | BASE-04 | Scalar/Integer/Function/Result/Match/Guard/String*/ReferenceEmissionテスト。E02–E05の制限を維持 |
| [x] BASE-06 | owned tuple/固定配列の全体構築・Copy/Move・破棄 | 現行35 aggregateテストが成立。projection、ABI、選択結果は含めない | BASE-05 | AggregateEmissionTestと生成fixture |
| [x] BASE-07 | 既存Windows runtime/helper、CLI build/run、LSP基本通信の証拠を再確立 | 対象source・構成・生成fixtureとhashが一致する通常native、CLI、LSPのログを保存。0005で限定した既存基盤の検証を完了 | BASE-05 | §2.6、S/scalars-result.json・final-native-coverage.json・final-cli-result.json・final-lsp.json。AF-0001 resolved。LSP診断/拡張host/CI全体は各未完了IDへ残す |
| [x] BASE-08 | 警告・未使用・宣言だけの機能の横断調査 | E01–E13を現行コード・テストで照合し、解析の限界を記録 | BASE-02 | §2.4のsource-inputs.json、implementation-gates.txt、disabled-test-scan.txt、IDE0051/0052 verify。intrinsic/登録経路は保持 |

## 5. 仕様境界と開発基盤

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] SPEC-01 | SPEC本文・Appendix A/Fと実装段階の要件台帳を維持する | 必須規則を機能ID・実装箇所・正常系/不正使用へ対応付ける。各Implementationで対象の節を先に具体化し、最後に全体の残差を閉じる | BASE-08 | §12–14を領域単位で展開。全体台帳の完成を独立実装の前提にしない |
| [ ] SPEC-COMPOSE-01 | 本文にある非置換root・Entry/Std接続方針の宣言/参照契約を確定 | SPEC冒頭7行・§13.8の確定事項を保ち、本文にないEntry宣言/参照文法・型/適合・アクセス規則は質問と正負例への決定を待つ。doc本文統合は行わない | BASE-04 | §13.1/13.3、AF-0002。本文に保存された決定後にFRONT/COMPOSEのsource受入例を確定 |
| [ ] SPEC-COMPOSE-02 | Provider選択・最終接続の公開設定契約を確定 | SPEC冒頭7行/§21.3.4.2のmodule identity分離・変更時再検証を保持。Provider指定/既定選択/欠落・競合・適合規則は本文の不足として決定待ち | BASE-04 | §13.3、AF-0002。Entry文法とは別に設定の正常/不正例と本文を確定。内部の失効検証は先行可能 |
| [ ] SPEC-TEST-01 | 本文にある#Testと製品/test所属・依存方向の契約を確定 | SPEC冒頭9行の一方向依存・共通検証/cleanupを保持。#Testの付与先、署名、発見/選択、通常modeの検査範囲は本文の不足として決定を待つ | BASE-03 | §13.1/13.3、AF-0002。所属と非選択本体の正常/不正例、依存方向を本文へ確定してからsource接続 |
| [ ] SPEC-VERIFY-01 | $expect/$requireの型・効果・失敗契約を確定 | 本文冒頭9行に操作名/共通検証はあるが、引数/結果・評価順・メッセージ・失敗後制御がない。§13.8の現行$abort境界との整合を本文で確定 | BASE-04 | §13.3、AF-0002。操作別の成功/失敗/cleanup/不正使用例の決定を待ち、通常require文と混同しない |
| [ ] SPEC-DEPS-01 | 依存の外部設定・版解決契約の不足を確定 | §18.1/20.1–2の直接参照名・定義側環境・複数版を保つ。既存KotonohaArrayはName/Versionだけで取得先を規定しない。版/参照名/取得先とgraph診断の公開設定は決定待ち | BASE-04 | AF-0011、§13.3。決定と正負設定例を本文へ保存。Dependencies/lock/単一版/DAG/restoreを推測せず、DEP-01の内部snapshot検証は先行 |
| [ ] SPEC-ARTIFACT-01 | portable source/interface交換契約の不足を確定 | §18.3の保存情報と§21.3.4の検証義務は確定。encoding/required fields/validation/compatibilityは本文が別仕様へ委ねるため、公開交換形式の決定を待つ | BASE-04 | AF-0011、§13.3。source/config再構築・破損/非互換拒否の本文と正負例を保存。serialized Koto、schema 1、SourceIdを推測しない |
| [ ] SPEC-MOD-01 | Mod hostの実装設計を具体化 | §20.7.7が実装設計へ委ねるquery/marker登録、assembly互換、project設定、取消し上限を具体化。ソース言語の追加仕様を創作しない | BASE-04 | syntax/semantics snapshot、例外/timeout、再生成の正負テストが書ける設計を保存 |
| [ ] SPEC-TEST-02 | 本文のprocess隔離・有限報告/回収をrunner設計へ対応 | SPEC冒頭9行の境界とB.7.3本文の製品/test再利用・予算分離を満たすtransport・上限・終了判定を実装設計として具体化。公開test操作はSPEC-TEST-01/SPEC-VERIFY-01で確定 | BASE-04 | 0件/報告欠落/破損/過大入力/期限/取消しの受入表を保存。IssueIdや公開一時directory APIを既定要件にしない |
| [ ] SPEC-API-01 | exchangeの公開APIだけの仕様決定を本文へ統合 | SPEC §15.7と本計画§13.3の質問に根拠付きの決定を得て、公開綴り・名前解決・型・取得/効果・使用位置を確定 | BASE-04 | 決定後に正常/不正例をSPECへ保存。未回答はblockedのままOWN-02のsource接続だけ待つ |
| [ ] SPEC-02 | 本文の契約別仕様不足・実装設計の残差を総括 | 未決の公開文法/設定と実装者が決める内部形式を分離し、確定した必須規則を弱めず各分割IDを閉じる。doc参照/本文統合タスクは対象外 | SPEC-COMPOSE-01, SPEC-COMPOSE-02, SPEC-TEST-01, SPEC-VERIFY-01, SPEC-DEPS-01, SPEC-ARTIFACT-01, SPEC-MOD-01, SPEC-TEST-02, SPEC-API-01 | §13の本文根拠・質問/解除条件と除外履歴を照合。個別実装を総括IDの完了待ちにしない |
| [ ] MAINT-01 | E11の未使用・未接続debug経路と実験コードを整理する | unitContext、dump、不要async/usingを削除・接続・明示隔離のいずれかで処理。generator/DI登録経路を壊さない | BASE-08 | 参照・生成コード確認、IDE解析、全build、CLI/LSPの起動終了と割り当て比較 |
| [ ] MAINT-02 | CIで構成・対象・結果を一致させる | PR検証、Debug/Releaseの明示、Windows通常native検証、0件検知、ログ保存をtest/publish両workflowへ接続 | BASE-07 | cleanローカル再現とworkflow検証。Linux managedとWindows nativeを区別。実workflow未実行は記録し完了扱いしない。NativeAOT・外部公開は実行しない |

## 6. フロントエンド・意味解析・所有権

### 6.1 型と宣言

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] FRONT-01 | SPECの確定構文とKoto/source round-tripの残差を解消 | 規定構文のsource span、child ownership、回復、再serializationを保つ。未決構文は対応SPEC分割IDの本文確定まで作らない | BASE-03 | Appendix F対照とparse正負テスト。FRONT-02の条件名修正は独立。SPEC-01全体の完成を着手前提にしない |
| [x] FRONT-02 | 条件環境のNameをSPECどおり大文字小文字を区別して解決 | §2.5/19.2–3/20.4に従いFeature/FEATURE、windows/WINDOWSを区別し、完全一致の重複/組込み衝突だけ拒否。誤った既存テスト期待とmetadata/再Prepareを同期 | BASE-03 | §14.1、AF-0010。QのCLI 5入力とRの7入力（完全一致重複の誤受理を含む）を回帰検証。culture/NFC/短絡/非選択arm/設定serialization/失敗後再Prepare、両構成managedとemit-llvm正負検証 |
| [ ] TYPE-01 | 完全Type・Semantics層・型役割・名目identityを完成 | ref/uniq/object/unsafeの入れ子、Tuple/enum/配列、関連型・generic slotの形成と拒否がSPEC §§3/8/9に一致 | FRONT-01, BASE-04 | TypeBinding、NestedTypeParse、ConstraintBindingを拡張。型役割のUnknownを成功扱いしない |
| [ ] TYPE-02 | 定数式・固定配列長・要素型推論とlength slotを完成 | 正規化・ValidLength・isize上限・宣言時証明と通常演算Abortを区別。length引数の候補Pendingを適切に解決 | TYPE-01 | §4.2–4.4の定数参照、N=0、負値、中間overflow、同型式、推論の正負テスト |
| [ ] BIND-01 | 複数source・group/struct断片・宣言containerの最終Bindingを完成 | 断片の契約、Name Reachability、可視性、protected receiver、重複・追加時の検証が列挙順に依存しない | TYPE-01 | Binding/InheritedReceiverBinding/IdentifierIdentity、複数source順序変更・再Bind |
| [ ] BIND-02 | overload・引数対応・期待結果・defaultの意味計画を完成 | 再選択なしにgeneric/length/Originを推論し、receiver・named/default引数の評価順と取得を固定 | BIND-01, TYPE-02 | candidate順序、曖昧さ、default副作用、引数失敗、unsafe境界のcallテスト |
| [ ] BIND-03 | static Contract・associated Type・conditional conformance・witnessを完成 | 宣言・継承・操作の適合証明を保持し、未解決の能力をruntimeへ先送りしない | BIND-02 | Contract/ConditionalConformance/ConditionalMember/ConstraintBindingを定義変更・反証込みで拡張 |

### 6.2 借用と値の責任

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] ORIGIN-01 | 抽象Originのbound・順序・交差・variance・代入証明 | E06の正当なboundを認証し、不正なbound・寿命延長を拒否。型identityと生成用Origin消去を混同しない | TYPE-01 | SpecReviewの未対応期待を正負の証明テストへ拡張、再Bind・相互制約・関連型 |
| [ ] BORROW-01 | 論理Place・storage anchor・Loanの一般データフロー | CFG合流、loop、参照保存、最後の利用、destructor観測を含め競合と寿命を判定できる | ORIGIN-01, BIND-03 | §15のdangling、alias、合流、再入、到達不能・部分状態の正負解析 |
| [ ] BORROW-02 | ref/Vのlocal保存・parameter転送・結果返却を実行へ接続 | string限定を外し、即時referentを指す1 pointerと返却依存を保持。参照破棄でreferentを破棄しない | BORROW-01, BASE-06 | ReferenceEmission、参照local/return/集約内保存、zero-size、結果Loan延長 |
| [ ] BORROW-03 | uniq/Vと子Reborrow・親権限制限を実行へ接続 | 排他的書換え、shared/exclusive再借用、親の停止・再開、slotとpayloadの権限を区別 | BORROW-02 | 重なる/重ならないPlace、二重借用、再借用後の親利用、戻り値・loop |
| [ ] BORROW-04 | 関数全体の効果要約とcall中Loan保護 | receiver/引数/capture/static/default/cleanup/間接callの効果を照合し、不明な競合を保守的に拒否 | BORROW-03, BIND-02 | §15.6.4、同一ref二重引数、uniq独立経路、static再入、default/cleanup失敗 |
| [ ] BORROW-05 | 一般の一時値借用・SharedReadResult・ABI属性の共通証明 | §10.2の適応を全対象で行い、型・取得・Loan・cleanup・属性を同じ証明から導出 | BORROW-04 | 一時aggregate/owned objectの引数、短絡、guard、依存結果、zero-size/未知alignment。O0/O2属性検証 |
| [ ] OWN-01 | Field/Tuple/固定配列/CaseのMove Pathと部分初期化を完成 | 完全値と部分値、静的射影、再初期化、動的index制限を区別。全体取得で欠損部分を隠さない | BORROW-01, TYPE-02, BASE-06 | Ownership/EnumOwnership/MatchOwnershipを部分Move、合流、loop、constructor途中で検証 |
| [ ] OWN-02 | storage borrowと初期化を維持するexchangeを実装 | §15.7の確定済み意味を内部計画で検証し、公開綴り/名前解決はSPEC-API-01で確定した契約にのみ接続。未初期化/部分状態を露出しない | OWN-01, BORROW-03, SPEC-API-01 | self/別slot、引数失敗・targetの後続read/Move、非重複証明。内部計画だけの検証をsource API完成としない |
| [ ] OWN-03 | user deinitを含む完全/部分aggregateのcleanup計画 | 論理逆順、base層、defer、parameter、結果確保の順を守り、未完成層のdeinitを呼ばない | OWN-01, BORROW-04 | §16、条件付き破棄、部分構築、早期return、Abort/非終了、破損計画拒否 |

### 6.3 制御フローとgenericの定義側

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] FLOW-01 | 全対応型の結果・require・非終了・未実行経路の共通検証 | 構造上の完了とruntime到達を分け、cleanupが返らない経路で結果を捏造しない | OWN-03, BIND-02 | Appendix A.15、選択/label/yield/外向きtransfer、未使用body・Never後の診断 |
| [ ] FAIL-01 | root function $abortとOption/Resultを使う明示制御フローを接続 | §17の明示Abortとrequire/return/matchの値確保・cleanup順を保つ。専用の暗黙unwrap/伝播構文は導入しない | FLOW-01 | $abort、require失敗、Never、不正な結果型・位置。Option/Resultのenum実行はCORE-01で検証 |
| [ ] GEN-01 | generic bodyの普遍検証と正当なDeferred Obligation | §8.10の全許容代入で型・操作・効果・Loanが合法。好都合な使用例やspecializationで定義エラーを救済しない | BIND-03, TYPE-02, BORROW-04, FLOW-01 | 未使用generic、Copy/Move両分岐、body-only型形成、公開条件と表現失敗の区別 |
| [ ] GEN-02 | 明示full specializationの閉集合と静的実装選択 | 定義側で全候補を検証し、Origin消去/LengthKey、継承契約、曖昧さ、参照・共有callerからの選択を固定 | GEN-01 | §8.8、未使用specialization、重複key、追加削除、通常bodyへ不正fallbackしないテスト |

## 7. 表現・生成・runtime

### 7.1 数値と共通配置

この段階のNUM項目は、意味解析の拡張全体を待つ必要はない。記載したBASE依存が満たされれば独立して実装できる。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [x] NUM-01 | 初期Windows profileのi128/u128実行範囲を実装 | 格納・取得・直接call・比較・bit/shift・整数変換・checked add/sub/neg/inc/dec/mulを接続。除算/剰余とfloat相互変換は§21.5.3通り最適化前に拒否 | BASE-05 | 128bit端点・overflow・shift範囲、未使用body/定数偽枝の禁止演算。O0/O2の実objectで乗算展開と未供給symbol不在を確認 |
| [x] NUM-02 | f32/f64の値計画・演算・比較・直接callを実装 | 単一rounding済みliteral、SSA/格納/取得、算術/比較、parameter/result、既存の選択結果・aggregate payloadへ接続。NaN/±0/subnormalと§21.5.4のFP境界を維持 | BASE-05 | §14.3の正負表。NumberLiteral/Scalar/Function/Result/Comparisonのmanagedと通常native O0/O2。数値間変換全体はNUM-04 |
| [x] NUM-03 | charの値計画・格納・比較・直接call・制御フローを実装 | Unicode scalarを型付き計画から実行へ接続。local/Copy/置換、関数/選択結果、literal match/guard、既存tuple/固定配列payloadでも意味を保持 | BASE-05, BASE-06 | §14.2の境界/不正値/演算禁止/破損計画テスト、CharLiteralParseと近傍回帰、char fixtureのLLVM verifier・O0/O2 |
| [ ] NUM-04-FLOAT | plain Type指定のf32/f64相互変換・同型・float literal fittingを完成 | 既実装の幅拡張/同型/直接fittingを維持し、実行時f64→f32の有限値→infinityをAbort。NaN/±infinity/符号0/subnormal・各段のrounding/失敗順を保持 | NUM-02 | §14.6/14.8。FloatConversionEmissionTestの未対応期待を正負/境界/nativeへ置換。破損計画・元source診断・LLVM verifier/O0/O2・割り当て検証 |
| [ ] NUM-04-INTEGER | plain Type指定のi8–u64/isize/usize↔f32/f64と整数literal→floatを実装 | §13.5.4/21.5.3の全許可40方向、整数literalのexact値から直接rounding、ordered範囲成功後のfptosi/fptoui。typed i128/u128↔floatは最適化前に拒否 | NUM-01, NUM-04-FLOAT | §14.7/14.8。全20 float→整数pairの両端/隣接float、逆20pairのties/最大値、NaN/±inf/±0、literalとtyped値の差、O0/O2・helper依存・元source診断 |
| [ ] NUM-04 | 初期profileで許可する数値変換と一般の同型取得を総合完成 | 子IDと整数12×12変換を統合し、§13.5.1–4の明示Semantics/略記・同じnormalized complete Typeの通常Copy/Moveも接続。borrow優先/Origin/取得権限を保持。char/bool数値変換・typed128bit↔floatは禁止。子IDだけで一般同型取得を完成としない | NUM-01, NUM-02, NUM-03, NUM-04-FLOAT, NUM-04-INTEGER, TYPE-01 | ConversionEmission/型・取得テストで一般同型、@owner等、literal/category、元source診断、単一評価・順序・禁止境界を検証。非数値borrow/object/pointer固有の実行はBORROW/OBJECT/UNSAFE各IDと合流 |
| [ ] LAYOUT-01 | struct/base、enum/Case、Tuple/配列、C layoutを共通TypeLayoutへ統合 | 全体代入からsize/alignment/offsetを計算し、論理取得・破棄順と物理順を分離。無限inlineとprofile上限を明示診断 | BIND-01, TYPE-02, OWN-01 | §21.1、同alignment順、base padding、tag、C offset、zero-size、上限。既存aggregate上限の扱いも明記 |
| [ ] ABI-01 | 全対応値の直接関数ABIと結果・parameter責任を統合 | aggregate、借用、zero-size、Neverを共通FunctionAbiから定義/callへ接続。取得の追加や結果早期公開がない | LAYOUT-01, BORROW-02, OWN-03 | §21.4、再帰、named引数、混合signature、cleanup非終了、破損ABI拒否 |

### 7.2 集約・宣言・Property

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] AGG-01 | Tuple/Field/固定配列の射影・要素アクセスを実行へ接続 | 正しいoffsetとbounds、read/write/borrow/Move制限を保ち、誤った全体Copyを行わない | LAYOUT-01, OWN-01, BORROW-03 | nested・zero-size・固定/動的index、参照alias、負index、部分再初期化 |
| [ ] AGG-02 | aggregateの関数引数・結果・if/do/loop/match結果を実装 | BASE-06のlocal-only制限を解除。未初期化最終slotに構築し、cleanup正常完了でのみ結果を渡す | ABI-01, OWN-03, FLOW-01, BASE-06 | Aggregate/Result/FunctionEmission、分岐、defer、再帰、直接構築と二重破棄なし |
| [ ] AGG-03 | enum構築・tag/payload配置・全体取得・破棄・match dispatchを実行へ接続 | 検証済みactive Caseだけを読み、残存payloadを正しい順に破棄する | LAYOUT-01, AGG-02, OWN-03 | EnumBinding/EnumOwnership/MatchEmissionの全Case、nested payload、失敗・未完全部分 |
| [ ] AGG-04 | Tuple/enum分解Patternとborrowed Subject・guardを完成 | candidateとbody binding、shared-read、部分Move、false-guard効果、網羅性を両グラフで保存 | AGG-01, AGG-03, BORROW-05 | PatternBinding/MatchOwnership/StringGuardEmission拡張、covered arm、借用guard、残存cleanup |
| [ ] STRUCT-01 | struct constructor/deinit、Field初期化・置換・member関数を実行へ接続 | headerだけの状態を解消し、self・完全性・初期化失敗・deinit後の自動cleanupを保証 | LAYOUT-01, ABI-01, OWN-03, BIND-01 | §§6.2/16.3、順序ログ、部分構築・default field・自己参照禁止・未使用body |
| [ ] PROP-01 | stored let/varの標準accessor操作を実装 | read/get/set/init等の許可と取得を区別し、slot直接操作でもaccess・Loan・初期化制約を保持 | STRUCT-01, BORROW-04 | §§11.1/11.2、getterとborrowの違い、consume、readonly、再代入順 |
| [ ] PROP-02 | custom/computed accessorのbody・ABI・cleanupを実装 | 独立したgetter結果型、setter入力、init、receiver・storage権限を正しくlowerする | PROP-01, ABI-01, FLOW-01 | PropertyRevisionParse/PropertyBindingから実行へ。副作用、一回評価、default、失敗時cleanup |
| [ ] PROP-03 | Contract Property witnessとconditional memberの呼出を完成 | 選択済みwitnessの契約を保持し、直接化で強い権限を取得しない | PROP-02, BIND-03, GEN-02 | 関連型、ref/uniq/owner receiver、条件付きmember、access差、不適合・再Bind |
| [ ] STATIC-01 | group/struct staticのlazy初期化とshutdownを実装 | 初期化順、再入/循環、失敗、成功済み値の逆順破棄とeffect依存を保存 | STRUCT-01, FLOW-01, BORROW-04 | §22.2、複数source、再帰初期化、Abort、未使用static、終了時defer/destructor |

### 7.3 Metadataとgeneric生成

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] META-01 | Type token、ValueMetadata、TypeContextと範囲破棄を実装 | 48-byte形式、HasCopy/null destroy、完全値の逆順破棄、full-width token、派生配置とsource locationを保持 | LAYOUT-01, OWN-03 | §21.2.2、hash衝突、同size別destructor、count0、stride0、部分値の除外 |
| [ ] GCODE-01 | 共通loweringとFixed/FromContextの基準共有bodyを実装 | 型付き計画から整数slot・隣接entry/context pairを生成。scalar helperで共有可能なbodyを強制複製しない | GEN-02, META-01, ABI-01, AGG-02, NUM-04, BORROW-05 | §21.3、明示specialization、full/partial固定、符号・alignment・SharedReadResult、slot未使用化 |
| [ ] GCODE-02 | budget非依存入口ABI、opaque callee context、正確なscratch frameを実装 | 閉じた代入ごとの固定容量を予約し、他instance追加で容量を膨らませない。schema差だけのadapterを作らない | GCODE-01 | §21.3.6/21.4.6、budget0 scalar入口、容量0 adapter、loop/再帰、escape拒否、probe/unwind |
| [ ] GCODE-03 | 有限生成・循環context・方式identity・接続検証を実装 | 有限cycleと型増殖を区別し、entry/context/固定事実を全接続で検証。未完成IRを公開しない | GCODE-02 | 相互再帰、T→Box型増殖、資源上限、破損slot、方式不一致、optional失敗時の基準復帰 |
| [ ] GCODE-04 | class内排他的特殊化と二つの増加予算を実装 | Appendix B.7の全member割当、元関数/生成単位上限、決定的cost/benefit順と取消しを実現 | GCODE-03 | 100+60の併存、全用途置換、0費用、同率、探索上限、列挙/並列/cache有無、body移動による会計漏れ |
| [ ] GCODE-05 | generic長さ・配列転送/破棄の共有経路を完成 | body長さは整数slot、破棄長さはarray TypeContext。count×Nが溢れる最適化で新たなAbortを追加しない | GCODE-03, TYPE-02, META-01, AGG-03 | §21.3.3.4、N0/stride0、普通の演算overflow、負index、展開と検査除去の正負 |

### 7.4 Callableとobject lifetime

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] CALLABLE-01 | Function Item・関数参照・間接callを実装 | identity・束縛済み引数・選択済み実装とABIを保持し、取得/receiverを一回だけ処理 | ABI-01, GEN-02, GCODE-03 | §7.6、通常/generic/specialized参照、unsafe取得、引数失敗、null等の偽call拒否 |
| [ ] CALLABLE-02 | concrete ClosureとcaptureのShared/Exclusive/Consuming実行 | capture順・Copy条件・部分消費・依存と逆順cleanupを保持し、header/call pointerを環境ごとに埋め込まない | CALLABLE-01, OWN-03, BORROW-05, STRUCT-01 | §21.2.5.1、明示/推論capture、zero-size destructor、self-borrow拒否、consuming後の再利用禁止 |
| [ ] CALLABLE-03 | 16-byte共通関数値と24-byte操作表の型消去を実装 | Owned/Shared/Non-Copyを守り、inline/heap環境語を値渡しし、call用viewと破棄責任を分離 | CALLABLE-02, META-01, GCODE-02 | size0/8/9、alignment8/16、16-byte Move、直接構築と一般式評価、heap free一回、hidden borrow非escape |
| [ ] OBJECT-01 | 静的継承・inherited Name制約・ObjectCompatibleを完成 | §6.2.2/12.4.4の静的選択・receiver調整・実装familyの公開効果証明を完成。virtual/overrideは未導入modifierとして拒否 | STRUCT-01, PROP-02, BIND-03, BORROW-04 | protected receiver、inherited conformance、保証の追加/撤回、祖先Name衝突、未使用body。§6.2.4の将来dispatchは実装要件にしない |
| [ ] OBJECT-02 | objの確保・payload構築・descriptor・破棄を実装 | 全modeに共通する16-byte header、payload+16、alignment16と完全動的型の破棄/解放を接続 | OBJECT-01, META-01, OWN-03 | §§13.5/21.2、部分構築、over-alignment、失敗location、base viewを誤解放しない |
| [ ] OBJECT-03 | rc/arcの取得・共有・release protocolを実装 | checked count、clone/move、最後のstrong解放、atomic/non-atomic差と順序規約を保証 | OBJECT-02, BORROW-04 | count上限、コピーとcloneの区別、別handleのpayloadアクセス、arc順序と生成IR、O0/O2 |
| [ ] OBJECT-04 | Weak side table・upgrade/downgrade・循環構築を実装 | strong0の非復活、移行・guard・最後のweak解放、構築中の公開禁止、解放後header非参照を保証 | OBJECT-03, CALLABLE-03 | §21.2.3、upgrade対final release、移行競合、builder失敗、弱メモリ順序。source並行機能は追加しない |
| [ ] OBJECT-05 | 規定済みobject view/upcast・runtime is・refinementを実行へ接続 | §13.5/13.6.1の具体struct Supports、Origin/取得、requireの有効型を保持。as・exact test・一般checked cast構文は導入しない | OBJECT-03, BORROW-04, FLOW-01 | RuntimeTypeTestをnativeへ。静的/動的型差、左辺一回評価、借用終了・再代入によるrefinement失効、非object/不正target拒否 |
| [ ] OBJECT-06 | object receiverのmethod/Property/generic操作を統合 | 直接/間接選択とreceiver取得を保ち、具体Coreのobject利用を完成。未導入runtime Contract Viewへ拡張しない | OBJECT-05, PROP-03, GCODE-03 | 受取Semantics行列、継承receiver、associated Type、dynamic destructor、invalid witness |

## 8. Core・文字列・コレクション・反復

catalogの「18枠が埋まった」だけでCore完成としない。SPEC §22.1と関連節の各宣言・操作・配置・所有権・実行を確認し、現catalogにない確定済みのobject/Weak API等も対象に含める。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] CORE-01 | Option/Resultのgeneric実行と成功/失敗/absenceの伝播 | 宣言だけの状態からCase構築・match・結果伝播・cleanupまで接続 | AGG-03, GCODE-05, FAIL-01 | §17.2、nested結果、失敗payload、Non-Copy、早期return、destructor非終了 |
| [ ] CORE-02 | Stringify・文字列補間・動的文字列構築を実装 | §22.1の確定signatureと§2.7/12.3の評価順、UTF-8長・確保・取得・破棄・失敗位置を保存。string +/+=の取得規則は§13.3の未導入境界を維持 | CORE-01, NUM-04, GCODE-03 | 日本語/NUL、nested補間、長さoverflow、Heap文字列置換、途中失敗、Stringify正負。未定義連結API待ちで補間を止めない |
| [ ] SEQ-01 | Index/Range/ResolvedRangeと固定配列metadataを実装 | range解決・bounds・長さ・indicesの型と検査を保持し、metadata readで不要Loanを作らない | TYPE-02, META-01, GCODE-03 | §4.6、空範囲、端点、負値、overflow、一回評価、保存index |
| [ ] SEQ-02 | 固定配列の値/借用/型依存要素読み取りを完成 | contextual literal、index/range、SharedReadResult、要素Loan、static Move制限を実行で保存 | SEQ-01, AGG-01, BORROW-05, GCODE-05 | Copy/Non-Copy/obj/uniq要素、N0、nested、借用中の書込/置換、O0/O2 |
| [ ] SEQ-03 | Arrayの格納・構築・容量・変更操作を実装 | §4.7のgrow/移送/挿入/削除/clearと状態・失敗・破棄順を保つ。capacityと初期化要素を分離。内部移送は確定した所有権計画を使う | SEQ-02, CORE-01, META-01, OWN-01, BORROW-03 | 再配置の重なり、容量境界、zero-size、要素借用、途中失敗、allocator失敗注入。公開exchange APIの命名を前提にしない |
| [ ] SEQ-04 | 共有Sliceの範囲・要素依存・再借用・返却を実装 | §4.6.6のCopy handleとSharedReadResult、tryGet/trySlice/splitAt/trySplitAtを接続。結果はhandle localでなく外部sourceへ依存 | SEQ-03, BORROW-05, OBJECT-03 | 部分範囲alias、Copy/Non-Copy/uniq要素のshared read、結果Loan/owner Move競合。Slice経由の書込・Move・排他borrowを拒否 |
| [ ] SEQ-05 | Dictionaryの規定済みkey/value操作・順序・取得/破棄を実装 | §4.7/12.3.4/22.1の検索・更新・容量・Equatable・返却と逆論理順cleanupを満たす。Core名だけのMissing状態を解消 | SEQ-03, BIND-03, CORE-01 | key重複/衝突、NaN-reflexive equality、置換、value/key破棄順、borrow中変更、値式を実行しない重複失敗 |
| [ ] ITER-01 | Iterator/Iterable・forの共通意味計画と生成を完成 | §22.1の確定済みassociated Element/Iteratorとnext/iterateを使用。witness、receiver効果、case lifetime、continue/exit/deferを統合 | SEQ-04, SEQ-05, GCODE-03, BIND-03, FLOW-01 | ResolvedRange/配列/collection/Slice、Copy/Non-Copy要素、nested/早期終了。Range自体の反復とlending iteratorは拒否 |
| [ ] CORE-03 | 比較Contract等を含む必須Core全体の宣言・実装監査 | 全規定Core名が正しいidentity・signature・capability・runtimeへ接続。Missingや同名偽Coreで穴埋めしない | CORE-02, ITER-01, OBJECT-04, OBJECT-06 | CoreCatalog/CoreModel/IntrinsicCapabilityと各操作の正負・改変拒否・再Bindテスト |

## 9. Unsafe・外部連携・ビルドライフサイクル

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] UNSAFE-01 | raw pointerの比較・算術・dereference・変換を実行へ接続 | §5のsigned offset、stride、初期化、provenance、明示unsafeを保ち、安全な借用権限を破壊しない | NUM-04, LAYOUT-01, BORROW-04 | null、one-past、zero stride、負offset、ptr/integer変換、alias属性の過剰付与なし |
| [ ] FFI-01 | LibraryImportの宣言検証・C ABI・呼出・依存記録を実装 | §22.3の許可signature・unsafe・呼出規約・外部symbol一致・link入力を確認。未導入aggregate passing等は拒否 | UNSAFE-01, ABI-01, BIND-02 | native fixtureで引数/結果/FP状態、誤DLL/symbol/signature、cleanup・unwind境界 |
| [ ] DEP-01 | 解決済み依存snapshotのidentity・定義側環境・失効を実装 | §18/20.2/21.3.4の直接参照名・module/version identity、推移metadata非公開、内容/不存在依存を内部入力で保持。公開設定・版探索はSPEC-DEPS-01とBUILD-01へ残す | BASE-04 | 内部SourceDocument/参照graph fixtureで別名同一identity、複数版、欠落、アクセス変更、定義側aliasを検証。本文にないDAG/全graph単一版/lock生成を強制しない |
| [ ] MODULE-01 | 解決済みsource snapshotを外部Kotonohaへload | DEP-01の内部解決結果からsource/moduleを一度だけ作り、定義側環境・直接参照名・版を保持。外部公開設定の完成とは区別 | DEP-01, BIND-01 | Compilation.Prepareの未接続loadを内部fixtureで接続し、順序/欠落/同identity別名/複数版/間接Name非公開を検証。最終外部設定接続はBUILD-01 |
| [ ] MODULE-02 | moduleを跨ぐ最終Binding・閉じた選択集合・private依存を接続 | import先のgeneric/contract/Originを保持し、依存追加削除・変更で適切に再検証する | MODULE-01, BIND-03, GEN-02 | §18/21.3.4、同名identity、private helper、specialization追加削除、アクセス拡大/撤回 |
| [ ] ARTIFACT-01 | §18.3のsource/config再構築とinterface情報保存を実装 | SPEC-ARTIFACT-01で確定した交換形式で定義側identity/可視性/完全Type/Origin/効果/cleanup/選択集合を欠損なく再構成。serialized Kotoをportable形式にしない | SPEC-ARTIFACT-01, MODULE-02 | §18.3/21.3.4の情報カテゴリを入力改変・欠落・版/target変更・private generic依存の正負round-tripで検証。未知のschema 1/SourceIdを採用しない |
| [ ] MODULE-03 | Libraryの検査用IR/object出力とApplicationへの生成依存を完成 | E02のLibrary一律拒否を解除。Libraryにはstartupを作らず、検証対象bodyを残す。外部安定ABIは約束しない | MODULE-02, GCODE-03, FFI-01 | Application/Library対照、unused body不正、entry/linkage、IR verifier、object symbol |
| [ ] MOD-01 | Mod登録・provisional query・snapshot・実行順を実装 | §20.7とSPEC-MOD-01のhost契約で決定的登録・scope照会・失敗/cancellation・変更禁止境界を接続 | SPEC-MOD-01, MODULE-02, BIND-03 | 重複/循環/順序、未知照会、selected/excluded syntax、失敗・再実行。Modを自動再試行しない |
| [ ] MOD-02 | 生成sourceの追加・統合・final再検証・無効化を実装 | 生成sourceのidentity/provenanceを保持し、古い出力の残留、二重生成、無検証公開を防ぐ | MOD-01, LAYOUT-01, GEN-01 | §20.7、断片追加/削除、生成診断位置、Mod入力変更、古いキャッシュ拒否、列挙順不変 |
| [ ] CACHE-01 | 検証済み意味計画までの永続再利用を実装 | §21.3.4.1–2本文のproof/ownership/effect/cleanup/source参照を照合し、使用時Loanを再検証。ABI/context/frame/budget/IRは再生成。Provider独立Library計画を分離 | MODULE-02, MOD-02, GCODE-03 | cache有無/破損/版・公開範囲・target変更/原文位置の正負検証。形式は内部設計で決め、doc由来のSourceId/store方式を必須にしない |
| [ ] COMPOSE-01 | 本文で確定したroot/Entry/Providerの宣言・選択を実装 | 非置換root、module identityと最終接続の分離を保ち、SPEC-COMPOSE-01/02で本文確定した文法・選択/適合規則だけをsourceへ接続 | SPEC-COMPOSE-01, SPEC-COMPOSE-02, MODULE-02, BIND-03, BORROW-04 | §13.1の確定事項と決定後の正常/欠落/重複/不適合例。group $/compose $を旧計画から採用せず、未決中は本ID未完了 |
| [ ] COMPOSE-02 | Std出力接続と構成依存生成物の再検証を実装 | SPEC冒頭7行のCore.writeLine→選択Std接続、§21.3.4.2の影響するwitness/inline/entry-context再生成を満たす。全bodyへCompositionIdを混入させない | COMPOSE-01, MODULE-03, CACHE-01, GCODE-04, BASE-05 | Provider変更/同module identity/古い生成物拒否/情報不足時の保守的失効、現行出力の回帰と製品/test予算分離 |
| [ ] NATIVE-01 | §20.8/21.5本文のnative要求・供給照合を外部moduleへ接続 | NativeLibraries・schema 3 manifest・採用backend/hash/ABI・kernel32生成・実objectのhelper依存検証を一貫実施。古い供給/未知helperを成功にしない | MODULE-02, FFI-01 | NativeToolchain/WindowsProfile/LibraryImportと通常native正負fixture。本文のsymbol/Kind/tool/hash条件を検証し、doc由来の一般COFF member閉包/weak/COMDAT専用契約を追加しない |
| [ ] BUILD-01 | 複数source/module、static、Modを含むbuild/runの原子性を完成 | 外部依存設定をSPEC-DEPS-01の確定契約へ接続し、failed/cancelled build・部分生成・依存変更で旧成功を誤認しない。既存Project.Checkの意味検証も維持 | COMPOSE-02, STATIC-01, MOD-02, FFI-01, NATIVE-01, SPEC-DEPS-01 | NativeToolchain/EmissionArtifacts/ToolchainResolverと既存CLI、失敗注入、空白path、hash/ABI/依存不一致、check内部経路。新規kimi check/restore/pack/publishを要件にしない |

## 10. 言語テスト・LSP・エディター拡張

LSP/拡張の改善は、多くのruntime機能の完成を待たず進められる。以下の依存欄が着手条件となる。補完・rename等、現状advertiseしておらずSPECにも要求のない新機能は追加要件として扱う。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] LANGTEST-01 | 本文で確定したtest構文・所属・共通検証を実装 | 冒頭9行の#Test/$expect/$require・一方向依存・共通検証/cleanupを、SPEC-TEST-01/SPEC-VERIFY-01の決定後の本文に接続 | SPEC-TEST-01, SPEC-VERIFY-01, MOD-02, GEN-01, DEP-01 | 決定後の発見/所属/正常・失敗検証/非選択検査の正負例。旧docだけのメッセージ転送/特定case lifetime規則を創作しない |
| [ ] LANGTEST-02 | kimi testのprocess隔離・有限報告/回収・再利用を実装 | 冒頭9行の必須方針とSPEC-TEST-02のrunner設計を接続。B.7.3本文に従い製品/testの要求・予算を分離し、追加testで製品planを再配分しない | LANGTEST-01, BUILD-01, GCODE-04, SPEC-TEST-02 | 成功/検証失敗/Abort/非終了、取消し、破損/欠落報告、0件、有限回収、再利用を隔離fixtureで検証。公開一時directory APIは根拠がなく対象外 |
| [ ] LSP-01 | 現行LSP通信の境界・取消し・resource lifetimeを検証し補完 | framing、分割入力、不正payload、open/change/close、版番号、shutdownを処理し、pool/lock/taskを解放 | BASE-07, MAINT-01 | stream単体テスト、UTF-16位置、複数変更、サイズ上限、EOF/取消し。既存scriptの成功だけで完了としない |
| [ ] LSP-02 | document snapshotから実コンパイラー診断へ接続 | 現行Parse/Bindingを正しいURI/UTF-16範囲/版へ公開し、古い解析で新しい版を上書きしない。後続言語機能も同じ経路を使う | LSP-01, BASE-04 | malformed/修正済source、他file依存、rapid change、close時消去、取消し・重複。全FRONT/BIND残実装を着手前提にしない |
| [ ] EDITOR-01 | KimiCodeの起動設定・lifecycle・配布構成と統合テスト | 固定Debug DLL/DebugWaitを通常利用から外し、設定されたserverを起動。client開始終了を依存APIに合わせ検証 | LSP-02 | node構文、拡張テスト、file/untitled、server欠落・再起動・deactivate、package内容確認 |
| [ ] QUALITY-01 | compiler/LSP/Coreの性能・保持参照・未使用経路を継続監査 | 必須処理を削らずallocation/peak memory/compile time/code sizeを測定し、正当なcacheと死んだ経路を区別 | CACHE-01, CORE-03, EDITOR-01, GCODE-04 | 既存Benchmarkの拡張、warm/cold・失敗後再利用・大規模入力・GC回収、context bytes/frame量、IDE解析 |

## 11. 完了判定と配布検証

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] ACCEPT-01 | 規範仕様と全実装段階の残差を閉じる | §12–15と要件台帳の全必須行が実装・正負テストへ接続。合法入力の未実装gate/未決必須契約を残さず、明示未導入境界は維持 | SPEC-01, SPEC-02, FRONT-02, TYPE-02, OWN-02, AGG-04, GCODE-05, OBJECT-04, OBJECT-06, CORE-03, BUILD-01, LANGTEST-02, QUALITY-01, ARTIFACT-01 | Appendix A/F・非実行body・不正計画・故障注入。除外履歴と全必須IDの依存閉包を機械検査し、対象外IDを実装済みにしない |
| [ ] ACCEPT-02 | clean取得からの再現build・全回帰・通常native・配布物検証 | Debug/Releaseの全suite、Windows O0/O2、CLI/LSP/拡張が成功。fixtureと実行input hashが一致し、warning/skip/失敗を記録 | ACCEPT-01, MAINT-02, EDITOR-01 | source/依存/toolchain準備から再現。Linuxはmanaged範囲、Windowsはnativeも確認。復元/packは外部公開しない |
| [ ] ACCEPT-03 | SPEC/STATUS/examples/READMEと完了宣言を同期 | 対応範囲・上限・未設計事項・配布手順・テスト結果が実物と一致。古い「planned」と実装状況の混在を解消 | ACCEPT-02 | examplesの実行、SpecTourの説明と実行例の区別、文書リンク/参照、Core catalogと実装照合 |

### 11.1 条件付きの追加検証

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] OPTIONAL-AOT-01 | ユーザーが明示的に指定した場合のみNativeAOT配布を検証 | publish、trim/serialization/CLI/LSPの実物検証が通る。未指定の現在は未実行のまま保持 | ACCEPT-02 | 明示指定後にAOT用手順を実施し、通常nativeと結果を区別して記録 |

OPTIONAL-AOT-01は本計画の通常完了を妨げる必須依存ではない。非Windows native、runtime Contract Views、source threads/tasks、未定義FFI拡張、re-export、永続object cacheはSPEC変更後に別IDで計画化する。未設計を独断で実装して「全機能完成」としない。

## 12. SPEC章・付録の対応表

この表は漏れを見つける索引であり、各章を一括で完了扱いする表ではない。SPEC-01で節・個別規則まで展開し、未対応が発見されたら適切な機能IDを追加する。

| SPEC範囲 | 主な実装ID |
| --- | --- |
| §1–2 方針・source・字句 | BASE-03, FRONT-01, SPEC-01 |
| §3 Type/取得/一時値 | TYPE-01, NUM-01, NUM-02, NUM-03, NUM-04-FLOAT, NUM-04-INTEGER, NUM-04, BORROW-01, BORROW-05, OWN-01 |
| §4 配列・長さ・Slice・collection | TYPE-02, AGG-01, GCODE-05, SEQ-01, SEQ-02, SEQ-03, SEQ-04, SEQ-05 |
| §5 unsafe/pointer | UNSAFE-01, FFI-01 |
| §6 宣言・struct・enum・Attribute | FRONT-01, BIND-01, STRUCT-01, AGG-03, OBJECT-01, FFI-01, MOD-01 |
| §7 関数・capture | BIND-02, ABI-01, CALLABLE-01, CALLABLE-02, CALLABLE-03 |
| §8 generic/static Contract | BIND-03, GEN-01, GEN-02, PROP-03, GCODE-01, GCODE-02, GCODE-03, GCODE-04。§8.5の未導入View構文は対象外 |
| §9–10 名前・可視性・推論 | TYPE-01, BIND-01, BIND-02, MODULE-02, BORROW-05 |
| §11 Property | PROP-01, PROP-02, PROP-03 |
| §12–13 式・演算・assignment・object操作 | NUM-04-FLOAT, NUM-04-INTEGER, NUM-04, OWN-02, AGG-01, OBJECT-02, OBJECT-03, OBJECT-04, OBJECT-05, OBJECT-06, CORE-02 |
| §14 制御・for・match・refinement/require | FLOW-01, AGG-02, AGG-04, ITER-01, OBJECT-05, FAIL-01 |
| §15–16 Origin/Loan/cleanup | ORIGIN-01, BORROW-01, BORROW-02, BORROW-03, BORROW-04, BORROW-05, OWN-01, OWN-02, OWN-03 |
| §17 失敗・警告 | FAIL-01, CORE-01, MAINT-02, ACCEPT-01 |
| §18 module/依存 | SPEC-DEPS-01, SPEC-ARTIFACT-01, ARTIFACT-01, DEP-01, MODULE-01, MODULE-02, MODULE-03, CACHE-01。本文の確定情報と形式不足は§13 |
| §19 directive | BASE-03, FRONT-01, FRONT-02, MOD-02。条件Nameの不一致はAF-0010 |
| §20 build/Mod/LLVM/native | SPEC-MOD-01, MOD-01, MOD-02, BUILD-01, NATIVE-01, FFI-01, MODULE-03, MAINT-02 |
| §21 layout/metadata/generic/ABI/runtime | LAYOUT-01, ABI-01, META-01, GCODE-01, GCODE-02, GCODE-03, GCODE-04, GCODE-05, CALLABLE-03, OBJECT-02, OBJECT-03, OBJECT-04 |
| §22 Core/startup/FFI/Windows | CORE-01, CORE-02, CORE-03, STATIC-01, FFI-01, BUILD-01 |
| Appendix A 検証義務 | 各機能の検証欄、ACCEPT-01, ACCEPT-02 |
| Appendix B 実装指針 | GCODE-04, QUALITY-01。参考algorithmを言語の追加受理条件にしない |
| Appendix C 実装状況 | BASE各項目、ACCEPT-03 |
| Appendix D/E/F 将来範囲・用語・文法 | SPEC-01, SPEC-02, FRONT-01, ACCEPT-03 |
| 冒頭本文のComposition/test方針 | SPEC-COMPOSE-01/02, SPEC-TEST-01, SPEC-VERIFY-01, SPEC-TEST-02, COMPOSE-01/02, LANGTEST-01/02。本文の確定方針を保持し、未記載の公開契約は決定待ち |
| 言語仕様外の既存製品部品 | MAINT-01, MAINT-02, LSP-01, LSP-02, EDITOR-01, QUALITY-01 |

完了の根拠はチェック数ではなく、選定した必須仕様を実ソースと実行結果で満たしたことである。新たな不具合・仕様不足が見つかった場合は、該当IDの条件を具体化し依存順を維持して更新する。

## 13. SPEC本文への再照合・除外履歴・仕様不足

### 13.1 本文で確定する契約と確定しない詳細

今回、doc/を検索・読込・参照せず、SPEC本文にある次の文だけを根拠にした。外部リンクの採用/優先主張から文法やAPIを補完しない。下表は仕様対応表で、チェック付きタスク行ではない。

| SPEC本文 | 保持する規則 | 計画ID・実装箇所・正常/不正検証 | 本文不足・対象外 |
| --- | --- | --- | --- |
| 冒頭7行、§13.8、§17.3 | 非置換root、Core.writeLineから選択Stdへの接続、module identityと最終接続の分離。現行§13.8の具体操作は$abortのみ | SPEC-COMPOSE-01/02、COMPOSE-01/02、FAIL-01。Parser/Binding/CoreIntrinsics/Emission。既存$abortと出力を維持し、決定後のEntry/選択を正負検証 | Entry宣言/参照の文法・型/適合・アクセス、Provider選択の公開設定/欠落/競合は未記載。group $/compose $/要求閉包の旧詳細は採用しない |
| 冒頭9行、§16/17、B.7.3本文 | #Test/$expect/$require/kimi test、一方向の製品/test依存、共通検証/cleanup、process隔離、有限報告/回収、成果物再利用。製品/test生成要求・共有class・予算を分離し製品を先に固定 | SPEC-TEST-01、SPEC-VERIFY-01、SPEC-TEST-02、LANGTEST-01/02、GCODE-04。Parser/Binding/Ownership/CLI/Emission。依存逆流・報告欠落/破損・非終了・追加testによる製品plan変更を検証 | #Test付与先/署名/所属/非選択本体検査、検証操作のsignature/効果/失敗時制御は不足。公開一時directory API、特定IssueId構造、メッセージ転送禁止等は本文根拠なし |
| §18.1/20.1–2 | 直接Kotonoha参照名、module/versionとSymbol identity、定義側alias、推移metadataをName公開しない。複数版は異なる参照名を使用可能。再利用は対象入力一致が必要 | SPEC-DEPS-01、DEP-01、MODULE-01/02、BUILD-01。ProjectFile/KotonohaIdentifier/Compilation/Binding。内部解決snapshotで別名/複数版/読込順/欠落/アクセス失効を検証 | 設定構文・版解決・参照graph診断は本文が別仕様に委ねる。Name/VersionだけのKotonohaArrayから取得先を推測しない。Dependencies/lock/restore/graph全体単一版/DAGを必須化しない |
| §18.3/21.3.4.1 | portable交換はsource artifactsまたはbinary interfacesでありserialized Kotoではない。source/config再構築、identity/可視性/完全Type/Origin/効果/cleanup/選択集合/private依存の保存情報は本文で規定 | SPEC-ARTIFACT-01、ARTIFACT-01、MODULE-02。Kotonoha/SourceDocument/Binding/interface実装。意味情報を欠損しないround-trip、内容/公開範囲/target変更による再検証 | encoding/required fields/validation/compatibilityは未記載。schema 1、manifest形式、SourceId、archive writer、pack/publish/store verifyの操作契約は対象外 |
| §21.3.4.2/21.4/B.7.3本文 | 初期永続cacheは検証済み意味計画まで。位置は現sourceへ再対応、使用時Loanを再検証。ABI/context/frame/budget/IRを再生成し、Provider独立Library意味計画と最終接続を分離 | CACHE-01、COMPOSE-02、GCODE-01–05。Analysis/Emission/生成record。cache有無/破損/内容変更/Provider変更/予算0・製品/test分離の正負検証 | SourceId/storeの信頼方式は採用しない。意味cacheの内部encodingは実装判断。生成選択/object cacheは未導入のまま |
| §20.8/21.5本文 | NativeLibraries、schema 3、hash/ABI/LLVM同一性、採用backend、kernel32生成、実objectのhelper依存、原子的成果物と失敗時無効化 | NATIVE-01、FFI-01、BUILD-01、BASE-07。NativeToolchain/WindowsProfile/EmissionArtifacts/backend scripts。IR/object/native・symbol/Kind/hash不一致・失敗注入を検証 | docだけの一般COFF member閉包・weak/COMDAT処理契約は追加しない。本文のkernel32 COFF検証等は引き続き必須 |
| §20.7.1–7 | C# prebuilt Mod、snapshot/query、依存順、一回実行、最終Binding、取消し/上限 | SPEC-MOD-01、MOD-01/02。Compilation/Project/Binding。二段生成、循環、失敗/取消し、古い出力・再試行禁止を検証 | 本文が実装設計へ委ねたquery/登録/assembly/project形式は実装者が具体化できる。追加言語機能は作らない |
| §2.5/19.2–3/20.4 | 条件Nameはcase-sensitive。Feature/FEATURE、windows/WINDOWSは別名。短絡・非選択Caseも不正Conditionを検査 | FRONT-02、AF-0010。Compilation/CompileTimeConditionEvaluator/ProjectFile/CompilationSpecificationTest。§14.1の正負5入力、culture、snapshot/再Prepare | 既存のignore-caseテスト成功を仕様適合の証拠にしない。SPEC本文は変更不要 |

### 13.2 ID維持・分割・除外の履歴

cycle 1のPlanでは前試行104 IDから下の4 IDを対象外履歴へ移し、100 IDをタスク行で維持した。FRONT-02、SPEC-COMPOSE-02、SPEC-VERIFY-01、SPEC-ARTIFACT-01を新設し104 ID（当時[x]7）とした。cycle 2ではこの104 IDを全て維持し、NUM-04の独立した残実装2 IDを新設して106 ID（[x]12、必須未完了93、任意未実行1）。元のHEAD計画91 IDも全て現行行に残る。除外を[x]やOPTIONAL-への付替えで表現しない。

| ID / 旧範囲 | 今回の扱いと理由 | 引継ぎ・必須依存への影響 |
| --- | --- | --- |
| SPEC-TEST-API-01 | 対象外。公開一時directory取得APIはSPEC本文に記載がなく、旧docのみが根拠 | SPEC-02/LANGTEST-02から依存を除去。AF-0009をdismissed。process隔離/有限回収の必須規則はSPEC-TEST-02/LANGTEST-02に保持 |
| CLI-CHECK-01 | 対象外。新規kimi checkのsource配布/lock連携CLI契約は本文にも既存登録にもない | ACCEPT-01から依存を除去。既存Project.Checkの内部意味検証はBUILD-01/ACCEPT-02に保持し、未接続stubと即断しない |
| ARTIFACT-02 | 対象外。新規kimi pack/publish、store閉包/発行表は本文根拠なし | ACCEPT-01から依存を除去。既存.NET/NuGet配布のpack内容・workflow検証はMAINT-02/ACCEPT-02に残す。外部公開はしない |
| ARTIFACT-03 | 対象外。SourceId管理content cacheとstore verifyは本文根拠なし | CACHE-01/ACCEPT-01の依存を除去。本文§21.3.4.2の検証済み意味計画の永続再利用はCACHE-01として必須維持 |
| SPEC-COMPOSE-01 → SPEC-COMPOSE-01/02 | IDを保持して宣言/参照とProvider選択に分割。doc本文統合を撤回し、本文不足を記録 | COMPOSE-01は両公開契約の確定を待つ。現行$abort、内部の失効検証、NUM/FRONTは独立 |
| SPEC-TEST-01 → SPEC-TEST-01 / SPEC-VERIFY-01 | #Test所属と$expect/$requireの型/効果を分離。本文にある方針は保持 | LANGTEST-01は両契約の確定を待つ。SPEC-TEST-02の内部transport/有限回収設計は独立 |
| SPEC-DEPS-01 → SPEC-DEPS-01 / SPEC-ARTIFACT-01 | 外部設定/版解決と交換encodingを分離。両方とも本文の明示不足を保存 | DEP-01/MODULE-01の内部解決snapshotを外部形式待ちから外す。BUILD-01は外部設定、ARTIFACT-01は交換形式へ接続して完成 |
| DEP-01 / ARTIFACT-01 / MODULE-01 / CACHE-01 / NATIVE-01 | IDは維持。doc由来のDependencies/lock/schema 1/SourceId/COFF詳細を除外し、§18/20/21の確定規則へ範囲を変更 | ARTIFACT-01をMODULE-02の後へ移動。SPEC-02とACCEPT-01から残る必須契約へ到達させ、循環・未定義・後方依存を検査 |
| FRONT-02 | FRONT-01の全体照合とは別の有界な新規修正。SPEC本文と実CLI/既存テストの反対動作を発見 | BASE-03のみを前提に最優先。BASEの限定した既存suite成功は保持するが、§19/20全規則の完成を意味しない |
| NUM-04 → NUM-04-FLOAT / NUM-04-INTEGER / NUM-04 | cycle 2でfloat相互変換と整数↔float・整数literal fittingを分割。NUM-04と一般同型取得/明示Semanticsの未完了条件を保持。既実装部分だけの[x]子IDは作らない | primitive子IDをTYPE-01待ちから分離。FLOAT→INTEGER→NUM-04の順に置き、NUM-04の既存下流からACCEPT-03へ必須依存を接続。一般同型取得・borrow優先/取得効果・禁止境界の範囲は削らない |

前試行で修正済みの範囲境界も本文で再確認した（AF-0003はresolved維持）。§21.5.3の128bit除算/剰余とfloat相互変換は初期profileの診断対象、§6.2.4/Dのvirtual/override、§13.6.1のas/未定義checked cast、§4.6.6の排他的Slice、§17.1の専用Option/Result伝播は必須実装にしない。共有Sliceとuniq要素のshared Reborrow、runtime is/upcast、明示require/return/match、§4.7/22.1のStringify/Dictionary/Iteratorは本文の必須規則を維持する。§13.3のstring連結取得が未決でも文字列補間を止めない。

### 13.3 保存する質問・影響・解除条件

非対話実行なので回答待ちを続けず、以下だけを外部決定待ちとして保存する。各質問はSPEC本文に不足する公開契約が対象であり、doc参照の許可を求めるものではない。

| 対象 / 指摘 | 必要な質問 | 影響・解除条件 / 独立作業 |
| --- | --- | --- |
| SPEC-COMPOSE-01、AF-0002 | Entryの宣言/参照文法、型/適合とアクセス規則をどう定めるか。§13.8の現行$abort境界へどの確定規則を追加するか | 本文に正負source例と規則が保存されたらCOMPOSE-01の該当部分を開始。非置換root/Std接続/module identity分離の方針は変更しない |
| SPEC-COMPOSE-02、AF-0002 | Providerの指定方法、既定選択、欠落/重複/不適合時の規則をどう定めるか | 本文の設定/正負例で解除。Provider変更の内部生成依存追跡は§21.3.4.2で先行可能 |
| SPEC-TEST-01、AF-0002 | #Testの付与先/署名、所属/発見/選択、通常modeと非選択本体の検査範囲をどう定めるか | 本文と正負source例でLANGTEST-01の所属部分を解除。製品→test依存禁止は維持 |
| SPEC-VERIFY-01、AF-0002 | $expect/$requireの引数・結果、評価順/効果、失敗後の制御/cleanupをどう定めるか | 本文と操作ごとの成功/失敗/不正使用例で解除。既存のrequire文と$abortは独立して実装/検証する |
| SPEC-DEPS-01、AF-0011 | §18.1が別途とする参照名/版制約/取得先の設定とgraph診断をどう定めるか | 本文と正負設定例で外部build接続を解除。既存Name/Versionからローカル取得先を創作しない。DEP-01/MODULE-01の内部snapshotは独立 |
| SPEC-ARTIFACT-01、AF-0011 | §18.3のsource/config再構築を満たす公開交換形式、必須field、validation/compatibilityをどう定めるか | 本文に交換/破損/非互換例を保存してARTIFACT-01を解除。意味情報カテゴリ・内部Kotonoha検証・意味cacheは独立 |
| SPEC-API-01、AF-0007 | §15.7の概念Exchange/Swapの最終source綴りと名前空間/intrinsic解決をどう定めるか | 決定と正負例を本文へ保存してOWN-02のsource接続を解除。確定済み責任移転/非重複の内部検証、Array内部移送は独立 |

SPEC-MOD-01のhost形式、SPEC-TEST-02の内部transport/有限上限、CACHE-01の内部encodingは通常の実装判断で具体化する。言語API・公開交換/設定仕様をそれらの判断に混入させない。NUM-04-FLOAT/INTEGER、LSP/拡張、CI、内部module検証などの独立作業があり、計画全体はblockedではない。

## 14. 実装契約（現在の次2枠は§14.6–14.8）

§14.1–14.5はcycle 1の受入条件と操作の履歴。そこで述べた未完了・setup待ち・次候補は、§2.6/§14.6以後とSTATUS先頭の現在の引継ぎで更新されている。完了済みFRONT-02/NUM-03/NUM-02/BASE-07/NUM-01を最初から実装し直さない。

### 14.1 過去契約: FRONT-02（0004で完了）

最初の操作はQ/directive-probes.jsonと現diffを確認し、Compilationの環境構築・リセット・metadata公開・名前照合を追い、case-sensitiveな§2.5/19.2/20.4へ修正すること。ProjectFileは原文名を保持しているため、lowercase化による別名統合はしない。CompilationSpecificationTestのignore-case成功/大小文字衝突期待を、本文どおりの正負テストへ置換する。SPECの規則をテストへ合わせて変えない。

| 規則・実装箇所 | 正常系 | 不正使用・再実行 |
| --- | --- | --- |
| §19.2/20.4、Compilation.Variables/Prepare/TryResolveValue | 組込みの正確な綴り、Feature=trueとFEATURE=falseの併存、独立したWINDOWS設定、文字列値のordinal比較 | 未設定WINDOWS/POINTERWIDTH、設定と参照の綴り違いをUnknownCompileTimeNameで拒否。完全一致の組込み衝突・予約語・不正設定型/値は引き続き拒否 |
| §2.5、ProjectFile/metadata/identifier | en-US/tr-TRで同じ選択、NFCの正規名、日本語、serialize/deserialize後も元の別名と値を維持 | 非NFC・予約語・完全一致の重複を拒否。Rで同一キー重複の上書きを再現済み。Project.TryCreate→ProjectFileの読込でDictionary集約前のキー列を検査し、完全一致重複を拒否する。引用/escape表記でも解読後のNameで検査 |
| §19.3/19.5、CompileTimeConditionEvaluator/Parser | #ifと#switchで同じ環境/選択、selected syntaxの元scopeとcleanupを保持 | true or/false andの不正名、選択済み後のCase条件を検査。未知名をfalseやgeneric Pendingにしない |
| §20.2/20.4、snapshot/再Prepare | 設定を準備後に変更してもsnapshot不変。別Compilation/再Prepareで環境とmetadataを再構築 | preparation失敗後に半端な環境を公開せず、許される再Prepareの成功/失敗順と既存parse後制限を維持 |

完了条件: 上記とQの5再現入力・Rの7再現入力を修正後のDebug/Release build・近傍/全managed suiteで検証し、現CLI emit-llvmで期待exit/診断・選択されたIRを確認する。新規runtime機能はないためこのIDに全通常nativeの再実行を課さず、BASE-07は未完了のまま分離する。割り当ては既存の近傍検査を使い、比較のためのToLower等の新規文字列生成を避ける。未確認条件があればFRONT-02は[ ]を維持する。

### 14.2 過去契約: NUM-03（char、0004で完了）

第1枠の修正後に新しい回帰またはFRONT-02の残検証があれば第2枠の先頭で閉じる。完了後は以下の既知未実装NUM-03へ進む。NUM-03の直接依存BASE-05は充足しており、Composition/test/依存形式の決定待ちは無関係。

前試行の `P/char-probe/probe.kimi` は合法なliteral/local/比較で、Release CLIはexit 1、値flow計画不一致。i32対照はexit 0。修正対象は主に ScalarTypes.cs、Analysis/OwnershipAnalysis.Values.cs、Emission/BodyLowering.Values.cs、比較/Match lowering、FunctionAbi/既存aggregate接続。CharLiteralKotoのRuneとSourceSpan、WindowsLoweringの4-byte i32配置は既にある。新しい構文/APIは不要。

最初の操作: 現HEAD/diffと上記probeを確認し、charを整数演算/整数変換と区別した値計画へ接続する。長い全体調査を繰り返さず、literal→local→比較→直接callの縦経路を実装し、同じID内で下表の残検証を閉じる。

| 必須規則 | 正常系・期待する観測 | 不正使用/破損計画の検証 |
| --- | --- | --- |
| §2.8/3.1.3/A.6: Unicode scalar | NUL、ASCII、日本語、D7FF/E000、非BMP、10FFFF、unassigned/noncharacter/combining。原文・escapeが等しいscalarなら同値 | surrogate/110000/複数scalar/不正escapeを既存parse段階で拒否。IRのchar定数/型を壊した内部計画から出力しない |
| §13.4/21.1.4: 比較と表現 | 6比較がscalar値順。local Copy/再利用/置換、4-byte格納、if/do/loop結果、必要なphi/slot、named直接call/returnが一致 | char算術・bit/shift・数値変換を許可しない。charを単に整数Widthへ追加して型境界を消さない |
| §14.8: literal match/guard | char literalとbinding/wildcard、別綴りの同値、guard falseから次arm、deferと結果到達 | 不正literal/型、literal列挙だけの非網羅性、型の異なるSubject、破損match値計画 |
| §16/21.1/21.4: 既存aggregate/cleanupへの接続 | 現行tuple/固定配列payloadにcharを含めても論理順とCopyを保存。関数/結果のdefer副作用順をO0/O2で比較 | 未実装の一般projection/aggregate ABIのgateを一括で解除しない。未使用body/到達不能箇所の不正利用も診断 |

受入: 近傍managedとDebug/Release suite、char用fixtureのLLVM verifier・O0/O2 stdout/stderr/exit・順序、再解析/破損計画、warm allocationを記録する。既存の一般aggregate/borrow完成をNUM-03へ混入させない。未確認条件があればNUM-03は[ ]を維持する。

native準備: `C:/App/llvm/bin` のLLVM 22.1.8とpinned dlltoolは前試行Pでpreflight済み。今回は環境を再検証していない。Implementationは現在のhashを確認して `pwsh -NoProfile -File backend/windows-x64/setup.ps1 -LlvmBin C:/App/llvm/bin` でignore対象toolchainへ採用backendを設置する。setupは通常native検証でありNativeAOTではない。fixture生成後に対象patternの一致件数>0と入力hashを保存して `test-scalars.ps1 -FixturePattern '<今回のchar fixture prefix>*.ll'` を直列実行する。実在しないfixture名を成功根拠に使わない。setup/環境失敗は製品回帰と分け、managed実装を先行できる。

### 14.3 過去契約: NUM-02（f32/f64、0004で完了）

FRONT-02とNUM-03の残実装/検証または新しい回帰があれば先に閉じる。NUM-03が閉じた後の候補はNUM-02（直接依存BASE-05は充足）。この契約は前試行から保持するが、現在の次の2枠をNUM-03→NUM-02と誤認しない。異なる未決APIを待つ必要はない。

最初の操作: `P/float-probe` の値flow不一致を起点に、Binding.Expressions.FitsFloatとNumberLiteralKoto.SourceSpellingから選択精度の値を計画へ保存する。TryGetBasicValue/旧helperのf64経由をf32生成に流用しない。ソースにexact decimalを保持して直接f32/f64へfitする既存経路を生かす。

| 必須規則 | 正常系・期待する観測 | 不正使用/破損計画の検証 |
| --- | --- | --- |
| §2.6/3.1.2/A.6 | decimal→選択精度の単一rounding、ties-even、f32二重丸めの反例、subnormal/±0、原文/serialization保持 | literal overflowをcompile-time診断。古いf64値/誤った精度・定数bitsから出力しない |
| §13.2–4/21.5.4 | fadd/sub/mul/div/neg、比較、代入と+=/-=/*=/ /=、直接call/結果、既存選択結果とaggregate payload。0除算はfloatのinf/NaN | floatの++/--・剰余/bit/shiftとfloat literal Patternを拒否。NaNのbuilt-in比較をEquatableのNaN-reflexive mappingと混同しない |
| §21.4/21.5/A.14 | NaN、±0、subnormalを含むO0/O2の同じ結果/cleanup。no fast-math/reassociation/fusion、ABI標準FP環境、実objectの依存確認 | integer overflow Abortの流用、精度の混在、未検証ABI/値計画を拒否。FP環境自動修復は追加しない |

通常の全数値間変換はNUM-04へ残す。NUM-02の実行検証には追加の数値表示APIを発明せず、比較のbool結果や固定文字列出力等を観測に使う。profileの禁止演算・入力gateを緩めて完了にしない。近傍/全managed→今回生成float fixtureの通常nativeの順で記録し、未完なら同IDの具体的残差をSTATUSへ渡す。

### 14.4 過去契約: BASE-07 → NUM-01（0005で完了）

AF-0001の残検証を採用。0004-implementationのsource/config 464件、fixture入力5,051件、Release DLLをhash照合してから全1,009 scalar fixture/2,018 O0/O2検証を開始した。CLI/LSP/runtime/policyは照合済み前枠の証拠を使う。成功件数・入力不変・終了コードを保存してBASE-07とAF-0001を判定する。

NUM-01はSPEC §21.5.3本文の確定済み128bit subsetを対象とする。配置定義を再利用し、値計画/operandの128bit保持、literal/ABI/local/result/match/既存aggregate、全整数型間の範囲検査、比較・bit/shift・checked演算を接続する。除算/剰余とfloat相互変換は未使用body/定数偽枝/compoundも最適化前に拒否。必要な定数評価は独立して保持する。

検証: WideIntegerEmissionTestとConversionEmissionTestの全12×12整数型行列、合法化した同一旧入力の正の回帰、端点/overflow/shift元幅/破損high bits・parameter index、warm allocationをmanaged Debug/Releaseで確認する。現compilerから生成した新規/変更fixtureにLLVM verifierとO0/O2 nativeを実行し、乗算の実object依存を調べる。変更のない旧fixtureはhash一致かつ全件証拠がある場合だけ再利用する。全条件が揃わなければNUM-01は未完了に維持。対象にblocking指摘なし。
### 14.5 過去契約: NUM-04の先行部分（0005で部分実装）

NUM-01完了後、SPEC §13.5.4のfloat literal fitting、f32→f64幅拡張、f32/f64同型取得を先行する。整数↔float、実行時f64→f32のfinite-to-infinity検査、一般の同型取得とTYPE-01依存は残す。NUM-04全体の行を[x]へ変更しない。幅拡張はIEEE値を正確に保持し、literalは選択された精度へ直接roundingする。NaN/±0/subnormal、nested conversion/phi/call/cleanup、破損plan、範囲外literal、整数/float混入拒否をmanaged両構成と現compiler生成fixtureのLLVM/O0/O2で確認する。追加featureの入力以外はhash一致で既存native証拠と対応付ける。

### 14.6 現在の第1枠: NUM-04-FLOAT（実行時f64→f32を完成）

最初の操作: S/float-convert-inputs.json・最終diffと現入力の一致を確認し、§2.7の欠落した生成fixtureを現sourceの両構成managed build/testから再生成してS/float-convert-fixtures.jsonとhash照合する。基準を保存したら、`FloatConversionEmissionTest.RemainingConversionsAreNotMistakenForWidening`の`let x: f64 = 1.25; x@f32`相当の拒否期待を起点に縦経路を実装する。Binding.Conversionsの型行列、BodyLowering.Valuesの破損計画検査、PlanConversion/Check分類/CFG、Writerの変換・失敗分岐、WindowsLowering.Abortの理由/元source位置を一体で接続する。float結果一律無検査とfpext固定を単に解除しない。既存整数の理由ID/診断を保つ内部catalog設計は通常の実装判断であり公開仕様待ちではない。

| SPEC本文・実装箇所 | 正常系・期待する観測 | 不正使用・失敗/再解析 |
| --- | --- | --- |
| §13.5.4/21.5.3–4、ConversionPlan/Writer | f64→f32がnearest/ties-even。f32最大有限値、そのoverflow境界の両側/隣接f64、正負subnormal・最小値・underflowによる符号付き0。f32→f64と同型は既証拠を回帰維持 | 有限値が±infinityに丸まる場合だけ実行時Abort。入力NaN/±infinityは受理し、payload保持は要求しない。単なるfloat算術のoverflowを新しくAbortにしない |
| §13.5.4/A.6、FloatingTypes/Binding literal分類 | direct浮動literalは対象精度へ一回rounding、括弧/直接符号/serializationを保持。typed値の連鎖f64→f32→f64は中間roundingが観測可能 | direct範囲外literalはcompile-time診断、同じ大きさのtyped f64値は評価された時だけAbort。未使用body/偽枝でもliteral fitting失敗は診断、正常なruntime変換は受理 |
| §13.5.6/17.3.4/A.15、Ownership/Graph/Results | 単一評価、named引数、discard、phi/loop結果、returnとdefer、既存aggregate payloadへの接続。成功する各段の結果確保・順序を保持 | 変換失敗後は後続引数/書込/cleanupを実行しない（Abort規則）。元sourceの位置を確認し、未評価の合法な失敗変換を不当に静的拒否しない |
| §21.5.3/A.14、Values検証/IR/実object | 変換結果と失敗条件の型/支配関係が整い、LLVM verifierとO0/O2でstdout/stderr/exitが一致 | source/target/ConversionBinding/operand/checkを壊した計画をIR出力前に拒否。char/bool数値変換、typed i128/u128↔floatの未使用body/偽枝禁止を保持。再Bind/再Analyzeとwarm allocationも確認 |

完了条件は上表と§14.8の現compilerによる検証。既存18 float変換fixtureに新規成功/Abort/順序ケースを追加し、全条件が揃った時だけNUM-04-FLOATを[x]にする。一般Typeや@owner略記/明示Semanticsの残差はNUM-04へ残す。受理する新しいソース操作/APIはSPEC本文の範囲だけ。

### 14.7 現在の第2枠: NUM-04-INTEGER（整数↔float・整数literal fitting）

第1枠で新しい回帰または残検証が出たら先に閉じる。次にNUM-04-INTEGERへ進む。最初の操作はBinding.Conversionsの整数literal→floatの先行拒否と数値行列、ConversionEmission/FloatConversionEmissionの拒否期待を対照し、NUM-04-FLOATの検査・診断経路を整数向けに拡張すること。全体台帳/TYPE-01/未決公開APIの調査を繰り返さない。

| SPEC本文・実装箇所 | 正常系・期待する観測 | 不正使用・失敗/境界 |
| --- | --- | --- |
| §13.5.4/21.5.3、Binding/ConversionPlan/Writer | f32/f64→i8/u8/i16/u16/i32/u32/i64/u64/isize/usizeの20pair。元精度p=24/53、signed N<=pはx > -2^(N-1)-1、N>pはx >= -2^(N-1)、上限x < 2^(N-1)。unsignedは-1 < x < 2^N。成功後にだけfptosi/fptoui | 各pairの端点と上下の隣接float、正負fraction、NaN/±infinityを検証。-128.75→i8=-128、-0.75→u8=0、-1→u8とf32 2147483648→i32はAbort。投機的変換をselectで隠さず、ordered比較でNaNを落とす |
| §13.5.4/21.5.3–4、integer→float lowering | 逆の20pairのsigned/unsigned・最小/最大値、精度p付近のties-even、0→+0、isize/usizeの64bit profile。si/uitofpの符号を保持 | 許可された64bit整数はf32最大有限値より小さいためfinite-to-infinity不能を証明できる。不要なチェックを強制しないが、誤符号/切捨て/中間floatによる二重丸めは不可 |
| §2.6/12.3.4/13.5.2–4/A.6、NumberLiteral/FloatingTypes/Ownership | 整数literal→f32/f64はexact magnitudeから直接roundingしi32/i64へ先にfitしない。decimal/base-prefix/区切り、直接符号/括弧、最大許可magnitude、serialization、ties/二重丸め反例 | literal表現上限を維持。直接の最大u128 magnitude→f32でinfinityになる場合はcompile-time失敗、同magnitude→f64のfittingは可能。typed i128/u128→floatは別で禁止。整数0と直接符号付き整数0は+0、浮動literal -0.0は-0 |
| §13.5.2/4/6/17.3.4、Binding/CFG/diagnostics | 浮動literal→整数は通常のf64 source Typeを先に決めてtruncate/range。typed/general式へtarget型を逆流させない。連鎖ごとにrounding/失敗、discard/call/return/phi/cleanup順を維持 | `1@f32`と`1.0@i32`の扱いを分離。128bit typed↔f32/f64の8方向、char/bool変換、未使用body/偽枝の不正利用をO0/O2前に診断。型/値/境界plan破損・再解析も検証 |

完了条件: 全40許可方向とdirect literalをmanaged正負/現LLVM O0/O2へ接続し、各float→整数pairの隣接境界を含むことをfixture一覧に記録する。変換ごとの元source診断、実objectの未供給helper不在、評価/失敗順・割り当てを確認する。テスト件数だけで行列全体の対応を主張しない。全条件が収まらなければNUM-04-INTEGERの[ ]を維持し、完了した方向/入力hash/残pairを次へ渡す。

### 14.8 2枠の検証・時間配分・独立作業への切替

- managedは現global.jsonのMTPを使用。まず近傍test選別を現runner helpで確認し、両構成の`dotnet build Kimigayo.slnx -c Debug/Release --no-restore -v:minimal`成功後に対応する`dotnet test --project xUnitTest/xUnitTest.csproj -c Debug/Release --no-build --no-restore`を直列実行する（Debug/Releaseは各コマンドで片方を指定）。restoreが必要なら通常の依存復元から始め、古いDLL/0件起動を成功にしない。
- 通常nativeは現compilerが生成した対象の`test-scalars.ps1 -FixturePattern`で近傍を検証し、件数>0・fixture/期待stdout/stderr/exit・source/toolchain/configのhashを保存する。前枠は`-LlvmBin C:/App/llvm/bin`でLLVM 22.1.8を検証済み。現hash/利用可能性を確認し、同じcopied dlltool権限失敗を条件不変のまま繰り返さない。
- Abort catalog/runtime共通IRが変われば旧scalarの全入力も変わる。安全に生成領域だけを退避して空からmanaged両構成で再生成し、全scalarのLLVM verifier/O0/O2、`test-emission.ps1 -Configuration Release`、`test-cli.ps1 -Configuration Release`とLSP基本通信を直列で確認する。追跡fixture/lock/設定は検証目的で再生成しない。共通IR・toolchain/供給が一致する既存nativeは入力一致の根拠付きで再利用できるが、異なるfixtureへ古い成功を転用しない。
- Sの旧全1,009 scalar/2,018実行は約33分。現1,145以上なら**40分程度以上**を見積もり、実装の後に長時間検証を残しすぎない。各枠の上限/deadlineを最初に確認し、最後5分は証拠/引継ぎ専用とする。全nativeが必要なら残45分以上を目安に開始判断し、収まらなければ新機能を止めて未実行範囲を保存する。件数増加と実測速度で見積りを更新し、段階を跨ぐ処理を残さない。
- source/planの割り当て増を避け、型付き境界と変換計画を再利用する。現32-byte OwnershipValue/24-byte EmissionOperandと、前枠のwarm analysis+IR 0 byteの測定条件を比較可能に保つ。新しい経路も反復で測定し、性能の揺れを機能成功と混同しない。
- 外部待ちのAF-0002/0007/0011はこの2枠を止めない。両子IDの後はNUM-04の未充足TYPE-01/一般取得を確認し、依存を満たさなければ準備済みBASE-04に基づくDEP-01の内部snapshot実装、またはBASE-08に基づくMAINT-01のLSP整理へ切り替える。数値検証だけが環境待ちになった場合も同じ独立作業を選ぶ。新しい根拠のない全体調査や同じ環境失敗を反復しない。

## 15. Appendix Aの必須検証と実装箇所の対応

これは§12の章索引を、規範の検証義務ごとに具体化した表。下記の既存テスト名は拡張する接続先であり、その機能全体の成功証拠ではない。個別の完了証拠は各IDへ付ける。

| 規範 | 主担当ID・実装領域 | 正常系 / 不正使用・無効化の検証 |
| --- | --- | --- |
| A.1 source identity | FRONT-01, BIND-01, MODULE-01, MOD-02; SourceDocument/CodeContext/Compilation/Parser | SourceDocumentAndDiagnostic、IdentifierIdentity、KotonohaSerialization: immutable snapshot/fragmentの元位置保持 / 別source alias漏れ・古いcontext再利用拒否 |
| A.2 directive | BASE-03, FRONT-01, FRONT-02, MOD-02; Tokenizer/Parser/CompileTimeConditionEvaluator | CompilationSpecification/DirectiveConditionValidation/CompileTimeSwitch: case-sensitive設定・選択項目を同scopeへ統合 / 短絡・未選択armの不正Condition、false #ifの境界 |
| A.3 Binding/incremental | TYPE-01/02, BIND-01/02/03, BORROW-04, MOD-01/02, CACHE-01; Binding.* | Binding/InheritedReceiver/ConditionalConformance/CompilationSpecification: candidateの選択・定義環境保存 / 名前追加・アクセス変更・不存在/効果/古いproofの失効 |
| A.4 Property | PROP-01/02/03, OWN-01/03; Binding.Properties / accessor lowering | PropertyBinding/PropertyRevisionParse: 標準操作とcustom ABI・一回評価 / private-set Move、部分構築、getter-resultの誤borrow |
| A.5 pointer | UNSAFE-01, FFI-01; Binding.Conversions / pointer emission | 合法なsigned offset/stride/typed access / 不正型・unsafe欠落・未証明inbounds/noalias。runtime UBを新しいAbortへ変更しない |
| A.6 literal | BASE-03, NUM-01/02/03/04, NUM-04-FLOAT, NUM-04-INTEGER, CORE-02; Number/Char/StringLiteralと値計画 | NumberLiteral/CharLiteralParse/StringLiteralParse: exact値・原文と対象精度 / 範囲外・二重丸め・破損値。次の2枠は§14.6–14.8 |
| A.7 cleanup | OWN-01/02/03, FLOW-01, ABI-01, AGG-02; OwnershipAnalysis.Cleanup / BodyLowering.Results | Deferred/Result/AggregateEmission: 登録と退出・結果確保・逆順 / 未完成層deinit、cleanup非終了、二重破棄、破損計画 |
| A.8 callable/object | CALLABLE-01/02/03, OBJECT-01–06, META-01; capture/effect/metadata/runtime | capture順・16-byte関数値・object/Weak責任 / self-borrow escape・count overflow・upgrade競合・Origin消去誤用 |
| A.9 refinement | OBJECT-05, FLOW-01; Binding.RuntimeTypeTests / ControlFlow / lowering | RuntimeTypeTest/ControlFlowAnalysis: require支配の有効型 / 保存boolの偽provenance・書換え/aliasによる失効 |
| A.10 enum/Pattern | AGG-03/04, CORE-01; Binding.Patterns / MatchCoverage / OwnershipAnalysis.Match | EnumBinding/PatternBinding/MatchOwnership: Case/tuple・candidate/body・guard順 / 型不一致・非網羅・部分Move/残存cleanup・covered arm不正 |
| A.11 static Contract | BIND-03, PROP-03, OBJECT-01; Binding.Contracts/ConditionalConformances | Contract/ConstraintBinding: witness/associated Type/conditional適合 / 循環証明・Unknown・不正receiver保証・公開状態の撤回 |
| A.12 generic | TYPE-02, ORIGIN-01, GEN-01/02, GCODE-01–05; Binding/Analysis/Emission | SpecReview/Constraint/Length拡張: schema・普遍body・明示選択 / 未使用generic不正・Pending不正認証・型増殖・古い選択集合 |
| A.13 sequences | SEQ-01–05, ITER-01, BORROW-05; Range/Collection BindingとCore/runtime | RangeIndex/CollectionLiteral/For拡張: bounds/SharedReadResult/容量・順序・反復 / 負index・借用中変更・duplicate key・Range反復・排他Slice拒否 |
| A.14 LLVM/runtime | NUM各ID, LAYOUT-01, ABI-01, META-01, GCODE各ID, OBJECT/CALLABLE各ID, BASE-07, NATIVE-01, ACCEPT-02; Emission/backend | managed IR+O0/O2 native、helper/COFF/unwind/ABI、fault adapter、product/test予算 / supply/hash/未知symbol・破損plan・予算0・非終了。NativeAOTは別の任意ID |
| A.15 control flow | FLOW-01, FAIL-01, AGG-02/04, LANGTEST-01; ControlFlow/Ownership/BodyLowering | CurrentControlFlow/ControlFlowConformance/Result/Match: 構造上と実行上の完了・値確保・順序 / 未使用body/定数偽枝の必須診断、非終了cleanup後の偽結果 |

既存CLIはBUILD-01/DEP-01、本文にあるkimi testはLANGTEST-02、LSPはLSP-01/02、拡張はEDITOR-01、既存CI/NuGet配布はMAINT-02/ACCEPT-02で必須合流する。新規source配布CLIの除外は既存製品の配布検証を除外するものではない。既存LSP scriptやnode構文検査だけを診断/拡張hostの統合成功に読み替えない。

### Implementation 0004 進捗

FRONT-02: 完了。E/front-inputs.jsonとfront.diffのソースでDebug/Release build各exit 0・警告0/エラー0、近傍各173件、全managed各3,830成功/失敗0/skip0。Q/Rの元入力hash一致、各構成12件のCLI期待exit一致と選択IRをE/front-cli.json・front-selection.jsonで確認。設定重複は読込失敗で拒否し、未知名は元source位置のUnknownCompileTimeName。比較lookupのwarm allocationは0 byte。SPEC本文変更不要。次は§14.2のNUM-03。

NUM-03: 完了。E/char-inputs.json・char.diffのソースでDebug/Release全managed各3,905成功/失敗0/skip0、最終buildは各警告0/エラー0、近傍148成功。今回生成36 char fixtureの一覧/hashをE/char-fixtures.jsonへ保存し、LLVM 22.1.8 verifierと通常native O0/O2計72実行（stdout/stderr/exit/混合集約の逆順破棄）成功。Pのchar CLI再現も現Releaseでexit 0。warm analysis/emission 0 byte。E/char-checks.json・char-result.json。コピー先LLVM起動権限エラーをE/llvm-setup.logへ記録し、元のpinned C:/App/llvm/binを明示したbuild/nativeが成功。NUM-02へ続行。

NUM-02: 完了。選択精度へ直接roundingするFloatingTypes、typed constant/ABI/FP命令・比較・結果/aggregateを接続。matchのPattern局所変数による結果型推論を修正した。E/float-inputs.json・float.diffのソースでDebug/Release近傍各63・全managed各3,968成功/失敗0/skip0。43 fixture/86 LLVM verifier・O0/O2実行成功、元float CLI再現exit 0。f32二重丸め反例・serialization再解析、NaN/±0/subnormal、混在stack ABI、cleanup、破損計画拒否とwarm allocation 0 byteを確認。E/float-checks.json・float-fixtures.json・float-result.json。charの180入力hash一致により既存72 native実行を再利用できる。buildのtest nullable警告1件はAssert.NotNullで修正し、BASE-07の最終Debug/Release buildで警告0/全managed各3,968成功を再確認した。通常変換はNUM-04、全体検証はBASE-07/ACCEPT-02へ残す。
BASE-07: 部分検証、未完了を維持。E/baseline-retirement.jsonの安全な退避後、空から現ソースで両構成build（警告0）/全managed各3,968を成功させた。baseline-inputs.json・baseline.diff・baseline-fixtures.jsonはソースとscalar 1,009 fixtureを含む5,051入力のhash。現Release runtime 68 O0/O2、CLI 8 scenario群、LSP基本通信（7要求/通知・4応答）、artifact-paths/toolchain/kernel32の3 scriptはexit 0。baseline-checks.json・baseline-integration-checks.json・baseline-policy-checks.json・各report/log参照。以前のchar/float native計158実行の入力も最終再生成と一致。全scalar 1,009/2,018実行は未実行で、約35分以上の見積りが残時間を越えるためslot 2へ渡す。baseline入力hash一致を確認し、test-scalars.ps1を絞込みなしで実行すること。AF-0001をこの部分結果だけでresolvedにしない。全体の完了認定・CI実workflow・VS Code host・NativeAOTは実施していない。
### Implementation 0005 進捗

証拠Sは同runの0005-implementation。BASE-07: 完了。前枠のsource/config 464件・fixture 5,051件・Release DLLを照合後、全1,009 scalar fixture/2,018 O0/O2成功。NUM-01: 完了。128bit値の保持/ABI/結果/checked算術・shift・全整数変換を接続し、正負/破損計画/serialization/0-byte warm allocationを確認。追加118 fixture/236 O0/O2とCLI禁止演算32ケースが成功。NUM-01時点の空から生成した両構成managed各4,092、現runtime68、CLI8 scenario群、LSP基本通信、policyが成功しAF-0001を解消した。

NUM-04: 部分実装。§14.5のfloat literal fitting、f32→f64、f32/f64同型取得を接続。float変換近傍31成功、追加18 fixture/36 O0/O2成功。最終の空からのDebug/Release buildは警告0/エラー0、全managed各4,123成功/失敗0/skip0。旧5,641入力のhash差異0で最終1,145 scalar fixture/2,290実行の証拠を対応付けた（float-convert-inputs.json・float-convert-checks.json・float-convert-fixtures.json・float-convert-result.json）。現CLI/LSPと32禁止診断も再確認。一般の同型取得/TYPE-01、実行時f64→f32、整数↔floatと整数literal→floatは未完了として残す。次はSTATUS先頭のNUM-04操作へ進む。SPECの規範・NUM-04全体の完了条件は弱めていない。
