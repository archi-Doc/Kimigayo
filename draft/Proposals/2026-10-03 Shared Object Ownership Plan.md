# 実装計画：共有オブジェクト所有権 `rc/T`・`arc/T`

日付：2026-10-03

状態：計画案。未着手。基準は `dev` の `92b6cb9b`。PLAN の順序では P34 は P33 の後にある。Milestone Program を実装対象にするにはユーザーの明示の指示が要る（PLAN §1）ので、着手時期は本書 8 の判断による。

本書は、マイルストーン P34（Program 34：rc/arc の生成、強参照の複製、ハンドルの Move、最後の解放）を完成させる作業単位を定める。あわせて、P35（Weak）が同じ制御語の上に載るように境界を決める。仕様の要求は SPEC と IMPL がすでに定めている。本書で仕様に手を入れるのは、本書 3 の D1 と D5 の二点だけである。SPEC の節は「§n」、IMPL の節は「IMPL §n」、本書の節は「本書 n」と書く。

## 1. 目的と範囲

**目的。** PLAN §5 の完了条件を P34 について満たす。

1. Program 34 を変更なしで O0/O2 でビルド・実行でき、期待する出力になる。
2. `test-milestone34.ps1` が通る。
3. Session 検証が通る。
4. Program 34 が使う SPEC 各節について、肯定と否定の焦点テストがある。
5. 隣接する未対応の形は、誤ったコードではなく診断で止まる。

**含む。**

- `Kimi.Intrinsics.makeRc`、`makeArc` と強参照の `clone`（§13.5.8）
- ハンドルの Move・受け渡し・結果・代入による置換
- `@objref` と `@follow@ref`、ハンドルを通した Field 読取りと共有受信者の呼出し
- 強参照の解放と、最後の解放による破棄・解放（§16.3.3）
- `arc` の原子的な数え方（IMPL §21.2.3）
- 集成体・コレクション・Closure・総称インスタンス内の rc/arc ハンドル
- rc/arc からの upcast と実行時 `is`（本書 4 の U7）

**含まない。**

- Weak、`downgrade`、`upgrade`、循環構築（P35。本書 7 に接続点だけを書く）
- 静的記憶域（P36）、フロー精密化（P33）、Program 38
- 並行実行（付録 D.2）
- nonnull Option 表現（IMPL §21.1.5、Toolchain T2）
- obj ハンドルを通した Field 書込み（本書 2.3。rc/arc は共有アクセスしか持たないので P34 には要らない）

## 2. 現状（2026-10-03 の調査と CLI 試行）

### 2.1. 段階ごとの状態

| 段階 | 状態 | 根拠 |
| --- | --- | --- |
| 型の形成 | `rc/T`・`arc/T` は形成され受理される。ObjectPayload と Object Target の判定は obj と共通 | `Binding.Types.cs:557-576`、`Binding.ConstraintValidation.cs:397-493` |
| Binding の共有限定規則 | 既にある：Non-Copy、共有層の権限、rc/arc を通した書込みの拒否、`@objref` の受理、`@follow@uniq` の `SharedPathAccess_Kd`、`h@rc` の `BareOwningShorthand_Kd` | `Binding.Capabilities.cs:139,526`、`Binding.ArgumentOperations.cs:197-275,936`、`Binding.Expressions.cs:1087`、`Binding.Payloads.cs:24`、`Binding.Conversions.cs:594` |
| 目録 | `makeRc`=24、`makeArc`=25、`clone`=26、`downgrade`=27、`upgrade`=28、`makeRcCyclic`=29、`makeArcCyclic`=30、`Weak`=31 が `SourceExpected: false`。使用すると `MissingFailure` を経て `UnsupportedBinding_Kd` になる | `KimiLibraryCatalog.cs:55-63`、`Binding.Diagnostics.cs:737-753`、`KimiLibrary.IsCompleteOwnershipFamily` |
| 所有権解析 | `SupportsType` が obj しか受け付けない（`ObjectTypes.IsOwner`）。rc/arc の引数を持つ関数は、引数の位置で `UnsupportedOwnership_Kd` になる（試行で確認） | `OwnershipAnalysis.Enums.cs:94,108` |
| 生成 | `ObjectTypes.IsOwner`（obj のみ）が 15 ファイル 24 か所で生成経路を絞っている。ハンドルの配置は obj と共通の 8 バイト `objectHandle`。工場関数は制御語に 0 を書く。実行時補助は `__kimi_drop_object` などで、`LlvmModuleWriter.Objects.cs` が文字列として生成する。`atomicrmw`・`cmpxchg`・`fence` はコードのどこにもない | `ObjectTypes.cs`、`AggregateLayout.cs:121-130`、`ObjectGenerationPlan.cs`、`LlvmModuleWriter.Objects.cs:128` |
| 試験 | obj の試験は `ObjectRuntimeTest`、`PipelineObjectTest`、`RuntimeTypeTest`、`ObjectPayloadTest`、`CoreCatalogTest` などにある。`test-milestone33/34/35/38.ps1` はない。`stage-baselines.json` は P34 を Binding の `Kimi.Intrinsics.makeRc` で止まるとしている | `tests/milestones/stage-baselines.json` |

