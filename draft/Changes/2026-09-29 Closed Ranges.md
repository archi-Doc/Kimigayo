# 仕様変更案：半開区間への統一と ClosedRange

日付: 2026-09-29

状態: 提案。正式仕様への取り込み・実装・検証は未実施。

基準: 「整数範囲と位置範囲」（2026-09-28）を取り込んだ後の SPEC §4.6.2–§4.6.4。本書が変更しない事項には既存仕様を適用する。

## 1. Current Specification — 現在の仕様

範囲構文 `..`（半開）と `..=`（閉）は同じ型の値を作り、`isInclusive: bool` で区別する。

| 型 | フィールド | x64 のサイズ |
| --- | --- | --- |
| `Range<T>` | `start: T`・`end: T`・`isInclusive: bool` | `u8` 3 B、`i32` 12 B、`i64` 24 B |
| `IndexRange` | `start: Index`・`end: Index`・`isInclusive: bool` | 40 B（`Index` は `isize` と `bool` で 16 B） |
| `ResolvedRange` | `start: isize`・`end: isize` | 16 B。常に半開 |

- **種別**: 両端が整数なら `Range<T>`。Index の境界か省略があれば `IndexRange` になり、`..=b`（開始の省略）も `IndexRange` になる。包含の終端は省略できない。
- **等価性**: `isInclusive` も比較するので、`0..3 != 0..=2` となる。
- **解決**: 半開は `0 <= s <= e <= L` のとき `[s, e)`、包含は `0 <= s <= e < L` のとき `[s, e + 1)`。`tryResolve` は包含の終端 `^0` に対して None を返す。
- **反復**: `RangeIterator<T>.starting(start, end, inclusive)`。`includesEnd` を持つので、T の最大値でも加算しない。
- **実装**: `resolve`・`tryResolve`・反復入口が、実行時に `isInclusive` で分岐する。

## 2. Proposed Specification — 新しい仕様

### 2.1. 型の構成

| 型 | 区間 | 構文 | 反復 | x64 のサイズ |
| --- | --- | --- | --- | --- |
| `Range<T>` | 半開 `[start, end)` | 両端が整数の `a..b` | `RangeIterator<T>` | 2 × T（`i32` 8 B、`i64` 16 B） |
| `ClosedRange<T>`（新設） | 閉 `[start, end]` | 両端が整数の `a..=b` | `RangeIterator<T>` | 2 × T |
| `IndexRange` | 半開 | Index の境界を含む `a..b`、`a..`・`..b`・`..` | なし | 32 B（§2.6 を採用すれば 16 B） |
| `ResolvedRange` | 半開 | 変更なし | `RangeIterator<isize>` | 16 B |

共通規則:

1. **包含の有無は、値ではなく型が表す。** どの範囲型も包含フラグを持たない。
2. **位置の範囲は半開とする。** `IndexRange` と `ResolvedRange` がこれにあたる。閉区間は、整数値の範囲 `ClosedRange<T>` だけとする。
3. **`start`・`end` は、書いた境界そのものとする。** `ClosedRange<T>` の `end` は区間に含まれる。

### 2.2. 構文と種別

| 構文 | 結果 |
| --- | --- |
| 両端が整数の `a..b` | `Range<T>` |
| 両端が整数の `a..=b` | `ClosedRange<T>` |
| Index の境界を含む `a..b`、`a..`・`..b`・`..` | `IndexRange` |
| Index の境界を含む `a..=b` | 型エラー（位置の範囲は半開） |
| `..=b`・`..=` | 構文エラー（`..=` には両端が必要） |

```ebnf
RangeExpression      := OrExpression
                      | OrExpression? ".." OrExpression?
                      | OrExpression "..=" OrExpression
```

- `Range<T>` の型の規則は、`ClosedRange<T>` にもそのまま適用する（両端が同じ T であること、リテラル式への伝播、Scalar read）。
- 両端がリテラル式の `a..=b` は、既定で `ClosedRange<i32>` になる。期待型に適合するのは、演算子に対応する型だけとする。`0..=3` は `Range<X>` に適合せず、`0..3` は `ClosedRange<X>` に適合しない。

```kimi
let a = 0..10             // Range<i32>
let b = 0..=255@u8        // ClosedRange<u8>。半開では書けない。
let c = 1..^1             // IndexRange
let d = 1..=^2            // エラー: 位置の範囲は半開。1..^1 と書く。
let e = ..=3              // 構文エラー: 0..=3 と書く。
let f = (0..3) == (0..=2) // エラー: 型が異なる。
```

