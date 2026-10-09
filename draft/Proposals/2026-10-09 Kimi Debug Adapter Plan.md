# Kimi 専用デバッグアダプターの仕様変更案・実装計画

日付：2026-10-09。状態：方針決定済み（利用者の承認による）。KD0 の試作は完了（2026-10-09。結果は `artifacts/verify/20261009-kd0/RESULTS.md`）。正式仕様への取り込み、KD1 以降の実装、動作検証は未実施。

## 1. 位置付け

本書に明記した変更は、[SPEC.md](../../docs/SPEC.md) およびその参照先（[IMPL.md](../../docs/IMPL.md) を含む）より優先する。本書で変更しない事項には、既存仕様を適用する。デバッグ対応によって、言語の合法性や実行時の意味は変わらない。

目的は、Kimigayo のプログラムを VS Code の F5 で起動し、ソース上で停止し、実行を制御し、値を確認できるようにすることである。デバッガーは Kimi 自身が持つ。`Kimi.exe` の `kimi dap` が Debug Adapter Protocol（DAP）を提供し、Win32 Debug API でデバッグ対象のプロセスを制御する。DAP、セッション、言語の表示を、LLDB や lldb-dap などの他のデバッガーに委ねない。Python や他社のデバッガー拡張にも依存しない。

本書は、2026-10-07 の LLDB 案を置き換える。LLDB 案は完了として閉じ、各項目の処置を [INTEGRATED.md](../INTEGRATED.md) に記録した。

調査と実験の証跡は `artifacts/verify/20261009-kimi-debug-adapter-research/` にある。根拠は次の二つの印で区別する。

- 【実験】：この証跡にある実験で確かめた事実。
- 【確認】：一次資料（Microsoft Learn、LLVM のソース、本リポジトリのソース）で確かめた事実。

### 1.1 用語

| 用語 | 意味 |
| --- | --- |
| アダプター | `kimi dap`。DAP を stdio で話す、Kimi 専用のデバッガー |
| デバッグ対象 | アダプターが起動して制御するプロセス |
| 起動対象 | 起動設定の `program` で選ぶ、プロジェクトまたはソース |
| ターゲット | ターゲットトリプル（`x86_64-pc-windows-msvc`） |
| 補助表 | コンパイラが出力する「Kimi デバッグ補助表」（§7）。言語の事実を持つ |
| デバッグ名 | 補助表と PDB を結び付ける名前。関数名はモジュール内で、変数名は関数内で一意（§6.1、§6.3） |
| 文の開始 | ブレークポイントとステップの停止点になる位置（§6.2） |
| ソースフレーム | 補助表に文の開始を持ち、origin が `project`、`package`、`kimi` のいずれかである関数のフレーム（`generated` と `runtime` は除く） |
| DebugInfo の成果物の組 | `DebugInfo=true` のビルドが書く `Name.debuginfo.*` のファイル群（§5.3） |
| KD0〜KD9 | 本書の実装段階（§11）。規則は KD-R、未決事項は KD-Q と表す |

### 1.2 取り込みと実装の順序

1. **試作**：KD0 の試作（§11）は P38 より前に行う。
   - 製品コードは変えない。コードは `temp/` の worktree とローカルブランチに置き、証跡は `artifacts/verify/` に置く。
   - KD0 は PLAN には登録しない。デバッグは付録 D の延期項目であり、延期項目は PLAN に載せないためである。セッションの記録は PLAN_HISTORY に残す。
2. **取り込み**：KD0 の結果を受けて、P38 より前に正式仕様へ取り込む（§14）。同じコミットで次のことを行う。
   - SPEC 付録 D から「debug information」を外す。
   - PLAN に Debugger track を「P38 の後」として登録する。
   - STATUS に、`kimi dap` と `--DebugInfo` を未対応として記録する。
   - `draft/INTEGRATED.md` に、本書の各節の取り込み先を記録する。未決事項が残れば、状態を一部取り込みとし、凍結する範囲を示す。

   取り込みに、実装の完了は要らない。
3. **実装**：KD1 以降の実装は、P38 の後に始める。
4. **完成**：初回の完成は KD9 の完了とする。STATUS は、支援の境界が変わる段階ごとに更新する。README の対応範囲は、検証が済んだ後にだけ更新する。

## 2. 範囲

### 2.1 初回の完成に含めるもの

- **対象**：Windows x64 の Application を、O0 で新規に起動してデバッグする。
- **操作**：F5、行ブレークポイント、継続、一時停止、ステップイン・オーバー・アウト、関数名付きのコールスタック、Stop、通常終了、Abort での停止。
- **値**：次の束縛について、名前、Kimi の型名、スコープ、取得できる値を表示する。
  - 引数、ローカル変数、ループ束縛、パターン束縛。
  - コンパイラが生成する本体（closure、ジェネリック具体化、Kimi ライブラリ）の中の束縛。
- **型表示**：基本型、ゼロサイズ値、構造体、タプル、固定配列、参照、`Array`、`Slice`、`UniqSlice`、`string`、`Dictionary`、enum。
- **入出力**：デバッグ対象の stdout と stderr は Debug Console に出す。stdin は空（EOF）とする。

### 2.2 対象外

**SPEC 付録 D の延期項目に加えるもの**
- Windows x64 以外の profile でのデバッグ
- attach、remote、core dump
- O2 でのデバッグ（`DebugInfo=true` は O0 に限る。§5.1）
- `kimi test` のデバッグ
- 対話的な stdin
- 実行中のデバッグ対象への Ctrl+C
- デバッグ対象のコードを実行しない範囲での、演算子や比較を含む式の評価
- 値の書き換え
- 条件付きブレークポイント、ログポイント
- Kimi ライブラリとパッケージのソースへのブレークポイント
- `obj` 系の動的な型の表示
- closure の捕捉値の表示
- 所有権と Loan の可視化
- 部分的に Move された束縛の部分的な表示

**既存の延期に従うもの**
- 非同期 task（付録 D の既存の延期項目）

**SETTLED に記録するもの**
- 関数呼び出しなど、デバッグ対象のコードを実行する評価は行わない（§14）。

VS Code の派生製品と他の DAP クライアントは、目標にせず、試験もしない。

### 2.3 動作要件

デバッグ機能の動作要件は Windows 10 以降とする。`WaitForDebugEventEx` と `DBG_REPLY_LATER` が Windows 10 以降の API だからである【確認】。コンパイラと生成物の動作要件は変えない。

## 3. 設計の規則

| 規則 | 内容 |
| --- | --- |
| KD-R1 所有 | DAP、セッション、言語の表示、Windows のプロセス制御は Kimi が持つ。他の OS のプロセス制御の方式は、その profile を追加するときに選ぶ（§4.4） |
| KD-R2 情報源 | 番地の事実（関数の範囲、行、変数の置き場所、unwind）は PDB と PE から読む。言語の事実（Kimi の名前、型とビュー、スコープ、文の開始、変数の状態）は補助表から読む。両者はデバッグ名と「論理パス＋SHA-256」の完全一致で結び付け、結び付けられなければセッションを始めない |
| KD-R3 非実行 | 表示と評価では、メモリとレジスターを読むだけにする。デバッグ対象への書き込みは次のものに限る：ブレークポイントの設置と撤去（元のバイトの復元と、保護の一時的な変更を含む）、TF（トラップフラグ）、戻り番地の監視に使うデバッグレジスター（DR0 と DR7）、巻き戻した RIP |
| KD-R4 停止の正規形 | 停止は二種類とする。イベント停止は `ContinueDebugEvent` を一つ保留した状態、サスペンド停止は全スレッドを `SuspendThread` した状態である。停止中のスレッドの文脈は、常に正規化しておく（int3 のヒットでは RIP を巻き戻し済みにする） |
| KD-R5 単一スレッド | デバッグ対象に触れる処理は、すべて専用のエンジンスレッドで行う |
| KD-R6 停止点 | ブレークポイント、next、step in は、文の開始で止まる。ただし関数から戻った直後（next、step out）は、戻り番地で止まる。Pause、ハードウェア例外、Abort は、実際の pc で止まる。戻り番地と実際の pc での停止は、§6.3 の「文の途中」の規則で表示する |
| KD-R7 生成の不変 | `DebugInfo=false` のビルドが生成するコード（IR、obj、EXE）は、現在と同一に保つ。manifest と記録は §5.3 の規則に従い、既定の設定のビルドでは現在と同一になる。`DebugInfo=true` が機械語を変えるのは、§6.5 に挙げた三点だけとする |
| KD-R8 範囲 | 範囲外の要求は推測で動かさず、理由を付けて拒否する |
| KD-R9 外部のコード | 外部のデバッガー、PDB リーダー、DAP の実装のコードは、仕組みを知るための参照にだけ使い、移植も複製もしない |

## 4. 全体構成

### 4.1 構成

