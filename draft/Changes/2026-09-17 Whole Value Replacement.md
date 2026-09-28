# 全体置き換えの制約緩和 — 最終仕様変更案

日付: 2026-09-17

本書は、オブジェクト本体（payload）を通常の値と同じ API で置換・交換するための仕様変更案である。[SPEC.md](../../SPEC.md) と参照先のうち、本書 §6 に示す差分に限って本書を優先する。それ以外は既存仕様に従う。仕様の確定と実装の完了は区別する。

「本書 §」は本書内、それ以外の節番号は既存仕様を指す。`Core` は型の構成要素、Core Kotonoha は基盤モジュールを指す。基盤 API の修飾名には現行の `Kimi.*` を使う。

## 1. 変更の要点

**非 `open` な完全な payload を `ref/T`・`uniq/T` として借用し、`Kimi.replace`・`Kimi.exchange`・`Kimi.swap` で更新できるようにする。** オブジェクトの同一性と Dynamic Type は変えず、内容だけを更新する。

| 対象 | 全体更新 |
| --- | --- |
| 通常の完全な所有値 | 排他アクセスがあれば許可。`open` 型でもよい |
| 非 `open` な完全な payload | 本書 §2 の排他的な射影を通じて許可 |
| `open` な object ビュー、基底部分 | 本案では許可しない |
| 共有アクセスだけを持つ payload | 許可しない。`rc/arc` は参照数 1 でも共有アクセスのみ |

ここで**射影**とは、ハンドルや object 借用から payload を指す値借用を得る操作である。ハンドル格納先の借用、フィールド更新、未初期化領域への初期化とは区別する。

値借用は原則として完全な参照先型の格納先を指す。従来の object receiver 適応・基底部分への receiver 射影は例外であり、`ObjectCallCompatible` の保証で保護する。本案の payload 射影は完全な値への借用なので、この例外には含めない。

## 2. payload を借用する条件

### 2.1. 型要件 `Sealed`

Core Kotonoha に compiler-intrinsic Requirement `Sealed`（`Kimi.Sealed`）を追加する。`T is Sealed` は、正規化した `T` が次の条件をすべて満たすことを表す。

- 外側の Semantics が `owner` で、有効な Core を表す。
- その Core が `open struct` でも `Never` でもない。

**sealed Core** は派生型を持てない Core、すなわち `open struct` 以外の Core とする。ただし型要件 `Sealed` は上記の owner 条件も要求し、値と value metadata を持たない `Never` を除外する。runtime Contract View は Core ではなく、`ref/X`・`obj/X` 等も非 owner なので要件を満たさない。

判定するのは外側の Core だけである。非 `open` な `Cell<T>` は、内部の `T` が open 型や参照型でも `Sealed` を満たす。Tuple、固定長配列、scalar、string、Unit、enum、コレクション、Function Item・Closure・共通 Function Type にも同じ定義を適用する。`Callable` 自体は Requirement であり、Core ではない。

本案では、`Never` を除く有効な owner Core を具体的な payload 対象とする。`Sealed` の証明は §8.1.1 の Supported Core／View Target の証拠にもなり、汎用宣言で `obj/T`・`objuniq/T` 等を形成できる。ただし、既存の open Core の object 形成も引き続き許可する。`Sealed` から `Copy`・`Owned`・排他アクセスは導かない。

宣言のシグネチャと本体は、宣言された Constraints だけを前提に検査する。有限レイアウトやプロファイル固有の payload アラインメントなど、代入型・ターゲットに依存する表現条件だけを §8.10 の deferred obligation として生成前に解決する。型の役割・能力・寿命の不足は先送りしない。

ユーザー実装、conformance 宣言、同名定義では `Sealed` を付与できない。`and/or/not` と未知の型の判定は §8.7 に従う。未証明を偽とせず、`not Sealed` から owner や `open struct` を推論しない。

### 2.2. 明示的な射影

§13.5.5 の Borrow 表に次の行を追加する。**すべて `T is Sealed` を要求する。** 各行は一つの定義済み操作であり、変換の連鎖を探索しない。

