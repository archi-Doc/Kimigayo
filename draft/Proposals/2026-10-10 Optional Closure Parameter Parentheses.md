# クロージャの単一引数の括弧省略

日付：2026-10-10（同日改訂：0引数は `()` 必須とし、`func` の判別規則と正規形を確定）。状態：文法方針は利用者合意済み。正式仕様への取り込み・実装は未実施。

## 1. 現在の仕様

[§7.6.1](../../docs/spec/07-functions-and-callable-values.md#761-syntax-and-inference) と [付録 F.4](../../docs/spec/appendices/F-syntax-summary.md#f4-expression-grammar) では、関数式は `func`、任意のキャプチャリスト、括弧付き引数リスト、任意の戻り値注釈、本体からなる。引数型を省くには、チェック前に確定した期待シグネチャが必要で、本体や後の使用からは推論しない。

```kimi
let twice = func (value: i32) => value * 2
let positive: (i32) -> bool = func (value) => value > 0
let zero: () -> i32 = func () => 0
```

式と宣言の両方を書ける項目の先頭では、実装は `func` の次が `(` か `[` なら関数式、それ以外なら関数宣言として読む。仕様にはこの判別の明文がない。

## 2. 問題

- 最も多い短いコールバックは型注釈のない1引数である。その括弧は情報を足さず、呼び出しの括弧と重なって読みにくい。

  ```kimi
  values.removeAll(matching: func (value) => value > 25)
  ```

- 項目の先頭での `func` の判別が仕様に書かれていない。

## 3. 新しい仕様

### 3.1. 単一名引数

型注釈のない1引数は、括弧を省いて名前だけで書ける。これを**単一名引数**と呼ぶ。それ以外の引数部は括弧付きリストで書く。

```ebnf
FunctionExpression           := "func" CaptureList? FunctionExpressionParameters
                                ("->" FunctionResult)? AnonymousBody
FunctionExpressionParameters := Name
                              | "(" TaskSlot? TrailingList<AnonymousParameter>? ")"
```

| 引数 | 書き方 |
| --- | --- |
| 型注釈のない1引数 | `func x`（`func (x)` も可） |
| 0引数 | `func ()` |
| 複数・型注釈付き・タスクスロット付き | `func (x, y)`、`func (x: i32)`、`func (task; x)` |

```kimi
let positive: (i32) -> bool = func value => value > 0
let shift: (i32) -> i32 = func [offset] value => value + offset
let doubled: (i32) -> i32 = func x -> i32
    return x * 2
let zero: () -> i32 = func () => 0
let typed = func (x: i32) => x * 2

// let sum: (i32, i32) -> i32 = func x, y => x + y // エラー：複数の引数には括弧が必要
// let bad = func x: i32 => x * 2                  // エラー：型注釈付きの引数には括弧が必要
// let none: () -> i32 = func => 0                 // エラー：0引数は `()` と書く
// let held: () -> i32 = func [count] => count     // エラー：キャプチャリストの後にも引数部が必要
```

### 3.2. `func` の判別

判別は位置と次のトークンだけで決め、型チェックの結果で読み直さない。

| 位置 | 規則 |
| --- | --- |
| 式と宣言の両方を書ける項目の先頭 | 次が `(` か `[` なら関数式、それ以外は関数宣言（現行実装と同じ） |
| 宣言だけを書ける位置 | 関数宣言（現行どおり） |
| 式の位置 | 関数式。`func`（とキャプチャリスト）の後の Name に `(` が続く場合は、関数式に名前を書いた誤りとして報告する（現行どおり） |

```kimi
func run => work()     // エラー：宣言の `(` がない。`run` を引数とは読まない
let f = func g() => 1  // エラー：関数式は名前を持てない
```

項目の先頭にある `func x => …` を関数式と読んでも、値を捨てる位置には期待シグネチャがないため、必ず引数型不足のエラーになる。宣言として読めば、`(` の欠落を正しく指摘できる。

### 3.3. 意味と表記

- `func x` は `func (x)` と同一の関数式である。違いはソース上の表記と範囲だけである。構文解析より後の規則（引数型の推論、引数の個数、キャプチャ、所有権、共通関数型への変換、タスクスロット）はすべて括弧形に対して定義され、変更しない。
- 単一名引数の範囲は名前トークンとし、ソースにない括弧を作らない。
- タスクスロットは括弧付きリストでだけ書ける。`func task => …` の `task` は通常の引数名である（[§2.5.1](../../docs/spec/02-source-and-lexical-structure.md#251-keywords)）。
- 改行の規則は [§2.2](../../docs/spec/02-source-and-lexical-structure.md#22-lines-indentation-and-continuation) のままとする。ヘッダーが行をまたげるのは区切り記号の内側か先頭の `->` だけなので、単一名引数は `func`（キャプチャリストがあればその `]`）と同じ行に書くことになる。

### 3.4. 正規形

- 型注釈のない1引数は、括弧を書かない形を正規形とする（STYLE `[Kimi]`）。0引数は `()` だけなので、どの引数部も正規形は1つに決まる。
- 括弧形 `func (x)` は引き続き受理する。型注釈を足す編集をその場所だけで済ませるためである。
- ソース書き出し（Koto の WriteTo。Hover や再出力が使う）は正規形で書く。表記を保持するフラグは持たない。整形コマンドは現存せず、本提案でも追加しない。

## 4. 採らない案

| 案 | 採らない理由 |
| --- | --- |
| 0引数の括弧省略 `func => 0` | 得られるのは2文字だけである。`func [x] => x`（x をキャプチャする0引数）と `func x => x` の違いが括弧の有無だけになり、取り違えやすい。キャプチャリストの省略は「推論」なのに、引数部の省略は「0に確定」となり意味が逆になる。関数型 `() -> T`、呼び出し `f()`、宣言 `func f()` とも揃わない。 |
| キャプチャリストの後だけ `()` を必須にする | 例外が1つ増えるだけである。 |
| 項目の先頭で `func 名前` の次を見て判別する（`(` か `<` なら宣言） | 受理するプログラムは §3.2 と同じで、`func run => …` に的外れな引数型不足のエラーを出す。 |
| 型注釈付きの単一名 `func x: i32` | 呼び出しのラベルや辞書の `:` と紛らわしく、規則が増える。 |

## 5. 原則との対応

- **One Concept, One Canonical Form**：単一名引数は括弧形と同じ関数式であり、正規形を1つに定める。括弧形を残すのは、型注釈を足す編集を局所に保つためである。
- **Local Reasoning**：判別は位置と次のトークンで決まり、型情報で読み直さない。
- **Explicit Semantics**：所有権、キャプチャ、タスクスロットを暗黙化しない。変わるのは括弧の表記だけである。
- **Compiler Server Protocol**：実際の表記と範囲を保持し、既存の関数式モデルで検査・編集する。

代償は、引数の `func x` と宣言の `func f(` の見た目が近くなることである。§3.2 の位置規則と既存の診断で区別する。

## 6. 仕様変更計画（U1）

文書だけの単位とする。ビルド・テストは実行せず、差分・リンク・見出しアンカーと `git diff --cached --check` を確認する。正式仕様は英語で書き、本書に依存させない。

1. 変更する箇所
   - §7.6.1：構成要素の文を「括弧付きリストまたは単一名引数」に改め、§3.1〜3.3 の規則と受理例・拒否例を加える。
   - 例を正規形に直す：§7.6.1 の `func (value)` 2箇所、§7.6.4 の `func [offset] (value)`、§4.7.2 の `removeAll(matching: func (value) => …)`。Kimi ライブラリとマイルストーンには該当する式がない。
   - F.4：FunctionExpression を §3.1 の文法に改め、注記から §7.6.1 の判別規則を参照する。
   - [STYLE §2.1](../../docs/STYLE.md#21-formatting)：§3.4 の `[Kimi]` 規則を加える。
   - [SETTLED](../../docs/SETTLED.md)：0引数の括弧省略を採らない記録を加える（§4 の1行目）。
2. 変更不要と確認した箇所
   - §2.2 と §2.5.1：§3.3 の改行と `task` の扱いは既存規則から導かれる。
   - [§10.5](../../docs/spec/10-overload-resolution-and-inference.md#105-inference-boundaries-and-specialization) と [§24.2.1](../../docs/spec/24-suspension-and-asynchronous-tasks.md#2421-form)：単一名引数は括弧形と同一で、引数の個数とタスクスロットの扱いは変わらない。
   - LIBRARY：宣言は変わらない。
3. 記録
   - STATUS：単一名引数は仕様のみで未実装（`func x` は `x` の位置で `(` を期待する構文エラーになる）と記録する。
   - PLAN：U2〜U5 を次の作業に加える。
   - [INTEGRATED](../INTEGRATED.md)：全項目を取り込み済み（§4 は不採用とその理由）として記録し、本書を `draft/Changes` へ移す。

## 7. 実装計画

### 7.1. 共通事項

- 単一名引数は解析時に `(x)` と同じ関数式ノードになる。そのため Binding 以降、凍結中の旧 Analysis/Emission 経路、生成コードは変更せず、native fixture も追加しない。U2 は構文解析の回復だけを直し、Binding は既存の回復記録（`CodeContext.RecordRecovery`）の扱いに従う。
- 新しい SyntaxForm・診断コード・ノード種別・フラグは追加しない。[CODEMAP](../../docs/dev/CODEMAP.md) の入口と責務は変わらないため更新しない。
- 各単位は次の順で進める：再現、実装、所有クラスへのテスト追加、Unit 検証、差分の再利用・重複・効率の見直し、コミット（領域別の純増減行数を記載）、push。ステージは変更したファイルだけとする。
- 順序は U1 → U2 → U3 → U4 とする。U5 は U1 の後ならいつでもよい。最後に `./scripts/verify.ps1 -Mode Session` を1回実行する。

| 単位 | 内容 | 主な対象 |
| --- | --- | --- |
| U1 | 仕様の取り込み（§6） | 文書のみ |
| U2 | 関数式ヘッダーの終端エラーからの回復 | 構文解析の回復 |
| U3 | 単一名引数の受理 | 構文解析 |
| U4 | 正規形の書き出し | `FunctionKoto` の書き出し |
| U5 | VS Code 拡張の着色 | `src/kimi-ext` |

### 7.2. U2：関数式ヘッダーの終端エラーからの回復

既存の不具合で、括弧形でも起きる。U3 の `func x, y` と `func x: i32` は同じ経路を通るため、先に直す。

- 再現（2026-10-10 に確認）
  - `let f: (i32, i32) -> i32 = func (x) y => x + y` は3件を報告する：`y` の位置の ExpectedSyntax_Kd（行末を期待）、行末の本体欠落（MissingSyntax_Kd）、`func` の位置の TypeMismatch_Kd。
  - 呼び出しの引数 `use(func (x) y => x + y)` は4件を報告する：`y` の位置の1件、本体欠落、読み飛ばしで `)` を失ったための `)` 欠落（MissingSyntax_Kd）、`use` の NoApplicableOverload_Kd。次の行の独立した型不一致は正しく残る。
- 目標：原因の1件（`y` の位置）だけを報告し、後続の独立したエラー（次の行の型不一致など）は残す。
- 変更：関数式のヘッダーの行末検査が失敗したら、次の3点を共通の回復規則として行う。
  - 読み飛ばしを、囲んでいる区切り記号の閉じ括弧の手前で止める。
  - 本体の欠落を報告しない。
  - その関数式を原因の回復として記録し（`CodeContext.RecordRecovery`）、それに依存する型不一致や多重定義の失敗を報告しない。
- テスト：`tests/diagnostics/syntax.json`（`SyntaxDiagnosticTest`）に、上の2つの再現を、次の行に独立した型不一致を置いた形で加える。期待する記録は、原因の1件と独立した型不一致の1件である。
- 検証：`pwsh -Command "./scripts/verify.ps1 -Class SyntaxDiagnosticTest,FrontEndSyntaxTest,CompilerSizeBudgetTest,ArchitectureRulesTest"`

### 7.3. U3：単一名引数の受理

- 再現：`let f: (i32) -> i32 = func x => x * 2` は現在、`x` の位置で `(` を期待する構文エラーになる。
- 変更（`Parser.ParseFuncDeclaration` の関数式の分岐と `TryParseFunctionName`）
  - `func`（とキャプチャリスト）の後が Name で、その次が `(` でなければ単一名引数とする。引数リスト解析の1引数の処理を再利用して `(x)` と同じ型なし1引数のリストを作り、ヘッダーの終端を名前の終わりにする。
  - Name の次が `(` なら、従来どおり `FunctionExpressionName` を報告する。
  - 項目の先頭での判別（`IntroducesDeclaration`、`ParseBlockItem`）は変更しない。
  - 単一名引数の後の `,` と `:` は、既存のヘッダー終端の検査（`ExpectLineEnd`）で報告する。
- テスト
  - `FrontEndSyntaxTest`
    - `PreservesSpecifiedSyntax` に `let a = func x => x`、`let a = func[k] x => x + k`、`let a = func x -> i32` と字下げ本体、`let a = func x => func y => x + y` を加える。
    - 単一名引数が `(x)` と同じ形（匿名、引数1つ、型なし、範囲は名前）になることを確かめる Fact を加える。
  - `tests/diagnostics/syntax.json`（`SyntaxDiagnosticTest`）
    - 新しい拒否：`func x, y => …`（`,` の位置）と `func x: i32 => …`（`:` の位置）。U2 の回復により記録は各1件になる。
    - 退行防止：`func => 0`、`func [k] => k`、項目の先頭の `func run => work()`。現在も、`=>` の位置で `(` を期待する1件だけを報告する。
    - 受理：既存の受理ケースに `func x` を加え、何も報告されないことを確かめる。
  - `ContextualClosureTest`：期待型のある束縛、Callable 引数、キャプチャリスト付きの各場合で、`func x` と `func (x)` が同一の IR を生成すること、期待シグネチャがないときは同じ診断コードを名前の位置に報告することを確かめる。
  - `HoverSyntaxTest`：単一名引数 `x` の Hover が、`func (x)` の `x` と同じ範囲と内容になることを確かめる。
- 検証：`pwsh -Command "./scripts/verify.ps1 -Class FrontEndSyntaxTest,SyntaxDiagnosticTest,ContextualClosureTest,HoverSyntaxTest,CompilerSizeBudgetTest,ArchitectureRulesTest"`
- 完了条件：上記の検証が成功し、STATUS の記録を「対応済み」に更新する。

### 7.4. U4：正規形の書き出し

- 変更：`FunctionKoto` の書き出しで、関数式の引数がタスクスロットなしの1つで、型注釈・既定値・属性がなければ `func x`（キャプチャリスト付きは `func [k] x`）と書く。それ以外は現行どおりとする。
- テスト：`FrontEndSyntaxTest` に、`func (x) => x` と `func x => x` がともに `func x => x` に、`func [k] (x)` が `func [k] x` に書き出され、`func ()`・`func (x: i32)`・`func (x, y)` は変わらないことを確かめるケースを加える。
- 検証：`pwsh -Command "./scripts/verify.ps1 -Class FrontEndSyntaxTest,PlaceResultParseTest,ControlFlowRevisionParseTest,CompilerSizeBudgetTest,ArchitectureRulesTest"`（関数式を書き出す既存のテストを含める）

### 7.5. U5：VS Code 拡張の着色

- 変更：[kimi.tmLanguage.json](../../src/kimi-ext/syntaxes/kimi.tmLanguage.json) の関数名の規則を、名前の後に `<` か `(` が続く場合（`\b(func)\s+(Name)(?=\s*[<(])`）に限る。単一名引数は関数名として着色しない。TextMate は位置を知らないが、正しいプログラムでは「名前の後が `(` か `<`」と「宣言である」が一致する。
- テスト：`src/kimi-ext/src/test/unit/grammar.test.ts` に、`func f(` と `func f<T>(` の `f` が関数名になり、`func x =>` と `func [k] x =>` の `x` が関数名にならないケースを加える。
- 検証：`npm --prefix src/kimi-ext ci` と `npm --prefix src/kimi-ext test`。リリースはしないため版数は上げない。

### 7.6. 完了

- Session 検証が成功したら、PLAN から U2〜U5 を外し、PLAN_HISTORY に数行で記録する。
- タスクスロットは未実装（`;` を字句解析で拒否）のため、`func (task; x)` と単一名引数の組み合わせは、タスクスロットを実装する単位で確かめる。
