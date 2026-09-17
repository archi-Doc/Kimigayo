# 全体置き換えの制約緩和 — 最終仕様変更案

日付: 2026-09-17

本書で変更する事項は、[SPEC.md](../../SPEC.md) およびその参照先より優先する。変更しない事項には既存仕様を適用する。置換・追記先は本書 §6 に示す。仕様の確定は実装完了を意味しない。

「本書 §」は本書内の参照、それ以外の節番号は既存仕様の参照とする。

## 1. 基本方針

非 `open` な完全なオブジェクト本体（payload）を、通常の `ref/T`・`uniq/T` として借りられるようにする。全体の置き換え・交換は `uniq/T` を受ける共通 API で扱う。

原則として、値借用は完全な参照先型の格納先を指す。例外は、ObjectCompatible で保護された従来のオブジェクト receiver 適応・基底部分への receiver 射影であり、これらから全体更新・所有値の切り出し・無制限の排他借用の逸出を許可しない。本案の payload 射影は完全な値への借用であり、この例外に含めない。

したがって、通常の完全な所有値は `open` でも従来どおり更新できる。オブジェクト本体は本書の射影で完全性と排他性を証明した場合に更新でき、`open` なビュー・基底部分・共有アクセスでは全体更新できない。フィールド更新、ハンドルの差し替え、未初期化領域への初期化は別の操作として既存規則に従う。

## 2. 型要件と payload 射影

### 2.1. 型要件 `Sealed`

Core に compiler-intrinsic Requirement `Sealed` を追加する。`T is Sealed` は、正規化した `T` が次の両方を満たすことを表す。

1. outer Semantics が owner で、有効な Core を表す。
2. その Core が `open struct` でも `Never` でもない。

**sealed Core** は派生型を持てない Core、すなわち `open struct` 以外の Core とする。struct の既存用語を一般化するが、型要件 `Sealed` には上記の値 Type の条件も含める。runtime Contract View は Core ではなく、`ref/X`・`obj/X` 等は非 owner なので要件を満たさない。`Never` は値も value metadata も持たないため除外する。

判定は外側の Core に対して行い、内部の型引数へ再帰的に Sealed を要求しない。例えば非 `open` な `Cell<T>` は、内部の `T` が open 型や参照型でも成立する。Tuple、固定長配列、scalar、string、Unit、enum、各種コレクション、Function Item・Closure・共通 Function Type も同じ定義に従う。`Callable` 自体は Requirement であり Core ではない。

本案では `Never` を除く有効な owner Core を言語上の具体的 payload 対象とする。`T is Sealed` の証拠は §8.1.1 の Supported Core／View Target の役割も満たし、汎用宣言で `obj/T`・`objuniq/T` 等を形成できる。ただし Sealed は object 形成の必要条件ではなく、open Core の既存の object 形成は維持する。Copy、Owned、排他性は導かない。

シグネチャと本体は宣言された Constraints を前提に検査する。有限レイアウトやプロファイル固有の payload アラインメントなど、代入する型・ターゲットに依存する表現条件だけを §8.10 の deferred obligation として記録し、生成前に解決する。型の役割・能力・寿命の不足は先送りしない。

ユーザー実装・conformance 宣言・同名定義では Sealed を付与できない。`and/or/not` と未知の型の判定は §8.7 に従う。未証明を偽とせず、`not Sealed` から owner や `open struct` を推論しない。

### 2.2. 明示的な射影

§13.5.5 の Borrow 表に次の行を追加する。各行は一つの定義済み操作であり、変換の連鎖を探索しない。

| 入力 | 明示表記 | 結果 |
| --- | --- | --- |
| `objref/T` または `objuniq/T` | `@ref/T` | payload への共有の子借用 `ref/T` |
| 読み取り可能な `obj/T`・`rc/T`・`arc/T` の place | `@ref/T` | payload への共有借用 `ref/T` |
| `objuniq/T` | `@uniq/T` | payload への排他の子借用 `uniq/T` |
| 排他的に書き込み可能な `obj/T` の place | `@uniq/T` | payload への排他借用 `uniq/T` |