```text
VS Code ── Kimi 拡張
  │  provider：信頼の確認、起動対象の選択、保存、キーの検証、
  │            DebugInfo のビルド（既存のタスク。出力はターミナル）
  │  factory ：DebugAdapterExecutable(Kimi.exe, ["dap"])
  ▼  stdio（DAP）
Kimi.exe dap
  ├─ DAP の受信と送信、セッション（状態の唯一の所有者）
  ├─ 読み込み：記録、EXE、PDB、補助表を照合する（ビルドはしない）
  └─ エンジンスレッド：CreateProcessW＋ジョブ、WaitForDebugEventEx、DbgHelp
          ▼
     Name.debuginfo.O0.exe（デバッグ対象）
        stdout と stderr はパイプ経由で Debug Console へ、stdin は NUL
```

### 4.2 F5 の流れ

1. **選択と保存**：拡張の provider が、既存の手順で次を行う。
   - 信頼済みワークスペースかを確かめ、信頼されていなければ拒否する。
   - 起動対象を一つの Application に確定する。
   - ソースを保存する。
2. **ビルド**：provider は、変数置換後の段階（`resolveDebugConfigurationWithSubstitutedVariables`）で、既存のタスクを使ってビルドする。
   - コマンドは `kimi build <起動対象> --Target x86_64-pc-windows-msvc --Optimization O0 --DebugInfo true --Debug <debug>`。
   - 出力は、Build と同じターミナルに出る。
   - 失敗した場合とキャンセルされた場合は、起動しない。
3. **アダプターの起動**：factory が `kimi dap` を stdio で起動する。
4. **照合**：launch を受けると、アダプターは DebugInfo の成果物の組を読み込んで照合する（§8.1）。そのうえで `initialized` を送る。
5. **デバッグ対象の起動**：setBreakpoints と configurationDone を受けると、エンジンは次の順に進める。
   1. デバッグ対象を `CREATE_SUSPENDED` で作り、ジョブに入れる。
   2. `ResumeThread` で動かす。
   3. `CREATE_PROCESS_DEBUG_EVENT` を受けたら、基底番地を確定してブレークポイントを置く。
   4. 継続する【実験】。
6. **停止中**：停止したら stopped を送る。stackTrace、scopes、variables、evaluate には、エンジンスレッドでメモリを読んで答える。
7. **終了**：デバッグ対象の出力を読み切ってから、exited と terminated を送る。

### 4.3 コンポーネント

| コンポーネント | 責務 |
| --- | --- |
| DAP の転送 | 受信には既存の `LspFrameReader` をそのまま使う。送信には `DapSender` を新しく作る（`LspSender` のポンプの構造を流用し、seq を送信順に振る）。JSON は source generation で扱う |
| セッション | 状態の唯一の所有者。寿命の状態機械、要求の振り分け、ブレークポイントの表、停止ごとに作り直す変数参照の表、イベントの順序を持つ |
| デバッグデータ | PE、PDB、補助表を読み込み、結び付け、索引を作る。読み込んだ後は変更しない。PDB リーダーは、コンパイラの結合検査（§6.4）と共有する |
| エンジン | 起動、イベントのループ、スレッドとモジュールの表、int3、ステップ、一時停止、例外の方針、出力の転送、終了 |
| スタック | DbgHelp の `StackWalkEx`。予備として、ソースフレーム用の自前の UNWIND_INFO v1 の unwinder |
| 値とビュー | 補助表の型に従ってメモリを読み、Kimi のビューを作る。上限、ページング、不正な値もここで扱う |
| パス式 | hover、watch、repl の式を、名前・フィールド・添字のパスとして解析する |

### 4.4 OS と CPU の境界

OS と CPU に依存する層は、最初から interface で分けておく。プロセス制御の粒度は、GDB Remote Serial Protocol の操作に合わせる（メモリ、レジスター、ブレークポイント、監視点、continue と step、停止の報告）。他の OS では、自前のバックエンドを作る方法と、lldb-server や debugserver を使う方法のどちらも選べるようにするためである。作るのは Windows x64 の実装一つだけとする。

```csharp
internal interface INativeDebugBackend : IDisposable      // OS ごと（Windows：Win32 Debug API）
{
    void Launch(DebugLaunch launch);                       // 作成し、ジョブに入れ、動かす
    bool TryWaitEvent(TimeSpan timeout, out DebugEvent e); // 停止、スレッド、モジュール、出力、終了
    int ReadMemory(ulong address, Span<byte> destination);
    void InsertBreakpoint(ulong address);                  // 踏み越しはバックエンドが受け持つ
    void RemoveBreakpoint(ulong address);
    void InsertReturnWatch(int threadId, ulong slot);      // 戻り番地のスロットの読み書き監視（Windows：DR0 と DR7）
    void RemoveReturnWatch(int threadId);
    void Resume(ResumeKind kind, int threadId);            // Continue、StepInstruction
    void SuspendAll();
    void Kill();
    RegisterSet GetRegisters(int threadId);
}
internal interface IDebugSymbols { }   // 行、関数、変数の置き場所、デバッグ名（Windows：PDB）
internal interface IUnwinder { }       // フレームとフレームごとのレジスター（Windows：DbgHelp）。ステップ用の CFA は .xdata から求める（§8.4）
internal interface ITargetArch { }     // ブレークポイント命令と PC の補正、ステップの判定、戻り番地（x64）
```

DAP の転送、セッション、補助表、ビュー、パス式、ブレークポイントとステップの方針は、OS の型（Win32 のハンドル、`CONTEXT`、PDB のレコード）に直接は触れない。

## 5. 設定と CLI（仕様変更）

### 5.1 設定

| 設定 | 種類 | 指定する場所 | check の有効設定 |
| --- | --- | --- | --- |
| `Debug` | 意味構成（`#if debug` の選択） | CLI `--Debug`、起動設定 `debug` | 入る（既存） |
| `Optimization` | 生成構成 | CLI `--Optimization`（新設）、`.kimiproj` | 入らない |
| `DebugInfo` | 生成構成（新設） | CLI `--DebugInfo` | 入らない |

- `DebugInfo` は真偽値で、既定は `false` とする。ProjectSnapshotId にも check の有効設定にも入れない（SPEC §18.5.2 の除外対象を「生成専用の設定」に広げる）。`.kimiproj` で指定できるようにするかは、後で判断する（KD-Q8）。
- `DebugInfo=true` は O0 に限る。`--Optimization O2 --DebugInfo true` は、emit、build、run のどれでも理由を付けて拒否する（延期項目。§2.2）。
- 有効値は「明示した CLI 値 → プロジェクト値 → 既定値」の順に一度だけ確定し、全工程で共有する。CLI の設定は、値ではなく「指定されたかどうか」で判定する。既存の `--Debug` も同じ扱いにそろえる（現状では、`build --Manifest` に `--Debug false` を渡すと黙って通る）。
- F5 は常に `Optimization=O0`、`DebugInfo=true` でビルドする。`Debug` は起動設定の値に従い、既定は `false` とする（§9.1）。

### 5.2 コマンド

| コマンド | 変更 | 段階 |
| --- | --- | --- |
| `emit`、`build`、ビルドを伴う `run` | `--Optimization O0\|O2` と `--DebugInfo true\|false` を受け付ける | KD1 |
| `run --no-build` | `Debug` と `DebugInfo` も記録と照合する（現状は Optimization だけ）。一致しなければ、違う設定名を挙げて失敗する。`--DebugInfo true` のときは、DebugInfo の成果物の組を選ぶ | KD1 |
| `build --Manifest` | `--Optimization` と `--DebugInfo` の指定も拒否する。生成構成は manifest から取る | KD1 |
| 設定の要約（IMPL §20.8.6.1） | CLI で上書きした Optimization と DebugInfo を表示する | KD1 |
| `run`（ビルドあり、`--no-build`） | `--` 以降を、アプリケーションの引数として渡す | KD5 |
| `kimi dap`（新設） | stdio で DAP を提供する。ビルドはしない。Windows 以外では理由を表示し、終了コード 1 で終わる | KD3 |

`run <path.exe>` は現状、設定の指定を黙って無視している。これを拒否する変更は本書に含めず、取り込みのときに PLAN の課題として記録する。

```powershell
# 追加予定の CLI。DebugInfo の成果物の組を作る
kimi build App.kimiproj --Optimization O0 --DebugInfo true
# その組を、引数付きで実行する（構成が違えば失敗する）
kimi run App.kimiproj --no-build --Optimization O0 --DebugInfo true -- input.txt
```

### 5.3 成果物と記録

- **成果物の組を分ける**：`DebugInfo=true` のビルドは、通常の組とは別の組に書く。

  ```text
  bin/<target>/
    App.ll, App.link.json, App.link.build.json, App.O0.exe, …          # 通常の組（変更なし）
    App.debuginfo.ll, App.debuginfo.kdbg.json, App.debuginfo.link.json # emit の出力
    App.debuginfo.link.build.json, App.debuginfo.O0.obj,
    App.debuginfo.O0.exe, App.debuginfo.O0.pdb                         # build の出力
  ```

  F5 と Ctrl+F5 は別の組を書くので、互いの成果物を上書きしない。接尾辞は、意味構成の `debug` と混同しないように、設定名に合わせて `debuginfo` とする。
