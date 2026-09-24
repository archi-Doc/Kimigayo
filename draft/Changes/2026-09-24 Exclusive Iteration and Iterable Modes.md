# 排他反復と三つの Iterable モード

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

対象は次の二つである。

- **提案 2**: `for item in values@uniq` と `match x@uniq` を、排他取得として定義する。
- **提案 4**: 共有・排他・消費の三つの取得モードごとに Iterable の Contract を置き、ユーザー型も三モードで反復できるようにする。

## 1. Current Specification — 現在の仕様

**主語規則（§15.1.6、§14.6.2）.** `match` の主語と `for` の反復元は、次のとおり取得する。

| 書き方 | 取得 |
| --- | --- |
| 所有 place（裸）、`x@ref`、借用値 | 共有（借用値は共有再借用） |
| 一時値、`x@move` | 値として取得（消費） |
| `x@uniq`、`x@objuniq`、`x@uniq/T` | エラー（`ExclusiveSubject_Kd`、2026-09-24 に導入） |

**反復.** `for` は認識された `Kimi.Iterable` を使う。要件は `iterate(self: owner/Self)` の消費形だけである。共有反復は Contract を通さず、§14.6.2 の固定表で決まる（`ref/Array<T>` は `values[..]` の Slice 反復、`ref/Dictionary` は Kimi の共有 pair iterator、`ref/Slice` と `ref/ResolvedRange` は Copy 読み）。

**制限.**

- 要素を書き換える反復はない。書き換えは `for i in values.indices` と添字で行う（§4.5）。排他要素の Slice は導入されていない（付録 D）。
- ユーザー型は共有反復を宣言できない。Slice などのビューを返すメンバーを経由するしかない（付録 D.1）。
- `uniq/T` への書き込みは、フィールド経路（`p.value = n`）か `Kimi.Intrinsics.replace` に限られる。`n: uniq/i32` に対する `n += 1` や `n = 7` は型不一致になる。
- `Iterator.next` は非 lending である（§14.6.2）。関連型は Core に限られ、例外は `Element` だけである（§8.4.3）。Contract 所有の抽象 Origin は定義されていない（§15.9）。

## 2. Proposed Specification — 新しい仕様

### 2.1. 取得モード

主語規則を三つのモードに整理し、`match` と `for` で共有する。書き方ごとの取得は、引数位置の貸与の規則（§15.1.5）と同じ対応にする。

| 書き方 | モード | 隠れ local | `match` の束縛 | `for` が使う Contract |
| --- | --- | --- | --- | --- |
| 裸の place、`x@ref`、借用値 | 共有 | `ref/X` | Copy 型は Copy、その他は `ref/T` | `SharedIterable` |
| `x@uniq`、`x@uniq/T`、`x@objuniq` | 排他 | `uniq/X` | `uniq/T`（Copy 型も含む） | `ExclusiveIterable` |
| `x@move`、一時値 | 消費 | `X` | Copy または Move | `Iterable` |

- 主語の位置には宣言されたモードがないので、裸は最も弱い共有とする。借用値も裸では共有再借用になり、排他にするには `r@uniq` と書く。
- 排他モードには、§15.1.5 の排他取得の条件（貸与点が排他的に書き込み可能であること）がそのまま適用される。`let` の collection は `@uniq` で取得できない。
- オブジェクト借用（`@objuniq`）は payload に同じ規則を適用する。
- 共有モードで `SharedIterable` を持たない Copy 型は、Copy して `Iterable` で反復する。これは §15.1.6 の「Copy 主語は Copy してよい」と同じ原理であり、Slice と ResolvedRange はこの規則で反復される。

### 2.2. 排他反復（提案 2）

`for item in values@uniq` は `values` を反復の間だけ排他的に貸与する。要素は `uniq/T during source` として渡る。

- **非 lending を維持する.** 要素は iterator ではなく `source` を借用する。要素どうしは互いに素なので、束縛を集めて保持してもよい。安全なコードで重なる `uniq` を作る手段はないので、Unsafe の義務は増えない。Kimi の iterator はこの互いに素であることを保証する。
- **Loan.** 反復元の Loan は、iterator と、取り出した要素が生きている間だけ続く。ループ内での `values.append(...)` などの競合する操作は拒否され、長さは変わらない。
- **要素型.**

  | 反復元 | 要素 |
  | --- | --- |
  | `[N of T]`、`Array<T>` | `uniq/T during source` |
  | `Dictionary<K, V>` | `(ref/K during source, uniq/V during source)`。キーは不変 |
  | `Slice<T>`、`ResolvedRange` | なし（Slice は共有ビュー。排他の部分範囲は付録 D に残す） |

