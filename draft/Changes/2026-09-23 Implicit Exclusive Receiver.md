# 暗黙の排他レシーバ

## 1. 位置付け

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

`2026-09-23 Explicit Transfer and Exclusive Borrow.md` は取り込み済みである。本書は現行仕様の貸与の規則（§15.1.5）のうち、排他貸与の部分を改める。転送（`@move`）の規則は変えない。

目的は次の三つである。

- 呼び出しの受信者を排他借用するときは、印を要らなくする（`tasks@uniq.append(x)` を `tasks.append(x)` と書く）。
- 受信者以外の位置では、書き換えが呼び出し側の綴りから見える状態を保つ。
- 効果を綴りだけで一意に決める。Copy 能力、`let`／`var`、経路の種類によって効果が変わらないようにする。

## 2. 用語

- **呼び出し**: メソッド、custom・computed・required accessor、closure と関数値の直接呼び出し（§7.6.3）をいう。
- **受信者 place**（Receiver Place）: 呼び出しの受信者として取得される式をいう。place と一時値のどちらでもよい。受信者までの経路には、ユーザーコードを呼ばない標準 field・Tuple 要素・配列要素の射影を含めてよい。非束縛呼び出し `Type.method(x, ...)` の `self` は引数であり、受信者 place ではない。
- **代入先**: 代入・複合代入・インクリメント・デクリメントの書き込み先は、受信者 place ではない。書き込み権限で取得し、印を要しない。評価順序は §13.7 のとおりである。代入先を探す途中で呼ぶ getter と custom `set` は呼び出しなので、その受信者は受信者 place である。
- **値の種類**: 取得元を次の順に判定して、三つに分ける。経路（直接・排他参照経由・共有参照経由）は、権限の判定と Movable Place の判定にだけ用いる。
  1. **借用値**: `ref`・`uniq`・`objref`・`objuniq` の値をいう。place に格納されたもの（`var r = x@uniq` の `r`、field `link: uniq/T`）と、呼び出しが返した参照を含む。
  2. **所有 place**: 所有値または所有 handle（`obj`・`rc`・`arc`）を保持する place をいう。
  3. **所有一時値**: 1 と 2 のどちらにも当たらない一時値をいう。
- **オブジェクト系の入力**: 所有 handle と、`objref`・`objuniq` の値をいう。それ以外を値型の入力という。
- **主語**（Subject Place）は `match`・`for` の用語として現行どおり用い、受信者 place とは区別する。

## 3. 規則

### 3.1. 貸与の規則

> 転送は常に `@move` で書く。新たな排他貸与は、受信者 place を除いて `@uniq`／`@objuniq` で書く。借用値は自身のモードで再借用し、印を要しない。

受信者 place は、値の種類によらず §3.2 で取得する。その他の位置では次のとおり取得する。

| 値の種類 | その他の位置での取得 |
| --- | --- |
| 借用値 | 要求されるモードで再借用するか、Copy read（§10.2）をする。自身のモードと経路の権限を超えない。印は要らない |
| 所有 place | 共有は裸で借用する。排他は `@uniq`／`@objuniq` を要する。所有は、Copy なら Copy し、そうでなければ `@move` を要する |
| 所有一時値 | 所有はそのまま渡す。共有借用は実体化して行う。排他借用は `@uniq` を要する |

**排他取得の条件.** 所有 place を排他取得するには、その所有 storage が排他的に書き込み可能でなければならない。これは次の場合をいう。

- `var` ローカル
- 書き込み権限のある経路の field（§11.1）
- 可変 static（§15.2.3）

`let` 束縛と所有型の引数の storage は、排他取得できない。所有一時値は排他取得できる。ただし所有された getter 結果は除く（§11.2.3）。借用値は自身のモードに従うので、`let` や引数に保持された `uniq` 値も再借用できる。

```kimi
// describe は ref/Array<Task>、consume は Array<Task>、modify は uniq/Resource を受け取る。
public func main()
    var tasks: Array<Task> = []
    tasks.reserve(additional: 4)                     // 受信者：暗黙に排他取得
    tasks.append(Task.init(1))
    let last = tasks.remove(^1)
    describe(tasks)                                  // 引数：共有借用
    var first = makeResource()
    var second = makeResource()
    Kimi.Intrinsics.swap(first@uniq, second@uniq)    // 引数の排他借用には印が要る
    var r = first@uniq                               // r は借用値
    modify(r)                                        // 再借用：印は不要
    modify(getExclusive())                           // uniq/Resource を返す呼び出し：借用値として再借用
    consume(tasks@move)                              // 転送には印が要る

func touch(target: uniq/Resource)
    target.update()                                  // uniq 引数の再借用：可
```