- **設定の項目**：manifest と記録には、`debug` と `debugInfo` の項目を既定値と異なるときだけ書き、項目がなければ既定値とみなす。`optimization` は、従来どおり常に書く。
  - `debug` が記録にあるので、`run --no-build` は意味構成を照合できる。
  - `DebugInfo=true` に固有の項目も、true のときだけ書く。manifest の `debugTable`（ファイル名と SHA-256）と、記録の `pdbSha256`、`pdbGuid`、`pdbAge` である。
  - 必須の項目は増えないので、schema は改訂しない。
  - 既定の設定（`Debug=false`、`DebugInfo=false`）のビルドでは、manifest と記録が現在とバイト単位で同じになる。`debug` 項目のない既存の manifest は `debug=false` とみなすので、`Debug=true` で作った既存の manifest は再 emit が必要になる。
- **公開の手順**：既存の手順（一時ファイルで完成させてから置き換える）に従う。
  - emit は、IR、補助表、manifest の順に公開する（manifest が最後）。
  - build は、ビルドごとに新しいステージングディレクトリでリンクし（§6.4）、結合検査に合格してから、PDB、EXE、記録の順に置き換えて公開する。
  - `build --Manifest` は、manifest が参照する補助表を読むだけで、書き直さない。
- **ロック**：F5 のセッション中に拒否するのは、同じ起動対象への 2 回目の F5 だけとする。Build と Ctrl+F5 は別の組を書くので、デバッグ中も使える（§9.2）。不変の記録への移行は本書に含めず、PLAN に別の課題として記録する。

## 6. デバッグ情報の生成（仕様変更）

### 6.1 DI の規則（`DebugInfo=true` のとき）

生成に必要な対応（ソース文書、位置、関数、スコープ、変数）は、Lowering が `EmissionModule` に持たせる。LLVM writer は、AST、Binding、ownership state を参照しない。`DebugInfo=false` では、DI 用の表と文字列を集めない。有効なときも、ID とバッファを再利用する。

| 対象 | 規則 |
| --- | --- |
| モジュール | `DICompileUnit`（`emissionKind: FullDebug`、言語は `DW_LANG_C_plus_plus`）と、module flags の `"Debug Info Version" 3`、`"CodeView" 1` を出す |
| ファイル | `DIFile` は論理パス `/_/<root>/<名前>` とする（root は `project`、`package/<id>`、`kimi/<版>`、`generated`）。checksum は `CSK_SHA256`。DI にはローカルの絶対パスを書かない |
| 関数 | Kimi の本体から作るすべての関数に `DISubprogram` を付ける。`name` はデバッグ名、`linkageName` は既存の `__kimi_fN` |
| 関数のデバッグ名 | 正規の宣言表示（修飾名、generic 引数、多重定義を区別する引数型、closure などの決まった接尾辞）を使う。それでも重複する場合は、出力の順に `#n` を付けてモジュール内で一意にする。PDB に残る関数名は `name` だけである【実験】 |
| 生成補助関数、runtime の入口 | `__kimi_abort`、`__kimi_abort_message`、`__kimi_array_grow` などには、`DIFlagArtificial` の `DISubprogram` を付ける。ファイルは `/_/generated/runtime`、最初の命令の位置は 1 行目とする。行がすべて 0 の関数は CodeView から落ちるからである【実験】。補助表での origin は `runtime` で、ソースフレームではない |
| 位置 | DI を持つ関数のすべての命令に `!dbg` を付ける（§6.2）。行と列は 1 起点で、列は UTF-16 の位置とする。65535 を超える列は 0 とし、その文は補助表に行だけで記録する |
| 変数 | §6.3。`DILocalVariable` のスコープは常に `DISubprogram` とし、可視性は補助表で決める。`DILexicalBlock` をスコープにすると、範囲がブロックの命令に切り詰められ、gap ができる【実験】 |
| DI の型 | サイズだけが正しい最小限の型にする。Kimi の型の表示は補助表から行う。構造体のメンバーなど、他のデバッガー向けの充実は後で判断する |

### 6.2 文の開始（停止点）

- **文の単位**：次のものを一つの文とする。
  - ブロックの直接の項目
  - 制御文のヘッダー（if や while の条件、for の反復の取得、match の対象）
  - match の各 arm
  - `=>` の後の一つの文
- **命令の位置**：命令を生んだ operation のソース位置から、最も内側の文の単位をたどって決める。物理命令は operation の ID を持っているので、「operation の ID → 位置」の表を関数ごとに作れば足りる【確認】。
- **入れ子の文**：入れ子の文の後に、外側の文の続きの命令が来ることがある（例：`let x = if c {…} else {…}` の束縛）。続きの命令には、文の開始とは**異なる列**の位置を付ける。補助表は、関数ごとに文の開始の（行, 列）の一覧を持つ。行表のすべての項目に列があるので、この区別は PDB でも成り立つ【実験】。LLVM が関数の先頭に出す（scopeLine, 列 0）の項目は、文の開始ではない。
- **cleanup の位置**：
  - 暗黙の cleanup は、スコープを離れる辺の文の位置とする（脱出文ならその文、通常の脱出ならブロックの最後の文）。
  - defer の本体の各文は、それぞれ自分の位置を持つ。
  - 複数の辺で共有する cleanup には、スコープの終わりの位置（ブロックの最後の文の終わり）を付ける。この位置は文の開始ではないので、補助表の文の開始には含めない。line 0 にすると、配置の上で直前にある、別の経路の行に吸収されてしまう【実験】。

### 6.3 変数

- **スロット**：`DebugInfo=true` では、名前付きでサイズが 0 でないすべての束縛（ローカル変数、パターン束縛、スカラー引数）が、関数の入口に固定のスロットを持つ（IMPL §21.5.5 の例外）。O0 では、SSA の値に付けた DI は、使われなければ消え、spill されなければ短いレジスターの範囲しか持たない。どちらも LLVM の fast regalloc 次第になるからである【実験】。
  - 現状でも、名前付きのローカル変数とパターン束縛はスロット（`alloca`）を持っている【確認】。そのため、この規則で機械語が変わるのは、スカラー引数だけである。
  - この規則で、後の最適化の変更から変数の表示を守る。
- **変数のデバッグ名**：関数の中で同じ名前が重なる場合は、後のものを `x#2` のように番号付きにして一意にする。引数は `S_LOCAL` の IsParameter フラグで区別し、名前に印は付けない。
- **使わない形**：即値の定数（範囲のない `S_CONSTANT` になる）、SSA 値、二段以上の間接参照は使わない。表せない束縛には DI の変数を出さず、補助表に「所在を表せない」と記録する。
- **ゼロサイズの束縛**：所在を持たない。補助表に変数として記録し、ビュー `zero` で値として表示する（メモリは読まない）。結合検査の `S_LOCAL` の対象外とする。
- **部分 Move**：部分的に Move された束縛は、束縛全体を取得不可とする。
- **`noinit` 配列**：静的に Initialized なので通常どおり表示し、値の妥当性は保証しない（SPEC §4.3.4 の利用者の責任）。
- **状態の持ち方（KD-Q2。KD7 の最初の単位で、次の二案を実際の IR で比べて確定する。KD0 の結果は推奨案を支持する）**：
  - **推奨案**：置き場所はスロットへの `#dbg_declare` で表す。有効かどうかは補助表が持つ。
    - PDB では `S_DEFRANGE_FRAMEPOINTER_REL` として出る。範囲は LiveDebugValues の有無にも関数の大きさにも左右されないが、始まりが最初の文の開始より後になることがある【実験】。そのため、アダプターは範囲を使わず、オフセットだけを使う。0xF000 バイトごとに分かれた記録はつなぎ合わせる。
    - 補助表は、文の開始と呼び出し位置ごとに、所有権解析の静的な状態を記録する。
    - Initialized の変数は表示する。MayInit（合流点で初期化が不確定）と Moved の変数は、取得不可とする。実行時のフラグは読まない。
    - 文の途中では、文の開始で Initialized で、その文の中で状態が変わらない変数だけを表示する。
  - **代替案**：`#dbg_value` の開閉で、LLVM に範囲を作らせる。O0 でも LiveDebugValues が範囲をブロックの間で伸ばす。ただし、MBB が 10000 を超え、かつ DBG_VALUE が 50000 を超える関数では、LiveDebugValues が警告なしに飛ばされ、範囲は 0 バイトになる。また、llc の時間が推奨案の約 1.6〜1.8 倍になる【実験】。

### 6.4 リンク、公開、検査

```text
（cwd はビルドごとに新しく作るステージングディレクトリ）
lld-link App.debuginfo.O0.obj <libraries> /entry:__kimi_start /subsystem:console /nodefaultlib /Brepro
         /debug /pdb:App.debuginfo.O0.pdb /pdbaltpath:App.debuginfo.O0.pdb /pdbsourcepath:/_/build
         /opt:ref,noicf /out:App.debuginfo.O0.exe
```

- **リンクの引数**：`/debug`、`/pdb`、`/pdbaltpath`、`/pdbsourcepath`、`/opt:ref,noicf` は、`DebugInfo=true` のときだけ加える。`DebugInfo=false` のリンクの引数は変えない（KD-R7）。EXE には、PDB の公開名だけを埋め込む。
- **再現性**：`DebugInfo=true` のリンクは次の条件で行い、成果物はステージングから置き換えて公開する。
  - 新しいステージングディレクトリを cwd にする。
  - `/out` と `/pdb` には、公開名と同じ相対名を使う。
  - `/pdbsourcepath` を付ける。

  `/debug` を付けると、出力名か場所が違うだけで EXE が変わる。上の条件なら、場所によらず EXE と PDB がバイト単位で一致し、PDB にローカルのパスも残らない【実験】。
