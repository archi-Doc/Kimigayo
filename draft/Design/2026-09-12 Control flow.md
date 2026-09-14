# Control flow — 制御フロー

本書は、[SPEC.md](../../SPEC.md) §14を中心に制御フローを再定義する。変更する規則は本書を優先し、未変更部分はSPEC.mdに従う。共通規則を§1、転送を§2、各構文を§3・§4、静的検査と後始末を§5、文法とSPEC.mdとの対応を§6にまとめる。

Unitの値は `()` で表す。コード例は独立した断片であり、周囲の宣言や対象ラベルは省略している。出力・後始末用の関数は、注記がなければUnitを返す。

## 1. 共通規則

### 1.1. 式と文

```text
制御フロー
├─ 式
│  ├─ 選択：if / match
│  ├─ 反復：for / while / loop
│  ├─ ブロック：block / Label: block
│  └─ 転送：return / exit / continue / yield
└─ 文
   ├─ unsafe：その場でunsafe操作を許可して実行する
   ├─ defer：現在のスコープの終了処理を登録する
   └─ require：条件がfalseなら、後続へ正常に進まない処理を実行する
```

式には型があり、値を捨てる場合も各操作の型を検査する。構文の結果として使う値と、その場で捨てる値は§1.4で区別する。文は初期化子・引数・演算子のオペランドには置けない。文の正常完了をUnitで表しても、その文が式になるわけではない。

### 1.2. 本体の2形式

```text
Body := "=>" (Expression | Statement)
      | 改行 + インデント本体
```

| 形式 | 内容 | 正常完了時の扱い |
| --- | --- | --- |
| 単一本体 | `=>` の右に式または文を1個置く | 式の値は§1.4に従って使うか捨てる。文はUnitで完了 |
| インデント本体 | 宣言・式・文・許可されたコンパイル時指令を順に実行する | 末尾到達でUnit。末尾式の値は捨てる |

単一本体は「単一項目本体」の略であり、1行だけという意味ではない。式の継続やmatchのアーム一覧を含む場合は複数行になる。

反復本体の正常完了は、反復全体の終了ではなく次の反復へ進むことを表す。転送と後始末の扱いは§2・§5に従う。

```kimi
if ready => start()
else => stop()

if ready
    prepare()
    start()

defer => unsafe => releaseRaw(pointer)
```

この2形式を、選択の各句・アーム、反復、block、unsafe、defer、requireの失敗本体に適用する。関数・匿名関数・Closure・特殊化・カスタムアクセサー・init・deinitにも適用する。

宣言・指令はインデント本体だけに置き、各位置の既存の制限を保つ。総称関数の明示的な制約節も、SPEC.md §7.4に従いインデント本体の先頭に置く。宣言コンテナーとmatchのアーム一覧は実行本体ではなく、本体のない宣言や標準アクセサーに本体を要求する変更でもない。

`=>` は、この位置ではヘッダーと本体を対応付ける。匿名関数には `func` が必要である。`:` は型注釈・ラベル・名前付き引数・辞書・base init・名前付き転送などの区切りであり、本体の開始には使わない。どちらも汎用演算子ではなく、意味は文法位置で決まる。

### 1.3. レイアウトとスコープ

構文の区切り、名前や値の寿命、制御転送の範囲を区別する。

| 概念 | 役割 | 主な例・参照先 |
| --- | --- | --- |
| 区切り範囲 | 構文の対応と入れ子を決める | 括弧、引数・要素、アームなど。§1.3.1 |
| 本体スコープ | 本体内の名前の有効範囲と後始末を決める | 2形式の本体。§1.3.3・§5.3 |
| 転送対象 | return・exit・continue・yieldを受け取る | 関数・選択式・反復式・block・defer。§2.2 |
| 探索境界 | 転送対象を探す範囲を制限する | 関数・defer。操作ごとの制限は§2.2 |

括弧は区切り範囲を作るが、本体スコープ・転送対象・探索境界を追加しない。blockは名前付きexitの対象となるが、他の転送の探索は止めない。

#### 1.3.1. 開始・継続・終了

- `=>` と右側の式・文の開始は、ヘッダーの終了行に置く。括弧内・チェーンなど通常の式の継続は認めるが、同じ区切り範囲で続く構文の実行本体は単一本体に限る。
- インデント本体の前に、本体用の `:` や `=>` を置かない。`=>` だけを書いて改行する形も認めない。
- 1段は4空白とする。基準はヘッダー開始行の行頭インデントであり、キーワードの文字位置・ラベルの長さ・ヘッダー途中の折り返しでは変えない。
- 本体は基準より1段深くする。matchのアーム一覧はmatchより1段、アーム本体はアームよりさらに1段深くする。
- 次の有効行で深さが戻ると本体を閉じる。空行・コメントは無視し、EOFでは残る本体を閉じる。外側のカンマや閉じ括弧で、同じ行のインデント本体を閉じない。

ヘッダーの条件・反復対象などを複数行にする場合、その式内部の括弧で継続する。構文全体を囲む外側の括弧だけでは継続しない。

**区切り範囲**は、単一本体の制限と後述のifの入れ子規則で共通に使う。

| 新しい範囲を作るもの | 範囲の終端 |
| --- | --- |
| グループ化した括弧内の式、括弧・角括弧内の各引数・要素 | 対応する閉じ括弧、または次の引数・要素との区切り |
| インデント本体の各項目 | 項目の終端 |
| matchの各アーム | アームの終端 |

ラベル・転送オペランド・連続する `=>` は範囲を分けない。matchのアーム一覧自体は実行本体ではないため、単一本体内のmatchと、その各アームのインデント本体は使用できる。

```kimi
if ready => for item in items => process(item)

defer => unsafe
    releaseRaw(pointer) // エラー：同じ範囲のunsafeにも単一本体が必要

for item in filtered(
    source,
    predicate
) => process(item)

consume(
    match mode
        .Fast => 1
        _
            prepare()
            yield 2
)
```

#### 1.3.2. elseと入れ子

ifのelse / else ifは、単一本体の終了行または次の有効行に置く。インデント本体の直後では、次の有効行のDEDENTで本体を閉じてから句を読む。別行の句は最初のifの基準にそろえ、間に別の項目を挟まない。requireのelseも、条件の終了行またはrequireと同じ基準の次の有効行に置く。

