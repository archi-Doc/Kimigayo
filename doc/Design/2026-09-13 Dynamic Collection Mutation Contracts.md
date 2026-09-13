# Array／Dictionary の更新 API と操作契約

2026-09-13 改訂。採用済みの決定を統合した仕様。本書の対象では [SPEC](../../SPEC.md) より本書を優先する。§5.3 の引数適合は、採用時点からコレクション以外も含む言語全体の SPEC §10.2 に適用する。

SPEC.md／STATUS.md への反映と実装は今回の対象外。コードは仕様例であり、現在のコンパイラーでの実行を保証しない。判断の経緯は[採否記録](../Decisions/2026-09-13%20Dynamic%20Collection%20Proposal%20Review.md)、既存の借用設計は [Array の Origin 伝播と Owned](2026-09-12%20Array%20Origins%20and%20Owned.md)、実装状況は [STATUS](../../STATUS.md) を参照する。

## 1. 共通契約

### 1.1 型・用語・表記

`Array<T>` と `Dictionary<K,V>` は Non-Copy。格納する完全な型に表現可能性を要求し、Owned／Copy は要求しない。Dictionary の K は Equatable とキーの安定性を満たす。型と Origin は宣言時に決め、後の追加から型推論を再開しない。

| 用語 | 意味 |
| --- | --- |
| receiver／self | メソッドの操作対象 |
| Copy／Move | コピーによる取得／所有権を移す取得 |
| Origin／Loan | 型に表す借用元・寿命の関係／格納領域への実際の借用とアクセス制限 |
| 全体排他 | コレクション全体と競合する他のアクセスを認めない権限 |
| cleanup／deinit | 後始末／値の破棄時の処理 |
| Abort | 通常の結果を返さずプロセスを終了すること |

API は公開のインスタンス操作。Self は所属する型であり、表で receiver を省略した更新メソッドは `self: uniq/Self` を取る。tryGet と capacity の読取りは共有アクセスを使う。

### 1.2 取得・返却・失敗

```text
値引数を一度取得
├─ Copy 型：Copy
└─ Non-Copy 型：Move
   ↓
取得した値の行き先
├─ コレクションへ格納
├─ 結果へ返却
└─ 通常の規則で破棄
```

暗黙の深い複製や参照カウント増加を追加しない。拒否されても Move 元を復元せず、返された入力は結果から取得する。取り出した値の所有権・破棄責任は結果へ移り、Copy 型でも削除元に要素は残らない。参照型の要素を返す場合、所有するのは参照値であり参照先ではない。格納・移送・返却で借用元の寿命を延長しない。

前提違反は Abort、正常な不在は Option、理由や拒否入力を返す失敗は Result とする。`try` は指定した失敗を値で返すことを示す。同名の Abort 版の存在や、すべての失敗からの回復は要求しない。必須確保と任意の縮小の違いは §4、途中終了は §6 に従う。

### 1.3 正常終了後の状態

| 操作 | 保証 |
| --- | --- |
| 追加 | length を増やし、既存要素の相対順序を保持。必要なら capacity を増やす |
| 既存値の置換 | 対象の値だけを変え、キー・順序・length・capacity を保持 |
| 削除 | length を減らし、残る相対順序と capacity を保持 |
| clear | 全要素を破棄して length = 0。capacity を保持 |
| reserve／shrinkToFit | capacity と内部配置だけを変更可能。値・順序・length を保持 |
| tryGet、不在・重複拒否 | 値・順序・length・capacity を保持 |

これは操作による論理状態の保証であり、引数式・等値比較・破棄が起こした外部副作用を巻き戻す保証ではない。静的な依存情報は §5.2 に従う。

## 2. Array の API

| API | 契約 |
| --- | --- |
| `append(value: T) -> ()` | 末尾へ一つ追加 |
| `insert(index: isize, value: T) -> ()` | 指定位置の直前へ追加 |
| `insert(index: Index, value: T) -> ()` | 同上。更新前の長さで Index を解決 |
| `pop() -> Option<T>` | 空なら None。それ以外は末尾を除いて Some(T) |
| `remove(index: isize) -> T` | 指定要素を除いて T を返す |
| `remove(index: Index) -> T` | 同上。更新前の長さで Index を解決 |
| `clear() -> ()` | §1.3 に従う |