### 2.2. obj で同じ形を試した結果

Program 34 の `makeRc`・`makeArc` を `makeObj` に置き換え、`clone` を除いて試した（スクラッチで実施。リポジトリは変更していない）。

- `first.id`、`handle.id`、`counter.value` のように**所有ハンドルから直接 Field を読む形は、obj でも `UnsupportedOwnership_Kd`** になる。これは rc 固有ではなく、オブジェクト全般の欠落である。
- これらを `first@follow@ref.id` に書き換えると、O0 でビルド・実行でき、出力も正しい。対象は、ハンドルの Move、所有引数での消費、`@objref`、`@follow@ref`、借用を持つ payload（`Counter {source}`）、スコープ終了時の破棄である。したがって P34 で新たに要るのは、次の二つが中心になる。
  - 直接の Field 読取り
  - rc/arc 固有の数え方と解放

### 2.3. 試行で見つかった付随事項

1. **診断の質。** 次の三つは Diagnostic Development Workflow の対象である。
   - `_ = h@objuniq`（`h: arc/T`）が `InvalidAssignment_Kd`（「代入を許さない」）になる。§13.5.5.2 と §13.5.7 の「rc/arc は排他オブジェクト借用を与えない」が説明されていない。
   - rc ハンドルで `uniq/Self` 受信者のメソッドを呼ぶと、`NoApplicableOverload_Kd` の候補不一致になり、共有限定が理由として出ない。
   - pair 形の `clone` を obj に適用すると、`NoApplicableOverload_Kd` だけになる。「`clone` は rc/arc だけを複製し、obj は複製できない」という Advice がない。
2. **obj の隣接する欠落（P33 の範囲）。**
   - `o.n = 2` と `o@follow.n += 1` が `UnsupportedOwnership_Kd` になる。
   - Tuple 要素の obj に対する `t.0.get()` が `UnsupportedOwnership_Kd` になり、同時に `ComparisonLoanConflict_Kd` も出る。G59 と同型の連鎖である。
3. **記録のずれ。** `CoreCatalogTest` は 125 項目・117 検証済みを確かめているが、STATUS は「124 項目、116 検証済み」と書いている。U3 で数が変わるときに直す。

## 3. 設計判断

### D1. 宣言の形

- `makeRc`・`makeArc` は、`makeObj` と同じく `src/Kimi/Library/Intrinsics.kimi` に `T is ObjectPayload` 付きで宣言する。目録には `Function: CompilerFunctionKind.MakeRc/MakeArc` を与え、`ValidMakeObj` と同じ形で検証する。
- 強参照の `clone` は次のように宣言する。

  ```kimi
  public func clone<s/T>(value: ref/(s/T)) -> s/T
      s is rc or arc
  ```

  pair 引数は一つの完全な型を一つのスロットで束縛する（§8.1.1）。そのため `clone<rc/Item>(h)` も推論も、`clone<S>` と同じに働く。適格性（§13.5.8「valid complete rc/arc handle Type」、§3.2.2 の総称の場合）は、Semantics 要件 `s is rc or arc` としてそのまま書ける。試行では、`ref/(rc/T)` からの推論と `<arc/T>` の明示は受理され、obj は拒否された。
