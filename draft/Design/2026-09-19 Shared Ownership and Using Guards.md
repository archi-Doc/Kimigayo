# 共有所有・ガード・using — 仕様原案

- 作成日: 2026-09-19
- 状態: 設計議論をまとめた仕様原案。現行SPECへの統合前であり、実装済み機能を示す文書ではない。
- 対象: `local`・`sync`、オブジェクト借用、ガード、`using`、スレッド能力、Weak。
- 将来拡張: `async` はSemanticsの文脈だけで予約する。非同期ガード・`await`・キャンセルはDeferredとする。

本書で変更する事項はSPEC.mdとその参照先より優先し、変更しない事項には既存仕様を適用する。`objref`・`objuniq` は維持し、共有所有方式を独立したSemanticsとして扱う。

表や手続きによって補った細部は、合意した方針を実現するための原案上の具体化である。実装方式・ABI・未設計のスレッド実行機能まで確定したものではない。現行SPECへ反映するときは、本書末尾の統合先と照合する。

例示コードは本原案の新構文・新APIを含む。現行コンパイラで実行できるという意味ではない。省略したヘルパー関数や既存の名前は、例の説明に必要な宣言が存在するものとする。

## 1. 設計の全体像

### 1.1. 役割の分担

所有方式、アクセス権、アクセス期間を別々に表す。

```text
オブジェクトの管理
├─ 所有方式を表すSemantics
│  ├─ obj       単独所有
│  ├─ rc        非atomicな参照カウントによる共有所有
│  ├─ arc       atomicな参照カウントによる共有所有
│  ├─ local     非atomicな共有所有 + 実行時借用検査
│  └─ sync      atomicな共有所有 + 同期Mutex
│
├─ オブジェクトへのアクセス権を表すSemantics
│  ├─ objref    共有借用
│  └─ objuniq   排他借用
│
├─ 取得した借用・ロックを管理する通常の所有値
│  ├─ ReadGuard<T>
│  ├─ WriteGuard<T>
│  └─ LockGuard<T>
│
└─ 管理対象を保護し、使用範囲を明示する構文
   └─ using var

将来拡張
└─ async       Semanticsの文脈だけで予約。非同期Mutex等の詳細はDeferred
```

`local/T` はRustの `Rc<RefCell<T>>`、`sync/T` は `Arc<Mutex<T>>` に相当する用途を担う。ただし、内部表現、失敗の扱い、継承ビュー、借用表現までRustと同じにするわけではない。

### 1.2. 維持する区別

| 型 | 所有・アクセスの意味 |
| --- | --- |
| `T` / `owner/T` | 完全な値を直接所有する |
| `ref/T` | 完全な値の格納領域を共有借用する |
| `uniq/T` | 完全な値の格納領域を排他借用する |
| `obj/T` | オブジェクトを単独所有する |
| `objref/T` | オブジェクトをTのビューで共有借用する |
| `objuniq/T` | オブジェクトをTのビューで排他借用する |

「排他的に操作できること」と「所有権を持つこと」は異なる。`objuniq/T` の破棄は、借用先オブジェクトの所有破棄ではない。

```text
obj/T
    └─ 所有責任を持つ。責任を移譲しなければ、破棄時にオブジェクトを破棄する。

objuniq/T
    └─ 借用期間中の排他アクセス権を持つ。借用だけから所有権は取得できない。
```

`ref/(obj/T)` は所有ハンドルの格納領域を借り、`objref/T` はオブジェクトそのものを借りる。この区別も維持する。

## 2. Semanticsと型体系

### 2.1. 独立したSemanticsとして扱う

`rc`・`arc`・`local`・`sync` は、型体系から識別できる組み込みSemanticsとする。通常のライブラリ型への単純な別名として展開しない。

| 完全な型 | 外側のSemantics | 直接の対象 |
| --- | --- | --- |
| `obj/Animal` | `obj` | `Animal` |
| `rc/Animal` | `rc` | `Animal` |
| `arc/Animal` | `arc` | `Animal` |
| `local/Animal` | `local` | `Animal` |
| `sync/Animal` | `sync` | `Animal` |

既存の `<s/T>` による分解を利用できる。例えば、`s is local or sync` はSemanticsに対する条件であり、所有ラッパーのCore名を検査する操作ではない。

実装上は、参照カウント、Weak、動的型情報、破棄処理などを共通化してよい。異なるSemanticsが必ず異なる機械語や異なる確保方式を要求するわけではない。

#### 文脈キーワード

`local`・`sync`・`async` は、既存の `rc`・`arc` と同じ文脈キーワードとする。型の接頭辞（`local/T`）、Semantics制約（`s is local or sync`）、明示変換先（`x@sync`・`x@sync/Animal`）でのみSemantics名として認識する。通常の除算式 `local / count` は影響を受けない。

それ以外では通常の名前として、`let local = 1`・`func sync() => ()`・`x.async` などに使える。`async` は上記のSemantics文脈だけで将来用に予約し、未対応の診断を出す（§13.1）。

### 2.2. Semanticsカテゴリ

現在のカテゴリへ `local`・`sync` を次のように追加する。

| カテゴリ | 本原案での構成 |
| --- | --- |
| `value` | `owner` |
| `valueborrow` | `ref`, `uniq` |
| `object` | `obj`, `rc`, `arc`, `local`, `sync` |
| `objectborrow` | `objref`, `objuniq` |
| `borrow` | `ref`, `uniq`, `objref`, `objuniq` |
| `owning` | `owner`, `obj`, `rc`, `arc`, `local`, `sync` |
| `reference` | `ref`, `uniq`, `obj`, `rc`, `arc`, `local`, `sync`, `objref`, `objuniq`, `unsafe` |

カテゴリ所属だけでは、固有のアクセス操作を許可しない。特に、`s is object` は「ガードなしでpayloadを読める」という保証ではない。

`async` は有効なSemantics、カテゴリの構成要素、Semantics引数、Weakの所有方式にはまだ加えない。通常の名前としての使用は妨げない。

### 2.3. 所有値の取得

`obj`・`rc`・`arc`・`local`・`sync` の所有ハンドルはNon-Copyとする。通常の取得ではMoveし、共有所有者を増やす場合は明示的な `clone()` を使用する。

ガードもNon-Copyだが、ガード用の明示的な `clone()` は提供しない。Non-Copyであることと、明示的cloneを持つかどうかは別の性質である。

### 2.4. 配列・Slice等からの共有要素取得

既存の共通規則 `SharedReadResult(T, source)` に、次の行を追加する。他の要素型の結果は変更しない。

| 完全な要素型 | 結果 | 操作 |
| --- | --- | --- |
| `local/T` | `ref/local/T from source` | ハンドルの格納場所を共有借用する |
| `sync/T` | `ref/sync/T from source` | ハンドルの格納場所を共有借用する |

この規則を使う配列・Slice等の操作すべてに適用する。要素取得だけではpayloadを借用せず、参照カウントの増加、実行時借用の取得、ロック取得も行わない。結果は元の格納場所と要素が持つ寿命依存を保持する。

```kimi
// itemsはSlice<local/Item>。Itemの定義は§14.1。
let handle = items[0] // ref/local/Item。要素のMoveやcloneではない。
using var guard = handle.read()
    inspectName(guard.value.name@ref)
```

