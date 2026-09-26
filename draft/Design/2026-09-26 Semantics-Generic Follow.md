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

`s = owner` なら要素そのものを借り、`s = ref`／`uniq` なら参照先を再借用したい。今は `s` を一つに固定するか、Semantics ごとに関数を書き分けるしかない。後者は同名関数の receiver 形状規則（§7.3）とも相性が悪い。

本書は、この穴を **既存の一規則の一般化** で埋める案を示す。新しい演算子、Contract、実行時判定は加えない。

## 2. 既存規則との関係

| 既存規則 | 内容 | 本案との関係 |
| --- | --- | --- |
| §8.1.1 ペア束縛 | `<s/T>` は完全型 `W` を束縛し、`s = OuterSemantics(W)`、`T = DirectTarget(W)`、`o = OuterOrigin(W)`（外側が安全な借用のときだけ存在） | 追従先は常に `T`、依存に `o` を使う |
| §8.1.2 条件付き Origin スロット | 直接入力の `s/U` は、`s` が安全な借用のときだけ有効な外側 Origin スロットを持つ | 追従結果の依存の定義に流用する |
| §8.7 admitted set | `s` への要求は admitted set の包含で決まる | 追従の可否と権限を admitted set で決める |
| §8.9 `x@s` | `T` の Place から `s/T` を作る。借用束縛なら借用、所有束縛なら同型取得 | 本案はその逆方向。`@s` と `@follow` が対になる |
| §3.4.1 参照経路の選択 | 安全な参照層は参照先へ進み、形状未確定の型引数は終端 | 「admitted set が value/valueborrow に収まるペア層」を、進んでよい層に加える |
| §13.5.5.1 `@follow` | `ref/T`／`uniq/T` の参照先 Place を選ぶ。Take は与えない | ペア層にも同じ操作を定義する |
| §10.2 共通適合、§3.5.3 Scalar read、§13.4 比較 | 安全な参照層をたどる | 同じ層の扱いを拡張する |
| §8.10 全称検証 | 総称本体は許される全束縛で妥当でなければならない | 権限・依存は admitted set 上の共通部分として自動的に決まる |

つまり本案は「**admitted set が `value or valueborrow` に収まるペア層は、あってもなくてもよい一つの安全な参照層として扱う**」の一文に集約できる。`s = owner` のときは層がなく、`s = ref`／`uniq` のときは一層ある。どの場合も到達先は `T` の Place であり、具体化後に選ぶ層が変わることはない（§3.4.1 の要件を満たす）。

## 3. 提案規則

### 3.1. 追従の定義

Place `P`（または値 `E`）の格納型が、ペア束縛 `s` を外側 Semantics に持つ `s/U` であり、`s is value or valueborrow` が Proven のとき、`P@follow` は **対象 Place** を選ぶ。

| 具体化後の `s` | `P@follow` が選ぶ Place |
| --- | --- |
| `owner` | `P` 自身（値 `E` なら実体化した一時 Place） |
| `ref`、`uniq` | 格納された参照の参照先（既存 §13.5.5.1） |

選ばれる Place の格納型は `U`。`@follow` は一層だけ外す（入れ子 `s/(t/U)` は二回書く）。参照値も参照先も Copy／Move しない。

条件を満たさないペア（`obj`、`rc`、`arc`、`objref`、`objuniq`、`unsafe` を含む admitted set）への `@follow` は定義時エラー。診断は admitted set と不足する制約（`s is value or valueborrow`）を示す。

### 3.2. 権限

対象 Place の権限は、許される各 `s` で得られる権限の共通部分である。総称本体は全束縛で妥当でなければならない（§8.10）ので、新しい規則ではなく既存規則の帰結だが、明文化する。

| 権限 | `owner` | `ref` | `uniq` | 総称での条件 |
| --- | --- | --- | --- | --- |
| Read | `P` が Read 可能 | 可 | 可 | `P` が Read 可能 |
| Write（排他借用・置換） | `P` が排他的に書き込み可能 | 不可 | 可（`P` が `let` でも可） | `s is owner or uniq` が Proven。`owner` が許されるなら `P` が排他的に書き込み可能 |
| Take | — | — | — | 常に不可（§13.5.5.1 と同じ） |