- **壊れたデバッグ情報**：LLVM は不正なデバッグメタデータを見つけると、警告を出してデバッグ情報を削除し、終了コード 0 で続行する【確認】。そのため `DebugInfo=true` では、IR を読むすべての LLVM の呼び出し（検証用の `opt` と `llc`）に `-disable-auto-upgrade-debug-info` を付ける。これで 0 以外の終了コードになる。llc は失敗しても 0 バイトの obj を残すので、成否は終了コードで判断する【実験】。
- **結合検査**：リンクの後、公開の前に、アダプターと同じ PDB リーダーで一時ファイルを検査する。
  - 補助表のすべての関数に、同じデバッグ名の手続きがちょうど一つあること。
  - DI を持つすべての変数に `S_LOCAL` があり、その DefRange が `S_DEFRANGE_FRAMEPOINTER_REL` だけで、つなぎ合わせた範囲が関数の終わりまで覆うこと。
  - `S_CONSTANT` がないこと。
  - すべてのソースの SHA-256 が一致すること。
  - オブジェクトにデバッグの節（`.debug$S`、`.debug$T`）があること。

  外れていれば、コンパイラの内部エラー（IMPL §21.3.5）として公開しない。利用者のプログラムを拒否するものではない。

### 6.5 `DebugInfo=true` による機械語の違いと、その検査

`DebugInfo=true` が機械語を変えるのは、次の三点だけとする。

1. 名前付きのスカラー引数に、スロットを加えること（§6.3）。
2. リンクで `/opt:ref,noicf` を使うこと。
3. `/debug` によって `.rdata` にデバッグディレクトリが入り、EXE の `.rdata` の配置、RIP 相対の変位、`.pdata` が変わること。obj の命令と再配置は変わらない【実験】。

`!dbg` などの DI は機械語を変えない。これを次の手順で検査する。

1. `DebugInfo=true` の IR から、`opt -strip-debug` で DI だけを除いた IR を作る。
2. 元の IR と、DI を除いた IR を、それぞれ llc で `.obj` にする。
3. 両者の `.text` の内容と再配置が一致することを確かめる（KD0 で一致を確認した【実験】）。

スロットはどちらの IR にもあるので、比べられるのは DI だけの違いになる。補助として、DI のない obj を同じ `/debug` の引数でリンクすれば、EXE の `.text` と `.pdata` も一致する【実験】。コンパイラに、試験専用の設定は作らない。

`DebugInfo=false` については、既存の試験と fixture の結果が変わらないことで確かめる。

## 7. Kimi デバッグ補助表

- **置き場所**：IR のファイル名の拡張子を `.kdbg.json` に替えたもの（例：`App.debuginfo.kdbg.json`）。emit の出力であり、manifest の `debugTable` から参照する。
- **形式**：JSON を `Utf8JsonWriter` で書き、一度の走査で読む。
- **内部形式**：同じ版のコンパイラとアダプターだけが使い、版をまたいだ互換は保証しない。版の一致は `compiler` で照合し、`schemaVersion` は形式の識別だけに使う。
- **絶対パスを持たない**：ホストの絶対パスは書かない。`/_/project` の実際の場所は、アダプターが起動対象から求める。Kimi ライブラリとパッケージのソースは、`source` 要求で内容を返す（§8.11）。
- **数値の出所**：数値はすべて、コンパイラの計算結果から書き出す（`AggregateLayoutPool`、`StructStorage`、Dictionary のハンドル定義など）。アダプターは物理的な定数を持たない。
- **runtime の引数**：runtime の入口の各引数は、レジスターと、間接参照の有無と、補助表の型で記述する。いずれも関数の ABI から出力する。

```json
{
  "schemaVersion": 1, "compiler": "0.1.1", "irSha256": "<…>",
  "sources": [
    { "path": "/_/project/src/Main.kimi", "sha256": "<…>" },
    { "path": "/_/kimi/0.1.1/Array.kimi", "sha256": "<…>", "embedded": "Kimi.Library.Array.kimi" }
  ],
  "runtime": { "abort": [
    { "debugName": "__kimi_abort", "reason": { "register": "rcx", "type": 0 },
      "location": { "register": "rdx", "type": 3 }, "locationLength": { "register": "r8", "type": 4 },
      "osError": { "register": "r9", "type": 4 } },
    { "debugName": "__kimi_abort_message", "message": { "register": "rcx", "indirect": true, "type": 2 },
      "location": { "register": "rdx", "type": 3 }, "locationLength": { "register": "r8", "type": 4 } } ] },
  "functions": [ {
    "debugName": "Main.sum(Array<i32>)", "display": "sum(values: Array<i32>) -> i32",
    "origin": "project", "form": "function", "source": 0,
    "statements": [[4, 5], [5, 5], [6, 9]],
    "variables": [
      { "debugName": "values", "name": "values", "type": 1, "scope": [3, 1, 9, 1] },
      { "debugName": "x", "name": "x", "type": 0, "scope": [4, 5, 5, 20] },
      { "debugName": "x#2", "name": "x", "type": 0, "scope": [6, 9, 7, 20] } ],
    "states": [ { "at": [5, 5], "initialized": [0, 1], "changedWithin": [] } ] } ],
  "types": [
    { "name": "i32", "size": 4, "view": "integer", "signed": true },
    { "name": "Array<i32>", "size": 24, "view": "array", "buffer": 0, "length": 8, "capacity": 16, "element": 0, "stride": 4 },
    { "name": "string", "size": 24, "view": "string", "data": 0, "length": 8, "release": 16 },
    { "name": "raw/u8", "size": 8, "view": "reference", "kind": "raw", "target": 5 },
    { "name": "i64", "size": 8, "view": "integer", "signed": true },
    { "name": "u8", "size": 1, "view": "integer", "signed": false } ]
}
```

- **origin と form**：`origin` は `project`、`package`、`kimi`、`generated`、`runtime` のいずれか。`form` は `function`、`instance`、`closure`、`default`、`drop`、`entry` のいずれか。
- **ビューの種類**：integer、float、bool、char、zero、string、struct、tuple、fixedArray、slice、array、dictionary、enum、reference（ref、uniq、raw）、object、opaque。
- **版**：v1（KD2）は sources、functions、statements、runtime と、runtime が参照する types（整数、raw、string）を持つ。v2（KD7）で、残りの types と variables、states を加える。

## 8. アダプター

### 8.1 読み込みと照合

アダプターは、`run --no-build` と同じ方法で設定と出力先を求める。プロジェクトの設定を読み（ソースは読まない）、launch の `debug`、`O0`、`DebugInfo=true` で確定して、DebugInfo の成果物の組を読み込む。

照合の規則は一つで、記録と manifest が持つ識別を、実際のファイルとすべて照らし合わせる。

- 記録が成功を示していて、設定が一致すること。
- EXE、PDB、補助表、IR の SHA-256 が、記録と manifest の値と一致すること。
- 補助表の `irSha256` が manifest の `irSha256` と一致すること。
- EXE の RSDS の GUID と age が、PDB と一致すること。
- 補助表の `compiler` が、アダプターの Kimi.exe の版と一致すること。
- 補助表とデバッグ名の結び付けが、§6.4 と同じ検査で成り立つこと。

一つでも外れれば、理由を付けて起動を拒否する。

### 8.2 起動と出力

- **フラグ**：`DEBUG_ONLY_THIS_PROCESS | CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT | EXTENDED_STARTUPINFO_PRESENT`。
- **ジョブ**：`ResumeThread` の前に、kill-on-close と例外時の終了（`0x2400`）を指定したジョブに入れる。この値は既存の `TestProcess` と同じである【確認】。
- **環境変数**：アダプターの環境に、launch の `env` と `_NO_DEBUG_HEAP=1` を加える。デバッガーが起動したプロセスは OS の debug heap を使うので、これを避けて Ctrl+F5 と同じヒープにする【確認】。
- **標準ハンドル**：`HANDLE_LIST` で 3 本に限る。stdout と stderr はパイプ、stdin は NUL とする。標準ハンドルを指定しないと、デバッグ対象の出力が DAP の stdout に混ざる【実験】。
- **出力の転送**：stdout と stderr は専用のスレッドで読み、UTF-8 の文字の途中で切らずに、まとめて output イベントにする。`EXIT_PROCESS` の後はパイプを最後まで読み切り、output、exited、terminated の順に送る。
- **ブレークポイントの設置**：`CREATE_PROCESS_DEBUG_EVENT` で基底番地を得てから置く（§4.2 の 5）。
- **モジュールのパス**：`hFile` に `GetFinalPathNameByHandleW` を使って求め、その後でハンドルを閉じる。`lpImageName` は信頼しない【実験】。
- **初期のブレーク**：ntdll のローダーが出す最初の `STATUS_BREAKPOINT` は、黙って継続する【実験】。
- **エンジンの異常**：エンジンスレッドはすべての例外を捕まえる。異常が起きたらデバッグ対象を `TerminateProcess` し、`EXIT_PROCESS` を受けるまで動き続ける。

