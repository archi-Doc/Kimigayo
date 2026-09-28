# 仕様変更案：整数範囲と位置範囲

日付: 2026-09-28

状態: 最終提案。正式仕様への取り込み・実装・動作検証は未実施。

本書は採用後の仕様を定める。取り込みまでは `docs/SPEC.md` とその参照先が正式仕様であり、本書が変更しない事項には既存仕様を適用する。

## 1. 概要

### 1.1. 目的

整数の範囲を直接反復できるようにし、対象の長さに依存する位置の範囲とは型で区別する。範囲の意味は、変数への保存、関数への受け渡し、`for`、添字のどこでも同じとする。

### 1.2. 型の構成

| 型 | 現行仕様からの変更 | 役割 | 生成 | 反復 | `length`・`isEmpty` |
| --- | --- | --- | --- | --- | --- |
| `Range<T>` | 新設 | 整数値の範囲。負数も扱う | 範囲構文のみ | Tを生成 | なし |
| `IndexRange` | 現行の `Range` を改名 | 位置の範囲。先頭相対・末尾相対・省略を含む | 範囲構文のみ | 不可 | なし |
| `ResolvedRange` | 役割を限定 | 対象長に対して検証済みの位置区間 | `indices`・`resolve`・`tryResolve` のみ | isizeを生成 | あり |

補助の宣言として、反復状態 `RangeIterator<T>` と、組み込み整数型だけが満たす要件 `PrimitiveInteger` を新設する。いずれもKimi直下の宣言とする。旧名の互換エイリアス、型引数を省略した `Range`、`Range<Index>` は設けない。

### 1.3. 共通規則

1. **範囲の値は構築時に順序を検査しない。** 順序と対象長は、範囲を使う操作（反復の開始・解決・スライス）が検査する。
2. **スライスは解決した区間を使う。** 範囲を現在の対象長で `ResolvedRange` に解決し、その区間を適用する。
3. **整数からIndexを作る構文は任意の整数型を受け付ける。** `IndexRange` の整数境界と接頭辞 `^` は任意の `PrimitiveInteger` を受け付け、Indexを作る時点で検査する。位置を受け取るAPIは `isize`・`Index` のままとする。

### 1.4. 例

```kimi
let values: [4 of i32] = [10, 20, 30, 40]

for var number in -3..3                     // Range<i32>。-3から2まで。
    Console.writeLine("Count \(number)")

let inner = 1..^1                           // IndexRange
let middle = values[inner]                  // 20と30の共有Slice
let resolved = inner.resolve(values.length) // ResolvedRange [1, 3)

for i in values.indices                     // ResolvedRange。iはisize。
    Console.writeLine("Value \(values[i])")
```

## 2. 型の基盤

### 2.1. リテラル式

**リテラル式**（literal-only expression）は、型を決めるための式の分類である。新しい構文ではなく、範囲以外の式にも適用する。SPEC §4.2 の literal-only subexpressions もこの用語に統一する。

リテラル式は次のいずれかとする。型付きの値、明示変換 `@`、呼び出しを含む式や、配列・辞書リテラルは含まない。

- 未確定の整数リテラル
- 括弧で囲んだリテラル式
- リテラル式だけを被演算子とする組み込みの単項 `+`・`-`、算術・ビット・シフト演算
- 両端がリテラル式の `a..b`・`a..=b`

**適用範囲**: 既存仕様で未確定リテラルを型へ適合させる規則は、リテラル式にもそのまま適用する。候補適合（SPEC §10.2）、比較の相手の型（SPEC §13.4）、Scalar read（SPEC §3.5.3）、分岐結果の共通型（SPEC §14.9.1）、Semanticsを保つ変換（SPEC §10.8）などが該当する。例外は数値変換だけで、変換先に適合するのは直接リテラルに限る（SPEC §13.5.4）。その他の被演算子は独立して型を決めるので、`(200 + 100)@u8` は従来どおり `i32` で計算してから変換する（SPEC §13.5.2）。

リテラル式は、次のように型へ適合する。

