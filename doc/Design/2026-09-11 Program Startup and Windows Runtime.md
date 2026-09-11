# 起動処理・型レイアウト・LLVM IR出力・Windows Runtimeの統合仕様

策定・統合日: 2026-09-11

状態: 採用仕様。実装状況はSTATUS.mdで別途管理する。

対象: Kimigayoコンパイラーの初期実行サブセットと、その後の型対応に使う生成規則

基準: LLVM 22.1.5 / `x86_64-pc-windows-msvc`

本書は、起動方法、型のメモリ配置、通常関数と外部関数の受け渡し、LLVM IRの生成、Windows上のRuntimeを一つの流れとして定める。先行する「起動処理・Windows Runtime案」と「structレイアウト・LLVM Lowering案」の採用内容を統合し、実装に必要な初期規則を具体化した。

本書の採用は、SPEC.mdへの反映やコンパイラーの実装完了を意味しない。既存文書への反映箇所は§18、実装後の検証は§17にまとめる。

## 読み方と全体構成

```text
Kimigayoの実行プログラムができるまで
├─ 起動する本体を決める
│  └─ トップレベル実行コード または public main
├─ 意味を確定する
│  └─ 型 / 呼び出し先 / 評価順 / 所有権 / cleanup
├─ 機械上の表現を決める
│  ├─ #Layout: 値の格納配置
│  ├─ ValueLowering: 計算値と格納値
│  └─ FunctionAbi: 引数と戻り値の受け渡し
├─ LLVM IRへ変換する
│  ├─ 利用者の関数
│  ├─ Coreの実装
│  ├─ Runtime補助関数
│  └─ Windows APIの外部宣言
└─ 利用者が手動でビルド・実行する
   └─ .ll → .obj → .exe
```

