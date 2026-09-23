# 暗黙の排他レシーバ

## 1. 位置付け

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

`2026-09-23 Explicit Transfer and Exclusive Borrow.md` は取り込み済みである。本書は、その結果として現行仕様にある貸与の規則（§15.1.5）のうち、排他貸与の部分を改める。転送（`@move`）の規則は変えない。

目的は次の三つである。

- メンバー操作の受信者を排他借用するときは印を要らなくする。`tasks@uniq.append(x)` を `tasks.append(x)` と書く。
- 引数などの受信者以外の位置では、呼び出し側の綴りから書き換えが見える状態を保つ。
- 呼び出しの綴りから効果が一意に決まる原則を保つ。効果が Copy 能力、`let`／`var`、経路の種類によって変わらないようにする。

### 1.1. 用語

- **メンバー操作**: 次の操作をいう。
  - メソッド呼び出し（連鎖を含む）
  - accessor（`get`・`set`）の呼び出し
  - 代入・複合代入・インクリメント・デクリメントの代入先への書き込み
  - closure と関数値の直接呼び出し（§7.6.3 で受信者として扱う callee）
- **受信者 place**（Receiver Place）: メンバー操作の受信者として取得される place をいう。受信者に至る経路には、ユーザーコードを呼ばない標準 field・Tuple 要素・固定配列要素・添字要素の射影を含めてよい。一時値は place ではないが、受信者として取得する規則はこれと同じとする（§2）。
- 非束縛呼び出し `Type.method(x, ...)` の `self` は引数であり、受信者 place ではない。
- **主語**（Subject Place）は `match`・`for` の用語として現行どおり用い、受信者 place とは区別する。
- **place**・**借用値**・**一時値**は現行仕様（§3.3〜3.6）に従う。経路（直接・排他参照経由・共有参照経由）は、権限の判定と Movable Place の判定にだけ用いる。

## 2. 貸与の規則

> 新たな転送は常に `@move` で書く。新たな排他貸与は、受信者 place を除いて `@uniq`／`@objuniq` で書く。既存の借用値の再借用には印を要しない。

取得の規則は、取得元の三つの分類と位置で決まる。

| 取得元 | 受信者 place | その他の位置 |
| --- | --- | --- |
| place | 受信者の要求する権限で暗黙に取得する（共有・排他）。所有受信者は、Copy なら Copy し、そうでなければ `@move` を要する | 共有は裸で借用する。排他は `@uniq`／`@objuniq` を要する。所有は、Copy なら Copy し、そうでなければ `@move` を要する |
| 借用値 | 要求されるモードで再借用する。値のモードと経路の権限を超えない | 同左 |
| 一時値 | 要求される権限で暗黙に取得する（排他を含む）。所有された getter 結果は §11.2.3 の制限に従う | 所有権を渡す。借用位置では実体化して共有借用する。排他借用は `@uniq` を要する |

- 排他の取得には、place が排他的に書き込み可能であることを要する。`let` 束縛・引数・共有参照経由の place・`ref` 値からは、印の有無にかかわらず排他の取得はできない。
- 所有受信者（`self: Self`、所有 getter など）の規則は変えない。`x@move.m()` と `holder@move.result` は現行どおりである。
- 受信者 place に明示の `@uniq`／`@objuniq` を付けてもよく、警告は出さない。§15.6.7 の準備経路によって、明示した場合と暗黙の場合は同じ意味になる（`holder@uniq.items.add(1)` は `holder.items.add(1)` と同じ）。

```kimi
// describe は ref/Array<Task>、consume は Array<Task> を受け取る。first と second は var ローカル。
public func main()
    var tasks: Array<Task> = []
    tasks.reserve(additional: 4)          // 暗黙の排他借用
    tasks.append(Task.init(1))
    tasks.insert(0, Task.init(2))
    let last = tasks.remove(^1)           // 結果は所有値。Loan は呼び出しで終わる
    describe(tasks)                       // ref/Array<Task> 引数への共有借用
    Kimi.Intrinsics.swap(first@uniq, second@uniq)  // 引数の排他借用には印が要る
    consume(tasks@move)                   // 転送には印が要る
```

## 3. 受信者 place の範囲

メンバー操作の受信者はすべて受信者 place として取得する。それ以外の位置は受信者 place ではない。