### 3.2. 暗黙の取得

> 受信者 place `p` の暗黙の取得は、受信者の要求に応じた明示の取得を `p` に補ったものと同じである。補った操作が不正なら、その呼び出しはエラーとする。

| 受信者の要求 | 値型の入力 | オブジェクト系の入力 |
| --- | --- | --- |
| `ref/Self`・`uniq/Self` | `p@ref`・`p@uniq`（借用値なら、そのモードでの再借用） | View Target がちょうど同じ完全な Sealed 型なら、完全な payload 投影 `p@ref/T`・`p@uniq/T`（§13.5.5.1）。そうでなければ `p@objref`・`p@objuniq` とし、ObjectCallCompatible Proven を要する（§12.4.4） |
| `objref/Self`・`objuniq/Self` | 該当しない | `p@objref`・`p@objuniq` |
| 基底 `B` の宣言 | §9.5.1 の射影 | 同左 |
| 所有 `Self`（オブジェクト Semantics の所有形を含む） | 所有一時値はそのまま渡す。所有 place は、Copy なら Copy し、そうでなければ補わない（`p@move.m()` と書く）。借用値は、referent が Copy なら Copy read する | 同左 |

この定義から、次のことが導かれる。

- **代替はない.** 補った操作が不正なら、Copy read・一時値の実体化・その他の取得には切り替えない。Copy した値を書き換えて更新を捨てる解釈は起こらない。
- **既存規則がそのまま効く.** 次のものは、それぞれの明示規則に従う。
  - static（§15.2.3）
  - 総称 Semantics の値（§8.9）
  - `rc`／`arc`（共有のみ、§13.5.5.2）
  - Property の権限（§11.1）
  - 所有された getter 結果（§11.2.3）
- **明示との関係.** 暗黙の取得は、表の操作を明示で書いたものと同じである。値型の入力で排他を要求する受信者なら、`p@uniq.m()` は §15.6.7 の準備経路により `p.m()` と同じになる。表と異なる明示は、書いたとおりの別の操作である（例：`tasks@uniq.length`）。
- **連鎖.** 各呼び出しは、直前の結果を受信者としてこの表で取得する。取得は前の呼び出しを越えて遡らない。予約も呼び出しごとに区切られる（§15.6.7）。

```kimi
struct Meter
    var hits: i32 = 0
    public computed reading: i32
        get(self: uniq/Self) -> i32
            self.hits += 1
            return self.hits

group Registry
    public var meter: Meter = Meter.init()

struct Builder
    func add(self: uniq/Self, value: i32) -> uniq/Self   // 排他参照を返す
    func with(self: Self, value: i32) -> Self            // 所有値を返す
    func view(self) -> ref/Self                          // 共有参照を返す
    func finish(self: uniq/Self)

var meter = Meter.init()
let seen = meter.reading              // meter@uniq を補う
holder.items.append(1)                // holder.items@uniq を補う
let shared = Registry.meter.reading   // 可変 static：Registry.meter@uniq を補う
makeResource().consume()              // 所有一時値をそのまま渡す

var builder = makeBuilder()
builder.add(1).add(2)                 // 2 回目の受信者は uniq の結果を再借用する
makeBuilder().with(1).finish()        // with の所有結果（一時値）を finish のために排他借用する
// builder.view().finish()            // エラー：ref の結果からは排他取得できない

let count = 0
var next = func [var count] () -> i32
    count += 1
    return count
let a = next()                        // Exclusive 呼び出し：next@uniq を補う
let b = applyTwice(10, next@uniq)     // 引数なので印が要る

holder@uniq.items.append(1)           // holder.items.append(1) と同じ
tasks@uniq.length                     // 別の操作：tasks を排他貸与してから共有で読む
```

補った操作が不正になる例は次のとおりである。

```kimi
struct Point
    Self is Copy
    var x: i32
    var y: i32
    func normalize(self: uniq/Self)
        ...

let p = makePoint()
p.normalize()             // エラー：p は let。Copy を書き換える解釈はしない

func plot(p: Point)
    p.normalize()         // エラー：所有型の引数

func plotRef(p: ref/Point)
    p.normalize()         // エラー：共有権限しかない

let tick = func [var count] () -> i32   // 捕捉が i32 だけなので Copy の closure
    count += 1
    return count
let n = tick()            // エラー：let の closure。Copy を進める解釈はしない

var q = p                 // 合法：Copy を明示する
q.normalize()
makePoint().normalize()   // 合法：最初から一時値
```

### 3.3. 受信者以外の位置

