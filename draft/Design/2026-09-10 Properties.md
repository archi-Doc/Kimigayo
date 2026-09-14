# プロパティ仕様（最終版）

確定日: 2026-09-10

Property を `let / var / computed` に統一し、Contract の要求を `property` で表す。標準アクセスは storage の Place を扱い、カスタム accessor は明示した関数契約に従う。`@move` は廃止する。

本書は設計上の最終仕様であり、SPEC.md 本体・コンパイラへの反映完了を意味しない。変更の背景と反映先は [Design Change](../Changes/2026-09-10%20Property%20Semantics.md) に記録する。

例は独立した断片である。`Resource` は非 Copy の所有型、`Point` は Copy 型とし、生成・検査用の関数は別途宣言済みとする。Accessor を含む宣言断片は、明記しない限り struct のメンバーとする。

## 1. Property の分類

```text
Property
├─ 具体的な宣言
│  ├─ let       immutable storage を持つ
│  ├─ var       mutable storage を持つ
│  └─ computed  storage を持たない
└─ Contract の要求
   └─ property  読み書きの操作を要求する
```

| 宣言 | Storage | Get | Set | Initializer | 文脈上の storage |
| --- | --- | --- | --- | --- | --- |
| `let` | Immutable | 必須。省略時は標準 | 不可 | instance は任意 | accessor 内で参照可 |
| `var` | Mutable | 必須。省略時は標準 | 必須。省略時は標準 | instance は任意 | accessor 内で参照可 |
| `computed` | なし | 本体付き get 必須 | 本体付き set を任意に宣言 | 不可 | 不可 |
| Contract の `property` | 要求しない | 要求必須 | 要求任意 | 不可 | 不可 |

Static の let/var は既存規則に従い initializer を必須とする。Storage の有無と可変性は宣言種別だけで決まり、accessor 本体の内容で変化しない。

具体的な Property は struct、group、rootgroup に宣言できる。Struct 内は instance、group/rootgroup 内は static とする。Enum とローカル computed は導入しない。ローカルの let/var は従来どおり binding である。

## 2. 型・Place・アクセス契約

### 2.1. 型

| 宣言・操作 | 型の意味 |
| --- | --- |
| Stored Property の `: T` | Storage 型 T。値として取得した結果も T |
| Stored のカスタム get | 戻り値は T と一致 |
| Stored のカスタム set | 入力 value は T と一致、結果は Unit |
| Computed の `: T` | Getter の戻り値型 |
| Contract の `property name: T` | 要求する getter の結果型 |

型の一致は、参照やネストした型を含む構造と、§4.2 の Origin 束縛・依存について検査する。同じ Core であるだけでは一致としない。

```text
Stored Property
├─ T が Copy
│  ├─ 標準 get/set：許可
│  └─ カスタム get/set：入出力型を T に揃えて許可
└─ T の Copy 能力が証明できない
   ├─ 標準 get/set：許可
   ├─ カスタム get：禁止
   └─ カスタム set：入力型を T に揃えて許可
```

Let はどちらの場合も set を持たない。Copy 制限は stored のカスタム getter に適用し、computed と Contract の操作契約には自動的に拡張しない。

### 2.2. 標準アクセスと関数結果

| 種類 | 公開する対象 |
| --- | --- |
| Stored の標準 get | 対応する storage Place への許可された操作 |
| Stored のカスタム get | 関数が返す T の値 |
| Computed の get | 関数が返す宣言済みの型の値 |

**標準 get は getter 関数ではなく、storage Place の読み取り側操作を公開する契約である。** 利用文脈に応じて Copy、Move、借用を選び、各操作の権限を検査する。カスタム get と computed の get は関数を呼び、その結果を返す。

Stored のカスタム get の結果型は T のままだが、`@ref` が指す対象は標準 get と異なる。標準 get を手書きの本体に置き換えることは、値の型が同じでもアクセス契約の変更になり得る。

## 3. 標準 get/set

### 3.1. 宣言と省略

```kimi
public let limit: i32 = 100
public var count: i32 = 0

public var otherCount: i32 = 0
    get
    private set
```

