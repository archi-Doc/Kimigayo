# Implementation Plan

調査日: 2026-09-14（Asia/Tokyo）。対象: この作業開始時の未コミット変更を含む作業ツリー。

## 1. 目的・対象範囲

SPEC.md 本文の確定済み規則を、字句・構文解析、Binding、所有権検証、コード生成、通常native実行まで接続し、既存CLI・LSP・エディター拡張を含むプロジェクトの完了条件を定める。

- 根拠は [AGENTS.md](AGENTS.md)、[SPEC.md](SPEC.md)、実ソースと実行したテスト。 [STATUS.md](STATUS.md) は調査の索引に使い、実装の証拠とは区別する。
- 初回調査ではdoc/ 以下を読み込まず、SPECからのリンクも追っていない。以後の自動実装ではSPECが採用済み・優先と明記する文書だけを該当契約の確認に使用し、参照先と優先理由を記録する。Design/の旧例や未採用案は根拠にしない。
- SPECに詳細がなく外部設計書への参照だけがある契約は、SPEC-02で採用済み設計の本文統合と本当に未決定の事項を分ける。既決事項の再承認を求めず、名前やコメントから未設計APIを創作しない。
- 初期の実行対象は SPEC §21.5 の windows-x64-v1。ほかの出力profile、runtime Contract Views、source concurrency、未導入の演算子拡張、re-export、動的generic格納、永続object cache等、SPECが明示的に将来事項とする機能は今回の必須完了範囲に含めない。未実装と未設計を区別する。
- 初回計画調査の成果物はこのファイルだけで、製品ソース等は変更していない。これは調査時の記録であり、以後のImplementationの編集制限ではない。以後はautomation/の段階別編集範囲を適用する。
- AGENTS.mdに従い、性能・割り当てを各段階で確認する。NativeAOTのpublish／テストは実行していない。将来の実行も明示的な指定がある場合に限る。

## 2. 調査結果

### 2.1 調査範囲と方法

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

### 2.2 実行した検証

環境: Windows x64、.NET SDK 10.0.401。既存のローカル依存を使い、restoreや製品公開は行っていない。

| 検証 | 今回の結果 |
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
| E03 | [ScalarTypes.cs](Kimi/Compiler/ScalarTypes.cs)、[ReferenceTypes.cs](Kimi/Compiler/ReferenceTypes.cs) | scalar実行はboolと64bitまでの10整数型。i128/u128、float、charは全実行経路がない。参照の実行形はref/stringのみで独立結果に限定 |
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

## 3. 計画の読み方・共通完了条件

IDは一意で、表の上から依存関係順に並べる。同一段階でも先行IDを参照する。依存欄は直接の前提であり、その依存先の前提も引き継ぐ。独立項目は順序を入れ替えてよい。

自動実装では状態・IDの表形式を維持し、任意・条件付き項目だけOPTIONAL-接頭辞を使う。runnerは必須[ ]が残る完成報告や存在しないtask_idsを拒否する。計画・監査は全体を毎回作り直さず、着手済み作業、前回の指摘、次の2実装枠の依存範囲を優先する。現在の操作はSTATUS先頭の引継ぎに保存する。検証起動の注意は[verification-guide](automation/verification-guide.md)を参照する。

[x] は記載した限定範囲について今回の実装・検証を確認済み。[ ] は部分実装を含めて未完了。一つの関数ではなく、独立した受け入れ条件を持つ機能単位で分割した。

各未完了項目は、個別条件に加えて次を満たすこと:

1. 対象の正常系を通し、不正使用を所定位置で診断する。合法機能のUnsupported解除だけで完了としない。
2. 関連する未使用body・到達不能枝・再解析・失敗後再実行でも検証が成立する。
3. runtimeを変更する場合はIR verifierとO0/O2実行で値、stdout/stderr、終了状態、評価・cleanup順を比較する。借用・所有権は失効、部分初期化、二重破棄、Abort／非終了を含める。
4. 既存の近傍テストと必要な統合テストを通し、設定上の警告・失敗を増やさない。コード量、割り当て、ピークメモリに関係する変更は代表測定を行う。
5. その実装作業時にSPEC/STATUSを必要に応じ更新する。本計画作成時には更新しない。

