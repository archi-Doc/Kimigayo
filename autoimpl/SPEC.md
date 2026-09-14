# autoframe 仕様 0.1

状態: 確定。ps1・プロンプトの実装基準は本書とする。実装は[autoframe/](autoframe/README.md)。疑似Workerによる基盤検証と実CLI疎通は成功。実CLIの通し試験は読取専用制約によりNeedsInputで停止（[検証記録](autoframe/VERIFICATION.md)）。

## QuickStart

配置済みのautoframe/を利用し、「PLAN.mdを作成 → 実行」の順に進める。山括弧を対象に合わせて置き換える。

### 手順1: PLAN.mdをプロンプトで作成

これはユーザーからの作成・更新依頼であり、自動実行中のWorkerとは区別する。本書だけで作成できる。

[作成用プロンプト](autoframe/prompts/create-plan.md)も利用できる。

```text
SPEC.mdの§2に従い、<プロジェクトのパス>/PLAN.mdを作成してください。

目的: <達成したいこと>
対象: <機能、フォルダー、参照資料>
完成条件: <どの状態になれば終了できるか>
対象外・制約: <変更しないもの、互換性、禁止する操作>

構成と既存の検証手順を確認し、判断できる詳細は具体化してください。
上記4項目を、PLAN.mdの「ユーザープロンプト（原文）」欄に原文のまま保存してください。
目的と完成条件からtasksとmilestonesを自動生成してください。
各milestoneに対象項目と確認可能な到達条件を設定し、全必須項目を対応付けてください。
目的・必須条件・変更範囲を左右する不足だけ質問してください。
PLAN.mdだけを作成し、製品修正・テスト・自動実行は始めないでください。
```

記入例: 「srcの入力検証と境界条件をレビューし、再現した不具合を修正する。対象全件の確認記録、不具合解消、既存・再現テストの成功を完成条件とする。公開API変更、全面改修、テスト無効化、NativeAOTは対象外とし、未コミット変更を保持する。」

作成時は必須フィールド・JSON構文・一意ID・参照・依存・完成条件との対応を確認する。未確認の資料やコマンドを確定事項として書かず、必須の判断が残れば質問する。更新では依頼外の要件と安定したIDを保持する。保存先と形式検査結果を短く報告し、生成過程や進捗はPLAN.mdに含めない。

### 手順2: 実行

利用環境でのCLI疎通とPLAN.mdの確認後、プロジェクトルートで起動する。Windows版の使用例を示す。

```powershell
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot .
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -Resume
```

**MaxRunMinutes（既定480分）に達すると、新規起動を止め、実行中のWorkerと子プロセスも強制停止する。** 再開前を含む累計上限であり、使い切った場合は引き上げてResumeする。停止・保存の猶予と詳細は§7.4を参照。

## 1. 基本方針

autoframeは、大きな処理を計画・監査・実行・検証に分割する自動実行フレームワークである。

**PLAN.mdを、ユーザーが定義するプロジェクト固有の唯一の制御入力とする。** runnerもWorkerもPLAN.mdを書き換えない。実装・レビュー・文書整備への用途変更や別プロジェクトへの適用は、PLAN.mdで指定する。

| 要素 | 責務 |
| --- | --- |
| PLAN.md | ユーザーの目的、完成条件、作業範囲、制約、任意の初期計画 |
| runner | Workerの逐次起動、機械検査、状態更新、遷移、停止 |
| Worker | 各段階で起動するCodex。計画、作業、監査、検証 |
| 内部状態 | 自動生成した計画、進捗、指摘、証拠、実行位置 |
| 成果物 | WorkerがPLAN.mdに従って参照・変更するファイル |

SPEC.mdやSTATUS.mdなど、特定の文書を必須にしない。内部状態を人が編集しないと運用できない設計にもならないようにする。

初版はローカルファイルシステム上の単一プロジェクト・逐次実行とする。各段階は新しいWorkerセッションで実行し、ファイルで引き継ぐ。並列編集、定期起動、プロジェクト外への成果物書き込み、外部サービスの更新・公開は対象外。実行環境の一時ファイル・キャッシュは既存の権限内で扱い、権限を追加しない。

PowerShell、Codex CLI、認証、開発ツールは環境の前提とし、ユーザー指示・適用されるAGENTS.md・環境の権限制約を守る。Gitは必須の制御入力とせず、自動reset・clean・stash・commit・pushは行わない。

## 2. ユーザーが定義する内容

### 2.1. PLAN.mdの記述項目

PLAN.mdには次のユーザー定義だけを記載し、進捗・証拠・指摘・runner用revisionは含めない。schema_versionは1。文字列は空文字不可、配列は表で許可した場合に空にできる。未定義フィールド、JSONの重複キー、不正な型は拒否する。

作成時は、JSONブロックの前に「ユーザープロンプト（原文）」を設け、入力された「目的」「対象」「完成条件」「対象外・制約」を原文のまま保存する。要約・補完せず、未入力の項目は未入力と記す。具体化した内容は次の実行用JSONへ記載し、保存前に原文との整合を確認する。更新時も保存済みの原文を保持する。既存PLANに原文がない場合は推測して復元しない。

