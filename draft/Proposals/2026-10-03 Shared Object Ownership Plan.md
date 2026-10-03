# 実装計画：共有オブジェクト所有権 `rc/T`・`arc/T`

日付：2026-10-03

状態：計画案。未着手。基準は `dev` の `92b6cb9b`。

本書は、マイルストーン P34（Program 34：rc/arc の生成、強参照の複製、ハンドルの Move、最後の手放し）を完成させる作業単位を定める。あわせて、P35（Weak）の前提になる nonnull Option 表現を入れ、P35 が同じ制御語の上に載るように境界を決める。仕様の変更は、U0 の四点だけである（本書 5.1）。

表記は次のとおり。

- SPEC の節は「§n」、IMPL の節は「IMPL §n」、本書の節は「本書 n」と書く。
- 実装の位置は、`src/Kimi/Compiler` からの相対パスで書く。

## 1. 決定事項（2026-10-03、ユーザー）

1. **順序。** マイルストーンは PLAN の順（P40 → P26 → …）に進める。rc/arc の単位は、同じセッションで P40・P26 の単位と交互に進める。別の worktree は使わない。
2. **宣言の形。** rc/arc の強参照を受け取る API と `Weak` は、`<s/T>` に `s is rc or arc` を付けて宣言する（本書 4.1）。
3. **Abort コード。** 数の上限を超えたら `KIMI_E_REF_COUNT` で Abort する（本書 4.5）。
4. **arc の検証。** P34 も P35 も、証明と生成 IR の検査で行う。モデル検査と実機の負荷テストは、x64 の多スレッドも ARM64 も行わない（本書 7.2）。
5. **nonnull Option 表現**（IMPL §21.1.5）。rc/arc の作業の中で、P35 より前に入れる（U8）。

## 2. 用語

| 語 | 意味 |
| --- | --- |
| 種別 | 所有ハンドルの Semantics。obj・rc・arc の三つ |
| 保持（retain） | 強参照の数を一つ増やす。`clone` が行う |
| 手放し（release） | 強参照の数を一つ減らす |
| 最後の手放し | 数を 1 から 0 にする手放し。その後で破棄と返却を行う（§16.3.3） |
| 破棄（destroy） | payload を `destroyValues` で破棄する |
| 返却（free） | オブジェクトの記憶域を `freeStorage` で返す |
| 片付け | スコープの終わりなどで、ハンドルの責任を果たすこと。obj では破棄と返却、rc/arc では手放し |

## 3. 目的と範囲

**目的。** PLAN §5 の完了条件を P34 について満たす。

1. Program 34 を変更なしで O0/O2 でビルド・実行でき、期待する出力になる（本書 6）。
2. `test-milestone34.ps1` が通る。
3. Session 検証が通る。
4. Program 34 が使う SPEC 各節に、肯定と否定の焦点テストがある。
5. 隣接する未対応の形は、誤ったコードではなく診断で止まる。

**含む。**

- 生成と複製
  - `Kimi.Intrinsics.makeRc`、`makeArc`
  - 強参照の `clone`（§13.5.8）
- ハンドルの扱い
  - Move、受け渡し、結果、代入による置換
  - 明示と暗黙の `objref` 適応（§10.2、§7.3）
  - `@follow@ref`
  - Field の読取り
- 手放しと最後の手放し（§16.3.3）
- arc の原子的な数え方（IMPL §21.2.3）
- 置き場所と変換
  - 集成体、コレクション、Option、Closure、総称インスタンス内のハンドル
  - 同じ種別の中での upcast と実行時 `is`
- nonnull Option 表現（U8）。P34 の完了条件ではない。Toolchain T2 の前半（`Option<ref/T>` の import 引数）はこの単位に移る。

**含まない。**

- Weak、`downgrade`、`upgrade`、循環構築（P35。本書 8 に接続点を書く）
- 静的記憶域（P36）、フロー精密化（P33）、Program 38
- 並行実行と、別スレッドへの公開（付録 D.2）
- obj ハンドルを通した Field 書込み（P33。rc/arc は共有アクセスしか持たないので、P34 には要らない）

## 4. 設計

### 4.1. 宣言の形

