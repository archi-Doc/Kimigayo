# 仕様変更案：言語サーバーの semantic tokens

日付：2026-10-02（2026-10-03 最終版）

状態：最終版。採否は未決定。正式仕様への取り込みと実装は未実施。

本書は、言語サーバー（SPEC §23.4）に **semantic tokens** を加え、コンパイラーが確定した宣言・参照・文脈キーワードの分類を編集器へ渡す。分類は検査結果の一部とし、文書の現在の状態に対する検査がそろってから応答する。本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。

VS Code 拡張 kimi-ext の TextMate 文法は拡張の一部であり、仕様ではない。本書の分類は、クライアントの構文ハイライトを補うものとして使える。

## 1. 現仕様の問題

1. **色分けが推測に限られる。** TextMate 文法は、文脈キーワード（`during`、`of`、`get` など）を出現位置で、型を UpperCamelCase の命名規約で推測する。enum Case の宣言は型に見え、struct・enum・Contract も、ローカル・パラメーター・Field も区別できない。
2. **コンパイラーの判断を渡す経路がない。** SPEC §23.4.1 が広告するのは文書の同期だけで、Appendix D は semantic tokens を保留している。
3. **暗黙の事実が見えない。** 排他受信者の暗黙の取得（SPEC §7.3）や unsafe 関数の呼出しは、呼出し側の字面からは分からない。
4. **二つの近似がずれる。** 文法とパーサーを別々に保守すると、構文の変更が文法に届かない。

## 2. 方針

1. 分類は検査結果の一部とする。利用者に見える役割（宣言・参照・語）と、それを確定する根拠（構文解析・名前解決）を分けて定める。
2. 応答は、文書の現在の状態に対する検査がそろってから返す。応答は確定した分類だけからなり、一つの revision に対応する。
3. 確定しない分類は返さず、クライアントの構文ハイライトに任せる（SPEC §23.2 の原則）。
4. 要求は検査を起こさず、構文解析もしない。
5. 構文ハイライトを持つクライアントには、コンパイラーだけが決められる分類を返す。それ以外のクライアントには、字句の分類も返す。

## 3. 仕様の変更点

### 3.1. 機能の広告（SPEC §23.4.1）

- `initialize` の `textDocument.semanticTokens` で、`requests.full` が `true` またはオブジェクトであり、`formats` が `relative` を含むとき、`semanticTokensProvider` を広告する。内容は本書 3.2.5 の legend と `full: true` で、`range` と `full/delta` は広告しない。機能は初期化時に固定する。
- `$/cancelRequest` を処理する（本書 3.2.2）。それ以外の未知の通知は、従来どおり無視する。

### 3.2. Semantic tokens（新設 SPEC §23.4.8）

Publication（§23.4.7）の後に新設し、現在の §23.4.8 と §23.4.9 を一つずつ繰り下げる。`2026-10-02 Structured Repair Candidates.md` を先に取り込んで code action の節が §23.4.8 になる場合は、本節をその次に置く。

#### 3.2.1. 分類の記録

- 検査は、報告先 URI（§23.4.7）になる各ソースについて、本書 3.2.6 の分類を結果に含める。文書が開いているかどうかによらない。
- 字句の分類（本書 3.2.7）は、それを返すセッションでだけ記録する。
- 構文解析で確定する分類は、ソースの内容と構文解析の設定が同じなら、結果の間で共有してよい。
- 分類は結果と同じ寿命で保持され、§23.4.6 の Retention に従う。

#### 3.2.2. 要求と応答

- 対象は開いているソース文書（§23.4.2）だけである。それ以外への要求には `null` を返す。要求は、受け取ったときのその文書の open から close までの区間に属する。
- 文書 D への要求には、次の両方を満たしたときに応答し、満たすまで保留する。
  1. base が D の最後の文書イベント（§23.4.5 の open・change）の番号以上であるワークスペース検査（§23.4.6）が完了している。開いた直後や変更の直後に、発見と必要単位の導出が D の現在の状態を見る前には応答しないためである。
  2. D の寄与者（§23.4.7）と、D をメンバーに持つ必要単位（§23.4.4）が、すべて有効な結果（§23.4.5）を持つ。
