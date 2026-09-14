# Implementation Plan

更新: 2026-09-14。確認対象: `e861ce5ebf7c365f8e8416e0ed692500eb9b1f42` の製品コード。

## 1. 目的と範囲

[SPEC.md](SPEC.md) 本文の確定規則を、構文・Binding・所有権・生成・通常native実行まで接続する。初期対象は windows-x64-v1。CLI・LSP・KimiCode・既存配布の検証を含む。

- 実装状況とコードの根拠は [STATUS.md](STATUS.md)、残作業・依存・次の操作は本書、監査履歴は [AUDIT_FINDINGS.md](AUDIT_FINDINGS.md) に分ける。旧計画の実行枠・期限・証拠prefix・引継ぎ指示は引き継がない。監査履歴中の旧章番号は再編前を指す。
- 仕様判断はSPEC本文を基準とし、doc/・旧Design例から未記載の公開契約を補完しない。本文の不足は§13へ分離し、内部実装設計と混同しない。
- 性能・割り当てを考慮し、SPEC/STATUSは実装変更に応じて更新する。NativeAOTは明示指定時のみ。通常の計画完了には含めない。

## 2. 着手順

今回、主要生成経路・拒否条件と両構成のmanaged suiteを確認した。既存106 IDを維持し、過去の工程記録を除いて現在の作業へ組み直した。BASE-07は**現入力の統合再検証**として未完了へ戻す。過去のnative成功やAF-0001の当時の解消を取り消すものではない。

| 優先 | 作業 | 着手点・出口 |
| --- | --- | --- |
| 1 | BASE-07 → NUM-04-FLOAT → NUM-04-INTEGER | 現fixtureとtoolchainの基準を保存し、Binding.Conversionsから型付き計画・検査・LLVM・診断へ接続。詳細は§14 |
| 2 | FRONT-01 → TYPE-01/02 → BIND-01–03 → ORIGIN/BORROW/OWN | 構文/型の残差、Origin証明、Place/Loan・部分初期化を整備。LAYOUT/ABI、集約・Property・generic生成の前提を作る |
| 独立 | MAINT-01、MAINT-02、LSP-01/02、EDITOR-01 | debug経路、構成一致のCI、診断snapshot、server起動設定。各行の依存を満たして進める |
| 独立 | DEP-01、SPEC-MOD-01、SPEC-TEST-02 | 解決済み依存snapshot、Mod host、隔離runnerの内部設計。未決の公開形式を推測しない |
| 合流 | §7–10の各機能 → ACCEPT-01–03 | 個別完了条件、必須依存、全仕様・配布検証を満たして完成を判断 |

仕様決定待ちは該当するsource/API接続だけを止める。計画全体を待機にしない。実装順は依存欄に従い、独立項目は入れ替えられる。

## 3. 状態と共通完了条件

`[x]` は行に限定した実装・検証を確認済み、`[ ]` は部分実装・検証待ちを含む。テストの拒否期待が成功しても、その機能を実装済みにしない。表のIDと依存は自動実装用にも維持する。

1. SPECの対象規則を実装・正常系・不正使用へ対応付け、元source位置で診断する。未使用body・定数偽枝・到達不能部分も必要な検査を通す。
2. 再Bind/再Analyze・入力変更・失敗後再実行で古い証明を使わない。不正な型/値/CFG/ABI/cleanup計画はIR公開前に拒否する。
3. 近傍回帰と必要な統合検証を行う。生成/runtime変更はLLVM verifierとO0/O2で値・stdout/stderr・exit・評価/破棄順を確認し、Abort・非終了・部分初期化・二重破棄も扱う。
4. build/testの構成を一致させ、件数0・失敗・skip・警告・未実行を明記する。source/config/toolchain/fixtureと結果を対応付け、入力が異なる過去のnative成功を転用しない。
5. 共有出力を使う検証は直列実行する。共通runtime変更時は影響する全fixtureを再生成・再検証。性能に影響する変更はallocation・保持参照・peak memory・compile time/code sizeを代表入力で測定する。

起動方法は [検証ガイド](automation/verification-guide.md)。STATUSには現在の実装範囲・制限・実施した検証だけを記載する。

