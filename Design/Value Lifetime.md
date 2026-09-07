# Value Lifetime

Kimigayo は、値の取得、配置、破棄を **Value Lifetime** として扱う。本書はその仕様追加案である。

取得は Copy / Move、配置は Initialization / Replacement、破棄は Destruction で表す。一つの代入が Move と Replacement を含むこともある。

本書では **借用（borrow）**、**所有権（ownership）**、**集合値（aggregate）** と表記する。例中の API は説明用であり、`place`・`storage` を使う状態遷移の記述は概念表記であってソース構文ではない。

Property の具体的な Move out と標準アクセスによる再初期化は、[Property Move Access（暫定仕様）](Property%20Move%20Access.md) で定義する。本書はその共通となる状態・所有権・破棄規則を定める。

## 1. 基本モデル

### 1.1. 保持場所と状態

**Storage** は値を保持する領域、**place** はその中の位置を表す。Compiler は place ごとに初期化状態と破棄責任を追跡する。

| 状態 | 意味 | 読み取り・借用・Copy・Move | `let` の初期化 | `var` の書き込み |
| --- | --- | --- | --- | --- |
| Uninitialized | 初期化済みの値を保持していない | 不可 | 可（初回） | 可（Initialization） |
| Initialized | 初期化済みの値を保持している | 型とアクセス規則に従う | 不可 | 可（Replacement） |
| Moved | Move によって未初期化になった | 不可 | **不可** | 可（再初期化） |

Uninitialized と Moved は、読み取り側から見た可否が同じであり、`let` の初期化可否だけが異なる。`Moved` は値が移動先で生存していることを表し、初回初期化を済ませた事実を保持する。

Initialization は空の place を Initialized にする。Destruction は値の lifetime と破棄責任を終了させ、正常完了後も storage が存続する場合はその place を Uninitialized にする。

Storage の有効期間が終わると、初期化状態にかかわらずその place にアクセスできない。

集合値では構築完了と各部分の状態を区別し、全体を使用できる条件を §2.2 で定める。

### 1.2. 所有権と破棄責任

所有する値の Move は、所有権と破棄責任を移す。元の place で同じ値を再び破棄してはならない。

借用値の Copy / Move はアクセス能力の複製・移転であり、借用先の所有権を取得しない。借用値の破棄は参照先を破棄しない。`rc/T`・`arc/T` の破棄は所有参照を解放し、object 自体の破棄条件は各 Type Semantics に従う。

`unsafe/T` は所有権を持たない raw pointer であり、`T` にかかわらず Copy である。参照先の破棄責任や storage の解放責任を持たず、pointer 自体の Copy / Destruction は参照先を複製・破棄・解放しない。実際のアクセス条件は Unsafe 仕様に従う。

### 1.3. Property と内部 storage

**Stored field** は集合値が保持する実データであり、状態と破棄責任の追跡対象になる。Computed Property は、それ自体の storage を持たない。

通常の Property 読み取りは getter、初期化後の代入は setter を通す。標準 getter は Copy または共有借用を行い、storage から Move しない。したがって `value.field` と書くだけでは、その field は Moved にならない。

明示的な `move receiver.property` は getter を呼ばず、宣言された Move access を使う。静的に特定できる所有 place の標準 getter / setter は、Property Move Access の条件に従って field 単位で処理できる。

```text
struct Person
    var name: string has get, move, set
    var age: i32

var person = makePerson()
let borrowed = person.name        // 標準 get：ref/string。storage は Initialized のまま。
let owned = move person.name      // Move access：storage は Moved。person は incomplete。
person.name = "Alice"             // 標準 set：旧値を破棄せず再初期化。person は complete。
```

本書の直接 field 操作と交換例は、許可された内部 storage の操作を表す。Place であることを根拠に accessor やアクセス制限を迂回してはならない。具体的な構文・API は担当仕様で定義する。

## 2. 初期化

### 2.1. 確実な初期化と書き込み権限

値を読み取り、借用、Copy、Move する位置では、そこに到達するすべての実行経路で対象が Initialized でなければならない。

```text
var value: i32
if condition
    value = 1
else
    value = 2
print(value)               // OK：すべての経路で Initialized。
```

