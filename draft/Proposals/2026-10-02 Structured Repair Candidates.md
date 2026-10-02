# 仕様変更案：構造化した修正候補

日付：2026-10-02（第 2 版：精査後）

状態：提案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書は、診断レコードに**修正候補（repair candidate）**を加え、`kimi check` の JSON 出力と言語サーバーの code action で機械が受け取れるようにする。本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例は独立した断片であり、「エラー」と記した行は意図した拒否例である。

第 2 版は、第 1 版を精査して次を改めた：保証と前提条件の二つの語彙を一つの**条件**の語彙に統合した（本書 3.1）。`Selection` の確立方法を、候補の試行的な再解決として定めた（本書 3.1）。code action の照合を範囲に基づくものにした（本書 3.5）。JSON 出力を共有の検査入口から得ることにした（本書 3.4）。Result 破棄の候補に伝播を加えた（本書 3.7）。評価の章を加えた（本書 5）。

## 1. 現仕様の問題

### 1.1. 検証した修正を散文にしか書けない

SPEC は、修正を検証してから示すことを各所で要求している。

- SPEC §10.6：取得競合の編集は「候補を一つに選ぶと確認できたときだけ示し、Copy・Take・Loan・寿命を検証したかを明示する」。
- SPEC §7.3：受信者式の修正は「修正後のプログラムがすべての取得・権限・Loan 条件を満たすときだけ示す」。
- SPEC §14.3.3：不要な `unsafe` の構造化した修正は「スコープ・破棄順序・制御転送を保つと証明できたときだけ示す」。
- SPEC §14.5.1、§17.2.4、§17.4.3：修正は「意図を述べ、意味の保存を無条件には主張しない」「自動では適用しない」。

一方、診断レコード（SPEC §23.3.6.2）の欄は Message・Reason・Related・Note・Advice などで、修正を入れる欄がない。しかも SPEC §23.3.2 と §23.3.6.1 は「Advice と Note の散文から機械適用可能な編集を推論しない」と定める。その結果、検証を済ませた修正も、機械が使ってはいけない散文の中にしか置けない。

実装も同じ形になっている。`Binding.Diagnostics.cs` の `ReportAcquisitionConflicts` は Copy の証明（`CopyProven`）と Take の可否（`OffersTake`）を判定したうえで四種類の英文から一つを選び、`ControlFlowAnalysis.WarnUnnecessaryUnsafe` はスコープが変わるかを判定したうえで二種類の英文から一つを選ぶ。判定の結果は捨てられ、文だけが残る。

### 1.2. 機械が受け取る経路がない

- JSON 形式（SPEC §23.3.6.8）は「どのコマンドも出力せず、公開スキーマを持たない」。
- 言語サーバー（SPEC §23.4.7）は label・関連位置・Note・Advice を message の一つの文字列に連結して送る。code action はない。
- CSP（SPEC §23.5）は要件の列挙だけで、実装されていない（Appendix D）。

AI を利用者に想定する言語でありながら、修正候補を構造化して受け取る経路が一つもない。

### 1.3. 検証済みと未検証の区別が散文に埋まる

取得競合の Advice「`@ref` を付けて借用するか、`@move` を付けて転送する」は、`@ref` が常に候補を一つに選ぶのに対し、`@move` は以後その Place を使わないことを前提にする。この違いは文の中に暗黙に含まれるだけで、利用者が機械的に判別できない。原則3（明示的な意味）に反する。

### 1.4. 原則4との乖離

Kimigayo Principles の原則4は、診断について「原因、関連位置、**明示的な前提条件と保証を伴う修正候補**」を構造化した界面で提供するとしている。SPEC §23.5.3 はこれを CSP の将来要件とし、Appendix D は CSP 全体を保留にしている。しかし修正候補は、他の事実（Reason・関連位置）と同じく診断レコードの一部であり、CSP の導入を待つ必要がない。

## 2. 理想的な動作

1. **候補は構造化データである：** 診断レコードは、問題を解消する代替案を**修正候補**として持つ。候補は、編集（入力の不変テキストに対する位置と置換文字列）と、条件の判定結果からなる。
2. **条件は一つの語彙で、検証済みか要求かに分かれる：** 候補に関係する条件を閉じた語彙で名指し、検査が自身の事実から確立した条件を **verified**、確立しなかった条件を **required** として示す。散文には埋めない。
3. **候補は検査の事実だけから決まる：** 効果が事実から決まる修正だけを候補にする。選択を要する修正（導入する名前、複数の Origin から選ぶなど）は候補にせず、Advice が代替案を述べる。
4. **一つの形：** 候補のある記録は、同じ編集を Advice で繰り返さない。すべての出力（コマンド、JSON、言語サーバー）は同じ候補を表示する。
5. **機械が受け取れる：** `kimi check --Format json` が公開スキーマの JSON を出力し、言語サーバーが `textDocument/codeAction` で候補を quick fix として返す。
6. **適用は要求されたときだけ：** ツールは候補を自動では適用しない。候補を適用した結果は新しい入力であり、その妥当性は新しい検査でだけ確立する。検査が読んだテキストと異なるスナップショットへの適用は拒否する。