共有経路を越えて排他権限を回復しない規則（§3.4.1）は、そのまま適用する。

### 3.3. 依存関係

対象 Place の借用は、次の両方に依存する。

1. `P` を有効に保つ依存（`P` の経路と Loan）。
2. ペア層の外側 Origin `o`。`s = owner` のときは不活性（§8.1.2 の条件付きスロットと同じ）。

したがって `P@follow@ref` の型は `ref/U during (p and o)` である（`p` は `P` の借用 Origin）。`s = ref` の具体化では、参照の Copy より短い依存になる（`P` にも依存する）。これは総称コードの保守的な合成であり、具体型での直接の Copy を置き換えるものではない。既存の Loan 同一性・親子関係はそのまま保つ。

### 3.4. 暗黙の選択

明示 `@follow` に加えて、安全な参照層をたどる既存の位置で、条件を満たすペア層も同じようにたどる。

| 位置 | 規則 |
| --- | --- |
| メンバー・添字・receiver（§3.4.1） | ペア層に宣言がなければ `U` へ進む。ペア層自体は宣言を持たないので、常に `U` で確定する |
| receiver の暗黙借用（§7.3） | `p.m()` は `p@follow@ref`／`@uniq` 相当。排他 receiver は §3.2 の Write 条件を要求する |
| 期待型 `ref/U` での共通適合（§10.2） | 「層をたどる一つの共有参照」にペア層を含める。結果は §3.3 の依存を持つ |
| Scalar read（§3.5.3）、比較（§13.4） | 終端が Scalar／比較可能な `U` なら、ペア層もたどる |
| 構造 Pattern（§14.8.1） | 必要な構造が `U` にあればペア層をたどる。束縛型は経路の権限に従う（`ref/X` または `uniq/X`。`owner` 具体化なら所有分解） |

`@ref`／`@uniq`、裸の取得、単独名の束縛は、既存どおりペア層をたどらない（`c[i]@ref` は `ref/(s/U)`）。

### 3.5. 具体化と生成

定義時に操作（対象 Place の選択）と効果族（Read／Write／依存）を固定し、具体化ではその計画に `s` を代入する（§8.9「Instantiations may have different effects」）。単相化プロファイルでは、`owner` は無操作、`ref`／`uniq` はポインタの読み出しとなり、実行時の判定も割り当ても発生しない。将来の総称コード共有では、`s` を共有キーに含めるか、追従を Type policy の操作にする必要がある（§21.3.3 の設計に委ねる）。

## 4. 例

```kimi
struct Collection<E>
    Self is UniqIndexable<isize>
    associate Element is E
    var items: Array<E>
    public func index(self, key: ref/isize) -> place ref/E during self
        return self.items[key]
    public func indexUniq(self: uniq/Self, key: ref/isize) -> place uniq/E during self
        return self.items[key]

// s に関係なく T の共有参照を返す。
func view<s/T>(c: ref/Collection<s/T>, i: isize) -> ref/T during c
    s is value or valueborrow
    return c[i]@follow@ref        // owner: 要素を借用。ref/uniq: 参照先を共有再借用。

// ref を除けば排他参照を返せる。
func viewUniq<s/T>(c: uniq/Collection<s/T>, i: isize) -> uniq/T during c
    s is owner or uniq
    return c[i]@follow@uniq

// 暗黙の選択：T のメンバーと Scalar read。
func total<s/T>(c: ref/Collection<s/T>) -> i32
    s is value or valueborrow
    T is Measured                  // func size(self) -> i32 を要求する Contract
    var sum = 0
    for item in c.items            // item: ref/(s/T)
        sum += item.size()         // ペア層をたどり、T.size を共有 receiver で呼ぶ
    return sum
```

`view` を `Collection<Node>`、`Collection<ref/Node during a>`、`Collection<uniq/Node during a>` で使うと、それぞれ要素の借用、`a` と `c` の両方に依存する共有参照、`a` と `c` に依存する共有再借用になる。`viewUniq` を `Collection<ref/Node>` に使うことは `s is owner or uniq` が Refuted なので呼び出し側で拒否される。

