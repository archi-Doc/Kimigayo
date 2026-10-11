# コード規則アナライザー（Roslyn）の仕様と実装計画

日付：2026-10-11（2026-10-12 改訂：振る舞いの確かめ方、基準線の鍵、抑止の禁止、KR1002 の拡張の時期、KR3003 の対象、Report の完全性）。状態：方針は決定済み（決定事項は §9）。テストへの適用だけは R5 以降に判断する。実装は未着手。

調査基点：`019daa61`（`dev`）。本書の件数は、付録 B の構文調査による近似値である。A0 で、R3 の完了時点のコードを使って測り直す。

本書で「計画書」は [2026-10-10 Compiler Size Reduction and Single MIR Plan](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>) を、R0〜R9 はその段階を指す。

---

## 1. 概要

### 1.1 目的

設計の規則（AGENTS.md、COMPILER_ARCHITECTURE.md）は文章で書かれている。機械で検査しているのは、正規表現で書いた `ArchitectureRulesTest` の一部だけである。この状態には次の問題がある。

- **守られない。** エージェントは、ビルドが失敗する条件には従う。しかし、文章の規則は作業の途中で守らなくなる。
- **検査が遅く、選び忘れる。** `ArchitectureRulesTest` は Unit 検証で明示的に選んだときしか走らない。インナーループでは走らない。
- **検査が不正確。** 正規表現は、コメント中の名前、同名の別の型、`var` 経由の参照、using の別名で誤る。
- **予算が詰め込みを促す。** 物理行の予算の下で新しく書かれたコードほど、1 行に詰め込まれている（§2）。

本計画の目的は、規則を「ビルドが失敗する条件」に移すことである。新しいコードには最初から規則を適用し、既存の違反は増やさせない。

### 1.2 方針

- **アナライザー。** リポジトリ内に Roslyn アナライザー `Kimi.Analyzers` を置き、`Kimi.csproj` から参照する。違反は、インクリメンタルビルドを含むすべてのビルドでエラーになる。
- **規則の群（§4）。**
  - KR1xxx アーキテクチャ：層の依存、禁止メンバー、閉じた語彙、構文ノードをキーにした側表、`Supports*` ゲート、MIR ビルダーの構文形状による分岐、移行用の一時構造、型を問わない catch
  - KR2xxx 増築の兆候：引数、省略可能な引数、out、bool 引数、名前なしの bool リテラル、文の数、入れ子、論理演算の連鎖、ファイルの大きさ
  - KR3xxx 密度：行の長さ、条件演算子の入れ子、計算値の並列代入
  - KR9xxx 運用：基準線・構成・特例の検査と、抑止の禁止
  - 組み込み規則：Roslyn の未使用コードの規則（IDE0051、IDE0052、IDE0060）
- **基準線。** 既存の違反は基準線（ベースライン）に記録する。基準線は縮む方向にしか動かない。上限を超えている既存のメンバーやファイルは、それ以上悪化させるとエラーになる。
- **特例。** 本質的に長い関数には、理由と上限をコードに書いた特例を認める（§3.7）。
- **予算。** コンパイラーの予算を、物理行から論理量（コメントと空白を除くトークン数）に切り替える（§5）。

### 1.3 範囲

- **対象。** `src/Kimi`（`Kimi.csproj`）の C#。
- **旧経路の扱い。** 旧 Analysis と旧 Emission（D5 で凍結、R6 で削除）には、KR1 群と組み込み規則だけを適用する。KR1 群は現在のラチェットを引き継ぐため、組み込み規則は未使用コードを削除するためである。
- **対象外。**
  - `tests/xUnitTest`（適用するかどうかは R5 以降に判断する）
  - Benchmark と Playground
  - 自動修正（code fix）
  - 言語仕様とコンパイラーの振る舞いの変更

### 1.4 拘束条件

| 区分 | 内容 |
| --- | --- |
| 検証 | `Kimi.csproj` のビルド構成を変えるので、コンパイラーの変更として扱う。Unit 検証と最終の Session 検証を行う（AGENTS） |
| 振る舞い | 本計画の単位（A0〜A8）は、コンパイラーの振る舞いを変えない。確かめ方は §6.1。基準線に入れた違反を後で直す変更（例：KR1008 の catch）は、それぞれの単位で振る舞いを確かめる |
| D5 | 旧経路には機能を足さない。旧経路のコードの変更は、未使用コードの削除（D5 が許す削除）に限る。旧経路の既存の違反は、基準線に記録する |
| 承認 | 規則の構成の変更と、規則を緩める変更には、利用者の承認が要る（§3.6） |
| 性能 | §3.8 |

---

## 2. 背景（現状の計測）

### 2.1 密度（1,000 行あたり）

| 領域 | 行数 | トークン／行 | 140 字を超える行 | 条件演算子の入れ子 | 計算値の並列代入 | 名前なしの bool リテラル |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Mir/Build | 2,688 | **8.86** | **56.9** | **8.6** | **24.9** | **19.3** |
| Mir（Build 以外） | 1,791 | 6.85 | 45.8 | 3.9 | 0 | 3.9 |
| Binding | 56,554 | 6.57 | 42.9 | 5.3 | 0.05 | 7.9 |
| Parsing | 16,249 | 4.66 | 12.1 | 0.7 | 0 | 2.0 |

- 140 字は計測した時点の基準である。決定した上限（160 字）での件数は §4.3 に示す。
- 物理行の予算の下で最も新しく書かれた Mir/Build が、どの指標でも最も密である。計算値の並列代入（`var (a, b) = (式, 式)`）は、Binding の約 470 倍の頻度で現れる。
- 例：[MirBuilder.Expansions.cs](../../src/Kimi/Compiler/Mir/Build/MirBuilder.Expansions.cs) の 439 行目は、6 つの変数を 1 つの並列代入で宣言していて、1 行が 267 字ある。
- コンパイラー全体（`.ll.in` と生成データを除く）は 128,593 行・832,458 トークンで、1 行あたり 6.47 トークンである。

### 2.2 構造

- **長い関数。** 長い関数に分岐を 1 つずつ足すことが、肥大化の主な経路になっている。
  - 例：`BindCallCore`（301 文）、`TryCandidateCore`（274 文・901 行・22 引数・out 6）、`ValidateFixedConstruction`（20 引数）
- **長い論理演算の連鎖。** 5 項以上の `&&`／`||` の連鎖がある。最大は 25 項（`BodyLowering.Elements.cs:255`、`OwnershipBody.Comparisons.cs:361`）。
- **Mir/Build での構文形状による判断。** Mir/Build は、Binding が公開すべき事実を構文の形から決めている箇所を含む。
  - [MirBuilder.Match.cs](../../src/Kimi/Compiler/Mir/Build/MirBuilder.Match.cs) の 126 行目：束縛の可変性を `SyntaxFormKoto` から決めている。
  - [MirBuilder.Places.cs](../../src/Kimi/Compiler/Mir/Build/MirBuilder.Places.cs) の 146 行目：Tuple 要素の番号を、数値リテラルの綴りから取っている。
- **分布。** 旧経路を除く 3,826 メソッドの分布は次のとおり。
  - 引数：中央値 2、90% 点 4、99% 点 8。6 以下のメソッドは 97.6%
  - 文：中央値 5、90% 点 23、99% 点 71。60 以下のメソッドは 98.5%
  - 文が 45〜55 のメソッドは、65〜172 行（中央値 98 行）である。

規則ごとの件数は §4 に示す。

---

## 3. 仕組み

### 3.1 構成要素