本体のない get/set は標準操作を指定する。Let では省略した get、var では省略した get/set を補う。一方だけをカスタム化しても、もう一方は標準のままとする。

各 accessor は高々一度宣言でき、記載順は任意とする。具体的な Property に inline has は使用せず、Contract の省略形に限る。

### 3.2. 値の取得と借用

```text
標準アクセスから値 T を取得する
├─ T が Copy     → Copy
└─ T が非 Copy   → Movable Place なら Move、そうでなければ Error

借用を要求する
├─ 共有借用      → 通常の適応・Origin・Loan 規則で検査
└─ 排他借用      → 更新権限・排他性・公開契約も検査
```

非 Copy 型の Move が不正でも、結果型を ref/T に変更したり、暗黙の借用・複製へ切り替えたりしない。

```kimi
struct Holder
    public var item: Resource

var holder = makeHolder()
let view = holder.item@ref/Resource // storage の共有借用
inspect(view)
// view の Loan が終了し、部分 Move の条件も満たすとする。
let item = holder.item             // Resource を Move。元の slot は未保持
holder.item = makeResource()        // 標準 set で再初期化
```

標準 set は storage を直接初期化・置換する。旧値が残っている場合は通常の破棄規則を適用し、新しい値を配置する。

### 3.3. 操作の権限

Place は値を保持する場所、Origin は寿命の依存先、Loan は借用に伴うアクセス制約を表す。実際に許可される操作は、次の条件をすべて満たす必要がある。

```text
Property が公開する操作
    ∩ 宣言と accessor のアクセス可能性
    ∩ receiver の権限
    ∩ 初期化状態・Origin・Loan
    ∩ Move・構築・破棄の規則
```

Copy、Move、共有借用、排他借用、書き込みの可否は独立に検査する。Storage の直接操作には、次の標準 accessor が存在し、使用地点からアクセス可能であることを要求する。

| 直接操作 | 必要な標準 accessor |
| --- | --- |
| Copy・共有借用 | 標準 get |
| let の Move | 標準 get |
| var の Move | 標準 get と標準 set |
| 排他借用 | 標準 get と標準 set |
| 書き込み | 標準 set |

カスタム accessor の存在やアクセス可能性では、この条件を代替できない。単純代入の最終対象には set のアクセス可能性を要求し、get は要求しない。対象までの経路に必要な get は別途検査する（§7.2）。

| var の構成 | 読み出し・借用の対象 | 更新 |
| --- | --- | --- |
| 標準 get ＋標準 set | Storage。各条件を満たす直接操作を公開 | 標準 set |
| カスタム get ＋標準 set | Getter の戻り値。直接 storage 借用・Move は不可 | 標準 set |
| 標準 get ＋カスタム set | Storage の Copy・共有借用。直接 Move・排他借用は不可 | カスタム set |
| カスタム get ＋カスタム set | Getter の戻り値。直接 storage アクセスなし | カスタム set |

**Move は取得元を未保持にする消費操作であり、代入ではない。** Var では標準 set のアクセス可能性も要求するが、setter は呼ばず、receiver の通常の書き込み権限も追加で要求しない。Let の消費は引き続き置き換えと区別する。

```kimi
struct Holder
    public var item: Resource
        get
        private set

// struct 外。holder は所有する完全な値で、競合 Loan はないとする。
let item = holder.item       // Error：var の標準 set が非公開
let view = holder.item@ref   // OK：標準 get による共有借用
```

初期化済み let の slot 自体への排他借用は禁止する。保持する参照の参照先の権限とは区別し、深い不変性を保証しない。

参照型などを保持する slot を借用するときは、既存の適応規則に従い完全な slot 型を指定する。`@ref` が常に slot を借用するわけではなく、保持する共有参照の Copy になる場合もある。

### 3.4. 子 Place と receiver の完全性

Storage 内の子 Place にアクセスする場合、経路上の各 Property の公開権限も検査する。明示的な操作だけでなく、メソッド・関数呼び出しや演算子に必要な暗黙の借用・消費の適応も対象とし、親の setter を迂回してはならない。

