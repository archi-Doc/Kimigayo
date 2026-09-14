# autoframe 仕様案

状態: 設計案。スクリプト・プロンプトは未実装。

## 1. 基本方針

autoframeは、大きな処理を分割し、計画・監査・実行・検証を繰り返す自動実行フレームワークである。

**プロジェクト固有の制御入力は、プロジェクトルートのPLAN.mdだけとする。** PLAN.mdを変更すれば、実装、コード検証、改善、文書整備などに用途を変えられる。別プロジェクトでも、スクリプトや共通プロンプトの変更は不要とする。

### 1.1. 責務の分担

| 要素 | 責務 |
| --- | --- |
| PLAN.md | 目的、完成条件、作業範囲、項目、依存関係、完了状態を定義する |
| runner | PowerShellでWorkerの起動、結果の受理、状態更新、遷移、停止を管理する |
| Worker | 各段階で起動するCodex。計画、作業、監査、検証を担当する |
| 作業対象 | ソース、仕様書、成果物など。WorkerがPLAN.mdに従って参照・変更する |
| 内部記録 | runnerが生成する実行位置、入力の写し、結果、ログ、証拠 |

runnerはSPEC.mdやSTATUS.mdなど、特定のプロジェクト文書を要求しない。必要な参照先や更新対象はPLAN.mdに記載する。内部記録は自動生成し、別プロジェクトへの適用時に手動編集を必要としない。

PowerShell、Codex CLI、認証、開発ツールは実行環境の前提である。ユーザー指示、適用されるAGENTS.md、環境の権限制約も有効であり、PLAN.mdでこれらを緩和しない。

### 1.2. 初版の範囲

単一プロジェクトを逐次処理する。各段階は新しいWorkerセッションで実行し、ファイルで引き継ぐ。並列編集、定期起動、プロジェクト外への書き込み、外部サービスへの更新・公開は初版の対象外とする。

本書は既存のautomation/やautoimpl2/を置換・起動するものではない。

## 2. ファイル構成

```text
project/
  PLAN.md                       # プロジェクト固有の定義
  autoframe/                    # 共通配布物
    run.ps1
    invoke-worker.ps1
    lib/                        # 計画・状態・遷移の処理
    prompts/
      common.md
      plan.md
      prepare.md
      audit.md
      work.md
      verify.md
      completion-audit.md
    schemas/
      plan.schema.json
      phase-result.schema.json
    templates/PLAN.template.md
    README.md
  .autoframe/                    # 自動生成
    lock
    state.json
    runs/<run-id>/<attempt-id>/
      input-plan.md
      input.json
      execution-plan.json
      result.json
      events.jsonl
      stderr.log
      evidence/
      receipt.json
```

PLAN.mdと内部記録はProjectRoot基準、共通配布物はスクリプト自身の場所を基準に解決する。autoframe/をプロジェクト外に置くこともできる。プロジェクト専用のconfig.jsonは不要とする。

state.jsonは実行位置・回数・受理記録だけを管理し、項目の状態を独立した台帳として持たない。履歴に保存したPLAN.mdは写しであり、現行計画の正本ではない。

内部記録は通常Git追跡対象外とする。証拠として参照されている記録は自動削除しない。

## 3. PLAN.mdの構造

### 3.1. 記述形式

PLAN.mdは、説明文と機械処理用のJSONを含むMarkdownとする。JSONは次のマーカーに囲まれたコードブロックを1つだけ置く。具体例は§7.1に示す。

```text
<!-- autoframe:begin -->
JSONコードブロック
<!-- autoframe:end -->
```

制御上の正本はJSONとする。通常の表やチェックボックスから状態を推測しない。説明文とJSONの意味が矛盾していれば、Planで確認事項として報告する。

### 3.2. 定義と更新者