| 項目 | 必須 | 内容 |
| --- | --- | --- |
| schema_version / project_id | 必須 | 形式の版、プロジェクト識別子 |
| objective / scope | 必須 | 目的、処理対象 |
| non_goals / constraints | 必須 | 対象外、守る条件。指定がなければ空配列 |
| completion_criteria | 必須 | 完成条件のid、condition、verification。1件以上 |
| work_scope | 必須 | ソースなどを変更できる範囲。読取専用作業なら空配列 |
| input_scope | 任意 | 検証入力の範囲。省略時はプロジェクト全体を基準にする |
| generated_scope | 任意 | ビルド出力など、検証に伴う生成物の書込範囲。既定は空配列 |
| environment_checks | 任意 | 環境識別用の読取コマンド。既定は空配列 |
| references | 任意 | 参照資料のpathとpurpose。既定は空配列 |
| milestones / tasks | 形式上は任意 | 新規作成時は自動生成する節目・初期項目。既存PLANの省略は空配列として扱う |

completion_criteriaには、有限の対象範囲と確認可能な期待結果を書く。「問題がなくなるまで」のような終わりを判定できない条件は避ける。verificationはコマンドだけでなく、文書照合やレビュー手順でもよい。

パスはProjectRoot基準とする。input_scopeを指定してもwork_scopeとreferencesのファイルは検証入力に含める。generated_scopeを使ってソース、設定、参照資料を入力から除外してはならない。範囲の詳細は§5.1に従う。

environment_checksは、必要なツールの版や外部依存を識別するPowerShellの読取コマンド文字列配列とする。実行方法と環境情報が不足する場合の扱いは§5.2に従う。

### 2.2. 初期計画とマイルストーンの自動作成

tasksの項目はid、description、required（真偽値）、depends_on・criterion_ids（ID配列）、acceptance、verificationを持つ。milestonesはid、description、task_ids、acceptanceを持つ。IDは種類ごとに一意とし、大文字小文字を区別して完全一致で参照する。並べ替えで変更しない。

PLAN.mdの新規作成時は、作成担当が目的と完成条件からtasksと1件以上のmilestonesを自動生成する。小規模なら1件、複数の意味のある節目があれば分割する。各milestoneのtask_idsは空にせず、全必須項目をいずれかのmilestoneへ対応付ける。acceptanceには現物・証拠で確認できる到達条件を記す。作成時に網羅性を確認し、進捗や証拠はPLAN.mdへ書かない。

初期項目はユーザーの指定として保持する。Planは内部計画で詳細化・分割できるが、元IDとの対応、必須性、条件を維持する。ユーザー指定の目的・条件を変更する必要があれば、具体案を保存してNeedsInputで停止する。通常の項目追加・分割・順序変更は自動で行う。

ユーザー指定の依存関係、criterion_ids、マイルストーンの対象項目は削除しない。分割時は置換先への対応を保ち、依存順序は中間項目を経由して維持してもよい。

マイルストーンは進捗の節目である。完成に必須の条件はcompletion_criteriaにも記載する。

マイルストーンが置換済み項目を参照している場合は、置換先すべての状態と証拠で到達を判定する。

### 2.3. PLAN.mdの例

JSONを、指定マーカーに囲まれたコードブロックとして1つ置く。制御上の正本はJSONとし、その外側にユーザープロンプトの原文と背景を保存する。原文欄はユーザーの依頼内容の記録であり、生成過程や進捗ではない。

次はPythonプロジェクトの例である。調査段階のテスト失敗を許容し、修正段階では全件成功を求めている。

~~~~markdown
# 既存テストの確認と不具合修正

## ユーザープロンプト（原文）

```text
目的: 既存テストの失敗原因を調べ、不具合を修正する。
対象: srcとtests。README.mdの利用方法も参照する。
完成条件: 既存テストがすべて成功し、必要な修正の妥当性を確認できる。
対象外・制約: 公開APIの変更、テストの削除・無効化は行わない。既存の未コミット変更を保持する。
```

## 実行計画

<!-- autoframe:begin -->
```json
{
  "schema_version": 1,
  "project_id": "sample-test-repair",
  "objective": "既存テストの失敗原因を調べ、不具合を修正する",
  "scope": ["srcとtests"],
  "non_goals": ["公開APIの変更", "テストの削除・無効化"],
  "constraints": ["既存の未コミット変更を保持する"],
  "work_scope": ["src/**", "tests/**"],
  "input_scope": ["src/**", "tests/**", "pyproject.toml"],
  "generated_scope": [],
  "environment_checks": ["python --version", "python -m pip freeze"],
  "references": [{"path": "README.md", "purpose": "利用方法"}],
  "completion_criteria": [
    {
      "id": "C1",
      "condition": "既存テストがすべて成功し、必要な修正の妥当性を確認できる",
      "verification": "python -B -m unittest discover -s tests -v を実行。1件以上実行し、失敗・エラー・skipは0件。修正した場合は差分と再現結果も確認する"
    }
  ],
  "tasks": [
    {
      "id": "T1", "description": "テストを実行し、失敗原因を調べる",
      "required": true, "depends_on": [], "criterion_ids": ["C1"],
      "acceptance": "実行結果と失敗原因、または失敗なしの根拠がある",
      "verification": "ログと原因調査を照合する。この項目ではテスト失敗を許容する"
    },
    {
      "id": "T2", "description": "必要な修正と最終検証を行う",
      "required": true, "depends_on": ["T1"], "criterion_ids": ["C1"],
      "acceptance": "C1を満たす。修正不要なら理由を残す",
      "verification": "C1の検証手順を実行する"
    }
  ],
  "milestones": [
    {
      "id": "M1", "description": "調査・修正完了",
      "task_ids": ["T1", "T2"], "acceptance": "原因と修正結果の対応が確認できる"
    }
  ]
}
```
<!-- autoframe:end -->
~~~~

