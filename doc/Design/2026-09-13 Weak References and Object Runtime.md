# Weak references と rc / arc の object runtime 設計

2026-09-13。**採用した設計。** `Core.Weak<rc/T>` / `Core.Weak<arc/T>`、遅延 side table、以下の通常の生成・昇格・解放 protocol を採用し、言語契約と内部配置の要点を SPEC に反映した。公開 API の最終的な綴りと §6 の cyclic factory は引き続き別途設計する。コンパイラー実装は未変更であり、この文書は実装完了を意味しない。

## 1. 採用範囲

合意済み: strong/object-borrow handle は共通 header を指す 1 pointer。object の動的型・完全な破棄責任を保持する。rc は非 atomic、arc は atomic。strong count は Windows x64 で 64 bit を使い、初期値 1、Move では増減せず、明示的複製で増加する。最大値での増加は wrap させず Abort。最後の release は完全な破棄後に元の object allocation を解放する。

追加採用: Core.Weak<S>、遅延 side table、inline count の tagging、64 bit 論理上限、昇格と最終解放の protocol、weak の借用依存。新しい `weak` Semantics は導入しない。

従来の SPEC §3.3 / §13.5.8 / §15.2.3 / §15.8.1 / §16.3.3 / §21.2 / §21.4 を土台とする。現在の single-thread execution と未設計の source concurrency（Appendix D.2）は維持する。以下の arc protocol は runtime の並行操作を設計するもので、thread-transfer や payload の並行変更を許可するものではない。

### 1.1 rc / arc の簡単な設計

`rc/T` と `arc/T` は、同じ object を複数の strong owner で共有する Non-Copy handle。どちらも object header を指す 1 pointer で、payload の Copy 性によって handle が Copy になることはない。

| 項目 | rc/T | arc/T |
| --- | --- | --- |
| 参照カウント | 非 atomic | atomic |
| 用途 | 同一スレッド内の共有所有 | atomic な所有権管理が必要な共有所有。source の thread-transfer 許可とは別 |
| payload へのアクセス | 共有のみ | 共有のみ。payload の同期機構を自動で追加しない |
| 対応する weak | Weak<rc/T> | Weak<arc/T> |
| object header | descriptor + control、16 bytes | 同じ配置、control と table の count が atomic |

生成は完成した owner/T を通常の Copy/Move で一度取得し、新しい object 領域に Move して、metadata と payload の完成後に strong=1 の handle を公開する。payload と header は通常一つの allocation にまとめる。具体的 payload の生成に blanket Owned 制約は置かず、借用依存を保持する。

| 操作 | strong count と責任 |
| --- | --- |
| 通常の代入・引数・戻り値 | Move。元の handle は消費され、count は変化しない |
| 明示的な strong 複製 | 同じ object / view / mode の owner を追加し、count +1。payload をコピーしない |
| objref/T への借用 | count は変えず、通常の Loan で owner による生存を保証する |
| Weak への downgrade | strong は変えず、weak の保持を追加する |
| Weak からの upgrade | 生存中だけ strong +1、成功結果が新しい所有責任を持つ |
| handle の破棄 | strong -1。最後の owner だけが完全型を破棄して object allocation を解放する |

weak が残る場合は object 本体を保持せず、side table だけを保持する。strong 複製・Weak 昇格・weak 複製の上限超過は wrap させず Abort。最初の Weak または inline count の表現上限でのみ通常経路から side table に移行するため、その場合の必要な allocation は失敗し得る。

count=1 でも排他的 payload access は与えない。rc と arc の相互変換、obj からの所有 mode 変更、borrow からの strong 生成は追加しない。view を変更しても動的型・元の領域・破棄責任を保持する。strong だけの循環は自動回収しない。詳細な配置・atomic ordering・競合処理は §3–§5 に定める。

## 2. 型: Core.Weak<S>

`S` は正規化後の外側 Semantics が rc または arc の完全な strong handle Type に限る。初期の payload/View Target の適格性は既存の object 規則に従う。

| 型 | 昇格の結果 |
| --- | --- |
| Weak<rc/T> | Core.Option<rc/T> |
| Weak<arc/T> | Core.Option<arc/T> |

T が裸の payload Core である Weak<T>、Weak<obj/T>、Weak<objref/T>、Weak<Weak<arc/T>> は不適格。generic 引数が完全な strong handle Type なら適格であり、generic 定義ではその外側 Semantics が rc/arc である証拠を要求する。型別名は展開して判定する。runtime Contract View の利用は既存の導入境界を越えない。rc と arc の変換や統合は行わない。

