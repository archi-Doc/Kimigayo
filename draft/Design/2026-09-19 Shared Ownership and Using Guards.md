# 共有所有・ガード・using

- 日付: 2026-09-19（2026-09-24 改訂）
- 状態: 仕様への取り込み前の設計案。実装済みの機能や検証済みの ABI を示すものではない。
- 範囲: `local`・`sync` Semantics、ガード、`@` による生成と移管、参照カウントと Weak、循環構築、`using`、スレッド能力。

## 1. 位置付け

本書で定める事項は `SPEC.md` とその参照先より優先する。それ以外は現行仕様を適用する。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。

本書は、取り込み予定の次の二つの変更案を前提とする。「本書 §」は本書の節、略称付きの「IER §」「OP §」はそれぞれの文書の節、それ以外の「§」は現行仕様の節を指す。

| 略称 | 文書 | 本書で使う内容 |
| --- | --- | --- |
| IER | `draft/Changes/2026-09-23 Implicit Exclusive Receiver.md` | 受信者式の暗黙の取得、受信者の形の統一、命名規約 |
| OP | `draft/Changes/2026-09-23 Object Payload.md` | `ObjectPayload`、Semantics の許容集合、オブジェクト対象 |

目的は次の三つである。

- 共有したオブジェクトを、実行時の借用検査（`local`）またはロック（`sync`）を通して安全に変更できるようにする。
- オブジェクトの生成と所有モードの移管を、`@` の一つの綴りにまとめる。
- ガードの解放を通常の所有権と寿命の規則だけで扱い、専用の保護規則を作らない。

本書 §3〜§8 を段階 A、§9〜§10（`sync` とスレッド能力）を段階 B として取り込む（本書 §12.1）。

```kimi
var shared = Item.init("initial")@local        // 生成
var other = Kimi.Intrinsics.clone(shared@ref)  // 強参照の複製
using writer = shared.write()                  // 排他の取得。本体の終わりで解放する
    writer.valueUniq.name = "changed"
```

## 2. 用語

- **ハンドル**: `obj`・`rc`・`arc`・`local`・`sync` の値。オブジェクトを所有する。
- **スロット**: ハンドルを保持する storage。`ref/local/T` はスロットの借用であり、ペイロードの借用ではない。
- **ガード**: `ReadGuard`・`WriteGuard` の値。一つの取得を解放する責任を持つ。
- **取得**: ハンドル操作（本書 §5.2）が、実行時の借用またはロックを得ること。成功したときだけガードができる。
- **生成**: 完全な値から新しいオブジェクトを作る `@` の操作（本書 §4.2）。
- **移管**: `obj` の所有を、同じオブジェクトのまま counted のモードへ移す `@` の操作（本書 §4.2）。
- **表記の規約**: Semantics は常に `local/T` または「local ハンドル」と書き、束縛は「ローカル変数」と書く。`local` はスタック確保もスレッドローカル記憶域も意味しない。

## 3. Semantics と区分

### 3.1. 役割

| 役割 | 型・構文 | 意味 |
| --- | --- | --- |
| 排他所有 | `obj/T` | 所有の責任を一つ持つ。ペイロードに共有・排他でアクセスできる |
| 共有所有 | `rc/T`・`arc/T` | 非アトミック／アトミックの参照カウント。ペイロードは共有アクセスだけ |
| ガード付き共有所有 | `local/T` | 非アトミックの参照カウントと実行時の借用検査。競合したら待たずに Abort する |
| ガード付き共有所有 | `sync/T` | アトミックの参照カウントと再入不可の Mutex。競合したら待つ（段階 B） |
| オブジェクト借用 | `objref/T`・`objuniq/T` | View への共有／排他アクセス。所有しない |
| 取得の寿命 | `ReadGuard<s/T>`・`WriteGuard<s/T>` | 取得を解放する責任を持つ所有値 |
| スコープ | `using x = e Body` | 束縛で始まる `do`（本書 §7） |

- `local`・`sync` は組み込みの Semantics であり、ライブラリの別名ではない。`<s/T>` の分解と Semantics の要件に参加する。
- `T` はオブジェクト対象でなければならない（OP §5）。本書は実行時 Contract View を有効にしない。
- `local`・`sync`・`async` は、待ち方で区別する系列である。`local` は待たない、`sync` はスレッド間で待つ、`async` は将来 await で待つ（本書 §14）。

### 3.2. 文脈キーワード

`local`・`sync`・`async` は、Semantics の前置、Semantics の要件、`@` の指定の位置でだけ認識する。`let local = 1`、`func sync() => ()`、`x.async`、`local / count` は通常の名前として扱う。`async` は Semantics の文脈で予約し、`async/T`・`x@async`・`s is async` を拒否する。

### 3.3. 区分

| 区分 | 要素 |
| --- | --- |
| `value` | owner |
| `valueborrow` | ref, uniq |
| `objectborrow` | objref, objuniq |
| `borrow` | ref, uniq, objref, objuniq |
| `object` | obj, rc, arc, local, sync |
| `guarded` | local, sync |
| `counted` | rc, arc, local, sync |
| `owning` | owner, obj, rc, arc, local, sync |
| `reference` | owner 以外のすべて（unsafe を含む） |

- `object` はハンドル全体を表す。ペイロードを直接借用できるハンドルは `object and not guarded` と書き、新しい区分名は作らない。
- `guarded` は共通の `write`・`tryWrite` を与える。`read`・`tryRead` は `local` だけが持つ。
- `counted` は強参照の複製と Weak を与える。Weak 自体は owner であり、counted ではない。
- OP の許容集合の全体は 11 種（owner, ref, uniq, obj, rc, arc, local, sync, objref, objuniq, unsafe）になる。OP のオブジェクト系（`object` ∪ `objectborrow`）は `local`・`sync` を含む。
- 現行 §3.3 の「`counted` という区分はない」を削る。`object`・`owning`・`reference` が広がるので、`s is object` の本体でペイロードを直接借用しているコードは `and not guarded` を加える。

### 3.4. 複製

すべてのハンドルとガードは Non-Copy である。通常の取得は Move であり、カウントを変えない。強参照を複製できるのは counted だけで、`Kimi.Intrinsics.clone` を使う（本書 §6.1）。`obj` とガードは複製できない。

ハンドルに `.clone()` のようなメンバーは置かない。`rc`・`arc` のメンバー検索はペイロードに進むので、名前が衝突するからである。

## 4. `@` による生成と移管

### 4.1. 取得の共通規則

> 所有 Semantics を指定する `@` は、オペランドを転送で取得してから、一つの操作を行う。

