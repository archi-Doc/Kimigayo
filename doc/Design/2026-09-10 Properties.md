# プロパティの設計仕様

本書は、プロパティに関する設計判断をまとめた仕様である。標準アクセサーは storage から契約を導出し、カスタムアクセサーは関数と同じく契約を明示する。Move は型・所有値を要求する文脈・Place の権限で決定し、`@move` は廃止する。

本書は SPEC.md とコンパイラへの反映に先立つ設計文書であり、実装済みであることを意味しない。コード例は互いに独立した断片である。`Resource` は非 Copy の所有型とし、生成・複製・検査用の関数は別途宣言済みとする。

## 1. 基本モデル

Property は、名前を通して値の取得や更新を提供するメンバーである。具体的な Property は `let`、`var`、`computed` の3種類に分類する。Contract 内では `property` によって操作を要求する。

```text
Property
├─ 具体的な宣言
│  ├─ let       immutable storage を持つ
│  ├─ var       mutable storage を持つ
│  └─ computed  storage を持たない
└─ Contract の要求
   └─ property  必要な操作の契約を表す
```

| 宣言 | Storage | Getter | Setter | 宣言時 initializer |
| --- | --- | --- | --- | --- |
| `let` | あり。初期化後の置き換え不可 | 必須。省略時は標準 | 不可 | 任意。ただし static は必須 |
| `var` | あり。更新可能 | 必須。省略時は標準 | 必須。省略時は標準 | 任意。ただし static は必須 |
| `computed` | なし | カスタム getter 必須 | カスタム setter を任意に宣言 | 不可 |
| Contract の `property` | 要求しない | 必須 | 任意 | 不可 |

Storage の有無と可変性は宣言種別だけで決まる。アクセサー本体が `storage` を参照するかどうかによって、レイアウトを変更してはならない。

具体的な Property は struct、group、rootgroup 内に宣言できる。struct 内では instance、group と rootgroup 内では static となる。既存の宣言位置の制限を維持し、enum およびローカルの computed は導入しない。ローカルの `let` と `var` は従来どおり binding である。

## 2. 型・Place・Origin・操作の権限

### 2.1. Stored Property

```kimi
public let item: Item
```

この宣言は、型 `Item` の storage を一つ持つ。instance Property の標準アクセスは、receiver 内の対応する storage の Place を指す。

標準操作の契約は、次の情報から導出する。

```text
Property 宣言と receiver
├─ storage の型
├─ storage の Place
├─ 宣言のアクセス可能性
├─ let / var による更新権限
├─ receiver の所有・共有借用・排他借用などの権限
└─ 初期化状態、Origin、既存の Loan
   └─ Copy / Move / 共有借用 / 排他借用 / 更新の可否
```

Storage 自体を新しく借用する場合、その有効期間は receiver と storage の有効期間に制約される。この意味で instance storage の借用元は `self` となる。

Storage の型に含まれる Origin は別の契約である。たとえば保持している参照が外部の Origin に依存していても、その依存先を `self` に置き換えてはならない。また、Move で取得した所有値に、単に取得元であったという理由で `self` への依存を追加しない。

### 2.2. カスタム操作

カスタムアクセサーは、明示したシグネチャを契約とする関数である。契約を本体から推論しない。本体が単に `storage` を返す場合も、標準アクセスに置き換えてはならない。

Stored Property の型注釈は storage の型を表す。カスタム getter の戻り値型およびカスタム setter の入力型は、それぞれのシグネチャで指定する。これらが storage の型と異なることを許可する。

Computed Property の型注釈は getter の戻り値型を表す。getter にも戻り値型を明記し、Origin 省略の補完後に同じ契約となることを要求する。

## 3. 標準アクセサー

### 3.1. 宣言と省略

Stored Property でアクセサーを省略した場合、`let` には標準 get、`var` には標準 get と標準 set を補う。本体のない `get` と `set` により、標準アクセサーを明記できる。

```kimi
public let limit: i32 = 100
public var count: i32 = 0

public var otherCount: i32 = 0
    get
    private set
```

一方のアクセサーだけをカスタム化しても、もう一方は標準のままとする。各 accessor は高々一度宣言でき、記載順は問わない。`let` の set はエラーである。

具体的な Property に inline `has` は使用しない。`has` は Contract の省略形に用いる。

### 3.2. 取得と借用

標準 get は通常の関数呼び出しではなく、storage に対する許可済みの操作を提供する。