強参照の `S` を受け取るものはすべて、`<s/T>` に `s is rc or arc` を付ける。pair 引数は一つの完全な型を一つのスロットで束縛する（§8.1.1）ので、`clone<rc/Item>(h)` の明示も推論も、`<S>` と同じに働く。この形で推論が通り、obj が拒否されることは試行で確かめた。

```kimi
// Kimi.Intrinsics（P34）
public func makeRc<T>(value: T) -> rc/T
    T is ObjectPayload
public func makeArc<T>(value: T) -> arc/T
    T is ObjectPayload
public func clone<s/T>(value: ref/(s/T)) -> s/T
    s is rc or arc

// Kimi と Kimi.Intrinsics（P35）
public struct Weak<s/T>              // 中身と drop は本書 8
    s is rc or arc
public func clone<s/T>(value: ref/Weak<s/T>) -> Weak<s/T>
    s is rc or arc
public func downgrade<s/T>(value: ref/(s/T)) -> Weak<s/T>
    s is rc or arc
public func upgrade<s/T>(value: ref/Weak<s/T>) -> Option<s/T>
    s is rc or arc
```

- **二つの `clone` は、引数型で排他になる。** `ref/(s/T)` に `ref/Weak<rc/X>` を渡すと `s = owner` になり、要件が否定される。逆向きは単一化しない。
- **`makeRc`・`makeArc` の目録。** `makeObj` と同じく `Function: CompilerFunctionKind.MakeRc/MakeArc` を与え、`ValidMakeObj` と同じ形で検証する。
- **仕様の変更（U0）。** 受理される集合は変わらない。
  - §13.5.8 の「Eligibility is an intrinsic formation rule …」は、二つの文に分ける。
    - ObjectPayload は内在の要件で、利用者の Contract ではない。この内容は残す。
    - 既存の強参照に対する API は、`s is rc or arc` で適格性を表す。
  - §3.2.2 の総称の三通りの説明は、「`Weak<s/T>` は `s is rc or arc` を要する」の一文にし、三通りは注に残す。
  - §13.5.9、§22.1 の `Weak` 行、§22.1.1 の表、LIBRARY.md の署名をそろえる。

### 4.2. 種別と二つの性質

ハンドルの性質は、互いに独立した二つの軸で決まる。

| 種別 | payload の権限（§13.5.5.1） | 数える段（§16.3.3） | 制御語の初期値 |
| --- | --- | --- | --- |
| obj | 排他を持てる | なし | 0 |
| rc | 共有だけ | 非原子的 | 2（strong = 1） |
| arc | 共有だけ | 原子的 | 2（strong = 1） |

- **一つの問い合わせ。**
  - `ObjectTypes.IsOwner`（obj のみ、15 ファイル 24 か所）を、種別を返す一つの問い合わせ（Obj/Rc/Arc）に置き換える。
  - 各箇所は、必要な性質だけを導いて使う。
    - payload の権限：排他の借用、payload の置換、`@objuniq`
    - 数える段：片付け、`destroyValues`
  - 種別の分岐は網羅的に書き、既定の分岐を持たない。obj の扱いに暗黙に流れ込む経路をなくす。
- **種別は変換で保存する。**
  - 所有ハンドルの変換が変えるのは View Target だけで、種別は変えない（§13.5.7）。
  - 現在の `BindObjectUpcast`（`Binding/Binding.ObjectViews.cs`）は、両側が `IsOwner` であることしか確かめていない。種別の一致を加える。
  - これがないと、たとえば rc を obj として片付け、別の強参照が残っているのに返却してしまう。
- **配置の同一性も種別ごとに分ける。**
  - ハンドルのバイト配置は三種別で同じ（8 バイト）である。
  - しかし `AggregateLayout` の `objectHandle`（`Emission/AggregateLayout.cs`）は一つの配置を共有している。これを種別ごとに分け、集成体の破棄関数と生成キャッシュが種別を取り違えないようにする。
- **共通の後半。**
  - 破棄と返却は、一つの補助関数（例 `__kimi_object_finalize`）にまとめる。
  - obj の片付けはこれを直接呼ぶ。rc/arc は、数える段で 0 にしたときだけ呼ぶ。