| 要素 | 場所 | 役割 |
| --- | --- | --- |
| アナライザー | `src/Kimi.Analyzers/Kimi.Analyzers.csproj` | すべての規則と、基準線の照合 |
| 構成 | `src/Kimi/CodeRules.globalconfig` | 適用範囲、しきい値、層、語彙の上限、現在の段階、catch の境界、組み込み規則の重大度。`GlobalAnalyzerConfigFiles` として渡す。例は付録 C |
| 基準線 | `src/Kimi/CodeRules.baseline.tsv` | 既存の違反。1 行 1 件で、整列して置く。`AdditionalFiles` として渡す |
| 参照 | `src/Kimi/Kimi.csproj` | 次の 2 つを持つ<br>・`ProjectReference`（`OutputItemType="Analyzer"`、`ReferenceOutputAssembly="false"`、`PrivateAssets="all"`）<br>・`CompilerVisibleProperty`（`KimiRepositoryRoot`、`KimiCodeRulesMode`） |
| ルート | `Directory.Build.props` | `KimiRepositoryRoot = $(MSBuildThisFileDirectory)` |
| 属性 | `src/Kimi` の internal な属性 2 つ | `CodeRuleException`（特例、§3.7）と `Transitional`（移行用の一時構造、KR1007）。どちらも `[Conditional]` を付け、使用箇所を製品のメタデータに残さない |
| スクリプト | `scripts/code-rules.ps1` | Report、Prune、Accept、Initialize（§3.5） |
| テスト | `tests/xUnitTest/Tests/CodeRules*Test.cs` | 規則ごとの正例・反例・境界・基準線。メモリ内のコンパイルでアナライザーを走らせる。xUnitTest は `Kimi.Analyzers` を通常のアセンブリとして参照し、`Microsoft.CodeAnalysis.CSharp` も同じ 4.14.0 を参照する |

選択の理由：

- **netstandard2.0 と `Microsoft.CodeAnalysis.CSharp` 4.14.0。**
  - Roslyn のアナライザーは netstandard2.0 で作る必要がある。4.14.0 の参照は、SDK 10.0.401 のコンパイラー（5.9.0）で読み込める。
  - 必要なパッケージは、すべてローカルの NuGet キャッシュにある。
  - プロジェクトには `EnforceExtendedAnalyzerRules`、`Microsoft.CodeAnalysis.Analyzers`、StyleCop を付ける。
- **globalconfig。** Roslyn の標準の構成機構である。JSON のライブラリを、アナライザーの読み込み文脈に持ち込まずに済む。変更すると再コンパイルが起きる。
- **TSV の基準線。** 構文解析器が要らない。差分とマージが読みやすい。
- **影響範囲。** `PrivateAssets="all"` により、Benchmark、Playground、xUnitTest には伝わらない。伝わらないことを A2 で確かめる。
- **検証入力。** `scripts/verification-inputs.ps1` は Git で追跡されるファイルをすべて含むので、変更は要らない。

### 3.2 適用範囲

パスは `KimiRepositoryRoot` からの相対パス（区切りは `/`）で比べる。こうすると、LF の検証用 worktree でも同じ結果になる。

| 規則 | 対象 |
| --- | --- |
| KR1 群 | `src/Kimi/` 全体（旧経路を含む）。ただし次の 2 つは別に定める |
| KR1006 | `src/Kimi/Compiler/Mir/Build/` だけ |
| KR1008 | 境界（CLI のコマンド、LSP、テストの実行器）の外 |
| KR2 群、KR3 群 | `src/Kimi/` から旧経路（`Compiler/Analysis/`、`Compiler/Emission/`）を除いたもの |
| 組み込み規則 | `Kimi.csproj` 全体 |

生成コードはどの規則の対象にもしない（`ConfigureGeneratedCodeAnalysis(None)`）。`<auto-generated>` の見出しを持つファイル（`UnicodeIdentifierData.cs`）と、ソース生成器の出力がこれに当たる。

### 3.3 診断

- **ID。** `KR` に 4 桁の番号を付ける。リポジトリに同じ形の ID はない。
- **重大度。** 規則の違反（KR1〜KR3 群）と KR9003〜9005 は Error とする。KR9000 と KR9001 は Info とする。KR9002 は、既定の Enforce モードでは Warning、verify が使う Verify モードでは Error とする（§3.5）。
- **設定の固定。** すべての KR の記述子に `WellKnownDiagnosticTags.NotConfigurable` を付ける。重大度は、.editorconfig、globalconfig、NoWarn、`#pragma` では変わらない（§3.6）。
- **メッセージ。** 英語の 1 文で、何が起きたか、測定値と上限、直し方を書く。例：
  - `KR2001: 'TryCandidateCore' has 23 parameters (limit 6, baseline 22); pass the owner's plan or split the responsibility.`
  - `KR3003: Parallel assignment of computed values; declare one variable per statement.`
- **HelpLinkUri。** COMPILER_ARCHITECTURE のコード規則の節を指す。

### 3.4 基準線

**鍵。** 基準線の 1 件は `(規則, 範囲, 錨)` で識別する。

- **範囲。** 違反を含む、いちばん内側の名前付きの型の DocumentationCommentId（例：`T:Kimi.Compiler.Binding`）とする。型の外にある違反（ファイル先頭の行など）と KR2009 は、ファイルのパスを使う。
  - 範囲をパスではなく型にするのは、AGENTS が求める「責務によるファイルの分割」で鍵が変わらないようにするためである。
  - 基準線のファイルには、読む人のためにパスも書く。パスは鍵に含めない。
- **錨。**

  | 錨 | 識別するもの | 記録する数 | 使う規則 |
  | --- | --- | --- | --- |
  | `member:<DocumentationCommentId>` | メンバー | 測定値（KR1005 は 1） | KR1005、KR2001、2002、2004、2006、2007、2010 |
  | `local:<メンバーの ID>/<経路>` | ローカル関数 | 測定値 | 上と同じ |
  | `file` | ファイル | トークン数 | KR2009 |
  | `node:<ハッシュ>` | 違反を表す構文 | 件数 | KR1004、1006、1008、KR2003、2005、2008、KR3002、3003 |
  | `line:<ハッシュ>` | 行 | 件数 | KR3001 |

- **ローカル関数の経路。** 外側のローカル関数から順に名前を `/` でつなぎ、最後に自分の名前を置く。同じメンバーの中に同じ経路が 2 つ以上あるときは、ソースの順に `#1`、`#2` を付ける。これにより、別々のブロックにある同名のローカル関数を区別する。
- **ハッシュ。** 64 ビットの FNV-1a を使う。
  - `node`：下の表の構文のトークンを、空白 1 つで連結した文字列（トリビアを除く）から計算する。改行コードと整形の影響を受けない。
  - `line`：行の文字列から、CR と行末の空白を除いて計算する。
- **node に使う構文。** 規則ごとに、違反を表す最小の構文を使う。

  | 規則 | 構文 |
  | --- | --- |
  | KR1004 | コレクションの型の構文（宣言または生成） |
  | KR1006 | 判定を行う最小の構文：`is` または `as` の式、パターン（switch の case と arm を含む）、`KotoKind` との比較の式 |
  | KR1008 | catch 句から本体のブロックを除いたもの（`catch`、`catch (Exception)`、`catch (Exception e)`） |
  | KR2003 | 引数の宣言 |
  | KR2005 | bool リテラルを含む、呼び出しまたは生成の式 |
  | KR2008 | 連鎖の全体 |
  | KR3002 | いちばん外側の条件演算子 |
   | KR3003 | 新規ローカル変数だけの分解宣言の全体 |

- **KR1 群の禁止は基準線を持たない。** KR1001〜1003 と KR1007 は 0 件から始め、違反は常にエラーにする。禁止の範囲を広げるのは、広げた範囲の違反が 0 件になってからとする（§6.4）。

**判定。**

| 錨の種類 | 通す条件 | 違反したとき |
| --- | --- | --- |
| 件数（`node`、`line`） | 出現数が基準線の件数以下 | その鍵の出現をすべて報告する。同じ文字列の出現どうしは区別できないためである |
| 測定値（`member`、`local`、`file`） | 測定値が「上限」と「基準線の値」の大きい方以下 | 両方の数を示して報告する |
| 基準線にない鍵 | 上限以下 | 通常どおり報告する |

