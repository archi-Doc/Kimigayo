# 排他反復と三つの Iterable モード

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。定めない事項は現行仕様に従う。本書は変更後の言語仕様を定義するものであり、実装の完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

対象は次の二つである。

- **提案 2**: `for item in values@uniq` と `match x@uniq` を、排他取得として定義する。
- **提案 4**: 共有・排他・消費の各モードに Iterable の Contract を一つずつ置き、ユーザー型も三つのモードで反復できるようにする。

## 1. Current Specification — 現在の仕様

### 1.1. 主語規則（§15.1.6、§14.6.2）

| 書き方 | 取得 |
| --- | --- |
| 裸の所有 place、`x@ref`、借用値 | 共有（借用値は共有再借用） |
| `x@move`、一時値 | 値として取得する |
| `x@uniq`、`x@uniq/T`、`x@objuniq` | エラー（`ExclusiveSubject_Kd`） |

### 1.2. 反復

- Contract は消費形の `Iterable`（`iterate(self: owner/Self)`）だけである。
- 共有反復は §14.6.2 の固定表で決まる。
  - `ref/Array` と `ref/[N of T]` は、`values[..]` の Slice 反復になる。
  - `ref/Dictionary` は、Kimi の共有 pair iterator を使う。
  - `ref/Slice` と `ref/ResolvedRange` は、Copy で読んで反復する。
- `Iterator.next` は非 lending である。関連型は Core に限られ、例外は `Element` だけである（§8.4.3）。Contract が所有する抽象 Origin は定義されていない（§15.9）。

### 1.3. 制限

- 要素を書き換える反復がない。書き換えは `for i in values.indices` と添字で行う。排他要素の Slice は導入されていない（付録 D）。
- ユーザー型は共有反復を宣言できず、ビューを返すメンバーを経由する（付録 D.1）。
- `uniq/T` への書き込みは、フィールド経路（`p.value = n`）か `Kimi.Intrinsics.replace` に限られる。`n: uniq/i32` に対する `n += 1` は型エラーになる。

## 2. Proposed Specification — 新しい仕様

### 2.1. 取得モード

`match` と `for` は同じ主語規則を使う。書き方とモードの対応は、引数位置の貸与の規則（§15.1.5）と同じにする。

| 書き方 | モード | 隠れ local | `for` が使う Contract |
| --- | --- | --- | --- |
| 裸の place、`x@ref`、借用値 | 共有 | `ref/X` | `SharedIterable` |
| `x@uniq`、`x@uniq/T`（payload 投影） | 排他 | `uniq/X` | `ExclusiveIterable` |
| `x@move`、一時値 | 消費 | `X` | `Iterable` |

- 主語の位置には宣言されたモードがない。そのため裸は最も弱い共有とし、借用値も裸では共有再借用する。排他にするには `r@uniq` と書く。
- 排他モードには、§15.1.5 の排他取得の条件がそのまま適用される。`let` の collection や `ref` の借用値は、`@uniq` で取得できない。
- オブジェクト借用 `x@objuniq` の主語は、引き続き `ExclusiveSubject_Kd` とする。オブジェクト payload を受け手にする規則（§7.3）が Sealed 型に限られるためである。payload を排他で使うときは `x@uniq/T` と書く。

### 2.2. 束縛の取得

取得は二段で決まる。

1. 主語は、2.1 のモードで隠れ local に取得する。
2. 束縛は次のとおり取得する。
   - **`match` のパターン束縛.** 主語のアクセスと観測上同等で、最も弱い形で取得する。
     - 共有: Copy 型は Copy、それ以外は `ref/T`
     - 排他: その位置に格納された完全型 S に対して `uniq/S`（Copy 型も含む。Copy では書き込みが失われ、同等でないため）
     - 消費: Copy または Move
   - **`for` の要素.** iterator の `Element` が決める。要素を分解するときのモードは、要素自身の型で決まる。`uniq/T` は排他、`ref/T` は共有、所有値は消費とし、それぞれ上の `match` の規則で分解する。モードはループの主語で選ばれているので、要素が借用値でも共有に弱めない。