- **共有の適応は種別によらない。**
  - payload の権限が「共有」で足りる操作は、三種別に同じ規則で許す。対象は、明示と暗黙の `objref` 適応、共有受信者、`@follow@ref`、Field の読取りである。
  - 現在の `AdaptObjectBorrow`（`Binding/Binding.ArgumentOperations.cs`）は、暗黙の適応を obj に限っている。これを、権限で判定するように改める。
- **種別を開く条件。**
  - 種別は、所有権解析の `SupportsType`（`Analysis/OwnershipAnalysis.Enums.cs`）で開く。開く前の種別は、使用位置で `UnsupportedOwnership_Kd` になる。
  - 開いた時点で、その種別について受理される操作と置き場所は、正しく動くか、診断で止まる。誤ったコードにはならない。
  - `SupportsType` は集成体の要素も再帰的に判定するので、開くと集成体の中のハンドルも受理される。
  - rc は U4、arc は U5 で開く。

### 4.3. 表現と工場

- ハンドルは、三種別ともヘッダーを指す 8 バイトのポインターである。
- ObjectDescriptor は、同じ Dynamic Type の三種別で共有する（IMPL §21.2.2）。
- 数え方を選ぶのは、記述子ではなくハンドルの静的な Semantics である（IMPL §21.2.3.1）。
- 工場の生成処理は三種別で共通とし、違いは制御語の初期値だけとする。初期値は、payload の初期化の後、公開前の通常の store で書く。

### 4.4. 数える手順

補助関数は、生成する IR として作る。Kimigayo のソースにしない理由は二つあり、`src/Kimi/Library/README.md` に記録する。

- 原子的な順序付けの操作は、付録 D.2 が保留している。
- 最後の手放しでは、記述子を通した間接呼出しが要る。

`MaxControl` は `MaxRefCount << 1`、つまり 2^64 − 2 とする。基本の遷移は種別によらず「減算を確定し、0 になったときだけ破棄と返却を行う」である（§13.5.8、IMPL §21.2.3.2）。

```text
rc の保持    c = load [h+8]
             c == MaxControl なら Abort(KIMI_E_REF_COUNT)
             store [h+8] = c + 2
rc の手放し  c = load [h+8] - 2
             store [h+8] = c
             c == 0 なら finalize

arc の保持   c = load monotonic [h+8]
             繰り返し: c == MaxControl なら Abort(KIMI_E_REF_COUNT)
                       (ok, c) = cmpxchg [h+8], c → c + 2   （成功・失敗とも monotonic）
                       ok なら終わる。失敗したら、返された c で繰り返す
arc の手放し c = load monotonic [h+8]
             繰り返し: (ok, c) = cmpxchg [h+8], c → c - 2   （成功 release、失敗 monotonic）
                       失敗したら、返された c で繰り返す
             成功した c が 2 なら、fence acquire の後で finalize
```

- **ヘッダーの制御語は CAS で更新する。** P35 で、制御語はサイドテーブルのポインターに変わりうる。そのため、`atomicrmw` で算術を直接当てることはしない（IMPL §21.2.3.2）。P35 での表現の判定は、本書 8 による。
- **展開と配置。** 速い経路の展開、補助関数への分け方、`cold` の付け方は、測って決める。測るのは、生成と破棄が中心の処理と、複製が中心の処理の両方で、`src/Benchmark` を使う。

### 4.5. 上限超過の Abort コード

- **仕様の欠落。** 増分が上限に達したら Abort する（§13.5.8、IMPL §21.2.3.1）が、§22.5.4 の目録にはそのコードがない。
- **変更。** `KIMI_E_REF_COUNT: Reference count limit exceeded` を、§22.5.4 と `Emission/WindowsLowering.Abort.cs` に加える。
- **報告位置。** 増分を行う明示の操作の呼出し式の始まりとする。P34 では `clone`、P35 では `downgrade`・`upgrade`・Weak の `clone` である。暗黙の増分はない。

### 4.6. 所有権の意味

- **権限。** rc/arc は Non-Copy の所有ハンドルである。payload への権限は、書込み可能な `var` の経路でも共有だけとする（§13.5.5.1、§12.4.4）。

