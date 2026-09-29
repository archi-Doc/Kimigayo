# 仕様変更案：範囲と位置の再設計 B

日付: 2026-09-29

状態: 取り込み済み（2026-09-29、`draft/INTEGRATED.md`）。統合前の見直しで見つかった誤りと漏れ（§2.3.1・§2.3.3・§2.4.1・§2.5.1・§2.6.2・§3.2・§3.4・§3.6・§5・§6.2）は `8afde300` で修正した。

本書で変更する事項は、`docs/SPEC.md` とその参照先より優先する。本書で変更しない事項には既存仕様を適用する。

- **基準**: 「整数範囲と位置範囲」（2026-09-28）を取り込んだ後の SPEC。
- **関係**: 「半開区間への統一と ClosedRange」（2026-09-29）の代替案である。本書を採用する場合、同書は取り込まない。

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
- **引数**: 位置は `isize` と `Index` のオーバーロードで受け取る。範囲添字は3つの範囲型を値で受け取る。`trySlice`・`splitAt` は Slice にだけあり、固定配列には読み取り API がない。

### 1.2. 問題点

- **P1 規則が多い**: 種別の決定表、Index 化の規則と評価順、包含終端 `^0` の禁止など、範囲の形ごとに個別の規則がある。
- **P2 失敗の段階がそろわない**: `s.trySlice(0..n)` は None を返すが、`s.trySlice(n..)` と `s.tryGet(^n)` は Index 化で Abort し、try API が呼ばれない。
- **P3 省略で系統が変わる**: `..3` は `IndexRange`、`0..3` は `Range<i32>` になる。
- **P4 整数型の扱いがそろわない**: `values[a..]` と `values[^a]` は任意の整数を受け取るが、`values[a]` は isize だけを受け取る。
- **P5 実行時の分岐と格納**: `isInclusive` と Index の方向で、解決と反復が実行時に分岐する。`Index` は 16 B、`IndexRange` は 40 B になる。
- **P6 等価性の落とし穴**: `(0..3) == (0..=2)` は黙って false になる。
- **P7 API の重複とばらつき**: 位置や範囲を受け取る API ごとに、型ごとのオーバーロードが要る。読み取り API の提供先も、列の型によって違う。
- **P8 受け手の制限**: 保存した範囲値と Index による添字は、パス形の受け手でしか使えない。これは仕様にない実装上の制限である。

## 2. Proposed Specification — 新しい仕様

### 2.1. 概念

| 概念 | 型 | 意味 |
| --- | --- | --- |
| 位置 | `Position` を満たす型（整数・`FromEnd<T>`・`Start`・`End`） | 長さ L の対象での境界。`[0, L]` に解決する |
| 要素位置 | 同上 | 解決した位置 q が `q < L` を満たすもの。1つの要素を指す |
| 範囲 | `PositionRange` を満たす型（`Range`・`ClosedRange`・`ResolvedRange`） | 位置の区間。`ResolvedRange` に解決する |
| 解決済み区間 | `ResolvedRange` | 検証済みの半開区間 `[start, end)`（`0 <= start <= end`） |

### 2.2. 共通規則

1. **型は構文で決まる。**
   - 区間の形（半開・閉）は構文で、型引数は境界の型で決まる。省略した境界は `Start`・`End` になる。
   - 範囲構文と `^` は、同名の利用者の型ではなく、常に Kimi の型を作る（現行どおり）。
2. **構築は検査しない。** `^x` と範囲構文は、値を保存するだけである。失敗しうるのは、境界式そのものの評価（`^(a - b)` の溢れなど）だけである。
3. **検査は使う時点で行う。**
   - 添字と API は、そのときの長さ L に対して解決して検査する。反復は、入口で順序を検査する。
   - Abort する操作と try 操作は、失敗の扱いだけが違う。
   - 範囲の値の意味は、保存・受け渡し・反復・添字のどこでも同じである（現行どおり）。
4. **位置と範囲は閉じた値型である。**
   - **型の集合**: §22.1 が指定する小さな Copy の値型だけである。
   - **状態**: 方向と区間の形を型で表し、境界以外の状態を持たない。Storage の Origin や Loan を保持せず、Comparable・算術・型の間の暗黙の変換も持たない（現行どおり）。
   - **受け渡し**: 位置と範囲の引数、および `tryResolve`・`resolve` の受け手は値とし、参照からは値を読む（§2.3.3）。

### 2.3. 位置

#### 2.3.1. `Position`

`Kimi.Position` は、**閉じた Contract** である。閉じた Contract は Kimi が宣言する通常の Contract で、適合する型を §22.1 が指定するものに限る。`Position` に適合する型は、12 の整数型（`PrimitiveInteger`）と、§22.1 が指定する `FromEnd<T>`・`Start`・`End` だけである。利用者の型は適合を宣言できない。

```kimi
public contract Position: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<isize>
```

- **含意**: `T is Position` は、Contract の継承（§8.4.2）で Equatable・Utf8Format を、Contract の制約で Copy・Owned を含意する。特別な証明規則は、「`T is PrimitiveInteger` は `T is Position` を含意する」の1つだけである。
- **整数の実装**: §22.1 が指定する Kimi の内部関数とし、コンパイラーが宣言IDで結び付ける（Equatable の組み込み適合と同じ経路）。整数型にメンバーは追加せず、この要件は `P is Position` を通じて呼ぶ。

#### 2.3.2. 位置の型と解決

`L < 0` なら、どの位置も解決に失敗する。`L >= 0` のときは次のとおりとする。

| 型 | 作り方 | 解決の条件 | 結果 |
| --- | --- | --- | --- |
| 整数 n | 整数の値 | `0 <= n <= L` | n |
| `FromEnd<T>` | `^x`（x は任意の `PrimitiveInteger`） | `0 <= offset <= L` | `L - offset` |
| `Start` | 範囲の開始の省略 | なし | 0 |
| `End` | 範囲の終端の省略 | なし | L |

```kimi
public struct FromEnd<T>
    T is PrimitiveInteger
    Self is Copy
    Self is Equatable
    Self is Position          // §22.1 が指定する適合。
    public let offset: T

public struct Start           // フィールドを持たない。End も同じ形。
    Self is Copy
    Self is Equatable
    Self is Position
```

- **意味**: `^0` と `End` は終端の境界を、`^1` は最後の要素を指す。
- **公開メンバー**: `FromEnd<T>` は `offset` を持つ。3型とも `tryResolve`・`resolve` を持ち、公開コンストラクターはない。
- **等価性**: `FromEnd<T>` は offset で決め、`Start`・`End` はそれぞれ常に等しい。`0`・`^0`・`End` は型が違うので、比較できない。
- **`^` の構文**（現行どおり）:
  - 接頭辞 `^` は、添字の外でも使える。中置の `^` は、整数の排他的論理和のままとする。
  - `^n + 1` は `(^n) + 1` と解釈され、`FromEnd` に加算がないので型エラーになる。距離の式は `^(n + 1)` と書く。

