# 共有所有・ガード・using

- 日付: 2026-09-19（2026-09-24 改訂）
- 状態: 仕様への取り込み前の設計案。実装済みの機能や検証済みの ABI を示すものではない。
- 範囲: `local`・`sync` Semantics、ガード、`@` による生成と移管、参照カウントと Weak、循環構築、一時値の確定境界、`using`、スレッド能力。

## 1. 位置付け

本書で定める事項は `SPEC.md` とその参照先より優先し、それ以外は現行仕様を適用する。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。「本書 §」は本書の節、それ以外の「§」は現行仕様の節を指す。受信者式の暗黙の取得（§3.4、§7.3）と ObjectPayload（§8.4.7.2）を前提とする。

目的は次の三つである。

- 共有したオブジェクトを、実行時の借用検査（`local`）またはロック（`sync`）を通して安全に変更できるようにする。
- オブジェクトの生成と所有モードの移管を、`@` の一つの綴りにまとめる。
- ガードの解放を、通常の所有権と寿命の規則だけで扱う。

本書 §3〜§8 を段階 A、§9〜§10（スレッド能力と `sync`）を段階 B として取り込む（本書 §12.1）。

```kimi
var shared = Item.init("initial")@local        // 生成
var other = Kimi.Intrinsics.clone(shared@ref)  // 強参照の複製
using writer = shared.write()                  // ガードの獲得。本体の終わりで解放する
    writer.valueUniq.name = "changed"          // 受信者 writer は暗黙に排他で取得される（§7.3）
```

## 2. 用語

| 用語 | 意味 |
| --- | --- |
| ハンドル | `obj`・`rc`・`arc`・`local`・`sync` の値。オブジェクトを所有する |
| スロット | ハンドルを保持する storage。`ref/local/T` はスロットの借用であり、ペイロードの借用ではない |
| ガード | `ReadGuard`・`WriteGuard` の値。一つの獲得を解放する責任を持つ |
| 獲得 | ハンドル操作（本書 §5.2）が実行時の借用またはロックを得ること。英語では *guard acquisition* と書く |
| 取得 | 現行仕様の *acquisition*（Copy・Move・借用など） |
| 確定 | 現行仕様の *secure*。取得した値を結果として固定すること |
| 確保 | 記憶域の割り当て（*allocation*） |
| 生成 | 完全な値から新しいオブジェクトを作る `@` の操作（本書 §4.2） |
| 移管 | `obj` の所有を、同じオブジェクトのまま counted のモードへ移す `@` の操作（本書 §4.2） |

`local` はスタック確保もスレッドローカル記憶域も意味しない。Semantics は常に `local/T` または「local ハンドル」と書き、束縛は「ローカル変数」と書く。

## 3. Semantics

### 3.1. 役割

| 役割 | 型・構文 | 意味 |
| --- | --- | --- |
| 排他所有 | `obj/T` | 所有の責任を一つ持つ。ペイロードに共有・排他でアクセスできる |
| 共有所有 | `rc/T`・`arc/T` | 非アトミック／アトミックの参照カウント。ペイロードへは共有アクセスだけ |
| ガード付き共有所有 | `local/T` | 非アトミックの参照カウントと実行時の借用検査。競合したら待たずに Abort する |
| ガード付き共有所有 | `sync/T` | アトミックの参照カウントと再入不可の Mutex。競合したら待つ（段階 B） |
| オブジェクト借用 | `objref/T`・`objuniq/T` | View への共有／排他アクセス。所有しない |
| 獲得の寿命 | `ReadGuard<s/T>`・`WriteGuard<s/T>` | 獲得を解放する責任を持つ所有値 |
| スコープ | `using x = e Body` | 束縛で始まる `do`（本書 §7） |

- `local`・`sync` は組み込みの Semantics であり、`<s/T>` の分解と Semantics の要件に参加する。`T` は Object Target でなければならない（§8.4.7.2）。実行時 Contract View は有効にしない。
- `local`・`sync`・`async` は待ち方の系列である。`local` は待たない、`sync` はスレッド間で待つ、`async` は将来 await で待つ（本書 §14）。

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

- `object` はハンドル全体を表す。`guarded` は共通の `write`・`tryWrite` を与え、`read`・`tryRead` は `local` だけが持つ。`counted` は強参照の複製と Weak を与える。Weak 自体は owner である。
- §8.7 の admitted set の全体は 11 種（owner, ref, uniq, obj, rc, arc, local, sync, objref, objuniq, unsafe）になる。§8.1.1・§8.4.7.2 の object family は `object` ∪ `objectborrow` と定義し直す。
- 現行 §3.3 の「`counted` という区分はない」を削る。

### 3.4. ペイロードへのアクセス

> ペイロードに直接アクセスする操作は、ハンドルの Semantics が `object and not guarded` のときだけ使える。guarded のハンドルからペイロードに触れる経路は、ガードだけである。

| 操作 | `local`・`sync` |
| --- | --- |
| ペイロードへの直接アクセス: `@objref`・`@objuniq`、ペイロードの投影（`@ref/T`・`@uniq/T`）、借用の upcast（`@objref/V`・`@objuniq/V`）、object-kind の受信者、SharedReadResult の `objref`、ペイロードの Contract 適合 | 使えない |
| ハンドル自体の操作: 転送、所有の upcast（`@local/V`）、`is` テスト、checked cast、`clone`・`downgrade`・`upgrade`、ハンドル操作 | 使える |

- **総称**: admitted set ⊆ `object and not guarded` を証明できるときだけ、直接アクセスを使える。`object`・`owning`・`reference` と制約のない `<s/T>` は guarded を含むので、これらの本体で直接アクセスするコードは `s is not guarded` を加える。
- **変性**: `local/T`・`sync/T` は T について不変とし、admitted set に guarded を含む `s/T` も同じく扱う。`Weak<S>` は S の変性に従う。共有したまま変更できるので、T の内部の Origin を短くしたハンドルを作れると、短い寿命の参照を書き込めてしまうからである。明示の View の upcast は、この Origin の短縮とは別の操作である。

### 3.5. 複製

すべてのハンドルとガードは Non-Copy である。所有の受け渡しは転送（place からは `@move`、一時値はそのまま）で行い、カウントを変えない。強参照を複製できるのは counted だけで、`Kimi.Intrinsics.clone` を使う（本書 §6.1）。ハンドルに `.clone()` のようなメンバーは置かない。`rc`・`arc` のメンバー検索はペイロードに進むので、名前が衝突するからである。

## 4. `@` による生成と移管

### 4.1. 取得の共通規則

> 所有 Semantics を指定する `@`（`@owner`・`@obj`・`@rc`・`@arc`・`@local`・`@sync`。`/T` 付きの形と総称の `@s` を含む）は、オペランドを転送で取得してから、本書 §4.2 の操作を一つ行う。

- place は Copy 型でも Moved になり、一時値はそのまま渡る（§13.5.3）。Copy の元を残すときは、先に Copy を明示する。
- 操作はオペランドの静的な型と指定で決まる。失敗しても別の操作は選ばない。

```kimi
var count = 1
let kept = count@i32@rc    // Copy してから生成する。count は使える
let taken = count@rc       // count は Moved
```

### 4.2. 操作

M は `object` の要素、C は `counted` の要素とする。