### 8.3 ブレークポイント

- **解決**：
  1. 要求されたパスを正規化し、大文字と小文字を区別せずに論理パスへ変換する。
  2. その行の文の開始のうち、列が最小のものを選び、行表の番地に対応させる。番地は、ジェネリック実体ごとに一つずつできる。
  3. 行に文の開始がなければ、位置を動かさずに未検証として理由を返す。
  4. ディスク上のソースのハッシュが補助表と違えば、未検証として理由を返す。
  5. ブレークポイントを置けるのは、ディスク上にある project のソースだけとする。それ以外のソースへの要求は、位置を動かさずに未検証として理由を返す。
- **設置**：元のバイトを読み、`VirtualProtectEx`、0xCC の書き込み、保護の復元、`FlushInstructionCache` の順に行う。ヒットしたら、RIP を巻き戻す【実験】。
- **付け直し**：入れ子の状態機械で扱う。
  1. 元のバイトに戻し、TF を立て、他の全スレッドを `SuspendThread` で止める。スレッドごとの Suspend の回数は 1 に保ち、Resume と釣り合わせる。`SuspendThread` が ERROR_ACCESS_DENIED を返したスレッド（終了中のもの）は、止まっていないものとして扱う。付け直しの間に作られたスレッドも止める。
  2. 1 命令だけ進め、0xCC を入れ直してから、止めたスレッドを再開する。
  3. 止める前に別のスレッドで起きていた停止イベントが届いたら、`DBG_REPLY_LATER` で継続する。そのスレッドは手順 1 で止めているので、再開したときにイベントが再送される。付け直しが済んでから、そのイベントを報告する。

  KD0 では、10,000〜20,000 回のヒットで取りこぼしがなかった。他のスレッドを止めない対照では、ヒットを取りこぼした【実験】。
- **撤去済みの表**：外した番地を表に残す。保留中のイベントがその番地を指していれば、自分の int3 として RIP を巻き戻し、黙って継続する。判定は番地だけで行い、世代番号は表から消す時期を決めるためだけに使う。表がないと、デバッグ対象は異常終了した【実験】。
- **実行中の変更**：内部で一時停止してから書き換える。

### 8.4 ステップ

- **フレームの識別**：（関数, CFA）で識別する。
  - CFA は、呼び出し前の呼び出し元の RSP とする。本体での RSP に、`.xdata` から得た固定のフレームサイズと、戻り番地の 8 を足して求める。StackWalk は使わない。KD0 では 33,835 回の照合で外れがなかった【実験】。
  - prologue と epilogue の中では、この計算を使わない。
- **戻り番地の監視**：呼び出しを踏み越すときと step out では、戻り番地のスロットに DR0 の読み書き監視（8 バイト）を、そのスレッドにだけ設定する。
  - 監視するスロットは、step out では現在のフレームの `[CFA-8]`、踏み越しでは呼ばれた側のフレームの戻り番地のスロットである。
  - ret がスロットを読むと止まる。RIP が戻り番地で、RSP が期待した値なら、DR7 を解除して戻ったものとする。一致しなければ（OS の例外処理がスロットを読んだ場合など）、監視を残したまま黙って継続する。
  - 深いフレームは別のスロットを使うので、再帰の深さによらずイベントは 1 回で済む。KD0 では、fib(20) の踏み越しが 0.41 ms だった。CFA を条件とするソフトウェアのブレークポイントでは、イベントが 6,764 回起き、1.41 s かかった【実験】。
  - ユーザーのブレークポイントには DR を使わないので、DR0 はこの監視に専用で使える。例外の巻き戻しが監視中のフレームを越えると、ret は実行されない。その場合は例外の規則で止まり、監視を外す。
- **next**：範囲ステップで実装する。
  1. TF で、現在の文の番地範囲を出るまで、1 命令ずつ進める。
  2. 命令の直後に RSP が 8 減り、`[RSP]` が（直前の RIP, 直前の RIP+15]の範囲を指していれば、呼び出しとみなし、戻り番地の監視で踏み越す。`[RSP]` が現在の関数の中を指すかどうかで判定すると、関数ポインター経由で呼ばれた関数の入口の push を誤って呼び出しとみなした【実験】。
  3. RSP が変わらずに関数だけが変わる末尾ジャンプは、範囲の外に出たものとして扱う。O0 の Kimi のコードには通常現れないが、外部のコードには現れうる。
  4. 範囲を出たら、次の文の開始で止まる。関数から戻った場合は、戻り番地で止まる。
- **step in**：
  - 別の関数に入ったとき、それがソースフレームで、かつ justMyCode の条件（origin が `project`）を満たせば、その関数の最初の文の開始で止まる。
  - 条件を満たさない関数では、戻り番地の監視で呼び出し元へ戻ってから続ける。
- **step out**：戻り番地の監視で止まる。
- **端の場合**：
  - RSP が CFA 以上になったら、呼び出し元に戻ったと判断する。
  - ソースフレームでない関数へ戻る場合は、continue として扱う。
  - 最上位のフレームがソースフレームでない場合は、最初のソースフレームを基準にする。
  - ステップ中に例外が起きたら、TF と DR7 の解除、int3 の入れ直し、他のスレッドの再開を済ませてから、例外として扱う。
- **割り込み**：ユーザーのブレークポイント、Abort、例外、Pause のいずれかが起きたら、それを優先し、ステップを取り消す。取り消すときは、ステップ中のスレッドの TF と DR7 も解除する。

### 8.5 一時停止とスレッド

- **一時停止**（KD-Q3：SuspendThread に確定）：
  1. 全スレッドを `SuspendThread` する。
  2. `WaitForDebugEventEx(…, 0)` で保留中のイベントがないかを確かめ、あれば先に処理する。負荷の下では、1 回の停止で最大 2 件の保留イベントがあった【実験】。
  3. 停止中に作られたスレッドも、停止の対象に加える。

  スレッドを注入しないので、利用者のスレッドで止まる。KD0 では、中央値 0.011〜0.032 ms で、180 回すべてが主スレッドで止まった。`DebugBreakProcess` は中央値 0.6〜1.2 ms かかり、180 回すべてが注入したスレッドで止まった【実験】。
- **スレッドの一覧**：主スレッドとソースフレームを持つスレッドを先に並べ、それ以外は目立たせない。現在の Kimi はユーザースレッドを作れないので、Kimi のコードは主スレッドでしか動かない【確認】。

### 8.6 例外、Abort、終了

| 事象 | 扱い |
| --- | --- |
| Abort の入口（フィルター `abort`、既定でオン） | reason、os_error、message、位置を読み、`stopped(exception)` を送る。表示するのは最初のソースフレーム |
| ハードウェア例外（フィルター `fault`、既定でオン） | 番地が Kimi の EXE の中にあるときだけ first chance で止まり、それ以外は second chance で止まる。一度止まって継続した例外では、二度は止まらない |
| 自分のものでない int3（初期のブレークを除く） | 例外として止まる |
| その他の first chance | デバッグ対象に渡す |
| Stop（disconnect） | `TerminateProcess` を呼び、出力を読み切ってから、exited、terminated、応答の順に送る。defer と drop は実行しない |

### 8.7 スタック

- **DbgHelp の設定**（KD-Q4：DbgHelp の `StackWalkEx` に確定。自前の unwinder は作らない）：
  - オプション：`SYMOPT_IGNORE_NT_SYMPATH | SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS | SYMOPT_DISABLE_SYMSRV_AUTODETECT`。
  - `SYMOPT_DEFERRED_LOADS` は付けない。付けると、`StackWalkEx` が失敗を返さないまま誤ったフレームを返した【実験】。
  - 検索パス：空の専用ディレクトリを渡す。
  - モジュールの登録：すべてのモジュールを `SLMFLAG_NO_SYMBOLS` で登録する。シンボルを読むと PDB がロックされ、再ビルドを妨げた【実験】。
  - シンボルサーバーへの接続を避ける設定は、予防として残す。System32 の dbghelp は、symsrv を読み込まなかった【実験】。

  この設定で EXE を登録し、ブレークポイントの番地から始めれば、ntdll までたどれる。既知の戻り番地と不揮発レジスターを正解として照合すると、全件で一致した。温まった状態で 64 フレームの walk は 0.25〜0.41 ms だった【実験】。
- **unwind の範囲**：要求された段数までだけ unwind し、続きは次の要求で再開する。
- **行の求め方**：最上位のフレームは pc で、それより上のフレームは pc−1 で行を引く。ソースフレームでないフレームは「モジュール＋オフセット」で示し、目立たせない。

### 8.8 変数と表示の状態

- **見えるか**：補助表の scope が、現在の文の位置を含むかどうかで決める。
- **値があるか**：補助表の状態で決める（§6.3）。
- **置き場所**：`S_FRAMEPROC` で基準のレジスターを選び、`S_DEFRANGE` のオフセットを足す。
- **入口と出口**：最上位のフレームの pc が prologue か epilogue の中にあれば、「取得不可（関数の入口または出口）」とする。prologue の終わりは `.xdata` の SizeOfProlog から求める。`S_GPROC32` の DbgStart と DbgEnd は常に 0 なので使わない。`S_FRAMEPROC` の HasOptimizedDebugInfo は常に立つので、判断に使わない【実験】。
- **表示の状態**：次の四つだけとする。
  - 値
  - 省略付きの値（省略印あり）
  - 取得不可（理由）
  - 生の表示

  取得不可は、§6.3 と本節で定めた場合にだけ使う。実装していない表示の代わりに使ってはならない。

