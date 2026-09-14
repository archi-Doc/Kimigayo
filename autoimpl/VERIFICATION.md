# 検証記録

対象: autoframe仕様0.1のWindows実装。2026-09-14、PowerShell 7.6.6、Codex CLI 0.154.0。

## 疑似Worker

既存56件に回帰9件を追加し、計65件を確認した。修正後の全体63件と、最終マイルストーン修正後の追加2件はすべて成功。PowerShell構文検査、仕様のJSON例・PLAN Schema検査、日英の節番号照合、git diff --checkも成功。

配置先をautoframe/へ変更後、6段階の正常完了テストを再実行して成功した。ローカルリンクと見出し参照24件、日英のPLAN例、PowerShell構文も確認した。

```powershell
pwsh -NoProfile -File ./autoframe/tests/test.ps1
pwsh -NoProfile -File ./autoframe/tests/test.ps1 -Filter 'spec: milestone*'
pwsh -NoProfile -File ./autoframe/tests/test.ps1 -Filter 'successful six phases*'
```

| 仕様 | 主な確認内容 |
| --- | --- |
| §2 入力 | 厳密なJSON・Schema、ID・依存・参照、初期項目なしの計画生成、ユーザー条件の保持 |
| §3 記録 | 排他、原子的な受理、保存失敗、証拠破損の回復、進捗と計画版の分離 |
| §4 遷移 | 6段階の完了、読取専用作業、3件一括・部分成功、必須指摘、節目の監査、重複抑止 |
| §5 証拠・回復 | 入力・依存証拠の失効、保護範囲、PLAN変更、未確定成果、NewRun、完成証拠の欠落・破損 |
| §6 停止 | Workなしループ、停滞と明示解除、回復時の進捗判定、実際に渡す共通プロンプト |
| §7 Runner | 引数、設定継承、上限、ハートビート、時間切れ・キャンセル・強制終了、子停止、大量ログ、終了コード |
| §8 受理契約 | 段階別権限、対象IDの網羅、再受理拒否、最終WorkのVerify、完成直前の照合 |

## 今回の修正

| 問題 | 修正・回帰確認 |
| --- | --- |
| Planがユーザー指定の依存関係・criterion_idsを削除できた | 元の対応と依存順序を検査。中間項目を使う正当な詳細化は許容 |
| マイルストーン対象の削除と、内部データの参照共有 | 元の対象を保持し、PLANデータから独立して複製 |
| 分割済み項目のマイルストーンが到達しなかった | 置換先すべての状態と証拠で判定。未完了時の監査と重複抑止を確認 |
| Workなしの部分的な回復でも計画停滞を解除できた | 新たな有効完了・必須指摘解消に限定 |
| ランチャーが非0終了を1に置き換えていた | native・ps1・cmdの元の終了コードを保持 |
| 完成証拠の欠落でErrorとなり、破損時も重複抑止が再監査を妨げた | 受理済みCompletionAuditと証拠を再照合し、必要なら新しい監査へ。上限中はPaused |
| NewRunが受理済み・未検証Workを再実行できた | 回復対象を引き継ぎ、before.jsonに実行前マニフェスト参照を保持 |

## マイルストーン自動作成・進捗表示の追加検証

新規3件と既存5件の計8件が成功した。今回の実行コマンド:

```powershell
pwsh -NoProfile -File ./autoframe/tests/test.ps1 -Filter '*milestone*'
pwsh -NoProfile -File ./autoframe/tests/test.ps1 -Filter '*timeout stops process tree*'
pwsh -NoProfile -File ./autoframe/tests/test.ps1 -Filter 'successful six phases*'
```

- テンプレートのSchema適合、初期項目・マイルストーン、全必須項目の対応を確認。
- 部分進捗、置換先すべての集計、pendingへの差戻しによる監査候補の取消し、空の既存マイルストーンの扱いを確認。
- 疑似Workerが内部マイルストーンを生成し、未完了の節目を残して監査へ遷移すること、Worker入力と実行ログの件数、PLAN.mdの不変を確認。
- 既存の依存・置換・監査重複抑止、6段階の完了、時間切れでの子プロセス停止を再確認。

PowerShell全10ファイルの構文、日英SPEC内のPLAN例のSchema・参照、変更文書のローカルリンク31件も確認した。実モデルによるPLAN.mdの自動作成品質は今回の試験対象外。作成手順はプロンプトとテンプレートで定義しており、RunnerがPLAN.mdを書き換えるものではない。

## 実CLI

```powershell
pwsh -NoProfile -File ./autoframe/tests/smoke-real-cli.ps1 -RunRealCli
```

通常ユーザーの実行環境で疎通試験が成功した。CLI 0.154.0、既存認証によるモデル応答、stdin入力、構造化JSONの完全一致、子プロセス停止を確認。制限された実行環境ではホームディレクトリ取得に失敗したため、両環境の結果を区別した。

Runnerでも一時プロジェクトの1ファイルを対象に、実CLIで読取専用レビューを開始した。PlanのJSONを1件受理し、約68秒でNeedsInputとして保存・停止した。Workerは読取専用で動作し、試行用の証拠ファイルを保存できなかった。対象ファイルの不変、未完了扱い、running=false、全子プロセスの停止を確認。権限・認証・モデル設定は変更していない。

保存先（この環境の一時ディレクトリ）:

- 疎通成功: `%TEMP%/autoframe-cli-smoke-5ec7e103506d47e3a13ae85cb9e0f86b/verification.json`
- Runnerの停止記録: `%TEMP%/autoframe-real-run-ee85f503b456450a9f89c7630492a0e9/.autoframe/state.json`

疎通成功は、Workerの証拠書込権限や実CLIの6段階完了を意味しない。利用環境で必要な権限を確認したうえで実行する。

## 未完了・制限

- 実CLIによる6段階の完了。今回の通し試験はPlanでNeedsInputとなった。実モデルの監査品質・使用量・長時間運転も未検証。
- OSの強制電源断、実キーボードのCtrl+C。プロセス強制終了、キャンセル要求、時間切れ、子残留と停止は疑似試験で確認。
- Windows以外は非対応。リンクは一律拒否し、globの包含・競合判定は保守的に行う。差分検査はOSの権限制御を代替しない。
- NativeAOT、製品作業用PLAN.md、本番の自動実行は対象外。試験用PLAN.mdは一時プロジェクト内に限定。
