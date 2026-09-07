> **暫定仕様**：本書は Property に Move access を追加する設計案である。構文・適用範囲・制約は今後変更する可能性があり、実装済みであることを意味しない。

# Property Move Access

## 1. 基本方針

Property のアクセスを `get`・`move`・`set` に分ける。通常の読み取りで暗黙に Move out せず、`move` を明示したときだけ backing storage の値を取り出す。

```text
Property Access
├─ get   値を読む
├─ move  backing storage から値を取り出す
└─ set   値を初期化・置換する
```

| アクセス | 結果 | 対象 storage の正常完了後の状態 |
| --- | --- | --- |
| 標準 `get` | Copy または共有借用 | Initialized のまま |
| `move` | Property Type の値 | Moved |
| 標準 `set` | Unit。Initialization / Replacement を行う | Initialized |

`move` は instance 全体を消費せず、対象 field の Move Path と破棄責任を更新する。取り出した field を除く部分は元の instance に残る。すべての必須 field が初期化済みに戻るまで、instance 全体は incomplete である。

上の `get`・`set` は標準アクセスの意味である。Custom getter / setter の結果と副作用は従来の Property 規則に従い、`move` との併用には §2.3 の制約を課す。

```text
struct Person
    var name: string has get, move, set
    var age: i32 has get, set

var person = makePerson()
let name = person.name        // get：ref/string
inspect(name)                 // 借用の最終使用

let ownedName = move person.name
// ownedName: string
// person.name の storage: Moved
// person: incomplete

print(person.age)             // 標準 get：残る field を読める
person.name = "Alice"         // 標準 set：旧値を破棄せず再初期化
use(person)                  // person は再び complete
```

例中の API は説明用である。初期化状態、Move Path、Loan、Origin、破棄の共通規則は [Value Lifetime](Value%20Lifetime.md) に従う。

## 2. 宣言

### 2.1. 構文と既定値

`has` の accessor 一覧に、bodyless な `move` を追加する。

```text
var name: string has get, move, set
let resource: Resource has get, move
```

ブロック形式でも宣言できる。

```text
var name: string
    get
    move
    set
```

```text
accessor-declaration := access-restriction? get ('->' ResultType)?
                      | access-restriction? move
                      | access-restriction? set
```

`get`・`move`・`set` はそれぞれ一つまでとし、記述順は意味に影響しない。Inline とブロック形式の一覧は混在させない。`move` に body、引数、receiver、戻り値型を指定してはならない。

`move` は常に明示 opt-in とする。省略時の `get`・`set` の補完規則は既存仕様を維持するが、`move` を自動追加しない。明示一覧に書かなかった accessor を、その一覧へ自動追加することもない。

```text
var name: string                  // move は提供しない
var item: Resource has get, move  // set は提供しない
```

### 2.2. Storage とアクセス制御

`move` は Compiler 提供の storage 操作である。その有効表現は contextual `storage` に結び付くため、既存の `HasStorage` 判定では Stored Property になる。追加の storage を複製して持つわけではない。

Custom getter が計算値を返していても、`move` を宣言した Property は一つの実 storage を持ち、その初期化も必要になる。Storage を持たない Computed Property に Move access は存在しない。

各 accessor は独立したアクセス制限を持ち、Property より公開範囲を広くできない。

```text
public var name: string has get, private move, private set
```

`get` の利用権限は `move` の利用権限を与えず、`move` も `set` の利用権限を与えない。Getter の戻り値型から `move` を選択することはない。

### 2.3. Custom accessor と暫定的な制限

`move` と custom getter は併用できる。明示的な `move` は getter を呼ばずに storage を取り出すため、この違いを公開することは型の設計者の判断になる。

`move` と併用する `set` は、Compiler 提供の bodyless な標準 setter に限る。Custom setter の body は許可しない。再初期化だけ custom setter の検査や変換を迂回することを防ぎ、incomplete な instance 全体への借用も避けるためである。

```text
var name: string
    get -> ref/string => storage@ref
    move
    set                          // OK：標準 setter

var score: i32
    get
    move
    set
        storage = normalize(value) // Error：move と custom setter の併用
```