| 区分 | 主なフィールド | 更新方法 |
| --- | --- | --- |
| 識別情報 | schema_version、project_id、revision | project_idの変更は新規実行。revisionはrunnerが更新 |
| ユーザーの要件 | objective、scope、non_goals、constraints、references、work_scope | ユーザーが定義・変更 |
| 全体の完成条件 | completion_criteriaのid、condition、verification | ユーザーが定義・変更 |
| 作業計画 | milestones、tasksの内容・依存・条件 | Planが変更案を提出 |
| 検証記録 | tasksのstatus・evidence、完成条件のevidence、findings | 段階結果に基づいてrunnerが更新 |

WorkerはPLAN.mdを直接編集せず、結果JSONで変更案を返す。runnerが更新権限・形式・入力との整合を検査して反映する。

目的や完成条件の本文はWorkerが変更できないが、完成条件のevidenceはCompletion Auditの結果から更新できる。これにより、要件の保護と検証記録の更新を両立する。

### 3.3. 項目・マイルストーン・指摘

**項目（tasks）**には、次を記載する。

| フィールド | 内容 |
| --- | --- |
| id / description / required | 安定したID、処理内容、必須かどうか |
| status / depends_on | 状態、依存する項目ID |
| completion_criteria_ids | 関連する全体完成条件のID |
| deliverables / acceptance / verification | 成果物、項目の完了条件、検証方法 |
| evidence | 証拠記録への参照 |
| blocker | 阻害理由と解除条件。通常はnull |
| replaced_by | 置換先ID。通常は空配列 |

**マイルストーン（milestones）**には、id、description、task_ids、acceptanceを記載する。所属項目がverifiedになり、固有の到達条件をCompletion Auditが確認した時点で到達とする。

マイルストーンは進捗の節目を表す。プロジェクト完成に必須の条件はcompletion_criteriaにも記載し、マイルストーンだけに隠れた完成条件を持たせない。

**指摘（findings）**には、id、required、description、resolution、task_ids、status、evidenceを記載する。statusはopenまたはresolvedとする。各段階の指摘をrunnerがPLAN.mdに追加し、解消はVerifyまたはCompletion Auditで確認する。Planは対応項目を割り当てるが、指摘を解消扱いにはしない。

別の指摘台帳は必須にしない。必須指摘の省略・削除・任意化は受理しない。監査で誤指摘と判明した場合も、理由を記録してresolvedにする。

### 3.4. 計画の整合性

起動時と更新時に、Schema、ID重複、未定義参照、依存の循環を検査する。依存は「着手前に満たす条件」とし、1回のWorkに選ぶ項目は、開始時点ですべての依存先がverifiedでなければならない。

初期のtasksとmilestonesは空でもよい。目的・範囲・全体の完成条件は開始前に必要とする。Plan後には全体の完成条件ごとに必須項目が対応し、Auditが作業の網羅性を確認する。

既存項目は削除せず、分割・統合時はsupersededとreplaced_byを使う。置換先へ必須性・範囲・条件を引き継ぎ、依存元の参照も更新する。任意項目でも必須項目の依存先なら完了が必要となる。

### 3.5. 項目の状態

```text
pending → in_progress → implemented → verified
              └────────────┴──────────→ pending（修正・再検証）
```

| 状態 | 意味 |
| --- | --- |
| pending | 未着手、修正待ち、再検証待ち |
| in_progress | Workに着手中。中断時は未確定のまま保持 |
| implemented | 作業実施済み。Verifyによる受理待ち |
| verified | 完了条件と証拠をVerifyで受理済み |
| blocked | 外部条件待ち。blockerに理由と解除条件を記録 |
| superseded | 他の項目へ置換済み。完了としては数えない |

in_progressへの変更はWork開始時にrunnerが行う。Workはimplementedまで提案でき、verifiedへの変更はVerifyだけが提案できる。調査・文書作業でも同じ状態を使う。

部分作業は実施範囲を記録し、Verifyが未完了と判定すればpendingへ戻す。検証自体が外部条件待ちならblockedとする。解除後はPlanが状態をpendingに戻し、修正が必要か、Verifyへ直接進めるかを判断する。