```text
var other: i32
if condition
    other = 1
print(other)               // Error：condition が false の経路で Uninitialized。
```

初期化状態は書き込み権限を与えない。Local binding の生存期間中、`let` は各実行経路で初回の初期化だけを許可し、`var` は通常のアクセス規則に従って再初期化・再代入できる。

```text
let a: Resource
a = makeResource()        // OK：初回の初期化。
consume(a)                // Move。a は Moved。
a = makeResource()        // Error：Moved は Uninitialized ではなく、初回初期化の事実が残る。
```

Stored `let` Property も初回初期化後の再設定を許可しない。初期化・再初期化は、Property のアクセス制限や専用の初期化規則に従う。

### 2.2. 構築完了と集合値の完全性

集合値では、次の二つを追跡する。

| 情報 | 意味 |
| --- | --- |
| 構築完了 | その値の初期化処理が成功し、完了点に到達した事実 |
| 現在の完全性 | すべての stored field が Initialized であること |

両方を満たす集合値を **complete value** と呼ぶ。全 field の設定だけでは、構築完了とは限らない。本改訂ではすべての stored field を完全性の対象とし、初期化を省略できる field は導入しない。

Constructor や段階的初期化には完了点を設ける。必要な初期化と成功条件を確認し、構築完了を確定してから値全体を使用・転送する。Constructor の成功結果もこの順で確保し、その後は通常の Scope Exit に従う。失敗出口では完了を確定しない。

**明示的な完了点構文がない場合、constructor body の正常完了を完了点とする。** Panic や失敗出口で離脱した場合は完了を確定しない。明示構文は各初期化仕様で定義する。

Complete になる前は、全体の読み取り・借用・Move・外部への公開を禁止する。未完成の `self` 全体を receiver とする通常の getter / setter も呼び出せない。初期化済み field の直接操作は、その field のアクセス規則に従う。構築完了後の Partial Move では、Property Move Access が定める標準 getter / setter を全体の借用を作らない field 操作として利用できる。この例外を初回構築中のアクセス許可に拡張しない。

Partial Move は現在の完全性だけを失わせる。欠けた部分の再初期化が許可されていれば、constructor の再実行なしに complete に戻る。再初期化が許可されない場合、その値は complete に戻らず、以後は全体としての使用・転送ができない。全体の Move は構築完了の情報も移し、全体の置換では新しい値の情報を使う。

## 3. Copy と Move

### 3.1. 操作の意味と使用条件

**Copy** は値を暗黙に複製し、元の place を Initialized のまま保つ。元の値の破棄責任は変わらない。Copy 自体はユーザー定義コード、heap allocation による複製、参照カウントの変更、resource acquisition を実行しない。

**Move** は値と破棄責任、または借用値のアクセス能力を移転し、元の place を Moved にする。ユーザー定義コードを呼び出さず、元のメモリの消去も必要としない。

通常の値取得では、完全な Type が Copy なら Copy、non-Copy なら Move を選び、必要な操作が許可されなければ compile error とする。初期化、代入の右辺、値渡し引数、明示・暗黙の結果転送に共通の規則である。明示的な Property Move access は例外として Copy 型でも Move を行い、元の field を Moved にする。借用の生成と再借用は別の操作であり、名前を書くだけでは値の消費を意味しない。

型の Copy capability と、使用位置での操作の許可は区別する。Copy は読み取りとして Loan 規則に従い、Move は重なる有効な Loan と競合してはならない。Copy / Move は Origin の依存を保持し、借用先の lifetime を延長しない。`let` / `var` は型の Copy capability を変えない。

### 3.2. Copy capability

#### 3.2.1. 型の分類

Copy capability は Core Type、Type Semantics、保持する部分の型によって決まる。`T` と `owner/T` は同じ所有型である。