**古い項目。** 次の場合、基準線ファイルの位置に KR9002 を出す。
- 基準線の件数が出現数を上回っている。
- 基準線の値が測定値を上回っている。

KR9002 は、通常のビルド（Enforce モード）では Warning なので、インナーループは止まらない。verify（Verify モード）では Error なので、違反を直すコミットは、同じコミットで基準線も縮めることになる。NotConfigurable の診断はコンパイラーの warnaserror では昇格しない（試験で確認）ので、昇格には頼らずモードで決める。

**この設計がもたらす挙動。**
- 長い行や入れ子の条件演算子を編集すると、ハッシュが変わる。そのため、触れた箇所はその場で直すことになる。
- 同じ型の中での移動（partial のファイルの間を含む）では、鍵は変わらない。別の型への移動は責務の移動なので、移動先では新しい違反として扱い（直すか、承認を受ける）、移動元の項目は古い項目になる。
- 上限を超えたメンバーの名前や引数を変えると、鍵が変わる。そのメンバーは上限を満たすか、承認を受ける必要がある。上限を超えたメソッドに引数を足すことは、この規則が止めたい操作そのものである。

### 3.5 モードとスクリプト

- **モード（`KimiCodeRulesMode`）。**

  | モード | 使う場面 | 動作 |
  | --- | --- | --- |
  | `Enforce`（既定） | 通常のビルド | 違反は Error、古い項目（KR9002）は Warning |
  | `Verify` | `verify.ps1` | 違反も古い項目も Error |
  | `Report` | `code-rules.ps1` | 違反の代わりに、すべての出現について KR9001（Info）を出す。KR9001 は、規則・範囲・錨・数・パスを診断のプロパティとして持つ。コンパイルの最後に、完了の記録 KR9000（Info）を 1 件出す。KR9002〜9005 の検査はそのまま行う |

- **モードの固定。** `verify.ps1` は `-p:KimiCodeRulesMode=Verify` を明示的に渡す。MSBuild は環境変数をプロパティとして読むので、環境変数による切り替えを、コマンドラインの指定で上書きして防ぐ。
- **Report の出力。** `-p:ErrorLog=<file>,version=2.1` で SARIF に書き出し、スクリプトがそれを読む。鍵を計算するのはアナライザーだけである。
- **Report の完全性。** スクリプトは、新しく取得した完全な解析結果だけを使う。
  - ビルドは毎回 `--no-incremental` で行い、SARIF は実行ごとに新しいパスに書く。インクリメンタルビルドではコンパイルが省略され、解析結果が出ないことがあるためである。
  - 次のどれかに当たれば、結果を使わずに失敗させる。
    - ビルドが失敗した
    - KR9000 がちょうど 1 件ではない
    - KR9000 が記録した構成と基準線のチェックサムが、現在のファイルと一致しない
    - アナライザーの例外（AD0001）がある
    - ビルドの前後でソースが変わった
  - Prune、Accept、Initialize は、この条件を満たした結果だけで基準線を書き換える。不完全な結果で項目を削ると、残っている違反が基準線から消えてしまうからである。
- **スクリプトのモード（`scripts/code-rules.ps1`）。**

  | モード | 動作 |
  | --- | --- |
  | `-Report` | 規則と領域ごとの件数、特例と移行用の一時構造の一覧を、証跡に書く |
  | `-Prune` | 基準線を「基準線」と「観測値」の小さい方に置き換える。項目は足さない |
  | `-Accept -Rule <id> -Path <path>` | 観測した出現を基準線に足す。承認が要る |
  | `-Initialize` | 規則を導入する単位でだけ使う |

- **Report を使う時期。** ステージの終了時と、マイルストーンの完了時の監査で使う。利用者は、その一覧で基準線、特例、移行用の一時構造を見直す。

### 3.6 承認と抑止の禁止

- **承認が要る変更。** 規則の構成の変更と、規則を緩める変更には、利用者の承認が要る。対象は次のとおり。
  - `CodeRules.globalconfig` のすべての変更。段階を進める変更と、上限を下げる変更も含む。差分から「緩めるかどうか」を機械で判定しないためである
  - 基準線への項目の追加と、値の引き上げ
  - 特例の属性の追加と、`Limit` の引き上げ
  - `Transitional` の段階を後ろへずらす変更
- **承認の記録と検査。** 承認は、コミットメッセージのトレーラー `Code-Rules-Approved: <承認者> <日付>: <理由>` で記録する。`scripts/verify-commit.ps1` は差分を見て、承認が要る変更にトレーラーがなければ失敗させる。
- **本計画の単位での作成。** A2〜A7 での構成と基準線の作成は、本書の採用（2026-10-11）をもって承認されたものとする。トレーラーには本書と単位を記す。
- **承認が要らない変更。** 次の、基準線や特例を縮める変更には要らない。
  - Prune
  - `Limit` を下げる変更
  - `Transitional` の段階を前へずらす変更
- **抑止の禁止。** 規則の違反を、ソースや構成で消せないようにする。仕組みは 3 段である。
  1. **NotConfigurable。** すべての KR の記述子に付ける。試験（Roslyn 4.14、`CompilationWithAnalyzers`）では、これを付けた診断は、ID なしの `#pragma warning disable`、ID 付きの `#pragma`、NoWarn（個別の重大度の設定）では消えなかった。ただし `[SuppressMessage]` では消えた。
  2. **KR9004。** `src/Kimi` の中の次のものを Error にする。
     - ID を指定しない `#pragma warning disable`（KR 以外の警告もまとめて隠すため。現状 0 件）
     - KR の ID、または組み込み規則（IDE0051、IDE0052、IDE0060）の ID を指定した `#pragma warning disable`、`SuppressMessage`、`UnconditionalSuppressMessage`（アセンブリに付けるものを含む）
  3. **verify の検査。** Verify モードのビルドは SARIF のエラーログも書く。そこに、抑止された KR の診断か組み込み規則の診断が 1 件でもあれば、`verify.ps1` を失敗させる。KR9004 自身を `[SuppressMessage]` で消しても、抑止はここで見つかる。この検査は、「csc はソースで抑止された診断も、抑止の情報付きでエラーログに書く」ことを前提にする。この前提は未確認で、A2 の回帰テストで確かめる。
  - **組み込み規則の重大度。** IDE0051、IDE0052、IDE0060 は NotConfigurable にできない。`CodeRulesConfigurationTest` は、これらの重大度が `CodeRules.globalconfig` の外（.editorconfig、他の .globalconfig、NoWarn）で変えられていないことを確かめる。KR の ID は NotConfigurable なので、この検査の対象にしない。
  - **回帰テスト（A2）。** 抑止の手段ごとに、違反が消えないこと、または KR9004 か verify の検査で見つかることを確かめる。手段は、ID なしと ID 付きの pragma、メンバー・型・アセンブリの `SuppressMessage`、NoWarn、.editorconfig と globalconfig の重大度、KR9004 を名指しした `SuppressMessage` である。`CompilationWithAnalyzers` だけでなく、実際のビルド（csc）でも確かめる。
- **回避の禁止。** 規則の意図に反する回避は、レビューで差し戻す。例：
  - 機械的な分割（`Part1`／`Part2`）
  - 引数を詰めただけの型
  - 基準線を抜けるための名前の変更
  - `Transitional` の属性だけを外す

### 3.7 特例

字句解析器のホットループのように、本質的に長い関数がある。短くすると、かえって保守性や性能が下がることがある。一方で、「本質的に長い関数」と「機能を足し続けて長くなった関数」は、どの指標でも機械的には区別できない。区別は設計の判断なので、判断した理由をコードに残す特例として扱う。

```csharp
[CodeRuleException("KR2006", Limit = 281,
    Reason = "One scanning loop per logical line; cursor and token state stay in the hot loop (measured: artifacts/benchmarks/...)")]
private void ReadLogicalLine()
```

