# 暗黙の排他レシーバ

## 1. 位置付け

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

`2026-09-23 Explicit Transfer and Exclusive Borrow.md` は取り込み済みである。本書は現行仕様の貸与の規則（§15.1.5）のうち、排他貸与の部分を改める。転送（`@move`）の規則は変えない。

目的は次の三つである。

- 呼び出しの受信者を排他借用するときは、印を要らなくする（`tasks@uniq.append(x)` を `tasks.append(x)` と書く）。
- 受信者以外の位置では、書き換えが呼び出し側の綴りから見える状態を保つ。
- 受信者の取得モードを、確定した宣言（メソッドの受信者、closure の呼び出し要件）と明示の操作だけから決める。Copy 能力、`let`／`var`、経路の種類によってモードは変わらない。これらは合法性だけに影響する。

## 2. 用語

- **呼び出し**: メソッド、custom・computed・required accessor、closure と関数値の直接呼び出し（§7.6.3）をいう。
- **受信者式**（Receiver Expression）: 呼び出しの受信者の位置にある式をいう。place と一時値のどちらでもよい。非束縛呼び出し `Type.method(x, ...)` の `self` は引数であり、受信者式ではない。
- **貸与点**: §3.2 で確定した借用・再借用・射影の対象となる storage をいう。取得元ごとに次のとおりである。
  - 所有 place: ユーザーコードを呼ばない標準射影（field・Tuple 要素・配列要素）を最長までたどった place
  - 借用値: その referent
  - オブジェクト借用: 対象の object。payload 投影では payload
  - 基底射影: 選択された基底 subobject

  対象の有効性を保つために参照 slot や所有者を保護することは、既存の依存関係の規則（§15.6.7）に従う。これは貸与点には含めない。
- **代入先**: 代入・複合代入・インクリメント・デクリメントの書き込み先は、受信者式ではない。書き込み権限で取得し、印を要しない。評価順序は §13.7 のとおりである。代入先を探す途中で呼ぶ getter と custom `set` は呼び出しなので、それらの受信者は受信者式である。
- **値の種類**: 取得元を次の順に判定して、三つに分ける。経路（直接・排他参照経由・共有参照経由）は、権限の判定と Movable Place の判定にだけ用いる。
  1. **借用値**: `ref`・`uniq`・`objref`・`objuniq` の値をいう。place に格納されたもの（`var r = x@uniq` の `r`、field `link: uniq/T`）と、呼び出しが返した参照を含む。
  2. **所有 place**: 所有値または所有 handle（`obj`・`rc`・`arc`）を保持する place をいう。
  3. **所有一時値**: 1 と 2 のどちらにも当たらない一時値をいう。
- **オブジェクト系の入力**: 所有 handle と、`objref`・`objuniq` の値をいう。それ以外を値型の入力という。
- **主語**（Subject Place）は `match`・`for` の用語として現行どおり用い、受信者式とは区別する。

## 3. 規則

### 3.1. 貸与の規則

> place からの転送は `@move` で書く。所有一時値は印なしで渡る。新たな排他貸与は、受信者式を除いて `@uniq`／`@objuniq` で書く。借用値は自身のモードで再借用し、印を要しない。

受信者式は、値の種類によらず §3.2 で取得する。その他の位置では次のとおり取得する。

| 値の種類 | その他の位置での取得 |
| --- | --- |
| 借用値 | 要求されるモードで再借用するか、Copy read（§10.2）をする。自身のモードと経路の権限を超えない。印は要らない |
| 所有 place | 共有は裸で借用する。排他は `@uniq`／`@objuniq` を要する。所有は、Copy なら Copy し、そうでなければ `@move` を要する |
| 所有一時値 | 所有はそのまま渡す。共有借用は実体化して行う。排他借用は `@uniq` を要する |

**排他取得の条件.** 所有 place と所有一時値を排他取得するには、借用する storage が排他的に書き込み可能でなければならない。

- root が `var` ローカル・可変 static（§15.2.3）・所有一時値であれば、書き込み可能である。
- 標準射影（field・Tuple 要素・配列要素）は root の権限を引き継ぎ、Property の権限（§11.1）や Loan の制限を加える。例：`values[i].update()`（§4.6）。
- 次の storage は排他取得できない。
  - `let` 束縛や所有型の引数を root とする storage
  - getter が返した所有一時値の storage と、そのインライン部分（§11.2.3）

返された参照や handle が指す別の実体は、その実体の権限に従う。可否は、どの式から来たかではなく、借用する storage で判定する。借用値は自身のモードに従うので、`let` や引数に保持された `uniq` 値も再借用できる。

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