**参照を通した書き込み（§3.3 と §13.7 の対称化）.** 代入・複合代入・インクリメントの書き込み先の型が `uniq/T` で、右辺が `uniq/T` には適合せず `T` に適合するときは、参照先に書き込む。これは `Kimi.Intrinsics.replace(target, with: value)` と同じ評価順序と破棄を持つ。型が完全に一致する場合は、従来どおり参照そのものを置き換える。現行ではエラーになる書き方だけが意味を持つので、既存プログラムの意味は変わらない。

```kimi
var values: Array<i32> = [1, 2, 3]
for v in values@uniq
    v += 1                  // 参照先 values[i] に書き込む
for task in tasks@uniq
    task.retry()            // 受信者は uniq/Task の再借用
```

**排他 match.** `match x@uniq` の主語は `uniq/X` を保持する。

- 本体の束縛は、各 payload 位置の `uniq/T` になる。Copy 型の payload も Copy しない（Copy では書き込みが失われ、観測上同等でないため）。
- guard は共有で読む。現行の guard 規則（Move・排他借用・書き換えの禁止）は変えない。
- 束縛が生きている間は、元の place の Case を置き換えられない。これは通常の Loan 規則で保証される。

```kimi
match state@uniq
    .Running(let count) => count += 1
    .Idle => ()
```

### 2.3. 三つの Iterable（提案 4）

`Kimi` に次の二つの Contract を追加する。既存の `Iterable` は消費モードとして残す。

```kimi
contract SharedIterable
    associate SharedIterator {source} is ::Kimi.Iterator
    func iterateShared(self: ref/Self) -> Self.SharedIterator{it}
        origin it.source == self

contract ExclusiveIterable
    associate ExclusiveIterator {source} is ::Kimi.Iterator
    func iterateExclusive(self: uniq/Self) -> Self.ExclusiveIterator{it}
        origin it.source == self
```

- **Origin スロット付きの関連型.** Contract の `associate Name {source}` は、構造体ヘッダー（§15.3.2）と同じ形で Origin スロットを宣言する。実装側の `associate SharedIterable.SharedIterator is BagIterator<T>` は、同名のスロット（`struct BagIterator<T> {source}`）を持つ Core に束縛する。スロットは束縛時には開いたままで、各使用箇所で `{it}` と関係節によって与えられる。関連型の Core 制限は保つ。Contract 自身の Origin 引数や、一般の高階 Origin は導入しない。
- **Element.** 各 iterator の `Iterator.Element` が決める。Array の共有 iterator は `ref/T during source`、排他 iterator は `uniq/T during source` を返す。
- **メンバー名を分ける理由.** 受信者の形は関数グループごとに一つである（§7.3）。そのため三モードの要件は、同名にせず `iterate`／`iterateShared`／`iterateExclusive` と分ける。
- **Kimi 型の適合.** §14.6.2 の固定表を、次の宣言で置き換える。
  - `Array<T>` と `[N of T]`: 三つすべてに適合する。
  - `Dictionary<K, V>`: 三つすべてに適合する。
  - `Slice<T>` と `ResolvedRange`: `Iterable` に適合し、共有モードでは 2.1 の Copy 規則で反復する。
- **ユーザー型.** 内部の collection に委譲すれば、Unsafe なしで三モードを提供できる。

```kimi
struct Bag<T>
    Self is SharedIterable
    Self is ExclusiveIterable
    associate SharedIterable.SharedIterator is Kimi.SliceIterator<T>
    associate ExclusiveIterable.ExclusiveIterator is Kimi.ExclusiveSliceIterator<T>
    var items: Array<T>
    public func iterateShared(self: ref/Self) -> Kimi.SliceIterator<T>{it}
        origin it.source == self
        return self.items.iterateShared()
    public func iterateExclusive(self: uniq/Self) -> Kimi.ExclusiveSliceIterator<T>{it}
        origin it.source == self
        return self.items.iterateExclusive()
```

