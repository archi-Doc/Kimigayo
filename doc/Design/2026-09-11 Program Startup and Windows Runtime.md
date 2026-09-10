# 起動処理・Windows Runtime・LLVM IR出力の仕様

策定日: 2026-09-11  
対象: Kimigayoコンパイラーの初期実行サブセット  
暫定基準: LLVM 22.1.5 / `x86_64-pc-windows-msvc`

本書は、トップレベル実行コードと明示的なmainによる起動、Runtimeの抽象操作、Windows APIへの接続、LLVM IRファイルの出力を定める。

会話で採用した方針をまとめ、実装に必要な細部を本書の初期実装規則として具体化する。設計の確定は、SPEC.mdへの統合やコンパイラーへの実装が完了したことを意味しない。既存SPECから変更する箇所は§11に示す。

## 1. 対象と実装範囲

### 1.1. 今回の到達点

コンパイラーは、意味解析と最終成立判定を通過したプログラムから、テキスト形式のLLVM IRファイル（`.ll`）を生成する。

| 項目 | 初期実装の方針 |
| --- | --- |
| 対象OS・CPU | Windows x64 |
| ターゲット | `x86_64-pc-windows-msvc` |
| LLVM互換性の基準 | LLVM 22.1.5 |
| 出力単位 | 1プロジェクト・1ターゲットにつき1つの`.ll` |
| Runtimeの供給 | 必要な補助関数を同じLLVMモジュール内に生成 |
| OS機能の利用 | Windows APIの外部宣言と呼び出しを生成 |
| オブジェクト生成 | 利用者が手動で実行 |
| リンク・生成物の起動 | 利用者が手動で実行 |
| 専用Runtime DLL | 初期実装では作成しない |

```text
Kimigayoコンパイラーの担当
├─ ソースの読み込み・構文解析
├─ Binding・型検査
├─ CFG・制御フロー解析
├─ 初期化・消費・Loan・Origin・lifetime・cleanup解析
├─ 最終成立判定
└─ Lowering・LLVM IR出力
   └─ ProjectName.ll

利用者が手動で実行
├─ LLVM 22.1.5によるオブジェクト生成
│  └─ ProjectName.obj
└─ lld-linkによるリンク
   └─ Application.exe
```

解析に未解決のobligationが残る場合、または選択済みの機能を生成器が扱えない場合は、診断を出して生成を失敗させる。未対応の処理を黙って省略したり、未解決の値を仮の成功値へ置き換えたりしない。

### 1.2. 最初に実行するプログラム

最初の動作確認は、通常関数、単純なローカル束縛、Unit、文字列リテラル、必要な所有権操作とcleanup、`Core.writeLine`を対象にする。

配列・Dictionary・継承・closure・static Propertiesの実行・汎用generic共有・複数Kotonohaのリンクは、この最初の実行確認の必須項目にしない。これらの既存言語規則を緩和するものではない。

ライブラリーの起動規則も本書で定めるが、安定した公開ABI、DLL生成、別プロジェクトからのロードまでを初期実装の完了条件には含めない。

## 2. 起動方式

### 2.1. 2種類の起動本体

アプリケーションでは、次のいずれか一方を起動本体とする。

1. SourceDocumentのトップレベル実行コード。
2. Root直下の明示的な`public func main() -> ()`。

両方式の混在は禁止する。どちらもない場合、または候補が複数ある場合はコンパイルエラーとする。

```text
起動候補の判定
├─ Application
│  ├─ トップレベル実行コードだけが1文書にある
│  │  └─ 非公開の暗黙起動本体を生成
│  ├─ 適格なpublic mainだけが1つある
│  │  └─ 明示的mainを呼び出す
│  ├─ 両方ある → エラー
│  ├─ 候補が複数ある → エラー
│  └─ 候補がない → エラー
└─ Library
   ├─ 起動候補を要求しない
   ├─ mainがあっても自動実行しない
   └─ トップレベル実行コードがあればエラー
```

### 2.2. 探索範囲とRootの意味

起動候補は、コンパイル時ディレクティブの選択後に、当該プロジェクト全体から探索する。依存ライブラリーのmainは候補にしない。ファイルの列挙順によって候補を選ばない。

本書の「Root直下の明示的main」は、ソースファイルの最上位に記述された公開関数宣言を指す。group・structの中のmainや、別の関数内のローカルmainは対象外とする。

