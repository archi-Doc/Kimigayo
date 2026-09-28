# 仕様変更案：半開区間への統一と ClosedRange

日付: 2026-09-29

状態: 提案。正式仕様への取り込み・実装・検証は未実施。

基準: 「整数範囲と位置範囲」（2026-09-28）を取り込んだ後の SPEC §4.6.2–§4.6.4。本書が変更しない事項には既存仕様を適用する。

## 1. Current Specification — 現在の仕様

- **包含の表し方**: `..`（半開）と `..=`（閉）は同じ型の値を作り、フィールド `isInclusive: bool` で区別する。対象は `Range<T>`（`start: T`・`end: T`）と `IndexRange`（`start: Index`・`end: Index`）である。`ResolvedRange` は常に半開である。
- **Index**: 格納フィールド `offset: isize`・`isFromEnd: bool` を公開する。
- **種別**: 両端が整数なら `Range<T>`。Index の境界か省略があれば `IndexRange` で、`..=b`（開始の省略）もこちらになる。包含の終端は省略できない。
- **等価性**: `isInclusive` も比較するので、`0..3 != 0..=2` となる。
- **解決**: 半開は `0 <= s <= e <= L` のとき `[s, e)`、包含は `0 <= s <= e < L` のとき `[s, e + 1)`。
- **実装**:
  - `resolve`・`tryResolve` は、実行時に `isInclusive` で分岐する。
  - 反復入口は、フラグを共用の `RangeIterator<T>` へ渡す。
  - 範囲の整数境界は、`Index.unchecked` が不正値を offset -1 の番兵にしておき、`IndexRange.init` が左から検査して Abort する。

## 2. Proposed Specification — 新しい仕様

### 2.1. 型と共通規則

| 型 | 区間 | 構文 | 反復 |
| --- | --- | --- | --- |
| `Range<T>` | 半開 `[start, end)` | 両端が整数の `a..b` | `RangeIterator<T>` |
| `ClosedRange<T>`（新設） | 閉 `[start, end]` | 両端が整数の `a..=b` | `RangeIterator<T>` |
| `IndexRange` | 半開 | Index の境界を含む `a..b`、`a..`・`..b`・`..` | なし |
| `ResolvedRange` | 半開 | 変更なし | `RangeIterator<isize>` |

1. **包含の有無は、値ではなく型が表す。** どの範囲型も包含フラグを持たない。
2. **位置の範囲は半開とする。** 閉区間は、整数値の範囲 `ClosedRange<T>` だけとする。
3. **`start`・`end` は、書いた境界そのものとする。** `ClosedRange<T>` の `end` は区間に含まれる。

### 2.2. 構文と種別

| 構文 | 結果 |
| --- | --- |
| 両端が整数の `a..b` / `a..=b` | `Range<T>` / `ClosedRange<T>` |
| Index の境界を含む `a..b`、`a..`・`..b`・`..` | `IndexRange` |
| Index の境界を含む `a..=b` | 型エラー（位置の範囲は半開） |
| `..=b`・`..=` | 構文エラー（`..=` には両端が必要） |

```ebnf
RangeExpression      := OrExpression
                      | OrExpression? ".." OrExpression?
                      | OrExpression "..=" OrExpression
```

`Range<T>` の型規則（両端で同じ T、リテラル式への伝播、Scalar read）は、`ClosedRange<T>` にもそのまま適用する。両端がリテラル式の `a..=b` は、既定で `ClosedRange<i32>` になる。期待型に適合するのは、演算子に対応する型だけとする。

```kimi
let a = 0..10             // Range<i32>
let b = 0..=255@u8        // ClosedRange<u8>。半開では書けない。
let c = 1..^1             // IndexRange
let d = 1..=^2            // エラー: 位置の範囲は半開。
let e = ..=3              // 構文エラー: 両端が必要。
let f = (0..3) == (0..=2) // エラー: 型が異なる。
```

### 2.3. `ClosedRange<T>`

`Range<T>` と次の点だけが異なる。

