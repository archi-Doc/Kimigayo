# autoframe 実行基盤

[SPEC.md](../SPEC.md) 0.1に基づく実装。Windows、PowerShell 7.4以降、対応するCodex CLIと既存の認証・権限設定を使用する。`autoframe/`一式を共通配置でき、プロジェクト固有の制御入力はプロジェクト直下の`PLAN.md`だけ。

## 1. PLAN.mdを準備する

ユーザーが定義する内容は次のとおり。進捗・証拠・指摘は含めない。

| 必須 | 任意 |
| --- | --- |
| 固定project_id、目的、有限の対象、対象外・制約、完成条件と検証方法、変更可能範囲 | 入力範囲、検証生成物の書込範囲、環境識別コマンド、参照資料、初期項目・依存、マイルストーン |

[テンプレート](templates/PLAN.template.md)と[PLAN Schema](schemas/plan.schema.json)を使用する。空のwork_scopeは読取専用作業。environment_checksは確認済みのPowerShell読取コマンドだけを書く。input_scope省略時はプロジェクト全体を基準にする。

PLAN.mdのJSONブロックの前に「ユーザープロンプト（原文）」を設け、入力された目的・対象・完成条件・対象外／制約を要約・補完せず保存する。具体化した内容は実行用JSONへ記載し、保存前に原文との整合を確認する。更新時も保存済みの原文を保持する。

[作成用プロンプト](prompts/create-plan.md)は、目的と完成条件から初期項目と1件以上のマイルストーンを自動生成する。各マイルストーンに対象項目と確認可能な到達条件を設定し、全必須項目を対応付ける。形式上は省略可能だが、既存PLANで未設定の場合もPlanが内部計画に補う。

作成を依頼するプロンプト例:

```text
autoframe/templates/PLAN.template.mdとautoframe/schemas/plan.schema.jsonに従い、
<対象プロジェクト>/PLAN.mdだけを作成してください。
目的: docs配下の利用手順を現行実装と照合し、誤記を修正する。
対象: docs配下の利用手順と対応する実装。
完成条件: 対象文書を全件確認し、コマンド・リンク・期待結果の整合を確認できる。
対象外・制約: 変更範囲はdocs/**。製品コード・公開APIの変更と外部公開は対象外。
上記4項目をPLAN.mdの「ユーザープロンプト（原文）」欄に原文のまま保存してください。
既存構成を調べ、確認済みの参照資料・検証方法を具体化してください。
tasksとmilestonesを自動生成し、全必須項目を到達条件付きのmilestoneへ対応付けてください。
目的・必須条件・変更範囲に関わる不足だけ質問してください。
本文は簡潔にし、進捗や証拠を含めないでください。
製品修正・テスト・自動実行は開始しないでください。
```

## 2. 実行・再開

```powershell
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot 'C:/Projects/Sample App'
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot 'C:/Projects/Sample App' -Resume
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot 'C:/Projects/Sample App' -Resume -MaxRunMinutes 720
```

MaxRunMinutesは再開前を含む累計上限。Resumeで省略した実行設定は保存値を継承する。未完了状態がある場合はResumeまたはNewRunを明示する。NewRunは履歴と成果物を保持して別のrun_idを開始する。

**MaxRunMinutesに達すると、新規起動を止め、実行中のWorkerと子プロセスを強制停止する。** 既定では締切5分前から保存・引継ぎに入るよう指示する。停止・保存の猶予は既定60秒で、正常に停止すればPaused、停止を確認できなければErrorとなる。

NewRunでも受理済み・未検証のWorkは回復対象にする。CompleteのResumeで完成証拠が欠落・破損していれば再監査へ戻り、上限到達中ならPausedとして保存する。

```powershell
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -Resume -ResetStallCounters -ResetReason '曖昧な完成条件を修正'

# PhaseModelsを指定する場合は、PowerShell内からhashtableを直接渡す。
$models = @{ Audit = '<利用可能なモデルID>'; Verify = '<利用可能なモデルID>' }
& ./autoframe/run.ps1 -ProjectRoot . -Resume -PhaseModels $models
```

| 引数 | 新規実行の既定値 |
| --- | --- |
| ProjectRoot / CodexCommand | 呼出元の現在地 / codex |
| MaxRunMinutes / MaxPhaseAttempts | 480分 / 100回 |
| PhaseTimeoutMinutes / SaveReserveMinutes | 60分 / 5分 |
| StopTimeoutSeconds | 60秒 |
| MaxTasksPerWork / CompletionAuditInterval | 最大3項目 / Verifyまで受理したWork 3回 |
| Resume / NewRun / ResetStallCounters | false |
| PhaseModels / ResetReason | 空の表 / 未指定 |

