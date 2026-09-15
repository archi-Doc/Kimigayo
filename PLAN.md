# Kimigayoコンパイラー全決定事項の実装計画

## ユーザープロンプト（原文）

```text
目的: Kimigayoコンパイラーを全て実装すること
対象: SPEC.md, STATUS.md
完成条件: SPEC.mdに記載されている決定事項（docフォルダー内への参照を除く）を全て実装すること
対象外・制約: docフォルダー内の仕様は、未確定のため参照しない
```

## 対象の解釈と構成

SPEC.mdは総合目次であり、spec/の22章が本文です。付録Aは規範的なcompiler要求、Bは任意の参考算法、Dは設計の延期境界、E/Fは用語・構文の補助です。旧doc/はdraft/へ改名されているため、両方を参照対象から除外します。本文へ統合済みの依存・成果物等の規則は対象です。参照先のみの優先仕様から未記載の意味を推測しません。

solutionはKimi（compiler/CLI）、xUnitTest、Benchmark、Playgroundで構成されます。製品処理はKimi/Compiler、Kimi/SolutionAndProject等、native helperと検証scriptはbackend/windows-x64、実行例はexamplesにあります。.NETの対象はnet10.0、test runnerはglobal.jsonでMicrosoft.Testing.Platformを選択しています。nativeの版・ABI・供給hashはbackend/windows-x64/profile.jsonを正本とします。

この計画は既存実装を再実装する指示ではありません。規則台帳で既実装部分の妥当性を確認し、不足と一般化を閉じます。本文で禁止/延期されたvirtual dispatch、runtime Contract View、追加並行API等を「全て実装」のために導入しません。確定したobject view/metadata/間接呼出等は個別に実装します。部分仕様の未確定部分を解くことが必須条件を左右する場合だけ、影響を明示して質問します。

## 既存の検証入口と将来の実行順

以下は将来の実装・検証時に使用する手順です。このPLAN作成では実行しません。設定・入力の指紋、実行件数、期待値、終了コードを結び付け、現物と一致しない過去の成功記録は使用しません。

```powershell
# restoreが必要な場合に先に行う。
dotnet restore Kimigayo.slnx

dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore
dotnet build Kimigayo.slnx -c Release --no-restore -v:minimal
dotnet test --project xUnitTest/xUnitTest.csproj -c Release --no-build --no-restore

# 機能単位の絞り込み。実装時に存在するclass名へ置換する。
dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore -- --filter-class '*ElementEmissionTest'

# 固定版toolchainが既にあることを確認してから、直列実行する。
./backend/windows-x64/test-toolchain.ps1
./backend/windows-x64/build.ps1
./backend/windows-x64/test-kernel32.ps1
./backend/windows-x64/test-emission.ps1 -Configuration Debug
./backend/windows-x64/test-emission.ps1 -Configuration Release
./backend/windows-x64/test-scalars.ps1 -FixturePattern '*.ll'
./backend/windows-x64/test-cli.ps1 -Configuration Release
./backend/windows-x64/test-artifact-paths.ps1

dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- build examples/Hello/Hello.kimiproj
dotnet run --project Kimi/Kimi.csproj -c Release --no-build -- run examples/Hello/Hello.kimiproj
./backend/windows-x64/test-manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json

# 性能は対応経路を選び、同一入力と設定で比較する。
dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*'
```

managed testがnative fixtureを生成します。test-emissionはbuild.ps1による一致した候補検証を必要とし、build.ps1は採用hashと一致した場合だけ生成archiveをtoolchain/windows_x64へ配置します。これはLLVMのダウンロードではありません。native scriptsはLLVM verifier、実objectの依存symbol、O0/O2、プロセス出力/終了を検査します。0 fixtureでの成功表示を許さず、実装対象のfixtureが実際に含まれたことも確認します。

既存scriptだけでは将来の全機能を覆えません。各taskでsource正負テスト、構造IR、COFF/C ABI、破棄/確保監査、破損/失効/中断、必要な静的証明を追加し、最後に全件を確認します。Libraryはobjectと未最適化body/signatureを確認し、未規定の外部リンク成功を求めません。弱参照のordering、一般借用、償却計算量は単なるO2実行成功では代替しません。NativeAOTは実行しません。

## 仕様と初期項目の対応

第1章から第22章はC01–C22に一対一で対応します。各criterionをtaskのcriterion_idsへ、全必須taskをmilestoneのtask_idsへ対応付けています。付録Aの主担当は次のとおりです。T01の台帳で各節の表行・境界・無番号規則へ細分化し、T34/T35で全体を逆引き確認します。

| 必須付録 | 主担当task |
| --- | --- |
| A.1 Source identity | T03, T27, T28, T30 |
| A.2 Directives | T04 |
| A.3 Binding/caches | T05, T06, T07, T12, T27, T30 |
| A.4 Property | T14 |
| A.5 Raw pointer | T18 |
| A.6 Literal representation | T03, T08, T17 |
| A.7 Cleanup | T09, T11, T14, T15 |
| A.8 Callable/object | T06, T10, T12, T21, T22, T23 |
| A.9 Refinement/require | T09, T12 |
| A.10 Enum/Pattern | T16 |
| A.11 Static Contract | T07 |
| A.12 Generic schemas | T06, T10, T19, T20 |
| A.13 Sequence | T24, T25, T26 |
| A.14 Layout/runtime | T02, T13, T15, T17, T18, T20, T21, T22, T23, T31, T32, T33 |
| A.15 Control flow | T09, T15, T16, T21, T26 |
| A.16 Dependencies/reuse | T28, T29, T30, T31, T33, T34 |

本文にも未解決の整合判断があり得ます。たとえばstring concatenationは§22.1の必要操作に含まれる一方、§13.3では取得・Loan・結果所有権・失敗規則が未確定です。T01/T17はこの差を明示して扱い、参照禁止のdraftで補完せず、解消前に実装済み/対象外と決めません。これは全体の目的を縮小する規定ではありません。

## 実行計画

制御上の正本は以下のJSONです。各taskは将来内部項目へ分割できますが、元ID・必須性・依存・完成条件・milestoneの対応を保持します。milestone到達も完成条件の充足も、現入力に対する確認可能な根拠で判定します。

