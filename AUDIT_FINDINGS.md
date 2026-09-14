# Audit findings

初回作成は2026-09-14のPlan試行（0001-plan）。0002-planで既存9 IDを保持して2 IDを追加し、0003-plan-auditでAF-0012を追加した。現在はPlan cycle 2再開（0007-plan）。対象HEADは`ff053be41aa4ea70ec34a0d43df63a1988bf9da7`。前2枠の実装と未受理Plan 0006の部分作業・開始前の外部差分を保持し、下表の12 IDと採否/状態を維持した。今回も新しいCompletion Audit入力はない。

正式な仕様根拠はSPEC.md本文のみ。doc/の検索・読込・参照・本文統合を行わず、前試行の採用主張を計画§13で照合し直した。本文不足は確定した必須規則と分離し、docだけの詳細は対象外履歴へ移した。plannedは対応計画がある状態であり、実装修正/検証の完了ではない。

証拠prefix **P**は`.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0001-plan/`、**Q**は同runの`0002-plan/`。その時点の445入力とDLL4件の一致はQ/prior-input-comparison.json・prior-binary-comparison.jsonへ保存済み。現在の証拠は**U**=`0007-plan/`、前の未受理試行は**T**=`0006-plan/`、再利用対象は**S**=`0005-implementation/`。過去と今回の実行を計画§2.4–2.7で区別する。下記の過去段階の本文は当時の結果であり、現在の状態は表のStateと末尾のPlan 0007に従う。