| Type | 分類 |
| --- | --- |
| 所有する整数、浮動小数点、`bool`、`char`、Unit | Copy |
| `ref/T`、`objref/T` | 参照先の `T` にかかわらず Copy |
| `uniq/T`、`objuniq/T` | non-Copy |
| `obj/T`、`rc/T`、`arc/T` | `T` が Copy でも non-Copy |
| `unsafe/T` | 参照先の `T` にかかわらず pointer value は Copy |
| Slice | 借用表現に従う。共有借用の Slice は Copy、排他借用の Slice は non-Copy |
| 捕捉を持たない関数値 | Copy |
| 所有する tuple、固定長配列 | すべての要素型が Copy の場合に限り Copy |
| 所有するユーザー定義 struct | 既定は non-Copy。明示 opt-in が必要 |
| 所有する `string` | non-Copy |

Never は値が存在しないため分類しない。表にない型は各 Type 仕様で分類し、要素の共有だけを根拠に Copy としない。

#### 3.2.2. 宣言と制約

`Copy` は Compiler が検証する組み込み capability である。Struct の `Self is Copy` は明示 opt-in を表し、次を満たす場合に Compiler が実装を自動導出する。

- すべての stored field の完全な Type が Copy である。
- ユーザー定義 `deinit` を持たない。

これらを満たす型の複製は Copy な field の複製の合成であり、所有権の重複も Origin の依存の追加も生じない。したがって追加の条件は課さない。

使用位置の Loan は §3.1 で別に検査する。Computed Property は stored field の条件に含めない。全 field が Copy でも opt-in は必要であり、ユーザーは Copy の実装 body を指定できない。

```text
struct Point
    Self is Copy

    var x: i32
    var y: i32

func duplicate<T>(value: T) -> (T, T)
    T is Copy

    return (value, value)
```

`T is Copy` は generic constraint であり、制約や特殊化で確定するまで Copy と仮定しない。自動導出は `Self is Copy` 固有であり、一般の `Self is Capability` には適用しない。

#### 3.2.3. 明示的な複製

Allocation、参照カウント増加、resource duplication を伴う複製は、method や `contract` で表す明示的な API にする。標準の複製 contract は本書では定義しない。

```text
let a: string = "Hello"
let b = a.clone()          // 明示的な複製 API の例。
let c = a                  // Move。以降 a は使用不可。
```

`string` は内部表現を変更しても non-Copy とする。コストや所有権への影響がある複製を明示させるためである。

### 3.3. 部分的な Move と取り出しの制限

#### 3.3.1. Move Path

**Move Path** は、初期化状態と破棄責任を独立して静的に追跡できる place の経路である。Partial Move は、そのような経路に対してのみ許可する。

本案で認める部分の経路は、stored field、tuple element、固定長配列の定数 index、およびこれらの組み合わせである。定数 index は意味解析時に言語の定数評価で確定するものとし、実行時変数の最適化による定数伝播には依存しない。

Runtime index は認めない。動的 container やユーザー定義 indexer は、index が数値リテラルでも自動的には含めない。追加の経路は Type 仕様で明示し、いずれも §1.3 のアクセス制限に従う。

**Move Path は初期化状態を追跡する粒度であり、それ自体がアクセス手段を与えるものではない。** 経路ごとに、通常構文から Partial Move へ到達できるかは次のとおりである。

| 経路 | 通常構文からの Partial Move |
| --- | --- |
| tuple element、固定長配列の定数 index | 直接 place へアクセスするため、通常の値取得がそのまま Partial Move になる |
| struct の stored Property | `value.field` は getter 呼び出しであり Move しない。`move value.field` を使う（Property Move Access） |

所有する集合値の一部を Move すると、その部分は Moved になる。残る初期化済み部分は使用できるが、全体は §2.2 に従って complete に戻るまで使用できない。

```text
var pair: (string, i32) = ("Alice", 30)
let name = pair.0        // Partial Move。pair.0 は Moved、pair.1 は Initialized。
let count = pair.1       // OK：残る初期化済み部分は使用できる。
// let both = pair       // Error：pair は complete ではない。
pair.0 = "Bob"           // 再初期化。
let both = pair          // OK：complete に戻っている。
```

#### 3.3.2. 借用先と deinit を持つ型