```kimi
// position は Copy 型で、標準 get とカスタム set を持つ。
object.position.x = 10 // Error：親の setter を迂回する更新
// modify は x を uniq/Self で受け取るメソッドとする。
object.position.x.modify() // Error：暗黙の排他借用も親の境界に従う

var next = object.position
next.x = 10
object.position = next // OK：親の setter を通す
```

保持する参照から別領域へ到達した場合は、storage の子領域とは区別し、参照型の権限に従う。カスタム getter を通る経路は背後の storage を指さず、戻り値に対する §4.3 の規則を適用する。

標準操作は、対象 Place が有効なら、receiver の別の部分が Move 済みでも利用できる。カスタム accessor と computed の呼び出しは、通常の関数と同じく受け渡す receiver が完全であることを要求する。

```kimi
// resource と count は標準 get、表示用の displayCount はカスタム get。
let resource = object.resource // 合法な部分 Move とする
let count = object.count       // OK：残る完全な Place を読む
let shown = object.displayCount // Error：不完全な object を ref/Self として渡せない
```

標準操作の借用は対象 Place に、関数呼び出しの借用は receiver・Origin の契約に基づく。Accessor 本体を見て、公開契約より狭い借用範囲を呼び出し側で推論しない。

Receiver が完全でも、構築中の self に対する accessor 呼び出しは §7.3 により禁止する。

Object receiver での accessor 呼び出しには、SPEC.md §12.4.4 の ObjectCompatible 検証も要求する。ref/Self・uniq/Self の宣言だけでは、この条件を満たしたことにならない。

## 4. Stored のカスタム get/set

### 4.1. 許可条件とシグネチャ

Stored のカスタム accessor は §2.1 の型・Copy 条件に加え、次の契約に従う。

- Instance getter の receiver は ref/Self、setter は uniq/Self とする。
- シグネチャを明記し、本体から型や receiver を推論しない。

```kimi
public var level: i32 = 0
    get(self: ref/Self) -> i32
        return storage

    set(self: uniq/Self, value: i32) -> ()
        storage = clamp(value, 0, 100)
```

Static getter は `get() -> T`、setter は `set(value: T) -> ()` とする。Default/optional 引数は認めない。本体は式形式またはインデントしたブロックとする。

```kimi
public var item: Resource
    set(self: uniq/Self, value: Resource) -> ()
        storage = value // OK：非 Copy の所有値を受け取り、置き換える

public var count: i32
    get(self: ref/Self) -> ref/i32
        return storage@ref // Error：戻り値型が storage 型 i32 と異なる
```

非 Copy のカスタム getter は、本体が標準操作と同じでも許可しない。Storage slot の直接借用を公開する場合は標準 get を用いる。Copy 型の保存済み参照をカスタム getter から返すこととは区別する。所有値の生成・取り出しなどは関数や computed の明示的な契約で扱う。

### 4.2. Storage と Origin

`storage` は、その stored Property の accessor 内で、自身の slot を指す文脈上の名前である。`self.name` のように accessor を呼び直さない。Receiver と let/var による権限、初期化状態、Loan に従う。

Origin は、通常の関数の位置別規則でシグネチャから補完した後、必要な型一致と本体の合法性を検査する。本体から契約を推論せず、stored では storage 型の Origin 束縛・依存とも一致させる。束縛の対応で比較し、名前の綴りの一致は要求しない。省略で一致を保証できなければ明示する。

```kimi
struct View origin source
    public var value: ref/i32 from source
        get(self: ref/Self) -> ref/i32 from source
            return storage
        set(self: uniq/Self, value: ref/i32 from source) -> ()
            storage = value
```

この getter で from source を省略すると、通常の補完は from self となり、storage 型との一致を保証できない。Setter 入力の省略も独立した入力 Origin を導入するため、同様に from source を明示する。

Computed と Contract にも同じ省略規則を適用するが、storage 型との一致検査は行わない。直接借用入力が self だけの getter は、未指定の戻り値 Origin を self から補完できる。Copy 型の参照や集約型も寿命・Loan 検査の対象であり、既に束縛された依存を self に置き換えない。

### 4.3. Getter 結果の操作と寿命