| ID | Priority | State | Plan / SPEC | Finding / evidence / resolution |
| --- | --- | --- | --- | --- |
| AF-0001 | required | resolved | BASE-07, ACCEPT-02; SPEC A.14 | 前Planで発見。旧native 68件・930 fixture/1,860実行、CLI/LSPのログ/入力hashが現環境で照合できずBASE-07は検証待ち。前試行managed両構成3,813成功はnativeの代用にならない。解除は現行build/空の生成領域からfixture一覧/hashを保存し、LLVM/O0/O2・CLI/LSPの実件数/失敗/skip/警告・終了コードとログを照合すること。P/llvm-preflight.jsonは事前確認のみ。Implementation 0004の部分検証: E/baseline-inputs.json・baseline-fixtures.jsonに現ソースと空から再生成した1,009 scalar fixture等のhashを保存。Debug/Release各3,968 managed、現Release runtime 68 O0/O2、CLI 8 scenario群、LSP基本通信とpolicy 3 scriptはexit 0（baseline-checks.json・baseline-emission-report.json・baseline-cli-report.json・baseline-integration-checks.json）。char/float 79 fixtureの計158 O0/O2成功は入力hash一致で再利用できるが、全1,009 fixture/2,018実行は時間見積り約35分以上のため今回未実行。slot 2で全件・入力不変を確認するまでBASE-07と本指摘を未完了/plannedに維持。 Implementation 0005で解消: S/scalars-result.jsonの全1,009/2,018成功、NUM-01とNUM-04部分の追加136/272成功、最終空からの両構成managed各4,123成功。最終5,731入力は旧/追加native証拠と全件hash一致。現CLI8 scenario群、LSP基本通信、runtime68、policy成功（S/float-convert-result.json・final-cli-result.json・final-lsp.json・current-emission-report.json）。 |
| AF-0002 | required | blocked | SPEC-COMPOSE-01, SPEC-COMPOSE-02, SPEC-TEST-01, SPEC-VERIFY-01; SPEC冒頭7/9行, 13.8, 21.3.4.2 | 前Planのdoc本文統合要求を今回撤回。本文には非置換root/Std接続/module identity分離と#Test/$expect/$require/kimi test・一方向依存/共通検証/隔離/有限報告回収があるが、Entry文法/適合、Provider選択設定、test所属/署名/非選択検査、検証操作の型/効果/失敗時制御は不足。旧docのgroup $/compose $等を使って埋めない。質問と影響は計画§13.3。各契約の本文・正常/不正例が決定されれば該当実装へ着手可能。FRONT/NUM、既存$abort、内部runner設計は独立。 |
| AF-0003 | required | resolved | NUM-01, NUM-04, OBJECT-01, OBJECT-05, SEQ-04, FAIL-01; SPEC 6.2.4/13.6/17.1/21.5.3/D | 前Planで発見した誤った必須範囲の計画文書修正。128bit除算/剰余・float相互変換の初期profile診断、virtual/override・排他的Slice・as/専用伝播の未導入境界を本文で再確認し、計画§13.2/各行に維持した。計画レベルの不一致の解消であり、対応する製品機能IDは未完了のまま。 |
| AF-0004 | required | resolved | NUM-03, NUM-02; SPEC 3.1.2–3/13.4/21.1.4/A.6/A.14 | 前Planの実CLIで合法なchar/f64のliteral→local→比較が各exit 1のMissing or inconsistent value-flow plan、i32対照exit 0。P/emission-probes.jsonとprobe入力/log、今回のsource/DLL hash一致で再利用。ScalarTypes.Supports/OwnershipAnalysis.Values/FunctionAbi/BodyLowering/Matchのgateを静的に再確認。解除は型付き値計画/ABI/結果/比較/正負managedと通常native O0/O2、破損計画拒否。Implementation 0004でNUM-03を解消: charのtyped値計画/比較/ABI/結果/match/既存aggregateと破損計画拒否、Debug/Release各3,905 managed成功、36 fixture/72 O0/O2 native成功（E/char-inputs.json・char-checks.json・char-fixtures.json・char-result.json）。NUM-02も選択精度の直接rounding、FP値計画/ABI/演算/比較/結果/aggregate、正負・破損計画拒否へ接続。E/float-checks.json・float-fixtures.json・float-result.json: Debug/Release各3,968 managed、43 fixture/86 O0/O2成功、元float CLI exit 0、単一rounding/serialization/NaN/±0/subnormal/warm allocationを確認し解消。 |
| AF-0005 | required | planned | MAINT-01, LSP-01, LSP-02, EDITOR-01 | 前Planで発見、今回ソース再確認。LspServerのopen/change診断送信がコメントでclose時の空診断だけ接続。extension.jsは隣接Debug DLLとDebugWait=true固定、package.jsonにhost統合テストなし。解除はsnapshot/版/URI/UTF-16診断、古い解析/close/取消し、設定したserverの起動停止を実hostで検証。node構文だけでは不可。P/node-check.result.jsonは環境不足で未起動。今回はnode/host未実行、前提準備を完了扱いしない。 |
| AF-0006 | required | planned | MAINT-02, ACCEPT-02 | 前Planで発見、今回test.yml/publish.ymlとglobal.jsonを再確認。Linux Release build後のdotnet testが構成/対象未指定で、MTPの明示起動、PR/Windows native/0件検知/ログ保存が未接続。解除はbuildとtestの構成/対象/配布物を揃え、cleanローカル再現と実workflow検証を区別して保存。NuGet既存配布は必須範囲に維持。NativeAOT/外部公開はこの修正に追加しない。 |
| AF-0007 | required | blocked | SPEC-API-01, OWN-02; SPEC 15.7 | 前Planで発見、本文再確認。Exchange/Swapの責任移転・非重複・初期化は確定するが最終API綴り/名前解決は別途。質問は概念表記を最終source APIとするか、名前空間/intrinsic解決をどう定めるか。本文と正常/不正例への決定で解除。内部所有権計画・Array内部移送・他の数値/借用実装は独立。内部テストだけでOWN-02の公開接続を完成としない。 |
| AF-0008 | required | planned | DEP-01, MODULE-01, MODULE-02, ARTIFACT-01, CACHE-01, BUILD-01, NATIVE-01; SPEC 18/20/21.3.4 | 前Planのdoc由来DAG/Dependencies/lock/SourceId/source配布CLI要件を撤回し本文へ限定。Compilation.Prepareのexternal load予定、final BindのみでMod未実行、LlvmEmitterの外部module/Library gateは現行ソースに残る。本文が求める定義側環境・直接参照/複数版identity・interface情報保存・意味plan再利用・native供給への接続不足は引き続き必須。内部snapshot経路DEP-01/MODULE-01は公開形式待ちから分離した。解除は対応IDをローカル正負fixtureと実ソースへ接続し、破損/内容変更/失敗時に旧成功を流用しないこと。外部形式不足はAF-0011で別管理。 |
| AF-0009 | required | dismissed | SPEC冒頭9行; 除外履歴はIMPLEMENTATION_PLAN.md 13.2 | 前Planの公開一時directory取得API待ちはdocだけが根拠。今回SPEC本文のtest方針/関連節を検索・読込し、公開APIもcase固有directoryも本文に規定されていないことを確認。SPEC-TEST-API-01を対象外履歴へ移しLANGTEST-02/SPEC-02の依存から除外。process隔離/有限回収そのものは必須として残す。本指摘の不採用はAPI実装済みやtest機能完成の主張ではない。 |
| AF-0010 | required | resolved | FRONT-02; SPEC 2.5/19.2–3/20.4 | Planで発見。Compilation.cs:72/166/199のOrdinalIgnoreCaseとCompilationSpecificationTest.cs:140/186の逆向き期待。QのCLI 5入力は1一致/4不一致。Plan AuditでR/condition-boundaries.jsonの新規7入力を実行し3一致/4不一致を確認（harness exit 0）。完全一致のFeature重複、選択済み後のCase条件、未選択arm内の条件、Feature設定に対するfeature参照を各exit 0で誤受理。False #if内部の未知名と日本語設定はexit 0、非NFC設定はInvalidCompileTimeSetting_Kd/exit 1で期待通り。Project.TryCreate:41→ProjectFile.CompileTimeSettings:41→Compilation.Prepare:211の読込経路では、Dictionary化後の比較子変更だけで入力の重複を復元できない。解除は§14.1にある重複読込検査を具体化し、Ordinal環境/metadata/serialization、正負/culture/NFC/短絡/非選択arm/再Prepareを修正して現build後の両構成managedとCLIで検証。Implementation 0004でCompilationのOrdinal環境とProjectFile.Loadの辞書化前検査を実装。E/front-checks.json: Debug/Release build各exit 0・警告0、近傍各173/全managed各3,830成功・失敗0/skip0。E/front-cli.json・front-selection.json: Q/Rの元入力hash一致、各構成12件の期待exit/元source診断/選択IR一致。warm lookup 0 byte、NFC/culture/serialization/再Prepare/失敗resetの回帰も成功し解消条件を充足。 |
| AF-0011 | required | blocked | SPEC-DEPS-01, SPEC-ARTIFACT-01; SPEC 18.1/18.3/20.1–2 | 今回Planでdoc除外後に明確化した独立の公開契約不足。§18.1は版付き参照設定/graph診断、§18.3はencoding/required fields/validation/compatibilityを別仕様に委ねており、本文だけでは実装受入条件を決められない。KotonohaIdentifier.csはName/Versionのみで取得先なし。質問は参照名/版/取得先と診断の設定、およびsource/config再構築を満たす交換形式。本文と正負例の決定を契約別に保存して解除。内部identity/読込/意味cacheは独立。旧doc形式の導入や必須情報保存の削除で解消しない。 |
| AF-0012 | advisory | resolved | NUM-02, NUM-03; IMPLEMENTATION_PLAN.md 243–244, 14.2–14.3 | Plan Auditで発見。NUM-02行の検証先§14.2はchar、NUM-03行の§14.1はFRONT-02を指している。正しい詳細契約はNUM-02→§14.3、NUM-03→§14.2。詳細表自体には必要な正負/native条件があるので着手を止めない。Implementation 0004でNUM-02→§14.3、NUM-03→§14.2へ参照を修正し、各行と見出しの一致を再照合した。製品完了の主張ではない。 |