> 受信者式 `p` の暗黙の取得は、受信者の要求に応じて次の表の操作を `p` に補ったものと同じである。判定は次の順に行い、いずれかが成り立たなければ、その呼び出しはエラーとする。

1. 補った操作が合法である。
2. その経路の受信者適合が成り立つ。通常は、結果の完全型が要求型に適合することをいう。オブジェクト借用の経路はオブジェクト受信者の適合（§12.4.3〜12.4.4）、基底射影の経路は §9.5.1 の適合に従う。たとえば `objuniq/T` から `uniq/Self` への適合は、型変換ではなくこの経路の規則で成り立つ。
3. 選択後の使用条件（ObjectCallCompatible など）が成り立つ。

| 受信者の要求 | 値型の入力 | オブジェクト系の入力 |
| --- | --- | --- |
| `ref/Self`・`uniq/Self` | `p@ref`・`p@uniq`（借用値なら、そのモードでの再借用） | View Target がちょうど同じ完全な Sealed 型なら、完全な payload 投影 `p@ref/T`・`p@uniq/T`（§13.5.5.1）。そうでなければ `p@objref`・`p@objuniq` |
| `objref/Self`・`objuniq/Self` | 該当しない | `p@objref`・`p@objuniq` |
| 基底 `B` の宣言 | §9.5.1 の射影 | 同左 |
| 所有受信者（`Self`、オブジェクト Semantics の所有形） | 次の操作に限る。所有一時値はそのまま渡す。所有 place は、Copy なら Copy し、そうでなければ補わない（`p@move.m()` と書く）。`ref/T`・`uniq/T` の値は、referent が Copy なら Copy read する（§10.2） | 所有一時値をそのまま渡すことに限る。所有 place は `p@move` を要する。オブジェクト借用からの Copy read はない |

この定義から、次のことが導かれる。

- **代替はない.** 補った操作が不正なら、Copy read・一時値の実体化・その他の取得には切り替えない。Copy した値を書き換えて更新を捨てる解釈は起こらない。
- **既存規則がそのまま効く.** 次のものは、それぞれの明示規則に従う。
  - static（§15.2.3）
  - 総称 Semantics の値（§8.9）
  - `rc`／`arc`（共有のみ、§13.5.5.2）
  - Property の権限（§11.1）
  - getter 結果の storage（§11.2.3）
- **ObjectCallCompatible は選択の後に検査する.** 取得経路として保護されたオブジェクト借用や基底射影を選んだ場合は、通常の overload 選択の後に、選ばれた候補について Proven を要求する（§12.4.4.1、§9.5.1）。完全な Sealed payload 投影は通常の完全値への呼び出しなので、Proven を要しない（§12.4.4）。Proven は候補の除外に使わず、不足しても別の候補を選び直さない。
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

let count: i32 = 0
var next = func [var count] (value: i32) -> i32
    count += 1
    return value + count
let a = next(1)                       // 2。Exclusive 呼び出し：next@uniq を補う
let b = applyTwice(10, next@uniq)     // 15。§8.6 の applyTwice。引数なので印が要る

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

期待型が排他を要求する位置では、所有 place からの排他借用に、経路にかかわらず `@uniq`／`@objuniq` を要する。対象の位置は、引数・注釈付き初期化子・代入の右辺・戻り値・default 値・要素と payload の位置である。現行の §10.2 は排他参照経由の place を暗黙に排他借用していたが、これを受信者式に限る。

このため、裸の所有 place が暗黙に `uniq`／`objuniq` の候補へ切り替わることはなく、§10.4 の曖昧さも生じない。by-value の候補と共有借用の候補の間では、従来どおり Copy 能力が選択に影響する。借用値の再借用と Copy read もこれまでどおりである。

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

- **貸与点**（§2）: 実行時添字の要素の Loan は、既存の規則どおり配列全体にかかる。経路の途中にある getter は、それ自身の受信者を §3.2 で取得する。
- **呼び出し予約**（§15.6.7）の対象に、受信者式の暗黙の排他取得を加える。代入先は対象外である（§2）。
- **結果が保持する Loan**: 排他の受信者から得た結果が受信者に依存する場合、結果の生存中は受信者の排他 Loan が続く（現行どおり）。