Weak は compiler-known Core の値型とし、新しい Semantics は導入しない。外側は通常の owner であり、既存の value/owning 分類に属する。この owning は weak 管理領域の責任を所有するという意味で、payload の strong ownership を持つという意味ではない。`reference` / `object` / `borrow` の分類集合を拡張しない。`ref/Weak<S>` は weak 値の格納領域への共有借用である。

Weak<S> は空の場合も含め常に Non-Copy。代入・引数・戻り値では通常の Move。明示的な複製は weak count を増やすため、Copy にしてはならない。Core 型として自動 cleanup を持ち、ユーザーが field 配置や deinit を置換できないようにする。強参照と同様、空か否かで型の Copy 性は変わらない。

完全型の identity と generic generation key は S の rc/arc、View Target、全 Type/Origin 引数を保持する。Weak 型自体の runtime representation と object の Runtime Type Identity は別の概念である。

### 2.1 操作の契約

以下の操作名は設計上の名称。公開 API の綴りは別途選定する。候補は Core.downgrade、Weak<S>.upgrade、Weak<S>.clone、空の Weak<S> constructor。

| 操作 | 入力 | 結果・効果 |
| --- | --- | --- |
| 空の weak | 適格な S | 空の Weak<S>。allocation なし |
| downgrade | 初期化済み S handle の共有借用 | 同じ object / view / mode の Weak<S>。strong は変えない。必要なら side table を確保 |
| upgrade | Weak<S> の共有借用 | Core.Option<S>。成功時のみ strong を 1 増やす。weak 自体は保持 |
| weak 複製 | Weak<S> の共有借用 | 同一 table を保持する新しい Weak<S>。空なら count 操作なし |
| weak Move | 所有する Weak<S> | 責任だけ移転。count 操作なし |
| weak 破棄 | 所有する Weak<S> | 外部 weak count を 1 減らす。payload の破棄や strong 減算は行わない |

downgrade は元の strong handle の格納領域に新たな長期 Loan を残さない。処理中は通常の共有アクセスを確保し、元の所有責任が失われないようにする。初期仕様では obj、object borrow、raw pointer からの生成を認めない。

空・期限切れの upgrade は通常の None。生存中でも strong 上限での増加は Abort であり、None とは区別する。allocation failure と weak count overflow も Abort。入力は一度だけ評価・取得し、結果は取得処理が完了してから公開する。

Weak<S> は payload の field/member への直接アクセス、暗黙 dereference、object borrow 生成、runtime `is`、checked cast の入力にならない。最初に upgrade し、得た S を通して既存の操作を行う。weak の view 変換も初期仕様では提供しない。必要なら strong の状態で view を変えてから downgrade する。

生存確認だけを行う public API は初期仕様に含めない。確認と使用を分けても寿命は確保できない。成功した upgrade が得た strong owner だけが使用期間を確保する。

### 2.2 借用・Owned・Closure

Weak は object 本体を生かさないが、S の Type/Origin 引数と payload の外部 Loan 依存を消去しない。upgrade はその依存を引き継ぐ S を返す。新しい非 static Origin や独立した排他的 Loan anchor を生成しない。

Weak<S> の OwnedOrigins には既存の generic 引数走査によって S 全体が寄与する。Owned は S の依存がすべて static と証明できるときに限り成立する。空の Weak や runtime で期限切れになった値にも、型レベルのこの規則を適用する。普通の具体的 Weak 生成・格納に blanket Owned 制約は置かない。

非 Owned payload の weak は、後で upgrade できる使用やその結果を通じた観測に必要な外部 Loan を保持する。object 本体が既に破棄されたはずだという期待だけで、期限切れ分岐の依存を消す解析は導入しない。weak の自動破棄自体は side table だけを操作し、payload を観測しない。最後の使用と cleanup の依存は既存の Loan / destruction analysis で区別する。

base/runtime-contract への payload erasure は従来どおり Owned 証明を必要とし、weak 化はこの検査の迂回手段にならない。既に証明された erased view の証明は維持する。

Weak 値を所有 capture した Closure は Non-Copy。capture は明示 Move または weak の明示複製から行う。ref/Weak<S> の capture は通常の共有借用の Copy/Loan 規則に従う。Owned な Weak<S> の所有 capture は common Function Type の Owned 環境と両立する。Weak<S> の lifetime independence と thread safety は別である。