- **伝播**: 期待型や候補の型を、各演算子の型規則に従って被演算子へ伝える。算術・ビット演算は両辺、単項演算は被演算子、範囲は両端へ伝える。シフトは左辺にだけ伝え、右辺は独立して型を決める（SPEC §13.3）。
- **適合の条件**: 各リテラルが伝えられた型に収まり（直接付いた符号はSPEC §13.5.4）、各演算子がその型で使えること。例えば `u8` に対する `-(1)` は、単項 `-` が符号付き整数に限られるので適合しない。
- **適合クラス**: 直接の値として適合すればLiteral fitting、一時値の借用などの取得を伴えばその取得に対応する既存のクラスとする（SPEC §10.2.1）。配列リテラルの候補ごとの適合（SPEC §4.3）と同じ扱いである。
- **既定化**: 候補の比較中は既定化せず、既定化で曖昧さを解消しない。期待型・他の引数・配列の兄弟要素・分岐結果から制約を集めた後、他の型情報がない場合だけ既定化する。既定は整数が `i32`、範囲が `Range<i32>`、長さの文脈が `isize` とする。

この分類は型の決定だけに使い、評価の時期や構文上の分類を変えない。

- 通常の式は決まった型で実行時に評価する。定数が見えても、実行時のAbortをコンパイルエラーに変えない。例えば `i8` の `127 + 1` は、各リテラルが適合し、加算が実行時にAbortする。
- 配列長などの必須定数評価は、既存の定数評価規則に従う。`[(4 / 0) of u8]` はコンパイルエラーのままとする。
- 長さの式で使える演算（SPEC §4.2）、固定配列の定数添字（SPEC §15.1.3 の ConstantIndexExpression）、Literal Pattern（SPEC §14.8.1）は広げない。`2 * 5` はパターンに書けない。

```kimi
func choose(value: i32) -> () => ()
func choose(value: i64) -> () => ()
choose(10)     // エラー: 両方に適合する（現行どおり）。
choose(2 * 5)  // エラー: リテラル式なので同じ扱い。

func accept(range: Range<i32>) -> () => ()
func accept(range: Range<i64>) -> () => ()
accept(0..10)  // エラー: 同じ理由。
let r = 0..10  // 独立した式なのでRange<i32>に既定化する。
accept(r)      // Range<i32>側を選ぶ。

func take(range: Range<i32>) -> () => ()
func take(range: ref/Range<i32>) -> () => ()
take(0..3)     // 値の候補を選ぶ。借用の候補は一時値の借用が要るので順位が低い。

let x: i64 = 7
let s: Range<i64> = 0..3
let p = x == 2 * 5                  // 2 * 5 は比較の相手のi64に適合する。
let q = s == (0..3)                 // (0..3) はRange<i64>に適合する。

let wide: Range<i64> = 0..(1 << 40) // 1はi64、シフト数40は独立にi32。
let bad: u8 = 1 << 256              // 256はi32。シフト数の検査で実行時にAbortする。
```

### 2.2. `PrimitiveInteger`

`PrimitiveInteger` は、メソッド要求を持たないコンパイラー内在の要件である（SPEC §8.4.7）。

- **適合する型**: 外側のSemanticsが `owner` で、Coreが `i8`・`u8`・`i16`・`u16`・`i32`・`u32`・`i64`・`u64`・`i128`・`u128`・`isize`・`usize` のいずれかである型だけ。浮動小数点・`char`・参照・ユーザー定義型は適合しない。ユーザー適合や同名宣言では付与できず、正規の宣言IDで識別する。
- **`T is PrimitiveInteger` で証明できること**: 12型に共通する組み込みの能力をすべて使える。
  - **能力と適合**: Scalar・Copy・Owned と、組み込みの比較・整形の適合（Equatable・Comparable・Utf8Format、SPEC §8.7）。参照からはScalar readで値を取得できる。
  - **演算**: 12型すべてに定義された組み込みの整数演算・比較・シフト。単項 `-` は符号付き整数に限られるので使えない。被演算子の型・結果型・失敗条件は既存規則に従う。
  - **変換**: `PrimitiveInteger` を満たす型の間の、明示的な検査付き変換 `value@U`。
  - **リテラル**: 未確定のTへのリテラルの適合は、12型すべてに収まる0〜127に限る。