- **仕様の変更。** §13.5.8 と §22.1.1 の表、および LIBRARY.md の署名を pair 形に揃える。「Eligibility is an intrinsic formation rule」は、この Semantics 要件に置き換える。受理される集合は変わらない。根拠は原則 1・2 で、隠れた規則を、宣言と既存の要件判定・診断経路で読める形にする。
- **退けた案。** コンパイラーが特別に適格性を判定する案は退ける。宣言に現れない規則になり、診断経路も別に作ることになる。

### D2. 表現

- ハンドルは、obj と同じくヘッダーを指す 8 バイトのポインターとする。
- ObjectDescriptor は、同じ Dynamic Type の obj・rc・arc で共有する（IMPL §21.2.2）。
- 数え方を選ぶのは、記述子ではなくハンドルの静的な Semantics である（IMPL §21.2.3.1）。
- 工場関数は (payload, mode) ごとに作る。payload を初期化した後、公開前の通常の store で制御語に 2（strong = 1）を書く。obj は従来どおり 0 である。

### D3. obj と rc/arc の区別

`ObjectTypes.IsOwner` を二つに分ける。

- `IsOwningHandle`：obj・rc・arc。Move、Non-Copy、`@objref`、`@follow@ref`、Field の読取り、upcast、`is` に使う。
- `IsExclusiveOwner`：obj のみ。`@objuniq`、payload の排他アクセス・置換、obj の破棄に使う。

24 か所すべてをどちらかに分類する。ハンドルの破棄は、`AggregateLayout` のハンドル種別で次のように振り分ける。

| 種別 | 破棄の補助関数 |
| --- | --- |
| obj | `__kimi_drop_object` |
| rc | `__kimi_rc_release` |
| arc | `__kimi_arc_release` |

**最大の危険。** 振り分けなしに判定だけを広げると、共有オブジェクトを `__kimi_drop_object` が即座に解放し、誤ったコードになる。そのため U1 では判定の分割と振り分けを先に済ませ、rc/arc の受理（U4）より前に置く。

### D4. 数え方の手順

補助関数は生成する IR とする。Kimigayo のソースにしない理由は二つある。

- 原子的な順序付け操作は付録 D.2 が保留している。
- 解放の後半では、記述子を通した `destroyValues` と `freeStorage` の間接呼出しが要る。

この理由は `src/Kimi/Library/README.md` の方針に記録する。

**rc（非原子的）**

- retain：制御語を読み、最大値（`MaxRefCount << 1`）なら Abort する。そうでなければ 2 を加えて書く。
- release：2 を引いて書き、0 になったら後半を実行する。

**arc（線形化可能。IMPL §21.2.3.3 の候補順序に従う）**

- retain：制御語全体の CAS ループ。成功も失敗も `monotonic`。
- release：CAS ループ。成功は `release`、失敗は `monotonic`。0 に達したら `fence acquire` の後で後半を実行する。
- `atomicrmw sub` は使わない。将来、サイドテーブルのポインターを古い数で上書きしないためである（IMPL §21.2.3.2）。

**共通の後半**

`__kimi_drop_object` と同じく、`destroyValues` の後に `freeStorage` を呼ぶ。解放した後はヘッダーを読み直さない。

**奇数の制御語（サイドテーブル）**

P34 には表を作る経路がない。奇数の分岐は、P35 で `downgrade` を受理するのと同じコミットで加える。受理を先にすると、分岐のない解放が誤ったコードになるからである。

**性能**

- retain は呼出し位置にインライン展開する。
- release は減算をインラインで行い、最後の解放だけを cold な呼出しにする。
- O0/O2 で `src/Benchmark` に測定を置き、obj の Move・破棄と比べる。

### D5. 上限超過の Abort コード

**仕様の欠落。** 増分が最大値に達すると Abort する（§13.5.8、IMPL §21.2.3.1）。しかし §22.5.4 の目録にはそのコードがない。