位置は本体で一度解決する。更新前の長さを L とすると、isize は先頭からの位置、Index は既存の境界解決に従う。from-end は offset <= L を確認してから `p = L - offset` を計算する。insert は `0 <= p <= L`、remove は `0 <= p < L` を要求し、範囲外は Abort。isize と Index の暗黙変換、Range overload は追加しない。

| 指定 | 意味 |
| --- | --- |
| `insert(^0, value)` | 末尾追加。空でも可 |
| `insert(^1, value)` | 末尾要素の直前。空なら Abort |
| `remove(^1)` | 末尾削除。空なら Abort。空を None にしたい場合は pop |
| `remove(^0)` | 常に範囲外 |

Index 構築が引数評価中に失敗した場合は、その時点で後続評価を止める。

通常の添字読み取りは Non-Copy 要素を Move しない。添字代入は旧値を破棄する置換であり、旧値を返す remove とは異なる。

```kimi
var values: Array<i32> = []
values.reserve(additional: 4)
values.append(10)
values.append(30)
values.insert(1, 20)          // [10, 20, 30]
let middle = values.remove(1) // 20。残り [10, 30]
values.insert(^1, 25)         // [10, 25, 30]
let last = values.remove(^1)  // 30。残り [10, 25]
values.clear()                // 容量を保持。
let missing = values.pop()    // None
values.insert(^0, 5)          // 空への末尾追加。

var texts: Array<string> = []
let text = "hello"
texts.append(text)            // string を Move。
// writeLine(text)            // エラー：Move 済み。
let removed = texts.remove(0)
writeLine(removed)            // 取り出した所有値を渡す。
```

## 3. Dictionary の API

### 3.1 追加・置換・削除

| API | キー不在 | 同値キーあり |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K,V)>` | 末尾へ追加し Ok(()) | 未変更で Err((入力キー, 入力値)) |
| `insertOrReplace(key: K, value: V) -> Option<V>` | 末尾へ追加し None | 値だけ置換し Some(旧値) |
| `remove(key: ref/K) -> Option<(K,V)>` | 未変更で None | 格納キーと値を除き Some((K,V)) |
| `clear() -> ()` | §1.3 に従う | 同左 |

tryInsert の Err は重複だけを表す。専用エラー型は追加しない。結果の破棄は通常規則に従い、結果式を捨てる場合は Core Result の警告対象となる。

insertOrReplace は既存キーと挿入位置を保持する。旧値を結果へ確保し、新値を格納してから、使わなかった入力キーを破棄する。None は追加成功であり、未変更を意味しない。

remove は検索キーを借用し、格納されていた K/V を返す。削除した同値キーの再追加は新しい末尾位置になる。格納キーを変更する API は追加しない。等値判定・キーの安定性・法則違反時の扱いは [SPEC §12.3.4](../../SPEC.md#1234-dictionary-construction-and-duplicate-keys) に従う。

```kimi
var names: Dictionary<i32, string> = [1: "first"]
let candidate = "second"
let result = names.tryInsert(1, candidate) // candidate は Move 済み。
match result
    .Ok(()) => ()
    .Err(let entry)
        let rejectedKey = entry.0
        let rejectedValue = entry.1
        writeLine(rejectedValue)

let previous = names.insertOrReplace(1, "updated") // Some("first")
let key: i32 = 1
let removed = names.remove(key) // 検索キーは消費しない。
// removed は Some((1, "updated"))。
```

### 3.2 既存キーへの添字代入

`map[key] = value` は既存値だけを置換して Unit を返す。不在では Abort。検索キーは remove と同じ `ref/K` 引数の型適合を使い、所有権を取得しない。K 自体の借用と、K の格納領域への借用は区別する。

```text
右辺を評価し V を確保
  → receiver、検索キーを左から一度ずつ評価
  → キーを共有アクセスして検索。不在なら Abort
  → 値スロットへの排他的アクセスを確立
  → 旧値を破棄し、新値を格納
