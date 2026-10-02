# 仕様変更案：範囲と位置の再設計 B

日付: 2026-09-29

状態: 最終提案。正式仕様への取り込み・実装・検証は未実施。

本書で変更する事項は、`docs/SPEC.md` とその参照先より優先する。本書で変更しない事項には既存仕様を適用する。

- **基準**: 「整数範囲と位置範囲」（2026-09-28）を取り込んだ後の SPEC。
- **関係**: 本書は「半開区間への統一と ClosedRange」（2026-09-29）の代替案である。本書を採用する場合、同書は取り込まない。

## 1. Current Specification — 現在の仕様

### 1.1. 仕様

| 型 | 格納 | 役割 |
| --- | --- | --- |
| `Index` | `offset: isize`・`isFromEnd: bool` | 先頭または末尾からの位置。方向は実行時の値 |
| `Range<T>` | `start: T`・`end: T`・`isInclusive: bool` | 両端が整数の範囲。反復・解決できる |
| `IndexRange` | `start: Index`・`end: Index`・`isInclusive: bool` | Index の境界か省略を含む範囲。解決だけできる |
| `ResolvedRange` | `start: isize`・`end: isize` | 長さに対して検証済みの半開区間。反復できる |

- **種別**: 両端が整数なら `Range<T>`、Index の境界か省略があれば `IndexRange` になる。`..` と `..=` は同じ型の値で、フラグで区別する。
- **Index 化**: `^x` と `IndexRange` の整数境界は、任意の整数から Index を作る。負数と isize に収まらない値は、その時点で Abort する。
- **位置の引数**: 要素添字、`tryGet`・`insert`・`remove`・`splitAt` などは、`isize` と `Index` のオーバーロードで受け取る。
- **範囲の引数**: 範囲添字は3つの範囲型を値で受け取る。`trySlice` はオーバーロード3つで受け取る。

### 1.2. 問題点

- **P1 規則が多い**: 種別の決定表、Index 化の規則と評価順、包含終端 `^0` の禁止など、範囲の形ごとに個別の規則がある。
- **P2 失敗の段階がそろわない**: `s.trySlice(0..n)` は None を返すが、`s.trySlice(n..)` と `s.tryGet(^n)` は Index 化で Abort し、try API が呼ばれない。
- **P3 省略で系統が変わる**: `..3` は `IndexRange`、`0..3` は `Range<i32>` になる。
- **P4 整数型の扱いがそろわない**: `values[a..]` と `values[^a]` は任意の整数を受け取るが、`values[a]` は isize だけを受け取る。
- **P5 実行時の分岐と格納**: `isInclusive` と Index の方向で、解決と反復が実行時に分岐する。`Index` は 16 B、`IndexRange` は 40 B になる。
- **P6 等価性の落とし穴**: `(0..3) == (0..=2)` は黙って false になる。
- **P7 API の重複**: 範囲や位置を受け取る API ごとに、型ごとのオーバーロードが要る。
- **P8 受け手の制限**: 保存した範囲値と Index による添字は、パス形の受け手でしか使えない。これは仕様にない実装上の制限である。

## 2. Proposed Specification — 新しい仕様

### 2.1. 概念

| 概念 | 型 | 意味 |
| --- | --- | --- |
| 位置 | `Position` を満たす型（整数、`FromEnd<T>`） | 長さ L の対象での境界。`[0, L]` に解決する |
| 要素位置 | 同上 | 解決した位置 q が `q < L` を満たすもの。1つの要素を指す |
| 範囲 | `PositionRange` を満たす型 | 位置の区間。`ResolvedRange` に解決する |
| 解決済み区間 | `ResolvedRange` | 検証済みの半開区間 `[start, end)`（`0 <= start <= end`） |

### 2.2. 共通規則

1. **型は構文で決まる。** 範囲の形は構文で決まり、型引数は境界の型で決まる。リテラル式の境界だけは、§2.4.2 の規則で型に適合する。
2. **構築は検査しない。** `^x` と範囲構文は、値を保存するだけである。失敗しうるのは、境界式そのものの評価（`^(a - b)` の溢れなど）だけである。
3. **検査は使う時点で行う。** 添字と API は、そのときの長さ L に対して解決して検査する。反復は、入口で順序を検査する。Abort する操作と try 操作の違いは、検査が失敗したときの扱いだけである。
4. **方向と区間の形は型で表す。** Kimi の位置と範囲の型は、境界以外の状態を持たない。
5. **位置は値で、範囲は共有借用で受け取る。**
   - 位置は、長さ・容量・個数と同じく値で受け取る（`P is Position`）。整数の参照は Scalar read で読む（§2.3.3）。
   - 範囲は、開いた Contract なので、Indexable のキーと同じく共有借用で受け取る（`R is PositionRange`）。

### 2.3. 位置

#### 2.3.1. `Position`