- **結果の依存は、結果契約が決める（§15.6.4）。**
  - 結果の型が名指す Origin に対応する Loan だけが続く。結果に現れない、操作の間だけの借用は、呼出しで終わる。
  - `clone` の結果 `s/T` は、`ref` の Origin を名指さない。そのため、スロットの借用は呼出しで終わる。
  - これに対し、`func field(p: ref/Box) -> ref/i32 during p` の結果は、`p` の借用を保つ。
  - 位置（内側か外側か）ではなく、結果契約と、実際の Loan の由来で判定する。

  ```kimi
  let number: i32 = 5
  let a = Kimi.Intrinsics.makeRc(Counter.init(number@ref))
  let b = Kimi.Intrinsics.clone(a@ref) // a のスロットの共有借用は、この呼出しの間だけ
  consume(a@move)                      // consume(h: rc/Counter) が受け取る。b は a のスロットに依存しない
  require b.value == 5 else => $abort("lost") // b が依存するのは number だけ
  ```

  - 実装の `SoleResultInput`（§15.6.3 の降下）がこの規則に従うことを、U4 で確かめる。P35 の `downgrade`・`upgrade` も、同じ規則で §13.5.9 の表を満たす。

- **手放しは、保守的に破棄の観測とする。**
  - どの手放しも、最後の手放しになりうる。
  - そのため手放しは、obj の片付けと同じく、破棄時の寿命検査（`Analysis/OwnershipBody.Borrows.cs`）と破棄 effect（`Binding/Binding.EffectBounds.cs`）に含める。

### 4.7. ハンドルからの直接の Field 読取り

- **§3.4.1 の明確化（U0）。** §3.4.1 は、選択を続ける層として、安全な値参照と pair 層しか挙げていない。オブジェクトハンドルと object 借用では View Target のメンバーを選ぶことを、一文で明示する。payload の権限は、§13.5.5.1 の表による。
- **実装。** 既存のオブジェクト view の Field 経路を、所有ハンドルにも適用する。対象は `ReadBorrowedField` と、payload への +16 の加算（`Emission/BodyLowering.StructBorrows.cs`）である。
- **書込み。** 書込みは P33 に残す。rc/arc を通した書込みは、Binding がすでに拒否している。

## 5. 作業単位

各単位は、再現・実装・焦点テストを一組にする。Unit 検証の後にコミットし、push する。

### 5.1. U0 仕様（文書のみ）

- 正式仕様に入れる四点
  1. 宣言の形（本書 4.1）：§3.2.2、§13.5.8、§13.5.9、§22.1、§22.1.1、LIBRARY.md
  2. Abort コード（本書 4.5）：§22.5.4、IMPL §21.2.3.1 からの参照
  3. §3.4.1 の明確化（本書 4.7）
  4. 検証の手段と遷移の表（本書 7.2）：IMPL §21.2.3.3
- `draft/INTEGRATED.md` に、本書を「一部取り込み」として記録する。
- 確認：SPEC の例をすべてパースする。

### 5.2. U1 種別と性質の導入（挙動は変えない）

- 実装
  - 本書 4.2 の問い合わせと二つの性質を入れ、24 か所を置き換える。
  - upcast に、種別の一致の条件を加える。
  - `objectHandle` を種別ごとの配置に分ける。
  - obj の片付けを、共通の後半 `__kimi_object_finalize` を通す形にする。
  - `SupportsType` は、この時点では obj だけを開く。
- 確認
  - 既存の `ObjectRuntimeTest`、`PipelineObjectTest`、`RuntimeTypeTest`、`BorrowAcquisitionTest`、`CallReservationTest`
  - Object 系の native fixture
  - 診断スナップショットが変わらないこと

### 5.3. U2 直接の Field 読取り（obj）

- 本書 4.7 の実装を行う。
- 再現：本書 9.1 の試行（`first.id`、`handle.id`、`counter.value`）を、obj で通す。
- 確認
  - native fixture `ObjectRuntimeFieldRead*.ll`（O0/O2）
  - 書込みが診断で止まること

### 5.4. U3 宣言と Binding

