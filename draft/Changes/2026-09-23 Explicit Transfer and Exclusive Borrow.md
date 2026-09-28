# 明示的な転送と排他借用

## 1. 位置付け

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

目的は、呼び出し側の式の綴りだけで「転送されるのか、排他的に借用されるのか、共有借用や Copy で済むのか」を判別できるようにすることである。

### 1.1. 用語

- **転送**: Move。値と破棄責任を移し、元の place を Moved にする操作。Consume は §15.1.4 の合法性判定、Consuming は §7.6.3 の呼び出し受信者要件を指す既存用語としてだけ用いる。
- **値の種類**（式の外側 Semantics）: **所有値**は `owner`、**借用値**は §3.3 の `borrow`（`ref`・`uniq`・`objref`・`objuniq`）、**handle** は `obj`・`rc`・`arc`。`unsafe` は現行どおり対象外。
- **経路**（place への到達）: **直接**は参照の解決を含まない経路（ローカル、引数、その inline 部分、static）。**排他参照経由**は lowering した経路に `uniq`／`objuniq` の解決（§15.6 の `*`）を含む経路で、`uniq` を返す getter の結果から先を含む。**共有参照経由**は `ref`／`objref` の解決を含む経路。handle の解決は、所有 `obj` なら直接、`objuniq` なら排他参照経由、`objref`・`rc`・`arc` なら共有参照経由に数える。一時値は place ではない。
- **Movable Place**: §15.1.5 のとおり。直接経路の所有 root とその inline 部分に限り、static、object field、借用越しの place は含まない。

値の種類と経路は独立している。`h: uniq/Holder` の field `link: uniq{a}/T` について、`h.link` は借用値であり、排他参照経由の place でもある。

## 2. 貸与点の規則

> 裸の式は、所有 place から新たな転送も排他貸与も開始しない。印が要るのは、所有 place が最初に排他的に貸される点（`@uniq`／`@objuniq`）と手放される点（`@move`）だけである。既存の排他参照から先の再貸与と、所有一時値の転送には印が要らない。

規則は三つである。

1. **転送**は `@move`（§3.1）で書き、値の種類にかかわらず Movable Place にだけ適用する。
2. **裸の取得**は、値が Copy なら Copy、借用値なら再借用、所有値・handle の place なら借用型を要求する位置での借用である。再借用と借用のモードは、位置の要求・値のモード・経路の権限のうち最も弱いものであり、直接経路の所有 place から排他モードを得るには印が要る。所有値を要求する位置で非 Copy ならエラー。
3. **`@uniq`／`@objuniq`** は、直接の所有 place では新しい排他借用、排他参照経由か排他借用値では排他再借用、共有参照経由ではエラー。

保証は取得操作に限る。裸の直接の所有 place の取得自体は元の place を変更・転送せず、新しい排他アクセス権を渡さない。共有借用した場合は Loan 規則が Loan の間の変更を制限するが、Copy は Loan を作らないので、`f(x, x@uniq)` は合法であり `x` は呼び出し中に変わり得る。転送操作の効果は Copy 能力に依存しないが、裸の取得と共有読み取り（SharedReadResult）は従来どおり Copy 能力で結果が変わる。

| 値の種類 | 裸 | `@uniq`／`@objuniq` | `@move` |
| --- | --- | --- | --- |
| 所有値の place | 所有位置は Copy か エラー。借用位置は共有借用、排他経路なら排他再借用 | 直接なら新しい排他借用、排他経路なら排他再借用、共有経路はエラー | Movable Place なら転送（Copy でも） |
| 借用値 | 要求モードでの再借用。`ref`・`objref` は Copy | 値と経路が排他なら排他再借用 | Movable Place なら参照そのものを転送 |
| handle の place | 所有位置はエラー。借用位置は `objref` 借用、排他経路なら `objuniq` 再借用 | 直接なら新しい `objuniq`、排他経路なら再借用 | Movable Place なら handle を転送 |
| 一時値・getter の結果 | 所有権を渡す。借用位置なら実体化して共有借用 | 明示のみ可。所有 getter 結果の storage と inline 部分は §11.2.3 により不可。返された参照の referent は通常の再借用 | 許可（効果なし） |

