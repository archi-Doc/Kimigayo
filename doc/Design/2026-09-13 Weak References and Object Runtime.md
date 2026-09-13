# rc・arc・Weak の仕様

2026-09-13 改訂。本書はレビュー結果を適用した設計案。**SPEC.md は変更していない。** 現行 SPEC との差分と各案の採否は §6 にまとめる。既存の型・借用・取得規則は [SPEC](../../SPEC.md)、実装状況は [STATUS](../../STATUS.md) を参照する。コードは設計上の例であり、現時点で実行可能なサンプルではない。

## 1. 型と操作

### 1.1 所有権

| 型 | 保持するもの | 対象へのアクセス |
| --- | --- | --- |
| `obj/T` | object の排他的な所有権 | 通常の借用規則に従う共有・排他アクセス |
| `rc/T` | 非 atomic な strong 所有権 | 共有のみ |
| `arc/T` | atomic な strong 所有権 | 共有のみ |
| `Weak<rc/T>` / `Weak<arc/T>` | 対応する weak 管理領域 | 昇格して得た strong を通してアクセス |

これらはすべて Non-Copy。通常の取得は Move となり、count は変わらない。rc と arc は同じ寿命規則を使い、count 更新の atomic 性だけが異なる。count が 1 でも排他アクセスは与えない。rc と arc の相互変換、obj の共有所有への変換、strong だけの循環の自動回収は導入しない。

`Core.Weak<S>` は compiler が管理する struct Core。型引数 S は、外側 Semantics が rc または arc の完全な型に限る。型別名は展開して判定する。generic では `<s/T>` と `s is rc or arc` で証明し、`Weak<s/T>` と記す。Weak は通常の owner 値であり、新しい Semantics ではない。`ref/Weak<S>` は Weak 値の格納領域への借用。

Weak は常に特定の object の管理領域を保持する。値がない場合は `Option<Weak<S>>` の None とし、空の Weak や引数なし constructor は設けない。構築中・期限切れの Weak は存在するが、いずれも upgrade は None を返す。Option の niche 最適化や 8-byte 表現は保証しない。

現在は rc/arc の payload に共有アクセスしかできず、内部可変性も未導入。主な用途は構築時に確定する自己リンク・親への逆リンクと、外部の排他的なコンテナに保持する Weak。rc/arc payload 内のリンクの後付け・書き換えや observer 登録は、この API だけでは行えない。内部可変性を追加する場合も、破棄開始後の昇格禁止・復活禁止・guard による table 保護を引き継ぐ。「strong 循環を回収しない」は回収保証の境界であり、循環を作る操作の許可ではない。