if・else if・elseの単一本体に、同じ区切り範囲のifを入れる場合、elseの有無によらず内側全体を括弧で囲む。ヘッダー式内に本体付きの式を書く場合も、その内側を括弧で区切る。

```kimi
let value = if a => (if b => 1 else => 2) else => 3
if (if a => b else => c) => work()
func run() => if ready => work() // 外側にifがないので括弧不要
```

文を式用の括弧に入れることはできない。必要な括弧の省略や、対応先のないelseは構文エラーとする。

フォーマッタは本体形式を維持し、単一本体内のifを同じ行で完結させる書式を優先する。本体形式の変換は、§1.4の意味を検査する独立したリファクタリング操作とする。

#### 1.3.3. スコープと非空性

両形式は独立した本体スコープを持つ。構文に属さない単独のインデント本体は認めない。

インデント本体には完全なソース項目が1個以上必要である。空行・コメントだけでは不十分で、何もしない場合は `()` を書く。非空性は条件付きコンパイルの選択前に検査する。完全な指令は1項目と数えるが、選択後の型・転送検査は免除しない。matchのアーム一覧は選択後も1アーム以上を必要とする。

ヘッダーの次行の `.where(...)` などはヘッダー継続ではない。誤記が疑われる場合は括弧による継続を案内し、構文を推測で読み替えない。引数途中のインデント本体で行頭カンマが必要になる場合は、値をletで受けるか、意味を変えずに最後の引数に置く書式を推奨する。

### 1.4. 結果の使用と破棄

#### 1.4.1. 値の使い方と型の役割

値を使うか捨てるかという判断と、その値に関係する型を区別する。

| 概念 | 判断すること |
| --- | --- |
| 評価コンテキスト | その位置の値を使うか、捨てるか |
| 期待型 | 外側からどの型が要求されているか |
| 対象結果型 | 対象へ供給する結果源を、どの型へ適合させるか |
| 式の型 | 検査後、その式がどの型を持つか |

期待型・対象結果型・式の型は同じこともあるが、役割は異なる。例えば `let value: i32 = loop => continue` は、初期化子なのでValue Context、期待型と対象結果型はi32である。一方、結果源がなく構造上も正常完了しないため、初期化式の型はNeverとなる（§5.1.2）。

| 位置 | 評価コンテキスト |
| --- | --- |
| 初期化子・引数・条件・match対象・反復対象・演算子や転送のオペランド | 値を使う（Value Context） |
| 独立した式、インデント本体に直接置いた式 | 値を捨てる（Discard Context）。末尾も同じ |

括弧はコンテキストと期待型を保つ。変数の未使用や最適化では変えない。Discard Contextは一般の式をUnitへ暗黙変換せず、その値を通常の寿命で破棄する。各操作の型・所有権・借用とResult破棄などの警告も検査する。

#### 1.4.2. 本体の結果と明示転送

**単一本体の値を使うか捨てるかは、本体の検査前に次表で決める。** 外側の期待型がUnitというだけでは、一般の式をDiscard Contextにしない。内部の引数・初期化子などは、それぞれのコンテキストを保つ。

| 所属する構文 | 単一本体の式と正常完了 | 結果を供給する転送 |
| --- | --- | --- |
| 関数・get | 戻り型が事前にUnitと確定していればDiscard Contextで捨て、Unitで完了。それ以外はValue Contextで戻り値にする | return |
| if / match / block | 構文自身のコンテキストを継承。Value Contextでは結果にし、Discard Contextでは捨ててUnitで完了 | 選択式はyield、blockは名前付きexit |
| for / while / loop | Discard Contextで捨て、次の反復へ進む | exit |
| unsafe / defer | Discard Contextで捨て、Unitで完了。deferの本体は終了処理として実行 | deferはexit。unsafeは対象を持たない |
| set / init / deinit | Discard Contextで捨て、Unitで完了 | return |
| requireの失敗本体 | Discard Context。後始末を含め、requireの後続への正常な継続を禁止 | 対象を持たない |

**構文の結果になる値は、構造上のすべての結果源から型を決める。その場で捨てる値同士は型をそろえない。** 結果源の収集と型推論は§5.1に従う。

for・while・defer・set・init・deinit、およびDiscard Contextのif・match・block・loopは、対象結果型をUnitに固定する。明示転送の値は捨てずにその型へ適合させる。関数も、戻り型がUnitなら `return 123` を許可しない。対象結果型と式のNeverの関係は§5.1.2に従う。

Value Contextのloopは、自分宛てexitを結果源とする。反復本体の末尾は反復を続けるだけであり、反復式の結果源にはしない。対象が転送を受け取った場合も、暗黙の本体結果を重ねて供給しない。

名前付き関数の戻り型省略はUnit、getは宣言した戻り型とする。匿名関数は明示型・固定の期待戻り型を優先し、なければ本体から推論する。本体のコンテキストは定義の型検査時に確定し、総称引数の具体化や推論後に再解釈しない。匿名関数の期待型を確定する順序は§5.1.3、関数境界は§2.2に従う。

```kimi
func direct() -> i32 => calculate()

func indented() -> i32
    return calculate() // calculate()だけでは末尾到達のUnitになる

let invalid: i32 = if ready
    1 // 捨てられる。末尾到達のUnitはi32に適合しない
else => 0

if ready => visited.insert(id) // insertがboolを返しても、その場で捨てる
deinit => handle.close() // closeがboolを返しても、その場で捨てる
func cleanup() => handle.close() // 既定の戻り型Unitでも同じ
func invalidReturn() => return 123 // エラー：明示returnはUnitに適合させる

match command
    .Put(let key, let value) => table.insert(key, value) // Option<V>を捨てる
    .Clear => table.clear() // Unit
    _ => ()

let invalidTypes = if ready => 1 else => "error"
// エラー：値として使う結果源は共通の型へ適合させる
```

2形式は意味が異なる。相互変換では、結果型・転送先・スコープ・破棄順序を保つ。値を返す単一本体をインデント本体に変える場合は、必要なreturn・yield・名前付きexitを明示する。

### 1.5. 静的解析の共通モデル

条件付きコンパイルの選択後、結果源の収集と制御経路の解析を区別する。次表は概念ごとの役割を示し、実装の処理順やパス数を指定するものではない。