## 4. 既存基盤と統合再検証

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [x] BASE-01 | Debug/Releaseの全Solution build | 4プロジェクトが警告0・エラー0 | なし | 今回の両構成build（STATUS §7） |
| [x] BASE-02 | managed回帰・fixture生成 | 両構成各4,123成功、失敗0・skip0。native実行とは区別 | BASE-01 | 今回の両構成test（STATUS §7） |
| [x] BASE-03 | 字句・構文・source identity・directive・Koto保存の既存基盤 | 現行テスト範囲を維持。全構文の意味解析・実行は含まない | BASE-02 | UnicodeIdentifier / SourceEncoding / DirectiveConditionValidation / ParserRegression / KotonohaSerialization |
| [x] BASE-04 | 名前・型・呼出・Contract・Property・startupの既存Binding | 正常・不正・再Bindの既存範囲を維持。未解決義務は成功にしない | BASE-03 | Binding / TypeBinding / ContractBinding / PropertyBinding / StartupBinding |
| [x] BASE-05 | bool・64bitまでの整数・Unit・直接call・制御フロー・string・限定ref/string | 対応範囲の値・Loan・cleanup・IR生成を維持。制限はSTATUS | BASE-04 | Scalar / Integer / Function / Result / Match / Guard / String* / ReferenceEmission |
| [x] BASE-06 | owned Tuple/固定配列の全体構築・Copy/Move・破棄 | 要素射影・集約signature/結果を含めず、現行範囲を維持 | BASE-05 | AggregateEmissionTest、生成fixture |
| [ ] BASE-07 | 現入力でWindows runtime/helper・CLI・LSP基本通信を再検証 | source・構成・toolchain・fixture hashに対応する通常native/CLI/LSPの結果を保存。診断・拡張・CI全体は別項目 | BASE-05 | §3・STATUS §7。過去成功の転用を避け、現fixtureのLLVM verifier/O0/O2と各統合scriptを実行 |
| [x] BASE-08 | 主要入口・拒否条件・未接続箇所のコード索引 | STATUSの根拠を現ソース・テストへ照合。全未使用コードの証明は含めない | BASE-02 | Compilation / Binding / Ownership / Emission / Core / LSP / extension / workflows |

## 5. 仕様境界と開発基盤

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] SPEC-01 | SPEC本文・Appendix A/Fの要件台帳 | 規則ごとに機能ID・実装・正負テストを対応付ける。対象節から具体化し、最後に残差を閉じる | BASE-08 | §12・15。台帳全体の完成を独立実装の前提にしない |
| [ ] SPEC-COMPOSE-01 | Entry宣言・参照契約の確定 | 非置換root・Std接続を保ち、文法・型/適合・アクセスを本文と正負例へ確定 | BASE-04 | §13、AF-0002。確定後にFRONT/COMPOSEのsource受入例を作成 |
| [ ] SPEC-COMPOSE-02 | Provider選択の公開設定契約の確定 | 指定・既定選択・欠落/競合/適合を本文と正負設定例へ確定。module identityと最終接続は分離 | BASE-04 | §13、AF-0002。変更時の内部失効検証は先行可能 |
| [ ] SPEC-TEST-01 | #Testの所属・発見・検査契約の確定 | 付与先・署名・発見/選択・通常mode/非選択本体の検査範囲を本文と正負例へ確定 | BASE-03 | §13、AF-0002。一方向依存と共通検証/cleanupを維持 |
| [ ] SPEC-VERIFY-01 | $expect/$requireの公開契約の確定 | 引数/結果・評価順/効果・メッセージ・失敗後制御/cleanupを本文と操作別正負例へ確定 | BASE-04 | §13、AF-0002。通常require文・現行$abortと区別 |
| [ ] SPEC-DEPS-01 | 外部依存設定・版解決契約の確定 | 参照名・版制約・取得先・graph診断を本文と正負設定例へ確定。直接参照・定義側環境・複数版を維持 | BASE-04 | §13、AF-0011。Name/Versionから取得先を推測せず、内部snapshotは先行可能 |
| [ ] SPEC-ARTIFACT-01 | portable source/interface交換契約の確定 | encoding・必須field・validation/compatibilityを本文と交換/破損/非互換例へ確定。§18.3の情報保存を維持 | BASE-04 | §13、AF-0011。serialized Koto・schema 1・SourceIdを推測しない |
| [ ] SPEC-MOD-01 | Mod hostの実装設計を具体化 | §20.7.7が実装設計へ委ねるquery/marker登録、assembly互換、project設定、取消し上限を具体化。ソース言語の追加仕様を創作しない | BASE-04 | syntax/semantics snapshot、例外/timeout、再生成の正負テストが書ける設計を保存 |
| [ ] SPEC-TEST-02 | 本文のprocess隔離・有限報告/回収をrunner設計へ対応 | SPEC冒頭9行の境界とB.7.3本文の製品/test再利用・予算分離を満たすtransport・上限・終了判定を実装設計として具体化。公開test操作はSPEC-TEST-01/SPEC-VERIFY-01で確定 | BASE-04 | 0件/報告欠落/破損/過大入力/期限/取消しの受入表を保存。IssueIdや公開一時directory APIを既定要件にしない |
| [ ] SPEC-API-01 | exchangeの公開API確定 | §15.7の責任移転・非重複・初期化を保ち、綴り・名前解決・型・取得/効果・使用位置を本文と正負例へ確定 | BASE-04 | §13、AF-0007。決定待ちはOWN-02のsource接続に限定 |
| [ ] SPEC-02 | 本文の契約別仕様不足・実装設計の残差を総括 | 未決の公開文法/設定と実装者が決める内部形式を分離し、確定した必須規則を弱めず各分割IDを閉じる。doc参照/本文統合タスクは対象外 | SPEC-COMPOSE-01, SPEC-COMPOSE-02, SPEC-TEST-01, SPEC-VERIFY-01, SPEC-DEPS-01, SPEC-ARTIFACT-01, SPEC-MOD-01, SPEC-TEST-02, SPEC-API-01 | §13の不足・解除条件と対象外範囲を照合。個別実装を総括IDの完了待ちにしない |
| [ ] MAINT-01 | 未使用・未接続debug経路と実験コードの整理 | unitContext、dump、不要async/usingを削除・接続・明示隔離のいずれかで処理。generator/DI登録経路を壊さない | BASE-08 | 参照・生成コード確認、IDE解析、全build、CLI/LSPの起動終了と割り当て比較 |
| [ ] MAINT-02 | CIで構成・対象・結果を一致させる | PR検証、Debug/Releaseの明示、Windows通常native検証、0件検知、ログ保存をtest/publish両workflowへ接続 | BASE-07 | cleanローカル再現とworkflow検証。Linux managedとWindows nativeを区別。実workflow未実行は記録し完了扱いしない。NativeAOT・外部公開は実行しない |

