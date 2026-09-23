# 借用 Origin の後置表記 — 最終仕様変更案

## 1. 位置付け

本変更案は、ここで変更する事項について `SPEC.md` およびその参照先より優先する。変更しない事項には既存仕様を適用する。本書は変更後の規則を定義する提案であり、正式仕様への反映や実装の完了を示さない。

借用 Origin を `during` で後置する。名前付き型の Origin スキーマに対する束縛集合（binding set）の命名 `{v}` と、型宣言のスキーマヘッダー `{source}` は維持する。

```text
現行：ref{a}/View<T>{v}
変更：ref/View<T>{v} during a
```

旧借用注釈 `ref{a}/T` などは廃止し、`from` を借用注釈として認めない。言語は pre-alpha であり、言語バージョンの変更や別途の移行文書は要求しない。

以下の例は型式・シグネチャなどの抜粋である。型・名前の解決、Origin の束縛、型形成、Loan などの条件は、それぞれ満たす必要がある。旧表記は比較のためにだけ使用する。

## 2. 現行表記の問題と目的

`ref{a}/View<T>{v}` では、同じ `{}` が借用 Origin の指定と binding set の命名を表す。現行構文でも位置によって解析できるが、読み手は役割を区別する必要があり、Origin の記述が `ref` と借用対象の型を分断する。

目的は、Semantics と対象型を続けて読み、Origin の役割と注釈先を構文で判別できるようにすることである。

## 3. 構文と注釈先

### 3.1. 記述順序

同じ階層の順序を **型本体 → 後置 `?` の列 → `during` とその引数** に固定する。関係する文法は次のとおりとし、その他の構文要素は既存仕様に従う。

```ebnf
Type             := FunctionType | AnnotatedType
FunctionType     := FunctionParameters "->" Type
AnnotatedType    := SemanticsType ("?")* BorrowOrigin?
SemanticsType    := Semantics "/" SemanticsType | TypeAtom
TypeAtom         := CoreType | "(" Type ")"
BorrowOrigin     := "during" OriginAtom
OriginExpression := OriginAtom ("and" OriginAtom)*
OriginAtom       := Name ("." Name)? | "static" | "(" OriginExpression ")"
```

`FunctionParameters`、`CoreType`、`Semantics`、`Name` は既存の構文・役割を引き継ぐ。タプル要素、配列要素、型引数などに含まれる型式にも `Type` を用いる。文法上解析できても、注釈先や使用位置が不適切ならエラーとする。

`during` は `AnnotatedType` の型本体と後置 `?` の列に続く位置で認識する文脈依存キーワードとし、それ以外では通常の名前として使用できる。認識は名前解決や注釈先の適格性に依存しない。空白によって解釈を変えず、専用の改行継続規則も追加しない。

```text
ref/T? during a            // 可
(ref/T during a)?          // 可：括弧の外で Optional 化
ref/T during a?            // 不可
ref/T during (a and b)?     // 不可
```

長いシグネチャは、既存の行頭 `->` によるヘッダー継続を使う。

```kimi
func borrow<T>(x: ref/Long<T>)
    -> ref/Long<T> during x
    return x
```

結果型と注釈も分ける場合は、括弧内の継続を使う。`->` の行で開いた括弧の内容はさらに一段深くする。本体はヘッダー開始行の一段下に置く。

```kimi
func borrow<T>(x: ref/Long<T>)
    -> (ref/Long<T>
        during x)
    return x
```

区切りが閉じた型の次の行から、`during` だけで継続することはできない。

### 3.2. 注釈先の決定

`during` は、**同じ `AnnotatedType` の `SemanticsType` が `Semantics "/" …` で始まる場合、その先頭の Semantics** に付く。`TypeAtom` で始まる場合は「対象なし」のエラーとする。Optional の展開、名前解決、別名展開、冗長な `owner` の除去によって対象を探し直さない。

型の後置構文（`{v}`・`?`・`during`）は、括弧の内側を注釈先として探索しない。`?` は直前の型全体を包む。名前付き型、型引数、タプル、配列、関数型などの内部も、外から注釈先として探索しない。

同じ `AnnotatedType` では、記述順は **本体 → `?` → `during`**、適用順は **本体 → `during` → `?`** である。`during` は Optional 化される前の先頭 Semantics に付く。