`@ref`、`tryGet()`、走査など、既に要素の格納場所を借りる操作は既存規則を維持する。未知の要素型を扱うジェネリック関数では、この二つもSharedReadResultの候補として検証する。

## 3. オブジェクトの生成と所有方式の移譲

### 3.1. objを所有権の出発点とする

```text
完全な所有値T
    │ @obj
    ▼
obj/T
    ├─ @rc    → rc/T
    ├─ @arc   → arc/T
    ├─ @local → local/T
    └─ @sync  → sync/T   （§11のTTとビュー保証が必要）
```

各変換は元の `obj/T` をMoveし、オブジェクトの所有責任を移譲する。payloadの複製や再コンストラクションは行わない。動的型・オブジェクトの同一性・外部への寿命依存を保持する。

```kimi
var object = A.init()@obj
var shared = object@local
// objectはMoved。以後の使用は禁止。

var second = shared.clone()
// sharedとsecondは、同じ実体と同じ借用状態を共有する。
```

新しい管理状態は、移譲先を公開する前に初期化する。保持中のLoanと競合する所有ハンドルのMoveは、通常の借用規則によって拒否する。

`objuniq/T`・`objref/T` から所有方式を生成してはならない。借用から、新しい所有責任を作ることはできない。

### 3.2. 直接生成の省略形

次を許可する。

```kimi
var a = A.init()@local
var b = A.init()@sync
```

所有権の意味は、次の段階的な生成と同じである。

```kimi
var a = (A.init()@obj)@local
var b = (A.init()@obj)@sync
```

これは中間のobj表現や二重のメモリ確保を要求しない。最終形式へまとめて生成する方針と配置の設計範囲は§16.1に定める。

入力の所有値は通常のCopy／Move規則で取得する。`a@local` が常にaをMoveするという意味ではなく、aがCopyなら、その取得結果を新しいオブジェクトへ格納する。

既存の `Kimi.Intrinsics.makeObj`・`makeRc`・`makeArc` は、本生成規則に従う便利APIとして位置付ける。本書では `@obj` 等を含む生成・移譲を新しい明示的Adaptationとして提案しており、現行SPECですべて使用できるという意味ではない。

### 3.3. 共有所有方式間の変換

初期版では、共有所有方式どうしの直接変換を許可しない。

```text
rc    ─×→ arc
local ─×→ sync
sync  ─×→ local
その他、共有所有方式どうしの直接変換も同様
```

参照数が一時的に1であっても、例外として扱わない。共有所有から `obj` へ戻す特別な回収APIも、本原案には追加しない。

所有方式を変えずにView Targetを変更するアップキャストは、この禁止とは別の操作である。

## 4. localとsyncのアクセスモデル

### 4.1. local/T

`local/T` は、単一スレッド内で共有する可変オブジェクトを表す。非atomicな参照カウントと、実体ごとに共有される実行時借用状態を持つ。

```text
local/TのハンドルA ─┐
                   ├─→ 共通の管理状態 ─→ オブジェクト
local/TのハンドルB ─┘       │
                           └─ 共有借用数 または 排他借用中
```

| 現在の状態 | read / tryRead | write / tryWrite |
| --- | --- | --- |
| 借用なし | 取得可能 | 取得可能 |
| 共有借用あり | 追加取得可能 | 競合 |
| 排他借用あり | 競合 | 競合 |

`read()`・`write()` は競合時にAbortする。待機はしない。`local` はスタック配置やスレッドローカル変数を意味せず、オブジェクトの共有方式を表す。

### 4.2. sync/T

`sync/T` は、atomicな参照カウントと同期Mutexによって共有する可変オブジェクトを表す。形成にはTのThreadTransferableを必要とし、Tがopenな型なら派生型全体へのTT保証も必要とする。詳細は§11に定める。

`lock()` は排他的なガードを返す。競合時は現在のスレッドが待機する。読み取りだけの場合でも、payloadへのアクセスにはロック取得が必要である。

ロックは非再入とする。同じ実体のロックを保持したまま再取得すると、待機によるデッドロックが起こり得る。自動的な再入、デッドロック検出、FIFOの公平性、待機時間の上限は保証しない。

unlockはrelease、成功したlock・tryLockはacquireとして同期する。同じMutexを介した先行処理の結果を後続処理から観測できなければならない。失敗したtryLockには取得による同期を保証しない。OS機構やatomic命令列は後で決める。

### 4.3. 直接アクセスの禁止

`local`・`sync` の所有ハンドルから、ガードを経由せずにpayloadを読み書きしてはならない。

```kimi
var shared = A.init()@local

// shared.name = "A"       // エラー：payloadへの直接アクセス。
// let view = shared@objref // エラー：ガードを迂回した借用。
// let view = shared@objuniq

using var guard = shared.write()
    guard.value.name = "A"
```

メンバー転送、暗黙のreceiver適応、通常のpayload投影、キャスト、型の絞り込みによっても、この制限を迂回できない。動的型メタデータだけを扱う操作とpayloadアクセスは区別し、型検査やビュー変更がアクセス権を新しく与えることはない。

ハンドル操作の名前解決と、payloadの同名メンバーとの区別は§5.2に定める。

### 4.4. 取得と失敗の共通規則

| 状況 | 通常API | try系API |
| --- | --- | --- |
| localの借用競合 | Abort | `None` |
| syncのロック競合 | スレッドを待機 | `None` |
| 共有借用数のoverflow | 状態変更前にAbort | 同左。`None`にはしない |

成功時は、借用・ロックの取得状態と一つのガードの解除責任を対応付けて公開する。未取得のまま返る場合は、その呼び出しによる借用数の増減や解除責任を残さない。競合を調べる内部処理は、この契約を変えてはならない。

strong・weak数も増加前にoverflowを検査し、限界を超える操作はAbortする。既存rc・arcの上限は維持し、新しい管理方式と共有借用数の具体的な上限は実装プロファイルで定める。カウンタの折り返しや飽和によって、取得成功や無制限の保持を装ってはならない。

## 5. ガードと取得API

### 5.1. 取得APIの一覧

| 取得式 | 戻り値 | valueの結果 | ガードの破棄 |
| --- | --- | --- | --- |
| `source.read()` | `ReadGuard<T>` | `objref/T` | 共有借用を解除する |
| `source.write()` | `WriteGuard<T>` | `objuniq/T` | 排他借用を解除する |
| `source.lock()` | `LockGuard<T>` | `objuniq/T` | unlockする |
| `source.tryRead()` | `Option<ReadGuard<T>>` | 成功時はreadと同じ | 同左 |
| `source.tryWrite()` | `Option<WriteGuard<T>>` | 成功時はwriteと同じ | 同左 |
| `source.tryLock()` | `Option<LockGuard<T>>` | 成功時はlockと同じ | 同左 |

表ではOriginを省略した。正式な入出力と寿命の契約は§5.2に定める。

`ReadGuard`・`WriteGuard` の取得元は `local/T`、`LockGuard` の取得元は `sync/T` とする。取得元ハンドルの束縛自体が不変でも、通常の共有アクセスによる取得は可能である。実体に対する権限は借用検査・ロックが管理する。