| 役割 | 行うこと | 詳細 |
| --- | --- | --- |
| 結果源の収集 | 値を供給する箇所を集める。明示転送は到達不能でも含め、暗黙Unitは構造的完了の規則で加える | §5.1.2 |
| 構造的完了（Structural Completion） | 構文の評価順序と転送から正常完了の候補を求め、暗黙UnitとNeverの判定に使う | §5.1.1 |
| 実行到達性（Runtime Reachability） | 共通の制御経路に初期化・Move・Loan・型の絞り込み・登録済みdeferと破棄を反映する | §5.1.4 |
| 型検査用の継続 | 到達不能コードも、その位置で有効な型情報を使って検査する。実行経路は追加しない | §5.2.2 |

構造的完了と実行到達性は、どちらも静的検査用の保守的近似であり、実行可能性の完全な証明ではない。**条件値を使わない共通の経路規則に従う。** 実行到達性は後始末による非終了も扱うが、その結果で型制約を削除しない。実行時の分岐は通常どおり条件値に従う。

## 2. ラベルと制御転送

### 2.1. 記法とラベルの有効範囲

```text
return [Expression]
exit [Expression] | exit to Label [: Expression]
continue [to Label]
yield [Expression] | yield to Label [: Expression]
```

角括弧は省略可能を表す。名前付き転送は操作の直後に `to Label` を置く。returnに名前付き形式はない。ラベルはif・match・for・while・loop・blockに `Label: 構文` の形で任意に付け、同じ物理行に書く。

```kimi
outer: for item in items => process(item)
let value = work: block => calculate()
```

ラベルは変数・型とは別の名前空間を持つ。同じ関数で有効範囲が重なる同名ラベルは禁止する。自分の本体内だけで有効であり、自分の条件・反復対象・match対象・ガードでは使えない。

転送できるのは、同じ関数内で自分を囲む構文だけである。別の関数、兄弟・内側・終了済みの構文へは転送できない。ラベルは命令の位置を表さない。

名前付き引数・辞書の区切りと競合する位置では、ラベル付き式全体を括弧で囲む。return・exit・yieldのオペランドがラベル付き式である場合も、転送先の指定の有無によらず括弧で囲む。

```kimi
consume(value: if ready => 1 else => 0) // valueは引数名
consume((choice: if ready => 1 else => 0)) // choiceはラベル
yield to outer: (inner: if ready => 1 else => 0)
return (work: block => calculate())
```

### 2.2. 対象の決定

| 操作 | ラベルなしの対象 | 名前付きの対象 | 探索境界 |
| --- | --- | --- | --- |
| return | 最も近い関数 | なし | defer |
| exit | 最も近い反復式またはdefer | 指定した反復式・block | 関数。名前付きではdeferも越えない |
| continue | 最も近い反復式 | 指定した反復式 | 関数・defer |
| yield | 最も近いif / match | 指定したif / match | 関数・defer |

**対象を決めてから使用位置・値・型を検査し、不適切でも外側へ探し直さない。** 対象が見つからなければエラーとする。ラベルなしyieldの対象がDiscard Contextなら、値の有無によらずエラーとし、ラベルの指定を案内する。

これは型検査とは別の誤転送防止規則である。条件付きyieldが内側のifだけを終える誤りを防ぎ、結果を捨てる選択式の早期終了には対象ラベルを要求する。ラベルを付けても、値は対象結果型へ適合させる。

各構文が転送対象や探索境界として働くのは、その本体内だけである。unsafe・requireは転送対象や探索境界を作らず、blockは自分宛ての名前付きexit以外を通過させる。選択式はreturn・exit・continueを、反復式はyieldを通過させる。外側への転送は関数・deferを越えられない。

関数境界には、メソッド・匿名関数・Closure・特殊化・アクセサー・init・deinitも含む。defer内に定義した関数は、自分のreturnを通常どおり使える。

```kimi
let value = selection: if enabled
    if cached() => yield to selection: cachedValue()
    yield calculate()
else => 0
// 条件付きyieldのラベルを省くと、内側のifがDiscard Contextなのでエラー
```

### 2.3. 値の指定

return・yield・exitの値省略は `()` を書いた形と等価であり、Unitも§1.4の対象結果型へ適合させる。continueは値を持たない。

return・exit・yieldの値の式は、転送キーワードと同じ物理行で開始する。名前付き転送では `to Label` も同じ行に置き、値との間に `:` を置く。値を省略する場合は `:` も省略する。式の開始後は§1.3の継続規則に従う。

`to` はexit・continue・yieldの直後でだけ文脈キーワードとなる。同名の変数を値として使う場合は `exit (to)` と書く。後置の `value to Label` は認めない。

```kimi
exit to search: score(item)
exit to outer: -1
continue to outer

yield to selection: match mode
    .Fast => 1
    _ => 0
```

転送式自身はNeverであり、評価元へ正常には戻らない。対象は転送を受け取って正常完了できるため、`yield 1` がNeverでも対象の選択式は整数を返せる。値の確保と後始末は§5.3に従う。

## 3. 式

各構文の実行・完了・転送・固有の制限を示す。本体形式と結果の共通規則は§1、転送対象の探索は§2に従う。

### 3.1. if

bool条件を上から順に評価し、最初にtrueとなった句を実行する。どれもfalseならelseを実行する。else ifの連鎖全体で1個の選択式となる。

**else省略は `else => ()` と等価である。** Value Contextでは、このUnitも結果型の制約に含める。Discard Contextでは各本体の値を捨てる。

自分宛てyieldを受け取って終了する。インデント本体から非Unitの値を供給する場合もyieldを使う。

```kimi
func reset() => if dirty => clear() // 両側ともUnit
if ready => 1 // 許可：値を捨てる
let invalid = if ready => 1 // エラー：整数とelse省略時のUnitが適合しない

let value = if ready
    prepare()
    yield 1
else if waiting => 2
else => 0
```

### 3.2. match

#### 3.2.1. 選択とアーム

対象を一度だけ評価・取得し、アームを上から調べる。Patternが一致し、任意のboolガードもtrueとなる最初のアームだけを実行する。次のアームへ処理を続けるfall-throughはない。

各アームは独立に本体形式を選び、結果は§1.4に従う。自分宛てyieldを受け取って終了する。