| オペランド | 指定 | 操作 | 操作による確保 |
| --- | --- | --- | --- |
| 同じ正規化済みの完全型 | オペランドと同じ Semantics（`@owner` を含む） | 転送だけ（現行） | なし |
| `M/S` のハンドル | `@M/V` | 同じモードの upcast | なし |
| 完全な owner 値 `T` | `@M`・`@M/T` | **生成**: 新しいオブジェクトを作る | オブジェクトと管理領域を確保しうる |
| `obj/T` | `@C`・`@C/T` | **移管**: 同じオブジェクトの所有を C へ移す | 管理領域と、再配置するなら移動先の領域を確保しうる |

**生成**

- `T is ObjectPayload` を要する（§8.4.7.2）。具体型は直接判定し、型が未確定なら宣言された前提を要する。open な型も生成できる。
- 取得した値をペイロードへ Move する。構築子・accessor・`deinit` は繰り返さない。一律の Owned 要件はなく、外部の依存は保たれる。
- `value@local` の所有の意味は `(value@obj)@local` と同じだが、中間のオブジェクトも二回の確保も要らない。

**移管**

- ハンドルを消費し、同一性・Dynamic Type・外部の依存を保つ。ペイロードは複製しない。
- 観測されたアドレス（unsafe で得たポインター、外部への登録）は保つ。観測されていなければ、実装は移管のときにペイロードを再配置してよい（`obj` をスタックに置くなどの最適化を妨げないため、§3.3.3）。再配置は通常の Move と同じく、構築子・`deinit` を呼ばず、移動元のペイロードを破棄しない。
- 管理状態は公開の前に初期化する。競合する Loan があれば、通常の Move の規則で拒否する。
- 既存のハンドルの操作なので `ObjectPayload` を再証明せず、移管先に固有の条件だけを加える（`sync` なら TT(T)、本書 §9.3）。

**共通**

- 生成と移管は `T` を保ち、View の変更とは一つの `@` で組み合わせない（`dog@local@local/Animal` と書く）。
- counted のモードの間の変換、counted から `obj` への変換、借用からの所有ハンドルの作成はない。強参照が一つでも同じである。
- `@` は利用者のコードを呼ばず、変換の連鎖を探さず、強参照を暗黙に複製しない。
- 確保に失敗したら、結果を公開する前に Abort する。完了した効果と Move は戻さない。確保の時点は本書 §8.4 に従う。
- 現行 §13.5 の「`@` は暗黙にボックス化せず、資源を取得しない」を、「オブジェクトを作り確保しうるのは、明示の生成と移管だけである」に改める。
- `Kimi.Intrinsics.makeObj`・`makeRc`・`makeArc` を削除し、`makeLocal`・`makeSync` も設けない。関数値が要るときは closure で包む（`func (value: Item) => value@rc`）。

### 4.3. 総称の Semantics 指定

> 総称の `@s` は、admitted set のすべての要素で同じ種類の操作（転送だけ・upcast・生成・移管）を選ばなければならない。種類が混ざれば定義エラーとする。

- 生成には、admitted set ⊆ `object` と `T is ObjectPayload` を要する。`sync` の TT は本書 §9.3 に従う。
- これにより、確保の有無を署名から読める。

### 4.4. View

- `local`・`sync` のハンドルは、同じモードの upcast（Move）、`is` テスト、checked cast（`Result<s/B, s/A>`）、ペイロードの消去（Owned）に参加する。§13.5.7・§13.6 の表には、ハンドル自体を扱う行にだけ `local`・`sync` を加える（本書 §3.4）。
- ガードから得た `objref`・`objuniq` は、既存のオブジェクト借用の upcast を使う。
- スロットの借用（`ref/local/T` など）には、ペイロードの upcast もコンテナの共変もない。
- 排他アクセスは、実体が正確に T であることを証明しない。ペイロードの投影は、現行 §13.5.5.1 のとおり Sealed を要する。

```kimi
// Dog は Animal の派生とする。
var original = Item.init("initial")@obj
var first = original@local        // 移管：original は Moved。同一性は変わらない
var dog = Dog.init()@local        // 生成
var animal = dog@local/Animal     // upcast：同じオブジェクト。dog は Moved
// let view = animal@objref       // エラー：guarded のハンドルから直接借用はできない
```

## 5. ガード付きアクセス

### 5.1. 獲得の状態と失敗

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

- 成功だけがガードを作る。競合は、獲得も解放の責任も作らない。
- try の操作が報告するのは獲得の競合だけで、上限超過や資源の不足からは回復しない。上限は本書 §8.3 に従い、カウントは折り返さず、飽和して成功することもない。

### 5.2. ハンドル操作

`local/T`・`sync/T` のハンドル型は、組み込みのメンバーを四つだけ持つ。

```kimi
// 組み込みメンバーの概形。Self はハンドルの完全型 s/T
func read(self: ref/Self) -> ReadGuard<s/T>                // s is local
func tryRead(self: ref/Self) -> Option<ReadGuard<s/T>>     // s is local
func write(self: ref/Self) -> WriteGuard<s/T>              // s is guarded
func tryWrite(self: ref/Self) -> Option<WriteGuard<s/T>>   // s is guarded
```

- **Origin**: 結果の `source` は受信者の Origin になり（§15.4.3 の規則 1）、ガードはスロットの共有 Loan を保つ。
- **受信者の取得**
  - §3.4 の Owned Place の owned handle に `local`・`sync` を加える。
  - §7.3 の表の列は、選ばれたメンバーで決める。ペイロードのメンバー（基底の宣言を含む）なら object-kind、ハンドル操作なら value-kind とし、`p@ref` でスロットを共有借用する。
  - ハンドルの place（`let` を含む）、`ref/s/T`、`uniq/s/T` から獲得できる。獲得はハンドルを Move しない。
- **メンバー検索**
  - guarded のハンドルでは、この四つだけが見つかる。ペイロードは検索し直さない。診断は `h.read().value.m()` や `h.write().valueUniq.m()` を示してよい。
  - 総称の `s/T` で admitted set が guarded と guarded 以外の両方を含むと、見つかるメンバーが束縛ごとに変わるので、定義エラーとする。
  - 解決は宣言の同一性で行う。同名のユーザー宣言は特権を得ない。
- **綴り**: メンバーの形だけとし、関数形（`Kimi.Intrinsics.read` など）、`lock`・`tryLock`・`LockGuard` は設けない。
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

- **`source`**: 安全な参照を格納しない phantom slot（§15.3.5）。組み込みのメタデータとして、共変、Loan 要件は `ref`（ソースのスロットへの共有 Loan）とする。
- **T の変性**: `ReadGuard` は共変、`WriteGuard` は不変とする。
- **命名と受信者**: 共有版を `value`、排他版を `valueUniq` とする（§4.7.1）。`valueUniq` は受信者を暗黙に排他で取得する（§7.3）ので、ガードの束縛は §15.1.5 の排他取得の条件を満たす storage（`var` または一時値）でなければならない。
- **形成**: `ReadGuard<sync/T>` は形成できない。utf8-formatting §1.1 の Loan に縛られたアダプターと同じく、オブジェクト化を禁止する。
- **構築と解放**: ガードを作るのはハンドル操作だけで、公開の構築子、可変の管理フィールド、`deinit` の追加、複製、手動の解放 API はない。通常の破棄が、そのとき持つ獲得を一度だけ解放する。
- **子の借用**: アクセサは管理状態を変えず、ペイロードへのアクセスを貸すだけである。子の Loan が終わっても獲得は解放されない。