| 入力 | 表記 | 結果 |
| --- | --- | --- |
| `objref/T` または `objuniq/T` | `@ref/T` | payload への共有の子借用 `ref/T` |
| 読み取り可能な `obj/T`・`rc/T`・`arc/T` の place | `@ref/T` | payload への共有借用 `ref/T` |
| `objuniq/T` | `@uniq/T` | payload への排他の子借用 `uniq/T` |
| 排他的に書き込み可能な `obj/T` の place | `@uniq/T` | payload への排他借用 `uniq/T` |

入力の View Target と出力の参照先型は、型引数と内部 Origin を含めて同じ完全な `T` とする。外側の借用期間は短縮できるが、射影自体で内部の型・Origin は変えない。適応先には既存規則どおり Origin を明記せず、元の依存を引き継ぐ。既存の Effective Type は利用できるが、現在の派生型一覧や最適化を根拠に `open` を sealed と扱わない。

初期化・アクセス・Loan／Origin は通常の値借用と同じ規則で検査する。結果は参照先の Loan・Origin と所有者までの依存を引き継ぎ、再借用・引数渡し・格納・返却もそれに従う。参照変数自体の寿命を参照先の寿命に置き換えない。共有借用は共存できるが、競合する親アクセス、所有者の Move・解放・ハンドル差し替えは拒否する。射影による所有権移転や参照カウント操作はない。

追加するのは完全指定の `@ref/T`・`@uniq/T` だけである。省略形 `@ref`・`@uniq`、通常引数の暗黙適応、逆方向の object ビュー生成は拡張しない。`@uniq/obj/T` は従来どおり**ハンドル格納先**への借用である。

```kimi
func borrowPayload<T>(source: objref/T) -> ref/T from source
    T is Sealed
    return source@ref/T

func borrowPayloadMut<T>(source: objuniq/T) -> uniq/T from source
    T is Sealed
    return source@uniq/T
```

### 2.3. receiver と呼び出し保証

呼び出しの保証には、改名後の **`ObjectCallCompatible`** を使う。runtime Contract View の適格性を表す `ObjectViewCompatible(C)` は別の概念であり、本案では変更しない。

| receiver の経路 | 呼び出し規則 |
| --- | --- |
| 同じ完全な `T` の `ref/Self`・`uniq/Self` を要求するメンバー | 本書 §2.2 の射影を receiver に限って暗黙適用できる。完全な値への呼び出しとして検査する |
| 上記の射影を使わない object／基底 receiver | 従来どおり公開された `ObjectCallCompatible = Proven` を要求する |

新しい経路の順位は §10.2 の cross-semantics borrow/reborrow とする。通常の名前解決・候補比較・アクセス検査に従い、選択後に一度だけ評価・適用する。追加の `ObjectCallCompatible = Proven` は要求しないが、公開済みの `NotProven` を呼び出し側で `Proven` に変更するものではない。

custom/computed accessor と生成済み Contract witness にも同じ区別を適用する。Property の権限と代入の右辺先行順序は維持する。継承メソッドの `Self` は宣言元の基底型のままであり、sealed な派生型から呼んでも基底部分の全体置換は許可しない。

**効果解析では、格納関係と完全性の証拠を別々に保持する。** §12.4.4.2 の Whole／Base／Part／Separate／MayAlias は元の object から引き継ぎ、射影しただけで Part や Separate に変更しない。

- 完全性が証明された対象への合法な更新・借用返却は、それ自体では receiver 保存違反にしない。
- 単に引数型が `ref/T`・`uniq/T` であることは、caller の格納先が完全である証拠にならない。callee の効果を実際の格納先に合成し、基底部分の更新や無制限の排他借用の逸出を隠さない。
- 未知の呼び出し・unsafe 効果、公開要約、全許容型・Origin と全明示的特殊化を対象とする公開状態の検証は、引き続き必要とする。

## 3. 置換・交換 API

### 3.1. 契約

次の三つを Core intrinsic として確定する。オブジェクト専用 overload は設けない。

```text
Kimi.replace<T>(target: uniq/T, with => value: T) -> ()
Kimi.exchange<T>(target: uniq/T, with => value: T) -> T
Kimi.swap<T>(first: uniq/T, second: uniq/T) -> ()
```

