# 仕様変更案：構造化した修正候補

日付：2026-10-02（第 3 版：精査後）

状態：提案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書は、診断レコードに**修正候補（repair candidate）**を加え、`kimi check` の JSON 出力と言語サーバーの code action で機械が受け取れるようにする。本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例は独立した断片であり、「エラー」と記した行は意図した拒否例である。

改版：第 2 版は保証と前提条件を一つの条件の語彙に統合した。第 3 版は、`Selection` の確立を宣言グループ全体の再評価に改め、条件の判定に「不成立」を加え、Loan・寿命の条件を `UsageLegality` として明示し、`unsafe` 除去を文の位置に限り、編集の生成規則と制限を定め、code action の照合・有効性・クライアント機能・contributor の規則を改めた。

## 1. 現仕様の問題

1. **検証した修正を散文にしか書けない。** SPEC §10.6・§7.3・§14.3.3・§14.5.1・§17.2.4・§17.4.3 は、修正を検証してから示すことを要求する。しかし診断レコード（SPEC §23.3.6.2）に修正の欄はなく、§23.3.2・§23.3.6.1 は「Advice と Note の散文から機械適用可能な編集を推論しない」と定める。実装も同じで、`ReportAcquisitionConflicts` は `CopyProven` と `OffersTake` を判定してから英文を選び、`WarnUnnecessaryUnsafe` はスコープの変化を判定してから英文を選ぶ。判定の結果は捨てられる。
2. **機械が受け取る経路がない。** JSON 形式（§23.3.6.8）はどのコマンドも出力せず、言語サーバー（§23.4.7）は label・Note・Advice を message に連結し、CSP（§23.5）は未実装である。
3. **検証済みと未検証の区別が散文に埋まる。** 取得競合の Advice は、常に候補を一つに選ぶ `@ref` と、以後の使用に条件が付く `@move` を同じ文で勧める。
4. **原則4との乖離。** 原則4の「明示的な前提条件と保証を伴う修正候補」は、§23.5.3 で CSP の将来要件とされ、Appendix D で保留になっている。修正候補は Reason や関連位置と同じくレコードの一部であり、CSP を待つ必要がない。

## 2. 理想的な動作

1. 診断レコードは、問題を解消する代替案を**修正候補**として持つ。候補は、入力の不変テキストに対する編集と、条件の判定結果からなる。
2. 候補に関係する条件を閉じた語彙で名指し、検査が自身の事実から確立した条件を **verified**、確立しなかった条件を **required** として示す。成立しないと確定した必須条件がある候補は提示しない。
3. 候補は、効果が検査の事実から決まる修正に限る。選択を要する修正は Advice が述べる。
4. 候補のある記録は、同じ編集を Advice で繰り返さない。すべての出力が同じ候補を示す。
5. `kimi check --Format json` が公開スキーマの JSON を出力し、言語サーバーが `textDocument/codeAction` で候補を quick fix として返す。
6. ツールは候補を自動では適用しない。候補を適用した結果は新しい入力であり、その妥当性は新しい検査でだけ確立する。検査が読んだテキストと異なるスナップショットへは適用しない。

## 3. 仕様の変更点

### 3.1. 修正候補（新設 SPEC §23.3.6.9）

**構成。**

| 要素 | 内容 |
| --- | --- |
| kind | 閉じた目録の安定名。編集の意図を表し、title のテンプレートと事実の名前・種類を定める |
| title | kind のテンプレートと候補の事実から作る一文 |
| facts | Reason と同じ型付き事実 |
| edits | 編集の列。各編集は、source table の項目（記録された入力に限る）、その不変テキストの UTF-16 位置と長さ、置換文字列からなる。表示データとして行・文字の範囲を持つ |
| verified | 確立した条件の集合 |
| required | 確立しなかった条件の列。各項目は条件の名前と、テンプレートと事実から作る句 |

**条件。** 語彙は閉じており、追加には仕様変更を要する。

