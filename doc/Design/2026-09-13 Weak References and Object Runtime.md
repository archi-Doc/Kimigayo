# rc・arc・Weak の仕様

2026-09-13 改訂。レビューで採用した設計をまとめる。**SPEC.md は未変更で、未反映の差分は §6 に示す。** 共通の型・借用・取得規則は [SPEC](../../SPEC.md)、実装状況は [STATUS](../../STATUS.md) に従う。例示コードは設計の説明であり、現在の compiler で実行できることを示さない。

## 1. 型と操作

### 1.1 所有権

| 型 | 保持するもの | 対象へのアクセス |
| --- | --- | --- |
| `obj/T` | object の排他的な所有権 | 通常の借用規則に従う共有・排他アクセス |
| `rc/T` | 非 atomic な strong 所有権 | 共有のみ |
| `arc/T` | atomic な strong 所有権 | 共有のみ |
| `Weak<rc/T>` / `Weak<arc/T>` | 対応する weak 管理領域 | 昇格して得た strong を通してアクセス |

これらはすべて Non-Copy。通常の取得は Move で、count は変わらない。rc と arc は同じ寿命規則を使い、count 管理の atomic 性が異なる。rc/arc は strong=1 でも排他アクセスを与えない。

`Core.Weak<S>` は compiler が管理する struct Core。型引数 S は、外側 Semantics が rc または arc の完全な型に限る。型別名は展開して判定する。generic では `<s/T>` と `s is rc or arc` で証明し、`Weak<s/T>` と記す。Weak は通常の owner 値であり、新しい Semantics ではない。`ref/Weak<S>` は Weak 値の格納領域への借用。

Weak は常に特定の object の管理領域を保持する。不在は `Option<Weak<S>>` の None とし、空の Weak や引数なし constructor は設けない。期限切れの Weak は管理領域を保持した値であり、不在とは異なる。Option の空き bit 利用による最適化や 8-byte 表現は保証しない。

### 1.2 公開 API

T は object payload にできる具体 Core、S は rc/arc の完全な handle 型。以下は Core の公開 intrinsic。型引数は通常の推論を使い、`value` / `build` を引数名とする。同名のユーザー関数には特別な意味を与えない。

| API | 入力 → 結果 | 効果 |
| --- | --- | --- |
| `Core.makeObj(value)` | `T → obj/T` | 完成した値から新しい object を生成 |
| `Core.makeRc(value)` / `Core.makeArc(value)` | `T → rc/T` / `T → arc/T` | strong=1 で生成 |
| `Core.clone(value)` | `ref/S → S` | strong +1。payload はコピーしない |
| `Core.clone(value)` | `ref/Weak<S> → Weak<S>` | weak +1 |
| `Core.downgrade(value)` | `ref/S → Weak<S>` | strong は変えず、weak の保持を追加 |
| `Core.upgrade(value)` | `ref/Weak<S> → Option<S>` | 生存中なら strong +1 して Some、それ以外は None |
| `Core.makeRcCyclic(build)` / `Core.makeArcCyclic(build)` | builder → `rc/T` / `arc/T` | §2.2 の循環構築 |

`Core.clone` は上表の保持責任を明示的に複製し、payload を深く複製しない。clone・downgrade・upgrade は同じ object・view・mode を保つ。共有入力には `value@ref` を渡し、入力は使い続けられる。借用依存は §1.3 に従う。

通常生成は完成した T を Copy/Move で一度取得し、新しい object の payload へ Move する。constructor や deinit は再実行しない。clone・upgrade は allocation せず、downgrade は最初の side table を確保することがある。必要な確保の失敗と count 上限での増加は、結果を公開せず Abort。解放は自動破棄で行う。

### 1.3 借用依存

Weak は payload を生かさないが、昇格先 S の外部依存を隠さない。Origin の型情報と実際の借用先を表す Loan を、generic 呼び出し・格納でも保持する。入力 handle の格納領域への借用は操作中だけとし、新しい排他的 Loan anchor は作らない。

