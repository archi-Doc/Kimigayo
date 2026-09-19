# 共有所有・ガード・using — 仕様変更案（最終版）

- 作成日: 2026-09-19
- 状態: 現行SPECへの統合前の最終提案。実装済み機能や検証済みABIを示す文書ではない。
- 対象: local/sync、ガード、using、スレッド能力、Weak、オブジェクト生成・移譲。

本書を採用する場合、明示した変更以外は [SPEC.md](../../SPEC.md) と参照先に従う。「現行§」は既存SPEC、「§」は本書を指す。言語上の要件、実装目標、将来機能をそれぞれ§1–6、§7、§9に分ける。例は提案構文を含み、現行コンパイラでの実行を保証しない。

コード片は独立した例であり、連結しない。ItemとinspectNameは§8.1の定義を共用し、他の前提は各例に示す。

## 1. 型とSemantics

### 1.1. 役割と対象

所有方式、payloadへのアクセス権、アクセス期間を分ける。

| 役割 | 型・構文 | 意味 |
| --- | --- | --- |
| 単独所有 | `obj/T` | 一つの所有責任を持つ |
| 読み取り共有所有 | `rc/T`・`arc/T` | 非atomic／atomicな参照カウント。共有payloadアクセス |
| ガード付き共有所有 | `local/T` | 非atomicな参照カウントと実行時借用検査 |
| ガード付き共有所有 | `sync/T` | atomicな参照カウントと非再入Mutex |
| オブジェクト借用 | `objref/T`・`objuniq/T` | Tのビューへの共有／排他アクセス。所有しない |
| 取得期間の管理 | `ReadGuard<T>`・`WriteGuard<T>`・`LockGuard<T>` | 借用・ロックの解除責任を持つ通常の所有値 |
| スコープ管理 | `using name = expression Body` | 管理対象を保持し、退出時に破棄する |

local/syncは組み込みSemanticsとし、ライブラリ型の別名にはしない。`<s/T>` の分解とSemantics制約を使用できる。Tは既存の有効なオブジェクト対象であり、syncには§5.2の追加条件を課す。runtime Contract Viewなどの未導入機能は有効化しない。

`ref/local/T` はハンドルの格納場所を借り、`objref/T` はpayloadを借りる。完全な値を借りる `ref/T`・`uniq/T` と、派生型を含み得るオブジェクト借用も区別する。

### 1.2. 文脈キーワードとカテゴリ

local/sync/asyncは型のSemantics接頭辞、Semantics制約、明示変換先でのみ認識する。`let local = 1`・`func sync() => ()`・`x.async`・通常の除算 `local / count` は名前の通常規則に従う。asyncはそのSemantics文脈だけで将来用に予約し、使用時には未対応の診断を出す。

| カテゴリ | 構成 |
| --- | --- |
| `value` | owner |
| `valueborrow` | ref, uniq |
| `object` | obj, rc, arc |
| `guarded` | local, sync |
| `counted` | rc, arc, local, sync |
| `objectborrow` | objref, objuniq |
| `borrow` | ref, uniq, objref, objuniq |
| `owning` | owner, obj, rc, arc, local, sync |
| `reference` | ref, uniq, obj, rc, arc, local, sync, objref, objuniq, unsafe |

新しいguarded/countedはSemantics制約だけの文脈キーワードとする。objectは既存の直接共有借用を維持し、guardedはガードを必要とする。countedはstrong clone・Weakの対象を表し、共有借用を意味しない。Weak自身の外側Semanticsはownerであり、countedではない。

現行§3.3のcounted不導入方針を変更する。objectの構成は維持するが、owning/referenceを使うジェネリック本体はlocal/syncを含む全許容型で検証する。カテゴリ所属だけでは排他権限や共通のガード取得APIを与えない。asyncはいずれの有効な集合にも含めない。

### 1.3. 所有権と複製

obj/rc/arc/local/syncのハンドルと全ガードはNon-Copyとする。通常の取得はMoveであり、カウントを増やさない。countedの複製は `Kimi.Intrinsics.clone(handle@ref)` を使う。objとガードのcloneは提供しない。Weakの複製は§6に従う。

ハンドルの `.clone()`・`.share()` は登録せず、rc/arcのpayloadメンバー解決を変えない。将来の `@ref` 省略は、clone固有の特例ではなく、現行では未導入のハンドルスロットへの暗黙引数借用として設計する（現行§10.2・§10.9）。

## 2. 生成・移譲・ビュー変更

### 2.1. 操作の選択

Mはobj/rc/arc/local/sync、Cはcountedに属する所有方式、T/VはView Targetとする。入力型と変換先から次の操作を選び、失敗時に別の行へ解釈し直さない。

| 入力 | 変換先 | 操作 |
| --- | --- | --- |
| 完全なowner/T（有効なpayload Core） | `@M` / `@M/T` | 通常のCopy／Moveで一度取得し、新しいオブジェクトを生成 |
| `obj/T` | `@C` / `@C/T` | 同じ実体の所有方式を移譲 |
| `M/T` | `@M` / `@M/T` | 同一ハンドルをMove。生成・cloneはしない |
| `M/T` | `@M/V`（Tと異なるV） | Supportsを証明し、同じ所有方式でビュー変更 |
| 異なるcounted方式間、countedからobj | 別の所有方式 | 禁止。参照数が1でも例外なし |
| 借用値 | 所有方式の生成・移譲 | 禁止 |