- 両方を満たしても分類を持つ結果がなければ（Blocked だけの場合を含む）、分類がないことが確定しているので空で応答する。同期が外れた D は Blocked の結果しか持たないので、空になる。
- 応答は、寄与者の結果の分類を本書 3.2.3 で統合したものである。有効な結果は D の現在の revision を記録しているので、応答は D の現在のテキストに対応する。
- 保留中の要求は、次のときに終わる。

  | 出来事 | 応答 |
  | --- | --- |
  | D が変わった | `-32801`（ContentModified） |
  | D が閉じられた | `null` |
  | `$/cancelRequest` | `-32800`（RequestCancelled） |
  | `shutdown` | `null` |

- 要求は入力イベントではない。mark を付けず、静穏期間を延ばさず、検査を起こさない。

#### 3.2.3. 統合

- 構文解析で確定する分類は、同じ内容からはどの結果でも同じである。
- 名前解決で確定する分類は、その span を分類した結果について、種別と修飾子を別々に統合する。
  - 種別がすべて同じならその種別を使い、修飾子はすべての結果に共通するものだけを残す。
  - 種別が一つでも異なれば、確定しないものとして返さない。
  - その span を分類しない結果（除外した結果、解決に失敗した結果、Blocked の結果）は数えない。
- 修飾子がないことは、その性質がないことを意味しない。提示しないことだけを意味する。

#### 3.2.4. 再取得の依頼

- ワークスペース検査の終わりに、まず保留中の要求について本書 3.2.2 の条件を調べて応答する。
- 次に、クライアントの `workspace.semanticTokens.refreshSupport` が `true` なら、開いている各文書について、寄与者の分類の組（各結果のその文書の分類の内容）が、その文書に最後に応答したときから変わったかを調べる。変わった文書が一つでもあれば、`workspace/semanticTokens/refresh` を一度送る。まだ応答していない文書は調べない。
- 変化の契機は、結果の採用、単位の引退、hold の解除であり、§23.4.7 で URI を再検討する契機と同じである。
- `refreshSupport` が `true` でないクライアントには、他の文書の変更などによる分類の変化は、クライアントが再要求するまで届かない。

#### 3.2.5. Legend

種別と修飾子は閉じた一覧であり、追加には仕様変更を要する。表の順序が legend の添字になる。

| 添字 | 種別 | Kimigayo での対象 |
| --- | --- | --- |
| 0 | `namespace` | group、rootgroup などの名前空間 |
| 1 | `type` | associated Type |
| 2 | `struct` | struct |
| 3 | `enum` | enum |
| 4 | `interface` | Contract |
| 5 | `typeParameter` | Type パラメーター、Semantics パラメーター（`<s/T>` の `s`）、Origin パラメーター（スロット、binding-set 名、暗黙の Origin） |
| 6 | `parameter` | 関数パラメーター、長さパラメーター |
| 7 | `variable` | ローカル束縛（`let`、`var`、`for` とパターンの束縛）、環境束縛（捕捉）、条件の Name（SPEC §19.2） |
| 8 | `property` | Field、Property |
| 9 | `enumMember` | enum Case |
| 10 | `function` | instance function 以外の関数 |
| 11 | `method` | instance function（SPEC §7.3） |
| 12 | `keyword` | キーワード |
| 13 | `modifier` | 組込みの Semantics 名と Semantics category |
| 14 | `decorator` | Attribute 名 |
| 15 | `label` | ラベル |
| 16 | `macro` | `#if`、`#switch`、`#case` |
| 17 | `comment` | コメント |
| 18 | `string` | 文字列リテラル、文字リテラル |
| 19 | `number` | 数値リテラル |
| 20 | `operator` | 演算子 |

| ビット | 修飾子 | 定義 |
| --- | --- | --- |
| 0 | `declaration` | 宣言する Name |
| 1 | `readonly` | `var` で宣言されていない束縛と Field、および条件の Name。パラメーター、`for` の束縛、長さパラメーター、`var` のない環境束縛を含む |
| 2 | `static` | group・rootgroup のメンバー（SPEC §6.1） |
| 3 | `abstract` | Contract の要件 |
| 4 | `modification` | 書き込まれる、または排他的に取得される Place の対象の Name（本書 3.2.6） |
| 5 | `defaultLibrary` | Kimi ライブラリとコンパイラーが提供する宣言と値、`$` 操作 |
| 6 | `documentation` | `///` のコメント |
| 7 | `unsafe` | unsafe 関数（SPEC §7.5） |
| 8 | `controlFlow` | 制御の移動を表すキーワード：`if` `else` `case` `for` `in` `while` `loop` `do` `match` `return` `exit` `continue` `yield` `to` `label` `try` `require` `defer` |

