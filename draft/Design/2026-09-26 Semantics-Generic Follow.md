# 設計案：ペア `s/T` の追従（Semantics に依存しない Get）

日付: 2026-09-26

状態: 提案。正式仕様への取り込み・実装・実行検証は未実施。PLAN の G21、Program 39 の根拠文書。

## 1. 目的

`Collection<E>` の Get は Place を返せば済む（§4.6.9、§7.1.1）。呼び出し側は、その Place に対してローカル変数と同じ操作（裸の Copy、`@ref`、`@uniq`、`@follow`、期待型での適合、代入、`remove`）を選ぶ。Collection 本体は E の Semantics を知らない。

残る一つの穴は、**E の Semantics を知らないまま E の中身 `T` を扱う総称コード**である。

```kimi
// c[i] の格納型は s/T。s が未確定なので、既存規則では s/T が経路の終端になる（§3.4.1）。
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    return c[i]@ref      // ref/(s/T)。ref/T ではない。
```

`s = owner` なら要素そのものを借り、`s = ref`／`uniq` なら参照先を再借用したい。今は `s` を一つに固定するか、Semantics ごとに本体を書き分けるしかない。

本書は、この穴を **既存規則の一般化** で埋める。新しい演算子、Contract、実行時判定は加えない。

## 2. 要点

**admitted set が `value or valueborrow` に収まるペア層は、あってもなくてもよい一つの安全な参照層として扱う。** `s = owner` なら層はなく、`s = ref`／`uniq` なら一層ある。ペア層に対する操作は、admitted set の各 Semantics に既存規則を当てはめて決め（§3.2）、総称本体はそのすべてで妥当でなければならない（§8.10）。選ぶ層は定義時に決まり、具体化で変わらない（§3.4.1）。

| 既存規則 | 本案での使い方 |
| --- | --- |
| §8.1.1 ペア束縛 | 追従先は直接対象（`T`、または `s/U` の `U`） |
| §8.1.2 条件付き Origin スロット | 追従したペア層の出現の外側 Origin を依存に使う。各場合の依存も同じ仕組みで条件付きに保つ |
| §8.7 admitted set | 追従の可否を決め、各場合の集合を与える |
| §8.9 Access Effect | 効果は各場合で異なってよい（記号的な効果） |
| §13.5.5 `@follow`・Borrow・Reborrow | `owner` の場合は Borrow、`ref`／`uniq` の場合は Reborrow の規則を使う。Take は与えない |
| §3.4.1、§3.5.3、§13.4、§10.2、§7.3 | 層をたどる既存の位置にペア層を加える |
| §14.8.1、§14.6.2、§15.1.6 | Pattern と `for` の mode にペア層を加える |

## 3. 提案規則

### 3.1. ペア層と追従

**ペア層**とは、正規化後の外側 Semantics がペア束縛 `s` である型をいう。元の `s/T`（直接対象 `T`）と、別の対象への適用 `s/U`（直接対象 `U`）を含み、`T` 単独は含まない。以下、直接対象を `U` と書く。

Place `P` または値 `E` の型がペア層で、`s is value or valueborrow` が Proven のとき、`@follow` は `U` を格納する Place（**選ばれる Place**、§13.5.5.1）を選ぶ。一層だけ外し（`s/(t/U)` は二回書く）、参照値も参照先も Copy／Move しない。条件を満たさないペア（`obj`、`rc`、`arc`、`objref`、`objuniq`、`unsafe` を含む admitted set）への `@follow` は定義時エラーとし、診断は admitted set と不足する制約を示す。

`x@s`（Semantics の適用）と `@follow`（Place の選択）は型の外側の扱いが対応するだけで、互いの逆演算ではない。`s = owner` で所有 Place に `x@s` を書くと Copy を要し、結果は別の一時値である。それに `@follow` を書いても元の `x` は選ばれず、Take も戻らない。

### 3.2. 共通規則

各場合の既存規則は次のとおりである。`p` は `P` を有効に保つ依存（経路と Loan）、`M` は直前までの mode（§15.1.6）を表す。