- 実装
  - `src/Kimi/Library/Intrinsics.kimi` に、`makeRc`・`makeArc`・強参照の `clone` を宣言する。目録の Function 種別と検証も加える。
  - `KimiLibrary.IsCompleteOwnershipFamily` を、P34 の部分（ID 24–26）と全体に分ける。
  - 暗黙の `objref` 適応を、payload の権限で判定する（本書 4.2）。
  - 本書 9.2 の診断三件を、Diagnostic Development Workflow に従って直す。
  - `CoreCatalogTest` の数と STATUS の記述を合わせる（本書 9.3）。
- 確認：`SharedObjectBindingTest`（新設）
  - 推論と明示の型引数
  - obj、`objref`、rc/arc でない値と、ObjectPayload を外した payload の拒否
  - 暗黙の適応（引数と `objref/Self` 受信者）の肯定と否定
  - 同じ種別の upcast の肯定
  - 異なる種別の間の変換（六通り）と、借用から所有を得る変換の否定
  - 診断コーパスと CLI 出力
- 段階基準：Program 34 は Binding を通り、所有権で止まる。`stage-baselines.json` と README を更新する。

### 5.5. U4 rc を開く

所有権の規則は rc と arc で共通なので、ここで両方に書く。`SupportsType` で開くのは rc だけである。開く条件（本書 4.2）を、この単位で満たす。

- 所有権
  - Move、引数での消費、結果、代入による置換（古い値の手放し）
  - `objref` 適応、`@follow@ref`、Field の読取り
  - `clone` の取得と、結果の依存（本書 4.6）
  - 手放しを破棄の観測にすること
- 生成
  - 工場（制御語 2）、rc の保持と手放し、補助関数
  - `clone`、`objref`、`@follow@ref`、Field の読取り、upcast、`is`
  - 種別ごとの `destroyValues` と片付け：集成体、Array、Dictionary、Option（現行のタグ付き表現）、Closure の capture、総称インスタンス
  - `KIMI_E_REF_COUNT`
- 確認：`SharedObjectOwnershipTest`（新設）
  - 肯定
    - 複製の後に元のハンドルを Move・片付けしても、複製を使えること（Origin を持つ payload を含む）
    - 結果の依存が、結果契約に従うこと（本書 4.6）
  - 否定
    - payload の借用中の Move
    - `var` のハンドルで、`h@uniq` が生きている間の `clone(h@ref)`
    - スコープを越えて返す `rc/Counter{number}`
    - `@follow@uniq`、`@objuniq`、排他受信者
    - arc が、使用位置で `UnsupportedOwnership_Kd` になること
- 確認：`SharedObjectRuntimeTest`（新設）と native fixture `SharedRc*.ll`（O0/O2）
  - 破棄が一度だけで、順序が正しいこと。Move で数が変わらないこと
  - 引数での消費、代入による置換、借用を持つ payload
  - 各置き場所での片付け（`(rc/T, i32)`、struct Field、`Array<rc/T>`、Dictionary の値、`Option<rc/T>`、capture）
  - upcast と `is`
  - 上限超過
    - IR 検査で、上限分岐が `KIMI_E_REF_COUNT` へ向かうことを確かめる。
    - 生成した `.ll` に同じモジュールのテスト用 driver を追記した fixture で、Abort の出力と位置を確かめる。製品にテスト用の入口は作らない。
  - `NativeAllocationAudit`（`Purpose=Allocation`）
    - `makeRc` の確保が 1 回
    - `clone` と、最後でない手放しの確保・返却が 0 回
    - 最後の手放しの返却が 1 回
- 段階基準：Program 34 は arc の部分で止まる。

### 5.6. U5 arc を開く

- 開く条件（本書 4.2）を、arc について満たす。
- 生成：本書 4.4 の CAS と ordering。
- 確認
  - 生成 IR の検査テスト：本書 7.2 の P34 の行を、一行ずつ確かめる。
  - native fixture `SharedArc*.ll`：U4 と同じことを確かめる。
  - Program 34 全体が、O0/O2 で期待どおりに出力すること。

### 5.7. U6 残りの隣接する形

U4・U5 の時点で、次の形は、動くか診断で止まる（本書 4.2）。この単位では、診断で止まっているものを動かす。