<!-- autoframe:begin -->
```json
{
  "schema_version": 1,
  "project_id": "kimigayo-spec-complete",
  "objective": "Kimigayoコンパイラーについて、SPEC.mdとそのspec/本文に記載された全決定事項を、doc/およびその改名先draft/への参照内容を除外して実装し、規則ごとの確認可能な証拠で完成を判定する。",
  "scope": [
    "SPEC.md、本文22章、規範的付録A。Dの設計境界とE/Fの参照を本文の規範性に従って扱う。対象は作成時の参照本文にある有限の決定事項であり、実行開始/再計画時の本文差分は規則台帳へ対応させる。",
    "STATUS.mdの既存対応と未対応を実コードで照合し、既実装機能も保持/一般化/退行検証の対象とする。前段のみの実装を完成としない。",
    "compilerの字句/構文・Binding/型/証明・所有権/Origin/効果・Lowering/ABI・Core/Windows runtime・CLI・依存/生成/成果物と、それを検証するテスト、fixture、benchmark、利用例。",
    "実行targetは本文が定義するwindows-x64-v1。Libraryは規定されたobject生成/検査まで含み、未規定の外部Kimigayo ABIを追加しない。"
  ],
  "non_goals": [
    "doc/**と改名先draft/**の仕様を読んで要件を補充すること。SPEC内の参照先優先通知もこの除外を上書きしない。本文へ既に統合された具体的規則は除外しない。",
    "延期/未導入の言語機能、外部参照だけにあるComposition Root/test syntax詳細、追加OS/CPU、source concurrency、未規定の公開APIを新たに仕様決定すること。",
    "autoframeの変更・自動起動、KimiCode全体/LSPの非仕様機能、CI運用・実registryへの公開・配布の開始。既存compiler内の仕様に必要な表示/整形/診断不変条件は対象に含む。",
    "NativeAOTのbuild/publish/test、外部サービスへの送信、toolchain自動ダウンロード、無関係なリファクタリング。"
  ],
  "constraints": [
    "今回の依頼で書くファイルはPLAN.mdだけ。tasks/verification/environment_checksは将来ユーザーが開始を指示した実装実行の計画であり、作成時に製品修正・製品テスト・runnerは実行しない。",
    "SPEC §1.3に従う。must/must notおよび無修飾の規則は必須、Specified not implementedも必須。should/参考算法は任意。Deferred designは導入せず、明示禁止を検証する。Partially specifiedでも確定済み部分を丸ごと除外しない。",
    "実装選択は本文の許可範囲で具体化し、必要ならowning spec章/STATUSへ記録する。完成のために必須規則を削除・延期へ格下げ・未対応診断で免除しない。意味/公開APIを左右する必須情報が不足する時だけ、元節/影響criteria/選択肢を示してNeedsInputにする。",
    "stage単位のstubだけを完成としない。各内部細分化は仕様→Binding/所有権→実行計画→IR/runtime→正負/退行テストを閉じる。未対応の合法構文を出力する段階では明示診断し、全体完成までには確定範囲の未実装を解消する。",
    "既存のユーザー変更・無関係なファイルを保持する。doc/draft/obsolete/autoframe/AGENTS/PLANやGit内部を実装Workerの編集対象にしない。commit/reset/clean/stash/pushは自動実施しない。",
    "重複を減らし、internされた型/identity、再利用可能な配列/List/範囲・worklistを使う。warm 0 Bの既存保証を維持し、実際の対象経路で測定する。無根拠な全経路0 B/高速化率を完成条件にしない。allocation-free化で寿命/破棄/失敗順序を変えない。",
    "再Parse/再Bindの古いKoto/Origin/Symbolや、未検証のcache/ABI/結果計画を再利用しない。一般式・0サイズ・未到達/未使用コードの必須チェックを省かない。",
    "NativeAOTは実行しない。managed testは.NET10/Microsoft.Testing.Platformを使い、Debug/Releaseを明示する。通常のLLVM生成native試験はNativeAOTとは別で実装開始後の検証対象。",
    "LLVM版/実hash/供給symbolはbackend profileに従う。AllowUnpinnedToolchainや検証無効化を完成証拠に使わない。backend変更時は実COFF/native検証と採用根拠を整え、単にhashを合わせて承認しない。",
    "検証の出力はgenerated_scopeへ集め、package/store/Mod/外部module fixtureはbin/conformance等の隔離されたproject内へ作る。通常ツールのcacheは既存権限に従い、新たなproject外成果物や実publish先は使わない。生成物にソースや必須参照資料を紛れ込ませない。",
    "同じfixture/outputを使うmanaged/native/CLI scriptsは直列に実行する。非停止/Abort/外部tool/子プロセスを時間制限とkillで管理する。終了コードだけでなくstdout/stderr/件数/生成物とproof coverageを検証する。",
    "PLANには進捗・証拠・指摘・runner revisionを書かない。将来の証拠はrunner試行領域とgenerated_scope、製品実装状況はSTATUS、規則対応/期待値は検証入力へ保存する。仕様や入力変更で影響する証拠は取り直す。",
    "本文内の必須記述と延期/未定義の衝突は台帳で独立して保持する。対象が全実装であることを理由に意味を発明せず、未確定であることを理由に必須候補を黙って削除しない。owning節からも判断できなければ該当task着手前に確認し、未解決のまま全体完成としない。"
  ],
  "work_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "Benchmark/**",
    "Playground/**",
    "examples/**",
    "SPEC.md",
    "spec/**",
    "STATUS.md",
    "README.md",
    "Kimigayo.slnx",
    "Directory.Build.props",
    "global.json",
    ".editorconfig",
    "stylecop.json"
  ],
  "input_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "Benchmark/**",
    "Playground/**",
    "examples/**",
    "SPEC.md",
    "spec/**",
    "STATUS.md",
    "README.md",
    "Kimigayo.slnx",
    "Directory.Build.props",
    "global.json",
    ".editorconfig",
    "stylecop.json",
    "AGENTS.md",
    ".gitattributes",
    ".gitignore"
  ],
  "generated_scope": [
    "Kimi/bin/**",
    "Kimi/obj/**",
    "xUnitTest/bin/**",
    "xUnitTest/obj/**",
    "Benchmark/bin/**",
    "Benchmark/obj/**",
    "Playground/bin/**",
    "Playground/obj/**",
    "bin/**",
    "backend/windows-x64/bin/**",
    "examples/**/bin/**",
    "examples/**/obj/**",
    "BenchmarkDotNet.Artifacts/**",
    "Logs/**",
    "toolchain/windows_x64/*.lib"
  ],
  "environment_checks": [
    "$PSVersionTable.PSVersion.ToString()",
    "dotnet --info",
    "Get-Content -LiteralPath global.json -Raw",
    "Get-Content -LiteralPath backend/windows-x64/profile.json -Raw",
    "& './toolchain/opt.exe' --version",
    "& './toolchain/llc.exe' --version",
    "& './toolchain/clang.exe' --version",
    "& './toolchain/lld-link.exe' --version",
    "Get-FileHash -Algorithm SHA256 -LiteralPath toolchain/opt.exe,toolchain/llc.exe,toolchain/clang.exe,toolchain/lld-link.exe,toolchain/llvm-nm.exe,toolchain/llvm-readobj.exe,toolchain/llvm-lib.exe,toolchain/llvm-dlltool.exe,toolchain/windows_x64/kimi_backend_windows_x64_v1.lib"
  ],
  "references": [
    {
      "path": "SPEC.md",
      "purpose": "仕様総合目次。本文へ統合済みの決定事項のみ採用し、doc/draftへのリンク・優先通知から参照先内容を補充しない。"
    },
    {
      "path": "STATUS.md",
      "purpose": "現実装と既存検証入口。完了証拠としては実行時に再確認し、残る制限を仕様の延期と混同しない。"
    },
    {
      "path": "spec/01-overview.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/02-source-and-lexical-structure.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/03-types-and-values.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/04-arrays-indexing-and-slices.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/05-raw-pointers-and-unsafe-memory.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/06-declarations-and-containers.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/07-functions-and-callable-values.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/08-generics-constraints-and-contracts.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/09-names-signatures-and-access.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/10-overload-resolution-and-inference.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/11-properties.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/12-expressions.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/13-operators-and-assignment.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/14-control-flow.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/15-ownership-and-lifetime-analysis.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/16-scope-exit-and-destruction.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/17-failure-handling.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/18-modules-and-dependencies.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/19-compile-time-directives.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/20-compilation-configuration.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/21-layout-runtime-and-code-generation.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/22-core-execution-and-foreign-functions.md",
      "purpose": "SPEC本文。対応章の規範的規則・明示禁止・許可された実装選択の正本。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/appendices/A-compiler-requirements.md",
      "purpose": "付録Aは全必須情報/検証要求、Dは延期/禁止境界。Bは任意の参考算法、E/Fは用語/構文の補助で本文を上書きしない。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/appendices/B-reference-models.md",
      "purpose": "付録Aは全必須情報/検証要求、Dは延期/禁止境界。Bは任意の参考算法、E/Fは用語/構文の補助で本文を上書きしない。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/appendices/D-deferred-features.md",
      "purpose": "付録Aは全必須情報/検証要求、Dは延期/禁止境界。Bは任意の参考算法、E/Fは用語/構文の補助で本文を上書きしない。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/appendices/E-terminology.md",
      "purpose": "付録Aは全必須情報/検証要求、Dは延期/禁止境界。Bは任意の参考算法、E/Fは用語/構文の補助で本文を上書きしない。doc/draftリンクは辿らない。"
    },
    {
      "path": "spec/appendices/F-syntax-summary.md",
      "purpose": "付録Aは全必須情報/検証要求、Dは延期/禁止境界。Bは任意の参考算法、E/Fは用語/構文の補助で本文を上書きしない。doc/draftリンクは辿らない。"
    },
    {
      "path": "AGENTS.md",
      "purpose": "性能・文書・draft・NativeAOT等の適用制約。"
    },
    {
      "path": "Kimigayo.slnx",
      "purpose": "solutionと4つの.NET projectの構成。"
    },
    {
      "path": "global.json",
      "purpose": "Microsoft.Testing.Platformの指定。"
    },
    {
      "path": "Directory.Build.props",
      "purpose": "共通C#設定・package version。"
    },
    {
      "path": "Kimi/Kimi.csproj",
      "purpose": "net10.0 compiler/CLI・embedded runtime/profile/Core等の構成。"
    },
    {
      "path": "xUnitTest/xUnitTest.csproj",
      "purpose": "xUnit v3/managedテスト構成。"
    },
    {
      "path": "Benchmark/Benchmark.csproj",
      "purpose": "BenchmarkDotNetの構成。"
    },
    {
      "path": "Benchmark/Program.cs",
      "purpose": "存在するbenchmark入口/filters。"
    },
    {
      "path": "backend/windows-x64/profile.json",
      "purpose": "固定LLVM 22.1.8・ABI2・実backendとdlltool/kernel32 hash/catalog。"
    },
    {
      "path": "backend/windows-x64/README.md",
      "purpose": "native helper build/検証/採用境界。リンクは許可されたproject資料に限る。"
    },
    {
      "path": "backend/windows-x64/test-scalars.ps1",
      "purpose": "managed testsが生成する全fixtureのLLVM verify/O0/O2・symbol・timeout検証。"
    },
    {
      "path": "backend/windows-x64/test-emission.ps1",
      "purpose": "生成module/runtime fault adapterの検証。先に一致したbackend候補が必要。"
    },
    {
      "path": "backend/windows-x64/test-cli.ps1",
      "purpose": "通常.NET DLLでbuild/run/emitとtoolchain/exit伝播を検証。NativeCompiler引数を使わない。"
    },
    {
      "path": "backend/windows-x64/test-toolchain.ps1",
      "purpose": "toolchain版/失敗policyの既存検証。"
    },
    {
      "path": "backend/windows-x64/test-kernel32.ps1",
      "purpose": "project-owned import生成/整合性検証。"
    },
    {
      "path": "backend/windows-x64/test-artifact-paths.ps1",
      "purpose": "共有成果物のpath/provenance policy検証。"
    },
    {
      "path": "backend/windows-x64/test-manual-build.ps1",
      "purpose": "明示nativeビルド経路の回帰。"
    },
    {
      "path": "examples/Hello/README.md",
      "purpose": "最小ApplicationのCLIと正確な出力。"
    }
  ],
  "completion_criteria": [
    {
      "id": "C01",
      "condition": "§1とSPEC.mdの参照本文に従い、対象の全決定事項を節・規則単位で列挙し、実装・必須テスト・担当taskへ対応付ける。doc/と旧doc/であるdraft/の参照内容を取り込まない。",
      "verification": "全22章・付録Aの各規則を、必須、明示的禁止、許容される実装選択、推奨、未確定/延期、参照先のみのいずれかへ分類した台帳を確認する。表・例示の元規則・本文中の無番号規則も含める。必須規則の未対応付けは0件。STATUSの未対応を仕様の延期と誤分類しない。"
    },
    {
      "id": "C02",
      "condition": "§2の字句、Unicode、名前、数値/文字/string literal、改行・レイアウト・escapeを実装する。",
      "verification": "Lexing/ParsingとSourceEncoding・UnicodeIdentifier・NumberLiteral・CharLiteralParse・StringLiteralParse等を拡張し、正常/不正UTF-8、NFC、数値境界、raw/escaped文字列、改行正規化、parse/write/parseの値と元位置を確認する。"
    },
    {
      "id": "C03",
      "condition": "§3の全確定Type/Semantics、Copy/Move、Place・一時値・Origin依存・型関係を意味解析と実行表現へ接続する。",
      "verification": "完全TypeとLLVM型を混同しないテスト、全primitive/compound/Semanticsの適合・拒否表、0サイズの責任、Copy導出、temporaryと保存/返却の寿命、再Bind/再Parse後の非保持を確認する。"
    },
    {
      "id": "C04",
      "condition": "§4の固定配列・length引数・Index/Range/ResolvedRange・Slice・反復・動的コレクションについて本文で確定した操作/借用/失敗/計算量を実装する。",
      "verification": "A.12/A.13の全境界を網羅する。添字型、静的Move Path、RHS-first代入、0長/0サイズ、負数/^0/^1、短いreceiverへの再適用、重複/不在、capacity内無確保、削除churn、reserve/shrink/clear、順序・償却操作数を確認する。未定義の公開APIは発明しない。"
    },
    {
      "id": "C05",
      "condition": "§5のptr、unsafe、逆参照、型付きアクセス、算術、比較、変換、外部memory契約を実装する。",
      "verification": "A.5と§21.5.3の対応を型/IR/COFFで確認する。安全コードの拒否、幅・alignment・stride・0サイズを検証し、LLVM poisonとUnsafe契約違反を区別する。契約違反の実行結果を言語保証として期待しない。"
    },
    {
      "id": "C06",
      "condition": "§6のcontainer/fragment、struct・継承、constructor/deinit、enum、binding、確定Attributeを実装する。",
      "verification": "文脈・宣言順・fragment順に依存しないBinding、継承の制限、constructor選択/初期化、部分構築の逆順cleanup、Case identity、0サイズdestructor、未使用宣言も含む不正宣言拒否を確認する。延期されたvirtual/overrideを有効化しない。"
    },
    {
      "id": "C07",
      "condition": "§7の通常関数・既定/名前付き引数・receiver・unsafe・関数式/capture・関数参照を実装する。",
      "verification": "引数のsource順とparameter順、default環境/Loan、途中転送、全Callable receiver、captureの取得/破棄順、返却依存、直接/間接呼出の同じ結果と責任を確認する。"
    },
    {
      "id": "C08",
      "condition": "§8のgeneric slots・Constraints・静的Contract・Callable・証明・明示完全specialization・generic effects/body checkingを実装する。",
      "verification": "A.11/A.12の例と境界を網羅し、Proven/Refuted/Unknown/Error、関連型・条件付きconformance、全置換に対する定義検証、未使用specialization・集合変更・予算0での明示選択を確認する。延期されたruntime Contract View等は不許可のままとする。"
    },
    {
      "id": "C09",
      "condition": "§9の名前・署名・アクセス・継承lookupを完全なsource/Container/role文脈で実装する。",
      "verification": "merged/private/protected/base・Type/Value/Origin/Label・alias・依存module・公開APIのアクセス領域・lookup停止を検証する。失敗時のbase/別overloadへのfallbackや古い文脈の再利用がない。"
    },
    {
      "id": "C10",
      "condition": "§10の適用可能性、順位、literal/generic/Origin推論、argument adaptation、Callable互換を実装する。",
      "verification": "候補と引数の列挙順を変え、候補ごとのrollback、曖昧性、期待型/外部引数名、principal Origin、temporaryの暗黙借用、usage違反後の再選択禁止を確認する。"
    },
    {
      "id": "C11",
      "condition": "§11のstored/computed/required Property、標準操作・custom accessor・権限・初期化・witnessを実行まで接続する。",
      "verification": "A.4のget/set/init、private-set Move、getter temporary、reference result、static effects、generic Copy/conditional Move、Contractの値/共有slot witness、self-assignment・cleanup順を検証する。"
    },
    {
      "id": "C12",
      "condition": "§12の式・評価順・literal構築・member/index/call・receiver保証を実装する。",
      "verification": "副作用ログで一度だけの評価と取得順を確認する。Tuple/配列/dictionaryの途中構築、返却値snapshot、ObjectCompatible等の単一public status、base投影、間接/別moduleへの効果伝播を検証する。"
    },
    {
      "id": "C13",
      "condition": "§13の確定演算・比較・論理・型適応・object/Weak操作・runtime is・代入を実装する。",
      "verification": "全許可Type組合せの受理/拒否、境界/NaN/±0/最小値/巨大値/shift・conversion、短絡、RHS-firstとtarget-first、Copy/Move/借用、view identityを確認する。初期profileで禁止された128bit除算や未導入cast/APIを完了目的で追加しない。"
    },
    {
      "id": "C14",
      "condition": "§14の全Body形・if/do/loop/while/for/require/match・transfer・refinement・到達不能検査を実装する。",
      "verification": "A.9/A.10/A.15を網羅する。構造上の経路と生成経路、guard falseの状態、covered armの診断、Never、ラベル/関数/defer境界、phi到着と値配送、refinement失効をDebugとO0/O2で確認する。"
    },
    {
      "id": "C15",
      "condition": "§15の初期化・部分Move・Origin順序/省略・ref/uniq/reborrow・効果・保存/escapeの全確定検証を実装する。",
      "verification": "A.3/A.8/A.12/A.13の直接・再帰・間接・generic・別moduleの競合/非競合を検証する。MovePath、初期化履歴、guardとcleanup、返却Loan anchors、static mutation、参照slotとreferent、抽象Originと本体外契約の区別を確認する。"
    },
    {
      "id": "C16",
      "condition": "§16のscope exit・defer・destructionを全対応Type/構築状態/転送へ適用する。",
      "verification": "逆取得順・逆parameter順・fields/base順・Move済み部分skip、非停止defer、自身exit、二重破棄/漏れ、通常転送の保留結果とAbort非unwindをIR/破棄監査/時間制限付き実行で確認する。"
    },
    {
      "id": "C17",
      "condition": "§17のOption/Result・Abort・warningと必須定数評価/通常実行時検査の区別を実装する。",
      "verification": "正常/None/Err・must-use/Unit/effect-free警告、constant expressionのruntime Abort、失敗地点の元位置/コード、operand失敗時の後続停止、Abort時にcleanupしないことを確認する。"
    },
    {
      "id": "C18",
      "condition": "§18のsource-first module identity、依存設定/解決、lock、入力記録、source package、local pack/publish/store、意味再利用、製品/テスト所属を実装する。",
      "verification": "A.16に従いローカルの隔離fixtureで多経路/循環/同release内容衝突、product/test lock両区分、原子的更新/中断、破損archive/cache、pin/collect、観測source情報、cache有無/列挙順の同値性を確認する。"
    },
    {
      "id": "C19",
      "condition": "§19の即時directive検証と選択を、生成・依存module・target環境でも一貫して実装する。",
      "verification": "A.2に従い未知/重複/非NFC Condition、未選択switch arm、false if除外、scope統合とdefer、genericでの再選択禁止、eager/lazy/cache時の診断同値を確認する。"
    },
    {
      "id": "C20",
      "condition": "§20のCompilation/target/settings/version・Mods・native要求/供給・CLI/成果物有効性の確定契約を実装する。",
      "verification": "Mod依存DAG/例外/出力除去/provenance、変更入力・native member閉包/directive・version/hash、emit/build/run、fail時の旧成功無効化を確認する。具体APIが未規定の箇所は言語を拡張せず内部host契約として文書化できる範囲だけ選ぶ。"
    },
    {
      "id": "C21",
      "condition": "§21のTypeLayout・metadata・object/Weak/borrow/Closure表現・generic共有生成/予算/frame・checked lowering・LLVM Windows profileを実装する。",
      "verification": "A.14の全表項目をType/IR/COFF/unwind/依存symbol/nativeで分けて検証する。固定Typeとlayout/context key、Entry ABI、scratch非escape、実際の供給symbol、予算/最適化によらない受理と意味、weak orderingの証明を確認する。"
    },
    {
      "id": "C22",
      "condition": "§22の必要Core、Application/Library startup/static初期化/終了、確定FFI、Windows runtimeを実装する。",
      "verification": "必要Core identity全件、main/暗黙本体/Library、static順と失敗、C ABI roundtrip、UTF-8/NUL出力、allocation/free失敗、強/弱参照とHeap stringのcleanupを確認する。Helloは製品プロセス自身が14 bytes・空stderr・exit 0を返す。"
    },
    {
      "id": "CA",
      "condition": "付録A.1–A.16の必須情報保持・検証ケースを全て実装と証拠へ対応付ける。",
      "verification": "T01の規則台帳を用い、各A節の表行/列挙境界ごとに正負テストまたは仕様が要求する静的証明/レビューを追跡する。parserのみ、LLVM verifierのみ、生成IRへのfixture注入のみで本体実装を完了としない。"
    },
    {
      "id": "C99",
      "condition": "C01–C22とCAを全て満たし、確定事項の未実装・未検証・未解決義務を残さず、実装選択とSTATUSを整合させる。",
      "verification": "最終snapshotに対してDebug/Release buildとmanaged全件、全対象native O0/O2、runtime/backend/CLI/Library・cache無効化・資源検証を再確認する。必須ケース欠落/0件成功/未承認skip/失敗0でない結果は不合格。許可された実装制限は根拠節と明示診断を照合し、単なるUnsupportedで必須機能を免除しない。"
    }
  ],
  "tasks": [
    {
      "id": "T01",
      "description": "全仕様の規則台帳と既存実装への対応を確定する（§1–22、A.1–16）。",
      "required": true,
      "depends_on": [],
      "criterion_ids": [
        "C01",
        "CA",
        "C99"
      ],
      "acceptance": "全規則に安定ID・元節・分類・実装箇所・必須ケース・担当taskがある。本文の決定事項と参照先のみ/延期を区別し、残る必須規則を既存taskの内部細分化へ割り当てる。 本文内で必須と延期が衝突する箇所は未解決として明示し、どちらかを黙って削除しない。",
      "verification": "SPEC.mdからspec/の章・付録を辿る。ただしdoc/draftリンクは開かない。STATUSと実装を照合し、台帳を将来の実装時にxUnitTest/Conformance/等の追跡入力へ保存する。全必須規則のcriterion/milestoneへの対応を機械検査する。 例として§22.1のstring concatenation必須操作記述と§13.3の延期記述を照合する。owning節と優先規則だけで確定できなければ、この機能の完成範囲をユーザー確認へ戻す。"
    },
    {
      "id": "T02",
      "description": "既存検証基盤と再現可能な基準を整える。",
      "required": true,
      "depends_on": [
        "T01"
      ],
      "criterion_ids": [
        "C01",
        "C21",
        "C99"
      ],
      "acceptance": ".NET10/Microsoft.Testing.Platformと固定LLVM/backendを識別でき、managed・IR・nativeの各検証に件数/期待値/時間上限/失敗伝播がある。既存変更を保持した基準がある。",
      "verification": "実装開始後に必要ならdotnet restore Kimigayo.slnxを行い、Debug/Release build→testを順に実行する。backend scriptsの前提と出力を点検し、0 fixture成功・残留child・古い成功報告を拒否する。環境不足は未実装の代わりに成功扱いしない。"
    },
    {
      "id": "T03",
      "description": "字句・literal・source identity・構文と保存/復元の不足を補う（§2、A.1/A.6）。",
      "required": true,
      "depends_on": [
        "T02"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "CA"
      ],
      "acceptance": "全確定構文のKotoとimmutable source/provenanceが保持され、literal fittingは丸め/符号の規則を満たす。再Parseで古い文脈を使わない。",
      "verification": "既存FrontEndSyntax/ParserRegression/serializationとliteralテストを拡張し、境界値・不正文法・roundtrip・文脈保持を確認する。"
    },
    {
      "id": "T04",
      "description": "target準備・設定とdirective選択を完成する（§19、§20.1–6、A.2）。",
      "required": true,
      "depends_on": [
        "T03"
      ],
      "criterion_ids": [
        "C19",
        "C20",
        "CA"
      ],
      "acceptance": "target/設定の衝突とversionを検査し、除外境界・scope・生成文書の環境が一意で、後段で条件を再選択しない。",
      "verification": "DirectiveConditionValidation/CompilationSpecificationを拡張し、target差・未選択arm・false if・読み込み順・cache差で受理/必須診断が一致する。"
    },
    {
      "id": "T05",
      "description": "宣言統合・名前・アクセスとoverload基盤を完成する（§6.1/6.4–5、§9–10、A.3）。",
      "required": true,
      "depends_on": [
        "T04"
      ],
      "criterion_ids": [
        "C06",
        "C09",
        "C10",
        "CA"
      ],
      "acceptance": "fragmentごとのsource context、role別lookup、可視性、候補分離と選択を保持し、不正使用後の再選択がない。",
      "verification": "Binding/InheritedReceiver/Overload関連テストでfragment/candidate/引数順を入れ替え、同じtargetまたは同じ必須診断を確認する。"
    },
    {
      "id": "T06",
      "description": "完全Type・Origin schema・Type関係・Copy/Owned導出を完成する（§3、§8.1、§15.2–5、A.8/A.12）。",
      "required": true,
      "depends_on": [
        "T05"
      ],
      "criterion_ids": [
        "C03",
        "C08",
        "C10",
        "C15",
        "CA"
      ],
      "acceptance": "Type slots/length/Origin bindersを区別し、条件付きCopyと未確定義務、variance/Owned依存を保持できる。",
      "verification": "TypeBinding/Origins/Constraint系を拡張し、recursive payload・nested Semantics・unused slots・Occurs/Origin bound/再Bindを検証する。"
    },
    {
      "id": "T07",
      "description": "静的Contract・関連型・条件付きconformance・証明を完成する（§8.2–7、A.11）。",
      "required": true,
      "depends_on": [
        "T06"
      ],
      "criterion_ids": [
        "C08",
        "C09",
        "C10",
        "CA"
      ],
      "acceptance": "lookup後にrequirementsを対応し、Unknown/cyclic evidenceを成功にせず、継承witnessの元identityとreceiver mappingを保持する。",
      "verification": "Contract/ConditionalConformance/InheritedReceiverのdiamonds、private実装、Self制限、強いConstraints禁止、load順とProven撤回を検証する。"
    },
    {
      "id": "T08",
      "description": "全許可scalar演算・変換・比較と失敗診断を完成する（§3.1、§12.3、§13.1–5、§17）。",
      "required": true,
      "depends_on": [
        "T06"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "C12",
        "C13",
        "C17",
        "C21"
      ],
      "acceptance": "残るfloat変換等も含め許可されたType組合せが実行でき、定数だけの普通の式でも失敗種別/位置を維持する。禁止演算は言語上の理由で拒否する。",
      "verification": "Scalar/Integer/WideInteger/Float/Char/Conversion各fixtureを境界行列で拡張し、最適化前IR、O0/O2、stderr/code、短絡と入れ子Abortを確認する。"
    },
    {
      "id": "T09",
      "description": "制御フロー・transfer・deferとrefinementの全Type共通基盤を完成する（§14/16/17、A.7/A.9/A.15）。",
      "required": true,
      "depends_on": [
        "T06"
      ],
      "criterion_ids": [
        "C07",
        "C12",
        "C14",
        "C16",
        "C17",
        "CA"
      ],
      "acceptance": "検査専用継続と実行辺を分け、転送値確保→cleanup→配送、条件cleanup→分岐を保ち、未到達部分にも必要な診断が出る。",
      "verification": "ControlFlow/CurrentControlFlow/Deferred/Unreachable/Resultsテストを拡張し、Debugの完了assert、phi前駆辺、非停止cleanupとtimeout/kill、refinement失効を確認する。"
    },
    {
      "id": "T10",
      "description": "Origin推論・省略・返却/保存依存を一般化する（§15.2–5/15.8、A.8/A.12）。",
      "required": true,
      "depends_on": [
        "T06",
        "T09"
      ],
      "criterion_ids": [
        "C03",
        "C07",
        "C10",
        "C15",
        "CA"
      ],
      "acceptance": "per-call Originと固定依存、入力のmeet、抽象bound、返却anchorsを本体外契約として扱い、再帰や別moduleでも同じ保存/escapeを判定する。",
      "verification": "local/parameter/static/temporary/field・複数入力/returned borrowを使い、入力順・共有Type intern・再Bindに依存しない正負テストを確認する。"
    },
    {
      "id": "T11",
      "description": "静的Move Path・部分初期化/Move・再初期化と部分cleanupを実装する（§15.1、§16）。",
      "required": true,
      "depends_on": [
        "T06",
        "T09"
      ],
      "criterion_ids": [
        "C03",
        "C04",
        "C06",
        "C15",
        "C16"
      ],
      "acceptance": "field/tuple/固定配列のeligible pathとwhole状態が整合し、分岐/loop/deferでMoved部分を読まず、残存部分だけを正しい順で破棄する。",
      "verification": "Ownershipのwhole regressionに加え、literal-only index、0長/0サイズ、branch join、部分値の置換、construct中断、self-assignmentを状態/IR/破棄監査で確認する。"
    },
    {
      "id": "T12",
      "description": "一般Loan・ref/uniq/reborrow・効果固定点とreceiver public保証を完成する（§12.4、§15.6–9、A.3/A.8）。",
      "required": true,
      "depends_on": [
        "T07",
        "T10",
        "T11"
      ],
      "criterion_ids": [
        "C03",
        "C07",
        "C08",
        "C11",
        "C12",
        "C15",
        "CA"
      ],
      "acceptance": "storageとreferentの重なり、shared/unique・静的効果・返却Loanを統一し、direct/recursive/indirect/genericの保留義務を選択を変えずに解消する。",
      "verification": "loan conflict表、defaults/cleanup/static/capture再入、Whole/Base/Part変更、NotProvenと通常エラー、conformance循環・効果summary変更の失効を検証する。"
    },
    {
      "id": "T13",
      "description": "struct/enum/Tuple/配列のlayoutと共通値転送ABIを完成する（§6.2–3、§21.1/21.4）。",
      "required": true,
      "depends_on": [
        "T11"
      ],
      "criterion_ids": [
        "C03",
        "C04",
        "C06",
        "C21",
        "CA"
      ],
      "acceptance": "alignment順と論理順、base/tag、全置換offset、0サイズ責任、Copy元分離、slot結果の通常復帰を全対応集成型で保持する。",
      "verification": "Aggregate/Function/ResultテストとC layout照合を拡張し、recursive layout拒否・overflow・padding/offsetof・多段転送/phi・不正物理計画の拒否を確認する。"
    },
    {
      "id": "T14",
      "description": "constructor/deinit・継承層・Property実行を接続する（§6.2、§11、A.4/A.7）。",
      "required": true,
      "depends_on": [
        "T12",
        "T13"
      ],
      "criterion_ids": [
        "C06",
        "C09",
        "C11",
        "C12",
        "C16",
        "CA"
      ],
      "acceptance": "base/own fieldの初期化履歴とexact destruction receiverを保持し、標準Place操作とcustom getter結果を区別してcall/cleanupへ接続する。",
      "verification": "constructor/accessorのbranchと途中転送、private-set Move、base禁止更新、computed temporary、nonCopy setter、deinit完了/中断をsourceからnativeまで検証する。"
    },
    {
      "id": "T15",
      "description": "一般関数call・receiver・defaultと全結果ABIを接続する（§7.1–5、§10、§21.4）。",
      "required": true,
      "depends_on": [
        "T12",
        "T13"
      ],
      "criterion_ids": [
        "C07",
        "C10",
        "C12",
        "C16",
        "C21"
      ],
      "acceptance": "論理/物理引数、source順default取得、結果のOriginとslot責任が一致し、Unit/Never/borrow/集成型で欠落/二重取得がない。",
      "verification": "named/external引数名、nested call、receiver位置、後続argument transfer、浅い再帰、default Loan、返却cleanupをABI構造とO0/O2で確認する。"
    },
    {
      "id": "T16",
      "description": "enum/Tuple分解・借用Subjectと一般guard/matchを実行へ接続する（§6.3、§14.8–10、A.10）。",
      "required": true,
      "depends_on": [
        "T12",
        "T13",
        "T15"
      ],
      "criterion_ids": [
        "C06",
        "C14",
        "C15",
        "C16",
        "C17",
        "CA"
      ],
      "acceptance": "guard candidateとbody bindingを分け、取得/分解/coverageを決定し、選ばれないarmも検査しながら実行時の到着と責任を正しく絞る。",
      "verification": "Option/Result/入れ子Case/tuple/borrowed Subject・guard中の依存escape・失敗cleanup・covered arm・構造包含warningを正負/IR/nativeで確認する。"
    },
    {
      "id": "T17",
      "description": "owned stringと共通runtime失敗/出力・破棄を全経路へ適用する（§3.1.4、§13.4、§17、§22.4–5）。",
      "required": true,
      "depends_on": [
        "T08",
        "T09"
      ],
      "criterion_ids": [
        "C03",
        "C13",
        "C16",
        "C17",
        "C22",
        "CA"
      ],
      "acceptance": "確定したliteral/補間規則・比較・Move/引数/結果/借用・破棄を扱い、StaticとHeapの責任とAbort非unwindを保持する。concatenationの§22.1と§13.3の不一致はT01で解消し、判断を記録した上で必要な内部項目へ分割する。未決のまま免除または公開API追加をしない。",
      "verification": "String系fixtureとruntime fault adapterでUTF-8/NUL/空文字/heap0長・二重free/漏れ・OOM/I/O/free失敗・比較Loanを確認する。Heap確保源のない段階はruntime単独証拠と製品source証拠を分ける。"
    },
    {
      "id": "T18",
      "description": "raw pointerと初期C FFIを実装する（§5、§21.1/21.5、§22.3、A.5）。",
      "required": true,
      "depends_on": [
        "T08",
        "T13",
        "T15"
      ],
      "criterion_ids": [
        "C05",
        "C06",
        "C21",
        "C22",
        "CA"
      ],
      "acceptance": "ptr固有の演算/unsafe制限とLibraryImport/C ABIを実装し、実供給symbolと型/呼出属性を照合できる。",
      "verification": "整数/pointer/bool/floatのC harness roundtrip、packing16、DLL import形、外部依存違反と実行時契約を確認する。延期されたaggregate passing/export/callback/varargsは追加しない。"
    },
    {
      "id": "T19",
      "description": "generic定義検証・明示specialization・symbolic ownership/effect計画を完成する（§8.8–10、A.12）。",
      "required": true,
      "depends_on": [
        "T07",
        "T12",
        "T14",
        "T15"
      ],
      "criterion_ids": [
        "C04",
        "C08",
        "C10",
        "C15",
        "C21",
        "CA"
      ],
      "acceptance": "実体化時にlookup/overloadをやり直さず、pair/length/Origin、conditional Copy/Move、closed specialization集合とuse義務を保持する。",
      "verification": "未使用body・全置換proof・specialization曖昧/撤回/追加・default/safety/Origin-only重複・recursive定義・ValidLength境界を検証する。"
    },
    {
      "id": "T20",
      "description": "generic共有生成・metadata/context・予算・Entry ABI・固定scratchを実装する（§21.2–4、A.12/A.14）。",
      "required": true,
      "depends_on": [
        "T19"
      ],
      "criterion_ids": [
        "C08",
        "C21",
        "CA"
      ],
      "acceptance": "キーとoperation pairs、共通/特化entry・adapter、exact frame予約、予算0での必須選択、bounded探索/診断が一貫する。runtime未知置換の未規定storageを追加しない。",
      "verification": "予算/列挙順/cache/LTO差、有限/増大型再帰、0capacity/0stride、slot除去・型別alignment/破棄、コード量/予約量とsemantic同値、COFF stackprobe/unwindを検証する。"
    },
    {
      "id": "T21",
      "description": "Closure/capture・Function Item/common function・間接呼出を実装する（§3.2/7.6/15.8/21.2.5）。",
      "required": true,
      "depends_on": [
        "T12",
        "T15",
        "T20"
      ],
      "criterion_ids": [
        "C03",
        "C07",
        "C08",
        "C10",
        "C15",
        "C21",
        "CA"
      ],
      "acceptance": "captureとcall取得、Shared/Exclusive/Consuming、per-call依存、inline/heap/空環境、erasure entriesとcleanupが契約を満たす。",
      "verification": "A.8/A.14のcapture順、nested/unused capture、Copy環境/NonCopy container、返却borrow、保存/escape、部分consuming失敗、direct対indirectを検証する。"
    },
    {
      "id": "T22",
      "description": "object identity・継承view・owner/obj/rc/arc操作とruntime type testを実装する（§3.3/13.5–6/21.2.1–3）。",
      "required": true,
      "depends_on": [
        "T12",
        "T14",
        "T20"
      ],
      "criterion_ids": [
        "C03",
        "C06",
        "C12",
        "C13",
        "C15",
        "C21",
        "C22",
        "CA"
      ],
      "acceptance": "確定したobject/view/receiver投影・metadata選択を実装し、publication前の構築cleanup、payload offset、count移行、最終dynamic cleanupを守る。未導入virtual/runtime Contract dispatchを増設しない。",
      "verification": "identity/alias/view差、base調整、fresh ownership・countoverflow・allocation失敗・最後のrelease/deinit順・resurrection拒否をmanagedモデル/IR/nativeで確認する。"
    },
    {
      "id": "T23",
      "description": "Weak・upgrade・rc/arc atomic orderingを実装して証明する（§3.2.2/13.5.9/21.2.3）。",
      "required": true,
      "depends_on": [
        "T22"
      ],
      "criterion_ids": [
        "C03",
        "C13",
        "C15",
        "C21",
        "C22",
        "CA"
      ],
      "acceptance": "未生成/None/expiredを区別し、weak guard・strong最後のreleaseとupgradeが解放済みpointerを読まない。仕様指定memory orderを満たす。",
      "verification": "全mode/境界count/upgrade失敗/失効をnativeとfault injectionで確認し、競合プロトコルはIRだけに依存せずinterleavingモデルとhappens-beforeレビューを残す。source並行APIの導入とは分ける。"
    },
    {
      "id": "T24",
      "description": "要素の全確定操作・Index/Range/ResolvedRange・Sliceを実装する（§4.1–6、A.13）。",
      "required": true,
      "depends_on": [
        "T11",
        "T12",
        "T13",
        "T15",
        "T20"
      ],
      "criterion_ids": [
        "C04",
        "C12",
        "C13",
        "C15",
        "C21",
        "CA"
      ],
      "acceptance": "Copy読みに加えstatic Move/再初期化、shared result、borrow、書込み/複合更新、範囲viewとmetadata/iteratorが実行できる。",
      "verification": "Element既存回帰とA.13表を拡張し、nested no-copy、RHS-first、root保護/unique開始時点、temporary保存・empty/split whole-array Loan、isize/Index/Range Type分岐とO(1)を確認する。"
    },
    {
      "id": "T25",
      "description": "Array/Dictionaryの確定mutation・capacity/失敗/効果を実装する（§4.7）。",
      "required": true,
      "depends_on": [
        "T16",
        "T20",
        "T24"
      ],
      "criterion_ids": [
        "C04",
        "C08",
        "C12",
        "C15",
        "C16",
        "C21"
      ],
      "acceptance": "指定されたsuccess/absence/duplicate、取得/破棄・キーidentityと挿入順、capacity保証・償却計算量・非減算のOrigin依存を守る。内部storageは仕様境界内で選んで記録する。",
      "verification": "reserve/insert/remove/clear/shrink等の本文記載API、最大長/overflow/OOM、deletion churn、再入効果、hash/equality/destructor、capacity内allocation countとoperation countを検証する。未確定の追加APIは対象にしない。"
    },
    {
      "id": "T26",
      "description": "必須Core宣言・Option/Result・Iterableと定義済み組込みを閉じる（§22.1、§4/8/17）。",
      "required": true,
      "depends_on": [
        "T16",
        "T20",
        "T21",
        "T23",
        "T25"
      ],
      "criterion_ids": [
        "C04",
        "C08",
        "C14",
        "C17",
        "C22"
      ],
      "acceptance": "本文catalogの全必須identity/signature/constraintsが提供され、名前の同一性だけの偽装を拒否する。for/Option/Resultと全定義済み公開操作がsourceから利用できる。",
      "verification": "CoreCatalog/Option/Result/for等を全catalogへ広げ、紛らわしい同名型・欠落/不正signature、有限iterator枯渇、must-use warning、generic呼出を確認する。"
    },
    {
      "id": "T27",
      "description": "Mod依存・暫定Binding・生成統合・失敗/再生成を接続する（§20.7、A.1–3）。",
      "required": true,
      "depends_on": [
        "T04",
        "T05",
        "T19"
      ],
      "criterion_ids": [
        "C06",
        "C09",
        "C19",
        "C20",
        "CA"
      ],
      "acceptance": "Requires/RequiresAfterの順と一度実行、append境界、immutable source/provenance、final Binding、入力変更時の旧出力除去を守る。host APIは確定契約の内部実装として定義できる範囲で具体化する。",
      "verification": "隔離C# Mod fixturesでcycle、generated target、暫定未解決と確定エラー、失敗descendant skip、追加入力hash・location観測・決定性・診断view/saveを確認する。"
    },
    {
      "id": "T28",
      "description": "module identity・依存graph・設定/lock・固定入力snapshotを実装する（§18.1–5/18.8、A.16）。",
      "required": true,
      "depends_on": [
        "T04",
        "T05",
        "T06"
      ],
      "criterion_ids": [
        "C09",
        "C18",
        "C19",
        "C20",
        "CA"
      ],
      "acceptance": "Project/Packageのexact version、alias/direct/transitive境界、両partition lock、入力bytes・logical source名とenvironmentが確定し、depth依存のhost再帰に頼らない。",
      "verification": "多経路/循環/同release内容衝突・hidden transitive・missing/stale/empty lock・移動/編集・同時restoreをlocal temporary graphで検証する。"
    },
    {
      "id": "T29",
      "description": "source package・local pack/publish・content storeと整合性/atomic更新を実装する（§18.6）。",
      "required": true,
      "depends_on": [
        "T28",
        "T27"
      ],
      "criterion_ids": [
        "C18",
        "C20",
        "CA"
      ],
      "acceptance": "本文指定format、全closure/environment検証、cache/store verify、pin/collection、destination単位の衝突と中断時の原子性を満たす。実利用のregistryへ公開しない。",
      "verification": "bin/conformance配下のローカルstoreで同version再試行、破損index/entry、圧縮差/Unicode衝突、Mod非対応packの拒否、publication中断/競合を再現する。"
    },
    {
      "id": "T30",
      "description": "意味記録・artifact互換・無効化とbounded再利用を実装する（§18.7、§21.3.4、A.3/A.16）。",
      "required": true,
      "depends_on": [
        "T19",
        "T20",
        "T21",
        "T22",
        "T28"
      ],
      "criterion_ids": [
        "C08",
        "C09",
        "C15",
        "C18",
        "C20",
        "C21",
        "CA"
      ],
      "acceptance": "source correspondence、absence/効果/証明・private依存、mode/target/contextを保持し、cache miss/hit/revalidationで選択を変えない。旧KotoやOriginから構文木を保持しない。",
      "verification": "no-cache対cache・source観測有無・Proven撤回・specialization追加・recursive SCC・部分破損/切断・many-path sparse Libraryを検証する。bytes/hashpass/worklist/loaded bodies/memoryを計測する。"
    },
    {
      "id": "T31",
      "description": "native member要求/供給・CLI・artifact公開とLibrary object生成を完成する（§20.8、§21.5）。",
      "required": true,
      "depends_on": [
        "T18",
        "T20",
        "T28",
        "T30"
      ],
      "criterion_ids": [
        "C18",
        "C20",
        "C21",
        "C22",
        "CA"
      ],
      "acceptance": "実COFF/import member閉包と型/設定/版/hashを検証し、Applicationは実行可能、Libraryは指定された未最適化body/linkageを保持したobjectまで生成できる。失敗/取消しで旧成功を再利用しない。",
      "verification": "既存test-cli/test-toolchain/test-kernel32/test-manual-build/test-artifact-pathsを拡張し、mixed archive/曖昧なrequired symbol/unused重複/directive/短長import、パス/変更/取消し/timeoutを確認する。"
    },
    {
      "id": "T32",
      "description": "startup・static初期化/終了・Application/LibraryとCore出力を統合する（§22.2/22.4–5）。",
      "required": true,
      "depends_on": [
        "T14",
        "T17",
        "T26",
        "T27",
        "T28",
        "T31"
      ],
      "criterion_ids": [
        "C06",
        "C07",
        "C16",
        "C17",
        "C20",
        "C22",
        "CA"
      ],
      "acceptance": "暗黙本体/明示main、generated declaration-only、全staticの順序と終了責任、Library制約、core/runtimeへの接続が本文内の確定規則に従う。",
      "verification": "A.14 Startup表、複数source/generated/dependency、static-only/Unit/invalid-main・initializer/deinit failureを確認し、Helloと複数の統合例をCLI build/runする。"
    },
    {
      "id": "T33",
      "description": "製品/テスト入力partition・共通生成・予算隔離を実装する（§18.8/21.3.7と本文内で確定した関連規則）。",
      "required": true,
      "depends_on": [
        "T20",
        "T28",
        "T29",
        "T30",
        "T31",
        "T32"
      ],
      "criterion_ids": [
        "C18",
        "C20",
        "C21",
        "C22",
        "CA"
      ],
      "acceptance": "製品のsharing/frame/budget/選択が無関係なtest入力で変わらず、所属・Provider変更・初期化cleanup閉包の確定要件を検証できる。draftのみのtest syntax/Composition詳細を追加しない。",
      "verification": "product/test partition差、testだけの依存失敗、新generic置換、Provider/設定変更を隔離fixtureで比較する。本文だけでは必須の意味が決まらない箇所は影響する規則IDを示してNeedsInputとし、恣意的に免除しない。"
    },
    {
      "id": "T34",
      "description": "必須境界全体・資源上限・性能と組合せの検証を仕上げる（A.1–16）。",
      "required": true,
      "depends_on": [
        "T03",
        "T04",
        "T05",
        "T06",
        "T07",
        "T08",
        "T09",
        "T10",
        "T11",
        "T12",
        "T13",
        "T14",
        "T15",
        "T16",
        "T17",
        "T18",
        "T19",
        "T20",
        "T21",
        "T22",
        "T23",
        "T24",
        "T25",
        "T26",
        "T27",
        "T28",
        "T29",
        "T30",
        "T31",
        "T32",
        "T33"
      ],
      "criterion_ids": [
        "C01",
        "CA",
        "C99"
      ],
      "acceptance": "全必須規則の正負/構造/実行/証明coverageが揃い、未実装拒否で有効プログラムを落とす抜けがない。性能とコード共有が意味・順序・診断を変えない。",
      "verification": "本文とAの台帳を逆引き監査し、Debug/Release全件と全native O0/O2、runtime/backend/Library、weak ordering、再Parse保持/メモリ、collection償却、generic予算を検証する。対象benchmarkは同一入力・cold/warm・各stageを分ける。"
    },
    {
      "id": "T35",
      "description": "仕様との最終照合・実装選択/STATUS整理・Completion Auditを行う。",
      "required": true,
      "depends_on": [
        "T34"
      ],
      "criterion_ids": [
        "C01",
        "C02",
        "C03",
        "C04",
        "C05",
        "C06",
        "C07",
        "C08",
        "C09",
        "C10",
        "C11",
        "C12",
        "C13",
        "C14",
        "C15",
        "C16",
        "C17",
        "C18",
        "C19",
        "C20",
        "C21",
        "C22",
        "CA",
        "C99"
      ],
      "acceptance": "全criterionとmilestoneの到達根拠が現在入力に結び付き、未完了必須項目0。仕様の禁止/延期と実装制限の混同0。STATUS/例/CLI説明と実装が一致する。",
      "verification": "修正された最終snapshotに対し失効した証拠を再取得し、台帳・source/IR/COFF/native・必須証明を照合する。specの必須条件削除による完了化、skip/件数0、過去ログ流用を排除する。未決事項は質問へ戻しCompleteにしない。"
    }
  ],
  "milestones": [
    {
      "id": "M01",
      "description": "対象仕様と検証入口の確定",
      "task_ids": [
        "T01",
        "T02"
      ],
      "acceptance": "全必須規則がcriterion/taskへ割当済みで、実装開始時の環境・基準検証・件数/失敗/timeoutの判定を再現できる。環境/仕様の不足が成功扱いされていない。"
    },
    {
      "id": "M02",
      "description": "source・宣言・型・静的証明の基礎",
      "task_ids": [
        "T03",
        "T04",
        "T05",
        "T06",
        "T07"
      ],
      "acceptance": "字句/構文/source identity・directive・lookup/overload・完全Type・Constraint/Contractの必須ケースが通り、順序変更と再解析で結果を変えない。"
    },
    {
      "id": "M03",
      "description": "scalar・制御フロー・string/runtime",
      "task_ids": [
        "T08",
        "T09",
        "T17"
      ],
      "acceptance": "確定scalar演算/変換と転送/cleanup・string出力/比較がO0/O2で一致し、失敗診断、非停止、snapshotと責任の監査が通る。"
    },
    {
      "id": "M04",
      "description": "一般所有権・Origin・Loan",
      "task_ids": [
        "T10",
        "T11",
        "T12"
      ],
      "acceptance": "返却/保存依存、部分Move/再初期化、ref/uniq/reborrowと効果固定点が正負ケースを正しく判定し、未解決保証を公開しない。"
    },
    {
      "id": "M05",
      "description": "集成型・構築/Property・call・分解match",
      "task_ids": [
        "T13",
        "T14",
        "T15",
        "T16"
      ],
      "acceptance": "struct/enum/Tuple/配列の値責任がconstructor/accessor/call/match/cleanupを跨いで正しく実行され、A.4/A.7/A.10の必須境界が通る。"
    },
    {
      "id": "M06",
      "description": "FFI・generic生成・Closure/間接call",
      "task_ids": [
        "T18",
        "T19",
        "T20",
        "T21"
      ],
      "acceptance": "C ABIとptrの確定契約、generic定義/共有/明示特化/frame/予算、Closure/capture/erasureが実証され、direct/indirectや予算差で意味を変えない。"
    },
    {
      "id": "M07",
      "description": "object・強/弱参照runtime",
      "task_ids": [
        "T22",
        "T23"
      ],
      "acceptance": "object/view identity・payload配置・rc/arc/Weak・最終dynamic cleanupがnativeで検証され、upgrade/release競合とmemory orderingの静的根拠がある。"
    },
    {
      "id": "M08",
      "description": "sequence・dynamic collections・必須Core",
      "task_ids": [
        "T24",
        "T25",
        "T26"
      ],
      "acceptance": "Index/Range/Slice/反復/全確定mutation・Core catalogを実行でき、capacity/計算量・Loan/escape・失敗/cleanupの必須ケースが通る。"
    },
    {
      "id": "M09",
      "description": "生成source・依存・package/store・意味再利用",
      "task_ids": [
        "T27",
        "T28",
        "T29",
        "T30"
      ],
      "acceptance": "Mod再生成とsource provenance、exact dependency/lock、local pack/publish/store、semantic reuseが破損/中断/変更時も契約を守り、cache有無で受理を変えない。"
    },
    {
      "id": "M10",
      "description": "製品CLI・startup/Library・test partition統合",
      "task_ids": [
        "T31",
        "T32",
        "T33"
      ],
      "acceptance": "ApplicationのCLI build/runとLibrary object、static lifecycle、native provider/入力検証、product/test生成隔離の確定要求を現物で確認できる。"
    },
    {
      "id": "M11",
      "description": "全決定事項の完成監査",
      "task_ids": [
        "T34",
        "T35"
      ],
      "acceptance": "C01–C22/CA/C99を全て満たし、全規則のcoverageと現入力の証拠が揃い、未完了必須項目/未承認skip/必須テスト0件成功がない。"
    }
  ]
}
```
<!-- autoframe:end -->