- **対象の指定**: `@owner`・`@obj`・`@rc`・`@arc`・`@local`・`@sync`。`/T` 付きの形と、総称の `@s` を含む。
- **取得**: place は Copy 型でも Moved になり、一時値はそのまま渡る（§13.5.3 の転送）。
- **操作**: オペランドの静的な型と指定から、本書 §4.2 の表の一つを選ぶ。失敗しても別の操作は選ばない。
- Copy の元を残したいときは、先に Copy を明示する（`n@i32@rc`）。

```kimi
var count = 1
let kept = count@i32@rc    // Copy してから生成する。count は使える
let taken = count@rc       // count は Moved
```

### 4.2. 操作

M は `object` の要素、C は `counted` の要素とする。

| オペランド | 指定 | 操作 | 操作によるヒープ確保 |
| --- | --- | --- | --- |
| 同じ正規化済みの完全型 | オペランドと同じ Semantics の指定（`@owner` を含む） | 転送だけ（現行） | なし |
| `M/S` の所有ハンドル | `@M/V` | 同じモードの upcast（§13.5.7 に `local`・`sync` の行を加える） | なし |
| 完全な owner 値 `T` | `@M`・`@M/T` | **生成**: 新しいオブジェクトを作る | オブジェクトと管理領域を確保しうる |
| `obj/T` | `@C`・`@C/T` | **移管**: 同じオブジェクトの所有を C へ移す | 管理領域を確保しうる。ペイロードは作り直さない |

**生成**

- `T is ObjectPayload` を要する（OP §4）。具体型は直接判定し、型が未確定なら宣言された前提を要する。open な型も生成できる。
- 取得した値をペイロードへ Move する。構築子・accessor・`deinit` は繰り返さない。一律の Owned 要件はなく、外部の依存は保たれる。
- `value@local` の所有の意味は `(value@obj)@local` と同じだが、中間のオブジェクトも二回の確保も要らない。

**移管**

- ハンドルを消費し、Dynamic Type・同一性・ペイロードのアドレス・外部の依存を保つ。ペイロードを複製も再配置もしない。
- 管理状態は公開の前に初期化する。競合する Loan があれば、通常の Move の規則で拒否する。
- 既存のハンドルの操作なので、`ObjectPayload` を再証明しない（OP §5.2）。移管先に固有の条件だけを加える（`sync` なら TT(T)、本書 §9.3）。

**共通**

- 生成と移管は `T` を保つ。View の変更と一つの `@` では組み合わせない（`dog@local@local/Animal` と書く）。
- counted のモードの間、および counted から `obj` への変換はない。強参照が一つでも同じである。借用からは所有ハンドルを作らない。
- `@` は利用者のコードを呼ばず、変換の連鎖を探さず、強参照を暗黙に複製しない。
- 必要な確保に失敗したら、結果を公開する前に Abort する。完了した効果と Move は戻さない。確保の時点は本書 §8.4 に従う。
- 現行 §13.5 の「`@` は資源を取得しない」を、「生成と移管だけが確保しうる」に改める。
- `Kimi.Intrinsics.makeObj`・`makeRc`・`makeArc` を削除する。`makeLocal`・`makeSync` は設けない。関数値が要るときは closure で包む（`func (value: Item) => value@rc`）。

### 4.3. 総称の Semantics 指定

> 総称の `@s` は、許容集合のすべての要素で同じ種類の操作を選ばなければならない。

- 種類は、転送だけ・upcast・生成・移管の四つである。種類が混ざる場合は定義エラーとする。
- 生成には、許容集合 ⊆ `object` と `T is ObjectPayload` を要する。
- これにより、ヒープ確保の有無を署名から読める。

### 4.4. View

- `local`・`sync` のハンドルは、Supports、upcast、実行時の `is`、checked cast（`Result<s/B, s/A>`）、ペイロードの消去（Owned）に参加する。対象の形成は OP §5 に従う。
- ガードから得た `objref`・`objuniq` は、既存のオブジェクト借用の upcast を使う。
- スロットの借用（`ref/local/T`・`uniq/local/T` など）には、ペイロードの upcast もコンテナの共変もない。
- 排他アクセスは、実体が正確に T であることを証明しない。完全なペイロードへの `@ref/T`・`@uniq/T` の投影は、現行 §13.5.5.1 のとおり Sealed を要する。
- 型テストと View の変更は、ペイロードへのアクセスを与えない。

```kimi
// Dog は Animal の派生とする。
var original = Item.init("initial")@obj
var first = original@local        // 移管：original は Moved。同一性とアドレスは変わらない
var dog = Dog.init()@local        // 生成
var animal = dog@local/Animal     // upcast：同じオブジェクト。dog は Moved
```

## 5. ガード付きアクセス

### 5.1. 取得の状態と失敗

| モードと状態 | `read`・`tryRead` | `write`・`tryWrite` |
| --- | --- | --- |
| `local`、未借用 | 成功 | 成功 |
| `local`、共有借用中 | 成功（共有を追加） | 競合 |
| `local`、排他借用中 | 競合 | 競合 |
| `sync`、未ロック | — | 成功 |
| `sync`、ロック中 | — | 競合 |

| 状況 | 通常の操作 | try の操作 |
| --- | --- | --- |
| `local` の競合 | 待たずに Abort | `None` |
| `sync` の競合 | 待つ | 待たずに `None` |
| 共有借用数の上限超過 | 結果を公開する前に Abort | 同じ（`None` ではない） |

- 成功だけがガードを作る。競合は、取得も解放の責任も作らない。
- try の操作が報告するのは取得の競合だけである。上限超過や必要な資源の不足からは回復しない。
- 上限と検査の時点は本書 §8.3 に従う。カウントは折り返さず、飽和して成功することもない。

### 5.2. ハンドル操作

`local/T`・`sync/T` のハンドル型は、組み込みのメンバーを四つだけ持つ。Self はハンドルの完全型 `s/T` である。

```kimi
func read(self: ref/Self) -> ReadGuard<s/T>                // s is local
func tryRead(self: ref/Self) -> Option<ReadGuard<s/T>>     // s is local
func write(self: ref/Self) -> WriteGuard<s/T>              // s is guarded
func tryWrite(self: ref/Self) -> Option<WriteGuard<s/T>>   // s is guarded
```

- **Origin**: 結果の `source` は受信者の Origin になる（§15.4.3 の規則 1）。ガードはスロットの共有 Loan を保つ。
- **受信者の取得**（IER §3.2 との接続）
  - IER の値の種類の「所有 handle」に `local`・`sync` を含める。
  - 受信者の経路は、選ばれた宣言の Self で決まる。Self が入力の View Target（ペイロードのメンバー）ならオブジェクト系の経路をとる。Self が入力の完全型（ハンドル操作）なら値型の経路をとり、`p@ref` でスロットを共有借用する。
  - `obj`・`rc`・`arc` はハンドル操作を持たないので、既存の意味は変わらない。
  - ハンドルの place（`let` を含む）、`ref/s/T`、`uniq/s/T` から取得できる。取得はハンドルを Move しない。
