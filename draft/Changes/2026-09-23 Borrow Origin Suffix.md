# 借用 Origin の後置表記

## 1. 位置付け

本変更案は、ここで変更する事項について `SPEC.md` およびその参照先より優先する。変更しない事項には既存仕様を適用する。本書は変更後の規則を定義する提案であり、正式仕様への反映や実装の完了を示さない。

借用 Origin を `from` で後置する。名前付き型の Origin スキーマに対する束縛集合（binding set）の命名 `{v}` と、型宣言のスキーマヘッダー `{source}` は維持する。

```text
現行：ref{a}/View<T>{v}
変更：ref/View<T>{v} from a
```

旧借用注釈 `ref{a}/T` などは廃止する。言語は pre-alpha であり、言語バージョンの変更や別途の移行文書は要求しない。

以下の例は型式・シグネチャなどの抜粋である。型・名前の解決、Origin の束縛、型形成、Loan などの条件は、それぞれ満たす必要がある。旧表記は比較のためにだけ使用する。

## 2. 現行表記の問題と目的

`ref{a}/View<T>{v}` では、同じ `{}` が借用 Origin の指定と binding set の命名を表す。現行構文でも位置によって解析できるが、読み手は役割を区別する必要があり、Origin の記述が `ref` と借用対象の型を分断する。

目的は、Semantics と対象型を続けて読み、Origin の役割と注釈先を構文で判別できるようにすることである。

## 3. 構文と注釈先

### 3.1. 記述順序

同じ階層の順序を **型本体 → 後置 `?` の列 → `from` とその引数** に固定する。関係する文法は次のとおりとし、その他の構文要素は既存仕様に従う。

```ebnf
Type             := FunctionType | AnnotatedType
FunctionType     := FunctionParameters "->" Type
AnnotatedType    := SemanticsType ("?")* BorrowOrigin?
SemanticsType    := Semantics "/" SemanticsType | TypeAtom
TypeAtom         := CoreType | "(" Type ")"
BorrowOrigin     := "from" OriginAtom
OriginExpression := OriginAtom ("and" OriginAtom)*
OriginAtom       := Name ("." Name)? | "static" | "(" OriginExpression ")"
```

`FunctionParameters`、`CoreType`、`Semantics`、`Name` は既存の構文・役割を引き継ぐ。タプル要素、配列要素、型引数などに含まれる型式にも `Type` を用いる。文法上解析できても、注釈先や使用位置が不適切ならエラーとする。

`from` は借用注釈の位置だけで認識する文脈依存キーワードとし、それ以外では通常の名前として使用できる。空白によって解釈を変えず、専用の改行継続規則も追加しない。改行は既存の括弧・角括弧・型引数などの継続規則に従う。

```text
ref/T? from a             // 可
(ref/T from a)?           // 可：括弧の外で Optional 化
ref/T from a?             // 不可
ref/T from (a and b)?     // 不可
```

### 3.2. 注釈先の決定

各 `from` について、Optional の展開や型の名前解決より前に、次の規則で注釈先を一意に決める。これは判定結果の定義であり、注釈ごとの構文木の再走査を要求しない（実装指針は §6.2）。

1. 直前の型式を起点とし、型のグルーピングと後置 `?` の外側の包みだけを繰り返したどる。これらの型構造は保持する。
2. 現れた最外側の、明示された Semantics を対象にする。Semantics の内側には進まない。
3. 対象がない場合はエラーとする。名前付き型、型引数、タプル要素、配列要素、関数の入出力、フィールドなどの内部を探索しない。別名展開や冗長な `owner` の除去によって対象を探し直すこともしない。

対象にできるのは `ref`・`uniq`・`objref`・`objuniq` と、安全な借用であることが宣言の条件から証明される Semantics パラメーターだけである。`owner`・`obj`・`rc`・`arc`・`unsafe` は対象にできない。構文で対象を固定し、その適格性を意味検査で確認する。