生成と移譲の対象は入力と同じTとする。所有方式変更とビュー変更を一つの変換に合成せず、必要なら二段階で明記する。`@M` のTは入力から決める。型・アクセス・Origin・Loan・必要なOwned・sync形成条件は各操作でも満たさなければならない。

ジェネリック本体は制約が許す全入力について操作、結果型、取得権限を証明する。未知のUをowner Coreと仮定せず、特定の具体化や最適化結果だけで成立する操作を許可しない。

### 2.2. 新規生成と既存objの移譲

新規生成は取得した完全値を新しいpayloadへMoveし、コンストラクタ・アクセサ・deinitを再実行しない。`value@local` は所有権の意味では `(value@obj)@local` と同じだが、中間オブジェクトや二重確保を要求しない。Copy値の生成元は使用可能なままである。

既存objからの移譲はハンドルをMoveし、動的型・オブジェクト同一性・payload位置・外部依存を保つ。payloadを複製・再配置・再構築せず、移譲先の公開前に管理状態を初期化する。競合するLoanがあればMoveを拒否する。ハンドルのMoveをpayloadのMoveとみなしてはならない。

既存のmakeObj/makeRc/makeArcは、引き続き完全なowner Coreから新規生成するintrinsicとする。objを受け取る移譲APIへ暗黙に拡張せず、移譲には上表の `@` を使う。

```kimi
// Itemは§8.1で定義する。
var original = Item.init("initial")@obj
var first = original@local // originalはMoved。実体とpayload位置は変わらない。
var second = Kimi.Intrinsics.clone(first@ref) // 同じ実体を共有する。
var independent = Item.init("other")@local   // 新しい実体を作る。
```

### 2.3. ビューと完全payload

local/syncにも既存のSupports、型検査、チェック付きキャストの規則を適用する。所有ビュー変更はMoveし、参照数を増やさない。借用ビュー変更はCopy／Reborrowとし、元のLoanを保つ。変更後も同じ実体・借用状態・ロックを使い、型消去時のOwnedと外部依存を維持する。syncの変換先にも§5.2を適用する。

```kimi
// DogはAnimalの派生型。必要なOwned等の条件を満たすものとする。
var dog = Dog.init()@local
var animal = dog@local/Animal // Dogの実体をAnimalとして見る。dogはMoved。
```

ガードの排他性は「実体が完全な静的型Tである」という証明にはならない。`objuniq/Animal` を通常の `uniq/Animal` へ暗黙変換しない。オブジェクト・基底部分へのメソッド呼び出しは現行のObjectCallCompatibleに従う。

View Targetが同じ完全なSealed型Tと証明できる場合だけ、現行§13.5.5.1の明示投影 `@ref/T`・`@uniq/T` を許す。投影結果もガード依存を保ち、合法なpayload置換からガードや管理領域を置換する権限は生じない。local/syncの対象を一律にSealedへ限定しない。

## 3. ガード付きアクセス

### 3.1. 実行時の取得と失敗

localは単一スレッド内でカウントと実行時借用状態を管理する。スタック配置やスレッドローカル変数を意味しない。syncはatomicカウントと非再入Mutexで管理する。

| 方式・状態 | read / tryRead | write / tryWrite | lock / tryLock |
| --- | --- | --- | --- |
| local・借用なし | 取得可能 | 取得可能 | 対象外 |
| local・共有借用中 | 追加取得可能 | 競合 | 対象外 |
| local・排他借用中 | 競合 | 競合 | 対象外 |
| sync・未ロック | 対象外 | 対象外 | 取得可能 |
| sync・ロック中 | 対象外 | 対象外 | 競合 |

| 状況 | 通常API | try系API |
| --- | --- | --- |
| localの競合 | Abort。待機しない | None |
| syncの競合 | スレッドを待機 | None。待機しない |
| 共有借用数のoverflow | 状態変更前にAbort | 同左。Noneにはしない |

成功時だけ、一つのガードに取得状態の解除責任を渡す。失敗時にカウント変更や未取得の解除責任を残さない。strong/weak数も増加前にoverflowを検査する。rc/arcの既存上限は維持し、新方式と借用数の上限はプロファイルで定める。折り返し・飽和による成功扱いは禁止する。

syncは読み取りにもlockを必要とする。同一実体の再ロックやロック順の逆転はデッドロックになり得る。再入、検出、FIFOの公平性、待機時間上限は保証しない。unlockはrelease、成功したlock/tryLockはacquireとして同期し、失敗したtryLockには取得による同期を保証しない。

Abortはプロセス全体を終了し、通常の巻き戻しや再開をしないため、中毒化（poisoning）は導入しない（現行§17.3）。

### 3.2. 標準取得APIと名前解決

取得関数はKimi.Intrinsicsに属し、引数ラベルはvalue、sourceは関数のOriginパラメータとする。Tの対象適格性とsyncの形成条件は結果のガード型にも適用する。