## 3. 仕様の変更点

### 3.1. 修正候補（新設 SPEC §23.3.6.9）

**構成。** 一つの修正候補は、記録された入力に対する一つの代替的な変更であり、次からなる。

| 要素 | 内容 |
| --- | --- |
| kind | 閉じた目録の安定名（`Repair.Borrow` など）。コードと同じく自由文字列を受け付けず、title のテンプレートと事実の名前・種類、関係する条件を目録が定める |
| title | kind のテンプレートと候補の事実から作る一文 |
| facts | Reason と同じ型付き事実（例：選ばれる宣言の表示値） |
| edits | 編集の列。各編集は、source table の項目（記録された入力に限る）、その不変テキストの UTF-16 位置と長さ、置換文字列からなる。長さ 0 は挿入、置換文字列が空なら削除。表示データとして行・文字の範囲を持つ |
| verified | 検査が修正後のプログラムについて自身の事実から確立した条件の集合 |
| required | 検査が確立しなかった条件の列。各項目は条件の名前と、テンプレートと事実から作る句 |

**規則：編集。** 一つの候補の編集は互いに重ならず、位置順に並び、一括して適用する。編集は正確な値であり、表示用に短縮・省略しない。編集は記録された入力だけを対象にし、`compiler://` や生成された source には及ばない。挿入・置換した文字列は、その位置で完全なトークンまたは項目であり、周囲のレイアウト（SPEC §2.2）を保つ。これはすべての候補の規則であって、条件ではない。

**条件の語彙。** 条件は、kind に関係するものだけを verified か required のどちらかに振り分ける。語彙は閉じており、追加には仕様変更を要する。

| 条件 | 意味 | verified とする根拠 |
| --- | --- | --- |
| `Selection` | 呼出しが候補をちょうど一つ選ぶ | 残った候補（SPEC §10.2.2 の手順 2 の時点）について、編集後の引数の取得計画で適用可能性と順位付け（§10.4）を試行し、ちょうど一つが選ばれること。試行は計画を記録するだけで状態を変えない（§10.1）。この改版では、`Selection` が verified でない候補は提示しない |
| `Copy` | 取得する値が Copy と証明済み | 保留していた Copy 証明（§10.2.2） |
| `Take` | その Place が Take を提供し、Movable Place の条件（§15.1.5）を満たす | 取得計画 |
| `ExclusiveAccess` | 貸与点が排他的に書き込み可能（§15.1.5） | 取得計画 |
| `NotUsedAfterwards` | 転送の後に、その Place を使わない | この改版では常に required。修正後のプログラムの Loan と寿命の解析を要する |
| `Scopes` | 名前の可視範囲、破棄と `defer` の順序、制御転送の対象が変わらない | 本体の項目の種類と構文 |

Loan と寿命は、修正後のプログラムの解析を要するので、この改版では verified にならない。`NotUsedAfterwards` がその代わりに required として現れる。

**規則：提示。**

- 候補は代替案であり、優先順位を持たない。ツールは要求なしに適用しない。
- 候補は、その効果が検査の事実から決まるときだけ提示する。選択を要する修正は提示しない。
- 候補の verified には、検査が自身の事実から確立した条件だけを入れる。再検査はしない。
- 派生レコード（`PrerequisiteUnavailable_Kd`）は候補を持たない。
- 候補を適用した結果は新しい入力である。その受理は新しい検査でだけ確立し、候補から何も推論しない。
- 順序は kind の目録順、次に最初の編集の位置とする。到着順や文言には依らない。

**kind の目録。** この改版の kind は本書 3.7 の表のとおりで、§23.3.6.9 に各章への参照つきで列挙する。kind の追加は仕様変更である。

### 3.2. レコード、制限、順序、契約（SPEC §23.3.6.2、§23.3.6.5、§23.3.6.6、§23.3.6.7）

- §23.3.6.2 のレコード表に `Repairs` 行を加える：「修正候補（§23.3.6.9）。問題を解消する代替的な構造化編集の列」。
- §23.3.6.5：記録あたりの候補数に制限を設け、超過分を Omissions（`repair candidates`）に数える。編集の文字列は制限しない。
- §23.3.6.6：レコードの等価性は候補を含む。順序は変えない。
- §23.3.6.7：次を診断契約の違反に加える。編集のない候補、重なる編集、入力範囲外の編集、記録された入力でない source への編集、派生レコードの候補、kind に関係しない条件、目録にない kind や条件。

### 3.3. Advice との関係（SPEC §23.3.2、§23.3.6.1）

「Advice と Note の散文から機械適用可能な編集を推論しない」は維持する。次を加える。