期待型が排他を要求する位置では、所有 place からの排他借用に、経路にかかわらず `@uniq`／`@objuniq` を要する。対象の位置は、引数・注釈付き初期化子・代入の右辺・戻り値・default 値・要素と payload の位置である。現行の §10.2 は排他参照経由の place を暗黙に排他借用していたが、これを受信者 place に限る。

このため、裸の所有 place が `uniq`／`objuniq` の引数候補に合うことはなくなる。§10.4 の曖昧さは生じず、Copy 能力によって選択が変わることもない。借用値の再借用と Copy read はこれまでどおりである。

```kimi
func bump(score: Score) -> Score     // 引数による overload は従来どおり可
func bump(score: uniq/Score) -> ()

struct Game
    var a: Resource
    var b: Resource
    var score: Score
    var items: Array<i32>

    func play(self: uniq/Self)
        self.items.append(1)                            // 受信者：印は不要
        Kimi.Intrinsics.swap(self.a@uniq, self.b@uniq)  // 引数：印が要る
        bump(self.score@uniq)                           // uniq 版が選ばれる
        bump(self.score)                                // by-value 版だけが候補。非 Copy ならエラー
        helper(self)                                    // 借用値の再借用：印は不要

    public computed first: uniq/Resource
        get(self: uniq/Self) -> uniq/Resource
            return self.a@uniq                          // 戻り値：印が要る
```

### 3.4. 受信者の形の統一

> メンバー検索が確定させた同名の関数群では、受信者の形が一つでなければならない。

受信者の形とは、`ref/Self`・`uniq/Self`・所有 `Self`・許可されたオブジェクト Semantics の形をいう。群には、受信者を持つ関数だけを数える。

- **型の宣言が作る群**: 宣言エラーとする。次の点に注意する。
  - 引数やラベルが違っても、受信者の形は揃えなければならない。
  - `when` 条件が互いに排他的であっても免除しない。
  - Contract と、その refinement の要件にも適用する。
  - 明示的な特殊化（§8.8）は、主宣言の受信者の形を保つ。
  - 基底と派生の間の同名宣言は、既存の inherited-Name 規則（§6.2.2）で既に禁止されている。
- **制約から集まる群**: 総称引数の制約から見つかる要件の群で形が揃わない場合は、使用した時点でエラーとする。要件を選び分ける構文は追加しない。
- **accessor は対象外**: 一つの Property の `get` と `set` は別の操作だからである。

```kimi
struct Counter
    var value: i32
    func peek(self) -> i32 => self.value + 1
    func next(self: uniq/Self) -> i32
        self.value += 1
        return self.value
    // func next(self, by: i32) -> i32     // エラー：next の受信者の形が揃わない

struct Buffer
    func insert(self: uniq/Self, index: isize, value: i32)
    func insert(self: uniq/Self, index: Index, value: i32)   // OK：形が同じ

contract Reader
    func read(self) -> i32
contract Consumer
    func read(self: uniq/Self) -> i32

func use<T>(value: uniq/T) -> i32
    T is Reader
    T is Consumer
    return value.read()                    // エラー：制約から集まる read の形が揃わない
```

**理由.** 受信者を暗黙に取得したうえで、受信者の形だけが違う overload を許すと、見えない条件によって本体が選ばれる。

```kimi
// 仮に許した場合
func next(self) -> i32               // 見るだけ
func next(self: uniq/Self) -> i32    // 進める
counter.next()     // 所有 place：どちらになるかは順位規則しだい
r.next()           // r: uniq/Counter：same-Semantics で uniq 版

func advance(self: Self) -> Cursor   // 新しい値を返す（Cursor は Copy）
func advance(self: uniq/Self)        // その場で進める
cursor.advance()   // Copy なら by-value 版（更新は捨てられる）。Self is Copy を外すと uniq 版
```

順位規則を足しても解決しない。ref を優先すると、`uniq` 値との食い違いと Copy 依存が残る。uniq を優先すると、`let`／`var` で本体が変わり、§10.4 に例外が入る。受信者の形を揃えれば、受信者の取得計画（§6）は名前だけで決まる。

### 3.5. 貸与点と呼び出し予約

- **貸与点**は受信者 place 全体とする。ユーザーコードを呼ばない標準射影を最長までたどった place である。実行時添字の要素の Loan は、既存の規則どおり配列全体にかかる。経路の途中にある getter は、それ自身の受信者を §3.2 で取得する。
- **呼び出し予約**（§15.6.7）の対象に、受信者 place の暗黙の排他取得を加える。代入先は対象外である（§2）。
- **結果が保持する Loan**: 排他の受信者から得た結果が受信者に依存する場合、結果の生存中は受信者の排他 Loan が続く（現行どおり）。