| 項目 | `owner` | `ref` | `uniq` |
| --- | --- | --- | --- |
| 選ばれる Place | `P` 自身。値 `E` なら一時値で、記憶域が要るときだけ実体化する（§3.6.1） | 参照先 | 参照先 |
| 効果（§8.9） | Borrow | Reborrow。§10.2 の共有参照の適合では参照の Copy | Reborrow |
| Read | `P` が Read 可能 | 可 | 可 |
| Write | `P` が排他的に書き込み可能 | 不可 | 可。`P` が `let` でもよいが、共有経路を越えては不可 |
| 依存 | Place なら `p`。値なら一時 Place（§3.6.2） | `o` と親 Loan。参照値を置いたスロットには依存しない（§13.5.5.2） | 同左 |
| 裸の Place を Subject にした mode | Shared | Shared | Exclusive |
| ペア層を越えた後の mode | `M` と Exclusive の弱い方 | Shared | `M` と Exclusive の弱い方 |

総称本体では、これを次のように合成する。

- **権限・mode**: admitted set の各場合のうち最も弱いもの。例えば Write には `s is owner or uniq` が要り、`owner` が許されるなら `P` の書き込み可能性も要る。
- **依存**: 各場合の依存を、その `s` のときだけ有効な条件付きの依存として保つ（§8.1.2 の条件付きスロットと同じ仕組み）。定義時の検査は有効になりうるすべての依存で行うので、`owner` と借用の両方が許されると、実質的に `p` と `o` の両方に依存する。
  - 各場合の依存は、その場合の経路全体に既存規則を当てて求め、層ごとに累積しない。上の表の依存の行は、選ばれる Place がペア層の直下にある場合である。
  - 経路を読む間だけ要る依存と、結果に残る依存を区別する。例えば `s/(ref/V during a)` を期待型 `ref/V` に適合させると、どの場合も内側の `ref` を Copy するので（§10.2）、結果は `a` と実際の Loan だけに依存する。外側の `P` や `o` は読み出す時点で有効であればよい。
- **効果**: 各場合で既存規則が合法なので、§8.9 の記号的な効果として扱う。
- **Take**: `@follow` は Take を与えない（§13.5.5.1）ので、どの場合でも含まない。admitted set が `{owner}` だけでも同じで、Pattern もペア層を越えて ByValue にはならない。所有権が必要なら `remove` や `Intrinsics.exchange` を使う。

`o` は、追従したペア層の出現に属する条件付き外側 Origin である。元の `s/T` なら `W` の外側 Origin、別の `s/U` なら §8.1.2 の位置規則または明示注釈で定まるその出現のスロットであり、同じ `s` を使う別の入力の Origin とは独立している。`U` の内側 Origin と実際の Loan はそのまま保つ。

### 3.3. 暗黙の選択

層をたどる既存の位置では、各層で次を繰り返す。現在の完全型が、必要な宣言・構造・型を宣言または公開された要件（Contract 適合や型同一性の制約）で持てば、そこで停止する。持たず、その層が安全な参照層か条件を満たすペア層なら一段進む（ペア層は `U` へ）。それ以外の形状不明の型は終端であり、公開制約で使える能力だけを使って、具体化後に探索を再開しない（§3.4.1）。例えば `s/(ref/V)` のメンバー選択は、ペア層の後も `ref/V` から `V` へ進む。一方、関連型が `s/T` と同一で、その関連型に Contract 適合が要求されていれば、そのメンバーは完全型 `s/T` の層で選ばれ、`T` へは進まない。

| 位置 | 規則 |
| --- | --- |
| メンバー・添字・receiver の選択（§3.4.1） | 上の規則。ペア層の完全型が要件を持たなければ `U` へ進む |
| `ref/Self`／`uniq/Self` receiver（§7.3） | `p@follow@ref`／`p@follow@uniq` 相当。排他 receiver は §3.2 の Write を要する |
| 所有 receiver `Self`（§7.3） | 暗黙に行うのは `U` が Scalar のときの Scalar read だけ（`ref` の場合の規則）。それ以外は `U` が Copy であることを示して `p@follow.m()` と書く |
| 固定の期待型 `ref/U` への適合（§10.2） | 「層をたどる一つの共有参照」にペア層を含める。依存は §3.2 |
| 総称の呼び出し先の型推論（§10.2 の手順 1） | ペア層をたどらない。`X` には完全型 `W` を束縛する |
| Scalar read（§3.5.3）、比較（§13.4） | 終端の Scalar／比較可能な型まで、ペア層もたどる |
| 構造 Pattern（§14.8.1）、`for` の入口（§14.6.2） | mode は §3.2 の表から合成する（§15.1.6） |