#### 2.3.3. 値の読み取り

- **読み取り型**: Scalar と、Position か PositionRange を満たす型を、**読み取り型**と呼ぶ。Scalar read（§3.5.3）は**値の読み取り**（value read）と改め、その対象を読み取り型に広げる。
  - 適用する場面は現行の Scalar read と同じで、共通適合の値の位置（引数・型注釈付き初期化・代入元・戻り値など）と、組み込み演算子の被演算子（範囲構文と `^` を含む）である。
  - Position か PositionRange が証明できるジェネリックな型も、対象になる。
  - 期待型のない `let p = t` は、現行どおり参照のまま束縛する。
  - 仕様で Scalar read の対象を「Scalar」と書いている箇所（§3.3.6・§3.4.1・§7.3・§10.2・§13.5.3・§14.9.1・§15.2・付録 E）は、個別の例外を書かず「読み取り型」に置き換える。
- **受け手**: §7.3 の受け手の表では、所有の受け手 `Self` を参照から暗黙に取れるのは、Scalar の `Self` を Scalar read で読む場合だけである。この行を読み取り型に広げる。`t.tryResolve(L)`（`t: ref/FromEnd<i32>`）では、参照経路の選択（§3.4.1）が参照先を選び、値の読み取りで `Self` を Copy する。
- **型引数の推論**（§10.2.1 の例外）: 制約から Position か PositionRange が導ける型引数は、参照の実引数から、参照の層を外した終端の型を推論する。
  - これらの Contract を満たすのは値型だけなので、ほかに候補はない。
  - `T is PrimitiveInteger` で制約した型引数と、列の添字のキー（§2.6.1）にも適用する。

```kimi
// indexes: Array<isize>、picks: Array<isize>、t: ref/FromEnd<i32>
indexes[indexes[0]] = x    // キーは値として読む。
indexes.remove(indexes[0]) // API も同じ。受け手と借用は重ならない。
for i in picks             // i: ref/isize
    indexes.remove(i)      // P は isize。
indexes.remove(t)          // P は FromEnd<i32>。
let p: FromEnd<i32> = t    // 型注釈付き初期化でも同じく読む。
```

### 2.4. 範囲

#### 2.4.1. 構文と型

| 構文 | 型 |
| --- | --- |
| `a..b` | `Range<S, E>` |
| `a..=b` | `ClosedRange<S, E>` |
| `a..` | `Range<S, End>` |
| `..b` | `Range<Start, E>` |
| `..=b` | `ClosedRange<Start, E>` |
| `..` | `Range<Start, End>` |

```kimi
public struct Range<S, E>     // ClosedRange<S, E> も同じ形。
    S is Position
    E is Position
    Self is Copy
    Self is Equatable
    Self is PositionRange
    public let start: S
    public let end: E
```

- **格納**: 公開・読み取り専用の `start: S`・`end: E` だけを持つ。`Start`・`End` は大きさを持たない。
- **能力と作り方**: Owned・Copy・Equatable で、`PositionRange` を満たす。範囲構文だけで作る。
- **公開メンバー**: `start`・`end`、`tryResolve`・`resolve`、反復の入口（§2.7）。
- **等価性**: 同じ型の境界どうしで決める。形の違う範囲は型が違うので、比較できない。
- **構文**: `a..=` は構文エラーとする。演算子の優先順位は変えない。

#### 2.4.2. 境界の型

- **境界**: `Position` を満たす型とする。リテラル式でない境界は独立に型を決め、期待型で推論し直さない。文脈に依存するジェネリックな呼び出しには、注釈が要る（現行どおり、§10.5）。
- **両端の型**: 境界ごとに決まり、同じでなくてもよい。整数型の一致は、反復の条件としてだけ求める（§2.7）。
- **リテラル式の境界**: §12.3.1 の伝播を境界ごとに行い、次の順で最初に当てはまるもので型を決める。型が合わなくても、次の規則で再試行しない。`^a` は、a がリテラル式ならリテラル式とする。
  1. **その境界の期待型**: 範囲の期待型の S か E。`FromEnd<T>` なら、T を `^` の被演算子に渡す。
  2. **他方の境界の整数型**: 型の確定した他方の境界の整数型（`FromEnd<T>` なら T）。
  3. **既定**: 通常の整数リテラルと同じ i32。位置専用の既定は設けない（`0..10`・`^1`・`values[3]` も i32）。
- **大きなリテラル**: i32 に収まらないリテラルの位置には、`values[3_000_000_000@isize]` のように型を明示する。

```kimi
let a = 1..^1         // Range<i32, FromEnd<i32>>
let b = 2..           // Range<i32, End>
let c = 0@i32..2@i64  // Range<i32, i64>。解決できるが、反復できない。
let n: i64 = 2
let d: Range<i64, u8> = n..10 // 10 は期待型から u8。
let e = ..            // Range<Start, End>
```

### 2.5. 解決

#### 2.5.1. `PositionRange`

`Kimi.PositionRange` は、`Position` と同じく閉じた Contract である。満たす型は、§22.1 が指定する `Range`・`ClosedRange`・`ResolvedRange` だけである。

```kimi
public contract PositionRange: Equatable, Utf8Format
    Self is Copy
    Self is Owned
    func tryResolve(self: Self, length: isize) -> Option<ResolvedRange>
```

- **含意**: `T is PositionRange` は、`Position` と同じ仕組みで Copy・Owned・Equatable・Utf8Format を含意する。
- **効果**: 解決は、境界と長さだけを読む。そのため、呼び出しと検査を融合・除去してよい。

#### 2.5.2. `ResolvedRange`

`ResolvedRange` は、`Range<isize, isize>` に不変条件 `0 <= start <= end` を加えた別の型である。

- **同じ意味**: 解決・反復・整形・等価性は、同じ境界の `Range<isize, isize>` と同じとする。
- **不変条件から従うこと**: 解決の検査は `end <= L` だけになり、反復の入口は順序を検査しない。
- **違い**: 両者の間に暗黙の変換はない。`ResolvedRange` だけが `length`・`isEmpty` を持ち、各型の `tryResolve`・`resolve` だけが作る。
- **`indices`**: 列の `x.indices` は、`(..).resolve(x.length)` と同じ意味である（取得時の長さのスナップショット）。
- **適用先**: 特定の対象を指さない数値の区間なので、十分な長さの別の対象にも適用できる。

#### 2.5.3. 解決の規則