## 4. 既存の確認済み基盤

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [x] BASE-01 | .NET 10の4プロジェクトをDebug/Releaseで再ビルド | 両構成で警告0・エラー0 | なし | §2.2のRebuildログ |
| [x] BASE-02 | 現在のmanaged回帰suiteとfixture生成 | 両構成3,813成功、失敗・skipなし | BASE-01 | 全xUnit実行。失敗期待テストも内容確認 |
| [x] BASE-03 | 現行のUTF-8/source identity、token、indent、directive選択、Koto round-trip基盤 | 既存Lexing/Parser/serializationテストが成立。全仕様の意味解析までを含めない | BASE-02 | UnicodeIdentifier、SourceEncoding、DirectiveConditionValidation、ParserRegression、KotonohaSerialization各テスト |
| [x] BASE-04 | 現行のname/type/call/contract/property/startupの限定Binding | 既存の正常・不正・再Bindケースが成立 | BASE-03 | Binding、TypeBinding、ContractBinding、PropertyBinding、StartupBinding各テスト |
| [x] BASE-05 | bool・10整数型・Unit、通常直接call、制御フロー、owned string・比較・guard・一時ref/string引数 | 対応範囲のIR、借用・cleanup計画と実行fixtureが生成できる | BASE-04 | Scalar/Integer/Function/Result/Match/Guard/String*/ReferenceEmissionテスト。E02–E05の制限を維持 |
| [x] BASE-06 | owned tuple/固定配列の全体構築・Copy/Move・破棄 | 現行35 aggregateテストが成立。projection、ABI、選択結果は含めない | BASE-05 | AggregateEmissionTestと生成fixture |
| [x] BASE-07 | 既存Windows runtime/helper、CLI build/run、LSP基本通信 | 今回の通常native68件とscalar系1,860実行、CLI、LSPの検証成功 | BASE-05 | §2.2の既存script。930 fixtureは今回のmanagedテストで再生成済み |
| [x] BASE-08 | 警告・未使用・宣言だけの機能の横断調査 | E01–E13をコードとテストで確認し、解析の限界も記録 | BASE-02 | source inventory、IDE0051/0052 verify、登録・呼出箇所確認 |

## 5. 仕様境界と開発基盤

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] SPEC-01 | SPEC本文・Appendix A/Fと実装段階の要件台帳を維持する | 全必須規則に実装IDと正負テストを対応付ける。将来事項を必須実装へ混入させない | BASE-08 | §12の章対応を各節・文法へ展開し、未対応gateの漏れを確認 |
| [ ] SPEC-02 | 採用済み契約をSPEC本文へ統合し、未決定事項を分離する | Composition Root/Entry、言語test機能はSPECが明示採用する文書に従って統合。外部配布・永続化の具体形式、未定義Core API等は不足ごとに確定済み・未決定・対象外を記録し、未採用案を暗黙採用しない。無関係な実装は止めない | SPEC-01 | 本文・文法・コード例で独立して受入テストが書けるか確認。採用元と本文の対応、未決事項の影響・解除条件、曖昧な節参照も確認 |
| [ ] MAINT-01 | E11の未使用・未接続debug経路と実験コードを整理する | unitContext、dump、不要async/usingを削除・接続・明示隔離のいずれかで処理。generator/DI登録経路を壊さない | BASE-08 | 参照・生成コード確認、IDE解析、全build、CLI/LSPの起動終了と割り当て比較 |
| [ ] MAINT-02 | CIで構成・対象・結果を一致させる | PR検証、Debug/Releaseの明示、Windows通常native検証、テスト件数0の検知、ログ保存を接続する | BASE-07, MAINT-01 | clean環境のworkflowを実行。LinuxのmanagedとWindows nativeを区別し、NativeAOTは勝手に追加しない |

## 6. フロントエンド・意味解析・所有権