- **メンバー検索**
  - guarded のハンドルでは、この四つだけが見つかる。ペイロードのメンバーは見つからず、失敗してもペイロードを検索し直さない。
  - 解決は標準の宣言の同一性で行う。同名のユーザー宣言は特権を得ない。
  - `@objref`、ペイロードの投影、メンバーの転送、cast、絞り込みは、ガードを迂回できない。ペイロードへのアクセスには常にガードが要る。
- **綴り**: メンバーの形だけとし、関数形（`Kimi.Intrinsics.read` など）は設けない。`lock`・`tryLock`・`LockGuard` は導入しない。
- **総称**: `s is guarded` の本体では `write`・`tryWrite` を、`s is local` の本体ではさらに `read`・`tryRead` を使える。`write` が待たないことは仮定できない。

### 5.3. ガード型

```kimi
// 組み込み宣言の概形。compiler-managed の Non-Copy struct
struct ReadGuard<s/T> {source}
    s is local
    Self is not ObjectPayload
    public computed value: objref/T
        get(self: ref/Self) -> objref/T during self

struct WriteGuard<s/T> {source}
    s is guarded
    Self is not ObjectPayload
    public computed value: objref/T
        get(self: ref/Self) -> objref/T during self
    public computed valueUniq: objuniq/T
        get(self: uniq/Self) -> objuniq/T during self
```

- **`source`**: 安全な参照を格納しない phantom slot（§15.3.5）である。組み込みのメタデータとして、共変、Loan 要件は `ref`（ソースのスロットへの共有 Loan）とする。
- **T の変性**: `ReadGuard` は共変、`WriteGuard` は不変とする。
- **命名**: 共有版を `value`、排他版を `valueUniq` とする（IER §4）。IER により `writer.valueUniq` は受信者に `writer@uniq` を補うので、ガードの束縛は書き込み可能（`var` または一時値）でなければならない。
- **形成**: 型の制約により、`ReadGuard<sync/T>` は形成できない。OP §9.2 の Loan に縛られたアダプターと同じく、オブジェクト化を禁止する。
- **構築と解放**: ガードを作るのはハンドル操作だけである。公開の構築子、可変の管理フィールド、`deinit` の追加、複製、手動の解放 API はない。通常の破棄が、そのとき持っている取得を一度だけ解放する。
- **子の借用**: アクセサは管理状態を変えず、ペイロードへのアクセスを貸すだけである。子の Loan が終わっても取得は解放されない。

### 5.4. 寿命と解放

```text
ソースのスロット -> ガード -> ペイロードの子借用 -> フィールド・再借用・捕捉
```

- ガードはソースのスロットを借用し、強参照を追加しない。ガードの生存中、ソースは Move も置き換えもできない。
- 依存と Loan の由来は、View の変更・cast・戻り値・保存・捕捉を通して保たれる。別の強参照がオブジェクトを生かしていても、解放済みのガードの子の参照は有効にならない。
- ガードは通常の Move・保存・戻り値・Origin・Loan の規則に従う。スレッドの規則は本書 §9 のとおりである。
- 解放は、責任を持つガードの通常の破棄で起こる（合法な置き換えを含む）。最後の使用で解放が早まることはない（§16.2.1）。Abort と発散は通常のスコープ終了に従う。
- 一時値のソースは通常の一時値の寿命に従い、ガードはそれより長く生きられない。`upgrade` の結果などは、先に束縛してから取得する。
- 一時値のガードは式の終わりまで生きる。

```kimi
inspectName(first.read().value.name)   // 一時値のガードは、この文の終わりで解放される
```

### 5.5. 要素の読み取り

現行 §4.6.6 の SharedReadResult を、次の原則で定義し直す。

| 要素の完全型 | 結果 |
| --- | --- |
| Copy 型 | 値の Copy |
| `uniq/T`・`objuniq/T` | 共有の再借用（`ref/T`・`objref/T`） |
| `object and not guarded` のハンドル | `objref/T`（カウントは変えない） |
| それ以外の Non-Copy | 要素のスロットの共有借用 `ref/E` |

- 現行の表と同じ結果を与える。guarded のハンドルは最後の行に入り（`ref/local/T`）、専用の行は要らない。
- guarded の要素の読み取りは、ペイロードの借用も、カウントの増加も、取得もしない。
- 総称の本体では、すべての場合を検証する。

```kimi
// items: Slice<local/Item>
let handle = items[0]          // ref/local/Item
using guard = handle.read()
    inspectName(guard.value.name)
```

## 6. 参照カウントと Weak

### 6.1. 操作

S は counted の完全なハンドル型とする。現行 §13.5.8〜§13.5.9 の操作を、`rc`・`arc` から counted 全体に広げる。

| 操作 | 入力 | 結果 |
| --- | --- | --- |
| 強参照の `clone` | `ref/S` | `S` |
| `downgrade` | `ref/S` | `Weak<S>` |
| `upgrade` | `ref/Weak<S>` | `Option<S>` |
| Weak の `clone` | `ref/Weak<S>` | `Weak<S>` |

- **スロットの借用**: ハンドルの place への `@ref` は、スロットの共有借用 `ref/s/T` を作る（§13.5.5.2 の所有 place の行。`@ref/local/T` と同じ）。実引数ではハンドルの外側に層を足す借用を暗黙に行わないので、明示する（§10.2）。
- **Weak の形成**: `Weak<S>` は、S の外側の Semantics が counted であることを要する。総称では許容集合 ⊆ `counted` とする（OP §5.3 の `rc`・`arc` を counted に広げる）。
- `clone` はペイロードを複製せず、利用者のコードを呼ばず、取得もしない。`upgrade` の成功で得られるのは強参照だけで、guarded ではガードが別に要る。
- `clone`・`upgrade` は管理領域を確保しない。最初の `downgrade` は side table を確保しうる。
- 最後の強参照との競合、ゼロからの復活の禁止、動的型全体の破棄、元の領域の解放、Weak の表の寿命は、現行 §13.5.8〜9 と §21.2.3 に従う。

### 6.2. 循環構築

```kimi
func makeCyclic<c/T, F>(build: F) -> c/T
    c is counted
    T is Owned and ObjectPayload
    F is Callable<owner, (Weak<c/T>) -> T>
```