- **区間**: `end` を含む。
- **解決**: `0 <= s <= e < L` のとき `[s, e + 1)`。
- **反復**: `end` まで生成し、T の最大値でも加算しない。

次の点は `Range<T>` と同じである。

- `T is PrimitiveInteger` で、Owned・Copy・Equatable。等価性は `start` と `end` で決める。
- 公開メンバーは、読み取り専用の `start`・`end`、`resolve`・`tryResolve`、反復の三入口とする。
- 生成は範囲構文だけとする。
- 逆転（`start > end`）した値も作れ、反復の入口で Abort する。空の閉区間はない。
- `length`・`isEmpty` は設けない。

### 2.4. 解決とスライス

- **解決**: `Range<I>`・`IndexRange` は半開の条件、`ClosedRange<I>` は §2.3 の条件で解決する。
- **スライス**: `x[r]` と `trySlice(r)` は `ClosedRange<I>` も受け付ける。`Slice<T>` には `trySlice<I>(range: ClosedRange<I>)` を加える。
- **削除する記述**: 「包含の終端 `^0` は無効」。もう書けないためである。

### 2.5. `Index` のプロパティ

`offset: isize`・`isFromEnd: bool` を、格納フィールドから読み取り専用のプロパティ（getter）に変える。

- **変わらないもの**: 取得する値と、`init`・`resolve`・`tryResolve`・等価性。
- **変わるもの**: 格納場所への借用は提供しない。`@ref` は、§11.2.3 の getter 一時値の共通規則に従う。

```kimi
inspect(index.offset@ref) // OK: 呼び出し中だけの一時値の借用。
let view = index.offset@ref
inspect(view)             // エラー: 初期化子の一時値は終了している。
```

格納の方式は実装仕様で定める（§3.3）。

## 3. Changes — 変更点

### 3.1. 仕様の変更

| 項目 | 現行 | 変更後 |
| --- | --- | --- |
| `Range<T>.isInclusive`・`IndexRange.isInclusive` | あり | 削除 |
| 両端が整数の `a..=b` | `Range<T>`（包含） | `ClosedRange<T>` |
| Index の境界を含む `a..=b` | `IndexRange`（包含） | 型エラー |
| `..=b` | `IndexRange` | 構文エラー |
| `(0..3) == (0..=2)` | false | 型エラー |
| `Index.offset`・`isFromEnd` | 格納フィールド | getter（値は同じ。格納場所の借用なし） |

### 3.2. 修復候補

修復候補は、§23.5.3 に従って前提と保証を明示する。

位置の閉区間から半開への書き換えが保証するのは、「元の範囲が成功する長さでは、同じ区間を選ぶ」ことまでである。失敗条件は保存しない。元が失敗する一部の場合で、書き換え後は成功する。

| 現行のコード | 候補 | 前提と差異 |
| --- | --- | --- |
| `a..=^n`（n を書いたもの） | `a..^(n - 1)`、n が 1 なら `a..` | 前提は n >= 1。1 だけの逆転（長さ 3 の `^1..=^2` が `^1..^1`）と、n = L + 1（長さ 0 の `0..=^1` が `0..`）は、失敗から空区間の成功に変わる |
| `Index.init(i)..=Index.init(j)` | `Index.init(i)..Index.init(j + 1)` | 前提は j + 1 が溢れないこと。i = j + 1 の逆転は、失敗から空区間の成功に変わる |
| `..=b`（b が整数） | `0..=b` | 結果は `ClosedRange<T>`。負の b は、Index化での Abort ではなく解決の失敗になる（`trySlice` では None） |
| 終端が Index の値の `..=` | なし | 方向が実行時に決まるので、共通の書き換えがない |
| `Range<i32>` の引数に `0..=2` | `0..3`、または `ClosedRange<i32>` のオーバーロード | ― |

### 3.3. 実装仕様への追加（§21.2）