### 6.1 型と宣言

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] FRONT-01 | SPECの確定構文とKoto/source round-tripの残差を解消 | 全規定構文のsource span、child ownership、回復、再serializationが保たれる。構文受理と意味合法性を分離 | SPEC-01, BASE-03 | Appendix F対照、現行parse suiteに不足する組合せ・破損入力追加 |
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
| [ ] OWN-02 | storage borrowと初期化を維持するexchangeを実装 | §15.7の操作が未初期化/部分状態を露出せず、aliasと排他権限を検証する | OWN-01, BORROW-03 | self/別slot、失敗・途中移動、外部参照、初期化状態の正負テスト |
| [ ] OWN-03 | user deinitを含む完全/部分aggregateのcleanup計画 | 論理逆順、base層、defer、parameter、結果確保の順を守り、未完成層のdeinitを呼ばない | OWN-01, BORROW-04 | §16、条件付き破棄、部分構築、早期return、Abort/非終了、破損計画拒否 |

### 6.3 制御フローとgenericの定義側

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] FLOW-01 | 全対応型の結果・require・非終了・未実行経路の共通検証 | 構造上の完了とruntime到達を分け、cleanupが返らない経路で結果を捏造しない | OWN-03, BIND-02 | Appendix A.15、選択/label/yield/外向きtransfer、未使用body・Never後の診断 |
| [ ] FAIL-01 | 明示AbortとOption/Result系の構文上の伝播計画 | §17の明示Abort、伝播先、値確保/cleanup順を確定し、暗黙算術失敗と区別する | FLOW-01 | explicit macro、require失敗、Never、誤った結果型、診断位置。enum実行はCORE-01で検証 |
| [ ] GEN-01 | generic bodyの普遍検証と正当なDeferred Obligation | §8.10の全許容代入で型・操作・効果・Loanが合法。好都合な使用例やspecializationで定義エラーを救済しない | BIND-03, TYPE-02, BORROW-04, FLOW-01 | 未使用generic、Copy/Move両分岐、body-only型形成、公開条件と表現失敗の区別 |
| [ ] GEN-02 | 明示full specializationの閉集合と静的実装選択 | 定義側で全候補を検証し、Origin消去/LengthKey、継承契約、曖昧さ、参照・共有callerからの選択を固定 | GEN-01 | §8.8、未使用specialization、重複key、追加削除、通常bodyへ不正fallbackしないテスト |

## 7. 表現・生成・runtime

### 7.1 数値と共通配置

この段階のNUM項目は、意味解析の拡張全体を待つ必要はない。記載したBASE依存が満たされれば独立して実装できる。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] NUM-01 | i128/u128の値計画・演算・直接callを実装 | 格納だけでなく比較、算術、除算、shift、checked overflowが動く。追加helperは供給・未定義symbol検証を伴う | BASE-05 | Integer/Division/BitwiseEmissionの128bit境界、最小値/-1、shift範囲、O0/O2 |
| [ ] NUM-02 | f32/f64の値計画・演算・比較・callを実装 | 選択精度、NaN、±0、rounding、FP環境をSPEC §§3/13/21.5.4通り維持 | BASE-05 | 正確literal fitting、非有限値、比較、呼出境界、O0/O2とLLVM属性 |
| [ ] NUM-03 | charの値計画・格納・比較・callを実装 | Unicode scalarを保持し、surrogate・上限外を規定通り拒否 | BASE-05 | CharLiteralParseを実行まで拡張、NUL/非BMP/境界/不正値 |
| [ ] NUM-04 | 数値・char・Semanticsを混同しない明示変換を完成 | §§10/13.5の変換可否と範囲検査が全対応primitiveで一致。literal fittingとruntime変換を分離 | NUM-01, NUM-02, NUM-03, TYPE-01 | ConversionEmissionの型行列、端点、負数→unsigned、NaN/overflow、定数と変数 |
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
| [ ] OBJECT-01 | 継承・override・ObjectCompatibleの共通保証を完成 | base/derivedの静的適合、receiver調整、共通実装familyの効果証明を提供 | STRUCT-01, PROP-02, BIND-03, BORROW-04 | §§6.2/12.4.4、protected receiver、inherited conformance、保証の追加/撤回、未使用override |
| [ ] OBJECT-02 | objの確保・payload構築・descriptor・破棄を実装 | 全modeに共通する16-byte header、payload+16、alignment16と完全動的型の破棄/解放を接続 | OBJECT-01, META-01, OWN-03 | §§13.5/21.2、部分構築、over-alignment、失敗location、base viewを誤解放しない |
| [ ] OBJECT-03 | rc/arcの取得・共有・release protocolを実装 | checked count、clone/move、最後のstrong解放、atomic/non-atomic差と順序規約を保証 | OBJECT-02, BORROW-04 | count上限、コピーとcloneの区別、別handleのpayloadアクセス、arc順序と生成IR、O0/O2 |
| [ ] OBJECT-04 | Weak side table・upgrade/downgrade・循環構築を実装 | strong0の非復活、移行・guard・最後のweak解放、構築中の公開禁止、解放後header非参照を保証 | OBJECT-03, CALLABLE-03 | §21.2.3、upgrade対final release、移行競合、builder失敗、弱メモリ順序。source並行機能は追加しない |
| [ ] OBJECT-05 | object view・upcast・is/as・refinementを実行へ接続 | token/Supports/base調整、検査失敗、Origin保存、requireによる有効型を保証 | OBJECT-03, BORROW-04, FLOW-01 | RuntimeTypeTestをnativeへ。静的/動的型の差、shadow/アクセス、借用終了、再代入によるrefinement失効 |
| [ ] OBJECT-06 | object receiverのmethod/Property/generic操作を統合 | 直接/間接選択とreceiver取得を保ち、具体Coreのobject利用を完成。未導入runtime Contract Viewへ拡張しない | OBJECT-05, PROP-03, GCODE-03 | 受取Semantics行列、継承receiver、associated Type、dynamic destructor、invalid witness |