| 利用側の操作 | 標準動作 |
| --- | --- |
| 値として取得 | storage の型が Copy なら Copy。それ以外は合法な場合に Move |
| `@ref` など | 通常の適応規則に従う共有借用、Reborrow、または参照値の Copy |
| storage の排他借用 | 更新権限と排他性を満たす場合に許可 |
| `=` | 標準 set または初期化規則による直接の配置・置き換え |

借用 receiver から非 Copy の所有値を取り出せない場合はエラーとする。戻り値を暗黙に参照型へ変更したり、失敗した Move を借用で代替したりしてはならない。

```kimi
struct Holder
    public var item: Resource

// Resource は非 Copy 型。生成用関数などは別途宣言済みとする。
var holder = makeHolder()
let view = holder.item@ref/Resource
inspect(view)

// view の Loan が終了した後。通常の Move 条件を満たすものとする。
let item = holder.item
holder.item = makeResource()
```

参照や object handle 自体を保持する slot を借用する場合は、既存の型適応規則に従って完全な slot 型を指定する。`@ref` が常に slot の借用を意味するわけではない。

### 3.3. Movable Place と消費文脈

所有値を要求する文脈（consuming context）とは、所有型の binding の初期化、所有型の引数への受け渡し、所有型の戻り値の返却、所有型 storage への代入など、受け取り側がその値を所有する文脈である。参照型の引数に合わせた借用などは、通常の適応規則に従って先に区別する。

Place から所有値を取得するとき、型が Copy なら Copy し、それ以外は Move する。Move は、その使用地点で対象が Movable Place である場合に限り許可する。

Movable Place は「Move による取得が許可された場所」を意味する。宣言の種類だけで固定される属性ではなく、次の条件を満たす必要がある。

```text
非 Copy の所有値を要求する
└─ 取得元は Place か
   ├─ はい：Movable Place の条件を検査
   │  ├─ 呼び出し側が対象を所有する
   │  ├─ 対象が初期化済みで完全である
   │  ├─ 競合する Loan がない
   │  ├─ アクセス契約が直接消費を許可する
   │  └─ 部分 Move・構築・deinit の規則を満たす
   │     ├─ すべて成立：Move し、取得元を未保持にする
   │     └─ 不成立：コンパイル時エラー
   └─ いいえ：生成済みの一時所有値を受け渡す
```

一時的な所有値は、その所有権を受け取り側へ渡すために、名前付き storage から取り出す必要がない。一時値を借用するために領域へ実体化する処理は、既存の一時領域規則に従う。

| 対象 | 所有値としての取得 |
| --- | --- |
| 所有するローカル変数 | Copy、または Movable 条件を満たす場合の Move |
| 所有する stored Property | Copy、または部分 Move とアクセス契約を満たす場合の Move |
| カスタム setter 付き Property の直接 storage アクセス | Copy のみ。非 Copy の直接 Move は不可 |
| 一時的な所有値 | 所有権を受け取り側へ渡す |
| 共有・排他借用を通して指す storage | 合法な Copy は可能。直接の抜き取りは不可 |

```kimi
let resource = makeResource()
consume(resource)          // Resource は非 Copy。local から Move
consume(resource)          // Error：すでに Move 済み

consume(makeResource())    // 生成した所有値をそのまま渡す

let count: i32 = 10
consumeCount(count)        // i32 は Copy。count は引き続き使用可能
```

取得が不正な場合、暗黙の借用や複製へ切り替えてはならない。Generic な型の Copy 能力が未確定の場合も、既存の generic 検証に従って Copy と Move の必要条件を満たすことを確認する。

`@move` は使用できない。Copy 型を強制的に Move して取得元を未保持にする操作は提供しない。この廃止は明示的な借用・型適応を廃止するものではない。

### 3.4. Place の操作権限

Movable、共有借用可能、排他借用可能、書き込み可能は、別々の条件として検査する。

```text
Place の操作権限
├─ Copy 可能       型の Copy 能力と読み取り条件
├─ Movable         所有・初期化・Loan・消費の公開契約
├─ 共有借用可能    読み取り・寿命・Loan
├─ 排他借用可能    更新権限・排他性・公開契約
└─ 書き込み可能    let / var・receiver・set のアクセス可能性
```

たとえば、所有する `let` は Movable になり得るが、初期化後に書き込み可能ではない。共有借用した `var` は、`var` であるという理由だけでは排他借用や Move が可能にならない。

標準 getter からの直接排他借用には、標準 setter もアクセス可能であることを要求する。非公開 setter を迂回して更新できてはならない。カスタム getter だけを公開する Property は、その戻り値の契約を通してアクセスを提供し、標準の直接 storage アクセスを追加しない。

### 3.5. Immutable storage と Move

