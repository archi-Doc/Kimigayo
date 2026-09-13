# Array／Dictionary の更新 API と操作契約

2026-09-13 改訂。採用した改善案を統合した設計仕様。本書の対象範囲では本書を [SPEC](../../SPEC.md) より優先する。SPEC.md と STATUS.md への反映、コンパイラー・Core・runtime の実装は本改訂に含めない。

コードは仕様の例であり、現在のコンパイラーでの実行を保証しない。エラーと記した例は受理してはならない。既存の借用設計は [Array の Origin 伝播と Owned](2026-09-12%20Array%20Origins%20and%20Owned.md)、実装状況は [STATUS](../../STATUS.md) を参照する。

## 1. 基本契約

### 1.1 対象と用語

`Array<T>` と `Dictionary<K,V>` は Non-Copy。格納する完全な型に通常の表現可能性を要求し、Owned／Copy は要求しない。Dictionary の K は Equatable とキーの安定性を満たす。型と Origin は宣言時に決め、後の追加から型推論を再開しない。

| 用語 | 意味 |
| --- | --- |
| receiver／self | メソッドの操作対象。`values.append(10)` では values |
| Copy／Move | 値のコピーによる取得／所有権を移す取得 |
| Origin／Loan | 型に表す借用元・寿命の関係／特定の格納領域への実際の借用とアクセス制限 |
| 全体排他 | コレクション全体と競合する他のアクセスを認めない権限 |
| cleanup／deinit | スコープ終了などの後始末／値の破棄時の処理 |
| Abort | 通常の結果を返さずプロセスを終了すること |

### 1.2 所有権と結果

```text
更新操作
├─ 値引数：通常の Copy／Move で一度取得
├─ 取得済み入力：格納・結果への返却・破棄のいずれか
├─ 取り出す値：所有権と破棄責任を結果へ移す
└─ 終了
   ├─ 通常の不在：Option の None
   ├─ 重複拒否：Result の Err に取得済み入力を返す
   ├─ 不正入力・必須確保失敗：Abort
   └─ 借用・型・初期化違反：コンパイル時エラー
```

暗黙の深い複製や参照カウント増加を追加しない。拒否されても Move 元を自動復元せず、返された入力は結果から取得する。

「削除で所有値を返す」とは、格納されていた T または `(K,V)` の破棄責任を移すことである。Copy 型でも削除元に要素は残らない。T が `ref/U` なら参照値を返し、U の所有権を返すわけではない。格納・取り出し・再配置は借用元の寿命を延長しない。

### 1.3 正常終了後の状態

| 操作分類 | 状態の変化 |
| --- | --- |
| 追加 | length が増える。既存要素の相対順序を保持し、必要なら capacity を増やす |
| 既存値の置換 | 対象の値だけを変える。キー・順序・length・capacity を保持 |
| 削除 | length が減る。残る要素の相対順序と capacity を保持 |
| clear | 全要素を破棄して length を 0 にする。capacity を保持 |
| reserveCapacity／shrinkToFit | capacity と内部配置だけを変更可能。値・順序・length を保持 |
| 不在・重複拒否 | 値・順序・length・capacity をすべて保持 |

「保持」は操作による論理状態の保証であり、引数評価・等値比較・破棄による外部副作用の巻き戻しを意味しない。静的な借用依存の扱いは §5 に従う。

## 2. Array の API

表のメソッドは公開で、共通の `self: uniq/Self` を省略する。Self は `Array<T>`。空の構築は `var values: Array<T> = []` のように要素型を指定する。

| API | 契約 |
| --- | --- |
| `append(value: T) -> ()` | 末尾へ一つ追加 |
| `insert(index: isize, value: T) -> ()` | `0 <= index <= length`。指定位置の直前へ追加。length は末尾位置 |
| `pop() -> Option<T>` | 空なら None。それ以外は末尾を除いて Some(T) |
| `remove(index: isize) -> T` | `0 <= index < length`。指定要素を除いて T を返す |
| `clear() -> ()` | §1.3 の共通契約に従う |

位置の範囲外は Abort。この版の更新位置は isize のみで、Index／Range overload は追加しない。通常の添字読み取りでは Non-Copy 要素を Move できない。添字代入は旧値を破棄する通常の置換であり、旧値を返す remove とは異なる。