```kimi
inspect(data)          // 共有借用。data はそのまま。
modify(data@uniq)      // 排他借用。data は書き換わり得る。
consume(data@move)     // 転送。data は使えなくなる。
add(count)             // count が Copy なら Copy。
```

## 3. 綴り

### 3.1. 転送 `@move`

`E@move` は Movable Place `E` を転送する。Copy 型でも Copy せず Moved にする。部分 Move・構築・`deinit`・Loan の条件は現行どおり。

- **同義の綴り**: target を §3.2.3 の `?` の付け方に従って解決し grouping を透明に扱った上で、最外層に明示された Semantics が owning（`owner`・`obj`・`rc`・`arc`、総称の `@s` で `s` が `owning`）であり、被演算子の外側 Semantics と一致するか §13.5.7 の upcast 行に該当する場合、転送である。alias の展開結果は明示 Semantics に数えない。`n@owner/i32` と `n@(owner/i32)` は転送、`x@uniq/i32?` と `x@owner/T?` は Option 全体の Type 指定（§3.3）、`x@owner/(T?)` は転送。それ以外の owning 指定は現行どおりエラーとし、Copy 型を Copy する例外はない。
- 借用値の転送は参照そのものを移し、referent には触れない。
- `@` は直接の被演算子にだけ作用し、レシーバの取得へ遡らない。getter の結果や一時値への `@move` は効果を持たない。
- `@move/T`、`@move?`、`@move{...}` は不可。`move` は `@` の直後に置く組み込み名として予約し、それ以外の位置では通常の名前とする。
- `x@move@ref` は転送した一時値の共有借用であり有効である。

### 3.2. 借用

`@ref`・`@uniq`・`@objref`・`@objuniq`、完全形 `@ref/T`・`@uniq/T`、総称の `@s`（`s` が `borrow`）の意味は現行どおり。一時値の排他借用は明示のみ（`modify(makeResource()@uniq)`）。handle の slot 借用は完全形で書く（`h@ref/rc/T`、`h@uniq/obj/T`）。

### 3.3. Type だけの target

最外層に明示 Semantics のない target（同一取得、数値変換、総称の `x@T`、`?` で包まれた型）は転送しない。place に対しては Copy 型なら Copy、非 Copy の同一取得はエラーとして `@move` または `@owner/T` を求める。一時値は所有権を渡す。`T` と `owner/T` の同値は型の関係であり、操作は明示された Semantics で決まる。

### 3.4. 構文

- `@` の直後の組み込み Semantics 名または `move` に `/` が続かない場合、target はそこで完成し、後続の `.`・`(`・`[` は後置チェーンを続ける。`buffer@uniq.clear()`、`builder@move.finish()`、`counter@uniq()` は `(buffer@uniq).clear()` などと同じである。完全形（`x@uniq/Pipeline.Accumulator`）と総称の `x@s` の後で選択・呼び出し・添字を続けるには従来どおり括弧が要る。
- `try` は `@` より弱く `* / %` より強く結合する新しい段に置く。`try x@move` は `try (x@move)`。`try prepare() + 1` は従来どおり `(try prepare()) + 1`、`try f().count` も従来どおり。取り出してから変換するには `(try f())@i64` と書く。level 2 の前置演算子は `try` 式を直接取れず、`-(try x)`、`not (try x)` と書く。右結合と評価順序は変わらない。

## 4. 位置ごとの裸の意味

### 4.1. 期待型がある位置

引数、注釈付き初期化子、代入の右辺、戻り値、default 値、要素・payload の位置では、要求される型に応じて次の一つを選ぶ。他の操作へは切り替えない。`T` は所有値（referent）の型を表す。