初期実装では、明示的な`public main`を共有ルートに属する通常の関数宣言として扱う。ソースファイル内のaliasなど、宣言位置の名前解決環境は保持する。publicは言語上の可視性であり、同名のネイティブシンボルやDLL exportを約束しない。

この追加によって、すべてのトップレベルローカル関数を共有ルートへ移すことはしない。他のトップレベルローカル束縛・ローカル関数は既存のSourceDocument内のスコープを維持する。mainはトップレベルの実行時ローカル変数をcaptureできない。

### 2.3. トップレベル実行コード

「実行コード」は、最上位にある式、ローカル変数の初期化、制御文などの列を指す。専用の名前付きブロックを追加するものではない。

```kimi
// Main.kimi: これだけでアプリケーションになる。
::Core.writeLine("Hello, world!")
```

同じ文書内に複数の実行文がある場合、ソース順に実行する。別々の文書に実行コードがある場合はエラーとする。

関数宣言、Container宣言、aliasだけでは実行コードがあるとは判定しない。特に、main宣言自体をトップレベル実行コードとして二重に数えない。ローカル関数の宣言だけの文書も暗黙起動候補にしない。

空文書や、ディレクティブ選択によって実行コードがなくなった文書は候補にならない。何もしないアプリケーションは、空の文書ではなく、たとえば`()`という実行式で明示できる。

```kimi
// 正常終了する最小の暗黙起動本体。
()
```

暗黙の本体は、コンパイラー内部の非公開関数に変換する。ソースから呼び出せるpublic mainは合成しない。SourceDocument内の可視性とCodeContextを維持し、トップレベルの`return`は従来どおりエラーとする。

### 2.4. 明示的main

アプリケーションの明示的mainは、次の条件を満たす。

| 項目 | 条件 |
| --- | --- |
| 名前 | 小文字の`main`と完全一致 |
| 可視性 | `public` |
| 位置 | Root直下 |
| 引数 | なし。receiverもなし |
| 戻り値 | Unit。通常の規則で省略時にUnitとなる宣言も可 |
| generic・Originパラメーター | なし |
| 安全性 | 安全な通常関数。`unsafe func`は不可 |
| 本体 | 通常の関数本体を持つ。外部importやspecializationは不可 |

```kimi
public func main() -> ()
    ::Core.writeLine("Hello, world!")
```

通常の関数として、Unitを返す`return`や末尾到達が使える。起動時の呼び出しだけに特別な所有権規則は導入しない。

アプリケーション内のRoot直下のpublic mainは、起動用の署名として検査する。不適格な宣言は診断し、overloadの中から都合のよい候補だけを選ばない。ライブラリーではmainを通常の関数として扱い、アプリケーション用の署名を要求しない。

```kimi
// エラー: 暗黙方式と明示方式の混在。
::Core.writeLine("Top level")

public func main() -> ()
    ::Core.writeLine("Explicit main")
```

### 2.5. ライブラリー

LibraryにはOS向けの起動関数を生成しない。public mainがあっても、自動的に呼び出さない。

トップレベル実行コードは「実行せずに捨てる」のではなく、エラーとする。ライブラリーのコードが明示的に呼ばれた場合の通常の実行・cleanupは、言語の既存規則に従う。

## 3. OS側の起動関数と終了

### 3.1. mainCRTStartup

Applicationでは、LLVM上に外部リンケージを持つ`mainCRTStartup`を生成する。これはリンカーの`/entry:mainCRTStartup`から指定するOS側の起動関数であり、ソース言語のmainとは別の関数である。

```text
Windowsからの起動
└─ mainCRTStartup（コンパイラーが生成）
   ├─ 必要なRuntime初期化
   ├─ 起動本体を1回呼び出す
   │  ├─ 非公開の暗黙起動本体
   │  └─ または、明示的なmain
   ├─ 正常終了時の残りのcleanup
   └─ Runtime.Exit(0)
```