arc の atomic 性は payload の同期や thread-transfer の許可を意味しない。source の thread/task とメモリモデルは [SPEC Appendix D.2](../../SPEC.md#d2-concurrency-memory-model-and-thread-transfer) の境界を維持する。

### 1.2 公開 API

T は既存規則で object payload として適格な具体 Core、S は rc/arc の完全な handle 型。下表の関数は Core の公開 intrinsic とし、同名のユーザー関数には特別な意味を与えない。`value` は入力引数名、`build` は builder 引数名。

| API | 入力 → 結果 | 効果 |
| --- | --- | --- |
| `Core.makeObj(value)` | `T → obj/T` | 完成した値から新しい object を生成 |
| `Core.makeRc(value)` / `Core.makeArc(value)` | `T → rc/T` / `T → arc/T` | strong=1 で生成 |
| `Core.clone(value)` | `ref/S → S` | strong +1。payload はコピーしない |
| `Core.clone(value)` | `ref/Weak<S> → Weak<S>` | weak +1 |
| `Core.downgrade(value)` | `ref/S → Weak<S>` | strong は変えず、weak の保持を追加 |
| `Core.upgrade(value)` | `ref/Weak<S> → Option<S>` | 生存中なら strong +1 して Some、それ以外は None |
| `Core.makeRcCyclic(build)` / `Core.makeArcCyclic(build)` | builder → `rc/T` / `arc/T` | §2.2 の循環構築 |

`Core.clone` は入力の値を明示的に複製する。rc/arc なら handle、Weak なら weak の保持責任の複製であり、payload の深い複製ではない。対象は上表の二種類に限り、string 等の一般的な複製 API は追加しない。関数の T/S/F は通常の推論を使う。共有入力には `value@ref` を渡し、入力の格納領域への借用は操作中だけ保持する。結果の外部依存は §1.3 に従う。

生成は入力を通常の Copy/Move で一度取得し、payload を新しい格納領域へ Move する。`clone` と `upgrade` は allocation しない。`downgrade` は最初の side table を確保することがある。必要な allocation の失敗と count 上限での増加は、結果を公開せず Abort。

解放は通常の自動破棄で行う。生存確認だけの API、Weak からの直接 dereference・object borrow・型検査・view 変換は追加しない。view を変える場合は strong を通して既存の操作を行う。将来の Weak upcast は §13.5.7 の静的な view 関係・Owned 証明・依存保持を再利用できる。ただし同じ pointer 表現だけでは許可せず、Move/clone と count の契約も定める。runtime の型検査が必要な downcast は別の設計とする。

### 1.3 借用依存

Weak は object 本体を生かさないが、昇格先 S の外部依存を隠さない。Origin の型情報と、実際の借用先を表す Loan を両方保持する。

| 操作 | 依存の扱い |
| --- | --- |
| strong の clone・downgrade | 対象 payload の外部依存を引き継ぐ。元の handle の格納領域に長期 Loan を作らない |
| Weak の clone | 同じ外部依存を引き継ぐ。新しい排他的 Loan anchor を作らない |
| Move | 保持責任と依存を移す |
| upgrade 成功 | 対象の依存を strong 結果へ引き継ぐ。Weak 値の格納領域は借用し続けない |
| Weak の自動破棄 | 管理領域だけを操作し、payload は観測しない |

generic 呼び出しや格納にもこの表を適用し、Loan の由来を Origin 名だけで代用しない。後続の upgrade と結果の使用に必要な依存を保つ。runtime で期限切れになったという期待だけでは依存を消去しない。

Owned は既存の OwnedOrigins で判定し、S 全体を走査する。構築中・期限切れでも型の条件は変わらない。通常の生成・downgrade・格納には一律の Owned 制約を置かない。型消去は既存の Owned 証明を必要とする。

Weak 値を所有 capture した Closure は Non-Copy。`ref/Weak<S>` の capture は通常の共有借用規則に従う。Owned な Weak の所有 capture は、common Function Type の Owned 環境に格納できる。

## 2. 寿命と構築

### 2.1 共通の状態遷移

```text
構築中 → 生存中 → 破棄中 → object 解放済み
```

| 状態 | strong の公開・昇格 | 保証 |
| --- | --- | --- |
| 構築中 | strong を公開しない。upgrade は None | 通常の object view や未初期化 payload を渡さない |
| 生存中 | strong を公開できる。upgrade は上限内で成功 | payload が完全に初期化されている |
| 破棄中 | 最後の release で strong=0。upgrade は None | 完全型の破棄を一度だけ実行 |
| object 解放済み | upgrade は None | Weak が残る間は管理領域だけを保持 |

構築完了による公開は一度だけ。破棄中・解放済みから生存中へは戻らない。通常生成と循環構築に同じ規則を適用する。構築中の None は将来の公開を否定せず、破棄開始後の None は永久に続く。

### 2.2 循環構築

`Core.makeRcCyclic<T,F>` は `F is Callable<owner, (Weak<rc/T>) -> T>` を要求する。arc 版は rc を arc に置き換える。builder は通常の Copy/Move で取得し、所有 receiver で一度だけ呼ぶ。F 自体に Owned や Copy は要求しない。

**T は Owned を必要とする。** この操作は、payload の実際の借用先が確定する前に Weak を builder へ渡す。非 static 依存を持たないことを型で証明し、出所が未確定の Loan を公開しない。通常生成にこの制約を広げない。

1. object 領域と side table を確保する。table は構築中とし、内部 guard と builder 用 Weak を一つずつ保持する。
2. 所有する Weak を builder に渡す。builder は通常の Move/clone でそれを保持できるが、upgrade は None。
3. builder の正常な戻りと呼び出し cleanup を完了し、返された完全な T を payload に Move する。
4. 生存中・strong=1 に一度だけ遷移し、結果 handle を返す。

builder が Abort / 非終了なら公開しない。回復可能な失敗結果を返す生成 API は設けず、通常の Abort・cleanup 規則に従う。成功後も未初期化格納領域の公開、自己を指す raw pointer の生成、排他的な object view の付与は行わない。

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

これで完全 payload の基点を得るための descriptor 読み取りは不要になる。base view の位置調整・動的型検査には引き続き metadata が必要。従来案より obj の論理確保サイズが増えるのは payload alignment が 8 以下の場合の 8 bytes で、16 の場合は既存の padding を使う。実際の速度・heap 消費は最適化と allocator にも依存する。配置の統一だけでは obj→rc 変換を許可しない。

この表は格納表現を定義する。引数・戻り値の渡し方は [SPEC §21.4.2](../../SPEC.md#2142-physical-function-signatures) に従って FunctionAbi が決める。1 pointer の格納だけから、直接渡しや slot 渡しを固定しない。

### 3.2 count と状態の符号化

全カウンターは 64 bit 領域を使い、**共通上限 `MaxRefCount = 2^63−1`** を適用する。weak count は guard を含む。上限での増加は更新前に Abort。表現を変えて上限を拡張する処理は設けない。

header.control の最下位 bit を表現 tag とする。

- 偶数: inline strong count を `count << 1` で格納。未公開の 0 を、構築完了時に 2（strong=1）へ設定する。最終 release 後の 0 は再利用しない。
- 奇数: `sideTablePointer | 1`。最下位 bit を外して復元する。table は 8-byte aligned であり、pointer の上位 bit は切り捨てない。

side table も通常の strong count を使う。

| 値 | 意味 |
| --- | --- |
| `1 .. MaxRefCount` | 生存中の strong count |
| `0` | 生存していない。構築中または破棄開始済みで、upgrade は None |

循環構築は strong=0、weak=2（guard と builder 用 Weak）で開始する。0→1 を許すのは、strong handle をまだ公開していない factory の構築完了処理だけで、一度に限る。公開時に構築権限を消費し、strong=0 の観測から権限を再取得してはならない。strong の clone は既存 strong を必要とし、upgrade は 0 を増やさない。§2.1 の意味上の状態は count 値だけから復元しない。

### 3.3 共通処理と移行

runtime の共通処理を `ensureSideTable`、`retainStrong`、`tryRetainStrong`、`retainWeak`、`releaseStrong`、`releaseWeak`、`publishObject` に分ける。これらは内部名であり、source API ではない。次を全経路の不変条件とする。

1. 正しい strong count の保管場所は常に一つ。arc の count 操作・移行・昇格は、全体として一つの確定点を持つ（線形化可能）。
2. object に触れる処理は、有効な所有・借用・構築・破棄の権限を持つ。初回公開以外の 0→1 は禁止する。
3. 公開済み table の利用前にその初期化を観測し、upgrade 成功後は完成した payload を観測できる。最終破棄は、それまでの適法に同期されたアクセスを引き継ぐ。count の atomic 性だけでこれらを代用しない。
4. object の解放完了まで guard を保持する。Weak または guard が残る間は table を解放・再利用しない。

通常生成では最初の downgrade だけが table を作る。既存 strong を生存させ、観測した count=n、weak=1、object=header の未公開 table を準備する。arc では control 全体への CAS で公開し、その時点から table が唯一の count 保管場所となる。失敗時は最新 count で再試行し、他の table が公開されていれば自分の未公開候補を解放してそれを使う。最後に weak を増やして Weak を返す。

arc の inline 増減も control 全体への CAS とし、移行済みの場所へ古い count を書き戻さない。rc は同じ状態遷移を非 atomic 操作で行う。移行は永久で、inline に戻さない。循環構築は最初から table を使う。どちらも公開済み table は一つで、Weak ごとの allocation は行わない。

upgrade は strong=0 なら None、上限なら更新前に Abort。それ以外は n→n+1 を試み、競合時は再判定する。**成功後にだけ table.object を読み**、Some(strong handle) を返す。最終 release が先なら失敗し、upgrade が先なら新しい strong が寿命を保持する。table.object は初期化後不変で、期限切れの table の解放時も参照しない。

### 3.4 実装ノート：atomic ordering（非規範）

以下は §3.3 を満たすための arc 実装候補。具体的な ordering を言語の保証にせず、採用時には初期化の可視性・移行・最終解放の同期を証明し、§5 の検証を行う。rc の非 atomic 実装と状態遷移を共有する。

| 操作 | ordering |
| --- | --- |
| header から公開済み table を解決 | Acquire |
| inline → table の公開 CAS | 成功 AcqRel。初期化と移行前の release の同期を引き継ぐ |
| strong / weak の複製 CAS | 成功 Relaxed |
| upgrade の CAS | 成功 Acquire、失敗 Relaxed |
| strong / weak の減算 | Release。最後なら破棄・解放前に Acquire fence |
| 通常生成の初期化 | 外部から観測不能な間に通常の初期化を完了 |
| 循環構築の完了 | factory が table.strong を 0→1 に Release store。公開済み Weak からの成功した upgrade と同期 |

この候補では CAS 失敗を Relaxed とし、最新の表現を再判定する。失敗で得た side pointer に直接触れず、Acquire 観測を経て table に進む。inline 減算は control CAS、table の減算は fetch_sub と最後の Acquire fence を使う。既存の責任を持たない減算は不変条件違反。

LLVM IR では Relaxed を `monotonic` と表す。`cmpxchg` の両 ordering は少なくとも monotonic、失敗側には release / acq_rel を指定できない。上表を IR に落とす際は使用する LLVM 版の verifier でも確認する。[LLVM LangRef](https://llvm.org/docs/LangRef.html#cmpxchg-instruction)、[Atomic ordering](https://llvm.org/docs/Atomics.html#monotonic)

## 4. 例

各例の constructor は単純化のため公開している。例4.2は例4.1の Item を使う。

### 4.1 生成・複製・Move・借用

```kimi
struct Item
    public let number: i32
    public init(number: i32)
        self.number = number

func sharedExample()
    let first: rc/Item = Core.makeRc(Item.init(7))
    let second = Core.clone(first@ref)      // strong +1
    let moved = second                     // count は変わらない。second は消費済み
    let weak = Core.downgrade(first@ref)    // strong は変わらない
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
| 同期 | table 初期化と pointer 公開、循環構築完了と upgrade、移行前の release を引き継ぐ最終破棄、最後の weak release と解放 |
| 配置と ABI | 全 mode の payload+16、base 調整、元領域の解放、alignment 上限、失効 pointer 非参照、FunctionAbi の一致 |

小さいテスト用上限でも共通 count 規則を検査する。操作の順序を列挙するテストだけでは弱いメモリ順序を証明できない。メモリモデル上の検証、LLVM IR の検査、最適化後の機械語・runtime テストを分ける。構文解析の成功を Borrow Check や実行の成功と扱わない。NativeAOT テストは明示指定がない限り実行しない。

## 6. レビュー結果と現行 SPEC との差分

本書内では次の改訂を採用する。現行 SPEC の規範を書き換えたものではなく、実装時には対応箇所との同期が必要。

| 案 | 判断・理由 | SPEC との関係 |
| --- | --- | --- |
| 1. header を 16 bytes に統一 | 採用。完全 payload の位置を mode によらず固定できる。base 調整は残り、各アクセスの速度向上や descriptor の物理的な唯一性は保証しない | §21.2.3 の obj header / offset を更新する改訂案 |
| 2. cyclic の Owned 制約を撤廃 | 見送り。結果の実際の Loan は capture の依存だけからは定まらない。下記参照 | §13.5.8・§15.2.3 の T-is-Owned を維持 |
| 3. Building 番兵を廃止 | 採用。構築権限と一度だけの公開で復活を防ぎ、count=0 を共通化できる | §21.2.3 の符号化・公開手順を更新する改訂案 |
| 4. 空 Weak を廃止 | 採用。不在は Option に統一。Option のサイズ増加の可能性は受け入れる | §3.2.2・§13.5.9・§21.2.3・§22.1 の空値・constructor を削除する改訂案 |
| 5. 適用範囲を明記 | 採用。ただし「用途は二つだけ」「safe な strong 循環は絶対に作れない」という網羅的な断定はしない | 既存の共有アクセス・機能導入境界を説明 |
| 6. ordering を実装ノートへ移動 | 一部採用。具体表を非規範化し、観測不能な初期化を簡略化。count の原子性だけでは保証できない可視性・最終破棄前の同期は必須条件として残す | §21.2.3 の ordering 規定を再構成する改訂案。D.2 の単一スレッド境界は維持 |

**案2をそのまま適用しない理由。** [SPEC §11.3.2](../../SPEC.md#1132-static-storage)・[§15.6.4](../../SPEC.md#1564-calls-and-origin-propagation) は、入力の寿命に制限した mutable static field の借用を helper が返す場合を認め、結果に実際の Field anchor を保持させる。builder が capture した参照をその入力に使っても、返される payload の Loan は別の Field を指す。したがって「非 static の依存は F の capture からしか来ない」は成立しない。

capture 由来でも、共有参照の Copy、排他的参照の Reborrow、参照値の Move では Loan の責任が異なる（§15.8.2）。F 全体の依存保持という方針は使えるが、それだけでは結果の Loan と宣言済み Origin の対応を代用できない。問題は保守的に多く保持することではなく、必要な依存をすべて含むと保証できない点にある。

制約を緩和するには、builder の公開された結果契約から **T に残る依存と Loan の移譲を特定し、呼び出し前の Weak にも同じ依存を付けられる** 規則が必要。呼び出し先の本体を調べず generic 呼び出しでも検証でき、循環・表現不能な依存を拒否する設計まで揃えてから扱う。現在は T-is-Owned を維持し、通常生成には広げない。

文書上の修正として、generic の証明方法、Weak upcast の将来境界、arm 終了時の破棄、obj を含む handle の列挙を追記した。clone は本書内を `Core.clone(x@ref)` に統一。SPEC §3.5.1 の `text.clone()` は一般複製の説明用の例であり、本書の二種類の intrinsic と同じ API とは定義されていない。string 版 Core.clone の追加はせず、SPEC の例も変更していない。

参考: [Rust の循環構築](https://doc.rust-lang.org/std/sync/struct.Arc.html#method.new_cyclic)、[Swift の side table](https://github.com/swiftlang/swift/blob/main/stdlib/public/SwiftShims/swift/shims/RefCount.h)、[LLVM atomic ordering](https://llvm.org/docs/Atomics.html)、[Windows HeapAlloc](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapalloc)。これらの実装全体を移植するものではない。