`ref/T`・`uniq/T`・`objref/T`・`objuniq/T` を通じて、non-Copy の借用先の全体または一部を通常の Move で取り出し、Moved / Uninitialized にしてはならない。後で再初期化する予定でも禁止する。排他的アクセスは借用先の所有権を移すものではなく、借用値自体の Move とは区別する。

ユーザー定義 `deinit` は complete value を前提とする。所有する集合値でも、自身または包含する祖先集合値の `deinit` の前提を崩す direct Partial Move は禁止する。ネストした field、型自身の method、`deinit` 内も例外にしない。Complete な所有値全体の Move は通常の規則に従う。

通常の Move out ができない対象の取り出しには、§4.3 の交換操作か型固有の操作を使う。交換は初期化状態を維持するが、型固有 invariant は実装側で維持し、必要に応じて storage へのアクセスを制限する。

```text
struct Connection
    var socket: Socket
    deinit
        close(socket)

func Connection.detach(self: uniq/Self, replacement: Socket) -> Socket
    return self.socket                              // Error：借用先からの Move out。
    return Exchange(self.socket, with: replacement)  // OK：常に Initialized を保つ。
```

## 4. 代入と交換

### 4.1. 操作の違い

| 操作 | 旧値 | 新しい値の配置 | 結果 |
| --- | --- | --- | --- |
| Initialization | なし | 空の place を初期化 | 代入なら Unit |
| Replacement | 残る旧値を**破棄する** | 破棄後に配置 | 代入なら Unit |
| `Exchange` | **破棄せず取り出す** | 初期化状態を維持して置換 | 旧値 |
| `Swap` | 二つの値を破棄せず交換 | 両方の初期化状態を維持 | Unit |

> **Replacement と `Exchange` は旧値の扱いが正反対である。** Replacement は単純代入 `=` が旧値を破棄する側面を指す概念名であり、`Exchange` は旧値を破棄せず結果として返す組み込み操作である。

単純代入 `=` は代入先の状態に応じて Initialization / Replacement を行う。`Exchange`・`Swap` は Compiler が一体の交換として意味を保証する操作であり、通常の Move と後続の再代入の組み合わせではない。

### 4.2. 単純代入

#### 4.2.1. 評価順序

`target = value` は一般の左から右への評価に対する例外として、次の順で処理する。

1. **右辺を評価する。** 代入先の Type に適合する一時結果を Copy / Move で確保し、元の place の状態と破棄責任を更新する。
2. **左辺を評価する。** Receiver と index を左から右へ評価し、代入先を一度だけ確定する。代入対象の Property getter は呼び出さない。
3. **残る旧値を破棄する。** その時点で代入先に残っている破棄責任を処理する。
4. **一時結果を配置する。** 通常の Copy / Move 規則で渡し、代入先を Initialized にする。Non-Copy の一時結果の破棄責任は代入先へ移る。

**右辺を先に評価するのは、代入先自身を値の供給元にできるようにするためである（§4.2.4）。** 左辺を先に確定すると、確定済みの代入先から Move する形になり、`Exchange` の制限（§4.3.2）と同型の衝突を生む。代入先の Type は静的に決定するため、左辺を先に評価する必要はない。正常な書き込み後、代入は Unit を返す。

```text
values[nextIndex()] = makeValue()
// makeValue() と結果確保 -> values と nextIndex() -> 旧値の破棄 -> 配置
```

**`=` と複合代入では、この評価順序が意図的に異なる。** 複合代入は旧値の読み出しを伴うため、代入先を先に確定しなければならない。

```text
values[index()] = value()   // value() -> index()
values[index()] += value()  // index() -> 旧値の読み出し -> value()
```

副作用の順序に依存する場合は、receiver や添字を事前にローカルへ束縛して曖昧さを避けること。

```text
let i = index()
values[i] = value()         // 順序が明示される。
```

手順 3 では Uninitialized / Moved な部分を破棄しない。一部だけに値が残る集合値は §5.2 に従って cleanup し、新しい集合値を配置する。

Property への代入では setter を選択する。Custom setter には手順 3・4 の代わりに結果を渡し、その内部の直接書き込みに本節を適用する。Compiler 提供の標準 setter は、Property Move Access の条件を満たせば対象 field に手順 3・4 を直接適用し、Moved field を旧値の破棄なしに再初期化する。Property の初回構築時の初期化は専用規則に従う。

