# LLDB によるデバッグ機能の仕様変更案・実装計画

日付：2026-10-07（改訂：2026-10-09）。状態：方針決定済み（§2.3 の決定は DBG0 の実証待ち）。正式仕様への取り込み・実装・動作検証は未実施。

## 1. 位置付け

本書に明記した変更は、[SPEC.md](../../docs/SPEC.md) およびその参照先（[IMPL.md](../../docs/IMPL.md) を含む）より優先する。本書で変更しない事項には既存仕様を適用する。デバッグ対応は言語の合法性や実行時の意味を狭めない。

目的は、LLVM・LLDB・Visual Studio Code の標準機能を最大限利用し、Kimigayo のソース上で停止・実行制御・値の確認を行えるようにすることである。独自実装は、コンパイラの情報出力、ビルドと起動の接続、必要な型表示に限る。

**用語**

- **DBG0〜DBG4**：§7 の実装段階。DBG0 は取り込み前に試作で方式を確かめる段階。
- **採用形式**：DBG0 で決めるデバッグ情報の形式（第一候補は CodeView/PDB）。
- **記録**：一回の成功ビルドの生成物を収めた不変のディレクトリ（§4.5）。**記録 ID** はその識別子。
- **最新ビュー**：project/target ごとに一つ置き、最後に成功した記録を指すファイル。
- **pin**：使用中の記録を回収から守る印（SPEC §18.5.2 の active pin）。
- **DI**：LLVM のデバッグ情報メタデータ。**CSP**：コンパイラサービス（SPEC §23）。
- **ビュー**：Kimi の型を論理的な値として見せる formatter の表示規則（§6.3）。

**二つの層**

| 層 | 内容 |
| --- | --- |
| (A) 生成契約 | 設定、デバッグ情報、成果物と記録、report、意味の不変性（§3・§4）。デバッガーに依存しない |
| (B) LLDB 構成 | toolchain の debug 部品、起動、型表示（§5・§6） |

DBG0 の結果で決まる事項は §2.3 にまとめ、他の節はそれを参照する。

**取り込みと実装の順序**

1. DBG0 を先に行う。試作のコードは `temp/` か `draft/Sketches`、証跡は `artifacts/verify/` に置き、製品コードとして commit しない。
2. DBG0 の合格とは、§2.3 の各行で第一候補か不成立時の代替のどちらかが成立し、確認事項がすべて成立することをいう。合格なら (A)・(B) を一度に取り込む。不成立なら (A) だけを取り込み、(B) は未解決の条件と実証結果を記録して再検討する。このとき (A) が依存する行（デバッグ形式、名前と識別子の表記、`ref`/`uniq`/`raw` の区別）は DBG0 で確定した結果を使い、LLDB で判定できなかった行は第一候補を使う。
3. 取り込みコミットで、[SPEC 付録 D](../../docs/spec/appendices/D-deferred-features.md) から「debug information」だけを外す（他の延期項目はそのまま）。同時に PLAN へ段階を登録し、`draft/INTEGRATED.md` に対応を記録する。
4. 観測可能な挙動を変える製品コードは、取り込みコミット以降（または同じコミット）でのみ入れる。
5. 対応範囲の記載（現在は README と IMPL §20.8.4 が、breakpoint・ステップ・行情報を未対応としている）は、各段階の検証後にのみ README と STATUS で更新する。

## 2. 範囲と構成

### 2.1. 責務分担

```text
Kimi コンパイラ → デバッグ情報付き LLVM IR → llc / lld-link → 記録（EXE + デバッグ情報）
Kimi 拡張 → 公式 LLDB DAP 拡張 → lldb-dap / LLDB → 対象 EXE
                  ↕
          VS Code 標準デバッグ UI
```

| 担当 | 責務 |
| --- | --- |
| Kimi コンパイラ | ソース位置・関数・型・変数・有効範囲を LLVM の標準形式で出力し、記録と report を作る。Kimi 用 formatter を同じ版で提供する |
| LLVM / LLD | 機械語と採用形式のデバッグ情報を生成する |
| LLDB / `lldb-dap` | 停止・再開・ステップ・スタック復元・値の取得と DAP 通信 |
| 公式 LLDB DAP 拡張 / VS Code | アダプターの起動と標準デバッグ UI |
| Kimi 拡張 | 対象の選択・保存、ビルド、report の取得、起動設定の作成とセッション管理 |

新規デバッガーエンジン、独自 DAP サーバー、独自シンボル形式、LLDB の fork、専用デバッグ UI は作らない。LLDB の標準機能で足りる操作を作り直さない。

### 2.2. 初期完了の範囲

初期完了は、§7 の DBG0〜DBG4 がすべて合格した状態とする。

- Windows x64 の Application を O0 で新規起動してデバッグする。
- 操作：F5、行ブレークポイント、継続、一時停止、ステップイン・オーバー・アウト、関数名付きコールスタック、停止・通常終了、Abort での停止、対話的な標準入出力。
- 値：引数・ローカル変数・ループ／パターン束縛の名前・型・スコープ・取得可能な値（§4.2・§6）。
- コンパイラが実行生成する本体（closure、ジェネリック具体化、Kimi ライブラリなど）も、行・関数・ローカル変数について通常の関数と同じ契約を持つ。非同期 task は未実装（SPEC 付録 D）のため対象外とし、実装する単位で同じ契約を適用する。
- 型表示：§6.5 で固定する代表集合。

対象外：O2 のデバッグ品質、attach、remote、core dump、テスト実行への専用統合、`obj` 系の動的型表示、closure の捕捉値の専用表示、Kimigayo 構文の式評価、値の編集、所有権・Loan の可視化、部分 Move 後の部分表示。未実装の言語機能は別の課題として記録する。

### 2.3. DBG0 で決める事項