Stored のカスタム get、computed の get、Contract 経由の get の結果に共通して適用する。結果に対する `@ref` などは戻り値に作用し、暗黙に内部 storage を公開しない。

#### 4.3.1. 一時領域の操作

**Getter が返した所有一時値の領域と子領域への代入・複合代入・増減・排他借用は禁止する。** 括弧、メンバー経路、暗黙の排他 receiver 適応でも回避できない。

```kimi
// position: Point は Copy 型で、カスタム get と標準 set を持つ。
object.position.x = 10       // Error：getter の一時結果だけを更新してしまう
object.position.x += 1       // Error
let edit = object.position@uniq/Point // Error：同じ一時結果の排他借用

var next = object.position   // 値を local に保存
next.x = 10                  // OK：通常の local の更新
object.position = next      // OK：position の set を使う
```

参照や object handle が指す別領域は、戻り値を保持する一時 slot と区別し、通常の権限・Origin・Loan 規則で更新できる。Property 自体への代入・複合代入・増減は set を通す更新であり、この禁止には含めない（§7.2）。

制限は getter 結果の一時領域に適用し、値の由来を追跡して別領域へ引き継がない。値を取得して local に保存した場合や、通常の関数へ値として渡した場合は、移転先の通常規則に従う。同じ一時領域を参照し続ける場合は制限が残る。値取得・共有借用・合法な所有権移転は妨げない。

```kimi
// identity は Point を値で受け取り、Point を値で返す通常の関数とする。
identity(object.position).x = 10 // 通常の関数の一時値規則で検査
// この更新も object.position には書き戻されない。
```

#### 4.3.2. 借用の寿命

借用に必要な期間は使用・返却・保持などの制約から決まり、借用先の有効期間内に収まらなければ借用は成立しない。SPEC.md §3.6 に従い、実体化や借用による一時領域の寿命延長は行わない。

```kimi
public var visible: i32 = 150
    get(self: ref/Self) -> i32
        return min(storage, 100)

// 初期化済み object に対して
let value = object.visible    // 100
inspect(object.visible@ref)   // OK：戻り値の一時領域は呼び出し終了まで有効
let view = object.visible@ref // 一時領域の寿命は initializer の終了まで
inspect(view)                // Error：この使用に必要な借用期間を確保できない

let saved = object.visible
let lastingView = saved@ref  // 保存した local を借用
inspect(lastingView)         // OK
```

上の inspect(view) は、必要な借用期間が一時領域の寿命を超えるためコンパイル時エラーとなる。後続使用のない binding を一律に拒否する規則は追加しない。標準 get なら、上の i32 に対する @ref は対応する storage を借用する。

### 4.4. カスタム setter の境界

直接アクセスと子 Place の制限は §3.3–3.4 に従う。禁止は Property 経由の slot 操作に限定し、receiver 全体の合法な Move や通常の破棄は妨げない。Stored getter の型一致・Copy 条件を回避する特別な排他参照返却も導入しない。

### 4.5. 非 Copy setter の所有権と更新

**入力の取得：** value は通常の引数と同じ初期化済み let 相当の binding である。非 Copy の所有値は setter に所有権を渡し、二度 Move できない。入力と storage の Origin は §4.2 に従う。

```kimi
struct Holder
    public var item: Resource
        get
        set(self: uniq/Self, value: Resource) -> ()
            storage = normalize(value)

var holder = makeHolder()
holder.item = makeResource()       // 所有値を setter に渡す
let view = holder.item@ref/Resource // OK：標準 get による共有借用
inspect(view)
// view の Loan が終了した後でも、以下の直接操作は禁止。
let taken = holder.item            // Error：カスタム setter の storage から Move
let edit = holder.item@uniq/Resource // Error：直接排他借用
```

**更新：** storage への代入は右辺を確保し、旧値を破棄して配置する。Setter を再呼び出しせず、失敗時も通常の代入・cleanup 規則に従う。Self は排他借用なので非 Copy の旧値を直接抜き取れず、検査には共有借用を使う。競合 Loan は更新前に終了させ、正常終了時に receiver を不完全なまま返してはならない。