- `Range<T>`・`RangeIterator<T>` と、それらを未確定のTで使う宣言には `T is PrimitiveInteger` を明記する。署名から制約を逆算しない。

これは新しい証明規則である。既存の選択制約 `T is i8 or u8 or …` からは、SPEC §8.7 に場合分けの証明がないため共通の能力を導けない。一般の場合分け推論や、ユーザーが拡張できる数値Contractは追加しない。

初期実装プロファイルの `i128`・`u128` の除算・剰余の制限は、言語上の保証を弱めない。具体型に確定したコードを最適化前に診断する実装制限として扱い、範囲の構築・反復・解決ではこれらの演算を使わない。

## 3. 範囲構文

### 3.1. 種別の決定

種別は、境界の省略と、独立に判明する境界の型だけで決める。期待型、実行時の値、添字の中かどうかでは変えず、種別を決めるためにリテラル式を既定化しない。境界は `PrimitiveInteger` を満たす型（参照はScalar readで読む）または `Index` とする。

リテラル式でない境界の型が独立に決まらなければ（ジェネリックやオーバーロードされた呼び出しなど）、注釈・明示的な型引数・型付きの中間束縛を求める（SPEC §10.5）。期待型から境界の呼び出しを推論し直さない。

| 構文 | 結果 |
| --- | --- |
| 両端が整数の `a..b`・`a..=b` | `Range<T>` |
| 境界の一方以上が `Index` | `IndexRange`。整数の境界はIndex化する（§3.3） |
| `a..`・`..b`・`..=b`・`..` | `IndexRange`。省略した開始は0、省略した終了は `^0`。包含終端は省略できない |

演算子の優先順位と非結合性は変更しない。`a..b..c` は構文エラー、`(a..b)..c` は境界の型エラーとなる。構文は常に正規のKimi宣言を使い、同名のユーザー宣言には置き換わらない。

```kimi
let a = 1..3                           // Range<i32>
let b = 1..^1                          // IndexRange
let c = ..3                            // IndexRange。0..3とは別の型。
let d: IndexRange = Index.init(1)..Index.init(3)
let e: IndexRange = 1..3               // エラー: Range<i32>を位置に読み替えない。
```

### 3.2. `Range<T>` の型

両端には同じTを要求する。確定した境界の型は、もう一方のリテラル式へ伝える。確定済みの異なる整数型は変換せず、エラーとする。両端がリテラル式なら、範囲全体がリテラル式となる（§2.1）。

```kimi
let a = 0..10              // Range<i32>
let b = 0@i64..10          // Range<i64>
let c: Range<u8> = 0..200  // Range<u8>
let n = 10
let d = 0..n@ref           // Scalar read。Range<i32>
let e = [0..3, 5..8@i64]   // Array<Range<i64>>
let f = 0@i32..1@i64       // エラー: 境界の型が異なる。
```

### 3.3. 評価順とIndex化

範囲式は、左境界、右境界の順に各1回評価する。境界式がAbortすれば、後続は評価しない。

- `Range<T>` は構築時に何も検査しない。境界の評価が終われば構築は成功する。
- `IndexRange` は両端の評価後、整数の境界を左からIndex化する。順序と対象長は解決時に検査する。

**Index化**は、整数値が「0以上かつisizeで表現可能」であることを検査し、先頭相対の `Index` にする。満たさなければAbortする。

- `IndexRange` の整数境界と接頭辞 `^` の被演算子は、任意の `PrimitiveInteger` を受け付ける。リテラル式には `isize` の期待型を伝える。
- `^x` は評価時にxをIndex化し、末尾相対の `Index` を作る。そのため後続の境界より先に失敗する。
- 位置を受け取るAPIは変更しない。`Index.init(offset: isize, fromEnd: bool = false)`、単一要素添字のキー（`isize`・`Index`）、`splitAt`・`tryGet` などの引数は従来どおりとする。単一要素添字のキー型はIndexable Contractの選択で決まるので、構文の規則は及ばない。