```kimi
var values: Array<i32> = []
values.reserveCapacity(4)
values.append(10)
values.append(30)
values.insert(1, 20)          // [10, 20, 30]
let middle = values.remove(1) // 20。残り [10, 30]
let last = values.pop()       // Some(30)。残り [10]
values.clear()                // []。容量は保持。
let missing = values.pop()    // None

var texts: Array<string> = []
let text = "hello"
texts.append(text)            // Non-Copy の string を Move。
// writeLine(text)            // エラー：Move 済み。
let removed = texts.remove(0)
writeLine(removed)            // 取り出した所有値を渡す。
```

## 3. Dictionary の API

### 3.1 メソッドと挿入順

表のメソッドは公開で、共通の `self: uniq/Self` を省略する。Self は `Dictionary<K,V>`。空の構築には型付きの `[:]` を使う。

| API | キー不在 | 同値キーあり |
| --- | --- | --- |
| `tryInsert(key: K, value: V) -> Result<(), (K,V)>` | 末尾へ追加し Ok(()) | 未変更で Err((入力キー, 入力値)) |
| `insertOrReplace(key: K, value: V) -> Option<V>` | 末尾へ追加し None | 値だけ置換し Some(旧値) |
| `remove(key: ref/K) -> Option<(K,V)>` | 未変更で None | 格納キーと値を除き Some((K,V)) |
| `clear() -> ()` | §1.3 の共通契約に従う | 同左 |

tryInsert の Err は重複だけを表す。専用エラー型は追加しない。結果を捨てる場合は通常の Core Result 警告の対象となり、返された入力も通常の規則で破棄される。

insertOrReplace の置換では、既存キーと挿入位置を保持する。旧値を結果へ確保し、新値を格納してから、使わなかった入力キーを破棄する。None は追加成功を表し、「未変更」ではない。

remove の検索引数は借用し、返す K は検索引数ではなく格納キー。削除後の同値キーの再追加は新しい末尾位置になる。格納キーを変更する API は追加しない。