`T` は任意の有効な完全な値 Type であり、`Sealed` は要求しない。対象は完全に初期化済みで、全体への排他的更新が許される格納先とする。新しい値の適合・取得を完了してから操作し、操作対象の型は内部 Origin も含めて一致させる。

| API | 処理 | 結果 |
| --- | --- | --- |
| `replace` | 旧値を元の格納先で破棄し、正常終了後に新値を配置する | Unit |
| `exchange` | 旧値を破棄せず取り出し、新値を配置する | 旧値 `T` とその責任 |
| `swap` | 二つの内容と責任を交換する。§15.7.2 の静的な非重複を要求する | Unit |

責任とは各値の所有・破棄等の責任である。`T` が借用型なら参照値を扱い、その参照先の所有権は取得しない。同名のユーザー関数に intrinsic の効果はなく、Core Kotonoha の定義 Identity で識別する。

引数の渡し方は次のとおりとする。

- 通常の owner place：§10.2 の暗黙の排他借用。
- payload：本書 §2.2 の明示射影。
- 既存の値借用：通常の再借用。
- 借用値・ハンドル等の非 owner Type の格納先：完全指定の storage borrow。

Property の権限から隠れた格納先へのアクセスは導かない。三 API は未初期化・部分 Move 後の復旧に使えず、通常代入 `=` の復旧規則も変更しない。借用から格納先を不完全にする MoveOut、特殊な Construction／Destruction receiver、不変の引数 Binding への `self = value` に関する禁止は維持する。

### 3.2. 評価順序と失敗

引数は名前付き引数も含め**記述順**に評価する（§10.1）。排他 Loan は借用の形成時に開始し、後続引数の評価中も存続する（§15.7.1）。二段階借用や遅延開始は導入しない。通常代入 `=` は引き続き右辺を先に評価する（§13.7.1）。

```kimi
var p: i32 = 0
// Kimi.replace(p, with: p + 1) // 拒否：排他借用後に p を読む。
let next = p + 1
Kimi.replace(p, with: next)   // 許可：先に新値を計算した。
p = p + 1                    // 通常代入は右辺先行なので許可。
```

引数評価が完了しなければ更新を開始しない。`replace` の破棄が Abort・非終了となれば新値を配置せず、非巻き戻し・cleanup は既存規則に従う。`exchange`・`swap` の転送自体はユーザーコード・破棄・Abort を実行せず、内部の空状態を公開しない。これはスレッド間の原子性を保証しない。

競合診断は Loan の取得位置と競合位置を示す。依存関係が許す場合には、新値を先にローカル変数へ入れる修正を提案する。

### 3.3. 利用例

以下は本案採用後の意味を示す。現在のコンパイラーでの実行可能性は保証しない。

```kimi
struct Cell<T>
    public var value: T

    public init(value: T)
        self.value = value

    public func overwrite(self: uniq/Self, replacement: Self)
        Kimi.replace(self, with: replacement)

func readCell(cell: ref/Cell<i32>) -> i32 => cell.value

func example()
    var local = Cell<i32>.init(1)
    var object = Kimi.makeObj(Cell<i32>.init(2))
    let shared = Kimi.makeRc(Cell<i32>.init(3))

    local.overwrite(Cell<i32>.init(10))
    object.overwrite(Cell<i32>.init(20)) // receiver は暗黙に payload を借用。
    let observed = readCell(shared@ref/Cell<i32>) // 通常引数では明示する。

    let old = Kimi.exchange(object@uniq/Cell<i32>, with: Cell<i32>.init(30))
    Kimi.swap(local, object@uniq/Cell<i32>) // 通常値と payload を交換。

    // readCell(shared)             // 拒否：通常引数の暗黙射影はない。
    // shared@uniq/Cell<i32>         // 拒否：共有所有から排他借用は得られない。
    // Kimi.swap(local, local)      // 拒否：二つの格納先が重複する。
```

ハンドルの交換は payload の交換と意味が異なる。

```kimi
var a = Kimi.makeObj(Cell<i32>.init(1))
var b = Kimi.makeObj(Cell<i32>.init(2))

Kimi.swap(a@uniq/Cell<i32>, b@uniq/Cell<i32>) // 同じ object の内容を交換。
Kimi.swap(a@uniq/obj/Cell<i32>, b@uniq/obj/Cell<i32>) // a・b の所有先を交換。
```