```

receiver の特定から配置完了まで構造変更・Move・破棄を禁止する。検索中は共有アクセスを保持し、更新前に既存 Loan と検索キーの借用との衝突を検査する。借用を便宜的に打ち切って更新を許可しない。

キーの一時値は §5.3 に従う。旧値の破棄が右辺や一時値の必要な依存を無効にする場合は拒否し、破棄が Abort／非終了なら新値を格納しない。

```kimi
var names: Dictionary<string, string> = ["id": "before"]
let key = "id"
names[key] = "after"
writeLine(key) // 添字代入で key は Move されない。
```

右辺先行は通常の単純代入規則である。複合代入は対象先行で、検索・読取り・右辺評価・書戻しを一度ずつ行う。

### 3.3 不在を返す検索

`tryGet(self: ref/Self, key: ref/K) -> Option<ref/V from self>` は、不在なら None、存在すれば値スロットへの共有参照を Some で返す。V の Copy 性によらず、V 自体の Copy／Move・削除は行わない。

結果の外側の Loan は Dictionary 全体の格納領域を保護し、V 内部の依存も保持する。検索キーやその一時値への操作中だけの借用は返さない。結果が Some を含み得る間は通常の借用検査を行う。

```kimi
var names: Dictionary<string, string> = ["id": "before"]
let exists = match names.tryGet("id")
    .Some(_) => true
    .None => false
if exists
    names["id"] = "after"
let removed = names.remove("id") // 一時的な string キーを共有借用。
```

この例では match 終了で結果の借用が終わるため更新できる。bool 自体は、その後の存在や値を保証する予約ではない。

## 4. 容量とメモリ確保

### 4.1 取得・予約・縮小

両型の公開読み取り専用 `capacity: isize` は、内部の動的確保なしで追加できる要素数／エントリー数の上限を表す。バイト数や bucket 数ではない。常に `0 <= length <= capacity <= isize の最大値`。読取りは一時的な共有アクセスで、Loan を持たないスナップショットを返す。

型付きの空リテラル `[]`／`[:]` は length = capacity = 0、内部の動的確保なしとする。管理値の初期化には固定コストがある。ゼロサイズ要素でも論理要素数と破棄責任を維持する。

| API | 契約 |
| --- | --- |
| `reserve(additional: isize) -> ()` | 本体開始時の長さ L に対し、成功後 capacity >= R = L + additional。縮小しない |
| `shrinkToFit() -> ()` | 削減を試みる。正常完了後は length <= 新 capacity <= 旧 capacity |

reserve の additional は常に追加数であり、capacity に加える量ではない。負値・加算上限超過は Abort。R <= capacity なら内部配置も変えず、0 は常に容量変更不要。引数名は省略できるが、例と診断の引数説明・修正例では `additional:` を明示する。総量指定の reserveCapacity は提供しない。

shrinkToFit は capacity == length や OS へのメモリ返却を保証せず、変化しない場合もある。両操作の値・順序・length の保持は §1.3 に従う。

### 4.2 確保と失敗の保証

| 操作・条件 | コレクション内部の動的確保 |
| --- | --- |
| capacity 以内の追加、R <= capacity の reserve | 禁止 |
| 空の構築、tryGet、既存値の置換、pop／remove／clear、不在・重複拒否 | 禁止 |
| capacity を超える追加・reserve | 許可。必須確保に失敗すれば Abort |
| shrinkToFit | 許可。縮小用確保に失敗すれば完全な現状維持で正常終了 |

禁止は新規確保・同容量の再確保・一時作業領域・Dictionary の補助領域にも及ぶ。削除・再追加を繰り返しても守り、必要な整理は確保済み領域で行う。ユーザーの入力生成・等値比較・破棄による確保は別である。

縮小は古い領域を維持して準備し、成功後にだけ移送する。確保や縮小候補の表現計算が成立しなければ、値・順序・length・capacity・内部配置を保持する。不変条件違反などの Abort を捕捉する規則ではない。

追加と reserve の拡張は §7.1 の共通成長方針に従う。倍率・丸め・物理レイアウトは固定しない。過剰確保の丸めが上限を超える場合は表現可能な容量へ抑え、要求を満たせるのに丸めだけを理由に拒否しない。

```kimi
var values: Array<i32> = []
values.reserve(additional: 100)
let saved = values.capacity // 100 以上。
values.append(10)           // 内部の動的確保なし。
values.clear()              // length = 0、capacity = saved。
values.shrinkToFit()        // 縮小用確保に失敗しても戻る。
```

## 5. 借用と静的解析

### 5.1 全体排他と評価順

更新メソッドは receiver 全体への uniq を、後続引数の評価前から保持する。実行時に未変更となる分岐にも適用し、二段階借用は導入しない。明示引数はソース順に一度評価する。添字代入のアクセス形成は §3.2 に従う。

要素スロット・Slice・空の Slice など、全体と重なる active Loan は更新と衝突する。必要期間は後続使用と観測可能な破棄から決め、字句スコープ終端まで一律に延ばさない。

```kimi
var values = [10, 20]
let expected: i32 = 10
let slot = values[0]@ref
values.append(30) // エラー：次の比較まで slot が必要。
let observed = slot == expected@ref
```

```kimi
var values = [10, 20]
// values.append(values[0])        // エラー：引数が receiver を読む。
// values.append(values.remove(0)) // エラー：引数が receiver を更新する。
let first = values[0]              // i32 を先に Copy。
values.append(first)
values.reserve(additional: 10)     // 本体で現在の長さを使う。
values.insert(^1, 40)              // 本体で末尾からの位置を解決。
```

先に取得しても結果が格納領域への借用なら衝突は残る。所有値が必要なら pop／remove を使う。

### 5.2 依存の伝播と権限

以下を公開契約とし、呼び出し元が非公開の本体を調べなくても同じ判定になるようにする。Loan の識別・格納先との対応・Reborrow 関係を保持する。同じ Origin でも異なる Loan を統合せず、型だけから実在しない Loan や使用権限を作らない。

| 操作 | 静的な依存情報 |
| --- | --- |
| 追加・置換 | 格納し得る入力の依存を更新前の集合へ追加 |
| tryInsert | 成否によらず入力 K/V の依存を追加。Err の payload は入力自身の依存を保持 |
| 取り出し・旧値の返却 | 更新前に返し得る T/K/V の依存を結果へ伝播。新入力の依存を旧値の結果へ混ぜない |
| remove／replace／clear | 個別要素の除去だけではコレクション側の集合を縮めない |
| 容量変更 | 既存の依存を維持 |
| tryGet | 集合を変えず、格納領域の共有 Loan と V の依存を結果へ伝播 |

整数リテラルの添字でも要素ごとの依存を特別に追跡せず、None／Err の確認でもコレクションの集合を更新前へ戻さない。実行時の未変更と、静的解析の状態復元は別である。型・Origin・Owned 判定は、空や削除を理由に変更しない。

所有値を返す結果には、receiver の格納領域や検索引数への操作中だけの Loan を追加しない。完全な型に含まれる依存は維持する。

```text
実際の所有権・排他的な権限：格納元から結果へ一度だけ転送
静的な依存情報：コレクションと結果の両方に残り得る
  → 情報の重複は権限の複製ではない
  → 独立性を証明できない競合アクセスは拒否