### 5.4. 寿命と解放

```text
ソースのスロット -> ガード -> ペイロードの子借用 -> フィールド・再借用・捕捉
```

- ガードはソースのスロットを共有借用し、強参照を追加しない。ガードの生存中、ソースは Move も置き換えもできない。
- 依存と Loan の由来は、View の変更・cast・戻り値・保存・捕捉を通して保たれる。別の強参照がオブジェクトを生かしていても、解放済みのガードの子の参照は有効にならない。
- 解放は、責任を持つガードの通常の破棄（合法な置き換えを含む）で起こる。最後の使用で早まることはない（§16.2.1）。Abort と発散は通常のスコープ終了に従う。
- ガードは一時値のソースより長く生きられない。`upgrade` の結果などは、先に束縛してから獲得する。一時値のガードの寿命は本書 §5.5 に従う。
- **ペイロードの置き換え**: ガードを通したペイロード全体の置き換え（§15.7.3、§16.3.3）は、最後の解放ではない。Dynamic Type とヘッダーは変わらないので、ほかのハンドルからのハンドル自体の操作（本書 §3.4）は、古い内容の破棄中も許す。ペイロードへのアクセスは、置き換えているガードが排除する。§16.4 の破棄中の制限は、最後の解放にだけ適用する。

### 5.5. 一時値の確定境界

> 部分式の値を受け取り先へ取得し終えた位置を**確定境界**とする。境界では、その部分式の評価で作られてまだ残っている一時値のうち、取得した値が直接にも間接にも借用していないものを破棄する。借用されている一時値は、すぐ外側の境界へ持ち越して再判定する。最も外側の式の終わりは最後の境界であり、寿命がそれより延びることはない。

| 構文 | 確定境界 |
| --- | --- |
| 呼び出し | 受信者と各引数（既定値を含む）の取得 |
| 構築 | Tuple・配列の要素、Dictionary のキーと値、構築子の引数、enum の payload の取得 |
| 演算子 | 各オペランド（短絡評価を含む）の取得 |
| 代入・複合代入 | 右辺の取得。代入先は境界ではなく、その一時値は文の終わりまで生きる |
| 宣言・遷移 | 初期化子、`return`・`exit`・`yield` の値の取得 |
| 制御 | 条件（現行 §14.2.3）、`match` の主語、`for` の反復対象の取得 |
| 文字列補間 | 各穴の書き込みの完了（通常の補間と `$tryWrite`） |

- 現行の条件式の規則を一般化したもので、§3.6.2 の一般規則を改める。
- **保護**: 破棄の対象は、その部分式で作られた一時値だけである。先に取得した受信者・引数・代入先などの未完了の値は、後の部分式の一時値を借用できない。持ち越した一時値は、それを使う呼び出しや代入が完了した後の境界でだけ再判定されるので、未完了の処理を壊さない。
- **決定性**: 借用の有無は既存の Loan 解析で決まり、破棄の時点は最適化に依存しない。総称で借用の有無が束縛によって変わる（SharedReadResult など）ときは、束縛ごとに決まり、本体は両方で検証する（§8.10 の条件付きの効果計画と同じ）。
- **補間**: 各穴は共有借用で書き込み（utf8-formatting §5.2）、その借用は書き込みの完了で終わる。`$tryWrite` が途中で失敗しても、書き込みを終えた穴の一時値は破棄済みで、残りは通常の後始末に従う。
- **Place の主語**: `match`・`for` の主語が Place なら、Copy 型でも共有借用する（§15.1.6）ので、ガードは構文の間生きる。§15.1.6 が認める Copy による実装でも、一時値の破棄の時点は借用と同じにする。その中で同じオブジェクトを排他で獲得すると、`local` は Abort、`sync` は自己デッドロックになるので、値だけが要るときは先にローカル変数へ確定する。
- **効果**: ガードは必要な間だけ獲得を保ち、`local` の競合と `sync` の臨界区間が短くなる。ほかの一時値の `deinit` も早まる。

```kimi
// readCount は objref/Item を受け取り、借用を保たない i32 を返す。
h.write().valueUniq.count = h.read().value.count + 1   // 右辺の確定で読み取りガードを解放してから、左辺で獲得する
f(h.read().value.count, h.write())                     // 第 1 引数の確定で解放する
let pair = (h.read().value.count, h.write())           // Tuple の要素も同じ
f(readCount(h.read().value), h.write())                // 内側では借用されるが、外側の第 1 引数の確定で再判定して解放する
f(h.read().value, 0)                                   // 第 1 引数が借用するので、呼び出しの間生きる
let text = "\(h.read().value.count) \(h.write().value.count)"   // 第 1 の穴の書き込みの完了で解放する
h.write().valueUniq.count += h.read().value.count      // 競合する：複合代入は代入先を先に評価し、そのガードは文の終わりまで生きる
let state = h.read().value.state                       // 主語に使う値は、先にローカル変数へ確定する
match state
    .Busy => h.write().valueUniq.state = .Idle
    _ => ()
```

### 5.6. 要素の読み取り

現行 §4.6.6 の SharedReadResult を、次の原則で定義し直す。現行の表と同じ結果を与え、guarded のハンドルは最後の行に入る。

| 要素の完全型 | 結果 |
| --- | --- |
| Copy 型 | 値の Copy |
| `uniq/T`・`objuniq/T` | 共有の再借用（`ref/T`・`objref/T`） |
| `object and not guarded` のハンドル | `objref/T`（カウントは変えない） |
| それ以外の Non-Copy | 要素のスロットの共有借用 `ref/E` |

```kimi
// items: Slice<local/Item>
let handle = items[0]          // ref/local/Item。ペイロードの借用も、カウントの増加も、獲得もしない
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

- **スロットの借用**: ハンドルの place への `h@ref` は `h@ref/s/T` の省略形で、スロットの共有借用を作る。`@ref` が `@objref` になることはないので曖昧さはない（§13.5.1）。実引数ではハンドルの外側に層を足す借用を暗黙に行わない（§10.2）ので、`clone(first)` ではなく `clone(first@ref)` と書く。
- **Weak の形成**: S の外側の Semantics が counted であることを要する。総称では admitted set ⊆ `counted` とする（§3.2.2 の `rc`・`arc` を広げる）。
- `clone` はペイロードを複製せず、利用者のコードを呼ばず、獲得もしない。`upgrade` の成功で得られるのは強参照だけで、guarded ではガードが別に要る。
- `clone`・`upgrade` は管理領域を確保しない。最初の `downgrade` は side table を確保しうる。
- 最後の強参照との競合、ゼロからの復活の禁止、動的型全体の破棄、元の領域の解放、Weak の表の寿命は、現行 §13.5.8〜9 と §21.2.3 に従う。

### 6.2. 循環構築

```kimi
func makeCyclic<c/T, F>(build: F) -> c/T
    c is counted
    T is Owned and ObjectPayload
    F is Callable<owner, (Weak<c/T>) -> T>