| 条件 | 意味 |
| --- | --- |
| `Selection` | 呼出しが候補をちょうど一つ選ぶ |
| `Copy` | 取得する値が Copy と証明済み |
| `Take` | Place が Take を提供し、Movable Place の条件（§15.1.5）を満たす |
| `ExclusiveAccess` | 貸与点が排他的に書き込み可能（§15.1.5） |
| `UsageLegality` | 選ばれた操作と、影響を受ける Place の以後の各使用が、使用合法性（§10.6：初期化と Move の状態、Loan、寿命）を満たす |
| `Structure` | 既存の名前の可視範囲、破棄と `defer` の順序、各式の評価 Context と結果の供給、既存の制御転送の対象が変わらない。候補が加える構造は title が述べる |

候補に**関係する条件**は、診断と kind の組で決まる（本書 3.7）。関係する各条件を、検査が自身の事実から次のいずれかに判定する：**verified**（確立した）、**required**（判定できない）、**不成立**（成立しないと確定した）。不成立の条件が一つでもある候補は提示しない。再検査はしない。Loan と寿命は修正後の解析を要するので、この改版では `UsageLegality` は常に required である。

`Selection` は、競合した呼出しの committed function group（§10.1）全体について、名前の探索結果と他の引数の型付けを再利用し、編集後の引数で §10.1 の手順、§10.2.2 の競合検査、§10.4 の順位付けを試行してちょうど一つの候補が選ばれたとき verified とする。編集前に除外された宣言も再評価の対象である（本書 4.1）。他の引数の競合が残れば一つに選ばれないので、候補は提示されない。試行は計画を記録するだけで状態を変えず（§10.1）、呼出しに期待型を待つ引数（§10.5）は対象外とする。試行には作業量の上限を設け、超えたときは候補を提示しない。

**編集の規則。**

- 編集は、対象の構文の置換と、境界で必要になる区切りの補完からなる。挿入・置換後の文字列は完全なトークンまたは項目であり、隣接する識別子と結合しない（`a&&b` には `a and b` を生成する）。
- 行頭の削除は、実在するインデントの空白だけを対象にする。空行、改行、複数行にわたるリテラルの内部には触れない。
- 一つの候補の編集は互いに重ならず位置順に並び、同じ位置への挿入は一つに結合する。一括して適用し、正確な値を表示用に短縮・省略しない。
- 編集は記録された入力だけを対象にし、`compiler://` や生成された source には及ばない。

**提示の規則。**

- 候補は代替案であり、優先順位を持たない。ツールは要求なしに適用しない。
- 候補は、その効果が検査の事実から決まるときだけ提示する。
- 派生レコード（`PrerequisiteUnavailable_Kd`）は候補を持たない。
- 候補を適用した結果は新しい入力である。その受理は新しい検査でだけ確立し、候補から何も推論しない。
- 順序は kind の目録順、次に最初の編集の位置とする。

**目録。** この改版の kind と、診断ごとに関係する条件は本書 3.7 のとおりで、§23.3.6.9 に各章への参照つきで列挙する。追加は仕様変更である。

### 3.2. レコード、制限、順序、契約（SPEC §23.3.6.2、§23.3.6.5–§23.3.6.7）

- §23.3.6.2 のレコード表に `Repairs` 行を加える：「修正候補（§23.3.6.9）。問題を解消する代替的な構造化編集の列」。
- §23.3.6.5：記録あたりの候補数、候補あたりの編集数、候補の総データ量に制限を設ける。超過した候補は丸ごと省き、Omissions（`repair candidates`）に数える。編集を途中で切り捨てることはない。
- §23.3.6.6：レコードの等価性は候補を含む。順序は変えない。
- §23.3.6.7：次を診断契約の違反に加える。編集のない候補、重なる編集、入力範囲外の編集、記録された入力でない source への編集、派生レコードの候補、目録にない kind や条件、関係する条件が verified と required のちょうど一方に現れない候補。

### 3.3. Advice との関係（SPEC §23.3.2、§23.3.6.1）

「Advice と Note の散文から機械適用可能な編集を推論しない」は維持し、次を加える。修正候補は、機械が適用できる唯一の形式である。§23.3.6.1 の「suggested intent is Advice, which states its conditions」を「suggested intent is a repair candidate with its conditions, or Advice」に改める。候補のある記録の Advice は、候補が表せない条件と代替案（不成立で提示しなかった修正を含む）を述べ、候補の編集を繰り返さない。

### 3.4. コマンドと JSON 出力（SPEC §23.3.6.8、IMPL §20.8）