- **符号化**: `Index` の格納は `isize` 1 個とする。先頭相対の n は `n`、末尾相対の n は `~n`（= `-1 - n`）で表す。
- **番兵がない**: すべてのビットパターンが有効な Index を表すので、不正値の番兵は確保できない。
- **方向判定**: 符号の検査に置き換わる。解決に必要な `length >= 0` と `offset <= length` の検査は残る。
- **`^x` の下げ**: この表現を直接書き込む。

### 3.4. 修正が必要な箇所

| 対象 | 内容 |
| --- | --- |
| `src/Kimi/Library/Core.kimi` | `Range<T>`・`IndexRange` から `isInclusive`・`through`・`upTo` を削除し、`tryResolve` を半開専用にする。`ClosedRange<T>` を追加する。`Index` を単一表現にし、`unchecked` を廃止する（§3.5 項4） |
| `src/Kimi/Library/Slice.kimi` | `trySlice<I>(range: ClosedRange<I>)` |
| `KimiDeclarationId`・`KimiLibraryCatalog`・`KimiLibrary` | `ClosedRange` の宣言IDとシンボル |
| `KimiLibraryValidation.cs` | `Range`・`IndexRange` のフィールド数、`ClosedRange` の検査の新設、`Index` の getter |
| `RangeKoto`・パーサー | `..=` の開始を必須にする |
| `Binding.Ranges.cs` | `..=` から `ClosedRange` を作る。Index の境界を含む `..=` を拒否する。`IndexRange` の形を `between`・`from`・`to`・`all` にする。範囲キーに `ClosedRange` を加える。境界のIndex化を改める（§3.5 項4） |
| `Binding.Expressions.cs`・`Binding.Calls.cs` | リテラル式の範囲の既定・適合と Scalar read で、範囲型を演算子と型シンボルの対応で判定する |
| `BodyLowering.Sequences.cs` | `FromEnd` の単一表現への書き込みとレイアウト検査。半開の直接選択は変えない |
| 診断 | §3.2 の候補を、前提と保証つきで出す |
| テスト | `IntegerRangeTest`・`RangeValueTest`・`RangeIndexParseTest`・`ExpressionPrecedenceTest`・`KeyedIndexingTest`・`SliceOperationsTest`、`test-milestone27.ps1`（`1..=^2`）、Playground。範囲と `Index` のレイアウトが現れるネイティブ fixture は全件を走査する |
| 文書 | `docs/LIBRARY.md`、`docs/impl/21-layout-runtime-and-code-generation.md`、`docs/impl/appendices/A-compiler-requirements.md`。`docs/STATUS.md` は対応範囲の変化に合わせて更新する |

### 3.5. 実装上の問題点

1. **期待型への適合の混同。** `RangeElement` は `Range<T>` の T を返すだけである。これを広げると、`0..=3` が `Range<i32>` の引数に適合してしまう。演算子と型の対応は 1 か所で判定する。
2. **合成呼び出しの再利用。** `rangeCalls` はノードごとに再利用される。編集で `..` と `..=` が入れ替わったときに、再束縛で宣言が切り替わることをテストで確かめる。
3. **開始を省略した `..=`。** 現在は構文として受理し、束縛で終端の欠落だけを拒否している。構文エラーに移すと、パーサーの回復と `ExpressionPrecedenceTest` の `..=end + 1` が変わる。
4. **境界のIndex化の順序。** 単一表現では番兵が使えない。そこで範囲式を「両境界の評価 → 整数境界の左からのIndex化 → 構築」の順で処理し、不正な Index も、失敗を運ぶ `Option` も作らない。
   - Index化を後回しにする必要があるのは、開始が整数の 2 境界（整数と整数、整数と Index）だけである。これらは、境界を元の型で受け取る内部生成関数で構築し、関数の中でIndex化する。
   - その他の整数境界は、評価の直後にIndex化しても順序は同じである。
   - 接頭辞 `^` が評価中にIndex化する規則は変えない。
   - 構築時の検査が不要になり、現行の「番兵 → 再検査」より処理が減る見込みである。生成コードで確かめる。
5. **レイアウトの変更。** `Range<T>`・`IndexRange`・`Index` のレイアウトが変わる。1 系統の実行では古い期待値が隠れるので、全 fixture を走査する。
6. **作業順序。** 進行中の整数範囲の実装と同じファイルを変更する。その実装単位をコミットしてから着手する。