**matchは使用位置によらず常に網羅的とする。** 処理しない値は `_ => ()` などで明示する。Patternと取得の制限は§3.2.2、網羅性の証明は§3.2.3に定める。

```kimi
func positiveOrZero(value: Option<i32>) -> i32
    return match value
        .Some(let n) if n > 0 => n
        .Some(_)
            log("non-positive")
            yield 0
        .None => 0

action: match event@ref
    .Save
        if readOnly => yield to action
        save()
    _ => ()
```

#### 3.2.2. Pattern・取得・ガード

Patternは一般の式とは別の構文であり、SPEC.md §14.8の形式を使う。

| 種類 | 例 |
| --- | --- |
| ワイルドカード・束縛 | `_`、`let value`、`var value` |
| リテラル | `0`、`-1`、`true`、`'A'`、`"ok"` |
| Enum Case | `.None`、`.Some(let value)` |
| Unit・Tuple・グループ化 | `()`、`(let x, let y)`、`(.Some(_))` |

非Copyの所有変数を `match message` として渡すと、messageはmatch開始時にMoveされる。Patternの束縛名はそのアームだけで有効となり、次の順に処理する。

1. 取得済み対象内の候補を、ガードから共有読み取りする。
2. ガードの結果を確保し、一時値・一時的なLoanを後始末する。
3. trueなら候補から本体のローカルへCopy・Move・借用する。falseなら候補を本体へ移さず、次のアームを調べる。

ガード失敗時も、match開始時の取得は取り消さない。ガードと本体の束縛は別の識別子として扱い、それぞれの型で名前・呼出しを解決する。

候補の変更・Move・排他的借用・直接captureは禁止し、共有読み取り結果の持ち出しはOrigin・Loanを検査する。対象と参照先の共有保護、構造検査、束縛型、部分Move、対象の破棄の詳細はSPEC.md §14.8・§15.1.6に従う。ガードや後始末が正常完了しなければ、本体の取得や次のアームへは進まない。

#### 3.2.3. 網羅性の証明

ガードなしのPatternだけで証明する。`if true` のガード、対象の定数値、本体のNeverを網羅性の根拠にしない。

| 対象 | 証明できる形 |
| --- | --- |
| 任意の型 | `_` または全体を束縛するPattern |
| bool / Unit | `true` と `false` / `()` |
| Enum | 全Caseを列挙し、各Caseのpayload全体を覆う |
| Tuple | 全要素を覆う1個のTuple Pattern |
| 整数・char・string・構造検査できない型 | `_` または全体を束縛するPatternが必要 |

全体を覆うPatternは、ワイルドカード・束縛・Unit・再帰的に全要素を覆うTupleである。複数の部分Patternを組み合わせる証明は将来の検討事項とし、本版では使わない。例えば `.Some(true)` と `.Some(false)` を書いても、`.Some(_)` などが必要となる。

1個の先行するガードなしPatternが後続Patternを包含するときは警告するが、そのアームも型・結果検査から除外しない。証明・包含の詳細はSPEC.md §14.8.4の保守的な規則を維持する。

Case追加の検出を保ちたい場合は、全体の `_` より `.Some(_)` などCaseごとの補完を推奨する。TupleではCaseごとの部分Patternを並べても証明できないため、各要素のmatchを入れ子にする方法を使える。

```kimi
match state
    .Idle
        match event
            .Start => start()
            .Stop => ()
    .Running
        match event
            .Start => ()
            .Stop => stop()
```

例のstateとeventは、それぞれ列挙した2つのCaseだけを持つCopyのEnumとする。各matchを独立に網羅性検査するため、どちらのEnumへのCase追加も検出できる。Tupleからの書き換えでは、対象式の評価順序とMove・借用・破棄が変わりうるため、機械的には置換しない。

### 3.3. for・while・loop

各反復は新しい本体スコープで実行する。本体の正常結果は捨て、スコープを後始末してから次の反復へ進む。

| 構文 | 開始時 | 本体末尾・自分宛てcontinue | 正常終了 |
| --- | --- | --- | --- |
| for | 対象を一度だけ取得し、最初の要素を求める | 次の要素を求める | 要素の終了または自分宛てexit。結果はUnit |
| while | bool条件を評価する | 条件を再評価する | 条件falseまたは自分宛てexit。結果はUnit |
| loop | 本体へ進む | 本体の先頭へ戻る | 自分宛てexitだけが結果を供給する |

whileは条件がtrueでも、静的解析では条件falseの終了経路を残す（§5.1.4）。無条件の反復にはloopを使う。

forの束縛は単一の名前またはTupleを分解する `(key, value)` とする。各反復の変更不可のletであり、名前の重複や一般のPatternは認めない。

取得・反復はSPEC.md §14.6.2のIterable / Iterator規則に従う。非Copyの所有コレクションは直接反復すると消費される。対応するコレクションの共有反復には `values[..]` を使う。iteratorの記憶域を貸し出す反復や、新しい暗黙借用は追加しない。

```kimi
for value in values
    if skip(value) => continue
    if done(value) => exit
    process(value)

while ready => process()

let found: i32 = search: loop
    for value in values[..]
        if accepts(value) => exit to search: score(value)
    exit -1
```

最後の例のscoreはi32を返す。`exit score(value)` なら内側のforを指すため、Unitとの型不一致になる。`loop => work()` は繰り返し、`let value = loop => exit 1` は1を結果にする。結果を捨てる `loop => exit 1` はUnitとの型不一致になる。

### 3.4. block

blockは一度だけ本体を実行する式である。正常完了時の結果は§1.4に従う。

自分宛ての名前付きexitを受け取って終了する。ラベルなしexitはblockを対象にしないため、ラベルのないインデント本体から非Unitの結果を返すことはできない。外側への転送や非終了は通常どおり認める。

`block` は予約語であり、変数・関数・メンバー・ラベルなどの名前には使えない。ヘッダー内での括弧は§1.3に従う。

```kimi
let value = work: block
    if cached() => exit to work: cachedValue()
    exit to work: calculate()

block
    let resource = open()
    defer => close(resource)
    use(resource)
// このblockの終了時にcloseと残る破棄を行う

let other = block => calculate()
```

## 4. 文

各文も§1の共通Bodyを使う。正常完了をUnitとして扱う場合も、文自体を値として使うことはできない。

### 4.1. unsafe