既存PLANとの互換性のため、tasksとmilestonesの省略は引き続き受け付ける。未設定の場合はPlanが内部計画へ項目とマイルストーンを自動生成し、PLAN.mdへは書き戻さない。コマンド、環境識別、対象範囲は適用先に合わせて定義する。

## 3. 内部計画と記録

### 3.1. 配置と保存

```text
project/
  PLAN.md
  autoframe/                    # 別の場所へ共通配置してもよい
    run.ps1 / invoke-worker.ps1
    lib/                        # 計画・状態・遷移・マニフェスト
    prompts/                    # 共通規則と6段階
    schemas/                    # PLAN・内部記録・段階結果
    templates/PLAN.template.md
    tests/ / README.md
  .autoframe/
    lock
    state.json                  # 現行記録への参照と実行制御
    records/                    # 受理結果・内部計画・証拠の履歴
    manifests/                  # 内容ハッシュで共有
    runs/<run-id>/<attempt-id>/  # 入力参照、ログ、前後差分
```

状態の正本はstate.jsonとその参照先。runnerだけが更新し、Workerには自分の試行用出力先を渡す。記録は参照中に削除せず、内部状態を人が編集する運用にしない。

runnerは新しい記録を確定保存してから、同じファイルシステム上でstate.jsonを一時ファイルから原子的に置換する。未参照記録は未受理として扱う。更新はハートビートを含めて直列化し、二重起動を排他ロックで防ぐ。

内部計画と状態には別の版を用いる。**base_plan_versionは項目定義・進捗・指摘を含む内部計画の版**であり、受理結果による変更時だけ増やす。ハートビート、時間、試行開始の保存では増やさず、実行中のWorker結果を無効化しない。

run_id、attempt_idは再利用しない。受理済みattempt_idの再適用を拒否し、結果の採用、内部計画の版、次の段階、カウンタ更新を1回の状態確定で反映する。

### 3.2. 内部項目と状態

Planは初期項目を基に内部項目を生成する。項目は§2.2の定義に、成果物、状態、証拠参照、阻害理由・解除条件、置換先IDを加える。

```text
pending → implemented → verified
    ↑          │           │
    └──────────┴───────────┘ 修正・再検証待ち
```

blockedは外部待ち、supersededは置換済みを表す。置換時は元条件を継承し、依存元を置換先へ付け替える。必須項目の削除・任意化は受理しない。

**in_progressは項目の状態ではなく、state.jsonの実行中試行と対象IDで表す。** Work開始時にはそれだけを保存し、PLAN.mdや項目定義を変更しない。Workはimplementedまで、Verifyだけがverifiedを提案できる。

依存先は着手時に有効なverifiedでなければならない。必須項目の依存先は、任意指定でも完成に必要となる。Plan後には全体完成条件ごとに必須項目が対応していなければならない。ID・参照・循環・置換の整合性を機械検査する。

部分作業は実施範囲を残し、Verifyがpending（修正・再検証待ち）またはblockedへ戻す。解除条件を確認したblockedはpendingへ戻す。既存成果や失効した証拠の確認だけなら、PlanのrecoverからVerifyへ直接進める。

### 3.3. 指摘と履歴

指摘はid、kind（plan/product）、required、内容、解消条件、対応項目ID、open/resolved、証拠参照を持つ。Planは対応項目を割り当てる。計画の指摘はAudit、成果物の指摘はVerifyまたはCompletion Auditが解消を確認する。誤指摘も理由を残してresolvedにする。

resolved指摘、superseded項目、古い証拠の本文は履歴へ移せる。現行状態には必要なID・置換先・参照を残し、必須条件や依存先を判定対象から落とさない。項目・指摘の省略は削除や解消を意味しない。

## 4. 実行手順

### 4.1. 役割と遷移

```text
Plan → Prepare → Audit → Work → Verify
          ↑        │                │
          └─ 修正 ─┘                │
          └──── 次の作業・修正 ──────┘

節目・一定回数・完成候補 → Completion Audit
  完成: Complete / 未完了: Plan
```

| 段階 | 処理 | decisionと行き先 |
| --- | --- | --- |
| Plan | 内部計画を差分更新し、必須指摘を項目へ対応付ける | ready→Prepare、recover→Verify |
| Prepare | 対象・範囲・手順・検証・時間配分を決める | ready→Audit、completion_candidate→全体監査 |
| Audit | 全体の網羅性と今回の計画を精査・修正する | approved→Work、revise→Prepare |
| Work | 作業、自己検証、残作業を記録する | reported→必ずVerify |
| Verify | 現物と証拠から項目別に判定する | accepted/revise→次の処理を判定 |
| Completion Audit | 完成条件、回帰、必須指摘を確認する | complete→完成検査、incomplete→Plan |