```

- `makeRcCyclic`・`makeArcCyclic` を置き換える。`c/T` は `build` の引数型 `Weak<c/T>` から推論し、呼び出し側で形成される（`sync` の TT(T) もそこで検査する、本書 §9.3）。
- **手順**
  1. 公開前のオブジェクト領域と side table を確保する。表は構築ガードと、ビルダーに渡す Weak を一つ持つ。モードの管理状態を初期化する。
  2. `build` を呼び出し元のスレッドで一度だけ呼び、Weak を値で渡す。Building の間、`upgrade` は `None` を返し、強参照・ペイロード・獲得は得られない。
  3. ビルダーの正常な結果と呼び出しの後始末が終わってから、完全な `T` をペイロードへ Move する。
  4. Alive と強参照一つを一度だけ公開する。`local` は未借用、`sync` は未ロックで始まり、`arc`・`sync` の公開の順序付けは現行どおりである。
- `F` は通常どおり取得し、Copy・Owned・TT を要さない。`T is Owned` は、ビルダーが得た依存が公開済みの Weak を通して逃げることを防ぐ。
- 確保の失敗とカウントの上限超過は公開の前に Abort する。ビルダーが Abort または発散すると公開されず、巻き戻しと後始末は保証しない。
- 強参照の循環は回収しないので、Weak で断つ。可変の Optional フィールドは、通常の生成の後に `write` を通して設定できる。必須の自己 Weak や不変のフィールドには循環構築を使う。

## 7. using

### 7.1. 構文

```text
DoExpression := [Label ":"] "do" Body
              | [Label ":"] "using" Name [":" Type] "=" Expression Body
```

- `using` は `do` の一形態であり、`do` を参照する規則（Completion、Body の結果、ラベル、§14.5.2 の遷移の表）をそのまま適用する。
- `var` のローカル変数を一つだけ導入する。`valueUniq` の受信者や `replace` の引数として排他に取得できるようにするためである（§15.1.5）。`let`／`var` の指定、本体のない形、複数の束縛、同じ字下げでの連結はなく、複数の獲得は入れ子で書く。
- **認識**: 式の先頭（ラベルの後を含む）で、`using` の後に同じ物理行で Name と `=` または `:` が続くときに認識する。コメントは読み飛ばす。認識したら確定し、エラーでも別の解釈に戻らない。`using = 1`、`using(x)`、`x.using()` は通常の名前である。
- 本体を持つ式を初期化子に書くときは、括弧で囲む。

### 7.2. 意味

`L: using x: T = e Body` は、本体のスコープが `var x: T = e` で始まる `do` と同じである。ただし次の点を保つ。

1. `T` と `e` は外側の環境で解決・評価する。`x`、`L`、本体の宣言は `e` から見えない。初期化子は本書 §5.5 の確定境界であり、寿命は延びない。
2. `x` は本体のスコープの先頭、文と `defer` の前に入る。重複と隠蔽は通常の規則に従い、`L` は本体の中だけで有効である（§14.4）。
3. 結果の規則は元の本体の形に適用し、単項目の本体は値を返す。展開は字句の書き換えではない。

初期化子から外側の有効な目標へ遷移できる。初期化が完了しなければ、束縛はできない。

### 7.3. 結果と所有

- 結果の規則は `do` と同じである。値を使う単項目の本体は値を返し、字下げした本体は終わりに達すると Unit を返す。Unit 以外の結果は名前付き `exit` で返す。`using` は `return`・`yield`・`continue` の目標にも、探索の障壁にもならない。
- 束縛は通常の `var` であり、Move、代入、部分 Move、借用、`replace`・`exchange`・`swap` を許す。`using` に固有の保護はなく、最初の値を最後に解放する保証もない。束縛全体の Move や代入への警告は任意とし、受理の可否を変えない。
- 結果の受け渡しより前に破棄されるガードに依存する参照は拒否する。独立した値と、転送したガード（`=> guard@move`）は許す。

## 8. 実装と性能

### 8.1. 管理状態と最適化

- オブジェクトごとに論理的な管理状態を一つ持ち、複製と View の間で共有する。カウント、借用状態、ロック、解放の責任は区別する。`using` は実行時の状態を加えない。
- ガードは、ヘッダーを指す非 null のポインター一つで表すことを目標とする。強参照も、ガードごとの確保も要らない。ソースの Loan は静的なもので、実行時にはたどらない。`Option<ReadGuard>`・`Option<WriteGuard>` は niche で 1 ワードにすることを目標とする。
- `local` の検査と状態の更新は、省いても観測できないときだけ省ける。観測の対象は、競合の Abort、try の結果、上限超過、入れ子と再入の獲得、呼び出し、後始末、表への移行である。
  - `clone`・`downgrade` がないことだけでは足りない。一つのハンドルからガードが重なりうる。
  - 獲得と解放は一貫して省き、論理的な Loan と寿命の検査は保つ。
  - 証明できる競合には任意で警告してよい。新しいコンパイルエラーにも、評価されない経路での早い Abort にもしない。
- 現行 §21.5.5 の `ref`・`uniq` の属性は直接の storage に関するもので、証明なしに、読み込んだハンドルを通してペイロードやヘッダーへ `readonly`・`noalias` を広げない。

### 8.2. local の表現（Windows x64）

現行のヘッダー（+0 記述子、+8 control、+16 ペイロード）とペイロードの位置を保ち、control と side table を再利用する。

| 移行 | 目標 |
| --- | --- |
| `obj` から `rc`・`arc` | 公開の前に control = 2（強参照 1）を書く。ヒープ上の `obj` なら確保しない |
| `obj` から `local` | 状態を control に詰める。ヒープ上の `obj` なら確保しない |
| `local` の最初の `downgrade` | カウントと借用状態を、公開する一つの表へ移す。生きているガードは、そのときの表現に対して解放する |

- **control の候補**: 下位ビットは `rc` と同じタグにする。借用状態は一つのフィールドで、0 は未借用、全ビット 1 は排他、それ以外は共有数とする。排他の獲得は `(control & (TAG | BORROW_MASK)) == 0` の 1 回の比較で判定できる。
- 移管は生きている `obj` からだけ行い、破棄済みのオブジェクトを復活させない。
- `rc` と `local` の間（段階 B では `arc` と `sync` の間）で、カウント・Weak・解放の仕組みを共有する。
- ビット配置、上限、表の配置は、プロファイルで検証し測定する。本書は固定 ABI を変えない。

### 8.3. カウントの上限

- **上限の単位**: カウントの種類（強参照・弱参照・共有借用）とモードごとに、プロファイルが上限を定める。同じモードではインラインと表で同じ上限を使い、表現によって Abort の条件を変えない。
  - `rc`・`arc`: 現行 §21.2.3 の `MaxRefCount = 2^63 − 1`。
  - `local`・`sync`: 強参照数と共有借用数は、インラインの詰めた幅に収まる値とする（例えば `sync` は下位 3 ビットを使うので、強参照数は 2^61 − 1 以下）。弱参照数は表にだけあるので、現行の上限を使う。
- **検査と更新**
  - インラインの表現は更新前に検査する（現行 §21.2.3 の規則はここに限る）。更新は、`rc`・`local` では通常の読み書き、`arc`・`sync` では並行する移行を考慮した制御語全体の CAS で行う。
  - `arc`・`sync` の表のカウントは、`fetch_add` の後で検査し、結果を公開する前に Abort してよい。上限と表現の最大値の差は、プロファイルの最大スレッド数より大きくなければならない（1 スレッドが同時に持つ未検査の増分は一つ）。Windows x64 では最大スレッド数を 2^32 未満とし、u64 の余裕は約 2^63 である。
  - 検査に失敗したら、利用者のコードを呼ばず、巻き戻しもせず、直ちに Abort する。
- 表現の競合と、ゼロからの `upgrade` の禁止は、この仮定とは別の正しさの要件である。

### 8.4. 確保

- 通常の生成では、`obj`・`rc`・`arc`・`local` の確保を一つにすることを目標とする。`downgrade`、循環構築、待機の仕組みの費用は別に数え、全体で一つだという保証はしない。
- **その場での構築**: 生成の確保と、確保の失敗による Abort は、オペランドの評価の前後どちらで起きてもよい。`Item.init(...)@local` をオブジェクトの領域に直接構築し、Move による複製を省ける。
  - オペランドの効果、取得の順序と回数は変わらない。
  - 先に確保した領域は内部の一時値として通常の後始末に従う。遷移で評価を離れれば解放し、Abort と発散では解放を保証しない（§16.2.3）。

## 9. スレッド能力（段階 B）

### 9.1. 規則

`Kimi.ThreadTransferable`（TT）は値の所有を別のスレッドへ移せること、`Kimi.ThreadShareable`（TS）は別のスレッドから共有アクセスできることを表す。両者は独立した組み込みの保証である。Owned はどちらも導かず、どちらも寿命の義務を免除しない。

| 完全型 | TT | TS |
| --- | --- | --- |
| 組み込みの Scalar、Unit、`string` | 真 | 真 |
| Kimi 提供型 | 本書 §9.6 | 本書 §9.6 |
| ほかの owner Core | 構造導出（下記）または公開保証（本書 §9.2） | 同左 |
| 共通 Function Type | 偽（環境が消去される） | 偽 |
| `ref/T`・`objref/T` | TS(T) | TS(T) |
| `uniq/T`・`obj/T`・`objuniq/T` | TT(T) | TS(T) |
| `arc/T` | TT(T) かつ TS(T) | 同左 |
| `sync/T` | 形成できれば真 | 同左（TS(T) は要らない） |
| `rc/T`・`local/T` | 偽 | 偽 |
| `Weak<S>` | TT(S) | TS(S) |
| `ReadGuard<local/T>`・`WriteGuard<local/T>` | 偽 | 偽 |
| `WriteGuard<sync/T>` | 偽 | TS(T) |

- 表は組み込みの規則である。ガードは S の能力を受け継がず、隠れたフィールドの解析で表を上書きできない。
- **構造導出**: 格納された成分（フィールド、enum の payload、捕捉）の能力だけで決める。`deinit` やメソッドの本体の効果は調べないので、本体を変えても公開の能力は変わらない。
  - 自動の導出は Sealed な型に限る。open な Core は、自身の、または継承した公開保証によってだけ能力を持つ。
  - 継承した基底の部分のフィールドは、派生の完全なオブジェクトへ展開する。基底型自身の公開保証は要さないが、基底の opt-out は受け継ぐ。独立した `Animal` のフィールドや、`Animal` を対象とするハンドル・借用は、`Animal` の公開の能力に従う。
  - 生ポインターと消去された環境は能力を持たない（検証済みの契約による手動の適合は保留）。スレッドに縛られた資源を整数などで包む型は、opt-out で外す（本書 §9.2）。外す判断は、その資源を扱う unsafe・FFI のコードを書く側の責任である。
  - 結果と opt-out は公開要約（§18.7、§21.3.4）に含め、分割コンパイルと再検証のために依存を記録する。変更の扱いは §8.4.7.2 の Compatibility と同じとする。この証明は基底の private メンバーへのアクセスを与えない。
- **型が未確定の場合**: 宣言された制約を要する。同名のユーザー Contract や空の適合は何も証明しない。
- **callable**: Function Item と具体 Closure は構造導出の根拠を保つ。共通 Function Type は、元の callable に能力があっても持たない。能力が要るときは `F is Callable<owner, (i32) -> i32> and ThreadTransferable` のように具体型を総称の `F` のまま保ち、保存するときも消去しない。
- **ガード**: すべてのガードは移せず、獲得したスレッドで解放する。`sync` の書き込みガードへの共有参照から使えるのは `value` だけである。

### 9.2. 継承される宣言

> 組み込み要件のうち、型の宣言での表明が、その型とすべての派生型に及ぶものを「継承される宣言」という。

| 宣言 | 効果 |
| --- | --- |
| `Self is ThreadTransferable`・`Self is ThreadShareable` | 宣言した型とすべての派生型に検証の義務を課す。派生型が追加したフィールドも検証する |
| `Self is not ThreadTransferable`・`Self is not ThreadShareable` | 宣言した型とすべての派生型がその能力を持たない（opt-out） |
| `Self is not ObjectPayload`（§8.4.7.2、既存） | 宣言した型とすべての派生型が `ObjectPayload` を満たさない |

- 派生型は宣言を撤回できず、書き直す必要もない。同じ能力の肯定と opt-out が一つの型にそろうとエラーになる。条件付きの保証は、条件と置換を階層全体で保つ。
- 肯定の表明は義務であり、それ自体は証明にならない。opt-out は §8.4.7.2 と同じく、書ける場所と形（条件なし、単独の節、繰り返し不可）を持つ宣言で、検証する命題ではない。TT と TS の opt-out は互いに独立している。
- 通常の Contract と Copy の適合は、従来どおり継承しない（§8.4.4）。将来、実行時 Contract View が能力を要求するときもこの仕組みを使う。

```kimi
public open struct Shape
    Self is ThreadTransferable      // Shape とすべての派生型に検証の義務を課す
    public var id: i32

