# Plan review

- 正本: input.json の plan_record / logical_path。PLAN.md の制御JSONと現行定義を照合。履歴の記録は参照していない。入力signature: a203e05f2e8e128a4e8438d9dda68352d6f28043591dff6fb17a47922ba79fd1。
- 期待: 全完成条件の必須task対応、非循環依存、全必須taskの到達条件付きmilestone対応、許可範囲内の必要成果物。実結果: plan-check.json参照。既存35項目のID・required・acceptance・verification・source_ids・criterion_ids・replacementsと11 milestoneの到達条件は維持。
- 差分: proposed-changes.json。T01–T35の空deliverablesを担当コード/テスト/必要生成物へ対応付ける。生成物はbinのfixture/native/CLI/Mod検証出力、backend候補・採用物、managed出力、BenchmarkDotNet.Artifactsを含む。globは当該taskの成果物照合範囲であり、全ファイルの変更を要求しない。個別試行のログ・証拠は各output_directoryへ保存する。bin/mod-tests/**はT22が新設する検証用生成source/provenanceの置場であり、現存・実装済みとは扱わない。
- T34の「基準と変更後で比較」を実装前に成立させるためT36を追加。source_ids=[T34]、既存T34は置換・削除しない。T01→T36→T02、T34にもT36依存、M11にT36を追加。新しい性能改善率や機能条件は課さない。T01は静的棚卸しのため先行可能。T36の基準データは後続試行の専用output_directoryにも保存し、T34の測定で上書きしない。
- 結論: ready。pending_work=false、uncertain/verify_targets/現行findings/現行evidenceはいずれも空、milestone verified_countはすべて0。回復検証に送る未受理Workはない。全36項目はpending。次はT01の規範条項台帳、その後T36の基準測定。後続Workerは起動していない。

## 現物の確認と限界

| 根拠 | 確認結果・計画上の扱い |
| --- | --- |
| SPEC.md §1.3、A.1–A.15/F.1–F.9の見出し、STATUS.md §§1–7 | 確定規則の規範性と段階別制限を確認。全条項本文・表・箇条書きの棚卸しはT01で未実施。C01を達成済みにはしない。doc/Design参照先は未読。 |
| xUnitTest/Tests/CoreCatalogTest.cs:15–30 | 6 validated / 18枠、残りMissingを期待する既存テスト。実行していない。T20の全Core完成要件を残す。 |
| Kimi/Compiler/Emission/LlvmEmitter.cs:132以降 | external module / Library / container / generic / capture等の拒否経路が現存。T21/T25/T26/T31等を残す。拒否条件は静的確認であり実行結果ではない。 |
| Benchmark/Program.cs、Benchmark/Benchmarks/BindingBenchmark.cs、Benchmark/Benchmark.csproj | T34指定workloadの登録、MemoryDiagnoser、net10.0を確認。基準測定の受理済み証拠はないためT36を実装変更の前提に追加。 |
| backend/windows-x64/test-emission.ps1、test-scalars.ps1、test-cli.ps1、build.ps1 | 構成別emissionと構成共有scalarの生成・消費先、候補backend検証の前提を確認。T32→T33と構成内直列検証を保持。 |
| backend/windows-x64/profile.json と plan-check.json pinned_identities | installed backend、llvm-dlltool、kernel32.defの実SHA-256が指定値と一致。LLVM/native試験成功や全ツール実行の再確認には読み替えない。入力environmentの既存probeと合わせて環境識別に使用。 |
| STATUS.md:5 | 削除済みIMPLEMENTATION_PLAN.mdへの旧リンクはT35の既存文書整合要件で修正対象。復元は計画しない。 |

## 実行記録

手順: output/check-plan.ps1をPowerShellで実行。詳細な対応表・順序・参照hash・446 tracked fileの安定性確認はplan-check.json、最終実行stdoutはplan-check.log。

最初の静的検査はJSON objectのキー順を文字列比較したためcompletion_criteriaで停止した。証拠生成用スクリプトだけを再帰的キー正規化へ修正し、再実行は成功。製品の失敗ではなく、初回結果を計画不整合や製品検証成功へ転用していない。

このPlanで未実施: T01全規範条項台帳、全製品実装、build、managed Debug/Release、LLVM verifier、通常native O0/O2、benchmark。NativeAOTは対象外につき未実行。STATUS.mdの過去の4,123成功という記載を現行受理証拠として採用していない。外部待ち・追加判断が必要な阻害要因は今回確認されていない。未実装の仕様事項を完成扱いしない。

PLAN.md、製品、指示、共通配布物、runnerの現行記録には書き込んでいない。作成した試行記録は本output_directory内のみ。Git statusは開始時・終了時とも?? .autoframe/のみ。保護確認は本試行の読取りと静的検査中の446ファイルhash比較の範囲であり、未参照領域・過去全履歴の完全差分を主張しない。
