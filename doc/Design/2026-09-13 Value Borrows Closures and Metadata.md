# 値の借用・Closure・関数値・metadata の仕様原案

2026-09-13。レビュー用の原案。新しい決定の採用や実装完了を意味しない。既存の規範は [SPEC](../../SPEC.md)、実装状況は [STATUS](../../STATUS.md) を参照する。以下のレコード名・操作名は compiler 内部の説明用であり、公開 API や新しい source 構文ではない。

## 1. 方針と適用範囲

値の格納、値へのアクセス、所有権、呼び出し、型 identity をそれぞれの責務に分ける。共通化するのは情報の契約であり、すべての値を object にしたり、すべての操作を動的 dispatch にしたりしない。

| 対象 | 推奨する決定 | 決定の位置づけ |
| --- | --- | --- |
| `ref/V`・`uniq/V` | V の値の格納領域を指す 1 pointer。Origin・Loan は静的に管理 | 借用先は既存の言語規則。1 pointer は初期 Windows x64 内部格納表現 |
| 具体 Closure | capture を直接格納する通常の集約値。call pointer・object header は埋め込まない | 内部格納表現。取得・Copy・呼び出し条件は既存規則を維持 |
| 共通関数値 | `{ environment, operations }` の 2 pointer。環境を排他的に所有 | 内部格納表現。Owned 環境・Shared 呼び出し・Non-Copy は既存規則 |
| 呼び出し | 分かる呼び先へ直接 call。共通関数値は操作表の call entry 経由 | 同じ FunctionAbi 契約から定義・call・adapter を生成 |
| metadata | ValueMetadata、ObjectDescriptor、FunctionOperations、GenericContext を役割別に接続 | 共通の内部契約。型 identity はアドレスと独立 |