- **対象。** 特例にできるのは、KR2006（文の数）と KR2007（入れ子の深さ）だけである。
  - 引数とファイルの大きさは、性能を損なわずに直せる（構造を変える、partial に分ける）ので、特例の対象にしない。
- **効果。** その規則の上限を、`Limit` に置き換える。`Limit` までは大きくできる。超えるには、属性を書き換えて承認を受ける。特例を付けたメンバーは、その規則について基準線に載せない（特例と基準線の両方には載せない）。
- **理由。** `Reason` は空にできない。
  - 正当な理由：ホットループの状態機械で、分割すると状態を `ref` やフィールドに移す必要がある（性能を理由にする場合は計測を添える）。または、閉じた語彙に対する平らな振り分けで、switch 式の腕にできない。
  - 正当でない理由：機能ごとの分岐が積み重なった。
- **検査。** 次の誤りは KR9005 とする。
  - 対象外の規則を指定した
  - 理由が空である
  - `Limit` が通常の上限以下である（特例が要らない）
  - 測定値が `Limit` を超えた
  - 同じメンバーが、同じ規則で基準線にも載っている
- **既存の関数。** 上限を超えている既存のメソッドは、基準線に入れる。特例に切り替えるかどうかは、その関数を大きくする必要が生じたときに、1 件ずつ判断する。

### 3.8 性能要件

- **基準。** `Kimi.csproj` を Release で非インクリメンタルにビルドしたとき、KR アナライザーの実行時間の合計が csc の時間の 10% 以下であること。実行時間は `-p:ReportAnalyzer=true` で測る。
- **計測。** A0 で導入前を測り、各単位で記録する。
- **実装の方針。**
  - `EnableConcurrentExecution` を使う。
  - KR3 群は、構文木の動作だけで行う。
  - KR2 群は、構文ノードの動作で行う。記号を引くのは、違反を見つけたときだけにする。
  - KR1 群は、依存元の層のファイルでだけ記号を解決する。大きな層では、まず名前で絞り込む。
  - 構成と基準線は、コンパイルごとに 1 回だけ解析する。

### 3.9 既存の検査との関係

規則の所有者は 1 つにする。規則をアナライザーへ移す単位では、同じ単位で元のテストを削除する。そのとき、テストが検出していた形をアナライザーも検出することを、禁止された形を写したテスト用のソースで示す。

| 既存の検査 | 処置 | 単位 |
| --- | --- | --- |
| `MirReadsNoBindingInternals`、`OnlyTheMirBuilderReadsSyntaxAndHir` | KR1001、KR1002 へ移す | A3 |
| `VocabularyDoesNotGrow` | KR1003 へ移す | A3 |
| `SourcePatternDoesNotGrow` のうち、上限 0 の 3 行（Parsing からの Binding の呼び出し、Hover の Binding 読み取り、旧い子ノード走査） | KR1001、KR1002 へ移す | A3 |
| `SourcePatternDoesNotGrow` のうち、「Koto-keyed dictionaries in Binding」と「Supports* gates」 | KR1004、KR1005 へ移す | A4 |
| `SourcePatternDoesNotGrow` のその他（Emission の失敗文字列、tinyhand の行、Operator Koto のサブクラス、Koto の意味自動プロパティ、テスト側のラチェット） | `ArchitectureRulesTest` に残す。どれも、除く段階で消える移行用のラチェットか、C# 以外の規則である | — |
| `CompilerSizeBudgetTest` | 論理量に切り替える（§5） | A1 |
| StyleCop | 変えない。行の長さや入れ子の規則は持たないので、重複しない | — |

`ArchitectureRulesTest` と `CompilerSizeBudgetTest` は残る。AGENTS が定める、コンパイラーの Unit 検証の必須の検査であることも変わらない。

---

## 4. 規則

件数は基点の近似値である。規則群の適用範囲は §3.2 による。

### 4.1 KR1xxx アーキテクチャ

| ID | 規則 | 基準線 | 現状 |
| --- | --- | --- | ---: |
| KR1001 | 層の依存 | なし | 0 |
| KR1002 | 禁止メンバー | なし | 0 |
| KR1003 | 閉じた語彙 | なし（上限は構成） | 0 |
| KR1004 | 構文ノードをキーにした側表 | 件数 | 152（うち旧経路 44） |
| KR1005 | `Supports*` ゲート | 測定値 | 13（うち旧経路 6） |
| KR1006 | MIR ビルダーの構文形状による分岐 | 件数 | 型テスト 66 以下、`KotoKind` 25 以下 |
| KR1007 | 移行用の一時構造 | なし | 0 |
| KR1008 | 型を問わない catch | 件数 | 約 11（うち旧経路 3） |

**KR1001 層の依存。**

- **層の決め方。** 名前空間は層と一致しない。例えば `KotoHelper` は Parsing のフォルダーにあるが、名前空間は `Kimi.Compiler` である。そのため、層は宣言の置かれたファイルのパスで決める。最も長く一致する接頭辞が勝つ。

  | 層 | パス（`src/Kimi/` から） |
  | --- | --- |
  | syntax | `Compiler/Lexing/`、`Compiler/Parsing/` |
  | binding | `Compiler/Binding/` |
  | compiler_root | `Compiler/` の直下のファイル |
  | legacy | `Compiler/Analysis/`、`Compiler/Emission/` |
  | mir_build | `Compiler/Mir/Build/` |
  | mir | `Compiler/Mir/`（Build 以外） |
  | hover | `Checking/Hover/` |

- **規則。** A3 では、現在のテストが強制している内容だけを写す。新しい制限は加えない。

  | 依存元 | 禁止 | 許可する例外 |
  | --- | --- | --- |
  | syntax | `Kimi.Compiler.Binding` 型 | — |
  | mir_build | binding、compiler_root、legacy の層 | 公開された Binding の型（基点で 46 型。現在の `PublishedBindingTypes`） |
  | mir | binding、compiler_root、legacy、syntax の層 | 公開型から、HIR と Plan の型（`Hir*`、`PlanRow`、`CallPlan`、`ExpansionPlan`、`BoundMatch`、`BoundClosure`、`DefaultDecl`）を除いたもの |
  | hover | `Kimi.Compiler.Binding` 型 | — |

  - syntax 層から binding 層全体を禁止するのは R6 の後である。R6 までは、Koto の意味スロットが Binding の型（`BoundType` など）を持つためである。
  - `SemanticsKind` は `src/Kimi/Misc` にあり、どの層にも属さない。そのため、許可の指定は要らない。
- **対象とする参照。**
  - 型に解決されるすべての名前。型引数、using の別名、属性、`typeof`、`nameof` を含む。
  - 禁止された型のメンバーに解決される名前。これにより、`var` 経由のアクセスも検出する。
- **partial の型。** 宣言のどれかが禁止された層にあり、かつ許可されていない型は、違反とする。
- **Backend 層。** R5 で Backend が作られたときに、層と規則を構成に加える。

**KR1002 禁止メンバー。** 指定したメンバーの参照を禁止する。全域の項目は、宣言も禁止する。override されたメンバーは、元の宣言に遡って照合する。

| 依存元 | 禁止するメンバー |
| --- | --- |
| mir_build、mir | `KotoHelper.UnwrapParentheses`、`KotoHelper.ResolveTransferTarget`、`Koto.Parent`、Koto の意味スロット 27 種（`Koto` とその派生型のメンバー。現在の `ArchitectureRulesTest.SemanticSlots`） |
| mir | `BindingSymbol.Declaration` |
| hover | Koto の意味スロット |
| 全域 | `VisitChildrenCore`、`GetChildNodes`、`ReplaceChildCore`、`ReplaceInList` |

**KR1003 閉じた語彙。**
- 構成で指定した列挙型のメンバー数が上限を超えたら、列挙型の宣言の位置でエラーにする。
- 初期値は、`VocabularyDoesNotGrow` の 19 型である。`IntrinsicId` の上限 34 は、30 に一時的な 4 つを加えたもので、R8 で 30 に戻す。
- 構成にある型が存在しなくなったら（R6 で旧語彙が消える）、KR9002 を出して構成から外させる。

