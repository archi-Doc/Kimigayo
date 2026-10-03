# 仕様変更案：共有オブジェクト所有権 `rc/T`・`arc/T`

日付：2026-10-03（最終版）

状態：最終版。本書 2 の変更は、2026-10-03 にユーザーが決定した。正式仕様への取り込み（U0）と実装は未実施である。基準は `dev` の `92b6cb9b`。

本書は、`rc/T`・`arc/T`（マイルストーン P34）を実装するための仕様の整理と、その実装計画を定める。

- 仕様の変更は本書 2 の四点だけである。どれも受理されるプログラムの集合と実行時の振る舞いを変えない。
- 本書 3 以降は仕様項目ではない。実装計画と調査の記録である。
- 本書で変更する事項は、SPEC.md とその参照先より優先する。変更しない事項には、既存仕様を適用する。
- SPEC の節は「§n」、IMPL の節は「IMPL §n」、本書の節は「本書 n」と書く。実装の位置は、`src/Kimi/Compiler` からの相対パスで書く。

## 1. 現仕様の問題

1. **強参照の適格性が、宣言に現れない規則で書かれている。** §13.5.8 は、`clone` の `S` の適格性を「intrinsic formation rule」としている。§3.2.2 は、総称の場合の `S` を三通りに分けて説明している。どちらも、既存の Semantics 要件で一つに書ける。
2. **参照カウントの上限超過に Abort コードがない。** 上限での増分は Abort する（§13.5.8、IMPL §21.2.3.1）。しかし §22.5.4 の目録には、そのコードがない。
3. **オブジェクトハンドルのメンバー選択が明示されていない。** §3.4.1 は、選択を続ける層として、安全な値参照と pair 層しか挙げていない。`h.id`（`h: rc/Payload`）のように、ハンドルの View Target のメンバーを選ぶことは書かれていない。
4. **arc の弱いメモリー上の検証に手段がない。** IMPL §21.2.3.3 は「Weak-memory behavior … is validated」と求めているが、手段は定めていない。遷移も候補表があるだけで、採用する遷移は決まっていない。

## 2. 仕様の変更点

### 2.1. 強参照を受け取る API と `Weak` の宣言形（§3.2.2、§13.5.8、§13.5.9、§22.1、§22.1.1）

既存の強参照を受け取る API と `Weak` は、pair 引数 `<s/T>` と Semantics 要件 `s is rc or arc` で宣言する。

```kimi
// Kimi.Intrinsics
public func makeRc<T>(value: T) -> rc/T
    T is ObjectPayload
public func makeArc<T>(value: T) -> arc/T
    T is ObjectPayload
public func clone<s/T>(value: ref/(s/T)) -> s/T
    s is rc or arc
public func clone<s/T>(value: ref/Weak<s/T>) -> Weak<s/T>
    s is rc or arc
public func downgrade<s/T>(value: ref/(s/T)) -> Weak<s/T>
    s is rc or arc
public func upgrade<s/T>(value: ref/Weak<s/T>) -> Option<s/T>
    s is rc or arc

// Kimi
public struct Weak<s/T>          // コンパイラー管理の Non-Copy struct。空の構築子を持たない
    s is rc or arc
```

**規則**

- pair 引数は、一つの完全な型を一つのスロットで束縛する（§8.1.1）。そのため `clone<rc/Item>(h)` の明示も推論も、これまでの `<S>` と同じに働く。
- 本文では、これまでどおり `S` を `s/T` 全体の意味で使う。
- 二つの `clone` は、引数型で排他になる。`ref/(s/T)` に `ref/Weak<rc/X>` を渡すと `s = owner` になり、要件が否定される。
- 受理される集合は変わらない。§3.2.2 の三通りは、この要件から導かれる。
  - 具体的な `rc/X`・`arc/X`
  - `X` が Object Target である総称の `rc/X`・`arc/X`
  - admitted set が {`rc`, `arc`} に含まれる pair