本体をその場で実行し、unsafe操作を字句的に許可する。本体が正常完了した場合は、後始末を終えて後続へ進む。

転送対象や探索境界は作らない。外側へ値を渡す場合は、本体内からreturn・yield・名前付きexitを使う。

unsafeの許可は入れ子のdeferにも及ぶが、関数境界を越えない。通常の型・所有権・借用検査は維持する。SPEC.md §7.5どおり、`unsafe func` は呼出し元への安全性条件を表し、その関数本体を自動でunsafeにする指定ではない。安全な関数は、未検査のメモリ安全性条件を呼出し元へ暗黙に要求してはならない。

```kimi
unsafe => releaseRaw(pointer)

unsafe
    releaseRaw(pointer)
    updateState()
```

`let value = unsafe => readRaw(pointer)` は、文を初期化子に置くためエラーとなる。

### 4.2. defer

到達したときに、直近の実行スコープの終了処理を登録する。登録時には本体・引数・条件・初期化子を評価せず、参照する変数もCopy・Moveしない。

未到達の登録は実行しない。反復ごとの登録はその反復の終了時に実行する。内側のdeferは、外側のdeferの実行中に登録し、その本体の終了時に実行する。

本体の正常完了とdefer宛てexitは、§5.3に従って残る後始末を再開する。defer宛てexitは本体だけを終了し、内側に反復があればラベルなしexitは先にその反復を指す。外側への転送禁止は§2.2に従う。

名前と有効な型は登録位置で解決し、実行時の値を使う。後の宣言や型の絞り込みで解釈し直さない。初期化・Move・Loanは、実行到達性が候補とするすべての後始末経路で検査する。deferが後で使う値を先にMoveして使用不能にするコードはエラーとなる。

```kimi
var count: i32 = 1
let saved = count
defer => log(saved)
defer => log(count)
count = 2
// スコープ終了時は2、1の順に出力
```

反復ごとの登録と、defer本体の早期終了は次のように書く。

```kimi
for item in items
    defer => finish(item)
    process(item)

defer
    if alreadyClosed() => exit
    flush()
    close()
```

`if shouldClose => defer => close(resource)` はif本体に登録するため、trueならif終了時にcloseを実行する。外側の終了時まで待つ場合は、外側にdeferを置く。登録位置での条件値を使いたい場合は、先に保存する。

```kimi
let closeAtEnd = shouldClose
defer
    if closeAtEnd => close(resource)
use(resource)
```

登録直後にスコープを離れるdeferも有効であり、それだけを理由とする警告や通常の呼出しへの自動置換は要求しない。

### 4.3. require

条件を一度評価し、trueなら後続へ進み、falseなら必須の失敗本体を実行する。

**失敗本体は、必要な後始末も含め、実行到達性でrequire直後へ正常に進む候補を持ってはならない。** 条件がtrueでも、失敗本体への進入を仮定して独立に検査する。外側への転送、Neverを返す呼出し、非終了、Abortなどが要件を満たす。require自身に暗黙のAbort・例外・returnはない。

結果・ラベル・転送対象・探索境界は持たない。requireはyieldの対象にならないため、ifへの単純な書き換えでは定義しない。defer内の `require needed else => exit` は、そのdeferを終了する。内部の構文が転送を受け取った場合は、その後の継続も追う。

```kimi
require ready else => return

require valid else
    logFailure()
    return
```

次は、失敗本体が正常に後続へ進む場合と、外側へ転送する場合の比較である。

```kimi
require true else => logFailure() // エラー：正常に後続へ進む

require ready else
    loop => exit // エラー：内側のloopの後、正常に後続へ進む

let result = if enabled
    require ready else => yield 0 // 外側のifへ転送
    yield calculate()
else => 0
```

## 5. 静的検査とスコープ終了

### 5.1. 結果型と正常完了

#### 5.1.1. 構造的完了と共通経路

構造的完了は、正常完了と対象付き転送の候補を次表で合成する。項目列に正常完了の候補があることを「構造上の末尾到達」と呼ぶ。非終了は処理を終えず、Abortはプロセスを終了するため、どちらも正常完了を供給しない。

条件値・boolリテラル・定数伝播・Pattern包含・呼出し先本体の解析では経路を除外しない。型の絞り込みで条件の成否が分かるように見える場合も同じとする。

| 構文 | 経路の合成 |
| --- | --- |
| 通常の式・項目列 | 評価順序を守り、正常完了する場合だけ次へ進む |
| if / require | 条件の評価後、true・falseの両側を候補とする |
| match | 対象の取得後、全アームを候補とする。各ガードはtrue・falseの両側を考慮し、評価自体が正常完了しない経路では、その先へ進まない。網羅性により未一致の終了経路は加えない |
| and / or | 左辺の評価後、右辺を評価する場合と省略する場合を候補とする |
| for / while | 対象の取得・条件の評価後、本体を実行する場合と、反復せず終了する場合を候補とする |
| loop | 本体末尾・自分宛てcontinueで反復し、自分宛てexitだけで正常終了する |
| return / exit / continue / yield | オペランドがあれば先に評価してから転送する。評価元の後続には進まず、対象が受け取った後の処理へ進む |
| block / unsafe | 本体に従う。blockは自分宛てexitを受け取る |
| 宣言・defer登録 | 必要な初期化・取得が正常完了すれば次へ進む。関数・deferの本体は、その宣言・登録時には実行しない |

Neverを返す呼出しとAbortの後には正常に進まない。構造的完了では、スコープ終了時の後始末による配送阻止を反映しない。

```kimi
func direct() -> i32
    return 1 // 末尾のUnitは加えない

func incomplete() -> i32
    if true => return 1
// エラー：false側の候補から末尾へ進み、Unitが加わる

func shortCircuit(flag: bool) -> i32
    flag and (return 1)
// エラー：右辺を省略する候補から末尾へ進み、Unitが加わる
```

#### 5.1.2. 結果源・対象結果型・Never

**結果源**は、対象に値を供給する箇所である。対象ごとに本体形式・評価コンテキスト・転送先を確定してから、次表で収集する。構文・名前・転送制限・各操作の型・matchの網羅性は、到達不能でも検査する。