### 8.9 ビュー

| ビュー | 子要素 | 要約 | 検査する不変条件 |
| --- | --- | --- | --- |
| 基本型 | なし | Kimi の型名と値。isize と i64 も区別する | bool は 0 か 1、char は Unicode のスカラー値 |
| zero | なし | 型名と空の値（メモリは読まない） | — |
| string | なし | 長さと文字列。NUL はエスケープし、長い値には省略印 | 長さ、release、表示する範囲の UTF-8。違反は取得不可 |
| struct、tuple | 宣言順の値。言語上のフィールドでない成分は出さない | 型名 | — |
| Array | 初期化済みの要素を添字の順に（indexedVariables） | 型と長さ | 0 ≤ 長さ ≤ 容量 |
| 固定配列、Slice、UniqSlice | 添字の順に（indexedVariables） | 型と長さ | 長さ ≥ 0。長さが 0 でなければ、buffer が読めること |
| Dictionary | 挿入順の `[キー] = 値`（head から `links.next` をたどる） | 型と件数 | 件数を上限とし、終端と循環を検出する |
| enum | 有効な Case の payload だけ | 型と Case 名 | tag が範囲内にあること |
| ref、uniq | 参照先を 1 段だけ | 番地 | 読めること |
| raw | 展開しない | 番地と型 | — |
| object、opaque | なし | 型名と生の表示 | — |

- **子要素の扱い**：ビューは、要素の子値を作るだけである。要素の中身の整形は、要素の型のビューに任せる。走査の量は、表示する子要素の数に比例させる。
- **Dictionary**：停止中に到達したリンクの位置をキャッシュし、全ページの取得を O(n) に抑える。
- **上限**：文字列は 64 KiB まで読み、要約は 1,000 文字までとする。不変条件に違反した場合は、違反を含む最小の値を「取得不可（理由）」とする。空、ゼロ、別の Case に置き換えることはしない。
- **検出の範囲**：検査するのは、列挙した不変条件だけである。unsafe や raw の操作による破損や dangling を、完全に検出することは保証しない。

### 8.10 パス式

```text
path := name ( "." name | "." digits | "[" integer "]" )*
```

- **名前の解決**：現在のフレームで見える変数から、内側のスコープを優先して探す。
- **参照**：参照は `.` で 1 段たどる。raw はたどらない。
- **受け付けないもの**：演算、呼び出し、代入、キャストは受け付けず、エラーを返す（英語で、名前、`.フィールド`、`[添字]` のパスだけを評価できる旨を伝える）。
- **Dictionary のキー**：キーによる評価（`d["k"]`）は後で判断する（KD-Q6）。

### 8.11 DAP の能力、ソース、エラー

- **宣言する能力**：`supportsConfigurationDoneRequest`、`supportsEvaluateForHovers`、`supportsExceptionInfoRequest`、`supportsDelayedStackTraceLoading`、例外フィルター（`abort`、`fault`）。
- **宣言しないもの**：terminate、setVariable、条件付きブレークポイント、ログポイント、関数ブレークポイント、データブレークポイント。
- **`source` 要求**：ディスク上に実ファイルがないソース（Kimi ライブラリ、パッケージ）は、`source` 要求で内容を返し、補助表の SHA-256 で照合する。`/_/generated` は内容を持たないので、ソースのないフレームとして扱う。
- **ソースの変更**：開いているファイルとブレークポイントを置いたファイルについて、必要になった時点で、補助表の SHA-256 と比べる。違っていれば、ビルドの後にソースが変わったことを一度だけ知らせる。再ビルドは、利用者の再起動でだけ行う。
- **エラー**：launch の失敗と拒否は、DAP のエラー応答（`showUser`）で返す。文言は CLI と同じ英語とする（本書の説明は、文言の趣旨を示すもの）。
- **ログ**：テレメトリは追加しない。DAP の通信の記録は、起動設定の `trace` で明示的に有効にしたときだけ、拡張のログ用ディレクトリに書く。

## 9. VS Code 拡張

### 9.1 起動設定

```jsonc
{
  "type": "kimi",
  "request": "launch",
  "name": "Kimi: Debug",
  "program": "${workspaceFolder}/App.kimiproj", // 省略すると、既存の対象選択を使う
  "args": ["input.txt"],
  "env": { "KEY": "value" },
  "debug": false,        // --Debug（#if debug の切り替え）。既定は false
  "stopOnEntry": false,  // 入口の本体の、最初の文の開始で止める
  "justMyCode": true,    // project 以外の関数には step in しない
  "trace": false         // DAP の通信を記録する
}
```

- **共通のキー**：`program`、`args`、`env`、`debug` は、F5 でも Ctrl+F5 でも同じ意味を持つ。Ctrl+F5 は `kimi run` を次のように呼ぶ。
  - `debug` が指定されていれば `--Debug` として渡す。
  - `args` は `--` 以降に渡す。
  - `env` は、子プロセスの環境に加える。
- **デバッガー専用のキー**：`stopOnEntry`、`justMyCode`、`trace` は、Ctrl+F5 では使わない。
- **Kimi が決めるキー**：利用者が `cwd`、`optimization`、`debugInfo`、`target`（ターゲットトリプル）、`console` を書いたら、エラーにする。作業ディレクトリは、`kimi run` と同じくプロジェクトのディレクトリとする。
- **`debug` の意味**：`debug` はデバッガーとは関係なく、`#if debug` の切り替えである。このことを README と起動設定の説明文に書く。起動設定の `debug` が LSP の有効設定（`false`）と違う場合は、起動のときに一度だけ知らせる。

### 9.2 F5 の手順とロック

- **ビルド**：provider は、既存の CommandRunner の経路で DebugInfo のビルドを行う（§4.2）。ディレクトリのロックは、ビルドの間だけ取る。
- **二重の F5**：拡張は、デバッグ中の起動対象の一覧を持つ。同じ起動対象に 2 回目の F5 が来たら、すでにデバッグ中である旨を英語で示して拒否する。Build と Ctrl+F5 は、デバッグ中も使える。
- **古い Kimi.exe**：古い Kimi.exe は、未知のオプション `--DebugInfo` を入力パスと解釈し、「Input not found」で失敗する。ビルドか `kimi dap` の起動に失敗したときは、Kimi.exe が古い可能性があることも併せて案内する。

### 9.3 変更するファイル

| ファイル | 変更 |
| --- | --- |
| `package.json` | ラベルを「Kimi」に変える。launch のキー（§9.1）を加える。初期構成とスニペットから `noDebug` を除く。`contributes.breakpoints` に `kimi` を加える |
| `runDebug.ts` | noDebug 以外の起動を拒否する処理をやめる。factory を振り分ける（noDebug なら RunAdapter、それ以外は `DebugAdapterExecutable`） |
| `extension.ts`、`targetSelection.ts` | 信頼の確認、フォルダーの案内、起動対象の選択、保存を、F5 でも共通に通す |
| `commandRunner.ts` | DebugInfo のビルドの操作を加える。ロックを API として切り出す |
| `evaluatable.ts`（新設） | hover で送る式を、名前の連鎖に限る |
| `.vscode-test.mjs`、`tasks.test.ts` | integration 試験に `debug` ラベルを加える。拒否の試験を F5 の試験に置き換える |
| README、CHANGELOG | F5 の手順と、既存の `"noDebug": true` の行を削除する案内を書く |

拡張の版は、KD9 で 0.1.0 に上げる。minor 版の変更には利用者の指示が要る（`src/kimi-ext/AGENTS.md`）が、2026-10-09 に承認を得ている。

## 10. 試験と検証

| 層 | 内容 |
| --- | --- |
| 生成 | DI の有無、デバッグ名の一意性、文の開始、禁止した形が出ないこと、`DebugInfo=false` の出力が変わらないこと、`DebugInfo=false` では DI 用の表と文字列を割り当てないこと（Purpose=Allocation） |
| 不変性 | §6.5 の、DI を除いた `.obj` の比較 |
| 補助表、PDB リーダー | 書き出しと読み込みの往復。実際の PDB を DbgHelp の結果と照合する（DbgHelp は試験の正解として使う） |
| DAP プロトコル | 偽のエンジンで、順序、seq、エラー、参照の寿命、ページング、評価の拒否を確かめる |
| ビュー、パス式 | 合成したメモリで、各ビュー、上限、不正な値を確かめる |
| 実エンジンのシナリオ | ブレークポイント、next、in、out（再帰を含む）、Pause、スタック、変数（Move、部分 Move、合流、同名）、Abort、出力の順序、Stop の後にジョブが空になること。スタックは、戻り番地と不揮発レジスターが既知のデバッグ対象を正解として照合する（KD3） |
| CLI | `--DebugInfo`、DebugInfo の成果物の組、`run --no-build` の照合、`run --` の引数（`test-cli.ps1`） |
| 拡張 | unit 試験と、integration 試験の `debug` ラベル |
| 手動 | KD5 で、利用者が VS Code での表示を確認する |