s・e は start・end の位置の解決結果、q は end の要素位置とする。

| 型 | 検査 | 結果 |
| --- | --- | --- |
| `Range<S, E>` | `s <= e` | `[s, e)` |
| `ClosedRange<S, E>` | `s <= q` | `[s, q + 1)` |

- **失敗**: `L < 0` か、位置の解決が1つでも失敗すれば、範囲の解決も失敗する。
- **順序**: 解決した境界どうしで、1回だけ比べる。逆転を空区間にはしない。開始が `Start` か終了が `End` の半開区間では、この検査は常に成り立つ。
- **溢れ**: `q < L <= isize.max` なので、`q + 1` は溢れない。

```kimi
(1..^1).tryResolve(5)   // Some(1..4)
(1..=3).tryResolve(5)   // Some(1..4)
(2..).tryResolve(5)     // Some(2..5)。End は 5 に解決する。
(2..=1).tryResolve(5)   // None: 逆転している。
(..=^0).tryResolve(5)   // None: ^0 は要素位置ではない。
(^7..).tryResolve(5)    // None: 7 > 5。
let r = (1..^1).resolve(5)
r.tryResolve(3)         // None: 終端 4 が長さ 3 を超える。
```

#### 2.5.4. Abort する操作と失敗

- **Contract の要件**: `Position`・`PositionRange` とも、`tryResolve` だけとする。
- **Abort 版の定義**: Abort する操作（添字、`insert`・`remove` など）と `resolve` は、一律に「同じ解決が None なら Abort する」と定める。
- **`resolve` の公開**: 整数以外の位置と範囲の型は、命名の対として `resolve(self: Self, length: isize)` を公開する。

| 操作（n は負の isize、s は Slice） | 結果 |
| --- | --- |
| `let p = ^n`、`let r = n..` | 成功する（検査しない） |
| `values[n..]`・`values[^n..]`・`values[^n]`・`values[2..=1]` | 解決で Abort |
| `s.tryGet(n..)`・`s.tryGet(^n)`・`s.tryGet(2..=1)`・`s.trySplitAt(^n)` | None |

### 2.6. 添字と API

#### 2.6.1. 列の添字

固定配列・Array・Slice の具体型の添字 `x[k]` は、k の型で決める。

| k の型 | 意味 |
| --- | --- |
| `P is Position` | k を要素位置 q に解決し、`Indexable<isize>` の添字 `x[q]` を行う。解決に失敗すれば Abort する |
| `R is PositionRange` | `k.tryResolve(L)` の結果を適用した Slice。解決に失敗すれば Abort する |

- **要素添字**: 要素 Place・権限・`index` と `indexUniq` の選択は、`Indexable<isize>` の規則に従う。
  - Slice は `Indexable<isize>` に、Array と固定配列は `UniqIndexable<isize>` に適合する。`Indexable<Index>` への適合は削除する。
  - 固定配列の直接の要素も、同じ入力と規則で選ぶ組み込みの射影とする（現行の §4.6.9）。
- **範囲添字**: Indexable の外の規則のままとする（現行の §4.6.9）。
- **正規化の範囲**: 位置の正規化は、列の具体型に限る。ジェネリックな `S is Indexable<Key>` と、その他の型の添字は、現行どおりキーの型そのもので `Indexable<Key>` を選ぶ。
- **キー**: §2.3.3 の規則で型を決め、値として読む。
- **受け手**:
  - 1回だけ評価する。既存の Place はその場所を使い、所有の一時値だけを一時 Place に実体化する。
  - 受け手の形は問わず、保存した範囲値でも同じとする。
  - 借用・アクセス予約と、連鎖添字で中間配列をコピーしないことは、現行の §4.6.4 に従う。
- **静的 Move Path と raw pointer**（現行どおり）: 静的 Move Path は、整数リテラルの添字だけとする。raw pointer の添字は isize の値だけとし、ほかの位置と範囲は受け付けない。

#### 2.6.2. 位置と範囲を受け取る API

位置と範囲を受け取る API はジェネリックとし、どちらも値で受け取る。引数ラベルは現行のままとする。

| API | 固定配列 | Array | Slice | 受け取るもの |
| --- | --- | --- | --- | --- |
| `tryGet(k)` | あり（追加） | あり | あり | 要素位置なら要素の参照、範囲なら Slice |
| `splitAt(p)`・`trySplitAt(p)` | あり（追加） | あり（追加） | あり | 境界 |
| `tryGetUniq(p)` | ― | あり | ― | 要素位置 |
| `insert(p, value)` | ― | あり | ― | 境界 |
| `remove(p)`・`swapRemove(p)`・`swap(p, q)` | ― | あり | ― | 要素位置 |

- **読み取り API**: `tryGet`・`splitAt`・`trySplitAt` は、Slice で定義する。
  - `tryGet(k)` は、添字 `x[k]` の try 版である。Dictionary の `tryGet` と同じ命名規約で、STYLE に記す。
  - `splitAt(p)` は、p を1回だけ評価・解決して `(x[..p], x[p..])` を返す。`trySplitAt(p)` は、解決に失敗すれば None を返す。
  - 固定配列と Array の同名 API は、`x[..]` の Slice の API と同じ意味とする。結果は受け手の共有借用に依存する（`during self`）。
  - 固定配列の API は、固定配列の反復の入口と同じく、内部 group `Kimi.Storage.FixedArray` の受け手関数（`<E, length N>`）として Kimi で宣言し、固定配列の受け手でだけ公開する。
- **`tryGet` の選択**: Position と PositionRange を両方満たす型はないので、2つの宣言のうち適用できるのは常に1つである。
- **isize の値のまま**: `truncate(length:)`・`init(capacity:)`・`init(repeating:count:)`、戻り値（`firstIndex`・`indices`）。

```kimi
// Slice<T>{source} 内の宣言。受け手はハンドルの値。
public func tryGet<P>(self: Self, index: P) -> Option<ref/T during source>
    P is Position
public func tryGet<R>(self: Self, range: R) -> Option<Slice<T> during source>
    R is PositionRange

// Array<T> 内の宣言。受け手は共有借用で、x[..] に委ねる。
public func tryGet<P>(self, index: P) -> Option<ref/T during self>
    P is Position
    return self[..].tryGet(index)
```

```kimi
var list: Array<i32> = [1, 2, 3]
list.insert(^0, 4)            // 末尾に追加する。[1, 2, 3, 4]
list.swap(0, ^1)              // [4, 2, 3, 1]
let x = list.remove(^2)       // 3。[4, 2, 1]
let n: u8 = 1
let y = list[n]               // 2。要素添字も任意の整数型を受け付ける。
let a = list.tryGet(^1)       // 1 への参照
let b = list.tryGet(1..)      // [2, 1] の Slice
let c = list.tryGet(5..)      // None
let parts = list.splitAt(^1)  // ([4, 2], [1])
```