## 3. Windows x64 の配置

strong / objref / objuniq は object header への non-null 1 pointer。Weak<S> は side table への nullable 1 pointer。null は空の weak を表し、source の null literal を安全な参照型に許可するものではない。

```text
ObjHeader (size 8, align 8)
  +0  descriptor: ptr
  ... payload alignment padding
      complete payload

CountedHeader (size 16, align 8)
  +0  descriptor: ptr
  +8  control: u64                  // arc は atomic
  ... payload alignment padding
      complete payload

SideTable (size 24, align 8)
  +0  strong: u64                   // arc は atomic
  +8  weak: u64                     // arc は atomic
  +16 object: ptr                   // 初期化後は不変
```

metadata / allocator 内部領域はこのサイズに含めない。rc/arc は同じ配置だが atomic 性が異なり、同じ allocation に atomic と非 atomic の count 操作を混在させない。Weak<S> の S から mode を静的に選ぶ。動的な mode tag は不要。

object の descriptor は不変で、Runtime Type Identity、payload offset / alignment、base adjustment、完全型の破棄、allocation 解放方針に到達できる。obj と rc/arc では payload offset が異なるため、配置 descriptor と共通の型識別情報を区別する。objref から所有 mode を推測して offset を決めてはならない。

### 3.1 control の encoding と上限

control は 64 bit word。最下位 bit を representation tag にする。

- bit 0 = 0: `control = strong << 1`。inline strong は 1 .. 2^63-1。0 は最終 release 済み。
- bit 0 = 1: `control = ptrtoint(sideTable) | 1`。mask で bit 0 を外して pointer を復元する。table は少なくとも 8-byte aligned。

これは Windows x64 の integral address-space-0 pointer に限定した内部 encoding。48-bit address の仮定、上位 bit の切り捨て、調整済み object/view pointer の格納は行わない。

**論理 strong 上限は 2^64-1 のまま。** inline が 2^63-1 で増加を要求された場合、現在の count をそのまま side table に移行し、その table 上で checked increment を行う。したがって inline の上限は言語上の count overflow ではない。最初の weak 作成だけでなく、この極端に大きい count でも table allocation が生じ得る。

side table は一度公開したら inline に戻さない。table 上の strong は u64 全体を使い、状態 bit を共用しない。weak counter は「外部 Weak の数 + object の解放完了まで保持する内部 guard 1」。合計の上限は 2^64-1。guard がある間は外部 Weak の上限がその分 1 小さい。上限を超える increment は更新前に Abort。

### 3.2 allocation と alignment

通常の object は header と payload の一括 allocation 一つ。最初の weak で別 allocation の side table を追加し、その後は共有する。候補 table の並行確保が競合した場合、一時的に複数確保されることはあるが、公開されるのは一つだけ。弱参照一個ごとの allocation はしない。

payloadOffset = alignUp(headerSize, payloadAlignment)。allocation alignment は max(8, payloadAlignment)。サイズ計算・round-up は checked にする。Windows HeapAlloc が保証する alignment を超える場合は、header/payload block を overallocate して整列し、header の直前に元の allocation pointer を保存する。header 自体を動かして handle の基点とし、descriptor の解放方針から prefix の有無を決める。サイズは prefix と最大 padding を含めて検査する。

side table は通常の 8-byte alignment で十分。object 解放は元の allocation pointer を使い、payload/view pointer を HeapFree に渡さない。allocation elimination は identity と全 weak/strong 使用、最後の解放までの有効性を証明できる場合に限る。

## 4. 遅延 side table への移行

初期の CountedHeader.control は 2（logical strong 1）。inline と side のどちらか一方だけを正しい count の保管場所とする。

1. 操作を行う既存 strong owner を生存させて header を読む。
2. inline count n に対して未公開 table を準備する。strong=n、weak=1（guard）、object=header。
3. header.control を、読んだ inline word から tagged table pointer に CAS する。
4. 成功した瞬間に table が唯一の count 保管場所になる。移行自体では logical count を変えない。
5. count 更新との競合で失敗したら最新値に合わせて private table を再準備して再試行する。他の table が公開済みなら未公開候補を解放し、その table を使う。
6. downgrade は公開 table の weak を checked increment して外部 Weak を返す。overflow 移行なら移行後の strong を checked increment する。

inline の strong 更新も control 全体への CAS にする。事前に inline と読んだ処理が移行後に古い場所へ fetch_add/fetch_sub してはならない。CAS の失敗時に再読して side path に切り替える。