- サーバーは、クライアントの `tokenTypes` と `tokenModifiers` にある種別と修飾子だけを使う。ない種別のトークンは送らず、ない修飾子は落とす。
- `label`（LSP 3.18）、`controlFlow`、`unsafe` は、クライアントが一覧に加えた場合にだけ届く。

#### 3.2.6. 分類の規則

**役割。**

1. **宣言**：宣言する Name は、下の表の種別、`declaration`、および宣言に当てはまる修飾子を持つ。
2. **参照**：Name の参照は、解決先の宣言と同じ種別と修飾子（`declaration` を除く）を持ち、使い方によって `modification` を加える。名前付き引数の名前は、対応するパラメーターの参照である。条件の Name は環境の値の参照であり、組込みの値なら `defaultLibrary` を持つ。
3. **語**：文脈キーワードなど、Name でない語は、パーサーが読んだ役割で分類する。
4. **兼用**：宣言と参照を兼ねる Name（捕捉一覧の名前）は、宣言として分類し、参照の使い方による修飾子を加える。`readonly` は新しい束縛から決める。
5. **暗黙の宣言**：暗黙に導入される Origin（SPEC §15.3.4）は、導入する出現のうちソース順で最初のものを宣言、残りを参照とする。

**根拠。**

- **構文解析で確定する分類**：alias 以外の明示的な宣言とその修飾子、語、捕捉一覧の名前の宣言、条件の Name。除外された構文（§19.5）でも返す。
- **名前解決で確定する分類**：参照、alias の宣言、暗黙の Origin、使い方による修飾子。選択された構文で Binding が確定したものだけを返す。
- 回復で読み飛ばした範囲と、解決に失敗した Name には分類を与えない。

| 宣言 | 種別 |
| --- | --- |
| group、rootgroup | `namespace` |
| struct、enum、contract | `struct`、`enum`、`interface` |
| Type・Semantics・Origin のパラメーター | `typeParameter` |
| associated Type | `type` |
| 関数 | instance function は `method`、それ以外は `function` |
| Field、Property | `property` |
| enum Case | `enumMember` |
| 関数パラメーター、長さパラメーター | `parameter` |
| ローカル束縛、環境束縛 | `variable` |
| ラベル | `label` |
| alias | 解決先の宣言の種別 |

| 語 | 種別 | 修飾子 |
| --- | --- | --- |
| パーサーが文脈キーワードとして読んだ語 | `keyword` | 制御の語なら `controlFlow` |
| 組込みの Semantics 名、Semantics category | `modifier` | なし |
| Attribute 名 | `decorator` | なし |
| `$` の後の名前 | `function` | `defaultLibrary` |

- 文脈キーワードと同じ綴りでも、Name として使えば Name として分類する（パラメーター `during` など）。文脈的な束縛（setter の `value` など）と、受信者でない `self`（SPEC §7.3）も Name である。
- 予約語、受信者の `self`、`Self` は、字句の分類（本書 3.2.7）とする。

**`modification` の対象の Name。** 書込み（代入、複合代入、`++`、`--`。computed Property への代入を含む）と排他取得（`@uniq`、`@uniq` の捕捉、SPEC §7.3 の暗黙の排他受信者）の対象の Place について、その式の形で決める。引数と添字の式の中には入らない。

| 対象の式 | `modification` を付ける Name |
| --- | --- |
| 単純名 `x` | `x` |
| メンバー参照 `a.b` | メンバー名 `b` |
| 添字 `a[i]` | 基底 `a` について、この表を繰り返す |
| 受信者の `self` | `self` |
| それ以外（呼出しの結果など） | なし |

#### 3.2.7. 字句の分類