- 修正候補は、機械が適用できる唯一の形式である。
- §23.3.6.1 の「suggested intent is Advice, which states its conditions」を「suggested intent is a repair candidate with its conditions, or Advice」に改める。
- 候補のある記録の Advice は、候補が表せない条件と代替案を述べる。候補の編集を散文で繰り返さない。
- 候補を持たない記録の Advice は現行どおりである。

### 3.4. コマンドと JSON 出力（SPEC §23.3.6.8、IMPL §20.8）

**コマンド。** 候補のある記録は Advice の後に候補を描画する：`Repair: <title>`、各編集を ` = path:line:column: insert '<text>'`（置換・削除も同形）、最後に ` = verified: <条件>; requires: <句>`。

**JSON。** `kimi check <project> --Format json` は、共有の検査入口（§23.3.2）で一つの検査単位を検査し、その出力を一つの JSON 文書として標準出力に書く。他の出力は標準エラーに書き、終了コードは変えない。`--Format text`（既定）は現行の描画である。検査入口を共有するので、JSON の内容は言語サーバーが同じ入力で得る結果と等しい。

```text
CheckOutput := {
  schema:       "kimi.check/1"
  compiler:     コンパイラーのビルド識別
  unit:         { project, target, mode: "Product" | "Test", debug }
  outcome:      "Completed" | "Blocked" | "Faulted"
  accepted:     bool
  testPresence: "Yes" | "No" | "Unknown"
  sources:      [ { path, isInput, sha256? } ]    // sha256 は記録された入力のバイト列のハッシュ
  diagnostics:  [ レコード：§23.3.6.2 の全欄と repairs ]
}
```

フィールド名は camelCase、列挙値は文字列、位置は `{ start, length }`（UTF-16）、範囲は 0 始まりの行・文字とする。スキーマは `docs/spec/schemas/check-output.schema.json` に置き、§23.3.6.8 がその版（`kimi.check/1`）を規定する。`sha256` は、エージェントが「検査と同じバイト列に編集を当てる」ことを確認するためのもので、JSON 出力のときだけ読取り時に計算する。

### 3.5. 言語サーバーの code action（SPEC §23.4.1、新設 §23.4.8）

- **機能：** `codeActionProvider: { "codeActionKinds": ["quickfix"] }` を広告する。`codeAction/resolve` と `command` は使わない。
- **応答：** `textDocument/codeAction` は、検査を走らせずに、その URI に最後に送った診断から答える。要求の `range` と範囲が重なる送信済みの診断について、その文書内に全編集が収まる候補ごとに一つの `CodeAction` を返す：`kind` は `quickfix`、`title` は候補の title（required があれば `; requires <句>` を続ける）、`diagnostics` はその送信済み診断、`edit.documentChanges` は診断を送ったときの version を付けた `TextDocumentEdit`。要求の `context.only` が `quickfix` を含まないときは空を返す。要求の `context.diagnostics` は照合に使わない。
- **古いスナップショットの拒否：** 文書の現在の version が診断を送った version と異なる間は、空を返す。SPEC §23.4.6 により、送った診断は送信時の文書テキストを読んだ結果なので、version の一致は編集位置の一致を意味する。
- **複数の contributor：** 一つの URI に複数の単位が同じ診断を送るとき（§23.4.7 のマージ）、候補はマージが残した contributor のものを使う。
- **他文書：** 他の文書に編集が及ぶ候補は、この改版では返さない。
- 現行の §23.4.8（監視と設定）と §23.4.9（例）は §23.4.9・§23.4.10 に繰り下げる。

### 3.6. CSP との分担（SPEC §23.5、Appendix D）

- §23.5.2：修正候補と JSON 形式を「提供済みの基盤」に移す。
- §23.5.3：「修正候補を構造化編集として提供する」要件を、「候補を識別したスナップショットに対して適用し、適用後の検査を検証可能な変更として結び付ける。Loan と寿命のように修正後の解析を要する条件は、その検査で確立する」に改める。
- Appendix D の CSP 行から「repair candidates」と「public schema」を外す。永続的なスナップショット識別、構文ハンドル、スナップショットへの編集適用、検証記録は保留のままとする。

### 3.7. 候補を定める診断

各章の「suggest」「fix」の文を、候補か Advice かのどちらかに分類して書き改める。候補は次のとおり。条件の列は、kind に関係する条件とその振り分けを示す。