arc の control 読み取りは Acquire、移行の成功 CAS は AcqRel。これにより table 初期化を公開し、移行前の release の同期も引き継ぐ。rc は同じ論理 protocol を非 atomic 操作で実装する。移行中にユーザーコードは呼ばない。

## 5. upgrade と release

### 5.1 upgrade

非空の Weak は table 自体の寿命を保証する。**object pointer を読む前に table.strong の増加に成功しなければならない。**

```text
upgrade(weak):
    if weak is empty: return None
    loop:
        n = table.strong.load(Relaxed)
        if n == 0: return None
        if n == UINT64_MAX: Abort
        if CAS(table.strong, n, n + 1, Acquire, Relaxed):
            // この取得済み strong 責任により object は生存している。
            return Some(strongHandle(table.object))
```

成功時の CAS が昇格の確定点。object と結果の static View Target は元のものを保持する。コピーした payload、別 object、別 ownership mode を返さない。None は source weak を消費しない。Weak の Clone/Move/upgrade が対象 lifetime を復活させることはない。

### 5.2 strong release

inline は count を 1 減らす control CAS。side は table.strong の fetch_sub。arc は Release、最後の 1 -> 0 を行った処理だけが Acquire fence 後に完全型の破棄を始める。普通の release は有効な strong 責任を一つ持つことが前提で、0 からの減算は runtime/compiler 不変条件違反。

side の最後の release は次の順序を守る。

1. strong を 1 -> 0 に確定し、その後の upgrade を失敗させる。
2. 由来する動的な完全型を一度だけ破棄する。
3. 元の object allocation を解放する。
4. table の内部 weak guard を一つ release する。
5. それが最後の weak count なら table を解放する。

payload 自身に Weak が格納されていて、その cleanup が最後の外部 Weak を release しても、guard が手順 4 まで table を保護する。破棄が Abort / diverge した場合、後続の cleanup / object 解放 / guard release は実行されない。通常の Abort 契約に従い leak-free を保証しない。

table.object は不変のままでよい。object 解放後は stale pointer となるが、成功した upgrade 以外から読み出し・比較・dereference・metadata 参照に使わない。table の解放処理もこの pointer を使用しない。

### 5.3 weak release と atomic ordering

外部 Weak と guard の release は同じ weak counter を減らす。arc は Release fetch_sub、最後なら Acquire fence の後に table を解放。通常の weak 複製は上限検査付き Relaxed CAS。空の weak の操作は no-op。

strong 複製は既存 strong を保持し、inline なら checked control CAS、side なら checked count CAS。増加自体は Relaxed でよいが、header の side pointer 解決には Acquire 読み取りを使う。CAS 失敗で side pointer を得る経路も Acquire を満たしてから table に触れる。成功/失敗 ordering が LLVM の cmpxchg 制約に適合するよう、Relaxed failure 後に新たな Acquire load でループする実装を許す。

upgrade が先に 1 -> 2 を確定すれば release 後にも strong 1 が残る。release が先に 1 -> 0 を確定すれば upgrade は失敗する。object への投機的アクセスや raw pointer の存在検査で代用しない。生きた Weak が table を保持するため、同じ table address を別 lifetime に再利用する ABA は発生させない。

この protocol は lock-free な atomic 操作で構成できるが、allocation や destructor の実行まで lock-free / wait-free とは保証しない。rc/arc は count 1 でも shared payload access のみ。weak の存在確認や count の観測から排他 access を付与しない。

## 6. 空、期限切れ、構築中の境界

基本の状態遷移は `LiveInline -> LiveSide -> Destroying -> Expired -> TableFreed`。weak を作らない場合は LiveInline から直接 Destroying と object 解放に進む。side への移行だけでは lifetime や view は変わらない。Expired から Live へ戻さない。

空の Weak は table がない。期限切れ Weak は table があり strong が 0。いずれも upgrade は None。公開の null、raw weak pointer、unowned、GC、cycle collection は導入しない。全てが strong の循環は残るので、必要な辺を明示的に weak にする。

**循環構築には別の決定が必要。** 現行 rc/arc は共有アクセスだけなので、「完成後に自分への Weak を field に書き込む」例を無条件に受理できない。通常の downgrade は完成済み object のみを対象とし、この規則を破らない。