## 6. フロントエンド・意味解析・所有権

### 6.1 型と宣言

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] FRONT-01 | SPECの確定構文とKoto/source round-tripの残差を解消 | 規定構文のsource span、child ownership、回復、再serializationを保つ。未決構文は対応SPEC分割IDの本文確定まで作らない | BASE-03 | Appendix F対照とparse正負テスト。FRONT-02の条件名修正は独立。SPEC-01全体の完成を着手前提にしない |
| [x] FRONT-02 | 条件Nameのcase-sensitive解決 | Feature/FEATURE・windows/WINDOWSを区別。完全一致の重複/組込み衝突を拒否し、metadata/再Prepareへ反映 | BASE-03 | CompilationSpecification / DirectiveConditionValidation。culture/NFC・短絡/非選択arm・設定保存・失敗後再Prepare・warm allocation |
| [ ] TYPE-01 | 完全Type・Semantics層・型役割・名目identityを完成 | ref/uniq/object/unsafeの入れ子、Tuple/enum/配列、関連型・generic slotの形成と拒否がSPEC §§3/8/9に一致 | FRONT-01, BASE-04 | TypeBinding、NestedTypeParse、ConstraintBindingを拡張。型役割のUnknownを成功扱いしない |
| [ ] TYPE-02 | 定数式・固定配列長・要素型推論とlength slotを完成 | 正規化・ValidLength・isize上限・宣言時証明と通常演算Abortを区別。length引数の候補Pendingを適切に解決 | TYPE-01 | §4.2–4.4の定数参照、N=0、負値、中間overflow、同型式、推論の正負テスト |
| [ ] BIND-01 | 複数source・group/struct断片・宣言containerの最終Bindingを完成 | 断片の契約、Name Reachability、可視性、protected receiver、重複・追加時の検証が列挙順に依存しない | TYPE-01 | Binding/InheritedReceiverBinding/IdentifierIdentity、複数source順序変更・再Bind |
| [ ] BIND-02 | overload・引数対応・期待結果・defaultの意味計画を完成 | 再選択なしにgeneric/length/Originを推論し、receiver・named/default引数の評価順と取得を固定 | BIND-01, TYPE-02 | candidate順序、曖昧さ、default副作用、引数失敗、unsafe境界のcallテスト |
| [ ] BIND-03 | static Contract・associated Type・conditional conformance・witnessを完成 | 宣言・継承・操作の適合証明を保持し、未解決の能力をruntimeへ先送りしない | BIND-02 | Contract/ConditionalConformance/ConditionalMember/ConstraintBindingを定義変更・反証込みで拡張 |

