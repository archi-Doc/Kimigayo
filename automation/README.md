# Implementation automation

PowerShell 7.4以降で、次の順序を実行します。

~~~text
1ラウンド:
    Plan → Plan Audit → Implementation → Implementation
    Plan → Plan Audit → Implementation → Implementation
    Plan → Plan Audit → Implementation → Implementation
    Completion Audit

完成確認 → 終了
不足あり → 次ラウンドのPlan
外部待ち・停滞・実行エラー → 停止
~~~

Planは計画を差分更新し、Plan Auditは計画を直接編集せず指摘を残します。
Implementationの1回目は指摘への対応を計画へ反映してから実装し、2回目は残実装・検証を優先します。
Completion Auditも計画を直接編集せず、不足を次のPlanへ渡します。

## 実行

リポジトリルートで実行してください。スクリプトの既定ProjectRootはautomation/の親なので、別の作業ディレクトリからも起動できます。

~~~powershell
# 通常実行: 最大10ラウンド、各段階90分まで
pwsh -NoProfile -File ./automation/codex-loop.ps1

# 1ラウンドに制限（正常経路では実装6回、全13段階）
pwsh -NoProfile -File ./automation/codex-loop.ps1 -MaxRounds 1

# 停止原因を解消した後、保存された実行を再開
pwsh -NoProfile -File ./automation/codex-loop.ps1 -Resume -MaxRounds 10
~~~