「受理」は有効な報告として採用することを指し、作業成功とは異なる。Verifyのacceptedは対象全件が完了、reviseは未完了を含む結果とする。どちらも形式・権限検査を通れば状態に反映する。

Work以外はreplanも使用できる。Workの再計画希望はreportedのroute_hintで伝え、先にVerifyを通す。各段階はneeds_inputで停止できるが、未検証の部分成果は保持する。

外部待ちは項目単位で記録する。未確定試行の回復を優先し、独立した実行可能項目があれば続行する。必要な処理が外部待ちだけならBlockedとする。単に候補を選べない場合はPlanへ戻す。

Plan・Prepare・Auditは製品を修正しない。Verify・Completion Auditは検証のみ行い、修正はWorkへ戻す。検証の書き込みはgenerated_scopeと自分の証拠保存先に限る。

受理後は、完成の成立、判断待ち、停滞、時間・回数上限を確認する。その後、未検証成果のVerify、必要な再計画、全体監査、次のPrepareの順に選ぶ。PLAN.md変更や形式エラーの結果は受理せず、§5に従う。

### 4.2. 一括実行とAudit

Workには独立項目を最大3件まとめられる。開始時に全依存が満たされ、相互依存・変更範囲の競合がなく、時間内に自己検証・引継ぎまで行えることを確認する。共有出力を使う検証は直列に行う。上限は目標件数ではなく、結果は項目別に返す。

Auditは原則毎回行う。再利用には、PLANハッシュ、内部計画の定義、実行計画、入力署名、関連する未解決指摘の一致を必要とする。実行計画のハッシュには論理的な内容を使い、試行ID・絶対締切・項目の実行中表示は含めない。

WorkにはAuditで受理した修正後の実行計画を渡す。新規・変更後の内部計画は、Work開始前に必須計画指摘の解消と網羅性をAuditで確認する。回復のためのVerifyは先に実行できる。意味的な要件の緩和を機械検査だけで防げるとは扱わない。

モデルは既存のCLI設定を既定とし、PhaseModelsで段階別に指定できる。品質と使用量を確認せず、一律に軽量化しない。

### 4.3. 検証と完成

Verifyは自己申告だけで完了を認めず、未実行・skip・証拠欠落を成功にしない。レビューや文書作業は照合記録で検証でき、コード変更やテスト実行を一律には要求しない。

全体監査は、必須項目と必要依存がすべて有効なverified、新たなマイルストーン到達候補、または所定回数のWorkをVerifyまで受理した場合に行う。複数の実行条件は1回にまとめ、受理した全体監査で間隔カウンタを0にする。

各Workerの入力にはmilestone_progressとして、マイルストーンID、置換を反映した対象ID、検証済み件数、全件数、監査候補かを渡す。証拠失効は件数にも反映する。Verify受理後は件数を実行ログへ表示する。全対象が有効なverifiedなら監査候補とし、Completion Auditがacceptanceを現物・証拠と照合して判定・不足を記録する。件数だけで到達を確定せず、全体完成とも区別する。

重複判定には、要件・内部計画の定義、現行の証拠、必須指摘の状態を使う。マイルストーンは到達条件も含め、単なる版番号や時刻の更新で再監査しない。同じincompleteを再要求されたらPlanへ戻し、停滞に数える。

Completeの条件は以下のすべてとする。

- 現行入力に対する全体監査が合格している。
- 全必須項目と必要依存が有効なverifiedで、各完成条件の証拠がある。
- 必須指摘が解消し、未確定試行・未検証成果が残っていない。

未着手の任意項目は残せる。runnerは構造と整合性、監査Workerは内容の妥当性を確認する。正常終了やSchema合格だけでは完成にならない。

## 5. 差分・証拠・再開

### 5.1. マニフェストと範囲

マニフェストは範囲定義と、正規化・ソートした相対パス、存在・種別、内容のSHA-256を記録する。追加・削除・空の一致集合も比較し、mtimeやサイズだけで一致としない。内容はストリーム処理し、同じ記録をハッシュで共有する。

| 用途 | 対象・保存時点 |
| --- | --- |
| 編集記録 | work_scopeとgenerated_scope。Workの起動前と終了後 |
| 検証入力 | input_scope・work_scope・referencesの和集合から生成物を除外。検証前後 |
| 成果物 | 完了条件が要求する出力。検証時と証拠再利用時 |
| 保護対象 | プロジェクト内の非許可ファイル、PLAN.md、共通配布物。各段階の前後 |

パスはProjectRoot内に限定する。globはルート相対で、`*`と`?`は区切りを越えず、単独の`**`は0階層以上に一致する。`src/**`は配下全体を表す。否定・brace展開は扱わない。大文字小文字は対象ファイルシステムに合わせ、範囲外へ到達するリンクは拒否する。

input_scopeの省略時はプロジェクト全体を基準とする。Git管理領域・runner内部記録・generated_scopeを検証入力から除外し、共通配布物は別途ハッシュ化する。生成物であっても必要な成果物は照合対象とする。

PLAN.md、適用される指示ファイル、Git管理領域、共通配布物、runner内部状態はwork_scope・generated_scopeより優先して保護する。比較から除外するGit管理領域・runner内部記録の操作は禁止し、Worker出力は試行専用領域に限定する。