### 6.2 借用と値の責任

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [ ] ORIGIN-01 | 抽象Originのbound・順序・交差・variance・代入証明 | 正当なboundを認証し、不正なbound・寿命延長を拒否。型identityと生成用Origin消去を混同しない | TYPE-01 | SpecReviewの未対応期待を正負の証明テストへ拡張、再Bind・相互制約・関連型 |
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

| 状態・ID | 実装内容 | 完了条件 | 依存 | 検証方法 |
| --- | --- | --- | --- | --- |
| [x] NUM-01 | 初期Windows profileのi128/u128実行範囲を実装 | 格納・取得・直接call・比較・bit/shift・整数変換・checked add/sub/neg/inc/dec/mulを接続。除算/剰余とfloat相互変換は§21.5.3通り最適化前に拒否 | BASE-05 | WideIntegerEmissionの端点・overflow・shift・禁止演算・破損値・serialization・allocation。現入力のO0/O2・乗算展開・未供給symbol検証はBASE-07 |
| [x] NUM-02 | f32/f64の値・演算・比較・直接call | 対象精度へ直接丸めたliteral、SSA/格納/取得、parameter/結果、既存選択結果/aggregateを接続。NaN/±0/subnormalを保持 | BASE-05 | FloatEmission / NumberLiteral / Function / Result / Comparison。変換の残りはNUM-04 |
| [x] NUM-03 | charの値・比較・直接call・制御フロー | Unicode scalarをlocal/Copy/置換、関数/選択結果、literal match/guard、既存Tuple/固定配列へ接続 | BASE-05, BASE-06 | CharEmission / CharLiteralParse。境界/不正値/禁止演算/破損計画/再解析・allocation |
| [ ] NUM-04-FLOAT | plain Type指定のfloat相互変換・literal fitting | 幅拡張/同型/直接fittingを保ち、f64→f32の有限値→infinityだけAbort。特殊値・各段rounding・失敗順を保持 | NUM-02 | §14.1、FloatConversionEmission。正負/境界/破損計画・元source位置・O0/O2・allocation |
| [ ] NUM-04-INTEGER | 64bitまでの整数↔float・整数literal fitting | 全40方向、exact literalからの直接rounding、ordered範囲検査成功後のfptosi/fptoui。typed128bit↔floatは禁止 | NUM-01, NUM-04-FLOAT | §14.2。20 float→整数pairの端点/隣接値、逆20pairのties/最大値、特殊値、literal/typed差、O0/O2・helper・元位置 |
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
| [ ] MODULE-01 | 解決済みsource snapshotを外部Kotonohaへload | DEP-01の内部解決結果からsource/moduleを一度だけ作り、定義側環境・直接参照名・版を保持。外部公開設定の完成とは区別 | DEP-01, BIND-01 | Compilationの参照識別子から外部source読込を内部fixtureで接続し、順序/欠落/同identity別名/複数版/間接Name非公開を検証。最終外部設定接続はBUILD-01 |
| [ ] MODULE-02 | moduleを跨ぐ最終Binding・閉じた選択集合・private依存を接続 | import先のgeneric/contract/Originを保持し、依存追加削除・変更で適切に再検証する | MODULE-01, BIND-03, GEN-02 | §18/21.3.4、同名identity、private helper、specialization追加削除、アクセス拡大/撤回 |
| [ ] ARTIFACT-01 | §18.3のsource/config再構築とinterface情報保存を実装 | SPEC-ARTIFACT-01で確定した交換形式で定義側identity/可視性/完全Type/Origin/効果/cleanup/選択集合を欠損なく再構成。serialized Kotoをportable形式にしない | SPEC-ARTIFACT-01, MODULE-02 | §18.3/21.3.4の情報カテゴリを入力改変・欠落・版/target変更・private generic依存の正負round-tripで検証。未知のschema 1/SourceIdを採用しない |
| [ ] MODULE-03 | Libraryの検査用IR/object出力とApplicationへの生成依存を完成 | Library一律拒否を解除。Libraryにはstartupを作らず、検証対象bodyを残す。外部安定ABIは約束しない | MODULE-02, GCODE-03, FFI-01 | Application/Library対照、unused body不正、entry/linkage、IR verifier、object symbol |
| [ ] MOD-01 | Mod登録・provisional query・snapshot・実行順を実装 | §20.7とSPEC-MOD-01のhost契約で決定的登録・scope照会・失敗/cancellation・変更禁止境界を接続 | SPEC-MOD-01, MODULE-02, BIND-03 | 重複/循環/順序、未知照会、selected/excluded syntax、失敗・再実行。Modを自動再試行しない |
| [ ] MOD-02 | 生成sourceの追加・統合・final再検証・無効化を実装 | 生成sourceのidentity/provenanceを保持し、古い出力の残留、二重生成、無検証公開を防ぐ | MOD-01, LAYOUT-01, GEN-01 | §20.7、断片追加/削除、生成診断位置、Mod入力変更、古いキャッシュ拒否、列挙順不変 |
| [ ] CACHE-01 | 検証済み意味計画までの永続再利用を実装 | §21.3.4.1–2本文のproof/ownership/effect/cleanup/source参照を照合し、使用時Loanを再検証。ABI/context/frame/budget/IRは再生成。Provider独立Library計画を分離 | MODULE-02, MOD-02, GCODE-03 | cache有無/破損/版・公開範囲・target変更/原文位置の正負検証。形式は内部設計で決め、doc由来のSourceId/store方式を必須にしない |
| [ ] COMPOSE-01 | 本文で確定したroot/Entry/Providerの宣言・選択を実装 | 非置換root、module identityと最終接続の分離を保ち、SPEC-COMPOSE-01/02で本文確定した文法・選択/適合規則だけをsourceへ接続 | SPEC-COMPOSE-01, SPEC-COMPOSE-02, MODULE-02, BIND-03, BORROW-04 | §13.1の確定事項と決定後の正常/欠落/重複/不適合例。group $/compose $を旧計画から採用せず、未決中は本ID未完了 |
| [ ] COMPOSE-02 | Std出力接続と構成依存生成物の再検証を実装 | SPEC冒頭7行のCore.writeLine→選択Std接続、§21.3.4.2の影響するwitness/inline/entry-context再生成を満たす。全bodyへCompositionIdを混入させない | COMPOSE-01, MODULE-03, CACHE-01, GCODE-04, BASE-05 | Provider変更/同module identity/古い生成物拒否/情報不足時の保守的失効、現行出力の回帰と製品/test予算分離 |
| [ ] NATIVE-01 | §20.8/21.5本文のnative要求・供給照合を外部moduleへ接続 | NativeLibraries・schema 3 manifest・採用backend/hash/ABI・kernel32生成・実objectのhelper依存検証を一貫実施。古い供給/未知helperを成功にしない | MODULE-02, FFI-01 | NativeToolchain/WindowsProfile/LibraryImportと通常native正負fixture。本文のsymbol/Kind/tool/hash条件を検証し、doc由来の一般COFF member閉包/weak/COMDAT専用契約を追加しない |
| [ ] BUILD-01 | 複数source/module、static、Modを含むbuild/runの原子性を完成 | 外部依存設定をSPEC-DEPS-01の確定契約へ接続し、failed/cancelled build・部分生成・依存変更で旧成功を誤認しない。既存Project.Checkの意味検証も維持 | COMPOSE-02, STATIC-01, MOD-02, FFI-01, NATIVE-01, SPEC-DEPS-01 | NativeToolchain/EmissionArtifacts/ToolchainResolverと既存CLI、失敗注入、空白path、hash/ABI/依存不一致、check内部経路。新規kimi check/restore/pack/publishを要件にしない |