クライアントの `augmentsSyntaxTokens` が `true` でないときは、字句の分類も返す。`true` のときは返さないが、`modification` を持つトークン（受信者の `self`）は返す。

| 対象 | 種別 | 修飾子 |
| --- | --- | --- |
| 予約語、受信者の `self`、`Self` | `keyword` | 制御の語なら `controlFlow` |
| `#if`、`#switch`、`#case` | `macro` | なし |
| コメント | `comment` | `///` なら `documentation` |
| 文字列リテラル、文字リテラル | `string` | なし。補間の部分で分け、補間の中は通常のコードとして分類する |
| 数値リテラル | `number` | なし |
| 演算子 | `operator` | なし |

#### 3.2.8. 符号化

- 形式は `relative` である。位置は UTF-16 の code unit で数え、改行は §23.4.2 の規則に従う。
- トークンは位置順に並び、重ならない。クライアントの `multilineTokenSupport` が `true` でなければ、複数行のコメントと文字列は行ごとに分ける。

### 3.3. 役割（SPEC §23.1、§23.4.6）

新しい役割は加えない。§23.1 の表の責務に次を加える。

| 役割 | 加える責務 |
| --- | --- |
| Worker | 検査の中で分類を作る（本書 3.2.1） |
| State owner | 保留中の要求の条件と refresh を判定し、寄与者の結果の不変な分類を sender に渡す（本書 3.2.2、3.2.4） |
| Sender | 分類を統合し、符号化する（本書 3.2.3、3.2.8） |

§23.4.6 には、要求は入力イベントではないこと（本書 3.2.2）への参照を加える。

### 3.4. Appendix D と CSP

- Appendix D の「Language-server features beyond diagnostics」の行から semantic tokens を除き、状態を「Not introduced; the server publishes check diagnostics and answers semantic token requests」とする。
- §23.5.3 は変えない。CSP の構文・意味ノードのハンドルは本書の分類より上位の機能であり、CSP が導入されたら、分類は同じノードの事実から作る。

### 3.5. 変更しないこと

- 言語の妥当性、診断、検査、静穏期間、publication の条件と抑制、ディスクの読み方は変えない。
- 色、テーマ、クライアントでの表示は定めない。
- TextMate 文法は仕様の対象外とする。
- 除外された構文を暗く表示する機能、hover、inlay hint は本書に含めない。

## 4. 例

### 4.1. 宣言・参照・修飾子

SPEC §7.3 の例を一つの文書にしたもの（`during` はパラメーターの Name）。

```kimi
struct Meter
    var measured: i32
    func read(self) -> i32 => self.measured
    func update(self: uniq/Self, value: i32) => self.measured = value

func show(during: i32) -> i32
    var meter = Meter.init(during)
    let shown = meter.read()
    meter.update(5)
    return shown
```

| 行 | トークン | 分類 | 役割 |
| --- | --- | --- | --- |
| 1 | `Meter` | `struct` `declaration` | 宣言 |
| 2 | `measured` | `property` `declaration` | 宣言（`var` なので `readonly` なし） |
| 3 | `read` | `method` `declaration` | 宣言（受信者あり） |
| 3 | `measured` | `property` | 参照 |
| 4 | `update` | `method` `declaration` | 宣言 |
| 4 | `uniq` | `modifier` | 語 |
| 4 | `value`（パラメーター） | `parameter` `declaration` `readonly` | 宣言 |
| 4 | `measured`（代入の対象） | `property` `modification` | 参照 |
| 4 | `value`（代入の値） | `parameter` `readonly` | 参照 |
| 6 | `show` | `function` `declaration` | 宣言 |
| 6 | `during` | `parameter` `declaration` `readonly` | 宣言（キーワードではない） |
| 7 | `meter` | `variable` `declaration` | 宣言 |
| 7 | `Meter` | `struct` | 参照 |
| 7 | `during` | `parameter` `readonly` | 参照 |
| 8 | `shown` | `variable` `declaration` `readonly` | 宣言 |
| 8 | `meter` | `variable` | 参照（共有受信者） |
| 8 | `read` | `method` | 参照 |
| 9 | `meter` | `variable` `modification` | 参照（暗黙の排他受信者） |
| 9 | `update` | `method` | 参照 |
| 10 | `shown` | `variable` `readonly` | 参照 |