**変更。** `KIMI_E_REF_COUNT: Reference count limit exceeded` を §22.5.4 と `WindowsLowering.Abort.cs` に加える。報告する位置は、失敗した操作（P34 では `clone`、P35 では `downgrade`・`upgrade`・Weak の `clone`）の呼出し式の始まりとする。

### D6. 所有権の意味

- **権限。** rc/arc は Non-Copy の所有ハンドルである。payload への権限は、`var` の書込み可能な経路でも共有だけとする（§13.5.5.1 の表、§12.4.4）。
- **`clone(h@ref)`。** 呼出しの間だけハンドルのスロットを共有借用する。結果の型 `s/T` は借用の外側の Origin を含まない。そのため §15.6.4 の通常の伝播では、結果に残る依存は `T` の Origin と、それに対応する実際の Loan だけになる。§13.5.8 の「入力スロットに持続する Loan を残さない」は、この伝播で満たされる見込みである。
  - **危険。** 実装の `SoleResultInput`（§15.6.3 の降下）は、結果の Origin を名指す入力が一つだけのとき、結果をその入力から降下させる。`T` が Origin を持つとき（`rc/Counter{number}`）に、この降下がハンドルスロットへの Loan を残すと、正しいプログラムを拒否することになる。
  - **確かめ方。** U4 で、元のハンドルを Move・破棄した後にも複製が使えることを確かめる。P35 の `downgrade` と `upgrade` も、同じ条件を満たさなければならない（§13.5.9 の表）。
- **解放と破棄。** どの強参照の解放も最後になりうる。そのため、ハンドルの破棄は保守的に payload の破棄の観測として扱う。obj と同じく破棄時の寿命検査（`OwnershipBody.Borrows.cs:783`）と破棄 effect（`Binding.EffectBounds.cs:1490`）に含める。

### D7. 所有ハンドルからの直接の Field 読取り

- 既存のオブジェクト view の Field 経路を、所有ハンドル（obj・rc・arc）にも適用する。対象は `ReadBorrowedField` と、payload の +16 である（`BodyLowering.StructBorrows.cs:335`）。
- 選択の扱いは、型ではなく既存の Binding（`Binding.Calls.cs:276-283`）が決めている。
- 書込みは、obj の排他経路の問題として P33 に残す。rc/arc を通した書込みは、Binding が既に拒否している。

## 4. 作業単位

各単位は、再現・実装・焦点テストを一組にする。Unit 検証の後にコミットし、push する（AGENTS.md）。