## 10. 言語テスト・LSP・エディター拡張

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

## 12. SPEC章・付録の対応表

章単位の完了表ではなく、SPEC-01で個別規則へ展開する索引。

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
| §19 directive | BASE-03, FRONT-01, FRONT-02, MOD-02（条件Nameの修正はFRONT-02で確認済み） |
| §20 build/Mod/LLVM/native | SPEC-MOD-01, MOD-01, MOD-02, BUILD-01, NATIVE-01, FFI-01, MODULE-03, MAINT-02 |
| §21 layout/metadata/generic/ABI/runtime | LAYOUT-01, ABI-01, META-01, GCODE-01, GCODE-02, GCODE-03, GCODE-04, GCODE-05, CALLABLE-03, OBJECT-02, OBJECT-03, OBJECT-04 |
| §22 Core/startup/FFI/Windows | CORE-01, CORE-02, CORE-03, STATIC-01, FFI-01, BUILD-01 |
| Appendix A 検証義務 | 各機能の検証欄、ACCEPT-01, ACCEPT-02 |
| Appendix B 実装指針 | GCODE-04, QUALITY-01。参考algorithmを言語の追加受理条件にしない |
| Appendix C 実装状況 | BASE各項目、ACCEPT-03 |
| Appendix D/E/F 将来範囲・用語・文法 | SPEC-01, SPEC-02, FRONT-01, ACCEPT-03 |
| 冒頭本文のComposition/test方針 | SPEC-COMPOSE-01/02, SPEC-TEST-01, SPEC-VERIFY-01, SPEC-TEST-02, COMPOSE-01/02, LANGTEST-01/02。本文の確定方針を保持し、未記載の公開契約は決定待ち |
| 言語仕様外の既存製品部品 | MAINT-01, MAINT-02, LSP-01, LSP-02, EDITOR-01, QUALITY-01 |