### 2.3. `ClosedRange<T>`

- `T is PrimitiveInteger` の整数範囲。Owned・Copy・Equatable で、等価性は `start` と `end` で決める。
- 公開メンバーは、読み取り専用の `start: T`・`end: T`、`resolve`・`tryResolve`、反復の三入口とする。
- 生成は範囲構文だけとし、公開の `init`・factory は設けない。逆転した値（`start > end`）も作れる。
- 空の閉区間はない。`start > end` は逆転として扱う。`length`・`isEmpty` を設けない理由は `Range<T>` と同じである。

### 2.4. 解決とスライス

| 型 | 成功条件 | 結果 |
| --- | --- | --- |
| `Range<I>`・`IndexRange` | `0 <= s <= e <= L` | `[s, e)` |
| `ClosedRange<I>` | `0 <= s <= e < L` | `[s, e + 1)` |

- `x[r]` と `trySlice(r)` は `ClosedRange<I>` も受け付ける。`Slice<T>` には `trySlice<I>(range: ClosedRange<I>)` を加える。
- 「包含終端 `^0` は無効」という記述は削除する。包含終端の `^0` は、もう書けないためである。

### 2.5. 反復

- `Range<T>`・`ClosedRange<T>`・`ResolvedRange` は、いずれも `RangeIterator` を返す。`ClosedRange<T>` は `start` から `end` までを、`end` を含めて昇順に生成する。
- 入口で `start > end` なら Abort する。閉区間でも同じである。
- `RangeIterator` の内部状態は規定しない。

### 2.6. `Index` の単一表現（性能改善）

公開 API は変えずに、`Index` の格納を `isize` 1 個にする。変えない API は、`init`・`offset`・`isFromEnd`・`resolve`・`tryResolve`・等価性である。

- 格納値は、先頭相対の n なら `n`、末尾相対の n なら `~n`（= `-1 - n`）とする。n は 0 以上なので、符号だけで方向が決まり、両者は重ならない。
- `offset` と `isFromEnd` は読み取り専用の計算プロパティとする。
- サイズは `Index` が 16 B から 8 B、`IndexRange` が 40 B から 16 B になる。等価比較は 1 回の比較で済み、解決は符号による 1 回の分岐で済む。

## 3. Changes — 変更点

### 3.1. 仕様の変更

| 項目 | 現行 | 変更後 |
| --- | --- | --- |
| `Range<T>.isInclusive`・`IndexRange.isInclusive` | あり | 削除 |
| 両端が整数の `a..=b` | `Range<T>`（包含） | `ClosedRange<T>` |
| Index の境界を含む `a..=b` | `IndexRange`（包含） | 型エラー |
| `..=b` | `IndexRange` | 構文エラー |
| `(0..3) == (0..=2)` | false | 型エラー |
| `Index` の `offset`・`isFromEnd` | 格納フィールド | 計算プロパティ（値は同じ） |

### 3.2. 移行と修復候補

| 現行のコード | 移行先 | 前提と差異 |
| --- | --- | --- |
| `values[..=b]` | `values[0..=b]` | 結果が `ClosedRange<T>` になり、負の b はIndex化のAbortではなくスライスで失敗する（`trySlice` では None） |
| `a..=^n` | `a..^(n - 1)`（n が 1 なら `a..`） | n >= 1 が前提。n = 0 は現行でも常に失敗する |
| `Index.init(i)..=Index.init(j)` | `Index.init(i)..Index.init(j + 1)` | j + 1 が溢れないことが前提 |
| `Range<i32>` の引数に `0..=2` を渡す | `0..3` を渡すか、`ClosedRange<i32>` のオーバーロードを加える | ― |
| `r.isInclusive` | 型で判断する | ― |

これらは、前提と差異を明示した修復候補として診断に載せる。

### 3.3. 修正が必要な箇所