```kimi
func sideEffect() -> isize
    return 2

let a: i32 = 1
let b: i32 = 2
let values: [4 of i32] = [10, 20, 30, 40]
let x = values[a..]         // IndexRange。aをIndex化する。
let y = values[a..^b]       // bも^でIndex化する。
let z = values[a]           // エラー: 単一要素添字はisizeかIndex。
let p = (-1)..sideEffect()  // Range<isize>。構築は成功する。
let q = ^(-1)..sideEffect() // ^(-1)でAbort。sideEffectは呼ばない。
```

## 4. 型とAPI

### 4.1. `Range<T>`

- `T is PrimitiveInteger` の整数範囲。Owned・Copy・Equatable。Comparableや範囲の算術は提供しない。
- 公開メンバーは、読み取り専用の `start: T`・`end: T`・`isInclusive: bool`、解決の `resolve`・`tryResolve`（§5.1）、反復の三入口（§4.4）とする。
- 生成は範囲構文だけとし、公開の `init`・factory・`tryCreate` は設けない。逆転した値（`start > end`）も作れる。
- 等価性は、同じTの `start`・`end`・`isInclusive` で決める。`0..3` と `0..=2` は等しくない。
- `length`・`isEmpty` は設けない。要素数がTにも `usize` にも収まらない場合があり、逆転した値もあるためである。

### 4.2. `IndexRange`

- 現行の `Range` を改名した位置の範囲。Owned・Copy・Equatable。
- 公開メンバーは、読み取り専用の `start: Index`・`end: Index`・`isInclusive: bool` と、`resolve`・`tryResolve`（§5.1）とする。
- 生成は範囲構文だけとする。現行の公開 `between`・`from`・`to`・`all`・`through`・`upTo` は内部化する。
- 反復できない。絶対位置だけの値でも同じとする。対象に依存しない `length`・`isEmpty` もない。
- 等価性は、正規化したIndex（方向とオフセット）と `isInclusive` で決める。`..` と `0..^0` は等しい。

### 4.3. `ResolvedRange`

- 対象長に対して検証済みの半開区間。常に `0 <= start <= end` を満たす。Owned・Copy・Equatable（両端で比較）。
- 公開メンバーは、読み取り専用の `start: isize`・`end: isize`・`length: isize`・`isEmpty: bool` と、反復の三入口（§4.4）とする。
- 生成は `indices` と `resolve`・`tryResolve` だけとする。現行の公開 `init(! start:, end:)` は内部化する。
- `values.indices` は、取得時の `[0, values.length)` のスナップショットである。保存後のサイズ変更は反映しない。

3型とも、対象のStorage・Origin・Loanを保持しない。型をまたぐ暗黙変換や等価比較はない。

### 4.4. 反復と `RangeIterator<T>`

`Range<T>` と `ResolvedRange` は、Iterable・UniqIterable・IntoIterable に適合する。

| 範囲 | 三入口の結果 | 生成する値 |
| --- | --- | --- |
| `Range<T>` | `RangeIterator<T>` | `start` から昇順・刻み1 |
| `ResolvedRange` | `RangeIterator<isize>` | 同じ両端の半開 `Range<isize>` と同じ |

- 入口は境界を値として取り込み、元の範囲のStorage・Origin・Loanを結果に残さない。借用入口の `IteratorType(source)` は、sourceによらず同じ型とする。
- `start > end` なら入口でAbortする。空範囲や降順には読み替えない。
- `RangeIterator<T>` はOwned・Non-CopyのIteratorで、`Item` はT、`next(self: uniq/Self)` は `Option<T>` を返す。最終要素の後で加算せず、一度Noneを返した後はNoneのままとする。Iteratorの効果上限（SPEC §22.1.2.4）を満たす。
- 公開コンストラクターと反復入口への適合は設けない。Iteratorを `for` で使うには、既存のアダプター `Kimi.Iteration.owning`・`borrowing` を通す。
- `for var x` でxを再代入しても、生成順序は変わらない。取得・借用・cleanupは既存の反復規則に従う。