対象にできるのは `ref`・`uniq`・`objref`・`objuniq` と、安全な借用であることが宣言の条件から証明される Semantics パラメーターだけである。`owner`・`obj`・`rc`・`arc`・`unsafe` は対象にできない。構文で対象を固定し、その適格性を意味検査で確認する。

```text
ref/ref/T during a          // 外側の ref に付く
unsafe/ref/T during a       // 不可：対象は unsafe
unsafe/(ref/T during a)      // 内側の ref に付く
owner/ref/T during a        // 不可：対象は owner
owner/(ref/T during a)       // 内側の ref に付く
Option<ref/T> during a      // 不可：対象となる明示 Semantics がない
Option<ref/T during a>       // 型引数内の ref に付く
T during a                 // 不可：T の解決結果には依存しない
(ref/T)? during a           // 不可：対象なし
(ref/T?) during a           // 不可：対象なし
```

`during` 自体は型の層を追加しない。一つの `AnnotatedType` の注釈は文法上高々一つであり、括弧の外からの再注釈もできない。別途の重複探索や、上書き・暗黙の交差は必要ない。

```text
ref/T during a during b       // 不可：二つ目の during は文法外
(ref/T during a) during b     // 不可：外側の注釈先がない
(ref/T during a)? during b    // 不可：外側の注釈先がない
ref/(uniq/T during b) during a // 可：別々の借用層
```

### 3.3. Origin 式と制約の区切り

`during` の引数は単独の名前、射影、`static`、または括弧付き Origin 式とする。交差は `during (a and b)` と書く。`during` が消費するのは一つの `OriginAtom` までであり、その外側の `and` は周囲の構文に属する。

```text
ref/T during a
ref/T during value.source
ref/T during static
ref/T during (a and b)
T is ref/U during a and Copy
T is ref/U during (a and b) and Copy
origin a and b outlives c
```

複合 Origin の括弧を必須にする規則は `during` の直後に限る。注釈には終端の区切りがない一方、`origin` 関係句は `outlives`・`==` と句の終端で範囲が決まるため、既存の文法を維持する。型式だけを要求する位置の `ref/T during a and b` はエラーとする。制約式では `and b` を同じ対象への要件の連言として読み、名前解決によって Origin の交差に読み直さない。誤読への診断は §6.2 に示す。

`during (a)` は許可する。空の `during ()`、リスト状の `during (a, b)`、末尾カンマ付きの `during (a,)` は許可しない。`f(x: ref/T during a,)` のカンマは通常の引数リストに属する。`_`、呼び出し、任意の値式などを Origin の新しい記法として追加しない。

## 4. 型構文との組合せ

### 4.1. Optional と多層借用

`?` は Semantics 列全体より弱く、関数型の `->` より強く結合する。§3.2 の順序で、一つの `?` ごとに型全体を `::Kimi.Option` で包む。名前解決に依存せず、二重の Option も平坦化しない。

| 表記 | 意味・同値な型表記 |
| --- | --- |
| `ref/T during a` | 外側の ref の Origin が a |
| `ref/T? during a` | `Option<ref/T during a>` |
| `(ref/T during a)?` | `Option<ref/T during a>` |
| `ref/T?? during a` | `Option<Option<ref/T during a>>` |
| `ref/(T?) during a` | `ref/Option<T> during a` |
| `obj/T?` | `Option<obj/T>` |
| `ref/(ref/T during b)` | 内側の ref の Origin が b。外側は省略規則に従う |
| `ref/(uniq/T during b) during a` | 外側の ref が a、内側の uniq が b |
| `ref/(uniq/T during b)? during a` | `Option<ref/(uniq/T during b) during a>` |

表中の `Option` は `::Kimi.Option` を表す。型としての同値性は、任意の綴りに `during` を付加できることを意味しない。注釈先は常に §3.2 の構文規則で決める。

### 4.2. 関数型と複合型

関数型の末尾の `during` は戻り値の型式に属する。関数型そのものに対する借用には、Semantics とグルーピングを明示する。`->` の右結合性は維持する。

| 表記 | 意味 |
| --- | --- |
| `(A) -> ref/B during b` | 戻り値の借用 Origin が b |
| `(A) -> ref/B? during b` | 戻り値が `Option<ref/B during b>` |
| `ref/((A) -> B) during a` | 関数値への借用 Origin が a |
| `((A) -> ref/B) during a` | 不可：括弧の外に注釈先がない |