独自の検証・変換を伴う再設定が必要なら、Move access を公開せず、complete な instance に対する `Exchange` や型固有の操作を使用する。

暫定版では、instance を持つ struct の Stored Property に適用する。Static Property、indexer 自体、`contract` 内の `move` requirement、仮想・動的な Move access は定義しない。

## 3. Move 式

### 3.1. 構文と結果型

次はそれぞれ独立した使用例である。

```text
let name = move person.name
let child = move parent.child
consume(move person.name)
return move person.name
```

`move` は accessor 宣言と前置式の位置でのみキーワードとして扱う contextual keyword であり、予約語にはしない。それ以外の位置では通常の Name として使用できる。式の operand は Property access でなければならず、本書は任意の local に対する `move x` を導入しない。

優先順位は前置単項演算子に従い、member access・call・index はそれより強く結合する。`move p.field + other` は `(move p.field) + other` と解釈する。結果の member を使う場合は `(move p.field).method()` と書く。`move p.field.method()` は operand が call なので、この構文としては不正である。式の優先順位表には、他の前置単項演算子と同じ段に `move` を追加する。

括弧で囲んだ Property access も operand として認める。括弧は place としての性質を変えないため、`move (p.field)` は `move p.field` と同じ意味になる。

結果型は、Origin を含む対象 Property の完全な Type とする。Getter Result Type や期待型によって変更せず、その後の結果の型適合は通常の規則で検査する。

| Property Type | 標準 get | move |
| --- | --- | --- |
| `string` | `ref/string` | `string` |
| `obj/Node` | `objref/Node` | `obj/Node` |
| `rc/Node` | `objref/Node` | `rc/Node`。参照カウントを増やさない |
| `i32` | `i32` の Copy | `i32`。元の field は Moved |
| `ref/T` | 借用値の Copy | `ref/T` 自体の移転。参照先は所有しない |
| `uniq/T` | `ref/T` への共有再借用 | `uniq/T` 自体の移転 |

`move` は **Copy 型でも Move を強制する**。これは通常の値取得時の Copy / Move 選択に対する明示的な例外であり、型の Copy capability 自体は変更しない。**Property Type や generic 引数の Copy 性によって `move` 式の意味が変わらないようにするためである。**

Copy 型の Property を読むだけなら標準 `get` で足りる。`move` を使うと instance が incomplete になり復旧が必要になるため、Copy 型への `move` は意図がある場合に限る。なお、`contract` に `move` requirement を導入するまで（§2.3）、generic なコードからこの統一性を利用することはできない。

「所有値を取り出す」とは、所有型の Property では所有権を得るという意味である。借用・raw pointer 型の Property から、その参照先の所有権を取得することはない。

### 3.2. 対象の条件

次の条件をすべて満たす場合に限り許可する。

1. 対象がアクセス可能な `move` を持つ Stored Property である。
2. Receiver は呼び出し元が所有する struct の place、または所有する struct の一時値である。Local receiver を暗黙に値渡しして instance 全体を消費しない。
3. 対象が Value Lifetime で認める静的な Move Path であり、すべての到達経路で Initialized である。
4. Receiver の構築は一度正常に完了している。別 field の Partial Move によって現在 incomplete であることは許容する。
5. 対象と重なる有効な Loan がなく、関連する Origin・アクセス規則を満たす。
6. 包含する祖先集合値のユーザー定義 `deinit` の前提を崩さない。対象を直接含む struct 自身の `deinit` は、下記の宣言時検査で既に排除されている。

対象 field の型そのものが `deinit` を持っていても、field の値を complete なまま丸ごと移すことは可能である。一方、`move` を宣言する struct 自身が `deinit` を持つ場合は、宣言を compile error とする。包含する祖先の制限は使用位置でも検査する。

Receiver への経路が借用を経由する場合は、`uniq/Self` であっても禁止する。Copy 型を明示的に Move する場合も同じである。

暫定版では、`obj/Self`・`rc/Self`・`arc/Self`・raw pointer の参照先からの field Move は対象外とする。Property の**値**が `obj/Node` であることとは区別する。Static storage と動的 index 経由の Move out も許可しない。

### 3.3. let と var

