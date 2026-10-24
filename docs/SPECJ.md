# Kimigayo言語仕様 — 簡約日本語版

本書は[英語版SPEC](SPEC.md)とその言語仕様22章、付録D・E・F、check出力スキーマを、1ファイルにまとめた新訳である。既存のSPECJ.mdは使用していない。説明の重複をまとめ、規則・条件・例外、コード例、規範文法とスキーマを保持した。実装契約（第20・21章・付録A）は[IMPL](IMPL.md)、実装状況は[STATUS](STATUS.md)を参照する。

翻訳元：`8ee50bf7e36708f32cf308145c5cb0debca005bd`（2026年10月23日確認）。表記は[SPECJ_TERMINOLOGY](SPECJ_TERMINOLOGY.md)に従う。本書は非規範的な翻訳であり、解釈が異なる場合は英語の定義を優先する。コード、識別子、文字列、文法、JSONは原文の綴りを保つ。

## 目次

- [1. 概要](#s-1)
- [2. ソースと字句構造](#s-2)
- [3. 型と値](#s-3)
- [4. 配列・添字・スライス](#s-4)
- [5. 生ポインターとunsafeメモリー操作](#s-5)
- [6. 宣言とコンテナー](#s-6)
- [7. 関数と呼出し可能な値](#s-7)
- [8. ジェネリクス・制約・Contract](#s-8)
- [9. 名前・シグネチャ・アクセス](#s-9)
- [10. オーバーロード解決と推論](#s-10)
- [11. プロパティ](#s-11)
- [12. 式](#s-12)
- [13. 演算子と代入](#s-13)
- [14. 制御フロー](#s-14)
- [15. 所有権と寿命の解析](#s-15)
- [16. スコープ退出と破棄](#s-16)
- [17. 失敗処理](#s-17)
- [18. モジュールと依存関係](#s-18)
- [19. コンパイル時ディレクティブ](#s-19)
- [22. Kimi、プログラム実行、外部関数](#s-22)
- [23. コンパイラーサービス](#s-23)
- [24. 一時停止と非同期タスク](#s-24)
- [付録D. 本版に含めない機能](#s-D)
- [付録E. 用語集](#s-E)
- [付録F. 文法](#s-F)
- [check出力JSONスキーマ](#check-output-schema)

<a id="s-1"></a>

# 1. 概要

<a id="s-1-1"></a>

## 1.1. 目的

Kimigayoは、一貫性・速度・単純さ・楽しさ・安全性を重視して一から設計したプログラミング言語である。改訂では、過去との互換性より言語の一貫性と品質を優先する。

本仕様は言語を定める。初期実装のビルド、ランタイム、コード生成の契約は[IMPL](IMPL.md)、実装済みの範囲は[STATUS](STATUS.md)に記す。

> 設計上の注記：`$`はマクロではなくComposition Rootである。

```kimi
alias Kimi.Console

#if windows
alias Kimi.Windows

public group Program
    public func runExample(arg: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        writeLine(arg)
        return

    func getString<s/T>(value: s/T) -> string
        s is ref or uniq
        T is Comparable

        #switch
            #case windows
                return "windows"
            #case linux
                return "linux"
            #case _
                return "other"
```

<a id="s-1-2"></a>

## 1.2. 表記

慣例として、利用者定義の型・Contract・宣言コンテナーにはPascalCase、関数・フィールド・プロパティ・ローカル束縛・パラメーターにはcamelCaseを用いる。組込み型のキーワードはそのまま書く。

| 表記 | 用途 |
| --- | --- |
| `[]` | 配列・Dictionaryの構築、固定長配列型、添字、スライス、キャプチャーリスト |
| `()` | 引数・パラメーター、タプル、Unit、関数型、条件、式のグループ化 |
| `<>` | 型引数、関数の長さ引数 |
| `{...}` | struct・enumのOriginスキーマ、名前付き型の束縛集合名。本体やコレクションには使わない |
| `=` | 初期化、既定引数、代入。取得はCopy・Move規則に従う |
| `@` | 明示的Adaptation、`@move`、`@copy`、`@follow` |
| `->` | 関数・関数型の結果型 |
| `=>` | 単一項目の実行本体、パラメーター名の対応付け、コンテナー別名の対象 |
| `:` | 名前と型、引数名と値、Dictionaryのキーと値、ラベルと構文を区切る。基底型、Contractの親、`: base(...)`も導入する。実行本体は導入しない |
| `#` | コンパイル時構文。小文字の指令、PascalCaseの属性 |
| `$` | 言語が提供するComposition Root操作 |
| `;` | タスク区切りの`task;`だけに使う |

型はSemantics、CoreまたはオブジェクトのView Target、必要なOriginからなる。

```kimi
group Geometry
    func area(width: i32, height: i32) -> i32
        let sides = [width, height]
        return sides[0] * sides[1]
```

<a id="s-1-3"></a>

## 1.3. 規範性と用語

言語バージョンは`0.0.2`。

| 部分 | 位置付け |
| --- | --- |
| 第1〜19・22・24章、付録F | 規範的な言語規則。規範的な文法は付録Fだけ |
| 第23章、check出力スキーマ | 規範的なツール契約。プログラムの合法性は変えない |
| 付録D・E | 参考情報。参照先の定義が優先する |
| IMPLの第20・21章、付録A | 初期実装の契約。言語の合法性や意味を決めない |

適合実装は、合法なプログラムだけを受理し、規定された動作を与える。未実装の合法な形式にはUnsupportedを報告できるが、その実装は部分実装である。不正なプログラムの受理や、合法なプログラムへの別の意味の付与は許されない。付録Dの延期項目は、構文や使用許可を与えない。

原文のmust／must notは義務／禁止、mayは制約内の許可、shouldは推奨を表す。違反がコンパイルエラー、Abort、未定義動作のどれになるかは各規則で定める。実装定義は実装が選択して文書化する事項、未規定は許された範囲で文書化せずに選べる事項であり、未定義動作とは異なる。未定義動作はunsafeの約束を破った場合にだけ生じ、その時点以降の動作は保証されない。安全なコードだけでは生じない。

断りのない規則は規範的である。例は規則を補わず、各例は独立する。例中の独自型・APIは定義済みとし、シグネチャだけの例も本体なしの定義を許可しない。`// Error:`は意図的な違反、`text`ブロックはKimigayo以外の表記を示す。

<a id="s-2"></a>

# 2. ソースと字句構造

<a id="s-2-1"></a>

## 2.1. ソースと行

ソースはUTF-8。不正なバイト列はコンパイルエラーとする。先頭にはU+FEFFのBOMを1個だけ置け、ソース本文には数えない。各ファイルはKotonohaのSourceDocumentである（[§18.1](#s-18-1)）。

改行はLF、CRLF、CR。行・列は1から数え、列は行頭からのUnicodeスカラー値の個数で測る。インデント、本体、継続行、ヘッダー式、分岐の接続、先頭ドット行の規則は[§F.4](#s-F-4)に定める。

```kimi
func double(x: i32) -> i32 => x * 2

func run(ready: bool, mode: Mode) -> i32
    if ready => work()
    let n = match mode
        .Fast => 1
        .Slow
            prepare()
            yield 2
    return n

func broken() =>       // Error: the item must follow `=>` on the same line
    work()
```

<a id="s-2-2"></a>

## 2.2. 空白とコメント

リテラル・コメント外の空白はU+0020と改行だけ。タブ、改行しない空白、その他の空白・制御文字はエラーとする。行頭の空白はインデントを定め、行末の空白は無視する。コメントはトークンを隔て、連結しない。

`//`は行末かファイル末まで、`/*`は最初の`*/`までのコメントを始める。ブロックコメントは入れ子にできず、閉じ忘れはエラー。リテラル内では区切り記号も内容だが、文字列補間内では通常のトークン規則を使う。

同一行のブロックコメントはトークン間に置け、その前の空白がインデントになる。改行を含むブロックコメントでは、閉じる`*/`の後の同じ行に空白・コメントしか置けない。

```kimi
let total = 1 /* inline comment */ + 2   // 3

func example()
    /* A multiline comment.
    */
    work()
    finish()                              // Both calls are in the body

func bad()
    /* comment
    */ work()                             // Error: code after a multiline comment's */
```

<a id="s-2-3"></a>

## 2.3. 文書コメント

文書行は、行頭の空白の後に`///`があり、直後が`/`でない行コメント。`///Text`と`/// Text`は該当するが、`////`、コードの後の`///`、`/** ... */`は通常のコメントである。通常のコンパイルでは文書行もコメントとして扱い、合法性・動作に影響しない。

同じインデントで連続する文書行が1つの文書ブロックになる。空行や通常コメントで終わる。宣言前の属性・空行・コメントからなる前置部分で、ヘッダーに最も近いブロックを選び、そのインデントがヘッダー基準位置と一致する場合だけ結び付ける。宣言ごとに最大1つ。以前のブロックへの戻り、無関係な構文・指令・スコープ境界の飛越しはしない。推奨順序は文書、属性、宣言である。

文書を持てるのは、group・rootgroup・struct・enum・contract、名前付き関数と要件・明示的特殊化・`init`・`drop`、プロパティとその要件・ローカル束縛、enumの各Case、関連型の宣言と指定である。パラメーター、ジェネリック・Originパラメーター、ペイロード、アクセサーは所属宣言の文書で説明する。

`rootgroup A.B`の文書はその`B`断片だけに属する。断片・オーバーロードごとに文書を持ち、オーバーライド元、実装する要件、特殊化元から継承・複製しない。指令で除外された構文には結び付かないため、条件付き宣言の文書は指令の管理領域内に書く。

本文はMarkdownとし、慣例として`- name: text`でパラメーターまたは`return`・`abort`・`safety`・`note`・`warning`・`example`を説明する。対応Markdown、抽出・描画・文書診断は実装定義である。

```kimi
/// Adds two values.
///
/// - left: Left value.
/// - right: Right value.
/// - return: Their sum.
func add(left: i32, right: i32) -> i32 => left + right

/// Checks that add can be called.
#Test
func addWorks() => ()

    /// Unattached: its indentation differs from the header baseline.
func other() => ()

#if windows
/// Returns the platform name.
func platformName() -> string => "windows"
```

<a id="s-2-4"></a>

## 2.4. トークンと記号

トークンの綴りは連続させる。隣接させると別のトークンになる場合は区切りが必要。トークンとして認識されても、その位置で使えるとは限らない。コメント・リテラル外でトークンを開始できない文字はエラー。記号、最長一致、ジェネリック閉じ記号の分割、`$`・`#`、使用位置を制限する記号は[§F.2.1](#s-F-2-1)に定める。論理積・論理和は`and`・`or`であり、式に`&&`・`||`は使わない。

```kimi
let r = 0..=9          // ..= is one token
let ne = p != q
let ok = p and q
let no = p && q        // Error: misplaced &&; write `and`
```

<a id="s-2-5"></a>

## 2.5. 名前

すべての宣言名は[§F.2.2](#s-F-2-2)のNameに従い、Unicode NFCでなければエラーとする。自動正規化はしない。双方向制御文字・ゼロ幅接合子を含むCf文字は禁止。名前の一致はUnicodeスカラー列の完全一致で判定し、大文字小文字を区別する。文化依存の比較や文字の折畳みは行わず、見た目が似ていても別名になる。文字分類とNFCはUnicode 15.0.0に固定し、その版の未割当て文字は禁止。条件名にも同じ規則を使う。予約語と文脈キーワードは[§F.3](#s-F-3)に示す。

`_`単独は名前ではなく、次だけに使える。

| 位置 | 意味 |
| --- | --- |
| 実行時パターン | ワイルドカード |
| `#case _` | 最後の全一致分岐 |
| 初期化付きローカルの固定長配列型注釈 | 長さまたは要素型の推論穴 |
| `_ = expression` | 結果を明示的に捨てる |
| `for`の束縛またはタプル要素 | 名前なしの反復束縛 |

`let _`、`var _`、パラメーター名、参照としての`_`などは構文エラー。`_value`のような長い名前には影響しない。

```kimi
let point2 = 1
let _value = 2
let délta = 3          // Valid: U+00E9 in NFC
let Dog = 4
let dog = 5            // A different Name from Dog
let 2point = 6         // Error: a Name cannot start with a digit
let _ = 7              // Error: `_` is not a Name
```

<a id="s-2-6"></a>

## 2.6. 数値リテラル

ASCIIの数字で始まり、[§F.2.3](#s-F-2-3)に従う。型接尾辞はなく、文脈または`123@i32`のような明示的指定で型が決まる。直前の符号を含めた形式を直接リテラルという（[§12.3](#s-12-3)）。桁区切りの`_`は無視する。

浮動小数点リテラルは、正確な十進値を`f32`または`f64`へ1回だけ最近接偶数丸めする。無限大になる値はエラー、ゼロ・非正規化数は許す。それ以外は整数で、大きさが2^128−1を超えるとエラー。

`0x_FF`、`0b__101__`、`1.5e-3`は有効。`1.`は整数とドットであり、メンバーがないためエラー。`0x`、`123abc`、指数の最初の数字を欠く`1e_2`もエラーとなる。

```kimi
let a = 1_000_000
let b = 0x_FF          // 255
let c = 0b__101__      // 5
let d = 1.5e-3
let e = 1.             // Error: the integer 1 followed by a `.` with no member
let f = 0x             // Error: no hexadecimal digit
let g = 123abc         // Error: malformed number
let h = 1e_2           // Error: the exponent needs a digit first
```

<a id="s-2-7"></a>

## 2.7. 文字エスケープ

文字リテラルとエスケープ文字列で共通し、各エスケープはUnicodeスカラー値1つを生成する。

| エスケープ | 値 |
| --- | --- |
| `\0` | U+0000 |
| `\\` | U+005C（バックスラッシュ） |
| `\e` | U+001B |
| `\t` | U+0009 |
| `\n` | U+000A |
| `\r` | U+000D |
| `\"` | U+0022 |
| `\'` | U+0027 |
| `\u(H...)` | 1〜6桁の16進数が表すスカラー値 |

U+10FFFF超、U+D800〜U+DFFF、未知・未完のエスケープはエラー。各エスケープを単独で検査し、サロゲート2つを組にしない。

```kimi
let a = '\u(41)'            // A
let b = '\u(1f600)'         // 😀
let c = '\u()'              // Error: no digits
let d = '\u(0000041)'       // Error: seven digits
let e = '\u(D83D)'          // Error: surrogate
```

<a id="s-2-8"></a>

## 2.8. 文字リテラル

単一引用符内に直接またはエスケープでスカラー値1つを書き、型は`char`。表示上の1文字とは異なり、結合文字単独は許すが、`'e\u(301)'`や`'🇯🇵'`は2値なのでエラー。内容の正規化や補間はしない。

アポストロフィ、バックスラッシュ、U+0000〜U+001F、U+007F〜U+009F、U+2028・U+2029はエスケープする。

```kimi
let letter: char = 'A'
let euro: char = '€'
let quote: char = '\''
let mark: char = '\u(301)'     // A combining mark alone
let empty = ''                 // Error: no scalar value
let pair = 'ab'                // Error: two scalar values
let accent = 'e\u(301)'        // Error: two scalar values
let flag = '🇯🇵'               // Error: two scalar values
```

<a id="s-2-9"></a>

## 2.9. 文字列リテラル

型は`string`、内容はUTF-8。どちらの形式も複数行にできる。

| 形式 | 区切り | エスケープ・補間 |
| --- | --- | --- |
| エスケープ文字列 | 両側の`"`1個 | あり |
| 生文字列 | 両側で同数の`"`3個以上 | なし |

閉じ区切りと、エスケープ文字列の未エスケープの`\`を除き、制御文字・タブも直接記せる。文字リテラルの除外規則は適用しない。リテラル内の改行は元の形式によらずLF1個となり、インデント・先頭末尾の改行を除去せず、正規化もしない。CRは`\r`で書く。リテラル本文はレイアウト処理の対象外。

<a id="s-2-9-1"></a>

### 2.9.1. エスケープ文字列と補間

`\`はエスケープか`\(expression)`による補間を始める。式の値を文字列化して挿入する（[§12.4.2](#s-12-4-2)）。補間末尾は通常の字句解析で対応する`)`を探し、入れ子のリテラル・コメント内の括弧は数えない。レイアウトは[§F.4.1](#s-F-4-1)、入れ子の上限は実装定義である。

```kimi
let greeting = "Hello, \(name)."
let total = "Total: \(price * quantity)"
let lines = "First line\nSecond line"
let block = "
First line
Second line
"                                  // Starts and ends with LF
```

<a id="s-2-9-2"></a>

### 2.9.2. 生文字列

開始時の連続する`"`を数える。1個ならエスケープ文字列、2個なら空文字列、N≥3個なら区切り長Nの生文字列となる。開始列は分割しないため、生文字列の内容を`"`で始めることはできない。

後に現れる最初のM≥N個の引用符列で閉じ、先頭M−N個は内容、末尾N個は区切りとする。N未満の列、バックスラッシュ、改行、`\(`は内容である。したがって`""""""`は空の生文字列ではなく未終了の開始列になる。隣接する文字列リテラルは連結しない。

```kimi
let path = """C:\Users\name\file.txt"""
let token = """"The token """ appears here.""""
let tail = """text""""             // Content is text"
let a = """"x""""                  // Content is x
let b = "\"x\""                    // Content is "x"
```

<a id="s-3"></a>

# 3. 型と値

型はSemantics、Core、必要なOriginを組み合わせ、1層を`Semantics/Core during Origin`と書く。

| 概念 | 意味 |
| --- | --- |
| 型 | Semantics、対象、すべてのOrigin依存を含む完全な型 |
| Semantics | 値の表現・所有・借用・アクセス・安全性の規則 |
| Core | 値の種類・構造・同一性。プリミティブ、struct、enum、タプル、固定長配列、Callable Core、`Array<T>`など |
| Origin | 借用が有効なプログラム上の領域を表す記号的な寿命。借用元、宣言、共通部分、`static`から得る |

`ref/Dog during source`では`ref`がSemantics、`Dog`がCore、`source`がOriginとなる。省略は推論・省略規則に従い、Originを持たない層もある。Coreの要素・型引数は完全な型を保持する。`ref`・`uniq`・`raw`は完全な型を包め、オブジェクトSemanticsはCoreの代わりに実行時Contractビューを対象にできる。

<a id="s-3-1"></a>

## 3.1. プリミティブCore

Scalarは、整数12種（`i8/u8`、`i16/u16`、`i32/u32`、`i64/u64`、`i128/u128`、`isize/usize`）、`f32`・`f64`、`bool`・`char`、ラップ整数12種だけからなる閉じた集合。`string`・Unit・Neverは含まず、ソース上の名前でもない。

整数型は`owner`の整数Core、ラップ整数型は整数型Tに対する`Wrapping<T>`、数値型はこれらと浮動小数点型である。整数型の規則は、明記した場合だけラップ整数型にも適用する。

| 型 | 記憶サイズ |
| --- | --- |
| `i8`・`u8`・`bool` | 8ビット |
| `i16`・`u16` | 16ビット |
| `i32`・`u32`・`f32` | 32ビット |
| `i64`・`u64`・`f64` | 64ビット |
| `i128`・`u128` | 128ビット |
| `isize`・`usize` | 対象環境のポインター幅 |

`bool`の有効なビット列は`0x00`（false）と`0x01`（true）だけ。他をboolとして読むと未定義動作になる。

<a id="s-3-1-1"></a>

### 3.1.1. ラップ整数型

`::Kimi.Wrapping<T>`はTと同じ表現・値解釈を持つ別のScalar。算術結果がTの範囲を超えると、Abortせず2ᴺを法として循環する。ローカルの同名宣言にはこの特性を与えない。

Tは整数型に限り、ジェネリックでは`T is PrimitiveInteger`の証明が必要。浮動小数点、参照、入れ子のWrappingは不可。メンバー・公開コンストラクターはなく、リテラル・算術・変換から値を得る。Tや別のWrappingとの暗黙変換はなく、両方に合うリテラルのオーバーロードは曖昧になる。

Copy・Owned・Sealed・ObjectPayload・読み取り型であり、TのEquatable・Comparable・Utf8Formatと算術Contractに適合する。符号なしでもNegatableを持ち、ビット演算`&`・`|`・`^`を使える。一方、PrimitiveInteger・Positionではないため、配列位置、範囲境界、末尾相対位置の`^`、長さ定数、定数添字、シフト数には使えない。

```kimi
let a: Wrapping<u8> = 250
let b: Wrapping<u8> = a + 10      // 4: wraps modulo 2⁸.
let h = a ^ b                     // XOR.
// let p = ^a                     // Error: Wrapping<u8> is not a Position.
let c: u8 = 250
let d = c + 10                    // Abort: 260 is not representable in u8.

func describe(value: u32) -> () => ()
func describe(value: Wrapping<u32>) -> () => ()
// describe(5)                    // Error: both candidates fit.
describe(5@Wrapping<u32>)
```

<a id="s-3-1-2"></a>

### 3.1.2. 文字と文字列

`char`はU+0000〜U+D7FFまたはU+E000〜U+10FFFFのスカラー値1つを32ビットで保持する。未割当て値も有効。表現が`u32`と同じとは限らず、UTF-8のコード単位でもない。UTF-8から復号したスカラー値はすべて有効なcharになる。

`string`はUTF-8文字列の組込みCoreで、レイアウトは実装定義、`owner/string`はNon-Copy。

```kimi
let euro: char = '€'          // One scalar value, three UTF-8 bytes.
let flag: string = "🇯🇵"
// let bad: char = '🇯🇵'      // Error: two scalar values.
```

<a id="s-3-1-3"></a>

### 3.1.3. UnitとNever

Unit型`()`は値を1つだけ持ち、有意な結果がないことを表す。記憶を占めなくても初期化状態と寿命を持つ。

Neverは値もコンストラクターも持たない組込み型`::Kimi.Never`。綴った名前は通常の名前解決に従い隠蔽され得るが、推論されたNeverは必ずKimiの宣言を指す。`return`・`exit`・`continue`・`yield`・`$abort(...)`の型はNeverで、オペランドが転送先へ結果を渡す。他の制御フロー式がNeverになるかは、全結果源と構造上の完了を検査する[§14.9](#s-14-9)で決める。実行時到達可能性だけで決めず、欠けた結果も免除しない。Neverは完了種別ではなく型である。

NeverはCopy・Owned・Transferableだが、Sealed・ObjectPayload・PrimitiveInteger・Callableではなく、他のContractにも適合しない。完全な値型を置ける位置なら型引数、フィールド、タプル・固定長配列要素、enumペイロードにも置ける。Neverをインラインに保持する値・Caseは構築不能でも、網羅性検査でそのCaseを除外しない。レイアウトは実装定義。

```kimi
let a: Option<Never> = .None
let b = a                // Copy: Never is Copy, so Option<Never> is Copy.
match r                  // r: Result<i32, Never>
    .Ok(let v) => use(v)
    .Err(_) => ()        // Still required: Never payloads give no coverage.
```

<a id="s-3-2"></a>

## 3.2. 複合型

名前付きCoreは`A.B<T, U>`のように修飾・型引数指定できる。入れ子の型はフィールドがなくても外側環境の正規化済み束縛を保持するが、外側インスタンスは暗黙には持たない。

`()`はUnit、`(T,)`は1要素タプル、`(T, U)`は2要素タプル、`(T)`はグループ化。関数型はパラメーター列、`->`、結果型からなり、その列はタプル型ではない。`(T) -> U`と`(T,) -> U`はいずれもTを1個取り、`(()) -> U`はUnit、`((T,)) -> U`はタプルを1個取る。先頭の`task;`はタスクスロットであり、パラメーターではない。

```kimi
func total(single: (i32,), pair: (i32, string), test: (i32) -> bool) -> i32
    return single.0 + pair.0
```

<a id="s-3-2-1"></a>

### 3.2.1. 呼出し可能な値の型

関数値のCoreは3種類ある。

| 種類 | 契約 |
| --- | --- |
| Function Item型 | 解決済み関数宣言とその実体化を指す。キャプチャー環境なし、Copy、Shared呼出し。束縛引数が[§15.2.3](#s-15-2-3)を満たせばOwned |
| 具体的クロージャー型 | 1つの無名関数式と実体化を指し、キャプチャーを保持する。同じ式・型引数なら同じCore、異なる式なら別型。全キャプチャーの完全な型がCopyの場合だけCopy |
| 共通関数型`(A1, ..., An) -> R` | 型消去した環境を所有するNon-Copy型。環境はOwned、呼出しはSharedのみ。環境に依存する借用を返せないが、引数・結果自体はOwnedでなくてよい |

クロージャー環境は利用者から触れるstructではなく、独自dropを定義できない。Function ItemとクロージャーはCallableジェネリクスでも実際のレシーバ・寿命契約を保持し、契約を満たす場合だけ共通関数型へ変換できる。変換後は環境とその後始末を所有する。タスクスロットの有無は型同一性の一部で、異なるもの同士は変換できない。

各値はOrigin束縛を保持する。直接の借用入力で省略されたOriginは呼出しごとに束縛され、結果は環境と独立にその入力へ依存できる。記述した入力Originは既存の固定Originを指す。呼出しごとに束縛する実装は、そのOriginを実体化して固定入力に合わせられるが、束縛済みの入れ子依存は固定のまま。空の環境は純粋性、無割当て、並行安全性を意味しない。共通関数値への`ref/F`はCopy、`uniq/F`はNon-Copyで、どちらも呼出し能力を増やさない。

```kimi
func twice(x: i32) -> i32 => x * 2

let item = twice                    // Function Item: Copy.
let common: (i32) -> i32 = twice    // Converted to a common Function Type.
// let copy = common                // Error: a common Function Type is Non-Copy.
let moved = common@move
```

<a id="s-3-2-2"></a>

### 3.2.2. 弱参照

`Kimi.Weak<s/T>`は`s is rc or arc`で宣言されたコンパイラー管理のNon-Copy struct。引数は有効なオブジェクト対象Xへの`rc/X`・`arc/X`、または許容集合がその2種内のペアに限る。弱い責任を1つ所有し、ペイロードは所有しない。外側はownerで、`ref/Weak<S>`はWeakスロットを借用する。移転には`@move`、複製には`Kimi.Intrinsics.clone`を用い、利用者はフィールド・dropを置換できない。

空のWeakはなく、不在には`Option<Weak<S>>`を使う。期限切れも存在する値だがupgradeできない。変性・Loan要件・OwnedOriginsは、どの状態でもSを格納するものとして解析し、Sの完全な型とLoan依存を保持する。アクセス権は与えず、メンバーアクセス、オブジェクト借用、実行時is、検査付きキャスト、ビュー変換の前にupgradeが必要。

```kimi
let strong: rc/Item = makeItem()@rc
let weak = Kimi.Intrinsics.downgrade(strong@ref)    // Weak<rc/Item>
let again: Option<rc/Item> = Kimi.Intrinsics.upgrade(weak)
// let copy = weak                                  // Error: Weak is Non-Copy.
```

<a id="s-3-2-3"></a>

### 3.2.3. Optional型

`T?`は名前解決によらず`::Kimi.Option<T>`そのもの。`?`ごとに1層増え、平坦化しない。

| 表記 | 展開 |
| --- | --- |
| `ref/T?`、`ref/obj/T?` | `Option<ref/T>`、`Option<ref/obj/T>` |
| `ref/(T?)` | `ref/Option<T>` |
| `[N of T?]`、`[N of T]?` | `[N of Option<T>]`、`Option<[N of T]>` |
| `(T) -> U?`、`((T) -> U)?` | `(T) -> Option<U>`、`Option<(T) -> U>` |

`(A)? -> B`は不正。Semanticsで関数型全体を包む場合は括弧が必要。Originの対象を先に決めるため、`ref/T? during a`は`Option<ref/T during a>`。束縛集合は名前付き型の直後、`?`より前に1つだけ書く。`View<T>{v}?`は有効だが、`View<T>?{v}`、`(View<T>){v}`、`View<T>{v}{w}`は不正。

展開で依存・Loanは消えず寿命も延びない。`ref/i32? during a`を`<s/T>`に渡すと、sはowner、Tは`Option<ref/i32 during a>`になる。`is Dog?`、`T?.Some`、`@ref?`のような専用の名前位置では使えない。`@S?`のSは型で、`x@ref/T?`はOption型への指定であって借用やSome構築ではなく、元から同じ型の場合だけ有効。暗黙の包み・取出し・既定値・引数省略はなく、`raw/T?`の`.None`と`.Some(null)`は異なる。OptionとUnitの表現は実装定義。

```kimi
let nested: i32?? = .Some(.None)
let noBorrow: ref/i32? during static = .None

func wrap<T>(value: View<T>) -> View<T>{result}?
    origin result.source == value.source
    return .Some(value@move)
```

<a id="s-3-3"></a>

## 3.3. Semantics

| カテゴリー | 構文 | 意味 |
| --- | --- | --- |
| `value` | `T`、`owner/T` | 直接所有する値 |
| `valueborrow` | `ref/T`、`uniq/T` | 値の共有／排他可変借用 |
| `object` | `obj/T`、`rc/T`、`arc/T` | 排他所有／非原子的参照計数／原子的参照計数のオブジェクト |
| `objectborrow` | `objref/T`、`objuniq/T` | オブジェクトの共有／排他可変借用 |
| なし | `raw/T` | 生ポインター |

さらに`borrow`はref・uniq・objref・objuniq、`owning`はowner・obj・rc・arc、`reference`はowner以外すべて（rawを含む）。Semanticsカテゴリーはこの7つだけ。

ジェネリックのSemantics位置に置けるのはペアバインダーだけで、`<s/T>`のsは完全な型の外側Semantics、Tはその直接の対象を表す。構文位置によってパラメーター種別は変わらない。カテゴリー名はペアバインダーを対象とする要件では隠蔽できず、他の位置では通常の名前である。値型やAdaptation略記としては使わない。具体的Semanticsも要件にでき、許容集合で判定する。

判定するのは外側の層だけ。`reference`は安全な借用を保証せず、`borrow`は外側の安全な借用を要求する。`owning or borrow`はrawだけを除外する。owningは再帰的性質Ownedとは別である。

値の借用は参照先型の記憶を借り、その中のポインターをたどらない。`ref/(rc/T)`はハンドルスロット、`objref/T`はオブジェクトを借りる。共有借用は複数共存できるが、排他借用と競合するアクセスは共存できない。オブジェクトは動的ペイロード、型情報、所有責任を保持し、必ずしもヒープに置かれない。rc・arcは所有参照がある間だけ生存を保ち、原子的計数だけで並行更新は安全にならない。完全なSealedペイロードは`@follow`で選び値として借用できる。並行実行の延期範囲は付録Dに従う。

```kimi
let node: rc/Node = makeNode()@rc
let slot: ref/(rc/Node) = node@ref     // Borrows the handle slot.
let view: objref/Node = node@objref    // Borrows the object.

func measure<s/T>(value: s/T) -> isize
    s is owning or borrow
    return 0
// measure(pointer)                    // Error: pointer: raw/i32; raw is not admitted.
```

<a id="s-3-3-1"></a>

### 3.3.1. オブジェクトビューと同一性

オブジェクトビュー型は、オブジェクトSemantics、View Target（Coreまたは関連型を固定した実行時Contractビュー）、objref・objuniqなら外側Originからなる静的型。オブジェクト借用にも値借用と同じ整形式規則を適用する。ObjectPayloadを満たすowner Coreだけを具体的ペイロードにでき、Neverと辞退したCoreは不可。SealedなView Targetならペイロードの完全性を証明できる。実行時ContractビューはCoreではなく、`objref/Speaker`に対応する`owner/Speaker`はない。

動的型は構築した具体的Core。その実行時同一性ではOriginを再帰的に消すが、型引数内のSemanticsは残す。完成したオブジェクトは、ビュー変更・全ペイロード置換でも動的型・同一性・破棄責任を保ち、別型への再初期化は禁止。同じアドレスの新オブジェクトは旧オブジェクトの同一性・Loan・フロー事実を継承しない。

`Supports(D, V)`は、VがD自身または直接・間接の基底Core、あるいは`ObjectViewCompatible(C)`かつ`Implements(D, C)`を満たす実行時ContractビューCの場合だけ成立する。アップキャスト、ビュー検査、検査付きキャストに使うが、アクセス・所有・Owned・Loan有効性・不適合メンバー呼出しを許可しない。

スライシング、ownerの派生型から基底型への暗黙変換、ref・uniqの派生型変換、コンテナー共変はない。オブジェクトのアップキャストは明示し、継承したレシーバ射影はレシーバだけを供給する。

```kimi
// Dog is a struct whose base is Animal.
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal     // Explicit upcast; animal owns the whole Dog.
let view = animal@objref
// let back = view@objref/Dog        // Error: Animal does not imply Dog.
```

<a id="s-3-3-2"></a>

### 3.3.2. 入れ子のSemanticsとOrigin注釈

`ref/V`・`uniq/V`・`raw/V`の参照先Vは完全な値型で、そのSemanticsを保った記憶を指す。括弧は型をまとめるだけ。`ref/((i32) -> bool)`は関数値への借用、`(ref/i32) -> bool`は借用引数を取る関数型で、`ref/(i32) -> bool`は不正。

`during`はそのAnnotatedTypeの最初の明示的Semanticsに付く。なければ、直接書かれた名前付き型の唯一のスキーマスロットに付く。それ以外は対象なしのエラーであり、括弧、別名、型引数、要素、シグネチャの内部を探したりownerを除いて探したりしない。指定可能なのはref・uniq・objref・objuniq・rawとペアバインダーの条件付きスロット。`?`の後に書くが、対象決定、Optional展開、整形式検査の順に処理する。1つのAnnotatedTypeには注釈1つだけで、括弧で内部層を再注釈できない。

| 表記 | 意味 |
| --- | --- |
| `ref/(uniq/T during b) during a` | 外側a、内側b |
| `Slice<T> during a` | Sliceの唯一のスロットをaへ束縛 |
| `raw/ref/T during a` | 外側rawのOriginがa |
| `(ref/T)? during a`、型パラメーター`T during a`、`(ref/T during a) during b`、`ref/T during a?` | エラー |

`ref/View<ref/U during c>{v} during a`ではaは外側借用、cは型引数、vはViewの束縛集合。関数型末尾のduringは結果に付く。

正規化では別名・グループ化・冗長なowner接頭辞を展開するが、参照層はすべて残す。`ref/ref/T`と`ref/T`は別型で、`owner/V`もVの参照を消さない。オブジェクトSemanticsにはObject Targetが必要なため、`ref/obj/T`は有効、`obj/ref/T`は不正。ジェネリック置換も同じ規則に従う。

各層は固有のOriginと整形式条件を保ち、外側注釈は内側を置換しない。duringは型の層や値操作を追加しない。rawは安全な参照や寿命延長を保証しない。Copyを決めるのは外側で、`ref/uniq/T`はCopy、`uniq/ref/T`はNon-Copy。共有経路から内側Non-Copy値のMove、uniqの排他使用、スロット更新はできず、共有再借用も外側Loanの範囲を超えない。外側参照のMove・破棄は参照先を壊さず、排他的なスロット置換も内側の型・Origin・Loan制約を守る。

```kimi
var number: i32 = 1
var exclusive = number@uniq                  // uniq/i32
let shared: ref/uniq/i32 = exclusive@ref     // Shared borrow of the slot; Copy.
let handle: obj/Node = makeNode()@obj
let slot: ref/obj/Node = handle@ref          // The handle slot, not objref/Node.
// let boxed: obj/ref/i32 = number@ref       // Error: a borrow is not an Object Target.
```

<a id="s-3-3-3"></a>

### 3.3.3. 参照先の選択と読み取り

参照値は通常、その参照先として読まれない。`@follow`または参照経路選択でPlaceを選ぶ。暗黙に参照先を読むのは[§3.5.3](#s-3-5-3)の値読み取りと、固定の期待型`ref/U`に対して層を通じた共有参照1つを作る場合だけ。参照を通じてNon-Copy値を取り出すことはできず、部分選択・比較・借用で利用する。

安全な参照に`*`はない。rawへは`@raw`、rawから安全な参照へはunsafe内でraw Placeを借用する。明示的借用は書かれたスロットを借り、格納された排他参照の裸の取得は再借用する。

```kimi
func bump(counter: uniq/i32, limit: ref/i32)
    counter@follow += 1          // Explicit selection of the referent.
    let snapshot: i32 = limit    // Value read.
    // counter += 1              // Error: an assignment target is never redirected.
```

<a id="s-3-4"></a>

## 3.4. Placeと記憶

記憶は、ローカル、静的スロット、集約の一部、オブジェクトペイロード、実体化した一時値、生の記憶領域などに値を保持する。生の記憶領域自体は格納値のPlaceを宣言しないが、配置された値と構成要素は各型のPlaceを占める。破棄責任は所有値を破棄する義務である。

Placeは、完全な型、アクセス経路、能力、依存を伴って選ばれた記憶。Place式の分類は括弧でも変わらない。Place自体は値ではなく、格納・引渡し・型引数への束縛・Optionへの包装はできない。位置を保持するには借用する。値の構築完了と現在の完全性は区別する（[§15.1.2](#s-15-1-2)）。

| 能力 | 操作 |
| --- | --- |
| Read | Copy、共有借用、比較、観察 |
| Write | 初期化、置換 |
| Take | 値と破棄責任の取出し |

3能力は独立する。所有letの根はTakeを持つがWriteを持たず、uniq参照先はWriteを持つがTakeを持たない。子Placeは宣言された完全な型を保ち、親のSemanticsを継がず、共有経路で失った排他能力を回復しない。Loanは能力の使用を制限し、能力自体を変更しない。

同一、片方が他方のインライン部分、または1バイトでも共有するPlaceは重なる。同じアドレスの別々のゼロサイズPlaceは、包含関係がある場合だけ重なる。Loan検査上の非重複は[§15.6.2](#s-15-6-2)に従う。

アクセス指定子（ローカル、フィールド、プロパティ、タプル要素、添字、参照先、生ポインター逆参照）はPlaceアクセス、ゲッター等による値取得、エラーのいずれかへ解決する。これらは別々の解決結果であり、失敗時の代替手段ではない。失敗したMoveを借用やゲッターへ切り替えない。

初期化は空のPlaceへ値を置き、破棄は値の寿命を終えてUninitializedにする。記憶自体の寿命後にはアクセスできない。取得（Copy・Move）、配置（初期化・置換）、破棄を区別し、1つの代入でMoveと置換の両方が起こり得る。Move元は再破棄しない。借用・rawの破棄は参照先を破棄せず、rc・arcの破棄は所有参照1つを解放する。Takeは能力、Moveは移転操作、Consumeは静的検査である。

```kimi
var pair: (string, i32) = ("a", 1)
let n = pair.1               // Read: Copy.
let name = pair.0@move       // Take: Partial Move.
// let all = pair@move       // Error: pair is not complete.
pair.0 = "b"                 // Write; pair is complete again.
```

<a id="s-3-4-1"></a>

### 3.4.1. アクセス経路と値の種別

| 経路 | 対象 |
| --- | --- |
| Direct | 参照を解決しないローカル・引数・静的記憶・一時Placeとそのインライン部分、所有objから届くペイロード |
| 排他参照経由 | uniq・objuniqを解決する |
| 共有参照経由 | ref・objref・rc・arcを解決する |

raw Placeにはこのアクセス経路を与えず、[§5.2](#s-5-2)の権限・借用・Take規則を適用する。すべての借用は、その経路の権限内に限る（[§15.1.5](#s-15-1-5)）。一時値の標準射影は一時値を実体化し、その内部PlaceをDirect経路で指す。

値の種別は次の先勝ち規則で決まり、経路では変わらない。

1. 外側Semanticsがref・uniq・objref・objuniqなら借用値。格納された参照はPlaceでもある。
2. それ以外のPlaceは所有Place。rawや所有ハンドルのスロットも含む。
3. それ以外の一時値は所有一時値。

所有ハンドルまたはobjref・objuniqはオブジェクト種の入力、それ以外は値種の入力。メソッド、アクセサー、関数値の直接呼出しのレシーバをレシーバ式とする。`Type.method(x)`のself引数、代入先、match・forのSubjectは含めない。

<a id="s-3-4-2"></a>

### 3.4.2. 貸出し点

貸出し点は借用対象の記憶で、Loanの基点となる。

| 操作・値 | 貸出し点 |
| --- | --- |
| 明示的`@ref`・`@ref/V`・`@uniq`・`@uniq/V` | 書かれたPlaceそのもの。参照格納時もスロット |
| 借用値の再借用 | 参照先 |
| 所有Place | 標準のフィールド・タプル・配列要素射影で届くPlace |
| 所有一時値／オブジェクト借用／ペイロード選択／基底射影 | 一時値／オブジェクト／ペイロード／基底部分 |

排他借用には貸出し点が排他的に書込み可能であることが必要。参照スロットの`@uniq`も所有Placeと同じ根の規則に従う。対象の有効性を保つために保護される所有者は、貸出し点そのものには含めない。

```kimi
var number = 1
var other = 2
var reference = number@ref
let slot = reference@ref     // Borrows the slot `reference`.
// reference = other@ref     // Error: slot is used below.
inspect(slot)
```

<a id="s-3-4-3"></a>

### 3.4.3. 参照経路の選択

メンバー・添字・メソッドレシーバは、各層の完全な型に必要な宣言（Contract適合や型同一性制約が提供するものも含む）があればそこで止まる。なければref・uniq層、または許容集合が`value or valueborrow`内のペア層をたどって繰り返す。これを安全な値参照層という。

選択には型だけを使い、後の取得・Loan失敗で別層へ戻らない。能力・Origin・Loanを保ち、オブジェクトではView Targetの宣言で止まる。借用forの入口と構造パターンも同じ選択を使うが、値読み取り・比較は末端を選ぶ。

ジェネリック本体では制約で形が決まらない型パラメーター・関連型が末端となり、宣言された能力だけを使う。実体化で選択を変えない。宣言された等価性が形を証明する場合はたどれる。裸の取得、単一名の束縛、型推論は参照先やペア層をたどらない。束縛パターンは[§14.8.1](#s-14-8-1)の範囲だけたどる。更新要求は選んだ経路だけへ伝わり、添字内の関数やゲッターの背後には伝わらない。

取得は選ばれたPlace自身の値種別で決める。排他経路のuniq・objuniqスロットは再借用する。レシーバ式以外で所有Placeを排他的に貸すには、そのPlaceに`@uniq`・`@objuniq`を付ける。`holder@uniq.items`が貸すのはholderで、itemsではない。添字は必要能力によりindex・indexUniqを選ぶ。

```kimi
// holder: a var Holder with Fields items: Array<i32> and link: uniq/Counter; values: var Array<Counter>; fill takes uniq/Array<i32>.
func update(node: uniq/Node)
    node.count += 1           // Selects the Field through the reference.

let link = holder.link        // Reborrow of a stored uniq/Counter.
fill(holder.items@uniq)
// fill(holder@uniq.items)    // Error: lending holder does not lend items.
values[0].bump()              // A Receiver Expression needs no spelling.
```

<a id="s-3-5"></a>

## 3.5. CopyとMove

Copyは元をInitializedのまま複製し、利用者コード・メモリー割当て・参照計数変更・資源取得を行わない。Moveは値と責任、または借用アクセスを移転して元をMovedにし、利用者コードを実行しない。Place PがTを格納するとき、`@`なしの裸の取得は次の先勝ち規則を使う。

| 操作 | 結果 |
| --- | --- |
| 裸、TがCopyと証明済み | Copy |
| 裸、Tがuniq/U・objuniq/U | 同じSemanticsで再借用 |
| 裸、その他のPlace | エラー。`@move`が必要 |
| `P@move` | Copyかどうかによらず移転し、PをMovedにする |
| `P@copy` | Copyの証明が必要な明示的Copy |
| `P@ref`・`P@uniq`および型付き形式 | P自身を借用 |
| 一時値の裸の取得・`@move` | 追加のCopy・Moveなしで値を渡す |

Placeからの移転は`@move`だけで、MovableなPlaceが必要。型・SemanticsへのAdaptationやオブジェクト構築は、Placeを暗黙にMoveしない。この規則は初期化・代入・引数・結果・集約要素・ペイロード・既定値・キャプチャー・結果を捨てる位置すべてに適用する。レシーバは[§7.3](#s-7-3)に従う。固定期待型での共通Adaptationは明示的取得後に最大1回で、Copy未証明を暗黙のMove・借用に読み替えない。

ジェネリック本体は許容場合ごとに操作を計画し、同名関数の重なるパラメーターは同じ取得モードを持つ。Copy能力はlet・varやLoanとは独立だが、実行するCopy・Moveは読み取り制限・Loan競合を守り、寿命を延長しない。取得済みSubjectをパターンへ分配するときは束縛ごとの`@move`は不要。

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()@obj
let owned = node@move     // Transfer; node becomes Moved.
// let bad = node         // Error: a bare Non-Copy Place never Moves.
```

<a id="s-3-5-1"></a>

### 3.5.1. 格納された排他参照の再借用

裸の再借用は`P@follow@uniq`または`P@objuniq`に相当し、PをMoveしない。子は格納型を保ち、外側Originを親の範囲内に制限する。Loanの祖先関係、初期化状態、スロット保護、破棄依存を保ち、参照先と親Loanに依存する。

子が生きている間は、Pやそれを含むPlaceのMove、競合借用などを禁止する。排他的借用はreplaceのようにその場で破棄する対象となる場合を除き競合し、子が排他的なら共有借用も競合する。最後の使用後は親を再使用できる。

参照先への排他能力が必要で、参照を格納するlet・引数も再借用できるが、所有let値に排他能力を与えるわけではない。`ref/(uniq/U)`のような共有経路からは排他再借用できず、期待型ref/Uなら共有アクセスを得る。再借用は外側が排他参照の場合だけで、`Option<uniq/U>`等は全体取得する。一方、`[r]`などのリテラルは要素を個別に取得する。

格納する再借用は形成時から生き、呼出しレシーバ・引数用は予約を開始する。uniqへの`@copy`はエラー、`@move`は参照を移転し、後のAdaptationで元を復活させない。直接期待型に合う移転済み排他参照は再借用しない。`_ = r`は子を再借用して破棄する。

```kimi
var number: i32 = 1
let r = number@uniq
let first = r                 // Reborrow: uniq/i32.
first@follow += 1
let held = r
// r@follow += 1              // Error: conflicts with the later use of held.
held@follow += 1
let inner = r@follow@ref      // ref/i32; r@ref would be ref/(uniq/i32).
let moved = r@move
```

<a id="s-3-5-2"></a>

### 3.5.2. Copy能力

Copyは組込みContractで、同じ形の独自Contractでは代用できない。

| 型 | Copy条件 |
| --- | --- |
| 整数・ラップ整数・浮動小数点・bool・char・Unit・Never | 常にCopy |
| ref・objref・raw・Slice・Function Item | 常にCopy |
| FromEnd・Start・End・Range・ClosedRange・ResolvedRange | 常にCopy |
| `Kimi.Loan<T>` | TがCopy |
| 具体的クロージャー・タプル・固定長配列 | 全キャプチャー／構成要素がCopy |
| uniq・objuniq・obj・rc・arc・UniqSlice・共通関数・Weak・Array・Dictionary・owner/string | Non-Copy |
| 利用者のstruct・enum | 明示的な適合宣言が必要 |

struct・enumは`Self is Copy`または条件付き適合を宣言する。structの自身のフィールドと直接基底がCopyで、利用者dropがなく、enumでは全ペイロードがCopyであることを制約と条件から証明する。派生型は個別に宣言し、ペイロードなしenumにも宣言が必要。Copy本体は書けず、全フィールドがCopyでも自動適合しない。スライシングも起こらない。

証明は全許容束縛で必要で、制約を推測せず、一部の実体化成功では未証明の宣言を正当化できない。`ref/T`だけを保持するenumに`T is Copy`は不要。Copy不明をCopyともNon-Copyとも扱わず、裸の値取得にはCopyの証拠か排他参照型の証明を必要とする。rc・arc・Weakの複製にはcloneを使う。

```kimi
enum CopyOption<T>
    Self is Copy when T is Copy
    Some(T)
    None

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

<a id="s-3-5-3"></a>

### 3.5.3. 値読み取り

読み取り型はScalar、またはPositionかPositionRangeを満たす型で、すべてCopyかつOwned。ジェネリックではPrimitiveInteger・Position・PositionRangeの証明、または`T is PrimitiveInteger`のWrapping&lt;T>が必要。

読み取り型Tが必要な位置では、Tを末端に持つ安全な値参照層をたどり、末端をCopyする。対象は共通Adaptationの値位置、読み取り型の結果源、組込み演算子（範囲境界・前置`^`を含む）、bool条件、読み取り型を所有するレシーバ、数値・Wrapping・ビット変換と読み取り型での同一型取得である。演算子は末端型から操作を選ぶが、算術経路が取得方法を決め、共有Contract要件は数値入力を借用する。

オブジェクトSemantics、raw、フィールドはたどらない。各層の初期化・能力・Loanを検査する。変換や借用を追加せず、その時点の値を読む。未解決リテラルは末端型に合わせ、オーバーフロー・短絡規則は変えない。代入先は参照先へ振り替えない。読み取り型でないCopy参照先には明示的`@follow`が必要で、比較は[§13.4](#s-13-4)に従う。

```kimi
for number in numbers        // numbers: Array<i32>; number: ref/i32.
    let reference = number   // ref/i32.
    let doubled = number * 2 // i32.
    let wide = number@i64    // Value read, then conversion.
    // number += 1           // Error: the target is the reference, not its referent.
```

<a id="s-3-6"></a>

## 3.6. 一時値

一時値は永続的Placeではない式の結果。記憶が必要なら実体化して一時Placeにするが、再評価・Copy・メモリー割当て・寿命延長はなく、Consume失敗をReadに変えない。新たに所有した一時値は、ゲッター結果などの制限がなければ排他的に書込み可能だが、Loan・Origin・読取り専用部分は守る。

寿命は最外側の式の終了までで、引数一時値は呼出しを含む。match・forのSubject評価で作った一時値は構文全体の終了まで生き、Subjectの後に生成逆順で破棄する。その他も生成逆順。途中の制御転送では[§16.2.1](#s-16-2-1)に従い、部分Move後は残った部分だけを破棄する。各条件評価は寿命境界となる。

借用である一時値はその参照とOriginとして取得し、`@ref`を付けると層が増える。一時値の借用・Slice形成で寿命は延びない。結果転送はスコープ終了処理の前に値を確保する。

```kimi
let view = makeResource()@ref
// inspect(view)                // Error: the temporary expired.
match makeHolder().item         // A Place in a temporary: shared borrow.
    .Some(let v) => inspect(v)  // v: ref/Item; the Holder temporary is still alive.
    _ => ()
```

<a id="s-3-7"></a>

## 3.7. OriginとLoan

Originは領域、Loanは借りたPlaceとアクセス方法を記録する。`origin a outlives b`と`origin a == b`は領域間の関係を表す。シグネチャ・型宣言では記述し、ローカルでは推論する。省略は[§15.4](#s-15-4)に従う。

```kimi
func first(items: ref/Array<i32>) -> ref/i32 during items
    return items[0]@ref
```

<a id="s-3-8"></a>

## 3.8. 型関係と操作

型関係だけでは値操作を許可しない。借用・再借用・Copy・Moveは別途検査し、未解決の型・Origin情報は互換性の証拠にならない。

正規化同一性は、正規化後の構造・宣言・型引数・Semantics・Origin束縛を比較し、束縛Originパラメーターはバインダーで対応させる。異なる名目的宣言は同一にならず、実行時オブジェクト同一性とも別である。

部分型`A <: B`が成立するのは、正規化後にOrigin束縛以外が同一で、各Origin位置が変性を満たす場合だけ。共変は`a outlives b`、反変は`b outlives a`、不変は`a == b`を要求する。duringなしrawは、すべてのOriginがoutlivesするOriginを持つものとして扱い、`raw/T during a <: raw/T`となる。

これが唯一の部分型規則で、関数型も同じ。Neverは他のどの型の部分型でもない。Wrapping&lt;T>とT、uniq/Tとref/T、派生型と基底型の間にも部分型関係はない。構造が合っていてOrigin関係だけが不足する場合は型不一致ではなくOrigin関係の問題とする。部分型関係は操作を挿入せず寿命も延ばさない。

Never式は、他の点で有効な任意の期待値型に、値変換なしで合う。ただし部分型関係ではない。完了しない経路では外側の値操作を行わず、制御転送のオペランドはそれ自身の結果境界で検査する。

Callable互換性、期待結果による候補選別、暗黙・明示Adaptation、アップキャスト、借用、数値変換はそれぞれの規則で決める。暗黙Adaptationは取得後に最大1回で連鎖しない。型が完全一致しても、Moved状態、能力不足、Loan競合により取得が失敗し得る。その失敗で名前解決・オーバーロード選択をやり直さない。

```kimi
func pick(long: ref/i32 during a, short: ref/i32 during b) -> ref/i32 during b
    origin a outlives b
    return long                          // OK: ref/i32 during a <: ref/i32 during b.

func f(handler: (i32) -> Never) -> ()
    let g: (i32) -> i32 = handler@move   // Error: (i32) -> Never is not a subtype of (i32) -> i32.
```

<a id="s-4"></a>

# 4. 配列・添字・スライス

固定長配列`[N of T]`はコンパイル時の長さNを持ち、要素をインラインに保持する。`Array<T>`は可変長の記憶を所有し、`Slice<T>{source}`・`UniqSlice<T>{source}`は連続要素への共有・排他ビューとなる。Dictionaryにも本章の添字・更新規則を適用する。

<a id="s-4-1"></a>

## 4.1. 固定長配列

型同一性には評価済みの長さと、Semantics・Originを含む完全な要素型を使う。異なる長さ、固定長配列とArray、所有配列とSliceの間に暗黙変換はない。

要素は添字順にインライン配置し、ヒープ割当てを行わない。入れ子配列では最内添字が連続する。d=`stride(T)`なら要素iのオフセットはi×d、アラインメントはN=0でも`alignment(T)`、サイズと配列のstrideはN×d。d=0では別要素が同じアドレスを持ち得る。具体的レイアウトが対象環境の限界または非負isizeに収まらなければコンパイルエラー。

`[0 of T]`はサイズ0で何も初期化・破棄しないが、Tの検査は省かない。`[0 of Self]`をインラインに保持するstructも再帰レイアウトエラーになる。参照・ポインターの参照先はインライン依存を作らない。

```kimi
let a: [4 of i32] = [1, 2, 3, 4]
let b: [(2 + 2) of i32] = a    // Same Type.
let c: Array<i32> = a          // Error: no conversion from a fixed array.

struct Node
    var children: [0 of Node]  // Error: recursive inline layout.
```

<a id="s-4-2"></a>

## 4.2. 長さ式

長さは非負で対象環境のisizeに収まるコンパイル時整数。ジェネリックの長さは実体化まで記号として保持する。

式に使えるのは整数リテラル、長さパラメーター、定数として読める束縛、括弧、単項`+`・`-`、二項`+`・`-`・`*`・`/`・`%`。定数として読める束縛は、これらだけで再帰的に初期化された整数letローカル、またはアクセス可能な標準getを持つ静的格納プロパティ。引数、var、インスタンスフィールド、独自・計算アクセサー、呼出し、循環初期化子は使えない。依存長では初期化子へ展開する。

評価は検査付き算術で行い、初期化子・ゲッター自体を実行しない。型付き定数は型を保ち、長さパラメーターはisize。リテラルだけの部分式には確定したオペランド型、なければisizeを使う。異なる整数型は混在できず、中間値の負数は許すが、途中を含むオーバーフロー、ゼロ除算、非整数、最終長の負数・範囲外はコンパイルエラー。

ルート束縛は起動ローカルなので、共有定数はgroupに置く。公開シグネチャは非公開の長さ定数を使えるが、その値は公開APIの一部になる。

```kimi
group Dimensions
    public let Width: isize = 4
    public let Height: isize = 3

func fill()
    let buffer: [(Dimensions.Width * Dimensions.Height) of u8] = [12 of 0]
    let bad: [Dimensions.Width * 2 of u8]  // Error: parentheses required.
    let negative: [(-1) of u8]             // Error: negative length.
    let invalid: [(4 / 0) of u8]           // Error: division by zero.
```

<a id="s-4-3"></a>

## 4.3. 配列の構築

<a id="s-4-3-1"></a>

### 4.3.1. 配列リテラルと型注釈の穴

固定長配列を期待する場合、リテラルは要素数がNと一致するその型を作り、ソース順に要素を取得する。独立したリテラルはそれ以外ならArray&lt;T>を作り、呼出し引数では候補ごとに合わせる。空リテラルには既知の要素型が必要。

初期化付きローカルlet・varの固定長配列注釈では、記述した配列構造の任意の深さで、長さ・要素型の一方または両方を`_`にできる。初期化子だけですべてを決める。長さはリテラル要素数または初期化子の固定長、要素型は通常の型根拠と数値既定型で決める。内部に固定長の期待が書かれていないリテラルはArrayになる。全要素の根拠は一致する必要があり、失敗をArrayへ切り替えない。

注釈で書いた部分は、呼出し先シグネチャからの推論も含め初期化子への期待となる。未初期化宣言、シグネチャ、フィールド、静的記憶、明示的ジェネリック引数に穴は使えない。

```kimi
func zeros<T>() -> [3 of T]
    T is PrimitiveInteger
    return [3 of 0]

var values: [_ of i32] = [1, 2, 3]                       // [3 of i32]
let wide: [_ of i64] = zeros()                           // T = i64; length 3 from the signature.
let rows: [_ of _] = [[1], [2, 3]]                       // [2 of Array<i32>]
let matrix: [_ of [_ of f32]] = [[1.0, 2.0], [3.0, 4.0]] // [2 of [2 of f32]]
let unknown: [_ of _] = []                  // Error: no element Type.
let ragged: [_ of [_ of _]] = [[1], [2, 3]] // Error: conflicting inner lengths.
let wrong: [3 of i32] = [1, 2]              // Error: element count.
```

<a id="s-4-3-2"></a>

### 4.3.2. 最初の構築

型注釈だけの宣言はUninitializedで、ゼロ埋めしない。初めての構築には配列全体の初期化・代入が必要で、その前に一部だけを書き込めない。

```kimi
var pending: [2 of i32]
pending[0] = 1   // Error: pending was never constructed.
pending = [1, 2]
```

<a id="s-4-3-3"></a>

### 4.3.3. 反復値による構築

`[Length of value]`は固定長配列を作り、Array期待とは合わない。Lengthは[§4.2](#s-4-2)に従い、要素型にはCopyが必要。長さ0でもvalueをちょうど1回評価・取得し、各要素へCopyする。長さ0を含め、配列はvalueのLoanを保持する。

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false] // [8 of bool]
var x = 1
let none = [0 of x@ref]  // [0 of ref/i32] carries x's Loan.
// x = 2                 // Error: none is used below.
inspect(none)
```

<a id="s-4-3-4"></a>

### 4.3.4. `noinit`

宣言の`=`直後で、括弧なしの初期化子全体が`noinit`なら、名前解決より先に特別扱いする。格納プロパティや既定引数でも認識するが、有効なのはunsafeブロック内の、穴のない固定長配列注釈を持つローカルvarだけ。他の位置は名前として扱わずエラー。`(noinit)`は通常の式となる。

要素は別名展開後にownerのScalarでなければならない。整数・ラップ整数・浮動小数点・bool・charが該当し、ジェネリックではPrimitiveInteger等で証明する。Copyだけでは足りない。配列全体の構築を完了させ、初期の要素書込みだけを省く。

各要素は読む前に書かなければ未定義動作。残存ビットは書込みではなく、関数・参照・ビュー・raw経由も同じ。全配列のCopy・Move・値渡しは全要素を読む。要素代入、アドレス取得、借用、slice・sliceUniq、メタデータ取得、破棄は要素を読まない。最適化・呼出し規約も、プログラムより先に要素を読んではならない。

```kimi
unsafe
    var buffer: [3 of i32] = noinit
    var view = buffer.sliceUniq()
    view[0] = 10
    view[1] = 20
    view[2] = 30
    let copied = view.slice().toArray() // Every element was written first.

func f(x: i32 = noinit) => x // Error: noinit is only for a local var.
```

<a id="s-4-4"></a>

## 4.4. 関数の長さパラメーター

<a id="s-4-4-1"></a>

### 4.4.1. 宣言と推論

`<length N>`は値名前空間の非負isize定数スロットで、GenericArityでは1個に数える。`<N>`は型スロット。長さスロットを持てるのは関数だけで、型宣言に値パラメーターはない。

明示的ジェネリック引数では全スロットを順に、長さ式（lengthは書かない）または型で指定する。省略時は候補ごとに、既知の固定長配列型とその候補に合わせたリテラルから長さ・要素型を推論する。根拠はすべて一致し、確定済みArrayを別型にしない。期待型は未解決部分だけを補う。`N * 2`などの方程式を逆算せず、N確定後に検査する。本体、後の使用、候補順からは推論せず、固定長・動的リテラルの優先順位もない。

```kimi
func process<length N>(values: [N of i32]) => ()
func keep<length N, T>(values: [N of T]) -> [N of T] => values@move
func work<length N>()
    let buffer: [N of u8] = [N of 0]

let a: [4 of i32] = [1, 2, 3, 4]
process([1, 2, 3]) // N = 3
process<3>(a)      // Error: length mismatch.
work<4>()          // A body-only length must be supplied.

func consume<T>(x: Array<T>) => ()
func consume<length N, T>(x: [N of T]) => ()
consume([1, 2, 3]) // Error: ambiguous.
```

<a id="s-4-4-2"></a>

### 4.4.2. 整形式の義務と正規化

シグネチャ内の各長さ式Eは、検査付き評価で非負isizeになる`ValidLength(E)`を要求する。非依存の失敗は宣言を拒否する。依存義務は公開された適用条件で、具体的呼出しの推論後・候補比較前に検査し、偽なら候補を除くが順位付けには使わない。選択後のレイアウト失敗で候補を選び直さない。

本体は公開条件を満たすすべての束縛について検査する。本体の長さ・呼出し先条件は、具体的評価、長さスロットの非負性、同じ正規化済み義務だけから導く。本体から条件を補わない。具体的レイアウトは実体化時の義務となる。

シグネチャ・依存型・義務では正規化済み式、具体的型では評価済み長さを比較する。定数として読める束縛を型付き初期化子へ再帰的に展開し、次を行う。

1. 括弧を除き、非依存部分を評価し、長さパラメーターをスロット位置へ置換する。
2. 整数の二項`+`・`*`の各オペランドを、ノード種別・型同一性・定数値・スロット位置で並べる。
3. 型・演算子と`-`・`/`・`%`の順序を保ち、結合替え・分配・相殺はしない。

したがって`N + (2 + 2)`と`4 + N`は一致するが、`(N + 2) + 2`と`N + 4`は一致するとは限らない。

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value

func f<length N>(a: [(N + 1) of u8])
    let k: isize = N + 1
    let b: [k of u8] = a // Valid: k normalizes to N + 1.
```

<a id="s-4-5"></a>

## 4.5. 配列の所有権

固定長配列は要素がCopyの場合だけCopyで、Ownedは構造から導き、要素のOrigin・Loanを保つ。部分Moveは[§15.1.3](#s-15-1-3)、途中の構築失敗は[§16.2.1](#s-16-2-1)に従う。破棄時は残る初期化済み要素を添字の降順で破棄する。

Array&lt;T>はNon-Copyで、表現可能なレイアウトを持つ完全な要素型を受け入れる。TのOriginと取得した要素のLoanを、削除・clear後も保持する。要素位置はOriginの変性を保つが、uniq/Array&lt;T>は不変となる。

所有配列の共有ビューは、範囲添字、slice、指定された読取り操作で明示的に作る。固定長配列はサイズ変更できず、Array更新には全体への排他アクセスが必要。Sliceから要素更新はできず、配列、排他要素Place、排他列挙、UniqSliceを使う。

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
let copy = values                     // Copy: i32 is Copy.
let middle: Slice<i32> = values[1..3] // Explicit shared view.
let all: Slice<i32> = values          // Error: no implicit conversion.
```

<a id="s-4-6"></a>

## 4.6. 位置と範囲

位置・範囲はKimiの型で、名前はキーワードではない。ただし`^`と範囲構文は常にKimiの型を作る。記憶のOrigin・Loanを保持しない。

<a id="s-4-6-1"></a>

### 4.6.1. 位置

位置は長さLの対象の境界`[0, L]`へ解決し、要素位置にはさらに`q < L`を要求する。閉じたContract Positionを満たすのは整数12種、`FromEnd<T>`、Start、Endだけで、PrimitiveIntegerはPositionを含意する。

L&lt;0ならすべて失敗する。そうでなければ切捨てのない数学的な値で判定する。

| 位置 | 成功条件 | 結果 |
| --- | --- | --- |
| 整数n | `0 <= n <= L` | n |
| `^x`によるFromEnd&lt;T>、TはPrimitiveInteger | `0 <= offset <= L` | `L - offset` |
| Start／End（省略境界） | 常に成功 | 0／L |

FromEndは読取り専用offset:Tを持ち、Start・Endはゼロサイズ。3型ともtryResolveと`resolve(self: Self, length: isize) -> isize`を持ち、公開コンストラクターはない。整数自体にメンバーは加わらず、tryResolveはPosition制約経由だけで呼べ、resolveはない。

構築では検査せず、`^(-1)`も値としては有効。FromEndはoffsetで等価比較し、Start同士・End同士はすべて等しい。順序、算術、変換はない。`^n + 1`ではなく`^(n + 1)`と書く。位置への参照は値読み取りし、Position制約の型引数は参照先型として推論する。

```kimi
public contract Position: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<isize>
```

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let last = ^1        // FromEnd<i32>
let a = values[last] // 40
let n: u8 = 1
let b = values[n]    // 20: every integer Type is a position.
let c = values[^0]   // Aborts if executed: ^0 is not an element position.
let d = ^1 + 1       // Error: FromEnd<i32> has no addition.
```

<a id="s-4-6-2"></a>

### 4.6.2. 範囲

範囲は格納・引渡し・列挙・添字で同じ意味を持つ。

| 構文 | 型 |
| --- | --- |
| `a..b`／`a..=b` | Range&lt;S,E>／ClosedRange&lt;S,E>（末尾を含まない／含む） |
| `a..` | Range&lt;S,End> |
| `..b`／`..=b` | Range&lt;Start,E>／ClosedRange&lt;Start,E> |
| `..` | Range&lt;Start,End> |

構文が形を決め、構築時に検査しないため逆順範囲も値として有効。両境界は独立したPosition型で、参照なら値読み取りする。リテラルだけでない境界は自身の既知型が必要。リテラルだけの境界（その条件を満たす前置^も含む）は、期待範囲型のS/E、相手境界の確定整数型（FromEndなら内側T）、既定i32の順に最初の根拠を使い、不一致でも次へ進まない。位置専用の既定型はない。

Range・ClosedRangeはCopy・Owned・Equatableで、読取り専用start:S・end:E、tryResolve・resolve・条件付き列挙を持つ。構築は範囲構文のみで、length・isEmptyはない。形の違う型同士は比較できない。

ResolvedRangeは`0 <= start <= end`を保つ別型で、同境界のRange&lt;isize,isize>と意味は同じだが相互変換しない。Copy・Owned・Equatableでlength・isEmptyも持つ。indicesか範囲解決だけが生成し、十分な長さの任意の対象へ使える。

```kimi
let a = 1..^1                 // Range<i32, FromEnd<i32>>
let c = 0@i32..2@i64          // Range<i32, i64>: resolvable, not iterable.
let n: i64 = 2
let d: Range<i64, u8> = n..10 // 10 is u8 from the expected Type.
let f = (0..3) == (0..=2)     // Error: different range Types.
```

<a id="s-4-6-3"></a>

### 4.6.3. 解決

PositionRangeは上記3範囲型だけが満たす。解決は境界と長さだけを読み、次の半開区間を返す。

| 型 | 成功条件・結果 |
| --- | --- |
| Range | 両境界をs・eへ解決し、s≤eなら`[s,e)` |
| ClosedRange | 始点sと要素位置としての終点qを解決し、s≤qなら`[s,q+1)` |
| ResolvedRange | end≤Lなら自身の区間 |

負のL、位置解決失敗、逆順は失敗し、逆順を空区間に丸めない。整数以外の位置・範囲型はtryResolveの対としてresolveを持つ。添字、slice・sliceUniq、resolve、insert・remove・swapRemove・swap等のAbortする操作は共通解決を使い、Noneなら`KIMI_E_INDEX_BOUNDS`でAbortする。try付き操作がNoneにするのは自身の境界失敗だけ。

```kimi
public contract PositionRange: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>
```

```kimi
(1..=3).tryResolve(5) // Some(1..4)
(2..=1).tryResolve(5) // None: reversed.
(..=^0).tryResolve(5) // None: ^0 is not an element position.
(^7..).tryResolve(5)  // None: 7 > 5.
```

<a id="s-4-6-4"></a>

### 4.6.4. 範囲の列挙

Range&lt;S,E>・ClosedRange&lt;S,E>は`S is PrimitiveInteger`かつ`E is S`の場合だけIterable・UniqIterable・IntoIterableに適合し、RangeIterator&lt;S>・ClosedRangeIterator&lt;S>を使う。ResolvedRangeは常に適合しRangeIterator&lt;isize>を使う。省略境界、末尾相対境界、異なる整数型の境界は列挙できない。

列挙開始時に境界をCopyし、範囲のLoanを保持しない。借用入口のIteratorType(source)もsourceに依存しない。start>endは入口でAbortし、空・降順とは扱わない。1ずつ増加し、閉区間は型の最大値でもendの次を計算せず終了する。無限・降順・刻み幅付き範囲は延期されている。

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]
for i in 0..values.length               // Range<isize, isize>
    Console.writeLine("\(values[i])")
for x in 0..=255@u8                     // Stops after 255.
    ()
for i in (1..^1).resolve(values.length) // 1, 2, 3
    ()
for i in 1..^1                          // Error: a FromEnd boundary is not iterable.
    ()
```

<a id="s-4-7"></a>

## 4.7. 添字

固定長配列・Array・Slice・UniqSliceは要素添字と範囲添字を持つ。rawポインターは境界検査なしのisizeオフセットだけを取る。

<a id="s-4-7-1"></a>

### 4.7.1. 添字式の解決

利用者型・Dictionaryも含め、単一要素の添字は次のContractで解決する。キーは共有借用し消費しない。参照・ハンドル型のキーを渡すなら`key@ref`と書く。

同じキーでは両要件が同じ要素を返し、indexUniqは格納型を変えず排他アクセスを与える。静的解決は次の順で行う。

1. 参照経路選択後、レシーバ型のIndexable&lt;Key>適合を集める。具体的シーケンスでは位置を型付けして正規化する。それ以外は各key:ref/Keyへ共通Adaptationで合わせ、リテラルは既定型決定前に合わせる。合う適合はちょうど1個必要で、0個は型エラー、複数は曖昧。リテラル既定型や期待結果から適合・Keyを選ばない。
2. 裸の取得、期待型へのAdaptation、書込み、借用、Subject、レシーバ式という用途から必要能力を決める。
3. 読み取り・共有借用ならindex、更新・排他借用・排他再借用なら同じ適合のindexUniqを選ぶ。

添字式は返された参照の参照先Placeを指し、格納型はElement、能力・依存はその参照に従い、Takeは持たない。明示的な`x.index(k)`は通常の関数呼出しで参照値を返すだけである。

indexUniqはレシーバを`@uniq`なしで排他的に取得するが、元の経路の能力を増やさない。失敗してもモード・候補を選び直さない。呼出し中はレシーバとキーをLoanし、要素の依存は返された参照の依存であってキーや参照を保持する一時値ではない。Element内のOrigin・Loanも保つ。

具体的シーケンスの`x[k]`では、kを先に読んで要素位置qに解決し、Indexable&lt;isize>経由の`x[q]`を境界検査1回で行う。キーの借用は残らない。ジェネリックのIndexable&lt;Key>にはこの正規化を追加せず、Indexable&lt;isize>制約だけでは`^1`を使えない。

| 対象 | 要素アクセス | 範囲結果・依存 |
| --- | --- | --- |
| 固定長配列・Array・UniqSlice | ref/Eまたはuniq/Eの参照先、UniqIndexable&lt;isize> | Slice&lt;E>、レシーバ借用に依存 |
| Slice | ref/E during sourceの参照先、Indexable&lt;isize> | Slice&lt;E>、sourceに依存 |
| Dictionary&lt;K,V> | ref/Vまたはuniq/Vの参照先、UniqIndexable&lt;K>。不在はAbort | 範囲なし。レシーバに依存しキーには依存しない |

範囲添字はIndexable系列の一部ではなく、範囲全体への代入もない。直接の固定長配列要素は組込み射影で、配列のCopyや全体借用を行わない。Array要素アクセスはindex/indexUniqで全体を借用する。

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
```

```kimi
var names: Dictionary<i64, string> = [:]
_ = names.tryInsert(1, "one")
names[1] = "uno"   // 1 fits the only Key, i64.
let id: i32 = 1
names[id] = "eins" // Error: an i32 key fits no conformance.
let lent = names.index(1) // ref/string: a written call returns the reference.
// names.indexUniq(1) = "x" // Error: the target is the reference, not its referent.
// For a Type conforming to Indexable<i32> and Indexable<i64>,
// pair[1] is ambiguous; write pair[1@i64].
```

<a id="s-4-7-2"></a>

### 4.7.2. 要素Place

添字でPlaceを作ってもCopyは起こらず、`matrix[1][2] = 10`も内側配列をCopyしない。代入は初期化・置換、@ref・@uniqは要素の借用となる。@moveで取り出せるのは所有固定長配列の静的Move Pathだけで、範囲内の整数リテラル添字だけがその経路を作る。

裸の読み取りは、Copy要素ならCopy、格納uniq・objuniqならindexUniq経由の再借用。他のNon-Copy要素は借用が必要で、暗黙にMoveしない。固定期待ref/Tは所有要素を暗黙に借用するが、参照・ハンドルを格納する要素にはその値のAdaptationを適用する。

```kimi
// resources: a complete owned [2 of Resource]; i: isize; refs: a writable Array<uniq/Node>.
inspect(resources[i]) // Shared borrow at a ref/Resource parameter.
normalize(refs[0])    // indexUniq; the stored reference is Reborrowed.
```

<a id="s-4-7-3"></a>

### 4.7.3. 長さのメタデータ

固定長配列・Array・Slice・UniqSliceは読取り専用`length: isize`と`indices: ResolvedRange`を持ち、固定長配列以外は`isEmpty: bool`も持つ。要素を読まず、結果にLoanを残さないが、固定長配列もレシーバアクセスを検査する。indicesは取得時の`(..).resolve(x.length)`で、その後のサイズ変更を反映しない。

```kimi
var values: Array<i32> = [1, 2, 3]
let range = values.indices // 0..3
values.append(4)           // range is still 0..3.
```

<a id="s-4-7-4"></a>

### 4.7.4. 評価・失敗・更新

レシーバと添字は評価順に各1回、範囲境界は始点から評価する。既存Placeはその場で使い、所有一時値だけを実体化する。添字評価中はレシーバ記憶を読めるが、更新・Move・破棄・再割当てできない。連鎖した要素Copyは、外側の後続オペランドより先に完了する。Sliceハンドルは先にCopyし、UniqSliceハンドルは借用する。

不正な要素・範囲添字はAbort、tryGet・trySlice・trySplitAtはNone。`[L..L]`・`[^0..]`は空だが、要素L・^0、閉区間の終点^0は不正。範囲外であることだけではコンパイルエラーにしない。

代入は右辺を確保してから対象を探す。複合代入・増減はその後、境界検査・読み取り・計算・書戻しを各1回行う。要素書込みの排他Loanは境界解決から配置完了まで続く。Intrinsics.swapには静的な非重複が必要。

```kimi
let values: [3 of i32] = [1, 2, 3]
let r = (-1)..2               // Valid: construction checks nothing.
let s = values[r]             // Aborts in resolution.
let t = values.trySlice(2..5) // None.
```

<a id="s-4-8"></a>

## 4.8. スライスとビュー

<a id="s-4-8-1"></a>

### 4.8.1. Slice

Slice&lt;T>{source}は初期化済みの連続領域へのCopyビューで、長さは型の一部ではない。範囲アクセスは必ずSliceを返し、添字は0から、末尾相対位置・再スライスは自身の長さで解決する。入れ子配列の行はSlice&lt;[N of T]>であり平坦化せず、異なる要素型間の変換もない。

Originは基底記憶の寿命、Loanは競合Placeを記録する。Copy・再スライス・イテレーター・要素参照は両方を保ち、ハンドル変数自体は借りない。配列からのビューは空でも、再スライス後も元配列全体をLoanし、競合する更新・Move・破棄・再割当てを禁止する。

要素を所有せず、TはOwnedでなくてもよい。sourceは短縮だけ可能で、OwnedはOwnedOriginsから導く。通常の格納規則を使い、静的記憶にはOwnedの場合だけ格納できる。元記憶や一時値の寿命は延びない。varのSliceはハンドルの再代入だけを許し、uniq/Slice&lt;T>もハンドルだけを借りる。等価比較はない。

```kimi
var values: Array<i32> = [1, 2, 3]
let view = values[0..2]
let moved = values@move     // Error: values is Loaned by view.
let first = view[0]

func makeArray() -> Array<i32> => [1, 2, 3]
inspect(makeArray()[..])    // The temporary lives through the call.
let escaped = makeArray()[..]
inspect(escaped)            // Error: the temporary has expired.
```

<a id="s-4-8-2"></a>

### 4.8.2. Sliceの操作

Sliceメンバーはself:Selfでハンドルを値渡しし、借用結果はsourceを保つ。

| 操作 | 契約 |
| --- | --- |
| `toArray(self: Self) -> Array<T>`、T is Copy | 各要素を順に1回Copyして新しい記憶を作る |
| `tryGet<P>(self: Self, index: P) -> Option<ref/T during source>`、P is Position | 要素位置でなければNone |
| `trySlice<R>(self: Self, range: R) -> Option<Slice<T> during source>`、R is PositionRange | 範囲解決失敗ならNone |
| `splitAt<P>(self: Self, index: P) -> (Slice<T> during source, Slice<T> during source)`、P is Position | 位置を1回pへ解決し、`s[..p]`と`s[p..]`を返す |
| `trySplitAt<P>(self: Self, index: P) -> Option<(Slice<T> during source, Slice<T> during source)>` | 同じ分割、失敗ならNone |
| `contains(self: Self, value: ref/T) -> bool`、T is Equatable | 等しい要素の有無 |
| `firstIndex(self: Self, of: ref/T) -> Option<isize>`、T is Equatable | 最初に等しい要素の添字 |
| `firstIndexWhere<F>(self: Self, predicate: F) -> Option<isize>`、F is Callable&lt;(ref/T) -> bool> | 最初に述語がtrueとなる添字 |

検索は添字順に共有参照を渡し、最初の一致で止まる。tryGetは常に参照を返す。固定長配列・ArrayもtryGet・trySlice・splitAt・trySplitAtをx[..]同様に提供し、結果はduring self。Slice要素は共有専用。toArrayは全要素を読み、割当ては最大1回、空なら0回。T内部の依存は残すが外側借用は残さない。

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let s = values[..]
let a = s.firstIndex(of: 30)                          // Some(2)
let b = s.firstIndexWhere(func (value) => value > 15) // Some(1)
let parts = s.splitAt(^1)                             // (10, 20, 30), (40)
s[0] = 5                                              // Error: Slice elements are read-only.

func first<T>(s: Slice<T>) -> ref/T during s.source
    return s[0]@ref // Valid for every T; a by-value `let x = s[0]` needs T is Copy.
```

<a id="s-4-8-3"></a>

### 4.8.3. Sliceの列挙

SliceはIterable・UniqIterable・IntoIterableに適合し、どのモードも添字順の`ref/T during source`を返す。要素はイテレーターでなく基底スロットを借り、nextをまたいで保持できる。要素内部に格納された参照のOriginは、スロット借用とは別に保つ。

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..]
let value = s[0]       // Copies the inner reference to data.
for element in s
    ()                 // element: ref/(ref/i32), borrowing the slot of refs.
```

<a id="s-4-8-4"></a>

### 4.8.4. ビューとUniqSlice

slice・sliceUniqには全体版と`R is PositionRange`を取る範囲版があり、範囲不正はAbortする。

| 提供型 | 共有ビュー | 排他ビュー |
| --- | --- | --- |
| 固定長配列・Array | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |
| Slice&lt;T>{source} | `slice(self: Self) -> Slice<T> during source` | なし |
| UniqSlice&lt;T>{source} | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |

`x[r]`はx.slice(r)で、範囲添字は常に共有Sliceとなる。

UniqSliceは初期化済み連続領域のNon-Copy排他借用で、要素を所有せず、TにCopy・Ownedを要求しない。メタデータ、index・indexUniq、ビュー操作を持つ。要素の読み取り・置換・借用はできるがMove取出しはできない。排他操作にはvar所有者または排他参照が必要。

暗黙Copy・Move・再借用・変換はなく、`view@move`、view.sliceUniq、view.sliceを明示する。列挙・toArrayはなく、Tは不変。子ビューは親の現在の借用に依存し、共有子が生きる間は書込みできない。異なる範囲を指定しても非重複の証明にはならない。

```kimi
var values: [3 of i32] = [1, 2, 3]
values.sliceUniq(0..3)[0] = 10
var view = values.sliceUniq()
let reference = view@uniq
reference[0] = 20     // The referent stays exclusively accessible.
values[0..3][0] = 40  // Error: range subscripting forms a shared view.
```

<a id="s-4-8-5"></a>

### 4.8.5. 重ならない排他要素ペア

Array&lt;E>と固定長配列は次を提供する。

片方が要素位置でないか、両方が同じ論理要素ならNone。それ以外は引数順に重ならない排他参照2つを返す。O(1)、無変更・無割当て・利用者コードなし。別々に形成した排他要素借用には、引き続き静的非重複の証明が必要。

```kimi
var values: Array<i32> = [1, 2, 3]
match values.tryGetPairUniq(first: 0, second: ^1)
    .Some(let pair) => exchange(pair.0, pair.1)
    .None => ()
let i: isize = 0
let a = values[i]@uniq
let b = values[1]@uniq // Error: no static non-overlap.
inspect(a)
```

<a id="s-4-8-6"></a>

### 4.8.6. 計算量

位置・範囲の構築・解決・Copy、ビューと要素Placeの形成・Copy・再スライス・分割はO(1)で、割当て・参照計数更新を行わない。要素間隔はstride(T)。

標準の配列・Slice・範囲・Dictionaryイテレーターでは、開始時の生きた要素数をnとする（空き容量は除外）。作成・破棄・各nextはO(1)、Dictionaryでは償却O(1)、全列挙はO(1+n)。管理領域はnに対してO(1)で、所有する要素記憶は別に数える。所有固定長配列はN×stride(E)バイト、動的コレクションの所有イテレーターは既存記憶を保持する。イテレーター操作で新規ヒープ割当てはしない。インライン配列の移動を含む要素移動・破棄コストは別計上する。

<a id="s-4-9"></a>

## 4.9. 動的コレクション

<a id="s-4-9-1"></a>

### 4.9.1. 共通規則

Array・DictionaryはNon-Copyの所有コレクション。Dictionaryは完全な格納型を受け入れ、K is Equatableと[§12.4.3](#s-12-4-3)のキー安定性を要求するが、公開Hash制約はない。

更新のselfはuniq/Self。書込み可能な貸出し点のレシーバ式なら@uniqなしで排他取得するが、通常のuniq引数には@uniqが必要。削除は格納責任を移転する。前提違反はAbort、不在はOption、回復可能な拒否は入力を含むResultで返す。

追加は既存順序を保って必要なら容量を増やし、置換は値だけを変える。削除は残る順序と容量、clearは容量を保つ。検索・拒否は変更せず、reserve・shrinkは値・順序・長さを保つ。引数・等価比較・デストラクターの効果は巻き戻さない。

```kimi
var values: Array<i32> = []
values.append(10)       // Exclusive receiver, no @uniq.
grow(values@uniq)       // A uniq parameter needs @uniq.
```

<a id="s-4-9-2"></a>

### 4.9.2. 命名の慣例

同名関数はレシーバ形とパラメーター取得モードを共有し、アクセサーのレシーバも固定されるため、異なる取得形には別名が必要。Kimiと仕様例では次の組を使う。

| 関係 | 命名 |
| --- | --- |
| 新しい値／その場で更新 | 過去分詞／動詞：sorted／sort |
| 共有／排他参照 | 排他側にUniq：tryGet／tryGetUniq |
| 観察／前進・取出し | 別動詞。プロパティに対しては動詞＋名詞：reading／nextReading |
| 観察／消費して取出し | 名詞プロパティ／into＋名詞：item／intoItem |

<a id="s-4-9-3"></a>

### 4.9.3. Arrayの操作

位置引数は値渡しし、入口時の長さで1回解決する。表のP・QはPosition。

| メンバー | 動作 |
| --- | --- |
| `init(! capacity: isize)` | 空を作る。負数はAbort |
| `init(! repeating: T, count: isize)`、T is Copy | repeatingを0個の場合も1回取得し、count個Copyする。負数は配置前にAbort。割当ては最大1回、0個なら0回 |
| `append(value: T)`／`pop() -> Option<T>` | 末尾へ追加／末尾から取出し |
| `insert<P>(index: P, value: T)` | 境界へ挿入。^0なら末尾 |
| `remove<P>(index: P) -> T`／`swapRemove<P>(index: P) -> T` | 順序を保って削除／末尾要素を穴へ移して削除 |
| `swap<P,Q>(first: P, second: Q)` | 2要素を交換。同一要素なら無変更、不正位置はAbort |
| `truncate(length: isize)`／`clear()` | 指定位置以降／全要素を破棄。大きい長さは無変更、負数はAbort |
| `appendAll(other: Array<T>)` | otherの全要素を末尾へMove |
| `appendCopies(values: Slice<T>)`、T is Copy | 全要素を末尾へCopy |
| `removeAll<F>(matching: F)`、F is Callable&lt;(ref/T) -> bool> | 添字順に各要素へ1回述語を呼び、一致要素を除く。残る順序を保つ |
| `reverse()` | 逆順にする |
| `sort()`、T is Comparable／`sort<F>(by: F)`、F is Callable&lt;(ref/T, ref/T) -> i32> | compare／byによる非減少順。不安定ソート。比較結果は負・0・正で大小を表す |
| `first`・`last: Option<ref/T during self>` | 共有レシーバ。空ならNone |
| `tryGetUniq<P>(index: P) -> Option<uniq/T during self>` | 要素位置でなければNone |

内部の要素移動はCopy・デストラクター呼出しを行わない。removeAll・sortのコールバックは配列を観察できず、コールバックがAbortしても各要素はちょうど1回初期化された状態を保つ。比較が矛盾する場合の順列は未規定。

```kimi
var values = Array<i32>.init(capacity: 4)
values.appendCopies([30, 10, 20][..])
values.sort()                                  // [10, 20, 30]
let removed = values.swapRemove(0)             // 10; values is [30, 20]
values.removeAll(matching: func (v) => v > 25) // [20]
values.append(values[0])                       // The Copy precedes receiver activation.
let bad = values.remove(^0)                    // Aborts: ^0 is not an element position.
```

<a id="s-4-9-4"></a>

### 4.9.4. Dictionaryの操作

検索は[§12.4.3](#s-12-4-3)の等価性対応を使い、挿入順序を変えない。

| 操作 | キー不在 | 等しいキーが存在 |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K,V)>` | 末尾へ追加、Ok(()) | 入力をErr((key,value))で返す |
| `insertOrReplace(key: K, value: V) -> Option<V>` | 末尾へ追加、None | 格納キーを保ち、旧値をSomeで返す |
| `remove(key: ref/K) -> Option<(K,V)>` | None | 格納エントリーを返す |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | None | 値への共有参照 |

検索前に両入力を取得し、insertOrReplaceは格納後に未使用の入力キーを破棄する。格納キーは更新しない。tryGetは値をCopyせず、結果はDictionary全体をLoanしキーには依存しない。再追加したキーは末尾に置く。clearは全エントリーを破棄する。

`map[key]`は既存値のPlaceで、不在ならAbortし、代入でも追加しない。代入は右辺取得、レシーバ・キー評価、共有検索、旧値破棄、新値配置の順。全過程でDictionaryの構造変更・Move・破棄を禁止し、キーを含む全Loanを配置前に検査する。旧値の破棄が右辺依存を壊すなら拒否する。複合代入の検索は1回。

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement"      // Replacement, not insertion.
names[2] = "two"              // Aborts if executed: key 2 is absent.
```

<a id="s-4-9-5"></a>

### 4.9.5. 容量とメモリー割当て

両コレクションは読取り専用capacity:isize（割当てなしで格納できる要素・エントリー数）を持ち、`0 <= length <= capacity`を保つ。型付きの空`[]`・`[:]`は割当てなし。init(capacity:c)は空構築後のreserve(additional:c)に相当する。

reserveは位置引数でも呼べ、負数・オーバーフローはAbort。`capacity >= length + additional`を保証し縮小せず、足りていれば割当て・要素移動をしない。容量内の追加、検索、置換、削除、clear、拒否は割当てを行わない。増量時の割当て失敗はAbortし得る。具体的容量は実装定義。

shrinkToFitは容量削減のため割当てを行え、結果容量はlength以上・旧capacity以下。値・順序・長さを保つ。サイズ計算・割当て失敗時も正常に戻り、容量・要素配置も保つ。厳密な容量一致やOSへの返却は保証しない。

<a id="s-4-9-6"></a>

### 4.9.6. Loan・依存・呼出し効果

排他レシーバは後続引数より前に予約し、入口で有効化する。その時点で生きた要素・SliceのLoanは競合する。追加・置換入力の依存は、tryInsertがErrを返す場合もコレクションへ合流する。削除値は格納型の全依存を持ち、どの操作も依存を差し引かない。tryGet結果には記憶のLoanを加える。削除したuniq/Tも、Loanの生存解析が許すまでは残るArray&lt;uniq/T>から独立しない。

| 操作 | 内部の利用者コード効果 |
| --- | --- |
| Arrayのclear・truncate・破棄、Array／固定長配列の所有イテレーター破棄、添字置換 | 対象Tの破棄 |
| Array.removeAll | matching呼出し、除いたTの破棄 |
| Array.sort／sort(by:) | compare／by呼出し |
| Slice.contains・firstIndex／firstIndexWhere | Tの等価比較／predicate呼出し |
| DictionaryのtryGet・tryInsert・remove・index・indexUniq | Kの等価比較 |
| Dictionary.insertOrReplace | Kの等価比較、未使用入力Kの破棄 |
| Dictionaryの添字置換 | Kの等価比較、旧Vの破棄 |
| Dictionaryのclear・破棄・所有イテレーター破棄 | V・Kの破棄 |
| [§4.6](#s-4-6)〜4.9のその他の操作 | なし |

この表と、依存規則、割当て、Abort、入力経由アクセスが完全な公開効果である。呼出し側はライブラリー本体でなくこの契約を使い、実装は契約を超えない。Callable引数の効果は実引数を通じて合成する。具体的実体化では表を直接用いる。型依存の破棄は[§15.6.4](#s-15-6-4)で拡大しない限り呼出し側でLoan検査へ合成する。効果境界検証では定義自身の束縛を代入し、なお抽象型に依存する破棄は分類不能。ジェネリック本体内のLoanに対しては[§16.3.3](#s-16-3-3)に従って検査する。

```kimi
var values: Array<i32> = [3, 1, 2]
let view = values[..]
values.sort()          // Error: view's Loan is live at activation.
let first = view[0]
```

<a id="s-4-9-7"></a>

### 4.9.7. 失敗と破棄順序

結果決定前に境界解決・検索を行う。不在・拒否は無変更で、サイズ・割当て検査は追加時だけ。再配置は利用者コードを呼ばない。引数評価中の制御転送は呼出しを飛ばす。

Arrayのtruncate・clear・破棄は添字降順。removeAllは最後の述語呼出し後に、未規定の順序で除去要素を破棄する。Dictionaryのclear・破棄は挿入逆順で各値をキーより先に破棄する。所有イテレーターの残り要素も同じ順序。

```kimi
var log: Array<Logged> = [Logged.init(1), Logged.init(2), Logged.init(3)]
log.truncate(1) // Destroys index 2, then index 1 (Logged 3, then Logged 2).
```

<a id="s-4-9-8"></a>

### 4.9.8. 計算量

nを長さ、cを容量、dを後始末が必要な破棄要素数とし、利用者コードのコストは除く。

| 操作 | 上限 |
| --- | --- |
| Array.append／pop | 償却O(1)／O(1) |
| Array.swap・swapRemove・first・last・tryGet・tryGetUniq | O(1) |
| Array.truncate・clear | O(1+d) |
| Array.appendAll・appendCopies・init(repeating:count:) | 要素ごとに償却O(1) |
| Array.insert・remove・removeAll・reverse、Sliceの検索 | O(1+n) |
| Array.sort | 最悪O(n log n)、割当てなし |
| Dictionaryの検索／更新 | 検索コスト＋O(1)／償却O(1) |
| Dictionary.clear | O(1+c+d) |

shrinkToFitを挟まないm操作の総増量コストはO(m+C)。Cは最大の容量・長さ・reserve要求であり、reserve(additional:1)とappendの反復もO(n)。Dictionary検索には等価比較・ハッシュ・探索のコストを含み、線形検索も許す。

<a id="s-5"></a>

# 5. 生ポインターとunsafeメモリー操作

unsafeは検証を作者へ委ねるもので、データ競合を含む規則を免除しない。unsafeブロック、unsafe関数、外部関数はコンパイラーが検証できない約束を示し、違反は未定義動作となる。安全なコードだけでは未定義動作は生じない。

`raw/T`は完全な型Tの記憶を指す非所有の生ポインター。T自身もポインター・参照にでき、どのTでもCopyで、Copy・破棄は参照先を触らない。`raw/T during o`で対象記憶のOriginを示せる。

unsafe文脈が必要なのは、raw Placeの形成（`*p`・`p[n]`）、ポインター算術、unsafe関数呼出し、noinit、raw Originの主張だけ。文脈外ではコンパイルエラー。それ以外のnull、等価比較、@raw、raw・usize・nullからの@raw/U、rawからの@usizeにはunsafeも有効なprovenanceも不要。ただし整数からポインターへの変換は環境への効果となる。

```kimi
let bytes = pointer@raw/u8       // No unsafe context.
unsafe
    let value = bytes[13]        // Forming a raw Place needs one.
let first = bytes[0]             // Error: raw Place outside an unsafe context.
```

<a id="s-5-1"></a>

## 5.1. nullと等価性

nullには期待raw型が必要で、または`null@raw/U`で型付きnullを作る。安全な参照はnullにならず、非nullのrawでも有効とは限らない。

`==`・`!=`は同じ型のポインター同士、またはポインターとnullのアドレスを比較する。参照先を読まず、null・ダングリングでも比較できる。nullはnullとのみ等しい。アドレス一致はprovenance・権限の一致を意味しない。ポインターの順序比較はない。

```kimi
let pointer: raw/i32 = null
let empty = pointer == null
let typed = null@raw/i32              // Typed Null Formation, not a cast.
let unknown = null                    // Error: no pointer Type.
let before = pointer < pointer        // Error: pointers have no ordering.
```

<a id="s-5-2"></a>

## 5.2. raw Place

provenanceはポインターの由来となる記憶と、アクセスできる範囲を表す。通常のPlaceはそのバイトが範囲内なら、ゼロサイズPlaceはアドレスが範囲内または末尾なら範囲内にある。provenance内にあるには、その範囲内で、同じ記憶に属する必要がある。型を読み替えるPlaceは覆うバイトの記憶に属する。

`*p`、`p[n]`とそのフィールド・要素はT型のraw Place。nullは逆参照できない。操作対象となるraw Placeは、そのアドレス・provenance内にある1つのPlaceを指す。型はOriginを等しいとみなす正規化同一性で一致しなければならない。フィールド・要素射影の基底自体はPlaceを指定しない。同型の別々のゼロサイズPlaceが同じアドレスにある場合はどれを指してもよく、アドレス・添字・フィールド名では区別できない。

読み書きは通常のCopy・代入規則に従い、全体の読み取り・置換・Take・借用には完全な値が必要。rawポインターはLoan、排他性、完全性の証拠を与えない。

**配置。** raw経由でPlaceを初期化できるのは、操作契約が定める構築または再配置だけ。操作契約は外部インポートの`- safety:`条件、または本体なしで実装が提供するKimi関数の契約である。本体を持つ関数は、実際に行う操作を通じてだけ初期化する。Uninitialized・Movedは値を持たない状態とする。

初期化は[§5.2.1](#s-5-2-1)を満たす書込みであり、既存Place、または生の記憶領域内の新しいPlaceへ行う。そのPlaceは値を持たず、祖先を除くInitialized Placeがそのバイトを保持していてはならない。noinit配列は既にInitializedである。

**指示先の整合性。** 集合の各操作よりhappens-beforeにある全操作も含む集合を閉じた集合とする。各raw Placeの指示先、各配置先、各再配置で移す値を一貫して選び、安全・unsafeの全操作が規則を満たし、そのPlace上にデータ競合がなければ整合する。Xを含み、Xよりhappens-afterの操作を含まない閉集合が不整合で、Xを除けば整合するなら、Xから未定義動作となる。この選択は実行の一部ではなく、動作が定義されるかを判定するだけで、実際の操作はアドレスと型に従う。

**型の読み替え。** 正サイズのCopy型のraw Placeは、読み取り・書込み・借用・Raw.sliceの要素アクセス時に、同アドレス・provenance内の読み替えPlaceを指せる。対象バイトはすべてInitialized Placeが保持し、値のない部分を含んではならない。読み替えPlaceはその操作または借用Loanの間だけ存在し、完全な値を持つ。配置・再配置・記憶領域の解放はその値とバイトを無視するが、Loanは競合検査に数える。

読み取りには、その型の有効な値を表すバイトが必要。パディングやenumの現在のCaseが使わないバイトは値を表さない。読み替え経由の書込み後、変更バイトを保持する各Initialized Placeを次に読み替え以外で触るまでに、その型の有効な値へ戻す。Non-Copy enumなら現在のCaseも変えてはならず、読み替えではペイロードを構築できない。

**再配置。** 操作契約が元範囲のバイトを生の記憶領域へコピーすると定める場合、完了時に、元範囲と元ポインターのprovenance内にPlace全体がある値を再配置できる。構成要素は所属値と一緒の場合だけ移動し、値全体が範囲に収まらない場合などは残る。移す値は構成要素と状態を保って宛先の同オフセットへ移り、元Placeは値を失う。宛先は、移す元を空と数えた配置規則を満たし、元にはTakeと同じ権限・Loan条件を適用する。

<a id="s-5-2-1"></a>

### 5.2.1. アクセス条件

raw Placeの読み取り・書込み・Take・借用は、次の全条件を満たさなければ未定義動作となる。

1. **provenance。** 対象はポインターのprovenance内にある。P@rawならPの記憶、Raw.allocateなら割当て全体、それ以外は整数変換の実装保証または生成操作の契約が範囲を定める。x[0]@rawは、同じアドレスでもx[1]に届かない。
2. **生存と値。** 記憶は生きていて、ゼロサイズTでもTに整列する。読むなら有効な初期化済み値が必要。内容の置換・Move・Takeは内容の寿命だけを終え、スロットの記憶は残る。ただし旧内容が所有したバッファー等は旧内容とともに終わる。
3. **権限。** アドレス取得元の経路がそのPlaceに持つ能力を超えない。読み替えPlaceでは、そのバイトを保持する各Placeの能力を守る。読み取りには読取り能力、書込み・Take・その場の破棄には書込み能力が必要。
4. **Loan。** 重なるPlaceの生きたLoanと競合しない。ただしポインター取得元の経路のLoanは除く。生存解析にはrawを含む値が到達する保持先も使う。
5. **到達範囲。** Loanを運ぶ値を読み書きする場合、ポインターが到達する保持先か、本体が所有しない記憶で行う。Originを示さない呼出し結果のフィールドに隠れたポインターや整数から復元したポインターなど、保持先を追跡できないものを、本体自身の保持先にあるLoan付き値の読み書きへ使ってはならない。

権限について、let、ref参照先、不変静的記憶、let束縛のobj、またはrc・arc・objrefから届くペイロードは読取り専用。書込み可能な所有経路と可変静的記憶は読み書き可能。uniq・objuniqの権限はLoanが生きる間だけ有効。Raw.allocateは読み書き可能で、整数変換・外部関数等は各保証が権限を定める。すべてのポインターに次の制限もある。

- 共有層、または共有Loanだけを保つOriginのrawを通じてPlaceから読んだポインターは書込み権限を持たない。そのCopy、オフセット、raw型変換、Originを落とす型合わせにも制限が残る。
- Origin付きポインターは、そのOriginを束縛した場所で定まる権限を超えない。宣言されたraw層を読み、その宣言上のOriginが共有Loanだけを保つなら、派生ポインターも書込み不能。フィールド・CaseではLoan要件refのスロット、関数・クロージャー・アクセサーのシグネチャ（関数型は除く）では、そのOriginを示す全入力借用が各許容場合でref・objref・Sliceで、結び付く全スロットがrefの場合に該当する。シグネチャが束縛するOriginを本体の借用で再判定せず、ローカル借用固有のOriginだけはその借用で判定する。
- 所有する記憶をたどる段階（own step）を通じてオブジェクトペイロードやduring selfのraw記憶へ作ったポインターの権限は、所有値が取得時の保持先に留まる間だけ。所有値のMoveで終わり、during selfフィールドを読み直したものは現在の保持先に届く。該当ポインターを運ぶ値がまだ使われ得る間に、所有値または逆参照を挟まない祖先のMove・Take・置換・破棄・排他借用があり得る場合はWarningを要求する。exchange・swap・replace等へuniqで渡す場合も含み、Placeはアクセス経路で比較する。

ローカルの記憶は宣言からスコープ末まで固定アドレスにある。全体・部分のMoveやraw経由のTakeは内容だけを空にし、var代入・Raw.initializeは同じPlaceを再初期化する。したがってスロットへのrawは、残る条件を守ればスコープ末まで使える。オブジェクトペイロードのアドレスは公開から破棄開始まで固定で、ハンドルの移動に影響されない。ただし上記の権限は別に守る。

生の記憶へ移した値もOrigin・Loan要件を保ち、Loanは記憶を追う。during selfの記憶はそのフィールドを持つ値の一部で、型は参照先を格納内容として数える。during oの記憶はoのLoanが覆い、その他はポインターの到達先となる。入力経由のraw記憶へ関数が格納できるのは、公開契約がそこへ保持できるとする値だけ。

```kimi
func h() -> ()
    var x: i32 = 0
    let r = x@ref
    let p = r@follow@raw          // raw/i32 during the Origin of r's shared borrow.
    unsafe => *p = 1              // Undefined behavior: that Origin keeps only shared Loans.
```

```kimi
var a = make()
let p = a@raw
consume(a@move)
a = make()
unsafe => inspect((*p)@ref)   // Valid: the same Place, initialized again.
```

<a id="s-5-2-2"></a>

### 5.2.2. raw Placeの借用

@ref・@uniq・@objref・@objuniqでraw Placeを借りられる。結果の参照先は新しいLoanの新しい基点となり、元の所有者・Loanは復元しない。Originは期待型・格納先に合わせ、staticにもできるが、元rawがOriginを示すならその範囲内に限る。結果はポインターの到達先も保持する。安全なコードでの「uniq Originをstaticへ束縛できない」という制限はここには適用しない。

この基点は、そこから派生しないPlaceとは静的に比較しない。結果またはそのOriginを保つ値を使う間、借用アクセスについて[§5.2.1](#s-5-2-1)を満たす。uniq・objuniqの排他性は作者の約束だけに依存し、他のrawを含む別経路から重なるPlaceへアクセスしてはならない。

<a id="s-5-2-3"></a>

### 5.2.3. raw Placeからの取出し

raw Placeは状態追跡なしでTakeを提供する。`(*p)@move`は値を取り出してPlaceを空にし、記憶は残る。Raw.initializeで再配置できる。以後の読み取りや、元所有者を含む二重破棄を防ぐ責任は作者にある。`_ = (*p)@move`はその場で値を破棄する。裸の取得は通常の[§3.5](#s-3-5)に従う。

<a id="s-5-3"></a>

## 5.3. ポインター算術と添字

p:raw/T、n:isizeに対し、p+n・p−nはn×stride(T)バイトずらし、p[n]は`*(p+n)`を表す。+=・−=は複合代入。これ以外のポインター算術はない。

strideはパディングを含む要素間隔。raw/()などで0なら変位は常に0で、ジェネリックraw/Tにサイズ制約は不要。ゼロサイズ要素の列はポインター一致で終端を判定せず個数で走査する。

0変位はnullを含め元を保つ。非0変位には生きた記憶が必要で、元・結果ともprovenanceの範囲内または末尾直後に置く。型・Origin・provenanceを保つ。isizeのオーバーフロー、アドレスの循環は未定義動作。添字の境界検査は行わない。

```kimi
unsafe
    pointer[-1] = 123
    let next = 1 + pointer       // Error: only p + n and p - n exist.
```

<a id="s-5-4"></a>

## 5.4. アドレスと変換

| 形式 | 意味 |
| --- | --- |
| P@raw | Pの格納型Vへのraw/V。Pへ到達する最後の借用・rawがあれば、その外側OriginまたはrawのOriginを示す |
| E@raw/U | raw・usize・nullからraw/Uへの変換 |
| E@(raw/U during o) | oを示す同変換。元Originまたはnullがoに合えば型合わせ、それ以外はunsafeが必要な主張 |
| E@usize | rawの数値アドレス |

型なし@rawは書いたスロットのアドレスを取る。p:raw/Tのp@rawはraw/raw/Tで、参照先ならr@follow@rawと書く。即座に終わる@ref同様に検査し、値を読まずLoanも残さない。一時値ならその寿命中だけ有効。効果要約ではPへのアクセスとなる。

Uと元の参照先が正規化後に同じ型なら、@raw/UはOriginを保つ通常のCopy。レイアウト一致だけでは足りない。整数リテラルはusizeへ合わせる。raw型間の変換はアドレス・provenanceを保ち、対象が別Originを指定しなければ元のraw Originも保つ。変換は参照先を読まず、provenance・権限を作らない。

```kimi
var number: i32 = 1
let p = number@raw            // raw/i32; no Loan is created.
let r = number@uniq
let q = r@follow@raw
unsafe
    *q = 2                    // Valid: inside r's Loan.
    *p = 3                    // Undefined behavior: conflicts with r's Loan.
r@follow += 1                 // r's Loan is live above.
```

<a id="s-5-5"></a>

## 5.5. 整数アドレス

ポインターと整数の変換を提供できるのは、単一アドレス空間、アドレス以外の状態を持たないポインター、usizeに収まるアドレス、ポインター・usize・isizeの同じ幅を持つ対象環境だけ。他ではUnsupported。nullと0は相互に変換する。

usizeはprovenanceを運ばない。逆変換は同じアドレスを返すだけで、provenance・寿命・権限・Originを回復せず、本体が所有しない記憶だけへ届く。アクセスには、provenance、権限、その内部に配置された値を定めた実装の文書化保証が必要で、[§5.2.1](#s-5-2-1)の生存・Loan・到達条件も引き続き守る。

```kimi
let restored = pointer@usize@raw/i32   // Equal address only; no provenance.
```

<a id="s-5-6"></a>

## 5.6. 生の記憶領域の操作

| 操作 | 契約 |
| --- | --- |
| allocate | 安全な関数。count要素分の空の記憶を割り当てる。負数、count×stride(T)のオーバーフロー、割当て失敗はAbort。対応最大アラインメントは実装定義で、それを超えるTはコンパイル時Unsupported。0バイトでも整列した非nullと固有の長さ0のprovenanceを返す。複数結果のアドレスは同じでもよい |
| release | null、0バイトの結果、未解放allocate結果の先頭を取る。raw型変換後でもよい。nullは無視し、0バイトは何も解放せず再度releaseできる。配置したNon-Copy値は事前に全体を破棄・Take・再配置する。全体破棄が構成要素の破棄だけなら、残るNon-Copy構成要素へ再帰的に同条件を満たしてもよい。その後、記憶内への全ポインターは無効 |
| initialize | 配置規則に従い、storageにあるTのPlaceへvalueを移す |
| slice | storageからlength個の共有Sliceを作る。length≥0は作者の義務。0ならnullを含む任意の値を許し、アクセスしない。正なら非null・T整列、length×stride(T)がisize内で、各storage[i]がprovenance内の別々のPlaceを指し、完全なTまたはnoinit配列要素を保持することが必要 |

sliceは要素を読まず、未書込みnoinit要素は読む前に書く必要がある。結果の要素iは対応するPlaceそのもの。結果だけに現れるOrigin sは新しい基点を持ち、引数がraw/Tへ合う前にOriginを示していたならその範囲内に限る。Origin全体を通じて要素を書き換えてはならない。効果要約ではallocateは割当て、残りはrawアクセスとなる。

privateは宣言コンテナーとその内部本体だけに届くため、2型が共有するメンバーはinternalにする。BufferViewが生きる間はsourceの共有Loanによりpushできない。raw/TフィールドのためBuffer&lt;T>のTは不変。during selfのdataが指す記憶はBufferの一部となり、挿入値のLoanをBufferと取出し・借用した値の生存中保持する。buffer.dataをsource経由で読めば既にduring sourceなので主張は不要で、sourceが共有Loanだけを保つためそのポインターは読取り権限だけを持つ。

```kimi
public group Raw   // Kimi.Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

```kimi
public struct Buffer<T>
    internal let data: raw/T during self   // Buffer owns the storage; internal: BufferView reads it.
    internal var length: isize
    let capacity: isize                    // private: only Buffer uses it.

    public init(capacity: isize)
        unsafe => self.data = Raw.allocate<T>(capacity)@(raw/T during self)   // A claim (§5.7).
        self.length = 0
        self.capacity = capacity

    public func push(self: uniq/Self, value: T)
        require self.length < self.capacity else => $abort("Buffer is full")
        unsafe => Raw.initialize(self.data + self.length, value@move)
        self.length += 1

    public func get(self: ref/Self, index: isize) -> ref/T
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref

    public func items(self: ref/Self) -> Slice<T>
        unsafe => return Raw.slice(self.data, self.length)

    public func view(self: ref/Self) -> BufferView<T>
        return BufferView<T>.init(self)

    drop
        let data = self.data
        while self.length > 0
            self.length -= 1
            unsafe => _ = data[self.length]@move   // Destroy in reverse order.
        unsafe => Raw.release(data)

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>
    let data: raw/T during source
    let length: isize

    internal init(buffer: ref/Buffer<T> during source)   // internal: Buffer.view calls it.
        self.data = buffer.data                          // raw/T during source: no claim.
        self.length = buffer.length
        self.loan = Loan.init(buffer)

    public func get(self: ref/Self, index: isize) -> ref/T during self.source
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref
```

<a id="s-5-7"></a>

## 5.7. rawポインターのOrigin

raw層のduringは対象記憶を示すだけで、Loan・権限・有効性を作らない。使用箇所をOrigin領域へ追加せず、Loanも生かさない。Origin付きrawもダングリングになり得る。

| 形式 | 指す記憶 |
| --- | --- |
| raw/T | 型では示さない。値の到達先と呼出し契約で決める |
| raw/T during o | oが覆う記憶。oが属する借用のLoanが守る記憶、staticなら静的記憶 |
| raw/T during self | その層を保持する値が所有する記憶。オブジェクトペイロード同様にown stepとなる |

**Originの所属。** 記述するoは共通部分でないOrigin原子1つで、借用に属さなければならない。スロット（x.source等の射影を含む）は、それを保つ格納安全参照・Loan&lt;T>フィールドに属し、合わせてそれらの参照先型が許すものを許容する。借用値の外側Originはその借用、staticは静的記憶に属する。シグネチャのraw Originは入力の借用、スロット、staticのいずれかに属する必要がある。

selfが保持値を指すのは、struct・enumのインライン記憶内のraw層（フィールド、タプル・固定長配列要素、Caseペイロード）だけで、型引数内部では使えない。型自身の本体ではselfはレシーバOriginとなり、コンストラクターでは構築中の値、dropでは破棄レシーバを指し、どちらも排他的である。

フィールド読出し、型付き経路、スロット・入力の置換で得たOriginには共通部分も許し、元Originと同じ所属を保つ。たとえばchoose(x,y)@follow@rawは`raw/T during (x and y)`になり得る。この型はローカルで推論できても記述できない。

**フィールドの読み取り。** during selfのrawフィールドを借用・rawの逆参照経路で読むと、最後に逆参照した借用の外側Origin、rawのOrigin、またはOriginなしを得る。逆参照を通らなければOriginなしで、フィールド記憶への@rawと同じ到達先を持つ。同じ保持値内のタプル・固定長配列では要素ごとに処理し、該当raw層だけこの規則を使う。型のスロットは通常どおり値の束縛で置換する。

**フィールドの書込み。** 借用・rawの逆参照経路を通る場合、during self層は同じ経路で読んだときのOriginを持ち、書込む値とそのPlaceの借用もそれに従う。逆参照なしで届く場合は、その保持型のコンストラクター・drop内のself経由を除き、読み取り（Moveを含む）、共有借用、nullの書込みだけを許す。それ以外の書込み・複合代入・排他借用はOrigin関係エラー。staticを含むどのOriginの値も、Originなしの値も書けず、exchange・swap・replaceの引数も同じ。

これは、保持値が所有する記憶が領域でなくown stepであり、outlivesで合わせるOriginがないためである。書くには保持値またはそれを含むPlaceを排他借用し、そのOriginを主張に使う。同じ保持値内のタプル・固定長配列も要素ごとに同規則を使い、Moveはraw層を読み、他要素をMoveする。復元時はその層へnullを書く。during self層を持つCaseペイロードの構築も、その層はnullに限る。

**型付き経路。** P@rawは最後の借用・rawのOriginを同様に示す。変位とraw型間変換はOriginを保ち、これらだけならunsafeは不要。

> 訳注：原文[§5](#s-5)冒頭・[§5.3](#s-5-3)はポインター算術にunsafeを要求する一方、[§5.7](#s-5-7)の型付き経路の説明は変位にも不要としている。本訳はこの原文上の不整合を解釈で解消していない。

**型合わせと主張。** duringなしは、すべてのOriginがoutlivesするOriginを示すものと扱う。raw層はOriginについて共変なので、Originの除去・短縮は通常の型合わせになる。それ以外の変更はunsafe内の`E@(raw/U during o)`だけで行える主張で、他ではOrigin関係エラー。nullは任意のOriginを取れる。主張は、そのポインターと全派生ポインターの全アクセスがoの覆う記憶内にあることを約束し、違反は未定義動作となる。

**権限。** raw Originは権限を与えず、rawアクセスは権限不足だけを理由にコンパイルエラーにはしない。書込み、Take、その場の破棄、raw Placeの排他借用が定義された動作かは[§5.2.1](#s-5-2-1)で決める。unsafe文脈とOrigin関係（型合わせ・主張・フィールド書込み）は通常どおり静的検査する。

Originが「共有Loanだけを保つ」とは、各許容Semanticsの場合で、所属する借用がすべてref・objref・Slice、全スロットのLoan要件がrefであることをいう。入力・スロットは束縛したシグネチャ・型宣言で判定し、呼出し側の実際の借用で変えない。本体で形成した借用の外側Originはその借用で判定する。このOriginのrawを通じた排他操作は権限条件に違反し未定義動作となる。

**スロットと所有。** raw Originは変性上は共変だが、スロットのLoan要件・Phantom判定における使用には数えない。rawが示すスロットはPhantomであってはならない。during selfの参照先はスロット特性・OwnedOriginsで格納内容と数え、その値のLoanを所有値の保持先に保つ。インライン記憶にduring self層を持つstruct・enumはCopyにならない。ただしraw層の型と、それを含むタプル・固定長配列の型はCopyで、そのPlaceは上の書込み規則と明示的Moveに従う。

**呼出し。** Origin付きraw経由では、そのOriginが属する各借用の参照先型が許す値を格納できる。selfなら保持値型、staticならraw参照先型を使う。Loanを形成せず、借用と同じ保持を行う。Originなしrawパラメーターはその参照先型が許す値を格納できる。結果型のOriginなしrawは、タプル・Option・型引数内部を含め、任意の引数を通じて届く記憶と、本体が所有しない記憶を指せる。名前付き結果型のフィールド内ポインターはその型のフィールド宣言だけで定まり、隠れたポインターには到達範囲条件を適用する。

```kimi
struct Lens<T> {source}
    internal let loan: Loan<ref/T during source>
    internal let at: raw/T during source        // Storage that source covers.

    internal init(target: ref/T during source)
        self.at = target@follow@raw             // raw/T during source: a typed path, no claim.
        self.loan = Loan.init(target)

func read(lens: ref/Lens<i32>) -> i32
    unsafe => return *lens.at                   // Valid: reads storage source covers.

func clear(lens: uniq/Lens<i32>)
    unsafe => *lens.at = 0                      // Undefined behavior: source keeps only a shared Loan.

func adopt(pointer: raw/i32) -> raw/i32 during static
    // return pointer                           // Error: stating an Origin needs a claim.
    unsafe => return pointer@(raw/i32 during static)   // A claim: pointer addresses static storage.
```

```kimi
struct Packet
    internal var pair: (raw/u8 during self, i32)
    public init() => self.pair = (null, 0)

func demo() -> i32
    var b = Packet.init()
    let t = b.pair                // A Copy: t.0 addresses b's storage, without an Origin.
    let moved = b.pair@move       // Reads the layer's element and Moves the i32.
    b.pair = (null, 2)            // The restore writes null in the layer's position.
    return t.1 + moved.1
```

<a id="s-6"></a>

# 6. 宣言とコンテナー

宣言は名前、同じスコープ内で宣言を区別するシグネチャ（[§9.1](#s-9-1)）、型を持つ。フィールドと計算メンバーは[§11.1](#s-11-1)で定義する。

<a id="s-6-1"></a>

## 6.1. 宣言コンテナー

**宣言コンテナー**は、種類に応じたメンバーを持つ名前付きスコープである。

| 種類 | インスタンス | 内容 |
| --- | --- | --- |
| `group` | なし | フィールド、計算メンバー、関数、入れ子のコンテナー。全メンバーは静的。独自パラメーターなし |
| `struct` | あり | 入れ子のgroup・struct・enum・Contract、フィールド、計算メンバー、関数、適合、関連型指定、コンストラクター、選択後に最大1個の`drop`。ジェネリックパラメーター、Origin、制約 |
| `enum` | あり | 閉じた直和型（[§6.5](#s-6-5)） |
| `contract` | なし | 要件、関連型、制約、親。独自の型パラメーターは持てるが、Origin・長さパラメーター、実装、記憶領域は不可（[§8.4](#s-8-4)） |

`extension`宣言と、その候補を探す名前解決はない。`static`は宣言修飾子ではない。groupとrootgroupのメンバーは静的、レシーバのないstruct関数は型関数、structのフィールドと計算メンバーはインスタンスメンバーとなる。Originの`static`は別概念。

```kimi
group Limits
    let max: i32 = 100                   // Static: a group member.
    // static func one() -> i32 => 1     // Error: `static` is not a modifier.
```

<a id="s-6-1-1"></a>

### 6.1.1. ソースルートと入れ子のコンテナー

各SourceDocumentのコンテナー宣言（rootgroupのパスを含む）はプロジェクトルートに加わる。文書の**実行スコープ**には最上位の`let`/`var`、ルート関数、実行項目が入り、他文書からは見えない。`public main`を含むルート関数はここにあり、互いに非修飾参照できる（[§9.5](#s-9-5)）が、名前付き関数なので最上位の束縛をキャプチャーできない（[§9.3](#s-9-3)）。コンテナー本体は実行スコープの外にあり、文書ローカルの束縛・関数を参照できない。`public main`以外のルート関数は文書ローカルで、アクセス修飾子を書くとエラー。起動は[§22.4](#s-22-4)に従う。

ルート関数の明示的完全特殊化は同じ文書の実行スコープ内だけで宣言できる（[§8.13](#s-8-13)）。

`rootgroup A.B`は入れ子のgroupパスを宣言し、ソースルート直下（選択されたルート指令項目を含む）に限る。各要素はgroupを作り、既存の非group型と衝突すれば種類の競合となる（[§9.1](#s-9-1)）。作られたgroupのアクセスは、同じパスの明示的group宣言があればそれに従い、なければ`private`。

ルート、group、structにはgroup・struct・enum・Contractを入れ子にできる。enum、Contract、実行ブロック、条件付き適合ブロックにはできない。言語上の深さ制限はなく、実装上の制限はResource診断。所属だけでは記憶領域、外側のインスタンス、レシーバ、継承、適合は生じない。

group・rootgroup・struct・contractの本体は省略でき、空行・コメント行は本体を作らない。指令選択後に空になってもよい。本体のないcontractはマーカーContract。enumは選択後も空にできず、実行ブロックには選択前にソース項目が必要（[§14.2.1](#s-14-2-1)）。

```kimi
func twice(x: i32) -> i32 => x * 2

public func main()
    let n = twice(2)                  // OK: main is inside the execution scope.

group G
    func f() -> i32 => twice(1)       // Error: a Container body does not see document-local functions.

// public func helper() -> i32 => 0  // Error: only `main` may be a public root function.
```

<a id="s-6-1-2"></a>

### 6.1.2. コンテナーの断片

groupとstructは同一ファイル内でも分割できる。元のKotonoha、親、名前、種類、独自のジェネリック引数数が同じ断片は一つの宣言となるが、Kotonohaライブラリーを越えて結合しない（種類の競合は[§9.1](#s-9-1)）。

断片間ではパラメーターの数・種類・順序・名前、`open`を含む修飾子、既定値適用後のアクセスを一致させる。structのOriginヘッダーは全断片で同じものを書くか、全断片で省略して独自スロットを持たせない（[§15.3.2](#s-15-3-2)）。既存コンテナーへ生成するメンバーは所属先の型ヘッダーを使う。

共有制約領域（型制約と`origin`関係）と基底句はそれぞれ最大1断片が供給する。同じ内容の再掲も不可。各句は記述元の文書で解決する。基底句の束縛集合名（`: Base<T>{b}`）はその断片だけで見え、それを使う`origin`関係も同じ断片の共有制約領域に置く。

指令選択と結合後にフィールド・計算メンバー名、関数・コンストラクターのシグネチャ、名前空間が重複すればエラー。structの選択済み`drop`は最大1個。主断片は不要。Cレイアウトの記憶領域は1断片に集める（[§6.7.1](#s-6-7-1)）。enumとContractは分割できず、同じ同一性（Kotonoha、親、名前、種類、独自型パラメーター数）の宣言は内容が同じでもエラー。Contract宣言の同一性と、束縛済み参照で決まる適合の同一性は別（[§8.6](#s-8-6)）。

```kimi
// A.kimi
struct Box<T>
    T is Comparable
    var count: i32 = 0

// B.kimi
struct Box<T>
    func countValue(self: ref/Self) -> i32 => self.count
// Repeating `T is Comparable` or renaming T to U in B.kimi is an error.
```

<a id="s-6-1-3"></a>

### 6.1.3. 継承する環境と宣言参照

入れ子のコンテナーはgroup・Contractも含め外側のジェネリック環境を継承するが、実行時変数やレシーバは継承しない。外側にパラメーターがあれば未使用でも内側の宣言はジェネリックとなる。

**宣言参照**は宣言の同一性（元のKotonoha、結合済みの親、名前、種類、独自引数数）と、自身および外側の宣言の正規化済みの型・Semantics・長さ・Origin引数からなる。未使用引数も保持し、証明・名前解決の文脈は含めない。各パラメータースロットは綴りでなくバインダーと位置で識別する。`Outer<i32>.Inner<string>`と`Outer<i64>.Inner<string>`は異なる型である。

```kimi
struct Outer<T>
    public struct Inner<U>
        var first: T
        var second: U
// Outer<i32>.Inner<string> and Outer<i64>.Inner<string> are distinct Types.
```

<a id="s-6-1-4"></a>

### 6.1.4. 制約の役割

宣言利用者が満たす入力条件は定義検査時の仮定となる。外側structのSelfの適合や閉じた条件など、宣言の義務には独立した証明が必要。内側の宣言は外側の入力条件と証明済みの根拠を使えるが、義務を仮定に変えたり外側のSelfを自身のSelfとして使ったりできない。内側の入力条件は外側へ伝わらない。

Contractでは継承した引数だけへの条件は参照の入力条件、適合するSelfへの条件は実装要件、閉じた条件は宣言の義務となる。型宣言の`Self is not ObjectPayload`はオプトアウトであり、条件でも義務でもない（[§8.7.3](#s-8-7-3)）。関数制約の主語は[§7.5](#s-7-5)に従う。structは自身に入れ子のContractを実装できる。適合は自身の識別・互換性の根拠にならない（[§8.3](#s-8-3)、[§8.11.3](#s-8-11-3)）。義務は許される全束縛で成立しなければならない（Finality、[§8.3](#s-8-3)）。

<a id="s-6-1-5"></a>

### 6.1.5. Self

`Self`は全束縛を含む最も内側のstructまたはenum、Contract内では適合する型を表す。groupはSelf文脈を作らず、最も近い外側のSelfを使う。groupメンバーにはレシーバがないが、型を明記した`self`というパラメーターは書ける（[§7.3](#s-7-3)）。group本体には制約句、適合、関連型指定を直接置けない。

```kimi
struct Parser
    group Helpers
        func identity(value: Self) -> Self => value@move       // Self is Parser.
        struct State
            func identity(value: Self) -> Self => value@move   // Self is State.
```

<a id="s-6-2"></a>

## 6.2. 構造体宣言

`struct`は名前付きの複合Coreを宣言する。フィールドはSemanticsとOriginを含む完全型を持ち、記憶領域を増やすのはフィールドだけ。

**有限なインライン記憶領域。** フィールド、直接基底、Caseペイロード、タプル・固定長配列要素（長さ0も含む）、宣言がインライン格納する型引数をたどり、借用・生ポインター・オブジェクトハンドルを通らず同じ宣言へ戻るstruct/enumは無効。型引数が異なっても同じで、未使用・未実体化でも宣言時にLanguageエラーとし、Resource診断にはしない。間接参照を通る異なる実体化の増殖（多相再帰など）だけが生成上限に達しうる。

```kimi
struct Shared
    var next: Option<rc/Shared>         // OK: an object handle is an indirection.

struct Node
    var next: Option<Node>              // Error: Node contains itself inline.

struct Grow<T>
    var more: Option<Grow<(T, T)>>      // Error: inline recursion, whatever the arguments.
```

<a id="s-6-2-1"></a>

### 6.2.1. 論理宣言順

文書間の宣言順は論理文書名（[§20.1](IMPL.md)）全体をUTF-8バイト列として比較し、同じ文書内ではソース位置で決める。`/`も通常のバイト。生成断片は通常の断片の後に実装定義の決定的な順序で置く。フィールド初期化子はこの順、破棄は逆順（[§16.3.2](#s-16-3-2)）で、レイアウトに依存しない。文書名変更で副作用の順序が変わりうる。パッケージのソース一覧には順序の意味はない。`#Layout("C")`以外のレイアウトは実装定義（[§6.7.1](#s-6-7-1)）。

```kimi
// a.kimi
struct Config
    var first: i32 = Log.trace(1)
// b.kimi
struct Config
    var second: i32 = Log.trace(2)
// Initialization calls Log.trace(1), then Log.trace(2) (Log: an application group).
```

<a id="s-6-2-2"></a>

### 6.2.2. 継承とopen構造体

`open struct`以外は**sealed**で基底にできない。open性はアクセスと独立で継承されないが、ObjectPayloadのオプトアウトは継承される（[§8.7.3](#s-8-7-3)）。名前・ジェネリックパラメーター・Origin列の後の`: BaseType`で直接基底を最大1個指定する。基底はアクセス可能な`open struct`のCoreに限り、型パラメーター、Semantics適用済み型、group、enum、contractは不可。同じジェネリック宣言の別構築を経る循環もエラー。基底の制約を満たし、そのアクセス範囲が派生型の実効アクセス範囲を覆う必要がある（[§9.4.4](#s-9-4-4)）。継承でprivateアクセスは得られない。非virtual呼出しはオブジェクトビュー経由でもレシーバの実効型から静的に実装を選ぶ（[§9.6](#s-9-6)、[§9.6.1](#s-9-6-1)）。

有効なoverride（[§6.4](#s-6-4)）を除き、派生structは宣言位置からアクセス可能な祖先メンバーと同名の値役割メンバー（任意シグネチャの関数、フィールド、プロパティ）を宣言できない。条件付き前提やオーバーロードも例外にならない。プロパティ自身のアクセスを調べ、protectedは派生レシーバで判定する。アクセス不能な祖先名は再利用でき、明示的特殊化は別メンバーではない。

直接基底は直接宣言フィールドに先行する単一のインライン所有サブオブジェクト。構築は基底から、破棄は派生層から行う（[§6.3.3](#s-6-3-3)、[§16.3.2](#s-16-3-2)）。基底ビューから基底サブオブジェクトをMove・置換・再構築できない。値全体の操作は正確な型を保ち、スライシングしない。継承フィールドのアクセスには全包含層の部分Move制限を適用する。オブジェクトのアップキャストは[§13.6.1](#s-13-6-1)。

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf     // Error: Leaf is sealed.
```

<a id="s-6-3"></a>

## 6.3. コンストラクター

<a id="s-6-3-1"></a>

### 6.3.1. 宣言

任意のアクセス指定、`init`、パラメーター列、任意の`: base(arguments)`、本体（[§14.2](#s-14-2)）で宣言する。名前、明示レシーバ、タスクスロット、独自ジェネリックパラメーター、結果注釈は持たない。修飾子は[§F.3](#s-F-3)、型パラメーター・Origin・制約は構造体のものを使う。借用注釈は呼出しごとのOriginを導入でき（[§15.3.4](#s-15-3-4)）、入力の省略は[§15.4](#s-15-4)に従う。ラベル、既定値、型検査は通常パラメーターと同じで、Origin関係は実行項目より前に書く。アクセスの既定値は`private`。自身の構造体断片だけが宣言できる。

```kimi
public open struct Named
    public let name: string
    protected init(name: string)
        self.name = name@move           // Transfer the Non-Copy parameter.

public struct Entry : Named
    public let number: i32
    public init(name: string, number: i32) : base(name@move)
        self.number = number            // Copy.

let entry = Entry.init("item", 1)
```

<a id="s-6-3-2"></a>

### 6.3.2. 構築式

型形の修飾パスに`.init(`が続く形は常に構築式。`::`で開始し型引数を含められるが、修飾部が値だけに解決すればエラー。

`Type.init(arguments)`は解決した正確な型の新しいownerを作る。構造体と外側環境を先に解決（[§9.7](#s-9-7)）し、自身のアクセス可能なコンストラクターを選ぶ（[§10](#s-10)）。独自の型引数は省略・推論でき（[§10.8.1](#s-10-8-1)）、推論引数を明記した場合と同じ結果となる。引数や期待型は別宣言の選択や外側コンテナーの推論に使わない。コンストラクターは継承されず、フィールド単位・ゼロ埋めの代替もない。裸の型パラメーターは構築できない。`Type.init`は関数値ではなく、`value.init(...)`は無効。オブジェクトハンドルは別に作る（[§13.6.2](#s-13-6-2)）。

```kimi
struct Box<T>
    public let value: T
    public init(value: T) => self.value = value@move

let number: i32 = 42
let box = Box.init(number)               // Box<i32>.
let wide: Box<i64> = Box.init(42)        // The literal fits i64.
// let wrong: Box<i64> = Box.init(number) // Error: the input binds T to i32.
```

<a id="s-6-3-3"></a>

### 6.3.3. 構築順

引数を評価してUninitializedな記憶領域を確保し、`self`を参照せず基底引数を評価した後、基底サブオブジェクトで基底コンストラクターを実行する。基底句の省略は引数なしで呼べる基底コンストラクターを選ぶ。基底がない`: base(...)`はエラー。基底呼出しはちょうど1回で、同じ型の別コンストラクターへの委譲はない。ここではprotectedな基底コンストラクターを利用できる（[§9.4.3](#s-9-4-3)）。

次にこの層のフィールド初期化子を論理宣言順（[§6.2.1](#s-6-2-1)）で実行し、本体へ進む。初期化子はパラメーターと`self`を参照できない。全コンストラクターが実行するため、Origin関係は型の公開前提と整形式性（[§15.3.2](#s-15-3-2)、[§15.6.1](#s-15-6-1)）だけで判断し、コンストラクターのシグネチャ・句を使わない。暗黙に初期化されるフィールドはない。格納引数のOriginは型の宣言済みOrigin契約で表現可能でなければならない（[§15.3](#s-15-3)）。

<a id="s-6-3-4"></a>

### 6.3.4. 構築時のレシーバ

アクセスとLoanの検査では、構築中の`self`は初期化済みの自身のフィールドに対する`uniq/Self`同様の排他書込み可能ルート。[§11.2](#s-11-2)の権限に従って読出し、Copy、共有・排他借用（レシーバ式の暗黙取得を含む）、置換ができる。ただし初期化済み`let`は読取り専用で、Non-Copy値をMoveで取り出せない。未初期化フィールドへの初回配置は[§11.7](#s-11-7)、[§15.1.5](#s-15-1-5)に従い、カスタム`set`を呼ばない。

計算アクセス、継承フィールドへのアクセス、`self`全体の取得・借用、`self`上のメソッド・カスタムアクセサー呼出し、`self`のキャプチャー・公開は禁止。フィールド借用は構築完了前に終了する。この特権はコンストラクター本体だけにあり、入れ子関数・メソッド・フィールド初期化子にはない。

```kimi
struct Tally
    var count: i32
    var total: i32
    init(start: i32)
        self.count = start                                // First placement.
        self.total = 0
        self.count = self.count + 1                       // Replacement of an initialized var Field.
        Kimi.Intrinsics.replace(self.total@uniq, with: 5) // Exclusive borrow of an initialized Field.
```

<a id="s-6-3-5"></a>

### 6.3.5. 完了と失敗

本体の結果はUnit。到達可能な正常終了では基底が完了し、全自身フィールドがInitializedでなければならない。その後Scope Exitを実行するため、`defer`では不足する初期化を補えない。コンストラクターは失敗不能で、失敗可能・非同期構築にはファクトリー関数を使う（[§24.2.1](#s-24-2-1)）。未完成の層のまま通常終了できず、不完全な`return`はエラー。構築を中断できるのは後始末をしないAbortだけ。

<a id="s-6-3-6"></a>

### 6.3.6. 暗黙コンストラクター

結合後、privateを含む選択済みコンストラクターがなく、自身の全フィールドに初期化子があり、基底がある場合は省略された基底句が基底コンストラクターを選べるときに限り、引数なしコンストラクターを一つ生成する。メンバー単位のコンストラクターはない。

基底選択は派生宣言の文脈・制約で行う通常の引数なし選択（[§9.4.3](#s-9-4-3)）。候補なし・曖昧なら生成せず、型は有効なまま。候補前提がUnknownでも生成せず、構築はエラー。生成の有無は初期化子の妥当性に依存しない。生成物は型の実効アクセスを持ち、`init() => ()`として動作する。[§22.1](#s-22-1)のKimi型には宣言済みコンストラクターしかない。

```kimi
public open struct Counter
    protected var count: i32 = 0
    protected init() => ()

public struct Tally : Counter        // Synthesized public init() calls the protected base constructor.

public open struct Choice
    public init(first: i32 = 1) => ()
    public init(second: bool = true) => ()

public struct Picked : Choice        // Valid; the base selection is ambiguous, so no constructor.
// let picked = Picked.init()       // Error: Picked has no constructor.
```

<a id="s-6-4"></a>

## 6.4. virtualメンバーとoverride

通常のメンバーは非virtual。virtualスロットは静的に選び、オブジェクトの動的型はその実装だけを選ぶ。

<a id="s-6-4-1"></a>

### 6.4.1. 宣言

`virtual func`はスロットと元の実装、`override func`は継承スロットの実装を宣言する。両者は併用できず、修飾子は[§F.3](#s-F-3)に従う。いずれも本体を持つsafeなインスタンス関数で、レシーバは`objref/Self`または`objuniq/Self`、独自の型・Semantics・長さパラメーターは不可。裸の`self`は`ref/Self`なので不可。元の宣言は`open struct`の非privateメンバー、overrideは派生structメンバーに限る。型・group関数、要件、プロパティ、アクセサー、`init`、`drop`はvirtualにできない。

公開契約は元の宣言だけが所有する。overrideは明示的特殊化同様（[§8.13.2](#s-8-13-2)）に継承し、レシーバのSelfだけを派生Selfへ写す。

| overrideのヘッダー | 規則 |
| --- | --- |
| 入力、レシーバモード | 数・順序・正規化済み型、および`objref`/`objuniq`が一致 |
| タスクスロット | 元の宣言と同じ（[§24.2.1](#s-24-2-1)） |
| 結果 | 種類・正規化済み型が一致。共変不可 |
| パラメーター名 | 外部名は一致、内部名は変更可 |
| Origin | 継承する。明記するなら同じ契約 |
| アクセス、`!`、既定値、制約、効果 | 元のアクセス範囲ごと継承し再宣言不可。`protected internal`を派生Kotonohaで解釈し直さない |
| 属性、その他の修飾子 | 不可 |

本体の名前解決とprivateアクセスは派生宣言の文脈を使う。レシーバ以外の`Self`は元の記述位置の型を保つ。元の`other: objref/Self`が`objref/Base`ならoverrideも`objref/Base`と書く。open structのoverrideは再overrideできる。

```kimi
open struct Base
    public virtual func score(self: objref/Self, bonus: i32 = 1) -> i32
        effect confined
        return bonus

struct Derived : Base
    override func score(self: objref/Self, bonus: i32) -> i32
        return base.score(bonus) + 10

let d = Derived.init()@obj
let a = d.score()                     // 11; the original supplies the default.
let b = Base.score(d@objref/Base)     // 11; Type qualification still dispatches.
```

<a id="s-6-4-2"></a>

### 6.4.2. override対象の選択

**virtualスロット**は元の宣言と、宣言型・基底パスの正規化済み束縛で識別する。

1. 直接基底から継承名前解決（[§9.6](#s-9-6)）を行い、最初のアクセス可能な同名関数群を取る。対象はvirtualスロットだけ。
2. 群内のアクセス可能な全virtualが共有する置換済み宣言前提を正規化命題として比較し、共通前提を字句環境に加えてoverrideの入力型を束縛する。Refutedな置換前提は根拠にならない。タスクの有無、レシーバモード、入力を制約証明でなく正規化型構造で比較する（[§8.13.1](#s-8-13-1)）。ここで入力型を解決できなければ宣言エラー。
3. 一致するスロットがちょうど一つなら、その全前提を継承して契約を検査する。

対象なし・複数・契約不一致は宣言エラー。結果、ラベル、既定値、宣言順で曖昧さを解消せず、遠い祖先も再探索しない。派生型の各スロットへのoverrideは、互いに排他的な条件付き適合ブロック内も含め最大1個。

overrideは名前解決の層、オーバーロード、公開関数を増やさない。`Derived.f`と`Base.f`は元の公開レシーバを持つ同じFunction Itemであり、アクセスと型が合えば`Derived.f(baseObject)`も有効。

```kimi
open struct Base<T>
    public virtual func f(self: objref/Self, x: ref/T) -> i32 => 0
    public virtual func f(self: objref/Self, x: ref/i32) -> i32 => 1
struct D : Base<i32>
    // override func f(self: objref/Self, x: ref/i32) -> i32 => 2
    // Error: matches two slots.
```

<a id="s-6-4-3"></a>

### 6.4.3. 呼出しと条件付きスロット

静的選択でスロットを固定し、実行時は動的型により継承経路上で最も派生した実装、なければ元の実装を選ぶ。名前・オーバーロード・制約・アクセスの再探索はしない。メンバー呼出し、型修飾呼出し、Function Item、Function値の全てがディスパッチする。非束縛呼出しに暗黙アップキャストは加わらない。

元の宣言は外側パラメーターへの条件付きメンバー前提`P`を持てる。呼出しとFunction Item取得には`P`が必要。overrideは派生型の制約と基底経路で置換した`P`を仮定して検査する。置換結果がRefutedまたはErrorなら、存在しないスロットの実装となるため宣言エラー。Unknownなら通常の仮定。条件付き適合ブロック内ではブロック条件がこれらの前提から導けなければならない。実装選択条件は追加できず、適用不能な`P`は代替実装や実行時判定を選ばない。

```kimi
open struct Base<T>
    public virtual func same(self: objref/Self, x: ref/T) -> bool
        T is Equatable
        return false
struct Plain
struct Derived : Base<Plain>
    // override func same(self: objref/Self, x: ref/Plain) -> bool => true
    // Error: Plain is Equatable is Refuted, so the slot does not exist here.
```

<a id="s-6-4-4"></a>

### 6.4.4. 直接基底呼出し

`base.f(arguments)`は字句上の`self`を使い、宣言structの直接基底から`f`を探し、その基底実装を直接呼ぶ。`A → B → C`ならCからの呼出しはB自身またはBが継承した実装となる。派生インスタンス本体でアクセス可能な基底インスタンス関数に使え、呼ばれた本体内の呼出しは通常どおりディスパッチする。静的に選んだ公開契約・既定値を使い、選択した本体の効果要約も使える。匿名関数では明示的な`self`キャプチャーが必要。裸の`base`、値としての`base.f`、フィールドの`base.x`、`base.base`は無効。

<a id="s-6-4-5"></a>

### 6.4.5. レシーバの安全性

virtualレシーバの借用は派生フィールドを含む全ペイロードを覆う。全オブジェクト基底ビュー操作にはOwned消去の証明が必要（[§13.6.1](#s-13-6-1)）。override本体では元のスロットのOwned消去の根拠が、字句上の`self`を元にする全基底ビュー操作を覆う。virtual・非virtual・直接基底呼出しのレシーバ射影と`self`の明示的アップキャストが含まれる。他オブジェクトには別証明が必要で、元のvirtual本体はこの根拠を得ない。破棄中のオブジェクトにはディスパッチできない（[§16.4](#s-16-4)）。

元の宣言と各overrideは全てObjectCallCompatibleがProvenでなければならない（[§12.7](#s-12-7)）。レシーバ操作は完全性、動的型、Origin束縛を保存する。基底ビュー・基底サブオブジェクトの置換は禁止だが、sealedな実装の`self@follow`置換など、完全ペイロードの更新と証明された操作は可能。各実装は個別に検査し、未使用でも違反はエラー。

```kimi
open struct Base
    public virtual func g(self: objref/Self) -> i32 => 0
    public virtual func f(self: objref/Self) -> i32 => 0
struct Derived<U> : Base
    var item: U
    override func f(self: objref/Self) -> i32
        return self.g()   // OK: projection of self is covered by the slot's evidence.
```

<a id="s-6-4-6"></a>

### 6.4.6. 効果とContractの証拠

スロットの効果境界は[§8.11.1](#s-8-11-1)に従う。動的呼出しはsealedな対象でもスロットの公開契約と境界だけを使う。効果は[§8.11.3](#s-8-11-3)の規則1・2、結果は公開Origin・Loan契約に従う。Contract要件のvirtual実装はスロットに対応する。要件を満たすのはスロットの公開契約と境界でなければならず、本体のより強い動作保証では代用できない。

<a id="s-6-5"></a>

## 6.5. enum

enumは名前付きの閉じた直和Core。値はちょうど一つの**Case**と、その位置引数データである**ペイロード**を持つ。

<a id="s-6-5-1"></a>

### 6.5.1. Caseとペイロード

Caseは1行に一つ書き、名前は重複不可。各ペイロード要素は完全型で、名前・既定値・可変長指定を持たない。`Quit`はペイロードなし、`Quit()`は無効、`Wrapped(())`はUnitペイロードを持つ。

ヘッダーにはジェネリックパラメーターと任意のOriginヘッダーを書ける（[§15.3.2](#s-15-3-2)）。本体にはCase、制約句、関連型指定、関数、条件付き適合（[§8.8](#s-8-8)）、指令を置ける。フィールド、計算メンバー、`init`、`drop`、入れ子コンテナー、継承は不可。選択後も1個以上のCaseが必要。Caseはenumのアクセス範囲を持ち修飾子を取らず、ペイロード型もその範囲でアクセス可能でなければならない（[§9.4.4](#s-9-4-4)）。他の値宣言とのCase名の衝突はエラー。

ペイロードはフィールド同様に検査する匿名記憶領域。依存はenumのOrigin引数に束縛し、変性とLoan要件は全Caseから求める（[§15.3.5](#s-15-3-5)）。Case選択でOrigin契約は弱まらない。共有経路からの`uniq/T`ペイロードは`ref/(uniq/T)`として束縛し、固定期待型`ref/T`へは両層を通って再借用する（[§10.2](#s-10-2)）。記憶領域は有限（[§6.2](#s-6-2)）で、暗黙のヒープ割当てはない。Case変更は全値代入で行い、`value.0`での抽出はない。等価・順序は自動導出しない（[§13.4.1](#s-13-4-1)）。判別値、タグサイズ、ABIは未規定。

enumオブジェクトは所有enum値へ`@obj`・`@rc`・`@arc`を適用して作る（[§13.6.2](#s-13-6-2)）。ハンドルを`@follow`でたどり、得た値Placeをmatchする（[§13.5.5](#s-13-5-5)）。期待型がオブジェクトの`.Case`はエラーで、暗黙のオブジェクト生成はない。Caseパターンはハンドルを透過しない（[§14.8.1](#s-14-8-1)）が、ペイロードにObject Semanticsは格納できる。

```kimi
let h = Option<i32>.Some(1)@obj
let n = match h@follow           // Sealed payload: matches the Option value Place.
    .Some(let x) => x            // x: ref/i32; value-read.
    .None => 0
// let bad: obj/Option<i32> = .Some(1)   // Error: `.Some` needs an owned expected enum Type.
```

<a id="s-6-5-2"></a>

### 6.5.2. Case参照

Case参照は`Type.Case`か`.Case`のみ。開く別名があっても非修飾名では選べない（[§9.5](#s-9-5)）。修飾形は通常のメンバーアクセス・呼出しとして解決し、Caseを選んだらCase構築となり、別呼出しとして再試行しない（[§9.6](#s-9-6)）。`.Case`（[§F.9.1](#s-F-9-1)）は既知の期待enum、すなわち所有期待型またはパターン位置の型（[§14.8.1](#s-14-8-1)）内だけで解決する。ペイロードCaseは全位置引数を取り左から右へ評価する。ペイロードなしCaseに括弧は付けない。Caseは関数値ではない。

独自の型引数は次の順で決まる。

1. 修飾部の独自型引数列を省略した場合、[§9.7](#s-9-7)でenumを選ぶ。
2. 構築では期待型が同じenumの所有型なら固定引数を先に供給する。結果のペイロード型に各引数をAdaptationし（[§10.2](#s-10-2)、関数型消去は[§7.7.4](#s-7-7-4)）、残る独自スロットをペイロードから推論する（[§10.8](#s-10-8)）。リテラル既定型（[§10.2.1](#s-10-2-1)）適用後も未束縛ならエラー。先頭ドット形にはenumを特定する期待型が必要。明示型引数が期待型と異なればエラー。
3. 修飾Caseパターンでは独自型引数を省略でき、パターン位置の型から得る。明示するなら一致が必要。

外側コンテナーの引数は推論しない（[§9.7.1](#s-9-7-1)）。修飾部にはenum自身のSemantics・Origin引数や束縛集合接尾辞を書かない（`View<T>{v}.Some(...)`はCase参照でない）。型引数内の完全型は自身のものを保つ。Origin引数は期待型とペイロードから推論し、新造しない。決まらなければ期待型を注釈する。

```kimi
enum Message
    Quit
    Write(string)
    func make() -> Message => .Quit
    func wrap(s: string) -> Message => Self.Write(s@move)
    // func bad() -> Message => Quit   // Error: a Case needs `Self.` or `.`.

let a = Option.Some(1)               // Option<i32>, from the payload.
let b: Option<i64> = Option.Some(1)  // The expected Type supplies T first.
// let c = Option.None               // Error: nothing determines T.
// let d: Option<i32> = .None()      // Error: a payload-free Case takes no parentheses.
let n = match a
    Option.Some(let x) => x          // T from the matched Type.
    .None => 0
```

<a id="s-6-6"></a>

## 6.6. 束縛

フィールドとローカル束縛は不変の`let`または可変の`var`で始める。推論可能なら型注釈か初期化子を省略でき、注釈のないフィールドは初期化子の型を取る（[§11.6](#s-11-6)）。ローカル型は宣言時に固定し、明示型でOriginを省略するなら初期化子が必要（[§15.4](#s-15-4)）。束縛は宣言後から見えるため、`let x = x`の右辺は外側の`x`を指す（[§9.3](#s-9-3)）。初期化は[§15.1.1](#s-15-1-1)に従う。

```kimi
let limit: i32 = 10
var later: i32                     // Fully specified: no initializer needed.
later = limit
// let r: ref/i32                  // Error: the omitted Origin needs an initializer.
```

<a id="s-6-7"></a>

## 6.7. 属性

属性は`#Name`と任意の引数からなり（[§F.6.1](#s-F-6-1)）、同じ字下げの次の宣言に付く。空行・コメントを挟める。単一宣言を対象とする`#if`の前ならその選択を共有する。指令ブロック・`#switch`の前、無関係な項目や字下げの終了の前にある属性は宙に浮き、エラーとなる。

コンテナー、関数、フィールド、計算宣言、パラメーターに付けられるが、明示的特殊化とContract要件には付けられない。除外構文では構文だけを検査する（[§19.4](#s-19-4)）。認識するのは`#Layout`、`#LibraryImport`（[§22.6](#s-22-6)）、`#Test`だけで、他の選択済み属性はエラー。属性名は大文字Luで始めるため`#test`は無効。

```kimi
#Test
func check() => ()
// #test   // Error: an Attribute Name starts with an uppercase (Lu) letter.
```

<a id="s-6-7-1"></a>

### 6.7.1. レイアウト

`#Layout("Kimigayo")`と`#Layout("C")`はstruct専用。名前なし・補間なしの文字列リテラルをちょうど一つ取り、大文字小文字を区別する。1断片に最大1個で同値の重複も不可。同じstructの断片で異なるモードは指定できず、省略時はKimigayo。Cモードでは生成分を含む選択済みインスタンスフィールドを一つの断片に記述順で集めるが、属性はどの断片に置いてもよい。

Kimigayoレイアウトは実装定義。Cは対象のC ABIに従い、対応するCレイアウトがなければUnsupported。open・派生・空のstruct、およびゼロサイズのフィールド（Unit、Never、長さ0配列、ゼロサイズstruct）を含むstructではCレイアウトを使えず、そのレイアウトを必要とする使用はLanguageエラー（[§8.15.3](#s-8-15-3)）。

```kimi
#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64

#Layout("c") // Error: mode names are case-sensitive.
struct Invalid
    var value: i32
```

<a id="s-6-7-2"></a>

### 6.7.2. テスト定義

引数なしの`#Test`は一つのテストを指定する。対象はルートまたはコンテナー内の名前と本体を持つsafe関数で、パラメーター・レシーバ・タスクスロットなし、結果Unitに限る。自身にも外側コンテナーにもジェネリック・Originパラメーターを持てない。外部インポート、特殊化、ローカル関数は対象外。不適格な対象や重複`#Test`はエラー。

テストビルド以外では本体を構文より先まで検査しない。利用者コードはTest関数を呼んだり値として取得したりできず、テストランナーだけが呼ぶ（[§20.6.1](IMPL.md)）。

```kimi
group Arithmetic
    func add(left: i32, right: i32) -> i32 => left + right

    #Test
    func addition()
        $expect(add(1, 2) == 3)
```

<a id="s-7"></a>

# 7. 関数と呼出し可能な値

<a id="s-7-1"></a>

## 7.1. 宣言・本体・結果

関数は`func`、非修飾名、任意のジェネリックパラメーター、括弧付きパラメーター列、任意の`->`結果型で宣言する（[§F.6](#s-F-6)）。名前付き関数で`->`を省略するとUnit（`()`）を返す。Originパラメーター列はなく、借用注釈で暗黙のスカラーOriginを導入できる（[§15.3.4](#s-15-3-4)）。宣言は記述位置のコンテナーまたは実行スコープに属し、`func View.get(...)`のような修飾宣言名はエラー。

結果型は宣言だけで決まる。ジェネリック本体は宣言型・制約の下で検査し、実体化時に再解釈しない。本体の結果は[§14.2](#s-14-2)、各`return`は[§14.9](#s-14-9)に従い結果型に合う必要がある。

既存の記憶領域は、共有なら`ref/T`、排他なら`uniq/T`を結果として返すことでだけ貸せる。参照のOriginと、通常の結果の返却・適合・推論・効果規則が適用される（[§15.3.4](#s-15-3-4)、[§15.4.3](#s-15-4-3)）。呼出し側は参照を値として受け取り、暗黙にはたどらない。`let r = f()`は参照を保持し、期待型があればAdaptation（[§10.2](#s-10-2)）、`f()@follow`ならCopy・Moveせず参照先を選ぶ。Readと`uniq/T`経由のWriteを得るがTakeは得ない（[§13.5.5](#s-13-5-5)）。参照先を借用したときの依存は参照先と親Loanにあり、参照を保持する一時値にはない（[§10.2](#s-10-2)、[§13.5.6](#s-13-5-6)、[§15.6.3](#s-15-6-3)）。結果の`during self`はレシーバ借用に依存する。`during self.source`など他のOriginなら、その宣言と本体検査で証明できる場合に限りレシーバ借用から独立する。`index`/`indexUniq`の結果参照を暗黙にたどるのは添字式だけ（[§4.7.1](#s-4-7-1)）。

```kimi
func direct(left: i32, right: i32) -> i32 => left + right
func indented(left: i32, right: i32) -> i32
    return left + right
func unused() => 123            // Valid: a Unit function discards 123.
func bad(left: i32, right: i32) -> i32
    left + right                // Error: the structural end supplies Unit.
```

```kimi
func first<T>(values: ref/Array<T>) -> ref/T during values
    return values[0]@ref                    // Shared borrow of the element.
func slot(values: uniq/Array<i32>) -> uniq/i32 during values
    return values[0]@uniq                   // An exclusive borrow is written.

// resources: Array<Resource>, Resource Non-Copy; counts: var Array<i32>.
let view = first(resources@ref)             // ref/Resource: the reference is kept.
inspect(first(resources@ref))               // The reference fits a ref/Resource parameter.
slot(counts@uniq)@follow += 1               // Updates counts[0].
// slot(counts@uniq) += 1                   // Error: an assignment target is never redirected.
// let value = first(resources@ref)@follow  // Error: a Non-Copy referent cannot be read bare.
```

<a id="s-7-2"></a>

## 7.2. パラメーター

匿名関数・コンストラクター・レシーバ・セッターの`value`を含む明示パラメーターは、初期化済みの不変な`let`相当の束縛であり、`var`パラメーターはない。`@move`で一度転送できるが、パラメーターの記憶領域の再代入・再初期化・新たな排他借用はできない。`uniq/T`や`objuniq/T`から参照先への排他アクセスはできる。構築・破棄レシーバは固有の権限を保つ（[§6.3.4](#s-6-3-4)、[§16.3.1](#s-16-3-1)）。

```kimi
func grow(items: uniq/Array<i32>, extra: Array<i32>)
    items.appendAll(extra@move)   // Valid: exclusive access to the referent; one transfer.
    // extra = Array.init(capacity: 0)  // Error: a parameter cannot be reassigned.
```

<a id="s-7-2-1"></a>

### 7.2.1. 引数名の境界

名前付き関数、コンストラクター、Contract関数要件のパラメーター列には、コンマの代わりに`!`を一つ書ける。その前の通常パラメーターは位置・名前の両方、後のものは外部名でだけ渡せる。境界なしなら全て両方で渡せる。左側は空でもよいが、右側には通常パラメーターが1個以上必要。レシーバはどちら側にも置けるが数えず、`(! self)`と`(self !)`は無効。末尾コンマは列全体の末尾だけに置け、`!`に接してはならない。`name?`・`name!`接尾辞はエラー。タスクスロットは両区画より前に置く（[§24.2](#s-24-2)）：`func pause(task; ! delay: Time.Duration)`。

`external => internal: T`でどちら側でも名前を変えられるが、改名だけでは名前指定を必須にしない。外部名はレシーバを含む全列で一意。内部名は[§9.3](#s-9-3)に従う。他の列には境界を置けず、特殊化ヘッダーにも書かない。外部関数宣言では境界を使えるが既定値は不可（[§22.6](#s-22-6)）。

<a id="s-7-2-2"></a>

### 7.2.2. 名前契約と既定値

通常パラメーター数を`N`、境界前の数を`K`（境界なしなら`N`）とする。レシーバ・タスクスロットは数えない。0始まりのパラメーター`i`を位置指定できるのは`i < K`のときだけ（[§10.1.1](#s-10-1-1)）。順序付き外部名と`K`が**引数名契約**であり、型・レシーバ位置・既定値は含まない。したがって`(self ! x: i32)`と`(! self, x: i32)`は同じ契約。特殊化はこれを継承する（[§8.13.2](#s-8-13-2)）。適用可能性には影響するが、オーバーロードの同一性・順位には影響しない（[§9.1](#s-9-1)）。

`= default`があるパラメーターは省略でき、なければ境界のどちら側でも必須。既定値付きが必須パラメーターに先行してもよく、その場合は呼出し側が後者を名前で渡す。呼出しは静的に選んだ宣言の契約・既定値を使い、override・特殊化のものは使わない。Contract要件経由は[§8.4.2](#s-8-4-2)に従う。関数値には名前も既定値もなく、全引数を位置指定する（[§7.7](#s-7-7)）。

```kimi
func scale(value: i32 ! by => factor: i32 = 1) -> i32 => value * factor
func options(mode: i32 = 0 ! count: i32) => ()

scale(3, by: 4)
scale(3, factor: 4)   // Error: factor is the internal name.
scale(3, 4)           // Error: by requires its name.
options(count: 3)     // mode uses its default.
options(3)            // Error: 3 supplies mode, and count is missing.
```

<a id="s-7-2-3"></a>

### 7.2.3. 既定値の評価

各呼出しでは明示引数をソース順に取得して待機スロットへ置き、その後省略された既定値をパラメーター順に一度ずつ宣言スコープで評価する。先行パラメーターは使えるが、後続パラメーターと呼出し側ローカルは使えない。先行するCopy値のCopyや一時的な共有アクセスでの観察は可能。予約済み排他入力も観察できるが排他権限は使えない（[§15.6.7](#s-15-6-7)）。先行スロットのMove・変更は禁止。結果は先行引数の新しい借用・再借用を保持できないが、既存の共有借用はCopyできる。観察Loanは被呼出し関数への進入前に終わる。

既定値内の呼出しが省略する既定値を経て自身を再評価しうる場合、その既定値は**再帰的**。省略した再帰的既定値の値は、その呼出しの全先行引数を保持しうる結果と同じく、全先行引数のLoanを伴う（[§15.6.4](#s-15-6-4)）。既定値内の制御転送はその内部の構文だけを対象にする。

既定値はジェネリック本体同様、宣言時に検査する（[§8.15](#s-8-15)）。制約を満たす全束縛でパラメーター型に合う必要があり、推論の根拠にはならない。評価ごとに呼出しの束縛で実体化する。通常の引数・Loan検査以外では、効果境界へ加わる効果だけが呼出しを無効にしうる（[§8.11](#s-8-11)）。引数評価中の制御転送は被呼出し関数を実行せず、準備済み値と一時値を[§16.2.1](#s-16-2-1)で後始末する。Abortは巻き戻さない。準備後のスロットはパラメーターとなり、被呼出し側がパラメーターの逆順で後始末する。

```kimi
func inc(value: i32) -> i32 => value + 1
func label(x: string, y: string = Text.toString(x)) => ()  // Borrows x; y is independent.
func bad(x: string, y: string = x) => ()                   // Error: a default cannot Move x.
func erased(action: (i32) -> i32 = inc) => ()              // Valid: inc is erased.
func run<F>(action: F = inc)                               // Error: inc's Type is not every such F.
    F is Callable<(i32) -> i32>
    return
```

<a id="s-7-3"></a>

## 7.3. レシーバ

<a id="s-7-3-1"></a>

### 7.3.1. インスタンス関数とレシーバ型

struct・enum直下の関数またはContract関数要件は、内部名`self`のパラメーターを持つときだけ**インスタンス関数**、なければ**型関数**。`self`は最大1個で、タスクスロットの後ならどの位置でもよい（[§24.2.1](#s-24-2-1)）。改名・既定値は禁止。

レシーバの正規化型は具体的Semantics一つの`S/Self`で、`raw`以外に限る：`Self`、`ref/Self`、`uniq/Self`、`obj/Self`、`rc/Self`、`arc/Self`、`objref/Self`、`objuniq/Self`。通常の型形成規則に従い、Object SemanticsにはSelfのObjectPayloadが必要。Origin注釈は通常パラメーターと同じ。`raw/Self`、制約の有無にかかわらないジェネリック`s/Self`、`ref/(ref/Self)`などの追加参照層、別の対象型はエラー。virtualはオブジェクト借用レシーバを使う（[§6.4.1](#s-6-4-1)）。

ローカル名前付き関数、group、rootgroupにはレシーバがなく、`self`というパラメーターも通常パラメーター。ローカル名前付き関数は何もキャプチャーせず、外側関数の文脈上の`self`も見えない。

裸の`self`は本体検査前に`self: ref/Self`と確定する省略形。Contract要件と完全特殊化を含むインスタンス関数のレシーバ位置だけで使える。名前付き関数・コンストラクターの他パラメーターには型が必要。アクセサーは独自の省略形を持つ（[§11.3](#s-11-3)）。

```kimi
struct Meter
    var measured: i32 = 0
    func read(self) -> i32 => self.measured           // ref/Self
    func update(self: uniq/Self, value: i32) => self.measured = value
    func total(self) -> i32
        func helper(self: ref/Meter) -> i32 => 1      // Valid: an ordinary parameter.
        return helper(self)
    // func f<s/T>(self: s/Self) => ()                // Error: generic receiver Semantics.

var meter = Meter.init()
let shown = meter.read()     // Shared receiver: implicit borrow.
meter.update(5)              // Exclusive receiver: meter is borrowed implicitly.
```

<a id="s-7-3-2"></a>

### 7.3.2. レシーバの形

レシーバの**形**はそのSemantics。関数群（[§9.6](#s-9-6)）ではレシーバ付き関数だけを数え、形は一つに限る。型の宣言またはContract要件での違反は、引数・ラベル・`when`条件に関係なく宣言エラー。制約から集めた群は使用時に検査する。完全特殊化は元の形を保つ（[§8.13.2](#s-8-13-2)）。プロパティの`get`と`set`は別操作として除外し、[§11.3](#s-11-3)で形を固定する。名前でオーバーロード解決前にレシーバ取得が決まるため、Best Candidateは明示引数だけを比較する（[§10.4](#s-10-4)）。群のタスクスロットの形も一つに限る（[§24.2.3](#s-24-2-3)）。

```kimi
struct Counter
    var value: i32 = 0
    func peek(self) -> i32 => self.value
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self, by: i32) -> i32 => by   // Error: next would have two shapes.
```

<a id="s-7-3-3"></a>

### 7.3.3. メソッド呼出し

`receiver.method(arguments)`はレシーバを先に評価・取得し、宣言された`self`の位置へ渡す。レシーバ式（[§3.4](#s-3-4)）には次表の操作を明記したものとして取得する。明示引数は残りのパラメーターに対応し、ラベルで`self`は渡せない。タスク引数はパラメーターを束縛しない（[§24.2.2](#s-24-2-2)）。排他準備は[§15.6.7](#s-15-6-7)に従う。

| レシーバ型 | 値種の入力`p` | オブジェクト種の入力`p` |
| --- | --- | --- |
| `ref/Self`、`uniq/Self` | 所有Place・一時値は`p@ref`・`p@uniq`。借用値は[§3.4.3](#s-3-4-3)の経路で`p@follow@ref`・`p@follow@uniq` | View Targetが正確な完全Sealed型なら`p@follow@ref`・`p@follow@uniq`（[§13.5.5](#s-13-5-5)）、それ以外は`p@objref`・`p@objuniq` |
| `objref/Self`、`objuniq/Self` | 適用不能 | `p@objref`・`p@objuniq` |
| 基底`B`で宣言 | [§9.6.1](#s-9-6-1)の射影 | 同左 |
| `Self`、`obj/Self`、`rc/Self`、`arc/Self` | 一時値はそのまま。所有PlaceはCopyならCopy、他は取得せず`p@move.m()`が必要。読出し型への参照（[§3.5.3](#s-3-5-3)）は値読出し、それ以外は取得せず、Copyな参照先をCopyするには`p@follow.m()`が必要 | 一時値はそのまま。所有Placeには`p@move`が必要。オブジェクト借用は供給しない（読出しなし） |

経路上のペア層（[§13.5.5](#s-13-5-5)）は借用値同様にたどるが、その経路の値レシーバが得るのは読出し型の値読出しだけ。

次の順で全条件を満たす必要がある。

1. 供給操作が明示操作の規則上有効である。静的記憶領域、ジェネリックSemantics、共有専用`rc`/`arc`、プロパティ権限、ゲッター結果の記憶領域、排他取得も検査する（[§15.2.3](#s-15-2-3)、[§8.14](#s-8-14)、[§13.5.5](#s-13-5-5)、[§11.2](#s-11-2)、[§11.5](#s-11-5)、[§15.1.5](#s-15-1-5)）。
2. 経路のレシーバ互換性を満たす。値経路では完全型が要求型に合う必要がある。オブジェクト借用・基底射影は[§12.6](#s-12-6)、[§12.7](#s-12-7)、[§9.6.1](#s-9-6-1)に従う。`objuniq/T`が`uniq/Self`を満たすのは変換でなく経路規則による。
3. 選択後、保護されるオブジェクト借用・基底射影はObjectCallCompatibleがProvenである（[§12.7](#s-12-7)）。証明がなくても再選択しない。

失敗時に別取得へ切り替えないため、Copyを変更して捨てることはない。表の操作を明記すれば同じ意味となり、別操作ならその意味となる。連鎖呼出しでは前の結果を毎回この表で取得する。

```kimi
struct Point
    Self is Copy
    var x: i32
    func normalize(self: uniq/Self) => self.x = 0
    func view(self) -> ref/Self => self

var q = makePoint()
q.normalize()                   // Supplies q@uniq.
makePoint().normalize()         // A temporary is borrowed exclusively.
// q.view().normalize()         // Error: no exclusive acquisition from a ref result.
let p = makePoint()
// p.normalize()                // Error: p is a let binding; no Copy is modified instead.
```

<a id="s-7-3-4"></a>

### 7.3.4. 非束縛参照

`Meter.update`など型修飾インスタンス関数参照は非束縛。呼出しでは全パラメーターを渡す。レシーバより前の全パラメーターを位置指定した場合は宣言位置で位置指定でき、それ以外は`self:`で渡す。他パラメーターは名前契約（[§7.2.2](#s-7-2-2)）を保ち、参照から作った関数値だけが全て位置指定となる。レシーバは通常引数なので、所有Placeを`uniq/Self`へ渡すには`@uniq`が必要。非束縛関数値はレシーバをキャプチャーしない。呼出しのない`value.method`は束縛済みメソッド値を作らない。

```kimi
struct Meter
    func update(self: uniq/Self, value: i32) => ()
    func mark(x: i32 ! y: i32, self) => ()

var meter = makeMeter()
Meter.update(meter@uniq, value: 5)    // The receiver is positional; value may also be positional.
Meter.mark(1, y: 2, self: meter@ref)  // The receiver cannot be positional after y.
// Meter.update(meter, 5)             // Error: an owned Place at uniq/Self needs @uniq.
```

<a id="s-7-4"></a>

## 7.4. パラメーターの取得の形

関数群内の引数取得は名前と位置で決まり、他引数が選んだ候補では変わらない。

<a id="s-7-4-1"></a>

### 7.4.1. 取得モードと許される場合

パラメーターの**取得モード**は、裸の所有Place引数への要求であり、別名展開、括弧・冗長な`owner`除去後の正規化型の外側Semanticsで決める。名称はSubjectモード（[§15.1.6](#s-15-1-6)）と共通。

| モード | 外側Semantics |
| --- | --- |
| ByValue | `owner`（Core、タプル、固定長配列、関数型）、`obj`、`rc`、`arc`、`raw` |
| Shared | `ref`、`objref` |
| Exclusive | `uniq`、`objuniq` |

置換で外側Semanticsが決まるパラメーターでは、取りうる各Semanticsを明記した場合として判定する。ペアバインダー`s`の`s/U`は`s`の許容集合（[§8.3](#s-8-3)）、関数・外側型の型パラメーター`T`や正規化できない関連型射影は全Semanticsを許す（[§8.1.1](#s-8-1-1)）。

推論は関数自身のスロットを引数の完全型へ束縛する。裸の所有Placeから`owner`以外へ束縛できるのは明示型引数だけ（[§10.1](#s-10-1)、[§10.8](#s-10-8)）。明示型引数はジェネリック列の長さ・種類が同じ宣言だけに届くため、他宣言と比較するとき、`owner`を許す関数スロットには`owner`の場合だけを使う。スロットの射影は他引数から束縛するため全場合を保つ。

<a id="s-7-4-2"></a>

### 7.4.2. 対応するパラメーター

宣言には、レシーバを除く束縛呼出し`value.f(...)`列と、記述位置に含める非束縛呼出し`Type.f(...)`列がある。レシーバなし関数は後者だけ。二宣言のパラメーターは、同じ列の同じ位置番号で位置指定できるか、外部名が同じなら**対応する**。非束縛列のレシーバは先行全パラメーターが位置指定可能なときだけ位置指定でき（[§7.3.4](#s-7-3-4)）、それ以外は`self:`名でだけ対応する。束縛呼出しのレシーバは[§7.3.2](#s-7-3-2)に従う。既定値、個数、`!`位置、ラベル、制約、`when`条件で対応関係は変わらない。タスクスロットは対応せず、番号もずらさない。

<a id="s-7-4-3"></a>

### 7.4.3. 照合キーと重なり

| パラメーター型 | 照合キー |
| --- | --- |
| `U`（`owner/U`）、`ref/U`、`uniq/U` | `Value(U)` |
| `obj/U`、`rc/U`、`arc/U`、`objref/U`、`objuniq/U` | `Object(U)` |
| ペアバインダー`s`の`s/U` | 許容される各Semanticsを明記した場合のキー。`raw`は`Value(raw/U)` |
| 裸の型パラメーター`T`、元の`s/T`、ペア対象単独、正規化不能な関連型射影 | Any：全キーと重なる |
| その他（`raw/U`、Core、タプル、固定長配列、関数型） | 型自身の`Value` |

[§8.9.1](#s-8-9-1)の手順2～4の単一化で結合できるキーは重なる（手順1の制約正規化はしない）。非重複を証明するのは固定構造の不一致だけ。すなわち[§8.2.3](#s-8-2-3)の構造の先頭（異なる名前付き型、タプル要素数、関数型の形、`uniq/Node`と`Node`のような確定Semantics）が異なるか、長さがともに定数で値が異なる場合。スロット、未解決関連型、非定数長は束縛可能なものと重なり、Originは無視する。使用時に推論するスロットは候補ごとに独立、固定外側スロットは共有する。異なるスロットも単一化できるため、外側の`A`・`B`を使う`Array<A>`と`ref/Array<B>`は重なる。型制約は参照せず、完成済みヘッダーで判定する（[§8.6](#s-8-6)）。

<a id="s-7-4-4"></a>

### 7.4.4. 宣言と呼出しの契約

同じ群の二宣言の対応パラメーターごとに、次を行う。

1. スロットを独立に束縛して全許容場合の組合せを調べる。重なる全組合せでモードが同じなら有効。
2. そうでなければ許される相関を仮定して再検査する。相関は同じ構築対象スロット、または長さ・種類が同じ二関数のジェネリック列の同番号スロット（等しい元からの等しい射影）。重なる箇所が一モードになるなら、その相関に依存する契約付きで有効。
3. それ以外は二宣言を併存させられない。

同一スコープの同名宣言（Contractの継承要件も含む、[§8.4.3](#s-8-4-3)）での違反は宣言エラー。使用時に集めた群（[§9.5.1](#s-9-5-1)、[§9.6](#s-9-6)）では引数によらず使用位置のエラー。個数、ラベル、既定値、`when`条件で免除される対はない。完全特殊化は元の契約を保つ。組込み演算子・添字・比較候補（`index`と`indexUniq`）も従う。

手順2の相関に依存する群を明示型引数なしで呼ぶには、個数・ラベル・既定値・制約・条件・期待結果で候補を絞る前に、必要な全相関を確立する。比較する全候補が次の同じ根拠でスロットを束縛するなら相関が成立する。

- 同じ固定の明示束縛または環境束縛。
- 対応パラメーターの、同じ独立型付け可能な引数またはリテラル引数（[§10.5.1](#s-10-5-1)）を、[§10.1](#s-10-1)の同じ推論段階（構造照合または呼出しシグネチャ照合）、同じ構造経路（型構築子と引数位置）で照合したもの。
- 結果型の同じ構造経路に沿う、呼出しの期待結果。

引数型が未解決でもよく、共通の未解決リテラルも相関を作る。その後は通常どおり、リテラルを期待型に合わせて既定型を適用する（[§10.2.1](#s-10-2-1)）。既定値は根拠にならず、いずれかの候補に追加の根拠があれば相関は失敗する。重なる位置で同じ取得モードになる証明で十分で、完全型の一致までは不要。候補除外、変換探索、関連型求解、待機引数の解析は相関を確立しない。相関を示せない呼出しは、明示型引数を書かなければエラー。

重なるキーでは全候補が同じ取得を行い、候補内の参照引数は自身の型に従い取得する（[§10.2.2](#s-10-2-2)）。`@uniq`忘れは唯一の候補でのエラーとなり（[§15.1.5](#s-15-1-5)）、値渡し候補へ黙って切り替わらない。重ならないキーでは引数型が候補を選ぶ。例えば`uniq/(uniq/Node)`と`ref/Node`は別モードでよい。

```kimi
func bump(score: Score ! flag: bool) => ()
func bump(score: uniq/Score ! flag: i32) => ()  // Error: ByValue and Exclusive at position 0.
func g<T>(value: T) => ()
func g(value: uniq/Node) => ()                  // Error: T may be Node.

func show(value: Node) => ()
func show(value: objref/Node) => ()             // Valid: Value(Node) and Object(Node).
func f(value: Node) => ()
func f<T>(value: T) => ()                       // Valid: T has the owner case alone.
func inspect(value: uniq/(uniq/Node)) => ()
func inspect(value: ref/Node) => ()             // Valid: different determined Semantics.

struct Item<T>
    public var value: T
    public init(value: T, code: i32) => self.value = value@move
    public init(value: T, name: string) => self.value = value@move

let item = Item.init(42, 0)   // Item<i32>: both candidates bind T from the same argument.
```

<a id="s-7-5"></a>

## 7.5. 関数制約

関数・コンストラクター・デストラクター・アクセサーの字下げ本体は**先頭領域**から始まる。これは、括弧なし制約句（付随する字下げCallable効果列を含む、[§8.2](#s-8-2)、[§8.11.4](#s-8-11-4)）、`origin`関係句（[§15.3.3](#s-15-3-3)）、元のvirtual関数だけに許す効果項目（[§8.11.1](#s-8-11-1)）が続く最長の先頭部分で、順序は任意。空行・コメントでは終わらない。`(value is Dog)`のような括弧付き検査は実行項目として終わらせる。コンパイル時指令内は全分岐で各項目の種類を数える（[§19.4](#s-19-4)）。先頭領域の後に現れる、ジェネリックパラメーターを根とする制約句は配置エラー、他位置の効果項目もエラー。アクセサーの先頭領域は`origin`関係だけ、デストラクターには先頭領域項目を置けない。

主語は関数・宣言型のジェネリックパラメーター、またはそれを根とする関連型射影（[§8.5](#s-8-5)）。結果を含むシグネチャは先頭制約に依存でき、同一性前提は[§8.2](#s-8-2)に従う。関数要件には専用領域がある（[§8.4.2](#s-8-4-2)）。呼出しの各ジェネリック引数は制約を満たし、本体はそれを仮定できる。制約はシグネチャに含まれず、制約だけが違う宣言は競合する。

宣言型のジェネリックパラメーターを制約するメンバー関数・コンストラクターは**条件付きメンバー**。その句と外側の`when`条件にレシーバ・修飾部の型引数を代入した前提で適用可能性を決める（[§8.8.2](#s-8-8-2)）。Refutedなら名前解決を変えず適用不能、同一性原子の前提エラー（[§8.2.3](#s-8-2-3)）はメンバーで報告し、ジェネリック呼出し側は前提を証明する。対象Contractは不要で、適合も付与しない。

```kimi
func pick<T>(a: ref/T, b: ref/T) -> ref/T during a
    origin b outlives a
    T is Copy                         // Still in the leading region.
    return a

struct Box<E>
    public var item: E
    public init(item: E) => self.item = item@move
    public func same(self, other: ref/Self) -> bool
        E is Equatable
        return self.item == other.item

let yes = Box<i32>.init(1).same(Box<i32>.init(1))  // Valid: i32 is Equatable.
// For a non-Equatable Point, same is inapplicable on Box<Point>.
```

<a id="s-7-6"></a>

## 7.6. unsafe関数

`unsafe func`は型で表せない呼出し側のメモリー安全条件を契約とし、文書コメントの`safety`に記す（[§2.3](#s-2-3)）。呼出しにはUnsafeブロックが必要（[§14.3](#s-14-3)）で、契約違反は未定義動作。宣言しても本体自体はunsafe文脈にならない。

`unsafe`はシグネチャに含まれない。オーバーロード解決はそれと呼出し側のunsafe文脈を無視し、選択後に要件を検査する。直接呼出しだけが可能で、値取得・引数渡し・変換はエラー。Unsafe 関数型は延期機能（[§D](#s-D)）。

```kimi
/// - safety: pointer refers to a live, initialized, readable i32 for the call.
unsafe func read(pointer: raw/i32) -> i32
    unsafe => return *pointer

unsafe func invalidRead(pointer: raw/i32) -> i32
    return *pointer      // Error: the body is not an unsafe context.

let reader = read        // Error: an unsafe function is not a value.
```

<a id="s-7-7"></a>

## 7.7. 関数式

<a id="s-7-7-1"></a>

### 7.7.1. 構文と推論

匿名関数は`func`、任意のキャプチャー列、パラメーター、任意の結果注釈、本体からなる（[§F.9](#s-F-9)）。裸の`(x) => x`、ラベル、既定値、`!`境界、ジェネリック匿名関数はない。タスクスロットはヘッダーに明記したときだけ持つ（[§24.2.1](#s-24-2-1)）。全パラメーターに引数が必要で、呼出しは位置指定。

パラメーター型を省略するには固定された期待呼出しシグネチャが必要（[§10.5.2](#s-10-5-2)）。タスクスロット後の位置に型を対応させ、本体・後の使用から推論しない。結果は期待型から、または入力型確定後に本体から、そのOrigin・Loanとともに得る（[§15.8.2](#s-15-8-2)）。匿名関数は待機引数（[§10.5.1](#s-10-5-1)）なので、本体結果は外側呼出しのスロットを推論する根拠にならず、クロージャー型をスロットに束縛しても別スロットは埋まらない（[§10.8](#s-10-8)）。`try`失敗は結果型候補を供給せず（[§17.3](#s-17-3)）、成功値を自動で包まない。

作成時はキャプチャーだけを評価する。呼出し時に独自のFunction Boundaryの下で本体を実行し、外側の転送先やUnsafe権限は継承しない。

```kimi
let twice = func (value: i32) => value * 2              // Result: i32.
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2                 // Error: no fixed input signature.
```

<a id="s-7-7-2"></a>

### 7.7.2. キャプチャー

| 列・項目 | 作成時の効果 |
| --- | --- |
| 列なし | 本体が使う外側の実行時束縛を各一度Copy。全てCopyが必要 |
| `[]` | 実行時キャプチャーなし |
| `[x, y]` | 列挙した束縛だけ |
| `x` / `var x` | 環境束縛を`let x = x` / `var x = x`同様に初期化 |
| `x@op` / `var x@op` | `let x = x@op` / `var x = x@op`同様。`op`は`move`・`ref`・`uniq` |

束縛の同一性で解決する。列なしではMove、借用、再借用、部分キャプチャーを推論しない。Copyフィールドしか読まなくてもNon-Copyの根は拒否する。既存`ref/T`は依存ごとCopyし、ジェネリックパラメーターに依存するCopyには宣言済みの根拠が必要。

明示項目は一つの束縛を作成時に通常の初期化と同様に取得する（[§3.5](#s-3-5)、[§13.5](#s-13-5)、[§8.14](#s-8-14)）。裸の項目は`uniq/T`・`objuniq/T`を保持する束縛を再借用し、それ以外のNon-Copyは拒否する。`x@ref`・`x@uniq`は外側スロットを借用し、クロージャーはそのスロットに依存する（[§15.8](#s-15-8)）。

対象は外側の実行時ローカル・パラメーターだけ。型名、コンテナー、静的記憶領域、ローカルを含む名前付き関数は対象外。静的フィールド・プロパティの直接使用は静的アクセス（[§15.6.4](#s-15-6-4)）。最上位ローカルも通常のローカル同様にキャプチャーする。文脈上の`self`とセッターの`value`は暗黙キャプチャーせず、明示時はレシーバ・アクセサー規則に従う。コンストラクター・`drop`では`self`を一切キャプチャーできない（[§6.3.4](#s-6-3-4)、[§16.3.1](#s-16-3-1)）。文脈上の`storage`は名前でキャプチャーできない。静的プロパティの`storage`はアクセサー内クロージャーから静的アクセスできるが、インスタンスの`storage`はキャプチャーしない。

明示項目は未使用でも左から右へ実行する。重複とパラメーター名はエラー。推論キャプチャーは選択済みソースでの初出順に各一度実行する。`var`なしは元の可変性によらず`let`相当、値キャプチャーはその時点の値を保存し、`var`は環境束縛の可変性だけを変える。

呼出し中は各環境束縛が自身の`let`/`var`を保つローカルとして振る舞う。Shared呼出しは読取りだけ、Exclusiveは`var`への代入とキャプチャーした`uniq`参照先の使用もでき、環境からのMoveはConsumingだけ。消費した`let`キャプチャーは再初期化できない。環境束縛はパラメーターでないので[§15.1.5](#s-15-1-5)の所有型パラメーター制限は適用しない。環境が別キャプチャーを介して自身の所有キャプチャーを借用することは禁止。

入れ子クロージャーは全外側環境を経て取得する。内側だけで使う束縛も外側でキャプチャーし、各省略列は個別にCopyを要求する。外側環境値を内側へMoveすれば外側呼出しはConsuming。内側の`[x@uniq]`は外側環境の束縛`x`を借用するため、`let`ならエラー、それ以外は外側呼出しの権限で判断する（[§7.7.3](#s-7-7-3)）。

```kimi
group Totals
    public var count: i32 = 0
        get() -> i32
            let read = func () -> i32 => storage   // Valid: a static Property's storage is a static access.
            return read()
```

```kimi
let number: i32 = 10
let copied = func () => number              // Copy capture.
let text = makeText()                       // string: Non-Copy.
// let bad = func () => text                // Error: an explicit list is required.
let holder = func [text@move] () => ()      // The transfer happens even if unused.

var value: i32 = 1
let r = value@uniq
var bump = func [r] () => r@follow += 1     // [r] Reborrows r as uniq/i32.
bump()                                      // An Exclusive call.
```

<a id="s-7-7-3"></a>

### 7.7.3. 呼出しレシーバ

具体的クロージャーは選択済みソースの本体で解決した全操作から、最小の**Call Receiver Requirement**を一つ持つ。

| 要件 | レシーバ | 本体のアクセス |
| --- | --- | --- |
| Shared | `ref/Self` | 環境の読取り |
| Exclusive | `uniq/Self` | 環境またはキャプチャー参照先の変更 |
| Consuming | `owner/Self` | 環境からのMove |

可能な経路のどれかにMoveがあればConsumingが必要。条件付き取得計画（[§8.14](#s-8-14)）の許容場合で効果が異なるなら各場合が固有の要件を持ち、全使用を全場合で検査する（[§8.15](#s-8-15)）。例えばペア層のキャプチャーを裸で取得すると、再借用する場合だけExclusiveになる。

直接呼出しの取得はメソッドレシーバ同様（[§7.3.3](#s-7-3-3)）。所有クロージャー PlaceはSharedなら共有借用、Exclusiveなら貸出し点が排他書込み可能な場合に排他借用する（[§15.1.5](#s-15-1-5)）。`uniq/F`・`ref/F`は再借用し、`ref/F`はExclusiveを供給できない。ConsumingはCopy クロージャーならCopy、それ以外は`c@move()`が必要。

呼出しシグネチャはタスクスロットの有無、レシーバ・パラメーター・結果の型、Origin、結果Loan依存を保持する。タスクスロット付きシグネチャ経由はタスク呼出し（[§24.3](#s-24-3)）。レシーバ保護は後続引数の評価前から始まり、間接・Callable呼出しも同じ（[§15.6.7](#s-15-6-7)）。ジェネリック呼出しは宣言Callableレシーバに従う（[§8.10](#s-8-10)）。

```kimi
let count: i32 = 0
var next = func [var count] () -> i32
    count += 1
    return count
let first = next()        // 1: an Exclusive call; the outer count stays 0.

let item = makeResource() // Non-Copy.
let take = func [item@move] () => item@move
let taken = take@move()   // Consuming: transfers item out.
// take()                 // Error: take was consumed.
```

<a id="s-7-7-4"></a>

### 7.7.4. 関数参照と型消去

関数参照は[§10.5](#s-10-5)で解決し、束縛済みジェネリック引数とOrigin契約を含むFunction Item型を持つ。異なる宣言は異なる型。`drop`は値取得できない。組込みの証拠を含む要件参照（[§13.3.3](#s-13-3-3)）とsafeなコンパイラー実装関数も同じ規則に従い、要件値は選択済み実装を保持する。

固定期待型（[§10.2](#s-10-2)）が共通関数型の位置で、Function Itemまたは具体的クロージャーを暗黙に型消去できるのは、次の全条件を満たす場合だけ。

- シグネチャが合い、タスクスロットの有無が同じ（[§24.2.4](#s-24-2-4)）で、最小レシーバがShared。
- 環境全体がOwnedで、結果が環境を借用しない。
- 公開Origin・Loan契約を全て満たし、元の値の通常取得が有効。

型消去は他の暗黙Adaptationと連鎖しない。借用クロージャーは消去せず（[§3.2.1](#s-3-2-1)）、消去後の値をさらに借用せず、既存構造内部も消去しない。明示取得は先行できる（[§10.2](#s-10-2)）。共通型探索（[§14.9.2](#s-14-9-2)）は型消去しない。修飾Case構築のペイロードも先頭ドット形同様に消去できる（[§6.5.2](#s-6-5-2)）。

型消去は元のCopy・Moveとは別の所有変換で、元を通常取得し（[§3.5](#s-3-5)）、キャプチャーを再実行しない。消去値はNon-Copy。レイアウト、割当て回数、インライン化は実装定義。割当ての省略は値・Move・Loan・後始末を保存し（[§15](#s-15)）、失敗不能。実際に割り当てて失敗した場合はAbortし、Moveを巻き戻さない（位置は[§22.5.1](#s-22-5-1)）。Callableを期待する位置への具体的クロージャーの受渡しは型消去ではない。

```kimi
func inc(value: i32) -> i32 => value + 1
func twice(action: ref/((i32) -> i32), value: i32) -> i32 => action(action(value))

let text = "hello"
let closure = func [text@move] () => ()
let erased: () -> () = closure@move                    // Transfer, then erasure; closure is Moved.
// let view: ref/(() -> ()) = func () => ()             // Error: erasure cannot then borrow.

var handler: (i32) -> i32 = inc                        // Erased: Non-Copy.
handler = inc                                          // Erased again; the old value is destroyed.
let next = handler@move
// let mixed = if ready => next@move else => inc       // Error: no common Function Type.
// let result = twice(inc, 1)                          // Error: erasure and a borrow are two operations.
```

<a id="s-8"></a>

# 8. ジェネリクス・制約・Contract

制約は、ジェネリック本体が仮定でき、使用側が証明すべき条件を定める。Contractは能力に名前を付け、要件と実装を対応させる。明示的特殊化は実行する実装だけを変え、呼出し契約は変えない。

<a id="s-8-1"></a>

## 8.1. ジェネリックパラメーター

<a id="s-8-1-1"></a>

### 8.1.1. 型スロットとペアパラメーター

`<T>`と`<s/T>`は、それぞれ完全な型引数Wを1個取り、Semantics・入れ子型・Originを保つ。長さは`<length N>`で、`<N>`は型スロット。Originは独自のスキーマで宣言する。

TはCoreだけでなくW全体を束縛する。ペアはWの外側を分け、sがペアバインダー（Semantics）、Tがペア対象（直接の対象）、元のs/TがWを表す。安全な借用の場合の外側Origin oはソース名を持たず、本体内ではバインダーごとに固定され、元のs/Tの全出現で共有する。借用を許す場合だけ存在する。

分解前に別名、完了した射影、括弧、冗長なownerを正規化し、別名循環はエラーとする。`ref/(uniq/i32 during b) during a`ならs=ref、T=uniq/i32 during b、外側Origin=a。`owner/(ref/i32 during a)`の外側もrefである。

| 外側 | ペア対象・別のUにsを適用する条件 | 外側Origin |
| --- | --- | --- |
| owner | W自身。owner/UはU | なし |
| ref・uniq | 完全な参照先型。uniqには排他取得とLoanも必要 | あり |
| obj・rc・arc | Object Target | なし |
| objref・objuniq | Object Target | あり |
| raw | 完全な参照先型。寿命保証なし | ペアではなし。Origin付きraw引数はOriginを落として合う。具体的raw層では指定可能 |

外側Originがなくても、ペイロード内部の依存は消えない。ペア対象Tを単独の値型（Option&lt;T>など）として使うには、sの全許容場合で値型である必要がある。owner・ref・uniq・rawでは成立するが、オブジェクト系列を含むならT is ObjectPayloadの証明が必要。名前解決でTを選んだ後の型合法性として検査する。

ジェネリック対象のオブジェクト型形成には、シグネチャでも本体でもObject Targetの証拠が必要。元のTについては、sの許容集合全体がオブジェクト系列ならペアの証拠で足りる。別のUへsを適用するなら、集合がオブジェクト系列と交わる場合にUのObject Target証拠が必要で、全許容Semanticsで有効な型を形成する。書かれたシグネチャが整形式であることから証拠を逆算しない。

同じリストの名前はすべて異ならなければならず、`<T,T>`、`<s/T,s/U>`、`<s/s>`はエラー。`<s>`は通常の型スロットで、独立したSemanticsスロットはない。

```kimi
func keep<s/T>(x: s/T)
    s is ref or uniq
    let empty: Option<T> = .None      // Valid: T is a Referent Type in both cases.

func lose<s/T>(x: s/T)
    let empty: Option<T> = .None      // Error: s may be obj, and T is not proven ObjectPayload.
```

<a id="s-8-1-2"></a>

### 8.1.2. ペアの再構成とOrigin注釈

元のs/Tは宣言時の束縛でWを指す。実体化後の型等価性で元かどうかを決めない。別のUへのs/UはSemantics種別だけを適用し、Wの外側Originをコピーしない。

- `s/T during b`・`s/U during b`はその出現の外側Originスロットを束縛し、借用の場合だけ有効になる。型の形成自体は元Originとbを関連付けず、値の変換も行わない。Origin注釈は再借用を作らず、既存の再借用も除去しない。値を合わせる時に短縮・取得・Loanを検査する。
- 注釈なしs/Uには位置ごとの省略規則を記号的に適用する。直接入力は独立した外側Originを得て、借用の場合だけ有効になる。フィールド・入れ子借用の省略を新たに許可しない。
- ペア層をたどる場合は、その出現の外側Originを使い、各依存は生成するSemanticsの場合だけ有効とする。

型形成と本体の合法性は全許容束縛で成立しなければならない。実体化は正準契約へ代入するだけで、不足証明を発見して補わない。

<a id="s-8-1-3"></a>

### 8.1.3. 型引数

型引数は有効な完全な型、または定義のパラメーターに依存する型。プリミティブ、struct、enum、Unit、タプル、共通関数型、安全な借用、オブジェクト、raw、Neverを含む。Function Item・クロージャー型は推論または既存の参照形式から得る。Contract、group、Semantics、値は型引数でなく、Never式だけでは未束縛型を推論できない。

明示リストは全スロットを順に供給し、末尾カンマを許す。リスト全体を省略すると各構文の推論を使う。部分リスト、`_`、既定引数、可変個数、Const genericsはない。`<s/T,U>`は完全な型を2個取る。

有効な型引数でも、基底型・関連型・レイアウト・Copy導出・ペイロードの各役割の条件を別途満たす。通常の格納はOwnedを要求せず依存を保持するが、静的格納はOwnedを要求する。

```kimi
func choose<T>(a: T, b: T) -> T => b@move

let y = choose<Option<string>>(.None, .Some("a"))
// let z = choose<i32, i32>(1, 2)   // Error: choose has one Type slot.
```

<a id="s-8-2"></a>

## 8.2. 制約と要件式

<a id="s-8-2-1"></a>

### 8.2.1. 制約節

`subject is requirement`が制約節。宣言の全制約を満たし、本体はその証明結果を利用できる。対象は役割に応じた完全な型、ペアバインダー、ペア対象、関連型射影、Selfで、宣言種別による制限にも従う。右辺はContract等の能力、完全な型による同一性、ペアバインダーならSemantics・カテゴリーを示す。

struct・enumでは制約をメンバーより前に置き、条件付き適合はメンバー位置に置く。型宣言のSelf is Cは適合宣言であり、組込みContractと特別な否定Self節は[§8.7.1](#s-8-7-1)に従う。

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Labeled<T>
    T is Describe
    Self is Describe         // Labeled<T> conforms; nothing is derived from T is Describe.
    var value: T
    public func describe(self: ref/Self) -> string => self.value.describe()
```

<a id="s-8-2-2"></a>

### 8.2.2. 要件式

制約と関連型宣言・指定のisはコンパイル時の検査で、実行時検査や#if・#caseには使わない。and・or・not・括弧で組み合わせる。is直後のnotは右辺全体を否定する。

| 形式 | 意味 |
| --- | --- |
| T is A and B／T is A or B | 両方／いずれかを満たす |
| T is not A or B | どちらも満たさない |
| T is (not A) or B | Aを満たさない、またはBを満たす |

`(T is A) and enabled`のように通常のbool式へ混ぜられない。

<a id="s-8-2-3"></a>

### 8.2.3. 型同一性とその前提

完全な型Uに対するX is Uは型の同一性を要求し、変換・部分型・Copy・適合とは異なる。同一型の既知の能力は通常の証拠として利用する。

**前提。** スコープ内の型同一性は、次の根拠を含み、反射・対称・推移、任意の型内部での同一型への置換、構造分解に閉じた最小の関係とする。根拠は、利用可能な肯定の型同一性前提（連言の分解、外側宣言の入力を含む）、T is Cが供給する制約・関連型要件、利用可能な完了済み関連型束縛。必要な射影の深さ、綴り、同一性を経る経路には依存しない。

or・not内部の原子、本体、呼出し、実体化、義務（宣言中の型の適合など）は根拠にしない。適合型の関連型指定は[§8.5.1](#s-8-5-1)に従う。スコープは制約付き宣言、または条件付き適合のスコープとブロックである。同一型は型検査全体で同じ型として扱い、構築された型へのContract事実も共有するが、同一性が新しい適合宣言やメンバーを作るわけではない。名前解決は自身の規則に従う。

型パラメーターは1つの型を表し、パターンではない。前提の左右・順序・方向・綴りや検査順は意味を変えず、矛盾から別の同一性は導かない。宣言名に依存し得る資源上限だけは[§8.3.3](#s-8-3-3)の例外となる。

**構造。** 別名・括弧・冗長owner・完了射影を解釈し、名前を宣言へ束縛したうえで、次の表を使う。

| 形式 | 先頭の識別（head） | 構成要素 | 比較する属性 |
| --- | --- | --- | --- |
| プリミティブ・Never | 型自身 | なし | なし |
| 名前付き型（struct・enum・ライブラリー型・入れ子型・Wrapping） | 外側を含む宣言 | 自身と外側の型引数。未使用引数も含む | Origin・束縛集合 |
| タプル（Unitは0要素） | 要素数 | 各要素 | なし |
| 固定長配列 | 固定長配列 | 要素型 | 正規化済み長さ |
| Semantics層k/V during o | k | V | o |
| ペア適用s/V during o | 許容集合が単一ならそのk、それ以外は未確定。同じsは共有 | V | o |
| 共通関数型 | taskスロットの有無、引数数 | 引数型・結果型 | 固定Originと量化構造（個数、出現、関係。改名は無関係） |
| Function Item・具体的クロージャー | 関数宣言・式の出現位置 | 同一性に含まれる実体化型引数 | 長さ引数・Origin |
| 抽象型（型パラメーター、ペア対象、ContractのSelf、未解決射影） | なし | なし | なし |

最後以外を構築された型といい、struct・enumのSelfも含む。フィールド・キャプチャー・メソッド集合は構成要素ではない。異なる宣言の型は構造・レイアウト・値が同じでも別で、共通関数型へ変換できるFunction Item等もその型とは同一でない。

抽象型は束縛・完了射影・許容集合で形が確定する。未確定ペアのheadは、許容集合が相手の外側Semanticsと交わらないときだけ異なると証明できる。ownerを許せばs/VはV自身になり得て、どの形とも交わる。関数型内部で量化したOriginは内部だけで対応し、外側Originとは同一にならないが、量化を保つ関数型全体の同一性は可能。実行時ContractビューはContract宣言をhead、型引数と公開関連型束縛を構成要素とする。

**分解。** headが同じと判明し、属性一致を証明した2つの構築型は、対応する全構成要素が同一である場合に限り同一。前提、推移で得た同一性、関連型束縛に共通し、1型の複数束縛はすべて成立するものとして扱い優先しない。

**属性。** 分解からOrigin関係、長さ等価、Semantics事実を新たに導かない。型を同一視するなら、属性とペアheadの一致を、当該同一性やそれに依存する事実を使わず、各領域の前提から証明する。`[4 of U]`と`[(2+2) of i32]`からU is i32は導けるが、NとMの同一性は導けない。ref/U during aとref/i32 during bの分解にはa==bが必要で、一方向のoutlivesでは足りない。

一致未証明は不一致ではない。問い合わせならUnknown、前提なら未証明要件で、全前提を見て判定する。Semanticsの場合で真偽が変わる前提も未証明とする。制約内の関数型で省略した入力Originは通常のパラメーター省略規則に従う。

**射影。** X.(C).A(o)の同一性は、対象、関連型宣言、束縛された定義Contract、Origin引数で決まり、継承経路や名前では決まらない。同一対象・Contract引数・Origin引数の同じ射影は同一だが、射影の同一性から対象や引数の同一性を逆算しない。Contract引数自体も分解せず、T is K&lt;U>とT is K&lt;i32>だけではU is i32を導けない。

構築型またはそれと同一の対象への射影は、その宣言済み適合の束縛へ型・Origin引数を代入したものとなり、構築結果は分解できる。束縛利用には適合経路の前提が必要で、条件付き束縛を別スコープで無条件に使わない。異なるOrigin引数の適用は、Contractが関連付けない限り同一視しない。ペア対象の同一性から完全な型・外側Semanticsは復元できない。

**Refuted。** 型引数未束縛でも、対応位置の既知head・属性が異なる、対応構成要素がRefuted、または片方が他方の真部分と同一ならRefutedとする。ただし真部分への経路が、ownerを許すペア適用だけの場合は除く。同一性を仮定して矛盾を探す証明はしない。未解決射影・別の型パラメーターを見た目の違いだけでRefutedにせず、複合同一性の否定からどの構成要素が違うかも選ばない。

**循環。** 真部分は、同一性を考慮しつつ構築型の構成要素を1段以上たどって得る型。未確定ペア適用もたどるが、射影の対象・引数、名目的型のフィールドはたどらない。T is UとU is Tは同一視するだけ、T is Option&lt;T>はエラー。T is T.(C).XやT is Option&lt;T.(C).X>だけではエラーでないが、後者にT.(C).X is Tが加わると循環する。名目的レイアウト・Contractの再帰・自己依存証拠は別規則に従う。

| 前提の問題 | 診断 |
| --- | --- |
| 同一にした型の同一性がRefuted、関数型内の量化Originを外へ持ち出す場合も含む | Language |
| 型が自身の真部分と同一 | Language |
| 比較属性の一致が未証明 | Proof（未証明要件） |
| ペアhead未証明など、真偽がSemanticsの場合で変わる。ownerを含むペアだけを通る循環も含む | Proof（未証明要件） |

原因前提が最初に集まる宣言で1回だけ報告し、使用側で繰り返さない。Contract自身の制約だけが原因ならContract、それ以外は関数・型・条件付き適合スコープ・条件付きメンバーで報告する。その他の矛盾は、直接得たか問い合わせた命題についてだけErrorとなる。

診断には競合構造・循環と結び付けた前提、未証明なら不足要件を示す。診断・hoverは記述された型に加え、同一性を使った判定では使用した同一型と根拠を示す。表示上どの型を選ぶかに意味はない。Unknown同一性を前提へ追加する修復は、Refutedでなく前提エラーを生まないことを前提とし、その要件がProvenになり、呼出し側に証明義務が生じることを保証する。

```kimi
func same<T, U>(value: U) -> i32
    T is Option<U>
    T is Option<i32>
    return value                    // Valid: U is i32.

func conflict<T, U>(value: T) -> i32
    T is Option<U>
    T is Array<U>                   // Error: two different nominal Types are identified.
    return 0

func otherView<T, U>(first: ref/U during a, second: ref/i32 during b, value: T) -> i32
    T is ref/U during a
    T is ref/i32 during b           // Error: a == b is not proven; a repair adds origin a == b.
    return 0

func independent<T, U>(left: T, right: U) -> i32
    T is Source
    U is Source
    T.(Source).Element is U.(Source).Element
    return 0                        // The Elements are one Type; T and U are not.

func cyclic<T, U>(value: T) -> i32
    T is Option<U>
    U is Option<T>                  // Error: T would contain itself.
    return 0
```

<a id="s-8-3"></a>

## 8.3. 制約の証明

<a id="s-8-3-1"></a>

### 8.3.1. 命題と判定結果

証明は本節の規則だけで行い、SAT、一般定理証明、型の列挙、最適化の事実を使わない。完全に決まった具体的条件はbool評価する。Callable効果境界は命題ではない。

要件の優先順位を先に適用し、T is (A and B)を2命題の連言とする。命題は綴りでなく束縛Symbolと正規化した完全な型で識別する。括弧、二重否定、同じheadの構築型同一性の属性・構成要素への分解以外の論理正規化はしない。

| 結果 | 意味 |
| --- | --- |
| Proven | 規則で証明できる |
| Refuted | 否定を証明できる。単なる証明不足ではない |
| Unknown | どちらも証明できない |
| Error | 名前・対象・宣言が不正、または証拠が矛盾する |

証拠は入力制約、組込み能力、検証済み適合から得る。環境指令は証拠を供給しない。入力制約は本体で仮定し、使用ごとに証明する。必須制約の成功はProvenだけ。

<a id="s-8-3-2"></a>

### 8.3.2. 証明規則

| 規則 | 導出 |
| --- | --- |
| 完全一致の仮定 | PがあればPをProven、not PがあればRefuted |
| 連言 | 利用可能なP and Qから両方を得る。両方Provenなら連言もProven、どちらかRefutedならRefuted |
| 選言 | どちらかProvenならProven、両方RefutedならRefuted。もう片方の証拠は増えない |
| 否定 | ProvenとRefutedを交換 |
| 具体的判定 | 具体的型同一性、Semantics、カテゴリー、組込み能力、閉世界規則 |
| 許容集合 | ペアバインダーは[§8.3.4](#s-8-3-4)で判定 |
| 検証済み適合 | 明示・条件付き・継承適合の実装が特定され互換なら証拠。効果境界は別検査 |
| Contractの拡張 | T is Cから祖先適合と自身・継承制約を、SelfをTに置換して得る。関連型要件も必要な全射影に供給する |
| 型・関連型同一性 | 完了束縛を代入して[§8.2.3](#s-8-2-3)で判定。メンバー探索や型の選択はしない |
| 閉じた含意 | PrimitiveIntegerからPosition |
| その他 | Unknown |

Errorは他の結果に優先する。ErrorでなければUnknownを含んでも、Refutedは連言を否定し、Provenは選言を肯定する。P or Qとnot PからQは導けず、場合分け・背理法はない。直接のPとnot P、または問い合わせた命題の両極性の証拠はError。

未束縛型・未解決前提を否定の根拠にしない。適合なしをRefutedにできるのは、宣言・継承・条件付き適合・前提の全経路が完了して排除された場合だけ。名前探索の失敗は証明ではない。プリミティブ適合は固定されているため、規定以外の解決済みContractへの適合はRefuted。

```kimi
func pick<T>(value: T) -> T
    T is Copy or Describe
    return value     // Error: Copy or Describe does not prove Copy, so value needs @move.
```

<a id="s-8-3-3"></a>

### 8.3.3. 再帰と判定の確定

適合は自身を証拠にできず、証明中の義務への再訪は証拠を与えない。余帰納は仮定しない。再帰宣言自体は有効で、Owned等の構造解析は専用規則を使う。

T is Cが直接または供給されたContractの制約を通じて、Tの射影のC宣言（型引数は任意）またはその子Contractへの適合を供給するなら、Cは再帰的である。拡大型再帰や無限展開型でなければ宣言は有効だが、具体型の適合は証明できない。

**拡大型再帰。** 相互参照するContract間で、KのSelfまたはその射影に供給するMの参照を調べる。Kの自身・外側の各型パラメーターPが、Kの前提下で正規化したM引数（射影のContract引数も含む）に現れるなら引継ぎとする。引数がPそのものなら平坦、それ以外なら拡大的。拡大的な辺を含む循環は、上限によらずその循環上の各ContractのLanguageエラーで、参照を関連位置に示す。抽象利用を失うのは具体型が適合できないContractだけ。

**無限展開型。** 型の展開は、抽象型を同一の構築型で再帰的に置換し、構成要素と射影のContract引数に入るが、射影の対象には入らない。有限展開を持たない型を生む前提はLanguageエラー。ただし全無限展開がownerを許すペア適用だけを通って反復する場合は未証明要件。真部分循環のほか、`T is Option<U.(K<T>).X>`のような射影引数経由も該当する。

**資源上限。** 同じ対象からの射影列の等式も許す。証明と前提の閉包には実装定義の上限を置ける。到達前に得た同一性と、それによるProven・Errorは保ち、上限に達した解析に依存するRefuted・Unknownだけを資源失敗にする。Unknown・Refutedとして代用しない。到達した宣言で上限名を1回報告し、依存宣言の資源失敗を関連付ける。到達有無は検査順に依存しないが、宣言名には依存し得る。

**確定。** 判定は、自身に依存しないプログラムの全事実を使い、宣言の検査順で変わらない。判定を保留したとき成立しなくなる事実は、記録された導出方法によらず自己依存で、証拠に使わない。必要箇所のUnknownは未証明要件エラーで、受理もRefuted扱いもしない。ジェネリック定義は宣言前提で検査し、実体化で意味上の条件追加、名前解決・オーバーロード選択の再実行はしない。実体化に依存するのはレイアウト・表現義務だけ。

```kimi
contract Tree
    associate Child is Tree      // Valid declaration.

struct Node
    Self is Tree                 // Error: proving Node is Tree needs Node is Tree.
    associate Tree.Child is Node
```

```kimi
contract Nested<A>
    associate Inner is Nested<Option<A>>  // Error: the argument grows; Nested<A> would be flat.

contract Stream
    Self.(Stream).Head is Option<Self.(Stream).Next.(Stream).Head>  // Error: Head has no finite expansion.
    associate Next is Stream
    associate Head

contract Window
    Self.(Window).Next.(Window).Head is Option<Self.(Window).Head>  // Valid: Self.(Window).Head stays abstract.
    associate Next is Window
    associate Head
```

<a id="s-8-3-4"></a>

### 8.3.4. Semanticsの許容集合

ペアバインダーsの前提は、9種のSemantics上の集合を定める。名前は単一要素、カテゴリーは所属要素、and・or・notは共通部分・和集合・補集合、別の節同士は共通部分となる。前提なしは9種すべて。

s is Rは許容集合がR内ならProven、交わらなければRefuted、それ以外はUnknown。空集合はError。この規則でペア証拠、ペア上のWeak、ペアの借用注釈を含む全Semantics要件を判定する。

```kimi
func peek<s/T>(value: s/T) -> i32
    s is object                         // Admitted set {obj, rc, arc}.
    let view: objref/T = value@objref   // Valid: pair evidence for T.
    return 0
```

<a id="s-8-4"></a>

## 8.4. Contract

<a id="s-8-4-1"></a>

### 8.4.1. Contract宣言

contractは関数・プロパティ要件、関連型、制約で能力を定める宣言コンテナーで、実装・記憶を持たない。Selfは適合型を表す。通常は静的で、実行時ビューは[§8.12](#s-8-12)。型パラメーターを宣言でき、外側環境を継承するが、Origin・長さパラメーターは持てない。異なる引数のIndexable&lt;isize>とIndexable&lt;string>は別の束縛済みContract参照となる。

```kimi
contract Source
    associate Element
    func read(self: ref/Self) -> Element

contract Sized
    property count: i32 has get

contract SizedSource: Source, Sized
    func reset(self: uniq/Self) -> ()

contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self
```

<a id="s-8-4-2"></a>

### 8.4.2. 関数要件

関数要件は名前、任意のジェネリックパラメーター、暗黙のシグネチャOrigin、任意のtaskスロット、型付きパラメーター、任意の結果型を持つ。結果省略はUnit。レシーバがあればインスタンス関数、なければ型関数。ヘッダー後の字下げ領域には、自身のジェネリックを根とする制約・Origin関係・効果節を置ける。これらは互換性検査の前提であり、実装へ制約を追加しない。

継承を含む同名のレシーバ付き要件はレシーバ形を、全同名要件はtaskスロット形を共有する。!境界は使えるが既定引数はなく、要件経由の呼出しは要件側の名前省略許可に従って全通常引数を渡す。アクセス修飾子は使わず、プロパティ要件はhasで宣言する。

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

<a id="s-8-4-3"></a>

### 8.4.3. Contractの拡張

`contract C: A,B`は束縛済みの親Contract参照を拡張し、要件・関連型・制約を継承する。親順による優先はなく、循環はエラー。Contract内の利用者ContractへのSelf is Cは拡張宣言にならない。

子への適合は最終適合型をSelfとする全祖先適合を含む。子は追加できるが削除・弱化できない。同じ祖先宣言へ同じ束縛で至る経路は1要件にまとめ、独立した同名宣言は別のままにする。1実装が複数要件を満たしてよい。

要件を別々の実装として共存させることも、1実装で満たすことも不可能なら拡張はエラー。ラベルだけではオーバーロードを区別せず、結果型だけの違いでは結果互換性があるため競合と断定しない。Unknownも競合の証拠ではない。

<a id="s-8-5"></a>

## 8.5. 関連型

<a id="s-8-5-1"></a>

### 8.5.1. 宣言と指定

関連型は完全な型を表し、束縛・射影・展開でOriginを再束縛したりstaticを補ったりしない。

| 形式 | 意味 |
| --- | --- |
| associate Element | Contractで宣言。適合型ではisがないためエラー |
| associate Element is R | Contractでは要件付き宣言。適合型では一意な同名関連型の指定・制約 |
| associate C.Element is R | Cで宣言した関連型を指定・制約 |
| associate LentItem(step) ... | Originパラメーターを持つ関連型 |

適合型のRを連言分解して型同一性原子が得られれば指定となり、なければ別箇所の指定・推論に委ねる。いずれも完了型はR全体を満たす。

指定とContract・祖先の同一性制約は、未確定関連型を決めるためだけの前提で、構造分解も含む。未確定関連型を含まない入力同士・入力と具体型の同一性等は適合の義務であり、仮定せず証明する。関連型は指定、制約、継承束縛、推論で一意に決め、未確定ならエラー。

修飾Cは型引数を持つ直接適合か祖先。無修飾名は全適合・拡張経路で同名関連型が一意な場合だけ使える。同じ宣言・同じ束縛への経路は1つと数え、C&lt;i32>.ElementとC&lt;i64>.Elementが両方あるなら修飾が必要。

```kimi
contract Destination
    associate Element

struct Pipe
    Self is Source
    Self is Destination
    associate Source.Element is i32
    associate Destination.Element is string
    public func read(self: ref/Self) -> i32 => 42

struct A
    Self is Source
    associate Source.Element is i32 and Copy   // Specifies i32; Copy is checked.
    public func read(self: ref/Self) -> i32 => 0

struct B
    Self is Source
    associate Source.Element is i32 or i64     // Leaves Element to inference; the result must be i32 or i64.
    public func read(self: ref/Self) -> i64 => 0
```

```kimi
contract Wrapped
    associate Inner
    associate Outer is Option<Inner>

struct Number
    Self is Wrapped
    associate Wrapped.Outer is Option<i32>     // Valid: decomposition determines Inner as i32.

contract Holder
    associate Item

struct Pack<T>
    Self is Holder
    associate Holder.Item is Option<T> and Option<i32>  // Error: T is i32 is an obligation that cannot be proven.
```

<a id="s-8-5-2"></a>

### 8.5.2. 射影

T.(C).ElementはTの束縛済みC適合の関連型、T.(C).LentItem(a)はOrigin引数を適用した型。Contract外のT.Elementは利用可能な制約で一意な場合だけ使える。Contract内の短い名前は自身または継承した関連型を指す。

CはContract名解決で探し、T.C.Elementは通常のパス。適合証拠は必須で、名前探索では得られない。基底はCore、型パラメーター、Self、別の射影でよい。結果はSemanticsを保つ型で、関数先頭の制約はシグネチャの射影にも適用する。異なる成功解釈が複数あれば曖昧。

```kimi
func readOne<T>(source: ref/T) -> T.(Source).Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.(Source).Element is i32
    return source.read()
```

<a id="s-8-5-3"></a>

### 8.5.3. 継承した関連型への制約

子Contractは祖先Cに対するSelf.(C).Element、または一意な短い名前で継承関連型を制約でき、新しい型は宣言しない。`associate Parent.LentItem(a) is E`は親の定義域の全aについて成立する制約となる。Selfのさらに深い射影にも制約を置けるが、各段で適合証拠が必要。その条件は適合Selfへの実装要件であり、T is Cから供給される。Loanの独立性は別に検査する。

```kimi
contract OrderedSource: Source
    Self.(Source).Element is Equatable

contract IntSource: Source
    Self.(Source).Element is i32    // Conformances need not repeat the binding.
```

<a id="s-8-5-4"></a>

### 8.5.4. Originパラメーター

関連型の括弧内の単純名はOriginパラメーターで、スコープはその右辺・形成型・付属節だけ。兄弟要件には届かず、メソッド内の同名Originは別バインダーとなる。

型文脈のLentItem(a)はOrigin原子を位置順に代入して完全な型を表す。共通部分はLentItem((a and b))と書く。引数数は一致させ、適合型の指定も同じ個数を再宣言する。名前変更は契約を変えない。未適用の型族、_、引数省略はエラーで、未適用型族は型引数にも別名対象にもできない。型文脈で名前付き型の直後の括弧だけが適用で、Originパラメーター付き関連型以外への適用はエラー。波括弧は束縛集合名を表す。適用結果には通常の変性・短縮・再借用検査を行う。

定義域はContractと付属origin関係で決める。固定右辺はその整形式条件を公開する。たとえばref/E during aならOwnedOrigins(E)がaをoutlivesする。能力要件は型を固定しないが、末尾の`for Type`で形成型の整形式条件を公開でき、そのTypeは外側制約から証明可能でなければならない。等式は右辺の未束縛Originを束縛し、残る関係が定義域になる。右辺・条件がなければContractが許すすべてのOriginを受け入れる。

使用側は代入後の条件を証明する。ペア層の外側Origin条件は、そのOriginがある場合だけ公開する。実装は右辺束縛後の残条件を証明し、呼出し条件を追加しない。OriginパラメーターはLoanも実行時表現も作らない。

```kimi
contract Lender<E>
    associate View(a) is ref/E during a
    associate LentItem(step) for uniq/Self during step
    func lend(self: uniq/Self during step) -> Self.LentItem(step)
    // func bad(self: ref/Self) -> Self.LentItem   // Error: the family is unapplied.
```

<a id="s-8-5-5"></a>

### 8.5.5. 推論

struct・enumの適合では、Originパラメーターなしの関連型指定を省略し、実装の宣言された結果から推論できる。実体化時には行わず、同じ明示束縛と同じ契約になる。

**前提の固定。** 各適合経路について、指定、Contract・祖先の同一性制約、継承束縛と、[§8.5.1](#s-8-5-1)で前提とされる分解結果を先に固定する。経路全体で有効な前提だけを使い、個々の要件・実装の前提で束縛を決めない。関連型は適合型、関連型宣言、束縛済み定義Contractで識別し、同名だけでは同一にしない。条件付き同一性を別経路で無条件化しない。固定同一性は前向きに代入する。

現在の定義は1つのstruct・enumとその適合で、入れ子型は別定義。自身の推論値を固定前提へ戻さない。他定義の値は、その値が決まれば他の値の完了を待たず使える。ただし自身の全推論値とそれらから推論した他定義の値を保留しても同じ値を決める証拠でなければ、自身の証拠にしない。

**実装の特定。** 固定前提下の照合キーで1回特定し、結果・Origin・制約・効果で候補を絞らない。候補キー比較が未確定関連型に依存する場合だけpending、それ以外はunique・missing・ambiguousのいずれか。uniqueだけが証拠を供給する。完了後に再特定するのはpendingだけで、互換性検査に使う。missing・ambiguousは確定結果。関数結果（index・indexUniqを含む）は根拠になるが、プロパティ・アクセサー結果、本体、呼出し箇所、実体化はならない。省略結果はUnit。

**抽出。** 正規化した結果のうち未確定関連型へ至る構造だけをたどる。未確定位置は実装の完全な型を受け取り、関数型全体も受け取れる。同じ名前付き型・タプルでは対応引数、長さ一致済み固定長配列では要素、具体的Semanticsでは同じ層をたどる。関数型内部、未解決射影、未解決Semantics適用には入らない。Originを含む全情報を保持し、全バインダーが適合スコープで有効でなければならない。メソッドの型パラメーター、呼出しごとのOrigin、省略レシーバOriginは使えない。他の結果部分は通常の互換性検査だけで、Origin方程式、変換、Copy判定、能力を満たす型の探索はしない。

**完了。** 別経路の証拠は、宛先前提が元前提を証明する場合だけ使う。1つの関連型の全証拠は完全な型として一致しなければならず、Originを含む不一致はエラー。証拠がなければ明示指定が必要。完了後は全通常検査を行い、失敗で実装を選び直さない。明示束縛では結果互換性によるOrigin短縮を使えるが、推論でその型を探さない。継承束縛は再推論せず、推論予算で受理を変えない。上限到達は資源失敗とする。

```kimi
struct NumberSource
    Self is Source
    public func read(self: ref/Self) -> i32 => 42   // Infers Source.Element is i32.

contract P
    associate X
    associate Z
    func x(self: ref/Self) -> Self.X
    func z(self: ref/Self) -> Self.Z

contract Q
    associate Y
    associate W
    func y(self: ref/Self) -> Self.Y
    func w(self: ref/Self) -> Self.W

struct PA
    Self is P
    public func x(self: ref/Self) -> PB.(Q).Y => 0   // PA.X is i64, from PB.Y.
    public func z(self: ref/Self) -> i32 => 0

struct PB
    Self is Q
    public func y(self: ref/Self) -> i64 => 0
    public func w(self: ref/Self) -> PA.(P).Z => 0   // PB.W is i32, from PA.Z.
```

<a id="s-8-6"></a>

## 8.6. 適合

<a id="s-8-6-1"></a>

### 8.6.1. 適合宣言と同一性

型宣言のSelf is Cが適合を宣言し、同名メンバーがあるだけでは適合しない。適合同一性は、完全な適合型と束縛済みContract参照（宣言、自身・外側の正規化型引数）の組。異なる型引数の適合は別で、関連型束縛では区別しない。拡張が含意する同じ適合と明示適合は1つで、冗長な明示は許す。Contract同一性自体は宣言だけを識別する。同じ束縛参照の直接適合を2回書けば条件によらず重複となる。

ジェネリックの無条件適合は全許容束縛で成立する必要があり、実体化成功で正当化しない。祖先適合・対応も検証する。組込み導出と標準プロパティ証拠の橋渡し以外の実装を自動生成しない。要件とメンバーの対応・関連型束縛は適合で固定し、呼出し側や実体化後に名前を探し直さない。外部登録、適合の置換、既定実装はない。

```kimi
contract C

struct Twice<T>
    Self is C
    Self is C when T is Copy        // Error: the same bound reference twice.
```

<a id="s-8-6-2"></a>

### 8.6.2. 継承適合

Bの検証済みC適合をD:Bが継承できるのは、要件側SelfをDへ置換した全要件を元の対応が満たす場合だけ。Aで宣言した実装側Selfは、A→B→Dでも基底経路の代入をしたAのまま。資格のある借用レシーバだけが基底部分射影を使え、通常引数の基底変換や所有レシーバのスライシングは増やさない。射影呼出しにはObjectCallCompatibleのProvenが必要。

借用レシーバだけにSelfが現れる場合、他検査を通れば継承できる。other:ref/Self、結果Self、所有Selfレシーバは継承できない。同じ型へ正規化するSelf.Elementは継承可能。

失敗経路があってもD自体は不正にならず、全明示・条件付き・拡張経路の失敗が証明された場合だけ適合をRefutedとする。成功経路は一致する必要がある。Dの明示適合は通常の名前解決を使い、同名宣言だけで継承対応を置換しない。

```kimi
contract Same
    func same(self: ref/Self, other: ref/Self) -> bool

open struct Shape
    Self is Same
    public func same(self: ref/Self, other: ref/Self) -> bool => true

struct Circle : Shape
    Self is Same    // Error: the inherited same takes ref/Shape, not ref/Circle.
```

<a id="s-8-6-3"></a>

### 8.6.3. 実装の照合

重複宣言・重なり検査は関連型完了後のヘッダーで行う。関連型推論はこの検査に依存せず、明示束縛でも推論束縛でも同じ結果となる。

Self、型引数、利用可能な関連型を代入し、関数要件の実装を次の完全一致で特定する：名前、関数種別、ジェネリックの個数・種別・順序、taskスロットの有無、通常パラメーターの個数・順序・外部ラベル、レシーバの有無・構造、正規化したパラメーター型構造。型構造にはSemantics、引数、完了関連型を含むが、Originは互換性検査用に残して照合から除く。

反変・Adaptationを使わず、結果で絞らず、優先指定・Origin・制約・効果で順位付けしない。候補0個は実装欠如、複数は曖昧。Callableは代わりに[§10.7](#s-10-7)を使う。1実装が複数要件を満たしてよいが、各照合と互換性義務を満たす必要がある。別実装宣言の共存は通常規則に従い、結果だけでは区別できない。別要件は呼出し候補として別のまま。

| 項目 | 互換性義務 |
| --- | --- |
| 制約 | 型・適合・要件の前提から実装の制約・条件を証明する |
| 入力 | 要件が許す全入力を受け入れる。呼出しローカル借用をstaticへ狭めない |
| 結果型 | [§10.7](#s-10-7)の結果互換性を満たす |
| 結果Origin | 少なくとも要件の保証を与える |
| アクセス | 適合の全領域で使える |
| 文脈・効果 | 強い呼出し文脈を要求しない。安全要件にunsafeを要求せず、効果境界を超えない |

効果境界は破棄を含む実装の要約に対して検査し、分類不能を空効果とみなさない。Origin契約比較は[§15.3.7](#s-15-3-7)に従う。互換性検査は変換・Copy・借用・型消去を挿入しない。失敗は適合エラーで、別実装を試す理由にはしない。プロパティ証拠は[§11.9](#s-11-9)・[§12.7](#s-12-7)に従う。

```kimi
contract Maker<T>
    func make() -> T

contract Both: Maker<i32>, Maker<string>

struct Stop
    Self is Both
    public func make() -> Never => $abort("No value") // One implementation satisfies both requirements.

struct Incomplete
    Self is Both
    public func make() -> i32 => 1                   // Error: does not satisfy Maker<string>.
    // public func make() -> string => "one"         // Error if added: duplicate Signature.

struct S
    Self is Source
    public func read(self: ref/Self) -> i32 => 1    // Infers Source.Element is i32.
    public func take(value: Self.Element) -> () => ()
    public func take(value: i32) -> () => ()        // Error: duplicate of the previous take.
```

<a id="s-8-6-4"></a>

### 8.6.4. 要件経由の呼出し

T is Cの下ではCと祖先の要件・関連型を使える。型関数はT.empty()のように型経由で呼び、Empty.empty()のようにContractから呼んで期待結果から型を推論することはできない。T.compareのような関数参照はSelfを束縛した要件のFunction Itemとなる。

別要件は別候補で、複数に合う呼出しは曖昧。要件を修飾して直接呼び分ける構文はなく、1つのContractに制約した補助関数を使う。

```kimi
contract ResetA
    func reset(self: uniq/Self)
contract ResetB
    func reset(self: uniq/Self)

func resetA<T>(x: uniq/T)
    T is ResetA
    x.reset()          // Valid: only ResetA.reset is available.

func resetBoth<T>(x: uniq/T)
    T is ResetA
    T is ResetB
    x.reset()          // Error: ResetA.reset and ResetB.reset are distinct requirements.
```

<a id="s-8-7"></a>

## 8.7. 組込みContractと閉じたContract

<a id="s-8-7-1"></a>

### 8.7.1. 組込みContract

Copy・Owned・Callable・Sealed・ObjectPayload・PrimitiveInteger・TransferableはKimiが宣言する組込みContract。同名・同形の利用者Contractには特別な効果を与えない。struct・enumのSelf is X（when P付きも同様）は次に従う。

| X | 意味 |
| --- | --- |
| Copy | Copyの導出を要求する |
| Owned | 制約・Pの全許容束縛でOwnedであることを検査する主張。能力を付与しない |
| Sealed・ObjectPayload・PrimitiveInteger・Callable・Transferable | 構造だけで決まるため宣言はエラー |

否定Self節はSelf is not ObjectPayloadによる辞退だけ。Contract内のSelf is X・Self is not ObjectPayloadは適合型への要件となり、各能力固有の規則で満たす。`contract C: Copy`のような組込み親指定も、本体のSelf is Copyと同じ。

Utf8Format・BufferWriter・Equatable・Comparable・列挙・Indexableの各Contractは同一性が固定された通常のContractで、宣言された境界を持つ。算術Contractは組込み証拠を持つ通常宣言。適合が証明するのは要件だけで、等価性の対称性等の法則を強制せず、初期化・Loan・表現も証明しない。

```kimi
struct Pair<T>
    Self is Copy when T is Copy     // Derived Copy.
    Self is Owned when T is Owned   // Checked; grants nothing.
    var first: T
    var second: T

struct Plain
    Self is Sealed                  // Error: Sealed is structural.
```

<a id="s-8-7-2"></a>

### 8.7.2. Sealed

正規化した型の外側がownerで、Coreが有効、Neverでもopen structでもない場合に限りSealedとなる。Scalar、string、Unit、enum、タプル、固定長配列、コレクション、Function Item、クロージャー、共通関数型は該当する。外側Coreだけを検査するため、非openのCell&lt;T>はTがopenでもSealed。Copy・Owned・排他アクセス・Object Targetは含意せず、not Sealedもownerやopen Coreを証明しない。

<a id="s-8-7-3"></a>

### 8.7.3. ObjectPayload

外側がowner、有効なCoreでNeverでなく、その宣言と全基底が辞退していない場合に限る。外側Coreだけを検査し、Boxが辞退していなければBox&lt;Parser>は該当する。すべて具体的オブジェクトペイロードにでき、ヒープ割当てを要求しない。

辞退はstruct・enumの先頭制約領域にSelf is not ObjectPayloadを単独・無条件で1回書く。Kimi.ObjectPayloadへ解決されなければならず、派生型も継承し、後で回復できない。

外側Semantics・Core・辞退が確定していれば、型引数未束縛でも直接判定する。元のs/Tの外側は許容集合で判定する。それ以外の型パラメーター・ペア対象・未解決射影には、ObjectPayload前提またはその拡張だけが証拠となる。Provenならownerの完全な値型だが、Copy・Owned・Sealed・Transferableは含意しない。

Object Targetは、ObjectPayloadの証明済み型、実行時Contractビュー対象、ペア証拠のあるペア対象のいずれか。Xを対象とするすべてのオブジェクト型（obj・rc・arc・objref・objuniq、およびWeakやキャスト・実行時is内のこれらの型）にはObject Targetが必要。値からのオブジェクト作成にはObjectPayload自体が必要で、既存ハンドル操作は証明し直さない。辞退Coreは自身の宣言内でもObject Targetにならず、Self is ObjectPayloadを証明するContractにも適合できないが、通常値、借用、他型内部への格納、キャプチャーは影響されない。

要件シグネチャでSelfのオブジェクト型を形成するContractには、宣言または継承によるSelf is ObjectPayloadの証明が必要。T is Cはその証拠も供給する。肯定と否定の両方があれば矛盾である。

```kimi
struct Parser
    Self is not ObjectPayload
    public var pos: i32 = 0

func boxed<T>(value: T) -> obj/T
    T is ObjectPayload
    return Kimi.Intrinsics.makeObj(value@move)

// let o = boxed(Parser.init())   // Error: Parser is not ObjectPayload.
```

<a id="s-8-7-4"></a>

### 8.7.4. PrimitiveInteger

メンバーを持たず、ownerの整数12種だけが満たす。証明されたT is PrimitiveIntegerは次を供給する。

- Scalar、Copy、Owned、Position、組込みEquatable・Comparable・Utf8Format。Tへの参照は値読み取りできる。
- 規定の算術Contract・演算子。ただしT自体の単項−は使えない。
- PrimitiveInteger同士の検査付き@U、Wrapping&lt;T>との相互変換、@wrap&lt;U>。
- 全12型に合う未型付けリテラル0〜127。
- これらの能力に単項−を加えたWrapping&lt;T>。

本体はこの能力で1回検査し、各実体化では具体型のオーバーフロー検査等を使う。`T is i8 or u8 or ...`は場合分け証明がないため同じ能力を供給しない。

```kimi
func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values
        total += value     // -value would be an error: unary - needs a signed Type.
    return total

let small = sum(1..5@u8)   // 10
```

<a id="s-8-7-5"></a>

### 8.7.5. 閉じたContract

Positionは整数12種・FromEnd&lt;T>・Start・End、PositionRangeはRange・ClosedRange・ResolvedRangeだけに固定する。他の型での適合宣言はエラー。整数にメンバーは増えず、各実装は境界と長さだけを読み、他の効果を持たない。

```kimi
struct Offset
    Self is Position    // Error: Position is closed.
```

<a id="s-8-8"></a>

## 8.8. 条件付き適合

<a id="s-8-8-1"></a>

### 8.8.1. 条件と実装ブロック

ジェネリックstruct・enumのメンバー位置にSelf is C when Pを置ける。型制約Dは全利用、Pはこの適合だけを制約する。Pはカンマで連ねた節の連言で、外側パラメーター、ペアバインダー・対象、射影を対象にし、肯定要件をandで結ぶ。or・not・値検査は禁止。外側パラメーター同士の同一性は適合スコープで有効となる。

任意の字下げ実装ブロックは型の名前空間に、関数、型種別が許す計算メンバー、関連型指定を宣言する。フィールド、Case、コンストラクター、drop、入れ子型、適合は置けず、レイアウトは変えない。Pは各ブロックメンバーだけの前提。既存メンバー・組込み導出で満たせばブロックを省ける。外部登録、拡張宣言、部分特殊化はない。

```kimi
enum Option<T>
    Self is Copy when T is Copy   // Option<i32> is Copy; Option<Resource> stays usable.
    Some(T)
    None

struct Box<T>
    var value: T
    Self is Describe when T is Describe
        public func describe(self: ref/Self) -> string
            return self.value.describe()
```

<a id="s-8-8-2"></a>

### 8.8.2. 検証と利用

定義時にD・Pの下で全許容束縛についてCを検証する。Pを満たす型が存在しなくても検証し、Pの同一性前提の誤りも適合エラーとなる。実装は固定した経路全体の前提で特定・完了する。各要件はD・P・要件前提の適合スコープで両側を正規化して比較する。ブロック外実装のシグネチャは変更しない。

利用時に代入済みPを証明すれば検証済み適合を使え、実装は再選択しない。ブロックメンバーと条件付きメンバー（自身の節をPに加える）、関数参照、計算アクセスも、名前解決・推論後に次で判定する。

| P | メンバー | 適合経路 |
| --- | --- | --- |
| Proven | 条件を満たす | 利用可能 |
| Refuted | 適用不能 | この経路からは適合しない |
| Unknown | 未証明 | 適合も不在も証明しない |
| Error | エラー | エラー |

条件で確定した名前解決層を変えず、適用不能でも探索し直さない。条件の強さで順位付けせず、後のLoan失敗でも再選択しない。Unknownが選択へ与える影響は[§10.1](#s-10-1)に従う。

```kimi
contract Flag

struct Either<A, B>
    Self is Flag when A is Option<B>, A is Array<B>  // Error: the condition identifies two nominal Types.
    var value: A

struct Slot<T>
    Self is Flag when T is Option<i32>
    var value: T

func plain<T>(value: T) -> i32
    T is not Flag
    return 0

func fill<V>(slot: Slot<Array<V>>) -> i32
    return plain(slot@move)         // Valid: Array<V> is Option<i32> is Refuted.
```

<a id="s-8-8-3"></a>

### 8.8.3. 親Contract

Child:Parentでは、Parentの証拠は直接適合かChild経路から得る。子経路には子の条件だけを要求するので、そこで使う親実装も子条件下で適用可能でなければならない。親の条件付きブロック内メンバーは親条件を持つため、そのまま子経路の実装にはできない。ブロック外に無条件、または子条件から証明できる条件で実装する。経路は一致し、同じシグネチャで条件だけが違うブロックメンバーは重複となる。

```kimi
contract Named
    func name(self: ref/Self) -> string
contract Tagged: Named
    func tag(self: ref/Self) -> i32

struct Tag<T>
    var value: T
    public func name(self: ref/Self) -> string => "tag"   // Outside both blocks.
    Self is Named when T is Copy
    Self is Tagged when T is Equatable
        public func tag(self: ref/Self) -> i32 => 1
// Moving name into a Self is Named when T is Copy block makes the Tagged declaration an error.
```

<a id="s-8-9"></a>

## 8.9. 衝突と証明経路

適合は[§8.6.1](#s-8-6-1)、要件・関連型は定義宣言と定義Contractの束縛で識別する。拡張経路で束縛を代入し、継承した入力条件は子の公開入力となる。

<a id="s-8-9-1"></a>

### 8.9.1. 直接適合の衝突

直接適合と経路の合流に、共通の衝突検査を使う。

1. Contract宣言ごとにまとめる。別宣言は衝突しない。同一性前提・分解・Origin規則で正規化する。
2. 型構造表のパラメーター・head・構成要素を、出現検査付きで単一化する。関数ローカルのバインダーは別名へ改名する。
3. headまたは定数長の不一致なら非衝突を証明でき、それ以外の置換は衝突条件となる。
4. 未解決射影、未確定ペアhead、非定数長、Originの共通部分・順序・比較属性、その他自由でない項は残余条件。未解決なら衝突の可能性があり、A.ItemとB.Itemも一致し得る。

直接適合の全組は型制約の下で非衝突を証明する必要があり、互いに排他的なwhen条件でも免除しない。

```kimi
struct Family<T>
    public contract Marker

struct Good
    Self is Family<i32>.Marker
    Self is Family<string>.Marker

struct Bad<A, B>
    Self is Family<A>.Marker
    Self is Family<B>.Marker   // Error: A and B can be the same Type.
```

<a id="s-8-9-2"></a>

### 8.9.2. 経路の合流と循環

同じ祖先宣言へ同じ正規化束縛で至る拡張経路は、1つの要件・関連型になる。関連型束縛、Origin条件、実装対応の一致を証明できなければ定義エラー。束縛が違う経路、独立した同名宣言は別要件のまま。

直接・拡張・継承の他の組合せは、各経路条件を衝突条件へ加え、交わり得る経路同士は一致を要求する。同じ拡張経路でContract宣言へ再訪すれば、引数が異なってもエラー。一方、別経路のA&lt;i32>とA&lt;i64>は循環ではない。

<a id="s-8-10"></a>

## 8.10. Callable制約

F is Callable&lt;r,S>は、レシーバ取得r（ref・uniq・owner）と、task有無を含む関数シグネチャSでの呼出しを要求する。Callable&lt;S>はrefの略。互換なFunction Item、クロージャー、共通関数型が該当し、callメンバーを探索しない。FはCopy・Ownedでなくてよい。結果は入力Originを保てるが、隠れたCallableレシーバを借用できない。

| r | 取得 | 許す呼出しレシーバ |
| --- | --- | --- |
| ref | ref/F | Shared |
| uniq | uniq/F | Shared・Exclusive |
| owner | Fの値 | Shared・Exclusive・Consuming |

Sの各直接借用入力の省略Originは呼出しごとに独立して束縛し、全Originについて適合を要求する。入れ子・キャプチャーOriginは固定。Sにはduringを書かない。集約の束縛集合は所属宣言のものを名付けられるが、入力スロットは完全な型か外側Originとの等式で固定する。結果省略Originは呼出しごとの入力から取り、複数なら共通部分。排他アクセスが必要な結果は対応する排他Loanを保つ。

ref呼出しは共有、uniq呼出しは排他アクセスを保持する。ownerはCopyまたは@moveで取得し、Shared・Exclusive本体なら取得値を借り、Consumingなら渡す。残りは呼出し後に破棄する。letからCopyした一時値でも排他的に使え、実体化で値取得を借用へ変えない。

複数Callable制約では、異なる正規化シグネチャを候補とし、同シグネチャでは最弱の宣言レシーバを使う。候補群が1つのレシーバ形・task形・パラメーター取得形を共有しなければ呼出しエラー。共有すれば通常の適用・最良候補規則で比較する。異なるシグネチャは矛盾証拠ではなく、Fへ束縛する型はすべてを満たす必要がある。

```kimi
func applyTwice<F>(value: i32, transform: uniq/F) -> i32
    F is Callable<uniq, (i32) -> i32>
    let first = transform(value)
    return transform(first)

let count: i32 = 0
var bump = func [var count] (value: i32) -> i32
    count += 1
    return value + count
let result = applyTwice(10, bump@uniq)   // 13
```

<a id="s-8-11"></a>

## 8.11. 効果境界

効果境界は呼出しがアクセスし得る範囲の保証で、全適合実装が守り、ジェネリック呼出し側が利用する。語彙はconfinedだけの閉じた集合。安全・unsafeで分類は変わらない。

<a id="s-8-11-1"></a>

### 8.11.1. 宣言

宣言できる場所は4つだけ。

- 関数要件の制約領域のeffect節：その要件を制限する。
- Contract内の`effect A.r bound`：祖先Aから継承した一意な関数要件rを制限する。
- Callable制約下のeffect節：その制約経由の呼出しを制限する。
- 元のvirtual関数の先頭領域のeffect節：スロットを制限し、overrideが継承する。

override、非virtual関数、先頭領域後など他の位置はエラー。実装は要件の境界を追加・削除できない。1つのContract内で同じ要件へ同じ境界を2回書けばエラーだが、祖先境界の再記述は同じ保証にまとめる。T is Cが保証するのはCと祖先の境界だけ。

```kimi
contract Take
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>

contract QuietTake: Take
    effect Take.take confined

open struct Base
    public virtual func score(self: objref/Self) -> i32
        effect confined          // Slot bound, inherited by overrides.
        return 1

struct Derived : Base
    override func score(self: objref/Self) -> i32
        // effect confined       // Error: an override cannot declare a bound.
        return 2
```

<a id="s-8-11-2"></a>

### 8.11.2. `confined`

confined実装の効果要約には環境効果・分類不能効果を含めない。環境効果は、入力でなく環境から可変状態への権限を得る操作。一度得た権限でのアクセスは追加の環境効果ではない。

| 操作 | 分類 |
| --- | --- |
| 入力（レシーバ・引数・taskスロット）自身またはその到達先 | 環境効果なし。Loan検査は必要 |
| 呼出し内で作る記憶、Raw.allocate | 環境効果なし |
| Placeの@raw | そのPlaceへの通常アクセスと同じ分類 |
| raw型間変換、rawからusize、rawアクセス | 環境効果なし。rawアクセスのLoan条件は別途適用 |
| Transferableと証明済みの不変静的値の読出し・借用 | 環境効果なし。ただし初期化子の効果を含める |
| その他の不変静的値の読出し・借用 | 環境効果 |
| 可変静的フィールドの全アクセス（初回初期化を含む） | 環境効果 |
| Console出力等のプログラム外状態へのKimi操作 | 環境効果 |
| 外部関数呼出し、整数からポインターへの変換 | 環境効果 |
| 定義で抽象のままの型（Copy未証明の型引数・関連型等）に依存する破棄・置換 | 分類不能、confined違反 |
| 環境効果不明の呼出し・破棄（型消去後の破棄を含む） | 分類不能、confined違反 |

静的値はその型で判定する。基底オブジェクトビュー・実行時Contractビュー・共通関数型には隠れたrc・raw内容があり得、抽象型にはTransferable前提が必要。割当て・Abortは許す。環境由来ポインターは未使用でも効果で、外部呼出しはシグネチャが許す引数経由アクセスも行う。引数として渡された静的借用の権限は入力由来となる。task操作は[§22.3.3](#s-22-3-3)に従う。

```kimi
contract Sink
    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        effect confined

group Metrics
    var puts: isize = 0
    public func record() => Metrics.puts += 1

struct Item
    public var value: i32 = 7

group Shared
    public let limit: i32 = 10
    public let node: rc/Item = Kimi.Intrinsics.makeRc(Item.init())

struct Reader
    Self is Sink
    var count: isize = 0
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        self.count += 1              // Valid: authority from self.
        let a = Shared.limit         // Valid: i32 is Transferable.
        let b = Shared.node@ref      // Error: rc/Item is not Transferable.
        Metrics.record()             // Error: reaches the mutable static Metrics.puts.
        return .Ok(())

contract Reset
    func reset(self: uniq/Self)
        effect confined

struct Slot<T>
    Self is Reset
    var item: Option<T>
    public func reset(self: uniq/Self)
        self.item = .None   // Error: destroying the old T has unknown effects.

struct CopySlot<T>
    T is Copy
    Self is Reset
    var item: Option<T>
    public func reset(self: uniq/Self)
        self.item = .None   // Valid: a Copy value has no destruction effects.
```

<a id="s-8-11-3"></a>

### 8.11.3. 検査と利用

実装を特定し、シグネチャ・関連型・前提が互換なら適合は証拠を供給する。効果境界は別義務として、再帰の不動点を含む完成済み推移要約で検査する。違反は適合宣言のエラーであり、その証拠を使った選択を変えない。祖先境界は祖先適合、条件付き境界はその条件下で検査する。

具体的呼出し先不明の要件呼出しは、前提が証明するContractの宣言境界だけを使い、実装の境界は使わない。呼出し効果は次となる。

1. 入力経由では、各取得モードでレシーバ・引数から届く全Placeを含む。共有層の先は共有のみで、rawや抽象部分内も含む。所有ハンドルの破棄効果も加える。OriginがLoanを表し得るというだけでは到達とせず、実際のPlace経路からだけ到達する。
2. 環境効果はconfinedならなし、それ以外は不明として静的Loanに作用する。task呼出しも静的Loanと比較する。

生きたLoanと競合し得れば呼出し箇所のエラー。不正なeffect項目は境界を供給しない。結果型が呼出しレシーバのLoanに依存する場合はそのLoanを保持し、生きている間の競合する次の呼出しを禁止する。

```kimi
contract Feed
    func next(self: uniq/Self) -> Option<i32>
        effect confined

group Feeds
    public func pull<F>(feed: uniq/F) -> Option<i32>
        F is Feed
        return feed.next()

struct Countdown
    Self is Feed
    var left: i32
    public func next(self: uniq/Self) -> Option<i32>
        if self.left == 0 => return .None
        self.left -= 1
        return Feeds.pull(self)   // Valid: Countdown is Feed is evidence; the bound is checked on the completed summary.
```

```kimi
// Take is the contract of §8.11.1.
func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is Take
    let first = source.take()
    let second = source.take()   // Valid: first keeps no receiver Loan, and the call reaches only source's Places.
    return (first@move, second@move)
```

<a id="s-8-11-4"></a>

### 8.11.4. Callableの効果境界

要件全体が単一のCallable&lt;...>である制約には、字下げしたeffect節を付けられる。関数・struct・enum・関数要件の制約領域（条件付きメンバー前提も含む）に限り、Contract全体の節、関連型宣言、適合条件には置けない。先頭領域を終了させず、同じ制約のconfinedは最大1回。

Fへ束縛する型は、rで取得する呼出し全体の境界を満たす。取得、ownerなら残りの破棄も含む。キャプチャーは入力であるレシーバ経由に数える。境界は呼出しだけを覆い、別の場所の破棄は外側要約へ加える。

適用可能性・制約証明では境界を無視し、選択済みの呼出し・関数参照・型形成の義務として完成要約で検査する。違反は該当する値引数・型引数で報告し、候補を選び直さない。

| 束縛型 | 証拠 |
| --- | --- |
| 非virtualのFunction Item | 自身の効果要約 |
| virtualスロットのFunction Item | スロット境界 |
| 要件のFunction Item | 利用可能な境界 |
| 具体的クロージャー | 取得・環境破棄を含む本体要約 |
| 型パラメーター・射影 | 同シグネチャ、同じか強いレシーバ（ref &lt; uniq &lt; owner）、その境界を持つ前提 |
| 共通関数型 | なし |

F経由の呼出しも、同シグネチャと同じか強いレシーバの前提境界を使う。呼出し側ではCallable入力経由の呼出し効果を実引数へ合成する。実装は要件にないCallable境界を追加できない。

```kimi
func apply<F>(transform: ref/F, value: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return transform(value)

contract Mapper
    func map<F>(self: uniq/Self, transform: ref/F) -> i32
        F is Callable<(i32) -> i32>
            effect confined      // Bounds calls of transform.
        effect confined          // Bounds the requirement map.
```

<a id="s-8-12"></a>

## 8.12. 実行時Contract

実行時Contractビューには、未定義の実行時使用指定が必要なため、本版では形成できない。本節はその拡張時の規則である。Contract名単独は値型にならない。

ObjectViewCompatible(C)には、実行時用の指定、型関数要件なし、関連型固定後の全インスタンス要件について次を要求する：安全なメソッド・アクセサー、非消費のオブジェクトレシーバ、Selfの使用はレシーバだけ、未束縛関連型・メソッドジェネリクスなし、型・Originが隠れた型なしでレシーバ・入力・staticから表現可能。ゲッター結果とセッター要件は別々に検査する。

Cへのオブジェクト型形成にはこの判定、値をビューへ消去するにはObjectPayload・Ownedが必要。Implements(D,C)には、束縛Contract参照に一意な明示・継承適合と、ObjectCallCompatibleのProvenを要求する。

```kimi
contract Speaker
    Self is ObjectPayload
    func speak(self: objref/Self) -> string

func announce(value: objref/Speaker) -> string   // Error in this revision: runtime Contract Views cannot be formed.
    return value.speak()
```

<a id="s-8-13"></a>

## 8.13. 関数の明示的完全特殊化

完全特殊化は、閉じた静的ジェネリック引数1組について実装を指定する。呼出し契約は保つが動作は異なってよく、該当する特殊化は必ず使う。

<a id="s-8-13-1"></a>

### 8.13.1. 宣言と対象

specialize funcに、単純な非修飾名、全スロットの明示引数、元と同じtask有無、型付きパラメーター、任意の結果型、本体を書く。型パラメーターを宣言せず、元の名前も見えない。結果省略はUnit。引数は閉じている必要があり、未束縛型・ペア、未解決射影、外側ジェネリックは使えない。

対象は通常本体を持つジェネリックのルート関数・型関数・メソッドだけ。コンストラクター・drop・アクセサー・演算子、部分・条件付き・コンテナー引数の特殊化はない。

元と特殊化は同じ宣言スコープ（ルートならSourceDocument実行スコープ、メンバーなら同じコンテナー）に属する。同じ名前・種別・スロット数で、代入後のtask・レシーバ・パラメーター構造がOriginを除いて一致する関数が一意でなければエラー。結果、制約、宣言順では選ばない。

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both declarations have the same substituted input structure.
```

<a id="s-8-13-2"></a>

### 8.13.2. 継承する契約

ヘッダーは元の代入済みレシーバ形・パラメーター・結果・外部名をAdaptationなしで再記述する。内部名はexternal => internalで変えてよい。アクセス、既定引数、!境界、制約、属性、安全性、効果境界、Originバインダーは継承し、書かない。既定引数は元を評価してから特殊化を実行する。

本体は元が許す全Origin束縛で動作しなければならず、Origin制限の追加、適用範囲の縮小、必須証明の置換はできない。ObjectCallCompatibleをNotProvenにする可能性はある。overrideヘッダーも同じ原則に従う。

<a id="s-8-13-3"></a>

### 8.13.3. 選択

キーは元の宣言と正規化した静的引数で、Originだけを除く。obj/Dとrc/Dは別。同キーの特殊化2つはエラー。

呼出し・関数参照は通常の名前解決・オーバーロード・元の契約を使い、特殊化は候補にならない。呼出し時でもジェネリック代入後でも、キーが確定したら該当特殊化を使う。動的型は関係しない。特殊化自身に名前はなく回避できず、内部で同じ関数・引数を呼べば再帰する。元のKotonoha・宣言スコープに属し、宣言・読込み順は選択に影響しない。

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1: the i32 specialization runs.
```

<a id="s-8-14"></a>

## 8.14. ジェネリックのアクセス効果

取得・Adaptationのアクセス効果は静的に一意で、取得（Copy・Move）、Loan操作（共有借用・排他借用・再借用）、対象PlaceとOrigin依存からなる。Consumeは取得種別ではなく、操作を許すかの静的検査名である。排他的な呼出しAdaptationは予約を使う。

裸のPlaceは各許容場合で、Copy証明があればCopy、uniq・objuniqと証明できれば再借用。それ以外の最終場合は定義エラーで、Moveを推論しない。@copyも証拠を要求し、@moveは移転する。@s・@s/T・@TypeはAdaptation表に従い、ownerを許すvalue@sはその場合に同じ型の取得となるためCopy証明が必要。

Copy不明をNon-Copy扱いせず、コンパイル時指令でアクセス効果を選ばない。場合が異なれば各場合のCopy・再借用計画を検証する。@sと@followは逆操作でなく、@followはPlaceを選ぶだけでTakeを復元しない。

```kimi
func inspectLocal<s/T>(value: s/T)
    s is owner or uniq
    T is Copy
    let local = value         // owner: Copy; uniq: Reborrow.
    // _ = value              // Error at definition: in the uniq case it conflicts with local.
    _ = local

func invalid<T>(value: T)
    let local = value         // Error at definition: neither Copy nor an exclusive reference is proven.
```

```text
Conceptual cases of inspectLocal:
  s = owner: func inspectOwner<T>(value: T)                         // local is a Copy of T.
  s = uniq:  func inspectUniq<T, origin o>(value: uniq/T during o)  // local is a Reborrow within o.
```

<a id="s-8-15"></a>

## 8.15. ジェネリック本体の検査

<a id="s-8-15-1"></a>

### 8.15.1. 全称検証

本体は、制約、外側の条件付き適合前提、シグネチャを満たす全整形式の型・長さ・Origin束縛について有効でなければならない。使用の有無によらず定義時に検査し、型列挙や隠れた制約の推論はしない。契約で表現・証明できない必要条件があれば定義を拒否する。ジェネリック呼出しは呼出し側前提から相手の要件を証明し、特殊化で不正本体を救えない。

公開義務はシグネチャ・スキーマ・関連型・制約・条件付きメンバー前提の整形式条件であり、本体が必要とするCopyなどを追加しない。元を初期化済みで残す読み取りにはCopy、借用、または再初期化が必要。取得・要素・ゲッター結果は宣言型を保つ。ObjectCallCompatibleは実装の保証で、適用前提ではなく、実体化でも強めない。

```kimi
func transfer<T>(value: T) -> T => value@move   // Moves a Copy or Non-Copy T alike.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
```

<a id="s-8-15-2"></a>

### 8.15.2. Semanticsの場合

ペア層の合法性は全許容場合について決め、その後の所有解析は、スコープ内の各ペアバインダー（本体、コンテナー、クロージャーの外側宣言を含む）に許容Semanticsを1つずつ選んだ場合ごとに行う。

各場合は完全な型、通常のアクセス効果、依存、Loan、後始末、クロージャーレシーバを持つ。場合を合流せず、Copyの場合に再借用の場合だけのLoanを持ち込まない。実体化は引数が選ぶ場合となる。この場合分けは所有解析専用で、制約証明に新しい前提を加えない。

全場合が有効なら定義は有効。列挙・共有・分解の実装方法は規定しない。解析する場合数の上限は実装定義で、超過は資源失敗であって言語上の不正ではない。

```kimi
func count<s/T>(value: s/T) -> i32
    s is ref or uniq          // s is a pair binder; the body is checked for ref and for uniq.
    let view = value@follow@ref
    return 0
```

<a id="s-8-15-3"></a>

### 8.15.3. 具体的レイアウトと表現

具体型のレイアウト義務（Cレイアウトのゼロサイズフィールド制限、アラインメント、サイズ上限等）は、その値を格納・作成・値渡し・返却するか、サイズが必要な使用で検査する。フィールド・ローカル・配列要素・ペイロード・raw割当てを含む。借用・raw・オブジェクトハンドルの背後だけで型を形成する場合や、制約内で名前を挙げるだけなら検査しない。

義務違反はレイアウトを必要とする使用箇所のLanguageエラー、実装上限超過は資源失敗、生成できないレイアウトはUnsupportedと区別する。

検査済みの非ジェネリック本体が直接・間接に必要とする全実体化を、到達可能性・コード生成有無によらず検査する。どの検査済み非ジェネリック本体からも実体化されないジェネリック本体は記号的にだけ検査する。対象環境の未対応操作をcheckでも報告するかは実装定義だが、報告するコマンドは検査対象集合全体を扱う。

型引数を拡大しながら再帰する多相再帰は合法。実体化ごとにコードを生成する実装が上限に達しても、資源失敗であって不正なプログラムではない。

```kimi
func divide<T>(a: T, b: T) -> T
    T is PrimitiveInteger
    return a / b

func unusedA(a: i128, b: i128) -> i128 => a / b        // Unsupported where i128 division is unsupported.
func unusedB(a: i128, b: i128) -> i128 => divide(a, b)  // Same: divide<i128> is in the checked set.

func depth<T>(value: T, n: i32) -> i32
    require n > 0 else => return 0
    return depth<Option<T>>(.Some(value@move), n - 1) + 1   // Valid; may reach a resource limit.
```

<a id="s-9"></a>

# 9. 名前・シグネチャ・アクセス

名前解決は名前を宣言に結び付ける。型名はその後に型名選択（[§9.7](#s-9-7)）と型の合法性検査、呼出しはオーバーロード解決（[§10](#s-10)）と使用位置の合法性検査へ進む。**後戻りはしない。** 段階・宣言・オーバーロードを選択後の失敗はエラーとし、別候補を選ばない。未知の名前と解決不能な依存循環はエラー。ファイル・別名・候補の順序で結果は変わらない。

<a id="s-9-1"></a>

## 9.1. シグネチャ

シグネチャは同一スコープ内で宣言が共存できるかを決める。

| 宣言 | シグネチャ |
| --- | --- |
| 型宣言 | 種類、名前、ジェネリックパラメーター数 |
| 関数 | 名前、ジェネリックパラメーター数、レシーバを含む順序付き正規化パラメーター型 |
| コンストラクター | 宣言struct、順序付き正規化パラメーター型 |
| フィールド、計算メンバー、プロパティ要件 | 名前 |
| enum Case | enum内の名前。ペイロードではオーバーロードしない |

同一スコープの同名型名前空間宣言は種類（group・struct・enum・contract）が同じでなければならず、引数数によらず種類競合はエラー。同種でジェネリック引数数が違えば別型。同種・同数のgroup/structは断片（[§6.1.2](#s-6-1-2)）、他の重複はエラー。rootgroupのパス要素が同スコープの非group型を指せば種類競合となり、横にgroupを作らない。

**GenericArity**は長さスロットを含み、ペアは一つと数える。Originスキーマではオーバーロードできず、`View<T> {source}`と`View<T> {left, right}`は共存できない。

正規化では各名前をSymbolとKotonohaへ解決し、透過的別名と完成済み関連型射影（[§8.6](#s-8-6)）を展開し、括弧と冗長な`owner`を除く。全Semantics層は保持し、ジェネリック位置を次の構造にする。

| ソース | 正規化 |
| --- | --- |
| 通常の`T`、元の`s/T` | `Slot(i)` |
| ペアバインダー`s` / ペア対象`T` | `OuterSemantics(Slot(i))` / `DirectTarget(Slot(i))` |
| 別スロット`U`への`s`適用 | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| 長さ`N` / 固定長配列 | `LengthSlot(i)` / `FixedArray(Length, Element)`。長さは[§4.4](#s-4-4) |
| `Self` | struct・enumは`S<Slot(0), …>`、Contractは適合型バインダー`SelfSlot` |

比較は構造的α同値で行い、実体化・制約証明では単純化しない。スロットの種類は区別しないため`f<T>(value: T)`と`f<s/U>(value: s/U)`は競合する。`ref/T`と`uniq/T`は別だが、同名のレシーバ形二つは[§7.3](#s-7-3)で禁止する。レシーバも通常パラメーターと同様に比較し、名前は無関係。

Origin・関係、結果型、パラメーター名、既定値、引数名契約（[§7.2.2](#s-7-2-2)）、アクセス、`unsafe`、制約、タスクスロット（[§24.2.3](#s-24-2-3)）はシグネチャを区別しない。重複はエラーで、異なっても取得の形（[§7.4](#s-7-4)）により競合しうる。別コンテナー由来の同一シグネチャは、解決で一つを選べなければ使用が曖昧となる。

```kimi
struct Box
struct Box<T>                  // Valid: same kind, different arity.
// group Box                   // Error: kind conflict with struct Box.
struct A<T>
// rootgroup A.B               // Error: A is a struct in this scope.

struct Reader
    func read(self: ref/Self, at: i32) -> i32 => 0
    // func read(r: ref/Reader, i: i32) -> i32 => 0 // Error: same Signature.

func identity<T>(value: T) -> T => value@move
// func identity<U>(value: U) -> U => value@move     // Error: same Signature.
```

<a id="s-9-2"></a>

## 9.2. 名前空間と名前解決の役割

構文が選ぶ**名前解決の役割**で候補を絞る。

| 名前空間 | 宣言 |
| --- | --- |
| 型 | コンテナー、Kotonoha参照名、名前付き別名、型パラメーター、ペアバインダー、関連型、`Self`、組込みSemanticsと分類 |
| 値 | 関数、フィールド、計算メンバー、enum Case、パラメーター、ローカル、長さパラメーター |
| Origin / Label | Origin / ラベル |

| 役割 | 名前空間 | 候補 |
| --- | --- | --- |
| Core | 型 | struct、enum、関連型、`Self`、型パラメーター、ペア対象 |
| Object View Target | 型 | Coreと実行時Contract |
| Type Semantics | 型 | ペアバインダーと言語定義Semantics |
| Qualifier | 型 | group、Kotonoha参照、メンバー修飾可能な型 |
| Requirement | 型 | 要件構文が許すもの |
| Declaration Container | 型 | group、struct、enum、contract、Kotonoha参照 |
| Value | 値 | 呼出し可能性によらず全値宣言 |
| Enum Case | 値 | 修飾部または期待型が固定したenumのCase。非修飾名は不可（[§9.5](#s-9-5)） |

役割の絞込みは宣言の種類と構文だけを見て、引数数・引数・期待結果・制約・後続メンバーは見ない。呼出し可能なもの専用の役割はない。オブジェクト対象構文はObject View Targetを選ぶ。選択後、宣言制約の下でその束縛を役割に使えるかを型の合法性で検査する（[§8.1.1](#s-8-1-1)）。ここや後のObjectViewCompatibleの失敗はエラーで、外側探索を再開しない。

型名と値名は共存できる。同じ名前空間・スコープでの同名は[§9.1](#s-9-1)だけが許す。Origin・Labelは型・値名前空間へ切り替えない。

```kimi
struct T

func f<s/T>(x: s/T, y: T) -> () => ()
// Error: T here is the pair target, not struct T, and is not proven a value Type.
```

<a id="s-9-3"></a>

## 9.3. ローカルスコープと文脈上の名前

ローカルは宣言後から見え、初期化子は外側を参照する。内側ブロックは外側ローカルを隠せるが、同一ブロックで同名を二度宣言できず、パラメーターは直下本体と名前空間を共有する。ローカル関数はブロック進入時から見え、オーバーロードできるがローカル・パラメーターと衝突できない。コンテナーメンバーは宣言前から使えるが、未初期化値は読めない。

入れ子関数は外側の型パラメーターを使えるが、名前付き関数は実行時ローカルをキャプチャーしない。キャプチャーは匿名関数だけ（[§7.7.2](#s-7-7-2)）。シグネチャに外側Originを書くことはキャプチャーではない（[§15.3.4](#s-15-3-4)）。文書実行スコープは[§6.1.1](#s-6-1-1)、パターンスコープは[§14.8.2](#s-14-8-2)。

`Self`は型・Contract文脈用の予約名。`self`、`value`、格納アクセサー内の`storage`（[§11.3](#s-11-3)）は文脈上の束縛で、有効な間は再宣言・暗黙キャプチャーできない。ローカル名前付き関数内では有効でない（[§7.3](#s-7-3)）。他の場所の`storage`は通常名。

```kimi
func example(input: i32) -> i32
    let x = input
    if true
        let x = x + 1     // Valid: the initializer uses the outer x.
    // let input = 0      // Error: a parameter and a local share one space.
    return x
```

<a id="s-9-4"></a>

## 9.4. アクセス

使用にはアクセス権、スコープ・修飾・別名を通る経路（**名前の到達可能性**）、適合する役割が必要。別名はアクセスを広げない。

<a id="s-9-4-1"></a>

### 9.4.1. アクセス水準と範囲

宣言は一つのアクセス指定を持つ（[§F.6.1](#s-F-6-1)、修飾子順は[§F.3.3](#s-F-3-3)）。protected系は入れ子コンテナー・アクセサーを含むstructメンバーだけに使える。アクセス範囲は次の原子の集合式。

- `Body(C)`：全断片と入れ子本体を含むコンテナー`C`の字句上の本体。
- `Kotonoha(K)`：Kotonoha `K`の全ソース。同一性は[§18.4.1](#s-18-4-1)。
- `Derived(S)`：`S`の任意の構築から直接・間接に派生する、現在および将来の全struct本体。`S`がopenでなければ空。
- `All`：全体。

| Kotonoha `K`のコンテナー`C`のメンバーのアクセス | 範囲 |
| --- | --- |
| `private`（既定） | `Body(C)` |
| `internal` | `Kotonoha(K)` |
| `protected` | `Body(C) ∪ Derived(C)` |
| `protected internal` | `Kotonoha(K) ∪ Body(C) ∪ Derived(C)` |
| `private protected` | `Kotonoha(K) ∩ (Body(C) ∪ Derived(C))` |
| `public` | `All` |

実効範囲`Access(D)`は全外側コンテナーの範囲との共通部分。privateなルート宣言の範囲は`Kotonoha(K)`。構築済みジェネリック型は宣言範囲と具体的引数の範囲の共通部分を持つ。package・friendアクセスはない。

privateはファイルでなく結合済みコンテナーに従い、親は子のprivateメンバーへアクセスできない。アクセサーは[§11.3.2](#s-11-3-2)に従う。enum CaseとContract要件はコンテナーの範囲を取り、修飾子不可。メンバー選択後に必要なアクセサーへアクセス不能、またはレシーバがなければエラー。

<a id="s-9-4-2"></a>

### 9.4.2. 範囲の包含

`A ⊆ B`は原子だけで判定する。`A`を共通部分の和、`B`を和の共通部分にする。`A`の各共通部分が`B`の各和`U`に対し、異なる二Kotonohaの原子を含む（空集合）か、`U`に含まれる原子を持てば成立する。原子が`U`に含まれるのは、`U`のいずれかの原子に含まれるか、それが`Derived(S)`で`Access(S) ⊆ U`のとき。

全原子は自身と`All`に含まれる。非openな`S`の`Derived(S)`は全原子に含まれる。さらに次だけが成立する。

- `Body(C) ⊆ Body(E)`：`E`が`C`自身またはその外側。
- `Body(C) ⊆ Kotonoha(K)`：`C`が`K`に属する。
- `Body(C) ⊆ Derived(S)`：`C`が`S`の派生struct自身またはその内部。
- `Derived(S) ⊆ Derived(S')`：`S`が`S'`自身または派生型。

`Derived`は一ビルドに存在するstructでなく、宣言済みの`open`と基底句から決める。

```kimi
internal struct Secret

public open struct Base
    protected func peek(value: Secret) -> () => ()
    // Error: Derived(Base) is not included in Kotonoha(K).
```

<a id="s-9-4-3"></a>

### 9.4.3. protectedレシーバ

派生struct `D`から基底`B`のprotectedインスタンスメンバーへアクセスするには、レシーバの静的実効Coreが`D`またはその派生型でなければならない。ジェネリックレシーバは証明済み基底制約を使える。`B`や兄弟型では足りない。アクセサーは個別に検査する。`B`本体内、同じKotonoha内の`protected internal`アクセス、非インスタンス宣言（型関数・コンストラクター・入れ子コンテナー）にはこの制限を適用しない。継承でprivateアクセスは得られない。

制限対象のprotected関数は`receiver.f(...)`でだけ使え、型修飾呼出し`Left.f(x)`やFunction Item `Left.f`はエラー。派生本体はアクセス可能なprotected基底コンストラクターを基底句や`Base.init(...)`で呼べる。

```kimi
open struct Base
    protected init() => ()
    protected virtual func f(self: objref/Self) -> i32 => 0

struct Left : Base
    init() : base() => ()
    func make() -> Base => Base.init()   // Valid: constructors have no receiver.
    func probe(self: objref/Self, other: objref/Base) -> i32
        // let g = Left.f                // Error: protected Function Item outside Base.
        // other.f()                     // Error: the receiver must be Left or derived.
        return self.f()

func outside() -> Base => Base.init()    // Error: Base.init is protected.
```

<a id="s-9-4-4"></a>

### 9.4.4. APIシグネチャのアクセス可能性

**APIシグネチャ**は宣言が公開する全型・要件。未使用でも、宣言`D`の各具体型・要件`T`について`Access(D) ⊆ Access(T)`が必要（[§9.4.2](#s-9-4-2)）。直接基底も派生型を`D`として従う。

対象はパラメーター・レシーバ・結果、enumペイロード（enumの範囲）、フィールド・プロパティ型（プロパティの範囲）、各アクセサー固有部分（自身の範囲）、本体内制約を含む要件。制限付きセッターはプロパティの範囲を狭めない。注釈のないフィールドなどの推論型も同様に検査する。別名展開後に再帰的に調べ、Semanticsでアクセス不能Coreを隠せない。関連型射影では修飾部、要件、公開束縛を調べる。ジェネリックパラメーターは記号的に扱って制約を検査し、publicなジェネリック宣言にもprivate型引数を渡せる。

```kimi
public group Api
    private struct Hidden
    public struct Box<T>

    public func identity<T>(value: T) -> T => value@move
    public func expose(value: Box<Hidden>) -> () => () // Error: Box<Hidden> is private to Api.
    internal func leak(value: Hidden) -> () => ()      // Error: wider than Hidden.
    private func keep(value: Hidden) -> Hidden
        return identity<Hidden>(value@move)            // Valid.
```

<a id="s-9-4-5"></a>

### 9.4.5. ジェネリック本体

実体化位置によらず、定義位置の文脈とソース環境（[§18](#s-18)）で名前解決・アクセス検査を行う。呼出し側の別名は候補を加えない。APIに露出しないprivate型・補助関数を使えても、外部での実体化で呼出し側にアクセス権は与えない。型引数と制約は使用位置で検査する。

<a id="s-9-4-6"></a>

### 9.4.6. 適合のアクセス可能性

型`T`、束縛済みContract参照`C`、各要求実装`M`について、次が必要。

祖先適合も個別に検査する。実装を先に選び、その後アクセスを検査し、再選択しない。privateメンバーに合わせて適合範囲を狭めない。

```text
Access(Conformance(T, C)) = Access(T) ∩ Access(C)
Access(Conformance(T, C)) ⊆ Access(M)
```

```kimi
public contract Readable
    property value: i32 has get

public struct Example
    Self is Readable
    private var stored: i32 = 0
    public computed value: i32
        get(self: ref/Self) -> i32 => self.stored        // Required: must be public.
        private set(self: uniq/Self, value: i32) -> () => self.stored = value
```

<a id="s-9-5"></a>

## 9.5. 非修飾名前解決

要求名前空間・役割について次の順に探す。

1. ローカル、各外側字句スコープとパラメータースコープ。ルート関数には文書実行スコープを含み、コンテナー本体には含まない（[§6.1.1](#s-6-1-1)）。
2. 現在のコンテナー。
3. 各親コンテナーから、使用元Kotonohaのプロジェクトルートまで。
4. 型の修飾部だけ：使用元KotonohaのCompilationルートの参照名（[§18](#s-18)）。
5. 使用元SourceDocumentの全明示別名を一緒に。
6. 定義元Kotonohaの全既定別名を一緒に（[§18.2.3](#s-18-2-3)）。

型パラメーターと文脈上の名前は宣言位置で見つかる。`E@X`の裸のAdaptation Targetは[§13.5](#s-13-5)の役割を使う。各段階で同名宣言を集め、同一Symbolへの経路を重複除去し、役割・アクセスで絞る。適格宣言がある最初の段階で止まり、同名関数は一つの**関数群**となる。同段階で関数と別種の値が混在すれば競合。停止後の型引数・ラベル・制約・引数型の不一致や呼出し不能値はエラーで、外側へ進まない。近いgroup `X`はCoreとしての`X`探索は止めないが、`X.member`はmemberがなくても止める。

非修飾名はenum Case、レシーバが必要なメンバー、継承宣言を選ばない。最初の適格段階がそれらだけなら、修飾が必要というエラーで止まる。段階2・3でstruct自身に適格宣言がない場合、基底層を順に調べ、アクセス可能で役割の合う最初の同名宣言（条件付きメンバーを含む、[§6.2.2](#s-6-2-2)）で同じエラーにする。基底引数は不要、protectedレシーバ制限は無視し、基底のジェネリックパラメーターと`Self`は飛ばす。自身の基底句はこの検査なしで解決するが、`struct S : S.N`のような循環は解決不能な依存循環となる。Origin・Labelにはこの規則を適用しない（[§15.3.4](#s-15-3-4)）。

```kimi
group Outer
    func marker() -> i32 => 1

    open struct Base
        protected var count: i32 = 0
        public func marker() -> i32 => 25

    struct Derived : Base
        func total(self) -> i32 => self.count + Self.marker() // Valid: qualified.
        func root() -> i32 => ::Outer.marker()                // Valid: Outer.marker.
        func first() -> i32 => marker()          // Error: stops at Base.marker.
        func second(self) -> i32 => count        // Error: write self.count.
```

<a id="s-9-5-1"></a>

### 9.5.1. 別名

名前付き別名は型名前空間の段階5に対象のQualifier役割で加わり、Coreではない。選んだ別名の後続メンバーがなくても戻らない。同じ文書内の同名別名二つは未使用でも同じ参照（正規化引数を含む）を指す必要がある。

開く別名（[§18.2.1](#s-18-2-1)）で公開したメンバーの競合は使用時に検査する。複数元から集めた関数群は[§7.4](#s-7-4)と単一タスクスロット形（[§24.2.3](#s-24-2-3)）を満たす必要があり、違反すれば全使用がエラー。`A.f`で一つの元を選べる。

<a id="s-9-6"></a>

## 9.6. 修飾名前解決と継承名前解決

`A.B.C`の先頭は非修飾名前解決、後続は選んだ対象のメンバーだけを探す。型側の途中要素はQualifier、末尾は構文の役割を使う。関連型射影は[§8.5](#s-8-5)、[§9.7.3](#s-9-7-3)。

型・値の両修飾が可能なら、各名前空間が独立に最初の段階を確定し、役割・アクセス・静的/インスタンス使用を含む全経路をたどる。[§9.5](#s-9-5)のエラー停止は経路を失敗させる。成功が一つなら選択、複数なら曖昧で、引数・期待型では決めない。同じSymbolへの経路は一つと数えるが、異なるレシーバ経由の値アクセスは別経路。

`::A.B`はローカル・別名を無視して使用元KotonohaのCompilationルートから始める（[§18](#s-18)）。ルート名と参照名の衝突は[§18.4.1](#s-18-4-1)。

structメンバー探索は自身、続いて基底引数を代入した各基底層を探し、アクセス可能で役割の合う宣言がある最初の層で止まる。その層の同名関数全体が関数群。レシーバ、適用可能性、アクセサー、Loan検査は後で行い、探索を再開しない。overrideは層を加えない（[§6.4.2](#s-6-4-2)）。アクセス可能な基底`f`と派生`f`の共存は[§6.2.2](#s-6-2-2)。新しい明示Contract実装もこの方法で選んだ後に検査する（[§8.6](#s-8-6)）。ジェネリックパラメーターでは制約から同名要件を集め（[§8.6.4](#s-8-6-4)）、使用時に群を検査する（[§7.3.2](#s-7-3-2)、[§7.4.4](#s-7-4-4)、[§24.2.3](#s-24-2-3)）。

```kimi
group Config
    public var count: i32 = 3

struct Settings
    public var count: i32 = 0

func read(Config: ref/Settings) -> i32
    return Config.count                  // Error: both the Type and the Value path succeed.
```

<a id="s-9-6-1"></a>

### 9.6.1. 基底サブオブジェクトへのレシーバ射影

`receiver.member`で、レシーバの静的実効Core `D`の基底`B`のインスタンス宣言を選ぶと、基底引数を代入してインライン`B`サブオブジェクトへ射影する。アクセスは先に元のレシーバで検査する。型関数は射影不要。型修飾呼出し・関数値は[§9.4.3](#s-9-4-3)の後、通常引数規則に従う。

宣言レシーバが`ref/B`・`uniq/B`なら、元の権限でサブオブジェクトの共有・排他借用または再借用を作る。暗黙レシーバ取得（[§7.3](#s-7-3)）の一部なので、裸の所有`D` Placeも貸出し点が排他書込み可能なら排他射影する（[§15.1.5](#s-15-1-5)）。レシーバは引数より前に一度だけ評価する。メソッド・カスタムアクセサーはサブオブジェクト全体を借用し、標準プロパティアクセスは記憶Placeへ射影（[§11.2.2](#s-11-2-2)）して、元の所有・借用・オブジェクト分類を保つ。

射影は変換でなくBest Candidate比較に使わない（[§10.4](#s-10-4)）。借用レシーバ実装を射影経由で使うには、選択後にObjectCallCompatible Provenが必要（[§12.7](#s-12-7)）。NotProvenはエラーで、候補除外には使わない。基底全体のMove・置換・切り出したownerは許さず、レシーバ由来結果はLoanを保つ。オブジェクトでは同一性と動的型を保ち、ハンドル生成・参照数変更はせず、Owned消去の根拠が必要（[§6.4.5](#s-6-4-5)）。`owner/B`・`obj/B`・`rc/B`・`arc/B`を満たすことはない。

<a id="s-9-7"></a>

## 9.7. 型名選択

名前解決停止後、使用位置で解決した明示型引数の数・種類で型候補を絞る。残りが一つでなければ、0なら不一致、複数なら曖昧。制約はその後に検査し、別型を試さない。

`.init`またはCase参照（式・パターン）の前で独自型引数列を省略した修飾部は、独自引数数0の参照があればそれを選び、なければ唯一のジェネリック参照を選ぶ。複数なら曖昧で、引数・期待型では決めない。Case引数推論は[§6.5.2](#s-6-5-2)。

```kimi
struct Box<T>
struct Box<T, U>

let a = Box<i32>.init()               // Valid: selects Box<T>.
// let b = Box.init()                 // Error: two generic references.
let c = Option.Some(1)                // Valid: Option<i32>.
```

<a id="s-9-7-1"></a>

### 9.7.1. 束縛済みコンテナーパス

コンテナー参照は、パス（役割・アクセス・各要素の引数数）、字句または基底置換を伴う型・Origin引数、全修飾部と対象の検証、の3段階で解決する。後の失敗で層を再探索しない。

外側型引数を省略できるのは環境が供給するときだけ。`Outer<T>`内の`Inner<i32>`は`Outer<T>.Inner<i32>`を意味する。期待型や入力から外側を推論せず、`Outer<i32>.Inner.init(value)`で推論するのもInner自身のスロットだけ。

継承コンテナーは定義元宣言を保つ。`Base<T>`内の`Node`を`Derived : Base<i32>`から参照する`Derived.Node`は`Base<i32>.Node`。アクセス可能な祖先コンテナーと型名前空間で同名なら、引数数によらず競合する。

<a id="s-9-7-2"></a>

### 9.7.2. パス上のOrigin

入れ子宣言のOriginスキーマは自身・継承スロットを含み、型引数・フィールド・基底のスロットは含まない。接尾辞はその出現の完全束縛集合を名付け、[§15.3](#s-15-3)で補完する。Contract参照、別名対象、コンテナー修飾部には完全Origin契約が必要。推論・継承束縛が供給しない関係は外側宣言に付ける。式レベルの制約ブロックと暗黙`static`はない。

`(Outer<T>{outer}).Inner<U>{inner}`は値を作らず途中修飾部のOriginを束縛し、その義務を保持する。束縛集合ラベルは字句スコープを持ち、型引数ではない。

<a id="s-9-7-3"></a>

### 9.7.3. 入れ子型と関連型射影

型パラメーターは同じ名前空間の入れ子宣言と競合する。祖先名を隠しても継承スロットは消えない。Originはスカラー名、束縛集合名、値による保持元を最も近いスコープでまとめて探し（[§15.3.4](#s-15-3-4)）、役割違いを越えて外側へ戻らない。

入れ子型は関連型ではない。`T.(C).Element`はContract `C`を選び、`T.C.Element`は入れ子パス。`T.Element`が両方に解決すれば曖昧。関連型候補の重複除去は定義宣言と束縛済みContract参照だけで行う。

<a id="s-10"></a>

# 10. オーバーロード解決と推論

呼出し・関数参照・構築に使う宣言を選び、その独自の型・Semantics・長さ・Origin引数を推論する。コンテナー修飾部は先に解決し（[§9.7.1](#s-9-7-1)）、省略した外側環境を推論したり、後の失敗で再探索したりしない。引数Adaptation、期待結果との互換性、取得の合法性は別判定（[§3.8](#s-3-8)）。関連型推論（[§8.5.5](#s-8-5-5)）も本章の規則を変えない。

<a id="s-10-1"></a>

## 10.1. 候補の適用可能性

確定した関数群のパラメーター取得形を先に検査（[§7.4](#s-7-4)）し、各宣言に次を行う。

1. 明示型引数の数・種類を検査。
2. 引数をパラメーターに対応させ、使う既定値を記録（[§10.1.1](#s-10-1-1)）。
3. レシーバ・引数から型引数を推論。呼出しシグネチャ照合も含む（[§10.8](#s-10-8)）。
4. 独立に既知の期待結果型で残る型引数を埋める。固定済み引数は変えない。
5. 置換後、引数Adaptation（[§10.2](#s-10-2)）、裸のPlaceの値取得に必要なCopy証明（[§3.5](#s-3-5)）、制約、条件付きメンバー前提（[§7.5](#s-7-5)、[§8.8](#s-8-8)）を検査。
6. 期待結果がある場合、使用位置が結果を受け入れられない候補を除外（[§10.3](#s-10-3)）。

待機引数は候補ごとに検査せず、[§10.5.1](#s-10-5-1)に従う。手順5の制約・前提がUnknownなら適用可能性は未証明だが、適用可能として比較する。厳密に最良の候補があれば選び（[§10.4](#s-10-4)）、そのUnknown判定を選択後の義務とする。Finalityの下でProvenでなければエラー（[§8.3](#s-8-3)）。最良がなければ曖昧。選択を延期・再試行せず、未証明候補を落として他を勝たせない。

適用候補なしはエラー。候補検査ではCopy・Move・借用・既定値評価を行わない。別候補が成功しても宣言エラーは残る。裸のPlaceの取得モードは残った候補に依存しない（[§7.4](#s-7-4)）。Callable効果境界は候補を除外・順位付けせず、選択した呼出し・関数参照で検証し、失敗しても再選択しない（[§8.11.4](#s-8-11-4)）。

```kimi
func keep<T>(value: T) -> ()
    T is Owned
func keep<T>(value: T, tag: i32 = 0) -> () => ()
func store(view: ref/i32)
    keep(view)     // The first is strictly best (fewer defaults); Error: T is Owned is not proven.
    keep(view, 1)  // The second: the only candidate with two parameters.
```

<a id="s-10-1-1"></a>

### 10.1.1. 引数対応

- 被呼出し先にタスクスロットがある場合に限り、先頭にタスク引数を書く（[§24.2.2](#s-24-2-2)）。それ以外はエラー。パラメーターは束縛しない。
- 位置引数は名前付き引数より前に、宣言順に対応する。直接呼出しでは`!`より前だけ（[§7.2.1](#s-7-2-1)）。名前必須・既定値付き・後で名前指定するパラメーターを飛ばさず、型で探さない。メソッド呼出しは先にレシーバ位置を除く（[§7.3.3](#s-7-3-3)）。
- 名前付き引数は外部名を使い、順序は自由だが同じパラメーターへ二度渡せない。未知ラベル・過剰な位置引数はエラー。
- 未対応パラメーターは既定値を使い、なければエラー。既定値は推論の根拠にしない。
- 明示引数はソース順、その後に既定値をパラメーター順で評価する（[§7.2.3](#s-7-2-3)）。関数値呼出しは全て位置指定。

```kimi
func scale(value: i32 ! by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // by is evaluated before value.

func configure(! mode: i32 = 0, count: i32 = 1) => ()
configure(count: 3)    // mode takes its default.
configure(3)           // Error: mode requires its name; matching does not skip to count.
```

<a id="s-10-2"></a>

## 10.2. 共通Adaptation

値を固定期待型に合わせるために使う。対象は引数、注釈付き初期化子、代入元、値結果、集約要素、enumペイロード、既定値、固定Target Result Typeの結果源（[§14.9.2](#s-14-9-2)）。期待型を借用・値読出しから逆算しない。期待型なしでは裸の取得（[§3.5](#s-3-5)）を使う。

`U`は完全な期待型。共有参照・値読出しは適格なペア層を含む複数層をたどれる（[§3.4.3](#s-3-4-3)、[§13.5.5](#s-13-5-5)）。

| 入力 | 期待型 | 操作 |
| --- | --- | --- |
| `U`を格納する読出し可能Place（参照・ハンドルスロットを除く） | `ref/U` | 新しい共有借用 |
| owner `U`の一時値 | `ref/U` | 一度実体化して共有借用 |
| `U`に至るsafeな値参照層 | `ref/U` | `U`への一つの共有参照 |
| `uniq/U` | `uniq/U` | 排他再借用 |
| 読出し可能な`obj/U`・`rc/U`・`arc/U` | `objref/U` | 共有オブジェクト借用。参照数不変 |
| `objuniq/U` | `objuniq/U`または`objref/U` | オブジェクト再借用 |
| 読出し型`U`に至るsafeな値参照層 | `U` | 値読出し（[§3.5.3](#s-3-5-3)） |
| Function Itemまたは具体的クロージャー | 共通関数型`U` | 型消去（[§7.7.4](#s-7-7-4)） |
| その他の値・Place | 自身の完全型 | 裸の取得または転送（[§3.5](#s-3-5)） |

**参照・ハンドルスロット**は型が`ref`・`uniq`・`objref`・`objuniq`または所有ハンドル`obj`・`rc`・`arc`のPlace。最初の行では暗黙借用せず、例えば`clone(handle@ref)`と書く。他の行はその参照先・オブジェクトへ適用でき、`obj/U`から`objref/U`などは可能。`Weak<S>`を含む他の名前付き型には最初の行を使う。

明示取得は暗黙Adaptationに先行する。`@move`は値と責任を転送し、元PlaceをMovedにする。後のAdaptationは転送を置換・取消ししない。転送済み排他参照が直接期待型に合えば、再借用せずそのまま渡す。それ以外は取得後の完全型・分類（裸の入力なら元のもの）から一行だけを選ぶ。格納`uniq/U`から`ref/U`は共有参照行を直接使い、暗黙操作は最大一つ。明示取得はその数に含めない。通常の一時値寿命・Loan・型消去規則に従い、Origin関係は選択後に判定する（[§15.6.1](#s-15-6-1)）。

複数層に`ref`があれば最も内側の`ref`をCopyし、その下の各`uniq`を共有再借用する。結果OriginはそれらのOriginのmeetで、外側層は依存を加えない。`ref`がなければ全層を通る共有再借用となる。全層で初期化・能力・Loanを検査する。共有参照と再借用は参照を保持する一時スロットへの依存を加えない。

これ以外の暗黙操作はない。オブジェクトペイロードの追跡、`rc`/`arc`複製、一時値の排他借用、オブジェクトアップキャスト、数値・利用者定義変換は行わない。裸のNon-CopyまたはCopy未証明Placeは値取得せず、必要な候補は適用不能。ただし格納排他参照の裸取得は再借用する。所有Placeの新しい排他借用には、レシーバ式（[§7.3](#s-7-3)）を除き`@uniq`・`@objuniq`が必要。裸の所有Placeや排他参照経由で到達したPlace（[§15.1.5](#s-15-1-5)）は`uniq`・`objuniq`パラメーターに合わない。注釈は寿命・能力を加えない。

owner一時値は一度だけ通常寿命のTemporary Placeへ評価し（[§3.6](#s-3-6)）、借用のため寿命を延長しない。`let view: ref/Node = makeNode()`の後で`view`を使うとエラー。

```kimi
func validate(node: ref/Node) -> () => ()
func normalize(node: uniq/Node) -> () => ()

func work(node: uniq/Node)
    validate(node)              // Shared Reborrow.
    normalize(node)             // Exclusive Reborrow.
    let transferred: uniq/Node = node@move // Transfers the reference; no Reborrow.

var node = Node.init()
validate(node)                  // Shared borrow of the owned value.
normalize(node@uniq)
// normalize(node)              // Error: no implicit exclusive borrow.
validate(node@move)             // Transfer, then borrow the temporary; node is Moved.
```

<a id="s-10-2-1"></a>

### 10.2.1. リテラル

リテラル引数は[§12.3](#s-12-3)の規則で候補型に合わせる。位置・範囲形式は固有規則を保ち、狭い幅を優先しない。既定型（`i32`、`f64`、長さ文脈の`isize`）はBest Candidate選択後、選んだ候補だけに適用する。適用可能性・比較中、リテラルだけを根拠とするスロットは未束縛のまま（[§10.5.1](#s-10-5-1)、[§10.8](#s-10-8)）なので、[§10.4](#s-10-4)手順2では比較不能となる。既定型で曖昧さを解消しない。

既定型適用後、その型を必要とした期待型に合うことと制約を検査し、失敗しても再選択しない。なお未束縛ならエラー。期待型のない式は直接既定型を使う。`null`、空コレクション、型なし関数には代替型がない。

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(1)        // Error: ambiguous; both integer Types fit.
choose(1@i64)    // choose(i64).

func f(value: i32) -> () => ()
func f<T>(value: T) -> () => ()
f(1)             // Error: ambiguous; T is unbound while candidates are compared.
f(1@i32)         // f(i32): T = i32 is evidence, and step 3 prefers the non-generic function.

func g<T>(value: T) -> () => ()
g(1)             // The only candidate; T defaults to i32 after selection.
```

<a id="s-10-2-2"></a>

### 10.2.2. 推論とAdaptationの分類

型推論はCopy・Move・借用を行わない。

1. 明示型引数を先に束縛し、残りはレシーバ・引数の元の完全型からまとめて推論する。走査順には依存しない。裸の`T`は引数型へ束縛するが、読出し型パラメーター（[§3.5.3](#s-3-5-3)）は参照層の末端参照先を束縛して値読出しする。`ref/T`・`uniq/T`は所有引数型、または借用の最外層の参照先へ`T`を束縛する。ペア層は借用でない（[§13.5.5](#s-13-5-5)）。要素型`s/U`に対する`inspect(c[i])`は`T = s/U`としてスロットを借り、`inspect(c[i]@follow)`は`U`を束縛する。
2. 独立に既知の期待結果で未束縛部分だけを型照合し、借用・値読出しを逆算したり確定型を変えたりしない。
3. [§3.5](#s-3-5)・[§10.2](#s-10-2)で取得・Adaptationを計画する。直接合う排他参照は裸なら再借用、`@move`なら転送。

順位は次の順で、同じ分類内に優劣はない。

| 分類 | 意味 |
| --- | --- |
| Exact | 借用・再借用を含むAdaptationなし |
| Literal fitting | リテラルを候補型に直接合わせる。一時値借用なども必要ならその取得の分類を使う |
| Same-Semantics Reborrow | 入力の外側Semanticsを保つ再借用 |
| Cross-Semantics adaptation | その他の借用・再借用、複数層を通る一つの共有参照、値読出し |

Exactは型の話で、値引数は裸ならCopy、`@move`なら転送するが両者に優劣はない。必要な排他再借用はExactにならない。権限とPlace/一時値の形は適用可能性で、初期化・Loan競合は選択後に呼出し予約とともに検査する（[§10.6](#s-10-6)、[§15.6.7](#s-15-6-7)）。ジェネリック本体は定義時の制約で取得・順位を決め、実体化で再選択しない。型消去には分類がなく、同引数位置の二つの消去は同等、消去と他操作は比較不能。

```kimi
// n: ref/i32; r: uniq/Node; identity<T>(value: T) -> T; double<T> with T is PrimitiveInteger.
let a = identity(n)        // T = ref/i32.
let b = identity<i32>(n)   // Value read at the fixed i32.
let c = identity(r)        // T = uniq/Node; an exclusive Reborrow, like let saved = r.
let e = double(n)          // T = i32; n is value-read.
```

<a id="s-10-3"></a>

## 10.3. 期待結果

期待結果は推論を補完でき（[§10.1](#s-10-1)手順4）、既知の結果を使用位置がどの取得・Adaptationでも受け入れられない場合だけ候補を除外する。順位付けには使わない。選択前は構造的に期待型に合うかだけを判定し、Origin関係は選択後（[§15.6.1](#s-15-6-1)）に判定する。ただしOwned、Callable、型同一性のようなOriginを含む制約は参加する。候補を残すために変換を挿入しない。結果を捨てる呼出しにはUnit期待を与えない（[§14.2](#s-14-2)）。

| 使用位置 | 既知の結果に適用する規則 |
| --- | --- |
| 固定期待型あり | 共通Adaptation（[§10.2](#s-10-2)）と位置の寿命検査 |
| 期待型なし | 裸の取得（[§3.5](#s-3-5)） |
| 明示操作・結果を捨てる | その操作の規則 |

未知の結果`T`をAdaptation後の型から逆算しない。

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // fetch(i32): only its result fits.
fetch(1)                    // Error: discarding leaves both candidates.

func read(x: i32, from: ref/i32) -> ref/i32 => from
func read(x: i64, from: ref/i32) -> i32 => 0
// let other: i32 = read(1, limit@ref) // Error: both results fit, by a value read or exactly.
```

<a id="s-10-4"></a>

## 10.4. 最良候補

対比較は優・劣・同等・比較不能を返し、同等の場合だけ次へ進む。他の全適用候補より優れる一候補だけを選ぶ。

1. レシーバを除く各明示ソース引数のAdaptation分類を比較する（[§10.2.2](#s-10-2-2)、[§7.3](#s-7-3)）。全位置で劣らず一つ以上で優れれば優位。位置ごとに優劣が逆なら比較不能。名前付き引数は元の式で対応し、既定値は除外し、コストは合計しない。
2. 手順1で対応した置換後パラメーター型の構造（Originを除く）を比較する。既定値で埋めた位置は除外。全て同一なら次へ、他は比較不能。
3. 長さを含む独自ジェネリックパラメーターのない関数を優先。外側型のジェネリック性は数えない。
4. 使用した既定値が少ない方を優先。
5. なお決まらなければ曖昧エラー。

幅、制約の強さ、パターンの一般性、宣言順では順位付けしない。前段階の比較不能を後段階で救済しない。

```kimi
func inspect(value: uniq/(uniq/Node)) -> () => ()
func inspect(value: ref/Node) -> () => ()
func work(x: uniq/(uniq/Node))
    inspect(x)          // The first: a same-Semantics Reborrow beats a cross-Semantics reference.
    inspect(x@follow)   // The second: only it adapts the selected uniq/Node Place.

func g(x: i32) -> () => ()
func g(x: i32, y: i32 = 0) -> () => ()
g(1@i32)                // g(x): steps 1–3 tie on x; step 4 prefers fewer defaults.
```

<a id="s-10-5"></a>

## 10.5. 推論の境界

- ローカル型は宣言時に固定し、固定長配列注釈の穴は初期化子から解決する（[§4.3.1](#s-4-3-1)）。
- 名前付き関数の結果は明記型かUnit。本体・呼出し側からシグネチャを推論しない（[§7.1](#s-7-1)）。匿名関数は推論する（[§7.7.1](#s-7-7-1)）。
- 明示的特殊化は推論・適用可能性に参加せず、元の宣言と引数決定後に選ぶ。
- 置換は入れ子を含むOrigin束縛・Loan要件を保持し、借用を作らない（[§15](#s-15)）。
- `try`のオペランド（[§17.3](#s-17-3)）と明示的に結果を捨てる式（[§14.2.4](#s-14-2-4)）には期待型がない。

<a id="s-10-5-1"></a>

### 10.5.1. 引数の種類

各引数は次のいずれか一つ。タプル・配列リテラル自身は分類せず、各要素を位置ごとの引数として再帰的に分類する。

- **Literal**：型未確定リテラル、リテラルだけの式（[§12.3](#s-12-3)）、全結果源がそれらである制御フロー式（[§14.9](#s-14-9)）。候補パラメーター型を全結果源へ伝え、Literal fittingで順位付けする。スロットの根拠にはせず、選択後に既定型を適用（[§10.2.1](#s-10-2-1)）。
- **独立型付け可能**：呼出しの期待もリテラル既定型も使わず完全型が決まる他の引数。型付き値・Place（明示`@`操作も含む）、`S`不要の関数参照（[§10.5.3](#s-10-5-3)）など。入れ子呼出しは期待結果なしで適用候補がちょうど一つの場合だけ。匿名関数は含めない。型は外側候補スロットの根拠となる。
- **待機**：その他全て。匿名関数、`S`が必要な関数参照、独立に型付けできない入れ子呼出しなど。残る外側候補共通の期待型、または選択済み候補の期待型だけで検査し、外側スロットの根拠にはしない。

タプル・配列リテラルは候補ごとに要素を期待型に合わせる（[§4.3.1](#s-4-3-1)）。Literal要素はその位置の型に合うだけで根拠にならず、独立要素は位置のスロットの根拠、待機要素はそこで待機検査する。リテラル全体を受けるスロットは、全要素が根拠になる場合だけ束縛できる。

内側呼出しを外側候補ごとに解決せず、除外候補は復活させない。共通期待・選択済み候補のどちらでも型付けできない待機入れ子呼出しには、注釈・明示型引数・型付き束縛が必要。匿名関数は[§10.5.4](#s-10-5-4)。

```kimi
func identity<T>(value: T) -> T => value@move
func take(value: i64) -> () => ()
func makeValue() -> i64 => 1
func inspect<T>(value: ref/T) -> () => ()
func makeBox<T>(value: T) -> Box<T> => Box.init(value@move)  // Box<T>: an application struct.
func outer<U>(value: Box<U>) -> () => ()
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
func pair<T>(first: T, rest: (T, T)) -> () => ()

inspect(makeValue())            // Independently typable: T = i64.
take(identity(1))               // Waiting: the expected i64 fixes the inner T.
outer(makeBox(1))               // Error: U is unbound; a waiting argument is no evidence. Write makeBox(1@i32).
pair(1, (makeValue(), identity(2))) // Elements one by one: T = i64 from makeValue(); identity(2) waits.
take(if ready => 1 else => 2)   // Literal: both sources fit i64.
choose(if ready => 1 else => 2) // Error: ambiguous; both candidates fit the literals.
```

<a id="s-10-5-2"></a>

### 10.5.2. 呼出しシグネチャ

型の**既知の呼出しシグネチャ**は、その値を呼ぶシグネチャ（[§7.7.3](#s-7-7-3)）。Function Itemは束縛引数を代入した宣言、具体的クロージャーは内部シグネチャ、共通関数型は型構造、ジェネリックパラメーターはCallable制約が与える唯一のシグネチャ（[§8.10](#s-8-10)）を使う。匿名関数はヘッダーに明記した入力・結果型だけを供給し、明記した場合だけタスクスロットを持つ（[§24.2.1](#s-24-2-1)）。

位置の期待型が共通関数型ならその型、または未束縛`F`の全Callable制約が同じ`S`を与える`F`・`ref/F`・`uniq/F`ならその`S`が、**固定期待呼出しシグネチャ**。外側候補が複数なら全候補共通の`S`だけを使う。`S`のスロットは[§10.8](#s-10-8)の未知数であり、未束縛スロットを含まない部分だけが引数を導く。

```kimi
func make<T>(action: () -> T) -> T => action()
func makeLarge() -> i64 => 2
let large = make(makeLarge)               // T = i64 from makeLarge's signature; then erased.
let written = make(func () -> i64 => 2)   // T = i64 from the written result Type.
// let open = make(func () => 2)          // Error: only the body could fix T.
```

<a id="s-10-5-3"></a>

### 10.5.3. 関数参照

参照候補は関数群のうち[§10.1](#s-10-1)手順1を通る宣言。型修飾インスタンス参照の`self`は通常パラメーター（[§7.3](#s-7-3)）。候補が2個以上、または明示引数で束縛されない独自スロットがあれば**`S`が必要**。その場合`S`なしはエラー、不要なら独立型付け可能（[§10.5.1](#s-10-5-1)）。

`S`が必要な参照は`S`の全パラメーター型が閉じた時点で選ぶ。各候補はAdaptationなしで入力型を`S`の入力、結果を`S`結果の閉じた部分と照合して独自スロットを束縛する（[§10.8](#s-10-8)）。全スロットが束縛され、制約・前提が[§10.1](#s-10-1)手順5を通り（Unknownは同節の規則）、置換後シグネチャが引数数・タスク有無を含め`S`の閉じた部分と互換なら適用可能（[§10.7](#s-10-7)）。[§10.4](#s-10-4)で比較し、手順1・4は同等とする。

選択後にunsafe・`drop`禁止と型消去条件を検査（[§7.6](#s-7-6)、[§7.7.4](#s-7-7-4)）し、再選択しない。`F`・`ref/F`・`uniq/F`では消去せずFunction Item型を`F`に束縛し、`ref/F`では一時値を借用する。選択したシグネチャで`S`の未確定結果を埋めない。

`T is Comparable`の下の`T.compare`などはSelf・関連型を代入した要件のFunction Itemであり、呼出しは実装へ届く（[§8.6](#s-8-6)）。実体化後も要件のFunction Itemを保つ。`Comparable.compare`は無効。

```kimi
func show(value: i32) -> () => ()
func show<T>(value: T) -> () => ()
func identity<T>(value: T) -> T => value@move

let a: (i32) -> () = show         // show(i32): step 3 prefers the non-generic function.
let b: (i32) -> i32 = identity    // T = i32 from S; then erased.
// let e = show                   // Error: no S and two candidates.
// let f = identity               // Error: no S and T is unbound.
// let g: (ref/Node) -> ref/Node = identity // Error: T would hold a per-call Origin.
```

<a id="s-10-5-4"></a>

### 10.5.4. 匿名関数

引数順によらず、本体より先に明示型・独立型付け可能引数・制約を処理する。ヘッダーの引数数・型は候補を絞り、推論の根拠になる。本体は明記シグネチャ、残る候補共通の期待、選択候補のシグネチャのいずれかで検査する。`F`・`ref/F`・`uniq/F`では固定期待呼出しシグネチャ（[§10.5.2](#s-10-5-2)）が省略入力型を導き、具体的クロージャー型を消去せず`F`に束縛する。

共通期待がなければ本体なしで候補を比較する。本体に必要な部分を期待が供給しないなら注釈が必要。本体は候補を選ばず、再検査せず、後の使用から入力を推論しない。本体検査前に結果がUnitなら単項本体の結果を捨て（[§7.1](#s-7-1)）、それ以外は値文脈とする。本体から推論した結果型は匿名関数だけのもので外側の根拠にならない。別引数が先に固定したUnitも有効だが、後の推論で本体を再解釈せず、型付き関数値の結果をUnitへ消去しない。

```kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()
run(func () => compute())        // Error: the candidates differ in expectation; the body never selects.
run(func () -> i32 => compute()) // The written result Type selects the second.
```

<a id="s-10-5-5"></a>

### 10.5.5. 推論後の制約

[§8.3](#s-8-3)に従い、Provenは成立、Refutedは拒否、Errorは不正な根拠を報告、Unknownは[§10.1](#s-10-1)手順5に従う。ジェネリック能力は定義時に証明する（[§8.15](#s-8-15)）。`T is C`を証明できなくても`T is not C`の証明にはならない。メンバーは呼出し側のインポートでなく定義環境を保つ（[§18](#s-18)）。定理証明、型列挙、制約の強さによる順位付けはない。除外構文の宣言は候補に入らず（[§19.4](#s-19-4)）、条件付きメンバーは名前解決に残って適用可能性だけに影響する（[§7.5](#s-7-5)、[§8.8](#s-8-8)）。

<a id="s-10-6"></a>

## 10.6. 使用の合法性と演算子

選択後にunsafe権限、初期化、Move状態、Loan、寿命、アクセサーアクセス、書込み能力を検査する。失敗でも選択は変えないため、より良いオーバーロードを加えると既存呼出しが無効になりうる。実装は解決作業量に上限を置けるが、超過はエラーであり候補選択ではない。

演算子候補は組込み操作、比較対応（[§13.4.1](#s-13-4-1)）、独自に経路を選ぶ算術Contract（[§13.3.4](#s-13-3-4)）が定義する。添字候補はIndexable Contractが定義し、キーが一意に合う適合を選ぶ。順位付け・期待結果による除外はしない（[§4.7.1](#s-4-7-1)）。

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: i64) -> () => ()
let number: i32 = 1
inspect(number) // Error: inspect(i32) is selected, then rejected outside an Unsafe Block.
```

<a id="s-10-7"></a>

## 10.7. Callableシグネチャの互換性

共通関数型消去（[§7.7.4](#s-7-7-4)）と`Callable<r, S>`（[§8.10](#s-8-10)）は同じ規則を使う。実装`(A1, ..., An) -> R`が要求`(P1, ..., Pn) -> Q`に合うには、タスクスロットの有無（[§24.2.4](#s-24-2-4)）と引数数が一致し、全位置で`Pi <: Ai`（[§3.8](#s-3-8)）、かつ`R`が`Q`と結果互換であることが必要。ラベル・既定値で不一致を補えない。

結果互換は`R <: Q`または`R`がNeverのとき成立する。Contract実装照合、Contractの拡張の競合検査、Callable互換、共通関数型消去は全てこの関係を使う。共通関数型値どうしの関係は部分型だけ。Neverが期待型に合う関係（[§3.8](#s-3-8)）は部分型とは別関係である。

Origin・Loanは正準Origin契約で比較する（[§15.3.7](#s-15-3-7)）。要求側の量化子は固定し、実装の呼出し時Originだけを推論する。互換性は借用・変換・型消去を挿入しない。直接一致と型消去は比較不能（[§10.2.2](#s-10-2-2)）。

```kimi
contract Empty
    func empty() -> Self

struct Impossible
    Self is Empty
    public func empty() -> Never => $abort("no value")   // OK: Never satisfies Self.
```

<a id="s-10-8"></a>

## 10.8. ジェネリック引数推論

明示引数を先に束縛し、型・Semantics・長さ（[§4.4](#s-4-4)）・Originの制約をレシーバと独立型付け可能引数からまとめて集める（[§10.5.1](#s-10-5-1)）。期待結果は未束縛部分だけを埋める。相関スロットに依存する群の明示型引数なし呼出しでは、[§7.4](#s-7-4)の相関を全て示す。

**呼出しシグネチャ照合。** 固定期待シグネチャ`S`（[§10.5.2](#s-10-5-2)）を持つ各パラメーターは、引数の既知シグネチャ、または`F`・`ref/F`・`uniq/F`では`F`に束縛する型のシグネチャと`S`を照合する。タスク有無・構造・Semanticsは正規化同一性、Originは限定推論（[§15.3.6](#s-15-3-6)）を使い、Adaptation・探索はしない。未束縛スロットを含む部分だけをこの方法で照合し、他部分は[§10.7](#s-10-7)で合う必要がある（共通関数型なら消去、`F`ならCallable制約）。ヘッダー型には互換性があれば足りる。呼出しごとのOriginはスロット内Originを解かない。`(T) -> ()`と`(ref/i32) -> ()`なら`T = ref/i32`となるが、そのOriginは他の根拠で決める。

**求解。** 照合失敗、構造・Semanticsの根拠の矛盾は候補を適用不能にする。Originの根拠は適用不能にしない（[§15.6.1](#s-15-6-1)）。明示型引数は保持する。それ以外は不変位置の束縛を解とし、全束縛の構造・Semanticsが一致しなければ適用不能、Originも等しくなければならない。不変束縛がなければ共変束縛のmeetを使う。各引数がOriginの条件を満たすことは選択後に判定する。

未解決関連型射影を逆算しない。`value: T.Element`への`i32`は`T`を束縛せず、`witness: Tag<T>`など別入力が必要。走査順、変換連鎖、共通基底探索、制約強度で解を選ばない。待機引数の完成クロージャーは外側スロットを埋めない。Neverの引数・要素は根拠にならず、Neverを含むタプル・配列リテラル全体を受けるスロットにも根拠を与えない。Neverは[§14.9.2](#s-14-9-2)で候補を与えないのと同じで、位置の期待型に合わせること自体は可能（[§3.8](#s-3-8)）。

**Semantics。** 期待`s/T`の下で`n@s`のLiteralオペランドは`T`に合う（オブジェクト対象は除く、[§13.6.2](#s-13-6-2)）。型付きオペランドは自身の型を保つ。未束縛`s`は変換探索せず元の外側Semanticsから推論する。`owner`と`ref`の根拠は矛盾する。`s is reference`はSemantics射影、ペアの`T is C`は対象射影を検査し、通常の`T`は完全Semanticsを保つ。

**未束縛スロット。** [§10.1](#s-10-1)手順3・4後、リテラル既定型適用前に根拠がないスロットは不一致ではない。他検査では候補を残し、そのスロットが必要な制約はまだ判定しない。これを含むパラメーター型は他のどの型とも同一・部分型にならない。ただし待機引数が束縛する`F`・`ref/F`・`uniq/F`はこの未束縛扱いではなく、等しい形は[§10.4](#s-10-4)手順2で等しい。`X = ref/X`のような無限置換はエラーで、循環は根拠にならない。

```kimi
func both<T, F, G>(first: ref/F, second: ref/G) -> T
    F is Callable<() -> T>
    G is Callable<() -> T>
    return first()

func makeSmall() -> i32 => 1
func makeLarge() -> i64 => 2
func identity<T>(value: T) -> T => value@move
func stop() -> Never => $abort("stop")
// let mixed = both(makeSmall, makeLarge)  // Error: conflicting evidence for T.
// let s = identity(stop())                // Error: T is unbound; Never supplies no evidence.
let t: i32 = identity(stop())              // T = i32 from the expected result.
```

<a id="s-10-8-1"></a>

### 10.8.1. コンストラクターの型推論

構造体の宣言と環境を固定後（[§9.7](#s-9-7)）、独自スロットに[§10.8](#s-10-8)を適用する。期待結果からは、同じ宣言のowner型との直接構造照合でだけ供給できる。借用、オブジェクト生成、アップキャスト、変換、関連型射影を逆算しない。既定値、フィールド初期化子、本体は根拠にならない。リテラルを期待型に合わせる規則・既定型は[§10.2.1](#s-10-2-1)、enum Caseは[§6.5.2](#s-6-5-2)。

推論構築を受理するには、その推論結果型を明示的に固定し、元の群・引数対応・型未確定リテラルで選び直した場合も同じコンストラクターを選ばなければならない。そうならなければ明示型引数が必要で、候補を切り替えない。別文脈を必要とする待機引数にも明示型引数が必要。ただ待機しているだけでは単一候補を拒否しない。

```kimi
func wrap<T>(value: T) -> Box<T>
    return Box.init(value@move)   // T from the argument.
// For C<T>.init(x: ref/i32, y: ref/T) and init(x: ref/T, y: ref/T),
// C.init(x, y) with x: ref/(ref/i32) and y: ref/i32 is an error:
// inference picks the first, but with C<i32> fixed both are ambiguous.
```

<a id="s-10-9"></a>

## 10.9. 対象外の形式

実装はコンストラクター固有ジェネリックパラメーター、構造体長さパラメーター、[§10.2](#s-10-2)以外の暗黙Adaptation、[§10.6](#s-10-6)以外の演算子候補、明示オーバーロード選択構文を加えてはならない。他の対象外ジェネリック形式は[§D.2](#s-D-2)。

<a id="s-11"></a>

# 11. プロパティ

<a id="s-11-1"></a>

## 11.1. プロパティの種類

具体的プロパティは、不変記憶領域の`let`、可変記憶領域の`var`、アクセサーだけの`computed`。Contractの`property`要件は操作を保証する（[§11.9](#s-11-9)）。

| 宣言 | `get` | `set` | 宣言初期化子 |
| --- | --- | --- | --- |
| `let` | 省略時は標準 | 禁止 | インスタンスは任意、静的は必須 |
| `var` | 省略時は標準 | 省略時は標準 | インスタンスは任意、静的は必須 |
| `computed` | 本体必須 | 任意、本体必須 | 禁止 |
| Contract `property` | 必須 | 任意 | 禁止 |

インスタンスプロパティはstruct、静的プロパティはgroup・rootgroupに属する。struct内のgroupもインスタンスフィールドを加えず、プロパティは静的のまま（[§22.4.4](#s-22-4-4)）。enumには置けない。ローカル`let`/`var`は束縛で、ローカル`computed`は禁止。値名前空間を使い、属性は[§6.7](#s-6-7)、インライン`has`は要件専用。

**格納プロパティ**は一つの記憶スロット（**フィールド**）を持つ`let`・`var`。格納の有無はアクセサー本体でなく宣言が決め、フィールドへの全ソースアクセスはプロパティのアクセサーに従う。

| 型契約 | 意味 |
| --- | --- |
| 格納`: T` | 記憶型 |
| 格納カスタム`get` / `set` | `get`結果は正確に`T`でCopy証明必須。`set`入力は正確に`T`、結果Unit |
| computed・Contractの`: T` | ゲッター結果型。computed・明示要件のセッター入力は別型でもよい |

型比較は完全構造とOrigin束縛を含む。Copy能力は取得を選ぶもので、`T`か`ref/T`かのように結果型を変えない。

```kimi
struct Counter
    public let limit: i32 = 100
    public var count: i32 = 0
    public computed remaining: i32
        get() -> i32 => self.limit - self.count
```

<a id="s-11-2"></a>

## 11.2. 標準アクセス

本体なし`get`・`set`は標準アクセスを選び、一方のカスタム化は他方を変えない。標準`get`は呼出しでなくPlaceアクセス。取得は[§3.5](#s-3-5)、[§15.1.5](#s-15-1-5)に従い、借用は先に値を取得しない。標準`set`は通常の状態・後始末規則で初期化・置換する。

フィールド操作には、自身と経路上の全祖先格納プロパティについて、次のアクセス可能な標準アクセサーが必要。

| 操作 | 対象フィールド | 各祖先格納プロパティ |
| --- | --- | --- |
| Copy・共有借用 | `get` | `get` |
| Move | `let`は`get`、`var`は`get`と`set` | 自身のMove行 |
| 排他借用 | `get`と`set` | `get`と`set` |
| 書込み | `set` | `get`と`set` |

カスタムアクセサーでは代用できない。カスタム`get`経由の読出し・借用は隠れた記憶領域でなくゲッター結果を使う（[§11.5](#s-11-5)）。カスタム`set`は最終代入先がそのプロパティの場合だけ呼ぶ。それを通る子経路への書込み・Move・排他借用は、メソッドレシーバ・演算子Adaptationを含めエラー。通常の能力・初期化・Origin・Loan・Move Path規則も適用する。単純代入は最終対象の`set`を必要とする（[§13.7](#s-13-7)）。

借用は[§13.5.5](#s-13-5-5)に従う。型`ref/U`のフィールドの`@ref`は`ref/(ref/U)`としてフィールドを借り、裸取得または期待`ref/U`では格納参照をCopyする（[§10.2](#s-10-2)）。`@follow@ref`は参照先を再借用する。

```kimi
// position: Point is Copy, with standard get and custom set.
// object.position.x = 10     // Error: the ancestor position needs a standard set.
var next = object.position
next.x = 10
object.position = next        // OK: calls position's set.
```

<a id="s-11-2-1"></a>

### 11.2.1. Moveと可変性

`var`からのMoveにはアクセス可能な標準`set`が必要だが、呼び出さず、書込み可能レシーバも要求しない。`let`スロットは`set`を持たないため排他借用・書込み不可。Movedな`let`は再初期化できない。不変性は深く伝播せず、格納参照の参照先は固有の能力を保つ。

```kimi
// resource: public standard get, private standard set; holder is owned.
let view = holder.resource@ref    // OK: shared storage borrow.
// let r = holder.resource@move   // Error: set is inaccessible here.
```

<a id="s-11-2-2"></a>

### 11.2.2. フィールド経路と部分Move

safeなフィールドMoveには、所有struct Place、適格な静的Move Path、構築完了、Initializedかつ完全な対象、祖先`drop`条件が必要。借用・オブジェクトレシーバ、静的記憶領域、破棄レシーバ（[§16.3.1](#s-16-3-1)）からsafeに取り出せない。生ポインターはUnsafe規則を保つ。

継承フィールドには型・Origin引数を代入した一意の基底経路で到達する。途中の基底を取得せず、`ref/Derived`を`ref/Base`へ変換せず、元レシーバの分類を保ち、privateアクセスを与えない。基底サブオブジェクト単独のMove・置換・再構築は禁止。

標準アクセスなら兄弟のMove後も残る完全なPlaceを使える。カスタム・computed・Contract要件アクセサーには完全なレシーバが必要で、本体・実装から非重複性を推論しない。

```kimi
struct Holder
    public var resource: Resource
    public var count: i32 = 0
    public computed displayCount: i32
        get() -> i32 => self.count + 1

func refill(holder: Holder) -> Holder
    var object = holder@move
    let item = object.resource@move     // Partial Move.
    let count = object.count            // OK: complete remaining storage.
    // let shown = object.displayCount  // Error: a computed getter needs a complete receiver.
    object.resource = makeResource()    // Restore through the standard set.
    return object@move
```

<a id="s-11-2-3"></a>

### 11.2.3. ジェネリックフィールド

標準アクセサーはCopy制約不要。格納カスタムゲッターには宣言のジェネリック前提で`T is Copy`がProvenであることが必要。カスタムセッターは許される全`T`で有効でなければならない。Copy未証明の裸読出しはエラー（[§8.14](#s-8-14)）。Copyを制約するか、フィールドを借用するか、`@move`で取り出して集約を不完全にする。Move・Loan失敗でオーバーロード選択をやり直さない（[§10.6](#s-10-6)）。

```kimi
struct Box<T>
    public var item: T

func take<T>(box: Box<T>) -> T
    // let x = box.item          // Error: T is not proven Copy.
    return box.item@move          // OK: box is incomplete afterwards.
```

<a id="s-11-3"></a>

## 11.3. アクセサー関数

カスタム・computedアクセサーは入力・結果型を明記し、レシーバだけ省略できる。共通Body（[§14.2](#s-14-2)）を使い、セッターは単項式の結果を捨てUnitで完了する。既定値・`!`境界は禁止。セッターの`value`は通常取得・後始末に従う不変束縛。

格納カスタムアクセサー内の`storage`は再度アクセサーを呼ばずフィールド自身を指す。通常Place規則に従い再宣言不可（[§9.3](#s-9-3)）。computed・要件には`storage`がない。

```kimi
struct Gauge
    public var level: i32 = 0
        get() -> i32
            return storage
        set(value: i32) -> ()
            // let storage = value    // Error: storage cannot be redeclared here.
            storage = if value > 100 => 100 else => value
```

<a id="s-11-3-1"></a>

### 11.3.1. アクセサーレシーバ

レシーバは本体でなく操作が固定する。インスタンス`get`は`self: ref/Self`、`set`は`value`より前の`self: uniq/Self`で、省略時に挿入する。静的アクセサーにレシーバはなく、書けばエラー。Origin注釈は可能（例：`get(self: ref/Self during static) -> T`）。`Self`、逆の借用形、Object Semantics、別Coreなど、他レシーバ型は全てエラー。

カスタム・computed・要件`get`の`x.p`は、`x`を共有レシーバ式として[§7.3.3](#s-7-3-3)の`ref/Self`行で取得する。排他予約を開始せず（[§15.6.7](#s-15-6-7)）、書込み可能な貸出し点も不要（[§15.1.5](#s-15-1-5)）。したがって`let`、所有パラメーター、`rc`/`arc`ペイロード、可変静的記憶領域も読める。

`x.p = v`、複合代入、`++`・`--`は書込み権限で対象を特定（[§13.7.1](#s-13-7-1)）し、カスタム・computed・要件`set`のレシーバをCopy・Moveせず`uniq/Self`として取得する。`objuniq/T`も[§7.3](#s-7-3)の経路で満たせる。`holder@move.item = value`・`point@copy.x = 10`は自ら作った一時値・Copyを更新する（[§3.6](#s-3-6)）。複合更新の対象保護は[§13.7.2](#s-13-7-2)。

排他アクセスや消費が必要な操作は関数とし（[§7.3](#s-7-3)）、[§4.9.2](#s-4-9-2)の命名ペアに従う。オブジェクト借用・基底射影経由はObjectCallCompatible Provenが必要（[§12.7](#s-12-7)）。`ref/Self`アクセサーは形からProvenだが（[§12.7.1](#s-12-7-1)）、`uniq/Self`だけでは証明にならない。

```kimi
struct Meter
    var hits: i32 = 0
    public computed hitCount: i32
        get() -> i32 => self.hits                 // Same as get(self: ref/Self).
    public computed reading: i32
        get(self: uniq/Self) -> i32 => self.hits  // Error: a getter reads through ref/Self.
    public func nextReading(self: uniq/Self) -> i32
        self.hits += 1                            // Advancing state is a function.
        return self.hits
```

<a id="s-11-3-2"></a>

### 11.3.2. アクセサーのアクセス可能性

既定ではプロパティと同じアクセス。明記するなら厳密に狭くする。広い順に`public`、`protected internal`、`protected`または`internal`（互いに順序なし）、`private protected`、`private`。同じ・広い指定はエラー。`set`範囲が`get`範囲に含まれる必要はない。

<a id="s-11-3-3"></a>

### 11.3.3. Non-Copy値のカスタムセッター

`storage`への代入は通常代入（[§13.7.1](#s-13-7-1)）として旧値を破棄し新値を置く。排他レシーバは旧値をMoveで取り出せないが、共有借用やフィールドを初期化済みに保つ操作（[§15.7](#s-15-7)）は可能。完全レシーバで戻る必要がある。記憶領域を変更しなくてもよく、未消費の`value`は呼出し側へ返さず破棄する。

```kimi
struct Holder
    public var item: Resource
        set(self: uniq/Self, value: Resource) -> ()
            storage = value@move

holder.item = makeResource()
inspect(holder.item@ref)          // OK: standard shared access.
// let item = holder.item@move    // Error: the custom set blocks a Move.
```

<a id="s-11-4"></a>

## 11.4. 計算プロパティ

computedには記憶領域、初期化子、本体なしアクセサー、インライン`has`列がない。ゲッター結果はOrigin補完後のヘッダー型と一致する。任意のセッターはUnitを返し、別の入力型を取ってもよい。代入元はセッター入力型に合わせる（[§13.7.1](#s-13-7-1)）。

ゲッターは新値、格納参照のCopy、`self`の借用を返せる。共有レシーバから所有Non-Copy部分を抽出したり、格納`uniq/T`を排他再借用したりできない（[§13.5.5](#s-13-5-5)）。get/set往復での等価性は保証しない。

```kimi
struct Holder
    private var item: Resource
    public init(item: Resource) => self.item = item@move
    public computed view: ref/Resource
        get() -> ref/Resource => self.item@ref

let holder = Holder.init(makeResource())
inspect(holder.view)                     // OK: a let binding is readable.
```

<a id="s-11-5"></a>

## 11.5. ゲッター結果

カスタム・computed・Contract `get`は関数結果を作る。その所有Temporary Placeとインライン子孫は、代入、複合更新、増減、排他借用ができない。括弧、射影、排他レシーバ取得（[§7.3](#s-7-3)）、`match`・`for`のSubject（[§15.1.6](#s-15-1-6)）でも制限を保つため、結果上の`uniq/Self`メソッドは呼べない。さらにゲッターを呼ぶことは可能。返した参照の参照先は固有の能力を保ち、`set`経由のプロパティ更新は可能。

制限はその一時Placeだけに属し、取得してローカル・引数へ渡した値は移動先の規則に従う（[§3.6](#s-3-6)）。借用で一時値の寿命は延びない。標準`get`への`@ref`は一時値でなく記憶領域を借用する。

```kimi
// position: Point is Copy, with custom get and standard set.
// object.position.x = 10        // Error: updates only the getter temporary.
inspect(object.position@ref)     // OK: the temporary lasts through the call.
let view = object.position@ref
// inspect(view)                 // Error: the initializer temporary has ended.
```

<a id="s-11-6"></a>

## 11.6. プロパティ型とOrigin

格納型は注釈、なければ宣言初期化子の型。両方なければエラー。アクセサー・後の代入から推論しない。初期化子の型付けが直接、別の推論プロパティ経由、または包含型のCopy・Owned・レイアウトなどを通して自身の型を必要とするなら、注釈が必要（エラー）。推論初期化子間の循環もエラー。

記憶領域は必要な全Originを明示束縛する（[§15.4](#s-15-4)）。`self`では自己借用記憶領域を作らない。アクセサーはOrigin列でなく借用注釈で呼出し時Originを導入する（[§15.3.4](#s-15-3-4)）。付随`origin`句は除外構文も含むソース順で実行項目より前（[§19.4](#s-19-4)）、プロパティ自身の句はアクセサーより前に置く。継承Origin名は再宣言不可。

格納カスタムアクセサーの`value`と結果の省略Originは、関数の省略規則より先に、`self`でなく記憶型`T`から継承する。明記束縛もそれと検査する。アクセサー入力で記憶領域が許す呼出しを狭めてはならない。computed・要件は通常の関数省略規則を使い、借用入力が`self`だけならゲッター結果Originを`self`へ省略できる。

```kimi
group Tags
    public func tagOf<T>() -> i32
        T is Owned
        return 0

struct Entry
    var tag = Tags.tagOf<Entry>()        // Error: proving Entry is Owned needs tag's inferred Type.
    // var tag: i32 = Tags.tagOf<Entry>() // OK: the annotation breaks the cycle.
```

<a id="s-11-7"></a>

## 11.7. 構築と破棄

宣言初期化子とコンストラクターの初回配置は、アクセサーを呼ばず自身の記憶領域を直接初期化する。初回配置には全流入経路が未初期化で、過去にも初回配置が完了していないことが必要。Move・破棄で履歴は戻らない。宣言初期化子のあるフィールドは初期化済みと数える。

| コンストラクターの書込み | 全経路で初回配置前 | 初期化済み、または混在 |
| --- | --- | --- |
| `let` | 直接初期化 | エラー |
| 標準`set`の`var` | 直接初期化 | 通常の配置・置換 |
| カスタム`set`の`var` | 直接初期化 | エラー |

構築レシーバはカスタム・computedアクセサーを呼べない。標準`get`は初期化済みの自身の記憶領域をCopy・借用できるが、Non-Copy Move、継承記憶領域へのアクセス、`self`全体の取得・借用は禁止。他は[§6.3](#s-6-3)に従う。レイアウト、Copy導出、暗黙構築、部分Move、破棄は格納フィールドと基底成分だけを使い、破棄はアクセサーを呼ばない。

```kimi
// In a constructor: level has a custom set and no initializer.
if ready
    self.level = 100   // First placement on this path; no setter call.
else
    self.level = 200
// self.level = 300    // Error: construction cannot call the custom set.
```

<a id="s-11-8"></a>

## 11.8. 静的記憶領域

group・rootgroupの格納プロパティには初期化子とOwnedな完全記憶型が必要（[§15.2.3](#s-15-2-3)）。各スロットは最初の実アクセスで初期化する（[§22.4](#s-22-4)）。アクセサー呼出しが初期化するのは実行で触れるスロットだけ。

初期化済み不変静的記憶領域の共有借用は`during static`にできる。可変静的記憶領域（インライン部分Placeも含む）の借用は、本体で上限を持つ有限Originとなる（[§15.6.5](#s-15-6-5)）。そこに格納された参照のCopyはその記憶領域の借用ではなく、元の参照先とOriginを保つ。

```kimi
group Config
    public let limit: i32 = 100
    public var name: string = ""

func limitRef() -> ref/i32 during static
    return Config.limit@ref       // OK: immutable static storage.
// func nameRef() -> ref/string during static => Config.name@ref  // Error: mutable.
```

<a id="s-11-9"></a>

## 11.9. Contractのプロパティ要件

`property`要件はインスタンス専用で、`get`必須、`set`任意。アクセサーアクセス修飾子、属性、既定値、`!`境界、初期化子、記憶領域、本体は持たない。`has get`は値結果の`get(self: ref/Self) -> T`、`has set`は`set(self: uniq/Self, value: T) -> ()`を要求する。明示形は[§11.3.1](#s-11-3-1)のレシーバでシグネチャを書き、セッター入力は`T`と異なってもよい。オブジェクトハンドル・基底射影は[§11.3.1](#s-11-3-1)、[§9.6.1](#s-9-6-1)に従いレシーバを満たす。

```kimi
contract Counted
    property count: i32 has get
contract ReplaceableItem
    property item: ref/Resource
        get() -> ref/Resource
        set(value: Resource) -> ()
```

<a id="s-11-9-1"></a>

### 11.9.1. 実装の互換性

通常メンバー探索で実装を選び、継承適合は対応を保持する（[§8.6](#s-8-6)）。失敗しても別名・基底を試さない。要件のSelf、ジェネリック引数、関連型を置換後、関数要件互換性を適用する。暗黙変換・より強い事前条件は不可。記憶型とヘッダー型の一致は不要。Unknownなジェネリック能力は互換性の証明にならない。

<a id="s-11-9-2"></a>

### 11.9.2. 標準アクセスの証拠

型`F`のフィールドの標準アクセスは、次の橋渡しだけで要件を実装する。

| 要求操作 | 標準実装 |
| --- | --- |
| 共有レシーバ→`F` | `F`のCopyがProvenならCopy |
| 共有レシーバ→`ref/F` | フィールドの共有借用 |
| 排他レシーバ、入力`F`→Unit | アクセス可能な`var`の標準`set` |

各橋渡しには[§11.2](#s-11-2)で操作に必要な標準アクセサーが適合宣言位置でアクセス可能でなければならず、結果Originはレシーバで制限する。カスタム`get`の裏の記憶領域は使えず、他のものは生成しない。継承フィールドは適合経路の前提で標準射影（[§11.2.2](#s-11-2-2)）して到達し、基底サブオブジェクトレシーバ（[§9.6.1](#s-9-6-1)）は作らない。橋渡しは名前解決候補を加えない。

```kimi
contract Viewable
    property item: ref/Resource has get

struct Slot
    Self is Viewable
    public var item: Resource    // OK: shared borrow bridge.
    // A by-value `property item: Resource has get` would need Resource to be Copy.
```

<a id="s-12"></a>

# 12. 式

式は値を作るか制御を転送する。構文は[§F.9](#s-F-9)。

<a id="s-12-1"></a>

## 12.1. 分類と文脈

通常完了する式はUnitも含む型付き結果を作る（[§3.1.3](#s-3-1-3)）。値文脈・結果を捨てる文脈（[§14.2](#s-14-2)）が利用方法を決め、捨てても効果と型・所有権・破棄の全検査は残る。

字下げ本体は式ではない。複数操作から値を得るには選択式・`do`式を使う。`unsafe`、`defer`、`require`、`_ = expression`は文。`let`・`var`宣言は式でも条件束縛でもない（[§14.2.3](#s-14-2-3)）。

```kimi
_ = compute()           // Discard: effects and checks are kept.
// let x = (let y = 1)  // Error: a declaration is not an expression.
```

<a id="s-12-2"></a>

## 12.2. 評価順

別規定がなければ、オペランドは左から右に一度ずつ評価する。

| 式 | 順序 |
| --- | --- |
| `a() + b() * c()` | `a`、`b`、`c`、`*`、`+` |
| `receiver().method(a(), b())` | レシーバ、呼出し先解決、`a`、`b`、呼出し |
| `array()[index()]` | 対象、添字、要素アクセス |
| `(a(), b())`、`[a(), b()]` | 要素のソース順 |
| `[key(): value()]` | 項目ごとにキー、重複検査、値、挿入（[§12.4.3](#s-12-4-3)） |
| `start()..end()` | 始点、終点。省略境界は評価せず、境界検査もしない |
| `"\(a()) \(b())"` | 各補間を順に評価・書込み。失敗で残りを停止 |

`and`・`or`・選択は必要なオペランド・分岐だけを評価する。代入は右辺が先（[§13.7](#s-13-7)）。型引数・長さ引数・Adaptation対象は実行時評価しない。

制御転送による完了、発散、Abortは後続オペランドと外側操作を飛ばす。飛ばす構文も名前・型・転送先を検査する。`#if`・`#switch`で除外する構文は[§19](#s-19)に従う。

```kimi
let ok = ready() and check()  // check() runs only when ready() is true.
```

<a id="s-12-3"></a>

## 12.3. リテラルの型付け

<a id="s-12-3-1"></a>

### 12.3.1. 期待型と既定型

束縛・パラメーター・結果の宣言型は期待型としてコレクション要素や選択分岐などに伝わり、それ以外ではオペランドから型を決める。組込み数値演算は単一の数値型を要求し、幅・符号・整数と浮動小数点を暗黙に混ぜない。`bool`・`char`・数値間の暗黙変換もなく、条件は`bool`。

型未確定整数リテラルは値が収まる期待整数型、浮動小数点リテラルは期待`f32`・`f64`を取る。他の場合、整数は`i32`、浮動小数点は`f64`、長さ文脈は`isize`が既定（[§4.2](#s-4-2)）。他に型の根拠がない範囲境界・`^`オペランドも`i32`（[§4.6.2](#s-4-6-2)）。既定型に収まらなければ型の明記が必要。呼出しでは候補選択後だけに適用し（[§10.2.1](#s-10-2-1)）、候補比較・適合選択（[§4.7.1](#s-4-7-1)）中には使わない。整数を暗黙に浮動小数点型へ合わせない。

```kimi
let a: i64 = 10
let b = a + 20       // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64    // A typed operand is converted explicitly.
let e = a + c        // Error: i64 and i32 do not mix.
```

<a id="s-12-3-2"></a>

### 12.3.2. 直接リテラル

**直接リテラル**は数値リテラルと、任意の一つの直接前置符号`+`・`-`からなる。間に空白を置け、全体を任意数の括弧で囲める。符号付き値が対象型の値なら合い、その型が単項演算子を定義するかには依存しない。`-(1)`など他への符号は単項演算子で、二オペランド間の`-`は二項。

```kimi
let a: i8 = -128              // Direct literal: fits.
let b: i8 = ((- 128))         // The same direct literal.
let c: i8 = -(128)            // Error: 128 does not fit i8.
let d: u32 = -0               // The value 0 fits.
let e: u32 = -1               // Error: -1 is not a u32 value.
let f: Wrapping<u8> = -(1)    // Unary - of Wrapping<u8> applied to 1.
```

<a id="s-12-3-3"></a>

### 12.3.3. リテラルだけの式

型付け上、次だけをリテラルだけの式とする。分類を混ぜず、`1 + 1.0`は無効。

- **整数**：型未確定整数、またはその式への括弧、単項`+`・`-`、算術・ビット・シフト演算。整数型とラップ整数型に合う。
- **浮動小数点**：型未確定浮動小数点、またはその式への括弧、単項`+`・`-`、二項`+`・`-`・`*`・`/`。`f32`・`f64`に合う。
- **位置・範囲**：整数だけの式をオペランドにする`^a`（[§4.6.1](#s-4-6-1)）、または明記した境界が整数・末尾基準のリテラルだけである範囲（[§4.6.2](#s-4-6-2)）。

全直接リテラルが受け取る型に収まり、全演算子がその型で定義されれば合う。ラップ整数型には、その整数型引数に収まる整数リテラルが合う。型は算術・ビット演算の両オペランド、単項オペランド、範囲の各境界の`S`・`E`、`^`の`FromEnd<T>`の`T`へ伝わる。シフトは左辺だけに伝え、回数は独立に型付けする（[§13.3.1](#s-13-3-1)）。数値変換で対象型に直接合わせるのは直接リテラルだけなので（[§13.5.4](#s-13-5-4)）、`(200 + 100)@u8`は`i32`で計算する。

期待型に合わせる際は、葉と演算子の型だけを検査し、計算結果は検査・候補選択に使わない。実行時は合わせた型で各演算を行う。`127 + 1`を`i8`に合わせれば実行時Abort、`Wrapping<i8>`なら`-128`、浮動小数点は各段階で丸める。必須定数評価は別規則なので`[(4 / 0) of u8]`はコンパイル時エラー。長さ形式（[§4.2](#s-4-2)）、ConstantIndexExpression（[§15.1.3](#s-15-1-3)）、リテラルパターン（[§14.8.1](#s-14-8-1)）の範囲は広げない。

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(2 * 5)                            // Error: both candidates fit in the same way.
let wide: Range<i64, i64> = 0..(1 << 40) // 1 is i64; the count 40 is i32.
let f = (1.0 + 1.0)@f32                  // Computed in f64, then converted.
```

<a id="s-12-4"></a>

## 12.4. 基本式

<a id="s-12-4-1"></a>

### 12.4.1. 名前・リテラル・括弧

| 形式 | 意味 |
| --- | --- |
| `name` | 見える束縛・関数などの名前付き実体 |
| `Self.member`、`isize.MaxValue` | 現在型・プリミティブ型の静的メンバー（[§9.6](#s-9-6)） |
| `123`、`1.5`、`true`、`'€'`、`"text"` | リテラル（[§2](#s-2)） |
| `"value = \(value)"` | 補間文字列（[§12.4.2](#s-12-4-2)） |
| `null` | 文脈で型付けするraw nullポインター（[§5.1](#s-5-1)） |
| `()` | Unit値 |
| `(value)` | Placeを保つ括弧 |
| `(value,)`、`(a, b)` | タプル |
| `[a, b]`、`[]` / `[key: value]`、`[:]` | 配列 / Dictionaryリテラル |

配列は一つの要素型、Dictionaryは一つのキー型・値型を持つ。空リテラルには期待型が必要。固定長配列が期待される場合（[§4](#s-4)）以外、配列・Dictionaryリテラルは必須Kimi宣言を使う（[§22.1](#s-22-1)）。要素型未解決時に万能object型へ切り替えない。

```kimi
let pair = (10, "ten")
let names = [1: "one", 2: "two",]
let empty: Array<i32> = []
let unknown = []        // Error: an empty literal needs an expected Type.
```

<a id="s-12-4-2"></a>

### 12.4.2. 補間文字列

所有`string`を作る。各補間は`Utf8Writer.write`の共有入力に合わせ（[§10.2](#s-10-2)）、選んだ参照先型に`Utf8Format`が必要。借用型はそれを転送しない。裸のPlaceをMoveしない。評価・書式化は[§22.10](#s-22-10)、`$tryWrite(writer, literal)`は[§22.10.4](#s-22-10-4)。

```kimi
let name = "Ada"
let count = 3
let line = "name = \(name), count = \(count)"
```

<a id="s-12-4-3"></a>

### 12.4.3. Dictionaryリテラル

等価なキーの重複はエラー。静的検査は真偽値・整数・`char`・通常文字列・Unitのリテラルキー（括弧可、整数は直接リテラル可、[§12.3.2](#s-12-3-2)）をキー型に合わせ、到達不能構文も含む全項目間で値比較する。他のキー、定数`let`、演算式は検査せず、最適化で対象を増やさない。

その他の項目はソース順に処理する。キーを一度評価し、等価キーが既にあれば値・後続項目を評価する前にAbort（[§17.4](#s-17-4)）。なければ値を一度評価して挿入する。通常完了しなかった項目は挿入せず、後続も処理せず、部分Dictionaryを結果にしない。完了済み効果は残る。

キー等価性はEquatableの`stored.equals(search)`を使い、Hash適合は不要。比較の回数・順序は未規定で、各呼出しの効果は残る。`f32`・`f64`では同じ型の全NaNを等価、両符号のゼロも等価とするが、`==`は変更しない（[§13.4.1](#s-13-4-1)）。利用者定義等価性は同値関係であり、Dictionaryがキーを保持・検査・挿入する間に変化してはならない。違反時は結果が未規定またはAbortとなりうるが、メモリー安全性は破らない。

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()] // Aborts at the second key; second() is not called.
let bad = [1: "a", (1): "b"]        // Error: duplicate literal key.
```

<a id="s-12-5"></a>

## 12.5. アクセスと呼出し

<a id="s-12-5-1"></a>

### 12.5.1. メンバーアクセス

`expression.name`は修飾名前解決で選び（[§9.6](#s-9-6)）、`self`を挿入しない。値参照自身にメンバーがなければ参照先へ進む（[§3.4.3](#s-3-4-3)）。`.`の後はメンバー名か、タプル要素を選ぶ範囲内の十進整数。動的メンバー探索はない。型修飾部は値を作らない束縛済みコンテナーパス（[§9.7.1](#s-9-7-1)）。`.init(...)`は構築式（[§6.3](#s-6-3)）で、値メンバー呼出しではない。プロパティは[§11.1](#s-11-1)。生ポインターは暗黙追跡せず、Unsafeブロック内で`(*pointer).name`と書く。

```kimi
let first = (10, "ten").0
var collection = makeCollection()
collection.count = 10   // Needs a setter and write permission.
```

<a id="s-12-5-2"></a>

### 12.5.2. 呼出しとジェネリック適用

`callee(a, b)`は関数・メソッド・関数値の呼出しで、末尾コンマ可。タスク呼出しは`callee(task; a)`のようにタスクを先頭に書く（[§24.3](#s-24-3)）。明示ジェネリック引数は[§8.1.3](#s-8-1-3)、`<`が型引数開始かの判定は[§F.9.3](#s-F-9-3)。

引数対応・選択は[§10](#s-10)、名前付き・省略は[§7.2](#s-7-2)。関数値は位置引数だけで、unsafeは[§7.6](#s-7-6)。ジェネリック引数は[§10.5](#s-10-5)の境界内で型・長さスロット（[§4.4](#s-4-4)）だけを埋める。解決後、一致する明示的特殊化を選ぶ（[§8.13](#s-8-13)）。`T(args)`は構築ではなく`T.init(args)`と書く（[§6.3](#s-6-3)）。

```kimi
let a = largest<i64>(1, 2)
let c = i<n            // A comparison: no matching >.
let d = Point(1, 2)    // Error: write Point.init(1, 2).
```

<a id="s-12-6"></a>

## 12.6. オブジェクトのメンバー呼出し

メンバー、オーバーロード、アクセス、公開契約はレシーバの実効型（[§14.10.1](#s-14-10-1)）から選ぶ。非virtualはその実装、virtualは同じスロットの動的型実装を使う（[§6.4.3](#s-6-4-3)）。選んだ宣言のサブオブジェクトへレシーバを調整する（[§9.6.1](#s-9-6-1)）。完全な派生型を隠す射影には選択後にビューアップキャストのOwned消去証明が必要。ただしoverride本体の字句上の`self`操作は[§6.4.5](#s-6-4-5)に従う。

オブジェクト借用・基底射影の呼出しにはObjectCallCompatible Provenが必要（[§12.7](#s-12-7)）。例外は**完全Sealedペイロード**。View Targetが正確な完全Sealed型なら、`ref/Self`・`uniq/Self`を暗黙（[§7.3](#s-7-3)）または`handle@follow.method()`でペイロード追跡（[§13.5.5](#s-13-5-5)）し、通常の完全値呼出しにできる。排他なら[§15.1.5](#s-15-1-5)も必要。通常引数には常に明示`@follow`が必要。openビュー・基底サブオブジェクトは全値置換や不完全にするMoveを行わない。

<a id="s-12-7"></a>

## 12.7. オブジェクトレシーバの互換性

**ObjectCallCompatible**は、借用レシーバ呼出しが完全性、アクセス権限、型・Origin・Loan契約を保つ公開保証。各呼出し操作（関数、カスタム・computedの`get`/`set`、標準Contractの証拠の橋渡し）はProvenまたはNotProvenを一つ持ち、実体化ごとには変わらない。標準`get`・`set`は呼出しでなくPlace操作なので、`Box<T>.item@ref`に`T is Copy`は不要。

状態は公開契約の一部で、本体と全明示的特殊化本体に対する[§12.7.1](#s-12-7-1)の効果規則の最小不動点。解析精度・最適化でなく、この規則だけで決まる。本体変更は状態とオブジェクト借用・基底射影呼出しの可否を変えうる。宣言Kotonohaの利用者は公開状態に依存する。選択後に検査し、候補除外・再選択に使わない（[§7.3](#s-7-3)）。呼出し側前提、型引数、動的型、選択した特殊化で強められない。不正な本体はエラー、検証未対応の本体はUnsupported（[§23.1.1](#s-23-1-1)）であり、NotProvenではない。

```kimi
open struct Base
    public var count: i32 = 0
    public init() => ()
    public virtual func g(self: objref/Self) -> i32 => 0
    public virtual func f(self: objref/Self) -> i32 => 0
    public func bump(self: uniq/Self) => self.count += 1            // Proven.
    public func reset(self: uniq/Self) => self@follow = Base.init() // NotProven.

struct Derived<U> : Base
    var item: U
    override func f(self: objref/Self) -> i32
        return self.g()   // Projection of lexical self: covered by the slot's evidence.

func use(d: uniq/Derived<i32>)
    d.bump()
    d.reset()  // Error: would replace the base inside Derived.
```

<a id="s-12-7-1"></a>

### 12.7.1. 効果規則

許される全型・Origin束縛を宣言シグネチャ・制約・前提（[§8.3](#s-8-3)～[§8.15](#s-8-15)）で検証する。本体から推論した制約は使わず、型検査する全経路を数える。

効果対象とレシーバ・入力ルートとの**記憶領域関係**は、Whole（ルート）、Base（基底辺だけ）、Part（フィールド・タプル・配列要素を通る）、Separate（[§15.6.2](#s-15-6-2)で非重複と証明済み）、MayAlias（それ以外）。格納参照経由の参照先は、その参照が持つLoanが指すPlaceで関係付ける（[§15.6](#s-15-6)）。あるルートとの分離は別ルートの証明にならず、ペイロード追跡でWholeをPart・Separateには変えない。

| レシーバへの効果 | 結果 |
| --- | --- |
| Whole・Baseの置換、再構築、所有権取得 | 対象が完全と証明され、[§15.7](#s-15-7)で有効な場合以外は違反 |
| 不完全にするMove | 後で復元しても違反 |
| Partの置換・Exchange、許可された読出し・借用、Separateへの効果 | 違反なし |
| 結果・格納・キャプチャー・被呼出し先へ、自身や基底への無制限の排他アクセスを逃す | 有効な完全対象借用以外は違反 |
| MayAlias、または到達しうるunsafeアクセス | 未証明効果 |
| 呼出し、アクセサー、既定値、後始末 | 引数・キャプチャーを通して被呼出し側効果を対応付ける |

被呼出し側の全体置換が呼出し側のPartに対応しても、その対象の基底サブオブジェクト制限は残る。許可済み効果は別名参照しうる対象でも許可されたまま。

数えるのはレシーバとそこから派生したものを通して到達する操作だけ。レシーバLoanは呼出し全体で生存する（[§15.6.4](#s-15-6-4)）ため、別引数・キャプチャー・静的アクセス・未知効果の関数など別経路から同じ記憶領域を操作すれば、その呼出しで拒否（[§15.6.2](#s-15-6-2)）、rawなら未定義動作（[§5.2.1](#s-5-2-1)）。`ref/Self`が到達するのは読出し・共有借用だけなので、そのレシーバを持つ有効な操作はProvenとなる。全カスタム・computed `get`と共有標準証拠の橋渡し（[§11.9.2](#s-11-9-2)）も含む。

**排他入力の上限。** 被呼出し側は[§8.11.3](#s-8-11-3)規則1の範囲だけ入力へ作用し、結果はシグネチャの依存を保つ（[§15.6.4](#s-15-6-4)）。`ref`入力は読出し・共有借用だけ（[§15.6.2](#s-15-6-2)）。排他入力の効果上限は、unsafeを含め実対象の完全性を保つ一回の置換で（[§5.2.3](#s-5-2-3)）、内部の未証明効果もこれを超えない。`Kimi.Intrinsics.replace`・`exchange`はその置換、`swap`はExchange（[§15.7](#s-15-7)）。この上限はレシーバPartを保つが、virtualスロットの完全性保証（[§6.4.5](#s-6-4-5)）がなければWhole・Baseに違反する。その保証が免除するのはこの上限だけで、他効果・Loanはなお数える。

**呼出し。** 効果は[§15.6.4](#s-15-6-4)の要約領域で再帰を越え最小不動点まで伝播する。再帰だけでは未証明にならない。間接・ジェネリック要件呼出しは要件効果境界（[§8.11](#s-8-11)）、Callable制約付き入力経由は実引数で合成する（[§8.11.4](#s-8-11-4)）。検証済み要約・要件境界がなければ排他入力の上限と、静的記憶領域に対する未知効果（[§15.6.4](#s-15-6-4)）を加える。レシーバなし関数は入力ルート効果を持つが状態は持たない。不動点は循環適合を証明せず、通常どおりFinalityを適用する。

<a id="s-12-7-2"></a>

### 12.7.2. 実装群

許容束縛のどれも違反・未証明効果を生まなければ、その実装は[§12.7.1](#s-12-7-1)を通る。通常本体と明示的特殊化集合の全実装が、呼ばれるかによらず通る場合だけ操作はProven、それ以外はNotProven。集合は環境選択・宣言収集後に閉じる。通常本体は特殊化対象を差し引かず全許容束縛で、各特殊化は置換契約で検証する。

互換性のない特殊化は操作をNotProvenにするが、その理由だけで特殊化自体をエラーにはしない。宣言シグネチャ・制約・Safety・Effectの義務は引き続き適用する。使用は実装選択前に元の公開契約で検査する（[§8.13.3](#s-8-13-3)）。

<a id="s-13"></a>

# 13. 演算子と代入

<a id="s-13-1"></a>

## 13.1. 結合と評価順

優先順位・結合規則は文法（[§F.9](#s-F-9)）、Adaptation対象に続けられる後置接尾辞は[§F.9.4](#s-F-9-4)で定める。括弧は演算子のオペランドを決めるだけで、型の妥当性や評価順は変えない。評価は[§12.2](#s-12-2)、`and`・`or`は短絡する（[§13.4](#s-13-4)）。

```kimi
let total = f() + g()     // f's effects happen before g's.
let scaled = (a + b) * c  // Grouping chooses operands, not order: a, b, then c.
```

<a id="s-13-2"></a>

## 13.2. 単項演算子

| 演算子 | オペランドと結果 |
| --- | --- |
| `+value` | 数値を変えずに返す |
| `-value` | 符号付き整数・ラップ整数・浮動小数点の組込み符号反転、または末端型のNegatable要件（[§13.3.3](#s-13-3-3)）。符号なし整数はエラー |
| `not value` | `bool`の否定 |
| `*pointer` | 生ポインターの参照先Place（[§5.2](#s-5-2)）。safe参照・ハンドルは`@follow`（[§13.5.5](#s-13-5-5)） |
| `^value` | 任意の`T is PrimitiveInteger`の値を持つ`FromEnd<T>`。解決時に検査（[§4.6.1](#s-4-6-1)） |
| `++target`、`--target` | 増減して新値を返す |
| `target++`、`target--` | 増減して旧値を返す |

増減には読書き可能な整数・ラップ整数のPlaceまたはプロパティが必要。対象特定・読出し・書込みは各一度。演算・Abort検査・式結果は読出し型（`get`結果型）を使い、書く値をセッター入力へ合わせる（[§13.7.1](#s-13-7-1)）。前置形でも再読出ししない。他は複合代入に従う（[§13.7.2](#s-13-7-2)）。

```kimi
var count: i32 = 1
let before = count++ // before = 1, count = 2
let after = ++count  // after = 3, count = 3
```

<a id="s-13-3"></a>

## 13.3. 算術・ビット・シフト演算子

二項`+`・`-`・`*`・`/`・`%`と単項`-`は、[§13.3.4](#s-13-3-4)で選ぶ組込み操作または算術Contract（[§13.3.3](#s-13-3-3)）を使う。ビット・シフト・増減は組込み専用。

<a id="s-13-3-1"></a>

### 13.3.1. 組込み数値演算子

組込み数値（[§13.3.3](#s-13-3-3)）の`+`・`-`・`*`・`/`は両辺が同じ型で、その型を返す。`%`は整数・ラップ整数だけ。整数除算はゼロ方向に切り捨て、非ゼロ余りの符号は被除数と同じ。

整数・ラップ整数（[§3.1.1](#s-3-1-1)）の算術・増減・複合代入は数学的結果を求める。表現不能なら通常整数はAbort、ラップ整数はビット幅`N`の法`2ᴺ`で合同な唯一の値へ**ラップ**する。両者ともゼロ除算・ゼロ剰余、`0 <= count < N`外のシフト回数はAbort。定数評価も含め[§17.4](#s-17-4)に従う。

| 式（32ビットの`MIN`、`MAX`） | `i32` | `Wrapping<i32>` |
| --- | --- | --- |
| `MAX + 1` | Abort | `MIN` |
| `MIN / -1` | Abort | `MIN` |
| `MIN % -1` | `0` | `0` |
| `x / 0`、`x % 0` | Abort | Abort |

ラップ整数は整数引数の演算子を持ち、二項の両辺はその同じラップ型。全ラップ整数に単項`-`がある。

`&`・`|`・`^`は同じ整数・ラップ整数型のビットAND・OR・XORで、boolは不可。`<<`・`>>`は左辺型を返し、回数は任意の通常整数型（ラップ型不可）。左シフトは上位ビットを捨て、右シフトは符号付きなら符号拡張、符号なしならゼロ拡張する。捨てたビットは表現不能結果とは扱わない。

`f32`・`f64`はIEEE 754の最近接・偶数丸めに従い、無限大・NaN・符号付きゼロを含む。ゼロ除算でAbortしない。丸め・NaN結果が変わりうる再結合・融合は禁止。

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010

func lowestBit(value: u32) -> u32
    let w = value@Wrapping<u32>
    return (w & -w)@u32     // -w wraps; the AND keeps the lowest set bit.
```

<a id="s-13-3-2"></a>

### 13.3.2. 文字列と生ポインター

`string`には算術・ビット・シフト・連結演算子がない。safe参照層経由も含めstringをオペランドとする`+`・`+=`は、利用者側の演算提供型があってもエラー。連結は埋込みを借用して所有stringを作る補間リテラル（[§12.4.2](#s-12-4-2)）、段階的な組立てはバイトバッファー（[§22.9.2](#s-22-9-2)）を使う。生ポインター算術は[§5.3](#s-5-3)の形式だけ。

```kimi
let first = "Hello, "
let joined = "\(first)world"   // An owning string; first is borrowed.
// let bad = first + "world"   // Error: strings are joined by interpolation.
```

<a id="s-13-3-3"></a>

### 13.3.3. 算術Contract

次のpublicなKimi Contractは綴りでなく宣言で識別し、それぞれ完全値型の`associate Output`を宣言する。

| 操作 | 通常Contractと要件 | Left Contractと要件 |
| --- | --- | --- |
| `+` | `Addable<Rhs>.added` | `LeftAddable<Lhs>.addedFrom` |
| `-` | `Subtractable<Rhs>.subtracted` | `LeftSubtractable<Lhs>.subtractedFrom` |
| `*` | `Multipliable<Rhs>.multiplied` | `LeftMultipliable<Lhs>.multipliedFrom` |
| `/` | `Dividable<Rhs>.divided` | `LeftDividable<Lhs>.dividedFrom` |
| `%` | `RemainderProvider<Rhs>.remainder` | `LeftRemainderProvider<Lhs>.remainderFrom` |
| 単項`-` | `Negatable.negated` | なし |

通常・Left要件の形は次のとおり。単項は`func negated(self: ref/Self) -> Self.Output`。全てsafe、非タスク、既定値なしの値返却関数で、Copy性によらず入力は共有借用。Leftは`left op self`を計算し、通常要件から自動生成しない。

**組込み数値型**は数値型（[§3.1](#s-3-1)）、PrimitiveIntegerがProvenな型パラメーター、その`Wrapping<T>`。**利用者側の演算提供型**は、外側Semanticsがownerで組込み数値でないstruct・enum。

明示的な通常・単項適合には利用者側のSelfが必要。Left適合には加えて組込み数値の`Lhs`が必要。通常の`Rhs`は外側ownerの任意の完全値型で、タプルや借用を含む型でもよい。直接stringのオペランドは両側とも不可。Self上で[§8.6](#s-8-6)に従い適合を宣言し、外部で型の組を登録しない。定義時に全許容束縛で適格性を証明し、使用側はそれに依存できる。外側ownerはOwned能力とは異なる。

Outputは本体でなく関連型推論（[§8.5.5](#s-8-5-5)）で決め、必要なら`T.(Multipliable<R>).Output`と書く。Selfと異なる型、Result、外部借用を含む型でもよく、Copy・Ownedは不要。代数法則、純粋性、全域性、効果は保証しない。

各組込み数値`N`は相手型・Outputとも`N`の適合を持ち、混合型・組込みLeft適合はない。整数、`Wrapping<I>`、`f32`、`f64`は五つの二項Contractを持つが、浮動小数点にRemainderProviderはない。符号付き整数、`Wrapping<I>`、浮動小数点はNegatableも持つ。証拠は[§13.3.1](#s-13-3-1)どおりに動作し、数値型に名前付きメンバー・コンストラクターを加えない。要件は`T.added`などContract経由だけで到達する。PrimitiveInteger前提は`T`の五つの二項適合を導くがNegatableは導かず、`Wrapping<T>`なら六つ全てを導く。これはジェネリック利用に使え、演算経路は変えない（[§13.3.4](#s-13-3-4)）。

`T is Addable<R>`の下の`T.added`は選択要件・実装を保持するFunction Itemで、通常規則で呼出し・格納・変換できる（[§7.7](#s-7-7)、[§10.7](#s-10-7)、[§8.11.4](#s-8-11-4)）。

```kimi
public contract Multipliable<Rhs>
    associate Output
    func multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output

public contract LeftMultipliable<Lhs>
    associate Output
    func multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output
```

```kimi
struct Vec2
    Self is Addable<Vec2>
    Self is LeftMultipliable<f64>
    associate Addable<Vec2>.Output is Vec2
    associate LeftMultipliable<f64>.Output is Vec2
    public let x: f64
    public let y: f64
    public init(x: f64, y: f64)
        self.x = x
        self.y = y
    public func added(self: ref/Self, right: ref/Vec2) -> Vec2
        return Vec2.init(self.x + right.x, self.y + right.y)
    public func multipliedFrom(left: ref/f64, self: ref/Self) -> Vec2
        return Vec2.init(left * self.x, left * self.y)

let a = Vec2.init(1.0, 2.0)
var total = a + 2.0 * a // LeftMultipliable<f64>, then Addable<Vec2>.
total += total          // Both operands are shared borrows; no Copy is needed.
// let b = a * 2.0      // Error: Vec2 has no Multipliable<f64>.

func sumPair<T>(left: ref/T, right: ref/T) -> T
    T is Addable<T>
    T.(Addable<T>).Output is T
    let add = T.added   // A Function Item.
    return add(left, right)
```

<a id="s-13-3-4"></a>

### 13.3.4. 経路選択

構文・明示文脈・公開前提から型を決め、safe値参照層を能力・Origin・Loanを保って末端型までたどる（[§3.4.3](#s-3-4-3)）。未知の型パラメーター・関連型は末端とし、オブジェクト・rawペイロードはたどらない。適合探索前に静的に経路を決める。

| 末端オペランド型 | 経路 |
| --- | --- |
| 両辺が組込み数値 | [§13.3.1](#s-13-3-1)の組込み |
| 左辺が利用者側の提供型 | 左型の通常Contract |
| 左辺が組込み数値、右辺が利用者側の提供型 | 右型のLeft Contract |
| [§5.3](#s-5-3)の生ポインター形 | 生ポインター規則 |
| その他 | 提供元なしのエラー |

両辺組込み数値なら算術Contract前提があっても組込みを使う。他の組では`L is Addable<R>`など直接適用可能な根拠が`L`分類なしに通常経路を固定する。数値片側だけでは組込みに決まらず、`S is PrimitiveInteger, V is LeftMultipliable<S>`の`s * v`は右側提供元を使う。同じ組への`L is Addable<R>`と`R is LeftAddable<L>`は矛盾でエラー。

固定提供元の適合候補を適格性と、`ref/Rhs`・`ref/Lhs`に構造的に合うことを検査する。まだ取得せず、型付きオペランドを再型付けしない。同一適合への経路は結合（[§8.9.2](#s-8-9-2)）し、未決定の条件付き候補は落とさない。複数なら曖昧で順位付けせず、自由関数・extension・変換・反対提供元を探さない。不正適合はエラーで経路変更の理由にならない。選択後に取得・Origin・Loan・効果と、結果が期待型に合うことを検査し、失敗でも再選択しない。期待結果は選択に使わない。

リテラルは[§12.3](#s-12-3)で合わせ、既定型は[§10.2](#s-10-2)に従う。リテラルだけのオペランドを固定提供元の相手型に合わせ、リテラルから利用者型を作らない。左の数値リテラルだけの式は、既知の右提供型に対して数値領域を確立する。残る全候補に共通の相手型は入れ子呼出し・関数式を導ける（[§10.5](#s-10-5)）。Neverは方向・相手型の根拠にならない。

定義検査で経路、Contract、要件、Output、たどる層、取得を固定し、実体化で切り替えない。参照をたどっても参照型に適合は与えない。`value: ref/T`には`T is Multipliable<f64>`を要求し、`ref/T`への要件にはしない。

```kimi
// Vec2 as in the previous example (Addable<Vec2>, LeftMultipliable<f64>); func make<T>() -> T.
func sum<T>(a: T, b: T) -> T
    T is PrimitiveInteger and Addable<T>
    return a + b          // Built-in route: operands are read as values.

let v = Vec2.init(1.0, 2.0)
let w = v + make()        // The unique counterpart Vec2 guides make.
let q = make<f64>() * v   // The Left Contract does not infer make's Type.
// let r = 2 * v          // Error: the integer literal does not fit the counterpart f64.
```

<a id="s-13-3-5"></a>

### 13.3.5. 算術演算の評価

左辺を評価・取得してから右辺へ進む。右提供元を`right.method(left)`へ書き換えない。組込みは各辺評価時に値を読み、Contract経路は数値を含む両入力を共有借用する。右辺評価中も左Loanは生存する。明示`@move`・`@copy`は先に実行し、借用する一時値を作れる。演算専用Loanは完了時に終わり、結果はそれに依存できない。Result OutputはResultのままなので、`try (a / b)`と書く。`try a / b`は`(try a) / b`。

<a id="s-13-4"></a>

## 13.4. 比較と論理演算子

`==`・`!=`・`<`・`<=`・`>`・`>=`はboolを返す。数値両辺は同型で、ラップ整数の順序は元整数の順序（循環順序ではない）。charはUnicodeスカラー値、stringは正規化・ロケールなしのUTF-8バイト辞書順。bool・Unitは等価比較だけ。浮動小数点の`+0.0 == -0.0`は真、NaNがあれば`!=`だけ真で他五つは偽。

対応要素が比較可能なタプルは、等価性を要素ごと、順序を辞書順に、左から決定まで比較する。順序不能な組があれば四つの順序演算子は全て偽。

safe値参照層（ref・uniq・適格なペア層）は取得せず末端Placeを表す（[§3.4.3](#s-3-4-3)）。末端型は同一または一方Neverである必要があり、その型の比較を使う。外側Originの一致は不要。比較オペランドには期待型がなく（[§12.3](#s-12-3)）、型未確定リテラル・リテラルだけの式・nullだけを他方の末端型に合わせる。`.Name`や空コレクションなど期待型が必要なものはエラー。

全オブジェクトハンドル型には比較がなく、ペイロードの適合も転送しない。許可される場合に明示`@follow`でペイロードを比較する（[§13.5.5](#s-13-5-5)）。オブジェクト同一性比較はない。raw比較は[§5.1](#s-5-1)。ジェネリック本体で`T is Equatable`またはComparableの下の`T`・`ref/T`比較は、`T = ref/f32`などでも全実体化でTのContract対応を使う。`T = f64`ならNaN反射的equals（[§13.4.1](#s-13-4-1)）を保ち、組込み`==`へ変えない。

比較は所有オペランドをMoveしない。組込み比較でNon-Copy所有Placeを観察する場合、辺の評価時に共有Loanを作り、右辺評価中も左Loanを保つ（[§15.6.2](#s-15-6-2)）。両辺はInitializedが必要。Loanは完了・中断時に終わり、bool結果は保持しない。

`and`・`or`・`not`はboolを取りboolを返す。andは左が真、orは左が偽の場合だけ右を評価する。

```kimi
func take(text: string) -> string => text@move

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text@move)
// Error: the left operand's shared Loan is live when take transfers text.
Console.writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

```kimi
let found = i < n and matches(values[i]) // Indexes only when in range.
if status == Status.Ready => start()
// if status == .Ready => start()            // Error: a comparison operand has no expected Type.
// a, b: rc/Point, with Point Sealed and Equatable.
let sameValue = a@follow == b@follow        // Compares payloads.
// let bad = a == b                          // Error: object handles have no comparison.
```

<a id="s-13-4-1"></a>

### 13.4.1. Contractによる比較

同じ利用者型の比較は次のKimi Contractを要求し、対応は一度解決する。自由関数・extension・変換は探さず、ジェネリックには対応制約が必要。組込み比較を優先する。

| 演算子 | 操作 | 結果 |
| --- | --- | --- |
| `==`・`!=` | 両辺を共有借用して`Equatable.equals` | boolまたはその否定 |
| `<`・`<=`・`>`・`>=` | 両辺を共有借用して`Comparable.compare` | i32結果と0の比較 |

ComparableはEquatableを拡張し、compareの符号は等価性・全順序と一致しなければならない。整数・ラップ整数・char・stringは両方、bool・Unit・f32・f64はEquatableだけを持つ。safe値参照・タプルは能力を転送・合成するが、ハンドルはしない。struct・enumには明示適合が必要で自動導出せず、比較から算術は得られない。f32・f64のequalsは[§12.4.3](#s-12-4-3)のNaN反射的等価性を使い、NaNの組込み`==`は偽のまま。

<a id="s-13-5"></a>

## 13.5. 明示操作

`@`はオーバーロード不能な組込み操作で、変換連鎖を探さず直接オペランドだけに作用する。それ自体は利用者コードを呼ばないが、オペランド評価は呼びうる。割り当てるのはオブジェクト生成（[§13.6.2](#s-13-6-2)）だけ。暗黙のボックス化・所有権複製・参照数増加はしない。Origin制限は結果を合わせるだけ（[§3.8](#s-3-8)）で別操作を選ばない。

<a id="s-13-5-1"></a>

### 13.5.1. 形式とAdaptation対象

| 形式 | 意味 |
| --- | --- |
| `E@Type`、`E@Semantics` | 定義済みAdaptation（[§13.5.3](#s-13-5-3)）。省略形はCore・参照先型・View Targetを元から取る |
| `E@move`、`E@copy` | 転送、またはCopyがProvenな値のCopy |
| `E@follow` | 参照・ハンドルの参照先Place（[§13.5.5](#s-13-5-5)） |
| `E@raw`、`E@raw/U`、`E@usize` | raw形式（[§5.4](#s-5-4)） |
| `E@wrap<U>`、`E@bits<U>` | ラップ・ビット変換（[§13.5.4](#s-13-5-4)） |

対象はSemanticsとCore、借用・ポインター層の完全内部型、またはView Targetを指定する。外側Semantics連鎖のOriginは括弧経由でも明記不可で推論する。例外は外側raw層で、`p@(raw/U during o)`のような括弧対象はOriginの条件を満たすこと・主張を記す（[§5.7](#s-5-7)）。Optionペイロードなど内部型の注釈は残す。コンテナーパス（[§9.7.1](#s-9-7-1)）は制限を緩めず、束縛済みコンテナー修飾部はここでは使えない。

対象の構文範囲（Semantics接頭辞、`/`、`<`、閉じた・開いた対象、後置接尾辞）は[§F.9.3](#s-F-9-3)・[§F.9.4](#s-F-9-4)。各Semantics接頭辞は具体的Semanticsまたは宣言ペアバインダーへ解決する。プリミティブキーワードはペアバインダーになれないため`x@i32 / y`は除算。

括弧対象は常に型で、`x@(copy)`は操作ではない。Semantics省略形に`?`・`during`は付けない。`?`は完全対象型へ適用する（[§3.2.3](#s-3-2-3)）ため`x: i32`への`x@i32?`はエラー。

裸の`E@X`は組込みSemanticsを先に認識し、それ以外は型（別名・型パラメーター含む）とペアバインダーを別々に探す。型だけなら`@Type`、ペアだけなら省略形。両方またはいずれかの曖昧さはエラーで、オペランド・期待型では解消しない。

```kimi
let n = tasks@uniq.length            // Closed target: postfix continues.
let x = (point@Point).x              // Open target: group before selection.
let o = saved@(ref/T? during a)      // saved: Option<ref/T during a>; the payload annotation is kept.
// let e = items@Array<i32>[0]       // Error: group the adaptation first.
// let w = value@(ref/T during a)    // Error: Origin written on the outer borrow.
// let d = saved@ref/T? during a     // Error: group the annotated target.
```

<a id="s-13-5-2"></a>

### 13.5.2. 選択と推論

書いた対象と元の型・分類から操作を選び、その後アクセス・所有権・Loan・Originを検査する。失敗しても別操作・ゲッター・オーバーロードを選ばない。通常推論規則が許す範囲で対象はリテラル・ジェネリック推論を導くが、確定済み入力・結果型は変えない。数値・ラップ・ビット変換対象が導くのは直接リテラルだけ（[§13.5.4](#s-13-5-4)）。外側期待型は操作を取り消さない。

数値変換対象は元の期待型ではない。`(200 + 100)@u8`はi32で計算して範囲検査に失敗し、`id(300)@u8`はu8からidを推論しない。変換と読出し型での同一型取得は参照オペランドを値読出し（[§3.5.3](#s-3-5-3)）する。借用・follow・copy・move・オブジェクト対象は参照自身を取る。ジェネリック効果は[§8.14](#s-8-14)。Neverなら操作は実行しないが検査は省略しない。

<a id="s-13-5-3"></a>

### 13.5.3. 定義済みAdaptation

| 操作 | 条件 |
| --- | --- |
| 転送 | `@move`。Move可能Placeまたは一時値 |
| Copy | `@copy`。CopyがProven |
| 同一型取得 | 正規化完全型が同じ、または元の外側Semanticsに合う完全所有対象。Copy Place、一時値、読出し型参照の値読出し |
| 数値・ラップ・ビット変換 | [§13.5.4](#s-13-5-4) |
| 借用・再借用 | [§13.5.6](#s-13-5-6) |
| オブジェクト生成・アップキャスト | [§13.6.2](#s-13-6-2)、[§13.6.1](#s-13-6-1) |
| raw変換・アドレス・型付きnull | [§5.4](#s-5-4)。raw Origin主張（[§5.7](#s-5-7)）以外はunsafe文脈不要 |

`@move`はMove可能Place（[§15.1.5](#s-15-1-5)）の値と破棄責任を転送し、Copy型でもMovedにする。借用値なら参照をMoveし、参照経由Placeは転送しない。ゲッター結果を含む一時値には効果なし。`holder.item@move`はフィールド・ゲッター結果を転送し、レシーバは転送しない（[§7.3](#s-7-3)）。

`@copy`はPlaceを変えずCopyする。Non-Copy・未証明はエラーで、ジェネリックTにはCopy制約が必要。`r@copy`は参照、`r@follow@copy`はCopyな参照先をCopyする。ByValue Subject（[§15.1.6](#s-15-1-6)）など所有値を明記する位置でmoveの対となり、候補を指定するものではない（[§7.4](#s-7-4)）。move・copy・followは型・`?`・during・束縛集合を取らない。

同一型取得はCopy PlaceをCopy、一時値を転送し、Non-Copy Placeはエラー。外側ref・uniq・objref・objuniqの対象は常に借用・再借用を選ぶ。裸の`@owner`は無効。完全所有・ジェネリック対象と`@obj`・`@rc`・`@arc`は同型取得・生成・アップキャスト、またはエラーとなり、強い所有者を複製しない。

その後のOrigin制限は変性・outlivesが許す分だけ結果Originを短縮し、Copy・Move・借用は加えない。CoreとSemanticsを同時に変えるなら一つの定義済み操作でなければならず、変換後借用を挿入しない。要素単位・構造・string・bool・char・利用者定義変換や`@`による動的キャストはない。safe参照から参照先値を得るのは値読出し、[§10.2](#s-10-2)の共有参照Adaptation、followだけで、Non-Copy参照先は取り出さない。

```kimi
let number: i32 = 1
let a = number@copy        // Copy; number stays usable.
let b = number@i32         // Identity Acquisition: the same Copy.
inspect(number@i64@ref)    // Convert, then borrow the temporary.
// number@ref/i64          // Error: conversion and Borrow are separate operations.
// let e = number@owner    // Error: a bare owning shorthand is not an operation; write @copy.
let c = number@move        // Transfer; number becomes Moved although i32 is Copy.
// let d = number@i32      // Error: number is Moved.
// resource@copy           // Error: Resource is Non-Copy; write @move or borrow.
// resource@Resource       // Error: a bare Non-Copy Place is not transferred; write @move.
```

<a id="s-13-5-4"></a>

### 13.5.4. 数値・ラップ・ビット変換

| 形式 | 元と対象 | 結果 | 失敗 |
| --- | --- | --- | --- |
| `@Type` | 整数・浮動小数点、`U`↔`Wrapping<U>` | 同値または後述の丸め | 範囲外Abort |
| `@wrap<U>` | 整数・ラップ整数 | Uへラップ | なし |
| `@bits<U>` | 浮動小数点と同幅の整数・ラップ整数 | 同じビット | なし |

`@Type`は整数を切り詰め・ラップしない。整数→整数は範囲検査。整数→浮動小数点・浮動小数点→浮動小数点は最近接・偶数丸めと段階的アンダーフローを使い、有限値が無限大へ丸まれば失敗。浮動小数点→整数はゼロ方向切捨て後に範囲検査し、NaN・無限大は失敗。浮動小数点間ではNaN・無限大・符号付きゼロ（ゼロへ丸まる値の符号も）を保つがNaNペイロードの保持は不要。整数ゼロは正ゼロになる。連鎖の各`@`で検査・丸めする。

ラップ整数を含む`@Type`は同じ整数引数の`U`↔`Wrapping<U>`だけで、ビットを保つ。他はエラー。幅・符号・浮動小数点境界を越える意図は、`x@U@Wrapping<U>`で検査、`x@wrap<Wrapping<U>>`でラップ、`f@bits<Wrapping<U>>`で再解釈と明記する。

直接リテラルは既定型を経ず正確な値から一度変換し、収まらなければコンパイル時エラー。他は先に型付けする。リテラルAdaptationで引数を期待型へ暗黙に合わせられる範囲は広がらない。

| 対象 | 整数リテラル | 浮動小数点リテラル |
| --- | --- | --- |
| 整数型 | 収まる必要あり | 正確な値をゼロ方向へ切捨て、収まる必要あり |
| `Wrapping<U>` | Uに収まる必要あり | エラー |
| `f32`・`f64` | 一度丸める | 一度丸める（[§2.6](#s-2-6)） |
| `wrap<U>` | Uへラップ | エラー |
| 浮動小数点Fの`bits<F>` | Fと同幅の符号なし型へラップしFとして読む | エラー |
| 整数Iの`bits<I>` | エラー。wrapを使う | Iと同幅の浮動小数点へ丸め、Iとして読む |

wrapは同型とPrimitiveInteger証明済みパラメーター（[§8.7.4](#s-8-7-4)）を含む全整数・ラップ整数間で使える。縮小は下位ビットを保持、拡大は元の符号性で拡張し、Semanticsなしの完全値型Uとして読む。

bitsはf32とi32・u32・Wrapping&lt;i32>・Wrapping&lt;u32>、またはf64と対応する64ビット型の双方向だけ。他の組はエラー。IEEE 754の符号化・復号でNaNペイロードとゼロ符号を保持する。どちらかが型パラメーターに依存する間は使えない。

```kimi
let minimum = ((-128))@i8          // Direct literal, as (-128)@i8.
let negated = -(128)@i8            // An i32 negation, then a conversion.
let b = 200@u8@wrap<i8>            // -56
let e = -1@i32@wrap<u32>@u64       // 0xFFFF_FFFF: zero-extended through u32.
let half = 1.5@f32@bits<u32>       // 0x3FC0_0000
let w = (300@u32)@Wrapping<u32>    // Same integer argument.
let n = -(128)@u8                  // Abort: -128 is outside u8.
// let x = 256@u8                  // Error: the direct literal does not fit.
// let y: Wrapping<u32> = -1       // Error: -1 is not a u32 value.
// let z = w@u64                   // Error: different integer argument; write w@u32@u64.
```

<a id="s-13-5-5"></a>

### 13.5.5. Follow

`E@follow`はref/T・uniq/Tの参照先Placeを選び、Copy・Moveしない。ref先はRead、uniq先はRead・Writeを持ち、Takeはない。一回で一層を除く。letは格納スロット置換を禁じるが格納参照の能力を弱めない。共有層は排他アクセスを与えず、`ref/(uniq/T)`・`uniq/(ref/T)`のTは共有だけ。

オブジェクト各Semanticsへのfollowは、View Targetが内部Originも含む正確なペイロード型Tで、`T is Kimi.Sealed`がProvenの場合だけ使える（[§8.7.2](#s-8-7-2)）。ハンドルスロットとペイロードは別Placeで、ペイロードにTakeはない。

| ハンドルへの経路 | ペイロードアクセス |
| --- | --- |
| 書込み可能な所有obj/T | Read・Write・共有借用・排他借用 |
| letなど読取り専用の所有経路 | Read・共有借用 |
| objuniq/T（letスロット経由も） | 借用内で排他 |
| rc/T・arc/T・objref/T、任意の共有経路 | 共有だけ |

この権限はビュー借用・アップキャスト・暗黙レシーバ借用にも適用し、openビューは[§12.7](#s-12-7)に従う。obj・rc・arcハンドル経由オブジェクト内を貸す借用は、そのハンドルPlaceから間に逆参照を挟まないアクセス経路上に、自身のモードのLoanを保つ（[§15.6.2](#s-15-6-2)）。生存中のハンドルはMove・置換・排他借用・破棄できない（[§16.3.3](#s-16-3-3)）が、共有借用なら読出し・cloneは可能。objref・objuniq経由の借用は元ハンドルのLoanを受け継ぎ所有ハンドルを保持する。借用ハンドルの置換・破棄は解放を行わず、それがMove Pathなら代入は、自身を正確な接頭辞とするアクセス経路の子Loanを終了する（[§15.6.3](#s-15-6-3)）。

**ペア層**は外側Semanticsがペアバインダーsの正規化型、すなわち直接対象Tの`s/T`か直接対象Uの`s/U`（[§8.1.1](#s-8-1-1)）。ペア対象T単独は含まない。sの許容集合（[§8.3.4](#s-8-3-4)）が`value or valueborrow`内なら、一つのsafe値参照層として扱う。ref・uniqでは存在しownerでは層なしとなり、followは直接対象を格納するPlaceを選ぶ。他の許容集合ではエラー。owner時に一時値へCopyする`x@s`（[§8.14](#s-8-14)）の逆操作ではない。各許容Semanticsの規則で全場合を検査する（[§8.15](#s-8-15)）。元PlaceをP、経路依存をp、到達済みモードをM（[§15.1.6](#s-15-1-6)）とする。

| 項目 | owner | ref | uniq |
| --- | --- | --- | --- |
| 選ぶPlace | P自身（[§3.6](#s-3-6)） | 参照先 | 参照先 |
| Access Effect（[§8.14](#s-8-14)） | Borrow | Reborrow。[§10.2](#s-10-2)のAdaptationでは参照Copy | Reborrow |
| Read | Pが読出し可能なら | 可 | 可 |
| Write | Pが排他書込み可能なら | 不可 | 可。let経由も可、共有経路は不可 |
| 依存 | p。値ならTemporary Place（[§3.6](#s-3-6)） | oと親Loan。スロットではない | oと親Loan。子Loan経路はPを通る（[§15.6.3](#s-15-6-3)） |
| 裸Place Subjectのモード | Shared | Shared | MとExclusiveの弱い方 |
| 層の先のモード | 貸出し点を排他取得できる場合だけMとExclusiveの弱い方（[§15.1.5](#s-15-1-5)） | Shared | ownerと同じ |

ジェネリック本体は許容場合の最も弱い能力・モードを得る。Writeには`s is owner or uniq`と、ownerを許すなら排他書込み可能Pが必要。各場合を経路全体から求めた固有依存で検査し、oはたどった出現の外側Origin（[§8.1.2](#s-8-1-2)）。順位には最悪の場合を使う（[§10.2.2](#s-10-2-2)）。Takeはなく、適格ペア層の先のパターンは値束縛しない（[§14.8.1](#s-14-8-1)）。Object Semanticsを許すなど集合外のペア層はたどらず、束縛はスロットを値取得する。実体化でPlace選択・合法性を変えない。followはCopy・実行時分岐・割当てを加えず、具体的所有Placeへのfollowはエラー。

```kimi
var number: i32 = 1
let r = number@uniq
r@follow = 2              // Updates number.
let child = r@follow@uniq // Exclusive Reborrow of number through r.
// r@uniq                 // Error: r is a let binding; its slot is not exclusively borrowable.
```

```kimi
func borrowPayload<T>(source: objref/T) -> ref/T during source
    T is Sealed and ObjectPayload
    return source@follow@ref
```

```kimi
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    s is value or valueborrow
    return c[i]@follow@ref   // owner: Borrow of the element; ref, uniq: shared Reborrow.

func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c
    s is owner or uniq
    return c[i]@follow@uniq  // Rejected for Collection<ref/Node>: s is owner or uniq is Refuted.
```

<a id="s-13-5-6"></a>

### 13.5.6. 借用と再借用

`E@ref`・`E@ref/V`・`E@uniq`・`E@uniq/V`は記述したスロットE自身を借りる。格納Vなら結果ref/V・uniq/Vで、Vを明記するなら格納型と正確に一致させる。参照先選択・同型参照Copy・再借用は行わない。`r: ref/T`への`r@ref/T`はエラーで、`r`または`r@copy`と書く。貸出し点は[§3.4.2](#s-3-4-2)。初期化・能力・Loan・Originの妥当性が必要で、一時値は実体化する（[§3.6](#s-3-6)）。期待型での暗黙操作は[§10.2](#s-10-2)。

| 入力 | 操作 | 結果 |
| --- | --- | --- |
| Vを格納する読出し可能Place | `@ref`・`@ref/V` | 新しいref/V |
| Vを格納する排他書込み可能Place | `@uniq`・`@uniq/V` | 新しいuniq/V |
| 読出し可能なobj/T・rc/T・arc/T Place | `@objref` | 新しいobjref/T。参照数不変 |
| 排他書込み可能obj/T Place | `@objuniq` | 新しいobjuniq/T |
| objref/T | `@objref` | 共有参照Copy |
| objuniq/T | `@objref`・`@objuniq` | 共有・排他オブジェクト再借用 |
| unsafe文脈のVを格納するraw Place | 所有Placeと同じ | 新しい貸出し点の参照（[§5.2.2](#s-5-2-2)） |

値参照の再借用は`r@follow@ref`・`r@follow@uniq`と参照先経由で書き、rをMoveせず能力を貸す。期待ref/T・uniq/Tはuniq/T値を暗黙再借用（[§10.2](#s-10-2)）し、uniq/Tを格納するPlaceの裸取得も再借用する（[§3.5.1](#s-3-5-1)）。子Loan生存中は親経由の競合アクセスは禁止。借用はスロット、再借用は参照先・親Loanに依存する。共有を排他に強めず、rc・arcから排他借用せず、値借用とオブジェクト借用を相互変換しない。

カスタム・computed・要件getは一度呼んで結果をAdaptationする。所有結果の借用は[§11.5](#s-11-5)の一時値を実体化する。呼出し・ゲッターが返す参照は値であり、期待型でAdaptationし、followで参照先を再借用する（[§7.1](#s-7-1)）。setは隠れた記憶領域の借用を許さない。

```kimi
var value = makeValue()
let exclusive = value@uniq
let child: uniq/Value = exclusive // Exclusive Reborrow; exclusive resumes after child's last use.
let moved = exclusive@move        // Transfers the reference, not its referent.
let view = holder.item@follow@ref // item's getter returns ref/Resource: Reborrow of the referent.
```

<a id="s-13-5-7"></a>

### 13.5.7. 評価と失敗

各オペランド・必要レシーバは一度評価し、連鎖は内側から完了する。取得を呼出し・ゲッターの後方へ伝えて隠れた記憶領域に作用させない。借用は形成時から保護され、排他レシーバ・引数は呼出し予約（[§15.6.7](#s-15-6-7)）、他の排他借用は即時に生存する。捨てる結果も評価・検査し、完了したMove・効果を巻き戻さない。

| 失敗 | 扱い |
| --- | --- |
| 未定義Adaptation、型・アクセス・初期化・Loan・Origin違反 | コンパイル時エラー |
| リテラルを型に合わせられない、必須定数変換失敗 | コンパイル時エラー |
| 実行時数値変換失敗 | 値が静的既知でも評価時Abort |
| unsafeの約束違反 | 未定義動作（[§5](#s-5)） |

その他の定数評価は[§17.4.4](#s-17-4-4)。

<a id="s-13-6"></a>

## 13.6. オブジェクト操作・実行時検査・検査付きキャスト

<a id="s-13-6-1"></a>

### 13.6.1. オブジェクトのアップキャスト

元の静的View TargetをS、異なる対象をVとする。全てのより派生した動的型で`Supports(S, V)`（[§3.3.1](#s-3-3-1)）が成立する静的証明が必要。具体的Coreは基底グラフ、Contractビューは実行時Contract規則（[§8.12](#s-8-12)）を使う。ObjectPayloadをオプトアウトしたCore（[§8.7.3](#s-8-7-3)）は対象にできない。ペイロード消去（[§15.8.1](#s-15-8-1)）、アクセス、Origin、Loanも検査する。

| 元 | 操作 | 結果 |
| --- | --- | --- |
| obj/S・rc/S・arc/S | `@obj/V`・`@rc/V`・`@arc/V` | 通常取得した同じ所有ハンドル。Placeにはmoveが必要 |
| objref/S | `@objref/V` | 共有参照Copy |
| objuniq/S | `@objuniq/V`・`@objref/V` | 排他・共有の子再借用 |
| 共有借用可能なobj/S・rc/S・arc/S Place | `@objref/V` | 共有オブジェクト借用 |
| 排他書込み可能obj/S Place | `@objuniq/V` | 排他オブジェクト借用 |

オブジェクト、同一性、動的型、参照数、後始末責任を保つ。暗黙アップキャスト・値借用アップキャストはなく、ref/Vはobjref/Vと違う。元が保証できない対象には検査付きキャストが必要（[§13.6.5](#s-13-6-5)）。

```kimi
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal // dog is Moved; animal owns the whole Dog.
let view = animal@objref
// let bad = view@objref/Dog     // Error: Animal does not statically imply Dog.
```

<a id="s-13-6-2"></a>

### 13.6.2. 生成・clone・ライフサイクル

obj・rc・arcのsに対する`@s`・`@s/T`、別名・ジェネリック対象は共通規則を使う。owner/T→s/TはObjectPayloadを証明して生成、s/T→s/Tは同型取得、s/Derived→s/Baseはアップキャスト（[§13.6.1](#s-13-6-1)）、他は別の定義済み操作かエラー。入力型は対象・期待型と独立に固定するため`1@obj`はobj/i32、`1@obj/i64`はエラー。`1@i64@obj`と書く。生成は型引数・長さ・内部Origin・Loanを保ち、アップキャストと一体化しない。obj→rc/arc、rc↔arc、共有→排他、借用→所有の操作はない。Never入力は生成しない。

Kimi.Intrinsics（[§22.1](#s-22-1)）には次の関数もある。value・buildラベル、およびWeak操作（[§13.6.3](#s-13-6-3)）のvalueラベルは省略可。関数と`@`の生成効果は同じ。生成はObjectPayload、循環生成はさらにOwnedを要求し、cloneの`<s/T>`は`s is rc or arc`を要求する。

| 関数 | 入力→結果 | 効果 |
| --- | --- | --- |
| `makeObj<T>(value)` | `T → obj/T` | 新しい排他オブジェクト |
| `makeRc<T>(value)`、`makeArc<T>(value)` | `T → rc/T`、`T → arc/T` | 強参照数1の共有オブジェクト。arcは原子的計数 |
| `clone<s/T>(value)` | `ref/(s/T) → s/T` | 同じオブジェクト・ビュー・モードの強参照を一つ追加 |
| `makeRcCyclic<T, F>(build)`、`makeArcCyclic<T, F>(build)` | `F → rc/T`、`F → arc/T` | 循環構築 |

生成は入力を一度取得（Non-Copy Placeはmove必須）し、コンストラクター・アクセサー・dropを走らせず新記憶領域へMoveする。arc生成はスレッド間転送を許可しない（[§D.5](#s-D-5)）。強cloneはハンドルスロットを共有借用し、暗黙借用しないため`clone(handle@ref)`と書く（[§10.2](#s-10-2)）。割当てなしでビュー・動的型・依存を保ち、入力はInitialized、結果は入力スロットへのLoanを保持しない。その後元ハンドルをMove・置換できる。Move・借用では参照数を変えない。

状態はBuilding、Alive、Destroying、Freedの順。Buildingは強参照・ペイロードアクセスを公開しない。最後の強参照解放で完全動的型を破棄して領域を解放し、Weakは生存を延長しない。objも計数なしで同じ境界を持つ。

arcではペイロード初期化が全強ハンドル経由使用に先行し、各強参照解放が最終解放に、最終解放が破棄に先行する。成功upgradeは最終解放に先行し、最終解放後のupgradeは永久に失敗する。作成スレッドへハンドルを返すだけではスレッド間同期は生じない。保証は参照計数と寿命だけでペイロード変更には及ばず、実現する原子順序は実装定義。

循環rc構築は`F is Callable<owner, (Weak<rc/T>) -> T>`（arcも同様）を要求し、Fを通常取得して一度呼ぶ。

1. 未公開領域とビルダー用Weak一つを割り当てる。
2. Weakを値渡しする。Move・clone可能だがBuilding中のupgradeはNone。
3. ビルダーが戻り、その呼出しの後始末完了後、結果をペイロードへMoveする。
4. Aliveと最初の強参照を公開して返す。

割当て失敗・最大参照数からの増加は公開前にAbortし、計数はラップしない。ビルダーのAbort・非停止は公開を妨げる。

```kimi
let shared = 7@rc
let again = Kimi.Intrinsics.clone(shared@ref) // Strong count two.
let boxed = "sample"@obj
// let wrong: obj/i64 = 1@obj                 // Error: the input stays i32.
// let duplicate = shared@rc                  // Error: a Non-Copy Place needs @move; this is not clone.
```

<a id="s-13-6-3"></a>

### 13.6.3. Weak参照

Kimi.Intrinsicsの各関数は`s is rc or arc`とs/Tの適格性（[§3.2.2](#s-3-2-2)）を要求し、入力を一度評価してInitializedのまま保つ。

| 関数 | 入力→結果 | 効果 |
| --- | --- | --- |
| `downgrade<s/T>(value)` | `ref/(s/T) → Weak<s/T>` | 同じオブジェクトのWeak。強参照数不変 |
| `upgrade<s/T>(value)` | `ref/Weak<s/T> → Option<s/T>` | Aliveなら新しい強参照Some、他はNone |
| `clone<s/T>(value)` | `ref/Weak<s/T> → Weak<s/T>` | 同じオブジェクトへの別Weak |

ref/Weak入力で強cloneはs=ownerとなりRefutedなので、両cloneは同時適用されない。downgradeは強rc・arcだけを受け、割当てしうる。upgrade・Weak cloneは割当てしない。結果はオブジェクト・ビュー・モードを保つ。upgradeと最終arc解放の競合は[§13.6.2](#s-13-6-2)に従う。Building中のNoneの後に公開されうるが、最終解放後の失敗は永久。参照数超過はAbort。

Weak、そのclone、upgradeした強参照はペイロードの外部依存を保つが、元スロットへの永続Loanは持たない。ローカル借用を持つペイロードへのWeakはその借用より長生きできない。WeakのMoveは計数不変、破棄はペイロードに触れない。ペイロードアクセスにはupgradeが必要。

```kimi
struct Node
    public let selfWeak: Weak<rc/Node>
    public init(selfWeak: Weak<rc/Node>)
        self.selfWeak = selfWeak@move

let build = func [] (weak: Weak<rc/Node>) -> Node
    let before = Kimi.Intrinsics.upgrade(weak) // None while Building.
    return Node.init(weak@move)
let node = Kimi.Intrinsics.makeRcCyclic(build)
let after = Kimi.Intrinsics.upgrade(node.selfWeak) // Some after publication.
```

<a id="s-13-6-4"></a>

### 13.6.4. 実行時is検査

通常式の`value is T`・`value is not T`は実行時オブジェクト検査。意味は名前解決前に文脈で決める。先頭領域（[§7.5](#s-7-5)）を含む制約句・関連型条件は要件検査（[§8.2](#s-8-2)）、コンパイル時指令では全is検査を拒否する。対象一つで終わるので`value is Dog or Cat`は`(value is Dog) or Cat`。

対象は任意に修飾した単一の名前付きstruct Coreで、Semantics・束縛名・合成は不可。型引数は完全型だが、別名展開後も対象内のどこにもOrigin・during・束縛集合を書かず、[§13.6.5](#s-13-6-5)で補完する。対象自身に型パラメーター・関連型・非struct Coreは不可。型引数内の外側型パラメーター・関連型は可（`value is Slot<T>`）。左のSemanticsで対象のオブジェクト型を形成可能でなければならない（[§8.7.3](#s-8-7-3)）。

左はstruct Core Sを対象とするobj/S・rc/S・arc/S・objref/S・objuniq/S。一度評価し、動的型Dについて型引数を含むSupports(D,T)または否定を返す。Copy・Move・取得・参照数変更はしない。静的既知の結果でも有効で、元は評価する。型の絞込みは[§14.10](#s-14-10)。

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// if value is T => () // Error: a Type parameter is not a struct Core.
```

<a id="s-13-6-5"></a>

### 13.6.5. 検査付きキャストと対象補完

Vへの検査付きキャストは元オブジェクトの動的型でSupportsが成り立つ場合だけ成功する。結果型形成も妥当性に含み、失敗は不在・エラー値であってAbortではない。本版は意味だけを定義しソース構文は持たない。Contractビューの対応検査・正確型検査も構文なし（[§D](#s-D)）。

objref/AからはOption&lt;objref/B>を作り元も使える。objuniq/Aからは子再借用でOption&lt;objuniq/B>を作り、失敗時は子Loanなし。所有s/A（obj・rc・arc）は一度MoveしてResult&lt;s/B,s/A>にし、失敗は元ビューを保持する。

実行時is、絞込み、検査付きキャストはOriginを記述しない対象を補完する。元ビュー（絞込み名では実効型のもの、[§14.10.1](#s-14-10-1)）をS、対象をDとし、実行時同一性のSupports（[§3.3.1](#s-3-3-1)）で判定する。ジェネリックは全許容束縛で判定。

| SとDの関係 | Dの束縛 | is | キャスト |
| --- | --- | --- | --- |
| DがS自身または基底とProven | その位置でSが与えるもの | 常に真、事実を加えない | アップキャスト同様に成功 |
| DがそのどちらでもないとProven | 全固定Originをstaticにし、完成DはOwned必須 | 絞込み（[§14.10.2](#s-14-10-2)） | 成功は完成Dを保持 |
| 未決定（ジェネリック本体だけ） | なし | 事実を加えない | エラー |

第2行は単一継承（[§6.2.2](#s-6-2-2)）に基づく。成功には厳密に派生した動的型が必要で、その消去時にペイロードOwnedを証明済み（[§15.8.1](#s-15-8-1)）。ジェネリック本体は制約からOwnedを証明する。non-static束縛・呼出しごとのCallable Origin（[§15.2.3](#s-15-2-3)）は作らず、ハンドルのSemantics・外側Origin・Loanを保つ。

```kimi
open struct Base
    protected init() => ()

struct Slot<U> : Base
    public var value: U
    public init(value: U) : base() => self.value = value@move

func readValue(view: objref/Base) -> ref/i32 during static
    require view is Slot<ref/i32> else => $abort("Not a slot")
    // require view is Slot<ref/i32 during static> else => $abort("Not a slot") // Error: a target writes no Origin.
    return view.value // view is objref/Slot<ref/i32 during static>.
```

<a id="s-13-7"></a>

## 13.7. 代入

<a id="s-13-7-1"></a>

### 13.7.1. 単純代入

代入・複合代入・増減の対象はレシーバ式ではない（[§7.3](#s-7-3)）。経路、let/var、[§11.2](#s-11-2)の権限で書込み可能な対象を特定し、`@uniq`・`@objuniq`は不要、呼出し予約も開始しない。複合代入・増減の保護は[§13.7.2](#s-13-7-2)。特定途中のゲッターとカスタム・computed・要件setのレシーバはレシーバ式（[§11.3.1](#s-11-3-1)）。

標準setの代入先型は対象Place型。他のsetではvalue入力型であり、右辺を期待型推論とリテラルを合わせる規則も使ってそこに合わせる。複合代入・増減は読出し型（get結果型）で演算・範囲検査・Abortし、前置・後置結果もその型。計算値を共通Adaptationでset入力に合わせる（[§10.2](#s-10-2)）。

`target = value`はUnitを返し、次の順に実行する。

1. 右辺を評価し、共通Adaptationで代入先型の一時値を確保する。Non-Copy Placeの`y = x@move`などの転送も含む。
2. 対象のレシーバ・アクセス経路を左から右へ一度評価する。対象自身のgetは呼ばない。
3. 旧値またはまだInitializedな部分を破棄する。構築未完了値の部分への書込みはエラー（[§15.1.2](#s-15-1-2)）。
4. 一時値を配置して対象をInitializedにする。

カスタム・computed・要件setは手順3・4の代わりに確保値を受け取る。標準setは[§11.2](#s-11-2)で直接配置し、不完全記憶領域を復元できる。自身のフィールドの初回配置は[§6.3.4](#s-6-3-4)。ゲッター一時値を根とする対象は[§11.5](#s-11-5)で制限する。

置換はコンストラクター・初期化子を実行しない。完全旧値は正確な型の破棄連鎖、不完全値は部分後始末（[§16.3.2](#s-16-3-2)）を使い、基底ビューは全値対象でない。後始末が完了しなければ配置しない。特定には右辺後の状態を使い、Movedローカルの領域は特定できるがMovedなowner・レシーバは読めない。更新依存は[§15.7.3](#s-15-7-3)。右辺未完了なら対象評価なし、特定失敗なら破棄・配置なし、巻戻しなし。Unitを返すため`a = b = c`はaへUnitを代入する。

```kimi
values[index()] = makeValue()  // makeValue, index, old destruction, placement.
obj().prop = arg()             // arg, obj, setter.
obj().setProp(arg())           // obj, arg, method call.

var x = makeResource()         // Non-Copy.
x = x@move                     // Transfer out, locate x, skip the Moved old value, place.

// g: Gauge, whose computed `level: i32` has `get() -> i32` and `set(value: i64) -> ()`.
g.level = 3_000_000_000 // OK: the literal fits the setter input i64.
// g.level += 1         // Error: the i32 result does not fit the setter input i64.
```

<a id="s-13-7-2"></a>

### 13.7.2. 複合代入

`+=`・`-=`・`*=`・`/=`・`%=`・`&=`・`|=`・`^=`・`<<=`・`>>=`は二項操作を行いUnitを返す。算術は元の演算子と同じ操作を選び（[§13.3.4](#s-13-3-4)）、別の更新Contractはない。

1. 操作が要求する方法で右辺を評価・取得する。
2. 対象を一度特定し、標準アクセスまたは選択getで旧値を一度読む。
3. 読出し型で操作を一度実行する（[§13.7.1](#s-13-7-1)）。表現不能な整数結果は書込み前にAbort（[§13.3.1](#s-13-3-1)）。
4. 操作専用Loanを終え、置換または選択setで一度書く。

レシーバ・添字も各一度。参照から参照先へ書込みを暗黙転送せず、`r@follow += rhs`と書く（[§3.5.3](#s-3-5-3)）。読出し・書込みは独立に選ぶ（[§11.1](#s-11-1)）。標準アクセスは許可された領域を使い、他はアクセサーを呼び、setを迂回しない。旧値は演算要件に従い取得し、暗黙Moveしない。カスタムsetでも標準getはNon-Copy旧値を貸せる。ゲッター結果は通常一時値で、その依存がsetと競合しうる。旧値消費は`a = transform(a@move)`と明記する。

**書戻し保護。** 対象特定から書込みまで、標準setなら対象Place、他のsetならレシーバを呼出し予約同様に保護する（[§15.6.7](#s-15-6-7)）。get呼出し・演算オペランドLoanを含む共有読出し・共有借用は共存できるが書込み前に終える。期間内の別経路からの書込み・Move・破棄・排他アクセスは、get・演算実装の効果も含めエラー。排他Loanは書込み時に生存し、予約を経る参照経由書込みとなる（[§15.6](#s-15-6)）。

更新依存は[§15.7.3](#s-15-7-3)。途中が未完了なら後続は走らず巻戻しなし。原子的操作ではない。stringに複合代入はなく（[§13.3.2](#s-13-3-2)）、rawは[§5.3](#s-5-3)の`+=`・`-=`だけ。

```kimi
values[nextIndex()] += amount() // amount, nextIndex, old value, addition, write.
x += x                          // OK: the operand's shared Loan ends before the write.
holder.item += delta            // OK: the shared get and the Addable call only read holder before set.
// log += "!"                   // Error: no string compound assignment; write log = "\(log)!".
```

<a id="s-13-8"></a>

## 13.8. 予約構文とComposition Root

演算子記号・優先順位・結合規則は固定。利用者が拡張できるのは比較（[§13.4.1](#s-13-4-1)）・算術（[§13.3.3](#s-13-3-3)）だけで、同名メソッドだけでは得られない。and・or・not・`=`・`@`・is・範囲・制御転送は再定義不可。独自演算子、`!`・`&&`・`||`・`~`・`**`・`??`・`?.`・三項`?:`・単項`&`（`@ref`を使う）・前置move・`<-`はなく、asは予約済み。利用者算術演算子は定数評価しない。`#Name`は属性、`#if`・`#switch`は指令であって演算子でない。

**Composition Root**の`$`は名前解決と独立に言語提供操作を選び、名前認識はキーワード表（[§F.3](#s-F-3)）に従う。操作は`$tryWrite(writer, literal)`（[§22.10.4](#s-22-10-4)）、`$abort(...)`（[§17.4](#s-17-4)）、テスト用`$expect(...)`・`$require(...)`（[§17.6](#s-17-6)）だけ。置換・オーバーロード・関数値化は不可。他の`$`操作はエラー。

```kimi
if n < 0 => $abort("negative input")
// let f = $abort  // Error: a $ operation is not a function value.
// let s = a ?? b  // Error: ?? is not an operator.
```

<a id="s-14"></a>

# 14. 制御フロー

制御フロー式はif・match・for・while・loop・doと、転送return・exit・continue・yield。unsafe・defer・require・明示的に結果を捨てる構文は文で、初期化子・引数・オペランドにはできない。テスト本体は`$expect`・`$require`も使える（[§17.6](#s-17-6)）。**反復構文**はfor・while・loop、**反復**はその本体の一回の実行。**選択**は一つのif連鎖またはmatch。括弧はグループ化だけを行い、スコープ・転送先・探索障壁を作らない。

<a id="s-14-1"></a>

## 14.1. 完了

評価は**完了**、終わらない**発散**、または完了・Scope Exitなしにプロセスを終える**Abort**（[§17.4](#s-17-4)）になる。**通常完了**`Normal(result)`は評価元へ戻る。文はUnitで通常完了しても式にはならない。**制御転送による完了**は`Return(target, result)`、`Exit(target, result)`、`Continue(target)`、`Yield(target, result)`。転送式自身は通常完了しない。

必要なScope Exit（[§16.2](#s-16-2)）の後、構文は自身宛ての転送を受け取り、他宛ては外へ伝える。

| 対象 | 受け取る転送 | 動作 |
| --- | --- | --- |
| 反復・do | `Exit(self, result)` | resultで完了 |
| 選択 | `Yield(self, result)` | resultで完了 |
| 遅延本体 | `Exit(self, ())` | 本体を終え、保留Scope Exitを再開 |
| 反復 | `Continue(self)` | 次の反復へ |
| 関数 | `Return(self, result)` | 呼出し側へresultを渡す |

Neverは完了でなく型で、[§14.9](#s-14-9)で推論する。

```kimi
func clamp(value: i32) -> i32
    if value < 0 => return 0                   // Return passes through the if.
    let size = label measure: do
        if value > 100 => exit to measure 100  // Exit completes the do with 100.
        exit to measure value
    return size
```

<a id="s-14-2"></a>

## 14.2. 本体と評価文脈

<a id="s-14-2-1"></a>

### 14.2.1. 本体の形式

全実行本体は次の**Body**（[§F.10](#s-F-10)）。

- **単項本体**`=> item`はヘッダーと同じ行で始まる一つの式・文を持つ。通常の式継続・matchアーム列で後へ続けられる。
- **字下げ本体**は宣言・式・文・許可された指令を順に実行する。完全なソース項目が1個以上必要で、空行・コメントは数えない。何もしないなら`()`。本体欠落はエラーで、次項目を吸収しない。

選択の節・アーム、反復、do、unsafe、defer、require失敗本体、関数、匿名関数、クロージャー、特殊化、アクセサー、init、dropがBodyを持つ。コンテナー本体とmatchアーム列はレイアウト本体（[§F.4.2](#s-F-4-2)）だがBodyではない。Bodyごとに独自スコープを持つ。宣言・指令には字下げ形が必要で、各位置の宣言制限を保つ（[§7.5](#s-7-5)）。空でないことは指令選択前に検査し、何も選ばない完全指令も項目と数える。選択後は通常の型・転送・結果検査を行う。

```kimi
defer => ()               // Explicit no-op.
defer
    #if windows
        closeHandle()     // Valid even if nothing is selected.
defer
    // Error: no source item.
nextOperation()
```

<a id="s-14-2-2"></a>

### 14.2.2. 評価文脈と本体結果

**値文脈**は初期化子、引数、条件、match・forのSubject、演算子・転送のオペランド。**結果を捨てる文脈**は単独式と字下げ本体の直接の式。括弧は文脈・期待型を保つ。期待Unit、未使用、到達可能性、最適化のいずれも値文脈を捨てる文脈に変えない。結果は通常寿命で破棄し、Unit変換はしない。Result値を捨てる警告（[§17.5](#s-17-5)）を含む全検査が残る。

字下げ本体の直接の式は末尾も捨てる。項目列の通常完了である**構造上の末尾到達**は、その構文が本体結果を使う場合にUnitを供給する。単項本体は本体検査前に次の規則を固定する。

| 所属構文 | 単項の式 | 結果転送 |
| --- | --- | --- |
| 関数・get | 結果型がUnitに固定済みなら捨ててUnit完了、他は値を返す | return |
| if・match・do | 値文脈では値、捨てる文脈では捨ててUnit完了 | yield。doは名前付きexit |
| for・while・loop | 捨てて次の反復へ | exit |
| unsafe・defer・set・init・drop | 捨ててUnit完了 | deferはexit、set・init・dropはreturn |
| require失敗 | 捨てるが後続へ進んではならない（[§14.11](#s-14-11)） | なし |

単項本体の文は通常完了可能な場合だけUnitを供給する。捨てた値に共通型は不要だが、対象へ供給する全値は、その対象結果を捨てる場合でも**Target Result Type**に合わなければならない（[§14.9.2](#s-14-9-2)）。for・while・defer・set・init・drop、および捨てる文脈のif・match・do・loopではUnit。転送の受領は暗黙の本体結果を供給しない。

```kimi
if ready => visited.insert(id)   // The bool is discarded.
let bad: i32 = if ready
    compute()                    // Error: discarded; the implicit Unit does not fit i32.
else => 0
loop => exit 1                   // Error: a discarded loop requires Unit exits.
```

<a id="s-14-2-3"></a>

### 14.2.3. 条件

if・else if・while・require・matchガードの条件はbool式。if let、var条件、条件パターンはない。ヘッダー内で本体を持つ式は[§F.4.4](#s-F-4-4)に従う。

条件を一度評価してboolを確保し、残る条件一時値を生成逆順に破棄し、ガード内一時Loanを終えてから分岐する。後始末の効果で条件を再評価しない。転送・発散・Abortはその経路の条件評価を終える。match Subject、イテレーター、束縛は固有スコープを保つ。

```kimi
let guard = registry.lock()
if guard.contains(id) => work()   // A guard created in the condition would be destroyed before work().
```

<a id="s-14-2-4"></a>

### 14.2.4. 明示的に結果を捨てる

`_ = Expression`はUnitで完了する文。代入・式ではなく、複合形はない。オペランドは期待型なしの値文脈で検査し、ローカルを導入せず、裸取得（[§3.5](#s-3-5)）または転送して文の一時値後始末で破棄する。裸のNon-Copy Placeはエラー。ただしuniq/T・objuniq/Tを格納する場合、`_ = r`は子を再借用して捨て、`_ = r@move`はrを消費する。

抑制するのはその結果を捨てる警告だけ（[§17.5](#s-17-5)）。Resultならエラーペイロードも破棄し、Abortは捕捉しない。`_ = try prepare()`なら失敗を伝播し成功を捨てる。

```kimi
_ = prepare()          // Ignore the whole Result, including Err.
_ = try prepare()      // Propagate failure; discard success.
_ = resource@move      // Destroy a Non-Copy value early.
_ = r                  // r: uniq/Node; r stays usable.
// _ = resource        // Error: a bare Non-Copy Place.
// _ = .None           // Error: no Type information.
// consume(_ = prepare())   // Error: not an expression.
```

<a id="s-14-3"></a>

## 14.3. doとunsafe

do Bodyは一度実行する式（[§14.2.2](#s-14-2-2)）。ラベルがあれば自身宛て`exit to Label`を使える。名前なしexitはdoを飛ばすため、ラベルなしの字下げdoは非Unit結果を供給できない。

unsafe・deferは実行スコープの文。deferはScope Exit用に本体を登録する（[§16.1](#s-16-1)）。**Unsafeブロック**（unsafeとBody。unsafe funcは修飾子）は即時実行し、[§5](#s-5)のunsafe操作とnoinit（[§4.3.4](#s-4-3-4)）を字句的に許可する。noinitは何も読まなくても許可を使用する。許可は入れ子deferへ届くがFunction Boundaryを越えず、最も内側のUnsafeブロックを使う。unsafe func自身は本体へ許可を与えない（[§7.6](#s-7-6)）。Unsafeブロックは転送先・障壁ではなく、型・所有権・Loan検査も緩めない。許可を使う操作がなければUnnecessaryUnsafeBlock_Kd警告。

```kimi
let result = label work: do
    if cached() => exit to work cachedValue()
    exit to work compute()
do
    let resource = openResource()
    defer => close(resource)
    use(resource)
defer => unsafe => releaseRaw(pointer)
unsafe
    let address = pointer@usize        // Warning: nothing needs unsafe permission.
// let x = unsafe => readRaw(pointer)  // Error: unsafe is a statement.
```

<a id="s-14-4"></a>

## 14.4. ラベル

`label Label:`は同じ物理行に続くif・match・for・while・loop・doを名付ける（[§F.10](#s-F-10)）。他には付けられず、スコープ・束縛を加えない。認識は[§F.3.2](#s-F-3-2)。独自名前空間を持ち、その構文の本体内だけで見え、条件・Subject・ガードでは見えない。同一関数の重なるスコープで同名ラベルはエラー。

ラベルはグループ化しない。`return (label c: if ready => a else => b) + 1`は両経路で加算するが、括弧なしではelseだけで加算する。

```kimi
consume(label choice: if ready => 1 else => 0)   // Labeled positional argument.
return label work: do => compute()
let label: bool = true
let accepted: bool = label label: do
    exit to label label   // Label namespace target; Value namespace operand.
```

<a id="s-14-5"></a>

## 14.5. 制御転送

<a id="s-14-5-1"></a>

### 14.5.1. 構文とオペランド

形式は`return [E]`、`exit [to Label] [E]`、`continue [to Label]`、`yield [to Label] [E]`（[§F.10](#s-F-10)）。型はNever。省略オペランドは`()`で、対象のTarget Result Typeに合わせて検査し、受渡しは[§16.2.2](#s-16-2-2)に従う。

exit・continue・yield直後のtoは文脈キーワードとして名前付き形を確定する。`return to`は値toを返す。ラベルは一つの名前で、計算した宛先は不可。オペランド省略は[§F.10.2](#s-F-10-2)で決め、完全式としての再試行はしない。

```kimi
exit to search score(item)
continue to outer
yield to choice match mode
    .Fast => 1
    _ => 0
// [exit: value]            // Error: ':' does not omit the operand.
// [exit to work x: value]  // A Dictionary: the colon follows the complete operand.
```

<a id="s-14-5-2"></a>

### 14.5.2. 対象と障壁

| 転送 | 名前なし対象 | 名前付き対象 | 探索障壁 |
| --- | --- | --- | --- |
| return | 関数 | なし | defer |
| exit | 反復・defer | 反復・do | 関数。名前付きはdeferも |
| continue | 反復 | 反復 | 関数・defer |
| yield | if・match | if・match | 関数・defer |

最も近い対象を先に解決してから検査し、不一致でも探索を再開しない。対象なし・種類違い、および捨てる文脈の選択への名前なしyieldはエラー。構文は自身の本体内だけで対象・障壁となり、unsafe・requireはいずれでもない。転送は同一関数内の外側構文だけを対象とし、関数・遅延本体を越えない。

Function Boundaryは名前付き関数、メソッド、匿名関数、クロージャー、特殊化、アクセサー、init、drop。defer内宣言の関数も独自return対象を持つ。drop内returnはフィールド破棄を飛ばさない（[§16.3.2](#s-16-3-2)）。名前付き関数は本体が終わらなくても宣言結果または既定Unitを保つ（[§7.1](#s-7-1)）。

```kimi
let result = label choice: if enabled
    if cached() => yield to choice cachedValue()   // Unlabeled, it would target the discarded inner if.
    yield compute()
else => 0
label outer: for row in rows
    for cell in row
        if skipRow(cell) => continue to outer
defer => exit ()     // Ends the deferred body.
// defer => return   // Error: defer is a barrier for return.
```

<a id="s-14-6"></a>

## 14.6. 反復構文

毎回新しい本体スコープを作り、次の反復前に後始末する。本体の通常結果は捨てる。

<a id="s-14-6-1"></a>

### 14.6.1. forとwhile

forはSubjectを一度取得して要素を順に取り、whileは毎回本体の前にbool条件を調べる。枯渇、偽条件、自身宛てexitでUnit完了する。whileはtrueでも静的な偽経路を残す（[§14.9.3](#s-14-9-3)）。無条件反復にはloopを使う。

for束縛は一スロットかスロットの括弧付きタプル（ForBinding、[§F.10](#s-F-10)）だけで、一般パターンではない。`(a, b)`はペア、`(a,)`は1要素タプルを分解し、`(x)`は無効。各スロットは不変の名前、再代入可能ローカルの`var Name`（参照要素は[§15.1.6](#s-15-1-6)）、または使えない無名束縛`_`。`var _`は無効で、名前付きスロットは重複不可。

```kimi
for (key, value) in dictionary => process(key, value)
for (only,) in singles => use(only)   // singles: Array<(i32,)>
for var number in numbers@move
    number += 1                       // Updates only the iteration local.
while ready => process()
// for (x) in xs => use(x)            // Error: write `for x in xs`.
```

<a id="s-14-6-2"></a>

### 14.6.2. 反復プロトコル

forはLendingIteratorと三つの反復入口だけを使う（[§22.2.1](#s-22-2-1)）。Subject取得（[§15.1.6](#s-15-1-6)）のモードで入口を選び、参照経路選択（[§3.4.3](#s-3-4-3)）で取得したレシーバで一度呼ぶ。各段階はイテレーターの短い排他再借用でLendingIterator.nextを呼び、Noneなら終わり、Someペイロードを束縛する。

| Subjectモード | 典型例 | 入口 |
| --- | --- | --- |
| Shared | values、values@ref、ref/C値 | `Iterable.iterate` |
| Exclusive | values@uniq、uniq/C値 | `UniqIterable.iterateUniq` |
| ByValue | values@move、一時値、range@copy | `IntoIterable.intoIterator` |

ByValueは参照先の所有権を取らない。適合なし・曖昧ならエラーで他方式を試さない。イテレーター値は`Kimi.Iteration.owning(it@move)`または`Kimi.Iteration.borrowing(it@uniq)`で列挙する（[§22.2.2](#s-22-2-2)）。標準イテレーターの管理・所有領域・要素コストは[§4.8.6](#s-4-8-6)。

要素型は`LendingIterator.LentItem(step)`、Iteratorなら`Iterator.Item`。単一スロットはそのまま束縛する。括弧付き束縛はスロット数と一致するタプルかそのsafe参照が必要（[§14.8.1](#s-14-8-1)）で、[§15.1.6](#s-15-1-6)で分解する。ref/(A,B)はref/A・ref/Bとなる。`_`も要素を取得して後始末する。

| 列挙値 | Iterable要素 | UniqIterable要素 | IntoIterable要素 |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `ref/E during a` | `uniq/E during a` | `E` |
| `Dictionary<K, V>` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K, V)` |
| `Slice<E>` | `ref/E during s` | `ref/E during s` | `ref/E during s` |
| `Range<T, T>`、`ClosedRange<T, T>`（T is PrimitiveInteger） | `T` | `T` | `T` |
| `ResolvedRange` | `isize` | `isize` | `isize` |

aはSubject借用のOrigin、sはSliceのsource。要素Originは`ref/(ref/Node during b) during a`のように入れ子を保つ。配列・Sliceは添字順、Dictionaryは挿入順。境界省略・末尾基準・混合型の範囲は列挙不可（[§4.6.4](#s-4-6-4)）。

Subject Loanはイテレーターか外へ逃した要素が必要とする間生存する。次のnextと競合するLoanがあれば反復の戻り辺がエラー。末尾到達と自身宛てcontinueは、次のnext前に反復スコープを後始末する。ループを離れる全転送（exit、return、try伝播、外側yield、外側continue）は、反復スコープ、イテレーター、内部Subjectの順で後始末する（[§16.2.2](#s-16-2-2)）。早期終了は追加要素を消費しない。束縛は本体ローカル・deferの後、逆順で後始末する（[§16.2](#s-16-2)）。

```kimi
for item in items => inspect(item)            // ref/Item; items stays usable.
for item in items@uniq => update(item)        // uniq/Item
for item in items@move => store(item@move)    // Item
for (a, b) in pairs => total += a + b         // pairs: Array<(i32, i32)>; a, b: ref/i32
// for x in counter => use(x)                 // Error: counter: Counter, a LendingIterator with no entry conformance; write Kimi.Iteration.owning(counter@move).
```

<a id="s-14-6-3"></a>

### 14.6.3. loop

loopは本体を繰り返し、末尾到達・continueで再開する。結果を供給するのは自身宛てexitだけ。値文脈では[§14.9](#s-14-9)で推論、捨てる文脈ではUnit。

```kimi
let found: i32 = label search: loop
    for item in items[..]
        if accepts(item) => exit to search score(item)   // Unlabeled, exit would end the for.
    exit -1
loop => work()
```

<a id="s-14-7"></a>

## 14.7. ifとyield

最初に真となる節、なければelseを実行する。else if連鎖は一つの選択。else省略は値文脈のUnit結果も含め`else => ()`と同じ。各節・アームは独立に本体形式を選べる。yieldは選択を終える（[§14.5.2](#s-14-5-2)）。捨てる選択を早く終えるには名前を付けUnitを供給する。

```kimi
let value = if ready
    prepare()
    yield 1
else => 0
let unit: () = if ready => ()   // Valid, with the omitted else.
// let bad = if ready => 1      // Error: i32 and Unit conflict.
label action: match event@ref
    .Save
        if readOnly => yield to action
        save()
    _ => ()
```

<a id="s-14-8"></a>

## 14.8. match

Subjectを一度評価・取得（[§15.1.6](#s-15-1-6)）してモードを固定する。ソース順でパターンが一致しガードが真になる最初のアームだけを実行する。アームは1個以上必要で、列内に指令は置けない（[§19.1](#s-19-1)）。全文脈で網羅性が必要（[§14.8.4](#s-14-8-4)）。

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n   // n: ref/i32, value-read at the i32 result.
        .Some(_) => 0
        .None => 0
```

<a id="s-14-8-1"></a>

### 14.8.1. パターン

パターンはmatchアームだけに現れる。

| パターン | 例 | 意味 |
| --- | --- | --- |
| ワイルドカード | `_` | 検査・取得せず受理 |
| リテラル | `0`、`-1`、`true`、`'A'`、`"ok"` | 組込み値と比較 |
| enum Case | `.None`、`.Some(let x)`、`FileError.NotFound` | Case、続いてペイロードを検査 |
| 束縛 | `let x`、`var x` | 受理してアーム内名を束縛 |
| Unit・タプル | `()`、`(let x,)`、`(let x, 0)` | 正確な要素数と内容を照合 |
| 括弧 | `(.Some(let x))` | 内側パターン |

入れ子Caseの期待型は位置から得る。修飾Caseの修飾部は束縛集合接尾辞なしの通常コンテナーパスで、自身・継承Originスロットは照合型から束縛し、省略enum引数は[§6.5.2](#s-6-5-2)に従う。名前、定数、プロパティ、呼出し、他の式はパターンではなく、値との比較にはガードを使う。パターン検査はコンストラクター・ゲッター・変換・利用者演算子を呼ばない。

Case・タプル・Unit・リテラルの**構造パターン**は、safe値参照（ref/T、uniq/T、適格ペア層、[§13.5.5](#s-13-5-5)）に必要構造がなければ参照先を繰り返し選ぶ（[§3.4.3](#s-3-4-3)）。ワイルドカード・括弧は選択せず、束縛は後述の場合だけ。Object Semantics・rawはたどらない。位置ごとに選択し、refより下は共有、uniqより下はSubjectモードと全外側層が許す場合だけ排他、ペア層は全場合の最弱アクセスを与える。

値としてのアクセス（ByValue）で適格ペア層へ到達した束縛は、その層にTakeがないため、層の先のモードで選択Placeへの参照を束縛する。refを許すならref/T、他はuniq/T（[§15.1.6](#s-15-1-6)）。他の束縛は追加選択しない。要素数・ペイロード数は正確な一致が必要。`(P)`は括弧、`(P,)`は1要素タプルで、`.Some((let x, let y))`は一つのタプルペイロード、`.Pair(let x, let y)`は二つ。外から内、左から右へ検査し、最初の不一致で止まり、Copy・Moveしない。

リテラルパターンはbool・整数・char・非補間string。負整数は`-`とリテラルで符号適用後に型へ合わせる（-128はi8に合い、128は合わない）。Wrapping&lt;T>はTに合う整数を取る。charはUnicodeスカラー値、stringは正確なUTF-8内容で比較する。浮動小数点・null・補間文字列・範囲はパターンではない。

```kimi
enum Box {source}
    Value(uniq/Option<i32> during source)

match box@uniq
    .Value(.Some(let x)) => x@follow += 1   // x: uniq/i32
    _ => ()
match box
    .Value(.Some(let x)) => use(x)          // Shared Subject: x: ref/i32
    _ => ()
```

<a id="s-14-8-2"></a>

### 14.8.2. 束縛スコープ

let name・var nameは常に一致し、本体ローカルを不変・再代入可能として作る。varはペイロードへのアクセスを増やさない（[§15.1.6](#s-15-1-6)）。同一パターンの名前は一意。let _・var _は無効、_nameは通常束縛。名前は自アームだけで見え、ガードでは候補、本体では別の束縛同一性を持つローカルとなる。本体で再宣言できないが、入れ子ブロックは隠せる。他のパターン形式は延期（[§D.3](#s-D-3)）。

```kimi
match pair                     // pair: (i32, i32)
    (let x, let y) if x == y => same(x)
    (let x, _) => other(x)
// (let x, let x) => ()        // Error: duplicate name, not an equality test.
```

<a id="s-14-8-3"></a>

### 14.8.3. ガード

各束縛位置はSubjectまたは参照先の初期化済み記憶領域である**Candidate Place**を指す。ガードの候補名はSubjectモードによらず`ref/T during g`。Tは自身のOriginを保つ格納完全型、gはガード後始末で終わるローカルOrigin。本体束縛は所有・共有・排他経路に応じT・ref/T・uniq/T（[§15.1.6](#s-15-1-6)）。読出し・比較は参照層をたどる（[§3.5.3](#s-3-5-3)、[§13.4](#s-13-4)）。ガード・本体はそれぞれの型で解決し、ガードの事実は本体へ渡らない。

ガードは一つのboolヘッダー式（[§F.4.4](#s-F-4-4)）で、let条件・ガード連鎖はない。パターン一致時に一度実行し、真偽によらず候補参照・一時値・新Loanを後始末する。真ならCandidate Placeから本体ローカルを初期化して実行し、偽ならSubjectを変えず次アームへ進む。

ガードで得た値の保存・後の使用はgに対する通常Origin・Loan検査に従う。g依存値は外へ出せず、候補から読出し・Copyした値は格納値自身のOriginだけを保つ。呼出しは本体でなくシグネチャのOrigin・効果境界で判断する。候補名はMove・再代入・排他借用・キャプチャー不可。通常ローカルへ読み出した値は通常規則でキャプチャーできる。

**Subject保護。** パターン検査開始からガード後始末完了まで、Subject Placeとパターンが参照経由で到達する全参照先に共有Loanを置く。通常の重複・呼出し規則で競合を判定（[§15.6.2](#s-15-6-2)、[§15.6.4](#s-15-6-4)）し、ガードが変えられるのは保護Placeと重ならないPlaceだけ。その変更はガードが偽でも巻き戻さない。ワイルドカード位置もSubject全体の保護に含む。

ガード・後始末が通常完了しなければ本体束縛を初期化せず後続アームも実行しない。通常転送はmatch後始末（[§15.1.6](#s-15-1-6)）と中断式の順序（[§16.2.1](#s-16-2-1)）に従う。ガードは転送先・障壁ではないが、自身のmatchへyieldできない。

```kimi
// Packet.Data stores Data; Packet.Link stores ref/Data during a.
// keep(slot: uniq/(ref/Data during s), value: ref/Data during s) -> bool
var saved = fallback@ref                                  // fallback: Data
match packet
    .Data(let d) if keep(saved@uniq, d) => ()               // Error: d's Origin ends at guard cleanup, but saved is used below
    .Link(let r) if keep(saved@uniq, r@follow@copy) => ()   // Valid: the copied reference keeps Origin a
    _ => ()
use(saved)
```

```kimi
// var state: State; var pair: (Option<i32>, i32)
match state
    .Idle if restart(state@uniq) => ()                // Error: state is protected during the guard
    _ => ()
match pair
    (.Some(let a), _) if bump(pair.1@uniq) => ()      // Error: the whole Subject is protected, Wildcard positions included
    _ => ()
```

<a id="s-14-8-4"></a>

### 14.8.4. 網羅性と到達不能パターン

網羅性はガードなしアームだけで証明する。if trueを含むガード付き、ガード論理、定数Subject、Never本体、Neverペイロードは網羅性を与えず、全宣言Caseを可能とみなす。

**位置全体パターン**はワイルドカード、束縛、Unit位置の`()`、それらだけからなるタプル（括弧は透過）。リテラル・Caseは含まない。

| 対象領域 | 網羅条件 |
| --- | --- |
| 任意 | 一アームに位置全体パターン |
| bool / Unit | trueとfalse / `()` |
| enum | 全Caseに、全ペイロード要素が位置全体のアーム |
| タプル・整数・char・string・他 | 位置全体パターン。部分パターンを組み合わせない |

リテラルは型に合わせた値で比較し、0・-0・0x00は同じ。bool規則はbool Subjectだけに適用し、Some(true)とSome(false)ではSome全体を覆わない。

先行する一つのガードなしパターンが後続を包含すると警告が必須。ワイルドカード・束縛はその位置の全て、同値リテラル・Unitは自身、同Case・同要素数タプルは全子が包含すれば包含する。名前・let/var・括弧は無視し、後続はガード付きでもよい。複数先行アームは合成しない。警告でも検査・結果収集からアームを除かず、任意lintも受理可否を変えない。

```kimi
match flagOption          // Error: Some needs a whole-payload arm.
    .Some(true) => 1
    .Some(false) => 0
    .None => -1
match r                       // r: Result<i32, Never>
    .Ok(let v) => use(v)
    .Err(_) => ()             // Still required.
match value
    .Some(_) => 1
    .Some(0) => 2             // Warning: the earlier Pattern contains it.
    .None => 0
```

<a id="s-14-9"></a>

## 14.9. 結果の検証

指令選択後、**結果源収集**が対象への全供給値を集め、**構造的完了**が構文から通常完了候補を求め（[§14.9.3](#s-14-9-3)）、**実行時到達可能性**が同じ経路へ初期化・Move・Loan・絞込み・後始末を適用する。到達不能項目も仮定経路状態で検査する（[§14.10.3](#s-14-10-3)）。

<a id="s-14-9-1"></a>

### 14.9.1. 結果源

| 結果源 | 供給値 |
| --- | --- |
| 明示転送 | 到達可能性によらず、自身宛てreturn・yield・exitの各オペランド。省略はUnit |
| 単項本体 | [§14.2.2](#s-14-2-2)が使う値。通常完了可能な文・捨てる本体はUnit |
| 字下げ本体 | 所属構文が結果を使う場合、構造上の末尾到達でUnit |
| 暗黙完了 | else省略、for枯渇、while偽経路のUnit |

全本体への進入を仮定する。構造的継続がない列は末尾Unitを供給しない。ループ本体末尾はループ結果ではない。実行時到達可能性は結果源の追加・除去や推論型の変更を行わない。

```kimi
let conflict = loop
    continue
    exit 1
    exit "x"      // Error: unreachable results must also fit.
```

<a id="s-14-9-2"></a>

### 14.9.2. 対象結果型と式型

**Target Result Type（対象結果型）**はソース順によらず次で決める。

1. 宣言型、[§14.2.2](#s-14-2-2)の固定型、または固定の外側期待型を使う。
2. なければ独立型付け可能な結果源（[§10.5](#s-10-5)）から共通型を一つ求める。構造だけを比較し（[§15.6.1](#s-15-6-1)）、共通型のOriginはローカル領域とする。各結果源からの条件は通常Origin関係となり、型不一致にはしない。Neverは候補を供給しない。一型上のsafe参照層だけが違う結果源やそのリテラル（[§12.3](#s-12-3)）は、その共通型を持ち値読出しする（[§3.5.3](#s-3-5-3)）。呼出し引数の制御フロー式は[§10.5](#s-10-5)の引数分類に従う。
3. その型で検査できる結果源へ伝える（例：0..nに並ぶ0..3）。リテラル既定型は[§10.2](#s-10-2)に従う。
4. 全結果源を検査する。なお型がない呼出し・匿名関数・空リテラルには注釈が必要。

入れ子結果も同じように伝え、外側制約より前にリテラル結果を固定しない。型付き束縛は後の使用から再推論しない。本体を再検査して組合せ探索しない（[§10.8](#s-10-8)）。

**式型**は対象結果型。ただし全結果源（あれば）がNeverで構造的完了に通常候補がなければNever。Neverで型不一致・結果欠落を免除せず、実行時到達可能性でNeverにもしない。宣言・期待結果のない匿名関数もこの規則で返却型を推論する。

```kimi
let large: i64 = 10
let second = if ready => large else => 1   // i64; no early default to i32.
var x = 1
var y = 2
let r = if ready => x@ref else => y@ref   // r: ref/i32; its Origin covers both Loans
x = 3                                      // Error: the Loan of x is still live
use(r)
let a = loop => continue                   // Never
let b = loop
    continue
    exit 1                                 // i32: an unreachable source still counts.
```

<a id="s-14-9-3"></a>

### 14.9.3. 到達可能性

構造的完了は保守的で、boolリテラル、条件値、定数、パターン包含、型矛盾、被呼出し本体から経路を削らない。発散、Never呼出し、Abort、Never Placeの読出し・明示取得を含む全Never式は通常候補を供給しない。

| 構文 | 経路 |
| --- | --- |
| 列 | 評価順。通常完了後だけ継続 |
| if・require | 真・偽の両方 |
| match | 全アームと各ガードの両結果。一致なし経路はない |
| and・or | 右辺評価・省略の両方 |
| for・while | 反復と即時完了 |
| loop | 自身宛てexitだけが完了 |
| 転送 | オペランド、対象。元位置は継続しない |
| defer・ローカル関数 | 登録・宣言するだけで実行しない |

構造的完了は後始末を無視する。実行時到達可能性は同じ経路へ実際の後始末を適用し、結果受渡しを妨げうることと、require失敗本体が続行しないことを検査する。`while true`は`(true)`も含め、静的偽経路を保つ旨を警告する。最適化で受理可否は変わらない。

```kimi
func incomplete() -> i32
    if true => return 1          // Error: the false path supplies Unit.
var port: i32
while true
    port = tryBind()
    if port > 0 => exit
use(port)                        // Error: the false path need not initialize port.
let stopped: i32 = label work: do
    defer => loop => ()
    exit to work 1               // Type i32; the cleanup blocks delivery.
```

<a id="s-14-10"></a>

## 14.10. 型の絞込み

<a id="s-14-10-1"></a>

### 14.10.1. 絞込み可能な束縛と実効型

実行時isは、struct Core対象のオブジェクト型を持つローカルletと再代入不能パラメーターを、裸名・括弧付き名で絞り込む。var、フィールド、プロパティ、添字、呼出しは対象外で、別名にも事実を渡さない。

その名前の**実効型**は、宣言型のView Targetを、その位置で保証される最も派生した**実効Core**へ置き換えた型。派生CoreのOriginは対象補完（[§13.6.5](#s-13-6-5)）から得る。他は変えない。再代入は事実を終わらせるがメンバー変更では終わらない。完全ペイロード更新は旧フィールド内容の事実を終える（[§15.7.3](#s-15-7-3)）。

名前の全値使用（名前解決、オーバーロード、推論、明示操作、レシーバ取得）は実効型を使い（[§13.5](#s-13-5)、[§7.3](#s-7-3)）、通常規則で候補に合わせ、宣言型で再試行しない。スロット操作`@ref`・`@uniq`・`@raw`（[§13.5.6](#s-13-5-6)、[§5.4](#s-5-4)）とキャプチャーは宣言型を使う。絞込みは暗黙Moveを加えず、既存メンバー名が選ぶ層を変えない（[§6.2.2](#s-6-2-2)）。

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else => return
    let dog = value          // objref/Dog
    let slot = value@ref     // ref/(objref/Animal)
    alias.bark()             // Error: alias has no Dog guarantee.
```

<a id="s-14-10-2"></a>

### 14.10.2. 条件の事実と合流

| 条件 | 真経路 | 偽経路 |
| --- | --- | --- |
| `x is T` | x is Tを追加 | 維持 |
| `x is not T` | 維持 | x is Tを追加 |
| `not C` | Cの偽経路 | Cの真経路 |
| `(C)` | Cの真経路 | Cの偽経路 |
| 他 | 追加なし | 追加なし |

A and BではAの真経路でBを検査し、偽経路を合流する。orはその双対。全式位置に適用し、保存したboolや呼出し経由では追跡しない。

実効Core Sへの事実x is Dは、対象補完（[§13.6.5](#s-13-6-5)）とジェネリック全許容束縛（[§8.15](#s-8-15)）で判定する。DがSの厳密な派生とProvenならDへ絞る。無関係とProvenなら可能な動的型はなくなり、その到達可能経路では宣言型でxを検査し、後続検査は事実を加えない。警告は可能。他の場合も事実を加えない。

合流では、可能な動的型が残る全流入経路で保証される最も派生した基底を保つ。全経路で可能型なしなら合流先もなし。実行時到達可能性（[§14.9.3](#s-14-9-3)）が保つ経路だけを合流する。if・whileは真事実を本体、偽事実を後続へ渡す。else ifは直前の偽事実から始める。requireは真事実を後続文へ渡す。ループ入口は全戻り辺を合流し、最も絞られた安定解を使う。deferは登録地点の事実を保つ（[§16.1.2](#s-16-1-2)）が、関数・キャプチャーは取り込まない。

```kimi
// Dog and Cat derive from Animal; only Dog declares score() -> i32.
func afterDeadBranch(animal: objref/Animal) -> i32
    require animal is Dog else => return 0
    if animal is Cat => log("dead")   // animal is checked as Animal here.
    return animal.score()             // Valid: the contradicted path constrains nothing.
func insideContradiction(animal: objref/Animal) -> i32
    require animal is Dog else => return 0
    require animal is Cat else => return 1
    return animal.score()             // Error: animal is checked as Animal.
```

<a id="s-14-10-3"></a>

### 14.10.3. 到達不能コード

実行時経路が届かない項目も、仮定経路から作った状態で検査する。その経路では次のNever形式を通常継続すると仮定する。

- 転送、`$abort`、結果Neverの呼出し：オペランド評価後、後始末前、自身の字句スコープで続行。
- 他のNever式：評価後に続行し、明示取得は有効（x@moveはxをMovedにする）。
- 自身宛てexitのないloop：反復入口状態で続行。

それでも値は生まれず、配置先は初期化されない。`let y: i32 = fail()`の後のyは未初期化。後始末も同様に検査し、後始末で受渡しが妨げられる転送も届いたと仮定する。

実流入が一つでもある項目は実経路だけを使い、なければ仮定流入を合流して全てで成立することだけを保つ。放棄した比較だけに必要だったLoanは仮定継続前に終える。この状態にも全検査を適用し、未初期化・Moved読出し、二度目Move、Loan競合はエラー。実効型・条件事実も実経路同様に引き継ぐ。

仮定経路は暗黙結果、通常完了、実行時到達辺を加えない。到達不能コードの明示結果源はなお数える（[§14.9.1](#s-14-9-1)）。

```kimi
func scoreInBranch(animal: objref/Animal) -> i32
    if animal is Dog
        return 0
        return animal.score()   // Valid: unreachable, but keeps the Dog refinement.
    return 0
func scoreAfterReturn(animal: objref/Animal) -> i32
    return 0
    require animal is Dog else => return 1
    return animal.score()   // Valid: refined in the unreachable region.

var p: i32
label w: do
    defer => loop => ()
    exit to w          // Delivery is blocked by the cleanup.
use(p)                 // Error: p is uninitialized on the assumed path.

consume(x@move)
fail()                 // fail: () -> Never
consume(x@move)        // Error: x was Moved.
let y: i32 = fail()
use(y)                 // Error: y is uninitialized; fail() places no value.

func pass(x: Never, y: Never) => ()
func absurd(x: Never)
    pass(x@move, x)    // Error: x was Moved; the read of x is checked on the assumed path.
```

<a id="s-14-11"></a>

## 14.11. require

`require Expression else Body`はbool条件を一度評価（[§14.2.3](#s-14-2-3)）し、真なら続行、偽なら失敗本体を実行する。elseは条件最終行、または次行のrequireと同じ字下げに置く。結果・ラベル・対象・障壁を持たず、ifではない。

失敗本体への進入は常に仮定し、false条件でも静的真経路を保つ。後始末を含む失敗本体のどの経路も、文の後へ通常継続してはならない。外向き転送、Never呼出し、発散、Abortはこれを満たす。暗黙の操作は挿入しない。

```kimi
let answer = if enabled
    require ready else => yield 0       // Targets the outer if.
    yield compute()
else => 0
defer
    require needsCleanup else => exit   // Ends this deferred body.
    cleanup()
require true else => logFailure()        // Error: normal continuation.
require ready else => while true => ()   // Error: the static false path completes.
require ready else => loop => ()         // Valid.
```

<a id="s-15"></a>

# 15. 所有権と寿命の解析

本章はPlace（[§3.4](#s-3-4)）、借用、Originの使用可否を定める。転送や一時値の省略は、値、Placeの同一性と寿命、Move状態、Loan、Origin、破棄責任、失敗を変えない場合に限る。最適化でプログラムの適否は変わらない。

<a id="s-15-1"></a>

## 15.1. 初期化とMoveの解析

<a id="s-15-1-1"></a>

### 15.1.1. 初期化状態

| 状態 | 意味 | 読取り・借用・Copy・Move | `let`への書込み | `var`への書込み |
| --- | --- | --- | --- | --- |
| Uninitialized | 値がない | エラー | 一度も初期化していなければ可 | 初期化 |
| Initialized | 値がある | 型とアクセス規則に従う | エラー | 置換 |
| Moved | 値と責任を転送済み | エラー | エラー | 再初期化 |

`let`は各経路で束縛の寿命中に一度だけ初期化でき、Moveしても回数は戻らない。状態だけでは操作権限を得ない。読取り・借用・Copy・Moveには、到達する全実行経路（[§14.9.3](#s-14-9-3)）で対象がInitializedであることが必要。到達不能コードも仮定経路の状態（[§14.10.3](#s-14-10-3)）で検査し、経路がないことを使用許可にしない。

```kimi
var number: i32
if condition
    number = 1
else
    number = 2
inspect(number)           // Valid: both paths initialize number.

let resource: Resource
resource = makeResource()
consume(resource@move)
resource = makeResource() // Error: a let is initialized only once.
```

<a id="s-15-1-2"></a>

### 15.1.2. 構築と完全性

**完全な値**には、構築が完了した事実と、全格納要素がInitializedかつ完全である現在の完全性が必要。structは基底部分と自分のフィールドを層ごとに追跡し、enumは現在のCaseのペイロードだけを対象とする。全格納フィールドの初期化が必要で、計算プロパティは要素に含めない。

構築完了を確定するのは、コンストラクターの段階（[§6.3](#s-6-3)）、Case・タプル・配列の構築、`noinit`（[§4.3.4](#s-4-3-4)）、操作契約が明記する構築だけ。型を読み替えたPlaceは完全な値を持ち、再配置は完了の事実を保つ（[§5.2](#s-5-2)）。他の操作はこれを確定もリセットもしない。不完全な退出やAbortは確定しない。コンストラクターは成功を検証し、記憶領域を生かしたまま後処理を終えてから、公開・転送の前に確定する。

- 未初期化、または全体をMove済みで構築完了のないPlaceは、値全体を置くことでしか初期化できない。その部分の書込み・読取り・借用はエラー。ただし構築レシーバ自身の格納フィールド（[§6.3.4](#s-6-3-4)）は例外で、`self.inner.x`のような入れ子の部分は除く。
- 不完全な値の全体読取り、Copy、借用、Move、公開は禁止。全体の`self`を受けるアクセサー経由も同じ。
- 部分Moveは現在の完全性だけを変える。欠けた部分を全て再初期化すればコンストラクターなしで回復する。それまではInitializedな部分の使用・後処理だけが可能。全体Moveは構築完了を転送し、全体置換は新しい値の完了を引き継ぐ。
- タプル・配列は添字昇順に要素を置き、各配置の完了時に初期化と責任を取得し、全要素配置後に全体を確定する。途中の制御転送では配置済み要素を[§16.2.1](#s-16-2-1)順に後処理し、巻き戻さない。
- 固定配列の初回構築は全体で行う（[§4.3.2](#s-4-3-2)）。`noinit`後は読取り前に有効な要素値を与える責任がプログラマーにある。

```kimi
var pair: (string, i32)
// pair.0 = "a"           // Error: pair was never constructed.
pair = ("a", 1)           // Whole initialization.
let name = pair.0@move    // Partial Move: pair is incomplete.
// let all = pair@move    // Error: incomplete.
pair.0 = "b"              // Reinitializes a Move Path; pair is complete again.
let all = pair@move
```

<a id="s-15-1-3"></a>

### 15.1.3. Move Pathと部分Move

**Move Path**は独立した初期化状態と責任を追跡できる静的経路で、既知の基底部分経路を通るフィールド、タプル要素、定数添字の固定配列要素、およびその組合せ。実行時添字、動的コンテナー、ユーザーインデクサーは該当せず、定数畳込みや整数証明でも増えない。

定数添字は、任意基数・桁区切りを許す非負整数リテラル1個を、必要なら括弧で囲んだもの。`i32`に収まり、`[N of T]`に対して`0 <= n < N`のときだけ要素Move Pathとなる。値で同一視するため`1`、`0x1`、`(1)`は同じ。`1 + 1`、`-1`、`3@isize`、名前、選択、`^1`は該当しない。範囲外リテラル自体はエラーでなく、実行時に境界検査がAbortするが、Move Pathを要する操作はエラー。

- Move Pathにならない添字の要素は、配列全体が完全なときだけInitializedと扱う。一要素でもMovedまたはUninitializedなら、その添字での読取り・借用・Copy・書込みは禁止。
- 所有するmatch Subjectでは選ばれたCaseのペイロード位置もMove Path（[§15.1.6](#s-15-1-6)）。
- Move Pathは追跡粒度であり権限ではない。`@move`は要素、または[§11.2](#s-11-2)の格納プロパティを転送する。裸の読取りはCopy値をCopyする。
- 借用を通るNon-Copy参照先やその部分はMoveできない。`exchange`か`swap`を使う。途中の計算ゲッターも迂回できない。

全体を渡す呼出しを含む全体使用とユーザー`drop`の直前には完全性が必要。祖先の`drop`自体は部分Moveを禁じないが、通常完了、`return`、`try`伝播、`exit`、`continue`、`yield`、`defer`からそれらの地点に達する全経路で完全性を戻す。復元値を`try`で得るなら、失敗のreturnでも破棄可能でなければならない。Abortは巻き戻さない。`defer`や内側の破棄による復元は実行順に検査し、二重破棄しない。

```kimi
var items: [2 of Resource] = [Resource.init(), Resource.init()]
let i: isize = 1
let first = items[0]@move  // Partial Move: items is incomplete.
inspect(items[1])          // Valid: literal index, element 1 is Initialized.
// inspect(items[i])       // Error: a runtime index needs a complete array.
items[0] = Resource.init() // Complete again.
inspect(items[i])          // Valid.
```

<a id="s-15-1-4"></a>

### 15.1.4. Consume検査

**Consume**は使用箇所で[§15.1.5](#s-15-1-5)の操作を許せるかを静的検査する。**Eligibility**はPlaceの種類、所有経路、Move Path、フィールド宣言、`drop`制限による構造上の資格。**Legality**はその使用箇所で、アクセス権、全入力経路でのInitializedと完全性、必要なレシーバの構築完了、Loan非競合、Originの有効性、実際の祖先の条件を満たすこと。制約が証明するのは構造的事実だけで、不明なものはジェネリックAccess Effects（[§8.14](#s-8-14)）に従う。対象がInitializedかつ完全で、祖先全体にアクセスせず位置を得られるなら祖先は不完全でもよい。ユーザー`drop`で資格を失うことがあるため、実際の祖先も毎回検査する。状態、責任、構築完了、完全性を分岐、ループ、転送、`defer`にわたり追跡する。

<a id="s-15-1-5"></a>

### 15.1.5. アクセス操作と取得

**Movable Place**はTakeを持つ。追跡するPlaceには完全性と追跡可能な所有経路も必要。raw Placeとそのフィールド・要素は状態追跡なしにTakeを持ち、完全性は作者の義務（[§5.2](#s-5-2)、[§5.2.3](#s-5-2-3)）。所有ルート、許可されたインラインフィールド・タプル要素、定数添字の固定配列要素はTakeを持つ。借用先、オブジェクトのペイロード、静的記憶領域、動的コレクション要素、他の添字、隠れたプロパティ記憶領域は持たない。`remove`と所有イテレーターによる抽出は個別契約に従う。

| 操作 | 条件 |
| --- | --- |
| Copy | Read、完全性、完全な型がCopy |
| 共有借用 | Read、完全性、有効なPlaceと依存 |
| 排他借用 | ReadとWrite、完全性、排他的に書込み可能な貸出し点 |
| Move | Take。追跡するPlaceでは完全性と追跡可能な所有経路 |
| 初期化 | Uninitialized、宣言の初期化許可 |
| 置換 | Write、旧値の破棄可能性、新旧値の依存を壊さないこと |

全操作でアクセス、Origin、Loan競合を検査する。`let`の初回初期化と構築中の初期化は専用権限でありWriteやTakeではない。Movedな`let`は再初期化できず、Uninitializedな領域は`uniq/T`として借用できない。所有するジェネリック値はContractなしで全体Moveでき、不明な`T`は`T`として取得する。raw Placeは[§5.2](#s-5-2)と[§5.2.1](#s-5-2-1)の義務を保つ。

**貸出し規則。** `@move`はCopy値でも転送し、元をMovedにして完全な型・依存・責任を渡す。参照のMoveは参照先でなく能力を渡す。Movedな`var`の復元には書込み権限が必要。所有する一時値の転送には明記不要。

新しい排他借用には、経路を問わず`@uniq`か`@objuniq`を明記する。ただしレシーバ式は暗黙に取得する（[§7.3](#s-7-3)）。借用値は明記せず、固定期待型のモードで再借用するか裸で取得し、自身や経路の権限を強めない。借用値への`@ref`・`@uniq`はそのスロットを借用する。引数、初期化子、代入元、結果、既定値、要素、ペイロードは、期待型固定時に共通Adaptation（[§10.2](#s-10-2)）、それ以外は裸の取得（[§3.5](#s-3-5)）。ゲッター結果も結果として取得し、隠れた記憶領域からMoveしない。

**排他取得。** 所有Place・所有一時値・明示`@uniq`で借りる参照スロットは、貸出し点が排他的に書込み可能であることが必要。ルートとして許すのは`var`ローカル、可変静的領域、所有一時値、構築レシーバの初期化済み自身のフィールド、破棄レシーバ。標準射影はルート権限を継承し、プロパティ権限（[§11.2](#s-11-2)）とLoan制限を加える。`let`や所有型パラメーターをルートとする領域、所有ゲッター結果とそのインライン部分は不可。クロージャー環境束縛は所有型パラメーターでなく、`let`/`var`が決める。この条件は借りる記憶領域に適用する。返された参照の参照先には固有の権限があり、`let`やパラメーターに保持する`uniq`値も排他再借用できる。

```kimi
// describe: ref/Array<Task>; consume: Array<Task>; modify: uniq/Resource.
var tasks: Array<Task> = []
tasks.append(Task.init(1))      // Receiver: implicit exclusive acquisition.
describe(tasks)                 // Argument: shared borrow.
var first = makeResource()
// let bad = first              // Error: a bare Non-Copy Place never Moves.
let r = first@uniq              // Exclusive borrows need a spelling.
modify(r)                       // Reborrow of a borrow value in a let.
consume(tasks@move)
```

<a id="s-15-1-6"></a>

### 15.1.6. matchとforのSubject

`match E`と`for`は`E`を一度評価し、通常の取得で内部の**Subject Place**に置く。ただし裸のPlace（標準射影した一時値を含む）はその場で共有借用する。格納型が`ref/T`・`uniq/T`なら自身のモードで再借用し、経路上の共有層でSharedに制限する。`E@ref`、`E@uniq`、`E@move`、`E@copy`、一時値など他の式は生成値を保持する。

**Subjectモード**は安全な参照層を越えて与えるアクセスで、Shared（`ref`、`objref`）、Exclusive（`uniq`、`objuniq`）、ByValue（所有）のいずれか。条件を満たすペア層では許容Case中の最弱モードでありByValueにはならない（[§13.5.5](#s-13-5-5)）。Object Semanticsを許すなど他のペア層は安全参照層ではなく、束縛はそのスロットを値で取る。共有層が一つでもあればShared。`var`、Copy能力、適合、本体はモードを変えない。明示借用は記述したスロットを対象とするため、`let values: uniq/Array<T>`への`values@uniq`は禁止。`values`か`values@follow@uniq`を使う。`for`はモードで反復入口を選び、パターンは[§14.8.1](#s-14-8-1)でPlaceを選ぶ。

Subject Placeは分岐選択前に初期化する。独自・計算・要件`get`は一度読み、その一時PlaceがSubjectとなり[§11.5](#s-11-5)の制限を受ける。ペア層を越える場合もExclusiveで排他取得できるのは貸出し点が許すときだけ。許さない束縛や反復入口はSharedへ弱めずエラー。検査とガードは[§14.8.3](#s-14-8-3)に従う。選択後の借用SubjectのLoanは束縛の使用に従い、生きた借用束縛のない分岐は元のPlaceを置換できる。

ガードがtrueとなり後処理を終えてから分岐を選び、ローカルを左から初期化する。所有Subject・反復項目の部分は格納された完全な型`T`と責任を転送し、共有経路はCopyな`T`でも`ref/T`、排他経路は`uniq/T`を束縛する。`_`は何も束縛せずSubjectが責任を保持する。`let`/`var`はローカルの再代入可否だけを決め、借用経路の`var`は再代入可能な参照となる。借用Subject全体の束縛は元のPlaceへの参照。格納参照を再借用するSubjectではその参照先をSharedなら`ref/E`、Exclusiveなら`uniq/E`で束縛する。所有Subject全体なら転送する。参照越しの部分は経路の能力で束縛しMoveせず、所有Subjectの未束縛部分はSubjectに残す。

Subject一時値は構文全体の終了まで生きる。結果・外向き転送値を確保し、分岐スコープ（本体の束縛は逆順）、Subjectの残部の順に後処理する。ガードからの外向き転送はガード一時値をSubjectより先に処理する。借用Subject経由の借用は内部スロットでなく参照先と元のLoanに依存する。所有Subjectのペイロードへの新しい借用はmatchを脱出できない。

```kimi
match message                        // Shared borrow; Message is Non-Copy.
    .Write(let text) => inspect(text)  // text: ref/string
    _ => ()
match counter@uniq                   // Exclusive Subject.
    .Count(let n) => n@follow += 1   // n: uniq/i32
    _ => ()

// holder.p has a computed getter returning (s/T, i32); s is owner or uniq.
match holder.p
    (let x, _) => use(x)             // Error: in the owner case x would be uniq/T into the getter result.
```

<a id="s-15-2"></a>

## 15.2. Origin

OriginとLoanの概要は[§3.7](#s-3-7)。

<a id="s-15-2-1"></a>

### 15.2.1. Origin式

**Origin**は名前、射影（`x.source`）、`static`、共通部分（`a and b`）で表す記号的な寿命（[§F](#s-F)）。各Originの**region**は借用が有効なプログラム点の集合。型同一性は正規化した記号形を比較し、`outlives`と`==`はregionを制約する。

具体的Origin（`x`、`self`）はパラメーター・レシーバ・ローカルの外側の借用Origin。struct・enumの格納raw層の`self`はその値を指す（[§5.7](#s-5-7)）。抽象Origin（`source`）は型スロットまたはシグネチャで暗黙導入するOrigin。射影はスロットを選ぶ。安全な借用値をOriginに使うと保持する借用の外側Originを指し、記憶領域を指さない。スロット自体の借用には推論Originを使う。外側の安全借用Originを持たない値はOriginにならない。`x.source`は型のスロットで、ローカル値のOriginはその字句スコープ内でのみ使え、公開シグネチャには使えない。

```kimi
struct View<T> {source}
    let data: ref/T during source
    func get(self: ref/Self) -> ref/T during self.source => self.data
```

<a id="s-15-2-2"></a>

### 15.2.2. 順序と共通部分

`a outlives b`は`region(a) ⊇ region(b)`を要求し、反射的・推移的。`static`は全Originより長生きする。`a == b`はregionの等しさで、Loanを併合しない。`a and b`は集合の共通部分。

共通部分は可換・結合的・冪等で、`a outlives b`を証明できれば`a and b = b`。比較前に平坦化し、証明済み同値を同一視し、重複と他の残存項より長生きすると証明できる項を除く。1項ならその項、複数なら項集合で比較する。証明には検査する宣言の前提だけを使い、契約比較では各側を自身の前提で正規化する。順序は観測不能。式を簡約しても、異なる入力のLoan依存は除かない。

<a id="s-15-2-3"></a>

### 15.2.3. `static`と`Owned`

`static`中の借用は[§11.8](#s-11-8)に従う。可変静的領域への新しい安全借用は有限Origin。安全なコードでLoan要件`uniq`のOriginを`static`に束縛することは不可。raw Placeの借用は新しいアンカーを持ち、自身の上限を持たない（[§5.2.2](#s-5-2-2)）。

`Owned`は非static寿命に依存しないという型制約であり、所有権やメモリー割当て許可ではない。完全な型`T`の**OwnedOrigins(T)**が全て`static`と等しいときに限りOwned。別名展開と束縛代入後、次を集める。

- 外側Origin（raw層では`self`以外も含む）とSemantics対象（参照先、ペイロード、View Target、rawポインター先の型）。
- 未使用・継承分を含む全型引数・Origin引数。
- 基底、格納フィールド、全Caseのペイロード、タプル成分、配列要素、具体的クロージャーのキャプチャー。
- Function Itemの束縛引数、具体的クロージャーの固定シグネチャOrigin、共通関数型に記した固定Origin。呼出しごとのOriginは除く。共通関数型の隠れた環境と、基底・実行時Contractビューのペイロードは型消去時に証明済み（[§15.8.1](#s-15-8-1)）。

再帰型は[§15.3.5](#s-15-3-5)に従う。この依存自体はLoanを意味しない。空コンテナーや非選択Caseでも免除されず、`raw/(ref/i32 during local)`はOwnedでない。`a outlives static`は`a == static`を証明する。未証明OriginはFinality下のUnknownとなり、ジェネリック定義では宣言制約だけから証明する。

Ownedを要するのは静的領域、ビューへのペイロード消去、関数型の環境消去、循環ファクトリーのペイロード、明示制約だけ。取得、Loan、破棄順、unsafe、並行性の検査は免除しない。関数型変換を含め、Owned証明の失敗は制約失敗であり、型不一致やOrigin関係失敗ではない。

<a id="s-15-2-4"></a>

### 15.2.4. `Transferable`

`Kimi.Transferable`はパラメーター・メンバーなしのコンパイラー組込みContract。名前は通常解決し、特殊規則はKimi宣言だけに適用する。適合は構造で決まり、ユーザー宣言では付与できない。唯一のワーカー境界（[§22.3.6](#s-22-3-6)）へ渡せる値と、`confined`が読める不変static（[§8.11.2](#s-8-11-2)）を決める。Ownedは不要で、呼出し元領域の借用もjoinまでLoanを保持して渡せる。同期機能や外部コードの並行再入を許すものではない。

OwnedOriginsと同じ成分を、具体的オブジェクトペイロードも含めて調べる。呼出しシグネチャだけは何も格納せず、空コンテナー・非選択Caseも成分型を免除しない。到達可能な`rc`、そのWeak、rawポインターは`ref`・`arc`の内側でもRefuted。共通関数型、実行時Contractビュー、基底オブジェクトビューも同じ。例外はKimiの`Array`、`Dictionary`、`Slice`、`UniqSlice`と[§22.2.2](#s-22-2-2)の標準イテレーター内部のrawポインターだけで、実装がワーカーでの健全性を保証する。`Async.Sender`・`Async.Receiver`内部を含む他のrawは不可。ユーザーは例外を宣言できない。再帰型は[§15.3.5](#s-15-3-5)に従い、循環で除外成分を隠せない。抽象型には前提が必要。

```kimi
func pass<T>(value: T) -> T
    T is Transferable and Owned
    return value@move

func caller(local: ref/i32)
    let a = pass((1, "x"))                                  // Valid.
    // let b = pass(local)                                  // Error: ref/i32 during local is not Owned.
    // let c = pass(Kimi.Intrinsics.makeRc(Item.init(7)))   // Error: rc refutes Transferable.
```

<a id="s-15-3"></a>

## 15.3. Originスキーマ、名前、関係

**束縛集合**は型の出現箇所ごとにスキーマスロットをOriginへ対応づける。関数のOriginパラメーターリストや型のOriginマッピングはない。関連型だけはOriginパラメーターを宣言できる（[§8.5.4](#s-8-5-4)）。

<a id="s-15-3-1"></a>

### 15.3.1. 借用注釈と束縛集合

`ref/T during o`は[§3.3.2](#s-3-3-2)に従い1借用層のOriginを指定する。`during`は`ref`、`uniq`、`objref`、`objuniq`、`raw`、ペアバインダーだけに許す。ペア層では許容借用Caseだけに存在する条件付きスロット、raw層では指す領域を表す。引数は名前、`value.slot`、`set.slot`、`static`、括弧付き式の1 atomで、共通部分は`during (a and b)`。rawでは共通部分を許さず`self`を許す。注釈はLoanを作らず、寿命を延ばさず、取得・変換しない。

名前付き型の後の`{name}`はその出現の束縛集合名。定義時に既知で空でないスキーマを要し、重複名、空の波括弧、集合を単一Originとして使うことは禁止。波括弧は命名だけで、位置指定、`{source => x}`、集合全体の等式、`f{a}(...)`はない。位置指定Origin適用は関連型だけ。未使用集合名は契約を変えない。

既知スキーマが1スロットだけの型では`Slice<T> during source`は`Slice<T>{v}`と`origin v.source == source`の略で、借用層を増やさない。シグネチャの未束縛名は量化する。他の型では不可。

`value.source`は安全借用層だけを連続して剥がし、その型の宣言スロットを選ぶ。フィールド、型引数、rawポインターは辿らない。`set.source`は指定出現のスロットを選ぶ。未知スロットはエラーで、射影は型情報だけを読む。

```kimi
func identity<T>(value: View<T>) -> View<T>{result}
    origin result.source == value.source
    return value@move

func tail<T>(values: Slice<T> during source) -> Slice<T> during source
    return values[1..]
```

<a id="s-15-3-2"></a>

### 15.3.2. 型スキーマと記憶領域

struct・enumはジェネリックパラメーター後のヘッダーに、一つ以上の新しい単純名で自身のOriginスロットを宣言する。重複、空、継承名の再宣言は禁止。ヘッダーなしは自身のスロットなしを意味するだけでOwnedにはならない。group・Contractはスロットを宣言しない。

格納位置（フィールド、ペイロード、基底）の名前は宣言スロット、可視な外側Origin、またはraw層の`self`へ解決する。未宣言名はエラー。入れ子型は外側スロットを字句的に継承する。断片は共有制約領域・生成メンバーも含め[§6.1.2](#s-6-1-2)に従い、enumは分割不可。スロット名はAPIで、格納に使わないスロットもPhantom Originとして残す。

全格納借用層と集成型スロットは、型の公開Origin、`static`、完全な型引数、注釈、関係から補完する。初期化子から推論せず、隠れたスロットも作らない。フィールド内集合はそのフィールドと付随節だけで可視。残るフィールド・ペイロード条件は公開前提と型の適格性から導く必要があり、追加前提は型の制約領域に公開する。

```kimi
struct Renamed<T> {buffer}
    let data: ref/T during buffer
    let other: ref/T during souce       // Error: souce is not declared.

func forward<T>(value: Renamed<T>) -> Renamed<T>{result}
    origin result.source == value.source   // Error: Renamed has no slot source.
    return value@move
```

<a id="s-15-3-3"></a>

### 15.3.3. 宣言に付随する関係

`origin a == b`、`origin a outlives b`は各1関係を表し、複数節は論理積。連鎖記法、論理和、否定、実行時検査はない。関数・型では先頭領域に置く。フィールド、ローカル、enum Case、関連型指定、コンテナー別名では宣言直下に字下げし、アクセサーや本体より前に置く。名前は全てそこで可視でなければならない。

公開関数・型は全許容束縛で定義を検査し、使用時に代入済み前提を証明する。フィールド・ペイロード・関連型指定の残存条件は外側公開契約から証明する。基底型の追加前提は型の制約領域に公開する。ローカル・コンテナー別名は確立済み証拠と初期化子に照らして検査し、仮定しない。宣言パラメーターに依存しない閉じた条件は定義時の義務で、偽ならエラー。

<a id="s-15-3-4"></a>

### 15.3.4. 名前、スコープ、量化

名前付き呼出しシグネチャの借用注釈に現れる未束縛の単純名は、結果にしかなくても全称Originを導入する。関係やローカル注釈は名前を参照するだけ。特殊化と継承格納アクセサーは元の契約を保つ。

| 名前の所有者 | 可視範囲 |
| --- | --- |
| 呼出しシグネチャのOrigin・集合 | シグネチャ、節、本体 |
| 型スロット | 型宣言と継承メンバー |
| フィールド・Case・関連型指定の集合 | その宣言と節 |
| 基底出現の集合 | 基底節を書く断片の制約領域 |
| 関連型のOriginパラメーター | 右辺、formation型、節 |
| ローカル集合 | 型、節、以後のスコープ |
| コンテナー別名集合 | 節とその文書の別名スコープ |

検索は内側から最初のOrigin文脈候補があるスコープで止まる。同一スコープの値・Origin・集合の役割競合はエラーで、役割違いでも外側へ戻らない。レシーバなしの裸のフィールドは候補でなく、型パラメーター名は別名前空間。新しいOrigin・集合名は可視Origin、集合、パラメーター、ローカルを隠せず、重複も不可。互いに不可視のフィールド内名は再利用でき、宣言順は解決に影響しない。

名前付き入れ子関数のシグネチャは外側関数の可視Originを指定できる。それは入れ子側の固定Originで、新しい全称Originでも実行時キャプチャーでもないが、関数値はその型に記したOriginへ依存する。

関数型、Callable、匿名関数は名前付きOriginを導入せず、直接入力の呼出しごとの量化と自身の結果省略規則（[§8.10](#s-8-10)）を使う。集合名は外側宣言に属する。入れ子集成入力の各スロットは完全な型か外側Origin式で固定する。ただし初期化済みローカルの型と匿名関数パラメーター内で省略したスロット・入れ子借用Originは、一度だけ固定する本体推論変数となる。内側の呼出しごとのOriginはバインダーを脱出できない。

```kimi
func useView<T>(x: View<T>, callback: (View<T>{c}) -> ())
    origin c.source == x.source   // Fixed by the outer call.
    callback(x@move)

func use(source: uniq/i32, bound: ref/i32) -> i32
    origin source outlives bound
    func pass(value: uniq/i32 during bound) -> uniq/i32 during bound => value
    let child = pass(source)      // bound is a fixed Origin of pass.
    child@follow = 42
    return source@follow          // Valid: child has no later use.
```

<a id="s-15-3-5"></a>

### 15.3.5. 変性、Loan要件、Phantom Origin

安全借用は外側Originに、raw層は自身のOriginに共変。`uniq`、`obj`、`objuniq`、`raw`は対象に不変で、他のSemanticsは対象に共変（共有層は変更権限を与えない）。宣言の変性ではペア層は共変と数えるが、ジェネリック本体で不変Semanticsを許すなら対象は不変。関数パラメーターは極性を反転する。型の変性は格納構造から導き、注釈はない。

スロットの**Loan要件**は使用から`none < ref < uniq`を導き、入れ子では強い方を伝播する。raw層のOriginは使用に数えず、`during self`のポインター先はその型の格納内容に数える。要件は呼出し側で保持すべきLoanを指定するだけで権限を与えない。Originの同一化・短縮でLoanを併合せず、排他アクセスも生まれない。

変性、Loan要件、OwnedOrigins、Transferableは宣言ごとに自身の型パラメーターとスロットで一度計算し、具体化を展開しない。未使用、`none`、空集合、除外なしを底とする最小不動点なので、`rc/Nest<(T, T)>?`のような非正則再帰も停止する。

格納フィールドを辿り、束縛された入れ子型スロットも含めて安全参照や`Loan<T>`フィールドへ達しないスロットは**Phantom**。依存だけを保ち、有効性、Loan、Copy、アクセスを与えない。要件は`none`、変性は不変。

`Kimi.Loan<T>`は記憶領域を持たず依存を表すゼロサイズの管理struct。`T`の外側は`ref`、`uniq`、`objref`、`objuniq`に限る。フィールドとしては全スロット特性で`T`を格納すると数え、Copy可否も`T`と同じ。アドレスもアクセス権もない。安全な`Loan<T>.init(value: T)`は借用依存を保ち、破棄は自身の責任だけを終える。Loanは保持値が使用される間生きる。Slice、UniqSlice（不変）、Arrayは自身の規則でスロット特性を決め、`Weak<S>`は`S`の格納として扱う。

```kimi
struct Window<T> {source}               // Covariant in T and source.
    let loan: Loan<ref/T during source>
    let data: raw/()

struct Node<T> {a}
    let value: ref/T during a
    let next: Option<rc/Node<T>{n}>
        origin n.a == a                 // Recursion adds nothing; a is covariant.
```

<a id="s-15-3-6"></a>

### 15.3.6. Origin推論

未解決位置だけに変数を置き、変性、関係、使用で制約する。

| 位置 | 候補 |
| --- | --- |
| 共変 | 等式で固定されなければ、全上限の共通部分 |
| 不変 | 証明済みの等値。共通部分は使わない |
| 反変 | 他の全下限に対してoutlivesを満たすと証明できる下限 |

`outlives`循環の変数は同一化して解く。共変・反変両位置にある変数は不変。各変数は該当行の種類の境界だけから候補を取り、他の境界をその候補で検査する。境界を他変数経由で固定・有限Originと`static`まで辿り、一つも届かなければ候補なし。該当行の候補がない場合、本体ではローカルregionとし`static`に置換せず、本体外では注釈を要する。推論はオーバーロード選択後に判定し、候補の適用可否を変えない。

ソルバーが使うのは反射律、推移律、`static`の最大性、`==`代入、[§15.2.2](#s-15-2-2)、`a outlives (a and b)`、`b outlives (a and b)`、および`(a and b) outlives c`と「`a outlives c`かつ`b outlives c`」の同値だけ。右辺の共通部分を別々の要件に分解しないが、`x outlives a`から`x outlives (a and b)`を証明できる。前提の順序・重複に依存せず、問合せ式、前提、その部分式以外のOriginを作らない。UnknownとRefutedを区別し、Finalityに従う。

```kimi
func choose<T>(x: ref/T, y: ref/T) -> ref/T  // Result Origin: (x and y).
    return x                                 // Valid: x outlives (x and y).
```

<a id="s-15-3-7"></a>

### 15.3.7. 契約比較

型の適格性は定義側の前提、使用側の義務。スロット同一性は宣言に属する。

等価性は構造、量化、条件、束縛、保証を、各側自身の前提で正規化して比較する。Origin・集合の綴りだけではオーバーロードや特殊化を区別せず、スロット名はAPI。互換性には、要件が許す全呼出しを受け入れ、少なくとも同じ結果保証を与えることが必要。要件の全称Originは固定し、実装の具体化可能な呼出しOriginだけを解く。入力は`required <: implementation`、結果は逆向きに検査し、実装条件を要件前提から証明する。レシーバ、環境、権限、Loanは全許容Semantics Caseで別に検査する。Originには実行時表現、引数、寿命タグがない。

<a id="s-15-4"></a>

## 15.4. Originの補完と省略

契約は定義時に一度補完する。継承した完全な型を保ち、名前解決、注釈・関係の正規化、残る省略位置の補完を行う。束縛集合の命名は省略を抑止せず、名前付き関数の契約を本体から推論しない。

<a id="s-15-4-1"></a>

### 15.4.1. 明示的な結果関係

通常の呼出し可能体の集成結果スロットは、(1)確立・継承済み束縛と注釈・既存Origin式との等式で固定した位置を保持し、(2)明示`origin`節の正規化後も非自明な`outlives`関係に残る未束縛結果スロットを、その関係の下で全称量化し、(3)残りを[§15.4.3](#s-15-4-3)で省略補完する。未解決結果同士だけの等式は一つの同値類とし、全位置に同じ既定値が必要。既定値が異なる、またはない位置があればエラーで、明示関係を要する。型の適格性だけでは全称量化しない。`static outlives x`、`x == x`のような恒真式は省略・量化を変えない。

<a id="s-15-4-2"></a>

### 15.4.2. 位置ごとの規則

別名、括弧、冗長なowner接頭辞を正規化してから、安全借用層・集成スロットの省略Originを位置で補完する。raw層は補完せず、`during`なしはOriginなし。

| 位置 | 補完 |
| --- | --- |
| 直接借用パラメーター・レシーバ | 入力の外側層に独立Origin |
| 呼出し入力の集成型 | 未解決スロットごとに独立全称Origin |
| 関数結果 | [§15.4.3](#s-15-4-3)。結果型全体の省略はUnit |
| 初期化子ありのローカル | [§15.4.4](#s-15-4-4)で推論 |
| 初期化子なしのローカル、フィールド、ペイロード | 明示情報から完全にする |
| 静的領域 | Ownedと[§11.8](#s-11-8)を要す。共有層と`none`/`ref`スロットは`static`、`uniq`はエラー |
| アクセサー・特殊化 | 継承契約を優先。ゲッター結果は自身のレシーバを使う |
| Adaptation・検査・キャスト対象 | オペランドから補完（[§13.5.1](#s-13-5-1)、[§13.6.5](#s-13-6-5)） |

入力補完は型引数、タプル・配列要素、借用対象へ再帰し、フィールド、完全な型、呼出し境界を越えない。入力・スロット間の独立性を保つ。補完するのは直接入力の外側借用層と集成スロットだけで、入れ子借用層の省略Originはエラー。コンストラクターは型の結果契約を保ち、元の`s/T`は束縛、再構成`s/U`は条件付きスロットを保つ。

<a id="s-15-4-3"></a>

### 15.4.3. 結果の既定値

残る結果Originを各々補完する。(1)借用レシーバを含む直接借用入力があれば、その全外側Originの共通部分を使い、集成型内部のOriginは使わない。(2)なければ共有層は`static`、排他層は明示契約が必要。(3)集成スロットは全入力型のOwnedが証明され（入力なしも該当）、Loan要件が`none`または`ref`の場合だけ`static`、それ以外は明示契約が必要。

条件付き直接借用入力は全許容Semantics Caseで評価する。省略がOwned制約を追加することはない。`func f<T>(x: ref/T? during a) -> ref/T`の`x`は直接借用入力でなく、結果は`static`。

```kimi
func first<T>(x: ref/T) -> ref/T => x                 // during x
func choose<T>(x: ref/T, y: ref/T) -> ref/T => y      // during (x and y)
func view<T>(x: ref/T) -> View<T> => makeView(x)      // source: x
func invalid<T>(x: View<T>) -> View<T> => x@move      // Error: a result relation is required.
```

<a id="s-15-4-4"></a>

### 15.4.4. ローカルと独立した型式

ローカル宣言は型と推論変数を固定し、後続使用は変数を制約するだけで推論を再開しない。後の代入も同じ契約に合わせる。初期化子なしでは等式か確立済み証明で全スロットを固定する。未解決義務はFinalityで判定し、不確実さを`static`の創作や依存消去で埋めない。付随節は型注釈と一緒に集め、`view.source`は宣言型のスロットを指す。

本体内の型式はその宣言に関係を付けて束縛する。初期化子に記した型式の集合名はローカル宣言に属し、中間コンテナーは`(Outer<T>{o}).Inner<U>`と命名する。関数先頭節は本体の型出現を名指しできない。コンテナー別名節は実行時値や他文書の束縛を参照できない。通常の格納は取得、Move、Copy、呼出し、破棄を通じ完全な型と実際のLoanを保ち、一律Ownedを要求しない。ヒープ配置も参照先寿命を延ばさない。

<a id="s-15-5"></a>

## 15.5. 排他的Origin

排他借用の一意性はOriginでなく、競合規則と再借用の包含で成立する。排他レシーバ経由の`uniq`結果はそのOrigin内に収め、安全なコードではLoan要件`uniq`のOriginを`static`に束縛できない。

`(self: uniq/Self) -> uniq/T during self.source`は有効なシグネチャだが、`self`経由で格納フィールドを再借用すると`self`のOrigin内に制限されるため、そのreturnはエラー。raw領域の分割ではraw Placeのunsafeな`uniq`借用で実装する（[§15.6.3](#s-15-6-3)）。

```kimi
struct View<T> {source}
    let data: ref/T during source

    func get(self: ref/Self) -> ref/T during self.source
        return self.data          // Copy of the stored reference keeps source.

struct UniqView<T> {source}
    let data: uniq/T during source

    func getUniq(self: uniq/Self) -> uniq/T during self
        return self.data          // Reborrow through self; self stays borrowed.

    func intoUniq(self: Self) -> uniq/T during self.source
        return self.data@move     // Consumes the view, so the capability is not reused.

    func detach(self: uniq/Self) -> uniq/T during self.source
        return self.data          // Error: a Reborrow through self stays within self.
```

<a id="s-15-6"></a>

## 15.6. 借用検査

**プログラム点**は操作の直前・直後。静的フィールド領域はローカル同様のPlaceルートで、非標準アクセサーは呼出し境界となる。ローカルregionを推論し、シグネチャOriginは全称region、`static`は最大regionを表す。

**Loan**はアクセス経路で表すPlaceと`ref`/`uniq`モードを持ち、借用位置とSemantics Caseで識別する。同一位置・Caseの反復実行は同じLoan。暗黙借用はその操作位置、呼出し側Loanは呼出し位置、呼出し時に評価する既定値の借用は呼出しと既定値内操作で識別する。パラメーターOriginを通じ受けるLoanは本体内経路を持たず、呼出し側で到達先と静的効果に照らして検査する。Loanの**アンカー**は生成した借用の貸出し点。

- Loanを運ぶのは借用自身、Copy、派生参照、それらを含む集成値・クロージャー・記憶領域。呼出し後は結果と公開契約が書込みを許す領域も、保持し得る引数Loanを運ぶ。
- **アドレス。** `P@raw`の経路に借用・rawの参照外しがなければ`P`のルート保持元を指す。それ以外は`P`の借用が運ぶものを、新しいLoanを除いて運ぶ。`during self`のrawフィールドから読んだポインターは、そのフィールド領域への`@raw`と同じ対象に届く。アドレスはCopy、変位、raw型間変換、保持値を通じLoan同様に伝わるが、競合も権限も与えない。
- **保持元（holder）**はローカル、パラメーター、一時値で、全部分に格納された値のLoanを運ぶ。部分から読む・Copy・Moveする値も全Loanを運び、部分内参照経由の借用も親Loanとして運ぶ。Loanを観測し得る部分破棄は保持元全体を使用する。保持元全体への`=`、領域の開始・終了による完全置換でのみ旧値のLoanを捨てる。部分の書込み・Moveでは終わらず、書いた値のLoanを追加する。
- 参照、Move Pathにならない添字、動的コンテナー経由の書込みは、到達し得る全保持元にLoanを加えるだけ。呼出しの書込みも同じで、`replace`、`exchange`、`swap`は保持元を完全置換しない。ただし`replace`は[§15.6.3](#s-15-6-3)のLoan終了を行う。参照が届くのは、運ぶLoanのうち参照外しのない経路が指す保持元、自身と再借用経路の参照の保持元、運ぶアドレスの保持元。
- パラメーター・キャプチャー・静的領域から来る参照、契約が引数と結び付けない呼出し結果部分の参照、その派生参照は**本体が所有しない領域**にも届く。派生にはCopy・再借用、呼出しが保持し得る値（借用引数の読取りを含む）、それらを含む保持元の部分から直接または参照経由で読む値を含む。`Raw.allocate`結果など、上記規則で保持元に届かないrawも本体外領域だけに届く。そこに置く値は全プログラム点の後で使用され得る。参照経由の読取りは到達先のLoanを運ぶ。複合代入・増減は予約経由の書込みなので旧Loanを終えない。
- 点Pの後でLoan保持値が使用され得る（破棄による観測を含む）なら、そのLoanはPで**live**。値の保持元と重なるPlaceのLoan、または保持元アドレスを運ぶ値を使用する地点でも、元の値を使用すると数える。Pの操作は、P後に使用される値がP前から運ぶLoan（Pで置換する値を除く）と、Pが以後使われる保持元・本体外領域へ格納する値のLoanに照らす。Loan識別子でなく、これらの値が決める。借用はまだ運ばれていない自身の新Loanとは比較しないが、同一位置の過去実行から運ばれたLoanとは比較する。予約の有効化は自身の引数を除き、呼出しへ持ち込むlive値と比較する。
- 生存解析は可能性解析。合流では一経路でもliveならlive、ループは最小不動点。Origin regionや`outlives`は寿命の整合性だけを検査し、Loanをliveに保つものではない。

```kimi
var a = 1
var b = 2
var r = a@ref
r = b@ref       // r no longer carries the Loan on a.
a = 3           // Valid.
inspect(r)
```

```kimi
var a = 1
var b = 2
var slot: ref/i32 = a@ref
let r = slot@uniq
r@follow = b@ref    // A write through r: slot may now carry b's Loan, and a's Loan is not ended.
// a = 3            // Error: slot may still carry a's Loan and is used below.
inspect(slot)
```

<a id="s-15-6-1"></a>

### 15.6.1. Origin関係と型の適格性

Origin `o`を含む型の値がP後に使用され得るなら、Pは`region(o)`に属する。ただしraw層Originはregionを制約しない。

安全借用層`B/V during o`（`ref`、`uniq`、`objref`、`objuniq`、許容借用Caseのペア層）では、OwnedOrigins(V)の全Originが`o`に対してoutlivesを満たさなければならない。オブジェクト借用の`V`は型・Origin引数を含むView Target。ペア層の外側Originは存在するCaseだけで数える。記述・推論した全型は適格でなければならない。定義はシグネチャ（入力、レシーバ、結果、Self）の適格性を前提にでき、各呼出しで具体化型について証明する。

値を格納先型に合わせる際は、まずOriginを等しいとみなす正規化型同一性を構造的に検査する。これだけの失敗が型不一致で、その場合はOrigin関係を作らない。成功後、変性に応じ共変・反変位置から`outlives`、不変位置から`==`を作る。宣言関係、呼出しで代入した関係、適格性もOrigin関係。静的領域規則は`static`に合うかの検査。適用可否・期待結果による絞込みには構造部分だけを使い、Origin関係は選択後に判定して選択を変えない。呼出しの新Originが等式で複数固定Originと等しくなるなら、それらも等しくなければならない。

Owned、Callable、型同一性などOriginを使う制約証明は別要件で、適用可否に参加する。契約全体比較のOrigin部分は前提だけから[§15.3.7](#s-15-3-7)で判定し、ProvenまたはUnknown。UnknownはProofエラー、Callable制約内なら制約失敗。

RefutedなOrigin関係はエラー。Finality後もUnknownなら「偽」ではなく「未証明」のProofエラー。失敗関係はregion推論へ制約を加えず、それ自体によるLoan競合・結果不一致を派生させない。本体が確立すべき節・適格性は義務であり前提ではない。

```kimi
// Holder<T> is a struct with the Field value: T.
func get(b: objref/Holder<ref/i32 during c> during o) -> ref/i32 during o
    return b.value   // Valid: b's Type is well-formed, so c outlives o.
```

<a id="s-15-6-2"></a>

### 15.6.2. Placeの重なりと競合

アクセス経路を一段ずつ比較する。ルートはローカル、パラメーター、一時値、静的フィールドのキー、raw Place借用の新アンカー。段はインライン部分（フィールド、基底、タプル要素、添字、Caseペイロード）、own段、参照外し。`ref`・`uniq`・`objref`・`objuniq`の参照外し（例`.*`）のうち`ref`・`objref`を通るものは共有参照外し。`obj`・`rc`・`arc`ペイロードはown段で、経路はハンドルを延長する。参照外し同士、own同士だけが一致する。

| 経路 | 判定 |
| --- | --- |
| 同一、または一方が他方の延長 | 重なる。到達判定は下記 |
| 異なるルート（参照外しを含む） | 非重複。別名は各参照のLoanが保護 |
| 同じ集成値の異なるフィールド、タプル要素、定数添字とその延長 | 非重複 |
| 同じenumの異なるCaseペイロードとその延長 | バイト共有でも非重複。Case変更はenum全体書込みで全ペイロードLoanに届く |
| raw借用の新アンカーと、そこから派生しないPlace | 比較せずunsafeの義務 |
| その他（異なる定数と証明できない添字など） | 未証明。非重複証明を要する操作はエラー |

同一静的フィールドの全キーは外側型引数を問わず同じルート、別宣言は別ルート。Loan経路が操作経路の接頭辞なら操作はそのLoanに届く。逆にLoanが操作経路の延長なら、読取り・書込み・破棄は途中に参照外しがない場合だけ届く。Move・借用とLoanを観測し得る破棄は最初の共有参照外しまで越えて届くが、その場で破棄する対象への借用（`replace`）はこの借用の例外。`i != j`など実行時比較や整数証明は使わない。配列由来Sliceは配列全体の範囲を保ち、非標準アクセサー呼出しは全レシーバを使う。

到達するlive `ref`に対して読取り・共有借用は可、live `uniq`なら不可。書込み、Move、排他借用、Place破棄はどちらとも競合する。

変更は直接経路、排他参照、または権限内のrawアクセスだけで許す。共有層`ref`・`objref`・`rc`・`arc`以後は読取り・共有借用だけ。そこから読んだrawや共有Loanだけを保持するOriginのrawを通じる他操作は、静的エラーでなく未定義動作。参照カウント・Weak表はPlaceではない。

<a id="s-15-6-3"></a>

### 15.6.3. 再借用と領域分割

排他借用経由の借用は参照先に子Loanを作り、そこまでに参照外しした全参照のLoanを親として運ぶ。共有参照外しより先の借用は自身のLoanを作らず、最後の共有参照とそれ以後の参照外し元だけから派生してLoanを運ぶ。子がliveな間は親経由アクセスを停止し、子経路の接頭辞（親参照を保持するPlaceなど）もMove・借用できない。ただし子が共有のときの共有借用、その場で破棄する対象（`replace`）は許す。

Move Pathをその場で破棄すると、旧値の破棄を検査した後、その経路を正確な接頭辞とする全Loanを終える。対象は`=`、領域終了、`replace`。段ごとに比較し、定数添字は同じ定数だけに一致し実行時添字には一致しない。続きには参照外しを含む任意の段を許す。終了Loanはどの値も運ばなくなるが、子を運んだ値は親Loanを保つ。そのためパラメーターの再借用を返せ、外側参照の置換で旧値経由の子を終えても親が旧参照先を保護する。他の子は保持値が以後使用されなくなれば終わる。

子Originは親と、親へ到達する安全借用（レシーバ・クロージャー呼出しレシーバなど）のOrigin内に収め、最初の共有層を含むところまで制限する。共有参照経由の共有借用は、その参照のOriginだけに含まれる。liveな子の接頭辞のMove・exchange・借用（子が共有のときの共有を除く）はエラーで、`replace`は`=`同様に子を終える。

**領域分割**も同じ規則で一親から複数の子を作る。排他の子と親の残部は相互非重複を[§15.6.2](#s-15-6-2)またはraw Placeのunsafeな`uniq`借用の約束で保証する。共有の子は重なってよい。子経由の再借用はその子内に収まる。イテレーターや残部のレシーバOriginから返す値は、[§15.6.4](#s-15-6-4)の引数別保持規則が与えるものを運ぶ。イテレーター・残部の破棄は公開済みの子Loanを終えない。

ライブラリーのraw領域分割では、残部が`Loan<uniq/… during source>`フィールドで親Loanを保持し、`during source`のrawフィールドで領域を指す。各子はraw Placeの`uniq`借用を`during state.source`で返す。全ての子で[§5.2.2](#s-5-2-2)を守ることがunsafeの義務。結果が呼出しのレシーバLoanに依存せず、後続呼出しの効果が保持Loanと競合しなければ、具体・抽象呼出し先を問わず同じレシーバの後続呼出しをまたいで保持できる。

```kimi
// Tree has `var value: i32` and `var children: Array<Tree>`.
func deepest(root: uniq/Tree) -> uniq/Tree during root
    var cursor = root
    while cursor.children.length > 0
        let next = cursor.children[0]@uniq  // Keeps indexUniq's receiver Loan: path cursor.*.children.
        // cursor.children.clear()          // Error: reaches the Loan that next keeps, and next is used below.
        cursor = next                       // Assigning cursor ends that Loan; cursor keeps root's Loans.
    return cursor
```

```kimi
var x = 1
var y = 2
var r = x@uniq
let c = r@follow@uniq                                       // A child Loan: path r.*.
// let moved = r@move                                       // Error: Moves r while c is live.
// let old = Kimi.Intrinsics.exchange(r@uniq, with: y@uniq) // Error: borrows r exclusively while c is live.
Kimi.Intrinsics.replace(r@uniq, with: y@uniq)               // Valid: destroys r's old value in place, ending c's Loan.
c@follow = 3                                                // c keeps the parent Loan on x.
```

```kimi
var x = 1
var z = 3
var slot = x@uniq
var slot2 = z@uniq
var rr = slot@uniq
let c = rr@follow@follow@uniq   // A child Loan: path rr.*.*, with parent Loans on slot and x.
// let w = rr@follow@ref        // Error: Borrows rr.*, a prefix of c's path, while c is live.
rr = slot2@uniq                 // Destroys rr's old value in place, ending c's Loan.
rr@follow@follow = 7            // Valid: writes z through the new value.
// slot@follow = 2              // Error: c keeps the parent Loan on slot.
c@follow = 1                    // Writes x.
```

```kimi
// Take is the contract of §8.11.1.
func takeFirst<S>(h: uniq/(uniq/S during s, i32)) -> Option<S.Item>
    S is Take
    var r = h@follow@uniq
    let first = r.0.take()  // Returned from the receiver r.0's Origins: keeps what a read through it carries (§15.6.4), not r's Loan on h.*.
    h.1 = 5                 // Valid: no live value carries r's Loan.
    return first@move
```

<a id="s-15-6-4"></a>

### 15.6.4. 呼出し

呼出しは(1)呼出し先の抽象Originに新regionを作り、(2)入力型を具体化して引数を合わせ、(3)結果型を具体化し、代入済み宣言関係と全入力・結果型の適格性を証明して新regionを解き、(4)依存を集め、(5)保持値の使用中にliveとなる呼出し側Loanを作る。抽象的な元の`s/T`へのペア束縛はOriginを保ち、再構成`s/U`の条件付きスロットは引数の外側スロットへ束縛する。借用入力（クロージャー呼出しレシーバを含む）のOriginを記す結果は、その参照先型のOwnedOrigins全体にも依存する。

`uniq`要件は排他Loanを作り、結果が依存する間は排他的なまま。Origin等式はLoanを生成・解放しない。保護は評価順に借用形成時から始まり（該当排他借用は予約）、呼出し先の後処理を含む全呼出し中続く。

結果・書込み可能領域（キャプチャー経由も含む）が引数を保持できるかは、直接・間接呼出しとも具体化した公開契約だけで決める。不保持が未証明ならアクセスを許さない。判定はOrigin別でなく引数別。借用引数の参照先型内Originを保持し得る結果・領域は、そのどのOriginを記すかによらず引数経由の読取りが運ぶ全Loanを運ぶ。それ以外のOriginを保持し得るなら引数Loanを運び、借用引数では読取りのLoanも加える。

raw引数もポインター先を参照先として同様に扱い、その型内Originを保持し得るものは読取りが運ぶものを運ぶ。ただし引数値自身（Loanと指す保持元）を保持するのは、その型の値を格納できる結果・領域、または引数の領域内で形成する結果だけ。ポインターは自身のポインター先の一部でなく、raw引数経由で書く領域は引数値自身を保持しない。Origin付きraw層は、そのOriginの借用と同様のものをLoanなしで保持する。Originなし入力raw層はポインター先型が許す保持を許し、名目型フィールド外の結果raw層は全引数（rawでは経由読取りのものも）を保持し得て本体外領域へ届く。共有層以後に届くraw層を通じて呼出しが書くことはない。

**効果要約**はアクセスし得る静的フィールドと読取り・借用・書込み・置換・破棄・初回初期化を列挙し、呼出し先、閉じた明示特殊化集合の全実装、既定値、アクセサー、遅延初期化、後処理も含む。宣言ごとに自身の型パラメーターで一度、再帰呼出しの最小不動点として計算する。**パラメーター依存効果**は、Copy未証明の型パラメーター・関連型射影の破棄、その型の公開操作のユーザーコード効果、Callable入力経由呼出し。宣言前提が射影の基底（またはその基底を反復したもの）のいずれかを再帰Contractへの適合と証明する場合、そこに保持する依存効果を未知環境効果へ広げる。他の射影深さは有界で不動点は有限。Callable入力呼出しは入力・レシーバモードごとに一度記録し、使用時に実引数と合成する。適合の効果境界は完成した要約で検査する。

live Loanと競合する呼出し・直接staticアクセスはエラー。フィールドにアンカーを置くlive Loanが証明するのはそのフィールドの初期化だけ。未知環境効果・消去された破棄は、可視性やモードを問わず可変staticの全live Loanと競合し、不変staticには影響しない。タスク呼出しは常に未知環境効果。要件・Callable制約経由の効果は効果境界から取る。可変static借用は本体内に上限があり、そのLoanを運ぶ値は本体を出られない。広げた効果は、呼出し元前提なら元の全効果を分類できる場合も未知呼出し同様に競合する。

```kimi
group State
    public var text: string = ""
    public var count: i32 = 0
    public let limit: i32 = 10

func current(anchor: ref/i32) -> ref/string during anchor
    return State.text@ref              // Error: a borrow of mutable static storage never leaves the body.

func leak(anchor: ref/i32, target: uniq/(ref/string during anchor))
    target@follow = State.text@ref     // Error: nor through a parameter.

func run(g: () -> ())
    let c = State.count@ref
    let l = State.limit@ref
    g()           // Error: g's unknown effects conflict with c, used below.
    inspect(c)
    inspect(l)    // l alone would not conflict.
```

```kimi
contract Tree
    associate Child is Tree       // Recursive (§8.3.3).
    func child(self: ref/Self) -> Option<Self.Child>
        effect confined

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree // Every U.Child… of a CopyTree U is Copy.

group Trees
    public func walk<T>(node: T, depth: i32)
        T is Tree
        if depth == 0 => return
        match node.child()
            .Some(let next) => Trees.walk(next@move, depth - 1)
            .None => return
// walk's summary has unknown environment effects: T.Child has the recursive base T.

group Counters
    public var count: i32 = 0

func visitCopies<U>(node: U)
    U is CopyTree
    let c = Counters.count@ref
    Trees.walk(node, 2)           // Error: walk's environment effects are unknown and conflict with c.
    inspect(c)
```

<a id="s-15-6-5"></a>

### 15.6.5. Origin関係の判定

本体は全称シグネチャの全呼出し元で成立しなければならない。Origin関係はProven・Refuted・Unknownで判定する。

- **固定Origin**：本体契約が固定する全称Origin、入力・レシーバOriginとその射影、`static`。クロージャー内ではキャプチャー、呼出しレシーバ、本体から推論した結果全体のOriginも固定。固定Originは本体の全点を含む。
- **有限Origin**：上限のある各借用固有のOrigin。本体ローカル借用は、借用・rawを通らず、フィールド・要素・owning層だけで本体のローカル、パラメーター、一時値ルート、構築・破棄レシーバへ達する借用で、本体が上限。暗黙借用・キャプチャー入口も含む。クロージャー環境束縛は呼出しレシーバのフィールドなので、借用レシーバ経由では本体ローカルでなく、Consumingなら該当する。可変staticとインライン部分の借用も本体が上限で、return、入力・キャプチャー経由格納など固定Originへ運ぶ関係はRefuted。
- **推論region**：他のローカルOrigin（無注釈ローカル型のスロットなど）。raw Place借用は自身の上限を持たず、Origin指定raw経由ならそのOriginに包含される。借用経由の借用も固有上限を持たず[§15.6.3](#s-15-6-3)の包含に従うため、この分類。

到達不能な結果元と仮定経路も全関係を判定し、生存解析・到達可能性を使わない。`a outlives b`をa→b、`a == b`を両方向辺とする。両端が固定・有限Originで、内部が推論regionまたは有限Originの経路を**chain**とし、始点が終点に対してoutlivesを満たすことを要す。経路列挙でなく到達可能性で判定する。

| 判定 | 条件 |
| --- | --- |
| Refuted | 有限Originが自身の上限外の固定Originより長生きする必要があるchain。否定規則はこれだけ |
| Proven | 固定Origin間で前提からソルバーが導くchain、または端に有限Origin・推論regionを持ちRefutedでないchain（後者はregion推論を制約） |
| Unknown | 固定Origin間でソルバーが導けないchain |

ペアバインダーの適格性前提は各Semantics Caseの型から取り、外側Originスロットはそれを持つCaseだけに存在する。必要な全Caseで成立する前提からのみProvenとする。ローカルPlaceの寿命超過使用は破棄・Move時のLoan競合であり、Origin関係失敗ではない。ローカル・一時値の借用を返す場合は返却値位置でRefutedとなる。

```kimi
func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    return local@ref      // Error: the borrow of local cannot outlive x.

func f(x: ref/i32, y: ref/i32) -> ref/i32 during y
    let r = x
    return r              // Error: the chain x -> r -> y needs x outlives y, which is not proven.

// Box<E> has the Field item: E.
func pick<s/T>(b: ref/Box<s/T during a> during c, x: ref/i32 during a) -> ref/i32 during c
    s is owner or ref
    return x              // Error: a outlives c holds only in the ref case.
```

<a id="s-15-6-6"></a>

### 15.6.6. 破棄時の寿命検査

破棄責任・順序は[§16.2](#s-16-2)、[§16.3](#s-16-3)。破棄が観測し得る全Origin・Loanはその地点で有効でなければならない。ユーザー`drop`は本体が未使用でも全到達可能Originを観測するとみなし、フィールド破棄にも再帰適用する。Originを何も観測しない破棄は要件を増やさない。

Loanを観測し得る破棄は、破棄Placeとそこから`obj`・`rc`・`arc`ペイロード、排他参照外しを越え最初の共有参照外しまでのPlaceへの排他使用。`=`、領域終了、`replace`など対象を破棄する呼出しで、Loanを終える前に検査する。何も観測しない破棄は破棄Placeだけにアクセスする。

```kimi
// Log is a group with observe(value: ref/Writer).
struct Logger {sink}
    let out: uniq/Writer during sink
    drop
        Log.observe(self.out)    // sink must be valid here, even for an empty drop.
```

```kimi
// S has the Field r: uniq/i32 and a drop that writes through it.
var x = 1
var y = 2
var s = S(r: x@uniq)
let c = s.r@follow@uniq          // A child Loan: path s.r.*.
s = S(r: y@uniq)                 // Error: the old s's drop may write through s.r while c is live.
c@follow = 1
```

<a id="s-15-6-7"></a>

### 15.6.7. 呼出し予約

**呼出し予約**は評価・寿命保護を保ったまま排他アクセスを遅らせる。呼出しのレシーバ・引数を作る排他借用・再借用は、直ちにlive Loanとせず予約にする。明示`@uniq`、`@uniq/T`、`@follow@uniq`、`@objuniq`、レシーバ暗黙取得、排他入力への借用値の再借用、許可された排他ペイロードfollow、排他に解決したジェネリックAdaptationを含む。

ユーザーコードを呼ばない標準フィールド・要素射影でルートからレシーバ・引数の貸出し点まで達する経路は、明示排他借用を含んでも一つの準備で、呼出し前にlive化しない。よって`holder@uniq.items.append(x)`、`holder.items@uniq.append(x)`、`holder.items.append(x)`は同じ。括弧は透過。経路上のゲッター・別呼出しは各々の呼出しで有効化する。全呼出し種別に適用し、オーバーロード選択を先に行い、予約の適否で変えない。

代入先は予約を開始せず、複合代入・増減は[§13.7.2](#s-13-7-2)に従う。予約はローカル・集成格納、キャプチャー、入れ子呼出し、制御フロー式結果、複合代入、増減、添字式を通過しない。格納した再借用（格納排他参照の裸取得を含む）は形成時にlive。

対象を一度評価・位置確定・検査し、Place、所有者、経由参照を保護する。共有読取り・借用は共存可能だが、書込み、Move、破棄、再割当て、他の排他操作は禁止。子予約は親権限を保つ。レシーバを先に位置確定し、明示引数を記述順、既定値をパラメーター順に評価する。既定値は呼出し前に終わる共有Loanで予約入力を読める。再帰的既定値の値が運ぶ予約自身のLoanとは有効化が競合しない。

その後、保持依存、キャプチャー、static効果、後処理効果を含め全予約を同時にlive化できなければならない。重なる排他同士、共有と排他の引数は呼出し先が未使用でもエラー。重なりは引数値の運ぶLoanで判定する。共有参照外し以後の借用は新Loanを作らないため、`r: ref/i32`の`g(s@uniq, s.r@follow@ref)`は有効。有効化を成立させるため一時値を早期破棄しない。制御転送はオペランドの評価・取得中も予約保護を維持し、中止する呼出しの予約だけを有効化せず終了してから、残る準備の後処理を行う。引数内部で受け止められる転送では、呼出し準備を中止しない。

```kimi
// tasks: Array<Task>.
var p: i32 = 1
Kimi.Intrinsics.replace(p@uniq, with: p + 1)  // Valid: p is read during the reservation.
tasks.insert(tasks.length, Task.init(9))      // Valid: a shared read during the reservation.
// tasks.append(tasks.remove(0))             // Error: two exclusive acquisitions of tasks overlap.

struct Inventory
    var newest: Item
    func newestItem(self: uniq/Self) -> ref/Item during self => self.newest

var inventory = makeInventory()
let item = inventory.newestItem()             // inventory stays exclusively borrowed while item is used.
// inventory.newest = makeItem()              // Error.
inspect(item)
```

<a id="s-15-7"></a>

## 15.7. 値全体の更新

以下の`Kimi.Intrinsics`はコンパイラー組込みで、Sealed・Copy・Owned制約を課さない。

| 操作 | 旧内容 | 新内容 | 結果 |
| --- | --- | --- | --- |
| `replace` | その場で破棄 | 破棄完了後に配置 | Unit |
| `exchange` | 転送 | 取得済み値 | 旧値 |
| `swap` | 両方転送 | 交換 | Unit |

対象は完全にInitialized、排他書込み可能、全体更新可能であること。`uniq/T`の不変性により対象が`T`を固定し、`value`を通常引数として合わせる。対象は`T`全体の記憶領域で、通常owner領域はopen Coreを含められるが、オブジェクトペイロードには[§13.5.5](#s-13-5-5)の完全対象証明を要する。基底部分・openオブジェクトビューは対象外。

対象も引数なので、所有Placeは`@uniq`、借用値は明記せず再借用し、ペイロードは`handle@follow@uniq`で選ぶ。借用型`T`では参照自身を転送する。未初期化・部分Move領域を修復したり、借用経由で値をMoveしたりはできない。`let`フィールドを持つ全体の置換はそのフィールドへの書込み権限を与えない。

```kimi
group Intrinsics    // Kimi.Intrinsics
    public func replace<T>(target: uniq/T ! with => value: T) -> ()
    public func exchange<T>(target: uniq/T ! with => value: T) -> T
    public func swap<T>(first: uniq/T, second: uniq/T) -> ()
```

<a id="s-15-7-1"></a>

### 15.7.1. 評価と転送

評価・予約・有効化は[§15.6.7](#s-15-6-7)。全引数の取得と期待型に合うかの検査を更新前に終え、引数が完了しなければ対象は不変。`replace`は通常のdrop・フィールド・基底順に旧値をその場で破棄し、そのAbort・発散は配置を妨げる。配置でコンストラクター・初期化子・セッターは動かない。`exchange`・`swap`はユーザーコード、破棄、Abortし得る操作を実行しない。

```kimi
var p: i32 = 0
Kimi.Intrinsics.replace(p@uniq, with: p + 1)
let old = Kimi.Intrinsics.exchange(p@uniq, with: 5)
var q: i32 = 9
Kimi.Intrinsics.swap(p@uniq, q@uniq)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)            // Error: overlapping targets.
// Kimi.Intrinsics.exchange(x@uniq, with: x@move)  // Error: transfers reserved storage; write x = x@move.
```

<a id="s-15-7-2"></a>

### 15.7.2. 静的な非重複

`swap`は[§15.6.2](#s-15-6-2)による対象の非重複証明を要する。liveな排他入力`a: uniq/T, b: uniq/T`は満たすが、実行時比較は証明にならない。

<a id="s-15-7-3"></a>

### 15.7.3. 記憶領域更新の依存

この規則は`=`、複合代入、増減、replace、exchange、swapの全てに適用する。記憶領域と内容の寿命は別で、rawポインターはLoan証拠を代替しない。

入力・返却・生存値が要するLoanとOrigin、操作の排他Loanと親連鎖を全て保つ。旧内容にアンカーを持つlive借用を無効化する更新はエラーで、同じアドレスへの再配置でも復活しない。独立した外部依存は残る（格納`ref/Data`のCopyは外部Loanだけ、フィールド自体の借用はフィールドに依存）。ペイロード更新はオブジェクト同一性、割当て、動的型、所有モードを保ち、最終解放ではない。空または破棄中のペイロードを公開しない。exchangeで返す旧値は自身の破棄義務を運ぶ。旧内容の事実は無効化するがオブジェクト同一性・動的型の絞込みは保つ。ハンドル置換は[§16.3.3](#s-16-3-3)。

```kimi
var data = makeData()
var other = makeData()
var slot: ref/Data = data@ref
let copy = slot                 // Depends on data only.
let inner = slot@ref            // Borrows the slot itself.
slot = other@ref                // Error: inner is used below.
inspect(inner)
```

<a id="s-15-8"></a>

## 15.8. キャプチャーと型消去の依存

<a id="s-15-8-1"></a>

### 15.8.1. オブジェクトペイロードの型消去

具体的ペイロードを基底・実行時Contractビューに隠すには、その完全なデータ型のOwnedが必要。ハンドル自身のOriginは判定外なのでローカルオブジェクト借用のOriginはローカルのまま。生成にはObjectPayloadも必要。消去ビューが証明を保証し、後のアップキャスト・キャスト、オーバーライド内の字句`self`も継承する。実行時検査・キャストの対象補完で`static`を与えられるのは、この証明が覆う固定束縛だけ。obj・objuniqのView Targetは不変で、他のハンドルは変更権限を持たないため、証明はオブジェクト寿命中保たれる。

<a id="s-15-8-2"></a>

### 15.8.2. クロージャーの依存

クロージャーは作成ブロックの寿命だけでなく、全キャプチャー値のOrigin・Loan依存を保持する。借用内容のない所有値のMoveキャプチャーは借用せず、外側スロットを借りる入口はそのスロットのLoanを保つ。

| 結果元 | 契約 |
| --- | --- |
| 環境所有データの借用 | 呼出しレシーバ借用に依存 |
| キャプチャーした共有参照、またはその経由共有借用 | 外部Originを保持 |
| キャプチャーした排他参照の再借用 | 外部Origin・Loanを保持。Shared・Exclusive呼出しでは呼出しレシーバにも依存 |
| Consuming呼出しでMoveして取り出す参照 | Originと能力を転送 |
| 借用引数 | パラメーターOrigin契約に従う |

結果型省略時、推論結果の各Originは内部呼出し契約の呼出しごとのOriginで、表に従う全結果元のOrigin内に収まる。複数結果では共変位置は元の共通部分、不変位置はOrigin等値を要求する。呼出しローカル領域や呼出しで消費する環境の借用など他の元はエラー。推論する結果全体のOriginは本体外の固定Originなので、本体ローカル借用の返却は返却値で関係失敗となり、領域破棄時のLoan競合ではない。

注釈結果・固定期待シグネチャは通常の結果省略を使う。隠れたレシーバを名指すOrigin式はなく、注釈名はパラメーターと外側スコープから解決し環境束縛からは取らない。共通関数型・Callable制約は隠れたレシーバ依存結果を返さない。

```kimi
let text = makeText()
let get = func [text@move] () => text@ref   // Result depends on the call receiver.
let view = get()
// let moved = get@move                     // Error: view is used below.
inspectText(view)

let other = makeText()
let invalid = func [other@move] () -> ref/string => other@ref
// Error: the annotated result defaults to static, not the environment borrow.
```

<a id="s-15-8-3"></a>

### 15.8.3. 脱出と保持

返却値、格納フィールド、別クロージャー、間接呼出し先は宛先の寿命・能力契約で検査する。借用キャプチャーが本質的に脱出不能なわけではなく、Moveキャプチャーも内包借用の依存を保つ。クロージャーのMove・ヒープ配置は参照先寿命を延ばさない。`ref/F`もCallableも環境由来値を保存しない保証ではない。Ownedはスレッド間転送を許可しない。

<a id="s-16"></a>

# 16. スコープ退出と破棄

<a id="s-16-1"></a>

## 16.1. deferブロック

`defer`は到達時に後処理を登録し、その時点では本体を評価しない。本体は結果を持たない共通本体（[§14.2](#s-14-2)）で、一時停止しない。

登録は直接包む実行本体スコープ（関数、分岐、分岐、現在の反復、do、unsafe、require失敗本体、実行中のdefer本体）に属し、そのScope Exitで実行する。未到達の登録は実行せず、反復ごとに別登録となる。登録の取消しや手動実行はない。

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
// For flag = true: "branch end", "after branch", "function end".
```

<a id="s-16-1-1"></a>

### 16.1.1. deferの制御境界

deferブロックを越える外向き制御転送は禁止。ブロック自身を対象に、オペランドなしまたはUnitに合う`exit`で本体を終えられる。入れ子選択、do、unsafe、require失敗本体からも同じ。内部のループへのexit・continueや選択・doの結果は通常どおり。その他の外部対象への転送は到達不能でもエラー。入れ子関数は自身の関数境界を保つ。

早期exitは自身の後処理を終えて保留中の外側Scope Exitへ戻る。登録を取り消さず、保留結果・転送を置換しない。

```kimi
func run() -> i32
    defer
        defer => log("inner end")
        for value in values()
            if done(value)
                exit       // Targets the inner for.
        if alreadyClosed()
            exit ()        // Ends this Deferred Block; "inner end" still runs.
        close()
    return 0
```

<a id="s-16-1-2"></a>

### 16.1.2. deferの評価と所有権

名前とEffective Typeは登録位置で解決する。登録はローカルをCopy・Move・借用せずクロージャーも作らない。本体は実行時の束縛を使い、全退出経路でその時点の所有権・Loan規則を検査する。後処理中にMoveした元は後の自動破棄から外れる。

内側deferは外側deferの実行中に登録し、その本体退出時、残る外側後処理より先に実行する。退出中のスコープへ登録し直せない。deferはunsafe権限を与えず、操作の字句文脈に従い、実行時に安全条件を満たす必要がある。二重破棄防止やrawポインター寿命延長の機能ではない。

```kimi
var count: i32 = 1
let saved = count
defer => log(saved)          // Prints 1, second.
defer => log(count)          // Prints 2, first.
count = 2
```

```kimi
let resource = makeResource()
defer => inspect(resource@ref)
consume(resource@move)       // Error: the cleanup reads a Moved value.
```

<a id="s-16-2"></a>

## 16.2. スコープ退出

**Scope Exit**は登録deferとスコープの自動破棄を実行する。通常完了、return、exit、continue、yield、try伝播でスコープを離れるときに起こる。値が属するスコープは各構文の規則が定める。

<a id="s-16-2-1"></a>

### 16.2.1. 後処理の順序

退出スコープを内側から処理し、各スコープでは宣言とdeferを字句順の逆に処理する。後の初期化・再代入で束縛位置は変わらない。登録済みdeferと、Initializedでそのスコープに破棄責任の残る値だけを処理する。最終使用だけでは責任は終わらない。入口のパラメーター、self、反復・パターン束縛は記述順で本体文より前に置き、暗黙束縛の位置は各構文が定める。

式が完了前に転送で中断した場合、生きた一時値と未完了タプル・配列・Case構築の配置済み成分は、取得時の最内実行本体スコープに属する。そのスコープで取得逆順に処理し、それ以前の宣言・deferより先に破棄する。matchのガード一時値・Subjectも含む。責任を転送済みの値はこの順序から除く。

```kimi
func process(first: Resource, second: Resource)
    let a = makeResource("a")
    defer => log("A")
    let b = makeResource("b")
    defer => log("B")
// Cleanup: "B", b, "A", a, second, first.
```

```kimi
func f() -> Result<i32, ParseError>
    defer => log("defer")
    consume(makeGuard(), try parse()) // On Err: the Guard temporary is destroyed, then "defer" runs.
    return .Ok(0)
```

<a id="s-16-2-2"></a>

### 16.2.2. 結果と転送

転送結果と、所有構文の結果として使う単一項目式は、(1)オペランド評価（別転送が起きればそちらを処理）、(2)Copy・Move規則で結果確保、(3)結果式で寿命が終わる一時値の破棄、(4)退出スコープのScope Exit、(5)確保結果の配送と対象の終了・継続、の順に進む。他の一時値は中断式の規則で処理する。

省略オペランドはUnit。Move結果は元で破棄せず、Copyなら元の責任を保つ。後処理は確保結果を置換できない。結果内借用は全後処理後も有効でなければならず、全過程で破棄寿命検査を行う。実際に離れるスコープだけを処理するため、continueは現在反復分、名前付き転送は離れる全スコープが対象。

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value                // Returns the already Copied 1.
```

<a id="s-16-2-3"></a>

### 16.2.3. 完了と異常終了

defer登録は本体開始時に消費し、高々一度実行する。保留結果・転送は全後処理完了後だけ配送する。後処理は一時停止も取消し観測もしない。

drop内を含めAbort後は後処理せず、巻き戻さない。強制プロセス終了、未定義動作、未解消の参照カウント循環では後処理を保証しない。

<a id="s-16-3"></a>

## 16.3. 破棄宣言

`drop`はstructの一層の破棄を定義する。生成断片を含めstruct直下だけに置き、条件選択・統合後に高々一つ。パラメーター、ジェネリクス、Origin、結果注釈、修飾子はない。共通本体を使い、単一式結果は捨て、明示returnはUnitに合わせ、unsafe操作には内側unsafeブロックが必要。

呼出し元は破棄だけで、直接・間接呼出しや関数値化は禁止。空のdropもstructをNon-Copyにし、部分Move後は全体使用・破棄までに完全性を戻す必要がある。全体Moveは可能。アクセス修飾子は破棄を抑止しない。

```kimi
struct Marker
    Self is Copy      // Error: a struct with a drop cannot be Copy.
    drop => ()
```

<a id="s-16-3-1"></a>

### 16.3.1. 破棄レシーバ

dropのselfはアクセス・Loan検査上`uniq/Self`相当の排他アクセスを持つ専用レシーバで、通常借用値でも所有者でもなく、Copy・Move不可。

全体を代入・exchange・swapの対象にしたり、メソッド・セッターを含め`uniq/Self`として渡したりできない。初期化済みフィールドは通常権限に従うが、所有struct PlaceではないためフィールドのMoveは不可。値を取り出すにはexchange・swapまたは型固有操作を使う。`self@ref`はdrop本体内で終わる通常の`ref/Self`で、ヘルパーに渡せるがキャプチャー・格納・返却不可。obj・rc・arc・objref・objuniqへ変えたり、後用に保存・脱出させたりできない。層Dの破棄中はSelf=Dで、破棄済み派生層は利用不可。

```kimi
struct Connection
    var buffer: Buffer
    drop
        // self.reset()                   // Error: reset takes uniq/Self.
        // let moved = self.buffer@move   // Error: a field of drop's self cannot be Moved out.
        let whole = self@ref
        Audit.inspect(whole)              // Allowed: a helper borrow.
        // let later = func [whole] () => Audit.inspect(whole) // Error: captures the destruction receiver.
        Audit.observe(self.buffer@ref)    // Shared borrow of an initialized field.
        let old = Kimi.Intrinsics.exchange(self.buffer@uniq, with: Buffer.init())
        Audit.release(old@move)
```

<a id="s-16-3-2"></a>

### 16.3.2. 成分の破棄

完全なstruct層は、自身のdropとそのScope Exit、自身のフィールドの論理宣言逆順、直接基底への同じ処理の順で破棄する。drop開始時には全フィールド・基底がInitializedで、dropのreturnも後続破棄を省略しない。計算プロパティは成分でなく、後処理はアクセサーを呼ばない。

部分Moveした層に自身のdropがなければ残るInitializedフィールドだけを逆順破棄する。自身のdropがあれば事前に完全性を戻す必要がある。基底は自身の状態で処理する。フィールド代入・置換順は破棄順を変えない。

タプル・配列は添字降順、各要素を完全に終えてから前要素を破棄する。enumは現在Caseの残存ペイロードだけを宣言逆順に破棄し、所有分解で転送したものを飛ばす。Slice・UniqSlice破棄は要素を破棄しない。Scalar固定配列の破棄はnoinitでも要素を読まない。ライブラリーコンテナーの要素順は各契約で定める。

```kimi
struct Triple
    var a: Resource
    var b: Resource
    var c: Resource

func split(t: Triple)
    consume(t.b@move)   // Partial Move; Triple has no drop.
// Scope exit destroys t.c, then t.a.
```

<a id="s-16-3-3"></a>

### 16.3.3. ハンドルとオブジェクト解放

owner/Tは正確にTとして破棄する。obj/Tはオブジェクトを破棄して記憶領域を解放する。rc/T・arc/Tは強参照を一つ解放し、0にした解放がオブジェクトを破棄・解放する。基底ビュー経由でも実際の型で派生から基底へ全後処理を行う。

ハンドル破棄の対象Placeはハンドルだけで、live Loanと比較する。ペイロード借用は到達元ハンドルのLoanを保持するため、所有するオブジェクト領域自体を対象にしない。rc・arc解放は最終解放の可能性があり、ペイロード破棄の観測と、staticなど他領域への後処理効果を要約に含む。呼出し先が所有ハンドル入力を破棄する場合も同じ。

動的型・環境型が静的に不明な破棄は未知効果。View Target自身のdropや宣言済み派生型だけからは求めない。SealedでないView Targetのobj・rc・arc（open基底、実行時Contract）と共通関数型環境、またそれらを成分に持つ値が該当する。可変staticの全live Loanと競合し、confinedでは分類不能。Sealed対象ハンドルとowner/Tはその型の破棄要約を使う。ジェネリック本体でCopy未証明の型パラメーター・関連型射影の破棄は、本体内検査ではLoan観測可能・未知環境効果とし、要約では実型と合成するパラメーター依存効果として公開する（[§15.6.4](#s-15-6-4)で広げる場合を除く）。

ペイロード全体置換は旧内容をその場で破棄し、領域・カウント・ライフサイクル状態を保つ。領域はUninitializedになるがlet初期化履歴は戻さず、コンストラクター・初期化子・セッターなしで新値を置く。exchangeは旧内容を結果へ移す。同じアドレスでも旧内容依存は終わる。借用・rawポインター破棄は参照先を破棄せず、Scalarに破棄処理はない。

```kimi
var action: () -> () = func [resource@move] () => ()
let view = State.text@ref          // State.text is a mutable static Field.
action = func [] () => ()          // Error: destroying the old environment has unknown effects.
inspect(view)
```

<a id="s-16-4"></a>

## 16.4. クロージャー環境と破棄中のオブジェクト

残るキャプチャーは初期化逆順で破棄する。Consuming呼出しの環境束縛はパラメーターより前なので、そのキャプチャーを最後、結果配送前に破棄する。Shared・Exclusive呼出しは環境を破棄しない。ゼロサイズを含め他の成分同様にOriginを観測する。

破棄レシーバはハンドル・オブジェクトビューになれないため、破棄対象への実行時ディスパッチ、型検査、検査付きキャストはない。対象へのWeak upgradeはNone。破棄は完了まで対象領域の排他Loanを保持し、static経由を含む他アクセスは競合する。drop内の外部関数・関数値呼出しも追加制限なく通常のOrigin・Loan・static効果で検査する。raw経由の違反は未定義動作。

```kimi
let first = makeResource("first")
let second = makeResource("second")
let job = func [first@move, second@move] () => ()
// Destroying job destroys second, then first.
```

```kimi
struct File
    var handle: isize
    drop
        Native.close(self.handle)   // Allowed: a foreign call checked by the ordinary rules.
```

<a id="s-17"></a>

# 17. 失敗処理

<a id="s-17-1"></a>

## 17.1. エラー方針

例外はない。呼出し元が扱える失敗はOption・Result、通常実行を続けられない場合はプロセス全体のAbortとする。実行時警告はAPIが通常値として返す。失敗値の伝播は明示tryだけで後置`?`はない。require文は制御フローで、Option・Resultの展開・伝播は行わない。

```kimi
// loadSize(path: string) -> Result<usize, FileError>
func run(path: string) -> Result<(), FileError>
    let size = try loadSize(path) // Explicit propagation.
    // Error: no postfix operator: let size = loadSize(path)?
    return .Ok(())
```

<a id="s-17-2"></a>

## 17.2. OptionとResult

Option&lt;T>は理由のない通常の不在、Result&lt;T,E>は成功または理由付き失敗を表す。Eは通常型。両者は[§22.1](#s-22-1)のKimi宣言で、再宣言しない。APIが用途に応じ選び、両方を区別するならResult&lt;Option&lt;T>,E>。Copyは通常enum規則に従い、Result&lt;i32,string>はOkでもNon-Copy。失敗値は通常制御フローで明示処理し、返却時には通常Scope Exitを行う。Result返却関数でもAbortし得る。

```kimi
// lookup(id: UserId) -> Result<Option<User>, LookupError>
match lookup(id)
    .Ok(.Some(let user)) => greet(user@move)
    .Ok(.None) => greetGuest()
    .Err(let error) => report(error@move)
```

<a id="s-17-3"></a>

## 17.3. try伝播

`try expression`は成功ペイロードを取り出し、失敗なら同位置のreturnと同じように返す。呼出し、選択、添字、@より弱く、乗除剰余より強く結合する。

正規化したオペランド型は所有Kimi Option・Resultでなければならず、名前・形でなくSymbolで識別し、ジェネリック定義でも証明する。Option&lt;ref/T>は可、ref/Option、uniq/Result、obj/Option、型Neverのオペランドは不可。成功ペイロードがNeverなのは可。

オペランドを期待型なしの値文脈で一度評価し、所有値として取得する。Copy PlaceはCopy、Non-Copy Placeは@move必須、一時値は転送する。Option&lt;T>はSomeのTを値として返し、失敗はOption&lt;U>対象へNone。Result&lt;T,E>はOkのTを返し、失敗はResult&lt;U,F>対象へErrを返してerrorを通常規則でFに合わせる。TとUの一致は不要。1層だけを展開し、正常結果はペイロードPlaceではなく値。借用ペイロードの権限・Origin・Loanを保ち、新しい借用を作らない。エラー型の暗黙結合・変換はない。

return対象と制限は同位置のmatch＋returnと同じ。関数、クロージャー、ゲッターは対象を持つが、選択、ループ、do、unsafeは持たない。return禁止位置（defer、既定値、検証メッセージ、ソース直下）とUnit対象のmain、init、set、dropでは不可。到達不能・既知Some/Okでも検査する。

ペイロード・失敗値を確保してから一時値とScope Exitを処理し、元のMoved状態も通常どおり。引数・集成値評価中の失敗は残り評価を飛ばし、取得済み部分を中断式として後処理する。

オペランドは自身のレシーバ・引数・明示型だけで解決し、tryへの期待型やreturn対象を推論・選択へ逆流させない。よって`try .None`は不可。匿名関数の暗黙失敗returnは期待型依存で結果型候補を与えず、注釈・固定期待シグネチャ・別の独立に型付けした結果から結果型を得る。成功をSome/Okで自動包装しない。ユーザー定義try、tryブロック、Option/Result相互変換はない。try接頭辞のAPI名は別で、その契約だけを保証する。

```kimi
// Inside a function returning Result<…, E>; parseCount() -> Result<i32, E>
try parseCount() + 1         // (try parseCount()) + 1
try parseCount().isEven      // Error: groups as try (parseCount().isEven); Result has no isEven.
try pending@move             // Transfer the stored Result, then extract.
```

```kimi
// Inside a function returning Result<…, ParseError>
// parse<T>() -> Result<T, ParseError>; getOpt() -> Option<i32>
let value = try parse<i64>()            // Valid in a compatible return target.
let bad: i64 = try parse()              // Error: the annotation does not infer T.
let g = func () -> Option<i32> => .Some(try getOpt())
let f = func () => try getOpt()         // Error: inferred i32 cannot return None.
func h() -> Option<i32> => try getOpt() // Error: i32 is not Option<i32>.
```

<a id="s-17-4"></a>

## 17.4. Abort終了

明示$abortと実行時検査失敗は同じ規則でプロセス全体を異常終了する。

<a id="s-17-4-1"></a>

### 17.4.1. 原因

Abortはプログラム欠陥または回復不能な外部条件を示し、原因によらず同じ制御フローとなる。検査付き整数の表現範囲超過（ラップ整数はラップ）、整数の0除算・剰余、位置・範囲解決失敗、逆向き範囲の反復開始、不正な数値変換・シフト数、Dictionaryリテラル重複キー・添字の欠落キー、禁止staticアクセス、Kimiオブジェクト生成・関数型消去の割当て失敗、強所有者数超過が対象。浮動小数点除算はIEEE 754、@wrap・@bitsは失敗しない。各操作が無効入力を定め、コードは[§22.5.2](#s-22-5-2)。型検査・検査付きキャスト失敗はbool・Option・Result契約に従う。同じ原因でも別API契約なら回復可能にできる。

```kimi
// tryAllocate(size: usize) -> Option<Buffer>
func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer@move
        .None => $abort("Required memory could not be allocated")
```

<a id="s-17-4-2"></a>

### 17.4.2. 明示Abort

`$abort(message)`は期待型stringで引数を一度評価してAbortする。引数が通常完了しなければ、その転送・発散・Abortが優先する。式型はNever。Abortは制御転送でなくCompletionを生じない。

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

<a id="s-17-4-3"></a>

### 17.4.3. 終了と後処理

Abort終了はプロセスごとに高々一度開始する。開始スレッドは通常実行に戻らずScope Exitせず全プロセスを終える。複数スレッドが開始を求めても一つだけが終了処理と報告を試み、他は戻らず報告しない。終了まで他スレッドが動くことはあり、その挙動は未規定で、終了の一部としてScope Exitすることはない。引数評価・終了の実時間上限は定めない。捕捉・回復・再開・スタック巻戻し・static終了処理はない。

Scope Exit中のAbortは後処理を直ちに止め、残るdefer・自動破棄・実行中defer/dropの残部を実行せず、完了分を巻き戻さない。保留return・exit・continue・yieldを中止し、確保結果を配送も破棄もしない。

スタック枯渇は未定義動作ではなく、Scope Exitなしにプロセスを終える。Abortとしての検出・報告、診断、終了コードは未規定。Abortは失敗終了状態を返し、理由と失敗操作または$abortの位置を報告しようとする。報告失敗は終了を妨げない。形式・出力先・コードは実装定義。

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition") // Neither close(resource) nor the destruction of resource runs.
```

<a id="s-17-4-4"></a>

### 17.4.4. ビルドとコンパイル時評価

Abort条件・挙動は全ビルド共通で、暗黙検査失敗から戻る設定はない。ラップはWrapping整数型と@wrapだけで選び、設定・文脈では変えない。飽和モードはない。ユーザー算術Contractの失敗は自身の契約。

実行時検査は実行して失敗したときだけAbortし、最適化は未実行分岐・短絡箇所でAbortさせてはならない。未定義動作の検出保証はない。必須コンパイル時評価（ディレクティブ、長さ）の失敗はコンパイルエラーで、リテラルが期待型に合うかの検査・重複キーなど静的検査も最適化によらない。ただし定数畳込みで実行時Abortをコンパイルエラーへ変えてはならない。

<a id="s-17-5"></a>

## 17.5. 警告

警告は型が期待型に合うかの判定、実行、オーバーロード選択を変えない。一つの値を一箇所で捨てるときは、意図しないUnit推論、Resultを捨てる場合、try成功値を捨てる場合、無効果値の順で最高優先の警告だけを出す。`_ = expression`はその結果の警告だけを抑止する。補間リテラルをUtf8Writer.writeへ直接渡して所有stringを先に作る警告は、この優先順と独立。

<a id="s-17-5-1"></a>

### 17.5.1. Resultとtry成功値を捨てる場合

結果を捨てる文脈でKimi Resultを捨てると、Copy可否・実行時Caseによらず警告する。別名越しもSymbolで識別し、同名ユーザー型は対象外。Option固有の警告はない。tryの結果を捨てる場合はUnit・Neverを証明できなければ警告し、Option&lt;()>の成功はUnit。結果を捨てる文脈は括弧・分岐本体へ伝わる。ジェネリックは定義で証明できなければ警告し、具体化で再検査しない。

```kimi
// prepare() -> Result<Data, E>; save() -> Result<(), E>
func run() -> Result<(), E>
    prepare()          // Warning: Result discarded.
    _ = prepare()      // No warning.
    try prepare()      // Warning: the success value Data is discarded.
    _ = try prepare()  // No warning.
    save()             // Warning: Result discarded.
    try save()         // No warning: the success value is Unit.
    return .Ok(())
```

<a id="s-17-5-2"></a>

### 17.5.2. 意図しないUnit推論

値として使う構文・結果推論する匿名関数で、結果がUnitと推論され、宣言・構文規則・期待型による固定でなく、Unitを供給する字下げ本体が末尾で非Unit・非Never値を捨てて終わる場合に警告する。名前付き関数の結果省略はUnitを固定するため対象外。検査は括弧・ラベルを越え通常完了するif・match・doへ入り、明示()、転送、反復、文、別関数で止まる。

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// Warning: total is Unit; the i32 results of both arms are discarded.
let corrected = if useCache => loadCached() else => compute()
```

<a id="s-17-5-3"></a>

### 17.5.3. 無効果値を捨てる場合

結果を捨てる文脈の非Unit式が、リテラル、Copyローカル、組込み操作・比較、Case構築、タプルだけからなり無効果と証明できれば警告する。呼出し、ユーザー比較、Move、Loan、破棄、Abort、発散は効果と数え、呼出し先本体は調べない。明示()は警告しない。

```kimi
func isAdult(age: i32) => age >= 18 // Warning: the bool is discarded.
left == right                       // Warning for initialized i32 locals.
func cleanup() => handle.close()    // No warning: a call is not assumed effect-free.
```

<a id="s-17-5-4"></a>

### 17.5.4. 必ず失敗する位置と範囲

リテラルだけの式、それへの^、省略境界だけで作る位置・範囲が、全長さで失敗するか、添字先・引渡し先の固定配列長で必ず失敗するなら警告する（-1、^0、..=^0、2..=1、^3..^5など）。判定は正確で最適化非依存。他の式の境界は判定しない。実行したときだけAbortする性質は変えず、try接頭辞操作ならNoneを返す。

```kimi
let fixed: [3 of i32] = [1, 2, 3]
let a = fixed[3]          // Warning: position 3 fails for length 3.
let c = values[2..=1]     // Warning: reversed for every length.
let d = values[1..^1]     // No warning: resolves when the length is at least 2.
let e = values.remove(^0) // Warning: ^0 is never an element position.
```

<a id="s-17-6"></a>

## 17.6. テスト検証操作

<a id="s-17-6-1"></a>

### 17.6.1. 形式と配置

`$expect(condition)`・`$require(condition)`は位置bool式一つと任意の`message:` string式を取る。単独項目で、式のオペランドにはならず、trueならUnitで完了する。テスト専用関数本体とその入れ子ローカル関数・クロージャー・deferだけで許す。テストからしか呼ばない製品ヘルパーでも不可。Option・Result展開や型の絞込みは行わない。

<a id="s-17-6-2"></a>

### 17.6.2. 評価と失敗

条件は通常の評価・短絡・所有権・Loanで一度評価し、一時値はbool条件の寿命を使う。trueなら条件一時値を処理して続行。falseなら失敗を記録し、条件一時値を処理し、メッセージがあれば一度評価してその一時値も処理した後、$expectは続行、$requireはAbort。所有権・初期化検査でもメッセージのMove・初期化は失敗経路だけで起こる。bool取得前の転送・Abort・発散は検証失敗ではなく、false取得後は何が起きても失敗記録は消えない。

$requireのAbortはKIMI_E_REQUIREで検証位置を報告し、条件・メッセージを再評価しない。テスト実行では現在Caseだけを終え、他Caseは実行・報告する。分離方法は実装定義。

```kimi
#Test
func stopsAfterFailure()
    defer => ::Kimi.Console.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Kimi.Console.writeLine("Not reached")
```

<a id="s-17-6-3"></a>

### 17.6.3. メッセージと観測値

メッセージは条件の一時値を使えず、内部のreturn・exit・continue・yieldは外部対象へ出られない。失敗報告は元の評価で得た値だけを示し、再評価・再読取り・短絡で飛ばした評価・ユーザーコード呼出しをせず、Copy・Move・Loan・寿命変化も追加しない。表示値と形式は実装定義。全ビルドで検証し、定数falseもコンパイルエラーにはしない。

<a id="s-18"></a>

# 18. モジュールと依存関係

モジュールは宣言をまとめ、他モジュールから到達できる名前を制御する。プロジェクト構文・設定・コマンド・パッケージ・ロック・リリース形式はIMPL [§20.2](IMPL.md)、[§20.3](IMPL.md)、[§20.5](IMPL.md)の実装契約。

<a id="s-18-1"></a>

## 18.1. モジュールとソース環境

Kotonohaは名前付きモジュール一つ。プロジェクトルートは宣言階層の最上位、コンパイルルートは各Kotonohaのルート宣言・自身の直接依存の参照名・予約Kimi。ソース環境はSourceDocumentの別名と検索文脈。

非修飾検索段階4、::経路、別名対象は、その文書が属するKotonohaのコンパイルルートを使い、利用側のものは使わない。解決Symbolは元Kotonohaと版を含む宣言を識別し、綴りや別名経路に依存しない。本体・型・制約は定義環境で検査し、利用側は別モジュールへ宣言を追加したり環境を再束縛したりできない。統合宣言の各断片は自身の文書の環境を保ち、生成文書も独自環境を持つ。

```kimi
// A.kimi
alias Kimi.Console
struct Config
    func show() -> () => writeLine("a")   // Valid: A.kimi opens Kimi.Console.

// B.kimi
struct Config
    func hide() -> () => writeLine("b")   // Error: B.kimi does not open Kimi.Console.
```

<a id="s-18-2"></a>

## 18.2. 別名

ソースは直接参照するKotonohaだけを参照名で指定できる。外部メンバーは修飾または別名で到達し、自動的な非修飾検索はしない。推移依存だけのKotonohaは名指しできない。

<a id="s-18-2-1"></a>

### 18.2.1. 形式と対象

`alias Path`は対象の直接メンバーを開き、`alias Name => Path`は対象自身を型名前空間の修飾名Nameにする。

別名はSourceDocument直下で通常宣言・実行項目より前に置く。選択・除外項目の両方をソース順で判定し、ルートディレクティブの対象・分岐内も直下項目に数える。入れ子別名とアクセス修飾子は禁止。

開く対象はKotonoha参照または全引数束縛済み宣言コンテナー（group、struct、enum、contract）。直接メンバーを各名前空間の検索段階5へ加え、形式パラメーター・Origin・Selfは開かない。Case・レシーバメンバーの必要修飾は[§9.5](#s-9-5)に従う。名前付き別名の対象はKotonohaかgroupだけで、具体化親配下のgroupも全型・Origin引数が束縛済みなら可。メンバーを開かず、ジェネリックパラメーター、Core、値、領域、新しい宣言同一性を作らない。

対象全体（型引数・Origin指定を含む）は自身のコンパイルルートから、ソース・既定別名を使わず解決する。先頭::は任意。i32、during staticなど組込み構文は有効。別名連鎖・循環はなく、順序は解決に影響しない。両形式ともコンテナーの配置・具体化方法を増やさず、右辺は式でなくコンテナー参照。

```kimi
// Counter.kimi declares struct Counter with `var count: i32` and `func zero() -> Counter`.
alias Counter
alias Kimi.Option<i32>
alias Output => ::Kimi.Console
alias K => Kimi
Output.writeLine("Hello")
K.Console.writeLine("Hello")
let c = zero()        // Valid: a Type function of Counter.
let n = count         // Error: an instance member needs a receiver; write c.count.
func pick() -> Option<i32>
    return Some(1)    // Error: write Option<i32>.Some(1) or .Some(1).
```

```kimi
alias K => Kimi
alias Output => K.Console          // Error: K exists only as an alias.
alias Number => i32                // Error: a Core is not a named-alias target.
alias O => Kimi.Option<i32>        // Error: an enum is not a named-alias target.
alias f => Kimi.Console.writeLine  // Error: a function is not a Container.
alias Number = i32                 // Error: there is no Type-alias syntax.
```

<a id="s-18-2-2"></a>

### 18.2.2. 意味とスコープ

別名は宣言時の元宣言同一性と引数束縛を固定し、経路置換や使用時の再推論ではない。宣言時に対象と中間修飾子のアクセス、型形成、入力条件、Origin義務を検査し、Finalityに従う。メンバーのアクセス・使用条件は使用ごとに再検査し、別名は権限・適合証拠を与えない。

別名は自身の文書だけに作用し、他文書・断片・生成文書・呼出し元へ伝播せず、再公開もしない。Project.A・::Aで指すルートメンバーを作らず、同綴りの実ルート宣言・直接参照は使える。衝突は[§9.5.1](#s-9-5-1)。開いた候補を統合するのは宣言と完全束縛が等しいときだけ。Family&lt;i32>.HelpersとFamily&lt;string>.HelpersのTokenは別候補で、通常の曖昧性となる。

```kimi
// Definitions.kimi
struct Family<T>
    public group Helpers
        public struct Token

// Use.kimi, in the same Kotonoha
alias Family<i32>.Helpers
group Use
    func make() -> Token => Token.init()   // Valid: Family<i32>.Helpers.Token.
```

<a id="s-18-2-3"></a>

### 18.2.3. 既定別名

依存・生成を含む全SourceDocumentに、グラフの予約Kimiを開く必須既定別名がある。設定で除去・対象変更できない。追加既定別名は設定したモジュールの文書だけに作用する。必須・追加をまとめて検索段階6とし、同一解決参照を重複除去する。明示別名は先行段階5なので`alias Kimi`は優先順位を変え得る。既に必須の別名を既定へ追加しても変化しない。

```kimi
// The module configures no additional default aliases.
Console.writeLine("Hello")   // Valid: the mandatory alias opens Kimi.
writeLine("Hello")           // Error: opening Kimi exposes Console, not its members.
```

<a id="s-18-3"></a>

## 18.3. Kotonoha間の名前到達可能性

本版に再公開はなく、別名も再公開ではない。外部型・要件を名指す利用側は、その元Kotonohaへの直接参照とアクセス可能なSymbol経路を要する。公開シグネチャに含まれることや推移依存の読込みは名前到達可能性を与えない。

```kimi
// Geometry depends on Math; Math depends on Units. Math.Arithmetic.scale returns Units.Meter.
alias Math.Arithmetic
alias Units.Meter         // Error: Units is not a direct dependency of Geometry.
let size = scale(2)       // Valid: no Units name is written.
```

<a id="s-18-4"></a>

## 18.4. 依存関係

<a id="s-18-4-1"></a>

### 18.4.1. 同一性と参照名

ReferenceNameは利用側ソースの直接依存名、PackageId/PackageVersionは定義モジュールと正確なリリース、SourceIdはソースパッケージの正確な内容。KotonohaはPackageIdとPackageVersionで識別し、PackageIdのないApplication・未パッケージProjectはProjectで識別する。同じPackageIdの別版は別Kotonohaで、internalアクセス・Symbol同一性は共有しない。別参照名で二版を直接参照できる。

PackageIdは小文字ASCII英字で始まり、小文字ASCII英数字の空でない区分を`.`または`-`で区切る。PackageVersionはASCII英数字で始まり、以後はASCII英数字・`.`・`-`・`+`。版比較は大小文字を区別する完全一致だけで、順序・互換性を推定しない。参照Libraryとパッケージ化Projectには両方が必要で、依存を使うApplicationには不要。

ReferenceNameは通常識別子でKimiは禁止。全名前空間のルート宣言は直接依存名・Kimiを使えず、ルート宣言同士は通常名前空間規則に従う。Kimi自身のソースでKimiは自身を指し、必須別名も自身を開く。同じ依存の複数名・経路は型やstaticを増やさない。

```kimi
// Geometry has a dependency named Math.
group Kimi          // Error: `Kimi` is reserved in the Compilation root.
    func f() -> () => ()
struct Math         // Error: Math is a dependency reference name.
```

<a id="s-18-4-2"></a>

### 18.4.2. 依存グラフ

モジュールグラフは非循環。自己同一性への参照と循環はエラーで、相互依存宣言は同じKotonohaに置く。テスト拡張を含む対象グラフ内で、ルート自身を含む各同一性は入力種別（ProjectかPackage）、内容、意味設定を一つだけ持つ。テスト依存がルート同一性へ達すれば製品ルートノードへ統合し、別入力種別・内容ならエラー。複数経路の同一ノードは統合するがProjectとPackageを相互代替しない。各モジュールは別名、既定別名、アクセス文脈、宣言、閉じた特殊化集合を保持し、グラフ全体でKimiは一つ。

```text
PackageId="example.geometry"
PackageVersion="1.0.0"
Dependencies=
  Math={ PackageId="example.math" PackageVersion="1.2.0" Project="../Math/Math.kimiproj" }
```

```kimi
// Math/Arithmetic.kimi
public group Arithmetic
    public func twice(value: i32) -> i32 => value + value

// Geometry/Measure.kimi
alias Math.Arithmetic
public group Measure
    public func doubled(value: i32) -> i32 => twice(value)
```

<a id="s-18-4-3"></a>

### 18.4.3. 解決と固定

依存はPackageId・PackageVersionを指定し、Projectまたはソースパッケージで供給する。記録はProject（.kimiproj）かPackage（.kimipkg）の一方だけを指定し、両方不可。どちらもなければPackageSourcesの明示パッケージ・ストアディレクトリから候補を取る。パスは宣言プロジェクト基準。読んだ同一性は要求と一致する必要がある。

Package依存は正確に一つのSourceIdへ解決する。候補衝突はエラーで、先勝ち・版範囲選択・不足ソース推測はしない。テスト入力は製品候補を供給しない。パッケージが記録する依存SourceIdは不変。ProjectのPackage依存はロックで固定し、明示restoreだけが固定値を変更する。

<a id="s-18-4-4"></a>

### 18.4.4. 実効環境

グラフ全体でtargetとDebugを一つ使い、全依存が対象triple文字列の完全一致で許可する必要がある。CompileTimeSettingsは定義モジュールのもので、利用側は供給できない。辺ごとの上書き、feature和集合、条件付き依存辺はない。

全グラフを利用側コンパイラーが一言語版で検査し、公開者のコンパイラーは関係しない。パッケージ化Projectの設定は自身の宣言か言語版固定の既定値だけから取り、パッケージへ記録する。

```kimi
// Math's source; Math's own CompileTimeSettings decide `fast`, whatever Geometry sets.
public group Arithmetic
    #if fast
        public func half(value: i32) -> i32 => value / 2
```

<a id="s-18-5"></a>

## 18.5. ソースパッケージ

Kotonohaはソースで配布する。一パッケージは一Library Kotonohaの製品ソースと論理名、PackageId、PackageVersion、LangVersion、Targets、CompileTimeSettings、追加既定別名、native要件、依存SourceIdを持つ。SourceIdは正確な内容を識別する。パッケージ形式は実装定義。

条件分岐を選択済みに書き換えず元ソースを格納し、利用Compilationのtarget・Debugとパッケージ自身の設定で選択する。Targetsは利用可能環境で、検証成功を保証せず、利用側ごとに検査する。配布は定義位置検索、アクセス、ジェネリック選択、所有境界を変えない。バイナリーインターフェイス、版範囲、ネットワークレジストリーは本版外。

```kimi
// Packed unchanged; a consumer building for windows selects the first arm.
#switch
    #case os == "windows"
        func lineEnd() -> string => "\r\n"
    #case _
        func lineEnd() -> string => "\n"
```

<a id="s-18-6"></a>

## 18.6. 製品入力とテスト入力

TestSourcesのファイルはプロジェクト内でもテスト専用。テスト依存はテストだけで解決し、製品に届かない。TestSources内宣言と他ソースのTest関数はテスト専用で、入れ子は外側の所属を継承する。他の未標識ヘルパーなどは製品宣言。生成宣言は入力の所属を継承し、製品生成はテスト専用入力を消費できず、生成Test関数もテスト専用。tests等のファイル・ディレクトリ名自体に意味はない。

製品の検索、候補、型、レイアウト、適合、特殊化、初期化、破棄は製品入力だけで固定し、インライン#Test編集も変えない。テストは製品宣言・依存をそのまま使う。製品コンテナーへ関数を追加できるが製品候補にはならず、重複・アクセス規則には従う。製品型のフィールド・基底・適合・固定意味、参照名、依存内容は変更不可。製品ジェネリックのテスト具体化も製品定義時検索を保つ。依存自身のテストとテスト依存は伝播しない。

```kimi
// tests/MeasureTests.kimi, listed in TestSources
group MeasureTests
    #Test
    func doubled()
        $expect(Measure.doubled(3) == 6)
```

<a id="s-19"></a>

# 19. コンパイル時ディレクティブ

ディレクティブはコンパイル中にソース構文を選び、実行時制御フローを作らない。#ifは一構文ノードを採否選択し、#switchは#case候補のCase Groupを作る。大文字始まりの#Nameは属性でありディレクティブではない。

<a id="s-19-1"></a>

## 19.1. 配置と構造

ディレクティブは所属リストの項目全体を選ぶ。ソース直下では宣言・別名・許可された実行項目、group/rootgroup/structでは許可コンテナー項目、enumではCase・関数・制約・関連型指定、contractでは要件・関連型宣言・制約、条件付き適合実装では関数・計算メンバー・関連型指定、実行本体では実行項目と関数先頭制約（Origin関係を含む）が対象。

式・パターン・型の一部は置換せず、match 分岐、パラメーター、引数、ジェネリック、アクセサー、キャプチャー、タプル・コレクション要素のリストへ直接置けない。式内の字下げ本体（クロージャーなど）には置けるが、単一項目本体には置けない。

#ifは同じ字下げの次ノード、または字下げした項目ブロックを制御する。Case Groupは#switch本体で終わり、隣接できる。#switch自体も#ifの対象となる一項目。選択項目は元順に周囲のリストへ差し込み、字下げはスコープ・寿命境界・後処理点・転送対象を作らない。選ばれたdefer・ローカルは周囲スコープに属し、後続から見える。選択・生成後に最終宣言木を検証する。

```kimi
#if debug
    let logging = true
    let assertions = true

func run() -> ()
    #switch
        #case windows
            useWindowsPath()
        #case linux
            useLinuxPath()
        #case _
            useGenericPath()
    #if debug
        defer => log("run done")   // Registers in the body of run.
    work()

#case debug                        // Error: #case outside a #switch body.
    log("misplaced")
```

```kimi
struct Box<T>
    let value: T
    Self is Describe when T is Describe
        #if debug           // Valid: directives select items of a conditional-conformance block.
            func describe(self) -> string => "Box(debug)"
        #if not debug
            func describe(self) -> string => "Box"
```

<a id="s-19-2"></a>

## 19.2. 条件

条件はboolで、通常優先順位の次の形式だけを使える。

| 形式 | 条件 |
| --- | --- |
| true・false、整数・文字列リテラル | 整数の型はi64で、値はその範囲内。符号は整数リテラル直前だけ。文字列補間なし |
| 条件名 | 組込み条件または当該モジュールのCompileTimeSettings |
| not、and、or | boolオペランド |
| ==、!= | 同じbool・i64・string型。文字列は大小文字を区別するordinal比較 |
| 括弧 | グループ化 |

is、呼出し、メンバー、添字、算術、大小比較、変換、コレクション、浮動小数点・文字・nullリテラルなど他の形式は不可。どのスコープでも型・Semantics・Contract・Origin・ジェネリック引数に依存しない。

条件名は大小文字を区別し、組込みと自身の設定だけから解決する。宣言、ジェネリック入力、別名、実行時値は供給も隠蔽もせず、制約も追加しない。Windowsとwindowsは別。モジュールの条件名と型は全Compilationで同じで、値だけが変わる。未知名は全target・build modeで未知。

```kimi
#if os == "windows" or os == "linux"
    func supported() -> bool => true

#if debug and featureEnabled          // featureEnabled: a configured bool setting.
    func trace() -> () => log("trace")

#if T is i32                          // Error: Type tests are not Conditions.
    func wide() -> () => ()
```

<a id="s-19-2-1"></a>

### 19.2.1. 組込み条件

| 名前 | 型 | 値 |
| --- | --- | --- |
| os | string | "windows"、"linux"、"macos"、"unknown" |
| arch | string | "x86_64"、"x86"、"aarch64"、"riscv64"、"unknown" |
| windows、linux、macos | bool | osと対応文字列の等値 |
| debug、release | bool | ビルドモード。release == not debug |
| pointerWidth | i64 | rawポインターのビット幅 |

os・archはtargetから導く。設定に組込み名を再使用してはならない。

```kimi
#switch
    #case os == "windows" and arch == "x86_64"
        func platformName() -> string => "win-x64"
    #case _
        func platformName() -> string => "other"
```

<a id="s-19-3"></a>

## 19.3. 評価と選択

解析前にtarget、build mode、モジュール設定が全条件入力を固定する。条件はTrue・False・Error。無効形式、未知名、オペランド型不一致、非bool結果はコンパイルエラーで、Errorはnot・and・orを通じ伝播する。両オペランドを常に検証するため、false and missing、true or (T is i32)、false and 1もエラー。除外構文、後続switch 分岐、配置違反caseも含め文書内の全条件を検証する。

除外構文外だけで選択する。#ifはTrueなら対象を選ぶ。全構造・条件が有効な#switchは最初のTrue 分岐、なければ#case _を選び、両方なければエラー。解析中に選択完了し、ジェネリック具体化・名前解決・生成による後延ばし・再選択はない。生成ソースも同じ入力を使う。選択項目の名前は後で通常スコープ規則で解決する。別target・設定には別Compilationが必要。

```kimi
func width<T>() -> i64
    #switch
        #case pointerWidth == 64
            return 64          // Selected for every T in this Compilation.
        #case _
            return 32

#if false and 1                // Error: 1 is not bool, although false decides the result.
    useFeature()
```

<a id="s-19-4"></a>

## 19.4. 除外構文

False・Errorの#if対象、#switchの非選択分岐（選択なしなら全分岐）、switch外caseの本体と、その内部全てが除外構文。内部ディレクティブは選択せず、選択エラーも報告しない。

除外にも符号化、トークン、レイアウト、文法、修飾子・属性・ディレクティブ配置、非空本体、配置規則、条件検証を全て適用する。選択、名前、型、所有権、属性解決、個数・一意性、宣言統合、文書関連付け、後段処理は選択構文だけ。

別名の先行配置、制約・Origin関係の先頭領域規則は選択前に全項目をソース順で判定する。ディレクティブ自体でなく、対象・分岐内項目をその種類で数える。重複宣言禁止・enum最低一Caseなど個数規則は選択後。非空本体はソース規則なので、全項目が除外されても元に項目があれば有効。

ソース検査は条件値を読まず、非生成文書の字句・文法・配置・条件エラーはそのモジュールの全Compilationで起こる。除外内も選択時と同じコード・主位置で診断する。除外構文は宣言、項目、制約、属性を何も供給しない。

```kimi
#if windows
    alias WindowsSupport
#if linux
    alias LinuxSupport
alias Kimi.Console          // Valid: only aliases precede it.

func process<T>(value: T) -> ()
    #if debug
        log("process")
    T is Copy               // Error in every build: follows an executable item.

#switch
    #case windows
        func platformName() -> string => "windows"
    #case _
        func platformName() -> string => "other"   // Valid: uniqueness is judged after selection.

#if windows
    #if useDirectWirte      // Error in every build: unknown Name.
        useDirectWrite()
```

<a id="s-22"></a>

# 22. Kimi、プログラム実行、外部関数

本版の実行対象プロファイルはwindows-x64のみ（IMPL [§21.4](IMPL.md)）。プログラムの適否がtargetに依存するのは条件式（[§19.2](#s-19-2)）と具体レイアウトのサイズ上限（[§8.15.3](#s-8-15-3)）だけだが、Unsupportedはtargetで変わり得る。他targetのbuild・run・testはUnsupported。

<a id="s-22-1"></a>

## 22.1. 必須Kimi宣言

各Compilationは予約直接参照名KimiにKimi Kotonohaを正確に一つ束縛する。Kimiはキーワードでなく内側で隠せるが、::Kimiは隠せない。必須既定別名がKimiを開く。

特殊な振る舞いは宣言同一性で識別し、同名ユーザー宣言・別名置換・ユーザー適合では得られない。互換Kimiは下表の全宣言を所定の形で持ち、追加は可能。コンパイラー自身以外のKimiを受け入れるかは実装定義。Windows・Timeを含む全Kimi宣言は全targetに存在する。

以下が必須宣言の唯一の一覧。名前はKimi相対。「反復入口」はIterable・UniqIterable・IntoIterableと[§14.6.2](#s-14-6-2)の項目を指す。

| 宣言 | 必須の形 |
| --- | --- |
| Never | public、非open、非ジェネリックstruct。Origin・基底・メンバーなし。組込みの値を持たない型（[§3.1.3](#s-3-1-3)） |
| Option&lt;T> | Some(T)、Noneの順のenum。TがCopyならCopy |
| Result&lt;T,E> | Ok(T)、Err(E)の順のenum。T・EがCopyならCopy |
| Equatable | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| Comparable: Equatable | `func compare(self: ref/Self, other: ref/Self) -> i32`。小・等・大を負・0・正で返す |
| Copy、Owned、Callable、Sealed、ObjectPayload、PrimitiveInteger、Transferable | [§8.7](#s-8-7)の組込みContract。Transferableはpublicでパラメーター・親・メンバーなし |
| 算術Contract | [§13.3.3](#s-13-3-3)の11種 |
| Position: Equatable, Utf8Format | 閉じたContract。Self is Copy、Self is Owned、`func tryResolve(self: Self, length: isize) -> Option<isize>`。適合型は12整数型、FromEnd&lt;T>、Start、Endだけ |
| PositionRange: Equatable, Utf8Format | 閉じたContract。Self is Copy、Self is Owned、`func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>`。適合型はRange、ClosedRange、ResolvedRangeだけ |
| FromEnd&lt;T>、Start、End | Copy・Owned・Equatableの位置。^xと省略境界だけで生成。FromEnd&lt;T>はT is PrimitiveIntegerでoffset:Tだけを格納 |
| Range&lt;S,E>、ClosedRange&lt;S,E> | S,E is PositionのCopy・Owned・Equatable区間。start:S・end:Eを格納し構文だけで生成。S is PrimitiveIntegerかつE is Sなら反復入口を持つ |
| ResolvedRange | Copy・Owned・Equatableの検証済み区間。length、isEmpty、resolve、反復入口 |
| Wrapping&lt;T> | フィールド・メンバーなし、T is PrimitiveInteger。T表現でラップ算術、[§13.5.4](#s-13-5-4)の変換、Tと[§13.3.3](#s-13-3-3)の適合を持つ。PrimitiveInteger・Positionではない |
| Array&lt;T> | Non-Copy所有列。capacity、[§4.7.3](#s-4-7-3)のメタデータ、UniqIndexable&lt;isize>、[§4.8.2](#s-4-8-2)・[§4.8.4](#s-4-8-4)・[§4.8.5](#s-4-8-5)・[§4.9](#s-4-9)の操作、反復入口 |
| Dictionary&lt;K,V> | Non-Copy所有コレクション、K is Equatable。length・capacity、既存キーへのUniqIndexable&lt;K>、[§4.9](#s-4-9)の操作、反復入口 |
| Slice&lt;T> {source} | ref/T during source上のCopy共有ビュー。[§4.7.3](#s-4-7-3)メタデータ、[§4.8.2](#s-4-8-2)操作、Indexable&lt;isize>、反復入口 |
| UniqSlice&lt;T> {source} | Non-Copy排他ビュー。length・isEmpty・indices、UniqIndexable&lt;isize>、[§4.8.4](#s-4-8-4)ビュー |
| Loan&lt;T> | 外側がref・uniq・objref・objuniqの借用型Tに対するゼロサイズstruct。init(value:T)、Copy可否はTと同じ |
| Weak&lt;s/T> | Non-Copy struct、s is rc or arc |
| LendingIterator、Iterator、Iterable、UniqIterable、IntoIterable | [§22.2.1](#s-22-2-1) |
| Indexable&lt;Key>、UniqIndexable&lt;Key>: Indexable&lt;Key> | associate Element、`index(self: ref/Self, key: ref/Key) -> ref/Element during self`、`indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self`。添字式は返却参照の参照先を選ぶ |
| 標準イテレーター | ArrayIterator&lt;T>、ArrayUniqIterator&lt;T>、ArrayOwningIterator&lt;T>、FixedArrayOwningIterator&lt;T,A>、SliceIterator&lt;T>、DictionaryIterator&lt;K,V>、DictionaryUniqIterator&lt;K,V>、DictionaryOwningIterator&lt;K,V>、RangeIterator&lt;T>、ClosedRangeIterator&lt;T> |
| Iteration | OwningIterator&lt;I>、BorrowingIterator&lt;I>、owning、borrowing |
| Intrinsics | 非ジェネリックgroup。makeObj、makeRc、makeArc、makeRcCyclic、makeArcCyclic、clone、downgrade、upgrade、replace、exchange、swap |
| Raw | allocate、release、initialize、slice |
| Async | Cancelled、SendFailure、Raced（2～4引数）、Sender&lt;T>、Receiver&lt;T>、run、sleep、checkpoint、shield、join、joinOk、race、each、eachReceived、pipe、offload、offloadEach |
| Console | writeLine |
| Time | Duration、Stopwatch |
| Windows | queryPerformanceCounter、queryPerformanceFrequency |
| UTF-8整形 | Utf8Format、BufferWriter、WriteWindow、Utf8Writer、BufferFull、Text |
| Test | tempDirectory() -> string。テスト専用本体だけ（[§17.6.1](#s-17-6-1)、IMPL [§20.6.3](IMPL.md)） |

反復・添字Contractは静的で、関連型は完全な型かつOriginパラメーターを持てる。Array、Dictionary、Slice、UniqSlice、Wrapping、Loan、Weak、NeverのコンストラクターはKimi宣言のものだけで合成しない（Weak・Wrapping・Neverは宣言なし）。全固定配列[N of T]は長さパラメーターを含む全要素型・長さで、[§4.7.3](#s-4-7-3)メタデータ、[§4.8.2](#s-4-8-2)読取り、[§4.8.4](#s-4-8-4)・[§4.8.5](#s-4-8-5)ビュー、UniqIndexable&lt;isize>と反復入口への組込み公開メンバー・適合を持ち、ソースで宣言できない。

ArrayはTが、DictionaryはK・VがOwnedならOwned。stringは組込みUTF-8文字列型で、リテラル、補間、比較、Utf8Formatを持ち、連結演算子はない。Kimiを開くとgroupだけが見え、そのメンバーは開かない。別名は宣言同一性を保つ。Kimi.replace・Kimi.writeLineのようなルート名はない。$abort・$tryWriteはどのgroupにも属さない。

```kimi
let watch = Time.Stopwatch.init()      // Time is visible through the default alias.
Console.writeLine("ready")
// writeLine("ready")                 // Error: Console's members are not opened.
var slot: i32 = 1
let old = Intrinsics.exchange(slot@uniq, with: 2)
// let bad = Kimi.exchange(slot@uniq, with: 3)   // Error: no root-level name.
```

<a id="s-22-2"></a>

## 22.2. 反復

<a id="s-22-2-1"></a>

### 22.2.1. Iterator Contract

全列挙はLendingIterator.nextを呼び、次項目を値で返す。各呼出しのstepはそのレシーバ借用へ束縛し、NoneはLoanを運ばない。LendingIteratorはNone後にSomeを返してもよいが、forは最初のNoneで止まる。

IteratorはnextのOriginを含まない完全なItem型を一つ持ち、適合側はassociate Iterator.Item is Tとfunc next(self: uniq/Self) -> Option&lt;T>を記す。IteratorでないLendingIteratorは自身を借りる項目を返せるが、ref/E during stepなどを保持すれば次のnextと競合する。stepなしの項目は通常Loan規則。

三反復入口は独立で、forはSubjectモードが選ぶ入口を要し、項目型は選んだIteratorTypeのLentItem(step)。借用入口は呼出しごとのsource、所有入口はSelf内部の依存を保つ。Iteratorと入口の両方へ適合できる。名前による適合、参照・Iteratorの自動適合、既定本体はない。標準適合は共有借用からIteratorを進めない。

```kimi
contract LendingIterator
    associate LentItem(step) for uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item

contract Iterable
    associate IteratorType(source) is LendingIterator for ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)

contract UniqIterable
    associate IteratorType(source) is LendingIterator for uniq/Self during source
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)

contract IntoIterable
    associate IteratorType is LendingIterator
    func intoIterator(self: Self) -> Self.IteratorType
```

```kimi
struct Countdown
    Self is Iterator
    associate Iterator.Item is i32
    var remaining: i32 = 3

    public func next(self: uniq/Self) -> Option<i32>
        if self.remaining == 0 => return .None
        self.remaining -= 1
        return .Some(self.remaining)

for number in Kimi.Iteration.owning(Countdown.init()) => use(number)   // 2, 1, 0
// for number in Countdown.init() => use(number)   // Error: Countdown declares no entry.
```

<a id="s-22-2-2"></a>

### 22.2.2. 標準アダプターとイテレーター

Iterationの二アダプターはLendingIterator Iを受け、結果はLendingIterator、IntoIterable（自身）、UniqIterable（BorrowingIterator&lt;Self>）に適合する。IがIteratorの場合に限り同じItem・依存でIteratorにも適合。入力の枯渇保証を保ち、割当て・項目の先読みをしない。

owning(iterator) -> OwningIterator&lt;I>はIを値で取得（Non-Copy Placeは@move）し、LentItem(step)と依存を転送する。borrowing(iterator) -> BorrowingIterator&lt;I>はuniq/Iを借りて移動せず進める。sourceは入力外側Origin、項目契約はIのまま、各stepは実際のnext再借用。

標準入口のIteratorTypeは[§22.1](#s-22-1)の10型の一つ。全てNon-Copy IteratorでItemは[§14.6.2](#s-14-6-2)どおり。RangeIterator・ClosedRangeIteratorはT is PrimitiveIntegerでOwned。公開コンストラクター・入口適合はなく、owning・borrowingで列挙する。一度Noneなら以後もNone、各要素を所定順に正確に一度返し、live排他項目を重ねず、nextでユーザーコードを呼ばない。所有型は未返却要素を所有し[§4.9.7](#s-4-9-7)順で破棄、借用型は何も破棄しない。各ステップは[§4.8.6](#s-4-8-6)の計算量境界を満たす。

```kimi
let values: Array<i32> = [1, 2]
var it = values.iterate()
for x in Kimi.Iteration.borrowing(it@uniq) => use(x)
// for x in values.iterate() => use(x)   // Error: an iterator has no entry conformance.
```

<a id="s-22-2-3"></a>

### 22.2.3. Iteratorの独立性

Iterator項目はその呼出しレシーバLoan・Iterator領域に依存しない。排他項目は権限から分割した子か転送値なので、IteratorのMove・解放で無効化せず、借用越しに得たIterator項目にもその借用Loanを残さない。従って後続nextをまたいだ保持に効果境界は不要。全実装はレシーバOrigin内に含まれるものを返さず、raw領域から貸す全項目で[§5.2.2](#s-5-2-2)を守る。

Iterator全体破棄はLoanを観測し得る破棄。未知・パラメーター依存効果は[§15.6.4](#s-15-6-4)、[§16.3.3](#s-16-3-3)、[§4.9.6](#s-4-9-6)に従う。結果のため自身を借りるラッパーはLendingIteratorにとどまる。

```kimi
func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    let first = iterator.next()
    let second = iterator.next()   // Valid: first keeps no receiver Loan.
    return (first@move, second@move)
```

<a id="s-22-3"></a>

## 22.3. 非同期タスク

Kimi.Asyncは[§24](#s-24)の非同期タスクを提供する。保留計算・実行中子・停止フレームを表すハンドル値はない。フレーム、待機元、実行器、スレッドごとの状態は実装定義だが、本節と[§24](#s-24)の全保証を満たす必要がある。実装ライブラリーはunsafeコードで、保証違反は未定義動作。

<a id="s-22-3-1"></a>

### 22.3.1. 宣言

join・joinOk・raceは子2～4個の各arityに正確に一オーバーロード、Racedも各arityに一宣言を持つ。Caseは子順にFirst、Second、Third、Fourthで、それぞれ全最終結果を保持する。5個以上は候補がなく、入れ子かeachを使う。

join・joinOk・race・pipe・shieldの一回限りの子は消費するため、Shared・Exclusive・Consuming本体を使え、let内Non-Copy子は@moveで渡す。each・eachReceivedは子を共有借用して並行呼出しするためShared本体が必要。

limitは同時実行子の上限、capacityはバッファー項目数で0はランデブー。limit &lt; 1、capacity &lt; 0はKIMI_E_ARG_RANGEでAbort。子のLoanは引数依存で、一回限りの子のLoanは操作が子を破棄するまで、さらに依存結果へ続く。排他キャプチャーの重なりは通常Loan競合。子にOwnedは不要。

```kimi
public group Async
    public func run<F, R>(root: F) -> R
        F is Callable<owner, (task;) -> R>
    public func sleep(task; duration: Time.Duration) -> Result<(), Cancelled>
    public func checkpoint(task;) -> Result<(), Cancelled>
    public func shield<F, R>(task; body: F ! grace: Time.Duration) -> R
        F is Callable<owner, (task;) -> R>
    public func join<A, B, RA, RB>(task; first: A, second: B) -> (RA, RB)
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>
    public func joinOk<A, B, TA, TB, E>(task; first: A, second: B) -> Result<(TA, TB), E>
        A is Callable<owner, (task;) -> Result<TA, E>>
        B is Callable<owner, (task;) -> Result<TB, E>>
    public func race<A, B, RA, RB>(task; first: A, second: B) -> Raced<RA, RB>
        A is Callable<owner, (task;) -> RA>
        B is Callable<owner, (task;) -> RB>
    public func each<I, F>(task; items: I, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        I is Iterator
        F is Callable<(task; I.Item) -> ()>
    public func eachReceived<T, F>(task; items: uniq/Receiver<T>, child: ref/F ! limit: isize)
        -> Result<(), Cancelled>
        F is Callable<(task; T) -> ()>
    public func pipe<P, C, T, R>(task; producer: P, consumer: C ! capacity: isize) -> R
        P is Callable<owner, (task; uniq/Sender<T>) -> ()>
        C is Callable<owner, (task; uniq/Receiver<T>) -> R>

    public struct Cancelled                 // Fieldless; only Kimi creates it.
        Self is Copy
        internal init() => ()
    public enum SendFailure
        Closed
        Cancelled(Async.Cancelled)
    public enum Raced<A, B>                 // The Case names the winner; every final result is kept.
        First(A, B)
        Second(A, B)
    public struct Sender<T>
        public func send(task; self: uniq/Self, value: T) -> Result<(), (T, SendFailure)>
    public struct Receiver<T>
        public func receive(task; self: uniq/Self) -> Result<Option<T>, Cancelled>
```

```kimi
func loadAll(task;) -> (Config, Users, Rules)
    return Async.join(task; loadConfig, loadUsers, loadRules)   // Three children.
// Async.join(task; f1, f2, f3, f4, f5)   // Error: no overload for five children.
```

<a id="s-22-3-2"></a>

### 22.3.2. 実行と構造化操作

Async.run(root)は新タスク木のrootを開始し、新タスク木の全タスクを現在スレッドで実行する。木の全タスク完了後にroot結果を返す。タスクのないコードからタスク操作を呼ぶ唯一の方法。入れ子run中は外側木のタスクを動かさず、内側が外側を待つと進行不能になる。進行不能木は発散し、検出は任意診断でAbortではない。子はrcをキャプチャーでき、非Owned値をOrigin終了前に破棄する保証はない。タスクを渡すユーザー関数は同じタスクの一部。

構造化操作は全子完了後だけ戻る。**決定イベント**で実行中の子へ取消しを要求し得る。

| 操作 | 決定イベント・処理 | 結果 |
| --- | --- | --- |
| join | 最後の完了 | 全結果 |
| joinOk | 最初のErr、なければ最後のOk。残りを取り消す | 最初のErrまたは全結果のOk。返さない結果を破棄 |
| race | 最初の完了。残りを取り消す | 全最終結果を持つRaced |
| each | 枯渇と最後の子完了 | Ok(())。要求で取得を止め、全join後はErr |
| eachReceived | 受信側close・排出完了と最後の子完了 | 同上。要求で受信を止める |
| pipe | consumer完了。receiverをcloseしproducerを取り消す | consumer結果 |
| shield | 本体完了 | 本体結果 |

join・joinOk・raceは引数順に子を開始し、各子が最初に一時停止・完了してから次を開始する。決定後に開始する子には最初から要求を適用する。実行可能なタスクの再開順は未規定。

eachは自身のフレームだけで、実行子数がlimit未満の場合だけnextする。取得前に適用要求を調べ、あれば以後取得しない。この検査は待機でもcommit点でもない。項目順に一子ずつ開始し、最初のNoneで止め、最後の子完了後にIteratorを破棄する。eachReceivedもlimit未満でだけ受信し、取得項目の子を開始してから次の受信をする。

```kimi
func pick(task;) -> i32
    let a = func [] (task;) -> i32 => 1
    let b = func [] (task;) -> i32 => 2
    match Async.race(task; a, b)
        .First(let x, _) => return x     // Always First: a completes before b starts.
        .Second(_, let y) => return y
```

<a id="s-22-3-3"></a>

### 22.3.3. 取消しと待機

取消しを要求できるのは自身が開始した子に対するKimi操作だけで、要求を表す値はない。要求はそのタスクの部分木にある現在・将来の**取消しを観測する待機**（sleep、checkpoint、I/O、send、receive）へ適用する。各待機は操作成立か取消しの早い方で正確に一度**commit**し、その結果を返す。取消しはErr(Async.Cancelled)、sendならErr((value,.Cancelled(reason)))、または領域エラーのCancelled Case。

開始時に要求があれば呼出し時に取消しでcommit。保留待機への要求もI/O以外は直ちにcommitする。commitは最終で後の要求・close・相手側操作では変わらず、一木内では全順序を持つ。sendの値転送はOk commit時だけで他はErrで返す。receiveの取り出しはOk(Some(item)) commit時だけ。取消しはフレームを割り込まず、巻き戻さず、破棄しない。

| 待機 | 操作によるcommit点 |
| --- | --- |
| send | 値を手放すとOk、受信側closeで値＋ClosedのErr |
| receive | 項目を受けるとOk(Some)、close済みかつ空ならOk(None) |
| sleep | 実行器の再開時に締切経過を確認 |
| checkpoint | yieldしないなら呼出し時、するなら再開時 |
| I/O | 即時完了なら呼出し時、他は完了通知受信時 |

必須I/O APIはない。実装が提供するI/O操作はタスクスロットを持つ取消し観測待機。保留I/Oへの要求はcommitせずプラットフォームへ取消しを依頼する。完了通知が「データ転送前に取消し」と報告すれば取消し、それ以外は報告結果でcommit。取り消したI/Oも完了通知を受けるまで戻らない。通知配送法は実装定義。

checkpointは保留要求を報告し、他タスクが実行可能なときまたは実装定義の呼出し回数後だけyieldし、時計を読まない。sleep(task;d)は呼出し後d未満ではOkを返さず、登録に割当てしない。sleep・shieldはStopwatchと同じ単調時計を使い、締切と入れ子grace合計を正確に扱う。大DurationでAbortせず、時計範囲外の締切には到達しない。

shieldは他タスクを動かしつつbodyを子として実行する。呼出し元への要求は、要求時と呼出し開始の遅い方からgrace経過後だけ子部分木へ届く。子内部要求は通常どおりで、呼出し元要求は次の観測待機まで残る。入れ子shieldのgraceは加算する。

子を実行するrun・shield・join・joinOk・race・each・eachReceived・pipe・offload・offloadEachは、停止後を含む全子のCallable入力呼出し効果をパラメーター相対で要約に含む。その効果、eachのIterator操作・破棄、所有値の破棄以外に、sleep・checkpoint・shield・join・joinOk・race・each・eachReceived・pipe・send・receiveは環境効果がなくconfinedで使える。runはスレッド別実行器状態にアクセスする環境効果で、confinedではない。I/O待機も環境効果。

Cancelledから領域エラーへの変換は明示。timeoutはsleepとのraceで、他子は観測待機でしか終了しない。deferはタスクを渡せないため、待つ終了処理は明示呼出しとし、取消し下でも動かすならshieldを使う。

```kimi
func flushAlways(task; log: uniq/Log) -> Result<(), LogError>
    let finish = func [log] (task;) -> Result<(), LogError> => log.flush(task;)
    let grace = Time.Duration.init(microseconds: 2000000)
    return Async.shield(task; finish@move, grace: grace)   // Up to 2 s more under cancellation.
```

<a id="s-22-3-4"></a>

### 22.3.4. チャネル

チャネル生成はpipeだけで、各子へ一端点をuniqで貸す。capacity >= 1なら定常状態のバッファリングにヒープ割当てしない。consumer完了時はreceiverをcloseし、producerを取り消してjoinする。保留sendはClosedへcommit。producerが先に戻ればsenderをcloseし、receiveは残項目を返した後、以後常にOk(None)。sendのOkはチャネルが値を受け取った意味で、consumerの受信済みを意味しない。配送項目はreceiver借用・チャネル領域に依存しない。端点の実行が切り替わるのはタスク呼出しだけ。

配送順は成功sendのcommit順FIFO。pipe返却前に残項目を成功send commit逆順、各々完全に破棄する。再開順には依存しない。切離しタスクはなく、動的処理は(task;) -> ()の消去ジョブをeachReceivedで扱う。ジェネレーターはcapacity 0のpipe。

```kimi
func sumEvens(task; limit: u64) -> Result<u64, Async.Cancelled>
    let produce = func [limit] (task; out: uniq/Async.Sender<u64>) -> ()
        var n: u64 = 0
        while n < limit
            match out.send(task; n)
                .Ok(_) => ()
                .Err(_) => exit                 // The consumer finished, or cancellation.
            n += 2
    let consume = func [] (task; inbox: uniq/Async.Receiver<u64>) -> Result<u64, Async.Cancelled>
        var total: u64 = 0
        loop
            match try inbox.receive(task;)
                .Some(let value) => total += value
                .None => return .Ok(total)
    return Async.pipe(task; produce, consume, capacity: 0)
```

<a id="s-22-3-5"></a>

### 22.3.5. 列とブロッキング呼出し

非同期列はloop内のタスク呼出し反復で読む。nextにタスクスロットがないためforは変えない。Async.run以外で待機し得るKimi操作は全てタスクスロットを持ち、タスクなし版はない。ただしConsole.writeLineはスレッドをブロックし、その間他タスクを動かさない。

<a id="s-22-3-6"></a>

### 22.3.6. 構造化ワーカーへの移譲

他スレッドでの計算は下記操作だけ。

ワーカー子はタスクスロットを持たず、タスク実行・取消し観測・巻戻しをしない。timeoutでも横取りできない。offloadは子を値で取得。offloadEachは子を共有借用し項目を値で渡し、Iteratorは親スレッドに残し、他はeachと同じ。

両操作は子・引数・Loanをワーカー終了まで保ち、全経路でjoinしてから戻る。途中まで開始した操作もliveな子を残して戻らない。ワーカー開始失敗はKIMI_E_THREADでAbort。

confinedは可変static、外部呼出し、Consoleなど環境権限を除外し、子と推移呼出し先が読む不変staticも[§8.11.2](#s-8-11-2)の許可に従う。入力static Loanは別に検査し、Transferableはスレッド安全でない入力を除外する。結果にTransferable・Ownedは不要でjoin後に配送する。ワーカー内Abortも通常の全プロセス終了。

```kimi
public group Async
    public func offload<F, R>(task; child: F) -> R
        F is Callable<owner, () -> R>
            effect confined
        F is Transferable
    public func offloadEach<I, F>(task; items: I, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        I is Iterator
        I.Item is Transferable
        F is Callable<(I.Item) -> ()>
            effect confined
        F is Transferable
```

<a id="s-22-3-7"></a>

### 22.3.7. データ競合

アクセスが重なるPlaceまたは同一オブジェクト参照カウントに触れ、一方以上が書込みで、少なくとも一方が実装のatomicアクセスではないとき**競合**する。Take、破棄、配置は書込み、再配置は元Placeにも書込み、解放は領域内全Placeへの書込み。別スレッドの競合アクセスをhappens-beforeが順序付けない場合は**データ競合**。happens-beforeはスレッド内順序と、次の辺の推移閉包。

- 親が子開始前に行った全て → ワーカー子のアクセス。
- 子値の破棄・結果構築を含むワーカーの全処理 → join後の親の継続。
- Initialized staticスロットの公開 → それを観測する全アクセス。

arcカウントプロトコルはハンドル状態・寿命だけを順序付け、ペイロードアクセスは順序付けない。データ競合は未定義動作だが、Loan検査とTransferableにより安全コードからは発生しない。Loan自体は実行時順序を作らず、競合アクセスを禁止するだけ。他のスレッド機能は本版外。

<a id="s-22-4"></a>

## 22.4. 起動と静的記憶領域

<a id="s-22-4-1"></a>

### 22.4.1. 起動形式の選択

条件選択・生成後の現在Projectルートで、Applicationは直下実行項目を持つ一文書、または一つのルートpublic func main() -> ()の、正確に一形式を持つ。混在・複数・なしはエラー。実行項目は式、制御式、unsafe、defer、require、結果を捨てる文、初期化子なしも含むローカルlet/var。宣言・別名・除外構文は数えず、生成項目は生成後の種類で数える。最小空Applicationは()。

選択文書の実行項目を文書実行スコープで元順に一度実行する。直下束縛は文書ローカルでstaticプロパティではなく、本体はreturn対象を持たない。

<a id="s-22-4-2"></a>

### 22.4.2. 明示mainとLibrary

明示mainはルートのpublic func main()。本体付きsafe関数、結果Unit（省略可）、task、入力、レシーバ、ジェネリック・Originパラメーターなしで、import・特殊化ではない。Applicationの全ルートpublic mainをこの形で検査し、違えばエラー。publicはnativeシンボルをexportしない。通常完了は成功終了、Abortは[§17.4.3](#s-17-4-3)。

Libraryは起動形式不要でmainを自動実行しない。mainは普通の関数で、直下実行項目は禁止。

```kimi
public func main() -> ()
    let message = "Hello, world!"
    Console.writeLine(message)          // message is borrowed and stays usable.
// Console.writeLine("Top level")       // Error: mixes the two startup forms.
```

<a id="s-22-4-3"></a>

### 22.4.3. staticの初期化と終了

static格納プロパティはスロットごとに初回アクセスで初期化する。起動は一スレッドで、他スレッドは構造化ワーカーだけ。全読取り・借用・書込み・その他アクセスで次の状態を検査する。

| 状態 | 動作 |
| --- | --- |
| Not started | 終了処理前はアクセススレッドの一つだけがInitializingにして初期化子を実行し、通常完了でInitializedにしてからアクセス。終了処理中ならKIMI_E_STATIC_SHUTDOWN |
| Initializing | 初期化スレッド自身ならKIMI_E_STATIC_CYCLE。他スレッドはInitializedまで待つ。スレッド間待機循環はデッドロックし得る |
| Initialized | アクセスを実行 |
| Finished | KIMI_E_STATIC_SHUTDOWN |

初回書込みも先に初期化してから置換する。実行したアクセスだけが初期化し、型・関数参照、未実行分岐、効果要約は開始しない。未使用初期化子も検査するが、未初期化スロットは破棄しない。依存を含む全Kotonohaに適用する。

起動本体とローカル後処理後、Initialized static varを全て、次にInitialized static letを全て破棄する。各段階はInitializedになった瞬間の全スレッド共通順の逆で、値を破棄する前にFinishedにする。let破棄から別letへアクセスするなら、相手が先に初期化済み（後で破棄）なら成功、それ以外はKIMI_E_STATIC_SHUTDOWN。実行時初期化順に依存する。終了処理中Abortも通常規則。

```kimi
group Registry
    public let names: Names = Names.init()
    public let probe: Probe = Probe.init()   // Probe's drop reads Registry.names.
// Initialized names, then probe: probe is destroyed first and its drop succeeds.
// Initialized probe, then names: names is Finished first, so probe's drop Aborts.
```

<a id="s-22-4-4"></a>

### 22.4.4. static領域キー

group直下のフィールドは、groupがstruct内でもstatic。キーはフィールド宣言と、Originを消去した外側型引数。Semantics層・未使用引数もキーを区別する。Loan・効果要約では同一フィールド全キーを一ルートと扱う。各キーは経路によらず一スロット・一初期化状態・一破棄を持つ。

格納値はOwnedとstatic条件を要するが外側型引数はOwned不要。アクセス時は全Originを保持して検査する。初期化子・破棄は同キーを共有する全Origin束縛で有効でなければ宣言エラー。

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
// Cache<i32>.Statistics.count and Cache<string>.Statistics.count are separate slots.
// Cache<ref/i32 during a> and Cache<ref/i32 during b> share one slot.
```

<a id="s-22-5"></a>

## 22.5. Abortの位置とコード

終了契約は[§17.4.3](#s-17-4-3)、windows-x64出力形式はIMPL [§21.4.2](IMPL.md)。

<a id="s-22-5-1"></a>

### 22.5.1. Abort位置

最適化段階・具体化によらず次の一位置を報告する。

- ユーザーが呼出し・添字・スライス・リテラル等で呼ぶKimi操作の内部失敗（内部コード、割当て、staticアクセス、$abortを含む）：呼出し側式の先頭。関数値経由なら値形成位置でなく呼出し位置。Dictionaryリテラル重複は後のキー式。
- Kimiが呼ぶユーザーequals・drop等：ユーザーコード自身の位置。
- forの暗黙反復入口・next：for式の先頭。
- ユーザー算術・シフト・変換検査：失敗式の先頭。複合代入・増減は更新式全体。
- ユーザーstaticアクセス：修飾子を含むPlace式の先頭。
- ユーザー$abort：その式。
- 関数値消去の割当て：変換式。

```kimi
group Values
    public let first: i64 = Values.second + 1
    public let second: i64 = Values.first + 1   // Reading Values.first finds it Initializing: KIMI_E_STATIC_CYCLE, reported at `Values`.
let value = Values.first
```

```kimi
let items: Array<i32> = [1]
let x = items[5]          // KIMI_E_INDEX_BOUNDS at the start of items[5], not inside Kimi code.
```

<a id="s-22-5-2"></a>

### 22.5.2. Abortコード

全Abort原因は次の閉じた表の正確に一行へ対応する。コード後の英語は規定のreasonなので保持する。

| コードとreason | 原因 |
| --- | --- |
| `KIMI_E_INT_OVERFLOW: Integer overflow` | 検査付き整数結果の表現範囲超過。符号付き最小値 / -1を含む |
| `KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero` | ラップ型を含む整数/・%の0除算 |
| `KIMI_E_INT_SHIFT_COUNT: Shift count out of range` | 0 &lt;= count &lt; bit width外のシフト数 |
| `KIMI_E_INT_CONVERSION: Integer conversion out of range` | 整数変換範囲外、NaN・無限を含む浮動→整数失敗 |
| `KIMI_E_FLOAT_CONVERSION: Floating conversion out of range` | 有限値が浮動無限大へ丸められる |
| `KIMI_E_INDEX_BOUNDS: Index out of bounds` | 添字、スライス、resolve、insert/remove位置を含む位置・範囲解決失敗 |
| `KIMI_E_MISSING_KEY: Dictionary key was not found` | 欠落キーのDictionary添字 |
| `KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key` | リテラルの実行時重複キー |
| `KIMI_E_ARG_RANGE: Argument out of range` | 負の個数・容量・長さ、limit &lt; 1、capacity &lt; 0、逆向き範囲反復 |
| `KIMI_E_FORMAT: Formatting failed` | 整形失敗 |
| `KIMI_E_REF_COUNT: Reference count limit exceeded` | clone・downgrade・upgradeで強/弱カウントが最大 |
| `KIMI_E_STATIC_CYCLE: Static initialization cycle` | 自スレッドが初期化中のスロットへアクセス |
| `KIMI_E_STATIC_SHUTDOWN: Static storage unavailable during shutdown` | Finishedへのアクセス、終了処理中の初回アクセス |
| `KIMI_E_ALLOC_SIZE: Allocation size exceeds limit` | 要求サイズのオーバーフロー・最大オブジェクトサイズ超過 |
| `KIMI_E_ALLOC: Failed to allocate memory` | アロケーターの割当て失敗 |
| `KIMI_E_FREE: Failed to free memory` | 検出された解放失敗 |
| `KIMI_E_PROCESS_HEAP: Failed to obtain process heap` | 実行時ヒープが利用不可 |
| `KIMI_E_STDOUT: Failed to write to stdout` | 標準出力失敗 |
| `KIMI_E_STRING_RELEASE: Invalid string release kind` | 破棄中に検出した無効文字列表現（検出保証なし） |
| `KIMI_E_CLOCK: Elapsed-time counter failed` | 時間カウンター失敗、非正周波数、逆行 |
| `KIMI_E_THREAD: Failed to start worker` | ワーカー開始失敗 |
| `KIMI_E_REQUIRE: <reason>` | falseの$require。評価済みmessageをそのまま、または省略時`Test requirement failed` |
| `KIMI_E_ABORT: <message>` | 明示$abort。messageをそのまま |

<a id="s-22-6"></a>

## 22.6. 外部関数のimport

本体なしfuncへの`#LibraryImport("library", "symbol")`はsafe・unsafeのC関数を宣言する。両引数は非空・補間なし・NULなし文字列リテラル。libraryは定義Kotonohaの大小文字を区別する論理nativeライブラリー名でbuild時に解決し、symbolは正確な外部シンボル。

配置はgroup/rootgroup直下またはレシーバなしstruct型関数。task、レシーバ、既定引数、可変長引数、特殊化、本体は禁止。自身・外側コンテナーに未使用を含む型・Semantics・長さ・Originのジェネリック入力があってはならない。ただし自身の借用注釈で導入するOriginは許し、raw層がそれを安全契約として記せる。全引数必須で、直接呼出しだけを許し関数値化しない。

宣言は、シグネチャと`- safety:`を満たす全呼出しで、同シグネチャのKimigayo関数同様に振る舞うと約束する。呼出し中だけ、引数の許す範囲へrefなら読取り、uniqなら排他、rawならsafety内のアクセスをし、有効値だけを格納する。構築・上書きする型は再帰的にユーザーdropなし（再配置は転送であり両者ではない）。借用を結果Originを越えて保持せず、有効結果を返し、巻戻し、非局所jump、コールバック、再入をKimigayoフレームへ越境させない。虚偽は未定義動作。型で表せない呼出し側義務がある場合だけunsafeにする。

効果はシグネチャの許すアクセスと環境効果。引数は左から一度ずつ評価し、マーシャリング、保持、所有権転送なしで渡す。浮動境界はIMPL [§21.2](IMPL.md)。

| 型 | C側 |
| --- | --- |
| i8～i64、u8～u64、f32、f64 | int8_t～int64_t、uint8_t～uint64_t、float、double |
| raw/T | データポインター。TのC交換可能性は不要 |
| ref/T、uniq/T | 非null const T *、T *。TはC交換可能 |
| Option&lt;ref/T>、Option&lt;uniq/T> | ポインター、Noneはnull |
| Unit（結果だけ） | void |

C交換可能型は固定幅整数、f32/f64、raw/T、C交換可能参照先のref/T・uniq/TとそのOption、正長の固定配列（要素もC交換可能）、全フィールドがC交換可能なCレイアウトstruct。string、動的コレクション、オブジェクトハンドル、関数・クロージャー値、それらの借用などC対応なしの入力・結果はエラー。表外の他型（bool、char、isize/usize、i128/u128、値渡しタプル・enum・struct・固定配列、非C交換可能参照先の借用）はUnsupported。

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public func observe(record: uniq/NativeRecord) -> ()     // C: void observe_record(NativeRecord *);

    #LibraryImport("observer", "observe_raw")
    public unsafe func observeRaw(record: raw/NativeRecord) -> ()   // - safety: states layout, lifetime, retention.

    // #LibraryImport("observer", "print")
    // public func print(text: ref/string) -> ()   // Error: string has no C counterpart.
```

<a id="s-22-7"></a>

## 22.7. Console出力

Consoleはsafe・非ジェネリックの`writeLine(text: ref/string) -> ()`と`writeLine(text: Text.Utf8Slice) -> ()`を持つ。文字列・ビューに応じ選択し暗黙変換しない。期待型なしの関数参照は曖昧。taskスロットはなく同期呼出し。

呼出し中だけ借りるためstring Placeは使い続けられ、リテラル・補間は借用一時値となる。全UTF-8バイトとLF一つを標準出力へ書き、正規化・改行変換・符号化変換をせずNULもデータ。hostがバイトを受け入れたら戻り、表示・永続化は保証しない。失敗はKIMI_E_STDOUTで、部分出力が残り得る。呼出しは何も破棄しない。

```kimi
Console.writeLine("Hello, world!")       // Writes "Hello, world!" and LF; exits successfully.
::Kimi.Console.writeLine("Hello")
// writeLine("Hello")                    // Error: Console is not opened.
```

<a id="s-22-8"></a>

## 22.8. 時間

Timeは単調経過時間を測り、日時時計の調整を受けない。時計源・分解能は実装定義で、時間操作は割当てしない。

<a id="s-22-8-1"></a>

### 22.8.1. Duration

Durationは整数マイクロ秒のCopy区間で、日時・タイムスタンプではない。`init(! microseconds: u64)`は全u64を正確に表し、rawMicroseconds:u64で読む。seconds・milliseconds・microseconds:f64はrawMicroseconds@f64を1,000,000・1,000・1で割った値。

<a id="s-22-8-2"></a>

### 22.8.2. Stopwatch

StopwatchはNon-Copyで、累積整数マイクロ秒と実行中区間の開始を保持し、読取り専用isRunning:boolを持つ。

| 宣言 | 動作 |
| --- | --- |
| init()、reset(self: uniq/Self) -> () | 0、停止。時計を読まない |
| start(self: uniq/Self) -> () | 停止中なら新しい区間開始、実行中なら何もしない |
| stop(self: uniq/Self) -> () | 現区間を加算し停止、停止中なら何もしない |
| restart(self: uniq/Self) -> () | 0にして新しい区間を開始 |
| elapsed(self: ref/Self) -> Duration | 累積＋実行中区間。変更せず、停止中は時計を読まない |

差d・周波数fの区間は正確にfloor(d × 1,000,000 / f)マイクロ秒を加え、結果がu64に収まるなら中間オーバーフローを起こさない。stopは端数を捨て区間間へ持ち越さない。累積u64超過はKIMI_E_INT_OVERFLOW、時計失敗はKIMI_E_CLOCK。

```kimi
var watch = Time.Stopwatch.init()
watch.start()
// Measured work.
watch.stop()
let seconds: f64 = watch.elapsed().seconds
```

<a id="s-22-8-3"></a>

### 22.8.3. Windowsカウンター関数

Windowsの以下は通常importで全targetに宣言があるが、windows-x64のbuild時だけ解決する。nativeフラグ（非0は成功）を返し、借用i64へカウントまたは毎秒カウント数を書く。safeで割当てせず、Abortしない。

```kimi
public group Windows
    #LibraryImport("kernel32", "QueryPerformanceCounter")
    public func queryPerformanceCounter(value: uniq/i64) -> i32

    #LibraryImport("kernel32", "QueryPerformanceFrequency")
    public func queryPerformanceFrequency(value: uniq/i64) -> i32
```

<a id="s-22-9"></a>

## 22.9. UTF-8テキストとバッファー

本節と[§22.10](#s-22-10)の長さ・容量はisizeのバイト数。

<a id="s-22-9-1"></a>

### 22.9.1. 宣言

BufferWriter・Utf8Formatはユーザー実装可能な通常静的Contract。整形ContractはUtf8Formatだけで、オプション・locale・実行時format文字列はない。reserve結果Originはselfへ省略補完する。Utf8Writerの外側借用とtargetは独立入力Originで、targetは外側借用以上の寿命を持つ必要がある。

BufferFull（Kimi）とText.InvalidUtf8は状態なしCopy structで公開init()を持つ。WriteWindow {source}、Utf8Writer {target}、Text.FixedBuffer {source}はNon-Copyで対応するLoan&lt;uniq/u8 during source/target>フィールドを持ち、ObjectPayloadではない。HeapBufferは伸長可能領域を所有するNon-Copy、Utf8Slice {source}は有効UTF-8のCopyビューでLoan要件ref。

WriteWindow・Utf8Writer・FixedBuffer・HeapBuffer・Utf8Sliceに公開コンストラクターはなく、[§22.9.2](#s-22-9-2)・[§22.9.4](#s-22-9-4)の操作だけで得る。Loanを形成するのはText.fixed・Text.writer・reserveだけ。window・view・adapterは割当てしない。

reserveにはconfined境界がある。Utf8WriterはWriter型を消去するためformatはWriter型にジェネリックではない。adapterはreserveだけを呼び、Writerを所有・Move・公開せず、WriterはLoanを保つ。消去reserve呼出しはconfined境界で検査する。

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        effect confined

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

<a id="s-22-9-2"></a>

### 22.9.2. Text操作

fixedはInitialized配列を借りて長さ0・容量Nとする。heapは長さ0・少なくとも要求容量で、0なら割当てなし、負ならKIMI_E_ARG_RANGE。writerは失敗記録なしのadapterを作る。utf8は文字列バイトをビュー化し、validateUtf8は一度検証する。両者とも割当て・Copyしない。

toStringは新stringへ一度整形し、BufferFullならKIMI_E_FORMAT。stringをCopyする方法でもある。tryFormatは事前走査・再試行せず配列へ一度整形する。結果はdestinationの排他Loanを保ち、生きている間はビュー経由でだけ配列を読める。BufferFull時は部分バイトが残り得る。

```kimi
group Text
    public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer
    public func heap(capacity: isize) -> HeapBuffer
    public func writer<W>(destination: uniq/W) -> Utf8Writer
        W is BufferWriter
    public func utf8(text: ref/string) -> Utf8Slice
    public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice during bytes.source, InvalidUtf8>
    public func toString<T>(value: ref/T) -> string
        T is Utf8Format
    public func tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8])
        -> Result<Utf8Slice during destination, BufferFull>
        T is Utf8Format
```

```kimi
var bytes: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, bytes@uniq)
// let first = bytes[0]  // Error: result still holds the exclusive Loan.
match result@move
    .Ok(let text) => Console.writeLine(text)
    .Err(_) => Console.writeLine("too small")
```

<a id="s-22-9-3"></a>

### 22.9.3. バッファーと検証

FixedBuffer・HeapBufferはBufferWriter。確定length、capacity（ゲッター）、検証済み長さを持ち、0 &lt;= validated &lt;= length &lt;= capacity。確定バイトは初期化済みで、先頭validatedバイトは有効UTF-8。

| 操作 | レシーバ | 動作 |
| --- | --- | --- |
| bytes() | ref/Self | 確定バイトのSlice&lt;u8> |
| text() | ref/Self | Result&lt;Utf8Slice,InvalidUtf8>。未検証接尾部を検証し記録は変えない |
| validate() | uniq/Self | Result&lt;(),InvalidUtf8>。全成功時だけ検証済み長さを記録 |
| clear() | uniq/Self | 両長さを0にし容量は保持 |
| intoText()（FixedBuffer） | owner/Self | Result&lt;Utf8Slice during self.source,InvalidUtf8>。接尾部を検証 |
| intoString()（HeapBuffer） | owner/Self | Result&lt;string,InvalidUtf8>。接尾部を検証し領域をCopyなしで転送 |

intoTextは元配列の排他Loanを保ち、呼出し元配列のビューは返せるがローカル配列を脱出させない。raw commitはvalidatedを変えず、adapter commitは予約時validated == lengthの場合だけ進める。adapterだけで書けば後の走査は不要。

検証はUnicode scalarのUTF-8だけを受理し、途中切れ、過長、surrogate、範囲外を置換・正規化なしで拒否する。NULはデータ。Utf8Sliceはlengthとbytes() -> Slice&lt;u8> during self.sourceを持つ。FixedBuffer・WriteWindow・Utf8Writerはdropなし、HeapBufferは領域を一度解放する。

<a id="s-22-9-4"></a>

### 22.9.4. 予約とwindow

reserve(minimum)成功はwritten == 0、remaining >= minimumのwindowを返し、確定済みバイトは変えない。minimum負はKIMI_E_ARG_RANGE。FixedBufferは残容量不足の場合に限りBufferFull。HeapBufferはBufferFullを返さず、length + minimumが最大オブジェクトサイズ超過ならKIMI_E_ALLOC_SIZE、容量超過ならmax(length + minimum, 2 * capacity)へ拡張して確定バイトをCopyする。倍増が最大超過ならlength + minimumを使う。

| window操作 | 動作 |
| --- | --- |
| written、remaining | 書込バイト数、残容量 |
| push(byte:u8)、append(bytes:Slice&lt;u8>) | Result&lt;(),BufferFull>。全体が収まるか、何も変更しない |
| limit(maximum:isize)（owner/Self） | 容量min(capacity,maximum)のwindow。maximum &lt; writtenはKIMI_E_ARG_RANGE |
| commit()（owner/Self） | 書込み済み接頭部を確定し長さを返す |

windowは先頭から連続して書き、未commit破棄は何も変えない。ユーザーWriterは標準bufferのwindowを転送・制限する。adapterは各ユーザーwindowのwritten == 0とremaining >= minimumを検査し、違反なら破棄してBufferFullを記録する。

```kimi
struct Limited
    Self is BufferWriter
    var inner: Text.HeapBuffer
    let maximum: isize

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        let remaining = self.maximum - self.inner.length
        if minimum > remaining => return .Err(BufferFull.init())
        let window = try self.inner.reserve(minimum)
        return .Ok(window@move.limit(remaining))
```

<a id="s-22-10"></a>

## 22.10. 整形と補間

<a id="s-22-10-1"></a>

### 22.10.1. Writerアダプター

Utf8WriterはT is Utf8Formatの`write<T>(self: uniq/Self, value: ref/T) -> Result<(),BufferFull>`と、記録失敗を返す`status(self: ref/Self) -> Result<(),BufferFull>`を持つ。補間・Text操作を含む全adapter writeは失敗記録があればformatせず返す。なければformatを一度呼び、BufferFull返却または入れ子writeの失敗を最初の失敗として記録・返却するため、formatterは失敗を隠せない。resetはない。失敗後はadapter使用を終え、必要ならbuffer.clearして新adapterを作る。確定出力は巻き戻さない。直接formatにはこの保護がない。writeへ補間リテラルを渡すと先に所有stringを作る。

formatは入力を借り、出力はその借用にもWriterにも依存しない。成功出力は完全表現で、値と外部状態が同じなら宛先によらず同じ。Writerは短縮せずBufferFullを返す。ユーザー整形は効果を持てるが一度実行する。組込み整形は正確な符号化長を一度予約（空出力は不要）し完全なUTF-8をcommitする。

<a id="s-22-10-2"></a>

### 22.10.2. 既定表現

| 値 | 表現 |
| --- | --- |
| 整数・Wrapping&lt;T> | ASCII十進、負だけ-、先頭0なし |
| bool・char・Unit | true/false、UTF-8、() |
| string・Utf8Slice | NULを含む自身のバイト |
| FromEnd&lt;T>・Start・End | ^とoffset（^1、^-1）、空、空 |
| Range・ClosedRange・ResolvedRange | start、..または..=、end（1..^1、..=3、2..、1..4） |

有限非0浮動値は、同じ幅で最近接偶数丸めにより元へ戻る最少有効桁数を使う。同数なら最も近い候補、さらに末尾偶数桁を選ぶ。正規化十進指数eが-4 &lt;= e &lt; 16なら固定表記、他は小文字eの指数表記で+と指数先頭0を省く。小数点は.で不要末尾0・点は付けずlocale非依存。特殊値は0、-0、Infinity、-Infinity、NaN（符号・ペイロードによらず）。借用は適合を転送せず補間がAdaptationする。ハンドル、ポインター、タプル、配列、ユーザー型の自動適合はない。

<a id="s-22-10-3"></a>

### 22.10.3. 所有文字列の補間

字句形は[§2.9](#s-2-9)。埋込み式をT is Utf8Formatのwrite&lt;T>(value:ref/T)へ渡す。所有Place・一時値は自身の型をTとして共有借用、ref/Uはそのまま、uniq/Uは共有再借用してT=U。裸の式は元をMoveせず、`\(x@move)`は移動した一時値を借りる。式を左から一度評価し、各writeを終えてから次へ進む。最初の失敗でKIMI_E_FORMAT、成功なら所有string。

```kimi
let count = 3
let message = "count = \(count), range = \(1..^1)"   // "count = 3, range = 1..^1"
```

<a id="s-22-10-4"></a>

### 22.10.4. `$tryWrite`

`$tryWrite(writer,literal)`はResult&lt;(),BufferFull>を返す組込み操作で、結合stringを作らず関数値にもならない。第一オペランドはuniq/Utf8Writerとして取得し、レシーバ式ではないため裸の所有Placeはエラー、借用値は再借用する。第二は通常・raw文字列リテラルで補間の有無を問わない。

第一を一度評価して借用を開始してから埋込み式へ進み、区分と値を左から書く。最初の失敗、または既存adapter失敗で後続式を評価しない（検査は行う）。埋込み式はadapter・bufferを使用できない。一時値スコープとreturn・exit・yieldの対象は変わらない。

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    // try $tryWrite(writer, "x")     // Error: a bare owned Place is not borrowed implicitly.
    match buffer.text()
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => $abort("unexpected invalid UTF-8")
    return .Ok(())
```

<a id="s-22-10-5"></a>

### 22.10.5. メモリー割当て

整形対象型が基本整数、Wrapping&lt;T>、bool、char、Unit、f32、f64なら値は**有界**、他は非有界。

- FixedBufferまたは十分な容量のHeapBufferへの組込み整形は割当てなし。
- bufferが全検証済みならtext・validate・intoText・intoStringは走査しない。
- 全値が有界、または唯一の非有界値がstring/Utf8Slice一つでその前がリテラル文字列だけの補間は、ヒープ割当て高々一回。
- Text.toStringは組込み整形が全出力を一度予約する場合高々一回。サイズ事前走査・再試行なしでformatterを一度呼ぶ。
- 境界は結果stringの領域を含む私有出力buffer割当てを数え、埋込み式・ユーザーformatterの割当ては除く。他の私有buffer回数は実装定義。実装は省略・結合・遅延・スタック配置でき、その割当て失敗も起きなくてよいが、結果・出力・評価順と回数・ユーザー効果は変えてはならない。

```kimi
let s = "xyz"
let line = "abc\(s)def"     // One unbounded value preceded only by literals: at most one allocation.
```

<a id="s-23"></a>

# 23. コンパイラーサービス

本章は診断分類、プログラム検査、kimi check・言語サーバーの外部契約、任意Compiler Server Protocolの要件を定める。言語の適否は変えず、エディター診断は助言でbuildへ影響しない。

<a id="s-23-1"></a>

## 23.1. 診断

<a id="s-23-1-1"></a>

### 23.1.1. 分類

診断はコード、重大度（Error・Warning・Information・Hint）、分類を持ち、各コードは一重大度・一分類に属する。Unsupported_Kdなど仕様が指定するもの以外のコード、文面、事実は実装定義。

| 分類 | 意味 |
| --- | --- |
| Language | 言語規則違反 |
| Proof | 必要な事実を証明できない |
| Unsupported | 有効な形式だが実装が非対応 |
| Input | 入力・設定・依存内容・ロックが確定できない、または無効 |
| Resource | Semantics Case数・生成具体化数等の実装上限。プログラムは有効かもしれない |
| Internal | コンパイラーの欠陥 |

Warningは有効だが疑わしい形式を示す。必須警告以外も出せるが、Warning・Information・Hintは拒否しない。仕様が許す未対応形式は位置付きError Unsupported_Kdで報告し、Languageにしない。特定具体型だけ未対応でも具体使用・具体化位置でUnsupportedとし、適合・定義検査を変えない。資源上限はResourceでありLanguage・Proofや無効判定にしない。

<a id="s-23-1-2"></a>

### 23.1.2. 報告

独立した失敗要件は他検査の失敗後も全て報告する。複数Semantics Case・具体化で見つかった同失敗のまとめ方、報告済み失敗だけを原因とする派生失敗の報告有無は実装定義で、受理を変えない。診断はソースと任意span、または全ソース外ならソースなし。人向け行・桁は[§2.1](#s-2-1)、プロトコル位置は各契約に従う。

```kimi
enum Failure
    Busy

func prepare() -> Result<i32, Failure> => .Ok(1)

func setup() -> i32
    prepare()              // Warning: Result discarded; a Warning rejects nothing.
    let text: string = 1   // Error: 1 does not fit string (Language).
    return missing         // Error: reported as well; it is independent (Language).
```

<a id="s-23-1-3"></a>

### 23.1.3. 修復候補

診断は検査で読んだソースへの構造化編集案を複数候補として持てる。各候補は検証済み条件と未判定条件を分け、未判定を検証済みと記さない。適用後は新入力であり再検査だけが有効性を確かめる。種類・条件は[§23.3](#s-23-3)スキーマの閉じた列挙で、提供する候補は実装定義。

<a id="s-23-2"></a>

## 23.2. 検査

<a id="s-23-2-1"></a>

### 23.2.1. 検査単位

一検査単位はProject、target、mode（Product/Test）、Debugの組合せ。結果は同入力・設定のkimi checkまたはテスト準備と一致する。Product・Testは別検査で、製品事実は製品入力だけによるため製品成功・テスト準備失敗は両立する。検査はprojectファイル、projectディレクトリの*.kimi一覧、ソース、lock、依存内容を読む。ソース文字列は[§2.1](#s-2-1)。

<a id="s-23-2-2"></a>

### 23.2.2. 結果と受理

Completedは検査集合の具体化を含む全Language・Proofエラーを報告し、具体レイアウト・表現義務も対象。Blockedは停止原因のInputエラーを報告する。

| outcome | 意味 |
| --- | --- |
| Completed | エラー有無を問わず検査完了 |
| Blocked | 入力・設定を確定できない。Inputエラー付き |
| Faulted | コンパイラー内部失敗。Internalエラー付き |

取消した単位は何も報告しない。CompletedかつErrorなしだけがaccepted。拒否結果は少なくとも一Errorを報告する。checkが受理したプログラムはemit/build/runでLanguage・Proofエラーにならない。ただしResource、対象未対応Unsupported、native解決、toolchain、出力、起動失敗はあり得る。checkがtarget非対応操作を既に報告するかは実装定義だが、報告するなら同じ検査集合を使う。

```kimi
func divide(a: i128, b: i128) -> i128 => a / b
// No Language or Proof error. A target without 128-bit division reports Unsupported
// when building; check may report it too.
```

<a id="s-23-2-3"></a>

### 23.2.3. checkコマンド

kimi checkはまず空でないProject/暗黙ソースパス集合を選び、一パス一Product単位を検査する。選択失敗はInput付きBlocked、内部選択失敗はInternal付きFaultedで、どちらも単位結果なし。空集合はInputエラー。

パスは絶対化し`.`・`..`を除き区切りを/に統一する。hostの比較規則で重複をまとめ、Unicode scalar順で最小の綴りを残す。その順で並べ、順序付けではシンボリックリンクを解決しない。実行順によらず結果もこの順。

選択成功後は全単位を検査し、個別load・設定失敗はBlocked、内部失敗はFaultedとして他単位を続ける。各結果は自身のoutcome、accepted、ソース表、診断を保つ。targetを確定できないunit.targetは--Target値または空文字列。

コマンドoutcomeは一つでもFaultedならFaulted、次にBlocked、他はCompleted。選択成功かつ全単位受理だけがコマンド受理。コマンド診断は選択失敗だけで、単位診断を重複掲載しない。Completedでも拒否され得る。終了コードはIMPL [§20.3](IMPL.md)。

全単位終了後だけJSONを公開する。公開前の取消しは全結果を捨てJSONを書かず130を返す。公開開始後は取消しを観測せず書き終え、結果の終了コードを使う。

<a id="s-23-3"></a>

## 23.3. JSON検査出力

kimi check --Format jsonは標準出力に一つのkimi.check/3コマンド文書を書き、他出力は標準エラーへ送る。unitsは全単位結果で、一Projectなら一要素。ルートはschema・compiler同一性、コマンドoutcome・accepted、コマンドsources・diagnosticsを持ち、選択成功後の後二者は空。診断・関連位置・修復のソース添字は所属結果の表だけを参照する。

巻末のcheck出力JSONスキーマ全文も本章契約の一部。spanのoffset・lengthは検査した文字列のUTF-16コード単位、rangeは0始まりの行・文字。取消しは[§23.2.3](#s-23-2-3)。フィールド・列挙値の削除、改名、意味変更でschema版を変え、任意フィールド・列挙値の追加は同版を保つ。

[check出力JSONスキーマ](#check-output-schema)

```json
{
  "schema": "kimi.check/3", "compiler": "example-build",
  "outcome": "Blocked", "accepted": false, "sources": [], "diagnostics": [],
  "units": [
    {
      "unit": { "project": "C:/work/App/App.kimiproj", "target": "x86_64-pc-windows-msvc", "mode": "Product", "debug": false },
      "outcome": "Completed", "accepted": true, "testPresence": "No",
      "sources": [], "diagnostics": []
    },
    {
      "unit": { "project": "C:/work/Library/Library.kimiproj", "target": "", "mode": "Product", "debug": false },
      "outcome": "Blocked", "accepted": false, "testPresence": "Unknown", "sources": [],
      "diagnostics": [
        { "code": "ExampleInput_Kd", "severity": "Error", "category": "Input",
          "message": "Project could not be loaded", "source": -1 }
      ]
    }
  ]
}
```

<a id="s-23-4"></a>

## 23.4. 言語サーバー

kimi lspは標準入出力でLSPを提供する。検査診断をUTF-16位置で公開し、修復候補のquick-fixやHoverを提供してよい。他能力・設定・スケジュールは実装定義。ファイル書込み、package restore、コード実行はしない。

開いた文書のProjectに対し検査単位の診断を公開する。開いた文書文字列をdiskの代わりに使い、読んだ同入力・単位のcheck/テスト準備と一致する。検査単位の選択・実行・終了時期は実装定義。文書は製品ソースまたはTestSourcesに含むProjectへ属する。Project探索法と暗黙Projectへする条件は実装定義。

<a id="s-23-5"></a>

## 23.5. Compiler Server Protocol

CSPはAIを含むプログラム向けの任意構造化インターフェイスで、通信形式は実装定義。提供するなら次を満たす。

- 全ソースsnapshotを永続的内容同一性で識別し、編集・結果・証拠が依拠入力を指定する。
- 編集・修復は生成元snapshotだけに適用し、黙って書かず、レビュー可能なソース差分を返す。未判定条件は編集後の再検査で確立する。
- 診断は[§23.3](#s-23-3)形式。
- 構文ノードへ安定handleを与え、除外構文と最内除外ディレクティブも含める。選択構文の意味ノードにも与える。
- 全検査・テスト・測定を正確なソース・設定・結果・残る不確実性へ結び付ける。
- unsafeブロックと利用操作、unsafe呼出し、LibraryImportの約束・義務を列挙し、一時停止点とtaskスロット、そこをまたぐlive Loan・所有値も列挙する。
- 言語規則を狭めない。

<a id="s-24"></a>

# 24. 一時停止と非同期タスク

[§24.2](#s-24-2)が停止可能な関数、[§24.3](#s-24-3)が停止箇所とその間の他タスク実行を定める。Async操作・取消し・runは[§22.3](#s-22-3)。一タスク木は一スレッドで動き、構造化ワーカーだけが他スレッドで通常計算を実行する。本章違反は全てLanguageエラー。

```kimi
func fetchBoth(task; a: ref/PageId, b: ref/PageId) -> Result<(Page, Page), FetchError>
    let getA = func [a] (task;) -> Result<Page, FetchError> => fetchPage(task; a)
    let getB = func [b] (task;) -> Result<Page, FetchError> => fetchPage(task; b)
    return Async.joinOk(task; getA@move, getB@move)   // Task call: the children run concurrently.
```

<a id="s-24-1"></a>

## 24.1. タスクとタスクフレーム

**タスク**はAsync.runが始めるrootまたはKimi操作が始める子の実行。rootは新タスク木、子は開始したタスクの木に属し、タスクと全子孫が部分木。

**タスクフレーム**はtaskスロット付き関数の一呼出しの入口から完了までの、停止中を含む状態。タスクは本体フレームと、その中のタスク呼出しで作る全フレームからなる。外側は内側を含む呼出しの実行中なので、最内フレーム停止は全フレームを止める。フレームは値でなくMove・Copy・格納できず、呼出し予約の有効化とも別。**タスク呼出し**はフレームを停止できる唯一の場所、すなわち**一時停止点**。

```kimi
func outer(task; delay: Time.Duration) -> ()
    inner(task; delay)               // outer's task frame waits inside this task call.

func inner(task; delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)     // Suspends inner and outer together.
```

<a id="s-24-2"></a>

## 24.2. タスクスロット

関数が停止できるのはパラメーターリスト先頭にtask;を書くときだけ。これは型でも値でも束縛でもなく、キャプチャー・格納・返却・型引数束縛はできない。

<a id="s-24-2-1"></a>

### 24.2.1. 形式

taskスロットを許すのは名前付き関数、Contract関数要件、明示特殊化、匿名関数、関数型・Callableシグネチャのパラメーターリスト。構築・基底初期化子を含む呼出し引数リストも同位置にtask引数task;を構文上許す。

taskは文脈キーワード。型位置でtask;から始まるリストは関数パラメーターリスト。空でも(task;)とセミコロンを必ず書き、その直後にコンマを置かない。(task)はtaskという通常入力・引数・括弧型。スロットはレシーバと!の両区間より前でどちらにも属さず、高々一つ。

コンストラクター、外部宣言、Application main、テスト関数にtaskスロットはなく、アクセサー・drop、属性・$操作引数、推論Caseペイロード、パターンのリストも許さない。匿名関数はヘッダーに明記した場合だけ持ち、期待シグネチャは補わない。ただし省略入力型はtaskスロット後に対応させて補う。明示特殊化・virtual overrideは元のtask有無と一致させる。

```kimi
contract Reader
    func read(task; self: uniq/Self, buffer: uniq/Array<u8>) -> Result<isize, ReadError>
    func late(value: i32, task;) -> ()        // Error: the task slot comes first.

func pause(task; ! delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)

struct Job
    init(task;) => ()                         // Error: a constructor has no task slot.
```

<a id="s-24-2-2"></a>

### 24.2.2. 引渡し

foo(task;x)、bar(task;)は最内関数（匿名を含む）のタスクを渡す。選択宣言・関数型・Callableにtaskスロットがある場合に限り、かつ必ずtask引数を記す。構築、修飾Case構築、基底初期化子の呼出し先にtaskスロットはない。

task引数は最内関数がtaskを持つときだけ記せ、その関数の既定式や登録defer本体には直接書けない。そこにある関数式は独自境界を持ち自身のtaskを渡せる。従って後処理は停止しない。task付き呼出し先にfoo(task,x)、foo(task)と書き、先頭taskが束縛に解決しない名前なら、task引数欠落として扱う。

```kimi
func runAll<F>(task; jobs: ref/Array<F>) -> ()
    F is Callable<(task;) -> ()>
    for job in jobs
        job(task;)

func misuse(task; delay: Time.Duration) -> ()
    let saved = task                                     // Error: the task slot is no value.
    let later = func [delay] () => _ = Async.sleep(task; delay)
                                                         // Error: the Closure has no task slot.
    Console.writeLine(task; "done")                      // Error: writeLine has no task slot.
```

<a id="s-24-2-3"></a>

### 24.2.3. パラメーターではなく、関数群ごとに一形式

taskスロットに位置番号、外部名、取得モード、照合キー、引数束縛はない。通常入力・レシーバの番号とN/Kは変わらず、configure(task; ! count:i32)のcountは0、K=0。Origin省略入力でも呼出しごとのOriginでもない。

一関数群は全宣言がtask付きか全てなし。宣言スコープ（コンテナーと断片、root、local、型メンバー、継承込みContract要件）の同名群は宣言で検査し、使用時に集めた群はそこで検査する。task有無はオーバーロードを区別しないため、task;忘れは別候補選択でなく引数欠落。taskだけが異なる二宣言は重複宣言でもある。

```kimi
contract Fetcher
    func fetch(task; self: ref/Self, id: ref/PageId) -> Page
    func fetch(self: ref/Self, id: ref/PageId, retries: i32) -> Page
                                         // Error: one group, two task-slot shapes.
```

<a id="s-24-2-4"></a>

### 24.2.4. 互換性

実装識別、特殊化対象識別、呼出しシグネチャ互換性、共通関数型変換、既知・固定シグネチャ照合、関数型同一性では、両側がtask付きか両方なしを要求し、残る入力を通常比較する。一時停止しない実装もtaskを宣言すればtask付き要件を満たせる。

```kimi
func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task; delay: Time.Duration) -> ()
    let wait = func [delay] (task; value: i32) => _ = Async.sleep(task; delay)
    runLater(wait@move)        // Error: the task slot differs from (i32) -> ().
```

<a id="s-24-3"></a>

## 24.3. タスク呼出し

引数リストがtask;で始まる呼出しがタスク呼出し。

1. 非タスク呼出しの実行中は、開始前から存在する同じ木の他タスクを動かさない。
2. 呼出し内のrunが始めた入れ子木は呼出し先の一部としてその呼出し内で動き、効果もその呼出しの効果と比較する。Kimi操作の子もその操作のタスク呼出し内で動き、同じ扱い。
3. 呼出し本体では、タスク呼出しは未知環境効果呼出しと同じ全live static Loanに競合する。呼出し先値・レシーバ・引数が保持・依存するものも含み、公開要約や効果境界に関係なく適用する。この比較は呼出し先効果要約に含めない。

非タスク呼出し先は停止しないため実行中の範囲がその呼出し進行範囲と一致する。外側木のタスクもrunへ到達した呼出しの実行中にある。他タスクが状態へ達する経路は、子へ渡すLoan（開始呼出しの引数依存）、static、共有だけを許すrc/arcペイロード、実装が保証するKimi内部領域だけ。ローカル・パラメーターLoanはこのstatic比較を受けず停止をまたいで保持できる。staticを借りるパラメーターは渡したタスク呼出し、またはキャプチャーしたrootのrun呼出し側で既に比較する。

```kimi
group Settings
    public var greeting: string = "hello"

contract Source
    func take(task; self: uniq/Self) -> u32
        effect confined

func show<S>(task; source: uniq/S) -> ()
    S is Source
    let text = Settings.greeting@ref
    _ = source.take(task;)      // Error: a task call; `confined` does not exclude other tasks.
    inspect(text)
```

<a id="s-D"></a>

# 付録D. 本版に含めない機能

各項目は本版外の機能と、その境界を定める節を示す。実装の対応状況はSTATUS.mdだけに記録する。

<a id="s-D-1"></a>

## D.1. 型、値、オブジェクト

- Weakの直接ビュー変換、生存だけの検査、unsafe weakポインター、unowned参照（[§3.2.2](#s-3-2-2)、[§13.6.3](#s-13-6-3)）。
- 暗黙または追加の通常基底変換（[§3.3.1](#s-3-3-1)）。
- 実行時Contractビューの指定・綴り、関連型束縛、正確型・Contract検査、検査付きキャスト、消費・ジェネリック・型関数の実行時要件（[§8.12](#s-8-12)、[§13.6.5](#s-13-6-5)）。
- object・rawペア層のfollow（s is object、objectborrow、raw許容。[§13.5.5](#s-13-5-5)）。
- 回復可能なオブジェクト生成、アロケーター選択、一般的値複製（[§13.6.2](#s-13-6-2)、[§3.5.2](#s-3-5-2)）。
- 固定配列の要素生成関数による構築（[§4.3](#s-4-3)）、文字列添字（[§4.7](#s-4-7)）。
- 降順・ステップ付き・無限範囲（開始だけの範囲反復を含む）、実行時に方向を選ぶ位置、ユーザー位置・範囲型、contains等の範囲操作、長さを持つユーザー型の位置正規化、Contract既定実装、実行時範囲ビュー（[§4.6.1](#s-4-6-1)、[§4.6.2](#s-4-6-2)）。
- UniqSlice反復適合、専用非重複分割、追加try操作、任意ポインターからの公開構築（[§4.8.4](#s-4-8-4)）。
- 追加動的コレクションAPI（[§4.9](#s-4-9)）。
- Cursor・UniqCursorとKimi.Iteration.shared/uniq（Shared&lt;C>・Uniq&lt;C>）。列挙はLendingIterator・Iteratorとowning/borrowingを使う（[§22.2](#s-22-2)、[§4.7.1](#s-4-7-1)）。

<a id="s-D-2"></a>

## D.2. 宣言、関数、ジェネリクス

- extension宣言・同一性、Contract断片・分割（[§6.1](#s-6-1)、[§6.1.2](#s-6-1-2)）。
- abstract関数、virtualプロパティ・アクセサー、関数自身のジェネリックvirtual、final/newslot、束縛済み基底関数値（[§6.4](#s-6-4)）。
- Layout・LibraryImport・Test以外の一般属性意味（[§6.7](#s-6-7)）。
- Borrowed・Exclusive・Consumingの消去呼出し型、不透明戻り型、レシーバ依存・貸出し型の公開結果、脱出禁止宣言、キャプチャー別名・初期化子・部分、ジェネリックレシーバSemanticsと{environment}、一般高階Callable契約（[§3.2.1](#s-3-2-1)、[§7.7.2](#s-7-7-2)、[§15.8.2](#s-15-8-2)、[§15.8.3](#s-15-8-3)）。
- Contract自身のOrigin・長さパラメーター、既定実装、外部適合、修飾要件呼出し（[§8.4](#s-8-4)）。
- 有界宣言結果を越える関連型推論、より強い記号的制約推論（[§8.2](#s-8-2)、[§8.5](#s-8-5)、[§8.3](#s-8-3)）。
- 関数長さ以外の値引数、定数型引数、独立Semanticsスロット、部分・既定・可変長ジェネリック引数（[§4.4](#s-4-4)、[§8.1](#s-8-1)、[§12.5.2](#s-12-5-2)）。
- 部分・条件付き明示特殊化、優先順位、ジェネリックコンテナー特殊化（[§8.13](#s-8-13)）。
- 直接一致と呼出し型消去の順位付け（[§10.7](#s-10-7)）。
- 既存公開Package版とのObjectCallCompatible状態比較（[§12.7](#s-12-7)）。
- 透過的なソース型別名（[§18.2](#s-18-2)）。
- テスト構成を含むComposition Root Entry/Providerの宣言・選択（[§13.8](#s-13-8)）。

<a id="s-D-3"></a>

## D.3. enum、パターン、失敗

- struct、名前付きペイロード、型、OR、Range、Rest、Arrayパターン（[§14.8.1](#s-14-8-1)）。
- ガード候補キャプチャー構文、束縛別@move/@ref等のパターン内取得指定（[§14.8.3](#s-14-8-3)、[§15.1.6](#s-15-1-6)）。
- 他の束縛構文での反証可能パターン（[§14.8](#s-14-8)）。
- 公開非網羅enum、空enum、enum表現・ABI保証（[§6.5.1](#s-6-5-1)）。
- ユーザー定義try・tryブロック（[§17.3](#s-17-3)）。

<a id="s-D-4"></a>

## D.4. 所有権、寿命、効果

- 追加動的Move Path（[§15.1.3](#s-15-1-3)）。
- 非staticペイロード依存を隠す存在的オブジェクトビュー、static PlaceのOrigin、Callable直接入力量化・Originパラメーター付き関連型を越える高階Origin。
- 破棄寿命条件の緩和（[§15.6.6](#s-15-6-6)）。
- 非virtual具体関数、プロパティ要件、関数型、open構造体dropへの効果境界（消去破棄の効果を既知にするもの）、confined以外の効果語彙、一般効果システム（[§8.11](#s-8-11)、[§16.3.3](#s-16-3-3)）。

<a id="s-D-5"></a>

## D.5. 並行性

- ソースのthread、切離し処理、atomic順序API、追加のスレッド転送・共有アクセス能力、外部コードの並行再入（[§22.3.6](#s-22-3-6)、[§22.3.7](#s-22-3-7)、[§15.2.4](#s-15-2-4)）。
- スレッド別I/O実行器、スレッド間チャネル、必須I/O API（[§22.3](#s-22-3)、[§22.3.6](#s-22-3-6)）。
- スレッド間のatomicな全値更新、同期したarcペイロード変更（[§15.7](#s-15-7)、[§13.6.2](#s-13-6-2)）。
- ブロッキング外部呼出しのワーカー移譲（[§22.3.6](#s-22-3-6)）。

<a id="s-D-6"></a>

## D.6. unsafe、外部関数、実行時

- unsafe関数型、unsafe・外部関数値、外部関数効果宣言、並行実行中のrawポインター、tryAllocate、rawバイトからの文字列生成、共変な所有コンテナー（[§5](#s-5)）。
- C集約値渡し、export、コールバック、可変長引数（[§22.6](#s-22-6)）。
- Kimigayo・C以外の構造レイアウト（[§6.7.1](#s-6-7-1)、IMPL [§21.1.2](IMPL.md)）。
- 過大アラインメント割当て、任意終了コード、浮動小数点環境制御（[§22.5](#s-22-5)）。

<a id="s-D-7"></a>

## D.7. パッケージ、Mods、テスト

- ソース非公開のバイナリー配布、安定した外部Kimigayo ABI（[§18.5](#s-18-5)）。
- 再公開（[§18.3](#s-18-3)）。
- ネットワークレジストリー、版範囲、依存辺設定上書き、PackageからProjectへの置換（[§18.4](#s-18-4)）。
- Mods（ソース生成）のhostインターフェイス、marker登録、設定（[§6.1.2](#s-6-1-2)）。
- パラメーター付きテスト、Suite属性、skip、resource lock、Option・Result検証ヘルパー、文字列・コレクションdiff、添付、期待Abort、コンパイル失敗テスト、coverage（[§6.7.2](#s-6-7-2)、[§17.6](#s-17-6)）。

<a id="s-E"></a>

# 付録E. 用語集

読み方を助ける一覧。原語のアルファベット順で、定義は参照節が所有する。

| 用語 | 意味 | 定義 |
| --- | --- | --- |
| アクセス経路（Access path） | 非raw Placeへの直接・排他参照経由・共有参照経由の経路。Loan検査は段ごとに比較する。raw Placeにはない | [§3.4.1](#s-3-4-1)、[§15.6.2](#s-15-6-2) |
| 取得モード（Acquisition mode） | 外側Semanticsで決まる、裸の所有Place引数へのByValue・Shared・Exclusive要件 | [§7.4.1](#s-7-4-1) |
| Adaptation Target | @が要求するSemanticsとCore、内側型、オブジェクトView Target | [§13.5.1](#s-13-5-1) |
| 保持元のアドレス（Address） | 借用・raw参照外しなしの@rawまたはduring selfフィールド読取りで運ぶ保持元への到達情報。Loanではなく、競合・権限を与えない | [§15.6](#s-15-6) |
| 許容集合（Admitted set） | 前提の下でペアバインダーが取り得るSemantics | [§8.3.4](#s-8-3-4) |
| 関連型（Associated Type） | 適合ごとに固定する型。Originパラメーターを宣言できる | [§8.5](#s-8-5) |
| 裸の取得（Bare acquisition） | @操作を記さない式の取得 | [§3.5](#s-3-5) |
| 束縛集合／Origin射影 | 型出現のスロット→Origin対応／その一スロットの選択 | [§15.3.1](#s-15-3-1) |
| 借用値（Borrow value） | 外側Semanticsがref・uniq・objref・objuniqの値 | [§3.4.1](#s-3-4-1) |
| 組込み数値型 | 数値型、PrimitiveInteger証明済み型パラメーター、そのWrapping&lt;T> | [§13.3.3](#s-13-3-3) |
| 呼出しレシーバ要件 | 呼出し本体がレシーバに求める最小アクセス | [§7.7.3](#s-7-7-3) |
| Callable制約 | ジェネリック値の呼び方を指定する制約 | [§8.10](#s-8-10) |
| Case／ペイロード | enumの選択肢／その位置付きデータ | [§6.5](#s-6-5) |
| raw Originの宣言的主張（Claim） | unsafe内で期待型に合う操作ではないE@(raw/U during o)により、全アクセスがoの領域内と約束すること | [§5.7](#s-5-7) |
| 閉じたContract | Kimiが適合型を固定するPosition・PositionRange | [§8.7.5](#s-8-7-5) |
| クロージャー／キャプチャー | 本体と環境を持つ呼出し値。各入口はlet x=xまたはvar x=x同様に環境束縛を初期化する | [§7.7.2](#s-7-7-2) |
| 共通Adaptation | 固定期待型のある位置で選ぶ単一操作 | [§10.2](#s-10-2) |
| コンパイルルート | 各Kotonohaのルート宣言、直接依存参照名、Kimi | [§18](#s-18) |
| コンパイラー組込みContract | Copy、Owned、Callable、Sealed、ObjectPayload、PrimitiveInteger | [§8.7.1](#s-8-7-1) |
| 完全な値 | 構築完了と現在の完全性を持つ値 | [§15.1.2](#s-15-1-2) |
| Composition Root | $abort・$tryWrite・$expect・$requireのルート$ | [§13.8](#s-13-8) |
| 条件（Condition） | 組込み条件とProject設定を使うboolディレクティブ式 | [§19.2](#s-19-2) |
| 条件付き適合 | 条件がProvenのとき使える適合 | [§8.8](#s-8-8) |
| 条件付きメンバー | 型のパラメーターを制約し、そのProven時だけ適用できるメンバー | [§7.5](#s-7-5) |
| confined | 環境効果・未分類効果がないという効果境界 | [§8.11.2](#s-8-11-2) |
| 競合アクセス | Placeまたは同一参照カウントへの重複アクセスで、一方が書込み、少なくとも一方が実装のatomicアクセスではないもの | [§22.3.7](#s-22-3-7) |
| 適合（Conformance） | 型がContractを満たすこと | [§8.6.1](#s-8-6-1) |
| 適合同一性 | 完全な適合型と束縛済みContract参照 | [§8.6.1](#s-8-6-1) |
| 制約／制約節 | 型・Semanticsの条件／subject is requirementの節 | [§8.2.1](#s-8-2-1) |
| 構成型／head | 構造を形式が固定する型（型入力、ペア対象、Contract Self、未解決射影を除く）／その形式を識別する宣言・Semantics・要素数など | [§8.2.3](#s-8-2-3) |
| Consume | アクセス操作の構造上の資格と使用箇所の適法性を静的に検査すること | [§15.1.4](#s-15-1-4) |
| Contract | 実装・格納を持たず要件を宣言するコンテナー | [§8.4.1](#s-8-4-1) |
| Copy | 元をInitializedに保つ複製 | [§3.5](#s-3-5) |
| Core | Semantics・Originを除いた型の値種別・構造・同一性 | [§3](#s-3) |
| データ競合 | 別スレッドの競合アクセスをhappens-beforeが順序付けないこと。未定義動作 | [§22.3.7](#s-22-3-7) |
| 宣言コンテナー | 種類に応じたメンバーを持つ名前付き宣言スコープ | [§6.1](#s-6-1) |
| deferブロック | スコープ退出のためdeferが登録する後処理 | [§16.1](#s-16-1) |
| 診断 | 重大度とLanguage・Proof・Unsupported・Input・Resource・Internalの一分類を持つ報告 | [§23.1.1](#s-23-1-1) |
| 直接基底呼出し | base.f(...)。直接基底の実装を選ぶ | [§6.4.4](#s-6-4-4) |
| 動的型（Dynamic Type） | オブジェクトが実際に構築されたCore | [§3.3.1](#s-3-3-1) |
| 効果境界 | 要件・Callable制約・virtualスロットに宣言するアクセス保証confined | [§8.11](#s-8-11) |
| Effective Type | 絞込み後の地点で保証される最派生Coreを持つ型 | [§14.10.1](#s-14-10-1) |
| 除外構文 | ディレクティブで選ばれずソース検査だけを受ける構文 | [§19.4](#s-19-4) |
| 型の展開（Expansion） | 各抽象型を同一な構成型で反復置換した型。有限展開がなければ前提エラー | [§8.3.3](#s-8-3-3) |
| フィールド | let・varプロパティの記憶領域 | [§11.1](#s-11-1) |
| 有限Origin | 上限内で使用からregionを推論するOrigin | [§15.6.5](#s-15-6-5) |
| 固定期待呼出しシグネチャ | 期待共通関数型、または未束縛FのF・ref/F・uniq/F位置のCallable制約が固定する唯一のシグネチャ | [§10.5.2](#s-10-5-2) |
| Follow | @followで参照・条件を満たすペア層の指すPlace、またはSealed証明済み対象のハンドルのペイロードを選ぶこと | [§13.5.5](#s-13-5-5) |
| Function Item／共通関数型 | 環境のない解決済み関数宣言／呼出し値間で共有する消去呼出し契約 | [§3.2.1](#s-3-2-1) |
| 保持元（Holder） | ローカル・入力・一時値。全部分とduring self raw層の所有領域のLoan・アドレスを運び、各部分が届く本体外領域へ届く | [§15.6](#s-15-6) |
| 独立に型付けできる引数 | 呼出しの期待・リテラル既定なしで完全型が決まる非リテラル引数。入れ子呼出しは期待結果なしで唯一の適用候補がある場合だけ。匿名関数は不可。タプル・配列リテラルは要素別分類 | [§10.5.1](#s-10-5-1) |
| Indexable／UniqIndexable | index・indexUniqがreceiver[key]の参照先となる参照を返すContract | [§4.7.1](#s-4-7-1) |
| 整数／ラップ整数／数値型 | 12整数Coreの一つ／Wrapping&lt;T>／整数・ラップ整数・浮動小数点 | [§3.1](#s-3-1) |
| Item／LentItem(step) | Iteratorの項目型／レシーバを借用し得るLendingIterator項目型 | [§22.2.1](#s-22-2-1) |
| Iterable／UniqIterable／IntoIterable | Shared・Exclusive・ByValue Subjectからforが選ぶ入口 | [§22.2.1](#s-22-2-1) |
| Iterator／LendingIterator | 項目が後続nextを越えて生存できる／項目が自身を借用し得る反復Contract | [§22.2.1](#s-22-2-1) |
| Kimi Kotonoha | Kimi名で参照する基盤モジュール | [§22.1](#s-22-1) |
| Kotonoha | 名前付きモジュール一つ | [§18](#s-18) |
| 貸出し点／Loanアンカー | 借用・再借用・射影が対象とする領域 | [§3.4.2](#s-3-4-2) |
| 貸出し規則 | 転送・新規排他借用を明記する条件 | [§15.1.5](#s-15-1-5) |
| リテラル引数 | 未型付けリテラル、リテラルのみの式、結果元が全てそれらの制御式。スロット証拠にならず、タプル・配列リテラルは要素別分類 | [§10.5.1](#s-10-5-1) |
| リテラルだけの式 | 未型付け数値リテラルと所定演算子で作る整数・浮動・位置・範囲式。型に合うかの検査は別判定 | [§12.3.3](#s-12-3-3) |
| Loan | 借用Placeの経路とref/uniqモード。借用位置・Semantics Caseで識別し、保持値が使用され得る間live。正確な接頭辞のMove Pathのその場破棄で終了 | [§15.6](#s-15-6)、[§15.6.3](#s-15-6-3) |
| Loan&lt;T> | 記憶領域なしに借用依存を表すゼロサイズstruct | [§15.3.5](#s-15-3-5) |
| Movable Place | @moveで値を転送できるPlace | [§15.1.5](#s-15-1-5) |
| Move | 値と破棄責任の転送。Placeでは@moveと記す | [§3.5](#s-3-5) |
| Move Path／部分Move | 独立初期化状態の追跡経路／集成値の部分のMove | [§15.1.3](#s-15-1-3) |
| noinit | 初期格納を省くunsafeな固定配列構築 | [§4.3.4](#s-4-3-4) |
| Object Target | オブジェクト形式を形成できる対象型 | [§8.7.3](#s-8-7-3) |
| ObjectCallCompatible | オブジェクト借用経由の呼出しがレシーバを保つ保証 | [§12.7](#s-12-7) |
| ObjectPayload | オブジェクトペイロードにできる型。Self is not ObjectPayloadで辞退する | [§8.7.3](#s-8-7-3) |
| ObjectViewCompatible | View Targetに使えるContract | [§8.12](#s-8-12) |
| Origin | regionを表す記号的寿命 | [§15.2.1](#s-15-2-1) |
| Originパラメーター | LentItem(step)など関連型のOriginバインダー | [§8.5.4](#s-8-5-4) |
| Origin関係 | Origin間に要求する==またはoutlives | [§15.6.1](#s-15-6-1) |
| 重なり（Overlap） | 同一Place、Placeとそのインライン部分、または1バイトでも共有するPlace | [§3.4](#s-3-4) |
| オーバーライド | 継承virtualスロット一つを実装する派生本体。検索層・候補・公開関数を増やさない | [§6.4.2](#s-6-4-2) |
| Owned | 非static Originからの独立性 | [§15.2.3](#s-15-2-3) |
| ペアバインダー／ペア対象 | &lt;s/T>のs／T | [§8.1.1](#s-8-1-1) |
| ペア層 | 外側Semanticsがペアバインダーの型 | [§13.5.5](#s-13-5-5) |
| パラメーター取得形 | 一関数群の重なる入力に共通の取得モード | [§7.4](#s-7-4) |
| パラメーター依存効果 | 型入力・関連型射影で保持し、[§15.6.4](#s-15-6-4)で範囲を広げない限り使用側で実型と合成する効果 | [§15.6.4](#s-15-6-4) |
| Place／記憶領域 | 型・能力を伴い選択した格納先／値を保持する領域 | [§3.4](#s-3-4) |
| 位置／範囲 | [0,L]へ解決する境界／位置の区間 | [§4.6.1](#s-4-6-1)、[§4.6.2](#s-4-6-2) |
| PrimitiveInteger | 12整数型だけの組込みContract | [§8.7.4](#s-8-7-4) |
| プロジェクトルート | Kotonoha宣言階層の最上位 | [§18](#s-18) |
| 真部分（Proper part） | 構成型の成分へ一段以上進んで得る型。射影・フィールドへは進まない | [§8.2.3](#s-8-2-3) |
| プロパティ | 格納let/varメンバーまたは計算メンバー | [§11.1](#s-11-1) |
| 型を読み替えたPlace（Punned Place） | Initialized Placeのバイト上に置く、Copy・正サイズ型の一時的Place | [§5.2](#s-5-2) |
| rawポインター／raw Place | raw/Tアドレス／そのアドレス上の*p・p[n]・その部分 | [§5](#s-5)、[§5.2](#s-5-2) |
| rawポインターOrigin | raw/T during oが指すoの領域、selfなら保持値自身の領域。Loanもregion制約も作らない | [§5.7](#s-5-7) |
| raw領域 | 割当て・動的要素領域など、配置値のPlaceを宣言しない記憶領域 | [§3.4](#s-3-4) |
| Read／Write／Take | Placeの観測／初期化・置換／抽出能力 | [§3.4](#s-3-4) |
| 読取り型／値読取り | Scalar・位置・範囲型／それを要する位置で安全ref/uniq層末端値を暗黙Copyすること | [§3.5.3](#s-3-5-3) |
| 再借用（Reborrow） | 既存借用から派生する借用 | [§13.5.6](#s-13-5-6) |
| レシーバ式 | メソッド・アクセサー・関数値呼出しのレシーバ | [§3.4.1](#s-3-4-1) |
| レシーバ形 | 一関数群で一つのレシーバ型Semantics | [§7.3.2](#s-7-3-2) |
| 再帰Contract | 適合型の射影が再び自身へ適合することを制約するContract。具体型は適合しない | [§8.3.3](#s-8-3-3) |
| 参照経路選択 | 型だけで連続する安全参照層を選択すること | [§3.4.3](#s-3-4-3) |
| region | プログラム点集合。region(o)はOrigin oの意味 | [§15.2.1](#s-15-2-1) |
| 領域分割 | 一親Loanから複数子を派生。排他子同士は重ならない | [§15.6.3](#s-15-6-3) |
| 修復候補 | 診断に任意で付ける構造化編集 | [§23.1.3](#s-23-1-3) |
| Scalar | 整数・浮動・bool・char・ラップ整数Core | [§3.1](#s-3-1) |
| Sealed | Never・open struct以外のowner Coreである証拠 | [§8.7.2](#s-8-7-2) |
| Semantics | 値の表現、所有、借用、アクセス規則 | [§3.3](#s-3-3) |
| Semantics Case | 各ペアバインダーに許容Semanticsを一つ選び本体検査する場合分け | [§8.15.2](#s-8-15-2) |
| SourceDocument | Kotonohaの一ソース入力 | [§18.1](#s-18-1) |
| static Loan | 静的領域にアンカーを持つLoan | [§24.3](#s-24-3) |
| Subject Place／Subjectモード | match・forがSubjectから初期化するPlace／与えるアクセス | [§15.1.6](#s-15-1-6) |
| 対象補完（Target completion） | 実行時検査・検査付きキャストの対象Origin束縛 | [§13.6.5](#s-13-6-5) |
| タスク／タスク木 | runのrootまたはKimi操作の子の実行／rootと子孫 | [§24.1](#s-24-1) |
| タスク呼出し | 引数がtask;で始まる呼出し。唯一の一時停止点 | [§24.3](#s-24-3) |
| タスクフレーム | 停止中を含むtask関数一呼出しの状態 | [§24.1](#s-24-1) |
| タスクスロット | パラメーターリスト・関数型先頭のtask; | [§24.2](#s-24-2) |
| 一時値／一時Place | 式の一時結果／それを実体化する記憶領域 | [§3.6](#s-3-6) |
| 型（Type） | Semantics、対象、全Originを含む完全な型 | [§3](#s-3) |
| 型同一性 | スコープで二型を一つにする根拠を、置換と同head構成型の分解で閉じた関係 | [§8.2.3](#s-8-2-3) |
| 未定義動作 | unsafeでの約束を破った結果 | [§1.3](#s-1-3)、[§5](#s-5) |
| UniqSlice | Non-Copyの排他的連続ビュー | [§4.8.4](#s-4-8-4) |
| unsafe文脈／unsafeブロック | unsafe操作の許可／字句的に許可するunsafe | [§5](#s-5)、[§14.3](#s-14-3) |
| ユーザープロバイダー型 | 外側ownerのstruct・enumで組込み数値型でないもの | [§13.3.3](#s-13-3-3) |
| 値文脈／結果を捨てる文脈 | 式結果を使う／保持しない文脈 | [§14.2.2](#s-14-2-2) |
| 値種別 | 借用値、所有Place、所有一時値 | [§3.4.1](#s-3-4-1) |
| View Target | オブジェクトビューの公開対象 | [§3.3.1](#s-3-3-1) |
| virtualスロット | 束縛宣言同一性を持つ元の公開関数契約 | [§6.4.2](#s-6-4-2) |
| 待機引数（Waiting argument） | リテラルでも独立型付け可能でもない引数。匿名関数、固定期待シグネチャが必要な関数参照、独立でない入れ子呼出しなど。外側スロットの証拠にならず、タプル・配列リテラルは要素別分類 | [§10.5.1](#s-10-5-1) |

訳注：原文用語集の組込みContract一覧はTransferableを省いている。完全な定義は[§8.7.1](#s-8-7-1)・[§22.1](#s-22-1)を参照。

<a id="s-F"></a>

# 付録F. 文法

本付録は規範文法。`? description ?`は各々が示す節で定義する。トークン境界、キーワード、記号、レイアウト、生成規則、優先順位は本付録が、意味と参照する文脈依存制約は各章が定める。生成規則の機械的内容は原文のまま保持する。

<a id="s-F-1"></a>

## F.1. 記法

引用符内は文字どおりの構文。`|`は選択、括弧はグループ化、`?`・`*`・`+`は任意・0回以上・1回以上。`List<X>`は`X ("," X)*`、`TrailingList<X>`は`List<X> ","?`。この山括弧は文法記法で、引用した`"<"`・`">"`はトークン。`NEWLINE`・`INDENT`・`DEDENT`はレイアウトイベントで、`ItemList<X>`・`IndentedList<X>`は[§F.4.2](#s-F-4-2)。`"get"`のような引用`Name`はその綴りに一致し、キーワード文脈は[§F.3.2](#s-F-3-2)。生成規則末尾の`//`コメントもその規則への制約。

<a id="s-F-2"></a>

## F.2. 字句文法

ソースは名前、キーワード、リテラル、記号のトークン列。符号化、空白、コメント配置、リテラル意味は[§2.1](#s-2-1)、[§2.2](#s-2-2)、[§2.6](#s-2-6)～[§2.9](#s-2-9)。

<a id="s-F-2-1"></a>

### F.2.1. トークン、コメント、記号

コメント・リテラル外では記号を最長一致で読む（a+++bはa、++、+、b）。一覧のトークン列を作れない記号はエラー。型引数・ジェネリック入力を閉じるときは>で始まるトークン（>、>=、>>、>>=）の最初の>だけを閉じに使い、残ソースを再字句解析する。式のシフト・比較は変えない。

$・#は独立トークンで、$abortは$と操作名、#ifは#とif。;はtask;だけ、!は入力リスト境界だけ、{}はOrigin波括弧だけ。他用途は禁止。&&・||は全使用をエラーにするためのトークンで、論理演算はand・orを使う。

```ebnf
PhysicalNewline := LF | CR LF | CR
LineComment     := "//" ? characters up to a PhysicalNewline or end of file, [§2.2](../02-source-and-lexical-structure.md#22-whitespace-and-comments) ?
BlockComment    := "/*" ? characters up to the first "*/", [§2.2](../02-source-and-lexical-structure.md#22-whitespace-and-comments) ? "*/"
Token           := Name | Keyword | NumberLiteral | CharLiteral | StringLiteral | Punctuator | "_"
Keyword         := ? a reserved word ([§F.3.1](#f31-reserved-words)) or a contextual keyword in its context ([§F.3.2](#f32-contextual-keywords)) ?
Punctuator      := "(" | ")" | "[" | "]" | "{" | "}" | "," | ";" | "." | ":" | "::"
                 | "->" | "=>" | "@" | "#" | "$" | "?" | "!"
                 | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                 | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                 | "==" | "!=" | "<" | "<=" | ">" | ">="
                 | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^=" | "<<=" | ">>="
                 | ".." | "..=" | "&&" | "||"
```

```kimi
let a: Box<Box<i32>>= make()     // `>`, `>`, `=`
let b: Box<i32>= make()          // `>`, `=`
func f() -> Box<i32>=> make()    // `>`, `=>`
let c = a1 >> 2                  // A shift: no Type arguments are open
```

<a id="s-F-2-2"></a>

### F.2.2. 名前

キーワードはName全体一致で、ifValue・tryGetは名前。単独_はNameでなくトークンで、使用位置、NFC、除外文字、等値は[§2.5](#s-2-5)。

```ebnf
Name         := NameStart NameContinue*   // not a reserved word and not "_"
NameStart    := "A".."Z" | "a".."z" | "_"
              | ? a character of Unicode General Category Lu, Ll, Lt, Lm, Lo or Nl, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
NameContinue := NameStart | "0".."9"
              | ? a character of Unicode General Category Mn, Mc, Nd or Pc, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
```

<a id="s-F-2-3"></a>

### F.2.3. 数値リテラル

符号はリテラルに含まず先頭+/-は演算子。.は直後が十進数字のときだけリテラルに含むため1.は1と.。数値直後のName継続文字は一つの不正トークンとして取り込み、0xg・0b102・123abc等をエラーにする。

メンバー選択.直後は空白・コメントを無視し、数字を小数・指数・基数接頭辞・区切りなしのTupleIndexとして読む。pair.0.1はpair、.、0、.、1。不正トークン規則も適用しpair.1_2はエラー。

```ebnf
NumberLiteral   := IntegerLiteral | FloatLiteral
IntegerLiteral  := DecimalSequence
                 | "0" ("b" | "B") "_"* BinaryDigit (BinaryDigit | "_")*
                 | "0" ("o" | "O") "_"* OctalDigit (OctalDigit | "_")*
                 | "0" ("x" | "X") "_"* HexDigit (HexDigit | "_")*
FloatLiteral    := DecimalSequence (Fraction Exponent? | Exponent)
DecimalSequence := DecimalDigit (DecimalDigit | "_")*
Fraction        := "." DecimalDigit (DecimalDigit | "_")*
Exponent        := ("e" | "E") ("+" | "-")? DecimalDigit (DecimalDigit | "_")*
DecimalDigit    := "0".."9"
BinaryDigit     := "0" | "1"
OctalDigit      := "0".."7"
HexDigit        := DecimalDigit | "a".."f" | "A".."F"
TupleIndex      := DecimalDigit+   // only after a member-access "."; spaces and comments may intervene
```

<a id="s-F-2-4"></a>

### F.2.4. 文字と文字列リテラル

字句生成規則と例は以下。

```ebnf
CharacterEscape := "\0" | "\\" | "\e" | "\t" | "\n" | "\r" | '\"' | "\'"
                 | "\u(" HexDigit HexDigit? HexDigit? HexDigit? HexDigit? HexDigit? ")"
CharLiteral     := "'" (DirectScalar | CharacterEscape) "'"
DirectScalar    := ? one Unicode scalar value not excluded by [§2.8](../02-source-and-lexical-structure.md#28-character-literals) ?
StringLiteral   := EscapedString | RawString
EscapedString   := '"' (StringText | CharacterEscape | Interpolation)* '"'
StringText      := ? any Unicode scalar value except '"' and "\", [§2.9](../02-source-and-lexical-structure.md#29-string-literals) ?
Interpolation   := "\(" Expression ")"
RawString       := QuoteRun RawText QuoteRun
QuoteRun        := ? a run of N >= 3 double quotation marks, selected by [§2.9.2](../02-source-and-lexical-structure.md#292-raw-strings) ?
RawText         := ? content up to the terminating quote run, [§2.9.2](../02-source-and-lexical-structure.md#292-raw-strings) ?
Literal         := NumberLiteral | CharLiteral | StringLiteral
                 | "true" | "false" | "null" | "(" ")"
```

```kimi
let count = 1_000 + 0xFF
let ratio = 2.5e-3
let path = """C:\temp\file.txt"""
let text = "count = \(count)\n"
let first = ((1, 2), 3).0.1       // Tuple indices
```

<a id="s-F-3"></a>

## F.3. キーワード

予約語はNameにならない。文脈キーワードは表の文脈だけでキーワード、他は通常名で、認識はトークンだけを使い名前解決・型を使わない。Never・TransferableはKimiの通常名で隠せるが、::Kimi.Never・::Kimi.Transferableは組込みを指す。

<a id="s-F-3-1"></a>

### F.3.1. 予約語

| 分類 | 綴り |
| --- | --- |
| 基本型 | isize usize i8 i16 i32 i64 i128 u8 u16 u32 u64 u128 f32 f64 bool char string |
| 束縛・関数 | let var func |
| 制御・検査・リテラル | if else case for while loop do match return exit continue yield try require defer is not and or true false null |
| アクセス | public internal private protected |
| 専用形式 | Self init drop base |
| コンパイル時選択 | switch（#の後だけ。実行時switchなし） |
| 将来用 | as |

<a id="s-F-3-2"></a>

### F.3.2. 文脈キーワード

次の表が全て。

| 語 | キーワードとなる文脈 |
| --- | --- |
| alias rootgroup group struct enum contract computed property | 宣言開始。extensionも同位置で認識するが未提供として拒否 |
| unsafe virtual override abstract open | 宣言修飾子。unsafeは実行項目先頭で次が=>または行末の場合も該当 |
| label | Primary先頭で、同じ物理行にNameと:が続く |
| in | forのForBinding直後 |
| to | exit・continue・yield直後 |
| task | 入力・引数・FunctionParametersの(直後で、次が; |
| self | ReceiverShorthand・AccessorReceiver。self・value・storageは文脈束縛でもある |
| get set has value | アクセサー・プロパティ要件構文 |
| associate when specialize | 関連型宣言・指定、条件付き適合、func直前 |
| Callable | RequirementAtom・ConformanceAtom先頭で&lt;が続く |
| effect confined | effectは項目先頭で同じ行にconfined、またはContract項目でContractSelectorが続くとき。confinedはeffect後だけ。効果項目は要件制約領域、Contract EffectSpecification、Callable効果リスト、元virtual本体の先頭領域 |
| during outlives static | AnnotatedType本体と?接尾辞後、OriginRelation内、OriginAtom内の各位置 |
| origin | 関係を許す項目先頭：付随節リスト、struct・enum本体、要件制約領域、関数・コンストラクター・アクセサー先頭領域。origin (...) == ...も常に関係 |
| owner ref uniq obj rc arc objref objuniq raw | 型、要件、CallableReceiver、キャプチャー操作、Adaptation対象のSemantics位置。他は名前 |
| value valueborrow object objectborrow borrow owning reference | 要件のSemanticsCategory |
| move copy follow wrap bits | @直後。wrap・bitsは空白・コメントなしに&lt;が直後の場合だけ |
| abort tryWrite expect require message | $後は予約語でも綴りで操作名に一致。messageはexpect・requireの引数ラベル |
| noinit | =後の括弧なし初期化子全体。そこで名前にはならず[§4.3.4](#s-4-3-4)のみ許可 |
| of length | [N of T]/[N of value]のArrayLength後、GenericParameter先頭 |

open・taskはこの外で通常名（file.open(task;path)のopenなど）。本体先頭でoriginという関数を呼ぶ式を書くなら(origin(b) == a)のように括弧で囲む。

```kimi
func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a
    origin(b) == a          // An Origin relation: it starts an item in the leading region
    return a

let effect = 1              // An ordinary Name: no bound word follows
```

<a id="s-F-3-3"></a>

### F.3.3. 宣言修飾子

ヘッダー順は属性、アクセス指定、高々一修飾子、導入語。func前はunsafe・virtual・override、struct前はopen。abstractも同位置で認識して未提供として拒否する。これらの語から同一行の宣言導入語までがアクセス語と同種修飾語だけなら修飾子として認識する。順序違い、重複、二修飾子、許さない宣言への修飾はエラー。他文脈では通常名。virtual/override対象は[§6.4](#s-6-4)、複合アクセス語はAccess生成規則。

```kimi
public open struct Shape
    public virtual func area(self) -> f64 => 0.0
    virtual public func size(self) -> i32 => 0   // Error: the access word comes first
```

<a id="s-F-4"></a>

## F.4. レイアウト

<a id="s-F-4-1"></a>

### F.4.1. 行、字下げ、本体

字下げ一段はU+0020四つで、コード字下げは4の倍数。コメント・リテラル内部文字列外でトークンを開始・継続する行が有効行。空行・コメントだけの行はイベントを作らず、本体・分岐接続も終えない。複数行リテラル内（補間外）は有効行でなく字下げ検査もしない。

補間\(...)はコードで、\(は区切りを開く。外側段は文字列開始有効行の字下げ、\(の行に始まる構文のヘッダー基準もその段とし、後続補間行は通常レイアウトで検査する。

ヘッダー基準は開始物理行の字下げであり、キーワード・ラベルの桁や継続行ではない。字下げ本体は基準の一段下、match 分岐リストもmatch基準の一段下、分岐本体はその分岐の一段下。単一項目=> Itemは=>と項目開始をヘッダー終了物理行に置く。=>直後改行、継続行先頭=>、本体前:は禁止。

```kimi
let label = "mode: \(match mode
    .Fast => "fast"
    .Slow => "slow"
)"
```

<a id="s-F-4-2"></a>

### F.4.2. レイアウトイベント

区切り外の有効行は、一段下の本体開始ならNEWLINE INDENT、浅くなるなら閉じる本体ごとのDEDENTの後NEWLINE、同じ段ならNEWLINE。継続行はイベントなし。区切り内の改行は、その中の入れ子本体を除きイベントなし。入れ子本体では自身の基準で上記を適用し、閉じる行はDEDENTだけを出す。ソース最初の有効行はイベントなし。EOFは開いた本体をDEDENTで閉じる。未閉じ区切り、存在しない本体段へのdedent、本体も継続も始めないindent増加はエラー。

本体とはIndentedListを許す全位置：実行本体、match 分岐リスト、コンテナー、付随Origin・アクセサー・効果リスト、要件制約領域、switch caseリスト。継続判定前に文法で本体開始を決める。AttributePrefix、IfDirective、BranchJoin、RequireJoinが記すNEWLINEはその生成規則が消費する。字下げ本体の内容と同じ行のコンマ・!・閉じ記号で本体を閉じられず、先に後続行のdedentで閉じる。

```ebnf
ItemList<X>     := X (NEWLINE X)*
IndentedList<X> := NEWLINE INDENT ItemList<X> DEDENT
```

<a id="s-F-4-3"></a>

### F.4.3. 区切りと継続

区切りは()、[]、Origin{}、型位置の型引数・ジェネリック入力&lt;>。比較&lt;は開かず、式内型引数は改行不可。区切り内継続は有効本体・チェーン段から開いた区切りごと一段下げ、対応閉じは開始行または内容に揃えられる。

区切り外では式は先頭.チェーン行だけ、ヘッダーは先頭->行だけで継続する。末尾二項演算子・=で継続せず不完全式エラーとなり、先頭二項演算子も前行を継がない。区切り内かつ入れ子本体外では先頭二項演算子で継続でき、同じ要素に前オペランドがあれば先頭+/-は二項となる。

先頭->は文法上まだ->を期待する関数・アクセサーヘッダーを、基準の一段下でだけ継続する。基準は変えず、その行の区切り内容はさらに一段下。次の有効行が->行と同じか上なら本体または次項目を開始する。

```kimi
let ok = (ready
    and enabled)        // Continues inside the parentheses
let diff = (a
    - b)                // Binary subtraction
let total = first +     // Error: incomplete; a line end does not continue outside delimiters
    second
```

<a id="s-F-4-4"></a>

### F.4.4. ヘッダー式と区切り領域

ヘッダー式は本体・分岐リストで終わるif/else if/while/require条件、match Subject、forのin式、分岐ガード。内部の本体付き式（if、match、for、while、loop、do、ラベル構文、匿名関数）は、そのヘッダー内で開いた区切り（括弧、引数、タプル・配列・Dictionary要素）内だけに置ける。パラメーター既定値はヘッダー式ではない。複数行継続もその区切り内だけで、構文全体を囲んでもヘッダーは継続しない。

区切り領域は各括弧式、括弧・角括弧内の各引数・要素（閉じ・区切りまで）、字下げ本体の各項目、各match 分岐で始まる。ラベル、転送オペランド、連続=>は開始しない。単一項目本体内の同領域で本体を持つ構文（unsafe、defer、require elseを含む）も単一項目形式が必要。match 分岐リストは実行本体でなく各分岐が領域を開始するので、分岐の字下げ本体は使える。同領域の単一if/else if/else本体内のifは、else有無を問わず全体を括弧で囲む。文は式として囲めず、必要括弧欠落・対応なしelseは構文エラー。

```kimi
if items.any(func (x: i32) => x > 0) => work()   // The argument list delimits the body
if (if a => b else => c) => work()                 // Grouped
let value = if a => (if b => 1 else => 2) else => 3
if do => ready() => work()                         // Error: body-bearing expression in the header
defer => unsafe
    releaseRaw(pointer)                            // Error: unsafe needs a single-item body here
```

<a id="s-F-4-5"></a>

### F.4.5. 分岐接続

BranchJoin・RequireJoinはNEWLINE?。elseは項目を始められず曖昧さはない。else/else ifは単一項目本体の終了物理行か次有効行に続く。字下げ本体後はその行のdedentで先に閉じる。独立行の節は最初のif基準に揃え、間に別項目を挟まない。requireのelseは条件と同行か次有効行でrequireに揃える。

```kimi
func run(ready: bool, waiting: bool, valid: bool)
    if ready => start()
    else if waiting => wait()
    else
        stop()
    require valid else => return
```

<a id="s-F-4-6"></a>

### F.4.6. 付随リストと先頭ドット行

任意付随リスト（Origin節、格納アクセサー、Callable効果）の開始は、一段下の次有効行の先頭トークンが項目を始められるか（origin、アクセス語/get/set、effect）だけで決める。先頭.は開始しない。

match 分岐位置の.はCaseパターン、新実行本体の最初の項目の.は推論Case式を開始でき、ヘッダーは継続しない。それ以外で、直前項目より正確に一段下の単独.始まり行はメンバーチェーンを継続し、後続チェーン行も同段。範囲トークンは単独.ではない。期待型・名前解決を使わず決める。

```kimi
let view = makeView()
    .trimmed()          // Chain continuation, not an Origin clause

struct Counter
    var count: i32 = 0
        private set     // Starts the accessor list
```

<a id="s-F-5"></a>

## F.5. 型

Name/Nameはペア入力、length Nameは関数長さスロット。Name・括弧式のGenericArgumentは宣言スロット種別で分類し、使用から決めない。ArrayLength・ArrayElementTypeの_は初期化済みローカル注釈だけ。借用注釈の結合は[§3.3.2](#s-3-3-2)。ContainerPathがContract・関連型を名指せる位置は[§9.7.1](#s-9-7-1)・[§8.5](#s-8-5)、実行時Contractビューは[§8.12](#s-8-12)。BoundContainerQualifierは型側だけでAdaptation対象には使えない。Origin関連生成規則は[§F.8](#s-F-8)。

```ebnf
Type               := FunctionType | AnnotatedType
AnnotatedType      := SemanticsType "?"* BorrowOrigin?
FunctionType       := FunctionParameters "->" Type
FunctionParameters := "(" TaskSlot? TrailingList<Type>? ")"
SemanticsType      := Semantics "/" SemanticsType | TypeAtom
TypeAtom           := CoreType | "(" Type ")"
Semantics          := BuiltinSemantics | Name
BuiltinSemantics   := "owner" | "ref" | "uniq" | "obj" | "rc" | "arc" | "objref" | "objuniq" | "raw"
CoreType           := NamedType | UnitType | TupleType | FixedArrayType
UnitType           := "(" ")"
TupleType          := "(" Type "," TrailingList<Type>? ")"
FixedArrayType     := "[" ArrayLength "of" ArrayElementType "]"
ArrayElementType   := Type | "_"
ArrayLength        := "_" | IntegerLiteral | ConstantName | "(" LengthExpression ")"
ConstantName       := "::"? Name ("." Name)*
LengthExpression   := LengthProduct (("+" | "-") LengthProduct)*
LengthProduct      := LengthUnary (("*" | "/" | "%") LengthUnary)*
LengthUnary        := ("+" | "-") LengthUnary | IntegerLiteral | ConstantName
                    | "(" LengthExpression ")"
NamedType          := NamedReference OriginApplication?
NamedReference     := ContainerPath OriginBindingSet?
ContainerPath      := "::"? TypeSegment PathSuffix*
                    | BoundContainerQualifier PathSuffix+
PlainContainerPath := "::"? TypeSegment ("." TypeSegment)*
BoundContainerQualifier := "(" ContainerPath OriginBindingSet ")"
PathSuffix         := "." TypeSegment | "." "(" ContractReference ")" "." Name
TypeSegment        := TypeName TypeArguments?
TypeName           := Name | PrimitiveType | "Self"
PrimitiveType      := "isize" | "usize" | "i8" | "i16" | "i32" | "i64" | "i128"
                    | "u8" | "u16" | "u32" | "u64" | "u128" | "f32" | "f64"
                    | "bool" | "char" | "string"
TypeArguments      := "<" TrailingList<GenericArgument> ">"
GenericArgument    := Type | ArrayLength
GenericParameters  := "<" TrailingList<GenericParameter> ">"
GenericParameter   := Name | Name "/" Name | "length" Name
```

```kimi
func first<T>(values: ref/Array<T>) -> ref/T during values
    return values[0]@ref

let grid: [2 of [3 of i32]] = [2 of [3 of 0]]
let check: (i32) -> bool = func (x: i32) => x > 0
let maybe: ref/i32? = Option<ref/i32>.None
```

<a id="s-F-6"></a>

## F.6. 宣言

<a id="s-F-6-1"></a>

### F.6.1. ソース単位、コンテナー、属性

ソース直下のlet/varはLocalBinding。コンテナー項目、入れ子、省略・空本体は[§6.1.1](#s-6-1-1)。enum本体は選択後も非空。ルート特殊化は同文書実行スコープの関数を対象とする。別名はルートの他項目より前だけで、=>は参照を導入し本体ではない。修飾子順は[§F.3.3](#s-F-3-3)。

#if/#switch/#caseはディレクティブ、#の後がLu始まりNameなら属性、その他#Nameはエラー。属性の位置・対象は[§6.7](#s-6-7)。

```ebnf
SourceUnit           := ItemList<SourceItem>?
SourceItem           := AliasDeclaration | Declaration | ExecutableItem | Directive<SourceItem>
Declaration          := AttributePrefix? UnattributedDeclaration
UnattributedDeclaration := GroupDeclaration | RootGroupDeclaration | StructureDeclaration
                      | EnumDeclaration | ContractDeclaration | FunctionDefinition
                      | ForeignFunctionDeclaration | SpecializationDeclaration
                      | StoredPropertyDeclaration | ComputedDeclaration
                      | ConstructorDeclaration | DropDeclaration
Attribute            := "#" AttributeName ("(" TrailingList<Argument>? ")")?
AttributeName        := ? a Name whose first character has General Category Lu, [§2.5](../02-source-and-lexical-structure.md#25-names) ?
AttributePrefix      := Attribute (Attribute | NEWLINE)* ("#" "if" CompileCondition NEWLINE)?   // the Attributes attach to the one declaration the #if controls, [§6.7](../06-declarations-and-containers.md#67-attributes)
Access               := "public" | "internal" | "private" | "protected"
                      | "protected" "internal" | "private" "protected"
QualifiedName        := Name ("." Name)*
AliasDeclaration     := "alias" (Name "=>")? ContainerReference OriginClauses?
GroupDeclaration     := Access? "group" Name ContainerBody
RootGroupDeclaration := Access? "rootgroup" QualifiedName ContainerBody
StructureDeclaration := Access? "open"? "struct" Name GenericParameters? OriginHeader?
                        BaseClause? ContainerBody
BaseClause           := ":" NamedReference
ContainerBody        := IndentedList<ContainerItem>?
ContainerItem        := Declaration | ConstraintClause | OriginRelation
                      | AssociatedTypeSpecification | ConditionalConformance
                      | Directive<ContainerItem>
EnumDeclaration      := Access? "enum" Name GenericParameters? OriginHeader?
                        IndentedList<EnumItem>
EnumItem             := EnumCase | AttributedFunctionDefinition | SpecializationDeclaration
                      | ConstraintClause | OriginRelation | AssociatedTypeSpecification
                      | ConditionalConformance | Directive<EnumItem>
EnumCase             := Name ("(" TrailingList<Type> ")")? OriginClauses?
```

```kimi
rootgroup App.Tools
    #Test
    func check() => ()
    // #test              // Error: an Attribute Name starts with a Lu letter
```

<a id="s-F-6-2"></a>

### F.6.2. Contract、要件、適合

要件isは右側orまで取り込み、直後notは全体を否定する。T is not A or BはT is not (A or B)。BorrowOrigin後の外側andは要件に属する（T is ref/U during a and Copy）。制約・効果・Origin関係の配置は[§7.5](#s-7-5)、[§8.2](#s-8-2)、[§8.11.1](#s-8-11-1)、関連型は[§8.5](#s-8-5)、条件付き適合は[§8.8](#s-8-8)。

```ebnf
ContractDeclaration  := Access? "contract" Name GenericParameters? ContractParentList?
                        IndentedList<ContractItem>?
ContractParentList   := ":" ContractReference ("," ContractReference)*
ContractReference    := ContainerReference
ContainerReference   := NamedReference
ContractSelector     := ContainerPath | "(" ContractReference ")"
ContractItem         := FunctionRequirement | PropertyRequirement | AssociatedTypeDeclaration
                      | AssociatedTypeSpecification | EffectSpecification | ConstraintClause
                      | Directive<ContractItem>
FunctionRequirement  := "unsafe"? "func" Name GenericParameters?
                        ParameterList<RequirementParameter> ("->" Type)?
                        RequirementConstraints?
RequirementParameter := ParameterName ":" Type | ReceiverShorthand
RequirementConstraints := IndentedList<ConstraintClause | OriginRelation | EffectClause>
EffectClause         := "effect" EffectBound
EffectSpecification  := "effect" ContractSelector "." Name EffectBound
EffectBound          := "confined"
AssociatedTypeDeclaration := "associate" Name OriginParameters? ("is" IsRequirement)?
                             FormationType? IndentedList<OriginRelation>?
FormationType        := "for" Type
AssociatedTypeSpecification := "associate" AssociatedTypeName OriginParameters?
                               "is" IsRequirement OriginClauses?
AssociatedTypeName   := Name | ContractSelector "." Name
AssociatedTypeReference := TypeQualifier "." Name OriginApplication?
                         | TypeQualifier "." "(" ContractReference ")" "." Name OriginApplication?
TypeQualifier        := ContainerPath
ConditionalConformance := "Self" "is" ContractReference "when" ConformanceConditions
                          IndentedList<ConditionalImplementationItem>?
ConformanceConditions := ConformanceCondition ("," ConformanceCondition)*
ConformanceCondition := ConstraintSubject "is" ConformanceRequirement
ConformanceRequirement := ConformanceAtom ("and" ConformanceAtom)*
ConformanceAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" ConformanceRequirement ")"
ConditionalImplementationItem := AttributedFunctionDefinition
                      | AttributePrefix? ComputedDeclaration
                      | AssociatedTypeSpecification
                      | Directive<ConditionalImplementationItem>
ConstraintClause     := ConstraintSubject "is" IsRequirement CallableEffects?
CallableEffects      := IndentedList<EffectClause>
ConstraintSubject    := Name | "Self" | AssociatedTypeReference
IsRequirement        := "not" Requirement | PositiveRequirement
PositiveRequirement  := RequirementAtom ("and" RequirementUnary)* ("or" RequirementAnd)*
Requirement          := RequirementAnd ("or" RequirementAnd)*
RequirementAnd       := RequirementUnary ("and" RequirementUnary)*
RequirementUnary     := "not" RequirementUnary | RequirementAtom
RequirementAtom      := CallableRequirement | Type | Semantics | SemanticsCategory
                      | "(" Requirement ")"
SemanticsCategory    := "value" | "valueborrow" | "object" | "objectborrow"
                      | "borrow" | "owning" | "reference"
CallableRequirement  := "Callable" "<" (CallableReceiver ",")? FunctionType ">"
CallableReceiver     := "ref" | "uniq" | "owner"
```

```kimi
contract Source
    associate Element
    func read(self: uniq/Self) -> Element

func apply<F>(f: ref/F, x: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return f(x)
```

<a id="s-F-6-3"></a>

### F.6.3. 関数、パラメーター、束縛

関数本体先頭領域は制約（効果リスト込み）、Origin関係、元virtualの効果節を順不同で持てる。文法は項目として認め、位置違反・アクセサー・破棄制限は[§7.5](#s-7-5)が定める。ReceiverShorthandは[§7.3](#s-7-3)でレシーバを許す位置だけ。!境界は[§7.2.1](#s-7-2-1)、taskリストは[§24.2.1](#s-24-2-1)。

外部宣言は本体なしで[§22.6](#s-22-6)の位置だけ。特殊化は[§8.13](#s-8-13)、構築は[§6.3](#s-6-3)、dropは[§16.3](#s-16-3)。func後のNameは関数定義、(・[は関数式を開始する。

```ebnf
FunctionHeader       := Access? FunctionModifier? "func" Name GenericParameters?
                        ParameterList<Parameter> ("->" Type)?
FunctionModifier     := "unsafe" | "virtual" | "override"
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader   // with #LibraryImport, [§22.6](../22-core-execution-and-foreign-functions.md#226-foreign-function-imports)
FunctionBody         := Body<FunctionItem>
FunctionItem         := Expression | LocalBinding | AttributedFunctionDefinition | Statement
                      | ConstraintClause | OriginRelation | EffectClause
                      | Directive<FunctionItem>
SpecializationDeclaration := "specialize" "func" Name TypeArguments
                             "(" TaskSlot? TrailingList<SpecializationParameter>? ")"
                             ("->" Type)? ExecutableBody
SpecializationParameter := Name ("=>" Name)? ":" Type | ReceiverShorthand
ConstructorDeclaration := Access? "init" ParameterList<Parameter> BaseInitializer? FunctionBody
BaseInitializer      := ":" "base" "(" TaskSlot? TrailingList<Argument>? ")"
DropDeclaration      := "drop" ExecutableBody
ParameterList<P>     := "(" TaskSlot? (TrailingList<P> | List<P>? "!" TrailingList<P>)? ")"
TaskSlot             := "task" ";"
Parameter            := Attribute* ParameterCore
ParameterCore        := ParameterName ":" Type ("=" Initializer)? | ReceiverShorthand
ParameterName        := Name ("=>" Name)?
ReceiverShorthand    := "self"
LocalBinding         := ("let" | "var") Name (":" Type)? ("=" Initializer)? OriginClauses?
Initializer          := "noinit" | Expression
```

```kimi
func find(task; value: i32 ! start: i32, end: i32) -> isize => 0
func scale(value: f64, by => factor: f64 = 2.0) -> f64 => value * factor

func fill()
    unsafe
        var buffer: [4 of u8] = noinit
        buffer[0] = 1
func f(x: i32 = noinit) => x       // Error: noinit is only for a local var
```

<a id="s-F-7"></a>

## F.7. プロパティ

各アクセサーは高々一回で順不同。省略レシーバ・種類・アクセサー規則は[§11.3](#s-11-3)、要件は[§11.9](#s-11-9)。

```ebnf
StoredPropertyDeclaration := Access? ("let" | "var") Name (":" Type)? ("=" Initializer)?
                             IndentedList<OriginRelation | StoredAccessor>?
ComputedDeclaration  := Access? "computed" Name ":" Type
                        IndentedList<OriginRelation | CustomAccessor>
StoredAccessor       := Access? ("get" | "set") | CustomAccessor
CustomAccessor       := Access? (GetterSignature | SetterSignature) AccessorBody
GetterSignature      := "get" "(" AccessorReceiver? ")" "->" Type
SetterSignature      := "set" "(" (AccessorReceiver ",")? "value" ":" Type ")" "->" UnitType
AccessorReceiver     := "self" ":" Type
AccessorBody         := Body<FunctionItem>
PropertyRequirement  := "property" Name ":" Type
                        ("has" RequiredAccessor ("," RequiredAccessor)*
                         | IndentedList<RequiredSignature>)
RequiredAccessor     := "get" | "set"
RequiredSignature    := (GetterSignature | SetterSignature) OriginClauses?
```

```kimi
struct Account
    var balance: i32 = 0
        get
        private set
    computed doubled: i32
        get(self: ref/Self) -> i32 => self.balance * 2

contract Named
    property name: string has get
```

<a id="s-F-8"></a>

## F.8. Origin

OriginHeaderはstruct/enumのジェネリック入力後。OriginBindingSetは名前付き型後に一集合を命名する。OriginApplicationは型内の関連型名後だけで、呼出し・タプル・括弧グループではない。during後の共通部分はduring (a and b)と括弧で囲むが、関係の両辺は完全OriginExpression。Origin関係は宣言メタデータで、所有宣言の一段下か先頭領域へ置く。

```ebnf
OriginHeader       := "{" TrailingList<Name> "}"
OriginBindingSet   := "{" Name ","? "}"
OriginApplication  := "(" List<OriginAtom> ")"
OriginParameters   := "(" List<Name> ")"
BorrowOrigin       := "during" OriginAtom
OriginExpression   := OriginAtom ("and" OriginAtom)*
OriginAtom         := Name ("." Name)? | "static" | "(" OriginExpression ")"
OriginRelation     := "origin" OriginExpression ("==" | "outlives") OriginExpression
OriginClauses      := IndentedList<OriginRelation>
```

```kimi
struct View<T> {source}
    let items: ref/Array<T> during source

func pick<T>(a: ref/T, b: ref/T) -> ref/T during a
    origin b outlives a
    T is Copy                  // Still in the leading region
    return a
```

<a id="s-F-9"></a>

## F.9. 式

<a id="s-F-9-1"></a>

### F.9.1. 生成規則

型修飾子後の.initは常にConstructionExpression。修飾enum Caseは通常Postfixとして解決する。Self・基本型名の.はSelf.marker()、isize.MaxValueなど静的メンバー選択。実行時isはNamedCoreType一つを取り、外側and/orを取り込まない。要件isは固有文脈でIsRequirementを使う。return・exit・yieldのオペランドは完全なExpression。

```ebnf
Expression       := Assignment
Assignment       := RangeExpression (AssignmentOperator Assignment)?
AssignmentOperator := "=" | "+=" | "-=" | "*=" | "/=" | "%="
                    | "<<=" | ">>=" | "&=" | "^=" | "|="
RangeExpression  := OrExpression | OrExpression? ".." OrExpression?
                  | OrExpression? "..=" OrExpression
OrExpression     := AndExpression ("or" AndExpression)*
AndExpression    := Comparison ("and" Comparison)*
Comparison       := BitOr (("<" | "<=" | ">" | ">=" | "==" | "!=") BitOr)?
                  | BitOr "is" "not"? NamedCoreType
NamedCoreType    := ? a ContainerPath with no Origin, "during" or binding set, also inside its
                      Type arguments, [§13.6.4](../13-operators-and-assignment.md#1364-runtime-is-tests) ?
BitOr            := BitXor ("|" BitXor)*
BitXor           := BitAnd ("^" BitAnd)*
BitAnd           := Shift ("&" Shift)*
Shift            := Additive (("<<" | ">>") Additive)*
Additive         := Multiplicative (("+" | "-") Multiplicative)*
Multiplicative   := Try (("*" | "/" | "%") Try)*
Try              := "try" Try | Adapted
Adapted          := Prefix ("@" (ClosedTarget PostfixSuffix* | OpenTarget))*
Prefix           := ("+" | "-" | "not" | "*" | "^" | "++" | "--") Prefix | Postfix
Postfix          := Primary PostfixSuffix*
PostfixSuffix    := "." (Name | TupleIndex)
                  | "." "(" ContractReference ")" "." Name
                  | "(" TaskSlot? TrailingList<Argument>? ")"
                  | AdjacentTypeArguments | "[" Expression "]" | "++" | "--"
                  | "@" "follow"
AdjacentTypeArguments := TypeArguments   // only as decided by [§F.9.3](#f93-type-arguments-or-less-than)
Argument         := (Name ":")? Expression
Primary          := "::"? Name | ("Self" | PrimitiveType) "." Name | Literal
                  | "(" Expression ")" | TupleExpression | ArrayExpression
                  | DictionaryExpression | FunctionExpression
                  | LabelPrefix? LabelableConstruct | Transfer
                  | BaseInvocation | ConstructionExpression | BoundContainerExpression
                  | InferredCaseExpression | CompositionRootExpression
BaseInvocation   := "base" "." Name AdjacentTypeArguments?
                    "(" TaskSlot? TrailingList<Argument>? ")"
ConstructionExpression := (PlainNamedType | BoundContainerQualifier) "." "init"
                          "(" TaskSlot? TrailingList<Argument>? ")"
PlainNamedType   := ContainerPath
BoundContainerExpression := BoundContainerQualifier
                            ("." Name | "." "(" ContractReference ")" "." Name)
InferredCaseExpression := "." Name ("(" TrailingList<Expression> ")")?
TupleExpression  := "(" Expression "," TrailingList<Expression>? ")"
ArrayExpression  := "[" TrailingList<Expression>? "]" | "[" ArrayLength "of" Expression "]"
DictionaryExpression := "[" ":" "]" | "[" TrailingList<DictionaryEntry> "]"
DictionaryEntry  := Expression ":" Expression
FunctionExpression := "func" CaptureList? "(" TaskSlot? TrailingList<AnonymousParameter>? ")"
                      ("->" Type)? ExecutableBody
AnonymousParameter := Name (":" Type)?
CaptureList      := "[" TrailingList<Capture>? "]"
Capture          := "var"? Name ("@" ("move" | "ref" | "uniq"))?
CompositionRootExpression := "$" "abort" "(" Expression ")"
                           | "$" "tryWrite" "(" Expression "," StringLiteral ")"
```

```kimi
struct Counter
    var count: i32 = 0
    func total(self) -> i32 => self.count + Self.marker()
    func marker() -> i32 => 1

let entry = Pair<i32, string>.init(1, "one")
let scaled = apply(func [factor] (x: i32) => x * factor)
let table = [1: "one", 2: "two"]
```

<a id="s-F-9-2"></a>

### F.9.2. 優先順位と結合

上ほど強く結合し、[§F.9.1](#s-F-9-1)がこの表を実装する。

| 段 | 演算・構文 | 結合 |
| --- | --- | --- |
| 1 | .name、(...)、&lt;Types>、[...]、@follow、後置++ -- | 左から後置チェーン |
| 2 | 前置+ - not * ^ ++ -- | 右 |
| 3 | @follow以外の@（@raw、@raw/U、@wrap&lt;U>、@bits&lt;U>を含む） | 左 |
| 4 | 前置try | 右 |
| 5 | * / % | 左 |
| 6 | + - | 左 |
| 7 | &lt;&lt; >> | 左 |
| 8 | & | 左 |
| 9 | ^ | 左 |
| 10 | \| | 左 |
| 11 | &lt; &lt;= > >= == !=、実行時is/is not | 非結合 |
| 12 | and | 左 |
| 13 | or | 左 |
| 14 | .. ..= | 非結合 |
| 15 | =と複合代入 | 右 |

括弧なし比較連鎖a &lt; b &lt; c・a &lt; b == flagは構文エラー。段2の前置演算子はtryを直接取れず、-(try x)と記す。

| 記述 | グループ化 |
| --- | --- |
| flags & mask == 0 | (flags & mask) == 0 |
| -x@i64 | (-x)@i64 |
| *p@raw | (*p)@raw（ポインター先のアドレス） |
| r@follow.x | (r@follow).x |
| try x@move * 2 | (try (x@move)) * 2 |
| not ready and n != 0 | (not ready) and (n != 0) |
| start + 1..end - 1 | (start + 1)..(end - 1) |

<a id="s-F-9-3"></a>

### F.9.3. 型引数か小なりか

式内のName後（後置、base.f後、@対象型名後を含む）の&lt;が型引数を始めるのは、(1)Name・Self・基本型名に空白・コメントを挟まず続き、(2)同じ行の後続トークンが対応>で閉じるTypeArgumentsを形成（>始まりトークンは分割）、(3)>の次が(、.、または式開始不能トークン（)、,、]、==、@、=>、行末など）、の全条件を満たす場合だけ。

他は小なり。名前解決せずトークンだけで決め、後の失敗でもやり直さない。型注釈、is対象、@wrap/@bits型引数など他の位置では型名後&lt;は常に型引数。open Adaptation対象後の(・[はこの判定でも構文エラー。

```kimi
let a = f<i32>(x)        // Type arguments
let b = i<n              // Less-than: no matching >
let c = (p<q, r>(s))     // The generic call p<q, r>(s)
let d = (p < q, r > s)   // Two comparisons: the first < is preceded by a space
let e = x@Box<i32> == y  // Type arguments: == cannot begin an expression
let g = (x@Box<i32>) + 1 // Grouped: + could begin an operand
```

<a id="s-F-9-4"></a>

### F.9.4. Adaptation対象

後置の.・呼出し・添字・++・--・@followを続けられるのはClosedTargetだけ。OpenTarget後の(・[はエラーで、.は修飾型headを続ける。(x@T).name、(f@T)()、(a@T)[0]と囲む。

@後のName・BuiltinSemanticsに/が続けば空白によらずSemantics接頭辞なのでa@T / bはT/b対象。move・copy・followは名前でなく、followは対象でもない。move/copy後/は構文エラー、follow後/は除算。括弧なし対象は外側関数型矢印を取らず、括弧なし型headはOriginApplication・BorrowOriginを消費しない。x@(Self.Item(a))のように囲む。裸rawはアドレス操作。各意味は[§13.5.1](#s-13-5-1)。

```ebnf
ClosedTarget     := "move" | "copy" | BuiltinSemantics          // not followed by "/"
                  | (Semantics "/")* DelimitedTarget
OpenTarget       := "wrap" TypeArguments | "bits" TypeArguments
                  | (Semantics "/")* "::"? TypeSegment PathSuffix* OriginBindingSet? "?"*
                  | (Semantics "/")* DelimitedTarget "?"+
DelimitedTarget  := UnitType | "(" Type ")" | TupleType | "[" ArrayLength "of" Type "]"
```

```kimi
let n = tasks@uniq.length         // Closed target: the postfix chain continues
let x = (point@Point).x           // Open target: group before selection
let w = big@wrap<u8>
let e = items@Array<i32>[0]       // Error: group the adaptation first
```

<a id="s-F-10"></a>

## F.10. 文、本体、パターン

<a id="s-F-10-1"></a>

### F.10.1. 本体と文

SingleItemは宣言・ディレクティブを含めない。TestVerificationは独立項目でオペランドにはならない。本体意味・非空規則は[§14.2](#s-14-2)、[§14.2.1](#s-14-2-1)。

```ebnf
Body<Item>       := "=>" SingleItem | IndentedList<Item>
SingleItem       := Expression | Statement
ExecutableBody   := Body<ExecutableItem>
ExecutableItem   := Expression | LocalBinding | AttributedFunctionDefinition | Statement
                  | Directive<ExecutableItem>
Statement        := UnsafeStatement | DeferStatement | RequireStatement | DiscardStatement
                  | TestVerification
UnsafeStatement  := "unsafe" ExecutableBody
DeferStatement   := "defer" ExecutableBody
RequireStatement := "require" Expression RequireJoin "else" ExecutableBody
RequireJoin      := NEWLINE?
DiscardStatement := "_" "=" Expression
TestVerification := "$" ("expect" | "require") "(" Expression ("," "message" ":" Expression)? ")"
```

```kimi
defer => close(file)
_ = compute()
unsafe
    releaseRaw(pointer)
```

<a id="s-F-10-2"></a>

### F.10.2. 制御構文

label、名前、:、構文キーワードは同じ物理行。match 分岐リストは非空でディレクティブ禁止。括弧ForBindingはタプル規則で(a,b)はペア、(a,)は1要素、(x)は禁止。

to Labelとオペランド開始は転送キーワード同行。次が行末、EOF、レイアウト境界、または外側の,・)・]・else・=>・入力リスト!の場合に限りオペランドを省略する。:・andは省略を示さない。ラベルとオペランド先頭は空白かブロックコメントで区切り、exit to work (x)は可、exit to work(x)は不可。

```ebnf
LabelPrefix        := "label" Name ":"
LabelableConstruct := IfExpression | MatchExpression | ForExpression | WhileExpression
                    | LoopExpression | DoExpression
IfExpression       := "if" Expression ExecutableBody
                      (BranchJoin "else" "if" Expression ExecutableBody)*
                      (BranchJoin "else" ExecutableBody)?
BranchJoin         := NEWLINE?
MatchExpression    := "match" Expression IndentedList<MatchArm>
MatchArm           := Pattern ("if" Expression)? ExecutableBody
ForExpression      := "for" ForBinding "in" Expression ExecutableBody
ForBinding         := ForSlot | "(" ForSlot "," TrailingList<ForSlot>? ")"
ForSlot            := Name | "var" Name | "_"
WhileExpression    := "while" Expression ExecutableBody
LoopExpression     := "loop" ExecutableBody
DoExpression       := "do" ExecutableBody
Transfer           := "return" Expression?
                    | "exit" ("to" Name)? Expression?
                    | "continue" ("to" Name)?
                    | "yield" ("to" Name)? Expression?
```

```kimi
label outer: for (key, value) in table
    if key == 0 => continue to outer
    match value
        .Some(let v) if v > 0 => use(v)
        _ => ()
for (only,) in singles => use(only)
// for (x) in xs => use(x)            // Error: write `for x in xs`
```

<a id="s-F-10-3"></a>

### F.10.3. パターン

CaseReference修飾子はenum自身のOriginに束縛集合・duringを記さない。型引数内の型は完全な型。意味は[§14.8.1](#s-14-8-1)。

```ebnf
Pattern          := "_" | LiteralPattern | BindingPattern | CasePattern
                  | UnitPattern | TuplePattern | GroupedPattern
BindingPattern   := ("let" | "var") Name
CasePattern      := CaseReference ("(" TrailingList<Pattern> ")")?
CaseReference    := PlainContainerPath "." Name | "." Name
UnitPattern      := "(" ")"
TuplePattern     := "(" Pattern "," TrailingList<Pattern>? ")"
GroupedPattern   := "(" Pattern ")"
LiteralPattern   := "true" | "false" | "-"? IntegerLiteral | CharLiteral
                  | NonInterpolatedString
NonInterpolatedString := ? a StringLiteral without Interpolation, [§14.8.1](../14-control-flow.md#1481-patterns) ?
```

```kimi
match outcome
    .Done((let a, 0)) => use(a)
    Outcome.Failed(let e) => report(e)
    _ => ()
```

<a id="s-F-11"></a>

## F.11. コンパイル時ディレクティブ

#case _は最後の分岐に高々一つ。Itemは周囲のリスト種別を保ち、禁止項目をディレクティブで許すことはない。上記文法がDirective&lt;…>を記すリストだけに置ける。配置・選択は[§19.1](#s-19-1)。switch 分岐リスト外のcaseはエラー。除外構文も同じ文法で解析する。

```ebnf
Directive<Item>       := IfDirective<Item> | SwitchDirective<Item>
IfDirective<Item>     := "#" "if" CompileCondition (NEWLINE Item | IndentedList<Item>)
SwitchDirective<Item> := "#" "switch" IndentedList<SwitchArm<Item>>
SwitchArm<Item>       := "#" "case" (CompileCondition | "_") IndentedList<Item>
CompileCondition      := CompileAnd ("or" CompileAnd)*
CompileAnd            := CompileComparison ("and" CompileComparison)*
CompileComparison     := CompileUnary (("==" | "!=") CompileUnary)?
CompileUnary          := "not" CompileUnary | CompileAtom
CompileAtom           := "true" | "false" | ("+" | "-")? IntegerLiteral | PlainString | Name
                       | "(" CompileCondition ")"
PlainString           := ? a StringLiteral without Interpolation, [§19.2](../19-compile-time-directives.md#192-conditions) ?
```

```kimi
#if windows
alias Kimi.Windows

func platformName() -> string
    #switch
        #case windows
            return "windows"
        #case _
            return "other"
```

<a id="check-output-schema"></a>

# check出力JSONスキーマ

[§23.3](#s-23-3)の機械可読契約。説明文を含むJSONスキーマは原文どおり収録する。

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://kimigayo.dev/schemas/check-output.schema.json",
  "title": "kimi check output (kimi.check/3)",
  "description": "One command result with an ordered array of check-unit results, including a singleton for one selected project. Input-selection failure has no units. Each result's diagnostic, related-location and repair source indices refer only to that result's sources. Spans are UTF-16 code units; ranges are zero-based lines and characters. Cancellation before publication emits no document. SPEC 23.2.3 defines aggregation, ordering and publication.",
  "type": "object",
  "required": ["schema", "compiler", "outcome", "accepted", "sources", "diagnostics", "units"],
  "additionalProperties": false,
  "properties": {
    "schema": { "const": "kimi.check/3" },
    "compiler": { "type": "string", "description": "The compiler build identity for all units." },
    "outcome": { "$ref": "#/$defs/outcome", "description": "Selection failure: Blocked for Input, Faulted for Internal. After selection: Faulted if any unit is Faulted, otherwise Blocked if any is Blocked, otherwise Completed." },
    "accepted": { "type": "boolean", "description": "True exactly when selection succeeds with at least one unit and every selected unit is accepted." },
    "sources": { "$ref": "#/$defs/sources", "description": "Sources of command-level selection diagnostics; empty after successful selection." },
    "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" }, "description": "Selection failures only; an Input Error for Blocked or an Internal Error for Faulted. Empty after successful selection; unit diagnostics are not duplicated here." },
    "units": { "type": "array", "items": { "$ref": "#/$defs/unitResult" }, "description": "Every selected unit in the path order of SPEC 23.2.3, including Blocked and Faulted units. Empty only when selection fails." }
  },
  "$defs": {
    "outcome": { "enum": ["Completed", "Blocked", "Faulted"] },
    "unitResult": {
      "type": "object",
      "required": ["unit", "outcome", "accepted", "testPresence", "sources", "diagnostics"],
      "additionalProperties": false,
      "properties": {
        "unit": {
          "type": "object",
          "required": ["project", "target", "mode", "debug"],
          "additionalProperties": false,
          "properties": {
            "project": { "type": "string", "description": "The normalized selected project path, or implicit-source path." },
            "target": { "type": "string", "description": "The selected target; when no single target could be selected, the --Target value or the empty string." },
            "mode": { "enum": ["Product", "Test"], "description": "Product: the kimi check result; Test: the test-preparation result." },
            "debug": { "type": "boolean" }
          }
        },
        "outcome": { "$ref": "#/$defs/outcome", "description": "Completed: checked, with or without errors; Blocked: an input or configuration could not be established; Faulted: the compiler failed internally." },
        "accepted": { "type": "boolean", "description": "True exactly when this unit's outcome is Completed and none of its diagnostics has severity Error. A rejected unit has at least one Error." },
        "testPresence": { "enum": ["Yes", "No", "Unknown"], "description": "Product unit: Yes when the project's own sources contain a `#Test` function, No when they contain none and all parsed without source errors, otherwise Unknown; always Unknown for a Test unit." },
        "sources": { "$ref": "#/$defs/sources" },
        "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" } }
      }
    },
    "sources": {
      "type": "array",
      "description": "Every source this result's diagnostics name, in the order read; indices are local to this result.",
      "items": {
        "type": "object",
        "required": ["path", "isInput"],
        "additionalProperties": false,
        "properties": {
          "path": { "type": "string", "description": "The display path." },
          "isInput": { "type": "boolean", "description": "True for an input the check read from a file or an open document; false for a built-in or generated source." },
          "sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$", "description": "The hash of the input's bytes, present for an input that could be read." }
        }
      }
    },
    "span": {
      "type": "object",
      "description": "Start offset and length in UTF-16 code units; length 0 marks an insertion point.",
      "required": ["start", "length"],
      "additionalProperties": false,
      "properties": { "start": { "type": "integer", "minimum": 0 }, "length": { "type": "integer", "minimum": 0 } }
    },
    "position": {
      "type": "object",
      "required": ["line", "character"],
      "additionalProperties": false,
      "properties": { "line": { "type": "integer", "minimum": 0 }, "character": { "type": "integer", "minimum": 0 } }
    },
    "range": {
      "type": "object",
      "description": "Zero-based lines and UTF-16 characters, end excluded.",
      "required": ["start", "end"],
      "additionalProperties": false,
      "properties": { "start": { "$ref": "#/$defs/position" }, "end": { "$ref": "#/$defs/position" } }
    },
    "value": {
      "type": "object",
      "description": "One typed fact of a reason or repair.",
      "required": ["name", "kind", "value"],
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string" },
        "kind": { "enum": ["Number", "Enumeration", "Boolean", "Text", "Requirement", "Origin"] },
        "value": { "type": "string" },
        "origin": { "enum": ["expression", "borrow", "omitted", "closure"], "description": "Present exactly when kind is Origin, whose value names a fixed or finite Origin. expression: an Origin expression as written in a signature; borrow: the source text of a Borrow or capture item; omitted: the Type occurrence or pair binder declaration of an unnamed Origin; closure: a Closure's call receiver or inferred call result." },
        "elided": { "type": "boolean", "description": "True when the value was shortened for display." }
      },
      "if": { "properties": { "kind": { "const": "Origin" } } },
      "then": { "required": ["origin"] },
      "else": { "not": { "required": ["origin"] } }
    },
    "related": {
      "type": "object",
      "required": ["role", "source"],
      "additionalProperties": false,
      "properties": {
        "role": { "type": "string" },
        "source": { "type": "integer", "minimum": -1, "description": "Source table index, or -1 for a location outside every source." },
        "span": { "$ref": "#/$defs/span", "description": "Absent when the location is the whole source, or when source is -1." },
        "range": { "$ref": "#/$defs/range" },
        "label": { "type": "string" }
      }
    },
    "omission": {
      "type": "object",
      "description": "A part of the diagnostic that a display limit omitted.",
      "required": ["part"],
      "additionalProperties": false,
      "properties": { "part": { "type": "string" }, "count": { "type": "integer", "minimum": 1, "description": "The number of omitted items; absent when unknown." } }
    },
    "excerptLine": {
      "type": "object",
      "required": ["line", "text", "start", "length"],
      "additionalProperties": false,
      "properties": {
        "line": { "type": "integer", "minimum": 1, "description": "1-based display line." },
        "text": { "type": "string" },
        "start": { "type": "integer", "minimum": 0 },
        "length": { "type": "integer", "minimum": 0, "description": "Length of the marked text; 0 marks an insertion point." }
      }
    },
    "display": {
      "type": "object",
      "description": "Display data; never needed to interpret the diagnostic.",
      "required": ["excerpt"],
      "additionalProperties": false,
      "properties": { "range": { "$ref": "#/$defs/range" }, "excerpt": { "type": "array", "items": { "$ref": "#/$defs/excerptLine" } } }
    },
    "edit": {
      "type": "object",
      "required": ["source", "span", "text"],
      "additionalProperties": false,
      "properties": {
        "source": { "type": "integer", "minimum": 0, "description": "The source table index of an input." },
        "span": { "$ref": "#/$defs/span" },
        "range": { "$ref": "#/$defs/range" },
        "text": { "type": "string", "description": "The replacement text; empty for a deletion." },
        "replaced": { "type": "string", "description": "The bounded text the span covers, for display; absent for an insertion." }
      }
    },
    "requiredCondition": {
      "type": "object",
      "description": "A condition the check could not decide, with a phrase naming what must hold.",
      "required": ["condition", "phrase"],
      "additionalProperties": false,
      "properties": {
        "condition": { "$ref": "#/$defs/condition" },
        "phrase": { "type": "string" }
      }
    },
    "condition": {
      "enum": ["Take", "ExclusiveAccess", "UsageLegality", "Structure", "Selection"],
      "description": "Take: the Place can be moved; ExclusiveAccess: the lending point is exclusively writable; UsageLegality: later uses stay legal; Structure: names, cleanup order and control flow are unchanged; Selection: the qualified name selects what the use needs."
    },
    "repair": {
      "type": "object",
      "description": "One alternative structured edit. Verified conditions were established by the check; required conditions were not decided. Applying it yields a new input that only a new check validates.",
      "required": ["kind", "title", "edits", "verified", "required"],
      "additionalProperties": false,
      "properties": {
        "kind": {
          "enum": ["Repair.Transfer", "Repair.Borrow", "Repair.BorrowExclusively", "Repair.RemoveUnsafe", "Repair.ReplaceToken", "Repair.InsertToken", "Repair.PropagateFailure", "Repair.ExplicitDiscard", "Repair.Qualify", "Repair.AddPremise", "Repair.RemovePremise"]
        },
        "title": { "type": "string" },
        "facts": { "type": "array", "items": { "$ref": "#/$defs/value" } },
        "edits": { "type": "array", "minItems": 1, "items": { "$ref": "#/$defs/edit" } },
        "verified": { "type": "array", "items": { "$ref": "#/$defs/condition" } },
        "required": { "type": "array", "items": { "$ref": "#/$defs/requiredCondition" } }
      }
    },
    "diagnostic": {
      "type": "object",
      "required": ["code", "severity", "category", "message", "source"],
      "additionalProperties": false,
      "properties": {
        "code": { "type": "string" },
        "severity": { "enum": ["Error", "Warning", "Information", "Hint"] },
        "category": { "enum": ["Language", "Proof", "Unsupported", "Input", "Resource", "Internal"] },
        "message": { "type": "string" },
        "source": { "type": "integer", "minimum": -1, "description": "Source table index, or -1 for a location outside every source." },
        "span": { "$ref": "#/$defs/span", "description": "Absent when the location is the whole source, or when source is -1." },
        "label": { "type": "string" },
        "reason": { "type": "array", "items": { "$ref": "#/$defs/value" } },
        "related": { "type": "array", "items": { "$ref": "#/$defs/related" } },
        "note": { "type": "string" },
        "omissions": { "type": "array", "items": { "$ref": "#/$defs/omission" } },
        "display": { "$ref": "#/$defs/display" },
        "repairs": { "type": "array", "items": { "$ref": "#/$defs/repair" } }
      }
    }
  }
}
```