```kimi
func makeNumbers() -> RangeIterator<i32>
    let numbers = 0..3
    return numbers.iterate() // numbersの借用を結果に残さない。

for number in Kimi.Iteration.owning(makeNumbers())
    Console.writeLine("Count \(number)")

let count: isize = 0
for i in 0..count - 1        // 0..-1 は逆転。反復の開始でAbortする。
    ()
```

## 5. 解決とスライス

### 5.1. 解決

`Range<I>` と `IndexRange` は、範囲を長さLに対して検査する次の2つを持つ。Lが負なら失敗とする。

- `resolve(length: isize) -> ResolvedRange`: 失敗するとAbortする。
- `tryResolve(length: isize) -> Option<ResolvedRange>`: 失敗するとNoneを返す。

検査は次の手順で行う。

1. 両端を数学的整数 s・e にする。
   - `Range<I>`: `start`・`end` の値そのもの。
   - `IndexRange`: 各Indexで `offset <= L` を確かめ、先頭相対ならoffset、末尾相対なら `L - offset` とする。満たさなければ失敗する。
2. 数学的整数上で区間を検査する。

| 形式 | 成功条件 | 結果 |
| --- | --- | --- |
| 半開 | `0 <= s <= e <= L` | `[s, e)` |
| 包含 | `0 <= s <= e < L` | `[s, e + 1)` |

逆転を空区間として受理しない。成功時の両端は正確にisizeで表せ、包含終端の加算は溢れない。比較回数や命令順序は規定しないが、切り詰めによる誤受理と、`tryResolve` の検査中の変換による余分なAbortは許さない。

### 5.2. スライス

固定配列・Array・Sliceのスライス `x[r]` と `Slice.trySlice(r)` は、`Range<I>`・`IndexRange`・`ResolvedRange` を受け付ける。

- `x[r]` は `x[r.resolve(L)]` と、`s.trySlice(r)` は `r.tryResolve(L)` の結果を適用したものと同じ意味とする。Lは現在の長さである。
- `ResolvedRange` は解決済みなので、`end <= L` だけを検査する。
- 実装は解決と適用を融合してよく、`ResolvedRange` を実体化しなくてよい。受け手と境界を再評価せず、アクセス予約・Loanの規則は既存どおりとする。

`Range<I>` 用の `trySlice` は、具体型ごとのオーバーロードではなく、単一のジェネリック宣言とする。

```kimi
// Slice<T>{source} 内の宣言。
public func trySlice<I>(self: Self, range: Range<I>) -> Option<Slice<T> during source>
    I is PrimitiveInteger
```

- 範囲添字はIndexable系列の外のままとし、ユーザー型へ新しいキー型の適合を自動付与しない。
- raw pointerの添字は単一のisizeだけを受け付け、`Index` と三つの範囲型は受け付けない。
- `[0..10]` は `Range<i32>` を1個含む配列であり、自動展開しない。

### 5.3. 失敗の段階

通常のスライスは失敗するとAbortし、`trySlice` は自身の検査失敗だけをNoneにする。引数の評価中（Index化を含む）のAbortは捕捉しない。

| 操作（n < 0） | 結果 |
| --- | --- |
| `values[-3..3]`・`values[3..1]` | スライスでAbort |
| `s.trySlice(-3..3)`・`s.trySlice(3..1)`・`s.trySlice(0..n)` | None |
| `s.trySlice(Index.init(3)..Index.init(1))` | None |
| `s.trySlice(n..)`・`s.trySlice(..n)`・`s.trySlice(^n..)` | Index化でAbort。`trySlice` は呼ばれない |

外部から受け取った整数は、元の型のまま範囲にして `trySlice` に渡せばよい。

```kimi
func tryWindow<T, I>(values: Slice<T>, ! start: I, end: I) -> Option<Slice<T> during values.source>
    I is PrimitiveInteger
    return values.trySlice(start..end) // 負数・逆転・範囲外はNone。
```

Kimiで書く検査のAbortには `$abort(message)`（`KIMI_E_ABORT`）を使い、Abortコードの再割り当ては行わない。

