# 仕様変更案：言語サーバーの semantic tokens

日付：2026-10-02（草案）

状態：草案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書は、言語サーバー（SPEC §23.4）に **semantic tokens** を加え、コンパイラーが確定した名前と文脈キーワードの分類を編集器へ渡す。構文だけで決まる分類（本書 3.3）は検査を待たずに返し、名前解決で決まる分類（本書 3.4）は採用済みの有効な検査結果から返す。本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例は独立した断片である。

前提として、VS Code 拡張 kimi-ext 0.0.11 は TextMate 文法で `.kimi` と Markdown の ```` ```kimi ```` ブロックを色分けする。この文法は拡張の一部であり、仕様ではない。本書は文法を置き換えず、その上に重ねる。

## 1. 現仕様の問題

1. **色分けがテキストからの推測に限られる。** TextMate 文法は正規表現で判定するので、文脈キーワード（`during`、`of`、`get` など）は出現位置で推測し、型は UpperCamelCase の命名規約で推測する。enum Case の宣言は型に見え、struct・enum・Contract・Type パラメーターは区別できず、ローカル・パラメーター・Field も区別できない。
2. **コンパイラーの判断を渡す経路がない。** SPEC §23.4.1 が広告する機能は文書の同期だけで、Appendix D は semantic tokens を「Not introduced」としている。パーサーは文脈キーワードを確定し、Binding は名前の参照先を確定しているが、その結果は診断にしか現れない。
3. **暗黙の事実が見えない。** 排他受信者の暗黙の取得（SPEC §7.3）や unsafe 関数の呼出し（SPEC §23.5.3）は、構文に書かれないか、呼出し側からは見分けられない。Kimigayo Principles の「明示的な意味論」は、省略してよいのは意味が局所的に明らかな場合だけとしている。編集器がその事実を示せれば、局所的に判断しやすくなる。
4. **二つの近似がずれる。** 文法とパーサーは別々に保守されるので、構文の変更が文法に届かないことがある。分類の正本はコンパイラーにあるべきであり、文法は、サーバーがないときの近似にとどめる。

## 2. 理想的な動作

1. サーバーは、開いているソース文書の名前と文脈キーワードを、パーサーが現在のテキストに与えた役割どおりに分類する。検査を待たず、検査を起こさない。
2. 現在のテキストに対して有効な検査結果があれば、参照も、その解決先の種類と修飾子（`readonly`、`defaultLibrary`、`unsafe`、暗黙の排他取得など）で分類する。
3. 確定しない分類は返さない。推測で埋めず、クライアントの構文ハイライトに任せる（SPEC §23.2 の原則）。
4. 要求の処理は state owner を止めず、ディスクを読まず、worker を待たない。
5. クライアント非依存とする。構文ハイライトを持つクライアント（`augmentsSyntaxTokens`）には、コンパイラーだけが決められる分類を返す。持たないクライアントには、予約語・コメント・リテラル・演算子も返す。

## 3. 仕様の変更点

### 3.1. 機能の広告（SPEC §23.4.1）

Capabilities の項に次を加える。

- クライアントが `initialize` で `textDocument.semanticTokens` を宣言し、`requests.full` または `requests.range` を持ち、`formats` に `relative` を含むとき、サーバーは `semanticTokensProvider` を広告する。内容は本書 3.2 の legend、`full: true`（delta なし）、`range: true` である。条件を満たさなければ広告しない。
- 機能は初期化時に固定する。動的登録はしない。

### 3.2. Semantic tokens（新設 SPEC §23.4.8）

Publication（§23.4.7）の後に新設し、現在の §23.4.8（Watching and session settings）と §23.4.9（Example session）を一つずつ繰り下げる。`2026-10-02 Structured Repair Candidates.md` を先に取り込んで code action の節が §23.4.8 になる場合は、本節をその次に置く。繰り下げた節へのリンクは取り込み時に更新する。

#### 3.2.1. 要求と文書

- `textDocument/semanticTokens/full` と `textDocument/semanticTokens/range` に応答する。range の結果は、範囲と交わるトークンだけを含む。
- 対象は開いているソース文書（§23.4.2）だけである。開いていない文書、プロジェクト文書、同期が外れた文書（desynchronized）には `null` を返す。
- 結果は、要求を受け取った時点の文書テキスト、つまり先行するすべての通知を適用した後のテキストに対応する。要求は入力イベントではない。mark を付けず、静穏期間（§23.4.6）を延ばさず、debounce もしない。
- 分類は要求された文書だけを構文解析して得る。検査を起こさず、ディスクを読まず、worker を待たない。名前解決による分類（本書 3.4）は、state owner がすでに保持している採用済みの結果からだけ読む。
- `$/cancelRequest` が、処理を始める前の要求を取り消したら、`-32800` で応答する。始めた処理は完了させて応答する。

#### 3.2.2. Legend

トークン種別と修飾子は閉じた一覧であり、追加には仕様変更を要する。順序が legend の添字になる。

| 添字 | トークン種別 | Kimigayo での意味 |
| --- | --- | --- |
| 0 | `namespace` | group、rootgroup |
| 1 | `type` | 種類を区別しない Type（本書 3.3 の Type 位置、associated Type） |
| 2 | `struct` | struct |
| 3 | `enum` | enum |
| 4 | `interface` | Contract |
| 5 | `typeParameter` | Type パラメーター |
| 6 | `parameter` | 関数パラメーター、長さパラメーター |
| 7 | `variable` | ローカル束縛（`let`、`var`、for とパターンの束縛） |
| 8 | `property` | Field、Property |
| 9 | `enumMember` | enum Case |
| 10 | `function` | Type function と、group・rootgroup・ローカルの関数 |
| 11 | `method` | instance function（SPEC §7.3） |
| 12 | `keyword` | パーサーが認めた文脈キーワード。拡張しないクライアントでは予約語も含む |
| 13 | `modifier` | Semantics 位置の Semantics 名（`ref/`、`@uniq` など）と Semantics category |
| 14 | `decorator` | Attribute 名 |
| 15 | `label` | ラベル |
| 16 | `macro` | `#if`、`#switch`、`#case`（拡張しないクライアントだけ） |
| 17 | `comment` | コメント（拡張しないクライアントだけ） |
| 18 | `string` | 文字列リテラルと文字リテラル（拡張しないクライアントだけ） |
| 19 | `number` | 数値リテラル（拡張しないクライアントだけ） |
| 20 | `operator` | 演算子（拡張しないクライアントだけ） |

