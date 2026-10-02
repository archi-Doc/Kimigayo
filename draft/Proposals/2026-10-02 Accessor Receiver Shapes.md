# 仕様変更案：accessor の受け手形状の固定

日付：2026-10-02

状態：提案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書で変更する事項は SPEC より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例は独立した断片であり、「エラー」と記した行は意図した拒否例である。

## 1. 問題と方針

### 1.1. 現仕様の問題

SPEC §11.2.2 は computed getter の受け手に、`ref/Self` のほか `uniq/Self`、所有 `Self`、object 形式（`obj`/`rc`/`arc`/`objref`/`objuniq`）を認め、`x.p` がその受け手を SPEC §7.3 の受信者式として暗黙に取得する。setter も同様である。これには次の問題がある。

**(a) 読み取りに見える式が排他借用・更新・消費を起こす。** `x.p` は格納フィールドの読み取りと字面が同じだが、getter が `uniq/Self` なら `x@uniq` を暗黙に取り、所有 `Self` なら `x` を消費する（Copy なら複製する）。

```kimi
struct Meter
    var hits: i32 = 0
    public var limit: i32 = 10           // 格納 Property：本当に読み取るだけ
    public computed reading: i32
        get(self: uniq/Self) -> i32
            self.hits += 1
            return self.hits

var meter = Meter.init()
let x = meter.limit        // 読み取り
let a = meter.reading      // 字面は同じだが、meter@uniq を取って hits を書き換える
let b = meter.reading      // a と b は別の値になる
let fixed = Meter.init()
// let c = fixed.reading   // エラー：let は排他取得できない（読み取りに見えるのに）
```

**(b) 使用側に手がかりがない。** メソッドなら `()` が呼び出しを示し、SPEC §7.3 により名前ごとに受け手の形状が固定されている。Property にはどちらもなく、宣言を読まなければ `x.p` が `x` に何をするか分からない。原則 2（局所的推論）と原則 3（明示的意味論）に反する。

**(c) static 状態も同じ形で書き換わる。** SPEC §7.3 の例 `Registry.meter.reading` は、可変 static の `Registry.meter` を排他で貸して書き換える。

**(d) 所有 setter は Copy の更新を黙って捨てる。** `set(self: Self, value:)` の受け手は所有受け手の規則（SPEC §7.3 の表）で渡される。`Self` が Non-Copy なら `holder@move.item = value` と書き、代入文が受け手を破壊する。`Self` が Copy なら `point.x = 10` が綴りなしで通り、`point` の Copy に対して setter が走って更新が消える。setter は Unit を返すので、Copy 上の効果は必ず失われる。SPEC §7.3 が受信者について「Copy を変更して更新を捨てることはない」と定めた事故が、代入では起きる。

### 1.2. 方針

1. **accessor の受け手形状を操作ごとに固定する。** instance `get` の受け手は `ref/Self`、instance `set` の受け手は `uniq/Self` のみとする。省略形はこの受け手を補う（現行どおり）。static accessor は受け手を持たない（現行どおり）。
2. **排他アクセスや消費を伴う操作は関数で書く。** 呼び出しの形と名前（SPEC §4.7.1 の命名規約）で効果を見せる。構文は追加しない。
3. **Contract の `property` 要件も同じ制限を受ける。** `has get` と `has set` は現行どおり `get(ref/Self)` と `set(uniq/Self, value)` を要求し、明示シグネチャだけが制限される。
4. **object 形式の受け手は accessor に認めない。** `ref/Self` の getter と `uniq/Self` の setter は、SPEC §7.3 の経路規則（object 借用経路が `ref/Self`・`uniq/Self` を満たす）により object ハンドル経由でも呼べるので、機能は失われない。
5. **消費する関数の命名規約を加える。** Property で観測する値を、受け手を消費して取り出す関数は `into` + 名詞とする。

### 1.3. 保証の範囲

本書が保証するのは、**`x.p` が `x` に対して行う取得が常に共有借用であり、`x.p = v` が行う取得が常に書き込み権限つきの排他取得である**ことである。共有受け手からは、格納された `uniq/T` の排他再借用（SPEC §13.5.5.1 で Write 能力が要る）、`rc`/`arc` の payload 更新、Non-Copy 部分の抽出のいずれも安全コードではできない。したがって getter が `self` から到達する格納領域を書き換える経路は、可変 static 領域と unsafe だけになる。