**KR1004 構文ノードをキーにした側表。**
- syntax 層の外で、キーの型が `Koto` の派生型であるコレクションを宣言または生成したら、違反とする。
- 対象のコレクション：`IDictionary<,>`、`IReadOnlyDictionary<,>`、`ISet<>` を実装する型と、`ConditionalWeakTable<,>`。
- 直し方：`SyntaxId` で索引する列（`HirTables`、または所有者の表）に置く。
- Binding の分は、R7 の終了時に 0 にする（計画書 §14-2）。

**KR1005 `Supports*` ゲート。**
- bool を返す `Supports*` という名前のメソッドまたはプロパティの宣言を、違反とする。
- 直し方：能力表と一般規則で扱う。それができなければ、位置付きの Unsupported にする（計画書 §6.2）。

**KR1006 MIR ビルダーの構文形状による分岐。**
- 次のものを違反とする。
  - `Koto` の派生型に対する型テスト：`is`、`as`、型・宣言・再帰のパターン、switch の case と arm のパターン
  - `KotoKind` の比較とパターン
- キャストは対象外とする。Op が規則を選んだ後で、子ノードを読むためのものだからである。
- 直し方：`HirOp`、`HirRes`、Plan で決める。事実が欠けていれば、Binding が公開する。
- Mir に基準線を置かない原則（§9-5）の例外とする。直すには Binding が事実を公開する必要があり、1 つの単位には収まらないためである。基準線は R6 の終了時までに 0 にする。

**KR1007 移行用の一時構造。**
- 移行のために一時的に置く型やメンバーには、`[Transitional("R8", Reason = "...")]` を付け、取り除く段階を書く。
- 構成の `kimi_code_rules.stage` には、現在の段階を書く。それより前の段階で取り除くはずの項目が残っていれば、エラーにする。段階は R0、R1、R2a、R2b、R3、R4、…、R9 の順に比べる。
- `stage` は、ステージに入るときに進める（構成の変更なので、承認が要る）。
- 最初に印を付ける対象：
  - Koto の転送プロパティ（R6）
  - `KIMI_PIPELINE` の切替（R6）
  - HIR の publisher（R7）
  - `LegacyRuntimeHelpers`（R8）
  - 一時的な intrinsic の `Capacity`、`FormatCapacity`、`FormatHint`、`FormatFinish`（R8）
- 印の付け忘れは機械では検出できない。レビューで確かめる。

**KR1008 型を問わない catch。**
- `catch (Exception)` と型のない `catch` を、境界（CLI のコマンド、LSP、テストの実行器）の外で禁止する。`when` 句で絞り込んだ catch は対象外とする。
- 理由：コンパイラーの不具合を握りつぶさず、InternalInvariant や例外として表に出すため。
- 既存の約 11 件（Documentation 4、Emission 3、Core 2、Hover 2）は基準線に入れる。挙動が変わりうるので、1 件ずつ調べてから直す。

### 4.2 KR2xxx 増築の兆候

| ID | 規則 | しきい値 | 基準線 | 現状 |
| --- | --- | ---: | --- | ---: |
| KR2001 | 引数が多い | 6 | 測定値 | 90 |
| KR2002 | out 引数が複数 | 1 | 測定値 | 46 |
| KR2003 | 失敗の文字列を out で返す | 0 | 件数 | 21 以下 |
| KR2004 | bool 引数が複数 | 1 | 測定値 | 49 |
| KR2005 | bool リテラルを名前なしで渡す | 0 | 件数 | 653 以下 |
| KR2006 | 文が多い | 60（特例あり） | 測定値 | 57 |
| KR2007 | 入れ子が深い | 4（特例あり） | 測定値 | 53 |
| KR2008 | 論理演算の連鎖が長い | 4 項 | 件数 | 185 |
| KR2009 | ファイルが大きい | 5,000 トークン | 測定値 | 25 |
| KR2010 | 省略可能な引数が多い | 2 | 測定値 | 47 |

- **基準線の扱い。** 基準線に載ったメンバーとファイルは、それ以上悪化させられない。改善したら、Prune で基準線を下げる。
- **対象のメンバー。** 引数の規則（KR2001、2002、2004、2010）の対象は、次の宣言である。
  - 対象：メソッド、コンストラクター、ローカル関数、演算子、インデクサー、デリゲート。拡張メソッドの `this` も数える。
  - 対象外：プライマリーコンストラクター（record など）。データの宣言だからである。

- **KR2001 引数。**
  - 定義：引数の数。
  - 直し方：呼び出し側がすでに持っている所有者のオブジェクト（Plan や文脈）を渡すか、責務を分ける。
- **KR2002 out。**
  - 定義：out 引数が 2 つ以上あるもの。
  - 直し方：結果を struct や record で返す。
- **KR2003 失敗の文字列。**
  - 定義：名前に failure、error、reason、message、problem のいずれかを含む（大文字小文字は区別しない）、`out string` または `out string?` の引数。
  - 直し方：失敗した地点で、型付きの Problem、Unsupported、InternalInvariant を記録する（計画書 §7）。
- **KR2004 bool 引数。**
  - 定義：値渡しの `bool` または `bool?` の引数が 2 つ以上あるもの。
  - 直し方：意味ごとにメソッドを分けるか、所有者が定義する列挙型にする。
- **KR2005 名前なしの bool リテラル。**
  - 定義：このコンパイルの中で宣言されたメソッド、コンストラクター、ローカル関数に、`true` や `false` を名前なしで渡すこと。
  - 直し方：引数に名前を付ける。そのフラグが振る舞いを選んでいるなら、メソッドを分ける。
- **KR2006 文の数。**
  - 定義：本体の文の数。ブロックは数えない。ローカル関数は別に数える。ラムダは、所属するメンバーに含める。
- **KR2007 入れ子。**
  - 定義：制御文（if、for、foreach、while、do、switch、try、using、lock）の入れ子の深さ。else-if の連鎖は 1 段と数える。ラムダとローカル関数の中は、0 から数え直す。
- **KR2008 論理演算の連鎖。**
  - 定義：`&&`、`||`、パターンの `and`、パターンの `or` のうち、同じ演算子が 5 項以上つながったもの。括弧で囲まれた部分は、別の連鎖として数える。
  - 直し方：節を足し続ける代わりに、型が所有する分類（表やフラグ）に、名前付きの述語を置く。
- **KR2009 ファイルの大きさ。**
  - 定義：ファイルごとのトークン数（トリビアを除く）。partial のクラスは、ファイルごとに数える。
- **KR2010 省略可能な引数。**
  - 定義：既定値を持つ引数の数。`params` は数えない。
  - 理由：既定値付きの引数を末尾に足すことは、呼び出し側を直さずに済む最も安い増築だからである。

### 4.3 KR3xxx 密度

| ID | 規則 | しきい値 | 基準線 | 現状 |
| --- | --- | ---: | --- | ---: |
| KR3001 | 行が長い | 160 字 | 件数 | 約 1,920 |
| KR3002 | 条件演算子の入れ子 | 0 | 件数 | 423 |
| KR3003 | 計算値の並列代入 | 0 | 件数 | 70（うち Mir/Build 67） |

- **KR3001 行の長さ。**
  - 定義：CR を除いた物理行の長さ（UTF-16 の符号単位）。コメントも数える。
  - 例外：行全体が複数行の文字列リテラル（verbatim、raw、interpolated raw）の中にある行。埋め込んだ IR や Kimigayo のコードのためである。
- **KR3002 条件演算子の入れ子。**
  - 定義：条件演算子 `?:` の条件または分岐の中に、別の条件演算子があるもの。ラムダ、ローカル関数、匿名メソッドを挟む場合は数えない。
  - 直し方：if と else、switch 式、または名前付きのローカル変数にする。