## 13. 仕様不足・対象外

### 13.1 本文から実装する範囲

SPEC冒頭のroot/Std接続・製品/test分離、§18/20の直接参照・定義側環境・複数版、§18.3の情報保存、§21.3.4の意味計画再利用、§20.7のMod、§20.8/21.5のnative供給を保持する。外部リンクの採用宣言だけから未記載の文法・設定・形式を決めない。検証済み意味計画と、再生成するABI/context/frame/budget/IRを区別する。

### 13.2 対象外の境界

- 初期profileはi128/u128の除算・剰余・typed値とfloatの相互変換を診断する。char/bool数値変換も許可しない。
- virtual/override・runtime Contract View・一般checked cast/as・排他的Slice・専用Option/Result伝播構文・source並行機能・re-export・永続object cache・非Windows nativeは追加しない。共有Slice、runtime is/upcast、明示require/return/matchは必須範囲に残る。
- string連結の取得規則が未決でも、確定済みStringify・補間はCORE-02で進める。Range自体の反復・lending iterator・未導入FFI拡張は含めない。
- 旧SPEC-TEST-API-01（公開一時directory API）、CLI-CHECK-01（新規kimi check）、ARTIFACT-02（新規pack/publish）、ARTIFACT-03（SourceId/store verify）は本文根拠がなく対象外。実装済みや任意項目へ付け替えない。
- Dependencies/lock/restore、graph全体の単一版/DAG、schema 1/SourceId、doc固有のCOFF閉包・weak/COMDAT規則を追加要件にしない。既存Project.Check、NuGet配布検証、意味cache、本文のkernel32/供給検証は維持する。
- OPTIONAL-AOT-01は明示指定時のみ実施し、通常完了の必須依存にしない。

### 13.3 決定待ちと解除条件

| 計画ID / 監査ID | 本文に不足する決定 | 解除条件・独立して進める範囲 |
| --- | --- | --- |
| SPEC-COMPOSE-01 / AF-0002 | Entryの宣言/参照文法、型/適合・アクセス | 本文と正負source例の確定後、COMPOSE-01へ。現行$abortは独立 |
| SPEC-COMPOSE-02 / AF-0002 | Provider指定・既定選択・欠落/重複/不適合 | 本文と正負設定例の確定後、COMPOSE-01へ。Provider変更時の内部失効検証は先行可能 |
| SPEC-TEST-01 / AF-0002 | #Test付与先・署名・所属/発見/選択、通常mode/非選択本体の検査 | 本文と正負例でLANGTEST-01の所属部分を解除。製品→test依存は禁止 |
| SPEC-VERIFY-01 / AF-0002 | $expect/$requireの型・評価順/効果・メッセージ・失敗後制御/cleanup | 本文と操作別正負例でLANGTEST-01へ。通常require文と$abortは独立 |
| SPEC-DEPS-01 / AF-0011 | §18.1の参照名/版/取得先設定・graph診断 | 本文と正負設定例でBUILD-01の外部接続へ。DEP-01/MODULE-01の内部snapshotは先行可能 |
| SPEC-ARTIFACT-01 / AF-0011 | §18.3の交換encoding・必須field・validation/compatibility | 本文と交換/破損/非互換例でARTIFACT-01へ。内部module・意味cacheは独立 |
| SPEC-API-01 / AF-0007 | §15.7 Exchange/Swapのsource綴り・名前空間/intrinsic解決 | 本文と正負例でOWN-02のsource接続へ。責任移転・非重複の内部計画、Array内部移送は独立 |