try系は待機せず、成功時に取得済みガードをSomeへ格納し、競合時にNoneを返す。overflowなどとの区別は§4.4に従う。

```kimi
match shared.tryWrite()
    .Some(var acquired)
        using var guard = acquired
            guard.value.name = "updated"
    .None => ()
```

`using` はOptionを自動的に展開しない。成功分岐から通常のMoveでガードを渡す。

### 5.2. 標準宣言・名前解決・Origin

#### 標準宣言の契約

次の表を標準APIの宣言スキーマとする。取得関数は `Kimi.Intrinsics` に属し、引数ラベルは `value` とする。Tは有効なオブジェクトのView Targetであり、syncを含む宣言には§11の形成条件も適用する。

| 標準関数 | 入力 | 結果 |
| --- | --- | --- |
| `read<T>` | `ref/local/T from source` | `ReadGuard<T> from source` |
| `write<T>` | `ref/local/T from source` | `WriteGuard<T> from source` |
| `lock<T>` | `ref/sync/T from source` | `LockGuard<T> from source` |
| `tryRead<T>` | readと同じ | `Option<ReadGuard<T> from source>` |
| `tryWrite<T>` | writeと同じ | `Option<WriteGuard<T> from source>` |
| `tryLock<T>` | lockと同じ | `Option<LockGuard<T> from source>` |
| `clone<S>` | `ref/S from input` | `S` |

sourceとinputは関数のOriginパラメータである。cloneのSは有効なrc・arc・local・syncハンドル、または既存規則で有効なWeakとする。cloneは入力スロットへの借用を結果へ持ち越さず、S自身のView・型引数・外部依存を保存する。

`Kimi.ReadGuard<T> origin source`、`Kimi.WriteGuard<T> origin source`、`Kimi.LockGuard<T> origin source` は、コンパイラが管理するNon-Copyなstruct Coreとする。`source` は所有ガードの型に含まれるOrigin引数であり、所有値に外側の借用Semanticsを追加するものではない。公開コンストラクタや解除責任を複製する操作は持たず、対応する取得関数だけが取得済みの状態を構築する。

valueは次のreceiverと結果を持つ組み込みアクセサであり、setを持たない。guardはアクセサ呼び出しでガード自身を借りるOriginとする。

| ガード | receiver | 結果 |
| --- | --- | --- |
| ReadGuard | `ref/(ReadGuard<T> from source) from guard` | `objref/T from guard` |
| WriteGuard | `uniq/(WriteGuard<T> from source) from guard` | `objuniq/T from guard` |
| LockGuard | `uniq/(LockGuard<T> from source) from guard` | `objuniq/T from guard` |

sourceはguard以上の期間有効でなければならない。結果はTの外部依存と、取得元・ガードを通るLoanの由来をすべて保持する。valueの結果をsourceだけに結び付け、ガードより長く使用可能にしてはならない。

#### ドット表記

`source.read()` 等は、対応する標準関数へハンドルの共有借用を渡す表記とする。ハンドルのPlace、またはそのスロットへのref・uniqから、通常のBorrow／Reborrowでreceiverを作る。読み取り専用の束縛や、§2.4の要素取得結果からも呼び出せる。

標準ハンドル操作は、次の対応表と標準宣言の識別情報で解決する。登録された名前はpayloadの同名メンバーより優先し、適用条件を満たさない場合もpayload側へ解釈し直さない。同名のユーザー関数やContractに特別な権限を与えない。

| モード | 登録する名前 |
| --- | --- |
| rc・arc | clone |
| local | read、write、tryRead、tryWrite、clone |
| sync | lock、tryLock、clone |

local・syncのpayloadメンバーはguard.valueから呼ぶ。rc・arcでpayload自身のcloneを呼ぶ場合も、明示的なpayload借用を取得してから呼ぶ。objref・objuniqに対する呼び出しを、所有ハンドルのcloneと解釈してはならない。対応表にない名前には既存の名前解決と§4.3を適用するため、local.lockなどがpayload経由で有効になることはない。

### 5.3. ガードの所有と破棄

- ガードは通常のNon-Copyな所有値であり、管理状態を解除する責任を持つ。
- `using` 外でも使用できる。通常のMove・格納・戻り値としての転送は、型・Loan・Originの規則を満たす限り許可する。
- 明示的なガードの `clone()` は導入しない。
- 標準ガードの公開 `unlock()`・`release()` は導入しない。
- 解除処理はガードの破棄に組み込み、正常な破棄で一度だけ実行する。
- ガードの最終使用を理由に、観測可能な解除時点をスコープ上の破棄時点より前へ移してはならない。

```kimi
do
    var guard = shared.write()
    guard.value.name = "A"
    // 後続でguardを使わなくても、ここではまだ解除しない。
// このスコープで責任を保持していれば、終了時に解除する。
```

### 5.4. valueアクセサの権限

`ReadGuard<T>.value` は共有アクセスから `objref/T` を取得する。`WriteGuard<T>.value` と `LockGuard<T>.value` は、ガードへの排他的アクセスから `objuniq/T` をReborrowする。

ガード自体を渡すことなく、payloadへの参照を通常の関数へ渡せる。

```kimi
using var guard = shared.write()
    rename(guard.value)
```

同じガードから競合する排他参照を作ることは禁止する。

```kimi
using var guard = shared.write()
    let first = guard.value
    // let second = guard.value // firstが後で使われるなら競合。
    first.name = "A"
```

最初のLoanが終了した後の再取得は許可する。ガードの解除時点と、子Reborrowの最終使用によるLoan終了は別である。

### 5.5. 生存期間の依存

初期APIは取得元ハンドルを借りるガードとして扱う。ガード自体が追加のstrong所有者をcloneするAPIは導入しない。ガードが生存している間、取得元に対する必要なLoanを保持し、実体を破棄できないようにする。

```text
所有ハンドルの有効性
    └─ 取得されたガードの有効性
           └─ guard.valueから得た参照の有効性
                  └─ フィールド参照・Reborrow・捕捉等の有効性
```

payloadが外部の値を借りていれば、その依存も保持する。ガード依存はCopy、アップキャスト、チェック付きキャスト、戻り値、フィールド格納、クロージャ捕捉を経ても失われない。

オブジェクトが別のstrong所有者によって生き続けていても、解除済みガードから得た参照は使用できない。参照先の寿命と、アクセス権の有効期間は別である。

取得元が一時値である場合も、既存の一時値・借用規則を満たす必要がある。`using` が無条件に任意の一時値の寿命を延ばすことはない。

## 6. 継承ビューと完全payload

### 6.1. objref・objuniqを維持する理由

```text
local/AnimalのView
    │
    └─ 実体: Dog
         ├─ Animal部分
         └─ Dog固有部分

write()で得たガード
    └─ value: objuniq/Animal
         ├─ Animalとしての許可された操作が可能
         └─ 完全なAnimalとしての全体置換は許可しない
```

排他借用やロックが保証するのはアクセスの排他性であり、実体が完全な静的型Tであることではない。

通常の `uniq/Animal` を要求する関数へ、`objuniq/Animal` を無条件に渡してはならない。