`func`、`var`、`let`、`i32`、`init`、`return`、受信者の `self`、`Self` は、構文ハイライトを持つクライアントには返さない。

構文ハイライトを持つクライアントへの 6 行目（LSP の行番号 5）の符号化：直前のトークンは 4 行目（行番号 3）の `value` なので、`show` は `[2, 5, 4, 10, 1]`、`during` は `[0, 5, 6, 6, 3]` である（3 は `declaration` と `readonly`）。

### 4.2. 文脈キーワードと Name

SPEC §14 の例。同じ綴りの `label` が、キーワード・ラベル・変数の三つの役割を持つ。

```kimi
let label: bool = true
let accepted: bool = label label: do
    exit to label label
```

| 行 | トークン | 分類 |
| --- | --- | --- |
| 1 | `label` | `variable` `declaration` `readonly` |
| 2 | 1 つ目の `label` | `keyword` `controlFlow` |
| 2 | 2 つ目の `label` | `label` `declaration` |
| 3 | `to` | `keyword` `controlFlow` |
| 3 | 1 つ目の `label` | `label` |
| 3 | 2 つ目の `label` | `variable` `readonly` |

### 4.3. 兼用・暗黙の宣言・条件の Name

```kimi
func pair<T>(x: ref/T during a, y: ref/T during a) -> ref/T during a => x

func count<s/T>(value: s/T) -> i32
    let start = 0
    let next = func [var start] () -> i32
        start += 1
        return start
    #if windows
    log("windows")
    return next()
```

| 行 | トークン | 分類 | 理由 |
| --- | --- | --- | --- |
| 1 | 1 つ目の `a` | `typeParameter` `declaration` | 暗黙の Origin を導入する最初の出現 |
| 1 | 2・3 つ目の `a` | `typeParameter` | 同じ Origin の参照 |
| 1 | `x`（本体） | `parameter` `readonly` | 参照 |
| 3 | `s`（`<s/T>`） | `typeParameter` `declaration` | Semantics パラメーター |
| 3 | `s`（`s/T`） | `typeParameter` | 組込みの Semantics 名ではない |
| 5 | `start`（捕捉一覧） | `variable` `declaration` | 兼用。新しい環境束縛は `var` なので `readonly` なし |
| 6 | `start` | `variable` `modification` | 環境束縛の参照 |
| 8 | `windows` | `variable` `readonly` `defaultLibrary` | 組込みの条件の値 |

`modification` の対象：`items[index] = value` では `items` に付き、添字の `index` には付かない。`self.update()` が `self` を暗黙に排他取得するとき、`self` は `keyword` `modification` として返る。

### 4.4. 応答の時期

| 出来事 | サーバーの動作 |
| --- | --- |
| D を開き、すぐに要求が届く | 保留する。base が D の open 以後の検査が、まだ完了していない |
| その検査が完了し、D の単位の結果が有効になる | 保留中の要求に、統合した分類で応答する |
| D を編集し、クライアントが新しい要求を送る | 保留中の古い要求があれば `-32801` で終え、新しい要求を保留する |
| 静穏期間の後の検査が完了する | 保留中の要求に、新しいテキストに対応する分類で応答する |
| D の単位が読む別の文書 A を編集する | その後の検査の終わりに D の分類の組が変わっていれば、refresh を送る |

## 5. 評価

**メリット。**
- 分類の正本がコンパイラーになり、文脈キーワードと型の推測が確定した分類に置き換わる。
- `readonly`、`modification`、`unsafe` によって、再代入の可否、排他アクセス、unsafe 呼出しがその場で分かる。
- 応答は一つの revision の確定した分類だけからなり、要求ごとの構文解析もない。

**費用。**
- §23.4 に節と legend が増え、種別と修飾子の追加は仕様変更になる。
- 検査結果は、報告先の各ソースの分類を保持する。
- 開いた直後と編集の後は、静穏期間と検査の時間だけ分類が返らない。その間はクライアントの構文ハイライトに任される。

**複雑性。** 分類は「宣言・参照・語」の役割と「構文解析・名前解決」の根拠の組合せで決まり、応答の条件は既存の base・寄与者・有効の規則を使う。新しい役割はなく、増える状態は、結果ごとの分類、保留中の要求、文書ごとの最後に応答した分類の組だけである。