| 関数／ドット表記 | 入力 | 結果 | ガード破棄時 |
| --- | --- | --- | --- |
| `read<T>` / `x.read()` | `ref/local/T from source` | `ReadGuard<T> from source` | 共有借用を解除 |
| `write<T>` / `x.write()` | 同上 | `WriteGuard<T> from source` | 排他借用を解除 |
| `lock<T>` / `x.lock()` | `ref/sync/T from source` | `LockGuard<T> from source` | unlock |
| `tryRead<T>` / `x.tryRead()` | readと同じ | `Option<ReadGuard<T> from source>` | Someのガードに同じ責任 |
| `tryWrite<T>` / `x.tryWrite()` | writeと同じ | `Option<WriteGuard<T> from source>` | 同上 |
| `tryLock<T>` / `x.tryLock()` | lockと同じ | `Option<LockGuard<T> from source>` | 同上 |

ドット登録はこの6操作だけとする。ハンドルのPlaceまたはそのスロットへのref/uniqから、通常の共有Borrow／Reborrowで入力を作る。不変束縛や§3.5の要素取得結果からも呼べる。取得はハンドルをMoveしない。

解決には標準宣言の識別情報を使い、同名のユーザー関数やContractへ権限を与えない。適用できなくてもpayloadの同名メンバーへ解釈し直さない。local/syncのpayloadアクセスは必ずガードを経由し、メンバー転送・暗黙receiver適応・キャスト・型絞り込みで迂回できない。メタデータだけの型検査やビュー変更は、新しいアクセス権を与えない。

### 3.3. ガード型とアクセサ

KimiのReadGuard/WriteGuard/LockGuardは、型パラメータTとOriginパラメータsourceを持つ、コンパイラ管理のNon-Copyなstruct Coreとする。`ReadGuard<T> origin source` は宣言、`ReadGuard<T> from source` は型使用時の引数指定であり、表記を統一しない。

公開コンストラクタ、管理フィールドの変更、独自deinitの追加、clone、unlock/releaseは提供しない。取得関数だけが取得済みの状態を構築し、通常の破棄で一度だけ解除する。

readValueは全ガードで共有読み取り、valueは各ガードの最大アクセス権を返す。どちらもsetを持たず、View Targetを変えない。固定のget型を使い、receiverの権限で同名getを多重定義しない。

| ガード | アクセサ | receiver | 結果 |
| --- | --- | --- | --- |
| ReadGuard | value・readValue | `ref/(ReadGuard<T> from source) from guard` | `objref/T from guard` |
| WriteGuard | readValue | `ref/(WriteGuard<T> from source) from guard` | `objref/T from guard` |
| LockGuard | readValue | `ref/(LockGuard<T> from source) from guard` | `objref/T from guard` |
| WriteGuard | value | `uniq/(WriteGuard<T> from source) from guard` | `objuniq/T from guard` |
| LockGuard | value | `uniq/(LockGuard<T> from source) from guard` | `objuniq/T from guard` |

guardはアクセサ呼び出しでガード自身を借りるOriginとする。両アクセサは管理状態を更新せず、payloadへの子借用を返す。共有Loanの生存中に競合する排他Loanは作れず、同時に二つの排他Loanも作れない。子Loan終了後の再借用は許すが、それだけでガードは解除しない。

### 3.4. 寿命と破棄

```text
取得元ハンドルの有効性
  └─ ガードの有効性
       └─ value/readValueの参照
            └─ フィールド参照・Reborrow・捕捉等
```

ガードは取得元スロットのLoanを保持し、strongを追加しない。sourceはguard以上の期間有効でなければならない。payloadの外部依存と実際のLoanの由来も、Copy、Move、ビュー変更、キャスト、返却、格納、捕捉を通じて保持する。別のstrongが実体を生かしていても、解除済みガードの参照は使えない。

ガードはusing外でも通常のMove・格納・返却が可能だが、型・Origin・Loan・スレッド条件を満たす必要がある。取得元が一時値なら通常の一時値規則を適用し、usingによる無条件の寿命延長は行わない。

解除は所有責任を持つガードの通常の破棄時点で行う。最終使用だけを理由に観測可能な解除を早めない。Abortや非終了時の後始末は§4.3に従う。

### 3.5. 配列・Slice等の要素取得

共通規則SharedReadResultに次を追加する。他の要素型は変えない。

| 完全な要素型 | SharedReadResult |
| --- | --- |
| `local/T` | `ref/local/T from source` |
| `sync/T` | `ref/sync/T from source` |

所有ハンドルでは、objectは既存の `objref/T`、guardedはスロット借用となる。guardedの要素取得はpayload借用・カウント増加・ガード取得をせず、格納場所と要素の依存を保つ。未知の型を扱う本体は、この結果も全許容型の検証に含める。

`@ref`・`tryGet()` 等、既にスロットを借りる操作はSharedReadResultに置き換えない。

```kimi
// items: Slice<local/Item>。Itemは§8.1を参照。
let handle = items[0] // ref/local/Item
using guard = handle.read()
    inspectName(guard.readValue.name@ref)
```

## 4. usingとスコープ管理

### 4.1. 文法と束縛