**この結果、Property setter への代入は、通常の method 呼び出しと引数の評価順序が逆になる。**

```text
obj().prop = arg()          // arg() -> obj() -> setter
obj().setProp(arg())        // obj() -> arg() -> 呼び出し
```

複合代入の評価順序と getter 読み取りは Assignment 仕様の別規則に従う。`Exchange`・`Swap` の引数評価は §4.3.1 に従う。

#### 4.2.2. 場所・Origin・Loan の有効性

左辺は右辺評価後の状態を使用する。Moved な `var` local の storage を再初期化先にすることはできるが、Moved な owner や receiver の値を読み取ってはならない。代入先の storage は、確定から書き込みまで有効でなければならない。

**旧値の Destruction により、右辺結果が要求する Origin または Loan の有効性が失われてはならない。** 左辺評価、その一時値の cleanup、代入先へのアクセスにも同じ検査を適用する。配置後も、結果の使用と Destruction に必要な依存が有効でなければならない。

右辺を先に評価するため、右辺結果が「代入先の旧値」への借用を保持したまま手順 3 に到達する形が構文上は書ける。この検査はそれを拒否する。

```text
struct Buffer
    var data: Array<u8>

func wrap(items: ref/Array<u8>) -> Buffer

var buffer = makeBuffer()
buffer = wrap(buffer.data)
// 手順 1：右辺結果が旧 buffer の data への借用を保持する。
// 手順 3：旧 buffer の Destruction がその data を破棄する。
// -> 右辺結果が要求する Loan が無効になるため compile error。

let copied = duplicate(buffer.data)
buffer = wrap(copied)       // OK：旧値への依存を断ってから代入する。
```

同じ storage に新しい値を置いても、旧値への依存は自動的に引き継がれない。

#### 4.2.3. 途中終了

右辺が正常完了しなければ左辺を評価しない。左辺が正常完了しなければ手順 3・4 を行わず、旧値の破棄が正常完了しなければ配置しない。既に生じた副作用や Move は巻き戻さない。

右辺の一時結果は左辺評価中も生存する。通常の制御移動で代入を中断する場合、残る一時値の破棄責任を Temporary Lifetime / Scope Exit の規則で処理する。転送結果を先に確保する順序を守り、破棄される一時値への借用を外へ逃がしてはならない。Panic や処理の停止は §5.5 に従う。

#### 4.2.4. 適用例：自己代入

初期化済みで書き込み可能な所有値について、他のアクセス制限に違反しない `x = x` は、通常の規則から合法になる。

```text
var x = makeResource()     // non-Copy
x = x

// 右辺：x を一時結果へ Move。x は Moved。
// 左辺：x の値を読まず、storage を確定。
// 破棄：旧値が残っていないので何もしない。
// 配置：一時結果を x へ Move。x は Initialized。
```

この代入ではユーザー定義 `deinit` を実行しない。Copy 型なら通常の Copy と Replacement になる。自己代入の特例は設けず、Move out の制限も維持する。Property の `x.p = x.p` は getter / setter の規則に従う。

### 4.3. 初期化状態を維持する交換

#### 4.3.1. 共通条件と Loan の開始

`Exchange(place, with: value)` と `Swap(placeA, placeB)` は、**言語が提供する組み込みの交換操作**である。本書はその意味規則を定義し、最終的な綴りと解決手順は Ownership / 交換 API 仕様で定める。

- 各対象は Initialized であり、書き込みと排他的アクセスが許可されていなければならない。
- 対象と引数は左から右へ評価する。各対象への借用引数を作る時点で排他的 Loan を開始し、後続引数の評価から交換終了まで維持する。
- 交換する値は Origin を含む完全な Type が一致しなければならない。必要な変換と引数評価は交換開始前に完了する。
- 交換中はユーザー定義コード、破棄、Panic を生じる処理、制御移動を実行せず、内部的な空の状態を program から観測させない。

**排他的 Loan の開始時点は、`uniq/Self` を取る通常の method 呼び出しと同一である。** `Exchange` / `Swap` に固有の緩和も強化も設けない。したがって、対象を引数式の中で読み直す形は拒否される。