自己 weak や immutable な親子の back-reference まで初期 API で扱うなら、Core の cyclic factory を追加する案を推奨する。意味は、factory が構築専用 table と object 領域を用意し、所有する Weak<S> を一度だけ builder に渡し、builder が返す完成した T を格納してから strong 1 を公開すること。構築中の upgrade は None。builder に通常の object view や未初期化 payload は渡さない。

この factory は未採用の追加案である。採用する場合は Building(strong=0) -> Live(strong=1) という構築専用の一回限りの遷移を定義し、最終 release 後の 0 -> 1 と区別する。外部 Weak を渡した後の初期化の公開は Release、upgrade 成功は Acquire。構築 guard、builder の Copy/Move receiver、戻り値取得と cleanup、失敗時に不完全 payload を破棄しないことを正式な Core 契約に統合する必要がある。通常の downgrade/upgrade 実装だけでこの API が完成したとは扱わない。

## 7. 内部 ABI と実装境界

Weak<S> の storage は 8 bytes / align 8、内部表現は nullable ptr。ただし外側 owner の Core aggregate なので、初期 ABI は既存の aggregate 引数スロット・先頭 result slot 規則を使う。1 word の型をすべて scalar 引数にする新ルールは導入しない。explicit ValueLowering で storage/computation/cleanup を記録する。

strong と object borrow は今回合意した直接 pointer passing を使う。Option<S> の戻り値は既存の enum ABI。null niche optimization を自動的な ABI 保証にしない。runtime helper は同一 FunctionAbi から定義と呼び出しを生成する。診断 context は既存の ABI 位置と Abort 契約を維持する。

実装は Type formation / Core declaration、Copy・Owned / Loan 解析、ownership cleanup、TypeLayout / ValueLowering、runtime lowering の順に行う。未対応段階では generation を拒否し、Weak を trivially-copyable pointer と仮定して通さない。source artifacts と runtime profile の互換性を明示的に改訂し、古い証明や配置 cache を流用しない。

## 8. 実装時の検証条件

- Weak<rc/T> / Weak<arc/T> 以外の型引数拒否、alias 正規化、generic inference、通常 Move と明示 Clone、空値の Non-Copy。
- downgrade は strong を変えない。upgrade 成功のみ strong +1。source Weak の責任は維持される。
- borrowed payload の非 static 依存、erased view の Owned 証明、空値/期限切れ値による依存隠しの拒否、Closure capture の Copy/Owned 判定。
- 最後の release と upgrade の両方の確定順序、複数 downgrade と retain/release の移行競合、未公開候補 table の cleanup。
- payload 内の最後の Weak の破棄でも guard が table を保護する。Derived の完全破棄、base view、high-alignment allocation の元領域解放。
- strong / weak 最大値の checked 増加、inline 閾値での移行、allocation failure。テスト専用の小さい上限で境界を再現し、production 上限の算術も検査する。
- 期限切れ Weak が残っていても object allocation は解放され、最後の Weak で table のみ解放される。空値は allocation しない。
- 破棄途中の upgrade は None、復活は不可能。Abort / divergence で後続 cleanup を走らせない。
- cyclic factory を採用する場合は構築中の None、完成公開後の成功、builder 失敗、外部へ退避された Weak、self Weak cleanup を追加する。

この文書のケースは受け入れ条件であり、実行済みのテストではない。NativeAOT テストは明示指定がない限り実行しない。

## 9. 比較に用いた一次資料

- [Rust Weak<Arc>](https://doc.rust-lang.org/std/sync/struct.Weak.html): weak は値の破棄を妨げないが、Rust の元 allocation は保持する。
- [Rust Weak<Rc>](https://doc.rust-lang.org/std/rc/struct.Weak.html): 非 atomic weak の型と操作。
- [Swift RefCount.h](https://github.com/swiftlang/swift/blob/main/stdlib/public/SwiftShims/swift/shims/RefCount.h): 遅延 side table と object/table の寿命の分離。本案は Swift の unowned、圧縮 count 配置、ObjC 関連 flags を移植しない。
- [Rust Arc::new_cyclic](https://doc.rust-lang.org/std/sync/struct.Arc.html#method.new_cyclic): 未完成 object の通常 access を渡さずに weak な自己参照を構築する API の参考。
- [LLVM atomic guide](https://llvm.org/docs/Atomics.html): runtime の ordering と IR への対応。
- [Windows HeapAlloc](https://learn.microsoft.com/en-us/windows/win32/api/heapapi/nf-heapapi-heapalloc): allocator の alignment と元領域の解放前提。