| 形 | 扱い |
| --- | --- |
| メソッド呼び出しと連鎖 `tasks.append(x)`、`builder.add(1).add(2)` | 受信者 place。連鎖では最初の受信者だけを取得し、以降の受信者は前の結果である |
| 射影経由 `holder.items.add(1)`、`items[0].mutate()` | 受信者 place。貸与点は受信者 place 全体（§8） |
| 一時値 `makeBuilder().add(1)` | 受信者として暗黙に取得する |
| closure・関数値の直接呼び出し `next()` | 受信者 place。§7.6.3 の受信者要件で取得する。`Callable` 経由の呼び出しも同じ |
| `uniq/Self` を受け取る custom・computed getter `holder.cache` | 受信者 place |
| setter、代入、複合代入、インクリメント | 受信者 place（現行どおり印は不要） |
| `obj` handle のメンバー呼び出し `h.m()` | 受信者 place。`uniq/Self` の受信者には、`objuniq` の取得または Sealed payload の完全投影（§12.4.4）を暗黙に用いる |
| 基底部分オブジェクト射影（§9.5.1） | 受信者 place。`owner/D → uniq/B` は暗黙に取得する |
| static storage `Registry.entries.append(1)` | 排他借用が許される static place には同じ規則を当てはめる。static の効果検査（§15.6）は変えない |
| 非束縛呼び出し `Type.method(x@uniq, 1)` | 引数（印が要る） |
| closure・関数値を引数で渡す `applyTwice(10, next@uniq)` | 引数（印が要る） |
| キャプチャ `[x@uniq]`、`match`／`for` の主語、期待型のない束縛、`@s` の推論 | 受信者 place ではない（現行どおり） |

```kimi
struct Meter
    var hits: i32 = 0
    public computed reading: i32
        get(self: uniq/Self) -> i32        // 読むたびに hits を数える
            self.hits += 1
            return self.hits

var meter = Meter.init()
let first = meter.reading                  // 受信者 place：暗黙の排他借用

var next = func [var count] () -> i32
    count += 1
    return count
let a = next()                             // Exclusive 呼び出し：暗黙の排他借用
let b = applyTwice(10, next@uniq)          // 引数：印が要る

var iterator = samples[..].iterate()
match iterator.next()                      // 受信者 place。主語は結果の一時値
    .Some(let item) => inspect(item)
    .None => ()
```

## 4. 受信者以外の位置の排他借用

期待型が排他を要求する位置で place から新たに排他借用する場合は、place の経路にかかわらず `@uniq`／`@objuniq` を要する。対象の位置は、引数・注釈付き初期化子・代入の右辺・戻り値・default 値・要素と payload の位置である。現行仕様では排他参照経由の place を暗黙に排他借用していたが、この規則を受信者 place に限る。`uniq` 値そのもの（`r`、`self`）の再借用には、現行どおり印を要しない。

裸の排他参照経由の place は、`uniq` 候補には適用できない。そのため §10.4 にある排他参照経由の place の曖昧さは生じず、引数位置で Copy 能力によって選択が変わることもない。

```kimi
func bump(score: Score) -> Score        // 引数による overload（受信者ではないので §6 の対象外）
func bump(score: uniq/Score) -> ()

struct Game
    var a: Resource
    var b: Resource
    var score: Score
    var items: Array<i32>

    func play(self: uniq/Self)
        self.items.append(1)                            // 受信者 place：印は不要
        Kimi.Intrinsics.swap(self.a@uniq, self.b@uniq)  // 引数：印が要る
        bump(self.score@uniq)                           // 排他候補だけが選ばれる
        bump(self.score)                                // Copy なら by-value 候補、非 Copy ならエラー
        helper(self)                                    // uniq 値の再借用：印は不要

    public computed resource: uniq/Resource
        get(self: uniq/Self) -> uniq/Resource
            return self.a@uniq                          // 戻り値：印が要る

func edit(h: uniq/Holder)
    let c: uniq/Resource = h.item@uniq                  // 注釈付き初期化子：印が要る
```

## 5. 暗黙の排他取得ができない場合

受信者 place を排他取得できない場合はエラーとする。Copy read、一時値の実体化、その他の取得に切り替えることはしない。この規則は、Copy 型の値を Copy して一時値を書き換え、更新を捨ててしまう解釈を排除する。仮にこの解釈を許すと、非 Copy 型では同じ行がエラーになり、Copy 型では黙って更新が失われる。総称本体と具体コードの結果も食い違う。

最初から一時値であるもの（`makePoint().normalize()`）は、従来どおり合法とする。

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32
    func normalize(self: uniq/Self)
        ...