| ビット | 修飾子 | 付く対象 |
| --- | --- | --- |
| 0 | `declaration` | 宣言する Name |
| 1 | `readonly` | `let` で宣言された束縛と Field、長さパラメーター、およびそれらへの参照 |
| 2 | `static` | `static` 宣言とその参照 |
| 3 | `abstract` | Contract の要件と、要件として解決された参照 |
| 4 | `modification` | 代入・複合代入・`++`・`--` の対象になる参照 |
| 5 | `defaultLibrary` | Kimi ライブラリとコンパイラーが提供する宣言への参照、`$` 操作 |
| 6 | `documentation` | `///` ドキュメント（拡張しないクライアントの `comment` だけ） |
| 7 | `unsafe` | unsafe 関数の宣言名とその呼出し（SPEC §5、§23.5.3） |
| 8 | `implicitExclusive` | 排他受信者として暗黙に取得される受信者式（SPEC §7.3）の最後の Name |

`unsafe` と `implicitExclusive` は LSP の標準にない。サーバーは、クライアントが `tokenTypes` で宣言した種別のトークンだけを送り、宣言していない修飾子は落とす。標準外の種別と修飾子は、クライアントが宣言した場合にだけ届く。

#### 3.2.3. 符号化

- 形式は `relative` である。位置は UTF-16 の code unit で数え、改行は §23.4.2 の規則に従う。
- トークンは位置順に並び、重ならない。一つのトークンは一行に収まる。クライアントが `multilineTokenSupport` を宣言しなければ、複数行のコメントと文字列は行ごとに分ける。
- 同じ span に複数の分類が当てはまるときは、本書 3.4 が 3.3 に優先する。

