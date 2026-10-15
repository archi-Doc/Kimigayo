# Kimigayo 言語仕様・英語用語版

本書は[SPECJ.md](SPECJ.md)の派生版。[用語表記一覧](SPECJ_TERMINOLOGY.md)にある全分類の用語を英語表記に統一し、説明文は日本語のまま残した。規則・構文・API・固定出力文字列は変更していない。

言語版 **0.0.2**。2026年10月15日時点の `SPEC.md` と各章・付録、および参照される実装仕様を一つにまとめた日本語訳。規則、条件、禁止事項、保証を残し、重複説明と例を整理した。キーワード、API名、type name、構文記号は原文のままとし、章番号も引き継ぐ。

言語規則は1〜19・22・24章と付録F、ツールのcontractは23章とJSON schema、初期実装のcontractは20・21章と付録Aにある。付録D・Eと実装状況は参考情報。仕様への記載は実装済みを意味しない。規則の正式な根拠は英語版であり、この翻訳によって言語規則や実装範囲を変更しない。

## 目次

- [1 概要](#s1)・[2 ソースと字句](#s2)
- [3 typeとvalue](#s3)・[4 配列・添字・slice](#s4)・[5 生ポインター](#s5)
- [6 declarationとcontainer](#s6)・[7 関数](#s7)・[8 genericsとContract](#s8)
- [9 名前・シグネチャ・アクセス](#s9)・[10 overloadとinference](#s10)・[11 property](#s11)
- [12 expression](#s12)・[13 演算と代入](#s13)・[14 制御フロー](#s14)
- [15 ownershipとlifetime](#s15)・[16 scope終了とdestruction](#s16)・[17 失敗処理](#s17)
- [18 モジュールと依存関係](#s18)・[19 コンパイル時指示](#s19)
- [20 コンパイル設定](#s20)・[21 layout・ランタイム・生成コード](#s21)
- [22 Kimi・実行・外部関数](#s22)・[23 コンパイラーサービス](#s23)・[24 suspensionと非同期task](#s24)
- [付録A コンパイラーrequirement](#app-a)・[付録D 保留機能](#app-d)・[付録E 用語](#app-e)・[付録F 文法](#app-f)
- [検査出力のJSON schema](#check-schema)・[実装状況](#status)・[出典](#sources)

<a id="s1"></a>
## 1. 概要

### 1.1 目的

Kimigayoは、一貫性、速度、簡潔さ、安全性、使う楽しさを重視する言語である。旧版との互換性よりも、言語の一貫性と品質を優先する。

言語仕様はプログラムの正しさと意味を定める。実装仕様はビルド・実行・コード生成のcontract、STATUSは検証済みの実装範囲を定める。実装上のconstraintで言語規則を狭めてはならない。

```kimi
alias Kimi.Console

group Geometry
    func area(width: i32, height: i32) -> i32
        let sides = [width, height]
        return sides[0] * sides[1]
```

### 1.2 表記

user-definedのtype・Contract・Declaration Containerは慣例としてPascalCase、関数・field・property・変数・引数はcamelCaseを使う。組み込みtypeの綴りは固定する。

| 記号 | 用途 |
| --- | --- |
| `[]` | 配列・辞書、固定長配列type、添字、slice、capture一覧 |
| `()` | 引数、tuple、Unit、Function Type、条件、expressionのグループ化 |
| `<>` | type argument、関数の長さ引数 |
| `{}` | struct/enumのOrigin schema、名前付きtypeのbinding集合。bodyやコレクションには使わない |
| `=` | initialization・既定引数・代入。valueのacquisitionにはCopy/Move規則を適用 |
| `@` | 明示的なAdaptation、`@move`、`@copy`、`@follow` |
| `->` | 関数の結果type |
| `=>` | 単一項目のbody、引数名の対応、container別名の対象 |
| `:` | type annotation、名前付き引数、辞書要素、ラベル、基底・親Contract、`: base(...)`。bodyは導入しない |
| `#` | コンパイル時指示と属性 |
| `$` | 言語が提供するComposition Root操作。マクロではない |
| `;` | `task;`だけに使う |

typeはSemantics、CoreまたはオブジェクトのView Target、および必要なOriginから成る。

### 1.3 規範性

完全な準拠実装は、全ての有効なプログラムを受理して規定どおりに実行し、無効なプログラムは拒否する。部分実装は有効だが未実装の構文を`Unsupported`として拒否できる。無効なプログラムの受理、有効なプログラムへの別の意味の付与は許されない。付録Dの保留項目は構文や利用許可を与えない。

| 原文の表現 | 意味 |
| --- | --- |
| must / must not | 必須／禁止。違反時のコンパイルエラー・Abort・undefined behaviorは各規則に従う |
| may | 明示された条件内で許可 |
| should | 推奨。準拠の必須条件ではない |
| implementation-defined | 実装が許容範囲内で選び、その選択を文書化 |
| unspecified | 許容範囲内の任意の結果。文書化は不要。undefined behaviorとは異なる |
| undefined behavior | 以後の動作を規定しない。unsafeの約束への違反だけが原因となり、安全なコードだけでは起きない |

断りのない規則・要求は規範的である。例は新たな規則を追加しない。API例のシグネチャだけの記載は、bodyなしのdefinitionを許可しない。例は互いに独立し、アプリケーション固有のtype・APIはdefinition済みとする。`// Error:`は意図的な違反例、`text`blockはKimigayo以外の表記を示す。

<a id="s2"></a>
## 2. ソースと字句

### 2.1 ソースと行

ソースはUTF-8。不正なバイト列はコンパイルエラー。先頭のBOM（U+FEFF）は一つだけ許可し、bodyには含めない。各ファイルはKotonohaのSourceDocumentである。改行はLF、CRLF、CRを認め、位置は1始まりの行・列で表す。列はUnicodeスカラーvalue単位。字下げ、継続行、bodyの境界は付録Fに従う。

### 2.2 空白とコメント

literal・コメントの外で使える空白はU+0020と改行だけ。タブ、改行しない空白、その他の空白・制御文字はエラー。行末の空白は無視する。コメントはtokenを区切り、隣のtokenと連結しない。

- `//`は行末またはファイル末尾まで。
- `/* ... */`は最初の`*/`まで。入れ子にできず、閉じ忘れはエラー。
- literal内のコメント記号は文字列の内容。文字列補間内では通常の字句規則を使う。
- 同一行のblockコメントはtoken間に置ける。字下げはコメント前の空白で決まる。改行を含むblockコメントの終了行では、`*/`の後に空白とコメントしか置けない。

### 2.3 文書コメント

行頭の空白だけに続く`///`は文書行。ただし直後が`/`なら通常のコメント。`////`、コードの後の`///`、`/** ... */`も通常のコメントである。文書は意味・有効性に影響しない。

同じ字下げで連続する文書行が一つのblockとなる。空行・通常コメントで終了する。declaration前の属性・空行・コメント列のうち、ヘッダーに最も近いblockを一つ選び、その字下げがヘッダー基準位置と一致する場合だけdeclarationに付ける。付かなければ以前のblockへ戻らない。無関係な構文、指示、scopeを越えて探さない。推奨順序は文書、属性、declaration。

対象はcontainer、名前付き関数・requirement・specialization、`init`、`drop`、property、局所を含む`let`/`var`、enumの各ケース、associated type。引数、generic・Originパラメーター、payload、accessorの説明は親declarationに書く。各断片・overloadは独自の文書を持ち、継承・override・requirement実装・specializationで文書を引き継がない。除外された構文には付かないため、条件付きdeclarationの文書はその条件領域の内側に置く。

内容はMarkdown。慣例として`- name: 説明`を使い、標準項目名は`return`、`abort`、`safety`、`note`、`warning`、`example`。対応Markdown、抽出、表示、文書diagnosticはimplementation-defined。

### 2.4 token

綴りは連続していなければならない。連結すると別のtokenになる箇所には区切りが必要。認識できるtokenでも、その位置で使えるとは限らない。いずれのtokenも始められない文字はエラー。最長一致、genericの`>`分割、記号一覧は付録Fに定める。論理演算は`and`/`or`であり、`&&`/`||`は常にエラー。

### 2.5 名前

名前の先頭は英字・`_`またはUnicode一般カテゴリLu/Ll/Lt/Lm/Lo/Nl、続きには数字とMn/Mc/Nd/Pcも使える。Unicode 15.0.0を基準とし、その版でunassignedの文字、書式文字Cfは使えない。NFCでない綴りは自動正規化せずエラー。比較はスカラー列の完全一致で、大小文字を区別し、カルチャー・正規化・大文字小文字の折り畳みに依存しない。見た目が似た名前も別名。条件名にも同じ規則を適用する。

`_`単独は名前ではなく、次の場合だけ使える。

| 場所 | 意味 |
| --- | --- |
| 実行時パターン | ワイルドカード |
| 最後の`#case _` | その他の条件 |
| initial value付き局所の`[_ of T]`、`[N of _]`、`[_ of _]` | 長さ・要素typeのinference |
| `_ = expression` | explicit discard |
| `for`のbindingまたはそのtuple要素 | 名前を付けない反復value |

`let _`、`var _`、引数名・参照としての`_`はエラー。`_value`などの長い名前は通常どおり使える。

### 2.6 数値literal

ASCII数字で始まり、type接尾辞はない。符号は演算子だが、literalに直接付いた符号は直接literalのtype決定に含める。typeは文脈または`123@i32`などから決まる。整数は10進、および`0b`・`0o`・`0x`（大文字も可）。区切りの`_`はvalueに影響しない。基数接頭辞直後の複数の`_`は許されるが、数字が一つ以上必要。

小数点の直後と指数の符号直後には10進数字が必要。`1.`は整数`1`と`.`、`1e_2`は不正。数値の直後に名前の継続文字があれば、不正な一つのtokenとして扱う（`123abc`、`0b102`など）。浮動小数点数は正確な10進valueからf32/f64へ一度だけ最近接・偶数丸めを行う。無限大になるvalueはエラー。ゼロ・非正規数は許可する。その他は整数で、絶対valueが2¹²⁸−1を超えるとエラー。

### 2.7 エスケープ

| 表記 | value |
| --- | --- |
| `\0` | NUL（U+0000） |
| `\\` | バックスラッシュ |
| `\e` | ESC（U+001B） |
| `\t`、`\n`、`\r` | タブ、LF、CR |
| `\"`、`\'` | 二重引用符、一重引用符 |
| `\u(H...)` | 1〜6桁の16進スカラーvalue |

U+10FFFF超、サロゲートU+D800..U+DFFF、不明・未完のエスケープはエラー。二つのサロゲートエスケープを組み合わせることもできない。

### 2.8 文字

`'A'`のように単引用符で囲む。一つのUnicodeスカラーvalueを直接またはエスケープで書き、typeは`char`。表示上の一文字とは限らず、結合文字単独は許可する。正規化しない。一重引用符、バックスラッシュ、U+0000..001F、U+007F..009F、U+2028、U+2029はエスケープ必須。補間はない。`''`、`'ab'`、`'e\u(301)'`、`'🇯🇵'`はいずれも一つのスカラーvalueではないためエラー。

### 2.9 文字列

typeは`string`で、内容はUTF-8。両形式とも複数行にまたがれる。

| 形式 | 区切り | エスケープ・補間 |
| --- | --- | --- |
| 通常文字列 | `"..."` | あり |
| 生文字列 | 同じ個数の3個以上の`"` | なし |

文字列には、終了区切りと通常文字列の未エスケープの`\`を除く任意のスカラーvalueを直接書ける。文字literalの制御文字制限は適用しない。各ソース改行は一つのLFとなる。字下げ、最初・最後の改行を除去せず、正規化もしない。CRには`\r`を使う。

通常文字列の`\(expression)`はvalueのテキストを挿入する。通常の字句解析で対応する`)`を探し、内側の文字列・コメント内の括弧は数えない。補間内の字下げは付録F、深さの上限はimplementation-defined。

生文字列の開始位置では連続する`"`をすべて数える。1個は通常文字列、2個は空文字列、N≧3個は長さNの生文字列区切り。開始列を分割しないため、内容の先頭を`"`にはできない。後に初めて現れるM≧N個の引用符列で終了し、先頭M−N個は内容、末尾N個は区切りとなる。短い列は内容である。したがって`""""""`は空の生文字列ではなく未終了の開始区切り。隣接する文字列は連結しない。

```kimi
let text = "合計: \(price * quantity)"
let path = """C:\Users\name\file.txt"""
let tail = """text""""              // 内容は text"
```

<a id="s3"></a>
## 3. typeとvalue

typeは**Semantics（所有・borrowなどの意味）＋Core（valueの種類・構造・identity）＋Origin（borrowのlifetime）**で決まる。一層は`Semantics/Core during Origin`と書く。Coreの要素・type argumentも完全なtypeであり、内部のborrowやOriginを保持する。オブジェクトの対象には、Coreの代わりに実行時Contract viewを置く拡張もある（現版の制限は§8.12）。省略は明示されたinference・省略規則だけで認める。

### 3.1 基本type

Scalarは通常整数12種、`f32`・`f64`、`bool`、`char`、ラッピング整数12種の閉じた集合。ソース上のtype nameではない。`string`、Unit、NeverはScalarではない。「整数type」はownerの通常整数を指し、ラッピング整数を含める場合は明記する。

| type | 格納ビット数 |
| --- | --- |
| `i8`、`u8`、`bool` | 8 |
| `i16`、`u16` | 16 |
| `i32`、`u32`、`f32`、`char` | 32 |
| `i64`、`u64`、`f64` | 64 |
| `i128`、`u128` | 128 |
| `isize`、`usize` | 対象のポインター幅 |

`bool`の有効なビット列はfalseの`0x00`とtrueの`0x01`だけ。他の表現をboolとして読むとundefined behaviorになる。

**ラッピング整数。** `::Kimi.Wrapping<T>`はTと同じ表現・value解釈を持つ別のScalarであり、算術オーバーフローを2ᴺの剰余にする。Tは通常整数、genericなら`T is PrimitiveInteger`のproofが必要。float、参照、Wrappingの入れ子は不可。ローカルで同名のtypeをdefinitionしてもこの扱いにはならない。公開constructorやmemberはなく、literal・演算・conversionで作る。Tや別のWrappingとの暗黙conversionはなく、どちらにもfitするliteralのoverloadは曖昧となる。

WrappingはCopy・Owned・Sealed・ObjectPayload・読み取りtypeであり、Tの比較・UTF-8書式・算術Contractにconformanceする。符号なしでもNegatableにconformanceし、`&`・`|`・`^`を使える。一方PrimitiveInteger・Positionではなく、添字、範囲境界、前置`^`、配列長、定数添字、シフト量には使えない。

**文字と文字列。** `char`はU+0000..D7FFまたはU+E000..10FFFFの一つのスカラーvalue。unassignedのvalueも有効。UTF-8の1バイトではなく、u32と同じ表現とも限らない。`string`はUTF-8テキストで、owner/stringはNon-Copy。layoutはimplementation-defined。

**UnitとNever。** `()`はvalueが一つのUnit type。記憶領域が0バイトでもinitialization状態とlifetimeを持つ。`::Kimi.Never`はvalueもconstructorもないtypeで、`return`・`exit`・`continue`・`yield`・`$abort`のexpression typeである。各転送のオペランドは転送先へ結果を渡す。他の制御expressionがNeverになるかは、全結果源と構造的完了を検証して決める。実行時に到達しないだけでは結果を省略できない。Neverは完了状態ではなくtypeである。

NeverはCopy・Owned・Transferableだが、Sealed・ObjectPayload・PrimitiveInteger・Callableではなく、他のContractにもconformanceしない。type argument、field、tuple・配列要素、enum payloadに置けるが、Neverをインラインに格納するvalueやケースは構築不能。それでもmatchの網羅性ではそのケースを除外しない。`Result<i32, Never>`でもErrの分岐が必要となる。

### 3.2 複合typeと呼び出し可能value

名前付きCoreは`A.B<T, U>`のように修飾できる。入れ子のtypeは、使っていないものも含め外側の正規化済みbindingを保持するが、外側インスタンスは暗黙に持たない。

`()`: Unit、`(T,)`: 1要素tuple、`(T,U)`: 2要素tuple、`(T)`: グループ化。Function Typeの引数列はtuple typeとは別で、`(T)->U`と`(T,)->U`はTを一つ、`(())->U`はUnitを一つ、`((T,))->U`はtupleを一つ受け取る。`task;`は引数ではない。

| 呼び出し可能valueのCore | contract |
| --- | --- |
| Function Item | 解決済みの関数declaration・実体化を一つ指す。captureなし、Copy、Shared呼び出し。binding引数が条件を満たすとOwned |
| 具体的closure | 匿名関数のexpressionと実体化ごとに固有のtype。同じexpression・type argumentなら評価ごとに同じCore。全captureがCopyならCopy。環境にユーザーのdropはdefinitionできない |
| common Function Type`(A...)->R` | Ownedな環境をtype erasureして所有するNon-Copy value。Shared呼び出しだけを公開し、環境に依存するborrow結果は返せない。引数・結果自体はOwnedでなくてもよい |

具体typeはCallable genericsを通してもreceiver・lifetimeのcontractを保持する。common Function Typeへのconversionはそのcontractを満たす場合だけで、conversion結果が環境の後始末を所有する。task slotの有無はtype identityの一部で、相互conversionできない。直接のborrow入力で省略したOriginは呼び出しごとにbindingされる。明記したOriginと既にbindingされた内部依存は固定。空の環境は純粋性・無allocation・並行安全性を意味しない。共通function valueへのrefはCopy、uniqはNon-Copyだが、呼び出し能力を追加しない。

**Weak。** `Kimi.Weak<s/T>`はsがrc/arcだけを許すコンパイラー管理のNon-Copy struct。対象はObject Targetで、外側はowner。payloadを所有せず、弱参照一つの責任を持つ。移動は`@move`、複製は`Kimi.Intrinsics.clone`。field・dropは置き換えられない。空valueはなく、不在にはOptionを使う。期限切れも存在するWeak valueである。分散・Loan・OwnedOriginsの解析では常にSを格納したものとして扱う。対象へアクセスするにはupgradeが必要で、直接のmemberアクセス、オブジェクトborrow、`is`、cast、view conversionはない。

**Optional。** `T?`は名前探索に関係なく`::Kimi.Option<T>`。`??`は入れ子を保持し、平坦化しない。

| 表記 | 展開 |
| --- | --- |
| `ref/T?` | `Option<ref/T>` |
| `ref/(T?)` | `ref/Option<T>` |
| `[N of T?]` / `[N of T]?` | `[N of Option<T>]` / `Option<[N of T]>` |
| `(T)->U?` / `((T)->U)?` | `(T)->Option<U>` / `Option<(T)->U>` |
| `ref/T? during a` | `Option<ref/T during a>` |

Originの付着を決めてからOptionalを展開する。binding集合は`View<T>{v}?`と書き、`View<T>?{v}`や重複接尾辞は不可。`(A)? -> B`も不可。名前専用の位置には使えず、`is Dog?`・`T?.Some`・`@ref?`は無効。`x@ref/T?`はOption typeへのAdaptationであり、borrowやSomeの生成ではない。暗黙のSome化、展開、default value、引数省略はない。`raw/T?`のNoneとSome(null)も別のvalue。

### 3.3 Semantics

| 分類 | 表記 | 意味 |
| --- | --- | --- |
| value | `T`、`owner/T` | valueを直接所有 |
| valueborrow | `ref/T`、`uniq/T` | valueへのshared borrow／排他的な変更可能borrow |
| object | `obj/T`、`rc/T`、`arc/T` | 単独所有／非原子的参照計数／原子的参照計数のオブジェクト |
| objectborrow | `objref/T`、`objuniq/T` | オブジェクトへの共有／exclusive borrow |
| 分類なし | `raw/T` | 生ポインター |

追加の分類はborrow＝ref/uniq/objref/objuniq、owning＝owner/obj/rc/arc、reference＝owner以外（rawを含む）。以上7分類だけをdefinitionする。分類名はpair binderに対するrequirementでは予約されるが、それ以外では通常の名前。分類自体はtypeやAdaptationの略記にならない。

genericのSemantics位置を占められるのは`<s/T>`のpair binder sだけ。分類は外側一層を検査し、`owning`は再帰的なOwnedとは異なる。`reference`は安全なborrowを保証しない。`s is owning or borrow`はrawだけを除く。

`ref/(rc/T)`はhandleを置くslotを借り、`objref/T`は対象オブジェクトを借りる。value borrowは格納されたポインターを自動追跡しない。shared borrowは複数共存できるが、exclusive borrowに競合する利用は共存できない。オブジェクトは動的payload・type情報・所有責任を保持するが、ヒープ配置は必須ではない。rc/arcは強参照がある間生存する。原子的計数は並行変更を安全にはしない。

**viewとidentity。** オブジェクトview typeはSemantics、静的なView Target、borrowなら外側Originを持つ。具体payloadにはObjectPayloadが必要。Sealedな対象はpayload全体であることをproofする。動的typeは構築したCoreで固定。実行時identityはCoreからOriginだけを再帰的に消し、type argument中のSemanticsは残す。したがって`Box<ref/i32 during a>`とb版は同一だが、`Box<i32>`とは異なる。

view変更や全体置換で動的type・identity・destruction責任は変わらない。同じアドレスへの新規構築は別のオブジェクトである。Supports(D,V)は動的type D自身、その基底、対応する実行時Contract viewとの関係だけを示し、アクセス権・Owned・Loanのproofにはならない。valueのスライシング、owner/ref/uniqの派生→基底conversion、containerのtype argument共変はない。オブジェクトのupcastは明示する。

**入れ子とOrigin注釈。** ref/uniq/rawは完全なtypeを包む。`during`は同じ注釈typeの最初の明示Semanticsへ、なければ直接書いた名前付きtypeの唯一のschema欄へ付く。括弧・別名・type argumentの内側を探さない。borrow層またはペア層だけが対象で、一つのAnnotatedTypeへ一回だけ付ける。グループ化で再注釈はできない。正規化は別名、括弧、冗長なownerを除くが、参照層は残す。外側のCopyは内部の排他的権限を複製しない。

通常のacquisitionは参照先を読まない。明示的な`@follow`、member等の経路選択、限定されたvalue読み取りを使う。安全な参照に`*`は使えず、参照経由でNon-Copy valueを取り出せない。

### 3.4 Placeと記憶領域

**Place**は記憶場所とその完全type・経路・権限・依存を表す。valueではないため、格納・引数渡し・Option化はできない。Read、Write、Takeは別の権限。所有valueのletはTakeできてもWriteできず、uniqの参照先はWriteできてもTakeできない。子Placeのtypeは親のSemanticsに置き換わらない。

アクセス指定はPlace、getter呼び出し、エラーのいずれかを一度だけ選ぶ。acquisitionやLoanの検査に失敗しても別経路へ戻らない。外側が安全なborrowならBorrow value、その他のPlaceならOwned Place、残りならOwned temporaryとして扱う。handleやrawのslotもOwned Placeである。receiver expressionとはメソッド構文のreceiverなどを指し、`Type.method(self: ...)`の通常引数、代入先、match/forの対象にはreceiverの省略規則を適用しない。

**貸出点。** `P@ref`・`P@uniq`はP自身を借りる。Pが参照を格納していてもslotを借りる。reborrowは参照先、オブジェクトborrowはオブジェクト、followしたborrowはpayload、基底projectionは基底部分を貸出点とする。lifetimeを守るために保護する所有者は、貸出点そのものではない。exclusive borrowにはその場所への排他的書き込み権限が必要。

**参照経路の選択。** member・添字・メソッドは、要求するdeclarationを完全typeが持つ最初の層で止まる。持たなければref/uniq、またはvalue/valueborrowだけを許すペア層をたどる。オブジェクト層はView Targetのdeclarationを持つためそこで止まる。各層の権限・Origin・Loanを保持し、選択はtypeだけで決める。constraintで形が決まらないgeneric typeは終端であり、実体化後に探索し直さない。

bare acquisition、単名binding、type inferenceは参照先を選択しない。更新要求は選択された経路だけをたどり、添字expression内部や関数・getterの背後には伝わらない。子Placeのacquisitionは子自身のvalue種別に従う。receiver expression以外で所有Placeを排他的に貸すには、そのPlaceに`@uniq`/`@objuniq`を書く。`holder@uniq.items`はholderを借り、itemsを借りる表記ではない。

### 3.5 Copy・Move・reborrow

Copyはユーザーコード・allocation・参照計数変更・資源獲得を行わず、valueを複製して元をinitialization済みのまま残す。Moveはvalueとdestruction責任またはborrowの権限を移し、元をMovedにする。ユーザーコードは実行しない。

| Place Pからのacquisition（上から適用） | 動作 |
| --- | --- |
| bare、typeがCopyとproof済み | Copy |
| bare、外側が`uniq/U`または`objuniq/U` | 同じSemanticsでreborrow |
| bare、その他 | エラー。`@move`が必要 |
| `P@move` | Copy typeでも移動し、PをMovedにする |
| `P@copy` | Copyのproofが必要。移動はしない |
| `P@ref`、`P@uniq` | P自体へのborrow |
| temporary valueのbareまたは`@move` | valueをそのまま移す。余分なCopy/Moveはない |

initialization・代入・引数・結果・集約要素・payload・default value・capture・destructionに同じ規則を使う。明示acquisition後の暗黙Adaptationは最大一つ。Copyがproofできないからといって暗黙Moveやborrowにはしない。`@owner/T`やオブジェクト作成・upcastもPlaceを暗黙Moveしない。Copy能力はlet/varやLoanとは独立だが、実際の操作は読み取り・移動の制限とLoanに従う。

**格納された排他的参照のreborrow。** bare acquisitionは、`P@follow@uniq`または`P@objuniq`に相当する子のborrowを作り、Pは移動しない。子Originは親以内で、Loanの系譜、initialization、slot保護、destruction依存を保持する。子が生きている間の競合する親利用は禁止し、最後の利用後に親を再利用できる。letや引数に格納したuniqでも可能だが、共有経路`ref/(uniq/U)`からは排他的にreborrowできない。

外側の排他的参照だけをreborrowする。Option・struct・配列全体には適用しないが、`[r]`は要素rを個別にacquisitionする。格納する子は形成時から生存し、呼び出しのreceiver・引数になる子は呼び出し予約を作る。`r@copy`は不可、`r@move`後のAdaptationはrを復活させない。直接fitする明示移動済みの排他的参照は、reborrowせず移す。

| type | Copy条件 |
| --- | --- |
| 整数・Wrapping・float・bool・char・Unit・Never | 常にCopy |
| ref・objref・raw・Slice・Function Item | 常にCopy |
| FromEnd・Start・End・Range・ClosedRange・ResolvedRange | 常にCopy |
| `Loan<T>` | TがCopy |
| closure・tuple・固定長配列 | 全構成要素がCopy |
| uniq・objuniq・obj・rc・arc・UniqSlice・common Function Type・Weak | Non-Copy |
| Array・Dictionary・owner/string | Non-Copy |
| ユーザーstruct/enum | 明示的なconformance declarationが必要 |

ユーザーtypeは`Self is Copy`または`Self is Copy when P`でconformanceをdeclarationする。structでは独自fieldと直接基底、enumでは全payloadがCopyであり、ユーザーdropを持たないことを全許容bindingでproofする。派生typeごと、payloadなしenumでもdeclarationが必要。fieldが全てCopyというだけではconformanceせず、Copy実装bodyも書けない。未知のCopyをNon-Copyと決め付けず、constraintを勝手に補わない。rc/arc/Weakの複製はcloneを使う。

**value読み取り。** 読み取りtypeはScalarまたはPosition/PositionRange conformance type。genericではPrimitiveInteger・Position・PositionRange、あるいは有効なWrappingのproofが必要。期待typeが読み取りtypeのvalue位置、結果、組み込み演算、bool条件、所有receiver、数値conversionなどで、安全なvalue参照層の終端をCopyできる。オブジェクト、raw、fieldは自動追跡しない。全層のinitialization・権限・Loanを検査し、余計なconversionやborrowは加えない。代入先は参照先へ転送しない。読み取りtypeでないCopy valueを参照から複製するには`@follow`を明示する。

### 3.6 temporary value

temporary valueは永続Placeではないexpression結果。必要時の実体化は一時Placeを与えるだけで、再評価・Copy・allocation・lifetime延長を伴わず、失敗した消費を読み取りへ変えない。新たに所有したtemporary valueは通常排他的に書き込めるが、getter結果などの制限は残る。

lifetimeは最外のexpressionの終了まで。引数のtemporary valueは呼び出し終了まで、match/forの対象評価中に作ったtemporary valueは構文全体の終了まで生き、対象の後に作成の逆順でdestructionする。他のtemporary valueも逆順にdestructionし、途中の転送にも後始末を行う。部分Move後は残った部分だけをdestructionする。各条件評価はlifetime境界。borrow・Slice形成でtemporary valueのlifetimeは延びない。結果valueはscope終了処理の前に確保する。

### 3.7 OriginとLoan

Originはborrowが有効なプログラム点の集合（領域）、LoanはどのPlaceをどの権限で借りるかを記録する。`origin a outlives b`と`origin a == b`は領域間の関係であり、同じOriginだから同じLoanや権限を持つわけではない。シグネチャ・type declarationに記し、局所ではinferenceする。

### 3.8 type関係

正規化されたtype identityは構造、declaration、type argument、Semantics、Origin bindingを比べる。binding Originパラメーターは対応するbinderで比べる。名目的に別のdeclarationは同一typeにならず、実行時オブジェクトidentityとも異なる。

唯一の部分type規則は、Origin以外が同一で、その各位置が分散を満たすこと。共変なら元Originが先Originより長く、反変なら逆、不変なら等しい。数値拡大、uniq→ref、派生→基底、Wrapping↔整数は部分type関係ではない。Neverも他typeの部分typeではない。

Never expressionは、有効な任意の期待value typeにvalue conversionなしでfitできる。これは部分type関係ではなく、完了しない経路で外側のvalue操作も実行しない。Function Typeの結果Neverを別の結果typeへ置き換えられるという意味ではない。type関係自体はborrow・Copy・Moveを許可せず、lifetimeも延長しない。typeが一致してもacquisitionやLoanに違反すれば失敗し、名前探索やoverloadをやり直さない。

<a id="s4"></a>
## 4. 配列・添字・slice

### 4.1 固定長配列

`[N of T]`のidentityは、評価済みの長さNと完全な要素type Tで決まる。長さの異なる配列、固定長配列とArray、所有配列とSliceの間に暗黙conversionはない。要素は添字順にインライン格納し、要素iのオフセットは`i * stride(T)`、全体の大きさは`N * stride(T)`。N=0でもTのalignmentを要求し、Tの有効性とインライン再帰を検査する。stride=0なら要素が同じアドレスを持てる。全体の大きさは非負isizeと対象実装の上限に収まらなければならない。

### 4.2 長さexpression

長さはコンパイル時の非負isize。整数literal、長さパラメーター、定数として読めるbinding、括弧、単項`+`/`-`、二項`+ - * / %`だけを使える。定数bindingは整数letの局所、またはアクセス可能な標準get付き静的格納valueで、initialization expressionも再帰的にこの条件を満たすもの。関数呼び出し、var、通常引数、インスタンスfield、custom get、循環は不可。

演算はtypedの検査付き整数演算。長さパラメーターはisize、literalだけの部分は確定した相手type、なければisizeにfitする。異なる整数typeを混ぜない。中間valueは負でもよいが、最終valueの負数・範囲外、途中のオーバーフロー・ゼロ除算はコンパイルエラー。公開typeの長さに非公開定数を使えても、その評価valueは公開APIの一部になる。

### 4.3 構築

配列literalは、期待typeが固定長配列なら要素数を厳密に合わせる。期待typeなしではArrayになる。呼び出しでは候補ごとにfitを検査し、失敗しても別形式へ変更しない。空配列には要素typeを決める期待typeが必要。

initial value付き局所注釈に限り`[_ of T]`・`[N of _]`・`[_ of _]`を使える。initialization expressionだけから全ての穴を決める。内側に固定長の期待typeがなければ、内側のliteralはArrayになる。type annotationだけのdeclarationは未initializationで、最初の全体構築より前に要素を個別代入して構築できない。

`[N of value]`は固定長配列の充填構築。要素typeはCopyが必要で、valueはN=0でも一度acquisitionする。

`noinit`はunsafe内の局所varで、穴のない固定長配列typeを明記し、initialization expression全体に括弧なしで書く場合だけ許す。要素はownerのScalarに限り、Copyというだけでは不足する。`(noinit)`は通常の名前expression。配列は構築済みとなるが、未書き込み要素の読み取りはundefined behavior。全体Copy/Move・value引数も全要素を読む。アドレスacquisition、borrow、view、長さ、destructionは要素を読まず、実装も利用者の操作より先に未書き込み要素を読んではならない。

### 4.4 関数の長さパラメーター

関数だけが`<length N>`をdeclarationでき、Nはvalue名前空間の一引数を占める。明示するtype argument列には全slotを書く。既知の固定長配列やliteralから同じ長さをinferenceし、期待typeは未確定部分だけを補える。`N * 2`の逆算、bodyや後続利用からのinferenceはしない。固定長候補を動的配列候補より優先しない。

シグネチャ内の依存長さexpressionは`ValidLength(E)`という公開の適用条件を持つ。具体的に偽なら選択前に候補から除き、順位付けには使わない。bodyで使えるproofは具体評価、長さslotの非負性、同じ正規化済み条件だけ。正規化はtyped定数の展開、括弧除去、非依存部分の評価、slotの位置対応、`+`/`*`直下の順序統一まで。再結合、分配、相殺による一般代数inferenceはしない。

### 4.5 ownership

固定長配列は要素typeがCopyの場合だけCopy。要素は添字の逆順にdestructionする。Arrayは任意の完全typeを格納できるNon-Copy所有value。removeやclear後もtypeとLoanの依存集合を縮めない。view形成は明示し、Arrayの構造変更には全体のexclusive borrowが必要。

### 4.6 位置と範囲

Positionは通常整数12種、`FromEnd<T>`、`Start`、`End`だけ。長さLが負なら解決は常に失敗。通常整数は0..L、`^offset`はL−offset、Start/Endは0/Lに解決する。要素位置にはさらに`q < L`が必要。`^(-1)`の生成自体は有効だが解決に失敗する。Start/End/FromEndには公開constructorがなく、通常の算術・順序・数値conversionもない。

`a..b`は半開範囲、`a..=b`は閉範囲。構文で範囲typeが決まり、境界typeは独立。省略境界はStart/Endとなる。生成時に境界を検査しない。literal境界は、期待する境界type、他方の整数type、i32の順でtypeを一度決め、失敗時に戻らない。Range/ClosedRangeにはlength/isEmptyがなく、解決済みの`ResolvedRange`とは別typeで相互conversionしない。

| 範囲 | 解決条件と結果 |
| --- | --- |
| Range | 境界をs,eへ解決し、s≦eなら`[s,e)` |
| ClosedRange | 終端qが要素位置かつs≦qなら`[s,q+1)` |
| ResolvedRange | もともと0≦s≦e。対象長さに対してe≦Lが必要 |

逆向きを空範囲へ丸めない。通常の添字・範囲操作は不正境界で`KIMI_E_INDEX_BOUNDS`。try系のNoneは、その操作自身の境界失敗だけを表す。

範囲の反復は両端が同じPrimitiveIntegerのRange/ClosedRange、またはResolvedRangeだけ。省略端、末尾相対、異typeの両端は反復できない。逆向きはAbort、1ずつ増加し、閉範囲の最大valueの次を計算しない。範囲自体のLoanは残さない。

PositionはEquatable・Utf8Format・Copy・Ownedを要求し、`tryResolve(self: Self, length: isize) -> Option<isize>`を持つ。PositionRangeも同じ能力を要求し、結果は`Option<ResolvedRange>`。FromEnd<T>はT is PrimitiveIntegerを必要とし、読み取り専用offset:Tを持つ。FromEnd・Start・End・各範囲typeはresolveも公開する。整数はPosition constraint経由のtryResolveだけを使い、整数自身にmemberやresolveを追加しない。Range/ClosedRangeは読み取り専用start:S・end:E、ResolvedRangeはstart/endとlength/isEmptyを持つ。異なる範囲形は別typeで比較できず、ResolvedRangeはindicesか解決操作だけが作る。

### 4.7 添字

単一添字は`Indexable`/`UniqIndexable`を使い、キーはshared borrowする。indexとindexUniqは同じキーに対して同じ要素を選び、indexUniqは格納typeを変えず排他権限を与える。具体的な標準シーケンスはPositionを一度isizeへ解決し、一度境界を検査する。一般の`Indexable<isize>`constraintだけでは`^n`を使えない。その他のキーtypeは一意のconformanceが必要で、順位付け、期待結果、既定typeによる候補選択はしない。

利用箇所の必要権限からindexまたはindexUniqを一度選ぶ。失敗しても変更しない。直接の固定長配列は要素projection、Arrayは全体borrowとなる。範囲添字は別のview操作であり、範囲全体への代入はない。

要素Placeは格納された完全typeを保つ。Copy要素はbareでCopy、外側が排他的参照ならreborrow。Non-Copyの要素Moveは、所有する固定長配列の範囲内整数literalによる静的経路だけに許す。

lengthとindices、固定長配列以外のisEmptyは要素を読まず、結果にLoanを持たない。indicesはacquisition時点の範囲のスナップショット。対象とキーは各一回評価し、キー評価中の対象の変更・Move・destruction・再allocationを禁止する。Slice handleは先にCopy、UniqSliceはborrowする。境界違反は実行時Abortであり、それだけを理由にコンパイルエラーにしない。末尾の空範囲は有効だが、末尾位置を要素としては使えない。

### 4.8 SliceとUniqSlice

`Slice<T>{source}`はinitialization済み連続領域へのCopyな共有view。長さはtype identityに含めない。添字と末尾相対位置はview自身の長さを使う。入れ子配列の行は`Slice<[N of T]>`であり、平坦化も異なる要素typeへのconversionもない。

コピー、再slice、iterator、要素参照は元領域のOriginとLoanを保持し、handle変数は借りない。空viewや再sliceも配列全体のLoanを保つ。要素を所有せず、TはOwned不要。sourceを短くできても長くはできず、temporary valueのlifetimeも延ばさない。var Sliceやuniq/Sliceはhandleの変更を許すだけで、要素は共有のみ。等価演算はない。

Sliceの各memberはhandleをvalueで受け、borrow結果はsourceに依存する。

| 操作 | contract |
| --- | --- |
| `toArray() -> Array<T>` | TはCopy。各要素を順に一回Copy。最大1回allocation、空なら0回。外側のborrowは除くがT内部の依存は残る |
| `tryGet<P>(index: P) -> Option<ref/T>` | PはPosition。要素位置でなければNone。Copy typeでも参照を返す |
| `trySlice<R>(range: R) -> Option<Slice<T>>` | RはPositionRange。不正範囲ならNone |
| `splitAt<P>(index: P) -> (Slice<T>, Slice<T>)` | PはPosition。一度pへ解決して`[..p]`と`[p..]`を返す |
| `trySplitAt<P>(index: P) -> Option<(Slice<T>, Slice<T>)>` | PはPosition。同じ分割、失敗ならNone |
| `contains(value: ref/T) -> bool` | TはEquatable。等価な要素の有無 |
| `firstIndex(of: ref/T) -> Option<isize>` | TはEquatable。最初に等価な添字 |
| `firstIndexWhere<F>(predicate: F) -> Option<isize>` | Fは`Callable<(ref/T)->bool>`。最初にtrueとなる添字 |

検索は添字順に共有参照を渡し、最初の一致で止まる。固定長配列とArrayにもtryGet・trySlice・splitAt・trySplitAtがあり、`x[..]`と同じ動作で結果はselfに依存する。Sliceは共有・排他・valueの全反復モードで`ref/T during source`を返す。結果はiteratorのslotに依存せず、nextを越えて保持できる。要素内の参照のOriginは、要素slotのborrowと別に保持する。

| 提供type | `slice` | `sliceUniq` |
| --- | --- | --- |
| 固定長配列・Array | ref/SelfからSlice、during self | uniq/SelfからUniqSlice、during self |
| Slice | valueからSlice、元source | なし |
| UniqSlice | ref/SelfからSlice、during self | uniq/SelfからUniqSlice、during self |

各操作に全範囲版とPositionRange引数版がある。範囲添字`x[r]`は常に共有の`x.slice(r)`。

UniqSliceはinitialization済み連続領域へのNon-Copyな排他view。要素を所有せず、読み取り・置換・borrowはできるがMoveできない。排他操作にはvar所有valueか排他的参照が必要。暗黙Copy/Move/reborrow/conversionはなく、`@move`、slice、sliceUniqを明示する。反復とtoArrayはない。Tは不変。子viewは親の現在のborrowに依存し、共有の子が生きている間は変更不可。範囲が違うだけでは非重複をproofしない。

Arrayと固定長配列の`tryGetPairUniq<P,Q>(self: uniq/Self ! first: P, second: Q) -> Option<(uniq/E during self, uniq/E during self)>`は、PositionにconformanceするP,Qを一度解決する。不正または同じ論理要素ならNone、異なれば引数順に非重複な二つの参照を返す。O(1)、変更・allocation・ユーザーコード呼び出しなし。別々に作る排他的要素borrowには静的な非重複proofが必要。

位置・範囲の構築/解決/Copy、view・要素Placeの形成/Copy/再分割はO(1)、allocation・参照計数変更なし。標準iteratorの形成・destruction・nextはO(1)（Dictionaryでは償却）、全走査O(1+n)、管理領域O(1)、追加ヒープallocationなし。所有する要素領域と要素Move/destructionの費用は別計上する。

### 4.9 動的コレクション

ArrayとDictionaryはNon-Copy所有value。DictionaryのKはEquatableとキー安定性を必要とするが、公開Hash constraintはない。変更はuniq/self、除去は格納責任を転送、premise違反はAbort、不在はOption、回復可能な拒否はResultで入力を返す。引数・比較・dropの副作用を巻き戻さない。

追加は既存順を保ち、置換はvalueだけを変える。通常の除去は残りの順序と容量、clearは容量を保つ。検索・拒否は変更しない。reserve/shrinkはvalue・順序・長さを保つ。命名は`sorted/sort`、`tryGet/tryGetUniq`、`peek/next`、`reading/nextReading`、`item/intoItem`のように責任を分ける。

| Arrayの操作 | 動作 |
| --- | --- |
| `init(! capacity: isize)` | 空を作る。負数はAbort |
| `init(! repeating: T, count: isize)` | TはCopy。入力を0個でも一回acquisition。負数は配置前にAbort。allocation最大1回、0個なら0回 |
| `append(value: T)` / `pop() -> Option<T>` | 末尾へ追加／末尾から除去 |
| `insert<P>(index: P, value: T)` | 境界位置へ挿入。`^0`は末尾 |
| `remove<P>(index: P) -> T` | 順序を保って除去 |
| `swapRemove<P>(index: P) -> T` | 除去位置へ末尾要素を移す |
| `swap<P,Q>(first: P, second: Q)` | 交換。同じ要素なら無変更、不正位置はAbort |
| `truncate(length: isize)` / `clear()` | 指定長さ以降／全要素をdestruction。長すぎるtruncateは無変更、負数はAbort |
| `appendAll(other: Array<T>)` | 全要素を移して追加 |
| `appendCopies(values: Slice<T>)` | TはCopy。全要素をCopyして追加 |
| `removeAll<F>(matching: F)` | 添字順に各要素へ共有述語を一回適用。一致を除去し残りの順序を保つ |
| `reverse()` | 逆順にする |
| `sort()` / `sort<F>(by: F)` | Comparable.compare／共有二引数でi32を返す比較関数に従い非減少順。安定ソートではない |
| `first` / `last` | 共有receiverから`Option<ref/T during self>`。空ならNone |
| `tryGetUniq<P>(index: P)` | `Option<uniq/T during self>`。不正要素位置ならNone |

P,QはPosition。removeAllのFは`Callable<(ref/T)->bool>`、sort(by:)のFは`Callable<(ref/T,ref/T)->i32>`で、負・0・正が小・等・大を表す。Array内部の要素移動はCopyもdropも呼ばない。removeAll/sortのコールバック中にArrayを観測できない。コールバックがAbortしても全要素はそれぞれ一回initializationされた状態を保つ。不整合な比較の結果順はunspecifiedの並べ替えとなる。

| Dictionaryの操作 | キー不在 | 等価なキーあり |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(),(K,V)>` | 末尾追加、Ok | 入力をErrで返す |
| `insertOrReplace(key: K, value: V) -> Option<V>` | 末尾追加、None | 既存キーを保ち、旧valueをSomeで返す |
| `remove(key: ref/K) -> Option<(K,V)>` | None | 格納項目を返す |
| `tryGet(self: ref/Self, key: ref/K) -> Option<ref/V during self>` | None | valueの共有参照 |

両入力をacquisitionしてから検索する。insertOrReplaceは配置後に未使用の入力キーをdestructionする。格納キーを変更する操作はない。再追加は新しい末尾位置となる。tryGetはvalueをCopyせず、辞書全体をLoanするが検索キーには依存しない。

`map[key]`は既存valueのPlaceで、不在はAbort。添字代入は挿入ではない。右辺acquisition→対象とキーの評価→共有検索→旧value destruction→新value配置の順。途中の辞書再構築・Move・destructionは禁止し、書き込み前にキーも含むLoanを確認する。旧value destructionで右辺依存が無効になる置換は拒否する。複合代入でも検索は一回。

**容量とallocation。** `0 <= length <= capacity`。typeが確定した空の`[]`/`[:]`はallocationなし。reserveはlength+additionalを満たし、負数・オーバーフローはAbort、縮小はしない。既存容量で足りればallocationも移動もない。shrinkToFitのallocation失敗は無変更。容量内追加、検索、置換、除去、clear、拒否はallocationなし。正確な容量はimplementation-defined。

**Loanとeffect。** 排他的receiverは後続引数より前に予約し、入口で有効化する。その時点まで生きる要素・SliceのLoanは競合する。追加・置換の入力依存はコレクションへ結合し、tryInsertがErrでも結合する。除去valueは完全な依存を保持し、依存集合を引き算する操作はない。

| 操作 | 公開するユーザーコードeffect |
| --- | --- |
| Arrayのclear/truncate/destruction、所有iteratorのdestruction、要素置換 | 対象要素Tのdestruction |
| removeAll | 述語と除去要素のdestruction |
| sort / sort(by:) | compare／比較関数 |
| Sliceのcontains/firstIndex/firstIndexWhere | 等価比較／述語 |
| Dictionaryの検索・挿入・除去・index/indexUniq | Kの等価比較 |
| DictionaryのinsertOrReplace | Kの比較、未使用入力Kのdestruction |
| Dictionaryの添字置換 | Kの比較、旧Vのdestruction |
| Dictionaryのclear・destruction・所有iterator destruction | VとKのdestruction |
| その他の位置・範囲・view・メタデータ・容量・標準要素移動操作 | なし |

この表と入力アクセス・依存・allocation・Abortが完全な公開effectであり、呼び出し側はライブラリーbodyを調べない。Callableのeffectやtype依存のdestructionは実引数へ合成する。抽象typeのまま分類できないdestructionを、effect boundの検証で無effectとはしない。

境界解決・検索後に結果を決め、不在・拒否は無変更。サイズとallocationを検査するのは追加時だけ。Arrayは逆添字順、Dictionaryは逆挿入順で各value→キーの順にdestructionする。removeAllのdestructionは最後の述語呼び出し後、順序unspecified。所有iteratorの未返却要素も同じ順序でdestructionする。

| 操作（ユーザーコードを除く） | 計算量 |
| --- | --- |
| Array append / pop | 償却O(1) / O(1) |
| swap・swapRemove・first・last・tryGet・tryGetUniq | O(1) |
| truncate・clear | O(1+d)、dはdestructionが必要な要素数 |
| appendAll・appendCopies・充填init | 要素あたり償却O(1) |
| insert・remove・removeAll・reverse、Slice検索 | O(1+n) |
| sort | 最悪O(n log n)、allocationなし |
| Dictionary検索／変更 | 検索費用＋O(1)／償却O(1) |
| Dictionary clear | O(1+c+d) |

shrinkToFitなしのm操作で総成長費用はO(m+C)。Cは最大容量・長さ・reserve要求。reserve(1)とappendの反復でも全体O(n)となる。辞書検索には比較・ハッシュ・探索を含み、線形検索も許される。

<a id="s5"></a>
## 5. 生ポインターとunsafeメモリー

unsafeは検証責任をプログラマーへ委ねるが、言語規則を免除しない。安全なコードだけでundefined behaviorは起こらない。raw Place形成、ポインター算術、unsafe関数呼び出し、noinitには字句的unsafe領域が必要。null、等価比較、raw type conversion、アドレスacquisitionには不要。

### 5.1 nullと等価比較

nullには期待raw typeまたは`@raw/U`が必要。安全な参照はnullにならない。同じraw type同士はアドレスで等価比較でき、ダングリングvalueでも比較自体は可能。ただし有効なprovenanceやアクセス権のproofにはならない。順序比較はない。

### 5.2 raw Place

`*p`、`p[n]`とそこからの子projectionはraw Place。通常のCopy・代入規則を使うが、未initialization記憶領域は`Raw.initialize`でだけinitializationする。アクセスには次の全条件を満たす必要がある。

1. ポインターのprovenance（由来の領域）内。`x[0]@raw`を作っただけではx[1]へ進めない。
2. 記憶領域が生存し、alignmentを満たす。読み取り時はtypeとして有効なinitialization済みのvalueがある。
3. 元経路が必要な権限を持つ。Takeとdestructionにも書き込み権限が必要。
4. acquisition元の経路自身を除く、生きたLoanと競合しない。

局所のアドレスはdeclarationからscope末まで固定で、Move後も記憶領域は残る。オブジェクトpayloadのアドレスは公開からdestruction開始まで固定。raw所有containerもtype argumentや`Loan<T>`により、格納valueの安全な参照依存を保持しなければならない。

raw Placeからのborrowは新しいアンカーとLoanを作り、所有者を復元しない。期待Originへfitでき、staticのexclusive borrowも構文上は可能だが、全期間の生存・排他性は作者の約束となる。raw Take後のinitialization状態は追跡しない。以後の読み取りや二重destructionを防ぐ責任は作者にある。`_ = (*p)@move`はその場のvalueを取り出してdestructionする。

### 5.3 算術と添字

許すのはrawポインターにisizeの加減算で、変位はstride(T)倍。0変位はnullを含めそのまま返す。非0変位では元と結果が生存するprovenance内または一つ先の位置でなければならず、計算オーバーフローやアドレスの巻き戻りはundefined behavior。境界検査はない。

### 5.4 アドレスとconversion

`P@raw`は書いたPlaceのアドレスを取る。即座に終了するshared borrow相当の検査を行うが、valueを読まず、持続Loanも作らない。temporary valueのlifetimeは延ばさない。`@raw/U`はアドレスとprovenanceを保つtype conversionであり、新たな権限を与えない。同じ正規化済みtypeなら通常CopyとなりOriginも保持する。

### 5.5 整数アドレス

raw↔usize conversionは単一アドレス空間、ポインターに付加状態なし、幅整合を保証する対象だけで認める。それ以外はUnsupported。整数はprovenanceを持たず、往復して得られるのは同じアドレスだけ。アクセスを正当化するには実装が文書化した保証が別途必要。

### 5.6 生の記憶領域操作

```kimi
public group Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

| 操作 | contract |
| --- | --- |
| allocate | count個の未initialization領域。負数、サイズ計算オーバーフロー、allocation失敗はAbort。alignment上限超はコンパイル時Unsupported。0バイトでも非nullでaligned、長さ0のprovenanceを返す。0バイト結果同士のアドレスは同じでもよい |
| release | nullと0バイト結果は無操作。それ以外は未deallocation allocate領域の先頭で、全要素を既にdestructionまたはTake済みでなければならない。deallocation後は領域内の全ポインターが無効 |
| initialize | 生存・alignment・書き込み可能でinitialization済みvalueを持たない場所へvalueをMove |
| slice | 非負lengthは呼び出し側のobligation。0ならnullを含む任意ポインターを許しアクセスしない。正なら非null、alignment、isizeに収まるサイズ、provenance内の連続要素が必要。要素はinitialization済みかnoinit配列に属するもの |

slice形成は要素を読まないが、noinitの未書き込み要素は読む前に書く必要がある。結果Origin sは新しいアンカーであり、その全期間、要素への書き込みを禁止する。effect上、allocateはallocation、その他はrawアクセスとして扱う。

<a id="s6"></a>
## 6. declarationとcontainer

### 6.1 Declaration Container

groupは静的memberの集合、structはvalueの構造、enumは閉じた選択type、contractは静的な能力requirementを表す。groupは独自のgeneric引数を持たず、contractは格納領域や実装bodyを持たない。extensionとstatic修飾子はない。

ソース直下の局所binding・名前付き関数・実行項目は、その文書だけの実行scopeに属する。container側からその局所valueは見えず、名前付き関数はcaptureしない。直下の関数でアクセス修飾を許すのはpublic mainだけ。

rootgroupはソース直下でだけdeclarationでき、groupのパスを開く。同名の非groupと衝突し、暗黙に作る途中のgroupは既定private。containerを入れ子にできるのはソース直下・group・structで、enum・contract・実行blockには置けない。group/rootgroup/struct/contractはbodyなしでもよい。enumは条件選択後に一つ以上のケースが必要。

**断片。** 同じKotonoha・親・名前・種類・引数数のgroup/struct declarationは断片として結合する。引数の種類・順序・名前、修飾、アクセスは一致し、Originヘッダーは全断片で同じか全て省略する。constraint領域と基底節はそれぞれ最大一つ。基底のbinding集合名はその断片内だけ。結合後に重複とdrop最大一つを検査する。enum/contractは断片化できない。

入れ子のdeclarationは、未使用分も含む外側のtype・Semantics・Origin等の環境を引き継ぐが、実行時のvalueやreceiverを引き継がない。declaration参照のidentityはdeclarationと正規化済みの全内外bindingで決まり、proof文脈は含めない。入力条件は仮定、Self conformanceなどはproof obligationであり、内側のdeclarationが外側のobligationを仮定に変えることはできない。

Selfは最も内側のstruct/enum/contractを指す。途中のgroupは新しいSelfを作らないが、groupにconstraint・conformance・associateを直接置くことはできない。

### 6.2 structと継承

記憶領域を持つのはfieldだけ。インライン格納で同じdeclarationを再訪する循環は、type argumentが違っても0長配列を介してもdeclarationエラー。borrow/raw/objectの間接層で循環は切れる。

fieldの論理順序は、文書の論理名のUTF-8バイト順、次にソース位置順。生成文書は通常文書の後で決定的に並べる。この順序がinitializationと逆順destructionを決め、物理layoutとは独立する。

structは既定でsealed。継承先にできるのはアクセス可能なopen struct一つだけで、constraint・アクセスを満たし、循環してはならない。直接基底は先に構築されるインライン部分。基底view全体をMove・置換・再構築できず、valueスライシングもない。アクセス可能な祖先のvalue名を派生typeで再declarationできるのは正しいoverrideだけ。overload追加や条件付きdeclarationもこの禁止を免れない。

### 6.3 constructor

`init`はアクセス、引数、任意の`: base(...)`、bodyを持つ。名前・receiver・task・独自generic引数・結果typeは持たず、既定private。構築は`T.init(...)`であり、`T(...)`は使えない。

`.init(`は常にconstructor構文。typeと外側環境を先に決め、そのtype自身のアクセス可能なconstructorだけを選ぶ。自分のtype argumentはinferenceできるが、外側引数をinferenceしない。継承、field別の暗黙構築、ゼロ埋め、type parameterの直接構築、function value化はない。

構築順は、明示引数→領域確保→基底引数（self使用不可）→基底構築一回→独自field initializerを論理順に実行→body。field initializerはconstructor引数もselfも参照できない。

構築中selfは、initialization済みの独自fieldだけにアクセスできる。varは排他的に利用でき、letは読み取りのみ。Non-CopyのMove、computedや継承member、self全体、メソッド、custom accessor、capture、外部公開は禁止する。field Loanは構築完了前に終了させ、入れ子関数へ構築特権を渡さない。

body結果はUnitで、全ての正常終了で基底と全独自fieldがinitialization済みでなければならない。deferで不足を補えない。不完全なselfを返せず、構築途中のinterruptionは後始末を行わないAbortだけ。回復可能な失敗や非同期構築はfactoryを使う。

明示initがなく、全fieldにinitializerがあり、必要な基底の引数なし構築が一意なら暗黙initを作る。基底構築が欠落・曖昧・未proofでもtype自体を無効にせず、暗黙initを作らない。initializerの正誤で合成の有無を変えない。

### 6.4 仮想member

virtual/overrideはsafeでbodyを持ち、objref/objuniq receiverを取る関数だけ。独自generic引数は不可。原declarationはopen struct内の非private関数。property、init、dropなどには使えない。

overrideは原slotのcontract全体を引き継ぐ。receiver以外のSelfも原typeの意味を保持し、引数・結果・acquisitionモード・task・外部名を一致させる。アクセス・default value・`!`・constraint・effect・属性を再declarationしない。

最初のアクセス可能な同名の基底グループで、正規化された入力構造から仮想slotを一意に選ぶ。結果、ラベル、default value、declaration順で選ばない。overrideは新たな探索層やoverloadではなく、原関数のFunction Itemを保つ。呼び出しでは静的にslotを選び、動的typeからその最派生実装だけを選ぶ。条件は呼び出し時のproof、override内の仮定となり、実装切替用の追加条件にはできない。

`base.f(args)`は字句的な直接基底の実装を直接呼ぶ。そのbody内の仮想呼び出しは通常どおり。裸のbase、基底field選択、基底valueのacquisitionはない。全実装にObjectCallCompatibleのproofが必要で、基底viewにはOwned消去proofを保持する。destruction中の動的dispatchは不可。公開するeffectはsealedな呼び出しでもslotの上限だけを使う。preserves resultsは同じオブジェクト・同じslotに限り、handle Move・reborrow・view変更では保持し、payload置換・Moveで失う。

### 6.5 enum

enumは一つ以上のケースと任意の位置引数payloadを持つ。`Quit`と`Wrapped(())`は異なる。ケースのアクセスはenumと同じ。独自field、computed、init、drop、入れ子container、継承はない。非選択ケースも含む全依存をtype contractに保持し、等価・順序を自動生成しない。判別value、タグの大きさ、ABIはunspecified。

ケースは`Type.Case`または`.Case`と書き、payloadなしには括弧を付けない。ケースをfunction valueにできない。期待する同じowner enum typeがあればそのtype argumentを先に使い、残りをpayloadからinferenceする。外側のtype argumentはinferenceしない。修飾子にSemantics・enum自身のOrigin・binding集合は書けず、Originは期待typeとpayloadから決まる。オブジェクトは明示作成し、match時はfollowする。オブジェクト期待typeから`.Case`を作らない。

### 6.6 binding

letは一度のinitialization、varは再代入を許す。局所typeはdeclaration時に固定し、後続利用でinferenceし直さない。type中のOriginを省略する場合、initialization expressionが必要。initialization expressionで自分と同じ名前を使うと外側のbindingを参照する。

### 6.7 属性

属性は同じ字下げの次のdeclarationに付き、対象なしはエラー。definition済みは`#Layout`、`#LibraryImport`、`#Test`だけ。

C layoutは一つの断片の記述順を使い、open、派生、空struct、0バイトfieldには使えない。対象側が未対応ならUnsupported。Testはsafe、名前付き、bodyあり、引数・receiver・task・generic・Originなし、結果Unitの関数。ソース直下または許可されたcontainerに置く。非テストモードでは規定の構文検査だけを行い、ユーザーコードからの呼び出し・function value acquisitionはできない。

<a id="s7"></a>
## 7. 関数と呼び出し可能value

### 7.1 declarationと結果

名前付き関数は`func name(...) -> R`。結果type省略はUnitであり、bodyから公開シグネチャをinferenceしない。単一body・字下げbodyの結果規則は§14に従う。

`place ref/T`と`place uniq/T`は、既存の完全な記憶領域を公開する**結果カテゴリ**であり、value typeではない。共有PlaceはRead、排他PlaceはRead/Write・完全value置換を許すが、どちらもTakeを許さない。returnはPlaceを指定し、そのvalueをacquisitionしない。局所やvalue引数自身のslotは返せない。if/match/doはPlaceを生成しないため、各枝でreturnする。結果モード、Origin、Loanは間接呼び出しでも保持する。結果を単に捨てる呼び出しはvalueを読まないが、`_ = call()`はacquisitionする。

### 7.2 引数とdefault value

引数はinitialization済みの不変binding。`@move`で一度消費できるが、slotの再代入・再initialization・新たなexclusive borrowはできない。格納するuniqの参照先は変更できる。

引数一覧の`!`は最大一つで、それ以後の通常引数を名前必須にする。receiverは境界の個数に含めず、一覧中の任意位置に置ける。`external => internal: T`で呼び出し名とbody名を分ける。task slotは最初。

順序付き外部名と境界Kが名前contractとなるが、シグネチャidentity・候補順位とは別。default value付き引数は省略でき、必須引数より前にも置ける。function value経由では全引数を位置指定し、default valueを使わない。

明示引数を記述順に評価し、次に省略引数のdefault valueを引数declaration順に評価する。default valueはdeclaration側のscopeで解決し、先行引数のCopyまたは一時的な共有観測だけを許す。Move・変更・新たなborrowの保存は禁止するが、既存の共有参照のCopyは可能。default valueはtype inferenceの証拠にならない。関数入口以後の引数は逆順に後始末する。default valueはdeclaration時に全許容bindingで引数typeへのfitを検証する。転送先はdefault value内の構文だけに限り、観測Loanは関数入口までに終える。

### 7.3 receiver

struct/enum/contract直下の関数で、内部名selfの引数がreceiverとなる。具体的でraw以外のSemanticsとSelf一層だけを使い、`self`の略記は`self: ref/Self`。group・ソース直下・局所関数のselfは通常引数にすぎない。

同名関数群はreceiver形状とtask形状を一つに統一する。overloadや条件付きdeclarationでも別形状を混ぜない。receiverのacquisition方法を候補選択前に決め、通常引数より前にacquisitionする。

| receiver contract | acquisition |
| --- | --- |
| ref/Self | 選択Placeをshared borrow、または既存borrowをfit |
| uniq/Self | 排他的に書ける選択Placeをborrow、またはreborrow |
| objref/Self・objuniq/Self | オブジェクトを対応する権限でborrow |
| ownerなどのvalue receiver | Copy、または明示Move。読み取りtypeだけはvalue読み取りを使える |

borrowされた非読み取りvalueを所有receiverへ渡すには明示followと通常のacquisition規則が必要。アクセス・基底projection・object fit・Loanの失敗で別層へ戻らない。`Type.method`は未binding参照で、selfも通常引数として渡すため、所有valueからuniq引数には`@uniq`が必要。`value.method`だけでbinding済みfunction valueは作れない。

### 7.4 引数acquisitionの形状

外側Semanticsのacquisitionモードは、owner/obj/rc/arc/raw＝ByValue、ref/objref＝Shared、uniq/objuniq＝Exclusive。genericでは全許容Semanticsケースを検査する。

同名候補の対応引数は、binding・未bindingの各呼び出し列で位置または外部名から定める。default valueや条件で対応を免れない。typeの重なりキーはValue(U)、Object(U)、Anyで、固定した構造の差だけが非重複をproofする。Originやconstraintを使って重なりを消さない。

重なる全ケースで同じacquisitionモードが必要。同じ構築slot、または同じ番号・種類の関数slotの相関だけを利用できる。相関に依存する呼び出しは、候補を絞る前に、同じ入力源・同じ構造経路・同じinference段階から相関をproofする。追加のinference証拠、default value、候補除外で後付けできない。proofできなければtype argumentを明示する。

### 7.5 constraint領域

body先頭の領域にはconstraintとOrigin関係を置け、原virtual関数だけはeffect上限も置ける。通常の実行expressionで領域が終わる。先頭でoriginという関数を呼ぶならexpression全体を括弧で囲む。指示の全分岐も配置検査に参加する。accessorではOrigin関係だけ、dropではどちらも不可。

constraintの対象は自分・外側のgeneric引数とそのprojection。条件付きmemberは探索を固定した後にpremiseを検査し、呼び出せること自体から新しいconformanceを生まない。

### 7.6 unsafe関数

unsafe関数は、typeだけで表せない呼び出し側の安全obligationを公開する。呼び出しにはunsafe領域が必要だが、declaration自身はbodyをunsafeにしない。通常選択の後に安全obligationを確認する。直接呼び出しだけで、function valueにはできない。

### 7.7 匿名関数・capture・type erasure

匿名関数は`func`、任意のcapture一覧、引数、bodyから成る。名前付き引数、default value、`!`、独自genericsはない。引数type省略には確定した期待シグネチャが必要で、bodyの結果を外側のinference証拠にしない。生成時はcaptureだけをacquisitionする。bodyは独立した関数境界であり、外側unsafeを継承しない。

一覧省略時は使用する外側ルートのCopyだけをinferenceし、部分captureやreborrowをinferenceしない。`[]`はcaptureなし。明示一覧があればそれが全てで、bare uniqはreborrow、`@ref`/`@uniq`は外側slotを借りる。self/valueは明示必須、storageと構築中・destruction中selfはcapture不可。

明示一覧は記述順、inference一覧は最初の出現順にacquisitionし、未使用でもacquisitionする。capture先のlet/varは元bindingと独立。あるcaptureが所有する環境内のvalueを別captureから借りる構築はできない。入れ子の関数は各環境を経由する。

呼び出しreceiverは選択された全body操作からShared・Exclusive・Consumingを決める。Moveの可能性があればConsuming。Semanticsケースごとに検査し、CopyなConsuming closureはCopyして呼べるが、Non-Copyなら`@move()`が必要。

common Function Typeへのtype erasureは、固定した期待typeがあり、シグネチャ・taskが一致し、Shared、Owned環境、環境依存のborrow結果なし、可変static結果アンカーなし、通常acquisitionが合法、という全条件が必要。borrow closureや集約内部を再帰的に消去せず、共通type探索でもtype erasureを試さない。環境を再評価しないNon-Copy所有conversionで、allocationはimplementation-defined、失敗はAbort。既に実行したMoveを巻き戻さない。

<a id="s8"></a>
## 8. generics・constraint・Contract

### 8.1 generic引数

`<T>`は完全type一つ、`<s/T>`も完全type一つを取り、正規化後に外側Semantics sと直接対象Tへ分ける。元のs/Tはtype全体と外側Originを保持するが、任意のs/UへそのOriginを移さない。独立したSemantics引数はなく、名前の重複も不可。

ペア対象Tをvalue typeとして単独使用するには、全許容ケースで完全value typeとなる証拠が必要。オブジェクトの場合を含むならObjectPayloadのproofが必要となる。Object Target形成に必要な証拠をシグネチャの見た目から逆算して作らない。ペア自身の許容集合が全てobjectなら、その対象へのペア証拠を使える。

ペアに対するduringはborrowケースのOrigin欄だけをbindingする。元Originとの等価やborrow操作は自動生成しない。s/Uの省略にも通常の位置別規則を適用する。全ての完全typeをtype argumentにでき、Neverも含むが、Never expressionだけからslotはinferenceできない。contract/group/Semantics/valueはtype argumentではない。明示引数列は全slotが必要で、部分指定、`_`、既定type argument、可変長、一般Const引数はない。

### 8.2 constraintとtype identity

constraint節は全て成立しなければならない。type declarationの`Self is C`はconformanceのdeclarationであり、一般のtype parameter constraintと区別する。requirement expressionはコンパイル時の命題で、実行時boolや#if条件には使えない。`T is not A or B`は`T is not (A or B)`を意味する。

type identity requirementはconversionではない。premiseから最小の合同同値関係を作り、type内部の置換やassociated type projectionに用いる。異なる完全確定typeを同じ同値類に置くこと、自身の真部分と同一にすることはエラー。一方、`Option<U> == Option<i32>`からU==i32を取り出すような分解inferenceはしない。

### 8.3 proof

| 結果 | 意味 |
| --- | --- |
| Proven | 規定の証拠で成立 |
| Refuted | 規定の証拠で不成立 |
| Unknown | どちらもproofできない |
| Error | premise・definitionが不正 |

Unknownは否定ではない。一般のSAT、typeの全列挙、定理proof、最適化の事実は使わない。命題の正規化は括弧と二重否定など規定されたものだけで、effect上限は命題ではない。

利用できるのは同じpremise、and分解、orの片側proof/両側反証、not反転、具体type判定、Semantics許容集合、検証済みconformance・Contract継承、type identity、PrimitiveInteger⇒Positionなど明記された含意。Errorは伝播し、同じ命題の正負をともに仮定すればError。`P or Q`と`not P`からQを導く一般inferenceはない。閉世界でRefutedとできるのは、全経路とpremiseが確定して排除された場合だけ。

自己依存のproofを証拠にせず、余帰納を使わない。proof上限超はResourceで、UnknownやRefutedではない。最終判定は自己依存しない全事実を利用し、検査順に依存しない。definition時のproof後、実体化で探索・選択をやり直さない。

ペアの許容集合は9種類のSemantics全体から、and＝積集合、or＝和集合、not＝補集合で計算し、節同士を交差する。空集合はError。要求集合に包含ならProven、交わらなければRefuted、残りはUnknown。

### 8.4 Contract

contractは静的能力を表し、関数requirement、associated type、constraintを持つ。独自のtype parameterは持てるが、Origin・長さパラメーターは持たない。実装bodyや格納領域はない。

関数requirementにはgenerics、task、value結果・Place結果を使える。結果省略はUnit。同名のreceiver・task形状を統一し、`!`は使えるがdefault valueやアクセス指定は不可。requirement premiseは実装互換性のproofに使い、実装へ勝手に追加しない。

親Contractはbinding済み参照として指定し、順不同で循環禁止。子はrequirementを追加する。同じ祖先・bindingへ複数経路で到達しても一つのrequirementだが、独立した同名requirementは別物。requirement同士が共存も共通実装も不可能とproofされた場合だけdeclarationエラーにし、Unknownだけでは衝突としない。

### 8.5 associated type

associateは完全typeとOriginを保持する。contract内のdeclarationと、type側の`is`による指定を区別する。requirement中の連言からtype identityが得られれば固定し、なければconstraintを残して別指定またはinferenceを待つ。最終的に一意でなければならない。

修飾したContractは直接または祖先Contractで、無修飾の同名associated typeは候補が一つの場合だけ使う。`T.(C).Element`が明示的なassociated type projection、`T.C.Element`は通常の名前パス。`T.Element`が両方として成功すれば曖昧。projectionにはconformanceのproofが必要で、名前を見つけたことからconformanceを生成しない。継承associated typeを制限しても新しいtypeにはならない。

**Origin付きassociated type。** `Element(a, b)`のような引数は位置対応するOrigin原子で、交差は括弧で囲む。個数は厳密一致し、未適用・`_`・省略は不可。パラメーターのscopeは右辺・形成条件・付随節だけ。`for Type`は公開の形成条件で、使用側が置換後の条件をproofする。実装は追加条件を要求できず、実行時表現やLoanも作らない。Origin族のidentityは全領域で成立する必要があり、Loan独立性は別に検査する。

**inference。** Originパラメーターのないassociated typeだけを関数結果からinferenceする。declaration・conformance経路のpremiseを先に固定し、inferenceしたvalueを自分自身のpremiseへ戻さない。実装は名前と入力キーで一度識別する。associated type不足でpendingとなったものだけ、type完成後に互換性確認のため再識別できる。欠落・曖昧は最終的な失敗。

証拠はvalue結果同士だけで、property、Place、body、呼び出し箇所からは取らない。同じ名目的type・同じ要素数のtuple・同長固定配列・具体Semanticsの構造を対応付ける。Function Type内部、未解決projection、ペア適用内部へは入らない。メソッドのgeneric・呼び出しごとのOriginなどconformance scope外の名前は使えない。全証拠の完全typeとOriginが一致する必要があり、AdaptationやOrigin短縮で共通typeを探さない。

### 8.6 conformance

conformanceのidentityは完全type Tと、内外のtype argumentをbindingしたContract C。associated typeのvalueは別conformanceの識別子にならない。同じ直接conformanceの重複は条件が違っても禁止。直接と親経由の同じconformanceは、内容が一致すればよい。外部実装・既定実装はなく、実装対応表はdefinition時に固定する。

継承したconformanceは、requirementのSelfを派生typeへ置換して全て互換な場合だけ有効。実装側Selfはdeclaration元に固定する。borrow receiverの基底projection以外に基底conversionは追加しない。conformance継承の失敗だけで派生type全体は無効にならない。

実装識別のキーは名前、種類、genericの個数・種類・順序、task、通常引数の個数・順序・外部名、receiver、正規化入力構造、結果カテゴリとPlaceモード。結果value type、Origin、constraint、effectは候補選択に使わない。

識別後、requirementが許す全呼び出しで実装constraint・入力・結果・Origin保証・アクセス・safe・effect上限を満たすか検査する。失敗しても別候補へ戻らない。generic bodyはrequirement経由で呼ぶ。type関数は`T.empty()`であり、`C.empty()`ではない。独立した同名requirementは複数候補になり、requirement名で呼び分ける修飾呼び出し構文はない。

### 8.7 組み込みContractと閉じたContract

コンパイラー固有のContractはCopy、Owned、Callable、Sealed、ObjectPayload、PrimitiveInteger、Transferable。ユーザーstruct/enumのSelf is Copyは導出declaration、Self is Ownedは検査付き表明であり、他の組み込みContractへの正のconformanceをdeclarationできない。負のdeclarationは`Self is not ObjectPayload`だけ。通常Contractの数学的法則は一般にコンパイラーがproofしない。

| Contract | 意味 |
| --- | --- |
| Sealed | 外側がownerで、有効・非Never・非openのCore。内部のtypeには要求せず、Copy/Owned/ObjectPayload/排他性も含意しない |
| ObjectPayload | 外側ownerの有効な非Never typeで、自身と基底が明示除外していない。条件なしの除外を一度だけ書け、継承先で復活できない |
| PrimitiveInteger | ownerの通常整数12種だけ。Scalar、Copy、Owned、Position、比較・書式・規定算術を保証。通常の単項マイナスは保証しない |
| Position・PositionRange | 規定された閉じたtype集合だけ。ユーザーconformance不可 |

ObjectPayloadは外側だけを検査する。既知Coreは未確定のtype argumentを含んでも直接判定できるが、type parameter・ペア対象・未解決projectionにはObjectPayloadのpremiseまたはContract継承によるproofが必要。ペア証拠が与えるのはObject Targetであり、valueからのobject生成に必要なObjectPayload proofではない。SealedやOwnedだけでも作れない。PrimitiveIntegerは整数conversion、0..127のliteral、Wrapping<T>を利用可能にするが、整数type nameをorで列挙しても同じproofにはならない。

### 8.8 条件付きconformance

generic struct/enumの`Self is C when P`でdeclarationする。Pは正のrequirementをandまたはカンマで結合したものだけで、or/not/実行時valueは使えない。bodyには関数・computed・associateだけを置き、layoutを変えない。条件はそのblockのmemberだけに適用し、bodyなしでもよい。

definitionのpremise DとPの下で全bindingについて検証し、Pを満たす具体typeが見つからなくても省略しない。使用時はProvenで有効、Refutedで不conformance、Unknownで未proof、Errorで失敗。探索・順位・候補再選択には使わない。子Contractの経路にある条件だけが利用でき、別の親条件付きblockのmemberを無条件に使えない。全経路の内容は一致しなければならない。

### 8.9 衝突とproof経路

同じContractへの直接conformanceは、正規化してoccurs check付き単一化を行い、固定構造の不一致だけを非衝突とする。未解決部分が残れば衝突し得る。互いに排他的なwhenでも免除しない。

同じ祖先とbindingへ合流する経路では、associated type、Origin、実装対応の一致をproofする。Contractの再訪はtype argumentが違っても循環。同じContractへ別枝から異なる引数で到達すること自体は循環ではない。

### 8.10 Callable

`Callable<r, S>`のrはref/uniq/owner、既定ref。必要能力はそれぞれShared、SharedまたはExclusive、全呼び出しモード。F自体にCopy/Ownedは不要で、callというmemberを探さない。隠れたreceiver環境に依存するborrow結果は公開できない。

シグネチャSの直接入力で省略したOriginは呼び出しごとの全称binding。S内にduringを記せず、内部の既存依存は固定する。owner呼び出しのacquisitionはCopyまたは明示Moveで、borrowへ変更しない。呼び出し後は残った環境をdestructionする。複数Callableシグネチャを持つ場合も、receiver・task・引数acquisitionの形状を一致させる。

### 8.11 effect上限

definition済み上限は`effect confined`と`effect preserves results`だけ。requirement、Contractの祖先requirement指定、Callable constraint、原virtual declarationにだけ置ける。通常関数の自由な属性ではなく、overrideで付け足したり外したりしない。

**confined。** 環境effectと分類不能effectを持たない。入力・新しい局所領域、Raw.allocate、rawアクセス、ポインター→usize、raw type conversionは、それだけでは環境effectではない。Transferableとproofされた不変staticはinitializerのeffectも含めて許す。それ以外のstatic、Console、外部関数、整数→ポインターは環境effect。抽象Non-Copy typeのdestruction、未知・type erasure済みのdestructionなど分類不能なeffectは、どちらの上限も満たさない。allocationやAbortはconfinedでも許される。

**preserves results。** borrow receiverが必要で、結果がその呼び出しreceiverのLoanやslotに依存しないことを全bindingでproofする。同じvalue・同じrequirementの過去の結果が保持するLoanに、今回の読み書き・新Loan・static・capture・後始末が競合しないことを保証する。純粋性ではなく、receiverのdestruction・置換、別requirement、後から加えた依存までは保証しない。

effect検査は完成した推移的要約に対する独立したobligation。effectで候補を選び直さない。要約が不明な呼び出しは入力から届く範囲と未知の環境effectを持ち、preservesがあれば過去結果の規定除外だけを使う。潜在的Loan競合はエラー。

同じself内field経路の同じ委譲valueから返した依存にはpreservesの保証を伝えられる。経路の置換・swap・Moveで失い、別の委譲value同士は互いの結果を保証しない。

Callableのeffect一覧は一つのCallable requirement全体へ字下げして付け、各上限は一回。呼び出し前のacquisitionとowner呼び出し後の残部destructionも含む。選択・命題proofには上限を使わず、選択後に実要約を確認する。common Function Typeから上限の証拠は得られない。仮想・requirement Function Itemはconfinedだけを利用できる。Callable引数に可変static由来の結果アンカーを隠せない。

### 8.12 実行時Contract

この版ではruntime Contract Viewを指定する構文が未definitionであり、形成できない。以下は拡張時のcontractで、単なる実装未対応とは区別する。

ObjectViewCompatibleなContractは、safeかつ非消費のオブジェクトreceiver、固定されたassociated type、メソッド独自genericsなし、type関数なし、receiver以外のSelf使用なしを要求する。payloadのtype erasureにはObjectPayloadとOwnedが必要。実装conformanceは明示または継承したconformanceに加え、ObjectCallCompatibleを要求する。

### 8.13 明示的な完全specialization

`specialize func`は同じcontainerにある一意の通常generic関数へ、全独自type argumentを閉じて指定する。部分・条件付き・外側環境のspecialization、constructor、drop、accessor、演算子のspecializationは不可。

原contractを全て引き継ぎ、外部引数名も一致させる。アクセス、default value、`!`、constraint、属性、安全性、effect、Origin binderを再記述せず、Origin条件も追加しない。キーは原関数と正規化された静的type argumentからOriginだけを消したもの。同じキーは重複できない。

specialization自体はoverload候補ではなく、公開contractで通常選択した後、一致すれば必ず使う。明示呼び出しで迂回できず、そのspecializationから同じ呼び出しをすると再帰になる。読み込み順で結果は変わらない。

### 8.14 genericのアクセスeffect

アクセスeffectはacquisition操作、Loan操作、Placeと依存をまとめたもの。Consumeはアクセス検査名であり、新しいacquisition種別ではない。bare acquisitionは各ケースでCopy proofまたは外側排他的参照のreborrowが必要。それ以外はdefinitionエラー。未知のCopyをNon-Copyと決め付けず、暗黙Moveや指示分岐による補完もしない。

### 8.15 bodyと具体layoutの検査

generic bodyは全ての整形式の許容bindingについてdefinition時に検証する。bodyから隠れたpremiseを作らず、specializationで一般bodyを救済しない。ペアごとのSemanticsケースはownership解析を独立に行い、Copyケースへreborrow Loanを混ぜず、ケース事実を一般のconstraint proofに追加しない。実装上限超はResource。

具体layoutは格納・構築・value渡し・value返却・サイズacquisitionが必要な時点で検査する。borrow/raw/handleの背後にtype nameを書くことだけでは不要。検査対象の非genericコードから必要になる全実体化を、到達不能箇所も含めて確認する。未使用genericは象徴的検査だけ。言語上の不正layoutはLanguage、実装資源上限はResource、生成未対応はUnsupported。多相再帰自体は合法であり、生成上限超を言語エラーにはしない。

<a id="s9"></a>
## 9. 名前・シグネチャ・アクセス

名前探索、type/候補選択、利用の合法性検査を順に行う。後段で失敗しても前段へ戻らず、ファイル・別名・候補の列挙順に依存しない。

### 9.1 declarationのidentity

同名のtype declarationで種類が違えば、引数数に関係なく衝突する。同じ種類で引数数が違うtypeは別declaration。Origin schemaはdeclarationを区別しない。

関数シグネチャは名前、独自generic引数数、正規化した順序付き引数typeに基づく。結果type・結果モード、引数名、default value、`!`、アクセス、unsafe、constraint、taskの有無で別overloadにはならない。receiverも引数列に含める。正規化はdeclaration記号、別名、完成したassociated type projection、括弧、冗長owner、binderの名前替えを考慮し、個別のproofや実体化で追加簡約しない。

generic引数数にはlengthも含み、ペアは一つと数える。slot種類自体は区別せず、`f<T>(x:T)`と`f<s/U>(x:s/U)`は重複する。initはdeclaration structと順序付き正規化引数type、field/computed/property requirementは名前、enumケースはそのenum内の名前で区別し、ケースのpayloadではoverloadしない。

### 9.2 名前空間と役割

type、value、Origin、ラベルは別の名前空間を持つ。構文位置から役割を決めて探索し、typeや呼び出し可能性から都合よく絞らない。見つかった名前が後のtype検査で使えなくても、外側の同名を探し直さない。

### 9.3 局所scope

局所bindingはdeclaration後から見え、initialization expression内の同名は外側を指す。同じblock内の重複は不可で、引数もbodyと同じscopeに属する。局所関数はblock入口から見えるが、valueをcaptureしない。

Selfは予約語。文脈的なself/value/storageは有効な領域で再declaration・暗黙captureできない。入れ子の名前付き関数内では外側の文脈bindingは有効でない。

### 9.4 アクセス領域

| 指定 | 許される領域 |
| --- | --- |
| private（既定） | Declaration Containerのbodyとその内側 |
| internal | 同じKotonoha |
| protected | 自身のbodyまたは派生typeのbody |
| protected internal | internalとprotectedの和集合 |
| private protected | internalとprotectedの積集合 |
| public | 全て |

外側containerと具体的type argumentのアクセス領域も交差する。ルートのprivateはKotonoha内。親は子containerのprivateへの特権を持たず、friendやパッケージ全体の特権はない。protectedを含む指定はstructのmember（入れ子container・accessorを含む）だけに許す。

領域包含はbodyの入れ子、Kotonoha identity、declarationされた派生関係など、規定された関係だけでproofする。sealedの派生領域は空。今回のビルドに存在する派生typeだけを数えて公開性を決めない。

派生type Dからprotectedインスタンスmemberを使う場合、receiverの静的Effective CoreはDまたはその子でなければならず、基底type Bや兄弟typeは不可。declaration元Bのbody、同じKotonohaのprotected internal、type関数・constructor・入れ子containerには規定の例外がある。このreceiver制限が適用される場合は`receiver.f(...)`というメソッド呼び出しだけを許し、type修飾呼び出し・Function Item acquisitionでは迂回できない。

公開シグネチャが露出するtype・requirementは、declarationのアクセス領域を全て覆う必要がある。inference type、別名、入れ子、projectionの内部も再帰的に確認する。setterの狭いアクセスでproperty自身の公開領域は狭まらない。公開genericに非公開typeを引数として使うこと自体は許され、その利用typeの領域が狭まる。

genericの探索・アクセスはdefinition位置で固定し、呼び出し側の別名は影響しない。conformanceの公開領域`Access(T) ∩ Access(C)`は各実装の領域内でなければならない。アクセス失敗でconformance領域を勝手に縮めたり実装を選び直したりしない。

### 9.5 無修飾探索

次の順に調べ、役割とアクセスに合う最初の段階で止まる。

1. 局所・字句scope。
2. 現在のcontainer。
3. 親containerからルート。
4. type修飾子の位置だけ、Compilationの直接参照名。
5. 明示的な別名全体。
6. 既定の別名全体。

enumケース、receiver必須member、継承memberなど修飾が必要なものに達したら、修飾要求エラーで止まり、外側へ進まない。名前付きaliasは修飾子でありCoreではない。同名aliasの重複は同じbinding済み参照の場合だけ。複数のopening aliasが作る関数群のacquisition形状・task形状は使用時に検査する。

### 9.6 修飾探索

修飾パスは選んだ対象内だけを探す。type側とvalue側の両解釈がある場合は各パスを最後まで解決し、成功が一つの場合だけ採る。引数や期待typeで解釈を選ばない。`::`はCompilation rootを指す。継承探索は最初のアクセス可能・役割に合う層で止まり、overrideは新層を作らない。

基底receiver projectionは対象を一回評価し、元receiverのアクセスを確認して基底部分だけを借りる。標準fieldはその記憶場所へprojectionする。ObjectCallCompatibleは候補選択後にproofする。基底全体のMove・置換・sliceした所有value生成はない。オブジェクトではOwned消去proofとidentityを保持する。

### 9.7 type declarationの選択

明示引数数・種類でtype declarationを一意に決め、constraintは後から検査する。`.init`やケースで自分の引数を省略した場合、0引数typeがあればそれを優先し、なければ一意のgeneric typeを使う。実引数や期待結果から別のtype declarationを選ばない。

外側パラメーターの省略は字句環境からだけ補い、入力・結果からinferenceしない。継承した入れ子typeもdeclaration元のidentityを保つ。入れ子typeのOrigin schemaは独自欄と字句的に継承する欄であり、type argument・field・基底の内部欄を平坦に加えない。修飾子のOriginは全て完成させ、暗黙staticを補わない。必要なら`(Outer<T>{outer}).Inner<U>{inner}`と書く。入れ子typeとassociated typeは別の探索で、`T.Element`が両方に成功すれば曖昧となる。

<a id="s10"></a>
## 10. overload解決とinference

### 10.1 適用可能性

候補ごとに、明示type argument→通常引数対応と省略記録→receiver・引数からのinference→期待結果による未確定部分の補完→Adaptation・Copy・constraint・条件→期待結果へのfitを確認する。候補を調べてもexpressionは実行しない。

Unknownな条件を持つ候補は適用可能なものとして比較し、最良候補を決めた後にproof obligationを報告する。Unknownを捨てて別候補を選ばない。effect上限は候補除外・順位に使わない。未確定の匿名関数などを候補ごとに検査して結果を見ることも禁止する。

通常引数は位置指定の後に名前指定を書く。位置指定は`!`を越えず、途中を飛ばさない。名前は外部名で、重複不可。taskは一覧先頭。default valueはinference証拠ではなく、実行時も明示引数の記述順の後、既定引数のdeclaration順に評価する。

### 10.2 共通Adaptation

明示acquisitionを先に行い、その後の暗黙Adaptationは最大一つ。表にないconversion鎖を探索しない。

| 元と期待type | 許すAdaptation |
| --- | --- |
| 非参照の所有Place・temporary value → ref/T | shared borrow |
| 安全なvalue参照層 → ref/T | fitする共有参照をCopy、またはshared reborrow |
| 安全なvalue参照層 → 読み取りtype | 終端のvalueをCopy |
| uniq/T → uniq/T | exclusive reborrow。明示Move後に直接fitする場合はそのまま転送 |
| オブジェクト → objref/T | 共有オブジェクトborrow |
| objuniq/T → objuniq/T・objref/T | 排他的／shared reborrow |
| 具体的呼び出し可能value → common Function Type | §7.7のtype erasure条件を満たす場合だけ |
| その他 | 通常のbare acquisitionとtype fit |

参照・handleのslot自体を暗黙に借りない。slotには`@ref`を明示する。安全な多層参照から共有参照を得るときは、最内の共有参照のCopyまたはuniqのshared reborrowを使い、必要なOriginの交差と経路権限を保持する。

暗黙のオブジェクトpayload follow、clone、排他的temporary value borrow、upcast、数値conversion、ユーザーconversionはない。所有valueから新しいexclusive borrowを作るには、receiverの場合を除き`@uniq`が必要。lifetimeは延長しない。

literalは各候補へfitを調べるが、既定typeを与えるのは候補選択後だけで、曖昧さの解消には使わない。null・空コレクション・type未記載の匿名関数に代替typeはない。inferenceは完全typeの証拠を同時に集め、読み取りtype constraintのときだけ参照終端を見る。`ref/T`は外側参照の対象を使い、ペアをborrow typeと決め付けない。

引数Adaptationの順位はExact、Literal、同Semantics reborrow、異Semantics Adaptationの順。type erasureはこの順位と比較不能。同じ引数に対する二つのtype erasureは同等とする。

### 10.3 期待結果

期待typeは未確定slotを補い、fitしない候補を除くために使うが、優先順位を付けない。Origin関係は選択後に検査する。捨てる文脈にUnitの期待typeを与えず、Adaptationを逆算して入力typeを作らない。

### 10.4 最良候補

明示された各入力のAdaptationを、どれも悪化せず少なくとも一つ改善するかで比較する。receiverと既定引数はこの比較に含めない。Adaptationが同順位で、置換後の引数構造が全て同じ場合だけ、自分のgenericsがない候補、次にdefault value使用の少ない候補を優先する。それでも同順位なら曖昧。比較不能を後段の規則で救済しない。整数幅、constraintの強さ、一般性、declaration順では順位付けしない。

### 10.5 inferenceの境界

局所typeはdeclaration時に固定。名前付き関数の結果をbodyからinferenceしない。tryのオペランドとexplicit discardには期待typeを渡さず、specializationは通常選択後に決める。

expressionのinference状態は、Literal、独立にtypeが決まるexpression、Waitingに分ける。独立typeは外側期待typeや既定typeなしに決まり、内側呼び出しなら適用候補が一つである必要がある。匿名関数は常にWaiting。内側の呼び出しを外側候補ごとに解き直さない。

確定シグネチャSはcommon Function Type、またはF/ref F/uniq Fに一意のCallableシグネチャがある場合に得られる。複数候補からは共通するSだけを使い、関数ヘッダーの既知部分だけをinference証拠とする。

関数参照に複数候補や未bindingの独自引数があればSが必要。Sの全入力typeが閉じてからAdaptationなしで一度選択し、選んだ関数の結果からSの未確定結果を埋めない。unsafe、destruction、type erasureは選択後に確認する。

匿名関数のヘッダーは候補除外・証拠に使えるが、bodyは明記type・共通期待type・選択済み期待typeの下で一回だけ検査する。bodyから外側slotや候補を選ばない。先にUnitと確定したbodyを、後からvalue bodyとして読み替えない。除外されたソースは候補を供給せず、条件付きdeclarationは探索した集合に残る。

### 10.6 選択後の合法性

unsafe許可、initialization、Move、Loan、lifetime、利用アクセス、書き込み権限は選択後に検査する。失敗しても再選択しない。添字は一意のContract conformanceで決め、通常overloadの順位や期待結果フィルターを使わない。

### 10.7 呼び出し可能シグネチャの互換性

taskの有無と引数数が一致し、各要求入力Piが実装入力Aiの部分typeであることが必要。value結果は実装結果Rが要求結果Qの部分type、またはRがNever。Place結果は同じ結果モードを必要とし、公開するtype・Origin・Loan contractを保つ。acquisition操作・conversionを挿入しない。要求側の全称Originは固定し、実装側で実体化可能なものだけを解く。

common Function Typeのvalue同士は通常の部分type関係だけでconversionできる。`(i32)->Never`から`(i32)->i32`へのconversionを、この結果互換性から許してはならない。

### 10.8 type argumentの同時inference

自分のslotを同時にinferenceし、既知シグネチャの未知部分だけを照合する。不変位置のbindingは一致が必要。それがなければ共変位置の証拠を交差する。Originのfitは選択後。associated type`T.Element`の逆算、conversion探索、共通基底の探索、証拠順の優先はしない。異なるSemanticsの証拠は衝突する。

未binding slotがあるというだけで候補を除かず、他の検査を進める。未binding typeを他typeと同じ・部分typeとは扱わず、循環した証拠も使わない。ただしWaiting引数がbindingするF/ref F/uniq Fは例外で、最良候補の構造比較では同じ形式を同一と比べる。

constructorではそのtype自身のslotだけをinferenceする。期待typeは同じowner declarationとの直接照合だけに使い、body・default value・field initializerを証拠にしない。inference typeを明示固定して元の呼び出しを再比較しても、同じconstructorが選ばれる必要がある。違えばtype argumentの明示を要求し、別constructorへ切り替えない。

### 10.9 拡張しない範囲

constructor独自のgenerics、structの長さパラメーター、追加の暗黙conversion・演算子・overload直接選択構文は、この版に含まれない。

<a id="s11"></a>
## 11. property

### 11.1 種類

| 種類 | 格納・accessor |
| --- | --- |
| let | 格納field。get省略は標準get、set禁止 |
| var | 格納field。get/set省略はそれぞれ標準accessor |
| computed | 格納領域なし。body付きgetter必須 |
| property requirement | Contract内。getter必須、setter任意 |

格納propertyのcustom getは格納typeそのものを返し、Copyのproofが必要。格納custom setterの入力は格納type Tと完全に同一、結果はUnit。computedのsetter入力はgetter typeと異なってよく、setter自体は省略できる。computedの各accessorにはbodyが必要。enumや局所にcomputedは置けない。全fieldアクセスは公開するaccessor contractに従う。

### 11.2 標準アクセスとMove

標準accessorはPlace操作であり、関数呼び出しや隠れたCopy constraintを加えない。

| 利用 | 対象field | 経路上の各祖先格納property |
| --- | --- | --- |
| Copy・shared borrow | 標準get | 標準get |
| Move | letは標準get、varは標準get/set | その祖先自身のMove行 |
| exclusive borrow | 標準get/set | 標準get/set |
| 書き込み | 標準set | 標準get/set |

全て、必要な位置からアクセス可能でなければならない。

varからMoveするときはアクセス可能な標準setが必要だが、setterを呼ぶわけではなく、slotの変更可能性を追加要求するわけでもない。letは書き込み・排他的slot borrow・再initializationが不可。ただし格納するuniq参照先の能力は失わない。

custom getは隠れたfieldではなく結果valueを返す。custom setは最終代入先でだけ使え、その背後の子を更新・Move・exclusive borrowできない。標準field Moveは所有structの静的Move経路、完全性、祖先dropの復旧条件に従う。borrow、object、static、drop receiverからはMoveできない。標準アクセスは不完全な親の完全な兄弟部分を使えるが、custom/requirement accessorにはreceiver全体の完全性が必要。

Copy不明の裸読み取りはエラーとなる。標準getはCopy constraintを勝手に足さず、明示borrow・Moveまたはdeclaration済みconstraintで利用する。

### 11.3 明示accessor

custom/computed accessorはtyped引数・結果を持ち、default value・`!`は不可。getter receiverはref/Self、setterはvalueより前のuniq/Selfで固定。staticにはreceiverなし。他モードは不可。getterは排他予約を作らない。setterはreceiverをCopy/Moveせずborrowする。object・基底経由ではObjectCallCompatibleも必要。

storageは格納fieldへの直接アクセスで、accessorを再帰呼び出ししない。setterのvalueは不変引数。storage代入も通常の旧value destruction・置換であり、排他的selfから旧valueをMoveできない。戻る際は完全でなければならず、未使用入力もdestructionする。

各accessorのアクセス指定はpropertyより厳密に狭い場合だけ認める。protectedとinternalは一般に比較不能。setter領域がgetter領域の部分集合である必要はない。

### 11.4 computed

格納領域・initializer・bodyなしdefinition・has略記はない。getterのtypeはヘッダーと一致させ、setter入力は別typeでもよい。共有receiverからNon-Copy valueを取り出したり、格納uniqをexclusive reborrowしたりできない。getしたvalueをsetすれば元に戻る等の法則は保証しない。

### 11.5 getter結果

getterが返した所有temporary valueとそのインライン部分は、直接代入、複合更新、増減、exclusive borrow、排他メソッド呼び出しの対象にできない。括弧・projection・match/forの内部対象でもこの制限は残る。返した参照の参照先の能力は保つ。局所や引数へacquisitionした後は、acquisition先の通常規則を使う。

### 11.6 格納typeとOrigin

typeは注釈またはinitialization expressionだけで決める。Copy・Owned・layoutのproofを介する循環も含め、inference循環にはtype annotationが必要。格納Originはdeclarationで全て完成させる。格納custom accessorの省略Originはstorageのcontractから引き継ぎ、selfから新たにinferenceしない。

### 11.7 初回配置と構築履歴

初回配置は全入力経路で未initializationかつそのfieldの初回配置が過去に完了していない場合だけ。Move/dropで履歴は戻らない。constructorの独自field初回配置はaccessorを呼ばない。custom-set varへ構築中に再書き込みすることはできず、letは一回だけ。自動destructionもaccessorを呼ばない。

### 11.8 static

静的格納valueにはinitializerとOwned typeが必要で、最初の実アクセス時にinitializationする。不変staticへのshared borrowはstatic Originを持てる。可変staticの新しいborrowは有限で、結果アンカーの逃避制限に従う。格納済み参照のCopyはstatic slotを借りることではない。

### 11.9 Contractのproperty requirement

インスタンスrequirementでgetter必須、setter任意。body・格納・default value・属性・アクセス・`!`はない。`has get, set`は固定シグネチャの略記。明示setterなら別入力typeを使える。

通常探索を一度行ってから互換性を検証し、ヘッダーtypeと実装storage typeを機械的に同じとはしない。標準accessorをrequirementへ対応させる形は次の三つだけ。

1. 共有receiverからFを返す：FがCopyとproof済み。
2. 共有receiverからref/Fを返す：標準fieldを借り、結果lifetimeはreceiver以内。
3. 排他的receiverとFからUnitを返す：varの標準set。

必要な各accessorにアクセスできること。custom getから隠れたstorageへ橋渡しせず、新しい探索候補も増やさない。

<a id="s12"></a>
## 12. expression

### 12.1 結果とstatement

通常完了するexpressionはtypedの結果を返し、Unitも結果に含む。結果を捨てても副作用・type・ownership・destructionの検査は残る。字下げbody自体はexpressionではなく、複数操作からvalueを得るには選択expressionやdoを使う。unsafe・defer・require・`_ = E`はstatement、let/varはdeclarationで、expressionや条件bindingにはならない。

### 12.2 評価順

原則として各オペランドを左から一回評価する。メソッドはreceiver→呼び出し先解決→引数→呼び出し、添字は対象→添字→アクセス、辞書はキー→重複検査→value→挿入、範囲は始点→終点。省略境界は評価せず、範囲生成時の境界検査もない。補間はexpression評価と出力を一組ずつ順に行う。

and/orと選択は必要な経路だけを評価し、代入は右辺が先。type argument・長さ引数・Adaptation先は実行時評価しない。転送・発散・Abort後は残りを実行しないが、構文と意味の検査は省略しない。

### 12.3 literalのtype

期待typeはbinding・引数・結果から構成要素や分岐へ伝わる。数値演算は同じ数値typeを使い、bool/char/数値間の暗黙conversionはない。整数literalは表現可能な期待整数type、浮動小数点literalは期待f32/f64にfitする。文脈がなければi32/f64、長さ文脈ではisize。呼び出しの既定化は候補選択後で、整数literalをfloatへ暗黙fitさせない。

直接literalは、数値に直接付いた一つの符号と、その全体を囲む括弧を含む。符号込みで範囲を検査する。`-128`や`((- 128))`はi8にfitするが、`-(128)`は128への単項演算なのでi8にはfitしない。

literalだけのexpressionは、整数の算術・ビット・シフト、floatの`+ - * /`、位置・範囲の各群で扱い、群を混ぜない。葉と演算のtype fitを検査し、計算結果で候補を選ばない。シフト量は左辺と独立したtype。演算は決定したtypeで逐次行うため、i8の127+1は実行時Abort、Wrapping<i8>なら−128となる。conversion先へ直接fitするのは直接literalだけ。定数長さ、静的Move添字、パターンの専用規則は広げない。

### 12.4 基本expression・補間・辞書

括弧はPlace性を保つ。配列要素、辞書のキー・valueはそれぞれ一つのtypeで、空には期待typeが必要。普遍object typeへ退避しない。

補間は所有stringを作る。各valueをUtf8Writer.writeの共有入力へfitさせ、選ばれた参照先typeにUtf8Formatを要求する。参照type自身へconformanceを自動転送せず、bare PlaceをMoveしない。

辞書literalは等価なキーの重複を禁止する。bool・整数・char・非補間string・Unitのliteralキーは、到達不能も含む全項目をキーtypeにfitさせて静的比較する。名前や演算expressionはこの静的検査に含めない。残りはキーを一回評価し、重複ならvalue評価前にAbortする。未完了項目を挿入せず、部分辞書を返さない。

等価比較は`stored.equals(search)`で、Hashは不要。比較回数・順序はunspecifiedだが副作用を保つ。floatのContract equalityではNaN同士、正負ゼロ同士は等価。ユーザーの等価関係は格納・検査・挿入中に安定した同値関係でなければならない。違反してもメモリー安全性は損なわない。

### 12.5 memberと呼び出し

`E.name`は修飾探索で、selfを自動追加しない。value参照層にdeclarationがなければ規定の参照経路をたどる。`.整数`は範囲内のtuple添字。動的な名前探索はない。type修飾子はvalueを作らず、`.init`は構築。raw memberは`(*p).name`と明示する。

引数末尾カンマは可。task呼び出しは`f(task; a)`。function valueは位置引数だけを使う。通常解決後に明示specializationを選ぶ。`T(args)`ではなく`T.init(args)`を使う。

### 12.6 オブジェクトmember

member・候補・アクセス・公開contractはreceiverのEffective Type、仮想実装は同じslotのDynamic Typeで決める。必要な基底projectionにはOwned消去proofを使う。オブジェクトborrowや基底projectionからの呼び出しにはObjectCallCompatible＝Provenが必要。viewが完全なSealed payload typeと一致するなら、followで通常の完全value操作を使える。通常引数ではfollowを明示する。open viewや基底部分を全体置換・不完全化Moveしてはならない。

### 12.7 ObjectCallCompatible

receiverの完全性・権限・type・Origin・Loan contractを守るという公開保証で、操作ごとにProven/NotProvenを一つ持つ。実体化ごとに判定を変えない。標準get/setはPlace操作として扱う。

通常bodyと全明示specializationのeffectを最小固定点で解析し、最適化や実装の解析精度に依存させない。選択後に確認し、失敗時に別候補へ戻らない。不正bodyはエラー、検証自体が未対応ならUnsupportedでありNotProvenと見なさない。

effect対象とreceiverの関係はWhole、Base、Part、Separate、MayAlias。Whole/Baseの置換・再構築・所有acquisition・無制限排他権限の逃避は、完全な対象であるproofを持つ合法操作を除き禁止。receiverを不完全にするMoveは、後で復旧しても違反となる。Partの置換・交換、許可された読み取り・borrow、Separateへのeffectは可能。MayAliasや到達するunsafe操作は未proofとなる。

呼び出し・accessor・default value・後始末のeffectを入力・capture・staticアンカーへ対応付ける。合法な共有receiverはProven。排他入力は完全性を保つ全体置換相当までのeffectを持ち得るため、Whole/Baseへ渡すには必要な仮想slot保証が要る。再帰だけを理由に失敗させず最小固定点を使う。間接・requirement呼び出しは公開上限を使い、要約がなければ排他入力への上限と未知static effectを仮定する。

一般bodyはspecialization範囲を除かず全許容bindingで検査する。未使用を含む全specializationが互換なときだけProven。specializationが非互換でもそれ自体を不正declarationとはせず、操作全体をNotProvenにする。公開contractの検証後に実装を選ぶ。

<a id="s13"></a>
## 13. 演算と代入

### 13.1 優先順位

優先順位と結合は付録Fに従う。構文上の結合は、typeの合法性や評価順を変えない。

### 13.2 単項演算

`+`は数値を保ち、`-`は符号付き整数・Wrapping・float・Negatableに使う。通常の符号なし整数には使えない。notはbool、`*`はraw逆参照、`^`は末尾位置。

前後の`++`/`--`は読み書き可能な整数/WrappingのPlace・propertyを一度位置決定し、一回読んで一回書く。前置も再読み取りしない。前置は更新後、後置は更新前のvalueを返す。演算、Abort、expression typeはget結果typeで決め、set入力へfitさせる。

### 13.3 算術

組み込み`+ - * /`は同一数値type同士で同typeを返す。`%`は整数/Wrapping。整数除算は0方向へ切り捨て、余りの符号は被除数と同じ。通常整数の表現不能はAbort、Wrappingは2ᴺの剰余。ゼロ除算・余り、範囲外シフトは両方でAbort。MIN % −1は0。

`& | ^`は同一整数/Wrappingのビット演算で、boolには使えない。シフト量は任意の通常整数で、Wrappingは不可。左シフトは上位を切り捨て、右は符号付きで符号拡張、それ以外はゼロ拡張。floatはIEEE 754最近接・偶数丸めで、ゼロ除算はAbortしない。結果を変える再結合・融合を禁止する。

stringに算術・連結演算はなく、参照経由やユーザーproviderを介しても`+`/`+=`を使えない。連結は補間。raw演算は§5の範囲だけ。

| 演算 | 左側provider | 右側のLeft provider |
| --- | --- | --- |
| `+` | Addable.added | LeftAddable.addedFrom |
| `-` | Subtractable.subtracted | LeftSubtractable.subtractedFrom |
| `*` | Multipliable.multiplied | LeftMultipliable.multipliedFrom |
| `/` | Dividable.divided | LeftDividable.dividedFrom |
| `%` | RemainderProvider.remainder | LeftRemainderProvider.remainderFrom |
| 単項`-` | Negatable.negated | なし |

算術Contractはsafe・非task・default valueなし、入力をshared borrowし、associated type Outputは完全value type。Leftは`left op self`を表し、自動生成しない。明示conformanceのSelfは外側ownerの非組み込みstruct/enum、LeftのLhsは組み込み数値。Rhsは外側ownerの完全value typeでstring不可。OutputにCopy/Ownedは要求せず、純粋性・代数法則も保証しない。組み込み数値は同typeのContractだけを持ち、floatの余り、通常unsignedのNegatableはない。requirementのFunction Itemはacquisitionできる。

安全なvalue参照を終端まで選んで経路を固定する。ここで組み込み数値typeにはPrimitiveIntegerをproofできるtype parameterとそのWrappingも含む。両方が組み込み数値なら組み込み、左がユーザーtypeなら左通常Contract、左数値・右ユーザーtypeなら右Left、規定raw形ならraw、その他はエラー。明示premiseが経路を固定する場合もある。同じ組合せに通常/Left両premiseを要求するのは矛盾。

固定providerの構造fitが複数なら曖昧。反対側探索、conversion探索、期待結果による選択、権限失敗後の再選択はしない。literalは相手typeへfitし、ユーザーtypeをliteralから構築しない。genericでも経路・requirement・Output・acquisitionをdefinition時に固定する。

右providerでも評価は左→右。組み込みはvalue読み取り、Contractは数値入力もshared borrowし、左Loanは右評価中も生存する。明示Move/Copyは先に行う。演算限りのLoanへ結果は依存できず、Result結果を自動tryしない。

### 13.4 比較と論理演算

数値は同type比較、charはスカラー順、stringはUTF-8バイトの辞書順、bool/Unitは等価だけ。組み込みfloat比較はNaNを含むと`!=`だけtrue。tupleは左から等価/辞書順に比較し、unorderedなら四つの順序比較はfalse。

安全な参照は終端を比較する。Neverを除き終端typeは同一でなければならないが、外側Originの一致は不要。通常の期待typeは与えず、literal/nullだけを反対側へfitさせるため、`.Case`や空集合は使えない。オブジェクトhandleの等価・identity比較はなく、必要ならpayloadをfollowする。Non-Copy valueをMoveせず共有観測し、左Loanは右評価から比較完了まで生きる。and/orは短絡、notはbool。

ユーザーtypeはEquatable.equalsで`==`/`!=`、Comparable.compareの0との比較で順序を定める。Comparableは等価と一致する全順序を約束する。整数/Wrapping/char/stringは両方、bool/Unit/floatはEquatableだけ。value参照とtupleは転送・合成するが、struct/enumは明示conformance、objectは転送しない。floatのContract equalsはNaNにも反射的であり、組み込み`==`とは異なる。genericの実体化でこの選択は変わらない。

### 13.5 明示操作

`@`は直接オペランドだけへ作用し、ユーザーconversionやconversion鎖を探さない。オブジェクト生成以外で隠れたallocation・boxing・計数増加はない。

形式は`@Type`、`@Semantics`、`@move`、`@copy`、`@follow`、`@raw`、`@raw/U`、`@usize`、`@wrap<U>`、`@bits<U>`。Adaptation先の外側Semantics鎖にOriginは書かず、内部payload typeの注釈は保つ。略記に`?`やduringは付けられない。裸の名前は組み込みSemanticsを先に認識し、その後はtype nameとpair binderを独立探索して両方なら曖昧とする。

書いた対象と元のtype・value種別から操作を一度決め、アクセス・Loan等を検査する。Adaptation先は通常規則の範囲でinferenceを導けるが、確定済みtypeを変えない。数値・wrap・bits conversion先が元のtypeを導くのは直接literalだけ。数値conversionと読み取りtypeの同一type acquisitionは参照先を読むが、borrow・follow・copy・move・object操作は参照value自身を扱う。

**acquisition。** @moveはCopy typeでも元をMovedにする。temporary valueでは追加操作なし。@copyにはCopy proofが必要で、参照コピーと参照先コピーは別。同一typeへのAdaptationはCopy Placeの複製またはtemporary value転送で、Non-Copy Placeを暗黙Moveしない。borrow type対象はborrow/reborrowを選ぶ。裸の@ownerはない。Origin短縮はacquisition操作を増やさない。conversion後の暗黙borrow、構造conversion、string/bool/charのconversion、ユーザーconversion、動的castを合成しない。

**数値conversion。** 通常conversionは範囲検査し、失敗はAbort。整数を巻き戻したり低位ビットへ切り詰めたりしない。整数→float、float→floatは最近接・偶数丸めで、有限valueが無限大になると失敗。float→整数は0方向切り捨て、NaN/無限大は失敗。Wrappingの通常conversionは対応する同じ整数Uとの往復だけ。float間は段階的アンダーフローとNaN・無限大・ゼロ符号を保つ（NaN payloadは保証なし）。整数ゼロは+0となる。

`@wrap<U>`は整数/Wrapping間で、縮小時は低位ビットを残し、拡大時は元typeが符号付きなら符号拡張、符号なしならゼロ拡張する。失敗しない。`@bits<U>`は同じ幅のfloat↔整数/Wrappingだけで、NaN payloadとゼロ符号を保つ。どちらかのtypeがtype parameterに依存する間は使えない。

直接literalは既定typeを経ず正確valueから一度conversionし、失敗は静的エラー。整数→bits<float>は対象幅へwrapしてから再解釈、float→bits<int>は同幅floatへ一度丸めてから再解釈する。通常のliteral expression全体へこの特例を広げない。

**follow。** 一層の参照先Placeを選ぶだけで、refはRead、uniqはRead/Write、どちらもTake不可。letはslotの変更を禁じるだけだが、共有層を越えて排他権限は得られない。オブジェクトのfollowには、内部Originまで一致する完全なSealed payload typeのproofが必要。書き込み可能objとobjuniqは排他、読み取り専用obj・rc・arc・objref・共有経路は共有のみ。payload borrowは経由handleにも同じモードのLoanを残す。

ペアのfollowは全許容Semanticsがvalue/valueborrowの場合だけ。ownerケースは自分、ref/uniqケースは参照先を選び、権限は全ケースの最弱とする。Take、実行時分岐、Copy、allocationは追加しない。具体的ownerへのfollowは不可。

**borrowとreborrow。** `P@ref`/`P@uniq`はPのslot自身。明示Vは格納typeと一致しなければならず、r:ref/Tにr@ref/Tは使えない。参照先を借りる表記は`r@follow@ref`/`@uniq`。オブジェクトは所有handleからobjref/objuniq、objrefからCopy、objuniqから共有/排他の子を作る。共有から排他への昇格、rcのexclusive borrow、value borrowとobject borrowの相互conversionはない。getterは一度呼んで結果へ適用し、背後のstorageを借りない。

各expression・receiverを一回評価し、連鎖は内→外。呼び出しやgetterの内部へacquisition要求を逆伝播しない。完了済みMoveや副作用は巻き戻さない。type・アクセス・Loan違反は静的エラー、実行時範囲失敗はAbort、unsafe約束違反はundefined behavior。

### 13.6 オブジェクト操作

**upcast。** 全動的派生typeにSupportsをproofし、必要なOwned消去・Origin・Loan条件を満たす場合だけ明示conversionできる。所有handle Placeは@move、objrefはCopy、objuniqは子のreborrow。動的type・identity・計数・責任を保つ。暗黙upcastやvalue borrowの派生→基底conversionはない。

**生成。** owner/T→obj/rc/arc/TにはObjectPayload proofが必要。同typeならacquisition、派生→基底ならupcastで、生成とupcastを一操作にまとめない。元typeはconversion先や外側期待typeから独立に決める。obj→rc/arc、rc↔arc、borrow→ownerは不可。makeObj/makeRc/makeArcも同じ生成contractに従う。

strong cloneは`clone(handle@ref)`で計数を一つ増やす。allocationも元slotへの依存も追加しないが、payload内の外部依存は保持する。ライフサイクルはBuilding→Alive→Destroying→Freed。最後のstrongで完全な動的typeをdestructionする。計数オーバーフローは巻き戻さずAbort。

arcはinitializationから利用、各releaseから最後のreleaseとdestruction、成功upgradeから最後のreleaseへの規定の順序を保証するが、payloadの任意変更を同期しない。

循環生成にはOwnedも必要。未公開領域とWeakを作り、builderを一回呼ぶ。Building中のupgradeはNone。builderの後始末後にpayloadを設置してAlive・strong=1とする。allocation失敗・計数オーバーフローは公開前Abort。builderのAbortや非終了でも公開しない。

**Weak。** downgradeはstrongを保ち弱参照を作る（allocation可）。upgradeはAliveならSomeのstrongを一つ増やし、Weak cloneとともにallocationなし。Weakはpayloadを生存させず、destruction時もpayloadを触らない。Building中の失敗は後で成功し得るが、最終release後の失敗は永久。元のWeak/strong slot Loanは引き継がず、payloadのtype依存は保持する。

**実行時is。** オブジェクトhandleの動的typeを一回検査し、acquisition・Move・Copy・計数変更はしない。左辺のView Targetはstruct Coreでなければならない。対象自体はSemanticsを付けない名前付きstruct Core一つ。type argumentはSemanticsを含む完全typeを使えるが、Origin・during・binding集合はtype argument内や別名展開後も書けない。type parameter/associated typeそのものは対象にできないがtype argumentには使える。結果が既知でも元expressionは評価する。`x is Dog or Cat`は`(x is Dog) or Cat`。

**検査付きcast。** 意味は規定するが、この版にソース構文はない。Supports成功でviewを変え、失敗は非AbortのOption/Result。objrefはOption<objref>、objuniqは成功時だけ子Loanを作るOption<objuniq>、所有s/AはMoveして`Result<s/B,s/A>`を返す。対象が元のtypeまたは基底とproofできれば元Origin bindingを使う。他の場合はOwnedとstatic補完を必要とする。genericで関係が未確定ならisはtypeの絞り込みを行わず、castはエラー。

### 13.7 代入と複合更新

代入先typeは標準setならPlace type、custom/computed/required setならvalue引数type。通常代入は、右辺をこのtypeへacquisition→対象経路を左から一回位置決定→旧valueまたは残存部分をdestruction→新value設置。非標準setterなら最後の二段をsetter呼び出しに置き換える。対象getterは呼ばない。未構築の一部への書き込み、基底view全体の置換は不可。

右辺が完了しなければ対象は評価せず、位置決定が失敗すれば旧valueをdestructionしない。旧value cleanupが完了しなければ設置しない。巻き戻しはない。結果はUnitで、`a = b = c`はaへUnitを代入する意味となる。代入経路自身はreceiver expressionでも呼び出し予約でもないが、途中のgetterやcustom setterのreceiverには通常規則を適用する。

複合代入は右辺acquisition→対象一回位置決定/get→読み取ったtypeで演算→演算限りのLoan終了→一回set。結果をsetter入力typeへfitさせ、参照slotへの更新を参照先へ自動転送しない。標準getはNon-Copy旧valueを借りられる。位置決定から書き戻しまで対象またはcustom setter receiverを保護し、共有読み取り・borrowだけを許して書き込み前に終了させる。別経路の書き込み・Move・destruction・exclusive borrowは禁止。操作はatomicではなく、失敗を巻き戻さない。

### 13.8 予約構文とComposition Root

ユーザーが拡張できるのは規定の算術・比較Contractだけ。独自operator、`! && || ~ ** ?? ?. ?:`、単項`&`、前置move、`<-`はない。asは将来用の予約語。

`$`は名前探索と独立した言語操作で、`$tryWrite`・`$abort`・`$expect`・`$require`だけをdefinitionする。置換、overload、function value化はできない。

<a id="s14"></a>
## 14. 制御フロー

### 14.1 完了

評価は通常完了Normal、Return/Exit/Continue/Yieldの転送、発散、Abortのいずれかで終わる。転送は対象と結果を持ち、自身は通常完了しない。cleanup後、反復/doはExit、選択はYield、deferはUnit Exit、反復はContinue、関数はReturnを受け取り、他の転送は外へ伝える。Neverはtypeであり完了状態ではない。

### 14.2 bodyと文脈

bodyはヘッダーと同じ行で始まる`=> item`、または一項目以上の字下げbody。どちらもscopeを持つ。declaration・指示には字下げbodyが必要。containerやmatch腕一覧はlayout上のbodyだが実行bodyとは別。

非空検査はコンパイル時指示の選択前に行う。完全な指示が一項目あれば全て除外されてもよい。何もしないbodyは`()`と書く。

Value Contextはinitial value、引数、条件、対象、演算・転送のオペランド。Discard Contextは単独または字下げbody直下のexpression。括弧は文脈と期待typeを保つ。期待Unit、未使用、到達不能だからDiscardにはならない。destructionはUnit conversionではなく通常lifetimeでの後始末である。

字下げbodyは末尾も含めexpression結果を捨て、構造的に末尾へ達する場合だけUnitを供給する。単一bodyは次に従う。

| body | 結果の扱い |
| --- | --- |
| 関数・getter | 結果Unitが固定なら捨て、それ以外は結果として返す |
| if・match・do | 外側文脈に応じてvalueまたはUnit |
| 反復 | 捨てて次の反復へ |
| unsafe・defer・setter・init・drop | 捨ててUnit |
| requireの失敗body | 捨てるが通常継続は禁止 |

対象が捨てられても、そこへ転送する全結果はTarget Result Typeにfitさせる。for/while/defer/set/init/dropと、Discard文脈のif/match/do/loopはUnit。

条件はboolで、if let等はない。一回評価してboolを確保し、条件temporary valueを逆順destruction・一時Loan終了してから分岐する。cleanupで条件を再評価しない。

`_ = E`は期待typeなしのvalue文脈でEをacquisition・転送し、temporary valueとしてdestructionするstatement。局所を作らず、複合形もない。Non-Copy Placeは@moveが必要だが、裸uniq/objuniqは子をreborrowして捨て、元を残す。抑制するのはその結果のdiscard警告だけ。Resultのエラーも捨て、Abortは捕まえない。

### 14.3 do・unsafe

doは一回実行する。自分へのexitはラベル指定時だけで、無名exitはdoを飛ばす。unsafeは字句的にraw操作やnoinitを許すstatementで、転送先にも障壁にもならず、ownership検査を緩めない。許可は内側deferへ届くが関数境界を越えない。unsafe関数のdeclarationだけではbodyを許可しない。許可を使わないblockにはUnnecessaryUnsafeBlock_Kdを警告する。

### 14.4 ラベル

`label L:`は同じ物理行のif/match/for/while/loop/doだけに付ける。新scope・value bindingを作らず、独立名前空間を使う。ラベルはbody内だけで見え、条件・対象・ガードから見えない。同一関数内で有効範囲が重なる同名ラベルは禁止。グループ化の代わりにはならない。

### 14.5 転送

`return [E]`、`exit [to L] [E]`、`continue [to L]`、`yield [to L] [E]`はNever expression。省略結果はUnit。toはexit/continue/yield直後でだけ名前付き転送を決める文脈語。return toはvalue toを返す。構文を失敗後に別解釈しない。

| 転送 | 対象 | 越えられない境界 |
| --- | --- | --- |
| return | 最内関数 | defer |
| 無名exit | 最内反復またはdefer | 関数 |
| 名前付きexit | 指定反復またはdo | 関数・defer |
| continue | 反復 | 関数・defer |
| yield | ifまたはmatch | 関数・defer |

最も近い対象を先に選び、typeやモードが合わなくても次を探さない。Discard選択への無名yieldは不可。構文が対象・障壁になるのはbody内だけ。unsafe/requireはどちらでもない。accessor、init、drop、specialization、匿名関数も関数境界。drop内returnで自動field destructionは省略されない。

### 14.6 反復

各回が新scopeで、次へ進む前にcleanupする。forは対象を一回、whileは条件を各回評価する。枯渇・false・自分へのExitでUnitを返す。while trueでも構造検査ではfalse経路を残す。

forのbindingは名前、`var name`、`_`、またはそのtupleで、一般パターンではない。`(x)`、var _、重複名は不可、`(x,)`は一要素tuple。

| 対象モード | 入口 |
| --- | --- |
| Shared | Iterable.iterate |
| Exclusive | UniqIterable.iterateUniq |
| ByValue | IntoIterable.intoIterator |

入口を一回呼び、LendingIteratorのnextを短期exclusive reborrowで呼ぶ。Noneで終了、Someの内容をbindingする。欠落・曖昧はエラーで別入口へ戻らない。iterator自身を列挙するにはIteration.owning/borrowingを使う。itemはLentItem(step)、Iteratorなら固定Item。tuple bindingは安全な参照を含め同じ要素数が必要。

| 標準対象 | Shared / Exclusive / ByValueのitem |
| --- | --- |
| Array・固定長配列 | ref/E / uniq/E / E |
| Dictionary | (ref/K,ref/V) / (ref/K,uniq/V) / (K,V) |
| Slice | 全てref/E |
| 同typeの通常整数範囲 | 全て境界整数type |
| ResolvedRange | isize |

配列・Sliceは添字順、辞書は挿入順。省略・末尾相対・異type境界の範囲は反復不可。対象Loanはiteratorまたは逃避itemが必要とする間生き、次のnextと競合する保持はback-edgeでエラー。自分宛てcontinueではbody、ループを離れる転送ではbody→iterator→内部対象をcleanupする。早期終了で追加itemを消費しない。反復bindingはbody局所/deferより後、逆順でdestructionする。

loopは末尾/Continueで反復し、自分へのExitだけが結果源。value文脈では結果typeを決め、捨てる文脈ではUnit。

### 14.7 if

最初にtrueの節、なければelseを実行する。else if列は一つの選択構文。else省略は`else => ()`と同じ。yieldは選択を終了する。Discard選択を早期終了するには名前付きUnit yieldを使う。

### 14.8 matchとパターン

対象を一回acquisitionしてモードを固定し、記述順でパターン一致かつガードtrueの最初の腕だけを実行する。一つ以上の腕が必要で、腕一覧に指示は置けない。全ての文脈で網羅性を要求する。

パターンは`_`、literal、enumケース、let/var binding、Unit/tuple、括弧だけ。名前付き定数・property・呼び出しは不可で、必要な比較はガードに書く。試験はconstructor、getter、conversion、ユーザー演算子を呼ばない。

構造パターンだけが必要な形まで安全な参照層をたどり、単名binding・ワイルドカードはたどらない。object/rawは対象外。各位置で共有経路なら共有、排他には全層と対象モードの許可が必要。要素数を厳密に合わせ、外→内、左→右に調べ、最初の不一致で止める。試験でCopy/Moveはしない。literalはbool・整数・char・非補間stringだけで、float/null/範囲は不可。

let/varは常に一致して腕bodyの局所を作る。varは局所の再代入性だけで、payload権限を増やさない。let _/var _は不可。同じパターンの名前は重複できず、腕内だけで見え、bodyで再declarationできない（入れ子scopeのshadowは可）。ガード用候補とbody局所は別のbinding identity・typeを持つ。

ガード候補は全モードで`ref/T during g`、gはガードcleanupまで。T内部のOriginは残す。bodyは所有/共有/排他経路に応じT/ref T/uniq Tとなり、ガードのtype絞り込みをbodyへ渡さない。ガード候補そのもののMove、再代入、exclusive borrow、captureは禁止。ただしCopyした内部参照は内部Originで保持できる。

試験開始からガードcleanupまで、ワイルドカード部分も含む対象全体と追跡参照先を共有Loanで保護する。非重複な場所の変更は可能で、ガードfalseでも巻き戻さない。true/falseとも候補・temporary value・新Loanをcleanupし、trueならbody局所をinitialization、falseなら対象を保って次へ進む。cleanupが完了しなければどちらにも進まない。ガードは自身のmatchへyieldできない。

**網羅性。** ガードなしの腕だけを使う。定数true、定数対象、Never body/payloadは不足を補わない。全体を覆うのはワイルドカード、binding、Unit位置の`()`, 全要素を覆うtuple。boolはtrue+false、enumは全ケースをそれぞれ全payloadごと覆う。部分パターンの組合せで一ケース全体を覆ったとinferenceしない。先行する一つの無ガード腕が後続を包含する場合は必須警告とするが、後続をtype・結果検査から除外しない。

### 14.9 結果検証

結果源は、到達不能も含む自分宛て転送のオペランド（省略Unit）、valueとして使う単一body、構造的なbody末尾Unit、省略else、for枯渇、while false。bodyは入ったと仮定して検査し、構造的継続がなければ末尾Unitはない。loop body末尾はloopの結果ではない。

Target Result Typeは順序に依存せず、declaration・構文の固定type・外側期待typeを優先し、なければ独立にtypeが付く結果源から同じ構造のtypeを得る。Originは領域と関係で合わせ、Neverを候補typeにはしない。参照のvalue読み取りとliteral fitを使える範囲は通常規則どおり。そのtypeを全結果源へ伝えて検査する。足りなければ注釈が必要で、後続利用や候補ごとのbody再検査で解かない。

expression typeは通常Target Result Type。結果源が全てNever（結果源なしも含む）で、かつ構造的な通常完了候補がない場合だけNeverとなる。ただしNeverでもtype不一致・結果不足は免除しない。

構造的完了は保守的で、定数bool、パターン包含、type矛盾、呼び出し先bodyで枝を刈らず、cleanupを無視する。if/requireは両枝、matchは全腕とガード両結果、短絡は両経路、for/whileは即時終了と反復を残す。loopの通常完了は自分宛てExitだけ。実行到達性はこの経路へcleanupを加え、送達の停止も扱う。最適化で受理条件を変えない。

### 14.10 実行時typeの絞り込み

対象はobject typeの局所letまたは再代入できない引数の裸/括弧付き名前だけ。var、field、property、添字、呼び出し、別名へ伝播しない。Effective Typeは最も派生した保証CoreへView Targetだけを置き換え、その他を保つ。全value利用に適用し、失敗時にdeclaration typeへ戻らない。slot操作@ref/@uniq/@rawとcaptureはdeclaration typeを使う。

is Tのtrue、is not Tのfalseで事実を得て、notで反転、and/orの短絡経路へ伝える。保存したboolや関数呼び出し経由では伝えない。厳密な派生のproofでだけ絞り込み、矛盾する経路も到達不能として捨てずdeclaration typeで検査する。合流では可能なDynamic Typeが残る入力だけを使い、全てが保証する最派生共通基底を保つ。全入力で候補がなければ合流後も候補なしで、矛盾経路の後のisは事実を追加しない。ループはback-edgeの安定解。deferは登録位置の事実を使い、関数・captureへ暗黙輸入しない。

実到達入力がないコードも仮定経路で検査する。転送/$abort/Never呼び出しのオペランド後の元の字句scope・cleanup前に仮の継続、Exitのないloopの反復入口状態から仮の継続を使い、cleanupで止まる転送も仮送達する。実入力があればそれだけ、なければ仮入力を合流する。initialization・二重Move・Loan・type絞り込みを確認するが、通常完了や暗黙結果源を新たに作らない。比較だけに必要なLoanは仮の継続前に終了する。

### 14.11 require

`require condition else body`はboolを一回評価し、trueで継続、falseで失敗bodyを実行する。結果・ラベル・転送先・障壁は作らない。失敗bodyは必ず入ると仮定して検査し、定数条件でも省略しない。cleanupを含む全経路で通常継続してはならず、外向き転送、Never、発散、Abortなどで終える。暗黙の転送やunwrapはない。

<a id="s15"></a>
## 15. ownershipとlifetime解析

転送やtemporary valueの最適化は、value、Place identity、lifetime、Move状態、Loan、Origin、destruction責任、失敗を全て維持する場合だけ許す。最適化で受理条件を変えない。

### 15.1 initialization・完全性・acquisition

| 状態 | 読み取り・borrow・Copy・Move | let | var |
| --- | --- | --- | --- |
| Uninitialized | 不可 | 過去にinitializationしていなければinitialization可 | initialization可 |
| Initialized | 通常の権限で可 | 再書き込み不可 | 置換可 |
| Moved | 不可 | 再initialization不可 | 再initialization可 |

letはbinding lifetimeの各経路で一回だけinitializationでき、Moveで履歴は戻らない。利用には全実入力経路でInitializedが必要。到達不能も§14.10の仮定状態で検査する。

**完全value。** 構築完了と、現在の全格納要素のinitialization・完全性の両方が必要。structは各層の基底と独自field、enumは選択中のpayloadが対象。computedは格納要素でない。構築完了を確定するのはconstructor、ケース、tuple・配列構築、noinitだけ。constructorは成功検証とcleanup後、公開前に確定する。

全体未initializationまたは全体Movedのvalueは、全体を設置する以外でinitializationできず、部分の読み書き・borrowも不可。例外は構築receiverの独自field initializationだけで、その入れ子部分へは広げない。不完全value全体のRead/Copy/borrow/Move、accessorのself acquisitionは禁止。Partial Moveは現在の完全性だけを変え、欠けた部分を再initializationすればconstructorなしで復旧できる。

tuple・配列は添字順に設置し、各要素の完了時に責任を持つ。途中転送では設置済み部分を後始末し、巻き戻さない。

**Move Path。** 静的に追跡できるfield（既知の基底経路を含む）、tuple要素、固定長配列の定数添字、およびその組合せ。追跡できること自体はMove権限を与えない。動的添字、動的container、ユーザーindexerは含まない。定数添字は、括弧を許すi32にfitする一つの非負整数literalだけ。名前、expression、負号、conversion、`^`は不可。範囲外literalはMove Pathを作らず、実行時Abortとなるが、その事実だけを静的エラーにはしない。

非Move Pathの添字は配列全体の完全性を要求し、部分Moved中の書き込みにも使えない。所有match payloadも追跡経路になる。参照先からは部分的にもMoveできず、getterの背後へ経路を延ばさない。祖先にdropがあってもPartial Move自体は禁止しないが、全体利用またはdestructionに達する全経路で復旧する必要がある。try失敗、defer、内側dropの順序も含める。

acquisitionの構造的適格性はPlace種、所有経路、Move Path、field/drop等から決め、実際の合法性は権限・initialization・完全性・構築中状態・Loan・Origin・祖先状態で確認する。constraintは構造的条件だけをproofする。親全体へアクセスせず完全な子を位置決定できるなら、不完全な親のその子は利用できる。

| 操作 | 必要条件 |
| --- | --- |
| Copy | Read、完全性、Copy proof |
| shared borrow | Read、完全性 |
| exclusive borrow | Read/Write、完全性、貸出点への排他書き込み権限 |
| Move | Take権限を持つMovable Place |
| Initialize | 専用のinitialization権限 |
| Replace | Write、旧valueをdestruction可能、依存の維持 |

追跡するTakeは完全な所有ルート・許可されたfield/tuple/静的配列要素だけ。raw Takeはunsafeの作者責任で状態を追跡しない。borrow参照先、object payload、static、動的要素、公開Place結果、隠れたproperty storageにはTakeがない。genericの所有value全体をMoveするのに追加Contractは不要。

新しいexclusive borrowを貸せるルートはvar局所、可変static、所有temporary value、構築receiverのinitialization済み独自field、drop receiverなど規定されたもの。let所有value、所有引数、所有getter結果とそのインライン部分は不可。ただし中に格納するuniqのreborrowは参照先権限に従う。closure bindingは自身のlet/varで決まる。receiver以外の所有Placeには@uniq/@objuniqを明示する。

**match/forの対象。** Eを一回内部Subject Placeへacquisitionする。裸Placeならその場のshared borrow、格納されたref/uniqなら本来のモードでreborrowするが、共有経路を越えると共有に制限する。その他のexpressionは生成valueを使い、getterは一回呼ぶ。対象モードはShared/Exclusive/ByValueで固定し、var・Copy・body利用で変えない。ペアは全ケースの最弱権限を使い、ByValueにしない。

bindingは左からinitializationする。所有部分はTと責任を移し、共有部分はCopy typeでもref/T、排他部分はuniq/T。`_`はbindingせず対象に責任を残す。varは局所の再代入性だけ。参照越しにMoveしない。結果確保→腕cleanup→対象の残存部分destructionの順。borrow対象由来の結果は元の参照先とLoanに依存し、内部Subject slotには依存しない。所有対象のpayloadを新たに借りた結果はmatch外へ逃せない。

### 15.2 Origin・Owned・Transferable

Originは名前、`x.slot`、static、交差`a and b`で表す象徴的lifetime。領域は有効なプログラム点の集合。type identityは正規化した記号形を比べ、outlives/==は領域constraintを課す。borrow value名をOriginとして使う場合は格納borrowの外側Originを指し、変数slotのlifetimeではない。外側が安全なborrowでないvalueはOriginではなく、局所Originの名前は字句scope内だけで見える。

`a outlives b`はregion(a)⊇region(b)。反射・推移が成り立ち、staticが最大。==は領域等価であってLoanを統合しない。交差はmeetで、可換・結合・冪等。proof済み上位の要素は除去できる。正規化には各declaration自身のpremiseを使い、別入力のLoanをOrigin簡約で消さない。

**Owned。** 非static lifetimeから独立している性質であり、所有Semanticsやallocation権限ではない。OwnedOriginsの全要素がstaticと等価ならOwned。次を再帰的に含める。

- 外側Origin、rawの参照先を含むSemantics対象。
- 未使用・継承分も含む全type/Origin引数、基底、全field、全enumケース、tuple・配列要素。
- 具体closureのcaptureと固定シグネチャOrigin、Function Itemのbinding引数、common Function Typeの固定Origin。

呼び出しごとのOriginは含めず、消去された環境・payloadは消去時にproofする。空コレクション、未選択ケースも免除しない。a outlives staticのproofは等価proofとなる。static格納、type erasure、循環オブジェクト生成、明示constraint等でOwnedを要求するが、acquisition・Loan・unsafe・並行安全の検査は残る。可変staticへの安全な新規borrowは有限で、排他Originを安全にstaticへ延長できない。

**Transferable。** 構造的な組み込みContractで、ユーザーconformanceは不可。workerへの受け渡しとconfinedでの不変static読み取りを決める。Ownedとは独立で、呼び出し側borrowはjoinまでLoanを保てばよい。OwnedOriginsと同様の構造をたどり、到達可能なrc、Weak<rc>、raw、common Function Type、実行時Contract/基底object viewはRefuted。rawの例外は指定されたKimiコレクションと標準iteratorの内部だけ。AsyncのSender/Receiverは例外でない。再帰は固定点、抽象typeにはpremiseが必要。同期や外部関数の並行再入許可は与えない。

### 15.3 Originのdeclaration・名前・関係

**注釈と集合名。** duringはref/uniq/objref/objuniqとペアに使い、ペアではborrowケースだけの条件付き欄となる。後ろはOrigin原子一つで、交差は括弧必須。注釈はborrow生成・acquisition・lifetime延長をしない。

`Type{set}`は既知の非空schemaを持つそのtype出現に集合名を付けるだけ。対応表、位置適用、空中括弧、集合全体の等価比較、`f{a}`呼び出しはない。一欄typeの`Type during x`はその欄==xの略記で、borrow層は増やさない。`value.slot`は連続する安全なborrow層だけをはがしてschemaを参照し、field・type argument・rawへは進まない。

struct/enumヘッダーで新Origin欄をdeclarationし、継承名を再declarationしない。group/contractは欄を持たない。格納typeのOrigin名はdeclaration欄か見える外側Originへ解決し、未知名から欄を合成しない。入れ子は字句環境を継承する。欄名は公開APIで、未使用欄もPhantomとして残す。

格納する全borrow層・集約欄は、公開Origin、static、完成したtype argument、注釈、関係で完成させる。initializerから隠れた欄を作らない。field固有の集合名はそのdeclarationと付随節だけ。残る形成条件は公開premiseからproofする。

**関係。** `origin A == B`または`origin A outlives B`を一つずつ書く。複数関係は全て必要。関係の連鎖表記、or/not、実行時試験はない。公開関数・typeは全許容bindingでdefinitionを検証し、各使用で条件をproofする。field、payload、associated type指定は外側contractからproofし、局所・aliasは既存証拠とinitialization expressionから検査するだけで新たな仮定にはしない。閉じた偽関係はdefinitionエラー。

**名前と量化。** 名前付き関数のシグネチャ注釈にある未bindingの単純Origin名は、結果だけに現れても全称パラメーターを導入する。関係節や局所注釈は既存名を参照するだけ。

| 名前の種類 | 範囲 |
| --- | --- |
| 関数のOrigin・集合名 | シグネチャ、付随節、body |
| typeのOrigin欄 | typeと継承member |
| field・ケース・associated type指定の集合名 | そのdeclarationと付随節 |
| 基底集合名 | その断片のconstraint |
| associated typeのOriginパラメーター | 右辺、形成条件、付随節 |
| 局所集合名 | type、付随節、以後の局所scope |
| alias集合名 | 付随節とその文書のalias範囲 |

最初のOrigin文脈候補scopeで役割を確定する。同じscopeで役割が衝突したり不適切でも、外側へ戻らない。新Origin/集合名は既存Origin・集合・引数・局所を隠せない。入れ子の名前付き関数が外側Originを使えば固定Originとなり、実行時captureは生じない。

Function Type、Callable、匿名関数は名前付きOriginを導入しない。直接入力の省略Originは呼び出しごとに量化し、既にbindingした内部依存は固定する。集約入力内部は完全typeとして扱う。局所initialization・匿名引数の許された省略はbody内inference変数を一度固定し、内側の呼び出しごとのbinderを外へ出さない。

**分散とLoan requirement。** 外側borrow Originは共変。uniq/obj/objuniq/rawの対象は不変、その他は共変、関数入力は向きを反転する。ペアdeclarationの分散は共変だが、bodyで不変Semanticsを許す対象は不変に扱う。欄のLoan requirementはnone<ref<uniqで、保持すべき呼び出し側Loanを示すだけで権限を与えない。

分散、Loan requirement、OwnedOrigins、Transferableはdeclaration単位の最小固定点で求め、実体化を無限展開しない。格納された安全参照やLoan fieldへ届かないPhantom欄は不変・Loan requirement noneで、依存だけを保持しアクセスを与えない。

`Loan<T>`は外側が安全なborrow type T用の0バイトstruct。Tを格納したのと同じOrigin/Loan性質を持ち、TがCopyならCopy。initで依存を保持するがアドレスや参照先アクセスを与えず、destructionは自身の責任を終えるだけ。

**inference。** 未確定欄だけを解く。共変は等価で固定されなければ上限のmeet、不変はproofされた等価、反変は他の全下限より長いとproofできる下限を使う。outlivesの循環は等価類、両分散を受ける欄は不変。候補を決めてから残りの境界を検査する。候補なしならbody内領域、body外なら明示注釈が必要で、staticを発明しない。

Origin inferenceはoverload後で、候補を変えない。proofは反射・推移・static・等価・meetの規定法則だけ。meetを右辺に持つ関係を、一般に別々の要求へ分解しない。UnknownとRefutedを区別する。

contract equalityは構造、量化、条件、binding、保証を各declarationのpremiseで正規化して比べる。Origin/集合の綴りはoverloadを区別しないが、typeの欄名はAPI。互換な実装は要求contractの全呼び出しを受け、同等以上の結果保証を持つ。要求入力<:実装入力、実装結果<:要求結果、実装条件は要求premiseからproofする。要求側の全称Originは固定し、解くのは実装側の呼び出し時Originだけ。Originの実行時引数やタグはない。

### 15.4 Originの完成と省略

contractはdefinition時に一回完成させ、名前付き関数のbodyからinferenceしない。結果の既存binding・注釈・等価を保ち、非自明なoutlivesに残る未binding結果欄を全称とした後、残りへ省略規則を使う。未解決結果欄同士の等価類には全位置で同じ既定Originが必要。異なる/欠ける場合は明示関係を要求する。形成条件だけで量化を増やしたり、恒真関係で省略結果を変えたりしない。

| 位置 | 完成方法 |
| --- | --- |
| 直接のborrow入力・receiverの外層 | それぞれ独立Origin |
| 関数入力集約の未binding欄 | それぞれ独立した全称Origin |
| 結果 | 下記の結果省略規則 |
| initial value付き局所 | initialization expressionから一度inference |
| initial valueなし局所、field、payload | 明示的に完成 |
| static | Ownedを要求し、共有/none/ref欄だけstatic既定。uniqは不可 |
| accessor・specialization | 継承するcontractを優先 |
| Adaptation・is・cast対象 | 元オペランドから規定どおり補完 |

入力完成はtype argument、tuple、配列、borrow対象へ進むが、field内部、既に完成したtype、別の呼び出し可能境界へは入らない。入力の入れ子borrow層のOrigin省略はエラー。

結果の各欄は独立に省略する。直接のborrow入力（receiverを含む）があれば全外側Originのmeetを使い、集約内部のborrowは数えない。なければ共有外層はstatic、排他外層は明示が必要。集約欄をstaticにできるのは、全入力Ownedをproofでき、Loan requirement none/refの場合だけ。条件付きペアでは全ケースで確認し、暗黙Owned constraintを足さない。Option内のrefは直接borrow入力ではない。

局所type・inference変数はdeclaration時に固定し、以後はfit constraintだけを検査する。代入でinferenceし直さず、未proofをstaticや依存消去で逃がさない。通常の格納に一律Ownedは要求せず、ヒープ配置でも参照先lifetimeは延びない。

### 15.5 exclusive borrowのlifetime

排他性はOrigin名でなく競合と子領域の包含で保証する。排他receiverからのuniq結果はreceiver Origin内。格納uniqを共有/排他receiver越しにreborrowしても、元の外部Originまで延長して返せない。消費したviewから参照そのものをMoveする場合は外部Originを保持できる。rawによる分割は別途unsafe contractに従う。

### 15.6 Loanとborrow検査

Loanは貸出点Placeとref/uniqモードを持つ。borrow、Copy、派生参照、それを格納する集約・closure・storageがLoanを運ぶ。結果・書き込み先が保持し得る依存は公開contractで決める。後の利用やdropの観測があれば生存する。Origin関係そのものはLoanを生成・統合・生存させない。

保持valueの全体置換は旧Loan、部分置換はその部分のLoanを終える。参照、非静的添字、動的container越しの書き込みは届き得る全Placeへ依存を加え、既存依存を消さない。合流は和集合、ループは最小固定点のmay解析を使う。

**形成とfit。** Origin oを含むvalueが点Pより後に使われ得るなら、Pはregion(o)内でなければならない。安全な`B/V during o`にはOwnedOrigins(V)の全要素がoより長いことを要求し、ペアは各ケースで確認する。シグネチャ形成条件はdefinition側のpremise、使用側のobligation。

type fitはまずOriginを同等と見なした構造を検査し、構造不一致だけをtype不一致とする。構造が合えば分散に応じたOrigin関係を作り、選択後に確認する。Owned/Callable等のconstraintは別で、通常の適用可能性に参加する。Refutedはエラー、最終UnknownはProofエラー。失敗関係を領域inferenceのpremiseへ加えず、そこから派生Loanエラーを作らない。

**非重複。** proofできる形は閉じた次の規則だけ。

- 同一Placeまたはインライン祖先・子は重複。
- 別ルート、別インラインfield、別tuple要素、異なる静的配列添字は非重複。
- 同時に生きる別アンカーの排他的参照先は非重複。他の参照はLoan由来を使う。
- rawの新規アンカーと、そこから派生しないPlaceとの関係はunsafe責任として比較しない。
- staticの同じキーは同ルート。同fieldのopen keyはその全キーと重複。

その他は未proof。実行時`i != j`、一般整数proof、別参照変数だけでは足りない。Sliceは配列全体を覆う。生きたrefは読み取りとshared borrowだけを許し、uniqは別経路からの全アクセスを禁じる。参照計数・Weak管理表は言語のPlaceではない。

**reborrowと分割。** 子は参照先をアンカーとし、生存中は親の競合利用を止める。親を格納する変数自身には依存しないが、親と、親までの安全なborrow経路の最初の共有層まで（その層を含む）のOrigin内に収める。共有参照越しのshared borrowはその共有参照のOriginだけに収める。排他的な子と残部は相互非重複、共有の子は重複可能。iterator/残部のdestructionは公開済み子Loanを終えない。結果が呼び出しreceiver Loanに依存せず、後の呼び出しeffectも競合しなければ、同receiverの次の呼び出しを越えて保持できる。

**呼び出し。** 新しいOriginを用意→引数fit→全実体化typeと結果の形成・declaration関係のproof→依存収集→呼び出し側Loan確認の順。入力borrow Originを結果が名指すなら、参照先のOwnedOriginsにも依存する。Loanは形成から（排他予約を除き）関数cleanupまで保護し、結果保持中も必要な排他性を保つ。

要約はstaticの全アクセス・initialization・後始末、結果の可変staticアンカー、呼び出し・specialization・default valueを含むdeclaration単位固定点。再帰の置換でstaticキーが自己不変とならなければそのfieldのopen keyを使う。未知の環境effectやtype erasure済みdestructionは全ての生きた可変static Loanと競合する。不変staticは除く。task呼び出しにも同じ比較を行う。

可変static由来の結果は、borrow入力で境界付けられた結果contract以外へbody外流出できない。別の固定Originを持つ引数・captureへ保存して逃がすこともRefuted。該当関数は既知要約の直接Function Item/具体closure呼び出しだけで、requirement・virtual・common Function Type・Callable引数へ消去できない。

**固定・有限・inference Origin。** 固定Originはcontractの全称、引数、receiver、projection、static、closure capture・呼び出しreceiver・inference結果全体。有限Originはbodyルートのborrow、または可変static borrow/結果で、それぞれbodyや規定の入力境界内に限る。rawの新規borrowは自動的な有限上限を持たない。

関係は生存・到達性に関係なく確認する。outlivesを有向辺、==を両向き辺とし、inference・有限Originを経由して固定/有限Origin同士を結ぶ連鎖で判定する。有限領域からその境界外の固定Originへの連鎖はRefuted。固定同士を結ぶ連鎖はpremiseからproofし、証拠がなければUnknown。有限/inference端の非反証連鎖は領域inferenceへ条件を与える。局所lifetime後の利用はdestruction/MoveとのLoan競合、局所borrowのreturnは結果Origin関係の反証として扱う。

**destruction。** dropが観測し得る全Origin/Loanはdestruction時に有効でなければならない。ユーザーdropは空でも全到達Originを観測すると見なし、field destructionも再帰的に含める。何も観測しないdestructionには追加lifetimeを要求しない。

**呼び出し予約。** receiver/引数となるexclusive borrow・reborrowは、直ちに排他Loanを有効化せず予約する。標準projectionだけのルート→貸出点は明示@uniqも含め一準備。getterや内側呼び出しは自身の入口で有効化する。予約は局所格納、集約、capture、内側呼び出し、制御expression結果、添字を計算するexpression、複合更新の境界を越えて持ち運ばない。

位置を一回決め、Place・所有者・provenanceを保護する。共有の読み取り/borrowは許すが、書き込み・Move・destruction・再allocation・別exclusive borrowは禁止。receiver→明示引数→既定引数の準備後、全予約を同時にLoan化できなければならない。重なる共有/排他引数もエラーで、temporary valueを規定より早くdestructionして回避しない。

外向き転送の結果acquisition中も予約を保護し、放棄する呼び出しの予約だけを有効化せず解除してから残りをcleanupする。引数内部で受け止める転送は呼び出し放棄ではない。

### 15.7 完全value更新

replace/exchange/swapは組み込み操作で、Sealed/Copy/Ownedを一般には要求しない。対象はinitialization済み・完全なTの記憶領域で、排他的に書ける必要がある。object payloadには完全なSealed type proofが必要で、基底/open viewは不可。Tは不変のuniq対象から固定する。未initialization・部分Movedの修復には使わず、borrow先を空にしない。

| 操作 | 責任と結果 |
| --- | --- |
| replace | 旧valueをその場でdestruction→新value設置、Unit |
| exchange | 旧valueを転送→新value設置、旧valueを返す |
| swap | 二つの責任を交換、Unit |

更新前に全引数fit・acquisition・予約有効化を完了する。引数が完了しなければ対象は保つ。replaceのcleanupが止まれば新valueを設置しない。constructor、initializer、setterは呼ばない。exchange/swapはユーザーコード・drop・Abortを伴わない。swapは静的な非重複proofが必要で、実行時アドレス比較では足りない。

`=`、複合代入、増減も含む全体更新は、新value・return value・生存value、操作の排他Loanと親依存を保持する。旧内容を指す生きたborrowを無効にする更新は禁止。同じアドレスへ新valueを置いても旧borrowは復活しない。格納参照のCopyは外部依存だけ、fieldそのもののborrowはfield依存を持つ。

payload更新はオブジェクトidentity・allocation・動的type・所有モードを保ち、最終releaseにはならない。旧内容の事実は失うが、オブジェクトtypeの絞り込みは保持する。

### 15.8 type erasureとclosureの逃避

基底/実行時Contract viewへpayloadを消去するには、handle自身の外側Originを除く完全データtypeがOwnedである必要がある。viewはproofを保持し、upcast/cast/overrideのselfへ引き継ぐ。proofされた固定対象だけをstaticで補完できる。

closureは全captureのOrigin/Loanを保つ。環境内の所有valueを借りた結果は呼び出しreceiverに依存し、capture共有参照は外部Originを保つ。capture uniqのreborrowは外部OriginとLoanを保ち、Shared/Exclusive呼び出しでは呼び出しreceiverにも依存する。Consumingで参照そのものをMoveすれば外部Origin・権限を転送できる。入力由来の結果は引数contractに従う。

結果注釈省略時は、全結果源より短い呼び出しごとのOriginを、共変meet/不変等価でinferenceする。呼び出し局所や消費済み環境のborrowは返せない。明示結果・期待シグネチャは通常省略規則を使い、隠れたreceiver名や環境bindingを注釈名として使えない。common Function Type/Callableは隠れたreceiver依存の結果を公開できない。

逃避・保持は保存先のcontractで検査する。borrow captureを一律に逃避不可とはせず、Move captureでも内部依存を残す。closureの移動・ヒープ配置はlifetimeを延ばさず、ref/FやCallableだから保存されないという保証もない。

<a id="s16"></a>
## 16. scope終了とdestruction

### 16.1 defer

deferは到達した時点で登録するだけで、bodyの評価・capture・borrowを行わない。直接含むbodyのScope Exitで実行し、未到達なら実行しない。分岐、腕、反復、do、unsafe、require失敗body、defer自身も登録先のbodyになり、各反復は別登録。cancellation・手動呼び出し・suspensionはできない。

deferの外へ転送できない。自分宛てのUnit exitはdefer bodyを終了し、内部対象への転送は通常どおり。入れ子関数は独立境界。defer内cleanup後、保留中の外側Scope Exitを再開し、確保済み結果や以前の登録は変更しない。

名前とEffective Typeは登録位置で固定するが、binding状態は実行時の各退出経路で検査する。cleanup中のMoveは後の自動destructionから責任を除く。入れ子deferは外defer実行中にそのbodyへ登録し、その終了時に実行する。既に退出中のscopeへ新規登録しない。unsafe許可は字句位置に従い、rawのlifetimeや二重destructionを自動保証しない。

### 16.2 Scope Exit

通常完了、return/exit/continue/yield/tryで実際に離れるscopeを、内側から外側へ後始末する。各scope内ではdeclarationとdeferを逆字句順に処理し、遅いinitialization・再代入で順番は変わらない。登録済み、initialization済み、責任が残るものだけを処理する。最後の利用は所有責任の終了ではない。

引数・self・パターン・反復bindingはbody前に記述順で導入したものとして扱う。途中転送時のtemporary valueと未完成集約の設置済み要素は、acquisition点の最内bodyに属し、逆acquisition順に、先行declaration/deferより先にdestructionする。既に転送した責任は除く。

結果の送達は、オペランド評価→Copy/Moveで結果確保→結果expressionのtemporary value cleanup→退出scope cleanup→対象へ送達の順。評価中に別転送が起きればそちらに従う。cleanupは確保済み結果を置き換えず、そのborrowも全cleanup後まで有効でなければならない。

defer登録はbody開始時に消費し最大一回実行する。全cleanupが完了して初めて保留転送を送達する。cleanupはsuspensionせず、cancellationも観測しない。Abortなら残りを行わず巻き戻さない。強制終了・undefined behavior・解消しないrc循環にはcleanup保証がない。

### 16.3 drop

dropはstruct一層のdestruction処理で、断片結合・条件選択後に最大一つ。引数、generics、Origin、結果type、修飾を持たず、直接/間接呼び出しもfunction value化もできない。unsafe操作には内部unsafe blockが必要。空dropでもNon-Copyになる。Partial Move後は全体利用/destructionまでに復旧が必要だが、全体Moveは可能。アクセス指定で自動destructionを止められない。

dropのselfはuniq/Self相当のアクセスを持つ特殊receiverで、通常の所有value・borrow valueではない。Copy/Move、全体代入/exchange/swap、メソッドやsetterも含むuniq/Selfへの引き渡しは禁止。field Moveも不可で、内容acquisitionには適切なexchange/swapを使う。self@refはdrop内に限りhelperへ渡せるが、capture・保存・返却・object view化で逃がせない。D層のdropではSelf=Dで、destruction済みの派生層へ戻れない。

| value | destruction順 |
| --- | --- |
| 完全struct | 自身のdropとそのcleanup→独自fieldの逆論理順→基底 |
| 自身のdropなしの部分value | 残存する独自fieldを逆declaration順にdestructionし、基底は自身の状態に従ってdestruction |
| tuple・固定長配列 | 逆添字順 |
| enum | 選択中の残存payloadを逆declaration順 |
| Slice・UniqSlice | 要素をdestructionしない |
| closure | 残存captureを逆initialization順 |

代入順はdestruction順を変えず、accessorも呼ばない。Scalar配列のdestructionはnoinitでも要素を読まない。コレクションの要素順は各contractに従う。

ownerは正確なT、objは完全な動的typeをdestructionして領域をdeallocationする。rc/arcは計数を減らし、最後だけ動的な派生→基底の完全destructionを行う。Loan比較のhandle destruction対象はそのslotだが、payload borrowは経由handleのLoanも保持する。どのstrong releaseも最後になり得るため、payload dropのOrigin観測とeffectを持つ。

open/実行時Contract viewや共通関数環境の消去済みdestructionは未知effectとなり、全可変static Loanと競合し、confinedでは分類不能。Sealed/ownerは既知要約、genericの未知typeはCopy以外のdestructionを未知としてbodyを検査し、type依存effectを呼び出し側へ公開する。borrow/rawのdestructionは参照先をdestructionしない。

### 16.4 closureとdestruction中オブジェクト

Consuming呼び出しでは環境bindingが引数より前にあるため、captureのdestructionは引数の後、結果送達の前。Shared/Exclusive呼び出しは環境をdestructionしない。

destruction中のオブジェクトからhandle/viewを作れず、dispatch、is、castも不可。Weak upgradeはNone。destruction領域は完了まで排他Loanで保護し、別経路からのアクセスは競合する。外部/function value呼び出しに追加特権はなく、raw約束違反はundefined behavior。

<a id="s17"></a>
## 17. 失敗処理

### 17.1 基本方針

例外はない。対処可能な不在・失敗はOption/Result、継続不能な失敗はAbort。実行時の警告もAPIの通常valueとして扱う。伝播は明示tryだけで、後置`?`はない。requireはunwrapも伝播もしない。

### 17.2 OptionとResult

Optionは理由なしの正常な不在、Resultは理由付き失敗。両方が必要なら`Result<Option<T>, E>`を使う。通常enumのCopy規則に従い、選択中のケースだけでCopyを決めない。失敗valueをreturnすれば通常のScope Exitを行うが、Resultを返す関数でもAbortは起こり得る。

### 17.3 try

tryはownerのKimi.Option/Result一層だけを扱う。同名ユーザーtype、安全borrow、object handle、Never typeのオペランドは不可。成功payloadがNeverであることは許す。

期待typeなしのvalue文脈で一回acquisitionし、Copy PlaceはCopy、Non-Copy Placeには@move、temporary valueは転送。Some/Okはpayloadをvalueとして取り出し、Placeにはならない。None/Errはその位置のreturnとして伝播する。返却先はOption入力ならKimi.Option<U>、Result入力ならKimi.Result<U,F>に限る。Resultのエラーは外側結果のエラーtypeへ通常fitし、暗黙conversion・エラーtypeの合併はない。成功type Tと外側成功type Uは独立で、成功の自動Some/Ok化もしない。

defer、既定引数、テスト検証メッセージ、トップレベル、Unit結果のmain/init/set/dropでは使えない。既知Some/Okや到達不能でも確認する。payload/エラーを確保後にcleanupし、途中の集約・引数評価の残りは飛ばし、既acquisition分をdestructionする。期待try typeやreturn対象をオペランドのinference・選択へ逆伝播しない。匿名関数の暗黙失敗returnは独立した結果type候補を与えない。user-defined try、try block、Option/Result相互conversionはない。

### 17.4 Abort

検査付き実行時操作の失敗はAbortとなる。通常整数のオーバーフロー、整数のゼロ除算/余り、不正位置・範囲、逆向き範囲の反復開始、数値conversion・シフト失敗、辞書重複/欠落キー、禁止staticアクセス、Kimi object生成・common Function Typeへの消去のallocation失敗、計数overflowなど、各規則が原因を定める。is/castの失敗はbool/Option/ResultでありAbortではない。

`$abort(message)`はstring期待typeでmessageを一回評価し、完了後にAbortするNever expression。message自身の転送・発散・Abortが先ならそれに従う。AbortはCompletionではない。

一プロセスでAbort開始は最大一回。複数threadが競合すれば一つだけが報告を試み、他の開始者は戻らず報告しない。他threadは終了まで走り得るが、その間の動作はunspecified。Scope Exit、unwind、static shutdownは行わず、catch/resumeも不可。cleanup途中なら残りを止め、保留結果を送達もdestructionもしない。

スタック枯渇もundefined behaviorではなく、cleanupなしのプロセス終了。ただし検出・diagnostic・終了状態はunspecified。通常Abortは失敗終了状態で理由と位置の報告を試み、出力失敗でも終了する。具体形式は実装contractに従う。

全ビルドで同じAbort規則を使い、Wrapping/@wrap以外のwrapや飽和モードはない。短絡等で実行しない操作からAbortしてはならない。unsafe違反の検出は保証しない。必須定数評価等の指定静的検査はエラーだが、定数畳み込みで実行時Abortを静的エラーへ変えてはならない。

### 17.5 警告

警告はtype・実行・候補選択を変えない。同じdiscard結果の優先順は、意図しないUnit inference、Result discard、try成功value discard、無effect value discard。`_ =`は当該結果だけを抑制する。補間文字列をUtf8Writer.writeへ渡して所有文字列を作る警告は独立。

- Kimi.ResultのdiscardはCopy/ケースに関係なく警告。Option固有のdiscard警告はない。
- tryの成功結果はUnit/Neverとproofできなければdiscardを警告。genericはdefinition時だけ判定する。
- Unitがdeclaration・構文規則・期待typeで固定されていない、valueとして使う構文や結果inference匿名関数がUnitとなり、そのUnitを供給する字下げbody末尾で非Unit/非Neverを捨てる場合、意図しないUnit inferenceを警告する。明示Unit・転送・反復・statement・別関数等の境界を越えて追わない。
- literal、Copy局所、組み込み演算/比較、ケース構築、tupleだけから成り、非Unitで無effectとproofできるexpressionのdiscardを警告する。呼び出し、ユーザー比較、Move、Loan、drop、Abort、発散はeffectで、呼び出し先bodyから無effectを推測しない。
- literalのみのexpression・`^`・省略境界だけから成る位置/範囲が、全ての長さ、または対象固定長配列の長さで必ず失敗するとき、最適化に依存せず正確に判定して警告する。動作は実行時Abort、try APIならNoneのまま。

### 17.6 テスト検証

`$expect(boolExpr, message: stringExpr)`と`$require(...)`は独立項目で、expressionオペランドには置けない。messageは省略可。test-only関数とその内側の関数・closure・deferだけで使え、テストからしか呼ばれない製品helperでも許可されない。unwrap・type絞り込みはしない。

条件を一回評価する。trueなら条件cleanup後に継続。falseなら失敗を記録→条件cleanup→遅延messageを一回評価・cleanup→expectは継続、requireはその位置のKIMI_E_REQUIREでAbort。bool確保前の転送等は検証失敗ではなく、false確保後の記録は後の失敗でも消えない。messageのMove・initializationは失敗経路だけに適用する。

messageは条件temporary valueを参照できず、外部対象への転送もできない。報告は元評価で得たvalueだけを使い、再評価・再読取・短絡部分の補完・ユーザーコード・追加acquisition/Loan/lifetime変更をしてはならない。全ビルドで実行し、定数falseを静的エラーにしない。require Abortは分離された現在のテストケースを止め、他ケースは継続する。

<a id="s18"></a>
## 18. モジュールと依存関係

### 18.1 Kotonohaとソース環境

Kotonohaは名前付きモジュール。Project rootはdeclaration階層の頂点、Compilation rootは現在モジュールのルートdeclaration・直接依存の参照名・予約Kimiを含む。Source environmentは文書のaliasと探索文脈を持つ。`::`、参照名探索、alias対象はdefinition文書のCompilation rootで解決する。

Symbolは元Kotonohaとバージョンを含むdeclaration identityであり、別名の綴りから独立する。利用側はdefinition環境を変更できず、結合断片と生成文書もそれぞれの環境を保つ。

### 18.2 alias

`alias Path`はモジュールまたは完全にbindingしたcontainerの直接memberを明示alias段階で開く。`alias Name => Path`はモジュール/groupだけをtype name前空間の修飾子にし、memberを開かず、type・value・storage・identityも作らない。

aliasは文書先頭の通常declaration/実行項目より前に置く。除外項目も順序検査に含め、入れ子・アクセス修飾は不可。対象全体をaliasなしのCompilation rootから解決するため、aliasの連鎖・循環・順序依存はない。`::`は任意。receiverやmemberの利用制限は保持する。

declaration時にdeclaration identityと全bindingを固定し、テキスト置換・使用時再inferenceはしない。アクセス、形成、constraint、Originをその時点で検査し、member利用も毎回確認する。追加アクセス権・conformance証拠は与えない。文書限定で、別断片・生成文書・呼び出し側へ伝わらず、再公開もルートmember化もしない。同じdeclarationと同じ完全bindingだけを重複除去する。

全文書に選択グラフのKimiを開く必須の既定aliasがあり、削除・変更できない。追加既定aliasは当モジュール限定。既定aliasは探索段階6、明示aliasは5なので、明示することで優先関係を変えられる。

### 18.3 直接依存と可視性

再exportはない。外部type・requirementを名前で書くには元モジュールへの直接依存とアクセス可能パスが必要。公開シグネチャや推移的読み込みだけで名前は見えない。外部type nameを書かず、inferenceされたvalueを使うことはできる。

### 18.4 identity・グラフ・解決

参照されるLibraryとpackするProjectはId/Version双方を持つ。Applicationが依存を使うだけなら不要。パッケージidentityはPackageIdと大小文字を区別する厳密なVersion。PackageIdなしはProject identityを使う。別バージョンは別Kotonoha・internal領域・Symbol。同じIDの二版を異なるReferenceNameで直接参照できる。

PackageIdは小文字ASCIIで始まり、小文字/数字の非空区画を`.`/`-`でつなぐ。VersionはASCII英数字で始まり、続きは英数字と`. - +`。ReferenceNameは識別子でKimi禁止。直接参照名とKimiはモジュールのルートdeclarationの全名前空間で予約する。同じ依存へ異なるパスで到達してもtypeやstaticを複製しない。

グラフは非循環で自己参照不可。同じidentityはtargetとtest拡張全体で入力種別（Project/Package）、内容、意味設定が一致し、一つのノードに結合する。ProjectとPackageを代用しない。test経由でroot identityへ戻ればrootのproductノードへ結合し、異入力/内容ならエラー。各モジュールの環境・specialization集合は独立し、Kimiはグラフ全体で一つ。

依存はId/VersionとProjectまたはPackageを指定し、両者を同時には書けない。どちらもなければ明示PackageSourcesで解決する。相対パスはdefinition Project基準。読み込んだidentityは厳密一致、Packageは一つのSourceIdへ決まる必要があり、競合を先勝ち・版範囲・推測で解かない。test入力からproduct候補を供給しない。パッケージ依存SourceIdは固定し、ProjectのPackage依存のSourceId pinを変更するのは明示restoreだけ。

target、Debug、言語版はグラフ全体で一つ。各依存は選択targetを許可しなければならず、Targetsとの照合は三つ組文字列の完全一致で行う。CompileTimeSettingsはdefinitionモジュール内だけで、利用側上書き、辺ごとの上書き、feature合併、条件付き依存はない。利用側コンパイラーがグラフ全体を検査し、公開者のコンパイラーには依存しない。

### 18.5 ソースパッケージ

一つのLibrary Kotonohaを含み、元の条件付きproductソース、論理名、identity、言語版、対象、設定、既定alias、native requirement、依存SourceIdを保持する。選択済みソースへ書き換えない。許可対象一覧は検証済み保証ではなく、利用側が自分の環境で検査する。パッケージのバイナリーインターフェース、版範囲、ネットワークレジストリーは対象外。

### 18.6 テスト用ソース

TestSourcesと通常ソース内のTest関数はtest-onlyで、入れ子も引き継ぐ。それ以外の無印helperはproduct。生成ソースは入力の所属を保ち、product生成はtest入力を使えない。ディレクトリー名には意味を持たせない。

productの探索・type・layout・conformance・specialization・initialization・destructionはproductだけで固定する。testは既存containerに関数を追加できるがproduct候補にはならず、通常の重複/アクセス規則に従う。product typeのfield・基底・conformanceなど固定済みの意味や、依存の参照名・内容を変更できない。依存モジュール自身のテストは推移的に参加しない。

<a id="s19"></a>
## 19. コンパイル時指示

### 19.1 配置

`#if`は次の同字下げ構文項目または字下げ一覧、`#switch`は#case分岐を選ぶ。周囲の完全な一覧項目だけを選択し、expression・type・パターンの一部、match腕、引数、generics、accessor、capture、tuple、コレクション一覧の直下には置けない。内部の実行bodyは可、単一項目bodyは不可。

選択項目を記述順に差し込み、指示の字下げでscope・lifetime・cleanup・転送先を増やさない。選択と生成後に最終declaration木を検査する。#switch全体は#ifの対象一項目にもなれる。

### 19.2 条件

条件はbool。bool/i64整数/非補間string literal、条件名、not/and/or、同typeの==/!=、括弧だけを使う。整数の符号はliteralへ直接付ける。文字列比較はordinalで大小文字を区別する。is、呼び出し、member、添字、算術、順序比較、conversion、コレクション、float、char、nullは使えない。

名前は組み込みとdefinitionモジュールのCompileTimeSettingsだけ。実行時binding・alias・genericsは供給も遮蔽もしない。名前とtypeの集合は同じモジュールの全Compilationで同一で、変わるのはvalueだけ。

| 組み込み名 | value |
| --- | --- |
| os | string: "windows" / "linux" / "macos" / "unknown" |
| arch | string: "x86_64" / "x86" / "aarch64" / "riscv64" / "unknown" |
| windows・linux・macos | 対応OSかを表すbool |
| debug・release | 互いに反対のbool |
| pointerWidth | ポインターのビット幅を表すi64 |

組み込み名は再利用できない。

### 19.3 検証と選択

解析前に入力を固定し、条件はTrue/False/Errorを取る。不明名、不正構文、type不一致、最終結果が非boolならError。論理演算でErrorを消さない。短絡でも両辺を検証し、除外領域、後続case、配置違反case内の条件も検証する。

除外の外側でだけ選択する。ifはtrueなら対象を選び、switchは構造・全条件が有効なとき最初のtrue、なければ最後の`_`を選ぶ。該当も既定腕もなければエラー。選択は構文解析中に完了し、type inference・名前探索・生成でやり直さない。

### 19.4 除外された構文

false/errorのif対象、未選択switch腕、配置違反caseのbodyと内部を除外する。内部指示は選択せず、その選択失敗も報告しないが、条件の検証は行う。

全ソースで文字コード、token、layout、文法、修飾子・属性・指示の配置、body非空、alias順、constraint先頭領域、条件を検査する。名前・type・ownership・属性の意味解決、declaration数・一意性・結合、文書コメント付着、後段処理は選択構文だけに適用する。

ソース順の配置には除外項目も数えるが、指示の包み自体を別項目とはしない。全項目が選択で消えても、選択前に非空ならbodyは有効。除外内のソースdiagnosticは同じコード・主要位置を使う。除外構文から意味上の事実や候補を供給しない。

<a id="s20"></a>
## 20. コンパイル設定（初期実装のcontract）

この章はwindows-x64プロファイルを定め、言語の有効性を狭めない。

### 20.1 Projectとソース

Project（`.kimiproj`）はApplicationまたはLibraryとKotonohaを一つdefinitionする。Solution（`.kimisln`）はProject一覧と任意の既定LangVersionを持つ。一つのCompilationは一Project・一target・一Debug設定。

productソースはProjectディレクトリー直下の通常`*.kimi`だけで、再帰探索しない。TestSourcesに指定したファイルはproductから除外する。指定はProject内の相対パスで、子ディレクトリーは可、glob・不正パス・重複は不可。論理名はProject相対の`/`区切り・NFCで、simple case foldingで等しい二名はエラー。

### 20.2 設定

設定ファイルの表記はimplementation-defined。未知・重複キー、type不一致はエラー。

| 設定 | type・default value |
| --- | --- |
| OutputKind | Application / Library、既定Application |
| Targets | target三つ組一覧、必須 |
| PackageId・PackageVersion | 文字列、既定なし |
| LangVersion | 言語版。Project→Solution→コンパイラー現在版の順 |
| Alias | 既定alias、既定なし |
| CompileTimeSettings | 名前→bool/i64/string、空 |
| Dependencies・PackageSources | 依存レコード、空 |
| TestSources・TestDependencies | テストパス/依存、空 |
| TestProjectId | 非空文字列、既定なし |
| Test | Timeoutは既定30s、Environment等のケース設定 |
| NativeRequirements・NativeLibraries | target別レコード、空 |
| Optimization | O0 / O2、既定O2 |
| OutputPath | `.ll`パス、既定`bin/<target>/<Name>.ll` |
| LlvmBin | ディレクトリー、既定なし |

対応targetは`x86_64-pc-windows-msvc`。Targetsと依存側許可対象は大小文字を区別して完全一致。ConditionsのOS/archは三つ組からOS版接尾辞を除いて次のように正規化する。

| 三つ組の成分 | 条件value |
| --- | --- |
| windows / win32 | windows |
| linux | linux |
| macos / macosx / darwin | macos |
| x86_64 / amd64 | x86_64 |
| i386 / i486 / i586 / i686 / x86 | x86 |
| aarch64 / arm64 | aarch64 |
| riscv64 | riscv64 |
| その他 | unknown |

CompileTimeSettings名は識別子で、重複・組み込み名は不可。言語版は0.0.2だけ。パッケージ化するProjectにはLangVersionを明記する。

### 20.3 コマンドと成果物

| コマンド | contract |
| --- | --- |
| restore | 明示された依存を解決してlockを置換。ネットワーク使用なし |
| check | buildと同じ入力・target・Debugで各ProjectをProduct検査。集約は§23 |
| emit | 検査して`.ll`と`.link.json`を発行。LLVMは実行しない |
| build | 現ソースから実行形式と成功記録を作る |
| build --Manifest | 発行済みのIR/manifest組から構築。ソースは読まず、生成設定はmanifestを使う |
| run | build成功後だけ起動。--no-buildでは入力と実行形式ハッシュに合う最新成功記録が必須 |
| test | 選択したテスト単位を準備し、ケースを分離実行 |
| pack / publish / store verify | ソースパッケージ作成、ローカル公開、キャッシュ再検査 |
| toolchain verify | Projectを読まずtoolchainを検査 |
| lsp | stdioの言語サーバー |

optionは大小文字を区別し、`--Format`はtext既定/json、boolはtrue/false、単独`--no-build`はtrue。--Targetはcheck/emit/build/run/packで設定済みtargetを選び、testでは全選択Projectへ適用する。target未指定は設定が一つの場合だけ選び、複数なら明示が必要。Release既定、`--Debug true`とOptimizationは独立。manifest構築にソース・target・Debug・lock設定を混ぜない。

通常成功0/失敗1、checkはaccepted時0、それ以外1。runは子プロセスの終了valueを返し、起動前失敗はstderrと1。cancellationは130。runは標準ストリームを転送し、作業ディレクトリーをProjectにする。直接`.exe`の場合は呼び出し元を保つ。

**入力解決。** 相対パスはコマンド起動位置基準、未指定は現在ディレクトリー。末尾`.kimi/.kimiproj/.kimisln/.exe`はASCII大小無視で認識し、そのパスだけを使う。それ以外のAはA→A.kimiproj→A.kimiの最初に存在するもの。存在後の読み込み失敗・不正・未対応で次へ戻らない。

ディレクトリー直下のSolutionが一つならそれ、複数ならエラー、ゼロなら全Project、Projectもなければエラー。選択した一Projectでもロードに失敗すれば全コマンド失敗。runにはApplicationが一つ必要。

単独`.kimi`はその一ファイルだけの暗黙Application/O2 Projectとなる。所在ディレクトリーとファイル名を使い、隣接ソース・設定・依存・lock・Projectファイルを追加しない。targetはホストまたは--Target。成功記録には選択したProject/ソースidentityを残す。

**発行。** Name.llに対してemitはName.llとName.link.json、buildはName.O0.exe/Name.O2.exeとName.link.build.jsonを作る。両emit出力の発行完了まで成功としない。build開始時に旧成功記録を無効化し、新exeは成功時だけ発行する。失敗後に古いexeをrunしない。Unsupportedで代用バイナリーを作らない。LibraryのIRは検査用で、manifest/記録形式はimplementation-defined。

toolchainルートは--ToolchainRoot→KIMI_TOOLCHAIN_ROOT→コンパイラー隣のtoolchain。選択先なしはエラーで、PATHやネットワークを探さない。LlvmBinはCLI→Projectの順にLLVMツールの場所だけを上書きする。CLI相対パスは起動位置、Project設定はProject基準。固定版・ハッシュ検査は通常ビルドでなくtoolchain verifyで行う。

### 20.4 nativeライブラリー

論理名はdefinition Kotonoha内で大小文字を区別する。NativeRequirementsはtargetごとのName、Kind（static/import）、任意ContractId/Sha256。予約名以外の選択されたimportごとにrequirementが必要。checkはnativeファイルを読まない。

NativeLibrariesの供給はName/Input/Package（既定はdeclaration Project）/ContractId/Sha256を持つ。自身への供給だけKindでrequirementもdeclarationできる。他モジュールのrequirementを変更できず、必要ContractIdは一致させる。同じname/targetへの複数供給は内容・ContractIdが一致する必要がある。

Inputは絶対またはdeclaration Project相対の`.lib`で、検索パスなし。実際に読むバイトをハッシュ検証し、その同じバイトをlinkerへ渡す。kernel32/kimi_backendはインストール済みの信頼する予約供給。kernel32の設定エントリーは禁止する。

必要なkernel32機能は全て供給し、他のsymbol集合はimplementation-defined。不足はnative解決エラー。static libraryはCRT起動、C/C++動的initialization、TLS initialization/終了、自動atexitに依存できない（ゼロ/定数データは可）。曖昧・未対応・Kind不一致をリンク順で解決しない。

### 20.5 パッケージ・lock

各形式はschema版を持つ。未知版・キー・必須featureは拒否する。SourceIdは正確なmanifestから決め、format 1では`Kimigayo.Source.v1`＋NUL＋manifestのSHA-256を使う。別形式とのidentityは保証しない。

lockはproduct/testを分離する。依存なしのmissing lockは空、不正lockは空と見なさない。restoreはproduct→testの順で、product失敗なら停止。testだけの失敗は記録し、restore自体は成功し得る。test以外のソースコマンドはproductだけを検証し、testは両方を使う。必要な更新を自動restoreせずエラーにし、--lockedはその方針を明示する。

ID/版/参照名/依存辺/固定内容変更にはrestoreが必要で、ソースbodyの変更だけでは不要。packはLibraryを現在環境（--verify-allなら全target/mode）で検証し、Project依存も含めbin/packagesまたは--outputへ書く。失敗なら何も発行しない。publishはローカルstoreへID/版→SourceIdの永久対応を依存closureごと追加する。同じ対応は再実行可、衝突時は全て拒否。store verifyはキャッシュを再検査し、破損を無効にする。

### 20.6 テスト実行

Solution内のProjectを重複除去し、依存のテストはそのProject自身を選んだ場合だけ実行する。ホスト・targetはwindows-x64。全ProjectのTest-mode準備でproduct/test/test依存の全bodyを検査してからlist/filterを行う。filterで不正bodyを隠せない。一つでもload/check/build失敗なら全ケースを開始しない。mainやトップレベルも検査するが起動処理は実行せず、ユーザー操作は有効なケース内だけで実行する。

--listは検査してケース一覧を表示する。--filterは大小文字を区別する完全修飾名の部分一致、--caseは既知ID一つ、--allow-emptyは空選択許可。--jobs Nは全Project合計の同時ケース上限、--no-parallelは1。--timeoutは正の整数にms/s/mを付け、最大24時間。不正・未知・重複・矛盾optionを丸めず拒否し、--caseと--filter、--jobsと--no-parallelは併用不可。Test.Environmentで追加環境を指定できるが、TMP/TEMP/KIMI_TEST_*は予約。--Format jsonは版付き結果文書1個を標準出力へ書く。

完全修飾名はProject rootからのdeclaration ContainerパスとNameを`.`で結んだもの。Project identityはTestProjectId→PackageId→Projectファイル名（拡張子込み）、暗黙Projectではソース名。選択Project間で重複不可。TestId/CaseIdはこのidentity、完全修飾名、論理パスが不変なら安定し、絶対位置・Solutionの場所に依存しない。

各ケースは専用processで一回だけ、再試行なし、順序unspecified。cwdはProject、stdinは閉じ、同期stdout/stderr handleを与え、別々に記録する。各caseの新規一時ディレクトリーをTMP/TEMPへ設定し、Test.tempDirectoryはその絶対パスを返す。どのoutcomeでもcase終了後に除去する。期限は起動→終了で、残った子processも終了させる。

falseの$requireによるAbortはfailed、他Abortはaborted。正常process終了かつ検証失敗なしだけをpassedとし、不正・欠落報告はrunner failure。Abortの報告はcaseごと1回で、他threadの後続出力により変わらない。結果はpassed/failed/aborted/timed out/cancelled（未開始も含む）/runner failure。並びは完全修飾名→論理パス→Project identity。

終了valueの優先は130（cancellation）>3（runner失敗）>2（設定・選択・対象・load/check/build失敗）>1（ケース失敗/Abort/timeout）>0。jobs default value、保存・保持上限、回収、結果schemaはimplementation-defined。

<a id="s21"></a>
## 21. layout・ランタイム・コード生成

### 21.1 layout

sizeはパディング込みのインラインバイト数、alignmentは正の2冪、strideはsizeをalignmentへ切り上げたvalue。windows-x64のsize/stride/offset上限は2⁶³−1で、必要な具体利用で超えればLanguageエラー。構成要素は自然alignmentし、集約alignmentはその最大以上。正サイズ要素は重ならず、入れ子のパディングを別要素へ再利用しない。

パディングvalueはunspecifiedで、バイト転送できてもtyped valueとして比較・読み取りしない。0サイズでも評価・initialization・ownership・Loan・dropは残り、アドレスを共有できる。layoutはfield identity、initialization、Partial Move、Loan、destruction順を変えず、同じコンパイラー/target/設定/ソースで決定的。

structは独自fieldと直接基底をインラインに持ち、computedは領域を持たない。参照/raw/handleは対象をインライン格納しない。インライン循環はdeclaration時Languageエラー。

Kimigayo layoutのfield順・パディング・alignment順はimplementation-definedで、tuple・enum payload・closureも同様。オフセットや外部ABIの安定性を保証しない。

C layoutはWindows x64 MSVC `/Zp16`に従い、記述順、各field alignment`min(alignment,16)`、全体最大alignmentと末尾切り上げを使う。pragma pack、明示alignment、ビットfieldはない。入れ子は自身のモードに従い、handle/headerには適用しない。C layoutだけでnative export、集約value ABI、直列化を許可しない。

対象はlittle-endian、ポインター64ビット。

| type | size / alignment / stride（バイト） |
| --- | --- |
| i8・u8・bool | 1 / 1 / 1 |
| i16・u16 | 2 / 2 / 2 |
| i32・u32・f32・char | 4 / 4 / 4 |
| i64・u64・isize・usize・f64 | 8 / 8 / 8 |
| i128・u128 | 16 / 16 / 16 |
| raw・ref・uniq・object handle/borrow・Weak | 8 / 8 / 8 |
| Unit・Loan・Never | 0 / 1 / 0 |

charは符号なし32ビットのスカラー表現だが、char/boolをCのchar/WCHAR/BOOLとは扱わない。Neverのインライン要素も0サイズとしてlayoutする。ref/uniqは非nullの直接記憶領域ポインターで、0サイズでもaligned非nullが必要。

Option<R>でRがref/uniq/obj/rc/arc/objref/objuniqなら一語ポインター、nullがNone。他の入れ子Option・集約・enumへこの表現を拡張しない。共通function valueは16バイト、alignment 8。環境が8バイト以下かつalignment 8以下なら内部格納、それを超えれば形成時にallocationする。その他のenum/string/header/計数/Weak表/type情報はモジュール内のimplementation-definedで、外部ABIを持たない。

### 21.2 外部関数ABI

Windows x64 C、LLVM cccを使い、signext/zeroext/inreg/byval/sretを追加しない。i8/i16はその幅で渡し、可変引数昇格はなく、レジスター上位valueに意味を与えない。ref/uniq/rawはアドレス、Optional borrowはnull可、Unit結果はvoid。dllimportはライブラリーKindに従う。

外部関数の入口/出口でfloat環境は最近接偶数、DAZ/FTZ無効、例外マスク済み。変更した外部関数自身が復元し、ランタイムは修復しない。状態フラグはunspecifiedで、言語例外handlerはない。

予約symbolは`_fltused`、`__chkstk`、`memcmp/memcpy/memmove/memset`、および`__kimi_`/`llvm.`接頭辞。importするとnative解決エラー。同一symbolのシグネチャ/ライブラリー不一致もエラー。

### 21.3 生成コード

明示specialization選択後に閉じた実体化ごとに単相化する。ただし生成するかに関係なく検査対象全体を確認する。内部ABI、symbol、最適化属性はimplementation-definedで、別生成モジュールのバイナリー互換性はなく、publicもnative exportを意味しない。

O0/O2で受理、結果、副作用、Abortコード/位置、cleanup、発散を変えない。資源上限超は原因のdeclaration・呼び出し・tokenにResourceとして報告し、独立した検査を続ける。インライン循環は資源不足に置き換えない。検査を省くには成功proofが必要で、失敗proof時は同じAbortへ置換する。

128ビット整数は格納、比較、ビット、シフト、整数conversion、検査付き加減乗算・単項符号・増減に対応する。除算、余り、float conversionと対応複合演算は、全検査対象の具体実体化で、未実行枝でもUnsupported。ただし必須コンパイル時評価と直接literal conversionは対応する。

floatはIEEEで、fast-math・再結合・融合を使わない。スタックprobeを保つ。

### 21.4 Windows x64ランタイム

基準命令セットはx86-64＋SSE2。unwind情報はOSのスタック歩行用で、例外cleanup用ではない。Applicationは`__kimi_start`から入り、CRT起動なしでruntime initialization→body→shutdown→ExitProcess(0)。Libraryに入口はない。

Abortはstderrへ次を一度報告し、LFを付けて終了1とする。

```text
path:line:column: abort CODE: Reason
```

ReasonのバイトはNUL/改行も含め改変しない。OSエラーがあれば` (win32=N)`を付ける。論理パスの非ASCII/制御文字は`\u{HEX}`、バックスラッシュは`\\`で表示する。stderr失敗時は途中で切れても終了する。

heapはHeapAlloc/HeapFree。非0 allocationはalignment 16、Rawの最大alignmentも16、最大objectサイズ2⁶³−1、Rawの0サイズ要求はallocationなし。ConsoleはGetStdHandle/WriteFileへUTF-8とLFを渡し、NUL終端にしない。画面表示はコンソール設定、overlapped handle対応はimplementation-defined。

Stopwatch/timerはQPCを使い、frequencyはKimi内部static letの初回利用でacquisitionする。QPC/QPF失敗・負カウンター等はKIMI_E_CLOCK。各threadのexecutorをrun間で再利用し、IOCP一つとready FIFOを持つが、言語上の再開順はunspecified。

FILE_SKIP_COMPLETION_PORT_ON_SUCCESSが使えるI/Oは即時成功時に呼び出し内commit、それ以外はcompletion packet時にcommitする。cancellationはCancelIoExで要求し、packet受領まで登録を保持する。ERROR_OPERATION_ABORTEDの完了でだけcancellationをcommitする。定常状態のtask呼び出し・待機・timer・channel・shield・反復runはヒープallocation 0、開始済み子の記録はpoolを使う。

runtimeのkernel32 import、mem*と__chkstkのsupport archive、モジュールの_fltusedを用意する。backendが要求するhelperの不足はtoolchain採用の失敗であり、個別プログラムの偶然のリンク失敗にしない。

<a id="s22"></a>
## 22. Kimi・プログラム実行・外部関数

この版の実行対象は`windows-x64`だけ。他の対象でのbuild・run・testはUnsupported。言語上の妥当性は、Conditionsと具体的layoutのサイズ制限を除き対象に依存しない。

### 22.1 必須の標準declaration

Compilationは、予約された直接参照名`Kimi`にKimi Kotonohaをちょうど1つ結び付ける。`Kimi`はキーワードではなく内側で隠せるが、`::Kimi`は隠せない。既定aliasはKimiを開く。組込みの扱いはdeclarationのidentityによって決まり、同名のユーザーdeclaration・conformance・aliasからは得られない。別のKimi Kotonohaを受け付けるかはimplementation-defined。

次のdeclarationは全対象に存在する。名前はKimiからの相対名。「反復入口」は`Iterable`、`UniqIterable`、`IntoIterable`。表と参照先の規則が必須の形を定め、追加declarationは許される。

| declaration | 必須の形・意味 |
| --- | --- |
| `Never` | public、non-open、非generic、Origin・基底・memberなしのstruct。valueが存在しない組込みtype |
| `Option<T>` | `Some(T)`, `None`の順のenum。TがCopyならCopy |
| `Result<T,E>` | `Ok(T)`, `Err(E)`の順のenum。TとEがCopyならCopy |
| `Equatable` | `equals(self: ref/Self, other: ref/Self) -> bool` |
| `Comparable: Equatable` | `compare(self: ref/Self, other: ref/Self) -> i32`。小・等・大を負・0・正で返す |
| `Copy`, `Owned`, `Callable`, `Sealed`, `ObjectPayload`, `PrimitiveInteger`, `Transferable` | §8の組込みContract。Transferableはpublic、引数・親・memberなし |
| 算術Contract | §13の11個 |
| `Position: Equatable, Utf8Format` | 閉じたContract。SelfはCopy・Owned。`tryResolve(self: Self, length: isize) -> Option<isize>`。12整数type、FromEnd、Start、Endだけがconformance |
| `PositionRange: Equatable, Utf8Format` | 閉じたContract。SelfはCopy・Owned。`tryResolve(self: Self, length: isize) -> Option<ResolvedRange>`。Range、ClosedRange、ResolvedRangeだけがconformance |
| `FromEnd<T>`, `Start`, `End` | Copy・Owned・Equatable。構文だけで作る位置。FromEndは`T is PrimitiveInteger`、保存valueは`offset: T`のみ |
| `Range<S,E>`, `ClosedRange<S,E>` | S/EはPosition。Copy・Owned・Equatable。`start: S`, `end: E`を持ち、構文だけで作る。SがPrimitiveInteger、EがSなら反復入口を持つ |
| `ResolvedRange` | 検証済み区間。Copy・Owned・Equatable。length、isEmpty、resolve、反復入口 |
| `Wrapping<T>` | field・memberなし、TはPrimitiveInteger。T表現の折返し算術。§3・§13のconformanceとconversionを持つが、PrimitiveIntegerにもPositionにもconformanceしない |
| `Array<T>` | Non-Copyの所有列。capacity、長さ情報、§4の列操作・view・排他的要素対・動的操作。`UniqIndexable<isize>`と反復入口 |
| `Dictionary<K,V>` | Non-Copy、KはEquatable。length、capacity、§4の操作。既存キーについて`UniqIndexable<K>`、反復入口 |
| `Slice<T> {source}` | `ref/T during source`のCopy共有view。長さ情報、読取り操作、`Indexable<isize>`、反復入口 |
| `UniqSlice<T> {source}` | Non-Copy排他view。length、isEmpty、indices、`UniqIndexable<isize>`、§4のview操作 |
| `Loan<T>` | 外側がref/uniq/objref/objuniqであるborrow type Tについてのサイズ0のstruct。`init(value: T)`。Tと同じCopy性 |
| `Weak<s/T>` | sがrcまたはarcであるNon-Copy struct |
| 反復・添字Contract | 下記および§22.2のdeclaration |
| 標準iterator | ArrayIterator、ArrayUniqIterator、ArrayOwningIterator、FixedArrayOwningIterator、SliceIterator、DictionaryIterator、DictionaryUniqIterator、DictionaryOwningIterator、RangeIterator、ClosedRangeIterator |
| `Iteration` group | OwningIterator、BorrowingIterator、owning、borrowing |
| `Intrinsics` group | 非generic。makeObj、makeRc、makeArc、makeRcCyclic、makeArcCyclic、clone、downgrade、upgrade、replace、exchange、swap |
| `Raw` group | allocate、release、initialize、slice |
| `Async` group | Cancelled、SendFailure、引数数2〜4のRaced、Sender、Receiver、run、sleep、checkpoint、shield、join、joinOk、race、each、eachReceived、pipe、offload、offloadEach |
| `Console` group | writeLine |
| `Time` group | Duration、Stopwatch |
| `Windows` group | queryPerformanceCounter、queryPerformanceFrequency |
| UTF-8整形 | Utf8Format、BufferWriter、WriteWindow、Utf8Writer、BufferFull、Text group |
| `Test` group | テスト専用body内だけで使える`tempDirectory() -> string` |

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> place ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> place uniq/Element during self
```

反復・添字Contractは静的で、associated typeは完全typeでありOrigin引数を持てる。Array、Dictionary、Slice、UniqSlice、Wrapping、Loan、Weak、Neverのconstructorは、標準declarationに明記されたものだけで、合成されない。Weak・Wrapping・Neverにはconstructorがない。

全固定配列`[N of T]`は、T・Nによらず組込みの長さ情報、読取り、view、排他的要素対、`UniqIndexable<isize>`、反復入口を持つ。これをソースでdeclarationすることはできない。ArrayはTがOwnedならOwned、DictionaryはK/VがOwnedならOwned。stringは組込みUTF-8 typeで、literal・補間・比較・Utf8Formatを持つが連結演算子はない。

Kimiを開いてもgroupのmemberまでは開かない。`Console.writeLine`や`Intrinsics.exchange`を用い、`Kimi.writeLine`、`Kimi.exchange`などのroot declarationは存在しない。aliasはdeclarationのidentityを保つ。`$abort`、`$tryWrite`はgroupに属さない。

### 22.2 反復

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

各nextのstepはそのcallのreceiver borrowを表す。stepを含む貸出しvalueを保持したまま次のnextを呼べない。generic codeが結果を次のnextを越えて保持するにはpreserves resultsが必要。3つの反復入口は独立し、member名・参照・Iteratorからの自動conformanceや既定bodyはない。標準のshared borrowからnextを進めることもない。

`next`はPlaceではなくOption valueを返す。Noneは結果Loanを持たない。LendingIterator一般はNone後にSomeを返してもよいが、forは最初のNoneで終える。IteratorのItemはnextの呼出しOriginに依存せず、`preserves results`により、過去の結果を保持したまま次へ進める。

`Iteration.owning`はiteratorをvalueで受け取り、必要なら`@move`する。`borrowing`はuniq borrowする。どちらもallocationや先読みをせず、Item、effect、終了を元のiteratorから引き継ぐ。両adapterはLendingIterator・IntoIterable・UniqIterableにconformanceし、borrow版はsourceと各nextの実際のReborrowを保持する。元がIteratorならIteratorにもconformanceする。

標準iteratorはすべてIteratorにconformanceするpublicなNon-Copy typeで、最初のNone後は常にNoneを返す。Range系iteratorはOwnedで、TはPrimitiveInteger。public constructorや反復入口へのconformanceは持たない。type argumentはArray/Slice系がT、Dictionary系がK/V、Range系がT、固定配列所有iteratorがT/A。各要素を規定順に1度ずつ返し、同時に生きる排他結果は重ならない。nextはユーザーコードを呼ばない。所有iteratorは§4の順で残要素をdropし、borrow iteratorは元要素をdropしない。各stepのコストは§4のcontractに従う。

全Iteratorの結果は、next receiverの一時的Loanやiterator自身の保存領域に依存しない。iteratorのMove・destructionが結果を無効にしてはならない。ユーザーIteratorも独立性とeffectを検証し、Move後も保証を保つ。receiver依存・競合effectを持つwrapperはLendingIteratorにとどまる。`preserves results`は同じvalue・同じrequirementへの次の呼出しの保証であり、iteratorのdestructionは別のeffectとして検査する。

### 22.3 非同期処理

Asyncはtask handleを公開せず、開始した子をすべて回収する構造化操作を提供する。task構文とsuspension点は§24。基本の公開declarationは次のとおり。

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

    public struct Cancelled                 // fieldなし。Kimiだけが生成する。
        Self is Copy
        internal init() => ()
    public enum SendFailure
        Closed
        Cancelled(Async.Cancelled)
    public enum Raced<A, B>                 // caseは勝者を示し、全ての最終結果を保持する。
        First(A, B)
        Second(A, B)
    public struct Sender<T>
        public func send(task; self: uniq/Self, value: T) -> Result<(), (T, SendFailure)>
    public struct Receiver<T>
        public func receive(task; self: uniq/Self) -> Result<Option<T>, Cancelled>
```

join、joinOk、raceの引数数は2〜4のみ。RacedのFirst/Second/Third/Fourthは勝者を示し、payloadには**全子の最終結果**を持つ。1回だけ呼ぶ子はShared・Exclusive・ConsumingのCallableを受け付け、Non-Copyのlet valueなら`@move`が必要。each/eachReceivedの子関数はSharedで、複数の呼出しが同時に生きる。

limitは1以上、capacityは0以上。違反はARG_RANGE Abort。capacity=0は送受信の直接受渡し。子へ渡すborrowは操作全体と、その結果が依存する期間にわたって有効でなければならないが、valueに一律のOwned constraintはない。

#### 実行と子の回収

`run`はtaskを持たないコードからtask treeを開始する入口。tree全体は呼出し元の同じthreadで動く。入れ子のrunは別treeを実行し、その間、外側treeは進まない。デッドロックは非終了であり、検出報告は任意、Abortではない。non-Owned valueがそのOriginの終了前にdestructionされる保証はない。

| 操作 | 決定・終了・結果 |
| --- | --- |
| join | 全子が完了して結果のtupleを返す |
| joinOk | 最初のErrで他の子へcancellationを要求。全子回収後、そのErrを返す。全てOkなら全payload。返さない結果はdrop |
| race | 最初の完了で他の子へcancellationを要求。全子回収後、勝者のcaseと全最終結果 |
| each | iterator終了後、全子完了でOk。cancellation要求時は追加acquisitionを止め、子を回収してErr |
| eachReceived | channelが閉じて空になり、全子完了でOk。cancellation時は同様に回収してErr |
| pipe | consumer完了時にreceiverを閉じ、producerへcancellationを要求し、両者回収後にconsumer結果 |
| shield | body完了後、その結果 |

join・joinOk・raceの子は引数順に開始し、それぞれ最初のsuspensionまたは完了まで動いてから次を開始する。後の子の開始前に決定が成立しても、その子はcancellation要求済みとして開始する。再開可能な子同士の再開順はunspecified。

eachは生きた子がlimit未満の間だけ、自分のframeでnextを呼ぶ。acquisition前にcancellationを確認し、この確認自体は待機・commitではない。最初のNoneでacquisitionを終え、全子を回収してからiteratorをdropする。eachReceivedもlimit未満のときだけreceiveし、受信valueの子を開始してから次を受信する。

#### cancellationとcommit

cancellation要求はKimiが開始した子へ伝え、valueとして公開しない。対象subtreeの現在・将来の待機に効く。sleep、checkpoint、I/O、send、receiveは、操作の成立とcancellationのうち、最初の1つをcommitする。既存の要求は直ちに観測され、commit済みの結果は変わらない。tree内のcommitには全順序がある。

未commitの非I/O待機はcancellationで直ちにErrになる。I/OはOSへcancellationを要求するが、完了通知まで戻らない。data転送前にcancellationが成立したと完了通知が示す場合だけCancelled、それ以外はその通知の結果を返す。cancellationは巻戻し、preemption、即時dropではない。

| 待機 | 成立時点 |
| --- | --- |
| sleep | 期限後に再開してOkをcommit |
| checkpoint | 即時、または他taskに実行を譲った後の再開時 |
| send | 受渡し・bufferへの格納、またはreceiver閉鎖 |
| receive | 要素の取出し、またはsender閉鎖かつ空 |

sendのOk時にvalueのownershipを渡し、Err時はvalueを返す。receiveのOk(Some)時に要素を取り出す。checkpointは時計を読まず、他の再開可能なtaskがある場合、またはimplementation-defined回数の呼出し後にだけ実行を譲る。

sleepとshieldは単調時計を用い、期限・猶予の和を正確に求める。sleepは呼出しから指定時間より前にOkを返さず、登録にallocationをしない。shieldのbodyは猶予前でも完了できる。長すぎるDurationだけを理由にAbortせず、時計の最大範囲を超える期限は到来しないものとして扱う。shield内では、呼出し元からのcancellationを`max(要求時刻, 呼出し開始)+grace`まで遅らせる。入れ子の猶予は加算され、内部で新たに起きた要求には通常の規則を適用する。外側の要求は消えず、shield後の待機で観測される。

#### effect・channel

effect要約は、suspension後の子の処理、iteratorのnextとdropなども含む。sleep・checkpoint・構造化操作・channel自身は、子とdropを除いて環境effectを持たない。runのexecutor状態、I/Oは環境effect。defer内ではtaskを渡せないため、待機を伴う後始末は明示的なshield等で書く。

channelを作る操作はpipeのみ。Sender/Receiverはuniqで操作する。capacity>=1では、initialization後の定常送受信にheap allocationをしない。consumer完了はreceiverを閉じ、保留sendをClosedにし、producerをcancellationして回収する。producer完了はsenderを閉じ、bufferを読み尽くすとreceiveがNoneを返す。残buffer要素はpipeが戻る前にdropする。

受信valueはReceiverの一時Loanやchannel保存領域に依存しない。切り離したtaskは作れない。動的な仕事列はeachReceivedの共通関数で処理し、generatorはcapacity=0のpipeで表せる。run以外の待機操作にはtaskが必要。Consoleは同期処理で、その呼出し中に同じtreeの他taskを進めない。

#### workerへの計算委譲

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

offloadのFはTransferableでeffectがconfinedな`Callable<owner, () -> R>`。offloadEachはIterator、TransferableなItem、Transferableでconfinedな共有Callableを用い、Fをrefで借りる。limitは1以上。

workerはtaskを持たず、cancellationやpreemptionを受けない。開始済みworkerを残して呼出し元へ戻らず、戻る前に全てjoinする。thread開始失敗はTHREAD Abort。結果はOwnedやTransferableでなくてもよい。入力のLoanはjoinまで生きる。offloadEachのiteratorは親threadに置き、Itemをvalueでworkerへ渡す。他のacquisition・回収規則はeachと同じ。confinedは環境へのアクセス権限を与えず、入力のstatic Loanは別途§24の呼出し規則で検査する。

異なるthreadから重なる記憶域へアクセスし、少なくとも一方が書込み、両方が実装によるatomic accessではなく、happens-before関係もなければdata raceでUB。順序関係はthread内順序、親の開始前から子へ、workerのアクセス・drop・結果からjoin後の親へ、static initializationからその完了を観測する処理へ、およびこれらの推移closure。arcは参照数とlifetimeを保護するだけでpayloadを同期しない。Loan規則は競合を禁じるが、それ自体がthread間の順序を作るわけではない。

### 22.4 起動・staticのlifetime

Applicationは、root sourceの実行bodyかpublic mainのいずれか1つを持つ。混在・複数・欠如は不正。実行bodyにはexpression、制御、unsafe、defer、require、discard、局所declaration（未initializationを含む）が入り、通常のdeclaration・alias・除外された項目は数えない。判定は生成・選択後に行う。空のApplicationは`()`で表す。

root bodyは文書scopeでソース順に1度実行し、局所valueはstaticにならず、return先にもならない。mainはsafe、public、bodyあり、引数・receiver・generic・Origin・task・import・specializationなし、Unit返却。各rootのpublic mainをこの形で検査する。Libraryには起動処理がなく、top-level実行項目を置けない。

static fieldは最初に実行されたinitialization・読取り・borrow・書込みでinitializationする。初回書込みも先にinitializerを実行する。typeへの言及、関数参照、未実行コード、effect要約だけでは開始しない。未使用initializerもtype検査する。

| 状態 | アクセス時の扱い |
| --- | --- |
| 未開始 | 1つのthreadだけがinitializationし、正常完了をinitialization済みとして公開してから利用。ただし終了処理開始後はSTATIC_SHUTDOWN |
| initialization中 | 同じthreadの再入はSTATIC_CYCLE。他threadは待機し、デッドロックし得る |
| initialization済み | 通常どおり利用 |
| 終了済み | STATIC_SHUTDOWN |

起動処理の局所valueをdestruction後、initialization済みの全static var、次にinitialization済みの全static letを、それぞれ全thread共通のinitialization完了順の逆順でdropする。drop前に終了済みへ移す。letのdropが利用できる他のletは、まだ生きているものだけ。

staticのidentityはfield declarationと外側のtype argumentで決め、Originを消去するがSemanticsと未使用引数は残す。開いたkeyは該当する全具体keyと重なる。group内fieldは入れ子struct内もstatic。保存valueはOwnedを要するが、外側のtype argumentすべてにOwnedを課すわけではない。initializerとdropは、同じkeyを共有する全Origin bindingで妥当でなければならない。各accessでは全Originを保持してLoan・lifetimeを検査する。

### 22.5 Abort

Abortは所定のcode・reason・位置を報告する。ユーザーが呼ぶKimi操作内の失敗は、呼出しexpressionの先頭位置。内部allocation・static処理も対応付ける。Kimiから呼ばれたユーザーequals・drop等の失敗はその処理自身の位置。function valueは形成位置ではなく呼出し位置、Dictionary重複は後のkey expression、forの暗黙entryとnextはfor位置、算術検査はexpression（更新なら更新expression全体）、staticアクセスは修飾Place先頭、`$abort`はそのexpression、消去function valueのallocationはconversion位置。

以下のコードには共通接頭辞`KIMI_E_`が付く。reasonの英語文字列は出力contractなので変更しない。

| コード | reason | 意味 |
| --- | --- | --- |
| INT_OVERFLOW | Integer overflow | 整数overflow。符号付き最小value/-1も含む |
| INT_DIV_ZERO | Integer division or remainder by zero | Wrappingを含む整数の0除算・剰余 |
| INT_SHIFT_COUNT | Shift count out of range | countが`0 <= count < bit幅`を満たさない |
| INT_CONVERSION | Integer conversion out of range | 整数conversionの範囲外。float→intのNaN・無限大を含む |
| FLOAT_CONVERSION | Floating conversion out of range | 有限valueがconversion・丸めで無限大になる |
| INDEX_BOUNDS | Index out of bounds | 添字または位置・区間解決の失敗。slice・resolve・insert・removeも含む |
| MISSING_KEY | Dictionary key was not found | キーなし |
| DUPLICATE_KEY | Dictionary literal contains an equivalent key | literalの同値キー重複 |
| ARG_RANGE | Argument out of range | 負のcount/capacity/length、limit<1、逆転区間等 |
| FORMAT | Formatting failed | 整形失敗 |
| REF_COUNT | Reference count limit exceeded | strong/weakのclone・downgrade・upgradeで参照数上限 |
| STATIC_CYCLE | Static initialization cycle | static initialization循環 |
| STATIC_SHUTDOWN | Static storage unavailable during shutdown | 終了中のstatic利用不可 |
| ALLOC_SIZE | Allocation size exceeds limit | allocationサイズ上限 |
| ALLOC | Failed to allocate memory | allocation失敗 |
| FREE | Failed to free memory | deallocation失敗 |
| PROCESS_HEAP | Failed to obtain process heap | process heap retrieval失敗 |
| STDOUT | Failed to write to stdout | 標準出力失敗 |
| STRING_RELEASE | Invalid string release kind | 不正なstring deallocation種別。検出は保証しない |
| CLOCK | Elapsed-time counter failed | counter read失敗、frequency<=0、時計逆行 |
| THREAD | Failed to start worker | worker開始失敗 |
| REQUIRE | 指定reason。既定はTest requirement failed | falseの`$require`。messageは変更せず用いる |
| ABORT | 指定message | 明示Abort |

### 22.6 外部関数

`#LibraryImport("library", "symbol")`をbodyのない関数に付ける。引数は空でなくNULを含まない非補間literal。文字列をそのまま解決に用いる。配置先はgroupまたはrootgroupの直下、またはreceiverを持たないstructのtype関数。

task、receiver、既定引数、可変長引数、specialization、genericは禁止。外側の未使用generic引数も、種類を問わず禁止する。シグネチャ自身のborrow Originは許す。直接呼出しのみ可能で、function valueにはできない。

外部declarationは、refは読取り、uniqは排他的なアクセス、rawは指定条件でのアクセス、valueの妥当性、結果Originを越えるborrow保持禁止、正当なresultを約束する。構築・上書きできるのは再帰的にユーザーdropを持たないvalueだけ。Kimi frameを越える例外unwind・非局所jump・callback・再入は禁止。signatureと-safety:を満たすcallでは、その引数が許すaccessだけを呼出し中に行う。約束違反はUB。unsafeはtypeで表せない呼出し側のobligationを示す。effectには入力経由と環境を含む。引数は左から1度ずつ評価し、marshal、暗黙保持、ownership移譲を行わず、浮動小数点のcontractも保つ。

| 境界のtype | 扱い |
| --- | --- |
| i8〜i64、u8〜u64、f32、f64 | 直接交換 |
| raw/T | pointeeのtypeによらずpointerとして交換 |
| ref/T、uniq/T | TがC交換可能なら非null pointer |
| Option<ref/T>、Option<uniq/T> | 同条件でnullable pointer |
| Unit | result位置でvoid |

C交換可能な保存typeは固定幅整数・浮動小数点・raw、再帰的に適格なborrowとoptional borrow、長さ正の固定配列、全fieldが適格なC struct。string、動的collection、object、function value・closure valueおよびそれらへのborrowには対応表現がなく、Language error。bool、char、isize/usize、128bit整数、tuple、enum、struct/配列のvalue渡し、その他の非適格な参照は、この版ではUnsupported。

### 22.7 Console

`Console.writeLine`はsafe・非generic・taskなし。ref/stringとText.Utf8Sliceのoverloadがあり、暗黙文字列化しない。関数参照の解決には期待typeが必要。元byte列にLFを加えて出力し、正規化せずNULもdataとして扱う。呼出し中だけborrowし所有しない。戻るまでにhostが受け入れるが永続化は保証しない。部分出力後も含め、失敗はSTDOUT Abort。

### 22.8 時間

Timeはallocationを行わず、暦時刻ではなく単調な経過時間を測る。

`Duration`はCopyで、正確なu64のmicrosecondsを保持する。`init(! microseconds: u64)`、rawMicrosecondsを持ち、seconds・milliseconds・microsecondsは、rawMicrosecondsを先にf64へconversionしてから、それぞれ1e6・1e3・1で除す。

`Stopwatch`はNon-CopyでisRunningを持つ。init/resetは時計を読まず、停止・累積0にする。startは停止中だけ開始し、stopは動作中の区間を加算して停止し、停止中なら何もしない。restartは0から開始。elapsedは累積valueと進行中区間の和を返し、状態を変えず、停止中なら時計を読まない。

tick差d、周波数fの区間は`floor(d * 1_000_000 / f)`でmicrosecondsへconversionする。u64に結果が収まる場合、中間演算overflowを起こさない。stopごとに端数を切り捨てる。結果overflowはINT_OVERFLOW、時計失敗はCLOCK。

```kimi
public group Windows
    #LibraryImport("kernel32", "QueryPerformanceCounter")
    public func queryPerformanceCounter(value: uniq/i64) -> i32

    #LibraryImport("kernel32", "QueryPerformanceFrequency")
    public func queryPerformanceFrequency(value: uniq/i64) -> i32
```

Windowsの2関数はsafe、allocation・Abortなし、uniq/i64を受け取りi32を返す。非0なら成功で、borrow先へcounterまたは毎秒countを書き込む。declarationは全対象に存在し、windowsでのみ外部symbolを解決する。

### 22.9 UTF-8とbuffer

length/capacityはisizeのbyte数。BufferWriterとUtf8Formatはユーザーが実装できる静的Contractで、Utf8Formatを唯一の整形Contractとする。format option、locale、runtime format stringは持たない。reserve結果のOriginはselfに依存する。

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        effect confined

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

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

| type | 性質 |
| --- | --- |
| BufferFull、Text.InvalidUtf8 | 状態なし、Copy、public init |
| WriteWindow {source}、Utf8Writer {target}、Text.FixedBuffer {source} | Non-Copy、`Loan<uniq/u8>`を保持しObjectPayloadではない |
| Text.HeapBuffer | Non-Copy、allocationを所有 |
| Text.Utf8Slice | Copy、妥当なUTF-8へのref Loan |

WriteWindow、Utf8Writer、FixedBuffer、HeapBuffer、Utf8Sliceはpublic constructorを持たない。これらのborrow依存はfixed・writer・reserveだけが形成する。view・window・adapterの形成にはallocationをしない。BufferWriter.reserveのeffectはconfined。Utf8Writerはreserve操作だけをtype erasureし、元writerの所有・Move・公開をせずLoanを保つ。外側からの一時borrowとtargetは別で、targetは外側borrowより長く生きる必要がある。

| 操作 | contract |
| --- | --- |
| Text.fixed | initialization済み固定配列を借り、length=0、capacity=N |
| Text.heap | length=0、capacityは要求以上。要求0ならallocation 0。負数はARG_RANGE |
| Text.writer | 元bufferへのadapterを作り、失敗状態をclear |
| Text.utf8 | stringのview |
| Text.validateUtf8 | byte列を1度検証。allocation・copyなし |
| Text.toString | 1度formatして新しいstring。BufferFullならFORMAT。stringの複製にも用いる |
| Text.tryFormat | 事前計測・再試行なしで1度format。結果がdestinationへの排他Loanを保ち、生存中の配列はview経由でのみ読める。失敗時は途中byteが残り得る |

bufferは`0 <= validated <= length <= capacity`を保つ。commit済み領域はinitialization済みで、validatedまでが妥当なUTF-8。bytesはSliceを返す。textは未検証suffixを検証してResult<Utf8Slice,InvalidUtf8>を返すが検証済み長を更新しない。uniqのvalidateは全体成功時だけvalidatedを進める。clearは長さ2つを0にしcapacityを保つ。

FixedBuffer.intoTextはbufferを消費し、外部保存領域への排他依存を持つResult viewを返す。HeapBuffer.intoStringは消費・検証後、allocationをcopyせずstringへ渡す。生byteのcommitはvalidatedを進めない。UTF-8 adapterは予約時にvalidated==lengthならcommit時に検証済み長も進める。全出力がadapter経由なら、後から再走査しない。

UTF-8は正確なUnicode scalar encodingであり、途中切断、過長表現、surrogate、範囲外valueを許さない。正規化・置換をせずNULはdata。Utf8Sliceのlength/bytesは元sourceを参照する。FixedBuffer、WriteWindow、Utf8Writerはdrop declarationを持たず、HeapBufferは1度deallocationする。

#### reserveとWriteWindow

reserve(min)の成功はwritten=0、remaining>=minで、commit済みbyteを変えない。負数はARG_RANGE。FixedBufferは残量不足のときだけBufferFull。HeapBufferはBufferFullを返さず、最大サイズ超過はALLOC_SIZE。拡張後capacityは`max(必要量, 2*旧capacity)`、倍増が最大object sizeを超える場合は必要量とし、commit済みbyteをcopyする。

windowはwrittenとremainingを持ち、先頭から連続して書く。push(u8)、append(Slice<u8>)はResult<Unit,BufferFull>を返し、全部入らなければ変更しない。limitはwindowを消費し、max>=writtenを要求（違反ARG_RANGE）、capacityを元とmaxの小さい方へ制限する。commitはwindowを消費して書込みprefixを確定し、その長さを返す。commitせずdropすれば何も変更しない。

ユーザーBufferWriterは標準windowを転送・制限する。adapterは返されたwindowのwritten=0・remaining>=minを検査し、違反ならwindowをdropしてBufferFullを記録する。

### 22.10 整形・補間・allocation保証

`Utf8Writer.write<T>(self: uniq/Self, value: ref/T) -> Result<(),BufferFull>`はTにUtf8Formatを要求し、`status(self: ref/Self)`は同じResultを返す。既に失敗していればformatせずその失敗を返す。そうでなければ1度formatし、format自身または入れ子のwriteが返す最初のBufferFullを記録して返す。statusは記録済みの失敗を返す。ユーザーformatterがErrを隠しても状態は戻らず、adapterにresetはない。利用を終え、必要ならbufferをclearして新しいadapterを作る。commit済みbyteは巻き戻さない。formatの直接呼出しにはこの保護がない。

formatは入力をborrowし、出力に入力・writerへの依存を残さない。同じvalueと外部状態の完全な表現をdestinationによらず生成し、容量不足で省略表現を出さない。ユーザーformatterは1度だけ実行し、そのeffectを保つ。組込みformatterは出力量を正確に計算してreserveを1度行い、空出力なら予約せず、妥当なUTF-8を全てcommitする。

| type | 標準表現 |
| --- | --- |
| 整数・Wrapping | ASCII十進、余分な先頭0なし、負数だけ`-` |
| bool、char、Unit | `true`/`false`、scalarのUTF-8、`()` |
| string、Utf8Slice | 元のbyte列 |
| FromEnd、Start、End | `^offset`、空、空 |
| Range、ClosedRange、ResolvedRange | `start..end`、`start..=end`、`start..end` |
| f32、f64 | 同じ幅へ最近接偶数丸めで戻る最短の有効数字。候補同長なら元valueに近く、なお同じなら末尾偶数 |

floatは正規化指数eが`-4 <= e < 16`なら固定小数、それ以外は小文字eの指数表記。指数の`+`と余分な0、小数部末尾の0・不要な末尾小数点を付けず、小数点は`.`としlocaleに依存しない。特殊valueは`0`、`-0`、`Infinity`、`-Infinity`、`NaN`。NaNの符号とpayloadは表示しない。borrowそのものへの自動conformanceはなく、引数Adaptationが働く。handle、raw、tuple、配列、ユーザーtypeに自動formatterを付けない。

補間は1つのadapterを共有し、左から各expressionを1度評価して、そのvalueの書込みを終えてから次へ進む。所有Place・temporary valueはborrowし、refは渡し、uniqはshared reborrowする。裸のexpressionはMoveせず、`@move`は一時所有valueを作る。最初の失敗でFORMAT、成功で所有stringを返す。通常の補間stringをwriteへ渡す場合、そのstringの生成は先に行われる。

`$tryWrite(writer, literal)`はResult<Unit,BufferFull>を返す組込みで、function valueにはできない。中間stringを作らず、writerを1度評価してuniq borrowした後、通常またはrawの文字列literal（補間可）を処理する。第1引数は通常の引数規則に従い、裸のownerには`@uniq`が必要。既存または最初の失敗後は後続補間expressionを評価しないが、静的検査は行う。補間expressionは、borrow中のwriter/bufferを利用できない。temporary valueと移譲には通常規則を適用する。

出力量が有界な組込みtypeは整数、Wrapping、bool、char、Unit、float。その他は非有界。FixedBufferまたは十分なHeapBufferへの組込み整形はallocation 0。全体が検証済みならtext、validate、intoText、intoStringの再走査は0。

補間がすべて有界type、または非有界部分が1つのstring/Utf8Sliceだけでその前がliteralのみなら、専用bufferのheap allocationは最大1回。組込み整形が出力全体を1回予約するtoStringもallocation最大1回。事前走査・formatの再実行は禁止。この数には結果stringに残る専用bufferを含み、補間expressionやユーザーformatter自身のallocationは含めない。他の場合はimplementation-defined。allocationの除去・統合・遅延・stack化はallocation失敗を消してよいが、他の結果、ユーザーeffect、評価順・評価回数は保つ。

<a id="s23"></a>
## 23. コンパイラーサービス

diagnostic、check、language server、任意のCSPは、言語の有効性を変えない。editor diagnosticは助言であり、build結果を左右しない。

### 23.1 diagnosticとrepair candidate

diagnosticにはcode、severity（Error/Warning/Information/Hint）、categoryがあり、各codeのseverityとcategoryは固定。仕様が名前を定めないcode・message・factはimplementation-defined。

| category | 意味 |
| --- | --- |
| Language | 言語規則違反 |
| Proof | 必要な事実をproofできない |
| Unsupported | 有効だが未実装 |
| Input | 入力・設定・依存内容・lockを確立できない、または不正 |
| Resource | implementation-definedの資源上限。プログラム自体は有効かもしれない |
| Internal | コンパイラーの欠陥 |

Warningは有効だが疑わしい形を示す。Warning/Information/Hintで拒否しない。有効な未実装形は、その位置でErrorの`Unsupported_Kd`を報告し、Languageにしない。特定の具体typeだけ未対応なら、その具体利用・実体化で報告し、conformanceやgeneric definition検査を変えない。資源超過をLanguage/Proofへ置き換えない。

独立した各失敗は報告する。同じ失敗の複数Semantics case・実体化をまとめる方法、他の失敗だけから派生したdiagnosticを追加するかはimplementation-definedだが、受理を変えてはならない。diagnostic位置はsource全体、source内span、またはsource外。人向け位置は§2、protocolは各contractの単位を使う。

repair candidateは、検査が読んだソースへの構造化編集の選択肢。検証済み条件と未決条件を分け、未決を保証済みにしない。適用後は別の入力であり、再検査して初めて妥当性が分かる。kind・conditionは後掲schemaの閉じた列挙、どの候補を提供するかはimplementation-defined。

### 23.2 検査単位と受理

check unitはProject・target・Product/Test・Debugの組。Productは同入力の`kimi check`、Testはtest準備と同じ結果になる。Productの事実はproduct入力だけから得るため、Product成功とTest失敗は両立する。検査はproject、直下のkimi一覧、ソース、lock、依存内容を読む。

| outcome | 意味 |
| --- | --- |
| Completed | 検査を完了。エラーの有無とは別 |
| Blocked | 入力・設定を確立できず、Input Errorで説明 |
| Faulted | 内部失敗をInternal Errorで説明 |

Completedは検査対象集合の全Language/Proof errorを報告する。具体実体のlayout・表現obligationも含む。acceptedはCompletedかつErrorなしの場合だけ。拒否結果にはErrorが1つ以上ある。cancellationされたunitは結果を出さない。

同じ入力・設定をcheckが受理したなら、後のemit/build/runでLanguage/Proof errorを出してはならない。Resource、targetのUnsupported、native library・toolchain・出力・起動失敗は残り得る。target固有のUnsupportedをcheckでも調べるかはimplementation-definedで、調べるなら同じ検査対象集合を使う。

#### 複数入力のcheck

`kimi check`は空でないProject/暗黙sourceパス集合を選び、各パスにProduct unitを1つ作る。選択不能・空集合はBlocked/Input、選択の内部失敗はFaulted/Internalで、どちらもunitsは空。

パスは絶対化し、`.`・`..`を除き、`/`区切りにする。hostの比較規則で重複する場合、Unicode scalar順で最小の綴りを残し、その順でsortする。この順序のためにsymbolic linkを解決しない。結果も実行順によらずこの順に並べる。

選択後は、1件がBlocked/Faultedでも全unitを検査する。各結果が独自のoutcome、accepted、source表、diagnosticを持つ。targetを確定できなければ`--Target`value、未指定なら空文字列。

command全体は、Faultedが1つでもあればFaulted、なければBlockedがあればBlocked、それ以外はCompleted。選択成功かつ全unit acceptedの場合だけ全体もaccepted。root diagnosticは選択失敗だけで、unit diagnosticを複製しない。Completedでも拒否され得る。

全unit終了後にJSONを一括公開する。公開前のcancellationは全結果を捨て、JSONなし・終了code130。公開開始後はcancellationを観測せず最後まで書き、command結果の終了codeを返す。

### 23.3 JSON出力

`kimi check --Format json`は標準出力に`kimi.check/3`のJSON文書を1つだけ書き、その他は標準errorへ送る。単一Projectでもunits配列を持つ。rootはschema・compiler・outcome・accepted・sources・diagnostics・unitsを持ち、選択成功時のroot sources/diagnosticsは空。

source indexは、そのdiagnostic・関連位置・修復を含む結果自身の表を参照し、別unitを参照しない。spanのoffset/lengthは読み取った文字列のUTF-16 code unit、rangeは0始まりの行・UTF-16文字位置、endは含まない。field/enumeration valueの削除・改名・意味変更はschema版を上げる。任意field・enumeration valueの追加だけなら版は維持する。

後掲の[JSON schema](#check-schema)を本章の一部とする。表示用の省略・抜粋を意味判定に用いない。

### 23.4 Language server

`kimi lsp`は標準入出力でLSPを提供する。check diagnosticをUTF-16位置で公開し、repair candidateのquick fixとHoverを提供してよい。他の機能・設定・実行時期はimplementation-defined。ファイル書込み、package restore、コード実行は行わない。

開いた文書の内容をdiskの代わりに使い、同unit・同入力のcheckまたはtest準備と同じdiagnosticを返す。対象unit、検査・結果撤去の時期はimplementation-defined。文書はそれをproduct sourceまたはTestSourcesに含むProjectに属し、その探索方法と暗黙Projectを作る条件はimplementation-defined。

### 23.5 Compiler Server Protocol

CSPはAI agentを含むprogram向けの任意の構造化interface。通信形式はimplementation-definedだが、提供するなら次を満たす。

- 全source snapshotを永続的な内容identityで識別し、編集・結果・証拠の根拠を特定する。
- 編集は作成元snapshotだけに適用し、reviewできるsource変更を返す。黙って書き込まない。未決条件は編集後の再検査で確立する。
- §23.3の形でdiagnosticを返す。
- 構文node（除外構文と最も内側の除外指示を含む）と、選択構文の意味nodeに安定したhandleを付ける。
- 検査・test・計測を正確なsourceと設定へ結び付け、結果と残る不確実性を示す。
- unsafe blockとその操作、unsafe call、LibraryImportの約束・obligationを列挙する。suspension点はtask slotと、suspensionを越えて生きるLoan・所有valueを示す。
- 言語規則を狭めない。

<a id="s24"></a>
## 24. suspensionと非同期task

同じtask treeは1つのthreadで実行する。§22.3の構造化workerは別threadで通常の計算を行う。本章の違反はLanguage error。

### 24.1 task・frame・suspension点

taskはAsync.runが開始したroot、またはKimi操作が開始した子の実行。rootは新しいtreeを作り、子は親と同じtreeに属する。taskとその子孫がsubtree。

task frameは、task slotを持つ関数の1呼出しについて、開始から終了までの状態をsuspension中も含めて表す。内側frameのsuspensionで、その呼出しを実行中の外側frameも全てsuspensionする。frameはvalueではなく、Move・Copy・保存できない。§15の呼出しactivationとも別。suspensionできる場所はtask callだけ。

### 24.2 task slot

関数ヘッダー・Function Type・Callableの引数一覧の先頭に`task;`を書く。receiver、通常引数、`!`より前であり、空引数でもsemicolonを省かない。taskはparameterでも名前bindingでもなく、valueとして参照できない。

```kimi
func pause(task; ! delay: Time.Duration) -> ()
    _ = Async.sleep(task; delay)

func apply<F>(task; action: F) -> ()
    F is Callable<(task;) -> ()>
    action(task;)
```

名前付き関数、requirement、specialization、無名関数、Function Type、Callableに書ける。constructor、外部関数、main、Test、accessor、drop、attribute、`$`操作、Case/Patternにtask slotはない。無名関数はヘッダーに明記したときだけ持ち、期待signatureから補わない。通常parameterのtype inferenceはtask slotの後を対応させる。overrideと完全specializationはoriginalと有無を一致させる。

呼出しは引数先頭の`task;`で、最も内側の関数のtaskを渡す。選択calleeにslotがある場合は必ず書き、ない場合は書けない。最も内側の関数にslotがない場所、既定引数expression、登録するdefer bodyの直下には書けない。そこに置いた無名関数は別の関数境界を持ち、自身のslotを渡せる。後始末はsuspensionしない。slotを要求するcalleeに`foo(task, x)`や`foo(task)`と書き、taskが未binding名なら、task引数欠落として扱う。

slotはparameterの位置番号、外部名、acquisition mode、matching key、引数、Originを持たず、N/Kの数やreceiver番号を変えない。Origin省略の入力にもならない。

同じfunction groupは全declarationがslotを持つか、全declarationが持たないかに統一する。declaration scopeのgroupはdeclaration時、利用時に集めるgroupはその利用時に検査する。同groupのoverloadで混在できない。slotだけが異なるdeclarationは重複であり、slotはoverloadの区別に使わない。

実装対応、specialization対象、Callable互換、Function conversion、既知・固定signatureの照合、Function Type identityのすべてでslotの有無を一致させる。実際にはsuspensionしない実装も、slot付きrequirementにconformanceするにはslotをdeclarationする。

### 24.3 task callとLoan

`task;`で始まる呼出しがtask callである。通常のcallを実行中は、そのcallより前に開始した同treeの他taskを進めない。callee内のAsync.runは別treeとしてそのcall内で動き、effectもcalleeのeffectとして比較する。Kimiが開始する子も、その構造化task call内で動く。

呼出し元bodyでtask callは、未知の環境effectを持つcallが競合する**全ての生きたstatic Loan**と競合する。callee value・receiver・引数が保持または依存するLoanも含み、confined等の公開effectで除外できない。この比較をcalleeのeffect要約へ加えるわけではない。

局所value・parameterのLoanはこの一律比較の対象外で、suspensionを越えて保持できる。他taskは子へ渡したLoan、static、共有アクセスだけを許すrc/arc payload、保証されたKimi内部保存領域を経由して状態へ届く。staticを借りるparameterは、それを渡すtask call、またはcaptureしたrootを渡すrunの呼出し側で検査済みである。

<a id="app-a"></a>
## 付録A. コンパイラーrequirement

準拠実装は言語規則に加えて以下を満たす。資源上限のResource diagnostic以外に、新しい言語上の失敗を追加しない。

| 項目 | requirement |
| --- | --- |
| 決定性 | source・Project設定・target・mode・compilerが同じなら、受理、diagnostic categoryと位置、Abort位置を含む動作が同じ。読込み・検査・生成・並列処理の順序で変えない。未完成headerを完成済みとして扱わない |
| 再利用 | 現入力の完全解析と同じ結果になる場合だけ、以前の結果を再利用できる。Unknown・Error・未検証は成功の証拠にならない |
| 受理前検査 | 到達可能・不可能な全body、全Semantics case、選択された全sourceで、言語が要求する全検査に成功してから受理する。Test buildはtest sourceも含む。Unsupportedは検査対象集合のコード生成前に報告する。除外構文にもsource検査を行う |
| 上限 | 深い・大きい入力でもhost stackを使い果たさず、深さ・memory上限はResourceにする。Language errorや受理へ置き換えない。初期実装のSemantics caseはdefinitionごと最大64 |
| 最適化 | Debug/Release、code共有、specialization、LTOも通常の最適化と同じ意味保存条件に従う。LLVMのinbounds・nonnull・alias等の仮定は言語がpremiseを保証するときだけ付ける |
| source writer | bodyの形、grouping、移譲先、文書コメントの対応を保ち、意味の同じsourceを出す |

<a id="app-d"></a>
## 付録D. この版で保留する機能

以下はこの版の範囲外であり、構文の利用許可を与えない。実装済みかどうかは別途STATUSで管理する。

| 分野 | 保留範囲 |
| --- | --- |
| value・object（§3・13） | Weakの直接view conversion、生存だけの確認、unsafe weak pointer、unowned参照。追加・暗黙の通常基底conversion。Runtime Contract Viewの指定・綴り・associated type binding・検査・cast、consuming/generic/type関数のruntime requirement。object/raw pair層のfollow。回復可能なobject生成、allocator選択、一般的なvalue複製 |
| 配列・区間（§4） | 要素generatorによる固定配列生成、string添字。降順・step付き・無限・始点のみの反復、実行時方向、user-defined Position/Range、contains等、長さを持つユーザーtypeの位置正規化、既定Contract実装、runtime range view。UniqSliceの反復conformance・専用分割・追加try操作・任意pointerからの公開生成。追加の動的collection API |
| 反復（§22） | Cursor/UniqCursorとshared/uniq adapter。現行の列挙はLendingIterator/Iteratorとowning/borrowingを使う |
| declaration（§6） | extensionとidentity、Contract断片・分割。abstract関数、virtual property/accessor、関数自身にgenericを持つvirtual、final/newslot、binding済みbase function value。Layout/LibraryImport/Test以外の一般attribute意味論 |
| Callable（§3・7・15） | Borrowed/Exclusive/Consumingのtype erasure Callable、opaque result、receiver依存・lending公開result、non-escaping declaration、captureのalias・initializer・部分指定、generic receiver Semantics・environment、一般的な高階量化contract |
| generic・Contract（§4・8・10） | Contract自身のOrigin/length parameter、既定実装、外部conformance、修飾requirement call。有界なdeclaration結果を越えるassociated type inference、identityの分解を含む強い記号inference。関数length以外のvalue引数、定数type argument、独立Semantics slot、部分・既定・可変長generic引数。部分/条件付きspecializationと優先度、Container specialization。direct fitとCallable消去のoverload順位 |
| その他のdeclaration（§12・13・18） | 既発行packageとのObjectCallCompatible比較、透過的なsource type alias、testを含むComposition RootのEntry/Provider declarationと選択 |
| enum・pattern・失敗（§6・14・17） | struct、名前付きpayload、type、OR、Range、Rest、配列pattern。guard候補capture、bindingごとのacquisition指定。その他のbinding構文での反駁可能pattern。公開non-exhaustive enum、空enum、enum表現/ABI保証。ユーザーtry支援、try block |
| ownership・Origin・effect（§8・15・16） | 追加の動的Move Path。non-static依存を隠すtype erasure view、static PlaceのOrigin、現行Callable入力量化・associated type以外の高階Origin。destruction時のlifetime緩和。非virtual具体関数・Property requirement・Function Type・open structのdropへのeffect bound、confined/preserves results以外の語彙、一般effect系 |
| 並行性（§15・22） | source thread、切離した仕事、atomic順序API、追加のthread移譲・共有能力、外部からの並行再入。thread別I/O executor、thread間channel、必須I/O API。thread間のatomic全体更新、arc payloadの同期変更、blocking外部callのoffload |
| unsafe・外部・runtime（§5・6・21・22） | unsafe Function Type、unsafe/外部function value、外部関数のeffect declaration、並行raw pointer、tryAllocate、生byteからのstring、共変な所有container。C集約value渡し、export、callback、varargs。Kimigayo/C以外のlayout。過剰alignment allocation、任意終了code、float環境制御 |
| package・Mod・test（§6・17・18） | source非公開のbinary配布、安定した外部ABI、再export。network registry、版範囲、依存辺設定override、Package→Project置換。Mod生成・host interface・marker登録・設定。parameter付きtest、Suite attribute、skip、資源lock、Option/Result検証helper、string/collection差分、添付、期待Abort、compile失敗test、coverage |

<a id="app-e"></a>
## 付録E. 用語

読み方の補助。正確なdefinitionと条件は各章にある。綴りを保った語は、本書全体で同じ概念を指す。

| 用語 | 簡潔な意味 | 章 |
| --- | --- | --- |
| Type / Core / Semantics | 完全type／所有・borrow・Origin以外のvalue構造とidentity／表現・所有・borrow・アクセスの規則 | 3 |
| Scalar / numeric | 整数・float・bool・char・Wrapping／整数・float・Wrapping | 3 |
| built-in numeric Type | 数値type、PrimitiveIntegerをproofしたtype parameter、そのWrapping | 13 |
| Place / Storage | typeと能力を伴う選択済み保存場所／valueを置く記憶域 | 3 |
| Access path | 非raw PlaceへのDirect・共有参照経由・排他参照経由の到達方法。rawにはない | 3 |
| Value kind | borrow value、所有Place、所有temporary valueの別 | 3 |
| Read / Write / Take | 観察／initialization・置換／valueの取出し能力 | 3 |
| Borrow value | 外側Semanticsがref/uniq/objref/objuniqのvalue | 3 |
| Bare acquisition | `@`を書かないacquisition | 3 |
| Copy / Move | 元をinitialization済みのまま複製／valueとdestruction責任の移譲 | 3 |
| Read Type / Value read | Scalar・位置・区間type／そのvalueが必要な場所で安全なref/uniq層の終端valueを暗黙Copyすること | 3 |
| Temporary Value / Place | expressionの一時結果／その実体化された保存場所 | 3 |
| Lending point / Loan anchor | borrow・reborrow・projectionが指す保存場所 | 3 |
| Reference-path selection | 安全な参照層をtypeだけで順に調べる選択 | 3 |
| View Target / Dynamic Type | object viewの公開対象／実際の生成Core | 3 |
| Position / Range | 長さLに対し[0,L]へ解決する境界／位置間の区間 | 4 |
| Indexable / UniqIndexable | 添字から共有／排他要素Placeを選ぶContract | 4 |
| UniqSlice | Non-Copyの排他連続view | 4 |
| noinit | 初期storeを省くunsafeな固定配列生成 | 4 |
| raw pointer / raw Place | raw/Tのアドレス／`*p`、`p[n]`とその部分 | 5 |
| Unsafe context / Block | unsafe操作を行う許可／その許可を字句的に与えるunsafe block | 5・14 |
| Declaration Container | 種類に応じたmemberを置ける名前付きdeclaration scope | 6 |
| Case / Payload | enumの選択肢／その位置付きdata | 6 |
| Virtual slot / Override | binding済みdeclaration identityを持つoriginal公開contract／それを実装する派生body。overrideはlookup層やoverloadを追加しない | 6 |
| Direct base call | `base.f(...)`による直接基底側の実装選択 | 6 |
| Acquisition mode / shape | 裸の所有Place引数に要求するByValue/Shared/Exclusive／同groupの重なるparameterで統一するacquisition形 | 7 |
| Receiver shape / Expression | receiver typeのSemantics／method・accessor・function value callのreceiver expression | 7・3 |
| Closure / Capture | 環境付きCallable body／環境bindingをlet/var initialization同様に作る各項目 | 7 |
| Call Receiver Requirement | Callable bodyがreceiverに要求する最小アクセス | 7 |
| Function Item / common Function Type | 環境を持たない解決済み関数／Callable valueを共通化するtype erasureされた呼出しcontract | 3 |
| Place result | 既存Storageを公開する`place ref/T`、`place uniq/T` | 7 |
| Pair binder / target / layer | `<s/T>`のs／T／外側Semanticsがpair binderであるtypeの層 | 8・13 |
| Admitted set / Semantics case | premiseが許すpair Semantics集合／各binderから1つずつ選ぶgeneric検査case | 8 |
| Contract / Constraint | 実装や保存領域のないrequirement Container／type・Semanticsへの条件 | 8 |
| Constraint Clause | `subject is requirement`の節 | 8 |
| Conformance / Identity | typeのContract conformance／完全typeとbinding済みContract参照の組 | 8 |
| Conditional Conformance / Member | 条件がProvenのとき利用できるconformance／所属type parameterを条件付けるmember | 8・7 |
| Associated Type | 各conformanceが確定するtype。Origin parameterを持てる | 8 |
| Origin parameter | `LentItem(step)`等のassociated typeのOrigin binder | 8 |
| Compiler-intrinsic Contract | コンパイラーがconformanceを定めるCopy、Owned、Callable、Sealed、ObjectPayload、PrimitiveInteger、Transferable | 8・15・22 |
| Closed Contract | conformance type集合をKimiが固定するPosition・PositionRange | 8 |
| Object Target / ObjectPayload | object形を作れるtype／valueをobjectのpayloadにできる能力 | 8 |
| Sealed | Never・open struct以外のowner Coreである証拠 | 8 |
| PrimitiveInteger | 12整数typeだけの組込みContract | 8 |
| ObjectViewCompatible | objectのView Targetに利用できるContract | 8 |
| Callable Constraint | generic valueの呼出し方を定めるconstraint | 8 |
| Effect bound | requirement・Callable・virtual slotが公開するアクセス保証 | 8 |
| confined | 環境effect・未分類effectなし | 8 |
| preserves results | 同じrequirementの以前の結果Loanと、次のcallが競合しない保証 | 8 |
| Common adaptation | 固定期待typeの位置で選ばれる1つのAdaptation操作 | 10 |
| Fixed expected call signature | common Function、または未binding FのCallable constraintがF/ref/F/uniq/Fへ与える唯一のsignature | 10 |
| Independently typable argument | 呼出しからの期待やliteral既定typeなしで完全typeが決まる非literal引数。無名関数は含まない | 10 |
| Literal argument / literal-only expression | 未typing literal、それだけのexpression・結果源／指定演算だけで組んだ未typing数値・位置・区間expression。slot inferenceの証拠にしない | 10・12 |
| Waiting argument | Literalでも独立typing可能でもなく、固定signature等を待つ引数。外側slotのinference証拠にしない | 10 |
| Property / Field | 保存let/varまたはcomputed member／その保存領域 | 11 |
| ObjectCallCompatible | object borrowをreceiverとするcallがreceiverを保存する保証 | 12 |
| Adaptation Target | `@`が要求するSemantics/Core、内側type、またはView Target | 13 |
| Follow / Reborrow | 参照層・適格pair層・Sealedなobjectの指すPlace選択／既存borrowからのreborrow | 13 |
| User provider Type | 外側ownerで組込み数値typeではないstruct/enum。算術実装を提供する | 13 |
| Composition Root | `$abort`、`$tryWrite`、`$expect`、`$require`のrootである`$` | 13 |
| Value / Discard Context | expressionの結果を使う／保持しない文脈 | 14 |
| Effective Type | refinement後、その地点で保証される最も派生したCoreのtype | 14 |
| Complete value | 構築完了済みで、現在も全体が完全なvalue | 15 |
| Consume | アクセス操作のEligibility・Legality静的検査 | 15 |
| Lending rule / Movable Place | 移譲・新規exclusive borrowを明記すべき条件／Moveで取出せるPlace | 15 |
| Move Path / Partial Move | 独自のinitialization状態を追跡する経路／集約valueの部分Move | 15 |
| Subject Place / mode | match/forのsubjectからinitializationするPlace／与えるアクセス形 | 15 |
| Origin / Region | 記号的なlifetime／program pointの集合。region(o)がoの意味 | 15 |
| Fixed / Finite Origin | 固定した領域のOrigin／上限内で用途から領域をinferenceするOrigin | 15 |
| Binding set / Origin projection | typeのOrigin slotへのbinding写像／その1slotの選択 | 15 |
| Origin relation | Origin間に要求する`==`またはoutlives | 15 |
| Owned | non-static Originへ依存しないこと。owner Semanticsとは別 | 15 |
| Loan / Loan<T> | borrow Placeとref/uniqアクセス。保持valueが利用され得る間生きる／保存領域なしでborrow依存を表すサイズ0のstruct | 15 |
| Region splitting | 親Loanから複数の子を作ること。排他の子同士は重ならない | 15 |
| Target completion | runtime test/cast対象へ補うOrigin binding | 13 |
| Deferred Block | scope終了用にdeferが登録する処理 | 16 |
| Kotonoha / SourceDocument | 1つの名前付きmodule／そのsource入力1つ | 18 |
| Project root / Compilation root | declaration階層の最上位／root declaration・直接依存の参照名・Kimiを持つ環境 | 18 |
| Kimi Kotonoha | 必須標準declarationを提供する基盤module | 22 |
| Condition / Excluded syntax | 組込み条件・Project設定によるbool指示expression／非選択でsource検査だけを受ける構文 | 19 |
| Item / LentItem(step) | Iteratorの要素type／receiverをborrowし得るLendingIteratorの要素type | 22 |
| Iterator / LendingIterator | 過去の要素を次のnext後も保持できる／要素がiteratorをborrowし得るiterator | 22 |
| Iterable / UniqIterable / IntoIterable | Shared/Exclusive/ByValue subject用のfor入口 | 22 |
| Conflicting access / Data race | 重なる記憶域への少なくとも一方が書込みで両方とも実装によるatomic accessではないアクセス／異threadで順序関係がない競合。UB | 22 |
| Diagnostic / Repair candidate | severityとcategoryを持つ報告／diagnosticに付けられる任意の構造化編集候補 | 23 |
| Task / tree / frame | rootまたは子の実行／rootと子孫／task関数1呼出しの状態 | 24 |
| Task slot / call | 引数一覧先頭のtask枠／`task;`を渡すcall。唯一のsuspension点 | 24 |
| Static Loan | static保存領域に根を持つLoan | 24 |
| Undefined behavior | unsafeの約束違反により動作が規定されなくなること | 1・5 |

<a id="app-f"></a>
## 付録F. 形式文法

本付録が唯一の規範文法であり、token境界、keyword、記号、layout、生成規則、優先順位を定める。意味と文脈に依存するconstraintは各章に従う。`? 説明 ?`は指定節でdefinitionする要素。非終端名は原文と同じものを用いる。

### F.1 記法

引用符内は実際の構文。`|`は選択、括弧はgroup、`?`・`*`・`+`は省略可・0回以上・1回以上。`List<X>`は`X ("," X)*`、`TrailingList<X>`は末尾commaを任意に許すList。ここでの山括弧は文法記法で、引用した`"<"`・`">"`だけがtoken。

NEWLINE/INDENT/DEDENTはlayout event。引用されたNameはその綴りのNameに一致し、keywordかどうかはF.3の文脈で決める。生成規則末尾の`//`も規則へのconstraintである。

### F.2 字句

sourceはName、keyword、literal、記号の列。encoding・空白・コメント・literalの意味は§2。

```ebnf
PhysicalNewline := LF | CR LF | CR
LineComment     := "//" ? PhysicalNewlineまたはEOFまでの文字列、 §2.2 ?
BlockComment    := "/*" ? 最初の"*/"までの文字列、 §2.2 ? "*/"
Token           := Name | Keyword | NumberLiteral | CharLiteral | StringLiteral | Punctuator | "_"
Keyword         := ? 予約語（§F.3.1）または当該文脈のkeyword（§F.3.2） ?
Punctuator      := "(" | ")" | "[" | "]" | "{" | "}" | "," | ";" | "." | ":" | "::"
                 | "->" | "=>" | "@" | "#" | "$" | "?" | "!"
                 | "+" | "-" | "*" | "/" | "%" | "++" | "--"
                 | "=" | "+=" | "-=" | "*=" | "/=" | "%="
                 | "==" | "!=" | "<" | "<=" | ">" | ">="
                 | "&" | "|" | "^" | "<<" | ">>" | "&=" | "|=" | "^=" | "<<=" | ">>="
                 | ".." | "..=" | "&&" | "||"
```

コメント・literal外の記号は最長一致で読む。例えば`a+++b`はa、++、+、b。列挙したtoken列にできなければエラー。genericの閉じ位置に`>`で始まるtokenが来たら、先頭の`>`だけを閉じ記号に使い、残りを再字句解析する。expressionのshift・比較には影響しない。

`$`と`#`は後の名前から独立したtoken。`;`はtask slot/引数、`!`は引数境界、`{}`はOriginにだけ使う。`&&`・`||`はエラーとして識別するためのtokenで、常に禁止。論理演算はand/or。

```ebnf
Name         := NameStart NameContinue*   // 予約語でも"_"でもない
NameStart    := "A".."Z" | "a".."z" | "_"
              | ? Unicode一般カテゴリLu/Ll/Lt/Lm/Lo/Nlの文字、 §2.5 ?
NameContinue := NameStart | "0".."9"
              | ? Unicode一般カテゴリMn/Mc/Nd/Pcの文字、 §2.5 ?
```

keywordはName全体が一致するときだけ認識する。単独`_`はNameではない。NFC、除外文字、名前のidentityと`_`の配置は§2。

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
TupleIndex      := DecimalDigit+   // member"."の後だけ。間の空白・コメントは可
```

literalに符号は含まれず、先頭+/-は演算子。`.`は直後が十進数字のときだけ数値に含む。直後のName継続文字は一つの不正tokenへ取り込み、`0xg`・`0b102`・`123abc`を拒否する。member`.`直後の数字は、空白・コメントを除いてTupleIndexとして読み、小数・指数・prefix・区切りを許さない。`pair.0.1`は2段の添字、`pair.1_2`は不正。

```ebnf
CharacterEscape := "\0" | "\\" | "\e" | "\t" | "\n" | "\r" | '\"' | "\'"
                 | "\u(" HexDigit HexDigit? HexDigit? HexDigit? HexDigit? HexDigit? ")"
CharLiteral     := "'" (DirectScalar | CharacterEscape) "'"
DirectScalar    := ? 除外規則に反しないUnicode scalar 1つ： §2.8 ?
StringLiteral   := EscapedString | RawString
EscapedString   := '"' (StringText | CharacterEscape | Interpolation)* '"'
StringText      := ? 次を除くUnicode scalar： '"'および"\", §2.9 ?
Interpolation   := "\(" Expression ")"
RawString       := QuoteRun RawText QuoteRun
QuoteRun        := ? N>=3個の二重引用符の連続。選択規則： §2.9の生文字列規則 ?
RawText         := ? 終端引用符列までの内容、 §2.9の生文字列規則 ?
Literal         := NumberLiteral | CharLiteral | StringLiteral
                 | "true" | "false" | "null" | "(" ")"
```

### F.3 keyword

予約語はNameにならない。文脈keywordは表の位置だけでkeywordになり、それ以外ではName。判定にname resolutionやtypeを使わない。NeverとTransferableは通常のNameで、組込みを確実に指すには`::Kimi.Never`、`::Kimi.Transferable`を使う。

#### F.3.1 予約語

| 予約語の分類 | 綴り |
| --- | --- |
| primitive type | isize usize i8 i16 i32 i64 i128 u8 u16 u32 u64 u128 f32 f64 bool char string |
| binding・関数 | let var func |
| 制御・検査・literal | if else case for while loop do match return exit continue yield try require defer is not and or true false null |
| access | public internal private protected |
| 専用形 | Self init drop base |
| コンパイル時 | switch（#の後だけ。runtime switchなし） |
| 将来用 | as |

#### F.3.2 文脈keyword

以下が文脈keywordの全一覧。

| 語 | keywordになる位置 |
| --- | --- |
| alias rootgroup group struct enum contract computed property | declaration導入。extensionも同位置で認識するが未提供として拒否 |
| unsafe virtual override abstract open | declaration修飾。unsafeは実行項目の先頭で、直後が=>または行末の場合も該当 |
| label | Primary先頭で、同じ物理行にNameと`:`が続くとき |
| in / to | forのbinding後／exit・continue・yield直後 |
| task | parameter/argument/FunctionParametersの`(`直後で、次tokenが`;` |
| self | ReceiverShorthand・AccessorReceiver。self/value/storageは§9・11の文脈bindingでもある |
| get set has value | accessor・Property requirement構文 |
| associate when specialize | associated type declaration/指定、条件付きconformance、func直前のspecialization |
| place | result位置でrefまたはuniqと`/`が続く場合 |
| Callable | RequirementAtom/ConformanceAtom先頭で`<`が続く場合 |
| effect confined preserves results | 項目先頭のeffectに同一行でconfined/preserves、またはContract内でContractSelectorが続く場合。他の3語はeffect後だけ。配置は§8のeffect declaration領域 |
| during outlives static | AnnotatedType本体と`?`suffix後／OriginRelation内／OriginAtom |
| origin | OriginRelationを許す項目先頭。付属節一覧、struct/enum、requirement constraint領域、関数・constructor・accessor先頭領域。`origin (...) == ...`も常にrelation |
| owner ref uniq obj rc arc objref objuniq raw | type・requirement・Place result・CallableReceiver・capture・Adaptation先のSemantics位置 |
| value valueborrow object objectborrow borrow owning reference | requirementのSemanticsCategory |
| move copy follow wrap bits | @直後。wrap/bitsは空白・コメントなしで`<`が直続する場合だけ |
| abort tryWrite expect require message | $後の操作名は予約語でも綴りで照合。messageは$expect/$requireの引数label |
| noinit | `=`後の括弧なしInitializer全体。そこではNameにならず§4の条件だけで有効 |
| of / length | `[N of T]`・`[N of value]`のArrayLength後／GenericParameter先頭 |

先頭領域でoriginという関数をexpressionとして呼ぶなら`(origin(b) == a)`のようにgroup化する。

declaration headerの順序はattribute、access、修飾子最大1つ、導入語。funcの前はunsafe/virtual/override、structの前はopen。abstractは同位置で認識して拒否する。候補語の後から導入語まで同一行にaccess語・修飾語だけが続くと修飾として認識し、不正順序・重複・複数・不適切なdeclarationへの修飾はエラー。他の位置では通常名で、`func virtual()`等は有効。

### F.4 layout

字下げ1段はU+0020を4個。コードの字下げは4の倍数。コメント・literal body外のtokenを開始/継続する行が有効行で、空行・コメント行はeventを作らずbodyや分岐接続を終えない。複数行literalの補間外は字下げを検査しない。

補間の`\(`はdelimiterを開く。所属levelはstring開始有効行の字下げで、`\(`と同じ行で始まる構文もそれをheader基準に使う。補間の後続行は通常の有効行として検査する。

header基準はheader開始物理行の字下げで、keyword列・label列・継続行ではない。bodyは基準+1段、match arm一覧はmatch+1段、arm bodyはarm+1段。`=> Item`の=>とItem開始はheader終了物理行に置く。=>の後で改行、継続行先頭の=>、body前の`:`は禁止。

| 場所 | layout event |
| --- | --- |
| delimiter外で1段深いbody開始 | NEWLINE INDENT |
| 同levelの次項目 | NEWLINE |
| 浅い行 | 閉じる段数のDEDENT、その後NEWLINE |
| 継続行 | なし |
| delimiter内 | 改行eventなし。ただし内包bodyは自身の基準で通常eventを出し、閉じ行はDEDENTだけ |
| 最初の有効行／EOF | 最初はなし／開いたbodyをDEDENTで閉じる |

閉じないdelimiter、開いていないbody levelへのdedent、bodyでも継続でもない字下げ増加はエラー。

```ebnf
ItemList<X>     := X (NEWLINE X)*
IndentedList<X> := NEWLINE INDENT ItemList<X> DEDENT
```

文法のIndentedListはすべてbody。実行bodyに加え、arm、Container、Origin/accessor/effect付属一覧、requirement領域、#switch一覧を含む。body開始を文法で先に判定し、その後に継続を考える。生成規則が指定するNEWLINEはその規則が消費する。字下げbody内の同じ行にcomma・!・closerを置いてもbodyは閉じず、後の行でdedentが必要。

delimiterは`()`、`[]`、Originの`{}`、type位置のgeneric/type argument`<>`。比較<は開かず、expression中のtype argumentは行をまたがない。delimiter内の継続内容は、現body/chain levelから開いたdelimiterごとに1段深くする。closerは開き行または内容に揃えられる。

delimiter外ではexpressionは先頭`.`のchainだけ、headerは先頭`->`だけで継続する。行末の演算子・`=`や次行先頭の二項演算子では継続しない。delimiter内の内包body外なら、行頭二項演算子を許し、同要素に前のoperandがある+/-は二項になる。

先頭`->`による継続は、文法上まだ`->`を受け取る位置にある関数・accessor headerだけに許し、header基準より1段深く置く。基準自体は変わらない。その行で開いたdelimiter内容はさらに1段深い。次の有効行が->行と同じか浅ければbodyまたは次項目を始める。

header expressionはif/else if/while/requireの条件、matchのsubject、forのin expression、arm guard。bodyを持つexpressionは、そのheader内で新たに開いたdelimiter内だけに置ける。構文全体のgroup化だけではheaderを継続しない。parameter既定expressionはheader expressionではない。

delimiter regionはgroup expression、各引数/要素、字下げbodyの各項目、matchの各armから始まる。label、control transferのoperand、連続=>はregionを作らない。単一項目bodyと同regionのbody付き構文（unsafe/defer/requireのelseを含む）はすべて単一項目形にする。match armは別regionなので字下げbodyを持てる。単一項目if/else if/else内の同regionのifは、elseの有無によらず全体をgroup化する。statementをexpressionとしてgroup化することはできない。

else/else ifは単一項目bodyの終了行、または次の有効行で接続する。別行では最初のif基準へ揃え、間に他項目を挟まない。字下げbodyは先にdedentして閉じる。requireのelseも同一行か、次の有効行をrequireへ揃える。

任意の付属一覧は、次の有効行が1段深く、先頭がorigin、access/get/set、effect等の当該項目を開始できるtokenなら始まる。`.`では始まらない。arm位置の`.`はCase Pattern、新bodyの最初の`.`はInferredCaseExpressionになり得るのでheaderの継続ではない。それ以外は、直前項目より正確に1段深い単独の`.`tokenで始まる行がmember chainを継続し、後続chainも同level。range記号は単独dotではない。期待type・name resolutionは使わない。

### F.5 type

```ebnf
Type               := FunctionType | AnnotatedType
AnnotatedType      := SemanticsType "?"* BorrowOrigin?
FunctionType       := FunctionParameters "->" FunctionResult
FunctionParameters := "(" TaskSlot? TrailingList<Type>? ")"
FunctionResult     := Type | PlaceResult
PlaceResult        := "place" ("ref" | "uniq") "/" SemanticsType BorrowOrigin?
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

`Name/Name`はpair binder/target、`length Name`は関数length slot。Nameやgroup expressionのgeneric引数はdeclarationされたslot種類で分類し、用途から決めない。ArrayLength/ArrayElementTypeの`_`はinitialization付き局所注釈だけ。borrow注釈の付着は§3、Contract・associated type・runtime Viewを指せる位置は§8・9。BoundContainerQualifierはtype側だけで、Adaptation先には使えない。

### F.6 declaration

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
AttributeName        := ? 先頭文字が一般カテゴリLuのName、 §2.5 ?
AttributePrefix      := Attribute (Attribute | NEWLINE)* ("#" "if" CompileCondition NEWLINE)?   // attributeは#ifが制御する1 declarationに付く、 §6.7
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

source rootのlet/varは局所binding。Container種類ごとの項目・空bodyの可否は§6、enumは選択後も空にしない。aliasはsource rootの他項目より前にだけ置き、=>は参照先を導入してbodyを作らない。#if/#switch/#caseはdirective、#に続くNameがLuで始まればattribute、それ以外の#Nameはエラー。

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
                        ParameterList<RequirementParameter> ("->" FunctionResult)?
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

requirementのisはorまで右辺に取り込み、直後notは全体を否定する。`T is not A or B`は`T is not (A or B)`。BorrowOrigin後の外側andはrequirementに属する。constraint・effect・Origin節の配置、associated type、条件付きconformanceは§7・8。

```ebnf
FunctionHeader       := Access? FunctionModifier? "func" Name GenericParameters?
                        ParameterList<Parameter> ("->" FunctionResult)?
FunctionModifier     := "unsafe" | "virtual" | "override"
FunctionDefinition   := FunctionHeader FunctionBody
AttributedFunctionDefinition := AttributePrefix? FunctionDefinition
ForeignFunctionDeclaration := FunctionHeader   // #LibraryImportを伴う、 §22.6
FunctionBody         := Body<FunctionItem>
FunctionItem         := Expression | LocalBinding | AttributedFunctionDefinition | Statement
                      | ConstraintClause | OriginRelation | EffectClause
                      | Directive<FunctionItem>
SpecializationDeclaration := "specialize" "func" Name TypeArguments
                             "(" TaskSlot? TrailingList<SpecializationParameter>? ")"
                             ("->" FunctionResult)? ExecutableBody
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

関数先頭領域にはConstraintClauseと付属effect、OriginRelation、original virtualのEffectClauseを任意順で置ける。文法上の項目許可と、§7による配置constraintは別。receiver・!・taskの可否、外部関数・specialization・init/dropは各章に従う。funcの後がNameならdefinition、`(`/`[`ならFunctionExpression。

### F.7 Property

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

各accessorは最大1回、順序は自由。省略receiver、property種別、requirementは§11。

### F.8 Origin

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

OriginHeaderはstruct/enumのgeneric引数後、OriginBindingSetは名前付きtype後に置く1つのbinding集合。OriginApplicationはtype内のassociated type名後だけで、call・tuple・groupではない。during後の交差は`during (a and b)`と括弧で囲み、relationのoperandには完全なOriginExpressionを使う。relationはdeclaration metadataとして所有declarationの1段下か先頭領域に置く。

### F.9 expression

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
NamedCoreType    := ? type argument内も含めOrigin・"during"・binding集合を持たないContainerPath、 §13.6の実行時is規則 ?
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
AdjacentTypeArguments := TypeArguments   // 次の判定だけによる： §F.9.3
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
                      ("->" FunctionResult)? ExecutableBody
AnonymousParameter := Name (":" Type)?
CaptureList      := "[" TrailingList<Capture>? "]"
Capture          := "var"? Name ("@" ("move" | "ref" | "uniq"))?
CompositionRootExpression := "$" "abort" "(" Expression ")"
                           | "$" "tryWrite" "(" Expression "," StringLiteral ")"
```

type修飾後の.initは必ず生成expression。修飾enum caseは通常のPostfixで解決する。Self/primitive typeの`.Name`は現在のtypeまたはprimitive typeの静的member。runtime isの右辺はNamedCoreType1つで、外側and/orを取り込まない。return/exit/yieldのoperandは完全なExpression。

優先順位は上ほど強い。

| 段 | 演算・構文 | 結合 |
| --- | --- | --- |
| 1 | .name、call、type argument、添字、@follow、後置++/-- | 左からの後置chain |
| 2 | 前置+ - not * ^ ++ -- | 右 |
| 3 | @follow以外の@Adaptation | 左 |
| 4 | try | 右 |
| 5 | * / % | 左 |
| 6 | + - | 左 |
| 7 | << >> | 左 |
| 8 | & | 左 |
| 9 | ^ | 左 |
| 10 | \| | 左 |
| 11 | < <= > >= == != is is not | 非結合 |
| 12 | and | 左 |
| 13 | or | 左 |
| 14 | .. ..= | 非結合 |
| 15 | =、複合代入 | 右 |

比較chainはgroupなしでは不正。段2の前置演算子はtryを直接operandにできず、`-(try x)`等とする。`-x@i64`は`(-x)@i64`、`try x@move * 2`は`(try (x@move))*2`。

#### F.9.3 type argumentと比較の区別

expression中でName・Self・primitive名の後の<をtype argumentにするのは、空白/コメントなしで直続し、同一行のtokenが対応する>までTypeArgumentsを形成し、その次tokenが`(`・`.`またはexpressionを開始できないtokenの場合だけ。例として`) , ] == @ =>`や行末は後者。他は比較<にする。name resolutionを使わず、後の失敗でも読み替えない。type annotation、is右辺、wrap/bitsのtype argumentなどtype位置では、type name後の<は常にtype argument。

```ebnf
ClosedTarget     := "move" | "copy" | BuiltinSemantics          // "/"が続かない
                  | (Semantics "/")* DelimitedTarget
OpenTarget       := "wrap" TypeArguments | "bits" TypeArguments
                  | (Semantics "/")* "::"? TypeSegment PathSuffix* OriginBindingSet? "?"*
                  | (Semantics "/")* DelimitedTarget "?"+
DelimitedTarget  := UnitType | "(" Type ")" | TupleType | "[" ArrayLength "of" Type "]"
```

postfix suffixはClosedTarget後だけ。OpenTarget後の`(`/`[`はエラー、`.`は修飾type headを続ける。expressionのmember/call/indexにするなら`(x@T).name`、`(f@T)()`、`(a@T)[0]`とする。

@後のNameまたはBuiltinSemanticsに/が続けば、空白の有無によらずSemantics prefix。したがって`a@T / b`の対象はT/b。move/copy/followは名前ではなく、followはtargetでもない。move/copy後の/は禁止、follow後は除算。括弧なしtargetに外側Function arrowを含めず、括弧なしtype headはOriginApplication/duringを取り込まない。必要なら`x@(Self.Item(a))`のようにgroup化する。裸rawはアドレス操作。

### F.10 statement・body・pattern

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

SingleItemはdeclaration・directiveを含まない。TestVerificationは独立項目でoperandにはならない。非空bodyと意味は§14。

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

label・Name・:・構文keywordは同じ物理行。match arm一覧は非空でdirectiveを置かない。ForBindingの`(a,b)`はpair分解、`(a,)`は1要素tuple、`(x)`は不正。

`to Label`とoperandの開始はkeywordと同じ行。operandを省略するのは、次が行末・EOF・layout境界、外側のcomma/`)`/`]`/else/=>/引数境界!のときだけ。:とandでは省略しない。labelとoperandの間に空白かblock commentを入れ、`exit to work(x)`のようには書かない。

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
NonInterpolatedString := ? 補間なしのStringLiteral、 §14.8 ?
```

CaseReferenceはenum自身のOrigin binding集合/duringを書かないが、type argument内のtypeは完全type。意味は§14。

### F.11 コンパイル時指示

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
PlainString           := ? 補間なしのStringLiteral、 §19.2 ?
```

#case _は最大1回、最後だけ。Itemの種類は周囲の一覧を引き継ぎ、directiveが禁止項目を許すことはない。Directiveを明記した一覧だけで使い、#switch arm外の#caseは禁止。除外構文も同じ文法で解析する。

<a id="check-schema"></a>
## 検査出力のJSON schema

キー・enumeration value・constraintは原文のまま、説明だけを日本語化した。

~~~json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://kimigayo.dev/schemas/check-output.schema.json",
  "title": "kimi check出力（kimi.check/3）",
  "description": "順序付きunitsを持つcommand結果1個。単一Projectも配列。選択失敗ならunitsは空。diagnostic・関連位置・修復のsource indexは当該結果内だけで有効。spanはUTF-16単位、rangeは0始まり。公開前cancellationでは文書を出さない。集約・順序・公開は§23.2。",
  "type": "object",
  "required": ["schema", "compiler", "outcome", "accepted", "sources", "diagnostics", "units"],
  "additionalProperties": false,
  "properties": {
    "schema": { "const": "kimi.check/3" },
    "compiler": { "type": "string", "description": "全unit共通のcompiler build identity。" },
    "outcome": { "$ref": "#/$defs/outcome", "description": "選択失敗はInputならBlocked、InternalならFaulted。選択後はFaultedがあればFaulted、なければBlockedがあればBlocked、他はCompleted。" },
    "accepted": { "type": "boolean", "description": "1つ以上のunitを選択でき、全unitがacceptedの場合だけtrue。" },
    "sources": { "$ref": "#/$defs/sources", "description": "commandの選択diagnosticが参照するsource。選択成功後は空。" },
    "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" }, "description": "選択失敗だけ。BlockedはInput Error、FaultedはInternal Error。選択成功後は空で、unit diagnosticを複製しない。" },
    "units": { "type": "array", "items": { "$ref": "#/$defs/unitResult" }, "description": "§23.2のパス順に全選択unitを格納。Blocked/Faultedも含む。空は選択失敗時だけ。" }
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
            "project": { "type": "string", "description": "正規化した選択Projectまたは暗黙sourceのパス。" },
            "target": { "type": "string", "description": "選択target。確定できなければ--Target value、なければ空文字列。" },
            "mode": { "enum": ["Product", "Test"], "description": "Productはkimi check、Testはtest準備の結果。" },
            "debug": { "type": "boolean" }
          }
        },
        "outcome": { "$ref": "#/$defs/outcome", "description": "Completedは検査済み（エラーの有無は別）、Blockedは入力・設定未確立、Faultedは内部失敗。" },
        "accepted": { "type": "boolean", "description": "outcomeがCompletedかつErrorなしの場合だけtrue。拒否unitにはErrorが1つ以上ある。" },
        "testPresence": { "enum": ["Yes", "No", "Unknown"], "description": "Productでは、自Projectのsourceに#Test関数があればYes、なく全てsource errorなしで解析できればNo、他はUnknown。Testでは常にUnknown。" },
        "sources": { "$ref": "#/$defs/sources" },
        "diagnostics": { "type": "array", "items": { "$ref": "#/$defs/diagnostic" } }
      }
    },
    "sources": {
      "type": "array",
      "description": "当該結果のdiagnosticが名指す全sourceを読取順に格納。indexは当該結果内だけで有効。",
      "items": {
        "type": "object",
        "required": ["path", "isInput"],
        "additionalProperties": false,
        "properties": {
          "path": { "type": "string", "description": "表示パス。" },
          "isInput": { "type": "boolean", "description": "ファイル・開いた文書から読んだ入力ならtrue。組込み・生成sourceはfalse。" },
          "sha256": { "type": "string", "pattern": "^[0-9a-f]{64}$", "description": "読めた入力のbyte列hash。読めた場合に存在。" }
        }
      }
    },
    "span": {
      "type": "object",
      "description": "UTF-16 code unitによる開始offsetと長さ。長さ0は挿入点。",
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
      "description": "0始まりの行とUTF-16文字位置。endを含まない。",
      "required": ["start", "end"],
      "additionalProperties": false,
      "properties": { "start": { "$ref": "#/$defs/position" }, "end": { "$ref": "#/$defs/position" } }
    },
    "value": {
      "type": "object",
      "description": "理由または修復に使うtyped fact1つ。",
      "required": ["name", "kind", "value"],
      "additionalProperties": false,
      "properties": {
        "name": { "type": "string" },
        "kind": { "enum": ["Number", "Enumeration", "Boolean", "Text", "Requirement", "Origin"] },
        "value": { "type": "string" },
        "origin": { "enum": ["expression", "borrow", "omitted", "closure"], "description": "kindがOriginの場合にだけ存在し、その場合は必須。valueはfixed/finite Originを示す。expressionはsignature内のOrigin expression、borrowはborrow/capture項目のsource、omittedは無名Originのtype出現/pair binder declaration、closureはClosureのcall receiverまたはinference call result。" },
        "elided": { "type": "boolean", "description": "表示用に短縮した場合true。" }
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
        "source": { "type": "integer", "minimum": -1, "description": "source表index。全source外は-1。" },
        "span": { "$ref": "#/$defs/span", "description": "source全体を指す場合、またはsource=-1なら省略。" },
        "range": { "$ref": "#/$defs/range" },
        "label": { "type": "string" }
      }
    },
    "omission": {
      "type": "object",
      "description": "表示上限で省略したdiagnostic部分。",
      "required": ["part"],
      "additionalProperties": false,
      "properties": { "part": { "type": "string" }, "count": { "type": "integer", "minimum": 1, "description": "省略項目数。不明なら省略。" } }
    },
    "excerptLine": {
      "type": "object",
      "required": ["line", "text", "start", "length"],
      "additionalProperties": false,
      "properties": {
        "line": { "type": "integer", "minimum": 1, "description": "1始まりの表示行。" },
        "text": { "type": "string" },
        "start": { "type": "integer", "minimum": 0 },
        "length": { "type": "integer", "minimum": 0, "description": "強調する文字列の長さ。0は挿入点。" }
      }
    },
    "display": {
      "type": "object",
      "description": "表示用data。diagnosticの解釈には不要。",
      "required": ["excerpt"],
      "additionalProperties": false,
      "properties": { "range": { "$ref": "#/$defs/range" }, "excerpt": { "type": "array", "items": { "$ref": "#/$defs/excerptLine" } } }
    },
    "edit": {
      "type": "object",
      "required": ["source", "span", "text"],
      "additionalProperties": false,
      "properties": {
        "source": { "type": "integer", "minimum": 0, "description": "入力のsource表index。" },
        "span": { "$ref": "#/$defs/span" },
        "range": { "$ref": "#/$defs/range" },
        "text": { "type": "string", "description": "置換文字列。削除なら空。" },
        "replaced": { "type": "string", "description": "span内の長さ制限付き表示文字列。挿入では省略。" }
      }
    },
    "requiredCondition": {
      "type": "object",
      "description": "検査が未決とした条件と、満たすべき内容の説明。",
      "required": ["condition", "phrase"],
      "additionalProperties": false,
      "properties": {
        "condition": { "$ref": "#/$defs/condition" },
        "phrase": { "type": "string" }
      }
    },
    "condition": {
      "enum": ["Take", "ExclusiveAccess", "UsageLegality", "Structure", "Selection"],
      "description": "TakeはPlaceをMove可能、ExclusiveAccessは貸出し点へ排他的に書込可能、UsageLegalityは後の利用が合法、Structureは名前・cleanup順・制御を維持、Selectionは修飾名が用途の要求を選択。"
    },
    "repair": {
      "type": "object",
      "description": "構造化編集の代替候補1つ。verifiedは検証済み、requiredは未決。適用後は新しい入力であり再検査で検証する。",
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
        "source": { "type": "integer", "minimum": -1, "description": "source表index。全source外は-1。" },
        "span": { "$ref": "#/$defs/span", "description": "source全体を指す場合、またはsource=-1なら省略。" },
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
~~~

<a id="status"></a>
## 実装状況（参考・2026年10月15日）

STATUSは仕様とは別の検証記録である。10月15日に仕様の57規則と、その後の8項目が決定・改訂されており、従来の検証結果が新仕様へのconformanceを保証するわけではない。S1015の完了まで改訂規則へのconformanceは未検証。以下は同日版STATUSの主要な境界の要約で、過去の個別修正・測定履歴・test名一覧は省略する。

| 分野 | 検証済み範囲と残る制限 |
| --- | --- |
| type・inference・Origin | 有界なassociated type inference、固定入力によるconstructor inference、Origin premiseの有界なclosure、実際のLoan保持等には個別の受理・拒否・編集・allocation回帰検証がある。一般のassociated type逆inference、任意のOrigin・Callable形、artifact間のproof公開までを保証しない |
| 演算・object・Callable | 算術Contract、共通の期待type Adaptation、Place result、共有virtual dispatch等に検証がある。排他ObjectCallCompatible、継承object receiverのContract projection、置換をまたぐCallable valueのidentity等は未完。参照層を通る一部のobjectアクセスには既知の未修正欠陥があり、言語の制限とは区別する |
| static・非同期 | 一般のstatic initialization・保持borrow・終了処理は未完。task;構文、Async、coroutine、scheduler、worker、Transferable、thread安全なstatic initializationは未実装。仕様にあることを実行可能性の根拠にしない |
| module・package | Project依存のsource graph、厳密identity、product/test lock分離、対応するmodule間callは検証済み。packageのload/pack/publish/store、完全なnative供給・内容照合、可搬な意味記録は未完 |
| diagnostic・editor | diagnostic、quick fix、declaration・Copy・effect・文書のHoverの対応範囲がある。completionとCSP adapterは未提供。compilerは単一unitのkimi.check/2を出し、本書のkimi.check/3 command envelopeは未実装 |
| test | Windows x64で発見、全body検査、$expect/$require、Project/Solution実行、分離process、期限・cancellation・log・一時領域処理が検証されている。通常compilerの未対応形はtestにも残り、依存module自身のtestは自動実行しない |
| 性能 | 一部の固定workloadにwarm時のcompiler allocation 0、native O0/O2・allocation回帰の証拠がある。全Binding、Project読込み、起動、editor応答全体の無allocation・高速化を意味しない |

各記録の対象入力・日付・証拠・未修正事項は原文STATUSに従う。この翻訳作業ではcompilerのbuild、test、native検証を新たに実行していない。

<a id="sources"></a>
## 出典と収録範囲

仕様indexから参照される言語章・付録、実装仕様、検査schemaを統合した。繰り返す説明や例を整理し、原文の節をまとめた箇所がある。body中の§番号は原文の規則位置を示すため、細かな節番号が本書の見出しと一致しない場合も、同じ章内の対応する説明を参照する。識別子・構文・固定出力文字列は翻訳しない。

| 本書 | 原文 |
| --- | --- |
| 仕様全体 | [SPEC.md](SPEC.md) |
| 1・2 | [概要](spec/01-overview.md)、[ソースと字句](spec/02-source-and-lexical-structure.md) |
| 3〜5 | [typeとvalue](spec/03-types-and-values.md)、[配列等](spec/04-arrays-indexing-and-slices.md)、[rawとunsafe](spec/05-raw-pointers-and-unsafe-memory.md) |
| 6〜8 | [declaration](spec/06-declarations-and-containers.md)、[関数](spec/07-functions-and-callable-values.md)、[genericとContract](spec/08-generics-constraints-and-contracts.md) |
| 9〜11 | [名前](spec/09-names-signatures-and-access.md)、[overloadとinference](spec/10-overload-resolution-and-inference.md)、[Property](spec/11-properties.md) |
| 12〜14 | [expression](spec/12-expressions.md)、[演算](spec/13-operators-and-assignment.md)、[制御フロー](spec/14-control-flow.md) |
| 15〜17 | [ownership・lifetime](spec/15-ownership-and-lifetime-analysis.md)、[destruction](spec/16-scope-exit-and-destruction.md)、[失敗](spec/17-failure-handling.md) |
| 18・19 | [moduleと依存](spec/18-modules-and-dependencies.md)、[コンパイル時指示](spec/19-compile-time-directives.md) |
| 20・21・A | [IMPL index](IMPL.md)、[設定](impl/20-compilation-configuration.md)、[layout・runtime・生成](impl/21-layout-runtime-and-code-generation.md)、[compiler requirement](impl/appendices/A-compiler-requirements.md) |
| 22〜24 | [Kimiと実行](spec/22-core-execution-and-foreign-functions.md)、[サービス](spec/23-compiler-services.md)、[非同期](spec/24-suspension-and-asynchronous-tasks.md) |
| D・E・F | [保留](spec/appendices/D-deferred-features.md)、[用語](spec/appendices/E-terminology.md)、[文法](spec/appendices/F-syntax-summary.md) |
| JSON schema | [check-output.schema.json](spec/schemas/check-output.schema.json) |
| 実装状況の要約 | [STATUS.md](STATUS.md) |

STATUSから先の開発計画、検証履歴、実装コード、benchmark等は翻訳範囲に含めない。