```text
UsingHeader := using Name [ : Type ] = Expression
UsingStatement := [ Label : ] UsingHeader { 同じ基準インデントの次のUsingHeader } Body
```

usingは値を返さない文であり、var/letを付けない。本文なしで外側スコープ終了まで管理する宣言形式や、カンマ区切りの複数束縛は導入しない。Bodyは現行§14.2の `=>` 単一項目またはインデント形式とする。

本文を持つ式を初期化式に使う場合は括弧で囲む。`=>` の配置、単一項目本体の領域、式の改行継続は現行§2.2に従い、ヘッダ列の連結だけを§4.2で追加する。

文頭または同じ物理行の `Label:` 直後で、usingに続くトークンがName、その次が `=` または `:` ならヘッダと認識する。Nameは通常の有効な名前で、文脈キーワードを含む。先読みは同じ物理行内でコメントを除いて行う。認識後の不正な型・初期化式・本体は診断し、名前へ解釈し直さない。`using = 1`・`using(x)`・`x.using()` は通常の名前の使用とする。

初期化式を一度評価し、通常の取得・型推論・Origin推論で束縛する。既存のガードをMoveして受け取れる。束縛は自身の初期化後から後続ヘッダと本体内で有効になり、本体終了でスコープを終える。名前の衝突・隠蔽はローカル束縛の規則に従う。

using束縛は中身への合法な変更・借用を許し、初期化直後から§4.4の保護を受ける独自の束縛種別とする。読み取りガードでも同じ構文を使う。

### 4.2. 複数取得とラベル

同じ基準インデントで連続するヘッダと末尾の共通Bodyを一つの文とする。空行・コメントだけの行は連結を切らず、本体にもならない。最後のヘッダにBodyがなければエラーとし、別の文・ラベル付きヘッダ・EOFで本体なしの列を終えない。本文が完結した後のusingは別の文となる。

先頭ヘッダだけにラベルを付けられ、ヘッダ列全体と本体を指す。ラベルは本体内だけで有効とし、自身の初期化式からは退出できない。ラベル名の衝突・関数/defer境界は現行規則に従う。本文内でインデントを増やしたusingは独立した入れ子となる。

1. ヘッダを上から初期化する。束縛名の重複を禁止し、型は個別に推論する。
2. 後続初期化式は、初期化済みの先行束縛を保護・借用規則の下で参照できる。
3. 全取得後に本体を実行し、本体の後始末後に管理対象を逆順で破棄する。

複数ロックの取得は逐次処理であり、一括取得・原子的取得・自動rollbackではない。空行、コメント、インデントと継続行は現行§2.2–2.3に従う。

```kimi
// firstとsecondは、異なる実体を指すsync/Itemハンドル。
work: using left = first.lock()
// コメントや空行は連結を切らない。

using right = second.lock()
    left.value.name = "left"
    right.value.name = "right"
    exit to work
// right、leftの順に解除済み。
```

### 4.3. 制御移動と後始末

裸のexitはusingを素通りし、現行どおり最も内側の反復構文またはdeferを対象とする。対象がなければエラーである。using自身への早期退出は `exit to Label` を使い、値は省略またはUnitだけとする。

usingはcontinue/yield/returnの対象にも検索障壁にもならない。内側のループだけを終了するexitは外側のusingを終了しない。関数・deferを越える退出も許可しない。

正常終了や有効なreturn/exit/continue/yieldでusingを離れるときは、次の順序とする。

1. 退出先と必要な結果を確保し、結果の借用が後始末を越えて有効か検証する。
2. 本体のローカル・defer・一時値を既存のScope Exit規則で処理する。
3. 管理対象を宣言の逆順で破棄し、退出先へ進む。

初期化途中の正常な制御移動でも、初期化済みの管理対象だけを逆順で破棄する。初期化式の一時値・未完成値は現行の構築・退出規則に従う。try系のNoneは普通の値であり、usingはOptionを自動展開しない。

Abortでは通常の破棄を保証しない。処理や破棄が非終了なら後続の解除も保証しない。単なる最終使用による早期破棄は行わない。

### 4.4. 管理対象の保護

#### 4.4.1. 許可・禁止する操作

| 操作 | 判定 |
| --- | --- |
| 読み取り、合法な借用、完全性を保つ部分更新 | 通常の権限・Loan・アクセス条件の下で許可 |
| 束縛自身のMove、所有引数・return・格納・所有捕捉への持ち出し | 禁止 |
| 束縛全体の代入・replace・再構築・swap・exchange・手動破棄 | 禁止 |
| 対象を不完全にするPartial Move | 禁止 |
| 対象を取り出せる包含値への破壊的操作 | 禁止 |
| 対象に依存する参照の寿命超過 | 禁止 |

通常のCopyをMoveとみなして禁止しない。完全値・Sealed・具体化済みの型であっても全体保護は解除しない。一方、ガードとは別領域のpayloadには§2.3の合法な投影・更新を許す。ガードの非公開管理状態を直接変更する権限は与えない。

汎用の所有値にも適用できる。例えばFileのcloseが状態だけを変更するなら型の契約に従うが、束縛全体を置換・取り出す実装なら拒否する。usingは「同じ所有値を完全な状態で保持し、退出時に破棄する」ことを保証し、任意の資源が常に開いていることまでは保証しない。