### 3.3. 構文による分類

文書を一つだけ構文解析した結果から決まる分類である。解析は検査と同じパーサーで行い、除外された構文（§19.5）も同じ文法で解析する。回復で読み飛ばされた範囲にはトークンを返さない。

| 構文 | 種別 | 修飾子 |
| --- | --- | --- |
| group・rootgroup・struct・enum・contract の宣言名 | 本書 3.2.2 の対応 | `declaration` |
| Type パラメーターの宣言 | `typeParameter` | `declaration` |
| 長さパラメーターの宣言（`length N`） | `parameter` | `declaration`、`readonly` |
| associated Type の宣言名 | `type` | `declaration` |
| 関数の宣言名 | 受信者があれば `method`、なければ `function` | `declaration`。`static` 宣言なら `static`、`unsafe func` なら `unsafe` |
| Contract の要件の宣言名 | `method` または `function` | `declaration`、`abstract` |
| Field の宣言名 | `property` | `declaration`。`let` なら `readonly` |
| computed・property の宣言名 | `property` | `declaration` |
| enum Case の宣言 | `enumMember` | `declaration` |
| 関数パラメーター（`self` を除く） | `parameter` | `declaration` |
| ローカル束縛 | `variable` | `declaration`。`let` なら `readonly` |
| ラベルの宣言と、`exit`・`continue`・`yield` の `to` の後の参照 | `label` | 宣言には `declaration` |
| 先頭のドットで書いた Case 参照（`.Some`） | `enumMember` | なし |
| Attribute 名 | `decorator` | なし |
| Type 位置の Name（型注釈、型引数、`->` の後、Constraint の `is` の右辺） | `type` | なし |
| パーサーが文脈キーワードとして読んだ語（SPEC §2.5.1 の Contextual class） | `keyword` | なし |
| Semantics 位置の Semantics 名と Semantics category | `modifier` | なし |
| `$` の後の名前 | `function` | `defaultLibrary` |

予約語、`self`、`Self` は、拡張するクライアントには返さない。文脈キーワードと同じ綴りの語を Name として使えば、その Name の分類だけを返す（例：パラメーター `during`）。

### 3.4. 名前解決による分類

- **有効な結果。** 文書 D の名前解決による分類は、D の現在の revision を記録した入力に持つ、採用済みの valid な結果（§23.4.5）からだけ得る。held の結果と、他の入力が古くなった結果は使わない。D が編集されると、次の結果が採用されるまで、名前解決による分類はなくなる。
- **一致。** D に寄与する有効な結果が複数あるとき（product と test の単位、複数の target）、ある span を分類する結果がすべて同じ分類を与える場合だけ、その分類を使う。一致しなければ確定しないものとして返さない。その span を選択しない結果（除外された構文）は数えない。
- **参照の分類。** 名前の参照には、解決先の宣言の種別と、`readonly`・`static`・`abstract`・`defaultLibrary`・`unsafe` の修飾子を与える。Type 位置の `type` は、解決先の種類（`struct`、`enum`、`interface`、`typeParameter`）で置き換える。alias の Name は、宣言も参照も、解決先の宣言の種類で分類する（SPEC §9.4.1）。
- **使い方の修飾子。** `modification` は、代入・複合代入・`++`・`--` の対象の Place を表す式の最後の Name に付ける。`implicitExclusive` は、SPEC §7.3 が排他受信者として暗黙に取得する受信者式の最後の Name に付ける。受信者式が Name で終わらなければ付けない。
- **再取得の依頼。** クライアントが `workspace.semanticTokens.refreshSupport` を宣言しているとき、ワークスペース検査が終わり、その検査で採用した結果が、開いている文書の名前解決による分類を変えた場合に、`workspace/semanticTokens/refresh` を一度送る。

### 3.5. 構文ハイライトを持たないクライアント

クライアントが `augmentsSyntaxTokens` を宣言しないとき、サーバーは本書 3.3 と 3.4 に加えて、予約語（`keyword`）、`self` と `Self`（`keyword`）、`#if`・`#switch`・`#case`（`macro`）、コメント（`comment`、`///` には `documentation`）、文字列と文字のリテラル（`string`）、数値リテラル（`number`）、演算子（`operator`）を返す。文字列の補間の中は、通常のコードとして分類する。