```kimi
for (name, count) in pairs@uniq   // 要素 uniq/(string, i32)
    count += 1                    // count: uniq/i32、name: uniq/string
```

### 2.3. 参照を通した書き込み

「参照を通した読み取り」（§3.3）と対になる規則を加える。

- **対象.** 代入・複合代入・インクリメント・デクリメントの書き込み先の型が `uniq/T` で、右辺が `uniq/T` には適合せず `T` に適合するとき、参照先に書き込む。
- **型が一致する場合.** 右辺の型が `uniq/T` なら、従来どおり参照そのものを置き換える。
- **評価順序.** §13.7.1 のとおり、右辺を確保し、参照から書き込み先を特定し、旧値を破棄して配置する。`replace` と共通なのは旧値の破棄と配置だけで、評価順序は代入のものを使う。
- **権限.** 参照の `uniq` 能力で判定する。書き込むのは参照先で、束縛（参照の slot）は変わらないので、`let` の束縛にも書き込める。
- **既存プログラム.** 現行ではエラーになる書き方だけが意味を持つので、既存プログラムの意味は変わらない。

```kimi
func bump(n: uniq/i32) => n += 1   // 参照先を +1 する

var r: uniq/Task = first@uniq
r = second@uniq                    // 型が一致する: 参照を付け替える
r = Task.init(3)                   // T に適合する: 参照先を置き換え、旧値を破棄する
```

### 2.4. 排他反復

`for item in values@uniq` は、反復の間 `values` を排他的に貸与する。

- **Loan.** collection 全体の排他 Loan を一つ持ち、要素はその Loan に依存する値とする。要素ごとには Loan を作らない。
- **非 lending.** 要素は iterator ではなく元の collection を借用するので、要素を集めて保持してもよい。Kimi の iterator は要素どうしが重ならないことを保証する。安全なコードで重なる `uniq` を作る手段はないので、Unsafe の義務は増えない。
- **制約.** ループ中は、`values.append(...)` などの競合する操作も、`values.length` の読み取りもできない。添字が必要なときは `indices` ループを使う。

```kimi
var values: Array<i32> = [1, 2, 3]
for v in values@uniq
    v += 1                   // v: uniq/i32。values[i] を書き換える
for task in tasks@uniq
    task.retry()             // 受信者は uniq/Task の再借用
```

### 2.5. 排他 match

`match x@uniq` の主語は `uniq/X` を保持する。

- 本体の束縛は 2.2 の排他の規則に従う。主語全体の束縛（`let v`）は `uniq/X` を Move する。
- 格納された参照（`ref/T` の payload など）は `uniq/ref/T` として束縛され、slot への排他アクセスになる。残る参照層を直接構造照合しない規則（付録 A.10）は変えない。
- guard は共有で読む。現行の guard 規則は変えない。
- 束縛が生きている間は、通常の Loan 規則によって元の place の Case を置き換えられない。

```kimi
match state@uniq
    .Running(let count) => count += 1   // count: uniq/i32
    .Idle => ()
```

### 2.6. 三つの Iterable Contract

`Kimi` に二つの Contract を加える。既存の `Iterable` は消費モードとして残す。

```kimi
contract SharedIterable
    associate SharedIterator {borrow} is ::Kimi.Iterator
    func iterateShared(self: ref/Self) -> Self.SharedIterator{it}
        origin it.borrow == self

contract ExclusiveIterable
    associate ExclusiveIterator {borrow} is ::Kimi.Iterator
    func iterateExclusive(self: uniq/Self) -> Self.ExclusiveIterator{it}
        origin it.borrow == self
```

#### 2.6.1. Origin スロット付き関連型

