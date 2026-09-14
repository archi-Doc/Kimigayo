# 値の借用・Closure・関数値・metadata

2026-09-13 改訂。本書の対象では本書を優先し、変更しない規則は [SPEC](../../SPEC.md) に従う。実装状況は [STATUS](../../STATUS.md)、採否と SPEC への同期先は [決定記録](../Decisions/2026-09-13%20Value%20Borrows%20Closures%20and%20Metadata%20Review.md) を参照する。例は仕様の説明であり、実装済みであることを示さない。

## 1. 全体像と共通規則

### 1.1 対象と内部表現

| 対象 | 決定 |
| --- | --- |
| 値の借用 `ref/V`・`uniq/V` | V の格納領域への 1 pointer。Origin・Loan は静的に保持 |
| 具体 Closure | capture を直接格納する集約値。値ごとの call pointer・header は不要 |
| 共通関数値 | 環境語と操作表 pointer の16 bytes。小環境は inline。Owned 環境・Shared 呼び出し・Non-Copy |
| 呼び出し | 判明した呼び先へ直接 call。型消去・共有に必要な箇所だけ entry 経由 |
| metadata | 値の配置・操作、object の確保情報、呼び出し context を役割別に保持 |

以下の byte offset は初期 Windows x64 の内部格納表現であり、安定した ABI や FFI ではない。レジスター、calling convention、物理引数順、返却方式は compiler が選ぶ。内部レコード・操作名は説明用であり、公開 API や source 構文を追加しない。

### 1.2 取得・破棄・領域解放

#### 1.2.1 論理上の責任

1. Copy は元の値を保持し、Move は値の責任を移す。どちらも Origin・Loan を引き継ぐ。
2. cleanup は責任の残る Initialized 部分だけを、型・スコープが定める破棄順で処理する。完全値用の破棄操作へ部分状態を渡さない。
3. **値の破棄と、格納領域の解放を分ける。** 破棄が正常完了してから領域を解放し、size だけで解放の要否を決めない。
4. 結果を先に確保し、必要な cleanup の正常完了後に返す。Abort・非終了では、後続の cleanup・解放・返却や Move の巻き戻しを保証しない。

構築途中から通常の制御転送で抜ける場合は、配置済み部分と取得済み一時値を既存の逆順 cleanup 規則で処理する。再代入は RHS の確保、対象の評価、旧値の破棄、新値の配置の順とする。

#### 1.2.2 値の転送

現行の格納可能な値の Copy/Move は、許可された取得を **size bytes の転送**で実現する。利用者のコード、count 変更、確保、pointer の辿り直し・補正は行わない。初期化先は未初期化で、非ゼロ領域は元と重ならない。

SSA 化や転送の省略を許し、memcpy の実行自体は要求しない。padding・環境語の未使用 byte を意味のある値として比較・整数化せず、それらを含む ABI 変換に無条件の noundef を付けない。

object handle の Move は、生存中の object 本体を移動しない。raw pointer は元の指し先を保つ。アドレスに依存する新しい型を追加する場合は、転送契約と schema を再設計する。

#### 1.2.3 連続範囲の再配置

同じ型の連続範囲は、検査済みの `count × stride` bytes を memmove 相当でずらす。

- 対象への排他な再配置権限を要求し、上書き先に残る別の値の責任を先に処理する。
- 重なる範囲では要素の対応に従って責任を移し、移送元に残った bit を重ねて破棄しない。
- 中間状態を利用者のコードに公開しない。size 0 でも要素数・初期化・所有責任を更新する。

[動的コレクション](2026-09-13%20Dynamic%20Collection%20Mutation%20Contracts.md)にも同じ規則を使う。移動量に比例する処理は残るが、要素ごとの間接 call は不要にできる。

### 1.3 確保と失敗

Closure の allocation 回数・配置は言語保証にしない。初期表現では具体 Closure を直接格納し、共通関数値は §4.1 の inline 条件を満たさない環境に領域を確保する。capture 自体や外側の object・collection の確保は、それぞれの契約に従う。

| 最適化の対象 | 維持すること |
| --- | --- |
| 言語上の処理 | 受理条件、取得・依存・cleanup、明示的 Abort、範囲・整数 overflow 等の必須検査 |
| 残る確保・解放 | 失敗時の Abort、元操作の診断位置、観測可能な評価順・効果 |
| 省略した確保・解放 | 省略した資源操作の失敗を再現する義務はない |