- **verify.ps1 での扱い**：デバッガーのシナリオは、通常の functional 回帰として Session で実行する。実行するのは Windows だけで、Linux の CI では飛ばす。既定で除外する新しい区分は作らない。
- **CI の時間**：fixture の EXE、PDB、補助表は、一度だけビルドして共有する。Windows CI の追加時間は、5 分程度を目安にする。
- **NativeAOT**：確認は、利用者の指示を受けて KD3 と KD9 で行う。
- **測定**：Benchmark にエントリーを加え、測定の条件を文書にする。DebugInfo の有無ごとに、emit とリンクの時間、サイズを測る。アダプターについては、読み込みの時間と、停止してから応答するまでの時間を測る。
  - KD0 での基準値は、ブレークポイントの 1 回のヒットが中央値約 190 µs（そのうち `SetThreadContext` が約 130 µs で、避けられない）、TF の 1 ステップが約 143 µs、イベントの往復が約 6.3 µs だった【実験】。
  - µs 単位の固定の目標は置かない。設計の規則として、停止イベントの数を減らす（§8.4 の戻り番地の監視）。基準値は CI の機械で測り直す。
- **C で書くデバッグ対象**：試験用に C で書くデバッグ対象は、`-ffreestanding` を使うと unwind 表が出ないので、`-fasynchronous-unwind-tables` を付ける。Kimi の IR はこの影響を受けない【実験】。

## 11. 段階計画

規模の数値は、いずれも未検証の概算である。

| 段階 | 時期 | 内容 | 完了の条件 | 規模（製品／試験、行） |
| --- | --- | --- | --- | --- |
| **KD0 試作** | P38 の前 | KD0-P1：エンジン（SuspendThread、保留中のイベントと `DBG_REPLY_LATER`、複数スレッドでの付け直し、再帰での next の所要時間、DbgHelp の設定とフレームごとの文脈、出力の転送）。KD0-P2：手書きの IR で確かめる（`#dbg_declare` の範囲、`#dbg_value` の範囲と MBB の分割、line 0、人工の DISubprogram、続きの命令の列、DI を除いた比較） | すべての項目の採否を `artifacts/verify/` に記録し、KD-Q3 と KD-Q4 を確定する。KD-Q2 の材料を残す。その後、P38 の前に取り込む。**2026-10-09 に完了**（`artifacts/verify/20261009-kd0/RESULTS.md`）。KD-Q3 と KD-Q4 を確定し、結果を本書に反映した。取り込みは未実施 | 試作のみ |
| **KD1 CLI と成果物の組** | P38 の後 | §5.1、§5.2（`kimi dap` と `run --` を除く）、§5.3 の組の名前と `debug`・`debugInfo` の項目 | CLI の試験、`DebugInfo=false` の出力が変わらないこと | 0.4〜0.8k／0.3〜0.5k |
| **KD2 DI、補助表 v1、PDB リーダー** | | §6.1、§6.2、§6.4、§6.5、補助表 v1、PE と PDB のリーダー（手続き、`S_LOCAL`、checksum、RSDS）、§5.3 の `debugTable`・`pdb*` の項目と公開の手順。最初の単位で、DI の規則を実際の IR で確かめる | CodeView の検査、結合検査、不変性 | 2.0〜3.0k／1.0〜1.4k |
| **KD3 DAP とエンジンの核** | | `kimi dap`、境界の interface、読み込みと照合、起動、イベント、continue、pause、Stop、出力の転送、スタック（モジュール＋オフセット） | プロトコル試験、プロセス試験、終了コード、Stop の後にジョブが空になること、NativeAOT の確認（指示を受けて） | 2.5〜3.5k／1.0〜1.5k |
| **KD4 行とブレークポイント** | | 行表の結び付け、行ブレークポイント、ソース付きのスタック、stopOnEntry、例外と Abort の方針、`source` 要求、ソースの変更の検出 | DbgHelp との照合、各シナリオ | 1.5〜2.5k／0.6〜1.0k |
| **KD5 VS Code** | | §9 の全項目（§5.3 のロックを含む）、`run --` の引数 | 拡張の unit と integration、利用者による手動の確認 | 0.5〜0.8k／0.4〜0.7k |
| **KD6 ステップ** | | §8.4 の全項目 | 再帰、ループ、ライブラリの回避、例外との交差、Pause の後のステップ | 2.0〜3.0k／0.8〜1.2k |
| **KD7 変数** | | §6.3（最初の単位で KD-Q2 を確定）、補助表 v2、所在、可視性、基本のビュー、パス式、hover | Move、合流、同名、closure、generic のシナリオ。スロットを加えた後の不変性の再確認 | 2.2〜3.5k／1.2〜1.8k |
| **KD8 Kimi ビュー** | | §8.9 の残り、上限、不正な値 | 代表集合（通常、空、大きい値、入れ子、不正、読み取りの失敗） | 1.0〜1.5k／0.8〜1.0k |
| **KD9 仕上げ（完成）** | | 測定の確定、NativeAOT の確認（指示を受けて）、STATUS と README の対応範囲、拡張 0.1.0 | 完成の条件をすべて満たす | 0.2〜0.4k |

- **合計**：製品が約 1.2 万〜1.9 万行、試験が約 0.6 万〜0.9 万行。
- **代表集合**：KD7 の開始時に固定する。§8.9 の各型と、要素の種類（スカラー、ゼロサイズ型、Non-Copy 構造体、enum、参照、入れ子のコレクション）を組み合わせ、入れ子は深さ 1 と 3 を代表とする。以後、表示の対象となる型に新しい生成を加える単位では、同じ単位で表示の試験を加えるか、STATUS に表示の制限を記録する。
- **KD0 の結果**：すべての項目が成立し、代替（`DebugBreakProcess`、自前の v1 unwinder）は使わないことにした。`#dbg_value` 方式は §6.3 の代替案として残し、KD7 で確定する。試作から生じた修正は §15 にまとめた。

## 12. リスクと対策

| リスク | 対策 |
| --- | --- |
| 複数のスレッドで、保留中のイベントと付け直しが競合する | 入れ子の状態機械、`SuspendThread` と `DBG_REPLY_LATER`、撤去済みの表。KD0 の負荷試験で取りこぼしがないことを確かめた。KD3 と KD6 のシナリオで固定する |
| LLVM の CodeView の癖（`IsStatement` が出ない、line 0 の吸収、関数の脱落、範囲の切断、`#dbg_value` の範囲の上限） | 停止点と状態を補助表に持つ、人工の 1 行目、共有の cleanup にスコープの終わりの位置、変数のスコープは DISubprogram、結合検査、LLVM 22.1.8 に結び付けた試験 |
| ステップが遅い、あるいは期待とずれる | 1 回の停止イベントは約 0.2 ms かかるので、イベントの数を減らす。呼び出しの踏み越しと step out は戻り番地の DR 監視で 1 回のイベントにし、TF は現在の文の範囲だけに使う。規則は IMPL に書き、シナリオで固定する |
| DbgHelp の挙動（ネットワーク、PDB のロック、`SYMOPT_DEFERRED_LOADS` での誤ったフレーム、OS の版による違い） | §8.7 の設定、スタックの正解との照合試験（§10）。照合に失敗する OS の版が見つかれば、その時点で自前の unwinder を別の提案で検討する |
| 自前のエンジンと PDB リーダーの保守 | 部品を小さく保つ。境界の interface。LLVM の更新は profile の再検証に含め、そこでデバッガーのシナリオを必須にする |
| 範囲を広げる圧力（O2、attach、条件付き、他の OS） | KD-R8 で理由を付けて拒否する。延期項目は付録 D に置き、広げるときは別の提案にする |

## 13. 未決事項

| # | 事項 | 方針 |
| --- | --- | --- |
| KD-Q2 | 変数の状態の持ち方 | 補助表を推奨。KD0 の結果は推奨を支持した（`#dbg_declare` の置き場所は LiveDebugValues と関数の大きさに依存しない。`#dbg_value` は上限を超える関数で範囲を失い、llc が約 1.6〜1.8 倍遅い）。KD7 の最初の単位で確定 |
| KD-Q6 | Dictionary のキーによる評価（`d["k"]`） | 後で判断する |
| KD-Q7 | Ctrl+F5 も `kimi dap` に統合するか | 後で判断する |
| KD-Q8 | `.kimiproj` での DebugInfo の指定 | 後で判断する |

決定済みの事項は次のとおり。

- KD-Q1：CLI の名前は `kimi dap` とする。
- KD-Q3：Pause は SuspendThread で行う（KD0、§8.5）。
- KD-Q4：unwinder は DbgHelp の `StackWalkEx` とし、`SYMOPT_DEFERRED_LOADS` は付けない。自前の v1 unwinder は作らない（KD0、§8.7）。
- KD-Q5（Ctrl+C）：stdio 方式を採ったので不要になった。

## 14. 文書の変更