#### 4.4.2. 呼び出し先と別名

現行§12.4.4.2の入力別効果要約、格納領域関係、Origin/Loan追跡にusingの保護対象を追加する。専用のRootPreserving契約、別の効果解析、実行時保護フラグは作らない。

receiver・引数・捕捉・静的な参照元・戻り値を実際の対象へ対応付け、関数・アクセサ・デフォルト引数・cleanupを経ても表の禁止を保持する。通常のuniqを返す別名も由来を失わない。分離を証明できる操作は対象への違反とせず、MayAliasだけで証明済みの無害な読み取りを拒否しない。対象を壊し得る未知の呼び出し・unsafe効果は拒否する。

ObjectCallCompatibleと同じ要約を使うが、そのProvenという一つの結果だけでは許可しない。using対象には「完全なSealed値なら全体更新できる」という例外を適用できないためである。直接操作だけの構文検査では、別名やジェネリック呼び出し経由の保持を保証できない。

再帰・特殊化集合・全許容型の検証は既存の固定点規則に従う。別コンパイルは公開要約、Contract・関数値はEffect契約を使い、呼び出し側でprivate本体を再解析しない。現行§18.3の保存・依存・再検証を利用し、必要な情報を欠く成果物を安全とみなさない。

## 5. スレッド能力

### 5.1. 能力と型別の規則

Kimi.ThreadTransferable（TT）は値の所有権を別スレッドへ移譲できる能力、Kimi.ThreadShareable（TS）は共有参照を複数スレッドから利用できる能力とする。両者は独立し、Copy・同時排他アクセス・寿命延長を意味しない。Ownedもその代わりにはならない。

ViewTT(T)/ViewTS(T)は「Tのビューで扱える全実体が該当能力を持つ」という仕様上の判定名であり、ソース言語の新しい型・Contractではない。

| 型 | TTの条件 | TSの条件 |
| --- | --- | --- |
| 純粋な組み込みScalar・Unit・不変文字列 | 成立 | 成立 |
| 通常の完全な複合値 | 保持する値・基底・捕捉等がTT | 保持する値・基底・捕捉等がTS |
| `ref/T` | TがTS | TがTS |
| `uniq/T` | TがTT | TがTS。共有アクセスから排他権限を取り出せないこと |
| `obj/T` | ViewTT(T) | ViewTS(T) |
| `objref/T` | ViewTS(T) | ViewTS(T) |
| `objuniq/T` | ViewTT(T) | ViewTS(T) |
| `rc/T`・`local/T` | 不成立 | 不成立 |
| `arc/T` | ViewTT(T)とViewTS(T) | 同左 |
| `sync/T` | 有効に形成できれば成立 | 同左。payloadのTSは不要 |
| `Weak<S>` | SがTT | SがTS |
| ReadGuard・WriteGuard | 不成立 | 不成立 |
| `LockGuard<T>` | 不成立 | ViewTS(T) |

通常型は構造と公開契約から、未知の型は制約から証明する。破棄・静的状態・外部資源のスレッド制約も検証し、格納型だけで安全性を認めない。生ポインタ、型消去した環境、外部資源には固有の公開契約が必要である。

LockGuardは取得したスレッドで破棄する。共有参照からはreadValueだけを使え、valueの排他権限を取得できない。OSプリミティブの都合で公開能力を変えない。同名のユーザーContractや空の適合宣言は能力の証明にならず、証明不能なら必要な使用を拒否する。

### 5.2. ビュー保証・継承・sync形成

Sealedな完全型のViewTT/ViewTSは自身のTT/TS証明と一致する。openな完全値の能力だけでは派生型を保証できず、ビューには明示した公開保証を必要とする。

組み込み能力への `Self is ThreadTransferable` / `Self is ThreadShareable` は公開ビュー保証の宣言とする。型自身を検証し、open型なら各派生型にも追加フィールド等を含む同じ義務を課す。保証は取り消せず、派生側で宣言を繰り返す必要はない。

```kimi
public open struct Animal
    Self is ThreadTransferable
    public var age: i32

    public init(age: i32)
        self.age = age
```

宣言自体を証明根拠にはしない。型制約やwhenを使う条件付き保証は、条件と型引数置換を派生側にも保持し、全許容型で検証する。この規則は二つの組み込み能力に限定し、一般ContractやCopyの継承規則を変えない。構造から自動導出したopen型の能力には公開ビュー保証を付けない。

`sync/T` の形成には、有効なView TargetとしてのT、TT(T)、ViewTT(T)を必要とする。Sealedなら自身の証明、openなら検証済みの公開保証で満たす。単なる `T is ThreadTransferable` はCore適格性やopenビューの保証にはならない。

```kimi
func acceptSync<T>(source: ref/sync/T)
    T is Sealed and ThreadTransferable
    ()
```

syncのビュー変更先も形成条件を満たす必要がある。obj/arc等では形成条件を満たす範囲で任意のTT/TSを失うビュー変更を許し、その後は変更後の型から再判定する。参照数・現在の動的型・最適化による推測で能力を強めない。型絞り込みやキャスト先で新しく証明できる場合だけ、その保証を使用する。