`let` は初期化後の置き換えを禁止する。通常の所有権規則で許可された Move まで禁止するものではない。Move 後も初回初期化の履歴は残り、`let` の再初期化はできない。

借用 receiver、object receiver、static storage、祖先の `deinit`、未完了の構築などによる既存の Move 制限を維持する。Property 化によって、従来禁止されていた部分 Move を許可してはならない。

## 4. カスタムアクセサーの契約

### 4.1. 明示する項目

本体を持つアクセサーには、パラメーター一覧と戻り値型を必須とする。instance accessor は receiver `self` とその型を明示する。setter は入力 `value` とその型を明示し、戻り値は `()` とする。

```kimi
struct Holder
    public var item: Resource
        get(self: ref/Self) -> ref/Resource
            return storage@ref/Resource

        set(self: uniq/Self, value: Resource) -> ()
            storage = value
```

Getter は `self` 以外の実引数を取らない。setter は `self` と `value` を取る。static getter の引数一覧は `()`、static setter は `(value: T)` とする。省略可能引数や default 引数は導入しない。

Receiver 型の許可範囲と受け渡しは通常の instance 関数に従う。共有、排他、所有 receiver などの権限を、本体の都合によって暗黙に切り替えない。Object receiver は既存の互換性検査も満たす必要がある。

```kimi
get => storage                  // Error：契約が未記載
set => storage = value          // Error：契約が未記載
get(self: ref/Self) -> Resource  // 本体からこの戻り値型を検証する
    return storage              // Resource が非 Copy なら共有 receiver からの Move で Error
```

アクセサーの本体は式形式またはインデントしたブロックとし、通常の関数の戻り値・制御フロー規則に従う。

### 4.2. Origin の省略

Origin は、通常の関数と同じシグネチャに基づく省略規則を適用する。本体から戻り値の Origin を推論してはならない。

```kimi
get(self: ref/Self) -> ref/Resource => storage@ref/Resource

// 上と同じ契約
get(self: ref/Self) -> ref/Resource from self => storage@ref/Resource
```

直接借用する入力が `self` だけなら、戻り値の省略された借用 Origin は `self` から導出される。所有 receiver まで一律に `from self` とする規則ではない。

別の依存先を要求する場合は、通常の関数と同様に Origin を明示する。ネストした借用と集約型の Origin 引数も既存の位置別省略規則に従う。Static accessor には架空の `self` を導入しない。

補完後の契約を本体が満たさない場合はエラーとする。宣言した契約を本体に合わせて自動変更してはならない。

### 4.3. Storage の参照

`storage` は stored Property のカスタム accessor 内でのみ使える文脈上の名前であり、その Property 自身の slot を指す。`self.item` と異なり、アクセサーを再呼び出ししない。

Storage への操作は宣言種別、receiver の権限、初期化状態、Loan に従う。共有 receiver の getter から storage を書き換えたり、非 Copy 値を抜き取ったりすることはできない。

`let` の storage は、排他 receiver を宣言しても初期化後に置き換えられない。Computed Property と Contract にはこの文脈上の `storage` は存在しない。

### 4.4. アクセス可能性

アクセサーは Property のアクセス修飾子を継承する。個別に指定する場合は、既存のアクセス制限規則に従って、Property より厳しい制限だけを許可する。

```kimi
public var count: i32 = 0
    get
    private set(self: uniq/Self, value: i32) -> ()
        storage = max(value, 0)
```

アクセス可能性と receiver の権限は別々に検査する。`public` であることは排他借用や Move の許可を意味しない。

## 5. 呼び出し側の操作

### 5.1. カスタム getter の結果

カスタム getter の読み出しは、getter を一度呼び、その戻り値を通常の関数結果として扱う。`@ref` と `@uniq` は戻り値に作用し、隠れた storage を直接操作しない。

```kimi
struct Snapshot
    public let item: Resource
        get(self: ref/Self) -> Resource
            return duplicateResource(storage@ref/Resource)

// snapshot は初期化済みとする。
let copy = snapshot.item       // getter が作った Resource
let view = snapshot.item@ref   // getter の一時的な戻り値を借用
```

一時値の寿命と借用は既存の一時領域規則に従う。Getter が参照を返す場合は、その宣言された Origin と Loan を保持する。呼び出し側が本体を解析して private storage への直接アクセスへ置き換えることはできない。

### 5.2. 更新と setter の迂回

単純代入は右辺の値を先に確保し、次に receiver を評価する。カスタム set は入力型に適合する値を受け取り、一度だけ実行される。Getter は呼ばない。