- ハンドルスロットの `swap` と `replace`。全値更新の既存の制限（Owned の証明）による。
- 総称インスタンスの本体での工場の呼出し。
  - 現在は `Emission/ObjectGenerationPlan.cs` が拒否している。
  - P22 の単相化に従い、三種別まとめて、インスタンスごとに工場を計画する。
- 確認
  - 動かした形に、肯定の native テストを置く。
  - 残る形は、診断で止まることを確かめる（完了条件 5）。

### 5.8. U7 P34 の完成

- `test-milestone34.ps1` を加える。
- `tests/milestones/README.md` の行と、`stage-baselines.json` の P34 項目を更新する。
- STATUS、PLAN（G4 を P35 の Weak 側に縮める）、LIBRARY.md、CODEMAP を更新する。
- `src/Benchmark/SharedObjects.md` に測定を置く（本書 4.4）。
- 確認：`-Milestone 34` と Session 検証

### 5.9. U8 nonnull Option 表現（P35 の前提）

- 実装
  - `Option<R>` を一語で表す（IMPL §21.1.5）。R は `ref`・`uniq`・`obj`・`rc`・`arc`・`objref`・`objuniq` の七種である。
  - null を `None` とする。
  - 構築、Case の判定、配置、片付け、生成キャッシュをこの表現にする。
  - `None` は片付けの責任を持たない。
  - `Option<ref/T>`・`Option<uniq/T>` の import 引数を受け付ける。
- 確認
  - 記憶域：大きさ・整列・stride がすべて 8 であること。
  - タグを保つもの：`Option<Option<R>>` と `Option<(rc/T, i32)>` はタグ付きの enum のままであること。Tuple 自体は、通常の集成体である。
  - 関数の境界：記憶域の大きさだけでは呼出し ABI の正しさは分からない（IMPL §21.1.5）。次の経路で、`None` と `Some` を渡して確かめる（native fixture `NonnullOption*.ll`）。
    - 一般の関数の引数と結果
    - 総称関数を経由する場合
    - 集成体への格納
    - FFI（`ForeignBorrowImportTest`）
  - 七種それぞれの片付け：`Some` だけを片付けること（obj は破棄と返却、rc/arc は手放し）。

## 6. 順序と期待出力

```text
U0 ─ U1 ─┬─ U2 ─┐
         └─ U3 ─┴─ U4 ─ U5 ─ U6 ─ U7 ─ U8 ─ (P35)
```

- U2 と U3 は、互いに独立している。U4 は、Program 34 の Field 読取りに U2 を使う。
- U8 は P34 の完了条件ではないが、P35 より前に置く。
- P34 は、P33 の残り（フロー精密化、継承 Field の射影、Program 33）に依存しない。

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

## 7. 検証

### 7.1. 機能・割当て・診断

- **機能。** 焦点テスト、native fixture（O0/O2）、Program 34 のハーネスで確かめる。誤ったコードは、破棄の印字と、`NativeAllocationAudit` の確保・返却の数で検出する。
- **割当て。** 確保・返却の回数（U4）を、`Purpose=Allocation` の回帰として置く。
- **診断。** U3 と U4 の診断の追加・変更は、Diagnostic Development Workflow の五段を踏む。

### 7.2. arc の数え方

**手段（決定）。** 証明と生成 IR の検査で確かめる。両者は、IMPL §21.2.3.3 に置く一つの遷移の表を参照する（U0 で、既存の候補表をこの表に広げる）。同じ表を参照することで、証明とテストのずれを防ぐ。

**証明が各遷移で示す不変条件**（IMPL §21.2.3.2 による）

1. 数の管理先は、常に一つである（inline か表のどちらか）。
2. 破棄と返却は、数を 0 にした成功した更新の後でだけ行う。失敗した CAS からは行わない。
3. 返却の後は、ヘッダーを読まない。
4. 上限では、更新の前に Abort する。数は折り返さない。
5. （P35）移行は数を変えず、それより前の手放しの効果を引き継ぐ。

**遷移の表**