## Plan Audit 0003（2026-09-14）

証拠 **R** は `.codex-loop/runs/09c17562dc0a4deea6af1a09c467e069/0003-plan-audit/`。上記のP/Qは過去のPlan証拠であり、Rの実行と区別する。

- **監査範囲:** 現行104行、今回の計画差分、SPEC章索引とAppendix A.1–15、§13の本文根拠/除外履歴、公開契約不足、FRONT-02→NUM-03（後続NUM-02）の受入条件。Compilation/Project読込/条件評価と近傍テスト、charの値計画・ABI・aggregate/match gate、LSP/CIの未接続を確認。変更のない全実コードの全面再調査は行っていない。
- **依存と完了主張:** R/structural-audit.jsonは104 ID・[x]7・必須未完了96、ACCEPT-03から必須103 IDへ到達し、未定義/後方依存/循環/完了行の未完了依存は0。公開契約待ちは局所的で、FRONT-02/NUM/DEP-01/内部設計を止めない。BASE-03等の[x]は既存suiteの限定範囲で、§19全体の適合とは記されていないため直接変更を要求しない。
- **既存証拠:** R/prior-evidence-comparison.jsonでPのsource/config 445入力とDLL 4件のSHA-256差異0。PのDebug/Release build警告0/エラー0、各managed 3,813成功/失敗0/skip0とログを再利用。今回build/全suiteを実行したとは扱わない。nativeの旧成功数は照合できずAF-0001を維持。char/f64の既知失敗もソースとgateが一致しAF-0004を維持する。
- **規範の境界:** AF-0003の初期128bit診断・将来dispatch/cast/排他Slice/専用伝播の除外、AF-0009と4旧IDのdocだけの要求除外は妥当。AF-0002/0007/0011は本文にない公開文法/API/形式の決定待ちで、内部形式/runner/Mod設計まで外部待ちにしない。B.7を新たなソース受理規則にせず、A.14が要求する予算/生成検証へ対応する現計画を維持する。
- **新規再現:** `pwsh -NoProfile -File R/probe-condition-boundaries.ps1`はexit 0、CLI 7件・期待一致3/不一致4・skip0。4件の製品不一致をAF-0010へ統合した。同一キー重複は計画§14.1で既に検査対象のため重複IDを作らず、疑いから再現済みへ更新。UnknownCompileTimeNameの診断/位置は修正後の確認事項であり、現在成功したとは主張しない。
- **Implementationへの操作:** FRONT-02の計画へ同一キーの実CLI証拠と読込時検査の対応箇所を同期して実装する。入力の生のキー列が失われる前に重複を検出し、正常な設定round-trip・綴りの保持・未設定名の元source診断・False #if境界を保つ。過去Qの5件とRの7件、近傍managedと現build後のDebug/Release全suiteで閉じる。完了後NUM-03へ進み、LLVM verifier/O0/O2を実行して初めて実行対応を認証する。AF-0012の参照修正は同時に可能。