| 単位 | 内容 | 主なテストと検証 |
| --- | --- | --- |
| **U0 仕様** | D1（`clone` の pair 形、§13.5.8・§22.1.1・LIBRARY.md）と D5（§22.5.4 の Abort コード、IMPL §21.2.3.1 の参照）を正式仕様に入れる。`draft/INTEGRATED.md` に本書を「一部取り込み」として記録する | 文書のみ。SPEC 例のパース確認 |
| **U1 区別の導入（挙動不変）** | `IsOwningHandle`/`IsExclusiveOwner` を導入し、24 か所を分類する。`AggregateLayout` にハンドル種別を加え、破棄の振り分けを用意する。この時点では rc/arc は `SupportsType` で引き続き拒否する | 既存の `ObjectRuntimeTest`、`PipelineObjectTest`、`RuntimeTypeTest`、`BorrowAcquisitionTest`、`CallReservationTest`、Object 系の native fixture。診断スナップショットは不変 |
| **U2 直接の Field 読取り** | 所有ハンドル（まず obj）からの Field 読取りと、共有受信者の呼出しを、所有権解析と生成で扱う（D7） | 本書 2.2 の再現（`first.id`、`handle.id`、`counter.value`）を obj で通す。書込みが診断で止まることも確かめる。native fixture は `ObjectRuntimeFieldRead*`。P33 にも効く |
| **U3 宣言と Binding** | `Intrinsics.kimi` に `makeRc`・`makeArc`・強参照の `clone` を宣言する。目録の Function 種別と検証を加える。`IsCompleteOwnershipFamily` を P34 の部分（24–26）と全体に分ける。`CoreCatalogTest` の数と STATUS を直す。本書 2.3 の診断三件を、Diagnostic Development Workflow に従って直す | `SharedObjectBindingTest`（新設）：推論、明示の型引数、obj・objref・Weak でない値の拒否、ObjectPayload を外した payload の拒否。診断コーパスと CLI 出力の確認。Program 34 の段階基準は、Binding 通過・所有権で停止に移る |
| **U4 所有権解析** | `SupportsType` で rc/arc を受理する。Move、引数での消費、結果、代入による置換（古い値の解放）、`@objref`、`@follow@ref`、`clone` の取得（呼出しの間だけの共有借用）、破棄の観測（D6）を扱う | `SharedObjectOwnershipTest`（新設）。肯定ケース：複製した後に元のハンドルを Move・破棄しても複製を使える（Origin を持つ payload を含む。D6）。否定ケース：payload の借用中の Move、`h@uniq` が生きている間の `clone(h@ref)`、スコープを越えて返す `rc/Counter{number}`、`@follow@uniq`、`@objuniq`、排他受信者。段階基準は生成に移る |
| **U5 rc の実行時と生成** | rc の工場関数（制御語 2）、`__kimi_rc_retain`/`__kimi_rc_release`、破棄の振り分け、`clone` の生成、`@objref`・`@follow@ref`・Field 読取りの生成、上限超過の Abort（D4、D5） | native fixture `SharedRc*.ll`（O0/O2）：破棄がちょうど一回で順序が正しいこと、Move で数が変わらないこと、所有引数の消費での解放、代入置換、ハンドルスロットの `swap`、借用を持つ payload。上限超過は、テスト側の driver が制御語を最大値近くに偽造して retain を呼び、`KIMI_E_REF_COUNT` を確かめる。`NativeAllocationAudit` で、`makeRc` が 1 回確保、`clone` が 0 回、最後の解放が 1 回解放することを確かめる（`Purpose=Allocation`） |
| **U6 arc の原子的な数え方** | `__kimi_arc_retain`/`__kimi_arc_release` の CAS ループと順序（D4） | 生成 IR の検査テスト：各操作の ordering と、`atomicrmw sub` がないこと。`SharedArc*.ll` は U5 と同じ観測をする。並行性の検証は本書 6 |
| **U7 隣接する形** | ハンドルの置き場所として、Tuple、struct Field、固定長配列、`Array<rc/T>`、Dictionary の値、`Option<rc/T>`（現行のタグ付き表現）、Closure の Move・借用 capture、総称インスタンスを扱う。型の `destroyValues` は mode ごとに作る。`@rc/V`・`@arc/V`・`@objref/V` の upcast と実行時 `is` を扱う。総称本体での `makeRc` は `makeObj` と同じ制限にし、未対応なら診断で止める | 対応する形ごとに肯定の native テストを置く。未対応の形は診断で止まることを確かめる（完了条件 4） |
| **U8 P34 の完成** | `test-milestone34.ps1` を加える。README の行と `stage-baselines.json` の P34 項目を更新・削除する。STATUS、PLAN（G4 を P35 の Weak 側に縮める）、LIBRARY.md、CODEMAP の「Object views」行を更新する。`src/Benchmark/SharedObjects.md` に測定を置く | `-Milestone 34` と Session 検証 |

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
pwsh -Command "./scripts/verify.ps1 -Class SharedObjectRuntimeTest,ObjectRuntimeTest,PipelineObjectTest,CoreCatalogTest -Fixtures 'SharedRc*.ll' -Milestone 34"
```

## 5. 順序と依存

```text
U0 ─ U1 ─┬─ U2 ─┐
         └─ U3 ─┴─ U4 ─ U5 ─ U6 ─ U7 ─ U8