## 8. Core・文字列・コレクション・反復

catalogの「18枠が埋まった」だけでCore完成としない。SPEC §22.1と関連節の各宣言・操作・配置・所有権・実行を確認し、現catalogにない確定済みのobject/Weak API等も対象に含める。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] CORE-01 | Option/Resultのgeneric実行と成功/失敗/absenceの伝播 | 宣言だけの状態からCase構築・match・結果伝播・cleanupまで接続 | AGG-03, GCODE-05, FAIL-01 | §17.2、nested結果、失敗payload、Non-Copy、早期return、destructor非終了 |
| [ ] CORE-02 | 確定済みStringify・文字列補間・動的文字列構築を実装 | UTF-8長・確保・取得・破棄・format順・失敗位置を定義通り維持。未定義の連結所有権はSPEC-02で先に解決 | CORE-01, NUM-04, GCODE-03, SPEC-02 | 補間nested評価、日本語/NUL、長さoverflow、Heap文字列再代入、途中失敗 |
| [ ] SEQ-01 | Index/Range/ResolvedRangeと固定配列metadataを実装 | range解決・bounds・長さ・indicesの型と検査を保持し、metadata readで不要Loanを作らない | TYPE-02, META-01, GCODE-03 | §4.6、空範囲、端点、負値、overflow、一回評価、保存index |
| [ ] SEQ-02 | 固定配列の値/借用/型依存要素読み取りを完成 | contextual literal、index/range、SharedReadResult、要素Loan、static Move制限を実行で保存 | SEQ-01, AGG-01, BORROW-05, GCODE-05 | Copy/Non-Copy/obj/uniq要素、N0、nested、借用中の書込/置換、O0/O2 |
| [ ] SEQ-03 | Arrayの格納・構築・容量・変更操作を実装 | §4.7のgrow/移送/挿入/削除/clearと状態・失敗・破棄順を保つ。capacityと初期化要素を分離 | SEQ-02, CORE-01, META-01, OWN-02 | 再配置の重なり、容量境界、zero-size、要素借用、途中失敗、allocator失敗注入 |
| [ ] SEQ-04 | Sliceの範囲・要素依存・再借用・返却を実装 | fixed/Array由来のviewがowner lifetimeと結び付き、権限・Origin・zero-sizeを保持 | SEQ-03, BORROW-05, OBJECT-03 | §4.6、部分範囲alias、排他slice、結果Loan、owner Move/破棄との競合 |
| [ ] SEQ-05 | Dictionaryのkey/value操作・順序・取得/破棄を実装 | SPECにある検索・更新・容量・key適合・値返却と逆論理順cleanupを満たす。未確定APIはSPEC-02の解決を前提 | SEQ-03, BIND-03, CORE-01, SPEC-02 | key重複/衝突、置換、value/key破棄順、borrow中変更、失敗と容量限界 |
| [ ] ITER-01 | Iterator/Iterable・forの共通意味計画と生成を完成 | 検証済みwitness、receiver効果、case lifetime、continue/exit/deferを統合。保持できない要素借用をescapeさせない | SEQ-04, SEQ-05, GCODE-03, BIND-03, FLOW-01, SPEC-02 | §14.6/4.6、range/配列/collection、Copy/Non-Copy要素、nested for、早期終了 |
| [ ] CORE-03 | 比較Contract等を含む必須Core全体の宣言・実装監査 | 全規定Core名が正しいidentity・signature・capability・runtimeへ接続。Missingや同名偽Coreで穴埋めしない | CORE-02, ITER-01, OBJECT-04, OBJECT-06 | CoreCatalog/CoreModel/IntrinsicCapabilityと各操作の正負・改変拒否・再Bindテスト |