必要なものはGit、利用可能な認証・設定を持つCodex CLI、対象プロジェクトの開発環境です。
モデルはCLI設定を引き継ぎます。起動にはstdin、ephemeral、workspace-write、自動承認レビュー、JSON Schema、最終応答ファイルを使用します。
`--approve-for-me`自体がworkspace-writeと自動承認レビューを選択するため、`--sandbox workspace-write`は併記しません。両方を渡すとCLIの引数解析で終了コード2になります。
使用中のCLIがこれらの引数を提供することは codex exec --help で確認できます。
CLIの非対話・構造化出力については[公式ドキュメント](https://learn.chatgpt.com/docs/non-interactive-mode)を参照してください。
起動前にAGENTS.md、SPEC.md、STATUS.mdと必要なautomationファイルの存在を検査します。計画と指摘台帳は初回Planで作成できます。既存の認証・モデル設定を用い、このスクリプトから設定や認証を変更しません。

| 引数 | 既定値・意味 |
| --- | --- |
| ProjectRoot | automation/の親。Gitリポジトリのルートを指定 |
| MaxRounds | 10。保存された実行全体のラウンド上限。上限停止後の再開では増やす |
| StageTimeoutMinutes | 90。各Codexプロセスの最大実行時間 |
| CodexCommand | codex。必要ならCLI実行ファイルやラッパーのパス |
| Resume | 保存状態を読み、部分変更を確認するためPlanから再開 |

旧版のMaxImplementationRuns、AuditInterval、FailureAuditThreshold、個別Prompt引数は廃止しました。
プロンプトはこのディレクトリのファイルを使用します。共通契約、段階別プロンプト、動的な実行情報の順に渡します。

## 例外時の遷移

- Implementationがcompleteを返すと、残りの枠を省略してCompletion Auditへ進みます。完成報告だけで終了コード0にはしません。
- Implementationがreplanを返すと、残りの実装枠を中止し次サイクルのPlanへ戻ります。3サイクル目なら次ラウンドのPlanへ進みます。この場合もラウンド上限を消費します。
- 進捗なしが2実装回連続すると再計画し、再計画を挟んでも計4実装回連続で進捗がなければ停止します。Plan/Auditではこのカウンターをリセットしません。
- progressは新しい実装・検証・原因究明の証拠に基づく実装者の報告です。コード差分の量やチェック数だけでは判定しません。報告の意味的な正しさは監査で確認します。
- ready / approvedにも根拠を必須とします。complete / verified_completeは必須の未完了計画行がある場合に拒否します。task_idsは実在する計画IDと照合し、finding_idsは台帳の未解消blocking / requiredを省略なく列挙しているか確認します。チェックや証拠の意味的な正しさ、計画外の仕様漏れは引き続き監査で確認します。
- blockedはどの段階でも即停止します。実行失敗、不正/欠落JSON、不整合な結果、編集範囲違反も自動再試行せず停止します。
- タイムアウトでは子プロセスツリーを終了します。プロンプトに上限・UTC deadlineと引継ぎ用の余裕を渡します。CLI失敗・タイムアウト時も終了後のファイルhashと編集範囲を確認し、部分変更を残します。Ctrl+C等の中断後も途中の作業を保存したまま、明示的に再開してください。

## 記録と再開

- IMPLEMENTATION_PLAN.md: `| [ ] TASK-01 | 実装内容 | 完了条件 | 依存 | 検証方法 |` の表形式。任意・条件付き項目だけOPTIONAL-接頭辞を使い、未指定NativeAOTを必須にしません。
- AUDIT_FINDINGS.md: `| AF-0001 | required | open | 対象 | 根拠・対応・解消条件 |` の表形式。先頭3列はID / Priority / State。詳細は[共通契約](common-prompt.md)を参照。Planが初回作成・旧形式の移行を行い、各段階が採否・対応・検証を引き継ぎます。
- STATUS.md: 先頭の「現在の自動実装引継ぎ」を更新し、対象ID、差分、検証、残作業、次の操作、外部待ち条件を保存。
- .codex-loop/state.json: ラウンド、サイクル、段階、停滞回数、直近結果、停止理由。
- .codex-loop/runs/実行ID/: 実行ごとの状態、入力プロンプト、Schema、結果JSON、stdout/stderr、前後のファイルhash。

状態は一時ファイル経由で保存し、同じリポジトリの二重起動はファイルロックで拒否します。
生成状態は専用.gitignoreでGitの通常追跡から除外します。既存の部分変更を自動で破棄することはありません。
編集範囲の検査は生成状態を除いたGit追跡対象・未追跡非ignoreファイルを対象とする事後検査であり、OSの書込制限ではありません。doc/もhash対象とし、Implementationでもautomation/、doc/、AGENTS.md、.codex/、.agents/の変更を拒否します。ignoreされたファイル、Gitのindexや履歴、外部サービスの変更を防止する仕組みではありません。

- Resumeなし: 新しい実行IDでPlanから開始し、既存の計画・指摘・ソースは引き継ぎます。過去の実行ログは保持します。
- Resumeあり: 同じ実行ID・ラウンドを維持し、Planから状態を再確認します。明示再開で停滞カウンターをリセットします。
- 完成済みのResume: 記録したリポジトリ入力が同じなら再実行せず終了します。変更があればPlanから再監査します。ignoreされた入力や外部toolchain等が変わった場合はResumeなしで新たに監査してください。
- 状態形式はversion 3です。version 2以前の完成記録は新しい判定条件を満たした証拠ではないため、旧版や破損状態のResumeは拒否します。Resumeなしで開始すると、ソース・計画・指摘を保持し、新しい実行IDで再確認します。

| 終了コード | 意味 |
| --- | --- |
| 0 | Completion Auditで完成を確認 |
| 2 | ラウンド上限。未完成 |
| 3 | 外部情報・権限・仕様決定待ち |
| 4 | 再計画後も進捗なし |
| 5 | CLI/JSON/状態/編集範囲/起動等のエラー |
| 6 | 段階タイムアウト |

上限到達や実行エラーを完成と扱わないでください。Completion Auditが必要なbuild・テストを実施するため、旧版の構成未指定build/testを外側で重複実行する処理は廃止しました。
プロンプトはdoc/の広域調査を省き、SPECが明示採用する文書だけを必要な契約の確認に利用します。採用済み設計のSPEC本文統合と未決定事項を分け、無関係な実装を止めません。NativeAOTは明示指定時だけ実施し、未決定の言語仕様を自動承認しません。

SPEC・STATUSは章索引と現行引継ぎから必要範囲を読み、毎回全履歴を読み直さないようにします。[検証ガイド](verification-guide.md)は現在のMicrosoft.Testing.Platformの起動形、構成の一致、fixture再生成、Debug/Releaseとnative検証の出力競合、ログ保存を整理しています。

## 検証

~~~powershell
# 起動引数と、実行エラーから全4段階を経る再開を短時間で検証
pwsh -NoProfile -File ./automation/test-codex-loop.ps1 -Smoke

# 一時Gitリポジトリと疑似CLIだけで順序・状態・停止・異常系を検証
pwsh -NoProfile -File ./automation/test-codex-loop.ps1

# 実際の1分タイムアウトによる停止も含める
pwsh -NoProfile -File ./automation/test-codex-loop.ps1 -IncludeTimeout
~~~

テストは実Codex/API、製品コンパイラー、NativeAOTを起動しません。一時領域のログを残し、失敗時の調査先を表示します。