struct Canvas
    var origin: Point
    func render(self)                       // ref/Self
        self.origin.normalize()             // エラー：共有経路。render を self: uniq/Self にする

let p = makePoint()
p.normalize()                               // エラー：let は排他借用できない。var で宣言する

func plot(p: Point)
    p.normalize()                           // エラー：引数は不変。var q = p を使う

func plotRef(p: ref/Point)
    p.normalize()                           // エラー：共有参照。引数を uniq/Point にする

let count = 0
let next = func [var count] () -> i32       // 捕捉が i32 だけなので Copy の closure
    count += 1
    return count
let n = next()                              // エラー：let の closure は Exclusive 呼び出しできない

let meter = Meter.init()
let r = meter.reading                       // エラー：getter の受信者が uniq/Self

var q = p                                   // 合法：Copy を明示する
q.normalize()
makePoint().normalize()                     // 合法：最初から一時値
var next2 = next                            // 合法：closure の Copy を明示する
let c = next2()                             // 1。next2 の count だけが進む
```

## 6. 同名メソッドの受信者の形

一つの型が持つ同名のインスタンス関数は、受信者の形がすべて同じでなければならない。受信者の形とは、`ref/Self`・`uniq/Self`・所有 `Self`・許可されたオブジェクト Semantics の形のどれかをいう。Contract の要件にも同じ規則を適用する。違反は宣言エラーとし、呼び出しの有無やその綴りによらない。

- 引数やラベルが違っても、受信者の形は揃えなければならない。明示引数による overload は現行どおり許す。
- `when` 条件が互いに排他的であっても免除しない。
- 基底と派生の間の同名宣言は、既存の inherited-Name 規則（§6.2.2、§9.5）で既に禁止されている。
- 明示的な特殊化（§8.8）は主宣言の受信者の形を保つ。
- 二つの Contract の同名要件の受信者の形が異なる場合、それらの両方に適合しようとする型では、この規則と適合の衝突規則（§8.4.9）によって一つの宣言で両方の要件を満たせない。
- accessor は対象外とする。一つの Property の `get` と `set` は別の操作だからである。Type 関数（受信者なし）も現行の lookup 規則に従う。

```kimi
struct Counter
    var value: i32
    func peek(self) -> i32 => self.value + 1
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self) -> i32 => self.value + 1   // エラー：next の受信者の形が揃わない

struct Buffer
    func insert(self: uniq/Self, index: isize, value: i32)
    func insert(self: uniq/Self, index: Index, value: i32)   // OK：受信者の形が同じ
    func sorted(self) -> Buffer                               // 新しい値を返す
    func sort(self: uniq/Self)                                // その場で変える
```

### 6.1. 理由

受信者を暗黙に取得すると、受信者の形だけが違う overload は、見えない条件によって別の本体を選ぶ。

```kimi
// 仮に許した場合
struct Counter
    var value: i32
    func next(self) -> i32 => self.value + 1
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value

var counter = makeCounter()
let a = counter.next()          // 所有 place：ref 優先なら進まない。そうでなければ曖昧
let r = counter@uniq
let b = r.next()                // uniq 値：same-Semantics で uniq 版が選ばれ、進む

// 仮に許した場合
struct Cursor
    Self is Copy
    var position: i32
    func advance(self: Self) -> Cursor
        var next = self@move
        next.position += 1
        return next@move
    func advance(self: uniq/Self) -> ()
        self.position += 1

var cursor = makeCursor()
cursor.advance()                // Copy なら by-value 版が Exact で勝ち、結果を捨てるだけになる
                                // Self is Copy を外すと uniq 版が選ばれ、書き換わる