共通条件は `T is Sealed` と既存の初期化・アクセス・Loan／Origin 規則である。入力の View Target と出力の参照先型は、型引数と内部 Origin を含む同じ完全な `T` とする。外側の借用期間は短縮できるが、射影自体で内部の型・Origin を変更しない。既存の Effective Type を利用できる一方、現在の派生型一覧や最適化で `open` を sealed と扱わない。

結果には参照先の Loan・Origin と所有者までの依存を引き継ぐ。参照を格納する変数の寿命と参照先の寿命を混同しない。再借用、引数渡し、格納、返却は通常の値借用と同じ規則に従い、共有借用は共存でき、競合する親アクセス・所有者の Move・解放・ハンドル差し替えは拒否する。所有権移転や参照カウント操作は行わない。

完全に指定した `@ref/T`・`@uniq/T` だけを追加する。省略形 `@ref`・`@uniq`、通常引数の暗黙適応、逆方向のオブジェクトビュー生成は拡張しない。`@uniq/obj/T` は従来どおりハンドル格納先への借用であり、payload 射影とは異なる。`rc/arc` は参照数 1 でも排他的な payload アクセスを与えない。

```kimi
func borrowPayload<T>(source: objref/T) -> ref/T from source
    T is Sealed
    return source@ref/T

func borrowPayloadMut<T>(source: objuniq/T) -> uniq/T from source
    T is Sealed
    return source@uniq/T
```

### 2.3. receiver と ObjectCompatible

同じ `T` の `ref/Self`・`uniq/Self` を要求するメンバーには、本書 §2.2 の行を receiver に限って暗黙に適用できる。候補適用時の順位は §10.2 の cross-semantics borrow/reborrow とし、選択後に一度だけ評価・適用する。通常の名前解決、順位比較、アクセス検査、評価順序を維持する。

この経路は完全な値への呼び出しとして検証し、追加の ObjectCompatible Proven を要求しない。公開された NotProven を呼び出し側で Proven に変更するわけではない。custom/computed accessor と生成済み Contract witness にも同じ規則を適用し、Property の権限や代入の右辺先行順序は維持する。

継承メソッドの Self は宣言元の基底型のままとする。sealed な派生型全体への射影は許可できるが、継承メソッドが基底部分を置き換えることはできない。本書の射影を使わないオブジェクト／基底 receiver 経路には従来の公開保証を適用する。

効果解析は §12.4.4.2 の格納関係と本書の完全性の証拠を組み合わせる。

- 射影結果の Whole／Base／Part／Separate／MayAlias は元の object から引き継ぐ。射影しただけで Part や Separate と分類しない。
- 完全な対象への合法な更新・借用返却は、それ自体では receiver 保存違反にしない。基底部分・全体性を証明できない receiver に対して同じ操作を許すものではない。
- 通常の `ref/T`・`uniq/T` 引数という表記だけで、効果要約に完全性の証拠を付けない。callee の操作を caller の実際の格納先へ合成し、基底部分への効果や逸出を隠さない。
- 未知の呼び出し・unsafe 効果は従来どおり検証する。公開状態は全許容型・Origin と明示的特殊化の集合について確定し、必要な要約・状態の公開を維持する。

## 3. 共通の更新操作

### 3.1. Core API

次の三つを Core intrinsic として確定する。オブジェクト専用 overload は設けない。

```text
Core.replace<T>(target: uniq/T, with => value: T) -> ()
Core.exchange<T>(target: uniq/T, with => value: T) -> T
Core.swap<T>(first: uniq/T, second: uniq/T) -> ()
```

`T` は任意の有効な完全な値 Type とし、Sealed は要求しない。対象は完全に初期化済みで、全体への排他的更新が許されなければならない。新しい値の適合・取得を完了してから操作する。完全な型は内部 Origin も含めて一致させる。

| 操作 | 内容と責任の扱い | 結果 |
| --- | --- | --- |
| replace | 古い値を元の格納先で破棄し、正常に完了した後で新しい値を配置する | Unit |
| exchange | 古い値を破棄せず取り出し、新しい値を配置する | 元の `T` 値 |
| swap | 二つの内容と責任を交換する。§15.7.2 の静的な非重複を要求する | Unit |

責任は各値が持つ所有・破棄等の責任を指す。`T` が借用型でも、その参照先の所有権を取得するわけではない。Core の定義 Identity だけが intrinsic の効果を持つ。