| 結果源 | 収集するもの |
| --- | --- |
| 明示転送 | 対象宛てのすべてのreturn・yield・exitのオペランド。値省略はUnit |
| 単一本体 | §1.4で値を結果にする式。文や値の破棄によってUnitを供給する本体は、構造上正常完了する場合のUnit |
| インデント本体 | 構造上の末尾到達が、所属構文の結果になる場合のUnit |
| 本体以外の暗黙の終了 | ifの省略elseのUnit、およびforの要素終了・whileの条件falseによる構造上の終了のUnit |

各本体への進入を仮定して収集し、到達不能な本体も調べる。項目列が外側へ転送した後に、その列の末尾Unitは加えないが、後に書かれた結果源は検査する。内側で受け取った転送は、§5.1.1に従って後続も調べる。

**対象結果型**は、結果源を適合検査する型である。次の順序で決め、結果源の走査順では変えない。

1. 宣言型・§1.4の固定型を優先し、なければ外側の固定した期待型を使う。
2. 型が未定なら、独立に型を求められる結果源の制約をまとめて集め、一意の共通型を求める。Neverは具体型の候補にしない。
3. 確定した型を、未確定のリテラルなど、その型で検査できる結果源へ伝える。他の型情報を使い切ってから、通常の数値リテラルの既定型を適用する。
4. すべての結果源を適合検査する。未確定の呼出し・匿名関数・空リテラルなどの解決に必要な型が決まらなければ、型注釈や明示型引数を要求する。

結果源として入れ子になった式にも、同じ期待型伝播を適用する。数値リテラルの既定型に依存する内側の結果型は、外側から伝えられる制約を処理するまで確定しない。括弧・ラベル・本体の入れ子だけでは既定型を確定しないが、変数の宣言などで既に確定した型を、後の使用から変更しない。呼出し・匿名関数の推論境界は§5.1.3を維持する。

結果源がない場合や、型が確定したNeverの結果源しかない場合は、対象結果型が未定でも、後述のNever判定へ進める。型の解決に失敗した結果源を「結果源なし」として扱わない。

型を伝えるのはその対象の結果源だけとし、途中で捨てる値や別の対象への転送には伝えない。既に型の決まった数値間の暗黙変換、共通基底型の探索、候補ごとの本体の再検査は行わない。Semantics・Originの適合はSPEC.mdの通常の規則に従い、未解決の結果源同士を推測で組み合わせない。呼出しと匿名関数の境界は§5.1.3に従う。

実行到達性や後始末によって結果源を追加・除外しない。ただし各式の型は、その位置で有効な絞り込みを使う（§5.2）。結果源の収集と、フロー情報を使う各式の型検査を区別する。

```kimi
let large: i64 = 10
let first = if ready => 1 else => large
let second = if ready => large else => 1
// 両方i64。リテラル1を先にi32へ確定しない

let nested = if ready
    yield if cached => 1 else => 2
else => large
// i64。外側の制約を内側のifへ伝えてからリテラルを確定する

let small = if cached => 1 else => 2 // この宣言でi32に確定
let invalid = if ready => small else => large // エラー：i32とi64

let conflicting = loop
    continue
    exit 1
    exit "x"
// エラー：到達不能なexit同士も、共通の型に適合させる
```

**式の型**は、外側の期待型との適合検査に使う。通常は対象結果型と同じだが、結果源をすべて検査した上で、次の両方を満たす場合はNeverとする。

- 結果源がない、またはNeverの結果源だけである。
- 構造的完了に正常完了の候補がない。

期待型や固定の対象結果型だけを理由に、値の供給を作らない。Neverは通常の適合規則に従い、任意の具体型を単独では推論しない。非Neverの結果源があれば到達不能でも制約を保ち、推論不能・結果不足・型不一致をNeverで救済しない。

```kimi
let a = loop => continue // 初期化式はNever
let b: i32 = loop => continue // 初期化式はNever、bの宣言型はi32
// それぞれ許可するが、初期化は完了しない
```

**実行到達性の結果で、式の型をNeverへ置き換えない。** 後始末で配送が止まっても、構造から決めた型を維持する。

関数の固定した戻り型は、本体が正常完了しなくても変えない。戻り型を推論する匿名関数では、上記の条件を満たす場合にNeverを推論する。関数値自体はFunction ItemまたはClosureの型を持つ。init本体のUnitは構築操作の所有値とは別であり、deinitのreturnも自動フィールド破棄を省略しない。

#### 5.1.3. 関数本体と期待型の確定

SPEC.md §10.5・§10.8の推論境界を維持する。名前付きの総称関数は定義時の宣言型と制約で本体を検査し、具体化時に再検査しない。例えば `func make<T>() -> T => 123` は任意のTに適合せず、TをUnitにした呼出しでも救済しない。

匿名関数の本体は、次の順で検査する。

1. 明示した型、独立に型を求められる他の引数、総称引数の制約を先に処理する。引数の記述順には依存させない。
2. 明示シグネチャ、残る候補に共通の期待シグネチャ、または一意に選ばれた候補の期待シグネチャを使う。未解決の複数候補があり、必要な期待シグネチャを確定できない場合は、本体を調べて候補を選ばず注釈を要求する。
3. その時点の正規化した戻り型がUnitなら、§1.4に従って単一本体の値を捨てる。戻り型を推論する場合はValue Contextのまま検査する。検査後のUnit推論や別候補への切替で、本体・captureを再解釈しない。

本体検査前に他の引数から確定したUnitも使える。型の由来では区別しないが、既に型が確定した関数値の戻り値をUnitへ変換しない。期待型のない独立した匿名関数は、§1.4どおり本体から戻り型を推論できる。

```kimi
func run(action: () -> ()) => action()
func run(action: () -> i32) => action()

run(func () => compute()) // computeはi32。期待シグネチャが一意でなくエラー
run(func () -> i32 => compute()) // 戻り型の明示で2番目を選ぶ

func apply<U>(value: U, action: () -> U) -> U => action()
apply((), func () => compute())
// valueからUがUnitに確定した後、本体の値を捨てて検査する
```

本体内にreturnを書いても、それだけでrunの候補を選ばない。値を使う適合と捨てる適合の順位は追加せず、SPEC.md §10.4の比較に進む前に期待型の境界を満たすことを要求する。

#### 5.1.4. 実行到達性と状態

実行到達性は、§5.1.1の経路に実行状態と後始末を反映する。各経路で登録されたdefer・初期化済みの値の破棄・受け取った転送の後続を追い、初期化・Move・Loan・絞り込みとrequireの非継続を検査する。SPEC.md §14.9.2のboolリテラルによる経路除外は適用しない。