## 4. 更新時に維持するもの

### 4.1. Loan・Origin と格納先

次の規則を **§15.7.3「格納先更新の依存規則」** として追加し、`=`・`replace`・`exchange`・`swap` に共通適用する。§13.7.1 の対応規則は、この節への参照に置き換える。

**更新の準備・移動・破棄・配置・cleanup は、新しい格納値、返却値、存続する値に必要な Loan／Origin を失効させてはならない。** 破棄しない `exchange`・`swap` でも、移動による依存の失効は許されない。

格納先の存続と内容の存続は区別する。操作に使う排他アクセスと親 Loan は維持する一方、旧内容に依存する借用と競合する更新は拒否する。同じアドレスへの再配置で、失効した依存を修復・復活させない。旧内容と独立した外部データへの依存は維持する。

例えば、フィールドの `ref/Data` を Copy した値は外部の Data を借り続けられる場合がある。一方、そのフィールド格納先を借りる `ref/(ref/Data)` は本体更新と競合する。取得元の構文ではなく、実際の Loan anchor で判断する。

### 4.2. 同一性・破棄・フィールドの事実

payload 更新は **同一性、格納領域、Dynamic Type、Descriptor、ヘッダーを維持する。** 内容の破棄は対象 object の最終解放ではなく、その状態遷移・参照カウント変更・領域解放を行わない。内容に含まれる所有ハンドル等は通常どおり破棄する。

`replace` は旧内容の deinit とフィールド・基底の破棄を既存順序で実行する。構築済みの新値を配置し、コンストラクター・宣言時初期化・setter を再実行しない。空・破棄途中の payload を通常アクセスに公開せず、特殊 receiver、再入・通常ビュー生成・復活の禁止は §16.3–16.4 に従う。

最終解放では、その時点の内容を一度破棄して領域を解放する。`exchange` で取り出した旧値の責任は返却先へ移る。

`let` フィールドへの個別代入は禁止したまま、完全な内容の置換を許可する。そのため、`let` の値が object の全存続期間を通じて不変とは限らない。更新では旧内容に由来するフィールド値の事実と内部投影結果を無効化し、同一性・Dynamic Type に基づく refinement と正当な格納先アクセスは維持する。ハンドル自体の差し替え・Move による失効は §14.10.1 に従う。

この区別は unsafe（§5）と最適化にも適用する。raw pointer は Loan の代わりにならず、利用者がアクセス権・有効性・内容の存続を検証する。内容の破棄だけで格納領域の lifetime を終了させたり、payload 更新をまたいで `let` の値を不変と仮定したりしてはならない。

## 5. 実装と検証

### 5.1. 表現・最適化

型要件・完全性・権限・Loan／Origin は静的に検証する。射影結果は通常の `ref/T`・`uniq/T` の ABI を使い、§21.2.3 の **payload アドレス**を指す。ヘッダーのアドレスを値借用として渡さない。所有者への依存は既存の Loan 情報に保持し、新しい参照モード、ヘッダー、追加の参照カウント操作、実行時世代カウンターは導入しない。

| 対象 | 実装・最適化の条件 |
| --- | --- |
| 更新処理 | 対象検査・引数取得・責任移転の計画を共用する。内容破棄に `destroyValues` を利用できるが、格納領域は解放しない。不要なコピー・一時領域・破棄処理を除去できる |
| 型情報 | sealed ビューの Dynamic Type は View Target と一致する。具体的な生成先が確定すれば Descriptor load・間接呼び出しを除去できる。共有汎用コードでは `Sealed` だけで具体 entry が決まるとは限らない |
| 型検査 | 同じ具体型への既存の型検査・checked cast は判定を定数化できるが、取得・Loan・Origin の検査と必要な効果は維持する |
| 解析情報 | 定義・特殊化・依存が不変なら要約・公開状態を再利用し、影響範囲だけ再計算する。sealed という理由だけで必要な `ObjectCallCompatible` 情報を省略しない |
| 破棄順序 | `replace` を `exchange`＋旧値破棄に変換するには、破棄位置・依存・観測可能な効果・Abort／非終了時の意味を保つ。排他 Loan だけを等価性の証拠にしない |