`Kimi.Position` は、閉じた内在 Contract である。満たす型は、12 の整数型（`PrimitiveInteger`）と `FromEnd<T>` だけである。ユーザーの型も、他の Kimi の型も適合できない。

```kimi
contract Position
    func tryResolve(self, length: isize) -> Option<isize>
```

- **含意**: `T is PrimitiveInteger` は `T is Position` を含意する。`T is Position` は、Copy・Owned・Equatable を含意する。
- **解決**: `L < 0` なら失敗する。それ以外は、次のとおりとする。
  - 整数 n は、`0 <= n <= L` なら n に解決する。
  - `FromEnd<T>` の offset は、`0 <= offset <= L` なら `L - offset` に解決する。
- **整数の実装**: §22.1 が指定する Kimi の内部関数とし、コンパイラーが宣言IDで結び付ける。整数型にメンバーは追加せず、この要件は `P is Position` を通じて呼ぶ。

#### 2.3.2. `FromEnd<T>`

```kimi
public struct FromEnd<T>
    T is PrimitiveInteger
    Self is Copy
    Self is Equatable
    Self is Position          // §22.1 が指定する適合。
    public let offset: T
```

- **作り方**: `^x` だけが作る。x は任意の `PrimitiveInteger` の値とする。
- **意味**: `^0` は終端の境界、`^1` は最後の要素を指す。
- **公開メンバー**: `offset`・`tryResolve`・`resolve`。
- **等価性**: offset で決める。`^0` と `0` は型が違うので、比較できない。

#### 2.3.3. 位置の受け渡し

位置を要求するところ（要素添字、位置の API の引数、`^x` の被演算子、範囲の境界）では、位置を値として読む。借用は残さない。

- **整数の参照**: 安全な参照の層で終わる整数は、Scalar read で読む（§3.5.3）。
- **ジェネリックな引数**: 制約から `P is Position` が導ける型引数も、これに含める。実引数が整数で終わる参照なら、P は終端の整数型に束縛し、Scalar read で読む（§10.2.1 の例外）。参照型は Position を満たさないので、この束縛のほかに候補はない。
- **`FromEnd<T>` の参照**: Scalar ではないので、`@follow` と書く（§3.5.3 の明示の規則と同じ）。

```kimi
// indexes: Array<isize>、picks: Array<isize>
indexes[indexes[0]] = x    // 組み込みの添字。キーは値として読む。
indexes.remove(indexes[0]) // API も同じ。受け手と借用は重ならない。
for i in picks             // i: ref/isize
    indexes.remove(i)      // P は isize。Scalar read で読む。
```

### 2.4. 範囲

#### 2.4.1. 構文と型

| 構文 | 型 | 格納（公開・読み取り専用） |
| --- | --- | --- |
| `a..b` | `Range<S, E>` | `start: S`・`end: E` |
| `a..=b` | `ClosedRange<S, E>` | `start: S`・`end: E` |
| `a..` | `RangeFrom<S>` | `start: S` |
| `..b` | `RangeTo<E>` | `end: E` |
| `..=b` | `RangeThrough<E>` | `end: E` |
| `..` | `RangeFull` | なし |

- **能力**: 6つの型は、すべて Owned・Copy・Equatable で、`PositionRange` を満たす。
- **作り方**: 範囲構文だけで作る。
- **公開メンバー**: 格納、`tryResolve`・`resolve`、反復の入口（§2.7）。
- **等価性**: 同じ型の境界どうしで決める。形の違う範囲は型が違うので、比較できない。
- **構文**: `a..=` は構文エラーとする。演算子の優先順位は変えない。

#### 2.4.2. 境界の型

- **境界**: `Position` を満たす型とし、§2.3.3 のとおり値として読む。
- **両端の型**: 独立に決まり、同じでなくてもよい。整数型の一致は、反復の条件としてだけ求める（§2.7）。
- **リテラル式**: `^a` は、a がリテラル式ならリテラル式とする。リテラル式の境界の型は、次の順で最初に当てはまるもので決める。型が合わなくても、次の規則で再試行しない。
  1. 範囲全体がリテラル式で期待型があれば、その S・E を各境界に渡す。`FromEnd<T>` なら T を被演算子に渡す。
  2. 他方の境界の型が独立に確定していれば、その整数型（`FromEnd<T>` なら T）に合わせる。
  3. どちらもなければ、通常の整数リテラルと同じ i32 とする。位置専用の既定は設けない（`0..10`・`^1`・`values[3]` も i32）。
- **大きなリテラル**: i32 に収まらないリテラルの位置には、`values[3_000_000_000@isize]` のように型を明示する。

```kimi
let a = 1..^1         // Range<i32, FromEnd<i32>>
let b = 0@i64..^2     // Range<i64, FromEnd<i64>>
let c = 0@i32..2@i64  // Range<i32, i64>。解決できるが、反復できない。
let d: Range<u8, u16> = 0..300 // 期待型を各境界に渡す。
let e = ..            // RangeFull
```