- 裸の payload、`obj`、object 借用、`Weak` 自身は、`s` が `owner`・`obj`・`objref`・`objuniq` になるので拒否される。

**各節の変更**

| 節 | 変更 |
| --- | --- |
| §3.2.2 | 「`Kimi.Weak<s/T>` は `s is rc or arc` を要する」とする。総称の三通りは、この要件から導かれる例として注に残す |
| §13.5.8 | 「Eligibility is an intrinsic formation rule …」を二つの文に分ける。①ObjectPayload は内在の要件で、利用者の Contract ではなく、現在のオブジェクトと実行時 Contract の境界を広げない。②既存の強参照を受け取る API は、`s is rc or arc` で適格性を表す。API 表の `clone` を pair 形にする |
| §13.5.9 | 「`S` has the eligibility of §3.2.2」を残し、API 表を pair 形にする |
| §22.1 | `Weak<S>` の行を `Weak<s/T>` にする |
| §22.1.1 | `clone`、`downgrade`、`upgrade` の行を pair 形にする |
| LIBRARY.md | 5.2・5.3 の署名をそろえる |

```kimi
let h = Kimi.Intrinsics.makeRc(Item.init(1))
let a = Kimi.Intrinsics.clone(h@ref)             // s = rc, T = Item
let b = Kimi.Intrinsics.clone<rc/Item>(h@ref)    // 明示の型引数も一つのスロット
let o = Kimi.Intrinsics.makeObj(Item.init(2))
// Kimi.Intrinsics.clone(o@ref)                  // Error: s = obj は s is rc or arc を満たさない

func keep<s/T>(handle: ref/(s/T)) -> s/T
    s is rc or arc                               // 総称の呼出し元も同じ要件を書く
    return Kimi.Intrinsics.clone(handle)
```

### 2.2. 上限超過の Abort コード（§22.5.4、IMPL §21.2.3.1）

- §22.5.4 の目録に、`KIMI_E_REF_COUNT: Reference count limit exceeded` を加える。
- 強参照と weak の数が上限（`MaxRefCount`）で増分しようとしたら、更新の前にこのコードで Abort する。
- 報告位置は、増分を行う明示の操作の呼出し式の始まりとする。そのような操作は `clone`、`downgrade`、`upgrade` だけで、暗黙の増分はない。
- IMPL §21.2.3.1 の「An increment at the maximum Aborts before updating」には、§22.5.4 への参照を加える。

```kimi
let copy = Kimi.Intrinsics.clone(h@ref)   // 数が上限なら、この式の位置で KIMI_E_REF_COUNT
```

### 2.3. オブジェクト層のメンバー選択（§3.4.1）

§3.4.1 に、次の一文を加える。

> オブジェクトハンドル（`obj`・`rc`・`arc`）と object 借用（`objref`・`objuniq`）の層では、その View Target のメンバー、index、受信者の宣言を選び、選択はその層で止まる。

- 選んだ Place の権限は、§13.5.5.1 の表による。
- 基底のメンバーは §9.5.1、オブジェクトの受信者は §12.4.3–§12.4.4 による。

```kimi
struct Payload
    public var id: i32
    public init(id: i32) => self.id = id

func read(h: rc/Payload, v: objref/Payload) -> i32
    return h.id + v.id   // どちらも View Target Payload の Field を選ぶ
// h.id = 2             // Error: rc の経路は共有アクセスだけ。h が var でも同じ（§13.5.5.1）
```

### 2.4. arc の数え方の遷移と検証（IMPL §21.2.3.3）

IMPL §21.2.3.3 の候補表を、次の遷移表に置き換えて採用する。

- §21.2.3.3 の正規の矢印は変えない。
- rc は、同じ遷移を原子操作なしで行う。