static getter の本体が static 領域を更新できることは本書の範囲外で、通常の関数と同じく STYLE §3.3 の `[Kimi]` 規則（観測可能な効果を持たない）に委ねる。効果境界（SPEC §8.4.10）の拡張は別提案とする。

## 2. 期待する動作

```kimi
struct Meter
    var hits: i32 = 0
    public computed reading: i32
        get(self: uniq/Self) -> i32           // エラー：getter の受け手は ref/Self
            self.hits += 1
            return self.hits
    public computed total: i32
        get(self: Self) -> i32 => self.hits   // エラー：所有受け手も不可
    public computed handle: objref/Meter
        get(self: objref/Self) -> objref/Meter => self   // エラー：object 形式も不可

    public func nextReading(self: uniq/Self) -> i32     // 修正：状態を進める操作は関数
        self.hits += 1
        return self.hits
    public computed hitCount: i32
        get() -> i32 => self.hits             // OK：共有読み取り（get(self: ref/Self) と同じ）

group Registry
    public var meter: Meter = Meter.init()

var meter = Meter.init()
let a = meter.nextReading()               // 排他取得が呼び出しの形で見える（SPEC §7.3）
let fixed = Meter.init()
let c = fixed.hitCount                    // OK：let でも読み取れる
let d = Registry.meter.hitCount           // OK：static 経由でも読み取りのみ
```

```kimi
struct Point
    Self is Copy
    var raw: i32 = 0
    public computed x: i32
        get() -> i32 => self.raw
        set(self: Self, value: i32) -> () => ()   // エラー：setter の受け手は uniq/Self
        // 現仕様では point.x = 10 が point の Copy に対して走り、更新が消える

struct Holder
    private var item: Resource
    public computed view: ref/Resource
        get() -> ref/Resource => self.item@ref/Resource   // OK：共有借用の結果
    public func intoResult(self: Self) -> Resource        // 旧 computed result の置き換え
        return self.item@move
    public var slot: Resource
        set(value: Resource) -> ()                         // OK：set(self: uniq/Self, value:) と同じ
            storage = value@move

let holder = Holder.init(makeResource())
let owned = holder@move.intoResult()      // 消費が綴りと呼び出しの形で見える
```

```kimi
contract Api
    property item: i32
        get(self: Self) -> i32                      // エラー
        set(self: objuniq/Self, value: i32) -> ()   // エラー

contract Viewed
    property item: ref/Resource
        get(self: ref/Self) -> ref/Resource         // OK：結果 Origin は self に省略される
```

共有 getter の `x.p` は、フィールド読み取りと同じ前提条件（初期化済み・完全・アクセス可能・Loan 競合なし）だけで成立する。`let` 束縛、所有型の引数、`ref` 経由の経路、`rc`/`arc` の payload のいずれを受け手にしても、書き込み権限の不足で失敗することはない。

## 3. 仕様変更

### 3.1. 受け手形状の規則

| accessor | 許可する受け手 | 省略時に補う受け手 | 宣言エラーになる受け手 |
| --- | --- | --- | --- |
| instance `get` | `ref/Self` | `ref/Self` | `Self`、`uniq/Self`、`obj`/`rc`/`arc`/`objref`/`objuniq` 形式 |
| instance `set` | `uniq/Self` | `uniq/Self` | `Self`、`ref/Self`、`obj`/`rc`/`arc`/`objref`/`objuniq` 形式 |
| static `get`/`set` | なし | — | 受け手の記述 |
| Contract 明示要件 `get`/`set` | instance の行と同じ | 同じ | 同じ |

- 受け手の Origin 注釈（`get(self: ref/Self during static) -> T` など）は引き続き許可する。形状は正規化した型の外側の Semantics で判定し、Origin と binding set は見ない。
- Effective Core が `Self` でない受け手（無関係な型、`raw/Self`、型パラメーター）は現行の形成エラーのままとする。本規則は Core が `Self` で Semantics が合わない場合に適用する。
- 受け手の形状は宣言で固定され、本体が必要とする能力で変わらない（現行どおり）。排他アクセスや消費を要する本体は関数に移す。
- stored Property の custom accessor は現行の SPEC §11.2 で既に `ref/Self`／`uniq/Self` に固定されている。本規則は computed accessor と Contract 明示要件を同じ形状に揃えるものである。