```

優先順位の規則を足しても解決しない。ref を優先しても、`uniq` 値との食い違いと Copy 依存が残る。uniq を優先すると、`let` と `var` で選ばれる本体が変わり、レシーバだけが Exact より上に来る例外が §10.4 に入る。受信者の形を名前ごとに揃えれば、受信者の取得は名前の検索だけで決まり、どちらの問題も生じない。

## 7. 総称の受信者

- 型が `T` の `var` ローカルに対するメンバー操作も、同じ規則で受信者を暗黙に取得する。Contract の要件の受信者の形は §6 により一つに決まる。
- 受信者が総称 Semantics の値（`func f<s/T>(x: s/T)` の `x`）である場合は、§8.9 の遅延効果決定に従う。許容される `s` のすべての束縛で排他取得が可能だと証明できなければ、定義時にエラーとする。
- `F` が Exclusive な `Callable` で `f` が `var` ローカルなら、`f()` は暗黙の排他借用である（§8.6）。引数（不変）である `f` は Exclusive 呼び出しできない。

## 8. 貸与点と呼び出し予約

- 暗黙の排他借用の貸与点は受信者 place 全体とする。これは、ユーザーコードを呼ばない標準射影を最長までたどった place である。実行時添字による要素の Loan は、既存の規則どおり配列全体にかかる。
- 途中に getter などの呼び出しがある場合は、その呼び出しの受信者を §3 で取得し、その後の受信者は結果から取得する。所有された getter 結果は、排他の受信者にはなれない（§11.2.3）。
- 呼び出し予約（§15.6.7）の対象に、受信者 place の暗黙の排他取得を加える。受信者の予約中に引数が共有読み取りをしてもよい。
- 排他の受信者から得た結果が受信者に依存する場合、その結果が生存している間、受信者の排他 Loan は続く（現行どおり）。

```kimi
tasks.insert(tasks.length, Task.init(9))    // 予約中の共有読み取り
holder.items.append(holder.items.length)    // 同上。貸与点は holder.items
// tasks.append(tasks.remove(0))            // エラー：二つの排他取得が重なる
let moved = tasks.remove(0)
tasks.append(moved@move)

struct Inventory
    var items: Array<Item>
    func newest(self: uniq/Self) -> ref/Item during self
        ...