## 6. 実装と性能

### 6.1. 束縛と生成経路

- `Binding.Ranges.cs` の一律なisize・Index化を、種別の決定・型の決定・Index化に分ける。Tが具体的な整数型か、`PrimitiveInteger` の証明を持つ型に決まってから、宣言IDで特定した内部の生成処理へ下げる。型が決まった後に推論をやり直さない。
- 生成・解決・反復は可能な限りKimiで実装し、通常のIteratorとモノモーフィゼーションを使う。演算・比較・取得は定義時に検査し、具体化時に再束縛しない。
- 内部化するのは、構文からの生成入口、`Index.init(! unchecked:)`、`IndexRange` の旧factory、`ResolvedRange.init` である。コンパイラーが合成する正規の生成処理だけを、宣言IDと内部のアクセス規則で接続する。利用者の呼び出しには権限を与えず、必要な検査が終わる前に公開値を作らない。
- `ResolvedRange` の反復に使っているコンパイラーの特別処理（`Binding.Sequences.cs` などの専用分岐）を除き、`RangeIterator<isize>` を使う。

### 6.2. 位置の正規化

内部関数 `tryPosition<I>(value: I, limit: isize) -> Option<isize>`（`I is PrimitiveInteger`）は、`0 <= value <= limit` なら正確なisize値を、それ以外ならNoneを返す。Index化（limitはisizeの最大値で、コンパイラーが定数引数として渡す）と、`Range<I>` の解決（limitは対象長）で共有する。

- 一般実装は、valueとlimitの負数を除外してからu128で比較し、成功値をisizeへ変換する。除算・剰余は使わない。
- `isize`・`i32`・`usize` には明示的な完全関数特殊化（SPEC §8.8）を用意し、O0でもu128を経由しない。ほかの型の特殊化は測定で判断する。
- 特殊化は一般実装と同じ結果・失敗を守り、不正な一般定義を救済しない。
- 成立済みの条件から導ける検査は繰り返さない。例えば半開の `Range<I>` では、`0 <= start <= end` と `end <= L` を確かめれば、startと対象長の比較は要らない。

### 6.3. 反復と検査除去

`RangeIterator` は、包含区間 `[s, e]` を「半開区間 `[s, e)` の後に `e` を1つ返すもの」として表す。入口の検査（§4.4）の後、`current` と `end` に範囲の両端を写し、`includesEnd` を `isInclusive`（`ResolvedRange` ではfalse）とする。終端の加減算はしない。`ResolvedRange` の入口は順序が保証済みなので、順序を検査しない。

```kimi
// RangeIterator<T>.next の本文例。
let value = self.current
if value < self.end
    self.current = value + 1
    return .Some(value)
if self.includesEnd
    self.includesEnd = false
    return .Some(value) // value == end。最大値でも加算しない。
return .None
```

- `value < end` から `value + 1` が溢れないことを証明できる。通常の検査付き加算を使い、特別なプリミティブは追加しない。物理レイアウトは固定しない。
- 半開の範囲と `ResolvedRange` では `includesEnd` が常にfalseなので、各要素の処理は比較1回と加算になる。
- 検査の統合・除去は、§5.1の意味を保つ限り許す。幅を狭める前に表現可能性を検査する。ソース上の `@usize` による負数のAbortと、内部の符号なし比較は区別する。
- `for i in values.indices` と `for i in 0..values.length` は同じ `RangeIterator<isize>` になる。同じ長さとStorageが保たれると証明できれば、本体の `values[i]` の境界検査を除去できる。評価順・Abort・借用・副作用は変えず、一般のユーザー定義Iteratorには適用しない（SPEC §4.6.8）。

### 6.4. 計算量

固定したTについて、構築・Copy・解決・Index化・反復の開始・各 `next`・破棄はO(1)、n要素の反復はO(1+n)、Iteratorの追加領域はO(1)とする。これら自身のヒープ確保と参照カウント更新はゼロとし、境界式や本体などのユーザー処理は別に数える。インライン化とOptionの中間コピー除去は、既存の最適化条件に従う。性能の判定は§7.3による。