### 3.2. getter の受け手取得

Property 読み取り `x.p`（custom、computed、required `get`）の受け手は受信者式（SPEC §3.4、§7.3）であり、常に共有で取得する。SPEC §7.3 の表の `ref/Self` 行をそのまま適用する。

| 受け手の値種 | 取得 |
| --- | --- |
| 所有 Place、所有一時値 | `x@ref` |
| 借用値 | `x@follow@ref`（参照経路選択の後） |
| object 種 | 完全な Sealed payload なら `x@follow@ref`、それ以外は `x@objref` |

共有取得は呼び出し予約（SPEC §15.6.7）の排他予約を始めない。`let`・所有型の引数・getter 結果の storage（SPEC §15.1.5 の排他取得条件）は共有取得の対象なので、`x.p` が貸与点の書き込み可能性で失敗することはない。SPEC §11.2.3 の「所有 getter 結果の Temporary Place は排他借用できない」規則は、その結果に対する `uniq/Self` メソッド呼び出しと更新に引き続き適用する。

### 3.3. setter の受け手取得

`x.p = v`、複合代入、インクリメント・デクリメントでは、代入先を書き込み権限つきで特定した後、custom・computed・required `set` の受け手を受信者式として取得する（SPEC §13.7.1、§7.3）。要求型が `uniq/Self` に固定されるので、`set` 自身の受け手取得による暗黙の Copy と所有権移転はなくなる。明示の `@move`・`@copy` は既存規則（SPEC §3.6.1、§15.1.5）に従い、`holder@move.item = value` や `point@copy.x = 10` は、明示的に作った一時値や Copy を更新する操作として書ける。`objuniq/T` の経路は SPEC §7.3 の経路規則で `uniq/Self` を満たす。

複合代入（SPEC §13.7.2）は受け手を一度だけ特定し、`get`（共有）と `set`（排他）を順に呼ぶ。stored custom accessor と同じ組合せになる。

### 3.4. Non-Copy 結果と消費

getter の結果が Non-Copy のときは、共有受け手から合法に作るか取得する。新しい値の生成、格納された参照の Copy、`self` に依存する借用（`ref/T during self`）がこれにあたる。共有受け手は所有する Non-Copy 部分を抽出できず、格納された `uniq/T` を排他で再借用することもできない。

受け手を消費して値を取り出す操作は関数で書く。

```kimi
public func intoResult(self: Self) -> Resource => self.item@move
let owned = holder@move.intoResult()
```

状態を進めて観測値を返す操作も関数で書く（`nextReading(self: uniq/Self)`）。

### 3.5. Contract の `property` 要件

- `has get` は `get(ref/Self) -> T`、`has set` は `set(uniq/Self, value: T) -> ()` を要求する（現行どおり）。
- 明示シグネチャの受け手は本書 3.1 の形状に限る。Origin 注釈は許可する。
- ObjectViewCompatible（SPEC §8.4.7.2）の要件表は変更しない。object 経由の呼び出しは SPEC §12.4.4 の経路規則で `ref/Self`・`uniq/Self` の要件を満たす。
- 標準操作の witness（SPEC §11.4.2）の橋渡し表は、共有受け手の `get` と排他受け手の `set` だけを扱っており、変更しない。

### 3.6. 命名規約

SPEC §4.7.1 の命名対表と STYLE §1.2 の表に次の行を加え、既存の行の例を補う。

| 対 | 規約 | 例 |
| --- | --- | --- |
| Property で観測する / 受け手を消費して取り出す | 名詞の Property / `into` + 名詞の関数 | `item` / `intoItem` |
| 観測する / 進める・取る（既存行） | 異なる動詞。Property との対では動詞 + 名詞 | `peek` / `next`、`reading` / `nextReading` |

### 3.7. 変更しない事項

- メソッドの受け手形状（SPEC §7.3）。関数は引き続き `Self`、`ref/Self`、`uniq/Self`、object 形式を使える。
- 一つの Property の `get` と `set` が receiver shape 規則の対象外であること。形状が操作ごとに固定されるので、同じ名前に共有と排他が共存する唯一の場所として残る。
- static accessor、構築中の accessor 禁止（SPEC §11.3.1）、getter 結果の Temporary の制限（SPEC §11.2.3）、custom setter の Non-Copy 入力（SPEC §11.2.4）、Origin の補完（SPEC §11.3）。

