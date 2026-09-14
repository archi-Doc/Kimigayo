# Kimigayo コンパイラー実装計画

## ユーザープロンプト（原文）

```text
目的: Kimigayoコンパイラーを全て実装すること
対象: SPEC.md, STATUS.md
完成条件: SPEC.mdに記載されている決定事項（docフォルダー内への参照を除く）を全て実装すること
対象外・制約: docフォルダー内の仕様は、未確定のため参照しない
```

## 実行計画

<!-- autoframe:begin -->
```json
{
  "schema_version": 1,
  "project_id": "kimigayo-compiler-spec-completion",
  "objective": "SPEC.md 本文内で決定済みの言語規則・コンパイラー要件をすべて実装し、STATUS.md と実装・検証結果を一致させ、Kimigayo コンパイラーを完成させる。doc フォルダー内の参照先を仕様根拠にしない。",
  "scope": [
    "仕様の正本はプロジェクト直下の SPEC.md（§1–22、規範的な付録A、本文の規則を要約する付録F）。STATUS.md は現実装の出発点と差分確認に使い、完成範囲をその残作業一覧に限定しない。",
    "Kimi/Compiler の Core・Lexing・Parsing・Binding・Analysis・Emission、型・定数・診断・永続化、Kimi/SolutionAndProject と CLI、必要な Core 定義と Windows runtime/backend を実装対象とする。",
    "既存 xUnitTest、backend/windows-x64 の通常 native 検証、examples、Benchmark を拡張・再利用する。SPEC.md と STATUS.md の更新は確定事項の維持、実装選択の明記、正確な対応状況・検証手順の整理に限る。",
    "doc へのリンクがある節も、SPEC.md 自体に記述された独立に判定可能な決定事項（§21.3 の generic 共有、§21.4.6 の固定 frame、§21.3.4 の再利用要件など）は対象。リンク先でのみ定義された機能・契約・優先規則は取り込まない。",
    "実装段階は字句/構文、意味解析、所有権/効果、layout/ABI、LLVM、native 実行を区別する。初期実行 subset や現在の Unsupported を完成範囲の上限にしない。仕様自身の初期 profile 制限は維持する。"
  ],
  "non_goals": [
    "doc/** の読取り・参照・変更、および他文書を経由した doc 仕様の補完。冒頭のリンク先による Composition Root の Entry/Provider/最終 composition 拡張、#Test・$expect・$require・kimi test は本計画の実装要件にしない。本文にある $abort と Core.writeLine は対象。",
    "付録Dおよび各担当節が未導入・Deferred design とする追加言語機能の制定。runtime Contract View の新構文、virtual/override、追加 Pattern、string +/+= の未確定な取得規則、並行言語機能、追加 OS/CPU profile、外部公開 Library/DLL ABI などを独自に導入しない。",
    "NativeAOT の publish・テスト、VS Code 拡張の一般整備、汎用 LSP 機能の完成、CI 運用・外部配布・公開。SPEC.md が要求する診断、source 表示/保存、構文往復と意味保存は除外しない。",
    "autoimpl/** の修正、runner/Worker の起動、自動実行の設定、PLAN.md 以外の作成作業を今回の依頼で開始すること。"
  ],
  "constraints": [
    "今回の操作は PLAN.md の作成と形式・参照・依存・網羅性の静的検査のみ。ここに記載する製品変更、ビルド、テスト、benchmark は後続の実装開始指示に対する計画であり、今回実行しない。",
    "SPEC.md §1.3 の規範性と担当節の境界を適用する。Specified, not implemented は必須。推奨・例・付録Bの任意アルゴリズムは新たな必須機能にしない。付録Cは STATUS.md への案内、付録Eは用語索引として扱う。",
    "未確定の公開構文/API/交換形式を創作しない。決定済みの意味・保持情報は内部 API、検証用入口、source 再構築等で実装・検証し、公開形式の未確定を理由に一括除外しない。内部表現、具体的 Mod host API、有限 resource limit など許された実装選択は合理的に決め、SPEC.md に記す。",
    "本文と要約の食い違いは担当節の明示的規則で整理する（例: §13.5.8–9 の公開 object/Weak API と F.9 の古い境界記述、§13.3 の string 連結の実行禁止）。この原則でも解消しない目的・必須条件・変更範囲の不足だけ、対象箇所と具体案を示して質問し、未決項目を完成扱いしない。",
    "既存の未コミット変更・削除を保持する。IMPLEMENTATION_PLAN.md の復元を前提にせず、別の計画文書を作らない。自動 reset/clean/stash/commit/push は行わない。PLAN.md、AGENTS.md、autoimpl/** は後続 Worker も変更しない。",
    "作業・入力範囲は下記の正のパス指定に限定し、doc/** と Design/** は仕様資料として参照しない。入力中の doc リンクは追跡しない。環境不足は記録し、成功・未実行・skip を混同しない。",
    "メモリ割り当てを実用上可能な範囲で抑え、既存の型/記号/CFG/ABI/layout 表・scratch 再利用を維持する。全経路 zero-allocation や未指定の速度向上率を完成条件に追加しない。意味検査・安全性・必須診断を性能のために省略しない。",
    "検証は同一構成で build → managed test → その構成由来の fixture の native 検証を直列実行する。bin/scalar-fixtures は構成共有なので Debug/Release の生成・消費を混在させない。古い生成物、0件、フィルターで除かれた必須例を合格に数えない。",
    "通常コンパイラーは .NET 10 managed DLL を用いる。global.json は Microsoft.Testing.Platform を選択しているため dotnet test --project を採用する。復元が必要なら後続作業で dotnet restore Kimigayo.slnx を先に行い、NativeAOT は明示の追加指示なしに実行しない。",
    "native 受入は既存 toolchain/ の実体と backend/windows-x64/profile.json の LLVM 22.1.8・ABI・hash を照合して行う。自動ダウンロード、PATH/認証/外部設定の変更をしない。未固定 toolchain の探索実行を受入証拠に使わない。",
    "generated_scope は生成物だけに使う。toolchain/windows_x64/** の検証済み backend 再生成物も成果物 hash として照合し、除外されたから未検証でよいとはしない。backend 変更時の catalog 更新は実際の native/generated-module 検証と共通 release/version 規則を満たす。",
    "各 task の verification は担当段階の到達条件を検証する。最終完了には全規範項目の対応表、未使用/未到達の不正例の拒否、必要な実行の証拠を含め、構文対応や内部モデルだけで source 実行可能な機能の完成を主張しない。証拠・進捗は STATUS.md または後続実行の記録へ置き、PLAN.md に追記しない。"
  ],
  "work_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "examples/**",
    "Benchmark/**",
    "SPEC.md",
    "STATUS.md",
    "README.md",
    "Directory.Build.props",
    "global.json",
    "Kimigayo.slnx",
    ".gitignore"
  ],
  "input_scope": [
    "Kimi/**",
    "xUnitTest/**",
    "backend/windows-x64/**",
    "examples/**",
    "Benchmark/**",
    "Playground/**",
    "SPEC.md",
    "STATUS.md",
    "README.md",
    "AGENTS.md",
    "Directory.Build.props",
    "global.json",
    "Kimigayo.slnx",
    ".editorconfig",
    ".gitattributes",
    ".gitignore",
    "stylecop.json",
    "toolchain/**"
  ],
  "generated_scope": [
    "bin/**",
    "Kimi/bin/**",
    "Kimi/obj/**",
    "Kimi/Generated/**",
    "xUnitTest/bin/**",
    "xUnitTest/obj/**",
    "Benchmark/bin/**",
    "Benchmark/obj/**",
    "Playground/bin/**",
    "Playground/obj/**",
    "backend/windows-x64/bin/**",
    "examples/**/bin/**",
    "examples/**/obj/**",
    "TestResults/**",
    "xUnitTest/TestResults/**",
    "BenchmarkDotNet.Artifacts/**",
    "toolchain/windows_x64/**"
  ],
  "environment_checks": [
    "dotnet --info",
    "$PSVersionTable | ConvertTo-Json -Depth 4",
    "Get-Content -LiteralPath backend/windows-x64/profile.json -Raw",
    "foreach ($name in @('clang','opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-objdump')) { $path = Join-Path 'toolchain' ($name + '.exe'); & $path --version; if ($LASTEXITCODE -ne 0) { throw ('Version probe failed: ' + $path) } }",
    "foreach ($name in @('clang','opt','llc','lld-link','llvm-nm','llvm-readobj','llvm-objdump','llvm-lib','llvm-dlltool')) { Get-FileHash -LiteralPath (Join-Path 'toolchain' ($name + '.exe')) -Algorithm SHA256 }",
    "Get-FileHash -LiteralPath toolchain/windows_x64/kimi_backend_windows_x64_v1.lib -Algorithm SHA256"
  ],
  "references": [
    {
      "path": "SPEC.md",
      "purpose": "doc 参照先を除いた確定規則・付録A/F・仕様境界の正本"
    },
    {
      "path": "STATUS.md",
      "purpose": "現実装の段階別制限、既存テスト名、構成別検証コマンド"
    },
    {
      "path": "AGENTS.md",
      "purpose": "性能、文書更新、NativeAOT 禁止の適用指示"
    },
    {
      "path": "autoimpl/SPEC.md",
      "purpose": "§2 の PLAN 制御入力契約および §5.1 の範囲記法"
    },
    {
      "path": "autoimpl/schemas/plan.schema.json",
      "purpose": "schema_version 1 の形式検査"
    },
    {
      "path": "README.md",
      "purpose": "通常 build、toolchain、CLI の既存入口。NativeAOT 手順は実行対象外"
    },
    {
      "path": "global.json",
      "purpose": "Microsoft.Testing.Platform の runner 選択"
    },
    {
      "path": "xUnitTest/xUnitTest.csproj",
      "purpose": ".NET 10 / xUnit v3 の検証プロジェクト"
    },
    {
      "path": "backend/windows-x64/README.md",
      "purpose": "既存 native helper、fixture、toolchain 検証手順"
    },
    {
      "path": "backend/windows-x64/profile.json",
      "purpose": "LLVM、backend ABI/version/hash、供給 symbol の正本"
    },
    {
      "path": "backend/windows-x64/test-scalars.ps1",
      "purpose": "bin/scalar-fixtures の通常 native O0/O2 検証"
    },
    {
      "path": "backend/windows-x64/test-emission.ps1",
      "purpose": "構成別 emission fixture と runtime 故障注入"
    },
    {
      "path": "backend/windows-x64/test-cli.ps1",
      "purpose": "managed DLL を使う CLI 統合検証"
    }
  ],
  "completion_criteria": [
    {
      "id": "C01",
      "condition": "SPEC.md §1–22、A.1–A.15、F.1–F.9 の全必須規則・禁止・実装上の不変条件・要求検証を有限の一覧にし、全項目を task・検証へ対応付ける。doc 参照先のみの要件、未確定部分、非規範部分の除外根拠を区別する。",
      "verification": "各見出しとその下の本文・表・箇条書きを順に照合し、安定した節/項目識別子、必須性、担当 task、検証段階・期待結果を STATUS.md の対応表または実行記録で確認する。必須の未対応・未実装・未検証を0件にする。"
    },
    {
      "id": "C02",
      "condition": "§2・§19・A.1–2/A.6・F の確定構文、source identity、Unicode/字句、literal、条件選択、構文往復・診断を実装する。",
      "verification": "既存 Lexing/Parsing/Source/Directive/Serialization テストを再利用し、正負例、位置、source isolation、parse/write/parse・保存/再読込、literal の exact 値、選択/非選択境界を照合する。"
    },
    {
      "id": "C03",
      "condition": "§3・§6・§8–10 の Type/Symbol/access/Contract/推論/特殊化規則と A.3/A.11/A.12 の保持・証明要件を満たす。",
      "verification": "Binding の正負例と順序入替、generic 定義時/使用時の検査、再Bind/依存変更時の失効を確認する。Unknown や未解決義務を成功扱いせず、選択後の所有権失敗による候補 fallback がない。"
    },
    {
      "id": "C04",
      "condition": "§7・§11・§12.4 の関数・default・receiver・Property・constructor・callable・効果規則を実装する。",
      "verification": "引数評価/取得順、getter/setter/初回配置、capture/call の分離、receiver ごとの公開保証、直接/間接/generic call と正常 cleanup の意味・実行結果を確認する。"
    },
    {
      "id": "C05",
      "condition": "§3.1・§12–13 の実行可能な scalar/literal/文字列/変換/演算/代入を実装し、担当節が禁止する操作は拒否する。",
      "verification": "型の全対象方向、数値境界・NaN/±0/subnormal・UTF-8/NUL・評価順・範囲外 Abort を managed と O0/O2 で検証する。初期 profile 禁止の i128 除算等と未確定の string 連結は拒否を確認する。"
    },
    {
      "id": "C06",
      "condition": "§3.4–3.7・§15–16 の初期化/部分Move/Origin/Loan/cleanup/deinit を満たす。",
      "verification": "Place overlap、戻り参照・保存・reborrow・capture/消去依存、CFG join/loop、途中構築/置換、逆論理順の破棄と非終了 cleanup を正負例および実行時の取得/破棄観測で確認する。"
    },
    {
      "id": "C07",
      "condition": "§14・§17・A.9/A.10/A.15 の制御フロー、Pattern/guard、refinement、require、Option/Result、Abort、必須警告を実装する。",
      "verification": "構造上の完了と実行到達の分離、全 Pattern 境界、結果確保→cleanup→配送、Never/未到達も含む検査、診断位置・必須警告と抑制優先順位を確認し、O0/O2 の観測結果を比較する。"
    },
    {
      "id": "C08",
      "condition": "§4・§12.3.4・§14.6・§22.1 の固定配列・Array・Dictionary・Index・Range・ResolvedRange・Slice・反復の確定契約を実装する。",
      "verification": "A.12/A.13 の length/stride/境界/部分Move/共有読取、動的更新の取得・commit・容量・失敗・破棄順・保持Loan、永久枯渇 iterator と element Type を検証し、実行できる source 操作は native まで確認する。"
    },
    {
      "id": "C09",
      "condition": "§3.2.2/3.3・§12.4.4・§13.5.7–9/13.6.1・§15.8・§21.2 の確定 object/Weak/metadata/動的型規則を実装する。",
      "verification": "makeObj/makeRc/makeArc/clone/downgrade/upgrade/cyclic、同一性・count・Borrow・破棄、static upcast/runtime is、Building/Alive/Destroying/Freed を検証する。arc の順序証明を IR/native 試験と分けて記録し、未導入の Contract View/cast 構文を追加しない。"
    },
    {
      "id": "C10",
      "condition": "§18・§20.1–7・§21.3.4 と A.1/A.3 の module/source/構成/Mod/意味情報保持・再利用契約を、SPEC.md 本文で定義された範囲で実装する。",
      "verification": "source 再構築、直接依存と非公開環境の保持、再利用/失効、Mod graph・一回実行・append 境界・順序/取消/失敗・出力除去を検証する。未確定の外部 binary 交換形式や doc 由来の composition/test 接続を前提にしない。"
    },
    {
      "id": "C11",
      "condition": "§21.3・§21.4.6 の generic 共有/特殊化、context/schema、resource limit、固定 scratch frame と A.12/A.14 の規範要件を満たす。",
      "verification": "予算0を含む選択、必須特殊化、有限再帰/増大key、全Type/length/Originに依存するキー、entry/adaptor/cleanup の一致、入力順序・cache有無・予算変更での意味不変を確認する。未知置換の動的 scratch への fallback は導入しない。"
    },
    {
      "id": "C12",
      "condition": "§21.1–5 の layout・metadata・内部ABI・checked lowering・Windows x64 profile を満たす。",
      "verification": "A.14 の layout/ABI/CFG/定数/FP/frame/unwind/symbol の各検証領域を対応付け、Clang の C layout 比較、最適化前後 verifier、COFF/逆アセンブル/依存・O0/O2 実行を確認する。LLVM Writer が未検証 AST を再解釈しない。"
    },
    {
      "id": "C13",
      "condition": "§5・§22 の全必須 Core 宣言、startup/static、FFI、console/runtime と raw pointer の確定操作を実装する。",
      "verification": "Core の不足/偽装/互換性を検査し、startup 候補・static cycle/shutdown、C ABI 全許可型・5引数以上、pointer の合法境界、runtime 故障注入を検証する。Hello はアプリ自身の14 UTF-8 byte、空stderr、exit 0、Abort は exit 1 を確認する。"
    },
    {
      "id": "C14",
      "condition": "§20.8 の emit-llvm/build/run、Library inspection、manifest/公開・toolchain/backend の規定を満たす。",
      "verification": "emit の LLVM 不要性、.ll/.link.json hash、Library の pre-opt body と object 生成、通常 Application build/run、失敗後の旧成功無効化、実 tool/library identity、未固定版/ABI/hash不一致の拒否を確認する。Library の外部リンク/公開ABIは要求しない。"
    },
    {
      "id": "C15",
      "condition": "全必須項目に対する変更後の managed Debug/Release 検証と、必要な LLVM/通常 native O0/O2 検証が成功し、性能・割り当ての規範条件と既存保証を維持する。",
      "verification": "C01 の一覧から試験と件数を照合し、0件・必須skip・未実行・予期しない失敗を0件にする。構成ごとの build/test/fixture/native ログ、期待stdout/stderr/exit/cleanup、allocation 検査と代表 workload の比較を保存する。NativeAOT は実行しない。"
    },
    {
      "id": "C16",
      "condition": "SPEC.md と STATUS.md が完成した実装と検証に一致し、既知の必須未実装/未検証が残らず、未確定・対象外との区別と再現手順が明確である。",
      "verification": "C01 の全項目をコード・テスト・証拠へ逆照合し、仕様削除/条件緩和による完了がないこと、文書のリンクと検証手順、全 milestone の到達条件を最終監査する。"
    }
  ],
  "tasks": [
    {
      "id": "T01",
      "description": "§1–22・付録A/Fの全規範項目を棚卸しし、STATUS.md と現コードの段階別差分、担当 task、期待結果を対応付ける。付録B/C/D/Eと doc 参照境界も分類する。",
      "required": true,
      "depends_on": [],
      "criterion_ids": [
        "C01",
        "C16"
      ],
      "acceptance": "全見出し配下の必須規則・禁止・検証表に担当があり、未確定/非規範/対象外は担当節による根拠がある。実装済みも検証対象に残す。",
      "verification": "文書とコード/既存テストの静的照合。対応表は STATUS.md または実行記録に保存し、以後の分割でも元 task と必須性を維持する。C01 の漏れ検査を行う。"
    },
    {
      "id": "T02",
      "description": "§2・A.1/A.6・F.1–F.9の字句/構文、source snapshot、診断位置、writer/serialization を仕上げる。",
      "required": true,
      "depends_on": [
        "T01"
      ],
      "criterion_ids": [
        "C02"
      ],
      "acceptance": "Unicode 15.0/NFC、UTF-8、不正文字/escape、indent/継続、完全Type/Origin/宣言/式の確定構文と回復を実装し、元sourceとexact literalを保持する。",
      "verification": "UnicodeIdentifier/SourceEncoding/SourceDocumentAndDiagnostic、NumberLiteral/CharLiteral/StringLiteral、FrontEndSyntax/ParserRegression/PropertyRevision/NestedType/KotonohaSerialization を基に正負例と往復検証を補完する。"
    },
    {
      "id": "T03",
      "description": "§19・§20.3–6・A.2 の target 準備、環境条件と即時 #if/#switch 選択を完成する。",
      "required": true,
      "depends_on": [
        "T02"
      ],
      "criterion_ids": [
        "C02",
        "C10"
      ],
      "acceptance": "case-sensitive の設定/組込み名、型・重複・NFC、全 reached 条件の診断、false #if と非選択 case の異なる検査境界、選択項目の同一scopeを満たす。",
      "verification": "CompilationSpecification/DirectiveConditionValidation/CompileTimeSwitch を拡張し、短絡・非選択・入れ子・診断位置・alias/defer/transfer の境界と eager/cache 結果一致を確認する。"
    },
    {
      "id": "T04",
      "description": "§3・§4.1–4.4・§8.1・§15.2–4・A.12 の完全 Type、generic pair/length/Origin schema、定数参照・length 推論を完成する。",
      "required": true,
      "depends_on": [
        "T02",
        "T03"
      ],
      "criterion_ids": [
        "C03",
        "C08"
      ],
      "acceptance": "nested Semantics、Core/完全Type slot、Origin bound/variance、関数/配列/Weak を表現し、length の正規化・負数/overflow・recursive layout 義務を保持する。",
      "verification": "TypeBinding/ConstraintBinding/NumberLiteral と新しい length/schema テストで slot種別、全Type identity、occurs-check、bound/宣言fragment一致、0長・0size・依存式・推論順序不変を確認する。"
    },
    {
      "id": "T05",
      "description": "§6・§9・§18.1・A.3 の container/fragment、Type/Value/Origin/Label、lookup/access/inheritance を完成する。",
      "required": true,
      "depends_on": [
        "T04"
      ],
      "criterion_ids": [
        "C03"
      ],
      "acceptance": "定義元source/aliasを保持し、前方宣言、重複、role/arity、可視性・公開API到達性、open/base・継承Name禁止、protected receiver を全祖先で検証する。",
      "verification": "Binding/IdentifierIdentity/InheritedReceiverBinding を拡張し、source/fragment/読み込み順を変えた結果、private generic依存、APIの入れ子Type、追加member/access変更による再検査を比較する。"
    },
    {
      "id": "T06",
      "description": "§8.2–8.7/8.9–10・§11.4・A.11 の proof、associated Type、static Contract、conditional conformance、witness を完成する。",
      "required": true,
      "depends_on": [
        "T04",
        "T05"
      ],
      "criterion_ids": [
        "C03",
        "C04"
      ],
      "acceptance": "Proven/Refuted/Unknown/Errorを区別し、明示identity、継承conformance、条件付きmember/Copy、共有候補の全置換保証、property operation別mappingを保持する。",
      "verification": "Constraint/Contract/ConditionalConformance/ConditionalMember/PropertyBinding を補完し、diamond・cycle・曖昧性・アクセス・非強化・receiver/Origin・parent経路一致、Unknownでの拒否と失効を確認する。"
    },
    {
      "id": "T07",
      "description": "§8.8–10・§10・A.3/A.12 の候補局所推論、generic定義検査、明示完全特殊化の登録・閉包・選択を完成する。",
      "required": true,
      "depends_on": [
        "T06"
      ],
      "criterion_ids": [
        "C03",
        "C11"
      ],
      "acceptance": "expected Type/literal/argument label に基づく候補比較と完全Type/length推論を行い、定義元のclosed setから必須特殊化を選ぶ。効果・所有権義務は期限まで保持し、最終化前に解消する。",
      "verification": "既存 TypeBinding/Constraint/Contract と追加特殊化テストで candidate rollback、曖昧性、Origin-only重複、unused選択body、defaults/safety継承、再帰・順序独立・使用違法時fallback禁止を確認する。"
    },
    {
      "id": "T08",
      "description": "§7.1–7.5・§12.1–2/12.4・§10.3/10.7 の通常関数、receiver、named/default/optional 引数と結果規則を完成する。",
      "required": true,
      "depends_on": [
        "T07"
      ],
      "criterion_ids": [
        "C04"
      ],
      "acceptance": "call plan が論理引数順・parameter対応・defaultの定義環境/評価・取得責任を保持し、Unit/Never/unsafe と明示receiverの制約を検証する。",
      "verification": "FunctionBody/FunctionEmission/Binding/ControlFlowConformance の段階別テストで名前付き順序、default副作用/途中transfer、未指定戻りUnit、optional単独構文、unsafe呼出/関数値禁止を確認する。"
    },
    {
      "id": "T09",
      "description": "§6.2.3・§11・A.4 の stored/computed/requirement Property、accessor、constructor/base initializer の意味計画を完成する。",
      "required": true,
      "depends_on": [
        "T06",
        "T08"
      ],
      "criterion_ids": [
        "C04",
        "C06"
      ],
      "acceptance": "read/get/set/初回配置/Moveを区別し、receiver・Copy/Origin・アクセス・witness制約と全construction layerの初期化/選択を保持する。static storageはT30の実行計画へ接続できる。",
      "verification": "PropertyBinding/PropertyRevisionParse/InheritedReceiverBinding とconstructorテストで private-set Move禁止、custom-get temporary、非Copy setter/self代入、branch初回配置、base access・暗黙constructor抑制を確認する。"
    },
    {
      "id": "T10",
      "description": "§3.1・§12.3・§13.1–5/13.7 の scalar、exact literal fitting、数値変換、比較、型/Semantics適応、代入の意味計画を完成する。",
      "required": true,
      "depends_on": [
        "T08",
        "T09"
      ],
      "criterion_ids": [
        "C05"
      ],
      "acceptance": "全数値型・char/bool/Unit/stringの定義済み操作を選択し、右辺先行代入/対象先行compound更新、チェック失敗、未導入操作の拒否を表す。string補間のStringify接続はT18で完成する。",
      "verification": "NumberLiteral/Integer/WideInteger/Float/Conversion/FloatConversion/Char/StringComparison のテストを基に全許可変換・literal境界・型identity・評価回数を検証する。string +/+= は実行禁止を維持する。"
    },
    {
      "id": "T11",
      "description": "§3.4–3.6・§11.1・§15.1・A.4/A.7/A.10 の Place 初期化・Consume・部分Move・構築責任を完成する。",
      "required": true,
      "depends_on": [
        "T09",
        "T10"
      ],
      "criterion_ids": [
        "C06"
      ],
      "acceptance": "field/base/tuple/固定配列literal pathとwhole Placeを区別し、Copy/Move/初回配置/再初期化/置換をCFG固定点で検査する。動的indexを勝手にMove Pathにしない。",
      "verification": "OwnershipAnalysis/EnumOwnership/MatchOwnership を拡張し、branch/loop join、途中構築・部分置換・base slicing禁止、0size責任、symbolic Copy/Move と診断sourceを確認する。"
    },
    {
      "id": "T12",
      "description": "§15.2–6/15.8–9・§3.7・A.8/A.12 の一般 Origin/Loan solver、ref/uniq/object borrow、保存/返却/効果伝播を完成する。",
      "required": true,
      "depends_on": [
        "T07",
        "T11"
      ],
      "criterion_ids": [
        "C06",
        "C09"
      ],
      "acceptance": "bound/meet/variance/principal推論とLoan provenanceを保持し、overlap・reborrow・親権限停止・呼出全期間保護・retention/escape/Ownedを検証する。Origin簡約で別Loanを消さない。",
      "verification": "ReferenceEmission/StringGuard と新規solverテストで複数入力の返却、nested reference、static/mutable static、alias/capture/callee効果、破棄時生存、入力順序・再Bind独立を確認する。"
    },
    {
      "id": "T13",
      "description": "§16・§15.7・A.7 の defer・deinit・通常transfer時cleanup・初期化維持exchangeの確定意味を完成する。",
      "required": true,
      "depends_on": [
        "T11",
        "T12"
      ],
      "criterion_ids": [
        "C06",
        "C07"
      ],
      "acceptance": "結果確保後の逆論理順破棄、base最後・完成済み層、Moved要素除外、defer境界/再入/非終了、Abortでunwindしない規則を解析計画に統合する。exchange未確定source APIは追加しない。",
      "verification": "DeferredEmission/EnumOwnership/UnreachableOwnership と追加cleanup計画テストで途中構築/呼出、deinit禁止操作・receiver escape、重複破棄、non-overlap exchangeの意味、cleanup阻害後の非配送を確認する。"
    },
    {
      "id": "T14",
      "description": "§7.6・§8.6/8.9–10・§12.4.4・§15.8・A.8 の capture/callable と効果族固定点を完成する。",
      "required": true,
      "depends_on": [
        "T08",
        "T12",
        "T13"
      ],
      "criterion_ids": [
        "C03",
        "C04",
        "C06"
      ],
      "acceptance": "capture取得順・環境identity・receiver別call・per-call Origin・erasure/Ownedを保持し、default/cleanup/間接/generic calleeを含めたObjectCompatibleの公開保証と使用義務を最終化する。",
      "verification": "captureとcallの取得/Copy/Move、Shared/Exclusive/Consuming、保持依存・結果Loan・未使用capture、再帰効果族・NotProven原因・循環conformance拒否を意味テストで確認する。"
    },
    {
      "id": "T15",
      "description": "§14・§17・A.9/A.10/A.15 の全制御構文、結果・refinement・Pattern/guard・必須診断を完成する。",
      "required": true,
      "depends_on": [
        "T10",
        "T12",
        "T13",
        "T14"
      ],
      "criterion_ids": [
        "C07"
      ],
      "acceptance": "全体/tuple/enum/共有subjectの分解、guard候補とbodyの別identity、網羅性/単一先行Pattern包含、require失敗非継続、未到達検査continuationを統合する。",
      "verification": "CurrentControlFlow/ControlFlowConformance/PatternBinding/MatchOwnership/UnreachableOwnership/RuntimeTypeTest を補完し、付録A.10/A.15全行、loop/label/transfer、Moveによるrefinement失効、Unit/効果なし値/Result破棄警告を確認する。"
    },
    {
      "id": "T16",
      "description": "§4.1–4.6・A.12/A.13 の固定配列構築/射影、Index/Range/ResolvedRange/Slice と一般 sequence access を完成する。",
      "required": true,
      "depends_on": [
        "T04",
        "T12",
        "T15"
      ],
      "criterion_ids": [
        "C08"
      ],
      "acceptance": "receiver/indexの単回評価、bounds後のexclusive activation、shared readと明示borrow、nested Place、Slice source Origin、0length/0stride/境界失敗を意味計画へ接続する。",
      "verification": "RangeIndexParse/AggregateEmissionを拡張し、^0/^1・最大isize・inclusive/reversed・saved range再利用、Copy/NonCopy要素、短い再slice・Loan/部分Move、共有読み取りfamilyを検証する。"
    },
    {
      "id": "T17",
      "description": "§4.7・§12.3.4・§22.1 の Array/Dictionary の構築・lookup/更新・容量/失敗契約を実装する。",
      "required": true,
      "depends_on": [
        "T06",
        "T12",
        "T13",
        "T16"
      ],
      "criterion_ids": [
        "C08"
      ],
      "acceptance": "規定のpublic API、重複key/equality、副作用・取得順、commit前後の責任、保持依存、容量の上限/複雑度、逆破棄順を満たす。未確定の追加API/公開storage ABIを固定しない。",
      "verification": "collectionの成功/失敗/0容量/overflow・同一/等値key・更新/削除・RHS/receiver順・active Loan競合・途中取得cleanupを意味/実装テストで確認し、内部storage選択を記す。"
    },
    {
      "id": "T18",
      "description": "§12.3.3・§13.4.1・§14.6・§22.1 の Stringify/比較・Iterator/Iterable と文字列補間を接続する。",
      "required": true,
      "depends_on": [
        "T06",
        "T14",
        "T16",
        "T17"
      ],
      "criterion_ids": [
        "C05",
        "C08",
        "C13"
      ],
      "acceptance": "associated Element の完全Type例外、所有iteratorの残要素破棄、range/Sliceの永久枯渇・外部Loan、独立owned stringの補間結果、比較witnessを実装する。",
      "verification": "forの単回iterate/next・early exit/continue・iterator cleanup、Copy/参照element、NaNとEquatableの差、補間評価順・UTF-8/escape/失敗時責任を検証する。"
    },
    {
      "id": "T19",
      "description": "§3.2.2/3.3・§13.5.7–9/13.6.1・§15.8 の object/Weak public操作と動的型・共有/排他取得を実装する。",
      "required": true,
      "depends_on": [
        "T06",
        "T12",
        "T14",
        "T15"
      ],
      "criterion_ids": [
        "C09"
      ],
      "acceptance": "makeObj/makeRc/makeArc/strong・Weak clone/downgrade/upgrade/cyclic のType/Origin/取得契約、concrete structのupcast/is、count不変のMove/borrowを意味/操作計画に表す。",
      "verification": "Core偽装/不正payload、Owned cyclic制約、view/identity保持、外部Loan、builderの単回callとcleanup後publication、無効変換・runtime Contract View拒否を意味テストで確認する。"
    },
    {
      "id": "T20",
      "description": "§22.1 の必須 Core 宣言catalogをすべて揃え、§3/4/7/8/13/14/17 と接続する。",
      "required": true,
      "depends_on": [
        "T18",
        "T19"
      ],
      "criterion_ids": [
        "C03",
        "C08",
        "C09",
        "C13"
      ],
      "acceptance": "現catalogの枠数を上限にせず、Weakと全object intrinsicsを含む本文表の全identity/shape/contractを実装する。合成/読み込みの同等性、Core唯一性とdefault aliasを保証する。",
      "verification": "CoreCatalog/CoreModel/IntrinsicCapability/EnumBinding/ContractBinding で全必須項目、missing/duplicate/incompatible/shadowing、Option/Result条件atom集合、Array/Dictionary/WeakのCopy/Owned依存を確認する。"
    },
    {
      "id": "T21",
      "description": "§18・§20.1–6・§21.3.4・A.1/A.3 の module参照、source再構築、意味情報保持とcache失効を実装する。",
      "required": true,
      "depends_on": [
        "T05",
        "T07",
        "T14",
        "T20"
      ],
      "criterion_ids": [
        "C10"
      ],
      "acceptance": "直接依存だけの名前公開、private定義環境、source/config identity、LangVersion/target入力、closed specialization/effect/Originを保持する。意味plan再利用とgeneration再構築を分ける。",
      "verification": "複数source/module、transitive非公開、alias/アクセス/定義追加削除、constant/body/Mod/設定変更での再検証を確認する。内部保存/再読込またはsource再構築で本文情報を検証し、Koto直列化をportable ABIと呼ばない。"
    },
    {
      "id": "T22",
      "description": "§6.5・§20.7全節・A.1/A.3 の逐次Mod host、provisional/final Binding、source追加と再生成を実装する。",
      "required": true,
      "depends_on": [
        "T03",
        "T05",
        "T21"
      ],
      "criterion_ids": [
        "C10"
      ],
      "acceptance": "Requires/RequiresAfter graph、Ordinal ready選択、一回実行、query snapshot、初回append後のBinding失効、合法containerへの追加のみ、生成source/provenanceを実装する。",
      "verification": "偽Modを使いgraphのduplicate/missing/cycle・後続生成解決・古いSymbol使用拒否・失敗descendant skip・取消/時間制限・古い出力除去・列挙順再現性を検証する。source保存/表示、graph/順序/時間/失敗理由を確認する。"
    },
    {
      "id": "T23",
      "description": "§21.1/21.2.1–4・A.5/A.14 の全確定layout・value metadata・borrow storageを実装する。",
      "required": true,
      "depends_on": [
        "T04",
        "T09",
        "T13",
        "T20"
      ],
      "criterion_ids": [
        "C09",
        "C12"
      ],
      "acceptance": "Kimigayo/C layout、fragment・base/tag位置、alignment安定順/論理順、tuple/enum/固定配列・0size、full Type key/CoreId、descriptor/context・borrow substitute addressを保持する。",
      "verification": "AggregateLayout/EmissionPlan を拡張し、packing16のC sizeof/alignof/offsetof比較、recursive/overflow、同size異alignment/破棄、token collision/retokenize、0size addressの属性前提を検証する。"
    },
    {
      "id": "T24",
      "description": "§21.4全節・§21.2.4・A.7/A.14 の一般checked lowering、slot ABI、CFG/cleanup、固定frame基盤を完成する。",
      "required": true,
      "depends_on": [
        "T13",
        "T14",
        "T15",
        "T23"
      ],
      "criterion_ids": [
        "C04",
        "C06",
        "C07",
        "C12"
      ],
      "acceptance": "scalar/aggregate/borrow/Unit/Neverの引数/結果、Copy元分離、normal returnだけの配送、部分責任、phi predecessor、source単回評価をtyped planから生成できる。",
      "verification": "EmissionPlan/Function/Result/Deferred/Aggregate/Reference の計画改変拒否とIR構造テストで結果storageの分離、支配・生存flag・途中call/cleanup、loop frame再利用・tailcall制限を確認する。"
    },
    {
      "id": "T25",
      "description": "§21.3・§21.4.6・A.12/A.14 のgeneric共有/特殊化generation、context/entry/adaptorと予算制御を実装する。",
      "required": true,
      "depends_on": [
        "T07",
        "T14",
        "T21",
        "T23",
        "T24"
      ],
      "criterion_ids": [
        "C11",
        "C12"
      ],
      "acceptance": "型/length/operation-context pair、正確な置換別scratch容量、必須generation limit、意味を変えない任意予算探索、選択の決定性を実装する。本文にある意味plan永続化境界を守る。",
      "verification": "予算0/既定/増加、mandatory specialization/関数参照、recursive finite/growing key、共有read family、capacity0/alignment16、同幅異型/異破棄を正負例・IR/nativeで確認する。B.7は採用した任意手法と規範要件を分ける。"
    },
    {
      "id": "T26",
      "description": "§21.2.3/21.2.5 と callable/object/Weak の実行・metadata・cleanup を LLVM/runtimeへ接続する。",
      "required": true,
      "depends_on": [
        "T19",
        "T23",
        "T24",
        "T25"
      ],
      "criterion_ids": [
        "C04",
        "C09",
        "C12"
      ],
      "acceptance": "concrete/erased closureのinline/heap/empty環境と間接call、obj/rc/arcのpayload+16/count/side table、動的型・最終release・Weak/循環構築の公開順を実装する。",
      "verification": "capture/呼出/最終破棄のnative観測、allocation/count overflow故障注入、Building upgrade None→Alive Some→expired None、view経由の一回cleanup、arc順序・race証明をA.8/A.14に対応付ける。"
    },
    {
      "id": "T27",
      "description": "aggregate/collection/Coreの全確定source操作を一般loweringへ接続する（§4・§6・§11・§14.6/14.8・§22.1）。",
      "required": true,
      "depends_on": [
        "T16",
        "T17",
        "T18",
        "T20",
        "T24",
        "T25"
      ],
      "criterion_ids": [
        "C04",
        "C06",
        "C07",
        "C08",
        "C12",
        "C13"
      ],
      "acceptance": "struct/enum/tuple/固定配列、field/index/Pattern分解、集約の選択結果・関数ABI、Property/constructor、Array/Dictionary/Slice/forを意味計画どおり実行する。",
      "verification": "native fixtureでCopy/NonCopy/nested/zero-size、partial construction/Move/置換、guard/早期transfer・残要素破棄、range/容量境界、順序・一回評価をO0/O2比較する。"
    },
    {
      "id": "T28",
      "description": "§3.1・§12–14・§17 の残るscalar/変換/string補間/制御フロー・Abort loweringを完成する。",
      "required": true,
      "depends_on": [
        "T10",
        "T15",
        "T18",
        "T24",
        "T27"
      ],
      "criterion_ids": [
        "C05",
        "C07",
        "C12"
      ],
      "acceptance": "runtime f64→f32、許可された整数↔float/literal fitting、一般同型・明示Semantics取得、heap string補間、全結果/guard/未到達の対応を完成し、禁止操作は最適化前に診断する。",
      "verification": "全整数方向とfloat境界の隣接値、NaN/∞/fraction/±0/subnormal、UTF-8/NUL、heap責任、短絡/非終了・hidden Unsupported をmanaged/IR/nativeで検証する。既存scalar fixtureを全件再利用する。"
    },
    {
      "id": "T29",
      "description": "§5・§21.1.6/21.5.3–4・§22.3 のunsafe raw pointer と LibraryImport C ABIを完成する。",
      "required": true,
      "depends_on": [
        "T10",
        "T12",
        "T23",
        "T24"
      ],
      "criterion_ids": [
        "C12",
        "C13"
      ],
      "acceptance": "null/比較/算術/index/変換の確定操作、unsafe境界、許可するC引数/結果、NativeLibraries/import/static・symbol衝突を一つの物理signatureで扱う。未確定pointer取得APIは創作しない。",
      "verification": "Clang生成のC fixtureと小整数/最大unsigned/FP混在/5引数以上を比較し、合法pointer境界・0変位・one-past、FP環境の保存変更復元、禁止signature/不正設定を検証する。Unsafe契約違反の結果を規定しない。"
    },
    {
      "id": "T30",
      "description": "§22.2/22.4–5・§11.3.2・§17.3 のstartup、static初回初期化/終了、console/確保/解放/Abortを仕上げる。",
      "required": true,
      "depends_on": [
        "T13",
        "T20",
        "T26",
        "T27",
        "T28",
        "T29"
      ],
      "criterion_ids": [
        "C06",
        "C13"
      ],
      "acceptance": "unique implicit/explicit main、static slotのInitializing cycle、逆成功初期化順shutdown、非終了/Abort、UTF-8出力と6 runtime操作・7 production APIの境界を満たす。",
      "verification": "Hello/StartupBinding/MinimalEmission、static side effect/再入/破棄後アクセス、空/日本語/NUL、partial/zero write・stdout欠落・stderr失敗・alloc/free failureのfault adapterを通常nativeで確認する。"
    },
    {
      "id": "T31",
      "description": "§20.8・§22.2.2 のLibrary inspectionとCLI/成果物/target/toolchainライフサイクルを完成する。",
      "required": true,
      "depends_on": [
        "T21",
        "T22",
        "T25",
        "T30"
      ],
      "criterion_ids": [
        "C10",
        "C14"
      ],
      "acceptance": "Applicationの新規生成/検証/buildと既存exe run、Libraryのentryなしpre-opt IR/object、schema3 pair公開・hash・旧成功無効化・子process取消/timeout・依存symbol検証を統合する。",
      "verification": "EmissionArtifacts/NativeToolchain/ToolchainResolver と test-cli.ps1 をmanaged DLLで実施する。LLVMなしemit、Library未使用body、空/複数project、失敗/混在公開、toolchain mismatch、run非再buildを確認する。"
    },
    {
      "id": "T32",
      "description": "§21.5.7・§20.8.5/20.8.7–8・A.14 のnative backend供給と検証harnessを完成する。",
      "required": true,
      "depends_on": [
        "T23",
        "T24",
        "T29",
        "T30"
      ],
      "criterion_ids": [
        "C12",
        "C13",
        "C14"
      ],
      "acceptance": "__chkstk/memcmp/memcpy/memmove/memset・kernel32 import・_fltused/unwind・/NODEFAULTLIB依存境界、実hashとcandidate/adoption、必要な新runtime/ABI fixtureを検証できる。",
      "verification": "pwsh -NoProfile -File backend/windows-x64/test-toolchain.ps1、test-artifact-paths.ps1、test-kernel32.ps1 -ToolchainRoot toolchain、build.ps1 -ToolchainRoot toolchain を順に実施する。各exit/報告・COFF/guard page/重なり/stack probe・O0/O2を確認する。"
    },
    {
      "id": "T33",
      "description": "全機能を統合し、Debug/Releaseのmanaged回帰と全必須native fixture/CLI/Library検証を完成する。",
      "required": true,
      "depends_on": [
        "T31",
        "T32"
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
        "C15"
      ],
      "acceptance": "全必須の正負例・A.1–A.15の検証表に有効な証拠があり、現在の入力に対してbuild/managed/IR/object/nativeを区別して全件合格する。未実行・0件・必須skipは完了にしない。",
      "verification": "後続作業で必要なら dotnet restore Kimigayo.slnx。Debug、Releaseそれぞれ直列に dotnet build Kimigayo.slnx -c <構成> --no-restore -v:minimal → dotnet test --project xUnitTest/xUnitTest.csproj -c <構成> --no-build --no-restore → pwsh -NoProfile -File backend/windows-x64/test-emission.ps1 -ToolchainRoot toolchain -Configuration <構成> → test-scalars.ps1 -ToolchainRoot toolchain → test-cli.ps1 -ToolchainRoot toolchain -Configuration <構成> を実施（後2本も同じpwsh起動形式）。dotnet Kimi/bin/<構成>/net10.0/Kimi.dll emit-llvm examples/Hello/Hello.kimiproj でIR/manifestの組を生成し、そのコマンドが報告した Hello.link.json のパスを -Manifest に渡して pwsh -NoProfile -File backend/windows-x64/test-manual-build.ps1 -Manifest <生成したmanifest> -ToolchainRoot toolchain を実施する。emission-fixtures の既存Hello fixtureは .ll のみなのでmanifest入力に流用しない。新規分野のnative harnessも同じ構成内で実行する。"
    },
    {
      "id": "T34",
      "description": "割り当て・再利用・generic resource/予算・生成コードの性能を規範要件と既存保証に照らして検証・改善する。",
      "required": true,
      "depends_on": [
        "T25",
        "T33"
      ],
      "criterion_ids": [
        "C11",
        "C12",
        "C15"
      ],
      "acceptance": "既存warm allocationと保持参照の保証、有限generation/固定frame/必須複雑度を維持し、不要なslot/転送/flag/割り当てを実用的に削減する。改善率や全経路無割り当てを捏造しない。",
      "verification": "AllocationMeasurement/ParserOptimization/Binding/Ownership系のallocationテストと、dotnet run --project Benchmark/Benchmark.csproj -c Release -- --filter '*BindingBenchmark*' '*OwnershipAnalysisBenchmark*' '*FrontEndBenchmark*' の通常managed workloadを基準と変更後で比較する。必要なgeneric/collection workloadとO2構造・code size/build timeも測定し、変更時は影響するC15検証を再実施する。"
    },
    {
      "id": "T35",
      "description": "SPEC.md/STATUS.md/必要なREADME・examplesを実装結果に合わせ、全規範項目とmilestoneを最終監査する。",
      "required": true,
      "depends_on": [
        "T33",
        "T34"
      ],
      "criterion_ids": [
        "C01",
        "C16"
      ],
      "acceptance": "仕様の決定事項を削除・緩和せず、許容された内部選択と実装/検証状況を記載する。全必須項目が実装と現在有効な証拠に結び付き、未確定/対象外との混同がない。",
      "verification": "T01の項目一覧を最終SPEC本文・コード・試験件数・成果物hashへ双方向照合する。未実装/Unsupportedの残りは仕様上の禁止/未確定範囲のみであること、STATUSの段階別記述と再現手順・リンクの整合を確認する。"
    }
  ],
  "milestones": [
    {
      "id": "M01",
      "description": "仕様網羅表とsource/構文・条件選択の基盤",
      "task_ids": [
        "T01",
        "T02",
        "T03"
      ],
      "acceptance": "C01 の全規範項目に担当taskと検証段階が付き、字句/構文・source identity・条件選択の正負例と往復検証が成功する。doc参照先の除外と本文決定事項の保持を確認できる。"
    },
    {
      "id": "M02",
      "description": "完全Type・名前解決・Contract・generic選択",
      "task_ids": [
        "T04",
        "T05",
        "T06",
        "T07"
      ],
      "acceptance": "Type/length/Origin schema、アクセス・fragment、証明/witness、候補選択とclosed specialization setの意味テストが成功する。未解決義務の保存/期限とcache失効を確認できる。"
    },
    {
      "id": "M03",
      "description": "関数・Property・constructor・演算の意味計画",
      "task_ids": [
        "T08",
        "T09",
        "T10"
      ],
      "acceptance": "関数/default/receiver、Property/constructor、scalar/型適応/代入の選択結果に型・評価順・取得・診断の根拠があり、正負例が期待どおりになる。"
    },
    {
      "id": "M04",
      "description": "一般所有権・Loan・効果・制御フロー",
      "task_ids": [
        "T11",
        "T12",
        "T13",
        "T14",
        "T15"
      ],
      "acceptance": "部分Move、保存/返却/reborrow、deinit/defer、callable効果、Pattern/refinement/requireの必須検証が成功し、義務を解消した計画だけをloweringへ渡せる。"
    },
    {
      "id": "M05",
      "description": "sequence・collection・object・Coreの契約",
      "task_ids": [
        "T16",
        "T17",
        "T18",
        "T19",
        "T20"
      ],
      "acceptance": "全必須Coreのidentity/shapeが揃い、配列/Slice/Dictionary/反復/補間、object/Weakの操作計画と寿命/失敗契約の正負例が成功する。本文のpublic APIに未対応枠がない。"
    },
    {
      "id": "M06",
      "description": "module/source再利用とModパイプライン",
      "task_ids": [
        "T21",
        "T22"
      ],
      "acceptance": "直接依存の名前境界と意味情報保持/失効を再構築試験で確認できる。Mod graph、一回実行、query/append/Binding境界、再生成/失敗/取消/診断・表示保存の統合検証が成功する。"
    },
    {
      "id": "M07",
      "description": "layout・一般ABI・generic generation",
      "task_ids": [
        "T23",
        "T24",
        "T25"
      ],
      "acceptance": "全対象layoutと責任付きABI/CFG、共有generic/entry/context/固定frameを検査済み計画から生成できる。C比較、IR検査、必須特殊化・予算/順序不変・limit拒否の検証が成功する。"
    },
    {
      "id": "M08",
      "description": "callable/object・aggregate/collection・scalar実行",
      "task_ids": [
        "T26",
        "T27",
        "T28"
      ],
      "acceptance": "各分野のsource操作が通常nativeで実行でき、O0/O2の結果・副作用/破棄順が一致する。Weak/arcの規定順序の根拠と、禁止・未到達/未使用操作の最適化前診断を確認できる。"
    },
    {
      "id": "M09",
      "description": "FFI・startup・static・runtime",
      "task_ids": [
        "T29",
        "T30"
      ],
      "acceptance": "許可C ABIとpointer境界、static初期化/終了・故障注入が成功する。Helloの14 byte出力・空stderr・exit 0、Abort exit 1とcleanupしない挙動を実アプリで確認できる。"
    },
    {
      "id": "M10",
      "description": "Library/CLI成果物とnative供給",
      "task_ids": [
        "T31",
        "T32"
      ],
      "acceptance": "Libraryのpre-opt IR/object検証とApplication emit/build/run、公開失敗/旧成功無効化、backend/kernel32の実hash/版・COFF/unwind/依存検証が成功する。NativeAOTは用いない。"
    },
    {
      "id": "M11",
      "description": "全必須検証・性能確認・文書整合",
      "task_ids": [
        "T33",
        "T34",
        "T35"
      ],
      "acceptance": "C01–C16をすべて満たす。Debug/Release managedと必要なO0/O2 native検証が有効な現入力に対応し、必須の未実装/未検証/skip・漏れが0件。SPEC/STATUSと全対応表がコード・証拠と一致する。"
    }
  ]
}
```
<!-- autoframe:end -->
