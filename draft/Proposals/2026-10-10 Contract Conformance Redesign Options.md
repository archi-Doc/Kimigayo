# Contract 適合の再設計案と導入順序

日付：2026-10-10。状態：検討資料。採否は未決定で、実装計画は含まない。本書の構文はすべて仮案である。

前提：[Contract の要件本体と外部適合](<2026-10-10 Contract Requirement Bodies and External Conformance.md>)（以下「基本案」）の後に行う（案 3 は基本案と同時でもよい）、より大きな変更を評価する。案の番号は検討時の番号のままとする。正式仕様の節は「§8.4.4」、基本案の節は「基本案 §3」、本書の節は「本書 §3」と書く。

## 1. 判定の一覧

| 案 | 内容 | 判定 |
| --- | --- | --- |
| 1 | 実装はブロックからだけ取る | 条件付きで推奨（型の中のブロックで行う） |
| 2 | 適合の宣言をすべて型の外の `conform` にする | 見送り |
| 3 | 継承による適合を廃止する | 推奨 |
| 4 | 区別できない同名要件は修飾で選ぶ | 推奨 |
| 5 | Contract の意味を witness record で定義する | 推奨 |
| 6 | 差し替えられる実装の共通規則を一つの章にまとめる | 共通規則の章は推奨。ヘッダーの規則と構文は統合しない |
| 7 | closed Contract を一般の機能にする | 条件付きで推奨 |
| 8 | 組み込み適合を Kimi のソースで書く | 保留 |
| 9 | Property 要件を accessor 要件の束にする | Property の要件本体を導入するときに推奨 |
| 10 | 適合は全体で一度だけ検証し、索引で引く | 仕様は基本案で足りる。実装で採用する |
| 11 | 単相化の鍵を、本体が区別する情報に縮める | 仕様は変えない。計測後に判断する |

## 2. 各案

### 案 1. 実装はブロックからだけ取る

**変更点**

- 型内適合は、型本体のメンバー位置に `Self is C`（または `Self is C when P`）と実装ブロックを書いて宣言する。型の先頭の Constraint 領域には `Self is C` を書かない。
- 適合の実装は、その適合のブロックの宣言と要件本体だけとする。型の通常のメンバーは、ブロックで転送しない限り実装にならない。
- ブロックの宣言はメンバーにならない。現行の条件付き適合ブロックの宣言（現行ではメンバー）も同じ扱いにする。ブロックの宣言にはアクセス修飾子を書かず、実効アクセスは適合のアクセス `Access(T) ∩ Access(C)` とする（基本案 §4.5 と同じ）。ブロックは型の中にあるので、型の private メンバーを使える（§9.3）。
- 具体型からは、基本案 §6 の段 2 で `bag.count()` と呼べる。
- 外部適合（基本案 §4）は、型の外のブロックとして残る。型内適合と外部適合で、「実装はブロックから取る」という同じ規則になる。
- Copy の派生と ObjectPayload の opt-out をどこに書くかは、採用時に決める。

**具体的なコード**

```kimi
// 現行（基本案を含む）：メンバーの名前と識別キーで実装を探す
public struct Bag
    Self is Measured
    var items: Array<i32> = []
    public func count(self) -> isize => self.items.length

// 変更後：実装は型の中のブロックにだけ書く
public struct Bag
    var items: Array<i32> = []

    Self is Measured
        func count(self) -> isize => self.items.length   // 型の中なので private の items を使える

let size = Bag.init().count()   // 基本案 §6 の段 2 で Measured.count を見つける
```

同じ名前の関数要件を持つ二つの Contract に適合する場合は、通常のメンバーを一つ置いて両方のブロックから転送するか、呼び出し側で修飾する。

```kimi
contract Tallied
    func count(self) -> isize   // Measured.count と同じ署名を持つ別の要件

public struct Bag
    var items: Array<i32> = []
    public func count(self) -> isize => self.items.length   // 両方の適合から使うメンバー

    Self is Measured
        func count(self) -> isize => self.count()   // ブロックの宣言はメンバーではないので、Bag.count を呼ぶ
    Self is Tallied
        func count(self) -> isize => self.count()

// メンバーがなければ、bag.count() は Measured.count と Tallied.count のどちらか決まらずエラーになる。
// その場合は bag.(Measured).count() と書く。
```

**メリット**