| 事項 | 第一候補 | 判定基準 | 不成立時 |
| --- | --- | --- | --- |
| デバッグ形式 | CodeView/PDB | §6.2 の基本型・ゼロサイズ値・参照の層、§6.3 の enum、§4.2 の所在範囲、関数名を満たせるか | DWARF-in-PE（`lld-link /debug:dwarf`） |
| 名前と識別子の表記 | — | 言語 tag、型名・関数名の表記、型の識別子（formatter が型を選ぶキー）の形式を、LLDB が誤解析せず型検索が安定するように決める | 別の表記を試す |
| `ref`/`uniq`/`raw` の区別 | CodeView のポインターモードへの対応付け | 標準の式（`*p` など）を壊さずに区別できるか | 区別を保証対象から外す |
| formatter の媒体 | formatter bytecode を EXE に埋め込む | 採用版 LLDB が PE から読み込み、Dictionary を上限内で走査できるか | Python formatter をコンパイラに同梱する |
| debug 部品の入手元 | LLVM 22.1.8 の公式 Windows 配布物 | lldb-dap と liblldb を含み、固定の digest で取得できるか | 同じ版の別の公式配布物 |
| Python | 固定版の Python を debug 部品に含める（公式の Windows 版 LLDB は特定版の Python DLL を要する） | 22.1.8 の lldb-dap の実際の依存関係 | Python に依存しない LLDB を得られれば含めない |
| 標準入出力の経路 | lldb-dap の `stdio` 属性 | Ctrl+F5 と同じ対話的な入出力になるか（Windows の lldb-dap は `runInTerminal` を拒否する） | stdio を named pipe に向け、拡張が開いたターミナルへ中継する |
| 公式拡張の最小対応版 | — | §5.2 の起動設定と §5.3 の流れが動く最小の版 | — |
| 基本型名の対応表 | — | §6.2 の基本型の契約を満たす名前（表は IMPL に新設するデバッグの小節に置く） | 別の表記を試す |

あわせて次を確認して記録する。

1. `-g` の有無で、llc が出力する `.obj` の `.text` の内容と再配置が一致する（§4.3）。
2. ステップが line 0 を飛ばす。
3. sourceMap の逆変換でブレークポイントが解決する。
4. Abort 入口と、generic 引数を含む関数名に、名前でブレークポイントを置ける。
5. 単純な参照（`a.b`、`xs[0]`）の hover が、式パーサーを通らず変数パスで解決される（Watch は §5.5 の利用者の式として扱う）。
6. stopOnEntry と Pause の実際の停止位置。
7. lldb-dap が継承する環境変数と serverMode の影響。
8. closure 本体での停止とローカル表示。
9. 利用者の Python も LLDB もない環境で、setup が配置したファイルだけで lldb-dap が起動し、formatter が読み込まれる。

## 3. 設定（A）

### 3.1. 四つの設定

| 設定 | 層 | 指定する場所 | check の有効設定 |
| --- | --- | --- | --- |
| `Target` | 意味構成 | CLI `--Target`（`.kimiproj` の `Targets` から選ぶ） | 入る |
| `Debug` | 意味構成 | CLI `--Debug`、起動設定 `debug` | 入る |
| `Optimization` | 生成構成 | CLI `--Optimization`、`.kimiproj`、起動設定 `optimization` | 入らない |
| `DebugInfo` | 生成構成 | CLI `--DebugInfo`、`.kimiproj`（F5 では常に true） | 入らない |

- 意味構成は check の結果と条件コンパイルを変え、生成構成は生成物だけを変える。生成構成は ProjectSnapshotId にも入れない。
- 四項目はすべて、manifest・記録・report・設定要約に入る。同じ項目は、どこに書いても同じ名前と型を使う（JSON と起動設定では camelCase）。
- `DebugInfo` は新設の真偽値で、既定は `false`。段階別のモードは設けず、段階による対応差は STATUS に記録する。
- 有効値は「明示した CLI 値 → プロジェクト値 → 既定値」の順で一度だけ確定し、全工程で共有する。

### 3.2. コマンドごとの扱い

| コマンド | 扱い |
| --- | --- |
| `emit`、`build`、ビルドを伴う `run` | 四項目を受け付けて生成に使う（`--Optimization` と `--DebugInfo` は新設） |
| `run`（すべての形） | `--` 以降をアプリケーションの引数として渡す（新設） |
| `run --no-build` | 四項目を確定し、最新ビューが指す記録と照合する。一致しなければ異なる設定名を挙げて失敗し、生成も再ビルドもしない |
| `build --Manifest` | manifest の値だけを使い、四項目の CLI 指定はすべて拒否する |
| `run <path.exe>` | 設定の指定を拒否する（現行は黙って無視している） |

```powershell
# 追加予定の CLI。Debug 設定は false のまま、デバッグ情報付きの O0 を生成する
kimi build App.kimi --Optimization O0 --DebugInfo true
# 同じ構成の最新成果物を、引数付きで実行する（構成が違えば失敗する）
kimi run App.kimi --no-build --Optimization O0 --DebugInfo true -- input.txt
```

## 4. 生成と成果物（A）

### 4.1. デバッグ情報の生成

生成に必要な対応（ソース文書・位置・関数・スコープ・変数）は、Lowering がすべて `EmissionModule` に持たせる。LLVM writer は AST、Binding、ownership state を参照しない。`DebugInfo=false` ではデバッグ専用の表や文字列を収集しない。有効なときも、位置などは値で共有して ID を再利用し、バッファも再利用する。