`@ref`／`@uniq`、裸の取得、単独名の束縛はペア層をたどらない（`c[i]@ref` は `ref/(s/U)`）。

```kimi
// c: ref/Collection<s/T>。s is value or valueborrow、T is Loaded。
func inspect<X>(value: ref/X) -> ()

inspect(c[i])              // X = s/T。推論ではペア層をたどらず、要素スロットを共有借用する（c[i]@ref と同じ）。
inspect(c[i]@follow)       // X = T。選ばれる Place を共有借用する。
let weight = c[i].load()   // receiver の選択は U へ進む。

// bonus: s/Option<i32>。裸の Subject の mode は owner・ref で Shared、uniq で Exclusive なので Shared。
match bonus
    .Some(let extra) => use(extra)   // extra: ref/i32
    .None => ()
```

### 3.4. 具体化と生成

定義時に追従計画（選ばれる Place の型、効果、権限、条件付きの依存、Loan）を確定し、具体化ではその計画に `s` を代入する（§8.1.2 末尾、§8.9）。具体化後に通常の所有 Place として解析し直すことはなく、Take の除外も残る。

単相化では既存のアドレスを再利用する。所有 Place はそのアドレス、参照値は保持しているアドレスを使い、参照を格納する Place からは必要なときだけポインタを読み出す。追従のために Copy、実行時の分岐、ヒープ割り当て、不要な参照一時スロットを加えない。将来の総称コード共有では、`s` を共有キーに含めるか、追従を Type policy の操作にする必要がある（§21.3.3 の設計に委ねる）。

## 4. 例

```kimi
contract Loaded
    func load(self: ref/Self) -> i32

struct Collection<E>
    Self is UniqIndexable<isize>
    associate Element is E
    var items: Array<E>
    public computed indices: ResolvedRange
        get() -> ResolvedRange => self.items.indices
    public func index(self, key: ref/isize) -> place ref/E during self => self.items[key]
    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/E during self => self.items[key]

// s に関係なく T の共有参照を返す。
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    s is value or valueborrow
    return c[i]@follow@ref        // owner: 要素の Borrow。ref／uniq: 参照先の共有 Reborrow。

// ref を除けば排他参照を返せる。
func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c
    s is owner or uniq
    return c[i]@follow@uniq

// 公開の indices と添字で要素 Place を得て、ペア層越しに共有 receiver で呼ぶ（items は非公開、§9.3）。
func total<s/T>(c: ref/Collection<s/T>) -> i32
    s is value or valueborrow
    T is Loaded
    var sum: i32 = 0
    for i in c.indices
        sum += c[i].load()
    return sum

// Scalar read：factor はペア層越しに i32 を読む。item と factor の外側 Origin は独立している。
func scaled<s/T>(item: s/T, factor: s/i32) -> i32
    s is value or valueborrow
    T is Loaded
    return item.load() * factor
```

`view` を `Collection<Node>`、`Collection<ref/Node during a>`、`Collection<uniq/Node during a>` で使うと、具体化ごとの依存は、それぞれ要素の Borrow、`a` への共有 Reborrow、`a` と親 Loan への共有 Reborrow になる。定義時の検査はそのすべてで行われ、結果の型は署名どおり `during c` である。`viewUniq` を `Collection<ref/Node>` に使うと、`s is owner or uniq` が Refuted なので呼び出し側で拒否される。

## 5. 検討した代替案

| 案 | 判断 | 理由 |
| --- | --- | --- |
| Semantics ごとの overload | 不採用 | 同じ処理を Semantics の数だけ書く重複と保守負担が残り、`Collection` 側の直交性も崩れる。なお、制約だけが異なる同一署名の宣言は §9.1 の重複で宣言できず、引数型が異なる overload（`Collection<T>`／`Collection<ref/T>` など）とは区別される |
| `#switch` で `s` を分岐 | 不採用 | コンパイル時ディレクティブは型を検査しない（§8.9、§19） |
| `Follow` Contract（Rust の `Deref` 相当）を `ref/T`、`uniq/T`、`T` に適合させる | 不採用 | 参照型・任意の T への一括適合は導入しない方針（§22.1.2.2）。Contract の追加、恒等適合、優先規則が増える |
| 専用演算子（`@target` など） | 不採用 | `@follow` の意味「T が格納された Place を選ぶ」の一般化で足りる。`owner` での層の省略は総称の具体化だけで起こり、具体型の `x@follow` を許すものではない |
| Place では一律に `p` と `o` の両方に依存する | 不採用 | §13.5.5.2 と §10.2 の既存規則より寿命が短くなり、値オペランドとも規則が分かれる。各場合の依存を条件付きに保てば、`owner` が許されるときは同じ結果になる |
| 収納側を `s is borrow` に限定 | 不採用 | 所有要素のコレクションを総称的に扱えない |
| 実行時タグで `s` を判別 | 不採用 | 単相化で不要。割り当て・分岐が増え、Semantics が型情報であるという原則に反する |