| 対象 | 内容 |
| --- | --- |
| `src/Kimi/Library/Core.kimi` | `Range<T>`・`IndexRange` から `isInclusive`・`through`・`upTo` を削除し、`tryResolve` を半開専用にする。`ClosedRange<T>` を追加する。`RangeIterator` は共用する。§2.6 は `Index` の格納と `unchecked` |
| `src/Kimi/Library/Slice.kimi` | `trySlice<I>(range: ClosedRange<I>)` |
| `KimiDeclarationId`・`KimiLibraryCatalog`・`KimiLibrary` | `ClosedRange` の宣言IDとシンボル |
| `KimiLibraryValidation.cs` | `ValidRange`・`ValidIndexRange` のフィールド数、`ValidClosedRange` の新設、§2.6 の `Index` 検査 |
| `RangeKoto`・パーサー | `..=` の開始を必須にし、構文エラーと修復候補を出す |
| `Binding.Ranges.cs` | `..=` から `ClosedRange` を作る。Index の境界を含む `..=` を拒否する。`BindIndexRange` の形を `between`・`from`・`to`・`all` にする。`TryBindKeyedSelection` の未解決キーに `ClosedRange` を加える |
| `Binding.Expressions.cs`（リテラル式範囲の既定と適合）・`Binding.Calls.cs`（Scalar read） | 範囲型の判定を、演算子と型シンボルの対応で行う |
| `Binding.Elements.cs`・`BodyLowering.Sequences.cs` の直接選択 | 半開の経路は変えない。閉区間の直接選択は測定で判断する |
| `BodyLowering.Sequences.cs` の `FromEnd` | §2.6 の 1 フィールドへの書き込みとレイアウト検査 |
| テスト | `IntegerRangeTest`・`RangeValueTest`・`RangeIndexParseTest`・`ExpressionPrecedenceTest`・`KeyedIndexingTest`・`SliceOperationsTest`、`test-milestone27.ps1`（`1..=^2`）、Playground、範囲と `Index` のレイアウトが現れるネイティブ fixture |
| 文書 | `docs/LIBRARY.md`、`docs/impl/appendices/A-compiler-requirements.md`。`docs/STATUS.md` は対応範囲の変化に合わせて更新する |

### 3.4. 実装上の問題点

1. **期待型への適合の混同。** `RangeElement` は、`Range<T>` の T を返すだけである。これを `ClosedRange` にも広げると、`0..=3` が `Range<i32>` の引数に適合してしまう。演算子と型の対応は 1 か所で判定する。
2. **合成呼び出しの再利用。** `rangeCalls` はノードごとに再利用される。編集で `..` と `..=` が入れ替わると宣言が変わるので、再束縛で `ClosedRange` 側へ解決し直すことをテストで確かめる。
3. **開始を省略した `..=` の位置づけ。** 現在は構文として受理し、束縛で終端の欠落だけを拒否している。構文エラーに移すと、パーサーの回復と、`ExpressionPrecedenceTest` の `..=end + 1` の期待が変わる。
4. **§2.6 の番兵。** `Index.unchecked` は、offset -1 を「不正」の番兵に使っている。新しい表現では -1 は `^0` を表すので、この番兵は使えない。範囲の境界のIndex化は、検査結果を `Option<isize>` などで運び、`IndexRange` の構築で左から Abort する形に改める。あわせて、`FromEnd` の生成コードとレイアウト検査（2 フィールド前提）も直す。計算プロパティにするので、`index.offset@ref` のような借用はできなくなる。
5. **ネイティブ fixture。** `Range<T>`・`IndexRange`・`Index` のレイアウトが変わる。1 系統の実行では古い期待値が隠れるので、全 fixture を走査する。
6. **作業順序。** 進行中の整数範囲の実装と同じファイルを変更する。その実装単位をコミットしてから着手する。

## 4. Rationale — 変更する理由

### 4.1. 目的

- 範囲値から、包含フラグによるパディングをなくす。
- 包含の有無を型で表し、局所的に読めるようにする（原則 2・3）。
- 位置の範囲を半開に統一する。`^0` を終端の境界とする Index の考え方と、`ResolvedRange` とに揃えるためである。

### 4.2. 理由

1. **サイズ。** `Range<i32>` は 12 B から 8 B、`Range<i64>` は 24 B から 16 B、`IndexRange` は 40 B から 32 B になる（§2.6 を採用すれば 16 B）。配列やフィールドに保存した範囲、`Option` の中の範囲にも同じ比率で効く。
2. **実行時分岐の除去。** `resolve`・`tryResolve`・反復入口にある `isInclusive` の分岐が、単相化によって型ごとの静的なコードになる。
3. **閉区間は型として残す必要がある。** `0..=255@u8` や `T.min..=T.max` は、終端 + 1 が溢れるので半開では書けない。正規化で消すことはできない。
4. **位置の範囲には閉区間が要らない。** 位置の境界は終端の `^0` まで表せるので、どの区間も半開で書ける。`a..=^n` は `a..^(n - 1)` と同じである。
5. **等価性の落とし穴の除去。** `(0..3) == (0..=2)` が黙って false になっていたが、型エラーになる。

### 4.3. 検討した代替案