| 診断（SPEC） | kind と編集 | verified | required | 提示条件 |
| --- | --- | --- | --- | --- |
| `AcquisitionRequired_Kd`（§10.6） | `Repair.Borrow`：引数全体に `@ref` を付ける | Selection | — | 試行的な再解決で候補が一つに決まる |
| 〃 | `Repair.Copy`：`@copy` | Selection, Copy | — | 同上。Copy 証明済み |
| 〃 | `Repair.Transfer`：`@move` | Selection, Take | NotUsedAfterwards | 同上。Take 可 |
| `TransferRequired_Kd`（§3.5、§7.6.2） | `Repair.Transfer`：`@move`。Capture エントリでは `Repair.Borrow`（`@ref`）も | Take（取得計画で確立したとき） | Take（未確立のとき）、NotUsedAfterwards | 常に |
| `ExclusiveBorrowRequired_Kd`（§7.3、§15.1.5） | `Repair.BorrowExclusively`：`@uniq`、オブジェクト handle なら `@objuniq` | ExclusiveAccess（確立したとき） | ExclusiveAccess（未確立のとき） | 常に |
| `UnnecessaryUnsafeBlock_Kd`（§14.3.3） | `Repair.RemoveUnsafe`：単一項目本体は `unsafe => ` を削除。インデント本体は `unsafe` の行を削除し、本体の各行の先頭からインデント一段を削除 | Scopes | — | 本体が名前を宣言せず `defer` を登録しない。`unsafe` の行に他のトークンやコメントがない。本体に複数行にわたるリテラルがない |
| `MisplacedSyntax_Kd` の `AmpersandAmpersand`・`BarBar`（§2.4、§13.8） | `Repair.ReplaceToken`：`and`・`or` | — | — | 常に |
| `BorrowOriginKeyword_Kd`（§3.3.6） | `Repair.ReplaceToken`：`from` を `during` に | — | — | 常に |
| `MissingSyntax_Kd` の閉じ記号（§23.3.6、DIAGNOSTICS §4.4） | `Repair.InsertToken`：挿入点に閉じ記号 | — | — | 挿入点が確定している閉じ記号 |
| `DiscardedResult_Kd`、try 成功値の破棄警告（§17.4.3） | `Repair.PropagateFailure`：式の先頭に `_ = try ` を挿入（Result 破棄のとき）。`Repair.ExplicitDiscard`：`_ = ` を挿入 | Scopes | — | 式がインデント本体の直接項目である（単一項目本体や分岐の末尾では Context が変わるので提示しない）。伝播は、囲む関数の失敗の戻り先が §17.2.4 で適合するときだけ。順序は §17.4.3 のとおり伝播、明示破棄 |
| `DiscardedValue_Kd`（§17.4.2） | 候補なし | — | — | 「戻り値の注釈か値の使用」は選択を要する |
| §7.3 の受信者式（`let` を `var` に、など） | 候補なし（この改版） | — | — | Loan 条件の検証を要する。Advice が条件を述べる |
| §14.5.1 のコロン、§15.3.2・§15.4.3 の Origin | 候補なし | — | — | 意図または選択を要する |

`Selection` の試行的な再解決は、競合した呼出しの残った候補に対して、編集後の引数の計画（新しい共有借用、Copy、転送）で適用可能性と順位付けをやり直す。他の引数は既に型付けされており、呼出しに期待型を待つ引数（§10.5）は選ばれた候補に対して後の検査で判定される。再解決は選ばれる候補の数だけを確立し、修正後のプログラムの他の問題を排除しない。

### 3.8. 変更しないこと

- コード・Reason・関連位置・Note・Advice の定義、問題の同一性、前提関係と抑制、結果の受理。
- 言語の妥当性。候補は言語規則を変えず、SPEC §23 の「サービスは言語の妥当性を変えない」を保つ。
- 修正後の再検査による条件の確立（Loan・寿命）。CSP の「検証可能な変更」で別に設計する。

## 4. 期待する動作

### 4.1. 取得競合

```kimi
func process(value: Node) -> i32 => 1
func process(value: ref/Node) -> i32 => 2

func run(node: Node) -> i32
    return process(node)        // エラー：AcquisitionRequired_Kd
```

`kimi check --Format json` の該当レコード（抜粋。`Node` は Copy 未証明）：

```json
{
  "code": "AcquisitionRequired_Kd", "severity": "Error", "category": "Language",
  "source": 1, "span": { "start": 118, "length": 4 },
  "reason": [{ "name": "type", "kind": "Text", "value": "Node" }],
  "related": [
    { "role": "candidate", "source": 1, "span": { "start": 20, "length": 4 }, "label": "process acquires it by value as Node" },
    { "role": "candidate", "source": 1, "span": { "start": 62, "length": 8 }, "label": "process borrows it as ref/Node" }
  ],
  "note": "Node is not proven Copy, so a by-value candidate cannot Copy this Place",
  "repairs": [
    { "kind": "Repair.Borrow", "title": "Append @ref to borrow node for process(value: ref/Node)",
      "edits": [{ "source": 1, "span": { "start": 122, "length": 0 }, "text": "@ref" }],
      "verified": ["Selection"], "required": [] },
    { "kind": "Repair.Transfer", "title": "Append @move to transfer node to process(value: Node)",
      "edits": [{ "source": 1, "span": { "start": 122, "length": 0 }, "text": "@move" }],
      "verified": ["Selection", "Take"],
      "required": [{ "condition": "NotUsedAfterwards", "phrase": "node is not used after this call" }] }
  ]
}
```