- `makeRcCyclic`・`makeArcCyclic` を置き換える。`c/T` は、`build` の引数型 `Weak<c/T>` から推論する。
- **手順**
  1. 公開前のオブジェクト領域と side table を確保する。表は構築ガードと、ビルダーに渡す Weak を一つ持つ。モードの管理状態を初期化する。
  2. `build` を呼び出し元のスレッドで一度だけ呼び、Weak を値で渡す。Building の間、`upgrade` は `None` を返し、強参照・ペイロード・取得は得られない。
  3. ビルダーの正常な結果と呼び出しの後始末が終わってから、完全な `T` をペイロードへ Move する。
  4. Alive と強参照一つを一度だけ公開する。`local` は未借用、`sync` は未ロックで始まる。`arc`・`sync` の公開の順序付けは現行どおりである。
- `F` は通常どおり取得し、Copy・Owned・TT を要さない。`T is Owned` は、ビルダーが得た依存が公開済みの Weak を通して逃げることを防ぐ。
- 確保の失敗とカウントの上限超過は、公開の前に Abort する。ビルダーが Abort または発散すると公開されず、巻き戻しと後始末は保証しない。
- 強参照の循環は回収しないので、Weak で断つ。可変の Optional フィールドは、通常の生成の後に `write` を通して設定できる。必須の自己 Weak や不変のフィールドには循環構築を使う。

## 7. using

### 7.1. 構文

```text
DoExpression := [Label ":"] "do" Body
              | [Label ":"] "using" Name [":" Type] "=" Expression Body
```

- `using` は `do` の一形態である。`do` を参照する規則（Completion、Body の結果、ラベル、§14.5.2 の遷移の表）を、そのまま `using` に適用する。
- `var` のローカル変数を一つだけ導入する。`let`／`var` の指定、本体のない形、複数の束縛、同じ字下げでの連結はない。複数の取得は入れ子で書く。
- **認識**: 式の先頭（ラベルの後を含む）で、`using` の後に同じ物理行で Name と `=` または `:` が続くときに認識する。コメントは読み飛ばす。認識したら確定し、エラーになっても別の解釈に戻らない。`using = 1`、`using(x)`、`x.using()` は通常の名前である。
- 本体を持つ式を初期化子に書くときは、括弧で囲む。
- 束縛が `var` なのは、排他の受信者（`valueUniq`）や `replace` が書き込み可能な storage を要するからである（IER §3.1）。

### 7.2. 意味

`L: using x: T = e Body` は、本体のスコープが `var x: T = e` で始まる `do` と同じである。ただし次の点を保つ。

1. `T` と `e` は外側の環境で解決・評価する。`x`、`L`、本体の宣言は `e` から見えない。初期化子の一時値は通常のローカル変数の初期化子と同じ時点で終わり、寿命は延びない。
2. `x` は本体のスコープの先頭、文と `defer` の前に入る。重複と隠蔽は通常の規則に従う。`L` は本体の中だけで有効である（§14.4）。
3. 結果の規則は元の本体の形に適用し、単項目の本体は値を返す。展開は字句の書き換えではない。

初期化子から外側の有効な目標へ遷移できる。初期化が完了しなければ、束縛はできない。

### 7.3. 結果と所有

- 結果の規則は `do` と同じである。値を使う単項目の本体は値を返し、字下げした本体は終わりに達すると Unit を返す。Unit 以外の結果は名前付き `exit` で返す。`using` は `return`・`yield`・`continue` の目標にも、探索の障壁にもならない。
- 束縛は通常の `var` であり、Move、代入、部分 Move、借用、`replace`・`exchange`・`swap` を許す。`using` に固有の保護はなく、最初の値を最後に解放する保証もない。
- 結果の受け渡しより前に破棄されるガードに依存する参照は拒否する。独立した値と、転送したガード（`=> guard@move`）は許す。
- 束縛全体の Move や代入への警告は任意とし、受理の可否を変えない。

## 8. 実装と性能

### 8.1. 管理状態と最適化

- オブジェクトごとに論理的な管理状態を一つ持ち、複製と View の間で共有する。カウント、借用状態、ロック、解放の責任は区別する。`using` は実行時の状態を加えない。
- ガードは、ヘッダーを指す非 null のポインター一つで表すことを目標とする。強参照も、ガードごとのヒープ確保も要らない。ソースの Loan は静的なもので、実行時にはたどらない。
  - `Option<ReadGuard>`・`Option<WriteGuard>` は、niche を使って 1 ワードにすることを目標とする。
- `local` の検査と状態の更新は、省いても観測できないときだけ省ける。観測の対象は、競合の Abort、try の結果、上限超過、入れ子と再入の取得、呼び出し、後始末、表への移行である。
  - `clone`・`downgrade` がないことだけでは足りない。一つのハンドルからガードが重なりうる。
  - 取得と解放は一貫して省き、論理的な Loan と寿命の検査は保つ。
  - 証明できる競合には任意で警告してよい。ただし新しいコンパイルエラーにも、評価されない経路での早い Abort にもしない。
- ハンドルのスロット、ガード、ペイロード、管理領域は別物である。現行 §21.5.5 の `ref`・`uniq` の属性は直接の storage に関するもので、証明なしに、読み込んだハンドルを通してペイロードやヘッダーへ `readonly`・`noalias` を広げない。

### 8.2. local の表現（Windows x64）

現行のヘッダー（+0 記述子、+8 control、+16 ペイロード）とペイロードの位置を保ち、control と side table を再利用する。

| 移行 | 目標 |
| --- | --- |
| `obj` から `rc`・`arc` | 公開の前に control = 2（強参照 1）を書く。追加の確保はしない |
| `obj` から `local` | 状態を control に詰める。確保はしない |
| `local` の最初の `downgrade` | カウントと借用状態を、公開する一つの表へ移す。生きているガードは、そのときの表現に対して解放する |

- **control の候補**
  - 下位ビットは `rc` と同じタグにする。
  - 借用状態は一つのフィールドで表す。0 は未借用、全ビット 1 は排他、それ以外は共有数とする。
  - 排他の取得は `(control & (TAG | BORROW_MASK)) == 0` の 1 回の比較で判定できる。
- 移管は生きている `obj` からだけ行い、破棄済みのオブジェクトを復活させない。
- `rc` と `local` の間（段階 B では `arc` と `sync` の間）で、カウント・Weak・解放の仕組みを共有する。
- ビット配置、上限、表の配置は、プロファイルで検証し測定する。本書は固定 ABI を変えない。

### 8.3. カウントの上限

- 現行の `rc`・`arc` のインライン表現（u64 の `strong << 1`）は、上限までの余裕がない。移行との競合のためにもともと CAS を使うので、更新前の検査を保つ。
- 表のカウントでは、`fetch_add` の後で上限を検査し、結果を公開する前に Abort してよい。
- **条件**: 上限と表現の最大値の差は、プロファイルの最大スレッド数より大きくなければならない。1 スレッドが同時に持つ未検査の増分は一つだからである。
  - Windows x64 では最大スレッド数を 2^32 未満とする。上限 2^63 − 1 の u64 には約 2^63 の余裕がある。
