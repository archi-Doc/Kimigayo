# 仕様変更案：範囲と位置の再設計 B

日付: 2026-09-29

状態: 提案。正式仕様への取り込み・実装・検証は未実施。

本書で変更する事項は、`docs/SPEC.md` とその参照先より優先する。本書で変更しない事項には既存仕様を適用する。基準は「整数範囲と位置範囲」（2026-09-28）を取り込んだ後の SPEC である。本書は「半開区間への統一と ClosedRange」（2026-09-29）の代替案であり、採用する場合、同書は取り込まない。

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
- **範囲の引数**: 範囲添字は3つの範囲型を受け取り、`trySlice` はオーバーロード3つで受け取る。

### 1.2. 問題点

- **P1 規則が多い**: 種別の決定表、Index 化の規則と評価順、包含終端 `^0` の禁止など、範囲の形ごとの個別規則がある。
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
3. **検査は解決で行う。** 位置と範囲は、使う時点の長さ L に対して解決し、そこで検査する。Abort する操作と try 操作の違いは、解決が失敗したときの扱いだけである。
4. **方向と区間の形は型で表す。** 位置と範囲の値は、境界以外の状態を持たない。
5. **位置は値として読む。** 位置を要求するところでは、参照をたどって終端の値を読む（§2.3.3）。
6. **位置は `Position`、範囲は `PositionRange` で受け取る。** 長さ・容量・個数は isize のままとする。

### 2.3. 位置

#### 2.3.1. `Position`

`Kimi.Position` は、閉じた内在 Contract である。満たす型は 12 の整数型と `FromEnd<T>` だけで、ユーザーの型も他の Kimi の型も適合できない。

```kimi
contract Position
    func tryResolve(self, length: isize) -> Option<isize>
```

- **含意**: `T is PrimitiveInteger` は `T is Position` を含意する。`T is Position` は、Copy・Owned・Equatable を含意する。
- **解決**: `L < 0` なら失敗する。
  - 整数 n は、`0 <= n <= L` なら n に解決する。
  - `FromEnd<T>` の offset は、`0 <= offset <= L` なら `L - offset` に解決する。
- **整数の実装**: §22.1 が指定する Kimi の内部関数とし、コンパイラーが宣言IDで結び付ける。整数型にメンバーは追加しない。この要件は `P is Position` を通じて呼ぶ。

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
- **等価性**: offset で決める。`^0` と `0` は型が違うので、比較できない。

#### 2.3.3. 位置の読み取り

位置を要求する次のところでは、`ref`・`uniq` の層を終端までたどり、その値をコピーする（§3.5.3 の Scalar read を広げる）。

- 要素添字
- `^x` の被演算子
- 範囲の境界
- `P is Position` の引数（P は、終端の型から推論する）

```kimi
let picks: Array<isize> = [0, 2]
for i in picks          // i: ref/isize
    let v = s.tryGet(i) // P は isize になる。
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

- 6つの型は、すべて Owned・Copy・Equatable で、`PositionRange` を満たす。範囲構文だけで作れる。
- 等価性は、同じ型の境界どうしで決める。形の違う範囲は型が違うので、比較できない。
- `a..=` は構文エラーとする。演算子の優先順位は変えない。

#### 2.4.2. 境界の型

- **境界**: `Position` を満たす型とし、§2.3.3 の規則で読む。
- **整数型**: 両端の整数型（`FromEnd<T>` では T）が分かるときは、両端でそろえる。型の確定した境界どうしは変換しない。これは二項演算と同じ規則である。
- **リテラル式**:
  - 境界がリテラル式なら、他方の境界の確定した整数型に合わせる。
  - 範囲全体がリテラル式なら、期待型に適合させる。手がかりがなければ isize を既定とする。
  - `^a` の a がリテラル式なら、`^a` もリテラル式とする。
- **ジェネリックな境界**: `P is Position` の境界は整数型を持たないので、上の整数型とリテラル式の規則を適用しない。

```kimi
let a = 1..^1       // Range<isize, FromEnd<isize>>
let b = 0@i32..^2   // Range<i32, FromEnd<i32>>
let c = 0@i32..2@i64 // エラー: 整数型が違う。
let d: ClosedRange<u8, u8> = 0..=255
```

### 2.5. 解決

#### 2.5.1. `PositionRange`

```kimi
contract PositionRange
    func tryResolve(self, length: isize) -> Option<ResolvedRange>