```

`Array<uniq/T>` も同じ規則を使い、取り出した値と残余要素の独立性を自動的に保証しない。以後の使用・派生値・破棄で不要になった依存には通常の Loan 生存性規則を適用する。

```kimi
var x: i32 = 10
var y: i32 = 20
var refs: Array<ref/i32> = [x@ref, y@ref]
let taken = refs.remove(0)
// y = 30 // エラー：taken は保守的に x と y の依存を保持。
let expected: i32 = 10
let observed = taken == expected@ref
```

### 5.3 owner 一時値の暗黙の共有借用

引数適合に「owner の完全な型 T の一時値 → ref/T」を追加する。一度評価して Temporary Place に保持し、共有借用する。型が分かる引数からは、同型の owner Place と同じ経路で T を推論でき、期待型の事前確定を必須としない。

未型付けリテラルは候補ごとに fitting し、receiver・他の引数・既知の結果型の制約を処理した後で、通常の値渡しと同じ既定型規則を使う。候補比較前の一律 i32/f64 化、既定型による曖昧さの解消、null・空コレクションなどへの新しい既定型は導入しない。

借用順位は owner Place と同じ cross-semantics。Exact や直接の literal fitting より優先せず、型推論と値の取得方法を混同しない。uniq 引数・object handle・既存借用の外側への暗黙の層追加には広げない。既存の Copy/Reborrow を優先し、K が `ref/X` なら `@ref/ref/X` などで格納領域の借用型を明記する。

```kimi
func makeValue() -> i64 => 1
func g<T>(x: ref/T) -> () => ()
g(makeValue()) // T = i64。
g(1)           // 他の制約がなく、通常の既定型で T = i32。