Plan、Verify、Completion Auditは証拠の失効を指摘できる。runnerは影響項目をpendingへ戻す。ユーザーがverifiedと記入しても、有効な検証記録がなければ再検証する。

## 4. 実行手順

### 4.1. 基本フロー

```text
Plan → Prepare → Audit → Work → Verify
          ↑        │                │
          └─ 修正 ─┘                │
          └──── 次の作業・修正 ──────┘

Verify後、節目または一定回数ごとにCompletion Audit
  ├─ 全体の完成条件を満たす → Complete
  └─ 未完了 → Plan
```

Prepareは前回の検証結果を使って次の作業を計画する。修正はWorkで行い、検証はVerifyで行う。最後のWorkも検証を省略しない。

### 4.2. 段階ごとの役割

| 段階 | 処理 | 主な結果 |
| --- | --- | --- |
| Plan | 全体計画を差分更新し、必須指摘の対応項目を決める | 更新案、次の処理 |
| Prepare | 実行可能項目を選び、今回の範囲・手順・検証を具体化する | 実行計画 |
| Audit | 計画の抜け、依存、作業量、検証方法を確認・修正する | 精査済み実行計画 |
| Work | 実行計画に従って作業し、自己検証と引継ぎを行う | 成果物、実行報告 |
| Verify | 現物と証拠を確認し、必要な追加検証を行う | 項目の受理・差し戻し |
| Completion Audit | 全体の完成条件、統合・回帰、必須指摘を確認する | 全体判定、不足事項 |

実行計画には、対象ID、関連資料、変更範囲、手順、完了条件、検証方法、時間配分、中断時の引継ぎを含める。Workに渡すのはAuditで修正・受理した計画そのものとする。

Plan・Prepare・Auditは製品の修正をしない。Verify・Completion Auditは検証を行うが、不具合修正は次のWorkへ回す。各段階は自分の証拠保存先へ記録できる。検証によるビルド出力なども、PLAN.mdのwork_scopeに含める。

### 4.3. 結果と遷移

| 段階 | decision | 次の処理 |
| --- | --- | --- |
| Plan | ready | Prepare |
| Plan | recover | 保存済みの部分成果をVerifyで確認 |
| Prepare | ready | Audit |
| Prepare | completion_candidate | Completion Audit |
| Audit | approved | Work |
| Audit | revise | Prepare。大幅な全体変更が必要ならreplan |
| Work | reported | 必ずVerify。その後の希望経路はroute_hintで報告 |
| Verify | accepted / revise | 続行、修正、全体監査の条件をrunnerが判定 |
| Completion Audit | complete / incomplete | Completeの受理検査、またはPlan |

Plan以外はreplanでPlanへ戻せる。ただし、Workはreplanをdecisionとして返さず、reportedとroute_hintで報告してVerifyを通す。

各段階はblockersで項目単位の外部待ちを報告できる。独立した実行可能項目があればPlanまたはPrepareへ進み、なければBlockedで停止する。要件変更などユーザー判断が必要ならneeds_inputを返し、NeedsInputで停止する。Workではneeds_inputの場合も部分成果と未検証状態を保存し、再開時にVerifyを必要とする。

Verify後は、停滞停止、再計画の必要性、全体監査の実行条件、次の作業の順に判定する。同時に成立した全体監査の実行条件は1回にまとめる。

### 4.4. 検証と完成判定

証拠には、対象、検証方法、結果、日時、ログ参照、対象ファイルのハッシュ、必要なツール・環境情報を記録する。PLAN.mdのevidenceは、その記録への参照を保持する。

有効な証拠は再利用できる。入力や環境が変わった場合、または有効性を判断できない場合は再検証する。未実行・skip・ログ欠落を成功として扱わない。レビューは対象・観点・結果の記録で検証でき、コード変更やテスト実行を一律には要求しない。

Completion Auditは、次のいずれかで実行する。

- 必須項目と必要な依存先がすべてverifiedになった。
- 新しいマイルストーン到達候補がある。
- 前回の全体監査から所定回数のWorkが終わり、Verifyまで受理された。