- 検査に失敗したら、利用者のコードを呼ばず、巻き戻しもせず、直ちに Abort する。現行 §21.2.3 の更新前の検査は、インライン表現に限る。
- 追加する状態（`local` の借用数など）の上限は、詰めたフィールドの幅に合わせ、表への移行の前後で変えない。
- 表現の競合と、ゼロからの `upgrade` の禁止は、この仮定とは別の正しさの要件である。

### 8.4. 確保

- 通常の生成では、`obj`・`rc`・`arc`・`local` について、オブジェクトの確保を一つにすることを目標とする。`downgrade`、循環構築、待機の仕組みの費用は別に数える。全体で確保が一つだという保証はしない。
- **その場での構築**: 生成の確保と、確保の失敗による Abort は、オペランドの評価の前後どちらで起きてもよい。
  - これにより、`Item.init(...)@local` をオブジェクトの領域に直接構築し、Move による複製を省ける。
  - オペランドの効果、取得の順序と回数は変わらない。オペランドが正常に完了しなければ、先に確保した領域は観測されないまま解放する。

## 9. スレッド能力（段階 B）

### 9.1. 完全型の規則

`Kimi.ThreadTransferable`（TT）は、値の所有を別のスレッドへ移せることを表す。`Kimi.ThreadShareable`（TS）は、別のスレッドから共有アクセスできることを表す。両者は独立した組み込みの保証である。Owned はどちらも導かず、どちらも寿命の義務を免除しない。

| 完全型 | TT | TS |
| --- | --- | --- |
| 組み込みの Scalar、Unit、`string` | 真 | 真 |
| Kimi 提供型 | 本書 §9.6 | 本書 §9.6 |
| ほかの owner Core | 本書 §9.2 の構造導出または公開保証 | 同左 |
| 共通 Function Type | 成り立たない（環境が消去される） | 同左 |
| `ref/T`・`objref/T` | TS(T) | TS(T) |
| `uniq/T`・`obj/T`・`objuniq/T` | TT(T) | TS(T) |
| `arc/T` | TT(T) かつ TS(T) | 同左 |
| `sync/T` | 形成できれば真 | 同左（TS(T) は要らない） |
| `rc/T`・`local/T` | 偽 | 偽 |
| `Weak<S>` | TT(S) | TS(S) |
| `ReadGuard<local/T>`・`WriteGuard<local/T>` | 偽 | 偽 |
| `WriteGuard<sync/T>` | 偽 | TS(T) |

- これらは組み込みの規則である。ガードは S の能力を受け継がず、隠れたフィールドの解析でこの表を上書きできない。
- **構造の検証**: 格納された成分と捕捉に加え、破棄、static 状態へのアクセス、外部資源の契約を検査する。生ポインターと消去された環境は、検証済みの契約を要する（手動の unsafe 適合は保留）。
- **型が未確定の場合**: 宣言された制約を要する。同名のユーザー Contract や空の適合は何も証明しない。
- **callable**: Function Item と具体 Closure は、構造の検証の根拠を保つ。共通 Function Type は、元の callable に能力があっても TT・TS を持たない。能力が要るときは `F is Callable<owner, (i32) -> i32> and ThreadTransferable` のように具体型を総称の `F` のまま保ち、保存するときも消去しない。
- **ガード**: すべてのガードは移せず、取得したスレッドで解放する。`sync` の書き込みガードへの共有参照から使えるのは `value` だけで、`valueUniq` は使えない。

### 9.2. 継承される宣言

> 組み込み要件のうち、型の宣言での表明が、その型とすべての派生型に及ぶものを「継承される宣言」という。

| 宣言 | 効果 |
| --- | --- |
| `Self is ThreadTransferable`・`Self is ThreadShareable` | 宣言した型と、すべての派生型に検証の義務を課す。派生型が追加したフィールドと効果も検証する |
| `Self is not ObjectPayload`（OP §7） | 宣言した型と、すべての派生型が `ObjectPayload` を満たさない |

- 派生型は宣言を撤回できず、書き直す必要もない。条件付きの保証は、条件と置換を階層全体で保つ。
- 表明は義務であり、それ自体は証明にならない。
- 通常の Contract と Copy の適合は、従来どおり継承しない（§8.4.4）。将来、実行時 Contract View が能力を要求するときもこの仕組みを使う。

```kimi
public open struct Shape
    Self is ThreadTransferable      // Shape とすべての派生型に検証の義務を課す
    public var id: i32
```

**導出の範囲**

- 自動の構造導出は Sealed な型に限る。open な Core は、自身の、または継承した公開保証によってだけ TT・TS を持つ。
- 構造の検証では、継承した基底の部分のフィールドと効果（破棄、外部資源、static 状態を含む）を、派生の完全なオブジェクトへ展開する。基底型自身の能力は要さない。
- 分割コンパイルと再検証のために依存を記録する。この証明は、基底の private メンバーへのアクセスを与えない。
- この扱いは継承の部分だけに適用する。独立した `Animal` のフィールドや、`Animal` を対象とするハンドル・借用は、`Animal` の公開の能力に従う。`Animal` への View の変更は、`Animal` が保証しない能力を失う。

```kimi
open struct Animal
    public var age: i32

    public init(age: i32)
        self.age = age

struct Dog : Animal
    public init(age: i32) : base(age)
        ()

var dog = Dog.init(2)@sync              // Dog は Sealed。継承した i32 は構造上安全
// let invalid = dog@sync/Animal        // エラー：Animal に TT の公開保証がない
var exclusive = Dog.init(3)@obj
var baseView = exclusive@obj/Animal     // 合法。Dog の TT/TS の根拠は失われる
```

### 9.3. sync の形成と総称の根拠

オブジェクト対象 `T` について、`sync/T` の形成に追加で要るのは TT(T) だけである。

| 状況 | 根拠 |
| --- | --- |
| 型が未確定の値からの生成 | `T is ObjectPayload and ThreadTransferable` |
| 既知の Core からの生成 | OP §4.2 の直接の判定と TT(T)。open な型では TT の公開保証 |
| 有効なハンドル `s/T` からの移管・View の変更 | 組の根拠（OP §5.1）。`sync` への移管では TT(T) を加える。View の変更では V の対象の形成と Supports、`sync` なら TT(V) を要する |

- カウント、既知の現在のオブジェクト、最適化の推測で公開保証を強めることはできない。正しい絞り込みと checked cast は、通常の規則で新しい根拠を与えうる。
- View を変えると能力を失いうる。以後は新しい静的な型に従う。