```text
ref/ref/T from a           // 外側の ref に付く
unsafe/ref/T from a        // 不可：対象は unsafe
unsafe/(ref/T from a)      // 内側の ref に付く
owner/ref/T from a         // 不可：対象は owner
owner/(ref/T from a)       // 内側の ref に付く
Option<ref/T> from a       // 不可：対象となる明示 Semantics がない
Option<ref/T from a>       // 型引数内の ref に付く
T from a                  // 不可：T の解決結果には依存しない
```

`from` 自体は型の層を追加しない。同じ構文上の借用層への明示注釈は一度だけとし、グルーピングや `?` を介しても重複を認めない。Origin が同じでもエラーとし、上書きや暗黙の交差は行わない。

```text
ref/T from a from b        // 不可
(ref/T from a) from b      // 不可
(ref/T from a)? from b     // 不可
(ref/T from a) from a      // 不可
ref/(uniq/T from b) from a // 可：別々の借用層
```

### 3.3. Origin 式と制約の区切り

`from` の引数は単独の名前、射影、`static`、または括弧付き Origin 式とする。交差は `from (a and b)` と書く。`from` が消費するのは一つの `OriginAtom` までであり、その外側の `and` は周囲の構文に属する。

```text
ref/T from a
ref/T from value.source
ref/T from static
ref/T from (a and b)
T is ref/U from a and Copy
T is ref/U from (a and b) and Copy
origin a and b outlives c
```

複合 Origin の括弧を必須にする規則は `from` の直後に限る。`origin` 関係句の文法は変更しない。型式だけを要求する位置の `ref/T from a and b` はエラーであり、名前解決によって交差として読み直さない。

`from (a)` は許可する。空の `from ()`、リスト状の `from (a, b)`、末尾カンマ付きの `from (a,)` は許可しない。`f(x: ref/T from a,)` のカンマは通常の引数リストに属する。`_`、呼び出し、任意の値式などを Origin の新しい記法として追加しない。

## 4. 型構文との組合せ

### 4.1. Optional と多層借用

`?` は Semantics 列全体より弱く、関数型の `->` より強く結合する既存規則を維持する。注釈先と Origin 指定を確定した後に `?` を展開し、一つの `?` ごとにその型全体を `::Kimi.Option` で包む。名前解決に依存せず、二重の Option も平坦化しない。

| 表記 | 意味・同値な型表記 |
| --- | --- |
| `ref/T from a` | 外側の ref の Origin が a |
| `ref/T? from a` | `Option<ref/T from a>` |
| `(ref/T from a)?` | `Option<ref/T from a>` |
| `(ref/T)? from a` | `Option<ref/T from a>` |
| `(ref/T?) from a` | `Option<ref/T from a>` |
| `ref/T?? from a` | `Option<Option<ref/T from a>>` |
| `ref/(T?) from a` | `ref/Option<T> from a` |
| `obj/T?` | `Option<obj/T>` |
| `ref/(ref/T from b)` | 内側の ref の Origin が b。外側は省略規則に従う |
| `ref/(uniq/T from b) from a` | 外側の ref が a、内側の uniq が b |
| `ref/(uniq/T from b)? from a` | `Option<ref/(uniq/T from b) from a>` |

表中の `Option` は `::Kimi.Option` を表す。型としての同値性は、任意の綴りに `from` を付加できることを意味しない。注釈先は常に §3.2 の構文規則で決める。

### 4.2. 関数型と複合型

関数型の末尾の `from` は戻り値の型式に属する。関数型そのものに対する借用には、Semantics とグルーピングを明示する。`->` の右結合性は維持する。

| 表記 | 意味 |
| --- | --- |
| `(A) -> ref/B from b` | 戻り値の借用 Origin が b |
| `(A) -> ref/B? from b` | 戻り値が `Option<ref/B from b>` |
| `ref/((A) -> B) from a` | 関数値への借用 Origin が a |
| `((A) -> ref/B) from a` | 不可：型全体に外側の借用がない |