Move は値の消費なので、所有する `let` local や `let` Property にも明示的な Move access を許可する。ただし再初期化の権限を与えない。

```text
struct Holder
    let item: Resource has get, move

var holder = makeHolder()
let item = move holder.item    // OK
holder.item = makeResource()   // Error：let Property は再初期化不可
```

`var` Property でも、所有する local が `let` で書き込み不可なら再初期化できない。再利用を意図する場合は、所有する binding と対象 Property の両方を書き込み可能にし、標準 `set` を提供する。

**再初期化が許可されない Property から `move` した instance は、二度と complete に戻らない。** 全体としての使用・借用・転送はできず、scope 終了時に残る field だけを破棄する。`let` Property、および標準 `set` を提供しない Property（§2.1 の `var item: Resource has get, move` など）が該当する。

```text
let item = move holder.item    // OK
// holder は以後 permanently incomplete。
use(holder)                    // Error：complete でない。
let moved = holder             // Error：全体の Move もできない。
```

### 3.4. 評価と状態更新

1. Receiver を一度だけ評価し、対象 storage を確定する。対象の getter / setter は呼ばない。
2. 値、所有権またはアクセス能力、破棄責任を結果へ移す。
3. 対象を Moved にし、祖先集合値の現在の完全性を更新する。

手順 2・3 は Compiler が一体で扱い、ユーザー定義コード・破棄・制御移動を挟まない。Receiver 評価が正常完了しなければ取り出しは行わず、既に生じた副作用は巻き戻さない。

所有する一時 receiver も許可する。取り出さなかった部分は、その一時値の通常の lifetime 終了時に破棄する。結果を捨てた場合も、取り出し自体を取り消さず、結果を通常の一時値として破棄する。

## 4. Partial Move 後のアクセスと再初期化

### 4.1. 標準アクセスと custom body の区別

Compiler 提供の標準 getter / setter は、対象 field だけの storage 操作として扱える。Receiver が所有する place として静的に特定できる場合、構築完了後の Partial Move による incomplete 状態でも、その field の状態とアクセス権を個別に検査する。

| 操作 | 許可条件 |
| --- | --- |
| 初期化済み field の標準 get | その field の読み取り・借用が許可される |
| 初期化済み field の move | §3.2 を満たす |
| 標準 set | 書き込み可能で、対象の set がアクセス可能である |
| Custom getter、通常の method | 全体を receiver として借用するなら instance が complete である |
| Instance 全体の Copy / Move / 借用 | instance が complete である |

**この表の帰結として、incomplete な instance に対してできるのは field 単位の標準 get / `move` / 標準 set だけである。** Custom getter も通常の method も呼べないため、取り出した後は速やかに標準 `set` で complete へ戻すことを想定した設計とする。

これは bodyless な標準アクセスの言語規則であり、custom body が偶然同じ処理をすることを根拠に適用してはならない。標準アクセスは親全体の `ref/Self`・`uniq/Self` を作らず、対象 field の Loan と書き込み範囲だけを扱う。

標準 getter が新しく作る借用の Loan は対象 field に結び付く。Origin はその storage と所有者の有効期間に制約される。既に格納された借用値を Copy する場合は、従来どおりその借用値自身の Origin を保持する。

この扱いは、同じ instance の `move` を持たない Stored Property の標準 get / set にも適用する。Custom getter / setter の通常の receiver 規則は変更しない。

### 4.2. 標準 set による復旧

単純代入は Value Lifetime の右辺先行規則に従う。

```text
person.name = createName()
// 右辺の結果確保 -> 対象 field の確定 -> 残る旧値の破棄 -> 配置
```

| 対象 storage | 標準 set の処理 |
| --- | --- |
| Initialized | 旧値を破棄して配置する |
| Moved / 初期化を許可された Uninitialized | 旧値を破棄せず初期化する |
| 一部だけが初期化済みの集合値 | 残る部分を破棄して新しい値を配置する |

全 field が Initialized に戻れば instance は complete に戻り、constructor は再実行しない。分岐によって旧値の有無が異なる場合も、同じ規則を経路ごとに適用する。