var inventory = makeInventory()
let item = inventory.newest()               // inventory の排他 Loan が item の生存中続く
// inventory.items.append(makeItem())       // エラー：item が生存している
inspect(item)
```

## 9. 命名規約

§6 により、共有版と排他版、新しい値を返す版とその場で変える版には別の名前が要る。標準ライブラリと仕様の例では、次の規約に従う。

| 対 | 規約 | 例 |
| --- | --- | --- |
| 新しい値を返す版／その場で変える版 | 形容詞（過去分詞）／動詞 | `sorted`／`sort`、`advanced`／`advance` |
| 共有参照を返す版／排他参照を返す版 | 排他版に接尾辞 `Uniq` | `tryGet`／`tryGetUniq`、`first`／`firstUniq` |
| 見るだけ／進める・取り出す | 別の動詞 | `peek`／`next` |

この規約は §4.7 に置き、ユーザーコードには強制しない。

## 10. 診断

引数などの位置で印がない場合は、既存の排他借用必須の診断を用い、`x@uniq`／`x@objuniq` を提示する。排他参照経由の place もこの対象に含める。

受信者 place を排他取得できない場合は、原因に応じて次の案内を示す。

| 原因 | 提示 |
| --- | --- |
| `let` 束縛（closure を含む） | `var` で宣言する |
| 引数 | `var local = p@move`、Copy なら `var local = p` |
| `ref/Self` のメソッド内の共有経路 | 外側のメソッドの受信者を `self: uniq/Self` にする |
| `ref` 値 | 引数や束縛の型を `uniq/T` にする |
| 所有された getter 結果 | 結果を `var` ローカルに受けて変更し、Property の `set` で書き戻す |
| 総称 Semantics の受信者 | `s` の制約か、明示の取得 |

Loan の競合では、暗黙の貸与点に「`tasks` を `append` の受信者として暗黙に排他借用」という note を付ける。次の場合も同様に示す。

- 排他の受信者から得た結果が Loan を保持している場合は、貸与点と、Loan を保持している結果の両方を示す。
- 二つの暗黙取得が重なる場合（`tasks.append(tasks.remove(0))`）は、両方の貸与点を示し、先に別の `let` で受けるよう提示する。

§6 の違反は宣言エラーとし、受信者の形が異なる宣言をすべて示す。

## 11. 実装と性能

- **受信者の取得は overload 解決の前に一度だけ決める。** §6 により、受信者の形は名前の検索だけで決まる。候補ごとに受信者の適応を計算して比較する処理をなくし、§10.4 の比較は明示引数だけで行う。候補ごとの適応の記録と、比較の分岐が減る。
- **取得のルーチンを一つにまとめる。** §1.1 のメンバー操作は、すべて同じ受信者取得ルーチンを通す。getter と closure 呼び出しも、メソッド呼び出しと同じ経路で扱う。排他の受信者は常に §15.6.7 の予約として扱い、受信者に「すぐに有効になる排他 Loan」の経路を作らない。
- **暗黙の印は 1 ビットにする。** 受信者の適応ノードに「暗黙」フラグを 1 ビット持たせる。§10 の原因別診断と、KimiCode（LSP）の inlay hint・semantic token がこれを読み、暗黙の排他借用の位置を表示する。追加の割り当ては要らない。
- **生成コードには影響しない。** 暗黙化は束縛段階の変更だけである。排他の受信者は現行どおりアドレスで渡す。§5 により、受信者のための一時的な Copy は作らない。
- **検証**: 裸の受信者を拒否していたハーネスの検査は受理ケースに変える。代わりに次の拒否を、必須の診断つきで加える。
  - §5 の各原因
  - 受信者以外の位置で裸の排他参照経由の place を使う場合
  - §6 の違反

## 12. SPEC.md への影響

正式仕様は本書に依存せず、次を更新する。

- `SPEC.md` の貸与の規則の要約
- §3.4（受信者 place の用語）
- §4.7.1（`values@uniq.append(...)` の説明を改める。命名規約）
- §6.2.2・§7.3（受信者の形を名前ごとに揃える規則。メソッド呼び出しの取得の説明と例）
- §7.6.2〜7.6.3（closure 呼び出しの表を「受信者と同じ」の一文に置き換える。例）
- §8.6（`Callable` の呼び出し）、§8.9（総称 Semantics の受信者）
- §9.5.1（基底射影の暗黙取得。順位の記述を削除する）
- §10.2（適応表の「`T` Place through an exclusive reference → `uniq/T`」行を受信者 place に限る。「A directly owned Place is never exclusively borrowed implicitly」を改める）
- §10.4（ステップ 1 の比較から受信者を除く。排他参照経由の place の曖昧さの段落と例を改める）
- §11.2（`uniq/Self` の getter、§11.2.3 の例）
- §12.4.4（Sealed payload の `uniq/Self` 投影を暗黙に許す）
- §13.5.5（例）
- §15.1.5（貸与の規則の本文）、§15.6.7（予約の対象と例）
- 付録 A・F、documentation-markdown・testing-profile・utf8-formatting の例
- milestones と examples のプログラム（Milestone13・14・29 の受信者から `@uniq` を外す。変更メソッド内で排他参照経由の place を渡す引数と戻り値に `@uniq` を付ける）

## 13. 決定

採用する。§4 により、`2026-09-23 Explicit Transfer and Exclusive Borrow.md` §11 の不採用案「裸の借用値と排他参照経由の place に `@uniq` を必須にする案」のうち、排他参照経由の place を受信者以外の位置で使う部分を採用に改める。借用値の再借用は、現行どおり印を要しない。同 §11 の「所有 closure の Exclusive 呼び出しを暗黙のままにする案」は、受信者 place の規則に含めて採用とする。

不採用とした案は次のとおり。

- **引数を含めて `@uniq` をすべて省略する案**: 呼び出し側で引数の書き換えが見えなくなる。特に Copy 型では、Copy して渡すのか書き換えるのかを区別できない。ref と uniq の overload の曖昧さが戻り、エイリアスのエラーが見えない借用を指すことになる。期待型のない束縛・キャプチャ・`@s` の推論では印が残るので、規則の境界もかえってわかりにくい。
- **暗黙化したうえで優先順位の規則を足す案**: ref 優先では `uniq` 値との食い違いと Copy 依存が残る。uniq 優先では `let`／`var` で本体が変わり、§10.4 に例外が入る（§6.1）。
- **受信者の形だけが違う組のうち、どちらかが `uniq` の組だけを禁止する案**: 「両方が適用可能になり得る」かどうかの判定に、既定値の省略・総称の単一化・`when` 条件の検査が要る。`(Self, ref/Self)` の組では、選ばれる本体の Copy 依存も残る。§6 の規則に置き換えた。
- **暗黙の排他取得ができないとき Copy や一時値に切り替える案**: 更新が失われ、Copy 能力によって結果が変わる（§5）。
- **排他参照経由の place を引数でも暗黙に排他借用する現行規則を残す案**: 引数では書き換えを見せるという根拠と矛盾し、Copy 依存と §10.4 の曖昧さも残る（§4）。
- **冗長な明示の `@uniq` を警告する案**: 明示と暗黙は §15.6.7 により同じ意味であり、警告しても得るものがない。
- **用語を「主語」とする案**: `match`・`for` の Subject Place と衝突する。