| 対象 | 規則 |
| --- | --- |
| ソースのパス | IR 全体（ModuleID、source_filename、DIFile）に、論理パス `/_/<root>/<論理名>` だけを書く。root は `project`、`package/<id>`、`kimi/<版>`、`generated/<id>`。内容は DIFile 標準の SHA-256 checksum で識別する。最適化レベルによらず同じ形にする |
| 対象コード | コンパイラが生成するすべての Kimigayo コードに同じ規則で出す。手書き IR の runtime はソース位置を持たないが、Abort 入口には名前で解決できる人工的な DISubprogram を付ける |
| 関数名 | DISubprogram の name は CSP の正規の宣言表示（修飾名、generic 引数、多重定義の区別に必要な引数型）、linkageName は既存の mangled 名。closure と生成補助関数は、親の表示に固定の接尾辞を付ける |
| 型の同一性 | 表示名ではなく、固定の識別子（Kimi の型識別のハッシュ）で表す。表示名は CSP の正規の型表示とする。識別子と名前の表記は §2.3 による |
| 構造体・タプル | DI のメンバーは宣言順・実オフセットで出す。Loan witness や整列用キャリアなど、言語上のフィールドでない成分は出さない |
| 内部表現 | Array・string のハンドルと、`raw/u8` の背後にある Dictionary のスロット `{links, key, value}` にも名前付きの DI 型を出す |
| enum | IMPL §21.1.5 の二つの表現に一つずつ対応させる。一般の enum は、tag（Case 名を宣言順に並べた列挙型）と Case ごとの struct からなる union。nonnull Option は `R` を一つ持つ記述 |
| ソース位置 | 暗黙の cleanup は、スコープを離れる辺の位置（脱出文ならその文、通常の脱出ならブロックの最後の文）。defer 本体は defer 文の位置。起動処理・生成補助関数・複数の辺で共有する cleanup は line 0 |

```llvm
!3 = !DIFile(filename: "src/Main.kimi", directory: "/_/project",
             checksumkind: CSK_SHA256, checksum: "<64 hex digits>")
```

Abort メッセージの位置文字列はデバッグ情報とは独立に残し、ファイル名には同じ論理名を使う。

### 4.2. 変数の所在

所在とは、デバッガーが変数の値を読める場所をいう。所在を開けば値を読める状態になり、閉じれば取得不可になる。

- 名前を持つ非ゼロサイズの束縛は、Optimization と DebugInfo によらず、pre-opt IR で固定のスタック領域を持つ。O2 では opt がそれをレジスターへ昇格させる。これで IR は Optimization に依存しないまま保たれる（IMPL §21.5.5 を改める）。
- 所在は、その領域を指す debug record（`#dbg_value`）で記述する。所有権解析が、その地点で全体を静的に Initialized と確定した区間だけを開く。
- 所在は、Move、破棄、部分 Move（aggregate 全体を閉じる）、静的に未確定な合流で閉じ、再初期化で再び開く。実行時の cleanup flag や残存ビットから推測しない。
- スコープ外の名前は出さない。スコープ内で所在が閉じていれば、取得不可とする。
- `noinit` 配列は静的に Initialized なので通常どおり表示し、値の妥当性は保証しない（SPEC §4.3.4 の利用者責任）。独自の実行時初期化 bitmap や所有権追跡は追加しない。

### 4.3. コード生成の不変性

規則：コード生成とリンク最適化は Optimization だけで決める。DebugInfo が加えるのは、DI と、デバッグ用の節・記録だけである。

- lld は `/debug` を付けると `/opt` の既定を変えるので、`/opt` を常に明示する。

  | Optimization | `/opt` |
  | --- | --- |
  | O0 | `/opt:ref,noicf`（同一関数を畳まず、名前を保つ） |
  | O2 | `/opt:ref,icf`（ICF による名前の畳み込みは O2 のデバッグ品質として対象外） |

- 主な証拠：DebugInfo だけが異なる二つのビルドで、llc が出力した `.obj` の `.text` の内容と再配置が一致すること。EXE は比較しない（`/debug` で加わるデバッグディレクトリによって後続の節の配置がずれ得るため）。実行結果と破棄順序の比較は補助とする。
- 回帰：DebugInfo=false の IR と EXE は、意図した変更（§4.1 の論理パス、§4.2 の固定領域、本節の `/opt` 明示）を除いて変更前と一致させる。意図した変更を入れる単位では、fixture の期待値を更新し、O0・O2 の実行時間・コンパイル時間・サイズを `src/Benchmark` で測る。

### 4.4. 壊れたデバッグ情報

LLVM は不正なデバッグメタデータを見つけると、警告を出してデバッグ情報を削除し、終了コード 0 で続行する。

- 規則：DebugInfo=true でデバッグ情報が削除・欠落した場合は、コンパイラの内部エラー（IMPL §21.3.5）として成果物を公開しない。利用者のプログラムを拒否するものではない。
- IR を読むすべての LLVM 呼び出し（検証用 `opt`、O2 の `opt`、`llc`）に `-disable-auto-upgrade-debug-info` を付ける。
- オブジェクトに採用形式のデバッグ節（PDB なら `.debug$S`・`.debug$T`）があることを、既存の `llvm-readobj` 検査で確認する。

### 4.5. 成果物と記録

規則：`build` の生成物はすべて不変の記録に属する。記録の外に置くのは、最新ビュー、pin（`pins/`）、内容 ID 名のソースの写し（`sources/`）だけとする。`build --Manifest` は、manifest のあるディレクトリを `bin/<target>/` の代わりに使う。

```text
bin/<target>/
  App.ll, App.link.json        # emit の出力（emit だけが書く）
  App.link.build.json          # 最新ビュー
  builds/<RecordId>/           # 記録（公開後は変更しない）
    App.ll, App.link.json      # このビルドの IR と manifest
    App.obj, App.exe, App.pdb  # PDB は DebugInfo=true かつ PDB 採用時
    App.O2.ll                  # O2 のとき
    App.inspection.txt
    inputs/                    # 固定した native 入力の写し
    build.json
  pins/                        # pin（記録の外に置く）
  sources/<ContentId>/         # ディスク上にないソースの読み取り専用の写し
```