| 要求 | 直接の所有 place | 排他経路の place・`uniq` 値 | 共有経路の place・`ref` 値 |
| --- | --- | --- | --- |
| `T` | Copy か エラー | Copy read か エラー | Copy read か エラー |
| `ref/T` | 共有借用 | 共有再借用 | 共有再借用、参照の Copy |
| `uniq/T` | エラー（`@uniq` が必要） | 排他再借用 | エラー |
| `objref/T`／`objuniq/T` | 借用／エラー（`@objuniq` が必要） | 再借用 | 共有再借用／エラー |

### 4.2. 期待型がない位置

- 無注釈の `let`／`var` の初期化子は、Copy なら Copy、借用値なら参照の Copy か同じモードの再借用、非 Copy の所有値・handle の place はエラーとする。借用を推論して束縛の型を変えることはしない。
- `_ = E` は所有位置である。Copy は Copy して破棄し、非 Copy は `_ = x@move` と書く。`_ = f()` は従来どおり。`_ = x@ref` は借用を捨てるだけで効果なし警告の対象。
- `try x` は所有された Option／Result を要求する。Copy なら Copy、非 Copy は `try x@move`。

### 4.3. 主語の取得

`match` と `for` の主語は次のとおり取得する。

| 主語 | 取得 |
| --- | --- |
| 所有値の place（Copy でも） | 共有借用 |
| 借用値 | 参照の Copy または共有再借用 |
| 所有一時値（`x@move` の結果、`makeMessage()` を含む） | 所有取得（現行どおり） |

束縛の型は、借用主語なら共有読み取り規則、所有主語なら従来の Copy／Move に従う。借用主語の Loan は束縛の実使用に従う。Subject Place は Copy の参照値を保持するだけなので、借用束縛が生存していない腕では主語の書き換えや転送ができる。

```kimi
match message              // 共有借用。text: ref/string。
    .Write(let text) => inspect(text)
    _ => ()
use(message)               // 借用が終われば使える。

match state                // 借用束縛のない腕では主語を書き換えられる。
    .Idle => state = .Running
    _ => ()

match message@move         // 所有一時値。payload を Move。
    .Write(let text) => store(text@move)
    _ => ()
```

### 4.4. 再借用

- 借用値の裸使用は、位置が要求するモードでの再借用であり、値のモードと経路の権限を超えない。期待型がなければ値と同じモードである。`let y = r` は再借用で、`y` の最終使用後に `r` が復帰する。
- 再借用の Origin は referent の place に anchor され、region は親の Origin に含まれる。親の参照を保持する変数の寿命には従わない。§15.6.3 の「親は生存したまま停止する」は親 Loan の region を指す。引数から作った再借用は、参照の Copy と同様に返却・格納できる。

```kimi
// h: uniq/Holder。item: Resource（非 Copy）、link: uniq{a}/T、view は uniq/Resource を返す getter。
let a = h                        // uniq/Holder の再借用。
// let b = h.item                // エラー：非 Copy の所有値の place。
let c: uniq/Resource = h.item    // 排他経路の place。親の権限による子 Loan。
let d = h.link                   // 格納された uniq の再借用。
// h.link@move                   // エラー：Movable Place ではない。
let e = h.view@uniq              // 返された参照の referent の再借用。

var r = other@uniq
let moved = r@move               // 直接のローカルなので参照を転送できる。

func update(self: uniq/Self, amount: i32)
    self.helper()                // 再借用。印は不要。
    self.items.add(amount)       // 排他経路の place。印は不要。

var holder = makeHolder()
holder@uniq.items.add(1)         // 貸与点は holder。
holder.items@uniq.add(1)         // 貸与点を field に絞る書き方。
```

### 4.5. 総称

- 裸の place を所有値として使うには定義時に Copy の証明（`T is Copy`）が要る。証明できなければエラーであり、by-value 候補には適用できない。保留・推論した Move・隠れた制約にはならない。
- `@move` は常に Move であり、`@s` は `s` の束縛だけで効果が決まる。`s` が `owning` を含む場合、`value@s` は Copy 型でも転送なので、`value@s` の後で `value` を使う定義は `T is Copy` があっても無効である（§8.9 の例を改める）。
- `<s/T>` に裸の非 Copy place を渡すと `s = owner` に推論した後に適用不可となる。`s = ref` を探しに行かない。