## 6. 既存の提案との関係

- **`2026-10-02 Structured Repair Candidates.md`（提案中）：** 同案も §23.4.1 の広告と §23.4 の新設節を変える。両方を取り込む場合は、広告の項を両方の機能で書き、節番号は取り込み順に決める（本書 3.2）。機能は独立している。
- **`Changes/2026-09-23 Implicit Exclusive Receiver.md`（凍結）：** 同書 6 は、暗黙の取得の記録を semantic token が読むと想定していた。本書は、暗黙の排他受信者に `modification` を付けてこれを実現する。inlay hint は本書に含めない。
- **`Design/2026-09-28 Language Server and Compiler Services.md`（凍結）：** semantic tokens は初版の範囲外とされ、Appendix D で保留された。本書はその保留を解く。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| S1：分類の記録 | 検査の中で、報告先のソースごとに分類を作り、結果に含める。構文解析で確定する分類の共有 | 本書 3.2.6 の各行と本書 4.1–4.3 の例、除外された構文、回復を含む入力、解決に失敗した Name、`modification` の対象の各形の試験が通る。Kimi ライブラリと SPEC の例で、トークンが位置順で重ならない |
| S2：LSP | 広告の条件、応答の条件と保留、各終了の場合、統合、符号化、refresh | `LspProtocolTest` に、能力の各値（`false` と省略を含む）、開いた直後の保留、本書 4.4 の各行、close と同じ URI の再 open、種別と修飾子の統合、単位の引退だけによる refresh、分類が変わらない採用では refresh しないこと、要求が検査の予定を変えないことの試験を加える |
| S3：拡張 | `label`・`controlFlow`・`unsafe` をクライアントの一覧に加える機能、package.json の `semanticTokenModifiers` と `semanticTokenScopes`（`keyword.controlFlow` の代替を `keyword.control` とする） | 統合テストで、実サーバーから `vscode.provideDocumentSemanticTokens` の結果を受け取り、本書 4 の分類を確かめる。本書 9.3 の表示を、VS Code の版とテーマを明記して確認する |
| S4：字句の分類 | 本書 3.2.7。コメントの span の記録を含む | 予約語、コメント、複数行の文字列の分割、補間の分割、受信者の `self` の `modification` の試験が通る |
| S5：評価と閉鎖 | 分類による検査時間と保持量の増加、応答時間の測定。STATUS・PLAN・CODEMAP の更新 | Session 検証が通る。大きなプロジェクトと複数 target での増加量を記録する |

**性能の方針。**

- 分類は、検査がすでに行う構文解析と名前解決の結果を一度たどって作る。要求のたびに構文解析しない。
- 本書 3.2.1 の共有を使い、結果ごとに持つのは名前解決で確定する分類だけにする。
- 各結果のソースごとの分類は、トークンごとに固定長の要素を並べた配列とし、内容のハッシュを付ける。refresh の判定（本書 3.2.4）はハッシュの組を比べるだけで済ませる。
- 統合した配列は、文書の revision と寄与者の結果の組ごとに一度作り、その組が現在のものである間だけ保持する。同時に届いた同じ要求はこれを共有する。符号化は再利用するバッファーに直接書き、トークンごとの割当てをしない。

## 8. 文書更新計画

正式な取り込みでは英語で更新し、仕様に draft への依存を残さない。

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §23.1 | 本書 3.3 の責務 | 取り込み時 |
| SPEC §23.4.1 | 本書 3.1 | 取り込み時 |
| SPEC §23.4.6 | 要求は入力イベントではないことへの参照 | 取り込み時 |
| SPEC §23.4.8（新設）と以降の繰り下げ | 本書 3.2。繰り下げた節へのリンクを更新する | 取り込み時 |
| SPEC の例のセッション（現 §23.4.9） | `initialize` の応答に `semanticTokensProvider` を加え、要求の例を一つ加える | 取り込み時 |
| Appendix D | 本書 3.4 | 取り込み時 |
| draft/INTEGRATED.md | 取り込みの記録 | 取り込み時 |
| STATUS、CODEMAP、PLAN | 実装した範囲 | 実装時 |
| README（Visual Studio Code）、kimi-ext の CHANGELOG | 拡張の対応 | S3 の実装時 |