- **固定パスの廃止**：`build` が固定パスに書いていた `App.O0.exe`・`App.O2.exe`・`.obj`・`App.O2.ll`・`inspection.txt` は廃止し、記録に移す。
- **公開**：staging ディレクトリで最終名のまま生成・リンクし、完成後にディレクトリを記録 ID の名前へ上書きなしで rename する。同じ ID がすでにあれば、内容を検証して再利用する（pack と同じ方式）。続けて pin を作り（要求された場合）、最新ビューを更新し、回収する。
- **記録 ID**：時刻を含まない記録内容（入力スナップショット ID、四項目、ツールチェーン識別、出力ハッシュ）の SHA-256。ツールチェーン識別には、解決した toolchain の `installation.json` の値を使う。通常のビルドではツールを再ハッシュしない。所要時間は report に書く。
- **最新ビュー**：ビルド開始時に無効化し（現行どおり）、成功時に新しい記録を指す。構成ごとには保持しない。現行と同じく、別の構成をビルドすると、前の構成での `--no-build` 実行は失敗する。
- **消費**：`run --no-build`、F5、証跡は記録の EXE を直接使う（`--no-build` の照合は §3.2、F5 の確認は §5.3）。他のビルドは別の記録を作るだけなので、使用中のファイルに触れない。
- **pin**：`pins/` に、記録 ID と所有プロセス ID を持つ小さなファイルとして置く。所有プロセスが終了した pin は無効とする。記録を使う消費者は、使う前に pin を取る。`run --no-build` は自身を所有者とし、F5 ではビルドが `--PinOwner <pid>` で記録の公開と同時に pin を作り、そのパスを report に書く。
- **回収**：成功したビルドの最後に、同じ project/target の記録のうち、最新ビューが指さず有効な pin もないものを回収する。手順は、まず記録ディレクトリを rename で隔離し、成功したものだけを削除する。実行中の EXE を含む記録は rename できないので残る。失敗したビルドは回収しない。`sources/` は、参照する記録がなくなったものを同時に回収する。プロセス間ロックは新設しない。拡張内のディレクトリロックは、同一ウィンドウでの重複防止だけを担う。
- **ディスク上にないソース**：パッケージと生成ソースは、DebugInfo=true のビルドで `sources/<ContentId>/` に一度だけ書き出す。Kimi ライブラリは、埋め込みと同じバイトをコンパイラの配布物に含める。
- **パス**：記録と report はローカルの実パスを持つ。秘匿化は、証跡として保存する写しにだけ適用する。IR には絶対パスがない（§4.1）ので、O2 の IR を書き換える現行の秘匿化処理は廃止する。
- **manifest**：最上位の `target` の隣に `debug` を、`codegen` に `debugInfo` と `debugFormat` を加える。`debugFormat` は設定ではなく、target と §2.3 の決定から導く値である。native build の引数（`/debug` など）は manifest だけから導く。schema の更新は、PLAN の T1（link manifest schema 4）と一本化する。
- **旧形式**：旧 schema の manifest と旧形式の記録は成功として扱わず、再ビルドを求めて失敗させる。
- **前提作業**：現行のネイティブ記録は、ID のない可変の JSON 一つである。SPEC §18.5.2 の不変の処理記録への移行を DBG1 の最初の単位とし、PLAN の T4（検証済み意味情報の再利用、SPEC §18.7 の記録）と仕組みを共有するかを PLAN に記す。固定パスに依存する既存の試験・スクリプト（milestone ハーネスなど）も記録経由に改める。

PDB を採用した場合のリンク：

```text
lld-link App.obj <libraries> /entry:__kimi_start /subsystem:console /nodefaultlib
         /debug /pdb:<staging>\App.pdb /pdbaltpath:App.pdb /opt:ref,noicf /Brepro /out:<staging>\App.exe
```

- EXE には PDB のファイル名だけを埋め込み、出力場所に依存させない。
- EXE と PDB の対応は、RSDS の GUID/age（lld が PDB の内容から作る）で示す。LLDB がこれを照合する。PDB の SHA-256 は公開時に一度だけ計算する。

### 4.6. build report

`build` に任意の `--Report <path>` を追加する。人向けのログとは別の契約として、ビルドの事実だけを機械可読な JSON で書く。デバッガーの場所は含めない（§5.1）。

```json
{
  "schemaVersion": 1,
  "succeeded": true,
  "elapsedMilliseconds": 1840,
  "results": [
    {
      "recordId": "<sha256>",
      "recordDirectory": "C:/work/App/bin/x86_64-pc-windows-msvc/builds/<sha256>",
      "executable": { "path": "C:/work/App/bin/x86_64-pc-windows-msvc/builds/<sha256>/App.exe", "sha256": "<sha256>" },
      "debugSymbols": { "path": "C:/work/App/bin/x86_64-pc-windows-msvc/builds/<sha256>/App.pdb", "sha256": "<sha256>", "guid": "<guid>", "age": 1 },
      "settings": { "target": "x86_64-pc-windows-msvc", "debug": false, "optimization": "O0", "debugInfo": true },
      "workingDirectory": "C:/work/App",
      "sourceMap": [
        ["/_/project", "C:/work/App"],
        ["/_/kimi/0.1.1", "C:/Kimi/Library"],
        ["/_/package/example.codec", "C:/work/App/bin/x86_64-pc-windows-msvc/sources/<id>"]
      ],
      "sources": [{ "path": "/_/project/src/Main.kimi", "sha256": "<sha256>" }],
      "debugView": { "version": 1, "formatter": null },
      "pin": "C:/work/App/bin/x86_64-pc-windows-msvc/pins/<file>"
    }
  ]
}
```

- `--Report` を受け付けたビルドは、通常の終了（失敗・キャンセルを含む）で必ず report を書く。書かないのは異常終了のときだけである。report は一時ファイルに書いてから rename する。
- `results` は常に配列にする。`debugSymbols` は別ファイルのデバッグ情報がなければ null とする。`sources` は project のソースの論理パスと SHA-256 を持つ。`debugView.version` は §6.4 のデバッグ表示版（(B) を取り込むまでは 0）、`debugView.formatter` は外部 formatter のパスか null とする。`pin` は `--PinOwner` を指定したときだけ書く。

## 5. 起動（B）

### 5.1. toolchain の部品

| 部品 | 内容 | 必須 | 使うコマンド |
| --- | --- | --- | --- |
| build | 現行の LLVM ツールと backend | 必須 | `build`、ビルドを伴う `run` |
| debug | `lldb-dap`、liblldb、固定版の Python（§2.3）、ライセンス | 任意 | F5 |