| 操作 | 依存の扱い |
| --- | --- |
| strong の clone・downgrade・Weak の clone | 同じ payload の外部依存を引き継ぐ |
| Move | 保持責任と依存を移す |
| upgrade 成功 | 対象の依存を strong 結果へ引き継ぐ |
| Weak の自動破棄 | 管理領域だけを操作し、payload は観測しない |

Loan の由来を Origin 名だけで代用しない。後続の upgrade と結果の使用に必要な依存は、runtime で期限切れになるという期待だけでは消去できない。

Owned は既存の OwnedOrigins で判定し、S 全体を走査する。構築中・期限切れでも型の条件は変わらない。通常の生成・downgrade・格納には一律の Owned 制約を置かない。型消去は既存の Owned 証明を必要とする。

Weak を所有 capture した Closure は Non-Copy。`ref/Weak<S>` の capture は共有借用規則に従う。共通関数値への変換には、環境全体の Owned に加え、Shared 呼び出しなど [SPEC §7.6.4](../../SPEC.md#764-function-references-and-common-type-conversion) の全条件が必要。

### 1.4 適用範囲と未導入の機能

主な用途は、構築時に確定する自己リンク・親への逆リンクと、外部の排他的なコンテナに保持する Weak。内部可変性は未導入のため、rc/arc payload 内のリンクの後付け・書き換えや observer 登録は、この API だけでは行えない。

次の機能は追加しない。

- rc↔arc・obj→共有所有の変換、string 等の一般的な複製 API、strong 循環の自動回収。
- 生存確認だけの API、Weak から payload への直接アクセス・object borrow・型検査・view 変換。アクセスや view 変更には、upgrade で得た strong を使う。
- source の thread/task・payload 同期・thread-transfer。arc の atomic 性はこれらを許可せず、[SPEC D.2](../../SPEC.md#d2-concurrency-memory-model-and-thread-transfer) に従う。

将来の内部可変性も §2–3 の寿命・guard 規則を引き継ぐ。Weak upcast には [SPEC §13.5.7](../../SPEC.md#1357-object-upcasts) の静的な view 関係・Owned 証明・依存保持を再利用できるが、Move/clone・count の契約が別途必要。同じ pointer 表現だけでは許可せず、runtime の検査を要する downcast とも区別する。

## 2. 寿命と構築

### 2.1 共通の状態遷移

全 object は次の寿命段階を持つ。表の count・Weak の規則は rc/arc に適用し、obj は count を持たない。

```text
構築中 → 生存中 → 破棄中 → object 解放済み
```

| 状態 | rc/arc の公開・昇格 | 共通の保証 |
| --- | --- | --- |
| 構築中 | strong を公開しない。upgrade は None | 通常の object view や未初期化 payload を渡さない |
| 生存中 | strong を公開できる。upgrade は上限内で成功 | payload が完全に初期化されている |
| 破棄中 | 最後の release で strong=0。upgrade は None | 完全型の破棄を一度だけ実行 |
| object 解放済み | upgrade は None。Weak が残る間は管理領域だけを保持 | payload にアクセスしない |

構築完了による公開は一度だけ。破棄中・解放済みから生存中へは戻らない。通常生成と循環構築に同じ規則を適用する。構築中の None は将来の公開を否定せず、破棄開始後の None は永久に続く。

### 2.2 循環構築

`Core.makeRcCyclic<T,F>` は `F is Callable<owner, (Weak<rc/T>) -> T>` を要求する。arc 版は rc を arc に置き換える。builder は通常の Copy/Move で取得し、所有 receiver で一度だけ呼ぶ。F 自体に Owned や Copy は要求しない。

**T には Owned を要求する。** payload の借用先が確定する前に Weak を渡すため、非 static 依存を持たないことを型で証明する。通常生成にはこの制約を広げない。

1. object 領域と side table を確保する。table は構築中とし、内部 guard と builder 用 Weak を一つずつ保持する。
2. 所有する Weak を builder に渡す。builder は通常の Move/clone でそれを保持できるが、upgrade は None。
3. builder の正常な戻りと呼び出し cleanup を完了し、返された完全な T を payload に Move する。
4. 生存中・strong=1 に一度だけ遷移し、結果 handle を返す。

builder が Abort / 非終了なら strong を公開せず、巻き戻しや後続 cleanup は保証しない。回復可能な失敗結果を返す生成 API、自己を指す raw pointer、排他的な object view は追加しない。

Owned 制約を外して F の capture の依存だけを引き継ぐ案は採用しない。[SPEC §11.3.2](../../SPEC.md#1132-static-storage)・[§15.6.4](../../SPEC.md#1564-calls-and-origin-propagation) は、入力の寿命に制限した mutable static field の借用を helper が返す場合を認める。この Field の Loan は capture の Loan とは別であり、Copy・Reborrow・Move による責任の違いも残る。緩和には、builder の公開された結果契約から実際の Loan と移譲を特定し、呼び出し前の Weak にも同じ依存を付ける規則が必要。

### 2.3 最後の解放

side table がある object の最終 release は、次の順序を守る。

1. strong を 1 から 0 にして、以後の upgrade を禁止する。
2. 動的な完全型を破棄する。
3. 元の object allocation を解放する。
4. side table の内部 guard を release する。

weak count は「外部 Weak の数 + 内部 guard」。最後の weak release で table を解放する。payload 内の Weak が破棄されても、guard が手順 4 まで table を保護する。table のない rc/arc は strong=0 の後に payload と object 領域だけを解放する。obj は count 操作なしで完全型を破棄し、object 領域を解放する。

破棄が Abort / 非終了になれば、残りの cleanup と解放は実行されない。元の格納領域を解放する際に、base view や payload の途中を指す pointer を使ってはならない。

## 3. Windows x64 の内部表現

### 3.1 配置

本改訂案の object handle・header・payload の配置は本節だけで定義し、他の設計書はここを参照する。ObjectDescriptor 自体のレコード形式は [metadata 設計 §5.3](2026-09-13%20Value%20Borrows%20Closures%20and%20Metadata.md#53-objectdescriptor) で定める。

| 対象 | 配置 | サイズ / alignment |
| --- | --- | --- |
| obj / rc / arc / objref / objuniq handle | 元の object header への non-null pointer | 8 / 8 bytes |
| Weak 値 | side table への non-null pointer | 8 / 8 bytes |
| obj header | `+0 descriptor pointer`, `+8 予約領域（0 で初期化）` | 16 / 8 bytes |
| rc / arc header | `+0 descriptor pointer`, `+8 control word` | 16 / 8 bytes |
| side table | `+0 strong count`, `+8 weak count`, `+16 object pointer` | 24 / 8 bytes |

全 mode の header を 16 bytes に統一する。rc と arc は同じ配置で、arc の control・strong・weak の公開後のアクセスを atomic にする。未公開で他から観測できない領域は通常の書き込みで初期化できる。mode は所有 handle の完全な型から選び、公開後の count に atomic / 非 atomic アクセスを混在させない。

descriptor は不変で、動的型の identity、payload のサイズ・alignment、base の位置、完全型の破棄と元領域の解放方法に到達できる。同じ動的完全型・配置では全 mode で共有し、mode の処理は所有 handle 側で選ぶ。descriptor のアドレスを型 identity と同一視しない。

object allocation を 16-byte aligned とし、完全 payload は常に `header + 16` に置く。全サイズ計算を検査し、`2^63−1` bytes の上限超過・確保失敗は既存の runtime 規則で Abort。16 bytes を超える payload alignment は生成時に未対応診断とする。独自の過剰確保や prefix は追加しない。

完全 payload の基点は descriptor を読まずに求められるが、base view の調整・動的型検査には metadata が必要。旧配置に比べ、obj は payload alignment が 8 以下なら論理確保サイズが 8 bytes 増え、16 なら変わらない。実際の速度・heap 消費は最適化と allocator にも依存する。

この表は格納表現を定義する。引数・戻り値の渡し方は [SPEC §21.4.2](../../SPEC.md#2142-physical-function-signatures) に従って FunctionAbi が決める。1 pointer の格納だけから、直接渡しや slot 渡しを固定しない。

### 3.2 count と状態の符号化

全カウンターは u64 領域を使い、**共通上限 `MaxRefCount = 2^63−1`** を適用する。weak count は guard を含む。上限での増加は更新前に Abort とし、wrap や表現変更による上限拡張は行わない。

header.control の最下位 bit を表現 tag とする。

- 偶数: inline strong count を `count << 1` で格納。未公開の 0 を、構築完了時に 2（strong=1）へ設定する。最終 release 後の 0 は再利用しない。
- 奇数: `sideTablePointer | 1`。最下位 bit を外して復元する。table は 8-byte aligned であり、pointer の上位 bit は切り捨てない。

side table も通常の strong count を使う。

| 値 | 意味 |
| --- | --- |
| `1 .. MaxRefCount` | 生存中の strong count |
| `0` | 生存していない。構築中または破棄開始済みで、upgrade は None |

循環構築は strong=0、weak=2（guard と builder 用 Weak）で開始する。0→1 は factory の初回公開だけに許し、その時点で構築権限を消費する。strong=0 を読んでも権限は再取得できない。strong の clone は既存 strong を必要とし、upgrade は 0 を増やさない。§2.1 の状態は count 値だけから復元しない。

### 3.3 共通処理と移行

runtime の共通処理は `ensureSideTable`、`retainStrong`、`tryRetainStrong`、`retainWeak`、`releaseStrong`、`releaseWeak`、`publishObject` とする。これらは内部名であり、公開 API ではない。

#### 3.3.1 不変条件

1. 正しい strong count の保管場所は常に一つ。arc の各 count 操作・移行・昇格には、一つの確定点を定める（線形化可能）。
2. object に触れる処理は、有効な所有・借用・構築・破棄の権限を持つ。初回公開以外の 0→1 は禁止する。
3. arc の runtime 操作は §3.3.2 の先行関係を保証する。count の atomic 性だけで順序保証を代用しない。
4. object の解放完了まで guard を保持する。Weak または guard が残る間は table を解放・再利用しない。

#### 3.3.2 runtime の先行関係

**A → B** は、runtime の実行順序または同期によって、A の完了と書き込みの効果を B が前提にできる先行関係。実時間の前後だけでは成立せず、関係は推移する。

| 対象 | 必要な先行関係 |
| --- | --- |
| table 公開 | table 初期化 → control または Weak を通じた table 公開 → その経路からの table 利用 |
| object 構築 | metadata・payload の初期化と必要な builder cleanup → 構築完了 → strong を得た runtime 操作による payload 利用 |
| strong 解放 | 同じ object の先行する strong release → 最終 strong release → 完全 payload の破棄。inline から table への移行でもこの関係を維持 |
| weak 解放 | object 解放 → guard release。同じ table の先行する weak release → 最終 weak release → table 解放 |

対象は runtime による初期化・count 管理・cleanup の呼び出し・解放。ユーザーの payload アクセスの同期は §1.4 のとおり未定義であり、解放済み object へのアクセスを許す規則でもない。

#### 3.3.3 table への移行と昇格

通常生成では最初の downgrade だけが table を作る。既存 strong を生存させ、読み取った count=n、weak=1、object=header の未公開 table を準備する。arc では control 全体への CAS で公開し、その時点から table が唯一の count 保管場所となる。失敗時は最新 count で再試行し、他の table が公開されていれば自分の未公開候補を解放してそれを使う。最後に weak を増やして Weak を返す。

arc の inline 増減も control 全体への CAS とし、移行済みの場所へ古い count を書き戻さない。rc は同じ状態遷移を非 atomic 操作で行う。移行は永久で、inline に戻さない。循環構築は最初から table を使う。どちらも公開済み table は一つで、Weak ごとの allocation は行わない。

upgrade は strong=0 なら None、上限なら更新前に Abort。それ以外は n→n+1 を試み、競合時は再判定する。**成功後にだけ table.object を読み**、Some(strong handle) を返す。最終 release が先なら失敗し、upgrade が先なら新しい strong が寿命を保持する。table.object は初期化後不変で、期限切れの table の解放時も参照しない。

### 3.4 実装ノート：atomic ordering（非規範）

以下は §3.3 の先行関係を満たすための arc 実装候補。具体的な ordering を言語の保証にせず、採用時にはその関係を証明し、§5 の検証を行う。rc の非 atomic 実装と状態遷移を共有する。

| 操作 | ordering |
| --- | --- |
| header から公開済み table を解決 | Acquire |
| inline → table の公開 CAS | 成功 AcqRel。初期化と移行前の release の同期を引き継ぐ |
| strong / weak の複製 CAS | 成功 Relaxed |
| upgrade の CAS | 成功 Acquire、失敗 Relaxed |
| strong / weak の減算 | Release。最後なら破棄・解放前に Acquire fence |
| 通常生成の初期化 | 外部から観測不能な間に通常の初期化を完了 |
| 循環構築の完了 | factory が table.strong を 0→1 に Release store。公開済み Weak からの成功した upgrade と同期 |

この候補では CAS 失敗を Relaxed とし、最新の表現を再判定する。失敗で得た side pointer に直接触れず、control の Acquire load を経て table に進む。inline 減算は control CAS、table の減算は fetch_sub と最後の Acquire fence を使う。既存の責任を持たない減算は不変条件違反。

LLVM IR では Relaxed を `monotonic` と表す。`cmpxchg` の両 ordering は少なくとも monotonic、失敗側には release / acq_rel を指定できない。上表を IR に落とす際は使用する LLVM 版の verifier でも確認する。[LLVM LangRef](https://llvm.org/docs/LangRef.html#cmpxchg-instruction)、[Atomic ordering](https://llvm.org/docs/Atomics.html#monotonic)

## 4. 例

例4.2は例4.1の Item を使う。例4.4は借用依存を隠せないことを示す不正例。

### 4.1 生成・複製・Move・借用

```kimi
struct Item
    public let number: i32
    public init(number: i32)
        self.number = number

func observe<s/T>(value: ref/(s/T)) -> Weak<s/T>
    s is rc or arc
    return Core.downgrade(value)

func sharedExample()
    let exclusive: obj/Item = Core.makeObj(Item.init(3))
    let borrowed = exclusive@objref         // obj の共有借用
    let first: rc/Item = Core.makeRc(Item.init(7))
    let second = Core.clone(first@ref)      // strong +1
    let moved = second                     // count は変わらない。second は消費済み
    let weak = observe(first@ref)           // generic な downgrade。strong は不変
    let weakCopy = Core.clone(weak@ref)     // weak +1
    let view = moved@objref                 // count を変えない共有借用

    match Core.upgrade(weakCopy@ref)
        .Some(let acquired) => Core.writeLine("生存中") // acquired はこの arm の終了時に破棄
        .None => Core.writeLine("昇格できない")
    // 関数を出るときは、残るローカルの所有責任を通常の順序で解放する。
```

### 4.2 strong がなくなった後の Weak

```kimi
func expiredExample() -> Weak<arc/Item>
    let value: arc/Item = Core.makeArc(Item.init(1))
    return Core.downgrade(value@ref)
    // Weak の戻り値を確保した後、value の最後の strong を解放する。

let expired = expiredExample()
match Core.upgrade(expired@ref)
    .Some(let value) => Core.writeLine("生存中")
    .None => Core.writeLine("期限切れ")     // この例はこちら

let absent: Option<Weak<arc/Item>> = .None
// absent は Weak を保持しない。expired は対象の管理領域を保持する。
match absent
    .Some(let weak) => Core.writeLine("Weak あり")
    .None => Core.writeLine("Weak なし")    // この例はこちら
```

### 4.3 自分への Weak を持つ object

```kimi
struct Node
    public let selfLink: Weak<rc/Node>
    public init(selfLink: Weak<rc/Node>)
        self.selfLink = selfLink

let build = func [] (selfWeak: Weak<rc/Node>) -> Node
    match Core.upgrade(selfWeak@ref)
        .Some(let unexpected) => $abort("構築中には昇格できない")
        .None => ()
    return Node.init(selfWeak)              // Weak を field へ Move

let node: rc/Node = Core.makeRcCyclic(build)
match Core.upgrade(node.selfLink@ref)
    .Some(let sameNode) => Core.writeLine("構築完了")
    .None => Core.writeLine("期限切れ")
```

Node の自己リンクは strong を増やさない。最後の strong がなくなれば、Node 内の Weak も cleanup され、guard の解放後に table を解放できる。

### 4.4 借用依存は Weak にしても消えない（不正例）

```kimi
struct View origin source
    public let value: ref/i32 from source
    public init(value: ref/i32 from source)
        self.value = value

func invalidEscape() -> Weak<rc/(View from static)>
    let local: i32 = 42
    let object: rc/View = Core.makeRc(View.init(local@ref))
    return Core.downgrade(object@ref)       // エラー: local の借用を static にできない
```

同じスコープ内で非 static の依存を保持する通常の Weak は許可する。期限切れになる予定でも、型の依存を static に変更する理由にはならない。

## 5. 実装時の確認事項

| 分類 | 必須の確認 |
| --- | --- |
| 型と取得 | 不適格な Weak 引数、Move 後使用、Option による不在、clone の対象、pair generic の制約・推論 |
| 借用 | §1.3 の依存伝播、非 static の不正 escape、型消去、Closure、cyclic の T-is-Owned 条件 |
| 構築と解放 | 構築中の None、公開後の成功、builder cleanup 後の公開、完全型の一度だけの破棄、payload 内の最後の Weak と guard |
| count と競合 | 上限で更新せず Abort、0→1 の初回公開限定、移行と複製・解放の競合、upgrade と最終 release、未公開候補 table の解放 |
| 先行関係 | §3.3 の各関係が実装で成立すること。CAS 失敗経路・inline→table 移行でも保証を維持し、解放済み object にアクセスしないこと |
| 配置と ABI | 全 mode の payload+16、base 調整、元領域の解放、alignment 上限、失効 pointer 非参照、FunctionAbi の一致 |

小さいテスト用上限でも共通 count 規則を検査する。操作の順序を列挙するテストだけでは弱いメモリ順序を証明できない。メモリモデル上の検証、LLVM IR の検査、最適化後の機械語・runtime テストを分ける。構文解析の成功を Borrow Check や実行の成功と扱わない。NativeAOT テストは明示指定がない限り実行しない。

## 6. SPEC・関連設計書との同期

以下は現行 SPEC への未反映事項。実装前に同期し、本書の改訂を既存の実装対応と混同しない。

| 改訂 | 本書 | SPEC の同期箇所 |
| --- | --- | --- |
| 全 mode の header・payload 基点を統一 | §3.1 | §21.2.3 |
| 構築中も strong=0 とし、Building 番兵を廃止 | §3.2 | §21.2.3 の符号化・公開手順 |
| 不在を Option に統一し、空 Weak・constructor を削除 | §1.1–2 | §3.2.2、§3.5.1 の Copy 分類表、§13.5.9、§21.2.3、§22.1 |
| runtime の先行関係を必須条件、ordering 表を実装候補に分離 | §3.3–4 | §21.2.3。D.2 の境界は維持 |

cyclic の T-is-Owned は現行 SPEC §13.5.8・§15.2.3 を維持する。理由は §2.2 に示す。SPEC §3.5.1 の `text.clone()` は一般複製の説明用であり、本書で string 版 Core.clone を定義したものではない。

[metadata 設計 §5.3](2026-09-13%20Value%20Borrows%20Closures%20and%20Metadata.md#53-objectdescriptor) は同期済み。object 配置は本書 §3.1、descriptor の形式は同設計書で定義し、重複した配置規則を置かない。

参考: [Rust の循環構築](https://doc.rust-lang.org/std/sync/struct.Arc.html#method.new_cyclic)、[Swift の side table](https://github.com/swiftlang/swift/blob/main/stdlib/public/SwiftShims/swift/shims/RefCount.h)、[LLVM atomic ordering](https://llvm.org/docs/Atomics.html)、[Windows HeapAlloc](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapalloc)。これらの実装全体を移植するものではない。