```kimi
func createSynchronized<T>(value: T) -> sync/T
    T is ObjectPayload and ThreadTransferable
    return value@sync
```

### 9.4. 再帰的な証明

- TT・TS の構造上の義務と、それに依存する型の形成は、正で有限な依存の最大不動点としてまとめて解く。例: `Node -> Weak<sync/Node> -> sync/Node の形成 -> TT(Node)` は一つの有限な群になる。
- 先に群を作って外部の前提を確定させ、それから違反（`local`・`rc`、証明されない外部資源など）を伝播させる。
- open な型の保証は、検証の義務に展開する。この展開は open な完全値と View に対して要求し、継承した基底の部分の展開には要求しない。
- 通常の Contract の適合の循環は認めず、暫定の結果も公開しない。無限のインライン配置と、型引数の際限ない増大は、既存の規則でエラーとする。完了した証明はキャッシュする。

### 9.5. 借用の受け渡し

- ガードから得た子の借用は、本書 §9.1 の通常の行に従う（`objref/T`・`objuniq/T`、および許された完全なペイロードの投影）。
- ガードと `local` の管理操作は、取得したスレッドにとどまる。
- 子の借用を別のスレッドへ渡せるのは、能力に加えて、競合するアクセスとガードの破棄より前に使用が終わると証明できるときだけである。そのためには scoped thread など、完了を証明できる実行の仕組みが要る（D.2）。本書は spawn・join の API を導入しない。

### 9.6. Kimi 提供型

> Kimi 提供型の TT・TS は、§22.1 のカタログで組み込みの保証として公開する。内部表現からは導出しない。

| 型 | TT | TS |
| --- | --- | --- |
| `Array<T>` | TT(T) | TS(T) |
| `Dictionary<K,V>` | TT(K) かつ TT(V) | TS(K) かつ TS(V) |
| `Slice<T>`・`Text.Utf8Slice` | `ref/T` と同じ | 同左 |
| 所有するイテレーター | 残っている要素に従う | 同左 |

`Option`・`Result`・Tuple・固定長配列は Sealed なので、構造導出で足りる。

## 10. sync（段階 B）

### 10.1. 意味

- `sync` はアトミックの参照カウントと、再入不可の Mutex を持つ。取得は `write`・`tryWrite` だけで、読み取りだけの処理にも `write` を使う。
- `sync` は Mutex に固定し、`read` は追加しない。並列の読み取りが要るなら、別の Semantics または型として設計する。
- **順序付け**: 解放は release、成功した取得は acquire の順序付けを持つ。失敗した `tryWrite` は同期を与えない。
- **デッドロック**: 同じオブジェクトの再取得や、一貫しないロックの順序でデッドロックしうる。再入、検出、FIFO の公平性、有限の待ち時間は保証しない。
- **poisoning**: Abort は巻き戻さずにプロセスを終えるので、poisoning は導入しない（§17.3）。

```kimi
// Item は TT を満たすとする。スレッドは作らない。
var first = Item.init("first")@sync
var second = Item.init("second")@sync
var finished = false

loop
    work: using left = first.write()
        using right = second.write()
            if finished
                exit                        // 両方のガードを破棄し、loop を抜ける
            left.valueUniq.name = "updated"
            finished = true
            exit to work                    // 両方のガードを破棄し、次の反復へ進む
```

### 10.2. 表現と待機（実装候補）

- **待機の鍵**: 元のヘッダーのアドレスを使い、外部の parking table で待つ。View で調整したアドレスや、移動しうる表のアドレスは使わない。
- **フラグ**: `LOCKED`・`HAS_WAITERS` を、インラインでも表でも同じ下位ビットに置く。
  - 下位 3 ビットをタグとフラグに使うので、表の整列は 8 以上とする（現行の確保は 16）。ポインターを復元するときは 3 ビットを消し、上位ビットを保つ。
  - これは候補固有の要件である。`rc`・`arc` の 1 ビットのタグや、固定の ABI は変えない。
- **カウント**
  - 表への移行は制御語全体の CAS で行い、最新のカウントとフラグを保つ。
  - インラインのカウントを更新するときは表現を再確認する。無条件の `fetch_add` は、並行して公開された表のポインターを壊しうる。
  - 安定した表のカウントには `fetch_add`・`fetch_sub` を使ってよい（本書 §8.3）。
  - `upgrade` はゼロから復活しない条件付きの retain であり、`clone` の速い経路では代えられない。