| 遷移 | 更新対象と成功条件 | ordering と同期 | 段階 |
| --- | --- | --- | --- |
| 生成 | 工場がヘッダーと payload を書き、ハンドルを返す | 公開前の通常の store。呼出し元への返却は、プログラム順で足りる | P34 |
| 保持 | 制御語。上限未満なら +2 | CAS：成功・失敗とも monotonic。順序は運ばない | P34 |
| 手放し | 制御語。−2。0 にした成功だけが最後の手放し | CAS：成功 release・失敗 monotonic。最後なら破棄の前に fence acquire | P34 |
| 破棄 → 返却 | finalize の中 | プログラム順 | P34 |
| 移行 | 制御語を、表のポインター｜1 へ。CAS の成功だけが数の権限を表へ移す | 成功 acq_rel。失敗で表を見つけたら、表を読む前に acquire | P35 |
| `upgrade` | 表の strong。0 でなく上限未満なら +1 | CAS：成功 acquire・失敗 monotonic。`table.object` は成功の後でだけ読む | P35 |
| 循環構築の公開 | 表の strong を 0 から 1 へ（構築権限を持つ工場だけ） | release | P35 |
| 表内の手放し | 表の strong か weak。−1。旧値 1 の手放しだけが最後。strong の最後：破棄 → 返却 → weak guard の手放し。weak の最後：表の返却 | `atomicrmw sub release`。旧値が 1 なら fence acquire | P35 |

- **IR の検査で確かめること。**
  - 表の ordering
  - 更新対象と成功の分岐：失敗した CAS から finalize へ届かないこと
  - 返却の後にヘッダーを読まないこと
  - ヘッダーに `atomicrmw` がないこと
- **別スレッドへの公開は、本書の対象外である。** 通常の関数の return は、別スレッドとの同期を作らない。別スレッドへ公開する機構は付録 D.2 で定まっておらず、その機構が同期を与える責任を持つ。

**モデル検査と実機テストを行わない理由。**

- x64 は TSO で、`lock cmpxchg` は完全な障壁として働く。そのため ordering の誤りは負荷テストに現れにくく、検出は保証されない。
- 誤りの主な源は、O2 の LLVM が `monotonic` の原子操作をまたいで通常の読み書きを移すことである。これは IR の検査で捉えられる。もう一つの源である弱いメモリーの CPU は、プロファイルの外である。
- Kimigayo には、まだソースのスレッドがない（付録 D.2）。

STATUS には、弱いメモリーについて確かめた範囲は「証明と IR の検査」であると記録する。

## 8. P35（Weak）への接続

- **宣言。** 本書 4.1 の形にする。Weak の `clone` には新しい安定 ID を加え、強参照の `clone`（ID 26）は保つ。
- **Weak の内部。** 次のどちらにするかは、P35 の着手時に決める。
  - コンパイラー管理
  - 非公開の `Storage` 原始操作の上に Kimigayo ソースで書いた struct（ライブラリー方針に沿う）
- **サイドテーブル。**
  - `ensureSideTable`、`tryRetainStrong`、`retainWeak`/`releaseWeak` を加える。
  - `downgrade` を受け入れるのと同じコミットで、表現の判定と weak guard を入れる。
  - 表現の判定は、制御語を更新するすべての経路で、算術の前と CAS が失敗した後に行う。偶数は inline の数、奇数は表である。
  - 表内の数は整数のままなので、減算には `atomicrmw sub` を使える（本書 7.2）。
- **`upgrade` の結果。** `Option<S>` は、U8 の nonnull 表現の上に載る。
- **循環構築。**
  - 単相化により、`F` はインスタンスごとに具体的な Closure になる。そのため、所有受信者による Consuming 呼出しを直接生成できる見込みである。P26 の一般的な `Callable<owner>` witness がなくても進められるはずなので、着手時に試行で確かめる。
  - `T is Owned` と、Building 状態（strong 0、weak 2）を扱う。
- **検証。** 本書 7.2 の P35 の行を、遷移ごとに証明し、IR の検査に加える。
- **Program 38。** P33、P36、P24 も要るので、P35 では Weak の段階まで進めるだけである。

## 9. 調査の記録（2026-10-03、CLI での試行を含む）

### 9.1. 段階ごとの状態