タプル・配列・型引数内の各型式には独立して適用する。例えば `(ref/T during a, ref/U during b)`、`[N of ref/T during a]`、`Pair<ref/T during a, ref/U during b>` の各注釈は、その要素・型引数内の借用だけを対象にする。

### 4.3. binding set とスキーマ

`View<T>{v}` は、その名前付き型の Origin スキーマに対する束縛集合に名前を付ける。既存の Origin や集合を適用する構文ではない。スキーマに含まれない型引数・フィールド・基底型の依存関係を再帰的に集約しない。

```text
ref/View<ref/U during c>{v} during a
```

この例では `a` は外側の借用、`c` は型引数内の借用、`v` は `View` のスキーマに対する binding set の名前であり、互いに異なる役割を持つ。`v` 自体を scalar Origin として使用できず、必要なスロットは `v.source` のように射影する。

`{v}` は名前付き型に付ける。`View<T>{v}?` と `(View<T>{v})` は許可するが、`View<T>?{v}` と `(View<T>){v}` は許可しない。型宣言のスキーマヘッダー、集合の命名条件・有効範囲・末尾カンマの規則は維持する。

例えば結果の `View<T>{result}` と関係句 `origin result.source == value` は、結果のスロットを入力の Origin に関係付ける。`View<T>{value}` で既存の入力 Origin を適用する省略形はない。直接の結果借用は `ref/T during value` と書く。

## 5. 意味と適用境界

### 5.1. Origin・借用・型形成

`S/V during O` は、選択された安全な借用層の Origin を `O` とする型注釈である。Origin は借用が有効なプログラム点の集合を表し、`and` はその交差を表す。`during x` の `x` が直接の借用値なら、その参照値が保持する Origin を指す。変数 x の格納領域の寿命を指すものではない。

注釈自体は値の取得・変換・再借用を実行せず、寿命を延長せず、Loan やアクセス権を生成しない。各層の依存関係、分散、型形成、適合性、排他性、破棄規則は既存仕様を維持する。例えば `ref/(uniq/T during b) during a` は、内側の b が外側の a 以上に長く有効であるという型形成条件も満たさなければならない。

意味の決定は **構文解析・注釈先の固定 → Optional 展開 → 既存の意味検査** と整理する。これは意味上の依存順序であり、コンパイラーのパス構成を指定するものではない。Copy・Owned・型同一性・ジェネリック分解・Origin の省略などは、展開後の完全な型で判定する。

### 5.2. 名前の導入・省略・ジェネリック

`during` への変更で、名前を導入できる位置や Origin の省略範囲を拡張しない。

- 通常の名前付き関数、コンストラクター、明示アクセサー、Contract の callable 要求では、許可されたシグネチャ注釈内の未束縛の単純名が普遍量化された Origin を導入する。結果だけに現れる名前も同じである。
- ローカルの注釈と Origin 関係句は既存名を参照する。ストレージのスキーマ収集・補完、名前の衝突・有効範囲、特殊化と継承契約の規則は維持する。
- Function Type、Callable、無名関数のシグネチャ内では、新しい名前付き scalar Origin を導入しない。Callable の直接借用注釈の禁止も維持する。
- 初期化子を持つローカルでは既存の推論を利用できる。入れ子の借用、格納型、公開シグネチャに新たな省略許可を与えず、名前付き関数の明記された結果契約を本体から推論しない。

```text
func f<T>(x: ref/T? during a) -> ref/T
```

この引数は展開後に Option であり、直接の借用入力ではない。既存の結果省略規則では、この戻り値の省略 Origin は `static` となる。a に依存する契約にするなら結果にも `during a` を指定する。これが関数本体から返せることを保証するわけではない。

この省略が結果の適合・寿命エラーの原因なら、入力内部の Origin は省略候補に含まれないことと、明示注釈の候補を診断に添える。正しく `static` を返す関数には一律の警告を要求せず、複数の候補から一つを自動選択しない。

ペア型引数の元の `s/T` は完全な型の Origin を保持する。`s/T during b` または `s/U during b` には s が安全な借用である証明が必要であり、その注釈は外側の Origin を b に指定する。型形成は内側の依存関係を検査し、実際の値の適合・短縮・Move・再借用は別に検査する。指定を省略した別ターゲット `s/U` の条件付き Origin スロットも既存規則を維持する。

