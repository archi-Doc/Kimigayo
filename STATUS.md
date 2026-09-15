# Kimigayo Implementation Status

更新: 2026-09-15。基礎調査対象: `e861ce5ebf7c365f8e8416e0ed692500eb9b1f42`。集成型の選択結果・関数ABI・Copy要素読み取りに続き、Copy要素への単純代入と評価順序の修正を§4.4・§7に反映。

文書構成（2026-09-14）: [SPEC.md](SPEC.md) を総合目次とし、本文22章と付録A・B・D・E・Fを `spec/` に分割した。付録Cは目次内の実装状況案内に集約。設計・決定・変更記録の `doc/` は `draft/` に改名した。章番号・仕様本文・既存の優先規則を維持し、コンパイラーの実装範囲は変更していない。

依存・成果物仕様の統合（2026-09-15）: 指定された draft を優先し、[第18章](spec/18-modules-and-dependencies.md)へ設定・解決・lock・入力記録・ソースパッケージ・pack/publish・意味検証の再利用・テスト所属を統合した。[第20章](spec/20-compilation-configuration.md)の native 要求/供給・member閉包・directive・CLI、[第21章](spec/21-layout-runtime-and-code-generation.md#2137-product-and-test-generation)の共通生成と製品/テスト予算、関連付録・目次も整合させた。英語の規則と例を整理し、旧「形式・設定は未定義」の記述やdraftへの本文委譲を置換した。全28仕様ファイルの681件のローカル参照とコードブロック・差分形式を確認した。文書のみの変更で、外部依存解決・pack/publish・永続意味キャッシュの実装完了を示さない。draftとcompilerは変更せず、compiler/native/NativeAOTテスト・性能測定は行っていない。

現ソースと対応テストで確認した範囲を記す。字句/構文、Binding、所有権、LLVM生成、native実行は別段階であり、前段の対応だけで実行可能とはしない。仕様は [SPEC.md](SPEC.md)、全体計画は [PLAN.md](PLAN.md)。旧C.*リンクは対応分野へ接続する。

receiver省略記法（2026-09-15）: instance関数・Contract関数requirementの `self` を `self: ref/Self` としてBindingし、instance Propertyの `get()` / `set(value: T)` はそれぞれ `ref/Self` / `uniq/Self` のreceiverを補完する。stored custom・computed・明示Contract accessorに対応し、group/rootgroupのaccessorはreceiverなしのまま。明示receiver、引数位置、Origin補完、通常関数のreceiver省略によるtype function判定を維持する。§7.3・§11.2・構文付録を更新。ReceiverShorthand / PropertyRevisionParse / PropertyBinding / FuncDeclarationParse / InheritedReceiverBinding / ContractBinding / TypeBinding / KotonohaSerializationの関連390件が成功し、省略形のparse/write/parse・保存/再読込・再Bindを確認。省略形を含むwarm Bindingの追加割り当ては0 bytes。一般accessor呼出の式検査・所有権・生成やspecialization全体の既存制限は継続し、この変更はそれらの実行対応を拡張しない。NativeAOTテストは実行していない。

<a id="c1-coverage-summary"></a>
<a id="c3-lexical-forms-and-types"></a>
<a id="c8-specification-review-integration-2026-09-08"></a>
<a id="c9-follow-up-specification-review-items-16"></a>
<a id="c10-type-and-literal-review-items-314"></a>
<a id="c14-tokenizeparse-increment-2026-09-09"></a>
<a id="c15-property-and-move-syntax-update-2026-09-10"></a>
<a id="c32-compile-time-switch-spelling-2026-09-12"></a>

## 1. 字句・構文・コンパイル条件

| 確認した実装 | 制限・根拠 |
| --- | --- |
| UTF-8 source、元位置/診断、固定Unicode 15.0の識別子・NFC検査、インデント/括弧/継続行・回復 | [Lexing](Kimi/Compiler/Lexing)、[SourceDocument](Kimi/Compiler/Core/SourceDocument.cs)、UnicodeIdentifier / SourceEncoding / ParserRegression各テスト |
| 型・generic/length/Origin、宣言/Property/accessor、capture、literal/collection、式・制御フローのKoto構築とparse/write/parse・保存/再読込 | [Parsing](Kimi/Compiler/Parsing)、FrontEndSyntax / PropertyRevisionParse / NestedTypeParse / KotonohaSerialization。構文の存在は意味解析・実行の完成を示さない。source artifactとportable交換形式も別 |
| 数値の原文・exact magnitudeを保持し、float literalは対象精度へ直接丸める。文字/文字列escapeを検査 | [NumberLiteralHelper](Kimi/Compiler/Helper/NumberLiteralHelper.cs)、[FloatingTypes](Kimi/Compiler/FloatingTypes.cs)、NumberLiteral / CharLiteralParse / StringLiteralParse |
| `#if`・`#switch`の選択・条件検査。条件Nameはcase-sensitive、完全一致の重複/組込み衝突を拒否 | [Compilation](Kimi/Compiler/Core/Compilation.cs)、[条件評価](Kimi/Compiler/Parsing/BasicValue/CompileTimeConditionEvaluator.cs)、CompilationSpecification / DirectiveConditionValidation。短絡/非選択Caseの検査とfalse `#if`の除外境界を区別 |

現構文は`#switch`、Property/accessor、通常の型適応。旧`#match`・専用`@move`は互換構文にしない。`alias`はContainerを開き、型別名は導入しない。

<a id="c4-declarations-and-compile-time-directives"></a>
<a id="c5-properties"></a>
<a id="c16-initial-binding-pipeline-2026-09-10"></a>
<a id="c17-complete-type-representation-and-declaration-origins-2026-09-10"></a>
<a id="c18-constraint-proof-foundation-and-declaration-validation-2026-09-11"></a>
<a id="c19-core-intrinsic-identities-and-copy--owned-2026-09-11"></a>
<a id="c20-static-function-contracts-and-associated-types-2026-09-11"></a>
<a id="c21-property-conditional-conformance-and-inherited-member-binding-2026-09-11"></a>
<a id="c26-enum-case-construction-and-coreoptionresult-2026-09-11"></a>
<a id="c28-positional-pattern-binding-and-match-coverage-2026-09-11"></a>
<a id="c33-runtime-type-test-binding-2026-09-12"></a>

## 2. Binding・型・Core

現 [Compilation.Bind](Kimi/Compiler/Core/Compilation.cs) はfinal Bindingを一回実行し、再Bind時に古い意味情報と所有権の確認結果を無効化する。Koto上の型/Symbol/呼出計画と再利用可能な表を使い、別のBound treeは作らない。

| 分野 | 確認した範囲 | 残る制限 |
| --- | --- | --- |
| 名前・呼出 | Type/Value分離、source-local scope/alias、前方関数、Core identity、可視性、positional/named/default対応、通常generic推論・候補選択 | 完全なアクセス/断片検証、default実行、general Origin推論、specialization、constructor/deinit、間接callは未完成 |
| 完全Type・Origin | nested Semantics、owner正規化、Tuple/関数/固定配列、generic pair/slot、宣言Origin・input/static/intersection、代入/variance・保持借用の表現 | bound証明、一般Origin/Loan solver、SemanticsTargetの一般適用、定数参照・length推論は未完成。未解決義務を保持 |
| Constraint・Contract | Proven/Refuted/Unknown/Error、宣言前提、associated Type、refinement、検証済みwitness、conditional conformance/member、継承receiverの選択 | Unknownは成功にしない。完全な候補同値性・generic body/効果証明・ObjectCompatibleのbody/callee検証は未完成 |
| Property | stored/computed/requirement、accessor型・権限・Copy・Origin対応、operation別witness | 一般式のread/get/set/init・receiver/cleanup・生成は未完成 |
| enum・Pattern | Case identity、expected Type付き構築、payload取得、Tuple/Case/全体Pattern、網羅性・包含警告、guard candidateとbody bindingの分離 | 借用Subject・一般分解/guard・genericの証明/生成は未完成。警告対象armも検査 |
| runtime `is` / `is not` | concrete struct Core上のobject SemanticsをboolへBindingし、元operand/target・shared要求を保持 | Flow Type refinement・object Loan・実行は未対応 |
| Core | Copy・Owned・Callable・writeLine・Option・Resultの6宣言を検証。Copy/Owned導出とCore同名偽装拒否 | catalog全18枠中12枠Missing。Option/Resultの宣言・解析はgeneric runtime完成を意味しない |

根拠: [Binding](Kimi/Compiler/Binding)、[CoreIntrinsics](Kimi/Compiler/Binding/CoreIntrinsics.cs)、[CoreCatalogTest](xUnitTest/Tests/CoreCatalogTest.cs)、TypeBinding / ConstraintBinding / ContractBinding / PropertyBinding / ConditionalConformanceBinding / InheritedReceiverBinding / EnumBinding / PatternBinding / RuntimeTypeTest。

<a id="c7-control-flow-and-failure-handling"></a>
<a id="c24-whole-place-ownership-cfg-and-cleanup-plans-2026-09-11"></a>
<a id="c27-enum-construction-ownership-and-ordered-cleanup-2026-09-11"></a>
<a id="c29-owned-match-acquisition-decomposition-and-cleanup-2026-09-11"></a>
<a id="c30-current-control-flow-syntax-results-and-cleanup-2026-09-12"></a>
<a id="c31-transfer-seeded-unreachable-ownership-checking-2026-09-12"></a>

## 3. 制御フロー・所有権

[Analysis](Kimi/Compiler/Analysis) はBindingの取得計画を使い、whole Placeの初期化・Move・代入履歴をCFG固定点で検査する。条件付き置換/破棄、局所値・一時値・引数・結果、concrete enum構築/分解、Tuple/固定配列の全体責任を扱う。一般のfield/index・部分Move・user deinitは未完成。

- `if`・短絡・`while`・`do`・`loop`・label・`require`・`match`・return/yield/exit/continue・deferを解析。構造上の完了と実行到達を区別し、結果を確保してからcleanup、正常完了後だけ届ける。Abortはcleanupしない。
- 破棄は論理的な逆順。途中構築や引数取得が通常transferで中断した場合、取得済み責任を処理する。非終了cleanup後の結果/後続破棄を作らない。
- 明示transfer後のsource検査は実行CFGと別の継続で行う。Never Subject/非終了guardも対象。一般Never呼出・exitless loop・cleanup阻害後など、検査開始状態を作れない未到達操作にはUnsupportedが残る。
- whole Subjectのguardはsource順の保守的な検証経路を持ち、false側の副作用も後続armへ渡す。実行時に省略するcovered armも診断する。
- Loanはstring比較、shared引数、一時string、string guard candidate、Tuple/固定配列の要素アクセス時の親ストレージ保護の限定範囲。後続引数/guard/cleanup中もownerを保護し、正常結果確保後または通常transfer時に終了する。Copy要素代入は位置取得用のLoanだけをstore直前に終了する。一般の参照保存/返却、uniq/reborrow、効果要約、借用Subjectは未完成。

根拠: OwnershipAnalysis / EnumOwnership / MatchOwnership / UnreachableOwnership / CurrentControlFlow / ControlFlowConformance / ReferenceEmission / StringGuardEmission各テスト。解析成功後も生成側の対応範囲を別途検査する。

<a id="c6-expressions-and-operators"></a>
<a id="c12-first-executable-milestone"></a>
<a id="c23-startup-selection-and-corewriteline-binding-2026-09-11"></a>
<a id="c34-minimal-literal-output-executable-2026-09-13"></a>
<a id="c38-compiler-pipeline-and-emission-preparation-2026-09-13"></a>
<a id="c39-compiler-review-dead-code-removal-and-emission-restructuring-2026-09-13"></a>
<a id="c40-scalar-control-flow-emission-and-nativeaot-2026-09-13"></a>
<a id="c41-scalar-selection-and-loop-results-2026-09-13"></a>
<a id="c42-deferred-cleanup-execution-2026-09-13"></a>
<a id="c43-checked-i32-division-and-remainder-2026-09-13"></a>
<a id="c44-scalar-functions-return-and-explicit-main-2026-09-13"></a>
<a id="c45-i32-bitwise-operations-and-checked-shifts-2026-09-13"></a>
<a id="c46-integer-execution-through-64-bits-2026-09-13"></a>
<a id="c47-explicit-integer-conversions-2026-09-13"></a>
<a id="c48-owned-string-locals-and-conditional-cleanup-2026-09-13"></a>
<a id="c49-owned-string-control-flow-results-2026-09-13"></a>
<a id="c50-owned-string-function-parameters-and-results-2026-09-13"></a>
<a id="c51-string-comparisons-and-backend-memcmp-2026-09-13"></a>
<a id="c52-unguarded-whole-subject-match-execution-2026-09-13"></a>
<a id="c53-copy-subject-match-guards-2026-09-13"></a>
<a id="c54-shared-string-arguments-and-referent-comparison-2026-09-13"></a>
<a id="c55-string-guard-candidates-and-temporary-shared-arguments-2026-09-13"></a>
<a id="c56-whole-tuple-and-fixed-array-execution-2026-09-13"></a>

## 4. LLVM生成の対応範囲

[LlvmEmitter](Kimi/Compiler/Emission/LlvmEmitter.cs) はwindows-x64-v1のApplicationに限定し、final Binding・startup・所有権を再検査する。暗黙のtop-level実行または適格な`public func main() -> ()`を選ぶ。外部module、宣言container、Library生成、未対応の未使用bodyも公開前に拒否する。

| 分野 | 現在の生成範囲 | 制限・根拠 |
| --- | --- | --- |
| scalar | bool、12整数型（i8/u8～i128/u128・isize/usize）、char、f32/f64、Unit。local・取得/置換・比較・直接引数/結果・既存制御フロー/aggregate payload | [ScalarTypes](Kimi/Compiler/ScalarTypes.cs)、Scalar / Integer / WideInteger / Char / FloatEmission |
| 整数演算 | checked add/sub/mul・符号付きneg・inc/dec、比較、bit演算、型独立のshift count検査、compound更新。64bitまでの除算/剰余は0・signed min/−1を検査 | i128/u128除算/剰余は初期profileの禁止。charはUnicode scalar順の比較のみで数値演算不可。Integer / Division / Bitwise / WideIntegerEmission |
| float | f32/f64算術・比較、対象精度のliteral、NaN・±0・subnormalを扱う値計画 | 数値変換全体とは別。float literal Patternは未対応。FloatEmission / NumberLiteralParse |
| 数値変換 | 整数12×12方向の型適応・範囲検査、direct integer literal fitting、float literalの対象精度へのfitting、f32→f64、float同型取得 | 実行時f64→f32、整数↔float、整数literal→float、一般同型取得、明示Semantics/略記は未対応。typed128bit↔float・char/bool数値変換は禁止。[Binding.Conversions](Kimi/Compiler/Binding/Binding.Conversions.cs)、Conversion / FloatConversionEmission |
| 関数・結果・cleanup | root/captureなしlocal関数、named引数、再帰、scalar/Unit/owned string/Tuple/固定配列の引数/結果、Never結果、選択・loop・match結果、defer | default・generic・明示Origin・capture・間接call・一般の集約ABIは未対応。[FunctionAbi](Kimi/Compiler/Emission/FunctionAbi.cs)、Function / Result / DeferredEmission |
| owned string | literal、local Move・self代入/置換、条件付き破棄、一時/選択/関数結果、writeLine、全6比較 | stringはStatic backingでもNon-Copy。UTF-8 byte列で比較。Heap構築のsource操作・補間/連結・明示所有権適応は未対応。String*Emission |
| shared string | 暗黙input Originの必須ref/string引数・転送、referent比較、一時owned stringのshared引数、string guard candidate | 参照はhandleへの1 pointer。結果は独立値だけ。local保存/返却・明示@ref・uniq・nested borrowは未対応。[ReferenceTypes](Kimi/Compiler/ReferenceTypes.cs)、Reference / StringGuardEmission |
| match/guard | bool・整数・char・Unit・owned stringの網羅的match、literal/wildcard/全体let/var/括弧Pattern、Copy候補・ref/string候補のguard | float Subjectは全体Pattern/guardの既存範囲。借用Subject・Tuple/enum分解実行は未対応。candidateはread-onlyでbody bindingと別。Match / Guard / Char / Float / StringGuardEmission |
| Tuple・固定配列 | 対応scalar・Unit・owned string・nested aggregateの構築、local全体Copy/Move/置換/条件付き破棄、if/do/loop/matchの結果配送・通常関数の値引数/結果、Tupleの数値selector・固定配列のisize添字によるCopy要素読み取り、初期化済みlocal varのCopy要素への単純代入・数値要素への複合代入・整数要素の前置/後置増減 | ownerのみ、深さ64、size/countはint.MaxValueまで。Non-Copy要素置換/取得・部分Move・借用引数/結果・集成型Subject・struct/enum生成は未対応。[AggregateLayout](Kimi/Compiler/Emission/AggregateLayout.cs)、AggregateEmission / AggregateResultEmission / AggregateFunctionEmission / ElementEmission / ElementAssignmentEmission / ElementUpdateEmission |

生成は検証済みの型付き値/CFG/ABI/cleanup計画から行う。WriterはASTを再解釈しない。型identity・定数・入力・支配・結果到着・生存flag・Loan・破棄計画の不整合を拒否する。同じLLVM幅でも別の言語型を混同しない。

stringは24-byte handleの各fieldを転送し、条件付き責任にのみflagを使う。Tupleは物理alignment順と論理順を分離、固定配列はstride配置。構築は最終subslotへ行い、全体転送は検査済みの別領域へmemcpy、破棄は逆論理順（配列はloop）。これらは現在の内部表現であり、一般公開ABIではない。

### 4.1. 集成型の選択結果（2026-09-14）

Tuple・固定配列はif/else、do/yield/exit、値付きloop、既存の対応Subjectを使うmatchの結果として配送できる。Copy集成型、所有stringを含む値、入れ子、ゼロサイズを同じ結果計画で扱い、local初期化/置換、構築payload、外側の結果への転送、未消費結果の破棄へ接続した。[AggregateResults](examples/AggregateResults/README.md) に実行例を示す。

従来のStringResultsをSlotResultsへ一般化し、Declare・結果Write・Join・到着edgeの検証を共有する。型分類はCopy性と独立し、具体的なlayoutの許可はLoweringで確認する。結果計画をlayout検査より先に登録し、検証済みの式結果だけを許可する。関数の戻り値PlaceやSubjectまでKindだけで許可しない。Binder・ABI・LLVM Writer・runtimeへの機能追加は不要だった。

結果確保はcleanup前、配送は正常到着時。未初期化への配置、全到着時のMustInit、Writeの支配、論理/物理それぞれの到着数を検証する。さらに結果の消費が現在の生存期間のJoin後にあることを確認し、cleanup中の早すぎる取得や過去の到着による誤承認を拒否する。defer複製はソース式ごとに結果スロットを共有し、Declare/Joinは展開別に保持。loopの結果Declareはhead前に一度置く。結果専用の生存flagやaggregate phiは追加しない。

転送・逆順破棄・条件付き置換は既存処理を再利用する。右辺の独立した結果を確保してcleanupを終え、必要な旧値破棄、新値転送、flag設定の順を守る。Abort/非停止cleanupでは後続配送・破棄を作らない。結果スロットへの直接構築、要素アクセス/部分Move、集成型関数ABIは§4.1の実装に含めず、関数ABIを§4.2、Copy要素読み取りを§4.3で追加した。

[AggregateResultEmissionTest](xUnitTest/Tests/AggregateResultEmissionTest.cs) はネスト、配列の期待型、covered arm/guard、結果の破棄、defer、戻り辺、O0 snapshot、Abort/非停止、不正計画と再解析による回復を検証する。deferを含むwarm BindとOwnership解析＋IR出力はそれぞれ0 B。既存のstring結果の検証も共通計画へ移行し、早期消費拒否を両型で固定した。throughputの改善率は未測定。

### 4.2. Tuple・固定配列の関数ABI（2026-09-14）

対応済みの所有Tuple・固定配列を、通常の直接呼び出しの値引数・戻り値へ接続した。入れ子、Copy/Move、ゼロサイズ、名前付き引数、Unit/scalar/string/ref-stringとの混在、浅い再帰、defer・選択結果からの返却を扱う。[AggregateFunctions](examples/AggregateFunctions/README.md) に実行例を示す。

サイズがある集成型は取得済み引数スロットとcallerの独立した結果スロットをptrで渡す。Copyは元の責任を残し、Moveは移す。引数名は論理番号の`%a<i>`、結果は`%ret`。ゼロサイズは物理引数・結果領域を省略するが、CallEntry・取得・parameter責任・通常復帰時のProduceは残す。同じcallの引数と結果の共有は拒否し、内側callの結果を外側callの取得済み引数として使う経路では追加転送しない。`return f(x)`の結果領域の直接転送は行わない。

stringの関数スロット検証をSlotFunctionsへ一般化し、選択結果のSlotResultsとは別の役割を維持した。call地点の未初期化、通常復帰だけによる結果初期化、return Writeの支配、cleanup後のDeliver、parameter Produceでの条件付き破棄flag初期化を検査する。既存の転送・逆論理順の破棄を再利用し、LLVM Writer/runtime/backendには追加を要しない。

署名と本体は同じAggregateLayoutPoolを使用する。ABIキャッシュは物理的な渡し方・省略・型の形だけを保持し、現在のBoundTypeは意味検証で照合する。成功・失敗の両方で一時的な型参照を解放する。共有引数のLoanは、参照を含まない所有集成型結果を確保した後に終了する。warm Bind、共有借用とdeferを含むOwnership＋IR出力は各0 B、再parse相当のABI再利用と構文木の非保持をテストした。throughputは未測定。

今回のABI対応はBinderの文脈推論を広げない。配列リテラルをcallの引数型から当てはめる処理や、一部の入れ子Tupleの引数推論は未完成であり、明示型のlocal経由で渡す。戻り値の期待型で配列リテラルを構築する既存経路は利用できる。集成型借用、要素書込み・Non-Copy要素取得・部分Move、struct/enum、generic・間接callは未対応。Copy要素読み取りは§4.3参照。

### 4.3. Tuple・固定配列のCopy要素読み取り（2026-09-14）

`pair.0` と `values[index]` を、local・parameter・一時値・選択/関数結果から読み取れる。固定配列の添字はisize、型なし整数はisizeへ当てはめる。対応scalar・Unit・Copy集成型を最終値として取得でき、親はstringを含むNon-Copy集成型でもよい。例は [ElementReads](examples/ElementReads/README.md)。

場所の形成（ProjectElement）と最終取得（Produce/Element）を分離した。連続する `.0` / `[i]` は同じrootからアドレスを段階的に計算し、中間集成型のスロットや転送を作らない。scalarは最終地点でload、Copy集成型は一度だけ独立スロットへ転送する。型・入力値のsource対応・root初期化・Loan・親射影/添字/取得の支配を生成前に照合し、不正計画はIRを書き出す前に拒否する。

rootを添字評価前にReadし、既存の共有Loanのスタックで最終Copyまで保護する。添字評価中のMove・置換・破棄は禁止、共有読み取りとCopyは許可する。通常transferでは対象境界のLoanをcleanup前に終了する。Copy後の親の変更は取得済みの値に影響しない。一時receiverはCopy後も通常の式末尾cleanupまで保持する。

各配列射影は負数も含めた `icmp uge i64 index, length` を検査し、成功ブロックでのみstride計算とアドレス形成を行う。不正添字は `KIMI_E_INDEX_BOUNDS` でAbortし、後続添字・defer・破棄を実行しない。空配列への定数添字も実行時Abort。ゼロサイズ要素でも検査を残し、不要なアドレス/load/転送は省く。専用アドレス名を使い、既存の演算検査と同じ後続ラベル方式に接続する。

Copy要素への単純代入は§4.4、数値要素の複合更新は§4.5で追加。Non-Copy要素のMove/共有結果、部分Move、集成型借用、Array/Slice/Index/^/Rangeは未対応。今回のroot保護は一般のLoan/Origin/Move Path solverではない。

[ElementEmissionTest](xUnitTest/Tests/ElementEmissionTest.cs) は57件。入れ子・各scalar幅・Copy結果・bool/Unit・0長/0サイズ・値のsnapshot・短絡/phi・parameter・ループ/defer・転送/到達不能/covered arm・Loan競合・不正計画と回復を検証する。warm Bindと所有権解析＋IR生成は各128回で0 Bを確認した。

### 4.4. Tuple・固定配列のCopy要素への単純代入（2026-09-15）

初期化済みで完全な所有Tuple・固定配列を保持するlocal varに対し、数値selector・isize添字・入れ子経路でCopy要素を `=` 置換できる。対応scalar・Unit・再帰的なCopy集成型が対象で、親にはowned stringを含められる。右辺の構築・Copy・関数結果・選択結果を独立した値として確保し、その後で左辺経路を一度ずつ評価する。例は [ElementAssignments](examples/ElementAssignments/README.md)。

位置取得は既存のLocateElement/ProjectElement・境界検査を共有し、最終操作だけをWriteElementとして区別する。親の全体Write・再初期化・破棄計画は作らず、親の初期化状態と責任を維持する。scalar storeは既存の格納表現、Copy集成型は既存のmemcpy生成を共有する。中間集成型のCopyや要素別の生存flagは追加しない。ゼロサイズでも評価・境界検査を残す。

位置取得中はrootを保護する。自身のアクセスLoan終了とstoreが通常edgeで直結し、間にユーザー操作も別の到着edgeもないことをLoweringで確認する。他のLoanの競合検査は維持する。型・入力source・Copy性・mutable root・右辺の初期化と支配・結果のJoin・親/添字の対応を照合し、不正計画はIR公開前に拒否する。添字内で同じrootの別要素へ書くケースも現在は保守的に拒否し、一般の非重複経路証明は導入していない。

構造的完了と制御フロー解析の単純代入をRHS先行に揃え、構文順のvisitorは維持した。添字の途中transfer後も静的な代入先型を保持し、ラベル付きdo式の値sourceを正規化する。右辺・添字の通常transfer、Abort、非停止cleanupは後続の位置取得/書き込みを実行せず、既存の結果確保・cleanup規則を維持する。

[ElementAssignmentEmissionTest](xUnitTest/Tests/ElementAssignmentEmissionTest.cs) は79件。入れ子・各scalar表現・snapshot・自己代入・Copy集成型・関数/選択結果・defer複製・transferの優先順位・未到達/covered arm・0長/0サイズ・境界Abort・非停止・親の破棄順/回数・Loan競合・不正計画と再解析回復を検証する。warm Bindと所有権解析＋IR生成は各128回で0 B。throughputの改善率は測定していない。

数値要素の複合代入/増減は§4.5で追加。Non-Copy要素の置換/取得、部分Move/再初期化、借用・一時receiver、Property、動的collectionはこの実装単位に含めない。SPEC本文の許可範囲を縮小する変更ではない。

### 4.5. Tuple・固定配列の数値要素更新（2026-09-15）

§4.4と同じ初期化済みの所有local varをrootとして、整数要素に `+= -= *= /= %= &= |= ^= <<= >>=` と前置/後置の `++ --`、f32/f64要素に `+= -= *= /=` を接続した。入れ子・Non-Copy親・defer展開・関数内でも利用できる。i128/u128除算/剰余は既存profileの禁止を維持する。例は [ElementUpdates](examples/ElementUpdates/README.md)。

SPEC §13.7.2に従い、位置取得・境界検査・旧値Copy・RHS評価・数値計算・storeの順で一度ずつ実行する。単純代入のRHS先行とは区別する。複合代入はUnit、前置増減は新値、後置増減は旧値をstore後に返し、再loadしない。整数のoverflow・除算/剰余・shift検査とfloatのIEEE演算は既存scalar処理を共有する。shift RHSは独立した整数型のまま検査する。

LocateElement・CopyElement・StoreElementを読み取り/単純代入/更新で共有し、数値計算と増減結果生成も通常local更新と共通化した。射影計画に更新の参照を追加し、再利用するElementUpdates表がRHS・計算・結果を結ぶ。中間集成型のCopy、要素別生存flag、更新専用のLLVM演算は追加しない。親の初期化・所有責任を維持する。

Loweringは更新元source、演算子、旧値とRHS、型、唯一の射影所有者、Loanと支配関係、計算→store→自身のLoan解放→結果の連続edge、前置/後置の結果選択を照合する。単純代入のRHS確保順序の検証も維持する。不正計画はIR出力前に拒否し、再解析で回復する。

添字評価中は共有保護とし、最終射影の境界検査成功後に自身の保護を排他的Loanへ切り替える。排他的Loanは旧値取得・RHS・計算・storeまで保持し、そのアクセス自身のstoreだけを許可する。既存の外側Loanは解除せず、同じ親の別アクセスが生存していれば排他取得を拒否する。新しいLLVM命令や値スロットは不要で、既存Loan表と射影の整数IDを再利用する。

`a[0] += a[0]` は仕様の排他的Loanと競合するため拒否する。RHSの関数引数への親Copyやdefer内の読み取りにも適用する。今回もroot単位の保守的な判定であり、同じ親の兄弟要素への読み取り/Copy/Move/書き込みを拒否する。仕様の静的非重複経路による許可は未実装。別rootの更新と添字評価中の共有読み取りは許可する。通常transferは放棄したアクセスのLoanをcleanup前に終了する。境界/演算Abort、RHS/添字のtransfer、非停止cleanupでは後続storeを行わない。添字がNeverになっても代入先の静的型を保持し、shiftのNever RHSも通常transferとして扱う。

初回実装ではRHS中も共有保護を用いて自己読み取りを許可していたが、§4.6.4/§15.6.2の排他的Loan規則との不整合を修正した。単純代入は現行仕様のRHS先行を維持する。要素更新前に必要な値をlocalへCopyする実行例に修正した。

[ElementUpdateEmissionTest](xUnitTest/Tests/ElementUpdateEmissionTest.cs) は114件。全整数幅、floatのNaN/無限大/符号付き0、評価回数/順序、shift幅、演算/境界Abort、親の破棄監査、Loan競合、defer・到達不能・transfer・非停止、不正計画を検証する。排他取得前後とstore後のLoan状態、自己読み取り/親Copyの拒否、通常transferでの解除、改変計画の拒否も含む。入れ子更新とdeferを含むwarm Bind、所有権解析＋IR生成は各128回で0 B。throughputの改善率は未測定。Non-Copy要素操作、部分Move/再初期化、借用・一時receiver、Property、動的collectionは未対応。

<a id="c2-builds-modules-and-source-artifacts"></a>
<a id="c11-executable-preparation-and-sequence-review-2026-09-09"></a>
<a id="c13-remaining-implementation-selections"></a>
<a id="c22-lowering-and-windows-profile-specification-integration-2026-09-11"></a>
<a id="c25-core-declaration-catalog-and-native-backend-candidate-2026-09-11"></a>
<a id="c35-shared-llvm-version-policy-2026-09-13"></a>
<a id="c36-native-build-execution-and-shared-package-release-2026-09-13"></a>
<a id="c37-generated-kernel32-import-library-2026-09-13"></a>

## 5. CLI・成果物・Windows runtime

- [Project](Kimi/SolutionAndProject/Project.cs) のCheckは意味検査、Generate/`emit-llvm`は整合した`.ll`＋`.link.json`、Build/`build`はnative exe生成。`run`は既存Applicationを実行し、source変更で自動再buildしない。
- [EmissionArtifacts](Kimi/Compiler/Emission/EmissionArtifacts.cs) / [NativeToolchain](Kimi/Compiler/Emission/NativeToolchain.cs) はschema 3、IR/exe/供給hash・ABI・LLVM同一性、失敗時の旧成功無効化、stagingからの公開を扱う。toolの出力を回収し、取消し・時間上限・child tree終了を実装。Applicationの出力/exitは転送する。
- [ToolchainResolver](Kimi/Compiler/Emission/ToolchainResolver.cs) はCLIのToolchainRoot、環境変数KIMI_TOOLCHAIN_ROOT、既定配置の順でrootを選ぶ。既定は実行file隣接toolchain、source buildではcheckoutのtoolchainを探索。LlvmBin・明示backend pathの上書きも別途扱う。emit-llvm単独にはLLVM導入不要。通常の.NET buildはbackendを再生成しない。
- [WindowsProfile](Kimi/Compiler/Emission/WindowsProfile.cs) と [profile.json](backend/windows-x64/profile.json) はLLVM 22.1.8、backend ABI 2、__chkstk/memcmp/memcpy/memmove/memsetを定義。版はDirectory.Build.propsと共有し、実archive hashも照合する。明示的な未固定toolchain試行でもintegrity検査は省略しない。
- [WindowsRuntime.ll.in](Kimi/Compiler/Emission/WindowsRuntime.ll.in) はstartup、UTF-8出力、確保/解放、string破棄、Abortを実装。通常exit 0、Abort 1、LFを別出力、NULはデータとして扱う。Staticは解放せず、Heap責任を解放する。一般object/Weak/metadata runtimeは未完成。
- [Kernel32Imports](Kimi/Compiler/Emission/Kernel32Imports.cs) はproject-owned定義とllvm-dlltoolからimport libraryを生成・検査する。runtimeの7 Windows APIとnativeテスト用3 APIを供給し、SDKのkernel32.lib設定を不要にする。

根拠: EmissionArtifacts / NativeToolchain / ToolchainResolver / Kernel32Imports / MinimalEmission各テスト、[backend検証script](backend/windows-x64)。通常nativeの今回の実行結果は§7参照。

## 6. 未接続の製品機能・性能基盤

| 分野 | コードで確認できる状態 |
| --- | --- |
| module・Mod | [Compilation](Kimi/Compiler/Core/Compilation.cs) は設定の外部Kotonoha識別子を保持するが、source読込への接続はない。BindにMod実行はなく、外部module/Libraryは生成側で拒否。portable交換・意味計画の永続再利用・Composition接続は未完成 |
| generic・object・Core | generic共有/特殊化生成、Closure/間接call、struct/enum/Property/user constructor/static実行、rc/arc/Weak・runtime refinement、Array/Slice/Dictionary・反復、言語test runnerは未完成。前段の宣言・解析は§2–3参照 |
| LSP | [LspServer](Kimi/Lsp/LspServer.cs) にinitialize・document管理・shutdown等の通信処理がある。open/changeの診断送信はコメント化、closeの空診断だけ接続。dumpの要求/出力も未接続 |
| KimiCode | [extension.js](KimiCode/extension.js) は隣接Debug DLLとDebugWait=trueを固定使用。一般利用のserver設定とhost統合テストは未整備 |
| CI・配布 | [test.yml](.github/workflows/test.yml) / [publish.yml](.github/workflows/publish.yml) はLinux Release build後に構成/対象未指定のtest。PR・Windows native・0件検知・ログ保存が未整備。NuGet pack/pushの定義はあるが今回未実行 |
| 性能 | Binding/型/CFG/ABI/定数/layoutの表・scratch・容量を再利用。対応テストにwarm allocation 0 byteと再parse後の保持参照検査、[Benchmark](Benchmark) に字句/Binding/所有権等のworkloadがある。全経路の無割り当てやthroughput改善は保証しない |

## 7. 今回の確認

### 7.1. 数値要素更新の確認（2026-09-15）

§4.5の排他的Loan修正と、§4.3–4.4の要素読み取り/単純代入の回帰を検証した。自己読み取りを成功扱いした旧fixtureは生成物から除外し、事前Copyの正常例とコンパイル拒否テストへ置き換えた。

| 検証 | 結果 |
| --- | --- |
| Solution build（Debug / Release） | 各成功、警告0・エラー0 |
| managed test（Debug / Release） | 各4,468成功、失敗0・skip0。ElementUpdateEmissionTestは17件追加して114件 |
| 要素更新＋既存要素読み取り/単純代入のLLVM verifier・通常native O0/O2 | 163 fixture、326実行成功。更新68 fixture・136実行、単純代入54 fixture・108実行、読み取り41 fixture・82実行。破棄監査・境界/演算Abort・タイムアウト付き非停止を含む |
| warm allocation | 入れ子要素更新・選択RHS・deferを含むBind、およびOwnership＋IR生成を各128回測定し0 B |
| ElementUpdatesのCLI build/run | Release/O2成功。`element updates complete`を出力してexit 0 |
| NativeAOT・新規throughput benchmark | 未実行 |

再現は下記§7.2の構成ごとのbuild→testに加え、生成済みfixtureへ `./backend/windows-x64/test-scalars.ps1 -FixturePattern 'Element*.ll'` を適用する。旧実装で生成済みの `ElementUpdateSelfRead.*` は削除してから再生成する。CLI例は [ElementUpdates](examples/ElementUpdates/README.md) を参照。

§4.5の初回実装時はmanaged各4,451件、Element系160 fixture/320実行、通常local更新（IntegerValues/WideOperations/FloatArithmetic）14 fixture/28実行が成功した。ただし、この時点の自己読み取り許可は上記のLoan修正前であり、仕様適合の根拠にはしない。

直前の§4.4実装時はmanaged各4,354件、Element系95 fixture/190実行、AggregateFunction系59 fixture/118実行が成功。ElementAssignments例もRelease/O2で期待出力・exit 0を確認した。

### 7.2. 直前の実装時の確認（2026-09-14）

基礎調査では主要入口・型/変換/ABI/aggregate・所有権の拒否条件、Core catalog、LSP/拡張/CIを現コード・対応テストと照合した。その後、§4.1の結果配送、§4.2の関数ABI、§4.3のCopy要素読み取りを実装し、以下のbuild・managed検証を再実行した。

| 検証 | 2026-09-14の結果 |
| --- | --- |
| Solution build（Debug / Release） | 各成功、警告0・エラー0 |
| managed test（Debug / Release） | 各4,275成功、失敗0・skip0。fixture生成を含む |
| 集成型選択結果のLLVM verifier・通常native O0/O2（§4.1実装時） | 52 fixture、104実行成功。破棄回数/順序、依存symbol、タイムアウト付き非停止を含む |
| 既存string結果のnative回帰（§4.1実装時） | 47 fixture、94回のO0/O2実行成功。集成型結果と合わせて99 fixture・198実行 |
| 集成型関数ABIのLLVM verifier・通常native O0/O2（§4.2実装時） | 59 fixture、118実行成功。破棄順・回数、Abort、タイムアウト付き非停止、依存symbolを検証 |
| 既存string関数のnative回帰（§4.2実装時） | 63 fixture、126実行成功。集成型関数と合わせて122 fixture・244実行 |
| Copy要素読み取りのLLVM verifier・通常native O0/O2（§4.3） | 41 fixture、82実行成功。境界Abort・空配列/ゼロサイズ・添字の評価順・一時receiverの破棄順/回数・依存symbolを検証 |
| 集成型関数ABIのnative回帰（§4.3実装後） | 59 fixture、118実行成功。要素読み取りと合わせて100 fixture・200実行 |
| ElementReadsのCLI build/run | Release/O2成功。`element reads complete`を出力してexit 0 |
| AggregateFunctionsのCLI build/run | Release/O2成功。`leaving echo`を2回、`aggregate functions complete`を出力してexit 0 |
| AggregateResultsのCLI build/run | Release/O2で成功。`cleanup`、`aggregate results complete`の順に出力しexit 0 |
| runtime/backend単独・LSP統合script | 今回未実行 |
| NativeAOT・VS Code host・CI実workflow・配布・新規benchmark測定 | 今回未実行 |

再現コマンド（構成ごとにbuild→testを直列実行）:

```powershell
dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore
```

buildの警告数は現設定下の結果。テストは対応・拒否境界を検証するもので、SPEC全体への適合証明ではない。

## 8. autoframe実行基盤

[autoframe.md](autoframe.md) 1.0に基づくWindows版を[autoimpl/](autoimpl/README.md)に実装。6段階のプロンプト、Schema、状態・証拠管理、再開、疑似Worker試験を含む。基盤の試験結果と実CLI未確認の理由は[検証記録](autoimpl/VERIFICATION.md)を参照。今回の基盤作業ではNativeAOT、製品用PLAN.mdの作成、本番の自動実行を行っていない。

追加精査で、部分受理、回復対象の欠落、証拠の破損・失効、監査と停滞の判定、停止確認、指示ファイルの保護・署名を修正。疑似試験は追加17件を含む計56件を確認した。実CLIは今回再試験していない。