```kimi
tasks.insert(tasks.length, Task.init(9))    // 予約中の共有読み取り
holder.items.append(holder.items.length)    // 貸与点は holder.items
// tasks.append(tasks.remove(0))            // エラー：二つの排他取得が重なる
let moved = tasks.remove(0)
tasks.append(moved@move)

struct Inventory
    var items: Array<Item>
    func newest(self: uniq/Self) -> ref/Item during self
        ...

var inventory = makeInventory()
let item = inventory.newest()               // item の生存中、inventory の排他 Loan が続く
// inventory.items.append(makeItem())       // エラー
inspect(item)
```

## 4. 命名規約

§3.4 により、共有版と排他版には別の名前が要る。標準ライブラリと仕様の例は次の規約に従い、ユーザーコードには強制しない。規約は §4.7 に置く。

| 対 | 規約 | 例 |
| --- | --- | --- |
| 新しい値を返す版／その場で変える版 | 形容詞（過去分詞）／動詞 | `sorted`／`sort` |
| 共有参照を返す版／排他参照を返す版 | 排他版に接尾辞 `Uniq` | `tryGet`／`tryGetUniq` |
| 見るだけ／進める・取り出す | 別の動詞 | `peek`／`next` |

## 5. 診断

- **受信者以外で印がない場合**: 既存の排他借用必須の診断で `@uniq`／`@objuniq` を提示する。排他参照経由の place もこの対象に含める。
- **受信者 place を取得できない場合**: 補った操作（§3.2）のうち、失敗した検査から診断を作る。原因を固定の分類に当てはめることはしない。主な原因と提示は次のとおりで、これで全部ではない。

| 失敗した検査 | 提示 |
| --- | --- |
| 不変な所有 storage（`let` 束縛、所有型の引数、`let` の closure） | `var` にする。引数なら `var local = p@move`（Copy なら `var local = p`） |
| 共有権限しかない（`ref`・`objref` 値、`rc`／`arc`、共有経路） | 上流を `uniq`／`objuniq` にする（例：外側のメソッドを `self: uniq/Self` にする） |
| Property の権限（custom `set` を持つ stored Property の直接の排他借用、setter へのアクセス） | ローカルに受けて変更し、`set` で書き戻す |
| 所有された getter 結果（§11.2.3） | 同上 |
| 受信者が不完全、ObjectCallCompatible が Proven でない | 既存の診断をそのまま用いる |

- **Loan の競合**: 暗黙の貸与点に「`tasks` を `append` の受信者として暗黙に排他借用」という note を付ける。結果が Loan を保持している場合は、その結果も示す。二つの暗黙取得が重なる場合は、両方の貸与点を示す。別のローカルで先に受けるよう提示するのは、受けた値の依存関係がその呼び出しを許す場合だけとする（§15.6.7 と同じ）。
- **§3.4 の違反**: 形が揃わない宣言または要件を、すべて示す。

## 6. 実装と性能

- **束縛**: 暗黙の取得は、明示の借用ノードに「暗黙」ビットを 1 つ付けて表す。所有権解析・予約・lowering は明示の場合と同じノードを処理する。暗黙ビットを読むのは §5 の診断と KimiCode（LSP）の inlay hint・semantic token だけにする。
- **受信者の取得計画**: 受信者の形は、宣言の束縛時に関数群ごとに 1 バイトで記録する。検査は群の要素数に比例する一回だけで済む。overload 解決の前には、群で共通の取得計画だけを作る。取得計画は、受信者の経路の解析、基本のモード、射影の共通部分からなり、1 バイトの記録はその索引に使う。
  - 候補ごとの検査は、既存の適用可能性の判定（§10.1）で行う。対象は Origin 条件、引数や結果との依存関係、ObjectCallCompatible の状態である。
  - Loan と取得は、選択の後に確定させる（§15.6.7）。
  - 受信者の適応の分類は群で共通なので、§10.4 の比較は明示引数だけで行う。
  - 制約から集まる群はキャッシュしてよい。キャッシュの鍵には、束縛された制約・型・Origin の環境を含める。
- **候補の早期判定**: 型推論の前に、各引数と候補の組を「適用可能／不適用／未確定」に分け、確実に不適用な候補だけを除外する。確実に不適用なのは次の二つである。型が未確定なら未確定とし、除外しない。
  - 裸の所有 place と `uniq`／`objuniq` の引数（新たな排他借用が要るため）
  - 非 Copy が確定した裸の所有 place と by-value の引数

  借用値は、再借用も Copy read（`uniq/i32` から `i32` への読み取りなど）もありうるので、除外しない。除外の理由は診断にそのまま使う。