```text
Exchange(p, with: p + 1)    // Error：対象の排他的 Loan と後続の読み取りが競合する。

let next = p + 1
Exchange(p, with: next)     // OK：借用開始前に計算する。
```

Loan の開始を全引数の評価後へ遅らせる緩和（reservation 方式）はこの形を許容できるが、本改訂では採用しない。必要になった場合は Ownership 仕様の拡張として別途検討する。

引数評価が正常完了しなければ交換せず、一時値を通常の規則で cleanup する。結果の Origin / Loan の依存も維持する。ここでの不可分性は単一スレッド内の観測についての規定であり、スレッド間の atomicity は保証しない。

#### 4.3.2. 値と破棄責任の移転

`Exchange` は交換開始前に replacement を値として確保する。Non-Copy の元の place は Moved になるが、対象 place は Initialized のままでなければならない。対象 place からの通常の Move や競合する借用で replacement を用意してはならない。

```text
交換前：対象 place = old、replacement = new
交換後：対象 place = new、result = old
```

**単純代入では代入先自身を値の供給元にできる（§4.2.4）が、`Exchange` では認めない。** `Exchange` は交換の全期間で対象を Initialized に保つ必要があり、対象からの Move はその不変条件を破るためである。

```text
x = x                      // OK（§4.2.4）
Exchange(x, with: x)        // Error：対象が一時的に空になる。
```

旧値の所有権と破棄責任は結果へ、新しい値の責任は対象 place へ移す。`uniq/Self` を受ける型固有の操作も、許可された field に適用して旧値を返せる。

`Swap` は二つの値と破棄責任を交換し、両方を初期化済みのまま保って Unit を返す。対象が §4.3.3 の解析で重ならないと判定できる場合に限り許可する。

#### 4.3.3. 重なりの静的判定

重なりがないことは、言語仕様で定める静的な place analysis で証明できなければならない。本案では、次の構造規則を適用する。「内部の部分」は storage に直接含まれる field・要素を指し、pointer / reference の参照先は逆参照の規則で別に判定する。

| 対象の関係 | 判定 |
| --- | --- |
| 同じ place、または place とその内部の部分 | 重なる |
| 異なる独立した local storage と、それぞれの内部の部分 | 重ならない |
| 同じ集合値の異なる stored field・tuple element・固定長配列の異なる定数 index と、それぞれの内部の部分 | 重ならない |
| 生存中の二つの `uniq` / `objuniq` 借用の逆参照で、Loan anchor が異なるもの | 重ならない（排他借用は重なる借用と共存できない） |
| その他の参照の逆参照 | Loan の由来を辿り、参照先に同じ規則を適用する |
| 上記で決定できない場合 | 重ならないとは証明できないため、Swap を拒否する |

排他借用の非共存性を利用できるため、二つの排他参照を経由した交換は追加の証明なしに許可される。

```text
func swapValues<T>(a: uniq/T, b: uniq/T)
    Swap(*a, *b)           // OK：a と b の Loan anchor は異なる。
```

定数 index の範囲は §3.3.1 に従う。異なる `ref` 変数や raw pointer 変数であっても、参照先が独立しているとは限らない。異なる集合値の部分として証明できる場合を除き、runtime index の要素同士の非重複は仮定しない。

任意の整数条件や最適化結果からの証明は要求せず、許可判定もそれらに依存させない。例えば `i != j` だけでは、同じ配列の `a[i]` と `a[j]` の `Swap` を許可しない。同一 place 同士も許可しない。これは static な対象経路と Loan の規則を具体化するものであり、アクセス権を追加するものではない。

## 5. 破棄

### 5.1. deinit と特別な receiver

`deinit` は型固有の破棄処理を記述する専用宣言であり、引数や明示的な戻り値型を持たない。

`deinit` の実行は Destruction の手順に限る。ユーザーコードからの明示呼び出し、関数値としての取得、間接呼び出しは compile error とする。通常の method として呼び出せる終了処理が必要なら、別の API を定義する。