### 2.5. 解決

#### 2.5.1. `PositionRange` と `ResolvedRange`

```kimi
contract PositionRange
    func tryResolve(self, length: isize) -> Option<ResolvedRange>
```

- **適合**: 6つの範囲型と `ResolvedRange` が適合する。ユーザーの型（Non-Copy の型を含む）も適合できる。
- **実装者の約束**: `Some` の結果は、渡された長さに収まる（`end <= L`）。
- **利用する側の保証**: 範囲を受け取る操作は、次を守る。
  - **呼び出し**: 操作ごとに `tryResolve` を、ちょうど1回呼ぶ。
  - **防御**: 適用の前に `end <= L` を検査し（`ResolvedRange` の適用）、失敗は解決の失敗として扱う。`ResolvedRange` は Kimi しか作れないので、どんな実装に対しても安全性が保たれる。
  - **除去**: Kimi の型では、この検査は常に成り立ち、解決には効果がない。そのため、呼び出しと検査を融合・除去してよい。
- **ユーザー実装の責任**: ユーザー実装自身の Abort・効果・計算量は、その実装の責任とする。Kimi の型の保証（§4.6.8）には含まれない。
- **`ResolvedRange`**: 公開メンバーは `start`・`end`・`length`・`isEmpty`、`tryResolve`・`resolve`、反復の入口とする。作るのは、`indices` と各型の `tryResolve`・`resolve` だけである。

#### 2.5.2. 解決の規則

| 型 | 開始 s | 終了 | 検査 | 結果 |
| --- | --- | --- | --- | --- |
| `Range<S, E>` | start | 境界 e | `s <= e` | `[s, e)` |
| `ClosedRange<S, E>` | start | 要素位置 q | `s <= q` | `[s, q + 1)` |
| `RangeFrom<S>` | start | ― | ― | `[s, L)` |
| `RangeTo<E>` | 0 | 境界 e | ― | `[0, e)` |
| `RangeThrough<E>` | 0 | 要素位置 q | ― | `[0, q + 1)` |
| `RangeFull` | 0 | ― | ― | `[0, L)` |
| `ResolvedRange` | ― | ― | `end <= L` | 自身 |

- **失敗**: `L < 0` か、位置の解決が1つでも失敗すれば、範囲の解決も失敗する。
- **順序**: 解決した境界どうしで、1回だけ比べる。逆転を空区間にはしない。
- **溢れ**: `q < L <= isize.max` なので、`q + 1` は溢れない。

```kimi
(1..^1).tryResolve(5)   // Some([1, 4))
(1..=3).tryResolve(5)   // Some([1, 4))
(2..=1).tryResolve(5)   // None: 逆転している。
(..=^0).tryResolve(5)   // None: ^0 は要素位置ではない。
(^7..).tryResolve(5)    // None: 7 > 5。
```

#### 2.5.3. Abort する操作

- **Contract の要件**: `tryResolve` だけとする。
- **Abort 版の定義**: Abort する操作（添字、`insert`・`remove` など）と `resolve` は、一律に「同じ解決が None なら Abort する」と定める。
- **`resolve` の公開**: Kimi の7つの範囲型と `FromEnd<T>` は、命名の対として `resolve` を公開する。

### 2.6. 添字と API

#### 2.6.1. 列の添字

固定配列・Array・Slice の添字 `x[k]` は、組み込みの規則で決める。範囲添字と同じく、Indexable の外の規則とする。その他の型の添字は、現行どおり Indexable で決める。

| k の型 | 結果 |
| --- | --- |
| `P is Position` | 要素位置 q の要素の Place。読み取りと更新の権限は、現行の §4.6.1 のとおり |
| `R is PositionRange` | 解決した区間の Slice（`ResolvedRange` の適用を基底とする） |

- **キー**: 位置は §2.3.3 のとおり値として読む。範囲は `ref/K` の引数と同じ規則で K を決め、共有借用する（§10.2.1）。
- **Indexable との関係**: 選ばれる要素は、`Indexable<isize>` の実装と一致する。
  - 列は、ジェネリックなコードのために `Indexable<isize>`（Array・固定配列は `UniqIndexable<isize>`）に適合する。
  - `Indexable<Index>` への適合は削除する。
- **受け手**:
  - 受け手は1回だけ評価する。既存の Place はその場所をそのまま使い、所有の一時値だけを一時 Place に実体化する。
  - 受け手の形は問わず、保存した範囲値でも同じとする。
  - 借用・アクセス予約と、連鎖添字で中間配列をコピーしないことは、現行の §4.6.4 に従う。
- **静的 Move Path**: 現行どおり、整数リテラルの添字だけとする。
- **raw pointer**: 添字は isize の値だけとし、`FromEnd<T>` と範囲は受け付けない。

#### 2.6.2. 位置と範囲を受け取る API

