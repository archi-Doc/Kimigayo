# プロパティ仕様案

本書は、SPEC.md 第11章を再構成するための仕様案である。現行コンパイラの実装状況を示すものではない。以下の規則は提案であり、末尾に確認事項と関連章への反映事項を記す。

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

Computed Property の型注釈は getter の戻り値型を表す。getter にも戻り値型を明記し、Origin 省略の補完後に同じ契約となることを要求する。この方針は確認済みである。

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
| `@move` | 合法な場合に Copy 型を含めて明示的に Move |
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

### 3.3. Immutable storage と Move

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

カスタム getter の読み出しは、getter を一度呼び、その戻り値を通常の関数結果として扱う。`@ref`、`@uniq`、`@move` は戻り値に作用し、隠れた storage を直接操作しない。

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

カスタム setter がある Property の storage に対しては、外部からの直接 Move と排他借用を公開しない。標準 getter を持つ場合の Copy と共有借用は許可する。この方針は確認済みである。非 Copy 型の通常の値取得が直接 Move を必要とする場合も禁止対象となり、暗黙の借用には切り替えない。

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
let taken = gauge.level@move        // Error：storage の直接消費
```

この制限は accessor 自身による合法な storage 操作を禁止しない。また、カスタム getter が明示的に返す排他参照まで禁止しない。その参照を公開すれば、参照先の更新が setter を通らないことも公開契約の一部となる。

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

## 9. 確認事項と関連章への反映

### 9.1. 確定した設計判断

- Q1（決定済み）：computed の見出し型を getter の結果型と一致させる。
- Q2（決定済み）：カスタム setter がある場合、標準アクセスによる storage の直接 Move・排他借用を禁止する。標準 getter による Copy・共有借用は許可する。

### 9.2. 本案で具体化した提案

次の項目は、議論の方向に沿って本書で具体化したものであり、既存の SPEC.md で確定している規則ではない。

- アクセサーの契約を `get(self: ...) -> ...`、`set(self: ..., value: ...) -> ()` で記載する。
- カスタム setter の入力型は storage 型と独立して指定できる。
- カスタム receiver の許可範囲を通常の関数に揃える。
- 明示形の Contract 要求は getter の結果型を見出し型とし、適合判定は操作単位で行う。
- Stored Property で片方のアクセサーを省略した場合、宣言種別に応じた標準操作を補う。

### 9.3. SPEC.md の更新対象

採用時は第11章だけでなく、用語集、構文概要、アクセス判定、型適応、代入・複合更新、Contract 適合、Origin 省略、storage の状態解析と初期化を更新する。

既存の「Field は accessor を持たない」「storage は文脈上の名前ではない」「すべての getter は共有 receiver」といった記述、および P/R と Field 型の一律な一致要求は、本案との整合を取る必要がある。STATUS.md の実装状況も、仕様の採用とは区別して更新する。