## 9. Unsafe・外部連携・ビルドライフサイクル

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] UNSAFE-01 | raw pointerの比較・算術・dereference・変換を実行へ接続 | §5のsigned offset、stride、初期化、provenance、明示unsafeを保ち、安全な借用権限を破壊しない | NUM-04, LAYOUT-01, BORROW-04 | null、one-past、zero stride、負offset、ptr/integer変換、alias属性の過剰付与なし |
| [ ] FFI-01 | LibraryImportの宣言検証・C ABI・呼出・依存記録を実装 | §22.3の許可signature・unsafe・呼出規約・外部symbol一致・link入力を確認。未導入aggregate passing等は拒否 | UNSAFE-01, ABI-01, BIND-02 | native fixtureで引数/結果/FP状態、誤DLL/symbol/signature、cleanup・unwind境界 |
| [ ] MODULE-01 | 外部source Kotonohaを実際にloadしidentityを管理 | 設定だけのKotonohaArrayから実module graphを作り、直接依存alias・version・source所有者・失敗を明示 | SPEC-02, BIND-01 | ローカルfixture module、欠落/重複/異版、循環、読込順、間接依存の名前漏れ |
| [ ] MODULE-02 | moduleを跨ぐ最終Binding・閉じた選択集合・private依存を接続 | import先のgeneric/contract/Originを保持し、依存追加削除・変更で適切に再検証する | MODULE-01, BIND-03, GEN-02 | §18/21.3.4、同名identity、private helper、specialization追加削除、アクセス拡大/撤回 |
| [ ] MODULE-03 | Libraryの検査用IR/object出力とApplicationへの生成依存を完成 | E02のLibrary一律拒否を解除。Libraryにはstartupを作らず、検証対象bodyを残す。外部安定ABIは約束しない | MODULE-02, GCODE-03, FFI-01 | Application/Library対照、unused body不正、entry/linkage、IR verifier、object symbol |
| [ ] MOD-01 | Mod登録・provisional query・snapshot・実行順を実装 | §20.7の決定的登録、scope別照会、失敗/cancellation、変更禁止境界を実現 | SPEC-02, MODULE-02, BIND-03 | 登録重複、順序、未知照会、selected/excluded syntax、失敗・再実行 |
| [ ] MOD-02 | 生成sourceの追加・統合・final再検証・無効化を実装 | 生成sourceのidentity/provenanceを保持し、古い出力の残留、二重生成、無検証公開を防ぐ | MOD-01, LAYOUT-01, GEN-01 | §20.7、断片追加/削除、生成診断位置、Mod入力変更、古いキャッシュ拒否、列挙順不変 |
| [ ] CACHE-01 | 検証済み意味計画だけの永続再利用を実装 | 実際に読んだ依存・不存在・proof・source対応を照合。使用時Loanは再検証し、ABI/context/frame/IRは再生成 | MODULE-02, MOD-02, GCODE-03, SPEC-02 | §21.3.4、cache有無/壊れた内容/版変更/原文位置/target変更/mだけ変更、変更しない依存の再利用 |
| [ ] COMPOSE-01 | SPEC本文で確定したroot/Entry/Providerの宣言・接続検証 | 非置換root、限定参照、静的適合、一意接続を本文で確定した構文と診断で実現 | SPEC-02, MODULE-02, BIND-03, OBJECT-01 | 定義不足/重複/不適合/不正参照、Provider非依存Libraryの検証。採用元の例だけで済ませず統合後の規範からテストを導く |
| [ ] COMPOSE-02 | 最終接続と生成依存・予算・Core出力経路を統合 | 接続変更に影響されるentry/context/inlineだけを再生成し、必要時は保守的失効。現行Core.writeLineを確定した接続へ適応 | COMPOSE-01, MODULE-03, CACHE-01, GCODE-04, CORE-03 | Provider変更、接続不足、再利用、source location、同入力の再現性、誤った古い生成物拒否 |
| [ ] BUILD-01 | 複数source/module、static、Modを含むbuild/runの原子性を完成 | failed buildで旧成功を誤認せず、取消し・部分生成・外部依存変更・出力pathを一貫処理 | COMPOSE-02, STATIC-01, MOD-02, FFI-01 | NativeToolchain/EmissionArtifacts/ToolchainResolverとCLI、失敗注入、空白path、hash/ABI不一致 |