`self` は access / Loan の観点では `uniq/Self` と同等の排他的アクセスを持つが、通常の借用値ではなく Destruction に属する特別な receiver である。Receiver 自体を Copy / Move することはできず、破棄対象とは別の自動破棄責任も持たない。

**Destruction 中の self 全体を、代入・Exchange・Swap の対象にしてはならない。** 借用経由の迂回も防ぐため、全体を通常の `uniq/Self` として渡すことを禁止する。これには全体を排他的 receiver とする method / setter 呼び出しも含む。全体の共有借用と、初期化済み field の直接操作・借用は通常の規則に従い、§3.3 の Move out 制限も維持する。

```text
struct Connection
    var socket: Socket
    var log: Logger

    deinit
        var spare = makeConnection()
        Swap(self, spare)                           // Error：self 全体は交換の対象にできない。
        self.reset()                                // Error：uniq/Self を取る method 呼び出し。
        flush(self.log@ref)                         // OK：初期化済み field の共有借用。
        Exchange(self.socket, with: closedSocket())  // OK：field 単位の交換。
```

### 5.2. 集合値の破棄順序

Complete value は次の順で破棄する。

1. ユーザー定義 `deinit` があれば実行する。開始時にはすべての stored field が Initialized である。
2. Body とその Scope Exit が正常完了した後、stored field を宣言の逆順に破棄する。通常の `return` も、この field cleanup を省略しない。

**構築が完了していない集合値では、その集合値自身のユーザー定義 `deinit` を実行せず、初期化済みの stored field だけを通常の field 規則で破棄する。**

Partial initialization / Move の cleanup では、残る部分の破棄責任を宣言の逆順に処理する。**この状態にある集合値は、§3.3.2 と Property Move Access の宣言時検査により、自身にもいずれの祖先にもユーザー定義 `deinit` を持たない。したがってこの cleanup で `deinit` を実行することはない。** 各 field に再帰的に適用し、Uninitialized / Moved な部分は破棄しない。配列や tuple の部分は、それぞれの型が定める順序に従う。

```text
// 宣言順は a, b, c。
a: Initialized
b: Uninitialized または Moved
c: Initialized

cleanup: c -> a
```

### 5.3. Destruction lifetime checking

**Destruction lifetime checking** は、Destruction が観測し得る Origin / Loan に対して、その観測位置での有効性を検査する。Origin / Loan を観測しない Destruction は、追加の lifetime requirement を生じない。

ユーザー定義 `deinit` は、到達可能なすべての Origin を観測し得るものとして扱う。実装 body が実際に参照していないことだけを理由に検査を省略しない。Field の Destruction にも同じ検査を再帰的に適用する。

```text
struct Logger origin sink
    let out: uniq/Writer from sink

    deinit
        observe(self.out)      // Origin sink を実際に観測する。

struct Silent origin sink
    let out: uniq/Writer from sink

    deinit
        ()                     // body は参照しないが、検査は省略しない。
```

どちらの型でも、Origin `sink` は Destruction 位置まで有効でなければならない。この保守的近似を緩和する仕組みは本改訂では定義しない。

### 5.4. Scope Exit

Scope Exit では、離れる scope を内側から外側へ処理する。同一 scope 内は **local declaration と `defer` を合わせた字句上の逆順**とし、後からの初期化・再初期化・再代入で位置を変えない。

```text
var first: Resource
defer: log("A")
let second = makeResource()
first = makeResource()
// cleanup: second の破棄 -> log("A") -> first の破棄
```

各位置で登録済みの `defer` と残る破棄責任だけを処理する。最終使用だけでは破棄責任は消えない。Scope entry binding、一時値、iteration binding などの位置と所属 scope は各仕様に従う。

`return`・`exit`・`continue`・`yield` は実際に離れる scope だけを cleanup し、転送結果は先に Copy / Move で確保する。一時値の終了、`defer` の実行と借用検査、結果配送は Scope Exit の共通規則に従う。

### 5.5. 一度だけの破棄と異常終了

通常の cleanup が破棄位置に到達した場合、残る破棄責任を一度だけ実行する。Move は責任を移し、元の place での二重破棄を防ぐ。先行処理で cleanup が停止する場合まで、全値の破棄完了を保証するものではない。