Mod host（SPEC-MOD-01）、隔離runnerのtransport/上限（SPEC-TEST-02）、意味cacheのencoding（CACHE-01）は内部設計として具体化する。未解消の実装指摘AF-0005/0006/0008は、それぞれLSP/拡張、CI、module/成果物の計画へ対応する。

## 14. 直近の数値変換

着手点は [Binding.Conversions.cs](Kimi/Compiler/Binding/Binding.Conversions.cs) と [FloatConversionEmissionTest.cs](xUnitTest/Tests/FloatConversionEmissionTest.cs)。既存の拒否期待を正負・境界テストへ置き換え、ConversionBinding → 所有権/値計画 → check/CFG → LLVM writer → Abort理由/元位置を一体で接続する。既存整数の診断IDを保ち、不正計画の検査を省略しない。

### 14.1 NUM-04-FLOAT

- f64→f32はnearest/ties-even。正負の最大有限値・overflow境界と隣接f64、subnormal・underflowの符号付き0を検証する。**有限値が±infinityになる場合だけAbort**し、入力NaN/±infinityは受理する。NaN payload保持は要求しない。通常float算術には新たなoverflow Abortを加えない。
- direct float literalは対象精度へ一回丸める。括弧・直接符号・serializationを保持し、範囲外は未使用body/偽枝でも静的診断。typed f64の変換失敗は評価時だけAbortする。f64→f32→f64の中間丸め、f32→f64/同型の既存挙動も検証する。
- 単一評価・named引数・discard・phi/loop結果・return/defer・aggregate payloadを確認。失敗後の引数/書込/cleanupは実行しない。source/target/分類/operand/check・支配関係の破損をIR前に拒否する。

### 14.2 NUM-04-INTEGER

10整数型（i8/u8/i16/u16/i32/u32/i64/u64/isize/usize）とf32/f64の全40方向を実装し、fixture一覧で行列を確認する。

| 変換 | 境界・検証 |
| --- | --- |
| float→signed N-bit | 元精度p=24/53。N≤pなら x > −2^(N−1)−1、N>pなら x ≥ −2^(N−1)。上限は x < 2^(N−1)。ordered比較成功後だけfptosi |
| float→unsigned N-bit | −1 < x < 2^N。ordered比較成功後だけfptoui。NaN/±infinityを拒否し、投機的変換をselectで隠さない |
| 全20 float→整数pair | 両端と上下の隣接float、正負fraction・±0。−128.75→i8=−128、−0.75→u8=0、−1→u8とf32 2147483648→i32はAbort |
| 逆20pair | signed/unsignedの最小/最大、精度境界のties-even、0→+0、64bit isize/usize。si/uitofpの符号を保ち、中間floatで二重丸めしない。64bit整数からfinite→infinityは生じない |
| 整数literal→float | exact magnitudeから直接丸め、先にi32/i64へfitしない。基数/区切り・直接符号/括弧・表現上限・serialization・ties/二重丸め反例を検証。u128最大magnitude→f32は静的失敗、→f64は可能。整数の直接符号付き0は+0、float −0.0は−0 |
| float literal→整数 | 通常のf64 source Typeを先に決めてtruncate/range検査。`1@f32`と`1.0@i32`を区別し、typed/general式へtarget型を逆流させない |

typed128bit↔floatの8方向とchar/bool数値変換は、未使用body/偽枝でも最適化前に拒否する。連鎖ごとの丸め・元位置・失敗順、破損計画、再解析、warm allocation、実objectの未供給helper不在を確認する。

両子IDの完了には現compilerのmanaged正負テスト・LLVM verifier/O0/O2が必要。NUM-04全体には一般同型取得・明示Semantics/略記・borrow優先/Origin/Copy/Moveも残る。子IDだけで完成扱いにしない。

## 15. Appendix Aの必須検証と実装箇所の対応

テスト名は拡張先。現時点の全機能対応や成功を意味しない。