マイルストーンの監査済み記録は内部状態に保存する。比較にはマイルストーンの定義と関連証拠のハッシュを使い、無関係なPLAN.md更新で再監査しない。

Completeの受理には、現行入力に対する全体監査の合格、必須項目・必要依存のverified、全体完成条件ごとの証拠、必須指摘の解消をすべて必要とする。未確定Workが1つでもあれば完成にしない。未着手の任意項目は残せる。

監査から受理までに、証拠が対象とするソースや参照資料が変わっていないことも確認する。検証中に生成したログ・ビルド出力は、検証入力とは分けて扱う。

runnerは構造と整合性を検査し、証拠の意味的な妥当性は監査Workerが判断する。正常終了や正しいJSONだけでは完成を認めない。

## 5. 結果の受理と再開

### 5.1. Workerとの入出力

入力は、共通・段階別プロンプト、PLAN.mdの写し、実行ID・試行ID、必要な受理済み結果、締切とする。Workには精査済み実行計画も渡す。履歴全体を毎回展開せず、必要な記録を参照する。

結果JSONは以下を共通フィールドとし、段階別Schemaでdecisionと変更内容を制限する。

```text
schema_version, run_id, attempt_id, phase
input_plan_revision, input_plan_hash, execution_plan_hash
decision, route_hint, summary
task_results, plan_proposal, findings, evidence, blockers, progress
```

該当しない値はnullまたは空配列とする。runnerは終了状態、Schema、ID、入力ハッシュ、変更権限、遷移を検査して受理する。自然文から次の段階を推測しない。Worker自身は次のWorkerを起動しない。

Audit後のin_progress化は、runnerによる許可済みの状態更新として記録し、新しい入力ハッシュをWorkへ渡す。それ以外に計画や関連入力が変われば、監査の有効性を確認し直す。

### 5.2. 保存と競合

PLAN.mdとstate.jsonを同時に更新できるとは仮定しない。runnerは旧ハッシュ、新しい計画、次の状態を更新記録へ保存した後、各ファイルを一時ファイルから置換し、最後に受理記録を確定する。

更新途中で止まった場合は、旧版・新版のハッシュと更新記録を照合して復旧する。どちらにも一致しない場合は競合として停止し、上書きしない。更新時は排他アクセス中に再照合し、外部編集との競合を防ぐ。排他アクセスを確保できなければ更新しない。

この保存手順は、Workが行った成果物の変更全体を巻き戻す仕組みではない。

### 5.3. 中断・再開

同じProjectRootの二重起動を排他ロックで防ぎ、Work開始前と各段階の受理後に状態を保存する。

結果欠落、不正JSON、CLI失敗、タイムアウトでは状態を先に進めず、部分成果を保持する。対象のWorkerと子プロセスが停止したことを確認してから再開する。初版では実行エラーを自動再試行しない。

Resumeは同じ実行IDを使い、更新記録の復旧後にPlanへ進む。Planは未確定の成果を確認し、必要ならrecoverで直接Verifyへ渡す。この場合に限り、Verifyはin_progressやpendingの成果も受理対象にできる。未確定Workを確認する前に、新しいWorkを始めない。

内部記録がなければ新規実行としてPlanから開始できるが、過去のverifiedを無条件には引き継がない。完成済みのResumeもPlanで証拠を再評価し、Completion Auditを経て完成を確認する。

### 5.4. PLAN.mdの変更

ユーザーはPLAN.mdを編集して処理内容を変更できる。停止中の編集を基本とし、実行中の編集は段階の開始・受理時にファイル全体のハッシュで検出する。

変更を検出した場合は古い結果を適用せず、Planへ戻す。作業中に生じた成果物は保持し、未確定として再評価する。外部編集によってproject_idが変わった場合は現在の実行を停止し、新規実行を必要とする。

要件や参照資料の変更で証拠が失効した場合は、影響項目とその利用側を再評価する。影響範囲が不明なら再検証する。自動処理は目的・完成条件を緩めず、変更が必要なら具体案を保存してNeedsInputで停止する。