コマンドの描画（Advice の後）：

```text
Repair: Append @ref to borrow node for process(value: ref/Node)
 = Main.kimi:5:21: insert '@ref'
 = verified: Selection
Repair: Append @move to transfer node to process(value: Node)
 = Main.kimi:5:21: insert '@move'
 = verified: Selection, Take; requires: node is not used after this call
```

`Node` が Copy 証明済みなら `Repair.Copy`（`@copy`、verified は Selection, Copy）が加わる。次の組では `Repair.Borrow` を提示しない。`node@ref` に対して `f<U>(value: U)` が `U = ref/Node` で適用可能なまま残り、再解決が二つの候補を返すからである（SPEC §10.2.2 の `f(node@ref)` は曖昧）。

```kimi
func f<U>(value: U) -> i32 => 1
func f<U>(value: ref/U) -> i32 => 2
f(node)                        // エラー：取得競合。候補は Repair.Copy（Copy なら）と Repair.Transfer だけ
```

### 4.2. 不要な unsafe

```kimi
unsafe
    update(counter@uniq)    // unsafe 権限を使う操作がない
    publish()
```

`UnnecessaryUnsafeBlock_Kd` の候補 `Repair.RemoveUnsafe` は三つの編集を持つ：`unsafe` の行の削除と、本体二行の先頭 4 文字の削除。verified は Scopes。

```kimi
unsafe
    let address = pointer@usize    // 名前を宣言する
    publish(address)
```

本体が名前を宣言するので候補を提示しない。現行の Advice（スコープが変わる旨）だけを残す。

### 4.3. 記号の論理演算子

```kimi
if ready && armed => fire()     // エラー：MisplacedSyntax_Kd（AmpersandAmpersand）
```

候補 `Repair.ReplaceToken`：`&&` を `and` に置換。条件はない。

### 4.4. 破棄と伝播

```kimi
func run() -> Result<(), Error>
    prepare()               // 警告：DiscardedResult_Kd。prepare は Result<Data, Error> を返す
    return .Ok(())
```

候補は二つ。`Repair.PropagateFailure`：`_ = try prepare()`（戻り先 `Result<(), Error>` に `Error` が適合する）。`Repair.ExplicitDiscard`：`_ = prepare()`。どちらも verified は Scopes。`if ready => prepare()` の単一項目本体では、`_ =` が Context を変えるので提示しない。

### 4.5. 言語サーバー

```text
→ {"jsonrpc":"2.0","id":3,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///C:/work/Main.kimi"},"range":{"start":{"line":4,"character":21},"end":{"line":4,"character":21}},"context":{"diagnostics":[]}}}
← {"jsonrpc":"2.0","id":3,"result":[
     {"title":"Append @ref to borrow node for process(value: ref/Node)","kind":"quickfix","diagnostics":[{…送信済みの診断…}],
      "edit":{"documentChanges":[{"textDocument":{"uri":"file:///C:/work/Main.kimi","version":3},
              "edits":[{"range":{"start":{"line":4,"character":23},"end":{"line":4,"character":23}},"newText":"@ref"}]}]}},
     {"title":"Append @move to transfer node to process(value: Node); requires node is not used after this call","kind":"quickfix","diagnostics":[{…}],
      "edit":{"documentChanges":[{"textDocument":{"uri":"file:///C:/work/Main.kimi","version":3},
              "edits":[{"range":{"start":{"line":4,"character":23},"end":{"line":4,"character":23}},"newText":"@move"}]}]}}]}
```

この後に `didChange` が届いて version が 4 になると、次の publish までは同じ要求に `[]` を返す。

## 5. 評価

### 5.1. メリット

- 原則4の「明示的な前提条件と保証を伴う修正候補」を、CSP を待たずに診断レコードの一部として提供する。
- 検証済みの修正が機械に届く。エージェントは JSON の `edits` と `sha256` で検査と同じバイト列に編集を当て、`required` で引き受ける条件を知る。
- 既存の判定（`CopyProven`・`OffersTake`・スコープの判定）が散文ではなく事実として残る。候補と Advice の役割が分かれ、同じ助言の二重形式がなくなる。
- 言語の妥当性は変わらない。候補はサービスの契約だけを広げる。

### 5.2. デメリットと費用

- SPEC §23 に目録（kind）と語彙（条件）が増え、新しい候補の追加が仕様変更になる。これはコードと要求の目録と同じ扱いであり、自由文字列を受け付けない方針の帰結である。
- 診断を加える作業に「候補か Advice か」の判断が加わる。AGENTS.md の Review 手順に一行を加えて明示する。
- `Selection` の試行的な再解決は、競合した呼出しにだけ走るが、`Binding.CandidateEvaluation` に計画の差し替えつきの再評価を加える必要がある。費用は失敗した呼出しの候補数に比例する。
- 誤った verified は利用者を誤らせる。候補を提示する記録箇所ごとに、条件を満たさない対照例が候補を出さないことを試験で固定する。