### 5.3. Adaptation Target

Adaptation Target の最上位では `during` を読まない。末尾の借用注釈には `@(Type)` のグルーピングを使う。専用文法から前置の `BorrowOrigin` を削除し、後置には追加しない。

```ebnf
AdaptationType := AdaptationCore ("?")*
AdaptationCore := Semantics "/" AdaptationCore | AdaptationAtom
```

`AdaptationAtom` は既存の型の先頭構文を引き継ぐ。グルーピングの `"(" Type ")"` と、型引数・タプル要素・配列要素の型式には §3.1 を適用する。内部の注釈が既存の区切りで閉じられる `@Option<ref/T during a>` などに、さらに外側の括弧は要求しない。

注釈を含む型に続くメンバー選択・呼び出し・添字は、その型を閉じる区切りの外に書く。関数型全体のグルーピング、型引数と比較の区別、空白・隣接規則は既存どおりとし、外側の `->` は消費しない。Semantics 省略形と `@move` に注釈は追加しない。

型の先頭で識別子形の名前の次のトークンが `/` なら、再帰的に Semantics 接頭辞として読む。一方、型引数・binding set・グルーピングなどで型の先頭構文が完結した後の `/` は割り算である。空白ではこの区別を変えず、`Name{…}/` を旧借用注釈として読み直さない。

構文を確定してから注釈先を固定し、Optional 展開後の位置で使用可否を検査する。

- 対象型の外側 Semantics 列に書かれた借用 Origin は禁止し、操作・被演算子・制約から推論する。グルーピングではこの禁止を回避できない。
- Option などの aggregate 内の完全な型に付く注釈は、その内部の依存関係として保持できる。対応する取得・適合・Loan の条件は別途必要である。

| 表記 | 扱い |
| --- | --- |
| `saved@(ref/T? during a)`、`saved@Option<ref/T during a>` | 対象型は `Option<ref/T during a>` |
| `saved@(ref/T? during value.source).count` | `)` で対象型が終わり、`.count` は結果へのメンバー選択 |
| `saved@ref/T? during a`、`value@ref/T during a` | 構文エラー：最上位の `during` は不可 |
| `saved@(ref/T during a)?` | 対象型は `Option<ref/T during a>`。内部の注釈として保持可能 |
| `value@(ref/T during a)` | 外側借用への明示 Origin のため不可 |
| `value@ref during a` | 不可：Semantics 省略形には注釈を追加しない |
| `x@T / y` | 対象型を `T/y` として解析。T には Semantics としての適格性が必要 |
| `x@View<T>{v} / y` | `(x@View<T>{v}) / y`。binding set 付きの型が完結しているため割り算 |
| `x@s{a}/T` | `(x@s{a}) / T`。s が Semantics だけなら型として不適格。旧表記の補足診断は §6.2 |

`saved` が同じ完全な Option 型なら通常の取得が可能である。メンバー選択などの後続操作には、それ自体の適格性も必要である。借用してから Some で包む暗黙操作や、新しい型変換は追加しない。適合性の検査結果によって別の構文へ読み直さない。実行時 `is` など、一般の型構文を許可していない位置は拡張しない。

## 6. 出力と診断

### 6.1. 標準表記とソース位置

標準の出力は `ref/T? during a`、`during (a and b)`、`ref/(uniq/T during b) during a` とする。型本体・`?`・`during` をまとめて出力し、借用層と Optional の位置を保つ。関数型と Adaptation Target には必要な括弧を付け、`ref/T during a?` のような禁止形を生成しない。

注釈と対象 Semantics のソース位置を保持する。`ref/T? during a` では、注釈は `?` の後にあり、対象は先頭の `ref` である。意味上の対象への関連付けによって構文上の記述位置や親子の範囲を壊してはならない。具体的なノード構造は指定しない。

型表示全般・診断用内部名・保存形式に新たな保証は設けない。既存の意味情報保持要件は維持する。

### 6.2. 誤記への診断

構文・注釈先・Semantics の適格性・使用位置・名前解決のエラーを区別し、次を案内する。診断のために別の構文として読み直さない。