カスタム setter がある Property の storage に対しては、外部からの直接 Move と排他借用を公開しない。標準 getter を持つ場合の Copy と共有借用は許可する。非 Copy 型の通常の値取得が直接 Move を必要とする場合も禁止対象となり、暗黙の借用には切り替えない。

```kimi
struct Gauge
    public var level: i32 = 0
        get
        set(self: uniq/Self, value: i32) -> ()
            storage = clamp(value, 0, 100)

var gauge = makeGauge()
gauge.level = 150                   // setter により 100 を格納
let view = gauge.level@ref/i32      // OK：標準 getter による共有借用
let edit = gauge.level@uniq/i32     // Error：カスタム setter を迂回する
```

非 Copy 型では、所有値を要求する文脈での読み出し自体がエラーになる。

```kimi
struct ResourceHolder
    public var item: Resource
        get
        set(self: uniq/Self, value: Resource) -> ()
            storage = value

var holder = makeResourceHolder()
let view = holder.item@ref/Resource // OK：共有借用
inspect(view)
// view の Loan が終了した後
let item = holder.item             // Error：storage は Movable として公開されない
holder.item = makeResource()        // OK：カスタム setter による更新
```

規則の対象は、Property が公開する storage の直接アクセスである。「カスタム setter がある Property の結果は消費できない」という意味ではない。カスタム getter が新しく生成・複製した所有値を返す場合、その結果は通常どおり消費できる。

```kimi
struct CopyingHolder
    public var item: Resource
        get(self: ref/Self) -> Resource
            return duplicateResource(storage@ref/Resource)
        set(self: uniq/Self, value: Resource) -> ()
            storage = value

// copyingHolder は初期化済み
consume(copyingHolder.item) // OK：getter の戻り値の所有権を渡す
```

この制限は accessor 自身による合法な storage 操作を禁止しない。また、カスタム getter が明示的に返す排他参照まで禁止しない。その参照を公開すれば、参照先の更新が setter を通らないことも公開契約の一部となる。共有借用の許可も深い不変性を意味せず、保持する参照や object handle の参照先の権限は、その型の通常の規則に従う。

### 5.3. 複合更新

複合代入および増減は、receiver、読出し、書込みをそれぞれ一度だけ評価する。Getter の結果に演算を適用でき、その結果を setter に渡せることを要求する。

```kimi
gauge.level += 10
```

Getter の戻り値型と setter の入力型が異なる場合、複合更新が成立しないことがある。また、getter の Loan が setter の呼び出しと両立しない場合や、getter が receiver を消費して後続の set ができない場合もエラーとなる。暗黙の複製や receiver の再評価で補ってはならない。

## 6. Computed Property

Computed Property は storage を持たず、カスタム getter と任意のカスタム setter によって定義する。Initializer と標準 accessor は禁止する。

```kimi
struct Temperature
    private var celsius: f64 = 0.0

    public computed fahrenheit: f64
        get(self: ref/Self) -> f64
            return self.celsius * 1.8 + 32.0

        set(self: uniq/Self, value: f64) -> ()
            self.celsius = (value - 32.0) / 1.8
```

借用を返す computed は、その参照型を明示する。

```kimi
struct Holder
    private var item: Resource

    public computed view: ref/Resource
        get(self: ref/Self) -> ref/Resource
            return self.item@ref/Resource
```

`view` の Origin は getter のシグネチャの位置で補完する。Storage を保持する宣言としての Origin 検査は行わない。

Getter と setter が同じ場所を扱うことや、set の直後に get すると同じ値が返ることは保証しない。

## 7. 初期化・レイアウト・破棄

宣言時 initializer とコンストラクターによる初回配置は、accessor を呼ばず storage を直接初期化する。カスタム setter があることは、初期値の検証を自動実行することを意味しない。

Instance storage は initializer を省略できるが、既存のコンストラクターの確実な初期化規則を満たす必要がある。ゼロ初期化を自動追加しない。型注釈の省略を許す場合も、既存の宣言時 initializer からの型推論規則に従い、カスタム accessor の本体から storage 型を推論しない。

Static storage は宣言時 initializer を必須とし、既存の遅延初期化・再入・借用保持の制限を維持する。Computed accessor の呼び出し自体は storage の初期化を意味せず、実行中に実際に触れた static storage が初期化対象となる。

レイアウト、Copy の導出、構築完了判定、部分 Move、破棄には stored Property の storage と基底部分を使用する。Computed Property は含めない。自動破棄で getter や setter を呼んではならない。

## 8. Contract の Property 要求

### 8.1. 要求の表現

Contract の `property` は、操作の契約を要求する。Storage の存在、可変性、Place の具体的な構造は要求しない。Getter を必須とし、setter を任意とする。