公開保証・条件・検証依存は成果物へ保存し、基底・格納型等の変更時に再検証する。

### 5.3. 再帰型の証明

TT/TSの組み込み構造解析は、正の依存関係の最大固定点で判定する。例えば `Node -> Weak<sync/Node> -> sync/Nodeの形成 -> NodeのTT` は有限の再帰グループになる。

型の骨格と依存関係を先に作り、グループ外の前提を証明でき、内部に違反がなければまとめて認める。rc/local等の不成立や未証明の外部資源条件を伝播し、一般Contractの適合循環や自己正当化する宣言を取り込まない。公開ビュー保証も宣言の存在だけでなく実際の構造へ展開する。

型形成と能力証明は一緒に確定し、途中の仮定を公開しない。無限のインライン配置や、型引数が無限に増える展開は有限の構造循環と区別して既存規則で拒否する。確定した証明は共有・再利用する。

### 5.4. 寿命と実行機能

TT/TSが成立しても、借用先・ガード・外部依存の寿命は必要である。local由来の `objref/T` もViewTS(T)と使用終了までのLoanを証明できれば転送可能とする。T自身のTSだけではopenなビューの証明にならない。

localのガード、カウント、借用状態の操作は元のスレッドに残す。別スレッドではpayload参照だけを使い、参照のCopy・破棄でlocal管理状態を操作しない。使用中の解除・破棄・競合する排他アクセスは禁止する。

本書はスレッド生成・join・スケジューラを導入しない。実際に利用できる場面は、スコープ付きスレッドやjoin等で使用終了を証明する実行機能の設計に依存する。

## 6. 参照カウントとWeak

### 6.1. 操作と依存

Sは外側Semanticsがcountedである有効な完全ハンドル型とする。以下はKimi.Intrinsicsの既存宣言の対象拡張であり、引数ラベルはvalue、inputは関数のOriginパラメータである。

| 操作 | 入力 | 結果 |
| --- | --- | --- |
| strong clone | `ref/S from input` | S |
| downgrade | `ref/S from input` | Weak<S> |
| upgrade | `ref/Weak<S> from input` | Option<S> |
| Weak clone | `ref/Weak<S> from input` | Weak<S> |

二つのclone宣言は分けて維持し、どちらのSもハンドル型を表す。操作中だけ入力スロットを共有借用し、結果にはSのビュー・所有方式・型引数・外部依存を保つ。入力スロットへの操作用Loanを結果へ持ち越さない。

cloneは責任を一つ増やすだけでpayloadを複製せず、ユーザーコードを呼ばず、ガードを取得しない。upgrade成功もstrongを確保するだけで、借用・ロック取得は別に必要である。clone/upgradeは管理領域を新規確保しないが、最初のdowngradeはtable確保を要し得る。

最終strong解放との競合、strong=0からの非復活、完全動的型の破棄と元の確保領域の解放、Weak管理領域の寿命は現行§13.5.8–9・§21.2.3に従う。Weakはpayloadを生かさず、期限切れでも型・Origin・Loan依存を消去しない。Weak破棄は管理領域だけを扱う。

obj、オブジェクト借用、ガード、asyncをWeakの所有方式にはしない。

### 6.2. 循環

strong循環の自動回収は導入せず、必要な切断にはWeakを使う。local/syncは、Option等で有効な初期状態を作れる可変フィールドなら、通常構築後にwrite/lockでWeakを設定できる。

構築時から必須の自己Weakや不変フィールドまで後設定で代替できるとはしない。local/sync専用の循環構築APIはDeferredとし、既存rc/arcの循環構築APIは維持する。

## 7. 実装・性能方針

### 7.1. 共通の不変条件

実体ごとに管理状態を一組持ち、clone・ビュー変更で複製しない。カウント、借用、ロック、解除責任を独立に管理し、静的なusing保護で実行時検査を代替しない。

ガードは追加のstrongや取得ごとのヒープ確保を不要にする。取得元スロットへの依存は静的なLoanで保持し、実行時にそのスロットを経由する必要はない。管理状態へ直接到達する小さな表現を優先するが、ガード移動時や状態移行時に古いポインタを残さない。

ハンドルスロット、ガード自身、payload、管理領域は別の記憶領域として扱う。現行§21.5.5のref/uniqに対する属性は直近の値の格納領域に限り、ロードしたハンドルの参照先やオブジェクト借用のヘッダへ無条件にreadonly/noaliasを広げない。local/syncの内側の変更と並行アクセスを含め、属性の前提を検証する。

### 7.2. control語の再利用

現行Windows x64プロファイルでは+0が記述子、+8がcontrol、payloadは+16であり、objのcontrolは0である。payload位置を維持し、この予約語とside table移行を利用する。

| 方式 | 実装目標 |
| --- | --- |
| objからrc/arc | 移譲先の公開前にcontrol=2（strong=1）を初期化する。既存ヘッダで完結し、管理領域の追加確保は不要 |
| objからlocal | 非atomicなcontrolにstrong数・共有借用数・排他状態と表現識別を詰め、初期移譲時の追加確保を避ける |
| sync | control内のカウント・ロック共存と、初めから別tableへロック語を置く方式を比較する。CAS競合・待機者・移行を含めて選ぶ |