| 段階 | 状態 | 根拠 |
| --- | --- | --- |
| 型の形成 | `rc/T`・`arc/T` は形成され、受理される | `Binding/Binding.Types.cs` |
| Binding | 共有限定の規則はある：Non-Copy、書込みの拒否、明示の `@objref`、`@follow@uniq` の `SharedPathAccess_Kd`。暗黙の `objref` 適応（引数と `objref/Self` 受信者）は、obj では通るが rc/arc では `NoApplicableOverload_Kd` になる（§10.2 違反）。upcast は、種別の一致を確かめていない | `Binding/Binding.ArgumentOperations.cs`、`Binding/Binding.ObjectViews.cs` |
| 目録 | ID 24–31（`makeRc` から `Weak` まで）が `SourceExpected: false`。使用すると `UnsupportedBinding_Kd` になる | `Binding/KimiLibraryCatalog.cs`、`Binding/Binding.Diagnostics.cs` |
| 所有権 | `SupportsType` は obj しか受け付けず、集成体の要素を再帰的に判定する。rc/arc の引数で `UnsupportedOwnership_Kd` になる | `Analysis/OwnershipAnalysis.Enums.cs` |
| 生成 | `ObjectTypes.IsOwner`（obj のみ）が 24 か所にある。所有ハンドルの配置は `objectHandle` 一つを共有している。工場は制御語に 0 を書く。原子命令は一つもない | `ObjectTypes.cs`、`Emission/AggregateLayout.cs`、`Emission/LlvmModuleWriter.Objects.cs` |
| 試験 | `test-milestone34.ps1` はない。P34 は、Binding の `Kimi.Intrinsics.makeRc` で止まる | `tests/milestones/stage-baselines.json` |

Program 34 を `makeObj` に置き換えて試した結果は次のとおり。

- **直接の Field 読取りは、obj でも未対応。** `first.id` は `UnsupportedOwnership_Kd` になる。
- **それ以外は obj で動く。** `first@follow@ref.id` に書き換えれば、O0 で正しく実行できた。確かめた範囲は、ハンドルの Move、引数での消費、`@objref`、`@follow@ref`、借用を持つ payload、片付けである。

### 9.2. U3 で直す診断

1. `_ = h@objuniq`（`h: arc/T`）が、`InvalidAssignment_Kd` になる。「rc/arc は排他オブジェクト借用を与えない」（§13.5.5.2、§13.5.7）と説明すべきである。
2. rc ハンドルで `uniq/Self` 受信者を呼ぶと、`NoApplicableOverload_Kd` の候補不一致になる。理由として、共有限定を示すべきである。
3. pair 形の `clone` を obj に適用すると、`NoApplicableOverload_Kd` だけになる。「`clone` は rc/arc だけを複製し、obj は複製できない」という Advice を加える。

### 9.3. その他

- **P33 の範囲。** obj の Field 書込み（`o.n = 2`、`o@follow.n += 1`）は `UnsupportedOwnership_Kd` になる。Tuple 要素の obj に対する `t.0.get()` は `UnsupportedOwnership_Kd` になり、同時に `ComparisonLoanConflict_Kd` も出る（G59 と同型の連鎖）。
- **記録のずれ。** `CoreCatalogTest` は 125 項目・117 検証済みを確かめているが、STATUS は 124・116 と書いている。

## 10. 文書の更新

| 文書 | 単位 |
| --- | --- |
| SPEC §3.2.2・§3.4.1・§13.5.8・§13.5.9・§22.1・§22.1.1・§22.5.4、IMPL §21.2.3.1・§21.2.3.3、`draft/INTEGRATED.md` | U0 |
| LIBRARY.md | U0、U3 |
| `src/Kimi/Library/README.md`（数え方を IR で生成する理由） | U4 |
| CODEMAP の「Object views and runtime Type tests」行（種別の問い合わせ、補助関数）と、enum 配置の行（nonnull Option） | U1、U4、U8 |
| STATUS（Kimi ライブラリー、Exclusive objects、arc の検証範囲、Foreign imports の `Option<ref/T>`） | 支援の境界が変わる U3–U8 |
| PLAN（rc/arc を交互に進めること、Toolchain T2 の縮小、G4）、PLAN_HISTORY、`tests/milestones/README.md` | 各セッションの終わり、U7、U8 |