| 遷移 | 更新対象と成功条件 | arc の ordering |
| --- | --- | --- |
| 生成 | 公開前のヘッダー（記述子、制御語 2）と payload を書く | 通常の store |
| inline の保持 | 制御語が偶数で上限未満なら +2。奇数なら表の保持へ | CAS：成功・失敗とも monotonic |
| inline の手放し | 制御語が偶数なら −2、奇数なら表の手放しへ。2 → 0 の成功だけが最後 | CAS：成功 release、失敗 monotonic。最後は fence acquire の後で破棄と返却 |
| 表の解決 | 奇数の制御語から、表のアドレスを得る | acquire |
| 移行（最初の `downgrade`） | 制御語を「表のポインター｜1」へ変える。成功だけが数の権限を表へ移す。失敗したとき、偶数なら最新の数で再試行する。表を見つけたら、候補を返却してその表を使う | 成功 acq_rel。表の発見は「表の解決」による |
| 表の strong の保持 | strong が上限未満なら +1 | CAS：monotonic |
| `upgrade` | strong が 0 なら None、上限なら Abort、それ以外は +1。成功の後でだけ `table.object` を読む | CAS：成功 acquire、失敗 monotonic |
| 表の strong の手放し | −1。旧値 1 が最後で、破棄 → 返却 → weak guard の手放しの順に行う | `fetch_sub` release。最後は fence acquire |
| weak の保持 | weak が上限未満なら +1（`downgrade`、Weak の `clone`） | CAS：monotonic |
| weak の手放し | −1。旧値 1 が最後で、表を返却する | `fetch_sub` release。最後は fence acquire |
| 循環構築の公開 | strong を 0 → 1 にする。構築の権限を持つ工場だけが行える | release |

**検証の手段。**「Weak-memory behavior, not just possible interleavings, is validated」を、次の二つで置き換える。付録 A の「weak-memory ordering proofs separately from IR/native tests」と一致する。

1. **証明。** 表の各行が、§21.2.3.3 の矢印と、§21.2.3.2 の不変条件を満たすことを示す。不変条件は次のとおりである。
   - 数の管理先は一つである。
   - 破棄と返却は、0 にした成功した更新の後でだけ行う。
   - 返却の後はヘッダーを読まない。
   - 上限では、更新の前に Abort する。
   - 移行は数を変えず、元に戻らない。
   - 失効した表のオブジェクトポインターは参照しない。
2. **生成 IR の検査。** 各行について、ordering、更新対象、成功の分岐、返却後にヘッダーを読まないことを確かめる。

数え方そのものは、native の単一スレッドのテストで確かめる。モデル検査と実機の負荷テストは要求しない。

**公開の範囲。** 表の「公開」は、生成したスレッドへの返却である。通常の return は、別スレッドとの同期を作らない。別スレッドへ渡す機構は付録 D.2 の対象で、その機構が同期を与える。

### 2.5. 変更しない事項

次の事項は変えない。

- 受理されるプログラムの集合
- `obj` と Weak の意味
- 制御語とサイドテーブルの表現（IMPL §21.2.3.1）
- §21.2.3.3 の正規の矢印
- nonnull Option 表現（IMPL §21.1.5）
- 並行実行を保留していること（付録 D.2）

## 3. 実装計画（仕様項目ではない）

### 3.1. 決定事項（2026-10-03、ユーザー）

1. マイルストーンは PLAN の順（P40 → P26 → …）に進める。rc/arc の単位は、同じセッションで P40・P26 の単位と交互に進める。
2. 本書 2.1・2.2・2.4 を採用する。arc の検証では、モデル検査も、実機の負荷テスト（x64 の多スレッド、ARM64）も行わない。
3. nonnull Option 表現は、rc/arc の作業の中で、P35 より前に入れる（U8）。

### 3.2. 用語