広いglobとgenerated_scopeの重複は生成物部分を除外できるが、明示した入力やreferencesを除外する指定は拒否する。新規作成予定の入力・編集先の欠落は記録して許容し、必須の参照資料の欠落や読取失敗は不完全な記録とする。

列挙・ハッシュ中に変更を検出したら再採取する。時間内に安定した記録を得られなければ採用しない。前後差分は保存時点間の変更を示し、全操作・変更者・範囲外操作を確定するものではない。事後検査はOSの書き込み制限を代替しない。

### 5.2. 証拠の再利用

検証記録は手順、期待結果、実結果、ログ・成果物参照と次の署名を持つ。

```text
入力署名 = SHA-256(正規化したJSON {
  PLAN.md全体のハッシュ,
  対象の定義・条件・検証手順のハッシュ,
  検証入力マニフェストのハッシュ,
  環境情報・共通配布物のハッシュ
})
```

定義のハッシュにはstatus・証拠ID・時刻を含めない。機械判定用のJSONはキー順と文字エンコーディングを固定する。

環境情報にはOS、PowerShell、CLI、固定した実行設定、environment_checksの終了状態・出力を含める。設定は全段階のモデル指定・権限等を使い、現在の段階・時刻・試行IDを含めない。environment_checksが空なら追加確認なしを意味し、直ちに情報不足とはしない。必要な環境・依存を識別できないと判明した場合は、参考結果を残して該当項目をblockedとし、情報不足を解消して再検証する。

環境コマンドは試行用スクリプトへ保存し、`pwsh -NoProfile -NonInteractive -File`で実行する。PowerShellのエラーとネイティブコマンドの非0終了を失敗として伝播させる。段階・全体の残時間を適用して結果を保存し、失敗を有効な識別として使わない。自然文からコマンドを生成しない。

証拠の再利用には、署名とログ・必要成果物のハッシュの一致、適切な段階による受理が必要。項目はVerify、計画監査はAudit、完成結果はCompletion Auditの受理記録を使う。別種の受理記録で代用しない。

検証前後で入力が異なれば採用せず、Work後は変更後の入力を検証する。完成受理直前にも署名を確認する。署名は宣言された入力の一致を示すだけで、入力定義の完全性はPlan・Auditで点検する。

署名失効は修正の必要性を意味しない。該当項目を再検証待ちにし、必要ならPlanのrecoverからVerifyへ渡す。複数項目を同じ現行入力で再検証できる。過去の調査記録は保持するが、現在のverifiedの根拠にはそのまま流用しない。

### 5.3. 異常終了と回復

Work前に編集マニフェストを確定し、試行ID・対象ID・開始記録を保存してからWorkerを起動する。保存できなければ起動しない。Worker終了後は結果の有無によらず後マニフェストを採取する。

未受理のまま終了した試行は未確定とする。再開時はPIDだけでなく開始時刻なども照合して旧プロセスを識別し、その子プロセスを含めた停止を確認する。別のプロセスを誤って終了させない。停止を確認できなければErrorで止める。

Planが差分と部分成果を照合し、recoverでVerifyへ渡す。回復結果の受理または残作業への差し戻しまで、同じ操作を無条件に繰り返さない。Verify・環境確認が中断された場合も、生成物と証拠を未確認のまま再利用しない。

後記録が不完全なら時間内で再採取する。前記録が欠落・破損していたら、差分の完全性は主張せず、既存証拠を無効にして現物から再計画する。現在の要件で検証できない部分はblockedまたはneeds_inputとして残す。記録不足だけで永久に再開不能にしない。

内部記録なしでもNewRunを開始できるが、旧状態の完了は引き継がない。成果物・未コミット変更・破損記録を保持する。

### 5.4. PLAN.mdの変更

PLAN.mdは停止中に編集することを基本とし、runnerは各段階の開始・受理時に全体ハッシュを比較する。実行中の変更を検出したら古い結果を現行計画へ適用せず、部分成果を保持してPlanへ戻す。不正なPLAN.mdならErrorで停止する。

ユーザー変更に合わせて内部計画・指摘・証拠を再評価する。削除・変更されたユーザー要件への対応も履歴に残し、失効した要件を自動で復活させない。project_idの変更はNewRunを必要とする。runnerによるPLAN.mdへの書き戻しは行わない。

## 6. 停止と文書量の制御

### 6.1. 停滞・上限

進捗は実装・検証・原因特定など、Verifyが受理した具体的な前進。カウンタは同じ試行について1回だけ更新し、Resumeで再加算しない。

| カウンタ | 加算・リセット | 動作 |
| --- | --- | --- |
| NoProgressWorks | WorkをVerifyまで受理し、前進なしなら加算。前進ありで0 | 2回でPlan、4回でStalled |
| WorklessPlanReturns | 前回Plan以降にWorkがないまま、自動でPlanへ戻るたび加算。Work開始で0 | 3回でStalled |
| AuditRevisions | Work開始までのAudit差し戻しを加算。Work開始で0 | 2回でPlan |

回復・再検証のVerifyで新しい有効な完了項目や必須指摘の解消が増えた場合も、WorklessPlanReturnsを0にする。同じ証拠の再利用や計画の言い換えでは解除しない。