**コマンド。** 候補のある記録は Advice の後に候補を描画する：`Repair: <title>`、各編集を ` = path:line:column: insert '<text>'`（置換・削除も同形）、最後に ` = verified: <条件>; requires: <句>`。

**JSON。** `kimi check <project> --Format json` は、共有の検査入口（§23.3.2）で一つの検査単位を検査し、その出力を一つの JSON 文書として標準出力に書く。他の出力は標準エラーに書き、終了コードは変えない。`--Format text`（既定）は現行の描画である。入口を共有するので、内容は言語サーバーが同じ入力で得る結果と等しい。

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

フィールド名は camelCase、列挙値は文字列、位置は `{ start, length }`（UTF-16）、範囲は 0 始まりの行・文字とする。スキーマは `docs/spec/schemas/check-output.schema.json` に置き、§23.3.6.8 がその版を規定する。`sha256` は検査と同じバイト列に編集を当てるためのもので、JSON 出力のときだけ読取り時に計算する。

### 3.5. 言語サーバーの code action（SPEC §23.4.1、新設 §23.4.8）

- **機能：** クライアントが `textDocument.codeAction.codeActionLiteralSupport` と `workspace.workspaceEdit.documentChanges` を宣言したときだけ、`codeActionProvider: { "codeActionKinds": ["quickfix"] }` を広告する。`codeAction/resolve` と `command` は使わない。
- **照合：** `textDocument/codeAction` は、検査を走らせずに、その URI に最後に送った診断から答える。要求の `context.diagnostics` は使わない。送信済みの診断は、要求の範囲と次の規則で照合する：両方が非空なら半開区間が交差する、一方が空ならその点が他方の範囲に端を含めて入る、両方が空なら同じ点である。
- **有効性：** 候補は、それを送った検査結果が valid（§23.4.5：記録された入力に新しい revision も mark もない）である間だけ返す。文書の変更イベントは mark を付けるので、同じ version の変更、閉じて開き直した文書、同期不成立、依存ファイルだけの変更のいずれでも候補は返らず、次の採用で戻る。
- **応答：** 一致した診断について、その文書内に全編集が収まる候補ごとに一つの `CodeAction` を返す：`kind` は `quickfix`、`title` は候補の title（required があれば `; requires <句>` を続ける）、`diagnostics` はその送信済み診断、`edit.documentChanges` は現在の文書 version を付けた `TextDocumentEdit`。`context.only` が `quickfix` を含まないときは空を返す。他の文書に編集が及ぶ候補は、この改版では返さない。
- **contributor：** 一つの URI に複数の単位が同じ診断を送るとき（§23.4.7 のマージ）、候補はすべての contributor で等しいときだけ返す。候補の一覧は結果の採用時に更新し、payload が再送されないときも更新する。
- 現行の §23.4.8（監視と設定）と §23.4.9（例）は §23.4.9・§23.4.10 に繰り下げる。

### 3.6. CSP との分担（SPEC §23.5、Appendix D）

§23.5.2 で修正候補と JSON 形式を「提供済みの基盤」に移す。§23.5.3 の要件は「候補を識別したスナップショットに対して適用し、適用後の検査を検証可能な変更として結び付ける。`UsageLegality` のように修正後の解析を要する条件は、その検査で確立する」に改める。Appendix D の CSP 行から「repair candidates」と「public schema」を外す。

### 3.7. 候補を定める診断

各章の「suggest」「fix」の文を、候補か Advice かに分類して書き改める。「判定」の列は、関係する条件の判定が verified・required・不成立のどれになるかを示す。