**`for` の定義.** `for p in E` は、2.1 のモードで `E` を隠れ local に取得し、そのモードの Contract の要件を一度呼んで iterator を得る。以降の `next`、要素の取得、後始末は現行の §14.6.2 のとおりである。適合がないときは、モードと Contract を示してエラーにし、ほかのモードの綴りを提案する。

## 3. Changes — 変更点

1. `match`／`for` の主語の `@uniq`・`@uniq/T`・`@objuniq` を、エラーから排他モードに変える（`ExclusiveSubject_Kd` は廃止する）。
2. 排他反復を追加する。要素は `uniq/T during source`（Dictionary は `(ref/K, uniq/V)`）で、非 lending のままとする。
3. 排他 `match` を追加する。束縛は `uniq/T` とし、guard は共有のままとする。
4. 書き込み先が `uniq/T` で右辺が `T` のとき、参照先に書き込む（`replace` と同じ意味）。
5. `Kimi.SharedIterable` と `Kimi.ExclusiveIterable`、Kimi の iterator 型（`SliceIterator`、`ExclusiveSliceIterator`、Dictionary の共有・排他 pair iterator）を公開する。いずれも公開の構築子は持たない。
6. Contract の関連型に Origin スロットヘッダーを許す。
7. §14.6.2 の反復表を、Kimi 型の適合宣言と共有モードの Copy 規則に置き換える。

## 4. Rationale — 変更する理由

**目的.**

- **綴りの意味を位置によらず一つにする.** `@uniq` は引数では排他を意味するのに、主語ではエラーになっている。三モードの対応表を一つにすれば、`match`・`for`・引数が同じ規則になる。
- **最も多い書き換えを直接書けるようにする.** 要素の書き換えは頻出だが、現行では添字ループになり、要素ごとに境界検査と式の再評価が入る。
- **組み込み型とユーザー型を同じ規則にする.** 現行は、共有反復が組み込み型だけの固定表で決まる。適合宣言にすれば、ライブラリーも利用者も同じ仕組みを使い、G20 のソース優先の方針とも一致する。

**副作用.**

- `ExclusiveSubject_Kd` は、導入直後に役割を終える。現行でこの書き方はエラーなので、既存プログラムの意味は変わらない。
- 排他反復の間は、collection 全体が貸与される。本体で `values.length` を読むこともできない。添字と要素を同時に使う場合は、従来の `indices` ループを使う。
- 参照を通した書き込みは型で方向を決める。`var r: uniq/i32` に `r = n`（`n: i32`）と書くと、参照を付け替えずに参照先を書き換える。完全一致を優先するので曖昧さはないが、読み手には規則の説明が要る。
- 排他 `match` の Copy payload は `uniq/T` になり、共有 `match` の Copy 束縛と型が異なる。モードの違いが型に現れるのは意図どおりである。
- Origin スロット付き関連型は、§15.9 で未定義としている「Contract 所有の抽象 Origin」の一部を定義することになる。対象はスロットヘッダーに限り、Contract 自身の Origin 引数は導入しない。

**検討した代替案.**

| 案 | 採らない理由 |
| --- | --- |
| 一つの `Iterable<r>`（`Callable<r, S>` 型） | 要件名が同じになり、受信者の形の規則（§7.3）と衝突する。`Callable<S>` の既定値 `ref` とも向きが逆になる |
| 借用型への適合（`ref/Self is Iterable`） | 適合の主語は `Self` だけであり（§8.4.4）、Origin を束縛する書き方も新たに要る |
| 排他 Slice を先に導入する | Loan と部分範囲の分割規則が大きい。collection 全体の排他反復だけで主要な用途を満たせる |
| 排他反復の要素を Copy で渡す | 書き込みが失われるので、排他にする意味がない |

**改善案.**

- **性能.**
  - 排他・共有反復は、Loan によって長さが固定される。そのため base と長さをループの外に出し、要素ごとの境界検査をなくせる。添字ループより命令数も境界検査も減る。
  - Kimi の iterator identity を認識してポインター走査に直接下ろすのは、現行の共有反復と同じである。これにより O0 でも `next` の呼び出しが残らない。
  - Contract の選択は静的（monomorphization）であり、実行時の費用はない。モードごとの適合判定は Conformance の検証結果を再利用し、Binding の hot path で割り当てを増やさない。