通常の owner place は §10.2 の暗黙の排他借用で渡せる。payload は本書 §2.2 の明示射影で渡す。既存の値借用は通常の再借用を使い、非 owner Type の格納先を新たに借りる場合は完全指定の storage borrow を使う。Property の権限から隠れた格納先へのアクセスを導かない。

exchange／swap の転送自体はユーザーコード・破棄・Abort を実行せず、対象の初期化を維持する。通常の代入が持つ未初期化・部分 Move 後の復旧能力は三 API に追加しない。借用からの破壊的 MoveOut、特殊な Construction／Destruction receiver の制限、不変の引数 Binding に対する `self = value` の禁止も維持する。

### 3.2. 利用例

次のメソッドを通常値と排他的な object で共用できる。例は本案採用後の意味を示し、現在のコンパイラーでの実行可能性は保証しない。

```kimi
struct Cell<T>
    public var value: T

    public init(value: T)
        self.value = value

    public func overwrite(self: uniq/Self, replacement: Self)
        Core.replace(self, with: replacement)

func readCell(cell: ref/Cell<i32>) -> i32 => cell.value

func example()
    var local = Cell<i32>.init(1)
    var object = Core.makeObj(Cell<i32>.init(2))
    let shared = Core.makeRc(Cell<i32>.init(3))

    local.overwrite(Cell<i32>.init(10))
    object.overwrite(Cell<i32>.init(20)) // receiver に排他的 payload 射影。
    let observed = readCell(shared@ref/Cell<i32>)

    let old = Core.exchange(object@uniq/Cell<i32>, with: Cell<i32>.init(30))
    Core.swap(local, object@uniq/Cell<i32>) // 通常値と payload の交換。

    // shared@uniq/Cell<i32> は拒否：共有所有から排他アクセスは得られない。
```

### 3.3. 評価順序と失敗

関数の引数評価は §10.1、排他 Loan の開始は §15.7.1、通常代入は §13.7.1 に従う。名前付き引数も記述順に評価し、二段階借用や Loan の遅延開始は導入しない。

```kimi
var p: i32 = 0
// Core.replace(p, with: p + 1) は拒否：排他借用後の競合する読み取り。
let next = p + 1
Core.replace(p, with: next)
```

競合診断は Loan の取得位置と競合位置を示し、依存関係が許すなら「値を先にローカル変数へ入れる」修正を提案する。引数評価が完了しなければ更新を開始しない。replace の破棄が Abort・非終了となれば新しい値を配置しない。cleanup と非巻き戻しは既存規則に従う。

## 4. 依存・同一性・破棄

### 4.1. 格納先更新の依存規則

以下を **§15.7.3「格納先更新の依存規則」** として追加し、§13.7.1 の対応する依存規則と §15.7 の各操作はここを参照する。

**更新の準備・移動・破棄・配置・cleanup は、新しい格納値、返却値、および存続する値が必要とする Loan／Origin を失効させてはならない。** `=`、replace、exchange、swap に共通して適用する。exchange／swap でも、破棄しないことは移動による依存の失効を許す理由にならない。

格納先の存続と内容の存続を分ける。操作に使う排他的アクセスと、それを支える親 Loan は維持する。古い内容に依存する借用と競合する更新は拒否し、同じアドレスへの再配置で依存を修復・復活させない。古い内容と独立した外部データへの依存は維持する。

例えば、フィールドに保存された `ref/Data` の Copy は外部の Data を借り続け得るが、フィールド格納先への `ref/(ref/Data)` は本体更新と競合する。取得元の構文ではなく、実際の Loan anchor で区別する。

payload 更新ではオブジェクトの同一性と Dynamic Type に基づく refinement を保持し、置き換えた内容に由来するフィールド値の事実と内部投影結果だけを無効化する。正当な格納先アクセスは維持する。ハンドル自体の差し替えや Move による事実の失効は §14.10.1 のままとする。

### 4.2. 内容の破棄とオブジェクトの存続

payload 更新は、同一性、格納領域、Dynamic Type、Descriptor、ヘッダーを維持する。内容の破棄では、対象 object の最終解放の状態遷移・カウント変更・領域解放を行わない。内容に含まれる所有ハンドル等の破棄は通常どおり行う。