```kimi
var port: i32
while true
    port = tryBind()
    if port > 0 => exit
use(port) // エラー：条件falseで一度も代入しない経路がある

var boundPort: i32
loop
    boundPort = tryBind()
    if boundPort > 0 => exit
use(boundPort) // 許可：すべての終了経路で初期化済み
```

`while true` と括弧で囲んだ同じ条件には、無条件反復を意図するならloopを使うよう警告する。警告自体はコードを拒否する理由にしない。`require ready else => while true => ()` は正常終了の候補を持つためエラーとなり、非継続には `loop => ()` などを使う。

consumeが非Copyのvalueを消費する場合、`if false => consume(value)` も静的にはMoveする経路を含む。これによる使用エラーでは「通常のifは、実行時にfalseでも静的検査から除外しない」と説明し、除外を意図した場合はSPEC.md §19の `#if` を案内する。型検査後の最適化は許可するが、最適化設定で受理・拒否を変えない。

### 5.2. 条件・一時値・型の絞り込み

#### 5.2.1. 条件と一時値

if・else if・while・require・matchガードの条件はbool式とする。`if let ...` など、条件そのものにlet・var・Pattern束縛を書く形式は認めない。条件式に含まれる別の構文の本体には、通常の本体規則を適用する。

```kimi
if (test: block
    let ready = check() // checkはboolを返す。block内の宣言は許可
    exit to test: ready
)
    work()
```

各判定は共通の順序で行う。

1. 条件を評価し、正常なbool結果を確保する。
2. 条件評価で生じた、破棄責任の残る一時値を逆生成順で破棄する。ガード固有の一時的なLoanも終了する。
3. 確保したboolに従い、本体・次の条件やアーム・後続・反復終了へ進む。

後始末の効果は状態に反映するが、確保したboolは再評価しない。条件や後始末が転送・非終了・Abortで終われば、それ以降の通常の判定処理は行わない。

match対象・iterator・明示的な束縛は条件の一時値ではなく、それぞれの所有スコープに従う。Moveした値は取得先の寿命に従う。本体まで保持する値は外に保存し、毎回新しく束縛して判定する場合はloopを使う。

```kimi
let guard = registry.lock()
if guard.contains(id) => work()

loop
    let ready = check()
    require ready else => exit
    work(ready)
```

この規則はSPEC.md §3.6.2のif条件の延命を変更する。他の一時値の寿命は同節を維持する。

#### 5.2.2. 型の絞り込みと到達不能な項目

型の絞り込みはSPEC.md §14.10に従う。適格なobject型の変更不可の束縛・引数に対し、is / is not・短絡評価・分岐から得た保証を使う。var・メンバー・添字・呼出し結果への一般化や、別名への事実の転写は行わない。

requireの後続にはtrue側の状態を渡す。合流では、実行到達性が候補とする全経路の共通の保証だけを残す。反復の入口・continue・終了も合流し、初期化・Move・Loanの状態を無視して型だけを絞り込まない。

実行到達性で進入経路を持たない領域も、**型検査用の継続**として構文順に検査する。直前までの有効な型情報と、囲む条件から得た絞り込みを引き継ぎ、到達不能になったことだけを理由に宣言型へ戻さない。内部の条件・require・合流には通常の絞り込み規則を使い、合流では共通の保証だけを残す。

returnなどの後も、その位置の字句スコープで有効な型情報を次の項目へ渡す。転送直後の型検査用の継続には、オペランドの評価・取得後、転送に伴うスコープ終了前の状態を使う。代入・Move・破棄などで既に無効になった事実は引き継がない。未初期化・Move済みの値や終了したLoanを復活させず、関数境界にも外側の絞り込みを持ち込まない。

型検査用の継続は、構造的完了や実行到達性の経路へ加えず、暗黙Unitやrequireの正常な後続も作らない。そこで検査した状態を到達可能な経路へ合流させないが、結果源の型は§5.1.2の検査に含める。

```kimi
func scoreInBranch(animal: objref/Animal) -> i32
    if animal is Dog
        return 0
        return animal.score() // 到達不能でもDogの絞り込みを保持
    return 0

func scoreAfterReturn(animal: objref/Animal) -> i32
    return 0
    require animal is Dog else => return 1
    return animal.score() // 到達不能な領域内でもrequireで絞り込む
// Dogにscore() -> i32があれば、両方の呼出しは型として成立する
```

### 5.3. 後始末と結果の配送

正常完了と制御転送のどちらでも、実際に離れるスコープを内側から順に後始末する。各スコープでは、ローカル宣言とdeferを合わせたソース順の逆順に処理する。登録済みのdeferと、初期化済みで破棄責任の残る値だけを処理し、Move済みの値や借用先を重ねて破棄しない。

```kimi
let first = makeResource("first")
defer => log("A")
let second = makeResource("second")
defer => log("B")
// B → secondの破棄 → A → firstの破棄
```

引数・反復束縛・Pattern束縛・iterator・match対象などの位置と破棄責任は、SPEC.md §16.2および各構文の所有スコープに従う。

転送の値と、構文の結果になる単一本体の式は、**評価・CopyまたはMoveによる確保 → 一時値と離れるスコープの後始末 → 対象への配送**の順に扱う。対象の結果を捨てる場合も、配送後に通常の規則で破棄する。本体で直接捨てる値は、通常の式の寿命に従う。

```kimi
func answer() -> i32
    var value: i32 = 1
    defer => value = 2
    return value // Copyで確保済みの1を返す
```

オペランド中に別の転送が起きれば、元の転送は実行しない。defer宛てexitは、その本体の後始末を終えて保留中の処理を再開し、確保済みの戻り値や転送先を置き換えない。返す借用は、後始末の後も有効でなければならない。

後始末が非終了なら、残りの後始末と結果配送へ進まない。Abortはプロセスを終了し、通常のスコープ終了処理を行わない。

### 5.4. 値の破棄に関する診断

診断は型・実行結果・オーバーロードの選択を変更しない。同じ箇所のResult破棄などの警告とは重複を避ける。

#### 5.4.1. 意図しないUnit推論

次の条件をすべて満たす場合、結果を返し忘れた可能性を警告する。