| 読みたい内容 | 参照先 |
| --- | --- |
| 対象範囲・起動方式・終了 | [§1 対象](#section-1)、[§2 起動](#section-2)、[§3 終了](#section-3) |
| Loweringの全体設計 | [§4 責務の分離](#section-4) |
| 基本型・size・alignment | [§5 共通規則](#section-5) |
| レイアウト属性 | [§6 属性](#section-6)、[§7 Kimigayo](#section-7)、[§8 C](#section-8) |
| 外部接続と通常関数の受け渡し | [§9 FFI](#section-9)、[§10 内部ABI](#section-10) |
| LLVM命令・算術・最適化 | [§11 LLVM IR生成](#section-11) |
| Runtime・Windows API・文字列 | [§12 Runtime](#section-12)、[§13 Windows](#section-13)、[§14 string](#section-14) |
| 型対応の拡張 | [§15 型・機能](#section-15) |
| ビルド・検証・文書反映・将来拡張 | [§16 ビルド](#section-16)、[§17 検証](#section-17)、[§18 反映](#section-18)、[§19 拡張](#section-19) |

### 用語

| 用語 | 本書での意味 |
| --- | --- |
| Lowering | 意味検査済みの操作を、機械に近い表現へ変換すること |
| エミット | LLVMの型・命令・関数などを出力すること |
| Field / stored Property | 値を格納する領域を持つメンバー。computedは領域を持たない |
| storage / slot / Place | それぞれ、値の格納領域、個別の格納枠、言語上の値の位置を指す |
| aggregate | struct、Tuple、配列、enum、string handleなど、複数の部分で構成される値 |
| fragment | 一つのstructなどを複数箇所に分けて宣言したときの各宣言部分 |
| base subobject | 派生structに直接埋め込む基底struct部分 |
| payload / handle | それぞれ、実データ部分、データの場所などを保持する管理用の値 |
| Copy / Move | 元の値を残す取得 / 所有権と破棄責任を移す取得 |
| cleanup | defer、デストラクター、所有値の解放など、スコープ終了に必要な後始末 |
| Loan / Origin | 借用によるアクセス制約 / 借用元と寿命の関係を表す言語上の情報 |
| obligation | 最終成立判定までに解決する必要がある型・所有権・表現などの条件 |
| caller / callee | 呼び出す側 / 呼び出される側 |
| ABI | 引数・戻り値・シンボルなどを機械上で接続するための約束 |
| SSA | 各計算値を一度だけ定義するLLVMの値の表し方 |
| DataLayout | ターゲットごとのサイズ・alignment等をLLVMへ伝える情報 |
| Runtime | コンパイラーが生成する実行時補助処理。本書ではWindows APIへのadapterを含む |

`kimi`は言語の例、`llvm`はIRの例、`c`はC側の宣言・検証例、`text`は説明用の系統図または擬似コードである。LLVMの断片例は、明記したものを除き、必要な型・helper・モジュール情報を別途補う前提で読む。

<a id="section-1"></a>

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

### 1.3. 設計の採用と実装段階

本書の「初期方式」は、対象機能を実装するときに採用する具体的な規則である。記述した機能をすべて同時に実装済みとする意味ではない。

| 区分 | 内容 |
| --- | --- |
| 最小実行確認 | §1.2のプログラムから.llを生成し、手動でビルド・実行する |
| 本書で採用する配置・生成規則 | #Layout、基本型、struct、内部ABI、検査付き演算、Tuple・enumの初期方式 |
| 後続機能の設計課題 | object handle等の未確定な物理表現、C aggregate値渡し、export、callback、特殊配置 |

コンパイラーは実装していない機能を明確に診断する。特に、型のレイアウト計算ができること、関数の物理ABIが決まること、必要なRuntimeが実装されることを別々に確認する。

<a id="section-2"></a>

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

<a id="section-3"></a>

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

Abort開始後は通常のスコープ終了処理やスタックを遡る破棄処理（stack unwinding）を行わない。cleanup中にAbortした場合も、その先のdefer・デストラクター・確保済み戻り値のcleanupを実行しない。詳細は[既存SPEC §17.3](../../SPEC.md#173-abort-termination)に従う。

<a id="section-4"></a>

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

### 4.3. 配置・値の表現・関数ABI

[SPEC §21.1](../../SPEC.md#211-structure-layout-and-abi)には既にsize・alignment・stride、同一入力での再現性、有限なinline storage、物理配置と初期化・破棄順序の分離がある。Unitのsize 0・alignment 1・stride 0も確定している。

本書では、配置方式の選択、方式ごとの保証、Cとの互換性の判定を追加する。「Rustに準じる」は配置自由度を持つ保証モデルを意味し、rustcと同じoffsetやRust ABIを約束しない。

以下の3項目を別々に扱う。

| 項目 | 決めるもの |
| --- | --- |
| Storage Layout | 値をメモリに置くときのsize・alignment・stride・Field offset |
| Value Representation | boolやenum tagなどの有効なビット表現 |
| Function ABI | 引数・戻り値の物理型、間接渡し、呼び出し規約、シンボル |

同じメモリ配置でも関数の受け渡しが同じとは限らない。[Rust Reference](https://doc.rust-lang.org/reference/type-layout.html#layout.guarantees)

### 4.4. Loweringで使う情報

```text
選択・統合・意味検査済みのプログラム
├─ TypeLayout: 値をどこへ置くか
│  └─ size / alignment / stride / Field・baseのoffset
├─ ValueLowering: 計算値と格納値をどう対応させるか
│  └─ LLVM上の型 / bool等の変換 / 有効な値
├─ FunctionAbi: 関数境界をどう越えるか
│  └─ 引数 / 戻り値領域 / calling convention / 属性
└─ CleanupPlan: 何をいつ破棄するか
   └─ 初期化済み / Move済み / 所有権 / 論理的な破棄順
      ↓
LLVMの型・命令・関数・Runtime補助処理
```

同じ情報を別の場所で再計算して食い違わせない。特に、物理offsetの順にcleanupを並べ替えない。

<a id="section-5"></a>

## 5. レイアウトの共通規則と基本型

### 5.1. size・alignment・stride

本書では、型Tをメモリに置くための量を次のように表す。これらは仕様を説明する記法であり、利用者向けの新しい演算子ではない。

| 記法 | 意味 | 例 |
| --- | --- | --- |
| size(T) | paddingを含む、値の格納に予約するバイト数 | u64は8 |
| alignment(T) | 値の先頭アドレスに必要な整列単位 | 8ならアドレスが8の倍数 |
| stride(T) | 配列要素やポインターの歩進に使う間隔 | sizeをalignmentの倍数へ切り上げた量 |
| offset(F) | structの先頭からField Fの先頭までのバイト数 | NativeRecord.valueは8 |

alignmentは正の2のべき乗とする。sizeが0ならstrideも0となる。structの初期配置では末尾paddingもsizeに含めるので、sizeとstrideは等しい。

```text
structを連続して置く場合
├─ 1個目: 先頭 + 0 × stride
│  ├─ Field
│  ├─ Field間のpadding
│  └─ 末尾padding
├─ 2個目: 先頭 + 1 × stride
└─ 3個目: 先頭 + 2 × stride
```

paddingは整列のための余白であり、言語上のFieldではない。初期化済みの値でも、paddingが初期化済み・読取り可能・ゼロであるとは保証しない。

### 5.2. Windows x64の基本型表現

初期ターゲットはlittle endian、通常のアドレス空間は0、ポインター幅は64ビットとする。次の表を初期実装の対応表として採用し、LLVM 22.1.5の対象DataLayoutとの一致を検証する。

「格納型」はメモリ上のLLVM型、「計算型」はLLVM命令の値として使う型を指す。LLVMの整数型iN自身には符号の区別がないので、符号付き・符号なしの違いは比較・除算・変換などの命令選択に反映する。

| Kimigayoの型 | 格納型 | 計算型 | size / alignment / stride（バイト） |
| --- | --- | --- | --- |
| i8 / u8 | i8 | i8 | 1 / 1 / 1 |
| i16 / u16 | i16 | i16 | 2 / 2 / 2 |
| i32 / u32 | i32 | i32 | 4 / 4 / 4 |
| i64 / u64 | i64 | i64 | 8 / 8 / 8 |
| i128 / u128 | i128 | i128 | 16 / 16 / 16 |
| isize / usize | i64 | i64 | 8 / 8 / 8 |
| f32 | float | float | 4 / 4 / 4 |
| f64 | double | double | 8 / 8 / 8 |
| bool | i8 | i1 | 1 / 1 / 1 |
| char | i32 | i32 | 4 / 4 / 4 |
| raw unsafe/T | ptr | ptr | 8 / 8 / 8 |
| Unit: () | 物理格納を省略可能 | 通常の値を省略可能 | 0 / 1 / 0 |

これはメモリ表現の表であり、全演算の実装完了やLibraryImportでの許可を意味しない。たとえばi128の演算に補助関数が必要なら、その供給を確認する。Cとの交換で認める型は§9に限定する。safe borrowやobject handleの表現を、このraw pointerの行から推論しない。

#### 5.2.1. bool

格納されたboolの有効な表現は0x00と0x01だけとする。メモリから計算値への変換は、有効なboolであるという言語上の前提の下で行う。無効なバイトを丸めて有効なboolへ変換する機能ではない。

```llvm
; %slotは有効なKimigayo boolの格納先である。
%stored = load i8, ptr %slot, align 1
%flag = trunc i8 %stored to i1

; 計算結果を格納する場合は、0または1へ拡張する。
%canonical = zext i1 %flag to i8
store i8 %canonical, ptr %slot, align 1
```

Windows APIのBOOLは別型としてi32で扱い、返された値が0かどうかを比較する。WindowsのTRUEをKimigayoのbool格納領域へそのまま書き込まない。

#### 5.2.2. char

charはUnicode scalar valueを表す符号なし32ビット数として格納する。U+D800..U+DFFFとU+10FFFFを超える値は無効とする。メモリ表現をi32へ定めても、CのcharやWindowsのWCHARとの互換性は生じない。

### 5.3. 配列・ゼロサイズ値・有限な配置

固定配列 `[N of T]` は次の規則で配置する。

```text
size([N of T])      = checkedMultiply(N, stride(T))
alignment([N of T]) = alignment(T)
stride([N of T])    = size([N of T])
要素iのoffset       = checkedMultiply(i, stride(T))
```

Nが0でもalignmentはTのalignmentを維持する。Unit、ゼロ長配列、ゼロサイズ型の配列は格納バイト数が0になり得る。それでも式の評価、初期化状態、寿命、所有権、必要な破棄を残す。Unitのポインター歩進・indexに関する既存の禁止も維持する。

```kimi
struct Empty

struct LocalState
    var marker: ()
    var count: i32

// 型の例。いずれもCレイアウト指定はない。
struct Buffers
    var bytes: [4 of u8]
    var empty: [0 of u64]
```

初期配置ではEmptyのsizeは0、alignmentは1。LocalStateはmarkerの格納バイトを必要としないが、markerの論理的なFieldは存在する。

```text
型の格納依存
├─ Field / baseを値として直接埋め込む
│  └─ 再帰して有限なsizeを求める。循環して無限になるならエラー
└─ raw pointer / object handle / borrowを格納する
   └─ 参照先全体を埋め込まない。その辺はinline循環に数えない
```

### 5.4. 大きさの上限と確保可能性

初期Windows x64では、size・stride・有効なField offsetを最大isize、すなわち2^63 - 1以内とする。切り上げ・加算・乗算の途中でもoverflowを検査する。LLVMや実装上の制約でさらに小さい上限が必要な機能は、その上限と理由を診断する。

型のsizeが表現可能であることは、実行時にメモリを確保できる保証ではない。静的な配置不成立はコンパイルエラー、対応する確保操作の実行時失敗はAbortにする。

ヒープのalignment上限16はRuntime.Allocの制約である。型のalignmentを勝手に16へ丸めない。将来16を超えるalignmentの型が導入された場合、対応するヒープ配置は確保方式が追加されるまで未対応診断とする。

<a id="section-6"></a>

## 6. 属性の表記と適用

```kimi
#Layout("Kimigayo")
struct LocalRecord
    var kind: u8
    var value: u64
    var flags: u8

#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64
    var flags: u8
```

- `#Layout`をコンパイラーが認識する組み込みAttributeとする。
- 引数は非補間文字列リテラル1個。大文字小文字を区別し、`"Kimigayo"`と`"C"`だけを受理する。通常の名前解決や実行時の式評価は行わない。
- 既存Attributeの末尾カンマを許可する。引数の欠落・追加・名前付き指定・未知の値は診断する。
- 対象はstruct宣言。Field、関数、enum、group、contract、型の使用箇所には付けられない。
- 無指定は`"Kimigayo"`と同じ。明示指定によって別のType Identityを作らない。
- 同じfragmentへの重複指定は、同値でもエラーとする。
- `C`は所有権・Copy capability・可視性・コンストラクター・cleanup規則を変更しない。

文字列を使うのは既存`#LibraryImport`との整合と、`C`を通常の変数・型名として解決する曖昧さを避けるためである。`#Repr(C)`等の別表記は同時に追加しない。

### 6.1. 分割宣言・生成ソース・generic

fragmentごとの省略は「指定なし」として統合する。選択済みfragmentに明示指定があれば全体で共有し、なければKimigayoを選ぶ。別fragmentで同じ指定を繰り返すことは許可し、異なる指定はエラーとする。したがってC指定のstructに、属性を省略したメソッド用fragmentを追加できる。

Fieldの論理順序は既存SPEC §20.7.4に従う。Cレイアウトではこの統合済み順序が物理順序にもなる。実ファイルの列挙順・Bindingの処理順は使用しない。公開C型では、順序管理を簡単にするためstorageを1つのfragmentへ集めることを推奨するが、言語上の必須条件にはしない。

Mods完了・ディレクティブ選択・storage分類・generic置換後にレイアウトを確定する。確定後のField追加は禁止する。選択によってFieldが変わればABIも変わり得る。

generic宣言にも指定でき、全インスタンスが同じ方式を使う。ただしsize・alignment・offsetは具体的な引数型に依存する。方式が同じことをレイアウト同一性やgenericコード共有の根拠にしない。未確定のレイアウト条件はobligationとして保持し、必要な具体化で解決できなければエラーにする。

### 6.2. 正常例とエラー例

```kimi
// 属性の省略と明示的なKimigayo指定は同じ方式。
struct DefaultPoint
    var x: i32

#Layout("Kimigayo")
struct ExplicitPoint
    var x: i32

// エラー: モード名は大文字小文字を区別する。
#Layout("c")
struct BadMode
    var x: i32

// エラー: 引数が必要。
#Layout
struct MissingMode
    var x: i32
```

以下は同じstructの分割宣言である。メソッドだけを追加する側では属性を繰り返す必要がない。

```kimi
// A.kimi
#Layout("C")
struct Record
    var number: i32

// B.kimi
struct Record
    func read(self: ref/Self) -> i32 => self.number
```

B.kimiにも`#Layout("C")`を書くことはできる。`#Layout("Kimigayo")`を書くと、A.kimiとの競合になる。

```kimi
#Layout("C")
struct Pair<T>
    var first: T
    var second: T
```

Pair<i32>とPair<u64>はどちらもC方式だが、初期Windows x64ではsizeがそれぞれ8と16になる。Pair<()>はゼロサイズFieldの制限によりエラーになる。Pair<string>はC方式で配置できるが、C交換用の型ではない。

### 6.3. 宣言順の決定

分割宣言や生成コードを含む順序は、既存SPEC §20.7.4を次のように適用する。

```text
Fieldの論理宣言順
├─ 通常のソース
│  ├─ 正規化したプロジェクト相対の論理パス順
│  └─ 同じソース内では宣言順
└─ Modsが生成したソース
   ├─ ModIdのOrdinal順
   └─ 同じMod内では追加順、そのfragment内では記述順
```

ホストOSの大文字小文字変換やロケールに依存して並べない。ファイル名、ModId、Fieldの追加順を変更するとCレイアウトも変わり得るので、それらをABIに関わる入力として扱う。初期化の副作用順序も既存規則どおり変わり得る。

<a id="section-7"></a>

## 7. Kimigayoレイアウト

### 7.1. 言語として保証すること

- 各Fieldと直接の基底部分は、その型のalignmentを満たす。
- structのalignmentは直接埋め込む各部分のalignment以上とする。
- 直接埋め込む各部分の予約領域はstruct内に収まり、正のsizeを持つ部分同士は重ならない。内側の型のpaddingを外側のFieldで再利用しない。
- 物理的な並べ替えを許すが、Field Identity、初期化順、Partial Move、Loan、破棄順は変えない。
- 同一のコンパイラー・ターゲット・設定・選択済み入力から同じ結果を生成する。
- コンパイラー版や入力変更をまたぐ安定ABI、宣言順配置、最小size、base offset 0を保証しない。
- ゼロサイズの部分のアドレスは他の部分と一致してよい。論理的な初期化・寿命・破棄責任は別に保持する。

この保証モデルはRustのデフォルト表現を参考にするが、Kimigayo自身の契約である。[Rust Reference: Rust representation](https://doc.rust-lang.org/reference/type-layout.html#the-rust-representation)

### 7.2. 初期実装の方式

初期版は直接の基底部分を先頭に置き、そのstruct自身のFieldを論理順に自然alignmentで配置する。末尾をstruct alignmentへ切り上げ、structではsize = strideとする。基底部分も含めてすべての部分がsize 0なら、size・strideを0、alignmentを各部分の最大値（部分がなければ1）とする。

将来、Fieldをalignment降順などに並べ替えてよい。初期版がCに似た配置になっても、Kimigayo指定にはC互換保証が生じない。並べ替え最適化の実装を最初のLoweringの前提にしない。

<a id="section-8"></a>

## 8. Cレイアウト

### 8.1. 保証の対象

`C`は、選択したターゲットの通常のC struct配置規則を使う。初期版はWindows x64 MSVC ABIに対応し、packing・明示alignment変更・bit-fieldを使わないC宣言を基準とする。別CPU・OS・ABI間で同じバイト列を保証するものではない。[Microsoft x64 ABI conventions](https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170#x64-type-and-storage-layout)

外側のC指定は内側の型のレイアウトを変更しない。内側がKimigayoなら、その内側の実装依存性は残る。このため「Cの配置規則を選んだstruct」と「Cとの交換が可能なstruct」を区別する。[Rust Reference: inter-field layout](https://doc.rust-lang.org/reference/type-layout.html#layout.repr.inter-field)

### 8.2. 配置アルゴリズム

論理順に並ぶField `F0 ... Fn`について、次を実行する。

```text
cursor = 0
aggregateAlignment = 1
for each Field F in logical declaration order:
    cursor = checkedAlignUp(cursor, alignment(F.Type))
    offset(F) = cursor
    cursor = checkedAdd(cursor, size(F.Type))
    aggregateAlignment = max(aggregateAlignment, alignment(F.Type))

alignment(S) = aggregateAlignment
size(S) = checkedAlignUp(cursor, aggregateAlignment)
stride(S) = size(S)
```

すべての計算をoverflow検査付きで行い、ターゲットの最大オブジェクトサイズを超えれば診断する。static storage・computed・関数はinstance Fieldに含めない。stored Propertyのカスタムaccessorは、そのstorage slotの有無や順序を変えない。

通常の自然配置例として、上のNativeRecordはWindows x64で次になる。[Microsoft x64 structure alignment](https://learn.microsoft.com/en-us/cpp/build/x64-software-conventions?view=msvc-170#x64-structure-alignment-examples)

| 内容 | offset | バイト数 |
| --- | ---: | ---: |
| kind | 0 | 1 |
| padding | 1 | 7 |
| value | 8 | 8 |
| flags | 16 | 1 |
| tail padding | 17 | 7 |

size 24、alignment 8、stride 24。Kimigayo方式の将来の並べ替えでは、value=0、kind=8、flags=9、size 16となる配置も許されるが、この結果自体は保証しない。

### 8.3. 初期制限

- `open struct`および基底型を指定したstructへのC指定は禁止する。CにはKimigayoの継承・receiver調整に対応する共通契約がない。
- 空structおよび直接のゼロサイズFieldを持つC structは初期版ではエラーとする。Unit・ゼロ長配列をCの非標準拡張へ暗黙対応させない。
- enum・union・bit-field・flexible array member・packed・明示offset・明示alignment・transparentは今回追加しない。
- 通常のメソッド・computed・constructor・deinitはレイアウトを変えないため、それだけではC指定を禁止しない。ただし外国語側による構築・破棄の適格性は別契約である。
- object allocationのヘッダーや参照カウントはpayloadの外に置く。C指定が固定するのはowned struct payloadであり、`obj/S`等のhandleやallocation全体のABIではない。

### 8.4. 内側の型とC側の宣言

```kimi
struct Inner
    var value: i64

#Layout("C")
struct Outer
    var inner: Inner
    var flags: u8
```

```text
Outer: C配置
├─ innerの開始offsetはC方式で決定
│  └─ Innerの内部はKimigayo方式のまま
└─ flagsの開始offsetはInnerのsize・alignmentを使って決定
```

OuterへC指定をしても、InnerをC型へ変換しない。Outer全体をC交換用にするには、InnerにもC指定を付け、さらに§9の再帰的な型条件を満たす必要がある。

NativeRecordに対応するC側の宣言例は次のとおり。通常のpacking条件でコンパイルする。

```c
#include <stdint.h>
#include <stddef.h>

typedef struct NativeRecord {
    uint8_t kind;
    uint64_t value;
    uint8_t flags;
} NativeRecord;

/* Windows x64の想定配置をC11以降のコンパイラーで確認する例。 */
_Static_assert(offsetof(NativeRecord, kind) == 0, "kind");
_Static_assert(offsetof(NativeRecord, value) == 8, "value");
_Static_assert(offsetof(NativeRecord, flags) == 16, "flags");
_Static_assert(sizeof(NativeRecord) == 24, "size");
_Static_assert(_Alignof(NativeRecord) == 8, "alignment");
```

これはC側で行う配置の検証例であり、Kimigayoにsizeof等の公開構文を追加するものではない。pragma packや明示alignmentのあるC宣言とは、この例の互換性を主張しない。

### 8.5. 初期制限の例

```kimi
// エラー: C方式の空struct。
#Layout("C")
struct EmptyNative

// エラー: C方式の直接Fieldがゼロサイズ。
#Layout("C")
struct NativeMarker
    var marker: ()
    var code: i32

// エラー: C方式とopenは併用しない。
#Layout("C")
open struct NativeBase
    var code: i32
```

これらはKimigayo方式の空struct、Unit、継承の一般的な禁止ではない。C方式の初期対応範囲を限定する規則である。

<a id="section-9"></a>

## 9. C互換性とFFIの境界

### 9.1. C互換なstorageの判定

初期のC交換用判定を次のように定める。これは説明上の判定名であり、新しい利用者向けContractを追加するものではない。

| Fieldの完全な型 | 初期のC交換用storage |
| --- | --- |
| owned i8/u8、i16/u16、i32/u32、i64/u64 | 許可。対応する固定幅C整数と対応づける |
| owned f32/f64 | 許可。対象ABIのfloat/doubleと対応づける |
| raw `unsafe/T` | 許可。ポインター値の表現だけを保証 |
| 正の長さの固定配列 | 要素がC交換用storageなら許可 |
| owned C指定struct | 全Fieldが再帰的に条件を満たす場合に許可 |
| Kimigayo指定struct、Tuple、enum、string、動的collection | 初期対象外 |
| bool、char、i128/u128、isize/usize | 対応するC型を別途定めるまで初期対象外 |
| safe borrow、object handle、function/closure値 | 初期対象外 |

ポインターの参照先がC互換である必要はない。opaque pointerとして扱える一方、C側が参照先を読む・書く場合にはそのレイアウトと有効値の契約が別途必要となる。

たとえばC指定structのFieldにstringを持たせること自体は配置計算が可能なら許可するが、その型をC交換用storageとは判定しない。外側のAttributeによって文字列marshallingを挿入しない。

初期の「C側が値を構築・上書きしてKimigayoが受け取る」用途では、さらに再帰的に独自deinitを持たない型に限定する。layout互換だけではconstructor不変条件、外部ポインターの寿命、所有権移転、accessor迂回の可否は証明できない。既存のunsafe契約が必要であり、C指定をCopyの自動付与にも使わない。

### 9.2. 関数の値渡しは別段階

現行SPEC §22.3のLibraryImportはaggregate引数・戻り値を許可していない。今回Cレイアウトを追加しても、この制限を自動解除しない。最初は既存のraw pointer署名でstructのアドレスを渡す用途から対応できる。raw pointerだから全pointeeを一律C互換検査する、という変更も行わない。

後でstruct値渡しを追加する際は、引数と戻り値をそれぞれ分類するTarget C ABI loweringが必要になる。Windows x64では小さいstructの整数としての受け渡しや、それ以外の間接引数などがあり、Cの署名をLLVMのaggregate型へそのまま写すだけでは足りない。[Microsoft x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170)

Cレイアウトはネイティブexport、名前mangling、calling convention、DLL互換性、ファイル保存形式を固定しない。ABIの互換性を比較する場合は、ターゲットABI、C互換な各Field型、統合済みField順・内容、配置オプションが同じことを確認する。

### 9.3. raw pointerによる接続例

以下は外部関数の宣言例である。NativeRecordは§6のC指定structとする。

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    unsafe func observe(record: unsafe/NativeRecord) -> ()
```

対応するC側の署名は次の形になる。

```c
void observe_record(const NativeRecord *record);
```

呼び出し前に、recordが有効なNativeRecordを指すこと、呼び出し中に生きていること、外部関数が書き込むか・保持するかを契約として確認する。Cのconstに相当する保証を、Kimigayoのraw pointer型だけで表しているわけではない。呼び出しにはunsafeの規則を適用する。

この例は署名の対応を示す。safe referenceからraw pointerを得る新しい変換や、raw storageから所有値を作る構文は追加しない。初期版で利用できるポインターの取得経路がなければ、その経路は別途実装するまで使えない。宣言が書けることと、安全な値の準備が完了することを区別する。

```text
外部へ値を渡すまでの検査
├─ #Layout("C")
│  └─ 外側のField配置を決める
├─ C交換用storageの判定
│  └─ 内側を含む値の表現を確認する
├─ 外部との使用契約
│  └─ 読書き、初期化、寿命、所有権、保持、Loanを確認する
└─ FunctionAbi
   └─ 実際の引数・戻り値の機械上の受け渡しを決める
```

### 9.4. LibraryImportの初期範囲

LibraryImportは、非補間・非空・NULを含まない文字列リテラル2個でライブラリー名とシンボル名を指定する。対象は本体のないunsafe funcであり、group/rootgroup直下またはstructのreceiverなし関数に置く。receiver、generic/Origin引数、デフォルト引数、省略可能引数、可変長引数、specializationは許可しない。

通常の引数・戻り値として許可するのはi8/u8、i16/u16、i32/u32、i64/u64、f32/f64、raw unsafe/Tである。Unitは戻り値だけに許可する。C交換可能なstructや固定配列であっても、その値を直接引数・戻り値にはしない。

引数を左から右へ一度ずつ取得し、正常復帰後は通常cleanupへ戻る。自動marshalling、参照先の保持、確保、解放、所有権の取得を挿入しない。外部例外、longjmp、Kimigayoフレームを横断するunwind、callbackや再入は初期契約の対象外である。外部呼び出しも既存のLoanを無効化する権限を与えない。

ライブラリーの解決とリンク入力への対応は再現可能なビルド入力として記録する。初期コンパイラーはリンクを実行せず、必要な外部ライブラリーは利用者が手動で指定する。対象ABIで扱えない署名はコンパイル時に診断し、未解決シンボルはリンクまたはロードの段階で失敗させる。

<a id="section-10"></a>

## 10. 通常関数の内部ABIと所有権の受け渡し

### 10.1. 3種類のABIを区別する

ABIは関数の引数・戻り値を機械上でどう受け渡すかという約束である。レイアウト属性は格納配置を選ぶが、関数ABIを選ばない。

```text
関数の呼び出し
├─ Kimigayo内部ABI
│  ├─ 利用者の通常関数
│  ├─ Coreの実装
│  └─ 非公開のRuntime補助関数
├─ ターゲットのC ABI
│  └─ LibraryImportで宣言した関数
└─ Windows x64の外部ABI
   ├─ WindowsRuntimeSymbolsの7関数
   └─ OS側の起動関数
```

初期の内部ABIは同一生成モジュール内で統一する。公開バイナリーABIではなく、内部ABIの版を変更してよい。初期LLVM出力では既定のcalling conventionであるcccを使用し、宣言とcallを同じ物理署名から生成する。cccの採用だけでソース言語のaggregateがC互換に分類されるわけではない。

### 10.2. 物理的な引数・戻り値

| 言語上の値 | 初期の受け渡し |
| --- | --- |
| 整数・浮動小数点・bool・char・raw pointer | §5の計算型で直接渡す・返す |
| owned struct・Tuple・固定配列・string・enum | 初期化済みの専用引数領域へのptrを渡す |
| aggregateの戻り値 | callerが確保した未初期化の戻り値領域へのptrを先頭引数に渡し、LLVM関数はvoidを返す |
| Unit | 引数の物理slotは省略、戻り値はvoid |
| Never | 戻り値を作らず、noreturnと非復帰経路を使う |
| borrow・object handle・共通関数値 | その表現を実装するときに、具体的なValueLoweringを定める。未確定のままptr一語へ省略しない |

aggregate引数はsourceの引数順で配置する。必要なreceiverも解析済みの署名に従って引数へ含める。診断位置などの非公開コンテキストを追加する補助関数では、通常の物理引数の後ろへ置く。

初期の内部ABIは、戻り値領域も引数領域も通常のptrとして明示する。自動的なbyvalやsretの挿入を前提にしない。将来それらを使用する場合はFunctionAbiを更新し、呼び出す側と呼び出される側を同時に変更する。

### 10.3. scalarとaggregateの例

以下は署名と処理を説明するソース例である。

```kimi
group Samples
    func isPositive(value: i32) -> bool
        return value > 0

    func echo(text: string) -> string
        return text
```

scalar関数のLLVM例は次の形になる。内部関数名は説明用に短くしている。

```llvm
define internal i1 @kimi_is_positive(i32 %value) {
entry:
  %result = icmp sgt i32 %value, 0
  ret i1 %result
}
```

stringのechoでは、戻り値領域と引数領域を別々に持つ。下記は型と物理署名の例であり、本体は省略している。

```llvm
%StringHandle = type { ptr, i64, i8 }
declare void @kimi_echo(ptr %result_storage, ptr %text_storage)
```

```text
caller
├─ 引数用領域を用意する
├─ stringをその領域へMoveする
├─ 戻り値用の未初期化領域を用意する
└─ kimi_echo(result_storage, text_storage)
   └─ callee
      ├─ textの所有権と破棄責任を受け取る
      ├─ textをresult_storageへMoveする
      ├─ 移動済みtextを破棄対象から外す
      ├─ 残るローカルのcleanupを実行する
      └─ 正常return
         └─ callerが戻り値の所有権と破棄責任を受け取る
```

メモリを確保・再利用する責任と、その領域内の値を破棄する責任は別である。calleeはcallerのstack領域をFreeしない。

### 10.4. 引数取得中の責任

引数を左から右へ一度ずつ評価・取得する。Copyなら元の値の責任を残し、Moveなら元から引数用の値へ責任を移す。専用slotの用意は物理実装であり、言語上の余分なCopyではない。

```text
複数の所有引数を渡す場合
├─ 第1引数を取得 → callerが一時値の責任を持つ
├─ 第2引数を取得 → callerが一時値の責任を持つ
├─ 全引数の取得が完了
└─ callへ到達
   └─ 所有引数の責任をcalleeへ移す
```

後続引数の評価で通常の制御移動が起きる場合、既に取得した一時値には解析済みのcleanup計画を適用する。Abortなら通常cleanupを行わない。非終了なら後続処理へ進まない。呼び出されていないcalleeへ破棄を任せない。

Copyするaggregateの元領域を、calleeが変更・消費できる引数用領域としてそのまま共有してはならない。slotの統合や転送省略は、alias・Loan・寿命・副作用が変わらないと証明できる場合の最適化とする。

### 10.5. 戻り値とcleanupの順序

```text
return expression
├─ 戻り値を一度評価・取得する
├─ caller用の戻り値領域へ値を確保する
├─ 離れるスコープのcleanupを実行する
│  ├─ 完了 → 正常return
│  ├─ Abort → 通常cleanupを打ち切って終了
│  └─ 非終了 → returnしない
└─ 正常returnした場合だけcallerへ結果を引き渡す
```

「戻り値を確保する」はcallee側のcleanupより前にその値を失わない状態にすることである。callee内に戻り値の責任を保持し、解析済みの例外的な制御移動がある場合はその計画に従う。正常return前にcallerが結果を読める契約にはしない。

### 10.6. ゼロサイズと非復帰

初期の内部ABIでは、Unit以外のゼロサイズaggregateにも論理上の引数領域・戻り値領域を対応させる。物理的には必要alignmentを満たす小さい領域を使ってよく、別々のアドレスは保証しない。元の型のsizeやstrideは変更しない。

ゼロサイズ型にdeinitなどの観測可能な処理があれば実行する。NeverはUnitと異なり「正常な結果を返さない」ことを表す。LLVMにNeverという格納型を新設せず、Abort・Exit後はunreachableでブロックを終える。

<a id="section-11"></a>

## 11. LLVM IR生成規則

### 11.1. モジュール構成

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

### 11.2. 起動関数のLLVM断片

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

浮動小数点を使うプログラムでは、§11.6.2の環境設定もentry bodyより前に生成する。上の断片はその初期化を省略した構造例である。

NeverをLLVMの戻り値型として作らない。戻らない処理は`void`の関数・`noreturn`属性・`unreachable`などで表現する。未解決の解析結果をunreachableへ置換しない。

### 11.3. 最適化と追加依存

初期の`.ll`出力で独自の高度な最適化は必須にしない。後続の最適化有無によって、言語上の検査・Abort条件・評価順序・cleanupの意味を変えない。

LLVMは、メモリ操作や大きなスタックフレームなどから、memcpy・memmove・memset・stack probe等の補助シンボルを要求する場合がある。これらはWindowsRuntimeSymbolsの7関数とは別の、コード生成に伴う依存である。

今回はこれらを新しいRuntime操作として追加しない。ただし、`/nodefaultlib`での手動リンク検証では未定義シンボルを確認する。依存が生じた機能は、適切な補助実装を供給するか初期対応から外して診断する。空の関数による代用や、必要なstack probeの無効化を解決策にしない。

「すべてのKimigayoプログラムがKernel32.libだけでリンクできる」とは保証しない。初期サブセットでは、§16の手動リンクが成立することを検証する。

### 11.4. レイアウトに基づくメモリ操作

コンパイラー内部で、少なくとも次の情報を分けて保持する。独立したLowered IRの新設は必須にしない。

| 情報 | 主な内容 |
| --- | --- |
| TypeLayout | 方式、size、alignment、stride、Field/base Identityからoffsetへの写像、LLVM storage型 |
| ValueLowering | SSA上の値の型、storageとの変換、有効値制約 |
| FunctionAbi | 物理引数・戻り値、間接slot、属性、calling convention |
| CleanupPlan | 論理順、初期化済みcomponent、移動済みcomponent、破棄責任 |

レイアウト計算結果をGEP、alloca、load/store、配列stride、metadataで共有する。LLVMのフィールドindexをソースのField順と同一視しない。再帰型は値を直接埋め込む格納依存の循環を拒否し、ポインターを介する再帰は許可する。

通常のLLVM structでは指定した要素順にDataLayoutのpaddingが入る。Kimigayoの並べ替えはフロントエンドが選択し、LLVM型へ反映する。Cレイアウトにpacked struct `<{ ... }>`を使わない。LLVM storageのallocation size・ABI alignment・offsetがTypeLayoutと一致することを検証する。[LLVM structure types](https://llvm.org/docs/LangRef.html#structure-types)

```llvm
; NativeRecordのstorage例。メンバー関数のABIはこの型から決まらない。
%NativeRecord = type { i8, i64, i8 }
```

value Fieldの読み取りは、たとえば次の形になる。%selfはalignment 8を満たす、有効で初期化済みのNativeRecordを指す前提とする。

```llvm
; 要素indexは1。バイトoffsetの8はDataLayoutから決まる。
%value_address = getelementptr %NativeRecord, ptr %self, i32 0, i32 1
%value = load i64, ptr %value_address, align 8
```

Fieldの論理的な位置、LLVMの要素index、バイトoffsetを同じ数として扱わない。上の例ではinboundsを付けずに表している。

Paddingは値ではない。ゼロ初期化、比較可能性、Copy後のpadding内容の保存を保証しない。struct比較をmemcmpへ置換したり、未初期化paddingを値として利用したりしない。Copy/Moveの物理実装にmemcpyを使える場合でも、言語上の取得・破棄責任はCleanupPlanに従う。

`noalias`、`nonnull`、`noundef`、`dereferenceable`、`inbounds`等は個別に前提を満たす場合だけ付ける。特に`uniq`という名称や借用の存在だけでLLVMの全関数にわたるalias保証を推論しない。`sret`や`byval`等のABI属性と最適化用属性も分けて管理する。[LLVM parameter attributes](https://llvm.org/docs/LangRef.html#parameter-attributes)

### 11.5. 検査付き算術と変換

整数overflow、ゼロ除算、不正shift、数値変換の失敗、評価順序は既存SPECに従う。ここでは、その意味をLLVMへ落とす方法を定める。

| 操作 | エミット規則 |
| --- | --- |
| 整数の加算・減算・乗算 | 符号に対応するoverflow intrinsic等で結果とoverflowを得て、失敗ならAbort |
| 単項マイナス・増減・複合代入 | 演算を検査し、成功した場合だけ結果を書き込む |
| 整数の除算・剰余 | 除数0を先に検査。符号付きでは最小値と-1の組も先に拒否 |
| shift | 元の右辺型で0以上かつ左辺bit幅未満を検査してから、命令の型へ変換 |
| 符号付き右shift | ashrを使用 |
| 符号なし右shift | lshrを使用 |
| 左shift | shlを使用。捨てられる上位bitを算術overflowとはしない |
| 整数から整数への変換 | 変換先の範囲を検査してから拡張・縮小 |
| floatから整数への変換 | NaN・無限大を拒否し、ゼロ方向へ丸めた数学上の整数が範囲内か確認してから変換 |
| 整数からfloat・float幅変更 | 指定の丸めを使い、有限値から無限大になる失敗を検査 |
| 配列index・Range等 | 既存の境界規則を検査し、成功した経路でアドレスを計算 |

`nsw`や`nuw`を付けただけでは、言語が要求するoverflow検査にならない。LLVMのpoisonや未定義動作を、Abortの代用にしない。[LLVM overflow intrinsics](https://llvm.org/docs/LangRef.html#arithmetic-with-overflow-intrinsics)

以下は符号付き32ビット加算の例である。abort helperは理由を固定した非公開関数で、%siteは元のソース位置を指す。

```llvm
declare { i32, i1 } @llvm.sadd.with.overflow.i32(i32, i32)
declare void @kimi_abort_integer_overflow(ptr) noreturn

define internal i32 @kimi_checked_add(i32 %a, i32 %b, ptr %site) {
entry:
  %pair = call { i32, i1 } @llvm.sadd.with.overflow.i32(i32 %a, i32 %b)
  %value = extractvalue { i32, i1 } %pair, 0
  %overflow = extractvalue { i32, i1 } %pair, 1
  br i1 %overflow, label %failed, label %ok

failed:
  call void @kimi_abort_integer_overflow(ptr %site)
  unreachable

ok:
  ret i32 %value
}
```

定数評価では同じ失敗をコンパイル時診断にする。単に「Releaseなら検査を省く」という規則は設けない。検査成功が証明できる場合に限り、意味を保って削除できる。

### 11.6. 浮動小数点と実行環境

#### 11.6.1. 言語の計算規則

f32/f64はIEEE 754、丸めは最も近い値・中間は偶数側とする。NaN、無限大、符号付きゼロ、非正規化数を扱い、通常の式を勝手に再結合・融合しない。初期版ではfast-mathフラグを付けない。

floatの比較では、通常の`==`とEquatableのNaNの扱いが異なるという既存規則も保持する。共通genericコードを特殊化するときに、Equatableの呼び出しを無条件にfcmp oeqへ置き換えない。

#### 11.6.2. Windows x64での環境管理

浮動小数点演算を実装する段階では、SSE2以降の演算を使い、次のMXCSR制御状態を初期実行条件にする。

| 制御項目 | 初期状態 |
| --- | --- |
| 丸め方向 | 最も近い値、中間は偶数側 |
| DAZ: 入力の非正規化数をゼロとみなす | 無効 |
| FTZ: 小さい結果をゼロへ丸める | 無効 |
| 浮動小数点のハードウェア例外 | マスクする。言語のAbort検査は別に生成 |

これらはWindows x64 ABIで規定される標準の制御状態とも一致する。[Microsoft MXCSR](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention?view=msvc-170#mxcsr)

Applicationでは、起動本体を実行する前に必要な状態を設定する。浮動小数点機能を使わない最小プログラムでは、この補助処理の生成は省略できる。

```text
浮動小数点を使うApplication
└─ mainCRTStartup
   ├─ MXCSRの制御状態を設定
   └─ 起動本体
      ├─ Kimigayoの計算
      └─ 外部関数呼び出し
         ├─ 呼び出し直前のMXCSRを保存
         ├─ 外部関数を呼ぶ
         ├─ 正常復帰後、保存したMXCSRを復元
         └─ Kimigayoの計算を続ける
```

初期版は外部呼び出しadapterで保存・復元する方式とし、全経路で制御状態の維持を証明できる呼び出しに限って省略できる。これによって浮動小数点環境の異なる外部実装から戻っても、その後のKimigayoの丸め規則を維持する。noreturnの呼び出しでは復元経路を作らない。

制御レジスター操作はコンパイラー内部のターゲット処理として生成する。Runtimeの公開論理操作やWindows APIを追加するものではない。実装にはLLVMの対応intrinsicまたは副作用を正しく指定したターゲット命令を使用し、環境変更を越えて計算を移動させない。必要な箇所ではconstrained floating-point intrinsicとstrictfpの規則を使用する。[LLVM constrained floating-point](https://llvm.org/docs/LangRef.html#constrained-floating-point-intrinsics)

浮動小数点例外のstatus flagを利用者向けAPIとして公開しない。外部からLibraryのコードを直接呼び出すexportやcallbackを追加するときは、入口と出口でも環境を保存・設定・復元するadapterを設計する。初期版はその外部入口を提供しない。

### 11.7. stack、一時領域、最適化用属性

#### 11.7.1. storageの有効期間

初期実装では、具体的なsizeが決まったローカル値と一時値を固定sizeのallocaへ配置する。必要なalignmentを明示する。動的stack allocationは初期対応に含めない。

```text
一時領域の再利用
├─ 値が初期化される
├─ 値を利用する
├─ 必要なcleanupと参照の最終利用が完了する
└─ 以後、その領域を再利用できる
```

llvm.lifetime.start/endは任意の最適化情報である。実際のstorageが生きている範囲に合わせ、Originの記法やMove直後という理由だけで終了させない。初期版は正しさを確定できないmarkerを省略してよい。

#### 11.7.2. 属性を付ける条件

| 属性・フラグ | 確認すること |
| --- | --- |
| align | その実際のポインターまたは格納先が必要alignmentを満たす |
| nonnull | nullにならないことがその表現と呼び出し契約から保証される |
| dereferenceable | 指定バイト数のアクセスが有効。ゼロサイズの代替領域から元のsize以上の保証を作らない |
| noalias | LLVMが要求するalias契約を満たす。uniqという名前だけで付けない |
| noundef | 渡す値に未定義bitやpoisonがない。paddingのあるaggregateを整数へまとめる場合も確認 |
| inbounds | オブジェクト内の位置・許可された終端位置・計算上限など、GEPの前提がすべて成立する |
| nsw / nuw | 指定した符号条件でoverflowしないと証明できる |

初期版では、成立を証明できない最適化用属性を付けない。この方針は言語側のLoan検査や有効値検査を省略してよいという意味ではない。

### 11.8. 名前、順序、生成キャッシュ

利用者の名前と内部補助関数名は別の名前変換を用い、重複しないシンボルを生成する。型名や関数名だけで区別せず、Kotonoha・宣言のIdentity・generic引数・実装選択を必要に応じて含める。

```text
生成結果の依存
├─ コンパイラー / レイアウト方式 / 内部ABIの版
├─ target triple / DataLayout / コード生成設定
├─ 選択済みの宣言・fragment・生成ソース
├─ 完全な型・具体的なgeneric引数・選択済み実装
└─ cleanup・呼び出し先・必要なRuntime helper
```

layout cacheはsize・alignmentだけをキーにしない。関数の生成キャッシュも同じ表現という理由だけで異なる所有権操作や実装を共有しない。定数・型・関数・外部宣言の出力順を決定的にし、作業ディレクトリーの絶対パスやファイル列挙順に依存させない。

<a id="section-12"></a>

## 12. Runtimeの6操作

### 12.1. 論理インターフェース

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

### 12.2. ソース位置の受け渡し

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

### 12.3. AllocとFree

初期実装では、Windowsのプロセスヒープを使う。

- Allocは未初期化の生メモリを返す。言語上の変数をInitializedにする操作ではない。
- sizeが0の場合は、内部では1バイトの確保として処理する。言語上のUnit等のsizeを1へ変更しない。
- Windows x64の初期対応alignmentは16バイトまでとする。これを超えるalignmentが必要なヒープ配置は、対応する確保方式を追加するまで未対応診断とする。
- Free(null)は何もせず成功する。
- Freeには、同じRuntime allocatorから得た未解放の元ポインターだけを渡す。内部ポインターやリテラル領域のポインターを渡さない。
- デストラクターを実行してから必要な生メモリを解放する順序は、Loweringが生成したcleanupが担当する。Free自身はデストラクターを呼ばない。

所有権違反や二重解放を、Freeが必ず検出するとは保証しない。正しいポインターと解放責任は静的解析・生成コードの責務である。

概念上の処理は次のとおり。`site`は§12.2の非公開診断コンテキストを表す。

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

### 12.4. WriteStdoutとTryWriteStderr

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

### 12.5. Abortと診断

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

### 12.6. Exit

ExitはExitProcessへ接続する。戻り値はなく、呼び出し側のLLVM基本ブロックは`unreachable`で終える。

ExitはKimigayoのcleanupを実行しない。正常終了経路では呼び出す前に必要なcleanupを済ませ、Abort経路では通常のcleanupを通さず呼び出す。Windows自身が行うプロセス終了処理とは区別する。

### 12.7. レイアウト・内部ABIとの接続

レイアウトと内部ABIの規則を適用しても、Runtimeは本章の6操作、Windows接続は§13の7関数で構成する。

- stringの`{ ptr, i64, i8 }`は内部表現として維持する。構造が似ていてもC交換可能なstringにはならない。
- Runtime.Allocのalignment上限16は、型そのもののlayout制限と分ける。初期版で16超のalignmentを必要とするヒープ配置は§12.3に従って診断し、黙って低いalignmentへ丸めない。
- size 0を1バイト確保へ置き換えても言語上のsize・strideは変わらない。確保を省略する場合はFreeしないことを別途管理する。
- privateなRuntime補助関数は内部ABI、Windows API宣言はターゲット外部ABIを使用する。同じFunctionAbiの仕組みで管理してもABIの種類を混同しない。
- 大きいaggregateやstack frameに伴うmemcpy等・stack probeの依存は§11.3に従って供給または未対応診断する。

<a id="section-13"></a>

## 13. WindowsRuntimeSymbols

### 13.1. 初期の7関数

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

### 13.2. LLVM宣言例

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

### 13.3. GetLastErrorの扱い

last-errorを提供するAPIが失敗した直後に取得する。成功時の古い値をエラーとして扱わない。

初期診断では数値コードを表示する。FormatMessageWによるOSの説明文取得は追加しない。GetLastErrorもソース言語へ直接公開せず、Windows adapter内部で使う。[Microsoft エラーコードの取得](https://learn.microsoft.com/en-us/windows/win32/debug/retrieving-the-last-error-code)

<a id="section-14"></a>

## 14. stringとCore.writeLine

### 14.1. stringの初期内部表現

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

通常関数間のhandleは、§10のaggregate用内部ABIに従い、専用slotへのポインターで受け渡す。物理的なビットの転送だけを、言語上のCopyとして数えない。Move後は元の束縛から所有権・破棄責任が移る。

リテラルのbackingは定数領域へ置けるため、Hello worldにヒープ確保は必須ではない。定数領域が共有可能でも、string自体のNon-Copy規則は変わらない。動的文字列の構築は、対応する式を実装するときに追加する。

### 14.2. writeLineの契約

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

### 14.3. 初期版のコンソール対応範囲

初期版は採用済みの7つのWindows APIでバイト出力を実装し、コンソールのコードページを変更しない。ファイル・パイプへのリダイレクトではUTF-8のバイト列をそのまま出力する。

Windowsコンソール上の非ASCII文字の見え方は、そのコンソール設定に依存する。全設定での日本語表示はこの初期版の保証に含めない。必要になった段階でGetConsoleMode・WriteConsoleW等を使うUnicode表示adapterを別途設計する。現在の契約を暗黙にUTF-16出力へ変更しない。

<a id="section-15"></a>

## 15. 型・機能の対応を広げるときの規則

この章は、Hello worldの完了条件を増やすものではない。Tupleとenumの初期配置方針は本書で採用し、その他の未確定な物理表現は対応機能を実装する前に定める。

```text
実装を広げる順序
├─ 最小の実行確認
│  └─ 通常関数 / Unit / string / writeLine / cleanup
├─ 値の配置と計算
│  ├─ 基本型 / struct / #Layout
│  └─ 固定配列 / Tuple / enum
├─ 実行時の間接表現
│  ├─ 継承 / object / rc / arc
│  └─ 動的配列 / Slice / closure
└─ 外部・共有機能
   ├─ generic共有 / 複数モジュール
   └─ C aggregate値渡し / export / callback
```

### 15.1. Tuple

初期版は要素の論理index順に自然alignmentで配置し、末尾を最大alignmentへ切り上げる。Unit以外でも全要素がゼロサイズならsize・strideは0とする。Fieldと同様に、論理indexとLLVMの要素index・offsetを別に保持する。

```kimi
let pair: (u8, u64) = (1, 2)
// 初期Windows x64では、要素0のoffsetは0、要素1は8。
// size 16、alignment 8。これはC ABIのTuple型を導入しない。
```

将来の内部配置変更を許すが、要素の評価・破棄順は変えない。Tupleへ#Layoutを適用する構文は追加しない。

### 15.2. enum

初期方式は、Caseを示す明示tagと、最大のCase payloadを収める領域の組とする。payloadの空きbit等へCaseを埋め込むniche最適化は初期版では行わない。

```kimi
enum Message
    Quit
    Number(i64)
```

```text
enumの内部storage
├─ tag: 現在のCaseを識別する整数
├─ padding: payloadのalignmentを満たす余白
└─ payload領域
   ├─ Quitの場合: 使用するpayloadなし
   └─ Numberの場合: i64の値
```

初期の物理tagはu32相当のi32とし、選択済みCaseの宣言順に0から番号を付ける。2^32を超えるCaseは未対応診断とする。これは内部番号であり、ソース言語に整数discriminantや整数変換を追加しない。

各Caseのpayloadは論理要素順に配置する。payload領域のalignmentは全Case payloadの最大値、sizeは最大のpayload sizeをそのalignmentへ切り上げた値とする。payloadの開始offsetはtagの4バイトをpayload alignmentへ切り上げ、enum全体のalignmentは4とpayload alignmentの最大値、sizeは全体alignmentへ切り上げる。payloadがすべて空ならpayload size 0・alignment 1とする。

有効なenum値は、選択済みCaseのtagと、そのCaseに対応する有効なpayloadの組だけである。初期化・Move・部分cleanup・matchはその組に基づく。使用しないpayload領域を読み出さない。有効なenum値だと確定していない外部整数をtagとして扱い、到達不能へ置き換えることもしない。

この方式は内部表現であり、C enumやC unionとの互換性を保証しない。Caseの切り替えは既存の全体代入・置換規則に従い、tagだけを書き換える利用者向け操作は追加しない。

### 15.3. 継承とobjectの表現

Kimigayoレイアウトでは、初期の直接base部分をoffset 0へ配置する。これは初期実装の選択であり、言語上の永続保証ではない。baseから派生先までの所有権を切り離したり、baseだけをMoveしたりできる根拠にもならない。

object対応時には次の物理契約を定める。

```text
objectの表現
├─ handle / view
│  ├─ 何を指すか、何語で表すか
│  └─ receiverやbaseへの調整方法
├─ allocation
│  ├─ ヘッダー・参照カウント
│  └─ owned payload: structの配置規則に従う
└─ 共有metadata
   ├─ 型のIdentity / descriptor
   ├─ dispatch用情報
   └─ Copy・Move・破棄の補助処理
```

C指定が固定するのはpayload内の配置だけである。ヘッダーや参照カウントをC payloadへ混ぜない。rc/arcのcounter幅、overflow検査、最後の所有者の破棄手順、arcのatomic orderingはobject対応前に確定する。arcのatomic操作があるだけで、言語のスレッド機能を許可しない。

### 15.4. 動的配列・Slice・closure

| 機能 | 実装前に定める物理契約 |
| --- | --- |
| 動的配列・Dictionary | data、length、capacity等の情報、再確保、要素の初期化と破棄 |
| Slice | 参照先と長さの表現、Origin/Loanの維持、再Slice時のアドレス計算 |
| 具体的closure | captureの格納、呼び出しentry、破棄entry、captureの初期化・破棄順 |
| 共通Function Type | 型消去した値と環境、inline格納/確保の選択、呼び出し・破棄の方法 |

captureの論理順序と物理offsetは分ける。参照や所有値を含むからといって、これらを一律にptr一語へ変換しない。必要な確保失敗は既存規則に従ってAbortする。

### 15.5. generic・外部ABI・レイアウト照会

| 項目 | 方針 |
| --- | --- |
| generic | 初期に対応する具体化範囲を明示し、完全な型と配置を確定する。未対応の共有やmetadata伝達は未対応診断にする |
| generic共有 | 同じsizeだけで共有せず、実装選択・演算・所有権・cleanupを保つ生成キーを用いる |
| Cのaggregate値渡し | 引数と戻り値のABI分類、整数へのcoercion、間接slotとalignment、必要な属性をC側との試験で確定する |
| レイアウト照会 | size・alignment・offsetの公開構文は今回追加しない。追加時は評価時期、generic条件、Mods完了前の照会可否を定義する |
| 特殊配置 | packed、align、transparent、union、bit-field、明示offset、外部enum表現は将来拡張とする |

未解決のobligationを「LLVMへ渡せば何とかなる」として通過させない。汎用generic共有が未実装でも、別の意味を持つ特殊化へ黙って置き換えない。

<a id="section-16"></a>

## 16. 出力設定と手動ビルド

### 16.1. 初期の設定方針

| 設定 | 初期方針 |
| --- | --- |
| Targets | 初期対応は`x86_64-pc-windows-msvc` |
| OutputKind | ApplicationまたはLibrary。既定値はApplication |
| OutputPath | `.ll`の出力先。既定値は`bin/<target>/<ProjectName>.ll` |
| EntrySource | 初期版の選択手段にしない。§2の規則で一意に決定 |
| NativeLibraries | 今回はコンパイラーによるリンクに使用しない |

OutputKind・OutputPathは追加予定の設定として記述する。現行ProjectFileで利用できると主張しない。既存STATUSにある`.exe`出力・EntrySource選択案は、本書の初期実装範囲へ合わせて改訂する。

生成失敗時に、部分的な`.ll`を今回の成功出力として公開しない。以前の成功出力が残っている場合も、それを今回の成功として報告しない。

### 16.2. 手動コマンド

以下はコンパイラーが`rdtsc.ll`を生成した後に、利用者が実行する例である。LLVM 22.1.5のツールと、x64用Kernel32.libを解決できる環境を前提とする。

```powershell
llc -filetype=obj rdtsc.ll -o rdtsc.obj
lld-link rdtsc.obj kernel32.lib /entry:mainCRTStartup /subsystem:console /nodefaultlib /debug /out:app.exe
.\app.exe
```

`.ll`から`.obj`への変換はllcの仕事であり、その後にlld-linkへ渡す。[LLVM llc](https://llvm.org/docs/CommandGuide/llc.html)

Kernel32.libを検索できない環境では、利用者がライブラリーのパスを明示する。現段階のコンパイラーはLLVM・Windows SDKの探索、インストール、llc・lld-link・生成物の自動実行を行わない。

`/debug`だけでKimigayoソースの行情報や変数情報が生成されるわけではない。CodeView/PDB向けデバッグ情報の生成は別の拡張とする。Abort用のソース位置は§12.2により独立して保持する。

### 16.3. 成功の意味

| 段階 | 確認できたこと |
| --- | --- |
| Kimigayoの`.ll`生成成功 | 対応範囲の意味検査とIR生成が完了 |
| LLVMでの検証・オブジェクト生成成功 | LLVM 22.1.5が生成IRを受理し、オブジェクトへ変換できた |
| 手動リンク成功 | 必要な外部シンボルを解決し、実行ファイルを生成できた |
| 実行確認成功 | 実行ファイル自身の出力と終了状態が期待どおりだった |

`.ll`生成成功だけを、リンク済み・実行済みとして報告しない。

<a id="section-17"></a>

## 17. 検証項目

### 17.1. 起動候補

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

### 17.2. 所有権と終了

- Move済みstringを再利用するとエラーになる。
- リテラルbackingにHeapFreeを発行しない。
- Heap由来の所有値は、正常経路で責任に従って一度だけ解放する。
- 正常return・スコープ終了では必要なdeferと破棄が実行される。
- Abortでは通常のcleanupを開始・継続しない。
- cleanup中の非終了によって、後続のcleanupと結果引き渡しが阻止される。
- 未解決の意味情報を持つ関数を生成しない。

### 17.3. 出力と失敗経路

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

### 17.4. LLVMと手動リンク

LLVM 22.1.5の検証器で、関数型、分岐先、基本ブロック終端、SSAの整合を確認する。手動でオブジェクト生成・リンク・実行まで確認し、未定義補助シンボルが残らないことを初期サブセットの検証に含める。

Debug/Release双方について、言語上の検査、出力、終了状態が一致することを確認する。最適化の有無で所有権上の可否を変えない。

### 17.5. 属性・レイアウト・C互換性

| ケース | 期待結果 |
| --- | --- |
| 属性なし / Kimigayo明示 | 同じ方式を選択 |
| 引数不足・過剰・未知のモード・不正な対象 | 属性の診断 |
| 同じfragmentに属性を重複 | 同じモードでもエラー |
| 別fragmentの同じ指定 / 指定省略 | 統合して共有 |
| 別fragmentの異なる指定 | エラー |
| 混在alignment・nested型・末尾padding・固定配列 | 確定したTypeLayoutとLLVMのoffset・size・alignmentが一致 |
| C指定内のKimigayo型・string | 配置が成立してもC交換用とは判定しない |
| C指定の空struct・直接ゼロサイズField・open・base | 初期制限としてエラー |
| generic置換で変化するalignment・size | 各具体化で計算・検査 |
| inlineの直接・間接再帰 | 無限layoutを診断 |
| ポインターを介する再帰 | 参照先をinline循環に数えない |
| size・stride・offsetのoverflow / 上限超過 | コンパイル時診断 |
| paddingのある値の比較・Copy | paddingの値を意味のあるデータとして利用しない |
| 並べ替え・最適化の有無 | 同じ言語上の初期化・評価・破棄順を維持 |

C交換用の例は、同一ターゲット・packing条件のCプログラムのsizeof・alignof・offsetofと比較する。Cポインター越しの読書きと、将来のstruct値渡しは別の試験にする。

### 17.6. 内部ABI・演算・実行環境

- scalarの直接受け渡しと、aggregateの引数slot・戻り値slotが宣言とcallで一致する。
- Copy元をcalleeが消費・変更してしまう共有を生成しない。
- 引数取得途中の制御移動、Abort、非終了について、取得済み一時値の責任が正しい。
- 戻り値の確保、calleeのcleanup、callerへの引き渡しが正しい順になる。
- Unit以外のゼロサイズ値でも、必要なdeinitとcleanupを残す。
- boolの格納が0/1のi8であり、Windows BOOLのi32と混ざらない。
- 整数の境界値、最小値/-1の除算と剰余、不正shift、変換範囲外を正しく診断・Abortする。
- 負の小数から符号なし整数への変換など、ゼロ方向へ丸めた後の範囲判定を確認する。
- NaN、無限大、符号付きゼロ、中間値の丸め、非正規化数を扱い、通常比較とEquatableを区別する。
- 外部関数が浮動小数点環境を変えた後の正常復帰で、Kimigayoの計算規則が維持される。
- 不要なnoalias・inbounds等によって最適化後だけ結果が変わるコードを生成しない。
- 同一入力から同じlayout・命名・出力順を再現する。

LLVM verifierの受理だけでは、C互換性、正しいoffset、所有権の責任移転まで証明できない。実装段階に応じて、構造検査と実行結果の確認を組み合わせる。

<a id="section-18"></a>

## 18. 既存文書への反映箇所

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
| SPEC §6.1.2・§6.2.1・§6.5 | Layout属性、fragment間の共有・競合、確定時期を追加 |
| SPEC §21.1 | Kimigayo/C方式、基本型表現、ゼロサイズ、上限、Tuple・enumの初期配置を整合 |
| SPEC §22.3 | C配置とC交換用判定を追加し、aggregate値渡しの保留と区別 |
| SPEC AppendixのFFI・Attribute保留項目 | 今回定義した範囲を反映し、特殊配置・外部ABI拡張の保留を維持 |
| STATUSのLowering・layout項目 | 属性検査、配置計算、内部ABI、検査付き演算、LLVM生成、相互運用の進捗を分けて記録 |
| STATUS C.13 | LLVM 22.1.5、独自entry、6操作・7APIの採用を記録 |

実装状況はSTATUSで管理する。本書に例があることや、設計が確定したことだけを根拠に、Parsing・Binding・Analysis・Lowering・Runtimeの実装済み範囲を拡大しない。

<a id="section-19"></a>

## 19. 将来の拡張

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

### 19.1. 将来拡張を追加する条件

将来の機能でも、評価順・所有権・cleanupの意味を維持する。追加の物理表現が決まっていない型は、対応済みの型へ黙って置き換えない。

| 分野 | 残る拡張 |
| --- | --- |
| 型の特殊配置 | packed、align、transparent、union、bit-field、明示offset、flexible array member |
| レイアウト公開API | size・alignment・offset照会の構文、評価時期、generic条件、Modsとの関係 |
| 外部ABI | C aggregate値渡し、export、callback、可変長引数、platform固有calling convention、外部enum表現 |
| object・共有コード | handle/header/descriptorの物理契約、参照カウント、汎用generic共有とmetadataの受け渡し |
| メモリモデル | スレッド生成、thread transfer、外部からの並行再入、同期規則 |

### 19.2. この統合文書の状態

本書は、起動処理・Windows Runtime案とstructレイアウト・LLVM Lowering案を統合した採用仕様である。例示コードは、言語例、生成されるLLVMの例、説明用の擬似コード、検証用Cコードをそれぞれ区別して掲載している。

文書の統合時点ではコンパイラーの実装、LLVMでの例示コードの実行試験、SPEC・STATUSへの反映は行っていない。実装の完了は§16.3と§17の段階別の検証で判断する。