## 4. Rationale — 変更する理由

### 4.1. 目的

- 範囲値と `Index` の格納サイズを減らす。
- 包含の有無を型で表し、局所的に読めるようにする（原則 2・3）。
- 位置の範囲を半開に統一する。`^0` を終端の境界とする Index の考え方と、`ResolvedRange` とに揃えるためである。

### 4.2. 理由

1. **格納サイズ。** windows-x64-v1 の配置規則（実装仕様 §21.1.3・§21.1.5）による値を示す。配列では stride がそのまま減る。一方、`Option` や外側の構造体では、タグと切り上げのために削減の一部または全部が消えることがある。

   | 型 | 単体 | `Option<…>` |
   | --- | --- | --- |
   | `Range<u8>` | 3 → 2 B | 8 → 8 B |
   | `Range<i32>` | 12 → 8 B | 16 → 12 B |
   | `Range<i64>` | 24 → 16 B | 32 → 24 B |
   | `Index` | 16 → 8 B | 24 → 16 B |
   | `IndexRange` | 40 → 16 B | 48 → 24 B |

2. **解決の分岐の除去。** `resolve`・`tryResolve` は型ごとの処理になり、包含の判定が消える。
3. **反復への効果は測定で確かめる。**
   - 入口は、包含しないことを定数で Iterator に渡す。直接書いた `for` では現行でも定数になり得るが、保存・受け渡しされた範囲でも定数になる。
   - Iterator を保存・返却した場合、共用型の状態は残り得る。
4. **閉区間は型として残す必要がある。** `0..=255@u8` や `T.min..=T.max` は、終端 + 1 が溢れるので半開では書けない。
5. **位置の閉区間は、要素集合なら半開で表せる。** 違いは閉区間に固有の失敗条件（1 だけの逆転、終端が要素でないこと）だけであり、そのために型を増やす用途は乏しい。
6. **等価性の落とし穴の除去。** `(0..3) == (0..=2)` が黙って false になっていたが、型エラーになる。

### 4.3. 検討した代替案

| 案 | 内容 | 不採用の理由 |
| --- | --- | --- |
| A. `IndexRange` の `..=` を構築時に正規化する | `a..=k` を `a..(k + 1)` に、`a..=^n` を `a..^(n - 1)` にする | 言語の意味として、失敗を成功に変えてしまう（§3.2 の差異）。これは「逆転を空にしない」規則と、`ClosedRange<T>` の挙動に反する。`..=^0` の失敗が構築時に移るので、`trySlice` が None を返せない。`start`・`end` も書いた境界と異なる |
| B. `ClosedIndexRange` を新設する | 位置の閉区間型を設ける | 失敗条件は保てるが、型とスライス API が増える。§4.2 の 5 のとおり用途が乏しい |
| C. 閉区間専用の反復器を設ける | `ClosedRangeIterator<T>` | 型と API の増加に見合う効果が未確認なので、今回は共用する。フラグを読むのは、半開では終了時の 1 回、閉区間では最終要素とその後の終了確認である |
| D. フラグを残してビットを詰める | 境界の余りビットを使う | T は全値域を使うので、余りビットがない |

### 4.4. 副作用

- 両方の範囲を受け取る API には、オーバーロードが 2 つ要る。Kimi の中では `trySlice` だけである。範囲型を束ねる Contract は対象外とする。
- 空の閉区間は書けない。`0..=n - 1` は n = 0 のとき逆転して Abort する（現行と同じ）。空になりうる場合は `0..n` を使う。
- `..=b` と、Index の境界を含む閉区間は書けなくなる。§3.2 の候補は、失敗条件までは保たない。
- `index.offset@ref` を束縛に保存するコードは、値を先にローカルへ保存する必要がある。
- 等価比較や引数の型不一致が、新たにコンパイルエラーになる。

## 5. Impact on SPEC.md — SPEC.md への影響

