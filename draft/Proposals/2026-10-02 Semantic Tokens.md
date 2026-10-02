# 仕様変更案：言語サーバーの semantic tokens

日付：2026-10-02（2026-10-03 改訂）

状態：草案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書は、言語サーバー（SPEC §23.4）に **semantic tokens** を加え、コンパイラーが確定した宣言・参照・文脈キーワードの分類を編集器へ渡す。分類は検査結果の一部とし、診断と同じ条件で応答する。本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。

VS Code 拡張 kimi-ext 0.0.11 の TextMate 文法は拡張の一部であり、仕様ではない。本書の分類は、その上に重ねて表示される。

## 1. 現仕様の問題

1. **色分けが推測に限られる。** TextMate 文法は、文脈キーワード（`during`、`of`、`get` など）を出現位置で推測し、型を UpperCamelCase の命名規約で推測する。enum Case の宣言は型に見え、struct・enum・Contract も、ローカル・パラメーター・Field も区別できない。
2. **コンパイラーの判断を渡す経路がない。** SPEC §23.4.1 が広告するのは文書の同期だけで、Appendix D は semantic tokens を保留している。
3. **暗黙の事実が見えない。** 排他受信者の暗黙の取得（SPEC §7.3）や unsafe 関数の呼出しは、呼出し側の字面からは分からない。
4. **二つの近似がずれる。** 文法とパーサーを別々に保守すると、構文の変更が文法に届かない。

## 2. 方針

1. 分類は検査結果の一部とする。宣言と語（文脈キーワードなど）は構文で、参照は名前解決で決める。
2. 応答は、その文書の診断を送れる条件（寄与者の結果がすべて有効）がそろうまで待つ。待つ間、クライアントは直前の分類を表示し続ける。
3. 確定しない分類は返さず、クライアントの構文ハイライトに任せる（SPEC §23.2 の原則）。
4. 要求は検査を起こさず、構文解析もしない。
5. 構文ハイライトを持つクライアントには、コンパイラーだけが決められる分類を返す。持たないクライアントには、字句の分類も返す。

## 3. 仕様の変更点

### 3.1. 広告と JSON-RPC（SPEC §23.4.1）

- クライアントが `initialize` で `textDocument.semanticTokens` を宣言し、`requests.full` を持ち、`formats` に `relative` を含むとき、`semanticTokensProvider` を広告する。内容は本書 3.2.4 の legend と `full: true` で、`range` と `full/delta` は広告しない。機能は初期化時に固定する。
- `$/cancelRequest` を処理する（本書 3.2.2）。それ以外の未知の通知は、従来どおり無視する。

### 3.2. Semantic tokens（新設 SPEC §23.4.8）

Publication（§23.4.7）の後に新設し、現在の §23.4.8 と §23.4.9 を一つずつ繰り下げる。`2026-10-02 Structured Repair Candidates.md` を先に取り込んで code action の節が §23.4.8 になる場合は、本節をその次に置く。

#### 3.2.1. 分類の記録

- 検査は、報告先 URI（§23.4.7）になる各ソースについて、本書 3.2.5 の分類を結果に含める。文書が開いているかどうかによらない。
- 除外された構文（§19.5）には、構文で決まる分類だけを含める。回復で読み飛ばした範囲と、解決に失敗した参照には、分類を含めない。

#### 3.2.2. 要求と応答

- 対象は開いているソース文書（§23.4.2）だけである。それ以外の文書への要求には `null` を返す。
- 文書 D への要求には、D の寄与者（§23.4.7）がすべて有効な結果（§23.4.5）を持つときに応答する。これは D の診断を送れる条件と同じである。寄与者がなければ空の分類で応答する。条件がそろうまで、要求は保留する。
- 応答は、寄与者の結果の分類を本書 3.2.3 で統合したものである。有効な結果は D の現在の revision を記録しているので、応答は D の現在のテキストに対応する。同期が外れた D は Blocked の結果しか持たないので、分類は空になる。
- 保留中の要求は、次のときに終わる。

  | 出来事 | 応答 |
  | --- | --- |
  | D が変わった | `-32801`（ContentModified） |
  | `$/cancelRequest` | `-32800`（RequestCancelled） |
  | `shutdown` | `null` |

- 要求は入力イベントではない。mark を付けず、静穏期間（§23.4.6）を延ばさず、検査を起こさない。

#### 3.2.3. 統合

- 構文で決まる分類は、同じテキストからはどの結果でも同じである。
- 名前解決で決まる分類は、その span を分類したすべての結果が同じ分類を与えるときだけ使う。異なれば確定しないものとして返さない。その span を分類しない結果（除外した結果、解決に失敗した結果、Blocked の結果）は数えない。

#### 3.2.4. Legend