- 結果を使う構文、または戻り型を推論する匿名関数で、結果型をUnitと推論した。
- 宣言・構文規則・期待型によってUnitに決まったわけではない。戻り型を省略した名前付き関数も対象外とする。
- Unitを供給するインデント本体に構造上の末尾到達があり、その末尾で非Unit・非Neverの値を捨てている。

末尾の検査は、括弧とラベルを通過し、if・match・blockなら構造上正常完了する本体の末尾へ再帰的に進む。単一本体ではその式、インデント本体では最後の項目を調べる。これらの構文自身がDiscard ContextでUnitになっていても、内部で捨てる値を確認する。明示した `()`、転送、反復、文、別の関数の内部へは進まない。

```kimi
let total = block
    if useCache => loadCached()
    else => compute()
// 両関数がi32を返す場合、totalはUnitと推論されるが警告する

let corrected = if useCache => loadCached() else => compute()
// 値を使う位置にifを置けば、各本体の値が結果になる
```

診断は対象に応じてyield・return・名前付きexit、または単一本体を案内する。

#### 5.4.2. 作用のない式の破棄

Discard Contextで非Unit値を捨てる式は、評価・取得・破棄を通して観測可能な作用も所有権状態の変更もないと確認できる場合に警告する。戻り型がUnit固定の関数も対象とする。

リテラル、Copyのローカル名、組込み演算・比較、Case構築、Tupleなどを調べる。内部の呼出し、ユーザー定義比較、Move・Loan、破棄、Abort・非終了の可能性も含めて判定する。呼出しや破棄処理の本体は解析せず、作用がないと確認できなければ、この警告を出さない。

```kimi
func isAdult(age: i32) => age >= 18
// 警告：boolを捨てている。結果を返す意図なら -> bool を明記する

if ready => 1 // 本体の1に警告
func answer() => 42 // 警告
func cleanup() => handle.close() // 呼出しの無作用を仮定しない
```

何もしない本体を表す `()` には警告しない。診断では戻り型の明示や値の使用を案内し、式を自動削除しない。

## 6. 文法と関連仕様

### 6.1. 文法の骨格

`?` は省略可能、`*` は0回以上を表す。各式の区切りと改行は§1.3に従う。

```text
Body<Item>         := "=>" SingleItem | NEWLINE INDENT ItemList<Item> DEDENT
SingleItem         := Expression | Statement
Statement          := UnsafeStatement | DeferStatement | RequireStatement
IfExpression       := "if" Expression Body (BranchJoin "else" "if" Expression Body)*
                      (BranchJoin "else" Body)?
MatchExpression    := "match" Expression NEWLINE INDENT MatchArmList DEDENT
MatchArm           := Pattern ("if" Expression)? Body
ForExpression      := "for" ForBinding "in" Expression Body
WhileExpression    := "while" Expression Body
LoopExpression     := "loop" Body
BlockExpression    := (Name ":")? "block" Body
LabeledSelection   := Name ":" (IfExpression | MatchExpression)
LabeledIteration   := Name ":" (ForExpression | WhileExpression | LoopExpression)
UnsafeStatement    := "unsafe" Body
DeferStatement     := "defer" Body
RequireStatement   := "require" Expression RequireJoin "else" Body
ReturnExpression   := "return" Expression?
ExitExpression     := "exit" (Expression? | "to" Name (":" Expression)?)
ContinueExpression := "continue" ("to" Name)?
YieldExpression    := "yield" (Expression? | "to" Name (":" Expression)?)
```

`Body` は、その位置で許された項目を持つ `Body<Item>` の略である。ItemListとMatchArmListは非空とし、BranchJoin・RequireJoinは§1.3の句の接続を表す。条件・ガードはbool式、Pattern・ForBindingは§3の制限に従う。blockは予約語とし、toの文脈キーワードとしての解釈は§2.3に従う。

関数・匿名関数・特殊化・アクセサー・init・deinitの各本体も、対応する `Body<Item>` に置き換える。

### 6.2. SPEC.mdとの対応

| SPEC.mdの箇所 | 本書での変更 |
| --- | --- |
| §2・付録F：構文とレイアウト | §1.2・§1.3・§6.1の共通Bodyへ置換。blockを予約語へ追加。旧InlineStatement・本体用コロン・`=>` だけの改行継続を廃止 |
| §6・§7・§11・§16.3：関数など | 共通Bodyと§1.4のUnit固定本体の破棄規則を適用。宣言した戻り型・制約節の位置・構築完了・自動フィールド破棄の要件は維持 |
| §3.1.5・§3.8・§14.1・§14.9：型と正常完了 | §1.5・§5.1の共通解析へ置換。全結果源の型検査と、実行到達性による型の置換禁止を適用 |
| §10・§12.3.1：推論とオーバーロード | §5.1.2で入れ子を含む結果源の推論順を明記。§10.5・§10.8の境界は維持し、§5.1.3の順で期待型を確定。値の破棄による候補順位は追加しない |
| §14.2〜§14.8：結果と転送 | §1.4・§2・§3へ置換。末尾Unit、常に網羅的なmatch、任意ラベルのblock、名前付き転送の前置ラベルとコロンを適用 |
| §14.9.2・§14.10・§15・§16：経路と実行状態 | boolリテラルによる経路除外を廃止。§5の共通経路で検査し、到達不能領域は§5.2.2の型検査用の継続で扱う |
| §14.5：yield | 反復を通過する。ラベルなしyieldの使用位置と探索境界は§2.2に従う |
| §3.6.2・§14：条件 | 条件そのものの束縛形式とif条件の一時値延命を廃止。入れ子の本体には通常の宣言規則を適用し、条件評価は§5.2の共通順序へ置換 |
| §14.11・§16：require・defer | 共通Bodyを適用。非継続要件・defer境界・終了処理は§4・§5に従う |

本体以外の型注釈・名前付き引数・辞書・initの `: base(...)` や、引数名・Originの `=>` は変更しない。未変更の所有反復、Pattern・ガード、型の絞り込み、借用、一時値、破棄の詳細は、本文で参照したSPEC.mdの規則に従う。

旧 `exit value from label`、`exit value to label`、`exit to label value` は、`exit to label: value` へ移行する。yieldも同様とする。構文の置換だけでなく、結果型・転送先・条件一時値の寿命も確認する。
