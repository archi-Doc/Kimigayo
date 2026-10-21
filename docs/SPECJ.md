# Kimigayo 言語仕様（日本語訳）

翻訳元：`217139ae4ad045f82be3ee8008bf73bc8c0b8587`（2026年10月21日）。本書は、このリビジョンのSPEC.mdと各章・付録・検査出力スキーマ、および参照先の実装仕様とSTATUS.mdを、英語原文から新たに翻訳して1ファイルにまとめたものである。表記は[SPECJ_TERMINOLOGY.md](SPECJ_TERMINOLOGY.md)に従う。

原文のSPEC.mdは言語仕様の目次であり、規則は`spec/`以下の各章にある。[§1.3](#s1-3)が規範となる部分を定め、[付録F](#sf)が規範的な文法を定める。20・21章と付録Aは[実装仕様](#implementation-specification)に属する。[STATUS.md](#status)はコンパイラーの実装状況を記録する。

本書では原文の節番号、規則、条件、表、例を保つ。コード例と識別子・キーワードは原文のままとし、文法内の説明とJSONスキーマの説明文は訳す。収録した文書への参照は本書内のリンクに統合した。言語規則・実装契約の正式な根拠は英語原文であり、翻訳によって仕様や実装範囲を変更しない。実装状況は翻訳元リビジョン時点の記録である。

<a id="contents"></a>
## 目次

### 第I部　導入とソーステキスト

- [1. 概要](#s1)
- [2. ソースと字句構造](#s2)

### 第II部　型・値・列

- [3. 型と値](#s3)
- [4. 配列・添字・スライス](#s4)
- [5. 生ポインターとunsafeメモリー](#s5)

### 第III部　宣言と静的な意味

- [6. 宣言とコンテナー](#s6)
- [7. 関数と呼出し可能値](#s7)
- [8. ジェネリクス・制約・Contract](#s8)
- [9. 名前・シグネチャ・アクセス](#s9)
- [10. オーバーロード解決と推論](#s10)
- [11. プロパティ](#s11)

### 第IV部　式と制御フロー

- [12. 式](#s12)
- [13. 演算子と代入](#s13)
- [14. 制御フロー](#s14)

### 第V部　所有権・後処理・失敗

- [15. 所有権と寿命解析](#s15)
- [16. スコープ終了と破棄](#s16)
- [17. 失敗処理](#s17)

### 第VI部　プログラムとKimiライブラリー

- [18. モジュールと依存関係](#s18)
- [19. コンパイル時指示](#s19)
- [22. Kimi・プログラム実行・外部関数](#s22)

### 第VII部　コンパイラーサービス

- [23. コンパイラーサービス](#s23)
  - [検査出力スキーマ：コマンド結果と単位結果](#check-schema)

### 第VIII部　非同期実行

- [24. 一時停止と非同期タスク](#s24)

### 付録

- [付録A. コンパイラー要件](#sa)（実装仕様）
- [付録D. 保留している機能](#sd)
- [付録E. 用語](#se)
- [付録F. 文法](#sf)

<a id="implementation-specification"></a>
### Kimigayo実装仕様

原文のIMPL.mdは実装仕様の目次であり、プログラムやツールが観測できる初期実装の契約をまとめる。言語は[言語仕様](#contents)が定め、実装仕様によってその範囲を狭めることはない。

- [20. コンパイル設定](#s20)
- [21. レイアウト・ランタイム・コード生成](#s21)
- [付録A. コンパイラー要件](#sa)

### 参考資料

- [実装状況](#status)
- [翻訳元ファイル一覧](#sources)

---

<a id="s1"></a>
## 1. 概要

[仕様の索引](#contents)

<a id="s1-1"></a>
### 1.1. 目的

**Kimigayo**は、一貫性、速さ、単純さ、楽しさ、安全性を目指してゼロから作るプログラミング言語である。改訂では、以前の版との互換性よりも一貫性と言語の品質を優先する。

本仕様は言語を定義する。[実装仕様](#implementation-specification)は初期実装のビルド、ランタイム、コード生成の契約を定義し、[STATUS.md](#status)は実装済みの内容を記録する。

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
> **設計上の注記（この文を変更しないこと）**
>
> * `$`はマクロではない。Composition Rootである。

<a id="s1-2"></a>
### 1.2. 規約と表記

ユーザー定義の型、Contract、宣言コンテナーには慣例としてPascalCaseを使う。組込みの型キーワードは元の綴りを保つ。関数、フィールド、プロパティ、ローカル束縛、パラメーターなどの値名には、慣例としてcamelCaseを使う。

| 表記 | 用途 |
| --- | --- |
| `[]` | 配列とDictionaryの構築、固定長配列型、添字アクセス、スライス、キャプチャーリスト。 |
| `()` | 順序のあるグループ化。パラメーター、引数、タプル、Unit、関数型、条件、優先順位。 |
| `<>` | 型引数と関数の長さ引数（[§4.4](#s4-4)）。 |
| `{...}` | structまたはenumのOriginスキーマヘッダー、または名前付き型の後に置く新しい束縛集合名（[§15.3](#s15-3)）。本体やコレクションには使わない。借用の注釈には`during`を使う（[§3.3.2](#s3-3-2)）。 |
| `=` | 初期化、パラメーターの既定値、代入。取得はCopyとMoveに従う（[§3.5](#s3-5)）。 |
| `@` | 明示的な操作。Adaptation、`@move`、`@copy`、`@follow`（[§13.5](#s13-5)）。 |
| `->` | 関数または関数型の結果型。 |
| `=>` | 単一項目の実行本体、パラメーター名の対応付け、コンテナー別名の参照先。 |
| `:` | 名前と型、引数名と値、Dictionaryのキーと値、ラベルと構文要素を区切る。基底、Contractの親、`: base(...)`を導入する。実行本体には使わない。 |
| `#` | コンパイル時の構文要素。`#if`などの小文字のディレクティブ、`#Inline`などのPascalCaseの属性。 |
| `$` | 言語が提供するComposition Root操作（[§13.8](#s13-8)）。 |
| `;` | タスク区切りの`task;`のみ（[§24.2](#s24-2)）。 |

型は、Semantics、CoreまたはオブジェクトのView Target、Originを組み合わせたものである（[§3](#s3)）。

```kimi
group Geometry
    func area(width: i32, height: i32) -> i32
        let sides = [width, height]
        return sides[0] * sides[1]
```

<a id="s1-3"></a>
### 1.3. 規範性と用語

本仕様は言語バージョン`0.0.2`を定義する。

| 部分 | 位置付け |
| --- | --- |
| 第1〜19章、第22章、第24章、付録F（文法） | 規範的な言語規則。付録Fが唯一の規範文法である。 |
| 第23章と`schemas/check-output.schema.json` | 規範的なツール契約。プログラムの妥当性は変更しない。 |
| 付録DとE | 参考情報。リンク先の節を正とする。 |
| IMPL（第20〜21章、付録A） | 初期実装プロファイルの契約。プログラムの妥当性や意味は決定しない。 |

適合する実装は、妥当なプログラムだけを受け入れ、規定された動作を与える。未実装の妥当な形式には、代わりにUnsupported（[§23.1.1](#s23-1-1)）を報告してもよく、その実装は部分実装となる。不正なプログラムを受け入れたり、妥当なプログラムに別の意味を与えたりしてはならない。付録Dは本改訂の対象外の機能を列挙するものであり、構文や許可を与えない。

| 原文の表現 | 意味 |
| --- | --- |
| must / must not | 要件または禁止。違反がコンパイル時エラー、Abort、未定義動作のどれになるかは、担当する規則が定める。 |
| may | 記載されたすべての制約内での許可。 |
| should | 推奨。適合の条件ではない。 |
| implementation-defined（実装定義） | 規定の範囲内で実装が選び、その選択を文書化する。 |
| unspecified（未規定） | 規定の範囲内のどの結果も許され、文書化は不要。未定義動作ではない。 |
| undefined behavior（未定義動作） | その時点以降、プログラムの動作は定義されない。unsafeの約束への違反だけが原因になり得る（[§5](#s5)）。安全なコードだけでは起こらない。 |

限定のない平叙文の規則と命令形の要件は、規範的である。例は規則を説明するものであり、新しい規則は加えない。APIの例がシグネチャだけを示す場合でも、本体のない定義を許すものではない。各例は独立している。例に登場するアプリケーション定義の型やAPIは存在を仮定し、`// Error:`の行は意図的に規則に違反する。`text`ブロックはKimigayo以外の表記を示す。

---

<a id="s2"></a>
## 2. ソースと字句構造

[仕様の索引](#contents)

本章はソーステキスト、行、コメント、トークン、名前、リテラルを定義する。

<a id="s2-1"></a>
### 2.1. ソーステキストと行

ソーステキストはUTF-8とする。不正なUTF-8バイト列はコンパイル時エラーとなる。ソースファイルの先頭にはU+FEFFのバイトオーダーマークを1つ置いてもよい。これはソーステキストには含まれない。

各ソースファイルはKotonohaのSourceDocumentである（[§18.1](#s18-1)）。

行はLF、CRLF、CRのいずれかで終わる。ソース位置は行と列で表し、どちらも1から数える。列は行の先頭からUnicodeスカラー値を数える。

インデント、ヘッダー基準位置、単一項目の本体とインデントされた本体、レイアウトイベント、継続、ヘッダー内の式、分岐の結合、ドットで始まる行は[§F.4](#sf-4)で定義する。

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

<a id="s2-2"></a>
### 2.2. 空白とコメント

リテラルとコメントの外では、空白として使えるのはU+0020と行末だけである。その位置のタブ、改行しない空白、その他の空白文字や制御文字はコンパイル時エラーとなる。スペースはトークンを区切り、行頭のスペースはインデントを定め、行末のスペースは無視される。コメントは隣接するトークンを区切り、結合しない。

- `//`は行末またはソースの終端まで続くコメントを開始する。
- `/*`は最初の`*/`で終わるブロックコメントを開始する。ブロックコメントは入れ子にならない。`*/`がなければコンパイル時エラーとなる。
- リテラルのテキスト内では、コメントの区切り記号は内容の一部になる。補間内には通常のトークン規則を適用する。

1行内で完結するブロックコメントはトークン間に置ける。その前のスペースが行のインデントを定める。ブロックコメントに行末が含まれる場合、`*/`と同じ行でその後に置けるのはスペースとコメントだけである。

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

<a id="s2-3"></a>
### 2.3. ドキュメントコメント

**ドキュメント行**とは、同じ行の`///`より前にスペースしかなく、直後にもう1つの`/`がない行コメントである。`///Text`と`/// Text`はどちらも該当する。`////`、同じ行のコードの後にある`///`、`/** ... */`は通常のコメントである。通常のコンパイルでは`///`を通常のコメントとして扱う。ドキュメントは妥当性や動作に影響しない。

同じインデントで連続するドキュメント行は、1つの**ドキュメントブロック**を作る。空行または通常のコメントでブロックは終わる。

**対応付け。** 宣言の前置部とは、ヘッダーの前に並ぶ属性、空行、コメントである。宣言が持てるドキュメントブロックは最大1つであり、前置部でヘッダーに最も近いブロックのインデントが[ヘッダー基準位置](#sf-4-1)と等しい場合、そのブロックが対応する。それ以外のブロックは対応先を持たない。以前のブロックに遡ったり、無関係の構文、ディレクティブ、スコープ境界を越えたりして対応付けることはない。属性はブロックの前後や途中に置けるが、ドキュメント、属性、宣言の順を推奨する。

次の宣言にドキュメントを付けられる。

| 対象 | 形式 |
| --- | --- |
| コンテナー | group、rootgroup、struct、enum、contract |
| 関数 | 名前付き関数（`main`を含む）と要件、明示的特殊化、`init`、`drop` |
| プロパティと束縛 | let、var、計算プロパティ、プロパティ要件、ローカルの`let`と`var` |
| ケース | enumの各ケース |
| 関連型 | associate宣言と指定 |

パラメーター、ジェネリックパラメーター、Originパラメーター、ペイロード、アクセサーは、それらを持つ宣言のドキュメントで説明する。`rootgroup A.B`のドキュメントは、その`B`の断片に属する。断片やオーバーロードはそれぞれ独自のドキュメントを持つ。オーバーライド対象のメンバー、実装対象の要件、特殊化対象の関数からドキュメントを継承・複製することはない。コンパイル時ディレクティブで除外された構文（[§19.4](#s19-4)）にはドキュメントを対応付けない。条件付き宣言のドキュメントは、そのディレクティブが制御する領域内に書く。

ドキュメントのテキストはMarkdownである。慣例として、リスト項目`- name: text`はパラメーター、または標準項目`return`、`abort`、`safety`、`note`、`warning`、`example`を説明する。対応するMarkdown、抽出、表示、ドキュメントの診断は実装定義とする。

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

<a id="s2-4"></a>
### 2.4. トークンと記号

トークンの綴りは連続していなければならない。隣り合うトークンを連結すると別のトークンになる場合、両者を区切る必要がある。トークンとして認識できても、その位置で妥当とは限らない。コメントとリテラルの外で、どのトークンも開始しない文字はエラーとなる。記号トークン、最長一致、ジェネリックの閉じ記号の分割、独立したトークン`$`と`#`、使用位置を制限するトークン`;`、`!`、`{`、`}`、`&&`、`||`は[§F.2.1](#sf-2-1)で定義する。

```kimi
let r = 0..=9          // ..= is one token
let ne = p != q
let ok = p and q
let no = p && q        // Error: misplaced &&; write `and`
```

<a id="s2-5"></a>
### 2.5. 名前

**名前**は宣言を識別する。宣言の種類にかかわらず、すべての名前は生成規則`Name`の形式に従う（[§F.2.2](#sf-2-2)）。

名前はUnicode正規化形式Cでなければならない。NFCでない綴りはエラーとなり、正規化しない。双方向制御文字やゼロ幅接合子を含む書式文字（`Cf`）は名前に使えない。2つの名前が等しいのは、Unicodeスカラー値の列が等しい場合に限る。比較は大文字と小文字を区別し、カルチャに依存せず、正規化や折り畳みを行わない。見た目が紛らわしい名前も別の名前である。文字カテゴリーとNFCにはUnicode 15.0.0を使う。この版で未割当ての文字は使えず、実行環境によって結果は変わらない。同じ規則を条件名にも適用する（[§19.2](#s19-2)）。

予約語と文脈キーワードは[§F.3](#sf-3)に列挙する。

`_`単独の綴りは予約トークンであり、名前ではない。次の位置でのみ使える。

| 位置 | 意味 |
| --- | --- |
| 実行時パターン | ワイルドカード（[§14.8.1](#s14-8-1)） |
| `#case _` | 最後の全条件に一致するケース（[§19](#s19)） |
| 初期化するローカル束縛の注釈内の`[_ of T]`、`[N of _]`、`[_ of _]` | 長さまたは要素型の推論（[§4.3.1](#s4-3-1)） |
| `_ = expression` | 明示的に結果を捨てる（[§14.2.4](#s14-2-4)） |
| `for`の束縛、またはそのタプル要素の1つ | 名前のない反復束縛（[§14.6.1](#s14-6-1)） |

`let _`、`var _`、パラメーターや参照としての`_`など、それ以外の`_`は構文エラーとなる。`_value`のような長い名前には影響しない。

```kimi
let point2 = 1
let _value = 2
let délta = 3          // Valid: U+00E9 in NFC
let Dog = 4
let dog = 5            // A different Name from Dog
let 2point = 6         // Error: a Name cannot start with a digit
let _ = 7              // Error: `_` is not a Name
```

<a id="s2-6"></a>
### 2.6. 数値リテラル

数値リテラルはASCIIの10進数字で始まる。文法は[§F.2.3](#sf-2-3)で定める。数値リテラルに直接適用した符号は直接リテラルを作る（[§12.3](#s12-3)）。数値リテラルに型の接尾辞はない。型は文脈（[§12.3](#s12-3)）または`123@i32`のような明示的な型合わせ（[§13.5.4](#s13-5-4)）で決まる。

桁区切りの`_`は無視する。浮動小数点リテラルは、その厳密な10進値を`f32`または`f64`の型へ一度だけ最近接・偶数丸めした値を表す。結果が無限大ならエラーとなる。ゼロや非正規化数は許される。それ以外のリテラルは整数であり、大きさが2^128 − 1を超えるとエラーとなる。

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

<a id="s2-7"></a>
### 2.7. 文字エスケープ

文字リテラルとエスケープ文字列は、次のエスケープを共有する。いずれもUnicodeスカラー値をちょうど1つ生成する。

| エスケープ | 結果 |
| --- | --- |
| `\0` | U+0000 |
| `\\` | U+005C バックスラッシュ |
| `\e` | U+001B エスケープ |
| `\t` | U+0009 タブ |
| `\n` | U+000A 改行 |
| `\r` | U+000D 復帰 |
| `\"` | U+0022 引用符 |
| `\'` | U+0027 アポストロフィ |
| `\u(H...)` | 16進コードHが表すスカラー値 |

`\u(...)`の値がU+10FFFFを超えるか、U+D800..U+DFFFに含まれる場合はエラーとなる。各エスケープは単独で検査するため、2つのサロゲートのエスケープがペアになることはない。それ以外のエスケープや不完全なエスケープもエラーとなる。

```kimi
let a = '\u(41)'            // A
let b = '\u(1f600)'         // 😀
let c = '\u()'              // Error: no digits
let d = '\u(0000041)'       // Error: seven digits
let e = '\u(D83D)'          // Error: surrogate
```

<a id="s2-8"></a>
### 2.8. 文字リテラル

文字リテラルの型は`char`であり、単一引用符の間にUnicodeスカラー値をちょうど1つ、直接またはエスケープ（[§2.7](#s2-7)）で記す。`char`は表示上の1文字ではなく1つのスカラー値なので、結合文字単独でも妥当である。内容は正規化しない。

アポストロフィ、バックスラッシュ、制御文字U+0000..U+001FとU+007F..U+009F、U+2028とU+2029はエスケープしなければならない。文字リテラルに補間はない。

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

<a id="s2-9"></a>
### 2.9. 文字列リテラル

文字列リテラルの型は`string`であり、UTF-8テキストを生成する。どちらの形式も複数行にまたがることができる。

| 形式 | 区切り記号 | エスケープ | 補間 |
| --- | --- | --- | --- |
| エスケープ文字列 | 両端に`"`を1つずつ | あり | あり |
| 生文字列 | 両端に同じ個数の、3つ以上連続する`"` | なし | なし |

文字列の内容には、制御文字、タブ、U+2028、U+2029を含め、どのUnicodeスカラー値も直接記せる。ただし、閉じ区切り記号と、エスケープ文字列内のエスケープされていない`\`は除く。文字リテラルの除外規則（[§2.8](#s2-8)）は文字列には適用しない。

リテラル内の各行末は、ソースでの形式にかかわらず1つのLFとなる。インデントや先頭・末尾の改行は取り除かず、正規化もしない。明示的なCRには`\r`を使う。リテラルのテキストはレイアウトに関与しない（[§F.4.1](#sf-4-1)）。

<a id="s2-9-1"></a>
#### 2.9.1. エスケープ文字列と補間

エスケープ文字列内では、`\`がエスケープ（[§2.7](#s2-7)）または補間を開始する。`\(expression)`は式の値をテキストとして挿入する（[§12.4.2](#s12-4-2)）。補間は対応する`)`で終わる。この対応は通常のトークン化で決めるため、入れ子のリテラルやコメント内の括弧は数えない。レイアウトは[§F.4.1](#sf-4-1)に従う。入れ子の深さの上限は実装定義とする。

```kimi
let greeting = "Hello, \(name)."
let total = "Total: \(price * quantity)"
let lines = "First line\nSecond line"
let block = "
First line
Second line
"                                  // Starts and ends with LF
```

<a id="s2-9-2"></a>
#### 2.9.2. 生文字列

生文字列内では、バックスラッシュ、改行、`\(`は通常の内容である。

文字列トークンの先頭では、連続する`"`の数を数える。1つならエスケープ文字列を開始し、ちょうど2つなら空文字列`""`となり、N ≥ 3個なら区切り記号長Nの生文字列を開始する。開始時の連続部分は分割しないため、生文字列の内容を`"`で始めることはできない。そのようなテキストにはエスケープ文字列を使う。その後で最初に現れるM ≥ N個の引用符の連続部分が、生文字列を終える。最初のM − N個は内容、最後のN個は区切り記号となる。より短い連続部分は内容となる。このため、`""""""`は空の生文字列ではなく、終端のない生文字列を開始する。隣接する文字列リテラルは結合しない。

```kimi
let path = """C:\Users\name\file.txt"""
let token = """"The token """ appears here.""""
let tail = """text""""             // Content is text"
let a = """"x""""                  // Content is x
let b = "\"x\""                    // Content is "x"
```

---

<a id="s3"></a>
## 3. 型と値

[仕様の索引](#contents)

**型**はSemantics、Core、必要なOriginを組み合わせたものである。1つの層は`Semantics/Core during Origin`と書く。

| 用語 | 意味 |
| --- | --- |
| 型 | 完全な型。Semantics、その対象、すべてのOrigin依存。 |
| Semantics | 値の表現、所有権、借用、アクセス、安全性の規則（[§3.3](#s3-3)）。 |
| Core | 値の種類、構造、同一性。プリミティブCore（[§3.1](#s3-1)）、structまたはenum（[§6](#s6)）、タプル、固定長配列（[§4](#s4)）、Callable Core（[§3.2.1](#s3-2-1)）、`Array<T>`などの名前付きライブラリー型。 |
| Origin | 借用が有効なプログラム上の位置の集合である領域を表す、記号的な寿命（[§3.7](#s3-7)）。借用元から導かれるもの、宣言されたもの、交差、`static`のいずれか。 |

`ref/Dog during source`では、Semanticsは`ref`、Coreは`Dog`、Originは`source`である。SemanticsとOriginは、推論と省略の規則で認められる場合にのみ省略できる。すべての層がOriginを持つわけではない。Coreの要素と型引数には完全な型を使える。`owner/(i32, ref/Dog during source)`のCoreはタプルであり、第2要素は借用とOriginを保つ。`ref`、`uniq`、`raw`は完全な型を囲める（[§3.3.2](#s3-3-2)）。オブジェクトSemanticsはCoreの代わりに実行時Contractビューを対象にできる（[§3.3.1](#s3-3-1)）。

<a id="s3-1"></a>
### 3.1. プリミティブCore

**Scalar**は、整数Core（`i8`、`u8`、`i16`、`u16`、`i32`、`u32`、`i64`、`u64`、`i128`、`u128`、`isize`、`usize`）、浮動小数点Coreの`f32`と`f64`、`bool`、`char`、12種類のラップ整数Coreからなる閉じた集合である。`string`、Unit、NeverはScalarではない。Scalarはソースで使える名前ではない。

**整数型**とは`owner` Semanticsの整数Core、**ラップ整数型**とは整数型`T`に対する`Wrapping<T>`、**数値型**とはこれらまたは浮動小数点型をいう。整数型の規則がラップ整数型にも適用されるのは、その旨を明記した場合だけである。

| 型 | 記憶サイズ |
| --- | --- |
| `i8`、`u8`、`bool` | 8ビット |
| `i16`、`u16` | 16ビット |
| `i32`、`u32`、`f32` | 32ビット |
| `i64`、`u64`、`f64` | 64ビット |
| `i128`、`u128` | 128ビット |
| `isize`、`usize` | ターゲットのポインターサイズ |

`bool`の妥当なビットパターンは`0x00`（`false`）と`0x01`（`true`）だけである。それ以外を`bool`として読むと未定義動作となる（[§5.2](#s5-2)）。

<a id="s3-1-1"></a>
#### 3.1.1. ラップ整数型

整数型`T`に対する`Kimi.Wrapping<T>`は、`T`と同じ表現と値の解釈を持つ、独立したScalarである。算術結果を`T`で表せない場合、Abortする代わりに2ᴺを法としてラップする（[§13.3.1](#s13-3-1)）。

- ローカルの`Wrapping`が何を表していても、Scalarとなるのは`::Kimi.Wrapping<T>`（[§22.1](#s22-1)）だけである。既定の別名によって`Wrapping<u32>`を使える。
- 引数は整数型でなければならない。`Wrapping<f32>`、`Wrapping<ref/u32>`、`Wrapping<Wrapping<u32>>`は不正である。型パラメーター`T`に対しては、`T is PrimitiveInteger`の証明が必要となる（[§8.7.4](#s8-7-4)）。
- メンバーも公開コンストラクターも持たない。値はリテラル（[§12.3](#s12-3)）、算術、変換（[§13.5.4](#s13-5-4)）から得る。
- `Wrapping<T>`と`T`の間には型関係も暗黙変換もない。引数が異なるラップ整数型同士も同様である。両方のオーバーロードに合うリテラル引数は曖昧となる。
- Copy、Owned、Sealed、ObjectPayloadを満たし、読出し型である（[§3.5.3](#s3-5-3)）。`T`のEquatable、Comparable、Utf8Formatへの適合と、算術Contractへの適合（[§13.3.3](#s13-3-3)）を持つ。符号なし引数に対するNegatableも含む。中置の`&`、`|`、`^`（XOR）はラップ整数型に適用できる（[§13.3.1](#s13-3-1)）。
- `PrimitiveInteger`も`Position`も満たさない。配列やスライスの位置、範囲の境界、接頭`^`（末尾からの位置）のオペランド、長さ定数、定数添字（[§15.1.3](#s15-1-3)）、シフト数には使えない。

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

<a id="s3-1-2"></a>
#### 3.1.2. 文字と文字列

`char`は、U+0000..U+D7FFまたはU+E000..U+10FFFFのUnicodeスカラー値1つを32ビットで格納する。この範囲の値は、割当て済みかどうかによらずすべて妥当である。文字リテラル（[§2.8](#s2-8)）が制限するのは綴りだけである。`char`は`u32`と表現が同じである必要はなく、UTF-8のコード単位でもない（バイトの格納には`u8`を使う）。UTF-8から復号されたスカラー値はすべて妥当な`char`である。

`string`はUTF-8テキスト用の組込みCoreである。記憶レイアウトは実装定義であり、`owner/string`はNon-Copyである。

```kimi
let euro: char = '€'          // One scalar value, three UTF-8 bytes.
let flag: string = "🇯🇵"
// let bad: char = '🇯🇵'      // Error: two scalar values.
```

<a id="s3-1-3"></a>
#### 3.1.3. UnitとNever

`()`はUnit型である。意味のある結果がないことを表す唯一の値を持ち、記憶領域を占めなくても初期化状態と寿命を持つ。

**Never**はコンパイラー組込みの値型`::Kimi.Never`（[§22.1](#s22-1)）であり、値もコンストラクターも持たない。記述した名前は通常の名前検索に従い、隠蔽されることもある。推論されたNeverは常にこのKimi宣言を表す。`return`、`exit`、`continue`、`yield`、`$abort(...)`の型はNeverであり、それらのオペランドが転送先に結果を渡す。それ以外の制御フロー式がNever型になるのは、すべての結果源と構造的完了を検査した後、結果検証（[§14.9](#s14-9)）によって決まる場合だけである。実行時到達可能性だけでは型は決まらず、結果の欠落も許されない。Neverは型であり、完了ではない（[§14.1](#s14-1)）。

NeverはCopy、Owned、Transferableを満たす。Sealed、ObjectPayload、PrimitiveInteger、Callableは満たさず、ほかのContractにも適合しない。完全な値型を許す位置なら、型引数、フィールド、タプルや固定長配列の要素、enumのペイロードなど、どこでもNeverを使える。Neverをインライン格納する型の値は構築できず、ペイロードがNeverをインライン格納するenumケースも構築できない。網羅性検査はそのようなケースを到達不能として扱わない。Neverのレイアウトは実装定義とする。

```kimi
let a: Option<Never> = .None
let b = a                // Copy: Never is Copy, so Option<Never> is Copy.
match r                  // r: Result<i32, Never>
    .Ok(let v) => use(v)
    .Err(_) => ()        // Still required: Never payloads give no coverage.
```

<a id="s3-2"></a>
### 3.2. 複合型

名前付きCoreは、`A.B<T, U>`のように修飾し、型引数を持たせられる。別の宣言内で宣言した型は、フィールドがなくても外側の環境の正規化済み束縛を保持し、暗黙の外側インスタンスは持たない（[§6.1.3](#s6-1-3)）。

`()`はUnit、`(T,)`は1要素タプル、`(T, U)`は2要素タプルであり、`(T)`はグループ化だけを行う。**関数型**は関数パラメーターリスト、`->`、結果型からなる（文法：`FunctionType`、`FunctionParameters`、[§F.5](#sf-5)）。リストはグループ化した型でもタプル型でもない。`(T) -> U`と`(T,) -> U`は`T`を1つ、`(()) -> U`はUnitを1つ、`((T,)) -> U`はタプルを1つ受け取る。先頭にはタスクスロット`task;`を置けるが、これはパラメーターではない（[§24.2.1](#s24-2-1)）。

```kimi
func total(single: (i32,), pair: (i32, string), test: (i32) -> bool) -> i32
    return single.0 + pair.0
```

<a id="s3-2-1"></a>
#### 3.2.1. Callable値型

**関数値**は、次の3種類のCoreのいずれかを持つ、呼出し可能な値である。

- **Function Item型**は、解決済みの関数宣言とそのインスタンス化1つを表し、キャプチャー環境を持たない。Copyであり、Shared呼出しを持ち、束縛済み引数が[§15.2.3](#s15-2-3)を満たすとOwnedとなる。
- **具象クロージャー型**は、無名関数式とそのインスタンス化1つを表し、キャプチャーを格納する。同じ型引数での評価は常に同じCoreを持ち、異なる式は異なる型を持つ。各キャプチャーの完全な型がすべてCopyである場合に限りCopyとなる。環境はユーザーがアクセスできるstructではなく、ユーザー定義の`drop`を付けることもできない。
- **共通関数型**`(A1, ..., An) -> R`は、所有された型消去済み環境を保持する。Non-Copyであり、Ownedな環境を要求し、Shared呼出しだけを公開する。環境に依存する借用は返せない。引数と結果はOwnedでなくてもよい。

Function Itemまたはクロージャーは、Callableジェネリクス（[§8.10](#s8-10)）を通しても、実際のレシーバと寿命の契約を含む具象型を保つ。または、共通関数型の契約を満たす場合にその型へ変換され、結果が環境とその後始末を所有する（[§7.7.4](#s7-7-4)）。タスクスロットの有無は関数型の同一性の一部である。有無が異なる型同士は、共通関数型を介しても変換できない（[§24.2.4](#s24-2-4)）。各値は独自のOrigin束縛を保持し、省略は[§15.4](#s15-4)に従う。直接借用する入力でOriginを省略した場合、そのOriginは呼出しごとに束縛され、結果は環境とは独立してそのOriginに依存できる。記述した入力Originは既存のOriginを指し、固定される（[§15.3.4](#s15-3-4)）。入力が呼出しごとに束縛される実装は、そのOriginをインスタンス化することでこの入力に合う（[§10.7](#s10-7)）。束縛済みの入れ子の依存は固定されたままである。環境が空でも、純粋性、メモリー割当てがないこと、並行処理の安全性は保証しない。共通関数値の`ref/F`はCopy、`uniq/F`はNon-Copyであり、どちらも呼出し能力を加えない。

```kimi
func twice(x: i32) -> i32 => x * 2

let item = twice                    // Function Item: Copy.
let common: (i32) -> i32 = twice    // Converted to a common Function Type.
// let copy = common                // Error: a common Function Type is Non-Copy.
let moved = common@move
```

<a id="s3-2-2"></a>
#### 3.2.2. 弱参照

`Kimi.Weak<s/T>`は、`s is rc or arc`を宣言した、コンパイラー管理のNon-Copy structである。引数`S`には、オブジェクト対象`X`（[§8.7.3](#s8-7-3)）に対する`rc/X`または`arc/X`、あるいは許容集合（[§8.3](#s8-3)）が{`rc`, `arc`}に含まれるペア`s/T`だけを認め、それ以外は拒否する。Weakは対象への弱参照の責任を1つ所有し、ペイロードは所有しない。外側のSemanticsは`owner`であり、`ref/Weak<S>`はWeakのスロットを借用する。`@move`で移動し、`Kimi.Intrinsics.clone`で複製する（[§13.6.3](#s13-6-3)）。ユーザーはフィールドや`drop`を置き換えられない。

空のWeakは存在しない。不在には`Option<Weak<S>>`を使う。期限切れのWeakは、存在しているがupgradeできない値である。変性、Loan要件、OwnedOriginsの解析では、`Weak<S>`はすべての状態で`S`を格納するものとして扱う（[§15.3.5](#s15-3-5)）。`S`の完全な型とLoan依存を保持するが、アクセスは許可しない。Weakにはメンバーアクセス、オブジェクト借用、実行時`is`、検査付きキャスト、ビュー変換はなく、先にupgradeする必要がある。

```kimi
let strong: rc/Item = makeItem()@rc
let weak = Kimi.Intrinsics.downgrade(strong@ref)    // Weak<rc/Item>
let again: Option<rc/Item> = Kimi.Intrinsics.upgrade(weak)
// let copy = weak                                  // Error: Weak is Non-Copy.
```

<a id="s3-2-3"></a>
#### 3.2.3. Optional型

`T?`はローカルの名前検索結果にかかわらず、正確に`::Kimi.Option<T>`を表し、すべての規則を展開後の型に適用する。`?`は1つごとに1層を加える。`T??`は`Option<Option<T>>`であり、平坦化しない（文法：`AnnotatedType`、[§F.5](#sf-5)）。

| 表記 | 展開後の型 |
| --- | --- |
| `ref/T?`、`ref/obj/T?` / `ref/(T?)` | `Option<ref/T>`、`Option<ref/obj/T>` / `ref/Option<T>` |
| `[N of T?]` / `[N of T]?` | `[N of Option<T>]` / `Option<[N of T]>` |
| `(T) -> U?` / `((T) -> U)?` | `(T) -> Option<U>` / `Option<(T) -> U>` |

`(A)? -> B`は不正であり、Semantics接頭辞の後に関数型全体を置く場合はグループ化が必要となる。Originの対応先は展開前に解決する（[§3.3.2](#s3-3-2)）。`ref/T? during a`は`Option<ref/T during a>`である。束縛集合の接尾辞は`?`より前の名前付き型に付く。`View<T>{v}?`（つまり`Option<View<T>{v}>`）と書く。`View<T>?{v}`、`(View<T>){v}`、`View<T>{v}{w}`は不正である。展開は依存やLoanを消去・延長しない。`ref/i32? during a`を`<s/T>`に渡すと、`s = owner`、`T = Option<ref/i32 during a>`となる。

名前専用の位置ではこの接尾辞を使えない。`is Dog?`、`T?.Some`、`@ref?`は不正である。`@S?`の`S`は型である。`x@ref/T?`の対象は`Option<ref/T>`であり、`x`の借用もSomeの構築も行わない。`x`が既にその型を持つ場合に限り妥当である（[§13.5.3](#s13-5-3)）。暗黙のラップ、アンラップ、既定値、引数省略はなく、`null`はNoneではない。`raw/T?`では`.None`と`.Some(null)`は異なる。OptionとUnitの記憶表現は実装定義とする。

```kimi
let nested: i32?? = .Some(.None)
let noBorrow: ref/i32? during static = .None

func wrap<T>(value: View<T>) -> View<T>{result}?
    origin result.source == value.source
    return .Some(value@move)
```

<a id="s3-3"></a>
### 3.3. Semantics

表の`T`はCoreを表す。オブジェクト形式では実行時ContractのView Targetも表す（文法：`SemanticsType`、[§F.5](#sf-5)）。

| カテゴリー | 構文 | 意味 |
| --- | --- | --- |
| `value` | `T`、`owner/T` | 直接所有する値 |
| `valueborrow` | `ref/T`、`uniq/T` | 値の共有借用／排他的な可変借用 |
| `object` | `obj/T`、`rc/T`、`arc/T` | 排他的に所有する／非アトミックに参照を数える／アトミックに参照を数えるオブジェクト |
| `objectborrow` | `objref/T`、`objuniq/T` | オブジェクトの共有借用／排他的な可変借用 |
| （なし） | `raw/T` | 生ポインター |

`borrow`は`ref`、`uniq`、`objref`、`objuniq`、`owning`は`owner`、`obj`、`rc`、`arc`、`reference`は`raw`を含む`owner`以外のすべてのSemanticsを表す。Semanticsのカテゴリーは、この7つだけである。

- ジェネリックなSemanticsの位置に置けるのはペアバインダーだけである。`<s/T>`では、ペアバインダー`s`が1つの完全な型の外側Semantics、ペア対象`T`がその直接の対象となる（[§8.1.1](#s8-1-1)）。構文上の位置によってパラメーターの種類が変わることはない。
- 主体がペアバインダーの場合、カテゴリー名は要件となり、その位置では隠蔽できない。それ以外では通常の名前である。カテゴリーは値型でもAdaptationの略記でもない。要件は具体的なSemanticsを指すこともでき、[§8.2](#s8-2)に従って組み合わせる（`s is ref or obj`）。その判定はバインダーの許容集合（[§8.3](#s8-3)）で決まる。
- 判定するのは外側の層だけである。`reference`は安全な借用を保証せず、`s is borrow`は外側の安全な借用を要求し、`s is owning or borrow`は`raw`以外のすべてを許す。`owning`は再帰的なOwnedの保証（[§15.2.3](#s15-2-3)）とは異なる。

値の借用は、参照先型を格納する記憶領域への非所有アクセスを与え、そこに格納されたポインターはたどらない。`ref/(rc/T)`はハンドルのスロットを、`objref/T`はオブジェクトを借用する。複数の共有借用は共存できるが、排他借用と競合するものは共存できない。オブジェクトは動的ペイロード、型メタデータ、所有の責任を保持する。ヒープに割り当てる必要はない。`rc`と`arc`は所有参照がある間、オブジェクトを生存させる。アトミックな参照カウントは並行した変更の安全性を保証しない。オブジェクトの生成と複製は[§13.6.2](#s13-6-2)の操作で行い、解放は[§16.3.3](#s16-3-3)に従う。完全なSealedペイロードは`@follow`で選び、値として借用できる（[§13.5.5](#s13-5-5)）。内容全体の更新は[§15.7](#s15-7)に従う。並行処理は将来の対象とする（[§D.5](#sd-5)）。

```kimi
let node: rc/Node = makeNode()@rc
let slot: ref/(rc/Node) = node@ref     // Borrows the handle slot.
let view: objref/Node = node@objref    // Borrows the object.

func measure<s/T>(value: s/T) -> isize
    s is owning or borrow
    return 0
// measure(pointer)                    // Error: pointer: raw/i32; raw is not admitted.
```

<a id="s3-3-1"></a>
#### 3.3.1. オブジェクトのビューと同一性

**オブジェクトビュー型**とは、オブジェクトハンドルまたは借用の静的型である。オブジェクトSemantics、**View Target**（Core、または関連型が固定された実行時Contractビュー）、さらに`objref`と`objuniq`では外側Originからなる（[§8.1.1](#s8-1-1)）。オブジェクト借用は値の借用と同じ整形式の規則に従う（[§15.6.1](#s15-6-1)）。ObjectPayload（[§8.7.3](#s8-7-3)）を満たすowner Coreは、すべて具象ペイロードになれる。Neverと明示的に除外されたCoreはなれない。SealedなView Targetはペイロードが完全であることを証明する。実行時Contractビューは実行時Contract拡張（[§8.12](#s8-12)）に属する。`objref/Speaker`の`Speaker`はCoreではなく、`owner/Speaker`は存在しない。

**動的型**は、構築された具象Coreである。その**実行時同一性**は、すべてのOriginを再帰的に消去したCoreである。型引数内のSemanticsは保つ。そのため、`Box<ref/i32 during a>`と`Box<ref/i32 during b>`は同じ実行時同一性を持つが、`Box<ref/i32>`と`Box<i32>`、あるいは`Box<objref/C>`と`Box<obj/C>`は異なる。完成したオブジェクトは、ビューの変更や完全なペイロードの置換（[§15.7](#s15-7)）を通して、1つの動的型、同一性、破棄責任を保つ。別の型としての再初期化は禁止する。同じアドレスに作った新しいオブジェクトは、同一性、Loan、フロー上の事実を引き継がない。

**`Supports(D, V)`**が成立するのは、`V`が具象Core`D`またはその直接・間接の基底である場合、あるいは`V`が`ObjectViewCompatible(C)`と`Implements(D, C)`を満たす実行時Contractビュー`C`である場合に限る（[§8.12](#s8-12)）。アップキャスト、ビュー検査、検査付きキャストはこの関係を使う。この関係は、アクセス、所有権、Owned、Loanの妥当性、互換性のないメンバーの呼出しを許可しない。スライシングはなく、`owner/Dog`から`owner/Animal`、`ref/Dog`から`ref/Animal`、`uniq/Dog`から`uniq/Animal`への暗黙変換もなく、コンテナーの共変性もない。オブジェクトのアップキャストは明示的であり（[§13.6.1](#s13-6-1)）、継承したレシーバの射影（[§9.6.1](#s9-6-1)）が提供するのはレシーバだけである。

```kimi
// Dog is a struct whose base is Animal.
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal     // Explicit upcast; animal owns the whole Dog.
let view = animal@objref
// let back = view@objref/Dog        // Error: Animal does not imply Dog.
```

<a id="s3-3-2"></a>
#### 3.3.2. 入れ子のSemanticsとOrigin注釈

`ref/V`、`uniq/V`、`raw/V`は、完全な値型`V`を**参照先型**として取る。外側の層は`V`を保持する記憶領域を参照し、`V`のSemanticsをそのまま保つ。括弧は完全な型をグループ化し、タプルや借用は加えない。`ref/((i32) -> bool)`は関数値を借用し、`(ref/i32) -> bool`は借用した整数を受け取り、`ref/(i32) -> bool`は不正である。

**`during`の対応先**（文法：`AnnotatedType`、[§F.5](#sf-5)）。注釈は、そのAnnotatedTypeの最初の明示的Semanticsを対象とする。明示的Semanticsがなければ、その位置に直接記した名前付き型の唯一のスキーマスロットを束縛する（[§15.3.1](#s15-3-1)）。どちらでもなければ対象がなく、エラーとなる。グループ化、別名、引数、要素、シグネチャをたどったり、`owner`を取り除いたりして対象を探すことはない。`during`を付けられるのは、`ref`、`uniq`、`objref`、`objuniq`、`raw`（任意のOriginを与える。[§5.7](#s5-7)）、ペアバインダー`s/T`（条件付きスロット）だけである（[§15.3.1](#s15-3-1)）。`?`接尾辞の後に書くが、Optionalのラップより前に適用する。構文で対応先を確定し、Optionalを展開してから、形成検査を適用する。AnnotatedTypeに付けられる注釈は最大1つであり、グループ化によって内側の層に注釈を付け直すことはできない。`during`は型の層も値操作も加えない。

| 表記 | 意味 |
| --- | --- |
| `ref/T? during a`、`(ref/T during a)?` | `Option<ref/T during a>` |
| `ref/(uniq/T during b) during a` | 外側は`a`、内側は`b` |
| `Slice<T> during a` | Sliceの唯一のスロットを束縛する |
| `raw/ref/T during a` | 外側のrawに`a`。内側の層に付けるには`raw/(ref/T during a)`と書く |
| `(ref/T)? during a`、型パラメーター`T`に対する`T during a`、`(ref/T during a) during b`、`ref/T during a?` | エラー |

`ref/View<ref/U during c>{v} during a`では、`a`は外側の借用、`c`は型引数に属し、`v`はViewのスキーマ束縛に名前を付ける。関数の末尾の`during`は結果に属する。`(A) -> ref/B? during b`は`Option<ref/B during b>`を返す。関数値を借用するには`ref/((A) -> B) during a`が必要となる。

**正規化。** 別名を展開し、すべての層を保つ。`ref/ref/T`と`ref/T`は異なる。グループ化と冗長な`owner`接頭辞は正規化で取り除くが、`owner/V`は`V`の参照を保つ。オブジェクトSemanticsはオブジェクト対象を要求する（[§8.7.3](#s8-7-3)）。`ref/obj/T`は妥当だが、`obj/ref/T`は不正である。ジェネリックの置換にも同じ規則を適用する。

**層。** 各層は独自のOriginを保ち（[§15.4](#s15-4)）、外側の注釈が内側の注釈を置き換えることはない。各借用層は整形式でなければならない（[§15.6.1](#s15-6-1)）。生ポインターは安全な参照の妥当性も、より長い寿命も確立しない。Copyは外側のSemanticsで決まる。`ref/uniq/T`はCopy、`uniq/ref/T`はNon-Copyである。外側の共有参照をCopyしても、内側の排他能力は複製されない。共有アクセスでは、内側のNon-Copy値のMove、その`uniq`の排他的使用、スロットへの書込みはできず、共有の再借用は外側のLoanの範囲内にとどまる。外側の参照を移動または破棄しても参照先は変わらず、排他的なスロット置換でも内側の型、Origin、Loanの制約を保つ。

```kimi
var number: i32 = 1
var exclusive = number@uniq                  // uniq/i32
let shared: ref/uniq/i32 = exclusive@ref     // Shared borrow of the slot; Copy.
let handle: obj/Node = makeNode()@obj
let slot: ref/obj/Node = handle@ref          // The handle slot, not objref/Node.
// let boxed: obj/ref/i32 = number@ref       // Error: a borrow is not an Object Target.
```

<a id="s3-3-3"></a>
#### 3.3.3. 参照先の選択と読出し

参照値は、既定では参照先として読み出さない。参照先のPlaceは、`@follow`（[§13.5.5](#s13-5-5)）による明示的な選択、または参照経路の選択（[§3.4.3](#s3-4-3)）で選ぶ。参照先を暗黙に読み出すのは、値の読出し（[§3.5.3](#s3-5-3)）と、固定された期待型`ref/U`に対して層を通して共有参照を1つ作る場合（[§10.2](#s10-2)）だけである。Non-Copyな参照先を参照越しに取り出すことはない（[§15.1.3](#s15-1-3)）。部分の選択、比較（[§13.4](#s13-4)）、借用によって調べる。

安全な`*`は存在しない。安全な参照が生ポインターになるのは`@raw`を通す場合だけであり、生ポインターが安全になるのはunsafe文脈で生Placeを借用する場合だけである（[§5.2.2](#s5-2-2)）。明示的な借用は記述したスロットを借用する（[§13.5.5](#s13-5-5)）。格納済みの排他参照を修飾なしで取得すると、それを再借用する（[§3.5.1](#s3-5-1)）。

```kimi
func bump(counter: uniq/i32, limit: ref/i32)
    counter@follow += 1          // Explicit selection of the referent.
    let snapshot: i32 = limit    // Value read.
    // counter += 1              // Error: an assignment target is never redirected.
```

<a id="s3-4"></a>
### 3.4. Placeと記憶領域

**記憶領域**は値を保持する。ローカル、静的スロット（[§22.4.4](#s22-4-4)）、集成体の一部、オブジェクトペイロード、実体化した一時値、**生の記憶領域**がこれに当たる。生の記憶領域には`Raw.allocate`の結果や動的コレクションの要素領域（[§4.9](#s4-9)）があり、そこに置く値のPlaceを宣言しない（[§5.2](#s5-2)）。生の記憶領域に置かれた値は、その型のPlaceをそこで占める。値の各構成要素は、そのPlaceの一部であるPlaceを占める。**破棄責任**とは、所有する値を破棄する義務である（[§16](#s16)）。**Place**とは、完全な型、アクセス経路、能力、依存を伴う選択済みの記憶領域である。**Place式**はPlaceを指し、括弧はその分類を保つ。Placeは値ではない。格納、受渡し、型引数への束縛、Optionでのラップはできない。記憶位置を保持するには借用する。完全な値には、構築の完了と現在の完全性がある（[§15.1.2](#s15-1-2)）。

| 能力 | 意味 |
| --- | --- |
| Read | 値をCopy、共有借用、比較、検査する |
| Write | 値を初期化または置換する |
| Take | 破棄責任とともに値を取り出す |

3つの能力は独立しており、どの能力もほかの能力を含意しない。

所有された`let`のルートはTakeを提供するがWriteは提供しない。`uniq/T`の参照先はWriteを提供するがTakeは提供しない。子のPlaceは宣言された完全な型を保ち、親のSemanticsを採用せず、共有経路から排他能力を回復せず、すべての層の制限を保つ（[§11.2.2](#s11-2-2)）。生存中のLoanは能力そのものを変えず、その使用を制限する。2つのPlaceが**重なる**のは、同一である場合、一方が他方のインライン部分である場合、または同じバイトを共有する場合である。同じアドレスにある別々のサイズゼロのPlaceは、一方が他方のインライン部分である場合に限り重なる。Loan検査が2つのPlaceを互いに素と扱う条件は[§15.6.2](#s15-6-2)で定める。

**アクセス指定子**（ローカル、フィールド、プロパティ、タプル要素、添字アクセス、たどった参照先、生ポインターの参照外し）は、Placeへのアクセス、ゲッター結果などの値取得、エラーのいずれかに解決される。これらは代替の分類であり、失敗時の代替処理ではない。Moveの失敗が借用やゲッター呼出しに変わることはない。カスタム、計算、要件の`get`は、宣言した結果を生成する（[§11.1](#s11-1)）。

**初期化**は空のPlaceに値を置く。**破棄**は値の寿命を終え、そのPlaceをUninitializedにする。記憶領域自体の寿命が終わった後はアクセスできない。値の寿命には、取得（CopyまたはMove）、配置（初期化または置換）、破棄がある。1回の代入でMoveと置換を両方行うこともある。Moveは移動元での2度目の破棄を防ぐ。借用や`raw/T`を破棄しても参照先は破棄しない。`rc/T`や`arc/T`を破棄すると所有参照を1つ解放する。TakeはPlaceの能力、Moveは値とその責任の移転、Consumeは[§15](#s15)の静的検査である。状態は[§15.1.1](#s15-1-1)に従う。**部分Move**はインライン部分を移転する（[§15.1.3](#s15-1-3)）。

```kimi
var pair: (string, i32) = ("a", 1)
let n = pair.1               // Read: Copy.
let name = pair.0@move       // Take: Partial Move.
// let all = pair@move       // Error: pair is not complete.
pair.0 = "b"                 // Write; pair is complete again.
```

<a id="s3-4-1"></a>
#### 3.4.1. アクセス経路と値の種類

| 経路 | 定義 |
| --- | --- |
| 直接 | 参照を解決しない経路。ローカルまたはパラメーター、静的記憶領域、一時Place、それらのインライン部分、または所有する`obj`ハンドルを通して到達したペイロード |
| 排他参照経由 | `uniq`または`objuniq`の参照を解決する経路 |
| 共有参照経由 | `ref`、`objref`、`rc`、`arc`の参照を解決する経路 |

生Place（`*p`、`p[n]`、それらの部分）はアクセス経路を持たない。その借用、Take、権限は生Placeの規則に従う（[§5.2](#s5-2)、[§5.2.1](#s5-2-1)、[§5.2.2](#s5-2-2)、[§5.2.3](#s5-2-3)）。経路の権限が、すべての借用を制限する（[§15.1.5](#s15-1-5)）。一時値の標準射影（フィールド、タプル要素、固定長配列の定数添字）は、一時値を実体化し、その一時Place内のPlaceを直接経路で指す。値の種類は下表に従う。

取得元の値の種類は、最初に一致する行で決まり、経路によって変わることはない。

| 値の種類 | 定義 |
| --- | --- |
| 借用値 | 外側のSemanticsが`ref`、`uniq`、`objref`、`objuniq`の値。格納済みか戻り値かを問わない。格納済みの参照はPlaceでもある |
| 所有Place | それ以外のPlace。`raw/T`や所有ハンドルを格納するものも含む |
| 所有一時値 | それ以外の一時値 |

**オブジェクト種の入力**とは、所有ハンドルまたは`objref`/`objuniq`の値をいう。それ以外は**値種の入力**である。**レシーバ式**とは、メソッド呼出し、アクセサー、関数値の直接呼出しのレシーバであり、[§7.3](#s7-3)に従って取得する。`Type.method(x)`の`self`引数、代入先、`match`や`for`のSubject（[§15.1.6](#s15-1-6)）はレシーバ式ではない。

<a id="s3-4-2"></a>
#### 3.4.2. 貸出し点

借用の**貸出し点**とは、借用が対象とする記憶領域であり、Loanのアンカーである（[§15.6](#s15-6)）。

- 明示的な`@ref`、`@ref/V`、`@uniq`、`@uniq/V`では、記述したPlaceそのもの。参照を格納する場合も同じであり、参照スロットを借用する。
- 借用値の再借用（暗黙、修飾なし、または`@follow@ref`、`@follow@uniq`と記述したもの）では、その参照先。
- 所有Placeでは、標準のフィールド、タプル要素、配列要素の射影で到達したPlace。
- 所有一時値ではその一時値、オブジェクト借用ではオブジェクト、たどったペイロードではペイロード、基底射影では基底部分オブジェクト。

排他借用では、貸出し点が[§15.1.5](#s15-1-5)に従って排他的に書込み可能でなければならない。`@uniq`で借用する参照スロットには、所有Placeと同じルートの規則を適用する。対象の妥当性を保つために保護される所有者は、貸出し点に含まれない（[§15.6.7](#s15-6-7)）。

```kimi
var number = 1
var other = 2
var reference = number@ref
let slot = reference@ref     // Borrows the slot `reference`.
// reference = other@ref     // Error: slot is used below.
inspect(slot)
```

<a id="s3-4-3"></a>
#### 3.4.3. 参照経路の選択

メンバーアクセス、添字アクセス、メソッドレシーバは、オペランドの型からPlaceを選ぶ。各層では、完全な型が必要な宣言を持つ場合に選択を止める。その宣言は直接宣言されたものでも、Contractへの適合や型同一性制約が提供するものでもよい。そうでなければ、`ref`層、`uniq`層、または許容集合が`value or valueborrow`に含まれるペア層（[§13.5.5](#s13-5-5)）で、参照先に進んで繰り返す。これらを**安全な値参照層**という。

- 選択に使うのは型だけである。その後の取得やLoanの失敗で別の層に戻ることはない。選択した各層の能力、Origin、Loanを保つ。
- オブジェクトハンドルとオブジェクト借用は、View Targetの宣言を持つため、そこで選択を止める（[§13.5.5](#s13-5-5)、[§9.6.1](#s9-6-1)、[§12.6](#s12-6)）。
- 同じ選択を、借用する`for`の入口（[§14.6.2](#s14-6-2)）と構造パターン（[§14.8.1](#s14-8-1)）にも適用する。値の読出しと比較は、代わりに終端を選ぶ。
- ジェネリック本体では、制約によって形が決まらない型パラメーターや関連型が終端となる。その制約が宣言する能力だけを使い、インスタンス化で別の層を選ぶことはない。宣言された等式によって証明された形はたどれる。ペア層は、その完全な型に必要な宣言がなければたどる。
- 修飾なしの取得、単一名の束縛（`let`または`var`宣言、単一の`for`スロット）、型推論は、参照先を選択せず、ペア層もたどらない。束縛パターンがたどるのは[§14.8.1](#s14-8-1)の規定に従う場合だけである。`matrix[f()][j] = value`のような更新要件は、選択した経路に沿ってだけ伝播し、`f()`の内部や関数・ゲッターの向こう側には伝播しない。

選択したPlaceの取得には、そのPlace自身の値の種類を適用し、親の種類は適用しない。`uniq/U`または`objuniq/U`を格納するPlaceは、経路が排他能力を与える場合に再借用する。レシーバ式の外で所有Placeを排他的に貸し出す場合、そのPlaceに`@uniq`または`@objuniq`が必要となる。`holder@uniq.items`が貸し出すのは`holder`であり、`items`ではない。添字アクセスは、用途に必要な能力に応じて`index`または`indexUniq`を選ぶ（[§4.7.1](#s4-7-1)）。

```kimi
// holder: a var Holder with Fields items: Array<i32> and link: uniq/Counter; values: var Array<Counter>; fill takes uniq/Array<i32>.
func update(node: uniq/Node)
    node.count += 1           // Selects the Field through the reference.

let link = holder.link        // Reborrow of a stored uniq/Counter.
fill(holder.items@uniq)
// fill(holder@uniq.items)    // Error: lending holder does not lend items.
values[0].bump()              // A Receiver Expression needs no spelling.
```

<a id="s3-5"></a>
### 3.5. CopyとMove

**Copy**は値を暗黙に複製し、取得元をInitializedのままにする。ユーザーコードの実行、メモリー割当て、参照カウントの変更、資源の取得は行わない。**Move**は値とその責任、または借用のアクセスを移転し、ユーザーコードを実行せず、移動元をMovedにする。**修飾なしの取得**とは、`@`操作のない式をいう。`T`を格納するPlace`P`には、最初に一致する行を適用する。

| 操作 | 結果 |
| --- | --- |
| 修飾なし、`T`のCopyを証明済み | Copy |
| 修飾なし、`T`が`uniq/U`または`objuniq/U` | 同じSemanticsで再借用（[§3.5.1](#s3-5-1)） |
| 修飾なし、それ以外のPlace | エラー。`@move`と書く |
| `P@move` | Copyかどうかによらず移転する。`P`はMovedとなる |
| `P@copy` | 明示的なCopy。`T`のCopyが証明されていなければならない |
| `P@ref`、`P@uniq`（または`@ref/T`、`@uniq/T`） | `P`自体の共有借用または排他借用 |
| 一時値の修飾なしまたは`@move`による取得 | 値を移転する。追加のCopyやMoveは行わない |

- Placeから移転する唯一の操作は`@move`であり、Move可能なPlaceを要求する（[§15.1.5](#s15-1-5)）。`@copy`は移転しない。`@owner/T`、`@obj/Base`、`@obj`、`@rc`、`@arc`は、その名前が示す操作を実行し（[§13.5.3](#s13-5-3)、[§13.6.2](#s13-6-2)）、Placeを暗黙にMoveしない。
- これらの規則は、初期化、代入、引数、結果（[§14.9.2](#s14-9-2)）、集成体の要素、ペイロード、既定値、キャプチャー項目（[§7.7.2](#s7-7-2)）、結果を捨てる操作（[§14.2.4](#s14-2-4)）という、すべての値位置に適用する。レシーバは[§7.3](#s7-3)に従う。期待型が固定されている場合、共通Adaptation（[§10.2](#s10-2)）は明示的取得の後に最大1つの暗黙Adaptationを許す。未証明のCopyが暗黙のMoveや借用に変わることはない。
- ジェネリック本体では、許容されるケースごとに表の適用を計画する（[§8.14](#s8-14)）。同じ名前の関数群は、重なるパラメーターを1つのモードで取得する（[§7.4](#s7-4)）。
- Copy能力は`let`/`var`やLoanとは独立している。Copyは読出し制限に従い、移転は生存中のLoanと競合してはならない。どちらも参照先の寿命を延長しない。取得済みのSubjectをパターン束縛に分配する場合、各束縛に`@move`は不要である（[§15.1.6](#s15-1-6)）。

```kimi
let a: i32 = 10
let b = a                 // Copy; a stays Initialized.
var node: obj/Node = makeNode()@obj
let owned = node@move     // Transfer; node becomes Moved.
// let bad = node         // Error: a bare Non-Copy Place never Moves.
```

<a id="s3-5-1"></a>
#### 3.5.1. 格納済み排他参照の再借用

修飾なしの取得の再借用の行は、明示的には`P@follow@uniq`または`P@objuniq`と書く操作である（[§13.5.6](#s13-5-6)）。`P`はMoveしない。子は格納された型を保ち、外側Originは親のOriginの範囲内になる（[§15.6.3](#s15-6-3)）。Loanの祖先関係、初期化状態、スロット保護、破棄依存を保ち、参照先と親Loanに依存する。子が生存する間は、親の競合する使用はエラーとなる。`P`やそれを含むPlaceのMove、およびそれらの借用も含む。排他借用では、その操作がその場で破棄する対象（`replace`）となる場合を除き、共有借用では子が排他的である間に、エラーとなる（[§15.6.3](#s15-6-3)）。子の最後の使用後は、親を再び使える。再借用には、経路を通した参照先への排他能力が必要である。

- 参照を保持する`let`束縛やパラメーターは再借用できる。所有された`let`の値が排他アクセスを与えない点は変わらない。
- `ref/(uniq/U)`のように共有経路で到達した`uniq`は再借用できない。修飾なしの取得はエラーとなり、期待型が`ref/U`なら共有アクセスを与える。
- 再借用するのは外側の排他参照だけである。`Option<uniq/U>`、struct、配列は全体を取得する。一方、`[r]`のような集成体リテラルは要素を個別に取得する。

記憶領域に保存される再借用は、形成時から有効となる。レシーバや引数を生成する再借用は、呼出し予約を開始する（[§15.6.7](#s15-6-7)）。`r: uniq/U`に対する`r@copy`はエラーとなる。`r@move`は参照を移転し（[§13.5.6](#s13-5-6)）、後のAdaptationで`r`が復元されることはない。直接期待型に合う排他参照は、再借用せずに移転する（[§10.2](#s10-2)）。`_ = r`は再借用して子を破棄する（[§14.2.4](#s14-2-4)）。

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

<a id="s3-5-2"></a>
#### 3.5.2. Copy能力

`Copy`はコンパイラー組込みのContractである（[§8.7](#s8-7)）。同じ形のユーザー定義Contractには、この効力はない。

| 型 | Copy |
| --- | --- |
| 整数、ラップ整数、浮動小数点、`bool`、`char`、Unit、Never | 満たす |
| `ref/T`、`objref/T`、`raw/T`、`Slice<T>{s}`、Function Item | 満たす |
| `FromEnd<T>`、`Start`、`End`、`Range<S, E>`、`ClosedRange<S, E>`、`ResolvedRange` | 満たす |
| `Kimi.Loan<T>` | `T`が満たす場合（[§15.3.5](#s15-3-5)） |
| 具象クロージャー、タプル、固定長配列 | 各キャプチャーまたは構成要素がすべて満たす場合 |
| `uniq`、`objuniq`、`obj`、`rc`、`arc`、`UniqSlice`、共通関数、Weak | 満たさない |
| `Array`、`Dictionary`、`owner/string` | 満たさない |
| ユーザー定義structまたはenum | 明示的に宣言した場合だけ |

structやenumは、`Self is Copy`または`Self is Copy when P`で明示的にCopyを宣言する（[§8.8](#s8-8)）。その制約と条件の下で、struct自身のフィールド型と直接の基底がCopyでなければならず、ユーザー定義の`drop`があってはならない。enumではすべてのペイロード型がCopyでなければならない。派生structはそれぞれ個別に宣言する必要があり、ペイロードのないenumにも宣言が必要である。ユーザーはCopyの本体を書けず、全フィールドがCopyというだけではCopyにならず、Copyがスライシングを起こすこともない。証明は許容されるすべての束縛で成立しなければならない。制約を新たに補うことはなく、インスタンス化が成功しても未証明の宣言が妥当になることはない。`ref/T`だけを格納するenumに`T is Copy`は不要である。`T is Copy`は制約であり、不明なCopyを仮定したりNon-Copyとして扱ったりしない。各許容ケースで、Placeを修飾なしで値取得するには、Copyの根拠か、排他参照型である証明が必要となる（[§8.14](#s8-14)、[§8.15](#s8-15)）。要素のPlaceは、格納された型を保つ（[§4.7](#s4-7)）。`rc`、`arc`、Weakの複製には`Kimi.Intrinsics.clone`を使う。

```kimi
enum CopyOption<T>
    Self is Copy when T is Copy
    Some(T)
    None

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)
```

<a id="s3-5-3"></a>
#### 3.5.3. 値の読出し

**読出し型**とは、Scalar、または`Position`か`PositionRange`を満たす型である（[§4.6.1](#s4-6-1)）。ジェネリック型が該当するのは、`PrimitiveInteger`（[§8.7.4](#s8-7-4)）、`Position`、`PositionRange`のいずれかを証明できる場合、または`T is PrimitiveInteger`を満たす`Wrapping<T>`である場合だけである。読出し型はCopyかつOwnedである。

読出し型`T`が必要な位置では、`T`で終わる`ref`や`uniq`の層を持つ値が、**値の読出し**によって`T`を供給する。安全な値参照層をたどり、終端の値をCopyする。この位置には、共通Adaptationの値位置（[§10.2](#s10-2)）、読出し型の結果源（[§14.9.2](#s14-9-2)）、組込み演算子のオペランド（範囲境界と接頭`^`のオペランドを含む）、`bool`条件、読出し型の所有レシーバ（[§7.3](#s7-3)）、数値・ラップ・ビット変換のオペランド、読出し型での同一型取得のオペランド（[§13.5.2](#s13-5-2)）がある。演算子は各オペランドの終端型から操作を選ぶが、取得の決定は算術経路に従い（[§13.3.5](#s13-3-5)）、共有のContract要件は数値入力を借用する。

オブジェクトSemantics、生ポインター、フィールドはたどらない。各層で初期化、能力、Loanを検査する。`s is value or valueborrow`の下の`factor: s/i32`では、`factor * 2`はすべての許容ケースで読出しを行う。変換は加えず、型未解決のリテラルは終端型に合わせる（[§12.3](#s12-3)）。オーバーフローと短絡評価の規則は変わらない。読出しはその時点の値を読み、借用を加えない。代入先を参照先へ振り替えることはない。Copyな参照先でも読出し型でない場合、Copyするには明示的な`@follow`が必要となる。比較は[§13.4](#s13-4)に従う。

```kimi
for number in numbers        // numbers: Array<i32>; number: ref/i32.
    let reference = number   // ref/i32.
    let doubled = number * 2 // i32.
    let wide = number@i64    // Value read, then conversion.
    // number += 1           // Error: the target is the reference, not its referent.
```

<a id="s3-6"></a>
### 3.6. 一時値

**一時値**は、持続するPlaceではない式の結果である。**実体化**は、記憶領域が必要なときに一時Placeを与える。再評価、Copy、メモリー割当て、寿命の延長は行わず、失敗したConsumeをReadに変えることもない。新しく所有された一時値は、ゲッター結果（[§11.5](#s11-5)）などの制限がなければ、排他的に書込み可能である。このため明示的な排他借用はできるが、Loan、Origin、読取り専用部分の制限を回避することはない。

一時値は、それを含む最も外側の式が終わるまで生存する。引数の一時値は呼出しが終わるまで生存する。`match`または`for`のSubject式の評価中に作った一時値は、その構文要素が終わるまで生存し、Subjectの後で生成の逆順に破棄する。それ以外の残る一時値も生成の逆順に破棄する。式を中断する制御転送は、[§16.2.1](#s16-2-1)に従って一時値を後始末する。Move後の一時Placeは、残った部分だけを破棄する。各条件検査は寿命の境界となる（[§14.2.3](#s14-2-3)）。既に借用である一時値は、そのOriginを伴う参照として取得する。それへの`@ref`は層を1つ加える。一時値の借用は一時値より長く生存できず、借用やスライスの形成は寿命を延長しない。結果の転送は、スコープ終了時の後始末より前に値を確保する（[§16.2](#s16-2)）。

```kimi
let view = makeResource()@ref
// inspect(view)                // Error: the temporary expired.
match makeHolder().item         // A Place in a temporary: shared borrow.
    .Some(let v) => inspect(v)  // v: ref/Item; the Holder temporary is still alive.
    _ => ()
```

<a id="s3-7"></a>
### 3.7. OriginとLoan

Originは領域を表し（[§15.2.1](#s15-2-1)）、LoanはどのPlaceをどのように借用したかを記録する（[§15.6](#s15-6)）。`origin a outlives b`と`origin a == b`は、それらの領域間の関係を表す（[§15.2.2](#s15-2-2)）。Originはシグネチャや型宣言に記述し、ローカルでは推論する。省略は[§15.4](#s15-4)に従う。

```kimi
func first(items: ref/Array<i32>) -> ref/i32 during items
    return items[0]@ref
```

<a id="s3-8"></a>
### 3.8. 型の関係と操作

型の関係は値操作を許可しない。借用、再借用、Copy、Moveは値操作のままであり、未解決のジェネリック情報やOrigin情報は互換性の根拠にならない。**正規化後の同一性**は、正規化（[§3.3.2](#s3-3-2)）後の2つの型について、構造、宣言、型引数、Semantics、Origin束縛を比較する。束縛されたOriginパラメーターは、バインダーによって対応付ける。異なる公称宣言が同一になることはない。オブジェクトの実行時同一性（[§3.3.1](#s3-3-1)）は別の関係である。

**部分型。** `A <: B`が成立するのは、正規化後の`A`と`B`がOrigin束縛を除いて同一であり、各Origin位置がその変性（[§15.3.5](#s15-3-5)）を満たす場合に限る。共変位置では`a outlives b`、反変位置では`b outlives a`、不変位置では`a == b`を要求する。`during`のないraw層は、すべてのOriginがoutliveするOriginを指定したものと扱う。そのため`raw/T during a <: raw/T`となる（[§5.7](#s5-7)）。これが唯一の部分型規則である。関数型間の型関係もこの規則だけに従う。Neverはほかの型の部分型ではない。`Wrapping<T>`と`T`、`uniq/T`と`ref/T`、派生型と基底型の間に型関係はない。各Origin位置は必要なOrigin関係を生み出す（[§15.6.1](#s15-6-1)）。したがって、構造が一致する型合わせが型不一致になることはない。部分型関係は操作を挿入せず、寿命を延長しない。

**Neverの型合わせ。** Never式は、値変換なしで、ほかの点で妥当な任意の期待値型に合う。これは部分型関係ではない。完了しない経路では外側の値操作は実行されず、転送のオペランドはそれ自身の結果境界に対して検査する（[§14.9](#s14-9)）。

その他の関係と操作は別の節が定める。Callableの互換性と結果の互換性（[§10.7](#s10-7)）、期待結果による絞込み（[§10.3](#s10-3)）、暗黙Adaptation（[§10.2](#s10-2)）、明示的Adaptation（[§13.5.3](#s13-5-3)）、オブジェクトのアップキャスト（[§13.6.1](#s13-6-1)）、借用（[§13.5.6](#s13-5-6)）、数値変換（[§13.5.4](#s13-5-4)）がある。暗黙Adaptationは取得後に最大1つだけを許し、連鎖させない。`i32`は`i64`の部分型ではない。型が完全一致しても、記憶領域がMoved、アクセスが利用不能、Loanが競合するなどの理由で、取得の合法性（[§15.1.5](#s15-1-5)）に必要なCopy、Move、借用の要件を満たせないことがある。この失敗で名前検索やオーバーロード選択をやり直すことはない。

```kimi
func pick(long: ref/i32 during a, short: ref/i32 during b) -> ref/i32 during b
    origin a outlives b
    return long                          // OK: ref/i32 during a <: ref/i32 during b.

func f(handler: (i32) -> Never) -> ()
    let g: (i32) -> i32 = handler@move   // Error: (i32) -> Never is not a subtype of (i32) -> i32.
```

---

<a id="s4"></a>
## 4. 配列、添字アクセス、スライス

[仕様の索引](#contents)

固定長配列`[N of T]`はコンパイル時の長さ`N`を持ち、要素をインラインに格納する。`Array<T>`は可変長の記憶領域を所有する。`Slice<T>{source}`と`UniqSlice<T>{source}`は、連続する要素の共有ビューと排他ビューである。`Dictionary<K, V>`も本章の添字アクセスと変更の規則に従う。

<a id="s4-1"></a>
### 4.1. 固定長配列

`[N of T]`の同一性には、評価済みの長さと、SemanticsとOriginを含む完全な要素型が含まれる。`[(2 + 2) of i32]`は`[4 of i32]`と同じである。長さの異なる配列間、固定長配列と`Array<T>`の間、所有配列とスライスの間に暗黙変換はない。

要素は添字順にインライン格納し、ヒープへのメモリー割当ては行わない。入れ子の配列では、最も内側の添字の変化が連続する。`d = stride(T)`とすると、要素`i`のオフセットは`i * d`、アラインメントは`alignment(T)`（`N = 0`でも同じ）、サイズと配列のストライドは`N * d`となる。`d = 0`では、異なる要素が同じアドレスを共有してもよい。サイズはターゲットの上限と非負の`isize`に収まるか検査し、具象レイアウトを表現できなければコンパイル時エラーとなる。`[0 of T]`のサイズはゼロであり、何も初期化・破棄しない。ただし`T`の検査は行い、`[0 of Self]`をインライン格納するstructは再帰レイアウトのエラーとなる。ポインターや参照の参照先は、インライン格納の辺を作らない。

```kimi
let a: [4 of i32] = [1, 2, 3, 4]
let b: [(2 + 2) of i32] = a    // Same Type.
let c: Array<i32> = a          // Error: no conversion from a fixed array.

struct Node
    var children: [0 of Node]  // Error: recursive inline layout.
```

<a id="s4-2"></a>
### 4.2. 長さの式

長さは、ターゲットの`isize`で表せる非負のコンパイル時整数である。ジェネリックな長さは、インスタンス化まで記号のまま保持する（文法：`ArrayLength`、`LengthExpression`、[§F.5](#sf-5)）。

長さの式には、整数リテラル、長さパラメーター、定数として読める束縛、グループ化、単項`+`と`-`、二項`+`、`-`、`*`、`/`、`%`を使える。**定数として読める束縛**とは、初期化子が再帰的にこれらの形式だけを使う、整数のローカル`let`、またはアクセス可能な標準`get`を持つ静的な格納プロパティである。パラメーター、`var`、インスタンスフィールド、カスタム・計算アクセサー、呼出し、循環する初期化子は除外する。依存する長さでは、その束縛を初期化子で置き換える（正規化、[§4.4.2](#s4-4-2)）。

評価には検査付き算術を使い、初期化子やゲッターは実行しない。型付き定数はその型を保ち、長さパラメーターの型は`isize`とする。リテラルだけの部分式は、確定したオペランド型（[§12.3](#s12-3)）があればその型、なければ`isize`を使う。異なる整数型同士は組み合わせられない。中間値は負でもよい。したがって`let Small = 4`に対し、`[(Small * 2) of u8]`は`i32`で計算する。最終的な長さが負または範囲外の場合、オーバーフロー（中間値を含む）、整数でない値、ゼロ除算はコンパイル時エラーとなる。

ルートレベルの束縛は起動時のローカルである（[§22.4](#s22-4)）。共有する長さ定数はgroupに置く。公開シグネチャの長さに非公開定数を使ってもよい。その値は公開APIの一部となり、長さを変えると、それを使うすべての型が変わる。

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

<a id="s4-3"></a>
### 4.3. 配列の構築

<a id="s4-3-1"></a>
#### 4.3.1. 配列リテラルと注釈の穴

期待型が固定長配列型なら、配列リテラルはその型を構築する。要素数はちょうど`N`でなければならず、要素はソース順に取得する（[§3.5](#s3-5)）。それ以外では、独立した配列リテラルは`Array<T>`を構築し、呼出し引数のリテラルは候補ごとに型を合わせる（[§4.4.1](#s4-4-1)）。空のリテラルには既知の要素型が必要となる。

初期化するローカルの`let`や`var`では、固定長配列の注釈に記した構造の任意の深さで、長さ、要素型、または両方に`_`を書ける。すべての穴は初期化子だけで決まらなければならない。長さの穴にはリテラルの要素数または初期化子の固定長配列の長さを使い、要素型の穴には通常の要素の根拠と数値の既定型を使う。内側のリテラルに固定長配列の期待型を記していなければ、`Array<T>`を構築する。すべての要素が一致しなければならず、不一致はエラーであり、`Array<T>`への切替えはしない。注釈に記した部分は、呼出し先のシグネチャを通じた推論も含め、初期化子への期待となる。未初期化宣言、シグネチャ、フィールド、静的記憶領域、明示的ジェネリック引数では穴を使えない。

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

<a id="s4-3-2"></a>
#### 4.3.2. 最初の構築

注釈だけの宣言はUninitializedのままであり、ゼロで埋めない。配列全体の初期化子または代入で構築する。構築前の部分ごとの書込みはエラーとなる（[§15.1.2](#s15-1-2)）。

```kimi
var pending: [2 of i32]
pending[0] = 1   // Error: pending was never constructed.
pending = [1, 2]
```

<a id="s4-3-3"></a>
#### 4.3.3. 同じ値で埋める構築

`[Length of value]`は固定長配列を構築する。期待型が`Array<T>`なら不一致となる。`Length`は[§4.2](#s4-2)に従い、要素型はCopyでなければならない。`value`は長さゼロでもちょうど1回評価・取得し、各要素へCopyする。配列はどの長さでも`value`のLoan（[§15.6](#s15-6)）を伴い、`[0 of v]`もそのLoanを保つ。

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false] // [8 of bool]
var x = 1
let none = [0 of x@ref]  // [0 of ref/i32] carries x's Loan.
// x = 2                 // Error: none is used below.
inspect(none)
```

<a id="s4-3-4"></a>
#### 4.3.4. `noinit`

宣言の`=`の後の初期化子全体が、括弧で囲まれていない`noinit`である場合、名前検索より前に`noinit`として認識する。格納プロパティの初期化子やパラメーターの既定値も含む。妥当なのは、Unsafeブロック（[§14.3](#s14-3)）内の、穴のない明示的な固定長配列注釈を持つローカル`var`だけである。それ以外ではエラーとなり、名前としては扱わない。`(noinit)`は通常の式である。

別名解決後の要素型は、`owner`のScalar、すなわち整数型、ラップ整数型、浮動小数点型、`bool`、`char`のいずれかでなければならない。ジェネリック定義は`PrimitiveInteger`などの制約からこれを証明する必要があり、`Copy`だけでは足りない。この宣言は配列全体の構築を完了するが、要素への最初の格納を省く。その後は、初期化済み配列のすべての規則を適用する。

プログラムは、要素を読む前に書き込まなければならない。先に読むと、関数、参照、ビュー、生ポインター経由でも未定義動作となる。残存ビットは書込みとみなさない。配列全体のCopy、Move、値渡しは、すべての要素を読む。要素への代入、アドレス形成、借用、`slice` / `sliceUniq`、メタデータへのアクセス、破棄は要素を読まない。最適化や呼出し規約によって、プログラムが読むより前に要素を読んではならない。

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

<a id="s4-4"></a>
### 4.4. 関数の長さパラメーター

<a id="s4-4-1"></a>
#### 4.4.1. 宣言と推論

長さスロットは`<length N>`と宣言する。単なる`<N>`は型スロットである。長さスロットは値名前空間の非負の`isize`定数であり、GenericArityに1つとして数える。長さスロットを持てるのは関数だけであり、型宣言に値パラメーターはない。

明示的なジェネリック引数リストは、`length`を付けない長さの式または型を、すべてのスロットに順番どおり渡す。それ以外では各候補内で、既知の固定長配列型と、その候補に合わせたリテラルから長さと要素型を推論する。すべての根拠は一致しなければならず、型が確定したArrayの型を決め直すことはない。期待型は未解決の部分だけを埋める。長さの方程式を逆向きに解くことはない。`[(N * 2) of T]`は`N`が分かってから検査する。本体、後の使用、候補順から長さを推論することはない（[§10.5](#s10-5)）。候補比較では、固定長リテラルと動的リテラルのどちらも優先しない。

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

<a id="s4-4-2"></a>
#### 4.4.2. 形成義務と正規化

シグネチャの各長さ式`E`は、`ValidLength(E)`、つまり`E`を検査付き演算で評価すると非負の`isize`になるという義務を生む。依存のない式が失敗すれば宣言を拒否する。依存する義務は公開された適用条件（`N - 1`には`N >= 1`が必要）となり、具象呼出しで推論後、候補比較前に検査する。偽なら候補を除外し、順位付けには使わない。選択後のレイアウト失敗で選択をやり直すことはない。

本体は公開条件を満たすすべての束縛について検査する。本体内の長さと呼出し先の条件は、具象評価、長さスロットの非負性、同一の正規化済み義務だけを使って、宣言から導けなければならない。本体から条件を推論しない。具象レイアウトはインスタンス化時の義務である（[§8.15](#s8-15)）。

**正規化。** シグネチャ、依存型、義務は正規化済みの長さ式を比較し、具象型は評価済みの長さを比較する。長さ式内の定数として読める束縛は、その初期化子を表す。正規化前に、型付き初期化子で再帰的に置き換える。正規化した長さに残るのは、定数、長さスロットの位置、演算子だけである。

1. グループ化を除き、依存のない部分式を評価し、長さパラメーターをスロット位置で置き換える。
2. 整数の各二項`+`と`*`のオペランドを、ノードの種類、型の同一性、定数値、スロット位置の順で整列する。
3. すべての型と演算子、および`-`、`/`、`%`のオペランド順を保つ。再結合、分配、相殺は行わない。

したがって`N + (2 + 2)`と`4 + N`は一致するが、`(N + 2) + 2`と`N + 4`は一致するとは限らない。

```kimi
func reordered<length N>(value: [(N + 4) of u8]) -> [(4 + N) of u8] => value

func f<length N>(a: [(N + 1) of u8])
    let k: isize = N + 1
    let b: [k of u8] = a // Valid: k normalizes to N + 1.
```

<a id="s4-5"></a>
### 4.5. 配列の所有権

固定長配列がCopyであるのは、要素型がCopyである場合に限る（[§3.5.2](#s3-5-2)）。Ownedは導出し、要素のOriginとLoanを保つ。部分Moveは[§15.1.3](#s15-1-3)に従う。後始末は、残る初期化済み要素を添字の降順に破棄する（[§16.3.2](#s16-3-2)）。中断された構築は[§16.2.1](#s16-2-1)に従う。

`Array<T>`はNon-Copyであり、表現可能なレイアウトを持つ完全な要素型を受け入れる。`T`のOrigin依存と、取得した要素のLoan（[§15.4](#s15-4)）を、削除やclearの後も保つ（[§4.9.6](#s4-9-6)）。要素位置はOriginの変性を保つ。`uniq/Array<T>`は不変である。

所有配列の共有ビューは、範囲添字、`slice()` / `slice(range)`（[§4.8.4](#s4-8-4)）、[§4.8.2](#s4-8-2)の読取り操作で明示的にのみ形成する。固定長配列のサイズは変更できず、Arrayの変更（[§4.9](#s4-9)）にはArray全体への排他アクセスが必要となる。スライスは要素の変更を許さない。配列、排他的な要素Place、排他列挙（[§14.6.2](#s14-6-2)）、UniqSliceのいずれかを使う。

```kimi
var values: [4 of i32] = [10, 20, 30, 40]
let copy = values                     // Copy: i32 is Copy.
let middle: Slice<i32> = values[1..3] // Explicit shared view.
let all: Slice<i32> = values          // Error: no implicit conversion.
```

<a id="s4-6"></a>
### 4.6. 位置と範囲

位置と範囲はKimiの型である（[§22.1](#s22-1)）。その名前はキーワードではないが、`^`と範囲構文は常にKimiの型を構築する。記憶領域のOriginやLoanは保持しない。

<a id="s4-6-1"></a>
#### 4.6.1. 位置

**位置**は長さ`L`の対象の境界を表し、`[0, L]`内の整数に解決される。**要素位置**は、さらに`q < L`を満たす。閉じたContractである`Kimi.Position`（[§8.7](#s8-7)）を満たす型は、12種類の整数型、`FromEnd<T>`、`Start`、`End`だけである。`T is PrimitiveInteger`は`T is Position`を含意する。

```kimi
public contract Position: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<isize>
```

`L < 0`では、すべての位置の解決に失敗する。それ以外では、切捨てを行わずに数学的な値を比較する。

| 型 | 表記 | 解決できる条件 | 結果 |
| --- | --- | --- | --- |
| 整数`n` | 整数値 | `0 <= n <= L` | `n` |
| `FromEnd<T>` | `^x`、`T is PrimitiveInteger` | `0 <= offset <= L` | `L - offset` |
| `Start` / `End` | 範囲の始点／終点の省略 | 常に | `0` / `L` |

`FromEnd<T>`は読取り専用の`offset: T`を持つ。`Start`と`End`はサイズゼロである。この3つは`tryResolve`と`resolve(self: Self, length: isize) -> isize`を提供し、公開コンストラクターは持たない。整数にはメンバーを加えない。整数の`tryResolve`には`P is Position`経由でのみ到達でき、`resolve`はない。構築では何も検査しない。`^(-1)`は妥当だが、解決には失敗する。`FromEnd<T>`はオフセットを比較し、`Start`の値同士、`End`の値同士はそれぞれすべて等しい。順序、算術、変換はないので、`^n + 1`は型エラーとなる（`^(n + 1)`と書く）。位置への参照はその値として読み出す（[§3.5.3](#s3-5-3)）。`Position`に制約された型引数は、参照先型として推論する（[§10.2.2](#s10-2-2)）。

```kimi
let values: [4 of i32] = [10, 20, 30, 40]
let last = ^1        // FromEnd<i32>
let a = values[last] // 40
let n: u8 = 1
let b = values[n]    // 20: every integer Type is a position.
let c = values[^0]   // Aborts if executed: ^0 is not an element position.
let d = ^1 + 1       // Error: FromEnd<i32> has no addition.
```

<a id="s4-6-2"></a>
#### 4.6.2. 範囲

**範囲**とは位置の区間であり、格納、受渡し、反復、添字としての使用のいずれでも同じ意味を持つ。優先順位は[§F.9](#sf-9)で定める。

| 構文 | 型 |
| --- | --- |
| `a..b` / `a..=b` | `Range<S, E>` / `ClosedRange<S, E>`：`b`を含まない／含む |
| `a..` | `Range<S, End>` |
| `..b` / `..=b` | `Range<Start, E>` / `ClosedRange<Start, E>` |
| `..` | `Range<Start, End>` |

形は構文だけで決まる。構築では何も検査せず、逆転した範囲も妥当な値である。各境界は`Position`型の値であり、参照越しに読み出せる（[§3.5.3](#s3-5-3)）。両境界の型は独立している。リテラルだけでない境界（[§12.3](#s12-3)）には、それ自身の既知の型が必要となる。リテラルだけの境界（`a`がリテラルだけなら`^a`も同じ）は、最初に適用できる規則で型を決め、不一致でも後の規則を試し直さない。

1. 範囲の期待型にある`S`または`E`。
2. もう一方の境界の確定した整数型（`FromEnd<T>`なら`T`）。
3. 既定型`i32`。長さの文脈以外での整数リテラルの既定型である（[§12.3](#s12-3)）。位置専用の既定型はないので、`0..10`、`^1`、`values[3]`は`i32`を使う。

`Range<S, E>`と`ClosedRange<S, E>`はCopy、Owned、Equatableを満たし、読取り専用の`start: S`と`end: E`、`tryResolve`、`resolve`、反復（[§4.6.4](#s4-6-4)）を持つ。構築できるのは範囲構文だけであり、`length`や`isEmpty`はない。異なる形は異なる型であり、比較できない。

`ResolvedRange`は、`0 <= start <= end`を不変条件とする独立した型である。同じ境界の`Range<isize, isize>`と意味は同じだが、相互変換はない。Copy、Owned、Equatableを満たし、`length`と`isEmpty`も持つ。生成するのは`indices`（[§4.7.3](#s4-7-3)）と範囲の解決だけであり、十分な長さを持つ任意の対象に適用できる。

```kimi
let a = 1..^1                 // Range<i32, FromEnd<i32>>
let c = 0@i32..2@i64          // Range<i32, i64>: resolvable, not iterable.
let n: i64 = 2
let d: Range<i64, u8> = n..10 // 10 is u8 from the expected Type.
let f = (0..3) == (0..=2)     // Error: different range Types.
```

<a id="s4-6-3"></a>
#### 4.6.3. 解決

```kimi
public contract PositionRange: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>
```

`PositionRange`は閉じたContractであり、満たすのは3種類の範囲型だけである。解決は境界と長さだけを読む。解決済みの境界を`s`と`e`、終点を要素位置として解決したものを`q`とすると、`Range`は`s <= e`を要求して`[s, e)`を返し、`ClosedRange`は`s <= q`を要求して`[s, q + 1)`を返し、`ResolvedRange`は`end <= L`を要求する。`L < 0`または位置の解決に失敗した場合は解決に失敗し、逆転した区間を空区間に丸めることはない。

整数以外の位置型と範囲型は、`tryResolve`と対になる名前として`resolve`を公開する（[§4.9.2](#s4-9-2)）。位置または範囲を取るAbort型の操作（添字アクセス、スライス、`slice`/`sliceUniq`の範囲、`resolve`、`insert`、`remove`、`swapRemove`、`swap`など）は、すべて同じ解決を行い、`None`なら`KIMI_E_INDEX_BOUNDS`でAbortする（[§22.5.2](#s22-5-2)）。try接頭辞の操作が`None`を返すのは、その操作自身の境界違反だけである。

```kimi
(1..=3).tryResolve(5) // Some(1..4)
(2..=1).tryResolve(5) // None: reversed.
(..=^0).tryResolve(5) // None: ^0 is not an element position.
(^7..).tryResolve(5)  // None: 7 > 5.
```

<a id="s4-6-4"></a>
#### 4.6.4. 範囲の反復

`Range<S, E>`と`ClosedRange<S, E>`は、`S is PrimitiveInteger`かつ`E is S`の場合（[§8.8](#s8-8)）、`RangeIterator<S>`と`ClosedRangeIterator<S>`によって`Iterable`、`UniqIterable`、`IntoIterable`に適合する。`ResolvedRange`は`RangeIterator<isize>`によって常に適合する。境界を省略した範囲、末尾からの境界を持つ範囲、2つの異なる整数型を持つ範囲は反復できない。入口は境界をCopyし、範囲のLoanを保持しない。借用する入口の`IteratorType(source)`は`source`に依存しない。`start > end`なら入口でAbortする（[§22.5.2](#s22-5-2)）。逆転した範囲は空でも降順でもない。値は1ずつ増加する。閉区間は`end`を最後に返し、`T`の最大値でもその先を計算しない。イテレーター型は[§22.2.2](#s22-2-2)に従う。無限範囲、降順範囲、刻み付き範囲は将来の対象とする（[§D](#sd)）。

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

<a id="s4-7"></a>
### 4.7. 添字アクセス

`[N of T]`、`Array<T>`、`Slice<T>`、`UniqSlice<T>`には、要素と範囲への添字アクセスが組み込まれている。生ポインターが受け取るのは`isize`オフセットだけであり、境界検査は行わない（[§5.3](#s5-3)）。

<a id="s4-7-1"></a>
#### 4.7.1. 添字式の解決

単一要素の添字式は、ユーザー定義型やDictionaryも含め、すべて次のKimi Contractを通して解決する。その要件は選択した要素への参照を返す。キーは共有借用し、消費しない。参照型やハンドル型のキーは`key@ref`と書く（[§10.2](#s10-2)）。

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
```

両要件は、同じキーに対して同じ要素への参照を返す。`indexUniq`は格納された型を変えずに排他アクセスを与える。`receiver[key]`は次のように静的に解決する。

1. 参照経路の選択（[§3.4.3](#s3-4-3)）後、レシーバの型から`Indexable<Key>`への適合を得る。具象シーケンス型（固定長配列、Array、Slice、UniqSlice）ではキーは位置であり、[§4.6.2](#s4-6-2)で型を決め、正規化する（後述の「位置の正規化」）。それ以外のレシーバでは、キーは各適合のパラメーター`key: ref/Key`の引数となる。共通Adaptation（[§10.2](#s10-2)）でそのパラメーターに合うとき、その適合に合う。リテラルだけのキーは、既定型を適用する前に[§12.3](#s12-3)に従って型を合わせる。合う適合はちょうど1つでなければならず、なければ型エラー、複数なら曖昧性エラーとなる。リテラルの既定型で適合を選ぶことはなく、期待結果から`Key`を推論することもない。
2. 用途が能力を決める。修飾なしの取得（[§3.5](#s3-5)）、固定された期待型へのAdaptation（[§10.2](#s10-2)）、書込み、明示的な借用、Subject（[§15.1.6](#s15-1-6)）、レシーバ式（[§7.3](#s7-3)）がある。
3. 読出しと共有借用は`index`を、更新、排他借用、排他的な再借用は同じ適合の`indexUniq`を選ぶ。

適合を通す添字式は、選択した呼出しが返す参照の参照先、つまり`@follow`が選ぶPlace（[§13.5.5](#s13-5-5)）を指す。格納型は`Element`であり、能力と依存はその参照のものとなる。Takeは提供しない。`names.index(1)`のように書いた呼出しは、その参照を値とする通常の呼出しである。参照をたどるのは添字演算子だけであり、関数名や結果型によってたどることはない。

`indexUniq`を選ぶと、`@uniq`なしでレシーバを排他的に取得する。ただし使える能力は経路が既に持つものだけであり、`let`で所有された経路や共有経路はそれを提供できない（[§15.1.5](#s15-1-5)）。失敗してもモードや候補を選び直さない。呼出し中はレシーバとキーのLoanを保つ。要素は戻り参照の依存先に依存し、キーにも参照を保持する一時値にも依存しない（[§10.2](#s10-2)、[§13.5.6](#s13-5-6)、[§15.6.3](#s15-6-3)）。`Element`内のOriginとその実際のLoanは通常どおり保つ。

**位置の正規化。** 具象シーケンス型では、`x[k]`は`k`を要素位置`q`に解決し、境界検査を1回行って`Indexable<isize>`経由で`x[q]`を実行する。先にキーを読み出して解決し、その借用は保持しない。そのため`indexes[indexes[0]] = x`は妥当である。ジェネリックな`S is Indexable<Key>`とその他のすべての型は、手順1で適合を選ぶ。したがって`S is Indexable<isize>`経由で`s[^1]`は使えない。

| 対象 | 適合を通して得る要素 | 範囲の結果 | 依存先 |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]`、`UniqSlice<E>` | `ref/E`または`uniq/E`の参照先 | `Slice<E>` | レシーバの借用`a` |
| `Slice<E>` | `ref/E during s.source`の参照先 | `Slice<E>` | `s.source` |
| `Dictionary<K, V>` | `ref/V`または`uniq/V`の参照先。不在ならAbort | なし | `a`。キーには依存しない |

Array、固定長配列、UniqSliceは`UniqIndexable<isize>`に、Dictionaryは`UniqIndexable<K>`に、Sliceは`Indexable<isize>`に適合する（[§8.6.1](#s8-6-1)）。範囲添字はIndexable群の一部ではなく、範囲全体への代入は存在しない。

固定長配列への直接の要素アクセスは組込み射影であり、配列をCopyせず、全体として借用もしない。Arrayの要素アクセスは`index`または`indexUniq`を呼び出し、Array全体を借用する。

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

<a id="s4-7-2"></a>
#### 4.7.2. 要素のPlace

添字式は、選択した能力を持つ要素Placeを表す。適合を通す場合は、戻り参照の参照先である（[§4.7.1](#s4-7-1)）。形成時に何もCopyしないため、`matrix[1][2] = 10`は内側の配列をCopyしない。`values[i] = v`は初期化または置換を行い、`@ref`と`@uniq`はPlaceを借用する。`@move`による移転は、所有された固定長配列の静的Move経路を通してのみ行える。この経路を形成するのは範囲内の整数リテラル添字だけである（[§15.1.3](#s15-1-3)）。修飾なしの読出しは修飾なしの取得である（[§3.5](#s3-5)）。Copy要素はCopyし、格納済みの`uniq/U`や`objuniq/U`は`indexUniq`を通して再借用する。それ以外のNon-Copy要素には借用が必要となる。修飾なしの読出しはMoveしない。固定された期待型`ref/T`は所有要素を暗黙に借用する（[§10.2](#s10-2)）。参照やハンドルを格納する要素は、代わりに再借用などのAdaptationを行う。

```kimi
// resources: a complete owned [2 of Resource]; i: isize; refs: a writable Array<uniq/Node>.
inspect(resources[i]) // Shared borrow at a ref/Resource parameter.
normalize(refs[0])    // indexUniq; the stored reference is Reborrowed.
```

<a id="s4-7-3"></a>
#### 4.7.3. 長さのメタデータ

固定長配列、Array、Slice、UniqSliceは、読取り専用の`length: isize`と`indices: ResolvedRange`を提供する。固定長配列以外は`isEmpty: bool`も提供する。これらは要素を読まない。ただし固定長配列でも、レシーバへのアクセスは検査する。結果はLoanを保持しない。`x.indices`は`(..).resolve(x.length)`のスナップショットであり、Arrayのサイズを変更しても更新されない。

```kimi
var values: Array<i32> = [1, 2, 3]
let range = values.indices // 0..3
values.append(4)           // range is still 0..3.
```

<a id="s4-7-4"></a>
#### 4.7.4. 評価、失敗、更新

レシーバと添字は、評価順（[§12.2](#s12-2)）に従ってそれぞれ1回評価する。範囲は、記述した境界を始点から評価する。レシーバの位置を求めるのは1回だけであり、既存のPlaceはその場で使い、所有一時値だけを実体化する。添字の評価中、レシーバの記憶領域は読めるが、変更、移動、破棄、再割当てはできない。連鎖する要素のCopyは、周囲の後続オペランドより前に完了する。Sliceレシーバのハンドルは先にCopyし、UniqSliceハンドルは借用する。

要素添字と範囲添字は境界が不正ならAbortし、`tryGet`、`trySlice`、`trySplitAt`は`None`を返す。`values[L..L]`と`values[^0..]`は空であり、要素位置の`L`や`^0`、閉区間の終点`^0`は不正である。範囲外の添字は、それだけではコンパイル時エラーにならない（[§17.5](#s17-5)）。

代入は、代入先を特定する前に右辺を確保する（[§13.7.1](#s13-7-1)、[§13.7.2](#s13-7-2)）。複合代入、インクリメント、デクリメントは、その後に境界検査、読出し、計算、書戻しを各1回行う。要素書込みの排他Loanは、境界の解決から配置まで続く。交換操作は[§15.7](#s15-7)に従う。`Kimi.Intrinsics.swap`には静的な非重複性が必要となる。

```kimi
let values: [3 of i32] = [1, 2, 3]
let r = (-1)..2               // Valid: construction checks nothing.
let s = values[r]             // Aborts in resolution.
let t = values.trySlice(2..5) // None.
```

<a id="s4-8"></a>
### 4.8. スライスとビュー

<a id="s4-8-1"></a>
#### 4.8.1. Slice

`Slice<T>{source}`は、初期化済みの連続した`T`の領域を表すCopyなビューである。長さは型の一部ではない。範囲アクセスは常にSliceを返す。添字はゼロから始まり、末尾からの位置と再スライスにはSlice自身の長さを使う。入れ子配列の行は`Slice<[N of T]>`となり、平坦化したSliceにはならない。要素型が異なるスライス間に変換はない。

Originは基礎となる記憶領域の寿命を、Loanは競合するPlaceを記録する。Copy、再スライス、イテレーター、要素参照は両方を保ち、ハンドル変数を借用しない。配列由来のビューが生存する間は、配列Place全体にLoanを持つ（[§15.6.2](#s15-6-2)）。再スライス後や空の場合も同じであり、競合する書込み、Move、破棄、再割当てを拒否する。

Sliceは要素を所有せず、`T`はOwnedでなくてもよい。`source`は短縮できるが延長できない。OwnedはOwnedOriginsに従う（[§15.2.3](#s15-2-3)）。Sliceの格納には通常の格納規則（[§15.4](#s15-4)）を適用する。静的記憶領域に格納できるのはOwnedの場合だけであり（[§11.8](#s11-8)）、一時値も含め、基礎となる記憶領域の寿命を延長することはない（[§3.6](#s3-6)）。`var`のSliceが許すのはハンドルの再代入だけであり、`uniq/Slice<T>`は要素ではなくハンドルを借用する。スライスに等価比較はない。

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

<a id="s4-8-2"></a>
#### 4.8.2. Sliceの操作

Sliceのメンバーはハンドルを値で受け取り、結果は`s.source`を保つ。

| 操作 | 契約 |
| --- | --- |
| `toArray(self: Self) -> Array<T>`、`T is Copy` | 各要素を順に1回ずつ、新しい記憶領域へCopyする |
| `tryGet<P>(self: Self, index: P) -> Option<ref/T during source>`、`P is Position` | `index`が要素位置でなければ`None` |
| `trySlice<R>(self: Self, range: R) -> Option<Slice<T> during source>`、`R is PositionRange` | `range`を解決できなければ`None` |
| `splitAt<P>(self: Self, index: P) -> (Slice<T> during source, Slice<T> during source)`、`P is Position` | `index`を1回だけ`p`に解決し、`(s[..p], s[p..])`を返す |
| `trySplitAt<P>(self: Self, index: P) -> Option<(Slice<T> during source, Slice<T> during source)>` | 同じ結果、または`None` |
| `contains(self: Self, value: ref/T) -> bool`、`T is Equatable` | `value`と等しい要素があるか |
| `firstIndex(self: Self, of: ref/T) -> Option<isize>`、`T is Equatable` | `of`と等しい要素の最初の添字 |
| `firstIndexWhere<F>(self: Self, predicate: F) -> Option<isize>`、`F is Callable<(ref/T) -> bool>` | `predicate`が`true`を返す最初の添字 |

検索は添字順に要素を訪れ、最初の一致で止まり、共有参照を渡す。`tryGet`は常に参照を返す。固定長配列とArrayは、`x[..]`と同じ`tryGet`、`trySlice`、`splitAt`、`trySplitAt`を提供し、結果は`during self`となる。Sliceの要素Placeは共有専用である。`toArray`はすべての要素を読み（[§4.3.4](#s4-3-4)）、メモリー割当てを最大1回、空なら0回行う。`T`内の依存は保つが、外側の借用は保持しない。

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

<a id="s4-8-3"></a>
#### 4.8.3. Sliceの反復

Sliceは`Iterable`、`UniqIterable`、`IntoIterable`に適合する。すべてのモードで、項目は添字順の`ref/T during source`となる（[§14.6.2](#s14-6-2)）。項目はイテレーターではなく基礎となるスロットを借用し、`next`の呼出しをまたいで保持できる。要素内に格納された内側の参照は、スロットの借用とは別に独自のOriginを保つ。

```kimi
let data: i32 = 10
let refs: [1 of ref/i32] = [data@ref]
let s: Slice<ref/i32> = refs[..]
let value = s[0]       // Copies the inner reference to data.
for element in s
    ()                 // element: ref/(ref/i32), borrowing the slot of refs.
```

<a id="s4-8-4"></a>
#### 4.8.4. ビューとUniqSlice

各`slice`と`sliceUniq`には、全体ビューのオーバーロードと、`R is PositionRange`を受け取る範囲オーバーロードがある。後者は境界が不正ならAbortする。

| 提供元 | 共有ビュー | 排他ビュー |
| --- | --- | --- |
| `[N of T]`、`Array<T>` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |
| `Slice<T>{source}` | `slice(self: Self) -> Slice<T> during source` | なし |
| `UniqSlice<T>{source}` | `slice(self: ref/Self) -> Slice<T> during self` | `sliceUniq(self: uniq/Self) -> UniqSlice<T> during self` |

`x[r]`は`x.slice(r)`を意味する。範囲添字は常に共有Sliceを形成する。

`UniqSlice<T>{source}`は、初期化済みの連続領域へのNon-Copyな排他借用である。要素を所有せず、`T`はCopyでもOwnedでもなくてよい。メタデータ、`index`と`indexUniq`（`UniqIndexable<isize>`）、ビューのメソッドを持つ。要素は読出し、置換、借用ができるが、Moveして取り出せない。排他操作には`var`の所有者または排他参照が必要となる。暗黙のCopy、Move、再借用、変換はない。`view@move`、`view.sliceUniq(...)`、`view.slice(...)`と書く。UniqSliceに反復と`toArray`はない。`T`は不変である。子ビューは親の現在の借用に依存し、共有の子が生存する間は書込みを禁止する。範囲が異なるだけでは非重複性を証明しない。

```kimi
var values: [3 of i32] = [1, 2, 3]
values.sliceUniq(0..3)[0] = 10
var view = values.sliceUniq()
let reference = view@uniq
reference[0] = 20     // The referent stays exclusively accessible.
values[0..3][0] = 40  // Error: range subscripting forms a shared view.
```

<a id="s4-8-5"></a>
#### 4.8.5. 重ならない排他要素のペア

`Array<E>`と`[N of E]`は、`P is Position`、`Q is Position`を満たす`tryGetPairUniq<P, Q>(self: uniq/Self ! first: P, second: Q) -> Option<(uniq/E during self, uniq/E during self)>`を提供する。どちらかが要素位置でない場合、または同じ論理要素を選ぶ場合は`None`を返す。それ以外は、引数順に2要素への排他参照を返し、両者は重ならない。O(1)で動作し、変更もメモリー割当ても行わず、ユーザーコードも呼び出さない。個別に形成した排他要素借用には、引き続き静的な非重複性が必要となる（[§15.6.2](#s15-6-2)）。

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

<a id="s4-8-6"></a>
#### 4.8.6. コスト

位置と範囲の構築・解決・Copy、およびビューと要素Placeの形成・Copy・再スライス・分割は、メモリー割当てや参照カウントの更新なしにO(1)時間で行う。SliceとUniqSliceの要素間隔は`stride(T)`である。

配列、スライス、範囲、Dictionaryの標準イテレーターについて、列挙開始時に生存する要素またはエントリーの数を`n`とする。空き容量は数えない。生成、破棄、各`next`はO(1)時間（Dictionaryでは償却O(1)）であり、全列挙はO(1 + n)時間となる。管理用の記憶領域は`n`に対してO(1)である。所有する要素の記憶領域は別に数える。インライン所有の`[N of E]`は`N * stride(E)`バイトを占め、動的コレクションの所有イテレーターは既存の要素領域を保持する。イテレーター操作は新たなヒープのメモリー割当てを行わない。要素の移動と破棄のコストは、インライン配列領域の移動も含めて別に数える。

<a id="s4-9"></a>
### 4.9. 動的コレクション

<a id="s4-9-1"></a>
#### 4.9.1. 共通規則

`Array<T>`と`Dictionary<K, V>`は、所有するNon-Copyのコレクションである。Dictionaryは任意の完全な格納型を受け入れ、`K is Equatable`と[§12.4.3](#s12-4-3)のキー安定性を要求する。公開のHash制約はない。

変更操作は`self: uniq/Self`を取る。そのため、レシーバ位置のコレクションは`@uniq`なしで排他的に取得する（[§7.3](#s7-3)）。ただし、貸出し点が排他的に書込み可能でなければならない（[§15.1.5](#s15-1-5)）。`uniq`パラメーターには引き続き`@uniq`が必要である（[§10.2](#s10-2)）。削除は格納された責任を移転する。事前条件の違反はAbort、不在は`Option`、回復可能な拒否は入力を伴う`Result`で返す。

追加は既存の順序を保ち、必要なら容量を増やす。置換は値だけを変える。削除は残る要素の順序と容量を保つ。clearは全要素を破棄し、容量を保つ。検索と拒否は何も変えない。予約と縮小は値、順序、長さを保つ。引数、等価比較、デストラクターによる効果は巻き戻さない。

```kimi
var values: Array<i32> = []
values.append(10)       // Exclusive receiver, no @uniq.
grow(values@uniq)       // A uniq parameter needs @uniq.
```

<a id="s4-9-2"></a>
#### 4.9.2. 命名規約

同じ名前の関数群は、レシーバの形（[§7.3](#s7-3)）とパラメーターモード（[§7.4](#s7-4)）を共有し、アクセサーのレシーバは固定される（[§11.3](#s11-3)）。そのため、共有版と排他版、および同じ状態を扱うプロパティと消費操作には異なる名前が必要となる。Kimiの宣言と本仕様の例は、次の対に従う。

| 対 | 規約 | 例 |
| --- | --- | --- |
| 新しい値／その場での変更 | 過去分詞／動詞 | `sorted` / `sort` |
| 共有参照／排他参照 | 排他側に`Uniq`接尾辞 | `tryGet` / `tryGetUniq` |
| 調べる／進める・取り出す | 異なる動詞。プロパティに対しては動詞＋名詞 | `peek` / `next`、`reading` / `nextReading` |
| 観察する／消費して取り出す | 名詞のプロパティ／`into`＋名詞 | `item` / `intoItem` |

<a id="s4-9-3"></a>
#### 4.9.3. Arrayの操作

位置は値で渡し、操作開始時の長さに対して1回解決する（[§4.6.3](#s4-6-3)）。

| メンバー | 動作 |
| --- | --- |
| `init(! capacity: isize)` | 空を構築する。負ならAbort |
| `init(! repeating: T, count: isize)`、`T is Copy` | `count`個をCopyする。`count`がゼロでも`repeating`を1回取得する。負の個数は要素を配置する前にAbortする。メモリー割当ては最大1回で、`count`がゼロなら行わない |
| `append(value: T)` / `pop() -> Option<T>` | 末尾に追加／末尾から削除 |
| `insert<P>(index: P, value: T)` | 境界に挿入する。`insert(^0, v)`は末尾に追加する |
| `remove<P>(index: P) -> T` / `swapRemove<P>(index: P) -> T` | 要素を削除する。順序を保つ／最後の要素をその位置に移す |
| `swap<P, Q>(first: P, second: Q)` | 2要素を交換する。同じ要素なら何も変えない。不正な位置ならAbort |
| `truncate(length: isize)` / `clear()` | `length`以降／すべての要素を、[§4.9.7](#s4-9-7)の順で破棄する。現在より大きい長さなら変更せず、負ならAbort |
| `appendAll(other: Array<T>)` | `other`の全要素を末尾へMoveする |
| `appendCopies(values: Slice<T>)`、`T is Copy` | 全要素を末尾へCopyする |
| `removeAll<F>(matching: F)`、`F is Callable<(ref/T) -> bool>` | 添字順に各要素で`matching`を1回呼ぶ。一致する要素を削除し、ほかの要素の順序を保つ。削除要素は[§4.9.7](#s4-9-7)の順で破棄する |
| `reverse()` | 順序を反転する |
| `sort()`、`T is Comparable` / `sort<F>(by: F)`、`F is Callable<(ref/T, ref/T) -> i32>` | `compare` / `by`による非減少順にする。安定ではない。`by`は小・等・大に対して負・ゼロ・正を返す |
| `first`、`last: Option<ref/T during self>`（共有レシーバ） | 空なら`None` |
| `tryGetUniq<P>(index: P) -> Option<uniq/T during self>` | 要素位置でなければ`None` |

`P`と`Q`はPosition型である。Array内の要素移動はCopyもデストラクターの実行も行わない。メモリー割当ての保証は[§4.9.5](#s4-9-5)で定める。`removeAll`と`sort`の実行中、コールバックはArrayを観察できない。コールバック内でAbortしても、各要素はちょうど1回初期化された状態に保たれる。一貫しない比較では、得られる並び順は未規定となる。

```kimi
var values = Array<i32>.init(capacity: 4)
values.appendCopies([30, 10, 20][..])
values.sort()                                  // [10, 20, 30]
let removed = values.swapRemove(0)             // 10; values is [30, 20]
values.removeAll(matching: func (v) => v > 25) // [20]
values.append(values[0])                       // The Copy precedes receiver activation.
let bad = values.remove(^0)                    // Aborts: ^0 is not an element position.
```

<a id="s4-9-4"></a>
#### 4.9.4. Dictionaryの操作

検索は[§12.4.3](#s12-4-3)の等価性の対応付けを使い、挿入順序を変えない。

| 操作 | キーが不在 | 等しいキーを格納済み |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K, V)>` | 末尾に追加し、`Ok(())` | `Err((key, value))` |
| `insertOrReplace(key: K, value: V) -> Option<V>` | 末尾に追加し、`None` | 格納済みキーを保ち、`Some(旧値)` |
| `remove(key: ref/K) -> Option<(K, V)>` | `None` | 格納済みエントリー |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | `None` | 値への共有参照 |

検索前に両引数を取得する。`insertOrReplace`は格納後に未使用の入力キーを破棄する。格納済みキーを変更する操作はない。`tryGet`は値をCopyせず、その結果はDictionary全体にLoanを持つが、キーには持たない。再追加したキーは新しい位置として末尾に追加する。`clear`は全エントリーを破棄する（[§4.9.7](#s4-9-7)）。

`map[key]`は既存キーの値Placeを選ぶ（[§4.7.1](#s4-7-1)）。不在ならAbortする。`map[key] = value`は、右辺を取得し、レシーバとキーを評価し、共有アクセスで検索してから、旧値を破棄して新しい値を置く。この間、Dictionaryの構造変更、移動、破棄はできない。書込み前にキーの借用も含むすべてのLoanを検査し、旧値の破棄が右辺の依存先を無効にする場合は置換を拒否する。複合代入は検索を1回行う。

```kimi
var names: Dictionary<i32, string> = [:]
match names.tryInsert(1, "first")
    .Ok(()) => ()
    .Err(let entry) => Kimi.Console.writeLine(entry.1)
names[1] = "replacement"      // Replacement, not insertion.
names[2] = "two"              // Aborts if executed: key 2 is absent.
```

<a id="s4-9-5"></a>
#### 4.9.5. 容量とメモリー割当て

両コレクションは、メモリー割当てなしで格納できる要素数またはエントリー数として、読取り専用の`capacity: isize`を公開する。`0 <= length <= capacity`が成立する。型付きの空の`[]`と`[:]`はメモリー割当てを行わない。`init(capacity: c)`は`[]`に続けて`reserve(additional: c)`を行うのと同じである。`reserve(additional:)`は位置引数も受け入れる。負またはオーバーフローする要求ではAbortし、`capacity >= length + additional`を保証する。縮小は行わず、容量が十分ならメモリー割当ても要素移動も行わない。容量内の追加、検索、置換、削除、`clear`、拒否はメモリー割当てを行わない。拡張時にはメモリー割当て失敗でAbortすることがある（[§22.5.2](#s22-5-2)）。正確な容量は実装定義とする。

`shrinkToFit()`は、容量を減らすためにメモリー割当てを行ってもよい。結果の容量は、長さ以上、元の容量以下となる。値、順序、長さを保つ。サイズ計算やメモリー割当てが失敗した場合は正常に戻り、容量と要素の配置も保つ。長さぴったりの容量や、OSへの解放は保証しない。

<a id="s4-9-6"></a>
#### 4.9.6. Loan、依存、呼出し効果

排他レシーバは後続引数より前に予約し、入口で有効化する（[§15.6.7](#s15-6-7)）。有効化時点で生存中の要素のLoanやSliceのLoanは競合する。追加・置換する入力の依存はコレクションの集合に加わり、`tryInsert`が`Err`を返す場合も同じである。削除された値は格納型の完全な依存を伴う。依存を差し引く操作はない。`tryGet`の結果は記憶領域のLoanを加える。削除された`uniq/T`は、Loanの生存性が許すまでは、残る`Array<uniq/T>`から独立しない。

| 操作 | 操作内のユーザーコードによる効果 |
| --- | --- |
| Arrayの`clear`、`truncate`、破棄、Arrayと固定長配列の所有イテレーターの破棄、添字による置換 | 該当する`T`の破棄 |
| Arrayの`removeAll` | `matching`の呼出し、削除する`T`の破棄 |
| Arrayの`sort()` | `T`の`compare` |
| Arrayの`sort(by:)` | `by`の呼出し |
| Sliceの`contains`、`firstIndex(of:)` | `T`の等価比較 |
| Sliceの`firstIndexWhere` | `predicate`の呼出し |
| Dictionaryの`tryGet` / `tryInsert` / `remove`と添字アクセス（`index`、`indexUniq`） | `K`の等価比較 |
| Dictionaryの`insertOrReplace` | `K`の等価比較、未使用の入力`K`の破棄 |
| Dictionaryの添字による置換 | `K`の等価比較、旧`V`の破棄 |
| Dictionaryの`clear`、破棄、所有イテレーターの破棄 | `V`と`K`の破棄 |
| [§4.6](#s4-6)〜[§4.9](#s4-9)のその他すべての操作：構築、`append`、`insert`、`pop`、`remove`、`swap`、`swapRemove`、`reverse`、`appendAll`、`appendCopies`、Array・固定長配列・Sliceへのアクセス、ビュー、位置、メタデータ、容量の操作 | なし |

表の各行と、前述の依存規則、メモリー割当て、Abort、操作の入力を通したアクセスを合わせたものが、その操作の完全な公開効果となる。呼出し側はライブラリー本体ではなくこれを使い、ライブラリー実装はこの範囲内に収まらなければならない。Callable引数の呼出しは、実引数を通して呼出し側で合成する（[§8.11.6](#s8-11-6)）。`Array<i32>`などの具象インスタンス化は、表の行を直接使う。パラメーターに依存する破棄は、[§15.6.4](#s15-6-4)が広げる場合を除き、Loan検査のために呼出し側で合成する。効果境界の検証（[§8.11.4](#s8-11-4)）は定義自身の束縛を代入し、それでも抽象型に依存する破棄はそこで分類不能となる。ジェネリック本体は、そのような破棄を[§16.3.3](#s16-3-3)に従って自身のLoanに対して検査する。

```kimi
var values: Array<i32> = [3, 1, 2]
let view = values[..]
values.sort()          // Error: view's Loan is live at activation.
let first = view[0]
```

<a id="s4-9-7"></a>
#### 4.9.7. 失敗と破棄順序

操作は結果を決める前に、境界を解決または検索する。拒否と不在は何も変えず、サイズとメモリー割当てを検査するのは追加だけである。再配置はユーザーコードを呼び出さない。引数評価中の制御転送は呼出しを飛ばす（[§16.2.1](#s16-2-1)）。

Arrayの`truncate`、`clear`、破棄は、添字の降順に要素を破棄する。`removeAll`は最後の`matching`呼出しの後、未規定の順で削除要素を破棄する。Dictionaryの`clear`と破棄は挿入の逆順に行い、各値をそのキーより先に破棄する。所有イテレーターも、残る要素を同じ順で破棄する。

```kimi
var log: Array<Logged> = [Logged.init(1), Logged.init(2), Logged.init(3)]
log.truncate(1) // Destroys index 2, then index 1 (Logged 3, then Logged 2).
```

<a id="s4-9-8"></a>
#### 4.9.8. コスト

ユーザーコードを除き、長さを`n`、容量を`c`、後始末が必要な型を持つ破棄要素数を`d`とする。

| 操作 | 上限 |
| --- | --- |
| Arrayの`append` / `pop` | 償却O(1) / O(1) |
| Arrayの`swap`、`swapRemove`、`first`、`last`、`tryGet`、`tryGetUniq` | O(1) |
| Arrayの`truncate`、`clear` | O(1 + d) |
| Arrayの`appendAll`、`appendCopies`、`init(repeating:count:)` | 1要素あたり償却O(1) |
| Arrayの`insert`、`remove`、`removeAll`、`reverse`、Sliceの`contains` / `firstIndex` / `firstIndexWhere` | O(1 + n) |
| Arrayの`sort` | 最悪O(n log n)、メモリー割当てなし |
| Dictionaryの検索／変更 | 検索コスト＋O(1)／償却O(1) |
| Dictionaryの`clear` | O(1 + c + d) |

`shrinkToFit`を使わない`m`回の操作では、拡張の総コストはO(m + C)となる。`C`は容量、長さ、`reserve`要求の最大値である。したがって`reserve(additional: 1)`と`append`を繰り返すコストはO(n)となる。Dictionaryの検索コストには、等価比較、ハッシュ計算、探索を含む。線形探索も許される。

---

<a id="s5"></a>
## 5. 生ポインターとunsafeなメモリー操作

[仕様の索引](#contents)

unsafe文脈は検証の責任を委ねるものであり、データ競合の規則（[§22.3.7](#s22-3-7)）を含め、どの規則も免除しない。Unsafeブロック（[§14.3](#s14-3)）、unsafe関数（[§7.6](#s7-6)）、外部関数（[§22.6](#s22-6)）は、コンパイラーが検証できない約束を示す。その違反は未定義動作となる（[§1.3](#s1-3)）。安全なコードだけで未定義動作が起こることはない。

`raw/T`は、完全な型`T`の記憶領域への非所有の**生ポインター**である。`T`自体がポインターや参照でもよい（`raw/raw/i32`）。すべての`T`についてCopyであり、Copyや破棄は参照先に触れない。raw層は`raw/T during o`のようにOriginを指定し、どの記憶領域を指すかを表せる（[§5.7](#s5-7)）。

unsafe文脈が必要なのは、生Placeの形成（`*p`、`p[n]`、[§5.2](#s5-2)）、ポインター算術（[§5.3](#s5-3)）、unsafe関数の呼出し、`noinit`（[§4.3.4](#s4-3-4)）、raw Originの表明（[§5.7](#s5-7)）だけである。それ以外の文脈ではコンパイル時エラーとなる。`null`、`==`、`@raw`、生ポインター・`usize`・`null`からの`@raw/U`、生ポインターからの`@usize`を含むその他の操作には、unsafe文脈も妥当な由来も必要ない。ただし整数からポインターへの変換は環境効果である（[§8.11.2](#s8-11-2)）。

```kimi
let bytes = pointer@raw/u8       // No unsafe context.
unsafe
    let value = bytes[13]        // Forming a raw Place needs one.
let first = bytes[0]             // Error: raw Place outside an unsafe context.
```

<a id="s5-1"></a>
### 5.1. nullと等価性

`null`リテラルには期待される生ポインター型が必要であり、そうでなければ`@raw/U`のオペランドでなければならない（型付きNull形成、[§5.4](#s5-4)）。安全な参照（`ref`、`uniq`、`objref`、`objuniq`）はnullにならない。nullでない生ポインターでも妥当とは限らない。

`==`と`!=`は、同じ型の2つのポインター、またはポインターと`null`のアドレスを比較する。参照先は読まず、nullやダングリングポインターにも使える。null同士は等しく、nullでないポインターとは等しくない。アドレスが等しくても、由来が等しいことやアクセス権限は意味しない。ポインターに順序比較はない。

```kimi
let pointer: raw/i32 = null
let empty = pointer == null
let typed = null@raw/i32              // Typed Null Formation, not a cast.
let unknown = null                    // Error: no pointer Type.
let before = pointer < pointer        // Error: pointers have no ordering.
```

<a id="s5-2"></a>
### 5.2. 生Place

**由来（Provenance）**とは、ポインターの元となる記憶領域と、そのポインターのアクセスが対象にできる範囲である（[§5.2.1](#s5-2-1)の条件1）。Placeが範囲内にあるとは、そのバイトが範囲内にあることをいう。サイズゼロなら、アドレスが範囲内またはその末尾にあることをいう。ポインターの由来の範囲内にあり、その由来の記憶領域に属するPlaceは、そのポインターの由来内にある。型を読み替えたPlaceは、そのバイトを保持する記憶領域に属する。

`*p`は型`T`の**生Place**であり、`p[n]`、生Placeのフィールドや要素も同様である。nullポインターは参照外しできない。操作の対象となる生Placeは、ポインターの由来内で、生Placeのアドレスにある1つのPlaceを指す。その型は、Originを等しいものとして扱う正規化後の型同一性（[§3.8](#s3-8)）の下で、生Placeの型と同じである。フィールドや要素の射影の基底はPlaceを指さない。同じ型の異なるサイズゼロのPlaceがアドレスを共有することもある（[§4.1](#s4-1)）。生Placeはいずれを指してもよく、アドレス、添字、フィールド名ではその1つを選べない。

読出しと書込みは通常のCopyと代入の規則に従う。生Placeの値全体の読出し、置換、Take、借用には、完全な値が必要となる（[§15.1.2](#s15-1-2)）。生ポインターはLoan、排他性、完全性の証明を提供しない。

**配置。** 生ポインターを通したPlaceの初期化は、**操作契約**が規定する構築または再配置（後述）でのみ行う。たとえば`Raw.initialize`（[§5.6](#s5-6)）の契約は構築を規定する。操作契約とは、外部インポートの`- safety:`条件（[§22.6](#s22-6)）、または実装が本体なしで提供するKimi関数の契約（[§22.1](#s22-1)）である。本体を持つ関数は、実行する操作を通してのみPlaceを初期化する。Placeが**値を保持しない**とは、UninitializedまたはMovedであることをいう（[§15.1.1](#s15-1-1)）。Placeの初期化は、その初期化に使うポインターについて[§5.2.1](#s5-2-1)の条件を満たす、Placeへの書込みである。そのPlaceは既存のPlace、または生の記憶領域内の新しいPlaceでなければならない。値を保持していてはならず、祖先以外のInitializedなPlaceがそのバイトを保持していてもならない。`noinit`配列（[§4.3.4](#s4-3-4)）は既にInitializedである。

**指示先。** 操作の集合が**閉じている**とは、その要素のいずれかより前に起こるすべての操作を含むことをいう（[§22.3.7](#s22-3-7)）。閉じた集合が**整合している**とは、各生Placeが指すPlace、各配置が埋めるPlace、各再配置が移す値について、集合全体に対する1つの選択が存在し、その下で安全・unsafeを問わずすべての操作が規則を満たし、選んだPlace上でデータ競合を起こさないことをいう。操作Xを含み、Xより後に起こる操作を含まない閉じた集合が不整合であり、Xを除いた集合は整合している場合、Xから未定義動作となる。この選択は実行の一部ではない。選択が何を割り当てても、操作はその型に従ってそのアドレスで働く。選択が決めるのは、動作が定義されるかどうかだけである。

**型を読み替えるアクセス。** 型がCopyかつ正のサイズを持つ生Placeは、読出し、書込み、借用の対象になるとき、または`Raw.slice`が要素として取るとき、代わりに**型を読み替えたPlace**を指してもよい。これは、ポインターの由来内の、生Placeと同じアドレスにある同じ型のPlaceであり、InitializedなPlaceが保持するバイトに重なる。ただし、値を保持しない部分のバイトを含んではならない。型を読み替えたPlaceは、それに働くアクセス、またはそれを借用するLoanの間だけ存在する。配置、再配置、解放はそのバイトと値を無視するが、Loanは[§5.2.1](#s5-2-1)の条件4で数える。このPlaceは完全な値を保持する。借用経由や`Raw.slice`の要素としての読出しも含め、その読出しには、バイトがその型の妥当な値を表すことが必要となる。パディング（[§21.1.1](#s21-1-1)）や、enumのアクティブケースが使わないバイトは値を表さない。型を読み替えたPlaceを通した書込みは、その借用経由も含め、変更したバイトを保持する各InitializedなPlaceについて、型の読み替え以外で次にアクセスするまでに、そのPlaceの型の妥当な値にしておかなければならない。そのPlaceがNon-Copy型のenumなら、アクティブケースも変えてはならない。型の読み替えはペイロードを構築しない。

**再配置。** 操作契約が、元の範囲のバイトを生の記憶領域へCopyすると規定する場合、その操作は完了時に、元の範囲と元ポインターの由来内にPlaceがある値を再配置してもよい。構成要素は、それを含む値と一緒にのみ移動する。再配置される値は構成要素とともに移動し、値のPlaceが元の範囲をはみ出すなどの理由で値が再配置されない場合、その構成要素もとどまる。再配置された各値は、構成要素とそれらの状態を伴い、移転先の同じオフセットのPlaceを占める。元のPlaceは値を保持しなくなる。各移転先Placeは、再配置元のPlaceを値なしと数えて配置規則を満たさなければならない。再配置元の各Placeには、Takeと同じく[§5.2.1](#s5-2-1)の条件3と4を適用する。

<a id="s5-2-1"></a>
#### 5.2.1. アクセス条件

生Placeの読出し、書込み、Take、借用は、次のすべてが成立しなければ未定義動作となる。

1. **由来。** アクセスの対象がポインターの由来内にあること（[§5.2](#s5-2)）。`P@raw`なら`P`の記憶領域（`x[0]@raw`は、同じアドレスでも`x[1]`に到達できない）、`Raw.allocate`なら割当て全体、それ以外は[§5.5](#s5-5)またはポインターを生成した操作の操作契約（[§5.2](#s5-2)）が定める。
2. **生存性と値。** 記憶領域が生存し、`T`に対してアラインされていること。サイズゼロの`T`でも同じである。読出しには妥当な初期化済みの値が必要となる。内容の置換、移動、取出しが終わらせるのは内容だけであり、記憶領域は生存し続ける。ただし旧内容が所有していた記憶領域（たとえばヒープバッファー）は、旧内容とともに終わる。
3. **権限。** アドレスを取得した経路がそのPlaceに持つ能力を、アクセスが超えないこと（[§13.5.5](#s13-5-5)のオブジェクトペイロード表、[§11.8](#s11-8)）。型を読み替えたPlace（[§5.2](#s5-2)）では、代わりにそのバイトを保持する各Placeの能力を超えないこと。読出しには読出し能力が、書込み、Take、その場での破棄（`_ = (*p)@move`）には書込み能力が必要となる。たとえば`let`束縛、`ref`の参照先、不変の静的値、`let h: obj/T`・`rc`・`arc`・`objref`経由のペイロードは読取り専用である。書込み可能な所有経路（書込み可能な所有`obj/T`のペイロードを含む）と可変の静的値は読書き可能である。`uniq`または`objuniq`の参照先は、そのLoanが有効な間だけ読書き可能である。`Raw.allocate`の記憶領域は読書き可能である。整数変換からのポインターは[§5.5](#s5-5)の保証が与える権限を、外部インポートなどの操作契約を持つ操作が生成するポインターは、その契約が定める権限を持つ。すべてのポインターに次の3つの制限を適用する。
   - 共有層（[§15.6.2](#s15-6-2)）の先のPlace、またはOriginが共有Loanだけを保つ生ポインターの先のPlaceから読み出した生ポインターには、書込み権限がない。
   - 型にOriginを指定したポインターの権限は、そのOriginが保つ権限を超えない（[§5.7](#s5-7)）。
   - ownステップを通して形成したポインター、つまりオブジェクトペイロード内、またはrawフィールドが`during self`と宣言する記憶領域内へのポインター（[§5.7](#s5-7)）の権限は、到達元の保持元に所有値がとどまる間だけ有効となる。その値をMoveすると権限は終わる。`during self`フィールドから再度読み出したポインターは、値の現在の保持元に到達する。
4. **Loan。** アクセス対象と重なるPlaceの生存中のLoanと競合しないこと。ただし、ポインター取得元の経路のLoanは除く。Loanの生存性は値の使用に従い、生ポインターも到達する保持元を通じて含める（[§15.6](#s15-6)）。
5. **到達先。** Loanを伴う値を読み書きするアクセスは、ポインターが到達する保持元、または本体が所有しない記憶領域で行うこと（[§15.6](#s15-6)）。本体が保持元まで追跡できないポインター、たとえばOriginを指定しない型の呼出し結果のフィールドに隠れたものや、整数から復元したものを、本体自身の保持元でそのような値を読み書きするために使ってはならない。

**ローカル変数。** ローカル変数の記憶領域は、宣言からスコープ終了まで固定された1つのアドレスで生存する。値またはその一部の移動（`@move`、部分Move）、生Placeを通した取出しが終わらせるのは、その内容の寿命だけである。記憶領域は生存し、内容のPlaceは値を保持しなくなり、その後の初期化（`var`への代入、または生ポインター経由の`Raw.initialize`）は同じPlaceを埋める。したがって、変数やその部分への生ポインターは、条件2〜5の下でスコープ終了まで使える。

```kimi
var a = make()
let p = a@raw
consume(a@move)
a = make()
unsafe => inspect((*p)@ref)   // Valid: the same Place, initialized again.
```

**オブジェクトペイロード。** ペイロードのアドレスは、ハンドルに何が起きても、公開から破棄の開始まで固定される。

**保持される依存。** 生の記憶領域へMoveした値はOriginとLoan要件を保ち、Loanは記憶領域に伴う（[§15.6](#s15-6)）。rawフィールドが`during self`と宣言する記憶領域は、そのフィールドを持つ値の一部となり、型は参照先を格納内容として数える（[§15.3.5](#s15-3-5)）。ポインターが`during o`として指す記憶領域は`o`のLoanが覆う。それ以外の記憶領域は、ポインターの到達先となる。入力から到達する生の記憶領域を通して関数が格納できるのは、呼出しがそこに保持することを公開契約で許された値だけである（[§15.6.4](#s15-6-4)）。

<a id="s5-2-2"></a>
#### 5.2.2. 生Placeの借用

`@ref`、`@uniq`、`@objref`、`@objuniq`は生Placeを借用できる。結果の参照先は、新しいLoanの新しいアンカー（[§3.4.2](#s3-4-2)）となる。アンカーは所有者やLoanを回復しない。Originは期待型または格納先に合わせ、`static`でもよい。ただし、Originを指定したポインター経由では、そのOriginの範囲内となる（[§5.7](#s5-7)）。結果はポインターの到達先も伴う（[§15.6](#s15-6)）。`uniq`のOriginを`static`に束縛できないという[§15.2.3](#s15-2-3)の安全なコードの規則は適用しない。このアンカーから導かれていないPlaceとは、アンカーを比較しない（[§15.6.2](#s15-6-2)）。結果、またはそのOriginを保持する値を使用している間、借用のアクセスは[§5.2.1](#s5-2-1)の条件を満たさなければならない。`uniq`と`objuniq`の排他性は、借用したPlaceと重なるPlaceに、別の生ポインターも含む他の経路がアクセスしないという、この約束だけに基づく。

<a id="s5-2-3"></a>
#### 5.2.3. 生Placeからの取出し

生Placeは状態追跡なしでTakeを提供する（[§15.1.5](#s15-1-5)）。`(*p)@move`は値を取り出し、指示先のPlaceを値なしにする。記憶領域は生存し、`Raw.initialize`で再び値を置ける。元の所有者によるものも含め、その後の読出しや二重破棄を防ぐ義務は作成者が負う。`_ = (*p)@move`はその場で値を破棄する。修飾なしの取得は[§3.5](#s3-5)に従う。

<a id="s5-3"></a>
### 5.3. ポインター算術と添字アクセス

`p: raw/T`と`n: isize`に対し、`p + n`と`p - n`は`p`を`n * stride(T)`バイトずらす。`p[n]`は`*(p + n)`である。`p += n`と`p -= n`は複合代入である（[§13.7.2](#s13-7-2)）。ほかのポインター算術は存在しない。

`stride(T)`は、パディングを含む要素間隔である（[§4.1](#s4-1)、[§21.1](#s21-1)）。`raw/()`のようにゼロの場合、すべての変位がゼロとなるため、ジェネリックな`raw/T`にサイズ制約は不要である。このような要素の列では、ポインターの等価比較で末尾を判定できず、個数を使って走査する。

ゼロの変位は、nullも含めポインターを保つ。ゼロでない変位には生存する記憶領域が必要となり、元と結果の両方がポインターの由来（[§5.2](#s5-2)）の範囲内、または末尾の直後に位置しなければならない。結果は由来と、Originを含むポインターの型を保つ。`isize`のオーバーフローやアドレスのラップは未定義動作となる。添字アクセスに境界検査はない。

```kimi
unsafe
    pointer[-1] = 123
    let next = 1 + pointer       // Error: only p + n and p - n exist.
```

<a id="s5-4"></a>
### 5.4. アドレスと変換

| 形式 | 意味 |
| --- | --- |
| `P@raw` | Place`P`のアドレス。`P`の格納型`V`に対して型は`raw/V`となる。`P`への到達に使った最後の借用または生ポインターにOriginがあれば、それを指定する。借用なら外側Origin、生ポインターならその指定Originである（[§5.7](#s5-7)） |
| `E@raw/U` | 生ポインター、`usize`、`null`から`raw/U`への変換 |
| `E@(raw/U during o)` | 同じ変換にOrigin`o`を指定したもの。`E`のraw Origin、または`null`が`o`に合うなら型合わせ（[§3.8](#s3-8)）、そうでなければ表明（[§5.7](#s5-7)）であり、unsafe文脈を必要とする |
| `E@usize` | 生ポインター`E`の数値アドレス（[§5.5](#s5-5)） |

単独の`@raw`は型を取らず、記述したスロットに適用する。`p: raw/T`に対して`p@raw`は`raw/raw/T`となり、参照先のアドレスは`r@follow@raw`となる。直ちに終わる`@ref`と同じように検査し、何も読まず、Loanを残さない。一時値では、その一時値が生存する間だけポインターが有効となる。効果の要約では`P`へのアクセスとなる（[§8.11.2](#s8-11-2)）。

`U`とオペランドの参照先型が、単にレイアウトが等しいだけでなく、正規化後（別名の展開後）に同じ場合、`@raw/U`は通常のCopyであり、Originを保つ。整数リテラルのオペランドは`usize`に合わせる。生ポインター型間の変換は、アドレス、由来、オペランドのraw Originを保つ。ただし変換先で別のOriginを指定した場合を除く。どの変換も、参照先の読出し、由来の作成、アクセス権限の付与は行わない。`U`としてのアクセスには、引き続き[§5.2](#s5-2)と[§5.2.1](#s5-2-1)が必要となる。

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

<a id="s5-5"></a>
### 5.5. 整数アドレス

ポインターと整数の変換を行うには、ターゲットが単一のアドレス空間を持ち、ポインターがアドレス以外の状態を持たず、アドレスが`usize`に収まり、ポインター、`usize`、`isize`の幅が一致しなければならない。それ以外ではUnsupportedとなる。nullとゼロは相互に変換される。

`usize`は由来を持たない。アドレスをポインターに戻すと等しいアドレスは得られるが、由来、寿命、権限、Originは得られない。そのポインターが到達するのは、本体が所有しない記憶領域だけである（[§15.6](#s15-6)）。それを通してアクセスするには、ポインターの由来（[§5.2](#s5-2)）、権限、その由来内に置かれた値を定める、実装が文書化した保証が必要となる。[§5.2.1](#s5-2-1)の条件2、4、5は引き続き適用する。

```kimi
let restored = pointer@usize@raw/i32   // Equal address only; no provenance.
```

<a id="s5-6"></a>
### 5.6. 生の記憶領域の操作

```kimi
public group Raw   // Kimi.Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

- **`allocate`**（安全）は、`count`個の要素のための生の記憶領域を割り当てる。この領域は値を保持しない。負の`count`、`count * stride(T)`のオーバーフロー、失敗はAbortする（[§17.4.3](#s17-4-3)）。`allocate`が対応する最大アラインメントは実装定義であり、それを超える`T`のアラインメントはコンパイル時にUnsupportedとなる。ゼロバイトの要求は、独自の長さゼロの由来を持つ、非nullでアラインされたアドレスを返す。ゼロバイトの結果同士は等しくてもよい。
- **`release`**は、null、ゼロバイトの結果、または未解放の`allocate`結果の先頭を受け取る。別の生ポインター型へ変換されていてもよい。nullは無視し、ゼロバイトの結果では何も解放しない。ゼロバイトの結果は再度releaseしてもよい。その記憶領域に置かれた各Non-Copy値は、既に破棄、取出し、再配置されていなければならない。これは値全体に行うか、値全体を破棄しても構成要素の破棄だけになる場合（[§16.3.2](#s16-3-2)）は、残るNon-Copy構成要素に同じ規則で行う。その後、その記憶領域内へのすべてのポインターは無効となる。
- **`initialize`**は、[§5.2](#s5-2)の配置規則に従い、`storage`にある型`T`のPlaceへ`value`をMoveする。
- **`slice`**は、`storage`から始まる`length`個の要素の共有`Slice`を形成する。`length >= 0`は呼出し側の義務である。`length == 0`なら、`storage`はnullを含む任意の値でよく、アクセスしない。`length > 0`なら、`storage`は非nullで`T`に対してアラインされ、`length * stride(T)`が`isize`に収まらなければならない。さらに`length`未満の各`i`に対し、`storage[i]`は`storage`の由来内にある別々のPlaceを指し、完全な`T`の値を保持する（たとえば`Raw.initialize`で配置済み）か、`noinit`配列に属さなければならない（[§4.3.4](#s4-3-4)）。結果の要素`i`はそのPlaceとなる。形成時に要素は読まない。未書込みの`noinit`要素は、読む前に書き込む必要がある。結果Origin`s`は結果にだけ現れ（[§15.3.4](#s15-3-4)）、新しいアンカーを持つ（[§5.2.2](#s5-2-2)）。`storage`引数が`raw/T`に合わせる前にOriginを指定していれば、その範囲内となる（[§5.7](#s5-7)）。このOrigin全体にわたって要素へ書き込んではならない。

効果の要約では、`allocate`はメモリー割当て、その他はrawアクセスとなる（[§8.11.2](#s8-11-2)）。

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

非公開アクセスが及ぶのは宣言コンテナーとその中に入れ子になった本体だけである（[§9.4](#s9-4)）。そのため、2つのstructで共有するメンバーは`internal`とする。生存中の`BufferView`は`source`の共有Loanを保つので、その間の`push`は借用エラーとなる。`raw/T`フィールドによって、`Buffer<T>`は`T`について不変となる（[§15.3.5](#s15-3-5)）。`data`は`during self`なので、その記憶領域はBufferの一部となり、保持元は挿入した値のLoanを伴う。したがって、挿入した値は、`Buffer`と、そこから取り出した値や借用した値が生存する間、Loanを保つ（[§5.2.1](#s5-2-1)）。`BufferView.init`は`source`を通して`buffer.data`を読むので、その値は既に`source`を指定しており、表明は不要である。ビューの共有Loanが与える`data`の権限は読出しだけとなる（[§5.7](#s5-7)）。

<a id="s5-7"></a>
### 5.7. 生ポインターのOrigin

raw層は`during`でOriginを指定できる（[§3.3.2](#s3-3-2)）。Originは、ポインターがどの記憶領域を指すかを表す。Loanの形成、アクセス権限の付与、妥当性の確立は行わず、ポインターの使用はその領域にプログラム位置を加えない（[§15.6.1](#s15-6-1)）。ほかの生ポインターと同じく、Originを持つものもダングリングになり得る（[§5.1](#s5-1)）。そのOriginはLoanを有効なまま保たない。

| 形式 | ポインターが指す記憶領域 |
| --- | --- |
| `raw/T` | 指定のない記憶領域。値の到達先（[§15.6](#s15-6)）であり、呼出しでは[§15.6.4](#s15-6-4)が定めるもの |
| `raw/T during o` | `o`が覆う記憶領域。`o`が属する借用のLoanが保護するもの。`static`なら静的記憶領域 |
| `raw/T during self` | その層を保持する値が所有する記憶領域。オブジェクトペイロードと同様、その値のownステップ（[§15.6.2](#s15-6-2)） |

- **Origin。** `o`は交差以外の1つのOrigin原子であり（[§15.3.1](#s15-3-1)）、借用に属する。スロット（値から射影した`x.source`も含む）は、それを保つ格納済みの安全な参照と`Loan<T>`フィールドに属し、それらの参照先型が許すものを合わせて許す。借用値の外側Origin（入力のもの、または本体内のローカルのもの）はその借用に属し、`static`は静的記憶領域に属する。シグネチャでは、raw層のOriginは入力の借用、スロット、`static`のいずれかに属さなければならない。`self`が保持値を指すのは、structまたはenumのインライン記憶領域（フィールド、タプル・固定長配列の要素、ケースのペイロード）内のraw層だけであり、型引数内では使えない。型自身の本体では、`self`はレシーバのOriginである（[§15.2.1](#s15-2-1)）。コンストラクターでは構築中の値、`drop`では破棄レシーバ（[§16.3.1](#s16-3-1)）であり、どちらも排他的である。
- **フィールドの読出し。** 層が`self`を指定するrawフィールドを、借用または生ポインターの参照外しを含む経路で読むと、最後に参照外しした借用または生ポインターのOriginを指定するポインターを得る。借用なら外側Origin、生ポインターならその指定Originとなり、指定がなければOriginなしとなる。そのような参照外しを通さない場合は、Originなしのポインターを得る。その到達先は、フィールドの記憶領域の`@raw`が到達するものと同じである（[§15.6](#s15-6)）。フィールド型のスロットは、ほかのフィールドと同じく値の束縛で置き換える。
- **型付き経路。** `P@raw`も同様に、`P`に到達する最後の借用または生ポインターのOriginを指定する（[§5.4](#s5-4)）。変位（[§5.3](#s5-3)）と生ポインター型間の変換はOriginを保つ。これらにunsafe文脈は不要である。
- **型合わせと表明。** `during`のないraw層は、すべてのOriginがoutliveするOriginを指定したものと扱い、raw層はOriginについて共変である（[§15.3.5](#s15-3-5)）。`raw/T during o`は`raw/T`の部分型となる（[§3.8](#s3-8)）。したがって、Originを取り除くことや、それがoutliveするOriginへ狭めることは型合わせである。型合わせでない変更は**表明**であり、unsafe文脈の`E@(raw/U during o)`でのみ形成する（[§5.4](#s5-4)）。それ以外ではOrigin関係エラーとなる（[§15.6.1](#s15-6-1)）。`null`は任意のOriginを取る。表明は、そのポインターと、そこから導いたすべてのポインターを通す各アクセスが、`o`の覆う記憶領域内にあることを約束する。違反は未定義動作となる。
- **権限。** ポインターのOriginが共有Loanだけを保つ場合、生Placeの書込み、Take、その場での破棄、`@uniq`または`@objuniq`による借用はエラーとなる。該当するのは、`ref`または`objref`借用の外側Origin、またはLoan要件が`ref`であるスロット（[§15.3.5](#s15-3-5)）であり、許容されるSemanticsのケースごとに判定する。その他は[§5.2.1](#s5-2-1)の条件3で制限する。
- **スロットと所有権。** raw層のOriginは変性について共変位置だが、Loan要件やPhantom判定でスロットの使用とは数えない。raw層が指定するスロットはPhantomであってはならず、そうであれば宣言は不正となる。`during self`の参照先は、スロットの性質とOwnedOriginsについて、その型の格納内容として数える。そこに格納した値は、所有値の保持元にLoanを保つ（[§15.6](#s15-6)）。`during self`層を持つ型はCopyではない。
- **呼出し。** Origin付きraw層を通して、呼出しはそのOriginが属する借用の参照先型が許すものを格納できる（`self`なら保持値の型、`static`なら参照先型）。その記憶領域は、Loanを形成せずに、その借用と同じように保持する。パラメーター内のOriginなしraw層を通して、呼出しはその参照先型が許すものを格納できる。結果型内のOriginなしraw層は、タプル要素、Optionペイロード、型引数として現れる場合も含め、呼出し先が任意の引数を通して到達する記憶領域と、本体が所有しない記憶領域を指してもよい（[§15.6.4](#s15-6-4)）。公称型の結果のフィールドにある生ポインターは、その型のフィールドだけが規定する。そこに隠れたポインターには[§5.2.1](#s5-2-1)の条件5を適用する。

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
    unsafe => *lens.at = 0                      // Error: source keeps only a shared Loan.

func adopt(pointer: raw/i32) -> raw/i32 during static
    // return pointer                           // Error: stating an Origin needs a claim.
    unsafe => return pointer@(raw/i32 during static)   // A claim: pointer addresses static storage.
```

---

<a id="s6"></a>
## 6. 宣言とコンテナー

[仕様の索引](#contents)

宣言は、名前、スコープ内で宣言を区別するシグネチャ（[§9.1](#s9-1)）、型を持つ。フィールドと計算メンバーは[§11.1](#s11-1)で定義する。

<a id="s6-1"></a>
### 6.1. 宣言コンテナー

**宣言コンテナー**は名前付きスコープであり、その種類によって許されるメンバーが決まる。

| 種類 | インスタンス | 内容 |
| --- | --- | --- |
| `group` | なし | フィールド、計算メンバー、関数、入れ子のコンテナー。全メンバーが静的で、独自のパラメーターは持たない |
| `struct` | あり | 入れ子のgroup・struct・enum・Contract、フィールド、計算メンバー、関数、適合、関連型の指定、コンストラクター、選択される最大1つの`drop`。ジェネリックパラメーター、Origin、制約 |
| `enum` | あり | 閉じた直和型（[§6.5](#s6-5)） |
| `contract` | なし | 要件、関連型、制約、親。独自の型パラメーターは持つが、Origin・長さパラメーター、実装、記憶領域は持たない（[§8.4](#s8-4)） |

`extension`宣言は存在せず、名前検索に拡張候補はない。

**静的メンバー。** `static`は宣言修飾子ではない。groupとrootgroupのメンバーは静的であり、レシーバのないstruct関数は型関数である。structのフィールドと計算メンバーはインスタンスメンバーである。Originの`static`とは無関係である。

```kimi
group Limits
    let max: i32 = 100                   // Static: a group member.
    // static func one() -> i32 => 1     // Error: `static` is not a modifier.
```

<a id="s6-1-1"></a>
#### 6.1.1. ソースルートと入れ子のコンテナー

各SourceDocumentは、そのコンテナー宣言（rootgroupのパスを含む）をプロジェクトルートに加える。SourceDocumentの**実行スコープ**には、最上位の`let`/`var`束縛、ルート関数宣言、実行項目が属する。他の文書からは見えない。`public main`を含むルート関数宣言はこの中にあり、非修飾の名前検索（[§9.5](#s9-5)）で互いを見つける。ただし名前付き関数なので、最上位の束縛はキャプチャーできない（[§9.3](#s9-3)）。コンテナー本体はこの外にあり、そこからの検索で文書ローカルの束縛や関数が見つかることはない。`public main`以外のルート関数宣言は文書ローカルであり、アクセス修飾子は付けられない。記述するとエラーとなる。起動は[§22.4](#s22-4)で定義する。

```kimi
func twice(x: i32) -> i32 => x * 2

public func main()
    let n = twice(2)                  // OK: main is inside the execution scope.

group G
    func f() -> i32 => twice(1)       // Error: a Container body does not see document-local functions.

// public func helper() -> i32 => 0  // Error: only `main` may be a public root function.
```

ルート関数は、同じSourceDocumentの実行スコープ内で明示的完全特殊化を認める（[§8.13](#s8-13)）。別の文書からは特殊化できない。

**rootgroup。** `rootgroup A.B`は入れ子のgroupパス`A.B`を宣言する。ソースルート直下（選択されたルートディレクティブ項目を含む）だけに置ける。パスが作るのはgroupだけであり、既存のgroup以外の型を指す区間は種類の衝突となる（[§9.1](#s9-1)）。パスが作るgroupのアクセスは、そのパスに明示的なgroup宣言があればその宣言と同じ、なければ`private`となる。

**入れ子。** ルート、group、structは、group、struct、enum、Contractを含められる。enum、Contract、実行ブロック、条件付き適合ブロックには含められない。深さに言語上の上限はなく、実装上の上限はResource診断となる。所属は、記憶領域、外側インスタンス、レシーバ、継承、適合を加えない。

**空の本体。** group、rootgroup、struct、contractは本体を省略できる。空行やコメントだけの行は本体を形成せず、ディレクティブの選択後に本体が空になってもよい。本体のないcontractはマーカーContractである。enumの本体は、選択後も空であってはならない。実行ブロックには、選択前にソース項目が必要となる（[§14.2.1](#s14-2-1)）。

<a id="s6-1-2"></a>
#### 6.1.2. コンテナーの断片

groupとstructの宣言は、同じファイル内でも複数の断片に分割できる。由来するKotonoha、親、名前、種類、ジェネリックアリティが同じ断片は、1つの宣言になる（種類の衝突：[§9.1](#s9-1)）。Kotonohaライブラリーをまたいで統合することはない。

断片間では、ジェネリックパラメーターの個数、種類、順序、名前、修飾子（`open`を含む）、既定値適用後のアクセスが一致しなければならない。アクセスの衝突はエラーとなる。structの全断片は同じOriginヘッダーを記すか、すべて省略しなければならない。全省略では独自のスロットを宣言しない（[§15.3.2](#s15-3-2)）。既存コンテナーに生成されるメンバーは、参加する型のヘッダーを使う。

共有の制約領域（型制約と`origin`関係）を提供できる断片は、句の内容が同じでも最大1つである。基底句を提供できる断片も最大1つである。各句は、それ自身のSourceDocument内で解決する。基底句の束縛集合名（`: Base<T>{b}`）は、それを記した断片内だけで見える。それを使う`origin`関係は同じ断片の制約領域に置く必要があるため、その断片が共有の制約領域も提供する。

ディレクティブの選択と統合の後、フィールド名と計算メンバー名の重複、関数またはコンストラクターのシグネチャの重複、名前空間の衝突はエラーとなる。structが持てる選択済みの`drop`は最大1つである。主断片は不要である。Cレイアウトでは記憶領域を1つの断片にまとめる（[§6.7.1](#s6-7-1)）。

enumとContractは分割できない。同じ同一性（Kotonoha、親、名前、種類、独自の型パラメーター数）を持つものが2つあれば、内容が同じでもエラーとなる。Contractの同一性は宣言を識別し、適合は束縛済み参照で識別する（[§8.6](#s8-6)）。

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

<a id="s6-1-3"></a>
#### 6.1.3. 継承する環境と宣言参照

入れ子のすべてのコンテナーは、groupやContractも含め、外側のジェネリック環境を継承する。ただし実行時の変数やレシーバは継承しない。外側にパラメーターがあれば、使っていなくても内側の宣言はジェネリックとなる。

**宣言参照**とは、宣言の同一性（由来するKotonoha、統合後の親、名前、種類、独自のアリティ）に、その宣言と外側の宣言の正規化済みの型・Semantics・長さ・Origin引数を加えたものである。未使用の引数も保つ。証明と検索の文脈は含めない。パラメータースロット（型、Semantics、長さ、Origin）は、そのバインダーと序数で識別し、綴りでは識別しない。

```kimi
struct Outer<T>
    public struct Inner<U>
        var first: T
        var second: U
// Outer<i32>.Inner<string> and Outer<i64>.Inner<string> are distinct Types.
```

<a id="s6-1-4"></a>
#### 6.1.4. 制約の役割

宣言の利用者が供給する入力条件は、定義の検査中には仮定となる。外側structのSelfの適合や閉じた条件などの宣言義務には、それ自体の証明が必要となる。内側の宣言は外側の入力条件と証明済みの根拠を使えるが、義務を仮定に変えたり、外側のSelfを自身のSelfとして読んだりはできない。内側独自の入力条件は外側へ伝播しない。

Contractでは、継承した引数だけに関する条件は参照の入力条件、適合するSelfに関する条件は実装要件、閉じた条件は宣言義務となる。型宣言の`Self is not ObjectPayload`（[§8.7.3](#s8-7-3)）は明示的な除外であり、条件でも義務でもない。関数制約の主体は[§7.5](#s7-5)に従う。structは、自身の中に入れ子になったContractを実装できる。適合は、自身の識別や互換性の根拠にはならない（[§8.3](#s8-3)、[§8.11.4](#s8-11-4)）。義務は許容されるすべての束縛で成立しなければならない（確定性、[§8.3](#s8-3)）。

<a id="s6-1-5"></a>
#### 6.1.5. Self

`Self`は、すべての束縛を伴う最も内側のstructまたはenumを表す。Contract内では適合する型を表す。groupはSelfの文脈を作らず、その中の`Self`は最も近い外側のSelf型を指す。groupのメンバーはレシーバを持たないが、明示的な型を持つ`self`という名前のパラメーターは妥当である（[§7.3](#s7-3)）。group本体に制約句、適合、関連型の指定を直接置くことはできない。

```kimi
struct Parser
    group Helpers
        func identity(value: Self) -> Self => value@move       // Self is Parser.
        struct State
            func identity(value: Self) -> Self => value@move   // Self is State.
```

<a id="s6-2"></a>
### 6.2. 構造体の宣言

`struct`は名前付きの複合Coreを宣言する。フィールドはSemanticsとOriginを含む完全な型を持つ。記憶領域を加えるのはフィールドだけである。

**有限のインライン記憶領域。** structまたはenumのインライン記憶領域（フィールド、直接の基底、ケースのペイロード、タプル・固定長配列の要素、長さゼロの配列、宣言がインライン格納する型引数を含む）が、借用、生ポインター、オブジェクトハンドルを通らずに同じ宣言へ再び到達する場合、その宣言は不正となる。型引数が何であるかは問わない。この検査は型を使用・インスタンス化するかどうかによらず宣言時に行い、Languageエラーとなる。Resource診断にはしない。生成上限に達し得るのは、間接参照を通した別々のインスタンス化の増大（たとえば多相再帰）だけである。

```kimi
struct Shared
    var next: Option<rc/Shared>         // OK: an object handle is an indirection.

struct Node
    var next: Option<Node>              // Error: Node contains itself inline.

struct Grow<T>
    var more: Option<Grow<(T, T)>>      // Error: inline recursion, whatever the arguments.
```

<a id="s6-2-1"></a>
#### 6.2.1. 論理宣言順

異なるSourceDocumentの宣言は、文書の論理名（[§20.1](#s20-1)）を名前全体のUTF-8バイト列として比較した順（`/`も通常のバイト）に並べ、その後、各文書内の位置で並べる。生成された断片は、実装定義の決定的な順序で、すべての通常の断片の後に置く。フィールド初期化子はこの順に実行し、フィールドは逆順に破棄する（[§16.3.2](#s16-3-2)）。これはレイアウトと独立しているため、ソース名を変えると初期化子の副作用の順序も変わり得る。パッケージのソースリストに順序の意味はない。

```kimi
// a.kimi
struct Config
    var first: i32 = Log.trace(1)
// b.kimi
struct Config
    var second: i32 = Log.trace(2)
// Initialization calls Log.trace(1), then Log.trace(2) (Log: an application group).
```

`#Layout("C")`を適用しない限り、レイアウトは実装定義とする（[§6.7.1](#s6-7-1)）。

<a id="s6-2-2"></a>
#### 6.2.2. 継承とopen構造体

`struct`の前に`open`がなければ、構造体は**sealed**である。sealedな構造体を基底にはできない。openかどうかはアクセスとは独立しており、継承されない。ObjectPayloadの明示的除外（[§8.7.3](#s8-7-3)）は継承される。

構造体は、名前、ジェネリックパラメーター、Originリストの後に`: BaseType`を置き、直接の基底を最大1つ指定できる。基底はアクセス可能な`open struct`のCoreでなければならない。ジェネリックパラメーター、Semanticsを適用した型、group、enum、contractは基底になれない。同じジェネリック宣言の異なる構築を通す場合も含め、継承の循環はエラーとなる。基底の制約が成立し、そのアクセスが派生型の実効アクセス領域を覆わなければならない（[§9.4.4](#s9-4-4)）。

```kimi
public open struct Base
    protected var count: i32 = 0

public struct Leaf : Base
    public func read(self: ref/Self) -> i32 => self.count

public struct Invalid : Leaf     // Error: Leaf is sealed.
```

継承は非公開アクセスを与えない。非virtual呼出しは、オブジェクトビュー経由でも、レシーバの実効型から静的に選んだ実装を使う。継承された名前検索（[§9.6](#s9-6)）とレシーバ射影（[§9.6.1](#s9-6-1)）を適用する。

**継承した名前。** 妥当なオーバーライド（[§6.4](#s6-4)）を除き、派生structは、その宣言からアクセスできる祖先メンバーと同じ名前の値役割のメンバー（任意のシグネチャの関数、フィールド、プロパティ）を宣言できない。条件付き前提やオーバーロードも例外にはならない。プロパティではそのプロパティ自身のアクセスを検査し、protectedアクセスでは派生レシーバで検査する。アクセス不能な祖先の名前は再使用できる。明示的特殊化は別のメンバーではない。

**基底部分オブジェクト。** 直接の基底は1つのインライン所有の部分オブジェクトであり、論理上は直接宣言したフィールドより前に置く。構築では基底を先に実行し（[§6.3.3](#s6-3-3)）、破棄では派生層を先に実行する（[§16.3.2](#s16-3-2)）。基底ビューを通して基底部分オブジェクトをMove、置換、再構築することはできない。値全体の操作は正確な型を保ち、値のスライシングは起こらない。継承フィールドへのアクセスには、それを含むすべての層の部分Move制限を適用する。オブジェクトのアップキャストは[§13.6.1](#s13-6-1)で定義する。

<a id="s6-3"></a>
### 6.3. コンストラクター

<a id="s6-3-1"></a>
#### 6.3.1. 宣言

コンストラクターは、任意のアクセス指定、`init`、パラメーターリスト、任意の`: base(arguments)`句、本体からなる（[§14.2](#s14-2)）。名前、明示的レシーバ、タスクスロット、独自のジェネリックパラメーター、結果注釈は持たない。修飾子は[§F.3](#sf-3)に従う。構造体の型パラメーター、Origin、制約を使う。借用注釈は呼出しごとのOriginを導入でき（[§15.3.4](#s15-3-4)）、入力の省略は[§15.4](#s15-4)に従う。パラメーターのラベル、既定値、型検査は通常の関数パラメーターに従い、Origin関係は実行項目より前に置く。既定のアクセスは`private`である。コンストラクターを宣言できるのは、その構造体自身の断片だけである。

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

<a id="s6-3-2"></a>
#### 6.3.2. 構築式

修飾された型形式のパスに`.init(`が続くものは、常に構築式である。修飾部は`::`で始めてもよく、型引数を含められる。値にしか解決されない修飾部はエラーとなる。

`Type.init(arguments)`は、解決された正確な型の新しいownerを構築する。まず構造体とその外側環境を解決し（[§9.7](#s9-7)）、その後で構造体自身のアクセス可能なコンストラクターを1つ選ぶ（[§10](#s10)）。独自の型引数リストは省略でき（[§10.8.1](#s10-8-1)）、結果は推論された引数を記述した場合と同じになる。引数や期待型が別の宣言を選んだり、外側のコンテナーを推論したりすることはない。

```kimi
struct Box<T>
    public let value: T
    public init(value: T) => self.value = value@move

let number: i32 = 42
let box = Box.init(number)               // Box<i32>.
let wide: Box<i64> = Box.init(42)        // The literal fits i64.
// let wrong: Box<i64> = Box.init(number) // Error: the input binds T to i32.
```

コンストラクターは継承しない。フィールド単位の構築やゼロ埋めへの代替処理はなく、単独の型パラメーターは構築できない。`Type.init`は関数値ではなく、`value.init(...)`は不正である。オブジェクトハンドルは別の操作で作る（[§13.6.2](#s13-6-2)）。

<a id="s6-3-3"></a>
#### 6.3.3. 構築順序

引数を評価した後、Uninitializedな記憶領域を割り当てる。次に基底引数を評価する。このとき`self`にはアクセスできない。その後、基底部分オブジェクトで基底コンストラクターを実行する。基底句を省略すると、引数なしで呼べる基底コンストラクターを選ぶ。基底がないのに`: base(...)`を記すとエラーとなる。基底呼出しはちょうど1回行い、同じ型のコンストラクターへの委譲は存在しない。この位置ではprotectedの基底コンストラクターにアクセスできる（[§9.4.3](#s9-4-3)）。

続いて、この層のフィールド初期化子を論理宣言順（[§6.2.1](#s6-2-1)）に実行し、その後に本体を実行する。フィールド初期化子はパラメーターや`self`を参照できない。暗黙に初期化されるフィールドはない。格納する引数のOriginは、型が宣言したOrigin契約で表現できなければならない（[§15.3](#s15-3)）。

<a id="s6-3-4"></a>
#### 6.3.4. 構築レシーバ

アクセスとLoanの検査では、コンストラクターの`self`は、初期化済みの自身のフィールドに対して`uniq/Self`レシーバと同様の排他的に書込み可能なルートとなる。これらのフィールドは、読出し、Copy、共有・排他借用（レシーバ式の暗黙取得も含む。[§7.3](#s7-3)）、置換ができる。ただし[§11.2](#s11-2)のプロパティ権限に従い、初期化済みの`let`フィールドは読取り専用である。そこからNon-Copy値をMoveして取り出すことはできない。Uninitializedなフィールドへの最初の配置は、[§11.7](#s11-7)と[§15.1.5](#s15-1-5)に従い、カスタム`set`は呼び出さない。

構築中は、計算メンバーへのアクセス、継承フィールドへのアクセス、`self`全体の取得・借用、`self`のメソッド・カスタムアクセサーの呼出し、`self`のキャプチャー・外部への公開を禁止する。フィールドの借用は完了前に終えなければならない。これらの特権があるのはコンストラクター本体自身だけであり、入れ子の関数、メソッド、フィールド初期化子にはない。

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

<a id="s6-3-5"></a>
#### 6.3.5. 完了と失敗

コンストラクター本体はUnitを返す。到達可能な正常終了のすべてで、基底が完了し、自身のフィールドがInitializedでなければならない。その後にスコープ終了を実行する（`defer`で不足する初期化を補うことはできない）。コンストラクターは失敗できない。失敗し得る構築や非同期構築にはファクトリー関数を使う（[§24.2.1](#s24-2-1)）。通常の終了で不完全な層を残すことはできない。不完全な`return`はエラーであり、構築を中断できるのは、後始末を行わないAbortだけである。

<a id="s6-3-6"></a>
#### 6.3.6. 暗黙のコンストラクター

統合後、コンストラクターが1つも選択されておらず（非公開のものも含む）、自身の全フィールドに初期化子があり、基底がある場合は省略した基底句が基底コンストラクターを選ぶときに限り、パラメーターのないコンストラクターを1つ合成する。メンバー単位のコンストラクターは存在しない。

基底の選択は、派生宣言（[§9.4.3](#s9-4-3)）からその制約の下で行う、引数なしの通常の選択である。適用可能な候補がないか選択が曖昧な場合、暗黙コンストラクターはなく、型は妥当なままとなる。候補の前提がUnknownなら暗黙コンストラクターはなく、その型の構築はエラーとなる。合成は初期化子の妥当性には依存しない。合成されたコンストラクターは型の実効アクセスを持ち、`init() => ()`として動作する。[§22.1](#s22-1)に列挙するKimiの型は、宣言されたコンストラクターだけを持つ。

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

<a id="s6-4"></a>
### 6.4. virtualメンバーとオーバーライド

通常のメンバーは非virtualである。virtualスロットは静的に選び、オブジェクトの動的型が選ぶのはその実装だけである。

<a id="s6-4-1"></a>
#### 6.4.1. 宣言

`virtual func`はvirtualスロットと元の実装を宣言し、`override func`は継承したスロットの実装を提供する。修飾子の認識と順序は[§F.3](#sf-3)に従い、両者は併用できない。どちらも、本体と`objref/Self`または`objuniq/Self`レシーバを持つ安全なインスタンス関数でなければならず、独自の型・Semantics・長さパラメーターは持てない。単独の`self`は`ref/Self`なので対象外である。元の宣言は`open struct`の非privateメンバー、オーバーライドは派生structのメンバーである。型関数、group関数、要件、プロパティ、アクセサー、`init`、`drop`はvirtualにならない。

公開契約を所有するのは元の宣言だけである。オーバーライドは明示的特殊化と同じように契約を継承し（[§8.13.2](#s8-13-2)）、レシーバのSelfだけを派生Selfへ対応付ける。

| オーバーライドのヘッダー | 規則 |
| --- | --- |
| 入力、レシーバモード | 個数、順序、正規化後の型が同じ。同じ`objref`または`objuniq` |
| タスクスロット | 元の宣言と同じ（[§24.2.1](#s24-2-1)） |
| 結果 | 種類と正規化後の型が同じ。共変性はない |
| パラメーター名 | 外部名は同じ。内部名は変更可 |
| Origin | 継承する。注釈を記す場合も同じ契約を表すこと |
| アクセス、`!`、既定値、制約、効果 | 元の宣言のアクセス領域とともに継承し、再宣言しない（`protected internal`を派生Kotonoha基準で解釈し直さない） |
| 属性、その他の修飾子 | なし |

本体内の名前検索と非公開アクセスは、派生宣言の文脈を使う。レシーバ以外の`Self`は、記述位置での型を保つ。元の宣言の`other: objref/Self`が`objref/Base`なら、オーバーライドでも`objref/Base`と書く。open struct内のオーバーライドは、さらにオーバーライドできる。

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

<a id="s6-4-2"></a>
#### 6.4.2. オーバーライド対象の選択

**virtualスロット**は、元の宣言と、その宣言型および基底経路の正規化済み束縛で識別する。オーバーライドは次のように対象を選ぶ。

1. 直接の基底から、継承された名前検索（[§9.6](#s9-6)）で最初に見つかる、アクセス可能な同名の関数群を取る。対象になるのはvirtualスロットだけである。
2. オーバーライドの入力型を、共通前提を加えた字句環境で束縛する。共通前提とは、群内のアクセス可能なすべてのvirtualが共有する、置換済みの宣言前提であり、正規化した命題として比較する。置換後の前提がRefutedなら根拠を与えない。タスクの有無、レシーバモード、入力は、明示的特殊化の対象（[§8.13.1](#s8-13-1)）と同じく、制約の証明ではなく正規化した型構造で比較する。その環境で解決できない入力型は宣言エラーとなる。
3. ちょうど1つのスロットが一致しなければならない。オーバーライドはその完全な前提を継承し、その契約に対して検査される。

対象なし、対象が複数、契約の不一致は宣言エラーとなる。結果、ラベル、既定値、宣言順では曖昧さを解消せず、さらに遠い祖先も試さない。派生型が1スロットに持てるオーバーライドは最大1つであり、互いに素な条件付き適合ブロック内でも同じである。

オーバーライドは、名前検索の層、オーバーロード、公開関数を加えない。`Derived.f`と`Base.f`は、元の公開レシーバを持つ同じFunction Itemである。そのため、アクセス（[§9.4.3](#s9-4-3)）とレシーバ型が合えば、`Derived.f(baseObject)`は妥当となる。

```kimi
open struct Base<T>
    public virtual func f(self: objref/Self, x: ref/T) -> i32 => 0
    public virtual func f(self: objref/Self, x: ref/i32) -> i32 => 1
struct D : Base<i32>
    // override func f(self: objref/Self, x: ref/i32) -> i32 => 2
    // Error: matches two slots.
```

<a id="s6-4-3"></a>
#### 6.4.3. 呼出しと条件付きスロット

静的選択がスロットを確定する。実行時には動的型が、継承経路で最も派生した実装、なければ元の実装を選ぶ。名前、オーバーロード、制約、アクセスの検索は行わない。メンバー呼出し、型修飾呼出し、Function Item、関数値はいずれもディスパッチする。未束縛呼出しが暗黙のアップキャストを加えることはない。

元の宣言は、外側のパラメーターに関する条件付きメンバーの前提`P`を持てる。呼出しとFunction Itemの取得には`P`が必要となる。オーバーライドは、派生型の制約の下で、基底経路を通して置換した`P`を仮定して検査する。置換後の`P`がRefutedまたはErrorなら、派生型には決して存在しないスロットを実装しているため、そのオーバーライドは宣言エラーとなる。Unknownなら通常の仮定とする。条件付き適合ブロック内のオーバーライドでは、ブロックの条件がこれらの前提から導けなければならない。実装選択の条件を加えることはできず、適用不能な`P`が代替実装や実行時検査を選ぶこともない。

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

<a id="s6-4-4"></a>
#### 6.4.4. 基底の直接呼出し

`base.f(arguments)`は字句上の`self`を使い、字句上の宣言structの直接の基底から`f`を検索し、その基底の実装を直接呼ぶ（`A → B → C`では、Cからの呼出しはB自身の実装、またはBが継承した実装を実行する）。派生型のインスタンス本体で、アクセス可能な基底のインスタンス関数に使える。呼ばれた本体内の呼出しは引き続きディスパッチする。基底の直接呼出しは、静的に選んだ公開契約と既定値を使い、選んだ本体の効果要約を使ってもよい。無名関数では明示的な`self`キャプチャーが必要となる。単独の`base`、値としての`base.f`、フィールドに対する`base.x`、`base.base`は不正である。

<a id="s6-4-5"></a>
#### 6.4.5. レシーバの安全性

virtualレシーバの借用は、派生フィールドを含むペイロード全体を覆う。オブジェクトの基底ビュー操作には、すべて[§13.6.1](#s13-6-1)のOwned消去証明が必要となる。オーバーライド本体では、元のスロットのOwned消去の根拠が、字句上の`self`オブジェクトを元とするすべてのオブジェクト基底ビュー操作を覆う。virtual呼出し、非virtual呼出し、基底の直接呼出し（`base.f(...)`）のレシーバ射影と、`self`の明示的アップキャストがこれに当たる。ほかのオブジェクトの基底ビュー操作には、それぞれ独自の証明が必要となる。元のvirtual本体にはこの根拠を与えない。破棄中のオブジェクトに対してディスパッチすることはできない（[§16.4](#s16-4)）。

元の宣言とすべてのオーバーライドは、ObjectCallCompatibleがProvenでなければならない（[§12.7](#s12-7)）。スロットは、レシーバ操作が完全性、動的型、Origin束縛を保つことを保証する。基底ビューや基底部分オブジェクトの置換は禁止する。sealedな実装の`self@follow`の置換など、証明済みの完全ペイロード更新は許す。各実装を個別に検査し、違反は未使用でもエラーとなる。

```kimi
open struct Base
    public virtual func g(self: objref/Self) -> i32 => 0
    public virtual func f(self: objref/Self) -> i32 => 0
struct Derived<U> : Base
    var item: U
    override func f(self: objref/Self) -> i32
        return self.g()   // OK: projection of self is covered by the slot's evidence.
```

<a id="s6-4-6"></a>
#### 6.4.6. 効果とContractの証人

スロットの効果境界は[§8.11.1](#s8-11-1)に従う。動的呼出しは、対象がsealedでも、スロットの公開契約と境界だけを使う。効果は[§8.11.4](#s8-11-4)の規則1〜3に従い、結果は公開されたOriginとLoanの契約だけを使う。

スロット上の`preserves results`（[§8.11.3](#s8-11-3)）は、同じオブジェクトに対する同じスロット経由の呼出し、または基底の直接呼出しでは同じ実装の呼出しの間に関係を与える。オブジェクトの同一性はOriginの等価性ではなく依存追跡で判定する。ペイロード内容の置換やMoveはこの関係を終わらせる。ハンドルのMove、再借用、規則に従うビュー変更は関係を保つ。保証を持つ呼出しの保守的な入力効果だけでは、この関係は終わらない。

Contract要件のvirtual実装はスロットに対応する。要件を満たす必要があるのはスロットの公開契約と境界であり、本体のより強い動作では代用できない。

<a id="s6-5"></a>
### 6.5. enum

enumは、公称的で閉じた直和Coreである。値は、ちょうど1つの**ケース**と、そのケースの位置順データである**ペイロード**を保持する。

<a id="s6-5-1"></a>
#### 6.5.1. ケースとペイロード

各ケースは独立した行に記し、ケース名は一意でなければならない。ペイロード要素は1つの完全な型であり、名前、既定値、可変長形式は持たない。`Quit`はペイロードを持たず、`Quit()`は不正である。`Wrapped(())`はUnitのペイロードを持つ。

ヘッダーにはジェネリックパラメーターと、任意のOriginヘッダー（[§15.3.2](#s15-3-2)）を指定できる。本体にはケース、制約句、関連型の指定、関数、条件付き適合（[§8.8](#s8-8)）、ディレクティブを置ける。フィールド、計算メンバー、`init`、`drop`、入れ子のコンテナー、継承は置けない。選択後も少なくとも1つのケースが残らなければならない。各ケースはenumと同じアクセス領域を持ち、修飾子は付けられない。ペイロード型はその領域でアクセス可能でなければならない（[§9.4.4](#s9-4-4)）。ケースがほかの値宣言と衝突するとエラーとなる。

ペイロード要素は無名の記憶領域であり、フィールドと同様に検査する。その依存はenumのOrigin引数に束縛し、変性とLoan要件はすべてのケースから導く（[§15.3.5](#s15-3-5)）。ケースを選んでもenumのOrigin契約は弱まらない。共有経路で到達した`uniq/T`ペイロードは`ref/(uniq/T)`として束縛し、固定された期待型`ref/T`では両方の層を通して再借用する（[§10.2](#s10-2)）。記憶領域は有限でなければならず（[§6.2](#s6-2)）、ヒープへのメモリー割当てを意味しない。ケースの変更は値全体の代入であり、`value.0`による取出しはない。等価性と順序は導出しない（[§13.4.1](#s13-4-1)）。判別値、タグサイズ、ABIは未規定とする。

**オブジェクト。** enumオブジェクトは、所有されたenum値から`@obj`、`@rc`、`@arc`で作る（[§13.6.2](#s13-6-2)）。ペイロードをパターン照合するには、`@follow`でハンドルをたどり（[§13.5.5](#s13-5-5)）、得られた値Placeを照合する。期待型がオブジェクトの位置での`.Case`はエラーとなる（[§6.5.2](#s6-5-2)）。オブジェクトを暗黙に作る操作はない。ケースパターンはオブジェクトハンドルを通して選択しない（[§14.8.1](#s14-8-1)）。ペイロードにはオブジェクトSemanticsを含められる。

```kimi
let h = Option<i32>.Some(1)@obj
let n = match h@follow           // Sealed payload: matches the Option value Place.
    .Some(let x) => x            // x: ref/i32; value-read.
    .None => 0
// let bad: obj/Option<i32> = .Some(1)   // Error: `.Some` needs an owned expected enum Type.
```

<a id="s6-5-2"></a>
#### 6.5.2. ケース参照

ケースを参照できるのは`Type.Case`または`.Case`だけである。非修飾の名前は、展開別名を通してもケースを選ばない（[§9.5](#s9-5)）。修飾形式は通常のメンバーアクセスまたは呼出しであり、名前検索（[§9.6](#s9-6)）がケースを選ぶとケース構築となる。別の呼出しとして試し直すことはない。`.Case`（[§F.9.1](#sf-9-1)）は、既知の期待enum内だけで解決する。これは所有された期待型、またはパターン位置の型（[§14.8.1](#s14-8-1)）である。ペイロードを持つケースは、すべての位置引数を取り、左から右へ評価する。ペイロードのないケースに括弧は付けない。ケースは関数値ではない。

enum自身の型引数は、次のように推論する。

1. 修飾部：独自の型引数リストを省略した場合、[§9.7](#s9-7)に従ってenumを選ぶ。
2. 構築：期待型が同じenumの所有型である場合、その固定された引数を先に供給する。次に、結果として得られるペイロード型へペイロード引数をAdaptationする（[§10.2](#s10-2)。関数型の型消去も含む：[§7.7.4](#s7-7-4)）。残る独自のスロットはペイロード引数から推論する（[§10.8](#s10-8)）。リテラルの既定型（[§10.2.1](#s10-2-1)）を適用した後も未束縛のスロットがあればエラーとなる。先頭ドット形式には、enumを確定する期待型が必要となる。明示した引数が期待型の引数と異なればエラーとなる。
3. パターン：修飾されたケースパターンはenum自身の引数を省略できる。引数はパターン位置の型から得る。明示する場合はそれと等しくなければならない。

外側コンテナーの引数は推論しない（[§9.7.1](#s9-7-1)）。修飾部にはenumのSemanticsやOrigin引数、束縛集合の接尾辞を書かない（`View<T>{v}.Some(...)`はケース参照ではない）。型引数内の完全な型は、それ自身のSemanticsとOriginを保つ。Origin引数は期待型とペイロードから推論し、作り出さない。それらで決まらなければ期待型に注釈を付ける。

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

<a id="s6-6"></a>
### 6.6. 束縛

フィールドとローカル束縛は、`let`（不変）または`var`（可変）で始める。推論可能なら型注釈または初期化子を省略できる。注釈のないフィールドは、初期化子の型を使う（[§11.6](#s11-6)）。ローカルの型は宣言時に固定される。明示した型でOriginを省略する場合は初期化子が必要となる（[§15.4](#s15-4)）。ローカルが見えるのは宣言の後なので、`let x = x`の初期化子は外側の`x`を参照する（[§9.3](#s9-3)）。初期化は[§15.1.1](#s15-1-1)に従う。

```kimi
let limit: i32 = 10
var later: i32                     // Fully specified: no initializer needed.
later = limit
// let r: ref/i32                  // Error: the omitted Origin needs an initializer.
```

<a id="s6-7"></a>
### 6.7. 属性

属性は、任意の引数を伴う`#Name`である（[§F.6.1](#sf-6-1)）。属性は、同じインデントの次の宣言に付く。間にコメントや空行を置いてもよい。対象が単一宣言の`#if`の前に置いた属性は、その選択を共有する。ディレクティブブロックや`#switch`の前にある属性、または後に無関係の項目やインデントの減少が続く属性は、対応先を持たない。対応先のない属性はエラーとなる。

属性は、コンテナー、関数、フィールド、計算宣言、パラメーターに使える。明示的特殊化やContract要件には使えない。除外された構文では構文だけを検査する（[§19.4](#s19-4)）。認識する属性は`#Layout`、`#LibraryImport`（[§22.6](#s22-6)）、`#Test`であり、それ以外の選択された属性はエラーとなる。

```kimi
#Test
func check() => ()
// #test   // Error: an Attribute Name starts with an uppercase (Lu) letter.
```

<a id="s6-7-1"></a>
#### 6.7.1. レイアウト

`#Layout("Kimigayo")`と`#Layout("C")`はstruct宣言にだけ適用し、名前がなく補間もない文字列リテラルをちょうど1つ受け取る。大文字・小文字を区別する。各断片の`#Layout`は、同じ値でも最大1つである。同じstructの断片で異なるモードを指定してはならず、指定がなければKimigayoモードとなる。Cモードでは、生成されたものも含むすべての選択済みインスタンスフィールドを、記述順に1つの断片へまとめなければならない。属性はどの断片に置いてもよい。Kimigayoレイアウトは実装定義であり、CレイアウトはターゲットのC ABIに従う。Cレイアウトのないターゲットは`#Layout("C")`をUnsupportedと報告する。openまたは派生struct、空のstruct、サイズゼロのフィールド（Unit、Never、長さゼロの配列、サイズゼロのstruct）を持つstructにはCレイアウトを使えない。そのようなレイアウトを必要とする使用はLanguageエラーとなる（[§8.15.3](#s8-15-3)）。

```kimi
#Layout("C")
struct NativeRecord
    var kind: u8
    var value: u64

#Layout("c") // Error: mode names are case-sensitive.
struct Invalid
    var value: i32
```

<a id="s6-7-2"></a>
#### 6.7.2. テスト定義

`#Test`は引数を取らず、1つのテストケースを示す。対象は、ルートまたはコンテナー内にある、本体を持つ安全な名前付き関数でなければならない。パラメーター、レシーバ、タスクスロットを持たず、結果はUnitとする。関数も外側のコンテナーも、ジェネリックパラメーターやOriginパラメーターを持ってはならない。外部インポート、特殊化、ローカル関数は対象外である。対象が条件を満たさない場合や`#Test`の重複はエラーとなる。テストビルド以外では、テスト本体は構文より先の検査を行わない。ユーザーコードはTest関数を呼び出せず、値として取得することもできない。テストを呼び出すのはテストランナーだけである（[§20.6.1](#s20-6-1)）。

```kimi
group Arithmetic
    func add(left: i32, right: i32) -> i32 => left + right

    #Test
    func addition()
        $expect(add(1, 2) == 3)
```

---

<a id="s7"></a>
## 7. 関数と呼出し可能な値

[仕様の索引](#contents)

<a id="s7-1"></a>
### 7.1. 宣言、本体、結果

関数宣言は、`func`、非修飾の名前1つ、任意のジェネリックパラメーター、括弧で囲むパラメーターリスト、任意の`->`と結果型からなる（文法：[§F.6](#sf-6)）。`->`のない名前付き関数はUnit（`()`）を返す。関数にOriginパラメーターリストはない。記述した借用注釈は、暗黙のスカラーOriginを導入できる（[§15.3.4](#s15-3-4)）。

関数は、記述したコンテナーまたは実行スコープに属する。`func View.get(...)`のような修飾された宣言名はエラーとなる。

結果型は宣言だけで決まる。ジェネリック本体は宣言された型と制約の下で検査し、インスタンス化時に解釈し直さない。

本体は[§14.2](#s14-2)に従って結果を供給し、すべての`return`は結果型に合わなければならない（[§14.9](#s14-9)）。

```kimi
func direct(left: i32, right: i32) -> i32 => left + right
func indented(left: i32, right: i32) -> i32
    return left + right
func unused() => 123            // Valid: a Unit function discards 123.
func bad(left: i32, right: i32) -> i32
    left + right                // Error: the structural end supplies Unit.
```

**既存の記憶領域の貸出し。** 関数が既存の記憶領域を貸し出すのは、返す参照を通す場合だけである。共有アクセスには`ref/T`、排他アクセスには`uniq/T`を返し、その参照のOriginを伴う（[§15.3.4](#s15-3-4)、[§15.4.3](#s15-4-3)）。return、型合わせ、推論、効果、Originは、すべての結果に共通の規則に従う。呼出し側は参照を値として受け取り、暗黙にたどることはない。`let r = f()`は参照を保持し、期待型があればAdaptationする（[§10.2](#s10-2)）。`f()@follow`はCopyもMoveもせずに参照先を選び、Readと、`uniq/T`経由ならWriteを得るが、Takeは得ない（[§13.5.5](#s13-5-5)）。参照先を通す借用は、参照先と親Loanに依存し、参照を保持する一時値には依存しない（[§10.2](#s10-2)、[§13.5.6](#s13-5-6)、[§15.6.3](#s15-6-3)）。結果の`during self`はレシーバの借用に依存する。`during self.source`など別のOriginがその借用から独立させるのは、宣言と本体の検査がそれを証明する場合だけである。`index`または`indexUniq`の戻り参照をたどるのは、添字式だけである（[§4.7.1](#s4-7-1)）。

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

<a id="s7-2"></a>
### 7.2. パラメーター

無名関数、コンストラクター、レシーバのパラメーター、セッターの`value`を含む明示的パラメーターは、初期化済みで不変の`let`相当の束縛である。`var`パラメーターは存在しない。パラメーターは`@move`で1回移転できるが、その記憶領域への再代入、再初期化、新たな排他借用はできない。`uniq/T`または`objuniq/T`パラメーターは、引き続き参照先への排他アクセスを許す。構築レシーバと破棄レシーバは、それぞれ独自の特権を保つ（[§6.3.4](#s6-3-4)、[§16.3.1](#s16-3-1)）。

```kimi
func grow(items: uniq/Array<i32>, extra: Array<i32>)
    items.appendAll(extra@move)   // Valid: exclusive access to the referent; one transfer.
    // extra = Array.init(capacity: 0)  // Error: a parameter cannot be reassigned.
```

<a id="s7-2-1"></a>
#### 7.2.1. 引数名の境界

名前付き関数、コンストラクター、Contractの関数要件のパラメーターリストには、コンマの代わりに`!`を1つ置ける。その前の通常パラメーターは位置引数と名前付き引数の両方を受け入れ、後の通常パラメーターには外部名が必要となる。`!`がなければ、すべての通常パラメーターが両方を受け入れる。左側は空でもよいが、右側には少なくとも1つの通常パラメーターが必要である。レシーバはどちら側にも置けるが数に含めないので、`(! self)`と`(self !)`は不正となる。末尾のコンマはリスト全体の末尾にだけ置け、`!`に接してはならない。接尾辞`name?`と`name!`はエラーとなる。タスクスロット（[§24.2](#s24-2)）は両側の区間より前に置く。例は`func pause(task; ! delay: Time.Duration)`である。

`external => internal: T`は、どちら側でもパラメーター名を変更する。名前を変更するだけでは、引数名が必須にはならない。外部名はレシーバも含むリスト全体で一意でなければならない。内部名は[§9.3](#s9-3)に従う。

ほかのリストには境界がなく、特殊化ヘッダーにも記さない。外部宣言では境界を許すが、既定値は許さない（[§22.6](#s22-6)）。

<a id="s7-2-2"></a>
#### 7.2.2. 名前の契約と既定値

通常パラメーター数を`N`、境界前の個数を`K`（境界がなければ`K = N`）とする。レシーバとタスクスロットはどちらにも数えない。ゼロから数えたパラメーター`i`に位置引数を渡せるのは、`i < K`の場合に限る（[§10.1.1](#s10-1-1)）。順序付きの外部名と`K`が**引数名契約**を作る。型、レシーバ位置、既定値は含まないため、`(self ! x: i32)`と`(! self, x: i32)`の契約は同じである。特殊化ヘッダーはこの契約を継承する（[§8.13.2](#s8-13-2)）。契約が影響するのは適用可能性であり、オーバーロードの同一性や順位ではない（[§9.1](#s9-1)）。

`= default`を持つパラメーターは省略できる。持たないものは境界のどちら側でも引数が必要となる。既定値付きパラメーターを必須パラメーターより前に置いてもよく、その場合は呼出し側が後者を名前付きで指定する。呼出しは、静的に選んだ宣言の名前契約と既定値を使い、オーバーライドや特殊化のものは使わない。Contract要件を通す呼出しは[§8.4.2](#s8-4-2)に従う。関数値には名前も既定値もなく、呼出しはすべてのパラメーターを位置引数で渡す（[§7.7](#s7-7)）。

```kimi
func scale(value: i32 ! by => factor: i32 = 1) -> i32 => value * factor
func options(mode: i32 = 0 ! count: i32) => ()

scale(3, by: 4)
scale(3, factor: 4)   // Error: factor is the internal name.
scale(3, 4)           // Error: by requires its name.
options(count: 3)     // mode uses its default.
options(3)            // Error: 3 supplies mode, and count is missing.
```

<a id="s7-2-3"></a>
#### 7.2.3. 既定値の評価

各呼出しでは、明示的引数をソース順に保留スロットへ取得した後、省略した各既定値をパラメーター順に、宣言のスコープで1回ずつ評価する。前のパラメーターは使えるが、後のパラメーターや呼出し側のローカルは使えない。前のCopy値をCopyしたり、一時的な共有アクセスで調べたりできる。予約済みの排他入力（[§15.6.7](#s15-6-7)）も排他アクセスを使わずに調べられる。ただし、そのスロットのMoveや変更はできない。結果は、前の引数に対する新しい借用や再借用を保持できないが、既存の共有借用はCopyできる。検査用のLoanは呼出し先への入場前に終わる。既定値が**再帰的**であるとは、その中の呼出しが省略する既定値を通じて、再び自身を評価し得ることをいう。省略した再帰的既定値の値は、それぞれを保持し得る結果と同じく、その呼出しの前のすべての引数のLoanを伴う（[§15.6.4](#s15-6-4)）。既定値内の制御転送は、その内部の構文要素だけを対象とする。

既定値は、ジェネリック本体と同じく宣言時に検査する（[§8.15](#s8-15)）。制約を満たすすべての束縛でパラメーター型に合わなければならず、推論の根拠にはならない。各評価は呼出しの束縛でインスタンス化する。呼出しの通常の引数・Loan検査以外で呼出しを不正にし得るのは、効果境界に加える効果だけである（[§8.11](#s8-11)）。

引数評価を中断する制御転送は呼出し先を実行せず、準備済みの値と一時値を[§16.2.1](#s16-2-1)に従って後始末する。Abortは巻戻しを行わない。準備後、保留スロットはパラメーターとなり、呼出し先がパラメーターの逆順で後始末する。

```kimi
func inc(value: i32) -> i32 => value + 1
func label(x: string, y: string = Text.toString(x)) => ()  // Borrows x; y is independent.
func bad(x: string, y: string = x) => ()                   // Error: a default cannot Move x.
func erased(action: (i32) -> i32 = inc) => ()              // Valid: inc is erased.
func run<F>(action: F = inc)                               // Error: inc's Type is not every such F.
    F is Callable<(i32) -> i32>
    return
```

<a id="s7-3"></a>
### 7.3. レシーバ

<a id="s7-3-1"></a>
#### 7.3.1. インスタンス関数とレシーバ型

structまたはenumで直接宣言した関数、またはContractの関数要件は、パラメーターの1つの内部名が`self`である場合に限り**インスタンス関数**となる。それ以外は**型関数**である。`self`は最大1つであり、タスクスロット（[§24.2.1](#s24-2-1)）より後ならどの位置にも置ける。名前変更や既定値は許さない。

レシーバの正規化後の型は、`raw`以外の具体的なSemantics`S`1つに対する`S/Self`である。つまり、通常の型形成規則の下での`Self`、`ref/Self`、`uniq/Self`、`obj/Self`、`rc/Self`、`arc/Self`、`objref/Self`、`objuniq/Self`となる（オブジェクトSemanticsでは`Self`がObjectPayloadでなければならない）。Origin注釈は通常のパラメーター規則に従う。それ以外のレシーバ型はエラーとなる。`raw/Self`、`s`の制約の有無を問わずジェネリックSemanticsの`s/Self`、`ref/(ref/Self)`のような追加の参照層、別の対象がこれに当たる。virtual関数はオブジェクト借用レシーバを使う（[§6.4.1](#s6-4-1)）。

ローカルの名前付き関数、group、rootgroupはレシーバを持たない。そこにある`self`というパラメーターは通常のパラメーターである。ローカルの名前付き関数は何もキャプチャーせず、外側関数の文脈的な`self`は内部から見えない。

**略記。** 単独の`self`は`self: ref/Self`を意味し、本体検査前に確定する。Contract要件や完全特殊化を含む、インスタンス関数のレシーバ位置だけで使える。名前付き関数やコンストラクターのほかのパラメーターには、すべて型が必要となる。アクセサーのレシーバには独自の略記がある（[§11.3](#s11-3)）。

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

<a id="s7-3-2"></a>
#### 7.3.2. レシーバの形

レシーバの**形**はそのSemanticsである。1つの関数群（[§9.6](#s9-6)）は、レシーバを持つ関数だけを数えて、1つの形を持つ。型の宣言群またはContractの要件群（[§8.4.2](#s8-4-2)）での違反は、パラメーター、ラベル、`when`条件にかかわらず宣言エラーとなる。制約から集めた群は使用時に検査する。完全特殊化は元の宣言の形を保つ（[§8.13.2](#s8-13-2)）。プロパティの`get`と`set`は別の操作なのでこの規則の対象外であり、その形は[§11.3](#s11-3)が固定する。このため、オーバーロード解決より前に名前がレシーバの取得を確定し、最良候補の比較は明示的引数だけを対象とする（[§10.4](#s10-4)）。群はタスクスロットの形も1つだけ持つ（[§24.2.3](#s24-2-3)）。

```kimi
struct Counter
    var value: i32 = 0
    func peek(self) -> i32 => self.value
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self, by: i32) -> i32 => by   // Error: next would have two shapes.
```

<a id="s7-3-3"></a>
#### 7.3.3. メソッド呼出し

`receiver.method(arguments)`では、最初にレシーバを評価・取得し、宣言された`self`の位置へ渡す。これはレシーバ式（[§3.4](#s3-4)）であり、表の操作を記述したものとして取得する。明示的引数は残るパラメーターに対応付ける。どのラベルも`self`を供給せず、タスク引数もパラメーターを束縛しない（[§24.2.2](#s24-2-2)）。排他的な準備は[§15.6.7](#s15-6-7)に従う。

| レシーバ型 | 値種の入力`p` | オブジェクト種の入力`p` |
| --- | --- | --- |
| `ref/Self`、`uniq/Self` | 所有Placeまたは一時値：`p@ref`、`p@uniq`。借用値：[§3.4.3](#s3-4-3)の経路上で`p@follow@ref`、`p@follow@uniq` | View Targetが正確に完全なSealed型なら`p@follow@ref`、`p@follow@uniq`（[§13.5.5](#s13-5-5)）。それ以外は`p@objref`、`p@objuniq` |
| `objref/Self`、`objuniq/Self` | 適用不可 | `p@objref`、`p@objuniq` |
| 基底`B`で宣言 | [§9.6.1](#s9-6-1)の射影 | 同左 |
| `Self`、`obj/Self`、`rc/Self`、`arc/Self` | 一時値はそのまま。所有PlaceはCopyならCopyし、それ以外は取得しない（`p@move.m()`と書く）。読出し型への参照（[§3.5.3](#s3-5-3)）は値を読み出し、それ以外は取得しない（Copyな参照先をCopyするには`p@follow.m()`と書く） | 一時値はそのまま。所有Placeには`p@move`が必要。オブジェクト借用は何も供給しない（読出しなし） |

経路上のペア層（[§13.5.5](#s13-5-5)）は借用値と同じくたどる。それを通す値レシーバが得られるのは、読出し型の値読出しだけである。

次が順に成立しなければ、呼出しはエラーとなる。（1）供給する操作が、その明示的な規則に従って合法であること（静的記憶領域、ジェネリックSemantics、共有専用の`rc`/`arc`、プロパティ権限、ゲッター結果の記憶領域、排他取得：[§15.2.3](#s15-2-3)、[§8.14](#s8-14)、[§13.5.5](#s13-5-5)、[§11.2](#s11-2)、[§11.5](#s11-5)、[§15.1.5](#s15-1-5)）。（2）経路のレシーバ互換性が成立すること。値種の経路では完全な型が要求型に合わなければならない。オブジェクト借用と基底射影の経路は[§12.6](#s12-6)、[§12.7](#s12-7)、[§9.6.1](#s9-6-1)に従う。したがって`objuniq/T`が`uniq/Self`を満たすのは、変換ではなく経路の規則による。（3）選択後、保護されたオブジェクト借用または基底射影のObjectCallCompatibleがProvenであること（[§12.7](#s12-7)）。証明がなくても選び直さない。

検査に失敗しても別の取得へ切り替えないため、Copyを変更して結果を捨てることはない。表の操作を明示しても意味は同じであり、別の操作を記せばその操作の意味になる（`tasks@uniq.length`は`tasks`を排他的に貸し出してから読む）。連鎖では各呼出しが表に従って直前の結果を取得する。

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

<a id="s7-3-4"></a>
#### 7.3.4. 未束縛の参照

`Meter.update`のように型で修飾したインスタンス関数参照は、未束縛である。それを通す呼出しは、すべてのパラメーターを渡す。レシーバより前のすべてのパラメーターを位置引数で渡すなら、レシーバも宣言位置に位置引数として渡せる。そうでなければ名前付きの`self:`引数として渡す。ほかのパラメーターは名前契約（[§7.2.2](#s7-2-2)）を保つ。参照から形成した関数値だけが、すべてを位置引数で呼ぶ。レシーバはレシーバ式ではなく通常の引数なので、`uniq/Self`に渡す所有Placeには`@uniq`が必要となる。未束縛の関数値はレシーバをキャプチャーしない。呼出しを伴わない`value.method`は、束縛済みメソッド値を形成しない。

```kimi
struct Meter
    func update(self: uniq/Self, value: i32) => ()
    func mark(x: i32 ! y: i32, self) => ()

var meter = makeMeter()
Meter.update(meter@uniq, value: 5)    // The receiver is positional; value may also be positional.
Meter.mark(1, y: 2, self: meter@ref)  // The receiver cannot be positional after y.
// Meter.update(meter, 5)             // Error: an owned Place at uniq/Self needs @uniq.
```

<a id="s7-4"></a>
### 7.4. パラメーター取得の形

1つの関数群で引数をどう取得するかは、名前と位置から決まり、ほかの引数が選ぶ候補では決まらない。

<a id="s7-4-1"></a>
#### 7.4.1. 取得モードと許容ケース

パラメーターの**取得モード**とは、修飾なしの所有Place引数に何を要求するかをいう。正規化した型（別名を展開し、グループ化と冗長な`owner`を除いたもの）の外側Semanticsで判定する。名称はSubjectのモード（[§15.1.6](#s15-1-6)）と同じである。

| モード | 外側Semantics |
| --- | --- |
| ByValue | `owner`（Core、タプル、固定長配列、関数型）、`obj`、`rc`、`arc`、`raw` |
| Shared | `ref`、`objref` |
| Exclusive | `uniq`、`objuniq` |

外側Semanticsが置換で束縛されるパラメーターは、取り得るSemanticsごとに1つの**許容ケース**を持ち、それを明記したものとして判定する。ペアバインダー`s`の`s/U`なら`s`の許容集合（[§8.3](#s8-3)）、関数や外側型の型パラメーター`T`、または正規化されない関連射影ならすべてのSemanticsが該当する（[§8.1.1](#s8-1-1)）。

推論は、関数自身のスロットを引数の完全な型へ束縛する。修飾なしの所有Placeでは、明示的型引数だけがそのスロットを`owner`以外のSemanticsに束縛する（[§10.1](#s10-1)、[§10.8](#s10-8)）。明示的型引数が到達できるのは、ジェネリックリストの長さと種類が同じ宣言だけである。そのため、それ以外の宣言との比較では、`owner`を許す関数スロットには`owner`ケースだけがある。スロットの射影はほかの引数から束縛され、すべてのケースを保つ。

<a id="s7-4-2"></a>
#### 7.4.2. 対応するパラメーター

宣言には2つのパラメーター列がある。レシーバを除く束縛済み呼出し`value.f(...)`と、レシーバを記述位置に含む未束縛呼出し`Type.f(...)`である。レシーバのない関数は未束縛列だけを持つ。2つの宣言のパラメーターが**対応する**とは、同じ列で同じ位置番号に位置引数として渡せるか、外部名が同じであることをいう。未束縛列でレシーバを位置引数として渡せるのは、前のすべてのパラメーターも位置引数として渡せる場合だけである（[§7.3.4](#s7-3-4)）。それ以外では`self:`としてだけ渡し、名前だけで対応する。束縛済み呼出しのレシーバは[§7.3.2](#s7-3-2)に従う。既定値、個数、`!`の位置、ラベル、制約、`when`条件は対応を変えない。タスクスロットは何にも対応せず、番号もずらさない。

<a id="s7-4-3"></a>
#### 7.4.3. 照合キーと重なり

| パラメーター型 | 照合キー |
| --- | --- |
| `U`（つまり`owner/U`）、`ref/U`、`uniq/U` | `Value(U)` |
| `obj/U`、`rc/U`、`arc/U`、`objref/U`、`objuniq/U` | `Object(U)` |
| ペアバインダー`s`の`s/U` | `s`の各許容ケースのキー。そのSemanticsを明記したものとして形成する（`raw`ケースは`Value(raw/U)`） |
| 単独の型パラメーター`T`、元の`s/T`、ペア対象単独、正規化されない関連射影 | Any。すべてのキーと重なる |
| その他（`raw/U`、Core、タプル、固定長配列、関数型） | 型自体の`Value` |

2つの照合キーが重なるのは、[§8.9.1](#s8-9-1)の手順2〜4の単一化で単一化できる場合である（手順1の制約正規化は行わない）。非重複性を証明できるのは、固定構造の不一致だけである。異なる公称型・タプル・関数型、異なる確定済みSemantics（`uniq/Node`と`Node`）、異なる値の2つの定数長が該当する。スロット、未解決の関連射影、非定数の長さは、束縛し得るものすべてと重なる。Originは無視する。使用時に推論するスロットは候補ごとに独立し、固定された外側スロットは共有する。異なるスロット同士も単一化できるので、外側スロット`A`と`B`に対して`Array<A>`と`ref/Array<B>`は重なる。型制約は参照しない。重なりは補完済みのヘッダーで判定する（[§8.6](#s8-6)）。

<a id="s7-4-4"></a>
#### 7.4.4. 宣言と呼出しの契約

1つの群の2宣言について、対応するパラメーターの各ペアを次のように検査する。

1. スロットを独立に束縛して、許容ケースの全組合せを検査する。重なる組合せがすべて同じモードなら、そのペアは妥当である。
2. そうでなければ、許される相関を仮定して繰り返す。相関とは、同じ構築対象スロット、または長さと種類が同じ2つの関数ジェネリックリストの同じ番号のスロット（等しい元からの等しい射影）である。このとき、キーが重なるすべての位置でペアが1つのモードを持つなら、その相関に依存する契約の下で妥当となる。
3. それ以外では、両宣言を共存させることはできない。

Contractの継承要件（[§8.4.3](#s8-4-3)）も含め、同じスコープの同名宣言間の違反は宣言エラーとなる。使用時に集める群（[§9.5.1](#s9-5-1)、[§9.6](#s9-6)）では、引数にかかわらずその使用位置でエラーとなる。個数、ラベル、既定値、`when`条件によって対象外になるペアはない。完全特殊化は元の宣言の契約を保つ。組込み演算子、添字、比較の候補にもこの規則を適用する（`index`と`indexUniq`）。

**呼出し契約。** 宣言時の妥当性がスロットの相関（手順2）に依存する群を、明示的型引数なしで呼ぶ場合、個数、ラベル、既定値、制約、条件、期待結果による候補の絞込みより前に、依存する各相関を確立しなければならない。相関が成立するのは、比較するすべての候補が同じ根拠からスロットを束縛する場合である。根拠は、同じ固定済みの明示的束縛や環境束縛、または対応するパラメーターの同じ独立に型付け可能な引数やLiteral引数（[§10.5.1](#s10-5-1)）である。後者は、[§10.1](#s10-1)の同じ推論手順（構造照合または呼出しシグネチャ照合）で、同じ構造経路（パラメーター型からスロットまでの型コンストラクターと引数添字）に沿って照合される必要がある。呼出しの期待結果も、結果型の同じ構造経路に沿う場合は根拠となる。引数の型はまだ解決されていなくてもよい。同じ未解決のリテラル引数はスロットを相関させ、その後のリテラルの型合わせと既定型の適用は通常どおり進む（[§10.2.1](#s10-2-1)）。既定値で埋めた引数は根拠にならない。候補のどれかがスロットに追加の根拠源を持てば、相関は成立しない。この根拠から、重なる位置が同じ取得モードになると示せれば十分であり、完全な型の等しさは要求しない。候補の除外、変換の探索、関連型の解決、待機引数の解析によって相関を確立することはない。相関を確立できない呼出しは、明示的型引数を記さない限りエラーとなる。

**帰結。** キーが重なる位置では、どの候補も同じ方法で引数を取得する。各候補内では、参照引数自身の型によって取得方法が決まる（[§10.2.2](#s10-2-2)）。`@uniq`を書き忘れた場合は唯一の候補でエラーとなり（[§15.1.5](#s15-1-5)）、値渡しの候補を黙って選ぶことはない。単一モードの保証があるのは、キーが重なる位置だけである。重ならないキーでは、`inspect(value: uniq/(uniq/Node))`と`inspect(value: ref/Node)`のように、引数の型が候補を選ぶ。

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

これらのコンストラクターの一方が`witness: Tag<T>`、もう一方が`witness: Tag<ref/T>`も受け取る場合、渡した`Tag<ref/i32>`は一方の候補に追加の根拠源を与える。その呼出しには明示的型引数が必要となる。

<a id="s7-5"></a>
### 7.5. 関数の制約

関数、コンストラクター、デストラクター、アクセサーのインデントされた本体は、**先行領域**で始まる。これは、括弧で囲まない制約句（[§8.2](#s8-2)。各句にはインデントされたCallable効果リストを伴ってもよい：[§8.11.6](#s8-11-6)）、`origin`関係句（[§15.3.3](#s15-3-3)）、元のvirtual関数だけに置ける効果項目（[§8.11.1](#s8-11-1)）が任意の順で続く、先頭の最長列である。空行やコメントでは終わらない。`(value is Dog)`のような括弧付きの検査は実行項目なので、この領域を終える。コンパイル時ディレクティブ内の項目は、すべての分岐で、その項目自身の種類として数える（[§19.4](#s19-4)）。後方に現れる、ジェネリックパラメーターを根とする制約句は、制約位置のエラーとなる。効果項目をほかの位置に置くこともエラーとなる。アクセサーの先行領域に置けるのは`origin`関係だけであり、デストラクター本体には先行領域の項目を置けない。

各主体は、関数または宣言型のジェネリックパラメーター、あるいはそれを根とする関連射影を指す（[§8.5](#s8-5)）。結果型を含むシグネチャは先行制約に依存してもよい。同一性の前提は[§8.2](#s8-2)に従う。関数要件には独自のインデント領域がある（[§8.4.2](#s8-4-2)）。

呼出しのすべてのジェネリック引数はこれらの句を満たさなければならず、本体はそれらに依存できる。制約はシグネチャの一部ではない。制約だけが異なる宣言は衝突する。

宣言型のジェネリックパラメーターを句で制約するメンバー関数やコンストラクターを、**条件付きメンバー**という。その前提は、それらの句と外側の`when`条件に、レシーバまたは修飾部の型引数を代入したものである。適用可能性は[§8.8.2](#s8-8-2)と同様に決める。前提がRefutedなら、名前検索は変えずにメンバーを適用不能とする。ジェネリックな呼出し側は前提を証明しなければならない。条件付きメンバーに対象Contractは不要であり、適合も与えない。

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

<a id="s7-6"></a>
### 7.6. unsafe関数

`unsafe func`で宣言する**unsafe関数**は、安全性の契約を持つ。これは呼出し側が満たすべき、型で表せないメモリー安全性の条件であり、`safety`項目で文書化する（[§2.3](#s2-3)）。呼出しにはUnsafeブロックが必要となり（[§14.3](#s14-3)）、契約違反は未定義動作となる。`unsafe func`を付けても、その本体自体はunsafe文脈にならない。

`unsafe`はシグネチャの一部ではない。オーバーロード解決は`unsafe`と呼出し側のunsafe文脈を無視し、選択後に要件を検査する。unsafe関数が対応するのは直接呼出しだけである。値としての取得、受渡し、変換はエラーとなる。unsafe関数型は将来の対象とする（[§D](#sd)）。

```kimi
/// - safety: pointer refers to a live, initialized, readable i32 for the call.
unsafe func read(pointer: raw/i32) -> i32
    unsafe => return *pointer

unsafe func invalidRead(pointer: raw/i32) -> i32
    return *pointer      // Error: the body is not an unsafe context.

let reader = read        // Error: an unsafe function is not a value.
```

<a id="s7-7"></a>
### 7.7. 関数式

<a id="s7-7-1"></a>
#### 7.7.1. 構文と推論

無名関数は、`func`、任意のキャプチャーリスト、パラメーター、任意の結果注釈、本体からなる（文法：[§F.9](#sf-9)）。単なる`(x) => x`形式はなく、ラベル、既定値、`!`境界、ジェネリック無名関数もない。タスクスロットを持つのは、ヘッダーに明記した場合だけである（[§24.2.1](#s24-2-1)）。各パラメーターに引数が必要であり、呼出しは位置引数で行う。

パラメーター型を省略するには、固定された期待呼出しシグネチャが必要である（[§10.5.2](#s10-5-2)）。これが、タスクスロットの後の位置に対応する型を供給する。本体や後の使用からパラメーター型を推論することはない。結果はその期待から、またはパラメーター確定後に本体から、結果のOriginとLoanを伴って得る（[§15.8.2](#s15-8-2)）。本体の結果が外側の呼出しのスロットの根拠になることはない。無名関数は待機引数であり（[§10.5.1](#s10-5-1)）、スロットに束縛されたクロージャー型が別のスロットを埋めることもない（[§10.8](#s10-8)）。`try`の失敗は結果型候補を供給せず（[§17.3](#s17-3)）、成功値をラップすることもない。

作成時に評価するのはキャプチャーだけである。呼出しは独自の関数境界の下で本体を実行し、外側の制御転送先やUnsafeの許可は引き継がない。

```kimi
let twice = func (value: i32) => value * 2              // Result: i32.
let positive: (i32) -> bool = func (value) => value > 0
let invalid = func (value) => value * 2                 // Error: no fixed input signature.
```

<a id="s7-7-2"></a>
#### 7.7.2. キャプチャー

| リストまたは項目 | 作成時の効果 |
| --- | --- |
| リストなし | 本体が使う外側の実行時束縛をそれぞれCopyする。すべてCopyでなければならない |
| `[]` | 実行時キャプチャーなし |
| `[x, y]` | 列挙した束縛だけをキャプチャーする |
| `x` / `var x` | `let x = x` / `var x = x`と同じように環境の束縛`x`を初期化する |
| `x@op` / `var x@op` | `let x = x@op` / `var x = x@op`と同様。`op`は`move`、`ref`、`uniq` |

キャプチャーは束縛の同一性で解決する。リストがない場合、Move、借用、再借用（[§3.5](#s3-5)）、部分キャプチャーは推論しない。そのため、Copyなフィールドしか読まなくてもNon-Copyのルートは拒否する。既存の`ref/T`は依存とともにCopyする。ジェネリックパラメーターに依存するCopyには、宣言された根拠が必要となる。

明示的な項目は1つの束縛を指し、作成時に、その初期化と同じように取得する（[§3.5](#s3-5)、[§13.5](#s13-5)、[§8.14](#s8-14)）。修飾なしの項目は`uniq/T`または`objuniq/T`を保持する束縛を再借用し、それ以外のNon-Copy束縛を拒否する。`x@ref`または`x@uniq`は外側のスロットを借用し、その後クロージャーはそのスロットに依存する（[§15.8](#s15-8)）。

キャプチャーするのは、外側の実行時ローカルとパラメーターだけである。型名、コンテナー、静的記憶領域（静的フィールドとプロパティ。その直接使用は静的アクセスとなる。[§15.6.4](#s15-6-4)）、ローカル関数も含む名前付き関数はキャプチャーではない。最上位のローカルも通常のローカルなので、同じようにキャプチャーする。文脈的な`self`とセッターの`value`は暗黙にキャプチャーしない。明示的にキャプチャーする場合はレシーバとアクセサーの規則に従う。コンストラクターや`drop`では、どの項目でも`self`をキャプチャーできない（[§6.3.4](#s6-3-4)、[§16.3.1](#s16-3-1)）。文脈的な`storage`束縛は名前でキャプチャーできない。

明示的項目は未使用のものも含め左から右へ実行する。重複とパラメーター名はエラーとなる。推論したキャプチャーは、選択済みソースでの初出順に、それぞれ1回実行する。

`var`のないキャプチャーは、元の可変性によらず`let`相当の環境束縛となる。値キャプチャーはスナップショットであり、`var`が変えるのは束縛の可変性だけである。呼出し中、各環境束縛はキャプチャー時の`let`/`var`可変性を持つ、その呼出しのローカルとして働く。Shared呼出しは読出しだけを行い、Exclusive呼出しはさらに`var`束縛への代入とキャプチャーした`uniq`の参照先の使用ができる。キャプチャーをMoveして取り出せるのはConsuming呼出しだけである。消費した`let`キャプチャーは再初期化できない。環境束縛はパラメーターではないため、[§15.1.5](#s15-1-5)の所有型パラメーターの制限は適用しない。どの環境も、別のキャプチャーを通して自身の所有キャプチャーを借用してはならない。

**入れ子のクロージャー**は、すべての外側環境を通して取得する。内側のクロージャーだけで使う束縛も、外側でキャプチャーしなければならない。リストを省略した各層は、それぞれCopyを要求する。外側環境の値を内側のクロージャーへMoveすると、外側の呼出しはConsumingとなる。内側の`[x@uniq]`は外側環境の束縛`x`を借用する。`x`が`let`ならエラーとなり、それ以外は外側呼出しの許可で判定する（[§7.7.3](#s7-7-3)）。

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

<a id="s7-7-3"></a>
#### 7.7.3. 呼出しレシーバ

各具象クロージャーは、選択済みソース内の本体のすべての解決済み操作から、最小の**呼出しレシーバ要件**を1つ持つ。

| 要件 | レシーバ | 本体のアクセス |
| --- | --- | --- |
| Shared | `ref/Self` | 環境を読む |
| Exclusive | `uniq/Self` | 環境またはキャプチャーした参照先を変更する |
| Consuming | `owner/Self` | 環境から値をMoveして取り出す |

可能な経路のどれかでMoveするならConsumingを要求する。条件付き取得計画（[§8.14](#s8-14)）が許容ケースごとに異なる効果を与える場合、各ケースが独自の要件を持ち、すべての使用を各ケースで検査する（[§8.15](#s8-15)）。たとえば、ペア層を型とするキャプチャー値を修飾なしで取得する呼出しは、その取得が再借用になるケースでのみExclusiveとなる。

直接呼出しは、メソッドレシーバと同じくレシーバを取得する（[§7.3.3](#s7-3-3)）。所有クロージャーPlaceは、Shared呼出しでは共有借用し、Exclusive呼出しでは貸出し点が排他的に書込み可能なら排他借用する（[§15.1.5](#s15-1-5)）。`uniq/F`と`ref/F`の値は再借用し、`ref/F`はExclusive呼出しを供給できない。Consuming呼出しはCopyクロージャーならCopyし、それ以外は`c@move()`を必要とする。

呼出しシグネチャは、タスクスロットの有無、レシーバ、パラメーターと結果の型、Origin、結果のLoan依存を保つ。タスクスロットのあるシグネチャを通す呼出しはタスク呼出しとなる（[§24.3](#s24-3)）。レシーバの保護は後続引数の評価前に始まり（[§15.6.7](#s15-6-7)）、間接呼出しやCallable呼出しでも同じである。ジェネリック呼出しは、宣言されたCallableレシーバに従う（[§8.10](#s8-10)）。

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

<a id="s7-7-4"></a>
#### 7.7.4. 関数参照と型消去

関数参照は[§10.5](#s10-5)に従って解決し、束縛済みジェネリック引数とOrigin契約を含むFunction Item型を持つ。異なる宣言は異なる型を持つ。`drop`は値として取得できない。組込みの証人（[§13.3.3](#s13-3-3)）を含む要件参照にも同じ規則を適用する。コンパイラー実装の安全な関数も同じように参照し、要件値は選択した実装を保持する。

固定された期待型（[§10.2](#s10-2)）が共通関数型である位置では、次のすべてを満たす場合に限り、Function Itemまたは具象クロージャーを暗黙に型消去する。

- シグネチャが合い、タスクスロットの有無が同じであり（[§24.2.4](#s24-2-4)）、最小レシーバがSharedである。
- 完全な環境がOwnedであり、結果が環境を借用しない。
- 公開されたすべてのOriginとLoanの契約が成立し、元の値の通常の取得が合法である。

型消去は、別の暗黙Adaptationと連鎖させない。借用されたクロージャーは型消去せず（[§3.2.1](#s3-2-1)）、型消去した値を続けて借用することもなく、既存の構造の内部も型消去しない。明示的取得は型消去に先行できる（[§10.2](#s10-2)）。[§14.9.2](#s14-9-2)の共通型探索は型消去しない。修飾したケース構築は、先頭ドット形式と同じくペイロードを型消去する（[§6.5.2](#s6-5-2)）。

型消去は所有する変換であり、元の値のCopyやMoveとは別である。元の値を通常どおり取得し（[§3.5](#s3-5)）、キャプチャーを繰り返さない。型消去した値はNon-Copyとなる。レイアウト、メモリー割当て回数、インライン化は実装定義である。メモリー割当てを省略しても、値、Move、Loan、後始末（[§15](#s15)）を保ち、その省略によって失敗してはならない。型消去がメモリー割当てを行い、それに失敗した場合は、Moveを巻き戻さずにプログラムをAbortする（位置：[§22.5.1](#s22-5-1)）。`Callable`を期待する位置に具象クロージャーを渡すことは、型消去ではない。

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

---

<a id="s8"></a>
## 8. ジェネリクス、制約、Contract

[仕様の索引](#contents)

制約は、ジェネリック本体が仮定できることと、各使用箇所で証明すべきことを定める。Contractは能力に名前を付け、要件を実装に対応付ける。明示的特殊化は実行する実装を変えるが、呼出しの契約は変えない。

<a id="s8-1"></a>
### 8.1. ジェネリックパラメーター

<a id="s8-1-1"></a>
#### 8.1.1. 型スロットとペアパラメーター

型スロット`<T>`とペアパラメーター`<s/T>`は、それぞれ完全な型引数`W`を1つ受け取り、そのSemantics、入れ子の型、Originを保持する。長さスロットには`<length N>`を使う（[§4.4](#s4-4)）。単なる`<N>`は型スロットである。Originパラメーターには専用のスキーマがある（[§15.3](#s15-3)）。

- `<T>`は`T`を`W`に束縛する。これはCoreだけでなく、完全な値の型である。
- `<s/T>`は`W`の外層を分ける。`s`は**ペアバインダー**（外側のSemantics）、`T`は**ペア対象**（直接の対象）である。元の`s/T`は引き続き`W`を表す。外層が安全な借用なら、`W`にはソース上の名前を持たない外側のOrigin `o`がある。`o`はペアバインダーごとに本体の固定Origin（[§15.6.5](#s15-6-5)）を1つ表し、元の`s/T`の全出現箇所で共有され、`s`に許容される借用の場合にのみ存在する。

分割前に、エイリアス、補完済みの射影、括弧によるグループ化、冗長な`owner/`を正規化する。エイリアスの循環はエラーである。`ref/(uniq/i32 during b) during a`では`s = ref`、`T = uniq/i32 during b`、外側のOriginは`a`となる。`owner/(ref/i32 during a)`の外側のSemanticsは`ref`である。

| 外側のSemantics | ペア対象と、別の`U`に`s`を適用するための要件 | 外側のOrigin |
| --- | --- | --- |
| `owner` | `W`自身。`owner/U`は`U` | なし |
| `ref`, `uniq` | 完全な参照先の型。`uniq`には排他的な取得とLoanも必要 | あり |
| `obj`, `rc`, `arc` | オブジェクト対象（[§8.7.3](#s8-7-3)） | なし |
| `objref`, `objuniq` | オブジェクト対象 | あり |
| `raw` | 完全なポインター先の型。寿命の保証はない | ペアではなし。そのため`raw/T during o`の引数はOriginを取り除くことで期待型に合う。具体的な`raw`層にはOriginを記述できる（[§5.7](#s5-7)） |

外側のOriginがなくても、ペイロードの依存関係は消去されない。`ref/(View<i32>{v}) during b`の`v.source`は内側の型に属する。

**値の型としてのペア対象。** 本体で`T`を単独の値の型として使えるのは（例えば`Option<T>`）、`s`に許容されるすべての場合で`T`が値の型となるときだけである。`owner`、`ref`、`uniq`、`raw`では値の型となる。`s`の許容集合がオブジェクト系と交わるなら、`T is ObjectPayload`がProvenでなければならない（[§8.7.3](#s8-7-3)）。そうでなければ、その使用は定義の検査時にエラーとなる。これは、探索によって`T`を選んだ後に行う型の合法性の検査である（[§9.2](#s9-2)）。

**オブジェクト形式。** ジェネリックな対象に対してオブジェクト形式を形成するには、シグネチャと本体でオブジェクト対象の証拠（[§8.7.3](#s8-7-3)）が必要になる。ペア自身の`T`には、**ペアの証拠**で足りる。すなわち、`s`の許容集合（[§8.3.4](#s8-3-4)）がオブジェクト系（`obj`、`rc`、`arc`、`objref`、`objuniq`）に含まれることである。別の`U`に`s`を適用するときは、許容集合がオブジェクト系と交わる場合に`U`がオブジェクト対象である必要があり、許容されるすべてのSemanticsが`U`と有効な型を形成しなければならない。記述されたシグネチャが整形式であることから証拠を導くことはない。

同じパラメーターリスト内の名前は互いに異ならなければならない。`<T, T>`、`<s/T, s/U>`、`<s/s>`はエラーである。`<s>`は通常の型スロットであり、単独のSemanticsスロットはない。

```kimi
func keep<s/T>(x: s/T)
    s is ref or uniq
    let empty: Option<T> = .None      // Valid: T is a Referent Type in both cases.

func lose<s/T>(x: s/T)
    let empty: Option<T> = .None      // Error: s may be obj, and T is not proven ObjectPayload.
```

<a id="s8-1-2"></a>
#### 8.1.2. ペアの再構成とOrigin注釈

元の`s/T`は、インスタンス化後の型の等価性ではなく、宣言された束縛によって`W`を指す。別の`U`に対する`s/U`はSemanticsの種類だけを適用し、`W`の外側のOriginをコピーしない。

- **注釈。** `s/T during b`または`s/U during b`は、その出現箇所の外側のOriginスロットを束縛する。このスロットが有効になるのは`s`に許容される借用の場合だけなので、`Collection<s/Option<T> during a>`の記述は`s`が何を許容しても変わらない。`W = ref/i32 during a`なら、`s/T during b`は`a`と`b`を関係付けたり値を変換したりせずに`ref/i32 during b`を形成する。そこに値が合うかを判定するときに、短縮、取得、Loanを検査する。注釈は再借用を生みも取り除きもしない。
- **省略。** `s/U`は、省略されたOriginの位置ごとの規則（[§15.4](#s15-4)）に記号的に従う。直接の入力には独立した外側のOriginスロットが与えられ、`s`が安全な借用の場合だけ有効になる。フィールドや入れ子の借用層で新たに省略が許されるわけではない。
- **追跡。** ペア層の追跡（[§13.5.5](#s13-5-5)）では、追跡する出現箇所の外側のOriginスロットを使う。各依存関係は、それを生むSemanticsの場合（[§8.15.2](#s8-15-2)）に限って有効になる。

型の形成と本体の合法性は、許容されるすべての束縛で成立しなければならない。インスタンス化では正準な契約（[§15.3.7](#s15-3-7)）に代入し、不足する証明を新たに発見することはない。

<a id="s8-1-3"></a>
#### 8.1.3. 型引数

型引数は、有効な完全な型、または定義のパラメーターに依存する型である。プリミティブ、構造体、列挙型、Unit、タプル、共通関数型、安全な借用、オブジェクト型、生ポインター、Neverが該当する（[§3.1.3](#s3-1-3)）。Function Item型やクロージャー型は、推論または既存の参照形式を通じて与える。Contract、グループ、Semantics、値は型引数ではない。Never式だけでは未束縛の型を推論できない。

明示的な引数リストはすべてのスロットに順番に引数を与え、末尾のコンマは省略できる。リスト全体を省略すると、その構文の推論を使う（[§10.8](#s10-8)）。部分的なリスト、`_`プレースホルダー、既定値、可変個数のスロット、Constジェネリクスはない。`<s/T, U>`には`<ref/i32 during a, string>`のように2つの引数を与える。

有効な引数があらゆる役割で有効とは限らない。基底型、関連型、レイアウト（[§8.15.3](#s8-15-3)）、Copy導出、ペイロードの規則は引き続き適用される。通常の記憶領域はOwnedを要求せずに引数の依存関係を保持する（[§15.4](#s15-4)）。静的記憶領域にはOwnedが必要である（[§11.8](#s11-8)）。

```kimi
func choose<T>(a: T, b: T) -> T => b@move

let y = choose<Option<string>>(.None, .Some("a"))
// let z = choose<i32, i32>(1, 2)   // Error: choose has one Type slot.
```

<a id="s8-2"></a>
### 8.2. 制約と要件式

<a id="s8-2-1"></a>
#### 8.2.1. 制約節

**制約節**`subject is requirement`は条件を記述する。宣言の**制約**に含まれるすべての節が成立しなければならず、本体はそれらが証明する内容を使える。主語には、要件の役割が許す範囲で完全な型、ペアバインダー、ペア対象、関連型の射影、`Self`を使い、宣言の種類ごとに制限がある（[§7.5](#s7-5)）。要件はContract（[§8.4](#s8-4)）などの能力、完全な型（[§8.2.3](#s8-2-3)）、またはペアバインダーの場合はSemanticsやSemanticsカテゴリ（[§3.3](#s3-3)）を指定する。

構造体や列挙型はメンバーより前に制約を宣言する。条件付き適合はメンバーの位置に記述する（[§8.8](#s8-8)）。型宣言内の`Self is C`は適合を宣言する（[§8.6.1](#s8-6-1)）。コンパイラー組込みContractと、唯一許される否定形の`Self`節は[§8.7.1](#s8-7-1)で扱う。

```kimi
contract Describe
    func describe(self: ref/Self) -> string

struct Labeled<T>
    T is Describe
    Self is Describe         // Labeled<T> conforms; nothing is derived from T is Describe.
    var value: T
    public func describe(self: ref/Self) -> string => self.value.describe()
```

<a id="s8-2-2"></a>
#### 8.2.2. 要件式

`subject is requirement`は、制約節と関連型の宣言・指定で使うコンパイル時の検査である。実行時の検査ではなく、`#if`や`#case`には使えない。式では実行時の`is`検査を使う（[§13.6.4](#s13-6-4)）。要件は`and`、`or`、`not`、括弧で組み合わせる（文法：`IsRequirement`、[§F.6](#sf-6)）。`is`の直後の`not`は右辺全体を否定する。

| 形式 | 意味 |
| --- | --- |
| `T is A and B` / `T is A or B` | `T`が両方／いずれかを満たす。 |
| `T is not A or B` | `T`がどちらも満たさない。 |
| `T is (not A) or B` | `T`が`A`を満たさないか、`B`を満たす。 |

要件検査は、`(T is A) and enabled`のような真偽値式の一部にはできない。

<a id="s8-2-3"></a>
#### 8.2.3. 型の同一性と同一性の前提

完全な型`U`を使う`X is U`は、`X`と`U`が同じ型であることを要求する。同一性は変換を意味しない。

**同一性の前提。** スコープ内で利用できる型の同一性の事実（完全な型を指定する前提`X is U`と、関連型の指定）は、型を同値類に分ける。これは、それらの事実を含み、射影を含む任意の型の内部で、型を同一の型に置き換えることについて閉じた最小の同値関係である（`X`と`U`が同じ型なら、`Option<X>`と`Option<U>`、`X.(C).Item`と`U.(C).Item`もそれぞれ同じ型となる）。正規化によって同じ同値類になる2つの型は同一であり、スコープ内のすべての型検査はそれらを1つの型として扱う。スコープは、制約の付いた宣言、または条件付き適合の適合スコープとブロックである（[§8.8.1](#s8-8-1)）。1つの同値類に完全に束縛された異なる2つの型が含まれる場合、または型が自身を真部分として含む必要がある場合、宣言はエラーとなる。同一性は分解しない。`Option<U> is Option<i32>`から`U is i32`は導けない。

```kimi
func same<T, U>(value: T) -> i32
    T is Option<U>
    T is Option<i32>
    return 0      // Option<U> and Option<i32> are one Type here; U and i32 are not.

func cyclic<T, U>(value: T) -> i32
    T is Option<U>
    U is Option<T>  // Error: T would contain itself.
    return 0
```

<a id="s8-3"></a>
### 8.3. 制約の証明

<a id="s8-3-1"></a>
#### 8.3.1. 命題と結果

ジェネリックの検査では、本節の規則だけで制約を証明する。SAT解法、定理証明、型の列挙、最適化器の事実は使わない。完全に確定した具体的な条件には真偽値の評価を使う。Callableの効果境界は命題ではない（[§8.11.6](#s8-11-6)）。

まず要件の優先順位を適用する。`T is (A and B)`は`(T is A) and (T is B)`を与える。命題は綴りではなく、束縛されたシンボルと正規化された完全な型で識別する。括弧は透過的で、`not not P`は`P`となる。それ以外の論理的な正規化はない。

| 結果 | 意味 |
| --- | --- |
| **Proven** | 規則によって命題が成立する。 |
| **Refuted** | 規則によって命題の否定が成立する。証明がないだけでは足りない。 |
| **Unknown** | 肯定も否定も成立していない。 |
| **Error** | 無効な名前、主語、宣言、または矛盾する証拠がある。 |

証拠は、宣言の入力制約、組込みの能力規則、検証済みの適合から得る。環境ディレクティブは証拠を与えない。入力制約は本体内で仮定し、各使用箇所で証明する。要求される制約はProvenの場合にのみ成功する。

<a id="s8-3-2"></a>
#### 8.3.2. 証明規則

| 規則 | 導出 |
| --- | --- |
| 完全一致する仮定 | 利用可能な`P`は`P`を証明し、利用可能な`not P`は`P`を反証する。 |
| 論理積 | 利用可能な`P and Q`は両方を与える。`P and Q`は両方がProvenならProven、どちらかがRefutedならRefutedとなる。 |
| 論理和 | `P or Q`はどちらかがProvenならProven（他方の証拠は得られない）、両方がRefutedならRefutedとなる。 |
| 否定 | `not P`はProvenとRefutedを入れ替える。 |
| 具体的な判定 | 具体的な型の同一性、Semantics、カテゴリ、組込み能力の検査と、後述の閉世界の規則。 |
| 許容集合 | ペアバインダーへの要件は[§8.3.4](#s8-3-4)で決定する。 |
| 検証済みの適合 | 検証済みの明示的、条件付き（[§8.8](#s8-8)）、または継承された（[§8.6.2](#s8-6-2)）適合は、実装が特定され、互換性がある（[§8.6.3](#s8-6-3)）時点で証拠を与える。効果境界は別に検査する（[§8.11.4](#s8-11-4)）。 |
| Contractの詳細化 | `T is C`は、`Self`を`T`に置き換えて、各祖先への適合と、`C`自身および継承された制約を与える。関連型宣言の要件もその1つである（`C`内の`associate X is R`は`T.(C).X is R`を与える）。 |
| 型と関連型の同一性 | 補完済みの関連型の束縛を代入した後、両辺が同じ同値類（[§8.2.3](#s8-2-3)）にあればProven、両辺が完全に束縛されていて異なればRefutedとなる。証明はメンバーの探索や型の選択を行わない。 |
| 閉じた含意 | `T is PrimitiveInteger`は`T is Position`を与える（[§8.7.4](#s8-7-4)）。 |
| その他 | Unknown。 |

Errorはどの結果にも優先する。それ以外では、Unknownのオペランドがあっても、Refutedのオペランドがあれば論理積はRefutedとなり、Provenのオペランドがあれば論理和はProvenとなる。`P or Q`と`not P`から`Q`は証明できない。場合分けや背理法はない。直接利用可能な`P`と`not P`、または問い合わせた命題の肯定と否定の両方を示す証拠は、矛盾する証拠（Error）である。

**閉世界。** 未束縛の型や未解決の前提条件が否定の結果になることはない。適合の不在がRefutedとなるのは、適用可能なすべての経路（宣言、継承された適合、条件付き適合、前提条件）が完了し、除外された場合だけである。探索の失敗は何も証明しない。プリミティブ型の適合は固定されている（組込み能力と、組込みの比較、書式化、Position、算術。[§13.3.3](#s13-3-3)）。したがって、解決済みのそれ以外のContractへの適合はRefutedとなる。

```kimi
func pick<T>(value: T) -> T
    T is Copy or Describe
    return value     // Error: Copy or Describe does not prove Copy, so value needs @move.
```

<a id="s8-3-3"></a>
#### 8.3.3. 再帰と確定性

**再帰。** 適合は自身を証拠として使えない。証明中の義務に再び到達しても証拠は得られず、余帰納は仮定しない。再帰的な宣言それ自体は有効であり、Ownedなどの構造的解析は[§15.3.5](#s15-3-5)に従う。Contract `C`が**再帰的**であるとは、`T is C`が（[§8.3.2](#s8-3-2)：`Self`を`T`に置き換えた`C`自身と継承された制約によって）、直接またはそうして与えられるContractの制約を通じて、`T`の射影から、任意の型引数を持つ`C`の宣言またはそれを詳細化するContractへの適合を与えることをいう。再帰的なContractは有効だが、具体的な型がそれに適合することは証明できない。証明探索の深さには実装定義の資源上限があり、それを超える証明は資源不足（[§23.1.1](#s23-1-1)）となる。UnknownやRefutedにはならない。

**確定性。** 判定は、その判定に依存しないプログラムのすべての事実を使う。実装が宣言を検査する順序によって結果が変わることはない。直接または他の宣言を通じて自身の判定に依存する事実は、証拠を与えない。証明が必要な箇所ではUnknownはエラー（未証明の要件）であり、受け入れたりRefutedとして扱ったりしない。ジェネリック定義は、検査時に宣言された前提の下で判定する。インスタンス化によって意味上の使用条件が加わることはなく、探索やオーバーロード選択も繰り返さない。インスタンス化に依存するのはレイアウトと表現の義務だけである（[§8.15.3](#s8-15-3)）。

```kimi
contract Tree
    associate Child is Tree      // Valid declaration.

struct Node
    Self is Tree                 // Error: proving Node is Tree needs Node is Tree.
    associate Tree.Child is Node
```

<a id="s8-3-4"></a>
#### 8.3.4. Semanticsの許容集合

ある地点でのSemanticsの前提は、ペアバインダー`s`の**許容集合**を与える。Semantics名は自身を、カテゴリはその要素を与える（[§3.3](#s3-3)）。`and`、`or`、`not`は9つのSemantics上の共通集合、和集合、補集合である。別々の節は共通集合を取り、前提がなければ9つすべてを許容する。`s is R`は、許容集合が`R`の要素に含まれればProven、互いに交わらなければRefuted、それ以外はUnknownとなり、空集合はErrorとなる。これによって、ペアの証拠、ペアに対する`Weak<S>`（[§3.2.2](#s3-2-2)）、ペアバインダーへの借用注釈（[§15.3](#s15-3)）を含む、すべてのSemantics要件を決定する。

```kimi
func peek<s/T>(value: s/T) -> i32
    s is object                         // Admitted set {obj, rc, arc}.
    let view: objref/T = value@objref   // Valid: pair evidence for T.
    return 0
```

<a id="s8-4"></a>
### 8.4. Contract

<a id="s8-4-1"></a>
#### 8.4.1. Contract宣言

**Contract**は、`contract`で宣言する宣言コンテナーであり、関数要件、プロパティ要件、関連型、制約を通じて能力を定義する。実装や記憶領域は持たない。`Self`は適合する型を表す。Contractは静的であり、実行時Contractビューは[§8.12](#s8-12)で扱う。Contractは型パラメーターを宣言できる（Originパラメーターや長さパラメーターは宣言できない）。また、外側の環境を継承する（[§6.1.3](#s6-1-3)）。`Indexable<isize>`と`Indexable<string>`は、束縛された別々のContract参照である（[§8.6.1](#s8-6-1)）。

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

<a id="s8-4-2"></a>
#### 8.4.2. 関数要件

関数要件は、名前、省略可能なジェネリックパラメーター、暗黙のシグネチャOrigin（[§15.3.4](#s15-3-4)）、省略可能なタスクスロット（[§24.2.1](#s24-2-1)）、型付きパラメーター（レシーバの短縮形：[§7.3](#s7-3)）、省略可能な結果型を持つ。結果を省略するとUnitとなる。レシーバがあればインスタンス関数、なければ型関数である。ヘッダーの後にはインデントした領域を任意に置ける。この領域には、その要件自身の制約節、Origin関係（[§15.3.3](#s15-3-3)）、効果節（[§8.11.1](#s8-11-1)）を記述し、主語の根はそのジェネリックパラメーターとする。これらは互換性（[§8.6.3](#s8-6-3)）の前提であり、実装に追加するものではない。

- 1つのContract内でレシーバを持つ同名の要件は、継承したものも含めて、1つのレシーバ形状を共有する（[§7.3](#s7-3)）。すべての同名の要件は1つのタスクスロット形状を共有する（[§24.2.3](#s24-2-3)）。
- 要件には`!`境界（[§7.2](#s7-2)）を使えるが、既定値は使えない。要件を通じた呼出しでは、その要件の名前省略の許可に従って、すべての通常の引数を与える。
- 要件にアクセス修飾子は付けない（[§9.4.6](#s9-4-6)）。プロパティ要件には`has`を使う（[§11.9](#s11-9)）。

```kimi
contract Factory
    func create<T>(value: T) -> Self
        T is Copy
```

<a id="s8-4-3"></a>
#### 8.4.3. 詳細化

`contract C: A, B`は、`contract UniqIndexable<Key>: Indexable<Key>`のように束縛されたContract参照（[§9.7.1](#s9-7-1)）で与えられた各親を詳細化し、その要件、関連型、制約を継承する。親の順序による優先順位はなく、循環はエラーとなる。Contract内でユーザー定義Contractに対する`Self is C`を書いても、詳細化の宣言にはならない（組込みの親：[§8.7.1](#s8-7-1)）。子への適合はすべての祖先への適合を伴い、`Self`は最終的な適合型となる。子は追加できるが、削除や弱化はできない。1つの祖先宣言への経路で束縛が等しければ、要件は1つとなる（[§8.9.2](#s8-9-2)）。独立した同名の宣言は別々のままであり、1つの実装が複数を満たせる。

詳細化の要件が、別々の実装として共存できず（[§8.6.3](#s8-6-3)、[§9.1](#s9-1)）、1つの実装でも満たせない場合、その詳細化はエラーとなる。ラベルだけでオーバーロードを区別することはなく、結果型が異なるだけでも衝突の証明にはならない。結果の互換性（[§10.7](#s10-7)）が適用されるためである。Unknownは衝突を証明しない。

<a id="s8-5"></a>
### 8.5. 関連型

<a id="s8-5-1"></a>
#### 8.5.1. 宣言と指定

関連型は完全な型を表す。その束縛、射影、展開によってOriginが再束縛されたり、`static`が与えられたりすることはない。`associate`はContract内で関連型を宣言し、適合型では必ず`is`を使って関連型を指定する。

| 形式 | 意味 |
| --- | --- |
| `associate Element` | Contract内で宣言する。適合型ではエラーとなる（`is`を使う）。 |
| `associate Element is R` | Contract内では要件`R`を付けて宣言する。適合型では、その名前で一意に決まる関連型を指定または制約する。 |
| `associate C.Element is R` | `C`が宣言した関連型を指定または制約する。 |
| `associate LentItem(step) ...` | Originパラメーターを持つ（[§8.5.4](#s8-5-4)）。 |

適合型の`associate C.X is R`は、`R`の論理積を分解して（[§8.3.2](#s8-3-2)）型の同一性を表す原子命題が得られる場合に`X`を指定する。それ以外では、`X`の指定を別の箇所または推論（[§8.5.5](#s8-5-5)）に委ねる。どちらの場合も、補完された型は`R`全体を満たさなければならない。同一性の原子命題が複数ある場合は[§8.2.3](#s8-2-3)に従う。適合の各関連型は、指定、同一性の制約、継承された束縛、推論によって一意に決まり、未決定のものが残るとエラーになる。

**修飾。** `associate C.Element`の`C`は、型引数を伴う直接の適合先または祖先である。修飾しない形式が有効なのは、適合と詳細化の全体で、その名前の関連型が1つだけ利用できる場合である（同じ束縛で1つの宣言に至る経路は1つと数える。[§8.9.2](#s8-9-2)）。`C<i32>.Element`と`C<i64>.Element`が両方利用できる場合は修飾が必要になる。

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

<a id="s8-5-2"></a>
#### 8.5.2. 射影

`T.(C).Element`は、束縛されたContract参照`C`への`T`の適合に属する関連型である。`T.(C).LentItem(a)`はOrigin引数を適用する。Contractの外で`T.Element`が有効なのは、利用可能な制約の下で関連型が一意に決まる場合だけである。Contractの内部では短い名前は自身または継承した関連型を意味する。`C`はContract名の探索で見つけ、`T.C.Element`は通常のパスとなる。適合の証拠が必要であり、名前の探索でその証拠を得ることはない。基部にはCore、パラメーター、`Self`、別の射影を使える。射影はSemanticsを保持する型である。関数の先頭の制約は、そのシグネチャの射影に適用される（[§7.5](#s7-5)）。異なる解釈が複数成功すると曖昧になる。

```kimi
func readOne<T>(source: ref/T) -> T.(Source).Element
    T is Source
    return source.read()

func readInt<T>(source: ref/T) -> i32
    T is Source
    T.(Source).Element is i32
    return source.read()
```

<a id="s8-5-3"></a>
#### 8.5.3. 継承した関連型への制約

子Contractは、祖先`C`に対する`Self.(C).Element`、または自身と継承した宣言の中で一意な未修飾名を使って、継承した関連型を制約できる。新しい型は作られない。`associate Parent.LentItem(a) is E`によって継承した型族を詳細化することもでき、これは親の定義域のすべての`a`について成立する。`Iterator`の`associate LendingIterator.LentItem(step) is Item`がその例である（[§22.2.1](#s22-2-1)）。Contractは`Self`のさらに深い射影も制約できる。例えば、`associate X is B`を宣言するContract `A`内で`Self.(A).X.(B).Y is A`と書ける（これによって`A`は再帰的となる。[§8.3.3](#s8-3-3)）。各段階には[§8.5.2](#s8-5-2)の適合の証拠が必要であり、その制約は適合するSelfへの条件として実装要件（[§6.1.4](#s6-1-4)）となり、`T is A`がそれを与える（[§8.3.2](#s8-3-2)）。Loanの独立性は別に検査する（[§15.6.3](#s15-6-3)）。

```kimi
contract OrderedSource: Source
    Self.(Source).Element is Equatable

contract IntSource: Source
    Self.(Source).Element is i32    // Conformances need not repeat the binding.
```

<a id="s8-5-4"></a>
#### 8.5.4. Originパラメーター

関連型は、括弧内の単純な名前によって**Originパラメーター**を宣言できる。そのスコープは、宣言の右辺、形成型、付随する節であり、同列の要件には及ばない。メソッドのシグネチャにある同じ綴りのOriginは別のバインダーであり、`Self.LentItem(step)`のように型族を適用する。名前は[§15.3.4](#s15-3-4)に従う。

**適用。** 型の文脈では、`LentItem(a)`はOrigin原子を位置に従って代入し、通常の完全な型を表す。共通部分は`LentItem((a and b))`のように括弧で囲む。個数は一致しなければならない。適合型での指定にも同じ個数のパラメーターを記述する。パラメーター名は要件のパラメーターに位置で対応し、名前を変えてもContractは変わらない。未適用の型族、`_`、引数の省略はエラーとなり、未適用の型族は型引数にもエイリアスにもできない。適用となるのは、型の文脈で名前付きの型の直後に置かれた括弧付きリストだけであり、その型がOriginパラメーターを持つ関連型でなければエラーとなる。波括弧は束縛集合を指定する（[§15.3.1](#s15-3-1)）。適用後の型には通常の変性、短縮、再借用の検査を行う。

**形成条件。** Contractと宣言に付随する`origin`関係が定義域を定める。固定の右辺はその整形式性を公開する。`associate View(a) is ref/E during a`では、OwnedOrigins(E)内のOriginは`a`より長生きする（[§15.6.1](#s15-6-1)）。能力要件は型を固定しないが、末尾に**形成型**`for Type`を付けることができ、これは`Type`の形成条件を公開する。例えば`associate IteratorType(source) is LendingIterator for ref/Self during source`と書く。`Type`は外側の制約から証明できなければならない。`origin source outlives a`などの節は[§15.3.3](#s15-3-3)の順序で処理する。等式は右辺の未束縛Originを束縛し、残りが定義域を形成する。右辺も条件もなければ、Contractが許すすべてのOriginを許容する。

使用箇所では、代入後の条件を証明する。ペア層は、そのバインダーが外側のOriginを持つ場合に限って、外側のOriginの条件を公開する（[§8.1.2](#s8-1-2)）。実装は右辺を束縛した後で残りの条件を証明し、呼出し条件を追加することはできない。OriginパラメーターはLoanを作らず、実行時表現を持たない。

```kimi
contract Lender<E>
    associate View(a) is ref/E during a
    associate LentItem(step) for uniq/Self during step
    func lend(self: uniq/Self during step) -> Self.LentItem(step)
    // func bad(self: ref/Self) -> Self.LentItem   // Error: the family is unapplied.
```

<a id="s8-5-5"></a>
#### 8.5.5. 推論

構造体や列挙型の適合では、Originパラメーターのない関連型を省略できる。推論は実装に宣言された結果からそれを補完する。推論はインスタンス化時には行わず、推論された束縛は、等しい明示的な束縛と同じ契約になる。

**固定された前提。** 最初に各適合経路の前提を固定する。これには、その経路の指定、そのContractと祖先の同一性の制約、継承された束縛、それらの帰結が含まれる（[§8.3](#s8-3)）。経路全体を通じて有効な前提だけを数え、1つの要件や実装の前提から束縛を決めることはない。関連型の同一性は、適合型、関連宣言、束縛された定義元Contractで決まる。名前が等しいだけでは同一性は成立せず、ある経路に条件付きで成立する同一性が、別の経路でも無条件に成立するわけではない。固定された同一性は先へ代入する。Iteratorの`LentItem(step) is Item`は`Item`を露出させる。

現在の定義は、1つの構造体または列挙型とその適合からなり、入れ子の定義は別とする。現在の定義について推論された値が、その固定された前提に加わることはない。別の定義について推論された値は、その定義の残りの値を待たず、決定した時点で使える。現在の定義で推論した値に、直接または別の定義の値を通じて依存する、現在の定義の値の証拠は、証拠として使えない。

**特定。** 各要件の実装は、固定された前提の下で[§8.6.3](#s8-6-3)のキーにより一度だけ特定し、結果、Origin、制約、効果によって絞り込まない。候補のいずれかのキー比較が、その定義の未決定の関連型に依存する場合、結果は**保留**となる。それ以外は**一意**、**欠落**、**曖昧**のいずれかとなる。一意に一致したものだけが証拠を与える。補完後は、保留だった特定だけを互換性のために再実行する。欠落と曖昧の結果は確定している。`index`と`indexUniq`を含む関数の結果は証拠を与えるが、プロパティやアクセサーの結果は与えない。実装の結果は、その宣言された結果、または省略時にはUnitである。本体、呼出し箇所、インスタンス化は何も与えない。

**抽出。** 正規化された結果を、未決定の関連型に至る構造に沿ってだけたどる。未決定の位置は実装の完全な型を受け取る（関数型全体も含む）。同じ名目的な構成とタプルでは、対応する引数をたどる。固定長配列では、長さがすでに等しい場合にだけ要素をたどる。具体的なSemantics層では、同じ層をたどる。関数型、未解決の射影、未解決のSemantics適用の内部には入らない。抽出した型はOriginを含めてすべてを保持し、その中のすべてのバインダーが適合スコープで有効でなければならない。メソッドのジェネリックパラメーター、呼出しごとのOrigin、省略されたレシーバOriginが含まれると使えない。結果の他の部分には通常の互換性を適用する。Originを解くことも、変換、Copy判定、能力を持つ型の探索も行わない。

**補完。** 別の経路の証拠は、利用先の前提から利用元の前提が証明できる場合に使える。1つの同一性について利用できるすべての証拠は、1つの完全な型を示さなければならず、その型が束縛となる。Originの違いを含め、型が異なればエラーとなり、証拠がなければ明示的な指定が必要となる。その後は通常の検査をすべて適用する（[§8.6.3](#s8-6-3)）。互換性の失敗によって選択をやり直すことはない。明示的な束縛には通常の結果の互換性が適用される（`longer outlives shorter`の下では、`ref/T during longer`は束縛`ref/T during shorter`を満たす）が、推論はそのような型を見つけない。継承された束縛を再推論することはない（[§8.6.2](#s8-6-2)）。受入れが推論の作業量の上限に依存することはなく、上限に達すれば資源不足となる（[§23.1.1](#s23-1-1)）。

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

<a id="s8-6"></a>
### 8.6. 適合

<a id="s8-6-1"></a>
#### 8.6.1. 適合宣言と同一性

型宣言内の`Self is C`は、`C`への適合を宣言する（コンパイラー組込みContract：[§8.7.1](#s8-7-1)）。同名のメンバーがあるだけでは適合しない。

**適合の同一性**は、完全な適合型と、束縛されたContract参照のペアである。後者はContract宣言に、その宣言自身の型引数と外側の宣言の正規化された束縛を合わせたものである。`Self is Multipliable<f64>`と`Self is Multipliable<Vec2>`は2つの適合となる。関連型の束縛は適合を区別しない。明示的な適合と詳細化から導かれる適合は、束縛された参照が同じなら1つの適合となり、冗長な明示的適合も許される。Contractの同一性（[§6.1.2](#s6-1-2)）は宣言だけを識別する。1つの型で等しい束縛済み参照は、条件にかかわらず重複となる。同じContract宣言への他の直接適合は[§8.9.1](#s8-9-1)に従う。

ジェネリック型の無条件の適合は、その制約が許すすべての束縛で成立しなければならない（条件付きの場合：[§8.8](#s8-8)）。インスタンス化で適合を検証することはない。祖先への適合と対応付けも検証する。適合は、組込みの導出（[§8.7.1](#s8-7-1)）とプロパティの適合証拠ブリッジ（[§11.9.2](#s11-9-2)）を除いて、実装を生成しない。要件からメンバーへの対応付けと関連型の束縛は適合によって固定され、Contractを通じた呼出しはそれらを使う。呼出し側やインスタンス化後に探索することはない。外部登録、置換する適合、既定の実装はない。

```kimi
contract C

struct Twice<T>
    Self is C
    Self is C when T is Copy        // Error: the same bound reference twice.
```

<a id="s8-6-2"></a>
#### 8.6.2. 継承された適合

`B`から`C`への検証済みの適合を`D : B`が継承するのは、要件側の`Self`を`D`に置き換えても、その対応付けがすべての要件を満たす場合だけである。`A`で宣言されたメンバーは、`A -> B -> D`を経由しても、基底経路の代入を適用した上で、実装側の`Self`として`A`を保持する。基底部分オブジェクトへの射影（[§9.6.1](#s9-6-1)）を使えるのは、条件を満たす借用レシーバだけである。他のパラメーターに基底変換は加わらず、所有レシーバにスライシングは加わらない。射影された呼出しにはObjectCallCompatibleがProvenであることが必要になる（[§12.7](#s12-7)）。

| 要件の形状 | `D`による継承 |
| --- | --- |
| 借用レシーバ内だけに`Self`がある | 他の検査に成功すれば可能 |
| `other: ref/Self` | 不可。`ref/A`は`ref/D`と一致しない |
| `func empty() -> Self` | 不可 |
| 所有する`Self`レシーバ | 不可 |
| 同じ型に正規化される`Self.Element` | 可能 |

ある経路が失敗しても`D`は無効にならない。`D`の適合がRefutedとなるのは、明示的、条件付き、詳細化によるすべての経路の失敗が証明された場合だけであり、成功する経路は一致しなければならない（[§8.9.2](#s8-9-2)）。`D`の明示的な適合は通常の探索を使い、同名の宣言が継承された対応付けを置き換えることはない。

```kimi
contract Same
    func same(self: ref/Self, other: ref/Self) -> bool

open struct Shape
    Self is Same
    public func same(self: ref/Self, other: ref/Self) -> bool => true

struct Circle : Shape
    Self is Same    // Error: the inherited same takes ref/Shape, not ref/Circle.
```

<a id="s8-6-3"></a>
#### 8.6.3. 実装の照合

宣言の重複（[§9.1](#s9-1)）と重なりの検査（[§7.4](#s7-4)、[§8.9.1](#s8-9-1)）は、関連型の補完後に、補完済みのヘッダーを比較する。関連型の推論はこれらの検査に依存しないため、明示的な束縛も推論による束縛も同じ結果を与える。

`Self`、型の引数、利用可能な関連型を代入した後、関数要件の実装は次の要素で特定する。完全に一致する名前、関数の種類、ジェネリックパラメーターの個数・種類・順序、タスクスロットの有無（[§24.2.4](#s24-2-4)）、通常のパラメーターの個数・順序・外部ラベル、レシーバの有無と構造（[§8.6.2](#s8-6-2)の対応による）、正規化されたパラメーター型の構造である。型の構造にはSemantics、引数、補完済みの関連型が含まれる。Originは除くが、互換性のために保持する。特定では反変性やAdaptationを使わず、結果によって絞り込まず、優先規則、Origin、制約、効果による順位付けも行わない。候補がなければ実装の欠落、複数あれば曖昧となる。`Callable`は代わりに[§10.7](#s10-7)を使う。

1つの実装は、各要件について特定とすべての互換性義務に合格すれば、同じContractの異なる束縛の要件も含め、複数の要件を満たせる。別々の実装宣言は通常の共存規則（[§9.1](#s9-1)）に従わなければならず、結果型だけで区別することはない。適合の衝突と一致には引き続き[§8.9](#s8-9)を適用し、別々の要件は別々の呼出し候補のままとなる（[§8.6.4](#s8-6-4)）。

| 観点 | 互換性の義務 |
| --- | --- |
| 制約 | 型、適合、要件の前提から、実装の制約と条件を証明する。 |
| 入力 | 要件が許容するすべての入力が引き続き有効である。呼出し内の借用を`static`へ狭めない。 |
| 結果型 | 結果の互換性（[§10.7](#s10-7)）。 |
| 結果のOrigin | 少なくとも要求された保証を持つ。 |
| アクセス | 適合の定義域全体で使用できる（[§9.4.6](#s9-4-6)）。 |
| 文脈と効果 | より強い呼出し文脈を要求しない。安全な要件がunsafeを必要とすることはない。適用される境界の外に効果がない（[§8.11](#s8-11)）。 |

境界は各実装の効果要約（[§15.6.4](#s15-6-4)）に対して検査し、破棄は[§16.3.3](#s16-3-3)に従う。要約で分類できない効果は衝突であり、空の効果ではない。境界の検査時点は[§8.11.4](#s8-11-4)に従う。Originの契約には[§15.3.7](#s15-3-7)を使う。互換性は変換、Copy、借用、型消去を挿入しない。失敗は適合のエラーであり、別の実装を試す理由にはならない。プロパティとアクセサーの適合証拠は[§11.9](#s11-9)と[§12.7](#s12-7)に従う。

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

<a id="s8-6-4"></a>
#### 8.6.4. 要件を通じた呼出し

`T is C`の下で、ジェネリックコードは`C`とその祖先の要件および関連型を使える。型関数は型を通じて呼び出す。`func empty() -> Self`を宣言する`Empty`に対して`T is Empty`がある場合は、`T.empty()`と書く。期待される結果から型を推論することはないため、`Empty.empty()`は無効である。`T is Comparable`の下での`T.compare`は、`Self`を束縛した要件のFunction Itemである（[§10.5](#s10-5)）。別々の要件は別々の呼出し候補であり、複数に一致する呼出しは曖昧となる。修飾した要件呼出しはない。1つのContractで制約された補助関数を通じて呼び出す。

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

<a id="s8-7"></a>
### 8.7. コンパイラー組込みContractと閉じたContract

<a id="s8-7-1"></a>
#### 8.7.1. コンパイラー組込みContract

Copy、Owned、Callable、Sealed、ObjectPayload、PrimitiveInteger、Transferableは、Kimiが宣言する**コンパイラー組込みContract**である（[§22.1](#s22-1)）。それぞれに定義された効果だけを持ち、同じ名前やメンバーを持つユーザー定義Contractにその効果は与えられない。構造体または列挙型の宣言では次のようになる。

| X | `Self is X`（任意で`when P`を付ける） |
| --- | --- |
| Copy | Copyの導出を要求する（[§3.5.2](#s3-5-2)）。 |
| Owned | 検査される表明。その制約（と`P`）が許容するすべての束縛で、型がOwned（[§15.2.3](#s15-2-3)）でなければならない。能力を与えるものではない。 |
| Sealed, ObjectPayload, PrimitiveInteger, Callable, Transferable | エラー。これらは型の構造だけで決まる。 |

構造体または列挙型の宣言で許される否定形の`Self`節は、`Self is not ObjectPayload`だけである。これは[§8.7.3](#s8-7-3)のオプトアウトである。Contract内の`Self is X`（および`Self is not ObjectPayload`）は、すべての適合型がX自身の規則によってXを満たすことを求める要件となる。Xを詳細化の親に挙げること（`contract C: Copy`）は、本体内の`Self is X`と同じ意味を持つ。

`Utf8Format`、`BufferWriter`、`Equatable`、`Comparable`、反復関連およびIndexableのContractの同一性は、[§22.1](#s22-1)で固定される。これらは宣言された境界を持つ通常のContractである。算術Contractは、組込みの適合証拠を持つ通常の宣言である（[§13.3.3](#s13-3-3)）。適合が証明するのは記述された要件だけであり、等価性の対称性など、文書化された法則を強制するものではない。初期化、Loan、表現についても何も証明しない。

```kimi
struct Pair<T>
    Self is Copy when T is Copy     // Derived Copy.
    Self is Owned when T is Owned   // Checked; grants nothing.
    var first: T
    var second: T

struct Plain
    Self is Sealed                  // Error: Sealed is structural.
```

<a id="s8-7-2"></a>
#### 8.7.2. Sealed

`Kimi.Sealed`が正規化された型について成立するのは、外側のSemanticsが`owner`で、Coreが有効であり、Neverでもopen構造体でもない場合に限る。スカラー、`string`、Unit、列挙型、タプル、固定長配列、コレクション、Function Item、具体的なクロージャー、共通関数型はSealedである。検査するのは外側のCoreだけであり、openでない`Cell<T>`は`T`がopenでもSealedとなる。SealedはCopy、Owned、排他的アクセス、オブジェクト対象のいずれも意味せず、`T is not Sealed`は`owner`もopenなCoreも証明しない。

<a id="s8-7-3"></a>
#### 8.7.3. ObjectPayload

`Kimi.ObjectPayload`が成立するのは、外側のSemanticsが`owner`で、Coreが有効かつNeverではなく、Coreの宣言もどの基底もオプトアウトしていない場合に限る。検査するのは外側のCoreだけであり、`Box`がオプトアウトしなければ`Box<Parser>`は該当する。これを満たすすべてのCoreは、具体的なオブジェクトペイロードになれる（[§3.3.1](#s3-3-1)）。オブジェクトの所有にヒープ割当ては必要ない（[§3.3](#s3-3)）。

**オプトアウト。** 構造体または列挙型の先頭の制約領域に`Self is not ObjectPayload`を書くとオプトアウトする。これは無条件で、単独の節として一度だけ記述し、`Kimi.ObjectPayload`に解決されなければならない。派生構造体はオプトアウトを継承し、この能力を復活させるものはない。

**証明。** 外側のSemantics、Core、オプトアウトが決まっていれば、未束縛の引数があっても直接判定する。`Box<T>`はProven、オプトアウトした`Parser<T>`と`ref/T`はRefutedとなる。ペア自身の`s/T`では、外側のSemanticsを`s`の許容集合から判定する（[§8.3.4](#s8-3-4)）。それ以外（型パラメーター、ペア対象、未解決の射影）で使える証拠は、前提`T is ObjectPayload`またはその詳細化だけである。`T is ObjectPayload`がProvenなら、`T`は`owner` Semanticsを持つ完全な値の型となる。Copy、Owned、Sealed、Transferableのいずれも意味しない。

**オブジェクト対象。** `X`がオブジェクト対象となるのは、`X is ObjectPayload`がProvenである場合、`X`が実行時ContractのView Targetである場合（[§8.12](#s8-12)）、または`X`がペアの証拠を持つペア対象である場合（[§8.1.1](#s8-1-1)）である。`X`に対するすべてのオブジェクト形式（`obj`、`rc`、`arc`、`objref`、`objuniq`。`Weak<rc/X>`、キャスト、実行時`is`検査も含む）は、オブジェクト対象を必要とする。値からオブジェクトを作る操作（[§13.6.2](#s13-6-2)）にはObjectPayload自体が必要であり、既存のハンドルに対する操作で再証明するものはない。オプトアウトしたCoreは自身の宣言内でもオブジェクト対象にはなれず、`Self is ObjectPayload`を証明するContractには適合できない。値、借用、他の型の内部への格納（`obj/Box<X>`）、キャプチャーには影響しない。

**Contract。** 要件のシグネチャが`Self`に対してオブジェクト形式を形成するContractでは、宣言または継承によって`Self is ObjectPayload`がProvenでなければならない。その場合、`T is C`は`T is ObjectPayload`を与える。肯定形と否定形が両方ある場合は、矛盾する証拠となる。

```kimi
struct Parser
    Self is not ObjectPayload
    public var pos: i32 = 0

func boxed<T>(value: T) -> obj/T
    T is ObjectPayload
    return Kimi.Intrinsics.makeObj(value@move)

// let o = boxed(Parser.init())   // Error: Parser is not ObjectPayload.
```

<a id="s8-7-4"></a>
#### 8.7.4. PrimitiveInteger

`Kimi.PrimitiveInteger`はメンバーを持たない。12種類の整数型（`i8`から`i128`、`u8`から`u128`、`isize`、`usize`）に対する`owner` Semanticsの場合に限って成立する。証明された`T is PrimitiveInteger`は次を与える。

- スカラー、Copy、Owned、`Position`（[§4.6.1](#s4-6-1)）、組込みのEquatable、Comparable、Utf8Formatへの適合。`T`への参照は値として読み取る（[§3.5.3](#s3-5-3)）。
- [§13.3.3](#s13-3-3)と[§13.3.4](#s13-3-4)の算術Contractと演算子。単項`-`は使えない。
- PrimitiveInteger型間の検査付き`value@U`、`value@Wrapping<T>`とその逆変換、`value@wrap<U>`（[§13.5.4](#s13-5-4)）。
- 12種類すべての型に合う、型なしリテラルの0から127（[§12.3](#s12-3)）。
- これらの能力と単項`-`を持つ`Wrapping<T>`（[§3.1.1](#s3-1-1)）。

本体はこれらの能力に対して一度検査し、各インスタンス化ではその具体的な型の操作（例えばオーバーフロー検査）を行う。証明には場合分けがないため、`T is i8 or u8 or ...`はこれらを何も与えない。

```kimi
func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values
        total += value     // -value would be an error: unary - needs a signed Type.
    return total

let small = sum(1..5@u8)   // 10
```

<a id="s8-7-5"></a>
#### 8.7.5. 閉じたContract

`Kimi.Position`と`Kimi.PositionRange`は**閉じている**。適合する型は[§22.1](#s22-1)で固定される。`Position`を満たすのは12種類の整数型、`FromEnd<T>`、`Start`、`End`であり、`PositionRange`を満たすのは`Range<S, E>`、`ClosedRange<S, E>`、`ResolvedRange`である（[§4.6.1](#s4-6-1)、[§4.6.3](#s4-6-3)）。他の型でこれらへの適合を宣言するとエラーになる。整数にメンバーは加わらず、すべての実装は自身の境界と長さだけを読み取り、他の効果を持たない。

```kimi
struct Offset
    Self is Position    // Error: Position is closed.
```

<a id="s8-8"></a>
### 8.8. 条件付き適合

<a id="s8-8-1"></a>
#### 8.8.1. 条件と実装ブロック

ジェネリックな構造体や列挙型は、メンバーの位置に`Self is C when P`を宣言できる。型の制約`D`はすべての使用を規定し、`P`はこの適合だけを規定する。`P`はすべてが成立すべき節のコンマ区切りリストであり、主語は外側のパラメーター、ペアバインダーとペア対象、射影の中から取り、肯定形の要件を`and`で結ぶ（文法：`ConformanceCondition`、[§F.6](#sf-6)）。`or`、`not`、値の検査はエラーとなる。外側のパラメーター間の同一性を表す原子命題は、適合スコープ内でそれらを1つの型にする（[§8.2.3](#s8-2-3)）。

インデントした**実装ブロック**を任意に置き、その型の名前空間にメンバーを宣言できる。関数、その型の種類が許す計算メンバー、関連型の指定は書けるが、フィールド、ケース、コンストラクター、`drop`、入れ子の型、適合は書けない。レイアウトは変わらない。`P`はブロック内の各メンバーだけの前提条件となる。既存のメンバーや組込みの導出が`C`を満たす場合、ブロックは省略できる。外部登録、拡張宣言、部分特殊化はない。

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

<a id="s8-8-2"></a>
#### 8.8.2. 検証と使用

定義時には、`P`を満たす型がなくても、許容されるすべての束縛について`D`と`P`の下で`C`を検証する。実装は[§8.6.3](#s8-6-3)によって特定し、補完には経路全体で固定された前提を使う（[§8.5.5](#s8-5-5)）。各要件は、`D`、`P`、その要件の前提からなる**適合スコープ**内で、両側をそこで正規化して検査する。`I is Iterator`の下では、`Option<I.(LendingIterator).LentItem(step)>`は`Option<I.Item>`となる。ブロック外の実装は自身のシグネチャを保持する。

使用箇所では、代入された引数について`P`を証明すると、検証済みの適合を使えるようになる。実装を選び直すことはない。ブロックのメンバー、または自身の節を`P`に加えた条件付きメンバー（[§7.5](#s7-5)）は、探索と推論の後で、`P`の判定に従ってのみ適用可能となる。関数参照と計算アクセスにも同じ規則を適用する。

| `P` | メンバーの適用可能性 | 適合の使用 |
| --- | --- | --- |
| Proven | 充足 | 経路を利用できる |
| Refuted | 適用不可 | この経路からの適合はない |
| Unknown | 未証明（[§10.1](#s10-1)） | 適合も不在も示さない |
| Error | Error | Error |

条件が確定済みの探索層を変えることはない。適用不可のメンバーによって探索を再開することも、条件の強さによって候補を順位付けすることも、後のLoan検査の失敗によって選択をやり直すこともない。適用可能性がUnknownである場合の選択への影響は、[§10.1](#s10-1)の手順5が定める。

<a id="s8-8-3"></a>
#### 8.8.3. 親Contract

`Child: Parent`では、Parentの証拠は、直接のParentへの適合またはChildへの適合から得る。子の経路に必要なのは子の条件だけである。そのため、子の経路が使う親の各実装は、子の条件の下で適用可能でなければならない。親の条件付きブロック内のメンバーには親の条件が付くため、その役割を果たせない。ブロックの外で、無条件に、または子の条件から証明できる条件を付けて実装する。経路は一致しなければならない（[§8.9.2](#s8-9-2)）。シグネチャが等しく、条件だけが異なるブロックメンバーは重複となる。

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

<a id="s8-9"></a>
### 8.9. 衝突と証明経路

適合は[§8.6.1](#s8-6-1)のとおりに識別する。要件や関連型は、その定義宣言と、定義元Contractの束縛によって識別する。束縛は詳細化に沿って代入され、継承した入力条件は子の公開入力となる。

<a id="s8-9-1"></a>
#### 8.9.1. 直接適合の衝突

直接適合と合流する経路には、1つの衝突検査を使う。

1. Contract宣言ごとにまとめる。異なる宣言は衝突しない。同一性の前提とOriginの規則で正規化する。
2. パラメーターと固定構造（名目的な型、タプル、関数型、確定したSemanticsの適用）を、出現検査を伴って単一化する。局所的な関数バインダーは、互いに異なる名前に変更する。
3. 固定構造が一致しなければ非衝突が証明される。それ以外では、その代入が衝突条件となる。
4. 残る未解決の項は残余条件となる。これには、未解決の関連型射影、ペアバインダーの適用、定数でない長さ式、Originの共通部分と順序関係、その他の自由でない項が含まれる。未解決の残余条件があれば衝突の可能性がある。`A.Item`と`B.Item`は等しいかもしれない。

直接適合のすべてのペアについて、型の制約の下で非衝突を証明しなければならない。`when`条件が相互排他的でも免除されない。

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

<a id="s8-9-2"></a>
#### 8.9.2. 経路の合流と循環

同じ正規化された束縛で同じ祖先宣言に至る詳細化経路は、1つの要件と1つの関連型を与える。それらの関連型の束縛、Origin条件、実装の対応付けは一致しなければならず、一致を証明できなければ定義はエラーとなる。束縛の異なる経路と、同じ名前を持つ独立した宣言は、別々の要件のままとなる。

直接、詳細化、継承による適合のその他の組合せでは、各経路の条件を衝突条件に加える。合流する可能性のある経路は一致しなければならない。`Family<A>.C`と`Family<B>.C`で、同じ関連型を`i32`と`string`に指定することはできない。詳細化経路がContract宣言を再訪すると、引数が異なっていても（`Outer<T>.C`から`Outer<List<T>>.C`など）エラーとなる。別々の経路上にある、1つのContractの異なるインスタンス化（例えば`contract C: A<i32>, A<i64>`）は循環ではない。

<a id="s8-10"></a>
### 8.10. Callable制約

`F is Callable<r, S>`は、レシーバのアクセス`r`（`ref`、`uniq`、`owner`）とシグネチャ`S`による呼出しを要求する。`S`は`(A1, ..., An) -> R`または`(task; A1, ..., An) -> R`である（[§24.2](#s24-2)）。`Callable<S>`は`Callable<ref, S>`を意味する。互換性のあるシグネチャ（[§10.7](#s10-7)）を持つFunction Item、具体的なクロージャー、共通関数型が該当する。`call`メンバーを探索することはなく、`F`がCopyやOwnedである必要もない。結果は入力のOriginを保持できるが、隠された呼出し可能値のレシーバを借用することはできない。

| `r` | 取得するレシーバ | 呼出し先に許される要件 |
| --- | --- | --- |
| `ref` | `ref/F` | Shared |
| `uniq` | `uniq/F` | SharedまたはExclusive |
| `owner` | 値としての`F` | Shared、Exclusive、Consuming |

**呼出しごとのOrigin。** `S`の直接の借用パラメーターで省略されたOriginは、それぞれ呼出しごとに独立して束縛され、すべての呼出し時Originで適合が成立しなければならない。入れ子とキャプチャーのOriginは固定されたままとなる。`S`には`during`を書かない（[§15.3.1](#s15-3-1)）。`S`内に現れる集約型は、それを含む宣言が所有する束縛集合を指定できる。その入力スロットは、完全な型または外側のOrigin式との等式によって固定しなければならない。省略された結果のOriginは、呼出しごとの入力Originから得る（[§15.4](#s15-4)）。入力が複数なら、その共通部分となる。排他的アクセスを必要とする結果は、対応する排他的Loanを保持する。

**呼出し。** `ref`は共有アクセスを通じて呼び出す。`uniq`は排他的アクセスを保持する（呼出し先はレシーバ式である。[§7.3](#s7-3)）。`owner`はCopyまたは`@move`で取得し、SharedまたはExclusiveの本体にはその値を借用し、Consumingの本体には移転する（取得した一時値は、`let`束縛からコピーした場合でも排他的にアクセスできる）。呼出し後には残りを破棄する。インスタンス化によってこれが借用に変わることはない。

**複数の制約。** `F`に対するCallable制約の、正規化後に異なるシグネチャが呼出しの候補となり、それぞれに、そのシグネチャとともに宣言された最も弱いレシーバを使う。候補が1つのレシーバ、タスクスロット形状（[§24.2.3](#s24-2-3)）、パラメーター取得形状（[§7.4](#s7-4)）を共有しなければ、呼出しはエラーとなる。共有していれば、[§10.1](#s10-1)と[§10.4](#s10-4)に従って比較する。シグネチャが異なっても矛盾する証拠ではない（[§8.3.2](#s8-3-2)）。`F`に束縛される型はそれぞれを満たさなければならない。Callableの効果境界は[§8.11.6](#s8-11-6)で定義する。

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

<a id="s8-11"></a>
### 8.11. 効果境界

**効果境界**は、呼出しが何にアクセスできるかについての保証である。すべての適合実装がこれを満たし、ジェネリックな呼出し側はこれに依拠する。語彙は閉じており、`confined`と`preserves results`だけである。安全なコードとunsafeコードで分類は同じである（[§5](#s5)）。

<a id="s8-11-1"></a>
#### 8.11.1. 宣言

境界は次の4つの位置で宣言する（文法：`EffectClause`、`EffectSpecification`、[§F.6](#sf-6)。キーワード：[§F.3](#sf-3)）。

- 関数要件の制約領域内の`effect`節は、その要件を制限する。
- Contract項目`effect A.r bound`は、祖先`A`から継承した要件`r`を制限する。これは正確に1つの関数要件を識別しなければならない。
- Callable制約の下の`effect`節は、それを通じた呼出しを制限する（[§8.11.6](#s8-11-6)）。
- 元の仮想関数の先頭領域（[§7.5](#s7-5)）にある`effect`節は、そのスロットを制限する。オーバーライドはこれを継承する。

オーバーライド、非仮想関数、先頭領域より後など、それ以外の本体にある効果項目はエラーとなる。実装は要件の境界を追加も削除もできない。同じContract内で1つの要件に同じ境界を2回指定するとエラーになる。祖先の境界を再記述した場合は1つの保証となる。`T is C`は、`C`とその祖先の境界だけを保証する。

```kimi
contract Take
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>

contract StableTake: Take
    effect Take.take preserves results

func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is StableTake
    let first = source.take()
    let second = source.take() // Valid: the bound excludes the Loans of first.
    return (first@move, second@move)

open struct Base
    public virtual func score(self: objref/Self) -> i32
        effect confined          // Slot bound, inherited by overrides.
        return 1

struct Derived : Base
    override func score(self: objref/Self) -> i32
        // effect confined       // Error: an override cannot declare a bound.
        return 2
```

`S is Take`だけの場合、`first`が生きている間の2回目の`take`は拒否される。

<a id="s8-11-2"></a>
#### 8.11.2. `confined`

`confined`実装の効果要約（[§15.6.4](#s15-6-4)）には、**環境効果**も分類できない効果も含まれない。環境効果は、可変状態に対する権限を入力ではなく環境から得る。一度得た権限によるアクセスは、それ以上の環境効果にはならない。

| 操作 | 分類 |
| --- | --- |
| 入力（レシーバ、引数、タスクスロット）をたどる操作、または入力自身 | 環境効果ではない。Loan検査は引き続き適用する。 |
| `Raw.allocate`（[§5.6](#s5-6)）を含め、呼出し内で作った記憶領域を使う操作 | 環境効果ではない。 |
| Placeの`@raw`（[§5.4](#s5-4)） | そのPlaceへの他のアクセスと同様に分類する。 |
| 生ポインター型間、または生ポインターから`usize`への変換、生アクセス（[§5.4](#s5-4)） | 環境効果ではない。生アクセスは[§5.2.2](#s5-2-2)によってLoanと比較する。 |
| 型がTransferableであると証明された不変の静的値の読取りや借用（[§15.2.4](#s15-2-4)） | 環境効果ではない。その初期化子は要約に加わる。 |
| その他の不変の静的値の読取りや借用（Transferableが反証済みまたは未証明） | 環境効果。 |
| 初回アクセス時の初期化を含む、可変の静的フィールドへのすべてのアクセス | 環境効果。 |
| Console出力など、プログラム外部の状態にアクセスするKimiの操作 | 環境効果。 |
| 外部関数の呼出し（[§22.6](#s22-6)）、整数からポインターへの変換（[§5.5](#s5-5)） | 環境効果。 |
| Copyと証明されていない型パラメーターや関連型射影など、定義が抽象のままにする型に破棄が依存する値の破棄または置換 | 分類できない効果。どちらの境界でも衝突となる。 |
| 型消去された破棄（[§16.3.3](#s16-3-3)）を含む、環境効果が不明な呼出しまたは破棄 | 分類できない効果。どちらの境界でも衝突となる。 |

検査対象は静的値の型なので、その型に含まれる基底オブジェクトビュー、実行時Contractビュー、共通関数型は、隠された`rc`または生ポインターの内容として扱い、抽象型にはTransferableの前提が必要となる。メモリー割当てとAbortは許される。環境から得たポインターは、未使用でも効果となる。外部関数の呼出しは、引数を通じてシグネチャが許すアクセスも行う。そのアクセスが以前の結果のLoanと衝突しなければ、`preserves results`実装は外部関数を呼び出せる。タスク操作の分類は[§22.3.3](#s22-3-3)に従う。引数として渡された静的借用は、入力からの権限である。

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

<a id="s8-11-3"></a>
#### 8.11.3. `preserves results`

**適用条件。** レシーバは借用（`Self`の`ref`、`uniq`、`objref`、`objuniq`）であり、許容されるすべての束縛で、正準な契約（[§15.3.7](#s15-3-7)）のどの結果も、その呼出しのレシーバLoanやレシーバの記憶領域（[§15.6.3](#s15-6-3)）に依存してはならない。これは、前提、結果Originの既定値（[§15.4.3](#s15-4-3)）、エイリアスを正規化した後で判定する。そうでなければ、その境界はエラーとなる。

**保証。** `r`に`preserves results`がある`T is C`の下では、値`v`に対する`r`の呼出しは、それより前の`v`に対する`r`の呼出し結果が保持するどのLoanとも衝突しない。その結果の移転や再借用の後に保持されるLoanも含む。除外される効果には、読取り、書込み、借用、新しい結果のLoan、静的値とキャプチャーへのアクセス、呼出し内部の後始末が含まれる。結果が返った後に追加された依存関係は、保証の対象外である。`v`の破棄や置換、他の要件、無関係なLoanも対象外であり、純粋性を約束するものではない。適合では、領域分割（[§15.6.3](#s15-6-3)）を使って、実装の要約を、その結果が保持し得るLoanと比較する。

```kimi
contract Peek
    associate Item
    func peek(self: ref/Self) -> Option<ref/Self.Item>
        effect preserves results // Error: the elided result Origin is self.
```

<a id="s8-11-4"></a>
#### 8.11.4. 検査と使用

適合が証拠（[§8.3.2](#s8-3-2)）を与えるのは、実装が特定され、そのシグネチャ、関連型、前提に互換性がある（[§8.6.3](#s8-6-3)）時点である。効果境界は適合の別の義務であり、[§15.6.4](#s15-6-4)の再帰の不動点を含む、完成した推移的要約に対して検査する。境界違反は適合宣言をエラーにするが、その適合を証拠に使った選択を変えることはない。祖先の境界は祖先への適合を通じて検査し、条件付きの場合はその条件の下で検査する。

具体的な呼出し先が分からない要件呼出しは、その前提から証明されたContractがその要件に宣言している境界を使い、実装の境界は使わない。実装の検査時と呼出し側で、その効果は次のようになる。

1. *入力を通じた効果：* 各パラメーターのアクセスモードで、レシーバと引数から到達可能なすべてのPlaceを対象とする。共有層を越えた先では共有アクセスだけとなり（[§15.6.2](#s15-6-2)）、raw層も、抽象部分の内部も含む（[§5.2.1](#s5-2-1)）。抽象的な入力は、そのOriginが表し得るすべてのLoanに到達し得る。所有ハンドルの引数の破棄は、[§16.3.3](#s16-3-3)の効果を加える。
2. *環境：* `confined`があればなし。それ以外は不明であり、[§15.6.4](#s15-6-4)のとおりに静的Loanへ影響する。タスク呼出しも静的Loanと比較する（[§24.3](#s24-3)）。
3. *以前の結果：* `preserves results`があれば、同じ値に対する同じ要件の以前の結果が保持するLoanを除外する。

生存中のLoan（[§15.6.2](#s15-6-2)）と衝突する可能性があれば、後の呼出しでエラーとなる。無効な効果項目は境界を与えない。

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

<a id="s8-11-5"></a>
#### 8.11.5. 委譲

`preserves results`を持つ`r`の実装を検証するとき、`self`からフィールドのパス（借用フィールドも含む）を通じて到達する値`w`に対して要件`d`を呼び、その結果から`r`の結果が保持するLoanは、`w`に対する`d`の以前の結果のLoanとして扱う。`d`が`w`について`preserves results`を持つ場合、[§8.11.4](#s8-11-4)の規則3によってそのLoanを除外する。本体が`w`へのパス上の値を置換、交換、Moveする場合は、何も除外しない。引数、他の要件、他の値からのLoanは通常どおり比較する。

```kimi
struct Drain<J>
    J is Iterator
    Self is StableTake
    associate Take.Item is J.Item
    var inner: J
    public func take(self: uniq/Self) -> Option<J.Item>
        return self.inner.next()                // Valid.

struct Merge<J>
    J is Iterator
    Self is StableTake
    associate Take.Item is J.Item
    var left: J
    var right: J
    public func take(self: uniq/Self) -> Option<J.Item>
        match self.left.next()                  // Error: may conflict with an earlier result from right.
            .Some(let item) => return .Some(item@move)
            .None => return self.right.next()
```

<a id="s8-11-6"></a>
#### 8.11.6. Callableの効果境界

Callable制約は、要件全体が1つの`Callable<...>`である場合に、インデントした`effect`節のリストを持てる（文法：`CallableEffects`、[§F.6](#sf-6)）。このリストは、関数、構造体、列挙型、関数要件の制約領域（条件付きメンバーの前提も含む）で使えるが、Contract全体の節、関連型宣言、適合条件では使えない。また、このリストは先頭領域を終わらせない（[§7.5](#s7-5)）。1つの制約内で各種類は高々1回だけ記述できる。`preserves results`には`ref`または`uniq`のレシーバが必要である。

`F is Callable<r, S>`に束縛される型は、`r`の下で取得される呼出しについて、取得と、`owner`の場合は残りの破棄も含めて、各境界を満たさなければならない。キャプチャーには、入力であるレシーバを通じて到達する。境界の対象は呼出しだけであり、別の箇所で値を破棄すると、その破棄効果が外側の要約に加わる（[§16.3.3](#s16-3-3)）。`preserves results`は、同じ呼出し可能値の以前の結果のLoanとの衝突を除外する。

適用可能性（[§10.1](#s10-1)）と証明（[§8.3](#s8-3)）は境界を考慮しない。境界は、選択された呼出し、関数参照（[§10.5](#s10-5)）、または型の形成の義務であり、完成した要約に対して検査する。選択された呼出し可能な引数が、そのCallable制約の効果境界に違反する場合、その引数または型引数でエラーとなる。

| 束縛される型 | 証拠 |
| --- | --- |
| 非仮想のFunction Item | その効果要約（[§15.6.4](#s15-6-4)）。 |
| 仮想スロットのFunction Item | `confined`に限り、スロットの境界（[§6.4.6](#s6-4-6)）。 |
| 要件のFunction Item | `confined`に限り、利用可能な境界。 |
| 具体的なクロージャー | 取得と環境の破棄を含む、その本体の要約。 |
| 型パラメーターまたは射影 | 同じシグネチャ、同じかより強いレシーバ（`ref` < `uniq` < `owner`）、その境界を持つ、その型についての前提。 |
| 共通関数型 | なし。 |

`F`を通じた呼出しは、[§8.11.4](#s8-11-4)の規則1から3に従い、同じシグネチャと、同じかより強いレシーバを持つ前提の境界を使う。呼出し側は、宣言内のCallable入力を通じた呼出しを実引数と合成する（[§15.6.4](#s15-6-4)）。実装は、要求されていないCallable境界を追加できない。

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

<a id="s8-12"></a>
### 8.12. 実行時Contract

実行時Contractビュー（Contractに対するオブジェクトビュー）には、まだ定義されていない実行時使用の指定が必要なため、この版では形成できない。ここでの規則は、その拡張に適用する。単独のContract名が値の型になることはない。

**ObjectViewCompatible(C)**が成立するのは、`C`が実行時使用に指定され、型関数の要件がなく、関連型を固定した上で各インスタンス要件が次を満たす場合である。消費しないオブジェクトレシーバを持つ安全なメソッドまたはアクセサーであり、`Self`をレシーバにだけ使い、未束縛の関連型やメソッドのジェネリックパラメーターを持たず、型とOriginを隠された型なしで（レシーバ、入力、`static`を通じて）表現できなければならない。各ゲッターの結果とセッターの要件は別々に検査する。`C`に対してオブジェクト形式を形成するには、この判定が必要である。値を消去してビューにするにはObjectPayload（[§8.7.3](#s8-7-3)）とOwned（[§15.8.1](#s15-8-1)）が必要となる。**Implements(D, C)**には、明示的または継承された適合（[§8.6.2](#s8-6-2)）で、ObjectCallCompatibleがProvenであるもの（[§12.7](#s12-7)）が必要であり、束縛されたContract参照に対する一意の適合（[§8.6.1](#s8-6-1)）を使う。

```kimi
contract Speaker
    Self is ObjectPayload
    func speak(self: objref/Self) -> string

func announce(value: objref/Speaker) -> string   // Error in this revision: runtime Contract Views cannot be formed.
    return value.speak()
```

<a id="s8-13"></a>
### 8.13. 関数の明示的な完全特殊化

**明示的な完全特殊化**は、静的ジェネリック引数の1つの閉じた組に対して、ジェネリック関数の実装を与える。呼出しの契約は保持するが、動作は異なってもよい。一致する特殊化があれば、それを使わなければならない。

<a id="s8-13-1"></a>
#### 8.13.1. 宣言と対象

`specialize func`で特殊化を宣言する。1つの未修飾名、すべてのスロットに順番に与える明示的引数（長さは[§4.4](#s4-4)に従う）、元の関数にある場合に限ってタスクスロット、型付きパラメーター、省略可能な結果、本体を記述する。型パラメーターは宣言せず、元の関数のパラメーター名も参照できない。結果を省略するとUnitとなる。引数は閉じていなければならず、未束縛の型パラメーターやペアバインダー、未解決の射影、外側のジェネリックパラメーターを含められない。対象は通常の本体を持つジェネリックなルート関数、型関数、メソッドであり、コンストラクター、`drop`、アクセサー、演算子は対象にできない。部分特殊化、条件付き特殊化、コンテナー引数の特殊化はない。

特殊化とその対象は、同じ宣言スコープに属する。ルート関数ならSourceDocumentの実行スコープ（[§6.1.1](#s6-1-1)）、メンバーなら同じコンテナーである。対象は、そのスコープ内で名前、種類、スロット数が同じであり、代入後のタスクスロット、レシーバ、パラメーターが、Originを除いたヘッダーの正規化構造と一致する一意の関数である。一致が0件でも複数でもエラーとなり、結果、制約、順序で同順位を解消することはない。

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func inspect<T>(value: T) -> () => ()
func inspect<T>(value: i32) -> () => ()
specialize func inspect<i32>(value: i32) -> () => ()
// Error: both declarations have the same substituted input structure.
```

<a id="s8-13-2"></a>
#### 8.13.2. 継承する契約

ヘッダーには、代入後の元のレシーバ形状（[§7.3](#s7-3)）、パラメーター、結果、外部名を、Adaptationなしで再記述する。内部名は`external => internal: Type`によって変えられる。アクセス、既定値、`!`境界、制約、属性、安全性、効果境界、Originバインダーは継承され、記述しない。呼出し側は元の既定値を使う。`find<i32>(10)`は元の既定値を評価してから、特殊化を実行する。本体は、元の関数が許容するすべてのOrigin束縛で動作しなければならない。特殊化はOriginの制限を加えたり、適用可能性を狭めたり、必要な証明を置き換えたりできない。ObjectCallCompatible（[§12.7.2](#s12-7-2)）はNotProvenのままでもよい。オーバーライドのヘッダーも同じ規律に従う（[§6.4.1](#s6-4-1)）。

<a id="s8-13-3"></a>
#### 8.13.3. 選択

選択キーは、元の宣言と正規化された静的引数であり、Originだけを除く。`obj/D`と`rc/D`は異なる。1つのキーに2つの特殊化があるとエラーになる。呼出しと関数参照は通常の探索、オーバーロード解決、元の契約を使い、特殊化は候補にならない。引数によってキーが決まると、呼出し時に引数が分かっていたか、ジェネリック代入後に分かったかを問わず、一致する特殊化を実行する。動的型は関係しない。特殊化は名前を持たず、迂回できない。その内部で同じ関数を同じ引数で呼び出すと再帰になる。特殊化は元の関数のKotonohaと宣言スコープ（§8.13.1）に属し、宣言順や読込み順が選択に影響することはない。

```kimi
func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

func forward<T>(value: ref/T) -> i32 => classify<T>(value)
let number: i32 = 10
let result = forward<i32>(number@ref) // 1: the i32 specialization runs.
```

<a id="s8-14"></a>
### 8.14. ジェネリックのアクセス効果

取得またはAdaptationは、静的に一意な**アクセス効果**を持つ。これは、取得（CopyまたはMove）、Loan操作（共有借用、排他借用、再借用）、およびその対象PlaceとOrigin依存関係からなる。消費は取得の種類ではない。[§15](#s15)の静的検査を指し、使用箇所が[§15.1.5](#s15-1-5)のアクセス操作を行ってよいかを調べる。呼出しの排他的Adaptationには予約を使う（[§15.6.7](#s15-6-7)）。

- 単独のPlaceは、許容される各場合に[§3.5](#s3-5)に従う。Copyが証明されていればCopyし、型が`uniq`または`objuniq`参照であると証明されていれば再借用する。どちらでもない場合は定義の検査時にエラーとなり、Moveを推論することはない。
- `@copy`は同じ証拠でCopyし、`@move`は移転する。
- `@s`、`@s/T`、`@Type`はAdaptation表（[§13.5](#s13-5)）に従う。`s`が`owner`を許容する場合、その場合の`value@s`は同じ型の取得となり、Copyの証拠が必要になる。

CopyがUnknownでもNon-Copyにはならず、コンパイル時ディレクティブがアクセス効果を選択することもない（[§19.3](#s19-3)）。許容される場合ごとに効果が異なるときは、それぞれについてCopyまたは再借用を計画し、Semanticsの場合ごとに検証する（[§8.15.2](#s8-15-2)）。`@s`と`@follow`は逆操作ではない。`@follow`はPlaceを選び、Takeを復活させることはない。

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

<a id="s8-15"></a>
### 8.15. ジェネリック本体の検査

<a id="s8-15-1"></a>
#### 8.15.1. 普遍的な検証

ジェネリック本体は、その制約、外側の条件付き適合の前提、シグネチャを満たすすべての整形式な型、長さ、Originの束縛に対して有効でなければならない。使用の有無にかかわらず、定義の検査時に[§8.3](#s8-3)の規則で検査する。型を列挙したり、隠れた制約を推論したりすることはない。契約で表現または証明できないことは、定義を無効にする。ジェネリック呼出しは、呼出し側の前提から呼出し先の要件を証明する。特殊化によって無効な本体を救済することはできない。

宣言の公開された義務は、そのシグネチャ、スキーマ、関連型、制約、条件付きメンバーの前提の整形式条件であり、Copyなどの本体内の要件ではない。読取り元を初期化済みのまま残す読取りには、`T is Copy`、借用、または再初期化が必要となる。matchの主語と反復項目は[§15.1.6](#s15-1-6)に従う。格納された取得結果、要素、ゲッターの結果は、宣言された型を保持する（[§4.7](#s4-7)）。ObjectCallCompatible（[§12.7](#s12-7)）は実装の保証であり、適用可能性の前提ではない。インスタンス化によって強まることもない。

```kimi
func transfer<T>(value: T) -> T => value@move   // Moves a Copy or Non-Copy T alike.

func twice<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)

func invalidTwice<T>(value: T) -> (T, T)
    return (value, value) // Error at definition: Copy is not guaranteed.
```

<a id="s8-15-2"></a>
#### 8.15.2. Semanticsの場合分け

ペア層（[§13.5.5](#s13-5-5)）の合法性は、許容されるすべての場合について一度判定する。その後、所有権解析は各**Semanticsの場合**の下で行う。これは、スコープ内の各ペアバインダー（本体自身、そのコンテナー、クロージャーなら外側の宣言のもの）に対して、許容されるSemantics（[§8.3.4](#s8-3-4)）を1つずつ選んだものである。各場合では、各ペア層はその場合の完全な型となり、通常のアクセス効果、依存関係、Loan、後始末、クロージャーのレシーバを持つ（[§7.7.3](#s7-7-3)）。場合同士を統合することはないので、Copyする場合に、再借用の場合だけが作るLoanが与えられることはない。インスタンス化は、その引数が選ぶ場合に対応する。Semanticsの場合分けはこの解析だけで使い、[§8.3](#s8-3)の証明に前提を加えない。

ジェネリック定義が有効となるのは、すべてのSemanticsの場合が有効であるときである。実装が場合をどう列挙、共有、分解するかは規定しない。実装が1つの定義について解析するSemanticsの場合の数は実装定義であり、その上限を超えることは資源不足（[§23.1.1](#s23-1-1)）であって、プログラムが無効であるという判定ではない。

```kimi
func count<s/T>(value: s/T) -> i32
    s is ref or uniq          // s is a pair binder; the body is checked for ref and for uniq.
    let view = value@follow@ref
    return 0
```

<a id="s8-15-3"></a>
#### 8.15.3. 具体的なレイアウトと表現

具体的な型のレイアウト義務（Cレイアウトのサイズ0のフィールドなどのレイアウトモード制限、アラインメント上限、サイズ上限）は、プログラムがその型の値を値として格納、生成、受渡し、返却するか、そのサイズを必要とするときに検査する（フィールド、局所変数、配列要素、オブジェクトペイロード、生メモリー割当て）。借用、生ポインター、オブジェクトハンドルの先でだけ型を形成する場合や、制約内で型名を指定する場合は検査しない。義務を満たさなければ、レイアウトが必要な使用箇所でLanguageエラーとなり、検査によって報告する（[§23.2.2](#s23-2-2)）。実装上限の超過は資源不足であり、意味上の無効性ではない。実装が生成できないレイアウトはUnsupportedとして報告する（[§23.1.1](#s23-1-1)）。

これらの検査は、検査対象の非ジェネリック本体が直接、または他のインスタンス化を通じて必要とするすべてのインスタンス化を対象とする。本体が到達可能か、生成されるかは問わず、この検査対象集合については常にレイアウト義務を検査する。検査対象のどの非ジェネリック本体からもインスタンス化されないジェネリック本体は、記号的にだけ検査する。ビルドではなく`check`が対象の未対応操作を報告するかは実装定義だが、どちらのコマンドが報告する場合も、検査対象集合全体を対象とする。

ジェネリック関数が拡大する型引数で自身を呼び出す多相再帰は有効である。インスタンス化ごとに1つのコピーを生成する実装では、実装定義の上限に達する場合がある。これは資源不足であり、無効なプログラムではない。

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

---

<a id="s9"></a>
## 9. 名前、シグネチャ、アクセス



名前解決は各名前を宣言に対応付ける。その後、型名には型名選択（[§9.7](#s9-7)）と型の合法性検査を、呼出しにはオーバーロード解決（[§10](#s10)）と使用箇所での合法性検査を適用する。

**後戻りしない。** 探索段階、宣言、オーバーロードを一度選択すると、その後の失敗はエラーとなり、別のものを選び直さない。未知の名前と解決不能な依存循環はエラーとなる。ファイル、エイリアス、候補の順序は結果に影響しない。

<a id="s9-1"></a>
### 9.1. シグネチャ

シグネチャは、同じスコープに宣言を共存させられるかを決める。

| 宣言 | シグネチャ |
| --- | --- |
| 型宣言 | 種類、名前、ジェネリックパラメーター数 |
| 関数 | 名前、ジェネリックパラメーター数、順序付きの正規化したパラメーター型（レシーバを含む） |
| コンストラクター | 宣言元の構造体、順序付きの正規化したパラメーター型 |
| フィールド、計算メンバー、プロパティ要件 | 名前 |
| enumのケース | enum内での名前。ペイロードではオーバーロードしない |

同じスコープの型名前空間にある同名の宣言は、種類（group、struct、enum、contract）が同じでなければならない。種類の衝突は、引数個数に関係なくエラーとなる。同じ種類でもジェネリック引数個数が異なれば別の型となる。種類と引数個数が同じgroupまたはstruct宣言はフラグメントとなる（[§6.1.2](#s6-1-2)）。その他の重複はエラーとなる。`rootgroup`のパス要素が同じスコープのgroup以外の型を指す場合は、種類の衝突となる。`rootgroup`がその横にgroupを合成することはない。

ジェネリックパラメーター数（**GenericArity**）には長さスロットを含め、ペアは1個と数える。Originスキーマではオーバーロードしない。`View<T> {source}`と`View<T> {left, right}`は共存できない。

**正規化。** 各名前をそのSymbolとKotonohaに解決する。透過的エイリアスと確定した関連型の射影（[§8.6](#s8-6)）を展開し、グループ化と冗長な`owner`接頭辞を除き、Semanticsの各層をすべて保持する。ジェネリックの位置は構造として表す。

| ソース | 正規化後 |
| --- | --- |
| 通常の`T`、または元の`s/T` | `Slot(i)` |
| ペアバインダー`s`／ペア対象`T` | `OuterSemantics(Slot(i))`／`DirectTarget(Slot(i))` |
| 別のスロット`U`に適用した`s` | `Apply(OuterSemantics(Slot(i)), Slot(j))` |
| 長さ`N`／固定長配列 | `LengthSlot(i)`／`FixedArray(Length, Element)`。長さは[§4.4](#s4-4)に従う |
| `Self` | structまたはenumでは`S<Slot(0), …>`。Contractでは適合型のバインダー`SelfSlot` |

比較には構造的なα同値を用い、インスタンス化や制約の証明による簡約は行わない。スロットの種類ではシグネチャを区別しない。`f<T>(value: T)`と`f<s/U>(value: s/U)`は衝突する。`ref/T`と`uniq/T`は別のものとして残る。ただし[§7.3](#s7-3)は、同じ名前の下に2つのレシーバ形状を置くことを禁じる。レシーバも他のパラメーターと同じように比較し、その名前は問わない。

Originとその関係、戻り値型、パラメーター名、既定値、引数名の契約（[§7.2.2](#s7-2-2)）、アクセス、`unsafe`、制約、タスクスロット（[§24.2.3](#s24-2-3)）では、シグネチャを区別しない。シグネチャの重複はエラーとなる。異なるシグネチャでも、パラメーター取得形状（[§7.4](#s7-4)）によって衝突することがある。異なるコンテナーにある同じシグネチャは、オーバーロード解決で1つを選択しない限り、使用時に曖昧となる。

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

<a id="s9-2"></a>
### 9.2. 名前空間と探索ロール

**探索ロール**は構文上の使用方法によって決まり、名前空間の候補を絞り込む。

| 名前空間 | 宣言 |
| --- | --- |
| 型 | コンテナー、Kotonoha参照名、名前付きエイリアス、型パラメーター、ペアバインダー、関連型、`Self`、組込みSemanticsとカテゴリー |
| 値 | 関数、フィールド、計算メンバー、enumのケース、パラメーター、ローカル、長さパラメーター |
| Origin／ラベル | Origin／ラベル |

| 探索ロール | 名前空間 | 選択可能な宣言 |
| --- | --- | --- |
| Core | 型 | 構造体、enum、関連型、`Self`、型パラメーター、ペア対象 |
| オブジェクトのView Target | 型 | CoreとランタイムContract |
| 型のSemantics | 型 | ペアバインダーと言語定義のSemantics |
| 修飾子 | 型 | group、Kotonoha参照、メンバーを修飾できる型 |
| 要件 | 型 | 要件構文が認めるもの |
| 宣言コンテナー | 型 | group、struct、enum、contract、Kotonoha参照 |
| 値 | 値 | 呼出し可能かどうかに関係なく、すべての値宣言 |
| enumのケース | 値 | 修飾子または期待型で決まるenumのケース。非修飾名は含まない（[§9.5](#s9-5)） |

ロールによる絞込みは宣言の種類と構文上の使用方法だけを見て行い、引数個数、引数、期待する結果、制約、後続のメンバーは見ない。呼出し可能なものだけを対象にするロールはない。オブジェクト対象の構文は、オブジェクトのView Targetロールを選ぶ。選択後、型の合法性検査により、宣言された制約の下でその束縛をそのロールに使えるかを確認する（[§8.1.1](#s8-1-1)）。ここでの失敗も、その後のObjectViewCompatibleの失敗もエラーとなり、外側に向けた探索を再開しない。

型名と値名は共存できる。同じ名前空間とスコープで名前を重複させられるのは、[§9.1](#s9-1)による場合だけである。Originとラベルの探索が型名や値名に切り替わることはない。

```kimi
struct T

func f<s/T>(x: s/T, y: T) -> () => ()
// Error: T here is the pair target, not struct T, and is not proven a value Type.
```

<a id="s9-3"></a>
### 9.3. ローカルスコープと文脈依存の名前

ローカルは宣言後から見え、その初期化子からは外側のスコープが見える。内側のブロックは外側のローカルを隠せる。同じブロックでは同じ名前を2回宣言できず、パラメーターは直下の本体と同じ名前の空間を共有する。ローカル関数はブロックの入口から見え、オーバーロードできるが、ローカルやパラメーターとは衝突できない。コンテナーのメンバーは宣言前にも使えるが、未初期化の値は読めない。

入れ子の関数は外側の型パラメーターを使えるが、名前付きの入れ子の関数は実行時のローカルをキャプチャーしない。キャプチャーできるのは匿名関数だけである（[§7.7.2](#s7-7-2)）。シグネチャで外側のOriginを指定することは、キャプチャーではない（[§15.3.4](#s15-3-4)）。SourceDocumentの実行スコープは[§6.1.1](#s6-1-1)、パターンのスコープは[§14.8.2](#s14-8-2)で定義する。

`Self`は型とContractの文脈のために予約される。`self`、`value`、および格納アクセサー内の`storage`（[§11.3](#s11-3)）は文脈依存の束縛である。有効な間は再宣言できず、暗黙にはキャプチャーされず、名前付きローカル関数内では有効にならない（[§7.3](#s7-3)）。それ以外での`storage`は通常の名前である。

```kimi
func example(input: i32) -> i32
    let x = input
    if true
        let x = x + 1     // Valid: the initializer uses the outer x.
    // let input = 0      // Error: a parameter and a local share one space.
    return x
```

<a id="s9-4"></a>
### 9.4. アクセス

使用するには、アクセス権、スコープ・修飾・エイリアスを通る経路（**名前の到達可能性**）、および対応するロールが必要となる。エイリアスはアクセス範囲を広げない。

<a id="s9-4-1"></a>
#### 9.4.1. アクセスレベルと領域

宣言は1つのアクセス指定を持つ（構文は`Access`、[§F.6.1](#sf-6-1)。修飾子との順序は[§F.3.3](#sf-3-3)）。protectedの形式を適用できるのは構造体のメンバーだけであり、入れ子のコンテナーとアクセサーもこれに含む。

アクセス領域は、次の基本集合を組み合わせた集合式である。

- `Body(C)`：コンテナー`C`の字句上の本体。すべてのフラグメントと入れ子の本体を含む。
- `Kotonoha(K)`：Kotonoha `K`の全ソース（同一性は[§18.4.1](#s18-4-1)）。
- `Derived(S)`：`S`のいずれかの構築型から直接または間接に派生する、現在および将来のすべての構造体の本体。`S`が`open`でなければ空集合。
- `All`。

| Kotonoha `K`内のコンテナー`C`のメンバーのアクセス | 領域 |
| --- | --- |
| `private`（既定） | `Body(C)` |
| `internal` | `Kotonoha(K)` |
| `protected` | `Body(C) ∪ Derived(C)` |
| `protected internal` | `Kotonoha(K) ∪ Body(C) ∪ Derived(C)` |
| `private protected` | `Kotonoha(K) ∩ (Body(C) ∪ Derived(C))` |
| `public` | `All` |

実効領域`Access(D)`は、この領域とすべての外側のコンテナーの領域の共通部分となる。privateなルート宣言の領域は`Kotonoha(K)`である。構築されたジェネリック型の領域は、その宣言の領域と具体的な引数の領域の共通部分となる。packageアクセスやfriendアクセスはない。

privateアクセスはファイルではなく、統合されたコンテナーに従う。親は子のprivateメンバーにアクセスできない。アクセサーのアクセスは[§11.3.2](#s11-3-2)に従う。enumのケースとContractの要件には、そのコンテナーの領域を用い、修飾子は認めない。メンバーを選択した後、必要なアクセサーにアクセスできない場合やレシーバがない場合はエラーとなる。

<a id="s9-4-2"></a>
#### 9.4.2. 領域の包含

`A ⊆ B`は基本集合だけで判定する。`A`を共通部分の和集合、`B`を和集合の共通部分として表す。`A ⊆ B`が成り立つのは、`A`の各共通部分が、`B`の各和集合`U`について、異なる2つのKotonohaの基本集合を含む（したがって空集合である）か、`U`に含まれる基本集合を含む場合である。基本集合が`U`に含まれるのは、`U`のいずれかの基本集合に含まれる場合、またはその基本集合が`Derived(S)`であり`Access(S) ⊆ U`である場合である。

基本集合の包含は次のとおりとする。各基本集合は自分自身と`All`に含まれる。`open`でない`S`の`Derived(S)`は、すべての基本集合に含まれる。`E`が`C`自身または`C`を内包する場合は`Body(C) ⊆ Body(E)`。`C`が`K`に属する場合は`Body(C) ⊆ Kotonoha(K)`。`C`が`S`から派生する構造体自身、またはその中に入れ子になっている場合は`Body(C) ⊆ Derived(S)`。`S`が`S'`自身または`S'`から派生する場合は`Derived(S) ⊆ Derived(S')`。それ以外の包含は成り立たない。`Derived`は宣言された`open`修飾子と基底句に従い、あるビルドに含まれる構造体には依存しない。

```kimi
internal struct Secret

public open struct Base
    protected func peek(value: Secret) -> () => ()
    // Error: Derived(Base) is not included in Kotonoha(K).
```

<a id="s9-4-3"></a>
#### 9.4.3. protectedレシーバ

派生構造体`D`から基底`B`のインスタンスメンバーにprotectedアクセスするには、静的な実効Coreが`D`、または`D`の派生型であるレシーバが必要となる（ジェネリックなレシーバでは、証明済みの基底制約を使える）。`B`や兄弟型のレシーバでは足りない。アクセサーは個別に検査する。この制限は、`B`の本体内、同じKotonohaからの`protected internal`アクセス、およびインスタンス宣言ではない型関数・コンストラクター・入れ子のコンテナーには適用しない。継承によってprivateアクセスが許可されることはない。

この制限が適用される場合、protectedなインスタンス関数を使えるのは`receiver.f(...)`という形だけである。その関数の型修飾呼出し（`Left.f(x)`）やFunction Item（`Left.f`）はエラーとなる。派生型の本体は、アクセス可能なprotected基底コンストラクターを基底句内、または`Base.init(...)`として呼び出せる。

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

<a id="s9-4-4"></a>
#### 9.4.4. APIシグネチャのアクセス可能性

宣言の**APIシグネチャ**とは、その宣言が公開するすべての型と要件である。宣言`D`と、その中の各具体的な型または要件`T`について、`D`が使用されるかに関係なく`Access(D) ⊆ Access(T)`を満たさなければならない（[§9.4.2](#s9-4-2)）。直接の基底も、派生型を`D`としてこの規則に従う。

対象には、パラメーター、レシーバ、結果、enumのペイロード（enumの領域）、フィールドとプロパティの型（プロパティの領域）、各アクセサー固有の構成要素（そのアクセサーの領域。制限されたセッターはプロパティの領域を狭めない）、および本体内に書かれた制約を含む要件が含まれる。型注釈のないフィールドなどの推論された型も、明記された型と同様に検査する。エイリアス展開後に再帰的に検査し、Semanticsによってアクセス不能なCoreを隠すことはできない。関連型の射影では、修飾子、要件、公開される束縛を検査する。ジェネリックパラメーターは記号的に扱い、その制約を検査する。publicなジェネリック宣言にはprivateな型引数を渡せる。

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

<a id="s9-4-5"></a>
#### 9.4.5. ジェネリック本体

ジェネリック本体は、どこでインスタンス化されても、その定義箇所の文脈とソース環境（[§18](#s18)）で名前を解決し、アクセスを検査する。呼出し側のエイリアスが候補を追加することはない。APIシグネチャに公開されないprivateな型や補助関数を使え、別の場所でのインスタンス化によって呼出し側にアクセス権が与えられることもない。型引数と制約は使用箇所で検査する。

<a id="s9-4-6"></a>
#### 9.4.6. 適合のアクセス可能性

型`T`、束縛済みContract参照`C`、各必須実装`M`について、次を満たす。

```text
Access(Conformance(T, C)) = Access(T) ∩ Access(C)
Access(Conformance(T, C)) ⊆ Access(M)
```

祖先の各適合は個別に検査する。先に実装を選択し、その後にアクセスを検査する。選択をやり直すことはない。適合の範囲をprivateメンバーに合わせて狭めることはない。

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

<a id="s9-5"></a>
### 9.5. 非修飾の探索

必要な名前空間とロールについて、次の段階を順に探索する。

1. ローカルスコープ、続いて外側の各字句スコープとパラメータースコープ。ルートの関数宣言では、その文書の実行スコープも含む。コンテナー本体はこの実行スコープに内包されない（[§6.1.1](#s6-1-1)）。
2. 現在のコンテナー。
3. 使用箇所のKotonohaのプロジェクトルートまで、各親コンテナー。
4. 型修飾子の場合だけ、使用箇所のKotonohaのコンパイルルートにある参照名（[§18](#s18)）。
5. 使用箇所のSourceDocumentにある明示的エイリアスをまとめて探索する。
6. 定義元のKotonohaの既定エイリアスをまとめて探索する（[§18.2.3](#s18-2-3)）。

型パラメーターと文脈依存の名前は、その宣言位置で見つかる。`E@X`の裸のAdaptation対象は、[§13.5](#s13-5)のロールを使う。

各段階では、同名の宣言を集め、同じSymbolへの経路を重複排除し、ロールとアクセスで候補を絞る。選択可能な宣言がある最初の段階で探索を止める。同名の関数は1つの**関数グループ**を構成する。同じ段階に関数と別種の値があると衝突する。探索停止後の型引数、ラベル、制約、引数型の不一致や、呼出し不能な値はエラーとなり、さらに外側を探索することはない。より近いgroup `X`はCoreとしての`X`の探索を止めないが、`X.member`の探索は`member`がなくても止める。

**暗黙のレシーバや継承はない。** 非修飾名は、enumのケース、レシーバを必要とするメンバー、継承された宣言を選択しない。選択可能な宣言がある最初の段階に、そのようなケースとメンバーしかない場合は、「修飾が必要」というエラーで探索を止める。段階2または3で探索するstructに自身の選択可能な宣言がない場合は、基底の各層を順に調べ（条件付きメンバーも含む。[§6.2.2](#s6-2-2)）、その名前の、アクセス可能でロールに合う宣言がある最初の層で同じエラーにより止める。この調査に基底の引数は不要であり、protectedレシーバの制限を無視し、基底のジェネリックパラメーターと`Self`は飛ばす。struct自身の基底句は、この調査をせずに解決する。それでも`struct S : S.N`のように循環ができる場合は、解決不能な依存循環となる。この規則はOriginやラベルの探索には適用しない（[§15.3.4](#s15-3-4)）。

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

<a id="s9-5-1"></a>
#### 9.5.1. エイリアス

名前付きエイリアスは、対象の修飾子ロールを持つものとして型名前空間の段階5に加わる。Coreではない。選択したエイリアスに後続のメンバーがなくても、別の候補に戻らない。同じSourceDocumentで同名の名前付きエイリアスを2つ宣言する場合は、未使用であっても、正規化された引数を含めて同じ参照を指さなければならない。

開くエイリアス（[§18.2.1](#s18-2-1)）によって公開されたメンバーは、使用時に衝突を検査する。そこで複数の出所から集めた関数グループは、[§7.4](#s7-4)を満たし、タスクスロット形状が1つでなければならない（[§24.2.3](#s24-2-3)）。満たさなければ、すべての使用がエラーとなる。`A.f`を使えば1つの出所を選べる。

<a id="s9-6"></a>
### 9.6. 修飾された探索と継承の探索

`A.B.C`の最初の要素は非修飾の探索で見つける。後続の要素は、選択された対象のメンバーだけを探索する。型側の途中の要素は修飾子であり、最後の要素のロールは構文に従う。関連型の射影は[§8.5](#s8-5)と[§9.7.3](#s9-7-3)に従う。

型による修飾と値による修飾の両方が可能な場合は、それぞれの名前空間が自身の最初の段階を確定し、完全な経路をたどる（ロール、アクセス、静的使用かインスタンス使用かを含む）。[§9.5](#s9-5)による停止があると、その経路は失敗する。成功した経路が1つだけならそれを選ぶ。複数あれば曖昧となり、引数や期待型で決めることはない。同じSymbolへの経路は1つと数えるが、異なるレシーバを介した値アクセスは別の経路とする。

`::A.B`は使用箇所のKotonohaのコンパイルルート（[§18](#s18)）から始め、ローカルとエイリアスを無視する。ルート名と参照名の衝突は[§18.4.1](#s18-4-1)に従う。

**継承の探索。** structのメンバー探索では、まずそのstructを、次に基底引数を代入した基底を1層ずつ探索し、アクセス可能でロールに合う宣言がある最初の層で止める。その層の同名関数だけが関数グループ全体となる。レシーバ、適用可能性、アクセサー、Loanの検査は後で行い、探索を再開することはない。オーバーライドは探索層を追加しない（[§6.4.2](#s6-4-2)）。アクセス可能な基底の`f`と並ぶ派生型の`f`は、[§6.2.2](#s6-2-2)に従う。新たな明示的Contract実装もこの方法で選択してから検査する（[§8.6](#s8-6)）。

**制約から集めるグループ。** ジェネリックパラメーターのメンバー探索は、その制約から同名の要件を集める（[§8.6.4](#s8-6-4)）。集めたグループは使用時に検査する（[§7.3.2](#s7-3-2)、[§7.4.4](#s7-4-4)、[§24.2.3](#s24-2-3)）。

```kimi
group Config
    public var count: i32 = 3

struct Settings
    public var count: i32 = 0

func read(Config: ref/Settings) -> i32
    return Config.count                  // Error: both the Type and the Value path succeed.
```

<a id="s9-6-1"></a>
#### 9.6.1. 基底部分オブジェクトへのレシーバ射影

`receiver.member`の探索で、レシーバの静的な実効Core `D`の基底`B`にあるインスタンス宣言を選択した場合は、基底引数を代入し、レシーバをそのインラインの`B`部分オブジェクトに射影する。まず元のレシーバに対してアクセスを検査する。型関数には射影が不要である。型修飾呼出しと関数値は、[§9.4.3](#s9-4-3)の後、通常の引数規則に従う。

宣言が`ref/B`または`uniq/B`の場合、射影は元の権限で、部分オブジェクトの共有または排他の借用・再借用を形成する。これは暗黙のレシーバ取得の一部である（[§7.3](#s7-3)）。型`D`の裸の所有Placeは、その貸出し点に排他的に書き込める場合、排他的に射影される（[§15.1.5](#s15-1-5)）。レシーバは引数より先に1回だけ評価する。メソッドや独自アクセサーは部分オブジェクト全体を借用する。標準プロパティアクセスは格納Placeに射影し（[§11.2.2](#s11-2-2)）、元のレシーバが所有・借用・オブジェクトのどれであるかという分類を保つ。

射影は変換ではなく、最良候補の比較には関与しない（[§10.4](#s10-4)）。射影を介して使う借用レシーバの実装には、ObjectCallCompatibleがProvenであることが必要となり、オーバーロード選択後に検査する（[§12.7](#s12-7)）。NotProvenはエラーとなり、候補の除外には使わない。射影は基底全体のMove、置換、または切り出された所有者を認めない。レシーバ由来の結果はそのLoanを保持する。オブジェクトでは、同一性と動的型を保ち、ハンドルを作らず参照数も変えず、Ownedの消去証拠を必要とする（[§6.4.5](#s6-4-5)）。`owner/B`、`obj/B`、`rc/B`、`arc/B`を満たすことはない。

<a id="s9-7"></a>
### 9.7. 型名選択

探索が止まった後、明示的な型引数の数と種類で型候補を絞る。型引数は使用箇所の文脈で解決する。候補はちょうど1つ残らなければならず、0個なら不一致、複数なら曖昧となる。その後に制約を検査し、別の型を試すことはない。

`.init`またはケース参照（式またはパターン）の前で、修飾子自身の型引数リストが省略されている場合は、自身の引数個数が0の参照があればそれを選び、なければ唯一のジェネリック参照を選ぶ。複数あれば曖昧となる。引数や期待型では決めない。ケースの引数推論は[§6.5.2](#s6-5-2)に従う。

```kimi
struct Box<T>
struct Box<T, U>

let a = Box<i32>.init()               // Valid: selects Box<T>.
// let b = Box.init()                 // Error: two generic references.
let c = Option.Some(1)                // Valid: Option<i32>.
```

<a id="s9-7-1"></a>
#### 9.7.1. 束縛済みコンテナーパス

コンテナー参照は3段階で解決する。まずパス（ロール、アクセス、各要素の引数個数）、次に字句的な代入または基底の代入を伴う型引数とOrigin引数、最後に各修飾子と対象の検証を行う。後の失敗によって前の層を開き直すことはない。

外側の型引数を省略できるのは、環境がそれを供給する場合だけである。`Outer<T>`内での`Inner<i32>`は`Outer<T>.Inner<i32>`を意味する。外側の型引数を期待型や入力から推論することはない。`Outer<i32>.Inner.init(value)`が推論するのは`Inner`自身のスロットだけである。

継承されたコンテナーは定義元の宣言を保つ。`open struct Base<T>`が`struct Node`を含み、`Derived : Base<i32>`である場合、`Derived.Node`は`Base<i32>.Node`となる。コンテナーは、アクセス可能な祖先コンテナーと型名前空間の名前が同じなら、引数個数に関係なく衝突する。

<a id="s9-7-2"></a>
#### 9.7.2. パス上のOrigin

入れ子の宣言のOriginスキーマは、自身と継承したスロットを保持し、その型引数、フィールド、基底のスロットは保持しない。接尾辞は、その出現箇所の完全な束縛集合を指定し、[§15.3](#s15-3)に従って補完される。Contract参照、エイリアス対象、コンテナー修飾子には完全なOrigin契約が必要である。推論や継承された束縛から得られない関係は、外側の宣言に付加する。式単位の制約ブロックや暗黙の`static`はない。

`(Outer<T>{outer}).Inner<U>{inner}`は値を作らずに途中の修飾子のOriginを束縛し、途中の義務も保持する。束縛集合のラベルは字句スコープを持ち、型引数ではない。

<a id="s9-7-3"></a>
#### 9.7.3. 入れ子の型と関連型の射影

型パラメーターは、同じ名前空間の入れ子の宣言と衝突する。祖先の名前を隠しても、継承したスロットは取り除かれない。Origin探索は、スカラー名、束縛集合名、値のキャリアーを最も近いスコープごとに合わせて探索し（[§15.3.4](#s15-3-4)）、ロールの違う候補を越えて外側を探し直すことはない。

入れ子の型は関連型ではない。`T.(C).Element`はContract `C`を選択し、`T.C.Element`は入れ子のパスとなる。`T.Element`が両方の方法で解決できる場合は曖昧となる。関連型候補を重複排除する基準は、定義元の宣言と束縛済みContract参照だけである。

---

<a id="s10"></a>
## 10. オーバーロード解決と推論



この章では、呼出し、関数参照、構築が使用する宣言を選択し、その宣言自身の型・Semantics・長さ・Originの引数を推論する。コンテナー修飾子は先に解決する（[§9.7.1](#s9-7-1)）。省略された外側のコンテナー環境を推論することはなく、後の失敗によって修飾子の探索を再開することもない。引数のAdaptation、期待する結果との互換性、取得の合法性は、別々に判定する（[§3.8](#s3-8)）。関連型の推論（[§8.5.5](#s8-5-5)）は、この章の規則を変えない。

<a id="s10-1"></a>
### 10.1. 候補の適用可能性

解決では、まず確定した関数グループがパラメーター取得形状を満たすかを検査する（[§7.4](#s7-4)）。その後、各宣言を次の順で検査する。

1. 明示的な型引数の数と種類を検査する。
2. 引数をパラメーターに対応付け、使用する既定値を記録する（[§10.1.1](#s10-1-1)）。
3. 呼出しシグネチャの照合も含め、レシーバと引数から型引数を推論する（[§10.8](#s10-8)）。
4. 独立に既知である期待結果型から、確定済みの型引数を変えずに、残る型引数を埋める。
5. 代入後、引数のAdaptation（[§10.2](#s10-2)）、裸のPlaceの値渡し取得に必要なCopyの証明（[§3.5](#s3-5)）、制約、条件付きメンバーの前提を検査する（[§7.5](#s7-5)、[§8.8](#s8-8)）。
6. 期待する結果がある場合は、使用位置がその結果を受け入れられない候補を除外する（[§10.3](#s10-3)）。

待機する引数（[§10.5.1](#s10-5-1)）は候補ごとには検査せず、同節の規定に従って検査する。

**適用可能性がUnknownの場合。** 手順5で制約や前提がUnknownの場合、その候補の適用可能性は未証明のままとする。未証明の候補は、適用可能であるものとして比較する。ある候補が厳密に最良なら（[§10.4](#s10-4)）、それを選択する。その候補の各Unknown判定は選択後に検査する義務となり、確定性の規則の下でProvenにならなければエラーとなる（[§8.3](#s8-3)）。厳密に最良の候補がなければ、呼出しは曖昧となる（エラー）。選択を先送りしたりやり直したりせず、別の候補を勝たせるために未証明の候補を除くこともない。

適用可能な候補がなければエラーとなる。候補の検査ではCopy、Move、借用、既定値の評価を行わない。宣言のエラーは別の候補が成功してもエラーのままとなる。裸のPlace引数の取得モードは、どの候補が残るかに依存しない（[§7.4](#s7-4)）。Callableの効果境界は候補の絞込みにも順位付けにも使わず、選択した呼出しまたは関数参照でその義務を満たす（[§8.11.6](#s8-11-6)）。失敗しても選択し直さない。

```kimi
func keep<T>(value: T) -> ()
    T is Owned
func keep<T>(value: T, tag: i32 = 0) -> () => ()
func store(view: ref/i32)
    keep(view)     // The first is strictly best (fewer defaults); Error: T is Owned is not proven.
    keep(view, 1)  // The second: the only candidate with two parameters.
```

<a id="s10-1-1"></a>
#### 10.1.1. 引数の対応付け

- 呼出し先にタスクスロットがある場合に限り、タスク引数を最初に書く（[§24.2.2](#s24-2-2)）。それ以外はエラーとなる。タスク引数はパラメーターを束縛しない。
- 位置引数は名前付き引数より前に置き、宣言順にパラメーターへ束縛する。直接呼出しでは`!`境界より前のパラメーターだけが対象となる（[§7.2.1](#s7-2-1)）。位置による対応付けでは、名前が必須のパラメーター、既定値を持つパラメーター、後で名前付きで渡されるパラメーターを飛ばさず、型による探索も行わない。メソッド呼出し（[§7.3.3](#s7-3-3)）では、先にレシーバの位置を取り除く。
- 名前付き引数は外部名を使い、順序を変えられる。各パラメーターへの束縛は最大1回とする。未知のラベルや余分な位置引数はエラーとなる。
- 束縛されなかったパラメーターには既定値を使う。既定値もなければ呼出しはエラーとなる。既定値は推論の証拠を供給しない。
- 明示的な引数をソース順に評価し、その後、既定値をパラメーター順に評価する（[§7.2.3](#s7-2-3)）。関数値の呼出しでは、すべての引数を位置で渡す。

```kimi
func scale(value: i32 ! by => factor: i32) -> i32 => value * factor
scale(by: 4, value: 3) // by is evaluated before value.

func configure(! mode: i32 = 0, count: i32 = 1) => ()
configure(count: 3)    // mode takes its default.
configure(3)           // Error: mode requires its name; matching does not skip to count.
```

<a id="s10-2"></a>
### 10.2. 共通Adaptation

共通Adaptationは、値を確定した期待型に合わせる。引数、型注釈付きの初期化子、代入元、値で返す結果、集約値の要素、enumのペイロード、既定値、および確定した対象結果型の結果源に適用する（[§14.9.2](#s14-9-2)）。期待型を借用や値の読取りから逆算することはない。期待型のない位置では裸の取得を使う（[§3.5](#s3-5)）。

`U`は完全な期待型である。共有参照と値の読取りの行では、条件を満たすペア層を含め、複数の層をたどれる（[§3.4.3](#s3-4-3)、[§13.5.5](#s13-5-5)）。

| 入力 | 期待型 | 操作 |
| --- | --- | --- |
| `U`を格納する読取り可能なPlace。ただし参照スロットとハンドルスロットを除く | `ref/U` | 新しい共有借用 |
| `owner`の`U`の一時値 | `ref/U` | 一度だけ実体化して共有借用 |
| `U`に至る安全な値参照の層 | `ref/U` | `U`への1つの共有参照 |
| `uniq/U` | `uniq/U` | 排他再借用 |
| 読取り可能な`obj/U`、`rc/U`、`arc/U` | `objref/U` | オブジェクトの共有借用。参照数は変わらない |
| `objuniq/U` | `objuniq/U`または`objref/U` | オブジェクトの再借用 |
| 読取り型`U`に至る安全な値参照の層 | `U` | 値の読取り（[§3.5.3](#s3-5-3)） |
| Function Itemまたは具体的なクロージャー | 共通関数型`U` | 型消去（[§7.7.4](#s7-7-4)） |
| その他の値またはPlace | その完全な型 | 裸の取得または移譲（[§3.5](#s3-5)） |

**参照スロットまたはハンドルスロット**とは、型が参照（`ref`、`uniq`、`objref`、`objuniq`）または所有オブジェクトハンドル（`obj`、`rc`、`arc`）であるPlaceをいう。最初の行はこれを暗黙に借用しない。`clone(handle@ref)`と書く。他の行による参照先やオブジェクトのAdaptationは行える。たとえば`obj/U`から`objref/U`へのAdaptationである。`Weak<S>`を含むその他の名前付き型には、最初の行を使う。

明示的な取得は暗黙のAdaptationより先に行う。`@move`は値と責任を移譲し、取得元のPlaceをMovedにする。後続のAdaptationがこの移譲を置き換えたり取り消したりすることはない。移譲された排他参照が期待型に直接合う場合は、再借用せず、そのまま渡す。それ以外では、取得済みの値の完全な型とカテゴリーから1行を選ぶ。裸の入力では元の型とカテゴリーを使う。暗黙のAdaptationは最大1回である。格納された`uniq/U`を`ref/U`に合わせる場合は、共有参照の行を直接使う。明示的な取得は暗黙のAdaptationには数えない。通常の一時値の寿命、Loan、型消去の規則を適用し、Originの関係は選択後に判定する（[§15.6.1](#s15-6-1)）。

**複数層を通る1つの共有参照。** `ref`層がある場合は、最も内側の`ref`をCopyし、その内側にある各`uniq`層を共有再借用する。Originはそれらの層のOriginの交わりとなり、外側の層は依存を追加しない。`ref`層がない場合は、すべての層を通る共有再借用となる。各層で初期化、能力、Loanを検査する。共有参照と再借用の行は、参照を保持する一時スロットへの依存を追加しない。

**それ以外の暗黙操作はない。** オブジェクトのペイロード追跡、`rc`／`arc`の複製、一時値の排他借用、オブジェクトのアップキャスト、数値変換、ユーザー定義変換を暗黙には行わない。Non-CopyまたはCopyが未証明の裸のPlaceを値として取得することはない。ただし、格納された排他参照は裸の取得によって再借用される。そのCopyが必要な候補は適用不能となる。所有Placeを新たに排他借用するには、レシーバ式（[§7.3](#s7-3)）を除き、`@uniq`または`@objuniq`が必要となる。そのため、裸の所有Placeや排他参照経由で到達したPlace（[§15.1.5](#s15-1-5)）が`uniq`または`objuniq`パラメーターに合うことはない。型注釈によって寿命や能力が増えることはない。

**一時値。** 所有する一時値は、通常の寿命を持つ一時Placeへ一度だけ評価する（[§3.6](#s3-6)）。借用のために寿命を延ばすことはない。`let view: ref/Node = makeNode()`の後で`view`を使うとエラーとなる。

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

<a id="s10-2-1"></a>
#### 10.2.1. リテラル

リテラル引数は[§12.3](#s12-3)の規則に従って候補の型に合わせる。位置と範囲の形式には、それぞれ固有の規則を使う。幅の狭い型を優先しない。

リテラルの既定型（`i32`、`f64`、長さの文脈では`isize`。[§12.3](#s12-3)）を適用するのは、最良候補を選択した後、選択した候補に対してだけである。適用可能性の検査と比較の間、リテラル引数（[§10.5.1](#s10-5-1)）だけを証拠とするスロットは未束縛のままとする（[§10.8](#s10-8)の「未束縛のスロット」）。そのため、[§10.4](#s10-4)の手順2では候補の組を比較不能とする。既定型で曖昧さを解消することはない。既定型の適用後、選択した候補でそのスロットを必要とする検査（型合わせ、制約）には既定型を使う。失敗はエラーとなり、選択し直さない。既定型の適用後も未束縛のスロットがあればエラーとなる。

期待型のない式には既定型を直接使う。`null`、空のコレクション、型のない関数には代替の型がない。

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

<a id="s10-2-2"></a>
#### 10.2.2. 推論とAdaptationのクラス

型の推論ではCopy、Move、借用を行わない。

1. 明示的な型引数を先に束縛する。残りは、レシーバと引数の元の完全な型からまとめて推論し、走査順には依存しない。裸の`T`には引数の型を束縛する。ただし、パラメーターが読取り型（[§3.5.3](#s3-5-3)）の場合は参照層の末端の参照先を束縛し、引数の値を読み取る。宣言された`ref/T`または`uniq/T`は、`T`を所有引数の型、または借用の最外層の参照先に束縛する。ペア層（[§13.5.5](#s13-5-5)）は借用ではない。要素型が`s/U`の場合、`inspect(c[i])`は`T = s/U`を束縛してスロットを借用し、`inspect(c[i]@follow)`は`U`を束縛する。
2. 独立に既知である期待結果型から、型の照合により未束縛の部分だけを埋める。借用や値の読取りを逆算せず、確定した型も変えない。
3. 各引数の取得とAdaptationは[§3.5](#s3-5)と[§10.2](#s10-2)に従って計画する。直接期待型に合う排他参照は、裸なら再借用し、`@move`では移譲する。

```kimi
// n: ref/i32; r: uniq/Node; identity<T>(value: T) -> T; double<T> with T is PrimitiveInteger.
let a = identity(n)        // T = ref/i32.
let b = identity<i32>(n)   // Value read at the fixed i32.
let c = identity(r)        // T = uniq/Node; an exclusive Reborrow, like let saved = r.
let e = double(n)          // T = i32; n is value-read.
```

Adaptationは次のクラス順に優先する。同じクラス内での優劣はない。

| クラス | 意味 |
| --- | --- |
| 完全一致 | 借用・再借用を含め、Adaptation操作がない |
| リテラルの型合わせ | リテラル引数（[§10.5.1](#s10-5-1)）を候補の型へ直接合わせる。一時値の借用などの取得も必要な場合は、その取得のクラスを適用する |
| 同一Semanticsの再借用 | 入力の外側のSemanticsを保つ再借用 |
| 異種SemanticsのAdaptation | その他の借用・再借用、複数層を通る1つの共有参照、値の読取り |

完全一致は型についての判定である。値渡しの引数でも、裸ならCopyし、`@move`なら移譲するが、両者の優劣はない。必要な排他再借用を完全一致とすることはない。権限と、Placeか一時値かという形式は適用可能性の検査で確認する。初期化とLoanの衝突は選択後に検査し（[§10.6](#s10-6)）、呼出し借用予約も適用する（[§15.6.7](#s15-6-7)）。ジェネリック本体の取得と順位付けは、定義時に宣言された制約に基づいて検査し、インスタンス化時には選択し直さない。型消去はどのクラスにも属さない。1つの引数に対する2つの型消去は同等であり、型消去と他の行は比較不能となる。

<a id="s10-3"></a>
### 10.3. 期待する結果

期待する結果は推論を補完できる（[§10.1](#s10-1)の手順4）。候補を除外するのは、その既知の結果に対する取得もAdaptationも使用位置が認めない場合だけである。候補の順位付けには使わない。選択前に判定するのは期待型に合うかどうかの構造部分だけであり、Originの関係は選択後に判定する（[§15.6.1](#s15-6-1)）。ただし、Originを含む制約（`Owned`、`Callable`、型の同一性）は引き続き判定に関わる。候補を残すための変換は挿入しない。呼出しの結果を捨てても、Unitという期待型は与えない（[§14.2](#s14-2)）。

| 使用位置 | 既知の結果に適用する規則 |
| --- | --- |
| 確定した期待型がある | 共通Adaptation（[§10.2](#s10-2)）と、その位置での寿命検査 |
| 期待型がない | 裸の取得（[§3.5](#s3-5)） |
| 明示的な操作、または結果を捨てる位置 | その操作の規則 |

未知の結果`T`を、Adaptation後の型から逆算して推論することはない。

```kimi
func fetch(value: i32) -> string => "text"
func fetch(value: i64) -> i64 => value
let text: string = fetch(1) // fetch(i32): only its result fits.
fetch(1)                    // Error: discarding leaves both candidates.

func read(x: i32, from: ref/i32) -> ref/i32 => from
func read(x: i64, from: ref/i32) -> i32 => 0
// let other: i32 = read(1, limit@ref) // Error: both results fit, by a value read or exactly.
```

<a id="s10-4"></a>
### 10.4. 最良候補

候補を2つずつ比較し、優位、劣位、同等、比較不能のいずれかを得る。同等の組だけが次の手順へ進む。候補を選択するのは、他のすべての適用可能な候補より優位な場合だけである。

1. 明示的なソース引数ごとにAdaptationのクラスを比較する（[§10.2.2](#s10-2-2)）。レシーバは除く（[§7.3](#s7-3)）。Aがどの位置でもBに劣らず、いずれかで優位なら、AがBに優越する。異なる位置で互いに優位なら比較不能となる。名前付き引数はソース式で対応付ける。既定値は除き、コストを合計することはない。
2. 各ソース引数について、手順1で対応付けた代入済みパラメーター型の構造部分を比較する（Originは無視する）。既定値で埋めたパラメーターは比較しない。すべて同一なら次へ進み、それ以外は比較不能とする。
3. 長さパラメーターを含め、自身のジェネリックパラメーターを持たない関数を優先する。外側の型がジェネリックかどうかは数えない。
4. 使用する既定値が少ない方を優先する。
5. それ以外は曖昧な呼出しとなる（エラー）。

幅、制約の強さ、パターンの一般性、宣言順で候補を順位付けすることはない。前の手順で比較不能だったものが、後の手順で比較可能になることもない。下の2つの`inspect`のキーは重ならない（[§7.4](#s7-4)）。

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

<a id="s10-5"></a>
### 10.5. 推論の境界

- ローカルの型は宣言時に確定する。固定長配列の型注釈の穴（[§4.3.1](#s4-3-1)）は、その初期化子から解決する。
- 名前付き関数の結果は明記した型またはUnitである（[§7.1](#s7-1)）。本体や呼出し側からシグネチャを推論することはない。匿名関数は推論する（[§7.7.1](#s7-7-1)）。
- 明示的特殊化は推論や適用可能性の判定に関与しない。元の宣言とその引数が決まった後で1つを選ぶ。
- 代入は、入れ子のものも含め、束縛されたOriginとLoan要件を保ち、借用を作らない（[§15](#s15)）。
- `try`のオペランドには期待型がない（[§17.3](#s17-3)）。明示的に結果を捨てる式にも期待型がない（[§14.2.4](#s14-2-4)）。

<a id="s10-5-1"></a>
#### 10.5.1. 引数の種類

呼出しの各引数は、次のいずれか1つに分類する。ただしタプルリテラルと配列リテラルの引数自体は分類せず、各要素を要素位置の引数として1つずつ再帰的に分類する。

- **リテラル**：型のないリテラル、リテラルだけの式（[§12.3](#s12-3)）、または結果源（[§14.9](#s14-9)）がすべてこれらである制御フロー式。各候補のパラメーター型をすべての結果源へ伝える。[§10.2.2](#s10-2-2)のリテラルの型合わせの行で順位付けする。スロットの証拠にはならず、既定型は選択後に適用する（[§10.2.1](#s10-2-1)）。
- **独立に型付け可能**：それ以外で、呼出しからの期待型もリテラルの既定型も使わずに完全な型が確定する引数。たとえば、型のある値やPlace（明示的な`@`操作を伴うものも含む）、`S`を必要としない関数参照（[§10.5.3](#s10-5-3)）。入れ子の呼出しが該当するのは、期待結果型なしで適用可能な候補がちょうど1つある場合だけである。匿名関数は該当しない。その型は外側の候補のスロットに対する証拠となる。
- **待機**：その他すべての引数。たとえば匿名関数、`S`を必要とする関数参照、独立に型付けできない入れ子の呼出し。残る外側の候補に共通する期待型、または選択した候補の期待型だけに対して検査し、外側のスロットの証拠にはしない。

タプルリテラルと配列リテラルの引数は候補ごとに型に合わせる（[§4.3.1](#s4-3-1)）。リテラル要素はその位置の型に合わせ、証拠にはしない。独立に型付けできる要素はその位置のスロットの証拠となる。待機する要素はその位置で待機する引数と同様に検査する。リテラル全体を受けるスロットをそこから束縛できるのは、すべての要素が証拠となる場合だけである。

内側の呼出しを外側の候補ごとに解決することはない。除外した候補を復活させることもない。待機する入れ子の呼出しを、共通の期待型でも選択済みの候補でも型付けできない場合は、型注釈、明示的な型引数、または型付きの束縛が必要となる。匿名関数は[§10.5.4](#s10-5-4)に従う。

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

<a id="s10-5-2"></a>
#### 10.5.2. 呼出しシグネチャ

型の**既知の呼出しシグネチャ**とは、その型の値を呼び出す際のシグネチャをいう（[§7.7.3](#s7-7-3)）。Function Itemでは束縛された引数を代入した宣言のシグネチャ、具体的なクロージャーでは内部シグネチャ、共通関数型ではその構造、ジェネリックパラメーターではその`Callable`制約が与える唯一のシグネチャとなる（[§8.10](#s8-10)）。匿名関数が供給するのはヘッダーに明記したパラメーター型と結果型だけであり、タスクスロットを持つのはそれを明記した場合に限る（[§24.2.1](#s24-2-1)）。

ある位置に**確定した期待呼出しシグネチャ**`S`があるのは、期待型が共通関数型の場合（このとき`S`はその型）、または期待型が`F`、`ref/F`、`uniq/F`で、未束縛スロット`F`のすべての`Callable`制約が同じシグネチャ`S`を与える場合である。外側に複数の候補がある場合は、すべてに共通する`S`だけを使う。`S`内のスロットは[§10.8](#s10-8)での未知数となり、未束縛スロットのない部分だけが引数の型付けを導く。

```kimi
func make<T>(action: () -> T) -> T => action()
func makeLarge() -> i64 => 2
let large = make(makeLarge)               // T = i64 from makeLarge's signature; then erased.
let written = make(func () -> i64 => 2)   // T = i64 from the written result Type.
// let open = make(func () => 2)          // Error: only the body could fix T.
```

<a id="s10-5-3"></a>
#### 10.5.3. 関数参照

参照の候補は、そのグループの宣言のうち[§10.1](#s10-1)の手順1を通過したものとする。型で修飾されたインスタンス参照は、`self`を通常のパラメーターとして扱う（[§7.3](#s7-3)）。候補が2つ以上残る場合、または明示的な引数で束縛されない自身のスロットを持つ候補がある場合、その参照は**`S`を必要とする**。そのような参照は`S`がなければエラーとなる。`S`を必要としない参照は独立に型付け可能である（[§10.5.1](#s10-5-1)）。

`S`を必要とする参照は、`S`のすべてのパラメーター型が閉じた時点で選択する。各候補は、自身のパラメーター型を`S`のパラメーター型と、自身の結果を`S`の結果の閉じた部分と照合して、自身のスロットを束縛する（[§10.8](#s10-8)）。Adaptationは行わない。すべてのスロットが束縛され、制約と前提が[§10.1](#s10-1)の手順5を満たし（Unknownの判定は同節のUnknownの適用可能性の規則に従う）、代入後のシグネチャが引数個数とタスクスロットの有無も含めて`S`の閉じた部分と互換である場合に、適用可能となる（[§10.7](#s10-7)）。候補は[§10.4](#s10-4)で比較し、手順1と4は同等とする。

選択後、unsafeと`drop`の禁止、および型消去の条件を検査し（[§7.6](#s7-6)、[§7.7.4](#s7-7-4)）、選択し直すことはない。`F`、`ref/F`、`uniq/F`では、`F`を型消去なしでFunction Item型に束縛する。`ref/F`では一時値を借用する。選択したシグネチャで、`S`の開いた結果を埋めることはない。

`T is Comparable`の下での`T.compare`のように、制約のある型を介して参照する要件は、`Self`と関連型を代入した要件のFunction Itemとなる。その呼出しは実装へ到達する（[§8.6](#s8-6)）。インスタンス化後も要件のFunction Itemのままである。`Comparable.compare`は無効である。

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

<a id="s10-5-4"></a>
#### 10.5.4. 匿名関数

明示的な型、独立に型付け可能な引数（[§10.5.1](#s10-5-1)）、制約は、引数の順序に関係なく匿名関数の本体より先に処理する。ヘッダーの引数個数と型は候補を絞り込め、証拠にもなる。本体は、明記されたシグネチャ、残る候補に共通する期待型、または選択した候補のシグネチャに対して検査する。`F`、`ref/F`、`uniq/F`では、確定した期待呼出しシグネチャ（[§10.5.2](#s10-5-2)）が省略されたパラメーター型を導き、`F`を型消去なしで具体的なクロージャー型に束縛する。候補に共通の期待型がなければ、本体を使わずに比較する。本体に必要な部分を期待型が与えられなければ、型注釈が必要となる。本体で候補を選択したり、本体の検査をやり直したりすることはない。後の使用からパラメーターを推論することもない。

本体の検査前に戻り値型がUnitである場合、単一項目の本体の結果は捨てる（[§7.1](#s7-1)）。それ以外では本体を値文脈とする。本体から推論した戻り値型は匿名関数だけに属し、外側への証拠にはしない（[§10.5.1](#s10-5-1)）。本体検査前に別の引数がUnitを確定させた場合も該当する。後の推論で本体を解釈し直すことはなく、型付きの関数値の戻り値をUnitへ消去することもない。

```kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()
run(func () => compute())        // Error: the candidates differ in expectation; the body never selects.
run(func () -> i32 => compute()) // The written result Type selects the second.
```

<a id="s10-5-5"></a>
#### 10.5.5. 推論後の制約

制約は[§8.3](#s8-3)で判定する。Provenなら満たし、Refutedなら拒否し、Errorなら無効な証拠を報告し、Unknownなら[§10.1](#s10-1)の手順5に従う。ジェネリックな能力は定義の検査時に証明する（[§8.15](#s8-15)）。`T is C`を証明できないことは、`T is not C`の証明にはならない。メンバーは定義環境を保ち、呼出し側のインポートを使わない（[§18](#s18)）。定理証明、型の列挙、制約の強さによる順位付けは行わない。除外された構文内の宣言は候補集合に入らない（[§19.4](#s19-4)）。条件付きメンバーは探索対象に残り、適用可能性だけに影響する（[§7.5](#s7-5)、[§8.8](#s8-8)）。

<a id="s10-6"></a>
### 10.6. 使用の合法性と演算子

選択後に、unsafeの許可、初期化、Move状態、Loan、寿命、アクセサーへのアクセス、書込み能力を検査する。失敗はエラーとなり、選択を変えることはない。そのため、より良いオーバーロードを追加すると、既存の呼出しが無効になることがある。

実装は解決処理の量に上限を設けてもよい。上限を超えた場合はエラーとなり、何かを選択してはならない。

演算子候補は、組込み操作、比較の対応付け（[§13.4.1](#s13-4-1)）、および自身で選択を決める算術Contract（[§13.3.4](#s13-3-4)）が定義する。添字式の候補はIndexable Contractが定義する。キーが一意に合う適合を1つ選び、順位付けや期待結果型による絞込みは行わない（[§4.7.1](#s4-7-1)）。

```kimi
unsafe func inspect(value: i32) -> () => ()
func inspect(value: i64) -> () => ()
let number: i32 = 1
inspect(number) // Error: inspect(i32) is selected, then rejected outside an Unsafe Block.
```

<a id="s10-7"></a>
### 10.7. Callableシグネチャの互換性

共通関数型への型消去（[§7.7.4](#s7-7-4)）と`Callable<r, S>`（[§8.10](#s8-10)）は、この規則を使う。実装`(A1, ..., An) -> R`が要件`(P1, ..., Pn) -> Q`と互換であるのは、タスクスロットが両方にあるか両方にないこと（[§24.2.4](#s24-2-4)）、引数個数が等しいこと、各位置で`Pi <: Ai`であること（[§3.8](#s3-8)）、`R`が`Q`と結果互換であることをすべて満たす場合である。ラベルや既定値で不一致を埋めることはできない。

**結果の互換性。** 実装の結果`R`が要件の結果`Q`と互換であるのは、`R <: Q`または`R`がNeverの場合である。Contract実装の照合、詳細化の衝突検査、Callableの互換性、共通関数型への型消去は、すべてこの1つの関係を使う。ある共通関数型の値が別の共通関数型に関係するのは部分型関係による場合だけであり、Neverが期待型に合うこと（[§3.8](#s3-8)）は、部分型関係とは別の関係である。

OriginとLoanは、正規のOrigin契約で比較する（[§15.3.7](#s15-3-7)）。要件側の量化子は固定し、実装の呼出し時Originだけを推論する。互換性のために借用、変換、型消去を挿入することはない。直接の一致と型消去は比較不能である（[§10.2.2](#s10-2-2)）。

```kimi
contract Empty
    func empty() -> Self

struct Impossible
    Self is Empty
    public func empty() -> Never => $abort("no value")   // OK: Never satisfies Self.
```

<a id="s10-8"></a>
### 10.8. ジェネリック引数の推論

明示的な引数を先に束縛する。その後、型、Semantics、長さ（[§4.4](#s4-4)）、Originの制約を、レシーバと独立に型付け可能な引数（[§10.5.1](#s10-5-1)）からまとめて集める。期待する結果は未束縛の部分だけを埋める。相関のあるスロットに妥当性が依存するグループを明示的な型引数なしで呼び出す場合は、[§7.4](#s7-4)が要求する各相関を成立させなければならない。

**呼出しシグネチャの照合。** 確定した期待呼出しシグネチャ`S`を持つ各パラメーター（[§10.5.2](#s10-5-2)）では、`S`を引数の既知の呼出しシグネチャと照合する。`F`、`ref/F`、`uniq/F`の場合は、引数が`F`に束縛する型の既知の呼出しシグネチャと照合する。タスクスロットの有無、構造、Semanticsは正規化された同一性によって、Originは限定された推論（[§15.3.6](#s15-3-6)）によって照合し、Adaptationや探索は行わない。この方法で照合するのは未束縛スロットを含む部分だけである。他の部分は[§10.7](#s10-7)の要求どおりに合わなければならない（共通関数型では型消去、`F`ではCallable制約）。したがって、ヘッダーの型は互換であればよい。呼出しごとのOriginでスロット内部のOriginを解くことはない。`(T) -> ()`を`(ref/i32) -> ()`と照合すると`T = ref/i32`となり、そのOriginは他の証拠で決める。

**解法。** 照合の失敗、または構造やSemanticsの証拠の衝突は、候補を適用不能にする。Originの証拠で適用不能にすることはない（[§15.6.1](#s15-6-1)）。明示的な型引数はそのまま保つ。それ以外では、スロットの解は不変の位置での束縛となる。そのような束縛はすべて構造とSemanticsが一致し（不一致なら候補は適用不能）、Originも等しくなければならない。不変の束縛がなければ、共変の束縛の交わりを解とする。各引数のOriginが解に合うかは選択後に判定する。`T.Element`のような未解決の関連型射影を逆算することはない。`value: T.Element`に型`i32`の引数を渡しても`T`は束縛されず、`witness: Tag<T>`などの別のパラメーターが束縛しなければならない。走査順、変換の連鎖、共通基底の探索、制約の強さで解を選ぶことはない。待機する引数の確定したクロージャーは、外側のスロットを埋めない。型Neverの引数や要素は証拠を供給しない。それを含むタプルリテラルや配列リテラル全体を受けるスロットに対しても同様である。これはNeverが[§14.9.2](#s14-9-2)で候補を供給しないためである。ただし、その位置の期待型には引き続き合う（[§3.8](#s3-8)）。

**Semantics。** 期待型が`s/T`である場合、`n@s`のリテラルオペランドは`T`に合わせる（オブジェクト対象を除く。[§13.6.2](#s13-6-2)）。型付きのオペランドは自身の型を保つ。未束縛の`s`は、変換を探索せず、取得元の外側のSemanticsから推論する。同じ`s`に対する`owner`と`ref`の証拠は衝突する。`s is reference`はSemantics射影を検査し、ペアの`T is C`は対象射影を検査する。通常の`T`は完全なSemanticsを保つ。

**未束縛のスロット。** [§10.1](#s10-1)の手順3と4が完了しても証拠によって束縛されないスロット（リテラルの既定型はまだ適用しない。[§10.2.1](#s10-2-1)）、たとえばリテラルや待機する引数でしか決められないスロットがあっても、不一致とはしない。候補は他の検査に基づいて適用可能なままとし、そのスロットを必要とする制約は判定しない。そのスロットを含むパラメーター型は、他のどの型とも同一ではなく、どの型の部分型でもない。待機する引数によって束縛するパラメーター`F`、`ref/F`、`uniq/F`は、この種のスロットではない。同じ形式同士は[§10.4](#s10-4)の手順2で等しいと比較する。`X = ref/X`のような無限の代入はエラーとなり、循環は証拠を供給しない。

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

<a id="s10-8-1"></a>
#### 10.8.1. コンストラクターの型推論

構築では、構造体の宣言と環境を確定した後、その構造体自身のスロットに[§10.8](#s10-8)を適用する（[§9.7](#s9-7)）。期待する結果がスロットを供給するのは、同じ宣言の所有型との直接の構造照合による場合だけであり、借用、オブジェクト作成、アップキャスト、変換、関連型射影を逆算することはない。既定値、フィールド初期化子、コンストラクター本体は証拠を供給しない。リテラル引数の型合わせと既定型は[§10.2.1](#s10-2-1)に従う。enumのケースは[§6.5.2](#s6-5-2)に従う。

**固定束縛検査。** 推論による構築を受け入れるのは、推論した構築型を明示的に固定しても、元の引数の対応付けと型のないリテラルを用いて、元のグループから同じコンストラクターが選ばれる場合だけである。それ以外では明示的な型引数が必要となり、候補を切り替えない。待機する引数が別の文脈を必要とする場合も、明示的な型引数が必要となる。待機しているというだけで、唯一の候補を拒否することはない。

```kimi
func wrap<T>(value: T) -> Box<T>
    return Box.init(value@move)   // T from the argument.
// For C<T>.init(x: ref/i32, y: ref/T) and init(x: ref/T, y: ref/T),
// C.init(x, y) with x: ref/(ref/i32) and y: ref/i32 is an error:
// inference picks the first, but with C<i32> fixed both are ambiguous.
```

<a id="s10-9"></a>
### 10.9. 対象外の形式

実装は、コンストラクター自身のジェネリックパラメーター、構造体の長さパラメーター、[§10.2](#s10-2)以外の暗黙のAdaptation、[§10.6](#s10-6)以外の演算子候補、明示的なオーバーロード選択構文を追加してはならない。この版の対象外となるその他のジェネリック形式は、[§D.2](#sd-2)に示す。

---

<a id="s11"></a>
## 11. プロパティ



<a id="s11-1"></a>
### 11.1. プロパティの種類

具体的なプロパティには、`let`（不変の格納領域）、`var`（可変の格納領域）、`computed`（アクセサーのみ）がある。Contractの`property`要件は操作を約束する（[§11.9](#s11-9)）。

| 宣言 | `get` | `set` | 宣言時の初期化子 |
| --- | --- | --- | --- |
| `let` | 省略時は標準 | 禁止 | インスタンスでは任意、静的では必須 |
| `var` | 省略時は標準 | 省略時は標準 | インスタンスでは任意、静的では必須 |
| `computed` | 本体が必須 | 任意。指定する場合は本体が必須 | 禁止 |
| Contractの`property` | 必須 | 任意 | 禁止 |

- インスタンスプロパティはstructに、静的プロパティはgroupまたはrootgroupに属する。struct内に入れ子になったgroupはインスタンスフィールドを追加しない。そのプロパティは静的なままである（[§22.4.4](#s22-4-4)）。enumはプロパティを持たない。ローカルの`let`／`var`は束縛であり、ローカルの`computed`は禁止する。
- プロパティは値名前空間を共有する。属性は[§6.7](#s6-7)に従う。インラインの`has`は要件だけに使える。
- **格納プロパティ**とは、**フィールド**と呼ぶ格納スロットを1つ持つ`let`または`var`をいう。格納プロパティであることは宣言で決まり、アクセサーの本体では決まらない。ソースからのフィールドアクセスは、すべてそのプロパティのアクセサーに従う。

| 型の契約 | 意味 |
| --- | --- |
| 格納プロパティの`: T` | 格納型 |
| 格納プロパティの独自の`get`／`set` | `get`の結果は正確に`T`で、Copyが証明済み。`set`の入力は正確に`T`で、結果はUnit |
| computedまたはContractの`: T` | ゲッターの結果型。computedまたは明示的な要件のセッター入力は異なってもよい |

型は完全な構造とOriginの束縛で比較する。Copy能力は取得方法を選び、`T`と`ref/T`のように異なる結果型を選ぶことはない。

```kimi
struct Counter
    public let limit: i32 = 100
    public var count: i32 = 0
    public computed remaining: i32
        get() -> i32 => self.limit - self.count
```

<a id="s11-2"></a>
### 11.2. 標準アクセス

本体のない`get`または`set`は標準アクセスを選ぶ。一方のアクセサーを独自にしても、他方は変わらない。標準`get`はPlaceアクセスであり、ゲッター呼出しではない。取得は[§3.5](#s3-5)と[§15.1.5](#s15-1-5)に従い、借用の前に値を取得することはない。標準`set`は、通常の状態と後処理の規則に従って、格納領域を初期化または置換する。

フィールドへの操作には、そのフィールド自身と、そのパス上の各祖先の格納プロパティで、次の標準アクセサーにアクセスできる必要がある。

| 操作 | フィールド自身 | 各祖先の格納プロパティ |
| --- | --- | --- |
| Copyまたは共有借用 | `get` | `get` |
| Move | `let`：`get`。`var`：`get`と`set` | そのプロパティ自身のMoveの行 |
| 排他借用 | `get`と`set` | `get`と`set` |
| 書込み | `set` | `get`と`set` |

独自アクセサーはこれらの要件を満たさない。独自の`get`を介した読取りと借用は、隠された格納領域ではなくゲッターの結果を使う（[§11.5](#s11-5)）。独自の`set`を呼ぶのは、最終的な代入先がそのプロパティである代入だけである。そのプロパティを通る子のパスへの書込み、Move、排他借用は、メソッドレシーバや演算子のAdaptationも含めてエラーとなる（上表）。通常の能力、初期化、Origin、Loan、Moveパスの規則も適用する。単純代入には、最終的な代入先の`set`が必要となる（[§13.7](#s13-7)）。

借用は[§13.5.5](#s13-5-5)に従う。型`ref/U`のフィールドに対する`@ref`は、そのフィールドを`ref/(ref/U)`として借用する。裸の取得、または期待型が`ref/U`の場合は、格納された参照をCopyする（[§10.2](#s10-2)）。`@follow@ref`は参照先を再借用する。

```kimi
// position: Point is Copy, with standard get and custom set.
// object.position.x = 10     // Error: the ancestor position needs a standard set.
var next = object.position
next.x = 10
object.position = next        // OK: calls position's set.
```

<a id="s11-2-1"></a>
#### 11.2.1. Moveと可変性

`var`からMoveするには、アクセス可能な標準`set`が必要となるが、これを呼び出すことはなく、書込み可能なレシーバも必要としない。`let`のスロットには`set`がないため、排他借用も書込みもできず、Movedになった`let`を再初期化することもできない。スロットの不変性は深い不変性ではない。格納された参照の参照先は、自身の能力を保つ。

```kimi
// resource: public standard get, private standard set; holder is owned.
let view = holder.resource@ref    // OK: shared storage borrow.
// let r = holder.resource@move   // Error: set is inaccessible here.
```

<a id="s11-2-2"></a>
#### 11.2.2. フィールドのパスと部分Move

安全なフィールドのMoveには、所有するstructのPlace、対象として認められる静的なMoveパス、構築の完了、Initializedかつ完全な対象、および祖先の`drop`条件が必要となる。借用レシーバ、オブジェクトレシーバ、静的格納領域、破棄レシーバ（[§16.3.1](#s16-3-1)）からは、安全に取り出せない。生ポインター操作には引き続きUnsafe規則を適用する。

継承されたフィールドには、型引数とOrigin引数を代入した一意の基底パスを通って到達する。このパスは途中の基底を取得せず、`ref/Derived`を`ref/Base`へ変換せず、レシーバのカテゴリーを保ち、privateアクセスを与えない。基底部分オブジェクトを単独でMove、置換、再構築することはできない。

標準アクセスでは、兄弟へのMove後も、残る完全なPlaceを使える。独自アクセサー、computedアクセサー、Contract要件のアクセサーには完全なレシーバが必要である。呼出し側は、その本体や実装から非重複性を推論しない。

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

<a id="s11-2-3"></a>
#### 11.2.3. ジェネリックフィールド

標準アクセサーにはCopy制約が不要である。格納プロパティの独自ゲッターには、宣言のジェネリックな前提の下で`T is Copy`が証明されている必要がある。独自セッターは、許されるすべての`T`について有効でなければならない。Copyが未証明の場合、フィールドの裸の読取りはエラーとなる（[§8.14](#s8-14)）。`T is Copy`で制約するか、フィールドを借用するか、`@move`でMoveする。Moveすると集約値は不完全になる。MoveやLoanの検査が失敗しても、オーバーロード選択をやり直さない（[§10.6](#s10-6)）。

```kimi
struct Box<T>
    public var item: T

func take<T>(box: Box<T>) -> T
    // let x = box.item          // Error: T is not proven Copy.
    return box.item@move          // OK: box is incomplete afterwards.
```

<a id="s11-3"></a>
### 11.3. アクセサー関数

独自アクセサーとcomputedアクセサーは、パラメーター型と結果型を明記する。省略できるのはレシーバだけである。アクセサーには共通の本体形式を使う（[§14.2](#s14-2)）。セッターは単一項目の式の結果を捨て、Unitで完了する。パラメーターの既定値と`!`境界は禁止する。セッターの`value`は不変の束縛であり、通常の取得と後処理に従う。

格納プロパティの独自アクセサー内では、文脈依存の束縛`storage`が、アクセサーを再度呼ばずにフィールド自身を指す。通常のPlace規則に従い、再宣言はできない（[§9.3](#s9-3)）。computedプロパティと要件には`storage`がない。

```kimi
struct Gauge
    public var level: i32 = 0
        get() -> i32
            return storage
        set(value: i32) -> ()
            // let storage = value    // Error: storage cannot be redeclared here.
            storage = if value > 100 => 100 else => value
```

<a id="s11-3-1"></a>
#### 11.3.1. アクセサーのレシーバ

アクセサーのレシーバは操作によって固定され、本体では決まらない。インスタンスの`get`は`self: ref/Self`を、インスタンスの`set`は`value`の前に`self: uniq/Self`を受け取る。省略した場合はこれらを挿入する。静的アクセサーにはレシーバがなく、明記するとエラーとなる。インスタンスの`get`のレシーバは`ref/Self`、インスタンスの`set`のレシーバは`uniq/Self`でなければならない。`get(self: ref/Self during static) -> T`のようなOrigin注釈は認める。それ以外のレシーバは、`Self`、逆の借用形式、`obj`、`rc`、`arc`、`objref`、`objuniq`の各形式、Coreが`Self`以外のものも含め、エラーとなる。

**ゲッターのレシーバ。** 独自、computed、または要件の`get`を介する読取り`x.p`は、[§7.3.3](#s7-3-3)の`ref/Self`の行に従い、`x`を共有モードのレシーバ式として取得する。排他予約を開始せず（[§15.6.7](#s15-6-7)）、書込み可能な貸出し点も必要としない（[§15.1.5](#s15-1-5)）。そのため、`let`束縛、所有パラメーター、`rc`／`arc`のペイロード、可変の静的格納領域からも読み取れる。

**セッターのレシーバ。** `x.p = v`、複合代入、`++`、`--`は、書込み権限を伴って対象を特定し（[§13.7.1](#s13-7-1)）、独自、computed、または要件の`set`のレシーバを`uniq/Self`として取得する。CopyやMoveでは取得しない。`objuniq/T`は[§7.3](#s7-3)を通じてこの要件を満たす。`holder@move.item = value`と`point@copy.x = 10`は、それぞれが作る一時値またはCopyを更新する（[§3.6](#s3-6)）。複合更新は[§13.7.2](#s13-7-2)に従って更新先を保護する。

レシーバへの排他アクセスを必要とする操作、またはレシーバを消費する操作は関数とし（[§7.3](#s7-3)）、[§4.9.2](#s4-9-2)の命名ペアに従って名付ける。

オブジェクト借用と基底部分オブジェクトへの射影を介する呼出しには、ObjectCallCompatibleがProvenであることが必要となる（[§12.7](#s12-7)）。`ref/Self`アクセサーは形状によってProvenとなる（[§12.7.1](#s12-7-1)）。`uniq/Self`レシーバだけでは証明にならない。

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

<a id="s11-3-2"></a>
#### 11.3.2. アクセサーのアクセス可能性

アクセサーのアクセスは、厳密に狭いものを指定しない限り、そのプロパティと同じになる。アクセスは`public`、`protected internal`、`protected`または`internal`（この2つに順序はない）、`private protected`、`private`の順に狭まる。同じ、または広い修飾子の指定はエラーとなる。`set`のアクセス範囲が`get`のアクセス範囲に含まれる必要はない。

<a id="s11-3-3"></a>
#### 11.3.3. Non-Copy値の独自セッター

`storage`への代入は通常の代入であり（[§13.7.1](#s13-7-1)）、古い値を破棄して新しい値を置く。排他レシーバは古い値をMoveして取り出せない。古い値の共有借用や、フィールドを初期化済みのまま保つ操作は使える（[§15.7](#s15-7)）。セッターはレシーバが完全な状態で戻る。格納内容を変えなくてもよい。消費しなかった`value`は呼出し側へ返さず、破棄する。

```kimi
struct Holder
    public var item: Resource
        set(self: uniq/Self, value: Resource) -> ()
            storage = value@move

holder.item = makeResource()
inspect(holder.item@ref)          // OK: standard shared access.
// let item = holder.item@move    // Error: the custom set blocks a Move.
```

<a id="s11-4"></a>
### 11.4. computedプロパティ

computedプロパティには、格納領域、初期化子、本体のないアクセサー、インラインの`has`リストがない。Origin補完後のゲッターの結果は、ヘッダーの型と一致する。任意のセッターはUnitを返し、別の入力型を取ってもよい。代入はセッターの入力型に合わせる（[§13.7.1](#s13-7-1)）。

**結果。** ゲッターは、新しい値、格納された参照のCopy、または`self`の借用を返せる。共有レシーバは、所有するNon-Copy部分を取り出したり、格納された`uniq/T`を排他的に再借用したりできない（[§13.5.5](#s13-5-5)）。getとsetを往復した値の等価性は保証しない。

```kimi
struct Holder
    private var item: Resource
    public init(item: Resource) => self.item = item@move
    public computed view: ref/Resource
        get() -> ref/Resource => self.item@ref

let holder = Holder.init(makeResource())
inspect(holder.view)                     // OK: a let binding is readable.
```

<a id="s11-5"></a>
### 11.5. ゲッターの結果

独自、computed、またはContractの`get`は関数の結果を生成する。その所有する一時Placeと、そこにインラインで含まれる子孫には、代入、複合更新、インクリメント、デクリメント、排他借用を行えない。括弧、射影、排他レシーバ取得（[§7.3](#s7-3)）、`match`や`for`のSubjectとしての使用（[§15.1.6](#s15-1-6)）でもこの制限を保つため、結果に対して`uniq/Self`メソッドを呼び出せない。結果に対する追加のゲッターは使える。返された参照は、その参照先自身の能力を保つ。`set`を通じたプロパティの更新は引き続き可能である。

この制限は、その一時Placeだけに属する。そこからローカルや引数へ取得した値は、取得先の規則に従う（[§3.6](#s3-6)）。借用によって一時値の寿命が延びることはない（[§3.6](#s3-6)）。標準`get`の場合、`@ref`は代わりに格納領域を借用する。

```kimi
// position: Point is Copy, with custom get and standard set.
// object.position.x = 10        // Error: updates only the getter temporary.
inspect(object.position@ref)     // OK: the temporary lasts through the call.
let view = object.position@ref
// inspect(view)                 // Error: the initializer temporary has ended.
```

<a id="s11-6"></a>
### 11.6. プロパティの型とOrigin

格納プロパティの型は型注釈から決め、型注釈がなければ宣言時の初期化子の型とする。両方ともないプロパティはエラーとなる。アクセサーや後の代入から型を推論することはない。初期化子の型付けにその型が必要な場合は、直接必要な場合、別の推論されるプロパティを通じて必要な場合、そのプロパティを含む型の事実（Copy、Owned、レイアウト）を通じて必要な場合のいずれでも、そのプロパティに型注釈が必要となる（なければエラー）。推論される初期化子間の循環はエラーとなる。

格納領域は必要なOriginをすべて明示的に束縛する（[§15.4](#s15-4)）。`self`が自己借用する格納領域を作ることはない。アクセサーはOriginリストではなく、借用注釈を通じて呼出しごとのOriginを導入する（[§15.3.4](#s15-3-4)）。付加する`origin`句は、除外される構文も含めたソース順で（[§19.4](#s19-4)）、アクセサーの実行項目より前に置く。プロパティ自身の句はアクセサーより前に置く。継承したOrigin名は再宣言できない。

格納プロパティの独自アクセサーの`value`と結果では、省略したOriginを関数の省略規則より先に格納型`T`から継承する（`self`からではない）。明記された束縛はそれに対して検査する。どのアクセサーパラメーターも、格納領域が許す呼出しを狭めてはならない。computedと要件のアクセサーには、通常の関数の省略規則を使う。借用入力が`self`だけのゲッターでは、結果のOriginを省略して`self`にできる。

```kimi
group Tags
    public func tagOf<T>() -> i32
        T is Owned
        return 0

struct Entry
    var tag = Tags.tagOf<Entry>()        // Error: proving Entry is Owned needs tag's inferred Type.
    // var tag: i32 = Tags.tagOf<Entry>() // OK: the annotation breaks the cycle.
```

<a id="s11-7"></a>
### 11.7. 構築と破棄

宣言時の初期化子とコンストラクターの最初の配置は、アクセサーを呼ばずに自身の格納領域を直接初期化する。最初の配置には、流入するすべての経路で未初期化であり、最初の配置をまだ一度も完了していないことが必要となる。Moveや破棄はこの履歴をリセットしない。宣言時の初期化子を持つフィールドは、初期化済みと数える。

| コンストラクターでの書込み | すべての経路で最初の配置前 | 初期化済み、または経路が混在 |
| --- | --- | --- |
| `let` | 直接初期化 | エラー |
| 標準`set`を持つ`var` | 直接初期化 | 通常の配置または置換 |
| 独自の`set`を持つ`var` | 直接初期化 | エラー |

構築レシーバは、独自アクセサーやcomputedアクセサーを呼べない。標準`get`では、初期化済みの自身の格納領域をCopyまたは借用できる。Non-CopyのMove、継承した格納領域へのアクセス、`self`全体の取得や借用は禁止する。[§6.3](#s6-3)のその他の規則も適用する。レイアウト、Copyの導出、暗黙の構築、部分Move、破棄が使うのは格納フィールドと基底の構成要素だけである。破棄はアクセサーを呼ばない。

```kimi
// In a constructor: level has a custom set and no initializer.
if ready
    self.level = 100   // First placement on this path; no setter call.
else
    self.level = 200
// self.level = 300    // Error: construction cannot call the custom set.
```

<a id="s11-8"></a>
### 11.8. 静的格納領域

groupまたはrootgroupの格納プロパティには、初期化子と、Ownedである完全な格納型が必要となる（[§15.2.3](#s15-2-3)）。各スロットは実際の最初のアクセス時に初期化する（[§22.4](#s22-4)）。アクセサー呼出しは、その実行が触れるスロットだけを初期化する。

初期化済みの不変な静的格納領域の共有借用は、`during static`にできる。可変な静的格納領域の借用は、インラインの部分Placeも含め、本体によって制限される有限のOriginを持つ（[§15.6.5](#s15-6-5)）。可変な静的格納領域に格納された参照のCopyは、その格納領域の借用ではない。Copyは、格納された参照先とOriginを保つ。

```kimi
group Config
    public let limit: i32 = 100
    public var name: string = ""

func limitRef() -> ref/i32 during static
    return Config.limit@ref       // OK: immutable static storage.
// func nameRef() -> ref/string during static => Config.name@ref  // Error: mutable.
```

<a id="s11-9"></a>
### 11.9. Contractのプロパティ要件

`property`要件はインスタンス専用で、必須の`get`と任意の`set`を持つ。アクセサーのアクセス修飾子、属性、既定値、`!`境界、初期化子、格納領域、本体は持たない。`has get`は、`get(self: ref/Self) -> T`による値での結果を要求し、`has set`は`set(self: uniq/Self, value: T) -> ()`を要求する。明示的な形式では、[§11.3.1](#s11-3-1)のレシーバを持つシグネチャを書く。そのセッターの入力は`T`と異なってもよい。オブジェクトハンドルと基底への射影は、[§11.3.1](#s11-3-1)と[§9.6.1](#s9-6-1)の規定に従ってこれらのレシーバを満たす。

```kimi
contract Counted
    property count: i32 has get
contract ReplaceableItem
    property item: ref/Resource
        get() -> ref/Resource
        set(value: Resource) -> ()
```

<a id="s11-9-1"></a>
#### 11.9.1. 実装の互換性

通常のメンバー探索で実装を選択する。継承した適合はその対応付けを保つ（[§8.6](#s8-6)）。失敗しても、別の名前や基底を試さない。要件の`Self`、ジェネリック引数、関連型を代入した後、関数要件の互換性を適用する。暗黙の変換や、より強い事前条件は認めない。格納型はヘッダーの型と等しくなくてもよい。Unknownのジェネリック能力では互換性は成立しない。

<a id="s11-9-2"></a>
#### 11.9.2. 標準アクセスによる証拠

型`F`のフィールドへの標準アクセスが要件を実装するのは、次の橋渡しによる場合だけである。

| 要求する操作 | 標準の実装 |
| --- | --- |
| 共有レシーバ -> `F` | `F`がCopyと証明済みならCopy |
| 共有レシーバ -> `ref/F` | フィールドの共有借用 |
| 排他レシーバ、入力`F` -> Unit | `var`のアクセス可能な標準`set` |

各橋渡しには、[§11.2](#s11-2)でその操作が必要とする標準アクセサーが、適合の宣言箇所でアクセス可能であることが必要となる。結果のOriginはレシーバによって制限される。

独自の`get`の背後にある格納領域は使えず、それ以外のものを合成することもない。継承されたフィールドには、その適合経路の前提の下で標準の射影（[§11.2.2](#s11-2-2)）を使って到達し、基底部分オブジェクトのレシーバは使わない（[§9.6.1](#s9-6-1)）。橋渡しは探索候補を追加しない。

```kimi
contract Viewable
    property item: ref/Resource has get

struct Slot
    Self is Viewable
    public var item: Resource    // OK: shared borrow bridge.
    // A by-value `property item: Resource has get` would need Resource to be Copy.
```

---

<a id="s12"></a>
## 12. 式



式は値を生成するか、制御を転送する。構文は[§F.9](#sf-9)で定める。

<a id="s12-1"></a>
### 12.1. 分類と文脈

正常に完了する式は、Unitの場合も含め、型付きの結果を生成する（[§3.1.3](#s3-1-3)）。値文脈と結果を捨てる文脈（[§14.2](#s14-2)）は、結果の使い方を決める。結果を捨てても、その効果と、型・所有権・破棄に関するすべての検査は保つ。

インデントされた本体は式ではない。複数の操作から値を得るには、選択式または`do`式を使う。`unsafe`、`defer`、`require`、`_ = expression`は文である。`let`と`var`の宣言は、式でも条件束縛でもない（[§14.2.3](#s14-2-3)）。

```kimi
_ = compute()           // Discard: effects and checks are kept.
// let x = (let y = 1)  // Error: a declaration is not an expression.
```

<a id="s12-2"></a>
### 12.2. 評価順序

構文が別の順序を定めない限り、オペランドは左から右へ1回ずつ評価する。

| 式 | 順序 |
| --- | --- |
| `a() + b() * c()` | `a`、`b`、`c`、`*`、`+` |
| `receiver().method(a(), b())` | レシーバ、呼出し先の解決、`a`、`b`、呼出し |
| `array()[index()]` | 対象、添字、要素アクセス |
| `(a(), b())`、`[a(), b()]` | ソース順に各要素 |
| `[key(): value()]` | エントリーごとにキー、重複検査、値、挿入（[§12.4.3](#s12-4-3)） |
| `start()..end()` | 開始、終了。省略された境界は評価しない。どの境界も検査しない |
| `"\(a()) \(b())"` | 各埋込みを順に評価して書き出す。失敗すると残りを止める |

`and`、`or`、選択は、必要なオペランドや分岐だけを評価する。代入は右辺を先に評価する（[§13.7](#s13-7)）。型引数、長さ引数、Adaptation対象は実行時に評価しない。

通常以外の完了、発散、Abortが起きると、後続のオペランドと外側の操作を飛ばす。飛ばされる構文にも、名前、型、制御転送先の検査は行う。`#if`／`#switch`によって除外された構文は[§19](#s19)に従う。

```kimi
let ok = ready() and check()  // check() runs only when ready() is true.
```

<a id="s12-3"></a>
### 12.3. リテラルの型付け

<a id="s12-3-1"></a>
#### 12.3.1. 期待型と既定型

束縛、パラメーター、結果に宣言された型は期待型となり、式の内部（コレクションの要素、選択の分岐）へ伝わる。それ以外では、型はオペランドから決まる。組込みの数値演算には、同じ数値型のオペランドが必要となる。幅、符号の有無、整数型と浮動小数点型を暗黙に混在させることはない。`bool`、`char`、数値の間に暗黙の変換はなく、条件は`bool`でなければならない。

型のない整数リテラルは、値が収まれば、期待される整数型を採用する。浮動小数点リテラルは、期待される`f32`または`f64`を採用する。それ以外では整数リテラルの既定型を`i32`とし（型を得る他の手掛かりがない範囲境界と`^`オペランドも含む。[§4.6.2](#s4-6-2)）、浮動小数点リテラルは`f64`、長さの文脈のリテラルは`isize`とする（[§4.2](#s4-2)）。既定型に収まらないリテラルには明示的な型が必要となる。呼出しで既定型を適用するのは候補選択後だけであり（[§10.2.1](#s10-2-1)）、候補比較中や適合選択中には適用しない（[§4.7.1](#s4-7-1)）。整数が暗黙に浮動小数点型に合うことはない。

```kimi
let a: i64 = 10
let b = a + 20       // 20 adopts i64.
let c: i32 = 3
let d = a + c@i64    // A typed operand is converted explicitly.
let e = a + c        // Error: i64 and i32 do not mix.
```

<a id="s12-3-2"></a>
#### 12.3.2. 直接リテラル

**直接リテラル**とは、数値リテラル、またはそれに接頭符号`+`か`-`を1つ直接適用したものをいう（間に空白があってもよい）。全体を任意個の括弧で囲んでもよい。直接リテラルが型に合うのは、表す符号付きの値がその型の値である場合である。その型が単項`+`や`-`を定義しているかは問わない。`-(1)`のように、それ以外に符号を適用する場合は単項演算子となり、2つのオペランドの間の`-`は二項演算子となる。

```kimi
let a: i8 = -128              // Direct literal: fits.
let b: i8 = ((- 128))         // The same direct literal.
let c: i8 = -(128)            // Error: 128 does not fit i8.
let d: u32 = -0               // The value 0 fits.
let e: u32 = -1               // Error: -1 is not a u32 value.
let f: Wrapping<u8> = -(1)    // Unary - of Wrapping<u8> applied to 1.
```

<a id="s12-3-3"></a>
#### 12.3.3. リテラルだけの式

リテラルだけの式は、型付け上の分類である。

- **整数**：型のない整数リテラル、または整数のリテラルだけのオペランドに対する、括弧、単項`+`／`-`、算術演算、ビット演算、シフト演算。整数型とラップアラウンド整数型に合う。
- **浮動小数点**：型のない浮動小数点リテラル、または浮動小数点のリテラルだけのオペランドに対する、括弧、単項`+`／`-`、二項`+`、`-`、`*`、`/`。`f32`と`f64`に合う。
- **位置／範囲**：整数のリテラルだけのオペランドを持つ`^a`（[§4.6.1](#s4-6-1)）、または明記した境界が整数か末尾からの位置のリテラルだけである範囲（[§4.6.2](#s4-6-2)）。

それ以外はリテラルだけの式ではなく、分類を混在させることもできない。`1 + 1.0`は無効である。

リテラルだけの式が型に合うのは、各直接リテラルがそれに伝わる型に収まり、各演算子がその型に定義されている場合である。整数リテラルは、ラップアラウンド整数型の整数引数に収まる場合、そのラップアラウンド整数型に合う。型は、算術演算子とビット演算子の両オペランド、単項オペランド、各範囲境界へはその`S`または`E`として（[§4.6.2](#s4-6-2)）、`^`のオペランドへは`FromEnd<T>`の`T`として、シフトでは左オペランドだけへ伝わる。シフト数は独立に型付けする（[§13.3.1](#s13-3-1)）。数値変換では、直接リテラルだけを対象の型に合わせる（[§13.5.4](#s13-5-4)）。したがって、`(200 + 100)@u8`は`i32`で計算する。

型に合わせる処理が変えるのは型付けだけである。末端と演算子を検査し、計算後の値は検査しない。値によって候補を選ぶこともない。式は合わせた型で、操作ごとに実行時評価する。`127 + 1`を`i8`に合わせると実行時にAbortし、`Wrapping<i8>`に合わせると`-128`になる。浮動小数点演算は各段階で丸める。必須の定数評価には固有の規則を保つため、`[(4 / 0) of u8]`はコンパイル時エラーとなる。長さの形式（[§4.2](#s4-2)）、ConstantIndexExpression（[§15.1.3](#s15-1-3)）、リテラルパターン（[§14.8.1](#s14-8-1)）は拡張しない。

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(2 * 5)                            // Error: both candidates fit in the same way.
let wide: Range<i64, i64> = 0..(1 << 40) // 1 is i64; the count 40 is i32.
let f = (1.0 + 1.0)@f32                  // Computed in f64, then converted.
```

<a id="s12-4"></a>
### 12.4. 基本式

<a id="s12-4-1"></a>
#### 12.4.1. 名前、リテラル、グループ化

| 形式 | 意味 |
| --- | --- |
| `name` | 可視の束縛、関数、またはその他の名前付き実体。 |
| `Self.member`、`isize.MaxValue` | 現在の型またはプリミティブ型の静的メンバー（[§9.6](#s9-6)）。 |
| `123`、`1.5`、`true`、`'€'`、`"text"` | リテラル（[§2](#s2)）。 |
| `"value = \(value)"` | 埋込み文字列（[§12.4.2](#s12-4-2)）。 |
| `null` | 文脈によって型付けする生nullポインター（[§5.1](#s5-1)）。 |
| `()` | Unit値。 |
| `(value)` | グループ化。Placeを保つ。 |
| `(value,)`、`(a, b)` | タプル。 |
| `[a, b]`、`[]`／`[key: value]`、`[:]` | 配列／Dictionaryリテラル。 |

配列は1つの要素型を、Dictionaryは1つのキー型と1つの値型を持つ。空のリテラルには期待型が必要となる。配列とDictionaryのリテラルは、必須のKimi宣言を使う（[§22.1](#s22-1)）。ただし、期待型が固定長配列の場合は除く（[§4](#s4)）。要素型が解決できなくても、汎用のオブジェクト型に切り替わることはない。

```kimi
let pair = (10, "ten")
let names = [1: "one", 2: "two",]
let empty: Array<i32> = []
let unknown = []        // Error: an empty literal needs an expected Type.
```

<a id="s12-4-2"></a>
#### 12.4.2. 埋込み文字列

埋込み文字列リテラルは、所有する`string`を生成する。各埋込みを`Utf8Writer.write`の共有入力に合わせ（[§10.2](#s10-2)）、選択した参照先の型に`Utf8Format`を要求する。借用型はこれを転送しない。埋込みにある裸のPlaceはMoveしない。評価と書式化は[§22.10](#s22-10)に従う。`$tryWrite(writer, literal)`は[§22.10.4](#s22-10-4)に従う。

```kimi
let name = "Ada"
let count = 3
let line = "name = \(name), count = \(count)"
```

<a id="s12-4-3"></a>
#### 12.4.3. Dictionaryリテラル

Dictionaryリテラル内で等価なキーが重複するとエラーとなる。

**静的検査。** 真偽値、整数、`char`、通常の文字列、Unitのリテラルキーを、括弧の有無に関係なくキー型に合わせ、コンパイル時に値で比較する（整数キーは直接リテラルでもよい。[§12.3.2](#s12-3-2)）。すべてのエントリーを通じて、到達不能な構文内でも検査する。それ以外のキーは検査せず、定数の`let`束縛や演算子式も対象にしない。最適化によって対象を増やすこともない。

**実行時検査。** その他のエントリーはソース順に処理する。キーを1回評価し、等価なキーがすでに挿入されていれば、この値や後続のエントリーを評価する前にAbortする（[§17.4](#s17-4)）。なければ値を1回評価して挿入する。正常に完了しないエントリーは挿入せず、後続のエントリーも処理せず、部分的なDictionaryも結果にしない。完了済みの効果は残る。

**キーの等価性**にはEquatableの対応付けを使い、`stored.equals(search)`として呼び出す。キー型にHash適合は不要である。等価性の呼出し順序と回数は未規定で、各呼出しの効果は保つ。`f32`と`f64`のキーでは、同じ型のすべてのNaN値を等価とし、正負のゼロも等価とする。これは`==`を変えない（[§13.4.1](#s13-4-1)）。ユーザー定義のキー等価性は同値関係でなければならず、Dictionaryがそのキーを保持、検査、挿入している間は変わってはならない。違反すると結果が未規定になったりAbortしたりすることがあるが、メモリー安全性は損なわない。

```kimi
let x: i32 = getKey()
let map = [x: first(), x: second()] // Aborts at the second key; second() is not called.
let bad = [1: "a", (1): "b"]        // Error: duplicate literal key.
```

<a id="s12-5"></a>
### 12.5. アクセスと呼出し

<a id="s12-5-1"></a>
#### 12.5.1. メンバーアクセス

`expression.name`は修飾された探索でメンバーを選ぶ（[§9.6](#s9-6)）。`self`を挿入することはない。値参照にメンバーがない場合は、その参照先で選択を続ける（[§3.4.3](#s3-4-3)）。`.`の後にはメンバー名、またはタプル要素を選ぶ範囲内の十進整数を置く。動的なメンバー探索はない。型修飾子は束縛済みコンテナーパスであり（[§9.7.1](#s9-7-1)）、値を作らない。`.init(...)`は構築式であり（[§6.3](#s6-3)）、値のメンバー呼出しではない。

プロパティアクセスは[§11.1](#s11-1)に従う。生ポインターを暗黙にはたどらない。Unsafeブロック内で`(*pointer).name`と書く。

```kimi
let first = (10, "ten").0
var collection = makeCollection()
collection.count = 10   // Needs a setter and write permission.
```

<a id="s12-5-2"></a>
#### 12.5.2. 呼出しとジェネリック適用

`callee(a, b)`は関数、メソッド、関数値を呼び出す。末尾のコンマを認める。タスク呼出しでは、`callee(task; a)`のようにタスクを最初に書く（[§24.3](#s24-3)）。明示的なジェネリック引数は[§8.1.3](#s8-1-3)に従う。`<`が型引数の開始かどうかは[§F.9.3](#sf-9-3)で決める。

引数の対応付けと候補選択は[§10](#s10)に、名前付き引数と省略は[§7.2](#s7-2)に従う。関数値が受け取るのは位置引数だけであり、unsafe呼出しには引き続き[§7.6](#s7-6)を適用する。ジェネリック引数が埋めるのは型スロットと長さスロット（[§4.4](#s4-4)）だけであり、[§10.5](#s10-5)の範囲に従う。解決後、一致する明示的特殊化を選択する（[§8.13](#s8-13)）。`T(args)`はコンストラクターではない。`T.init(args)`と書く（[§6.3](#s6-3)）。

```kimi
let a = largest<i64>(1, 2)
let c = i<n            // A comparison: no matching >.
let d = Point(1, 2)    // Error: write Point.init(1, 2).
```

<a id="s12-6"></a>
### 12.6. オブジェクトのメンバー呼出し

メンバー、オーバーロード、アクセス、公開契約は、レシーバの実効型から選択する（[§14.10.1](#s14-10-1)）。非仮想呼出しはその実装を使い、仮想呼出しは同じスロットに対する動的型の実装を使う（[§6.4.3](#s6-4-3)）。レシーバは、選択した宣言の部分オブジェクトへ調整する（[§9.6.1](#s9-6-1)）。完全な派生型を隠す射影には、ビューのアップキャストに必要なOwned消去の証明が必要であり、選択後に検査する。ただし、オーバーライド本体内の字句上の`self`への操作は除く（[§6.4.5](#s6-4-5)）。

オブジェクト借用または基底部分オブジェクトへの射影を介する呼出しには、ObjectCallCompatibleがProvenであることが必要となる（[§12.7](#s12-7)）。ただし、**完全なSealedペイロード**は除く。View Targetがレシーバの完全なSealed型と正確に一致する場合、`ref/Self`または`uniq/Self`レシーバを、ペイロード追跡（[§13.5.5](#s13-5-5)）により暗黙に（[§7.3](#s7-3)）、または`handle@follow.method()`として取得し、通常の完全な値への呼出しを行う（排他的な呼出しには[§15.1.5](#s15-1-5)も必要）。引数では必ず明示的な`@follow`が必要となる。openなビューと基底部分オブジェクトでは、値全体の置換や、不完全な状態を残すMoveを行わない。

<a id="s12-7"></a>
### 12.7. オブジェクトレシーバの互換性

**ObjectCallCompatible**は、借用レシーバを使う呼出しが、レシーバの完全性、アクセス権限、型・Origin・Loanの契約を保つという公開保証である。各呼出し操作（関数、独自またはcomputedの`get`や`set`、標準Contractの証拠となる橋渡し）は、ProvenまたはNotProvenの状態を1つ持ち、インスタンス化ごとの状態は持たない。標準`get`と`set`はPlace操作であり、呼出しではない。`Box<T>.item`を`@ref`で借用するのに、`T is Copy`は必要ない。

操作のObjectCallCompatible状態は、公開契約の一部である。操作の本体とその明示的特殊化の本体に[§12.7.1](#s12-7-1)の効果規則を適用して得る最小不動点であり、その規則だけで決める。解析の精度や最適化では変わらない。したがって、本体の変更によって状態が変わり、オブジェクト借用や基底部分オブジェクトへの射影を介する呼出しを受け入れられるかも変わり得る。宣言元のKotonohaを利用する側は、この状態に依存する。オーバーロード選択後に検査し、候補の除外や再選択には使わない（[§7.3](#s7-3)）。呼出し側の前提、型引数、動的型、選択した特殊化で状態を強めることはできない。無効な本体はエラー、検証が未実装の本体はUnsupportedとなる（[§23.1.1](#s23-1-1)）。どちらもNotProvenではない。

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

<a id="s12-7-1"></a>
#### 12.7.1. 効果規則

許されるすべての型とOriginの束縛を、宣言のシグネチャ、制約、前提の下で検証する（[§8.3](#s8-3)〜[§8.15](#s8-15)）。本体から推論した制約の下では検証しない。型検査されるすべての経路を数える。

**格納領域の関係。** 効果の対象とレシーバまたは入力ルートの関係は、Whole（ルート自身）、Base（基底への辺だけを通る）、Part（フィールド・タプル要素・配列要素を通る）、Separate（[§15.6.2](#s15-6-2)のPlace重複規則でルートとの非重複が証明済み）、MayAlias（Whole、Base、Part、Separateのいずれでもない関係）のいずれかとする。格納された参照を通じて到達する参照先は、その参照が保持するLoanの指すPlaceを通じて関係付ける（[§15.6](#s15-6)）。あるルートからの分離は別のルートについて何も証明しない。ペイロード追跡によってWholeがPartやSeparateに変わることもない。

| レシーバへの効果 | 結果 |
| --- | --- |
| WholeまたはBaseを置換、再構築、所有権取得する | 違反。ただし対象が完全と証明され、操作が[§15.7](#s15-7)で合法なら除く |
| そこからMoveし、不完全な状態を残す | 後で復元しても違反 |
| Partの置換または交換、許可された読取りと借用、Separateな対象への効果 | 違反なし |
| 結果、格納、キャプチャー、呼出し先を介して、それ自身または基底への無制限の排他アクセスが流出する | 違反。ただし完全な対象の合法な借用なら除く |
| MayAlias、またはそこに到達し得るunsafeアクセス | 未証明の効果 |
| 呼出し、アクセサー、既定値、後処理 | 引数とキャプチャーを通じて呼出し先の効果を対応付ける |

呼出し先の全体置換を呼出し側のPartに対応付けても、その対象の基底部分オブジェクトとしての制限を保つ。許可された効果は、別名参照の可能性がある対象でも許可されたままである。

**到達する操作。** 数えるのは、レシーバとそこから派生するものを通じて到達する操作だけである。レシーバのLoanは呼出し全体で生存するため（[§15.6.4](#s15-6-4)）、別の経路（別の引数、キャプチャー、静的アクセス、効果が不明な呼出し先）からその格納領域に操作すると、その呼出しで拒否される（[§15.6.2](#s15-6-2)）。生のアクセスでは未定義動作となる（[§5.2.1](#s5-2-1)）。`ref/Self`レシーバから到達するのは読取りと共有借用だけである。そのため、すべての独自またはcomputedの`get`、共有の標準証拠橋渡し（[§11.9.2](#s11-9-2)）を含め、このレシーバを持つ操作は、有効である場合に限りProvenとなる。

**排他入力の境界。** 呼出し先が入力に及ぼせる効果の上限は、[§8.11.4](#s8-11-4)の規則1が許す範囲であり、結果はシグネチャが定める依存を保つ（[§15.6.4](#s15-6-4)）。`ref`入力にあるのは読取りと共有借用の効果だけである（[§15.6.2](#s15-6-2)）。排他入力では、unsafeコードを含め（[§5.2.3](#s5-2-3)）、実際の対象の完全性を保つ1回の置換が上限となる。呼出し先内部の未証明の効果も、この上限を超えない。`Kimi.Intrinsics.replace`と`exchange`はそのような置換、`swap`は交換である（[§15.7](#s15-7)）。この境界はレシーバのPartを保つが、WholeまたはBaseでは違反となる。ただし、操作が仮想スロットの完全性保証を持つ場合は除く（[§6.4.5](#s6-4-5)）。その保証が免除するのはこの境界だけであり、呼出しのその他の効果とLoanは引き続き数える。

**呼出し。** 効果は[§15.6.4](#s15-6-4)の要約領域を用い、再帰を含む最小不動点まで伝播する。再帰があるだけでは未証明にならない。間接呼出しとジェネリック要件を介した呼出しは、要件の効果境界を使う（[§8.11](#s8-11)）。Callable制約付きパラメーターを介する呼出しは、実際の引数を通じて合成する（[§8.11.6](#s8-11-6)）。検証済みの要約も要件の効果境界もない呼出しは、排他入力の境界を効果に加え、静的格納領域については[§15.6.4](#s15-6-4)の不明な効果を加える。レシーバのない関数は入力ルートへの効果を持つが、状態は持たない。不動点によって循環した適合が証明されることはない。確定性は通常どおり適用する。

<a id="s12-7-2"></a>
#### 12.7.2. 実装群

実装が[§12.7.1](#s12-7-1)を満たすのは、許されるどの束縛も違反や未証明の効果を生まない場合である。操作がProvenとなるのは、通常の本体と明示的特殊化集合内のすべての実装が、呼ばれるかどうかに関係なく検査を満たす場合に限る。それ以外はNotProvenとなる。この集合は環境選択と宣言収集の後に閉じる。通常の本体は、特殊化される束縛を除かず、許されるすべての束縛について検証する。各特殊化は代入済みの契約の下で検証する。互換でない特殊化は操作をNotProvenにするが、その理由だけではエラーにならない。宣言されたシグネチャ、制約、安全性、効果の義務は引き続き適用する。使用箇所は、実装選択前に元の公開契約に対して検査する（[§8.13.3](#s8-13-3)）。

---

<a id="s13"></a>
## 13. 演算子と代入



<a id="s13-1"></a>
### 13.1. グループ化と評価順序

優先順位と結合規則は文法で定義する（[§F.9](#sf-9)）。どのAdaptation対象の後に後置の接尾辞を置けるかは、[§F.9.4](#sf-9-4)で決める。グループ化はどのオペランドが演算子に属するかだけを決め、式を型に適合させたり、評価順序を変えたりはしない。オペランドの評価は[§12.2](#s12-2)に従う。`and`と`or`は短絡評価する（[§13.4](#s13-4)）。

```kimi
let total = f() + g()     // f's effects happen before g's.
let scaled = (a + b) * c  // Grouping chooses operands, not order: a, b, then c.
```

<a id="s13-2"></a>
### 13.2. 単項演算子

| 演算子 | オペランドと結果 |
| --- | --- |
| `+value` | 数値をそのまま返す。 |
| `-value` | 符号付き整数、ラップアラウンド整数、浮動小数点値の組込みの符号反転、または末端の型の`Negatable`要件（[§13.3.3](#s13-3-3)）。符号なし整数型ではエラー。 |
| `not value` | `bool`の否定。 |
| `*pointer` | 生ポインターの指すPlace（[§5.2](#s5-2)）。安全な参照とオブジェクトハンドルには`@follow`を使う（[§13.5.5](#s13-5-5)）。 |
| `^value` | 任意の`T is PrimitiveInteger`の`value`を格納する`FromEnd<T>`。解決時に検査する（[§4.6.1](#s4-6-1)）。 |
| `++target`、`--target` | 対象をインクリメントまたはデクリメントし、新しい値を返す。 |
| `target++`、`target--` | 対象をインクリメントまたはデクリメントし、古い値を返す。 |

インクリメントとデクリメントには、読取りと書込みが可能な整数型またはラップアラウンド整数型のPlaceかプロパティが必要となる。対象の特定、読取り、書込みをそれぞれ1回行う。演算とそのAbort検査には読取り型（`get`の結果型）を使い、式の結果も読取り型となる。書き込む値はセッターの入力型に合わせる（[§13.7.1](#s13-7-1)）。前置形式も対象を再び読み取らない。それ以外は複合代入に従う（[§13.7.2](#s13-7-2)）。

```kimi
var count: i32 = 1
let before = count++ // before = 1, count = 2
let after = ++count  // after = 3, count = 3
```

<a id="s13-3"></a>
### 13.3. 算術・ビット・シフト演算子

二項`+`、`-`、`*`、`/`、`%`と単項`-`は、組込み操作または算術Contract（[§13.3.3](#s13-3-3)）を使い、[§13.3.4](#s13-3-4)でその経路を選ぶ。ビット演算、シフト、インクリメント、デクリメントは組込みだけである。

<a id="s13-3-1"></a>
#### 13.3.1. 組込み数値演算子

組込み数値オペランド（[§13.3.3](#s13-3-3)）では、`+`、`-`、`*`、`/`は同じ型を要求してその型を返す。`%`が受け入れるのは整数型とラップアラウンド整数型だけである。整数の除算はゼロ方向に切り捨て、0でない剰余は被除数と同じ符号になる。

**整数の結果。** 整数型とラップアラウンド整数型（[§3.1.1](#s3-1-1)）では、算術演算、インクリメント、デクリメント、複合代入は数学上の結果を計算する。型がその結果を表せない場合、整数型ではAbortし、ラップアラウンド整数型では、ビット幅をNとして、2ᴺを法とした結果と合同な一意の値へ**ラップアラウンド**する。0による除算・剰余と、`0 <= count < N`の範囲外のシフト数は、両方の型でAbortする。Abortは定数評価の規則も含め、[§17.4](#s17-4)に従う。

| 式（32ビットの`MIN`、`MAX`） | `i32` | `Wrapping<i32>` |
| --- | --- | --- |
| `MAX + 1` | Abort | `MIN` |
| `MIN / -1` | Abort | `MIN` |
| `MIN % -1` | `0` | `0` |
| `x / 0`、`x % 0` | Abort | Abort |

ラップアラウンド整数型は、その整数引数と同じ演算子を持つ。二項演算の両オペランドはその同じ型でなければならない。すべてのラップアラウンド整数型は単項`-`を持つ。

**ビットとシフト。** `&`、`|`、`^`は、同じ整数型またはラップアラウンド整数型の2つのオペランドに対するビットAND、OR、XORであり、`bool`は受け入れない。`<<`と`>>`は左オペランドの型を返し、シフト数にはラップアラウンド整数型を除く任意の整数型を取る。左シフトは上位ビットを捨てる。右シフトは符号付きの値では符号拡張し、符号なしの値ではゼロ拡張する。ビットが捨てられることを、表現不能な結果とはしない。

**浮動小数点。** `f32`と`f64`は、無限大、NaN、符号付きゼロを含め、最近接・偶数丸めのIEEE 754に従う。0による除算はAbortしない。丸めやNaNの結果が変わり得る場合は、演算の結合順を変えたり演算を融合したりしない。

```kimi
let quotient = -7 / 3       // -2
let remainder = -7 % 3      // -1
let bits: u32 = 0b1010
let masked = bits & 0b0110  // 0b0010

func lowestBit(value: u32) -> u32
    let w = value@Wrapping<u32>
    return (w & -w)@u32     // -w wraps; the AND keeps the lowest set bit.
```

<a id="s13-3-2"></a>
#### 13.3.2. 文字列と生ポインター

`string`には算術・ビット・シフト・連結の演算子がない。直接、または安全な参照層を介した`string`オペランドを持つ`+`と`+=`は、相手がユーザー定義の提供型でもエラーとなる。文字列の結合には、所有する`string`を作り、埋め込む値を借用する埋込み文字列リテラルを使う（[§12.4.2](#s12-4-2)）。段階的なテキスト構築にはバイトバッファーを使う（[§22.9.2](#s22-9-2)）。生ポインターの算術演算は、[§5.3](#s5-3)の形式だけである。

```kimi
let first = "Hello, "
let joined = "\(first)world"   // An owning string; first is borrowed.
// let bad = first + "world"   // Error: strings are joined by interpolation.
```

<a id="s13-3-3"></a>
#### 13.3.3. 算術Contract

Kimiは次のpublicなContractを宣言する。綴りではなく宣言によって識別する。各Contractは、完全な値型である`associate Output`を宣言する。

| 操作 | 通常のContractと要件 | Left Contractと要件 |
| --- | --- | --- |
| `+` | `Addable<Rhs>.added` | `LeftAddable<Lhs>.addedFrom` |
| `-` | `Subtractable<Rhs>.subtracted` | `LeftSubtractable<Lhs>.subtractedFrom` |
| `*` | `Multipliable<Rhs>.multiplied` | `LeftMultipliable<Lhs>.multipliedFrom` |
| `/` | `Dividable<Rhs>.divided` | `LeftDividable<Lhs>.dividedFrom` |
| `%` | `RemainderProvider<Rhs>.remainder` | `LeftRemainderProvider<Lhs>.remainderFrom` |
| 単項`-` | `Negatable.negated` | なし |

通常の要件とLeft要件は下の形状を持つ。`negated`は`func negated(self: ref/Self) -> Self.Output`である。いずれも安全な、タスクではない関数で、既定値を持たず、値を返す。型がCopyかどうかに関係なく、入力は常に共有借用である。Left要件は`left op self`を計算し、通常の要件から生成されることはない。

```kimi
public contract Multipliable<Rhs>
    associate Output
    func multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output

public contract LeftMultipliable<Lhs>
    associate Output
    func multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output
```

**用語。** **組込み数値型**とは、数値型（[§3.1](#s3-1)）、`PrimitiveInteger`が証明された型パラメーター、またはそのようなパラメーターの`Wrapping<T>`をいう。**ユーザー提供型**とは、外側のSemanticsが`owner`であり、組込み数値型ではないstructまたはenumをいう。

**適格性。** 明示的に宣言する通常または単項の適合では、Selfがユーザー提供型でなければならない。明示的に宣言するLeft適合では、さらに`Lhs`が組込み数値型でなければならない。通常の`Rhs`には、外側のSemanticsが`owner`である任意の完全な値型を使える。タプルや借用を保持する型も含む。どちらの側も直接の`string`オペランドは禁止する。適合は[§8.6](#s8-6)に従いSelfに宣言する。外部からペアを登録する仕組みはない。定義の検査は許されるすべての束縛について適格性を証明し、使用側はその証明に依存できる。外側の`owner`は`Owned`能力ではない。

**Output**は関連型の推論で決め（[§8.5.5](#s8-5-5)）、本体からは決めない。必要な場合は`T.(Multipliable<R>).Output`と書く。Selfと異なっても、`Result`を含んでも、外部への借用を保ってもよい。CopyもOwnedも要求しない。これらのContractは、代数法則、純粋性、全域性、効果の保証を主張しない。

**組込みの適合。** 各数値型`N`は、相手の型とOutputを`N`として適合する。型を混在させた適合や、組込みのLeft適合はない。整数、`Wrapping<I>`、`f32`、`f64`は5つの二項Contractすべてを持つが、浮動小数点型には`RemainderProvider`がない。符号付き整数、`Wrapping<I>`、浮動小数点型は`Negatable`を持つ。証拠となる実装は、[§13.3.1](#s13-3-1)と正確に同じ動作をする。数値型に名前付きメンバーやコンストラクターは追加しない。要件に到達できるのは、`T.added`のようにContractを通じる場合だけである（下記「関数値」）。`T is PrimitiveInteger`は`T`の5つの二項適合を含意するが、`Negatable`は含意しない。このとき`Wrapping<T>`は6つすべてを持つ。含意される適合は、`Addable<T>`を要求する関数へ`T`を渡すなど、ジェネリックな使用に使う。演算子の経路選択は変えない（[§13.3.4](#s13-3-4)）。

**関数値。** `T is Addable<R>`の下で、`T.added`は選択した要件と実装を保つFunction Itemとなる。通常の規則に従い、呼出し、格納、変換ができる（[§7.7](#s7-7)、[§10.7](#s10-7)、[§8.11.6](#s8-11-6)）。

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

<a id="s13-3-4"></a>
#### 13.3.4. 経路選択

選択は静的に行う。オペランドの型は、構文、明示的な文脈、公開された前提から得る。安全な値参照の層は、能力、Origin、Loanを保ちながら末端の型までたどる（[§3.4.3](#s3-4-3)）。未知の型パラメーターや関連型は末端とする。オブジェクトや生のペイロードはたどらない。適合を探す前に経路を選ぶ。

| 末端のオペランド型 | 経路 |
| --- | --- |
| 両方が組込み数値型 | [§13.3.1](#s13-3-1)の組込み規則 |
| 左がユーザー提供型 | 左の型の通常のContract |
| 左が組込み数値型、右がユーザー提供型 | 右の型のLeft Contract |
| [§5.3](#s5-3)の生ポインター形式 | 生ポインターの規則 |
| その他 | エラー。提供元なし |

両方の末端型が組込み数値型なら、その演算の算術Contractの前提が明記されていても、組込み経路を適用する。その他の組では、`L is Addable<R>`のような直接適用できる証拠があれば、`L`を分類せず通常経路に固定する。数値オペランドが片方だけでは組込み経路に決まらない。`S is PrimitiveInteger, V is LeftMultipliable<S>`の下での`s * v`は、右の提供元を使う。同じオペランドの組について`L is Addable<R>`と`R is LeftAddable<L>`が前提にあると矛盾し、エラーとなる。

固定した提供元で、適用可能な適合の適格性を検査し、`ref/Rhs`または`ref/Lhs`に構造的に合わせる。オペランドの取得や、型付きオペランドの再型付けは行わない。同じ適合への経路は統合する（[§8.9.2](#s8-9-2)）。未決定の条件付き候補は除外しない。候補が複数あれば曖昧となる。候補の順位付けは行わず、独立した関数、拡張、変換、反対側の提供元を探すこともない。無効な適合はエラーであり、別の経路を選ぶ理由にはしない。選択後に取得、Origin、Loan、効果を検査し、結果を期待型に合わせる。失敗しても選択し直さず、期待結果型によって何かを選択することもない。

**リテラル。** リテラルの型合わせは[§12.3](#s12-3)、既定型は[§10.2](#s10-2)に従う。リテラルだけのオペランドは、固定した提供元の相手側の型に合わせる。リテラルからユーザー型を構築することはない。左オペランドが数値リテラルだけの式で、右に既知のユーザー提供型がある場合、左が数値の領域に属することが確定する。残るすべての候補に共通する相手側の型は、入れ子の呼出しや関数式の型付けを導ける（[§10.5](#s10-5)）。`Never`オペランドは方向も相手側の型も証明しない。

**ジェネリックの安定性。** 定義の検査で、経路、Contract、要件、Output、たどる層、取得方法を固定する。インスタンス化しても経路を切り替えない。参照をたどっても、参照自体に適合は与えられない。`value: ref/T`には`ref/T is Multipliable<f64>`ではなく、`T is Multipliable<f64>`を要求する。

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

<a id="s13-3-5"></a>
#### 13.3.5. 算術演算子の評価

左オペランドを右より先に評価して取得する。右側の提供元の呼出しは、`right.method(left)`への書換えではない。組込み経路は各オペランドの評価時に値を読み取る。Contract経路は数値の入力も含め両入力を共有借用し、右オペランドの評価中も左のLoanが生存する。明示的な`@move`と`@copy`は先に実行し、借用対象となる一時値を供給できる。結果は、演算だけのために作られたLoanに依存してはならない。そのようなLoanは演算完了時に終わる。Outputが`Result`なら結果も`Result`のままである。`try a / b`は`(try a) / b`なので、`try (a / b)`と書く。

<a id="s13-4"></a>
### 13.4. 比較演算子と論理演算子

`==`、`!=`、`<`、`<=`、`>`、`>=`は`bool`を返す。

- 数値オペランドは同じ型でなければならない。ラップアラウンド整数型の順序はその整数引数と同じであり、循環順序ではない。`char`はUnicodeスカラー値を比較し、`string`は正規化やロケールを使わずUTF-8バイト列を辞書順で比較する。`bool`とUnitには等価比較だけがある。
- 浮動小数点値では、`+0.0 == -0.0`はtrueとなる。NaNオペランドがある場合、`!=`はtrue、他の5つはfalseとなる。
- 対応するすべての要素が比較をサポートするタプルでは、等価比較は要素ごとに、順序比較は辞書順に行い、結果が決まるまで左から右へ進む。順序不能な要素の組があると、4つの順序演算子はすべてfalseになる。

安全な値参照の層（`ref`、`uniq`、条件を満たすペア層）を持つオペランドは、末端のPlaceを表す（[§3.4.3](#s3-4-3)）。たどるだけでは何も取得しない。末端型は等しくなければならず、または片方のオペランドが`Never`でなければならない。その型の比較を適用する。外側のOriginは一致しなくてもよい。比較オペランドには期待型がない（[§12.3](#s12-3)）。型のないリテラル、リテラルだけのオペランド、`null`だけを相手の末端型に合わせる。推論されるケース`.Name`や空のコレクションリテラルのように、期待型を必要とするオペランドはエラーとなる。

オブジェクトハンドル型（`obj/T`、`rc/T`、`arc/T`、`objref/T`、`objuniq/T`）には比較演算子がなく、ペイロードの適合も転送されない。ペイロードは、許可される場合に`@follow`を明示して比較する（[§13.5.5](#s13-5-5)）。オブジェクトの同一性比較はない。生ポインターの比較は[§5.1](#s5-1)に従う。ジェネリック本体で、`T is Equatable`（または`Comparable`）の下にある型`T`または`ref/T`のオペランドを比較する場合、`T = ref/f32`でもすべてのインスタンス化で`T`のContract対応付けを使う。`T = f64`では、組込み`==`ではなく、[§13.4.1](#s13-4-1)のNaNに対して反射的な`equals`を保つ。

**検査用Loan。** 比較は所有オペランドをMoveしない。Non-Copyの所有Placeを調べる組込み比較は、オペランドの評価時に共有Loanを作る。そのため、通常の衝突規則の下で（[§15.6.2](#s15-6-2)）、右オペランドの評価中も左のLoanが生存する。両オペランドはInitializedでなければならない。これらのLoanは比較の完了または中断時に終わり、`bool`の結果はどれも保持しない。

<!-- verified-example: comparison-inspection (positive; commented line is a rejection) -->
```kimi
func take(text: string) -> string => text@move

let text = "a"
let same = text == "a" // Shared inspection; text is not Moved.
// let invalid = text == take(text@move)
// Error: the left operand's shared Loan is live when take transfers text.
Console.writeLine(text) // Allowed: the completed comparison's Loan has ended.
```

**論理演算子。** `and`、`or`、`not`は`bool`を取り、`bool`を返す。`left and right`は`left`がtrueの場合だけ`right`を評価し、`left or right`は`left`がfalseの場合だけ評価する。

```kimi
let found = i < n and matches(values[i]) // Indexes only when in range.
if status == Status.Ready => start()
// if status == .Ready => start()            // Error: a comparison operand has no expected Type.
// a, b: rc/Point, with Point Sealed and Equatable.
let sameValue = a@follow == b@follow        // Compares payloads.
// let bad = a == b                          // Error: object handles have no comparison.
```

<a id="s13-4-1"></a>
#### 13.4.1. Contractによる比較の対応付け

同じユーザー型の2つのオペランドを比較するには、下記のKimi Contractが必要であり、その対応付けは一度だけ解決する。独立した関数、拡張、変換を探索することはない。ジェネリックコードには対応する制約が必要となる。組込み比較を優先する。

| 演算子 | 操作 | 結果 |
| --- | --- | --- |
| `==`、`!=` | 両オペランドの共有借用に対する`Equatable.equals` | その`bool`、またはその否定 |
| `<`、`<=`、`>`、`>=` | 両オペランドの共有借用に対する`Comparable.compare` | その`i32`と0の比較 |

`Comparable`は`Equatable`を詳細化する。`compare`の符号は、等価性と全順序に一致しなければならない。整数、ラップアラウンド整数、`char`、`string`は両方を提供する。`bool`、Unit、`f32`、`f64`は`Equatable`だけを提供する。安全な値参照型とタプル型はこれらの能力を転送または合成するが、オブジェクトハンドル型は行わない。structとenumには明示的な適合が必要であり、何も導出しない。比較によって算術演算が許可されることもない。`f32`と`f64`の`Equatable.equals`は[§12.4.3](#s12-4-3)のNaNに対して反射的な等価性を使う。一方、NaNに対する組込み`==`はfalseである。

<a id="s13-5"></a>
### 13.5. 明示的な操作

`@`は組込み操作である。オーバーロードできず、変換の連鎖を探索せず、直接のオペランドだけに適用する。オペランドの評価がユーザーコードを呼ぶことはあるが、この操作自身は呼ばない。メモリーを割り当てるのはオブジェクト作成だけである（[§13.6.2](#s13-6-2)）。どの形式も暗黙のボックス化、所有権の複製、参照数の増加を行わない。Originの制限は結果を期待型に合わせる処理であり（[§3.8](#s3-8)）、別の操作に置き換えることはない。

<a id="s13-5-1"></a>
#### 13.5.1. 形式とAdaptation対象

| 形式 | 意味 |
| --- | --- |
| `E@Type`、`E@Semantics` | 定義されたAdaptation（[§13.5.3](#s13-5-3)）。短縮形式はCore、参照先の型、View Targetをオペランドから得る |
| `E@move`、`E@copy` | 移譲、またはCopyが証明された値のCopy（[§13.5.3](#s13-5-3)） |
| `E@follow` | 参照またはオブジェクトハンドルが指すPlace（[§13.5.5](#s13-5-5)） |
| `E@raw`、`E@raw/U`、`E@usize` | 生ポインターの形式。[§5.4](#s5-4)を参照 |
| `E@wrap<U>`、`E@bits<U>` | ラップアラウンド変換とビット変換（[§13.5.4](#s13-5-4)） |

**Adaptation対象**は、SemanticsとCore、借用層やポインター層の完全な内側の型、またはオブジェクトのView Targetを与える。外側のSemanticsの連鎖にOriginを書くと、括弧で囲んでもエラーとなる。これらのOriginは推論する。例外は外側の`raw`層であり、`p@(raw/U during o)`のように括弧で囲んだ対象は、そのOriginを、通常の規則による型合わせ、または表明によって指定する（[§5.7](#s5-7)）。Optionのペイロードなど、内部に入れ子になった型は注釈を保つ。コンテナーパスの構文（[§9.7.1](#s9-7-1)）もこれらの制限を緩めず、ここでは束縛済みコンテナー修飾子を使えない。

**構文上の範囲。** 対象の範囲（Semantics接頭辞、`/`、`<`、閉じた対象と開いた対象、後置の接尾辞を続けられる位置）は、[§F.9.3](#sf-9-3)と[§F.9.4](#sf-9-4)で定義する。各Semantics接頭辞は、具体的なSemanticsまたは宣言されたペアバインダーに解決しなければならない。プリミティブのキーワードはペアバインダーになれないため、`x@i32 / y`は除算となる。

括弧で囲んだ対象は常に型なので、`x@(copy)`は操作にはならない。Semanticsの短縮形式には`?`も`during`も付けられない。`?`は完全な対象型に適用するため（[§3.2.3](#s3-2-3)）、`x: i32`に対する`x@i32?`はエラーとなる。

```kimi
let n = tasks@uniq.length            // Closed target: postfix continues.
let x = (point@Point).x              // Open target: group before selection.
let o = saved@(ref/T? during a)      // saved: Option<ref/T during a>; the payload annotation is kept.
// let e = items@Array<i32>[0]       // Error: group the adaptation first.
// let w = value@(ref/T during a)    // Error: Origin written on the outer borrow.
// let d = saved@ref/T? during a     // Error: group the annotated target.
```

**裸の対象名。** `E@X`では、まず組込みSemantics名を認識する。それ以外では、`X`を型（エイリアスと型パラメーターを含む）として、またペアバインダーとして別々に探索する。型だけなら`@Type`、ペアバインダーだけなら短縮形式となる。両方ある場合、またはいずれかのロールで曖昧な場合はエラーとなり、オペランドや期待型では解消しない。

<a id="s13-5-2"></a>
#### 13.5.2. 選択と推論

操作は、明記した対象と、オペランドの型およびカテゴリーから選ぶ。アクセス、所有権、Loan、Originはその後で検査し、失敗しても別の操作、ゲッター、オーバーロードを選ばない。通常の推論規則が許す範囲で、対象はリテラルやジェネリックの推論を導けるが、確定したオペランドや結果の型は変えない。数値変換、ラップアラウンド変換、ビット変換の対象が型付けを導くのは、直接リテラルだけである（[§13.5.4](#s13-5-4)）。外側の期待型が操作を取り消すことはない。数値変換の対象はオペランドの期待型ではない。`(200 + 100)@u8`は`i32`を計算して範囲検査に失敗し、`id(300)@u8`は`u8`から`id`を推論しない。読取り型への変換と同一型取得は、値の読取りによって参照オペランドを読むため（[§3.5.3](#s3-5-3)）、`number: ref/i32`での`number@i64`は参照先を変換する。一方、借用、`@follow`、`@copy`、`@move`、オブジェクト対象は参照自身を取る。ジェネリックなアクセス効果は[§8.14](#s8-14)に従う。`Never`オペランドは操作の実行を妨げるが、検査は妨げない。

<a id="s13-5-3"></a>
#### 13.5.3. 定義されたAdaptation

| 操作 | 条件 |
| --- | --- |
| 移譲 | `@move`。Move可能なPlaceまたは一時値 |
| Copy | `@copy`。Copyが証明された型 |
| 同一型取得 | 正規化された完全な型が同じ、またはオペランドの外側のSemanticsに一致する完全な所有対象。CopyのPlace、一時値、または読取り型への参照の値の読取り |
| 数値・ラップアラウンド・ビット変換 | [§13.5.4](#s13-5-4) |
| 借用・再借用 | [§13.5.6](#s13-5-6) |
| オブジェクト作成・アップキャスト | [§13.6.2](#s13-6-2)、[§13.6.1](#s13-6-1) |
| 生ポインターの変換・アドレス・型付きnull | [§5.4](#s5-4)の形式。raw Originの表明（[§5.7](#s5-7)）を除き、unsafe文脈は不要 |

**移譲。** Move可能なPlace（[§15.1.5](#s15-1-5)）への`E@move`は、Copy型であっても値と破棄責任を移譲し、そのPlaceをMovedにする。借用値では参照をMoveする。参照を通じて到達したPlaceを移譲することはない。ゲッターの結果を含む一時値には効果がない。`holder.item@move`はフィールドまたはゲッターの結果を移譲し、レシーバを移譲しない（[§7.3](#s7-3)）。

**Copy。** `E@copy`は値をCopyし、Placeは変えない。Non-CopyまたはCopyが未証明のオペランドはエラーとなるため、ジェネリックな`T`には`T is Copy`が必要となる。`r@copy`は参照をCopyし、`r@follow@copy`はCopyの参照先をCopyする。ByValueのSubject（[§15.1.6](#s15-1-6)）のように所有する値を明記する必要がある場所では、`E@move`に対応するCopy操作となる。オーバーロード候補を指定するものではない（[§7.4](#s7-4)）。`move`、`copy`、`follow`には、型、`?`、`during`、束縛集合を付けられない。

**同一型取得**は、CopyのPlaceをCopyし、一時値を移譲する。Non-CopyのPlaceはエラーとなる。外側のSemanticsが`ref`、`uniq`、`objref`、`objuniq`の対象は、借用または再借用を選び（[§13.5.6](#s13-5-6)）、同一型取得を選ぶことはない。裸の`@owner`は無効である。完全な所有対象、ジェネリックな対象、短縮形式`@obj`、`@rc`、`@arc`は、同一型の取得、オブジェクト作成、アップキャストを選ぶか、エラーとなる。どれも強参照の所有者を複製しない。

**Originの制限**は、その後、変性とoutlives関係が許す範囲で結果のOriginを短くするだけであり、Copy、Move、借用を追加しない。CoreとSemanticsの両方を変える対象は、定義された1つの操作でなければならない。変換してから借用する操作は挿入しない。要素ごと、構造的、文字列、`bool`、`char`、ユーザー定義の変換はなく、`@`を通じた動的キャストもない。安全な参照が参照先の値を得るのは、値の読取り、[§10.2](#s10-2)の共有参照Adaptation、または`@follow`による場合だけである。Non-Copyの参照先を取り出すことはない。

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

<a id="s13-5-4"></a>
#### 13.5.4. 数値変換、ラップアラウンド変換、ビット変換

| 形式 | オペランドと対象の型 | 結果 | 失敗 |
| --- | --- | --- | --- |
| `@Type` | 整数型と浮動小数点型。`U` ↔ `Wrapping<U>` | 同じ値、または下記の丸め | 範囲外ならAbort |
| `@wrap<U>` | 整数型とラップアラウンド整数型 | `U`へラップアラウンドした値 | なし |
| `@bits<U>` | 浮動小数点型と、同じ幅の整数型またはラップアラウンド整数型 | 同じビット列 | なし |

`@Type`は整数を切り詰めたりラップアラウンドしたりしない。整数→整数では範囲を検査する。整数→浮動小数点と浮動小数点→浮動小数点では、段階的アンダーフローを伴う最近接・偶数丸めを行う。有限値が丸めによって無限大になると失敗する。浮動小数点→整数ではゼロ方向に切り捨てて範囲を検査し、NaNと無限大は失敗する。浮動小数点間の変換は、NaN、無限大、符号付きゼロを保ち、ゼロへ丸めた値の符号も保つ。NaNのペイロードは保たなくてもよい。整数の0は正のゼロに変換する。連鎖する各`@`で丸めと検査を適用する。

**ラップアラウンド整数型。** ラップアラウンド整数型を含む`@Type`変換は、ビット列を保つ`U`→`Wrapping<U>`とその逆だけである。それ以外はエラーとなる。幅、符号の有無、浮動小数点との境界を越える場合は、意図を明記する。`x@U@Wrapping<U>`は検査し、`x@wrap<Wrapping<U>>`はラップアラウンドし、`f@bits<Wrapping<U>>`は再解釈する。

**直接リテラル。** 直接リテラル（[§12.3](#s12-3)）は、中間の既定型を使わず、正確な値から一度だけ変換する。収まらなければコンパイル時エラーとなる。それ以外のオペランドは先に型付けする。リテラルのAdaptationは、暗黙の引数の型合わせの範囲を広げない。

| 対象 | 整数リテラル | 浮動小数点リテラル |
| --- | --- | --- |
| 整数型 | 収まらなければならない | 正確な値をゼロ方向に切り捨てた後、収まらなければならない |
| `Wrapping<U>` | `U`に収まらなければならない | エラー |
| `f32`、`f64` | 一度だけ丸める | 一度だけ丸める（[§2.6](#s2-6)） |
| `wrap<U>` | `U`へラップアラウンド | エラー |
| `bits<F>`（`F`は浮動小数点型） | `F`の幅の符号なし整数型へラップアラウンドし、`F`として読む | エラー |
| `bits<I>`（`I`は整数型） | エラー。`@wrap`を使う | `I`の幅の浮動小数点型へ丸め、`I`として読む |

**ラップアラウンド変換。** `E@wrap<U>`は、同じ型同士も含め、任意の整数型とラップアラウンド整数型の間で変換する。`PrimitiveInteger`が証明されたパラメーターも含む（[§8.7.4](#s8-7-4)）。狭める場合は下位ビットを保ち、広げる場合はオペランドの符号の有無に従って拡張する。そのビット列を、Semanticsを持たない完全な値型`U`として読む。

**ビット変換。** `E@bits<U>`は、`f32`と`i32`、`u32`、`Wrapping<i32>`、`Wrapping<u32>`の間、および`f64`とそれぞれの64ビット版の間で、両方向に変換する。それ以外の型の組はエラーとなる。結果はIEEE 754の符号化、または符号化された値となり、NaNのペイロードとゼロの符号を保つ。どちらかの型が型パラメーターに依存する間は使えない。

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

<a id="s13-5-5"></a>
#### 13.5.5. Follow

`E@follow`は`ref/T`または`uniq/T`の値が指すPlaceを選び、何もCopyやMoveしない。`ref/T`の参照先はReadを、`uniq/T`の参照先はReadとWriteを提供する。どちらもTakeは提供しない。各`@follow`は1層を取り除く。`let`はスロットの置換を禁じるが、格納された参照の能力は弱めない。共有層から排他アクセスを得ることはなく、`ref/(uniq/T)`と`uniq/(ref/T)`から`T`に到達するときは共有アクセスだけとなる。

```kimi
var number: i32 = 1
let r = number@uniq
r@follow = 2              // Updates number.
let child = r@follow@uniq // Exclusive Reborrow of number through r.
// r@uniq                 // Error: r is a let binding; its slot is not exclusively borrowable.
```

**オブジェクトのペイロード。** `obj/T`、`rc/T`、`arc/T`、`objref/T`、`objuniq/T`への`@follow`がペイロードを選ぶのは、View Targetが内部のOriginも含めてペイロード型`T`と正確に一致し、`T is Kimi.Sealed`が証明済みの場合だけである（[§8.7.2](#s8-7-2)）。ハンドルのスロットとペイロードは別のPlaceであり、ペイロードはTakeを提供しない。

| ハンドルへの経路 | ペイロードへのアクセス |
| --- | --- |
| 書込み可能な所有`obj/T`経路 | Read、Write、共有借用、排他借用 |
| `let h: obj/T`などの読取り専用の所有経路 | Readと共有借用 |
| `objuniq/T`。`let`スロット経由も含む | 借用の範囲内で排他 |
| `rc/T`、`arc/T`、`objref/T`、または共有経路 | 共有だけ |

これらの権限は、ビューの借用、アップキャスト、暗黙のレシーバ借用にも適用する。openなビューには固有の規則を保つ（[§12.7](#s12-7)）。`obj`、`rc`、`arc`ハンドルを通じて到達するオブジェクト内に貸出し点がある借用は、自身のモードのLoanを、そのハンドルPlaceから間に参照外しを挟まないアクセス経路上に保つ（[§15.6.2](#s15-6-2)）。Loanの生存中、そのハンドルはMove、置換、排他借用、破棄ができない（[§16.3.3](#s16-3-3)）。ただし共有借用なら、ハンドルの読取りとcloneは認める。借用である`objref`または`objuniq`ハンドルを通じた場合、借用はそのハンドルのLoanを引き継ぎ、それが所有ハンドルをその場に保つ。借用ハンドルの置換や破棄は何も解放しない。借用ハンドルがMoveパスであるときにそこへ代入すると、そのパスが正確な接頭辞となるアクセス経路を持つ子Loanが終わる（[§15.6.3](#s15-6-3)）。

```kimi
func borrowPayload<T>(source: objref/T) -> ref/T during source
    T is Sealed and ObjectPayload
    return source@follow@ref
```

**ペア層。** **ペア層**とは、正規化した型の外側のSemanticsがペアバインダー`s`であるものをいう（[§8.1.1](#s8-1-1)）。直接の対象が`T`の`s/T`、または直接の対象が`U`の`s/U`が該当する。ペア対象`T`単体は該当しない。`s`の許容集合（[§8.3.4](#s8-3-4)）が`value or valueborrow`に含まれる場合、ペア層は安全な値参照の1層であり、`s = ref`または`uniq`では存在し、`s = owner`では存在しない。`@follow`は直接の対象を格納するPlaceを選ぶ。それ以外の許容集合ではエラーとなる。これは`s = owner`で一時値へCopyする`x@s`（[§8.14](#s8-14)）の逆操作ではない。各操作は許される各Semanticsの規則を適用し、本体はそのすべてについて有効でなければならない（[§8.15](#s8-15)）。オペランドPlace `P`のパス依存を`p`、それまでに到達したモードを`M`とすると（[§15.1.6](#s15-1-6)）、次のようになる。

| 項目 | `owner` | `ref` | `uniq` |
| --- | --- | --- | --- |
| 選択するPlace | `P`自身（[§3.6](#s3-6)） | 参照先 | 参照先 |
| アクセス効果（[§8.14](#s8-14)） | 借用 | 再借用。[§10.2](#s10-2)のAdaptationでは参照のCopy | 再借用 |
| Read | `P`が読取り可能なとき | 可 | 可 |
| Write | `P`が排他的に書込み可能なとき | 不可 | 可。`let`スロット経由でも可。共有経路経由は不可 |
| 依存 | `p`。値の場合はその一時Place（[§3.6](#s3-6)） | スロットではなく、`o`と親Loan | `o`と親Loan。子Loanのアクセス経路は`P`を通る（[§15.6.3](#s15-6-3)） |
| 裸のPlaceのSubjectのモード | Shared | Shared | `M`とExclusiveの弱い方 |
| 層を越えた後のモード | 貸出し点を排他的に取得できる位置だけで、`M`とExclusiveの弱い方（[§15.1.5](#s15-1-5)） | Shared | `owner`と同じ |

ジェネリック本体が得るのは、許される場合のうち最も弱い能力とモードである。そのためWriteには`s is owner or uniq`が必要となり、`owner`を許す場合は`P`が排他的に書込み可能でなければならない。各場合は、経路全体から導いた自身の依存で検査する。`o`は、たどった出現箇所の外側のOriginである（[§8.1.2](#s8-1-2)）。オーバーロードの順位付けには、許される場合のうち最も悪いものを使う（[§10.2.2](#s10-2-2)）。Takeは提供しないため、条件を満たすペア層を越えてパターンが値で束縛することはない（[§14.8.1](#s14-8-1)）。Object Semanticsを許す場合など、許容集合が`value or valueborrow`に含まれないペア層はたどらず、束縛はそのスロットを値で取得する。インスタンス化は選択するPlaceやその合法性を変えない。ペア層をたどっても、Copy、実行時分岐、メモリー割当ては追加しない。具体的な所有Placeへの`@follow`はエラーとなる。

```kimi
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    s is value or valueborrow
    return c[i]@follow@ref   // owner: Borrow of the element; ref, uniq: shared Reborrow.

func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c
    s is owner or uniq
    return c[i]@follow@uniq  // Rejected for Collection<ref/Node>: s is owner or uniq is Refuted.
```

<a id="s13-5-6"></a>
#### 13.5.6. 借用と再借用

`E@ref`、`E@ref/V`、`E@uniq`、`E@uniq/V`は、記述したスロット`E`自身を借用する。格納型が`V`なら結果は`ref/V`と`uniq/V`であり、`V`を明記する場合は格納型と正確に一致しなければならない。参照先の選択、同一型の参照のCopy、再借用は行わない。そのため、`r: ref/T`に対する`r@ref/T`はエラーとなり、`r`または`r@copy`と書く。貸出し点は[§3.4.2](#s3-4-2)で定める。初期化、能力、Loan、Originは有効でなければならない。一時値は実体化する（[§3.6](#s3-6)）。期待型に対する暗黙のAdaptationは[§10.2](#s10-2)で定める。

| 入力 | 操作 | 結果 |
| --- | --- | --- |
| `V`を格納する読取り可能なPlace | `@ref`、`@ref/V` | 新しい`ref/V` |
| `V`を格納する排他的に書込み可能なPlace | `@uniq`、`@uniq/V` | 新しい`uniq/V` |
| 読取り可能な`obj/T`、`rc/T`、`arc/T`のPlace | `@objref` | 新しい`objref/T`。参照数は変わらない |
| 排他的に書込み可能な`obj/T`のPlace | `@objuniq` | 新しい`objuniq/T` |
| `objref/T` | `@objref` | 共有参照のCopy |
| `objuniq/T` | `@objref`、`@objuniq` | オブジェクトの共有再借用または排他再借用 |
| unsafe文脈で`V`を格納する生のPlace | 所有Placeと同じ | 新たな貸出し点を持つ新しい参照（[§5.2.2](#s5-2-2)） |

値参照の**再借用**は、参照先を通じて記述する。`r@follow@ref`と`r@follow@uniq`は、`r`をMoveせずに参照先の能力を貸し出す。期待型`ref/T`または`uniq/T`は`uniq/T`値を暗黙に再借用し（[§10.2](#s10-2)）、`uniq/T`を格納するPlaceの裸の取得もそれを再借用する（[§3.5.1](#s3-5-1)）。子Loanが生存する間、親を通じた競合アクセスは禁止する。借用は借りるスロットに、再借用は参照先と親Loanに依存する。共有アクセスを強めることはなく、`rc`と`arc`は排他借用を与えず、値の借用とオブジェクトの借用は相互に変換しない。

**ゲッターの結果。** 独自、computed、または要件の`get`を一度呼び、その結果をAdaptationする。所有する結果の借用は、[§11.5](#s11-5)に従って一時値を実体化する。呼出しやゲッターが返す参照は値である。期待型がそれをAdaptationし、`@follow`は参照先を再借用する（[§7.1](#s7-1)）。`set`は隠された格納領域の借用を与えない。

```kimi
var value = makeValue()
let exclusive = value@uniq
let child: uniq/Value = exclusive // Exclusive Reborrow; exclusive resumes after child's last use.
let moved = exclusive@move        // Transfers the reference, not its referent.
let view = holder.item@follow@ref // item's getter returns ref/Resource: Reborrow of the referent.
```

<a id="s13-5-7"></a>
#### 13.5.7. 評価と失敗

各オペランドと必要なレシーバは一度だけ評価し、連鎖する操作は内側から外側へ完了する。取得が呼出しやゲッターを逆向きに通って、隠された格納領域まで伝わることはない。借用は形成時から保護される。排他レシーバと引数の借用は呼出し予約に従い（[§15.6.7](#s15-6-7)）、それ以外の排他借用は直ちに生存する。結果を捨てる場合も評価と検査を行う。完了したMoveや効果を巻き戻すことはない。

| 失敗 | 扱い |
| --- | --- |
| 未定義のAdaptation。型、アクセス、初期化、Loan、Originの違反 | コンパイル時エラー |
| リテラルの型合わせの失敗、または必須の定数変換の失敗 | コンパイル時エラー |
| 実行時の数値変換の失敗 | 値が静的に既知でも、評価時にAbort |
| unsafeの約束への違反 | 未定義動作（[§5](#s5)） |

その他の定数評価は[§17.4.4](#s17-4-4)に従う。

<a id="s13-6"></a>
### 13.6. オブジェクト操作、実行時検査、検査付きキャスト

<a id="s13-6-1"></a>
#### 13.6.1. オブジェクトのアップキャスト

取得元の静的なView Targetを`S`、異なる対象を`V`とする。アップキャストには、より派生したすべての動的型について`Supports(S, V)`（[§3.3.1](#s3-3-1)）が成り立つという静的な証明が必要となる。具体的なCoreには基底グラフを、ContractビューにはランタイムContractの規則を使う（[§8.12](#s8-12)）。ObjectPayloadからの除外を宣言するCore（[§8.7.3](#s8-7-3)）は対象になれない。ペイロードの型消去（[§15.8.1](#s15-8-1)）、アクセス、Origin、Loanを検査する。

| 取得元 | 操作 | 結果 |
| --- | --- | --- |
| `obj/S`、`rc/S`、`arc/S` | `@obj/V`、`@rc/V`、`@arc/V` | 通常の取得による同じ所有ハンドル（Placeには`@move`が必要） |
| `objref/S` | `@objref/V` | 共有参照のCopy |
| `objuniq/S` | `@objuniq/V`、`@objref/V` | 排他的または共有の子再借用 |
| 共有借用可能な`obj/S`、`rc/S`、`arc/S`のPlace | `@objref/V` | オブジェクトの共有借用 |
| 排他的に書込み可能な`obj/S`のPlace | `@objuniq/V` | オブジェクトの排他借用 |

アップキャストは、オブジェクト、その同一性、動的型、参照数、後処理の責任を保つ。暗黙のアップキャストも、値の借用のアップキャストもない。`@ref/V`は`@objref/V`ではない。取得元から保証できない対象には、検査付きキャストが必要となる（[§13.6.5](#s13-6-5)）。

```kimi
let dog: obj/Dog = makeDog()
let animal = dog@move@obj/Animal // dog is Moved; animal owns the whole Dog.
let view = animal@objref
// let bad = view@objref/Dog     // Error: Animal does not statically imply Dog.
```

<a id="s13-6-2"></a>
#### 13.6.2. オブジェクトの作成、clone、ライフサイクル

`s`が`obj`、`rc`、`arc`のいずれかであるとき、`@s`と`@s/T`、エイリアス、ジェネリック対象には同じ規則を使う。`owner/T`から`s/T`は`T is ObjectPayload`を証明してオブジェクトを作る。`s/T`から`s/T`は同一型の取得である。`s/Derived`から`s/Base`はアップキャストである（[§13.6.1](#s13-6-1)）。それ以外は、別の定義されたAdaptationかエラーとなる。入力型は対象や期待型とは独立に確定するため、`1@obj`は`obj/i32`となり、`1@obj/i64`はエラーとなる。`1@i64@obj`と書く。作成は型引数、長さ、内部のOrigin、Loanを保ち、アップキャストと組み合わせることはない。`obj → rc/arc`、`rc ↔ arc`、共有から排他、借用から所有者への操作はない。`Never`の入力は何も作らない。

`Kimi.Intrinsics`（[§22.1](#s22-1)）は、次の関数も宣言する。その`value`と`build`ラベル、およびWeak操作（[§13.6.3](#s13-6-3)）の`value`ラベルは省略できる。関数と`@`による作成の効果は同じである。作成には`T is ObjectPayload`、循環を伴う作成にはさらに`T is Owned`が必要となる。`clone`は`s is rc or arc`の下で`<s/T>`を取る。

| 関数 | 入力 → 結果 | 効果 |
| --- | --- | --- |
| `makeObj<T>(value)` | `T → obj/T` | 新しい排他オブジェクト |
| `makeRc<T>(value)`、`makeArc<T>(value)` | `T → rc/T`、`T → arc/T` | 強参照数1の新しい共有オブジェクト。`arc`は参照数をアトミックに扱う |
| `clone<s/T>(value)` | `ref/(s/T) → s/T` | 同じオブジェクト、ビュー、モードへの強参照を1つ追加 |
| `makeRcCyclic<T, F>(build)`、`makeArcCyclic<T, F>(build)` | `F → rc/T`、`F → arc/T` | 下記の循環構築 |

**作成**は入力を一度取得し（Non-CopyのPlaceには`@move`が必要）、コンストラクター、アクセサー、`drop`を実行せず新しい格納領域へMoveする。`arc`の作成によってスレッド間の移譲が許可されることはない（[§D.5](#sd-5)）。**強参照のclone**はハンドルのスロットを共有借用する。ハンドルのスロットは暗黙には借用しないため、`clone(handle@ref)`と書く（[§10.2](#s10-2)）。メモリーを割り当てず、ビュー、動的型、依存を保つ。入力はInitializedのままで、結果は入力のハンドルスロットへのLoanを保持しない。そのため、取得元のハンドルはその後でMoveや置換ができる。オブジェクトのMoveや借用によって参照数が変わることはない。

**ライフサイクル。** オブジェクトはBuilding、Alive、Destroying、Freedの順に遷移する。Buildingでは強参照やペイロードへのアクセスを公開しない。最後の強参照の解放は完全な動的型を破棄し、格納領域を解放する。Weakはオブジェクトを生存させない。`obj`も、参照数がない点を除き同じ境界を持つ。

**`arc`の順序。** 1つの`arc`オブジェクトについて、ペイロードの初期化は、強ハンドルを通じるペイロードのどの使用よりも先に起きる。各強参照の解放は最後の強参照の解放より先に起き、最後の解放はペイロードの破棄より先に起きる。成功する`upgrade`は最後の強参照の解放より先に起きる。最後の解放後、`upgrade`は永続的に失敗する。新しいハンドルをその作成スレッドへ返しても、スレッド間の同期は生まれない。これらの保証が対象とするのは参照カウントと寿命だけであり、ペイロードの変更は対象にしない。これらを実現するアトミック操作の順序は実装定義である。

**循環構築。** `rc`の生成関数は`F is Callable<owner, (Weak<rc/T>) -> T>`を要求する（`arc`も同様）。`F`は通常どおり取得し、一度呼び出す。

1. 未公開の格納領域と、構築用のWeakを1つ割り当てる。
2. Weakを値で構築関数へ渡す。Moveやcloneは可能で、Building中の`upgrade`は`None`を返す。
3. 構築関数が戻り、その呼出しの後処理が完了した後、結果をペイロードへMoveする。
4. 最初の強参照とともにAliveとして公開し、その強参照を返す。

メモリー割当ての失敗、または最大参照数からの増加は、公開前にAbortする。参照数はラップアラウンドしない。構築関数のAbortや停止しない実行では公開しない。

```kimi
let shared = 7@rc
let again = Kimi.Intrinsics.clone(shared@ref) // Strong count two.
let boxed = "sample"@obj
// let wrong: obj/i64 = 1@obj                 // Error: the input stays i32.
// let duplicate = shared@rc                  // Error: a Non-Copy Place needs @move; this is not clone.
```

<a id="s13-6-3"></a>
#### 13.6.3. 弱参照

`Kimi.Intrinsics`の各関数は、`s is rc or arc`と、`s/T`について[§3.2.2](#s3-2-2)の適格性を要求する。入力を一度評価し、Initializedのままにする。

| 関数 | 入力 → 結果 | 効果 |
| --- | --- | --- |
| `downgrade<s/T>(value)` | `ref/(s/T) → Weak<s/T>` | 同じオブジェクトへのWeak。強参照数は変わらない |
| `upgrade<s/T>(value)` | `ref/Weak<s/T> → Option<s/T>` | オブジェクトがAliveなら新しい強参照の`Some`、それ以外は`None` |
| `clone<s/T>(value)` | `ref/Weak<s/T> → Weak<s/T>` | 同じオブジェクトへの別のWeak |

`ref/Weak<…>`の引数に対し、強参照の`clone`は`s = owner`を束縛するが、これはRefutedとなる。そのため2つのcloneが両方とも適用可能になることはない。`downgrade`は強い`rc`または`arc`ハンドルだけを受け取り、メモリーを割り当てることがある。`upgrade`とWeakの`clone`は割り当てない。結果はオブジェクト、ビュー、モードを保つ。`upgrade`は[§13.6.2](#s13-6-2)に従って最後の`arc`解放と競合する。Building中の`None`の後に公開されることはあるが、最後の解放後の失敗は永続的である。参照数があふれるとAbortする。Weak、そのclone、upgradeで得た強参照は、ペイロードの外部依存を保つが、取得元のスロットへの永続するLoanは保たない。そのため、ローカルの借用を保持するペイロードへのWeakは、その借用より長く生存できない。WeakのMoveは参照数を変えず、破棄はペイロードに触れない。ペイロードへのアクセスには`upgrade`が必要となる。

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

<a id="s13-6-4"></a>
#### 13.6.4. 実行時のis検査

通常の式における`value is T`と`value is not T`は、実行時のオブジェクトを検査する。`is`の意味は探索前に文脈で決まる。本体の先頭領域を含む制約句（[§7.5](#s7-5)）と関連型の条件では、要件検査を使う（[§8.2](#s8-2)）。コンパイル時ディレクティブは、すべての`is`検査を拒否する。`is`検査は1つの対象の後で終わるため、`value is Dog or Cat`は`(value is Dog) or Cat`となる。

対象は、必要に応じて修飾した1つの名前付きstructのCoreであり、Semantics、束縛名、合成を持たない。その型引数は完全な型でなければならない。エイリアス展開後も含め、対象内のどこにもOrigin、`during`、束縛集合を書かず、対象の補完（[§13.6.5](#s13-6-5)）が束縛を供給する。対象自身は、型パラメーター、関連型、struct以外のCoreであってはならない。ただし型引数には、外側の型パラメーターと関連型を含められる（`value is Slot<T>`）。左辺のSemanticsの下で、対象のオブジェクト形式を形成できなければならない（[§8.7.3](#s8-7-3)）。

左辺は、structのCore `S`を持つ`obj/S`、`rc/S`、`arc/S`、`objref/S`、`objuniq/S`のいずれかの型である。左辺を一度評価し、`value`の動的型を`D`として、ジェネリック引数も含む`Supports(D, T)`またはその否定を結果とする。検査は何もCopy、Move、取得せず、参照数も変えない。結果が静的に既知の検査も有効であり、オペランドを評価する。型の絞込みは[§14.10](#s14-10)に従う。

```kimi
require value is Dog and value.isHealthy() else => return
value.bark()
// if value is T => () // Error: a Type parameter is not a struct Core.
```

<a id="s13-6-5"></a>
#### 13.6.5. 検査付きキャストと対象の補完

`V`への検査付きキャストが成功するのは、元のオブジェクトの動的型について`Supports`が成り立つ場合に限る。合法性には結果型を形成できることも含む。失敗は不在またはエラーの値となり、Abortにはならない。この版では意味を定義するが、ソースでの表記は定義しない。Contractビューに対するビューのサポート検査と、正確な型の検査にも、同様にソース構文はない（[§D](#sd)）。

取得元が`objref/A`なら`Option<objref/B>`を得て、取得元は引き続き使える。`objuniq/A`なら子再借用により`Option<objuniq/B>`を得て、失敗時は子Loanを保持しない。`obj`、`rc`、`arc`の取得元`s/A`は、`Result<s/B, s/A>`へ一度Moveし、失敗時は元のビューを保持する。

**対象の補完。** 実行時の`is`検査、型の絞込み、検査付きキャストは、Originを書かない対象の束縛を補完する。オペランドのView Targetを`S`（絞り込まれた名前では実効型のView Target。[§14.10.1](#s14-10-1)）、対象を`D`とする。関係は、実行時の同一性に対する`Supports`で判定する（[§3.3.1](#s3-3-1)）。ジェネリック本体は、許されるすべての束縛について判定する。

| `S`と`D`の関係 | `D`の束縛 | `is`検査 | 検査付きキャスト |
| --- | --- | --- | --- |
| `D`が`S`または`S`の基底であると証明済み | その位置で`S`が与える束縛 | 常にtrue。事実を追加しない | アップキャストと同様、常に成功 |
| `D`がそのどちらでもないと証明済み | 固定されたOrigin束縛はすべて`static`。補完した`D`はOwnedでなければならない | 絞り込む（[§14.10.2](#s14-10-2)） | 成功時は補完した`D`を保持 |
| 未決定（ジェネリック本体内だけ） | なし | 事実を追加しない | エラー |

第2行は単一継承に依存する（[§6.2.2](#s6-2-2)）。成功には厳密により派生した動的型が必要であり、その型消去時にペイロードがOwnedであると証明されている（[§15.8.1](#s15-8-1)）。ジェネリック本体は制約からOwnedを証明する。non-staticな束縛や、呼出しごとのCallableのOrigin（[§15.2.3](#s15-2-3)）を作ることはない。ハンドルのSemantics、外側のOrigin、Loanは保つ。

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

<a id="s13-7"></a>
### 13.7. 代入

<a id="s13-7-1"></a>
#### 13.7.1. 単純代入

代入、複合代入、インクリメント、デクリメントの対象はレシーバ式ではない（[§7.3](#s7-3)）。[§11.2](#s11-2)のパス、`let`／`var`、プロパティの権限に従い、書込み権限を伴って特定する。`@uniq`や`@objuniq`は不要であり、呼出し予約も開始しない。複合代入、インクリメント、デクリメントは、[§13.7.2](#s13-7-2)に従って更新先を保護する。対象の特定のために呼ぶゲッターと、独自・computed・要件の`set`のレシーバは、レシーバ式である（[§11.3.1](#s11-3-1)）。

**代入先の型。** 標準`set`では、代入先の型は代入先Placeの型である。選択した`set`が独自、computed、または要件のものである場合、代入先の型はそのセッターの`value`入力型となる。`=`の右辺は、期待型による推論とリテラルの型合わせも含め、その型に合わせる。複合代入、インクリメント、デクリメントは、読取り型（`get`の結果型）で計算する。演算、その範囲検査とAbort、前置・後置の`++`／`--`の結果にはその型を使う。その後、計算した値を共通Adaptationによってセッターの入力型に合わせる（[§10.2](#s10-2)）。

`target = value`はUnitを返し、次の順に実行する。

1. 右辺を評価し、移譲も含む共通Adaptationによって代入先の型の一時値を確保する（[§10.2](#s10-2)）。Non-CopyのPlaceなら`y = x@move`となる。
2. 対象のレシーバとアクセス経路を、左から右へ一度評価する。対象自身のゲッターは呼ばない。
3. 古い値、またはそのInitializedのまま残る部分を破棄する。構築が完了していない値の一部への書込みはエラーとなる（[§15.1.2](#s15-1-2)）。
4. 一時値を配置する。代入先はInitializedになる。

独自、computed、または要件の`set`は、手順3と4の代わりに確保した値を受け取る。標準`set`は、[§11.2](#s11-2)に従って値を直接配置し、不完全な格納領域を復元できる。コンストラクターによる自身のフィールドの最初の配置は[§6.3.4](#s6-3-4)に従う。ゲッターの一時値を根とする代入先は、[§11.5](#s11-5)の制限を受ける。

置換はコンストラクターや初期化子を実行しない。完全な古い値は正確な型の破棄連鎖によって、不完全な古い値は部分的な後処理によって破棄する（[§16.3.2](#s16-3-2)）。基底ビューは値全体の対象ではない。その後処理が完了しなければ、何も配置しない。対象の特定には右辺の評価後の状態を使うため、Movedのローカルの格納領域を特定できるが、Movedの所有者やレシーバは読めない。格納領域の更新の依存は[§15.7.3](#s15-7-3)に従う。右辺が完了しなければ対象を評価しない。対象の特定が失敗すれば、破棄も配置も行わない。何も巻き戻さない。代入はUnitを返すため、`a = b = c`は`a`にUnitを代入する。

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

<a id="s13-7-2"></a>
#### 13.7.2. 複合代入

`+=`、`-=`、`*=`、`/=`、`%=`、`&=`、`|=`、`^=`、`<<=`、`>>=`は二項演算を行い、Unitを返す。算術の複合代入は、対応する演算子と同じ操作を選択する（[§13.3.4](#s13-3-4)）。更新専用のContractはない。

1. 演算が要求する方法で右辺を評価し、取得する。
2. 更新先を一度特定し、標準アクセスまたは選択した`get`によって古い値を一度読む。
3. 読取り型で演算を一度行う（[§13.7.1](#s13-7-1)）。表現不能な整数の結果は、書込み前にAbortする（[§13.3.1](#s13-3-1)）。
4. 演算だけのためのLoanを終えてから、置換または選択した`set`によって一度書き込む。

レシーバと添字は一度評価する。書込み先を参照から参照先へ変更することはない（[§3.5.3](#s3-5-3)）。`r@follow += rhs`と書く。読取りと書込みは独立に選択する（[§11.1](#s11-1)）。標準アクセスは許可された格納領域アクセスを使い、独自・computed・要件のアクセサーは呼び出す。セッターを迂回することはない。古い値は演算子が要求する方法で取得し、暗黙のMoveは行わない。標準`get`は、独自の`set`があってもNon-Copyの古い値を貸し出せる。ゲッターの結果は通常の一時値であり、その依存が`set`と衝突することがある。古い値の消費は`a = transform(a@move)`と明記する。

**書戻しの保護。** 複合代入、インクリメント、デクリメントが更新先を特定してから書き込むまで、更新先Place（標準`set`の場合）、または独自・computed・要件の`set`のレシーバを、呼出し予約と同様に保護する（[§15.6.7](#s15-6-7)）。`get`呼出しと演算子のオペランドのLoanを含む共有読取りと共有借用は共存できるが、書込み前に終わらなければならない。その区間に別の経路から対象への書込み、Move、破棄、排他アクセスを行うと、`get`や演算子の実装の効果による場合も含めてエラーとなる。書込み時に排他Loanが生存し始め、参照を通じた書込みと同様に予約を通じて書き込む（[§15.6](#s15-6)）。

格納領域の更新の依存は[§15.7.3](#s15-7-3)に従う。ある手順が完了しなければ後続の手順を実行せず、何も巻き戻さない。複合代入はアトミックではない。`string`の更新先には複合代入がない（[§13.3.2](#s13-3-2)）。生ポインターは、[§5.3](#s5-3)の下で`+=`と`-=`だけを認める。

```kimi
values[nextIndex()] += amount() // amount, nextIndex, old value, addition, write.
x += x                          // OK: the operand's shared Loan ends before the write.
holder.item += delta            // OK: the shared get and the Addable call only read holder before set.
// log += "!"                   // Error: no string compound assignment; write log = "\(log)!".
```

<a id="s13-8"></a>
### 13.8. 予約構文とComposition Root

演算子の記号、優先順位、結合規則は固定されている。ユーザーコードが拡張できるのは比較（[§13.4.1](#s13-4-1)）と算術（[§13.3.3](#s13-3-3)）だけであり、同名のメソッドだけではどちらも使えない。`and`、`or`、`not`、`=`、`@`、`is`、範囲、制御転送は再定義できない。独自演算子はなく、`!`、`&&`、`||`、`~`、`**`、`??`、`?.`、三項`?:`、単項`&`（`@ref`を使う）、前置`move`、`<-`もない。`as`は予約されている。ユーザー定義の算術演算子を定数として評価することはない。`#Name`は属性、`#if`／`#switch`はディレクティブであり、演算子ではない。

**Composition Root。** `$`は名前探索とは独立に、言語が提供する操作を選択する。操作名の認識はキーワード表で定義する（[§F.3](#sf-3)）。操作には`$tryWrite(writer, literal)`（[§22.10.4](#s22-10-4)）、`$abort(...)`（[§17.4](#s17-4)）、テスト操作の`$expect(...)`と`$require(...)`（[§17.6](#s17-6)）がある。置換、オーバーロード、関数値としての使用はできない。それ以外の`$`操作はエラーとなる。

```kimi
if n < 0 => $abort("negative input")
// let f = $abort  // Error: a $ operation is not a function value.
// let s = a ?? b  // Error: ?? is not an operator.
```

---

<a id="s14"></a>
## 14. 制御フロー



制御フロー式には、`if`、`match`、`for`、`while`、`loop`、`do`と、制御転送の`return`、`exit`、`continue`、`yield`がある。`unsafe`、`defer`、`require`、明示的に結果を捨てる操作は文であり、初期化子、引数、オペランドにはできない。テスト本体では`$expect`と`$require`も認める（[§17.6](#s17-6)）。

**反復構文**は`for`、`while`、`loop`を指し、**反復**はその本体の1回の実行を指す。**選択**は1つの`if`連鎖または1つの`match`を指す。括弧はグループ化だけを行い、スコープ、制御転送先、探索の境界は作らない。

<a id="s14-1"></a>
### 14.1. 完了

評価は、**完了**、**発散**（実行が終わらない）、または**Abort**（完了やスコープ退出なしでプロセスが終わる。[§17.4](#s17-4)）に至る。**通常の完了**`Normal(result)`は評価元へ戻る。文は式になることなくUnitで完了する。**通常以外の完了**は、`Return(target, result)`、`Exit(target, result)`、`Continue(target)`、`Yield(target, result)`のいずれかである。制御転送式自身が通常の完了に至ることはない。

必要なスコープ退出（[§16.2](#s16-2)）の後、構文は自身宛ての制御転送を受け取り、それ以外を外側へ伝える。

| 対象 | 受け取る転送 | 動作 |
| --- | --- | --- |
| 反復または`do` | `Exit(self, result)` | `result`で完了する。 |
| 選択 | `Yield(self, result)` | `result`で完了する。 |
| 遅延本体 | `Exit(self, ())` | 本体を終了し、保留中のスコープ退出を再開する。 |
| 反復 | `Continue(self)` | 次の反復を開始する。 |
| 関数 | `Return(self, result)` | `result`を呼出し側へ渡す。 |

Neverは型であり、完了ではない。[§14.9](#s14-9)に従って推論する。

```kimi
func clamp(value: i32) -> i32
    if value < 0 => return 0                   // Return passes through the if.
    let size = label measure: do
        if value > 100 => exit to measure 100  // Exit completes the do with 100.
        exit to measure value
    return size
```

<a id="s14-2"></a>
### 14.2. 本体と評価文脈

<a id="s14-2-1"></a>
#### 14.2.1. 本体の形式

実行可能な本体はすべて**本体**（Body）である（文法は[§F.10](#sf-10)）。

- **単一項目の本体**`=> item`は、ヘッダーと同じ行で始まる式または文を1つ持つ。通常の式の継続やmatchのアームリストによって後の行へ続けられる。
- **インデントされた本体**は、宣言、式、文、許可されたディレクティブを持ち、順に実行する。完全なソース項目が少なくとも1つ必要であり、空行やコメントは数えない。何もしない場合は`()`と書く。本体がなければエラーであり、次の項目を取り込まない。

選択の句とアーム、反復、`do`、`unsafe`、`defer`、`require`の失敗本体、関数、匿名関数、クロージャー、特殊化、アクセサー、`init`、`drop`は本体を持つ。コンテナー本体とmatchのアームリストはレイアウト上の本体だが（[§F.4.2](#sf-4-2)）、Bodyではない。各Bodyは自身のスコープを持つ。宣言とディレクティブにはインデント形式が必要であり、各位置の宣言制限を保つ（[§7.5](#s7-5)）。

空でないことはディレクティブ選択前に検査するため、完全なディレクティブは何も選択しなくても1項目と数える。選択後は、通常の型、制御転送、結果の検査を適用する。

```kimi
defer => ()               // Explicit no-op.
defer
    #if windows
        closeHandle()     // Valid even if nothing is selected.
defer
    // Error: no source item.
nextOperation()
```

<a id="s14-2-2"></a>
#### 14.2.2. 評価文脈と本体の結果

**値文脈**には、初期化子、引数、条件、`match`と`for`のSubject、演算子と制御転送のオペランドが含まれる。**結果を捨てる文脈**には、単独の式とインデントされた本体の直下の式が含まれる。括弧は文脈と期待型を保つ。期待型がUnitであること、未使用、到達可能性、最適化のいずれも、値文脈を結果を捨てる文脈へ変えない。結果を捨てると通常の寿命で結果を破棄する。これはUnitへの変換ではなく、`Result`の結果を捨てる警告も含め（[§17.5](#s17-5)）、すべての検査を適用する。

インデントされた本体の直下の式は、最後の式も含めて結果を捨てる。**構造上の末尾への到達**、すなわち本体の項目列の通常の完了は、所有者が本体の結果を使う位置へUnitを供給する。単一項目の本体は次の表に従う。これらの規則は本体の検査前に固定する。

| 所有者 | 単一項目の式 | 結果の制御転送 |
| --- | --- | --- |
| 関数、`get` | 戻り値型がUnitに固定されていれば結果を捨ててUnitで完了。それ以外は値を返す | `return` |
| `if`、`match`、`do` | 値文脈では値を使う。結果を捨てる文脈では結果を捨ててUnitで完了 | `yield`。`do`には名前付き`exit` |
| `for`、`while`、`loop` | 結果を捨て、反復を継続 | `exit` |
| `unsafe`、`defer`、`set`、`init`、`drop` | 結果を捨て、Unitで完了 | `exit`（`defer`）、`return`（`set`、`init`、`drop`） |
| `require`の失敗 | 結果を捨てる。後続へ進んではならない（[§14.11](#s14-11)） | なし |

単一項目の本体にある文がUnitを供給するのは、通常の完了に至れる場合だけである。捨てる値同士に共通の型は不要だが、対象に供給する各値は、対象の結果を捨てる場合でも、その**対象結果型**に合わなければならない（[§14.9.2](#s14-9-2)）。対象結果型は、`for`、`while`、`defer`、`set`、`init`、`drop`、および結果を捨てる文脈の`if`、`match`、`do`、`loop`ではUnitとなる。制御転送を受け取っても、暗黙の本体結果は供給しない。

```kimi
if ready => visited.insert(id)   // The bool is discarded.
let bad: i32 = if ready
    compute()                    // Error: discarded; the implicit Unit does not fit i32.
else => 0
loop => exit 1                   // Error: a discarded loop requires Unit exits.
```

<a id="s14-2-3"></a>
#### 14.2.3. 条件

`if`、`else if`、`while`、`require`の条件とmatchのガードは、`bool`式である。`if let`、`var`条件、条件パターンはない。ヘッダー式内の、本体を持つ式は、[§F.4.4](#sf-4-4)のヘッダー式の規則に従う。

検査は条件を一度評価して`bool`を確保し、残る条件の一時値を生成順の逆に破棄し、ガード内の一時Loanを終えてから分岐する。後処理の効果によって条件を評価し直すことはない。制御転送、発散、Abortは、その経路の検査を終える。matchのSubject、イテレーター、束縛は、それぞれ固有のスコープを保つ。

```kimi
let guard = registry.lock()
if guard.contains(id) => work()   // A guard created in the condition would be destroyed before work().
```

<a id="s14-2-4"></a>
#### 14.2.4. 明示的に結果を捨てる

`_ = Expression`はUnitで完了する文であり、代入でも式でもなく、複合形式もない。オペランドは期待型のない値文脈で検査し、ローカルを導入しない。裸の取得（[§3.5](#s3-5)）または移譲で取得し、文の一時値の後処理で破棄する。裸のNon-CopyのPlaceは、`uniq/T`または`objuniq/T`を格納する場合を除きエラーとなる。この例外では、`_ = r`は子を再借用して破棄し、`_ = r@move`は`r`を消費する。明示的に結果を捨てる操作が抑止するのは、その結果を捨てることへの警告だけである（[§17.5](#s17-5)）。`Result`の結果を捨てるとエラーペイロードを破棄するが、Abortは捕捉しない。

```kimi
_ = prepare()          // Ignore the whole Result, including Err.
_ = try prepare()      // Propagate failure; discard success.
_ = resource@move      // Destroy a Non-Copy value early.
_ = r                  // r: uniq/Node; r stays usable.
// _ = resource        // Error: a bare Non-Copy Place.
// _ = .None           // Error: no Type information.
// consume(_ = prepare())   // Error: not an expression.
```

<a id="s14-3"></a>
### 14.3. `do`と`unsafe`

`do Body`は、[§14.2.2](#s14-2-2)に従って一度実行する式である。ラベルがあれば、自身を対象とする`exit to Label`を使える。ラベルなしの`exit`は`do`を飛ばすため、ラベルのないインデント形式の`do`はUnit以外の結果を供給できない。

`unsafe`と`defer`は、実行可能スコープの文である。`defer`はスコープ退出時に実行する本体を登録する（[§16.1](#s16-1)）。**Unsafeブロック**（本体を持つ`unsafe`。`unsafe func`は修飾子）は直ちに実行され、[§5](#s5)のunsafe操作と`noinit`（[§4.3.4](#s4-3-4)）に字句的な許可を与える。`noinit`は何も読み取らなくてもその許可を使う。許可は入れ子の遅延本体には届くが、関数境界は越えない。操作は最も内側のUnsafeブロックを使う。`unsafe func`は自身の内部に許可を与えない（[§7.6](#s7-6)）。Unsafeブロックは制御転送先でも境界でもなく、型、所有権、Loanの検査を緩めない。許可を使う操作がないUnsafeブロックには、`UnnecessaryUnsafeBlock_Kd`の警告を出す。

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

<a id="s14-4"></a>
### 14.4. ラベル

`label Label:`は、同じ物理行で後に続く`if`、`match`、`for`、`while`、`loop`、`do`に名前を付ける（文法は[§F.10](#sf-10)）。スコープや束縛を追加せず、それ以外の前には置けない。この接頭辞の認識は[§F.3.2](#sf-3-2)に従う。

ラベルは独自の名前空間を持ち、その構文の本体内だけから見える。条件、Subject、ガードからは見えない。同じ関数内でスコープが重なる同名のラベルはエラーとなる。ラベルはグループ化を追加しない。`return (label c: if ready => a else => b) + 1`は両経路で加算するが、括弧がなければ`else`経路だけで加算する。

```kimi
consume(label choice: if ready => 1 else => 0)   // Labeled positional argument.
return label work: do => compute()
let label: bool = true
let accepted: bool = label label: do
    exit to label label   // Label namespace target; Value namespace operand.
```

<a id="s14-5"></a>
### 14.5. 制御転送

<a id="s14-5-1"></a>
#### 14.5.1. 構文とオペランド

形式は`return [E]`、`exit [to Label] [E]`、`continue [to Label]`、`yield [to Label] [E]`である（文法は[§F.10](#sf-10)）。制御転送の型はNeverとなる。省略したオペランドは`()`であり、対象の対象結果型に対して検査する。結果の受渡しは[§16.2.2](#s16-2-2)に従う。

- `to`は`exit`、`continue`、`yield`の直後で文脈依存となり、名前付き形式に確定する。`return to`は値`to`を返す。ラベルは正確に1つの名前であり、計算した転送先ではない。
- オペランドの省略は[§F.10.2](#sf-10-2)で決める。完全な式を別の形で解釈し直すことはない。

```kimi
exit to search score(item)
continue to outer
yield to choice match mode
    .Fast => 1
    _ => 0
// [exit: value]            // Error: ':' does not omit the operand.
// [exit to work x: value]  // A Dictionary: the colon follows the complete operand.
```

<a id="s14-5-2"></a>
#### 14.5.2. 対象と境界

| 制御転送 | ラベルなしの対象 | 名前付きの対象 | 探索の境界 |
| --- | --- | --- | --- |
| `return` | 関数 | なし | `defer` |
| `exit` | 反復または`defer` | 反復または`do` | 関数。名前付きでは`defer`も |
| `continue` | 反復 | 反復 | 関数、`defer` |
| `yield` | `if`または`match` | `if`または`match` | 関数、`defer` |

最も近い対象を先に解決し、その後で検査する。不一致でも探索を再開しない。対象がない場合や種類が違う場合はエラーとなる。結果を捨てる文脈の選択に対するラベルなしの`yield`もエラーとなる。構文が対象や境界になるのは、その本体の内部だけである。`unsafe`と`require`はどちらにもならない。制御転送は、同じ関数の外側の構文だけを対象とし、関数本体や遅延本体を越えない。

**関数境界**は、名前付き関数、メソッド、匿名関数、クロージャー、特殊化、アクセサー、`init`、`drop`である。`defer`内で宣言した関数は、自身の`return`の対象を持つ。`drop`内の`return`はフィールドの破棄を飛ばさない（[§16.3.2](#s16-3-2)）。名前付き関数は、本体が終わらない場合も、宣言した結果型または既定のUnit結果を保つ（[§7.1](#s7-1)）。

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

<a id="s14-6"></a>
### 14.6. 反復構文

各反復は新しい本体スコープを持ち、次の反復の前に後処理する。本体の通常の結果は捨てる。

<a id="s14-6-1"></a>
#### 14.6.1. `for`と`while`

`for`はSubjectを一度取得し、要素を順に取り出す。`while`は各反復の前に`bool`条件を検査する。どちらも、要素の終了、偽の条件、自身宛ての`exit`でUnitとして完了する。`while`は条件が`true`でも静的な偽の経路を保つ（[§14.9.3](#s14-9-3)）。無条件に繰り返すには`loop`を使う。

`for`の束縛は、1つのスロット、または括弧で囲んだスロットのタプルであり（文法は`ForBinding`、[§F.10](#sf-10)）、一般のパターンではない。`(a, b)`はペア、`(a,)`は1要素のタプルを分解し、`(x)`は無効である。スロットは、名前（不変）、`var Name`（再代入可能なローカル。参照要素は[§15.1.6](#s15-1-6)で扱う）、または`_`（使用できない名前なしの束縛）である。`var _`は無効であり、名前付きスロットの名前は互いに異ならなければならない。

```kimi
for (key, value) in dictionary => process(key, value)
for (only,) in singles => use(only)   // singles: Array<(i32,)>
for var number in numbers@move
    number += 1                       // Updates only the iteration local.
while ready => process()
// for (x) in xs => use(x)            // Error: write `for x in xs`.
```

<a id="s14-6-2"></a>
#### 14.6.2. 反復プロトコル

`for`が使うのは、`LendingIterator`（[§22.2.1](#s22-2-1)）と3つの反復入口（[§22.2.1](#s22-2-1)）だけである。Subjectは[§15.1.6](#s15-1-6)に従って取得する。そのモードが入口を選び、参照経路選択（[§3.4.3](#s3-4-3)）で取得したレシーバに対して一度呼び出す。各ステップはイテレーターの短い排他再借用に対して`LendingIterator.next`を呼び、`None`なら終了し、`Some`ならペイロードを束縛する。

| Subjectモード | 典型的なSubject | 入口 |
| --- | --- | --- |
| Shared | `values`、`values@ref`、`ref/C`値 | `Iterable.iterate` |
| Exclusive | `values@uniq`、`uniq/C`値 | `UniqIterable.iterateUniq` |
| ByValue | `values@move`、一時値、`range@copy` | `IntoIterable.intoIterator` |

ByValueが参照先の所有権を取ることはない。適合がない場合や曖昧な場合はエラーとなり、他の方法は試さない。イテレーターの値は、`Kimi.Iteration.owning(it@move)`または`Kimi.Iteration.borrowing(it@uniq)`を通じて列挙する（[§22.2.2](#s22-2-2)）。標準イテレーターの管理処理、所有する格納領域、要素のコストは[§4.8.6](#s4-8-6)に従う。

要素型は`LendingIterator.LentItem(step)`、Iteratorの場合は`Iterator.Item`である。単一スロットはそれをそのまま束縛する。括弧で囲んだ束縛には、スロット数と正確に同じ要素数のタプル、またはその安全な参照が必要である（[§14.8.1](#s14-8-1)）。[§15.1.6](#s15-1-6)に従って分解し、`ref/(A, B)`は`ref/A`と`ref/B`を束縛する。`_`も要素を取得して後処理する。

| 列挙する値 | `Iterable`の要素 | `UniqIterable`の要素 | `IntoIterable`の要素 |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `ref/E during a` | `uniq/E during a` | `E` |
| `Dictionary<K, V>` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K, V)` |
| `Slice<E>` | `ref/E during s` | `ref/E during s` | `ref/E during s` |
| `Range<T, T>`、`ClosedRange<T, T>`（`T is PrimitiveInteger`） | `T` | `T` | `T` |
| `ResolvedRange` | `isize` | `isize` | `isize` |

`a`はSubjectの借用のOrigin、`s`はSliceの`source`である。要素のOriginは入れ子のまま保つ（`ref/(ref/Node during b) during a`）。配列とSliceは添字順、Dictionaryは挿入順に進む。境界が省略、末尾からの位置、または異なる型の組合せである範囲は列挙できない（[§4.6.4](#s4-6-4)）。

**Loanと後処理。** SubjectのLoanは、イテレーターまたは外へ出た要素が必要とする間、生存する。次の`next`と衝突するLoanがあれば、反復の戻り辺はエラーとなる。末尾への到達とこのループ宛ての`continue`は、次の`next`より前に反復スコープを後処理する。ループを離れるすべての制御転送（`exit`、`return`、`try`の伝播、外向きの`yield`、外側のループへの`continue`）は、反復スコープ、イテレーター、内部のSubjectの順に後処理する（[§16.2.2](#s16-2-2)）。早期退出はそれ以降の要素を消費しない。束縛は、本体のローカルとdeferの後に、最後から最初へ後処理する（[§16.2](#s16-2)）。

```kimi
for item in items => inspect(item)            // ref/Item; items stays usable.
for item in items@uniq => update(item)        // uniq/Item
for item in items@move => store(item@move)    // Item
for (a, b) in pairs => total += a + b         // pairs: Array<(i32, i32)>; a, b: ref/i32
// for x in counter => use(x)                 // Error: counter: Counter, a LendingIterator with no entry conformance; write Kimi.Iteration.owning(counter@move).
```

<a id="s14-6-3"></a>
#### 14.6.3. `loop`

`loop`は本体を繰り返し、本体の末尾と`continue`で再開する。結果を供給するのは自身宛ての`exit`だけである。値文脈では[§14.9](#s14-9)に従って推論し、結果を捨てる文脈ではUnitとなる。

```kimi
let found: i32 = label search: loop
    for item in items[..]
        if accepts(item) => exit to search score(item)   // Unlabeled, exit would end the for.
    exit -1
loop => work()
```

<a id="s14-7"></a>
### 14.7. `if`と`yield`

`if`は条件がtrueとなる最初の句、または`else`を実行する。`else if`の連鎖は1つの選択である。`else`の省略は、値文脈でのUnitの結果も含め、正確に`else => ()`と同じである。各句とアームは、それぞれ独立に本体の形式を選ぶ。`yield`は自身の選択を終える（[§14.5.2](#s14-5-2)）。結果を捨てる選択を早期終了するには、名前を付け、Unitを供給する。

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

<a id="s14-8"></a>
### 14.8. `match`

Subjectを一度評価し、[§15.1.6](#s15-1-6)に従って取得し、モードを固定する。ソース順で、パターンが一致しガードがtrueである最初のアームを選び、その本体だけを実行する。matchには少なくとも1つのアームが必要であり、アームリストにディレクティブは置けない（[§19.1](#s19-1)）。matchはすべての文脈で網羅的でなければならない（[§14.8.4](#s14-8-4)）。

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n   // n: ref/i32, value-read at the i32 result.
        .Some(_) => 0
        .None => 0
```

<a id="s14-8-1"></a>
#### 14.8.1. パターン

パターンを使えるのは`match`のアームだけである。

| パターン | 例 | 意味 |
| --- | --- | --- |
| ワイルドカード | `_` | 検査も取得もせず受け入れる。 |
| リテラル | `0`、`-1`、`true`、`'A'`、`"ok"` | 組込みの値と比較する。 |
| enumのケース | `.None`、`.Some(let x)`、`FileError.NotFound` | ケースを検査し、次にペイロードを検査する。 |
| 束縛 | `let x`、`var x` | 受け入れ、アーム内の名前に束縛する。 |
| Unit／タプル | `()`、`(let x,)`、`(let x, 0)` | 正確な要素数と各要素を照合する。 |
| グループ化 | `(.Some(let x))` | 内部のパターンを表す。 |

入れ子のケースパターンは、その位置から期待型を得る。修飾されたケースパターンの修飾子は、束縛集合の接尾辞のない通常のコンテナーパスである。自身と継承したOriginスロットは照合対象の型から束縛し、省略したenum引数は[§6.5.2](#s6-5-2)のパターン規則に従う。名前、定数、プロパティ、呼出し、その他の式はパターンではない。値との比較にはガードを使う。パターン検査はコンストラクター、ゲッター、変換、ユーザー演算子を呼ばない。

安全な値参照（`ref/T`、`uniq/T`、条件を満たすペア層。[§13.5.5](#s13-5-5)）に必要な構造がない場合、**構造パターン**（ケース、タプル、Unit、リテラル）は参照先を繰り返し選ぶ（[§3.4.3](#s3-4-3)）。ワイルドカードとグループ化は選ばず、束縛は下記の場合だけ選ぶ。Object Semanticsと生ポインターは選ばない。各位置は独立に選択する。`ref`の下はすべて共有となり、`uniq`の下が排他となるのはSubjectモードと外側のすべての層が許す場合だけである。ペア層は各場合のうち最も弱いアクセスを与える。条件を満たすペア層へ値でのアクセスで到達した束縛は、その層がTakeを提供しないため（[§13.5.5](#s13-5-5)）、層を越えた後のモードで、層が選択したPlaceへの参照を束縛する。`ref`を許す場合は`ref/T`、そうでなければ`uniq/T`となる（[§15.1.6](#s15-1-6)）。それ以外の位置では、束縛は何も選ばない。

要素数とペイロード数は正確に一致しなければならない。`(P)`はグループ化、`(P,)`は1要素のタプルである。`.Some((let x, let y))`は1つのタプルペイロードを、`.Pair(let x, let y)`は2つのペイロードを照合する。検査は外側から内側へ、左から右へ進み、最初の不一致で止まる。何もCopyやMoveしない。

**リテラルパターン**は、`bool`、整数、`char`、埋込みのない`string`のリテラルである。負の整数パターンは`-`とリテラルであり、符号を適用した後で型に合わせる（`-128`は`i8`に収まるが、`128`は収まらない）。`Wrapping<T>`は`T`に収まるリテラルを取る。文字はUnicodeスカラー値、文字列は正確なUTF-8内容で比較する。浮動小数点値、`null`、埋込み文字列、範囲はパターンではない。

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

<a id="s14-8-2"></a>
#### 14.8.2. 束縛のスコープ

`let name`と`var name`は常に一致し、再代入不能または再代入可能な本体ローカルを作る。`var`はペイロードへのアクセスを与えない（[§15.1.6](#s15-1-6)）。1つのパターン内の名前は互いに異ならなければならない。`let _`と`var _`は無効であり、`_name`は通常の束縛である。名前は自身のアーム内だけから見える。ガードでは候補として、本体では独自の束縛同一性を持つ別のローカルとして見える。本体での再宣言はできないが、入れ子のブロックはそれを隠せる。その他のパターン形式は将来の課題とする（[§D.3](#sd-3)）。

```kimi
match pair                     // pair: (i32, i32)
    (let x, let y) if x == y => same(x)
    (let x, _) => other(x)
// (let x, let x) => ()        // Error: duplicate name, not an equality test.
```

<a id="s14-8-3"></a>
#### 14.8.3. ガード

各束縛位置は**候補Place**、すなわちSubjectまたはその参照先にある初期化済みの格納領域を指定する。ガード内では、Subjectモードに関係なく候補名の型を`ref/T during g`とする。`T`は自身のOriginを変えない完全な格納型であり、`g`はガードの後処理で終わるガード内のOriginである。本体内の束縛は、所有・共有・排他の経路に応じて`T`、`ref/T`、`uniq/T`となる（[§15.1.6](#s15-1-6)）。読取りと比較は参照層をたどる（[§3.5.3](#s3-5-3)、[§13.4](#s13-4)）。ガードと本体はそれぞれ自身の型で解決し、ガードの事実を本体へ引き継がない。

ガードは1つの`bool`式であり、ヘッダー式である（[§F.4.4](#sf-4-4)）。`let`条件やガードの連鎖はない。パターンが一致すると、ガードを一度実行する。その後、結果に関係なく、候補の参照、ガードの一時値、新しいLoanを後処理する。`true`なら候補Placeから本体ローカルを初期化して本体を実行し、`false`ならSubjectを変えずに次のアームを試す。

**候補。** ガードで得た値を格納したり、ガード後に使ったりできるかは、`g`に対する通常のOriginとLoanの検査から決まる。`g`に依存する値はガードを出られない。一方、候補から読み取るかCopyした値は、格納された値のOriginだけを保つ。呼出しは呼出し先のシグネチャ（Originと効果境界）で判定し、本体では判定しない。候補名はMove、再代入、排他借用、キャプチャーできない。通常のローカルへ読み取った値は、通常の規則でキャプチャーできる。

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

**Subjectの保護。** パターン検査の開始からガードの後処理の完了まで、SubjectのPlaceと、パターン検査が参照を通じて到達する各参照先に共有Loanが生存する。このLoanとの衝突は通常のPlace重複と呼出しの規則で判定する（[§15.6.2](#s15-6-2)、[§15.6.4](#s15-6-4)）。ガードが変更できるのは保護されたPlaceと重ならないPlaceだけであり、その変更はガードがfalseでも巻き戻さない。

```kimi
// var state: State; var pair: (Option<i32>, i32)
match state
    .Idle if restart(state@uniq) => ()                // Error: state is protected during the guard
    _ => ()
match pair
    (.Some(let a), _) if bump(pair.1@uniq) => ()      // Error: the whole Subject is protected, Wildcard positions included
    _ => ()
```

ガードまたはその後処理が通常の完了に至らなければ、本体の束縛を初期化せず、後続のアームも実行しない。ガードから外への通常の制御転送は、matchの後処理規則（[§15.1.6](#s15-1-6)）と中断された式の順序に従う（[§16.2.1](#s16-2-1)）。ガードは制御転送先でも境界でもないが、自身のmatchへ`yield`することはできない。

<a id="s14-8-4"></a>
#### 14.8.4. 網羅性と到達不能なパターン

網羅性を証明するのは、**ガードのない**アームだけである。ガード付きアーム（`if true`でも）、ガードの論理、定数のSubject、Never型の本体、Neverのペイロードは、網羅性の証拠にならない。宣言されたすべてのケースが可能とする。

**位置全体のパターン**とは、ワイルドカード、束縛、Unitの位置での`()`、または位置全体のパターンからなるタプルパターンをいう（グループ化は透過的に扱う）。リテラルパターンとケースパターンは該当しない。

| 照合する領域 | 網羅する条件 |
| --- | --- |
| 任意 | 1つのアームが位置全体のパターンを持つ |
| `bool`／Unit | `true`と`false`／`()`がある |
| enum | 各ケースに、すべてのペイロード要素が位置全体のパターンであるアームがある |
| タプル、整数、`char`、`string`、その他 | 位置全体のパターンがある。部分的なパターンを組み合わせない |

リテラルは型に合わせた値で比較する（`0`、`-0`、`0x00`は一致する）。`bool`の規則は`bool`のSubjectだけに適用し、`.Some(true)`と`.Some(false)`の組には適用しない。

先行する1つのガードなしパターンが後のパターンを含む場合は、警告が必須である。ワイルドカードと束縛はその位置のすべてを含む。型に合わせた値が同じリテラルとUnitは自身を含む。同じケース、または同じ要素数のタプルパターンは、各子が対応する子を含む場合に、他方を含む。束縛名、`let`／`var`、グループ化は無視する。後のアームにはガードがあってもよく、先行するアームを組み合わせることはない。警告によってアームを検査や結果収集から除外せず、任意のlintが受入れ可否を変えることもない。

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

<a id="s14-9"></a>
### 14.9. 結果の検証

ディレクティブの選択後、3つの処理を適用する。**結果源の収集**は、対象に供給されるすべての値を集める。**構造的完了**は、構文から通常の完了の候補を見つける（[§14.9.3](#s14-9-3)）。**実行時の到達可能性**は、同じ経路に初期化、Move、Loan、型の絞込み、後処理を適用する。到達不能な項目は、仮定した経路の状態で検査する（[§14.10.3](#s14-10-3)）。

<a id="s14-9-1"></a>
#### 14.9.1. 結果源

| 結果源 | 供給するもの |
| --- | --- |
| 明示的な制御転送 | 到達可能かに関係なく、自身宛ての各`return`、`yield`、`exit`のオペランド。省略時はUnit |
| 単一項目の本体 | [§14.2.2](#s14-2-2)が値を使う位置ではその値。完了可能な文または結果を捨てる本体からはUnit |
| インデントされた本体 | 所有者が使う位置で、構造上の末尾への到達によるUnit |
| 暗黙の完了 | 省略された`else`、`for`の要素終了、`while`の偽の経路によるUnit |

すべての本体に入るものと仮定する。構造上の継続がない項目列は、末尾のUnitを供給しない。ループ本体の末尾はループの結果ではない。実行時の到達可能性は、結果源を追加・削除せず、推論した型も変えない。

```kimi
let conflict = loop
    continue
    exit 1
    exit "x"      // Error: unreachable results must also fit.
```

<a id="s14-9-2"></a>
#### 14.9.2. 対象結果型と式の型

**対象結果型**は、ソース順とは独立に決める。

1. 宣言された型、[§14.2.2](#s14-2-2)で固定された型、または固定された外側の期待型を使う。
2. それ以外では、独立に型付け可能な結果源の共通型を1つ求める（[§10.5](#s10-5)）。結果源の型は構造部分だけを比較する（[§15.6.1](#s15-6-1)）。共通型のOriginはローカルな領域とし、各結果源をそれに合わせることで通常のOrigin関係を得る。型の不一致にはしない。Neverは候補を供給しない。同じ型の上の安全な参照層だけが異なる結果源、またはその型のリテラルである結果源（[§12.3](#s12-3)）は、その型を共通型とし、値を読み取る（[§3.5.3](#s3-5-3)）。呼出し引数として使う制御フロー式は、[§10.5](#s10-5)の引数の種類の規則で分類する。
3. その型に対して検査できる結果源へ型を伝える（`0..n`と並ぶ`0..3`など）。リテラルの既定型は[§10.2](#s10-2)の規定に従って適用する。
4. すべての結果源を検査する。それでも型がない呼出し、匿名関数、空のリテラルには型注釈が必要となる。

入れ子の結果も同じように伝播し、リテラルの結果は外側の制約より先に固定しない。型付き束縛を後の使用から推論し直すことはない。推論が本体を再検査して組合せを探索することはない（[§10.8](#s10-8)）。

**式の型**は対象結果型となる。ただし、結果源があればすべてNeverであり、構造的完了に通常の候補がない場合はNeverとなる。Neverであっても型の不一致や結果の欠落を許さない。実行時の到達可能性によって型がNeverになることもない。結果型の宣言も期待型もない匿名関数は、これらの規則で戻り値型を推論する。

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

<a id="s14-9-3"></a>
#### 14.9.3. 到達可能性

**構造的完了**は保守的に判定する。`bool`リテラル、条件値、定数、パターンの包含、型の矛盾、呼出し先の本体によって経路を除かない。発散、Neverの呼出し、Abort、およびNever型のPlaceの読取りや明示的な取得など、その他すべてのNever型の式は、通常の候補を供給しない。

| 構文 | 経路 |
| --- | --- |
| 項目列 | 評価順。通常の完了後だけ継続 |
| `if`、`require` | 真と偽の両方 |
| `match` | すべてのアーム、各ガードの両方の結果。不一致の経路はない |
| `and`、`or` | 右オペランドを評価する経路と飛ばす経路 |
| `for`、`while` | 反復と即時完了 |
| `loop` | 自身宛ての`exit`だけが完了 |
| 制御転送 | オペランド、次に対象。転送元では何も継続しない |
| `defer`、ローカル関数 | 登録または宣言するが、実行しない |

構造的完了は後処理を無視する。**実行時の到達可能性**は、結果の受渡しを妨げることもある実際の後処理を伴って同じ経路をたどり、`require`の失敗本体が後続へ進まないことを検査する。`while true`には、`(true)`の場合も含め、静的な偽の経路が残るという警告を出す。最適化は受入れ可否を変えない。

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

<a id="s14-10"></a>
### 14.10. 型の絞込み

<a id="s14-10-1"></a>
#### 14.10.1. 絞込み可能な束縛と実効型

実行時の`is`検査は、structのCoreを対象とするオブジェクト型のローカル`let`束縛と再代入不能なパラメーターを絞り込む。名前は裸でも括弧で囲んでもよい。`var`束縛、フィールド、プロパティ、添字アクセス、呼出しは絞り込まれず、事実はエイリアスへ渡らない。

そのような名前の**実効型**は、宣言型のView Targetを**実効Core**、すなわちその位置で保証される最も派生したCoreに置き換えた型である。派生CoreのOrigin束縛は対象の補完から得る（[§13.6.5](#s13-6-5)）。束縛のその他の性質はすべて変わらない。再代入はその事実を終わらせるが、メンバーの変更は終わらせない。完全なペイロードの更新は、古いフィールド内容についての事実を終わらせる（[§15.7.3](#s15-7-3)）。

名前を値として使うすべての箇所は実効型を使う。探索、オーバーロード、推論、明示的な操作（[§13.5](#s13-5)）、レシーバの取得（[§7.3](#s7-3)）も含む。各オーバーロード候補は通常の規則で実効型に合わせ、宣言型で試し直さない。スロット操作の`@ref`、`@uniq`、`@raw`（[§13.5.6](#s13-5-6)、[§5.4](#s5-4)）とキャプチャーは宣言型を使う。絞込みは暗黙のMoveを追加せず、既存のメンバー名がどの層を選ぶかも変えない（[§6.2.2](#s6-2-2)）。

```kimi
func handle(value: objref/Animal) -> ()
    let alias = value
    require value is Dog else => return
    let dog = value          // objref/Dog
    let slot = value@ref     // ref/(objref/Animal)
    alias.bark()             // Error: alias has no Dog guarantee.
```

<a id="s14-10-2"></a>
#### 14.10.2. 条件の事実と合流

| 条件 | 真の経路 | 偽の経路 |
| --- | --- | --- |
| `x is T` | `x is T`を追加 | 保持 |
| `x is not T` | 保持 | `x is T`を追加 |
| `not C`、`(C)` | `C`の偽／真の経路 | `C`の真／偽の経路 |
| その他 | 新たな事実なし | 新たな事実なし |

`A and B`では、`A`の真の経路で`B`を検査し、偽の経路を合流させる。`A or B`はその双対である。これはすべての式の位置で成り立つ。格納した真偽値や呼出しを通じた事実は追跡しない。

実効Core `S`に対する事実`x is D`は、対象の補完と同様に判定し（[§13.6.5](#s13-6-5)）、ジェネリック本体では許されるすべての束縛について判定する（[§8.15](#s8-15)）。`D`が`S`より厳密に派生していると証明できれば、`x`を`D`に絞り込む。`D`と`S`が無関係と証明できれば、可能な動的型がなくなる。その到達可能な経路では`x`を宣言型で検査し、後続の検査も事実を追加しない。警告を出してもよい。それ以外では何も追加しない。

合流点では、可能な動的型が残るすべての流入経路で保証される、最も派生した基底を保つ。どの流入経路にも可能な動的型がなければ、合流後も存在しない。合流するのは実行時の到達可能性が保つ経路だけである（[§14.9.3](#s14-9-3)）。`if`と`while`は真の事実を本体へ、偽の事実を後続へ渡す（`else if`は直前の偽の事実から始める）。`require`は真の事実を後続の文へ渡す。ループ入口ではすべての戻り辺を合流させ、最も絞り込まれた安定解を結果とする。`defer`本体は登録位置の事実を保つ（[§16.1.2](#s16-1-2)）。関数とキャプチャーは事実を取り込まない。

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

<a id="s14-10-3"></a>
#### 14.10.3. 到達不能なコード

実行時の到達可能性のどの経路も到達しない項目も、仮定した経路から作る状態で検査する。仮定した経路では、次のNever形式を通常の完了に至るものとして扱う。制御転送、`$abort`、結果型がNeverの呼出しは、オペランドを評価した後、後処理の前に、自身の字句スコープで継続する。その他のNever型の式は、評価後、明示的な取得の効果を反映して継続する（`x@move`は`x`をMoveする）。自身宛ての`exit`がない`loop`は、反復入口の状態で継続する。そのような形式には依然として値がないため、仮定した完了でも配置先を初期化しない。`let y: i32 = fail()`の後では`y`は未初期化である。後処理も同じ方法で検査するため、後処理が受渡しを妨げる制御転送も、受け渡されたと仮定する。実際の流入経路が1つでもある項目は、実際の経路だけを使う。それ以外では、仮定した流入経路を合流し、そのすべてで成り立つものだけを保つ。放棄された比較だけが必要としていたLoanは、仮定した継続の前に終わる。この状態ですべての検査を適用する。未初期化またはMovedの値の読取り、2回目のMove、Loanの衝突はエラーとなり、実効型と条件の事実は実際の経路と同じように引き継ぐ。仮定した経路は、暗黙の結果、通常の完了、実行時の到達可能性の辺を追加しない。到達不能なコード内の明示的な結果源は引き続き数える（[§14.9.1](#s14-9-1)）。

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

<a id="s14-11"></a>
### 14.11. `require`

`require Expression else Body`は`bool`条件を一度評価し（[§14.2.3](#s14-2-3)）、trueなら後続へ進み、falseなら失敗本体を実行する。`else`は条件の最終行、またはその次の行に`require`と揃えて置く。`require`は結果、ラベル、対象、境界を持たず、`if`ではない。

失敗本体には常に入ると仮定し、条件が`false`でも静的な真の経路を保つ。後処理も含め、失敗本体を通るどの経路も、文の後へ通常どおり継続してはならない。外向きの制御転送、Neverの呼出し、発散、Abortはこの要件を満たす。暗黙に何かを挿入することはない。

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

---

<a id="s15"></a>
## 15. 所有権と寿命解析



この章は、Place（[§3.4](#s3-4)）、借用、Originのどの使用が有効かを定める。実装が転送や一時値を省略できるのは、値、Placeの同一性と寿命、Move状態、Loan、Origin、破棄責任、失敗が変わらない場合だけである。最適化によってプログラムの有効性は変わらない。

<a id="s15-1"></a>
### 15.1. 初期化とMoveの解析

<a id="s15-1-1"></a>
#### 15.1.1. 初期化状態

| 状態 | 意味 | 読取り・借用・Copy・Move | `let`への書込み | `var`への書込み |
| --- | --- | --- | --- | --- |
| Uninitialized | 値を保持しない | エラー | 一度も初期化していない場合だけ | 初期化 |
| Initialized | 値を保持する | 型とアクセスの規則に従う | エラー | 置換 |
| Moved | 値と責任を転送済み | エラー | エラー | 再初期化 |

- `let`は各経路で、束縛の寿命につき一度だけ初期化できる。Moveしてもその回数はリセットされない。状態だけで操作が許可されることはない。
- 読取り・借用・Copy・Moveには、実行時到達可能性の全ての流入経路でPlaceがInitializedであることが必要（[§14.9.3](#s14-9-3)）。到達不能なコードは[§14.10.3](#s14-10-3)の仮定した経路の状態で検査する。経路が存在しなくても、使用不能な値が使用可能にはならない。

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

<a id="s15-1-2"></a>
#### 15.1.2. 構築と完全性

**完全な値**は、**構築完了**（構築が終了したこと）と**現在の完全性**（格納する全構成要素がInitializedかつ完全であること）の両方を満たす。構造体の構成要素は基底部分オブジェクトと自身のフィールドであり、層ごとに完了を追跡する。enumの構成要素は現在のケースのペイロードであり、他のケースの初期化は不要。全ての格納フィールドを初期化しなければならない。計算プロパティは構成要素ではない。

完了を確定するのは構築だけである。コンストラクターの各段階（[§6.3](#s6-3)）、ケース構築（[§6.5.2](#s6-5-2)）、タプル・配列の構築、`noinit`（[§4.3.4](#s4-3-4)）、操作の契約が定める構築が該当する。型を読み替えたPlaceは完全な値を保持し、再配置は値の構築完了を保つ（[§5.2](#s5-2)）。他の操作は完了を確定もリセットもしない。不完全なままの脱出やAbortでも確定しない。コンストラクターは、成功を検証し、記憶領域が生存したまま後処理を終えた後、値を外部に見せたり転送したりする前に完了を確定する。

- 構築完了していないPlace（一度も初期化していないか、値全体をMove済み）は、値全体の配置でしか初期化できない。その一部への書込み・読取り・借用はエラー。唯一の例外は、構築レシーバ自身の格納フィールド（[§6.3.4](#s6-3-4)）であり、`self.inner.x`のような入れ子の部分は含まない。
- 不完全な値では、値全体の読取り・Copy・借用・Move・外部への公開はエラー。`self`全体を受け取るアクセサーを通じた操作も同じである。
- 部分Moveが変えるのは現在の完全性だけ。失われた全ての部分を再初期化すれば、コンストラクターを実行せずに完全性が戻る。それまでは不完全なままだが、Initializedな部分は使用でき、後処理もできる。値全体のMoveは構築完了の事実を転送し、値全体の置換は新しい値の事実を引き継ぐ。
- タプル・配列の構築は添字の昇順に要素を配置する。配置が完了した要素は初期化済みとなり、破棄責任を持つ。集約値は全要素の配置後に完了を確定する。未完了の構築から制御が外へ転送された場合、配置済み要素を[§16.2.1](#s16-2-1)に従って後処理する。巻き戻しは行わない。
- 固定長配列の初回構築は全体に対して行う（[§4.3.2](#s4-3-2)）。`noinit`の後、読取り前に要素へ有効な値を与えるのはプログラマーの義務である。

```kimi
var pair: (string, i32)
// pair.0 = "a"           // Error: pair was never constructed.
pair = ("a", 1)           // Whole initialization.
let name = pair.0@move    // Partial Move: pair is incomplete.
// let all = pair@move    // Error: incomplete.
pair.0 = "b"              // Reinitializes a Move Path; pair is complete again.
let all = pair@move
```

<a id="s15-1-3"></a>
#### 15.1.3. Move Pathと部分Move

**Move Path**は、独自の初期化状態と責任を持ち、静的に追跡できる経路である。静的に分かる基底部分オブジェクトの経路を通じたフィールド、タプル要素、定数添字の固定長配列要素、およびそれらの組合せが該当する。実行時の添字、動的コンテナー、ユーザー定義のインデクサーはMove Pathを形成しない。定数伝播や整数の証明でも追加されない。

**定数添字。** 定数添字は、0以上の整数リテラル一つを、任意個のグループ化括弧で囲んだもの。基数は任意で桁区切りも許し（[§2.6](#s2-6)）、`i32`に収まらなければならない（[§4.6.2](#s4-6-2)）。値`n`が`0 <= n < N`を満たす場合だけ、`[N of T]`の要素Move Pathを形成する。同一性には値を使うため、`1`、`0x1`、`(1)`は同じ要素を指す。`1 + 1`、`-1`、`3@isize`、名前、選択式、`^1`は定数添字ではない。範囲外のリテラルは経路を形成しないが、それ自体はエラーではない。実行されれば境界検査でAbortする（[§17.4.4](#s17-4-4)）。Move Pathが必要な操作で、経路が存在しない場合はエラーとなる。

- Move Pathを形成しない添字で到達する要素は、配列全体が完全な場合だけInitializedとなる。要素の一つでもMovedまたはUninitializedなら、その添字での読取り・借用・Copy・書込みはエラー。
- 所有するmatchのSubject内では、選択したケースのペイロード位置もMove Pathとなる（[§15.1.6](#s15-1-6)）。
- Move Pathが定めるのは追跡の粒度であり、操作の許可ではない。`@move`は要素、または[§11.2](#s11-2)に従う格納プロパティを転送する。裸の読取りはCopyな値をCopyする。
- 借用を通じて到達したNon-Copyの参照先やその一部はMoveできない。`exchange`または`swap`を使う（[§15.7](#s15-7)）。
- フィールド経路が途中の計算ゲッターを飛び越えることはない。

**部分Moveと`drop`。** 値全体の各使用（全体を受け取る呼出しも含む）の直前、およびユーザー定義の`drop`の実行前には完全性が必要。祖先の`drop`は部分Moveを禁止しないが、そのような点へ到達する全経路で完全性を戻さなければならない。通常完了、`return`、`try`の伝播、`exit`、`continue`、`yield`、`defer`本体も含む。復元用の値を`try`から得る場合、失敗によるreturnでも値を破棄できなければならない。Abortは巻き戻さない。`defer`や内側のデストラクターによる復元は実行順に検査し、二重に破棄するものはない。

```kimi
var items: [2 of Resource] = [Resource.init(), Resource.init()]
let i: isize = 1
let first = items[0]@move  // Partial Move: items is incomplete.
inspect(items[1])          // Valid: literal index, element 1 is Initialized.
// inspect(items[i])       // Error: a runtime index needs a complete array.
items[0] = Resource.init() // Complete again.
inspect(items[i])          // Valid.
```

<a id="s15-1-4"></a>
#### 15.1.4. Consume検査

**Consume**は、使用箇所で[§15.1.5](#s15-1-5)のアクセス操作を行えるかを調べる静的検査。**Eligibility**は構造上の条件であり、Placeの種類、所有経路、Move Path、フィールド宣言、`drop`の制限によって操作の有無が決まる。**Legality**は使用箇所ごとの条件である。アクセスが許可され、対象が全流入経路でInitializedかつ完全であり、必要なレシーバの構築が完了し、競合するLoanがなく、Originが有効で、実際の祖先が操作を許すことを確かめる。制約が証明するのは構造上の事実だけであり、未知の事実はジェネリックのアクセス効果（[§8.14](#s8-14)）に従う。対象がInitializedかつ完全で、祖先全体にアクセスせず位置を特定できるなら、祖先は不完全でもよい。ユーザー定義の`drop`によって経路が不適格になることがあるため、各使用で実際の祖先を検査する。状態、責任、構築完了、完全性は、分岐・ループ・制御転送・`defer`を通じて追跡する。

<a id="s15-1-5"></a>
#### 15.1.5. アクセス操作と取得

**Move可能なPlace**はTakeを提供する。追跡されるPlaceには、完全であり、追跡可能な所有経路で到達することも必要。raw Placeとそのフィールド・要素は、状態追跡なしでTakeを提供し、完全性は作成者の義務となる（[§5.2](#s5-2)、[§5.2.3](#s5-2-3)）。所有ルート、許可されたインラインフィールド・タプル要素、定数添字の固定長配列要素はTakeを提供する。借用の参照先、オブジェクトのペイロード、staticな記憶領域、動的コレクション要素、その他の添字、隠れたプロパティの記憶領域はTakeを提供しない。`remove`や所有イテレーターは、それぞれの契約に従って要素を取り出す。

| 操作 | 必要条件 |
| --- | --- |
| Copy | Read、完全であること、完全な型がCopyであること |
| 共有借用 | Read、完全であること、有効なPlaceと依存 |
| 排他借用 | ReadとWrite、完全であること、貸出し点に排他的に書き込めること（後述） |
| Move | Take。追跡されるPlaceでは、完全で、追跡可能な所有経路があることも必要 |
| 初期化 | Uninitialized、宣言による初期化の許可 |
| 置換 | Write、旧値を破棄できること、旧値・新値の依存を壊さないこと |

どの操作でも、アクセス、Origin、Loanの競合を検査する。`let`の初回初期化と構築中の初期化は専用の許可であり、WriteやTakeではない。Movedな`let`は再初期化できず、Uninitializedな記憶領域は`uniq/T`として借用できない。ジェネリックな所有値はContractなしで全体をMoveできる。未知の`T`は`T`として取得する。raw Placeには[§5.2](#s5-2)と[§5.2.1](#s5-2-1)の義務が引き続き課される。

**貸出し規則。**

- `@move`はMove可能なPlaceを要求し、Copyの値でも転送する。元をMovedとし、完全な型、依存、責任を転送する。参照をMoveするのは、その能力の移動であり、参照先の移動ではない。Movedな`var`の復元には書込み許可が必要。所有一時値は明示的な記法なしで転送する。
- 新たな排他借用は、アクセス経路によらず`@uniq`または`@objuniq`と書く。ただし、暗黙に取得するレシーバ式は例外とする（[§7.3](#s7-3)）。
- 借用値には明示的な記法が不要。固定された期待型の要求するモードで再借用するか、裸の取得を行う。自身のモードや経路の権限より強い取得にはならない。その値への`@ref`や`@uniq`は、値を格納するスロットを借用する（[§13.5.6](#s13-5-6)）。
- その他の位置（引数、初期化子、代入元、結果、既定値、要素、ペイロード）では、期待型が固定されていれば共通Adaptation（[§10.2](#s10-2)）、そうでなければ裸の取得（[§3.5](#s3-5)）で値を取得する。ゲッターの結果は結果として取得し、隠れた記憶領域からMoveすることはない。

**排他的な取得。** 所有Place、所有一時値、明示的な`@uniq`で借用する参照スロットの排他的な取得には、明示・暗黙のどちらでも、貸出し点（[§3.4](#s3-4)）に排他的に書き込めることが必要。

- 書込み可能なルートは、`var`局所変数、可変なstatic記憶領域、所有一時値、構築レシーバ自身の初期化済みフィールド（[§6.3.4](#s6-3-4)）、破棄レシーバ（[§16.3.1](#s16-3-1)）。
- 標準の射影はルートの許可を引き継ぎ、[§11.2](#s11-2)のプロパティの許可とLoanの制限を加える。
- `let`束縛や所有型のパラメーターをルートとする記憶領域、および所有するゲッター結果とそのインライン部分（[§11.5](#s11-5)）は排他的に取得できない。クロージャー環境の束縛は所有型のパラメーターではなく、`let`か`var`かで可変性が決まる（[§7.7.2](#s7-7-2)）。

この条件は借用する記憶領域に適用する。返された参照を通じて到達する参照先には、それ自身の許可がある。`let`やパラメーターに保持した`uniq`値は、引き続き排他的に再借用できる。

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

<a id="s15-1-6"></a>
#### 15.1.6. matchとforのSubject

`match E`と`for`は、通常の取得（[§3.5](#s3-5)、[§13.5](#s13-5)）によって`E`を一度評価し、内部の**Subject Place**に格納する。ただし、裸のPlaceはその場で借用する。

| Subject `E` | Subject Place |
| --- | --- |
| 一時値の標準射影も含む裸のPlace（[§3.4](#s3-4)） | Placeの共有借用。Placeが`ref/T`または`uniq/T`を格納する場合は、その参照自身のモードでの再借用。ただし、経路に共有層があればSharedを上限とする |
| その他の式（`E@ref`、`E@uniq`、`E@move`、`E@copy`、一時値など） | その式が生成する値 |

**Subjectモード**は、Subject Placeが安全な参照層の先に与えるアクセスであり、Shared（`ref`、`objref`）、Exclusive（`uniq`、`objuniq`）、ByValue（所有）のいずれか。条件を満たすペア層では、許可されるケースのうち最も弱いモード（[§13.5.5](#s13-5-5)）であり、ByValueにはならない。オブジェクトのSemanticsを許すものなど、他のペア層は安全な参照層ではないため、束縛はそのスロットを値として受け取る。経路のどこかに共有層があれば、モードの上限はSharedとなる。モードは`var`、Copy能力、適合、本体に依存しない。明示的な借用は書かれたスロットに適用するため、`let values: uniq/Array<T>`への`values@uniq`はエラー。`values`または`values@follow@uniq`と書く。`for`はモードから反復の入口を選び（[§14.6.2](#s14-6-2)）、パターンは[§14.8.1](#s14-8-1)に従ってPlaceを選ぶ。

- Subject Placeはアーム選択前に初期化する。独自・計算・要件の`get`を持つプロパティは一度だけ読み、Subject Placeはその一時Placeとなる。[§11.5](#s11-5)の制限も適用する。
- ペア層の先も含め、Exclusiveモードが排他アクセスを与えるのは、貸出し点を排他的に取得できる場合だけ（[§15.1.5](#s15-1-5)）。それ以外で排他アクセスを必要とする束縛や`for`の入口はエラーとなり、共有束縛にはならない。
- パターン検査とガード中は[§14.8.3](#s14-8-3)を適用する。アーム選択後は、借用SubjectのLoanはその束縛の使用に従うため、生存する借用束縛のないアームは元のPlaceを置換できる。

**束縛。** ガード付きアームは、ガードがtrueとなり、その後処理が完了した後にだけ選択する。選択したアームの局所変数は左から右に初期化する。

| 選択したPlace | 束縛する値 |
| --- | --- |
| 所有Subjectまたは反復要素の一部 | 格納された完全な型`T`の値とその責任を転送 |
| 共有アクセスで到達 | `ref/T`。`T`がCopyでも同じ |
| 排他アクセスで到達 | `uniq/T` |
| `_` | 何も束縛せず、Subjectが責任を保持 |

- `let`と`var`が決めるのは、局所変数への再代入の可否だけ。借用経路上の`var`束縛は、再代入可能な参照となる。
- 借用Subject全体の束縛は、元のPlaceへの参照を束縛する。格納された`ref/E`または`uniq/E`を再借用するSubjectでは、そのPlaceは参照先であり、SharedのSubjectなら`ref/E`、Exclusiveなら`uniq/E`として束縛する。所有Subject全体の束縛は、それを転送する。
- 参照を通じた部分の束縛にはその経路の能力を使い、そこからMoveしない。所有Subjectの束縛しなかった部分はSubjectに残る。ペイロードのCopy能力は束縛型を変えない。

**寿命。** Subjectの一時値は構文が終わるまで生存する（[§3.6](#s3-6)）。まず結果や外向きの転送値を確保し、次にアームのスコープを後処理し（本体の束縛は逆順）、最後にSubjectの残りの部分を破棄する。ガードから外向きに転送する場合は、Subjectより先にガードの一時値を後処理する（[§16.2.1](#s16-2-1)）。借用Subjectを通じた借用は、内部スロットではなく、参照先と元のLoanに依存する。所有Subjectのペイロードへの新たな借用は、matchの外へ逃避できない。

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

<a id="s15-2"></a>
### 15.2. Origin

OriginとLoanの概要は[§3.7](#s3-7)に示す。

<a id="s15-2-1"></a>
#### 15.2.1. Origin式

**Origin**は記号的な寿命であり、名前、射影（`x.source`）、`static`、共通部分`(a and b)`のいずれかで表す（文法は[§F](#sf)）。各Originは**領域**`region(o)`、すなわち借用が有効なプログラム上の点の集合を表す。型の同一性では、正規化した記号形式でOrigin束縛を比較する（[§15.2.2](#s15-2-2)）。`outlives`と`==`は領域を制約する。

具体的なOrigin（`x`、`self`）は、パラメーター・レシーバ・局所変数の外側の借用Originである。ただし、struct/enumの記憶領域内のraw層では、`self`はそれを保持する値を指す（[§5.7](#s5-7)）。抽象的なOrigin（`source`）は、型スロットまたは暗黙に導入するシグネチャのOrigin。射影（`x.source`、`set.source`）はスロットを選ぶ。

- 安全な借用値をOriginとして使うと、その値が保持する借用の外側のOriginを表し、格納領域は表さない。そのスロットの借用（[§13.5.5](#s13-5-5)）には推論したOriginが付く。外側に安全な借用Originを持たない値はOriginではない。
- `x.source`は`x`の型のスロット`source`を射影する。局所値がOriginを提供するのは字句スコープ内だけであり、公開シグネチャでは使えない。

```kimi
struct View<T> {source}
    let data: ref/T during source
    func get(self: ref/Self) -> ref/T during self.source => self.data
```

<a id="s15-2-2"></a>
#### 15.2.2. 順序と共通部分

`origin a outlives b`は`region(a) ⊇ region(b)`を要求する。この関係は反射的・推移的で、`static`は全てのOriginより長く生存する。`origin a == b`は領域の等価性を要求するが、Loanを統合しない。`a and b`は交わりであり、`region(a and b) = region(a) ∩ region(b)`となる。

共通部分は交換則・結合則・冪等則を満たし、`a outlives b`が証明されると`a and b = b`となる（[§15.3.6](#s15-3-6)）。比較前に平坦化し、等しいと証明されたOriginを一つのオペランドとして扱う。その後、重複と、他の残るオペランドより長く生存すると証明されたオペランドを取り除く。1要素の共通部分はそのオペランド自身となる。得られたオペランド集合が等しければ、二つの共通部分は等しい。証明には検査中の宣言の前提だけを使う。二つの契約を比較する場合（[§15.3.7](#s15-3-7)）、それぞれ自身に宣言された前提で正規化する。オペランドの順序は観測できない。Origin式を簡約しても、別の入力のLoan依存は取り除かない。

<a id="s15-2-3"></a>
#### 15.2.3. `static`と`Owned`

`static`の間の借用は[§11.8](#s11-8)を満たさなければならない。可変なstatic記憶領域への新たな安全な借用には有限Originを与える（[§15.6.5](#s15-6-5)）。安全なコードでは、Loan要件が`uniq`であるOriginを`static`に束縛できない（[§15.5](#s15-5)）。raw Placeの借用には新たな基点を与え、その借用自身には上限がない（[§5.2.2](#s5-2-2)）。

`Owned`はnon-staticな寿命に依存しないことを示す型制約であり、所有権やメモリー割当ての許可ではない。完全な型`T`が`Owned`となるのは、**OwnedOrigins(T)**内の全Originが`static`と等しい場合に限る。別名の展開と束縛の代入後、OwnedOrigins(T)は次を収集する。

- 外側のOrigin（raw層の`self`以外のOriginも含む。[§5.7](#s5-7)）と、Semanticsの対象（参照先、ペイロードまたはView Target、生ポインターの指示先の型）。
- 実体化された全ての型引数・Origin引数。未使用のものや継承したものも含む（[§6.1.3](#s6-1-3)）。
- 基底、格納フィールド、全ケースのペイロード、タプルの構成要素、配列要素、具体的なクロージャーのキャプチャー。
- Function Itemの束縛済み引数、具体的なクロージャーのシグネチャの固定Origin、共通の関数型に書かれた固定Origin。呼出しごとのOrigin（[§8.10](#s8-10)）は除く。共通の関数型の隠れた環境と、基底ビュー・実行時Contractビューのペイロードは、型消去時に検証済みである（[§15.8.1](#s15-8-1)）。

再帰型は[§15.3.5](#s15-3-5)に従う。これらの依存はLoanの存在を意味しない。空のコンテナーや選択されていないケースも免除しないため、`raw/(ref/i32 during local)`はOwnedではない。`a outlives static`は`a`と`static`の等価性を証明する。未証明のOriginは最終性の規則でUnknownとなる（[§8.3](#s8-3)）。ジェネリック定義は、宣言された制約だけから`Owned`を証明する（[§8.15](#s8-15)）。

`Owned`を要求するのは、static記憶領域、ビューへのペイロードの型消去、関数型の環境の型消去、循環ファクトリーのペイロード（[§13.6.2](#s13-6-2)）、宣言された制約だけである。取得、Loan、破棄順序、unsafe、並行処理の検査を免除しない。`Owned`の証明失敗は、関数型への変換（[§7.7.4](#s7-7-4)）でも制約の失敗（[§8.3](#s8-3)）であり、型の不一致やOrigin関係の失敗にはならない。

<a id="s15-2-4"></a>
#### 15.2.4. `Transferable`

`Kimi.Transferable`はパラメーターもメンバーも持たないコンパイラー組込みContract（[§8.7.1](#s8-7-1)）。記述した名前は通常の名前探索に従い、以下の規則を持つのはKimiの宣言だけである。適合は構造で決まり、ユーザー宣言で与えることはできない。構造化されたワーカーが受け取れる値（[§22.3.6](#s22-3-6)。これが唯一のワーカー境界）と、`confined`な実装が読める不変のstatic（[§8.11.2](#s8-11-2)）を定める。`Owned`は要求しない。呼出し元の記憶領域の借用も条件を満たせるが、そのLoanは合流まで保持する。同期は提供せず、外部コードからの並行した再入も許可しない。

- 具体的なオブジェクトのペイロードも含め、OwnedOrigins（[§15.2.3](#s15-2-3)）の構成要素をたどる。呼出し可能シグネチャだけでは何も格納しない。空のコンテナーや非アクティブなケースでも構成要素の型を免除しない。
- 到達可能な`rc`、`rc`のWeak、生ポインターが一つでもあれば、`ref`や`arc`の先でも適合を否定する。共通の関数型、実行時Contractビュー、基底オブジェクトビューも同じ。
- 唯一の例外は、Kimiのコレクション型`Array`、`Dictionary`、`Slice`、`UniqSlice`と、[§22.2.2](#s22-2-2)の標準イテレーターの内部の生ポインター。実装はワーカーでの使用でもその健全性を保つ。`Async.Sender`や`Async.Receiver`の内部状態を含む他の全ての生ポインターは、`Transferable`への適合を否定する。ユーザーコードはこの例外を主張できない。
- 再帰型は[§15.3.5](#s15-3-5)に従う。循環によって除外対象の構成要素が隠れることはない。抽象型には前提が必要（[§8.3](#s8-3)）。

```kimi
func pass<T>(value: T) -> T
    T is Transferable and Owned
    return value@move

func caller(local: ref/i32)
    let a = pass((1, "x"))                                  // Valid.
    // let b = pass(local)                                  // Error: ref/i32 during local is not Owned.
    // let c = pass(Kimi.Intrinsics.makeRc(Item.init(7)))   // Error: rc refutes Transferable.
```

<a id="s15-3"></a>
### 15.3. Originのスキーマ・名前・関係

**束縛集合**は、型の出現箇所のスキーマスロットをOriginに対応付ける。関数のOriginパラメーターリストや、型へのOriginの対応指定はない。関連型はOriginパラメーターを宣言できる（[§8.5.4](#s8-5-4)）。

<a id="s15-3-1"></a>
#### 15.3.1. 借用注釈と束縛集合

`ref/T during o`は、一つの借用層のOriginを設定し、[§3.3.2](#s3-3-2)に従って適用する。`during`を許すのは`ref`、`uniq`、`objref`、`objuniq`、`raw`、ペアバインダーだけ。ペアバインダーでは条件付きスロットであり、許容集合の借用ケースだけに存在する（[§8.1.2](#s8-1-2)）。`raw`ではポインターが指す記憶領域を表す（[§5.7](#s5-7)）。引数はOriginの基本式一つ（名前、`value.slot`、`set.slot`、`static`、丸括弧で囲んだ式。共通部分は`during (a and b)`と書く）。`raw`では共通部分を使わず、`self`を使える。注釈はLoanを作らず、寿命を延ばさず、取得や変換も行わない。

- **束縛集合。** 名前付き型の後の`{name}`は、その出現箇所の集合に名前を付ける。型のスキーマは空でなく、定義時に分かっていなければならない。名前の重複、空の波括弧、集合を単一のOriginとして使うことはエラー。波括弧は集合を命名するだけであり、位置順でも`{source => x}`のような対応指定でもOriginを適用しない。集合全体の等価性や、`f{a}(...)`のような呼出し箇所での適用もない。位置順のOrigin適用は関連型だけに存在する（[§8.5.4](#s8-5-4)）。未使用の集合名は契約を変えない。
- **単一スロットの束縛。** 既知のスキーマがちょうど一つのスロットを持つ型では、`Slice<T> during source`は、`Slice<T>{v}`と`origin v.source == source`を意味する。借用層は追加しない。シグネチャでその位置の名前が未束縛なら量化する（[§15.3.4](#s15-3-4)）。他の型では拒否する。
- **射影。** `value.source`は値の型に宣言されたスロットを選ぶ。連続した安全な借用層だけを取り除き、フィールド・型引数・生ポインターはたどらない。`set.source`は名前を付けた型の出現箇所のスロットを選ぶ。未知のスロットはエラー。射影が読むのは型情報だけである。

```kimi
func identity<T>(value: View<T>) -> View<T>{result}
    origin result.source == value.source
    return value@move

func tail<T>(values: Slice<T> during source) -> Slice<T> during source
    return values[1..]
```

<a id="s15-3-2"></a>
#### 15.3.2. 型のスキーマと記憶領域

struct/enumは、ジェネリックパラメーターの後のヘッダーで、自身のOriginスロットを一つ以上の新しい単純名として宣言する。重複、空の波括弧、継承した名前の再宣言はエラー。ヘッダーがなければ自身のスロットはないが、それだけでOwnedにはならない。groupとContractはスロットを宣言しない。

- **記憶領域内の名前**（フィールド・ペイロード・基底）は、宣言されたスロット、可視な外側のOrigin（[§15.3.4](#s15-3-4)）、またはraw層の`self`（[§5.7](#s5-7)）に解決する。未宣言の名前はエラー。入れ子の型は、字句的に外側のスロットを引き継ぐ。
- **断片**は、共有の制約領域と生成メンバーも含めて[§6.1.2](#s6-1-2)に従う。enumは分割できない。
- **安定性。** スロット名はAPIの一部（[§15.3.7](#s15-3-7)）。どの記憶領域でも使わないスロットは、Phantom Originのままとなる（[§15.3.5](#s15-3-5)）。
- **記憶領域の補完。** 格納する全ての借用層と集約型スロットは、型の公開Origin、`static`、完全な型引数、注釈、関係から補完する。初期化子から推論せず、隠れたスロットも作らない。フィールド局所の集合は、そのフィールドと付随する節だけで可視となる。残るフィールド・ペイロードの条件は、型の公開前提と整形式性から導けなければならない。追加の前提はその型の制約領域に置く。

```kimi
struct Renamed<T> {buffer}
    let data: ref/T during buffer
    let other: ref/T during souce       // Error: souce is not declared.

func forward<T>(value: Renamed<T>) -> Renamed<T>{result}
    origin result.source == value.source   // Error: Renamed has no slot source.
    return value@move
```

<a id="s15-3-3"></a>
#### 15.3.3. 宣言に付随する関係

`origin a == b`と`origin a outlives b`は、それぞれ一つの関係を表す。複数の節は論理積となり、連鎖・論理和・否定・実行時検査はない。関数や型の節は先頭領域に置く（[§7.5](#s7-5)）。フィールド、局所変数、enumケース、関連型の指定、コンテナー別名では、宣言の下に字下げして節を付け、アクセサーや本体より前に置く。全ての名前がその位置で可視でなければならない。

| 対象 | 義務 |
| --- | --- |
| 公開関数・公開型 | 許可される全束縛について定義を検査し、各使用で代入後の前提を証明する |
| フィールド・ペイロード・関連型の指定 | 残る条件を外側の公開契約から証明する |
| 基底型 | 追加の前提を型の制約領域で公開する |
| 局所変数・コンテナー別名 | 確立した根拠と初期化子に照らして検査する。新たな仮定は置かない |

宣言のパラメーターに依存しない閉じた条件は、定義時の義務であり、偽ならエラー。

<a id="s15-3-4"></a>
#### 15.3.4. 名前・スコープ・量化

名前付きの呼出し可能宣言のシグネチャでは、借用注釈内の未束縛の単純名は全称量化されたOriginを導入する。結果だけに現れる名前も同じ。関係と局所注釈は名前を参照するだけである。特殊化と、継承した格納プロパティのアクセサーは元の契約を保つ。

| 名前の所属 | 可視範囲 |
| --- | --- |
| 呼出し可能シグネチャのOrigin・集合 | シグネチャ、節、本体 |
| 型のスロット | 型宣言と継承メンバー |
| フィールド・ケース・関連型指定の集合 | その宣言と付随する節 |
| 基底型の出現箇所の集合 | 基底節を記述する断片の制約領域（[§6.1.2](#s6-1-2)） |
| 関連型のOriginパラメーター | 右辺、形成型、節（[§8.5.4](#s8-5-4)） |
| 局所の集合 | その型、節、後続のスコープ |
| コンテナー別名の集合 | 付随する節と、その文書の別名スコープ |

- 探索は内側から外側へ進み、Origin文脈の候補がある最初のスコープで止まる。同じスコープで値・Origin・集合の役割が競合すればエラー。異なる役割への一致を理由に外側へ探し直すことはない。レシーバのない裸のフィールドは候補にならない。型パラメーター名は別の名前空間に属する。
- 新しいOrigin名や集合名は、可視なOrigin・集合・パラメーター・局所変数を隠せない。重複はエラーだが、互いに不可視なフィールド局所の名前は同じでもよい。解決は宣言順に依存しない。
- 名前付きの入れ子関数のシグネチャは、外側の関数の可視なOriginを指定できる。その名前は入れ子関数の固定Originであり、新たな全称Originではない。実行時には何もキャプチャーしない。その入れ子関数の関数値は、型でそれらを指定する他の値と同じく、そのOriginに依存する。

**入れ子のシグネチャ。** 関数型、Callable、匿名関数は名前付きOriginを導入しない。呼出しごとの直接入力の量化と、自身の結果省略規則を保つ（[§8.10](#s8-10)）。それらの集合名は外側の宣言に属する。入れ子の集約型入力の各スロットは、完全な型または外側のOrigin式で固定する。ただし、初期化子を持つ局所変数の型と匿名関数のパラメーターでは、省略したスロットや、入れ子の借用層の省略Originは、本体の推論変数として一度だけ固定する（[§15.3.6](#s15-3-6)）。内側の呼出しごとのOriginは、そのバインダーの外へ逃避できない。

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

<a id="s15-3-5"></a>
#### 15.3.5. 変性・Loan要件・Phantom Origin

**変性。** 安全な借用は外側のOriginについて共変であり、raw層も自身のOriginについて共変である（[§5.7](#s5-7)）。`uniq`、`obj`、`objuniq`、`raw`は対象について不変。他のSemanticsは全て対象について共変である。これは`ref`、`objref`、`rc`、`arc`が変更を許可しないためである（[§15.6.2](#s15-6-2)）。宣言の変性ではペア層を共変と数えるが、ジェネリック本体では不変なSemanticsを許す場合、その対象は不変となる。関数のパラメーターは極性を反転する。型の変性は記憶領域から導き、変性注釈はない。

**Loan要件。** 各スロットには、共有・排他の使用から決まる`none < ref < uniq`の要件がある。入れ子の使用は、より強い要件を伝える。raw層のOriginは使用とは数えず、`during self`の指示先はその型の格納内容として数える（[§5.7](#s5-7)）。この要件は保持すべき呼出し側のLoanを指定するだけで、何も許可しない。Originが等しい、または短くなったことによって、Loanが統合されたり排他アクセスが生じたりすることはない。

**不動点。** 変性、Loan要件、OwnedOrigins、`Transferable`は、宣言ごとに自身の型パラメーターとスロットを使って一度計算し、実体化を展開して計算しない。各計算は最小値（未使用、`none`、空集合、除外なし）からの最小不動点を求めるため、`rc/Nest<(T, T)>?`フィールドのような非正則なものも含め、再帰型で停止する。

**Phantom Origin。** 格納フィールドをたどるどの連鎖も、格納された安全な参照や`Loan<T>`フィールドへ到達しないスロットは**Phantom**である。そのスロットに束縛された入れ子の型のスロットも連鎖に含める。依存は保つが、有効性・Loan・Copy・アクセスは与えない。要件は`none`で、変性は不変となる。

**`Kimi.Loan<T>`**は、記憶領域なしで依存を表す、コンパイラー管理のサイズ0の構造体。`T`は外側が`ref`、`uniq`、`objref`、`objuniq`のいずれかである借用型でなければならない。`Loan<T>`フィールドは、スロットの全性質について`T`を格納するものと数える。`T`がCopyならCopyであり、アドレスを持たず、アクセスも与えない。`Loan<T>.init(value: T)`は安全で、借用の依存を保持する。破棄すると自身の責任だけが終わり、Loanはそれを保持する値が使われる間は生存する（[§15.6](#s15-6)）。`Slice<T>`（[§22.1](#s22-1)）、`UniqSlice<T>`（不変。[§4.8.4](#s4-8-4)）、`Array<T>`（[§4.5](#s4-5)）のスロットの性質は、それぞれの規則で決まる。`Weak<S>`は`S`を格納するものと数える（[§3.2.2](#s3-2-2)）。

```kimi
struct Window<T> {source}               // Covariant in T and source.
    let loan: Loan<ref/T during source>
    let data: raw/()

struct Node<T> {a}
    let value: ref/T during a
    let next: Option<rc/Node<T>{n}>
        origin n.a == a                 // Recursion adds nothing; a is covariant.
```

<a id="s15-3-6"></a>
#### 15.3.6. Originの推論

推論変数は未解決の位置だけに置き、変性・関係・使用によって制約する。

| 位置 | 候補 |
| --- | --- |
| 共変 | 等価性で固定されていなければ、全上限の共通部分 |
| 不変 | 証明された等価性。共通部分は使わない |
| 反変 | 他の全下限より長く生存すると証明された下限 |

`outlives`関係の循環でつながる変数は等しく、一つとして解く。共変と反変の両方の位置にある変数は不変として解く。残る各変数は、自身の行に該当する種類の境界だけから候補を求め、その候補に対して他の境界を検査する。変数の境界は他の変数をたどって、固定Origin・有限Origin（[§15.6.5](#s15-6-5)）・`static`まで追う。どれにも到達しなければ候補はない。その行で候補がない場合、実行可能本体内なら局所領域となり（[§15.6](#s15-6)）、`static`で置き換えてはならない。それ以外では注釈が必要。推論はオーバーロード選択後に判定し、候補を適用不能にはしない。

ソルバーが使う規則は次に限る。反射性、推移性、`static`は全Originより長く生存すること、`==`の代入、[§15.2.2](#s15-2-2)の法則、`a outlives (a and b)`と`b outlives (a and b)`、および`(a and b) outlives c`が成り立つのは`a outlives c`と`b outlives c`が共に成り立つ場合に限ること。右辺の共通部分を別々の要件に分けることはないが、`x outlives a`は`x outlives (a and b)`を証明する。含意は前提の順序や重複に依存せず、問合せ対象の式、前提、その部分式以外のOriginを形成しない。UnknownはRefutedと異なり、最終性の規則で判定する（[§8.3](#s8-3)）。

```kimi
func choose<T>(x: ref/T, y: ref/T) -> ref/T  // Result Origin: (x and y).
    return x                                 // Valid: x outlives (x and y).
```

<a id="s15-3-7"></a>
#### 15.3.7. 契約の比較

整形式性は定義の前提であり、使用箇所の義務でもある（[§15.6.1](#s15-6-1)）。スロットの同一性は宣言に結び付く。

- **等価性**は、正規化した構造・量化・条件・束縛・保証を比較する。両辺をそれぞれ自身の前提で正規化する。Originや集合の綴りだけでは、オーバーロードも特殊化も区別しない。スロット名はAPIの一部である。
- **互換性**は、要件が許す全呼出しを受け入れ、少なくとも要件と同じ結果保証を与える。要件の全称Originは固定して扱い、解いてよいのは実装の実体化可能な呼出しOriginだけ。入力は`required <: implementation`、結果は`implementation <: required`として検査する（[§3.8](#s3-8)）。実装の条件は要件の前提から証明する。レシーバ、環境、権限、Loanは、許可される全Semanticsケースで別に検査する。

Originは実行時の表現を持たず、実行時引数や寿命タグを追加しない。

<a id="s15-4"></a>
### 15.4. Originの補完と省略

契約は定義時に一度補完する。継承した完全な型を保ち、名前を解決し、注釈と関係を正規化して、残る省略位置を以下の規則で補完する。束縛集合の命名は省略を抑制しない。名前付き関数の契約を本体から推論することはない。

<a id="s15-4-1"></a>
#### 15.4.1. 明示的な結果の関係

通常の呼出し可能宣言の、集約型の結果スロットには次を適用する。

1. 確立済みの完全な束縛・継承した束縛と、注釈または既存Origin式との等価性で固定された位置を保つ。
2. 明示的な`origin`節を正規化した後、非自明な`outlives`関係に残る未束縛の結果スロットを、その関係の下で全称量化する。
3. 残るスロットに結果の省略規則（[§15.4.3](#s15-4-3)）を適用する。未解決の結果スロットだけをつなぐ等価関係は一つのクラスを形成し、同じクラスの全位置には同じ既定値を与えなければならない。既定値が異なるか、一つでも既定値のない位置があれば宣言はエラーとなり、結果の関係の明示が必要。

整形式性だけで、省略スロットが全称量化されることはない。`static outlives x`や`x == x`のような恒真の関係は、省略も量化も変えない。

<a id="s15-4-2"></a>
#### 15.4.2. 位置ごとの規則

別名、グループ化、冗長なowner接頭辞を正規化した後、安全な借用層や集約型スロットの省略Originを位置に応じて補完する。raw層のOriginは補完しない。`during`がなければOriginの指定はない（[§5.7](#s5-7)）。

| 位置 | 省略したOrigin |
| --- | --- |
| 直接借用のパラメーター・レシーバ | その入力の外側の層に独立したOriginを与える |
| 呼出し可能宣言の入力にある集約型 | 未解決スロットごとに独立した全称Originを与える |
| 関数の結果 | [§15.4.3](#s15-4-3)に従う。結果型全体の省略はUnit |
| 初期化子のある局所変数 | 推論する（[§15.4.4](#s15-4-4)） |
| 初期化子のない局所変数、フィールド、ペイロード | 明示情報から完全に決まらなければならない |
| static記憶領域 | `Owned`と[§11.8](#s11-8)が必要。共有層と`none`/`ref`スロットは`static`を既定値とし、`uniq`はエラー |
| アクセサー・特殊化 | 継承した契約を先に使う。ゲッターの結果はゲッターのレシーバを使う |
| Adaptationの対象、検査対象、キャスト対象 | オペランドから決める（[§13.5.1](#s13-5-1)、[§13.6.5](#s13-6-5)） |

入力の補完は型引数、タプル・配列要素、借用対象を再帰的にたどるが、フィールド・完全な型・呼出し可能宣言の境界は越えない。異なる入力とスロットは独立したまま保つ。入力内に入れ子になった借用層のOriginの省略はエラー。補完するのは直接入力の外側の層と集約型スロットだけである。コンストラクターは型の結果契約を保つ。元の`s/T`（[§8.1.1](#s8-1-1)）はその束縛を保ち、再構成した`s/U`は条件付きスロットを保つ（[§8.1.2](#s8-1-2)）。

<a id="s15-4-3"></a>
#### 15.4.3. 結果の既定値

残る省略結果Originは、一つずつ独立に補完する。

1. 借用レシーバも含めて直接借用の入力があれば、その外側の全Originの共通部分を使う。集約型内部のOriginは使わない。
2. それ以外で共有層なら`static`を既定値とする。排他層には明示的な契約が必要。
3. それ以外で集約型スロットなら、全入力型が`Owned`と証明でき（入力なしなら真）、Loan要件が`none`または`ref`の場合だけ`static`を既定値とする。それ以外には明示的な契約が必要。

条件付きの直接借用入力は、許可される全Semanticsケースで評価する。省略規則は`Owned`制約を追加しない。`func f<T>(x: ref/T? during a) -> ref/T`では、`x`は直接借用の入力ではないため、結果は`static`となる。

```kimi
func first<T>(x: ref/T) -> ref/T => x                 // during x
func choose<T>(x: ref/T, y: ref/T) -> ref/T => y      // during (x and y)
func view<T>(x: ref/T) -> View<T> => makeView(x)      // source: x
func invalid<T>(x: View<T>) -> View<T> => x@move      // Error: a result relation is required.
```

<a id="s15-4-4"></a>
#### 15.4.4. 局所変数と独立した型式

局所変数の宣言で型と推論変数を固定する。後続の使用はそれらを制約するが、推論をやり直さない。後続の代入は同じ契約に合わなければならない。初期化子がなければ、等価関係や確立した証明で全スロットを固定する。未解決の義務は最終性の規則で判定する（[§8.3](#s8-3)）。不確実だからといって`static`を作り出したり、依存を消去したりしない。付随する節は型注釈と共に収集し、その中の`view.source`は宣言された型のスロットを指す。本体内の型式は、その宣言に関係を付けて束縛する。初期化子に書いた型式の集合名は局所宣言に属し、中間のコンテナー修飾子には`(Outer<T>{o}).Inner<U>`と名前を付ける。関数の先頭の節は本体内の型の出現箇所を指定できない。コンテナー別名の節は、実行時の値や別ファイルの束縛を参照できない。

通常の記憶領域は、一律の`Owned`要件なしに、取得・Move・Copy・呼出し・破棄を通じて完全な型と実際のLoanを保持する。ヒープへの配置は参照先の寿命を延ばさない。

<a id="s15-5"></a>
### 15.5. 排他的なOrigin

排他借用が一意となる根拠は、Originではなく、競合規則（[§15.6.2](#s15-6-2)）と再借用の包含関係（[§15.6.3](#s15-6-3)）である。そのため、排他レシーバを通じて得た`uniq`結果は、そのレシーバのOrigin内に収まらなければならない。また安全なコードでは、Loan要件が`uniq`であるOriginを`static`へ束縛できない。違反はエラー。

シグネチャ`(self: uniq/Self) -> uniq/T during self.source`自体は有効だが、本体で`self`を通じた格納フィールドの再借用によって満たすことはできない。その再借用は`self`のOrigin内に収まるため、返そうとしたreturnの位置でエラーとなる。生の記憶領域の分割では、raw Placeへのunsafeな`uniq`借用でこのシグネチャを実装する（[§15.6.3](#s15-6-3)）。

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

<a id="s15-6"></a>
### 15.6. 借用検査

**プログラム点**は、操作の直前または直後の位置を指す。staticフィールドの記憶領域（[§22.4.4](#s22-4-4)）は、局所変数と同じくPlaceのルートである（[§15.6.2](#s15-6-2)）。非標準のアクセサーは呼出し境界となる。局所領域は推論し、シグネチャのOriginは全称量化された領域を表し、`static`は最大の領域となる。

**Loan。** **Loan**は、アクセス経路（[§15.6.2](#s15-6-2)）で指定するPlaceと、`ref`または`uniq`のモードを持つ。借用箇所とSemanticsケース（[§8.15](#s8-15)）で識別し、同じケースの同じ箇所での各実行は同じLoanを形成する。暗黙の借用（レシーバ式、引数のAdaptation、比較のための検査、キャプチャー項目）の箇所はその操作の箇所であり、呼出し側のLoan（[§15.6.4](#s15-6-4)）の箇所はその呼出しである。呼出しで評価する既定値（[§7.2.3](#s7-2-3)）の借用は、その呼出しと既定値内の操作で識別する。パラメーターのOriginを通じて本体が受け取るLoanは、その本体内にアクセス経路を持たない。その本体の呼出し箇所で、呼出しが到達し得るもの（[§8.11.4](#s8-11-4)）と、static記憶領域への効果（[§15.6.4](#s15-6-4)）に照らして検査する。Loanの**基点**は、それを作る借用の貸出し点である（[§3.4](#s3-4)）。

- Loanを保持する値は、その借用自体、そのCopy、その借用から導いた参照、およびそれらを保持する全ての集約値・クロージャー・記憶領域である。呼出し後は、結果と、公開契約が書込みを許す記憶領域が、それぞれ保持し得る引数のLoanを保持する（[§15.6.4](#s15-6-4)）。
- **アドレス。** `P@raw`で形成する生ポインターは、`P`の経路が借用も生ポインターも参照外ししない場合、`P`のルートにある保持元を**アドレスで指す**。それ以外では、`P`の借用が保持するもの（[§15.6.3](#s15-6-3)、生ポインターの先では[§5.2.2](#s5-2-2)）を保持するが、その借用が形成するLoanは保持しない。`self`を指定するrawフィールドから読んだ生ポインターは、そのフィールドの記憶領域への`@raw`と同じものに到達する（[§5.7](#s5-7)）。アドレスは、Copy、変位、生ポインター型間の変換、それらを保持する全ての値や保持元によって、Loanと同様に保持される。ただしLoanではなく、何とも競合せず、権限も与えない（[§5.2.1](#s5-2-1)）。
- **保持元**は局所変数、パラメーター、一時値である。保持元は、自身の全ての部分に格納された値のLoanを保持する。一部から読み取る・Copyする・Moveする値は、保持元が保持する全Loanを引き継ぐ。一部に格納された参照を通じた借用も、それらを親Loanとして引き継ぐ（[§15.6.3](#s15-6-3)）。Loanを観測し得る部分の破棄（[§15.6.6](#s15-6-6)）は、保持元全体を使用する。保持元全体への`=`、またはその記憶領域の開始・終了によって完全に置換された保持元は、旧値のLoanを保持しなくなる。一部への書込みや一部のMoveでは、そのLoanは一つも終わらない。書き込んだ値のLoanは保持元に追加する。本体が所有しない記憶領域（次項）も、Loanと同じく保持元に従う。次の場合、値はそのような記憶領域に到達する参照から導かれたものとなる。その参照のCopyや再借用、その参照から呼出しが保持し得る値（借用引数の読取りを含む。[§15.6.4](#s15-6-4)）、その参照を保持する保持元の一部から直接または保持元への参照を通じて読んだ値。
- 参照、Move Pathでない添字、動的コンテナーを通じた書込みは、到達し得る全保持元に書込み値のLoanを追加し、既存のLoanを終えない。呼出しも契約が書込みを許す記憶領域に同じように書き込むため、`replace`、`exchange`、`swap`（[§15.7](#s15-7)）は保持元を完全には置換しない。`replace`がLoanを終えるのは[§15.6.3](#s15-6-3)に定める場合だけ。参照は、自身が保持するLoanのうち、アクセス経路に参照外しのないもの（[§15.6.2](#s15-6-2)）が指定する保持元へ到達する。自身のLoanと、再借用時に通った参照のLoanを含み、アドレスで指す保持元にも到達する。また、パラメーターやキャプチャー、static記憶領域、契約上どの引数にも結び付かない呼出し結果に由来する場合、またはそのような参照から導かれる場合（前項）、**本体が所有しない記憶領域**にも到達する。`Raw.allocate`の結果など、これらの規則でどの保持元にも到達しない生ポインターは、そのような記憶領域だけへ到達する。そこに格納した値は、どの点の後でも使われ得る。参照を通じた読取りは、到達先のLoanを保持する。複合代入、インクリメント、デクリメントは予約を通じて代入先へ書き込むため（[§13.7.2](#s13-7-2)）、代入先が保持していたLoanを一つも終えない。
- 点Pの後にLoanを保持する値が使われ得る場合、そのLoanはPで**生存**する。破棄による観測も使用に含む（[§15.6.6](#s15-6-6)）。ある値の保持元と重なるPlaceへのLoan、またはその保持元のアドレスを保持する値が使われる位置でも、元の値は使用される。Pの操作は、次の値がPの前から保持するLoanに対して検査する。Pの後に使われる値のうちPが置換するものを除いた値、およびPの後に使われる保持元や本体が所有しない記憶領域へPが格納する値である。判断を決めるのはこれらの値であり、Loanの同一性ではない。借用は、自身が形成するLoanに対しては検査しない。その前にはどの値もそのLoanを保持しないためである。ただし、同じ箇所の以前の実行で形成され、そのような値が保持するLoanに対しては検査する。予約の有効化（[§15.6.7](#s15-6-7)）は、自身の引数を除き、その呼出しまで生存する値に対して検査する。
- 生存性は、起こり得る使用の解析である。合流後は、いずれかの流入経路で生存していればLoanは生存する。ループでは最小不動点を使う。
- Originの領域と`outlives`関係は寿命の互換性だけを検査し、Loanを生存させることはない。

```kimi
var a = 1
var b = 2
var r = a@ref
r = b@ref       // r no longer carries the Loan on a.
a = 3           // Valid.
inspect(r)
```

参照を通じて全体を書き換えても、参照先が以前から持つLoanは一つも終わらない。

```kimi
var a = 1
var b = 2
var slot: ref/i32 = a@ref
let r = slot@uniq
r@follow = b@ref    // A write through r: slot may now carry b's Loan, and a's Loan is not ended.
// a = 3            // Error: slot may still carry a's Loan and is used below.
inspect(slot)
```

<a id="s15-6-1"></a>
#### 15.6.1. Origin関係と整形式性

**生存性。** 型にOrigin `o`を含む値がPの後に使われ得るなら、Pは`region(o)`に属する。raw層のOriginは例外であり、領域を制約しない（[§5.7](#s5-7)）。

**整形式性。** 安全な各借用層`B/V during o`では、OwnedOrigins(V)（[§15.2.3](#s15-2-3)）内の全Originが`o`より長く生存しなければならない。`B`は`ref`、`uniq`、`objref`、`objuniq`、または許容された借用ケースのペアバインダーを指す。オブジェクト借用の`V`は、型引数・Origin引数を伴うView Targetである。この規則は許可されるSemanticsケースごとに判定する。ペア層の外側のOriginがOwnedOriginsに入るのは、そのバインダーが外側のOriginを持つケースだけ（[§15.6.5](#s15-6-5)）。明示・推論を問わず全ての型は整形式でなければならない。定義は、自身のシグネチャの型（パラメーター、レシーバ、結果、`Self`）が整形式であると仮定してよい。各呼出しは、実体化した型についてそれを証明する（[§15.6.4](#s15-6-4)）。

**Origin関係。** 値が代入先の型に合うかの判定（[§10.2](#s10-2)）には構造の部分があり、Originを等しいものとして扱った、正規化後の型の同一性で判定する（[§3.8](#s3-8)）。この部分の失敗だけが型の不一致であり、その場合はOrigin関係を形成しない。構造が合えば、各Origin位置は変性に応じた関係を生む。共変・反変位置では`outlives`、不変位置では`==`である。宣言された関係、呼出しで代入された関係、整形式性もOrigin関係であり、[§11.8](#s11-8)のstatic記憶領域の規則は`static`に合うかの判定である。適用可能性（[§10.1](#s10-1)）と期待結果による絞込み（[§10.3](#s10-3)）は構造の部分だけを使う。Origin関係は選択後に判定し（[§15.6.5](#s15-6-5)）、選択を変えない。等価関係により呼出しの新たなOriginが複数の固定Originと等しくなる場合、それらの固定Originも等しくなければならない。

証明にOriginを使う制約（`Owned`、Callable、型の同一性。[§8.3](#s8-3)）は別の要件であり、適用可能性の判定に参加する。契約全体の比較（[§8.6](#s8-6)、[§8.13](#s8-13)、[§10.7](#s10-7)）のOrigin部分は、[§15.3.7](#s15-3-7)に従い前提だけから判定するため、ProvenかUnknownとなる。UnknownはProof分類のエラーであり、Callable制約の内側では制約の失敗となる。

**エラー。** RefutedなOrigin関係はエラー。最終性の規則（[§8.3](#s8-3)）でもUnknownならProof分類のエラーであり、偽ではなく未証明を意味する。失敗した関係は領域推論に制約を加えないため、それ自体を原因としてLoan競合や結果の不一致を生じさせない。本体が確立すべき節と整形式性は義務であり、前提ではない。

```kimi
// Holder<T> is a struct with the Field value: T.
func get(b: objref/Holder<ref/i32 during c> during o) -> ref/i32 during o
    return b.value   // Valid: b's Type is well-formed, so c outlives o.
```

<a id="s15-6-2"></a>
#### 15.6.2. Placeの重なりと競合

Placeの重なりは[§3.4](#s3-4)に従う。Loan検査は、操作とLoanのアクセス経路（[§3.4.1](#s3-4-1)）を段階ごとに比較する。経路はルート（局所変数・パラメーター・一時値、staticフィールドのキー、raw Place借用の新たな基点）から始まり、インライン部分（フィールド・基底部分オブジェクト・タプル要素・添字・ケースのペイロード）、**所有の段階**、**参照外し**をたどる。参照外しは`ref`、`uniq`、`objref`、`objuniq`の参照を通る段階で、`ref`または`objref`を通るものは**共有**である。例では`.*`と書く。`r.*.x`は`r`の参照先のフィールド`x`を表す。`obj`、`rc`、`arc`ハンドルのペイロードに入る段階は所有の段階であり、参照外しではない。ペイロードの経路は、[§3.4.1](#s3-4-1)が許すアクセスの種類によらず、ハンドルの経路を延長する。参照外しは参照外しとだけ、所有の段階は所有の段階とだけ一致する。以下の規則で互いに素と証明できない限り、二つの経路は重なる。

| 経路 | 結果 |
| --- | --- |
| 同一の経路、またはある経路とその延長 | 重なる。到達の規則は後述 |
| 参照外しを含め、異なるルートからの経路 | 互いに素。各参照が保持するLoanが別名を覆う（[§15.6](#s15-6)） |
| 同じ集約値の異なるインラインフィールド・タプル要素・定数添字、およびその延長 | 互いに素 |
| 同じenumの異なるケースのペイロード、およびその延長 | バイトを共有し得るが、互いに素。ケースを変えるのはenumへの書込みだけであり、その書込みはペイロードの全Loanに到達する |
| raw Place借用の新たな基点（[§5.2.2](#s5-2-2)）と、そこから導かれていないPlace | 比較しない。unsafeの義務となる（[§5.2.1](#s5-2-1)） |
| 異なる定数添字以外の添字など、上記以外 | 未証明。証明が必要な操作はエラー |

- 一つのstaticフィールドの全キーは、外側の型引数によらず同じルートとなる。異なるフィールド宣言のキーは異なるルートとなる（[§22.4.4](#s22-4-4)）。
- Loanの経路が操作の経路の接頭辞なら、その操作はLoanに到達する。そのため、参照を通じたアクセスは、その参照を保持するPlace上のLoanに到達する。Loanの経路が操作の経路の延長である場合、読取り・書込み・破棄がLoanに到達するのは、両者の間に参照外しがない場合だけ。Moveや借用は参照外しを越えて到達するが、その操作がその場で破棄する対象の借用（[§15.7](#s15-7)の`replace`）は例外。Loanを観測し得る破棄（[§15.6.6](#s15-6-6)）も参照外しを越えて到達する。いずれも、最初の共有参照外しまでを対象とする。
- `i != j`のような実行時比較や整数の証明は、何も証明しない。配列から導いたSliceは配列全体を対象範囲として保つ（[§4.8.1](#s4-8-1)）。標準以外のアクセサー呼出しはレシーバ全体を使う。

各操作は、その点で検査対象となるLoan（[§15.6](#s15-6)）のうち、到達する全てのLoanに対して検査する。

| 操作 | 生存する`ref` | 生存する`uniq` |
| --- | --- | --- |
| 読取り、`ref`の作成 | 許可 | エラー |
| 書込み、Move、`uniq`の作成、Placeの破棄 | エラー | エラー |

**共有アクセスは変更を許さない。** Placeへの書込み・Move・排他借用・破棄は、直接経路または排他参照（[§3.4](#s3-4)）を通じるか、権限内のrawアクセス（[§5.2.1](#s5-2-1)）によってだけ行える。共有層（`ref`、`objref`、`rc`、`arc`）の先では読取りと共有借用だけが可能であり、そこで読み取った生ポインターを通じても同じ。また、Originが共有Loanだけを保持する生ポインターの先も同じである（[§5.2.1](#s5-2-1)）。参照カウントとWeakテーブルはPlaceではない。

<a id="s15-6-3"></a>
#### 15.6.3. 再借用と領域の分割

排他借用を通じて借用すると、参照先を基点とする子Loanを作る。その値は、そこへ到達するために参照外しした全参照のLoanも、親Loanとして保持する。共有参照外し（[§15.6.2](#s15-6-2)）の先の借用は、自身のLoanを形成しない。最後の共有参照外しの参照と、その後に参照外しする参照だけから導かれ、それらのLoanを保持する。子が生存する間は親を通じたアクセスを停止し、親を保持するPlaceなど、子のアクセス経路のどの接頭辞もMoveや借用ができない。ただし、子が共有の場合の共有借用と、その操作がその場で破棄する対象としての借用（[§15.7](#s15-7)の`replace`）は例外とする。

Move Path（[§15.1.3](#s15-1-3)）をその場で破棄すると、その位置の旧値の破棄を検査した後（[§15.6.6](#s15-6-6)）、そのMove Pathが正確な接頭辞となるアクセス経路を持つ全Loanが終わる。`=`、記憶領域の終了、`replace`の対象が該当する。Move Pathは段階ごとに比較するため、定数添字は同じ定数にだけ一致し、実行時添字には一致しない。その先のアクセス経路は、参照外しを含む任意の段階を続けてよい。終了したLoanを保持する値はなくなるが、終了した子を保持していた値は親Loanを保つ。そのためパラメーターの再借用は返せる。また、外側の参照の置換はその旧値を通じて形成した子を終えるが、親Loanは旧参照先を守り続ける。それ以外の子は、それを保持する値が使われ得なくなると終わる。子は、親のOriginと、親に到達するために通る各安全な借用のOrigin内に収まる。レシーバ（[§15.5](#s15-5)）やクロージャーの呼出しレシーバ（[§15.8.2](#s15-8-2)）などを含み、最初の共有層までを、その層も含めて対象とする。したがって、共有参照を通じた共有借用は、その参照のOrigin内だけに制限される。

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

生存する子のアクセス経路の接頭辞をMove・交換することや、子が共有の場合の共有借用以外で借用することはエラー。`replace`は`=`と同じく子を終える。

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

二段階の参照外しを通じて形成した子も、外側の参照の置換によって同じように終わり、親Loanは旧経路を守る。子と共に旧経路を保持する借用は、子が生存する間は拒否する。

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

**領域の分割**は、同じ方法で一つの親から複数の子Loanを導く。排他的な子同士、および各子と親の残りの部分は、[§15.6.2](#s15-6-2)か、raw Placeの`uniq`借用のunsafeの約束によって、互いに重ならないことが必要。共有の子は重なってよい。子を通じた再借用はその子の範囲内に収まる。呼出しがレシーバの型のOriginだけを持つ値を返し、他の入力型がそのOriginを指定しない場合、その値は、分割元のIteratorや残りの部分など、その呼出しのレシーバに由来する。`next`がこれに該当する。この値は、それらのOriginのどれを指定していても、レシーバを通じた読取りが保持する全Loanを保持する（[§15.6.4](#s15-6-4)）。呼出し先の具体的な実装が不明な要件呼出し（[§8.11.4](#s8-11-4)）や、Callable制約のある値を通じた呼出し（[§8.11.6](#s8-11-6)）では、その値はさらに、呼出しのレシーバLoan（[§8.11.3](#s8-11-3)）を除くレシーバ引数の全Loanも保持する。再借用されたレシーバの親Loanなどが該当する。Iteratorや残りの部分を破棄しても、公開済みの子Loanは終わらない。ライブラリーは通常のコードで生の記憶領域を分割する。残りの部分は`Loan<uniq/… during source>`フィールドで親Loanを保持し、rawフィールド`during source`で記憶領域を指す（[§5.7](#s5-7)）。各子はraw Placeの`uniq`借用であり、`during state.source`として返す。生存する二つの排他的な子に重なるPlaceを貸し出さないことがunsafeの義務となる。

```kimi
// Take is the contract of §8.11.1.
func takeFirst<S>(h: uniq/(uniq/S during s, i32)) -> Option<S.Item>
    S is Take
    var r = h@follow@uniq
    let first = r.0.take()  // Descends from the receiver r.0 and keeps its parent Loans, r's Loan on h.* among them.
    // h.1 = 5              // Error: reaches r's Loan on h.* while first is live.
    return first@move       // Valid: the end of h's storage ends r's Loan.
```

結果の型が、その呼出しのレシーバLoanに依存せず、呼出し先の効果（`preserves results`ではその効果境界。[§8.11.3](#s8-11-3)）が結果と競合しなければ、同じレシーバへの後続の呼出しを越えて結果を保持できる。

<a id="s15-6-4"></a>
#### 15.6.4. 呼出し

呼出しは次の手順で検査する。

1. 呼出し先の抽象Originに新しい領域を作る。
2. パラメーター型を実体化し、引数がそれに合うかを調べる。抽象的な元の`s/T`に束縛されたペアスロットはOriginを保ち、`s/U`の条件付きスロットは引数の外側のOriginスロットを束縛する（[§8.1.2](#s8-1-2)）。
3. 結果型を実体化し、代入後の宣言された関係と、実体化した全パラメーター型・結果型の整形式性を証明する。新しい領域は[§15.3.6](#s15-3-6)で解く。
4. 依存を集める。クロージャーの呼出しレシーバ（[§7.7.3](#s7-7-3)）も含め、借用入力のOriginを指定する結果は、その入力の参照先の型のOwnedOrigins内の全Originにも依存する。
5. 呼出し側のLoanを作る。それを保持する値が使われる間は、そのLoanが生存する。

`uniq`要件は排他Loanを作り、結果が依存する間、そのLoanは排他的なままとなる。Originの等価性はLoanを作ることも解放することもない。保護は、評価順に従って借用を形成した時点から始まり（条件を満たす排他借用は予約として始まる。[§15.6.7](#s15-6-7)）、呼出し先の後処理も含む呼出し全体を通じて続く。どの結果・書込み可能な記憶領域が引数を保持し得るかは、直接・間接の呼出しとも実体化した公開契約で決まる。クロージャーのキャプチャー経由で到達する記憶領域も含む。保持しないことが未証明なら、アクセスを許可しない。保持はOriginごとではなく引数ごとに判定する。クロージャーの呼出しレシーバも含む借用引数の参照先の型の内部のOriginを保持し得る結果や記憶領域は、そのうちどのOriginを指定していても、その引数を通じた読取りが保持する全Loanを保持する（[§15.6](#s15-6)）。引数のそれ以外のOriginを保持し得る場合は、引数のLoanを保持する。引数が借用なら、その読取りのLoanも保持する。[§15.6.3](#s15-6-3)で定める呼出しのレシーバに由来する結果は、同節で追加するLoanも保持する。raw層の保持は[§5.7](#s5-7)に従う。Originがあれば、そのOriginが属する借用と同じものをLoanなしで保持する。Originがなければ、パラメーターのraw層は指示先の型が許すものを許し、結果型内で公称型のフィールドの外にあるraw層は、全引数を保持し得て、本体が所有しない記憶領域に到達する。呼出しは、共有層の先で到達したraw層を通じて書き込まない（[§5.2.1](#s5-2-1)）。

**効果の要約。** 呼出し可能宣言の**効果の要約**は、読取り・借用・書込み・置換・破棄・初回アクセス時の初期化によってアクセスし得るstaticフィールド（[§22.4.4](#s22-4-4)）を列挙する。呼出し先、閉じた明示的特殊化集合内の全実装（[§12.7.2](#s12-7-2)）、既定値、アクセサー、遅延初期化、後処理の効果も含む。宣言ごとに自身の型パラメーターを使い、再帰呼出しに対する最小不動点として一度計算する。**パラメーター依存の効果**は、型パラメーターや関連型射影を使って保持する効果である。その型がCopyと証明されていない場合の破棄（[§16.3.3](#s16-3-3)）、その型に公開された操作のユーザーコードの効果（[§4.9.6](#s4-9-6)）、Callable入力を通じた呼出しが該当する。射影の基底（直接の基底、その基底、以下同様）のいずれかが再帰的Contract（[§8.3.3](#s8-3-3)）に適合すると、要約する宣言の前提から証明できる場合、その射影上の効果を保持する代わりに、未知の環境効果とする。他のパラメーター依存の効果は射影の深さが有界であるため、不動点は有限となる。Callable入力を通じた呼出しは入力とレシーバモードごとに一度記録し、各使用で実引数と合成する。適合の効果境界は、完成した要約に照らして検査する（[§8.11.4](#s8-11-4)）。

- 生存するLoanと競合する呼出しや直接staticアクセスはエラー（[§15.6.2](#s15-6-2)）。フィールドに基点を持つ生存Loanが証明するのは、そのフィールドの初期化だけである。
- 未知の環境効果を持つ呼出しや、型消去された破棄（[§16.3.3](#s16-3-3)）は、フィールドの可視性によらず、可変なstatic記憶領域に基点を持つ全ての生存Loanと競合する。共有・排他の両方が対象。不変なstatic記憶領域に基点を持つLoanには影響しない。タスク呼出しは常に未知の環境効果を持つ（[§24.3](#s24-3)）。
- 要件呼出しとCallable制約のある呼出しの効果は、効果境界から得る（[§8.11.4](#s8-11-4)、[§8.11.6](#s8-11-6)）。

**可変staticのLoan。** 可変なstatic記憶領域の借用は本体内に制限される（[§15.6.5](#s15-6-5)）。そのためLoanを保持する値は本体の外へ出ず、各呼出しは公開契約だけから結果の依存を得る。

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

広げられた効果は、未知の呼出しと同じように競合する。呼出し側の前提なら置き換えられた全効果を分類できる場合も同じである。

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

<a id="s15-6-5"></a>
#### 15.6.5. Origin関係の判定

本体は、全称量化されたシグネチャの全ての呼出し元に対して動作しなければならない。Origin関係はProven、Refuted、Unknownのいずれかと判定する（[§8.3](#s8-3)）。Originは次の3種類に分かれる。

- **固定Origin**は本体の契約で固定されるもの。全称Origin、パラメーターやレシーバのOrigin、その射影、`static`が該当する。クロージャー本体では、キャプチャー、呼出しレシーバ、本体から推論した結果全体（[§15.8.2](#s15-8-2)）のOriginも固定される。固定Originは本体内の全ての点を含む。
- **有限Origin**は上限付きの借用に属する。そのような借用ごとに固有のOriginを持つ。
  - **本体局所のOrigin**は、本体の局所変数・パラメーター・一時値のルートに基点を持ち、フィールド・要素・所有層の射影で到達する借用に属する。借用や生ポインターは通らない。上限は本体である。暗黙の借用とキャプチャー項目の借用（[§7.7.2](#s7-7-2)）も含む。クロージャー環境の束縛は呼出しレシーバのフィールドであるため、借用レシーバを通じたそれらの借用は本体局所ではない。Consuming呼出しでは本体局所となる。
  - インライン部分も含む可変なstatic記憶領域の借用も、本体を上限とする。それを返す場合や、パラメーター・キャプチャーを通じて格納する場合など、固定Originへ運ぶ関係はRefutedとなる（[§15.6.4](#s15-6-4)）。
- **推論される領域**は、それ以外の局所Origin。型注釈のない局所変数の型スロットなどが該当する。raw Placeの借用（[§5.2.2](#s5-2-2)）は自身の上限を持たないため（Originを指定したポインターを通じる場合は、そのOrigin内に収まる。[§5.7](#s5-7)）、そのOriginは有限ではなく、推論される領域である。借用を通じて到達する借用のOriginも同じ。自身の上限を持たず、到達時に通る借用への包含は[§15.6.3](#s15-6-3)に従う。

本体が検査する全関係を判定する。到達不能な結果源（[§14.9](#s14-9)）と、[§14.10.3](#s14-10-3)の仮定した経路の状態も含む。判定には生存性も実行時の到達可能性も使わない。各関係`a outlives b`は`a`から`b`への辺であり、`a == b`は双方向の辺となる。**連鎖**は、両端が固定Originまたは有限Origin、内部の節点が推論される領域または有限Originである経路であり、始端が終端より長く生存することを要求する。連鎖は経路を列挙せず、グラフの到達可能性で判定する。

- **Refuted：** 有限Originが、その上限の外にある固定Originより長く生存することを要求する連鎖。Origin関係を否定する規則はこれだけである。
- **Proven：** ソルバー（[§15.3.6](#s15-3-6)）が前提から導ける固定Origin間の連鎖、または、一方の端が有限Originか推論される領域で、Refutedではない連鎖。後者は領域推論を制約する。
- **Unknown：** ソルバーが導けない固定Origin間の連鎖。

ペアバインダーでは、各Semanticsケースの型の整形式性を前提とする（[§8.15](#s8-15)）。ペア層の外側のOriginスロットは、外側のOriginを持つケースだけに存在する（[§8.1.1](#s8-1-1)）。関係を必要とする全ケースで成り立つ前提からのみ、その関係をProvenと判定する。局所Placeの寿命を超える使用は、その破棄またはMoveでのLoan競合（[§15.6.6](#s15-6-6)）であり、Origin関係の失敗ではない。局所変数や一時値の借用を返す場合は、返す値の位置でRefutedの関係となる。

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

<a id="s15-6-6"></a>
#### 15.6.6. 破棄時の寿命検査

破棄責任と順序は[§16.2](#s16-2)と[§16.3](#s16-3)に従う。破棄が観測し得る全OriginとLoanは、その位置で有効でなければならない。ユーザー定義の`drop`は、本体が何も使わなくても到達可能な全Originを観測すると仮定する。この検査をフィールドの破棄にも再帰的に適用する。Originを観測しない破棄は要件を追加しない。

Loanを観測し得る破棄は、破棄するPlaceと、そこから到達するPlaceの排他的な使用である。`obj`、`rc`、`arc`のペイロードと排他参照外しを越え、最初の共有参照外しまでを対象とする（[§15.6.2](#s15-6-2)）。`=`、記憶領域の終了、`replace`などの対象を破棄する呼出しで適用する。Loanを一つでも終える前に検査する（[§15.6.3](#s15-6-3)）。何も観測しない破棄は、破棄するPlaceだけにアクセスする。

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

<a id="s15-6-7"></a>
#### 15.6.7. 呼出しの予約

**呼出しの予約**は排他アクセスを遅らせるものであり、評価や寿命の保護は遅らせない。呼出しのレシーバまたは引数を生成する排他借用・再借用は、生存するLoanの代わりに予約を開始する。明示形式`@uniq`、`@uniq/T`、`@follow@uniq`、`@objuniq`に加え、暗黙形式も対象となる。レシーバ式の排他的な取得（[§7.3](#s7-3)）、`uniq`または`objuniq`パラメーターに渡す借用値の排他的な再借用（[§10.2](#s10-2)）、許可された排他的なペイロードのfollow、解決後の効果が排他的であるジェネリックのAdaptationが該当する。ルートから、ユーザーコードを呼ばない標準のフィールド・要素射影を経てレシーバや引数へ至る経路は、一つの準備として貸出し点（[§3.4](#s3-4)）で終わる。明示的な排他借用を含んでも、呼出し前にはその経路上の排他Loanは生存しない。したがって、`holder@uniq.items.append(x)`、`holder.items@uniq.append(x)`、`holder.items.append(x)`は同じ呼出しとなる。

- 丸括弧はこの扱いを変えない。経路上のゲッターや別の呼出しは、自身の呼出しで有効化する。全ての種類の呼出しにこの規則を適用する。オーバーロードを先に選択し、予約の有効性によって選択を変えない。
- 代入先は呼出しの予約を開始しない。複合代入・インクリメント・デクリメントは、[§13.7.2](#s13-7-2)に従って代入先を保護する。
- 予約は、局所・集約値の記憶領域、キャプチャー、入れ子の呼出し、制御フロー式の結果、複合代入、インクリメント、デクリメント、添字式を通過しない。格納した排他参照の裸の取得も含め、記憶領域に保存する再借用は形成時から生存する。

**予約と有効化。** 対象は一度だけ評価・位置特定・検査し、そのPlace、所有者、到達時に通った参照を保護する。共有の読取りや借用は共存できるが、対象への書込み・Move・破棄・再割当て・その他の排他操作はエラーとなる。子の予約は親の権限を保つ。まずレシーバの位置を特定し、明示引数を記述順、既定値をパラメーター順に評価する（[§7.2](#s7-2)）。既定値は、呼出し先への進入前に終わる共有Loanを通じ、予約中の入力を調べてよい。その後、保持される依存・キャプチャー・static記憶領域への効果・後処理の効果を含めて、全予約を同時に生存状態にできなければならない。重なる引数が両方とも排他的、または一方が共有でもう一方が排他的なら、呼出し先が使わなくてもエラーとなる。引数間の重なりは、それぞれの値が保持するLoanを通じて判定し、各有効化をそのLoanに対して検査する（[§15.6](#s15-6)）。共有参照外しの先の借用は自身のLoanを作らないため（[§15.6.3](#s15-6-3)）、`r: ref/i32`である`g(s@uniq, s.r@follow@ref)`は有効である。有効化を可能にするために一時値を早く破棄することはない。制御転送では、オペランドの評価・取得中も予約の保護を保つ。その後、放棄する呼出しの予約だけを、有効化せず、放棄した準備の残りの後処理より前に終える。引数内で受け止められる転送は何も放棄しない。

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

<a id="s15-7"></a>
### 15.7. 値全体の更新

以下の`Kimi.Intrinsics`宣言（[§22.1](#s22-1)）はコンパイラー組込みであり、`Sealed`、`Copy`、`Owned`制約を課さない。

```kimi
group Intrinsics    // Kimi.Intrinsics
    public func replace<T>(target: uniq/T ! with => value: T) -> ()
    public func exchange<T>(target: uniq/T ! with => value: T) -> T
    public func swap<T>(first: uniq/T, second: uniq/T) -> ()
```

| 操作 | 旧内容 | 新内容 | 結果 |
| --- | --- | --- | --- |
| `replace` | その場で破棄 | 破棄完了後に配置 | Unit |
| `exchange` | 転送 | 取得した値 | 旧値 |
| `swap` | 両方を転送 | 交換 | Unit |

- 各対象は完全にInitializedで、排他的に書込み可能であり、値全体の更新が許可されていなければならない。`T`は対象から固定する（`uniq/T`は`T`について不変）。`value`は通常の引数として`T`に合うようにする（[§10.2](#s10-2)）。
- 各対象は`T`全体の記憶領域である。通常のowner記憶領域はopenなCoreを含んでよいが、オブジェクトのペイロード記憶領域には[§13.5.5](#s13-5-5)の完全な対象の証明が必要。基底部分オブジェクトやopenなオブジェクトビューは条件を満たさない。
- 対象は引数である。所有Placeは`@uniq`で貸し出し、借用値は明示的な記法なしで再借用する（[§15.1.5](#s15-1-5)）。オブジェクトのペイロードは`handle@follow@uniq`で選ぶ。`T`が借用型の場合、移動するのは参照であり参照先ではない。
- これらの操作でUninitializedや部分的にMovedの記憶領域を修復することや、借用を通じて値をMoveして取り出すことはできない。`let`フィールドを持つ値全体の置換は、そのフィールドへの書込みを許可しない。

<a id="s15-7-1"></a>
#### 15.7.1. 評価と転送

引数の評価・予約・有効化は[§15.6.7](#s15-6-7)に従う。取得と期待型に合うことの確認は更新前に終える。引数の評価が完了しなければ、対象は変わらない。`replace`は通常の`drop`・フィールド・基底の順で旧値をその場で破棄する。その途中のAbortや発散は配置を妨げる。配置ではコンストラクター・初期化子・セッターを実行しない。`exchange`と`swap`はユーザーコード・破棄・Abortする操作を実行しない。

```kimi
var p: i32 = 0
Kimi.Intrinsics.replace(p@uniq, with: p + 1)
let old = Kimi.Intrinsics.exchange(p@uniq, with: 5)
var q: i32 = 9
Kimi.Intrinsics.swap(p@uniq, q@uniq)
// Kimi.Intrinsics.swap(p@uniq, p@uniq)            // Error: overlapping targets.
// Kimi.Intrinsics.exchange(x@uniq, with: x@move)  // Error: transfers reserved storage; write x = x@move.
```

<a id="s15-7-2"></a>
#### 15.7.2. 静的な非重複

`swap`は[§15.6.2](#s15-6-2)に従い、対象が互いに素と証明されることを要求する。生存する排他入力`a: uniq/T, b: uniq/T`は条件を満たすが、実行時比較は根拠にならない。

<a id="s15-7-3"></a>
#### 15.7.3. 記憶領域の更新と依存

以下の規則は、Placeの値全体を更新する全操作に適用する。`=`、複合代入、インクリメント、デクリメント、`replace`、`exchange`、`swap`が該当する。記憶領域の寿命とその内容の寿命は異なり（[§5.2.1](#s5-2-1)）、生ポインターをLoanの根拠の代わりにはできない。

- 入ってくる値、返す値、生き残る値が必要とする全LoanとOrigin、および操作の排他Loanと親の連鎖を保たなければならない。
- 旧内容に基点を持つ、生存中の借用を無効にする更新はエラー。同じアドレスに新しい値を置いても、その借用は復元されない。独立した外部依存は残る。格納された`ref/Data`のCopyは外部Loanだけを保ち、フィールドの借用はそのフィールドに依存する。
- オブジェクトのペイロードの更新は、オブジェクトの同一性、メモリー割当て、動的型、所有モードを保つ。最後の解放ではない。空のペイロードや破棄中のペイロードを外部に見せない。交換で取り出した旧値には、その値自身の破棄義務が伴う。
- 旧内容に関する事実は無効になるが、オブジェクトの同一性と動的型の絞り込みは保つ（[§14.10.1](#s14-10-1)）。ハンドルの置換は[§16.3.3](#s16-3-3)に従う。

```kimi
var data = makeData()
var other = makeData()
var slot: ref/Data = data@ref
let copy = slot                 // Depends on data only.
let inner = slot@ref            // Borrows the slot itself.
slot = other@ref                // Error: inner is used below.
inspect(inner)
```

<a id="s15-8"></a>
### 15.8. キャプチャーされた依存と型消去された依存

<a id="s15-8-1"></a>
#### 15.8.1. オブジェクトのペイロードの型消去

基底ビューや実行時Contractビューの背後へ具体的なペイロードを型消去するには、その完全なデータ型が`Owned`でなければならない（[§15.2.3](#s15-2-3)）。ハンドル自身のOriginはこの検査に含めないため、局所的なオブジェクト借用は局所Originを保つ。オブジェクトの生成には[§8.7.3](#s8-7-3)を満たす必要がある。型消去されたビューはその証明を保証し、後続のアップキャストやキャスト、オーバーライド本体内の字句的な`self`も引き継ぐ（[§6.4.5](#s6-4-5)）。実行時検査やキャストの対象の補完（[§13.6.5](#s13-6-5)）で`static`として与えてよいのは、証明が覆う固定束縛だけである。`obj`と`objuniq`はView Targetについて不変で、他のハンドルは変更を許さないため、証明はオブジェクトの寿命中有効である。

<a id="s15-8-2"></a>
#### 15.8.2. クロージャーの依存

クロージャーは、生成したブロックの寿命だけでなく、キャプチャーした各値のOriginとLoanの依存を保つ。借用内容のない所有値をMoveして取り込んでも、何も借用しない。外側のスロットを借用する項目（[§7.7.2](#s7-7-2)）は、そのスロット上のLoanを保つ。

| 結果源 | 契約 |
| --- | --- |
| 環境が所有するデータの借用 | 呼出しレシーバの借用に依存する |
| キャプチャーした共有参照、またはそれを通じた共有借用 | 外部のOriginを保つ |
| キャプチャーした排他参照の再借用 | 外部のOriginとLoanを保ち、Shared・Exclusive呼出しでは呼出しレシーバにも依存する（[§15.6.3](#s15-6-3)） |
| Consuming呼出しでMoveして取り出す参照 | Originと能力を転送する |
| 借用引数 | パラメーターのOrigin契約に従う |

結果型を省略した場合、推論した結果の各Originは、内部呼出し契約の呼出しごとのOriginとなる。表に従い、返す値の全ての源がそのOriginより長く生存する。複数の戻り値は、共変位置では各源の共通部分を与え、不変位置ではOriginの等価性を要求する。呼出し局所の記憶領域や、その呼出しが消費する環境の借用など、他の源はエラー。本体から推論した結果全体のOriginは本体の外にある固定Originであり、各結果源をそれに照らして判定する。そのため、本体局所の借用を返すことは、記憶領域の破棄時のLoan競合ではなく、返す値の位置でのエラーとなる。

注釈のある結果や、固定された期待呼出しシグネチャ（[§10.5](#s10-5)）には通常の結果省略規則を使う。隠れたレシーバを指定するOrigin式はなく、注釈内の名前はパラメーターと外側のスコープで解決し、環境の束縛には解決しない。共通の関数型とCallable制約は、隠れたレシーバに依存する結果を返さない（[§8.10](#s8-10)）。

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

<a id="s15-8-3"></a>
#### 15.8.3. 逃避と保持

返す値、格納フィールド、他のクロージャー、間接呼出し先は、渡し先の寿命と能力の契約に照らして検査する。借用キャプチャーだからといって必ず逃避しないわけではない。Moveキャプチャーも入れ子の借用依存を保ち、クロージャーをMoveしたりヒープに置いたりしても参照先の寿命は延びない。`ref/F`もCallableも、環境から導いた値を保存しないとは約束しない。`Owned`はスレッド間転送を許可しない（[§D.5](#sd-5)）。

---

<a id="s16"></a>
## 16. スコープ退出と破棄



<a id="s16-1"></a>
### 16.1. 遅延ブロック

**遅延ブロック**（`defer`）は、実行がそこに達したときに後処理を登録する。登録時には本体の何も評価しない。本体には共通の本体形式を使い（[§14.2](#s14-2)）、結果は持たない。遅延ブロックは一時停止しない（[§24.2.2](#s24-2-2)）。

登録は、それを直接含む実行可能な本体スコープに属する。該当するのは、関数、分岐、アーム、現在の反復、`do`、`unsafe`、`require`の失敗本体、実行中の`defer`本体である。そのスコープのスコープ退出時に実行する（[§16.2](#s16-2)）。到達しなかった登録は実行しない。各反復は独立に登録する。登録を取り消したり、手動で呼び出したりすることはできない。

```kimi
func process(flag: bool)
    defer => log("function end")
    if flag
        defer => log("branch end")
        work()
    log("after branch")
// For flag = true: "branch end", "after branch", "function end".
```

<a id="s16-1-1"></a>
#### 16.1.1. 遅延ブロックの制御境界

外向きの制御転送は、遅延ブロックを越えてはならない（[§14.5.2](#s14-5-2)）。内部では次の規則に従う。

- 遅延ブロック自身を対象とし、オペランドがないかUnitに合うオペランドを持つ`exit`は、本体を終了する。入れ子の選択、`do`、`unsafe`、`require`の失敗本体の中からでも同様である。
- 本体内の対象への制御転送は、その意味を保つ。内側のループは自身への`exit`と`continue`を受け取り、内側の選択と`do`式は自身の結果を保つ。
- それ以外の本体外への制御転送は、到達不能なコードでもエラーとなる（[§14.5.2](#s14-5-2)）。入れ子の関数は自身の関数境界を保つ。

早期の`exit`は、その本体自身の後処理を完了してから、保留中の外側のスコープ退出を再開する。登録を取り消したり、保留中の結果や制御転送を置き換えたりはしない。

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

<a id="s16-1-2"></a>
#### 16.1.2. 遅延評価と所有権

名前と実効型は登録位置で解決する。登録ではローカルのCopy、Move、借用を行わず、クロージャーも作らない。本体は実行時点の束縛を使い、すべての退出経路について、その時点の所有権とLoanの規則を検査する。後処理中にMoveすると、その取得元に対する後の自動破棄はなくなる。

内側の`defer`は、外側の遅延ブロックの実行中に登録され、その本体の退出時、残る外側の後処理より前に実行する。すでに退出中のスコープに対して登録することはできない。

遅延ブロックはunsafeの許可を与えない。許可は操作の字句上の文脈に従い（[§14.3](#s14-3)）、本体の実行時に安全条件を満たさなければならない。`defer`は二重破棄を防がず、生ポインターの有効期間も延ばさない。

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

<a id="s16-2"></a>
### 16.2. スコープ退出

**スコープ退出**は、登録された遅延ブロックと、そのスコープの自動破棄を実行する。スコープが完了したとき、および`return`、`exit`、`continue`、`yield`、`try`による伝播がスコープを離れるときに起きる。各構文が作る値を所有するスコープは、その構文の規則で定める。

<a id="s16-2-1"></a>
#### 16.2.1. 後処理の順序

離れるスコープは内側から外側へ後処理する。スコープ内では、宣言と`defer`文を字句順の逆に処理する。後で初期化や再代入を行っても、束縛の位置は変わらない。登録済みのブロックだけを実行し、Initializedで、破棄責任がスコープに残る値だけを破棄する。最後の使用によって責任が終わることはない。

スコープ入口で導入する束縛（パラメーター、`self`、反復の束縛、パターン束縛）は、記述順に本体の文より前に置く。各構文は、自身の暗黙の束縛の位置を定める。

```kimi
func process(first: Resource, second: Resource)
    let a = makeResource("a")
    defer => log("A")
    let b = makeResource("b")
    defer => log("B")
// Cleanup: "B", b, "A", a, second, first.
```

**中断された式。** 式の完了前に制御転送が式を離れる場合、生存する各一時値と、未完了のタプル・配列・enumケース構築の配置済みの各構成要素は、その取得時点で最も内側にある本体スコープに属する。そのスコープとともに取得順の逆に後処理するため、そのスコープの先行する宣言や遅延ブロックより前に破棄する。matchのガードの一時値とmatchのSubjectも含む。値の責任を移譲すると、その値はこの順序から外れる。

```kimi
func f() -> Result<i32, ParseError>
    defer => log("defer")
    consume(makeGuard(), try parse()) // On Err: the Guard temporary is destroyed, then "defer" runs.
    return .Ok(0)
```

<a id="s16-2-2"></a>
#### 16.2.2. 結果と制御転送

制御転送の結果、またはその所有者の結果として使う単一項目の式では、次の順に処理する。

1. オペランドを評価する。別の制御転送が起きた場合は、そちらを代わりに処理する。
2. CopyとMoveの規則に従って結果を確保する。
3. その結果式とともに寿命が終わる一時値を破棄する。
4. 離れるスコープのスコープ退出を実行する（[§16.2.1](#s16-2-1)）。
5. 確保した結果を渡し、対象の終了または継続を完了する。

その他の生存する一時値は、[§16.2.1](#s16-2-1)の「中断された式」に従って後処理する。省略されたオペランドはUnitを供給する（[§14.5.1](#s14-5-1)）。Moveされた結果は取得元では破棄しない。Copyされた結果では、取得元の責任がそのまま残る。後処理は確保した結果を置き換えられない。結果に含まれる借用は、すべての後処理の後も有効でなければならない。処理全体を通じて破棄時の寿命検査を適用する（[§15.6.6](#s15-6-6)）。

後処理するのは実際に離れるスコープだけである。`continue`は現在の反復のスコープを後処理し、名前付きの制御転送は離れるすべてのスコープを後処理する。

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value                // Returns the already Copied 1.
```

<a id="s16-2-3"></a>
#### 16.2.3. 完了と異常終了

登録は本体の開始時に消費され、実行は最大1回である。保留中の結果や制御転送を渡すのは、すべての後処理が完了した後だけである。後処理は一時停止せず、取消しも検知しない（[§24.2.2](#s24-2-2)、[§22.3.3](#s22-3-3)）。

Abortは、`drop`内の場合も含め、それ以降の後処理を行わず、何も巻き戻さない（[§17.4](#s17-4)）。プロセスの強制終了、未定義動作、解消されない参照カウントの循環（[§13.6.3](#s13-6-3)）では、後処理を保証しない。

<a id="s16-3"></a>
### 16.3. 破棄宣言

`drop`宣言は、構造体の1つの層の破棄処理を定義する。

- 生成されたフラグメントも含め、struct本体の直下だけに置ける。各structは、条件選択と統合の後で最大1つを持つ。
- パラメーター、ジェネリクス、Origin、結果の型注釈、修飾子を持たない（[§F.3](#sf-3)）。本体には共通の本体形式を使う。単一項目の式の結果は捨て、明示的な`return`はUnitに合わせる。unsafe操作には内側のUnsafeブロックが必要となる。
- 呼び出すのは破棄処理だけである。直接・間接に呼び出したり、関数値として取得したりするとエラーとなる。
- `drop => ()`を含むどの`drop`も、そのstructをNon-Copyにする（[§3.5.2](#s3-5-2)）。値を部分Moveするすべての経路では、値全体の使用または破棄の前に完全性を復元しなければならない（[§15.1.3](#s15-1-3)）。値全体のMoveは認める。
- アクセス修飾子が破棄を抑止することはない。

```kimi
struct Marker
    Self is Copy      // Error: a struct with a drop cannot be Copy.
    drop => ()
```

<a id="s16-3-1"></a>
#### 16.3.1. 破棄レシーバ

`drop`内の`self`は、構築レシーバと同様に（[§6.3.4](#s6-3-4)）、アクセスとLoanの検査では`uniq/Self`相当の排他アクセスを持つ。これは破棄レシーバであり、通常の借用値や所有者ではなく、CopyもMoveもできない。

- レシーバ全体は、代入・交換・swapの対象にできず、メソッドやセッターを含め、`uniq/Self`として渡せない。
- 初期化済みのフィールドは通常の権限に従うが、どのフィールドもMoveして取り出せない。レシーバは所有するstructのPlaceではなく（[§11.2.2](#s11-2-2)）、排他アクセスで到達した参照先をMovedのまま残すこともない（[§15.1.3](#s15-1-3)）。フィールドの値を取り出すには、`Kimi.Intrinsics.exchange`、`swap`（[§15.7](#s15-7)）、または型固有の操作を使う。
- `self@ref`は通常の`ref/Self`であり、そのOriginは`drop`本体内で終わる。補助関数に渡せるが、キャプチャー、格納、戻り値にはできない。
- 破棄レシーバは、`obj`、`rc`、`arc`ハンドルやオブジェクトビュー（`objref`、`objuniq`）になれず、後で使うために格納したり、外へ流出させたりできない。

層`D`の破棄中は`Self`が`D`となり、すでに後処理を終えた派生層は使えない。

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

<a id="s16-3-2"></a>
#### 16.3.2. 構成要素の破棄

完全なstructの層を破棄するには、まずその`drop`とその本体のスコープ退出を実行し、次に自身のフィールドを論理的な宣言順の逆に破棄し（[§6.2.1](#s6-2-1)）、最後に直接の基底を同じ方法で破棄する。`drop`開始時には、すべてのフィールドと基底がInitializedである。`drop`内の`return`がこれらの破棄を飛ばすことはない。computedプロパティは構成要素ではなく、後処理はアクセサーを呼ばない。

| 破棄時の層の状態 | 自身の`drop` | フィールド |
| --- | --- | --- |
| 完全 | 宣言されていれば実行 | その後にすべて |
| 部分Move、自身の`drop`なし | なし | 残るInitializedフィールドを逆順に破棄 |
| 部分Move、自身の`drop`あり | エラー。先に完全性を復元しなければならない（[§15.1.3](#s15-1-3)） | — |

基底は自身のフィールドの後に続き、基底自身の状態を使う。フィールドへの代入順や後の置換によって、この順序は変わらない。

- タプルと配列の要素は添字の降順に破棄する。各要素の破棄を完全に終えてから、その前の要素へ進む。
- enumは、現在のケースに残るペイロードだけを、宣言順の逆に破棄する。所有による分解で移譲されたペイロードは飛ばす（[§15.1.6](#s15-1-6)）。
- SliceやUniqSliceの破棄は、その要素を破棄しない（[§4.8](#s4-8)）。Scalarの固定長配列の破棄は、`noinit`であっても要素を読み取らない（[§4.3.4](#s4-3-4)）。
- ライブラリーのコンテナーは、自身の契約で要素の破棄順序を定める。

```kimi
struct Triple
    var a: Resource
    var b: Resource
    var c: Resource

func split(t: Triple)
    consume(t.b@move)   // Partial Move; Triple has no drop.
// Scope exit destroys t.c, then t.a.
```

<a id="s16-3-3"></a>
#### 16.3.3. ハンドルとオブジェクトの解放

`owner/T`は、正確に`T`として破棄する。`obj/T`の破棄は、オブジェクトを破棄してからその記憶領域を解放する。`rc/T`または`arc/T`の破棄は強参照を1つ解放する。参照数が0になる解放では、オブジェクトを破棄してその記憶領域を解放する。オブジェクトは、基底ビューを介する場合も常に実際の型として破棄し、派生型から基底型へ完全な後処理を行う。

**ハンドルの破棄とLoan。** ハンドルの破棄が破棄するのはハンドルのPlaceだけであり、これを生存中のLoanと比較する（[§15.6.2](#s15-6-2)）。オブジェクトを通じて所有する格納領域は対象にしない。その内部への借用はすべて、到達経路のハンドルに対するLoanを保持するためである（[§13.5.5](#s13-5-5)）。`rc`や`arc`の解放はどれも最後の解放になり得るため、ペイロードの破棄が観測するものを観測し（[§15.6.6](#s15-6-6)）、静的フィールドなどの他の格納領域に対するその後処理の効果を、外側の要約に加える（[§15.6.4](#s15-6-4)、[§8.11.2](#s8-11-2)）。所有ハンドルのパラメーターを破棄する呼出し先も、同じ効果を加える。

**型消去された破棄。** 動的型または環境の型が静的に分からない値の破棄は、不明な効果を持つ。その効果をView Target自身の`drop`だけや、プログラムが宣言する派生型から求めることはない。対象には、View TargetがSealedではない`obj`、`rc`、`arc`ハンドル（`rc/Base`や`obj/Shape`のようなopenな基底、またはランタイムContractのビュー）と、共通関数型の値の環境が含まれる。破棄される値の構成要素である場合も含む。この破棄は、不明な呼出しと同様に、可変の静的フィールドに対する生存中のすべてのLoanと衝突し（[§15.6.4](#s15-6-4)）、`confined`では分類不能な効果となる（[§8.11.2](#s8-11-2)）。SealedなView Targetを持つハンドルと`owner/T`は、その型の破棄要約を使う。ジェネリック本体で型パラメーターまたは関連型射影の値を破棄する場合も、その型がCopyでない限り、本体自身のLoan検査では不明となる。本体の要約では、呼出し側が実際の型と合成するパラメーター依存の効果として公開する（[§4.9.6](#s4-9-6)）。ただし、[§15.6.4](#s15-6-4)がそれを広げる場合は除く。

```kimi
var action: () -> () = func [resource@move] () => ()
let view = State.text@ref          // State.text is a mutable static Field.
action = func [] () => ()          // Error: destroying the old environment has unknown effects.
inspect(view)
```

**ペイロードの置換。** ペイロード全体の置換（[§15.7.3](#s15-7-3)）は、オブジェクトの格納領域、参照数、ライフサイクル状態を保ったまま、古い内容をその場で破棄する。この時点で格納領域はUninitializedとなるが、`let`の初期化履歴はリセットしない。その後、コンストラクター、初期化子、セッターを実行せずに置換する内容を配置する。ペイロードの交換では、代わりに古い内容を結果へ移譲する。同じアドレスであっても、古い内容への依存は終わる。

借用や生ポインターの破棄は、その参照先を破棄しない。Scalarには破棄処理がない。

<a id="s16-4"></a>
### 16.4. クロージャー環境と破棄中のオブジェクト

**クロージャー環境。** 残るキャプチャーは初期化順の逆に破棄する。Consuming呼出しでは環境の束縛がパラメーターより前に置かれるため、キャプチャーは最後に、結果を渡す前に破棄する。SharedとExclusiveの呼出しは環境を破棄しない。キャプチャーの破棄は他の構成要素と同じようにOriginを観測し、サイズが0のキャプチャーでも同様である。

```kimi
let first = makeResource("first")
let second = makeResource("second")
let job = func [first@move, second@move] () => ()
// Destroying job destroys second, then first.
```

**破棄中のオブジェクト。** 実行時ディスパッチ、型検査、検査付きキャストにはハンドルまたはオブジェクトビューが必要であり、破棄レシーバはそのどちらにもなれないため（[§16.3.1](#s16-3-1)）、破棄中のオブジェクトにこれらは適用できない。そのオブジェクトへのWeakを`upgrade`すると`None`を返す（[§13.6.3](#s13-6-3)）。破棄は、呼出しが`uniq`レシーバを保持するのと同様に（[§15.6.4](#s15-6-4)）、完了するまで破棄対象の格納領域への排他Loanを保持する。静的フィールドを介するものも含め、その格納領域へのその他のアクセスはLoanの衝突となる。`drop`内の呼出しに追加の検査は行わない。外部関数や関数値は、通常のOrigin、Loan、静的効果の規則だけで検査する。生ポインターを通じてこれらの制限に違反すると未定義動作となる（[§5](#s5)）。

```kimi
struct File
    var handle: isize
    drop
        Native.close(self.handle)   // Allowed: a foreign call checked by the ordinary rules.
```

---

<a id="s17"></a>
## 17. 失敗の処理



<a id="s17-1"></a>
### 17.1. エラー方針

Kimigayoに例外はない。呼出し側が処理できる失敗は`Option<T>`または`Result<T, E>`の値で表し（[§17.2](#s17-2)）、通常の実行を継続できなければプロセスをAbortする（[§17.4](#s17-4)）。APIは実行時の警告を通常の値として返す。失敗値の伝播は明示的な`try`でのみ行う（[§17.3](#s17-3)）。後置`?`はない。`require`文（[§14.11](#s14-11)）は明示的な制御フローであり、OptionやResultの取り出しや伝播を行わない。

```kimi
// loadSize(path: string) -> Result<usize, FileError>
func run(path: string) -> Result<(), FileError>
    let size = try loadSize(path) // Explicit propagation.
    // Error: no postfix operator: let size = loadSize(path)?
    return .Ok(())
```

<a id="s17-2"></a>
### 17.2. OptionとResult

`Option<T>`は値、または理由を伴わない通常の不在を表す。`Result<T, E>`は成功、または理由を伴う失敗を表す。`E`は通常の型である。両者はKimiの宣言であり、必須の形状を[§22.1](#s22-1)に定める。プログラムで再宣言しない。

APIの契約では、通常の不在には`Option`を、理由のある失敗には`Result`を選ぶ。`Result<Option<T>, E>`なら両者を区別できる。いずれも通常のenumのCopy導出に従う（[§3.5.2](#s3-5-2)）。`Result<i32, i32>`はCopyだが、`Result<i32, string>`は`Ok`を保持していてもCopyではない。

失敗値は通常の制御フローで処理し、暗黙には伝播しない。失敗値を返すと通常のスコープ終了が実行される（[§16.2](#s16-2)）。`Result`を返す関数もAbortする場合がある。

```kimi
// lookup(id: UserId) -> Result<Option<User>, LookupError>
match lookup(id)
    .Ok(.Some(let user)) => greet(user@move)
    .Ok(.None) => greetGuest()
    .Err(let error) => report(error@move)
```

<a id="s17-3"></a>
### 17.3. tryによる伝播

`try expression`はOptionまたはResultの成功ペイロードを取り出すか、同じ位置の`return`と同様に失敗を返す。結合は文法（`Try`、[§F.9](#sf-9)）に従う。呼出し、選択、添字アクセス、`@`はより強く結合し、乗除算演算子はより弱く結合する。

```kimi
// Inside a function returning Result<…, E>; parseCount() -> Result<i32, E>
try parseCount() + 1         // (try parseCount()) + 1
try parseCount().isEven      // Error: groups as try (parseCount().isEven); Result has no isEven.
try pending@move             // Transfer the stored Result, then extract.
```

**オペランド。** オペランドの正規化された型は、所有するKimiの`Option`または`Result`でなければならない。名前や形状ではなくSymbolで識別し、ジェネリック定義ではそのことが証明されなければならない。`Option<ref/T>`は有効だが、`ref/Option<T>`、`uniq/Result<T, E>`、`obj/Option<T>`は無効である。Never型のオペランドは無効（`try $abort(...)`）だが、成功ペイロードがNeverであることは有効である。

**評価。** オペランドを期待型なしの値コンテキストで一度だけ評価し、所有値として取得する。CopyのPlaceはコピーし、非CopyのPlaceには`@move`が必要で、一時値は転送する。その後は次のとおりである。

| オペランド | 通常の値 | 失敗時のreturn | 必要なreturn先 |
| --- | --- | --- | --- |
| `Option<T>` | `Some`のペイロード、型は`T` | `.None` | `Option<U>` |
| `Result<T, E>` | `Ok`のペイロード、型は`T` | `.Err(error)` | 通常の規則で`error`が`F`に合う`Result<U, F>` |

取り出すのは一層だけである。通常の値はペイロードのPlaceではなく値となる。借用されたペイロードは権限、Origin、Loanを保持し、`try`自体は借用を作らない。`T`と`U`は一致しなくてもよい。エラー型を暗黙に結合したり変換したりすることはない。

**return先。** `try`は、同じ位置で`return .None`または`return .Err(error)`を行う`match`と同様に動作し、そのreturn先と制限に従う。関数、クロージャー、ゲッターにはreturn先があるが、選択、ループ、`do`、`unsafe`にはない。したがって、`return`が禁止される位置（`defer`本体、既定値、検証メッセージ、ソースのトップレベル項目）や、return先がUnitである`main`、`init`、`set`、`drop`では`try`を使えない。この検査は到達不能コードでも、オペランドが`Some`または`Ok`と判明している場合でも行う。

**後処理。** 一時値の後処理とスコープ終了の前に、ペイロードまたは失敗値を確保する。オペランドの元の場所は通常どおりMoved状態を保つ。引数や集成体の評価中に失敗した場合は、残りの評価を飛ばし、取得済みの部分を中断された式として後処理する（[§16.2.1](#s16-2-1)）。保留中のreturnの引渡しは[§16.2.3](#s16-2-3)に従う。

**推論。** オペランドは自身のレシーバ、引数、明示的な型から解決する。`try`に対する期待型もreturn先も、オペランドの推論やオーバーロード選択に逆伝播しないため、`try .None`は無効である。明示したCaseのreturnと同様、匿名関数の暗黙の失敗returnは期待型に依存し、戻り値型の候補を与えない。戻り値型は、注釈、固定された期待シグネチャ（[§10.5](#s10-5)）、または独立して型付けされた別の結果から得る。成功結果を自動的に`Some`や`Ok`で包むことはない。

```kimi
// Inside a function returning Result<…, ParseError>
// parse<T>() -> Result<T, ParseError>; getOpt() -> Option<i32>
let value = try parse<i64>()            // Valid in a compatible return target.
let bad: i64 = try parse()              // Error: the annotation does not infer T.
let g = func () -> Option<i32> => .Some(try getOpt())
let f = func () => try getOpt()         // Error: inferred i32 cannot return None.
func h() -> Option<i32> => try getOpt() // Error: i32 is not Option<i32>.
```

ユーザー定義の`try`、`try`ブロック、OptionとResultの相互変換、暗黙のエラー変換はない。名前が`try`で始まるAPIはこの機能と無関係であり、文書化された結果だけを保証する。

<a id="s17-4"></a>
### 17.4. Abortによる終了

**Abort終了**はプロセス全体を異常終了させる。明示的な`$abort`と実行時検査の失敗には、この節の共通規則が適用される。

<a id="s17-4-1"></a>
#### 17.4.1. 原因

Abortは、プログラムの欠陥または回復不能な外部条件を示す。どの原因でも制御フローは同じである。検査付きの実行時操作は、次の場合にAbortする。整数の結果を型が表現できない場合（ラップ整数型では代わりにラップする。[§13.3.1](#s13-3-1)）、整数のゼロ除算やゼロによる剰余（浮動小数点数の除算はIEEE 754に従う）、位置や範囲の解決失敗、逆順範囲の反復開始、無効な数値変換やシフト数（`@wrap`と`@bits`は失敗しない）、Dictionaryリテラルのキー重複や添字指定したキーの不在、禁止された静的記憶領域へのアクセス（[§22.4.3](#s22-4-3)）、Kimiオブジェクト生成や関数型への型消去に必要なメモリー割当ての失敗、強い所有者数のオーバーフローである。各Kimi操作が無効な入力を定義する。Abortコードは[§22.5.2](#s22-5-2)に列挙する。型テストや検査付きオブジェクトキャストの失敗は、代わりにそれぞれのbool、Option、Resultの契約に従う。同じ原因でも、別のAPI契約では回復可能な場合がある。

```kimi
// tryAllocate(size: usize) -> Option<Buffer>
func allocateRequired(size: usize) -> Buffer
    return match tryAllocate(size)
        .Some(let buffer) => buffer@move
        .None => $abort("Required memory could not be allocated")
```

<a id="s17-4-2"></a>
#### 17.4.2. 明示的なabort

`$abort(message)`は期待型`string`で引数を一度だけ評価してからAbortする。引数が正常完了しない場合は、その引数自体の転送、発散、Abortが発生する。式の型はNeverである（[§3.1.3](#s3-1-3)）。Abortは制御転送ではなく、完了を生成しない（[§14.1](#s14-1)）。

```kimi
func requireValue(value: Option<i32>) -> i32
    return match value
        .Some(let x) => x
        .None => $abort("Required value is missing")
```

<a id="s17-4-3"></a>
#### 17.4.3. 終了と後処理

Abort終了の開始は、プロセスにつき最大一回である。開始したスレッドは通常の実行に戻らず、スコープ終了を行わず、プロセス全体を終了させる。複数のスレッドがAbortを起こす場合、そのうち厳密に一つがAbort終了を開始して報告を試みる。他の開始要求スレッドはすべて戻らず、何も報告しない。他のスレッドはプロセス終了まで動作を続けることがある。その間の動作は未規定であり、どのスレッドもこの終了の一環としてスコープ終了を行わない。この規則は引数評価や終了の実時間に上限を設けない。Abortは捕捉、回復、再開できず、スタックの巻戻しも行わない。Abort終了では静的終了処理も行わない（[§22.4.3](#s22-4-3)）。

スコープ終了中にAbortが始まると、後処理は直ちに止まる。残りの遅延ブロック、自動破棄、実行中の遅延ブロックや`drop`の残りは実行せず、完了済みの後処理も取り消さない。保留中の`return`、`exit`、`continue`、`yield`は放棄し、確保済みの結果は引き渡しも破棄もしない。

```kimi
func process()
    let resource = makeResource()
    defer => close(resource)
    $abort("Fatal condition") // Neither close(resource) nor the destruction of resource runs.
```

スタックの枯渇は未定義動作ではない。プロセスを終了し、スコープ終了は実行しない。Abortとして検出・報告するかどうか、その診断、終了ステータスは未規定である。

Abortは失敗を示す終了ステータスでプロセスを終了する。ランタイムは理由とソース位置（失敗した操作または`$abort`呼出し。[§22.5.1](#s22-5-1)）の報告を試みる。報告に失敗しても終了を妨げない。診断形式、出力ストリーム、終了コードは実装定義である。

<a id="s17-4-4"></a>
#### 17.4.4. ビルドとコンパイル時評価

Abortの条件と動作はすべてのビルドで同じである。暗黙の検査失敗から処理を戻すことはできない。Abortの代わりにラップする動作を選べるのは、ラップ整数型（[§3.1.1](#s3-1-1)）と`@wrap`（[§13.5.4](#s13-5-4)）だけであり、ビルド設定やコンテキストでは選べない。飽和モードはない。ユーザー定義の算術Contractは自身の失敗時の動作を定義する（[§13.3.5](#s13-3-5)）。

実行時検査がAbortするのは、実行されて失敗した場合だけである。最適化によって、実行されない条件分岐内の操作や短絡された操作をAbortさせてはならない。未定義動作をAbortとして検出する保証はない。

必須のコンパイル時評価（ディレクティブ、長さ）の失敗はコンパイル時エラーとなる。リテラルが型に合うかの検査やキーの重複（[§12.4.3](#s12-4-3)）など、言語が定める静的検査は最適化の有無によらず適用する。定数畳込みによって実行時Abortをコンパイル時エラーに変えてはならない。

<a id="s17-5"></a>
### 17.5. 警告

警告は、型が期待型に合うか、実行、オーバーロード選択のいずれも変えない。

一つの値を一か所で捨てる場合、該当する警告のうち最も優先順位が高いものだけを報告する。優先順位は、意図しないUnit推論、捨てられた`Result`、捨てられた`try`の成功値、効果のない値の順である。明示的に結果を捨てる`_ = expression`（[§14.2.4](#s14-2-4)）は、その捨てられる結果への警告だけを抑制する。

補間文字列リテラルを直接`Utf8Writer.write`に渡すと、先に所有文字列が作られることを警告する（[§22.10.4](#s22-10-4)）。この警告は上記の優先順位と独立している。

<a id="s17-5-1"></a>
#### 17.5.1. 捨てられたResultとtryの成功値

結果を捨てる文脈でKimiの`Result`を捨てると、Copy能力や実行時のCaseによらず警告する。Kimiの`Result`のSymbolで識別し、別名を経由する場合も対象となる。ユーザー定義の`Result`という名前の型は対象外である。`Option`を捨てても型固有の警告はない。

`try`の結果を捨てると、UnitまたはNeverであると証明できる場合を除き警告する。`Option<()>`の成功値はUnitである。結果を捨てる文脈は括弧と分岐本体を通じて伝播する。ジェネリック定義では、その定義内でUnitまたはNeverと証明できない限り警告し、インスタンス化時には再検査しない。

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

<a id="s17-5-2"></a>
#### 17.5.2. 意図しないUnit推論

値を使う構文要素、または戻り値型を推論する匿名関数の結果型がUnitと推論され、Unitが宣言、構文規則、期待型によって固定されておらず（名前付き関数での戻り値型の省略はUnitに固定する）、Unitを与えるインデント本体が非Unit・非Neverの値を捨てて末尾に到達する場合、警告する。検査はグループ化とラベルを通り、正常完了する`if`、`match`、`do`の本体内まで進む。明示的な`()`、転送、反復、文、別の関数では止まる。

```kimi
let total = do
    if useCache => loadCached()
    else => compute()
// Warning: total is Unit; the i32 results of both arms are discarded.
let corrected = if useCache => loadCached() else => compute()
```

<a id="s17-5-3"></a>
#### 17.5.3. 捨てられた効果のない値

結果を捨てる文脈の非Unit式が、リテラル、Copyのローカル変数、組込み操作と比較、Caseの構築、タプルだけで構成され、効果がないと証明できる場合、警告する。呼出し、ユーザー定義の比較、Move、Loan、破棄、Abort、発散は効果に数える。呼出し先の本体は調べない。明示的なUnit値`()`には警告しない。

```kimi
func isAdult(age: i32) => age >= 18 // Warning: the bool is discarded.
left == right                       // Warning for initialized i32 locals.
func cleanup() => handle.close()    // No warning: a call is not assumed effect-free.
```

<a id="s17-5-4"></a>
#### 17.5.4. 必ず失敗する位置と範囲

リテラルだけの式（[§12.3](#s12-3)）、それへの`^`の適用、省略された境界だけで構成される位置または範囲は、解決（[§4.6.1](#s4-6-1)、[§4.6.3](#s4-6-3)）がすべての長さで失敗する場合（要素位置`-1`と`^0`、閉区間の終端`..=^0`、逆順範囲`2..=1`と`^3..^5`）、または添字アクセス先や引渡し先の固定配列の長さで失敗する場合に警告する。判定は厳密で、最適化から独立している。他の式から得た境界は判定しない。操作がAbortするのは実行された場合だけであり、名前が`try`で始まる操作は代わりに`None`を返す。

```kimi
let fixed: [3 of i32] = [1, 2, 3]
let a = fixed[3]          // Warning: position 3 fails for length 3.
let c = values[2..=1]     // Warning: reversed for every length.
let d = values[1..^1]     // No warning: resolves when the length is at least 2.
let e = values.remove(^0) // Warning: ^0 is never an element position.
```

<a id="s17-6"></a>
### 17.6. テスト検証操作

<a id="s17-6-1"></a>
#### 17.6.1. 形式と配置

`$expect(condition)`と`$require(condition)`は、位置引数として`bool`式を一つ取り、省略可能な`message:`文字列式を取る（文法：`TestVerification`、[§F.10](#sf-10)）。これらは独立した項目であり、式のオペランドにはならず、条件がtrueならUnitで完了する。テスト専用関数の本体（[§18.6](#s18-6)）と、その中のローカル関数、クロージャー、`defer`本体でのみ使える。テストからしか呼ばれなくても、製品側の補助関数では使えない。検証はOptionやResultの取り出しも、型の絞込みも追加しない。

<a id="s17-6-2"></a>
#### 17.6.2. 評価と失敗

条件は通常の評価、短絡、所有権、Loanの規則に従って一度だけ評価する。その一時値は`bool`条件の寿命を持つ（[§14.2.3](#s14-2-3)）。`true`なら一時値を後処理して実行を続ける。`false`なら失敗を記録し、条件の一時値を後処理し、メッセージがあれば一度だけ評価してその一時値を後処理する。その後、`$expect`は実行を続け、`$require`はAbortする。所有権と初期化の検査では、メッセージとそのMove・初期化は失敗経路でのみ起こる。`bool`を得る前の転送、Abort、発散は検証失敗ではない。`false`を得た後は、何が起きても失敗を取り消さない。`$require`のAbortは[§17.4.3](#s17-4-3)に従い、[§22.5.2](#s22-5-2)で定義した理由と`KIMI_E_REQUIRE`を使う。検証位置を報告し、条件やメッセージを再評価しない。テスト実行では、Abortは現在のケースだけを終了させる。他のケースは引き続き実行・報告する。ケースの隔離方法は実装定義である。

```kimi
#Test
func stopsAfterFailure()
    defer => ::Kimi.Console.writeLine("Not run after Abort")
    $require(false, message: "Input is not ready")
    ::Kimi.Console.writeLine("Not reached")
```

<a id="s17-6-3"></a>
#### 17.6.3. メッセージと観測値

メッセージは条件の一時値を使えない。また、その中の`return`、`exit`、`continue`、`yield`はメッセージの外の構文要素を転送先にできない。

失敗報告に表示するのは、元の評価中に得た値だけである。オペランドの再評価や再読取り、短絡されたオペランドの評価、ユーザーコードの呼出しを行わず、Copy、Move、Loan、寿命の変更も追加しない。どの値をどのように表示するかは実装定義である。検証はすべてのビルドで実行する。常にfalseとなる条件もコンパイル時エラーにはならない。

---

<a id="s18"></a>
## 18. モジュールと依存関係



モジュールはソース宣言をまとめ、他のモジュールから到達できる名前を制御する。プロジェクトファイルの構文、設定、コマンド、パッケージ・ロック・リリースの形式は実装契約である（[§20.2](#s20-2)、[§20.3](#s20-3)、[§20.5](#s20-5)）。

<a id="s18-1"></a>
### 18.1. モジュールとソース環境

| 用語 | 意味 |
| --- | --- |
| Kotonoha | 名前を持つ一つのモジュール。 |
| プロジェクトルート | Kotonohaの宣言階層の最上位。そこにある宣言を**ルート宣言**という。 |
| コンパイルルート | Kotonohaごとに、そのルート宣言、直接依存先の参照名、予約された`Kimi`を合わせたもの。 |
| ソース環境 | SourceDocumentの別名と検索コンテキスト。 |

検索の第4段階（[§9.5](#s9-5)）、`::`パス（[§9.6](#s9-6)）、別名の対象は、常にそのSourceDocumentを含むKotonohaのコンパイルルートを使い、利用側のものは使わない。

解決されたSymbolは、綴りや別名のパスによらず、由来するKotonohaとバージョンを含めて宣言を識別する。宣言の本体、型、制約は常に定義環境で検査する。利用側は他のモジュールに宣言を追加したり、その環境を再束縛したりできない。

統合された宣言の各断片は、それぞれのSourceDocumentのソース環境を保つ。統合によって一つの断片の別名が他の断片に適用されることはない。生成文書も独自のソース環境を持つ。

```kimi
// A.kimi
alias Kimi.Console
struct Config
    func show() -> () => writeLine("a")   // Valid: A.kimi opens Kimi.Console.

// B.kimi
struct Config
    func hide() -> () => writeLine("b")   // Error: B.kimi does not open Kimi.Console.
```

<a id="s18-2"></a>
### 18.2. 別名

ソースから名前で参照できるKotonohaは、直接参照するものだけであり、その参照名を使う。外部メンバーには`Geometry.Shapes.Circle`のような修飾か別名で到達する。非修飾では見つからない。推移的な依存先にすぎないKotonohaは名前で参照できない。

<a id="s18-2-1"></a>
#### 18.2.1. 形式と対象

| 形式 | 意味 |
| --- | --- |
| `alias Path` | 対象の直接のメンバーを検索対象に開く。 |
| `alias Name => Path` | 対象自体を型名前空間の修飾子`Name`として導入する。 |

- **配置。** 別名はSourceDocumentのトップレベルで、通常の宣言や実行可能な項目より前に置く。順序は選択項目と除外項目の両方を含むソース順で判定する（[§19.4](#s19-4)）。ルートのディレクティブ対象や分岐内の別名もトップレベル項目である。入れ子の別名や別名へのアクセス修飾子はエラーとなる。
- **開く対象。** `alias Path`はKotonoha参照、またはすべての引数を束縛した宣言コンテナー（group、struct、enum、contract）を開く。直接のメンバーは、それぞれの名前空間で検索の第5段階に加わる（[§9.5](#s9-5)）。仮引数、Origin、`Self`は開かない。Caseやレシーバメンバーに必要な修飾も含め、開いたメンバーの検索は[§9.5](#s9-5)に従う。
- **名前付きの対象。** `alias Name => Path`の対象はKotonohaまたはgroupに限る。すべての型引数とOrigin引数を束縛した、インスタンス化済みの親の下にあるgroupも含む。対象のメンバーは開かず、ジェネリックパラメーターを持たず、Core、値、記憶領域、宣言の同一性を作らない。
- **解決。** 型引数とOrigin指定を含む対象全体を、その別名のKotonohaのコンパイルルートから解決する（[§18.1](#s18-1)）。ソース別名や既定の別名は使わない。先頭の`::`は省略できる。`i32`や`during static`などの組込み構文は有効なままである。別名の連鎖や循環はなく、別名の順序は解決に影響しない。

どちらの形式もコンテナーの配置場所やインスタンス化方法を追加しない（[§6.1](#s6-1)）。右辺はコンテナー参照であり、式ではない。

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

<a id="s18-2-2"></a>
#### 18.2.2. 意味とスコープ

別名は宣言時に参照を固定する。固定されるのは元の宣言の同一性と引数の束縛であり、使用時のパス置換や再推論ではない。別名宣言は、対象と途中の修飾子について、アクセス、型の形成、入力条件、Originの義務を検査する。証明はFinalityに従う（[§8.3](#s8-3)）。メンバーへのアクセスと使用条件は使用のたびに再検査する。別名はアクセス権も適合の証拠も与えない。

別名は自身のSourceDocument内だけに適用される。他の文書、統合された断片、生成文書、呼出し側には及ばず、再公開もされない。`Project.A`や`::A`として参照できるルートメンバーも作らない。同じ綴りの実際のルート宣言や直接参照は引き続き使える。衝突は[§9.5.1](#s9-5-1)に従う。

開いたメンバーの重複を除くのは、同じ宣言で、引数の束縛全体が等しい場合だけである。`Family<i32>.Helpers`と`Family<string>.Helpers`の両方を開くと、内側の`Token`には二つの候補が残り、通常の同名の曖昧さの規則が適用される。

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

<a id="s18-2-3"></a>
#### 18.2.3. 既定の別名

依存先や生成ソースを含むすべてのSourceDocumentは、グラフで選択された予約済みのKimi Kotonohaを開く、必須の既定の別名を持つ。設定でこれを削除したり対象を変えたりできない。モジュールは追加の既定の別名を設定できる（[§20.2](#s20-2)）。追加分はそのモジュールの文書だけに適用される。

必須と追加の既定の別名は、合わせて検索の第6段階を構成する。この段階内で、同一に解決された参照の重複を除く。ソースに明示した別名はそれより前の第5段階を構成するため、ソースの`alias Kimi`によって優先順位が変わり得る。既に必須である別名を既定の別名に追加しても何も変わらない。

```kimi
// The module configures no additional default aliases.
Console.writeLine("Hello")   // Valid: the mandatory alias opens Kimi.
writeLine("Hello")           // Error: opening Kimi exposes Console, not its members.
```

<a id="s18-3"></a>
### 18.3. Kotonoha間の名前の到達可能性

この版では再公開を提供しない。別名は再公開ではない。利用側が外部の型や要件を名前で参照するには、その由来するKotonohaを直接参照し、そこへのアクセス可能なSymbolパスを持たなければならない（名前の到達可能性、[§9.4](#s9-4)）。その型を含む公開シグネチャも、推移的な依存先の読込みも、到達可能性を与えない。

```kimi
// Geometry depends on Math; Math depends on Units. Math.Arithmetic.scale returns Units.Meter.
alias Math.Arithmetic
alias Units.Meter         // Error: Units is not a direct dependency of Geometry.
let size = scale(2)       // Valid: no Units name is written.
```

<a id="s18-4"></a>
### 18.4. 依存関係

<a id="s18-4-1"></a>
#### 18.4.1. 識別情報と参照名

| 識別情報 | 意味 |
| --- | --- |
| ReferenceName | 利用側がソース内で使う、直接依存先の名前。 |
| PackageId / PackageVersion | 定義元のモジュールと、その正確なリリース。 |
| SourceId | ソースパッケージの正確な内容（[§18.5](#s18-5)）。 |

KotonohaはPackageIdとPackageVersionで識別する。PackageIdのないもの（Applicationまたはパッケージ化されていないProject）はProjectで識別する。同じPackageIdの異なる二つのバージョンは、別のKotonohaである。`internal`アクセスとSymbolの同一性は両者をまたがない。モジュールは同じPackageIdの二つのバージョンを、それぞれ別のReferenceNameで直接参照できる。

PackageIdはASCII小文字で始まり、ASCII小文字と数字からなる空でない区間を`.`または`-`で区切ったものとする。PackageVersionはASCII英字または数字で始まり、以降はASCII英字、数字、`.`、`-`、`+`からなる。バージョンは大文字・小文字を区別した厳密な一致で比較し、順序や互換性を推論しない。参照されるLibraryとパッケージ化するProjectには両方が必要である。Applicationが依存関係を利用する際にはどちらも不要である。

ReferenceNameは通常の識別子であり、`Kimi`であってはならない。どの名前空間でも、ルート宣言に直接依存先のReferenceNameや`Kimi`を使ってはならない。ルート宣言同士には通常の名前空間規則を適用する（[§9.2](#s9-2)）。Kimi Kotonoha自身のソースでは、`Kimi`はそのKotonohaを表し、必須の既定の別名がそれを開く。一つの依存先に複数の名前やパスがあっても、新しい型や静的記憶領域は作られない。

```kimi
// Geometry has a dependency named Math.
group Kimi          // Error: `Kimi` is reserved in the Compilation root.
    func f() -> () => ()
struct Math         // Error: Math is a dependency reference name.
```

<a id="s18-4-2"></a>
#### 18.4.2. 依存グラフ

モジュールグラフは非循環である。モジュール自身の識別情報への参照と循環はエラーとなる。相互依存する宣言は同じKotonohaに置く。テスト拡張を含む一つのターゲットグラフ内では、ルート自身を含む各識別情報について、入力種別（ProjectまたはPackage）、内容、意味上の設定はそれぞれ一つである。そのため、テスト依存関係がルート自身の識別情報に到達した場合は、ルートの製品ノードに合流する。入力種別や内容が異なればエラーとなる。複数のパスから到達した同一ノードは統合する。ProjectをPackageの代わりにしたり、その逆にしたりしない。

各モジュールは自身の別名、既定の別名、アクセスコンテキスト、宣言、閉じた特殊化集合を保つ。グラフでは一つのKimi Kotonohaを使う。

GeometryのプロジェクトファイルはMathへの依存を宣言する。設定は[§20.2](#s20-2)に列挙する。

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

<a id="s18-4-3"></a>
#### 18.4.3. 解決と固定

各依存先はPackageIdとPackageVersionを指定し、Projectまたはソースパッケージで供給される（[§20.2](#s20-2)）。依存レコードは`Project`（`.kimiproj`パス）または`Package`（`.kimipkg`パス）の一方だけで内容を指定する。どちらもなければ、明示的なパッケージとストアディレクトリを列挙する`PackageSources`から候補を得る。パスは宣言元プロジェクトからの相対パスである。読み込んだ識別情報は要求したものと一致しなければならない。Package依存先は厳密に一つのSourceIdに解決する。候補の競合はエラーであり、最初の一致を選ぶことも、バージョン範囲を選ぶことも、欠けたソースを推測することもない。テスト専用入力は製品の候補を供給しない。ソースパッケージは依存先のSourceIdを固定し、以後変えない。ロックはProjectのPackage依存先のSourceIdを固定し、その固定を変えるのは明示的なrestoreだけである（[§20.5](#s20-5)）。

<a id="s18-4-4"></a>
#### 18.4.4. 有効な環境

グラフ全体に一つのターゲットと一つのDebug設定を適用し、すべての依存先はトリプル文字列の厳密な一致によって、そのターゲットを許可しなければならない（[§20.2.1](#s20-2-1)）。CompileTimeSettingsは定義元モジュールに属し、利用側は供給しない。この版には依存辺ごとの上書き、機能の和集合、条件付き依存辺はない。

利用側のコンパイラーが、一つの言語バージョン（[§20.2.2](#s20-2-2)）でグラフ全体を検査する。公開側のコンパイラーは関係しない。パッケージ化したProjectの設定は、そのProject自身の宣言か、言語バージョンが固定する既定値だけに由来し、パッケージに記録する。

```kimi
// Math's source; Math's own CompileTimeSettings decide `fast`, whatever Geometry sets.
public group Arithmetic
    #if fast
        public func half(value: i32) -> i32 => value / 2
```

<a id="s18-5"></a>
### 18.5. ソースパッケージ

Kotonohaはソースパッケージとして配布する。ソースパッケージは一つのLibrary Kotonohaを収める。内容は、論理名を伴う製品ソース（[§20.1](#s20-1)）、PackageIdとPackageVersion、LangVersion、Targets、CompileTimeSettings、ユーザーが追加した既定の別名、ネイティブ要件、依存先のSourceIdである。SourceIdはパッケージの正確な内容を識別する。パッケージ形式は実装定義である（[§20.5](#s20-5)）。

- パッケージには分岐を選択して書き換えたソースではなく、元の条件付きソースを収める。コンパイルのターゲットとDebug設定、およびパッケージ自身のCompileTimeSettingsが分岐を選ぶ（[§19.2](#s19-2)）。
- パッケージのTargetsは許可する利用環境を示すもので、検証成功を示すものではない。各利用側は自身の環境でパッケージを検査する。
- 配布によって、定義位置での検索、アクセス、ジェネリック選択、所有権の境界は変わらない。

この版にはバイナリーインターフェース、バージョン範囲、ネットワークレジストリは含めない（[§D](#sd)）。

```kimi
// Packed unchanged; a consumer building for windows selects the first arm.
#switch
    #case os == "windows"
        func lineEnd() -> string => "\r\n"
    #case _
        func lineEnd() -> string => "\n"
```

<a id="s18-6"></a>
### 18.6. 製品入力とテスト入力

Projectのソースは[§20.1](#s20-1)で定める。`TestSources`に列挙されたファイルは、プロジェクトディレクトリ内にあってもテスト専用である。テスト依存先はテスト用にだけ解決し、製品には届かない。

- **所属。** TestSources内の宣言と、それ以外のソース内のTest関数はテスト専用である。入れ子の宣言は外側の宣言の所属を継承する。それ以外の宣言は、印のない補助関数も含め、すべて製品宣言となる。生成宣言は入力の所属を引き継ぐ。製品の生成にテスト専用入力は使えず、生成されたTest関数はテスト専用となる。`tests`などのディレクトリ名やファイル名に意味はない。
- **製品の固定。** 製品の検索、オーバーロード、型、レイアウト、適合、特殊化、初期化、破棄は製品入力だけから決める。テスト専用コードは、インラインの`#Test`編集を含め、これらを一切変えない。
- **テストができること。** テストは製品の宣言と依存関係をそのまま使う。製品コンテナーに関数を追加できるが、それらが製品側の候補になることはない。宣言重複とアクセスの規則は引き続き適用する。製品型のフィールド、基底、適合などの固定された意味を変えたり、参照名を再割当てしたり、依存先の内容を変えたりしてはならない。テストによる製品ジェネリックのインスタンス化は、製品の定義時検索を保つ。
- **伝播。** 依存先自身のテストとテスト依存関係は伝播しない。

```kimi
// tests/MeasureTests.kimi, listed in TestSources
group MeasureTests
    #Test
    func doubled()
        $expect(Measure.doubled(3) == 6)
```

---

<a id="s19"></a>
## 19. コンパイル時ディレクティブ



ディレクティブはコンパイル中にソース構文を選択し、実行時の制御フローは生成しない。`#if`は一つの構文ノードを含めるか除外するかを決める。`#switch`は**Caseグループ**を導入し、その`#case`分岐を選択肢とする。大文字で始まる`#Name`はディレクティブではなく属性である（[§6.7](#s6-7)）。

<a id="s19-1"></a>
### 19.1. 配置と構造

ディレクティブは、それを含むリストの項目全体を選択する。

| 外側のリスト | ディレクティブが選択できる項目 |
| --- | --- |
| SourceDocumentのルート | ソース宣言、ソース内だけの別名、その位置で許可される実行可能項目 |
| group、rootgroup、structの本体 | その種類で許可されるコンテナー項目 |
| enumの本体 | enumのCase、関数、制約、関連型の指定 |
| contractの本体 | 要件、関連型の宣言、制約 |
| 条件付き適合の実装ブロック | 関数、計算メンバー、関連型の指定 |
| 実行可能な本体または関数本体 | 実行可能項目と、Origin関係を含む関数先頭の制約 |

ディレクティブは式、パターン、型の一部を置き換えない。match分岐、パラメーター、引数、ジェネリック、アクセサー、キャプチャー、タプル、コレクション要素の各リスト内には直接置けない。クロージャー本体など、式に入れ子になったインデント付き実行可能本体は実行可能本体であり、ディレクティブを含められる。単一項目の本体にはディレクティブを含められない（[§14.2](#s14-2)）。

`#if Condition`は、同じインデントの次の構文ノードか、項目を含む一つのインデントブロックを制御する。`#if`、`#switch`、`#case`の形式は[§F.11](#sf-11)で定める。Caseグループは`#switch`本体の末尾で終わるため、二つのグループを隣接させられる。`#switch`は一つの項目であり、`#if`で制御できる。

選択した項目は、ソース順で外側のリストに差し込む。ディレクティブのインデントは、スコープ、寿命の境界、後処理位置、制御転送先を作らない。選択した`defer`やローカル変数は外側のスコープに属し、その後の項目から見える。選択とソース生成の後、最終的な宣言木を検証する（[§6.1.1](#s6-1-1)）。

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

<a id="s19-2"></a>
### 19.2. 条件

条件の型は`bool`であり、通常の優先順位で次の形式だけを使う。

| 形式 | 規則 |
| --- | --- |
| `true`、`false`、整数リテラル、文字列リテラル | 整数は`i64`であり、その型に収まらなければならない。符号は整数リテラルに直接付ける場合だけ許可する。文字列に補間はない。 |
| 条件名 | 組込み条件（[§19.2.1](#s19-2-1)）またはそのモジュールのCompileTimeSettingsの項目。 |
| `not E`、`E and E`、`E or E` | オペランドは`bool`。 |
| `E == E`、`E != E` | オペランドは同じ型の`bool`、`i64`、`string`。文字列は序数順で、大文字・小文字を区別して比較する。 |
| `(E)` | グループ化。 |

その他の形式はすべて無効な条件である。`is`テスト、呼出し、メンバーアクセス、添字アクセス、算術、大小比較、変換、コレクション、浮動小数点・文字・nullリテラルも含む。どのスコープでも、条件は型、Semantics、Contract、Origin、ジェネリック引数に依存しない。

条件名は、大文字・小文字を区別して（[§2.5](#s2-5)）、組込み条件とそのモジュールのCompileTimeSettings（[§20.2.1](#s20-2-1)）だけから解決する。宣言、ジェネリックパラメーター、別名、実行時の値は条件名を供給せず、隠しもしない。制約も条件名を追加しない。`Windows`は`windows`ではない。モジュールの条件名とその型はすべてのコンパイルで同じであり、異なるのは値だけである。したがって未知の名前は、どのターゲット、どのビルドモードでも未知となる。

```kimi
#if os == "windows" or os == "linux"
    func supported() -> bool => true

#if debug and featureEnabled          // featureEnabled: a configured bool setting.
    func trace() -> () => log("trace")

#if T is i32                          // Error: Type tests are not Conditions.
    func wide() -> () => ()
```

<a id="s19-2-1"></a>
#### 19.2.1. 組込み条件

| 名前 | 型 | 値 |
| --- | --- | --- |
| `os` | `string` | ターゲットのOS系列：`"windows"`、`"linux"`、`"macos"`、`"unknown"` |
| `arch` | `string` | ターゲットのアーキテクチャ系列：`"x86_64"`、`"x86"`、`"aarch64"`、`"riscv64"`、`"unknown"` |
| `windows`、`linux`、`macos` | `bool` | `os == "windows"`、`os == "linux"`、`os == "macos"` |
| `debug`、`release` | `bool` | ビルドモード。`release == not debug` |
| `pointerWidth` | `i64` | 生ポインターのビット幅 |

`os`と`arch`はターゲットから導出する（[§20.2.1](#s20-2-1)）。モジュールのCompileTimeSettingsで組込み名を再利用してはならない。

```kimi
#switch
    #case os == "windows" and arch == "x86_64"
        func platformName() -> string => "win-x64"
    #case _
        func platformName() -> string => "other"
```

<a id="s19-3"></a>
### 19.3. 評価と選択

ターゲット、ビルドモード、モジュールのCompileTimeSettingsは、構文解析前に条件のすべての入力を固定する。条件の評価結果はTrue、False、Errorのいずれかである。Errorは、無効な形式、未知の名前、オペランド型の不一致、非`bool`の結果を意味する。コンパイル時エラーとなり、`not`、`and`、`or`の結果はErrorに吸収される。両オペランドは常に検証するため、`false and missing`、`true or (T is i32)`、`false and 1`はエラーとなる。SourceDocument内のすべての条件を検証する。除外構文内（[§19.4](#s19-4)）、`#switch`の後続分岐内、誤った位置の`#case`内の条件も含む。

選択を行うのは、除外構文の外にあるディレクティブだけである。

- `#if`は、条件がTrueなら対象を選択する。
- 構造と条件がすべて有効な`#switch`は、最初のTrueの分岐を選ぶ。Trueの分岐がなければ`#case _`を選ぶ。Trueの分岐も`#case _`もなければエラーとなる。したがって`#case _`の省略は、どの分岐にも合わない環境を拒否する。

選択は構文解析中に完了する。条件を後回しにはしない。ジェネリックのインスタンス化、名前解決、ソース生成は条件の入力を供給せず、ディレクティブを再選択しない。生成ソースも同じ入力を見る。選択した項目内の名前は、その後、外側のスコープの通常の規則で解決する。別のターゲットや設定には、別のコンパイルが必要である。

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

<a id="s19-4"></a>
### 19.4. 除外構文

**除外構文**とは、条件がFalseまたはErrorの`#if`の対象、`#switch`の選択された分岐以外のすべての分岐（何も選択されなければ全分岐）、`#switch`の外にある`#case`の本体、およびそれらの内側のすべてをいう。除外構文内のディレクティブは何も選択せず、選択エラーも報告しない。

除外構文には、選択された構文と同じすべてのソース検査を行うが、意味検査は行わない。

- **すべての場所で検査するもの：** 文字コード、トークン、レイアウト、文法、修飾子・属性・ディレクティブの配置、本体が空でないこと（[§14.2.1](#s14-2-1)）、後述の配置規則、条件の検証。
- **選択された構文だけで検査するもの：** 選択、名前、型、所有権、属性の解決、個数と一意性の規則、宣言の統合、文書の対応付け、それ以降のすべてのコンパイル処理。

配置規則は選択前に、選択項目と除外項目の両方を含めたソース順で判定する。別名は通常の宣言や実行可能項目より前に置き（[§18.2.1](#s18-2-1)）、制約とOrigin関係は先頭領域に置く（[§7.5](#s7-5)、[§11.6](#s11-6)、[§15.3.3](#s15-3-3)）。ディレクティブ自体は項目ではない。その対象と分岐に含まれる項目を、それぞれの種類で数える。宣言重複の禁止やenumに少なくとも一つのCaseが必要といった個数・一意性の規則は、選択後の選択項目だけに適用する。本体が空でないという規則はソースの規則であり、すべての項目が除外された本体も有効なままである。

ソース検査は条件の値を読まない。そのため、生成されていないSourceDocumentの字句・文法・配置・条件エラーは、そのモジュールのすべてのコンパイルで発生する。除外構文内の診断は、選択構文内の場合と同じコードと主位置を持つ。除外構文は宣言、項目、制約、属性を何も供給しない。

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

---

<a id="s20"></a>
## 20. コンパイル設定

[実装仕様目次](#implementation-specification)

この章は、windows-x64プロファイルのプロジェクト、コマンド、出力、テスト実行を定める。

<a id="s20-1"></a>
### 20.1. プロジェクトとソース

**Project**（`.kimiproj`）は、一つのApplicationまたはLibraryとそのKotonohaを定義する。**Solution**（`.kimisln`）はProjectを列挙し、既定の`LangVersion`を指定できる。一回のコンパイルは、一つのProjectを一つのターゲットと一つのDebug設定でコンパイルする。

- **製品ソース**は、`.kimiproj`のディレクトリ直下にある通常の`*.kimi`ファイルである。サブディレクトリは検索しない。それ以外のファイルはProjectに加わらない。
- `TestSources`に列挙されたファイルは製品ソースではなく、テスト専用である（[§18.6](#s18-6)）。各項目はプロジェクトディレクトリ内を指すプロジェクト相対パスであり、サブディレクトリのファイルも指定できる。グロブ、不正なパス、重複はエラーとなる。
- ソースの**論理名**は、`/`区切りのプロジェクト相対パスである。論理名はNFCでなければならず、単純ケースフォールディングで等しくなる二つの名前はエラーとなる。

```text
App/App.kimiproj
App/Main.kimi          product source, logical name Main.kimi
App/sub/Helper.kimi    not a source unless listed in TestSources
```

<a id="s20-2"></a>
### 20.2. 設定

`.kimiproj`と`.kimisln`の表記構文は実装定義である。未知・重複の設定や型の合わない値はエラーとなる。

| 設定 | 型 | 既定値 | 定義箇所 |
| --- | --- | --- | --- |
| OutputKind | `Application`または`Library` | `Application` | [§20.3.2](#s20-3-2) |
| Targets | ターゲットトリプル | 必須 | [§20.2.1](#s20-2-1) |
| PackageId, PackageVersion | 文字列 | なし | [§18.4.1](#s18-4-1) |
| LangVersion | バージョン | 定義箇所を参照 | [§20.2.2](#s20-2-2) |
| Alias | 既定の別名 | なし | [§18.2.3](#s18-2-3) |
| CompileTimeSettings | 名前から`bool`、`i64`、`string`への対応 | 空 | [§20.2.1](#s20-2-1) |
| Dependencies, PackageSources | 依存レコード | 空 | [§18.4](#s18-4) |
| TestSources | プロジェクト相対パス | 空 | [§20.1](#s20-1) |
| TestDependencies | Dependenciesと同じ | 空 | [§18.6](#s18-6) |
| TestProjectId | 空でない文字列 | なし | [§20.6.2](#s20-6-2) |
| Test | ケース設定 | 定義箇所を参照 | [§20.6.2](#s20-6-2) |
| NativeRequirements, NativeLibraries | ターゲット別レコード | 空 | [§20.4](#s20-4) |
| Optimization | `O0`または`O2` | `O2` | [§20.3.2](#s20-3-2) |
| OutputPath | `.ll`パス | `bin/<target>/<Name>.ll` | [§20.3.2](#s20-3-2) |
| LlvmBin | ディレクトリ | なし | [§20.3.2](#s20-3-2) |

<a id="s20-2-1"></a>
#### 20.2.1. ターゲットとコンパイル時設定

windows-x64プロファイルのターゲットは`x86_64-pc-windows-msvc`一つである。他のターゲットは[§22](#s22)に従う。Projectの`Targets`と依存先の許可ターゲットは、大文字・小文字を区別した厳密な文字列として比較する。

組込み条件`os`と`arch`（[§19.2](#s19-2)）は、トリプルのOSとアーキテクチャから得る。OSバージョンの接尾辞は取り除く。

| トリプルの要素 | 値 |
| --- | --- |
| OS `windows`、`win32` | `windows` |
| OS `linux` | `linux` |
| OS `macos`、`macosx`、`darwin` | `macos` |
| その他のOS | `unknown` |
| arch `x86_64`、`amd64` | `x86_64` |
| arch `i386`、`i486`、`i586`、`i686`、`x86` | `x86` |
| arch `aarch64`、`arm64` | `aarch64` |
| arch `riscv64` | `riscv64` |
| その他のarch | `unknown` |

各`CompileTimeSettings`項目は、`bool`、`i64`、`string`のいずれか一つの値を持つ条件を追加する。名前は識別子であり（[§2.5](#s2-5)）、名前の重複はエラーとなる。組込み名は[§19.2.1](#s19-2-1)により拒否する。

```kimi
// CompileTimeSettings={ Tracing=true }
group Log
    #if Tracing
        func note(text: ref/string) -> () => Console.writeLine(text)
```

<a id="s20-2-2"></a>
#### 20.2.2. 言語バージョン

`LangVersion`を指定する場合、対応するバージョンを指定しなければならない。この版が対応するのは`0.0.2`だけであり、それ以外はエラーとなる。省略時にはSolutionの`LangVersion`を使い、それもなければコンパイラーの現在のバージョンを使う。パッケージ化するProjectは明示しなければならない。

<a id="s20-3"></a>
### 20.3. コマンド

| コマンド | 動作 |
| --- | --- |
| `restore <project>` | 依存先を解決し、ロックを置き換える（[§20.5](#s20-5)）。ネットワークにはアクセスしない。 |
| `check <input>` | `build`と同じ方法で入力、ターゲット、Debug設定を選び、選択したプロジェクトごとにProduct単位を一つ検査する。順序、単位失敗後の検査継続、全体の受理判定、取消しは[§23.2.3](#s23-2-3)、単一のJSONコマンド文書は[§23.3](#s23-3)で定める。 |
| `emit <input>` | 検査し、`.ll`と`.link.json`の組を公開する。LLVMツールは実行しない。 |
| `build <input>` | 現在の製品ソースを検査し、OutputKindに応じた成果物を公開する（§20.3.2）。`--Manifest <file>`を指定すると、ソースを読み込まず、出力済みのApplicationの組をビルドする。Libraryのマニフェストは拒否する。生成設定はマニフェストから取得し、ソース、ターゲット、Debug、ロックのオプションはエラーとなる。 |
| `run <input>` | `build`と同じようにビルドした後、Applicationを実行する。ビルド失敗時は何も起動しない。`--no-build`では、最新のビルド記録が成功を示し、解決した入力を指し、実行ファイルのハッシュと一致する場合に、既存の実行ファイルを実行する。`run <file.exe>`はそのファイルを直接実行する。 |
| `test <input>` | テストを実行する（[§20.6](#s20-6)）。 |
| `pack`、`publish`、`store verify` | パッケージ操作（[§20.5](#s20-5)）。 |
| `toolchain verify` | プロジェクトを読み込まずにツールチェーンを検査する（[§20.3.2](#s20-3-2)）。 |
| `lsp` | 標準入力・標準出力でLanguage Server Protocolを提供する（[§23.4](#s23-4)）。 |

- オプション名は大文字・小文字を区別し、この章の表記に従う（`--Format`であり、`--format`ではない）。`--Format`は既定の`text`または`json`を取る。真偽値オプションは`true`または`false`を取る。`--no-build`単独はtrueを意味する。
- `--Target`は、`check`、`emit`、`build`、`run`、`pack`で設定済みターゲットを選ぶ。`test`では選択した各Projectのターゲットを選ぶ。指定しなければ各Projectの唯一のターゲットを使い、複数あればエラーとなる。既定のモードはReleaseである。`--Debug true`は`Optimization`と独立してDebugを選ぶ。
- `check`はコマンド全体が受理された場合に0を返し（[§23.2.3](#s23-2-3)）、それ以外は1を返す。他のコマンドは成功時に0、失敗時に1を返す。`run`はプログラムの終了コードを返す。起動前に失敗した場合は標準エラー出力で報告し、1を返す。`test`は[§20.6.4](#s20-6-4)に従う。取消しは130を返す。
- `run`は標準ストリームを引き継ぎ、プロジェクトディレクトリで実行する（`.exe`の直接実行では呼出し元ディレクトリ）。

<a id="s20-3-1"></a>
#### 20.3.1. 入力の解決と暗黙のプロジェクト

入力は起動ディレクトリからの相対パスであり、入力を省略すると現在のディレクトリを使う。`.kimi`、`.kimiproj`、`.kimisln`、`.exe`で終わる入力（ASCIIの大文字・小文字を区別しない）は、そのパスだけを選択する。その他の入力`A`は、次のうち最初に存在するものに解決する。

1. パス`A`。
2. ファイル`A.kimiproj`。
3. ファイル`A.kimi`。
4. いずれもなければ、試したパスを示すエラー。

選択したファイルが読取り不能、無効、未対応であればエラーとし、次の候補を試す理由にはしない。ディレクトリ入力は、そのディレクトリだけを調べる。`.kimisln`が厳密に一つならそのSolutionを選び、複数ならエラーとなる。一つもなければすべての`.kimiproj`を選び、それもなければエラーとなる。選択したProjectのいずれかを読み込めなければ、コマンドは失敗する。`run`には、選択されたApplicationが厳密に一つ必要である。

選択した`.kimi`ファイルは**暗黙のプロジェクト**を構成する。名前はファイル名から`.kimi`を除いたもの、場所はそのファイルのディレクトリで、そのファイルだけをソースとする。ターゲットはホストターゲット（または`--Target`）で、それ以外は既定設定の`O2` Applicationとなる。依存先もロックもなく、プロジェクトファイルは書き出さない。各ビルド記録は、選択した`.kimiproj`または`.kimi`ファイルを入力として記録する。

```text
kimi build A.kimi            # implicit project; writes bin/T/A.O2.exe
kimi run A --no-build        # resolves A.kimiproj; Error: the record names A.kimi
```

<a id="s20-3-2"></a>
#### 20.3.2. ビルドと成果物

すべてのソースビルドは、選択したターゲットとDebug環境で、TestSourcesを除くProduct単位を検査する（§20.6.1）。

| OutputKind | ビルドの成功条件 | 成果物 |
| --- | --- | --- |
| Application | 必須検査と実行ファイル生成の成功 | 実行ファイルと成功したビルド記録 |
| Library | 必須の製品検査の成功 | 成功したビルド記録。実行ファイルは作らない |

Solutionやディレクトリのビルドは、Libraryも含め選択したすべてのProjectにこの規則を適用し、すべて成功した場合にのみ成功する。Libraryのビルドはネイティブツールを起動しない。`build --Manifest`が受け付けるのはApplicationマニフェストだけである。Libraryのソース検査結果を確立することはできない。Libraryにはソースビルド、IRには`emit`、配布パッケージには`pack`を使う。

`OutputPath`が`Name.ll`なら、`emit`は`Name.ll`と`Name.link.json`を書き出す。Applicationのビルドはさらに`Name.O0.exe`または`Name.O2.exe`を書き出し、すべてのビルドは`Name.link.build.json`を書き出す。Libraryのビルドは記録だけを書き出す。`emit`で要求したLibraryのIRは調査専用である。成功したLibraryの記録は、入力、選択ターゲット、Debug設定、言語とコンパイラーのバージョン、完了した製品検査を識別する。`.link.json`とビルド記録の形式は実装定義である。

- `emit`は両方の出力を公開してから成功を報告する。一部だけの公開は失敗となる。
- ビルドはまず、最新の成功したビルド記録を無効化する。必須検査と成果物が完了してから成功を公開する。Applicationは成功時にのみ実行ファイルを公開する。失敗や取消しの場合、その試行の成功記録は残さない。`run`は失敗したビルドが残した古い実行ファイルを実行しない。
- 以前の結果の再利用は[§A.2](#sa-2)に従う。
- Unsupportedの操作に対して仮のバイナリーを生成しない。

**ツールチェーン。** LLVMツールとバックエンドライブラリーは一つのディレクトリに置く。選択順は`--ToolchainRoot`、`KIMI_TOOLCHAIN_ROOT`、コンパイラーの隣の`toolchain`である。選択したルートがなければエラーとする。PATHは検索せず、何もダウンロードしない。`LlvmBin`（コマンドライン、次にプロジェクト）はLLVMツールのディレクトリだけを上書きする。相対ルートとコマンドラインの相対`LlvmBin`は起動ディレクトリからの相対パス、プロジェクトの相対`LlvmBin`はプロジェクト相対パスである。固定されたLLVMリリースに対してツールのバージョンとハッシュを検査するのは`toolchain verify`だけである。

<a id="s20-4"></a>
### 20.4. ネイティブライブラリー

`#LibraryImport`（[§22.6](#s22-6)）は、自身のKotonohaに属するネイティブ論理名を指定する。名前は大文字・小文字を区別して比較する。

- `NativeRequirements`は、各ターゲットを、`Kind`（`static`または`import`）と省略可能な`ContractId`・`Sha256`を持つ論理名に対応付ける。予約名以外の選択されたimportには、すべて対応項目が必要である。検査ではネイティブファイルを読まない。
- `NativeLibraries`は、各ターゲットを供給情報に対応付ける。供給情報は`Name`、`Input`、省略可能な`Package`（既定は宣言元Project）、`ContractId`、`Sha256`である。宣言元Project向けの供給情報には`Kind`を追加でき、その場合は要件も宣言する。供給情報は要求された`ContractId`と一致しなければならず、他のモジュールの要件を変えない。同じ名前・ターゲットに複数の供給情報がある場合、内容と`ContractId`が一致しなければならない。
- `Input`は`.lib`パスであり、絶対パスでなければ宣言元Projectのディレクトリからの相対パスとする。検索パスは使わない。各入力は読取り時にハッシュを計算し、ハッシュの表明を検査する。リンカーにはそのバイト列を渡す。
- `kernel32`と`kimi_backend`は予約された供給名であり、ツールチェーンのセットアップでインストールされたものを信頼する。項目の指定は不要で、`kernel32`の項目はエラーとなる。`kernel32`はKimiライブラリーとランタイムがimportするすべてのkernel32関数を提供する。正確なシンボル集合は実装定義である。プログラムがその他のkernel32関数をimportすると、ネイティブ解決時にエラーとなる。
- 供給する静的ライブラリーは、CRT起動処理、C/C++の自動動的初期化、TLS初期化・終了コールバック、自動`atexit`ハンドラーに依存してはならない。プログラムはそれらを提供しない。ゼロ初期化データと定数データは許可する。
- シンボル解決とリンカーディレクティブは実装定義である。ただし、曖昧・未対応のネイティブ依存先や、宣言された`Kind`と整合しない依存先はエラーであり、リンク順で解決してはならない。

```kimi
public group Observer
    #LibraryImport("observer", "observer_notify")
    public func notify(code: i32) -> i32
// NativeLibraries: observer={ Kind="static" Input="native/observer.lib" }
```

<a id="s20-5"></a>
### 20.5. パッケージとロック

ソースパッケージ（`.kimipkg`）、リリース表、ロックファイルの形式は実装定義である。各形式はスキーマバージョンを持ち、読取り側は未知のバージョン、キー、必須機能を拒否する。SourceIdはマニフェストの正確なバイト列のSHA-256である（形式バージョン1では`"Kimigayo.Source.v1"`、NUL、マニフェストの順にハッシュ化する）。そのため、同じSourceIdの保証は同じ形式バージョン内に限られる。

- **ロック。** ロックは製品区分とテスト区分を持つ（[§18.4.3](#s18-4-3)）。依存先がなくロックもない場合、解決結果は空である。破損したロックはエラーであり、空の解決結果として扱わない。
- **restore**は製品区分、次にテスト区分を解決し、両方をロックに保存する。製品側の失敗はrestoreを失敗させ、テスト区分を未解決のままにする。テスト専用の失敗は報告し、未解決として記録するが、restore自体は失敗させない。
- その他のコマンドは自身の区分を検証し（`test`は両方、それ以外は製品区分）、restoreが必要なら自動restoreせず失敗する。`--locked`は同じ検査を明示的に要求する。依存先ID、バージョン、参照名、辺、固定されたPackage内容が変わった場合はrestoreが必要であり、ソース編集だけなら不要である。
- **pack**は`build`が選ぶ環境でLibrary Projectを検証し（`--verify-all`では全ターゲット・全モード）、そのProjectとProject依存先をパッケージとして`bin/packages`または`--output`に書き出す。失敗後は何も書き出さない。
- **publish**は、パッケージとその依存先の閉包をローカルストアディレクトリに追加する。そこでIDとバージョンは一つのSourceIdに永久に対応付く。同じ対応付けの繰返しは成功する。競合が一つでもあれば公開全体を拒否する。
- **store verify**はユーザーキャッシュを再検査し、破損内容を無効化する。キャッシュの場所は実装定義である。

<a id="s20-6"></a>
### 20.6. テスト実行

<a id="s20-6-1"></a>
#### 20.6.1. 選択と準備

`test`は[§20.3.1](#s20-3-1)と同じ方法で入力を解決する。Solutionは列挙された各Projectを一度ずつ選択する。依存先のテストは、そのProject自体が選択された場合だけ実行する。テスト実行にはwindows-x64のホストとターゲットが必要である（[§20.2.1](#s20-2-1)）。

**テスト準備**は、一つのターゲットについて、Projectの製品ソースとテストソースをテスト依存先とともに検査する。テストの検出や実行は行わず、その結果はTestモードの検査結果となる。一覧表示やフィルターより前に、選択したすべてのProjectを準備する。そのためフィルターがエラーを隠すことはなく、読込み、検査、ビルドのいずれかが失敗すると、どのケースも実行しない。テスト（[§6.7.2](#s6-7-2)）はコンパイル時に検出する。

[§22.4](#s22-4)の起動規則は製品ビルドだけに適用する。テストビルドは、TestSourcesを含む全ソースの明示的な`main`関数とトップレベルの実行時本体項目を検査するが、実行しない。テスト実行ファイルでは、すべてのユーザー操作をそのケースの実行中に行うため、検証操作（[§17.6](#s17-6)）と`Kimi.Test.tempDirectory()`には常に実行中のケースがある。

```kimi
// Main.kimi, a product source
group Paths
    func scratch() -> string => Kimi.Test.tempDirectory() // Error: not a test-only body
```

<a id="s20-6-2"></a>
#### 20.6.2. オプション、設定、識別情報

- `--list`は検査してケースを列挙する。`--filter TEXT`は、完全修飾名に`TEXT`を含むケースを選ぶ（大文字・小文字を区別）。`--case ID`は一つのCaseIdを選ぶ。未知のIDはエラーとなる。`--allow-empty`は空の選択を許可する。
- `--jobs N`は、全Projectを通じた実行中ケース数を制限する。`--no-parallel`は1を意味する。`--timeout DURATION`は各ケースの期限を設定する。値は`ms`、`s`、`m`を伴う正の整数で、最大24時間とする。
- `--Format json`は、バージョン付きの結果文書を一つ標準出力に書く。
- 未知、重複、無効、競合するオプション（`--case`と`--filter`、`--jobs`と`--no-parallel`）はエラーとし、値を範囲内へ丸めない。
- Projectの`Test`設定は、オプションで上書きできる`Timeout`（既定値`30s`）と、変数マップ`Environment`を持つ。`TMP`、`TEMP`、`KIMI_TEST_*`は予約済みである。
- `--jobs`の既定値、結果の保存場所と保持期間、回復の上限、結果スキーマは実装定義である。

テストの**完全修飾名**は、プロジェクトルートから宣言元コンテナーまでのパスと名前を`.`で結んだもの（`MeasureTests.doubled`）である。**プロジェクト識別情報**は`TestProjectId`、なければ`PackageId`、それもなければ拡張子付きのプロジェクトファイル名とする。暗黙のプロジェクトはソースファイル名（`A.kimi`）を使う。選択した二つのProjectの識別情報が同じならエラーとなる。TestIdとCaseIdの形式は実装定義であり、プロジェクト識別情報、完全修飾名、論理ソースパスが変わらない限り安定し、絶対パスやSolution内の位置には依存しない。

<a id="s20-6-3"></a>
#### 20.6.3. ケースの実行

選択した各ケースは、そのProjectのテスト実行ファイルの専用プロセスで一度だけ実行し、再試行しない。ケース順は未規定である。

- 作業ディレクトリはProjectディレクトリとし、標準入力は閉じる。各ケースに同期の標準出力・標準エラーハンドルを与え、別々に記録する。
- 環境に`Test.Environment`を追加し、`TMP`と`TEMP`をそのケース用に新設した一時ディレクトリに設定する。`Kimi.Test.tempDirectory()`はその絶対パスを返す（[§17.6.1](#s17-6-1)）。どの結果になっても、その後にディレクトリを削除する。
- 期限は起動から終了までを計る。どの結果になっても、その後にランナーはケースの残存プロセスを停止する。
- Abortは自身のケースだけを終了する（[§17.6.2](#s17-6-2)）。Abortしたケースは一度だけ報告する。falseとなった`$require`によるAbortならfailed、それ以外はabortedとする。その後の別スレッドからの出力は結果を変えない。
- `test`とケースプロセス間のプロトコルは実装定義である。ケースが成功するのは、プロセスが正常完了し、失敗を報告しなかった場合だけである。報告の欠落や不正はランナーの失敗となる。

<a id="s20-6-4"></a>
#### 20.6.4. 結果と終了コード

各ケースは、passed、failed（falseの`$expect`または`$require`。falseの`$require`によるAbortも含む）、aborted（Abortまたはその他の異常プロセス終了）、timed out、cancelled（未開始ケースも含む）、runner failureのいずれかとして報告する。一覧と結果は、完全修飾名、論理ソースパス、プロジェクト識別情報の順でソートする。

終了コードは130、3、2、1の順で最初に該当するものを選び、どれにも該当しなければ0とする。

- 0：一覧表示が成功した、選択した全ケースが成功した、または許可された空の選択。
- 1：ケースがfailed、aborted、timed outとなった。
- 2：無効なオプション・設定・選択（`--allow-empty`なしの空選択、未対応のホストやターゲット）、または読込み・検査・ビルドの失敗。
- 3：起動、通信、回復、結果保存におけるランナーの失敗。
- 130：取消し。

---

<a id="s21"></a>
## 21. レイアウト、ランタイム、コード生成

[実装仕様目次](#implementation-specification)

この章は、windows-x64ターゲットプロファイルを定める。対象は、プログラムが依存できるレイアウトと外部関数の保証、コード生成方針、観測可能なランタイム動作である。生成コードのそれ以外の事項は実装定義である。

<a id="s21-1"></a>
### 21.1. 記憶レイアウト

<a id="s21-1-1"></a>
#### 21.1.1. 共通規則

格納可能な型`T`について、`size(T)`はパディングを含めてインラインに確保するバイト数、`alignment(T)`は正の2の累乗、`stride(T)`はサイズをアラインメントの倍数へ切り上げた値である。これらは記法であり、ソースの演算子ではない。windows-x64では、サイズ、ストライド、フィールドオフセットは2^63 − 1を超えてはならない。それを超えるレイアウトは、そのレイアウトを必要とする使用位置でLanguageエラーとなる（[§8.15.3](#s8-15-3)）。

- すべての構成要素は自然アラインメントを満たす。集成体のアラインメントは、少なくとも構成要素の最大値とする。正のサイズを持つ構成要素は重ならず、入れ子の構成要素のパディングは再利用しない。
- パディングの内容は定義しない。バイト転送でコピーしてもよいが、型付きの読取りや比較では値として扱わない。
- サイズ0の値も、評価、初期化、所有権、Loan、破棄を保つ。異なるサイズ0のPlaceが同じアドレスを共有する場合がある。生のPlaceはアドレスではなくPlaceを指す（[§5.2](#s5-2)）。
- 物理レイアウトによって、フィールドの同一性、初期化順、部分Move、Loan、破棄順は変わらない。
- コンパイラー、ターゲット、設定、選択ソースが同じなら、同じレイアウトを生成する。

structは自身のフィールドと直接のインライン基底を格納する。計算メンバーは記憶領域を追加しない。借用、生ポインター、オブジェクトハンドルは参照先を内部に埋め込まない。インライン記憶領域の循環は、その宣言位置でLanguageエラーとなる（[§6.2](#s6-2)）。

<a id="s21-1-2"></a>
#### 21.1.2. レイアウトモード

`#Layout`の受理条件は[§6.7](#s6-7)で定める。

**Kimigayoモード。** 物理的なフィールド順、パディング、アラインメントによる並替えは、共通規則の範囲内で実装定義である。タプル、enumのCaseペイロード、クロージャーのキャプチャーも同じ方法で配置する。オフセット、C互換性、コンパイラーバージョンをまたぐABIの安定性は保証しない。

**Cモード**は、パッキング16（`/Zp16`）のWindows x64 MSVCレイアウトに従い、`pragma pack`、明示的アラインメント、ビットフィールドは使わない。フィールドを記述順に置き、それぞれを`min(alignment(F), 16)`に揃う次のオフセットに配置する。structのアラインメントはフィールドの最大アラインメント、サイズとストライドは末尾オフセットをそのアラインメントの倍数へ切り上げた値となる。入れ子の型は自身のモードを保つ。Cレイアウトはstruct自身の記憶領域を表し、オブジェクトハンドルやそのヘッダーは表さない。

**Cモードの制限。** [§6.7.1](#s6-7-1)の制限を適用する。

```kimi
#Layout("C")
struct NativeRecord
    var kind: u8       // offset 0
    var value: u64     // offset 8
    var flags: u8      // offset 16; size and stride 24, alignment 8

#Layout("C")
struct Tagged
    var tag: u8
    var nothing: ()

func keep(value: Tagged) -> Tagged => value@move   // Error: Tagged's C layout has a zero-sized Field.
```

`NativeRecord`に対応するC宣言は`struct { uint8_t kind; uint64_t value; uint8_t flags; }`である。Cモードだけでは、ネイティブexport、集成体の引数渡し、直列化は定義しない。

<a id="s21-1-3"></a>
#### 21.1.3. スカラー、借用、ハンドル

windows-x64はリトルエンディアンで、ポインターは64ビットである。組込み条件`pointerWidth`（[§19.2](#s19-2)）は64となる。

| 型 | サイズ／アラインメント／ストライド（バイト） |
| --- | --- |
| `i8` / `u8`、`bool` | 1 / 1 / 1 |
| `i16` / `u16` | 2 / 2 / 2 |
| `i32` / `u32`、`f32`、`char` | 4 / 4 / 4 |
| `i64` / `u64`、`isize` / `usize`、`f64` | 8 / 8 / 8 |
| `i128` / `u128` | 16 / 16 / 16 |
| `raw/T`、`ref/T`、`uniq/T`、オブジェクトハンドル、`Weak<S>` | 8 / 8 / 8 |
| Unit、`Loan<T>`、Never | 0 / 1 / 0 |

- `char`はUnicodeスカラー値を符号なし32ビット整数として格納する。`char`も`bool`も、Cの`char`、`WCHAR`、Windowsの`BOOL`には対応しない。
- Neverをインラインに格納する型は、Neverをサイズ0として配置する。そのため、Cモードのサイズ0フィールドに対する制限が適用される。
- `ref/V`と`uniq/V`は、`V`の直接の記憶領域を指す一つの非nullポインターであり、長さ、個数、Loanデータを持たない。サイズ0の`V`の借用は、`V`に必要なアラインメントを満たす非nullアドレスを保持する。
- **非null Option表現。** `R`が`ref/T`、`uniq/T`、`obj/T`、`rc/T`、`arc/T`、`objref/T`、`objuniq/T`のいずれかである`Option<R>`は、ポインターサイズの一ワードで表す。nullは`None`、`R`のすべての有効値は`Some`となる。この規則は`Option<Option<R>>`、参照を含むstructやタプル、他のenumには拡張しない。
- 共通関数値は16バイトで、アラインメントは8である。サイズが最大8バイト、アラインメントが最大8の環境はインラインに格納する。それより大きい環境は、値を形成するときにメモリーを割り当てる（[§7.7.4](#s7-7-4)）。
- 非null Option以外のenumのレイアウト、文字列表現、オブジェクトヘッダー、参照カウントの記憶領域、弱参照の補助表、参照カウントの最大値、すべてのランタイムメタデータ（型記述子、値メタデータ、仮想ディスパッチ表）は、実装定義でモジュール内に閉じ、外部ABIではない。カウント最大値でのAbortを含む参照カウント付きオブジェクトのライフサイクルは、[§13.6.2](#s13-6-2)と[§13.6.3](#s13-6-3)で定める。

```kimi
let some: Option<ref/i32> = .None   // One 8-byte word holding null.
let pair: (u8, u64) = (1, 2)        // Layout implementation-defined.
```

<a id="s21-2"></a>
### 21.2. 外部関数ABI

`#LibraryImport`のシグネチャで許される型とそのCでの意味は、[§22.6](#s22-6)で定める。windows-x64のimportはWindows x64 C呼出し規約（LLVMの`ccc`）を使い、`signext`、`zeroext`、`inreg`、`byval`、`sret`は付けない。

- `i8`と`i16`は自身の幅で渡し、可変長引数の整数昇格によって幅を広げない。未使用のレジスター上位ビットに意味はない。
- `ref/T`、`uniq/T`、`raw/T`は参照先のアドレスを渡す。`Option<ref/T>`と`Option<uniq/T>`は非null Option表現を使い（[§21.1.3](#s21-1-3)）、`None`はnullポインターとなる。
- Unitの戻り値はCの`void`となる。
- `dllimport`とネイティブ供給はライブラリー種別に従う（[§20.4](#s20-4)）。

外部コードへのすべての呼出し時と、外部コードからのすべてのreturn時に、浮動小数点制御状態はWindows x64の既定状態とする。丸めは最近接偶数、非正規化数はフラッシュしない（DAZとFTZはオフ）、浮動小数点例外はマスクする。これを変更した外部コードはreturn前に復元しなければならない。ランタイムは修復しない。Kimigayoコードが残すステータスフラグは未規定である。生成コードに例外ハンドラーはない（巻戻しをしない保証については[§22.6](#s22-6)を参照）。

`_fltused`、`__chkstk`、`memcmp`、`memcpy`、`memmove`、`memset`と、`__kimi_`または`llvm.`で始まる名前は予約済みである。windows-x64向けビルドでは、予約名の`#LibraryImport`はネイティブ解決時にエラーとなる。同じ外部シンボルに対するimport間で、シグネチャや供給ライブラリーが異なる場合もエラーとなる（[§20.4](#s20-4)）。

```kimi
group Native
    #LibraryImport("observer", "inspect")
    public func inspect(value: Option<ref/i64>, mode: i16) -> i32
// C: int32_t inspect(const int64_t *value, int16_t mode);
// Native.inspect(.None, 1) passes NULL.
```

<a id="s21-3"></a>
### 21.3. コード生成

<a id="s21-3-1"></a>
#### 21.3.1. 生成方針

この実装は単相化を行う。明示的特殊化の選択後（[§8.13](#s8-13)）、出力に必要な閉じたインスタンス化ごとに別の本体を生成する。コード生成の有無によらず、検査は[§8.15](#s8-15)の検査対象集合全体を対象とする。

内部の関数ABI（パラメーター渡し、隠れた引数、結果の記憶領域、Kimigayo関数間の呼出し規約）、シンボル名、最適化属性は実装定義であり、ビルド間で変わり得る。独立して生成したモジュール間にバイナリー互換性はない。`public`宣言はネイティブexportではない。最適化レベルO0とO2によって、受理判定、結果、効果、Abortコードと位置、後処理、非停止性は変わらない。

```kimi
func identity<T>(value: T) -> T => value@move

let a = identity(1)       // Generates identity<i32>.
let b = identity("x")     // Generates a separate body, identity<string>.
```

<a id="s21-3-2"></a>
#### 21.3.2. 上限

解析と生成の上限（インスタンス化の増加、コンテキストの深さ、既定値の展開数、解析用記憶領域、文字列補間の入れ子）は実装定義である。上限を超えると、該当する宣言、呼出し、トークンでResource診断を報告する（[§23.1.1](#s23-1-1)）。プログラムを意味上無効にはせず、独立した検査も止めない。その他のレイアウト失敗は[§8.15](#s8-15)と[§23.1.1](#s23-1-1)に従って分類する。インライン記憶領域の循環は生成上限ではない（[§21.1.1](#s21-1-1)）。

<a id="s21-3-3"></a>
#### 21.3.3. 検査付き操作

算術、変換、シフト、添字アクセス、範囲の検査は、言語規則に従ってAbortする（[§13.3.1](#s13-3-1)、[§13.5.4](#s13-5-4)、[§17.4.1](#s17-4-1)）。コードは[§22.5.2](#s22-5-2)を使う。検査を省けるのは成功が証明された場合だけである。失敗が証明された検査はAbortに置き換える。

**128ビットの対応範囲。** `i128`/`u128`の格納、比較、ビット操作、シフト、整数変換、検査付き加算・減算・符号反転・インクリメント・デクリメント・乗算に対応する。除算、剰余、`f32`/`f64`との相互変換は、複合代入も含めUnsupportedである（[§23.1.1](#s23-1-1)）。実行されない分岐も含め、検査対象集合の各インスタンス化の具体的な使用位置で報告する（[§8.15](#s8-15)）。これらの操作でも、必須のコンパイル時評価（[§17.4.4](#s17-4-4)）と直接のリテラル変換（[§13.5.4](#s13-5-4)）には対応する。

```kimi
func half(value: i128) -> i128 => value >> 1    // Supported.
func third(value: i128) -> i128 => value / 3    // Unsupported on windows-x64.
```

浮動小数点操作はIEEEの意味に従い、fast-math、再結合、融合は行わない。スタックの枯渇はスタックプローブで検出する（[§17.4.3](#s17-4-3)）。

<a id="s21-4"></a>
### 21.4. Windows x64ランタイム

<a id="s21-4-1"></a>
#### 21.4.1. ターゲット

windows-x64プロファイルのターゲットは`x86_64-pc-windows-msvc`であり、基本のx86-64 CPUとSSE2を使う。それ以外のCPU機能は仮定しない。生成関数は、OSによるスタック走査のためにWindows x64の巻戻し情報を持つ。これは言語の例外や後処理を実装するものではない。ツールチェーンは[§20.3.2](#s20-3-2)の方法で解決する。

<a id="s21-4-2"></a>
#### 21.4.2. プロセスの入口と終了

Applicationは外部入口`__kimi_start`を定義し（`/entry:__kimi_start`でリンク）、Cランタイムの起動処理を使わない。ランタイムを初期化し、プログラム本体を実行し（[§22.4](#s22-4)）、終了処理を完了してから`ExitProcess(0)`を呼ぶ。Libraryは入口を定義しない。

Abort（[§17.4.3](#s17-4-3)）は、標準エラー出力に一つの報告を書いた後、`ExitProcess(1)`を呼ぶ。

```text
path:line:column: abort CODE: Reason
```

接頭辞`path:line:column: abort CODE: `、変更しないReasonのバイト列、最後のLFを書き出す。メッセージは改行やNULを含んでいてもよく、エスケープも削除もしない。OSがエラーコードを供給した場合は、Reasonの後、最後のLFの前に` (win32=N)`を付ける。

- `line:column`は[§2.1](#s2-1)に従う。コードと理由は[§22.5.2](#s22-5-2)、位置は[§22.5.1](#s22-5-1)に従う。
- 論理ソースパスは、非ASCII文字と制御文字を`\u{HEX}`、バックスラッシュを`\\`にエスケープして表示する。
- 標準エラー出力への書込みに失敗した場合、報告は途中で切れるが、プロセスは引き続きコード1で終了する。

```kimi
group Counter
    public func next(value: i32) -> i32 => value + 1

let n = Counter.next(2147483647)
// Main.kimi:2:44: abort KIMI_E_INT_OVERFLOW: Integer overflow
```

<a id="s21-4-3"></a>
#### 21.4.3. メモリー

メモリー割当てにはプロセスヒープ（`HeapAlloc`、`HeapFree`）を使う。サイズが0でないすべての`Raw.allocate`の結果を含め、すべてのヒープ割当ては16バイト境界に揃える。`Raw.allocate`が対応する最大アラインメントは16である（[§5.6](#s5-6)）。最大オブジェクトサイズは2^63 − 1バイトである。0バイトの`Raw.allocate`はメモリーを割り当てない。割当て、解放、サイズの失敗は、[§22.5.2](#s22-5-2)のコードでAbortする。

```kimi
let p = Raw.allocate<u64>(4)    // 32 bytes, 16-byte aligned on windows-x64.
unsafe => Raw.release(p)
```

<a id="s21-4-4"></a>
#### 21.4.4. コンソール出力

`Console.writeLine`（[§22.7](#s22-7)）は、標準出力ハンドル（`GetStdHandle`）に`WriteFile`で書き込み、NUL終端は付けない。リダイレクト先のファイルやパイプには変更しないバイト列を渡す。コンソールでの非ASCIIテキストの表示は設定に依存する。オーバーラップI/O用に開いた標準ハンドルへの書込み結果は実装定義である（テストランナーは同期ハンドルを使う。[§20.6.3](#s20-6-3)）。

```kimi
Console.writeLine("ありがとう")   // A pipe receives 15 UTF-8 bytes and LF.
```

<a id="s21-4-5"></a>
#### 21.4.5. 経過時間とタイマー

`Time.Stopwatch`（[§22.8.2](#s22-8-2)）とタスクタイマーは、`Kimi.Windows`のimport（[§22.8.3](#s22-8-3)）を通じて`QueryPerformanceCounter`を使う。カウンター周波数は、初回使用時に初期化するKimi内部の静的`let`にキャッシュする。windows-x64では、`QueryPerformanceCounter`または`QueryPerformanceFrequency`呼出しの失敗、または負のカウンター値は、時計の失敗となる（`KIMI_E_CLOCK`、[§22.5.2](#s22-5-2)）。

<a id="s21-4-6"></a>
#### 21.4.6. タスク

タスクのコンパイル方法とスケジューリング方法は、[§22.3](#s22-3)と[§24](#s24)の保証の範囲内で実装定義である。この実装はスレッドごとに一つの実行器を持ち、`run`呼出しをまたいで保持する。実行器には一つのI/O完了ポートがあり、実行可能なタスクをFIFO順で再開する（再開順は未規定。[§22.3.2](#s22-3-2)）。

I/O待機は、完了ポートを使って[§22.3](#s22-3)の確定規則を実現する。可能ならハンドルを`FILE_SKIP_COMPLETION_PORT_ON_SUCCESS`付きで関連付ける。そのハンドルで直ちに完了する操作は呼出し時に確定し、それ以外は完了パケットが待機を確定する。取消し要求は`CancelIoEx`を呼ぶ。待機はパケットが届くまで登録を保持し、パケットが`ERROR_OPERATION_ABORTED`を報告した場合に限り取消しに確定する。

定常状態では、タスク呼出し、待機、タイマー、チャネル操作、`shield`、繰返しの`run`呼出しはヒープメモリーを割り当てない。開始した子はプールされたレコードを使う。

<a id="s21-4-7"></a>
#### 21.4.7. ネイティブimportとバックエンド支援

ランタイムは`GetProcessHeap`、`HeapAlloc`、`HeapFree`、`GetStdHandle`、`WriteFile`、`GetLastError`、`ExitProcess`などの`kernel32`関数をimportする。予約された`kernel32`供給は[§20.4](#s20-4)で定める。

このプロファイルのバックエンド支援アーカイブは`memcmp`、`memcpy`、`memmove`、`memset`、`__chkstk`を供給し、すべてのモジュールは`_fltused`を定義する。ツールチェーンの採用時には、生成コードが未供給のバックエンド補助関数を必要としないことを検証する。補助関数が欠けていれば、個々のプログラムのリンク時ではなく採用時に失敗させる。

---

<a id="s22"></a>
## 22. Kimi、プログラム実行、外部関数



この版は、実行可能なターゲットプロファイルとしてwindows-x64を一つ定める（[§21.4](#s21-4)）。プログラムの有効性は、条件（[§19.2](#s19-2)）と具体的レイアウトのサイズ上限（[§8.15.3](#s8-15-3)）を除いてターゲットに依存しない。Unsupportedの報告はターゲットに依存してよい。他のターゲット向けのビルド、実行、テストはUnsupportedである。

<a id="s22-1"></a>
### 22.1. 必須のKimi宣言

すべてのコンパイルは、予約された直接参照名`Kimi`の下に、厳密に一つの**Kimi Kotonoha**を束縛する（[§18.4.1](#s18-4-1)）。`Kimi`はキーワードではなく、内側のスコープで隠せる。`::Kimi`は隠せない。必須の既定の別名が`Kimi`を開く（[§18.2.3](#s18-2-3)）。

Kimi宣言は宣言の同一性で識別し、名前やユーザー定義の適合では識別しない。同じ綴りのユーザー宣言や置換した別名が、その特別な動作を得ることはない。Kimi Kotonohaが以下の全行を指定の形状で宣言すれば互換性がある。追加の宣言があってもよい。コンパイラーが自身に付属するもの以外のKimi Kotonohaを受け付けるかは実装定義である。`Kimi.Windows`と`Kimi.Time`を含むすべてのKimi宣言は、すべてのターゲットに存在する。

次の表だけが必須Kimi宣言の一覧である（名前は`Kimi`からの相対名。「反復入口」は、[§14.6.2](#s14-6-2)の項目を持つ`Iterable`、`UniqIterable`、`IntoIterable`をいう）。

| 宣言 | 必須の形状 |
| --- | --- |
| `Never` | public、非open、非ジェネリックのstruct。Origin、基底、メンバーを持たない。[§3.1.3](#s3-1-3)の、値を持たないコンパイラー組込みの値型 |
| `Option<T>` | `Some(T)`、`None`の順のenum。`T`がCopyならCopy |
| `Result<T, E>` | `Ok(T)`、`Err(E)`の順のenum。`T`と`E`がCopyならCopy |
| `Equatable` | `func equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `func compare(self: ref/Self, other: ref/Self) -> i32`。小さい・等しい・大きい場合に負・0・正 |
| `Copy`、`Owned`、`Callable`、`Sealed`、`ObjectPayload`、`PrimitiveInteger`、`Transferable` | コンパイラー組込みContract（[§8.7](#s8-7)）。`Transferable`はpublicで、パラメーター、親、メンバーを持たない |
| 算術Contract | [§13.3.3](#s13-3-3)の11個のContract |
| `Position: Equatable, Utf8Format` | 閉じたContract。`Self is Copy`、`Self is Owned`、`func tryResolve(self: Self, length: isize) -> Option<isize>`。適合型は12個の整数型、`FromEnd<T>`、`Start`、`End`だけ（[§4.6.1](#s4-6-1)） |
| `PositionRange: Equatable, Utf8Format` | 閉じたContract。`Self is Copy`、`Self is Owned`、`func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>`。適合型は`Range`、`ClosedRange`、`ResolvedRange`だけ（[§4.6.3](#s4-6-3)） |
| `FromEnd<T>`、`Start`、`End` | Copy、Owned、Equatableの位置。`^x`と省略された範囲境界だけで作る。`FromEnd<T>`は`T is PrimitiveInteger`を要求し、`offset: T`だけを格納する（[§4.6.1](#s4-6-1)） |
| `Range<S, E>`、`ClosedRange<S, E>` | `S, E is Position`に対するCopy、Owned、Equatableの区間。`start: S`、`end: E`を格納し、構文だけで作る。`S is PrimitiveInteger`かつ`E is S`なら反復入口を持つ（[§4.6.2](#s4-6-2)、[§4.6.3](#s4-6-3)） |
| `ResolvedRange` | Copy、Owned、Equatableの検証済み区間。`length`、`isEmpty`、`resolve`と反復入口を持つ（[§4.6.2](#s4-6-2)） |
| `Wrapping<T>` | フィールドを持たないstruct。`T is PrimitiveInteger`で、メンバーはない。`T`として表現し、ラップ算術、[§13.5.4](#s13-5-4)の変換、`T`および[§13.3.3](#s13-3-3)の適合を持つ。`PrimitiveInteger`でも`Position`でもない（[§3.1.1](#s3-1-1)） |
| `Array<T>` | 非Copyの所有シーケンス。`capacity`と[§4.7.3](#s4-7-3)のメタデータ、`UniqIndexable<isize>`、[§4.8.2](#s4-8-2)・[§4.8.4](#s4-8-4)・[§4.8.5](#s4-8-5)・[§4.9](#s4-9)の操作、反復入口 |
| `Dictionary<K, V>` | 非Copyの所有コレクション。`K is Equatable`。`length`、`capacity`、既存キーに対する`UniqIndexable<K>`、[§4.9](#s4-9)の操作、反復入口 |
| `Slice<T> {source}` | `ref/T during source`に対するCopyの共有ビュー。[§4.7.3](#s4-7-3)のメタデータ、[§4.8.2](#s4-8-2)の操作、`Indexable<isize>`、反復入口 |
| `UniqSlice<T> {source}` | 非Copyの排他ビュー。`length`、`isEmpty`、`indices`、`UniqIndexable<isize>`、[§4.8.4](#s4-8-4)のビュー |
| `Loan<T>` | 外側のSemanticsが`ref`、`uniq`、`objref`、`objuniq`の借用型`T`に対するサイズ0のstruct。`init(value: T)`。`T`がCopyの場合に限りCopy（[§15.3.5](#s15-3-5)） |
| `Weak<s/T>` | 非Copyのstruct。`s is rc or arc`（[§13.6.3](#s13-6-3)） |
| `LendingIterator`、`Iterator`、`Iterable`、`UniqIterable`、`IntoIterable` | [§22.2.1](#s22-2-1) |
| `Indexable<Key>`、`UniqIndexable<Key>: Indexable<Key>` | `associate Element`、`index(self: ref/Self, key: ref/Key) -> ref/Element during self`、`indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self`。`receiver[key]`は返された参照の参照先を指す（[§4.7.1](#s4-7-1)） |
| 標準イテレーター | `ArrayIterator<T>`、`ArrayUniqIterator<T>`、`ArrayOwningIterator<T>`、`FixedArrayOwningIterator<T, A>`、`SliceIterator<T>`、`DictionaryIterator<K, V>`、`DictionaryUniqIterator<K, V>`、`DictionaryOwningIterator<K, V>`、`RangeIterator<T>`、`ClosedRangeIterator<T>`（[§22.2.2](#s22-2-2)） |
| `Iteration` group | `OwningIterator<I>`、`BorrowingIterator<I>`、`owning`、`borrowing`（[§22.2.2](#s22-2-2)） |
| `Intrinsics` group | 非ジェネリック。`makeObj`、`makeRc`、`makeArc`、`makeRcCyclic`、`makeArcCyclic`、`clone`、`downgrade`、`upgrade`（[§13.6.2](#s13-6-2)、[§13.6.3](#s13-6-3)）、`replace`、`exchange`、`swap`（[§15.7](#s15-7)） |
| `Raw` group | `allocate`、`release`、`initialize`、`slice`（[§5.6](#s5-6)） |
| `Async` group | `Cancelled`、`SendFailure`、`Raced`（引数数2～4）、`Sender<T>`、`Receiver<T>`、`run`、`sleep`、`checkpoint`、`shield`、`join`、`joinOk`、`race`、`each`、`eachReceived`、`pipe`、`offload`、`offloadEach`（[§22.3](#s22-3)） |
| `Console` group | `writeLine`（[§22.7](#s22-7)） |
| `Time` group | `Duration`、`Stopwatch`（[§22.8](#s22-8)） |
| `Windows` group | `queryPerformanceCounter`、`queryPerformanceFrequency`（[§22.8.3](#s22-8-3)） |
| UTF-8書式化 | `Utf8Format`、`BufferWriter`、`WriteWindow`、`Utf8Writer`、`BufferFull`、`Text` group（[§22.9](#s22-9)） |
| `Test` group | `tempDirectory() -> string`。テスト専用本体だけで使用可能（[§17.6.1](#s17-6-1)、[§20.6.3](#s20-6-3)） |

反復と添字アクセスのContractは静的である。その関連型は完全な型であり、Originパラメーターを取れる（[§8.5.4](#s8-5-4)）。`Array`、`Dictionary`、`Slice`、`UniqSlice`、`Wrapping`、`Loan`、`Weak`、`Never`が持つコンストラクターは、`Array<T>.init(capacity:)`などのKimi宣言に明記されたものだけである（`Weak`、`Wrapping`、`Never`にはない）。合成はしない（[§6.3.6](#s6-3-6)）。

すべての固定配列`[N of T]`は、長さパラメーターを含むすべての要素型と長さについて、組込みの公開メンバー（[§4.7.3](#s4-7-3)のメタデータ、[§4.8.2](#s4-8-2)の読取り操作、[§4.8.4](#s4-8-4)と[§4.8.5](#s4-8-5)のビュー）と、`UniqIndexable<isize>`および反復入口への組込み適合を持つ。これらはソースでは宣言できない。

Arrayは`T`がOwnedならOwnedとなり、Dictionaryは`K`と`V`がOwnedならOwnedとなる。キーワード`string`は組込みUTF-8文字列型を表す。リテラル、補間、比較、`Utf8Format`があり、連結演算子はない（[§13.3.2](#s13-3-2)）。

**配置。** `Kimi`を開くとそのgroupは見えるが、groupのメンバーは見えない（[§18.2.3](#s18-2-3)）。`alias Memory => Kimi.Intrinsics`などの別名は、宣言の同一性を保つ。`Kimi.replace`や`Kimi.writeLine`のようなルートレベルの名前はない。`$abort`や`$tryWrite`などの組込み操作は、どのgroupにも属さない。

```kimi
let watch = Time.Stopwatch.init()      // Time is visible through the default alias.
Console.writeLine("ready")
// writeLine("ready")                 // Error: Console's members are not opened.
var slot: i32 = 1
let old = Intrinsics.exchange(slot@uniq, with: 2)
// let bad = Kimi.exchange(slot@uniq, with: 3)   // Error: no root-level name.
```

<a id="s22-2"></a>
### 22.2. 反復

<a id="s22-2-1"></a>
#### 22.2.1. Iterator Contract

```kimi
contract LendingIterator
    associate LentItem(step) for uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item
    effect LendingIterator.next preserves results

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

どの列挙も`LendingIterator.next`を呼び、次の項目を値として受け取る。各呼出しは`step`をその呼出しのレシーバ借用に束縛する。`None`はLoanを持たない。LendingIteratorは`None`の後に`Some`を返してもよい。`for`は最初の`None`で停止する。

- **Iterator**は、`next`のOriginを一つも名指さない完全な`Item`型を一つ持つ。適合型は`associate Iterator.Item is T`と`func next(self: uniq/Self) -> Option<T>`を書く。
- IteratorではないLendingIteratorは、自身を借用する項目を貸し出せる。`ref/E during step`のような項目は、保持されている間、次の`next`と競合する。`step`を持たない項目は通常のLoan規則に従う（[§15.6.3](#s15-6-3)）。ただし、ジェネリックコードが次の`next`をまたいで保持できるのは、`preserves results`境界がある場合だけである。

三つの入口は独立している。`for`にはSubjectモードが選ぶ入口が必要であり、その項目は選択した`IteratorType`の`LentItem(step)`となる（[§14.6.2](#s14-6-2)）。借用の入口は呼出しごとの`source`を保ち、所有の入口は`Self`内の依存を保つ。一つの型が`Iterator`と入口の両方に適合してもよい。メンバー名による適合、参照やIteratorの自動適合、既定の本体はない。共有借用を通じてIteratorを進める標準適合はない。

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

<a id="s22-2-2"></a>
#### 22.2.2. 標準アダプターとイテレーター

`Kimi.Iteration`はLendingIterator `I`に対する二つのアダプターを宣言する。各結果は`LendingIterator`、`IntoIterable`（自身）、`UniqIterable`（`BorrowingIterator<Self>`）に適合する。また、`I`が`Iterator`に適合する場合に限り、同じ`Item`、依存、効果境界を持って`Iterator`に適合する。アダプターは入力の終端保証を保ち、メモリーを割り当てず、項目を先取りしない。

- `owning(iterator) -> OwningIterator<I>`は`I`を値で受け取り（非CopyのPlaceには`@move`を書く）、その`LentItem(step)`と依存を転送する。
- `borrowing(iterator) -> BorrowingIterator<I>`は`uniq/I`を受け取り、Moveせずに進める。`source`は入力の外側のOriginである。項目は`I`の契約を保ち、各項目の`step`は`next`の実際の再借用となる。

**標準イテレーター。** 各標準入口の`IteratorType`は、[§22.1](#s22-1)の10個の標準イテレーター型のいずれかである。それぞれは非CopyのIteratorで、`Item`は[§14.6.2](#s14-6-2)の項目となる。`RangeIterator<T>`と`ClosedRangeIterator<T>`は`T is PrimitiveInteger`を要求し、Ownedである。公開コンストラクターや入口への適合はない。イテレーター値は`Iteration.owning`または`Iteration.borrowing`を通じて列挙する。標準イテレーターは、最初の`None`の後は`None`を返し続け、各要素を[§14.6.2](#s14-6-2)の順序で厳密に一度ずつ渡し、生存中の排他的な項目が重なることはなく、`next`でユーザーコードを呼ばない。所有する場合は未返却要素を所有し、[§4.9.7](#s4-9-7)の順序で破棄する。借用する場合は何も破棄しない。各ステップで[§4.8.6](#s4-8-6)の上限を満たす。

```kimi
let values: Array<i32> = [1, 2]
var it = values.iterate()
for x in Kimi.Iteration.borrowing(it@uniq) => use(x)
// for x in values.iterate() => use(x)   // Error: an iterator has no entry conformance.
```

<a id="s22-2-3"></a>
#### 22.2.3. Iteratorの独立性

Iteratorの項目は、公開結果の独立性を満たす（[§15.6.3](#s15-6-3)）。項目は、その呼出しのレシーバLoanにもIteratorの記憶領域にも依存しない。排他的な項目はIteratorの権限から分割した子か、外へ転送する値である。そのため、IteratorのMoveや解放で返却済み項目は無効にならず、借用を通じて到達したIteratorの項目は、その借用のLoanを保持しない。

`preserves results`（[§8.11.3](#s8-11-3)）は、`next`の効果が、同じIteratorが以前に返した項目の保持するLoanと競合しないことを意味する。その項目の転送や再借用を通じたLoanも含む。IteratorのMoveはこの境界を引き継ぐ。`Self is Iterator`を宣言するユーザー型を含むすべての実装を、[§8.11](#s8-11)に従って検証する。Iterator全体の破棄は別の効果であり、保持中の項目と競合する破棄は拒否する。未知の効果とパラメーター依存効果は、[§15.6.4](#s15-6-4)、[§16.3.3](#s16-3-3)、[§4.9.6](#s4-9-6)に従う。結果のために自身を借用するラッパーや、`next`に競合する効果を追加するラッパーは、LendingIteratorにしかならない。

```kimi
func nextPair<I>(iterator: uniq/I) -> (Option<I.Item>, Option<I.Item>)
    I is Iterator
    let first = iterator.next()
    let second = iterator.next()   // preserves results permits retaining first.
    return (first@move, second@move)
```

<a id="s22-3"></a>
### 22.3. 非同期タスク

`Kimi.Async`は、[§24](#s24)の非同期タスクを提供する。タスクにハンドルはない。保留中の計算、実行中の子、一時停止中のタスクフレームを表す値はない。タスクの実装方法（タスクフレーム、待機者、実行器、そのスレッドごとの状態）は実装定義である。実装は、この節と[§24](#s24)のすべての保証を提供しなければならない。それを実装するライブラリーコードはunsafeコードであり（[§5](#s5)）、保証への違反は未定義動作となる。

<a id="s22-3-1"></a>
#### 22.3.1. 宣言

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

- **引数数。** `join`、`joinOk`、`race`は、子の数2～4のそれぞれに対して厳密に一つのオーバーロードを持つ。`Raced`も引数数2～4ごとに一つの宣言を持ち、子の順にCase `First`、`Second`、`Third`、`Fourth`を持つ。各Caseはすべての最終結果を保持する。それより多い子を渡す呼出しには候補がない。呼出しを入れ子にするか`each`を使う。
- **子。** 一度だけ実行する子（`join`、`joinOk`、`race`、`pipe`、`shield`の子）は消費するため、本体はShared、Exclusive、Consumingのいずれでもよい（[§8.10](#s8-10)）。`let`内の非Copyの子は`@move`で渡す。`each`と`eachReceived`は子を共有借用して並行に呼ぶため、Sharedの本体が必要となる。
- **上限。** `limit`は同時に実行する子の最大数、`capacity`はチャネルが保持する項目数である（`0`は待ち合わせ方式）。`limit < 1`と`capacity < 0`は`KIMI_E_ARG_RANGE`でAbortする。
- **所有権。** 子のLoanは引数の依存である（[§15.6.4](#s15-6-4)）。一度だけ実行する子のLoanは、操作が子を破棄するまで、さらにそれに依存する結果を通じて存続する。重なり合う排他的キャプチャーは通常のLoan競合となる（[§15.6.7](#s15-6-7)）。子にOwnedは不要である。

```kimi
func loadAll(task;) -> (Config, Users, Rules)
    return Async.join(task; loadConfig, loadUsers, loadRules)   // Three children.
// Async.join(task; f1, f2, f3, f4, f5)   // Error: no overload for five children.
```

<a id="s22-3-2"></a>
#### 22.3.2. 実行と構造化操作

`Async.run(root)`は、新しいタスクツリーのルートとして`root`を開始する。その全タスクは現在のスレッドで実行し、ツリーの全タスクが完了するとルートの結果を返す。タスクを持たないコードがタスク操作を呼ぶ唯一の方法である。入れ子の`run`内では外側のツリーのタスクは実行しないため、外側のタスクを待つ内側のツリーは進行できない。進行できないツリーは発散する。この検出は任意の診断であり、Abortにはしない。子は`rc`ハンドルをキャプチャーできる。非Ownedの値について、Originが終わる前に破棄される保証はない。自身のタスクを引き渡すユーザー関数は、同じタスクの一部である。

すべての構造化操作は、その子がすべて完了してからreturnする。**決定イベント**で、まだ実行中の子を取り消す場合がある。

| 操作 | 決定イベント | その後 | 結果 |
| --- | --- | --- | --- |
| `join` | 最後の完了 | — | すべての結果 |
| `joinOk` | 最初の`Err`、なければ最後の`Ok` | 他の子を取り消す | 最初の`Err`、または全結果の`Ok`。返さない結果は破棄する |
| `race` | 最初の完了 | 他の子を取り消す | すべての最終結果を持つ`Raced` |
| `each` | イテレーターの終端と最後の子の完了 | — | `Ok(())`、または要求によって取り出しを止め、全子の終了を待ってから`Err` |
| `eachReceived` | 受信側が閉じられて空になり、最後の子が完了 | — | 同様。要求によって受信を止める |
| `pipe` | 消費側の完了 | 受信側を閉じ、生産側を取り消す | 消費側の結果 |
| `shield` | 本体の完了 | — | 本体の結果 |

`join`、`joinOk`、`race`は引数順に子を開始し、各子が最初に一時停止するか完了するまで実行してから、次の子を開始する。決定イベント後に開始する子は、取消し要求が適用された状態で開始する（[§22.3.3](#s22-3-3)）。実行可能なタスクの再開順は未規定である。

`each`は自身のタスクフレーム内でだけ`next`を呼び、実行中の子が`limit`未満の場合に限る。取り出しの前に毎回、自身に適用される要求を検査し、適用される要求があれば以後取り出さない。この検査は待機ではなく、確定点を持たない。各項目につき一つの子を順に開始し、最初の`None`で止まり、最後の子が完了してからIteratorを破棄する。`eachReceived`も同様に、実行中の子が`limit`未満の場合だけ受信し、受信した項目ごとに子を開始してから次を受信する。

```kimi
func pick(task;) -> i32
    let a = func [] (task;) -> i32 => 1
    let b = func [] (task;) -> i32 => 2
    match Async.race(task; a, b)
        .First(let x, _) => return x     // Always First: a completes before b starts.
        .Second(_, let y) => return y
```

<a id="s22-3-3"></a>
#### 22.3.3. 取消しと待機

取消しを要求するのはKimi操作だけであり、自身が開始した子に対して行う（[§22.3.2](#s22-3-2)）。要求を表す値はない。あるタスクへの要求は、その部分木内で現在保留中、または後から開始するすべての**取消しを観測する待機**に適用される。対象は`sleep`、`checkpoint`、I/O待機、`send`、`receive`である。各待機は、自身の操作（下表）か取消しのうち先に起きた方によって、厳密に一度だけ**確定**し、その確定結果を返す。取消しは`Err(Async.Cancelled)`、`send`では`Err((value, .Cancelled(reason)))`、または領域固有エラーの`Cancelled` Caseである。

- 要求が適用されている間に開始した待機は、呼出し時に取消しへ確定する。要求は、I/O待機を除く保留中の待機を直ちに確定する。
- 確定は最終的であり、その後の要求、クローズ、相手側の操作で変わらない。一つのタスクツリー内の確定には全順序がある。
- 値がMoveするのは確定時だけである。`send`は`Ok(())`への確定時にだけ値を渡し、それ以外では`Err`で返す。`receive`が項目を取り除くのは`Ok(Some(item))`への確定時だけである。
- 取消しはタスクフレームを中断、巻戻し、破棄しない。

| 待機 | 操作による確定時点 |
| --- | --- |
| `send` | 値が送信側から離れたとき：`Ok(())`。受信側が閉じられたとき：値と`Closed`を持つ`Err` |
| `receive` | 項目が受信側に届いたとき：`Ok(Some(item))`。チャネルが閉じられて空になったとき：`Ok(None)` |
| `sleep` | 実行器が再開するときに期限を過ぎている |
| `checkpoint` | 実行を譲らない場合は呼出し時、譲る場合は再開時 |
| I/O | 操作が直ちに完了する場合は呼出し時、それ以外は完了を受け取ったとき |

**I/O。** KimiはI/O操作を必須としない。実装が提供する各I/O操作はタスクスロットを取り、取消しを観測する待機となる。要求は保留中のI/O待機を確定せず、プラットフォームに操作の取消しを依頼する。その完了によって待機を確定する。データを一切転送する前に操作を取り消したと報告された場合は取消しに、それ以外は報告された結果に確定する。I/O待機は、取り消されたものも含め、完了を受け取ってからreturnする。完了の配送方法は実装定義である。

- **`checkpoint`**は保留中の要求を報告する。他に実行可能なタスクがある場合、または実装定義の回数呼び出された後にだけ実行を譲り、時計は読まない。
- **`sleep(task; d)`**は、呼出しから`d`が経過するより前には`Ok(())`を返さない。登録ではメモリー割当てを行わない。`sleep`と`shield`は`Time.Stopwatch`の時計で単調な経過時間を測る。期限と入れ子の猶予時間の合計は正確である。大きな`Time.Duration`でAbortすることはなく、時計の範囲を超える期限には到達しない。
- **`shield(task; body, grace: g)`**は、他のタスクが動作を続ける間、`body`を子として実行する。呼出し側に適用される要求が本体の部分木へ届くのは、要求時点と呼出し開始時点の遅い方から`g`が経過した後だけである。本体内で行われた要求は通常どおり適用する。呼出し側の要求は、次の取消しを観測する待機まで保留される。入れ子のshieldでは猶予時間を加算する。
- **効果。** 子を実行するKimi操作（`run`、`shield`、`join`、`joinOk`、`race`、`each`、`eachReceived`、`pipe`、`offload`、`offloadEach`）の効果要約には、Callable入力を通じて各子を呼ぶ効果を、パラメーター相対で含める（[§15.6.4](#s15-6-4)）。一時停止後に実行するすべての部分も含む。子の効果、`each`が進めて破棄するIteratorの効果、操作が所有する値の破棄を除き、`sleep`、`checkpoint`、`shield`、`join`、`joinOk`、`race`、`each`、`eachReceived`、`pipe`、`send`、`receive`は環境効果を持たず、`confined`な実装で使える。`run`はスレッドごとの実行器状態にアクセスするため環境効果を持ち、`confined`にはならない。I/O待機は環境効果である。
- **その他の規則。** `Async.Cancelled`から領域固有エラーへの変換は明示的に行う（[§17.3](#s17-3)）。タイムアウトは`sleep`との`race`で表す。もう一方の子は、取消しを観測する待機でのみ終了する。遅延ブロックはタスクを渡せないため（[§24.2.2](#s24-2-2)）、待機を伴う終了処理は明示的な呼出しとし、取消し中でも動かすには`shield`を通じて呼ぶ。

```kimi
func flushAlways(task; log: uniq/Log) -> Result<(), LogError>
    let finish = func [log] (task;) -> Result<(), LogError> => log.flush(task;)
    let grace = Time.Duration.init(microseconds: 2000000)
    return Async.shield(task; finish@move, grace: grace)   // Up to 2 s more under cancellation.
```

<a id="s22-3-4"></a>
#### 22.3.4. チャネル

チャネルを作る方法は`pipe`だけであり、各子に一つの端点を`uniq`として貸し出す。`capacity >= 1`では、定常状態でヒープメモリーを割り当てずに項目をバッファーに保持する。消費側が完了すると、`pipe`は受信側を閉じ、生産側を取り消してその終了を待つ。保留中の`send`は`Closed`に確定する。生産側が先にreturnすると送信側を閉じ、`receive`はバッファー内の項目を返した後、以降の呼出しで常に`Ok(None)`を返す。`send`の`Ok`はチャネルが値を受け取ったことを示し、消費側が受け取ったことは意味しない。引き渡された項目は、受信側の借用にもチャネルの記憶領域にも依存しない。端点の実行が交互になるのはタスク呼出しだけである（[§24.3](#s24-3)）。

配送は、成功したsendの確定順にFIFOで行う。`pipe`がreturnする前に、バッファーに残る項目を成功したsendの確定の逆順で破棄し、一つの破棄を完全に終えてから次へ進む。これらの順序はタスクの再開順から独立している。

切り離されたタスクはない。動的な作業には、型`(task;) -> ()`へ型消去した処理を`eachReceived`で使う（[§7.7.4](#s7-7-4)）。ジェネレーターは容量0の`pipe`で表す。

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

<a id="s22-3-5"></a>
#### 22.3.5. シーケンスとブロッキング呼出し

非同期シーケンスは、`loop`内でタスク呼出しを繰り返して読む。`next`にタスクスロットはないため、`for`は変わらない（[§14.6.2](#s14-6-2)）。`Async.run`を除き、待機し得るすべてのKimi操作はタスクスロットを持ち、タスクなしの対応操作はない。例外は`Console.writeLine`で、スレッドをブロックし、その間に他のタスクを実行しない。

<a id="s22-3-6"></a>
#### 22.3.6. 構造化ワーカーへの処理の移譲

別スレッドでの計算は、次の操作を通じてのみ実行する。

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

- ワーカーの子はタスクスロットを持たず、タスクを実行せず、取消しの観測も巻戻しも行わない。タイムアウトで強制中断はできない。`offload`は子を値で取得する。`offloadEach`は子を共有借用し、各項目を値で渡し、Iteratorは親スレッドに保つ。それ以外は`each`に従う（[§22.3.2](#s22-3-2)）。
- どちらの操作も、ワーカーの終了まで子、その引数、それらのLoanを保持し、すべての経路で子の終了を待ってからreturnする。操作が一部だけ開始された場合も、子が生存中にreturnすることはない。ワーカーの開始失敗は`KIMI_E_THREAD`でAbortする。
- `confined`（[§8.11.2](#s8-11-2)）は、可変static、外部呼出し、Console出力などの環境権限を除外する。ワーカーと、その推移的な要約に含まれるすべての呼出し先が不変staticを読めるのは、その節で許可する場合だけである。操作の入力が持つ静的Loanは別途検査する（[§24.3](#s24-3)、[§15.6.4](#s15-6-4)）。`Transferable`（[§15.2.4](#s15-2-4)）はスレッド安全でない入力を除外する。
- 結果にはTransferableやOwnedの制約は不要であり、子の終了を待ってから引き渡す。ワーカーのAbortは[§17.4.3](#s17-4-3)に従う。

<a id="s22-3-7"></a>
#### 22.3.7. データ競合

二つのアクセスが**競合**するのは、重なるPlace（[§3.4](#s3-4)）または同じオブジェクトの参照カウント（[§13.6.2](#s13-6-2)）に触れ、少なくとも一方が書込みであり、両方とも実装によるアトミックアクセス（`arc`カウントの更新など）というわけではない場合である。Take、破棄、配置は書込みと数える。再配置は元のPlaceにも書き込み、記憶領域の解放はその中の全Placeに書き込む。**データ競合**は、異なるスレッドからの競合するアクセスの組で、happens-beforeによる順序がないものをいう。happens-beforeは、スレッド内の順序に次の辺を加えた推移閉包である。

- ワーカーの子が始まる前の親のすべての操作は、その子のアクセスより前に起こる。
- 子の値の破棄と結果の構築を含む、ワーカーの子のすべての操作は、終了待機後の親の継続より前に起こる。
- Initializedの静的スロットの公開は、それを観測するすべてのアクセスより前に起こる（[§22.4.3](#s22-4-3)）。

`arc`のカウントプロトコル（[§13.6.2](#s13-6-2)）が順序付けるのはハンドルの状態と寿命だけであり、ペイロードへのアクセスではない。データ競合は未定義動作である。安全なコードはデータ競合を起こせない。Loan検査と`Transferable`が、これらの辺の外でスレッド間の競合するアクセスを許さないためである。Loanは実行時の順序を作らず、競合するアクセスを禁止するだけである。その他のスレッド機能はこの版の範囲外である（[§D.5](#sd-5)）。

<a id="s22-4"></a>
### 22.4. プログラム起動と静的記憶領域

<a id="s22-4-1"></a>
#### 22.4.1. 起動形式の選択

Applicationは厳密に一つの起動形式を持つ。条件選択とソース生成の後、現在のプロジェクトのルート項目から判定する。トップレベルの実行時本体項目を持つ一つのSourceDocumentか、ルートレベルの一つの`public func main() -> ()`である。形式の混在、複数候補、候補なしはエラーとなる。実行時本体項目は、式、制御式、`unsafe`、`defer`、`require`、結果を捨てる文、ローカルの`let`/`var`であり、初期化子がないものも含む。宣言、別名、除外構文は該当しない。生成項目は、それが生成する種類の項目として数える。最小の空のApplicationはUnit式`()`である。

選択した文書の本体項目は、その文書の実行スコープでソース順に一度だけ実行する（[§6.1.1](#s6-1-1)）。トップレベルの束縛は文書内のローカルであり、静的プロパティではない。本体はreturn先を持たない（[§14.5.2](#s14-5-2)）。

<a id="s22-4-2"></a>
#### 22.4.2. 明示的なmainとLibrary

明示的なmainは、ソースルートの`public func main()`に限る（[§6.1.1](#s6-1-1)）。本体を持つ安全な関数で、結果はUnit（省略可）とし、タスクスロット、パラメーター、レシーバ、ジェネリックパラメーター、Originパラメーターは持たない。importや特殊化でもない。Applicationのルートレベルの`public main`はすべてこの要件を検査し、無効ならエラーとなる。`public`はネイティブシンボルをexportしない。正常完了は成功の終了ステータスでプロセスを終える。Abortは[§17.4.3](#s17-4-3)に従う。

Libraryに起動形式は不要であり、`main`を自動的に呼ばない。Libraryの`main`は通常の関数であり、トップレベルの実行時本体項目はエラーとなる。

```kimi
public func main() -> ()
    let message = "Hello, world!"
    Console.writeLine(message)          // message is borrowed and stays usable.
// Console.writeLine("Top level")       // Error: mixes the two startup forms.
```

<a id="s22-4-3"></a>
#### 22.4.3. 静的初期化と終了処理

静的な格納プロパティ（[§11.8](#s11-8)）は、スロットごとの初回アクセス時に初期化する。プログラムは一つのスレッドで始まり、別スレッドでコードを実行するのは構造化ワーカーへの移譲だけである（[§22.3.6](#s22-3-6)）。スロットはNot started、Initializing、Initialized、Finishedのいずれかであり、読取り、借用、書込み、その他のアクセスのたびに状態を検査する。

| 状態 | 動作 |
| --- | --- |
| Not started | 終了処理前：アクセスした厳密に一つのスレッドがスロットをInitializingにして初期化子を実行し、正常完了後にInitializedにしてからアクセスする。終了処理中：`KIMI_E_STATIC_SHUTDOWN`でAbort |
| Initializing | 初期化中のスレッドによるアクセス：`KIMI_E_STATIC_CYCLE`でAbort。別スレッドによるアクセス：Initializedになるまで待つ。スレッドをまたぐ待機循環はデッドロックする場合がある |
| Initialized | アクセスする |
| Finished | `KIMI_E_STATIC_SHUTDOWN`でAbort |

最初の書込みでも、置き換える前に初期化する。実行されたアクセスだけが初期化を起こす。型・関数への参照、選ばれない分岐、効果要約は何も初期化しない。使われなくてもすべての初期化子を検査する。未初期化スロットは破棄しない。これらの規則は依存先を含むすべてのKotonohaのスロットに適用する。

起動本体が完了し、そのローカル変数を後処理した後、終了処理はInitializedの静的`var`スロットをすべて破棄し、次にInitializedの静的`let`スロットをすべて破棄する。各段階では、各スロットがInitializedになった時点の逆順（全スレッド共通の一つの順序）で処理し、値を破棄する前にスロットをFinishedにする。静的`let`スロットの破棄中に別の静的`let`スロットへアクセスすると、そのスロットの初期化が先に完了していれば（破棄は後なので）成功し、それ以外は`KIMI_E_STATIC_SHUTDOWN`でAbortする。結果は実行時の初期化順に依存する。終了処理中のAbortは[§17.4.3](#s17-4-3)に従う。

```kimi
group Registry
    public let names: Names = Names.init()
    public let probe: Probe = Probe.init()   // Probe's drop reads Registry.names.
// Initialized names, then probe: probe is destroyed first and its drop succeeds.
// Initialized probe, then names: names is Finished first, so probe's drop Aborts.
```

<a id="s22-4-4"></a>
#### 22.4.4. 静的記憶領域のキー

group直下のフィールドは、そのgroupがstruct内に入れ子でも静的である。**静的記憶領域キー**は、フィールド宣言と、Originを消去した外側の型引数で構成する。Semantics層と未使用の引数もキーを区別する。Loan検査と効果要約では区別せず、一つのフィールドの全キーを一つのルートとする（[§15.6.2](#s15-6-2)、[§15.6.4](#s15-6-4)）。到達パスによらず、各キーは一つのスロットを持ち、初期化状態も破棄も一つとなる。格納値はOwnedで、静的記憶領域の条件を満たさなければならない（[§11.8](#s11-8)）。外側の型引数はOwnedでなくてよい。アクセスではLoan・寿命検査のために完全なOriginを保持する。初期化子と破棄は、そのキーを共有するすべてのOrigin束縛について有効でなければならない（全称的な検査、[§8.15](#s8-15)）。そうでなければ宣言をエラーとする。[§22.4.3](#s22-4-3)の規則を適用する。

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
// Cache<i32>.Statistics.count and Cache<string>.Statistics.count are separate slots.
// Cache<ref/i32 during a> and Cache<ref/i32 during b> share one slot.
```

<a id="s22-5"></a>
### 22.5. Abortの位置とコード

Abortの契約は[§17.4.3](#s17-4-3)、windows-x64の出力形式は[§21.4.2](#s21-4-2)で定める。

<a id="s22-5-1"></a>
#### 22.5.1. Abort位置

Abortは一つのソース位置を報告する。その位置は、最適化レベルやインスタンス化によって変わらない。

- ユーザーコードが呼出しまたは構文（添字アクセス、スライス、リテラル）で起動したKimi操作（Kimi Kotonohaの公開関数、アクセサー、メンバー）内の失敗。その操作の内部コード、メモリー割当て、静的記憶領域へのアクセス、`$abort`も含む：起動したユーザー式の先頭。関数値経由の呼出しでは、その値を形成した位置ではなく呼出し位置。Dictionaryリテラルの重複キーは、後のキー式を報告する。
- `equals`や`drop`など、Kimi操作が呼び出したユーザーコード：そのコード自身の位置。
- `for`の暗黙の反復入口または`next`呼出し：`for`式の先頭。
- ユーザーコードの算術・シフト・変換の検査：失敗した式の先頭。複合代入と`++`/`--`では更新式全体。
- ユーザーコードの静的記憶領域のAbort：スロットを指すPlace式の先頭。修飾子を含む（`Cache<i32>.Statistics.count`では`Cache`）。
- ユーザーコードの`$abort`：`$abort`式。
- 型消去した関数値のメモリー割当て：変換式。

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

<a id="s22-5-2"></a>
#### 22.5.2. Abortコード

[§17.4.1](#s17-4-1)とKimi操作のすべてのAbort原因は、この閉じた表の厳密に一つの行に対応する。

| コードと理由 | 原因 |
| --- | --- |
| `KIMI_E_INT_OVERFLOW: Integer overflow` | 検査付き整数結果を表現できない。符号付き最小値の-1による除算も含む |
| `KIMI_E_INT_DIV_ZERO: Integer division or remainder by zero` | 整数のゼロによる`/`または`%`。ラップ型も含む |
| `KIMI_E_INT_SHIFT_COUNT: Shift count out of range` | シフト数が`0 <= count < bit width`の範囲外 |
| `KIMI_E_INT_CONVERSION: Integer conversion out of range` | 実行時整数変換が対象型の範囲外、またはNaN・無限大を含む浮動小数点数から整数への変換失敗 |
| `KIMI_E_FLOAT_CONVERSION: Floating conversion out of range` | 有限の変換元が丸めによって浮動小数点の無限大になる |
| `KIMI_E_INDEX_BOUNDS: Index out of bounds` | 位置・範囲の解決失敗。添字アクセス、スライス、`resolve`、`insert`/`remove`の位置 |
| `KIMI_E_MISSING_KEY: Dictionary key was not found` | 存在しないキーによるDictionary添字アクセス |
| `KIMI_E_DUPLICATE_KEY: Dictionary literal contains an equivalent key` | Dictionaryリテラルの実行時キー重複 |
| `KIMI_E_ARG_RANGE: Argument out of range` | 数値引数の範囲外。負の個数・容量・長さ、`limit < 1`、`capacity < 0`、逆順範囲の反復 |
| `KIMI_E_FORMAT: Formatting failed` | 書式化の失敗（[§22.10](#s22-10)） |
| `KIMI_E_REF_COUNT: Reference count limit exceeded` | `clone`、`downgrade`、`upgrade`で強参照・弱参照カウントが最大値に達した |
| `KIMI_E_STATIC_CYCLE: Static initialization cycle` | 同じスレッドが初期化中のスロットへのアクセス |
| `KIMI_E_STATIC_SHUTDOWN: Static storage unavailable during shutdown` | Finishedのスロットへのアクセス、または終了処理中の初回アクセス |
| `KIMI_E_ALLOC_SIZE: Allocation size exceeds limit` | 要求サイズがオーバーフローするか、最大オブジェクトサイズを超える |
| `KIMI_E_ALLOC: Failed to allocate memory` | アロケーターがメモリーを返さない |
| `KIMI_E_FREE: Failed to free memory` | 検出された解放失敗 |
| `KIMI_E_PROCESS_HEAP: Failed to obtain process heap` | ランタイムヒープを利用できない |
| `KIMI_E_STDOUT: Failed to write to stdout` | 標準出力の失敗 |
| `KIMI_E_STRING_RELEASE: Invalid string release kind` | 破棄中に無効な文字列表現を検出した（検出の保証はない） |
| `KIMI_E_CLOCK: Elapsed-time counter failed` | 経過時間カウンターが失敗する、正でない周波数を報告する、または逆行する |
| `KIMI_E_THREAD: Failed to start worker` | ワーカーを開始できない（[§22.3.6](#s22-3-6)） |
| `KIMI_E_REQUIRE: <reason>` | falseの`$require`によるAbort（[§17.6.2](#s17-6-2)）。理由は評価済みの`message:`をそのまま使い、省略時は`Test requirement failed` |
| `KIMI_E_ABORT: <message>` | 明示的な`$abort(message)`。メッセージを変更せずに書き出す |

<a id="s22-6"></a>
### 22.6. 外部関数のimport

本体のない`func`に付けた`#LibraryImport("library", "symbol")`は、安全・unsafeのどちらでもC関数を宣言する。両引数は空でなく、補間がなく、NULを含まない文字列リテラルである。一つ目は、定義元Kotonohaの、大文字・小文字を区別するネイティブライブラリー論理名で、プログラムのビルド時に解決する（[§20.4](#s20-4)）。二つ目は正確な外部シンボルである。

importはgroupまたはrootgroupの直下、またはレシーバのないstructの型関数として宣言し、タスクスロット、レシーバ、パラメーター既定値、可変長引数、特殊化、本体を持たない。importはジェネリックであってはならない。import自身も外側のすべてのコンテナーも、種類を問わずジェネリックパラメーター（型、Semantics、長さ、Origin。[§6.1.3](#s6-1-3)）を宣言してはならず、未使用のものも含む。自身のシグネチャの借用注釈が導入するOriginは許可し、raw層はimportの安全性契約の一部としてその一つを指定できる（[§5.7](#s5-7)）。すべての引数が必須である。importは直接だけ呼び出し、関数値としては使わない。

宣言は、シグネチャと`- safety:`条件を満たすすべての呼出しについて、その外部関数が同じシグネチャのKimigayo関数として動作することを保証する。アクセスできるのは引数が許可する対象だけで、呼出し中だけである。`ref`経由では読取り、`uniq`経由では排他アクセス、`raw/T`経由では`- safety:`条件が許すアクセスを行う。有効な値だけを格納し、再帰的に見て型にユーザー`drop`がない値だけを構築・上書きする（再配置は値をMoveするため、そのどちらでもない。[§5.2](#s5-2)）。結果のOriginを超えて借用を保持せず、有効な結果を返し、巻戻し、非局所ジャンプ、コールバック、再入がKimigayoフレームをまたぐことを許さない。偽の保証は未定義動作である。importを`unsafe`とするのは、型で表せない義務が呼出し側にある場合だけである（[§7.6](#s7-6)）。呼出しの効果は、シグネチャが許すアクセスと環境効果である（[§8.11.2](#s8-11-2)）。引数は左から右へ一度だけ評価し、マーシャリング、保持、所有権移転をせずに渡す。[§21.2](#s21-2)の浮動小数点境界契約を適用する。

| パラメーター型または結果型 | Cでの意味 |
| --- | --- |
| `i8`…`i64`、`u8`…`u64`、`f32`、`f64` | `int8_t`…`int64_t`、`uint8_t`…`uint64_t`、`float`、`double` |
| `raw/T` | データポインター。`T`はCと交換可能でなくてもよい |
| `ref/T`、`uniq/T` | 非nullの`const T *`、`T *`。`T`はCと交換可能 |
| `Option<ref/T>`、`Option<uniq/T>` | ポインター。`None`はnull |
| Unit（結果だけ） | `void` |

Cと交換可能な型は、固定幅整数、`f32`、`f64`、`raw/T`、Cと交換可能な参照先への`ref/T`・`uniq/T`とその`Option`、Cと交換可能な要素の正の長さの固定配列、または全フィールドがCと交換可能なCレイアウトstruct（[§21.1.2](#s21-1-2)）である。Cに対応するものがない型のパラメーター・結果はエラーとなる。`string`、動的コレクション、オブジェクトハンドル、関数値・クロージャー値、それらの型の借用が該当する。表にないその他の型、すなわち`bool`、`char`、`isize`/`usize`、`i128`/`u128`、値で渡すタプル・enum・struct・固定配列、およびCと交換可能でない参照先の借用はUnsupportedである。

```kimi
group Native
    #LibraryImport("observer", "observe_record")
    public func observe(record: uniq/NativeRecord) -> ()     // C: void observe_record(NativeRecord *);

    #LibraryImport("observer", "observe_raw")
    public unsafe func observeRaw(record: raw/NativeRecord) -> ()   // - safety: states layout, lifetime, retention.

    // #LibraryImport("observer", "print")
    // public func print(text: ref/string) -> ()   // Error: string has no C counterpart.
```

<a id="s22-7"></a>
### 22.7. コンソール出力

`Kimi.Console` groupは、安全で非ジェネリックな二つのオーバーロード、`writeLine(text: ref/string) -> ()`と`writeLine(text: Text.Utf8Slice) -> ()`を宣言する。オーバーロード解決は文字列には文字列のオーバーロード、ビューにはビューのオーバーロードを選ぶ。暗黙変換はなく、期待型のない関数参照は曖昧となる。どちらもタスクスロットを持たず、呼出しは同期的である（[§22.3.5](#s22-3-5)）。

テキストは呼出しの間だけ借用する。文字列のPlaceは引き続き使え、リテラルや補間は借用された一時値となる。各呼出しは、テキストの全UTF-8バイトと一つのLFを標準出力へ書き出す。正規化、改行変換、文字コード変換はせず、NULもデータである。ホストがバイト列を受け取ってからreturnするが、表示や永続的な保存の完了は待つとは限らない。失敗は`KIMI_E_STDOUT`でAbortし、出力の一部が残ることがある。この呼出しは何も破棄しない。

```kimi
Console.writeLine("Hello, world!")       // Writes "Hello, world!" and LF; exits successfully.
::Kimi.Console.writeLine("Hello")
// writeLine("Hello")                    // Error: Console is not opened.
```

<a id="s22-8"></a>
### 22.8. 時間

`Kimi.Time` groupは、単調な経過時間を測る。暦時計の調整は適用しない。時計源とその分解能は実装定義である。時間操作はメモリーを割り当てない。

<a id="s22-8-1"></a>
#### 22.8.1. Duration

`Time.Duration`は整数マイクロ秒単位のCopyの時間間隔であり、日付やタイムスタンプではない。`init(! microseconds: u64)`は正確な値を作る（すべての`u64`を表現できる）。`rawMicroseconds: u64`で読み戻せる。ゲッター`seconds`、`milliseconds`、`microseconds: f64`は、`rawMicroseconds@f64`をそれぞれ1,000,000、1,000、1で割った値である（[§13.5](#s13-5)）。

<a id="s22-8-2"></a>
#### 22.8.2. Stopwatch

`Time.Stopwatch`は非Copyである。積算した整数マイクロ秒と、動作中なら現在の計測区間の開始時点を保持する。読取り専用の`isRunning: bool`は動作中かどうかを返す。

| 宣言 | 動作 |
| --- | --- |
| `init()`、`reset(self: uniq/Self) -> ()` | 0にして停止する。時計は読まない |
| `start(self: uniq/Self) -> ()` | 停止中なら新しい計測区間を開始する。動作中なら何もしない |
| `stop(self: uniq/Self) -> ()` | 現在の計測区間を加算して停止する。停止中なら何もしない |
| `restart(self: uniq/Self) -> ()` | 0にして、新しい計測区間を開始する |
| `elapsed(self: ref/Self) -> Duration` | 変更せずに、積算時間と動作中の計測区間の合計を返す。停止中なら時計は読まない |

カウンター差`d`とカウンター周波数`f`に対し、一つの計測区間が加える時間は厳密に`floor(d × 1,000,000 / f)`マイクロ秒である。結果が`u64`に収まる場合、中間オーバーフローなしで計算する。`stop`は区間の1マイクロ秒未満の端数を捨て、区間間に端数を持ち越さない。積算が`u64`を超えると`KIMI_E_INT_OVERFLOW`、時計の失敗は`KIMI_E_CLOCK`でAbortする。

```kimi
var watch = Time.Stopwatch.init()
watch.start()
// Measured work.
watch.stop()
let seconds: f64 = watch.elapsed().seconds
```

<a id="s22-8-3"></a>
#### 22.8.3. Windowsカウンター関数

`Kimi.Windows` groupは通常のimportを二つ宣言する（[§22.6](#s22-6)）。すべてのターゲットに存在するが、importを解決するのはwindows-x64向けにプログラムをビルドするときだけである。

```kimi
public group Windows
    #LibraryImport("kernel32", "QueryPerformanceCounter")
    public func queryPerformanceCounter(value: uniq/i64) -> i32

    #LibraryImport("kernel32", "QueryPerformanceFrequency")
    public func queryPerformanceFrequency(value: uniq/i64) -> i32
```

各関数はネイティブのフラグ（非0が成功）を返し、借用した`i64`にカウント、または1秒あたりのカウント数を書き込む。安全な関数であり、メモリーを割り当てず、Abortしない。

<a id="s22-9"></a>
### 22.9. UTF-8テキストとバッファー

この節と[§22.10](#s22-10)の長さと容量は、`isize`型のバイト数である。

<a id="s22-9-1"></a>
#### 22.9.1. 宣言

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        effect confined

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

どちらも通常の、ユーザーが実装できる静的Contractである。書式化Contractは`Utf8Format`だけであり、書式化にオプション、ロケール、実行時書式文字列はない。`reserve`の結果Originは、省略規則によって`self`となる。`Utf8Writer`の外側の借用とその`target`は独立した入力Originであり、targetは外側の借用より長く生存しなければならない。

- `BufferFull`（`Kimi`内）と`Text.InvalidUtf8`は、公開`init()`を持つ状態のないCopy structである。
- `WriteWindow {source}`と`Utf8Writer {target}`（`Kimi`内）、`Text.FixedBuffer {source}`は、`Loan<uniq/u8 during source>`（または`target`）フィールドを持つ非Copy structであり（[§15.3.5](#s15-3-5)）、ObjectPayloadではない。
- `Text.HeapBuffer`は拡張可能な割当て領域を所有する非Copy structである。`Text.Utf8Slice {source}`は有効なUTF-8のCopyビューであり、必要なLoanは`ref`である。

`WriteWindow`、`Utf8Writer`、`FixedBuffer`、`HeapBuffer`、`Utf8Slice`に公開コンストラクターはない。[§22.9.2](#s22-9-2)と[§22.9.4](#s22-9-4)の操作からだけ取得する。

これらのLoanを形成するのは`Text.fixed`、`Text.writer`、`reserve`だけである。ウィンドウ、ビュー、アダプターはメモリーを割り当てない。

`BufferWriter.reserve`は`confined`境界を持つ（[§8.11.2](#s8-11-2)）。`Utf8Writer`はWriterの型を消去するため、`format`はWriter型をジェネリックに取らない。アダプターはWriterの`reserve`だけを呼び、Writerを所有、Move、公開しない。Writerは自身のLoanを保つ。型消去された`reserve`呼出しは、`confined`境界に対して検査する（[§8.11.4](#s8-11-4)）。

<a id="s22-9-2"></a>
#### 22.9.2. Text操作

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

- `fixed`は初期化済みの配列を借用し、長さ0、容量`N`となる。`heap`は長さ0で、少なくとも要求した容量を持つ。容量0ではメモリーを割り当てず、負の容量は`KIMI_E_ARG_RANGE`でAbortする。`writer`は失敗記録のないアダプターを作る。
- `utf8`は文字列のバイト列をビューにする。`validateUtf8`はバイト列を一度だけ検証する。どちらもメモリー割当てやコピーを行わない。
- `toString`は新しい文字列に書式化し、`BufferFull`なら`KIMI_E_FORMAT`でAbortする。文字列のコピーもこの操作で行う。
- `tryFormat`は配列に一度だけ書式化し、事前の走査や再試行はしない。結果は`destination`の排他Loanを保つため、生存中に配列を読むにはビューを通じなければならない。`BufferFull`では、配列に一部のバイト列が残る場合がある。

```kimi
var bytes: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, bytes@uniq)
// let first = bytes[0]  // Error: result still holds the exclusive Loan.
match result@move
    .Ok(let text) => Console.writeLine(text)
    .Err(_) => Console.writeLine("too small")
```

<a id="s22-9-3"></a>
#### 22.9.3. バッファーと検証

`FixedBuffer`と`HeapBuffer`は`BufferWriter`を実装する。それぞれが確定した`length`、`capacity`（ゲッター）、検証済みの長さを持ち、`0 <= validated <= length <= capacity`を満たす。確定済みバイトは初期化され、先頭の`validated`バイトは有効なUTF-8である。

| 操作 | レシーバ | 動作 |
| --- | --- | --- |
| `bytes()` | `ref/Self` | 確定済みバイトの`Slice<u8>` |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`。未検証の末尾部分を検証し、何も記録しない |
| `validate()` | `uniq/Self` | `Result<(), InvalidUtf8>`。完全成功時だけ検証を記録する |
| `clear()` | `uniq/Self` | 両方の長さを0にし、容量を保つ |
| `intoText()`（FixedBuffer） | `owner/Self` | `Result<Utf8Slice during self.source, InvalidUtf8>`。末尾部分を検証する |
| `intoString()`（HeapBuffer） | `owner/Self` | `Result<string, InvalidUtf8>`。末尾部分を検証し、コピーせずに割当て領域を転送する |

`intoText`は元の配列の排他Loanを保持するため、呼出し側の配列のビューを返せるが、ローカル配列を外へ逃がすことはできない。生の`commit`は検証済みの長さを変えない。アダプターのcommitが検証済み長さを進めるのは、予約時に`validated == length`だった場合だけである。そのため、アダプターだけを通じて書き込めば、後の走査は不要となる。

検証が受理するのはUnicodeスカラー値のUTF-8符号化だけであり、途中で切れた列、過長符号化、サロゲート、範囲外の値を拒否する。置換や正規化はせず、NULもデータである。`Utf8Slice`は`length`と`bytes() -> Slice<u8> during self.source`を持つ。`FixedBuffer`、`WriteWindow`、`Utf8Writer`には`drop`がない。`HeapBuffer`は割当て領域を一度だけ解放する。

<a id="s22-9-4"></a>
#### 22.9.4. 予約とウィンドウ

`reserve(minimum)`が成功すると、`written == 0`、`remaining >= minimum`のウィンドウを返し、確定済みバイトは変えない。負の`minimum`は`KIMI_E_ARG_RANGE`でAbortする。`FixedBuffer`は、未使用容量が不足する場合に限り`BufferFull`を返す。`HeapBuffer`は`BufferFull`を返さない。`length + minimum`が最大オブジェクトサイズを超えると`KIMI_E_ALLOC_SIZE`でAbortする。容量を超える場合は、バッファーを`max(length + minimum, 2 * capacity)`まで拡張し（`2 * capacity`が最大オブジェクトサイズを超える場合は`length + minimum`）、確定済みバイトをコピーする。

| ウィンドウ操作 | 動作 |
| --- | --- |
| `written`、`remaining` | 書込み済みバイト数と残り容量 |
| `push(byte: u8)`、`append(bytes: Slice<u8>)` | `Result<(), BufferFull>`。全体が収まるか、何も変更しないかのいずれか |
| `limit(maximum: isize)`（`owner/Self`） | 容量を`min(capacity, maximum)`としたウィンドウ。`maximum < written`は`KIMI_E_ARG_RANGE`でAbort |
| `commit()`（`owner/Self`） | 書込み済みの先頭部分を確定し、その長さを返す |

ウィンドウは先頭から連続して書き込む。確定せずに破棄した場合は何も変わらない。ユーザー定義のWriterは、標準バッファーのウィンドウを転送するか制限する。アダプターは各ユーザーウィンドウを検査し（`written == 0`、`remaining >= minimum`）、違反があれば破棄して`BufferFull`を記録する。

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

<a id="s22-10"></a>
### 22.10. 書式化と補間

<a id="s22-10-1"></a>
#### 22.10.1. Writerアダプター

`Utf8Writer`は、`T is Utf8Format`を条件とする`write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>`と、記録済みの失敗を返す`status(self: ref/Self) -> Result<(), BufferFull>`を持つ。補間や`Text`操作によるものも含め、アダプターの`write`はすべて、既に失敗があれば書式化せずその記録済み失敗を返す。なければ`format`を一度呼び、`format`が`BufferFull`を返すか入れ子のwriteがそれを記録した場合、最初の失敗を記録して返す。そのため、フォーマッターは書込み失敗を隠せない。リセットはない。失敗後はアダプターの使用を終え、必要ならバッファーを`clear`し、新しいアダプターを作る。確定済み出力は巻き戻さない。`format`の直接呼出しにはこの保護はない。補間リテラルを`write`に渡すと、先に所有文字列を作る。

`format`は入力を借用し、出力はその借用にもWriterにも依存しない。成功時の出力は完全な表現であり、同じ値と外部状態なら出力先によらず同じになる。Writerは省略した表現にせず、`BufferFull`を報告する。ユーザー定義の書式化は効果を持ってよく、一度だけ実行する。組込み書式化は、符号化した正確な長さを一度予約し（空出力なら何もしない）、完全なUTF-8を確定する。そのため`123`は3バイトの出力先に収まる。

<a id="s22-10-2"></a>
#### 22.10.2. 既定の表現

| 値 | 表現 |
| --- | --- |
| 整数、`Wrapping<T>` | ASCIIの10進数。負数だけに`-`を付け、先頭の0は付けない |
| `bool`、`char`、Unit | `true`/`false`、UTF-8符号化、`()` |
| `string`、`Utf8Slice` | NULを含む自身のバイト列 |
| `FromEnd<T>`、`Start`、`End` | `^`とオフセット（`^1`、`^-1`）、空、空 |
| `Range`、`ClosedRange`、`ResolvedRange` | 開始、`..`または`..=`、終了。`1..^1`、`..=3`、`2..`、`1..4` |

有限で0でない浮動小数点数は、自身の幅で同じ値へ丸めて戻せる最少の有効桁数を使う（最近接偶数丸め）。同数なら最も近い候補、さらに同数なら末尾が偶数の候補を選ぶ。正規化した10進指数を`e`とすると、`-4 <= e < 16`では固定小数点表記、それ以外では小文字の`e`を使う科学表記とする。`+`や指数先頭の0は付けない。小数点は`.`とし、不要な末尾の0や小数点は付けず、ロケールに依存しない。特殊値は`0`、`-0`、`Infinity`、`-Infinity`、`NaN`である（NaNの符号やペイロードによらない）。借用は適合を転送しない（補間はAdaptationする）。オブジェクトハンドル、ポインター、タプル、配列、ユーザー型に自動適合はない。

<a id="s22-10-3"></a>
#### 22.10.3. 所有文字列を作る補間

字句形式は[§2.9](#s2-9)で定める。各埋込み式は、`T is Utf8Format`の下で`write<T>(value: ref/T)`として渡す（[§10.2](#s10-2)）。所有するPlaceや一時値は、その型を`T`として共有借用する。`ref/U`はそのまま渡し、`uniq/U`は共有で再借用する。どちらも`T = U`となる。裸の式は元の値をMoveしない。`\(x@move)`はMoveした一時値を借用する。式は左から右へ一度ずつ評価し、各writeを完了してから次の式を評価する。最初の失敗で`KIMI_E_FORMAT`によりAbortし、成功すれば所有文字列を結果とする。

```kimi
let count = 3
let message = "count = \(count), range = \(1..^1)"   // "count = 3, range = 1..^1"
```

<a id="s22-10-4"></a>
#### 22.10.4. `$tryWrite`

`$tryWrite(writer, literal)`は、結果が`Result<(), BufferFull>`の組込み操作である。結合文字列は作らず、関数値でもない。最初のオペランドは`uniq/Utf8Writer`として取得し、レシーバ式ではない（[§7.3](#s7-3)）。そこに裸の所有Placeを置くとエラーとなり、借用値は再借用する。二つ目は通常またはrawの文字列リテラルで、置換の有無は問わない（文法：[§F](#sf)）。最初のオペランドを一度だけ評価し、埋込み式より先にその借用を開始する。その後、[§22.10.3](#s22-10-3)と同様に、各部分と値を左から右へ書き込む。最初の失敗時、またはアダプターが既に失敗している場合、後の式は評価しない（検査は行う）。埋込み式はアダプターやそのバッファーを使えない。一時値のスコープと、`return`、`exit`、`yield`の転送先は変わらない。

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

<a id="s22-10-5"></a>
#### 22.10.5. メモリー割当て

値のwriteが書式化する型（[§22.10.3](#s22-10-3)）が、プリミティブ整数、`Wrapping<T>`、`bool`、`char`、Unit、`f32`、`f64`なら、その値を**上限あり**とする。それ以外の型は上限なしである。

- `FixedBuffer`、または容量が十分な`HeapBuffer`への組込み書式化は、メモリーを割り当てない。
- バッファー全体が検証済みなら、`text`、`validate`、`intoText`、`intoString`は何も走査しない。
- 補間は、すべての値に上限がある場合、または上限のない値が一つの`string`か`Utf8Slice`だけで、その前がリテラルテキストだけの場合、ヒープ割当てを最大一回だけ行う。
- `Text.toString`は、組込み書式化が出力全体を一度に予約する場合、ヒープ割当てを最大一回だけ行う。サイズ計算の事前処理や再試行をせず、フォーマッターを一度呼ぶ。
- これらの上限は、結果文字列が保持する記憶領域を含む内部の出力バッファー割当てを数えるが、埋込み式やユーザーフォーマッターによる割当ては数えない。補間と`Text.toString`のそれ以外の内部バッファー割当て回数は実装定義である。実装はその割当てを省略、統合、遅延、スタック配置してよいため、割当て失敗は必ずしも発生しない。結果、出力、評価の順序と回数、ユーザーの効果はこれに依存しない。

```kimi
let s = "xyz"
let line = "abc\(s)def"     // One unbounded value preceded only by literals: at most one allocation.
```

---

<a id="s23"></a>
## 23. コンパイラーサービス



この章は、診断の分類、プログラムの検査の意味、`kimi check`と言語サーバーの外部契約、および任意提供のCompiler Server Protocolの要件を定める。これらの契約によって、どのプログラムが有効かは変わらない（[§1.3](#s1-3)）。エディターの診断は助言であり、ビルドには影響しない。

<a id="s23-1"></a>
### 23.1. 診断

<a id="s23-1-1"></a>
#### 23.1.1. 分類

診断はコード、重大度（Error、Warning、Information、Hint）、分類を持つ。各コードの重大度と分類はそれぞれ一つである。`Unsupported_Kd`など本仕様が名前を定めるコードを除き、コード、メッセージ、それらが持つ事実は実装定義である。

| 分類 | 意味 |
| --- | --- |
| `Language` | ソースが言語規則に違反している。 |
| `Proof` | 必要な事実を確立できなかった。 |
| `Unsupported` | 形式は有効だが、その実装は対応していない。 |
| `Input` | 入力、設定、依存先の内容、ロックファイルを確定できないか、それらが無効である。 |
| `Resource` | 実装定義の上限に達した（定義ごとのSemanticsケース数や生成インスタンス数など）。プログラムは有効な可能性がある。 |
| `Internal` | コンパイラーの欠陥。 |

Warningは、有効だが疑わしい形式を報告する。本仕様は一部の警告を必須とし、実装はそれ以外も報告できる。Warning、Information、Hintによってプログラムを拒否することはない。

本仕様で許可されるが実装が対応しない形式は、その位置でErrorの`Unsupported_Kd`（分類Unsupported）として報告し、Languageエラーにはしない。実装は、一部の具体的な型について有効な操作を未対応としてもよい。その場合はインスタンス化を含む具体的な使用位置でUnsupportedを報告し、適合や定義の検査は変えない。

資源上限の超過はResourceとして報告し、LanguageやProofにはせず、プログラムを無効と判定しない。

<a id="s23-1-2"></a>
#### 23.1.2. 報告

他の検査が失敗していても、独立した要件の失敗はそれぞれ報告する。失敗を診断レコードにまとめる方法（複数のSemanticsケースやインスタンス化で見つかる同じ失敗を含む）、および別の報告済み失敗だけが原因の失敗も報告するかどうかは、実装定義である。まとめ方によって受理判定は変わらない。

診断はソースと、任意でその中のスパンを指定する。どのソースにも属さない問題はソースを指定しない。人に表示する行・列は[§2.1](#s2-1)に従い、ツールのプロトコルは自身の契約が定める位置を使う。

```kimi
enum Failure
    Busy

func prepare() -> Result<i32, Failure> => .Ok(1)

func setup() -> i32
    prepare()              // Warning: Result discarded; a Warning rejects nothing.
    let text: string = 1   // Error: 1 does not fit string (Language).
    return missing         // Error: reported as well; it is independent (Language).
```

<a id="s23-1-3"></a>
#### 23.1.3. 修復候補

診断には修復候補を付けられる。検査が読み取ったソースファイルへの構造化された編集案であり、各候補は検査で確認した条件と判断できなかった条件を列挙する。判断できなかった条件を確認済みとして列挙してはならない。候補を適用すると新しい入力となり、その検証には新たな検査が必要である。修復の種類と条件はJSONスキーマの閉じた列挙で定める（[§23.3](#s23-3)）。実装がどの候補を提示するかは実装定義である。

<a id="s23-2"></a>
### 23.2. 検査

<a id="s23-2-1"></a>
#### 23.2.1. 検査単位

**検査単位**は、一つのプロジェクト、ターゲット、モード（ProductまたはTest）、Debug設定の組である。その結果は、同じ設定と入力に対する`kimi check`（Product）またはテスト準備（Test）の報告と等しい（[§20.3](#s20-3)、[§20.6.1](#s20-6-1)）。Product単位とTest単位は別々に検査する。製品の事実は製品入力だけから得る（[§18.6](#s18-6)）ため、製品検査が成功してもテスト準備が失敗することがある。

検査はプロジェクトファイル、プロジェクトディレクトリの`*.kimi`一覧（[§20.1](#s20-1)）、ソースファイル、ロックファイル、依存先の内容を読み取る。ソーステキストは[§2.1](#s2-1)に従う。

<a id="s23-2-2"></a>
#### 23.2.2. 結果と受理

Completedの結果は、その単位のすべてのLanguageエラーとProofエラーを報告する。検査対象集合の具体的なインスタンスで見つかったものも含む（[§8.15](#s8-15)）。Blockedの結果は、停止原因となったInputエラーを報告する。具体的なレイアウトと表現の義務も検査対象に含む。

結果は一つの**outcome**を持つ。

| outcome | 意味 |
| --- | --- |
| Completed | エラーの有無によらず、検査を完了した。 |
| Blocked | 入力または設定を確定できなかった。Inputエラーで理由を示す。 |
| Faulted | コンパイラー内部で失敗した。Internalエラーで理由を示す。 |

取り消された単位検査は何も報告しない。単位結果は、outcomeがCompletedでErrorを報告していなければ**受理**される。拒否された単位結果は少なくとも一つのErrorを報告する。コマンドの集約と取消しは[§23.2.3](#s23-2-3)に従う。

`kimi check`が受理したプログラムが、`emit`、`build`、`run`でLanguageエラーやProofエラーにより失敗することはない。ただし、Resource上限、ターゲットが対応しない操作（Unsupported）、ネイティブライブラリーの解決（[§20.4](#s20-4)）、ツールチェーン、出力、起動の失敗によって失敗することはある。`check`がターゲットの未対応操作（`i128`の除算など）も報告するかどうかは実装定義である。報告する場合は、同じ検査対象集合について報告する。

```kimi
func divide(a: i128, b: i128) -> i128 => a / b
// No Language or Proof error. A target without 128-bit division reports Unsupported
// when building; check may report it too.
```

<a id="s23-2-3"></a>
#### 23.2.3. 検査コマンド

`kimi check`は最初に、プロジェクトまたは暗黙ソースのパスからなる空でない集合を選ぶ（[§20.3.1](#s20-3-1)）。その後、各パスについてProduct単位を一つ検査する。この集合を選べなければコマンドはBlockedとなり、Inputエラーを報告する。選択中の内部障害ならFaultedとなり、Internalエラーを報告する。いずれも単位結果は持たない。空の選択はInputエラーである。

選択したパスを絶対パスに解決し、`.`と`..`の要素を取り除き、区切りには`/`を使う。ホストのパス比較規則で同じになる複数のパスは一つの単位を選択し、Unicodeスカラー順で最小の表記を使う。残したパスをその順序で並べる。この順序付けのためにシンボリックリンクを解決しない。単位結果は実行順にかかわらず、この順序を使う。

選択に成功したら、選択したすべての単位を検査する。プロジェクトの読込みや設定の失敗はBlocked単位、内部障害はFaulted単位となる。どちらの失敗の後も、残りの単位を続ける。各結果は自身のoutcome、受理判定、ソース表、診断を保持する。単位のターゲットを確定できなければ、`unit.target`は`--Target`の値、または空文字列となる。

コマンドのoutcomeは、Faulted単位が一つでもあればFaulted、そうでなくBlocked単位があればBlocked、それ以外はCompletedとなる。入力の選択に成功し、選択したすべての単位が受理された場合に限り、コマンドを受理する。コマンドレベルの診断は選択失敗だけを示し、単位の診断をそこにコピーしない。そのためCompletedのコマンドでも拒否される場合がある。終了コードは[§20.3](#s20-3)に従う。

すべての単位が終了してからJSON文書を公開する。公開前に取り消された場合は全結果を捨て、JSONを書かず、130を返す。公開を開始したら、その後の取消しは受け付けずに公開を完了し、コマンド結果の終了コードを使う。

<a id="s23-3"></a>
### 23.3. JSON形式の検査出力

`kimi check --Format json`（[§20.3](#s20-3)）は、一つの`kimi.check/3`コマンド文書を標準出力に書く。それ以外はすべて標準エラー出力に送る。`units`配列は各単位結果を含み、選択されたプロジェクトが一つなら要素は一つとなる。ルートはスキーマとコンパイラーの識別情報、コマンドのoutcomeと受理判定、コマンドレベルのソースと診断を持つ。最後の二つの配列は選択成功後には空となる。すべての診断、関連位置、修復は、それを含む結果のソース表を使い、他の単位の表は使わない。スキーマファイル[`schemas/check-output.schema.json`](#check-schema)は全フィールドを記述し、この章の契約の一部である。スパンは、検査が読み取ったソーステキストのUTF-16コード単位によるオフセットと長さで表す。範囲は0始まりの行と文字で表す。取消しは[§23.2.3](#s23-2-3)に従う。

フィールドや列挙値の削除、改名、意味の変更ではスキーマバージョンを変更する。省略可能なフィールドや列挙値の追加ではバージョンを維持する。

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

<a id="s23-4"></a>
### 23.4. 言語サーバー

`kimi lsp`は標準入力と標準出力を通じてLanguage Server Protocolを提供する。検査診断を公開し（位置はUTF-16コード単位）、修復候補をクイックフィックスのコードアクションとして提供でき、Hoverも提供できる。その他の機能、設定、実行時機は実装定義である。ファイルの書込み、パッケージのrestore、コードの実行は行わない。

サーバーは、開いている文書が属するプロジェクトについて、検査単位の診断を公開する。各診断は、サーバーが読み取った入力に対して同じ単位を`kimi check`またはテスト準備で処理した結果と等しい。検査には、ディスク上のファイルの代わりに、開いている文書のテキストを使う。どの単位をいつ検査し、いつ対象から外すかは実装定義である。

開いている文書は、製品ソースまたは`TestSources`（[§20.1](#s20-1)）にその文書を含むプロジェクトに属する。サーバーがそれらのプロジェクトを見つける方法と、代わりにその文書を暗黙のプロジェクト（[§20.3.1](#s20-3-1)）にする条件は実装定義である。

<a id="s23-5"></a>
### 23.5. Compiler Server Protocol

Compiler Server Protocol（CSP）は、AIエージェントを含むプログラム向けの、任意提供の構造化されたコンパイラーインターフェースである。通信プロトコルは実装定義である。提供する実装は、次の要件を満たさなければならない。

- 各ソーススナップショットを永続的な内容識別情報で識別し、各編集、結果、証拠が依拠する入力を特定する。
- 修復候補を含む編集は、それを生成したスナップショットにだけ適用し、レビュー可能なソース変更を返す。ファイルへ黙って書き込まない。修復の未判断条件は、編集後の入力を検査して確立する。
- [§23.3](#s23-3)の形式で診断を報告する。
- 構文ノードに安定したハンドルを与える。除外構文と、それを除外した最内側のディレクティブも含む（[§19.4](#s19-4)）。選択構文の意味ノードにも安定したハンドルを与える。
- すべての検査、テスト、測定を、正確なソースと設定、および結果と残る不確実性に結び付ける。
- unsafeの保証（Unsafeブロックとそれを使う操作、unsafe呼出し、`#LibraryImport`宣言）を、その義務とともに列挙する（[§5](#s5)）。一時停止点（[§24.1](#s24-1)）を、タスクスロット、およびそこをまたいで生存するLoanと所有値とともに列挙する。
- 言語規則を狭めない。

---

<a id="s24"></a>
## 24. 一時停止と非同期タスク



非同期タスクは二つの規則に基づく。[§24.2](#s24-2)は一時停止できる関数を定め、[§24.3](#s24-3)は一時停止の位置と、その間に実行できる他のタスクを定める。`Kimi.Async`の操作、取消し、`Async.run`は[§22.3](#s22-3)で定める。一つのツリーの全タスクは一つのスレッドで実行する。構造化ワーカー（[§22.3.6](#s22-3-6)）は、通常の計算を別のスレッドで実行する。この章への違反はすべてLanguageエラーである（[§23.1.1](#s23-1-1)）。

```kimi
func fetchBoth(task; a: ref/PageId, b: ref/PageId) -> Result<(Page, Page), FetchError>
    let getA = func [a] (task;) -> Result<Page, FetchError> => fetchPage(task; a)
    let getB = func [b] (task;) -> Result<Page, FetchError> => fetchPage(task; b)
    return Async.joinOk(task; getA@move, getB@move)   // Task call: the children run concurrently.
```

<a id="s24-1"></a>
### 24.1. タスクとタスクフレーム

- **タスク。** `Async.run`が開始するルート、またはKimi操作が開始する子の実行（[§22.3.2](#s22-3-2)）。ルートは新しい**タスクツリー**を開始する。子は、それを開始したタスクのツリーに属する。タスクとその子孫は、そのタスクの**部分木**を構成する。
- **タスクフレーム。** タスクスロットを持つ関数の一回の呼出しについて、入口から完了までの状態。一時停止中も含む。タスクは、自身の本体のタスクフレームと、そのタスクフレーム内で行われるタスク呼出しのタスクフレームからなる。最内側のタスクフレームが一時停止すると、そのタスクのすべてのタスクフレームが一時停止する。外側の各フレームは、次のフレームを含むタスク呼出しを実行中だからである。タスクフレームは値ではなく、Move、Copy、格納の対象にならない。[§15.6.7](#s15-6-7)の呼出し活動とは異なる。
- **タスク呼出し。** [§24.3](#s24-3)で定める呼出し。タスクフレームが一時停止できる唯一の位置であり、タスク呼出しを**一時停止点**という。

```kimi
func outer(task; delay: Time.Duration) -> ()
    inner(task; delay)               // outer's task frame waits inside this task call.

func inner(task; delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)     // Suspends inner and outer together.
```

<a id="s24-2"></a>
### 24.2. タスクスロット

関数が一時停止できるのは、パラメーターリストが`task;`と書く**タスクスロット**で始まる場合だけである。タスクスロットは型でも値でもない。束縛を宣言しないため、キャプチャー、格納、return、型引数としての束縛はできない。

<a id="s24-2-1"></a>
#### 24.2.1. 形式

- **リスト。** タスクスロットを許すのは、名前付き関数、Contractの関数要件、明示的特殊化、匿名関数のパラメーターリスト、および関数型やCallableシグネチャの関数パラメーターリストだけである（[§3.2](#s3-2)、[§8.10](#s8-10)）。構築や基底初期化子も含め、呼出しの引数リストでは、同じ位置に**タスク引数**`task;`を置ける。
- **キーワード。** `task`は文脈依存キーワードである（[§F.3.2](#sf-3-2)）。型位置で`task;`から始まるリストは関数パラメーターリストとなる（[§3.2](#s3-2)）。
- **表記。** 後ろに何もなくても`(task;)`のように`;`を必ず書く。その直後にコンマは置かない。`(task)`は`task`という名前の通常のパラメーター、引数、または括弧で囲んだ型である。
- **位置。** タスクスロットはレシーバと`!`境界の両区間より前に置き、そのいずれにも属さない（[§7.2.1](#s7-2-1)）。一つのリストに最大一つである。
- **置けない場所。** コンストラクター、外部関数宣言、Applicationの`main`、テスト関数はタスクスロットを持たない（[§6.3](#s6-3)、[§22.6](#s22-6)、[§22.4.2](#s22-4-2)、[§6.7.2](#s6-7-2)）。アクセサーと`drop`にはタスクスロットを許すリストがない（[§11.3](#s11-3)、[§16.3.1](#s16-3-1)）。属性と`$`操作の引数リスト、推論されるCaseペイロード、パターンにもない。
- **匿名関数。** 匿名関数がタスクスロットを持つのは、ヘッダーに明記した場合に限る。期待される呼出しシグネチャが補うことはない。ただし、省略されたパラメーター型は、タスクスロットの後に対応付けて引き続き補う（[§7.7.1](#s7-7-1)、[§10.5](#s10-5)）。
- **特殊化とオーバーライド。** 明示的特殊化や仮想オーバーライドがタスクスロットを持つのは、元の宣言が持つ場合に限る（[§8.13](#s8-13)、[§6.4.1](#s6-4-1)）。

```kimi
contract Reader
    func read(task; self: uniq/Self, buffer: uniq/Array<u8>) -> Result<isize, ReadError>
    func late(value: i32, task;) -> ()        // Error: the task slot comes first.

func pause(task; ! delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)

struct Job
    init(task;) => ()                         // Error: a constructor has no task slot.
```

<a id="s24-2-2"></a>
#### 24.2.2. 引渡し

呼出しは、引数リストの先頭に`task;`を書くことで、最内側の関数（匿名関数も含む）のタスクを渡す。例は`foo(task; x)`、`bar(task;)`である。呼出し先（選択した宣言、関数型、Callableシグネチャ）がタスクスロットを持つ場合に限り、タスク引数を書く。構築、修飾されたCase構築、基底初期化子の呼出し先はタスクスロットを持たない。タスク引数は、最内側の関数がタスクスロットを持つ場合にのみ書ける。ただし、その関数の既定値式や、その関数が登録する遅延ブロックの本体には直接書けない。その内側の関数式は独自の関数境界を持ち、自身のタスクスロットを渡せる（[§7.7.1](#s7-7-1)、[§16.1.1](#s16-1-1)）。したがって後処理は一時停止しない。呼出し先がタスクスロットを持ち、呼出しの最初の引数がどの束縛にも解決しない名前`task`である場合、たとえば`foo(task, x)`や`foo(task)`は、タスク引数の欠落となる。

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

<a id="s24-2-3"></a>
#### 24.2.3. パラメーターとの区別と関数グループ内の統一

タスクスロットはパラメーターではない（[§7.2.2](#s7-2-2)）。位置番号、外部名、取得モード、照合キーを持たず、引数を束縛しない（[§7.4](#s7-4)）。レシーバと通常のパラメーターの番号、個数`N`と`K`は変わらない。`func configure(task; ! count: i32)`では、`count`は通常のパラメーター0であり、`K = 0`である。タスクスロットは省略規則の入力ではなく（[§15.4.3](#s15-4-3)）、呼出しごとのOriginも持たない（[§8.10](#s8-10)）。

一つの関数グループ内（[§7.3](#s7-3)、[§9.6](#s9-6)）では、全宣言がタスクスロットを持つか、どの宣言も持たないかのいずれかとする。一つの宣言スコープ（コンテナーとその断片、プロジェクトルート、ローカルスコープ、型のメンバー、継承分を含むContractの要件）の同名宣言で構成されるグループは、その宣言位置で検査する。使用位置で集められたグループ（[§9.5.1](#s9-5-1)、[§9.6](#s9-6)）は、その使用位置で検査する。タスクスロットはオーバーロードを区別しない。そのため、`task;`の書き忘れはタスク引数の欠落であり、別のオーバーロードの呼出しにはならない。タスクスロットだけが異なる二つの宣言は、重複宣言にも該当する（[§9.1](#s9-1)）。

```kimi
contract Fetcher
    func fetch(task; self: ref/Self, id: ref/PageId) -> Page
    func fetch(self: ref/Self, id: ref/PageId, retries: i32) -> Page
                                         // Error: one group, two task-slot shapes.
```

<a id="s24-2-4"></a>
#### 24.2.4. 互換性

実装の識別（[§8.6.3](#s8-6-3)）、特殊化対象の識別（[§8.13.1](#s8-13-1)）、Callableシグネチャの互換性と共通の関数型変換（[§10.7](#s10-7)）、既知シグネチャ・固定シグネチャの照合（[§10.5](#s10-5)、[§10.8](#s10-8)）、関数型の同一性（[§3.2.1](#s3-2-1)）では、両側がタスクスロットを持つか、どちらも持たないことを要求する。それ以外のパラメーターは従来どおり比較する。一時停止しない実装でも、タスクスロットを宣言すれば、タスクスロットを持つ要件を満たせる。

```kimi
func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task; delay: Time.Duration) -> ()
    let wait = func [delay] (task; value: i32) => _ = Async.sleep(task; delay)
    runLater(wait@move)        // Error: the task slot differs from (i32) -> ().
```

<a id="s24-3"></a>
### 24.3. タスク呼出し

**タスク呼出し**は、引数リストがタスク引数`task;`で始まる呼出しである。

1. **スケジューリング。** タスクがタスク呼出し以外の呼出しを実行している間、その呼出しの開始前に開始済みだった同じツリーの他のタスクは動作しない。
2. **入れ子のツリーと子。** 呼出し中に`Async.run`が開始するツリーは、その呼出し内で呼出し先の一部として実行し、その効果はその呼出しの効果として比較する。Kimi操作が開始する子は、その操作のタスク呼出し内で実行し、その効果はその呼出しの効果として比較する（[§22.3.3](#s22-3-3)）。
3. **静的Loan。** 呼出し側の本体において、タスク呼出しは、環境効果が未知の呼出しと競合するすべての生存中の**静的Loan**（静的記憶領域に固定されたLoan）と競合する（[§15.6.4](#s15-6-4)）。呼出し先の値、レシーバ、引数が保持または依存するLoanも含む。公開された要約や利用可能な効果境界にはよらない（[§8.11.4](#s8-11-4)）。この比較は、呼出し先の効果要約には含めない。

タスク呼出し以外の呼出しは、呼出し先が一時停止できないため、呼出し先の実行中に限り進行中である。外側のツリーのタスク自体は、`Async.run`に到達した呼出しを実行中である。他のタスクがあるタスクの状態に到達できる経路は、子へ渡したLoan（子を開始する呼出しの引数依存。[§8.10](#s8-10)、[§15.6.4](#s15-6-4)）、静的記憶領域、共有アクセスだけを許す`rc`と`arc`のペイロード（[§13.5.6](#s13-5-6)）、および実装が[§22.3](#s22-3)の保証の下で管理するKimi内部の記憶領域だけである。したがって、ローカル変数とパラメーターのLoanは比較せず、一時停止をまたいで保持できる。静的記憶領域を借用するパラメーターは、呼出し側で既に比較されている。その位置は、それを渡したタスク呼出し、またはルートがそれをキャプチャーした`Async.run`呼出しである。

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

---

<a id="sa"></a>
## 付録A. コンパイラー要件

[実装仕様目次](#implementation-specification)

適合する実装は、言語規則に加えて以下の要件をすべて満たす。これらは、実装上の上限に対するResource診断（[§A.4](#sa-4)）以外に、言語の失敗動作を追加しない。

<a id="sa-1"></a>
### A.1. 決定的な結果

同じ入力（ソース、プロジェクト設定、ターゲット、モード、コンパイラー）に対し、実装は同じ受理判定、同じ診断分類と位置、およびAbort位置を含む同じプログラム動作を生成する。

- ソースの読取り順、宣言の検査順、ソースの生成順、並列処理の実行順によって結果は変わらない。受理判定はFinalityに従う（[§8.3](#s8-3)）。それ以外の検査順とスケジューリングは実装が選べる。
- 宣言ヘッダーを、実際に完了する前に完了済みとして扱わない。証拠が利用可能になる時点は[§8.5.5](#s8-5-5)と[§8.11.4](#s8-11-4)で定める。

```kimi
// a.kimi
group Area
    public func square(side: i32) -> i32 => Scale.times(side, side)

// b.kimi
group Scale
    public func times(a: i32, b: i32) -> i32 => a * b
// Accepted whichever document is read or checked first.
```

<a id="sa-2"></a>
### A.2. 以前の処理結果の再利用

コンパイラーが以前の処理結果を再利用できるのは、現在の入力を完全に解析した場合と結果（受理判定、診断分類、Abort位置を含むプログラム動作）が等しい場合だけである。Unknown、Error、未完了の検証は成功の証拠にはならない。

<a id="sa-3"></a>
### A.3. 検査を省いた受理の禁止

- 実装がプログラムを受理するのは、到達可能・到達不能なすべての本体、すべてのSemanticsケース（[§8.15](#s8-15)）、すべての選択されたソースについて、言語が要求する全検査が成功した後だけである。テストビルドではテストソースも含む。
- Unsupportedの形式（[§23.1.1](#s23-1-1)）は、検査対象集合（[§8.15.3](#s8-15-3)）のコードを生成する前に報告する。
- 除外構文には[§19.4](#s19-4)のソース検査を行う。

<a id="sa-4"></a>
### A.4. 実装上の上限

実装上の上限とそのResource診断は[§21.3.2](#s21-3-2)に従う。深い入力や大きな入力でホストのスタックを枯渇させない。深さやメモリーの上限に達した場合はResource診断（[§21.3.2](#s21-3-2)）とし、Languageエラーにも受理にもしてはならない。この実装が解析するSemanticsケースは、定義ごとに最大64個である（[§8.15](#s8-15)）。

<a id="sa-5"></a>
### A.5. 最適化とビルド設定

- コンパイラーのビルド設定（DebugやReleaseなど）、コード共有、特殊化、リンク時最適化が受理判定や動作に及ぼす変更は、最適化レベルによる変更を超えない（[§21.3.1](#s21-3-1)）。
- 生成コードがバックエンドの仮定（LLVMの`inbounds`、`nonnull`、別名属性など）を持てるのは、言語がその前提を保証する箇所だけである。
- 実装が提供するソース出力器やフォーマッターは、意味が同じソースを生成する。本体の形式、グループ化、転送先、文書の対応付けを保持する。

---

<a id="sd"></a>
## 付録D. 保留している機能



各項目は、この版の対象外の機能（[§1.3](#s1-3)）と、その境界を定める節を示す。実装状況は[STATUS.md](#status)だけに記録する。

<a id="sd-1"></a>
### D.1. 型・値・オブジェクト

- Weakの拡張：Weakビュー間の直接変換、生存確認だけの検査、unsafeな弱ポインター、所有しない参照。[§3.2.2](#s3-2-2)、[§13.6.3](#s13-6-3)
- 通常の基底変換の追加、またはその暗黙化。[§3.3.1](#s3-3-1)
- 実行時Contractビュー：指定方法と構文、ビューの関連型束縛、完全一致検査とContract検査、検査付きキャスト。消費型・ジェネリック・型関数の実行時要件。[§8.12](#s8-12)、[§13.6.5](#s13-6-5)
- オブジェクトおよび`raw`のペア層をたどる操作（`s is object`、`objectborrow`、または許可された`raw`）。[§13.5.5](#s13-5-5)
- 失敗から回復できるオブジェクト生成、アロケーターの選択、一般的な値の複製。[§13.6.2](#s13-6-2)、[§3.5.2](#s3-5-2)
- 要素生成関数による固定長配列の構築。[§4.3](#s4-3)
- 文字列の添字アクセス。[§4.7](#s4-7)
- 降順・刻み幅付き・無限の範囲（始点だけの範囲の反復を含む）、実行時に方向を選ぶ位置、ユーザー定義の位置型・範囲型、`contains`などの範囲操作、長さを持つユーザー型の位置の正規化、Contractの既定実装、実行時の範囲ビュー。[§4.6.1](#s4-6-1)、[§4.6.2](#s4-6-2)
- UniqSliceの反復用適合、重ならない領域への専用分割、`try`で始まる新たな操作、任意のポインターからの公開コンストラクターによる構築。[§4.8.4](#s4-8-4)
- 動的コレクションAPIの追加。[§4.9](#s4-9)
- カーソルのContract（`Cursor`、`UniqCursor`）と、そのアダプター`Kimi.Iteration.shared`/`uniq`（`Shared<C>`、`Uniq<C>`）。列挙にはLendingIterator、Iterator、`owning`/`borrowing`アダプターを使う。[§22.2](#s22-2)、[§4.7.1](#s4-7-1)

<a id="sd-2"></a>
### D.2. 宣言・関数・ジェネリクス

- 拡張宣言とその同一性、Contractの断片と分割。[§6.1](#s6-1)、[§6.1.2](#s6-1-2)
- 抽象関数、仮想プロパティ・アクセサー、関数自身がジェネリックな仮想関数、`final`/newslot、束縛済みの基底関数値。[§6.4](#s6-4)
- Layout、LibraryImport、Test以外の一般的な属性の意味。[§6.7](#s6-7)
- Callableの拡張：Borrowed・Exclusive・Consumingの型消去された呼出し可能型、不透明な戻り値、レシーバに依存する公開結果やレシーバを貸し出す公開結果、逃避しない宣言、キャプチャーの別名・初期化子・部分、ジェネリックなレシーバのSemanticsと`{environment}`、一般的な高階ランクのCallable契約。[§3.2.1](#s3-2-1)、[§7.7.2](#s7-7-2)、[§15.8.2](#s15-8-2)、[§15.8.3](#s15-8-3)
- Contract自身のOrigin・長さパラメーター、既定実装、外部適合、修飾された要件呼出し。[§8.4](#s8-4)
- 有界な宣言結果を超える関連型推論、同一性の前提の分解（単射性）を含む、より強い記号的な制約推論。[§8.2](#s8-2)、[§8.5](#s8-5)、[§8.3](#s8-3)
- 関数の長さ以外の値引数、定数の型引数、単独のSemanticsスロット、部分・既定・可変個数のジェネリック引数。[§4.4](#s4-4)、[§8.1](#s8-1)、[§12.5.2](#s12-5-2)
- 部分的・条件付きの明示的特殊化、特殊化の優先順位、ジェネリックコンテナーの特殊化。[§8.13](#s8-13)
- 直接一致と呼出し可能値の型消去との間のオーバーロード順位付け。[§10.7](#s10-7)
- 公開済みのPackage版とのObjectCallCompatible状態の比較。[§12.7](#s12-7)
- ソースで宣言する透過的な型別名。[§18.2](#s18-2)
- テストの構成を含む、Composition RootのEntry/Providerの宣言と選択。[§13.8](#s13-8)

<a id="sd-3"></a>
### D.3. enum・パターン・失敗

- struct、名前付きペイロード、型、OR、範囲、残余、配列のパターン。[§14.8.1](#s14-8-1)
- ガード候補のキャプチャー構文。束縛ごとの`@move`や`@ref`など、パターン内の取得選択子。[§14.8.3](#s14-8-3)、[§15.1.6](#s15-1-6)
- 他の束縛構文での一致しない場合があるパターン。[§14.8](#s14-8)
- 非網羅的な公開enum、空のenum、enumの表現とABIの保証。[§6.5.1](#s6-5-1)
- ユーザー定義のtry対応とtryブロック。[§17.3](#s17-3)

<a id="sd-4"></a>
### D.4. 所有権・寿命・効果

- 動的なMove Pathの追加。[§15.1.3](#s15-1-3)
- non-staticな型消去ビュー（ペイロードのnon-staticな依存を隠す存在型のオブジェクトビュー）、static PlaceのOrigin、Callableの直接入力の量化とOriginでパラメーター化された関連型を超える、高階ランクのOrigin。
- 破棄時の寿命要件の緩和。[§15.6.6](#s15-6-6)
- 非仮想の具体関数、プロパティ要件、関数型、open構造体の`drop`に対する効果境界（型消去後の破棄にも既知の効果を持たせるため）。`confined`と`preserves results`以外の効果の語彙、一般的な効果システム。[§8.11](#s8-11)、[§16.3.3](#s16-3-3)

<a id="sd-5"></a>
### D.5. 並行処理

- ソース上のスレッド、切り離した処理、アトミックな順序制御API、スレッド間転送・共有アクセス能力の追加、外部コードからの並行した再入。[§22.3.6](#s22-3-6)、[§22.3.7](#s22-3-7)、[§15.2.4](#s15-2-4)
- スレッドごとのI/O実行器、スレッド間チャネル、必須のI/O API。[§22.3](#s22-3)、[§22.3.6](#s22-3-6)
- スレッド間での値全体のアトミックな更新、同期を伴う`arc`ペイロードの変更。[§15.7](#s15-7)、[§13.6.2](#s13-6-2)
- ブロックする外部呼出しの委譲。[§22.3.6](#s22-3-6)

<a id="sd-6"></a>
### D.6. unsafeコード・外部関数・ランタイム

- unsafe関数型、unsafe関数値・外部関数値、外部関数の効果宣言、並行実行での生ポインター、`tryAllocate`、生バイト列からの文字列構築、共変な所有コンテナー。[§5](#s5)
- Cの集約値渡し、エクスポート、コールバック、可変長引数。[§22.6](#s22-6)
- KimigayoとC以外の構造体レイアウトモード。[§6.7.1](#s6-7-1)、[§21.1.2](#s21-1-2)
- 自然なアラインメントを超えるメモリー割当て、任意の終了コード、浮動小数点環境の制御。[§22.5](#s22-5)

<a id="sd-7"></a>
### D.7. Package・Mod・テスト

- ソースを非公開にしたバイナリー配布、安定した外部向けKimigayo ABI。[§18.5](#s18-5)
- 再エクスポート。[§18.3](#s18-3)
- ネットワークレジストリー、版の範囲指定、依存辺の設定のオーバーライド、PackageからProjectへのオーバーライド。[§18.4](#s18-4)
- Mod（ソース生成）。ホストインターフェース、マーカー登録、設定を含む。[§6.1.2](#s6-1-2)
- パラメーター化されたテスト、Suite属性、スキップ、リソースロック、Option・Resultの検証ヘルパー、文字列・コレクションの差分、添付ファイル、Abortの発生を期待するテスト、コンパイル失敗テスト、カバレッジ。[§6.7.2](#s6-7-2)、[§17.6](#s17-6)

---

<a id="se"></a>
## 付録E. 用語



読解を助ける参考資料。原語のアルファベット順に並べる。各用語の定義は、リンク先の節に従う。

| 用語 | 意味 | 定義節 |
| --- | --- | --- |
| アクセス経路 | rawでないPlaceへの到達方法。Direct、排他参照経由、共有参照経由のいずれか。Loan検査は経路の各段階を比較する（[§15.6.2](#s15-6-2)）。raw Placeにはアクセス経路がない。 | [§3.4.1](#s3-4-1) |
| 取得モード | 裸の所有Place引数にパラメーターが要求する取得方法。型の外側のSemanticsに応じ、ByValue、Shared、Exclusiveのいずれかとなる。 | [§7.4.1](#s7-4-1) |
| Adaptationの対象 | `@`が要求する対象。SemanticsとCore、内側の型、またはオブジェクトのView Target。 | [§13.5.1](#s13-5-1) |
| 保持元のアドレス | 借用や生ポインターの参照外しを経ずに`@raw`で形成するか、`during self`フィールドから読むとき、生ポインターがLoanの代わりに保持するもの。保持元へ到達するが、何とも競合せず、権限も与えない。 | [§15.6](#s15-6) |
| 許容集合 | 前提の下でペアバインダーが取り得るSemantics。 | [§8.3.4](#s8-3-4) |
| 関連型 | 各適合が確定する型。Originパラメーターを宣言できる。 | [§8.5](#s8-5) |
| 裸の取得 | `@`操作を書かない式からの取得。 | [§3.5](#s3-5) |
| 束縛集合／Origin射影 | 型の出現ごとに定まる、OriginスロットからOriginへの対応／そのスロットの一つを選ぶ操作。 | [§15.3.1](#s15-3-1) |
| 借用値 | 外側のSemanticsが`ref`、`uniq`、`objref`、`objuniq`のいずれかである値。 | [§3.4.1](#s3-4-1) |
| 組込み数値型 | 数値型、`PrimitiveInteger`が証明された型パラメーター、またはそのパラメーターを使う`Wrapping<T>`。 | [§13.3.3](#s13-3-3) |
| 呼出しレシーバ要件 | 呼出し可能な本体がレシーバに必要とする最小限のアクセス。 | [§7.7.3](#s7-7-3) |
| Callable制約 | ジェネリックな値をどう呼び出せるかを定める制約。 | [§8.10](#s8-10) |
| ケース／ペイロード | enumの選択肢／その位置付きデータ。 | [§6.5](#s6-5) |
| 表明（rawのOrigin） | unsafe文脈の`E@(raw/U during o)`で、通常の規則では期待型に合わないもの。ポインターを通じた全アクセスが`o`の覆う記憶領域内に収まるという約束。 | [§5.7](#s5-7) |
| 閉じたContract | `Position`または`PositionRange`。適合する型をKimi Kotonohaが固定する。 | [§8.7.5](#s8-7-5) |
| クロージャー／キャプチャー | 呼出し可能な本体とその環境。各キャプチャー項目は、`let x = x`または`var x = x`と同じように環境内の束縛を一つ初期化する。 | [§7.7.2](#s7-7-2) |
| 共通Adaptation | 期待型が固定された位置で選ぶ、唯一の操作。 | [§10.2](#s10-2) |
| コンパイルルート | Kotonohaごとに、そのルート宣言、直接依存の参照名、`Kimi`からなる。 | [§18](#s18) |
| コンパイラー組込みContract | Copy、Owned、Callable、Sealed、ObjectPayload、PrimitiveIntegerのいずれか。 | [§8.7.1](#s8-7-1) |
| 完全な値 | 構築が完了しており、現在も完全な値。 | [§15.1.2](#s15-1-2) |
| Composition Root | `$`。`$abort`、`$tryWrite`、`$expect`、`$require`のルート。 | [§13.8](#s13-8) |
| 条件 | 組込み条件とProject設定からなる、`bool`型の指示式。 | [§19.2](#s19-2) |
| 条件付き適合 | 条件がProvenのときに利用できる適合。 | [§8.8](#s8-8) |
| 条件付きメンバー | 型のパラメーターを制約節で制約するメンバー。その制約がProvenのときだけ適用可能。 | [§7.5](#s7-5) |
| `confined` | 効果境界。環境への効果も、未分類の効果もないことを保証する。 | [§8.11.2](#s8-11-2) |
| 競合するアクセス | 重なるPlace、または同じオブジェクトの参照カウントへの二つのアクセス。少なくとも一方が書込みで、両方とも実装内のアトミックなアクセスである場合を除く。 | [§22.3.7](#s22-3-7) |
| 適合 | 型がContractを満たすこと。 | [§8.6.1](#s8-6-1) |
| 適合の同一性 | 適合する完全な型と、束縛済みのContract参照の組。 | [§8.6.1](#s8-6-1) |
| 制約／制約節 | 型またはSemanticsに課す条件／その節`subject is requirement`。 | [§8.2.1](#s8-2-1) |
| Consume | アクセス操作の静的なEligibility検査とLegality検査。 | [§15.1.4](#s15-1-4) |
| Contract | 実装や記憶領域を持たず、要件を宣言するコンテナー。 | [§8.4.1](#s8-4-1) |
| Copy | 元をInitializedのまま残す複製。 | [§3.5](#s3-5) |
| Core | SemanticsとOriginを除いた、型の値の種類・構造・同一性。 | [§3](#s3) |
| データ競合 | 異なるスレッドによる競合アクセスで、happens-beforeによる順序がないもの。未定義動作となる。 | [§22.3.7](#s22-3-7) |
| 宣言コンテナー | 種類に応じて許可されるメンバーを持つ、名前付きの宣言スコープ。 | [§6.1](#s6-1) |
| 遅延ブロック | スコープ終了時の後処理として`defer`が登録するコード。 | [§16.1](#s16-1) |
| 診断 | 重大度と、Language、Proof、Unsupported、Input、Resource、Internalのいずれか一つの分類を持つ報告。 | [§23.1.1](#s23-1-1) |
| 直接基底呼出し | `base.f(...)`。直接の基底が保持する実装を選ぶ。 | [§6.4.4](#s6-4-4) |
| 動的型 | オブジェクトが実際に構築されたCore。 | [§3.3.1](#s3-3-1) |
| 効果境界 | 呼出しがアクセスし得るものに関する保証。要件、Callable制約、仮想スロットに宣言する`confined`または`preserves results`。 | [§8.11](#s8-11) |
| 実効型 | 絞り込み後のある時点で保証される、最も派生したCoreを持つ型。 | [§14.10.1](#s14-10-1) |
| 除外された構文 | 指示で選択されなかった構文。ソース検査だけを受ける。 | [§19.4](#s19-4) |
| フィールド | `let`または`var`プロパティの記憶領域。 | [§11.1](#s11-1) |
| 有限Origin | 上限の範囲内で、使用箇所から領域を推論するOrigin。 | [§15.6.5](#s15-6-5) |
| 固定された期待呼出しシグネチャ | その位置の期待型によって固定される、唯一の呼出しシグネチャ。共通の関数型、または未束縛スロット`F`のCallable制約が`F`、`ref/F`、`uniq/F`の位置に与える唯一のシグネチャ。 | [§10.5.2](#s10-5-2) |
| Follow | `@follow`。参照や条件を満たすペア層が指すPlace、またはView TargetがSealedと証明された型であるオブジェクトハンドルのペイロードを選ぶ。 | [§13.5.5](#s13-5-5) |
| Function Item／共通の関数型 | 環境を持たない、解決済みの一つの関数宣言／呼出し可能値が共有する、型消去された呼出し契約。 | [§3.2.1](#s3-2-1) |
| 保持元 | 局所変数・パラメーター・一時値。その各部に格納された値のLoanとアドレスを保持する。`during self`のraw層が所有する記憶領域も含み、各部が到達する、本体が所有しない記憶領域へも到達する。 | [§15.6](#s15-6) |
| 独立に型付けできる引数 | 呼出しからの期待型やリテラルの既定型を使わずに完全な型が決まる、非リテラル引数。入れ子の呼出しは、期待結果なしで適用可能な候補がちょうど一つある場合だけ該当する。匿名関数は該当しない。タプル・配列リテラル引数はいずれの分類にも属さず、その要素を分類する（[§10.5.1](#s10-5-1)）。 | [§10.5.1](#s10-5-1) |
| Indexable / UniqIndexable | `index`と`indexUniq`が、`receiver[key]`で要素へたどる参照を返すContract。 | [§4.7.1](#s4-7-1) |
| 整数／ラップアラウンド整数／数値型 | 12種類の整数Coreのいずれか／`Wrapping<T>`／整数、ラップアラウンド整数、浮動小数点数のいずれか。 | [§3.1](#s3-1) |
| Item / `LentItem(step)` | Iteratorの要素型／レシーバを借用できるLendingIteratorの要素型。 | [§22.2.1](#s22-2-1) |
| Iterable / UniqIterable / IntoIterable | Shared、Exclusive、ByValueのSubjectに応じて`for`が選ぶ入口。 | [§22.2.1](#s22-2-1) |
| Iterator / LendingIterator | 後続の`next`呼出し後も要素が有効なもの／要素がイテレーターを借用し得るもの。 | [§22.2.1](#s22-2-1) |
| Kimi Kotonoha | `Kimi`として参照する基盤モジュール。必須の宣言は[§22.1](#s22-1)に列挙する。 | [§22.1](#s22-1) |
| Kotonoha | 一つの名前付きモジュール。 | [§18](#s18) |
| 貸出し点／Loanの基点 | 借用、再借用、射影の対象となる記憶領域。 | [§3.4.2](#s3-4-2) |
| 貸出し規則 | 転送または新たな排他借用を明示しなければならない条件。 | [§15.1.5](#s15-1-5) |
| リテラル引数 | 型のないリテラル、リテラルだけの式、または結果源が全てそれらである制御フロー式。スロットの根拠にはならない。タプル・配列リテラル引数はいずれの分類にも属さず、その要素を分類する（[§10.5.1](#s10-5-1)）。 | [§10.5.1](#s10-5-1) |
| リテラルだけの式 | 型のない数値リテラル、または規定の演算子でそれらを組み合わせた整数・浮動小数点・位置・範囲の式。型に合うかどうかは別に判定する。 | [§12.3.3](#s12-3-3) |
| Loan | アクセス経路で表す借用先のPlaceとアクセスモード（`ref`または`uniq`）。借用箇所とSemanticsケースで識別する。これを保持する値が使われ得る間は生存し、アクセス経路の正確な接頭辞であるMove Pathをその場で破棄すると終了する（[§15.6.3](#s15-6-3)）。 | [§15.6](#s15-6) |
| `Loan<T>` | 記憶領域なしで借用依存を表す、サイズ0の構造体。 | [§15.3.5](#s15-3-5) |
| Move可能なPlace | `@move`で値を転送できるPlace。 | [§15.1.5](#s15-1-5) |
| Move | 値とその破棄責任の転送。Placeでは`@move`と書く。 | [§3.5](#s3-5) |
| Move Path／部分Move | 独自の初期化状態を持つ、追跡される経路／集約値の一部をMoveすること。 | [§15.1.3](#s15-1-3) |
| `noinit` | 初期格納を省く、unsafeな固定長配列の構築。 | [§4.3.4](#s4-3-4) |
| オブジェクト対象 | オブジェクト形式を形成できる対象の型。 | [§8.7.3](#s8-7-3) |
| ObjectCallCompatible | オブジェクト借用経由の呼出しにおける、レシーバを保つ保証。 | [§12.7](#s12-7) |
| ObjectPayload | 値をオブジェクトのペイロードにできることを示す。`Self is not ObjectPayload`で除外する。 | [§8.7.3](#s8-7-3) |
| ObjectViewCompatible | オブジェクトのView Targetとして使えるContract。 | [§8.12](#s8-12) |
| Origin | 領域を表す記号的な寿命。 | [§15.2.1](#s15-2-1) |
| Originパラメーター | `LentItem(step)`のような、関連型のOriginバインダー。 | [§8.5.4](#s8-5-4) |
| Origin関係 | Origin間で要求される`==`または`outlives`。 | [§15.6.1](#s15-6-1) |
| 重なり | 同一のPlace、Placeとそのインライン部分、または1バイト以上を共有するPlace。 | [§3.4](#s3-4) |
| オーバーライド | 継承した一つの仮想スロットを実装する派生側の本体。探索の層、オーバーロード、公開関数を追加しない。 | [§6.4.2](#s6-4-2) |
| Owned | non-static Originに依存しないこと。 | [§15.2.3](#s15-2-3) |
| ペアバインダー／ペア対象 | ペアパラメーター`<s/T>`の`s`／`T`。 | [§8.1.1](#s8-1-1) |
| ペア層 | 外側のSemanticsがペアバインダーである型。 | [§13.5.5](#s13-5-5) |
| パラメーター取得形状 | 同じ関数グループの重なるパラメーターに共通する、一つの取得モード。 | [§7.4](#s7-4) |
| パラメーター依存の効果 | 型パラメーターや関連型射影を使って要約に保持する効果。[§15.6.4](#s15-6-4)で広げる場合を除き、呼出し側が実際の型で合成する。 | [§15.6.4](#s15-6-4) |
| Place／記憶領域 | 型と能力を伴う、選択された記憶領域／値を保持する領域。 | [§3.4](#s3-4) |
| 位置／範囲 | `[0, L]`内に解決される境界／位置の区間。 | [§4.6.1](#s4-6-1), [§4.6.2](#s4-6-2) |
| `preserves results` | 効果境界。呼出しが、同じ要件の過去の結果のLoanと競合しないことを保証する。 | [§8.11.3](#s8-11-3) |
| PrimitiveInteger | ちょうど12種類の整数型を対象とする、コンパイラー組込みContract。 | [§8.7.4](#s8-7-4) |
| Projectルート | Kotonohaの宣言階層の最上位。 | [§18](#s18) |
| プロパティ | 記憶領域を持つ`let`/`var`メンバー、または計算メンバー。 | [§11.1](#s11-1) |
| 型を読み替えたPlace | InitializedなPlaceが保持するバイト上に形成する、Copyでサイズが正の型の一時的なPlace。 | [§5.2](#s5-2) |
| 生ポインター／raw Place | `raw/T`のアドレス／そのアドレスにあるPlaceを指す`*p`、`p[n]`、またはその一部。 | [§5](#s5), [§5.2](#s5-2) |
| 生ポインターのOrigin | `raw/T during o`。ポインターが指す記憶領域は`o`の領域、または`self`の場合は保持する値自身が所有する領域。Loanを形成せず、領域を制約しない。 | [§5.7](#s5-7) |
| 生の記憶領域 | メモリー割当てや動的コレクションの要素領域など、配置する値のPlaceを宣言しない記憶領域。 | [§3.4](#s3-4) |
| Read / Write / Take | Placeの能力。観測／初期化または置換／取出し。 | [§3.4](#s3-4) |
| 読取り型／値の読取り | Scalar型・位置型・範囲型／読取り型が必要な位置で、安全な`ref`/`uniq`層の末端の値を暗黙にCopyすること。 | [§3.5.3](#s3-5-3) |
| 再借用 | 既存の借用から導く借用。 | [§13.5.6](#s13-5-6) |
| レシーバ式 | メソッド、アクセサー、関数値の呼出しのレシーバ。 | [§3.4.1](#s3-4-1) |
| レシーバ形状 | レシーバの型のSemantics。関数グループごとに一つ。 | [§7.3.2](#s7-3-2) |
| 再帰的Contract | 適合する型の射影が再び同じContractに適合することを制約で要求するContract。具体型は適合しない。 | [§8.3.3](#s8-3-3) |
| 参照経路の選択 | 型だけに基づき、連続する安全な参照層を通じて選択すること。 | [§3.4.3](#s3-4-3) |
| 領域 | プログラム上の点の集合。`region(o)`はOrigin `o`が表す領域。 | [§15.2.1](#s15-2-1) |
| 領域の分割 | 一つの親Loanから複数の子Loanを導くこと。排他的な子同士は重ならない。 | [§15.6.3](#s15-6-3) |
| 修復候補 | 診断に任意で付ける構造化編集。 | [§23.1.3](#s23-1-3) |
| Scalar | 整数、浮動小数点、`bool`、`char`、ラップアラウンド整数のCore。 | [§3.1](#s3-1) |
| Sealed | 型が、Neverとopen構造体以外のowner Coreであるという根拠。 | [§8.7.2](#s8-7-2) |
| Semantics | 値の表現・所有権・借用・アクセスの規則。 | [§3.3](#s3-3) |
| Semanticsケース | 各ペアバインダーに許可されたSemanticsを一つずつ割り当てたもの。その下でジェネリック本体を検査する。 | [§8.15.2](#s8-15-2) |
| SourceDocument | Kotonohaの一つのソース入力。 | [§18.1](#s18-1) |
| static Loan | staticな記憶領域に基点を持つLoan。 | [§24.3](#s24-3) |
| Subject Place／Subjectモード | `match`または`for`がSubjectから初期化するPlace／それが与えるアクセス。 | [§15.1.6](#s15-1-6) |
| 対象の補完 | 実行時検査や検査付きキャストの対象に与えるOrigin束縛。 | [§13.6.5](#s13-6-5) |
| タスク／タスク木 | `Async.run`が開始するルート、またはKimiの操作が開始する子の実行／ルートとその子孫。 | [§24.1](#s24-1) |
| タスク呼出し | 引数が`task;`で始まる呼出し。唯一の一時停止点。 | [§24.3](#s24-3) |
| タスクフレーム | タスク関数の一回の呼出しの状態。一時停止中かどうかは問わない。 | [§24.1](#s24-1) |
| タスクスロット | パラメーターリストまたは関数型の先頭の`task;`。 | [§24.2](#s24-2) |
| 一時値／一時Place | 式の一時的な結果／それを実体化する記憶領域。 | [§3.6](#s3-6) |
| 型 | Semantics、対象、全てのOriginからなる完全な型。 | [§3](#s3) |
| 未定義動作 | unsafeの約束に違反した結果。 | [§1.3](#s1-3), [§5](#s5) |
| UniqSlice | Non-Copyで排他的な、連続領域のビュー。 | [§4.8.4](#s4-8-4) |
| unsafe文脈／unsafeブロック | unsafe操作の許可／その許可を字句的に与える`unsafe`。 | [§5](#s5), [§14.3](#s14-3) |
| ユーザー提供型 | 外側のSemanticsが`owner`で、組込み数値型ではないstructまたはenum。 | [§13.3.3](#s13-3-3) |
| 値の文脈／結果を捨てる文脈 | 式の結果を使う文脈／保持しない文脈。 | [§14.2.2](#s14-2-2) |
| 値の種類 | 借用値、所有Place、所有一時値のいずれか。 | [§3.4.1](#s3-4-1) |
| View Target | オブジェクトビューが公開する対象。 | [§3.3.1](#s3-3-1) |
| 仮想スロット | 束縛済みの宣言同一性を伴う、元の公開関数の契約。 | [§6.4.2](#s6-4-2) |
| 待機する引数 | リテラル引数にも独立に型付けできる引数にも該当しない引数。匿名関数、固定された期待呼出しシグネチャが必要な関数参照、独立に型付けできない入れ子の呼出しなど。外側のスロットの根拠には決してならない。タプル・配列リテラル引数はいずれの分類にも属さず、その要素を分類する（[§10.5.1](#s10-5-1)）。 | [§10.5.1](#s10-5-1) |

---

<a id="sf"></a>
## 付録F. 文法



付録Fは規範的な文法を定める。`? description ?`形式の各プレースホルダーは、それぞれが参照する節で定義する。参照のないプレースホルダーはない。この付録はトークンの境界、キーワード、句読記号、レイアウト、生成規則、優先順位を定める。意味と、ここから参照する文脈依存の制約は各章で定める。

<a id="sf-1"></a>
### F.1. 記法

- 引用符内の文字列は、そのままの構文を表す。`|`は選択、丸括弧はグループ化、`?`、`*`、`+`はそれぞれ省略可、0回以上、1回以上を表す。
- `List<X>`は`X ("," X)*`、`TrailingList<X>`は`List<X> ","?`を表す。この山括弧は文法の記法であり、引用符付きの`"<"`と`">"`はトークンである。
- `NEWLINE`、`INDENT`、`DEDENT`はレイアウトのイベント。[§F.4.2](#sf-4-2)で、これらを使って`ItemList<X>`と`IndentedList<X>`を定義する。
- 生成規則でNameを引用符付きで書いた場合（`"get"`など）、その綴りのNameに一致する。その語がキーワードになる文脈は[§F.3.2](#sf-3-2)に列挙する。
- 生成規則の末尾の`//`コメントは、その規則に課す制約である。

<a id="sf-2"></a>
### F.2. 字句文法

ソーステキストは、Name、キーワード、リテラル、句読記号というトークンの列である。符号化、空白、コメントの配置、リテラルの意味は[§2.1](#s2-1)、[§2.2](#s2-2)、[§2.6](#s2-6)〜[§2.9](#s2-9)で定める。

<a id="sf-2-1"></a>
#### F.2.1. トークン・コメント・句読記号

```ebnf
PhysicalNewline := LF | CR LF | CR
LineComment     := "//" ? PhysicalNewlineまたはファイル末尾までの文字列。 [§2.2](#s2-2) ?
BlockComment    := "/*" ? 最初の"*/"までの文字列。 [§2.2](#s2-2) ? "*/"
Token           := Name | Keyword | NumberLiteral | CharLiteral | StringLiteral | Punctuator | "_"
Keyword         := ? 予約語（[§F.3.1](#sf-3-1)）、またはその文脈内の文脈キーワード（[§F.3.2](#sf-3-2)） ?
Punctuator      := "(" | ")" | "[" | "]" | "{" | "}" | "," | ";" | "." | ":" | "::"
                 | "->" | "=>" | "@" | "#" | "$" | "?" | "!"
                 | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                 | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                 | "==" | "!=" | "<" | "<=" | ">" | ">="
                 | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^=" | "<<=" | ">>="
                 | ".." | "..=" | "&&" | "||"
```

- **最長一致。** コメントとリテラルの外では、最も長い句読記号の綴りを採用する。`a+++b`は`a`、`++`、`+`、`b`となる。列挙したトークンの列に分けられない記号はエラー。
- **ジェネリックの閉じ記号。** 型引数やジェネリックパラメーターを閉じる際、トークンが`>`で始まる場合（`>`、`>=`、`>>`、`>>=`）、先頭の`>`を閉じ記号として取り、残りのソースをそこから再び字句解析する。式のシフト演算や比較には影響しない。
- `$`と`#`は独立したトークン。`$abort`は`$`と操作名`abort`、`#if`は`#`とキーワード`if`である。
- `;`はタスクスロットとタスク引数の`task;`だけ（[§F.6.3](#sf-6-3)）、`!`はパラメーターリストの境界だけ、`{`と`}`はOriginの波括弧だけ（[§F.8](#sf-8)）に使う。それ以外の出現はエラー。
- `&&`と`||`をトークンとするのは、全ての使用をエラーとして報告するためだけである。論理演算子は`and`と`or`。

```kimi
let a: Box<Box<i32>>= make()     // `>`, `>`, `=`
let b: Box<i32>= make()          // `>`, `=`
func f() -> Box<i32>=> make()    // `>`, `=>`
let c = a1 >> 2                  // A shift: no Type arguments are open
```

<a id="sf-2-2"></a>
#### F.2.2. 名前

```ebnf
Name         := NameStart NameContinue*   // 予約語でも"_"でもない
NameStart    := "A".."Z" | "a".."z" | "_"
              | ? Unicode一般カテゴリがLu、Ll、Lt、Lm、Lo、Nlのいずれかである文字。 [§2.5](#s2-5) ?
NameContinue := NameStart | "0".."9"
              | ? Unicode一般カテゴリがMn、Mc、Nd、Pcのいずれかである文字。 [§2.5](#s2-5) ?
```

キーワードはNameの綴り全体に一致する場合だけ認識する。`ifValue`と`tryGet`はNameである。単独の`_`はNameではなくトークンであり、使用できる位置は[§2.5](#s2-5)に列挙する。NFC、除外する文字、Nameの等価性も[§2.5](#s2-5)で定める。

<a id="sf-2-3"></a>
#### F.2.3. 数値リテラル

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
TupleIndex      := DecimalDigit+   // メンバーアクセスの"."の後だけ。空白とコメントは挟んでよい
```

- リテラルは符号を含まない。先頭の`+`や`-`は演算子である。`.`は直後に10進数字がある場合だけリテラルに含む。`1.`は`1`と`.`になる。
- 数値リテラルの直後にNameの継続文字がある場合、それも取り込んで一つの不正なトークンとし、エラーにする。例：`0xg`、`0b102`、`123abc`。
- メンバーアクセスの`.`の直後（空白とコメントは無視）では、数字を小数部・指数・接頭辞・区切り文字のないTupleIndexとして読む。`pair.0.1`は`pair`、`.`、`0`、`.`、`1`となる。不正トークンの規則も適用するため、`pair.1_2`はエラー。

<a id="sf-2-4"></a>
#### F.2.4. 文字リテラルと文字列リテラル

```ebnf
CharacterEscape := "\0" | "\\" | "\e" | "\t" | "\n" | "\r" | '\"' | "\'"
                 | "\u(" HexDigit HexDigit? HexDigit? HexDigit? HexDigit? HexDigit? ")"
CharLiteral     := "'" (DirectScalar | CharacterEscape) "'"
DirectScalar    := ? 次の節で除外されていないUnicodeスカラー値一つ： [§2.8](#s2-8) ?
StringLiteral   := EscapedString | RawString
EscapedString   := '"' (StringText | CharacterEscape | Interpolation)* '"'
StringText      := ? '"'と"\"以外のUnicodeスカラー値。 [§2.9](#s2-9) ?
Interpolation   := "\(" Expression ")"
RawString       := QuoteRun RawText QuoteRun
QuoteRun        := ? 二重引用符N個（N >= 3）の連続。選択は次の節に従う： [§2.9.2](#s2-9-2) ?
RawText         := ? 終端となる連続引用符までの内容。 [§2.9.2](#s2-9-2) ?
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

<a id="sf-3"></a>
### F.3. キーワード

予約語はNameにならない。文脈キーワードは以下に示す文脈だけでキーワードとなり、その他では通常のNameとなる。認識にはトークンだけを使い、名前探索や型を使わない。

`Never`と`Transferable`は必須のKimi宣言の通常のNameであり（[§22.1](#s22-1)）、キーワードではない。通常の名前探索で隠されることがある。`::Kimi.Never`と`::Kimi.Transferable`は組込みの宣言を識別する。

<a id="sf-3-1"></a>
#### F.3.1. 予約語

| 分類 | 綴り |
| --- | --- |
| 基本型 | `isize` `usize` `i8` `i16` `i32` `i64` `i128` `u8` `u16` `u32` `u64` `u128` `f32` `f64` `bool` `char` `string` |
| 束縛と関数 | `let` `var` `func` |
| 制御・検査・リテラル | `if` `else` `case` `for` `while` `loop` `do` `match` `return` `exit` `continue` `yield` `try` `require` `defer` `is` `not` `and` `or` `true` `false` `null` |
| アクセス | `public` `internal` `private` `protected` |
| 専用構文 | `Self` `init` `drop` `base` |
| コンパイル時選択 | `switch`（`#`の後だけ。実行時のswitchはない） |
| 将来のための予約 | `as` |

<a id="sf-3-2"></a>
#### F.3.2. 文脈キーワード

この表で全てを列挙する。

| 語 | キーワードとなる文脈 |
| --- | --- |
| `alias` `rootgroup` `group` `struct` `enum` `contract` `computed` `property` | 宣言の導入語。同じ位置の`extension`は認識するが、利用できないものとして拒否する。 |
| `unsafe` `virtual` `override` `abstract` `open` | 宣言の修飾子（[§F.3.3](#sf-3-3)）。`unsafe`は、実行可能項目の先頭で直後が`=>`または行末の場合にもキーワードとなる（UnsafeStatement）。 |
| `label` | Primaryの先頭で、同じ物理行の後続トークンがNameと`:`の場合（[§14.4](#s14-4)）。 |
| `in` | `for`ヘッダーのForBindingの後。 |
| `to` | `exit`、`continue`、`yield`の直後。 |
| `task` | パラメーターリスト、引数リスト、FunctionParametersの`(`の直後の最初のトークンで、次のトークンが`;`の場合（[§24.2.1](#s24-2-1)）。 |
| `self` | ReceiverShorthand、およびAccessorReceiver内。`self`、`value`、`storage`は文脈に応じた束縛でもある（[§9.3](#s9-3)、[§11.3](#s11-3)）。 |
| `get` `set` `has` `value` | アクセサーとプロパティ要件の構文内（[§F.7](#sf-7)）。 |
| `associate` `when` `specialize` | それぞれ関連型の宣言・指定、ConditionalConformance内、`func`の直前。 |
| `Callable` | RequirementAtomまたはConformanceAtomの先頭で、直後が`<`の場合。 |
| `effect` `confined` `preserves` `results` | `effect`は、項目の先頭で、同じ行の後続トークンが`confined`か`preserves`、またはContract項目ではContractSelectorの場合。`confined`、`preserves`、`results`は`effect`の後だけ。効果項目を置けるのは、要件の制約領域、Contractの`EffectSpecification`、Callableの効果リスト、元の仮想関数の本体の先頭領域である（[§8.11.1](#s8-11-1)）。 |
| `during` `outlives` `static` | `during`はAnnotatedTypeの本体と`?`接尾辞の後。`outlives`はOriginRelation内。`static`はOriginAtomとして使う場合。 |
| `origin` | OriginRelationを許す位置で項目を開始する場合。付随する節リスト、struct/enumの本体（ContainerItem、EnumItem）、要件の制約領域、関数・コンストラクター・アクセサー本体の先頭領域が該当する（[§7.5](#s7-5)）。その位置では`origin (...) == ...`も含め、必ずOriginRelationとなる。 |
| `owner` `ref` `uniq` `obj` `rc` `arc` `objref` `objuniq` `raw` | 型、要件、CallableReceiver、キャプチャー操作、Adaptation対象内のSemanticsの位置。その他では、フィールド`raw`のようなNameである。 |
| `value` `valueborrow` `object` `objectborrow` `borrow` `owning` `reference` | 要件内のSemanticsCategory。 |
| `move` `copy` `follow` `wrap` `bits` | `@`の直後。`wrap`と`bits`は、空白やコメントを挟まずに`<`が直結する場合だけ。 |
| `abort` `tryWrite` `expect` `require` `message` | `$`の次のトークンはComposition Rootの操作名であり、予約語でも綴りで照合する（[§13.8](#s13-8)）。`message`は`$expect`と`$require`の引数ラベル。 |
| `noinit` | `=`の後の、丸括弧で囲まないInitializer全体。その位置ではNameにはならず、[§4.3.4](#s4-3-4)が許す場合だけ有効。 |
| `of` `length` | `of`は`[N of T]`と`[N of value]`のArrayLengthの後。`length`はGenericParameterの先頭。 |

`open`と`task`は、これらの文脈以外では通常のNameである。`file.open(task; path)`では、`open`はメンバー名、`task;`はタスク引数となる。

本体の先頭領域は[§7.5](#s7-5)で定める。`origin`という関数を呼ぶ式で本体を始める場合は、`(origin(b) == a)`のように丸括弧で囲む。

```kimi
func pick(a: ref/i32, b: ref/i32) -> ref/i32 during a
    origin(b) == a          // An Origin relation: it starts an item in the leading region
    return a

let effect = 1              // An ordinary Name: no bound word follows
```

<a id="sf-3-3"></a>
#### F.3.3. 宣言の修飾子

宣言ヘッダーは、属性、アクセス指定、高々一つの修飾子、導入語の順に書く。修飾子は、`func`の前の`unsafe`・`virtual`・`override`、または`struct`の前の`open`。同じ位置の`abstract`は認識するが、利用できないものとして拒否する。`unsafe`、`virtual`、`override`、`abstract`、`open`のいずれかの後から同じ行の宣言導入語までに、アクセス指定語とこれらの語しかない場合、その語は修飾子となる。他の順序、重複、二つの修飾子、修飾子を許さない宣言への指定はエラー。その他では、`func virtual()`、`let override: i32`、`file.open(path)`のような通常のNameである。`virtual`や`override`にできる関数は[§6.4](#s6-4)、複合アクセス指定は`Access`生成規則（[§F.6.1](#sf-6-1)）に従う。

```kimi
public open struct Shape
    public virtual func area(self) -> f64 => 0.0
    virtual public func size(self) -> i32 => 0   // Error: the access word comes first
```

<a id="sf-4"></a>
### F.4. レイアウト

<a id="sf-4-1"></a>
#### F.4.1. 行・字下げ・本体

- 字下げの1段はU+0020の空白4個。コードの字下げは4の倍数でなければならない。
- **有効行**は、コメントやリテラル内部のテキストの外でトークンを開始または継続する行。空行とコメントだけの行は有効行ではなく、レイアウトのイベントを生成せず、本体や分岐の接続を終えない。
- 一つの複数行リテラル内で、補間の外にある行は有効行ではなく、字下げを検査しない。
- 補間`\( … )`はリテラル内のコードであり、`\(`が区切りを開く。その外側の段は、文字列リテラルが始まる有効行の字下げとする。`\(`の行で始まる構文は、その段をヘッダー基準位置に使う。補間内の後続行は全て有効行であり、通常のレイアウト規則で検査する。
- 構文の**ヘッダー基準位置**は、ヘッダーが始まる物理行の字下げであり、キーワード・ラベル・継続行の列位置ではない。字下げ本体は、その基準位置よりちょうど1段深く始める。matchアームのリストは`match`のヘッダー基準位置より1段深く、字下げアーム本体はそのアームより1段深くする。
- 単一項目形式`=> Item`では、`=>`と項目の先頭を、ヘッダーが終わる物理行に置く。直後が行末の`=>`、継続行の先頭の`=>`、本体の前の`:`はエラー。

```kimi
let label = "mode: \(match mode
    .Fast => "fast"
    .Slow => "slow"
)"
```

<a id="sf-4-2"></a>
#### F.4.2. レイアウトのイベント

レイアウト規則は行構造を`NEWLINE`、`INDENT`、`DEDENT`というイベントに変換する。

- 区切りの外では、有効行が1段深い本体を開くと`NEWLINE INDENT`、浅くなると閉じる本体の段ごとに`DEDENT`を生成してから`NEWLINE`、同じ段なら`NEWLINE`を生成する。継続行（[§F.4.3](#sf-4-3)、[§F.4.6](#sf-4-6)）はイベントを生成しない。
- 区切りの内側では、改行はイベントを生成しない。ただし、その区切り内に入れ子になった本体は、自身のヘッダー基準位置を基準に上記のイベントを生成する。その本体を閉じる行は、対応する`DEDENT`だけを生成する。
- ソースの最初の有効行はイベントを生成しない。
- ファイル末尾では、開いている本体を`DEDENT`で閉じる。閉じていない区切り、開いている本体に存在しない段への字下げ解除、本体を開かず行も継続しない字下げの増加はエラー。

```ebnf
ItemList<X>     := X (NEWLINE X)*
IndentedList<X> := NEWLINE INDENT ItemList<X> DEDENT
```

**本体**とは、文法が`IndentedList<X>`を許す全ての位置を指す。実行可能な本体、matchアームのリスト、コンテナー本体、付随するOrigin節・アクセサー・効果のリスト、要件の制約領域、`#switch`のケースリストが該当する。行が本体を開くかどうかは、継続を検討する前に文法で決める。生成規則が明示する`NEWLINE`（AttributePrefix、IfDirective、BranchJoin、RequireJoin）は、その規則が消費する。字下げ本体の内容と同じ行にあるコンマ、`!`、閉じ記号は、本体を閉じない。先に後続行で字下げを解除して閉じなければならない。

<a id="sf-4-3"></a>
#### F.4.3. 区切りと継続

- 区切りは丸括弧、角括弧、Originの波括弧、および型の位置にある型引数・ジェネリックパラメーターの山括弧。比較の`<`は区切りを開かず、式内の型引数は行をまたがない（[§F.9.3](#sf-9-3)）。
- 区切り内で継続する内容は、現在の本体またはチェーンの段から、開いた区切り一つにつき1段字下げする。対応する閉じ記号は、開き記号の行または内容の字下げにそろえてよい。
- 区切りの外で式が次行に続くのは、先頭が`.`のチェーン行だけ（[§F.4.6](#sf-4-6)）。ヘッダーが続くのは、先頭が`->`の行だけ。行末の二項演算子や`=`は行を継続せず、不完全な式としてエラーになる。二項演算子で始まる行も前行を継続しない。
- 区切りの内側で、その中の入れ子の本体の外では、継続行を二項演算子で始めて式を続けられる。この位置の先頭の`+`や`-`は、同じ要素内に先行するオペランドがあれば二項演算子となる。
- 先頭の`->`が関数・アクセサーのヘッダーを継続するのは、文法がまだ`->`を期待する位置だけ。その行はヘッダー基準位置より1段深くするが、基準位置自体は変えない。`->`行で開いた区切りの内容は、その行より1段深くする。次の有効行が`->`行と同じ段か浅い段なら、本体または次の項目を始める。

```kimi
let ok = (ready
    and enabled)        // Continues inside the parentheses
let diff = (a
    - b)                // Binary subtraction
let total = first +     // Error: incomplete; a line end does not continue outside delimiters
    second
```

<a id="sf-4-4"></a>
#### F.4.4. ヘッダー式と区切り領域

**ヘッダー式**は、構文ヘッダーの末尾で、本体やアームリストにつながる式を指す。`if`、`else if`、`while`、`require`の条件、`match`の対象、`for`の`in`式、matchアームのガードが該当する。本体を持つ式（`if`、`match`、`for`、`while`、`loop`、`do`、ラベル付き構文、匿名関数）をヘッダー式内に置けるのは、そのヘッダー式内で開いた区切りの中だけである。グループ化の丸括弧、引数リスト、タプル・配列・辞書の要素が該当する。パラメーターの既定値はヘッダー式ではない。ヘッダー式の行継続もそのような区切り内に限る。構文全体を括弧で囲んでもヘッダーは継続しない。

**区切り領域**は、グループ化された式、丸括弧・角括弧内の各引数や要素（閉じ記号または区切り記号で終わる）、字下げ本体の各項目、matchの各アームから始まる。ラベル、制御転送のオペランド、連続する`=>`は領域を開始しない。

- 単一項目本体内では、同じ領域内でBodyを持つ構文（本体付きの式、`unsafe`、`defer`、`require`の`else`）も単一項目本体にする。matchアームのリストは実行可能な本体ではなく、各アームが領域を開始するため、そのmatchには字下げアーム本体を置ける。
- 単一項目形式の`if`・`else if`・`else`本体内で同じ領域にある`if`は、自身の`else`の有無にかかわらず全体を丸括弧で囲む。
- 文を式として丸括弧で囲むことはできない。必要な括弧の欠落、対応する`if`のない`else`は構文エラー。

```kimi
if items.any(func (x: i32) => x > 0) => work()   // The argument list delimits the body
if (if a => b else => c) => work()                 // Grouped
let value = if a => (if b => 1 else => 2) else => 3
if do => ready() => work()                         // Error: body-bearing expression in the header
defer => unsafe
    releaseRaw(pointer)                            // Error: unsafe needs a single-item body here
```

<a id="sf-4-5"></a>
#### F.4.5. 分岐の接続

`BranchJoin`と`RequireJoin`は`NEWLINE?`である。`else`は項目を開始できないため、接続は曖昧にならない。`else`または`else if`は、単一項目本体が終わる物理行か、次の有効行に続く。字下げ本体の後では、その行の字下げ解除によって先に本体を閉じる。独立した行に置く節は、最初の`if`のヘッダー基準位置にそろえ、間に別の項目を挟まない。`require`の`else`は、条件と同じ行か次の有効行に置き、`require`にそろえる。

```kimi
func run(ready: bool, waiting: bool, valid: bool)
    if ready => start()
    else if waiting => wait()
    else
        stop()
    require valid else => return
```

<a id="sf-4-6"></a>
#### F.4.6. 付随リストと先頭ドット行

任意の付随リスト（Origin節、格納プロパティのアクセサー、Callableの効果リスト）が始まるかどうかは、1段深い次の有効行の最初のトークンで決める。そのトークンが対応する項目を開始できる場合だけ始まる（`origin`、アクセス指定語または`get`・`set`、`effect`）。`.`で始まる行は、このようなリストを開始しない。

matchアームの位置にある先頭の`.`はケースパターンを開始する。新しい実行可能本体の最初の項目では、InferredCaseExpressionを開始し得る。どちらもヘッダーの継続ではない。それ以外では、直前の項目よりちょうど1段深く、単独の`.`で始まる行は、その式をメンバーチェーンとして継続する。後続のチェーン行も同じ段に置く。範囲トークンは単独のドットではない。これらの判断には期待型も名前探索も使わない。

```kimi
let view = makeView()
    .trimmed()          // Chain continuation, not an Origin clause

struct Counter
    var count: i32 = 0
        private set     // Starts the accessor list
```

<a id="sf-5"></a>
### F.5. 型

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

- `Name "/" Name`は、ペアバインダーとペア対象からなるペアパラメーターを宣言する（[§8.1.1](#s8-1-1)）。`length Name`は関数の長さスロットを宣言する（[§4.4](#s4-4)）。Nameまたはグループ化された式であるGenericArgumentは、使用箇所ではなく、宣言されたスロットの種類で分類する（[§4.4](#s4-4)）。
- ArrayLengthまたはArrayElementTypeとしての`_`は、初期化子のある局所変数の型注釈だけで有効（[§4.3.1](#s4-3-1)）。
- 借用注釈の適用先は[§3.3.2](#s3-3-2)で定める。`ContainerPath`は、[§9.7.1](#s9-7-1)と[§8.5](#s8-5)が許す位置でContractや関連型を指せる。名前付きの対象は実行時Contractビューを表し得る（[§8.12](#s8-12)）。
- BoundContainerQualifierは型側だけの形式であり、Adaptation対象では使えない。
- OriginApplication、OriginBindingSet、BorrowOriginは[§F.8](#sf-8)で定義する。

```kimi
func first<T>(values: ref/Array<T>) -> ref/T during values
    return values[0]@ref

let grid: [2 of [3 of i32]] = [2 of [3 of 0]]
let check: (i32) -> bool = func (x: i32) => x > 0
let maybe: ref/i32? = Option<ref/i32>.None
```

<a id="sf-6"></a>
### F.6. 宣言

<a id="sf-6-1"></a>
#### F.6.1. ソース単位・コンテナー・属性

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
AttributeName        := ? 先頭文字の一般カテゴリがLuであるName。 [§2.5](#s2-5) ?
AttributePrefix      := Attribute (Attribute | NEWLINE)* ("#" "if" CompileCondition NEWLINE)?   // 属性は#ifが制御する一つの宣言に付く。 [§6.7](#s6-7)
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

- ソースのルートでは`let`と`var`はLocalBindingである。各コンテナーが許す項目、入れ子、コンテナー本体の省略・空の本体は[§6.1.1](#s6-1-1)で定める。enum本体は選択後も空であってはならない。
- ルートの`SpecializationDeclaration`は、同じSourceDocument実行スコープ内の関数を対象とする（[§8.13.1](#s8-13-1)）。
- 別名はソースのルートで、他の項目より前にだけ置ける（[§18.2](#s18-2)）。別名の`=>`は参照を導入し、本体を導入しない。
- 修飾子の順序と認識は[§F.3.3](#sf-3-3)に従う。
- `#`形式は次のとおり。後続トークンが`if`、`switch`、`case`なら指示（[§F.11](#sf-11)）、Lu文字で始まるNameなら属性、それ以外の`#Name`はエラー。属性の配置と対象は[§6.7](#s6-7)で定める。

```kimi
rootgroup App.Tools
    #Test
    func check() => ()
    // #test              // Error: an Attribute Name starts with a Lu letter
```

<a id="sf-6-2"></a>
#### F.6.2. Contract・要件・適合

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
EffectBound          := "confined" | "preserves" "results"
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

- 要件の`is`は右辺を`or`まで取り込み、`is`の直後の`not`はその全体を否定する。`T is not A or B`は`T is not (A or B)`を意味する（[§8.2](#s8-2)）。BorrowOriginの後の外側の`and`は要件に属する：`T is ref/U during a and Copy`。
- 制約節、効果項目、OriginRelationの配置は[§7.5](#s7-5)、[§8.2](#s8-2)、[§8.11.1](#s8-11-1)に従う。関連型は[§8.5](#s8-5)、条件付き適合は[§8.8](#s8-8)に従う。

```kimi
contract Source
    associate Element
    func read(self: uniq/Self) -> Element

func apply<F>(f: ref/F, x: i32) -> i32
    F is Callable<(i32) -> i32>
        effect confined
    return f(x)
```

<a id="sf-6-3"></a>
#### F.6.3. 関数・パラメーター・束縛

```ebnf
FunctionHeader       := Access? FunctionModifier? "func" Name GenericParameters?
                        ParameterList<Parameter> ("->" Type)?
FunctionModifier     := "unsafe" | "virtual" | "override"
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader   // #LibraryImportを伴う。 [§22.6](#s22-6)
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

- 関数本体の先頭領域には、ConstraintClause（効果リストを含む）、OriginRelation、元の仮想関数ではEffectClauseを任意の順に置ける。文法はこれらを項目として許し、[§7.5](#s7-5)が配置を制限する。誤った位置の制約のエラー、アクセサーとデストラクターの制限も含む。
- `ReceiverShorthand`は[§7.3](#s7-3)がレシーバを許す位置だけで有効。`!`境界は[§7.2.1](#s7-2-1)で定める。
- タスクスロットを許すリストは[§24.2.1](#s24-2-1)で定める。
- ForeignFunctionDeclarationは本体を持たず、[§22.6](#s22-6)が許す位置にだけ置ける。明示的特殊化は[§8.13](#s8-13)、コンストラクターは[§6.3](#s6-3)、`drop`は[§16.3](#s16-3)に従う。
- `func`の後では、Nameは関数定義を、`(`または`[`はFunctionExpressionを開始する（[§F.9.1](#sf-9-1)）。

```kimi
func find(task; value: i32 ! start: i32, end: i32) -> isize => 0
func scale(value: f64, by => factor: f64 = 2.0) -> f64 => value * factor

func fill()
    unsafe
        var buffer: [4 of u8] = noinit
        buffer[0] = 1
func f(x: i32 = noinit) => x       // Error: noinit is only for a local var
```

<a id="sf-7"></a>
### F.7. プロパティ

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

各アクセサーは高々一つで、順序は任意。アクセサーの規則、レシーバの省略、プロパティの種類は[§11.3](#s11-3)、プロパティ要件は[§11.9](#s11-9)で定める。

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

<a id="sf-8"></a>
### F.8. Origin

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

- OriginHeaderはstruct/enumのジェネリックパラメーターの後に置く。OriginBindingSetは名前付き型の後に置き、一つの束縛集合に名前を付ける（[§15.3.1](#s15-3-1)）。OriginApplicationは型内の関連型名の後だけに置ける（[§8.5.4](#s8-5-4)）。呼出し、タプル、グループ化にはならない。
- `during`の後の共通部分は`during (a and b)`のように丸括弧で囲む。関係のオペランドには完全なOriginExpressionを使う。
- OriginRelationは宣言のメタデータであり、対象宣言の1段下に付けるか、先頭領域に置く（[§15.3.3](#s15-3-3)、[§7.5](#s7-5)）。

```kimi
struct View<T> {source}
    let items: ref/Array<T> during source

func pick<T>(a: ref/T, b: ref/T) -> ref/T during a
    origin b outlives a
    T is Copy                  // Still in the leading region
    return a
```

<a id="sf-9"></a>
### F.9. 式

<a id="sf-9-1"></a>
#### F.9.1. 生成規則

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
                      Type arguments, [§13.6.4](#s13-6-4) ?
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
AdjacentTypeArguments := TypeArguments   // 次の節で判定する場合だけ： [§F.9.3](#sf-9-3)
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

- 型修飾子の後の`.init`は常にConstructionExpressionになる（[§6.3](#s6-3)）。修飾されたenumケースは通常のPostfix形式で、[§6.5.2](#s6-5-2)に従って解決する。
- `("Self" | PrimitiveType) "." Name`は、`Self.marker()`や`isize.MaxValue`のように、現在の型または基本型のstaticメンバーを選ぶ（[§9.6](#s9-6)）。
- 実行時の`is`は一つのNamedCoreTypeを取り、外側の`and`や`or`を取り込まない。要件の`is`は、その文脈でIsRequirement（[§F.6.2](#sf-6-2)）を使う。
- `return`、`exit`、`yield`のオペランドは完全なExpressionである（[§F.10.2](#sf-10-2)）。

```kimi
struct Counter
    var count: i32 = 0
    func total(self) -> i32 => self.count + Self.marker()
    func marker() -> i32 => 1

let entry = Pair<i32, string>.init(1, "one")
let scaled = apply(func [factor] (x: i32) => x * factor)
let table = [1: "one", 2: "two"]
```

<a id="sf-9-2"></a>
#### F.9.2. 優先順位と結合規則

表の上の行ほど強く結合する。[§F.9.1](#sf-9-1)の文法は、この表を表現している。

| 優先度 | 演算子・構文 | 結合規則 |
| --- | --- | --- |
| 1 | `.name`、`(...)`、`<Types>`、`[...]`、`@follow`、後置`++` `--` | 後置チェーン、左から右 |
| 2 | 前置`+` `-` `not` `*` `^` `++` `--` | 右 |
| 3 | `@follow`以外の`@`操作（`@raw`、`@raw/U`、`@wrap<U>`、`@bits<U>`を含む） | 左 |
| 4 | 前置`try` | 右 |
| 5 | `*` `/` `%` | 左 |
| 6 | `+` `-` | 左 |
| 7 | `<<` `>>` | 左 |
| 8 | `&` | 左 |
| 9 | `^` | 左 |
| 10 | `\|` | 左 |
| 11 | `<` `<=` `>` `>=` `==` `!=`、実行時の`is`と`is not` | 非結合 |
| 12 | `and` | 左 |
| 13 | `or` | 左 |
| 14 | `..` `..=` | 非結合 |
| 15 | `=`と複合代入 | 右 |

- `a < b < c`や`a < b == flag`のように、丸括弧で囲まない比較の連鎖は構文エラー。
- 優先度2の前置演算子は`try`を直接取れない。`-(try x)`と書く。

| 書いた形式 | グループ化 |
| --- | --- |
| `flags & mask == 0` | `(flags & mask) == 0` |
| `-x@i64` | `(-x)@i64` |
| `*p@raw` | `(*p)@raw`。指示先のアドレス |
| `r@follow.x` | `(r@follow).x` |
| `try x@move * 2` | `(try (x@move)) * 2` |
| `not ready and n != 0` | `(not ready) and (n != 0)` |
| `start + 1..end - 1` | `(start + 1)..(end - 1)` |

<a id="sf-9-3"></a>
#### F.9.3. 型引数と小なり演算子の区別

式内のNameの後の`<`（後置接尾辞内、`base.f`の後、または`@`に続くAdaptation対象の型名の後）が型引数を開始するのは、以下の全てを満たす場合に限る。

1. Name、`Self`、基本型名の直後に、空白もコメントも挟まずに置かれている。
2. 同じ行の後続トークンが、対応する`>`で閉じた`TypeArguments`を形成する。`>`で始まるトークンは[§F.2.1](#sf-2-1)のジェネリック閉じ記号の規則に従って分割する。
3. その`>`の次のトークンが`(`、`.`、または式を開始できないトークン（`)`、`,`、`]`、`==`、`@`、`=>`、行末など）である。

それ以外では`<`は小なり演算子となる。この判定はトークンだけを使い、名前探索を使わない。後続の失敗でも判定を変更しない。型注釈、`is`のオペランド、`@wrap`や`@bits`の型引数を含む他の全ての位置では、型名の後の`<`は常に型引数を開始する。開いた形式のAdaptation対象の後では、続く`(`や`[`は引き続き構文エラーである（[§F.9.4](#sf-9-4)）。

```kimi
let a = f<i32>(x)        // Type arguments
let b = i<n              // Less-than: no matching >
let c = (p<q, r>(s))     // The generic call p<q, r>(s)
let d = (p < q, r > s)   // Two comparisons: the first < is preceded by a space
let e = x@Box<i32> == y  // Type arguments: == cannot begin an expression
let g = (x@Box<i32>) + 1 // Grouped: + could begin an operand
```

<a id="sf-9-4"></a>
#### F.9.4. Adaptationの対象

```ebnf
ClosedTarget     := "move" | "copy" | BuiltinSemantics          // 後ろに"/"が続かない
                  | (Semantics "/")* DelimitedTarget
OpenTarget       := "wrap" TypeArguments | "bits" TypeArguments
                  | (Semantics "/")* "::"? TypeSegment PathSuffix* OriginBindingSet? "?"*
                  | (Semantics "/")* DelimitedTarget "?"+
DelimitedTarget  := UnitType | "(" Type ")" | TupleType | "[" ArrayLength "of" Type "]"
```

- 後置接尾辞（`.`、呼出し、添字、`++`、`--`、`@follow`）を続けられるのはClosedTargetだけ。OpenTargetの後では`(`と`[`は構文エラーとなり、`.`は修飾された型の先頭部分を続ける。`(x@T).name`、`(f@T)()`、`(a@T)[0]`と書く。
- `@`の後では、NameまたはBuiltinSemanticsの語に`/`が続くと、空白の有無にかかわらずSemantics接頭辞になる。そのため`a@T / b`は対象`T/b`を指定する。`move`、`copy`、`follow`はNameにはならず、`follow`は対象にもならない。`move`と`copy`の後の`/`は構文エラーで、`follow`の後では除算となる。グループ化しない対象は外側の関数型の矢印を取らず、グループ化しない型の先頭部分はOriginApplicationもBorrowOriginも取り込まない。`x@(Self.Item(a))`のように対象を丸括弧で囲む。単独の`raw`はアドレス操作（[§5.4](#s5-4)）。各形式の意味は[§13.5.1](#s13-5-1)で定める。

```kimi
let n = tasks@uniq.length         // Closed target: the postfix chain continues
let x = (point@Point).x           // Open target: group before selection
let w = big@wrap<u8>
let e = items@Array<i32>[0]       // Error: group the adaptation first
```

<a id="sf-10"></a>
### F.10. 文・本体・パターン

<a id="sf-10-1"></a>
#### F.10.1. 本体と文

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

- SingleItemには宣言も指示も含めない。TestVerificationは独立した項目であり、オペランドにはならない（[§17.6](#s17-6)）。
- 本体の意味と、空の本体を禁止する規則は[§14.2](#s14-2)、[§14.2.1](#s14-2-1)に従う。

```kimi
defer => close(file)
_ = compute()
unsafe
    releaseRaw(pointer)
```

<a id="sf-10-2"></a>
#### F.10.2. 制御構文

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

- `label`、ラベル名、`:`、構文のキーワードは同じ物理行に置く（[§14.4](#s14-4)）。
- matchアームのリストは空にできず、指示も置けない。
- 丸括弧付きのForBindingはタプルの規則に従う。`(a, b)`はペアを、`(a,)`は1要素タプルを分解する。`(x)`は構文エラー（[§14.6.1](#s14-6-1)）。
- `to Label`とオペランドはキーワードの行で始める。オペランドを省略するのは、次のトークンが行末、ファイル末尾、レイアウト境界、または外側の`,`、`)`、`]`、`else`、`=>`、パラメーターリストの`!`である場合に限る。`:`や`and`では省略しない。ラベルとオペランドの最初のトークンは、空白またはブロックコメントで区切る。`exit to work (x)`と書き、`exit to work(x)`とは書かない。意味は[§14.5.1](#s14-5-1)に従う。

```kimi
label outer: for (key, value) in table
    if key == 0 => continue to outer
    match value
        .Some(let v) if v > 0 => use(v)
        _ => ()
for (only,) in singles => use(only)
// for (x) in xs => use(x)            // Error: write `for x in xs`
```

<a id="sf-10-3"></a>
#### F.10.3. パターン

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
NonInterpolatedString := ? Interpolationを含まないStringLiteral。 [§14.8.1](#s14-8-1) ?
```

CaseReferenceの修飾子には、enum自身のOriginの束縛集合も`during`も書かない。その型引数内の型は完全な型である。パターンの意味は[§14.8.1](#s14-8-1)に従う。

```kimi
match outcome
    .Done((let a, 0)) => use(a)
    Outcome.Failed(let e) => report(e)
    _ => ()
```

<a id="sf-11"></a>
### F.11. コンパイル時指示

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
PlainString           := ? Interpolationを含まないStringLiteral。 [§19.2](#s19-2) ?
```

- `#case _`は高々一つで、最後のアームに置く。
- `Item`は周囲のリストの種類を引き継ぐため、指示によって、そのリストが禁止する項目を許可することはない。指示は上記で`Directive<…>`を明示する項目リストだけに置ける。該当するリストと選択の規則は[§19.1](#s19-1)で定める。`#switch`のアームリスト外の`#case`はエラー。
- 除外された構文も同じ文法で解析する（[§19.4](#s19-4)）。

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

---

<a id="check-schema"></a>
## 検査出力のJSONスキーマ

規範的なツール契約（§23）。説明文と表題だけを訳し、キー・列挙値・制約は原文のままとする。

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://kimigayo.dev/schemas/check-output.schema.json",
  "title": "kimi checkの出力（kimi.check/3）",
  "description": "一つのコマンド結果と、順序付きの検査単位結果の配列。選択したProjectが一つなら配列は1要素となる。入力選択に失敗した場合、単位はない。各結果の診断・関連位置・修復のソース番号は、その結果のsourcesだけを参照する。spanはUTF-16コード単位、rangeは0始まりの行と文字で表す。公開前に取り消された場合、文書を出力しない。集約・順序・公開はSPEC §23.2.3に従う。",
  "type": "object",
  "required": ["schema", "compiler", "outcome", "accepted", "sources", "diagnostics", "units"],
  "additionalProperties": false,
  "properties": {
    "schema": { "const": "kimi.check/3" },
    "compiler": { "type": "string", "description": "全単位に共通するコンパイラービルドの識別情報。" },
    "outcome": { "$ref": "#/$defs/outcome", "description": "選択失敗時は、InputならBlocked、InternalならFaulted。選択後は、Faultedの単位が一つでもあればFaulted、そうでなくBlockedがあればBlocked、それ以外はCompleted。" },
    "accepted": { "type": "boolean", "description": "選択が成功して単位が一つ以上あり、選択した全単位が受理された場合に限りtrue。" },
    "sources": { "$ref": "#/$defs/sources", "description": "コマンド単位の選択診断のソース。選択成功後は空。" },
    "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" }, "description": "選択失敗だけを収録する。BlockedならInput Error、FaultedならInternal Error。選択成功後は空で、単位の診断をここへ重複して収録しない。" },
    "units": { "type": "array", "items": { "$ref": "#/$defs/unitResult" }, "description": "選択した全単位をSPEC §23.2.3のパス順で収録する。BlockedやFaultedの単位も含む。選択失敗時だけ空。" }
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
            "project": { "type": "string", "description": "正規化された選択Projectのパス、または暗黙のソースパス。" },
            "target": { "type": "string", "description": "選択したターゲット。単一のターゲットを選べなかった場合は、--Targetの値または空文字列。" },
            "mode": { "enum": ["Product", "Test"], "description": "Productはkimi checkの結果、Testはテスト準備の結果。" },
            "debug": { "type": "boolean" }
          }
        },
        "outcome": { "$ref": "#/$defs/outcome", "description": "Completedは検査済み（エラーの有無は問わない）。Blockedは入力または設定を確定できなかった状態。Faultedはコンパイラー内部の失敗。" },
        "accepted": { "type": "boolean", "description": "この単位のoutcomeがCompletedで、重大度Errorの診断がない場合に限りtrue。拒否された単位には少なくとも一つのErrorがある。" },
        "testPresence": { "enum": ["Yes", "No", "Unknown"], "description": "Product単位では、Project自身のソースに#Test関数があればYes、なく、全ソースをソースエラーなしで解析できた場合はNo、それ以外はUnknown。Test単位では常にUnknown。" },
        "sources": { "$ref": "#/$defs/sources" },
        "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" } }
      }
    },
    "sources": {
      "type": "array",
      "description": "この結果の診断が参照する全ソースを読込み順に並べる。番号はこの結果の内側だけで有効。",
      "items": {
        "type": "object",
        "required": ["path", "isInput"],
        "additionalProperties": false,
        "properties": {
          "path": { "type": "string", "description": "表示用のパス。" },
          "isInput": { "type": "boolean", "description": "検査がファイルまたは開いている文書から読んだ入力ならtrue。組込みソースや生成ソースならfalse。" },
          "sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$", "description": "入力バイト列のハッシュ。読取り可能だった入力に付ける。" }
        }
      }
    },
    "span": {
      "type": "object",
      "description": "UTF-16コード単位で表す開始オフセットと長さ。長さ0は挿入位置を示す。",
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
      "description": "0始まりの行とUTF-16文字位置。終端を含まない。",
      "required": ["start", "end"],
      "additionalProperties": false,
      "properties": { "start": { "$ref": "#/$defs/position" }, "end": { "$ref": "#/$defs/position" } }
    },
    "value": {
      "type": "object",
      "description": "理由または修復に含まれる、型を持つ事実一つ。",
      "required": ["name", "kind", "value"],
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string" },
        "kind": { "enum": ["Number", "Enumeration", "Boolean", "Text", "Requirement", "Origin"] },
        "value": { "type": "string" },
        "origin": { "enum": ["expression", "borrow", "omitted", "closure"], "description": "kindがOriginの場合に限り存在し、valueは固定Originまたは有限Originを表す。expressionはシグネチャに書かれたOrigin式、borrowは借用またはキャプチャー項目のソーステキスト、omittedは無名Originの型の出現箇所またはペアバインダー宣言、closureはクロージャーの呼出しレシーバまたは推論された呼出し結果。" },
        "elided": { "type": "boolean", "description": "表示のために値を短縮した場合はtrue。" }
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
        "source": { "type": "integer", "minimum": -1, "description": "ソース表の番号。全ソースの外の位置は-1。" },
        "span": { "$ref": "#/$defs/span", "description": "位置がソース全体を指す場合、またはsourceが-1の場合は省略する。" },
        "range": { "$ref": "#/$defs/range" },
        "label": { "type": "string" }
      }
    },
    "omission": {
      "type": "object",
      "description": "表示上限により省略した診断の部分。",
      "required": ["part"],
      "additionalProperties": false,
      "properties": { "part": { "type": "string" }, "count": { "type": "integer", "minimum": 1, "description": "省略した項目数。不明な場合は省略する。" } }
    },
    "excerptLine": {
      "type": "object",
      "required": ["line", "text", "start", "length"],
      "additionalProperties": false,
      "properties": {
        "line": { "type": "integer", "minimum": 1, "description": "1始まりの表示行番号。" },
        "text": { "type": "string" },
        "start": { "type": "integer", "minimum": 0 },
        "length": { "type": "integer", "minimum": 0, "description": "印を付けたテキストの長さ。0は挿入位置を示す。" }
      }
    },
    "display": {
      "type": "object",
      "description": "表示用のデータ。診断の解釈には不要。",
      "required": ["excerpt"],
      "additionalProperties": false,
      "properties": { "range": { "$ref": "#/$defs/range" }, "excerpt": { "type": "array", "items": { "$ref": "#/$defs/excerptLine" } } }
    },
    "edit": {
      "type": "object",
      "required": ["source", "span", "text"],
      "additionalProperties": false,
      "properties": {
        "source": { "type": "integer", "minimum": 0, "description": "入力のソース表番号。" },
        "span": { "$ref": "#/$defs/span" },
        "range": { "$ref": "#/$defs/range" },
        "text": { "type": "string", "description": "置換後のテキスト。削除では空。" },
        "replaced": { "type": "string", "description": "spanが覆う、長さを制限した表示用テキスト。挿入では省略する。" }
      }
    },
    "requiredCondition": {
      "type": "object",
      "description": "検査で判定できなかった条件と、成立すべき内容を示す句。",
      "required": ["condition", "phrase"],
      "additionalProperties": false,
      "properties": {
        "condition": { "$ref": "#/$defs/condition" },
        "phrase": { "type": "string" }
      }
    },
    "condition": {
      "enum": ["Take", "ExclusiveAccess", "UsageLegality", "Structure", "Selection"],
      "description": "TakeはPlaceをMoveできること。ExclusiveAccessは貸出し点に排他的に書き込めること。UsageLegalityは後続の使用が有効なままであること。Structureは名前・後処理の順序・制御フローが変わらないこと。Selectionは修飾名が使用箇所で必要なものを選ぶこと。"
    },
    "repair": {
      "type": "object",
      "description": "代替候補となる構造化編集一つ。verifiedの条件は検査で確認済み、requiredの条件は未判定。適用後は新しい入力となり、新たな検査でのみ妥当性を確認できる。",
      "required": ["kind", "title", "edits", "verified", "required"],
      "additionalProperties": false,
      "properties": {
        "kind": {
          "enum": ["Repair.Transfer", "Repair.Borrow", "Repair.BorrowExclusively", "Repair.RemoveUnsafe", "Repair.ReplaceToken", "Repair.InsertToken", "Repair.PropagateFailure", "Repair.ExplicitDiscard", "Repair.Qualify"]
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
        "source": { "type": "integer", "minimum": -1, "description": "ソース表の番号。全ソースの外の位置は-1。" },
        "span": { "$ref": "#/$defs/span", "description": "位置がソース全体を指す場合、またはsourceが-1の場合は省略する。" },
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

---

<a id="status"></a>
## Kimigayo実装状況

現在の対応範囲、既知の不具合、残る制限。[SPEC](#contents)と[IMPL](#implementation-specification)が必須動作を定める。この文書の制限によってその規則を狭めることはない。次の作業と課題IDは[PLAN](dev/PLAN.md)、実装の案内は[CODEMAP](dev/CODEMAP.md)、各マイルストーンの状態は[プログラム一覧](../tests/milestones/README.md)で管理する。[AGENTSのDocumentation](../AGENTS.md#documentation)に従い、変わった境界だけを更新する。

**適合性についての留保：** 2026-10-15の仕様改訂とその後の変更で、言語契約と実装契約が変わった。以下の対応記録は、改訂されたすべての規則への適合を証明するものではない。その監査はPLAN S1015として残る。コンパイラーは、指定された`kimi.check/3`コマンド全体の形式ではなく、単一単位の`kimi.check/2`をまだ出力する。2026-10-17と2026-10-19のSPEC §15.6改訂（A5の簡略化と基底によるLoanの同一性、正確なMove-Path接頭辞によるLoan終了とloss P12、本体が所有しない記憶領域が保持元に従うこと、§15.6.4の再帰的基底を持つ射影におけるパラメーター依存効果の拡大。[INTEGRATED](../draft/INTEGRATED.md)）はMIRパイプラインのA4とA5に属し、まだ完了していない（A4はDECISIONS R4-M87以降拡大する。G170が隠す再帰的基底はそのatomを保ち、二段階の`CopyTree`射影でG170がCopyを誤って否定するため、MIR上のインスタンス化不能なコードで一部の有効な呼出し側を拒否する）。R6-checkまでは、既定パイプラインは以前の規則を保つ（[A5計画](../draft/Proposals/2026-10-16%20Complete%20Polonius%20A5%20Plan.md) §9.2、OD5）。そのため、改訂SPECが拒否する一部のプログラムを受理する。たとえば、ローカル変数の一部への書込みや、参照を通じた全体への書込みの後で、古い値が保持していたLoanと競合する場合である。また、改訂SPECが受理する一部を拒否する。たとえば、参照を通じて再借用した子が生存中の参照の`replace`、古い値を通じて形成した子が生存中に置換された外側の参照を通じた書込み、共有参照外しの先で既存の共有借用をCopyする既定値（`x: ref/i32 during a`に対する`y: ref/i32 during a = x@follow@ref`）である。以前の規則のテストには`OldRule`特性を付け、コーパスの行では`Old`項目に記す（DECISIONS R4-M60）。

詳しい過去の記述と証拠への参照は、[整理前のスナップショット](https://github.com/archi-Doc/Kimigayo/blob/5287ae5058fbc355a8b39ee4685d83b5ebb857ad/docs/STATUS.md)とGit履歴に残す。新しい単位の証拠は、この文書ではなくコミットと`artifacts/verify/`に置く。

<a id="status-1"></a>
### 1. 字句解析、構文解析、コンパイル条件

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| ソースと構文 | 不変のUTF-8スナップショット、元の位置、NFC名、インデント・区切り文字、数値・文字列リテラル、補間、宣言・式の構文、除外構文の検査。対応する関連型ファミリーの形成型は構文解析と往復変換が可能。 | `task;`を含むタスク構文は未実装。`open`は文脈依存ではなく予約語のまま。 |
| 文書 | `///`の対応付け、ソース・断片の同一性、独立した限定プロファイルのMarkdown解析・描画。 | 宣言されたMarkdown部分集合への対応であり、任意のMarkdownへの対応でも、生成断片の由来情報の完全性の証明でもない。 |
| 回復 | 対応する経路では、条件付き前提、字句、区切り文字、レイアウトの回復で独立したエラーを保つ。 | 無効な環境の伝播と、破棄された宣言の名前の原因はG55/G60として残る。回復機能はすべての不正入力への対応を保証しない。 |

<a id="status-2"></a>
### 2. 束縛、型、Kimi

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| 名前と宣言 | 型・値の分離検索、ソース内スコープ、アクセス、名前付き引数、コンテナーの入れ子、ジェネリックパス、別名、分割宣言。 | 完全な候補等価性、生成断片の確定、外側の編集による無効化の全要件は未完了。型・値スロットの種類が異なるオーバーロード間の明示引数はUnsupported。 |
| 型とContract | 完全な型、Semanticsペア、4値の証明、宣言前提、関連する同一性、条件付き適合、共有インライン継承のwitness対応。束縛された要件参照は別々の同一性を保ち、置換を合成する。 | 等価な関数候補の統合、任意の束縛・絞込み合成、完全なジェネリック効果証明、一部のプロパティ実行は未完了。共有インライン適合への対応は、継承したオブジェクトレシーバの射影を保証しない（G83）。継承した関連型の制約を通じたCopy証明（`T is CopyTree`の下の`T.(Tree).Child.(Tree).Child`）は、より強い修飾子を明記した場合にしか見つからず、その値の有効なコピーを両パイプラインで拒否する（G170）。フィールド`obj/Chain<(T, T)>`を持つ`Chain<T>`のように拡大するジェネリック再帰では、OwnedはUnknownとなる（G174）。 |
| 推論 | 固定された期待型、既知の呼出しシグネチャ、範囲を限定した関連型推論、コンストラクター推論、ローカルの固定配列の長さ・要素の穴。SPEC 10.5.1の引数種類は、呼出し引数、タプル・配列の要素、制御フロー引数、文脈に依存する`.Case(...)`、Caseペイロードに対応する。Literal部分は選択後に既定型へ決まり、待機部分は選択候補が完成させ、Case構築はペイロードからスロットを解く（DECISIONS BU-2）。SPEC 14.9.2の結合では、リテラルのソースや葉、およびNever要素を含むタプル・配列・辞書リテラルは、決定された対象結果型を受け取る。入れ子、括弧、ラベル、呼出し引数でも同様である（DECISIONS BU-4、BU-9）。`@move`や`@copy`のオペランドは、その位置から型を受け取らない（SPEC 12.3.1、13.5.2）。 | 一般的な関連型の逆推論、候補コンテキストの混在、複数の呼出し時専用引数、異なる参照コンテキスト、入れ子のタプル・配列の既定型の混在は未完了（G10）。リテラルの隣に未知の型付きソースがある制御フロー引数は待機する（G152）。すべてのソースが待機する結合は、それぞれの型を比較する（G159）。Case構築の未束縛スロットは`UnresolvedBinding_Kd`、呼出しの未束縛スロットは`UnboundTypeArgument_Kd`となる。束縛された関連型推論は固定された宣言結果の証拠を使い、本体や制限のないファミリー逆推論は使わない。SPEC 8.5.5の定義間相互参照の例はどちらの宣言順でも拒否される。受理判定が宣言順に依存する場合もある（代替の根拠を持つ循環が一方の順序だけで受理される。G172）。 |
| Origin | ヘッダースロット、シグネチャのバインダー、付随関係、ローカル注釈の補完、可視前提に対する範囲を限定した証明閉包。完全特殊化とオーバーライドは、省略された結果専用バインダーと元の関係を継承する。 | 対応するローカル補完の外にある、呼出し修飾子と記述された型引数の開いたOriginスロットはG10。入れ子のローカル関数注釈は新しいOrigin推論を導入しない。契約全体の比較では、必要な入力の整形式性の全前提をまだ使っていない。一般的な主推論と任意のOrigin多相実行は未完了。 |
| 算術と比較 | 標準算術Contract、数値witness、ユーザー・ジェネリック比較、直接・Item・Callable経路、対応範囲の型消去。複合更新は選択操作を使い、右辺を先に評価し、対象評価を一度だけ行う。 | 要件が所有するジェネリックスロット・名前付きの呼出しごとのOrigin、オブジェクトwitnessの射影、CSPへの公開は別の未対応事項。ネイティブ数値の制限は§4に記す。 |
| プロパティ | カスタム・計算型のCopyゲッター／セッター、所有値およびレシーバ・外部から借用した値を返すゲッター、Originを持たない非Copyセッター入力。対応するジェネリックゲッター・カスタムセッター、共有継承ゲッター、排他的Contract経路。 | Originを持つセッター入力、異なるプロパティ要件の結合、継承した排他セッター、排他的オブジェクトアクセサー、静的アクセサーの実行は未対応または未検証。 |
| 仮想呼出し | 非ジェネリック型と閉じたジェネリック型の共有`objref`ディスパッチ、継承スロット、条件付き適用可能性、直接の基底呼出し、公開効果、Item、モジュール間アクセス。 | 排他的ObjectCallCompatibleの推論・強制・公開、以前の結果の同一性による除外、成果物をまたぐ検証済みスロット保証は未完了（G81/G83）。 |

<a id="status-"></a>
#### ライブラリーとコレクション

公開宣言と保証は[LIBRARY](LIBRARY.md)で管理する。以下は実行の境界であり、API一覧の重複ではない。

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| 配列とビュー | 動的Array記憶領域、固定配列、ローカルの長さの穴、スカラーの`noinit`。Copy読取り、置換、借用を通じた書込み、互いに重ならないペア参照、ソート、共有・排他ビュー。UniqSliceとSlice.toArrayはKimi本体を通じて実行する。 | 一般的な参照合成と動的な非Copyの取り出しは未完了。スカラー`noinit`の書込み前読取りの例は、受理だけを検査し、実行していない。Sliceの`firstIndexWhere(predicate:)`は未実装。 |
| Dictionary | 空の記憶領域、reserve/shrink、検索、挿入・置換・削除・clear、添字アクセス、挿入順の共有・排他・所有反復、対応する格納参照の結果。 | より広い参照合成は、以下の共通の所有権規則と要素規則によって引き続き制限される。 |
| 位置と範囲 | 型付きの位置・範囲、検査付き正規化、独立した整数境界型、対応する範囲・シーケンス反復。 | ジェネリックIndexableでの組込みシーケンスの使用と、任意のSlice要素結果は未完了。Sliceの由来が混在する場合は保守的なまま。 |
| Indexableと反復 | ユーザー添字は、選択された`index`/`indexUniq`の返す参照をたどる。具体的・ジェネリックな共有・排他・所有の貸出しループは、記録された経路で実行する。 | 一つの型パラメーターに対する複数のIndexable事実の選択は未実装。Slice要素への書込みと非CopyのMoveは引き続き拒否する。Originを持つ関連型ファミリー結果が、object/raw Semantics、それらを許すペア、または関数型を含む場合はUnsupported。 |
| 所有権とオブジェクト | 値全体のreplace/exchange/swap、具体的なobj/rc/arcファクトリー、clone・後処理、対応するペイロードのCopy読取り、基底ビュー、フィールド、共有呼出し、一時ハンドルの借用。 | Weak、検査付きキャスト、実行時Contractビュー、一般的なフロー絞込みは未実装。オブジェクトと参照層の不具合は§3に列挙する。オブジェクト対応はObjectCallCompatible効果の完全性を意味しない。 |
| テキストと書式化 | 固定・ヒープバッファー、UTF-8検証、型消去したWriter、組込み・ユーザーフォーマッター、Text変換、所有文字列を作る補間、`$tryWrite`、reserve効果の検査、上限付きConsoleスタック出力。 | 効果解析には以下の保守的な呼出し・別名制限が残る。スタック書式化は1,024バイトが上限であり、上限不明なら通常のバッファー経路を使う。改訂されたメモリー割当て保証は引き続きS1015の対象。 |
| 時間と数値ラッパー | WindowsのDuration/Stopwatch、および対応するWrapping整数操作・変換・書式化。 | 操作別の数値・生成境界は残る。改訂されたすべてのライブラリー保証を確立するものではない。 |
| OptionとResult | オプション型、対応するenum記憶領域、`try`、明示的に結果を捨てる操作、通常の取得・後処理。 | Never・staticスカラー参照のペイロード、一般的な集成体の共変性・Origin多相ABIは未完了。IMPL §21.1.3のメモリー上の非null Option（`ref`、`uniq`、`obj`、`rc`、`arc`、`objref`、`objuniq`のペイロードに対してポインター一ワード）はまだ配置されず、そのOptionはタグ付きenumレイアウトのままである（PLAN U8）。 |

<a id="status-3"></a>
### 3. 制御フローと所有権

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| フローと後処理 | 対応する表現におけるif/match/do/loopの結果、ガード、ラベル、defer、既定引数、返す値、部分構築、逆順の後処理。デストラクターの`self`は初期化済みフィールドを排他的に貸し出す。その一部をMoveで取り出さず、本体内のクロージャーは補助借用`self@ref`やそれを保持する値をキャプチャーしない（SPEC 16.3.1）。所有するSubjectまたは`for`項目の、条件を満たすペア層でのパターン束縛は、その層の先のモードの参照を束縛する。ゲッター結果への排他的なものは拒否する（SPEC 13.5.5、15.1.6）。 | 一般的な不揃いの取得スタック、効果・絞込みのケース、借用されたSubject、集成体の組合せは未完了。Abort・発散の後に通常の後処理は実行しない。`drop`の`self`全体の使用（補助借用`self@ref`、`ref/Self`の引数やレシーバ）は、R6-checkまで既定パイプラインで`Unsupported_Kd`となる。コンストラクターの排他フィールド借用（SPEC 6.3.4の例）は`Unsupported_Kd`、レシーバ式のものは`NoApplicableOverload_Kd`となる（PLAN G166）。既定パイプラインでは、次のパターン形式はR6-checkまで位置付き`Unsupported_Kd`となる（DECISIONS BU-14）。devが値として束縛するか構造的にたどって実行していた場合も含む。所有するSubjectまたは`for`項目のペア層スロットでの束縛（`match pair@move`の`(let x, _)`、`for (x, _) in arr@move`）、許可される集合のいずれかでペア層をたどる束縛、所有するSubjectのownerを許すペア層を通過した構造パターンの下の束縛（`match o@move`の`.Some(let n) => n@follow = 5`）である。同じ位置のワイルドカードと束縛のない構造テストはdevと同じままとする。裸でないペア型Subjectへの`for`は、層を通じて入口を選ぶ代わりに制約エラーを報告する。MIRでは（shadowでありR6-checkまで未公開。DECISIONS R4-M93、R4-M96–R4-M98）、A5はSPEC 7.2.3に従って既定値を検査する。先行スロットへの書込み・排他借用を行う既定値、またはそのスロットの新しい借用を自身の結果や本体が所有しない記憶領域に保持する既定値は、宣言位置で`DefaultArgumentAccess_Kd`または`DefaultArgumentBorrow_Kd`となる。省略呼出しの検査用Loanは、その呼出しの検査前に終了する。再帰的な既定値は、すべての先行引数のLoanを保持する。インライン展開された既定値のコピー内で見つかった失敗は、一回の評価内に限りそのテンプレートに属し、それ以外は省略呼出し位置で呼出し側に属する。スロットの型がraw権限（生ポインター、rcハンドル、そのWeak）を持ち、入力がLoanを保持する再帰的な既定値の呼出しは、G160のraw Originまで、省略呼出しで位置付き`Unsupported_Kd`となる（PLAN G160）。これは、有効なプログラムをLanguageエラーにしていた設計上のraw格納の行を置き換える（DECISIONS R4-M93）。第20ラウンドの、既定値自身のLoanを後続の同じ既定値の評価や兄弟の既定値へ持ち越す形式は、可変staticの借用を既定値またはraw格納に保持し、Bindingが拒否する（SPEC 15.6）。 |
| Loanと返された参照 | 固定Originと呼出しごとの入力Loanは、対応する直接・値・共通関数呼出し、返された値・格納値・キャプチャー値、その後の取り出しを通じて存続する。最後の競合する保持元が親を解放する。 | 未知の記憶領域・祖先関係と間接対象フローは保守的なまま。オブジェクト借用のルート、表現されていない環境Origin、非PlaceのCallableレシーバには別の制限が残る（G65/G75）。SPECのraw到達とraw Origin（§5.7、§15.6、DECISIONS R4-M95）は未実装（PLAN G160、G184）。既定パイプラインは生ポインター経由で格納した値のLoanを保持しない（`Raw.initialize(p, x@ref); x = 2`を受理する）。A5は、呼出しが保持元の生ポインターを通じて格納するLoanと、本体内のraw読取りが回収するLoanを見落とす（不健全な受理）。raw層の`during`と表明`E@(raw/U during o)`は、G160-0単位で位置付き`Unsupported_Kd`を報告するまで、Languageエラー（`BorrowOriginSemantics_Kd`、`AdaptationOrigin`）で拒否する。 |
| Semanticsケース | 定義時検査は共有エンジンを通じて許可されるSemanticsケースを検査する。対応する借用・オブジェクトのケースと、独立した入力能力も含む。 | 借用引数に対する呼出し先適用`u/V<U>`、ケース別ではなく最大のクロージャーレシーバ、裸の排他要素、タプル要素のクロージャー呼出し、`objuniq`ルートは未完了（G75）。 |
| クロージャーと関数値 | 具体的なキャプチャー記憶領域、Shared/Exclusive/Consuming呼出し、ブロック結果推論、入力依存結果、Function Item、対応する共通関数の型消去。 | R1の現在の値呼出し結果の境界は、staticと呼出し先自身の呼出しごとの入力Originだけを許す。環境または外側の固定Originにわたる結果はR6までUnsupported。独自のジェネリックスロットや名前付きの呼出しごとのOriginを持つ要件参照、一般的なOrigin依存のキャプチャー・型消去の組合せは未完了。既定パイプラインは、文の後で使われるFunction Item一時値の借用を受理する（SPEC 3.6。たとえば、結果のOriginが`f`のOriginである`f: ref/F`引数として使う場合。SPEC 15.4.3 (1)。MIR解析は拒否する）。誤ったコードの生成はなく、R6-checkで解消する。 |
| Callable効果 | `confined` / `preserves results`の検査、ジェネリック前提、コールバック合成、公開効果のHover。 | 後の呼出しが前のCallable値であり得る場合、除外に置換をまたぐ同一性が必要ならUnsupported。安定した完全フィールドパスには範囲を限定した除外がある。別名、委譲された値フロー、呼出し側の静的Loan、ビルド間の要約はG78。補助関数の効果は保守的な公開境界を使う。 |
| 要素と再借用 | 対応する静的タプル・固定配列のパスは実際のLoanを保持し、互いに重ならない兄弟へのアクセスと部分Move・修復を許し、スナップショットではなく選択した記憶領域を借用する。一層の参照を格納する要素の下の部分は、その参照を通じて到達するPlaceとなる。ユーザー添字の要素の下で、そのレシーバパスがローカル変数・パラメーター・キャプチャー、それらの格納フィールド、`uniq/`パラメーター経由のタプル要素、または同様の別のユーザー添字要素である場合、レシーバ、書込み、更新、タプル要素は参照先を変更する。共有読取りや比較される文字列はそこで直接読み取る。ローカル変数のタプル要素や組込み要素を通じて到達するユーザー添字要素の下、およびシーケンスがローカル変数・パラメーター・キャプチャーである組込み要素の下でも同様である。ユーザー添字や`Array`要素に書き込んだり排他的に借用したりできるのは、レシーバを排他的に取得できる場所だけである（SPEC 15.1.5）。`var`ルートのタプル・`Array`要素や一時値の下も含むが、`let`束縛、所有パラメーター、共有層を通じる場合は許さない。参照の先にあるrc、arc、objrefビューも含む（DECISIONS BU-13）。 | 動的な排他要素の再借用、参照の期待位置に置かれた格納済み一時値の部分、多層の添字アクセス・反復、ペア層のArray添字アクセス、一層の参照より先のシーケンスメタデータは、引き続き拒否またはUnsupported。R6-checkまで、要素が格納する二層以上の参照を通じた部分読取り、レシーバの許可リスト外（組込み要素、ローカル変数のタプル要素）の要素の下での排他的使用、またはシーケンスがローカル変数・パラメーター・キャプチャーではない組込み要素の下の読取り（`t.0[0].n`、`h.vals[0].n`、`vals[0][0].n`）は、旧解析による要素位置の`Unsupported_Kd`となる。Bindingは受理し、MIRはその場で構築する（DECISIONS BU-10）。未知の添字関係から非重複は証明しない。 |
| 予約 | 通常・メソッド・コンストラクター・Callable・ジェネリック・値全体・対応するオブジェクトの各呼出しは、借用を準備して有効化する。 | 準備中に、予約と重ならないパスへ書き込むことはSPECで許可されるが、R1ではUnsupported。 |
| 到達不能コード | 通常の到達可能な所有権検査と、到達不能なローカル変数・パラメーター使用がないコードは、対応済みの動作を保つ。 | R1は必須の到達不能ソース検査を実装していない。他の所有権問題がなければ、最初の到達不能なローカル変数・パラメーター使用はUnsupported。そのような使用のない到達不能コードは引き続き検証・出力する。回復はR6に割り当てられている。 |

既知の不具合は、安全に拒否される境界ではない。

| 領域 | 未解決の不具合 |
| --- | --- |
| オブジェクト・参照層 | 一部の参照層を通じた継承ゲッターと共有メソッドは誤ったアドレスを読む。比較の右オペランドが、参照を通じて調べる左の文字列オペランドを無効化できる。ほかにも検査を通って生成に失敗する形式や、Languageコードを報告する形式がある。[P34の残作業](dev/P34_REMAINING.md)を参照。これらの形式の安全性は主張しない。 |
| オブジェクト対象の変性 | obj/objuniqのView Target不変性は未完了（P39）。直接のobj対象は、等価性が必要な場合にも、共変な`outlives`によって期待型に合うことを受理する。 |
| 保持された排他的な子 | G75 E6/E7は、配置された要素・フィールドから再びMoveで取り出された子や、キャプチャーした排他参照を通じて格納された子をすべて保持するわけではない。一部の競合するプログラムを受理する。`(p@uniq, 1)`のような直接配置した再借用は、検査を通って生成に失敗することがある。 |
| Sliceと範囲の一時値 | 旧解析は、文の後で使われる借用されたSlice一時値や範囲リテラル（`(0..3)@ref`）を受理する（SPEC 3.6）。既定パイプラインの不健全な受理であり、原因はまだ見つかっていない（PLAN G177）。 |
| 条件付き一時値 | `and`/`or`の右側のUnit・struct一時値は、検査を通って条件付き破棄の生成時に失敗することがある。 |
| 破棄レシーバの借用 | `drop`やコンストラクターで、あるフィールドの先行する共有・排他借用が生存中でも、既定パイプラインはそのフィールドへの書込みや二つ目の排他借用を受理する（`let r = self.f@uniq`の後に`self.f = …`、さらに`r`を使う場合）。MIRは拒否する（PLAN G165）。コンストラクターでは、既定パイプラインがフィールド借用にパラメーター0のLoanを与えるため、その借用が生存中にパラメーターをMoveすると、誤った`ComparisonLoanConflict_Kd`となる（PLAN G178）。 |
| 静的初期化 | 循環検出は、アクセスするPlace式での`KIMI_E_STATIC_CYCLE`ではなく、フィールド宣言での`KIMI_E_ABORT`を使う。 |
| 入れ子の特殊化 | ジェネリック宣言の二段階以上内側に入れ子になった名前付き関数の特殊化を選ぶ呼出しは、元の本体を実行する（旧生成器は一段階だけ転送する）。MIRコード生成はSPEC 6.1.3に従ってそのインスタンスを識別する（R5 K1、DECISIONS R5-M34）。 |
| Neverペイロード | BindingはNeverペイロード束縛の読取りを拒否する（`Result<i32, Never>`の`.Err(let e) => e`。PLAN G145）。Neverを保持する型のPlaceに対する旧解析の`Unsupported_Kd`には、関数位置の派生的な`UninitializedPlace_Kd`が伴う。また、参照先がNeverの排他参照引数（`g(y@(uniq/Never))`、`uniq/Never`パラメーターの引渡し）では`InternalInvariant_Kd`が伴う（DECISIONS BU-6）。どちらもR6-checkで旧解析とともに解消する。 |

<a id="status-4"></a>
### 4. LLVM生成の対応範囲

ネイティブ実行のターゲットはWindows x64である。束縛の成功はネイティブ対応を意味しない。以下は範囲を限定した実行部分集合である。

| 領域 | 記録された実行 | 残る境界 |
| --- | --- | --- |
| スカラー | Bool、i128/u128までの整数型、char、f32/f64、Unit。対応する検査付き算術、ビット・シフト操作、比較、変換。 | 初期プロファイルはi128/u128の除算・剰余と、型付き128ビット数値↔浮動小数点数またはchar/boolの数値変換を禁止する。charの算術と浮動小数点リテラルのパターンは未対応。 |
| 関数と集成体 | 直接呼出し、再帰、名前付き・既定引数、対応するstruct・タプル・固定配列・enumの記憶領域とABI、コンストラクター、返す値、破棄。 | 任意の借用集成体、集成体の共変性、Origin多相ABI、パターンの全組合せは未確認。動的な非Copy取得と、一時値・関数・選択の結果からの部分MoveはUnsupported。 |
| 文字列 | 所有値、対応経路での借用引数・結果、UTF-8比較・出力、補間、Text変換。 | 一般的な入れ子の借用は未完了。§3の参照比較の不具合は未解決。 |
| ジェネリック生成 | 閉じた置換の下での単相化、選択した特殊化、転送結果、複合値、具体的な所有権検査。インスタンスごとのソース再束縛は行わない。 | 増大するコンテキストは資源上限の結果で失敗する。展開深さ128、本体ごとの閉じたコンテキスト数最大1,024、インラインレイアウト深さ64。Bindingの記憶領域準備は、記憶領域の高さが64を超える型（強連結成分のメンバーは同じ高さを共有）や、走査が256型を超える型を拒否し、要求した呼出しで位置付き`Unsupported_Kd`を報告する（PLAN G128）。拒否したインスタンスに代替コードは生成しない。2^63 - 1バイトを超える値渡しレイアウトは、MIRコード生成では検査時のLanguageエラー`OversizedLayout_Kd`となる。G2までは、既定パイプラインはビルド時に生成サイズ上限（`GenerationResourceLimit_Kd`）として報告する。配置64段階より先でしか見えない過大サイズは、深さ上限のResourceレコードだけで報告する。 |
| 間接呼出しとクロージャー | 対応するスカラー、生ポインター、Unit/Never、タプル、文字列、固定配列のシグネチャ。小さく単純なShared環境はインラインに型消去し、対応する大きい環境・所有環境はヒープ記憶領域を使う。 | 省略ヘッダー・参照選択には引き続き固定された期待型が必要。一般的なオーバーロード・Origin多相Callableシグネチャと、任意のパラメーター・結果の組合せは未完了。§3の結果Originの制限を適用する。 |
| 静的メンバー | 閉じた非ジェネリックコンテナー内の不変スカラーgroupフィールドには、初回読取り時の初期化、依存順序、循環検出がある。 | 可変・借用・集成体の静的記憶領域、保持された借用の追跡、継承したジェネリック記憶領域のキー、終了処理、一般的な寿命プロトコルは未完了（P42）。静的フィールド、またはその下のフィールド・要素を、フィールドの修飾名で明示的に借用する形式（`State.counter@uniq`、`@ref`、`@raw`、型付き、`@objref`、Case Adaptation、引数として、自身のgroup内、括弧付き、部分の借用：`(State.limit)@ref`、`State.pair.0@ref`、`State.values[0]@ref`）は、Bindingの位置付き`Unsupported_Kd`となる（P42）。`(State.limit)@uniq`は`State.limit@uniq`と同じくLanguageエラー。まだ未対応なのは、自身のgroup内の非修飾借用と、静的オブジェクトハンドルを通じた`State.h.v@ref`である。これらは束縛後、既定パイプラインが`Unsupported_Kd`を報告する。そのハンドルを通じたその他の借用、および修飾されたstaticの暗黙の引数・レシーバ借用（Dictionary要素の添字`State.dict[1]@ref`も含む）はLanguageエラーとなる（PLAN G100）。循環診断は上記のとおり誤っている。MIRコード生成では、staticが`var`の場合、型がスカラーでない場合（集成体、借用型、dropが必要な型）、またはジェネリックコンテナー内のgroupに属し、アクセスが記録を持たない場合（リテラル`let`も含む）、P42まで、使用の有無によらず宣言位置で`Unsupported_Kd`となる（DECISIONS R5-M82）。既定パイプラインのcheckは、ジェネリックコンテナーのgroup内の不変スカラー`let`を受理する。MIRでは（shadowでありR6-checkまで未公開。DECISIONS R4-M92）、A5はA4のSemanticsケースビューごとのキーを使い、各呼出し・破棄の静的効果を、可変static上の生存Loanと比較する。そのLoanは、PLAN G100が束縛可能として列挙する明示形式を除き、P42までは暗黙のもの（groupメンバー自身のレシーバや引数の借用）である。書込みや未知の効果はそのLoanすべてと、読取りは排他的なLoanやその呼出しが有効化する予約と競合し、可変staticの破棄は自身の効果と競合する（SPEC 16.4）。A4が効果を分類しない場合、および生存Loanの初期化証明だけが競合を決められる場合、位置付き`Unsupported_Kd`を報告する（生存性は可能性の性質である。その証明は、すべての位置で不変staticの初期化子にも、その位置が直接かつ証明済み初期化子経由でも生成するキーにも適用する。そのため、一部のSPECエラーはその位置で`Unsupported_Kd`になる）。インライン展開された既定値のコピー内で見つかった効果競合は、Loanがそのコピーの同じ評価内で発行された場合だけテンプレートに属し、それ以外は呼出し側に属して省略呼出し位置に記録する。コピー内の可変staticの破棄が自身の効果と競合する場合は、Loanを取らないためテンプレートに属する（DECISIONS R4-M97）。 |
| 生ポインターとFFI | 対応する生ポインター算術・変換、Unsafeアクセス、raw Take・破棄（同じアドレスに配置したサイズ0の値はそれぞれのPlaceを保持する。SPEC §3.4、§5.2）、具体的なC struct、整数・浮動小数点数・生ポインター・ref・uniqシグネチャの直接import。 | 非Copy値への非消費rawアクセス、Option<ref/T>/Option<uniq/T>のimport ABI、C交換可能性の認証、依存モジュールの供給情報、アーカイブ・供給元の完全な検証は未完了。 |

<a id="status-5"></a>
### 5. CLI、成果物、Windowsランタイム

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| コマンド | check、emit、Applicationのbuild/run、Libraryの製品検査ビルド、共通の入力解決。Libraryの成功は、IR・実行ファイル・LLVMツールなしで`checked`を公開する。 | 成果物生成には、選択された一つのWindows x64ターゲットが必要。Libraryのマニフェストビルドは拒否する。ソースモジュールのライブラリー調査は、公開ネイティブABIでもソースパッケージでもない。 |
| ネイティブ成果物 | スキーマ3のリンクマニフェスト、入出力ハッシュ、段階的な公開、取消し・タイムアウト、失敗時の以前の成功の無効化。Applicationのマニフェストビルドは共通のネイティブパイプラインを使う。 | マニフェストビルドはソースの最新性を表明しない。通常のビルドではツールチェーン検証を未実施として記録し、セットアップ・明示的検証がインストールされたツールの同一性検査を担う。スキーマ3は、モジュール単位の完全なネイティブ検証を認証しない。 |
| ソース依存関係 | 正確なソースモジュール識別情報、直接の可視性、循環・競合検査、製品・テストのロック分離、対応する推移的生成、依存先を呼び出すルートテスト。 | パッケージのload/pack/publish/store、可搬な内容・意味レコード、完全なモジュール・ネイティブ入力レコードは未完了（T1/T5）。直列化は構文解析と束縛をやり直すもので、権威ある意味証明の永続化ではない。 |
| ネイティブ供給 | プロジェクト設定は、要件・供給情報の形状、ターゲット・名前の同一性、宣言されたContractId/Sha256の一致を検証する。 | 実際のモジュール供給・種別の検査、リンク時の使用、統合された内容の一致は未完了。 |
| ランタイム | 起動、UTF-8出力、メモリー割当て・解放、対応する文字列・オブジェクトの後処理、Abort。標準の数値範囲失敗は`KIMI_E_ARG_RANGE`を使う。 | 一般的なWeak・メタデータのランタイム対応と静的終了処理は未完了。Arrayのinsert/removeなど、一部のライブラリーラッパーには呼出し位置の転送がまだない（G71）。 |

<a id="status-6"></a>
### 6. コンパイラーサービスとその他の製品境界

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| 診断 | 対応経路における共通CLI/JSON/LSPの事実、前提条件、Reason、関連位置、証明結果、構造化された修復候補。言語上有効な未対応形式は、位置だけを持つ`Unsupported_Kd`を使う。 | G10/G56には隠蔽・順序の欠落が残る。型消去や呼出しごとのスロットの失敗が他の引数Origin失敗を隠すことがあり、複数の保留候補・結果専用引数が事実を失うことがある。witnessのOriginだけの不一致は、まだ事実のない適合エラーになり得る。BindingのOrigin失敗は所有権検査を止める。一部の有効なフロー絞込み形式にはLanguageコードを報告し、以前のMove・競合Loanの位置が欠けている。ブロックからの逸脱の報告は、破棄済みルートを統合したり、未解決の結合・破棄原因を重複させたりすることがある。これらは診断の不具合であり、言語規則の縮小ではない。 |
| 言語サーバー | 診断、クイックフィックス、宣言・変数・Copy・効果・文書のHover。範囲を限定したスナップショット、同一性検査、以前の解析結果のラベル付けを備える。 | 補完とCSPアダプターは未提供。Hoverは未対応の言語形式を推測しない。見た目の受入れ確認とエディター全体の遅延は、コンパイラーテストでは確認されていない。[Hoverの境界](dev/HOVER.md)を参照。 |
| エディター | リポジトリのVS Code拡張。字句によるTextMate強調表示、管理されたLSP接続、診断、対応するHover・コードアクション。 | 字句による色付けはセマンティックトークンへの対応ではない。KimiCodeクライアントはこのリポジトリの範囲外。 |
| Modと合成 | ソースの追記・再束縛と、暫定・確定の基盤。 | BindingはModを実行しない。ホストインターフェースは延期。Composition RootのEntry/Provider選択は撤回済み・未確定。 |
| Async | Callable効果境界には、独立して実装された部分集合がある。 | タスク・ワーカーは延期。タスク構文、Kimi.Async、コルーチンへの変換、スケジューラー・I/O、Transferable、スレッド安全な静的初期化は未実装。 |
| 性能 | 選定したウォーム状態のコンパイラー処理は記憶領域を再利用し、メモリー割当てゼロの回帰検査を持つ。選定したネイティブ処理にはO0/O2とメモリー割当ての証拠がある。 | コンパイラー全体、モジュール読込み、起動、エディター全体のメモリー割当て・速度は保証しない。依存本体の借用表は、最悪時に二次の記憶量を要する。 |

<a id="status-7"></a>
### 7. 言語のテスト実行

| 領域 | 記録された対応 | 残る境界 |
| --- | --- | --- |
| 選択と検査 | プロジェクト・ソース・ディレクトリ・Solution入力、検出、フィルター・一覧表示前の選択済み全本体の検査、製品・テスト入力の分離、起動処理を実行しないこと。 | 依存先のテストは検出しない。意味上有効でも生成器が出力できないコードで`--list`が成功することがある。 |
| 表明 | `$expect` / `$require`、条件の一度だけの評価、必要時だけのメッセージ評価、後処理、型付き比較のスナップショット、区別されたrequire-Abort。 | 通常のコンパイラーの表現・所有権・生成制限はテスト本体にも適用する。 |
| Windowsワーカー | 隔離プロセス、EOFの標準入力、結果・標準出力・標準エラーの分離、期限・取消し、有限の予算、固定された実行ファイルの同一性、不可分な報告、上限付きの保持。 | 一般的な静的終了処理と完全なモジュール・ネイティブ資源契約は未完了。名前付き実行プロファイルは延期。NativeAOTの検証は主張しない。 |

---

<a id="sources"></a>
## 翻訳元ファイル一覧

以下を翻訳・統合した。開発計画・検証履歴など、収録対象外の補助資料への参照は原文ファイルへのリンクとして残す。

- [SPEC.md](SPEC.md)
- [IMPL.md](IMPL.md)
- [spec/01-overview.md](spec/01-overview.md)
- [spec/02-source-and-lexical-structure.md](spec/02-source-and-lexical-structure.md)
- [spec/03-types-and-values.md](spec/03-types-and-values.md)
- [spec/04-arrays-indexing-and-slices.md](spec/04-arrays-indexing-and-slices.md)
- [spec/05-raw-pointers-and-unsafe-memory.md](spec/05-raw-pointers-and-unsafe-memory.md)
- [spec/06-declarations-and-containers.md](spec/06-declarations-and-containers.md)
- [spec/07-functions-and-callable-values.md](spec/07-functions-and-callable-values.md)
- [spec/08-generics-constraints-and-contracts.md](spec/08-generics-constraints-and-contracts.md)
- [spec/09-names-signatures-and-access.md](spec/09-names-signatures-and-access.md)
- [spec/10-overload-resolution-and-inference.md](spec/10-overload-resolution-and-inference.md)
- [spec/11-properties.md](spec/11-properties.md)
- [spec/12-expressions.md](spec/12-expressions.md)
- [spec/13-operators-and-assignment.md](spec/13-operators-and-assignment.md)
- [spec/14-control-flow.md](spec/14-control-flow.md)
- [spec/15-ownership-and-lifetime-analysis.md](spec/15-ownership-and-lifetime-analysis.md)
- [spec/16-scope-exit-and-destruction.md](spec/16-scope-exit-and-destruction.md)
- [spec/17-failure-handling.md](spec/17-failure-handling.md)
- [spec/18-modules-and-dependencies.md](spec/18-modules-and-dependencies.md)
- [spec/19-compile-time-directives.md](spec/19-compile-time-directives.md)
- [impl/20-compilation-configuration.md](impl/20-compilation-configuration.md)
- [impl/21-layout-runtime-and-code-generation.md](impl/21-layout-runtime-and-code-generation.md)
- [spec/22-core-execution-and-foreign-functions.md](spec/22-core-execution-and-foreign-functions.md)
- [spec/23-compiler-services.md](spec/23-compiler-services.md)
- [spec/24-suspension-and-asynchronous-tasks.md](spec/24-suspension-and-asynchronous-tasks.md)
- [impl/appendices/A-compiler-requirements.md](impl/appendices/A-compiler-requirements.md)
- [spec/appendices/D-deferred-features.md](spec/appendices/D-deferred-features.md)
- [spec/appendices/E-terminology.md](spec/appendices/E-terminology.md)
- [spec/appendices/F-syntax-summary.md](spec/appendices/F-syntax-summary.md)
- [STATUS.md](STATUS.md)
- [spec/schemas/check-output.schema.json](spec/schemas/check-output.schema.json)