- どの関数がどの要件を実装するかが、ブロック一つを見れば分かる。
- [§8.4.5](../../docs/spec/08-generics-constraints-and-contracts.md#845-implementation-matching) の識別は、ブロック内の小さな集合だけで済む。全メンバーを照合する手順と、メンバーの overload による曖昧さがなくなる。
- [§9.5](../../docs/spec/09-names-signatures-and-access.md#95-qualified-and-inherited-lookup) の、継承を含むメンバー探索による実装の選択がなくなる。
- [§11.4.2](../../docs/spec/11-properties.md#1142-standard-operation-witnesses) の Property witness bridge が要らなくなる（ブロックに getter を書く）。
- 偶然一致したメンバーが実装になる問題と、上流の基底や Contract の変更で適合の実装が黙って切り替わる問題がなくなる。基本案 §7 の「合うメンバーを持つ型では、そのメンバーが実装になる」という注意と、基本案 §3.4 の型内適合・外部適合による候補範囲の違いも消える。
- [§8.2](../../docs/spec/08-generics-constraints-and-contracts.md#82-constraints) の、型本体の `Self is C` が「Constraint」と「適合の宣言」を兼ねる二重の意味がなくなる。
- 関連型推論の証拠をブロックの結果型に限れるので、推論の手順が局所的になる。

**デメリット**

- Kimi ライブラリ、テスト、マイルストーンのすべての適合を書き直す。
- 同名の要件を持つ複数の Contract に適合すると、転送か修飾が必要になる。
- Field をそのまま Property 要件の実装にできなくなり、getter を一行書く必要がある。
- 具体型からの要件の呼び出しはすべて基本案 §6 の段 2 を通るので、呼び出しの形は要件の契約になる。実装側の引数の既定値・名前の省略、結果の部分型や強い結果 Origin は、具体型からも見えなくなる。
- 要件と同名で、アクセスでき役割の合う通常のメンバー（基底から継承したものを含む）が一つでもあると、探索は段 1 で止まり、要件は `.(C)` でしか呼べない。ブロックの宣言はメンバーではなく inherited-Name の規則（§6.2.2）に当たらないので、上流の基底が同名のメンバーを加えると、具体型での呼び出しは黙ってそのメンバーに切り替わり、総称コードや `.(C)` とは別の実装を呼ぶ。
- virtual 関数は型の直接のメンバーでなければならないので、ブロックに置けない。virtual slot を実装にするには転送が要る。
- 案 3 と併せると、派生型の適合ごとにブロックが要る。基底が実装を通常のメンバー（または public な補助関数）として持つ場合だけ転送できる。基底がブロックの中だけで実装していると、派生型からは再利用できない（`self.(C).f()` は自分自身を呼ぶ）。

**判定**：条件付きで推奨する。型の中のブロックで行い、案 2 は採らない。案 4 を先に入れておく。採用前に、Kimi ライブラリとテストにある適合の数、同名要件の重なり、案 3 で必要になる派生型の適合の数を調べる。重なりが多ければ、メンバーを実装に指名する一行の形（仮：`use count`）を加えるか検討する。

### 案 2. 適合の宣言をすべて型の外の `conform` にする

**変更点**

- 型の所有者による適合も Contract の所有者による適合も、すべてルートの `conform` 宣言で書く。型本体の `Self is C` と、メンバー位置の条件付き適合はなくなる。
- 置ける場所の規則は、「型か Contract の宣言の所有者の Kotonoha」の一つになる。基本案 §4.1 の表（型の所有者は型の中に書く）は、この規則に置き換わる。閉じた Contract と、利用者が付与できない intrinsic Contract の制限は残る。

**具体的なコード**

```kimi
// 現行
enum Option<T>
    Self is Copy when T is Copy
    Some(T)
    None

public struct Bag
    Self is Measured
    var items: Array<i32> = []
    public func count(self) -> isize => self.items.length

// 変更後
enum Option<T>
    Some(T)
    None

conform<T> Option<T> is Copy when T is Copy

public struct Bag
    internal var items: Array<i32> = []    // 型の外のブロックから読むので internal に広げる

conform Bag is Measured
    func count(self) -> isize => self.items.length
```

**メリット**

- 適合の宣言の形が、宣言する側によらず一つになる。
- §8.2 の `Self is C` の二重の意味がなくなり、[§8.4.8.1](../../docs/spec/08-generics-constraints-and-contracts.md#8481-conditions-and-implementation-scope) のメンバー位置の規則と付録 F の `ConformanceClause`・`ConditionalConformance` が一つの文法になる。
- enum のように分割できない型でも、適合の実装を型本体の外に置ける。

**デメリット**

- 型の外のブロックは、型の private メンバーを使えない（§9.3。[§9.3.1](../../docs/spec/09-names-signatures-and-access.md#931-effective-access-domains-and-protected-receivers) は、型を対象にするだけで private の特権を与えることを禁じる）。実装に使う Field を internal に広げるか、型の中に転送用のメンバーを置く必要がある。後者は案 1 の利点を打ち消す。
- 基本案 §12 の未決事項（対象の Origin slot と pair slot の binder の書き方）を先に決める必要がある。`Slice<T> {source}` などの Kimi の型の適合は、関連型の指定で `during source` を使うからである。
- 型本体を読むだけでは、その型の適合が分からない。
- 移行量が大きい。
- 異なる Kotonoha が同じ (型, Contract) を宣言しないことは DAG から従うが、これは基本案 §4.1 でも同じで、案 2 の利点ではない。同じ Kotonoha の中の重複と衝突は、§8.4.8.3・§8.4.9.1 で引き続き検査する。

**判定**：見送る。案 1 を型の中のブロックで行えば、案 2 の利点のうち、`Self is C` の二重の意味の解消と、無条件・条件付きの適合の文法の統一が得られる。private のアクセスと Origin の binder の問題も生じない。宣言する側によらず形が一つになる利点は得られないが、基本案 §4.1 でも宣言する側ごとに書き方は一つに決まっている。[SETTLED の「`is` has several meanings」](../../docs/SETTLED.md#is-has-several-meanings)は再提起しない。Constraint の `X is Y` はそのまま残る。

### 案 3. 継承による適合を廃止する

**変更点**

- 派生型の適合は、派生型が自分で宣言したときだけ成立する。継承による適合（§8.4.4 の Inherited conformance）はなくなる。
- 宣言した派生型の実装の探索は、現行どおり基底のメンバーも見る（§9.5）。現行の名前照合のままなら、宣言は一行で済む。

**具体的なコード**

```kimi
public open struct Shape
    Self is Utf8Format
    // Utf8Format の実装（省略）

public struct Circle : Shape
    Self is Utf8Format    // 変更後は必要。実装は Shape のメンバーが探索で見つかる
    var radius: f64 = 1.0
```

**メリット**

- [§8.4.4 の Inherited conformance](../../docs/spec/08-generics-constraints-and-contracts.md#844-conformance) がなくなる。継承の可否を決める Self の形の表、継承経路と明示経路の一致、一部の経路だけが失敗した場合の扱い、その診断が不要になる。
- Self の形による成否は、通常の照合の結果として説明できる。たとえば `other: ref/Self` は `ref/Shape` と `ref/Circle` が一致しないので失敗する。別の表は要らない。
- §8.5 の「validly inherited conformance」と、§8.7 の検証済みの適合の規則が単純になる。
- 基本案の継承に関する規則が消える。基本案 §3.5 が不要になり、基本案 §4.4 の「外部適合だけは継承しない」という区別もなくなる（どの適合も継承しない）。基本案 §4.8 の索引にも継承の分が入らない。

**デメリット**

- 適合させたい派生型ごとに、一行の宣言が要る。
- 基底のメンバーを実装に使う場合の受け手の射影と、ObjectCallCompatible の要求（[§9.5.1](../../docs/spec/09-names-signatures-and-access.md#951-base-subobject-receiver-projection)）は残る。
- 将来の実行時 View（§8.5）で、基底の型の View が持つ派生型の値は、派生型が適合を宣言していなければ (D, C) を持たない。派生型に宣言を求めるか、基底の部分オブジェクトの適合を使うかを、View の設計で決める必要がある。

**判定**：推奨する。移行は小さく、規則は大きく減る。基本案と同時に取り込めば、基本案の継承に関する規則を書かずに済む。

### 案 4. 区別できない同名要件は修飾で選ぶ

**変更点**

- [§8.4.6](../../docs/spec/08-generics-constraints-and-contracts.md#846-calls-and-shared-requirements) の、別々の Requirement Identity を一つの呼び出し候補にまとめる規則（署名・条件の同値と、すべての代入で実装が同じことの証明）を削除する。それに依存する [§8.4.10.4](../../docs/spec/08-generics-constraints-and-contracts.md#84104-checking-and-use) の「§8.4.6 でまとめた要件は、すべての上限を使える」という文も削除する。
- 集めた要件は別々の候補のまま、通常の overload の解決で選ぶ。一つに決まらなければ曖昧としてエラーにし、`.(C)` を付ける修復候補を出す。
- 同じ宣言から束縛違いで生じた別々の要件（`Addable<T>` と `Addable<i32>`）は、従来どおり引数の型による overload の解決で選ぶ。演算子もそのまま使える。
- 同じ要件への複数の経路は、従来どおり一つの要件として数える。

**具体的なコード**

```kimi
contract Resettable
    func reset(self: uniq/Self)
contract Restartable
    func reset(self: uniq/Self)

func restart<T>(value: uniq/T)
    T is Resettable
    T is Restartable
    value.reset()                  // 現行でも曖昧（抽象的な T では §8.4.6 の同値の証明が成り立たない）
                                   // 案 4 では修復候補 value.(Resettable).reset() などを出す
    value.(Resettable).reset()     // OK
```

**メリット**

- 呼び出しの意味が字面だけで決まる。
- 同値の証明が不要になり、仕様とコンパイラーの両方から規則が減る。
- 修復候補を機械的に適用できる（Compiler Server Protocol）。

**デメリット**

- 現行で同値の証明によって一つの候補にまとまる呼び出しにも、修飾が要るようになる。抽象的な型では実装の対応が分からず、証明が成り立つ場面はほとんどないので、そうした呼び出しはまれである。

**判定**：推奨する。基本案の `.(C)` があれば代わりの書き方が常にあり、移行もほとんどない。

### 案 5. Contract の意味を witness record で定義する

**変更点**

- 仕様の説明の土台として、`T is C` を「C の要件を欄に持つコンパイル時の記録を、暗黙に受け取ること」と定める。個々の規則はその帰結として書き直す。
- 記録は説明のためのモデルであり、実行時に辞書を渡すことはしない。単相化だけで実装するという方針は変えない。
- コンパイラーの MIR は、要件の呼び出しを（証拠, 欄の番号）で表す。証拠は、総称コードでは前提（`T is Measured`）を、具体型では ConformanceId を指す。単相化で ConformanceId に置き換える。

**具体的なコード**

```kimi
contract Measured
    func count(self) -> isize
    func isEmpty(self) -> bool
        return self.count() == 0

func check<T>(value: ref/T) -> bool
    T is Measured
    return value.isEmpty()
```

```text
説明上の展開（ソースの構文ではない）
  Measured の記録<Self>：
    count   : (ref/Self) -> isize
    isEmpty : (ref/Self) -> bool       適合が実装を与えなければ要件本体
  check<T>(value, 記録 w : Measured の記録<T>) = w.isEmpty(value)
```

| 現行の規則 | 記録での説明 |
| --- | --- |
| 対応は検証時に固定し、呼び出し側で探し直さない | 記録は値である |
| 経路の一致（§8.4.8.3、§8.4.9.2） | 祖先の記録を一つだけ共有する |
| `T.(C).Element`、`x.(C).f()` | 記録の型の欄、関数の欄 |
| 要件本体 | 適合が実装を与えない欄を埋める、Contract 側の関数 |
| 条件付き適合 | 条件の証拠を受け取って記録を作る |
| 一意性 | (型, Contract) ごとに記録は一つ |

**メリット**

- 多くの規則が一つのモデルの帰結になり、仕様の記述が短くなる。
- [単一 MIR 計画](<2026-10-10 Compiler Size Reduction and Single MIR Plan.md>)の R7「単一の Contract ソルバー」の出力の形が決まる。
- 要件の呼び出しの解決が、表を一回引くだけになる。
- 将来の実行時 Contract View では、記録を vtable の材料に使える。ただし ObjectViewCompatible の制限、各実装の ObjectCallCompatible、派生型を通じた Supports の保存（§8.5）は、記録だけでは示せないので別に定める。

**デメリット**

- 仕様の書き方を大きく改める必要がある。
- Origin 引数を持つ関連型、effect bound、条件付き適合を記録の言葉で正確に書く必要がある。書き損じると、仕様の意味を変えてしまう。
- 記録を実行時の値と誤読されないよう、説明を補う必要がある。

**判定**：推奨する。利用者から見える変更はない。単一 MIR 計画の R7 と同じ時期に行う。

### 案 6. 差し替えられる実装の共通規則を一つの章にまとめる

**変更点**

virtual、explicit specialization、要件本体は、それぞれ元の宣言と差し替えられる実装を持つ。

| | virtual | specialize | 要件本体 |
| --- | --- | --- | --- |
| 実装を選ぶ鍵 | 動的型 | 静的な型引数 | 適合する型 |
| 実装を加えられる者 | 派生型 | 元の関数の Kotonoha | 型か Contract の所有者 |
| ヘッダーの規則 | 元の宣言を書き直して継承する（§8.8.2） | 同左 | 互換性で検査する（§8.4.5） |
| effect bound | 元の slot に宣言 | 宣言しない（効果は呼び出し側の要約に入る） | 要件と、上限を加える子孫 Contract が宣言（基本案 §3.3） |
| 失敗時に選び直すか | 選び直さない | 選び直さない | 選び直さない |
| 元の本体を呼べるか | `base.f()` で呼べる | 呼べない | 呼べない |

- 「開いた宣言」の章を設け、共通の規則を一度だけ定める。共通の規則とは、失敗しても選び直さないこと、自分自身の呼び出しは再帰になること、実装を加えられる者を限ること、選んだ結果を記録することである。
- ヘッダーの規則は統合しない。override と specialize は [§8.8.2](../../docs/spec/08-generics-constraints-and-contracts.md#882-inherited-contract)、Contract の実装は §8.4.5 の互換性に従う。
- 三者の構文は変えない。元の本体を呼べるかどうかの違いは、別に決める。

**具体的なコード**

```kimi
public open struct Shape
    public virtual func area(self: objref/Self) -> f64 => 0.0

public struct Square : Shape
    var side: f64 = 1.0
    override func area(self: objref/Self) -> f64 => self.side * self.side

func classify<T>(value: ref/T) -> i32 => 0
specialize func classify<i32>(value: ref/i32) -> i32 => 1

contract Measured
    func count(self) -> isize
    func isEmpty(self) -> bool => self.count() == 0
```

**メリット**

- 仕様の重複が減る。三者のどれかに規則を足すときも、共通の部分は一か所で済む。
- 三者の違いが表で明示され、意図しない差異（元の本体の呼び出しなど）が見つけやすくなる。

**デメリット**

- 選ぶ時期（実行時かコンパイル時か）と、ヘッダーの規則が違うので、共通の章の範囲を慎重に決める必要がある。

**判定**：共通規則の章は推奨する。ヘッダーの規則と構文は統合しない。元の本体を呼べるかどうかの統一は保留とし、要件本体の利用例が集まってから決める。

### 案 7. closed Contract を一般の機能にする

**変更点**

- `closed contract K` を加える。K への適合を成立させられるのは、K の Kotonoha だけとする。K の Kotonoha でない型の所有者は、型内適合でも成立させられない。
- K の Kotonoha が宣言を集め終えた時点で、全適合型に共通する能力を一度だけ計算する。
  - 各適合型 X が寄与するのは、X の Constraints が許すすべての束縛で無条件に成り立つ、`X is Y<…X…>` の形の適合と関連型の束縛だけである。X を Self に置き換えて比べる。
  - 総称の適合型（`FromEnd<T>` など）も計算に加える。除くと、`T is K` がその適合型の持たない能力を証明してしまう。総称の適合型があると、共通部分は小さくなる。
  - 条件付きの適合は、能力に数えない。
- `T is K` は、その共通の能力を証明する。
- PrimitiveInteger を、12 の整数型を適合型とする閉じた Contract として定め直す。

**具体的なコード**

```kimi
// 利用者の Kotonoha
public closed contract SmallUnsigned
conform u8 is SmallUnsigned
conform u16 is SmallUnsigned

func double<T>(value: T) -> T
    T is SmallUnsigned
    return value + value    // u8 と u16 に共通の Addable<Self>（Output は Self）から証明される。両辺は借用するので Copy は使わない
```

**メリット**

- PrimitiveInteger を閉じた Contract にすれば、整数型に共通する適合（Copy、Owned、Equatable、Comparable、Utf8Format、Position、五つの二項算術 Contract）と、[§8.7](../../docs/spec/08-generics-constraints-and-contracts.md#87-constraint-proof-system) の「Closed implication」が、一つの規則の帰結になる。
- Position と PositionRange の「閉じた Contract」も、同じ機能で書ける。
- 利用者も閉じた Contract を定義できる。
- 計算は Contract ごとに一度で、使う側では場合分けをしない。§8.7 の原則は保たれる。

**デメリット**

- Contract で表せない能力は特例のまま残る。`Wrapping<T>` の算術適合と Negatable、Left 側の適格性（算術 Contract §2.2）、リテラルの範囲、数値変換、ビット演算・シフト・増減などの組み込み演算子である。
- 閉じた Contract に適合型を加えると、共通の能力が減ることがある。公開 API の変更として扱う必要がある。
- 計算する時点を、specialization の集合を閉じる時点（[§8.8.3](../../docs/spec/08-generics-constraints-and-contracts.md#883-selection-and-declaration-ownership)）と同じように定める必要がある。

**判定**：条件付きで推奨する。基本案の所有者規則が前提になる。どれだけの特例が消えるかを、[§8.4.7.3](../../docs/spec/08-generics-constraints-and-contracts.md#8473-primitiveinteger) と算術 Contract の規則で確かめてから採否を決める。

### 案 8. 組み込み適合を Kimi のソースで書く

**変更点**

- プリミティブ型の Equatable、Comparable、Utf8Format、算術 Contract、Position への適合を、Kimi のソースの通常の適合として宣言する。本体は intrinsic を呼ぶ。
- Kimi は i32 と Equatable の両方の所有者なので、基本案では `conform` を使えず、型内適合で書く必要がある。現行では型内適合の実装はメンバーになり、整数にメンバーを加えてしまう（§8.4.7）。そのため、案 1（ブロックの宣言はメンバーにならない）と、プリミティブ型を Kimi のソースで宣言する仕組みが前提になる。

**具体的なコード**

```kimi
// Kimi（案 1 のブロックの形。プリミティブ型を Kimi のソースで宣言する場合。構文と intrinsic の名前は仮）
public intrinsic struct i32
    Self is Equatable
        func equals(self, other: ref/Self) -> bool => Intrinsics.equalI32(self, other)
```

**メリット**

- 組み込み適合を、仕様の表（§8.4.7、算術 Contract §1.1、§22.1）ではなく Kimi のソースの宣言で表せる。組み込み適合と利用者の適合が、同じ検証、同じ記録、同じ索引を通る。
- コンパイラーが個別に知る witness が減る。単一 MIR 計画の §5.8（アルゴリズムをライブラリへ移す）と同じ方向である。

**デメリット**

- 算術 Contract の「明示的な適合は、利用者の provider 型を Self に要する」という条件を改める必要がある。
- 組み込みの数値どうしの演算子は従来の規則のままで、ソースの適合は総称コードと関数値でしか使われない。
- 組み込み型にメンバーを加えないという規則（§8.4.7）を保つため、これらの適合を基本案 §6 の段 2 から除く例外が要る。
- Kimi ライブラリの束縛（Bind）の量が増える。Bind の固定費はすでに Kimi ライブラリが大半を占めている。
- O0 でも組み込み演算と同じ性能になるよう、intrinsic を確実に展開する必要がある。intrinsic の witness の失敗位置（算術 Contract §3）も保つ必要がある。

**判定**：保留する。利点がコンパイラーの内部に限られる一方で、組み込み型の扱いに例外が増える。単一 MIR 計画の R8 で、コンパイラーが知る witness を減らす必要が生じたときに、案 1 の後で再検討する。

### 案 9. Property 要件を accessor 要件の束にする

**変更点**

- `property total: isize has get, set` を、それぞれ Requirement Identity を持つ getter 要件と setter 要件の二つとして定義する。
- accessor 要件は Property の名前で引き、同名の関数要件の group には入らない。getter（`ref/Self`）と setter（`uniq/Self`）の受け手の形が違っても、[§8.4.1](../../docs/spec/08-generics-constraints-and-contracts.md#841-function-requirements) の「同名要件の受け手の形は一つ」には反しない。
- 要件本体は accessor ごとに持てる。

**具体的なコード**

```kimi
contract Measured
    property isEmpty: bool
        get(self: ref/Self) -> bool => self.count() == 0   // getter の要件本体（この案で可能になる）

    func count(self) -> isize
```

**メリット**

- 基本案で保留した Property の本体が、関数の要件本体と同じ規則で入る。Property 全体の本体か getter だけの本体か、という問題がなくなる。
- [§11.4.1](../../docs/spec/11-properties.md#1141-operation-compatibility) の、関数の要件と並行する照合の規則を減らせる（現行も、要求される accessor は別々に検査している）。

**デメリット**

- Field を実装に使う bridge（§11.4.2）で getter をまかない、setter は本体を使う、という組み合わせの規則を決める必要がある。`let` と `var` の権限との関係も決める必要がある。ただし、案 1 を先に採っていれば bridge はなく、この問題は生じない。

**判定**：Property の要件本体を導入するときに推奨する。それまでは不要である。

### 案 10. 適合は全体で一度だけ検証し、索引で引く

**変更点**

- 各適合は、それを宣言した Kotonoha で一度だけ検証する。下流は記録を使うだけで、検証し直さない。
- 型宣言ごとに、祖先まで閉包した Contract の一覧（条件付き）を公開する。
- 検証の単位は型ごととする（祖先の適合と経路の一致の検査を含む）。適合の間の依存を強連結成分にまとめ、成分ごとに並列に検証する。

**具体的なコード**

```kimi
// Kotonoha Mine
public contract Measured
    func count(self) -> isize
public contract Bounded: Measured
    func capacity(self) -> isize

public struct Bag
    Self is Bounded
    var items: Array<i32> = []
    public func count(self) -> isize => self.items.length
    public func capacity(self) -> isize => 8

conform Foreign.Buffer is Measured
    func count(self) -> isize => self.length
conform i32 is Measured
    func count(self) -> isize => 1
```

```text
Mine の適合の索引（祖先まで閉包）
  Mine.Bag       -> Mine.Measured, Mine.Bounded
  Foreign.Buffer -> Mine.Measured
  i32            -> Mine.Measured
```

`Foreign.Buffer is Mine.Measured` は、Foreign と Mine の索引だけを引く。`Foreign.Buffer is Mine.Bounded` はどちらの索引にもないので Refuted になり、両方の索引を absence dependency として記録する。

**メリット**

- 具体型の `X is C` と、適合しないという判定が、二つの索引を引くだけで決まる。
- 検証結果を再利用でき、Bind の時間が下がる。

**デメリット**

- 公開する記録（[§18.7.2](../../docs/spec/18-modules-and-dependencies.md#1872-correspondence-and-semantic-records)）の形式を決める必要がある。

**判定**：仕様は基本案の §4.2、§4.8、§7 で足りる。実装の方法として採用し、実装計画で扱う。

### 案 11. 単相化の鍵を、本体が区別する情報に縮める

**変更点**

- 総称本体ごとに、型引数のどの情報（layout と破棄の手順、使う witness の欄、specialization に関わるか）に依存するかを要約する。
- 実体化の鍵をその組にして、同じ鍵の実体はコードを共有する。
- §8.8.3 は、specialization の選択がコードの共有とは独立だと定めているので、仕様は変わらない。

**具体的なコード**

```kimi
func firstOrNone<T>(items: ref/Array<T>) -> Option<ref/T>
    // 要素を移動も比較もしないので、要素の layout が同じなら同じ機械語にできる
    ...
```

`firstOrNone<ref/A>` と `firstOrNone<ref/B>` は同じ鍵になり、コードを共有する。

**メリット**

- 生成コードの量とコンパイル時間が減る。要件本体の実体も、使う欄の witness が同じなら共有できる。

**デメリット**

- 要約の計算が増える。
- デバッグ情報で、共有した実体と元の型を対応づける必要がある。
- LLVM の関数の併合で十分な場合もあり、効果は計測しないと分からない。

**判定**：仕様は変えない。単一 MIR 計画の後に、実体化の数とコードの量を計測してから判断する。

## 3. 導入の順序

段は、仕様に取り込む順序を表す。実装の時期は別に決める。第 1 段には、無条件に推奨する案だけを置く。単一 MIR 計画は新しい言語機能を範囲外とする（同計画 §1.2）ので、言語機能の実装の時期は同計画と調整する。

| 段階 | 案 | 前提 | 理由 |
| --- | --- | --- | --- |
| 第 0 段 | 基本案 | なし | 以後の前提 |
| 第 1 段 | 案 3、案 4 | 基本案（案 3 は基本案と同時でもよい） | 移行が小さく、規則が大きく減る |
| 第 2 段 | 案 7 | 基本案の所有者規則 | 消える特例の量を確かめてから採る |
| 第 3 段 | 案 1 | 案 4 | 適合の構造を作り直す |
| 第 4 段 | 案 5 | 単一 MIR 計画の R7 | 説明の土台を R7 の出力と合わせる |
| 第 5 段 | 案 6、案 9 | なし（案 9 は Property の要件本体を入れる判断） | 独立しており急がない |

段階に含めない案：案 2（見送り）、案 8（保留。単一 MIR 計画の R8 で、案 1 の後に再検討）、案 10・案 11（仕様を変えない。実装の方法として扱う）。

### 3.1. 各段の基本案への影響

この節の節番号は、断りがなければ基本案の節を指す（「§11 の §8.4.4 の行」は、基本案 §11 の表の、正式仕様 §8.4.4 の行）。

- **第 1 段**
  - 案 3：基本案 §3.5 を削除する。§4.4 第 2 項を「継承による適合はない。派生型 S の適合は、S の所有者の型内適合か、C の所有者の `conform S is C` で宣言する。」とし、その後の理由と例を削除する。§4.2 末尾の「外部適合を継承しないこと（本書 §4.4）」を「継承による適合がないこと（本書 §4.4）」に改める。§4.6 第 4 項と §4.8 第 1 項から継承した型内適合を除く。§6 の段 2 を「X 自身の宣言による適合」にする。§10 の「外部適合の継承」の行を削除する。§11 の §8.4.4・§8.5 の行から継承の項目を除く。
  - 案 4：§5.2 の第 1 項に、正式仕様 §8.4.6 の同値の証明でまとめる段落と、正式仕様 §8.4.10.4 の該当の文を削除することを加える。§11 では、§8.4.6 の行に前者を、§8.4.10 の行に後者を加える。§5.2 第 1 項の §9.5 についての文と、§6 の文言は変わらない。
- **第 2 段**（案 7）：§4.1 の第 4 項を「compiler-intrinsic Contract は外部適合の対象にならない。閉じた Contract への適合は、その Contract の Kotonoha だけが成立させる」とする。§4.2 に、閉じた Contract K への適合は K の Kotonoha だけが成立させ、K の Kotonoha でない型の所有者は成立させられないという例外を加える。§4.8 に、閉じた K への適合は K の索引だけで決まることを注記する。
- **第 3 段**（案 1）
  - §2 の「型内適合」の定義を、メンバー位置の `Self is C` とブロックに改める。「明示実装」を「宣言として書かれた実装。適合のブロックの宣言（要件本体と組み込みの witness は含まない）」とし、「実装 Identity」からメンバーの場合を除く。
  - §3.4 の候補範囲の区別を削除する（型内も外部もブロックだけ）。§4.1 第 2 項の理由（ブロックの宣言がメンバーになるかが書く場所で変わる）を削除する。
  - §5.1 の「選ばれる実装」と §7 の出どころから「メンバー」を除く。§7 の「Contract に本体付きの要件を加えても…」の項のうち、「この変化は対応表の記録で確認できる。」までの 3 文を「Contract に本体付きの要件を加えても、識別キーの合う宣言をブロックに持たない適合は壊れない。」に改める。子孫の Contract についての「ただし…」の 2 文は残す。
  - §6 の例を、メンバー位置の `Self is Measured` とブロックで書き直し、`bag.count()` を段 2 の例にする。
  - §11 の §8.4.4 の行の「外部適合ではブロックの宣言だけが候補」を「どの適合も、ブロックの宣言だけが候補」に、§9.5 の行の「外部適合の例外」を「どの適合の実装も、この探索では選ばない」に改める。§11.3 の §11.4.1・§11.4.2 の行を「適合の Property 実装はブロックの computed から選ぶ。Field による bridge は使わない」に改める。
- **第 4 段**（案 5）：規則の書き方が変わる。意味は変わらない。
- **第 5 段**
  - 案 6：§3.2 と §5.1 の再帰の記述、§3.4 の「本体には切り替えない」を、共通の章への参照に置き換える。意味は変わらず、§9 の規約は残す。
  - 案 9：§2 の「要件本体」の定義を「関数要件と accessor 要件が持つ本体」に改め、§3.1 冒頭に accessor 要件も本体を持てることを加える。§3.1 の制限と §10 の行から Property を外す（関連型は残す）。

## 4. 採らない案

| 案 | 理由 |
| --- | --- |
| 構造的な適合（同名のメンバーがあれば自動的に適合する） | 局所推論に反する。依存先の変更で適合が変わる |
| 名前付きの複数の適合（同じ型と Contract に複数の実装） | 一意性と経路の一致が崩れる |
| Contract の型引数と関連型を一つの仕組みにまとめる | Origin 引数を持つ関連型の族があるため、規則はかえって増える |
| 子 Contract による要件本体の優先順位付きの上書き | 実装の選択に優先順位が要る。基本案 §10 の判断を維持する |
| specialize を要件本体と適合で置き換える | specialize は制約のない総称関数に使えるが、適合で置き換えるには、すべての型が適合する blanket 適合が必要になる |