## 7. 取り込みと検証

### 7.1. 変更対象

| 対象 | 変更内容 |
| --- | --- |
| SPEC §3.5.3・§8.4.7・§8.7 | `PrimitiveInteger` の識別・適合・証明規則 |
| SPEC §3.5.3・§4.2・§10.2・§10.8・§12.3.1・§13.4・§14.9.1 | リテラル式の定義と、未確定リテラルの規則の適用・期待型伝播・既定化 |
| SPEC §4.6・§5.3・§13.2 | 3型の契約、範囲構文、Index化、`^` の被演算子型、解決とスライス |
| SPEC §14.6.2・§17.3.1・§22 | 反復の三入口と `RangeIterator`、逆転時のAbort、必須宣言の表 |
| 付録E・F、SPEC索引、§12–15、実装仕様 | 用語の追加と、旧名・関連規則の点検 |
| Kimi・Compiler | Core・Slice・反復の宣言、`Binding.Ranges.cs`・`Binding.Sequences.cs`・推論・制約証明・`BodyLowering.Sequences.cs` など |
| 公開索引・規約 | `docs/LIBRARY.md`、`docs/STYLE.md`、`src/Kimi/Library/README.md` |
| 例・テスト | 範囲・数値推論・反復・スライス・所有権・補間、Milestone 2/7/27/28/32/39など |

### 7.2. 既存コードの移行

型名の一括置換では意味の変更を反映できないので、用途を確かめて更新する。pre-alphaのため、移行用の構文や言語バージョンの分岐は追加しない。

| 現行のコード | 移行先 |
| --- | --- |
| `let r: Range = 1..3` | 数値なら `Range<isize>` など。位置なら `IndexRange` と `Index.init(1)..Index.init(3)` |
| `Range` 型（改名後は `IndexRange`）の引数に `1..3` を渡す | 引数を `Range<I>` にするか、`Index` で書く |
| `ResolvedRange.init(start: 2, end: 5)` | isizeで反復するなら `let r: Range<isize> = 2..5`。スライスには `2..5` をそのまま使う。検証済みの区間や `length` が要るなら `(2..5).resolve(values.length)` とする（対象長の検査が加わる） |
| 両端が整数の範囲の負の境界（現行は構築時にAbort） | スライスで失敗する。`trySlice` ではNoneになる |
| オーバーロードへのリテラル算術（`choose(2 * 5)`） | 適合する候補を既存の順位規則で決められなければ曖昧になる。型を明示する |

### 7.3. 受け入れ条件

| 分類 | 検証事項 |
| --- | --- |
| 名前と宣言 | 構文と宣言の接続、旧名の除去、`Range<Index>` の拒否、内部化した入口へのアクセス拒否 |
| `PrimitiveInteger` | 12型だけの適合、ユーザー適合・参照型の拒否、共通の演算・取得・変換・比較・文字列補間、0・1・127の適合と128・-1の拒否、単項 `-` と制約不足の定義時拒否、128-bit実装制限との区別 |
| リテラル式 | 範囲と範囲以外の両方で、未確定リテラルの各規則（候補適合・比較・Scalar read・分岐結果・Semanticsを保つ変換）が働くこと、曖昧性と既定化（保存の前後を含む）、借用を伴う候補との順位、シフト右辺の独立した型、演算子を使えない型の拒否、リテラル不適合と演算時Abortの区別、必須定数評価でのコンパイルエラー、数値変換への非伝播、Literal Patternを広げないこと |
| 構文と評価 | 種別の決定、型が独立に決まらない境界の拒否、異なる整数型の拒否、評価順、全整数型の境界と `^` のIndex化、負数・表現不能値 |
| 反復 | 全12整数型、負数、空、1要素、逆転時のAbort、最小値・最大値の近傍、包含の最大値、再走査、Iteratorの寿命・Move・Copy拒否、`for var` の再代入、終了後のNone、exit・continue・cleanup |
| 解決とスライス | `Range<I>` と `IndexRange` の解決結果の一致、`^0`・省略、負の長さ、包含境界、逆転を空にしないこと、`ResolvedRange` の再検査、§5.3の失敗段階、借用・アクセス予約・raw pointer |
| コスト | 確保ゼロ、O(1)の初期化と `next`、O0とO2の意味の一致、`tryPosition` の一般実装と特殊化の一致、頻出3型でO0でもu128を使わないこと |