初回・ResumeでのPlan入場はWorklessPlanReturnsに加算せず、保存済み値も消さない。項目ID・計画の変更だけではリセットしない。Work開始だけではNoProgressWorksを解除しない。

Stalledは通常のResumeや上限引上げで解除しない。原因修正後のResetStallCountersとResetReasonで3カウンタだけを解除し、累計時間・総試行数は維持する。停滞は単なる上限到達より先に判定する。

### 6.2. 実行状態

| 状態 | 終了コード | 意味 |
| --- | --- | --- |
| Complete | 0 | 全体の完成を受理 |
| Paused | 2 | 時間・回数上限、ユーザー中断、段階タイムアウト |
| Blocked | 3 | 独立した実行可能項目がなく外部待ち |
| NeedsInput | 4 | 要件変更などの判断待ち |
| Stalled | 5 | 作業または計画の反復が停滞 |
| Error | 6 | 起動、CLI、形式、保存、整合性などの異常 |

実行中はRunningとする。Pausedは再開可能な保存状態を示し、再開すれば必ず進むという意味ではない。エラーは自動再試行しない。強制終了などでコードを返せなくても完成扱いしない。

### 6.3. 簡潔な文書

**文書は判定に必要な正確性を保ち、最小限にする。**

| 文書 | 必要な内容 |
| --- | --- |
| PLAN.md | ユーザー定義だけ |
| 実行計画 | 対象ID、差分、範囲、手順、検証、時間配分 |
| 実行報告 | 項目別結果、証拠参照、未実行、阻害要因、次の操作 |
| 更新案 | 基準の内部計画版と、変更する種類・ID・フィールド |
| 監査結果 | 判定、未解決指摘、解消条件 |

要約は原則5箇条以内。定義や証拠はIDで参照し、ログ・コード・背景・履歴を転載しない。必要な失敗や未確認事項を省略せず、正確性のためなら長さの目安を超えてよい。

Workerには必要な対象・依存・指摘・差分・証拠参照を渡す。PLAN.mdの写しやマニフェストは共有し、各文書へ複製しない。履歴本文は必要なときだけ読む。

### 6.4. 生成するプロンプトの共通規則

全Workerの入力に次の指示を含め、段階固有の目的・許可操作・Schemaを追加する。

- 指定された段階だけを担当し、次のWorkerを起動しない。上位指示と権限制約を守る。
- PLAN.mdは読取専用。現行の内部計画と受理記録を使い、履歴と混同しない。不足は参照して確認し、推測で補わない。
- 許可範囲だけを編集し、無関係な変更を保持する。役割・受理権限は§3・§4に従う。
- 必須条件を緩和・省略して完了にしない。判断が必要なら具体案とneeds_inputを返す。
- 項目別の結果を示し、一部成功・未実行・skip・証拠欠落を全件成功にしない。証拠は§5、進捗は§6.1に従って扱う。
- 保存余裕の開始までに作業を区切り、部分成果・未検証事項・阻害要因・次の操作を締切前に保存する。
- 記述は§6.3、結果の識別子・形式・更新方法は§8.1に従う。

共通指示は独立ファイルでも各段階への組込みでもよい。生成時に必要な規則を展開し、本書全体を毎回転載しない。runnerが実際に各Workerへ渡す入力を検証する。

## 7. run.ps1の仕様

### 7.1. 起動引数

PowerShell 7.4以降のpwshで実行する。引数名・型・以下の動作を初版の公開仕様とする。実装の対応環境・検証状況は[README](autoframe/README.md)を参照する。

| 引数 | 型 | 新規実行の既定値 | 動作・制約 |
| --- | --- | --- | --- |
| ProjectRoot | string | 呼出元の現在のフォルダー | 既存のプロジェクトルート。PLAN.mdをここから読む |
| MaxRunMinutes | int | 480 | 再開前を含む累計稼働時間の上限。分単位 |
| Resume | switch | false | 保存済みの実行を再開 |
| NewRun | switch | false | 保存済み状態があっても、明示的に別の実行を開始 |
| MaxPhaseAttempts | int | 100 | Worker起動試行の累計上限。起動失敗も1回に数える |
| PhaseTimeoutMinutes | int | 60 | 各Workerの起動から終了までの上限 |
| SaveReserveMinutes | int | 5 | 各Workerの締切前に確保する引継ぎ時間 |
| StopTimeoutSeconds | int | 60 | 上限・中断時のプロセス停止と記録保存の猶予 |
| MaxTasksPerWork | int | 3 | 1回のWorkの項目数上限。1〜3 |
| CompletionAuditInterval | int | 3 | 全体監査の間隔。Verifyまで受理したWork回数 |
| CodexCommand | string | codex | コマンド名またはCLIランチャーのパス。追加引数を含めない |
| PhaseModels | hashtable | 空 | 段階名とモデルIDの対応。未指定の段階はCLI設定を使用 |
| ResetStallCounters | switch | false | Resume時に停滞カウンタだけを解除 |
| ResetReason | string | 未指定 | カウンタ解除の理由。解除時は空文字不可 |

数値は正の整数、MaxTasksPerWorkは1〜3、SaveReserveMinutesはPhaseTimeoutMinutes未満とする。0や負数による無制限実行は設けない。ResumeとNewRunは併用不可。ResetStallCountersはResumeとResetReasonを必要とし、ResetReasonだけの指定も拒否する。