| 語 | 意味 |
| --- | --- |
| 種別 | 所有ハンドルの Semantics。obj・rc・arc |
| 保持 | 強参照の数を一つ増やす（`clone`） |
| 手放し | 強参照の数を一つ減らす。1 → 0 が最後の手放しで、その後に破棄と返却を行う（§16.3.3） |
| 破棄 | payload を `destroyValues` で破棄する |
| 返却 | オブジェクトの記憶域を `freeStorage` で返す |
| 片付け | ハンドルの責任を果たすこと。obj では破棄と返却、rc/arc では手放し |

### 3.3. 範囲

**目的。** P34 を、PLAN §5 の完了条件で完成させる。

1. Program 34 を変更なしで O0/O2 でビルド・実行でき、期待する出力になる（本書 3.6）。
2. `test-milestone34.ps1` が通る。
3. Session 検証が通る。
4. Program 34 が使う SPEC 各節に、肯定と否定の焦点テストがある。
5. 隣接する未対応の形は、誤ったコードではなく診断で止まる。

**含む。**

- 生成と複製：`makeRc`、`makeArc`、強参照の `clone`
- ハンドルの扱い
  - Move、受け渡し、結果、代入による置換
  - 明示と暗黙の `objref` 適応（§10.2、§7.3）
  - `@follow@ref`、Field の読取り
- 手放し、arc の原子的な数え方
- 置き場所と変換
  - 集成体、コレクション、Option、Closure、総称インスタンス内のハンドル
  - 同じ種別の中での upcast と `is`
- nonnull Option 表現（U8）。P34 の完了条件ではない。Toolchain T2 の前半（`Option<ref/T>` の import 引数）を引き取る。

**含まない。**

- Weak、`downgrade`、`upgrade`、循環構築（P35。本書 3.7）
- 静的記憶域（P36）、フロー精密化（P33）、Program 38
- obj ハンドルを通した Field 書込み（P33）

### 3.4. 設計

**種別と二つの性質。**

| 種別 | payload の権限（§13.5.5.1） | 数える段 | 制御語の初期値 |
| --- | --- | --- | --- |
| obj | 排他を持てる | なし | 0 |
| rc | 共有だけ | 非原子的 | 2 |
| arc | 共有だけ | 原子的 | 2 |

- **一つの問い合わせ。**
  - `ObjectTypes.IsOwner`（obj のみ、15 ファイル 24 か所）を、種別を返す一つの問い合わせに置き換える。
  - 各箇所は、必要な性質だけを使う。
  - 種別の分岐は網羅的に書き、既定の分岐を持たない。
- **種別は変換で保存する。**
  - 所有ハンドルの変換が変えるのは、View Target だけである（§13.5.7）。
  - `BindObjectUpcast`（`Binding/Binding.ObjectViews.cs`）に、種別の一致を加える。
- **配置の同一性を種別ごとに分ける。**
  - `AggregateLayout`（`Emission/AggregateLayout.cs`）の `objectHandle` を、種別ごとに分ける。
  - バイト配置は同じでも、集成体の破棄関数と生成キャッシュは種別を区別する。
- **共通の後半。**
  - 破棄と返却は、一つの補助関数（例 `__kimi_object_finalize`）にする。
  - obj はこれを直接呼ぶ。rc/arc は、0 にしたときだけ呼ぶ。
- **共有の適応は種別によらない。**
  - 権限が「共有」で足りる操作は、三種別に同じ規則で許す。対象は、`objref` 適応、共有受信者、`@follow@ref`、Field の読取りである。
  - `AdaptObjectBorrow`（`Binding/Binding.ArgumentOperations.cs`）は、暗黙の適応を obj に限っている。これを、権限で判定するように改める。
- **種別を開く条件。**
  - 種別は、所有権解析の `SupportsType`（`Analysis/OwnershipAnalysis.Enums.cs`）で開く。開く前は、使用位置で `UnsupportedOwnership_Kd` になる。
  - 開いた時点で、受理される操作と置き場所は、正しく動くか、診断で止まる。`SupportsType` は集成体の要素も再帰的に判定するので、開けば集成体の中のハンドルも受理される。
  - rc は U4、arc は U5 で開く。