replace は旧内容の deinit とフィールド・基底の破棄を既存順序で実行する。新しい内容は構築済みの値として配置し、コンストラクター、宣言時初期化、setter を再実行しない。空・破棄途中の payload を通常のアクセスへ公開せず、特殊 receiver、再入・通常ビュー生成・復活の禁止は §16.3–16.4 に従う。

最終解放ではその時点の内容を一度破棄して領域を解放する。exchange で取り出した値の責任は返却先へ移る。途中の破棄が Abort・非終了となった場合の非継続・非巻き戻しは既存規則のままである。

`let` フィールドへの個別代入は禁止したまま、新しい内容への全体更新を許可する。従来の全体更新禁止から得られた、object の全存続期間にわたるフィールド値の不変性は保証しない。安全性は排他 Loan と本書 §4.1 によって確保する。

§5 の unsafe の安全義務にもこの規則を適用する。raw pointer は安全な Loan の代わりにならず、その利用者はアクセス権・有効性・内容の存続を検証する。let フィールドの値を payload 更新をまたいで不変と仮定してはならない。

## 5. 実装・性能・検証

### 5.1. 共通化と最適化

型要件、完全性、権限、Loan／Origin は静的に検証する。射影結果は通常の `ref/T`・`uniq/T` の ABI を使い、所有者への依存は既存の Loan 情報に保持する。射影・本体更新のために、新しい参照モード、ヘッダー、追加の参照カウント操作、実行時世代カウンターは導入しない。

射影は §21.2.3 の payload アドレスを返す。ヘッダーのアドレスを値借用として渡してはならない。配置・オフセットの数値は同節に従う。

| 対象 | 許可する最適化と条件 |
| --- | --- |
| 更新処理 | 対象検査・引数取得・責任移転の計画を共用する。内容の破棄には destroyValues を利用できるが、格納領域を解放しない。破棄不要な呼び出し・不要なコピー・一時領域は除去できる |
| 型情報 | sealed ビューでは Dynamic Type が View Target と一致する。具体的な生成先を確定できれば Descriptor load・間接呼び出しを除去できる。共有汎用コードでは Sealed だけで具体 entry が確定するとは限らない |
| 型検査 | 同じ具体型への既存の型検査・checked cast は判定を定数化できる。取得・Loan・Origin 等の静的検査と必要な効果は維持する |
| 解析情報 | 定義・特殊化・依存が不変なら要約・公開状態を再利用し、影響範囲だけ再計算する。sealed という理由だけで必要な ObjectCompatible 情報を省略しない |
| 破棄順序 | replace を exchange＋旧値破棄に変換するには、破棄対象の位置、依存、観測可能な効果、Abort・非終了時の意味を保つ。排他 Loan だけでは等価性の証拠としない |

値の破棄だけで格納領域の lifetime を終了させたり、let フィールドの不変性を object の全存続期間へ拡張したりしてはならない。

payload の swap は通常、その格納サイズに比例した転送を要する。同一性と内容の対応を維持する必要がなければ、`Core.swap(a@uniq/obj/T, b@uniq/obj/T)` でハンドル格納先を交換できる。これは payload を転送しない別の操作であり、コンパイラーが自動的に置き換えてはならない。

最適化・解析キャッシュ・コード共有の有無で受理条件を変えない。スレッド、内部可変性、runtime Contract の機能追加は本案の範囲外とする。

### 5.2. 依存更新と受け入れ検証

Sealed の証明、射影、更新効果、完全な型、Loan／Origin、破棄責任を検証済み計画に保持する。型形成、openness、関数・特殊化の効果が変われば、§18.3・§21.3.4 に従って再検証する。

公開型の `open` の付け外しは API の変更である。正負の Sealed 制約、継承、射影、公開効果に関する依存を再検証し、既存の利用を無効にする変更は破壊的変更として扱う。