```kimi
func reset(target: uniq/Animal)
    Kimi.Intrinsics.replace(target, with: Animal.init())

using var guard = animal.write()
    guard.value.name = "Pochi"
    // reset(guard.value) // エラー：完全なAnimalへの借用ではない。
```

オブジェクトや基底部分に対するメソッド呼び出しは、既存の `ObjectCallCompatible` と通常のアクセス・Loan規則に従う。呼び出し先で全体置換する経路も、この保証で検査する。

### 6.2. ビュー変更

`local`・`sync` でも、既存のオブジェクトモデルと同じSupports関係に基づく明示的なビュー変更を扱う。

```kimi
// DogはAnimalの派生型。必要なOwned等の既存条件を満たすものとする。
var dog = Dog.init()@local
var animal = dog@local/Animal
// 同じDog、同じ所有方式、同じ借用状態。dogはMoved。
```

所有ハンドルのアップキャストはMoveであり、参照数を増やさない。借用ビューの変更は既存のCopy／Reborrow規則に従う。

別ビューを作っても、借用状態やロックを新しく作らない。型消去時のOwned条件と外部依存を維持し、変換先に必要な形成条件を満たさない変換は拒否する。特にsyncでは§11のビューTT保証を必要とする。任意の転送能力を失うだけの他モードの変換は、その後の型から能力を再判定する。

### 6.3. Sealedな完全payloadへの投影

View Targetが同じ完全なSealed型Tであると証明できる場合は、既存の明示的投影を使える。

```kimi
using var guard = cell.write()
    let payload = guard.value@uniq/Cell
    resetCell(payload) // uniq/Cellを受け取る通常の関数。
```

この投影もガードのLoanを保持する。完全payloadの置換が合法でも、ガード自身やオブジェクトの管理領域を置換する権限は与えない。

`local`・`sync` の対象を一律にSealedへ限定する案は採用しない。

## 7. using var

### 7.1. 基本構文と意味

```kimi
using var guard = source.write()
    guard.value.name = "A"
```

`using` は管理対象を取得し、その使用範囲と破棄責任を保持する文である。値を返す式にはしない。束縛構文は `using var` に統一し、`using let` は導入しない。

`var` は管理対象への排他的アクセスを可能にする。一方、`using` が管理対象自身のMove・再代入・全体交換を制限するため、通常のvarと全く同じ権限ではない。

読み取りガードでも同じ構文を使い、payloadへの権限はガード型で決める。文字列を借りて読む例は§14.1に示す。

初期化式を一度評価し、正常に初期化された時点から管理対象を保護する。通常の値取得、型推論、Origin推論を使う。既存のガードを初期化式からMoveして管理対象にすることは許可する。

本体は共通Body規則に従う。以下の例ではインデント形式を使う。末尾に本体を持たず、外側スコープ終了まで管理を継続する宣言形式は導入しない。

### 7.2. 複数宣言は縦に連ねる

```kimi
using var first = a.lock()
using var second = b.lock()
    update(first.value, second.value)
```

同じインデントで連続するusingヘッダと、その後の共通の本体を、一つのusing文として扱う。カンマ区切りの複数宣言は導入しない。

```text
一つのusing文
├─ ヘッダ1: firstを取得
├─ ヘッダ2: secondを取得
└─ 共通の本体
     └─ 退出時: 本体の後始末 → second破棄 → first破棄
```

1. 上から下へ初期化する。各束縛の型は個別に推論する。
2. 後続の初期化式は、先に初期化済みの束縛を参照できる。保護・借用規則はここにも適用する。
3. 同じヘッダ列で束縛名を重複させない。
4. すべての取得後に本体を実行する。
5. 本体の後始末後、管理対象を宣言の逆順で破棄する。

先行するロックを保持したまま後続のロックを取得する。一括取得、原子的な複数ロック操作、競合時の自動rollbackではない。同一ロックの再取得や取得順の食い違いによるデッドロックは許容する。

### 7.3. 初期化途中の制御移動

後続の初期化が `return` や外側への `exit` 等で中断された場合、正常に初期化済みの管理対象だけを逆順に破棄する。未取得の対象に破棄処理を実行しない。

初期化式の一時値や未完成の値は、既存の構築・退出規則で処理する。Abortや非終了では通常の後始末を保証しない。try系の `None` は通常の値であり、usingが自動的に失敗退出として扱うことはない。

using自身が退出対象として有効になるのは本体の中だけとする。ヘッダの初期化式から、自分自身のusingへexitする規則は追加しない。既に初期化された対象の破棄責任は、本体へ到達する前でも有効である。

### 7.4. ラベルと入れ子

ラベルは先頭のヘッダに付け、ヘッダ列全体と本体を指す。後続ヘッダに独立の退出ラベルを付けない。

```kimi
work: using var first = a.lock()
using var second = b.lock()
    exit to work
```

インデントを増やして本体内へusingを書く場合は、独立した入れ子のusing文になる。

```kimi
using var first = a.lock()
    using var second = b.lock()
        exit // 内側のusingだけを終了。secondを破棄する。
    inspect(first.value)
```

縦連結と入れ子は、取得・破棄の順序が似ていても、裸のexitが指す単位は異なる。

## 8. exitとScope Exit

### 8.1. usingを退出対象に追加する

裸の `exit` は、内側から探して最初の有効な反復構文・using・deferを対象とする。既存の関数・deferの境界規則を維持する。`do` は従来どおり、裸のexitの対象にはしない。

using自身へのexitはUnitのみを受け取る。`exit`・`exit ()` は許可するが、`exit 123` は許可しない。

```kimi
using var guard = shared.lock()
    if unnecessary
        exit
    update(guard.value)
// exit時も、guardを破棄してからここへ進む。
```

### 8.2. 外側への制御移動

| 制御移動 | usingから退出する場合の処理 |
| --- | --- |
| using自身へのexit | 後始末して、usingの直後へ進む |
| 外側への名前付きexit | 後始末して、指定された外側へ移動する |
| 外側の反復構文へのcontinue | 後始末して、次の反復へ進む |
| return | 結果を確保し、借用の妥当性を検証し、後始末して関数から戻る |
| 外側の選択への有効なyield | 通常の対象選択・結果規則に従い、退出するusingを後始末する |

usingは `continue`・`yield`・`return` の独自の対象や検索障壁にはならない。内側のループだけを抜けるexitは、外側のusingを終了しない。

```kimi
outer: loop
    using var guard = shared.lock()
        if skipWork
            exit          // usingだけを抜ける。
        if finished
            exit to outer // guardを破棄してloopを抜ける。
        if retry
            continue      // guardを破棄して次の反復へ進む。
        update(guard.value)
```

### 8.3. 後始末の順序と保証の範囲

```text
usingからの正常な退出
    1. 退出先と、必要な結果を確定する
    2. 本体内のローカル変数・deferを既存の逆順規則で処理する
    3. using管理対象を宣言の逆順で破棄する
    4. 退出先へ制御を渡す
```

厳密な一時値の破棄位置と結果確保は、既存のScope Exit規則を維持する。返す値がガードの破棄後も有効であることを検査し、無効になる参照を返してはならない。