```kimi
func choose<T>(first: T, second: T, useFirst: bool) -> T
    return if useFirst => first@move else => second@move

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value)   // Copy の証明があるので裸で可。
```

## 5. 呼び出し

### 5.1. 引数と overload

暗黙適応表（§10.2）の 2 行を置き換える。

| 現行 | 変更後 |
| --- | --- |
| Exclusively writable `T` Place → `uniq/T` | 排他参照経由の `T` Place → `uniq/T`。子の排他借用／cross |
| Exclusively writable `obj/T` Place → `objuniq/T` | 排他参照経由の `obj/T` Place → `objuniq/T`。子の排他オブジェクト借用／cross |

残る行（共有借用、一時値の共有借用、再借用、Copy read）と順位（Exact、literal fitting、same-Semantics reborrow、cross-Semantics borrow）は維持する。排他参照経由の place に `ref`・`uniq` 両候補がある場合は cross どうしで曖昧となり、`@ref`／`@uniq` で選ぶ。

非 Copy または Copy 未証明の裸の place は by-value 候補に適用できない。Copy 能力は型の性質なので applicability で判定し、flow 依存の検査は従来どおり選択後に行う。overload の選択結果によって裸の place が転送されることはない。

```kimi
func foo(x: Resource)
func foo(x: ref/Resource)
foo(res)        // ref 候補だけが適用可能。
foo(res@move)   // by-value 候補が Exact。

func bar(x: i32)
func bar(x: ref/i32)
bar(n)          // Exact の by-value を選ぶ（現行どおり）。

func inspect(value: ref/i32)
func inspect(value: uniq/i32)
inspect(x)      // 共有候補だけが適用可能。§10.4 の曖昧例は解消する。
inspect(x@uniq) // 排他候補。
```

### 5.2. レシーバ

レシーバは引数を §4.1 の規則に当てはめて取得する。受信者省略形 `func read(self)` が `ref/Self` を表す規則は変えない。

| レシーバ式 | `ref/Self` | `uniq/Self` | 所有 `Self` |
| --- | --- | --- | --- |
| 直接の所有 place `x` | `x.m()` | `x@uniq.m()` | Copy なら `x.m()`、転送なら `x@move.m()` |
| 排他経路の place `p`、`uniq` 値 `r` | `p.m()`、`r.m()` | `p.m()`、`r.m()` | Copy なら Copy read、非 Copy はエラー |
| 共有経路の place、`ref` 値 | `r.m()` | エラー | 同上 |
| handle `h: obj/T` | `h.m()`（objref） | `h@objuniq.m()` | `h@move.m()` |
| 一時値 | `f().m()` | `f()@uniq.m()` | `f().m()` |

- 代入・複合代入・インクリメントでは、書き込みに必要な排他借用の印だけを省略する（`x.count = 5`、`x.count += 1`）。所有レシーバの setter は Copy なら Copy、非 Copy なら `x@move.property = value` と書き、借用レシーバは通常規則に従う。代入先へ到達する途中の getter は、それ自身のレシーバをこの表で取得する。
- `uniq/Self` の結果を返すメソッドの連鎖は、最初のレシーバだけに印を付ける（`builder@uniq.add(1).add(2)`）。
- 非束縛呼び出し `Type.method(x@uniq, 1)` も同じ規則に従う。
- Sealed payload の暗黙投影（§12.4.4）は共有側だけ残す。`obj` place から `uniq/Self` へは `(h@uniq/T).m()` か、Proven なら `h@objuniq.m()` と書く。

### 5.3. 関数値と closure

関数値の呼び出しは、§7.6.3 の受信者要件をレシーバとして §5.2 の規則で取得する。これは呼び出し時の取得規則であり、`Callable<r, S>` の受信者適合条件とは別である。`Callable` 経由の呼び出しも同じ取得規則に従う。