種別と修飾子は閉じた一覧であり、追加には仕様変更を要する。表の順序が legend の添字になる。

| 添字 | 種別 | Kimigayo での対象 |
| --- | --- | --- |
| 0 | `namespace` | group、rootgroup などの名前空間 |
| 1 | `type` | associated Type |
| 2 | `struct` | struct |
| 3 | `enum` | enum |
| 4 | `interface` | Contract |
| 5 | `typeParameter` | Type パラメーター、Origin パラメーター（スロット、binding-set 名） |
| 6 | `parameter` | 関数パラメーター、長さパラメーター |
| 7 | `variable` | ローカル束縛（`let`、`var`、`for` とパターンの束縛） |
| 8 | `property` | Field、Property |
| 9 | `enumMember` | enum Case |
| 10 | `function` | instance function 以外の関数 |
| 11 | `method` | instance function（SPEC §7.3） |
| 12 | `keyword` | キーワード |
| 13 | `modifier` | Semantics 位置の Semantics 名と Semantics category |
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
| 1 | `readonly` | `var` で宣言されていない束縛と Field。パラメーター、`for` の束縛、長さパラメーターを含む |
| 2 | `static` | group・rootgroup のメンバー（SPEC §6.1） |
| 3 | `abstract` | Contract の要件 |
| 4 | `modification` | 書き込まれる、または排他的に取得される Place を表す式の最後の Name。代入・複合代入・`++`・`--` の対象、`@uniq`、§7.3 の暗黙の排他受信者 |
| 5 | `defaultLibrary` | Kimi ライブラリとコンパイラーが提供する宣言、`$` 操作 |
| 6 | `documentation` | `///` のコメント |
| 7 | `unsafe` | unsafe 関数（SPEC §5） |
| 8 | `controlFlow` | 制御の移動を表すキーワード：`if` `else` `case` `for` `in` `while` `loop` `do` `match` `return` `exit` `continue` `yield` `to` `label` `try` `require` `defer` |

サーバーは、クライアントが宣言した種別と修飾子だけを使う。宣言されていない種別のトークンは送らず、宣言されていない修飾子は落とす。`label`（LSP 3.18）、`controlFlow`、`unsafe` は、クライアントが宣言した場合にだけ届く。

#### 3.2.5. 分類の規則

共通規則は三つである。

1. **宣言**：宣言する Name は、下の表の種別と `declaration`、および当てはまる修飾子を持つ。
2. **参照**：Name の参照は、解決先の宣言と同じ種別と修飾子（`declaration` を除く）を持ち、使い方によって `modification` を加える。
3. **語**：パーサーが読んだ役割で分類する。

| 宣言 | 種別 |
| --- | --- |
| group、rootgroup | `namespace` |
| struct、enum、contract | `struct`、`enum`、`interface` |
| Type パラメーター、Origin パラメーター | `typeParameter` |
| associated Type | `type` |
| 関数 | instance function は `method`、それ以外は `function` |
| Field、Property | `property` |
| enum Case | `enumMember` |
| 関数パラメーター、長さパラメーター | `parameter` |
| ローカル束縛 | `variable` |
| ラベル | `label` |
| alias | 解決先の宣言の種別。名前解決で決まるので、除外された構文では分類しない |

| 語 | 種別 | 修飾子 |
| --- | --- | --- |
| パーサーが文脈キーワードとして読んだ語 | `keyword` | 制御の語なら `controlFlow` |
| Semantics 位置の Semantics 名と Semantics category | `modifier` | なし |
| Attribute 名 | `decorator` | なし |
| `$` の後の名前 | `function` | `defaultLibrary` |

- 予約語、受信者の `self`、`Self` は字句の分類（本書 3.2.6）とする。
- 文脈キーワードと同じ綴りでも、Name として使えば Name として分類する（パラメーター `during` など）。文脈的な束縛（setter の `value` など）と、受信者でない `self`（SPEC §7.3）も Name である。

#### 3.2.6. 字句の分類

クライアントが `augmentsSyntaxTokens` を宣言しないときだけ、字句の分類も返す。

| 対象 | 種別 | 修飾子 |
| --- | --- | --- |
| 予約語、受信者の `self`、`Self` | `keyword` | 制御の語なら `controlFlow` |
| `#if`、`#switch`、`#case` | `macro` | なし |
| コメント | `comment` | `///` なら `documentation` |
| 文字列リテラル、文字リテラル | `string` | なし。補間の部分で分け、補間の中は通常のコードとして分類する |
| 数値リテラル | `number` | なし |
| 演算子 | `operator` | なし |

#### 3.2.7. 符号化

- 形式は `relative` である。位置は UTF-16 の code unit で数え、改行は §23.4.2 の規則に従う。
- トークンは位置順に並び、重ならない。クライアントが `multilineTokenSupport` を宣言しなければ、複数行のコメントと文字列は行ごとに分ける。