Abortでは通常の破棄を保証しない。処理や破棄が非終了になる場合も、後続の解除が実行されるとは保証しない。単なる最終使用を理由に、using管理対象を早期破棄しない。

## 9. using管理対象の保護

### 9.1. 禁止する操作

| 操作 | 制限 |
| --- | --- |
| Move・所有権の持ち出し | 別変数、所有引数、return、所有キャプチャ、格納先等へのMoveを禁止する |
| 再代入・全体置換 | 管理対象自身への代入、replace、破棄と再構築を禁止する |
| 全体交換・取り出し | 管理対象自身を対象とするswap・exchangeを禁止する |
| 不完全化 | 管理対象を不完全にするPartial Moveを禁止する |
| 手動破棄 | 自動破棄責任を迂回する管理対象の破棄を許可しない |
| 参照の寿命超過 | 管理対象やガードに依存する参照が、必要な生存期間を超えることを禁止する |

管理対象が完全な値であること、Sealedな型であること、型引数が具体化されたことは、この保護を解除する理由にならない。

```kimi
using var guard = shared.write()
    guard.value.name = "A" // 許可。

    // let taken = guard                       // 禁止：Move。
    // consume(guard)                          // 禁止：所有引数への取得。
    // guard = other                           // 禁止：再代入。
    // Kimi.Intrinsics.swap(guard, other)        // 禁止：全体交換。
    // Kimi.Intrinsics.exchange(guard, with: other)
```

### 9.2. 中身の操作と管理対象の操作

管理対象の通常の読み取り、合法な借用、完全性を保つフィールド更新は、それぞれのアクセス権とLoan規則の下で許可する。ガードの非公開管理フィールドを利用者が直接変更するAPIは与えない。

特に、次を混同しない。

```text
guard自身への操作
    └─ usingによる全体保護の対象

guard.valueが指すpayloadへの操作
    └─ objref／objuniq、ObjectCallCompatible、ガードのLoanで管理
```

payloadの全体置換がSealed投影等によって合法になる場合でも、ガード自身の全体置換は許可しない。

### 9.3. 汎用リソースにも適用する

usingを標準ガードだけに限定しない。適切な破棄処理を持つファイル等の通常の所有値にも適用できる。

```kimi
using var file = File.open(path)
    file.write("hello")
```

この呼び出しも、後述の効果検証を通過する必要がある。特定のメソッド名を理由に無条件で許可しない。

usingが保証するのは管理対象の保持と破棄責任であり、任意のリソース型の `close()` まで一律に禁止するわけではない。型が合法なclose操作を持つ場合、その状態遷移やclose後の破棄は、その型自身の契約による。

標準ガードの早期解除は、公開のunlock・release・cloneを提供せず、管理状態と破棄処理を型の内部で管理することで防ぐ。

## 10. 関数呼び出し先までの保護

### 10.1. 採用する方式

**既存の効果解析と借用追跡を使い、usingを保護対象の指定として追加する。**

ガード自身を直接引数に書くことだけを禁止する方式は採用しない。合法な借用や関数呼び出しを許し、その操作が保護対象を破壊しないことを検証する。

説明用に、入力の根を保持する保証を `RootPreserving` と呼ぶ。これは新しいSemantics名や、全関数に手書きする注釈構文ではない。

### 10.2. 解析する対象

効果は関数全体に一つの安全フラグを付けるだけでなく、receiver、各引数、捕捉、静的な参照元、返された別名に対応付ける。

```text
関数の公開された効果情報
├─ receiverへの操作
├─ 第1引数への操作
├─ 第2引数への操作
├─ 捕捉・静的な参照元への操作
└─ 戻り値が参照する入力・格納領域の対応
```

既存のWhole・Base・Part・Separate・MayAlias等の格納領域関係と、操作の種類を利用する。usingでは、保護対象全体を更新できる「完全な対象だから許可する」という例外を設けない。

| 操作・関係 | usingに対する判定 |
| --- | --- |
| 合法な読み取り・借用 | 許可 |
| 完全性を保つ合法な部分更新 | それ自体では全体保護違反にしない |
| 保護対象全体やそれを取り出せる包含値の更新・所有取得 | 禁止 |
| 借用の返却 | Origin・Loan・対象対応と保護を保持する |
| 別の領域への操作 | 分離を証明できれば、その保護対象への違反ではない |
| 別名か不明な対象への破壊的操作 | 証明不足として拒否 |
| カスタムget/set、通常関数、デフォルト引数、cleanup | 効果を実際の参照先へ対応付けて合成する |

無害だと証明済みの読み取りまで、単に別名の可能性があるという理由だけで破壊的操作とはみなさない。

### 10.3. 関数を経由した全体更新

```kimi
func reset<T>(target: uniq/T, replacement: T)
    Kimi.Intrinsics.replace(target, with: replacement)
```

この関数は、通常の完全な値に対しては使用できる。その効果は「targetの全体置換」として公開する。using管理対象をtargetとして渡す呼び出しは拒否する。

別の関数がresetを呼ぶ場合も、その効果を上位へ伝播する。receiver引数、アクセサ、関数値を経由したからといって、禁止を失わせない。

### 10.4. 別名と戻り値

```kimi
using var guard = shared.lock()
    let alias = forward(guard) // 保護を保った排他借用を返す関数を想定。
    // alias経由でも、guard全体の置換・取り出しは禁止。
```

表面上の型が通常の `uniq/LockGuard<T>` でも、保護対象を指すLoanの由来を保持する。参照を返す関数の公開情報は、戻り値と入力の対応を失ってはならない。

新しい `protecteduniq/T` のようなSemanticsは追加しない。型が同じであることやOriginが同じであることだけで、保護情報を消去できない。

### 10.5. 別コンパイル・ジェネリクス・間接呼び出し

| 呼び出し | 必要な情報 |
| --- | --- |
| 検証可能な直接呼び出し | 本体から導出した効果要約 |
| 別コンパイルされた関数 | 成果物に保存した検証済みの公開効果情報 |
| ジェネリック関数 | 宣言された制約の下で、すべての許容される型・Originについて成立する保証 |
| Contract経由 | Requirement側の効果保証と、その保証を満たす実装 |
| 関数値・コールバック | その呼び出し契約で保持される効果保証 |
| 証明できない呼び出し | 保護対象に影響し得る場合は拒否 |

既存の効果検証と同様に、再帰は固定点で扱い、未完了の解析を「安全」として公開しない。型検査される分岐と、閉じた明示的特殊化集合の全実装を考慮する。都合のよい実型や最適化結果だけで保証を強化しない。

未検証のunsafe・外部呼び出しが保護対象へ影響し得る場合も、無条件には許可しない。生のポインタを使えば任意のメモリ操作まで自動的に安全になる、という保証ではない。

`ObjectCallCompatible` の解析基盤を共通化するが、その判定結果をそのまま流用しない。オブジェクトの完全性保護と、using管理対象の保持では、許可条件が異なる。

### 10.6. 実行時コスト

usingの保護は静的な型・所有権・効果検証で行い、保護用の実行時フラグを必要としない。実行時の管理処理と配置の方針は§16.1にまとめる。

## 11. スレッド能力

### 11.1. 名称と意味