**表現と工場。**

- ハンドルは、三種別ともヘッダーを指す 8 バイトのポインターである。
- 記述子は、同じ Dynamic Type の三種別で共有する（IMPL §21.2.2）。
- 工場の生成処理は三種別で共通とし、違いは制御語の初期値だけとする。

**数える手順。**

- 補助関数は、生成する IR として作る。原子的な順序付けの操作は付録 D.2 が保留しており、最後の手放しには記述子を通した間接呼出しが要るからである。理由は `src/Kimi/Library/README.md` に記録する。
- `MaxControl` は `MaxRefCount << 1`（2^64 − 2）である。

```text
rc の保持    c = load [h+8];  c == MaxControl なら Abort;  store c + 2
rc の手放し  c = load [h+8] - 2;  store c;  c == 0 なら finalize
arc の保持   c = load monotonic [h+8]
             繰り返し: c == MaxControl なら Abort
                       (ok, c) = cmpxchg c → c + 2（monotonic/monotonic）; ok なら終わる
arc の手放し c = load monotonic [h+8]
             繰り返し: (ok, c) = cmpxchg c → c − 2（release/monotonic）; ok なら抜ける
             成功した c が 2 なら fence acquire; finalize
```

- **表現の判定は P35 で入れる。** P34 には表がないので、奇数の制御語は生じない。P35 で `downgrade` を受け入れるのと同じコミットで、すべての更新経路に入れる（本書 3.7）。
- **展開と配置は測って決める。** 速い経路の展開、補助関数への分け方、`cold` の付け方は、生成と破棄が中心の処理と、複製が中心の処理の両方を `src/Benchmark` で測って決める。

**所有権の意味。**

- **権限。** rc/arc の payload への権限は、書込み可能な `var` の経路でも共有だけである（§13.5.5.1、§12.4.4）。
- **結果の依存は、結果契約が決める（§15.6.4）。**
  - 結果の型が名指す Origin に対応する Loan だけが続く。
  - `clone` の結果 `s/T` は `ref` の Origin を名指さないので、スロットの借用は呼出しで終わる。
  - これに対し、`func field(p: ref/Box) -> ref/i32 during p` の結果は、`p` の借用を保つ。
  - 実装の `SoleResultInput` がこの規則に従うことを、U4 で確かめる。

  ```kimi
  let number: i32 = 5
  let a = Kimi.Intrinsics.makeRc(Counter.init(number@ref))
  let b = Kimi.Intrinsics.clone(a@ref)   // a のスロットの借用は、この呼出しの間だけ
  consume(a@move)                        // consume(h: rc/Counter)。b は a のスロットに依存しない
  require b.value == 5 else => $abort("lost")
  ```

- **手放しは、保守的に破棄の観測とする。** どの手放しも最後になりうる。そのため、obj の片付けと同じく、次の二つに含める。
  - 破棄時の寿命検査（`Analysis/OwnershipBody.Borrows.cs`）
  - 破棄 effect（`Binding/Binding.EffectBounds.cs`）

**ハンドルからの直接の Field 読取り（本書 2.3）。**

- 既存のオブジェクト view の Field 経路（`ReadBorrowedField`、payload への +16）を、所有ハンドルにも適用する。
- 書込みは P33 に残す。

### 3.5. 作業単位

各単位は、再現・実装・焦点テストを一組にする。Unit 検証の後にコミットし、push する。

- **U0 仕様**
  - 本書 2 を正式仕様と LIBRARY.md に取り込む。
  - `draft/INTEGRATED.md` に「取り込み済み」として記録し、本書を `draft/Changes` へ移す。本書 3 以降は仕様項目ではなく、進捗は PLAN が持つ。
  - 確認：SPEC の例をすべてパースする。