確保の省略によって capture の取得・破棄効果まで省略してはならない。残る失敗可能な操作を移動する場合も、観測可能な順序を保つ証明が必要となる。

## 2. 値の借用

### 2.1 借用先と格納表現

V は直後の完全な Referent Type。`ref/V`・`uniq/V` は V の Place を借り、内側の pointer を自動的に辿らない。必要な初期化状態・アクセス権・Origin・Loan を検証する。

```text
ref/(rc/T) → [ rc handle: pointer ] → [ header: 16 bytes | T payload ]
objref/T  ────────────────────────→ [ header: 16 bytes | T payload ]
```

値借用は V の alignment を満たす non-null の address-space-0 pointer。size/alignment/stride は 8/8/8 bytes とし、metadata・length・count・Origin・Loan ID を格納しない。Array/Slice でも値全体の格納領域を指す。

借用は referent を所有せず、借用値の Move・破棄で referent を破棄しない。`uniq/(rc/T)` は handle の slot への排他アクセスであり、T への排他アクセスではない。`ref/(ref/T from inner)` の内側と外側の依存も別に保持する。

```kimi
struct Item

let handle = Core.makeRc(Item.init())
let slot: ref/(rc/Item) = handle@ref // handle の格納領域を指す。
let view: objref/Item = handle@objref // 元の object header を指す。
```

`@ref`・`@uniq` の既存の Copy/Reborrow 規則は維持する。例えば `ref/T` への `@ref` は参照値の Copy であり、自動的に参照層を追加しない。object borrow の完全 payload 基点は object 配置の共通規則で得て、必要な view 調整に descriptor を使う（§5.3）。

### 2.2 ゼロサイズと generic

#### 2.2.1 ゼロサイズ値

ゼロサイズ値にも初期化・Move・Loan・破棄の論理状態を持たせる。アドレスが必要なら、alignment を満たす non-null の代替領域を使う。初期実装では alignment ごとの静的 backing を共有できる。size/stride は 0 のままで、backing が静的でも値の Origin や lifetime は延長しない。

異なるゼロサイズ Place は同じ pointer を持ってよい。排他性は論理 Place と Loan に対して判定する。同じ Place への競合借用は size 0 でも禁止する。0 byte の読み書きは不要だが、必要な destructor の効果は実行する。代替領域から正の `dereferenceable` や余分な byte へのアクセス権を推論しない。noalias の根拠は代替領域ではなく、§6.2.2 の借用契約とする。

#### 2.2.2 配置が未知の型

共有 generic body 内で V の配置が未知でも、借用は同じ 1 pointer とする。必要な配置・操作だけを別の GenericContext から渡す。借用を転送するだけなら V の metadata を省略できる。

```kimi
func keepBorrow<T>(value: ref/T) -> ref/T from value => value
// T の値を読まずに借用を返す。referent の配置情報は不要。
```

pointer と metadata の対応は型代入・生成計画で保証する。V の直接格納には有限配置が必要。共有 body の一時領域を実現できなければ adapter・特殊化か未対応診断を使い、unsized 型や暗黙の box で補わない。

## 3. 具体 Closure

### 3.1 capture と配置

#### 3.1.1 capture の取得

具体 Closure E は取得済み capture を直接格納する集約値。各 capture の完全な型、Binding Identity、論理順、mutability、Move Path、Origin・Loan を物理配置と別に保持する。capture は利用者の Field ではなく、E に利用者定義の `deinit`・Layout Attribute・反射 API は追加しない。