有効なobjからの移譲であり、破棄済みcountedのstrong=0を復活させる操作ではない。arc/syncは共有公開後にatomic/non-atomicアクセスを混在させず、スレッド間の公開・同期条件も満たす。

localの最初のdowngradeはstrong数と借用状態を一つのtableへ移し、唯一の管理状態として公開する。生存中のガードも解除時に現在の表現を参照する。inline/tableで上限を変えず、overflowを移行で回避しない。rc/arcの既存上限は維持する。

syncの移行は、待機中のロック語のアドレス、解除先、カウント操作の線形化も保つ必要がある。tableの公開と責任移譲を一度だけ行い、古い表現への更新を残さない。

### 7.3. 確保回数と未確定のABI

rc/arc/localのinline直接生成は、一回のオブジェクト確保を目指す。後のdowngradeによるtable確保は別である。syncが別tableを使えば、既存objからは追加一回、直接生成ではオブジェクトとtableの二回を要し得る。全方式で総確保一回とは保証しない。

rc/local、arc/syncでカウント・Weak・解放処理を共有する。ビット配分と上限、table拡張、一語ガード、ワードロック、待機資源と解放経路はプロファイルで検証・測定して確定する。本書だけで固定ABIを変えたり、OS資源の破棄不要を保証したりしない。

## 8. 例示コード

### 8.1. localの共有・更新・競合

```kimi
struct Item
    public var name: string

    public init(name: string)
        self.name = name

func inspectName(name: ref/string) => ()

var first = Item.init("initial")@local
var second = Kimi.Intrinsics.clone(first@ref)

using reader = first.read()
    inspectName(reader.readValue.name@ref)
    match second.tryWrite()
        .None => () // 別ハンドルでも同じ実体が共有借用中。
        .Some(var acquired)
            using writer = acquired
                writer.value.name = "unexpected"

using writer = second.write()
    writer.value.name = "changed"
    inspectName(writer.readValue.name@ref)

using reader = first.read()
    inspectName(reader.readValue.name@ref) // changedを読む。
```

readerの最終使用が早くても、破棄までは共有借用中である。上のtryWriteをwriteにすれば競合時にAbortする。stringはNon-Copyなので借用して読み、payloadをMoveしない。

### 8.2. syncとexit

以下はItemのTTが証明できることを前提とする。実際のスレッドは生成しない。

```kimi
var first = Item.init("first")@sync
var second = Item.init("second")@sync
var finished = false

loop
    work: using left = first.lock()
    using right = second.lock()
        if finished
            exit // 両ガードを逆順に解除し、loopを終了する。

        left.value.name = "updated"
        finished = true
        exit to work // usingだけを終了。次の反復へ進む。
```

### 8.3. usingの保護とpayload投影

```kimi
func replaceItem(target: uniq/Item)
    Kimi.Intrinsics.replace(target, with: Item.init("replaced"))

var shared = Item.init("initial")@local
using guard = shared.write()
    replaceItem(guard.value@uniq/Item) // ItemはSealed。payloadの完全置換は合法。
    inspectName(guard.readValue.name@ref)

    // let taken = guard // エラー：using束縛自身のMove。
    // guard = shared.write() // エラー：using束縛全体の再代入。
```

関数内に隠したガード全体のreplaceやswapも拒否する。通常のuniq借用自体は禁止せず、実際の効果で判断する。payload投影でガードや管理領域への権限は増えない。

### 8.4. Weakとガード生存中の管理状態移行

```kimi
var strong = Item.init("initial")@local
using reader = strong.read()
    let weak = Kimi.Intrinsics.downgrade(strong@ref)
    // 最初のdowngradeでtableへ移ってもreaderの借用状態を保持する。
    match Kimi.Intrinsics.upgrade(weak@ref)
        .Some(let restored)
            using another = restored.read()
                inspectName(another.readValue.name@ref)
        .None => ()
// another、readerはそれぞれのスコープで一度だけ共有借用を解除する。
```

生存中のstrongがあるこの例ではupgradeは成功する。一般には最後のstrong解放と競合してNoneになり得るが、成功してもガード取得は別操作である。

## 9. 導入しない機能と将来方針

### 9.1. 現段階の境界

| 項目 | 方針・理由 |
| --- | --- |
| async Semantics | 文脈内だけ予約。`async/T`・`x@async`・Semantics制約の `s is async` は未対応として拒否 |
| syncのread/write・RwLock | ViewTTだけで形成するsyncへ並列読み取りを足すにはViewTSも検討する必要があるため、別設計 |
| 同一スレッド再ロックの自動検出 | 所有スレッドIDの固定追加や性能上の推奨はしない。非再入とデッドロックの可能性を明示 |
| ガードがstrongを所有する取得API | 初期版では取得元スロットを借りる方式だけ |
| 共有方式間の変換・objへの回収・循環回収 | 導入しない。local/syncの循環構築APIも別設計 |
| 手動のunsafeスレッド能力適合 | 固有の安全性契約を設計するまでDeferred |
| payloadの移譲時再配置 | ハンドルのMoveはpayloadのMoveではなく、生ポインタ・外部登録・同一性への影響を排除できないため許可しない |