| 状況 | 診断・修正候補 |
| --- | --- |
| 型位置の旧借用注釈 | 対象層を保つ新表記。例：`ref{a}/uniq{b}/T` → `ref/(uniq/T during b) during a` |
| Adaptation の `Name{…}/…` が構文・型解決エラーとなり、Name の Semantics としての役割を確認できる | 通常のエラーに旧借用注釈の可能性を補足。外側 Origin を指定する Adaptation は不可であり、借用操作なら `x@s/T` で推論させる |
| 型だけを要求する位置で、型の直後の `from a` が構文エラーになる | 借用注釈は `during a` と案内。注釈先の適格性は別途検査 |
| `during x` の x が、外側に安全な借用を持たない値 | x の名前だけでは Origin を指定できない。宣言済みスキーマのスロットがあれば `x.slot`、ローカルの格納領域を借りる意図なら Borrow と推論を案内 |
| `(ref/T)? during a`、`(ref/T?) during a` | 対象なし。`ref/T? during a` を案内 |
| 同じ階層での `during` の繰り返し・括弧外からの再注釈 | 余分な後置注釈。括弧の外なら「対象なし」とし、最初の注釈を上書きしない |
| `x@ref/T? during a` | 最上位の注釈は不可。`x@(ref/T? during a)` を案内し、使用可否は別途検査 |
| 型だけを要求する位置で `during a and …` | 交差なら `during (a and …)` が必要 |
| 制約の `during a and b` で、右の単純名・射影が要件として無効だが既存の Origin として有効 | 通常の要件エラーに、交差の括弧を案内する補足を付ける |
| 型の直後の行の `during` が、改行した注釈と見られる構文エラーを生む | 同じ行に書くか、§3.1 のように型と注釈を括弧で囲むよう案内 |

連言の右項が型・Contract と Origin の両方として有効な場合は、正しい連言の可能性があるため、注意は任意の lint とする。`T is (ref/U during a) and B` のように型を括弧で区切れば、その注意の対象外とする。Origin としての補足検索では新しい名前を導入せず、通常の名前解決や受理結果を変えない。

旧表記の補足は構文エラー時だけでなく名前解決の失敗時にも行うが、正常な binding set と割り算を旧表記として警告しない。`x@(s/T during a)` は型表記の説明には使えても、外側借用注釈を許す修正候補にはしない。補足検索は受理結果や束縛を変えない。

Borrow の案内は、例えばローカルの `let r = x@ref` で Origin を推論する方法を示す。`during (x@ref)` は Origin 式ではなく、Borrow しても寿命は延びない。通常の名前として成立する `during`・`from` には、改行や旧注釈の診断を出さない。結果 Origin の省略の補足は §5.2 に従う。

### 6.3. 検証範囲

本書の例を受理・拒否の確認表とし、実装時には次を検証する。

- 型の各組合せ、括弧の境界、記述順序、複数注釈、全安全借用 Semantics と総称 Semantics の証明。
- 名前導入・省略・再構成・量化・既存の借用検査の意味保存。
- Adaptation の括弧内外の `?`、内部の型引数・要素、後続操作と割り算の境界、旧表記の解析結果と名前解決時の診断。
- 行頭 `->` と括弧内の改行、空白、通常の名前としての `during`・`from`、旧 `from` と所有値の Origin 誤記、正常なコードへの誤警告の防止。
- 標準表記での再解析、注釈と対象のソース位置、不正な多重注釈や深い入力からの回復。

受理例には既存の意味条件も必要であり、解析できるだけでは実装完了としない。

## 7. 利点・欠点・副作用

### 7.1. 利点

- 借用 Origin の指定と binding set の命名を表記で区別できる。
- Semantics と対象型を続けて読める。Optional も `ref/T? during a` と短く書ける。
- 多層借用の括弧が注釈先を示し、構文だけで対象と式の終端を決められる。

コンパイラーには、次の簡素化が見込める。これは実装指針であり、実測した性能向上ではない。

- 現行 `Parser.HasBorrowOriginSuffix` の、`{…}/` を探す先読み走査を通常の型解析から除ける。借用注釈の有無は、型本体と `?` の後の一トークンで判定できる。
- 同じ `AnnotatedType` の先頭 Semantics への参照を保持すれば、注釈先を O(1) で決められる。括弧を越える候補伝播、重複状態の表、構文木の再走査は不要になる。Origin 式自体の解析・検査に必要な処理は残る。