| 受信者要件 | 直接の所有 closure | `uniq/F` の値 | `ref/F` の値 | 一時値 |
| --- | --- | --- | --- | --- |
| Shared | `c()` | `c()` | `c()` | `f()()` |
| Exclusive | `c@uniq()` | `c()` | エラー | `f()@uniq()` |
| Consuming | Copy なら `c()`、非 Copy は `c@move()` | `F` が Copy なら Copy read、非 Copy はエラー | 同左 | `f()()` |

- 非 Copy の concrete Closure・common 関数値の転送は `register(closure@move)`、`let g = f@move` と書く。Function Item は Copy なので裸で可。
- キャプチャ一覧は引数と同じ規則に従う。`[x]` は Copy のみ、`[x@move]` は転送、`[x@ref]`・`[x@uniq]` は借用、`[var x]` は Copy、`[var x@move]` は転送。`uniq` 参照の `[r]` は排他再借用（`[r@uniq]` と同じ）、転送は `[r@move]`。一覧省略時に Copy しか推論しない規則は従来どおり。closure 本体が環境から値を出す場合も `=> item@move` と書く。

```kimi
var next = func [var count] () -> i32
    count += 1
    return count
let first = next@uniq()                                // Exclusive 呼び出し。

let holder = func [text@move] () => inspect(text)     // Shared 呼び出し。
let take = func [item@move] () => item@move           // Consuming 呼び出し。
let taken = take@move()
let pick = func [n] () => n@move                       // n: i32。Copy かつ Consuming。
let viewed = pick@ref                                  // 参照からも環境を Copy して呼べる。
let value = viewed()
```

### 5.4. オブジェクト handle

- 排他レシーバと排他引数は `@objuniq` と書く。handle の slot 借用は完全形（§3.2）。
- handle を所有値として渡すには `h@move`。生成は `makeObj(v@move)`。
- `rc`／`arc` の強参照の複製は `clone(h@ref/rc/T)` のように slot を借用して渡す。`clone` のための暗黙適応は追加しない。`obj` は複製できない。
- upcast 表の owning 行（`@obj/Base` など）は転送、`@objref/Base`・`@objuniq/Base` は借用である。

### 5.5. 予約

呼び出し予約（§15.6.7）の規則は変えず、対象を明示の `@uniq`・`@objuniq` と、借用値・排他参照経由の place の裸の排他再借用とする。貸与点から、ユーザーコードを呼ばない標準 field・要素投影を連ねて呼び出しへ至る経路は一つの準備であり、途中で Loan を有効化しない。getter や別の呼び出しを挟む場合は、その呼び出しで予約を有効化する。

```kimi
Kimi.Intrinsics.replace(p@uniq, with: p + 1)
Kimi.Intrinsics.swap(a@uniq, b@uniq)
holder@uniq.items.append(holder.items.length)   // 予約中の共有読み取り。holder.items@uniq.append(...) と同じ。
// f(x@move, x@ref)   // エラー：転送と借用が重なる。
```

## 6. 構文位置ごとの規則

- **構築**: `self.field = param@move`、`: base(name@move)`、`T.init(a@move, b@move)`、`.Some(v@move)`。構築中の `self.field@move` は禁止のまま。
- **Accessor**: setter は `storage = value@move`。所有レシーバの getter は `get(self: Self) -> Resource => self.item@move` と定義し、`holder@move.item` で呼ぶ。stored への `holder.item@move` は標準 get/set の権限下で部分 Move となる。
- **リテラル**: `(a@move, b@move)`、`[a@move, b@move]`。
- **結果位置**: 単一式本体 `=> x@move`、`return`・`exit`・`yield` の被演算子、`if`／`match`／`do` の腕の結果。
- **代入**: `y = x@move`、自己代入 `x = x@move`。
- **default 値**: 先行引数 slot とその所有内容を転送・変更できず、新たな借用を結果に保持できない（§7.2.3）。default 内の一時値とローカルには通常規則を適用する。
- **static Field**: 転送不可のまま。