**入力を保存しない場合：** 更新せず return すれば旧 storage は残り、未消費の入力は通常の引数 cleanup で破棄する。消費済み入力を二重破棄したり、呼び出し側へ自動復元したりしない。受理・拒否の通知が必要なら、結果を返す関数を使用する。

非 Copy の `holder.item = holder.item` は、右辺の直接 Move が禁止されるため呼び出し前にエラーとなる。初回配置については §7.3 に従う。

## 5. 非 Copy・Generic・Move

### 5.1. 非 Copy の stored Property

標準 get が公開されていても、共有・排他借用を通して指す非 Copy 値は直接抜き取れない。

```kimi
func inspectHolder(holder: ref/Holder) -> ()
    let view = holder.item@ref/Resource // OK：共有借用
    inspect(view)
    let item = holder.item             // Error：借用 receiver からの Move
```

### 5.2. Generic の検証

標準 Property の宣言自体に Copy 制約は不要である。

```kimi
struct Box<T>
    public var item: T

func view<T>(box: ref/Box<T>) -> ref/T from box
    return box.item@ref/T

func readCopy<T>(box: ref/Box<T>) -> T
    T is Copy
    return box.item
```

制約のない T のカスタム getter は禁止する。宣言に適用される generic 制約から T is Copy を証明する必要があり、特定の具体化が Copy であるだけでは定義を合法にしない。カスタム setter は Copy 制約なしで宣言できるが、本体は許容する全型について通常の所有権規則を満たさなければならない。

```kimi
struct AssignedBox<T>
    public var item: T
        set(self: uniq/Self, value: T) -> ()
            storage = value // Copy 型は Copy、非 Copy 型は value から Move
```

AssignedBox の標準 get では、T が Copy なら値を取得できる。非 Copy の場合は共有借用を使い、直接 Move はできない。Copy 能力が未証明なら、値取得がすべての場合に合法だとは証明できない。

標準の値取得結果は T のままであり、条件付きの戻り値型 Read(T) は導入しない。Copy 能力が未確定なら SPEC.md §8.10 に従い、取得後の状態を潜在的な Move として検証する。後続操作が初期化済みの取得元を必要とする場合、Copy/Move の両方で合法でなければならない。

```kimi
// Box<T> は上の宣言。部分 Move を妨げる deinit などはないとする。
func test<T>(box: Box<T>) -> ()
    let x = box.item
    inspect(box@ref) // Error：非 Copy の場合、item が未保持で box は不完全

func testCopy<T>(box: Box<T>) -> ()
    T is Copy
    let x = box.item
    inspect(box@ref) // OK：item は Copy され、box は完全なまま
```

これは解析上の保守的な扱いであり、実行時に Copy 型まで Move してはならない。Copy 制約、取得の代わりの借用、または合法な再初期化によって後続の使用を成立させられる。影響を受けない別の Place の操作まで一律に禁止しない。

Overload は通常の型・引数適応規則で選択し、その後に使用時の初期化状態や Loan を検査する。Move が不正でも借用型の別候補を選び直さない。

### 5.3. Movable Place と let

`@move` は使用できない。直接 Place から所有値を要求する文脈では、Copy 型なら Copy、それ以外は Movable Place に限り Move する。

```text
Movable Place の条件
├─ 対象を所有している
├─ 対象が初期化済み・完全である
├─ 競合する Loan がない
├─ アクセス契約が消費を許可する
└─ 部分 Move・構築・deinit の条件を満たす
```

一時所有値は、その所有権を受け取り側へ渡す。借用のための一時領域への実体化は、既存の規則に従う。

Let は置き換えを制限し、消費自体は制限しない。Move は新しい値の代入ではない。Move 後も初回初期化の履歴は残り、let の再初期化はできない。

```kimi
struct FixedHolder
    public let item: Resource

let holder = makeFixedHolder()
let item = holder.item             // 通常の条件を満たせば Move
holder.item = makeResource()        // Error：let の再初期化
```

借用・object receiver・static storage・構築中の対象・祖先の deinit などに関する既存の直接 Move 制限は維持する。

## 6. Computed

Computed は storage を持たず、getter と任意の setter を通常の関数として実装する。Initializer、本体なしの標準 get/set、文脈上の storage は使用できない。