| 規範 | 主担当ID・実装領域 | 正常系 / 不正使用・無効化の検証 |
| --- | --- | --- |
| A.1 source identity | FRONT-01, BIND-01, MODULE-01, MOD-02; SourceDocument/CodeContext/Compilation/Parser | SourceDocumentAndDiagnostic、IdentifierIdentity、KotonohaSerialization: immutable snapshot/fragmentの元位置保持 / 別source alias漏れ・古いcontext再利用拒否 |
| A.2 directive | BASE-03, FRONT-01, FRONT-02, MOD-02; Tokenizer/Parser/CompileTimeConditionEvaluator | CompilationSpecification/DirectiveConditionValidation/CompileTimeSwitch: case-sensitive設定・選択項目を同scopeへ統合 / 短絡・未選択armの不正Condition、false #ifの境界 |
| A.3 Binding/incremental | TYPE-01/02, BIND-01/02/03, BORROW-04, MOD-01/02, CACHE-01; Binding.* | Binding/InheritedReceiver/ConditionalConformance/CompilationSpecification: candidateの選択・定義環境保存 / 名前追加・アクセス変更・不存在/効果/古いproofの失効 |
| A.4 Property | PROP-01/02/03, OWN-01/03; Binding.Properties / accessor lowering | PropertyBinding/PropertyRevisionParse: 標準操作とcustom ABI・一回評価 / private-set Move、部分構築、getter-resultの誤borrow |
| A.5 pointer | UNSAFE-01, FFI-01; Binding.Conversions / pointer emission | 合法なsigned offset/stride/typed access / 不正型・unsafe欠落・未証明inbounds/noalias。runtime UBを新しいAbortへ変更しない |
| A.6 literal | BASE-03, NUM-01/02/03/04, NUM-04-FLOAT, NUM-04-INTEGER, CORE-02; Number/Char/StringLiteralと値計画 | NumberLiteral/CharLiteralParse/StringLiteralParse: exact値・原文と対象精度 / 範囲外・二重丸め・破損値。数値変換は§14 |
| A.7 cleanup | OWN-01/02/03, FLOW-01, ABI-01, AGG-02; OwnershipAnalysis.Cleanup / BodyLowering.Results | Deferred/Result/AggregateEmission: 登録と退出・結果確保・逆順 / 未完成層deinit、cleanup非終了、二重破棄、破損計画 |
| A.8 callable/object | CALLABLE-01/02/03, OBJECT-01–06, META-01; capture/effect/metadata/runtime | capture順・16-byte関数値・object/Weak責任 / self-borrow escape・count overflow・upgrade競合・Origin消去誤用 |
| A.9 refinement | OBJECT-05, FLOW-01; Binding.RuntimeTypeTests / ControlFlow / lowering | RuntimeTypeTest/ControlFlowAnalysis: require支配の有効型 / 保存boolの偽provenance・書換え/aliasによる失効 |
| A.10 enum/Pattern | AGG-03/04, CORE-01; Binding.Patterns / MatchCoverage / OwnershipAnalysis.Match | EnumBinding/PatternBinding/MatchOwnership: Case/tuple・candidate/body・guard順 / 型不一致・非網羅・部分Move/残存cleanup・covered arm不正 |
| A.11 static Contract | BIND-03, PROP-03, OBJECT-01; Binding.Contracts/ConditionalConformances | Contract/ConstraintBinding: witness/associated Type/conditional適合 / 循環証明・Unknown・不正receiver保証・公開状態の撤回 |
| A.12 generic | TYPE-02, ORIGIN-01, GEN-01/02, GCODE-01–05; Binding/Analysis/Emission | SpecReview/Constraint/Length拡張: schema・普遍body・明示選択 / 未使用generic不正・Pending不正認証・型増殖・古い選択集合 |
| A.13 sequences | SEQ-01–05, ITER-01, BORROW-05; Range/Collection BindingとCore/runtime | RangeIndex/CollectionLiteral/For拡張: bounds/SharedReadResult/容量・順序・反復 / 負index・借用中変更・duplicate key・Range反復・排他Slice拒否 |
| A.14 LLVM/runtime | NUM各ID, LAYOUT-01, ABI-01, META-01, GCODE各ID, OBJECT/CALLABLE各ID, BASE-07, NATIVE-01, ACCEPT-02; Emission/backend | managed IR+O0/O2 native、helper/COFF/unwind/ABI、fault adapter、product/test予算 / supply/hash/未知symbol・破損plan・予算0・非終了。NativeAOTは別の任意ID |
| A.15 control flow | FLOW-01, FAIL-01, AGG-02/04, LANGTEST-01; ControlFlow/Ownership/BodyLowering | CurrentControlFlow/ControlFlowConformance/Result/Match: 構造上と実行上の完了・値確保・順序 / 未使用body/定数偽枝の必須診断、非終了cleanup後の偽結果 |