### 5.3. 複雑性

- 利用者から見える概念は「候補」「条件」「verified／required」の三つで、条件の語彙は六語である。保証と前提条件を別の語彙にした第 1 版より小さい。
- 実装は、記録側の構造体一つ、目録一つ、最終化の再配置と契約検査、CLI オプション一つ、LSP の要求一つである。既存の関連位置（Related）と同じ経路を使う。
- 段階的に価値が出る：U1–U2 で CLI に候補が現れ、U3 で取得競合、U4 で JSON、U5 で quick fix。U6（破棄）は独立に後回しにできる。

### 5.4. 検討して採らなかった案

本書 8.1 に示す。

## 6. 既存の提案との関係

- **`2026-10-02 Parameter Acquisition Shape.md`（提案中）：** 採用されると `AcquisitionRequired_Kd` と取得競合がなくなり、本書 3.7 の最初の三行と `Selection` の再解決を削除する。`TransferRequired_Kd` と `ExclusiveBorrowRequired_Kd` の候補は、その提案の下でも唯一の候補が要求する取得をそのまま表すので残る。両提案のどちらが先に取り込まれても、この差し替えだけで整合する。先にその提案を採用するなら、本書 U3 は省ける。
- **`2026-09-28 Language Server and Compiler Services.md`（凍結）：** 「automatic fixes は範囲外」としたのは言語サーバーの初版についてであり、本書はその基盤の上に候補を加える。設計書は変更しない。
- **`2026-09-29 Diagnostics.md`（凍結）：** 将来項目「Repair（構造化した修正）は、前提条件と保証を示せる修正から導入する。古い入力や編集の競合は拒否する」を本書が実現する。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進め、各単位は AGENTS.md の Diagnostic Development Workflow に従う。PLAN には「Repair track」として R1–R7 の行を加える。

### 7.1. 実装単位

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U1：レコードモデル | `DiagnosticRepair.cs`（`DiagnosticRepair`・`DiagnosticEdit`・`RepairCondition` 列挙・`RequiredCondition`）、手書きの `RepairKind` 列挙と `DiagnosticRepair.tinyhand`（kind ごとの Title テンプレート、事実、関係する条件、条件の句。`DiagnosticRequirement.tinyhand` と同じ方式で整合テストを置く）。`DiagnosticFact` に記録側の候補（Koto と span に基づく編集）を加え、`Koto.Report(repairs:)` と `DiagnosticCollection.Report` を拡張する。`DiagnosticOwner` は同一キーへの再報告で候補が異なれば Note・Advice と同じく契約違反とし、最終化で編集を source table に再配置し、`DiagnosticLimits.Repairs` と Omissions、本書 3.2 の契約違反を検査する。`CheckDiagnostic.Repairs` と等価性、スナップショットの差分種別 `repair` | `DiagnosticRepairTest`（最終化、順序、制限、等価、各契約違反）と `DiagnosticContractTest` が通る。候補のない記録で割当てが増えない。既存スナップショットに差分がない |
| U2：構文・unsafe の候補とコマンド描画 | `TokenReader.Diagnostics.cs` で `&&`・`\|\|` の置換と閉じ記号の挿入、`BorrowOriginKeyword_Kd` の置換。`WarnUnnecessaryUnsafe` の候補（単一項目本体、インデント本体のデデント、提示条件：`unsafe` 行の末尾と複数行リテラルは SourceDocument のテキストとリテラル節の span で判定する）。`Kimigayo.Render` の候補描画 | `SyntaxDiagnosticTest`（`syntax.json` に `Repairs` 期待欄を加える）・`UnnecessaryUnsafeBlockTest`・`DiagnosticRelationTest`（空行の追加で編集位置だけが動く）が通る。提示条件を満たさない対照例で候補が出ない。CLI 出力の目視レビューを `review/` に残す |
| U3：取得の候補 | `Binding.CandidateEvaluation` に、競合した呼出しの残った候補を差し替えた計画で再評価する試行（状態を変えない）を加え、`ReportAcquisitionConflicts` で本書 3.7 の候補を作る。`AcquisitionAdvice*` の四定数を削除する。`TransferRequired_Kd`・`ExclusiveBorrowRequired_Kd` の候補（Binding と所有権解析の両方の報告箇所） | `AcquisitionConflictTest`：SPEC §10.2.2 の例（`process`・`select`・`combine`・`f<U>`・`route`）で候補の有無と verified が期待どおり。候補のある記録に重複する Advice がない。温まった再 Binding の割当てゼロを維持する |
| U4：JSON 出力 | `KimiOptions.Format`。JSON のとき `kimi check` は `CheckService.Run` でディスク入力の一単位を検査し、`CheckOutput` に `schema`・`compiler`・`unit`・`sources[].sha256` を付けて標準出力に書く。`docs/spec/schemas/check-output.schema.json` | `CheckCommandJsonTest`：標準出力が JSON だけであること、Blocked・Faulted も JSON であること、順序の安定、ハッシュの一致、テキスト形式が変わらないこと、同じ入力でテキスト形式と同じ記録であること。スキーマはライブラリーを足さず、固定のフィールド名・列挙値の対照表で検査する |
| U5：code action | `Json.cs` の `CodeActionParams`・`CodeAction`・`WorkspaceEdit`・`TextDocumentEdit`・`TextEdit`、`LspMethods.CodeAction`、機能の広告。`LspSession` は送信済み payload と候補を併せて保持し（`LspDiagnostic` には載せない）、state owner で同期に応答する | `LspCodeActionTest`：publish 後に候補が返る、編集後は返らない、再 publish で戻る、required が title に出る、`only` の尊重、二つの contributor の同一診断で候補が重複しない、他文書への編集は返らない。実サーバーで VS Code の Quick Fix を手動確認する。拡張（`src/kimi-ext`）はコードを変えない |
| U6：破棄と伝播の候補 | `ControlFlowAnalysis` の `DiscardedResult_Kd` と try 成功値の警告に、位置判定つきの `Repair.PropagateFailure`（戻り先の適合は Binding の結果で判定）と `Repair.ExplicitDiscard` | 既存の破棄警告テストに候補と対照例（単一項目本体、分岐の末尾、適合しない戻り先では出ない）を加える |
| U7：評価と閉鎖 | コーパス全体のスナップショット、CLI・LSP の目視レビュー、`Benchmark --diagnostics` の前後比較、DIAGNOSTICS §9 の評価、STATUS・PLAN の更新 | Session 検証が通る。有効プログラムの割当て差がゼロであること、300 エラーのプログラムで候補 1 件あたりのバイト数を記録する |