### 2.7. 反復

| 型 | 反復の条件 | Iterator |
| --- | --- | --- |
| `Range<S, E>` | `S is PrimitiveInteger` かつ `E is S` | `RangeIterator<S>` |
| `ClosedRange<S, E>` | 同上 | `ClosedRangeIterator<S>` |
| `ResolvedRange` | なし（`Range<isize, isize>` と同じ） | `RangeIterator<isize>` |

- **適合**: 条件付き適合（§8.4.8）で、`Iterable`・`UniqIterable`・`IntoIterable` に適合する。片側の範囲や `FromEnd` を含む範囲は、条件を満たさないので反復できない。
- **入口**: `start > end` なら Abort する。入口は境界をコピーし、元の範囲の Storage・Origin・Loan を残さない。借用入口の `IteratorType(source)` は、source によらず同じ型とする（現行どおり）。
- **生成**: start から昇順・刻み1で生成する。`for var` で束縛を再代入しても、列は変わらない（現行どおり）。
  - `RangeIterator<T>` は `[start, end)` を生成する。
  - `ClosedRangeIterator<T>` は `[start, end]` を生成し、終端が T の最大値でも加算で溢れない。
- **反復器の性質**（2つとも、現行の `RangeIterator` と同じ）:
  - Owned・Non-Copy の Iterator で、公開コンストラクターも反復入口への適合もない。`for` で使うには、`Kimi.Iteration.owning`・`borrowing` を通す。
  - None を返した後は、None を返し続ける。Iterator の効果上限（§22.1.2.4）を満たす。
  - 内部表現は規定しない（§3.4）。

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]
let inner = 1..^1
for i in 0..values.length             // Range<isize, isize>
    Console.writeLine("\(values[i])")
for x in 0..=255@u8                   // ClosedRangeIterator<u8>。255 の後で加算しない。
    ()
for i in inner.resolve(values.length) // ResolvedRange。1, 2, 3
    ()
for i in inner                        // エラー: FromEnd を含む範囲は反復できない。
    ()

func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values               // T is T が成り立つので反復できる。
        total += value
    return total