`mainCRTStartup`という名前によってCRTが自動初期化されるわけではない。今回の実装は独自起動処理を生成し、C/C++のCRT起動コードを前提にしない。根拠となるリンク設定は[Microsoft /ENTRY](https://learn.microsoft.com/en-us/cpp/build/reference/entry-entry-point-symbol)を参照する。

ソース言語の関数名は通常の名前変換を行い、`mainCRTStartup`やRuntime補助関数と衝突させない。内部名を利用者の名前解決空間に公開しない。

### 3.2. 正常終了

起動本体の通常のスコープ終了によって、そのローカル値をcleanupする。その後、実装対象に含まれる初期化済みstatic値を、既存SPECの逆初期化順でcleanupし、最後に`Runtime.Exit(0)`を呼ぶ。

起動本体がすでにcleanupしたローカル値を、起動関数がもう一度破棄してはならない。cleanupが終了しなければ、次のcleanupやプロセス終了へ進まない。

static Propertiesの実行をまだ実装していない段階では、必要な意味・実行機能を未対応として診断する。この設計書だけでstatic機能を実装済みとは扱わない。

### 3.3. Abort

Abortは、診断の出力を試みてから異常終了する。初期Windows実装の終了コードは`1`とする。

Abort開始後は通常のScope Exitやstack unwindingを行わない。cleanup中にAbortした場合も、その先のdefer・デストラクター・確保済み戻り値のcleanupを実行しない。詳細は[既存SPEC §17.3](../../SPEC.md#173-abort-termination)に従う。

## 4. Loweringと責務の分離

### 4.1. 解析から受け取るもの

Loweringは、確定した型、呼び出し先、評価順序、Copy／Move、初期化・破棄責任、Loan・Origin・lifetimeの成立結果、cleanup計画を受け取る。

所有権上の可否をLLVM生成中に再解釈しない。通常の転送では「結果の取得 → 離れるスコープのcleanup → 結果・転送の引き渡し」の順序を維持する。

ターゲット非依存の独立したLowered IRは必須にしない。検証済みCFGと意味情報から直接LLVM IRを生成してよい。

### 4.2. 実装要素

```text
意味解析・検証済みCFG
└─ LLVM Lowering
   ├─ 通常の式・関数・制御フローの生成
   ├─ Copy／Move・cleanupの生成
   ├─ Core組み込み実装の生成
   │  └─ Core.writeLine
   └─ Runtime抽象操作
      ├─ Windows向けの実装生成
      │  └─ WindowsRuntimeSymbols
      └─ 将来の別ターゲット向け実装
```

Runtimeはコンパイラー内部の抽象化であり、新しいソース言語の公開APIではない。WindowsRuntimeSymbolsは外部シンボルの定義情報を管理する。関数名だけの置換表にはしない。

## 5. Runtimeの6操作

### 5.1. 論理インターフェース

以下は実装用の擬似的な署名である。`ptr`は内部の生ポインター、`usize`は対象ターゲットの符号なしポインター幅整数を表す。ソース言語から直接呼び出す構文ではない。

```text
Runtime.Alloc(size: usize) -> ptr
Runtime.Free(ptr: ptr) -> ()
Runtime.WriteStdout(ptr: ptr, length: usize) -> ()
Runtime.Exit(code: u32) -> Never
Runtime.TryWriteStderr(ptr: ptr, length: usize) -> bool
Runtime.Abort(reason: DiagnosticText, sourceLocation: SourceLocation) -> Never
```

| 操作 | 成功時 | 失敗時 |
| --- | --- | --- |
| Alloc | 生メモリのポインターを返す | Abort |
| Free | 生メモリを解放する | 検出した解放失敗はAbort |
| WriteStdout | 指定された全バイトを出力して戻る | Abort |
| Exit | プロセスを終了する | 正常には戻らない |
| TryWriteStderr | 全量出力できればtrue | false。Abortを呼ばない |
| Abort | 診断出力を試み、Exit(1) | 診断失敗でも終了へ進む |

### 5.2. ソース位置の受け渡し

上記は論理APIであり、LLVM上の物理的な引数列を固定する公開ABIではない。

Alloc・Free・WriteStdoutから起きるAbortについても、元のソース操作を識別できるようにする。初期実装では、必要な補助関数へ非公開の診断コンテキスト引数を追加し、Loweringが静的なソース位置情報へのポインターを渡す方式とする。

```text
ソース上の操作
    ↓ Loweringで位置を関連付ける
Runtimeの物理的な補助関数(..., 診断コンテキスト)
    ↓ 失敗時
Abort(理由, 元の操作の位置)
```

診断コンテキストは、ソースの論理パス・行・列を含む。生成コードでは既存のCodeContextに基づくprovenanceを保持する。これはPDBやスタックトレースに依存しない。

### 5.3. AllocとFree

初期実装では、Windowsのプロセスヒープを使う。

- Allocは未初期化の生メモリを返す。言語上の変数をInitializedにする操作ではない。
- sizeが0の場合は、内部では1バイトの確保として処理する。言語上のUnit等のsizeを1へ変更しない。
- Windows x64の初期対応alignmentは16バイトまでとする。これを超えるalignmentが必要なヒープ配置は、対応する確保方式を追加するまで未対応診断とする。
- Free(null)は何もせず成功する。
- Freeには、同じRuntime allocatorから得た未解放の元ポインターだけを渡す。内部ポインターやリテラル領域のポインターを渡さない。
- デストラクターを実行してから必要な生メモリを解放する順序は、Loweringが生成したcleanupが担当する。Free自身はデストラクターを呼ばない。

所有権違反や二重解放を、Freeが必ず検出するとは保証しない。正しいポインターと解放責任は静的解析・生成コードの責務である。

概念上の処理は次のとおり。`site`は§5.2の非公開診断コンテキストを表す。

```text
Alloc(size) @ site:
    heap = GetProcessHeap()
    if heap == null:
        Abort("プロセスヒープを取得できません", site)
    memory = HeapAlloc(heap, 0, max(size, 1))
    if memory == null:
        Abort("メモリを確保できません", site)
    return memory

Free(memory) @ site:
    if memory == null:
        return
    heap = GetProcessHeap()
    if heap == null:
        Abort("プロセスヒープを取得できません", site)
    if HeapFree(heap, 0, memory) == 0:
        error = GetLastError()
        Abort(固定理由とerror, site)
```

HeapAllocのflagsは0とし、例外生成やprocess heapの同期無効化を要求しない。HeapAllocは失敗時にlast-errorを設定しないため、null時にGetLastErrorの値を確保失敗の理由として利用しない。alignmentなどのAPI契約は[Microsoft HeapAlloc](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapalloc)を参照する。

### 5.4. WriteStdoutとTryWriteStderr

両操作は、長さで指定されたバイト列を同期的に出力する。改行の追加、NUL終端の検索、文字コード変換は行わない。

| 項目 | 初期規則 |
| --- | --- |
| lengthが0 | ハンドル取得もポインター参照もせず成功 |
| lengthが正 | その範囲が読み取り可能で、呼び出し完了まで有効であること |
| 部分書き込み | 書けた分だけ進み、残りを出力 |
| 要求が残っているのに進捗0 | 失敗として扱う。無限に再試行しない |
| Runtime独自の出力バッファー | 初期実装では持たない |
| ポインターの保持 | 呼び出し後に保持しない |
| 完了の意味 | OSが全量を受け付けたこと。表示完了や永続化までは保証しない |

Windows側では、stdoutは`GetStdHandle(-11)`、stderrは`GetStdHandle(-12)`を使う。ハンドルがnullまたはINVALID_HANDLE_VALUEなら失敗とする。取得した標準ハンドルをRuntimeが閉じることはしない。[Microsoft GetStdHandle](https://learn.microsoft.com/en-us/windows/console/getstdhandle)

内部の共通書き込み処理は、成功・失敗と必要ならOSエラーコードを返す。WriteStdoutは失敗をAbortへ接続し、TryWriteStderrはfalseとして返す。

WriteFileには、32ビットの書き込み済みバイト数の格納先と、nullのOVERLAPPEDを渡す。長い入力はDWORDに収まる単位へ分割する。失敗時のGetLastErrorは、診断出力など別のAPI呼び出しより先に取得する。初期対応は同期標準ハンドルとし、非同期I/Oを提供しない。[Microsoft WriteFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-writefile)

### 5.5. Abortと診断

診断はstderrを使う。初期の表示形式は次の形とする。

```text
Main.kimi:3:5: abort: 標準出力への書き込みに失敗しました (win32=6)
```

これは表示例であり、エラーコード6をすべての出力失敗に使うという意味ではない。OSエラーコードが取得できない場合、その部分は省略する。

診断理由とソース位置は、定数データ・有効な入力文字列・小さな固定長の作業領域から出力できるようにする。数値変換のために新たなHeapAllocを必須にしない。任意長の診断を1つの動的文字列へ連結する必要もない。

明示的な`$abort(expression)`では、通常の言語規則でexpressionを一度評価し、その結果のstringを診断に使う。引数評価中の失敗や非終了は既存SPECに従う。Abort開始後にその文字列を通常のcleanupで破棄しようとしない。

```text
WriteStdoutの失敗、確保失敗、言語上の実行時検査の失敗
└─ Abort(理由, ソース位置)
   ├─ TryWriteStderrで診断の出力を試みる
   │  ├─ 成功 → 終了へ進む
   │  └─ 失敗 → 診断を打ち切って終了へ進む
   └─ Exit(1)
```

TryWriteStderrは、失敗してもAbort・WriteStdout・Allocを呼ばない。診断失敗による再帰を作らない。診断が一部しか出なかった場合も、通常実行には戻らない。同期I/Oの待機時間まで一定にする保証は含めない。

### 5.6. Exit

ExitはExitProcessへ接続する。戻り値はなく、呼び出し側のLLVM基本ブロックは`unreachable`で終える。

ExitはKimigayoのcleanupを実行しない。正常終了経路では呼び出す前に必要なcleanupを済ませ、Abort経路では通常のcleanupを通さず呼び出す。Windows自身が行うプロセス終了処理とは区別する。

## 6. WindowsRuntimeSymbols

### 6.1. 初期の7関数

```text
WindowsRuntimeSymbols
├─ メモリ
│  ├─ GetProcessHeap
│  ├─ HeapAlloc
│  └─ HeapFree
├─ 標準出力・標準エラー
│  ├─ GetStdHandle
│  └─ WriteFile
├─ エラー情報
│  └─ GetLastError
└─ プロセス終了
   └─ ExitProcess
```

各定義は、外部シンボル名、LLVMの戻り値・引数型、対象の呼び出し規約、dllimport属性、必要なリンク入力を保持する。必要な宣言だけをモジュールへ一度生成する。

### 6.2. LLVM宣言例

以下はWindows x64用の宣言部分であり、単独では実行プログラムにならない。

```llvm
target triple = "x86_64-pc-windows-msvc"

declare dllimport ptr @GetProcessHeap()
declare dllimport ptr @HeapAlloc(ptr, i32, i64)
declare dllimport i32 @HeapFree(ptr, i32, ptr)
declare dllimport ptr @GetStdHandle(i32)
declare dllimport i32 @WriteFile(ptr, ptr, i32, ptr, ptr)
declare dllimport i32 @GetLastError()
declare dllimport void @ExitProcess(i32) noreturn
```

| Windowsの型 | このターゲットのLLVM型 |
| --- | --- |
| HANDLE・各種ポインター | `ptr` |
| SIZE_T | `i64` |
| DWORD・UINT・BOOL | `i32` |
| LPDWORD | `ptr`。参照先の格納領域は32ビット |
| void | `void` |

WindowsのBOOLを`i1`で宣言しない。Runtime内部の論理boolとは別に変換する。また、LLVMの`HeapAlloc()`は「引数型を省略した宣言」ではなく「引数なしの宣言」なので使用しない。

実際のモジュールには、既存IrTargetから得た`target datalayout`も生成する。LLVM 22.1.5との整合を検証し、CPU/OSを無視した共通のDataLayoutを使わない。呼び出し規約は宣言側とcall側で一致させる。

`dllimport`はインポート参照の生成方法を指定する。必要なライブラリーを自動的にリンクする指示ではない。[LLVM DLL Storage Classes](https://llvm.org/docs/LangRef.html#dll-storage-classes)

### 6.3. GetLastErrorの扱い

last-errorを提供するAPIが失敗した直後に取得する。成功時の古い値をエラーとして扱わない。

初期診断では数値コードを表示する。FormatMessageWによるOSの説明文取得は追加しない。GetLastErrorもソース言語へ直接公開せず、Windows adapter内部で使う。[Microsoft エラーコードの取得](https://learn.microsoft.com/en-us/windows/win32/debug/retrieving-the-last-error-code)

## 7. stringとCore.writeLine

### 7.1. stringの初期内部表現

stringはUTF-8の所有値であり、Non-Copyである。初期実装では次の情報を持つhandleとして表す。

```text
StringHandle
├─ data: UTF-8バイト列へのポインター
├─ byteLength: バイト数
└─ releaseKind
   ├─ Static: コンパイラー生成の定数領域。解放しない
   └─ Heap: Runtime.Alloc由来の元ポインター。破棄時にFreeする
```

初期Windows実装の内部表現は`{ ptr, i64, i8 }`とし、releaseKindはStatic=0、Heap=1とする。padding・alignmentはDataLayoutに従う。これはコンパイラー内部の版付き表現であり、公開FFIや安定したバイナリーABIにしない。

通常関数間のhandleの物理的な受け渡しは、同一モジュール内で生成器が統一する。物理的なビットの転送だけを、言語上のCopyとして数えない。Move後は元の束縛から所有権・破棄責任が移る。

リテラルのbackingは定数領域へ置けるため、Hello worldにヒープ確保は必須ではない。定数領域が共有可能でも、string自体のNon-Copy規則は変わらない。動的文字列の構築は、対応する式を実装するときに追加する。

### 7.2. writeLineの契約

言語上の署名は既存SPECを維持する。

```kimi
public func writeLine(text: string) -> ()
```

これはCore宣言の署名例であり、利用者が同名の関数を作れば組み込みになるという意味ではない。Bindingは正規のCore KotonohaとSymbol Identityを確認する。

```text
Core.writeLine(text)
├─ 引数を所有値として一度取得
├─ Runtime.WriteStdout(data, byteLength)
├─ Runtime.WriteStdout(LFのアドレス, 1)
├─ textの通常の破棄
│  ├─ Static → backingは解放しない
│  └─ Heap → Runtime.Free(data)
└─ Unitを返す
```

本文とLFの連結用バッファーは必須にしない。本文が空でもLFは出力する。出力に失敗した場合はAbortし、通常の引数破棄へ進まない。出力済みのバイト列は巻き戻さない。

```kimi
public func main() -> ()
    let message = "Hello, world!"
    ::Core.writeLine(message) // messageの所有権を移す。
    // この後にmessageを再利用すると、消費後使用としてエラー。
```

UTF-8、NULをデータとして扱うこと、LFのみの付加、非atomicな出力、引数破棄の規則は[既存SPEC §22.4](../../SPEC.md#224-minimal-console-output)に従う。

### 7.3. 初期版のコンソール対応範囲

初期版は採用済みの7つのWindows APIでバイト出力を実装し、コンソールのコードページを変更しない。ファイル・パイプへのリダイレクトではUTF-8のバイト列をそのまま出力する。

Windowsコンソール上の非ASCII文字の見え方は、そのコンソール設定に依存する。全設定での日本語表示はこの初期版の保証に含めない。必要になった段階でGetConsoleMode・WriteConsoleW等を使うUnicode表示adapterを別途設計する。現在の契約を暗黙にUTF-16出力へ変更しない。

## 8. LLVM IR生成規則

### 8.1. モジュール構成

```text
ProjectName.ll
├─ target triple / target datalayout
├─ 定数データ
│  ├─ UTF-8文字列リテラル
│  ├─ LF
│  └─ 診断理由・ソース位置
├─ 必要なWindows APIのdeclare
├─ Runtime補助関数のdefine
├─ Core実装のdefine
├─ ユーザー関数・cleanupのdefine
└─ Applicationの場合だけmainCRTStartupのdefine
```

Runtime・Coreの専用補助関数は、外部から参照する必要がなければinternal/privateとして生成する。OSの起動シンボルだけはリンカーから到達できるようにする。

### 8.2. 起動関数のLLVM断片

以下は構造を示す断片である。`__kimi_entry_body`、`__kimi_shutdown`、`__kimi_runtime_exit`の本体は生成器が供給するため、この断片だけではリンクできない。

```llvm
declare void @__kimi_entry_body()
declare void @__kimi_shutdown()
declare void @__kimi_runtime_exit(i32) noreturn

define void @mainCRTStartup() noreturn {
entry:
  call void @__kimi_entry_body()
  call void @__kimi_shutdown()
  call void @__kimi_runtime_exit(i32 0)
  unreachable
}
```

暗黙方式ではentry bodyがトップレベルコードを実行し、明示方式では明示的mainへ接続する。必要なRuntime初期化がある場合は、entry bodyより前に追加する。初期のheap・標準ハンドル取得は各操作内で行えるため、空の初期化関数を必須にしない。

NeverをLLVMの戻り値型として作らない。戻らない処理は`void`の関数・`noreturn`属性・`unreachable`などで表現する。未解決の解析結果をunreachableへ置換しない。

### 8.3. 最適化と追加依存

初期の`.ll`出力で独自の高度な最適化は必須にしない。後続の最適化有無によって、言語上の検査・Abort条件・評価順序・cleanupの意味を変えない。

LLVMは、メモリ操作や大きなスタックフレームなどから、memcpy・memmove・memset・stack probe等の補助シンボルを要求する場合がある。これらはWindowsRuntimeSymbolsの7関数とは別の、コード生成に伴う依存である。

今回はこれらを新しいRuntime操作として追加しない。ただし、`/nodefaultlib`での手動リンク検証では未定義シンボルを確認する。依存が生じた機能は、適切な補助実装を供給するか初期対応から外して診断する。空の関数による代用や、必要なstack probeの無効化を解決策にしない。

「すべてのKimigayoプログラムがKernel32.libだけでリンクできる」とは保証しない。初期サブセットでは、§9の手動リンクが成立することを検証する。

## 9. 出力設定と手動ビルド

### 9.1. 初期の設定方針

| 設定 | 初期方針 |
| --- | --- |
| Targets | 初期対応は`x86_64-pc-windows-msvc` |
| OutputKind | ApplicationまたはLibrary。既定値はApplication |
| OutputPath | `.ll`の出力先。既定値は`bin/<target>/<ProjectName>.ll` |
| EntrySource | 初期版の選択手段にしない。§2の規則で一意に決定 |
| NativeLibraries | 今回はコンパイラーによるリンクに使用しない |

OutputKind・OutputPathは追加予定の設定として記述する。現行ProjectFileで利用できると主張しない。既存STATUSにある`.exe`出力・EntrySource選択案は、本書の初期実装範囲へ合わせて改訂する。

生成失敗時に、部分的な`.ll`を今回の成功出力として公開しない。以前の成功出力が残っている場合も、それを今回の成功として報告しない。

### 9.2. 手動コマンド

以下はコンパイラーが`rdtsc.ll`を生成した後に、利用者が実行する例である。LLVM 22.1.5のツールと、x64用Kernel32.libを解決できる環境を前提とする。

```powershell
llc -filetype=obj rdtsc.ll -o rdtsc.obj
lld-link rdtsc.obj kernel32.lib /entry:mainCRTStartup /subsystem:console /nodefaultlib /debug /out:app.exe
.\app.exe
```

`.ll`から`.obj`への変換はllcの仕事であり、その後にlld-linkへ渡す。[LLVM llc](https://llvm.org/docs/CommandGuide/llc.html)

Kernel32.libを検索できない環境では、利用者がライブラリーのパスを明示する。現段階のコンパイラーはLLVM・Windows SDKの探索、インストール、llc・lld-link・生成物の自動実行を行わない。

`/debug`だけでKimigayoソースの行情報や変数情報が生成されるわけではない。CodeView/PDB向けデバッグ情報の生成は別の拡張とする。Abort用のソース位置は§5.2により独立して保持する。

### 9.3. 成功の意味

| 段階 | 確認できたこと |
| --- | --- |
| Kimigayoの`.ll`生成成功 | 対応範囲の意味検査とIR生成が完了 |
| LLVMでの検証・オブジェクト生成成功 | LLVM 22.1.5が生成IRを受理し、オブジェクトへ変換できた |
| 手動リンク成功 | 必要な外部シンボルを解決し、実行ファイルを生成できた |
| 実行確認成功 | 実行ファイル自身の出力と終了状態が期待どおりだった |

`.ll`生成成功だけを、リンク済み・実行済みとして報告しない。

## 10. 検証項目

### 10.1. 起動候補

| ケース | 期待結果 |
| --- | --- |
| 1文書のトップレベルwriteLine | 暗黙方式で生成 |
| 1つのpublic main | 明示方式で生成 |
| トップレベル実行コードとpublic mainの混在 | エラー |
| 実行コードが複数文書に存在 | エラー |
| Root直下のpublic mainが複数存在 | エラー |
| 起動候補なしのApplication | エラー |
| 空文書を起動対象として指定して成功させる | 初期版では不可 |
| 不適格なmain署名 | Applicationで診断 |
| ディレクティブで候補が除外される | 選択後の候補集合で判定 |
| Libraryのmain | 自動実行・起動関数生成なし |
| Libraryのトップレベル実行コード | エラー |

### 10.2. 所有権と終了

- Move済みstringを再利用するとエラーになる。
- リテラルbackingにHeapFreeを発行しない。
- Heap由来の所有値は、正常経路で責任に従って一度だけ解放する。
- 正常return・スコープ終了では必要なdeferと破棄が実行される。
- Abortでは通常のcleanupを開始・継続しない。
- cleanup中の非終了によって、後続のcleanupと結果引き渡しが阻止される。
- 未解決の意味情報を持つ関数を生成しない。

### 10.3. 出力と失敗経路

- Hello worldのstdoutが、UTF-8の`Hello, world!`とLFの14バイトになる。
- 正常時のstderrが空で、終了コードが0になる。
- 空文字列でもLFを1つ出力する。
- NULを含む文字列や日本語を、ファイル・パイプへ正しいUTF-8バイト列として出力する。
- 部分書き込みでは残りを出力し、進捗0を成功扱いしない。
- 標準出力なし・書き込み失敗ではAbortする。
- stderrへの書き込み失敗がAbortの再帰を起こさない。
- 確保失敗の診断に、追加のヒープ確保を必須としない。
- Abort診断に理由と元のソース位置を保持する。

確保失敗・部分書き込みなどの再現には、テスト用adapterや注入可能なAPI呼び出しを利用してよい。本番用に採用する6操作・7APIを増やす必要はない。

### 10.4. LLVMと手動リンク

LLVM 22.1.5の検証器で、関数型、分岐先、基本ブロック終端、SSAの整合を確認する。手動でオブジェクト生成・リンク・実行まで確認し、未定義補助シンボルが残らないことを初期サブセットの検証に含める。

Debug/Release双方について、言語上の検査、出力、終了状態が一致することを確認する。最適化の有無で所有権上の可否を変えない。

## 11. 既存文書への反映箇所

本書の追加だけでは、以下の既存記述の更新は完了しない。

| 反映先 | 変更内容 |
| --- | --- |
| SPEC §6.1.1・名前解決規則 | Root直下の明示的public mainと、既存SourceDocumentローカル宣言を区別 |
| SPEC §14.11 | トップレベルreturn禁止を維持し、内部関数への変換と区別 |
| SPEC §22.2 | 二方式、混在禁止、候補一意性、Libraryの扱いへ改訂 |
| SPEC §22.2の空entry規則 | 設定された空文書を自動的に成功アプリとする規則を、初期版では採用しない |
| SPEC §22.4 | writeLineの言語契約を維持し、本書のWindows実装を参照 |
| SPEC Appendix A.1・B.1 | 暗黙起動本体の非公開性と、解析後のLLVM Loweringを整合 |
| STATUS C.12 | 当面のコンパイラー出力を`.ll`までとし、手動ビルド・実行確認を区別 |
| STATUS C.12の設定案 | OutputPathを`.ll`に変更し、EntrySourceは初期版から外す |
| STATUS C.13 | LLVM 22.1.5、独自entry、6操作・7APIの採用を記録 |

実装状況はSTATUSで管理する。本書に例があることや、設計が確定したことだけを根拠に、Parsing・Binding・Analysis・Lowering・Runtimeの実装済み範囲を拡大しない。

## 12. 将来の拡張

次の項目は、今回の採用関数に含めない。

```text
将来の拡張
├─ ビルド・実行
│  ├─ LLVMとWindows SDKの探索・配布管理
│  ├─ オブジェクト生成・リンク・runの自動化
│  ├─ デバッグ情報
│  └─ 複数モジュール・ライブラリーABI
├─ Runtime
│  ├─ より大きなalignment・再確保
│  ├─ ファイル入力・コマンドライン引数・時刻
│  ├─ コンソールのUnicode表示adapter
│  └─ 必要なコード生成補助関数の供給
└─ ターゲット
   └─ 他OS・他CPU向けのRuntime実装
```

別ターゲットを追加するときも、Alloc・Free・WriteStdout・Exit・TryWriteStderr・Abortの論理契約と、言語上の評価順序・所有権・cleanupを維持する。OS APIの違いはターゲット側の実装で吸収する。