見出し型 T は getter の戻り値型と Origin 補完後に一致させる。Getter は receiver と結果型、setter は receiver・入力型・Unit の結果を明記する。Receiver と戻り値は通常の関数の所有権規則に従う。

各 accessor は高々一度宣言し、順序は任意とする。Static には receiver を設けない。Default/optional 引数と inline has は使用できず、本体の形式は §4.1 に従う。

Computed は明示的な所有 receiver も許可する。この場合、読み出しが receiver 全体を消費し得る。

```kimi
struct Holder
    private var item: Resource
    public computed result: Resource
        get(self: Self) -> Resource
            return self.item // 通常の部分 Move・deinit 条件を満たす場合に限る

let holder = makeHolder()
let result = holder.result // 非 Copy の Holder 全体を getter に渡す
inspect(holder@ref)        // Error：receiver は Move 済み
```

Getter が receiver を消費し、後続の set に渡せなくなる複合更新はエラーとする。通常の関数に存在しない暗黙の複製や復元は行わない。

```kimi
struct Temperature
    private var celsius: f64 = 0.0
    public computed fahrenheit: f64
        get(self: ref/Self) -> f64
            return self.celsius * 1.8 + 32.0
        set(self: uniq/Self, value: f64) -> ()
            self.celsius = (value - 32.0) / 1.8
```

非 Copy の結果や getter と異なる setter 入力型も許可する。共有 receiver から内部の非 Copy 値を抜き取ることはできず、所有値を返すなら合法な生成・取得が必要となる。

```kimi
struct Holder
    private var item: Resource
    public computed view: ref/Resource
        get(self: ref/Self) -> ref/Resource
            return self.item@ref/Resource
```

Origin と戻り値への借用は §4.2–4.3 に従う。Get/set の往復で値が一致することや、同じ場所を扱うことは保証しない。

## 7. Accessibility・評価順・初期化

### 7.1. Accessibility

Accessor は Property のアクセス修飾子を継承し、個別指定は既存規則に従った厳しい制限だけを許可する。アクセス可能性と receiver の権限は別々に検査する。

```kimi
public var count: i32 = 0
    get
    private set
```

少なくとも一方の accessor を Property と同じアクセス範囲にする追加制限は設けない。直接操作の条件は §3.3 に従う。

### 7.2. 評価順

SPEC.md の右辺先行の単純代入と、対象先行の複合代入を維持する。

```text
単純代入
  RHS の評価・値の確保 → 対象の評価 → 標準 set またはカスタム set

複合代入
  対象の評価 → 標準の取得または getter → RHS → 演算 → set
```

対象・読出し・書込みは必要なものを一度だけ評価する。単純代入では最終対象の get は呼ばないが、receiver やアクセス経路の評価に必要な getter は呼ぶ。増減も右辺評価を除いて複合更新に従う。演算結果が set の入力に適合し、全段階で Loan と対象の有効性を満たす必要がある。

```kimi
// view は computed で uniq/Point を返し、Point.x は標準 set を持つとする。
object.view.x = 10
// RHS → view の getter → 参照先の x の set。x の get は呼ばない。
```

RHS や演算が正常終了しなければ後続の更新は行わず、実行済みの副作用は取り消さない。暗黙の複製や対象の再評価で不足する権限を補わない。複合更新は atomic ではない。

#### 7.2.1. 非 Copy 型のカスタム setter と複合代入

標準 get とカスタム set を持つ非 Copy の Property は、演算に左辺の所有値取得が必要な場合、複合代入できない。値取得には Move が必要だが、カスタム setter の storage は直接 Move を公開しないためである。

```kimi
// holder.item: Resource は標準 get とカスタム set を持つ。
holder.item += x // Error：Resource の所有値取得を必要とする更新は不可
```

この例は所有権上の制限を示す。現行 SPEC.md §13.8 ではユーザー定義の算術演算子自体が未導入であり、Resource の + や += を定義する許可を本書から追加しない。

借用から独立した所有値を生成できる場合は、通常の関数と単純代入で表現できる。