struct WindowHandle
    Self is not ThreadTransferable  // 作成したスレッドでだけ使える OS 資源を i32 で包む
    Self is not ThreadShareable
    var raw: i32

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

Object Target `T` について、`sync/T` の形成に追加で要るのは TT(T) だけである。

- **pair evidence**: 呼び出し側が形成した組の元の型 `s/T` は、Object Target の根拠（§8.1.1）に加えて、モードに固有の形成条件も与える。admitted set ⊆ {`sync`} なら、本体は TT(T) を使える。`local` を含みうる組には TT を要求しない。
- **新しい型の形成**: 組の元の型以外（`sync/T`、`s/U` など）を形成するときは、その条件を証明する。`s/U` では admitted set のすべての Semantics について証明する（§8.1.1）。

| 状況 | 根拠 |
| --- | --- |
| 型が未確定の値からの生成 | `T is ObjectPayload and ThreadTransferable` |
| 既知の Core からの生成 | §8.4.7.2 の直接の判定と TT(T)。open な型では TT の公開保証 |
| 有効なハンドル `s/T` からの移管・View の変更 | pair evidence。`sync` への移管では TT(T)、View の変更では V の対象の形成と Supports（`sync` なら TT(V)）を加える |

カウント、既知の現在のオブジェクト、最適化の推測で公開保証を強めることはできない。正しい絞り込みと checked cast は、通常の規則で新しい根拠を与えうる。View を変えると能力を失いうる。

```kimi
func createSynchronized<T>(value: T) -> sync/T
    T is ObjectPayload and ThreadTransferable
    return value@sync
```

### 9.4. 再帰的な証明