| 案 | 内容 | 不採用の理由 |
| --- | --- | --- |
| A. `IndexRange` の `..=` を構築時に正規化する | `a..=k` を `a..(k + 1)` に、`a..=^n` を `a..^(n - 1)` にする | 1 だけ逆転した区間が空になる（`^1..=^2` が `^1..^1` になる）。これは「逆転を空にしない」規則に反し、同じ形で Abort する `ClosedRange<T>` とも食い違う。`..=^0` の失敗が解決から構築へ移るので、`trySlice` が None を返せなくなる。`start`・`end` も書いた境界と異なる |
| B. `ClosedIndexRange` を新設する | 位置の閉区間型を設ける | 型とスライス API が増える一方、§4.2 の 4 のとおり表現力は増えない |
| C. 閉区間専用の反復器を設ける | `ClosedRangeIterator<T>` | 要素ごとの処理（比較と加算）は同じである。終端フラグを読むのは最後の 1 回だけなので、型を増やす利点がない |
| D. フラグを残してビットを詰める | 境界の余りビットを使う | T は全値域を使うので、余りビットがない |

### 4.4. 副作用

- 両方の範囲を受け取る API には、オーバーロードが 2 つ要る。Kimi の中では `trySlice` だけである。範囲型を束ねる Contract は、本書の対象外とする。
- 空の閉区間は書けない。`0..=n - 1` は n = 0 のとき逆転し、反復で Abort する（現行と同じ）。空になりうる場合は `0..n` を使う。
- `..=b` と、Index の境界を含む閉区間は書けなくなる。§3.2 の修復候補で機械的に移行できる。
- 等価比較や引数の型不一致が、新たにコンパイルエラーになる。

## 5. Impact on SPEC.md — SPEC.md への影響

| 箇所 | 変更 |
| --- | --- |
| `docs/SPEC.md`（Kimi の宣言一覧） | `ClosedRange<T>` を追加する |
| §3（Copy の表） | `ClosedRange<T>` を追加する |
| §4.6.3 | 型の表に `ClosedRange<T>` を加える。共通規則に「包含は型が表す」「位置の範囲は半開」を加える |
| §4.6.3.1 | 構文の表・種別の表・`..=` の制限・例 |
| §4.6.3.2 | 題を「`Range<T>` and `ClosedRange<T>`」とし、両者を記述する。`isInclusive` を削除し、等価性を改める。節番号は変えない |
| §4.6.3.3 | `isInclusive` を削除し、等価性を改める |
| §4.6.3.5 | 反復の表に `ClosedRange<T>` を加える |
| §4.6.4 | 解決の表を型で分ける。包含終端 `^0` の記述を削除する。スライスが受け付ける型を改める |
| §12.3.1 | `..=` の既定を `ClosedRange<i32>` とし、適合を演算子に対応する型に限る |
| §14.6.2 | Subject の表に `ClosedRange<T>` を加える |
| §22.1 | 必須宣言の表に `ClosedRange<T>` を加える。§2.6 を採用すれば、`Index` の「read-only fields」を「read-only properties」にする |
| 付録 E・F | 用語と文法（§2.2） |

§4.6.2 の `Index` の意味は、§2.6 を採用しても変わらない。

## 6. Decision — 最終決定

採用する:

1. `Range<T>`・`IndexRange` から `isInclusive` を削除し、半開区間専用にする。
2. `ClosedRange<T>` を新設し、両端が整数の `a..=b` はこれを作る。
3. `..=` は両端とも整数の場合に限る。Index の境界を含めば型エラー、開始を省略すれば構文エラーとする。
4. 反復器は `RangeIterator<T>` を共用する。
5. `Index` を `isize` 1 個の表現にする。公開 API は変えない。独立した実装単位とし、測定で退行があれば見送る。

採用しない: §4.3 の案 A–D。範囲型を束ねる Contract と、降順・刻みの範囲は対象外とする。

実装順序:

1. `ClosedRange<T>` の新設、`..=` の切り替え、`Range<T>.isInclusive` の削除
2. `IndexRange.isInclusive` の削除と `..=` の制限
3. `Index` の単一表現

各単位は `./scripts/verify.ps1 -Class ...` と、関連する O0・O2 の fixture で検証してからコミットする。性能は各単位の直前のコミットを基準とし、条件をそろえて交互に実行して測る。対象は、`for i in 0..n`・`for i in 0..=n`、`values[a..=b]`、`values[1..^1]`、`s.trySlice(1..^1)` である。O2 の生成コードで残る検査が増えず、時間の悪化が 3% 以内であることを条件とする。範囲型と `Index` のサイズを記録する。