- **U1 種別と性質の導入（挙動は変えない）**
  - 一つの問い合わせ、種別の保存、種別ごとの配置、共通の後半を入れる。`SupportsType` は obj だけを開く。
  - 確認：既存の Object 系のテスト（`ObjectRuntimeTest`、`PipelineObjectTest`、`RuntimeTypeTest`、`BorrowAcquisitionTest`、`CallReservationTest`）と native fixture。診断スナップショットが変わらないこと。
- **U2 直接の Field 読取り（obj）**
  - 再現：本書 4.1 の `first.id`、`handle.id`、`counter.value` を obj で通す。
  - 確認：`ObjectRuntimeFieldRead*.ll`（O0/O2）。書込みが診断で止まること。
- **U3 宣言と Binding**
  - 実装
    - `src/Kimi/Library/Intrinsics.kimi` に、`makeRc`・`makeArc`・強参照の `clone` を宣言する。目録の Function 種別と検証も加える。
    - `KimiLibrary.IsCompleteOwnershipFamily` を、P34 の部分（ID 24–26）と全体に分ける。
    - 暗黙の `objref` 適応を、権限で判定する。
    - 本書 4.2 の診断三件を直す。
    - `CoreCatalogTest` の数と STATUS を合わせる。
  - 確認：`SharedObjectBindingTest`（新設）
    - 推論と明示の型引数
    - 非 rc/arc の拒否
    - 暗黙の適応（引数と `objref/Self` 受信者）
    - 同じ種別の upcast の肯定
    - 異なる種別の間の変換（六通り）と、借用から所有を得る変換の否定
  - 段階基準：Program 34 は Binding を通り、所有権で止まる。
- **U4 rc を開く**
  - 所有権の規則は rc と arc で共通に書き、開くのは rc だけにする。
  - 所有権：Move、消費、結果、代入による置換、`objref`、`@follow@ref`、Field の読取り、`clone`、結果の依存、破棄の観測
  - 生成：工場、保持と手放し、`clone`、upcast、`is`、種別ごとの `destroyValues` と片付け、`KIMI_E_REF_COUNT`
  - 確認：`SharedObjectOwnershipTest`（新設）
    - 肯定：複製の後に元のハンドルを Move・片付けしても、複製を使えること。結果の依存が結果契約に従うこと。
    - 否定：payload の借用中の Move。`var` のハンドルで `h@uniq` が生きている間の `clone(h@ref)`。スコープを越える `rc/Counter{number}`。`@follow@uniq`、`@objuniq`、排他受信者。arc の `UnsupportedOwnership_Kd`。
  - 確認：`SharedObjectRuntimeTest`（新設）と native fixture `SharedRc*.ll`（O0/O2）
    - 破棄が一度だけで、順序が正しいこと。Move で数が変わらないこと。
    - 置き場所ごとの片付け：`(rc/T, i32)`、struct Field、`Array<rc/T>`、Dictionary の値、`Option<rc/T>`、capture
    - 上限超過：IR 検査に加え、同じモジュールに追記したテスト用 driver で確かめる。製品にテスト用の入口は作らない。
    - `NativeAllocationAudit`（`Purpose=Allocation`）：`makeRc` で確保 1 回、`clone` と最後でない手放しで 0 回、最後の手放しで返却 1 回。
  - 段階基準：Program 34 は arc の部分で止まる。
- **U5 arc を開く**
  - 生成：CAS と ordering。
  - 確認：本書 2.4 の表の P34 の行を、生成 IR の検査で一行ずつ確かめる。`SharedArc*.ll` で U4 と同じことを確かめる。Program 34 全体が期待どおりに出力すること。
- **U6 残りの隣接する形**
  - U4・U5 の時点で診断で止まっている形を動かす。
  - 対象：ハンドルスロットの `swap` と `replace`。総称インスタンスの本体での工場呼出し（`Emission/ObjectGenerationPlan.cs` が拒否している。単相化に従い、三種別まとめてインスタンスごとに計画する）。
  - 確認：動かした形に肯定の native テストを置く。残る形が診断で止まること。