等値判定・キーの安定性・法則違反時の扱いは [SPEC §12.3.4](../../SPEC.md#1234-dictionary-construction-and-duplicate-keys) に従う。検索方式・内部 hashing は実装に委ね、公開 Hash 条件や平均 O(1) は要求しない。

```kimi
var names: Dictionary<i32, string> = [1: "first"]
let candidate = "second"
let result = names.tryInsert(1, candidate)
// candidate は Move 済み。拒否入力は result から取り戻す。
match result
    .Ok(()) => ()
    .Err(let entry)
        let rejectedKey = entry.0
        let rejectedValue = entry.1
        writeLine(rejectedValue)

let previous = names.insertOrReplace(1, "updated") // Some("first")
let key: i32 = 1
let removed = names.remove(key@ref)
match removed
    .Some(let entry)
        let storedKey = entry.0
        let storedValue = entry.1
        writeLine(storedValue)
    .None => ()
```

### 3.2 既存キーへの添字代入

`map[key] = value` は既存値だけを置換し、Unit を返す。キー不在では Abort。検索キーは完全な K の値を共有借用して調べ、所有権を取得しない。K 自体が借用型の場合も、K の格納領域への共有借用と、K に含まれる借用を区別する。

```text
右辺を評価し、V を通常の Copy／Move で確保
  → receiver、検索キーを左から一度ずつ評価
  → 検索キーを共有借用して検索
  → 不在なら Abort
  → 値スロットへの排他的アクセスを確立
  → 通常の置換規則で旧値を破棄し、新値を格納
```

receiver の特定から配置完了まで構造変更・Move・破棄を禁止する。検索中は共有アクセスを保持し、更新前に既存 Loan と検索キーの借用との衝突を検査する。借用を便宜的に打ち切って更新を許可しない。

検索専用の借用を格納値へ残さない。検索キーが一時値なら通常の一時値規則で破棄する。旧値の破棄が右辺や一時値の必要な依存を無効にする場合は拒否する。旧値の破棄が正常終了しなければ新値を格納しない。

```kimi
var names: Dictionary<string, string> = ["id": "before"]
let key = "id"
names[key] = "after" // key は Move されない。
writeLine(key)
```

右辺先行は通常の単純代入規則であり、receiver 先行の更新メソッドとは異なる。複合代入は既存の対象先行規則に従い、検索・読取り・右辺評価・書戻しを一度ずつ行う。

## 4. 容量とメモリ確保

### 4.1 容量の取得・変更

両型に公開読み取り専用 `capacity: isize` を提供する。Array では要素数、Dictionary ではエントリー数であり、バイト数や bucket 数ではない。常に `0 <= length <= capacity <= isize の最大値` を満たす。

capacity は「その要素数まで、コレクション内部の動的メモリ確保なしで追加できる容量」を表す。読取りは receiver を一時的に共有し、Loan を持たない isize のスナップショットを返す。

| 公開 API（共通の `self: uniq/Self` を省略） | 契約 |
| --- | --- |
| `reserveCapacity(minimum: isize) -> ()` | 必要な総容量を指定。成功後 capacity >= minimum。縮小しない |
| `shrinkToFit() -> ()` | 削減を試みる。正常完了後は length <= 新 capacity <= 旧 capacity |

minimum が負なら Abort。現在の capacity 以下への reserve は内部配置も含め状態を変えない。shrinkToFit は capacity と length の一致や OS へのメモリ返却を保証せず、変化しない場合もある。両操作の値・順序・length の保持は §1.3 に従う。

成長倍率・丸め・物理レイアウト・空の構築時の確保の有無は固定しない。ゼロサイズ要素でも論理要素数と破棄責任を管理する。

### 4.2 確保を許す条件

| 操作・条件 | コレクション内部の動的メモリ確保 |
| --- | --- |
| 成功後の length が現在の capacity 以下の追加 | 行わない |
| 既存値の置換、pop／remove／clear、不在・重複拒否 | 行わない |
| 現在の capacity 以下への reserveCapacity | 行わない |
| capacity を超える追加・reserveCapacity | 許可する |
| shrinkToFit | 許可する |

禁止は新規確保・同容量の再確保・一時作業領域・Dictionary の補助領域にも及ぶ。削除と再追加を繰り返しても保証を守り、必要な空き領域の再利用や表の整理は確保済み領域で行う。ユーザー定義の等値比較・値の生成・破棄が行う確保は対象外。

許可された必須確保の失敗は、縮小用の確保も含め Abort。回復可能な allocator API は導入しない。

```kimi
var values: Array<i32> = []
values.reserveCapacity(100)
let saved = values.capacity // 100 以上。
values.append(10)           // コレクション内部の動的確保なし。
values.clear()              // length = 0、capacity = saved。
values.shrinkToFit()        // 容量削減を試みる。
```

## 5. 借用と静的解析

### 5.1 全体排他と評価順

本書の更新メソッドは全体への `uniq` を要求する。実行時に未変更となる分岐にも同じ契約を適用する。要素スロット、Slice、空の Slice など、全体と重なる active Loan と衝突する。借用の必要期間は後続使用と観測可能な破棄から決め、字句スコープの終端まで一律に延ばさない。

メソッドは receiver、明示引数のソース順、本体の順に評価する。receiver の排他的 Loan は引数評価中も有効で、二段階借用の例外は設けない。添字代入のアクセス形成は §3.2 に従う。

```kimi
var values = [10, 20]
let expected: i32 = 10
let slot = values[0]@ref
values.append(30) // エラー：次の比較まで slot が必要。
let observed = slot == expected@ref
```

```kimi
var values = [10, 20]
// values.append(values[0]) // エラー：receiver と引数のアクセスが衝突。
let first = values[0]       // i32 を先に Copy。
values.append(first)

// values.reserveCapacity(values.length + 10) // 同じ理由でエラー。
let target = values.length + 10
values.reserveCapacity(target)
```

先にローカルへ取得しても、取得結果が格納領域への借用なら衝突は残る。所有値が必要なら pop／remove を使う。

### 5.2 依存の伝播と権限の転送

静的解析は、格納値が参照し得る Loan・格納先の対応・Reborrow 関係を保持する。同じ Origin でも異なる Loan を同一視しない。型に Origin があるだけで、実在しない Loan や使用権限を生成しない。以下の伝播を公開操作の契約として扱い、呼び出し元が非公開の本体を調べなくても同じ判定になるようにする。

| 操作 | 依存情報の扱い |
| --- | --- |
| 追加・置換 | 格納し得る入力の依存を、更新前のコレクションの集合に加える |
| tryInsert | 成否で集合を戻さず、入力 K/V の依存を保守的に加える。Err の payload は入力自身の依存を保持 |
| 動的な取り出し・旧値の返却 | 更新前の状態から、返し得る T／K／V に対応する依存の集合を結果へ伝播。旧値の結果に新入力の依存を混ぜない |
| remove／replace／clear | コレクション側の既存集合を個別要素の除去だけでは縮めない |
| 容量変更 | 既存の依存を維持 |

添字が整数リテラルでも、動的コレクションの要素ごとの依存を特別に追跡しない。結果に receiver の格納領域や検索引数への操作中だけの Loan を追加しない。完全な型に含まれる依存は別途維持する。

この版では、None／Err の確認を理由にコレクションの集合を更新前へ戻す解析は行わない。実行時に未変更でも、静的解析まで更新前の状態へ戻るとは限らない。これは型を変更する規則ではなく、既に型へ適合した入力の依存を記録する規則である。

```text
実行時の所有権・排他的な権限
  格納元 → 取り出した値へ一度だけ移る。複製しない

静的な依存情報
  コレクションと結果の両方に保守的な情報が残り得る
  → 情報が重複しても新しい権限は与えない
  → 独立性を証明できない競合アクセスは拒否する
```

`Array<uniq/T>` などにも同じ規則を適用する。取り出した権限は使えるが、残るコレクション経由の権限との独立性を自動的に保証しない。必要な使用・派生値・破棄がなくなれば通常の Loan 生存性規則を使う。空になっても型の Origin や Owned 判定は変わらない。

```kimi
var x: i32 = 10
var y: i32 = 20
var refs: Array<ref/i32> = [x@ref, y@ref]
let taken = refs.remove(0)
// y = 30 // エラー：taken は保守的に x と y の依存を保持する。
let expected: i32 = 10
let observed = taken == expected@ref
```

## 6. 更新の確定・途中終了・破棄

### 6.1 検査と確定の順序

```text
入力を評価・取得（メソッド／代入の順序に従う）
  → 入力形式・位置を検査し、必要なら検索
  → 不在・重複・置換・追加を決定
     ├─ 不在・重複拒否：未変更で None／Err
     ├─ 置換・削除：追加用の長さ・容量検査はしない
     └─ 追加：長さ上限・必要容量を検査し、必要なら確保
  → 所有権・結果・管理情報を操作の契約に従って確定
  → 不要な値を破棄し、結果を返す
```

reserveCapacity／shrinkToFit は追加分岐ではなく、要求する容量変更を検査する。重複拒否や不在の決定前に容量を変えない。長さが上限でも、重複 tryInsert は Err、既存キーの insertOrReplace は置換として処理する。

長さ・容量・必要バイト数の計算は overflow させず、表現不能な要求は Abort。位置検査や長さ検査は操作本体で行い、それより前に取得済みの入力を復元しない。

### 6.2 外部から観測できる処理

要素のずらし・再配置・所有権移送そのものに、ユーザー定義 Copy／Move／deinit を追加しない。等値比較は構造が整合した状態で行う。移送中の一時的な未初期化状態をユーザーコードへ公開せず、通常の破棄中は破棄対象への再入・置換を禁止する。

添字代入は旧値の破棄後に新値を格納する。insertOrReplace は旧値を返すため、旧値の破棄を行わず、新値の格納後に未使用の入力キーを破棄する。この差は所有権の行き先の差である。

Dictionary literal は既存どおり「キー評価 → 重複検査 → 値評価 → 挿入」の順で、重複なら値式を評価する前に Abort。通常の更新メソッドはキー・値引数を取得してから検索するため、tryInsert の重複でも値式の副作用は既に発生している。

### 6.3 途中終了

| 終了経路 | 契約 |
| --- | --- |
| 本体開始前の通常の return／exit など | 更新本体を実行しない。転送結果を先に確保し、残る取得済み引数・一時値を通常の規則で cleanup |
| None／Err を返す | API ごとの通常完了。状態の保証は §1.3 に従う |
| Abort | 結果の返却・入力復元・巻き戻し・cleanup を保証しない |
| ユーザーコードが終了しない | 後続処理や結果返却も行わない |

入力・等値比較・破棄の外部アクセスは通常の Loan／effect 検査を受ける。競合する再入アクセスを許可しない。更新の確定は、スレッド間の atomic 性や復帰可能な transaction を意味しない。

```kimi
func stopBeforeAppend() -> ()
    var values = [1, 2]
    values.append(do
        return
    )
```

この return では append 本体は始まらず、values は通常のスコープ終了規則で破棄される。Abort の cleanup 非保証とは区別する。

### 6.4 破棄順

| 対象 | 順序 |
| --- | --- |
| Array の clear・通常破棄 | 残る初期化済み要素を現在の index の降順で破棄 |
| Dictionary の clear・通常破棄 | 残る挿入順の逆順。各エントリーは V、K の順 |
| 所有 iterator の途中終了 | 未返却要素に上記の対応する順序を適用 |
| 通常の途中構築終了 | 完了した取得・配置の逆順。部分構築は既存の aggregate 規則に従う |

移送済み要素・余剰容量・空き bucket を値として読み取ったり破棄したりしない。移送先だけが破棄責任を持つ。借用値の破棄は参照先を破棄しない。破棄途中の Abort や非終了では、残りの破棄や結果返却を保証しない。

## 7. 性能・受け入れ条件・範囲

### 7.1 性能契約

n は操作前の要素数。固定された要素型について、要素生成・等値比較・ユーザーの破棄処理と allocator 内部の実行時間を除く、コレクション自身の処理を評価する。

| Array の操作 | 計算量 |
| --- | --- |
| append | 償却 O(1)。空から末尾追加だけを n 回行う処理は合計 O(n) |
| pop | O(1) |
| 順序を保持する insert／remove | O(n) |
| clear | O(n)。空の場合も呼び出し自体の固定コストはある |

append の償却保証は、途中に明示的な reserve／shrink などを挟んで毎回再配置させる操作列には適用しない。成長倍率そのものは指定せず、追加ごとに全要素を移す成長方式は認めない。

操作ごとの不要な一時配列や、要素ごとの個別確保を避ける。Dictionary に必要な O(n) の索引・管理領域は許すが、§4.2 の確保禁止を守る。

### 7.2 実装時の受け入れ条件

| 分類 | 必須事例 |
| --- | --- |
| 取得と結果 | Copy／Non-Copy／借用を含む値、拒否入力の回収、Move 元の再使用拒否、削除結果の正しい破棄責任 |
| Array | 先頭・中間・末尾、空 pop、範囲外 Abort、順序保持、追加列の償却計算量 |
| Dictionary | 重複拒否、既存キー保持、検索キー非消費、削除後の再追加順、上限での重複拒否・置換 |
| 添字代入 | 右辺先行・各式一度、検索中の構造保護、Loan 衝突、不在 Abort、旧値の破棄中に失敗した場合 |
| 容量 | 総容量指定、容量内で動的確保なし、同容量再確保・一時領域も禁止、削除と再追加の反復、縮小時の値・順序・長さ保持 |
| 借用 | slot／Slice／空 Slice、receiver と後続引数、複数 Origin・Loan、動的結果の保守的依存、Err 確認後も集合を戻さないこと |
| 排他的要素 | uniq 要素の一度だけの転送、依存情報から権限を複製しないこと、残余要素との独立性未証明時の拒否 |
| 途中終了・破棄 | 取得済み入力の通常 cleanup、literal とメソッドの評価順、二重破棄なし、逆順・V→K、所有 iterator の残余、ゼロサイズ要素、確保失敗・Abort・非終了 |

### 7.3 今回の範囲外

固定配列の容量変更、可変 Slice、新しい借用 iterator、一括操作、resize と要素生成、順序を変える削除、entry／factory API、回復可能 allocator API、特定の内部 ABI は追加しない。