- **宣言.** Contract の `associate Name {borrow}` は、構造体ヘッダー（§15.3.2）と同じ形で Origin スロットを宣言する。Contract 自身の Origin 引数や、一般の高階 Origin は導入しない。
- **使用.** ヘッダーでスキーマが既知になるので、射影に集合名を付けられる（`Self.SharedIterator{it}`、`C.SharedIterable.SharedIterator{it}`）。
- **束縛.** 既存の関係節で書く。関係節の中では、ヘッダーのスロットを `関連型名.スロット名`（例: `SharedIterator.borrow`）で参照する。型自身のスロットとは名前が衝突しない。
- **使わないスロット.** ヘッダーのスロットを使わない束縛も許す。受け手の借用より長く生きる iterator（Slice）がこれにあたる。

```kimi
// Array: 要素は受け手の借用に依存する
associate SharedIterable.SharedIterator is SliceIterator<T>{s}
    origin s.source == SharedIterator.borrow

// Slice: 要素は元の配列に依存し、Slice の local より長く生きる
associate SharedIterable.SharedIterator is SliceIterator<T>{s}
    origin s.source == source
```

#### 2.6.2. 要件名

受信者の形は関数グループごとに一つである（§7.3）。そのため要件名は、`iterate`・`iterateShared`・`iterateExclusive` と分ける。いずれも公開メソッドとして直接呼べる。

#### 2.6.3. Kimi 型の適合と要素

§14.6.2 の固定表と Copy 読みの特例を、次の適合宣言に置き換える。

| 型 | `Iterable` | `SharedIterable` | `ExclusiveIterable` |
| --- | --- | --- | --- |
| `[N of T]`、`Array<T>` | `T` | `ref/T` | `uniq/T` |
| `Dictionary<K, V>` | `(K, V)` | `(ref/K, ref/V)` | `(ref/K, uniq/V)`（キーは不変） |
| `Slice<T>` | `ref/T` | `ref/T`（元の配列に依存） | なし |
| `ResolvedRange` | `isize` | `isize` | なし |

- 表の参照は、共有・排他ともに元の collection の Origin に依存する。
- Kimi の iterator 型（`SliceIterator`、`ExclusiveSliceIterator`、Dictionary の共有・排他 pair iterator）を公開する。公開の構築子は持たない。
- 排他の部分範囲（排他 Slice）は導入しない。

#### 2.6.4. ユーザー型

ユーザー型は、内部の collection に委譲すれば Unsafe なしで三つのモードを提供できる。Copy のビュー型でも、裸で回すには `SharedIterable` の宣言が必要である。実装は `iterate` への委譲で足りる。

```kimi
struct Bag<T>
    Self is SharedIterable
    Self is ExclusiveIterable
    associate SharedIterable.SharedIterator is Kimi.SliceIterator<T>{s}
        origin s.source == SharedIterator.borrow
    associate ExclusiveIterable.ExclusiveIterator is Kimi.ExclusiveSliceIterator<T>{e}
        origin e.source == ExclusiveIterator.borrow
    var items: Array<T>
    public func iterateShared(self: ref/Self) -> Kimi.SliceIterator<T>{it}
        origin it.source == self
        return self.items.iterateShared()
    public func iterateExclusive(self: uniq/Self) -> Kimi.ExclusiveSliceIterator<T>{it}
        origin it.source == self
        return self.items.iterateExclusive()

func count<C>(items: ref/C) -> i32
    C is SharedIterable
    var n: i32 = 0
    for _ in items           // 借用値なので共有モード
        n += 1
    return n
```

### 2.7. `for` の定義

`for p in E` は次の手順で評価する。

1. 2.1 のモードで `E` を隠れ local に取得する。
2. そのモードの Contract の要件を一度呼んで、iterator を得る。
3. `next`、要素の取得、後始末は現行の §14.6.2 のとおりである。

共有モードの経路は `SharedIterable` の一つだけで、Copy 型の特例はない。

### 2.8. 診断