性能は、各手順（§7.4）の直前のコミットを基準とし、その手順が変える処理で判定する。

| 手順 | 測定する処理 | O2の生成コード |
| --- | --- | --- |
| 2 | リテラルを多く含むソースのBind | ― |
| 3 | `values[a..b]`・`values[a..]`・`s.trySlice(a..b)` | 残る検査が基準より増えない |
| 4 | 空・8要素・10^6要素の `values.indices` の反復 | `next` の加算検査が消える。残る検査が基準より増えず、`for i in 0..values.length` も同じ数になる |

- **測定条件**: 実行コードは実行時の入力で動かし、結果を計測の外で検証して、測定する処理が生成コードに残ることを確かめる。短い処理は多数回をまとめて1回の計測とする。基準版と変更版は、ビルド条件・ウォームアップ・測定回数を揃え、コアを固定して交互に実行する。
- **判定**: 同じ測定回数から求めた最小値が、基準より3%を超えて悪化しない。Bindは時間と割当量の両方で判定する。
- **記録のみ**: O0の時間と、Iteratorの状態サイズ。

### 7.4. 実装順序

1. **改名**: 現行の `Range` を `IndexRange` に改名する。意味は変えない。
2. **リテラル式と `PrimitiveInteger`**: 一般規則として実装し、範囲以外の推論で検証する。
3. **`Range<T>` と構文の切り替え**: `Range<T>`・`RangeIterator<T>`・解決・スライスの受理・種別の決定・Index化（`^` を含む）・内部化を実装し、例・Milestone・テストを更新する。
4. **`ResolvedRange` の整理**: 反復を `RangeIterator<isize>` に統一し、`init` の内部化とコンパイラーの特別処理の除去を行う。

各段階は必要に応じて検証単位に分け、§7.3の性能判定を含めて検証する。単位ごとに `./scripts/verify.ps1 -Class ...` と関連するO0・O2のfixture・Milestoneで検証してコミットし、セッションの最後に `./scripts/verify.ps1 -Mode Session` を実行する。NativeAOTは対象外とする。

正式仕様と公開索引は、対応する実装単位で更新する。`docs/dev/PLAN.md`・`docs/dev/PLAN_HISTORY.md` には進捗を記録し、`docs/STATUS.md` は検証済みの対応範囲が変わった時だけ更新する。正式仕様は本書を参照せず自己完結させ、取り込み後に `draft/INTEGRATED.md` へ対象節またはコミットを記録して本書を固定する。本書だけの更新では、正式仕様・実装・STATUSを変更しない。

## 8. 対象外と参照

対象外: 降順・任意刻み・無限の範囲、配列の自動展開、範囲値の `Utf8Format` 適合、一般の数値Contract、単一要素添字と位置APIの整数型拡張、API横断のAbortコード統一と診断の改善。

- [範囲・添字・スライス](../../docs/spec/04-arrays-indexing-and-slices.md)
- [整数型とScalar read](../../docs/spec/03-types-and-values.md)
- [ジェネリックと制約](../../docs/spec/08-generics-constraints-and-contracts.md)
- [オーバーロードと推論](../../docs/spec/10-overload-resolution-and-inference.md)、[式の型推論](../../docs/spec/12-expressions.md)、[明示変換](../../docs/spec/13-operators-and-assignment.md)
- [反復と取得規則](../../docs/spec/14-control-flow.md)、[Kimiの反復Contract](../../docs/spec/22-core-execution-and-foreign-functions.md)
- [コード生成の実装プロファイル](../../docs/impl/21-layout-runtime-and-code-generation.md)、[検証済みの対応範囲](../../docs/STATUS.md)
- [公開宣言](../../docs/LIBRARY.md)、[コーディング規約](../../docs/STYLE.md)