## 6. 運用上の制御

### 6.1. 上限と停滞

| 起動引数 | 既定値 | 意味 |
| --- | --- | --- |
| ProjectRoot / Resume | 現在のフォルダー / false | 対象と再開指定 |
| CompletionAuditInterval | 3 | 全体監査の間隔。Verifyまで受理されたWork回数 |
| MaxPhaseAttempts | 100 | 全段階を含む起動回数の上限 |
| MaxRunMinutes | 480 | 再開前も含む累計稼働時間の上限 |
| PhaseTimeoutMinutes | 60 | 1段階の時間上限 |
| SaveReserveMinutes | 5 | 時間上限の内側に確保する保存余裕 |
| MaxAuditRevisions | 2 | 同じ実行対象への連続差し戻し上限。到達時はPlanへ |
| NoProgressReplanThreshold | 2 | 進捗なしの連続回数。到達時はPlanへ |
| NoProgressStopThreshold | 4 | 再計画後も進捗がない場合の停止回数 |

保存余裕は段階上限より短くし、停滞停止回数は再計画回数より大きくする。時間・回数は正の値とし、不正な組合せは起動時に拒否する。

進捗は、実装・検証・原因特定などの具体的な前進をVerifyが受理した場合に認める。Verifyまで受理されたWorkごとに停滞を数え、PlanやAuditの通過、Resumeだけではリセットしない。計画だけの反復もMaxPhaseAttemptsで止める。

全体残時間と段階上限の短い方を締切にする。保存余裕が取れなければ次の段階を起動しない。上限のためVerifyを起動できなければ未検証として停止し、再開後に検証する。

### 6.2. 実行状態と終了コード

| 状態 | コード | 意味 |
| --- | --- | --- |
| Running | — | 実行中 |
| Complete | 0 | 全体の完成を受理 |
| Paused | 2 | 回数・時間上限、ユーザー中断 |
| Blocked | 3 | 独立した実行可能項目がなく外部待ち |
| NeedsInput | 4 | 要件変更などの判断待ち |
| Stalled | 5 | 再計画しても進捗なし |
| Error | 6 | 起動、CLI、結果形式、保存、整合性などのエラー |

段階のタイムアウトはPausedとし、理由を別途記録する。プロセス終了を確認できない場合や強制終了で終了コードを返せない場合も、完成扱いにはしない。

### 6.3. 編集範囲

作業対象の書き込みはwork_scope内に限定する。PLAN.md、共通配布物、内部状態、他試行の記録は保護対象とし、自分の試行の証拠保存先だけを別途許可する。

パスはProjectRoot基準で正規化し、親フォルダーやリンクを経由した範囲外への書き込みも拒否する。開始時の未コミット変更を保持し、自動reset・clean・stash・commit・pushは行わない。

差分・ハッシュによる事後確認は、OSによる書き込み制限とは異なる。実行権限は環境に従い、runnerが認証・権限設定を自動変更しない。

## 7. 例

### 7.1. PLAN.md

Pythonの小規模なプロジェクトで、既存テストを確認して不具合を修正する例。コマンドや範囲は対象に合わせて変更する。

~~~~markdown
# 既存テストの確認と不具合修正