通常の要求には省略形を用意する。

```kimi
contract Counted
    property count: i32 has get

contract MutableCounted
    property count: i32 has get, set
```

この省略形の get は共有 receiver から注釈型の値を返す契約、set は排他 receiver と注釈型の入力から Unit を返す契約となる。標準 stored getter の全操作を要求する意味ではない。

特殊な契約は、アクセサーのシグネチャを明示する。本体は記載しない。

```kimi
contract ReadableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource

contract ReplaceableItem
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource
        set(self: uniq/Self, value: Resource) -> ()
```

明示形の Property 注釈型は getter の結果型とする。Contract 内の accessor に `storage`、initializer、アクセス修飾子を記載してはならない。

### 8.2. 適合判定

適合は実際の操作の契約を比較して判定する。実装の storage 型と要求の見出し型の単純な一致は要求しない。Self、型引数、Origin を対応付け、通常の関数要求との互換性を検証する。

標準 stored getter から要求への対応付けは、少なくとも次の2種類を定義する。

| 要求される get | 標準 storage による実装 |
| --- | --- |
| storage 型 T の所有値を共有 receiver から返す | T が Copy と証明される場合の Copy |
| `ref/T` を返す | 共有借用可能な T の slot を借用 |

各対応付けはアクセス可能性と Origin・Loan の要求も満たさなければならない。非 Copy の値を共有 receiver から Move して適合させてはならない。上記以外の変換が必要な場合は、明示したカスタム getter を使用する。

Set 要求は、アクセス可能な標準 set または互換なカスタム set で満たす。`let` は set 要求を満たさない。

```text
get 要求
├─ let       標準操作またはカスタム get の契約で検証
├─ var       標準操作またはカスタム get の契約で検証
└─ computed  カスタム get の契約で検証

set 要求
├─ var       標準 set またはカスタム set で検証
└─ computed  カスタム set で検証
```

Contract 経由で使用できるのは、要求に記載された操作だけである。実装が storage を持つという理由で、直接 Move や排他借用を追加してはならない。

## 9. 診断と契約の安定性

次の場合はコンパイル時エラーとする。

| 条件 | 理由 |
| --- | --- |
| `let` に set を宣言する | Immutable storage の宣言と両立しない |
| `computed` に initializer や標準 accessor を書く | 対応する storage がない |
| カスタム accessor のパラメーター型・戻り値型を省く | 契約を本体から推論しない |
| Computed の見出し型と getter の結果型が異なる | 公開する結果型が一致しない |
| 借用 receiver から非 Copy の storage を返す | Movable Place ではない |
| カスタム setter の storage を直接消費・排他借用する | 直接アクセスの公開契約に含まれない |
| 戻り値の借用が明示・補完した Origin を満たさない | 本体が返却契約に違反する |
| 複合更新で getter の結果を setter に渡せない | 操作を合成できない |
| `@move` を使用する | 明示的 Move 演算子は廃止された |

アクセサー本体を変更しても、同じシグネチャが表す呼び出し側の契約を変更してはならない。Getter が storage を返すか、新しい値を作るかを呼び出し側が解析する必要はない。

標準 accessor からカスタム accessor への変更は、公開契約の変更になり得る。特に、標準 setter をカスタム setter に変更すると、storage の直接 Move・排他借用が利用できなくなる。この影響は本体の内容ではなく宣言形式で決まる。

## 10. SPEC.md と実装への反映範囲

本書は従来の Field と computed を Property の分類として再構成する。内部の storage の識別・レイアウト・部分 Move の追跡に Field Identity 相当の情報を保持することは妨げない。

採用した規則を SPEC.md に反映するときは、次の関連箇所を一貫して更新する。

- 第11章：Property の分類、標準・カスタム accessor、storage、Contract の要求。
- 関数と Origin：アクセサーの明示的 receiver と通常の Origin 省略規則。
- 所有権と式：消費文脈、Movable Place、Copy 型の取得、一時所有値の受け渡し。
- 演算子と型適応：`@move` の廃止、および借用対象が storage か getter の結果かの区別。
- アクセス制御と更新：標準 setter の権限、カスタム setter の迂回禁止、単純代入と複合更新。
- Contract：操作単位の適合判定と、標準 storage 操作からの対応付け。
- 初期化・破棄・レイアウト：stored Property だけを対象とする storage の処理。
- 用語集、構文概要、既存コード例、診断、および STATUS.md の実装状況。

本書だけで lexer・parser・所有権解析が変更されたと扱ってはならない。特に `@move` 廃止はプロパティ以外にも及ぶため、言語全体の構文と例を同時に整合させる必要がある。