タプル・配列・型引数内の各型式には独立して適用する。例えば `(ref/T from a, ref/U from b)`、`[N of ref/T from a]`、`Pair<ref/T from a, ref/U from b>` の各注釈は、その要素・型引数内の借用だけを対象にする。

### 4.3. binding set とスキーマ

`View<T>{v}` は、その名前付き型の Origin スキーマに対する束縛集合に名前を付ける。既存の Origin や集合を適用する構文ではない。スキーマに含まれない型引数・フィールド・基底型の依存関係を再帰的に集約しない。

```text
ref/View<ref/U from c>{v} from a
```

この例では `a` は外側の借用、`c` は型引数内の借用、`v` は `View` のスキーマに対する binding set の名前であり、互いに異なる役割を持つ。`v` 自体を scalar Origin として使用できず、必要なスロットは `v.source` のように射影する。

`{v}` は名前付き型に付ける既存規則を維持する。`View<T>{v}?` と `(View<T>{v})` は許可するが、`View<T>?{v}` と `(View<T>){v}` は許可しない。`from` の注釈先判定における括弧の透明性を、binding set の命名には拡張しない。型宣言のスキーマヘッダー、集合の命名条件・有効範囲・末尾カンマの規則も維持する。

## 5. 意味と適用境界

### 5.1. Origin・借用・型形成

`S/V from O` は、選択された安全な借用層の Origin を `O` とする型注釈である。Origin は借用が有効なプログラム点の集合を表し、`and` はその交差を表す。`from x` の `x` が直接の借用値なら、その参照値が保持する Origin を指す。変数 x の格納領域の寿命を指すものではない。

注釈自体は値の取得・変換・再借用を実行せず、寿命を延長せず、Loan やアクセス権を生成しない。各層の依存関係、分散、型形成、適合性、排他性、破棄規則は既存仕様を維持する。例えば `ref/(uniq/T from b) from a` は、内側の b が外側の a 以上に長く有効であるという型形成条件も満たさなければならない。

意味の決定は **構文解析 → 注釈先の固定・重複検査 → Optional 展開 → 既存の意味検査** と整理する。これは意味上の依存順序であり、コンパイラーのパス構成を指定するものではない。Copy・Owned・型同一性・ジェネリック分解・Origin の省略などは、展開後の完全な型で判定する。

### 5.2. 名前の導入・省略・ジェネリック

`from` への変更で、名前を導入できる位置や Origin の省略範囲を拡張しない。

- 通常の名前付き関数、コンストラクター、明示アクセサー、Contract の callable 要求では、許可されたシグネチャ注釈内の未束縛の単純名が普遍量化された Origin を導入する。結果だけに現れる名前も同じである。
- ローカルの注釈と Origin 関係句は既存名を参照する。ストレージのスキーマ収集・補完、名前の衝突・有効範囲、特殊化と継承契約の規則は維持する。
- Function Type、Callable、無名関数のシグネチャ内では、新しい名前付き scalar Origin を導入しない。Callable の直接借用注釈の禁止も維持する。
- 初期化子を持つローカルでは既存の推論を利用できる。入れ子の借用、格納型、公開シグネチャに新たな省略許可を与えず、名前付き関数の明記された結果契約を本体から推論しない。

```text
func f<T>(x: ref/T? from a) -> ref/T
```

この引数は展開後に Option であり、直接の借用入力ではない。既存の結果省略規則では、この戻り値の省略 Origin は `static` となる。a に依存する契約にするなら結果にも `from a` を指定する。これが関数本体から返せることを保証するわけではない。

ペア型引数の元の `s/T` は完全な型の Origin を保持する。`s/T from b` または `s/U from b` には s が安全な借用である証明が必要であり、その注釈は外側の Origin を b に指定する。ペアから保持した Origin は同じ構文上の層への明示注釈の重複には数えない。型形成は内側の依存関係を検査し、実際の値の適合・短縮・Move・再借用は別に検査する。指定を省略した別ターゲット `s/U` の条件付き Origin スロットも既存規則を維持する。

### 5.3. Adaptation Target