## 10. 言語テスト・LSP・エディター拡張

LSP/拡張の改善は、多くのruntime機能の完成を待たず進められる。以下の依存欄が着手条件となる。補完・rename等、現状advertiseしておらずSPECにも要求のない新機能は追加要件として扱う。

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] LANGTEST-01 | 本文で確定した言語test構文・発見・Bindingを実装 | SPEC冒頭に名前だけある拡張を、SPEC-02で確定した文法・mode・診断に従って扱う | SPEC-02, MOD-02, GEN-01 | 通常mode/test mode、製品→test依存禁止、発見順・誤用、対象選択のテスト |
| [ ] LANGTEST-02 | kimi testの実行・失敗報告・成果物再利用を実装 | 本文で確定した隔離・報告上限・停止・再利用を満たし、製品とtestの生成要求・予算を混ぜない | LANGTEST-01, BUILD-01, GCODE-04 | 成功/失敗/Abort/非終了、取消し、cache、test-only代入追加で製品entry/schema/frame/予算が不変 |
| [ ] LSP-01 | 現行LSP通信の境界・取消し・resource lifetimeを検証し補完 | framing、分割入力、不正payload、open/change/close、版番号、shutdownを処理し、pool/lock/taskを解放 | BASE-07, MAINT-01 | stream単体テスト、UTF-16位置、複数変更、サイズ上限、EOF/取消し。既存scriptの成功だけで完了としない |
| [ ] LSP-02 | document snapshotから実コンパイラー診断へ接続 | Parse/Bindingの診断を正しいURI/範囲/版へ公開し、古い解析結果が新しい版を上書きしない | LSP-01, BIND-01, FRONT-01 | malformed/修正済source、他file依存、rapid change、close時消去、取消しと診断重複 |
| [ ] EDITOR-01 | KimiCodeの起動設定・lifecycle・配布構成と統合テスト | 固定Debug DLL/DebugWaitを通常利用から外し、設定されたserverを起動。client開始終了を依存APIに合わせ検証 | LSP-02 | node構文、拡張テスト、file/untitled、server欠落・再起動・deactivate、package内容確認 |
| [ ] QUALITY-01 | compiler/LSP/Coreの性能・保持参照・未使用経路を継続監査 | 必須処理を削らずallocation/peak memory/compile time/code sizeを測定し、正当なcacheと死んだ経路を区別 | CACHE-01, CORE-03, EDITOR-01, GCODE-04 | 既存Benchmarkの拡張、warm/cold・失敗後再利用・大規模入力・GC回収、context bytes/frame量、IDE解析 |