この監査で製品実装や計画本体を変更していない。必須未解消はAF-0001/0002/0004/0005/0006/0007/0008/0010/0011の9件を維持し、advisoryのAF-0012を追加した。通常native、NativeAOT、VS Code host、CI workflow、restore/pack/publish、新規benchmarkは今回未実行。計画に追加の必須欠落は確認されなかったが、既存必須指摘の解消前なので結果はfindingsであり、製品の完成認定ではない。

## Implementation 0004: 採否と結果

AF-0010の追加再現を採用し、FRONT-02/§14.1へDictionary集約前の検査とQ/R全12入力の回帰を同期した。製品修正が必要なのでplannedを維持。AF-0012は参照修正を採用・照合してresolved。他のrequired指摘の既存採否・対応計画とAF-0003/0009の解消・除外根拠を維持し、公開契約不足を創作で補わない。対象FRONT-02にblocking指摘はない。

証拠 **E** は同runの `0004-implementation/`。開始時に採用したAF-0010は製品修正・両構成managed・元CLI入力の再検証でresolved。AF-0004もchar/floatの型付き値計画からO0/O2まで接続してresolved。AF-0012は計画参照の照合でresolved。AF-0001は上記の部分証拠を追記したが全scalarが未実行なのでplanned。他の採否・優先度・解除条件は維持し、仕様決定待ちを新規文法で補っていない。
## Implementation 0005（slot 2）

AF-0001を採用して全件検証を継続し解消。証拠Sは同runの0005-implementation。S/scalars-result.jsonは1,009 fixture/2,018 O0/O2成功・入力不変。NUM-01の追加118 fixture/236実行も成功。S/current-checks.jsonで空から生成したDebug/Release各4,092 managed・build警告0、S/current-native-coverage.jsonで5,641入力全件hash一致。現runtime68実行・CLI8 scenario群・LSP基本通信・policy成功。default copied dlltoolの権限失敗は製品失敗と区別し、S/current-kernel32-explicit.jsonで実行可能な同版LLVMを明示して成功。S/current-cli-result.jsonも現Release・通常nativeでexit0。BASE-07の限定範囲の解消でありACCEPT-02やLSP診断/VS Code host/CI全体の完了ではない。ほかのrequired指摘の採否・優先度・解除条件は維持。

## Plan 0006（cycle 2、2026-09-14、保護対象変更で未受理）