Adaptation Target の完全な型指定にも、§3.1 の後置順序を適用する。専用文法の関連部分を次に置き換え、Semantics と `/` の間の旧 `BorrowOrigin` は削除する。

```ebnf
AdaptationType := AdaptationCore ("?")* BorrowOrigin?
AdaptationCore := Semantics "/" AdaptationCore | AdaptationAtom
```

`BorrowOrigin` は §3.1 と共通とし、`from` の後は一つの `OriginAtom` まで読み取る。`AdaptationAtom` は既存の許可された型の先頭構文を引き継ぎ、その内部の型式には §3.1 を適用する。関数型全体のグルーピング、型引数と比較の区別、後続のメンバー選択・呼び出し・添字には、既存の括弧・隣接規則を適用する。外側の `->` は消費しない。Semantics 省略形と `@move` は `from` で拡張しない。

構文を確定してから注釈先を固定し、Optional 展開後の位置で使用可否を検査する。

- 対象型の外側 Semantics 列に書かれた借用 Origin は禁止し、操作・被演算子・制約から推論する。グルーピングではこの禁止を回避できない。
- Option などの aggregate 内の完全な型に付く注釈は、その内部の依存関係として保持できる。対応する取得・適合・Loan の条件は別途必要である。

| 表記 | 扱い |
| --- | --- |
| `saved@ref/T? from a` | `Option<ref/T from a>` を対象型として解析 |
| `saved@(ref/T? from a)` | 上と同じ対象型 |
| `value@ref/T from a`、`value@(ref/T from a)` | 構文は解析できるが、外側借用への明示 Origin のため不可 |
| `value@ref from a` | 不可：Semantics 省略形には注釈を追加しない |

`saved` が同じ完全な Option 型なら通常の取得が可能である。借用してから Some で包む暗黙操作や、新しい型変換は追加しない。適合性の検査結果によって別の構文へ読み直さない。§13.5.1 の「どの層にも Origin を記述しない」と読める総論は、上の外側と内部の区別に置き換える。実行時 `is` など、一般の型構文を許可していない位置は拡張しない。

## 6. 表示・保持・実装指針

### 6.1. 表示と保存の保証範囲

表示と保存は、次の役割を区別する。

| 役割 | 保証 |
| --- | --- |
| ソース再出力 | ソースとして記述可能な型構文を、同じ宣言・名前環境・付随する Origin 関係句の下で再解析したとき、正規化された契約が同値であること |
| 診断表示 | 型と依存関係を識別できる説明表記であること。内部 Origin や具体的な Closure Type には診断用の記号を許可し、再解析可能性は要求しない |
| 保存・読み戻し | 型・束縛・スコープの識別情報と対応関係、正規化された契約を保持すること。診断用文字列を意味情報の復元元にしない |

ソース再出力では、借用層、Option の位置、Origin の束縛・量化・関係を保存する。所有ローカルの格納領域などを表す診断用の内部名を、使用可能なソース名として扱ってはならない。

標準のソース表記は `ref/T? from a` とし、§3.1 の順序に従う。交差は `from (a and b)` とし、内側の借用注釈や関数型には必要な括弧を付ける。型本体・`?`・`from` を一まとまりとして出力し、禁止形の `ref/T from a?` を生成しない。

### 6.2. 注釈の位置と効率的な対象決定

**構文上の記述位置と、意味上の注釈先を別々に保持する。** 例えば `(ref/T?) from a` の `from a` は括弧の外にあり、対象は内側の `ref` である。注釈のソース範囲と構文上の所有箇所を保存し、対象の Semantics への参照または識別子を別に持たせる。内側のノードへ注釈を移すために、そのソース範囲を親の括弧の外まで広げない。

対象決定は、解析結果の小さな一時情報に注釈先候補を保持し、次のように伝播させる方法を推奨する。

1. 明示 Semantics では、その層を候補にする。借用としての適格性は後で検査する。
2. グルーピングと後置 `?` では候補を引き継ぐ。名前付き型・タプル・配列・関数型などの全体は、外側からの候補を持たない。内部の各型式は独立して処理する。
3. `from` では候補とその層の明示注釈済み状態を参照する。重複時は既存注釈を上書きせず診断し、エラー回復中もこの状態を保持する。