- TT・TS の構造上の義務と、それに依存する型の形成は、正で有限な依存の最大不動点としてまとめて解く。例: `Node -> Weak<sync/Node> -> sync/Node の形成 -> TT(Node)` は一つの有限な群になる。
- 先に群を作って外部の前提を確定させ、それから違反（`local`・`rc`、生ポインター、opt-out など）を伝播させる。open な型の保証は、open な完全値と View で検証の義務に展開し、継承した基底の部分の展開には要求しない。
- 通常の Contract の適合の循環は認めず、暫定の結果は公開しない。無限のインライン配置と型引数の際限ない増大は、既存の規則でエラーとする。完了した証明はキャッシュする。

### 9.5. 借用の受け渡しと static

- ガードから得た子の借用は、本書 §9.1 の通常の行に従う。ガードと `local` の管理操作は、獲得したスレッドにとどまる。
- 子の借用を別のスレッドへ渡せるのは、能力に加えて、競合するアクセスとガードの破棄より前に使用が終わると証明できるときだけである。そのためには scoped thread など完了を証明できる仕組みが要る（D.2）。本書は spawn・join の API を導入しない。
- **static**: static はすべてのスレッドから共有される storage なので、`let`・`var` を問わず、その完全型に TS を要求する（§11.3.2）。`let` の static でも、ハンドル操作などで状態を変えられるからである。
  - 可変 static への書き込み、static の遅延初期化・公開・終了時の破棄の同期、スレッドごとの static は D.2 で定める。
  - 段階 A で書ける `local`・`rc` などの static は、段階 B で拒否される。

### 9.6. Kimi 提供型

> 能力は §22.1 と utf8-formatting §1.1 のカタログで、内部表現によらない公開の契約として定める。表にない通常の struct・enum は構造導出し、表にない compiler-managed・verified intrinsic の型は能力を持たない。

| 型 | TT | TS |
| --- | --- | --- |
| `Array<T>`、Array と固定長配列の所有イテレーター | TT(T) | TS(T) |
| `Dictionary<K,V>` とその所有イテレーター | TT(K) かつ TT(V) | TS(K) かつ TS(V) |
| `Slice<T>` と Slice のイテレーター | TS(T) | TS(T) |
| Dictionary の共有ペアイテレーター | TS(K) かつ TS(V) | TS(K) かつ TS(V) |
| `HeapBuffer` | 真 | 真 |
| `WriteWindow`・`FixedBuffer` | 真（`uniq` のバイト列と同じ） | 真 |
| `Utf8Writer` | 偽（書き込み先を消去する） | 偽 |
| `Weak<S>`、ガード | 本書 §9.1 | 同左 |

- **イテレーターの共通規則**: 要素を所有するイテレーターは所有する要素に、共有借用するイテレーターは借用する要素の `ref` に従う。上の表のイテレーターの行は、この規則の適用である。
- 構造導出する型の例: `Option`・`Result`・`Index`・`Range`・`ResolvedRange` とその反復子・`BufferFull`・`InvalidUtf8`・`Utf8Slice`（`Slice<u8>` を持つので真）。固定長配列は Sealed なので構造導出する。
- 能力は完全型で決まり、実行時の内容（空のコレクション、`.None`、消費し終えたイテレーター）では変わらない。

## 10. sync（段階 B）

### 10.1. 意味

- アトミックの参照カウントと再入不可の Mutex を持つ。獲得は `write`・`tryWrite` だけで、読み取りだけの処理にも `write` を使う。Mutex に固定し、`read` は追加しない。並列の読み取りは別の Semantics または型として設計する。
- **順序付け**: 解放は release、成功した獲得は acquire の順序付けを持つ。失敗した `tryWrite` は同期を与えない。
- **デッドロック**: 同じオブジェクトの再獲得や、一貫しないロックの順序でデッドロックしうる。再入、検出、FIFO の公平性、有限の待ち時間は保証しない。
- **poisoning**: Abort は巻き戻さずにプロセスを終えるので、導入しない（§17.3）。

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
- **ロック状態の正本**: `LOCKED`・`HAS_WAITERS` は常にヘッダーの制御語の下位ビットに置き、表へ移行しても移さない。正本が一か所なので、古い場所を更新する問題は起きない。
  - 下位 3 ビットをタグとフラグに使うので、表の整列は 8 以上とする（現行の確保は 16）。ポインターの復元では 3 ビットを消し、上位ビットを保つ。`rc`・`arc` の 1 ビットのタグや固定の ABI は変えない。
- **カウント**
  - 表への移行は制御語全体の CAS で行い、最新のカウントとフラグを保つ。インラインのカウントの更新は表現を再確認する。無条件の `fetch_add` は、並行して公開された表のポインターを壊しうる。
  - 安定した表のカウントには `fetch_add`・`fetch_sub` を使ってよい（本書 §8.3）。
  - `upgrade` はゼロから復活しない条件付きの retain であり、`clone` の速い経路では代えられない。