位置と範囲を受け取る API は、ジェネリック1本とする。引数ラベルは現行のままとする。

| 受け取るもの | API |
| --- | --- |
| 要素位置（`q < L`） | 要素添字、`tryGet`・`tryGetUniq`・`remove`・`swapRemove`・`swap` |
| 境界（`p <= L`） | `insert`・`splitAt`・`trySplitAt` |
| 範囲 | 範囲添字、`trySlice` |
| isize の値のまま | `truncate(length:)`・`init(capacity:)`・`init(repeating:count:)`、戻り値（`firstIndex`・`indices`） |

```kimi
// Slice<T>{source} 内の宣言。
public func tryGet<P>(self: Self, index: P) -> Option<ref/T during source>
    P is Position
public func trySlice<R>(self: Self, range: ref/R) -> Option<Slice<T> during source>
    R is PositionRange
```

```kimi
var list: Array<i32> = [1, 2, 3]
list.insert(^0, 4)       // 末尾に追加する。[1, 2, 3, 4]
list.swap(0, ^1)         // [4, 2, 3, 1]
let x = list.remove(^2)  // 3
let n: u8 = 1
let y = list[n]          // 2。要素添字も任意の整数型を受け付ける。
```

### 2.7. 反復

| 型 | 反復の条件 | Iterator |
| --- | --- | --- |
| `Range<S, E>`・`ClosedRange<S, E>` | `S is PrimitiveInteger` かつ `E is S` | `RangeIterator<S>` |
| `ResolvedRange` | なし | `RangeIterator<isize>` |
| その他の範囲型 | 反復できない | ― |

- **適合**: 条件付き適合（§8.4.8）で、`Iterable`・`UniqIterable`・`IntoIterable` に適合する。
- **入口**: `start > end` なら Abort する。`ResolvedRange` は常に `start <= end` である。
- **保証**: 次を保証し、内部表現は規定しない（§3.4）。
  - start から昇順・刻み1で生成する。
  - 終端が T の最大値でも、加算で溢れない。
  - None を返した後は、None を返し続ける。
- **`RangeIterator<T>`**: その他の性質（Non-Copy、公開コンストラクターなし）は現行どおりとする。

### 2.8. 総合例

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]

let inner = 1..^1          // Range<i32, FromEnd<i32>>
let middle = values[inner] // 20, 30, 40
let last = values[^1]      // 50
let n: i64 = 3
let head = values[..n]     // RangeTo<i64>。i64 のまま解決する。

for i in 0..values.length  // Range<isize, isize>
    Console.writeLine("\(values[i])")
for x in 0..=255@u8        // ClosedRange<u8, u8>。255 の後で加算しない。
    ()
for i in inner.resolve(values.length) // ResolvedRange。1, 2, 3
    ()
for i in inner             // エラー: FromEnd を含む範囲は反復できない。
    ()

let picks: Array<isize> = [0, 2]
for i in picks             // i: ref/isize
    let v = values[..].tryGet(i) // P は isize。Scalar read で読む。

func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values    // T is T が成り立つので反復できる。
        total += value
    return total