```kimi
struct Holder
    public var item: Resource
        set(value: Resource) -> ()
            storage = value@move
    public init(value: Resource) => self.item = value@move

func id(x: Resource) -> Resource => x@move

var pair: (string, i32) = ("Alice", 30)
let name = pair.0@move       // 部分 Move が見える。
```

## 7. 反復

`for` の取得元は §4.3 で取得し、隠れ iterable local に置く。既存の消費 `iterate` がその local を消費して iterator を返す。protocol は変えず、認識される Iterable の表に共有行を追加する。

| 取得元 | 隠れ local | 反復 |
| --- | --- | --- |
| 所有 place の `Array<T>`、`[N of T]` | `ref/Array<T>`、`ref/[N of T]` | `values[..]` の Slice 反復に写像し、要素は `ref{source}/T` |
| 所有 place の `Dictionary<K, V>` | `ref/Dictionary<K, V>` | Kimi に追加する共有 pair iterator。要素は `(ref{source}/K, ref{source}/V)`、挿入順 |
| 所有 place の `Slice<T>`、`ResolvedRange` | `ref/Slice<T>`、`ref/ResolvedRange` | Copy read で `C` を取得し現行どおり反復する。Slice の backing Loan は保持し、local への Loan は残さない |
| `uniq`／`ref` の参照値 | 共有再借用または参照の Copy | 上と同じ |
| 所有一時値（`values@move`、`makeArray()`） | 所有 | 現行どおり転送して要素を消費 |

payload の束縛取得（`next` の結果からの Copy／Move）は一時値からなので印は要らない。`for` は Iterable を要求するので、ユーザー型の共有反復は Iterable な view（Slice など）を返すメンバーか明示の adapter で行う。共有 Iterable 要件は付録 D に置く。

```kimi
for item in items           // 共有反復。items は残る。
    inspect(item)
for item in items@move      // 転送して反復。
    store(item@move)
```

## 8. Kimi 規定宣言

- `Kimi.Console.writeLine` の引数を `text: ref/string` に変える。文字列リテラル・補間・`stringify` の結果は一時値の共有借用で渡り、`writeLine(name)` は `name` を残す。§22.5.5 の実行時規定は、引数を借用し破棄しない形に改める。
- `Intrinsics` の `replace`・`exchange`・`swap`・`makeObj`・`makeRc`・`makeArc`・`clone` の宣言は変えない。呼び出し例を §5.4〜5.5 の形に改める。

## 9. 診断

- 所有位置の裸の非 Copy place: 転送なら `x@move`、借用でよいなら `x@ref` または `x@uniq` を提示する。
- `uniq`／`objuniq` を要求する位置の裸の直接の所有 place、および Exclusive 要件の所有 closure の裸の呼び出し: `x@uniq`／`x@objuniq`／`c@uniq()` を提示する。
- overload: by-value 候補を除外した理由（非 Copy または Copy 未証明の裸の place）と、排他参照経由の place で cross 候補が競合した旨を候補一覧に示す。
- `try` の直後に `@Type` が続く場合: `(try expr)@Type` を候補として提示する。
- 総称本体で Copy 未証明の裸の所有使用: `T is Copy` の追加か `@move` を提示する。

## 10. 影響と実装

### 10.1. SPEC.md への影響

正式仕様は本書に依存せず、次を更新する。