| 能力 | 意味 |
| --- | --- |
| `ThreadTransferable` | 通常の所有権・寿命条件を満たした上で、値の所有権を別スレッドへ安全に移譲できる |
| `ThreadShareable` | 通常の寿命条件を満たした上で、共有参照を複数スレッドから安全に利用できる |

二つは独立した能力であり、一方から他方を自動的には導かない。移譲はCopyや同時アクセスの許可ではない。`Owned` も寿命独立性の保証であり、これらの代わりにはならない。

```kimi
func acceptSync<T>(source: ref/sync/T)
    T is Sealed and ThreadTransferable
    ()
```

この例のSealed条件は、ジェネリックなTがオブジェクトの対象として有効なCoreであることも簡潔に証明するために付けている。ThreadTransferableだけでは、TがSemantics適用済みの型ではないことまでは証明しない。syncの対象を一律にSealedへ限定するという意味ではない。

### 11.2. 証明方針

- 基本型・組み込みSemanticsには、コンパイラが固有の規則を与える。
- 通常の構造体・enum・Tuple等は、実際に保持する値・基底部分・捕捉等の能力から構造的に判定する。
- 型引数が未知なら、ジェネリクスの制約から証明する。
- 通常の空のContract適合宣言だけで、自由に能力を主張できるようにはしない。
- 同名のユーザーContractに組み込みの効果を与えない。
- 手動のunsafe適合宣言を含む例外機構は、別途設計するまでDeferredとする。
- 証明不能は能力があることを意味しない。必要な能力を証明できない使用は拒否する。

破棄、静的状態、外部資源のスレッド制約も無視できない。格納フィールドの検査だけで、外部APIの呼び出し条件まで免除することはない。

### 11.3. 型別の導出条件

TTはThreadTransferable、TSはThreadShareableを表す。`ViewTT(T)`・`ViewTS(T)` は「Tのビューで扱えるすべての実体が、その能力を満たす」という仕様上の判定名であり、新しいソース言語の型やContractではない。導出規則は§11.4に定める。

| 型 | TTの条件 | TSの条件 |
| --- | --- | --- |
| 純粋な組み込みScalar・Unit・不変文字列 | 成立 | 成立 |
| 通常の完全な複合値 | 保持する値・基底・捕捉等がTTであること | 保持する値・基底・捕捉等がTSであること |
| `ref/T` | TがTS | TがTS |
| `uniq/T` | TがTT | TがTS。共有アクセスから排他権限を取り出せないこと |
| `obj/T` | ViewTT(T) | ViewTS(T) |
| `objref/T` | ViewTS(T) | ViewTS(T) |
| `objuniq/T` | ViewTT(T) | ViewTS(T) |
| `rc/T` | 不成立 | 不成立 |
| `arc/T` | ViewTT(T)とViewTS(T) | 同左 |
| `local/T` | 不成立 | 不成立 |
| `sync/T` | 有効に形成できれば成立 | 同左。payloadのTSは要求しない |
| `Weak<S>` | SがTT | SがTS |
| `ReadGuard<T>`・`WriteGuard<T>` | 不成立 | 不成立。localの管理状態はスレッド内に限定 |
| `LockGuard<T>` | 不成立 | ViewTS(T)。共有アクセスの操作と通常の寿命条件も満たすこと |

生ポインタ、型消去された呼び出し環境、外部資源等について、上の一般表だけから自動的に安全性を認めない。対応する公開契約・安全性規則が必要である。

LockGuardは取得したスレッドで破棄する。共有参照からvalueの排他アクセサを呼ぶことはできない。公開された能力をOSプリミティブの都合で変えない。

### 11.4. 継承ビューと型消去

#### 完全な値とビューの保証

完全なowner/Tの能力は§11.2の構造解析で判定する。openなTの完全値がTTでも、その派生型がTTであるとは限らない。したがって、その証明だけからobj/Tなどの能力は導かない。

```text
obj/Animal
    └─ 実体Dogの追加フィールドにlocal/Xが含まれる可能性
         └─ Animal部分の検査だけではDogのTTを証明できない
```

| View Targetの条件 | ViewTT(T)／ViewTS(T)の証明 |
| --- | --- |
| Sealedな完全型T | T自身のTT／TSの証明を使う |
| openな型T | 該当する能力について、検証済みの公開ビュー保証を必要とする |
| 上記を証明できない対象 | 能力を必要とする使用を拒否する |

この判定は型の契約であり、変数に現在入っている実体、参照数、最適化結果によって変わらない。新しい実体の格納、引数渡し、clone、Weakからのupgradeにも同じ契約を適用する。

#### 公開ビュー保証の宣言と継承

組み込み能力 `Kimi.ThreadTransferable`・`Kimi.ThreadShareable` に対する明示的な `Self is ...` は、能力の検証要求とする。openな型では、その型とすべての派生型に要求する公開ビュー保証も宣言する。

```kimi
public open struct Animal
    Self is ThreadTransferable
    // 格納内容・破棄等がTTの条件を満たすことを検証する。
```

基底型にこの保証がある場合、各派生型の定義時にも、追加フィールドを含む完全な型について同じ能力を検証する。保証を取り消す派生型は定義できず、宣言の繰り返しは不要とする。TSも同じ規則である。例えば、このAnimalを継承しlocal/Xを格納する型は、TT保証に違反するため拒否する。

宣言そのものを、その宣言の検証根拠にしてはならない。通常の構造解析と外部資源の契約を検証し終えてから保証を公開する。型制約や既存の `when` 条件を使う場合、保証と派生型への義務にはその条件と型引数の置換を保持し、条件が成立するすべての許容型で検証する。

この継承規則は二つの組み込み能力に限定し、一般のContractやCopyの継承規則を変更しない。構造解析だけで自動導出されたopen型の能力には、派生型を拘束する公開保証を付けない。別コンパイルの成果物にも保証・条件・検証依存を保存し、基底や格納型の変更時に再検証する。

#### syncの形成とビュー変更

`sync/T` は、有効なView TargetであるTについて、TのTTとViewTT(T)を証明できる場合に形成できる。Sealedなら同じ構造証明で両方を満たし、openなら上記の公開保証を必要とする。単にジェネリックな `T is ThreadTransferable` があるだけでは、TのCore適格性やopenなビューの保証までは証明しない。

`sync/Dog` から `sync/Animal` への変換にもAnimalの公開TT保証を必要とする。保証がなければ、現在のDogがTTでも変換を拒否する。objやarc等では、変換先の形成条件を満たす限り、任意のTT・TSを失うビュー変更を許可する。その後の転送・共有は変換先の型だけで再判定する。

型の絞り込みやチェック付きキャストでも同じ規則を使う。変換先がSealedなどで新たに証明できる場合を除き、失った能力を自動的に復元しない。どの場合もガードのLoanや型消去時のOwned条件は維持する。未導入のruntime Contract Viewを、この保証の追加だけで有効にしない。

### 11.5. 再帰的な型の証明

TT・TSの組み込み構造解析では、再帰型を固定点で扱う。例えば、SealedなNodeが `Option<Weak<sync/Node>>` を保持する場合、次の依存が生じる。

```text
NodeのTT
  → Weak<sync/Node>のTT
    → sync/Nodeの形成条件
      → NodeのTT
```