```

- 6つの範囲型と `ResolvedRange` が適合する。
- ユーザーの型も適合できる。それでも安全性は保たれる。`ResolvedRange` は Kimi しか作れず、適用するときに `end <= L` を検査するためである。

#### 2.5.2. 規則

| 型 | 開始 s | 終了 | 順序の条件 | 結果 |
| --- | --- | --- | --- | --- |
| `Range<S, E>` | start | 境界 e | `s <= e` | `[s, e)` |
| `ClosedRange<S, E>` | start | 要素位置 q | `s <= q` | `[s, q + 1)` |
| `RangeFrom<S>` | start | ― | ― | `[s, L)` |
| `RangeTo<E>` | 0 | 境界 e | ― | `[0, e)` |
| `RangeThrough<E>` | 0 | 要素位置 q | ― | `[0, q + 1)` |
| `RangeFull` | 0 | ― | ― | `[0, L)` |
| `ResolvedRange` | ― | ― | `end <= L` | 自身 |

- **失敗**: 位置の解決が1つでも失敗すれば、範囲の解決も失敗する。
- **順序**: 解決した境界どうしで、1回だけ比べる。逆転を空区間にはしない（`2..=1` も失敗する）。
- **溢れ**: `q < L <= isize.max` なので、`q + 1` は溢れない。

#### 2.5.3. Abort する操作

- Contract の要件は `tryResolve` だけとする。
- Abort する操作（添字、`insert`・`remove` など）と `resolve` は、一律に「同じ解決が None なら Abort する」と定める。
- Kimi の範囲型と `FromEnd<T>` は、命名の対として `resolve` を公開する。

### 2.6. 添字と位置の API

#### 2.6.1. 組み込みの列の添字

固定配列・Array・Slice の添字 `x[k]` は、組み込みの規則で決める。範囲添字と同じく、Indexable の外の規則とする。

| k の型 | 結果 |
| --- | --- |
| `P is Position` | 要素位置 q の要素の Place |
| `R is PositionRange` | 解決した区間の Slice（`ResolvedRange` の適用を基底とする） |

- **Indexable との関係**: 選ばれる要素は、`Indexable<isize>` の実装と一致する。列は、ジェネリックなコードのために `Indexable<isize>`（Array・固定配列は `UniqIndexable<isize>`）に適合する。`Indexable<Index>` への適合は削除する。
- **受け手**: 受け手を1回評価して長さを読み、キーを解決して適用する。受け手の形は問わず、保存した範囲値でも同じとする。借用とアクセス予約は、既存の規則に従う。
- **静的 Move Path**: 現行どおり、整数リテラルの添字だけとする。
- **raw pointer**: 添字は isize だけで、`FromEnd<T>` と範囲は受け付けない。

#### 2.6.2. 位置の API

位置と範囲を受け取る API は、ジェネリック1本とする。引数ラベルは現行のままとする。

| 受け取るもの | API |
| --- | --- |
| 要素位置（`q < L`） | 要素添字、`tryGet`・`tryGetUniq`・`remove`・`swapRemove`・`swap` |
| 境界（`p <= L`） | `insert`・`splitAt`・`trySplitAt` |
| 範囲 | 範囲添字、`trySlice` |
| isize のまま | `truncate(length:)`・`init(capacity:)`・`init(repeating:count:)`、戻り値（`firstIndex`・`indices`） |

```kimi
// Slice<T>{source} 内の宣言。
public func tryGet<P>(self: Self, index: P) -> Option<ref/T during source>
    P is Position
public func trySlice<R>(self: Self, range: R) -> Option<Slice<T> during source>
    R is PositionRange