| 箇所 | 変更 |
| --- | --- |
| `docs/SPEC.md`（Kimi の宣言一覧） | `ClosedRange<T>` を追加する |
| §3（Copy の表） | `ClosedRange<T>` を追加する |
| §4.6.2 | `offset`・`isFromEnd` を読み取り専用のプロパティ（getter）とする |
| §4.6.3 | 型の表に `ClosedRange<T>` を加え、§2.1 の共通規則を加える |
| §4.6.3.1 | 構文の表・種別の表・`..=` の制限・例 |
| §4.6.3.2 | 題を「`Range<T>` and `ClosedRange<T>`」とし、§2.3 の差分を記す。`isInclusive` を削除し、等価性を改める。節番号は変えない |
| §4.6.3.3 | `isInclusive` を削除し、等価性を改める |
| §4.6.3.5 | 反復の表に `ClosedRange<T>` を加える |
| §4.6.4 | 解決の表を型で分ける。包含の終端 `^0` の記述を削除する。スライスが受け付ける型を改める |
| §12.3.1 | `..=` の既定を `ClosedRange<i32>` とし、適合を演算子に対応する型に限る |
| §14.6.2 | Subject の表に `ClosedRange<T>` を加える |
| §22.1 | 必須宣言の表に `ClosedRange<T>` を加える。`Index` の「read-only fields」を「read-only properties」にする |
| 付録 E・F | 用語と文法（§2.2） |
| 実装仕様 §21.2 | §3.3 の `Index` の表現 |

## 6. Decision — 最終決定

採用する:

1. `Range<T>`・`IndexRange` から `isInclusive` を削除し、半開区間専用にする。
2. `ClosedRange<T>` を新設し、両端が整数の `a..=b` はこれを作る。
3. `..=` は、両端とも整数の場合に限る。
4. 反復器は `RangeIterator<T>` を共用する。
5. `Index` の格納を `isize` 1 個にし、`offset`・`isFromEnd` を getter にする。範囲の整数境界は、番兵を使わない順序でIndex化する（§3.5 項4）。

採用しない: §4.3 の案 A–D。範囲型を束ねる Contract と、降順・刻みの範囲は対象外とする。

実装順序:

1. `ClosedRange<T>` の新設、`..=` の切り替え、`Range<T>.isInclusive` の削除
2. `IndexRange.isInclusive` の削除と `..=` の制限
3. `Index` の単一表現と、境界のIndex化の順序の変更

各単位は `./scripts/verify.ps1 -Class ...` と、関連する O0・O2 の fixture で検証してからコミットする。

**正しさの検証**:

- `Index` の表現の境界: `0`・`^0`・`Index.init(isize.max)`・`^isize.max` について、生成・取得・等価・解決を確かめる。
- Index化の順序と Abort。
- §3.2 の各候補の前提と差異。

**性能の検証**: 各単位の直前のコミットを基準とする。

| 観点 | 対象 |
| --- | --- |
| 直接の使用 | `for i in 0..n`・`0..=n`、`values[a..=b]`・`values[1..^1]`・`s.trySlice(1..^1)` |
| 保存と受け渡し | 範囲の配列の走査とコピー、保存した範囲からの反復、関数から返した Iterator の反復 |
| 長さ | 空・1 要素・8 要素・10^6 要素 |
| Index | 実行時に方向が決まる Index の解決と、`offset` の取得 |

- **指標と合格条件**:
  - 時間: 最小値が、基準より 3% を超えて悪化しない。
  - 格納サイズ（単体・`Option`・配列の stride）: 基準以下である。
  - O2 の生成コード: 検査数などを記録し、差の分析に使う。単独の合格条件にはしない。
  - O0 の時間: 記録だけとする。
- **測定条件**:
  - 入力は実行時に与える。結果は計測の外で検証し、測定対象が生成コードに残ることを確かめる。
  - 短い処理は、多数回をまとめて 1 回の計測とする。
  - ビルド条件をそろえ、ウォームアップ 3 回の後、基準版と変更版をコアを固定して交互に各 21 回測る。