候補は構文出現ごとの一時情報とし、共有された解決済み型には書き込まない。注釈のソース情報は明示注釈がある箇所だけに保持する。構文木全体のコピーを避け、意味検査では既存の借用層の表現を利用する。推論された Origin を含む意味情報の保持は §6.1 に従う。

この方法では、重複注釈を含む深い入力でも、注釈先決定のために同じ包みを繰り返したどらずに済む。改善対象はコンパイル・診断処理の時間とメモリであり、実行時性能の向上を主張するものではない。

### 6.3. 診断と検証

旧借用注釈はエラーとし、対象層を保存した新表記を案内する。例えば `ref{a}/uniq{b}/T` は `ref/(uniq/T from b) from a` となる。診断は、対象なし・非借用 Semantics・重複・記述順序・Origin 式・使用位置・名前解決の問題を区別する。重複では追加の注釈と最初の注釈の位置を示す。

本書の例を受理・拒否の確認表とし、実装時には次を検証する。

- 単層・多層・Optional・グルーピング・関数型・複合型・binding set の組合せと、記述順序・重複・使用位置の拒否。
- 全安全借用 Semantics、証明を伴う総称 Semantics、名前導入・省略・再構成・Adaptation Target の意味保存。
- 括弧付き・括弧なしの Adaptation Target と、後続の演算子・式の境界。
- 空白・既存の改行継続・通常の名前としての `from`、§6.1 の各保証、注釈と対象のソース位置。
- 深いグルーピング・Optional・重複注釈を含む不正入力での、回復後の状態、走査量、割り当て、スタック使用。

受理例は既存の意味条件を満たすことを前提とする。パーサーの受理だけを、意味検査や実行の対応完了と扱わない。

## 7. 利点・欠点・副作用

### 7.1. 利点

- 借用 Origin の指定と binding set の命名を表記で区別できる。
- Semantics と対象型を続けて読める。Optional も `ref/T? from a` と短く書ける。
- 多層借用の括弧が注釈先を示し、構文だけで対象と式の終端を決められる。

### 7.2. 欠点と記述場所による負担

`from` によって単層の記述が長くなり、多層借用と複合 Origin では括弧が増える。型と注釈の距離も長くなり得る。推論やジェネリックで常に負担を解消できるわけではない。

初期化子のあるローカルや完成した型をそのまま渡すジェネリックでは、既存の推論・保持規則により型全体の記述を省ける。一方、明示的な公開シグネチャ、内側の借用層、インスタンスフィールドや enum payload の格納契約では、必要な注釈と括弧が残る。

### 7.3. 副作用と変更範囲

旧表記を使用するソース、ライブラリ、例、milestone プログラム、およびパーサー、型表示、診断、ドキュメント、関連テストの更新が必要になる。正式仕様では旧構文を使う説明・例と構文要約も更新対象となり、付録 F.4 の Adaptation 専用文法にも §5.3 を反映する。

この変更は借用層と Origin の意味を保存する。寿命・借用・推論の能力、既存コードが表す型の表現力を狭めず、ABI、ランタイムの寿命情報、実行時処理や割り当ての追加を要求しない。実装済みの対応範囲を拡大したとみなすものでもない。

関連する正式仕様は、[字句・継続規則](../../spec/02-source-and-lexical-structure.md)、[Optional と型の合成](../../spec/03-types-and-values.md)、[ジェネリック再構成と Callable](../../spec/08-generics-constraints-and-contracts.md)、[Adaptation Target](../../spec/13-operators-and-assignment.md)、[Origin の名前・関係・補完](../../spec/15-ownership-and-lifetime-analysis.md)、[構文要約](../../spec/appendices/F-syntax-summary.md)である。正式仕様は取り込み時に本書の規則を自己完結した形で記載し、廃止構文との比較だけを意味の定義にしない。