```kimi
// rebuild(left: ref/Resource, right: X) -> Resource は別途宣言済み。
// 戻り値は left への借用依存を保持せず、呼び出し終了時に入力 Loan が終了する。
holder.item = rebuild(holder.item@ref/Resource, x)
// RHS で新しい所有値を確保し、借用終了後に setter へ渡す。
```

### 7.3. Storage の初期化と破棄

#### 7.3.1. 初回配置と構築中のアクセス

Initializer とコンストラクターの own storage への初回配置は、accessor を呼ばず直接初期化する。カスタム setter の検証は初期値には自動適用されない。

初回配置はソース上の出現順ではなく、代入先へアクセスする時点の制御フロー状態で判定する。到達する全経路で own storage が未初期化かつ初回配置前である場合に限り、setter を省略する。Move や内部の破棄は初回配置の履歴をリセットしない。

構築中の self に対するカスタム get/set と computed の呼び出しは、SPEC.md §6.2.3 に従って禁止する。全 storage の初期化が済んだだけでは、この禁止は解除されない。

初期化済みの own storage は、標準 get の権限で Copy・借用できる。非 Copy 値の Move、継承元の storage へのアクセス、self 全体の取得・借用は禁止する。構築中の借用は構築完了前に終了させる。

| 構築中の own storage への代入 | 全到達経路で初回配置前 | 初期化済み、または経路が混在 |
| --- | --- | --- |
| let | 直接初期化 | Error：初回配置を保証できない |
| var ＋標準 set | 直接初期化 | 通常の状態別の配置・置換規則で検証 |
| var ＋カスタム set | 直接初期化 | Error：構築中の setter 呼び出しは禁止 |

初回配置とカスタム setter を実行時に選び分けない。初期値を宣言時 initializer で配置済みの場合も、この表では初期化済みとして扱う。

```kimi
// コンストラクター内。level は未初期化の var で、カスタム set を持つ。
if condition
    self.level = 100 // この経路の初回配置。setter を呼ばない
else
    self.level = 200 // この経路の初回配置。setter を呼ばない

self.level = 300 // Error：構築中のカスタム setter 呼び出し
```

```kimi
// 別の例。level は未初期化で、カスタム set を持つ。
if condition
    self.level = 100 // この経路の初回配置

self.level = 200 // Error：初期化済みの経路と未初期化の経路が混在
```

```kimi
public var level: i32 = 999
    set(self: uniq/Self, value: i32) -> ()
        storage = clamp(value, 0, 100)
// 初期値は 999。構築完了後の代入は setter を通る。
```

#### 7.3.2. 宣言・静的初期化・破棄

Instance storage は initializer を省略できるが、確実な初期化規則を満たす必要がある。ゼロ初期化は追加しない。Storage 型の推論は宣言時 initializer から行い、accessor 本体や後の代入から推論しない。保持する Origin は既存の storage 宣言規則で検査する。

宣言時 initializer では self とコンストラクター引数を参照できない。基底の構築、宣言順の initializer 評価、コンストラクター本体、cleanup 後の構築完了判定は SPEC.md §6.2.3 に従う。

Static storage の initializer、遅延初期化、安全な借用の保持禁止、Loan と置換の制限は維持する。カスタム static accessor には self を設けず、通常の関数の Origin 規則を適用する。

レイアウト、Copy 導出、構築完了、部分 Move、破棄には stored Property の storage と基底部分を使用する。Computed は含めず、自動破棄で accessor を呼ばない。

## 8. Contract の property

### 8.1. 操作の要求

Contract では property 構文を使用する。具体的な storage の有無や可変性ではなく、固定した get/set の操作契約を要求する。

```kimi
contract Counted
    property count: i32 has get

contract MutableCounted
    property count: i32 has get, set
```

**Has get は単なる「読み取り可能」ではなく、共有 receiver から見出し型 T の値を返す要求（by-value get requirement）である。** T 自体が参照型なら参照値を返す。Has set は排他 receiver と T の入力から Unit を返す要求となる。Storage の直接操作を一括して要求する意味ではない。

非 Copy Resource を保持する標準 Property は、`property item: Resource has get` を満たせない。`ref/Resource` の返却要求なら、標準 Place の共有借用から witness adaptation できる。