```kimi
tasks.insert(tasks.length, Task.init(9))    // 予約中の共有読み取り
holder.items.append(holder.items.length)    // 貸与点は holder.items
holder.link.update()                        // link: uniq/T。貸与点は referent。link の slot は保護されるだけ
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
共通方針として、修正案は、修正後の取得・権限・Loan の条件をすべて満たす場合だけ示す。

- **受信者式を取得できない場合**: 補った操作（§3.2）のうち、失敗した検査から診断を作る。主な原因と提示は次のとおりで、これで全部ではない。

| 失敗した検査 | 提示（共通方針を満たす場合） |
| --- | --- |
| 不変な所有 storage（`let` 束縛、所有型の引数、`let` の closure） | `var` にする。引数なら `var local = p@move`（Copy なら `var local = p`） |
| 共有権限しかない（`ref`・`objref` 値、`rc`／`arc`、共有経路） | 上流を `uniq`／`objuniq` にする（例：外側のメソッドを `self: uniq/Self` にする） |
| 直接の排他借用が禁止された storage（custom `set` を持つ stored Property、getter 結果の storage） | 値をローカルに受けて変更し、`set` で書き戻す。値を取得できない場合と、`set` にアクセスできない場合は示さない |
| `set` へのアクセス、受信者の不完全さ | 既存の診断をそのまま用いる |

- **ObjectCallCompatible が Proven でない場合**: 選択の後の既存の診断を用いる（§3.2）。
- **Loan の競合**: 暗黙の貸与点に「`tasks` を `append` の受信者として暗黙に排他借用」という note を付ける。結果が Loan を保持している場合は、その結果も示す。二つの暗黙取得が重なる場合は、両方の貸与点を示す。別のローカルで先に受ける案は、共通方針を満たす場合だけ示す（§15.6.7 と同じ）。
- **§3.4 の違反**: 形が揃わない宣言または要件を、すべて示す。

## 6. 実装と性能

言語仕様として保証するのは、§3.2 の操作の等価性だけである。以下のデータ表現は実装上の選択肢の一つである。

- **束縛**: 暗黙の取得は、確定した取得操作（借用、再借用、Copy、Copy read、一時値の受け渡し、射影）に対応する既存のノードを再利用し、暗黙に由来することを記録する（例：1 ビット）。所有権解析・予約・lowering は、明示の場合と同じノードを処理する。暗黙の記録を読むのは §5 の診断と、KimiCode（LSP）の inlay hint・semantic token だけにする。
- **受信者の取得計画**: 受信者の形は、宣言の束縛時に関数群ごとに記録する（例：1 バイト）。検査は群の要素数に比例する一回だけで済む。overload 解決の前には、群で共通の取得計画だけを作る。取得計画は、受信者の経路の解析、基本のモード、射影の共通部分からなる。形の記録は、その索引として使う。
  - Origin 条件や、引数・結果との依存関係など、候補ごとの型の適合性は、既存の適用可能性の判定（§10.1）で調べる。
  - ObjectCallCompatible は、選ばれた候補についてだけ照会する（§3.2）。
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
  - 暗黙形と、表の操作を明示した形を対にして、次の項目が一致することを確かめる。
    - 評価の順序と回数（副作用のある添字や getter を含む受信者を、一度だけ評価する）
    - 予約（予約中の共有読み取りを許し、重なる排他取得を拒否する）
    - 戻り値が保持する Loan の期間
    - オブジェクトの経路（Sealed payload 投影では Proven を要さず、保護されたオブジェクト経路では要する）
    - 暗黙化によって、Copy・ヒープ確保・参照カウント操作が増えないこと

## 7. SPEC.md への影響

正式仕様は本書に依存せず、次を更新する。

- **貸与の規則**: `SPEC.md` の要約、§15.1.5、付録 E（Lending rule）
- **用語**: §3.4（受信者式・貸与点・値の種類）、§13.7（代入先は受信者式ではない）
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
- 「所有 closure の Exclusive 呼び出しを暗黙のままにする案」は、受信者式の規則に含めて採用に改める。

本書で不採用とした案は次のとおり。

- **引数を含めて `@uniq` をすべて省略する案**: 引数の書き換えが見えなくなる。Copy 依存と ref／uniq の曖昧さが戻る。
- **暗黙化したうえで順位規則を足す案**: 依存する条件が Copy 能力から `let`／`var` へ移るだけである（§3.4）。
- **`uniq` を含む組だけを禁止する案**: 適用可能性が重なるかどうかの判定が複雑になり、`(Self, ref/Self)` の組の Copy 依存も残る。
- **Copy や一時値に切り替えて呼ぶ案**: 更新が失われる（§3.2）。
- **排他参照経由の place を引数でも暗黙に排他借用する案**: 引数の書き換えを見せるという根拠と矛盾する（§3.3）。
- **代入先を受信者式に含める案**: §13.7 の評価順序と §15.6.7 の予約規則とぶつかる。代入先はもともと印を要しない。
- **冗長な明示の `@uniq` を警告する案**: 排他を要求する受信者では、明示と暗黙は同じ意味である。
- **用語を「主語」とする案**: `match`・`for` の Subject Place と衝突する。