- **KR3003 計算値の並列代入。**
  - 対象：すべての要素が新規ローカル変数である `var (a, b) = (e1, e2)` の分解宣言だけ。右辺は 2 要素以上のタプルリテラルで、単純でない要素を含むものとする。単純な要素とは、名前、リテラル、`this`、`default`、`null`、およびそれらのメンバーアクセスの連鎖である。
  - 既存変数への代入は対象外とする。捕捉や直接の依存がなくても、例外や `ref` の別名により、順次代入への分割で意味が変わるためである（原則 1）。`var (i, n) = (0, xs.Length)` は、要素が単純なので違反にしない。
  - 直し方：左から順に、新規変数ごとに 1 つの宣言にする。右辺の評価順を保ち、既存変数への書き込みを途中に挟まない。例えば `var (a, b) = (x, y);` は 13 トークン、`var a = x; var b = y;` は 10 トークンである。

### 4.4 KR9xxx 運用

| ID | 内容 | 重大度 |
| --- | --- | --- |
| KR9000 | Report モードでの完了の記録（解析した構文木の数、構成と基準線のチェックサム） | Info |
| KR9001 | Report モードでの出現の記録 | Info |
| KR9002 | 古くなった基準線または構成の項目 | Enforce モードでは Warning、Verify モードでは Error |
| KR9003 | 構成または基準線の書式の誤り | Error |
| KR9004 | KR 規則の抑止 | Error |
| KR9005 | 特例の誤り | Error |

### 4.5 組み込み規則

- Roslyn に組み込みの次の規則を、エラーにする。
  - IDE0051：使われない private メンバー
  - IDE0052：読まれない private メンバー
  - IDE0060：使われない引数
- 設定：重大度は `CodeRules.globalconfig` に書き、`Kimi.csproj` に `EnforceCodeStyleInBuild` を付ける。IDE0060 は既定の設定（すべてのメソッドが対象）で使う。
- 基準線：組み込み規則は KR の基準線を使えない。既存の違反は、すべて消す。消すことがそのままコードの削減になる。旧経路の分も、D5 が許す削除として消す。
- 件数：A0 で試行ビルドをして測る。件数が多い場合は、利用者に相談する。
- 試行ビルドで確かめること：`EnforceCodeStyleInBuild` を付けると、.editorconfig の既存の設定もビルドで報告される。例えば `csharp_style_namespace_declarations=file_scoped:warning` は警告として報告され、verify を失敗させうる。

### 4.6 検討して採らなかった規則

| 候補 | 採らない理由 |
| --- | --- |
| 名前の綴りによる識別（`.Name == "capacity"` など） | 検出できるのは約 12 件だけで、R8 の lang item が置き換える |
| 書き換え可能な static フィールド | 現状 0 件 |
| 循環的複雑度・認知的複雑度 | 文の数、入れ子、論理演算の連鎖と重なる |
| クラスの大きさ（`Binding` の partial クラス全体） | 予算と R7 で管理する |

---

## 5. 論理量の予算

- **問題。** 物理行の予算には、次の 2 つの問題がある。
  - 1 行への詰め込みに報いてしまう（§2.1）。
  - コメントと空行が予算を消費する。COMPILER_ARCHITECTURE は「説明のコメントを消すことは削減ではない」と書いているが、指標はそれに報いてしまう。
- **測り方。**
  - C# の領域は、トリビアを除いたトークン数で測る。コメント、空行、整形は数えない。
  - `.ll.in` は行数のまま測る（LlvmTemplates の領域）。
  - `<auto-generated>` の見出しを持つファイル（`UnicodeIdentifierData.cs`、1,687 行）は予算から除く。
- **実装。** `CompilerSizeBudgetTest` を、Roslyn の字句解析器を使う形に改める。全体を集計する検査なので、アナライザーには入れない。字句解析にかかる時間は 1 秒程度である。
- **換算。** A1 で、上限を次のように置き換える。換算によって状態が変わる領域は出ない。
  - 各領域の上限：行数の上限に、その領域の実測のトークン／行を掛ける。Helper は、行数の上限から生成データの 1,687 行を引いてから換算する。
  - 合計の上限：C# の領域のトークンだけで持つ。`.ll.in` は、LlvmTemplates の行数の上限で別に検査する。
  - 計画書の残りの段階の上限（目標 84,350 行を含む）：`.ll.in` と生成データの分を引き、A1 の時点でのコンパイラー全体のトークン／行で換算する。換算した値は COMPILER_ARCHITECTURE に記録する。計画書の本文は変えない。
- **報告。** コミットメッセージには、領域ごとのトークンの純増減を書く。行数は参考として併記する。

---

## 6. 実装計画

### 6.1 共通

- **時期。** R3 の完了後に着手し、R4 の前に A0〜A8 を終える。
  - R4 では、MIR 解析のコードが最も多く書かれる。そこに最初から規則を効かせるためである。
- **取り込み。** A0 の着手時に、利用者の指示で本書を取り込む。`draft/INTEGRATED.md` に記録し、COMPILER_ARCHITECTURE にコード規則の節を置く。
- **作業のしかた。** 作業は専用の worktree で行う。検証とコミットは、`dev` の上で直列に行う（AGENTS）。
- **検証。** A0 は文書と計測だけなので、差分と参照の確認で足りる（AGENTS）。A1〜A8 は次のとおり。
  - Unit：関連する `CodeRules*Test`、`CompilerSizeBudgetTest`、`ArchitectureRulesTest`
  - 単位の最後：Session（ソリューション全体のビルドを含む）
  - 振る舞いを変えないことの確認：すべての単位で、VERIFICATION の「振る舞いを変えないコミット」の手順により、直前の単位と次のものが一致することを示す。
    - 診断のスナップショット
    - 意味のスナップショット（HIR と MIR を含む）
    - 生成した IR
  - `Kimi.dll` のバイト一致は使わない。生成される `AssemblyInformationalVersion` にコミット ID が入り（`0.1.1+<コミット>`）、PDB のパスも作業ツリーごとに違うので、振る舞いが同じでもコミットごとに DLL は変わる。決定的ビルドが保証するのは、入力が同じ場合の一致だけである。
  - 性能：KR アナライザーの実行時間を記録する（§3.8）
- **証跡。** `artifacts/verify/<日時>-code-rules-<単位>` に置く。
- **コミット。** 規則の数と、規則ごとの基準線の件数を書く。

### 6.2 単位

