# Contract の要件本体と外部適合

日付：2026-10-10。状態：提案。正式仕様への取り込み・実装は未実施。本書の構文はすべて仮案である。

関連：[Contract 適合の再設計案と導入順序](<2026-10-10 Contract Conformance Redesign Options.md>)（本書の後に検討する、より大きな変更）。

本書では、正式仕様の節を「§8.4.4」のように、本書の節を「本書 §3」のように書く。

## 1. 目的と原則

### 1.1. 現在の仕様と問題

[§8.4.4](../../docs/spec/08-generics-constraints-and-contracts.md#844-conformance) は、要件と実装の対応を適合の検証時に一度だけ固定し、呼び出し側で選び直さない。一方、次のものはない（§8.4.4、[付録 D](../../docs/spec/appendices/D-deferred-features.md)）。

- **既定の実装**：共通の処理を、すべての適合で書き直す必要がある。
- **外部適合**：他の Kotonoha の型を自分の Contract に適合させるには、ラッパー型が要る。
- **Contract を指定するアクセス**：同名の要件を書き分けられない。[§9.5](../../docs/spec/09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) では、Constraints から集めた同名要件の受け手の形が違うと使用がエラーになるが、それを避ける構文がない。

### 1.2. 原則

1. 実装の対応は、適合ごとに一度だけ固定する（現行の維持）。
2. 型にメンバーを加えるのは、型自身の宣言だけである。
3. 適合を成立させられるのは、型か Contract の宣言の所有者だけである。

### 1.3. 導入するもの

| 機能 | 本書の節 |
| --- | --- |
| 要件本体 | 本書 §3 |
| 外部適合（`conform`） | 本書 §4 |
| Contract 修飾アクセス `.(C)` | 本書 §5 |
| 具体型での要件の探索 | 本書 §6 |

## 2. 用語

| 用語 | 意味 |
| --- | --- |
| 要件本体（Requirement Body） | 関数要件が持つ本体。明示実装も組み込みの witness もない適合で、その要件の実装になる |
| 宣言 Contract | 要件を宣言した Contract。本書では D と書く |
| 明示実装 | 宣言として書かれた実装。型のメンバー、または外部適合のブロックの宣言（要件本体と組み込みの witness は含まない） |
| 実装 Identity | 対応表の項目の同一性。メンバーは Member Identity、ブロックの宣言はその宣言の Identity、組み込みの witness はその witness の Identity、要件本体は（要件 Identity, 最終的に適合する型） |
| 対応表 | 適合ごとに一度だけ固定する、要件から実装への対応（§8.4.4 の retained mapping、§18.7.2 の witness mapping） |
| 型内適合 | 型の宣言の中の `Self is C` と条件付き適合（§8.4.8） |
| 外部適合 | Contract の所有者が、別の Kotonoha で宣言された型について `conform` で宣言する適合 |
| 組み込み適合 | プリミティブ型などに対して仕様が指定する適合（§8.7。比較、書式、整数の Position、算術 Contract の built-in witness） |
| 所有者 | 宣言を所有する Kotonoha。宣言 Identity の originating Kotonoha（[§6.1.3](../../docs/spec/06-declarations-and-containers.md#613-inherited-environments-and-declaration-references)）。プリミティブ型は、Kimi が所有する型宣言として扱う。「T の Kotonoha」は T の宣言の所有者を指す |
| 適合を成立させる | まだ存在しない適合を宣言によって存在させること。refinement で含意される祖先の適合を含む |

本書は要件本体を "default" と呼ばない。正式仕様の "default" は主に引数の既定値（§7.2）を指す。既定の実装を指す箇所の書き換えは、本書 §11 に挙げる。

## 3. 要件本体

### 3.1. 宣言

関数要件を「本体を省略できる関数宣言」とする。ヘッダーの後のインデント領域は、通常の関数と同じく、先頭の Constraint prefix（[§7.4](../../docs/spec/07-functions-and-callable-values.md#74-function-constraints)）とその後の本体からなる。virtual の元の宣言が `effect` 句の後に本体を書くのと同じ形である。

- 実行項目が一つ以上あるか、`=>` の単一項目本体があれば、要件本体を持つ。
- Constraint、Origin 関係、effect 句だけなら、要件本体を持たない（現行どおり）。
- Unit を返す空の要件本体は `=> ()` と書く。

```kimi
contract Measured
    func count(self) -> isize

    func isEmpty(self) -> bool
        return self.count() == 0

    func summary(self) -> string => "measured"
```

`isEmpty` は通常の要件であり、明示実装のない適合でだけ本体が使われる。要件ではない「Contract に付属するメソッド」という種類は作らない。

制限：

- 対象は関数要件（instance 関数と Type 関数）だけとする。Property 要件と関連型は本体を持たない。
- 要件本体は要件の宣言にだけ書ける。継承した要件に本体を付ける構文はない。
- 子 Contract の関数要件と祖先の要件の、どちらかが要件本体を持つ場合は、両者の識別キー（[§8.4.5](../../docs/spec/08-generics-constraints-and-contracts.md#845-implementation-matching)）が一致しないことを Contract の前提の下で証明できなければ、宣言エラーとする。[§8.4.9.1](../../docs/spec/08-generics-constraints-and-contracts.md#8491-direct-conformance-collisions) と同じく、束縛によって一致しうる場合もエラーとする。[§8.4.2](../../docs/spec/08-generics-constraints-and-contracts.md#842-refinement) では両者は別々の要件として共存するが、どちらかに本体があると、本体の追加・置き換え・取り消しと同じ効果になるからである。
- 同じ要件へ複数の refinement 経路があっても、要件は一つなので本体も一つである。別々の Contract が宣言した同名の要件は別々の要件で、それぞれの本体を持つ。
- 引数の既定値は、本体があっても禁止のままとする（[§8.4.1](../../docs/spec/08-generics-constraints-and-contracts.md#841-function-requirements)）。

### 3.2. 本体の検査

要件本体は、次の前提を持つ総称定義として、宣言 Contract D で一度だけ検査する（[§8.10](../../docs/spec/08-generics-constraints-and-contracts.md#810-generic-body-checking-and-deferred-obligations)）。

- `Self is D` と、D の Constraints
- 要件自身の generic parameter と Constraints
- D が継承する外側の環境（§6.1.3）

`Self` について使えるのは、D とその祖先の要件・関連型・Constraints だけである。適合する具体型がたまたま持つメンバーは使えない。`self.count()` は、その適合に固定された `count` の実装を呼ぶ。本体から同じ要件を呼べば、通常の再帰になる。

名前解決とアクセスは D の定義位置で行う（[§9.3.3](../../docs/spec/09-names-signatures-and-access.md#933-generic-bodies-and-separate-compilation)）。その位置からアクセスできる補助関数、たとえば D を囲む Container の private な関数や、同じ Kotonoha の internal な関数も使える。

本体の中で要件を呼ぶときは、`self.f()` か `Self.f()` と書く。修飾のない名前の探索が D を調べるとき、その名前が D 自身または祖先の要件にあれば、instance 関数の要件でも Type 関数の要件でも、[§9.4](../../docs/spec/09-names-signatures-and-access.md#94-unqualified-lookup) の instance member と同じく `QualificationRequired_Kd` で止まり、外側へは進まない。

### 3.3. effect bound

要件 r に effect bound を宣言する Contract X ごとに、r の本体が X の上限を満たすことを、前提 `Self is X` の下で一度だけ検査する。X は D 自身か、`EffectSpecification`（[§8.4.10.1](../../docs/spec/08-generics-constraints-and-contracts.md#84101-declarations)）で上限を加えた子孫である。前提 `Self is X` の下では、X とその祖先が宣言した上限を、本体内の要件呼び出しに使える（[§8.4.10.4](../../docs/spec/08-generics-constraints-and-contracts.md#84104-checking-and-use)）。

- 満たさなければ X の宣言エラーとする。本体で満たせない上限を加えることは、本体を事実上取り消す（上書きする）ことであり、本書 §3.1 が禁じる場合と同じだからである。
- 上限を宣言しない X の子孫 Y では検査し直さない。前提 `Self is Y` の下で使える上限は `Self is X` の下より減らないので、X で成り立った結果は Y でも成り立つ。
- 結果は適合する型に依存しない。

```kimi
contract StrictMeasured: Measured
    effect Measured.count confined
    effect Measured.isEmpty confined   // OK：Self is StrictMeasured の下で count は confined

contract QuietMeasured: Measured
    effect Measured.isEmpty confined   // エラー：count の環境 effect が不明なので、本体が confined にならない
```

### 3.4. 実装の決定

適合の検証では、§8.4.5 の識別を終えてから、要件ごとに実装を決める。表は候補の数だけで分ける。

| 識別の結果 | 扱い |
| --- | --- |
| 候補が一つ | その候補を検証して採用する。失敗すればエラーで、本体には切り替えない |
| 候補が複数 | 曖昧としてエラー |
| 候補がなく、要件本体がある | 要件本体を採用する |
| 候補がなく、要件本体もない | 実装がないエラー |

- **候補の範囲**：型内適合では、通常のメンバー探索（本書 §6 の段 1）で見つかった宣言とする。外部適合では、ブロックの宣言だけとする（本書 §4.5）。
- **アクセスと条件**：アクセスできない宣言は探索を確定させないので、候補にならない（§9.3）。見つかった候補が適合のアクセス（[§9.3.4](../../docs/spec/09-names-signatures-and-access.md#934-conformance-accessibility)）を満たさない場合や、条件を証明できない場合は、検証の失敗としてエラーにし、本体には切り替えない。
- **関連型との順序**：「候補がない」は、関連型を凍結した前提での識別結果が missing と確定した場合に限る（[関連型推論 §2](../../docs/spec/associated-type-inference.md#2-identify-once)）。pending は「候補がない」ではない。
- **推論の証拠**：要件本体は関連型推論の証拠にならない。本体を持つ要件からしか決まらない関連型は、明示的に指定する。
- **組み込み適合**：組み込みの witness がない要件は、要件本体があれば本体を使う。
- **互換性**：明示実装の候補は、所有権、Origin、task slot、アクセス、effect bound の互換性を §8.4.5 のとおり検査する。要件本体は本書 §3.2–3.3 で一度だけ検査済みなので、適合ごとには検査し直さない。アクセスは本書 §3.6 により常に成り立つ。

### 3.5. 継承された適合

型内適合の継承（§8.4.4 の Inherited conformance）では、次のようにする。外部適合は継承しない（本書 §4.4）。以下、派生型を S と書く。

- メンバーの項目は、§8.4.4 のとおり継承する。
- 本体の項目は継承せず、S で本書 §3.4 の決定をやり直す。候補がなければ、S で具体化した本体を使う。本書 §3.4 でエラーになる場合（候補の検証の失敗、候補が複数）は、エラーにせず、この継承経路が成り立たないものとする（§8.4.4）。
- §8.4.4 の Self の形の表と、「同名の宣言は継承した対応を置き換えない」は、本体の項目には適用しない。

要件本体はメンバーではないので、S は同じ名前のメンバーを宣言できる（inherited-Name の規則に当たらない）。決定をやり直すことで、継承の経路、明示的な経路、具体型での呼び出し（本書 §6）が同じ実装を使う。

```kimi
contract Bounded: Measured
    func capacity(self) -> isize

public open struct Base
    Self is Measured
    public func count(self) -> isize => 0

public struct Derived : Base
    Self is Bounded                    // Derived から Measured への経路が二つある
    public func capacity(self) -> isize => 8
```

基底での具体化を継承すると、継承経路の `isEmpty` は Base で具体化した本体、`Bounded` 経由の経路は Derived で具体化した本体になり、経路の一致（§8.4.4、[§8.4.9.2](../../docs/spec/08-generics-constraints-and-contracts.md#8492-merging-paths-and-cycles)）に反する。本規則では、どちらも Derived で具体化した本体になる。Derived が `public func isEmpty(self) -> bool` を宣言していれば、どちらの経路もそのメンバーを使う。

### 3.6. アクセス

要件本体の実効アクセスは、宣言 Contract D のアクセスと同じである（§9.3.4）。本体が実装するのは適合 (T, D) の要件であり、§9.3.4 は祖先の適合ごとに独立に検査する。適合 (T, D) のアクセス `Access(T) ∩ Access(D)` は D のアクセスを超えないので、アクセスの条件は常に満たされる。生成した witness thunk でアクセスを迂回することもない。

## 4. 外部適合

### 4.1. 宣言する場所

| 宣言する Kotonoha | 書き方 |
| --- | --- |
| 型の所有者 | 型内適合（`Self is C`、`Self is C when P`） |
| Contract の所有者で、型の所有者ではない | 外部適合（`conform`） |
| どちらの所有者でもない | 書けない。ラッパー型を使う |

- `conform` はソースのルートにだけ置ける。
- 型の所有者は `conform` を使わない。型の中の宣言と分けると、同じ適合に二つの書き方ができ、ブロックの宣言がメンバーになるかどうかも書く場所で変わってしまうからである。
- モジュールグラフは DAG なので（[§18.4.1](../../docs/spec/18-modules-and-dependencies.md#1841-identities-and-graph)）、同じ型と Contract について、型の所有者の型内適合と Contract の所有者の外部適合が同時に存在することはない。両者の併記を禁じる規則は要らない。
- Kimi は依存先を持たないので、compiler-intrinsic Contract と閉じた Contract（[§8.4.7](../../docs/spec/08-generics-constraints-and-contracts.md#847-intrinsic-and-closed-contracts)）への外部適合は書けない。
- テスト専用の `conform` は、テスト専用の Contract についてだけ書ける。製品の適合は製品の入力だけで決まる（[§18.8](../../docs/spec/18-modules-and-dependencies.md#188-product-and-test-inputs)）。

### 4.2. 所有者規則

宣言が適合 (T, C) を新たに成立させられるのは、その宣言の Kotonoha が T か C の所有者であるときだけである。

外部適合 `conform<…> X is C when P` が refinement で含意する祖先 A のうち、宣言する Kotonoha が所有しないものについては、次のようにする。

- (X, A) が、この宣言を除いて、X の Kotonoha と A の Kotonoha の索引（本書 §4.8）にある適合から、X の通常の Constraints と P の下で Proven でなければならない。Unknown の場合や、一部の束縛でしか成り立たない場合はエラーとする。
- その祖先適合の対応と関連型をそのまま使い、経路の一致を [§8.4.8.3](../../docs/spec/08-generics-constraints-and-contracts.md#8483-uniqueness-and-parent-contracts) のとおり検査する。
- 外部適合では、§8.4.8.3 の「子の適合が自分の条件の下で親の証拠を供給する」規則によって、既存の祖先適合の範囲を広げない。

所有者は宣言 Identity で判断する。綴り、alias、型引数、Contract の型引数は関係しない。`Foreign.Box<MyType>` は外部の型、`Foreign.Indexable<MyKey>` は外部の Contract である。

```kimi
// Mine の Kotonoha。Lib が Parent を、Foreign が Buffer と Box を所有する。
// Foreign.Buffer は Lib.Parent に適合していない。
// Foreign.Box<T> は型内で Self is Lib.Parent when T is Copy を宣言している。
// Box の 2 行は別々の書き方の例である（同じ適合は一つしか書けない。本書 §4.7）。
contract Child: Lib.Parent

conform Foreign.Buffer is Child                               // エラー：(Buffer, Parent) を新たに成立させる
conform<T> Foreign.Box<T> is Child when T is Owned            // エラー：Copy でない T について (Box<T>, Parent) を成立させる
conform<T> Foreign.Box<T> is Child when T is Owned and Copy   // OK
```

この規則と、外部適合を継承しないこと（本書 §4.4）により、(T, C) の有無は T の Kotonoha と C の Kotonoha の索引だけで決まる（本書 §4.8）。

### 4.3. 対象と構文

```text
ExternalConformance := "conform" GenericParameters? NamedReference "is" ContractReference
                       ("when" ConformanceConditions)? IndentedList<ConditionalImplementationItem>?
```

生成規則は付録 F の既存のものを使う。ブロックの項目に対する制限は本書 §4.5 で定める。

**対象**

- 名前付きの型宣言で、外側の Semantics が `owner` のものに限る。プリミティブ型と Kimi の型（`string`、`Array<T>` など）も含む（所有者は Kimi。本書 §2）。
- 対象の型宣言が持つすべての generic slot（外側の宣言から継承した slot を含む。§6.1.3）を、宣言と同じ種類・順序の、互いに異なる新しい binder で受ける。
  - 可：`conform<T> Foreign.Box<T>`、`conform<T, U> Foreign.Outer<T>.Inner<U>`
  - 不可：`conform Foreign.Box<i32>`、`conform<T> Foreign.Pair<T, T>`、`conform<U> Foreign.Outer<i32>.Inner<U>`
- 対象を制限するときは `when` だけを使う。条件の文法と意味は [§8.4.8.1](../../docs/spec/08-generics-constraints-and-contracts.md#8481-conditions-and-implementation-scope) と同じで、外側の parameter も制限でき、型同一性の atom（`T is i32`）も使える。
- 次のものは対象にしない：型パラメーターと関連型の射影（blanket 適合になる）、構造型（Tuple、固定長配列、Function Type、Closure）、Semantics を適用した型（`ref/X`、`obj/X` など）。

```kimi
// Measured の Kotonoha
conform Foreign.Buffer is Measured
    func count(self) -> isize => self.length

conform<T> Foreign.Box<T> is Measured when T is Measured
    func count(self) -> isize => self.value.count()

conform i32 is Measured
    func count(self) -> isize => 1
```

### 4.4. 意味と継承

- 外部適合は型の外に置いた適合で、条件付き適合（§8.4.8）と同じ規則に従う。対象型の通常の Constraints を前提とし、`when` の条件が §8.4.8 の P になる（`when` がなければ P は空）。検証と使用は [§8.4.8.2](../../docs/spec/08-generics-constraints-and-contracts.md#8482-verification-and-use) に従う。
- 外部適合は派生型に継承されない。§8.4.4 の継承は型内適合だけに適用する。派生型 S の適合は、S の所有者の型内適合（継承を含む）か、C の所有者の `conform S is C` で宣言する。

継承を許すと、どの Kotonoha も検証できない適合が生じる。

```text
Shapes … open struct Shape を宣言する
Mine   … Shapes に依存。contract Measured と conform Shapes.Shape is Measured を宣言する
Other  … Shapes にだけ依存。struct Circle : Shapes.Shape を宣言する
```

Other は Measured を、Mine は Circle を知らないので、継承を許すと (Circle, Measured) はどちらの Kotonoha でも検証できない。本規則では (Circle, Measured) は成り立たない。Mine が Other に依存すれば、`conform Other.Circle is Measured` を書ける。

### 4.5. 実装ブロック

- **書けるもの**：関数、computed による Property 要件の実装、関連型の指定。対象が enum のときは computed を書けない。Field、enum Case、コンストラクター、`drop`、入れ子の Container、入れ子の適合は書けない（§8.4.8.1 と同じ）。
- **所属**：ブロックの宣言は適合に属し、ルートの Container にも対象型にも属さない。メンバー探索では見つからず、名前で参照する方法もない。ルートの宣言とも衝突しない。
- **関数の種類**：`self` を持つ関数は対象型の instance 関数で、受け手の省略形（[§7.3](../../docs/spec/07-functions-and-callable-values.md#73-explicit-receivers)）を使える。`self` を持たない関数は Type 関数である。
- **名前解決**：`Self` は対象型を表す。`self.f()` は対象型の具体型のメンバー探索（本書 §6 の段 1・段 2）で解決する。外部適合は段 2 に寄与しないので、この適合の要件は `self.(C).f()`（C はこの適合の Contract）で呼ぶ。
- **アクセス**：アクセス修飾子は書かない。実効アクセスは適合のアクセス `Access(T) ∩ Access(C)` である。対象型の private メンバーへの特別なアクセスはない（[§9.3.1](../../docs/spec/09-names-signatures-and-access.md#931-effective-access-domains-and-protected-receivers)）。
- **候補**：実装の候補はブロックの宣言だけである。対象型のメンバーは、ブロックで明示的に転送しない限り実装にならない。上流が対象型にメンバーを加えても、実装は変わらない。
- **使われない宣言**：ブロックの関数と computed は、この宣言が新たに成立させる適合（C と、本書 §4.2 で成立させる祖先）の要件の少なくとも一つの実装にならなければならない。関連型の指定も、新たに成立させる適合の関連型に限る。既存の祖先適合の要件と関連型は、ブロックで扱わない。
- **省略**：新たに成立させる適合の要件がすべて要件本体でまかなえるなら、ブロックは省略できる。既存の祖先適合の要件は数えない。

```kimi
// 本書 §4.3 の Foreign.Buffer の適合に isEmpty を加えた形（同じ適合は一つしか書けない。本書 §4.7）
conform Foreign.Buffer is Measured
    func count(self) -> isize => self.length
    func isEmpty(self) -> bool => self.isEmpty()   // Foreign.Buffer 自身の isEmpty を呼ぶ。再帰しない
```

### 4.6. 関連型の推論

- 外部適合も、有界推論（関連型推論 §1–§4）の対象とする。対象型が struct・enum でなくてもよい。
- 推論単位は、一つの Kotonoha が一つの対象型宣言について宣言した外部適合のすべて（refinement の経路を含む）とする。
- 識別の候補はブロックの宣言だけとし、通常の探索は使わない。
- 推論単位の外の適合（対象型の型内適合と継承した型内適合、他の Kotonoha の外部適合、組み込み適合。本書 §4.8 の索引にあるもの）の束縛は、完了した凍結前提として使う。
- 要件本体は証拠にならない（本書 §3.4）。

### 4.7. 一意性

直接の適合宣言は、型宣言と束縛済み Contract 参照の組ごとに一つまでとする（§8.4.8.3 の拡張）。§8.4.9.1 の衝突検査は、型内適合と外部適合を区別せずに行う。本書 §4.1 により、一つの型宣言から一つの Contract 宣言への直接の適合はすべて同じ Kotonoha にあるので、この検査はその Kotonoha の中で完結する。

### 4.8. 探索と否定の判定

- 各 Kotonoha は、自分が成立させた適合を、（型宣言, Contract 宣言）をキーとする索引で公開する。索引には、型内適合、自分の型が継承した型内適合（その Kotonoha で検証したもの）、外部適合が入る。Kimi の索引には組み込み適合も入る。
- (T, C) の判定は、T の Kotonoha と C の Kotonoha の索引だけを見る。alias、import、ファイルの読み込み順で、適合が有効にも無効にもなることはない。
- [§8.7](../../docs/spec/08-generics-constraints-and-contracts.md#87-constraint-proof-system) の「プリミティブ型の適合は固定され、どの宣言も追加しない」は、この規則に置き換える。プリミティブ型の適合は、固有の能力（Copy、Owned、Sealed、PrimitiveInteger などの compiler-intrinsic Contract。§8.4.7）、組み込み適合、Contract の Kotonoha の外部適合からなる。
- 適合しない（Refuted）という判定は、両方の索引を確認した後に行う。判定に使った索引は absence dependency として記録する（[§18.7.3](../../docs/spec/18-modules-and-dependencies.md#1873-invalidation-and-persistence)）。
- `T is PrimitiveInteger` は、利用者の `conform i32 is Measured` を含意しない（§8.7 に場合分けはない）。

## 5. Contract 修飾アクセス `.(C)`

### 5.1. 形と意味

値の文脈で、`x.(C).name` と `T.(C).name` は、x の型または T の、束縛済み Contract 参照 C への適合に固定された実装を使う。

- **C の解決**：[§8.4.3](../../docs/spec/08-generics-constraints-and-contracts.md#843-associated-types) の射影 `T.(C).Element` と同じく、Contract 名と alias の探索で解決する。適合の証拠が必要で、C は証拠となる適合の祖先でもよい。
- **受け手の層**：[§3.4.1](../../docs/spec/03-types-and-values.md#341-reference-path-selection) の層を順にたどり、その層の型について C への適合の証拠があれば止まる。証拠がない値の参照の層（§3.4.1 が先へ進むペアの層を含む）は、先へ進む。object の層（`obj`、`rc`、`arc`、`objref`、`objuniq`）では View Target について証拠を求め、なければエラーとする。受け手は要件の受け手の形に従い、§7.3 の受け手式の規則（object の層では [§12.4.3](../../docs/spec/12-expressions.md#1243-object-member-calls)–§12.4.4）で取得する。
- **name の探索**：C の要件（自身と継承分）から同名の要件を集め、Constraints から集めた group（§9.5、§8.4.6）と同じ規則で選ぶ。関数は通常の overload の解決で選ぶ。一つに決まらなければエラーとし、その要件を宣言した祖先の束縛済み Contract 参照で修飾して書く（例：`x.(Indexable<i32>).index(k)`）。別々の親から来た同名の要件や、同じ宣言を別の束縛で継承した要件は、別々の要件である（§8.4.2）。
- **使える形**：instance 関数の呼び出し、Type 関数の呼び出し、Property の読み書き、関数参照。`x.(C)` だけでは値ではない。
- **選ばれる実装**：常に、適合が選んだ実装（メンバー、ブロックの宣言、組み込みの witness、要件本体のいずれか）を使う。要件本体を強制的に呼ぶ構文ではない。明示実装の中で同じ要件を `self.(C).f()` で呼ぶと、その明示実装自身を呼ぶ再帰になる（explicit specialization の [§8.8.3](../../docs/spec/08-generics-constraints-and-contracts.md#883-selection-and-declaration-ownership) と同じ）。
- **組み込み適合**：組み込み適合の要件も `.(C)` で呼べる。メンバーは加わらない。

```kimi
contract Reader
    func read(self) -> i32
contract Consumer
    func read(self: uniq/Self) -> i32
contract Counted
    property total: isize has get

func useBoth<T>(value: uniq/T) -> i32
    T is Reader
    T is Consumer
    // value.read() は、受け手の形が違う要件の group なのでエラー（§9.5）
    return value.(Consumer).read()     // Consumer.read を選ぶ

func makeEmpty<T>() -> T
    T is EmptyConstructible            // §8.4.6 の例の Contract
    return T.(EmptyConstructible).empty()

func totalOf<T>(value: ref/T) -> isize
    T is Counted
    return value.(Counted).total

func isBufferEmpty(buffer: ref/Foreign.Buffer) -> bool
    return buffer.(Measured).isEmpty()   // 外部適合の要件。ref の層を越えて Foreign.Buffer で止まる
```

### 5.2. 既存規則との関係

- [§8.4.6](../../docs/spec/08-generics-constraints-and-contracts.md#846-calls-and-shared-requirements) と [§7.3.1](../../docs/spec/07-functions-and-callable-values.md#731-parameter-acquisition-shape) の「選ぶ構文はない」という記述を、本節で置き換える。§9.5 で、Constraints から集めた同名要件の group がエラーになる場合も、`.(C)` で書ける。
- [§12.4.1](../../docs/spec/12-expressions.md#1241-member-access) の「`.` の右側はメンバー名か Tuple の添字」に、`(ContractReference)` を加える。
- [virtual-dispatch.md §2](../../docs/spec/virtual-dispatch.md#2-slot-identity-and-selection) の例 `U.Catalog.Element` は、§8.4.3（括弧のない `T.C.Element` は通常の修飾パス）に反する。`U.(Catalog).Element` に直す。

## 6. 具体型での要件の探索

具体型のメンバー探索に、最後の段を一つ加える。

1. 通常のメンバー探索（基底の層を含む。§9.5）。
2. 段 1 で、アクセスでき役割の合うメンバーが見つからなかったときだけ、受け手の静的な型 X（型修飾 `X.name` では X）の型内適合の要件を集める。X の型内適合とは、X 自身の宣言による適合と、X に有効に継承された適合（§8.4.4、本書 §3.5）である。実装には (X, C) の対応表を使う。
   - 集め方と選び方は、Constraints から集めた group（§9.5、§8.4.6）と同じとする。
   - 条件付き適合の要件は、その条件 P を applicability とする（§8.4.8.2）。
   - アクセスと役割で絞る。要件のアクセスは宣言 Contract のアクセスであり（§9.3.4）、使用位置が適合のアクセス `Access(X) ∩ Access(C)` に含まれる適合だけを集める。

外部適合と組み込み適合は段 2 に寄与しない。整数などの組み込み型にメンバーを加えないという現行の規則（§8.4.7）は、そのまま保たれる。これらの要件は `.(C)` か Constraints を通して呼ぶ。段 2 は呼び出しと参照のための探索であり、実装の識別（本書 §3.4）と Property の実装の選択には使わない。

```kimi
public struct Bag
    Self is Measured
    var items: Array<i32> = []
    public func count(self) -> isize => self.items.length

let bag = Bag.init()
let size = bag.count()                // 段 1：メンバー
let empty = bag.isEmpty()             // 段 2：Measured の要件（要件本体）
let same = bag.(Measured).isEmpty()   // 同じ実装
```

- 実装を明示実装にしても要件本体にしても、呼び出せる名前は変わらない。
- 型が適合を宣言していれば、ジェネリックコードと具体型のコードで同じ書き方ができる（`T.empty()` と `Bag.empty()` など）。
- 依存先が外部適合を加えても、メンバーの候補は増えない（原則 2）。
- 段 1 で見つかる限り、探索の手間は増えない。

## 7. 記録・互換性・コード生成

- 対応表の各項目に、実装 Identity と出どころ（メンバー、ブロックの宣言、組み込みの witness、要件本体）を記録する（[§18.7.2](../../docs/spec/18-modules-and-dependencies.md#1872-correspondence-and-semantic-records)）。Hover と意味の検査はそれを示す。
- 本書 §3.3 の検査結果は、上限を宣言した Contract の記録に含める。
- 要件本体の推移的な効果の要約は、前提 `Self is D` の下で一度だけ求めて公開する（§18.7.2 の callable contract）。
- 要件本体の中身が変わったときの伝播は、§18.7.3 に従う。
  - 署名と本書 §3.3 の結果だけを読む判断（ジェネリックな要件呼び出し、適合での上限の検査）は、それらが変わらなければ再検証を要さない。
  - 本体の効果の要約を読む判断は、要約が変わったときに再検証する。本書 §3.3 の検査、具体型での呼び出し（本書 §6、`.(C)`）、関数参照、それらを呼ぶ他の実装の上限の検査、Callable の上限の証拠がこれに当たる。
  - コードは再生成する。
- 外部適合の追加と削除は、Contract の Kotonoha の公開情報の変更である。適合しないという判定に依存した利用者も再検証する。
- Contract に本体付きの要件を加えても、その要件と識別キーの合うメンバーを持たない型の適合は壊れない。合うメンバーを持つ型では、そのメンバーが実装になる（互換でなければエラー）。この変化は対応表の記録で確認できる。ただし、識別キーの一致しうる要件を宣言した子孫の Contract は宣言エラーになる（本書 §3.1）。要件本体を変えると、上限を宣言した子孫の Contract が宣言エラーになることもある（本書 §3.3）。
- 要件本体のコードは、実装 Identity（要件 Identity, 最終的に適合する型。要件 Identity は Contract の束縛を含む）と関数の型引数の組ごとに、使われたときだけ生成する。

## 8. 診断

本書は、違反ごとの主位置と関連位置だけを定める。コード名は取り込み時に [§23.3.6.1](../../docs/spec/23-compiler-services.md#23361-codes-and-categories) に従って決め、既存の要件と同じ違反には既存のコードを使う。ブロックの宣言が要件と互換でない場合は、現行どおり `IncompatibleContractImplementation_Kd` とする。

| 違反 | 主位置 | 関連位置 |
| --- | --- | --- |
| 子 Contract の要件が祖先の要件と識別キーで一致しうるときに、どちらかが本体を持つ（本書 §3.1） | 子の要件 | 祖先の要件 |
| 要件本体が上限を満たさない（本書 §3.3） | 上限の宣言 | 要件本体、最初の違反 effect |
| `conform` がルート以外にある | `conform` | なし |
| Contract の所有者でない Kotonoha の `conform`、対象が自分の Kotonoha の型、製品の Contract へのテスト専用の `conform`（本書 §4.1） | `conform` のヘッダー | Contract または型の宣言 |
| 宣言する Kotonoha が所有しない祖先 A について、(X, A) がこの宣言を除いて Proven でない（成り立たない、Unknown、一部の束縛でしか成り立たない）（本書 §4.2） | `conform` のヘッダー | 祖先 Contract、既存の適合 |
| 対象が名前付きの型宣言でない（型パラメーター、関連型の射影）、binder の形でない、構造型、Semantics を適用した型（本書 §4.3） | 対象 | なし |
| 直接の適合宣言の重複・衝突 | 後の宣言 | 先の宣言（既存の規則） |
| ブロックにアクセス修飾子、または書けない宣言（本書 §4.5） | その宣言 | なし |
| ブロックの宣言がどの要件の実装にもならない、または既存の祖先適合の関連型を指定する（本書 §4.5） | その宣言 | 対象の Contract または既存の適合 |
| `.(C)` の適合の証拠がない、要件が見つからない・一つに決まらない、`x.(C)` を単独で使った（本書 §5.1） | `.(C)` | 候補の要件 |

## 9. Kimi の規約（STYLE.md）

`[Kimi]` 規約として次を加える。

- 適合する側も再利用しうる処理は public な総称関数に置き、要件本体と明示実装の両方からそれを呼ぶ。
- 要件本体どうしが循環しないようにする。たとえば `count` の本体が `isEmpty` を呼び、`isEmpty` の本体が `count` を呼ぶと、どちらも実装しない型は無限に再帰する。
- 明示実装の中で、同じ要件を `self.(C).f()` で呼ばない。自分自身を呼ぶ再帰になる（本書 §5.1）。

## 10. 導入しないこと

| 項目 | 理由 |
| --- | --- |
| 子 Contract による本体の追加・置き換えと、その優先順位 | 実装の選択に優先順位が要り、局所的に決められない |
| 明示実装から要件本体を呼ぶ構文 | 処理を共有する二つ目の書き方になる。共有する処理は public な総称関数に切り出せる（本書 §9） |
| Property 要件と関連型の本体 | Property 全体と getter だけの整合、関連型の決定規則との関係を別に詰める必要がある |
| 型と Contract のどちらの所有者でもない Kotonoha による適合 | 無関係な二つのライブラリが、同じ適合の意味をそれぞれ決めてしまう。ラッパー型を使う |
| 外部適合によるメンバーの追加 | 依存先の変更で、通常のメンバーの候補が増える（原則 2） |
| 外部適合の継承 | どの Kotonoha も検証できない適合が生じる（本書 §4.4） |
| blanket 適合、部分的に具体化した対象、条件の強さによる実装の優先 | 対象の型宣言を一つに特定できず、一意性と否定の判定が複雑になる。優先順位があると実装を局所的に決められない |
| 構造型への外部適合 | 所有者が定まらない |

## 11. 仕様の変更箇所

### 11.1. 第 2・3・6・7 章

| 文書 | 変更 |
| --- | --- |
| §2.5.1 | 文脈キーワード `conform`。`when` を外部適合の条件でも使う |
| §3.4.1 | `.(C)` の層の選択（本書 §5.1） |
| §6.1 の表、§6.1.1 | Contract が要件本体を持てる。外部適合のブロックに入れ子の Container は置けない |
| §6.1.3.2、§9.2 | 外部適合のブロックでは `Self` が対象型を表す |
| §7 冒頭、§7.3 | ブロックの関数の所属。instance 関数と受け手の省略形の対象にブロックを加える |
| §7.3.1 | 「選ぶ構文はない」を `.(C)` に置き換える |
| §7.4 | 要件の領域は Constraint prefix と任意の本体 |

### 11.2. 第 8 章

| 文書 | 変更 |
| --- | --- |
| §8.4 冒頭 | Contract が要件本体を持てる |
| §8.4.1 | 要件本体（本書 §3.1）と用語 |
| §8.4.2 | 子の要件と祖先の要件が識別キーで一致しうるときに、どちらかが本体を持つ場合の禁止（本書 §3.1） |
| §8.4.3 | 関連型の指定は外部適合のブロックにも書ける（「inside a conforming Type」にブロックを加える）。ブロックで指定できるのは、新たに成立させる適合の関連型だけである（本書 §4.5） |
| §8.4.4 | 「Conformance declarations generate no implementations」の例外に要件本体の具体化を加える。「requirement-to-Member Identity mapping」を実装 Identity への対応に改める。「There is no external registration, replacement conformance, default implementation or access-bypassing witness thunk.」から external registration と default implementation を除き、replacement conformance と witness thunk の否定は残す。「A new explicit conformance uses ordinary implementation lookup」に、外部適合ではブロックの宣言だけが候補であることを加える（本書 §3.4）。継承での本体の項目（本書 §3.5）。外部適合は継承しない（本書 §4.4） |
| §8.4.5 | 実装の決定と候補の範囲（本書 §3.4） |
| §8.4.6 | `.(C)`（本書 §5）。qualified-call を否定する記述を削除。Member Identity を実装 Identity に改める。「A concrete call such as `Buffer.empty()` uses ordinary Type-member lookup」に、段 1 で見つからないときは本書 §6 の段 2 で適合の要件を探し、その対応表を使うことを加える |
| §8.4.8.1 | ブロックを省略できる条件に要件本体を加え、「通常の実装は生成しない」に要件本体の例外を加える。`when` は外部適合でも使う |
| §8.4.8.2 | Requirement-to-Member mappings を実装 Identity への対応に改める |
| §8.4.8.3、§8.4.9.1 | 外部適合も数える（本書 §4.7）。「The child path does not additionally require `T is A`」に外部適合の例外を加える。宣言する Kotonoha が所有しない祖先 A については、(X, A) がこの宣言を除いて Proven でなければならず、子の適合はその範囲を広げない（本書 §4.2） |
| §8.4.8.4、§8.4.9.2 の末尾 | external registration、"Defaults"、external conformance を導入しないという記述を書き直す。extension 宣言と実装の優先順位を導入しない記述は残す |
| §8.4.10 | 要件本体の上限の検査（本書 §3.3） |
| 新節 | 外部適合（本書 §4） |
| §8.5 | 「There is no replacement conformance, default implementation or external registration.」から default implementation と external registration を除く。`Implements(D, C)` が使う継承された適合は、本書 §3.5 に従い、外部適合を含まない（本書 §4.4） |
| §8.7 | プリミティブ型の適合（本書 §4.8）。検証済みの適合と閉世界の判定は、外部適合を含む索引を見る |

### 11.3. 第 9・11・12 章

| 文書 | 変更 |
| --- | --- |
| §9.3.4 | 要件本体とブロックの宣言のアクセス（本書 §3.6、§4.5） |
| §9.4 | 要件本体の中の修飾のない名前は、D と祖先の要件で `QualificationRequired_Kd` として止まる（本書 §3.2） |
| §9.5 | 具体型の探索の段 2（本書 §6）、group と `.(C)`。「A new explicit Contract implementation is selected this way」に外部適合の例外を加える（本書 §3.4） |
| §11.4.1、§11.4.2 | 外部適合の Property 実装はブロックの computed から選ぶ。対象型の Field による bridge は使わない |
| §12.4.1 | `.` の右側に `(ContractReference)` を加える |

### 11.4. 第 18・23 章

| 文書 | 変更 |
| --- | --- |
| §18.4.1 | 外部適合は対象型に宣言を加えない（「Consumers can neither append declarations」との関係を明記） |
| §18.7.2 | 実装 Identity と出どころ、適合の索引、要件本体の効果の要約 |
| §18.8 | テスト専用の `conform` はテスト専用の Contract に限る |
| §23 | 本書 §8 の診断 |

### 11.5. その他の文書

| 文書 | 変更 |
| --- | --- |
| 関連型推論 §1、§2 | 外部適合の推論単位と候補（本書 §4.6）。要件本体は証拠にならない。Member Identity を実装 Identity に改める |
| virtual-dispatch.md §2 | `U.(Catalog).Element` |
| lsp-hover.md | 対応表の項目の出どころの表示 |
| 付録 D | Contract の行の "default implementations"、external conformance、qualified requirement calls を外す。位置と範囲の行の "default Contract implementations" を要件本体の記述に改める |
| 付録 E・F、`docs/SPEC.md` の索引 | 用語（Contract の定義、要件本体、外部適合）、文法 |
| STYLE.md、LIBRARY.md | 本書 §9 の規約。ライブラリが要件本体を使うときに LIBRARY.md を更新する |

## 12. 未決事項

- 外部適合の対象で、Origin slot と pair slot の binder をどう書くか（§15.3 の binding set に合わせて取り込み時に決める）。
- 診断のコード名。
- `conform` を置ける位置を、ルート以外（group）にも広げるか。
- より大きな再設計（[別文書](<2026-10-10 Contract Conformance Redesign Options.md>)）。