```

- U2 は U3 と独立している。obj だけでも価値がある。
- U5 までで rc の Program 34 前半が動く。U6 で後半の arc が動く。
- P34 は、P33 の残り（フロー精密化、継承 Field の射影、Program 33）に依存しない。PLAN の順序（P40 → P26 → P24 → P25 → P33 → P34）を入れ替えるか並行させるかは、本書 8 の判断による。

## 6. 検証方針

- **機能。** 単位ごとの焦点テスト、native fixture（O0/O2）、Program 34 のハーネスで確かめる。誤ったコードの検出には、破棄の印字と `NativeAllocationAudit` の確保・解放の数を使う。
- **割当て。** 次を `Purpose=Allocation` の回帰として置く。
  - `clone` と非最後の解放が 0 回であること。
  - `makeRc`/`makeArc` が 1 回であること。
  - 最後の解放が 1 回の `Free` であること。
- **arc の並行性。** 次の四段で行う。
  1. 生成 IR の ordering を検査テストで固定する。
  2. IMPL §21.2.3.3 の各矢印を、どの release/acquire の対が満たすかの対応表を `artifacts/verify/` に記録する。
  3. 任意：生成した補助関数を複数スレッドから呼ぶ native の負荷テストを置く。x64 は TSO なので、これで分かるのは原子性と交錯であり、弱いメモリーモデルではない。
  4. 弱いメモリーモデルの検証（GenMC や herd などのモデル検査）は、道具の選択が要る。選ぶまでは STATUS に未検証として残す（本書 8）。
- **診断。** U3 と U4 の診断の追加・変更は、Diagnostic Development Workflow の五段を踏む。

## 7. P35（Weak）への接続

- **Weak の宣言。** `struct Weak<s/T>` に `s is rc or arc` を付ける（D1 と同じ形で、§3.2.2 の総称の場合と一致する）。中身と `drop` は次のどちらかにする。
  - コンパイラー管理
  - 非公開の `Storage` 原始操作の上に Kimigayo ソースで書いた struct（ライブラリー方針に沿う）

  どちらにするかは P35 の着手時に決める。
- **Weak の `clone`。** 強参照の `clone` との多重定義になる。目録には新しい安定 ID を加え、強参照の `clone`=26 は保つ。二つの候補は引数型で排他になる（`ref/(s/T)` に `Weak<…>` を渡すと `s = owner` になり、要件が否定される）。
- **サイドテーブル。** `ensureSideTable`、`tryRetainStrong`、`retainWeak`/`releaseWeak` を加える。D4 の奇数分岐と weak guard は、`downgrade` の受理と同じコミットで入れる。
- **`upgrade` の結果。** `upgrade` は `Option<S>` を返す。U7 の `Option<rc/T>` の上に載る。nonnull 表現（T2）は別の単位である。
- **循環構築。** 単相化により `F` はインスタンスごとに具体的な Closure になる。そのため、所有受信者による Consuming 呼出しを直接生成でき、P26 の一般的な `Callable<owner>` witness がなくても進められる見込みである。着手時に試行で確かめる。`T is Owned` と Building 状態（strong 0、weak 2）も扱う。
- **Program 38。** P33、P36、P24 も要るので、P35 では Weak の段階まで進めるだけである。

## 8. 未決事項（ユーザーの判断が要るもの）

1. **P34 の着手時期と PLAN の順序。** P40・P26 の後に置くか、先にするか並行させるか。
2. **D1。** `clone`（および P35 の `Weak`）を pair 形で宣言し、SPEC の表現をそれに揃えるか。
3. **D5。** Abort コードの名前と文言。案は `KIMI_E_REF_COUNT: Reference count limit exceeded`。
4. **arc の弱いメモリー検証の手段。** モデル検査の道具を導入するか。導入しない場合は、本書 6 の 1–3 までを完了条件とし、残りを STATUS の制限として記録してよいか。

## 9. 文書の更新

| 文書 | 更新する単位 |
| --- | --- |
| SPEC §13.5.8・§22.1.1・§22.5.4、IMPL §21.2.3.1、`draft/INTEGRATED.md` | U0 |
| LIBRARY.md | U0、U3 |
| `src/Kimi/Library/README.md`（数え方を IR で生成する理由） | U5 |
| CODEMAP の「Object views and runtime Type tests」行（共有ハンドル、補助関数の位置） | U1、U5 |
| STATUS（Kimi ライブラリー、Exclusive objects の行、arc の検証範囲） | 支援の境界が変わる U3–U8 |
| PLAN、PLAN_HISTORY、`tests/milestones/README.md` | 各セッションの終わりと U8 |