- 規則：各コマンドは自分が使う部品だけを要求する。debug 部品の有無はビルドの成否に影響しない。
- setup：debug 部品も LLVM と同じく、既存のディレクトリからコピーする（例：`-LldbBin`、`-PythonHome`）。ダウンロードはしない。コピーしたファイルは §2.3 で固定した digest と照合し、一致しなければ導入しない。ハッシュは `installation.json` に記録する。
- `kimi toolchain verify` は部品ごとに結果を報告する。導入していない任意部品は「not installed」とし、失敗にはしない。照合はハッシュで行い、版表示による照合は版表示を持つツールにだけ行う。
- `kimi toolchain locate --Component debug --Report <path>` は、`lldb-dap` の絶対パスと、LLDB・Python の動作に影響する環境変数を返す。環境変数は不要なものも空の値として明示し、継承された値が効かないようにする。通常の操作では版やハッシュを検証しない、という既存の原則に従う。
- CI のキャッシュキーには、debug 部品の入手元を含める。
- 調査時の compiler profile は LLVM 22.1.8 である。公式拡張の最小対応版は §2.3 で確定する。

### 5.2. 拡張と起動設定

- 規則：言語機能（構文、LSP、Build/Run/Ctrl+F5）はデバッガーに依存しない。`extensionDependencies` は使わず、README で公式 LLDB DAP 拡張（`llvm-vs-code-extensions.lldb-dap`）を推奨する。オフライン環境では、F5 を使うときだけ、その VSIX を別途入手する必要がある。
- 既存の `kimi` デバッグ型を、F5 と Ctrl+F5 の両方に使う。ラベルは「Kimi」に改め、初期構成と snippet から `noDebug` を除き、`runDebug.ts` にある noDebug 以外の起動を拒否する処理とその試験もなくす。既存の launch.json にある `"noDebug": true` の行は、CHANGELOG と README で削除を案内する。

```jsonc
{
  "type": "kimi",
  "request": "launch",
  "name": "Kimi: Debug",
  "program": "${workspaceFolder}/App.kimiproj", // 省略時は対象選択
  "debug": false,          // --Debug の値（既定 false）
  "optimization": "O0",    // 省略時は F5 なら O0、Ctrl+F5 ならプロジェクト値
  "stopOnAbort": true,     // 既定 true
  "args": ["input.txt"],
  "env": { "KEY": "value" }
}
```

キーの扱い：

1. **実行キー**（`program`、`debug`、`optimization`、`args`、`env`）は、F5 と Ctrl+F5 で同じ意味を持つ。Ctrl+F5 は、指定された `debug`・`optimization` を `kimi run` の `--Debug`・`--Optimization` として渡し（省略時は渡さない）、`args` を `--` 以降に渡し、`env` を子プロセスの環境に加える。
2. **Kimi が決めるキー**（`target`、`debugInfo`、`cwd`、`stdio`・`console`、`debugAdapterExecutable`/`Args`/`Env`/`Port`/`Hostname`）は、利用者が書いたらエラーにする。`cwd` は両経路ともプロジェクトのディレクトリ（暗黙プロジェクトではソースのディレクトリ）とする。DebugInfo は F5 では true、Ctrl+F5 ではプロジェクト値とする。
3. **リスト型のキー**（`initCommands`、`preRunCommands`、`sourceMap`）は、Kimi の値の後に利用者の値を連結する。
4. **その他の lldb-dap 標準キー**は変更せず転送する。`stopOnEntry` は標準の意味（起動直後の停止）のまま使う。最初のユーザー行で止めたい場合は、入口関数への関数ブレークポイントを使う。

- `stopOnAbort` と 3・4 のキーはデバッガーの動作だけに関わるので、Ctrl+F5 では使わない。
- Kimi の `program` はソースまたはプロジェクトを指す。lldb-dap に渡す `program` は report の EXE である。
- 起動設定の `debug` が LSP の有効設定と異なる場合は、起動時に一度だけ通知する。

### 5.3. F5 の流れ

1. 既存の処理で対象を選び、信頼済みワークスペースかを確認して保存し、一つの Application/target に確定する。曖昧な候補から最初のものを選ばない。
2. 公式拡張（最小対応版以上）を `vscode.extensions.getExtension` で、debug 部品を `kimi toolchain locate` で確認する。足りなければ案内して止める。
3. 後段 resolver（`resolveDebugConfigurationWithSubstitutedVariables`）で、選んだ project だけをビルドする。拡張は `.kimiproj`・manifest・ログを解釈せず、report だけを使う。

   ```powershell
   kimi build <project> --Debug <debug> --Optimization <optimization> --DebugInfo true --PinOwner <拡張ホストの PID> --Report <新しい一時パス>
   ```

4. 成功の report があり、`results` がちょうど一つの場合だけ続ける。失敗の report ならビルド失敗として扱う。report がなければ起動せず、ビルドの出力を示す（古いコンパイラでは、未知のオプションとして出力に現れる）。F5 は `kimi.runBuilds`（古い run 専用の実行ファイル向けの設定）の影響を受けない。
5. report の EXE の SHA-256 を照合する。
6. lldb-dap 用の構成を作り、`vscode.debug.startDebugging` で独立したセッションを開始する。元の構成には `undefined` を返して静かに終える。
7. セッションの終了、起動失敗、キャンセルのいずれでも、report が示す pin を削除する。Stop は起動した対象を終了させ、detach して残さない。キャンセルは resolver の CancellationToken に一本化する。

F5 は CLI での上書きだけを使い、`.kimiproj` や利用者・ワークスペースの設定を書き換えない。対象は lldb-dap が launch し、`kimi run` で起動してから attach する方式は採らない。見かけだけの親セッションや DAP の中継も作らない。Ctrl+F5（`noDebug`）は既存の RunAdapter 経路で `kimi run` を使い、RunAdapter にデバッガーは実装しない。

生成する lldb-dap 構成の例：