PhaseModelsのキーはPlan、Prepare、Audit、Work、Verify、CompletionAuditだけを許容し、値は空でないモデルIDとする。明示指定した表は保存済みの表を置き換える。hashtableを渡す場合はPowerShell内からスクリプトを直接呼ぶ。JSON文字列への暗黙変換は行わない。

### 7.2. パス・起動・入出力

ProjectRootは呼出元基準で既存フォルダーの絶対パスへ解決する。親のPLAN.md探索やルート自動作成はしない。Worker・検証の作業ディレクトリもProjectRootとする。

共通配布物はrun.ps1の所在から、CodexCommandはPATHまたは呼出元から解決する。.exe・.cmd・.ps1を扱い、alias・functionは拒否する。空白や特殊文字を含むパス・引数は、文字列連結やInvoke-Expressionで起動しない。

プロンプトはstdin、最終結果・イベント・stderrは別ファイルに渡す。ログは逐次保存し、全量をメモリに保持しない。子プロセスの残留を確認してから結果を受理する。

CLI・ランチャーの終了コードは元の値で記録し、非0終了を一律に1へ置き換えない。

起動前に引数、PLAN・共通ファイル・CLI、Resume対象の状態形式、二重起動を検査する。失敗時は既存状態を上書きせずErrorとする。認証・権限を自動変更せず、CLI・モデルの非対応を記録して停止する。

### 7.3. 新規実行とResume

| 指定・保存状態 | 動作 |
| --- | --- |
| 指定なし、状態なし／前回Complete | 履歴を保持し、新しいrun_idでPlanから開始 |
| 指定なし、未完了・破損・非対応状態あり | Error。ResumeまたはNewRunを案内 |
| Resume、対応する状態あり | 同じrun_idと累計値で回復・Planへ |
| Resume、状態なし・破損・非対応 | Error。黙って新規実行にしない |
| NewRun | 旧状態を履歴へ保存し、新しいrun_id・累計値で開始 |

Resumeで省略した数値・CodexCommand・PhaseModelsは保存値を使い、明示分だけ変更する。PSBoundParametersで区別し、変更履歴を残す。ProjectRootと操作switchは引き継がない。

同じ実行のProjectRootとproject_idは固定。NewRunでも旧プロセスの停止確認と未確定成果の回復を省略せず、破損・非対応状態の完了判定は引き継がない。

受理済みでもVerify前のWorkは、NewRunで回復対象として引き継ぐ。実行前マニフェストへの参照を試行ディレクトリにも保存し、同じ作業を繰り返す前に部分成果を確認する。

CompleteのResumeは、現在の署名と証拠を残時間内で再確認する。有効なら0で終了し、変更があればPlanへ戻る。Stalledは§6.1の明示解除を必要とする。

完成証拠の欠落・破損も再監査の対象とし、古い監査の重複抑止を解除する。残時間・試行回数が不足する場合はPausedとし、上限変更後に再開できるようにする。

### 7.4. MaxRunMinutesと停止

MaxRunMinutesはrun_idの累計上限。480分使用後に720でResumeすると残りは約240分となる。

上限到達時は新規Workerの起動を止めるとともに、動作中のWorkerと子プロセスを強制停止する。Workerの自然終了を上限後まで待ち続ける方式ではない。SaveReserveMinutesは締切前の保存・引継ぎ指示であり、Workerが従わない場合も締切で停止する。

ロック取得から終了処理までを稼働時間とし、Worker・検査・保存を含め、正常停止中の待機を除く。単調増加時計を用い、段階境界と30秒以内の間隔で累計値・UTC時刻・稼働中フラグを保存する。

強制終了で終了時刻が不明なら、保存時刻から再開時の停止確認までを非負の値で保守的に加算し、推定区間を記録する。二重計上せず、実際より長く計上された場合は必要に応じて累計上限を増やす。

Worker締切は全体残時間とPhaseTimeoutMinutesの短い方。その内側にSaveReserveMinutesを確保する。保存余裕以下なら起動しない。起動回数上限は起動済み結果の受理・保存を妨げない。

runner処理にも全体残時間を適用する。採取や環境確認の時間切れは不完全として保存し、再開時に回復する。受理記録の照合・停止確認を優先し、残時間不足ならWorkerを起動せずPausedとする。

締切・Ctrl+Cでは新規起動を止め、StopTimeoutSeconds以内に子停止と回復情報の保存を試みる。成功すればPaused、確認不能ならError（終了不能なら未確定）とする。未完の後マニフェストは再開時に採取する。猶予も累計時間に含み、OSの停止処理まで厳密な終了時刻を保証するものではない。

### 7.5. 起動例

通常開始とResumeはQuickStart参照。以下は追加の指定例である。

```powershell
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot 'C:/Projects/Sample App' -MaxRunMinutes 120
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -Resume -MaxRunMinutes 720
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -NewRun
pwsh -NoProfile -File ./autoframe/run.ps1 -ProjectRoot . -Resume -ResetStallCounters -ResetReason '完成条件の曖昧さを修正'
```

## 8. 出力契約と実装計画

### 8.1. 結果の受理