この復旧は構築済み instance のための規則であり、constructor や Property 宣言 initializer から未完成の `self` へアクセスする新しい権限を与えない。初回構築は専用の初期化規則に従う。

### 4.3. 自己代入と再使用

```text
person.name = move person.name // OK：右辺で取り出し、同じ field を再初期化
```

対象が `var`、`move` と標準 `set` が利用可能で、他のアクセス規則を満たす場合に許可する。この例では取り出した旧値の `deinit` を実行しない。

```text
person.name = person.name
```

こちらは右辺の get を使う。`string` の標準 getter は `ref/string` を返すため、所有する `string` が必要な setter へ暗黙に Move して渡すことはできない。

## 5. 借用・経路・破棄との関係

### 5.1. Loan と Origin

```text
let name = person.name
let ownedName = move person.name
inspect(name)                  // Error：name の Loan が Move と競合
```

借用の最終使用が Move より前なら、通常の非字句的 lifetime 解析に従って許可できる。Instance 全体への Loan は、その内部 field の Move と競合する。別 field への標準 getter の Loan は、非重複が証明されれば競合しない。

Move 結果の Origin は storage に格納されていた値の依存を保持する。新たに親全体への借用を作る操作ではなく、依存を消去・延長する操作でもない。結果や残る field が必要とする Origin / Loan を、親の破棄や再設定で無効にしてはならない。

### 5.2. ネストした Property

`move outer.inner.name` で通常の `outer.inner` getter を暗黙に迂回しない。Receiver 部分は通常の式として解決する。

標準 getter が `ref/Inner` を返すなら、`move outer.inner.name` は借用先からの Move out となり禁止する。Getter が Copy または新たな所有値として `Inner` を返す場合は、その一時値の field を取り出すのであり、元の `outer.inner` の storage を取り出すことにはならない。

所有する内部値を段階的に取り出す例は次のとおりである。

```text
var inner = move outer.inner
let name = move inner.name
inner.name = "Alice"
outer.inner = inner
```

この例では各段階で `move` と標準 `set` が必要である。複数の Property を直接辿る特別な projection 構文は、暫定版では定義しない。

### 5.3. Cleanup と deinit

取り出した値は結果側で破棄し、元の Moved field は破棄しない。親が incomplete のまま scope を離れた場合も、残っている field だけを Value Lifetime の順序で破棄する。

`deinit` を持つ祖先の Partial Move 禁止、Destruction 中の特別な receiver、明示的な `deinit` 呼び出し禁止は維持する。Move access を使ってこれらを迂回してはならない。

`defer` が後で対象 field や親全体を使用する場合、その cleanup 経路でも必要な初期化状態と Loan が成立しなければならない。正常な制御移動は Scope Exit に従い、Panic は cleanup を開始・継続しない。

`Exchange`・`Swap` は値を残したまま交換する別操作である。Move access の宣言だけでは、それらへ backing storage の排他的参照を渡す一般的な権限は追加しない。

## 6. Compiler 要件と統合

Compiler は次を検査・記録する。

- `move` の明示宣言、アクセス制限、Stored Property と標準 setter の条件。
- Receiver の所有状態、Move Path、field の初期化状態、祖先の完全性。
- Copy 型への明示 Move を含む状態更新、Loan / Origin、破棄責任。
- 分岐・繰り返し・結果転送・`defer` を含むすべての到達経路。
- Generic struct の特殊化後も成立する accessor・型・所有権の条件。

状態判定を実行時の getter / setter 選択に委ねない。標準アクセスの field 単位の意味、Move access の有無とアクセス権、型として必要な storage 情報は、別コンパイルでも利用できる interface 情報として保持する。物理レイアウトをソースコードに公開する必要はない。

部分状態に応じた cleanup の flag と最適化は Value Lifetime に従う。Move access はユーザー定義関数への通常の呼び出しではなく、必要な所有場所の状態変化を Compiler が直接表す操作とする。

既存仕様への統合では、Property の accessor 文法・`HasStorage`・標準 receiver の説明、式の前置 `move`、Value Lifetime の明示 Move と再初期化の入口を更新する。Custom setter、借用 receiver、object receiver、contract、動的 dispatch の拡張は本暫定仕様の対象外とする。