| 診断（SPEC） | kind と編集 | 関係する条件と判定 | 提示条件 |
| --- | --- | --- | --- |
| `AcquisitionRequired_Kd`（§10.6） | `Repair.Borrow`：引数全体に `@ref` | Selection：再評価。UsageLegality：required | Selection が verified |
| 〃 | `Repair.Copy`：`@copy` | Selection：再評価。Copy：保留した証明。UsageLegality：required | 同上。Copy が verified |
| 〃 | `Repair.Transfer`：`@move` | Selection：再評価。Take：取得計画。UsageLegality：required | 同上。Take が verified |
| `TransferRequired_Kd`（§3.5） | `Repair.Transfer`：`@move` | Take：取得計画で verified か required、提供しない Place（借用先、公開された Place、static）は不成立。UsageLegality：required | Take が不成立でない |
| `TransferRequired_Kd` の Capture エントリ（§7.6.2） | `Repair.Transfer`：`x@move`。`Repair.Borrow`：`x@ref` | 転送：上の行と同じ。借用：UsageLegality：required | 転送は上の行と同じ。借用は常に |
| `ExclusiveBorrowRequired_Kd`（§7.3、§15.1.5） | `Repair.BorrowExclusively`：`@uniq`、オブジェクト handle なら `@objuniq` | ExclusiveAccess：取得計画で verified か required、`let` 根・所有型の引数・getter 結果は不成立。UsageLegality：required | ExclusiveAccess が不成立でない |
| `UnnecessaryUnsafeBlock_Kd`（§14.3.3） | `Repair.RemoveUnsafe`：`unsafe => ` の削除、またはインデント本体の `unsafe` 行の削除と本体各行のデデント | Structure：本体が名前を宣言せず `defer` を登録しないとき verified、そうでなければ不成立 | `unsafe` 文がインデント本体の直接項目である。`unsafe` の行に他のトークンやコメントがない。本体に複数行リテラルがない |
| `MisplacedSyntax_Kd` の `AmpersandAmpersand`・`BarBar`（§2.4） | `Repair.ReplaceToken`：`and`・`or` | なし | 常に |
| `BorrowOriginKeyword_Kd`（§3.3.6） | `Repair.ReplaceToken`：`during` | なし | 常に |
| `MissingSyntax_Kd` の閉じ記号（DIAGNOSTICS §4.4） | `Repair.InsertToken`：挿入点に閉じ記号 | なし | 挿入点が確定している閉じ記号 |
| `DiscardedResult_Kd`、try 成功値の破棄警告（§17.4.3） | `Repair.PropagateFailure`：`_ = try ` を挿入（Result 破棄のとき）。`Repair.ExplicitDiscard`：`_ = ` を挿入 | Structure：verified | 式がインデント本体の直接項目である。伝播は、囲む関数の失敗の戻り先が §17.2.4 で適合するとき。順序は §17.4.3 のとおり伝播、明示破棄 |
| `DiscardedValue_Kd`（§17.4.2）、§7.3 の受信者式、§14.5.1 のコロン、§15.3.2・§15.4.3 の Origin | 候補なし | — | 選択か、Loan 条件の検証を要する。Advice が述べる |

### 3.8. 変更しないこと

コード・Reason・関連位置・Note・Advice の定義、問題の同一性、前提関係と抑制、結果の受理、言語の妥当性。修正後の再検査による条件の確立は CSP の「検証可能な変更」で別に設計する。

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
      "verified": ["Selection"],
      "required": [{ "condition": "UsageLegality", "phrase": "the call and later uses of node satisfy the initialization, Loan and lifetime conditions" }] },
    { "kind": "Repair.Transfer", "title": "Append @move to transfer node to process(value: Node)",
      "edits": [{ "source": 1, "span": { "start": 122, "length": 0 }, "text": "@move" }],
      "verified": ["Selection", "Take"],
      "required": [{ "condition": "UsageLegality", "phrase": "the call and later uses of node satisfy the initialization, Loan and lifetime conditions" }] }
  ]
}
```

コマンドの描画（Advice の後）：

```text
Repair: Append @ref to borrow node for process(value: ref/Node)
 = Main.kimi:5:21: insert '@ref'
 = verified: Selection; requires: the call and later uses of node satisfy the initialization, Loan and lifetime conditions
Repair: Append @move to transfer node to process(value: Node)
 = Main.kimi:5:21: insert '@move'
 = verified: Selection, Take; requires: the call and later uses of node satisfy the initialization, Loan and lifetime conditions
```

編集前に除外された宣言も再評価する。次では `f(node)` の競合は第一・第二宣言の間に起きるが、`node@ref` では第三宣言が `T = ref/Node` で適用可能になり、第二宣言と順位が並ぶ。再評価が二つの候補を返すので、`Repair.Borrow` は提示しない。

```kimi
func f(value: Node) -> i32 => 1
func f<T>(value: ref/T, extra: i32 = 0) -> i32 => 2
func f<T>(value: T, extra: i32 = 0) -> i32
    T is Copy
    return 3