## 9. 設計判断

### 9.1. 採用しなかった案

**応答の時期と内容**
- **要求ごとに構文解析し、構文で確定する分類をすぐ返す。** 名前解決の分類は検査を待つので、応答に二つの revision の分類が混ざり、編集のたびに参照の分類が抜ける。検査と別の構文解析と、それを行う役割も要る。
- **結果がまだなければ空で応答する。** 開いた直後の空の応答が確定した結果に見え、refresh を使えないクライアントでは更新されない。
- **古い結果の分類を、編集を越えて引き継ぐ。** 別の場所に加えた宣言が解決先を変えることがあり、確定していない分類を確定したものとして示す（SPEC §23.2）。
- **結果を採用するたびに refresh を送る。** 分類が同じでも全体の再取得が起き、逆に単位の引退だけで分類が変わる場合を見落とす。
- **種別と修飾子をまとめて一致を判定する。** 修飾子一つの違いで、確定している種別まで失う。

**分類の範囲**
- **開いている文書だけ分類を記録する。** 閉じている間に作られ、開いた後も有効なまま再利用される結果に分類がない。作り直すにはスケジューラーに例外が要る。
- **`range` や `full/delta` を広告する。** 分類は文書全体で保持済みなので、節約できるのは送信量だけである。測定で必要が示されるまで加えない。
- **除外された構文を修飾子で暗くする。** 構文ハイライトを持つクライアントには予約語やリテラルを送らないので、暗くなる部分が不揃いになる。
- **ローカルの名前を構文だけで解決する。** 名前解決を Binding と別に実装することになり、二つの判断がずれうる。

**分類の規則**
- **「宣言は構文、参照は名前解決」とだけ定める。** 暗黙の Origin（SPEC §15.3.4）や alias の宣言は名前解決で決まるので成り立たない。役割と根拠を分ければ、例外なしに扱える。
- **Type 位置の Name を構文だけで `type` にする。** 修飾の途中の group（`Kimi.Iteration.X` の `Kimi`）や、`[N of T]` の長さパラメーター `N` を誤って型にする。
- **暗黙の排他取得に専用の修飾子を設ける。** 代入や `@uniq` と同じ排他アクセスの一種であり、`modification` に含めれば標準の修飾子一つで済む。
- **`modification` を式の最後の Name に付ける。** `items[index] = value` で、読むだけの `index` に付いてしまう。式の形で決める。
- **語を常に `keyword` だけで返す。** 制御の語とそれ以外の区別を、クライアントの構文ハイライトと共有できない。`controlFlow` を付ければ、両方の種類のクライアントで同じ区別を表せる。

### 9.2. Kimigayo Principles との対応

- **一つの概念に一つの正規形：** 分類の正本をコンパイラーに置き、文法は近似にとどめる。分類は役割と根拠の組合せで決め、修飾子は仕様の概念（再代入、group のメンバー、Contract の要件、排他アクセス、unsafe 関数）で定義する。
- **局所的な推論：** 再代入の可否、排他アクセス、unsafe 呼出しを、その場の分類として示す。
- **明示的な意味論：** 構文が省略した暗黙の排他取得を、言語の規則を変えずに示す。
- **Compiler Server Protocol：** 構文・意味の検査（§23.5.1）の一部を、編集器の標準的な経路で先に提供する。CSP の要件（§23.5.3）は変えない。

### 9.3. 未確認事項

サーバーが保証するのは、確定した分類を本書の規則で返すことだけである。表示上の期待は、VS Code の版とテーマを明記して S3 で確認する。

- **表示（S3）**
  - 要求を保留している間、直前の分類が編集に合わせてずらして表示され続けること。保留中の要求への `-32801` と `-32800` で、表示が消えないこと。
  - クライアントの一覧に加えた `label`・`controlFlow`・`unsafe` が表示に使われること。`semanticTokenScopes` は代替であり、semantic token 用の規則を持つテーマでは、予約語と文脈キーワードの色が異なりうる。
- **保持量（S1、S5）**：報告先の全ソースの分類を保持することによる増加。