func choose(x: ref/i32) -> () => ()
func choose(x: ref/i64) -> () => ()
// choose(1)   // エラー：両候補に fitting でき、既定型では選ばない。
```

一時値の寿命は既存の最外式・条件・match などの境界に従い、少なくとも呼び出し中は維持する。呼び出し終了だけを理由に短縮せず、結果や格納先への借用保持を理由に延長もしない。寿命越えの診断は暗黙借用の位置・一時値の終了点・必要な使用を示し、型と Origin の条件も満たせる場合に、必要期間生存するローカルへの束縛を提案する。

### 5.4 公開 effect

receiver／引数へのアクセスと、内部から呼び出し得るユーザー処理の effect を公開要約として合成する。

| 操作 | 内部から呼び出し得るユーザー処理 |
| --- | --- |
| Array の append／insert／pop／remove、両型の容量変更 | なし |
| Dictionary の tryGet／tryInsert／remove | K の等値比較 |
| Dictionary の insertOrReplace | K の等値比較、未使用の入力 K の破棄 |
| Dictionary の添字代入 | K の等値比較、旧 V の破棄 |
| Array の clear・通常破棄・添字代入 | T の破棄 |
| Dictionary の clear・通常破棄 | V と K の破棄 |

引数式・default・一時値 cleanup・返却値の後の破棄は、対応する位置で別途合成する。組込み操作にはその既存契約を適用する。

generic 定義では比較・再帰的な破棄の effect を型パラメーターに依存する式として検証・公開し、具体化では検証済みの対応へ代入する。非公開本体の再調査や、有利な具体化までの定義時検証の先送りはしない。許される実装・分岐の effect は和を取る。

要約から不要と分かる static アクセスは付けない。一方、receiver の uniq/ref 権限でユーザーコードの外部アクセスを免除せず、mutable static の Dictionary への競合する再入や、不明で衝突を否定できない effect は拒否する。

## 6. 更新の確定と終了処理

### 6.1 処理順

```text
メソッド／代入の規則で入力を評価・取得
  → 位置を検査し、必要なら検索
  → 不在・重複・置換・追加を決定
     ├─ 不在・重複拒否：状態を変えず None／Err
     ├─ 置換・削除：追加用の上限検査はしない
     └─ 追加：長さ・必要容量を検査し、必要なら必須確保
  → API の順序で所有権移送・配置・破棄を実行
  → 確保した結果を、必要な cleanup の完了後に返却
```

reserve／shrinkToFit は §4 の容量操作として処理する。重複拒否・不在の決定前に容量を変えず、長さ上限でも重複 tryInsert と既存値置換は通常どおり行う。長さ・容量・必要バイト数は overflow させず、表現不能な必須要求は Abort。

要素移送自体にユーザー定義 Copy/Move/deinit を追加しない。等値比較は構造が整合した状態で行い、移送中の一時的な未初期化状態を公開しない。破棄対象への競合する再入・置換を禁止する。これはスレッド間の atomic 性を保証しない。

Dictionary literal は「キー評価 → 重複検査 → 値評価 → 挿入」で、重複なら値式の前に Abort。メソッドはキー・値引数を取得してから検索するため、重複拒否でも値式は評価済みとなる。

### 6.2 途中終了

| 終了経路 | 契約 |
| --- | --- |
| 本体開始前の通常の return／exit など | 本体を実行せず、転送結果を先に確保して残る取得済み引数・一時値を通常 cleanup |
| None／Err、任意縮小の断念 | API ごとの正常終了 |
| Abort | 返却・入力復元・巻き戻し・cleanup を保証しない |
| ユーザーコードの非終了 | 後続処理・返却を行わない |

```kimi
func stopBeforeAppend() -> ()
    var values = [1, 2]
    values.append(do
        return
    )