payload の `swap` は通常、格納サイズに比例する転送を要する。本書 §3.3 のハンドル交換は別の操作なので、自動的に置き換えない。最適化・解析キャッシュ・コード共有の有無で受理条件を変えない。スレッド、内部可変性、runtime Contract の機能追加は範囲外とする。

### 5.2. 依存更新・受け入れ基準

検証済み計画には、`Sealed` の証明、射影、更新効果、完全な型、Loan／Origin、破棄責任を保持する。型形成・openness・関数／特殊化の効果が変われば、§18.3・§21.3.4 に従って再検証する。

公開型の `open` の付け外しは API 変更である。正負の `Sealed` 制約、継承、射影、公開効果への依存を再検証し、既存の利用を無効にする変更は破壊的変更として扱う。

| 検証対象 | 主な確認事項 |
| --- | --- |
| 型と API | 定義時証明、各 Core 種別、Never・非 owner・未証明型の射影拒否、正負の制約、表現義務、通常の open 所有値の更新 |
| 射影と呼び出し | 明示／receiver の一致、rc/arc の ref 成功と uniq 拒否、sealed 派生型全体と基底部分の区別、Property・witness・全特殊化の検証 |
| 借用と更新 | 内部借用との競合、独立した外部参照、親 Loan と所有者の保護、引数順、通常値／payload の混在交換、借用・ハンドル格納先の交換、非重複 |
| 破棄と生成 | 破棄回数・位置・順序、Abort／非終了時の非配置、let と refinement、同一性・ヘッダー維持、payload アドレス、不要な確保・カウント操作の不在 |

## 6. 既存仕様への統合先

以下は変更する条文と本書の対応である。関係する章全体を無効にするものではない。

| 統合先 | 変更内容 | 本書 |
| --- | --- | --- |
| §3.3.2／§3.3.5–6 | 完全な payload 借用、同一性を保つ内容更新を追記 | §1・§2・§4 |
| §5 冒頭／§5.2 | unsafe でも payload 更新をまたぐ let 不変性を仮定しない | §4.2 |
| §6.2.2 | sealed を Core 全般へ一般化し、公開 openness の変更を API 変更として扱う | §2.1・§5.2 |
| §8.1.1／§8.2／§8.7／§8.10 | Sealed、対象役割の証拠、定義時証明と表現義務を追加 | §2.1 |
| §8.4.4–5／§9.5.1／§10.2 | 同じ完全な sealed receiver への射影を cross 順位で追加。基底経路の保証は維持 | §2.3 |
| §12.4.3–4 | object の全体更新・objuniq→uniq の禁止に、本案の完全な payload 射影の例外を追加 | §1・§2 |
| §12.4.4.1–3 | ObjectCallCompatible の適用経路を区別し、効果解析に完全性の証拠を追加。公開状態の共通保証を維持 | §2.3 |
| §13.5.5／§13.5.7–8 | 射影を単一操作として追加し、Sealed の対象役割の証拠を共有。逆方向の変換は追加しない | §2 |
| §13.7.1／§15.7.3（新設） | 更新の依存規則を §15.7.3 に集約。通常代入の評価順と部分復旧は維持 | §3・§4.1 |
| §14.10.1 | payload 更新による事実の失効と、ハンドル差し替えを区別 | §4.2 |
| §15.7／§15.7.1–2 | Kimi.replace／exchange／swap を確定。= は別の評価順を持つ操作として維持 | §3 |
| §16.3.3／§16.4 | 内容破棄と最終解放を分離し、既存の再入制限を適用 | §4.2 |
| §18.3／§21.3.4 | 証明・射影・更新計画の記録と、依存変更時の再検証を追加 | §5.2 |
| §21.2.2–4／§21.3–5 | 通常の値借用 ABI と更新計画を共用し、意味を保つ最適化だけを許可 | §5.1 |
| §22.1 | Sealed と Kimi.replace／exchange／swap の Identity・契約を追加 | §2.1・§3.1 |