## 4. 診断と回復

### 4.1. 診断

Language Error `AccessorReceiverShape_Kd` を新設する（要件 `Binding.AccessorReceiverShape`：「the accessor receiver has the shape of its operation」）。原因は型の形成ではなく形状規則なので、現行の形成エラーは流用しない。

- **単位：** accessor 宣言ごとに一件。
- **位置：** 主範囲は書かれた受け手の型。関連位置は Property の見出し。
- **Reason：** accessor の種類（`get`/`set`）、書かれた Semantics、要求される形（`ref/Self` または `uniq/Self`）。
- **Advice：** 書かれた受け手・Origin・本体を保ったまま関数へ移す（`func name(self: R) -> T`、setter なら `func name(self: R, value: U) -> ()`）。名前は本書 3.6 に従う。受け手の型を置き換える助言はしない。object 形式や所有形式の本体は値形式では成立しないことがあり、共有受け手で有効な本体（`self@copy` など）が排他受け手で有効とも限らないからである。
- **構造化修復候補（SPEC §23.3.6）：** 「関数へ移す」一つを提示する。対象は `set` を持たず Contract 要件でない computed Property で、`computed name: T` と `get(self: R) -> T` の見出しを `func name(self: R) -> T` に置き換え、本体・Origin 節・Attribute・アクセス修飾子を保つ。受け手を保つので本体の意味は変わらず、関係する条件はない。使用側の `x.name` は既存の診断（関数には呼び出しが必要）で別に報告する。それ以外の対象は Advice にとどめる。
- **SPEC §7.3 の診断表：** 「Only shared permission」行の提案「enclosing method に `self: uniq/Self`」は、enclosing の宣言が getter のときは提示せず、操作を関数へ移す Advice にする。

### 4.2. 回復と相互作用

- 本体は書かれた受け手の型で検査する。`uniq/Self` getter の `self.hits += 1` は本体では有効なので、宣言エラーに本体のエラーが重ならない。
- 形状エラーの Property も member lookup に残し、使用は通常どおり検査する。使用がその宣言エラーだけを理由に失敗する場合（`let` 受け手に対する `uniq/Self` getter の取得失敗など）は派生問題として扱い（SPEC §23.3.6.4）、独立した診断を重ねない。
- 正当な対：`get()`、`get(self: ref/Self during static)`、`set(value: U)`、`set(self: uniq/Self, value: U)`、Contract の `has get, set` と明示 `get(self: ref/Self)`／`set(self: uniq/Self, value:)`。

## 5. 既存の提案との関係

### 5.1. Implicit Exclusive Receiver（2026-09-23、取り込み済み）

[同提案](../Changes/2026-09-23%20Implicit%20Exclusive%20Receiver.md) は受信者式の暗黙取得を導入し、その例と §11.2・§11.2.3 の変更で `uniq/Self` getter の暗黙排他取得を定めた。本書はその getter 形式を廃止するが、受信者式の暗黙取得の規則自体（メソッド、共有 getter、closure 呼び出し）は変更しない。同提案の本文は固定のまま変更せず、本書を取り込む場合は、同提案から正式仕様に入った `uniq/Self` getter の部分を本書で置き換えることを `draft/INTEGRATED.md` に追記する。

### 5.2. Property Semantics（2026-09-10、取り込み済み）

[同提案](../Changes/2026-09-10%20Property%20Semantics.md) は stored instance accessor の受け手を `ref/Self`（get）と `uniq/Self`（set）に固定した。本書は同じ形状を computed accessor と Contract 明示要件に広げるものである。

### 5.3. STYLE §3.3 の助言

STYLE §3.3 の `[Advice]`「共有 getter を優先する。排他アクセスや消費を要する操作は関数の方が明確」を言語規則（`[Language]`）に昇格する。

## 6. 既存コードへの影響

テキスト走査による調査（2026-10-02）。実装時に新しい検査で全件を確認する。