### 3.6. アーキテクチャ（SPEC §23.1、§23.4.6）

§23.1 の役割の表に **Classifier** を加える。

| 役割 | 責務 |
| --- | --- |
| Classifier | state owner が渡した不変のテキストと、採用済みの分類の写しから semantic tokens を計算し、sender に渡す。state owner の外で動き、worker とは独立している。 |

state owner は、要求を受け取ったとき、文書テキストの不変の写しと、その文書の名前解決による分類（本書 3.4 で有効なもの）を Classifier に渡すだけである。§23.4.6 に「semantic tokens の要求は入力イベントではなく、検査の予定を変えない」と加える。

### 3.7. CSP と Appendix D（SPEC §23.5、Appendix D）

- Appendix D の「Language-server features beyond diagnostics」の行から semantic tokens を除き、状態を「Not introduced; the server publishes check diagnostics and answers semantic token requests」とする。
- §23.5.3 は変えない。CSP の構文・意味ノードのハンドルは、本書の分類を含む上位の機能であり、本書の分類は CSP の前倒しではない。CSP が導入されたら、分類は同じノードの事実から作る。

### 3.8. 変更しないこと

- 言語の妥当性、診断、検査、静穏期間、publication、ディスクの読み方は変えない。
- 色とテーマは定めない。分類をどう表示するかはクライアントが決める。
- TextMate 文法は仕様の対象外とする。拡張の一部として保守する。
- 除外された構文（§19.5）を暗く表示する機能、hover、inlay hint、`full/delta` は本書に含めない。

## 4. 例

SPEC §7.3 の例を、次の一つの文書とする（`during` はパラメーターの Name）。

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

| 行 | トークン | 構文による分類（3.3） | 名前解決を加えた分類（3.4） |
| --- | --- | --- | --- |
| 1 | `Meter` | `struct` `declaration` | 同じ |
| 2 | `measured` | `property` `declaration` | 同じ |
| 3 | `read` | `method` `declaration` | 同じ |
| 3 | `measured`（`self.measured`） | なし | `property` |
| 4 | `update` | `method` `declaration` | 同じ |
| 4 | `uniq` | `modifier` | 同じ |
| 4 | `value`（宣言） | `parameter` `declaration` | 同じ |
| 4 | `measured`（代入の対象） | なし | `property` `modification` |
| 4 | `value`（代入の値） | なし | `parameter` |
| 6 | `show` | `function` `declaration` | 同じ |
| 6 | `during` | `parameter` `declaration` | 同じ（キーワードではない） |
| 7 | `meter` | `variable` `declaration` | 同じ |
| 7 | `Meter` | なし | `struct` |
| 7 | `during`（引数） | なし | `parameter` |
| 8 | `shown` | `variable` `declaration` `readonly` | 同じ |
| 8 | `meter`（共有受信者） | なし | `variable` |
| 8 | `read` | なし | `method` |
| 9 | `meter`（排他受信者） | なし | `variable` `implicitExclusive` |
| 9 | `update` | なし | `method` |
| 10 | `shown` | なし | `variable` `readonly` |

構文による分類だけの結果では、6 行目（LSP の行番号 5）は次のように符号化される。直前のトークンは 4 行目（行番号 3）の `value` の宣言なので、`show` は `[2, 5, 4, 10, 1]`、`during` は `[0, 5, 6, 6, 1]` である。`func` と `i32` は予約語なので、拡張するクライアントには返さない。

9 行目を `meter.update(6)` に書き換えると、次の検査結果が採用されるまで、文書全体で 3.4 の分類がなくなる。3.3 の分類は書き換えた直後の要求から返り、参照（`meter`、`update`、`Meter` など）には構文ハイライトの色だけが残る。結果が採用されると、サーバーは refresh を送り、クライアントが再取得する。

## 5. 評価