#### 3.2.8. 再取得の依頼

クライアントが `workspace.semanticTokens.refreshSupport` を宣言しているとき、開いている文書を報告先に持つ結果を採用したワークスペース検査の終わりに、`workspace/semanticTokens/refresh` を一度送る。

### 3.3. 役割（SPEC §23.1）

新しい役割は加えない。§23.1 の表の責務に次を加える。

| 役割 | 加える責務 |
| --- | --- |
| Worker | 検査の中で分類を作る（本書 3.2.1） |
| State owner | 応答の条件を判定し、寄与者の結果の不変な分類を sender に渡す（本書 3.2.2） |
| Sender | 分類を統合し、符号化する（本書 3.2.3、3.2.7） |

§23.4.6 には、要求は入力イベントではないこと（本書 3.2.2）への参照を加える。

### 3.4. Appendix D と CSP

- Appendix D の「Language-server features beyond diagnostics」の行から semantic tokens を除き、状態を「Not introduced; the server publishes check diagnostics and answers semantic token requests」とする。
- §23.5.3 は変えない。CSP の構文・意味ノードのハンドルは本書の分類より上位の機能であり、CSP が導入されたら、分類は同じノードの事実から作る。

### 3.5. 変更しないこと

- 言語の妥当性、診断、検査、静穏期間、publication の条件と抑制、ディスクの読み方は変えない。
- 色とテーマは定めない。表示はクライアントが決める。
- TextMate 文法は仕様の対象外とする。
- 除外された構文を暗く表示する機能、hover、inlay hint は本書に含めない。

## 4. 例

### 4.1. 宣言・参照・修飾子

SPEC §7.3 の例を、一つの文書にしたもの（`during` はパラメーターの Name）。

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

| 行 | トークン | 分類 | 根拠 |
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

`self`、`Self`、`func`、`var`、`let`、`i32`、`init`、`return` は予約語または受信者なので、構文ハイライトを持つクライアントには返さない。

6 行目（LSP の行番号 5）の符号化：直前のトークンは 4 行目（行番号 3）の `value` なので、`show` は `[2, 5, 4, 10, 1]`、`during` は `[0, 5, 6, 6, 3]` である（3 は `declaration` と `readonly`）。

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

拡張は `keyword.controlFlow` を TextMate の `keyword.control` に対応づけるので、予約語の `exit` と文脈キーワードの `to` は同じ色になる。

### 4.3. 編集中

4.1 の 9 行目を `meter.update(6)` に変えると、D の寄与者の結果は有効でなくなり、要求は保留される。その間 VS Code は、直前の分類を編集に合わせてずらして表示し続け、書き換えた部分は TextMate の色で表示する。検査結果が採用されると、保留中の要求に応答する。

## 5. 評価

**メリット。** 分類の正本がコンパイラーになり、文脈キーワードと型の推測が確定した分類に置き換わる。`readonly`、`modification`、`unsafe` によって、再代入の可否、排他アクセス、unsafe 呼出しがその場で見える。応答は一つの revision の分類だけからなるので、編集のたびに参照の色が消えることがない。要求ごとの構文解析もない。

**費用。** §23.4 に節と legend が増え、種別と修飾子の追加は仕様変更になる。検査結果は、報告先の各ソースの分類を保持する。新しく書いた部分の分類は、検査結果が採用されるまで遅れる（その間は TextMate の色で表示される）。

**複雑性。** 利用者に見える概念は「宣言・参照・語」の三つの規則だけで、応答の条件は既存の寄与者と有効の規則をそのまま使う。新しい役割や状態はなく、増えるのは結果ごとの分類の一覧だけである。

## 6. 既存の提案との関係