| 対象 | 該当 | 対応 |
| --- | --- | --- |
| Kimi ライブラリ、`docs/LIBRARY.md`、`docs/examples` | なし（computed はすべて `get()` 省略形、custom setter なし） | — |
| SPEC の例 | §7.3 の `Meter.reading`／`Registry.meter.reading`、§11.2.2 の `Meter.reading`／`Holder.result`、§11.2.3 の `holder.view@follow@uniq`／`holder.view.update()` | 本書 8 のとおり関数へ書き換えるか削除する |
| マイルストーン | `tests/milestones/Milestone24.kimi` の `Parcel.result`（`get(self: Self)`）と `parcel@move.result` | `func intoResult(self: Self) -> Resource` と `parcel@move.intoResult()` に再綴りする。`tests/milestones/README.md` の行 24 と関連記述、`DiagnosticSnapshotTest` の milestone/Milestone24 基準を更新する |
| テスト | `AccessorReceiverBindingTest`（受け手形式一覧、要件 `get(self: Self)`、`set(self: Self, ...)` を使う 2 件）、`PropertyBindingTest.ComputedWitnessRetainsTheExplicitReceiver`、`ReceiverShorthandTest.ComputedAccessorsBindSelfAndPreserveExplicitReceiverOverrides`、`CopyPropertyEmissionTest.ComputedExclusiveGetterBorrowsWritableReceiver`（native fixture `CopyPropertyExclusiveGet`） | 拒否側へ移すか `ref/Self`／`uniq/Self` に書き換える。fixture は `nextReading()` 形に置き換えて実行の網羅を保つ。`PropertyRevisionParseTest` は構文解析のみで変更しない |
| 診断コーパス | `tests/diagnostics/syntax.json` | 本書 4 の拒否例と正当な対を追加する |
| STATUS | 「computed getters can borrow exclusively」（P23 の項）、「ownership-bearing accessor generation」の残課題 | 前者を削除し、後者を Non-Copy の結果・入力に限定して記述する |

影響はすべて宣言時のコンパイルエラーとして現れ、これまで有効だった読み取りや代入の挙動が黙って変わることはない。pre-alpha のため互換性の手当ては要求しない。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U1：仕様・文書の取り込み | 本書 8 の更新、`docs/SETTLED.md` の項目追加、`draft/INTEGRATED.md` の記録、Milestone24 の再綴りと README・PLAN の更新 | 仕様が本書 3 と一致し、draft への依存がない |
| U2：形状検査と診断 | `Binding.Properties.cs` の accessor 検証に形状検査を加え、`BindingModel.cs` と `Binding.cs` のコード対応、`DiagnosticCode.cs`・`DiagnosticCode.tinyhand`・`DiagnosticRequirement.tinyhand` の項目、修復候補、SPEC §7.3 診断表の条件を実装する。受け手取得処理（`Binding.Expressions.cs` の受信者式の適応）は変更しない | 本書 2 の拒否例と正当な対が通り、CLI と language server の代表出力を確認し、DIAGNOSTICS §10 の手順を満たす |
| U3：テストと検証 | 本書 6 のテスト更新、fixture の置き換え、スナップショットの再基準化、STATUS の更新 | `verify.ps1 -Class AccessorReceiverBindingTest,PropertyBindingTest,ReceiverShorthandTest,CopyPropertyEmissionTest,DiagnosticSnapshotTest -Fixtures 'CopyProperty*.ll' -Milestone 24` が通り、最後に `-Mode Session` が通る |

性能：検査は accessor 宣言ごとに Semantics の一比較で、使用ごとの費用はない。`WarmAccessorReceiverChecksAllocateNothing` の割当て回帰を維持する。

## 8. 文書更新計画

正式な取り込みでは英語で更新し、仕様に draft への依存を残さない。