Panic Termination の終了規則と cleanup の打ち切りは Error Handling 仕様に従う。本書は Panic 時の破棄動作を独自には定めない。

終了しない cleanup は後続の cleanup と結果配送を妨げる。強制終了と undefined behavior に cleanup の保証はなく、cancellation の保証には別仕様を必要とする。

## 6. コンパイラの検証と実装

Compiler は制御フロー上で、次を静的に検証する。

| 分類 | 検証内容 |
| --- | --- |
| 初期化 | Move Path ごとの初期化状態、`let` の初回初期化、構築完了、集合値の完全性 |
| 取得 | Copy / Move の選択、明示的な Property Move、Copy capability と使用位置の Loan の区別 |
| 配置 | Initialization / Replacement の判定、標準 setter による field 単位の再初期化 |
| 交換 | 排他的 Loan の開始時点、重なりの静的判定 |
| 破棄 | 破棄責任の所在と順序、Destruction lifetime checking、二重破棄の防止 |
| 経路 | 分岐・繰り返し・結果転送・`defer` を含むすべての到達経路 |

これにより、未初期化値・Move 後の使用、不正な Partial Move、Copy / deinit の衝突、無効な借用、不正な書き込み・破棄を防ぐ。Raw pointer 操作の追跡限界は Unsafe 仕様に従い、追跡できない元 owner の破棄責任の自動修復は保証しない。

Copy / Move の具体的なメモリ操作と、一時結果の物理的な配置は実装詳細である。言語上の値・所有権・Origin・Loan・破棄責任を保つ限り、実際のメモリ転送や一時領域を省略できる。

経路ごとの状態や破棄責任の違いは、制御フローの分割や必要な実行時 flag で条件付き cleanup を実装できる。全値への flag 付加は要求せず、安全性は静的に検証する。Move Path の許可範囲を最適化設定によって変えてはならない。

## 7. 本改訂で定義しないもの

次は本書の規則から推測してはならない。それぞれ別仕様または将来の改訂で定める。

- `Exchange`・`Swap` の最終的な綴りと解決手順。本書は意味規則だけを定義する。
- 対象を引数式の中で読み直せるようにする Loan 開始の緩和（reservation 方式）。
- 標準の複製 contract と、明示的な複製 API の綴り。
- 完了点の明示構文と、constructor の詳細な初期化規則。
- Move Path の追加経路（動的 container、ユーザー定義 indexer、runtime index）。
- Destruction lifetime checking の保守的近似を緩和する仕組み。
- 交換操作のスレッド間 atomicity と cancellation の保証。
- 初期化を省略できる stored field。

## 付記：他の仕様との分担・統合

本書は値の取得・配置・破棄を定義する。Property は storage へのアクセス、Ownership / Origin はアクセスの有効性、Scope Exit は cleanup の位置と順序、Error Handling は Panic Termination を定義する。

既存仕様への統合時には、次を同期する。

| 対象 | 同期する内容 |
| --- | --- |
| Copy / Move、Property の Copy 参照 | Copy の定義・分類・opt-in・generic constraint、string の non-Copy、Slice と関数値の分類 |
| Origin の Loan requirement | Copy の型としての成立条件と、使用位置の Loan 検査の区別。型の Copy 判定から active Loan requirement の条項を削除する |
| Bindings・初期化 | `let` の初回初期化、構築完了点の既定、集合値の完全性 |
| 式・Assignment | 単純代入の右辺先行とその根拠、複合代入との評価順序の差、Property setter の引数評価順、自己代入、途中終了、Origin / Loan 検査 |
| Ownership・交換 API | Move Path の到達手段、Partial Move 制限、交換時の Loan 開始、排他借用を利用した重なりの静的判定 |
| Property Move Access（暫定） | 明示 move、Copy 型の強制 Move、標準アクセスの field 単位の状態検査と再初期化、`deinit` を持つ型での `move` 宣言禁止 |
| Destruction・Scope Exit | deinit の明示呼び出し禁止、特別な receiver、全体置換の禁止、Destruction lifetime checking の名称と参照 |