結果の共通フィールドは、schema_version、run_id、attempt_id、phase、input_plan_hash、base_plan_version、execution_plan_hash、execution_plan、decision、route_hint、summary、task_results、changes、findings、evidence、progressとする。

phaseは§7.1の段階名、route_hintはnull／continue／replanとする。execution_plan_hashは入力で渡された計画のハッシュ（なければnull）。Prepare・Auditはexecution_planに新しい計画を返し、runnerがそのハッシュを計算・保存する。他段階のexecution_planはnull。未定義フィールドを拒否する。

task_resultsはID・status・証拠ID・残作業を持ち、blockedなら理由と解除条件も持つ。Workはpending／implemented／blocked、Verifyはpending／verified／blockedを返す。両段階で対象IDの省略・重複・混入を拒否する。他段階は影響する項目のpending／blockedだけを提案できる。

changesは種類（task/milestone/finding）、ID、変更フィールドを指定する。省略は変更なし、新規IDは追加として検査する。findingsは新規指摘、evidenceは証拠のID・種類・相対パス・ハッシュ・対象ID・入力署名、progressは具体的な前進と証拠IDを持つ。該当しない配列は空とする。

次はWork結果の要部である。実際の出力には共通フィールドをすべて含める。

```json
{
  "decision": "reported",
  "route_hint": "continue",
  "summary": "T2を実施。Verify待ち。",
  "task_results": [
    {"id": "T2", "status": "implemented", "evidence_ids": ["E12"], "remaining": []}
  ],
  "changes": [],
  "findings": [],
  "progress": [{"task_id": "T2", "description": "再現テストを追加して修正", "evidence_ids": ["E12"]}]
}
```

進捗状態はtask_resultsから反映し、changesでは更新しない。定義変更・supersededへの置換はPlan、計画指摘の解消はAudit、成果物指摘の解消はVerify・Completion Auditに制限する。Verify以外からverifiedを受け付けない。

runnerはCLI終了、子プロセス終了、Schema、識別子、PLANハッシュ、base_plan_version、対象ID、更新権限、証拠ファイル、前後差分を照合する。不正な結果はErrorとし、部分成果は回復対象に残す。PLAN.mdの外部変更は§5.4で扱う。

時間・カウンタだけの状態更新と内部計画の版を区別する。論理的な更新は§3.1の手順で一度だけ確定し、自然文や古い結果から状態を推測して進めない。

### 8.2. 実装順序と完了条件

| 順序 | 実装するもの | その段階の検証条件 |
| --- | --- | --- |
| 1 | PLAN・内部記録・結果Schema、読込、ID・依存検査 | 不正型・重複キー・循環・不明参照を拒否。PLANへの書戻しなし |
| 2 | ロック、状態保存、版管理、履歴・マニフェスト | 保存各境界の中断で二重受理しない。追加・削除・リンク・読取失敗を扱う |
| 3 | 純粋な遷移処理、停滞・予算判定 | 成功・差し戻し・部分成功・Workなしループ・回復を決定的に判定 |
| 4 | Workerアダプター、時間制限、プロセス停止 | 疑似CLIでstdin・結果・大量ログ・起動失敗・子残留・タイムアウトを確認 |
| 5 | run.ps1、共通・段階別プロンプト、再開 | 既定値・継承・明示上書き・NewRun・状態破損・空白パスを検証 |
| 6 | PLANテンプレート、README、統合検証 | 異なる用途の一時プロジェクトで、完成・停止・証拠失効・履歴化まで通す |

疑似Workerによる試験を既定とし、実Codexは明示的な疎通試験に分ける。疎通では利用版の引数・認証・構造化出力・停止を確認し、疑似試験だけで実CLI対応を宣言しない。NativeAOTは既定試験で実行しない。

最終受入では、最終WorkのVerify、依存先の証拠失効、環境不足、監査の重複防止、必須指摘の保持、回復中の上限、ハートビート中の結果受理、通常Resumeで停滞を回避できないことを確認する。生成された各プロンプトに§6.4が含まれることも検査する。

全段階と実CLI疎通の結果を記録した時点で実装完了とする。疎通できない環境では、基盤検証済み・実CLI未確認を区別する。性能は起動回数、総時間、実作業時間、使用量、差し戻しで測る。

CLI接続時の参照: [公式の非対話実行ドキュメント](https://learn.chatgpt.com/docs/non-interactive-mode)。具体的なフラグは利用版で確認する。

## 付記: ビルド手順

```text
SPEC.mdを仕様の正本として、
<配置先>/autoframe/に自動実行フレームワークを実装してください。

§8.2の順序で、ps1、共通・6段階のプロンプト、JSON Schema、
PLANテンプレート、疑似Workerによる検証、簡潔な実行方法を作成してください。
§6.4の共通規則と§7の公開インターフェースを守り、
プロジェクト固有の処理をスクリプトへ埋め込まないでください。

既存ファイルを確認して必要な箇所を更新し、正確かつ簡潔に実装してください。
実CLIの疎通試験は疑似Workerの試験と分け、未実施事項を明記してください。
NativeAOT、製品作業用PLAN.mdの作成、本番の自動実行は開始しないでください。
```

作成した実行基盤は別プロジェクトでも再利用できる。既存のプロンプトファイルを仕様の補完に必要としない。