| 検証単位 | 主な確認事項 |
| --- | --- |
| 型と API | 汎用シグネチャの定義時証明、Core の各種別、Never・非 owner・未証明型の射影拒否、正負の Sealed 制約、表現義務 |
| 射影と呼び出し | 明示／receiver の一致、共有 rc/arc の ref 成功と uniq 拒否、sealed 派生型全体と基底部分の区別、Property・witness・特殊化 |
| 借用と更新 | 内部借用との競合、独立した外部参照、親 Loan と所有者の保護、名前付き引数順、混在 swap、借用・ハンドル格納先の交換、非重複 |
| 破棄と生成 | 破棄回数・位置・順序、Abort・非終了時の非配置、let と refinement、同一性・ヘッダー維持、payload アドレス、不要な確保・カウント操作の不在 |

## 6. 置換・追記する条文

以下は統合先と変更範囲である。本書の優先条項は表の差分に適用し、関係する章全体を無効にはしない。

| 既存仕様の節 | 既存条文の要旨 | 変更の種類 | 新しい規則 |
| --- | --- | --- | --- |
| §3.3.2／§3.3.5–6 | 値借用の格納先、Dynamic Type と同一性、object 対象 | 追記 | 完全な payload 借用と同一性を保つ内容更新（本書 §1・本書 §2・本書 §4） |
| §5 冒頭／§5.2 | unsafe の有効性・アクセス・更新義務 | 追記 | payload 更新をまたぐ let 不変性を仮定しない（本書 §4.2） |
| §6.2.2 | sealed は非 open struct | 一般化・追記 | sealed Core と公開 openness の API 変更（本書 §2.1・本書 §5.2） |
| §8.1.1／§8.2／§8.7／§8.10 | object の対象役割、要件と定義時証明 | 追記 | Sealed の意味、対象役割の証拠、表現上の義務（本書 §2.1） |
| §8.4.4–5／§9.5.1／§10.2 | 基底 receiver、conformance、適応順位 | 追記 | 完全な sealed receiver には射影表の行を cross 順位で適用。基底経路の保証は維持（本書 §2.3） |
| §12.4.3–4 | object 呼び出しの保証、同型でも全体更新禁止、一般の objuniq→uniq 変換なし | 条件付きに変更 | 本書 §2 の定義済み射影と完全値呼び出しを許可。それ以外は従来どおり |
| §12.4.4.2 | Whole/Base 更新・排他借用逸出は保存違反 | 条件付きに変更・追記 | 完全性の証拠と格納関係を伝播し、保護対象への違反を判定（本書 §1・本書 §2.3） |
| §13.5.5 | 値借用と object 借用の相互変換禁止 | 条件付きに変更 | 本書 §2.2 の単一操作行だけ追加。逆方向や一般の変換は追加しない |
| §13.5.7–8 | 各行は単一操作、payload 適格性は intrinsic の形成規則 | 追記 | 射影も単一操作。Sealed を対象役割の証拠として共有し、既存の生成規則は維持（本書 §2） |
| §13.7.1 | 旧値破棄が新値の依存を壊してはならない | 参照へ置換 | 共通の §15.7.3 を参照。代入の順序・部分復旧は維持（本書 §4.1） |
| §14.10.1 | 同一性・Dynamic Type が不変なら refinement を維持 | 追記 | payload 更新とハンドル差し替えを区別（本書 §4.1） |
| §15.7／§15.7.1–2 | Replacement は =、Exchange/Swap の API 名は未確定 | 置換・追記 | 本書 §3 の三 API を確定。= は別表記・別評価順として維持。非重複規則は共用 |
| §15.7.3（新設） | — | 追記 | 格納先更新の依存規則（本書 §4.1） |
| §16.3.3／§16.4 | 破棄と object 最終解放、特殊 receiver | 追記 | 内容の破棄と格納領域の解放を分離し、既存の再入制限を適用（本書 §4.2） |
| §18.3／§21.3.4 | 公開情報と依存の再検証 | 追記 | Sealed・射影・更新計画の記録と失効（本書 §5.2） |
| §21.2.2–4／§21.3–5 | payload／借用表現、共有生成・cleanup・最適化 | 追記 | 通常の値借用 ABI と更新計画を共用し、意味を保つ最適化だけ許可（本書 §5.1） |
| §22.1 | Copy／Owned／Callable 等の intrinsic 表 | 追記 | Sealed と Core.replace／exchange／swap の Identity・契約（本書 §2.1・本書 §3.1） |