## 11. 完了判定と配布検証

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] ACCEPT-01 | 規範仕様と全実装段階の残差を閉じる | §12とSPEC要件台帳の必須行が全て実装・正負テストへ接続。合法入力のUnsupported/Pending/Missingが残らず、将来事項は明示拒否/対象外を維持 | SPEC-01, TYPE-02, OWN-02, AGG-04, GCODE-05, OBJECT-04, OBJECT-06, CORE-03, BUILD-01, LANGTEST-02, QUALITY-01 | Appendix A全項目と文法の横断、非実行body、不正計画、故障注入。拒否テスト削除だけで通過させない |
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
| §3 Type/取得/一時値 | TYPE-01, NUM-01, NUM-02, NUM-03, NUM-04, BORROW-01, BORROW-05, OWN-01 |
| §4 配列・長さ・Slice・collection | TYPE-02, AGG-01, GCODE-05, SEQ-01, SEQ-02, SEQ-03, SEQ-04, SEQ-05 |
| §5 unsafe/pointer | UNSAFE-01, FFI-01 |
| §6 宣言・struct・enum・Attribute | FRONT-01, BIND-01, STRUCT-01, AGG-03, OBJECT-01, FFI-01, MOD-01 |
| §7 関数・capture | BIND-02, ABI-01, CALLABLE-01, CALLABLE-02, CALLABLE-03 |
| §8 generic/static Contract | BIND-03, GEN-01, GEN-02, PROP-03, GCODE-01, GCODE-02, GCODE-03, GCODE-04。§8.5の未導入View構文は対象外 |
| §9–10 名前・可視性・推論 | TYPE-01, BIND-01, BIND-02, MODULE-02, BORROW-05 |
| §11 Property | PROP-01, PROP-02, PROP-03 |
| §12–13 式・演算・assignment・object操作 | NUM-04, OWN-02, AGG-01, OBJECT-02, OBJECT-03, OBJECT-04, OBJECT-05, OBJECT-06, CORE-02 |
| §14 制御・for・match・refinement/require | FLOW-01, AGG-02, AGG-04, ITER-01, OBJECT-05, FAIL-01 |
| §15–16 Origin/Loan/cleanup | ORIGIN-01, BORROW-01, BORROW-02, BORROW-03, BORROW-04, BORROW-05, OWN-01, OWN-02, OWN-03 |
| §17 失敗・警告 | FAIL-01, CORE-01, MAINT-02, ACCEPT-01 |
| §18 module/依存 | MODULE-01, MODULE-02, MODULE-03, CACHE-01。外部仕様依存はSPEC-02 |
| §19 directive | BASE-03, FRONT-01, MOD-02 |
| §20 build/Mod/LLVM/native | MOD-01, MOD-02, BUILD-01, FFI-01, MODULE-03, MAINT-02 |
| §21 layout/metadata/generic/ABI/runtime | LAYOUT-01, ABI-01, META-01, GCODE-01, GCODE-02, GCODE-03, GCODE-04, GCODE-05, CALLABLE-03, OBJECT-02, OBJECT-03, OBJECT-04 |
| §22 Core/startup/FFI/Windows | CORE-01, CORE-02, CORE-03, STATIC-01, FFI-01, BUILD-01 |
| Appendix A 検証義務 | 各機能の検証欄、ACCEPT-01, ACCEPT-02 |
| Appendix B 実装指針 | GCODE-04, QUALITY-01。参考algorithmを言語の追加受理条件にしない |
| Appendix C 実装状況 | BASE各項目、ACCEPT-03 |
| Appendix D/E/F 将来範囲・用語・文法 | SPEC-01, SPEC-02, FRONT-01, ACCEPT-03 |
| 冒頭のComposition/test参照 | SPEC-02, COMPOSE-01, COMPOSE-02, LANGTEST-01, LANGTEST-02。明示採用された参照先を本文へ統合し、未決事項と分離 |
| 言語仕様外の既存製品部品 | MAINT-01, MAINT-02, LSP-01, LSP-02, EDITOR-01, QUALITY-01 |

完了の根拠はチェック数ではなく、選定した必須仕様を実ソースと実行結果で満たしたことである。新たな不具合・仕様不足が見つかった場合は、該当IDの条件を具体化し依存順を維持して更新する。