| 文書 | 変更 | 時期 |
| --- | --- | --- |
| SPEC 付録 D | 「debug information」を延期項目から外す（他の延期項目は残す）。§2.2 の項目を、延期項目として加える | 取り込み |
| SPEC §18.5.2 | ProjectSnapshotId の除外対象を、「生成専用の設定（Optimization、DebugInfo）」に広げる | 取り込み |
| SPEC §23 と docs/SPEC.md の索引 | 章の冒頭の範囲の文と §23.1 の構成に、デバッグ対象を実行するサービス `kimi dap` を加える。新しい節に、ターゲットに依存しない保証を置く：表示と評価でデバッグ対象のコードを実行しないこと、パス式の文法、表示の四つの状態、KD-R6 の停止点、範囲外の要求を理由付きで拒否すること。索引にこの節を加える | 取り込み |
| IMPL §20.8.1、§20.8.6.1 | `DebugInfo` の設定、設定の要約、暗黙のプロジェクトのパス一覧 | 取り込み |
| IMPL §20.8.3 | `debug` と `debugInfo` の項目は既定値と異なるときだけ書く規則（`optimization` は従来どおり）、`debugTable` | 取り込み |
| IMPL §20.8.4 | 手動手順の例を、コンパイラの実際の引数に合わせる。「`/debug` は行や変数の情報を作らない」の文を、DebugInfo の規則に置き換える | 取り込み |
| IMPL §20.8.6 | コマンドの表（`kimi dap`、`--Optimization`、`--DebugInfo`、`run --` の引数、`run --no-build` の照合、`build --Manifest` での拒否）、DebugInfo の成果物の組、公開の手順 | 取り込み |
| IMPL §20.8（新設の小節） | Windows x64 のデバッグ：DAP の能力、起動設定のキー、出力の方針、照合、例外の方針、補助表の形式と PDB との結び付け、動作要件 | 取り込み |
| IMPL §21.5（新設の小節）、§21.5.5 | DI の生成規則、リンクの引数、結合検査、`DebugInfo=true` による機械語の違い、`DebugInfo=true` では名前付きのすべての束縛がスロットを持つという例外 | 取り込み |
| docs/SETTLED.md | SETTLED の形式（Problem、Example、Proposed fix、Why not applied）で記録する。対象は三つ：DAP、セッション、言語の表示を LLDB や lldb-dap に委ねないこと。デバッグ対象のコードを実行する評価を行わないこと。F5 で `debug` の既定を true にしないこと | 取り込み |
| docs/dev/PLAN.md | Debugger track（P38 の後）、`run <path.exe>` の課題、不変の記録への移行の課題 | 取り込み |
| docs/STATUS.md | 取り込みでは未対応として記録する。その後は、支援の境界が変わる段階（KD1 以降）ごとに更新する | 取り込み、各段階 |
| docs/dev/VERIFICATION.md | デバッガーのシナリオの扱いと、測定のエントリー | KD3 |
| docs/dev/CODEMAP.md | Debugging（DAP、エンジン、シンボル、値）と DI の生成の行 | 各段階 |
| docs/dev/PLAN_HISTORY.md | KD0 と各段階の、セッションごとに数行 | KD0 以降 |
| draft/INTEGRATED.md | 本書の各節の取り込み先を記録する。未決事項が残れば、一部取り込みとして凍結する範囲を示す | 取り込み |
| README.md | 検証が済んだ範囲だけを書く | KD4 以降 |
| 拡張の CHANGELOG、package.json | F5、移行の案内、0.1.0 | KD5、KD9 |
| docs/LIBRARY.md、docs/STYLE.md | 変更しない | — |

言語の構文、型の同一性、Move と Borrow、例外と Abort の意味、ABI、Kimi の公開宣言は変更しない。

## 15. Kimigayo Principles による評価

| 原則 | 本書での扱い |
| --- | --- |
| 1. One Concept, One Canonical Form | コンソールの方式は stdio の一つだけにした。ビルドの経路は Build と同じタスク一つにし、アダプターはビルドしない。言語の事実は補助表、番地の事実は PDB と、情報源をそれぞれ一つにした。停止点は文の開始に限った。`debug` と `debugInfo` の項目は「既定値と異なるときだけ書く」の一規則にした。照合も一規則にした。`debug` は CLI の `--Debug` と同じ名前と型にし、成果物の接尾辞は `debuginfo` として意味構成と区別した |
| 2. Local Reasoning | 何を表示するかは、補助表と PDB を名前とハッシュで明示的に結び付けて決め、推測しない。照合に失敗したら、理由を付けて起動しない。変数の状態は所有権解析の静的な結果だけで決め、実行時のフラグは読まない。OS と CPU の層は、interface で局所化した |
| 3. Explicit Semantics | `DebugInfo=true` が機械語を変える点を三つに限って列挙し、`DebugInfo=false` のコードは変えない。Stop で defer と drop が実行されないこと、取得不可の理由、範囲外の要求を拒否する理由を明示する。`debug` は `#if debug` の切り替えだと明記する。記録が意味構成を持つので、`run --no-build` は構成の違いを黙って見過ごさない |
| 4. Compiler Server Protocol | 記録と manifest が、EXE、PDB、補助表、IR、ソースのハッシュを結び付けるので、照合できる。結合検査は、生成の誤りを公開の前に内部エラーとして検出する。DAP のエラー応答は、原因と対象を示す |

**評価で修正した点**

- **コンソールの方式を一つにした**：統合ターミナル方式と stdio 方式を併存させず、stdio 方式だけにした（原則 1）。対話的な stdin は延期項目とする。
- **ビルドを拡張側に置いた**：アダプターの中でビルドすると、ビルドの経路と出力先が二つになる。拡張が既存のタスクでビルドし、アダプターは照合だけを行う形にした（原則 1、2）。その結果、ビルドの結果を別に報告する経路も、成果物を保持する仕組みも要らなくなった。
- **通常の出力を変えないようにした**：スロットを常に持たせる案と、`/opt` を常に明示する案は、通常のビルドを変える。KD-R7 で通常のコードを変えないことにし、`DebugInfo=true` による違いを列挙した（原則 3）。デバッグ情報が機械語を変えないことは、DI を除いた IR との比較で確かめる（原則 4）。
- **設定の項目を一規則にした**：「`DebugInfo` の項目は true のときだけ書く」と「`run --no-build` で `Debug` を照合する」は、そのままでは両立しない。そこで「既定値と異なるときだけ書く」を `debug` と `debugInfo` に共通の規則とした（原則 1、3）。既定の設定のビルドの manifest と記録は、現在のまま変わらない。
- **合流点の扱いを一つにした**：合流点で初期化が不確定な変数は取得不可とし、実行時のフラグは読まない。表示の可否は、静的な状態だけで決まる（原則 2）。
- **形式に依存しない名前にした**：補助表のキーは `debugName` とした。他の OS で DWARF を使う場合にも、同じ結び付けが成り立つ（原則 2）。

**KD0 の結果で修正した点**

- **戻り番地での停止を一つの仕組みにした**：踏み越し、再帰、step out を、戻り番地のスロットへの DR 監視一つで扱う。CFA を条件とするソフトウェアのブレークポイントと、再帰用の特別な規則が要らなくなり、イベントは再帰の深さによらず 1 回になった（原則 1）。DR への書き込みは KD-R3 に明記した（原則 3）。
- **呼び出しの判定を局所的な事実にした**：「`[RSP]` が直前の命令の直後を指す」は、直前の命令だけで決まる。関数の範囲に頼る規則は、関数ポインター経由の呼び出しで誤った（原則 2）。
- **誤りを黙って返す設定を外した**：`SYMOPT_DEFERRED_LOADS` は失敗を返さずに誤ったフレームを返したので外し、スタックの正解との照合を試験に加えた（原則 3、4）。
- **cleanup の位置を明示した**：line 0 は別の経路の行に吸収され、停止点の意味が実際の経路と食い違った。共有の cleanup には、文の開始ではないスコープの終わりの位置を付ける（原則 2、3）。
- **機械語の違いを漏れなく列挙した**：`/debug` が EXE の `.rdata` と `.pdata` を変えることを、三つ目の違いとして加えた（原則 3）。DebugInfo=true のリンクは、固定の相対名、cwd、`/pdbsourcepath` で再現できるようにし、結合検査に DefRange の条件を加えた（原則 4）。

## 16. 参照資料

外部資料は、実装方式の根拠であり、本書の対応済みを宣言するものではない。外部のコードは参照にだけ使い、移植しない（KD-R9）。

- [Win32 Debugging Events](https://learn.microsoft.com/en-us/windows/win32/debug/debugging-events)
- [WaitForDebugEventEx](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-waitfordebugeventex)、[ContinueDebugEvent](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-continuedebugevent)
- [Process Creation Flags](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
- [StackWalkEx](https://learn.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-stackwalkex)、[SymSetOptions](https://learn.microsoft.com/en-us/windows/win32/api/dbghelp/nf-dbghelp-symsetoptions)
- [LLVM の PDB ファイル形式](https://llvm.org/docs/PDB/index.html)、[CodeView のシンボル](https://llvm.org/docs/PDB/CodeViewSymbols.html)
- [LLVM のソースレベルデバッグ情報](https://llvm.org/docs/SourceLevelDebugging.html)
- [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/specification)
- [GDB Remote Serial Protocol](https://sourceware.org/gdb/current/onlinedocs/gdb.html/Remote-Protocol.html)