最初に型の骨格と能力の依存関係を作り、相互依存する判定を一つのグループにまとめる。組み込みの正の構造条件について、グループ外の前提をすべて証明でき、内部に違反がなければグループ全体を認める。これは、その構造条件の最大固定点を求める規則である。

local・rcなどの不成立条件は依存先へ伝播する。外部資源の安全性など、未証明の外部条件があるグループを成功扱いしない。条件付き保証の前提、一般のContract適合、自己正当化する宣言をこの構造上の循環へ取り込んではならない。

公開ビュー保証の検証も、宣言の存在を確認した上で、その実際の構造条件へ展開する。宣言があるだけでは成功せず、追加フィールド等の検査を含めて固定点を確定する。

型形成と能力証明は完了時にまとめて確定し、途中の仮定を公開しない。無限のインライン配置は既存規則で拒否し、異なる型引数を際限なく生成する展開は有限の再帰グループとは区別する。確定したグループを共有・再利用し、同じ解析を繰り返さない。

### 11.6. 寿命と実行機能の境界

TT・TSが成立しても、借用先が短命なら転送・共有できない。特に、ガードから得た参照のガード依存、payload中の外部依存、必要なスレッド内管理の依存を維持する。

本書は能力名・証明方針・syncの基本的な同期契約を定義するが、ソース言語のスレッド生成・join・スケジューラ等を追加しない。既存の未設計の並行実行機能を、型名の追加だけで使用可能にしたものとみなしてはならない。

## 12. Weakと循環

### 12.1. 有効な対象

既存のWeak対象へlocal・syncを追加する。

```text
Weak<S>
├─ Weak<rc/T>
├─ Weak<arc/T>
├─ Weak<local/T>
└─ Weak<sync/T>
```

`Weak<obj/T>`、オブジェクト借用、ガード、予約中のasyncを対象にする形は導入しない。

### 12.2. 操作

既存の `Kimi.Intrinsics.downgrade`・`upgrade`・`clone` の管理方式を拡張する。Weak自身はNon-Copyで、明示的なWeakのcloneは引き続き可能である。ガードのclone禁止とは異なる。

```kimi
var strong = A.init()@local
var weak = Kimi.Intrinsics.downgrade(strong@ref/local/A)

match Kimi.Intrinsics.upgrade(weak@ref)
    .Some(var restored)
        using var guard = restored.write()
            guard.value.name = "restored"
    .None => ()
```

upgradeは有効なstrong所有者を確保し、`Option<S>` を返す。成功しただけでは、共有借用・排他借用・ロックのいずれも取得していない。localの借用競合やsyncのロック競合は、その後の取得APIで扱う。

ビューと所有方式、外部のOrigin依存を維持する。Weakが期限切れになる可能性を理由に、必要な寿命依存を消去しない。最終strong解放との競合、非復活、Weak管理領域の寿命は既存の契約に従う。

### 12.3. 導入しないもの

- local・sync専用の循環構築APIは導入しない。
- strong参照の循環を自動回収する機構は導入しない。
- 既存rc・arcの循環構築APIの廃止は、本議論の決定に含まれない。

必要な循環の切断にはWeakを使う。通常構築後に可変フィールドへWeakを設定する等の方法も、通常の型・初期化・借用規則を満たす必要がある。

## 13. 予約とDeferred

### 13.1. asyncの予約

`async` はSemanticsの文脈だけで予約する（§2.1）。`async/T`・`x@async`・`x@async/T`・Semantics制約の `s is async` は通常の名前として解決せず、未対応の診断を出す。型の形成・生成、所有方式変換、カテゴリへの追加、Weak対象化は許可しない。他の文脈では通常の名前として使える。

将来の用途は、共有所有と非同期Mutexを組み合わせたオブジェクト管理である。現在有効な機能として説明しない。

### 13.2. 将来方針として保存する事項

以下は議論で選んだ将来の方向であり、現在の実行可能な仕様には含めない。

| 項目 | 将来方針 |
| --- | --- |
| 非同期取得 | `await source.lock()` がタスクの待機を行う |
| 非同期ガード | `AsyncLockGuard<T>` が `objuniq/T` を提供する |
| awaitをまたぐ保持 | ReadGuard・WriteGuard・同期LockGuardでは禁止、AsyncLockGuardでは許可 |
| 取得待ちのキャンセル | 待機登録を解除する |
| 取得後のタスク破棄 | ローカルの後始末とガードの解放を行う |
| 変更の扱い | ガードの解放は、既に行った変更のrollbackを意味しない |

```text
将来の構文イメージ。現在は使用不可:

using var guard = await shared.lock()
    await useAsync(guard.value)
```

ガードを構造体やクロージャへ格納した場合も、await禁止を迂回できないようにする必要がある。スレッド間転送能力と、awaitをまたげる能力は別に検査する。

### 13.3. 今後設計する範囲

- 非同期計算・タスクの型、状態機械、実行モデル。
- awaitの型規則と中断時のLoan保持。
- キャンセルの開始点、伝播、破棄順序、待機登録との競合。
- 非同期取得が成功する瞬間とキャンセルの競合。
- 非同期ガードの転送・共有能力。
- 解放自体が非同期になるリソースの扱い。

`using var guard = await ...` のawaitは取得を待つ操作である。C#風の `await using` に相当する「非同期の解放」は別の機能であり、本書では導入しない。

## 14. 通しの例

### 14.1. localの読み書き

```kimi
struct Item
    public var name: string

    public init(name: string)
        self.name = name

func inspectName(name: ref/string)
    () // 実際の利用では、nameを読み取って処理する。

var first = Item.init("initial")@local
var second = first.clone()

using var guard = first.read()
    let name = guard.value.name@ref
    inspectName(name)

using var guard = second.write()
    guard.value.name = "changed"

using var guard = first.read()
    inspectName(guard.value.name@ref) // changedが見える。
```

stringはNon-Copyなので、読み取り借用から値として取り出さない。ここでは参照を渡し、文字列の複製や追加の確保を行わない。

### 14.2. 別ハンドルでも同じ借用状態

```kimi
var first = Item.init("initial")@local
var second = first.clone()

using var reader = first.read()
    match second.tryWrite()
        .None => () // 同じ実体が共有借用中なので、こちらになる。
        .Some(var unexpected)
            using var writer = unexpected
                writer.value.name = "unexpected"
```

上のtryWriteをwriteに置き換えると、借用競合によってAbortする。readerの最終使用が早くても、破棄前なら借用状態は解除されない。

### 14.3. syncの複数取得とexit

```kimi
var first = Item.init("first")@sync
var second = Item.init("second")@sync

work: using var left = first.lock()
using var right = second.lock()
    if shouldSkip()
        exit to work

    left.value.name = "left"
    right.value.name = "right"
// right、leftの順にunlock済み。
```

Itemの構造と公開契約からThreadTransferableを証明できるものとする。この例は実際のスレッドを生成するものではない。

## 15. 診断と検証の観点

### 15.1. 診断に含める情報