```

### 2.8. 整形

位置と範囲は、それを書く構文の形で整形する（`Utf8Format`）。

| 値 | 整形 |
| --- | --- |
| 整数 n・`FromEnd<T>` | `n`・`^n` |
| `Start`・`End` | 空文字列（構文で省略する境界） |
| `Range<S, E>`・`ClosedRange<S, E>` | `start..end`・`start..=end` |

```kimi
Console.writeLine("\(1..^1)")              // 1..^1
Console.writeLine("\(..=3)")               // ..=3
Console.writeLine("\((1..^1).resolve(5))") // 1..4
```

### 2.9. 対象外

- 降順・刻み・無限の範囲（開始だけの範囲の反復を含む）
- 実行時に方向を選ぶ位置の型、ユーザー定義の位置と範囲の型
- `contains` などの範囲演算
- 長さを持つユーザー型への位置の正規化
- Contract の既定実装、範囲の実行時 View
- 文字列の添字

## 3. Changes — 変更点

### 3.1. 変更一覧

| 項目 | 現行 | 変更後 |
| --- | --- | --- |
| 位置の型 | `isize`・`Index` | `Position`（整数・`FromEnd<T>`・`Start`・`End`） |
| `^x` | Index 化（負数で Abort） | `FromEnd<T>`（検査しない） |
| 範囲の型 | `Range<T>`・`IndexRange` | `Range<S, E>`・`ClosedRange<S, E>`（省略は `Start`・`End`） |
| 閉区間 | `isInclusive` | `ClosedRange` と `ClosedRangeIterator` |
| 両端の型 | 同じ T（Index なら Index 化） | 境界ごと。反復するときだけ一致を求める |
| 引数 | 型ごとのオーバーロード | ジェネリック。値で受け取り、参照からは値を読む |
| 要素添字・位置 API の整数型 | isize だけ | 任意の整数型 |
| 読み取り API | Array・Slice でばらつく | Slice で定義し、列の3型でそろえる。`trySlice` は `tryGet` に統合する |
| 位置のリテラルの既定 | 範囲は i32、`^1` と要素添字は isize | すべて i32 |
| 負の位置を渡した try API | Abort | None |
| `(0..3) == (0..=2)` | false | 型エラー |
| 保存した範囲値の添字 | パス形の受け手だけ | 受け手の形を問わない |
| 整形 | なし | 構文の形で整形する |

### 3.2. 言語機構の変更

1. **型同一性の要件**（§8.3・§8.4.8.1・§8.7）: `X is U`（U は型）を一般規則とし、条件付き適合の条件にも使えるようにする。
   - 正規化して同一（同じ束縛子を含む）なら Proven、具体型どうしで異なれば Refuted、それ以外は Unknown とする。
   - 前提として得た同一性は、その有効範囲（条件付き適合のブロック本体を含む）で置換に使える。
2. **閉じた Contract**（§8.4.7）: `Position` と `PositionRange` は、適合する型を §22.1 が指定するものに限る Kimi の通常の Contract とする。含意は Contract の継承と制約で表す。整数の Position への適合は組み込みとし、§8.4.7.3 の PrimitiveInteger が与える能力に Position を加える（唯一の特別な証明規則）。
3. **値の読み取り**（§3.5.3・§7.3・§10.2・§10.2.1）: Scalar read を値の読み取りと改め、対象を読み取り型（Scalar・Position 型・PositionRange 型）に広げる。§7.3 の所有の受け手の行も読み取り型に広げ、その型引数の推論を定める（§2.3.3）。
4. **リテラル式**（§12.3.1）: `^a` と片側の範囲を加え、範囲の境界には期待型を境界ごとに伝える（§2.4.2）。既定は通常の整数リテラルと同じ i32 とし、「Index 化では isize」という既定を削除する。
5. **警告**（§17.4）: 位置と範囲がリテラル式・`Start`・`End` だけからなり、次のどちらかのときに警告を出す。判定はリテラルの値による線形の不等式なので、決定的に行える。実行時の Abort は、現行どおりコンパイルエラーに変えない（§17.3.4）。
   - すべての長さ L で解決に失敗する（例: 要素位置の `^0`、`..=^0`、`2..=1`、`^3..^5`）。
   - 対象が固定配列で、その長さ N で解決に失敗する（例: `fixed[5..2]`、要素数 3 の `fixed[3]`）。

### 3.3. 移行

| 現行 | 変更後 |
| --- | --- |
| `Index.init(n)` | `n` |
| `Index.init(n, fromEnd: true)`、`^n` | `^n`（`FromEnd<T>`） |
| 実行時に方向を選ぶ `Index` | 利用側で分岐するか、enum にする |
| `let r: Range<i32> = 0..3` | `let r: Range<i32, i32> = 0..3` |
| `IndexRange` や `Range<T>` を受け取る引数 | 関数が必要とする能力で選ぶ（§3.4 の診断） |
| `s.trySlice(r)` | `s.tryGet(r)` |
| 読み取り API のための `values[..]` | 不要（固定配列と Array で直接呼べる） |
| `Indexable<Index>` を使う汎用コード | `Indexable<isize>` を使うか、具体型にする |
| i32 に収まらないリテラルの位置 | `3_000_000_000@isize` のように型を明示する |

### 3.4. 実装方針

**ライブラリ**（Kimi で実装する）:

- **型**: `FromEnd<T>`・`Start`・`End`・`Range`・`ClosedRange`・2つの Contract・2つの反復器・整形を実装する。
- **解決の部品**: 意味は、境界の解決・要素位置の解決・順序の検査という3つの内部部品の組み合わせで書く。
  - `L >= 0` は、解決の入口で1回だけ検査する。
  - 幅の違う整数は、切り詰める前に上限を検査する。
  - 部品の表現は、中間値の構築・コピーと重複する検査を減らせるものを選ぶ。
- **整数の特殊化**: 整数の Position の実装は、現行の `Index.tryPosition<I>` を内部 group へ移したもので、その明示的な特殊化（isize・i32・usize）を引き継ぐ。よく使う範囲の組み合わせ（`Range<isize, isize>`・`Range<i32, i32>` など）の解決も、測定して必要なら特殊化する（§8.8）。
- **薄い入口**: ジェネリックな API は、解決だけを行う薄い入口にする。
  - 本体（要素の移動・Slice の生成）は、isize・`ResolvedRange` を受け取る共通の処理に任せる。
  - Array と固定配列の読み取り API は、Slice の API に委ねる。
  - Array の `insert`・`remove` などの位置版は、Kimi の入口で isize に解決してから、コンパイラーが実装する isize 版を呼ぶ。コンパイラーの Index 版と、Index のレイアウトに依存する生成は削除する。
- **反復器**: `RangeIterator<T>` は `current` と `end` だけを持ち、終端のフラグを持たない。`ClosedRangeIterator<T>` は、次の2案を測定で比べて選ぶ。
  - (a) 終端が T の最大値でなければ `[s, e + 1)` の状態で始め、最大値のときだけ終端のフラグを使う。
  - (b) `current > end` を終了状態とし、`current` と `end` の2つだけで表す。最後の要素を返すときに `current = 1`・`end = 0` とする。入口で逆転を拒否するので、内部の逆転は終了だけを表す。

**コンパイラー**:

- **型の決定**: 範囲構文は、形と境界の型から宣言IDで型を決める。Index 化の処理は削除する。
- **直接選択と検査の統合**: 範囲構文の直接選択は、ライブラリと同じ内部部品を呼ぶ形に下げる。
  - 生成コードでは、`L >= 0` の後、範囲全体の不等式で検査をまとめる。末尾相対の位置は、常に元の L で解決する。
  - 列の位置添字では、正規化の検査と `Indexable<isize>` の範囲検査を1回にまとめる。

  | 範囲 | まとめた条件 |
  | --- | --- |
  | `a..b` | `0 <= a <= b <= L` |
  | `a..=b` | `0 <= a <= b < L` |
  | `^a..^b` | `0 <= b <= a <= L` |

- **定数の畳み込み**: §3.2 項目5の警告の評価器を共有し、固定配列とリテラルの位置（`fixed[1..^1]`・`fixed[^1]`）の解決を定数に畳み込む。
- **受け手**: 保存した値による添字も、§2.6.1 の受け手の規則で長さの取得と解決を共通化し、P8 の制限を外す。
- **検査の除去**（§4.6.8）: 次の2つの証明を分けて扱う。この区別はコンパイラー内部のもので、公開型には所有者の情報を加えない。
  - 境界検査の除去には、`end <= 現在の長さ` の証明だけを要する（`indices` と `resolve` の結果を含む）。
  - アドレスや要素参照の再利用には、Storage・借用・アドレスがなお有効である証明が別に要る。再確保の前のアドレスは使わない。
- **固定配列の読み取り API**: Kimigayo には拡張宣言がないので、固定配列の反復の入口と同じく、内部 group `Kimi.Storage.FixedArray` の受け手関数として Kimi で宣言する。コンパイラーは固定配列の受け手でだけ、この group を探す。`length`・`indices` も、都合のよい時点でこの group に移す。
- **`ResolvedRange` の反復**: 意味は `RangeIterator<isize>` の入口と同じに保ち、反復器を作らない現行のカーソルループは、最適化として残す（§6.3 の性質テストで同じ列を確かめる）。

**診断**（`Fix` の文。§23 の構造化した修復候補の定義にも使う。自動の修復候補は、変更後の本体が検証できる場合だけ示す）:

- **反復できない範囲**: `FromEnd`・`Start`・`End` を含むなら `r.resolve(values.length)` を、整数型が違うなら明示変換を示す。
- **範囲の型の不一致**（`Range<T, T>` の引数に `ClosedRange` を渡すなど）: 関数が必要とする能力に合わせて示す。Slice に適用するなら `R is PositionRange`、列挙するなら反復の Contract（入口と Item の制約を含む）、境界を使うなら具体的な範囲型である。
- **必ず失敗する位置と範囲**: §3.2 項目5の警告を出す。
- **型の表示**: 利用者の宣言が `Start`・`End` などを隠している場合、診断とホバーでは `Kimi.Start` のように修飾して表示する。

### 3.5. 修正が必要な箇所

| 区分 | 対象 | 内容 |
| --- | --- | --- |
| ライブラリ | `Core.kimi` | `Index`・`IndexRange`・`Range<T>` を削除する。§3.4 の型・Contract・部品・反復器を追加する |
| | `Slice.kimi`・`Array.kimi`・`ArrayOperations.kimi` | §2.6.2 の API を、薄い入口と共通の本体に分ける。`trySlice` を `tryGet` に統合し、Array の読み取り API を Slice に委ねる |
| | `Formatting.kimi` | 位置と範囲の `Utf8Format` |
| 宣言 | `KimiDeclaration.cs`・`KimiLibrary.cs`・`KimiLibraryCatalog.cs`・`KimiLibraryRecords.cs`・`KimiLibraryValidation.cs` | 宣言ID、閉じた Contract の適合、整数の組み込み適合の結び付け、署名の検査 |
| 構文 | `Koto.cs`（`FromEndIndex`）・`KotoHelper.cs`・`UnaryKoto.cs`・`RangeKoto.cs` | `^x` と範囲の形 |
| 束縛・推論 | `Binding.Ranges.cs`・`Binding.Expressions.cs`・`Binding.Elements.cs`・`Binding.Sequences.cs`・`ElementAccess.cs`、制約の証明、条件付き適合 | 種別、境界ごとの期待型、値の読み取りと推論、位置添字の正規化、固定配列の読み取り API、反復、型同一性、閉じた Contract、警告 |
| 所有権・生成 | `OwnershipAnalysis*.cs`・`OwnershipModel.cs`・`BodyLowering.*.cs`・`ControlFlowAnalysis.cs`・`LlvmModuleWriter.Sequences.cs`・`ReferenceTypes.cs` | `FromEnd` 操作と Index のレイアウト依存の削除。直接選択の部品化、検査の統合・畳み込み・除去、受け手の制限の撤廃、`ArrayInsertIndex`・`ArrayRemoveIndex` の更新 |
| テスト | `IntegerRangeTest`・`RangeValueTest`・`RangeIndexParseTest`・`IndexValueTest`・`ExpressionPrecedenceTest`・`KeyedIndexingTest`・`DynamicArrayIndexTest`・`SliceOperationsTest`・`SliceCostTest`・`ArrayMembersTest`・`DynamicArrayCostTest`・`CoreCatalogTest`・`CatalogSignatureValidationTest`・`ContractSignatureValidationTest`・`RecordLayoutValidationTest`、Milestone 27 など、ネイティブ fixture、`docs/examples/Ranges` | 型名・既定の型・意味を更新し、§6.3 の性質テストを加える。レイアウトが現れる fixture は全件を走査する |
| 文書 | `docs/LIBRARY.md`・`docs/STYLE.md`・`docs/STATUS.md` | 公開宣言、API 規約（§2.2 の規則4、`tryGet` の命名）、対応範囲 |

### 3.6. 実装上の注意

1. **閉じた Contract は前例がない**。現行の検証は、内在 Contract にメンバーを認めない（`KimiLibraryValidation`）ので、内在 Contract にはしない。
   - 適合の集合は宣言IDで固定し、利用者の `Self is Position` は拒否する。
   - Kimi の型（`FromEnd<T>` など）の適合は、通常の照合（§8.4.5）で検証する。
   - 整数の適合は照合を使わず、Equatable の組み込み適合と同じ経路で、宣言IDにより内部関数に結び付ける。署名は `KimiLibraryValidation` で検査する。
2. **型同一性の証明と置換**: 条件付き適合の本体では、E を S として型検査する。§8.4.3 の関連型の同一性の事実と同じ仕組みで扱う。
   - 現行では、具体型を代入した後の判定はできるが、型の適合（`FitsTypeCore`）は事実を使わないので、本体で `self.end`（型 E）を S として扱えない。
   - 一般的な等式の解決器は作らない。有効範囲にある型引数どうしの同一性の前提は、その範囲で片方の型引数を他方に置き換える正規化として実装する。`sum<T>(values: Range<T, T>)` は同じ型なので、置換なしで証明できる。
3. **位置添字の正規化と Indexable の選択**（`bf6c09cd`）: 列の具体型の位置添字は、先に isize へ正規化してから `Indexable<isize>` を選ぶ。それ以外は、現行の選択に任せる。
4. **`tryGet` の2つの宣言**: リテラル式や参照の実引数でも、適用できる候補が1つに決まることを確かめる（`tryGet(3)`・`tryGet(0..3)`・`tryGet(i)`）。
5. **受け手の誤った統一**: 次の2つは誤りである。§2.6.1 のとおり、場所を1回だけ確定する。
   - 受け手を一時変数へ値で退避する。大きな配列のコピーや Non-Copy の拒否が起き、元と異なる場所にアクセスする。
   - 受け手全体を借用して統一する。Partial Move の後に残った要素へ、直接アクセスできなくなる。
6. **名前の衝突**: コンパイラー内部の `FromEnd` 操作と、構文の `FromEndIndex` は、削除するか改名する。
7. **編集時の再束縛**: 範囲の形が編集で変わったとき、合成呼び出しの宣言が切り替わることをテストで確かめる。

## 4. Rationale — 変更する理由

### 4.1. 目的と効果

目的は、範囲と位置の意味を少数の閉じた値型と1つの解決規則で説明し、失敗の段階をそろえ、格納と実行時の分岐を最小にすることである。

| 問題 | 解決の仕方 |
| --- | --- |
| P1 | 種別の表・Index 化・包含終端 `^0` の禁止が、「構文→型」の表と2行の解決表に置き換わる。省略した境界は位置の型の1つとし、`ResolvedRange`・`indices`・`splitAt` も同じ規則で定義する |
| P2 | 構築は検査しないので、失敗はすべて使う時点で起きる |
| P3・P4 | 位置を受け取るところは、すべて同じ `Position` を同じ読み方で受け取る |
| P5 | 位置と範囲の解決に、方向と形の実行時分岐がなくなる。半開区間の反復器は、終端のフラグを持たない |
| P6 | 形の違う範囲の比較は、型エラーになる |
| P7 | ジェネリックな宣言にまとまる。読み取り API は Slice で定義し、列の3型でそろう |
| P8 | 受け手の形を問わない、共通の下げ方になる |

### 4.2. 性能

**型から確定する効果**:

| 値（境界が i32、windows-x64） | 現行 | 変更後 |
| --- | --- | --- |
| `^1` | 16 B | 4 B |
| `1..^1` | 40 B | 8 B |
| `0..10` | 12 B | 8 B |
| `n..` | 40 B | 4 B |
| `..` | 40 B | 格納なし |

- **分岐**: 方向と区間の形が静的に決まるので、解決に実行時の分岐がない。`Start`・`End` は検査なしで 0・L に解決し、これらを含む半開区間は順序の検査も要らない。
- **受け渡し**: 位置と範囲は値で受け渡すので、借用が要らない。閉じた Kimi の型だけなので、解決は常に融合できる。
- **反復器**: 半開区間の反復器は、終端のフラグの状態を持たない。

**生成コードで確かめる目標**（方法は §3.4、確認は §6.3）:

- **値の渡し方**: 渡し方はコンパイラーが選ぶ（実装仕様 §21）。小さな位置と範囲は、Kimi の内部呼び出しでフィールドごとにレジスターで渡す。
- **比較**: 同じ幅の `0 <= n <= L` を、符号なし比較1回にする。言語の変換 `n@usize` は負数で Abort するので、この目的には使わない。
- **検査**: 検査の統合・定数の畳み込み・除去が効くこと。反復変数の拡幅と検査の除去は、非負性と上限の証明がある場合だけ行う。範囲の反復変数は、負にもなりうる（`-3..3`）。
- **O0**: 部品の中間値、整数の特殊化、`x[..]` への委譲のコスト。
- **反復**: 保存・返却した Iterator を含む、各要素の検査数。`ClosedRangeIterator` の2案の比較。

### 4.3. 検討した代替案

設計の分岐点になった案:

| 案 | 不採用の理由 |
| --- | --- |
| 「半開区間への統一と ClosedRange」（2026-09-29） | `..=b` と、Index を含む閉区間が書けない。P2・P3・P4・P7 が残る |
| 同じ型引数の `Range<B>` と、実行時の方向を持つ Index | 混在形 `1..^1` で Index 化が要り、構築時の失敗（P2）と方向の分岐が残る |
| 省略の形ごとの範囲型（`RangeFrom`・`RangeTo`・`RangeThrough`・`RangeFull`） | 型と解決規則が形の数だけ増える。省略を位置の型で表せば、2つの型と2行の解決表で足りる |
| `Position`・`PositionRange` をユーザーに開く | ユーザーの `tryResolve` の効果が不明になる。ジェネリックな API を検証するには、`Iterator.next` と同じ形の効果上限（§8.4.5）と、結果の防御・呼び出し回数・実装者の責任の規則が要る。Non-Copy の範囲のために、共有借用で受け取る必要も生じる |
| 位置と範囲を共有借用で受け取る | 借用が呼び出しの間ずっと残るので、受け手の中の位置（`indexes.remove(indexes[0])`）を渡せない（§15.6.7）。組み込みの添字とも挙動が分かれる |
| 要素添字は isize だけのままにする | 範囲の境界と要素添字で受け付ける型が違う、という例外（P4）が残る |

そのほかの不採用案:

- **省略した境界を値の `0`・`^0` で表す**: 型引数が決まらず、格納が増え、`..n` が暗黙に反復できてしまう。
- **閉じた終端を位置の型 `Through<E>` にして範囲型を1つにする**: `2..=1` の検出に特例が要り、反復の扱いも複雑になる。
- **半開区間と閉区間で反復器を共用する**: 閉区間の終了を表す状態を、半開区間にも持たせることになる。
- **読み取りを使う場面の列挙で定める**: 同じ期待型でも、場面で読み方が変わる。
- **位置と範囲の型を Scalar に分類する**: Scalar の既存の意味（レイアウト、演算子、パターン）に広く影響する。
- **期待型を「範囲全体がリテラル式」のときだけ渡す**: `n..10` と期待型 `Range<i64, u8>` のように、境界ごとの型が合わなくなる。
- **整数型の違う両端を構築時に禁止する**: ジェネリックな境界を通すと防げず、防ぐには新しい型形成の制約が要る。
- **Contract の既定実装、型引数の既定値、`Resolvable<R>` への統合、`tryGetUniq` の統合**: 言語の一般機能が増えるか、選択や §7.3 の命名で区別が残る。
- **位置のリテラルの既定を isize にする**: 通常の整数リテラルと別の既定が要り、`for x in 0..10` の型も変わる。
- **型を明示した整数リテラルを静的添字（§15.1.3）に加える**: 効果は要素数 2^31 以上の固定配列だけである。
- **「必ず失敗する」を証明能力で判定する、または lint にする**: 警告が実装ごとにそろわない。

### 4.4. 副作用

- **共通の型がない**: 形の違う範囲は、分岐・配列・フィールドで混在できない。保存には `ResolvedRange`、受け渡しにはジェネリック、混在には利用側の enum を使う。
- **型の表記**: 整数範囲は `Range<i32, i32>`、`..` は `Range<Start, End>` と書き、表示される。
- **独自の型を作れない**: 実行時に方向を選ぶ位置の型や独自の範囲型は、作れない。利用側で分岐するか、Kimi の範囲に変換する。
- **失敗の時点**: `^n` と `n..` は構築では失敗せず、使う時点で失敗する。
- **反復できない範囲**: 片側の範囲、`FromEnd` を含む範囲、両端の整数型が違う範囲。
- **大きなリテラルの位置**: i32 に収まらない場合は型の明示が要り、その定数添字は静的 Move Path にならない（要素数 2^31 以上の固定配列だけに関係する）。
- **ジェネリックな添字**: 正規化は列の具体型に限るので、`S is Indexable<isize>` では `s[^1]` や i32 のキーを使えない。
- **整数の型引数の推論**: `T is PrimitiveInteger` で制約した既存の関数も、参照の実引数から終端の整数型を推論するようになる（`ref/i32` を渡した `double<T>(x: T)` が通る）。
- **整形**: 単独の `Start`・`End` は、空文字列に整形される。
- **API の変化**: `trySlice` は `tryGet` に統合される。固定配列と Array に読み取り API が加わる。位置の引数が Position に広がるので、`swap(^1, 0)` なども受け付ける。

## 5. Impact on SPEC.md — SPEC.md への影響

| 箇所 | 変更 |
| --- | --- |
| `docs/SPEC.md` の宣言索引、§3 の Copy の表 | `Index`・`IndexRange`・`Range<T>` を削除する。`FromEnd<T>`・`Start`・`End`・`Range<S, E>`・`ClosedRange<S, E>`・`ClosedRangeIterator<T>`・`Position`・`PositionRange` を追加する |
| §3.5.3・§10.2・§10.2.1 | Scalar read を値の読み取りと改め、対象を読み取り型に広げる。その型引数の推論 |
| §3.3.6・§3.4.1・§13.5.3・§14.9.1・§15.2 | Scalar read の対象としての「Scalar」を、読み取り型に置き換える |
| §5.3 | raw pointer の添字の文言（`pointer[^1]`・`pointer[0..4]`） |
| §7.3 | 所有の受け手の行を読み取り型に広げる。例の `insert(index: Index…)` を改める |
| §12.1・§12.2 | 分類の「From-end Index」、片側の範囲の評価順 |
| §4.5・§4.6.1 | 型の表、添字が受け付ける型、`indices` の定義、固定配列の読み取り API。長さの情報と要素 Place の規則は維持する |
| §4.6.2・§4.6.3 | 該当部分を本書 §2.3・§2.4・§2.7 で置き換える。本書が「現行どおり」とした規則は、型名を改めて維持する |
| §4.6.4 | 解決と失敗の記述を本書 §2.5 で置き換え、Index 化の記述を削除する。評価順・書き込み・Abort の規則は維持する |
| §4.6.6・§4.7.2 | 位置と範囲の API（本書 §2.6.2）。`splitAt`・`trySplitAt` を範囲添字で定義する |
| §4.6.8 | 表現と性能の文言、数値の証明による検査の除去 |
| §4.6.9・§10.6 | 列の具体型の位置添字を `Indexable<isize>` への正規化とする。範囲添字とキーの値の読み取り。列の Indexable への適合は isize だけにする |
| §8.3・§8.4.8.1・§8.7 | 型同一性の要件 |
| §8.4 冒頭・§8.4.7・§8.4.7.3 | `Indexable<Index>` の例、閉じた内在 Contract、PrimitiveInteger による含意、`sum` の例 |
| §12.3.1 | リテラル式と、境界ごとの期待型の伝播 |
| §13.1・§13.2 | 優先順位の例（`^n + 1`）、接頭辞 `^` |
| §14.6.2 | 反復の Subject |
| §15.1.3 | ConstantIndexExpression の表 |
| §17.3.1・§17.4 | 失敗の例、必ず失敗する位置と範囲の警告 |
| §22.1 | 必須宣言の表（新しい型・閉じた Contract の適合・整数の Position の実装） |
| `utf8-formatting.md` | 位置と範囲の整形 |
| 付録 D・E・F | 対象外の項目、用語、文法 |
| 実装仕様 §21.5.3・付録 A.13 | 検査の生成、試験要件 |
| アンカー | §3.5.3・§4.6.2〜4.6.4 の見出しを変えるので、それを参照するリンク（LIBRARY・付録 E・examples・milestones の README など）を直す |

仕様の外の文書（`docs/LIBRARY.md`・`docs/STYLE.md`・`docs/STATUS.md`・`docs/examples/Ranges`・`tests/milestones/README.md`）は、実装の各単位で更新する。

## 6. Decision — 最終決定

### 6.1. 決定

- §2 の仕様と §3 の変更を採用する。「半開区間への統一と ClosedRange」（2026-09-29）は取り込まない。
- 正式仕様は本書を参照せず、自己完結させる。取り込み後は `draft/INTEGRATED.md` に対象節とコミットを記録し、本書を固定する。

### 6.2. 実装順序

1. **型同一性の要件**を、一般規則として実装する。
2. **位置と範囲の Contract と API を入れる**。
   - `Position`・`PositionRange`（閉じた Contract）と、`FromEnd<T>`・`Start`・`End` を追加する。
   - API を値で受け取るジェネリックな入口にし、読み取り API を Slice の定義にそろえ、値の読み取りを実装する。
   - 途中段階に限り、Kimi は `Index` を Position に、既存の範囲型を PositionRange に適合させる（仕様の規則ではない）。
3. **構文を切り替える**。`^x` を FromEnd に、範囲を `Range`・`ClosedRange` に切り替え、リテラル式・2つの反復器・整形を実装する。`Index`・`IndexRange`・`Range<T>` を削除する。
   - 旧 `Range<T>` と新 `Range<S, E>` が同名で並ぶ期間は作らない。新しい型の追加と構文の切り替えを1つの単位にするか、仮の名前で追加してから改名する。
   - 影響するテストが多い（範囲と Index のテストクラス約15個、ネイティブ fixture 約130件、Milestone 7・9・11・13・14・27・28・29・37・39）ので、切り替えは範囲・`^x`・テストに分けてコミットする。
4. **下げ方を共通化する**。直接選択の部品化、検査の統合・畳み込み・除去、受け手の制限の撤廃、警告を実装する。

各単位は、`./scripts/verify.ps1 -Class ...` と、関連する O0・O2 の fixture・Milestone で検証してからコミットする。セッションの最後に `./scripts/verify.ps1 -Mode Session` を実行する。

### 6.3. 検証

- **共通の性質**（多数の整数型・境界値・長さで確かめる）:
  - 解決に成功すれば、必ず `0 <= start <= end <= L` である。
  - 解決した結果を同じ L で再び解決すると、同じ結果になる。
  - 範囲を直接書いた場合と、変数に保存した場合で、選ぶ区間が一致する。
  - try 版が成功する入力では、Abort 版も同じ位置・区間を選ぶ。
  - `ResolvedRange` の反復回数は `end - start` と一致し、同じ境界の `Range<isize, isize>` と同じ列を生成する。
  - 統合・畳み込みした検査は、溢れのない数学的な参照モデルと結果が一致する。
- **個別の確認**:
  - **位置と範囲**: 幅と符号の違う全整数型・`FromEnd`・`Start`・`End` を、値 0・L・L + 1（表現できる場合）・負数・型の最大値、長さ 0・isize の最大値で解決する。形ごとの結果、逆転（`2..=1`）、負の長さ（`..` を含む）も確かめる。
  - **反復**: 最大値を含む閉区間の列挙と、終了後の `next`。反復できない範囲の拒否、型同一性による反復、入口が元の範囲の借用を残さないこと。
  - **型**: 境界ごとの期待型（`let d: Range<i64, u8> = n..10`）、既定の i32、i32 に収まらないリテラルの拒否、`tryGet` の宣言の選択、`^n + 1` の型エラー。
  - **読み取り**: 値・一時値・`ref`・`uniq`・二重の参照から渡す位置と範囲。型注釈付き初期化・戻り値・参照を通した受け手での読み取り。`let p = t` が参照のままであること。`T is PrimitiveInteger` の型引数の推論。
  - **API と受け手**:
    - 列の3型の読み取り API が、`x[..]` の Slice の API と同じ結果になること。Array の結果が受け手の借用に依存すること。
    - `values[values[0]] = x` と `values.remove(values[0])` が書けること。受け手の副作用が1回だけ起きること。
    - Partial Move の後の固定配列、静的 Move Path、連鎖添字。
  - **失敗・警告・整形**: try API は None を、Abort 版は Abort を返すこと。§3.2 項目5の警告が、例示した場合に出て、それ以外には出ないこと。§2.8 の各形。
- **性能**: 変更前後を比べ、最小値の悪化が 3% 以内であること（既存の基準）。中央値が 3% を超えて悪化したときや、差がばらつきと同程度のときは、追加で測定して原因を分析する。
  - **記録するもの**: 中央値とばらつき、O0 の時間、O2 の生成コードとコード量、格納サイズ、§4.2 の目標の達成状況。
  - **条件**: 長さ（空・短い・通常）、境界の整数型の幅、ループで直接使う場合と Iterator や範囲を返す・保存する場合。
  - **対象**:
    - `values[a..b]`・`values[1..^1]`・`values[a..]`・`s.tryGet(r)`・`fixed[1..^1]`
    - `values[^1]`・`values.insert(^1, x)`
    - `for i in 0..n`・`for i in 0..=n`（直接と、保存した Iterator。`ClosedRangeIterator` の2案）
    - i32 の反復変数による `values[i]`、`for i in r.resolve(values.length)` の中の `values[i]`