注釈のソース情報は明示注釈がある箇所だけに追加し、構文出現ごとの情報を共有された解決済み型に書き込まない。効果はコンパイル時間とメモリについて測定し、実行時の高速化とは区別する。

旧表記の診断のために先読み走査を戻さない。`Name{…}` は binding set と共通の区切り解析・エラー回復で一度だけ読み、閉じた直後の一トークンが `/` かを確認する。追加の形状判定だけが O(1) であり、括弧内の解析や必要な名前解決まで O(1) とするものではない。この情報は補足診断にだけ使い、構文選択を変えない。

### 7.2. 欠点と記述場所による負担

`during` によって単層の記述が長くなり、多層借用・複合 Origin・Adaptation の末尾注釈では括弧が増える。型と注釈の距離も長くなり得るため、長い型では §3.1 の改行を利用する。推論やジェネリックで常に負担を解消できるわけではない。

初期化子のあるローカルや完成した型をそのまま渡すジェネリックでは、既存の推論・保持規則により型全体の記述を省ける。一方、明示的な公開シグネチャ、内側の借用層、インスタンスフィールドや enum payload の格納契約では、必要な注釈と括弧が残る。

### 7.3. 副作用と変更範囲

取り込み時には、旧表記を使うソース・ライブラリ・例・milestone プログラム、パーサー・出力・診断・関連テストを更新する。正式仕様の直接関係する記述は、次の範囲で置換する。

| 既存の場所 | 置換・維持する内容 |
| --- | --- |
| `SPEC.md` の Origin 案内、第1章 §1.2 | 前置注釈への案内と `{}` の借用用途を後置 `during` に更新 |
| 第2章 §2.4・§2.5.1 | `{}` の役割から借用式を除き、`during` の文脈依存の用途を追加。`from` に Origin の役割がない点は維持 |
| 第3章の型の基本形、§3.2.3・§3.3・§3.3.6 | `Semantics{Origin}/Core`、前置注釈の説明・例を本書 §3–4 に置換。括弧が注釈先を移さない原則と Optional の展開規則は維持 |
| 第6章 §6.3.1 の enum の例と説明 | payload と直接借用を `during` に更新。結果の `{result}` は命名、`origin result.source == value` は関係指定と説明し、既存 Origin を適用する single-Origin shorthand の記述を削除 |
| 第15章 §15.3.1 | `/` の前の借用注釈、借用式の末尾カンマ、`Name{…}/` による brace の役割判定を削除。安全借用だけに許可する条件、型全体の括弧への注釈禁止、旧 `origin`・`from` 注釈の拒否は維持 |
| 第13章 §13.5.1・§13.5.5 | 「どの層にも書けない」「grouped and generic inner Types を含めて禁止」を、本書 §5.3 の外側 Semantics 列と aggregate 内部の区別へ統一。runtime `is` の制限は維持 |
| 付録 F.2・F.4・F.7 | 型・借用注釈・Adaptation の文法と Origin の用途説明を本書 §3・§5.3 に合わせる |
| 付録 A.12 | brace の借用用途と前置注釈の保持を、`during` の結合・出力・位置保持の検証に置換。契約保持の要件は維持 |
| 付録 A.22 | Semantics 接頭辞・関数矢印の結合検証を維持し、`?` と後置 `during` の順序、括弧の境界、Adaptation の Optional 展開後の検査を明記 |

第8章の総称型再構成を含む各章の旧表記も、対象層を保って書き換える。

この変更は借用層と Origin の意味を保存する。寿命・借用・推論の能力、既存コードが表す型の表現力を狭めず、ABI、ランタイムの寿命情報、実行時処理や割り当ての追加を要求しない。実装済みの対応範囲を拡大したとみなすものでもない。

関連仕様は、[字句・継続規則](../../spec/02-source-and-lexical-structure.md)、[型の合成](../../spec/03-types-and-values.md)、[ジェネリックと Callable](../../spec/08-generics-constraints-and-contracts.md)、[Adaptation](../../spec/13-operators-and-assignment.md)、[Origin](../../spec/15-ownership-and-lifetime-analysis.md)、[コンパイラー要件](../../spec/appendices/A-compiler-requirements.md)、[構文要約](../../spec/appendices/F-syntax-summary.md)を参照。取り込み後の正式仕様は自己完結させる。