**メリット。** 色分けの正本がコンパイラーになり、文脈キーワードと型の推測が確定した分類に置き換わる。暗黙の排他取得と unsafe 呼出しが見えるので、所有と安全性を局所的に判断しやすくなる。構文による分類は検査を待たないので、入力中も宣言と文脈キーワードは正しく表示される。

**費用。** §23.4 に節が一つ、legend が一つ増え、種別と修飾子の追加は仕様変更になる。検査結果は、名前解決による分類を保持するぶん大きくなる。要求のたびに文書を構文解析する。

**懸念。** 編集中は名前解決による分類が消えるので、参照の色が構文ハイライトの色との間で切り替わる。拡張の文法が通常の Name を `variable.other.kimi` に分類しておけば、多くのテーマで `variable` と同じ色になり、切り替わりは目立たない（本書 7 の S3）。それでも気になる場合に、編集を越えて分類を引き継ぐ案は本書 9.1 で退けた。

**複雑性。** 利用者に見える概念は「構文による分類」と「名前解決による分類」の二つで、後者の有効性は既存の valid・adopt の規則をそのまま使う。新しい状態は、結果ごとの分類の一覧と Classifier だけである。

## 6. 既存の提案との関係

- **`2026-10-02 Structured Repair Candidates.md`（提案中）：** 同案も §23.4.1 の広告と §23.4 の新設節を変える。両方を取り込む場合は、広告の項を両方の機能で書き、節番号は取り込み順に決める（本書 3.2）。機能は独立している。
- **`Changes/2026-09-23 Implicit Exclusive Receiver.md`（凍結）：** 同書 6 は、暗黙の取得の記録を「KimiCode（LSP）の inlay hint・semantic token」が読むと想定していた。本書の `implicitExclusive` がその semantic token にあたる。inlay hint は本書に含めない。
- **`Design/2026-09-28 Language Server and Compiler Services.md`（凍結）：** 初版の範囲外とした項目に semantic tokens は挙がっておらず、Appendix D で保留にした。本書はその保留を解く。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| S1：構文による分類 | 一つの文書の構文解析結果から、本書 3.3 のトークン列を位置順に作る。文脈キーワードはパーサーが読んだ場所だけを記録する | 本書 3.3 の各行と、文脈キーワードと同じ綴りの Name、回復を含む入力、除外された構文の試験が通る。Kimi ライブラリと SPEC の例のコーパスで、トークンが重ならず、位置順で、テキストの境界に収まる |
| S2：LSP の要求 | 広告の条件、legend、`full` と `range`、不変の写しと Classifier、`$/cancelRequest`、本書 3.2.1 の `null` の場合、クライアントが宣言した種別と修飾子だけを送る規則 | `LspProtocolTest` に、広告の有無、各 `null` の場合、通知の後の要求が新しいテキストに対応すること、入力中に検査の予定が変わらないこと、取消しの試験を加える。要求の処理中も受信が止まらない |
| S3：拡張 | package.json の `semanticTokenTypes`・`semanticTokenModifiers`・`semanticTokenScopes`、`label`・`unsafe`・`implicitExclusive` をクライアントの宣言に加える機能、文法で通常の Name を `variable.other.kimi` にする | 統合テストで、実サーバーから `vscode.provideDocumentSemanticTokens` の結果を受け取り、本書 4 の分類を確かめる。VS Code で目視確認する |
| S4：名前解決による分類 | 検査結果に、名前の参照の分類の一覧を source ごとに持たせる。本書 3.4 の有効性と一致の規則、refresh | 本書 4 の表、編集後に分類が消えて採用後に戻ること、複数 target で分類が異なる span を返さないこと、除外された構文を数えないことの試験が通る |
| S5：使い方の修飾子 | `modification`、`implicitExclusive`、`unsafe`、`abstract`、`defaultLibrary` | 各修飾子の付く例と付かない例（受信者式が Name で終わらない場合、共有受信者、Copy の読取り）の試験が通る |
| S6：構文ハイライトを持たないクライアント | 本書 3.5。コメントの span の記録を含む | 予約語・コメント・複数行の文字列の分割・補間の中の分類の試験が通る |
| S7：評価と閉鎖 | `LspMeasurements` に要求の測定を加え、STATUS・PLAN・CODEMAP を更新する | Session 検証が通る。大きな文書での `full` と `range` の時間と割当てを記録する |