| 文書 | 更新内容 |
| --- | --- |
| SPEC §11.2 | 「Receiver shorthand」に本書 3.1 の表の規則を加える。「An explicit receiver Type remains available and must satisfy the accessor restrictions」をこの規則への参照にする |
| SPEC §11.2.2 | 「Instance receivers follow explicit ordinary function contracts, including ownership-bearing receivers」と `get(self: uniq/Self)` の暗黙排他取得の文を、本書 3.2・3.3 の記述に置き換える。`Meter` の例を拒否例と `nextReading` に、`Holder.result` を `intoResult` 関数に置き換える。「An owning getter or setter consumes the complete receiver, so … `holder@move.result` or `holder@move.item = value`」の段落を削除し、本書 3.4 の記述に置き換える |
| SPEC §11.2.3 | 「a `uniq/Self` method or getter cannot be called on an owned getter result」から「or getter」を除く。`holder.view@follow@uniq`／`holder.view.update()` の例は、共有 getter が `self` から `uniq/T` を返せないため、`ref/T` の例に置き換えるか削除する |
| SPEC §11.4 | 明示要件シグネチャの受け手制限を一文加える（例 `ReplaceableItem` は既に適合） |
| SPEC §7.3 | 例の `Meter.reading` を `func nextReading(self: uniq/Self)` に、`let seen = meter.reading` と `Registry.meter.reading` を呼び出し形に置き換える。「The `get` and `set` of one Property are distinct operations and are exempt」に形状が §11.2 で固定される旨を添える。診断表の「Only shared permission」行に本書 4.1 の条件を加える |
| SPEC §4.7.1 | 命名対表に本書 3.6 の行を加える |
| SPEC §3.4、§13.7.1、§15.1.5、§15.6.7 | 変更なし。受信者式の定義と取得規則は現行どおりで、要求型の固定は §11.2 に書く |
| Appendix F | `AccessorReceiver` の構文は変えない。散文（F.6 の accessor の段落）に形状規則を一文加える |
| Appendix E | 変更なし（「Receiver Expression」「Getter result Type」はそのまま） |
| STYLE §1.2、§3.3 | 命名対表の行を加える。§3.3 の `[Advice]` を `[Language]`（SPEC §11.2 への参照）にし、`into` 命名の `[Kimi]` 規則を一行加える |
| GUIDE | Property の段落に「getter の受け手は常に共有、setter は常に排他。更新・消費は関数で」を加える |
| SETTLED | 「Exclusive or owning accessor receivers」を本書 1.1 の問題・例・不採用の理由つきで加え、再提案を抑止する |
| STATUS、PLAN（P24 の説明から「legal receiver consumption」を外す）、PLAN_HISTORY、CODEMAP（代表テスト名が変わる場合）、DIAGNOSTICS（新コードの単位と前提） | 実装後に更新する。STATUS は検証済みの対応境界が変わったときだけ更新する |
| draft/INTEGRATED.md | 本書の取り込み範囲と、本書 5.1 の置き換えを同じコミットで記録する |

## 9. 設計判断

### 9.1. 採用しなかった案

| 案 | 不採用の理由 |
| --- | --- |
| 使用側に綴りを要求する（`meter@uniq.reading`） | SPEC §7.3 の暗黙取得と、`tasks@uniq.length`（排他で貸してから共有で読む）の意味と衝突し、一つの構文に二つの取得規則ができる |
| 宣言側に印を付ける（`mutating get` など） | 宣言には見えても使用側の字面は読み取りのままで、問題 (b) が残る |
| 警告にとどめる | 形式が残る限り、`let` 受け手の失敗や Copy の更新消失も残る |
| `objref/Self` getter と `objuniq/Self` setter を残す | `ref/Self`・`uniq/Self` は経路規則で object 経由からも呼べ、失うのは「`self` をハンドルとして返す getter」だけである。形式を後から加える方が、後から外すより安全である |
| 所有 setter を Copy の `Self` だけ禁止する | 特例が増える（原則 1）。Non-Copy の所有 setter にも実用がない |
| static getter の static 領域更新を禁止する | 効果境界（SPEC §8.4.10）の拡張であり、本書の範囲を超える。別提案とする |

### 9.2. Kimigayo Principles との対応

- **原則 1：** 読み取りは Property、更新と消費は関数という一つの形に揃え、accessor の受け手形式を操作ごとに一つにする。
- **原則 2：** `x.p` と `x.p = v` が `x` に何をするかを、宣言を読まずに字面から決められる。
- **原則 3：** 排他借用は `()` と名前、消費は `@move` と名前に必ず現れる。
- **原則 4：** 問題を宣言で一度だけ報告し、条件を明示した修復候補を提示する。

### 9.3. 残る確認

本書は仕様・ソース・既存テストとの静的な照合による。新しい検査の実装・実行試験は未実施である。残る確認は、テスト全体での該当宣言の数、Milestone24 のスナップショット差分、修復候補の適用範囲である。