- **獲得の速い経路の候補**（性能も命令も保証しない）

  | 方式 | 獲得 | 解放 |
  | --- | --- | --- |
  | CAS | `LOCKED` が 0 の語を CAS で更新 | CAS で `LOCKED` を消す |
  | ビット演算 | `fetch_or(LOCKED, Acquire)`。戻り値の `LOCKED` が 0 なら成功 | `fetch_and(~LOCKED, Release)`。戻り値に `HAS_WAITERS` があれば parking へ |
  | 事前確認付き | 読み取りで `LOCKED` を確かめ、0 のときだけ上のどちらかで更新 | 同左 |

  - ビット演算は、カウント・タグ・ほかのフラグを保ち、カウントだけの変化による CAS の再試行を避ける。事前確認は競合時の書き込みを減らすが、競合がなければ読み取りが一回増える。
  - 事前確認だけで眠ってはならない。待機の登録はキューのプロトコルの中で再確認する。
  - [parking_lot 0.12.5 の Mutex](https://docs.rs/parking_lot/0.12.5/src/parking_lot/raw_mutex.rs.html) は通常の経路で CAS を使っており、ビット演算の候補が検証済みである根拠にはならない。
- **待機の義務**
  - カウント・タグ・フラグの同時の遷移、待機の登録と解放、表への移行をまたぐ release/acquire、最後の解放を検証し、起こし損ないがないことを確かめる。
  - `HAS_WAITERS` の更新、状態の検証、キューへの登録はキューのプロトコルの下で調整し、起きたら正本の状態を確認し直す（[parking_lot_core の `park` の契約](https://docs.rs/parking_lot_core/0.9.12/parking_lot_core/fn.park.html)）。
  - 待っている呼び出し元はソースの Loan でオブジェクトを生かす。ヘッダーを再利用する前に待機の記録を外し、別の寿命のオブジェクトとアドレスを取り違えない。
  - キューの記憶域、初期化、競合、OS 資源、失敗は別に数える。
- **比較と測定**: カウントとロックを同じ語に置く方式を、生成時に安定した表を用意して別の語に分ける方式とも比べる。分離方式は確保が一つ増えるが、移行がなく、独立した操作が同じ語で競合しない（生成と移管での確保は本書 §4.2 が許す）。
  - 負荷: `clone` と破棄の多い負荷、獲得が競合する負荷、表への移行と獲得が重なる負荷。
  - 指標: 平均時間に加え、失敗した `tryWrite` の費用と、競合時の待ち時間。
  - 同居方式を経済的に検証できない場合や、測定で劣る場合は、分離方式を採る。同居方式では、Weak も競合もなければ生成時の確保を一つにできる。
- 共有して公開した後は、`arc`・`sync` の管理領域にアトミックと非アトミックのアクセスを混ぜない。現行の初期化と解放の順序付けに、Mutex の順序付けを加える。

## 11. 例

### 11.1. local の共有・変更・競合

```kimi
struct Item
    public var name: string

    public init(name: string)
        self.name = name@move

func inspectName(name: ref/string) => ()

var first = Item.init("initial")@local
var second = Kimi.Intrinsics.clone(first@ref)

using reader = first.read()
    inspectName(reader.value.name)
    match second.tryWrite()
        .None => ()                          // 同じオブジェクトが共有で獲得されている
        .Some(var writer)
            writer.valueUniq.name = "unexpected"

using writer = second.write()
    writer.valueUniq.name = "changed"
    inspectName(writer.value.name)
```

- `reader` は、最後の使用の後も破棄されるまで獲得を保つ。`tryWrite` を `write` に替えると、競合して Abort する。
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
    Kimi.Intrinsics.replace(guard@uniq, with: shared.read())   // 引数なので @uniq が要る
    guard = shared.read()                    // 代入も合法
    let taken = guard@move                   // guard は Moved
    inspectName(taken.value.name)
```

- 代入と `replace` は、新しい獲得を終えてから古いガードを破棄する（「解放してから獲得し直す」ではない）。読み取りの獲得は共存できるが、同じオブジェクトの排他のガードを解放の前に獲得し直すと、`local` では Abort、`sync` では自己デッドロックになる。
- `replace` は Unit を返し、`exchange` は古い値を破棄せずに返す。

### 11.3. ガードの生存中の Weak と、循環構築

```kimi
var strong = Item.init("initial")@local
using reader = strong.read()
    let weak = Kimi.Intrinsics.downgrade(strong@ref)   // 表へ移行する。reader の獲得は保たれる
    match Kimi.Intrinsics.upgrade(weak)
        .Some(let restored)
            using another = restored.read()            // 束縛したハンドルから獲得する
                inspectName(another.value.name)
        .None => ()                                    // 一般には最後の解放との競合に負けうる

struct Node
    public let selfWeak: Weak<local/Node>

    public init(selfWeak: Weak<local/Node>)
        self.selfWeak = selfWeak@move

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

let kept = using guard = shared.read() => guard@move   // 責任は kept へ移る。shared は kept より長く生きる

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
    return value@local              // 移管：pair evidence を使うので ObjectPayload は要らない

func acquireWrite<s/T>(value: ref/s/T) -> WriteGuard<s/T>
    s is guarded
    return value.write()            // 結果の source は value（§15.4.3）

func inspect<s/T>(handle: s/T) -> i32
    s is object and not guarded     // ペイロードへの直接アクセスには guarded を除く（本書 §3.4）
    let view: objref/T = handle@objref
    return 0

let createRc = func (value: Item) => value@rc   // 削除した makeRc の代わり
```

## 12. 統合

### 12.1. 段階

| 段階 | 内容 | 時期 |
| --- | --- | --- |
| A | 本書 §3〜§8（`sync` に固有の部分を除く）と §11 | いつでも取り込める |
| B | スレッド能力（本書 §9）、`sync`（本書 §10）、待機のランタイム | Appendix D.2 のスレッド設計と同時 |

- **段階 A**
  - `sync` は `async` と同じく Semantics の文脈で予約して拒否する。`guarded` の要素は `local` だけとし、admitted set の全体から `sync` を除く。
  - 仕様と実装を一つの単位で更新する。`@obj` による生成を実装し、`makeObj` の利用箇所（`Kimi/Library/Intrinsics.kimi`、`KimiLibraryCatalog.cs`・`KimiLibraryValidation.cs`、`milestones/Milestone14.kimi`、xUnit テスト 4 ファイル）を置き換えてから削除する。確定境界（本書 §5.5）に合わせて一時値の後始末の生成を改め、STATUS の対応範囲を更新する。
- **段階 B**
  - `guarded` の範囲が広がるので、`s is guarded` の本体を再検証する。static の TS（本書 §9.5）と、static の初期化・公開・終了時の破棄の同期を含める。
  - 分ける理由: TT・TS は別のスレッドへ渡す手段がなければ観測できず、捕捉の能力や完了の証明と一緒に決めないと手戻りが出る。

### 12.2. SPEC.md への影響

正式仕様は本書に依存しない形で更新する。STATUS は、実装と検証が済むまで、本書の機能を対応済みとして扱わない。

- **型と区分**: §2（文脈キーワード、`using` の認識）、§3.3（Semantics の表、区分）、§3.3.3、§3.3.5（Object Semantics）、§3.4（Owned Place、アクセス経路）、§3.2.2（Weak）、§8.7（admitted set 11 種）、§8.1.1・§8.4.7.2（object family、例の `boxed` を `value@obj`、`inspect` を `s is object and not guarded` に）、§15.3.1（`during`）、§15.3.5（変性）、付録 E（本書 §2 の用語）
- **ペイロードへのアクセス**（本書 §3.4）: §4.6.6、§7.3、§8.4（Contract 適合）、§12.4.4、§13.5.5、§13.5.7、§13.6
- **`@`**: §13.5、§13.5.3（転送の共通規則、生成、移管、総称の `@s`）、§13.5.8（make API の削除、counted、`makeCyclic`）、§13.5.9、§10.2・§13.5.8（`h@ref` の省略形を認める。現行でも §13.5.9 の `downgrade(strong@ref)` と §4.6 の `s[0]@ref` が省略形を使っている）、§22.1 と SPEC.md の索引
- **ガード**: §7.3・§9.5（ハンドル操作、メンバー検索）、§11（アクセサ）、§22.1 と索引
- **寿命**: §3.6.2（確定境界の規則と構文の表）、§4・§6.2.3・§12.3・§13.7・§14.2.3・§14.6.2・§15.1.6・§16.2.2（各構文からの参照。§15.1.6 は Copy による実装でも破棄の時点を借用と同じにする）、utf8-formatting §5.2・§5.3（穴の書き込みの完了を境界にする）、§15.7.3・§16.3.3・§16.4（置き換え中のハンドル自体の操作）
- **using**: §14.2〜14.5、§9（名前の可視性）、付録 F
- **ランタイム**: §21.2.3（`local` の表現、ハンドルの行、上限の単位、検査と更新）、§21.5.5（属性）
- **段階 B**: §3.3、§8.2・§8.4.7（TT・TS、opt-out、継承される宣言）、§6.2.2、§11.3.2（static の TS）、§18.7・§21.3.4（公開要約）、§22.1・utf8-formatting §1.1（Kimi 提供型の能力）、§21.2.3（`sync`）、付録 D.2

## 13. 検証

受け入れの義務であり、実行済みの結果ではない。診断は、該当する操作・束縛・Loan・呼び出しの経路・不足している証明を示す。

- **区分とアクセス**
  - 文脈キーワード、`async` の拒否、広げた区分と admitted set、object family
  - ペイロードへの直接アクセスの拒否（本書 §3.4 の表のすべての操作、総称の admitted set を含む）と、ハンドル自体の操作の受理
  - 変性（`local/T`・`Weak<local/T>`・guarded を含む `s/T` で Origin を短縮できず、`rc/T` は変わらず、View の upcast は受理）
- **`@`**
  - 表のすべての行と確保の有無、転送の共通規則（`count@rc` で Moved、`count@i32@rc` で元が残る）
  - 同一性と依存の保存、観測されたアドレスの保存と再配置、`ObjectPayload` の要否（生成は要し、移管は要さない）
  - 生成と View の変更の組み合わせの拒否、総称の `@s` の種類の混在の拒否、make API の削除と closure による代替
  - `h@ref` と `h@ref/s/T` が同じであること、`clone(first)` の拒否
- **ガード**
  - 形成（`ReadGuard<sync/T>` とオブジェクト化の拒否）、phantom な `source` の変性と Loan 要件
  - 受信者（スロットの共有借用、`let` からの獲得、基底の宣言を含むペイロードのメンバーは object-kind）
  - メンバー検索（ペイロードが見つからないことと診断、admitted set が混ざる場合の拒否、同名のユーザー宣言に特権がないこと）
  - `valueUniq` の暗黙の排他取得と、`let` の束縛での拒否
- **寿命**
  - ソースのスロットへの依存、一時値のソースより長く生きるガードの拒否
  - 確定境界: 構文の表のすべての行、部分式ごとの破棄対象、親の境界での再判定（本書 §5.5 の例）、代入先と借用され続ける一時値の寿命、総称での束縛ごとの決定、補間の穴と `$tryWrite` の途中失敗、Place の主語では Copy による実装でも破棄の時点が同じこと
  - 通常の Move・代入・部分 Move と残った部分の後始末、ガードを返す戻り値とぶら下がる子の結果の拒否
  - 置き換え（新しい獲得の後の解放、`local` の Abort、破棄中のほかのハンドルからの型テストと `clone`）
- **要素・Weak・循環構築**: SharedReadResult の原則が現行の表と一致すること、counted の四つのモード、`makeCyclic` の推論、Owned・ObjectPayload、Building 中の `None`、一度だけの公開、逃げた Weak と最後の解放。
- **using**: 認識と確定、本体が必須であること、入れ子、外側の環境での解決、本体の宣言が見えないこと、ラベルの範囲、単項目と字下げの本体の結果、Discard Context、Never、名前付き `exit`、外への遷移。
- **ランタイム**
  - `local` の競合、try の結果、上限超過の Abort（結果を公開する前）、折り返しや飽和による成功がないこと
  - 生きているガードがある状態での表への移行、正本の状態が一つであること
  - 上限の単位（種類とモードごと、表現によらない）、インラインの更新前の検査とモード別の更新方法、表の余裕の条件
  - 先に確保した領域が遷移で解放されること、省略の最適化が観測できる挙動を保つこと、確保を保証とは別に測ること、属性の検証
- **段階 B**
  - TT・TS の表、構造導出、継承される宣言（肯定と opt-out の衝突を含む）、公開保証のない open な型の独立したフィールドの拒否、失敗の再帰的な伝播
  - pair evidence による TT(T) の利用と新しい型の形成の証明、static の TS、Kimi 提供型の行とイテレーターの共通規則、既定の規則
  - `sync` の排他と try、順序付け、待機の鍵、3 ビットの予算、獲得の方式の比較、移行の CAS、登録・解放・移行の競合、アドレスの再利用後の誤った起こし

## 14. 範囲外と将来

| 項目 | 扱い |
| --- | --- |
| `async` | Semantics の文脈で予約する。将来の `await source.write()` と非同期のガードは別に設計する。獲得前のキャンセルでは待機の登録を外し、獲得後のタスクの破棄では通常の後始末を行う。`ReadGuard` と同期の `WriteGuard` は await をまたがない |
| 強参照を所有するガード | 将来の課題。一時値のハンドル（`upgrade` の結果など）から、保持できる獲得を得られるようにする |
| counted の間の変換、`obj` への復帰、循環の回収 | 導入しない |
| 再入とデッドロックの検出 | 導入しない。スレッド ID のフィールドも必須にしない |
| 手動の unsafe 能力の適合 | 具体的な安全の契約ができるまで保留する |
| 能力を持つ消去された callable 型、スレッドごとの static | D.2 で決める |
| 実行時 Contract View | §8.5 に従う |

## 15. 決定

採用する。本文で理由を述べた選択（ペイロードへのアクセスの共通規則、ハンドルにメンバーの `clone` を置かないこと、ガードの命名、総称の `@s` とメンバー検索、確定境界の持ち越しと再判定、`using` の形、段階の分割）は再掲しない。ほかに不採用とした案は次のとおりである。

- **名前と綴り**
  - **`local` を `cell` などに改名する案**: 待ち方の系列（`local`・`sync`・`async`）が崩れる。用語の衝突は表記の規約（本書 §2）で避ける。
  - **ガードの獲得を「取得」と呼ぶ案**: 現行仕様の acquisition と区別できない。
  - **ハンドル操作の関数形を併存させる案**: 同じ操作の綴りが二つになる。
  - **実引数でスロットを暗黙に借用する案（`clone(first)`）**: 外側に層を足す借用は綴りで見せる（§10.2）。
- **意味**
  - **生成で Copy の元を残す案**: 「所有 Semantics の指定は転送」という規則に例外が入る。
  - **`object` を `obj`・`rc`・`arc` のままにする案**: §8.4.7.2 の object family と一致せず、`local`・`sync` だけが別扱いになる。
  - **make API を残す案、`makeLocal`・`makeSync` を加える案**: 生成の綴りが二つになる。
  - **モードごとの循環構築の関数**: counted を増やすたびに API が増える。
  - **移管での再配置を一律に禁止する案**: 観測されないアドレスまで固定し、`obj` の配置の最適化を妨げる。
  - **`sync` に `read` や RwLock を加える案**: Mutex の表現と両立しない。
  - **TT・TS の継承を Contract 一般に広げる案**: §8.4.4 の方針を変える必要がある。名前のある種類（継承される宣言）に限る。
  - **`deinit` やメソッドの効果から TT・TS を導く案**: 本体を変えるだけで公開の能力が変わり、FFI を使う型が TT にならない。
  - **別のスレッドから使う static だけに TS を要求する案**: 捕捉のない関数からも static を使えるので、呼び出しをまたぐ効果の解析が要る。
  - **`using` の束縛の保持を保証する案**: 通常の `var` の規則に例外が入る。
- **寿命とランタイム**
  - **一時値を §3.6.2 のまま最も外側の式の終わりまで残す案**: ガードで自然なコードが Abort・デッドロックする。
  - **一度借用された一時値の寿命を最も外側の式まで固定する案**: `f(readCount(h.read().value), h.write())` のように、借用が終わった後もガードが残る。
  - **一時値の破棄を最後の使用で決める案**: 破棄の時点が解析の精度に依存し、決定的でなくなる。
  - **Copy 型の Place の主語を Copy で取得する案**: 取得規則をもう一つ変えることになり、総称で Copy が未確定のときに条件付きの取得が要る。
  - **表のカウントも CAS による更新前の検査に限る案**: 本書 §8.3 の余裕の条件で足り、速い経路が遅くなるだけである。