性能の方針：分類は、文書の構文解析一回と、トークン列の符号化一回で済ませる。符号化は再利用するバッファーに直接書き、トークンごとの割当てをしない。名前解決による分類の一覧は、一つの検査結果につき source ごとに一つの配列とし、結果と同じ寿命で保持する。

## 8. 文書更新計画

正式な取り込みでは英語で更新し、仕様に draft への依存を残さない。

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §23.1 | Classifier の役割 | 取り込み時 |
| SPEC §23.4.1 | 本書 3.1 | 取り込み時 |
| SPEC §23.4.6 | 要求は入力イベントではない | 取り込み時 |
| SPEC §23.4.8（新設）、以降の節の繰り下げ | 本書 3.2–3.5。繰り下げた節へのリンクを更新する | 取り込み時 |
| SPEC の例のセッション（現 §23.4.9） | `initialize` の応答に `semanticTokensProvider` を加え、要求の例を一つ加える | 取り込み時 |
| Appendix D | 本書 3.7 | 取り込み時 |
| draft/INTEGRATED.md | 取り込みの記録 | 取り込み時 |
| STATUS、CODEMAP、PLAN | 実装した範囲 | 実装時 |
| README（Visual Studio Code）、kimi-ext の CHANGELOG | 拡張の対応 | S3 の実装時 |

## 9. 設計判断

### 9.1. 採用しなかった案

- **名前解決による分類を、編集を越えて引き継ぐ。** 古い結果の分類を、適用された編集で位置をずらして使い続ければ、入力中の色の切り替わりはなくなる。しかし、別の場所に加えた宣言が名前の解決先を変えることがあり、確定していない分類を確定したものとして示すことになる（SPEC §23.2 の原則に反する）。切り替わりは文法の側で目立たなくする（本書 5）。
- **常に予約語・コメント・リテラルも返す。** VS Code では、semantic token の `keyword` が TextMate の `keyword.control` などの区別を上書きし、表示が貧しくなる。`augmentsSyntaxTokens` で分ける。
- **分類を worker で計算する。** worker は一度に一つの検査しか実行しないので、長い検査の間、色が更新されない。構文による分類は検査に依存しないので、Classifier を分ける。
- **最初から `full/delta` を実装する。** 構文解析は文書全体で行うので、delta で節約できるのは送信量だけである。測定で必要が示されるまで加えない。
- **除外された構文を semantic token の修飾子で暗くする。** 拡張するクライアントでは名前と文脈キーワードしか送らないので、予約語やリテラルが暗くならず、表示が不揃いになる。除外範囲の表示は別の機能として扱う。
- **ローカルの名前を構文だけで解決する。** 入力中も参照を分類できるが、名前解決を Binding と別に実装することになり、二つの判断がずれうる（Kimigayo Principles の「一つの概念に一つの正規形」）。

### 9.2. Kimigayo Principles との対応

- **一つの概念に一つの正規形：** 分類の正本をパーサーと Binding に置き、文法は近似にとどめる。
- **局所的な推論：** 暗黙の排他取得、unsafe 呼出し、`let` と `var` の違いを、その場で見えるようにする。
- **明示的な意味論：** 構文が省略した取得を、言語の規則を変えずに表示する。
- **Compiler Server Protocol：** 構文・意味ノードの検査（§23.5.1）の一部を、編集器の標準的な経路で先に提供する。CSP の要件（§23.5.3）は変えない。

### 9.3. 未確認事項

- VS Code が、クライアントの宣言に加えた標準外の種別と修飾子を、拡張の `semanticTokenTypes`・`semanticTokenModifiers` と組み合わせて表示に使うこと（S3 で確認する）。
- 名前解決による分類の一覧が、検査結果の保持量をどれだけ増やすか（S4 と S7 で測定する）。
- 編集中の色の切り替わりが、文法の変更の後も気になるかどうか（S3 で目視確認する）。