- **U7 P34 の完成**
  - `test-milestone34.ps1` を加え、`tests/milestones/README.md` と `stage-baselines.json` を更新する。
  - STATUS、PLAN（G4 を P35 の Weak 側に縮める）、CODEMAP を更新する。
  - `src/Benchmark/SharedObjects.md` に測定を置く。
  - 確認：`-Milestone 34` と Session 検証
- **U8 nonnull Option 表現（P35 の前提）**
  - 実装
    - IMPL §21.1.5 のとおり、`Option<R>` を一語で表す。R は七種、null は `None` で、`None` は片付けの責任を持たない。
    - `Option<ref/T>`・`Option<uniq/T>` の import 引数を受け付ける。
    - U8 までは、ハンドルの Option はタグ付きのままで、IMPL §21.1.5 との既知の差として STATUS に記録する。
  - 確認：`NonnullOption*.ll` と `ForeignBorrowImportTest`
    - 大きさ・整列・stride がすべて 8 であること。
    - `Option<Option<R>>` と `Option<(rc/T, i32)>` がタグ付きのままであること。
    - 関数の境界で `None` と `Some` を渡すこと：一般の関数の引数と結果、総称関数、集成体への格納、FFI。
    - `Some` だけを片付けること。

### 3.6. 順序・期待出力・検証

```text
U0 ─ U1 ─┬─ U2 ─┐
         └─ U3 ─┴─ U4 ─ U5 ─ U6 ─ U7 ─ U8 ─ (P35)
```

- U4 は、Program 34 の Field 読取りに U2 を使う。
- P34 は、P33 の残り（フロー精密化、継承 Field の射影）に依存しない。

Program 34 の期待出力：

```text
Handle reads payload 1.
Cloned handle shares payload 1.
Released one strong handle.
Payload 1 destroyed.
Arc views read payload 2.
Payload 2 destroyed.
Borrowed counter is 5.
Shared ownership finished.
```

Unit 検証の例（クラスの列は `pwsh -Command` で渡す）：

```bash
pwsh -Command "./scripts/verify.ps1 -Class SharedObjectOwnershipTest,SharedObjectRuntimeTest,ObjectRuntimeTest,CoreCatalogTest -Fixtures 'SharedRc*.ll' -Milestone 34"
```

- **機能。** 誤ったコードは、破棄の印字と、`NativeAllocationAudit` の確保・返却の数で検出する。
- **arc。** 本書 2.4 の証明と IR の検査で確かめる。STATUS には、弱いメモリーについて確かめた範囲は「証明と IR の検査」であると記録する。
- **診断。** U3・U4 の診断の追加・変更は、Diagnostic Development Workflow の五段を踏む。

### 3.7. P35（Weak）への接続

- **宣言。** 本書 2.1 の形にする。Weak の `clone` には新しい安定 ID を加え、強参照の `clone`（ID 26）は保つ。
- **Weak の内部。** コンパイラー管理にするか、非公開の `Storage` 原始操作の上に Kimigayo ソースで書くかは、P35 の着手時に決める。
- **サイドテーブル。**
  - `ensureSideTable`、`tryRetainStrong`、`retainWeak`/`releaseWeak` を加える。
  - `downgrade` を受け入れるのと同じコミットで、表現の判定と weak guard を入れる。
  - 表現の判定は、制御語を更新するすべての経路で、算術の前と CAS が失敗した後に行う。偶数は inline の数、奇数は表である。
- **`upgrade` の結果。** U8 の nonnull 表現の上に載る。
- **循環構築。** 単相化により、`F` はインスタンスごとに具体的な Closure になる。所有受信者による Consuming 呼出しを直接生成できる見込みなので、着手時に試行で確かめる。`T is Owned` と Building 状態（strong 0、weak 2）も扱う。
- **Program 38。** P33、P36、P24 も要るので、P35 では Weak の段階まで進めるだけである。