- §2 予約名（`move`）、§3.3（値の種類と経路の用語）、§3.5〜3.6（Copy/Move、一時値の例、「明示的 Move 演算子はない」の削除）
- §4.6〜4.7（要素読み取り行、コレクション API の例、Dictionary の共有 iterator）
- §6.2.3・§6.3.2（構築）、§7.2〜7.3（引数、default、レシーバ）、§7.6.2〜7.6.3（キャプチャ、Exclusive・Consuming 呼び出し）
- §8.6（`Callable` 経由の呼び出し）、§8.9（効果の決定、`value@s` の例）、§10.2・§10.4（適応表の置き換え、曖昧例の削除）
- §11.2〜11.3（accessor、所有レシーバの setter、暗黙の排他レシーバ）、§12.4.4（Sealed payload の暗黙投影を共有側に限定）
- §13.1（`try` の段）、§13.5.1・§13.5.3・§13.5.5・§13.5.7（target 構文、転送の族と最外層判定、Type だけの target、upcast）、§13.7（代入例）、§13.8
- §14.2.4（明示的破棄）、§14.6.2（主語の取得と Iterable 表）、§15.1.3・§15.1.5・§15.1.6（Move Path、Movable Place、match）、§15.6.3（再借用の anchor と親 Loan の文言）、§15.6.7（準備経路と例）
- §17.2.4（`try` の優先順位と例）、§22.4〜22.5（`writeLine`）
- 付録 A・E・F、付録 D（共有 Iterable 要件）、documentation-markdown と testing-profile の例、milestones と examples の全プログラム

### 10.2. 実装

二段に分ける。第一段は `@uniq`／`@objuniq` の必須化、§10.2 の 2 行の置き換え、排他参照経由の再借用と準備経路、closure の Exclusive 呼び出し、レシーバ位置の構文（§3.4、§4.4、§5.1〜5.3、§5.5）である。第二段は `@move`（§3.1、§3.3、§4、§6）、主語の取得と `try`（§4.3、§7、`try` の段）、キャプチャ、`writeLine` である。各段で milestones と examples を書き換え、`verify.ps1` で検証する。

### 10.3. 性能

- Copy の place の Moved 状態は静的検査だけで管理し、実行時フラグや消去を要しない。追跡対象は解決済みの転送操作（`@move`、`@owner`、owning の `@s`）と Move Path から集め、それ以外の Copy の place では追跡しない。
- 転送後は元の値を保持する必要がないので、大きな Copy struct・固定配列では、Loan・寿命・評価順序を保てる場合に転送先への直接構築や storage の再利用を行える。
- 取得元の型が非 Copy と分かり、候補の引数が所有型と分かっている組み合わせは型推論より前に除外できる。それ以外は通常の推論後に判定する。
- by-value 取得の効果は綴りで決まるので、§8.9 の遅延効果決定は借用系の `@s` と共有読み取り（SharedReadResult、Pattern）に限られる。共通の取得計画を再利用し、判定の重複と割り当てを減らす。
- `writeLine` の借用化は、既存の文字列を複製せずに出力でき、保持のための生成・割り当てを避けられる。一時文字列の破棄は callee から caller に移るだけで、入れ子の式では外側の式の終了まで生存し得るので、出力は独立した文にする。
- 共有 iterator は要素を事前に複製せず、追加のヒープ割り当てを要しない。

## 11. 決定

採用する。不採用とした案は次のとおり。

- `@ref` も必須にする案: 共有借用は元の place を変えないので情報がなく、レシーバに例外が要る。
- Copy 型の `@move` を Copy にする案、`match`／`for` の Copy 主語を Copy にする案: 効果が Copy 能力に依存し、総称本体で一意に決まらない。
- `_ = x` を無印で転送にする案: 所有位置の規則の唯一の例外になり、Copy の field・static・実行時要素の破棄を部分 Move やエラーに変える。
- 裸の借用値と排他参照経由の place に `@uniq` を必須にする案: 変更メソッド内の全ての呼び出しに印が付く。再借用は元の関係を変えない。
- 排他参照経由の place を期待型のない位置でも再借用にする案: 借用を推論して束縛型を変えることになり、§4.2 と矛盾する。
- 所有 closure の Exclusive 呼び出しを暗黙のままにする案: 印なしで所有 place を排他的に貸す例外が残る。
- Copy の取得元に呼び出し中の不変性を保証する案: Copy に Loan を追加することになり、`f(x, x@uniq)` や static call effects の既存の自由度を失う。
- `try` を `return` と同じ最弱にする案: `try a + 1` の意味が変わり利点がない。
- `writeLine` を所有引数のままにする案: 印字が変数を転送し、`@move` が頻出する。