- **採否/状態:** AF-0001/0004/0010/0012は前2枠の実ソース・近傍テスト・保存ログ/入力一致へ照合しresolvedを維持。新たにresolvedへ変更した指摘はない。AF-0003の初期profile/将来境界とAF-0009のdocだけの要求除外も変更しない。前Plan Auditの指摘は全件対応計画/証拠があり、新しいCompletion Audit入力はない。
- **未解消全件:** AF-0005/0006/0008はplanned。LSPのopen/change診断未接続、拡張のDebug DLL/DebugWait固定、CI構成/対象未指定とWindows native/PR検証不足、external module/Mod/Library gateを現ソースで再確認した。AF-0002/0007/0011はblockedのまま、計画§13.3に契約別の質問・影響・解除条件を保持。数値実装や内部snapshotの独立作業を止めない。
- **残実装を計画へ反映:** Binding.Conversions/BodyLowering/WriterとFloatConversionEmissionTestで既知NUM-04の未許可行列を確認。NUM-04-FLOAT/NUM-04-INTEGERを新設し§14.6–14.8へSPEC本文の境界式・正負例・LLVM/診断検証を対応付けた。既にNUM-04が保持する同じ未実装を新しいAFとして重複登録しない。一般同型取得/明示Semantics等の必須範囲とNUM-04の未完了を維持した。
- **今回の証拠:** T/initial-comparison.jsonのsource/config等466入力・fixture5,731入力・Release DLL2件のhash差異0（capture-inputs.ps1、exit 0）。T/prior-evidence-summary.jsonにSの両構成managed各4,123成功・警告/失敗/skip0、最終scalar1,145/2,290 O0/O2の保存ログ/対応を集約。T/numeric-source-evidence.txt・product-gates.txtは現ソースの接続/拒否箇所。今回build/test/native/host/workflowは再実行しておらず、過去の成功と今回のread-only照合を区別する。
- **最終照合の欠落:** T/final-observed-change.json（実コマンドexit 1）でroot bin/のfixture5,731入力とautoimpl/Design.mdの欠落を検出。T/generated-path-observations.jsonで所在を確認し、source/config466入力・Release DLL2件の一致は維持した。このPlanの削除/移動ではなく原因未確認。開始時の入力一致と保存済み実行証拠は有効なのでAF-0001を単なる生成物欠落で再登録/再開せず、計画§2.6/14.6へ次枠の再生成・hash照合を追加した。現fixtureの存在・最終hash照合成功は主張しない。

必須未解消は **AF-0002、AF-0005、AF-0006、AF-0007、AF-0008、AF-0011** の6件。追加指摘なし。T/plan-validation.jsonで一意な表行/計画依存/全件列挙を検査し、T/final-comparison.jsonで編集範囲と保存を照合する。これはPlan Auditへの提出準備であり計画承認・製品完成の認定ではない。

## Plan 0007（cycle 2再開、2026-09-14）

- **再開と採否:** runnerは前Planのautoimpl/Design.mdの保護対象変更を拒否した。原因/実行主体は確定せず、現在の欠落とautoimpl2/の14追加入力をU/initial-comparison.jsonに開始前の差分として保存した。復元/移動/編集は行わない。製品の新規問題として重複IDを登録せず、計画§2.7とSTATUSへ実行境界・解除手順を保存した。
- **状態の維持:** AF-0001/0004/0010/0012等の過去の解消は、現source/config466入力・Release DLL2件のhash一致と保存された実logを根拠に維持。現在のfixture5,731入力は欠落し、現native成功は未確認である。次Implementationの両構成build/testによる再生成・S manifestとの照合をNUM-04-FLOATの最初の操作に残す。新たにresolvedへ変更した指摘はない。
- **未解消全件と実装接続:** AF-0005/0006/0008はplanned。現LSP/拡張/CI/moduleの未接続はU/product-gates.txt、数値変換の既知不足はU/numeric-source-evidence.txtで確認し、既存NUM-04-FLOAT/INTEGERの計画へ対応付けた。AF-0002/0007/0011はblockedで、SPEC本文だけでは不足する公開契約の質問/影響/解除条件を§13.3に維持。docだけの対象外履歴とAF-0009のdismissedも維持する。
- **検証範囲:** 開始時U/capture.ps1・U/review.ps1はexit 0、静的調査・過去log/入力照合のみ。Sの両構成managed各4,123成功・失敗0・skip0とbuild警告0を今回の実行に数えない。U/validate-plan.ps1はexit 0、全106 ID/必須依存/12指摘/状態不変・git diff --checkに不整合なし。U/capture.ps1 -Finalは03:29:25Zにexit 1で、autoimpl2/の変更1入力・追加2入力を検出（first-final-observed-changes.json）。本実行はこれらを編集しておらず、実行主体は未特定。製品source/configとDLLは不変だが、作業ツリー全体の差分ゼロを主張しない。最終snapshotをU/final-comparison.jsonへ保存する。今回の製品build/test/native、fixture再生成、host/workflow/公開/NativeAOTは未実行。

必須未解消は **AF-0002、AF-0005、AF-0006、AF-0007、AF-0008、AF-0011** の6件。対応計画または待ち条件があり、数値2枠と内部snapshot等の独立作業を続行できる。Plan Auditへの提出準備であり、計画承認・完成認定ではない。