数値は正の整数。MaxTasksPerWorkは1〜3、保存余裕は段階上限未満。ResumeとNewRunは併用不可。停滞解除にはResumeと空でないResetReasonが必要で、累計時間・試行数は解除しない。明示したPhaseModelsは保存表全体を置き換える。

終了コード: Complete=0、Paused=2、Blocked=3、NeedsInput=4、Stalled=5、Error=6。通常ResumeではStalledを解除しない。Pausedでも再開すれば進むとは限らない。

## 3. 記録と実装上の選択

`.autoframe/state.json`と参照先が現行状態。`records/`は受理結果・計画・証拠、`manifests/`は内容ハッシュで共有する記録、`runs/`は各試行の入力・前後記録・ログ・時間測定を保存する。内部状態は手で編集しない。

各試行の`before.json`は実行前マニフェストを参照する。受理後も参照を残し、NewRunの回復で利用する。

Worker入力の`milestone_progress`に各マイルストーンの対象ID・検証済み件数・全件数・監査候補を渡し、Verify受理後は件数をログに表示する。全対象の有効な検証後、Completion Auditが到達条件を現物・証拠と照合する。証拠失効は進捗にも反映し、PLAN.mdは書き換えない。

- 証拠と記録を先に確定保存し、結果・計画版・遷移・カウンタをstate.jsonの1回の置換で受理する。保存失敗時の部分受理を防ぎ、未参照記録は未受理とする。ハートビートは計画版を変更しない。
- PLAN変更や証拠失効は再評価へ戻し、失効した指摘の解消判定も取り消す。未検証Workと未確定対象をVerifyへ引き継ぎ、一部の検証で残りを省略しない。
- Auditは毎回実行し、全対象の証拠をWork直前にも照合する。独立したWorkを最大3件にまとめる。完成監査の重複と停滞は証拠内容で判定し、IDの付け替えでは解除しない。
- ユーザー指定の依存関係・criterion_ids・マイルストーン対象を保持する。分割後のマイルストーンは置換先すべての状態・証拠で判定する。Workなしの回復では、新たな有効完了または必須指摘の解消だけが計画停滞を解除する。
- globは`*`、`?`、独立した`**`。リンク、Windowsの予約名・末尾の空白や点は拒否する。edit_scopeは宣言済みwork_scopeのglobまたは範囲内の具体的ファイルに限定し、競合の可能性があれば一括実行を拒否する。
- 子プロセスは起動前のゲートとWindows Job Objectで管理する。他OSは起動前にErrorとなる。ファイル差分による事後検査はOSの書込制限や全操作の記録を代替しない。
- CLIはstdin入力、JSONLイベント、stderr、最終JSONを分離する。既存のモデル・認証・権限を継承し、権限拡張フラグを付けない。必要な権限・CLI機能がない場合は停止する。

`.exe`・`.ps1`・`.cmd`の終了コードは元の値を保存する。Workerには少なくとも試行の`output_directory`へ証拠を書ける既存の権限が必要。CLI疎通試験はツール不使用なので、疎通成功だけではこの書込権限や6段階の完了を保証しない。

CLIの事前確認も累計予算に含める。入力範囲を絞ってもプロジェクト内の指示・設定ファイルを署名対象に含め、祖先ディレクトリの指示・設定も照合する。モデルの自動変更は行わない。

`prompts/common.md`は毎回の入力に展開し、必要な現行記録への参照を渡す。要約は原則5箇条以内。履歴・ログ・コードを文書へ重複転載せず、失敗・未確認事項は省略しない。

## 4. 出力契約と検証

[result Schema](schemas/result.schema.json)がWorkerの出力契約。changesは種類・ID・setによる差分で、setの変更しないフィールドはnull。blockerが不要ならnull。証拠パスは試行のoutput_directory基準で、SHA-256と入力signatureを指定する。必要成果物は内部taskのdeliverablesにファイルまたはglobで列挙する。

```powershell
# 既定: 一時プロジェクトと疑似Workerだけを使う。
pwsh -NoProfile -File ./autoframe/tests/test.ps1

# 実CLIの疎通だけを明示的に行う。製品用PLANや自動実行は不要。
pwsh -NoProfile -File ./autoframe/tests/smoke-real-cli.ps1 -RunRealCli
```

疑似試験の失敗時は一時ディレクトリを保持する。`-KeepFixtures`で成功時も保持でき、`-Filter '*対象名*'`で個別検証できる。実CLIの疎通は一時ディレクトリ内でツール不使用・読取専用の短い要求を送り、構造化出力と子停止を確認する。

実行結果と未実施事項は[VERIFICATION.md](VERIFICATION.md)を参照。実CLI接続の根拠は[OpenAI公式の非対話実行](https://learn.chatgpt.com/docs/non-interactive-mode)と利用版の`codex exec --help`。