```

### 2.7. 反復

| 型 | 反復の条件 | Iterator |
| --- | --- | --- |
| `Range<S, E>`・`ClosedRange<S, E>` | `S is PrimitiveInteger` かつ `E is S` | `RangeIterator<S>` |
| `ResolvedRange` | なし | `RangeIterator<isize>` |
| その他の範囲型 | 反復できない | ― |

- **適合**: 条件付き適合（§8.4.8）で、`Iterable`・`UniqIterable`・`IntoIterable` に適合する。
- **入口**: `start > end` なら Abort する。閉区間は、終端が T の最大値でなければ `[s, e + 1)` の状態で始め、最大値のときだけ終端のフラグを使う。
- **RangeIterator**: `RangeIterator<T>` の性質は現行どおりとする（Non-Copy、None の後は None のまま、最大値の後で加算しない）。

### 2.8. 例

```kimi
let values: [5 of i32] = [10, 20, 30, 40, 50]

let inner = 1..^1          // Range<isize, FromEnd<isize>>
let middle = values[inner] // 20, 30, 40
let last = values[^1]      // 50
let n: i64 = 3
let head = values[..n]     // RangeTo<i64>。i64 のまま解決する。
let second = values[n - 2] // 要素添字も任意の整数型。20

for i in 0..values.length  // Range<isize, isize>
    Console.writeLine("\(values[i])")
for x in 0..=255@u8        // ClosedRange<u8, u8>。255 の後で加算しない。
    ()
for i in inner.resolve(values.length) // ResolvedRange。1, 2, 3
    ()
for i in inner             // エラー: FromEnd を含む範囲は反復できない。
    ()

func sum<T>(values: Range<T, T>) -> T
    T is PrimitiveInteger
    var total: T = 0
    for value in values    // T is T が成り立つので反復できる。
        total += value
    return total