## 6. 導入しない境界

- object Semantics のペア（`s is object`／`objectborrow`）の追従。`T is Sealed` と完全 payload の証明（§13.5.5.1）を組み合わせれば将来拡張できるが、本案には含めない。
- `unsafe` を含む admitted set の追従。
- 具体型の所有 Place への `x@follow`（無操作としての許可）。

## 7. 実装方針

前提は P27 の Place 基盤だが、最小の再現は `struct Box<s/T>` の Field `item: s/T` に対する `box.item@follow` で済み、Indexable は不要である。

| 段階 | 作業 |
| --- | --- |
| Binding | 正規化後の型とペア束縛の証拠から「完全型・Semantics パラメーター・直接対象・条件付き外側 Origin スロット」を取り出す共通処理を設ける。元の `s/T` は `WholeType`（`BoundTypeKind.Parameter`）、別の `s/U` は `SemanticsApplication` であり、`TargetProjection`（`T` 単独）はペア層ではない。別名と関連型の射影を展開した後の型にも使う。キャッシュするのは型だけで決まる**追従の雛形**（直接対象、Origin スロット、admitted set の各場合の効果と権限・mode の上限）に限り、内部化された `BoundType` ごとに一度だけ計算する。入力 Place とその書き込み可能性（`let`／`var`、共有・排他経路、getter の結果）、現在の mode、Origin と Loan、停止条件（メンバー名・期待型など）は利用ごとに与える。`@follow`、`ReceiverThroughLayers`、Scalar read、比較、共通適合、Pattern、`for` は停止条件と要求権限だけを指定する。失敗は admitted set を示す診断 |
| 所有権解析 | §3.2 の表を admitted set で合成した記号的な計画：最も弱い権限・mode、条件付きの依存、記号的な効果。具体化ではこの計画を代入する |
| 生成 | 置換ごとの本体で §3.4 のとおり既存のアドレスを再利用する。ABI は変わらない |
| 検証 | 下記 |

- 実行と依存: `Box<s/T>`、`Collection<s/T>` の三具体化、出現ごとの Origin、admitted set が `valueborrow` だけのときの Reborrow と同じ依存、値オペランドの依存
- 拒否: `ref` を含む admitted set での排他、Take、object／unsafe、所有 receiver の暗黙の Copy
- 暗黙の選択: メンバー、receiver、Scalar read、比較、共通適合、型推論（`X = W`）、Pattern と `for`（裸の Subject と ByValue の Subject）
- 性能: warm 割り当てゼロ、大きな所有値を Copy しない、参照一時スロットを作らない、getter と添字を一度だけ評価する

Program 39 は `Collection<s/T>` を使い、三つの具体化と拒否例を一つの Application にまとめる。

## 8. 取り込み先

採用時は次を更新し、本書を `draft/INTEGRATED.md` に記録して凍結する。

| 節 | 変更 |
| --- | --- |
| §13.5.5.1 | 「ペア層」の段落：定義、§3.2 の表と合成規則、Take なし |
| §3.4.1 | ペア層をたどってよい層に追加。一段進んで既存規則を繰り返す停止条件 |
| §3.5.3、§13.4、§10.2 | Scalar read、比較、層をたどる共有参照にペア層を含める。型推論ではたどらない |
| §7.3 | receiver の表にペア層（所有 receiver は Scalar read だけ） |
| §8.1.2 | 追従した出現の条件付き外側 Origin と、各場合の依存を条件付きに保つこと |
| §8.9 | `@s` と追従の対応（逆演算ではない）、記号的な効果 |
| §14.8.1、§14.6.2、§15.1.6 | Pattern と `for` の mode にペア層を含める |
| 付録 A.23、E、F | 検証項目、用語（ペア層、選ばれる Place）、構文は変更なし |
| Appendix D | object ペアの追従を将来拡張として記載 |