| 単位 | 内容 | 完了条件 | 規模（概算） |
| --- | --- | --- | --- |
| **A0 計測と取り込み** | 次のものを測って記録する。コードは変えない。<br>・`Kimi.csproj` のビルド時間（固定条件で 5 回）<br>・比較の基準になる、診断と意味のスナップショットと IR<br>・付録 B の件数<br>・組み込み規則の試行ビルドの結果<br>・Mir の違反の一覧<br>あわせて本書を取り込む | 証跡がそろい、取り込みの記録がある | 文書のみ |
| **A1 論理量の予算** | `CompilerSizeBudgetTest` をトークンに切り替え、上限を換算する。xUnitTest に `Microsoft.CodeAnalysis.CSharp` を加える | 換算によって状態の変わる領域がない | テスト ±60 |
| **A2 基盤** | 規則を入れずに、次のものを作る。<br>・アナライザーのプロジェクトと、slnx への登録<br>・`Kimi.csproj` の参照と `Directory.Build.props`<br>・構成の読み取り、適用範囲、鍵とハッシュ、基準線の照合<br>・3 つのモード、NotConfigurable、KR9000〜9004<br>・`code-rules.ps1`（Report の完全性の検査を含む）<br>・`verify.ps1` の Verify モードの固定と、エラーログでの抑止の検査<br>・テストの土台、`CodeRulesBaselineTest`、`CodeRulesConfigurationTest`、抑止の回帰テスト（§3.6） | アナライザーが他のプロジェクトに伝わらない。CRLF と LF で同じ鍵になる。抑止の手段のどれでも違反が消えない。エラーログの前提（§3.6 の 3）が成り立たなければ、止めて報告する | アナライザー 450、テスト 250、スクリプト 150 |
| **A3 アーキテクチャ規則の移設** | KR1001〜1003 を入れ、§3.9 の該当する検査を削除する。削除した検査の検出範囲を、テスト用のソースで示す（意味スロット、using の別名、`var` 経由、型引数）。許可された形も示す（公開型） | 現在のソースで 0 件 | アナライザー 350、テスト 250、ArchitectureRulesTest −70 |
| **A4 側表・ゲート・構文分岐・catch** | KR1004〜1006 と KR1008 を入れ、基準線を Initialize する。正規表現のラチェット 2 行を削除する | 元のラチェットが数えていた範囲の件数が、ラチェットの値以下である。数え方の違い（対象のコレクションの種類、旧経路）による差は記録する | 250、テスト 150 |
| **A5 密度** | KR3001〜3003 を入れる。Mir の違反（基点で長い行 92、条件演算子の入れ子 30、分解宣言 67 以下。分解宣言は §4.3 の定義で A0 に数え直す）は直し、それ以外は Initialize する。新規ローカル変数だけの分解宣言を、右辺の評価順を保って書き換える | Mir の KR3 群が 0 件。振る舞いが変わっていない（§6.1） | 150、テスト 100、Mir の書き換え |
| **A6 増築の兆候と特例** | KR2001〜2010、`CodeRuleException` と KR9005 を入れる。Mir の違反は直し、それ以外は Initialize する。基点での Mir の違反は次のとおり（A0 の一覧で確定する）。<br>・引数が 6 を超える 2 メソッド（`MirBody.Call` 8 個、`MirBuilder.Control.cs:210` の `Test` 7 個）<br>・省略可能な引数が 3 個以上の 3 メソッド（`MirDescriptors.cs:141`、`MirBuilder.Calls.cs:22`、`MirBuilder.Control.cs:210` の `Test`。`Test` は引数の規則にも当たる）<br>・`MirBuilder.Into`（69 文）<br>・`MirBuilder.Expansions.cs`（5,176 トークン）<br>・名前なしの bool リテラル（59 以下） | Mir の KR2 群が 0 件 | 350、テスト 180、Mir の書き換え |
| **A7 移行用の一時構造と未使用コード** | KR1007 と `Transitional` を入れ、§4.1 の対象に印を付ける。組み込み規則（§4.5）を有効にし、既存の違反を消す | 組み込み規則の違反が 0 件。印を付けた対象が Report の一覧に出る | アナライザー 120、テスト 80、削除は A0 で測る |
| **A8 運用** | `verify-commit.ps1` に承認のトレーラーの検査を入れる。AGENTS と VERIFICATION を仕上げる。性能を最終計測する | トレーラーの検査の回帰テストがある。性能要件を満たす | スクリプト 60、テスト 40 |

合計の規模は、アナライザー約 1,700 行、テスト約 1,100 行、スクリプト約 210 行である。どれも `src/Kimi/Compiler` の外にあり、コンパイラーの予算の対象外である。

### 6.3 順序の理由

- A1（予算の切り替え）は、A5 と A6（Mir の書き換え）より前に行う。書き換えで行数が増えても、トークン数はほぼ変わらない。そのため、行数の予算のままだと上限に掛かりうる。
- A2 は、すべての規則の土台なので、A3 より前に行う。
- A3 は、規則を足さずに既存の検査を置き換えるだけの単位である。新しい検出と混ぜないことで、検出範囲が同じであることを確かめやすくする。

### 6.4 R4 以降の予定

| 時期 | 内容 |
| --- | --- |
| R5 | Backend 層を構成に加える。Mir の解析と Backend で `System.Linq.Enumerable` を禁止する（KR1001 の構成の変更）。Mir の LINQ（基点で 11 件、多くは `MirDump`）は書き換える |
| R5 以降 | `tests/xUnitTest` に適用するかどうかを判断する |
| R6 の終了時 | KR1006 の基準線を 0 にする。syntax 層から binding 層全体を禁止する |
| R7 の終了時 | KR1004 のうち、Binding の基準線を 0 にする。`UnwrapParentheses` を Parsing と Binding の外で禁止する（KR1002 の構成の変更）。基点の 105 件は、旧経路の 86 件が R6 で、根の補助関数の 19 件（`ElementAccess.cs` 18、`StaticScalar.cs` 1。計画書 §9 で R7 までに削除）が R7 で消える。KR1 群は基準線を持たないので、広げる時点で 0 件であることを確かめる。残っていれば、先に直す |
| R8 | `IntrinsicId` の上限を 30 に戻す |
| 各ステージの終了時 | Report を取り、基準線、特例、移行用の一時構造の一覧を利用者が見直す |

---

## 7. 文書の更新

| 文書 | 更新 | 単位 |
| --- | --- | --- |
| COMPILER_ARCHITECTURE.md | 次のものを書く。<br>・コード規則の節（ID、しきい値、基準線の方針、特例、承認）<br>・依存の表と語彙は KR1001〜1003 で強制すること<br>・Mir と Backend には基準線を置かない原則<br>・各ステージの終了時の条件（R6：KR1006 が 0、R7：Binding の KR1004 が 0）<br>・トークンでの上限 | A0、A1、A3 |
| AGENTS.md | 次のものを書く。<br>・KR の規則はビルドエラーであり、抑止しないこと<br>・緩める変更には承認が要ること<br>・機械的な回避は差し戻すこと<br>・特例の使い方<br>・コミットに書く純増減をトークンに改めること | A1、A8 |
| VERIFICATION.md | 次のものを書く。<br>・`code-rules.ps1` の使い方<br>・ステージの終了時の Report<br>・承認のトレーラー<br>・Report モードを verify で使わないこと | A2、A8 |
| CODEMAP.md | `Kimi.Analyzers` の入口（規則群ごとのファイル、構成、基準線）を書く | A2〜A7 |
| `draft/INTEGRATED.md` | 本書の取り込みを記録する | A0 |
| PLAN.md、PLAN_HISTORY.md | 各セッションの位置と記録を書く | 各単位 |
| STATUS.md | 変更しない（言語の支持の境界は変わらない） | — |

---

## 8. リスクと対応

| # | リスク | 対応 |
| --- | --- | --- |
| 1 | 規則を回避される（機械的な分割、引数を詰めた型、名前の変更） | AGENTS に明記し、レビューで差し戻す。名前を変えたメンバーは新しい鍵になり、上限を満たさなければ通らない |
| 2 | 誤検出でビルドが止まる | 規則ごとにテスト（正例、反例、境界）を置き、単位ごとに導入する。不具合はアナライザーを直して解決する。無効化のスイッチは置かない |
| 3 | ビルドが遅くなる | §3.8 の要件で、単位ごとに計測する |
| 4 | 並行する worktree で基準線が衝突する | 1 行 1 件で整列して置く。マージした後に Prune を実行する |
| 5 | Roslyn の版が合わない | 4.14.0 を参照する。SDK を下げる場合は、読み込めることを確かめる |
| 6 | 作業の途中でビルドが止まる | 意図した挙動である。古い項目は Warning なので、削除の作業は止めない |
| 7 | 規則が目的化する | 規則は兆候を検出するもので、設計の代わりではない。AGENTS の設計手順、独立レビューと併用する（付録 A） |
| 8 | ハッシュが衝突する | 64 ビットなので、数千件では無視できる |
| 9 | 組み込み規則を有効にすると、既存の .editorconfig の設定が警告として報告される | A0 の試行ビルドで確かめ、A7 で直す |
| 10 | R3 の完了までに、Mir の違反が増える | A0 で数え直し、A5 と A6 ですべて直す |
| 11 | 抑止の検査の前提（csc がソースで抑止された診断もエラーログに書く）が成り立たない | A2 の回帰テストで確かめる。成り立たなければ止めて報告し、KR9004 の報告位置をソースの外に置く方式などを設計し直す |