```jsonc
{
  "type": "lldb-dap",
  "request": "launch",
  "name": "Kimi: Debug",
  "program": "C:/work/App/bin/x86_64-pc-windows-msvc/builds/<sha256>/App.exe",
  "cwd": "C:/work/App",
  "args": ["input.txt"],
  "env": { "KEY": "value" },
  "debugAdapterExecutable": "<locate が返したパス>",
  "debugAdapterArgs": ["--no-lldbinit"],
  "debugAdapterEnv": { /* locate が返した環境変数 */ },
  // Python formatter を採用した場合は、その読み込みもここに加える
  "initCommands": ["settings set target.process.thread.step-avoid-regexp <Kimi ライブラリの接頭辞>"],
  "preRunCommands": ["breakpoint set -n __kimi_abort", "breakpoint set -n __kimi_abort_message"],
  "sourceMap": [["/_/project", "C:/work/App"], ["/_/kimi/0.1.1", "C:/Kimi/Library"]]
  // 標準入出力の指定は §2.3 で決めた経路に従う
}
```

**Abort での停止**：すべての Abort 経路は、runtime の Abort 入口（現状は `__kimi_abort` と `__kimi_abort_message`）を通る。`stopOnAbort` が true なら、F5 はそこに関数ブレークポイントを置く。原因箇所は呼び出し元のフレームとして標準 UI で見せ、最上位のフレームを独自に差し替えない。

### 5.4. ソースの一致とステップ

- ソースは DIFile の論理パスと checksum で識別する。実パスへの対応は、report の `sourceMap` 一か所で与える。sourceMap はパスの変換であって、内容が一致する証拠ではない。
- 拡張は、開いているファイルとブレークポイントを置いたファイルだけを、必要になった時点で report の `sources` と比べる。内容が異なれば、起動前でも実行中でも同じ「古いビルドを実行中」の通知を出す。再ビルドは利用者の再起動でのみ行い、編集後の行へ位置を読み替えない。
- ステップには LLDB 標準の step-avoid を使い、既定では Kimi ライブラリの関数に入らない。利用者は `initCommands` で上書きできる。

### 5.5. 実行中の保証

- 実行可能な命令のない行では、LLDB が解決した位置と未解決の状態を標準 UI に反映する。要求したすべての行で必ず停止するとは保証しない。
- Pause：全スレッドが止まり、利用者のスレッドを選んで Kimi のフレームを含むスタックを表示し、再開できる。Kimi のソース行で止まることは約束しない（Windows では割り込み用スレッドで止まる）。
- 標準入出力：対象の stdin・stdout・stderr を VS Code 上で対話的に使え、Ctrl+F5 と同じ意味を持つ。
- 式の評価は、式を誰が書いたかで分ける。
  - Kimi が提供するもの（formatter、生成した構成、Kimi のコマンド）は、対象プロセスのコードを実行しない（formatter は §6.1）。hover は lldb-dap が変数パスだけで解決する。
  - 利用者が書いた式（Watch、条件、logpoint、Debug Console）は LLDB 標準の評価であり、対象のコードを実行し得る。Kimigayo の構文・安全性・意味の保証の対象外で、Kimigayo の評価対応とは表示しない。
- 再現性：Kimi のセッションは、Kimi が固定した入力だけで表示契約を満たす。`--no-lldbinit` で `.lldbinit` を読まず、利用者の LLDB 設定は標準キー `initCommands` で渡す。

## 6. 型表示（B）

### 6.1. 共通規則

- 表示状態は「値」「省略付きの値（省略印あり）」「取得不可（理由）」「生表示（標準の内部表示）」の四つとする。取得不可は §4.2 と本節で定めた状態のときだけ使い、実装していない表示の代わりにしない。
- formatter は停止中のメモリとデバッグ情報だけを読む。ユーザー関数・getter・Iterator・文字列化関数は実行せず、メモリも書き換えない。Property の getter など、呼び出しが必要な値は表示しない。
- 物理形状（オフセット・サイズ・stride）はすべて DI（§4.1）から読み、formatter は数値の定数を持たない。型は識別子（§2.3）で選び、表示名の正規表現には頼らない。
- 子要素は言語の標準の反復順で並べ、標準の Iterator と同じ構造の不変条件でたどる。要素の中身の整形は、LLDB の再帰的な適用に任せる。
- 走査量は表示する子要素の数に比例させ、容量や空きスロットには比例させない。子要素の数は、格納された長さから O(1) で求める。
- 上限（子要素数、summary の文字数）は LLDB の標準設定だけを出所とする。展開は LLDB の遅延展開とページ指定を使う。状態は LLDB の `update()` 契約に従い、停止をまたいで持たない。
- 不正な値：各ビューは依拠する表現の不変条件を SPEC/IMPL の妥当性の表から列挙し、使う前に検査する（検査量は上限内とする）。違反があれば、違反を含む最小の値を「取得不可（理由）」とし、空・ゼロ・別の Case に置き換えない。生のメンバーは常に生表示で見られる。検査は列挙した不変条件に限り、unsafe／raw 操作による破損や dangling を完全に検出することは保証しない。
- 標準表示で §6.2 の契約を満たせる型には、summary・synthetic の formatter を追加しない（§6.2 の type format を除く）。

### 6.2. 標準の構造表示

| 型 | 表示 |
| --- | --- |
| 基本型 | 幅・符号・種類（整数・浮動小数点・真偽・文字）を判別できる型名と値 |
| ゼロサイズ値 | 型名と空の値。他の値と混同しない |
| 構造体・タプル | 型名と、フィールド名または要素番号ごとの値（宣言順） |
| 固定配列 `[N of T]` | 型・長さ・要素 |
| `ref/T`・`uniq/T` | 一度に一層だけ展開し、子は静的型の直接の参照先とする（平坦化しない）。参照先はその型の規則で表示する。種別の区別は §2.3 による |
| `raw/T` | アドレスと静的型。参照先は有効性を保証できないので自動では展開しない |

- 基本型名の対応表は §2.3 で確定する。
- PDB では、1 バイト整数を文字ではなく整数として表示する type format を一つだけ加える。
- `isize` と `i64` のように同じ表現になる型の区別は保証しない。`char` は Unicode スカラー値として表示する。

### 6.3. Kimi ビュー