```

失敗の例（n は負の isize、s は Slice）:

| 操作 | 結果 |
| --- | --- |
| `let p = ^n`、`let r = n..` | 成功する（検査しない） |
| `values[n..]`・`values[^n..]`・`values[^n]`・`values[2..=1]` | 解決で Abort |
| `s.trySlice(n..)`・`s.trySlice(^n..)`・`s.tryGet(^n)`・`s.trySlice(2..=1)` | None |

### 2.9. 対象外

- 降順・刻み・無限の範囲（`RangeFrom` の反復を含む）
- 実行時に方向を選ぶ位置の型
- 範囲値の `Utf8Format`、`contains` などの範囲演算
- Contract の既定実装、範囲の実行時 View
- 文字列の添字

## 3. Changes — 変更点

### 3.1. 変更一覧

| 項目 | 現行 | 変更後 |
| --- | --- | --- |
| 位置の型 | `isize`・`Index` | `Position`（整数・`FromEnd<T>`） |
| `^x` | Index 化（負数で Abort） | `FromEnd<T>`（検査しない） |
| 範囲の型 | `Range<T>`・`IndexRange` | 6つの形の型 |
| 閉区間 | `isInclusive` | `ClosedRange`・`RangeThrough` |
| 両端の整数型 | 同じ T（Index なら Index 化） | 独立。反復するときだけ一致を求める |
| 位置・範囲の引数 | 型ごとのオーバーロード。範囲は値で渡す | ジェネリック1本。位置は値 `P`、範囲は共有借用 `ref/R` |
| 要素添字・位置 API の整数型 | isize だけ | 任意の整数型 |
| 位置のリテラルの既定 | 範囲は i32、`^1` と要素添字は isize | すべて i32 |
| `trySlice(n..)`・`tryGet(^n)`（n < 0） | Abort | None |
| `(0..3) == (0..=2)` | false | 型エラー |
| 保存した範囲値の添字 | パス形の受け手だけ | 受け手の形を問わない |

### 3.2. 言語機構の変更

1. **型同一性の要件**（§8.3・§8.4.8.1・§8.7）: `X is U`（U は型）を一般規則として定め、条件付き適合の条件にも使えるようにする。
   - 正規化して同一（同じ束縛子を含む）なら Proven とする。
   - 具体型どうしで異なれば Refuted とする。
   - それ以外は Unknown とする。
   - 前提として得た同一性は、その有効範囲（条件付き適合のブロック本体を含む）で置換に使える。
2. **閉じた内在 Contract `Position`**（§8.4.7）: 適合は、12 の整数の組み込み適合と、§22.1 が指定する `FromEnd<T>` だけとする。§8.4.7.3 の PrimitiveInteger が与える能力に、Position を加える。
3. **リテラル式**（§12.3.1）: `^a` と片側の範囲を加える。既定は通常の整数リテラルと同じ i32 とし、「Index 化では isize」という既定を削除する。
4. **位置の Scalar read**（§3.5.3・§10.2.1）: 制約から `P is Position` が導ける型引数は、整数で終わる参照の実引数から終端の整数型を推論し、Scalar read で読む（§2.3.3）。

範囲の共有借用には、新しい規則を加えない。既存の §10.2・§10.2.1 で扱う。

### 3.3. 移行

| 現行 | 変更後 |
| --- | --- |
| `Index.init(n)` | `n` |
| `Index.init(n, fromEnd: true)`、`^n` | `^n`（`FromEnd<T>`） |
| 実行時に方向を選ぶ `Index` | 利用側で分岐するか、enum にする |
| `let r: Range<i32> = 0..3` | `let r: Range<i32, i32> = 0..3` |
| `IndexRange` や `Range<T>` を受け取る引数 | 関数が必要とする能力で選ぶ（§3.4 の診断） |
| `Indexable<Index>` を使う汎用コード | `Indexable<isize>` を使うか、具体型にする |
| i32 に収まらないリテラルの位置 | `3_000_000_000@isize` のように型を明示する |

### 3.4. 実装方針

- **ライブラリ**（Core.kimi に Kimi で実装する）:
  - `FromEnd<T>`・6つの範囲型・2つの Contract・反復の入口を実装する。
  - 解決は、3つの内部部品の組み合わせで書く。境界の解決、要素位置の解決、順序の検査である。
    - `L >= 0` は解決の入口で1回だけ検査し、その前提を部品に渡す。
    - 幅の違う整数は、切り詰める前に上限を検査する。
  - ジェネリックな API は、解決だけを行う薄い入口にする。本体（要素の移動・Slice の生成）は、isize・`ResolvedRange` を受け取る共通の処理に任せ、型ごとに具体化されるコードを抑える。
  - 整数の `Position` の実装は、現行の `Index.tryPosition<I>` を内部 group へ移したものとする。
  - `RangeIterator` の内部表現は、測定で選ぶ。候補は、閉区間を「終端が T の最大値でなければ `[s, e + 1)` の状態で始め、最大値のときだけ終端のフラグを使う」表現である。
- **コンパイラー**:
  - 範囲構文は、形と境界の型から宣言IDで型を決める。Index 化の処理は削除する。
  - 範囲構文の直接選択は、ライブラリと同じ内部部品を呼ぶ形に下げる。検査を二重に実装せず、O2 ではインライン化する。
  - 保存した値による添字も、§2.6.1 の受け手の規則で長さの取得と解決を共通化し、P8 の制限を外す。
- **診断**（`Fix` の文）: 自動の修復候補は、変更後の本体が検証できる場合だけ示す。§23 の構造化した修復候補を実装するときにも、この定義を使う。
  - **反復できない範囲**: `FromEnd` を含むなら `r.resolve(values.length)` を、整数型が違うなら明示変換を示す。
  - **範囲の型の不一致**（`Range<T, T>` の引数に `ClosedRange` を渡すなど）: 関数が必要とする能力に合わせて示す。
    - Slice に適用するなら、`R is PositionRange`。
    - 列挙するなら、反復の Contract（入口と Item の制約を含む）。
    - 境界を使うなら、具体的な範囲型。
  - **`FromEnd<T>` の参照を位置に渡した**: `@follow` を示す。
  - **常に失敗する位置**（要素位置や閉じた終端に書いた `^0`）: 警告を出す。

### 3.5. 修正が必要な箇所

| 区分 | 対象 | 内容 |
| --- | --- | --- |
| ライブラリ | `Core.kimi` | `Index`・`IndexRange`・`Range<T>` を削除する。`FromEnd<T>`・6つの範囲型・2つの Contract・内部部品・反復の入口を追加する |
| | `Slice.kimi`・`Array.kimi` | §2.6.2 の API を、ジェネリックな薄い入口と共通の本体に分ける |
| 宣言 | `KimiDeclaration.cs`・`KimiLibrary.cs`・`KimiLibraryCatalog.cs`・`KimiLibraryRecords.cs`・`KimiLibraryValidation.cs` | 宣言ID、整数の組み込み適合の結び付け、署名の検査 |
| 構文 | `Koto.cs`（`FromEndIndex`）・`KotoHelper.cs`・`UnaryKoto.cs`・`RangeKoto.cs` | `^x` と範囲の形 |
| 束縛・推論 | `Binding.Ranges.cs`・`Binding.Expressions.cs`・`Binding.Elements.cs`・`Binding.Sequences.cs`・`ElementAccess.cs`、制約の証明、条件付き適合 | 種別、リテラル式、位置の Scalar read（推論を含む）、範囲キーの共有借用、位置添字、反復、型同一性、Position |
| 所有権・生成 | `OwnershipAnalysis*.cs`・`OwnershipModel.cs`・`BodyLowering.*.cs`・`ControlFlowAnalysis.cs`・`LlvmModuleWriter.Sequences.cs`・`ReferenceTypes.cs` | `FromEnd` 操作と Index のレイアウト依存を削除する。直接選択の部品化、受け手の制限の撤廃、`ArrayInsertIndex`・`ArrayRemoveIndex` の更新 |
| テスト | `IntegerRangeTest`・`RangeValueTest`・`RangeIndexParseTest`・`IndexValueTest`・`ExpressionPrecedenceTest`・`KeyedIndexingTest`・`DynamicArrayIndexTest`・`SliceOperationsTest`・`SliceCostTest`・`ArrayMembersTest`・`DynamicArrayCostTest`・`CoreCatalogTest`・`CatalogSignatureValidationTest`・`ContractSignatureValidationTest`・`RecordLayoutValidationTest`、Milestone 27 など、ネイティブ fixture、`docs/examples/Ranges` | 型名・既定の型・意味を更新する。レイアウトが現れる fixture は全件を走査する |
| 文書 | `docs/LIBRARY.md`・`docs/STYLE.md`・`docs/STATUS.md` | 公開宣言、API 規約（§2.2 の規則5）、対応範囲 |

### 3.6. 実装上の注意

1. **要件を持つ閉じた内在 Contract は前例がない**（`PrimitiveInteger` は要件を持たない）。
   - 対策: 整数の実装を宣言IDで内部関数に結び付け、`KimiLibraryValidation` で署名を検査する。§8.4.5 の照合は使わない。
2. **型同一性の要件の証明と置換**: 条件付き適合の本体で、E を S として型検査する必要がある。
   - 対策: §8.4.3 の関連型の同一性の事実と同じ仕組みで扱う。
3. **列の添字と Indexable の選択**（`bf6c09cd`）の関係。
   - 対策: 列には組み込みの規則を先に適用し、それ以外は現行の選択に任せる。
4. **受け手の誤った統一**: 次の2つの実装は誤りである。§2.6.1 のとおり、場所を1回だけ確定する。
   - 受け手を一時変数へ値で退避する。大きな配列のコピーや Non-Copy の拒否が起き、元と異なる場所にアクセスすることになる。
   - 受け手全体を借用して統一する。Partial Move の後に残った要素へ、直接アクセスできなくなる。
5. **名前の衝突**: コンパイラー内部の `FromEnd` 操作と、構文の `FromEndIndex` がある。
   - 対策: 削除するか、改名する。
6. **編集時の再束縛**: 範囲の形が編集で変わったとき、合成呼び出しの宣言が切り替わることをテストで確かめる。

## 4. Rationale — 変更する理由

### 4.1. 目的と効果

目的は、範囲と位置の意味を型と1つの解決規則で説明し、失敗の段階をそろえ、格納と実行時の分岐を最小にすることである。§1.2 の問題は、次のように解決する。

| 問題 | 解決の仕方 |
| --- | --- |
| P1 | 種別の表、Index 化、包含終端 `^0` の禁止が、「構文→型」と1つの解決表に置き換わる |
| P2 | 構築は検査しないので、失敗はすべて使う時点で起きる |
| P3・P4 | 位置を受け取るところは、すべて同じ `Position` を受け付ける |
| P5 | 位置と範囲の解決に、方向と形の実行時分岐がなくなる |
| P6 | 形の違う範囲の比較は、型エラーになる |
| P7 | ジェネリック1本になる |
| P8 | 受け手の形を問わない、共通の下げ方になる |

### 4.2. 性能

格納サイズ（境界が i32 のとき、windows-x64）:

| 値 | 現行 | 変更後 |
| --- | --- | --- |
| `^1` | 16 B | 4 B |
| `1..^1` | 40 B | 8 B |
| `0..10` | 12 B | 8 B |
| `n..` | 40 B | 4 B |
| `..` | 40 B | フィールドなし |

- **比較**: `L >= 0` を入口で確かめた後は、同じ幅の `0 <= n <= L` を、生成コード上の符号なし比較1回にできる。言語の変換 `n@usize` は負数で Abort するので、この目的には使わない。
- **幅の変換**: i32 の位置は、isize の長さと比べる前に幅の変換が入る。定数は畳み込む。
- **引数の渡し方**:
  - 位置は値で渡すので、間接参照がない。
  - 範囲の共有借用は、Kimi の型なら、薄い入口のインライン化で一時領域を除去できる見込みである。除去しても、借用・評価順・呼び出しの効果は保つ。
  - ユーザー定義の `PositionRange` の `tryResolve` 呼び出しは、除去しない。
  - いずれも §6.3 で確かめる。
- **検査の除去**: 反復変数の拡幅と境界検査の除去は、非負性・上限・長さの不変が証明できる場合だけ行う（§4.6.8）。範囲の反復変数は、負にもなりうる（`-3..3`）。

### 4.3. 検討した代替案

| 案 | 不採用の理由 |
| --- | --- |
| 「半開区間への統一と ClosedRange」（2026-09-29） | `..=b` と、Index を含む閉区間が書けない。P2・P3・P4・P7 が残る |
| 同じ型引数の `Range<B>` と、実行時の方向を持つ Index | 混在形 `1..^1` で Index 化が要り、構築時の失敗（P2）と方向の分岐が残る |
| Contract の既定実装 | 要件を `tryResolve` だけにすれば不要になる。範囲のためだけに言語規則を増やさない |
| `Position` をユーザーに開く | 解決の結果を信頼できなくなり、範囲の解決で再検査が要る。整数の適合は、どのみち組み込みになる |
| 要素添字は isize だけのままにする | 範囲の境界と要素添字で受け付ける型が違う、という例外（P4）が残る |
| 整数型の違う両端を、構築時に禁止する | ジェネリックな境界（`P`・`Q is Position`）を通すと防げない。防ぐには、抽象型にも証明できる型形成の制約が要り、概念が増える |
| 位置も共有借用で受け取る | 借用が呼び出しの間ずっと残るので、受け手の中の位置（`indexes.remove(indexes[0])`）を渡せなくなる（§15.6.7）。組み込みの添字とも挙動が分かれる |
| 引数の借用を早く終える規則（呼び出し先が先に読み終える場合に重なりを許す） | 引数を読む時期という新しい概念が要る。値で受け取れば、同じ効果が既存の規則で得られる |
| 参照から値を読む規則を `FromEnd<T>` にも広げる | Scalar でない Copy 型は明示の `@follow` で読む、という §3.5.3 の規則に例外を作る。整数だけで、実用上の場合は足りる |
| 位置と範囲のリテラルの既定を isize にする | 通常の整数リテラルとは別の既定が要り、規則が1つ増える。`for x in 0..10` の型も現行から変わる |
| 型を明示した整数リテラルを、静的添字（§15.1.3）に加える | 効果があるのは、要素数 2^31 以上の固定配列だけである。構文の規則を増やす理由にならない |

### 4.4. 副作用

- **形の違う範囲に共通の型がない**: 分岐・配列・フィールドで混在できない。保存には `ResolvedRange`、受け渡しにはジェネリック、混在には利用側の enum を使う。
- **型の表記が長い**: 整数範囲は `Range<i32, i32>` と書く。
- **実行時に方向を選ぶ位置の型がない**: 利用側で分岐する。
- **失敗の時点が遅れる**: `^n` と `n..` は構築では失敗せず、使う時点で失敗する。
- **反復できない範囲がある**: `FromEnd` を含む範囲、両端の整数型が違う範囲、片側の範囲は反復できない。
- **大きなリテラルの位置**: i32 に収まらないリテラルの位置には、型の明示が要る。その定数添字は静的 Move Path にならないが、該当するのは要素数 2^31 以上の固定配列だけである。
- **`FromEnd<T>` の参照**: 位置として渡すには `@follow` が要る（§2.3.3）。整数の参照は、現行どおり暗黙に読める。
- **引数の型の広がり**: 位置の引数が Position に広がるので、`swap(^1, 0)` なども受け付ける。

## 5. Impact on SPEC.md — SPEC.md への影響

| 箇所 | 変更 |
| --- | --- |
| `docs/SPEC.md` の宣言索引、§3 の Copy の表 | `Index`・`IndexRange`・`Range<T>` を削除する。`FromEnd<T>`・6つの範囲型・`Position`・`PositionRange` を追加する |
| §3.5.3・§10.2.1 | 位置の Scalar read と、その型引数の推論 |
| §4.6.1 | 型の表と、添字が受け付ける型を改める。長さの情報と要素 Place の規則は維持する |
| §4.6.2・§4.6.3 | 本書 §2.3・§2.4・§2.7 で置き換える |
| §4.6.4 | 解決と失敗の記述を本書 §2.5 で置き換え、Index 化の記述を削除する。評価順・書き込み・Abort の規則は維持する |
| §4.6.6・§4.7.2 | 位置と範囲の API（本書 §2.6.2） |
| §4.6.8 | 表現と性能の文言 |
| §4.6.9・§10.6 | 列の添字を組み込みの規則とする（位置は値、範囲は共有借用）。列の Indexable への適合は isize だけにする |
| §8.3・§8.4.8.1・§8.7 | 型同一性の要件 |
| §8.4 冒頭・§8.4.7・§8.4.7.3 | `Indexable<Index>` の例、`Position`、PrimitiveInteger による含意、`sum` の例 |
| §12.3.1 | リテラル式 |
| §13.2 | 接頭辞 `^` |
| §14.6.2 | 反復の Subject |
| §15.1.3 | ConstantIndexExpression の表 |
| §17.3.1 | 失敗の例 |
| §22.1 | 必須宣言の表（整数の Position の実装を含む） |
| 付録 D・E・F | 対象外の項目、用語、文法 |
| 実装仕様 §21.5.3・付録 A.13 | 検査の生成、試験要件 |

## 6. Decision — 最終決定

### 6.1. 決定

- §2 の仕様と §3 の変更を採用する。
- 「半開区間への統一と ClosedRange」（2026-09-29）は取り込まない。
- 正式仕様は本書を参照せず、自己完結させる。取り込み後は `draft/INTEGRATED.md` に対象節とコミットを記録し、本書を固定する。

### 6.2. 実装順序

1. **型同一性の要件**を、一般規則として実装する。
2. **`Position`・`FromEnd<T>`・`PositionRange` を追加する**。位置と範囲の API をジェネリックな入口にし（位置は値、範囲は共有借用）、位置の Scalar read を実装する。
   - 途中段階に限り、Kimi は `Index` を Position に、既存の範囲型を PositionRange に適合させる（仕様の規則ではない）。
3. **構文を切り替える**。`^x` を FromEnd に、範囲を6つの形の型に切り替え、リテラル式と反復を実装する。`Index`・`IndexRange`・`Range<T>` を削除する。
4. **下げ方を共通化する**。直接選択を部品化し、受け手の制限を撤廃する。

各単位は、`./scripts/verify.ps1 -Class ...` と、関連する O0・O2 の fixture・Milestone で検証してからコミットする。セッションの最後に `./scripts/verify.ps1 -Mode Session` を実行する。

### 6.3. 検証

- **正しさ**:
  - **位置**: 幅と符号の違う全整数型と `FromEnd` を解決する。値は 0・L・L + 1（表現できる場合）・負数・型の最大値、長さは 0 と isize の最大値とする。
  - **範囲**: 形ごとの解決結果、逆転（`2..=1` を含む）、負の長さ（`RangeFull` を含む）。
  - **反復**: 最大値を含む閉区間の列挙と、終了後の `next`。FromEnd を含む範囲と、整数型が違う範囲を拒否すること。型同一性による反復。
  - **型**: リテラル式の適合の順序（`let d: Range<u8, u16> = 0..300` を含む）、既定の i32、i32 に収まらないリテラルの拒否。
  - **受け渡し**:
    - 値・一時値・`ref`・`uniq` から渡す位置と範囲。ジェネリック経由の異なる境界型。
    - ユーザー定義の `PositionRange`: `tryResolve` を操作ごとにちょうど1回呼ぶこと。対象より長い `ResolvedRange` を返す場合を含む。
  - **借用**: `values[values[0]] = x` と `values.remove(values[0])` が書けること。`ref/(ref/isize)` の位置を渡せること。`ref/FromEnd<T>` は `@follow` なしでは拒否し、診断が出ること。
  - **失敗**: try API は None を返し、Abort 版は Abort すること。
  - **添字**: 受け手の副作用が1回だけ起きること。Partial Move の後の固定配列、静的 Move Path、連鎖添字。
- **性能**: 変更前後を比べ、最小値の悪化が 3% 以内であること（既存の基準）。中央値が 3% を超えて悪化したときや、差がばらつきと同程度のときは、追加で測定して原因を分析する。
  - **記録するもの**: 中央値とばらつき、O0 の時間、O2 の生成コードとコード量、格納サイズ。
  - **測り方**: 次の条件を分けて測る。
    - 長さ: 空、短い、通常の大きさ。
    - 境界の整数型の幅。
    - ループで直接使う場合と、Iterator や範囲を返す・保存する場合。
  - **対象**:
    - `values[a..b]`・`values[1..^1]`・`s.trySlice(r)`
    - `values[^1]`・`values.insert(^1, x)`
    - `for i in 0..n`・`for i in 0..=n`
    - i32 の反復変数による `values[i]`