<!-- autoframe:begin -->
```json
{
  "schema_version": 1,
  "revision": 1,
  "project_id": "sample-test-repair",
  "objective": "既存テストの失敗原因を調べ、製品コードの不具合を修正する",
  "scope": ["srcとtests"],
  "non_goals": ["公開APIの変更", "テストの削除・無効化"],
  "constraints": ["既存の未コミット変更を保持する"],
  "references": [{"path": "README.md", "purpose": "実行環境と利用方法"}],
  "work_scope": ["src/**", "tests/**"],
  "completion_criteria": [
    {
      "id": "C1",
      "condition": "既存テストがすべて成功し、再現した不具合の修正を確認できる",
      "verification": "python -B -m unittest discover -s tests -v を実行し、失敗・エラー・skipが0件で、1件以上のテストが実行されることを確認する。修正があれば差分と再現結果も確認する",
      "evidence": []
    }
  ],
  "milestones": [
    {
      "id": "M1",
      "description": "テストの確認と修正を完了",
      "task_ids": ["T001", "T002"],
      "acceptance": "調査結果と修正結果の対応が確認できる"
    }
  ],
  "tasks": [
    {
      "id": "T001",
      "description": "既存テストを実行し、失敗原因と修正対象を記録する",
      "required": true,
      "status": "pending",
      "depends_on": [],
      "completion_criteria_ids": ["C1"],
      "deliverables": ["実行記録内のテストログと原因調査"],
      "acceptance": ["全テストの結果と、失敗原因または失敗なしの根拠がある"],
      "verification": ["実行ログと原因調査を照合する。ここではテスト失敗を許容する"],
      "evidence": [],
      "blocker": null,
      "replaced_by": []
    },
    {
      "id": "T002",
      "description": "不具合を修正し、既存テストと必要な再現テストを確認する",
      "required": true,
      "status": "pending",
      "depends_on": ["T001"],
      "completion_criteria_ids": ["C1"],
      "deliverables": ["必要なコード修正", "実行記録内の最終検証ログ"],
      "acceptance": ["C1を満たす。修正不要なら理由と検証結果を残す"],
      "verification": ["C1の検証手順を実行し、修正差分と結果を確認する"],
      "evidence": [],
      "blocker": null,
      "replaced_by": []
    }
  ],
  "findings": []
}
```
<!-- autoframe:end -->
~~~~

T001の完了条件は「調査の完了」であり、テスト成功ではない。T001をVerifyで受理してから、T002に着手する。環境不備や未決定の仕様が原因なら、無理に修正せず阻害要因として扱う。

### 7.2. 起動方法

以下は実装後に提供する予定のインターフェースであり、現時点では実行できない。

```powershell
# 通常実行
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot .

# 上限を引き上げて同じ実行を再開
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -Resume -MaxPhaseAttempts 150 -MaxRunMinutes 720
```

### 7.3. 遷移の擬似コード

次はWork後の分岐だけを示すPowerShellの擬似コードである。保存、結果検査、停止処理は§5・§6に従って別途実装する。

```powershell
# Workの有効な実行報告を受理した後
$nextPhase = 'Verify'

# Verifyで状態と進捗を受理した後
if ($stopForStagnation) {
    $runStatus = 'Stalled'
}
elseif ($requiresReplan) {
    $nextPhase = 'Plan'
}
elseif ($completionAuditDue) {
    $nextPhase = 'CompletionAudit'
}
else {
    $nextPhase = 'Prepare'
}
```

## 8. 初版の完成条件

| 観点 | 必要な確認 |
| --- | --- |
| 可搬性 | PLAN.mdだけを変え、実装・レビューなど異なる用途を処理できる |
| 遷移 | 最終Work、差し戻し、全体監査、部分成果の回復経路が正しく動く |
| 整合性 | 不正な依存、古い結果、権限外更新、必須指摘の脱落を拒否する |
| 再開 | 更新途中の中断、結果欠落、PLAN.md競合でも部分成果を保持する |
| 完成判定 | 証拠不足、未確定Work、要件の緩和で完成にならない |
| 停止 | 上限、外部待ち、判断待ち、停滞、実行エラーを区別する |

疑似Workerを使う一時プロジェクトで上記を検証する。二重起動、証拠失効、独立項目への切替、監査の重複防止、計画だけの反復上限も含める。

実Codex CLIとの短い疎通試験は別途行い、引数・入出力・作業ディレクトリ・停止処理を確認する。既定の試験でNativeAOTは実行しない。

CLI接続実装時の参照: [公式の非対話実行ドキュメント](https://learn.chatgpt.com/docs/non-interactive-mode)。具体的なCLIフラグは実装時に利用版で確認する。