取得は明示 capture の記述順、推論 capture の解決済み初出順に一度ずつ行う。未使用の明示 capture も取得する。推論 capture の Copy 要求、外部借用、自分の owned capture への自己借用禁止は [SPEC §7.6](../../SPEC.md#76-function-expressions) に従う。環境は完成してから公開する。

#### 3.1.2 集約の共通配置

**Kimigayo struct の own Field、capture、Tuple 要素、enum の各 Case payload に同じ配置規則を適用する。** natural alignment の降順、同じ alignment では論理順に置き、末尾を最大 alignment に丸める。全サイズ計算を検査する。C layout、配列の要素位置、base の offset 0、enum tag は既定の配置を保つ。derived struct の own Field は base の予約領域の後に置き、base の padding を再利用しない。enum は Case 内だけを並べ替え、Case 間の領域共有・tag との境界は既存規則に従う。

配置は最適化レベルによらず、取得・破棄順を変えない。generic では型が固定された Field も offset が型代入に依存するため、集約全体の TypeLayout から解決する。

```kimi
let a: u8 = 1
let b: u64 = 2
let c: u8 = 3
let d: u64 = 4
let packed = func [a, b, c, d] () -> () => ()
// 取得順 a,b,c,d。物理配置 b,d,a,c。環境は 24 bytes、alignment 8。
// 記述順に配置した場合の 32 bytes から padding を削減する。
```

#### 3.1.3 環境と Function Item のサイズ

E に call pointer・object header を埋め込まず、型・選択済み実装から呼び先を解決する。通常の引数・結果・集約要素として直接格納でき、返却だけでは専用 heap 環境を要求しない。

空環境は size/alignment/stride が 0/1/0。ゼロサイズ capture だけの環境は size/stride 0、alignment は要素の最大値となる。Function Item も runtime storage は 0/1/0 とし、関数 identity・束縛済み型引数・Origin 契約は静的に保持する。空の storage から、型の同一性・純粋性・依存の欠如は推論しない。

### 3.2 呼び出しと cleanup

#### 3.2.1 receiver と取得

E は全 capture の完全な Type が Copy のときだけ Copy。取得済み環境の Copy/Move は §1.2 に従い、capture を再取得しない。

| 最小呼び出し条件 | 内部 receiver | 環境の扱い |
| --- | --- | --- |
| Shared | `ref/E` | 共有アクセス。owned capture の Move-out は不可 |
| Exclusive | `uniq/E` | 許可された変更。owned capture の Move-out は不可 |
| Consuming | `owner/E` の通常取得 | 取得した環境から Move-out できる。残存部分を cleanup |

既存の receiver 推論・アクセス条件を維持する。Consuming でも Copy の E は Copy で取得する。generic Callable は宣言された receiver を取得してから実装へ適応させ、Shared 実装でも宣言側の owner 取得を借用へ変えない。

#### 3.2.2 破棄と部分消費

環境の破棄は論理 capture 順の逆順。部分状態は CleanupPlan と CFG で管理し、分岐合流に必要なら local flag を使う。全 Closure に bitmap を埋め込まない。capture 内の Partial Move 制限は維持し、不完全な E を完全値として取得・型消去・再呼び出ししない。

Consuming の暗黙環境 receiver は明示引数より前の所有 binding とする。結果確保後、body の local/defer、引数、最後に環境の残存部分を既存の Scope Exit 順で処理する。Shared/Exclusive receiver の終了では環境を所有破棄しない。

```kimi
struct First
    deinit => Core.writeLine("first")
struct Second
    deinit => Core.writeLine("second")

func demonstrate() -> ()
    let a = First.init()
    let b = Second.init()
    let take = func [a, b] () -> First => a
    let result = take() // a を結果へ Move。残る b を破棄して "second"。
    // take は消費済み。関数終了時に result を破棄して "first"。
```

この例の capture はゼロサイズだが Non-Copy。格納 byte がなくても、消費状態と二つの破棄効果は保持する。

## 4. 共通関数値と呼び出し

### 4.1 格納と所有

#### 4.1.1 handle と環境

共通 Function Type F は **Owned 環境・Shared 呼び出し・Non-Copy** とする。環境を排他的に所有し、handle の size/alignment/stride は 16/8/16 bytes。

| offset | フィールド | 契約 |
| --- | --- | --- |
| +0 | `environmentWord: 8 bytes` | `size(E) ≤ 8` かつ `alignment(E) ≤ 8` なら E を inline 格納。それ以外は heap 環境への non-null pointer |
| +8 | `operations: ptr` | 不変 FunctionOperations への non-null pointer。alignment 8 |

方式は E の配置で決まり、tag は不要。空環境にも同じ条件を使い、size 0・alignment 16 の E は heap 側とする。inline の環境語は pointer とは限らず、未使用 byte は未規定。

heap 側は `Alloc(size(E))` で確保する。allocator は16-byte alignment と size 0 の代替 byte を保証し、alignment 引数・header・独自 prefix は追加しない。16を超える alignment は生成時に未対応診断とし、[確保上限](../../SPEC.md#2252-allocation-and-release)を適用する。

#### 4.1.2 操作表

FunctionOperations は24 bytes、alignment 8 の不変 record。

| offset | フィールド | 契約 |
| --- | --- | --- |
| +0 | `callEntry: ptr` | F の呼び出し ABI に適合する non-null entry |
| +8 | `context: ptr` | entry が必要とする不変 context。不要なら null |
| +16 | `destroyEnvironment: ptr` | 環境値の破棄と必要な heap 解放。両方不要なら null |

共有 key は E、F の呼び出し契約、選択済み実装、context schema・内容、ABI を含む。E の一致だけでは共有しない。環境 metadata は必要なときだけ context に含める。

先頭16 bytes は callee record `{entry, context}` と同じ配置とする。receiver・引数の適応は FunctionAbi に従い、配置の一致だけで異なる呼び出し契約を同一視しない。

#### 4.1.3 環境語の受け渡しと破棄

両 entry は **environmentWord の値**を受け取り、F の slot pointer は要求しない。heap 側はその pointer が E を指す。inline 側で環境のアドレスが必要なら、entry が local slot に配置する。この転送は言語上の Copy・再取得ではなく、pointer の由来と有効な byte を保つ ABI 上の処理とする。物理的な整数一語やレジスターには固定しない。

| 処理 | 環境と責任 |
| --- | --- |
| Shared call | 元の F が環境を所有したまま、変更せずに使う。inline の local slot は一時的な共有ビューであり、破棄しない |
| F の破棄 | F を消費し、後始末の責任を entry へ一度だけ渡す。inline は環境値だけ、heap は環境値の正常な破棄後に元領域も解放する |

呼び出し中は receiver Loan で元の F と環境を保護する。環境内の領域への借用を結果・保存先へ逃がさず、呼び出し間のアドレス一致も保証しない。capture 内の外部 pointer は元の指し先・権限を保つ。呼び出しごとの確保は要求しない。

heap 側は E の破棄が不要でも解放が必要なので destroyEnvironment は non-null。null の場合も F は論理的に消費し、責任を残さない。Abort・非終了は §1.2 に従う。

inline 環境への自己 pointer を保存しないため、F の Move は16 bytes の転送で済む。Moved・未初期化 storage は有効な F ではなく、無効化のための null 書き込みは不要。

### 4.2 型消去と利用

共通型へ変換できるのは、完全な環境が Owned かつ Shared-callable で、signature が適合する場合だけ。結果は hidden 環境 receiver を借用できないが、公開引数からの借用は許す。借用環境の型消去、Exclusive/Consuming の共通型、clone、callback FFI は追加しない。

変換位置に Closure 式を直接書いた場合は、新しい未初期化の F と必要な heap 領域を先に用意し、capture を論理順に最終環境へ直接取得する。capture 取得は binding への Copy/Move/Borrow/Reborrow であり user code を実行しない。静的な取得効果は同じ順で解析し、確保失敗は変換箇所で Abort とする。

一般の source 式は先に一度評価する。例えば `makeClosure()` の呼び出し・効果を確保の後へ移さない。既存 E の取得は最終環境への転送と統合できるが、source の評価順、alias、未初期化 destination、Loan、cleanup を保つ。代入先の既存 F を早期に上書きせず、通常の置換順を守る。

Function Item の束縛済み引数・選択済み実装は operations/context に保持する。同じ F の再取得は通常の Move であり、再確保・再 erasure はしない。

```kimi
func makeAdder(offset: i32) -> (i32) -> i32
    return func [offset] (value: i32) -> i32 => value + offset
    // 4-byte 環境を handle 内へ直接取得。基本方式でも heap 確保は不要。

let original = makeAdder(10)
let callback = original // F を Move。original は使用不可。
let first = callback(1) // 11。Shared call。
let second = callback(2) // 12。同じ環境を再利用。
```

### 4.3 FunctionAbi と adapter

Function Item・具体 Closure は既知の実装へ直接 call する。共通 F は callEntry、共有 generic の Callable は検証済み witness/callee entry を使う。Callable の利用だけで F へ型消去しない。

定義・call・adapter は同じ FunctionAbi 計画を使い、論理 receiver・引数・結果・context と物理位置を対応させる。同じ F の entry 群には互換な ABI を要求し、LLVM の pointer 型の一致だけで互換としない。

receiver を一度評価・取得してから明示引数を source 順に処理する。Type-qualified な unbound call の self は記述された引数順に従う。adapter は再評価、言語上の追加 Copy、overload・specialization の再選択を行わない。

```text
callEntry(environmentWord, arguments..., context) -> result
destroyEnvironment(environmentWord, context, location)
freeStorage(header, descriptor, location)
```

具体 Closure の receiver は §3.2 のまま。location は元操作の診断位置であり、source 引数ではない。共有の破棄・解放と adapter の後始末は対応する位置を埋め込むか引き継ぎ、call body・deinit 内の処理は自身の位置を使う。

引数評価中の通常転送では、取得済み一時値を caller が cleanup し、callee に入った後の責任と区別する。Unit の slot を省いても評価・Loan・cleanup を残し、Never は正常な返却経路を作らない。

## 5. metadata と generic 共有

### 5.1 型 identity

ValueMetadata の型 key は、別名等を正規化し、Origin だけを再帰的に除いた完全な値の ArgKey。外側を含む Semantics、型構造、名目 identity、束縛済み引数を残す。object の動的型 identity は実際の payload D の CoreId とする。payload の owner/D は通常の正規化で冗長な owner を除くため、その ArgKey は CoreId(D) と一致する。view や handle の所有 mode で置き換えない。詳細な正規化は [SPEC §21.2.1](../../SPEC.md#2121-type-identity-and-descriptors) に従う。

最終生成単位で、異なる正規化 key に異なる非ゼロ u64 token を決定的に割り当てる。hash 衝突は生成時に元の key で照合する。artifact に key と依存を保持し、生成単位の統合時は token と全参照を更新する。数値の外部公開・永続化・動的 link は対象外とする。

同じ型に複数の metadata record を許す。配置・code address が同じでも型 identity を統合せず、token の一致だけでは静的な型・Origin・Loan の互換性や code 共有を認めない。

### 5.2 ValueMetadata と TypeContext

#### 5.2.1 格納形式

ValueMetadata は48 bytes、alignment 8 の不変 record。本 profile の size は末尾 padding を含んで alignment に整列し、stride と等しい。導出できる stride と、共通転送規則で不要になった Copy/Move entry は保持しない。

| offset | フィールド | 内容 |
| --- | --- | --- |
| +0 | `typeKey: u64` | V の非ゼロ型 token |
| +8 | `size: u64` | 末尾 padding を含む byte size |
| +16 | `alignment: u64` | 正の2の累乗。TypeLayout と一致 |
| +24 | `flags: u64` | bit 0 が HasCopy。他の bit は0 |
| +32 | `destroyValues: ptr` | 完全な V の範囲破棄。全値について runtime 破棄が不要なら null |
| +40 | `typeContext: ptr` | 必要な配置・型依存操作への TypeContext。不要なら null |

HasCopy は言語上の Copy 能力。現行の Copy 型は利用者定義の deinit を持たず構成要素も Copy なので destroyValues は null。ただし、破棄不要の Non-Copy 型もあり、逆は成立しない。size 0 や現在の enum Case だけで型全体の破棄不要を決めない。

#### 5.2.2 範囲破棄

```text
destroyValues(first, count, metadata, location)
    // 論理添字 count-1, ..., 0 の順に、それぞれ完全な V を破棄する。
```

metadata 経由の値破棄はこの一種類で、単一値は count=1。count=0 または entry=null なら call・要素アドレス計算を省く。caller は要素数、byte 範囲・積・offset を保証し、entry を呼ぶ場合の first は non-null で必要な alignment・lifetime を満たす。size 0 でも count 回の効果を降順に実行する。

対象は完全な Initialized 値 V が stride 間隔で並ぶ範囲。余剰容量・Moved・部分状態を含めない。部分 cleanup は既定の破棄順に分解し、順序を保てる連続範囲だけまとめる。Dictionary の挿入逆順や Case/Field の破棄順をアドレス順に置き換えず、まとめられない箇所は count=1 とする。

entry は各値の deinit・構成要素の cleanup を完了してから前の値へ進み、外側の storage は解放しない。Abort・非終了は §1.2 に従う。Array<string> なら一度の間接 call から降順ループに入れるが、各要素の Free や動的 payload の dispatch は必要に応じて残る。

#### 5.2.3 型依存情報

**破棄操作は渡された metadata から必要な型依存情報へ到達できること。** TypeContext は要素 metadata、Case 情報、検証済みの操作・specialization の callee context を保持し、元の caller の context や stack に依存しない。具体 entry に情報を埋め込むこともできる。

集約の TypeLayout は、型代入後の全論理要素について offset を持つ。共有 consumer が配置を読む場合は、必要な集約の TypeContext にその対応表を生成する。Field の型だけから offset を固定しない。空集約や配置を直接解決する code のために、未使用の runtime 表を強制しない。

schema は生成側と使用側で共有し、instance の count・部分初期化状態・Origin・Loan を入れない。Never は通常の ValueMetadata を持たないが、必要な型 identity・静的制約は保持する。

### 5.3 ObjectDescriptor

object の handle・header・payload 配置は [object runtime 設計 §3.1](2026-09-13%20Weak%20References%20and%20Object%20Runtime.md#31-配置) を正本とする。そこでは全 mode の header が16 bytes、payload が header+16、allocation の alignment が16に統一されている。現行 SPEC との差分は同設計書 §6 に従う。

descriptor は24 bytes、alignment 8 の不変 record。同じ動的完全型・配置では全 mode で共有する。

| offset | フィールド | 内容 |
| --- | --- | --- |
| +0 | `payloadMetadata: ptr` | 完全な payload D の non-null ValueMetadata。typeKey は CoreId(D) に対応する token |
| +8 | `freeStorage: ptr` | 元の object allocation だけを解放する non-null entry |
| +16 | `viewMap: ptr` | 必要な Supports 関係・base/receiver 調整。不要なら null |

allocationSize は `16 + payloadMetadata.size`。必要な箇所だけで求め、具体的な D なら生成時に上限検査した定数、未知なら検査付き計算を使う。Free は size を使わない。allocationSize・payloadOffset・allocationAlignment の重複フィールドは置かず、D 自体の配置に使う payloadMetadata.alignment は残す。

完全 payload の基点には descriptor の load が不要。descriptor は identity・型依存の破棄・解放・viewMap に使い、mode ごとの count 処理は所有 handle 側で選ぶ。descriptor のアドレスを D の型 identity と同一視しない。

```text
最後の strong release（count 操作の詳細は object runtime 設計に従う）:
    strong を 1 から 0 にする
    table = 対応する side table（なければ null）
    if payloadMetadata.destroyValues != null:
        payloadMetadata.destroyValues(header + 16, 1, payloadMetadata, location)
    freeStorage(header, descriptor, location)
    if table != null:
        table の guard を解放 // object 解放後に header を読み直さない。
```

各段階は前段の正常完了後だけ実行する。count・guard の詳細は [object runtime 設計 §2.3](2026-09-13%20Weak%20References%20and%20Object%20Runtime.md#23-最後の解放) に従う。freeStorage は payload を再破棄せず、count・side table を操作しない。viewMap は検証済み関係を表し、新しい仮想 method 選択を導入しない。

### 5.4 GenericContext と取得計画

GenericContext は、共有 body・entry が必要とする ValueMetadata、検証済み witness、選択済み callee record への不変 pointer 列。slot は8 bytes、offset は `8 × index`。schema・slot 数・型引数との対応を生成時に固定し、サイズ計算を検査する。不要なら context=null。

callee record は entry とその entry 用 context を組にする。異なる schema への接続は対応 record または adapter で行い、caller の列を位置だけで流用しない。context は閉じた型代入から生成・共有し、呼び出しごとの確保や runtime の型探索を要求しない。TypeContext は型の操作を支える情報、GenericContext は呼び出す body を支える情報として使い分ける。

**metadata は、定義時に合法性を証明した取得・破棄計画だけを実行する。** 無条件の Copy には Copy の証拠が必要。Copy/Move の両方で成立する通常取得は条件付き計画として保持し、実体化した型に従って選択する。HasCopy の参照はこの計画の実行であり、未検証の source を runtime に受理する仕組みではない。

```kimi
func transfer<T>(value: T) -> T => value // Copy/Move のどちらでも合法。

func duplicate<T>(value: T) -> (T, T)
    T is Copy
    return (value, value) // 元の値を再利用するため Copy の証拠が必要。

let number = transfer(7) // Copy。
let text = "sample"
let moved = transfer(text) // string は Non-Copy。text は Move 済み。
```

共有 body の取得と cleanup は同じ静的計画から生成する。通常取得の物理転送は Copy/Move で共通であり、Copy の元値は破棄不要なので、この選択だけの runtime 分岐は省ける。ただし、元値の Initialized/Moved、再利用、Loan の違いは保持する。

**SharedReadResult は通常取得と区別する。** [SPEC §4.6.6](../../SPEC.md#466-slice-operations-and-element-results) の完全な型の Semantics に従う。owner の要素は Copy なら値、Non-Copy なら格納領域の ref を返すが、rc/arc/obj は objref、uniq/objuniq は共有 Reborrow になる。

HasCopy は owner の分岐に必要だが、一般の T の取得方法をそれだけで決めない。型代入と context/adapter の計画に、Semantics ごとの結果型・取得効果・Origin・ABI の対応を保持する。共有 body は許される全ケースについて定義時に検証する。

```text
共有要素読み取りの例（型と処理を示す疑似コード）:
    i32    の要素 → i32       // Copy
    string の要素 → ref/string
    rc/T   の要素 → objref/T  // count を増やさない
    uniq/T の要素 → ref/T     // 親の競合アクセスを停止する Reborrow
```

常に要素 slot の借用が必要なら `values[index]@ref` を使う。例えば rc/T の slot への借用は ref/(rc/T) となり、通常読み取りの objref/T と区別する。

完全な値 `rc/D` の ValueMetadata と、payload D の metadata は交換できない。metadata が足りない場合、source の overload・Contract mapping・明示 specialization を選び直して埋め合わせない。

## 6. 生成・最適化・検証

### 6.1 生成・共有・定数

TypeLayout、ValueLowering、FunctionAbi、CleanupPlan、metadata/context 計画を再利用し、構文木を複製しない。生成・cache key は、選択済み実装、配置、操作・cleanup、context schema、ABI、compiler/profile、依存内容を区別する。

必要な runtime record だけを生成し、同じ契約の record・entry を共有する。内部専用の不変 record は `private unnamed_addr constant` とし、アドレスを identity に使わない。内容を保った定数統合・entry load の畳み込みを許す。schema 変更時は生成側・使用側・cache を更新する。[LLVM の global 規則](https://releases.llvm.org/22.1.0/docs/LangRef.html#global-variables)

有限の生成 key に収まる再帰は参照で閉じ、無限の inline 配置は拒否する。型引数・context が増え続ける場合は、合法な source でも生成上限で診断され得る。不足情報を仮 pointer で補わず、record・entry を全使用・cleanup の間有効に保つ。

### 6.2 最適化

#### 6.2.1 直接化とレイアウトクラス

未使用 metadata・context を省き、転送サイズを定数化し、既知の呼び先と adapter を統合する。確保と中間転送の除去は §1.3・§4 に従う。

heap 環境の stack 配置は、全使用・破棄経路を静的に追跡できる場合だけ行う。call と破棄を対応する直接コードへ置き換え、heap 解放 entry に stack pointer を渡さない。操作表の方式別変種は作らず、証明できなければ heap 方式を使う。

型 T を転送・破棄にしか使わない body では、選択済み実装、size、alignment、必要な HasCopy、破棄の有無をレイアウトクラスの基本 key にできる。ただし、型固有の意味を消してはならない。破棄を metadata 経由で残すなら実際の T の metadata/context を渡し、破棄を直接化するなら選択した破棄 entry と埋め込む context の差も key に含める。ABI と静的な取得・cleanup 計画の適合も検証する。

同じ size で両方に destructor があっても、出力・count 操作・再帰的破棄は異なり得る。したがって破棄の有無だけでは間接 call は消えない。typeKey・witness・Field offset 等も使う場合は、それらを保持する別の共有計画が必要となる。

#### 6.2.2 借用引数の属性

**呼び出し全体の共通証明。** 通常の安全な値借用を物理 pointer で渡す場合、**ref/V・uniq/V の noalias は、呼び出し全体の借用契約から共通に証明する。** FunctionAbi はこの証明を定義・call・adapter で共有し、pointer の由来を保つ。callee 内の最後の使用だけで、呼び出し側の保護を解除しない。

receiver・引数の Loan は評価中の形成時から call の終了まで保護し、結果が依存する分はさらに延長する。引数同士と capture の依存・storage anchor を通常の重なり規則で照合する。static への直接・間接アクセス、再入、default、初期化、cleanup は [SPEC §15.6.4](../../SPEC.md#1564-calls-and-origin-propagation) の呼び出し全体の効果要約で照合する。不明な効果は競合し得る active static Loan に対して保守的に拒否する。Owned 環境でも残る static anchor や、capture 内の依存を省かない。

ref/V の inline bytes は call 中に変更されず、uniq/V には独立した経路からアクセスできない。uniq 引数に由来する子 Reborrow は許す。LLVM の noalias は関数実行中に変更される領域を制約するため、同じ値を二つの ref 引数で読むことは合法となる。

**属性への対応。**

| 属性 | 共通の適用条件 |
| --- | --- |
| nonnull・noundef | 有効な通常の ref/V・uniq/V を表す pointer |
| align(n) | 全入力で保証できる定数 alignment。未知 generic は静的に証明できる下限だけ |
| dereferenceable(n) | 入り口で実際に有効な正の byte 範囲を定数で保証。size 0 には付けない |
| readonly | ref/V。派生 pointer 経由でも inline storage へ書き込まない |
| noalias | 上記の呼び出し契約を満たす ref/V・uniq/V。個別 body の再証明は不要 |

unsafe/FFI にも同じ契約を課す。ref から得た raw pointer で referent の inline bytes に書き込まず、uniq の referent へ call 中に独立した経路でアクセスしない。必要な Loan・効果検証が未対応なら、該当 call を生成前に診断する。

属性の対象は pointer が直接指す V の格納領域。そこから読み出した handle/pointer の参照先は、その値自身の権限に従う。rc/arc の別 handle からの payload アクセスは共有のままで、count 更新は handle の inline bytes を変更しない。objref 等の header、初期化途中の内部 storage pointer、環境語全体へこの表を流用しない。readonly は関数全体の副作用なしを意味しない。

```kimi
func same(a: ref/i32, b: ref/i32) -> bool => a == b

let value: i32 = 7
let equal = same(value@ref, value@ref) // true。同じ領域の共有読み取りは合法。
```

Array/Slice の handle に付けた noalias は、load した要素 pointer の非重複を自動的に証明しない。要素ループのベクトル化等には、要素領域の由来・範囲・借用も保持して証明する。内部可変性や並行アクセスを追加する場合は、この共通証明を再検討する。[LLVM の引数属性](https://releases.llvm.org/22.1.0/docs/LangRef.html#parameter-attributes)

### 6.3 適合確認

| 分野 | 確認する例・条件 |
| --- | --- |
| 転送 | Non-Copy の一括移送、左右に重なる範囲、size 0、count×stride の上限、初期化状態と二重破棄防止 |
| 配置 | struct/capture/Tuple/Case の同一規則、base・tag・C layout の維持、型代入で変わる offset |
| Closure | 全 receiver、条件付き取得、部分消費、ゼロサイズ destructor、結果と逆順 cleanup |
| 共通関数値 | size 0/8/9、alignment 8/16、16-byte Move、環境語の値渡し、call 中の元 F・環境の保護、Shared 一時領域の破棄なし、破棄 entry での一度だけの後始末、直接構築 |
| metadata | 48/24/24-byte record、型 key と token、HasCopy と null destructor、範囲の降順破棄、部分要素・Dictionary の順序、location の伝播、object 解放後の header 参照なし |
| 共有 code | 同じ配置で異なる destructor、SharedReadResult の全 Semantics と結果の依存、schema の対応、specialization、再帰・cache |
| LLVM 属性 | 同じ領域の二つの ref、uniq の子 Reborrow と別経路の拒否、再入・static 書き込み・未知の間接効果との競合拒否、rc の別 handle、call 全体の保護、ゼロサイズ、未知 alignment、raw pointer の権限、inline 語の未使用 byte |
| 最適化 | 受理条件・必須検査・取得・出力・破棄順を O0/O2 で比較。資源失敗は各版に実在する確保・解放で検証 |

実装は共通転送、値の借用、具体 Closure、共通関数値と adapter、共有 metadata の順に進める。未対応操作は生成前に診断する。本書の改訂は実装完了を意味しない。