- どのusing束縛が保護対象なのか。
- どの操作がMove・全体更新・不完全化に該当するのか。
- どの引数・receiver・戻り値の対応を通って、その対象へ到達したのか。
- ガード由来の参照が、どの解除・破棄点を越えようとしているのか。
- 型形成やビュー変更で、どのスレッド能力の証明が不足しているのか。
- 予約された非同期機能の使用である場合、その機能がDeferredであること。

### 15.2. 仕様統合時に確認する代表例

| 観点 | 確認内容 |
| --- | --- |
| local共有借用 | 複数readは成功し、writeとは競合する |
| local排他借用 | 生存中はread/writeとも競合する |
| clone・ビュー変更 | 同じ実体の借用状態・ロックを共有する |
| try系 | 競合をNoneで返し、未取得のガードを破棄しない |
| カウンタ上限 | read・tryReadともoverflow前にAbortし、競合のNoneと区別する |
| 要素取得 | local/syncの要素をスロット借用として取得し、ガードを迂回しない |
| 標準API | payloadの同名メンバーへ解釈し直さず、Originと取得元のLoanを保持する |
| ガードのLoan | valueの参照が解除後へ逃げない |
| using保護 | 直接・別名・関数・アクセサ・特殊化経由の全体更新を拒否する |
| Sealed | payload投影は許せても、using対象自身の保護は外れない |
| 複数ヘッダ | 上から取得、逆順に解放。中途退出では取得済みのみ解放する |
| exit | 縦連結全体と、インデントされた入れ子を区別する |
| Scope Exit | return/continue/yield/deferとの後始末順序を維持する |
| スレッド能力 | local/rcを含む値を、syncや型消去によって不正に転送可能にしない |
| 公開ビュー保証 | 保証違反の派生型を拒否し、保証のないopen型の構造証明をビューへ流用しない |
| 再帰型 | Weak<sync/Node>の正の構造循環を解決し、不成立・証明不能の条件を成功にしない |
| Weak | upgrade成功と、ガード取得成功を区別する |
| 文脈キーワード | local/sync/asyncは型の接頭辞・Semantics制約・明示変換先でのみ認識し、通常の名前や除算式を妨げない |
| async | Semanticsの文脈では未対応の診断を出し、通常の名前としての使用は許可する。非同期機能はDeferredとする |

本節は今後の検証観点であり、テスト実施結果ではない。

## 16. 実装方針と仕様への統合

### 16.1. 論理的な管理規則と物理配置

実体ごとに、所有・Weak・借用またはロックの管理状態を一組持つ。cloneやビュー変更で別の管理状態を作らない。取得・失敗・解除・最終破棄の意味は本書で固定し、具体的な配置やOS機構はDeferredとする。

#### 不要な確保と同期を避ける

- 標準ガードは小さな所有値として扱い、取得ごとのガード用ヒープ確保を不要にする。具体的なサイズは約束しない。
- ガード取得・破棄は取得元スロットへの借用で寿命を保証し、strong数を増減しない。
- cloneとWeakのupgradeは所有管理だけを行い、payloadのMutexや実行時借用を取得しない。atomicな管理処理まで不要になるという意味ではない。
- 直接生成は最終形式へまとめて構築できるようにし、中間のobj用確保・破棄を省く。評価・初期化・公開・破棄の観測可能な順序は保つ。
- 既存objからの移譲は、payloadの再配置や再コンストラクションを行わず、必要な管理状態を追加してから公開する。

管理状態への参照と通常の値のMoveでガードを表現できるようにし、ガード移動時のアドレス修正を必要としない設計を優先する。usingの静的な保護と、localの実行時借用検査・syncのMutex・参照カウントは別の処理である。

#### ABIとの境界

既存のobj・rc・arcの物理レイアウトは、本書だけでは変更しない。local・syncと新しいガードの配置、管理領域の一体確保・別確保、待機機構は実装プロファイルで設計する。既存の固定ヘッダに新しい状態が収まるとは仮定しない。

既存objからの移譲では、同一性とpayloadの位置を維持するために管理領域の追加確保が必要になる場合がある。直接生成についても、総確保回数が必ず一回になるというABI上の保証はしない。初期実装は共通の管理方式を優先し、測定後に内部配置を最適化する。既存ABIの変更が必要なら、別の明示的な仕様変更として扱う。

### 16.2. 現行仕様との統合先

| 統合先 | 反映する内容 |
| --- | --- |
| [§2 字句・ソース構造](../../spec/02-source-and-lexical-structure.md) | using、local/sync/asyncの文脈キーワード化、Semantics文脈限定のasync予約、縦連結ヘッダと共通Body |
| [§3 型と値](../../spec/03-types-and-values.md) | local/sync、Semanticsカテゴリ、ガードの所有、Weak対象 |
| [§4 配列・Slice](../../spec/04-arrays-indexing-and-slices.md) | SharedReadResultのlocal/syncスロット借用、ジェネリックな取得結果 |
| [§6 宣言・継承](../../spec/06-declarations-and-containers.md) | open型の公開スレッド能力保証と派生型の検証 |
| [§7 関数](../../spec/07-functions-and-callable-values.md) | 保護対象を借用する呼び出し、関数値の保証保持 |
| [§8 ジェネリクス・Contract](../../spec/08-generics-constraints-and-contracts.md) | TT/TSとビュー保証、再帰的な構造証明、Semantics分解、効果保証 |
| [§10 適応と推論](../../spec/10-overload-resolution-and-inference.md) | ガードとハンドルのreceiver適応、禁止するpayload迂回 |
| [§11 プロパティ](../../spec/11-properties.md) | guard.valueのreceiver・戻り値・OriginとReborrow |
| [§12 式とオブジェクト呼び出し](../../spec/12-expressions.md) | 既存効果解析の共通化とusing用の保護判定 |
| [§13 Adaptationと所有API](../../spec/13-operators-and-assignment.md) | @obj生成、objからの移譲、省略形、ビュー変更、Weak操作 |
| [§14 制御フロー](../../spec/14-control-flow.md) | using文、縦連結、exit対象、初期化途中の制御移動 |
| [§15 所有権・寿命](../../spec/15-ownership-and-lifetime-analysis.md) | 保護対象、ガードのLoan、別名・結果・捕捉の伝播 |
| [§16 破棄](../../spec/16-scope-exit-and-destruction.md) | ガード解除、管理対象の逆順破棄、通常退出とAbortの区別 |
| [§18 成果物](../../spec/18-modules-and-dependencies.md) | 公開効果情報と能力証明の保存・検証 |
| [§21 ランタイム](../../spec/21-layout-runtime-and-code-generation.md) | 管理状態、overflow、ロック同期、§16.1に基づく新しい表現の設計 |
| [§22 Kimi・実行](../../spec/22-core-execution-and-foreign-functions.md) | 標準API・ガード・能力Contractの宣言識別情報 |
| [付録D Deferred](../../spec/appendices/D-deferred-features.md) | async、await、キャンセル、手動unsafe適合等の境界 |

現行SPECで禁止されているobjからrc/arcへの変換等は、本原案を採用する際に明示的に更新する。既存のobjectカテゴリを受け取る取得・配列要素・メンバー適応等も点検し、カテゴリ拡張だけでlocal/syncの直接payload借用が可能にならないようにする。

本原案の作成だけで、SPEC・STATUS・実装の対応状況を変更したとはみなさない。