```

この return では append 本体は始まらず、values は通常のスコープ終了規則で破棄される。

### 6.3 破棄順

| 対象 | 順序 |
| --- | --- |
| Array の clear・通常破棄 | 残る初期化済み要素を現在の index の降順 |
| Dictionary の clear・通常破棄 | 残る挿入順の逆順。各エントリーは V、K の順 |
| 所有 iterator の途中終了 | 未返却要素に上記の順序を適用 |
| 通常の途中構築終了 | 完了した取得・配置の逆順。部分構築は既存の aggregate 規則に従う |

移送先だけが破棄責任を持つ。移送済み領域・余剰容量・空き bucket を値として読んだり破棄したりせず、借用値の破棄で参照先を破棄しない。

## 7. 性能・確認事項・範囲

### 7.1 性能契約

n は要素数、c は capacity。固定された型について allocator 内部・入力生成・破棄処理の時間を別に数える。d は、完全な型から cleanup 省略を証明できない要素／エントリー数。参照カウント解放も cleanup に含み、実行時の Case を調べるコストまで省略したことにはしない。

| 操作 | コレクション自身のコスト |
| --- | --- |
| Array の append／pop | append は償却 O(1)、pop は O(1) |
| Array の順序を保つ insert／remove | O(1 + n)。破棄不要でも移動は残る |
| Array の clear | O(1 + d)。cleanup 不要の型なら O(1) |
| Dictionary の tryGet | 検索コスト＋O(1) の管理コスト |
| Dictionary の追加・削除・置換 | 検索コスト＋償却 O(1) の管理コスト |
| Dictionary の clear | O(1 + c + d) を許す。索引リセットに O(c) を使ってよい |

**共通の成長保証。** 縮小を挟まない m 回の操作列について、C を「初期容量・最大 length・各 reserve の必要総容量 R」の最大値とする。additional 単体や、実装が任意に過大確保した容量は基準にしない。両型の拡張に伴う領域準備・移送の管理コストは合計 O(m + C) 以内。空から `values.reserve(additional: 1)` と末尾追加を n 回反復しても、Array の合計は O(n) となる。

**Dictionary の会計。** 操作の種類によらず、等値比較・内部ハッシュ計算などキー内容を調べる仕事と、候補/bucket の探索を検索コストとする。要素移送・索引書換え・順序更新は管理コストに残す。線形探索も許し、公開 Hash 条件やユーザー定義ハッシュ呼出しを追加しない。

拡張・tombstone 除去・無確保の整理を含む再索引のキー処理回数は、同じ操作列全体で O(m + C) 以内。特定のキーが生涯 O(1) 回だけ処理される保証ではない。ハッシュ再計算の内容依存の時間は実際の検索コストへ数え、保存か再計算かは実装が選ぶ。

既知のエントリーの削除・配置・順序更新は、満容量で削除と追加を反復しても償却 O(1) とし、§4.2 の無確保保証を守る。O(c) の索引・管理領域、空き slot と連結情報などを許し、物理的に隣接した格納は要求しない。

不要な一時配列や要素ごとの個別確保を避け、観測可能な順序を維持する。大きな reserve 自体の O(1) は保証せず、shrinkToFit の呼び出しは償却保証の対象外とする。

### 7.2 実装時の確認事項

| 分類 | 必須事例 |
| --- | --- |
| Array | Copy/Non-Copy、isize/Index、先頭・中間・末尾、空の ^0 挿入・^1 拒否、^0 削除拒否、pop の None、Index 構築時と解決時の Abort |
| Dictionary | 重複入力の回収、既存キー保持、検索キー非消費、再追加順、長さ上限での拒否・置換、tryGet の Copy/Non-Copy 値・Loan・bool 化後の更新 |
| 容量・性能 | 引数名あり/なしの追加数指定、負値・加算上限・0、予約反復、空の無確保構築、同容量/一時領域も含む確保禁止、縮小成功・断念、満容量での削除と追加、可変長キー、整理を含む再索引回数、cleanup 不要型の clear |
| 借用・推論 | slot/Slice/空 Slice、receiver と後続引数、複数 Origin/Loan、保守的な結果依存、Err 後の集合保持、uniq 権限の一度だけの転送と独立性未証明時の拒否 |
| 引数適合・effect | コレクション外の一時値、generic 推論と既定型順、overload 順位・曖昧さ、寿命診断と束縛提案、参照層の暗黙追加禁止、公開要約と static 再入 |
| 評価・破棄 | 添字代入の右辺先行・各式一度、検索中の構造保護、literal とメソッドの評価順、途中転送の cleanup、旧値破棄中の Abort/非終了、二重破棄なし、逆順・V→K、所有 iterator の残余、ゼロサイズ要素 |

### 7.3 今回の範囲外

今回は次を追加しない。

- 型・参照：固定配列の容量変更、可変 Slice、新しい借用 iterator。
- 更新：一括操作、resize と要素生成、順序を変える削除。
- 検索・挿入：contains、重複時 Abort の Dictionary.insert、entry/factory API。
- メモリ管理：回復可能 allocator API、特定の内部 ABI。

tryGet で確認してから添字代入する例は検索を二度行う。一度の検索で既存値に応じた更新や必要時だけの値生成を行う操作は、借用参照を通した書込みの構文・取得契約と合わせて別途設計する。uniq 版の検索だけを先に追加しない。
