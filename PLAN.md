# Kimigayoコンパイラー完成計画

<!-- autoframe:begin -->
```json
{
  "schema_version": 1,
  "project_id": "kimigayo-compiler-completion",
  "objective": "Kimigayoコンパイラーについて、PLAN作成時（2026-09-14）のSPEC.md内で具体的に確定している決定事項をすべて実装し、規定された静的検査・コード生成・実行動作・検証義務まで完成させる。",
  "scope": [
    "仕様対象はSPEC.mdの第1〜22章、規範的なAppendix A.1〜A.15、本文に対応するAppendix Fの構文。禁止・拒否・必須警告・資源制限・性能契約も含む。Appendix Dと各節の設計境界で未導入部分を区別し、Appendix Bの参考アルゴリズムを必須の実装方式とはしない。",
    "STATUS.mdを既存実装と残る制限の案内に使い、現ソース・テストと照合する。字句/構文、Binding、制御フロー/所有権、LLVM、通常native実行の各段階を区別し、STATUSの実装済み表示も再確認する。",
    "SPEC.md内で具体的に確定している規則だけを必須とし、docにしかない詳細は対象外とする。Composition Root・言語テスト機能の冒頭要約だけから、Entry/Providerの構文や#Test・$expect・$require・kimi testの未統合の契約を補作しない。$abortなど本文で具体化済みの規則、具体化済みの依存再検証規則は対象に残す。",
    "部分仕様の機能は機能全体を除外しない。module/source artifact、Mod、Exchange等は確定した意味・不変条件を内部APIを含めて実装・検証し、未確定の公開API/交換形式/構文の策定は含めない。",
    "Kimi/のコンパイラー・Core・実行時処理・Project/Solution・CLIと、backend/windows-x64/、対応するmanaged/nativeテスト・サンプル・性能検証。native生成の必須対象はSPECのwindows-x64-v1。Libraryは規定どおりIR/オブジェクト生成まで検証する。"
  ],
  "non_goals": [
    "doc/**の読取・検索・リンク追跡・変更、およびdocだけに記載された仕様の実装。Design/**、旧実装計画や監査記録を追加の仕様正本として扱うこと。",
    "SPECが未導入・延期と明記した言語拡張の設計。例: runtime Contract Viewの未確定構文、virtual/override、再export、言語並行実行、追加OS/CPU profile、string連結/+=の未確定所有権規則、未確定の公開Mod/Exchange API。",
    "NativeAOTのpublish・テスト、ツールの自動ダウンロード/システムインストール、外部サービスへの公開・配布。",
    "SPECの決定事項に必要のないKimiCode/VS Code機能拡張、CI/配布基盤の刷新、汎用標準ライブラリ追加。",
    "autoframe/・automation/の実装や変更、runner起動、定期実行の設定。このPLANの作成依頼ではPLAN.mdだけを保存し、製品修正・ビルド・テスト・自動実行を開始しない。"
  ],
  "constraints": [
    "実装工程と検証コマンドは、ユーザーが別途実装を開始する際の定義である。PLAN作成時の確認は資料・コード・手順の読取とPLANの形式検査に限る。",
    "適用されるAGENTS.mdと既存の未コミット変更を保持する。プロジェクト外（隣接Tinyhandを含む）を変更せず、reset/clean/stash/commit/pushを自動実行しない。",
    "SPECの確定事項を削除・緩和して完成扱いにしない。仕様本文の整理・実装上の選択の説明は可能だが、Design Noteの変更禁止文を保持する。仕様矛盾や目的・必須条件・変更範囲を左右する不足だけ、具体的な判断案を示してNeedsInputとする。",
    "doc参照禁止はSPEC等にあるdoc優先指定より優先する。本文内の具体的規則と明示的な設計境界を照合する。未確定部分の除外理由は節単位で記録し、単なる未実装・実装困難を対象外にしない。",
    "既存のKoto、Binding/型・候補計画、CFG/Loan/cleanup計画、Emission/ABI表の構成を起点に詳細化する。汎用化が必要なら変更できるが、Emitterの拒否検査を消すだけで機能対応としない。Unknown・未解決義務・古い解析結果を成功として公開しない。",
    "メモリ割り当てを可能な範囲で最小化し、表・scratch・容量を再利用する。SPECに規定された計算量・無割り当て条件と意味を優先し、測定のない全経路0 allocationや速度改善を主張しない。",
    "テストの削除/無効化/期待値の弱体化で通さない。既存の未対応拒否テストは、その機能が確定仕様どおりに実装された時に対応する成功・不正入力の回帰テストへ改訂する。危険なunsafe違反に規定外の実行結果を要求しない。",
    "DebugとReleaseのmanaged suite、fixture生成、LLVM/native検証は共有出力を使うため直列実行する。--no-buildは同じ構成の現行ソースのbuild成功後、--no-restoreは必要な依存の復元後だけ使う。起動失敗、0件、skip、未実行、古いfixtureを成功に数えない。",
    "通常nativeとNativeAOTを区別する。LLVM/backend検証はprofile.jsonの固定版・ABI・実hashを使う。AllowUnpinnedToolchainでの探索結果は完成証拠にしない。供給物を変更する場合は規定の採用検証と整合したcatalog更新を行う。",
    "生成物の退避/再生成では絶対パスがこのrepoの生成領域内に収まることを事前確認する。ソース、設定、テスト入力、参照資料をgenerated_scopeで隠さない。検証入力が変われば必要な証拠を無効化する。",
    "PLANにはユーザー定義だけを保持する。内部計画・要件対応表・進捗・指摘・証拠はautoframeの試行/内部記録へ保存し、製品の実装状況と再現手順はSTATUS.mdへ反映する。WorkerはPLANを書き換えない。"
  ],
  "work_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "examples/**",
    "Benchmark/**",
    "Playground/**",
    "Kimigayo.slnx",
    "Directory.Build.props",
    "global.json",
    "SPEC.md",
    "STATUS.md",
    "README.md"
  ],
  "input_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "examples/**",
    "Benchmark/**",
    "Playground/**",
    "Kimigayo.slnx",
    "Directory.Build.props",
    "global.json",
    ".editorconfig",
    "stylecop.json",
    ".gitignore",
    "AGENTS.md",
    "SPEC.md",
    "STATUS.md",
    "README.md",
    "toolchain/*.exe",
    "toolchain/*.dll"
  ],
  "generated_scope": [
    "Kimi/bin/**",
    "Kimi/obj/**",
    "Kimi/Generated/**",
    "xUnitTest/bin/**",
    "xUnitTest/obj/**",
    "xUnitTest/TestResults/**",
    "Benchmark/bin/**",
    "Benchmark/obj/**",
    "Playground/bin/**",
    "Playground/obj/**",
    "bin/**",
    "TestResults/**",
    "BenchmarkDotNet.Artifacts/**",
    "backend/windows-x64/bin/**",
    "examples/**/bin/**",
    "examples/**/obj/**",
    "toolchain/windows_x64/**"
  ],
  "environment_checks": [
    "dotnet --info",
    "dotnet --list-sdks",
    "Get-Content -LiteralPath global.json -Raw",
    "Get-Content -LiteralPath backend/windows-x64/profile.json -Raw",
    "$ErrorActionPreference = 'Stop'; foreach ($name in @('clang','opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-objdump','llvm-lib','llvm-dlltool')) { Get-FileHash -LiteralPath (Join-Path 'toolchain' ($name + '.exe')) -Algorithm SHA256 }",
    "$ErrorActionPreference = 'Stop'; & ./toolchain/clang.exe --version; if ($LASTEXITCODE -ne 0) { throw 'LLVM version identification failed' }",
    "$ErrorActionPreference = 'Stop'; Get-FileHash -LiteralPath toolchain/windows_x64/kimi_backend_windows_x64_v1.lib -Algorithm SHA256"
  ],
  "references": [
    {
      "path": "SPEC.md",
      "purpose": "唯一の言語仕様正本。本文の具体的な決定事項・Appendix Aの検証義務・設計境界を使い、docリンク先は読まない。"
    },
    {
      "path": "STATUS.md",
      "purpose": "段階別の実装範囲・制限・managed検証の再現手順。履歴の成功を現在の完成証拠に置き換えない。"
    },
    {
      "path": "AGENTS.md",
      "purpose": "性能・文書更新・NativeAOT禁止の作業規則。"
    },
    {
      "path": "autoframe.md",
      "purpose": "§2のPLAN形式と、範囲・検証・完成・停止の制御規則。"
    },
    {
      "path": "automation/verification-guide.md",
      "purpose": "Microsoft.Testing.Platformの起動形、fixture共有、通常nativeとNativeAOTの区別に限る既存手順。旧計画へのリンクやautomation独自の進捗保存指示は採用しない。"
    },
    {
      "path": "backend/windows-x64/README.md",
      "purpose": "固定LLVM、backendの再生成/検証、native fixture、CLI統合の既存手順。"
    },
    {
      "path": "examples/Hello/README.md",
      "purpose": "build/run/emit-llvmとmanual-buildの実在する起動方法・成果物の場所。"
    }
  ],
  "completion_criteria": [
    {
      "id": "C01",
      "condition": "対象SPECの具体的な決定事項とAppendix Aの全検証義務について、有限の要件一覧と実装/検証の対応があり、対象内の未実装・未検証・未解決の必須指摘が0件である。",
      "verification": "第1〜22章とAppendix A/Fを節・規則単位で棚卸しし、要件ID、正確な節、期待動作/診断、関連段階、実装、テストまたは照合手順を内部記録で対応付ける。Appendix D等の除外根拠とdocだけの詳細を別に照合する。全体監査で一覧からコード/証拠へ、残るUnsupported/Missing/TODOからSPECへ逆方向にも確認し、対象規則の抜けや未実装を除外へ付け替えた箇所がないことを確認する。"
    },
    {
      "id": "C02",
      "condition": "確定した字句・構文、型/Origin、宣言/名前/アクセス、推論/overload、Contract/Property/generic body、式/演算/制御フローの静的意味と診断が実装される。正当な入力を受理し、規定の不正入力・必須警告を正しい位置で報告する。",
      "verification": "各規則に正常・不正・境界・相互作用ケースを割り当て、xUnitTestの構文/Binding/解析テストで確認する。parse/write/parseと保存/再読込、source-local context、候補/入力順、定義時の普遍的証明、未到達コード検査、キャッシュ失効を含め、Debug/Releaseで受理と必須診断が一致する。"
    },
    {
      "id": "C03",
      "condition": "全対象操作で初期化・Copy/Move/部分Move・Origin/Loan/reborrow・効果要約・refinement・capture・通常transfer/defer/deinitとAbortの所有権/cleanup規則が実装され、必要な義務を解決してから生成する。",
      "verification": "Appendix A.3〜A.4/A.7〜A.13/A.15を対応表と照合し、分岐/loop固定点、返却/格納借用、引数/default/間接call/static/cleanup効果、構築中断、逆順破棄、結果確保後の非終了を検証する。managedの受理/拒否とnativeの観測可能な取得/副作用/破棄順を別々に確認する。"
    },
    {
      "id": "C04",
      "condition": "確定したwindows-x64-v1のApplicationとLibraryに必要な型・値・関数・Core操作・generic・object/Weak・collection・layout・ABI・FFI・runtimeが実装され、ApplicationはLLVM検証とO0/O2実行で仕様どおりに動作する。Libraryは規定の未最適化IR/署名/linkageとオブジェクト生成を満たす。",
      "verification": "Appendix A.14と§21〜22の型/ABI/数値変換/定数/メタデータ/rc・arc・Weak/FFI/故障注入/backend供給の表を網羅する。LLVM verifierと実nativeを実行し、stdout/stderrのbytes、exit、副作用/cleanup順をO0/O2で比較する。arcのメモリ順序はIR/native成功に加えて規定のprotocolをレビューする。Hello自身が14 UTF-8 bytesのHello, world!＋LF、空stderr、exit 0を出し、Abortは規定どおり終了する。"
    },
    {
      "id": "C05",
      "condition": "module/source context、確定したMod実行規則、依存・意味計画の再検証、Core同一性、Project設定、startup/static初期化、emit-llvm/build/runおよび成果物の整合性が接続される。部分仕様の確定済み意味を未実装のまま残さない。",
      "verification": "§18〜20、§21.3.4、§22、Appendix A.1〜A.3に対応するmulti-source/module・Mod・再Bind/再生成・cache破損/依存変更・Core偽装・初期化順/循環・取消/失敗のテストを行う。未確定の公開形式を要する部分は内部APIで確定規則を検証する。CLIでIR/manifest/exeのhash対応、失敗時の旧成功無効化、runの非自動build、出力/exit転送を確認する。"
    },
    {
      "id": "C06",
      "condition": "現行入力に対するDebug/Release build、全managed suite、LLVM/通常nativeと関連統合検証が成功し、実行漏れ・0件・skip・古いfixtureの混入がない。",
      "verification": "依存未復元時のみdotnet restore Kimigayo.slnxを先行する。dotnet build Kimigayo.slnx -c Debug --no-restore -v:minimal → dotnet test --project xUnitTest/xUnitTest.csproj -c Debug --no-build --no-restore → Debug用LLVM/native検証、その後Releaseも同じ順で直列実行する。LLVM/nativeはpwsh -NoProfile -File ./backend/windows-x64/build.ps1 -ToolchainRoot ./toolchain、test-emission.ps1 -Configuration DebugまたはRelease -ToolchainRoot ./toolchain、test-scalars.ps1 -ToolchainRoot ./toolchainを使用する。test-cli.ps1 -Configuration Release -ToolchainRoot ./toolchain、test-lsp.ps1 -CompilerPath Kimi/bin/Release/net10.0/Kimi.dll、test-artifact-paths.ps1、test-toolchain.ps1、test-kernel32.ps1 -ToolchainRoot ./toolchainも同ディレクトリーからpwsh -NoProfile -Fileで実行する。test-manual-build.ps1 -Manifest examples/Hello/bin/x86_64-pc-windows-msvc/Hello.link.json -ToolchainRoot ./toolchainは現行Helloのemit後に行う。実装で追加した対象機能の検証も全件含める。各コマンドの終了コード、実件数、警告、入力hash、期待/実際の出力を保存する。全体監査は生成先を安全に空にして構成ごとにfixtureを再生成し、生成された全入力とnativeで検証した一覧/hashを突き合わせる。"
    },
    {
      "id": "C07",
      "condition": "SPECが規定する計算量・無割り当て・配置・generic生成上限を満たし、代表的なコンパイル経路に避けられる重大な割り当て/保持参照/コード膨張の回帰がない。",
      "verification": "既存のAllocationMeasurement系テスト、再parse後の保持参照検査、collection容量内操作のallocation/操作回数、SliceのO(1)操作、generic上限/固定frameを検証する。必要な比較はBenchmark/の該当workloadで同じ条件の前後を測る。O2の代表的IR/コードをレビューして不要なslot/transfer/flagを確認する。全経路ゼロ割り当てや新たな数値目標を完成条件に追加しない。"
    },
    {
      "id": "C08",
      "condition": "SPEC.mdとSTATUS.mdが完成した製品と一致し、確定事項の未実装を隠さず、残る対象外の設計境界と実際の検証範囲を明記している。",
      "verification": "STATUSの各分野・制限を要件一覧と現物へ照合し、文書リンクと再現コマンドを確認する。SPEC更新が規則の削除/緩和やdoc由来の要件追加でないことを差分レビューする。必須条件C01〜C07の有効な証拠と全必須taskの対応を最終監査し、NativeAOT・未確定機能・追加profileを実施済みと書いていないことを確認する。"
    }
  ],
  "tasks": [
    {
      "id": "T01",
      "description": "SPEC全対象の要件一覧と、STATUS/現ソース/既存テストの段階別対応を内部計画に作る。",
      "required": true,
      "depends_on": [],
      "criterion_ids": [
        "C01",
        "C08"
      ],
      "acceptance": "すべての具体的規則・Appendix Aの義務に対応先があり、未確定部分の除外理由が節単位で限定されている。後続taskを必要に応じ分割し、元ID/必須性/条件を保持する。",
      "verification": "SPEC第1〜22章・Appendix A/D/Fと双方向に照合する。docを参照せず、既存の未対応拒否が言語仕様か実装制限かを分類する。"
    },
    {
      "id": "T02",
      "description": "実装開始時の環境・依存・生成物の範囲を確認し、既存managed/native検証の基準を採取する。",
      "required": true,
      "depends_on": [
        "T01"
      ],
      "criterion_ids": [
        "C06",
        "C07"
      ],
      "acceptance": "現ソースに対する開始時の結果と失敗原因、native供給物の固定版/hash、fixture経路が識別できる。このtaskは不具合発見を許容するが、未実行を成功としない。",
      "verification": "STATUS §7とC06の手順を必要な前提順で用いる。net10.0とMicrosoft.Testing.Platformを確認する。toolchainが不足すれば既存backend READMEの準備条件を示し、環境依存の検証だけを保留する。NativeAOTは実行しない。"
    },
    {
      "id": "T03",
      "description": "字句/構文・source context・directive・Koto保存/再読込と必須診断の不足を補完する。",
      "required": true,
      "depends_on": [
        "T01",
        "T02"
      ],
      "criterion_ids": [
        "C02"
      ],
      "acceptance": "§2/19、Appendix A.1/A.2/A.6/A.15とFに適合し、元位置・literal精度・body形式・非選択構文の検査境界が保持される。",
      "verification": "Unicode/encoding/literal、front-end/parse/write/serialization、directive条件・回復・source-local環境の既存および不足回帰テストを実行する。"
    },
    {
      "id": "T04",
      "description": "完全Type/Origin/length、宣言断片、名前/アクセス・overload/推論・候補確定を完成する。",
      "required": true,
      "depends_on": [
        "T03"
      ],
      "criterion_ids": [
        "C02",
        "C05"
      ],
      "acceptance": "§3/6〜10の型表現・宣言/可視性・候補選択と未解決義務の保持が成立する。Origin bound/length定数、generic slot、defaultの宣言環境、継承receiverを失わない。",
      "verification": "TypeBinding/Constraint/Contract/Access/Call系を拡充し、前方参照、fragment、候補順、曖昧性、generic/length推論、cache失効、usage失敗後のfallback禁止を確認する。"
    },
    {
      "id": "T05",
      "description": "一般Place・Origin/Loan solver、部分Move/reborrow、refinement、制御フローとcleanup計画を完成する。",
      "required": true,
      "depends_on": [
        "T04"
      ],
      "criterion_ids": [
        "C02",
        "C03"
      ],
      "acceptance": "§14〜17の静的規則がfield/index・返却/格納借用・未到達検査まで成立する。CFG到達性と型検査継続、構造的完了、結果確保とcleanupを区別する。",
      "verification": "Ownership/ControlFlow/Unreachable/Pattern系を拡充し、分岐/loop固定点、uniq再借用、借用escape、再初期化、guard失敗側、defer非終了、通常transferとAbortを確認する。"
    },
    {
      "id": "T06",
      "description": "struct/enum/Property、constructor/base/deinit、static storageの意味解析と責任計画を完成する。",
      "required": true,
      "depends_on": [
        "T04",
        "T05"
      ],
      "criterion_ids": [
        "C02",
        "C03"
      ],
      "acceptance": "§6/11/16の構築・accessor・部分初期化・継承・破棄の契約を表現し、storageとcomputed結果、論理順と物理順を混同しない。",
      "verification": "Property/Enum/Startup/Ownershipテストでfirst placement、自分への代入、custom setter、祖先deinit、構築中断・base責任・逆論理順を確認する。"
    },
    {
      "id": "T07",
      "description": "generic body/静的Contract/associated Type/conditional conformance、特殊化、効果familyとObjectCompatibleの証明を完成する。",
      "required": true,
      "depends_on": [
        "T04",
        "T05",
        "T06"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "C05"
      ],
      "acceptance": "§7〜12/15/21.3とAppendix A.3/A.8/A.11/A.12の定義時保証が成立する。再帰効果/default/cleanupを含め、Unknownや未確定familyから公開保証を作らない。",
      "verification": "候補と使用合法性の分離、条件付きCopy/Move、Originの普遍性、特殊化closed set、再帰/間接/別module向け要約と失効を検証する。"
    },
    {
      "id": "T08",
      "description": "scalar・数値変換・直接関数/default引数・全制御フローのLLVM/実行を一般化する。",
      "required": true,
      "depends_on": [
        "T05"
      ],
      "criterion_ids": [
        "C03",
        "C04"
      ],
      "acceptance": "規定の数値変換・評価順・checked算術・Unit/Never/選択/loop結果と引数/結果責任をloweringする。i128除算等profileの明示禁止は維持する。",
      "verification": "Scalar/Integer/WideInteger/Float/Conversion/Function/Result/Deferred系の不足を埋め、精度境界・NaN/±0・範囲外・短絡・非終了・default副作用をLLVM/O0/O2で検証する。"
    },
    {
      "id": "T09",
      "description": "aggregate ABI、struct/enum/Property/constructor/deinit、要素射影/index、分解matchと集約結果の生成を完成する。",
      "required": true,
      "depends_on": [
        "T06",
        "T08"
      ],
      "criterion_ids": [
        "C03",
        "C04"
      ],
      "acceptance": "Tuple/固定配列/struct/enumをlocal全体転送に限定せず、引数/結果・部分操作・cleanupまで規定のlayout/ABIで扱う。",
      "verification": "Aggregate/Enum/Match/Propertyの実行fixtureを拡充し、zero-size、padding、重なり、深さ/size限界、部分構築/Move、借用subject/guard、逆順破棄を検証する。"
    },
    {
      "id": "T10",
      "description": "Function Item・Closure・共通Function Type・Callableのcapture/間接call/結果借用と実行表現を完成する。",
      "required": true,
      "depends_on": [
        "T07",
        "T09"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "C04"
      ],
      "acceptance": "§7.6/8.6/15.8/21.2〜3のcapture順、receiver別取得、Owned環境、隠れた依存、erasure/ABIを満たす。",
      "verification": "明示/暗黙・未使用/nested capture、Copy/Move/ref/uniq、Shared/Exclusive/Consuming、返却Loan、間接call効果とcleanupをmanagedおよびO0/O2で検証する。"
    },
    {
      "id": "T11",
      "description": "object Semantics・view/refinement・obj/rc/arc/Weak、循環構築とruntime metadataを完成する。",
      "required": true,
      "depends_on": [
        "T07",
        "T09",
        "T10"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "C04"
      ],
      "acceptance": "確定した生成/複製/降格/upgrade・strong/weak count・初期化/最終解放・dynamic cleanup・静的member呼出を満たす。未導入のruntime Contract View構文は追加しない。",
      "verification": "§13.5〜13.6/21.2.3、Appendix A.8/A.9/A.14を網羅し、失敗注入、count上限、循環生成中の公開、resurrection拒否、view別release、arc protocolのorderingを検証する。"
    },
    {
      "id": "T12",
      "description": "Coreの全確定宣言/操作、Array/Index/Range/ResolvedRange/Slice/Dictionary、反復、比較/Stringify/補間を接続する。",
      "required": true,
      "depends_on": [
        "T07",
        "T09",
        "T10"
      ],
      "criterion_ids": [
        "C02",
        "C03",
        "C04",
        "C07"
      ],
      "acceptance": "Core catalogの現在の18枠を上限にせず、§22.1と参照先が要求するWeak等も含めて完成する。§4.6〜4.7の容量/順序/Loan/複雑度、§12〜14の構築・補間・forを満たす。未確定のstring連結所有権は導入しない。",
      "verification": "Core identity/shape/偽装拒否とcollection/iterator/string実行を検証する。空/最大境界、^0/^1、重複key、削除再追加順、None/Err、返却依存、capacity内無割り当て、O(1)/償却上限、失敗時cleanupを含める。"
    },
    {
      "id": "T13",
      "description": "raw pointer・unsafe・C/Kimigayo layout・LibraryImportとWindows ABI/供給物を完成する。",
      "required": true,
      "depends_on": [
        "T06",
        "T09"
      ],
      "criterion_ids": [
        "C02",
        "C04"
      ],
      "acceptance": "§5/21.1/21.5/22.3の許可操作・拒否境界・layoutとABIを満たす。LLVM inbounds/poison、言語UB、checked算術を混同しない。",
      "verification": "Clang C対照、small整数拡張、FP混在/5引数以上、pointer往復/正当なone-past/zero displacement、FFI衝突、unwind/ASLR/_fltused/未解決symbol/backend依存を検証する。"
    },
    {
      "id": "T14",
      "description": "外部Kotonoha/ソース依存、Mod実行、意味計画の保存/再検証をコンパイル入口へ接続する。",
      "required": true,
      "depends_on": [
        "T07"
      ],
      "criterion_ids": [
        "C01",
        "C05"
      ],
      "acceptance": "§18/20.7/21.3.4の確定したsource identity・Mod順序/一回実行/生成統合/finalization・依存失効が成立する。公開パッケージ/API形式の未確定を理由にこれらの意味を省かない。",
      "verification": "multi-source/moduleのdefinition-site scope、依存順/循環、Mod追加/失敗/取消・provisional Binding、署名/アクセス/特殊化追加や保証撤回、互換再検証とrebuild情報不足を内部APIとartifactテストで確認する。"
    },
    {
      "id": "T15",
      "description": "generic共有/完全特殊化/自動特殊化、schema/entry ABI、生成上限・固定frameとLibrary生成を完成する。",
      "required": true,
      "depends_on": [
        "T10",
        "T11",
        "T12",
        "T13",
        "T14"
      ],
      "criterion_ids": [
        "C04",
        "C05",
        "C07"
      ],
      "acceptance": "§21.3〜21.4の生成方式・予算・依存closure・静的storage前提を満たす。検証済み意味計画の再利用と未導入の永続object-code cacheを区別する。",
      "verification": "generic型/length/Origin、再帰/共有entry、選択済み特殊化、上限/コード共有、source/module順、Library IR/署名/オブジェクトを検証し、最適化や予算で意味/cleanupが変わらないことを確認する。"
    },
    {
      "id": "T16",
      "description": "Application startup/static初期化・Core統合・runtime・CLI/成果物を全対象機能へ接続する。",
      "required": true,
      "depends_on": [
        "T11",
        "T12",
        "T13",
        "T14",
        "T15"
      ],
      "criterion_ids": [
        "C04",
        "C05"
      ],
      "acceptance": "全対象入力について意味解析から実行まで到達でき、一般container/外部module/未使用bodyの一律Unsupported制限を必要な検査へ置換する。設定・toolchain・原子的公開・取消の保証を満たす。",
      "verification": "§20.8/22のApplication/Library区別、main選択/static順序/循環/破棄、失敗時旧成果無効化、hash/版不整合、emit/build/run/manual-build、Hello/既存examplesの期待出力を確認する。LSPは既存通信の回帰確認に留める。"
    },
    {
      "id": "T17",
      "description": "規定の性能契約と割り当て/保持参照/コード生成の回帰を検証し、必要な修正を行う。",
      "required": true,
      "depends_on": [
        "T16"
      ],
      "criterion_ids": [
        "C07"
      ],
      "acceptance": "C07の必須性能契約と既存の妥当なallocation/再利用条件を満たし、代表経路で見つかった重大な不要割り当て・保持参照・code膨張を解消する。",
      "verification": "C07を実行する。測定対象と環境、前後値を記録し、意味を保つ修正後に影響範囲を再検証する。"
    },
    {
      "id": "T18",
      "description": "全対象のmanaged/LLVM/通常native/CLI/backend回帰と仕様適合監査を行い、残る欠落を修正する。",
      "required": true,
      "depends_on": [
        "T17"
      ],
      "criterion_ids": [
        "C01",
        "C02",
        "C03",
        "C04",
        "C05",
        "C06",
        "C07"
      ],
      "acceptance": "全必須規則の成功証拠が現行入力に対応し、未実装/未検証/必須指摘が残らない。既存件数だけに依存しない。",
      "verification": "C01〜C07の手順を実行する。Debug生成物の検証完了後にReleaseへ進み、空の生成先から得たfixture一覧/hashと実行全件を照合する。不足箇所は対応taskを再計画/修正して再検証する。"
    },
    {
      "id": "T19",
      "description": "SPEC/STATUSと関連利用文書を製品に整合させ、完成条件を最終監査する。",
      "required": true,
      "depends_on": [
        "T18"
      ],
      "criterion_ids": [
        "C01",
        "C08"
      ],
      "acceptance": "確定仕様の意味を保持した文書と有効な全体証拠が一致する。対象外の詳細・禁止された検証を完了扱いにしていない。",
      "verification": "C08を実行し、文書更新後の入力署名/証拠の有効性を再確認する。必要な照合/再検証を経た場合だけCompleteとする。"
    }
  ],
  "milestones": [
    {
      "id": "M01",
      "description": "要件と既存検証の基準を確定",
      "task_ids": [
        "T01",
        "T02"
      ],
      "acceptance": "対象規則/除外境界と実装段階の対応、実装開始時の検証条件が識別できる。"
    },
    {
      "id": "M02",
      "description": "構文・静的意味・所有権の一般機能を完成",
      "task_ids": [
        "T03",
        "T04",
        "T05",
        "T06",
        "T07"
      ],
      "acceptance": "対象機能の静的判断と証明義務を実装し、未解決を成功にしない。"
    },
    {
      "id": "M03",
      "description": "全対象機能の生成・実行とProject統合を完成",
      "task_ids": [
        "T08",
        "T09",
        "T10",
        "T11",
        "T12",
        "T13",
        "T14",
        "T15",
        "T16"
      ],
      "acceptance": "対象SPECのCore/ABI/runtime/module機能が接続され、機能別のmanaged/native検証を満たす。"
    },
    {
      "id": "M04",
      "description": "性能・回帰・仕様適合・文書の完成監査",
      "task_ids": [
        "T17",
        "T18",
        "T19"
      ],
      "acceptance": "C01〜C08の全完成条件を現行入力の有効な証拠で満たす。"
    }
  ]
}
```
<!-- autoframe:end -->