| 状況 | 診断 |
| --- | --- |
| `x@objuniq` を主語に書いた | `ExclusiveSubject_Kd`（対象を `@objuniq` に限る） |
| 型がそのモードの Contract に適合しない | `NotIterable_Kd`。モードと Contract を示し、適合するほかの綴りを提案する |
| `ref/T` の要素に書き込もうとした | 既存の型エラーに、`@uniq` で反復する提案を注記する |

## 3. Changes — 変更点

1. 主語の `@uniq` と `@uniq/T` を、エラーから排他モードに変える。`@objuniq` はエラーのまま残す。
2. 束縛の取得を二段で定める。排他の束縛は `uniq/S` とし、反復要素の分解は要素自身の型が示すモードで行う。
3. `uniq/T` への参照を通した書き込みを加える（評価順序は §13.7.1）。
4. 排他反復を加える。Loan は collection 全体に一つとし、非 lending を保つ。
5. 排他 `match` を加える。
6. `SharedIterable` と `ExclusiveIterable`、Kimi の iterator 型、Origin スロット付き関連型を加える。
7. §14.6.2 の固定表と Copy 読みの特例を、Kimi 型の適合宣言に置き換える。
8. 診断 `NotIterable_Kd` を加え、`ExclusiveSubject_Kd` の対象を `@objuniq` に絞る。

## 4. Rationale — 変更する理由

### 4.1. 目的

- **綴りの意味を位置によらず一つにする.** `@uniq` は引数では排他を意味するのに、主語ではエラーになっている。
- **最も多い書き換えを直接書けるようにする.** 現行では要素の書き換えが添字ループになる。
- **組み込み型とユーザー型を同じ規則で扱う.** 固定表をやめ、適合宣言で扱う。これは G20 のソース優先の方針とも一致する。

### 4.2. 副作用

- **既存プログラムへの影響はない.** 変更した書き方（`@uniq` の主語、`uniq/T` への `T` の代入）は、いずれも現行ではエラーである。`ExclusiveSubject_Kd` は導入直後に対象を狭めることになる。
- **追加の記述が要る場面.**
  - P28 の `View<T>` のような Copy のビュー型は、裸で反復するのに `SharedIterable` の宣言が要る（2.6.4）。
  - 排他反復の間は collection 全体を読めない（2.4）。
- **読み手に説明が要る点.** 付け替えと置き換えが同じ `=` で書かれる（2.3）。完全一致を優先するので曖昧さはない。
- **§15.9 の境界が動く.** Contract が所有する抽象 Origin のうち、関連型のスロットヘッダーだけを定義することになる。

### 4.3. 検討した代替案

| 案 | 採らない理由 |
| --- | --- |
| 一つの `Iterable<r>`（`Callable<r, S>` と同じ形） | 要件名が同じになり、§7.3 と衝突する。`Callable<S>` の既定値 `ref` とも向きが逆になる |
| 借用型への適合（`ref/Self is Iterable`） | 適合の主語は `Self` だけである（§8.4.4） |
| 共有モードで Copy 型を Copy して反復する | 経路が二つになり、総称本体で選択が決まらないことがある |
| 同名スロットの Core にだけ束縛する | スロット名の違う型や、Slice の寿命を表せない |
| 排他 Slice を先に入れる | Loan と部分範囲の分割規則が大きい。collection 全体の排他反復で主な用途は満たせる |
| 書き込みを Copy 型に限る | 排他反復で要素を置き換えられなくなる |

### 4.4. 改善の見込み

- **性能（未測定）.** 排他・共有反復では Loan によって長さが固定される。base と長さをループの外に出せるので、添字ループより境界検査と命令が減る見込みである。Contract の選択は静的（monomorphization）であり、実行時の費用はない。段階 2 で測定して確認する（第 5 章）。
- **一貫性.** 読み取りと書き込みが、参照を通して対になる。主語規則と貸与の規則が、同じ三行の表で説明できる。
- **概念の整理.** `for` は「主語規則による取得＋そのモードの要件を一回呼ぶ」だけで定義できる。固定表と Copy 読みの特例がなくなる。