### 9.2. 非同期の将来方針

将来は `await source.lock()` とAsyncLockGuardによるobjuniqアクセスを検討する。取得待ちのキャンセルは待機登録を解除し、取得後のタスク破棄はローカルとガードを後始末する。解除は既に行った変更のrollbackではない。

ReadGuard/WriteGuard/同期LockGuardのawait越し保持は禁止する方向とし、AsyncLockGuardとは区別する。構造体・クロージャへの格納でも迂回させず、スレッド能力と中断をまたぐ能力を別に検査する。

タスク型、状態機械、awaitのLoan、取得成功とキャンセルの競合、非同期ガードの能力は未設計である。取得を待つawaitと、解放自体が非同期のawait using相当機能も区別し、いずれも現在の使用可能機能にはしない。

## 10. 仕様統合と検証

### 10.1. 統合先

| 現行SPEC | 反映内容 |
| --- | --- |
| [§2 字句・ソース](../../spec/02-source-and-lexical-structure.md)、[§3 型](../../spec/03-types-and-values.md) | 文脈キーワード、カテゴリ、counted不導入記述の更新、ハンドルとガード |
| [§4 配列・Slice](../../spec/04-arrays-indexing-and-slices.md) | guardedのSharedReadResultとスロット借用の区別 |
| [§6 宣言](../../spec/06-declarations-and-containers.md)、[§8 制約](../../spec/08-generics-constraints-and-contracts.md) | 組み込み能力、公開ビュー保証、継承、再帰証明、形成条件 |
| [§7 関数](../../spec/07-functions-and-callable-values.md)、[§10 適応](../../spec/10-overload-resolution-and-inference.md)、[§11 プロパティ](../../spec/11-properties.md) | 取得API、receiver、固定型アクセサ、Origin契約 |
| [§12 オブジェクト呼び出し](../../spec/12-expressions.md)、[§15 所有権](../../spec/15-ownership-and-lifetime-analysis.md) | using保護、既存効果の適用、ガードLoan、別名・返却・捕捉 |
| [§13 変換・所有API](../../spec/13-operators-and-assignment.md) | §2の生成・移譲・取得表、local/syncのビュー操作、clone/Weakの対象拡張 |
| [§14 制御](../../spec/14-control-flow.md)、[§16 破棄](../../spec/16-scope-exit-and-destruction.md) | using文法・束縛・ラベル・透過的exit・初期化途中と正常退出の後始末 |
| [§18 成果物](../../spec/18-modules-and-dependencies.md) | 既存効果要約の利用、能力保証・条件・証明依存の保存と再検証 |
| [§21 実装](../../spec/21-layout-runtime-and-code-generation.md)、[§22 Kimi](../../spec/22-core-execution-and-foreign-functions.md) | 管理状態、カウンタ、同期・移行、最適化属性、標準宣言の識別情報 |
| [付録D Deferred](../../spec/appendices/D-deferred-features.md)、[付録F 文法](../../spec/appendices/F-syntax-summary.md) | 将来機能の境界、usingの文法、Semantics制約 |

objectは維持するが、owning/reference・型検査・キャスト・取得・メンバー適応の各判定表を更新し、local/syncがガードを迂回しないことを確認する。規範の統合、実装、検証が終わるまでSTATUSに実装済みとして記録しない。

### 10.2. 検証項目

| 分野 | 必須の代表例 |
| --- | --- |
| 字句・型 | 通常名local/sync/async/using、guarded/countedの文脈限定、async拒否、全許容型でのカテゴリ検証 |
| 変換 | 生成・移譲・同一取得・ビュー変更の識別、競合LoanでのMove拒否、所有方式間の禁止、同一性・位置・依存の維持 |
| ガード | 複数read、write競合、tryのNone、overflow、全readValue、排他Loan競合、最終使用後も解除しないこと |
| 名前解決 | payload.cloneを奪わないこと、標準取得APIの識別、不適用時のpayload再解釈禁止 |
| 寿命 | 要素スロット・一時値・ガード・外部依存、Copy/返却/格納/捕捉後の解除越し参照の拒否 |
| using構文 | ラベル後の認識、空行・コメントを挟む連結、本体欠落、単一項目Body、入れ子、初期化式での自身ラベル拒否 |
| using保護 | 直接・別名・関数・アクセサ・間接呼び出し・特殊化・cleanup経由の全体更新と不完全化の拒否、合法な部分更新 |
| Scope Exit | 裸のexitの透過性、名前付きUnit退出、return/continue/yield、取得途中の退出、逆順破棄、Abortの境界 |
| 能力 | openビューの保証不足、派生型違反、型消去・再帰証明、local参照の転送とガード本体の非転送 |
| 管理状態 | ガード生存中のdowngrade、唯一のtable公開、移行後の解除、syncの待機・移行・最終解放の競合、Weak非復活 |
| 実装 | カウンタ上限、追加確保の有無、ガード表現、alias属性の範囲、証明・成果物依存の再検証 |

診断は対象の束縛・操作・Loan・呼び出し経路・不足する能力を示す。本節は検証要件であり、テスト実施結果ではない。