| ビュー | 型 | 子要素と順序 | 要約 |
| --- | --- | --- | --- |
| 連続要素 | `Array<T>`、`Slice<T>`、`UniqSlice<T>` | 初期化済みの論理要素を index 順に並べる。capacity の未使用領域は出さない | 型・長さ |
| テキスト | `string` | なし | 長さと文字列。埋め込み NUL はエスケープし、長い値には省略印を付ける。不正な UTF-8 は §6.1 により取得不可とする |
| リンク列 | `Dictionary<K, V>` | 生きているエントリを挿入順に並べる（head から `links.next`）。件数はハンドルの length を使う | 型・件数 |
| タグ付き共用体 | enum（`Option`・`Result` を含む） | 有効な Case の payload だけ | 型・Case 名 |

- `Slice` と `UniqSlice` は同じビューを使い、共有か排他かは型名で区別する。
- リンク列は、一回の `update()` の中で到達済みのリンク位置を遅延キャッシュし、全子要素の取得を O(n) に抑える。
- enum は、一般の表現では tag と同名の union メンバーだけを表示し、列挙子にない tag は取得不可とする。nonnull Option は、値 0 を None、それ以外を `Some(R)` とする。
- ジェネリック具体化、入れ子、空の値にも同じ規則を適用する。
- 対象外の型（`obj` 系、closure、Iterator・Remainder など）は、取得できる型名と生表示だけを出す。部分的な生表示をもって対応済みとはしない。

### 6.4. formatter とデバッグ表示版

規則：formatter は、版を決めるもの（コンパイラとライブラリ）と一緒に配る。

- ビュー論理（走査順・生存判定・妥当性検査）は `src/Kimi/Library` に一つの定義として置き、対応するライブラリ型と同じ版で管理する。コンパイラはそこから、§2.3 で決めた媒体の formatter を作る。走査結果はライブラリの native 試験で照合する。公開宣言ではないので、LIBRARY は変更しない。
- formatter はコンパイラと一緒に配り、LLVM の toolchain には置かない。
- **デバッグ表示版**は、DI の形状規則（§4.1）かビュー論理を変えたときに上げる整数で、report の `debugView.version` に書く。
- bytecode を EXE に埋め込む場合、formatter は成果物の中で完結するので照合は要らない。Python formatter の場合だけ、DebugInfo=true の成果物にデバッグ表示版を埋め込み、formatter が読み込んだモジュールの版と照合する。report の `debugView.formatter` は、その formatter のパスを示す。
- 版が違う場合や formatter を読み込めない場合は、Kimi ビューを適用せず生表示にし、一度だけ診断する。起動は続け、推測で解釈しない。

### 6.5. 保証対象の固定

- 判定に使う代表集合を、DBG3 の開始時に固定する。§6.2・§6.3 の各型と、要素の種類（スカラー、ゼロサイズ型、Non-Copy 構造体、enum、参照、入れ子コレクション）の組合せで構成し、入れ子は深さ 1 と 3 を代表とする。§6.2 の部分は DBG3、§6.3 の部分は DBG4 の判定に使う。
- 以後、§6.2・§6.3 の型に新しい実行生成を加える単位では、同じ単位で表示の試験を加えるか、STATUS に表示の制限を記録する。

## 7. 実装段階と完了条件

契約項目とは、§3〜§6 の各箇条と表の各行をいう。各項目に段階と試験 ID を一つずつ割り当て、その対応表は取り込み時に PLAN に置く。段階の完了とは、その範囲の項目がすべて合格したことをいう。段階名は、PLAN の完了済みトラック D0〜D5 と重ならないようにする。

| 段階 | 層 | 範囲 | 完了の要点 |
| --- | --- | --- | --- |
| DBG0 実証 | — | §2.3 | 決定表の全行と確認事項 1〜9 について、採否と根拠を記録している |
| DBG1 生成と成果物 | A | §3、§4.1（型の同一性と DI の形状を除く）、§4.3〜§4.6（pin を除く） | CLI 試験で合格する。`.obj` で不変性を確認する。実行中の EXE を含む記録が残り、その間も他のビルドが成功する |
| DBG2 起動 | A・B | §4.5・§4.6 の pin（`--PinOwner` を含む）、§5 | DAP 試験（§8）の正常系と異常系が合格する。公式拡張なしで LSP と Ctrl+F5 が動く |
| DBG3 変数と標準表示 | A・B | §4.1 の型の同一性と構造体・タプルの形状、§4.2、§6.1 の表示状態と取得不可、§6.2、§6.5 | 同名変数、引数、入れ子のスコープ、ループ／パターン束縛、再代入、Move・部分 Move・再初期化、分岐の合流、破棄について、名前・型・値・取得不可が正しい。closure とジェネリック具体化の本体も含む |
| DBG4 Kimi ビュー | A・B | §4.1 の内部表現と enum の形状、§6.1 の formatter の規則、§6.3、§6.4 | 代表集合のうち §6.3 の部分について、通常値・空・大きな値・入れ子・読み取り失敗・不正値・非活性領域を確認する |

- DBG0 の実証不足を、後の段階へ先送りしない。未検証の LLDB fork や独自 DAP を暗黙に導入して完了としない。
- 実装の入口は [CODEMAP](../../docs/dev/CODEMAP.md) に従う。生成は `BodyLowering` → `EmissionModule` → `LlvmModuleWriter`、成果物は `EmissionArtifacts`・`NativeToolchain`・`ArtifactPaths`、配布は `ToolchainResolver` と backend setup、拡張は `src/kimi-ext` の対象選択・command runner・`runDebug.ts` を拡張する。

## 8. 検証と記録

- **情報の検査**：オブジェクトのデバッグ節は既存の `llvm-readobj` で、PDB は採用版の LLDB で読み、ファイル・行・関数・型・変数・所在を検査する。存在するだけでは合格にしない。
- **不変性**：§4.3 に従う。
- **DAP 試験**：DBG2 で作るハーネスが lldb-dap を直接操作し、独立した期待値で停止位置・スタック・値を確認する。各試験には timeout と後片付けを設ける。
  - 正常系：対象の選択から停止、ステップ、スタック、Pause、Stop、通常終了、Abort での停止まで。あわせて、標準入出力、F5・Ctrl+F5 両経路での args・env、古いビルドの通知、step-avoid の既定。
  - 異常系：未解決の breakpoint、ビルド失敗、キャンセル、古いコンパイラ、debug 部品や公式拡張の欠如。