言語上の取得順序、Copy/Move、Origin/Loan、結果の引き渡し、cleanup は最適化の有無によらず維持する。レジスター、calling convention、物理引数順、返却方式は [SPEC §21.4.2](../../SPEC.md#2142-physical-function-signatures) に従い compiler が選ぶ。以下の byte offset は初期 storage profile の原案であり、関数 ABI、FFI、永続化形式、独立 module 間の互換性を定めない。

Closure の allocation 回数・配置を言語保証に追加しない方針は、今回のレビューで確認済み。初期実装では具体 Closure 専用の heap allocation を導入せず、共通関数値の非ゼロ環境にだけ所有コンテナーの確保を使う。capture 自体が行う処理や、外側の object・collection 等の確保はそれぞれの契約に従う。必要な確保の失敗は既存の Abort 規則に従う。

## 2. 値の借用

### 2.1 指す場所と保持する情報

V は直後の完全な Referent Type とする。`ref/V` と `uniq/V` は、V の初期化済みの値を置く論理 Place への借用であり、V 内の pointer を自動的に辿った先への借用ではない。

```text
ref/(rc/T) ──→ [ rc handle: object pointer ] ──→ [ header | padding | T payload ]
objref/T   ───────────────────────────────────→ [ header | padding | T payload ]
```

| 型 | pointer が指すもの | pointer だけでは決まらないもの |
| --- | --- | --- |
| `ref/i32` | i32 の格納領域 | 借用の有効期間・Loan |
| `uniq/(rc/T)` | rc handle の格納領域 | slot の置換権限・Loan。T への排他権限は与えない |
| `ref/(ref/T from inner)` | 内側の参照値の格納領域 | inner と outer の独立した Origin |
| `ref/F`（F は共通関数型） | 2 語の関数値全体の格納領域 | 環境の所有権。借用側へ移らない |
| `objref/T`・`objuniq/T` | 元の object header | payload offset と view 調整は descriptor から得る |

既存の `@ref`・`@uniq` による Copy/Reborrow の適用を変えない。とくに、既存の `ref/T` に `@ref` を適用しても、勝手に参照層を追加しない。表の型の格納上の意味と、source の借用構文による取得規則を区別する。

Windows x64 では安全な値の借用を non-null の address-space-0 pointer で格納し、size/alignment/stride は 8/8/8 bytes とする。指す値の alignment を満たす。借用値に descriptor、length、Origin、Loan ID、reference count を追加しない。可変長データを扱う既存の Array/Slice 等への借用も、その値の格納領域を指す。新しい unsized referent や trait-object 形式を導入しない。

借用は referent の破棄・解放責任を持たない。参照値の Move や破棄では referent を破棄しない。借用した slot の Move、再配置、置換は通常の Loan 規則を満たす必要がある。pointer が同じであることから、Place・Origin・Loan の同一性を推論しない。

### 2.2 ゼロサイズ値

size/stride が 0 の V にも、初期化・Move・Loan・cleanup の論理状態を保持する。必要な pointer は V の alignment を満たす non-null の代替領域を指す。初期実装では、必要な alignment ごとに共有可能な静的 backing 領域を生成できる。これは V の lifetime を延長せず、静的 Origin を与えない。

異なるゼロサイズ Place が同じ pointer を持ってよい。排他性は論理 Place と Loan に対して判定し、異なる値の `uniq` の pointer が同じであることだけで競合とはしない。同じ Place への競合する借用は、ゼロサイズでも禁止する。ゼロ byte の操作で実メモリーを読み書きしない一方、ゼロサイズ型に必要な destructor の効果は省略しない。

代替領域の存在は、正の `dereferenceable` byte 数、pointer の一意性、一般的な `noalias`、unsafe での追加 byte のアクセス許可を与えない。LLVM 属性はそれぞれの前提を確認して設定する。言語の値 size/stride を 1 に補正しない。

### 2.3 generic で配置が未知の場合

呼び出し時点で具体 V の有限で有効な配置が確定する場合、共有 body 内で V が未知でも借用の storage は同じ 1 pointer とする。必要な V の size・alignment・操作は別の GenericContext から得る。単に参照を受け渡すだけなら、それらの metadata を渡さずに済む場合がある。

pointer と metadata の対応は静的な型代入と生成計画で保証する。pointer の内容から型を推測しない。値の借用先には object header があるとは限らず、参照から descriptor を読む一般操作は存在しない。generic aggregate が未知の V を直接保持する場合は、集約全体の TypeLayout が必要になる。共有 body が可変サイズの一時領域を実現できない段階では adapter・特殊化を使うか未対応診断を出し、借用を暗黙の box に変えない。

## 3. 具体 Closure

### 3.1 capture の配置

具体 Closure E は、解決済み capture の完全な取得後 Type を要素とする compiler 生成の集約値とする。Binding Identity、論理 capture 順、mutability、Move Path、Origin/Loan を配置と別に保持する。初期配置は論理 capture 順に natural alignment で並べ、末尾を最大 alignment に丸める。checked size 計算、padding、ゼロサイズ、再帰的 inline storage の制限は通常の集約と共通とする。

capture の取得順は [SPEC §7.6.2](../../SPEC.md#762-capture-acquisition-and-environment) の明示順／推論順に従う。配置最適化で取得順・破棄順を変えない。環境を完成するまで外部へ公開しない。capture は user Field ではなく、Closure に user `deinit`、Layout Attribute、反射 API を追加しない。

call entry は E の型と選択済み実装に結び付いた code であり、E の各値には格納しない。環境だけを引数、結果、Tuple、struct、Array 等の通常の値として直接格納できる。返却可能性は既存の Origin/Loan と Copy/Move 規則で判定する。戻り値にするだけでは heap 環境を要求しない。

capture が空なら size/alignment/stride は 0/1/0。ゼロサイズ capture のみなら通常の集約規則で size/stride 0、alignment は capture に必要な最大値となる。空であることと、ゼロサイズだが論理 capture を持つことは区別する。静的な型・Origin 引数や関数 identity は storage が空でも消さない。

### 3.2 Copy・Move・呼び出し・破棄

全 capture の完全な Type が Copy のときだけ E は Copy。空環境もこの規則で Copy となる。取得済み環境の Copy は capture の通常の Copy、Move は所有・借用依存と破棄責任の移転であり、capture の取得式を再実行しない。Move のために reference count を増減したり、専用 allocation を追加したりしない。

| 呼び出し | 環境への内部 receiver | 呼び出し後 |
| --- | --- | --- |
| Shared | `ref/E` | 同じ環境を保持。owned capture の Move-out は不可 |
| Exclusive | `uniq/E` | 同じ環境を保持。許可された変更のみ反映 |
| Consuming | `owner/E` の通常取得 | call が取得した環境の残存部分を cleanup |

最小 receiver の推論、Copy な Consuming Closure の呼び出し時 Copy、generic Callable の宣言側契約、借用結果の依存は [SPEC §7.6.3](../../SPEC.md#763-call-receiver-and-acquisition) をそのまま適用する。

環境の破棄は論理 capture 順の逆順。各 capture の残存する Initialized 部分のみ破棄し、Moved/Uninitialized 部分は飛ばす。capture 自体が部分的なら、その型の通常の部分破棄に従う。call の結果を先に確保し、body の local/defer/引数等を既存の Scope Exit 順で cleanup し、Consuming の暗黙環境 receiver は明示引数より前に導入された所有 binding として最後に cleanup する。Shared/Exclusive receiver の終了は環境の所有破棄ではない。

```text
環境の論理順: resourceA, resourceB, resourceC
Consuming body が resourceB を結果へ Move:
    結果を確保 → body の cleanup → resourceC → resourceA → 正常返却
    resourceB は結果の責任。環境から二重に破棄しない。
```

部分状態は CleanupPlan と CFG 上の初期化状態で表す。分岐の合流で必要なら local の状態 flag を生成できるが、全 Closure の storage に共通 bitmap を埋め込まない。部分的に消費した環境を完全な E として Move、Copy、共通型へ変換、再呼び出ししてよい根拠にはしない。capture 内の user `deinit` が禁止する Partial Move は引き続き禁止する。

構築の通常の中断では取得・配置済みの責任だけを既存の逆順 cleanup 規則で処理する。Abort・非終了では残りの cleanup や Move の巻き戻しを保証しない。破棄中の Abort・非終了でも後続 capture の破棄や結果返却は起きない。

## 4. 共通関数値

### 4.1 handle と環境

共通 Function Type F の有効な値は次の storage を持つ。初期 Windows x64 では size/alignment/stride は 16/8/16 bytes。

| offset | フィールド | 契約 |
| --- | --- | --- |
| +0 | `environment: ptr` | 完成した具体環境 E の先頭。non-null、E の alignment を満たす |
| +8 | `operations: ptr` | 不変 FunctionOperations。non-null、alignment 8 |

環境はこの値が排他的に所有する。rc/arc header や count、SBO 用 tag は置かない。呼び出し権限は Shared のままであり、排他的所有が Exclusive call を与えるわけではない。nullable な関数型を導入しない。Moved/Uninitialized storage の bit pattern は有効な関数値ではなく、null 書き込みによる無効化は必須にしない。

非ゼロサイズ E は初期の標準方式では `size(E)` bytes、`alignment(E)` で一度確保し、E を直接置く。allocator の上限・alignment 制限は [SPEC §22.5.2](../../SPEC.md#2252-allocation-and-release) に従う。header や独自の prefix は不要。空またはゼロサイズの E は §2.2 の backing を使い、環境用 heap allocation をしない。この場合も各関数値の論理環境・破棄責任は独立する。

FunctionOperations は同じ生成計画で共有し、値の生成ごとに allocation しない。初期形は alignment 8、size 32 bytes。

| offset | フィールド | 契約 |
| --- | --- | --- |
| +0 | `callEntry: ptr` | F の共通呼び出し ABI に適合する non-null entry |
| +8 | `destroyEnvironmentEntry: ptr` | 環境の完全破棄と、必要ならその storage 解放を行う non-null entry |
| +16 | `environmentMetadata: ptr` | E の ValueMetadata。non-null |
| +24 | `context: ptr` | 閉じた GenericContext。不要なら null |

entry の論理入力には environment と必要な operations/context を含める。物理順・渡し方は FunctionAbi に任せる。entry を同じ名前の source 関数として公開しない。F の呼び出しで許される public signature と、E の hidden receiver を adapter が接続する。

この方式は値ごとの storage を 16 bytes に保ち、呼び出し・破棄に必要な情報を共有できる。間接 call では operations を経由する load が必要になる。操作表の共有 key は E、F の呼び出し契約、選択済み実装、context、両側の ABI と破棄・確保方式を含め、signature や環境 size だけでは共有しない。

### 4.2 変換と lifetime

変換条件は [SPEC §7.6.4](../../SPEC.md#764-function-references-and-common-type-conversion) のまま。完全な環境が Owned、Shared-callable、public signature が適合し、結果が hidden 環境 receiver を借用しないことを静的に証明する。Owned は既存の OwnedOrigins 判定であり、単に pointer がないことを意味しない。public 引数からの借用結果は引き続き許される。

変換の標準手順は、source の通常取得を一度行い、必要なら環境領域を確保し、取得済み E を Move-initialize し、operations と組にして完成した F を公開する。途中の E の責任は一つの生成一時値／環境にだけ置く。outer capture を読み直さない。割り当て失敗で source の Move を取り消さない。

Function Item の変換では runtime capture は空。束縛済み型引数と、静的に選択された ordinary/specialized 実装への経路を operations/context に保持する。同じ signature であることから別の Function Item と統合しない。

F から同じ F の取得は単なる Move であり、再 allocation・再 erasure はしない。Shared call は環境を消費せず、call ごとの allocation を要求しない。呼び出し中に F が Move/破棄されないよう、環境を含む F の有効性を receiver Loan で保持する。

破棄では `destroyEnvironmentEntry` が E の完全破棄を行い、正常終了後に非ゼロ環境を元の allocator へ解放する。ゼロサイズでも必要な破棄効果は実行し、backing は解放しない。呼び出し元は同じ E に追加の destroy/free を実行しない。破棄が Abort・非終了なら、その後の free を保証しない。

小環境の inline 格納、stack 配置、allocation elimination、直接 call 化は将来の最適化とする。元の source の有効性を変えず、取得・依存・破棄・既存の失敗契約を維持することが条件。共通関数値の clone、借用環境の erasure、Exclusive/Consuming の共通型、callback FFI は追加しない。

## 5. 関数値の呼び出しと adapter

| source の呼び先 | 基本の呼び出し経路 | 環境・context |
| --- | --- | --- |
| Function Item | 選択済み関数へ直接 call | runtime 環境なし。必要な generic context は別途供給 |
| 具体 Closure E | E の body/選択済み entry へ直接 call | 最小 receiver を通常取得。generic 呼び出しでは宣言の Callable 契約に従う |
| 共通関数値 F | operations.callEntry 経由 | F の環境への Shared access と operations/context |
| 共有 generic の Callable 引数 | 検証済み witness/callee record 経由 | 宣言された receiver 契約。共通 F への変換は不要 |

静的に呼び先が分かれば、generic でも直接 call に解決できる。間接呼び出しが必要な場合は、宣言と context が定める同一の ABI を使う。LLVM の pointer 型が一致するだけでは entry の互換性を認めない。

FunctionAbi は論理 receiver・引数・結果・hidden context と物理位置の対応、所有責任の移転点、call convention、必要な属性を共有する。定義とその全 call は同じ契約を参照する。adapter は入力側 FunctionAbi と出力側 FunctionAbi の両方を参照する変換計画とし、callee の物理 signature を手書きで複製しない。共通 F の互換な entry 群では共通の call ABI を選び、具体 body が別 ABI なら adapter で接続する。

receiver を一度評価・取得してから明示引数を source 順に評価・取得する。物理引数の並べ替えや metadata 読み出しの都合で順序を変えない。adapter は引数の再評価、capture の再取得、隠れた Copy、再 overload 解決、specialization の選び直しをしない。ABI 上の移送と source の取得は別であり、adapter を挟んでも取得回数は増えない。

取得済み引数と結果の責任は [SPEC §21.4.3](../../SPEC.md#2143-slot-responsibility-and-normal-return) に従う。adapter に残る一時値も同じ規則で一度だけ cleanup する。Unit の物理 slot を省略しても効果・Loan・cleanup を省略しない。Never は正常 result slot・返却 edge を作らない。結果を先に格納しても、必要な cleanup が正常完了するまで caller に公開しない。

## 6. metadata の共通契約

### 6.1 identity と配置を分ける

型 identity の意味は [SPEC §21.2.1](../../SPEC.md#2121-type-identity-and-descriptors) の ArgKey/CoreId を維持する。ValueMetadata の型 key は完全な値の型の ArgKey であり、外側の rc/arc/ref 等も残す。ObjectDescriptor の動的型 identity は実際の payload D の CoreId であり、view や外側所有 mode に置き換えない。

初期 module 内では、検証済みの正規化 key に重複のない u64 token を割り当て、0 を予約する。token の等価性で runtime の key equality を判定する。hash は検索を速めるためだけに使い、衝突時は元の正規化 key を照合する。割り当て順は同じ入力で再現可能にするが、token の数値は source・外部 ABI・永続化に公開しない。数の上限を超える生成は診断する。

Origin を消した runtime key は、完全な静的 Type identity や Loan の代わりではない。artifact には正規化 key と依存関係を保持し、別生成単位の token をそのまま比較しない。最終生成単位で再割り当て・参照更新する。将来の動的 link は別契約が必要となる。

同じ型 key に複数の metadata record があってよい。同じ D でも obj と rc/arc で header size や解放経路が異なるため ObjectDescriptor は異なり得る。逆に、同じレイアウト・同じ code address でも別の型 key を統合しない。

### 6.2 ValueMetadata

materialize 可能な完全な値 V の配置と基本操作を共通形式で表す。初期 Windows x64 は alignment 8、size 64 bytes。size/alignment/stride は TypeLayout と同じ値で、格納時に上限・alignment・overflow を検証する。

| offset | フィールド | 内容 |
| --- | --- | --- |
| +0 | `typeKey: u64` | V の ArgKey を表す非ゼロ token |
| +8 | `size: u64` | V の byte size |
| +16 | `alignment: u64` | V に必要な alignment |
| +24 | `stride: u64` | V の stride。ゼロサイズでは 0 |
| +32 | `moveInitialize: ptr` | non-null。未初期化 dst へ src の値と責任を移す |
| +40 | `copyInitialize: ptr` | Copy が許される V だけ non-null。Non-Copy は null |
| +48 | `destroyValue: ptr` | non-null。完全な初期化済み V を破棄。外側の格納領域を free しない |
| +56 | `layoutDetail: ptr` | 必要な component 配置・metadata への情報。不要なら null |

entry の論理入力は storage pointer と当該 metadata/context。dest/src の初期化、権限、Loan、alias 条件は caller が静的に満たす。copyInitialize の non-null は runtime で Copy の可否を発見して source を受理する仕組みではない。Copy のない generic 定義から呼んではならない。Move と Copy は未初期化で独立した destination に対する初期化操作であり、任意の重複領域への memmove 契約ではない。ゼロサイズ Place は物理 pointer が同じでも論理的に独立し得る。

自明な操作は共通 no-op/転送 helper を共有できる。ただし size 0 だけを理由に destructor を no-op にしない。基本 record に任意の member table、Copy/Owned の runtime 検査、Origin、instance count、部分初期化 bitmap を置かない。Never は値として materialize しないため、この通常の ValueMetadata を作らない。型引数としての Never の identity と静的制約は別途保持する。

layoutDetail は生成時に固定された schema に従う不変 record とする。必要な logical component から byte offset・component metadata への対応を持ち、enum では active Case の判定と該当 payload 配置を表す。共有 body が使う schema と metadata の producer を同じ生成計画で確定する。object の base/view 調整は §6.3 の別の情報とする。

完全値向け destroyValue に部分状態の値を渡さない。部分構築・Partial Move の cleanup は caller の CleanupPlan が残存 component へ分解し、各 component の metadata/cleanup を使う。再代入は既存の RHS 確保→対象評価→旧値破棄→新値配置の契約から合成し、初期共通 record に moveAssign/copyAssign を必須としない。

### 6.3 ObjectDescriptor

既存の header の +0 descriptor pointer の参照先を具体化する。初期 Windows x64 は alignment 8、size 48 bytes。descriptor は生成計画で共有する不変 record。

| offset | フィールド | 内容 |
| --- | --- | --- |
| +0 | `payloadMetadata: ptr` | 実際の D の ValueMetadata。non-null。typeKey が CoreId(D) に対応 |
| +8 | `payloadOffset: u64` | 元の header から payload までの checked offset |
| +16 | `allocationSize: u64` | header と padding と payload を含む allocator 要求 size |
| +24 | `allocationAlignment: u64` | allocator に要求した alignment |
| +32 | `freeStorage: ptr` | non-null。元の object allocation だけを解放する entry |
| +40 | `viewMap: ptr` | 必要な Supports 関係・base/receiver 調整の情報。不要なら null |

payloadOffset は既決定の `alignUp(headerSize, alignment(D))`。allocationSize はその offset と size(D) の checked 和、allocationAlignment は header と D の必要 alignment の最大値とする。allocator が内部で要求を丸める場合の実確保量と混同しない。

共通手順は `header → descriptor → payloadMetadata`。objref/objuniq はこの経路で offset を得るため、借用型から失われた所有 mode を推測する必要がない。viewMap は検証済みの名目関係と receiver 調整を保持する。静的な要求に応じた情報のみを生成し、runtime member selection や仮想 method table を新設しない。

`payloadMetadata.destroyValue(header + payloadOffset)` は D 全体を破棄し、base view の部分だけを破棄しない。object の storage はその後に freeStorage で解放する。rc/arc の最後の strong release は既決定の順序、すなわち strong=0→D の破棄→object 解放→side-table guard 解放に従う。descriptor の freeStorage は strong/weak を操作せず、D を再破棄せず、side table も解放しない。

instance count、side table、Building 状態は既存の header/runtime 契約に残す。descriptor は allocation mode・layout・free 経路に応じて分けられるが、CoreId はそれと独立に共有する。全情報は public view ではなく実際の allocation と一致させる。

### 6.4 GenericContext と必要情報の伝達

共有 body が必要とする型配置、Contract mapping、Callable entry、静的に選択された specialization への経路を渡す。元の型引数の完全な静的情報は compiler/artifact に保持し、runtime context に Origin/Loan を追加しない。

初期 GenericContext は、生成計画が schema を持つ不変の pointer 列とする。各 slot は 8-byte alignment、offset は `8 × slotIndex`、全体 size は checked な slot 数×8。slot の種別は ValueMetadata、検証済み witness record、selected-callee record のいずれかとし、producer と consumer が同じ schema を使う。0 slot なら record 不要で context=null。runtime の型探索用 dictionary や、呼び出しごとの可変 record 構築を要求しない。

witness/callee record は、必要な entry とその context、receiver 調整を同じ契約で結び付ける。signature/schema は生成時に確定し、runtime に生 pointer の型を推測しない。source の overload・Contract mapping・明示 specialization を再選択しない。型 D の object descriptor を、完全な値 `rc/D` の ValueMetadata の代わりに渡さない。

閉じた型代入に必要な context は生成時に構築・共有する。共有 callee が異なる schema を必要とする場合は、選択済み callee record にその callee 用 context を持たせるか、検証済み adapter で接続する。caller の列を位置が似ているという理由で流用しない。既存の有限生成・resource limit を適用する。

同じ情報を埋め込んだ具体 entry では context や個々の slot を省略できる。公開した共通 record の途中のフィールドを consumer に知らせず除去してはならない。schema を変えるなら全 producer/consumer と生成 key を更新する。metadata を別途渡すことは、値ごとの fat pointer や heap boxing を意味しない。

## 7. 生成・検証と導入順

TypeLayout、ValueLowering、FunctionAbi、CleanupPlan に加え、metadata/context の生成計画を immutable record として共有する。構文木を metadata 用に複製しない。cache key には compiler/profile と配置方式、metadata/context schema、選択済み実装、必要な ABI 契約と依存内容を含める。型 key だけで code や descriptor を再利用しない。

runtime record は object・間接 entry・共有 body 等から必要なものだけを生成する。具体 code 内で配置・操作を直接解決できる型に、未使用の 64-byte record を一律に出力しない。record を出力する場合はその schema の必須部分を満たす。参照先の型 metadata をすべて再帰的に埋め込む必要はなく、pointer を介する合法な再帰型は生成計画の既存 key を参照して閉じる。有限生成できない場合は既存の診断を使う。公開した metadata と entry は、それを使う値・call・cleanup の全期間にわたって有効とし、初期 profile では不変 module 領域に置く。

| 順序 | 実装する境界 | 主な確認 |
| --- | --- | --- |
| 1 | 値の借用とゼロサイズ Place | nested Semantics、handle slot と header の区別、alignment、Loan、未知 generic の別 metadata |
| 2 | 具体 Closure の直接格納と call | 空・非空・borrow capture、Copy/Move、全 receiver、部分消費、結果と cleanup の順 |
| 3 | 共通関数値と adapters | 2 語 storage、非ゼロ環境の確保と一度だけの free、ゼロサイズ destructor、Shared call、引数の途中 transfer |
| 4 | metadata と共有 generic の全経路 | 完全値/部分値の destroy、型 identity、mode ごとの descriptor、context の対応、静的 specialization |

先行段階もこの metadata 契約を参照し、必要な部分だけ具体 entry で解決できる。最後の段階まで fake pointer や不足情報を生成して通さない。実装しない機能は現在と同じく generation 前の診断とする。

追加する適合確認では、同じ CoreId に異なる object descriptor、同じ size に異なる ownership 操作、同じ code に異なる Function Item、同じ pointer に異なるゼロサイズ Place を用意する。runtime token が等しいことから Origin の異なる値を代入できないことも確認する。O0/O2 で取得回数、出力、破棄順、Abort、source の受理/拒否が一致することを検証する。

これは文書原案であり、本変更で compiler の対応範囲は増えない。NativeAOT 試験の実行を要求しない。

## 8. 採用時の文書反映

| 文書 | 反映内容 |
| --- | --- |
| SPEC §21.1 | 値の借用、具体 Closure、共通関数値の初期 storage profile |
| SPEC §7.6 / §16.3 | capture の逆順破棄と Consuming 暗黙 receiver の cleanup 位置を明文化。既存の取得・呼び出し制限を維持 |
| SPEC §21.2 / §21.3 | metadata record と context の内部契約。identity と descriptor の区別 |
| SPEC §21.4 / Appendix B.4 | entry/adapter の契約と原案から採用済み設計への参照更新 |
| Startup 設計 §15.4–15.5 / §19.1 | Closure・共有 metadata に関する残課題を採用内容へ同期 |
| STATUS | 仕様採用と実装進捗を分けて記録 |

Startup 設計 §15.3 の rc/arc 未決定記述は、この原案の採否と無関係に、すでに採用済みの [SPEC §21.2.3](../../SPEC.md#2123-windows-x64-object-and-weak-profile) へ同期する。