- **`2026-10-02 Structured Repair Candidates.md`（提案中）：** 同案も §23.4.1 の広告と §23.4 の新設節を変える。両方を取り込む場合は、広告の項を両方の機能で書き、節番号は取り込み順に決める（本書 3.2）。機能は独立している。
- **`Changes/2026-09-23 Implicit Exclusive Receiver.md`（凍結）：** 同書 6 は、暗黙の取得の記録を semantic token が読むと想定していた。本書は、暗黙の排他受信者に `modification` を付けてこれを実現する。inlay hint は本書に含めない。
- **`Design/2026-09-28 Language Server and Compiler Services.md`（凍結）：** semantic tokens は初版の範囲外とされ、Appendix D で保留された。本書はその保留を解く。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| S1：分類の記録 | 検査の中で、報告先のソースごとに分類の一覧を作り、結果に含める | 本書 3.2.5 の各行、本書 4 の例、文脈キーワードと同じ綴りの Name、除外された構文、回復を含む入力、解決に失敗した参照の試験が通る。Kimi ライブラリと SPEC の例で、トークンが位置順で重ならない |
| S2：LSP | 広告の条件、応答の条件と保留、変更・取消し・shutdown、統合、符号化、refresh | `LspProtocolTest` に、広告の有無、保留と応答の時期、各終了の場合、寄与者の分類が異なる span、要求が検査の予定を変えないことの試験を加える |
| S3：拡張 | `label`・`controlFlow`・`unsafe` をクライアントの宣言に加える機能、package.json の `semanticTokenModifiers` と `semanticTokenScopes`（`keyword.controlFlow` を `keyword.control` に対応づける） | 統合テストで、実サーバーから `vscode.provideDocumentSemanticTokens` の結果を受け取り、本書 4 の分類を確かめる。VS Code で目視確認する |
| S4：字句の分類 | 本書 3.2.6。コメントの span の記録を含む | 予約語、コメント、複数行の文字列の分割、補間の分割の試験が通る |
| S5：評価と閉鎖 | 分類による検査時間と保持量の増加、応答時間の測定。STATUS・PLAN・CODEMAP の更新 | Session 検証が通る。大きなプロジェクトでの増加量を記録する |

**性能の方針。**

- 分類は、検査がすでに行う構文解析と名前解決の結果を一度たどって作る。要求のたびに構文解析しない。
- 一覧はトークンごとに固定長の要素を並べた配列とし、結果と同じ寿命で保持する。
- 統合した一覧は、文書の revision と寄与者の結果の組ごとに一度作って再利用する。符号化は再利用するバッファーに直接書き、トークンごとの割当てをしない。

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

- **要求ごとに構文解析し、構文で決まる分類をすぐ返す。** 名前解決の分類は検査を待つので、編集のたびに参照の分類が消えて色が切り替わる。応答に二つの revision の分類が混ざり、検査と別の構文解析と、それを行う役割も要る。待つ間はクライアントが直前の分類を保つので、応答を待たせるほうが表示も仕組みも単純になる。
- **古い結果の分類を、編集を越えて引き継ぐ。** 別の場所に加えた宣言が解決先を変えることがあり、確定していない分類を確定したものとして示すことになる（SPEC §23.2）。
- **開いている文書だけ分類を記録する。** 文書を閉じている間に作られ、開いた後も有効なまま再利用される結果には分類がない。分類を作り直すには、スケジューラーに例外が要る。
- **暗黙の排他取得に専用の修飾子を設ける。** 代入や `@uniq` と同じ「排他アクセス」の一種であり、`modification` に含めれば標準の修飾子一つで済む。
- **語を常に `keyword` だけで返す。** VS Code では、semantic token の `keyword` が TextMate の `keyword.control` の色を上書きし、`for` と `in` の色が分かれる。`controlFlow` を付ければ、両方の種類のクライアントで同じ区別を保てる。
- **Type 位置の Name を構文だけで `type` にする。** 修飾の途中の group（`Kimi.Iteration.X` の `Kimi`）や、`[N of T]` の長さパラメーター `N` を誤って型にする。参照はすべて名前解決で分類する。
- **`range` や `full/delta` を広告する。** 分類は文書全体で保持済みなので、節約できるのは送信量だけである。測定で必要が示されるまで加えない。
- **除外された構文を修飾子で暗くする。** 構文ハイライトを持つクライアントには予約語やリテラルを送らないので、暗くなる部分が不揃いになる。
- **ローカルの名前を構文だけで解決する。** 名前解決を Binding と別に実装することになり、二つの判断がずれうる。

### 9.2. Kimigayo Principles との対応

- **一つの概念に一つの正規形：** 分類の正本をコンパイラーに置き、文法は近似にとどめる。分類の規則は「宣言・参照・語」の三つにまとめ、修飾子は仕様の概念（再代入、group のメンバー、排他アクセス、unsafe 関数）で定義する。
- **局所的な推論：** 再代入の可否、排他アクセス、unsafe 呼出しをその場で見えるようにする。
- **明示的な意味論：** 構文が省略した暗黙の排他取得を、言語の規則を変えずに表示する。
- **Compiler Server Protocol：** 構文・意味の検査（§23.5.1）の一部を、編集器の標準的な経路で先に提供する。CSP の要件（§23.5.3）は変えない。

### 9.3. 未確認事項

- 要求を保留している間、VS Code が直前の分類を編集に合わせてずらして表示し続けること。保留中の要求への `-32801` と `-32800` を、表示を消さずに扱うこと（S3 で確認する）。
- クライアントの宣言に加えた `label`・`controlFlow`・`unsafe` が、拡張の `semanticTokenModifiers`・`semanticTokenScopes` と組み合わせて表示に使われること（S3）。
- 報告先の全ソースの分類を保持することによる、検査結果の保持量の増加（S1、S5 で測定する）。