## 4. 調査の記録（2026-10-03、CLI での試行を含む）

### 4.1. 段階ごとの状態

| 段階 | 状態 | 位置 |
| --- | --- | --- |
| 型の形成 | `rc/T`・`arc/T` は形成され、受理される | `Binding/Binding.Types.cs` |
| Binding | 共有限定の規則はある：Non-Copy、書込みの拒否、明示の `@objref`、`@follow@uniq` の `SharedPathAccess_Kd`。暗黙の `objref` 適応（引数と `objref/Self` 受信者）は、obj では通るが rc/arc では `NoApplicableOverload_Kd` になる（§10.2 違反）。upcast は種別の一致を確かめていない | `Binding/Binding.ArgumentOperations.cs`、`Binding/Binding.ObjectViews.cs` |
| 目録 | ID 24–31（`makeRc` から `Weak` まで）は `SourceExpected: false` で、使用すると `UnsupportedBinding_Kd` になる | `Binding/KimiLibraryCatalog.cs` |
| 所有権 | `SupportsType` は obj だけを受け入れ、集成体の要素を再帰的に判定する | `Analysis/OwnershipAnalysis.Enums.cs` |
| 生成 | `IsOwner`（obj のみ）が 24 か所にある。ハンドルの配置は `objectHandle` 一つを共有している。工場は制御語に 0 を書く。原子命令はない | `ObjectTypes.cs`、`Emission/AggregateLayout.cs`、`Emission/LlvmModuleWriter.Objects.cs` |

Program 34 を `makeObj` に置き換えて試した結果：

- 直接の Field 読取り（`first.id`）は、obj でも `UnsupportedOwnership_Kd` になる。
- `first@follow@ref.id` に書き換えると、O0 で正しく実行できた。確かめた範囲は、Move、引数での消費、`@objref`、`@follow@ref`、借用を持つ payload、片付けである。

### 4.2. U3 で直す診断

1. `_ = h@objuniq`（`h: arc/T`）が `InvalidAssignment_Kd` になる。「rc/arc は排他オブジェクト借用を与えない」（§13.5.5.2、§13.5.7）と説明すべきである。
2. rc ハンドルで `uniq/Self` 受信者を呼ぶと `NoApplicableOverload_Kd` になる。理由として、共有限定を示すべきである。
3. obj に `clone` を適用すると `NoApplicableOverload_Kd` だけになる。「`clone` は rc/arc だけを複製する」という Advice を加える。

### 4.3. その他

- **P33 の範囲。** obj の Field 書込み（`o.n = 2`、`o@follow.n += 1`）は `UnsupportedOwnership_Kd` になる。Tuple 要素の obj に対する `t.0.get()` は、`ComparisonLoanConflict_Kd` も伴う（G59 と同型）。
- **記録のずれ。** `CoreCatalogTest` は 125 項目・117 検証済みを確かめているが、STATUS は 124・116 と書いている。

## 5. 文書の更新

| 文書 | 単位 |
| --- | --- |
| SPEC §3.2.2・§3.4.1・§13.5.8・§13.5.9・§22.1・§22.1.1・§22.5.4、IMPL §21.2.3.1・§21.2.3.3、LIBRARY.md、`draft/INTEGRATED.md` | U0 |
| `src/Kimi/Library/README.md`（数え方を IR で生成する理由） | U4 |
| CODEMAP の「Object views and runtime Type tests」行と enum 配置の行 | U1、U4、U8 |
| STATUS（Kimi ライブラリー、Exclusive objects、arc の検証範囲、Option の表現、Foreign imports） | 支援の境界が変わる U3–U8 |
| PLAN（rc/arc を交互に進めること、Toolchain T2 の縮小、G4）、PLAN_HISTORY、`tests/milestones/README.md` | 各セッションの終わり、U7、U8 |