## 5. Impact on SPEC.md — SPEC.md への影響

### 5.1. 修正箇所

- **本文**
  - §3.3、§13.7.1–13.7.2: 参照を通した書き込み。書き込み先の定義に「参照先を書く」の一行を加える
  - §4.5、§4.6.7、§4.7: Kimi 型の三モード適合と要素（2.6.3 の表）
  - §7.3: 変更なし（要件名を分ける根拠として参照する）
  - §8.4.3、§15.3: Origin スロット付き関連型、射影の集合名、`関連型名.スロット名`
  - §14.6.2: モード別の Contract 選択。固定表と Copy 読みの特例を削除する
  - §14.8、§15.1.6: 排他主語、束縛の二段規則、`@objuniq` の扱い
  - §15.9: 抽象 Origin の境界から、関連型のスロットヘッダーを除く
  - §22.1: `SharedIterable`、`ExclusiveIterable`、Kimi の iterator 型
- **付録**
  - A: 排他反復の Loan、要素の互いに素、タプル要素の排他分解、参照を通した書き込み（付け替えと置き換えの両例）、排他 match、`NotIterable_Kd`
  - B.6: AccessMode に Exclusive を加える
  - D: 「排他主語と排他反復」の行を「`@objuniq` 主語」に改める。排他 Slice の行を残し、「添字付き排他反復（`(isize, uniq/T)` を返す iterator）」を候補として加える
  - E、F: 用語（取得モード）と構文（`associate Name {slots}`）
- **その他**
  - `SPEC.md`: 主語規則の要約を三つのモードに改める
  - Milestone 28: ユーザー型の共有・排他反復を加える

### 5.2. 実装上の問題点

- **Binding**
  - 主語のモード判定は、現行の `RejectExclusiveSubject` を差し替える。
  - Origin スロット付き関連型は、parser、束縛、射影、正規化に及ぶ。最も費用が大きい。
  - 参照を通した書き込みは、代入の型適合に一段加える。
- **所有権解析**
  - collection 全体の排他 Loan と、要素をその Loan に依存する値として扱う。要素ごとに `uniq` Loan を作ると、保持した要素どうしを誤って競合と判定する。
  - 参照を通した書き込みは、現行の `WriteBorrowedField` を参照先のスカラーに広げる。
- **排他 match**
  - パターン計画に Exclusive のアクセスモードを加える。束縛の住所計算は共有の実装を再利用できる。
- **下ろし**
  - Kimi の iterator identity は、現行の共有反復と同じくポインター走査に直接下ろす。

### 5.3. 段階

| 段階 | 内容 | 完了条件 |
| --- | --- | --- |
| 1 | 参照を通した書き込み | 付け替えと置き換えの両方、評価順序、`let` 束縛への書き込み |
| 2 | 組み込み型の排他反復 | 保持した要素の Loan。添字ループとの比較測定（O0/O2 の命令数と境界検査数、ウォーム時の Binding 割り当てゼロ） |
| 3 | 排他 `match` | 束縛の型、guard、Case 置き換えの拒否 |
| 4 | Contract と Origin スロット付き関連型 | Kimi 型の適合への移行、ユーザー型と総称関数、P28 |

## 6. Decision — 最終決定

- **採用**
  - 取得モードと束縛の二段規則（2.1、2.2）
  - 参照を通した書き込み（2.3）
  - 排他反復（2.4）
  - 排他 `match`（2.5）。実装は段階 3 とする
  - 三つの Iterable Contract と Origin スロット付き関連型（2.6）。実装は P28 とする
- **保留（付録 D）**
  - `@objuniq` 主語
  - 排他 Slice
  - 添字付き排他反復
- **決定事項**
  - 要件名は `iterate`・`iterateShared`・`iterateExclusive` とする。
  - Contract のスロット名は `borrow` とする。
  - 新しい診断は `NotIterable_Kd` とする。