f(node)                        // エラー：取得競合。候補は Repair.Transfer だけ
```

### 4.2. 不要な unsafe

```kimi
unsafe
    update(counter@uniq)    // unsafe 権限を使う操作がない
    publish()
```

`Repair.RemoveUnsafe` は三つの編集を持つ：`unsafe` の行の削除と、本体二行の実在するインデント一段の削除。verified は Structure。

```kimi
unsafe
    let address = pointer@usize    // 名前を宣言する：Structure が不成立
    publish(address)

let value = do => unsafe => 42     // 文の位置でない：除去すると do の結果が Unit から i32 に変わる
```

どちらも候補を提示せず、Advice だけを残す。

### 4.3. 記号の論理演算子

```kimi
if a&&b => fire()               // エラー：MisplacedSyntax_Kd（AmpersandAmpersand）
```

`Repair.ReplaceToken` は `&&` を ` and ` に置換し、`a and b` を生成する。識別子に隣接しない `a && b` では `and` だけを置換する。

### 4.4. 破棄と伝播

```kimi
func run() -> Result<(), Error>
    prepare()               // 警告：DiscardedResult_Kd。prepare は Result<Data, Error> を返す
    return .Ok(())
```

候補は `Repair.PropagateFailure`（`_ = try prepare()`：戻り先 `Result<(), Error>` に `Error` が適合する）と `Repair.ExplicitDiscard`（`_ = prepare()`）。verified はどちらも Structure。`if ready => prepare()` の単一項目本体では、`_ =` が Context を変えるので提示しない。

### 4.5. 言語サーバー

カーソル（空範囲）が診断の範囲 4:19–4:23 の中にある要求：

```text
→ {"jsonrpc":"2.0","id":3,"method":"textDocument/codeAction","params":{"textDocument":{"uri":"file:///C:/work/Main.kimi"},"range":{"start":{"line":4,"character":21},"end":{"line":4,"character":21}},"context":{"diagnostics":[]}}}
← {"jsonrpc":"2.0","id":3,"result":[
     {"title":"Append @ref to borrow node for process(value: ref/Node); requires the call and later uses of node satisfy the initialization, Loan and lifetime conditions",
      "kind":"quickfix","diagnostics":[{…送信済みの診断…}],
      "edit":{"documentChanges":[{"textDocument":{"uri":"file:///C:/work/Main.kimi","version":3},
              "edits":[{"range":{"start":{"line":4,"character":23},"end":{"line":4,"character":23}},"newText":"@ref"}]}]}},
     {"title":"Append @move to transfer node to process(value: Node); requires …","kind":"quickfix","diagnostics":[{…}],
      "edit":{"documentChanges":[{"textDocument":{"uri":"file:///C:/work/Main.kimi","version":3},
              "edits":[{"range":{"start":{"line":4,"character":23},"end":{"line":4,"character":23}},"newText":"@move"}]}]}}]}