- **ロックの速い経路の候補**（CAS 版と比較して評価する。性能も命令も保証しない）

  | 操作 | 候補 | 解釈 |
  | --- | --- | --- |
  | 取得 | `fetch_or(LOCKED, Acquire)` | 戻り値の `LOCKED` が 0 なら成功。そうでなければ競合の経路へ進む |
  | 解放 | `fetch_and(~LOCKED, Release)` | 戻り値に `HAS_WAITERS` があれば、parking の遅い経路へ進む |

  どちらも、カウント・タグ・ほかのフラグを保つ。カウントだけが変わったことによる CAS の再試行は避けられるが、同じキャッシュラインでの競合は残る。[parking_lot 0.12.5 の Mutex](https://docs.rs/parking_lot/0.12.5/src/parking_lot/raw_mutex.rs.html) は通常の経路で CAS を使っており、この候補が検証済みである根拠にはならない。
- **待機の義務**
  - カウント・タグ・フラグの同時の遷移、待機の登録と解放、表への移行をまたぐ release/acquire、最後の解放を検証する。起こし損ないがないことも確かめる。
  - `HAS_WAITERS` の更新、状態の検証、キューへの登録は、キューのプロトコルの下で調整する。競合を見ただけで眠ってはならず、起きたら正本の状態を確認し直す（[parking_lot_core の `park` の契約](https://docs.rs/parking_lot_core/0.9.12/parking_lot_core/fn.park.html)を参照）。
  - 待っている呼び出し元は、ソースの Loan でオブジェクトを生かす。ヘッダーを再利用する前に待機の記録を外し、別の寿命のオブジェクトとアドレスを取り違えることを防ぐ。
  - キューの記憶域、初期化、競合、OS 資源、失敗は別に数える。
- **代替**: ヘッダーを使うプロトコルを経済的に検証できなければ、生成時に安定した表のロックを用意してよい。生成と移管での確保は本書 §4.2 が許している。
- **確保の目標**: 優先する候補では、Weak も競合もなければ、生成時の確保を一つにする。
- **測定**: カウントとロックが同じ語にあるので、複製の多い負荷でのロックの遅延を測る。
- 共有して公開した後は、`arc`・`sync` の管理領域にアトミックと非アトミックのアクセスを混ぜない。現行の初期化と解放の順序付けに、Mutex の順序付けを加える。

## 11. 例

### 11.1. local の共有・変更・競合

```kimi
struct Item
    public var name: string

    public init(name: string)
        self.name = name

func inspectName(name: ref/string) => ()

var first = Item.init("initial")@local
var second = Kimi.Intrinsics.clone(first@ref)

using reader = first.read()
    inspectName(reader.value.name)
    match second.tryWrite()
        .None => ()                          // 同じオブジェクトに共有の取得がある
        .Some(var writer)
            writer.valueUniq.name = "unexpected"

using writer = second.write()
    writer.valueUniq.name = "changed"        // 受信者：writer@uniq を補う（IER）
    inspectName(writer.value.name)
```

- `reader` は、最後の使用の後も、破棄されるまで取得を保つ。`tryWrite` を `write` に替えると、競合して Abort する。
- Pattern の束縛は腕のスコープで通常どおり破棄されるので、`using` は要らない。

### 11.2. ペイロードとガードの置き換え

```kimi
func replaceItem(target: uniq/Item)
    Kimi.Intrinsics.replace(target, with: Item.init("replaced"))

var shared = Item.init("initial")@local
using guard = shared.write()
    replaceItem(guard.valueUniq@uniq/Item)   // Item は Sealed。ペイロード全体を置き換える
    inspectName(guard.value.name)

using guard = shared.read()
    Kimi.Intrinsics.replace(guard@uniq, with: shared.read())
    guard = shared.read()                    // 代入も合法
    let taken = guard@move                   // guard は Moved
    inspectName(taken.value.name)
```

- 二つ目の `using` の取得は、どれも同じ有効なソースに依存し、更新の時点で子の Loan はない。
- 代入と `replace` は、新しい取得を終えてから古いガードを破棄する。「解放してから取り直す」という意味ではない。
  - 読み取りの取得は共存できる。同じオブジェクトの排他のガードを、解放の前に取り直すと、`local` では Abort、`sync` では自己デッドロックになる。
- `replace` は Unit を返し、`exchange` は古い値を破棄せずに返す。

### 11.3. ガードの生存中の Weak と、循環構築

```kimi
var strong = Item.init("initial")@local
using reader = strong.read()
    let weak = Kimi.Intrinsics.downgrade(strong@ref)   // 表へ移行する。reader の取得は保たれる
    match Kimi.Intrinsics.upgrade(weak)
        .Some(let restored)
            using another = restored.read()            // 束縛したハンドルから取得する
                inspectName(another.value.name)
        .None => ()
```

この例では `strong` が生きているので `upgrade` は成功する。一般には、最後の解放との競合に負けることがある。

```kimi
struct Node
    public let selfWeak: Weak<local/Node>

    public init(selfWeak: Weak<local/Node>)
        self.selfWeak = selfWeak

func buildNode(weak: Weak<local/Node>) -> Node
    let unavailable = Kimi.Intrinsics.upgrade(weak)    // Building 中は None
    return Node.init(weak@move)

var node = Kimi.Intrinsics.makeCyclic(buildNode)       // c/T = local/Node を推論する
```

### 11.4. 式の結果

```kimi
struct Counter
    public var count: i32

    public init(count: i32)
        self.count = count

var shared = Counter.init(41)@local
let next = using guard = shared.read() => guard.value.count + 1

let answer = work: using guard = shared.read()
    exit to work: guard.value.count + 1

let kept = using guard = shared.read() => guard@move
// 責任は kept へ移る。shared は kept より長く生きなければならない。

// let escaped = using guard = shared.read() => guard.value
// エラー：結果は、受け渡しの前に破棄されるガードを借用している。
```

字下げした本体の最後にスカラーを書いても、`do` と同じく結果は Unit になる。

### 11.5. 総称の根拠と関数値

```kimi
func createShared<T>(value: T) -> rc/T
    T is ObjectPayload
    return value@rc

func share<s/T>(value: s/T) -> local/T
    s is obj
    return value@local              // 移管：組の根拠を使うので ObjectPayload は要らない

func acquireWrite<s/T>(value: ref/s/T) -> WriteGuard<s/T>
    s is guarded
    return value.write()            // 結果の source は value（§15.4.3）

func acquireRead<s/T>(value: ref/s/T) -> ReadGuard<s/T>
    s is local
    return value.read()

let createRc = func (value: Item) => value@rc   // 削除した makeRc の代わり
```

## 12. 統合

### 12.1. 段階

| 段階 | 内容 | 時期 |
| --- | --- | --- |
| A | 本書 §3〜§8（`sync` に固有の部分を除く）と §11 | IER と OP の取り込みの後 |
| B | `sync`（本書 §10）、スレッド能力（本書 §9）、待機のランタイム | Appendix D.2 のスレッド設計と同時 |

- 段階 A の間、`sync` は `async` と同じく Semantics の文脈で予約して拒否する。`guarded` の要素は `local` だけとし、許容集合の全体から `sync` を除く。
- 段階 B で `guarded` の範囲が広がるので、`s is guarded` の本体を再検証する。
- 分ける理由: TT・TS は、別のスレッドへ渡す手段がなければ観測できない。捕捉の能力や完了の証明と一緒に決めないと、手戻りが出る。

### 12.2. ほかの draft との関係

統合するときに、IER・OP の記述へ次を反映する。

- **IER**: 値の種類の「所有 handle」と、受信者の経路（本書 §5.2）。
- **OP**
  - オブジェクト系と `Self is not ObjectPayload` の対象に `local`・`sync` が入る（本書 §3.3）。Weak の組の条件は ⊆ `counted` になる（本書 §6.1）。
  - `makeObj`・`makeRc`・`makeArc` への制約は `@` による生成の要件に（本書 §4.2）、`makeRcCyclic`・`makeArcCyclic` への制約は `makeCyclic` に移る（本書 §6.2）。
  - 例を書き換える。`makeObj(value@move)` は `value@obj` に、`s is object` の下でペイロードを直接借用するものは `s is object and not guarded` にする。
- **Sealed**: 用途は、ペイロードの投影と、TT・TS の構造導出（派生型がないこと）に限られる。

### 12.3. SPEC.md への影響

正式仕様は、本書に依存しない形で更新する。STATUS は、実装と検証が済むまで、本書の機能を対応済みとして扱わない。

- **型と区分**
  - §2: 文脈キーワード、`using` の認識
  - §3.2.2、§3.3（Semantics の表、区分、`counted` を否定する記述の削除）、§3.3.3、§3.3.5（Object Semantics）、§3.4（guarded には直接の経路がない）
  - §8.3・§8.7（許容集合 11 種、区分）
  - 付録 E（ハンドル、スロット、ガード、生成、移管、`local` の表記規約）
- **`@` とオブジェクト**
  - §13.5（原則の改訂）、§13.5.3（転送の共通規則、生成、移管、総称の `@s`）、§13.5.7（`local`・`sync` の upcast）
  - §13.5.8（make API の削除、counted への拡大、`makeCyclic`、`@ref` によるスロットの借用の説明の統一）、§13.5.9、§13.6.1〜13.6.2
- **ガード**
  - §4.6.6（SharedReadResult）、§7.3・§12.4（ハンドル操作、受信者の経路）、§11（ガードのアクセサ）
  - §22.1 と SPEC.md の索引（ガード型、ハンドル操作、`makeCyclic`）
- **using**: §14.2〜14.5（`DoExpression`）、§9（名前の可視性）、付録 F
- **ランタイム**: §21.2.3（`local` の表現、ハンドルの行、上限の検査の規則）、§21.5.5（属性）
- **段階 B**: §3.3、§8.4.7（TT・TS、継承される宣言）、§6.2.2、§18（証明の依存）、§22.1（Kimi 提供型の能力）、§21.2.3（`sync`）、付録 D.2

## 13. 検証

以下は受け入れの義務であり、実行済みの結果ではない。診断は、該当する操作・束縛・Loan・呼び出しの経路・不足している証明を示す。

- **名前と区分**: 文脈キーワードを通常の名前として使えること、`async` の拒否、広げた区分と許容集合、総称の全範囲の検査、特殊化の選択。
- **`@`**
  - 表のすべての行と、確保の有無
  - 転送の共通規則（Copy 型の place も Moved になること、`n@i32@rc` では元が残ること）
  - 生成・移管・Move の区別、同一性・アドレス・依存が保たれること
  - 生成と View の変更を一つの `@` で組み合わせないこと、`ObjectPayload` の要否（生成は要し、移管は要さない）
  - 総称の `@s` で操作の種類が混ざる場合の定義エラー、make API の削除と closure による代替
- **ガード**
  - 形成（`ReadGuard<sync/T>` とオブジェクト化の拒否）、受信者の経路（スロットの共有借用、`let` からの取得）、ペイロードのメンバーが見つからないこと
  - 同名のユーザー宣言に特権がないこと、`valueUniq` の暗黙の排他の受信者と、`let` の束縛での拒否
  - phantom な `source` の変性と Loan 要件
- **寿命**
  - ソースのスロットへの依存、一時値のソースより長く生きるガードの拒否、一時値のガード
  - 通常の Move・代入・部分 Move と、残った部分の後始末
  - ガードを返す合法な戻り値と、ぶら下がる子の結果の拒否
  - 置き換え（新しい取得の後で古い取得を解放すること、`local` の Abort）、`replace` と `exchange`
- **要素**: SharedReadResult の原則が現行の表と一致すること、guarded の要素がスロットの借用になること。
- **Weak と循環構築**: counted の四つのモード、`makeCyclic` の推論、Owned・ObjectPayload、Building 中の `None`、一度だけの公開、ビルダーから逃げた Weak と最後の解放。
- **using**
  - 認識と確定、本体が必須であること、入れ子
  - 注釈と初期化子を外側の環境で解決すること、本体の宣言が見えないこと、ラベルの範囲
  - 単項目と字下げの本体の結果、Discard Context、Never、名前付き `exit`、外への遷移
- **ランタイム**
  - `local` の共有・排他の競合、try の結果、上限超過の Abort（結果を公開する前）、折り返しや飽和による成功がないこと
  - 生きているガードがある状態での表への移行、正本の状態が一つであること
  - 上限の余裕の条件と、インライン表現での更新前の検査
  - 省略の最適化が観測できる挙動を保つこと、確保を保証とは別に測ること、属性の検証
- **段階 B**
  - TT・TS の表、継承される宣言、基底の部分の展開、公開保証のない open な型の独立したフィールドの拒否、失敗の再帰的な伝播、Kimi 提供型の行
  - `sync` の排他と try、順序付け、待機の鍵、3 ビットの予算、フラグを保つビット演算、移行の CAS、登録・解放・移行の競合、アドレスの再利用後の誤った起こし

## 14. 範囲外と将来

| 項目 | 扱い |
| --- | --- |
| `async` | Semantics の文脈で予約する。将来の `await source.write()` と非同期のガードは別に設計する。取得前のキャンセルでは待機の登録を外し、取得後のタスクの破棄では通常の後始末を行う。`ReadGuard` と同期の `WriteGuard` は await をまたがない |
| 強参照を所有するガード | 将来の課題。一時値のハンドル（`upgrade` の結果など）から、保持できる取得を得られるようにする |
| counted の間の変換、`obj` への復帰、循環の回収 | 導入しない |
| 再入とデッドロックの検出 | 導入しない。スレッド ID のフィールドも必須にしない |
| 手動の unsafe 能力の適合 | 具体的な安全の契約ができるまで保留する |
| 能力を持つ消去された callable 型 | spawn の設計（D.2）で決める |
| 移管でのペイロードの再配置 | 禁止する。同一性と外部の登録を保つためである |
| 実行時 Contract View | OP §10 に従う |

## 15. 決定

採用する。本文で理由を述べた選択（ハンドルにメンバーの `clone` を置かないこと、ガードの命名、総称の `@s`、`using` の形、Pattern の束縛、段階の分割）は再掲しない。ほかに不採用とした案は次のとおりである。

- **名前と綴り**
  - **`local` を `cell` などに改名する案**: `local`・`sync`・`async` の、待ち方による系列が崩れる。用語の衝突は表記の規約（本書 §2）で避ける。
  - **ハンドル操作の関数形を併存させる案**: 同じ操作の綴りが二つになる。
  - **実引数でスロットを暗黙に借用する案（`clone(first)`）**: 外側に層を足す借用は、綴りで見せる（§10.2）。
- **意味**
  - **生成で Copy の元を残す案**: 「所有 Semantics の指定は転送」という規則に例外が入る。
  - **`object` を `obj`・`rc`・`arc` のままにする案**: OP のオブジェクト系と一致せず、`local`・`sync` だけが別扱いになる。
  - **make API を残す案、`makeLocal`・`makeSync` を加える案**: 生成の綴りが二つになる。
  - **モードごとに循環構築の関数を置く案**: counted を増やすたびに API が増える。
  - **`sync` に `read` や RwLock を加える案**: Mutex の表現と両立しない。
  - **TT・TS の継承を Contract 一般に広げる案**: §8.4.4 の方針を変える必要がある。名前のある種類（継承される宣言）に限る。
  - **`using` の束縛の保持を保証する案（Move や代入の禁止）**: 通常の `var` の規則に例外が入る。
- **ランタイム**
  - **表のカウントも CAS による更新前の検査に限る案**: 本書 §8.3 の余裕の条件で足り、速い経路が遅くなるだけである。