---

## 9. 決定事項（2026-10-11、利用者）

1. **時期。** R3 の完了後に着手し、R4 の前に A0〜A8 を終える。取り込みは A0 の着手時に指示する。
2. **しきい値。** 次の値とする。ほかは §4 の表の値とする。
   - 引数 6、省略可能な引数 2（KR2010 を新設）
   - 文 60
   - 行 160 字
3. **特例。** 文の数と入れ子の深さに、理由と上限を書いた特例を設ける（§3.7）。
4. **適用範囲。** KR2 群と KR3 群には、旧経路を含めない。テストへの適用は、R5 以降に判断する。
5. **Mir の違反。** Mir と、今後作る Backend には、どの規則の基準線も置かずに直す。例外は KR1006 で、R6 の終了時までに 0 にする。
6. **KR2005。** 採用する。
7. **予算。** 論理量に切り替える。生成データは除く。
8. **承認。** コミットのトレーラー `Code-Rules-Approved` で記録する。
9. **追加の規則。** KR1007（移行用の一時構造）、KR1008（型を問わない catch）、組み込みの未使用コード規則を加える。
10. **後の段階で入れるもの。** LINQ の禁止（R5）と、`UnwrapParentheses` の禁止の拡張（違反が 0 件になる R7 の終了時）は、構成の変更として入れる（§6.4）。

---

## 付録 A：場当たり修正の兆候と規則の対応

| 兆候 | 検出する規則 |
| --- | --- |
| 既存の述語や switch に、1 つの形のための節を足す | KR2008。既存のメンバーの悪化は KR2006、KR2007 |
| bool の引数やモードの引数を足す | KR2001、KR2004、KR2005、KR2010 |
| ノードをキーにした、機能ごとの側表 | KR1004 |
| 消費側が構文の種類で判断する | KR1001、KR1002、KR1006 |
| `Supports*` ゲート | KR1005 |
| 失敗の文字列の連鎖 | KR2003 |
| 除く条件のない一時構造 | KR1007（印を付けたもの） |
| 不具合の握りつぶし | KR1008 |
| 使われなくなったコードの放置 | IDE0051、IDE0052、IDE0060 |
| 1 行への詰め込み | KR3001〜3003、§5 |
| 下流での再導出、事後の重複抑制、規則の写し | 一部だけ検出する（KR1001、KR1002）。残りは設計手順とレビューで防ぐ |

## 付録 B：計測の方法

- **入力。** `git archive 019daa61 src/Kimi tests/xUnitTest` を作業用のディレクトリに展開した。リポジトリの作業ツリーは使っていない。
- **道具。** `Microsoft.CodeAnalysis.CSharp` 4.14.0 で、構文だけを解析した。意味モデルは使っていない。
  - 型テストの対象は、名前が `Koto` で終わる型とした。
  - 名前なしの bool リテラルの件数は、外部のメソッドへの引数も含む上限値である。
  - `<auto-generated>` の見出しを持つファイルと、`src/Kimi/Generated` は除いた。
- **証跡。** `artifacts/verify/20261011-code-rules-survey/`（Git の管理外）。
- **再計測。** A0 で、R3 の完了時点のコードを測り直す。基準線の初期値は、各単位で Report モードを使って取る。

## 付録 C：構成ファイルの例

`src/Kimi/CodeRules.globalconfig`（抜粋。`...` は省略）：

```ini
is_global = true

# Scopes: path prefixes relative to the repository root. Rule groups default to src/Kimi/.
kimi_code_rules.scope.accretion.exclude = src/Kimi/Compiler/Analysis/;src/Kimi/Compiler/Emission/
kimi_code_rules.scope.density.exclude = src/Kimi/Compiler/Analysis/;src/Kimi/Compiler/Emission/
kimi_code_rules.scope.syntax_dispatch.include = src/Kimi/Compiler/Mir/Build/
kimi_code_rules.catch_boundaries = src/Kimi/Unit/;src/Kimi/Lsp/;src/Kimi/Testing/

# Current stage (KR1007).
kimi_code_rules.stage = R4

# Limits.
kimi_code_rules.max_line_length = 160
kimi_code_rules.max_parameters = 6
kimi_code_rules.max_optional_parameters = 2
kimi_code_rules.max_out_parameters = 1
kimi_code_rules.max_bool_parameters = 1
kimi_code_rules.max_statements = 60
kimi_code_rules.max_nesting_depth = 4
kimi_code_rules.max_logical_operands = 4
kimi_code_rules.max_file_tokens = 5000

# Layers (the longest matching prefix wins) and dependency rules (KR1001, KR1002).
kimi_code_rules.layer.syntax = src/Kimi/Compiler/Lexing/;src/Kimi/Compiler/Parsing/
kimi_code_rules.layer.binding = src/Kimi/Compiler/Binding/
kimi_code_rules.layer.compiler_root = src/Kimi/Compiler/*.cs
kimi_code_rules.layer.legacy = src/Kimi/Compiler/Analysis/;src/Kimi/Compiler/Emission/
kimi_code_rules.layer.mir_build = src/Kimi/Compiler/Mir/Build/
kimi_code_rules.layer.mir = src/Kimi/Compiler/Mir/
kimi_code_rules.layer.hover = src/Kimi/Checking/Hover/
kimi_code_rules.deny_types.syntax = Kimi.Compiler.Binding
kimi_code_rules.deny_layers.mir_build = binding;compiler_root;legacy
kimi_code_rules.allow_types.mir_build = Kimi.Compiler.AdtDef;Kimi.Compiler.BindingSymbol;Kimi.Compiler.CallPlan;...
kimi_code_rules.deny_members.mir_build = Kimi.Compiler.KotoHelper.UnwrapParentheses;Kimi.Compiler.Parsing.Koto.Parent;...
kimi_code_rules.deny_layers.mir = binding;compiler_root;legacy;syntax
kimi_code_rules.allow_types.mir = Kimi.Compiler.AdtDef;Kimi.Compiler.TyCtxt;...
kimi_code_rules.deny_members.mir = Kimi.Compiler.BindingSymbol.Declaration;...
kimi_code_rules.deny_types.hover = Kimi.Compiler.Binding
kimi_code_rules.deny_members.all = Kimi.Compiler.Parsing.Koto.VisitChildrenCore;...

# Closed vocabularies (KR1003). IntrinsicId: 30 + 4 temporary; R8 returns it to 30.
kimi_code_rules.vocabularies = Kimi.Compiler.HirOp:40;Kimi.Compiler.IntrinsicId:34;Kimi.Compiler.Mir.MirStatementKind:5;...

# Built-in rules (section 4.5).
dotnet_diagnostic.IDE0051.severity = error
dotnet_diagnostic.IDE0052.severity = error
dotnet_diagnostic.IDE0060.severity = error
```

`src/Kimi/CodeRules.baseline.tsv`（抜粋）。各行は、規則、範囲、錨、数、パスをタブで区切ったものである。鍵は規則・範囲・錨で、パスは読む人のための参考である（§3.4）。範囲、規則、錨の順に整列する。

```text
KR2001	T:Kimi.Compiler.Binding	member:M:Kimi.Compiler.Binding.TryCandidateCore(...)	22	src/Kimi/Compiler/Binding/Binding.Calls.cs
KR2006	T:Kimi.Compiler.Binding	member:M:Kimi.Compiler.Binding.TryCandidateCore(...)	274	src/Kimi/Compiler/Binding/Binding.Calls.cs
KR3001	T:Kimi.Compiler.Binding	line:9f3c1a07d25e4b81	1	src/Kimi/Compiler/Binding/Binding.Calls.cs
KR2009	src/Kimi/Compiler/Parsing/Parser.cs	file	26068	src/Kimi/Compiler/Parsing/Parser.cs
```