```kimi
contract ReadableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource

contract ReplaceableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource
        set(self: uniq/Self, value: Resource) -> ()
```

明示形では getter の戻り値を見出し型に揃え、setter 入力は個別に指定できる。Getter は必須、setter は任意。本体、initializer、storage、accessor のアクセス修飾子は記載しない。Origin は §4.2 に従う。

### 8.2. 適合

Let/var の標準操作、許可された stored custom accessor、または computed によって要求を満たす。実装の storage 型と要求の見出し型を一律に一致させず、各要求操作の型・receiver・アクセス可能性・Origin・Loan を検証する。

Self・型引数・関連型を置換後、通常の関数要求の適合規則を適用する。暗黙の型変換や実装側の強い事前条件は追加しない。Origin は束縛の対応と寿命条件で比較し、綴りだけでは判定しない。

Object receiver で使う適合には、既存の ObjectCompatible と runtime conformance の条件も適用する。

実装対象は通常のメンバー検索・継承規則で名前から選択し、その後に適合を検査する。型・accessor・アクセス権などの不適合を理由に、別の同名メンバーや基底の候補へ探索を戻さない。

標準 storage 型を F とすると、次の対応付けを提供する。

| 要求 | 標準 stored 操作による実装 |
| --- | --- |
| 共有 receiver から F を返す get | F が Copy と証明できる場合の Copy |
| 共有 receiver から ref/F を返す get | 許可された slot の共有借用 |
| F を受け取る set | アクセス可能な var の標準 set |

この対応付けを **witness adaptation** と呼ぶ。Contract 適合は、公開された標準 Place 操作から要求を満たす witness operation を合成できる。

```text
Concrete な標準 Property の公開操作
    → 型・権限・Origin・Loan を検証
    → Contract の要求を実現する witness operation
```

Concrete Property にカスタム accessor を追加するものではないため、非 Copy の標準 Property でも共有借用要求を実装できる。借用の Origin は receiver と slot の有効性を超えられず、カスタム getter に隠された storage は使用できない。

この標準対応付けから、所有 handle の object 借用への変換や、保存済み排他参照の再借用などは合成しない。表にない操作は、適合するカスタム getter または computed で明示する。保存済み参照の Copy は元の Origin 依存を保持する。

適合情報は要求、選択した Property、型・Origin の対応、および実行する操作を保持する。実装を関数呼び出しにするか直接操作へ展開するかは規定せず、runtime の witness table を必須にしない。

```text
get 要求 → let / var / computed の適合する操作
set 要求 → var / computed の適合する操作
```

非 Copy F を共有 receiver から所有値で返す要求は、標準 storage の Move で満たせない。Generic な適合も宣言された条件の下ですべての場合に証明する。型や Loan の検査に失敗しても要求型を変更しない。

Contract 経由では要求された操作だけを利用できる。実装が storage を持つという理由で、直接 Move・排他借用を追加しない。Getter の所有一時結果には、実装方式によらず §4.3 を適用する。Witness を直接操作へ展開する場合も、要求に基づく型・Origin・Loan と一時領域の操作制限を保持する。

## 9. 検証と対象外の機能

本書の許可条件を満たさない宣言・操作はコンパイル時エラーとする。検証箇所は次のとおり。

| 検証対象 | 規則 |
| --- | --- |
| 宣言種別、型、accessor 契約 | §1–2、§4.1–4.2、§6 |
| 直接操作、子 Place、receiver | §3.3–3.4 |
| Getter 結果の操作と寿命 | §4.3 |
| Setter の所有権、Generic、Move | §4.4–5 |
| アクセス制御、評価順、初期化 | §7 |
| Contract 適合 | §8 |

既存規則のうち、本書で変更を明記しないものは維持する。SPEC.md・構文・診断・所有権解析・STATUS.md への反映範囲は Design Change に従う。

借用を取る算術演算子と、その複合代入への適応は未導入である。将来追加する場合も、対象の一度だけの評価、set 入力への型適合、更新と Loan の両立を要求し、失敗した Move からの自動的な借用への切り替えは認めない。
