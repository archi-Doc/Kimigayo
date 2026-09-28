# @follow・@copy・記法整理と実装仕様の分離

日付: 2026-09-26
状態: 取り込み済み・実装済み。正式仕様は `SPEC.md` と参照先、実装仕様は `IMPLEMENTATION.md` と `impl/` が正とする。

2026-09-26 に決定した変更 2 点（`@follow`、形成型 `for`）と改善提案 7 点を、正式仕様とコンパイラーに反映した記録である。

## 1. 変更一覧

| # | 変更 | 旧 | 新 | 仕様 |
| --- | --- | --- | --- | --- |
| 1 | 参照先 Place の選択 | `r@deref` | `r@follow` | §13.1、§13.5.5.1 |
| 2 | 関連型の Origin 仮引数の定義域 | `associate LentItem(step)` と字下げした `wellformed uniq/Self during step` | `associate LentItem(step) for uniq/Self during step` | §8.4.3.1、§22.1.2 |
| 3 | 明示的な Copy | なし（`x@owner` が同型取得として Copy） | `x@copy`。裸の `@owner`・`@obj`・`@rc`・`@arc` は演算ではない | §3.5、§13.5.3 |
| 4 | 単一スロットの Origin 束縛 | `Slice<T>{v}` と `origin v.source == a` | `Slice<T> during a` | §3.3.6、§15.3.1 |
| 5 | Place 結果の記法 | `place(ref, T) during self` | `place ref/T during self` | §7.1.1 |
| 6 | 用語 | 安全な参照の dereference | follow（生ポインターの `*p` は dereference のまま） | 全章 |
| 7 | outlives の表記 | `a : b` | `a outlives b` | §3.8、§8.1.2 |
| 8 | Cursor | `Cursor`・`UniqCursor`、`Kimi.Iteration.shared`/`uniq` | Appendix D へ延期 | §22.1.2、付録 D |
| 9 | 実装仕様 | 第 20・21 章、付録 A・B、テスト実行プロファイルが言語仕様内 | `IMPLEMENTATION.md` と `impl/` に分離（番号は維持） | SPEC.md |

## 2. 各変更の要点

### 2.1. `@follow`

`r@follow` は `ref`/`uniq` の値が指す Place、または Sealed が証明された object handle の完全な payload を選ぶ。参照にも参照先にも Copy・Move を行わない、レベル 1 の後置演算である。`ref` と対になる逆演算ではなく「参照をたどる」操作なので、`deref` から改名した。

```kimi
var number: i32 = 1
let r = number@uniq
r@follow = 2               // number を更新する。
let child = r@follow@uniq  // r を通した number の排他 Reborrow。
```

### 2.2. 形成型 `for`

Origin 仮引数を持ち、固定の型を持たない関連型の宣言は、ヘッダーの末尾に `for 型` を書き、その型の Origin 形成条件を仮引数の定義域として公開する。能力要件は `for` の前に書く。

```kimi
contract LendingIterator
    associate LentItem(step) for uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.LentItem(step)>

contract Iterable
    associate IteratorType(source) is LendingIterator for ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)
```

### 2.3. `@copy` と裸の所有 shorthand の廃止

`E@copy` は、Copy が証明された値を Copy する。Place はそのまま使え、一時値はそのまま使う。Non-Copy や Copy 未証明の値は `NonCopyOperand_Kd` になる。ByValue の Subject には `E@copy` または `E@move` を書く。

```kimi
match count@copy        // ByValue。count は変わらない。
    var n => n += 1
for v in fixed@copy     // IntoIterable の入口を選ぶ。
    total += v
```

裸の `@owner`・`@obj`・`@rc`・`@arc` は Core を名指さないため演算ではなく、`BareOwningShorthand_Kd` になる。`@owner/T` や `@obj/Base` のような完全な形と、所有 Semantics に束縛された総称の `@s` は従来どおり同型取得・upcast である。

### 2.4. 単一スロット束縛

スキーマのスロットがちょうど 1 つの名前付き型は、後置 `during` でそのスロットを束縛できる。借用層は追加しない。スロットが 0 個・複数・未知の型、binding set との併用は拒否する。

```kimi
func tail<T>(values: Slice<T> during source) -> Slice<T> during source
    return values[1..]
```

### 2.5. Place 結果

`place ref/T` と `place uniq/T` は格納型 `T` の既存 Storage を公開する。`place` の後の綴りは、その Place に `@ref`/`@uniq` を適用した結果の型と一致する。

```kimi
func first<T>(values: ref/Array<T>) -> place ref/T during values
    return values[0]
```

### 2.6. 確定した解釈

1. 総称推論で宣言 `ref/T` は、借用値の最外層の直接の参照先に `T` を束縛する（§10.2.1）。
2. 格納された `ref`/`uniq` を Reborrow する Subject の全体束縛は、その参照先を `ref/E`/`uniq/E` として束縛する（§15.1.6）。
3. 固定の `objref/U` 期待位置では、読み取り可能な `obj`/`rc`/`arc` の handle を参照カウントを変えずに共有 object 借用する（§10.2）。

### 2.7. 実装仕様の分離

言語仕様は第 1–19 章と第 22 章、Documentation Markdown と UTF-8 formatting の各プロファイル、付録 C–F とする。第 20・21 章、テスト実行プロファイル、付録 A・B は `IMPLEMENTATION.md` が索引する `impl/` に移した。節番号は変えないため、コード中の `SPEC 20.x` などの参照はそのまま有効である。

## 3. 実装への反映

| 単位 | 内容 | コミット |
| --- | --- | --- |
| 仕様 | SPEC 本体・付録・リンクの更新、実装仕様の分離 | `b71eba3b` |
| `@follow` | パーサー、`ConversionBinding.Follow`/`PayloadFollow`、診断文、テストと Program 14・16・33 の改綴 | `d0acea79` |
| `@copy` | 束縛（Identity 取得）、`NonCopyOperand_Kd`・`BareOwningShorthand_Kd`、`@move`/`@copy` した構造体一時値のメンバー選択 | `95cb6f4f` |
| 単一スロット束縛 | パーサーの付着規則、Binding でのスロット束縛、書き戻し、ライブラリ宣言（Slice、Text、FixedBuffer、Utf8Slice）への適用 | `80f74a75` |

形成型 `for`、`place ref/T` の Place 結果、Origin 仮引数付き関連型は、旧記法と同様にまだ構文解析されない（Program 27/28 の範囲。STATUS.md に境界を記録）。Cursor はライブラリにもともと宣言がなく、延期による実装変更はない。