### 7.2. 性能方針

- 候補は失敗した検査にだけ付く。有効なプログラムの経路に割当てを加えない（U1 の完了条件で固定し、`Benchmark --diagnostics` で確認する）。
- 編集の文字列は `@ref`・`and` のような定数か、`unsafe` のデデントのように警告時にだけ作る小さな文字列である。
- 条件は検査自身の事実だけから立て、再検査しない。`Selection` の再解決は競合した呼出しの候補数に比例し、計画の記録以外に割り当てない。
- code action は検査を起こさず、state owner が送信済みの状態を読むだけである。受信ループを止めない。
- `sha256` は JSON 出力のときだけ、読取り時に計算する。

### 7.3. 検証

単位の完了時は `scripts/verify.ps1 -Class ...` で正式に検証し、最後に一度 `-Mode Session` を実行する。証拠は `artifacts/verify/`、測定結果は `artifacts/benchmarks/diagnostics-repairs/` に置く。NativeAOT は実行しない。

## 8. 文書更新計画

正式な取り込みでは英語で更新し、仕様に draft への依存を残さない。

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §23.3.2、§23.3.6.1 | 本書 3.3 | 取り込み時 |
| SPEC §23.3.6.2、§23.3.6.5–§23.3.6.7 | 本書 3.2 | 取り込み時 |
| SPEC §23.3.6.8 | 候補の描画、JSON の出力と文書形、スキーマ版、共有の検査入口 | 取り込み時 |
| SPEC §23.3.6.9（新設） | 本書 3.1 と kind の目録 | 取り込み時 |
| SPEC §23.4.1、§23.4.8（新設）、§23.4.9–§23.4.10（繰り下げ） | 本書 3.5。繰り下げる節へのリンクを更新する | 取り込み時 |
| SPEC §23.5.2、§23.5.3、Appendix D | 本書 3.6 | 取り込み時 |
| SPEC §10.6、§7.3、§14.3.3、§14.5.1、§17.2.4、§17.4.2、§17.4.3、§2.4 または §13.8、§3.3.6、§3.5、§7.6.2 | 本書 3.7 のとおり、各文を候補か Advice に分類して書き改める | 取り込み時 |
| SPEC Appendix E | Repair candidate、Condition（verified／required） | 取り込み時 |
| `docs/SPEC.md` | §23 の要約に候補・JSON・code action を加える | 取り込み時 |
| `docs/spec/schemas/check-output.schema.json`（新設） | 本書 3.4 の文書形 | 取り込み時 |
| IMPL §20.8 | `--Format` | 取り込み時 |
| `AGENTS.md` | Diagnostic Development Workflow の Review に「修正は候補か Advice かを決め、候補の verified は事実から確立した条件に限る」を加える | 取り込み時 |
| `draft/INTEGRATED.md` | 本書の取り込みを記録する | 取り込み時 |
| `README.md` | Check の JSON 出力と Visual Studio Code の Quick Fix | 実装時（U4、U5） |
| `docs/dev/DIAGNOSTICS.md` | 新 §4.5（記録側の API、目録、条件の確立規則、契約）、§7（出力）、§9（評価） | 実装時 |
| `docs/dev/PLAN.md` | Repair track R1–R7 の行。200 行以内を保つ | 実装時 |
| `docs/dev/CODEMAP.md` | Diagnostics and LSP 行に新ファイルと入口を加える | 実装時 |
| `docs/STATUS.md` | Compiler services 行。境界が変わる単位ごとに更新する | 実装時（U2–U5、U7） |
| `docs/dev/PLAN_HISTORY.md` | セッションごとに数行 | 実装時 |
| `docs/LIBRARY.md`、`docs/STYLE.md` | 変更しない | — |