- **生成コード**: 暗黙化そのものによって、Copy・ヒープ確保・参照カウント操作が増えることはない。排他の受信者を成り立たせるための代わりの Copy はしない（§3.2）。所有受信者のための Copy と一時値の実体化は現行どおりで、実体化は追加のヒープ確保を意味しない。
- **検証**: 文字列が残っていないかではなく、意味ごとのケースで確かめる。
  - 裸の排他受信者を受理する。現行ハーネスで裸の受信者を拒否している検査は、受理ケースに変える。
  - 受信者以外での新たな排他借用には印が要る。排他参照経由の place も含む。
  - 明示の取得は、指定した効果を保つ（例：`tasks@uniq.length`）。冗長な明示も受理する。
  - §5 の主な原因と §3.4 の違反は、必須の診断つきの拒否ケースにする。

## 7. SPEC.md への影響

正式仕様は本書に依存せず、次を更新する。

- **貸与の規則**: `SPEC.md` の要約、§15.1.5、付録 E（Lending rule）
- **用語**: §3.4（受信者 place、値の種類）、§13.7（代入先は受信者 place ではない）
- **受信者**
  - §7.3（取得の説明と例）
  - §7.6.3（closure 呼び出しの表を「受信者として取得する」の一文に置き換える）
  - §8.6（Callable の `uniq` 取得）
  - §9.5.1（基底射影を補う操作に含め、順位の記述を削除する）
  - §11.2・§11.2.3（`uniq/Self` の getter と例）
  - §12.4.4（Sealed payload 投影の選択規則を共有と排他で共通にする）
- **overload**
  - §6.2.2・§7.3・§9.5（受信者の形の統一、制約から集まる群）
  - §10.2（排他参照経由の 2 行を削除する。「A directly owned Place is never exclusively borrowed implicitly」を改める）
  - §10.4（ステップ 1 から受信者を除く。排他参照経由の曖昧さの段落と例を削除する）
- **予約と全体更新**: §15.6.7（予約の対象）、§15.7（「排他参照経由の place は通常の再借用を使う」という本文と例）
- **命名規約と例**
  - §4.7.1（規約と例）
  - §13.1・§13.5.5・§14・§15.6 の例
  - 付録 A・F、documentation-markdown・testing-profile・utf8-formatting の例
  - milestones と examples（受信者の `@uniq` を外す。受信者以外で排他参照経由の place を使う箇所に `@uniq` を付ける）

**完了条件**: 旧規則が必須としていた印と説明を、すべて更新する。候補の箇所は、受信者位置の `@uniq.`・`@objuniq.` と、「through an exclusive reference」「lent exclusively」「without a spelling」の検索で洗い出す。ただし一律には消さず、一つずつ意味を確かめる。効果の違う明示（`tasks@uniq.length`）と冗長な明示の受理例は残してよい。

## 8. 決定

採用する。`2026-09-23 Explicit Transfer and Exclusive Borrow.md` §11 の不採用案は、次のとおり扱いを改める。

- 「裸の借用値と排他参照経由の place に `@uniq` を必須にする案」のうち、排他参照経由の place を受信者以外の位置で使う部分を採用に改める。借用値の再借用は、現行どおり印を要しない。
- 「所有 closure の Exclusive 呼び出しを暗黙のままにする案」は、受信者 place の規則に含めて採用に改める。

本書で不採用とした案は次のとおり。

- **引数を含めて `@uniq` をすべて省略する案**: 引数の書き換えが見えなくなる。Copy 依存と ref／uniq の曖昧さが戻る。
- **暗黙化したうえで順位規則を足す案**: 依存する条件が Copy 能力から `let`／`var` へ移るだけである（§3.4）。
- **`uniq` を含む組だけを禁止する案**: 適用可能性が重なるかどうかの判定が複雑になり、`(Self, ref/Self)` の組の Copy 依存も残る。
- **Copy や一時値に切り替えて呼ぶ案**: 更新が失われる（§3.2）。
- **排他参照経由の place を引数でも暗黙に排他借用する案**: 引数の書き換えを見せるという根拠と矛盾する（§3.3）。
- **代入先を受信者 place に含める案**: §13.7 の評価順序と §15.6.7 の予約規則とぶつかる。代入先はもともと印を要しない。
- **冗長な明示の `@uniq` を警告する案**: 排他を要求する受信者では、明示と暗黙は同じ意味である。
- **用語を「主語」とする案**: `match`・`for` の Subject Place と衝突する。