- **verify.ps1**：デバッガー試験は選択できる区分とし、実行しなければ「not performed」と記録する（`-VerifyToolchain` と同じ形）。段階の完了と Windows CI では必須とする。
- **VS Code**：イベントや変数など自動で検査できる部分は、拡張の unit・integration 試験と並べて、vscode-test と DAP tracker で検査する。表示の目視確認は利用者が担当する。分離した profile で、単一ソースとプロジェクト、空白・日本語を含むパス、ソースの変更、成果物の不一致、依存ファイルの不足を含める。
- **回帰と負荷**：既存の O0・O2 の回帰を維持する。DebugInfo=false の allocation・reuse と、formatter の走査量の上限を検証する。DBG3・DBG4 の完了時には、milestone プログラムで DebugInfo の有無ごとに emit・リンク時間、割り当て、IR・obj・PDB のサイズを `src/Benchmark` で測定する。
- **証跡**：結果・失敗・未確認事項は `artifacts/verify/`、測定は `artifacts/benchmarks/`、配布物は `artifacts/packages/` に保存する。証跡には、ソース・設定・記録 ID と、compiler・LLDB・formatter・公式拡張の版を結び付ける。

## 9. 変更対象

### 9.1. 文書

「取り込み」は仕様を更新する層、「実装」は STATUS・CODEMAP を更新する段階を示す。

| 変更内容 | 所有文書 | 取り込み | 実装 |
| --- | --- | --- | --- |
| 「debug information」の延期解除 | SPEC 付録 D | A | — |
| ProjectSnapshotId の除外対象を、生成構成（Optimization・DebugInfo）に広げる | SPEC §18.5.2 | A | DBG1 |
| `DebugInfo` 設定、設定要約、暗黙プロジェクトのパス一覧 | IMPL §20.8.1、§20.8.6.1 | A | DBG1 |
| manifest（`debug`、`codegen` の `debugInfo`・`debugFormat`）、schema（T1 と一本化） | IMPL §20.8.3 | A | DBG1 |
| 記録と report のパスの方針（秘匿化は証跡の写しだけ）、O2 の秘匿化の廃止 | IMPL §20.8.3、§20.8.8 | A | DBG1 |
| 手動手順の例をコンパイラの実際の引数に合わせ、「`/debug` は行・変数情報を作らない」の文を §4 の規則に置き換える | IMPL §20.8.4 | A | DBG1 |
| CLI（生成構成、`run` の引数、`--no-build`、`run <path.exe>`、`--Report`）、記録・最新ビュー・回収 | IMPL §20.8.6 | A | DBG1 |
| pin と `--PinOwner` | IMPL §20.8.6 | A | DBG2 |
| デバッグ情報の生成規則、`/opt`、壊れた情報の扱い | IMPL §21.5 | A | DBG1・DBG3・DBG4 |
| 名前付き束縛の固定スタック領域 | IMPL §21.5.5 | A | DBG3 |
| toolchain の部品、setup の入力、verify、locate | IMPL §20.8.5、§20.8.8 | B | DBG2 |
| 起動と表示の契約（§5・§6）、基本型名の対応表 | IMPL §20.8 に新設するデバッグの小節 | B | DBG2〜DBG4 |
| VS Code の手順と移行の案内、`kimi` 型（ラベル、初期構成、noDebug の拒否処理とその試験） | README、拡張の CHANGELOG・package.json・`runDebug.ts`・`tasks.test.ts` | — | DBG2 |
| デバッガー試験の区分、選択方法、「not performed」の記録 | docs/dev/VERIFICATION.md | — | DBG2 |
| 対応範囲、入口、計画 | README、STATUS、CODEMAP、PLAN | — | 各段階 |
| 非目標（LLDB fork、独自 DAP、Kimigayo 式の評価、実行時の初期化 bitmap） | SETTLED（候補） | A | — |

言語構文、型の同一性、Move・Borrow、例外・Abort の意味、ABI、Kimi の公開宣言は変更しない。

### 9.2. 利用者に見える既存の挙動の変化

既定値は維持する。変わる挙動は次に限る。

- `build` は固定パスに EXE・obj・`O2.ll`・`inspection.txt` を書かず、成果物を記録に置く（§4.5）。
- 旧 schema の manifest と旧形式の記録は失敗し、再 emit・再ビルドが必要になる（§4.5）。
- `run --no-build` は四項目を照合し、Debug や DebugInfo が一致しなくても失敗する（§3.2）。
- `run <path.exe>` は設定の指定を拒否する。`run` は `--` 以降を引数として渡す（§3.2）。
- IR（`emit` の出力を含む）のソースパスが、最適化レベルと DebugInfo によらず論理パスになる（§4.1）。
- 名前付きの束縛が固定のスタック領域を持つ。リンクでは `/opt` を明示する（O0 は `noicf`）（§4.2・§4.3）。
- 記録はローカルの実パスを持ち、O2 の IR の秘匿化処理はなくなる。秘匿化は証跡の写しだけに行う（§4.5）。
- Ctrl+F5 は、起動設定の `debug`・`optimization`・`args`・`env` を使う（§5.2）。
- 既存の launch.json にある `"noDebug": true` の行は、削除する必要がある（§5.2）。

## 10. 参照資料

外部資料は実装方式の根拠であり、本書が対応済みと宣言するものではない。採用版で実証する。

- [LLVM のソースレベルデバッグ情報](https://llvm.org/docs/SourceLevelDebugging.html)
- [LLD の Windows／PDB 対応](https://lld.llvm.org/windows_support.html)
- [LLDB DAP の責務分担と起動](https://lldb.llvm.org/use/lldbdap.html)
- [公式 LLDB DAP 拡張](https://marketplace.visualstudio.com/items?itemName=llvm-vs-code-extensions.lldb-dap)
- [LLDB の値の表示拡張](https://lldb.llvm.org/use/variable.html)
- [LLDB の formatter bytecode](https://lldb.llvm.org/resources/formatterbytecode.html)
- [VS Code の extensionDependencies](https://code.visualstudio.com/api/references/extension-manifest)