- **一貫性.**
  - 「参照を通した読み取り」と「参照を通した書き込み」が対になる。書き込みは `replace` と同じ意味なので、§15.7 の規則をそのまま使える。
  - 主語規則と貸与の規則が、同じ三行の表で説明できる。
- **概念の整理.**
  - `for` は「主語規則による取得＋モードの Contract の要件を一回呼ぶ」だけで定義できる。固定表、Copy 読みの特例、共有 pair iterator の特例がなくなる。
  - 束縛の取得は「主語のアクセスと観測上同等で、最も弱い形」という一つの原理で説明できる。共有なら Copy か `ref`、排他なら `uniq`、消費なら値になる。

## 5. Impact on SPEC.md — SPEC.md への影響

**修正が必要な箇所.**

| 箇所 | 変更 |
| --- | --- |
| §3.3 | 「参照を通した読み取り」の隣に「参照を通した書き込み」を置く |
| §4.5、§4.6.7、§4.7 | Array・固定長配列・Dictionary の三モード適合と要素型。Slice は共有ビューのままとする |
| §8.4.3 | 関連型の Origin スロットヘッダー、束縛、射影 |
| §13.7.1–13.7.2 | `uniq/T` への書き込み（`replace` と同じ評価順序と破棄） |
| §14.6.2 | モード別の Contract 選択。固定表を削除し、共有モードの Copy 規則を置く |
| §14.8、§15.1.6 | 排他主語、`uniq/T` 束縛、guard は共有のまま |
| §15.9 | 抽象 Origin の境界を「関連型のスロットヘッダーを除く」に改める |
| §22.1 | `SharedIterable`、`ExclusiveIterable` と Kimi の iterator 型 |
| 付録 A | 排他反復の Loan、互いに素な要素、書き込み、排他 match の検証項目 |
| 付録 B.6 | AccessMode に Exclusive を加える |
| 付録 D | 「排他主語と排他反復」の行を削除し、排他 Slice の行を残す |
| 付録 E、F | 用語（取得モード）と構文（`associate Name {slots}`） |
| `SPEC.md` の要約 | 主語規則の行を三モードに改める |
| Milestone 28 | ユーザー型の共有・排他反復を加える（P28 の範囲） |

**実装上の問題点.**

- **Binding.**
  - 主語のモード判定は、今回の実装（`RejectExclusiveSubject`）を差し替えれば済む。
  - Origin スロット付き関連型は、parser、関連型の束縛・射影・正規化に及ぶ新機能であり、最も費用が大きい。
  - 参照を通した書き込みは、代入の型適合に一段加える。
- **所有権解析.**
  - 反復の間は collection 全体の排他 Loan を一つ持ち、要素はその Loan に依存する値として扱う。要素ごとの `uniq` Loan を作ると、保持した要素どうしを誤って競合と判定する。
  - 書き込みは、現行のフィールド書き込み（`WriteBorrowedField`）をスカラーの参照先に広げる。
- **排他 match.** パターン計画に Exclusive のアクセスモードを加える。束縛の下ろし方は、共有の住所計算を再利用できる。
- **下ろし.** 排他要素は住所計算だけで済む。長さの固定を前提に、ループ外へ出す最適化を検証する。
- **段階.** 次の順で進めると、各段階が独立して検証できる。
  1. 書き込み
  2. 組み込み型の排他反復
  3. 排他 match
  4. Contract と Origin スロット付き関連型（P28）

## 6. Decision — 最終決定

1. **採用.** 取得モード三つの対応表（2.1）。
2. **採用.** 排他反復（2.2）と参照を通した書き込み。
3. **採用.** 排他 match。ただし実装は反復の後とする。
4. **採用.** `SharedIterable`／`ExclusiveIterable` と、Origin スロット付き関連型（2.3）。実装は P28 で行う。
5. **保留.** 排他 Slice と排他の部分範囲。付録 D に残す。
6. **決定事項.** 要件名は `iterate`／`iterateShared`／`iterateExclusive` とする。`ExclusiveSubject_Kd` は、2 の実装時に廃止する。