## 9. 設計判断

### 9.1. 採用しなかった案

| 案 | 不採用の理由 |
| --- | --- |
| Advice の散文を機械が解析する | 推論禁止（SPEC §23.3.2）に反し、文言の変更で壊れる |
| 保証と前提条件を別々の語彙にする（第 1 版） | 同じ条件（Take など）が記録によって保証にも前提条件にもなる。一つの語彙を verified と required に振り分ける方が小さく、矛盾しない |
| `Syntax` と `Transfers` を条件にする（第 1 版） | すべての候補が常に満たす。条件ではなく候補の規則にする |
| `Selection` を当事者の数だけで判定する（第 1 版） | ジェネリックな値取得側（`f<U>(value: U)`）は `@ref` の後も適用可能で、他の引数の順位も影響する。試行的な再解決が要る |
| 初版から修正後の再検査で Loan・寿命まで確立する | 診断ごとに再コンパイルが要り、検査の費用が記録の数に比例して増える。CSP の検証可能な変更として別に設計する |
| 候補に優先順位（`isPreferred`）を付ける | 意味の違う編集を自動で選ばない規則（SPEC §10.6）に反する |
| `codeAction/resolve` で編集を遅延生成する | 送信済みの結果から答えられるので遅延の利点がなく、状態の保持が二重になる |
| 要求の `context.diagnostics` と照合する（第 1 版） | クライアントが message を改変せずに返すことに依存する。送信済みの診断と要求の範囲の照合で足りる |
| LSP の `Diagnostic.data` に不透明なキーを入れる | クライアント機能（dataSupport）に依存する |
| 古いスナップショットでは候補を `disabled` で返す | クライアント機能（disabledSupport）に依存し、次の publish で解消する。空を返す |
| 候補の kind と title を自由文字列で記録する | 「自由文字列を受け付けない」（SPEC §23.3.6.1）と一貫しない。目録にする |
| JSON を `kimi check` の既定出力にする | 既存のテキスト出力と検証証拠（`review/`）を変える。選択式にする |
| JSON を現行の CLI 経路（`Project.Check`）から出す | 結果の形（outcome・testPresence）が共有の検査入口にしかない。入口を共有すれば言語サーバーと同じ結果になることが構造で保証される |
| 候補を独自の通知で送る | 標準の code action で足りる |
| Result 破棄の候補を明示破棄だけにする（第 1 版） | §17.4.3 は伝播を先に挙げる。戻り先が適合するときの伝播は事実から決まる |

### 9.2. Kimigayo Principles との対応

- **原則1：** 修正は候補という一つの形で表し、条件は一つの語彙で表す。Advice は候補が表せない条件と代替案だけを述べる。
- **原則2：** 候補の効果と条件の判定は検査の事実だけから決まり、その記録の中に明示される。再解決は競合した呼出しに閉じている。
- **原則3：** verified と required の区別を語彙で表し、散文に埋めない。
- **原則4：** 診断の「明示的な前提条件と保証を伴う修正候補」を、CSP を待たずに基盤として提供する。

### 9.3. 未確認事項

- **再解決の実装：** `Binding.CandidateEvaluation` は適用可能性の状態（`Applicable`・`CopyUnproven`）と計画を候補ごとに持つ。一つの引数の計画を差し替えて順位付けだけをやり直す経路は未実装で、その規模は U3 で見積もる。呼出しに期待型を待つ引数（§10.5）は再解決の対象外とし、選ばれた候補の数だけを確立する。
- **`unsafe` のデデント：** 提示条件の判定には SourceDocument のテキスト（`unsafe` 行の末尾）とリテラル節の span（複数行リテラル）が要る。`ControlFlowAnalysis` からそれらに届かなければ、インデント本体の候補を単一項目本体だけに絞る。
- **クライアントの挙動：** 範囲に基づく照合は、VS Code がカーソル位置の範囲で要求することを前提にしている。他のクライアントは U5 で確認する。
- **制限値：** 記録あたりの候補数の上限は仮に 4 とし、U1 で `DiagnosticLimits` に置く。
- **オプション名：** `--Format` は既存の `--Target`・`--Debug` に合わせた。bool 展開の特例を避けるため文字列値とする。
- **費用：** 候補 1 件あたりの割当て、再解決の時間、`sha256` の時間は、実装後に測ってから記録する。本書は数値を主張しない。