```

| 操作（n は負の isize） | 結果 |
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
| 位置・範囲の引数 | 型ごとのオーバーロード | `P is Position`・`R is PositionRange` の1本 |
| 要素添字・位置 API の整数型 | isize だけ | 任意の整数型 |
| 位置と範囲のリテラルの既定 | `Range<i32>`（Index 化は isize） | isize |
| `trySlice(n..)`・`tryGet(^n)`（n < 0） | Abort | None |
| `(0..3) == (0..=2)` | false | 型エラー |
| 保存した範囲値の添字 | パス形の受け手だけ | 受け手の形を問わない |

### 3.2. 言語機構の変更

1. **型同一性の要件**（§8.3・§8.7）: `X is U`（U は型）を一般規則として定める。判定は次のとおり。
   - 正規化して同一（同じ束縛子を含む）なら Proven。
   - 具体型どうしで異なれば Refuted。
   - それ以外は Unknown。
   - 前提として得た同一性は、その有効範囲で置換に使える。条件付き適合のブロック本体も有効範囲に含む。
2. **閉じた内在 Contract `Position`**（§8.4.7）: 適合は、12 の整数の組み込み適合と、§22.1 が指定する `FromEnd<T>` だけとする。§8.4.7.3 の PrimitiveInteger が与える能力に、Position を加える。
3. **位置の読み取り**（§3.5.3・§10.2）: §2.3.3 の規則。Position で制約した型引数は、参照の引数の終端の型で推論する。
4. **リテラル式**（§12.3.1）: `^a` と片側の範囲を加える。位置と範囲の既定を isize とする（長さの文脈と同じ）。

採用しないもの（§4.3）: Contract の既定実装、包括的適合（blanket conformance）、実行時の方向を持つ位置の型。

### 3.3. 移行

| 現行 | 変更後 |
| --- | --- |
| `Index.init(n)` | `n` |
| `Index.init(n, fromEnd: true)`、`^n` | `^n`（`FromEnd<T>`） |
| 実行時に方向を選ぶ `Index` | 利用側で分岐するか、enum にする |
| `let r: Range<i32> = 0..3` | `let r: Range<i32, i32> = 0..3` |
| `IndexRange` や `Range<T>` を受け取る引数 | `R is PositionRange` |
| `Indexable<Index>` を使う汎用コード | `Indexable<isize>` を使うか、具体型にする |
| `for x in 0..10` の x（i32） | isize になる。i32 が要るなら `0@i32..10` と書く |

### 3.4. 実装方針

- **ライブラリ**:
  - `FromEnd<T>`・6つの範囲型・`Position`・`PositionRange`・反復の入口を、Core.kimi に Kimi で実装する。
  - 解決は、境界の解決・要素位置の解決・順序の検査という3つの内部部品を組み合わせて書く。
  - 整数の `Position` の実装は、現行の `Index.tryPosition<I>` を内部 group へ移したものとする。
- **コンパイラー**:
  - 範囲構文は、形と境界の型から宣言IDで型を決める。Index 化の処理は削除する。
  - 範囲構文の直接選択は、ライブラリと同じ内部部品を呼ぶ形に下げ、検査の二重実装をなくす。O2 ではインライン化する。
  - 保存した値による添字も、受け手を1回評価して長さを読む共通の下げ方にする。これで P8 の制限を外す。
- **診断**（`Fix` の文。§23 の構造化した修復候補を実装するときの定義にも使う）:
  - 反復できない範囲には、`r.resolve(values.length)` か、整数の範囲への書き換えを示す。
  - 形の不一致（`Range<T, T>` の引数に `ClosedRange` を渡すなど）には、引数を `R is PositionRange` か反復の Contract にする書き換えを示す。
  - 整数型の不一致には、明示変換を示す。
  - 常に失敗する位置（要素位置の `^0`、`..=^0`）には、警告を出す。

### 3.5. 修正が必要な箇所

| 区分 | 対象 | 内容 |
| --- | --- | --- |
| ライブラリ | `Core.kimi` | `Index`・`IndexRange`・`Range<T>` を削除する。`FromEnd<T>`・6つの範囲型・2つの Contract・内部部品・反復の入口を追加する |
| | `Slice.kimi`・`Array.kimi` | §2.6.2 の API をジェネリックにする |
| 宣言 | `KimiDeclaration.cs`・`KimiLibrary.cs`・`KimiLibraryCatalog.cs`・`KimiLibraryRecords.cs`・`KimiLibraryValidation.cs` | 宣言ID、整数の組み込み適合の結び付け、署名の検査 |
| 構文 | `Koto.cs`（`FromEndIndex`）・`KotoHelper.cs`・`UnaryKoto.cs`・`RangeKoto.cs` | `^x` と範囲の形 |
| 束縛・推論 | `Binding.Ranges.cs`・`Binding.Expressions.cs`・`Binding.Elements.cs`・`Binding.Sequences.cs`・`ElementAccess.cs`、推論、制約の証明、条件付き適合 | 種別、リテラル式、位置の読み取り、位置添字、反復、型同一性、Position |
| 所有権・生成 | `OwnershipAnalysis*.cs`・`OwnershipModel.cs`・`BodyLowering.*.cs`・`ControlFlowAnalysis.cs`・`LlvmModuleWriter.Sequences.cs`・`ReferenceTypes.cs` | `FromEnd` 操作と Index のレイアウト依存の削除、直接選択の部品化、受け手の制限の撤廃、`ArrayInsertIndex`・`ArrayRemoveIndex` |
| テスト | `IntegerRangeTest`・`RangeValueTest`・`RangeIndexParseTest`・`IndexValueTest`・`ExpressionPrecedenceTest`・`KeyedIndexingTest`・`DynamicArrayIndexTest`・`SliceOperationsTest`・`SliceCostTest`・`ArrayMembersTest`・`DynamicArrayCostTest`・`CoreCatalogTest`・`CatalogSignatureValidationTest`・`ContractSignatureValidationTest`・`RecordLayoutValidationTest`、Milestone 27 など、ネイティブ fixture、`docs/examples/Ranges` | 型名・既定の型・意味を更新する。レイアウトが現れる fixture は全件を走査する |
| 文書 | `docs/LIBRARY.md`・`docs/STYLE.md`・`docs/STATUS.md`・`docs/impl/appendices/A-compiler-requirements.md` | 公開宣言、API 規約（§2.2 の規則6）、対応範囲、試験要件 |

### 3.6. 実装上の問題点

1. **要件を持つ閉じた内在 Contract は前例がない**（`PrimitiveInteger` は要件を持たない）。
   - 対策: 整数の実装を宣言IDで内部関数に結び付け、`KimiLibraryValidation` で署名を検査する。§8.4.5 の照合は使わない。
2. **型同一性の要件の証明と置換**: 条件付き適合の本体で、E を S として型検査する必要がある。
   - 対策: §8.4.3 の関連型の同一性の事実と同じ仕組みで扱う。
3. **位置の読み取りによる推論**: 参照の引数から終端の型を推論する。
   - 対策: Position で制約した型引数だけを対象にし、オーバーロードの順位は変えない。
4. **リテラルの既定の変更**（i32 から isize）: テストと Milestone の期待値や、反復の型が変わる。
5. **組み込みの列の添字と Indexable の選択**（`bf6c09cd`）の関係。
   - 対策: 列には組み込みの規則を先に適用し、それ以外は現行の選択に任せる。
6. **名前の衝突**: コンパイラー内部の `FromEnd` 操作と、構文の `FromEndIndex`。
   - 対策: 削除するか改名する。
7. **編集時の再束縛**: 範囲の形が編集で変わったとき、合成呼び出しの宣言が切り替わることをテストで確かめる。
8. **受け手の制限の撤廃**: 受け手を一時的な Place に評価してから長さを読む。時間と生成コードを記録する。

## 4. Rationale — 変更する理由

### 4.1. 目的

- 範囲と位置の意味を、型と1つの解決規則で説明し、規則を減らす。
- 失敗を解決の段階にそろえ、try API の意味を一貫させる。
- 方向と区間の形を静的にし、格納と実行時の分岐を最小にする。

### 4.2. 効果

| 問題 | 解決の仕方 |
| --- | --- |
| P1 | 種別の表、Index 化、包含終端 `^0` の禁止が、「構文→型」と1つの解決表に置き換わる |
| P2 | 構築は検査しないので、失敗はすべて解決の段階で起きる |
| P3・P4 | 位置を受け取るところは、すべて同じ `Position` を受け付ける |
| P5 | 方向と形の実行時分岐がなくなる。残るのは、閉区間の反復で終端が最大値のときのフラグだけ |
| P6 | 形の違う範囲の比較は型エラーになる |
| P7 | ジェネリック1本になる |
| P8 | 受け手の形を問わない共通の下げ方になる |

格納サイズ（境界が i32 のとき、windows-x64）:

| 値 | 現行 | 変更後 |
| --- | --- | --- |
| `^1` | 16 B | 4 B |
| `1..^1` | 40 B | 8 B |
| `0..10` | 12 B | 8 B |
| `n..` | 40 B | 4 B |
| `..` | 40 B | フィールドなし |

性能面では、ほかに次の効果がある。

- `0 <= n <= L` は、同じ幅なら符号なし比較1回で済む。
- 位置の既定が isize なので、添字で幅を変換しなくてよい。

### 4.3. 検討した代替案

| 案 | 不採用の理由 |
| --- | --- |
| 「半開区間への統一と ClosedRange」（2026-09-29） | `..=b` と、Index を含む閉区間が書けない。P2・P3・P4・P7 が残る |
| 同じ型引数の `Range<B>` と、実行時の方向を持つ Index | 混在形 `1..^1` で Index 化が要り、構築時の失敗（P2）が残る。解決に方向の分岐が残る |
| Contract の既定実装 | 要件を `tryResolve` だけにすれば不要になる。範囲のためだけに言語規則を増やさない |
| `Position` をユーザーに開く | 解決の結果を信頼できなくなり、範囲の解決で再検査が要る。整数の適合は、どのみち組み込みになる |
| 要素添字は isize だけのままにする | 範囲の境界と要素添字で受け付ける型が違う、という例外（P4）が残る |
| 整数型の違う両端を許す | 反復できない整数範囲ができ、二項演算の規則ともずれる |
| 位置のリテラルの既定を i32 のままにする | 長さの文脈（isize）とずれ、添字で幅の変換が要る |

### 4.4. 副作用

- **形の違う範囲に共通の型がない**: 分岐・配列・フィールドで混在できない。保存には `ResolvedRange`、受け渡しにはジェネリック、混在には利用側の enum を使う。
- **型の表記が長い**: 整数範囲は `Range<i32, i32>` と書く。
- **実行時に方向を選ぶ位置の型がない**: 利用側で分岐する。
- **失敗の時点が遅れる**: `^n` と `n..` は、構築では失敗しない。使う時点で失敗する。
- **反復の既定の型が変わる**: `for x in 0..10` の x は isize になる。
- **反復できない範囲がある**: `FromEnd` を含む範囲と、片側の範囲は反復できない。
- **コードサイズ**: ジェネリックの API は、形と境界の型ごとに具体化される。
- **引数の型の変化**: 位置の引数が Position に広がるので、`swap(^1, 0)` なども受け付ける。

## 5. Impact on SPEC.md — SPEC.md への影響

| 箇所 | 変更 |
| --- | --- |
| `docs/SPEC.md` の宣言索引、§3 の Copy の表 | `Index`・`IndexRange`・`Range<T>` を削除する。`FromEnd<T>`・6つの範囲型・`Position`・`PositionRange` を追加する |
| §3.5.3 | 位置の読み取り |
| §4.6.1–4.6.4 | 本書 §2.1–2.5 で置き換える |
| §4.6.6、§4.7.2 | 位置と範囲の API（§2.6.2） |
| §4.6.8 | 表現と性能の文言 |
| §4.6.9、§10.6 | 列は組み込みの添字規則とする。Indexable への適合は isize だけにする |
| §8.3・§8.7 | 型同一性の要件 |
| §8.4 冒頭、§8.4.7、§8.4.7.3 | `Indexable<Index>` の例、`Position`、PrimitiveInteger による含意、`sum` の例 |
| §10.2・§10.5 | 位置の読み取りによる推論 |
| §12.3.1 | リテラル式 |
| §13.2 | 接頭辞 `^` |
| §14.6.2 | 反復の Subject |
| §15.1.3 | ConstantIndexExpression の表 |
| §17.3.1 | 失敗の例 |
| §22.1 | 必須宣言の表 |
| 付録 E・F | 用語と文法 |

## 6. Decision — 最終決定

### 6.1. 決定

- §2 の仕様と §3 の変更を採用する。
- 「半開区間への統一と ClosedRange」（2026-09-29）は取り込まない。

### 6.2. 実装順序

1. **型同一性の要件**（一般規則）。
2. **`Position`・`FromEnd<T>`・`PositionRange` の追加**: 位置と範囲の API をジェネリックにする。移行中に限り、Kimi は `Index` を Position に、既存の範囲型を PositionRange に適合させる。
3. **構文の切り替え**: `^x` を FromEnd に、範囲を6つの形の型に切り替える。リテラル式、位置の読み取り、反復も実装する。`Index`・`IndexRange`・`Range<T>` を削除する。
4. **下げ方の共通化**: 直接選択を部品化し、受け手の制限を撤廃する。

各単位は、`./scripts/verify.ps1 -Class ...` と、関連する O0・O2 の fixture・Milestone で検証してからコミットする。セッションの最後に `./scripts/verify.ps1 -Mode Session` を実行する。

### 6.3. 検証

- **正しさ**:
  - 位置: 全整数型と `FromEnd` を、境界値（0・L・L + 1・負数・型の最大値）で解決する。
  - 範囲: 形ごとの解決結果、逆転（`2..=1` を含む）。
  - 失敗: try API が None を返し、Abort 版が Abort すること。
  - 型: リテラル式の型、整数型の不一致、型同一性による反復、参照を通した位置。
  - 添字: 受け手の形を問わない添字、静的 Move Path。
- **性能**: 次を変更前後で比べ、最小値の悪化が 3% 以内であること。O2 の生成コードと格納サイズを記録する。
  - `values[a..b]`・`values[1..^1]`・`s.trySlice(r)`
  - `values[^1]`
  - `for i in 0..n`・`for i in 0..=n`