```

この後に `didChange` が届くと（version が変わらなくても）結果は held になり、次の採用までは同じ要求に `[]` を返す。

## 5. 評価

**メリット。** 原則4の修正候補を CSP を待たずに提供する。エージェントは JSON の `edits` と `sha256` で検査と同じバイト列に編集を当て、`required` で引き受ける条件を知る。既存の判定が散文ではなく事実として残り、候補と Advice の役割が分かれる。言語の妥当性は変わらない。

**費用。** §23 に目録（kind）と語彙（条件）が増え、候補の追加は仕様変更になる。診断を加える作業に「候補か Advice か」の判断が加わる。`Selection` の再評価は競合した呼出しの宣言グループ全体に及び、作業量の上限で抑える。誤った verified は利用者を誤らせるので、記録箇所ごとに不成立・required の対照例を試験で固定する。

**複雑性。** 利用者から見える概念は「候補」「条件」「verified／required」で、条件は六語である。実装は、記録側の構造体一つ、目録一つ、最終化の再配置と契約検査、CLI オプション一つ、LSP の要求一つで、関連位置と同じ経路を使う。U1–U2 で CLI、U3 で取得競合、U4 で JSON、U5 で quick fix と段階的に価値が出る。

## 6. 既存の提案との関係

- **`2026-10-02 Parameter Acquisition Shape.md`（提案中）：** 採用されると `AcquisitionRequired_Kd` と取得競合がなくなり、本書 3.7 の最初の三行と `Selection` の再評価を削除する。`TransferRequired_Kd` と `ExclusiveBorrowRequired_Kd` の候補は残る。先にその提案を採用するなら本書 U3 は省ける。
- **`2026-09-28 Language Server and Compiler Services.md`（凍結）：** 「automatic fixes は範囲外」は言語サーバーの初版の範囲であり、本書はその基盤の上に候補を加える。
- **`2026-09-29 Diagnostics.md`（凍結）：** 将来項目「Repair は、前提条件と保証を示せる修正から導入する。古い入力や編集の競合は拒否する」を本書が実現する。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進め、各単位は AGENTS.md の Diagnostic Development Workflow に従う。PLAN には「Repair track」として R1–R7 の行を加える。

### 7.1. 実装単位

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U1：レコードモデル | `DiagnosticRepair.cs`（`DiagnosticRepair`・`DiagnosticEdit`・`RepairCondition` 列挙と判定状態）。内部表現は「条件名＋判定＋事実」に統一し、公開形式で verified と required に分ける。手書きの `RepairKind` 列挙と `DiagnosticRepair.tinyhand`（Title テンプレート、事実、条件の句。`DiagnosticRequirement.tinyhand` と同じ方式で整合テストを置く）。`DiagnosticFact` に記録側の候補（Koto と span に基づく編集）を加え、`Koto.Report(repairs:)` と `DiagnosticCollection.Report` を拡張する。`DiagnosticOwner` は同一キーへの再報告で候補が異なれば契約違反とし、最終化で編集を source table に再配置し、`DiagnosticLimits` の三つの制限と Omissions、本書 3.2 の契約違反を検査する。`CheckDiagnostic.Repairs` と等価性、スナップショットの差分種別 `repair` | `DiagnosticRepairTest`（最終化、順序、制限、等価、各契約違反）と `DiagnosticContractTest` が通る。診断のない経路で割当てが増えない。既存スナップショットに差分がない |
| U2：構文・unsafe の候補とコマンド描画 | `TokenReader.Diagnostics.cs` で `&&`・`\|\|` の置換（区切りの補完を含む）と閉じ記号の挿入、`BorrowOriginKeyword_Kd` の置換。`WarnUnnecessaryUnsafe` の候補（文の位置、`unsafe` 行の末尾と複数行リテラルは SourceDocument のテキストとリテラル節の span で判定）。`Kimigayo.Render` の候補描画 | `SyntaxDiagnosticTest`（`syntax.json` に `Repairs` 期待欄）・`UnnecessaryUnsafeBlockTest`・`DiagnosticRelationTest`（空行の追加で編集位置だけが動く）が通る。提示条件を満たさない対照例（`a&&b`、`do => unsafe => 42`、名前を宣言する本体）で期待どおり。CLI 出力の目視レビューを `review/` に残す |
| U3：取得の候補 | `Binding.CandidateEvaluation` に、競合した呼出しの宣言グループ全体を差し替えた計画で再評価する試行（名前探索と他の引数の型付けを再利用、状態を変えない、作業量の上限つき）を加え、`ReportAcquisitionConflicts` で本書 3.7 の候補を作る。`AcquisitionAdvice*` の四定数を削除する。`TransferRequired_Kd`・`ExclusiveBorrowRequired_Kd` の候補（Binding と所有権解析の両方の報告箇所で Take・ExclusiveAccess の三つの判定） | `AcquisitionConflictTest`：SPEC §10.2.2 の例と本書 4.1 の三宣言、複数引数の競合、`let` 根と借用先の不成立で、候補の有無と判定が期待どおり。候補のある記録に重複する Advice がない。温まった再 Binding の割当てゼロを維持する |
| U4：JSON 出力 | `KimiOptions.Format`。JSON のとき `kimi check` は `CheckService.Run` でディスク入力の一単位を検査し、`CheckOutput` に `schema`・`compiler`・`unit`・`sources[].sha256` を付けて標準出力に書く。`docs/spec/schemas/check-output.schema.json` | `CheckCommandJsonTest`：標準出力が JSON だけ、Blocked・Faulted も JSON、順序の安定、ハッシュの一致、テキスト形式が不変、同じ入力でテキスト形式と同じ記録。スキーマはライブラリーを足さず、固定のフィールド名・列挙値の対照表で検査する |
| U5：code action | `Json.cs` の要求・応答型とクライアント機能、`LspMethods.CodeAction`、機能の条件付き広告。`LspSession` は単位ごとの候補一覧を結果の採用時に更新して保持し（`LspDiagnostic` には載せない）、state owner で結果の有効性、範囲の照合、contributor の一致を判定して同期に応答する | `LspCodeActionTest`：publish 後に候補が返る、同じ version の変更・依存ファイルの変更・再オープン・同期不成立の後は返らない、再採用で戻る、空範囲の照合、required が title に出る、`only` の尊重、機能未宣言なら広告しない、contributor の候補が異なれば返らない、他文書への編集は返らない。実サーバーで VS Code の Quick Fix を手動確認する。拡張（`src/kimi-ext`）はコードを変えない |
| U6：破棄と伝播の候補 | `ControlFlowAnalysis` の `DiscardedResult_Kd` と try 成功値の警告に、位置判定つきの `Repair.PropagateFailure`（戻り先の適合は Binding の結果で判定）と `Repair.ExplicitDiscard` | 既存の破棄警告テストに候補と対照例（単一項目本体、分岐の末尾、適合しない戻り先）を加える |
| U7：評価と閉鎖 | コーパス全体のスナップショット、CLI・LSP の目視レビュー、`Benchmark --diagnostics` の前後比較（大きい `unsafe` 本体、多数の引数競合、候補の多い呼出しを含む）、DIAGNOSTICS §9 の評価、STATUS・PLAN の更新 | Session 検証が通る。診断のない経路の割当て差がゼロであること、300 エラーのプログラムで候補 1 件あたりのバイト数と再評価の時間を記録する |

### 7.2. 性能方針

- 診断のない経路に割当てを加えない（U1 の完了条件で固定し、`Benchmark --diagnostics` で確認する）。候補は失敗した検査と警告にだけ付く。
- 編集の文字列は定数か、警告時にだけ作る小さな文字列である。制限（候補数、編集数、総データ量）が生成・保持・応答の費用を抑える。
- 条件は検査自身の事実だけから立て、再検査しない。`Selection` の再評価は編集に依存しない判定結果を再利用し、作業量の上限を超えたら候補を提示しない。
- code action は検査を起こさず、state owner が保持する候補一覧と有効性を読むだけである。受信ループを止めない。
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
| SPEC §23.3.6.9（新設） | 本書 3.1 と目録 | 取り込み時 |
| SPEC §23.4.1、§23.4.8（新設）、§23.4.9–§23.4.10（繰り下げ） | 本書 3.5。繰り下げる節へのリンクを更新する | 取り込み時 |
| SPEC §23.5.2、§23.5.3、Appendix D | 本書 3.6 | 取り込み時 |
| SPEC §10.6、§7.3、§14.3.3、§14.5.1、§17.2.4、§17.4.2、§17.4.3、§2.4、§3.3.6、§3.5、§7.6.2 | 本書 3.7 のとおり、各文を候補か Advice に分類して書き改める | 取り込み時 |
| SPEC Appendix E | Repair candidate、Condition（verified／required） | 取り込み時 |
| `docs/SPEC.md` | §23 の要約に候補・JSON・code action を加える | 取り込み時 |
| `docs/spec/schemas/check-output.schema.json`（新設） | 本書 3.4 の文書形 | 取り込み時 |
| IMPL §20.8 | `--Format` | 取り込み時 |
| `AGENTS.md` | Diagnostic Development Workflow の Review に「修正は候補か Advice かを決め、候補の verified は事実から確立した条件に限り、不成立の条件がある候補は提示しない」を加える | 取り込み時 |
| `draft/INTEGRATED.md` | 本書の取り込みを記録する | 取り込み時 |
| `README.md` | Check の JSON 出力と Visual Studio Code の Quick Fix | 実装時（U4、U5） |
| `docs/dev/DIAGNOSTICS.md` | 新 §4.5（記録側の API、目録、条件の判定規則、再評価、契約）、§7（出力）、§9（評価） | 実装時 |
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
| 保証と前提条件を別々の語彙にする | 同じ条件が記録によって保証にも前提条件にもなる。一つの語彙を判定で振り分ける方が小さい |
| 「以後その Place を使わない」を転送の条件にする | Loan・寿命の条件の代わりにならず、再初期化した `var` の再使用を禁じて厳しすぎる。§10.6 の使用合法性を条件にする |
| 未判定と不成立を区別せず、常に候補を出す | Take のない Place や `let` 根に対して、成立しない修正を quick fix として示す |
| `Selection` を残った候補だけで判定する | 編集前に除外された宣言が編集後に適用可能になる（本書 4.1）。宣言グループ全体を再評価する |
| `unsafe` 除去を単一項目本体でも許す | `do => unsafe => 42` のように結果の供給が変わる。文の位置に限る |
| 編集を固定文字列の置換で生成する | `a&&b` が `aandb` になる。境界の区切りを補完する規則にする |
| 候補数だけを制限する | 一つの候補が大量の編集を持てる。編集数と総データ量も制限し、超過は候補ごと省く |
| 初版から修正後の再検査で Loan・寿命を確立する | 診断ごとに再コンパイルが要る。CSP の検証可能な変更として別に設計する |
| 候補に優先順位（`isPreferred`）を付ける | 意味の違う編集を自動で選ばない規則（SPEC §10.6）に反する |
| `codeAction/resolve` で編集を遅延生成する | 送信済みの結果から答えられる。状態の保持が二重になる |
| 要求の `context.diagnostics` と照合する | クライアントが message を改変しないことに依存する。範囲の照合で足りる |
| 文書 version の一致で古いスナップショットを判定する | §23.4.2 により同じ version で本文が変わり得る。依存ファイルの変更も見えない。結果の有効性（§23.4.5）で判定する |
| クライアント機能を確認せず code action を広告する | `CodeAction` と version 付き `documentChanges` は任意機能である。version なしの編集は古いスナップショットの拒否と両立しない |
| 表示が等しい診断の候補を一方の contributor から取る | 一方の target でしか確立していない条件を全体の verified にしてしまう。一致するときだけ返す |
| 古いスナップショットでは候補を `disabled` で返す | クライアント機能に依存し、次の採用で解消する。空を返す |
| kind と title を自由文字列で記録する | 「自由文字列を受け付けない」（SPEC §23.3.6.1）と一貫しない |
| JSON を `kimi check` の既定出力にする、または現行の CLI 経路から出す | 既定を変えると既存の検証証拠が変わる。結果の形は共有の検査入口にしかなく、入口を共有すれば言語サーバーと同じ結果になる |
| Result 破棄の候補を明示破棄だけにする | §17.4.3 は伝播を先に挙げる。戻り先が適合するときの伝播は事実から決まる |

### 9.2. Kimigayo Principles との対応

- **原則1：** 修正は候補という一つの形、条件は一つの語彙で表す。Advice は候補が表せない条件と代替案だけを述べる。
- **原則2：** 候補の効果と条件の判定は検査の事実だけから決まり、記録の中に明示される。再評価は競合した呼出しの宣言グループに閉じ、作業量に上限がある。
- **原則3：** verified・required・不成立の判定を語彙で表し、散文に埋めない。
- **原則4：** 診断の「明示的な前提条件と保証を伴う修正候補」を基盤として提供する。

### 9.3. 未確認事項

- **再評価の実装：** `Binding.CandidateEvaluation` は候補ごとの適用可能性の状態と計画を持つが、一つの引数の計画を差し替えて宣言グループ全体を再評価する経路は未実装である。規模と作業量の上限の値は U3 で見積もる。
- **`unsafe` の提示条件：** `unsafe` 行の末尾と複数行リテラルの判定に、`ControlFlowAnalysis` から SourceDocument のテキストとリテラル節の span に届く必要がある。届かなければ、インデント本体の候補を見送り、単一項目の `unsafe => stmt` が直接項目のときだけにする。
- **有効性の判定：** state owner が結果の valid を同期に判定するために、記録された入力ごとの revision と mark を単位の結果から引ける必要がある。既存の再検証の経路を再利用できるかは U5 で確認する。
- **制限値：** 候補数、編集数、総データ量の上限は U1 で `DiagnosticLimits` に置く。
- **オプション名：** `--Format` は既存の `--Target`・`--Debug` に合わせた文字列値である。
- **費用：** 候補 1 件あたりの割当て、再評価の時間、`sha256` の時間は、実装後に測ってから記録する。本書は数値を主張しない。