## 5. 検討した代替案

| 案 | 判断 | 理由 |
| --- | --- | --- |
| Semantics ごとの overload | 不採用 | 同名関数は一つの receiver 形状（§7.3）。本体を三つ書く負担が残り、`Collection` 側の直交性も崩れる |
| `#switch` で `s` を分岐 | 不採用 | コンパイル時ディレクティブは型を検査しない（§8.9、§19） |
| `Follow` Contract（Rust の `Deref` 相当）を `ref/T`、`uniq/T`、`T` に適合させる | 不採用 | 参照型・任意の T への一括適合は導入しない方針（§22.1.2.2）。Contract の追加、恒等適合、優先規則が増える |
| 専用演算子（`@target` など） | 不採用 | `@follow` の意味「T が格納された Place を選ぶ」の一般化で足りる。`owner` での無操作は総称の具体化だけで起こり、具体型の `x@follow` を許すものではない |
| 収納側を `s is borrow` に限定 | 不採用 | 所有要素のコレクションを総称的に扱えない |
| 実行時タグで `s` を判別 | 不採用 | 単相化で不要。割り当て・分岐が増え、Semantics が型情報であるという原則に反する |

## 6. 導入しない境界

- object Semantics のペア（`s is object`／`objectborrow`）の追従。`T is Sealed` と完全 payload の証明（§13.5.5.1）を組み合わせれば将来拡張できるが、本案には含めない。
- `unsafe` を含む admitted set の追従。
- 具体型の所有 Place への `x@follow`（無操作としての許可）。
- ペア層への Take。所有権が必要なら `remove` や `Intrinsics.exchange` を使う。
- Loan の精緻化（`s = ref` の具体化で `P` への依存を落とすこと）。必要なら具体型で直接 Copy する。

## 7. 実装方針

前提は P27 の Place 基盤だが、最小の再現は `struct Box<s/T>` の Field `item: s/T` に対する `box.item@follow` で済み、Indexable は不要である。

| 段階 | 作業 |
| --- | --- |
| Binding | `ConversionKoto`（`@follow`）の対象がペア適用（`SemanticsApplication`／`TargetProjection`）のとき、admitted set ⊆ {owner, ref, uniq} を証明して結果型を `U` にする。`ReceiverThroughLayers`、Scalar read、比較、共通適合の層追跡にペア層を加える。失敗は admitted set を示す診断 |
| 所有権解析 | 記号的な効果計画：対象 Place の権限を §3.2 の共通部分、依存を `P` と条件付き Origin スロットの合成にする。具体化後の再解析（単相化）は既存の owner／ref／uniq の規則をそのまま使う |
| 生成 | 置換ごとの本体で `owner` は無操作、`ref`／`uniq` はポインタ読み出し。ABI は変わらない |
| 検証 | `Box<s/T>`、`Collection<s/T>` の三具体化での実行と依存、`ref` を含む admitted set での排他拒否、Take 拒否、object／unsafe の拒否、暗黙選択（メンバー・receiver・Scalar read・比較・Pattern）、Origin の合成、warm 割り当てゼロ |

Program 39 は `Collection<s/T>` を使い、三つの具体化と拒否例を一つの Application にまとめる。

## 8. 取り込み先

採用時は次を更新し、本書を `draft/INTEGRATED.md` に記録して凍結する。

| 節 | 変更 |
| --- | --- |
| §3.4.1 | 「admitted set が value/valueborrow に収まるペア層」を、たどってよい層に追加 |
| §3.5.3、§13.4、§10.2 | Scalar read、比較、層をたどる共有参照にペア層を含める |
| §8.1.2 | 条件付き外側 Origin スロットを追従結果の依存に使うことを明記 |
| §8.9 | `@s` の逆方向として追従を追加。効果の一意性と具体化ごとの効果 |
| §13.5.5.1 | 「ペア対象」の段落：定義、権限表、依存、Take なし |
| §14.8.1 | 構造 Pattern のペア層 |
| 付録 A.23、E、F | 検証項目、用語（対象 Place）、構文は変更なし |
| Appendix D | object ペアの追従を将来拡張として記載 |
