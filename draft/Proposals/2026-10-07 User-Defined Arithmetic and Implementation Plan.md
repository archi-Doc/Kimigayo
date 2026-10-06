# 仕様変更・実装計画：ユーザー定義算術

日付：2026-10-07。状態：採用方針をまとめた提案。正式仕様への取り込み・実装・実行検証は未実施。

## 1. 位置づけと目的

**本書の変更事項は、[SPEC.md](../../docs/SPEC.md) およびその参照先より優先する。変更しない事項には既存仕様を適用する。** 今回作成するのは本書だけであり、正式仕様や実装は変更しない。取り込み時には変更事項を正式仕様へ統合し、正式仕様を本書に依存させない。

目的は、既存の演算子を明示的な Kimi Contract に結び付け、同型・異型の値の算術と組み込み数値が左辺の算術を扱うことである。記号や優先順位を増やさず、型・公開制約から一意な実装と結果型を決める。

- **一つの標準形：** 演算ごとの Contract と関連型 `Output` を使う。複合代入の独立した実装は設けない。
- **局所的な推論：** 演算を提供する側を先に固定し、反対側への探索、暗黙変換、期待結果からの逆引きをしない。
- **明示的な意味：** 入力の共有借用、結果の依存、評価順序、失敗、書き戻しを公開契約で定める。
- **検証可能性：** 選択した適合・取得・依存・効果を保持し、診断とコンパイラサービスが同じ意味情報を使う。

## 2. 仕様変更

### 2.1. 対象と定義権限

対象は二項 `+ - * / %`、単項 `-`、対応する複合代入 `+= -= *= /= %=` とする。各演算は独立した能力であり、全演算・比較・Copy をまとめて要求する `Number` Contract は導入しない。

本書では、組み込み整数12型、`f32`、`f64`、`Wrapping<I>`（`I is PrimitiveInteger`）を **組み込み数値型** と呼ぶ。これは既存の numeric Type の集合であり、新しい型名・制約名ではない。`bool`、`char`、ユーザーの `Decimal` や `BigInt` は含めない。

**ユーザー提供型**は、外側の Semantics が `owner` の通常の struct / enum とする。Kimi の通常の struct / enum も含むが、組み込み数値型などコンパイラが能力を閉じている型は含めない。外部登録は禁止し、適合はその型の宣言で行う。継承・条件付き適合・公開範囲・適合の経路一致は §8・§9 の既存規則に従う。

| 選択された左右の型 | 提供元 |
| --- | --- |
| 組み込み数値型同士 | 既存の組み込み演算。同型条件や演算の有無を維持する |
| 左辺がユーザー提供型 | 左辺の通常 Contract |
| 左辺が組み込み数値型、右辺がユーザー提供型 | 右辺の左オペランド用 Contract |
| 既存の raw ポインター算術 | §5.3 の既存規則だけを適用する |
| その他 | 演算を提供しない |

提供元を適合の有無で選び直さない。通常・左オペランド用の適合領域は重ならず、左右横断の重複検査や「反対側に適合がない」という証明を不要にする。

通常 Contract の右辺型は、外側が `owner` の完全な値型とする。数値・struct・enum に限定せず、Tuple なども指定でき、内部に借用を持ってよい。外側が借用・object・raw の型を直接の演算相手にはしない。`string` を直接のオペランドに含む算術は引き続き禁止する。安全な参照層を通した利用は §2.5 による。

これらは標準 Contract の**公開された適合条件**である。適合の定義時に、許される全束縛について条件を証明する。ジェネリックな相手型の外側 `owner` は、既知の型構造、pair の Semantics 制約、既存の能力から得られる事実などで証明し、`Owned` を代用しない。適合の利用者もこの条件を証拠として利用できる。条件不明の宣言を実体化まで保留しない。

`Vector` 側で `f64 * Vector` は定義できるが、別ライブラリの `Decimal * Vector` は追加できない。後者は `Decimal` 側の通常適合、ラッパー型、名前付き関数で表す。一般の右辺提供や型の組への外部登録は本書の対象外とする。

### 2.2. 標準 Contract と実装要件

以下の名前を Kimi の公開宣言として追加する。認識は宣言 Identity に基づき、同名のユーザー宣言やメソッドだけでは演算を許可しない。

| 演算 | 通常 Contract / 要件名 | 左オペランド用 Contract / 要件名 |
| --- | --- | --- |
| `+` | `Addable<Rhs>` / `added` | `LeftAddable<Lhs>` / `addedFrom` |
| `-` | `Subtractable<Rhs>` / `subtracted` | `LeftSubtractable<Lhs>` / `subtractedFrom` |
| `*` | `Multipliable<Rhs>` / `multiplied` | `LeftMultipliable<Lhs>` / `multipliedFrom` |
| `/` | `Dividable<Rhs>` / `divided` | `LeftDividable<Lhs>` / `dividedFrom` |
| `%` | `RemainderProvider<Rhs>` / `remainder` | `LeftRemainderProvider<Lhs>` / `remainderFrom` |
| 単項 `-` | `Negatable` / `negated` | なし |

全 Contract は通常の関連型 `associate Output` を一つ持つ。二項要件の形は次のとおりで、表の各要件名へ置き換える。§2.1 の適合条件はこれらの宣言 Identity に付随する公開条件であり、以下の構文に新しい制約名を省略しているわけではない。

```kimi
contract Multipliable<Rhs>
    associate Output
    func multiplied(self: ref/Self, right: ref/Rhs) -> Self.Output

contract LeftMultipliable<Lhs>
    associate Output
    func multipliedFrom(left: ref/Lhs, self: ref/Self) -> Self.Output

contract Negatable
    associate Output
    func negated(self: ref/Self) -> Self.Output
```

- 通常・単項の明示適合はユーザー提供型、左オペランド用は組み込み数値の `Lhs` とユーザー提供型の `Self` に限る。組み込み適合は §2.3 だけを供給する。
- 左オペランド用要件の意味は常に `left op self`。減算・除算・剰余でも順序を逆にせず、通常適合から自動生成しない。
- 両辺は固定の共有借用。Copy によって取得方式・実装を変えず、暗黙の Move・複製・排他借用を追加しない。
- 要件は safe、task slot なし、既定引数なし、通常の値結果とする。実装は既存の適合照合規則に従う。Place 結果や中断可能な要件は追加しない。
- `Output` は適合ごとの明示指定または公開された型同一性から一意に確定する。実装本体・戻り値シグネチャから推測しない。曖昧な関連型名は `T.(Multipliable<R>).Output` のように修飾する。
- `Output` は `Self` と異なってよく、通常の完全な型として `Result` や外部借用を含む値も許す。呼び出しごとの Origin を受け取る関連型族にはせず、`Copy`・`Owned` を一律に要求しない。
- 通常の公開効果を適用する。`confined` や `preserves results` を必須にせず、純粋性・割り当てなし・全域性を仮定しない。

これらは演算の提供能力であって、可換性・結合性・逆元・数学的な割り切れを保証する Contract ではない。型ごとの数学的意味、丸め、定義域、失敗条件は公開 API 契約で定める。

### 2.3. 組み込み適合

組み込み数値型 `N` に次の適合をコンパイラが供給する。二項適合はすべて `<N>`、`Output` は `N` である。異なる数値型間の適合、左オペランド用適合、数値変換は追加しない。

| `N` | 二項適合 | `Negatable` |
| --- | --- | --- |
| 符号付き整数 | `Addable`、`Subtractable`、`Multipliable`、`Dividable`、`RemainderProvider` | あり |
| 符号なし整数 | 同上 | なし |
| `Wrapping<I>` | 同上 | 符号にかかわらずあり |
| `f32` / `f64` | `Addable`、`Subtractable`、`Multipliable`、`Dividable` | あり |

計算結果と失敗条件は §13.3 の組み込み演算と同じとする。checked / wrapping、ゼロ除算、`MIN / -1` と `MIN % -1`、IEEE 754、再結合・融合の制限を維持する。

`T is PrimitiveInteger` から上表の整数共通の二項適合と `Output is T` を得られるようにする。`Negatable` は得られない。`Wrapping<T>` には二項適合、`Output is Wrapping<T>`、`Negatable` を供給する。これらは限定した組み込み証明規則であり、一般の候補探索・場合分けを追加しない。

`PrimitiveInteger` はリテラル・変換・位置などの保証も持つため、算術 Contract に置き換えない。`Wrapping<T>` への公開メンバーやコンストラクターも追加しない。組み込み witness は既存の比較適合と同様に保持し、通常のメンバー一覧へ同名メソッドを生成しない。

既存の実装プロファイルの制限は維持する。特に [IMPL §21.5.3](../../docs/impl/21-layout-runtime-and-code-generation.md#2153-checked-instructions-and-raw-pointers) の128-bit整数の除算・剰余は、対応する wrapping 型と新しい witness 経路にも同じ Unsupported 境界を適用する。言語上の適合は存在させ、制限を理由に適合を消さない。本書はこの既存の native 制限の解消を実装範囲に追加しない。

### 2.4. 型付けと選択

1. 演算子、オペランド自身の構文・型・公開制約から、選択対象の型を求める。参照層は §2.5、未確定の数値リテラルは下記による。
2. §2.1 の規則、または既に公開されている算術 Contract の証拠で提供元を固定し、対応する bound Contract を識別する。`T is Addable<R>` は通常方向を固定する証拠であり、未知の `T` を組み込み型・ユーザー型へ別途分類する必要はない。
3. 通常の適合条件・経路一致を検査し、一意な requirement-to-member mapping と `Output` を保持する。
4. 保持した要件に対して取得・Origin・Loan・効果を検査する。最後に結果を使用位置の期待型へ適合させる。

同名の自由関数、import した拡張、変換経路、反対側の提供元は探索しない。期待結果による候補削減・順位付け・相手型の推論もしない。取得や結果の適合に失敗しても選択を戻さない。既存の bound Contract の衝突規則を適用し、条件付き適合による実装の優先・置換は導入しない。

**リテラル。** 一方が既知のユーザー提供型、または公開された算術 Contract の証拠で提供元が固定され、他方が未確定の数値リテラルまたは literal-only expression の場合、その提供元が公開する該当演算の適合だけを調べる。通常方向では `Rhs`、左オペランド方向では `Lhs` に既存のリテラル適合を行う。

- 適合する相手型が一意なら採用する。複数なら曖昧性エラーとし、数値の幅・`i32` / `f64` のデフォルト・`Output` では順位を付けない。
- 候補のない相手型を数値型一覧から生成せず、ユーザー型への暗黙構築もしない。未証明の条件を「適合なし」として一意性を得ない。
- 整数リテラルは整数・wrapping 整数へ、浮動小数点リテラルは `f32` / `f64` へ、既存規則で適合する。整数から浮動小数点への暗黙適合は追加しない。
- 型付きオペランドは型を固定する。literal-only subtree は選ばれた数値型で既存の演算規則どおり評価し、式全体を数学的整数で計算して最後だけ丸めたり wrap したりしない。
- 両辺がリテラルだけの式や、組み込み数値だけの式は既存の期待型伝播・デフォルトを維持する。期待結果がユーザー型でも、その型の演算やコンストラクターを探索しない。

例えば `L is Addable<R>`、`R is PrimitiveInteger` で候補が一つなら、`left + 1` は既存の全整数型に適合するリテラル規則で成立する。`L is Addable<L>` だけでは、任意の `L` を `1` から構築できないため成立しない。

通常のネスト呼び出し・匿名関数には §10.5 の共有期待型と推論境界を適用する。提供元と全候補で一致する入力期待型が独立に定まる場合だけ共有できる。候補ごとの再解析や、未確定のオペランド型を `Output` から補う推論は行わない。

例えば `v * make()` は `v` から通常方向が決まり、全候補の入力型が `f64` なら、その型を `make()` に渡せる。一方、`make() * v` の左辺型が未確定なら、右側の適合だけで数値型へ誘導しない。左辺は別のユーザー提供型かもしれないため、`make<f64>()` のような型引数や注釈で先に確定させる。数値リテラルは元から数値であるため、前述の有限候補への適合が可能である。

`Never` は独立に確定した入力期待型へ既存規則で適合するが、相手型・提供方向・候補を選ぶ証拠にはしない。例えば `v + stop()`（`stop() -> Never`）でも候補を勝手に一つ選ばず、必要な型が決まらなければエラーとする。実行されないオペランド・演算も既存の検査継続規則で確認する。

### 2.5. 参照層とジェネリック

算術式は、定義時に型構造が分かる安全な値参照層（`ref`、`uniq`、適格な pair layer）を終端までたどって演算を選ぶ。各層の権限・Origin・Loan を保持し、未知の型パラメーターや関連型はその場の終端とする。既存の object・raw の境界を越えて payload を暗黙に取り出さない。

参照層を通して演算できることと、参照型自身の適合は別である。`Vec` の適合だけから `ref/Vec is Multipliable<f64>` は導出しない。ジェネリック関数は、例えば `value: ref/T` と `T is Multipliable<f64>` を使う。

ジェネリック本体は公開制約で一度検証し、提供方向・bound Contract・実装対応・`Output`・参照層・取得計画を固定する。実体化はその対応を具体化するだけで、追加の層をたどったり、別の提供方向・組み込み式へ選び直したりしない。左オペランド用適合の宣言は、固定数値型、`PrimitiveInteger`、それを引数とする `Wrapping<T>` などから数値領域を証明する。全数値型を一括する新しい制約は追加しない。

左右が同一数値型であることなど、演算全体が既存の組み込み規則に適合すると定義時に証明できる場合は、`PrimitiveInteger` / `Wrapping<T>` の従来経路を維持する。一方だけが組み込み数値であることでは、この経路を選ばない。例えば `S is PrimitiveInteger` と `V is LeftMultipliable<S>` の下の `s * v` は右辺提供である。算術 Contract の証拠だけで検証する式は Contract 経路に固定し、具体化後の最適化も定義時の取得の適法性を変えてはならない。

### 2.6. 評価・依存・失敗

通常の二項式は左辺を評価・取得してから右辺を評価・取得し、それぞれ一度だけ実行する。右辺提供でも同じ順序である。概念上は選択済み要件への非束縛呼び出しであり、`right.method(left)` への構文置換ではない。

**取得方式は選択した演算全体で決まる。** 組み込み数値演算は評価時点の値を Copy する従来の value read を維持する。ユーザー演算および共有借用の Contract 呼び出しは、数値側も含めて要件どおり共有借用する。左辺の検査 Loan は右辺の評価前に開始する。明示的な `@move`・`@copy` は通常どおり先に実行し、その一時値を借用できる。

結果は、この呼び出しで入力を検査するために新たに作った Loan に依存してはならない。入力型が元から持つ外部依存は保持してよい。通常の型・Origin・実装照合・結果 Loan 検証を適用し、固定された `Output` を理由に実際の依存検査を省略しない。

演算だけに必要な Loan は演算完了時に終了するが、既存 Loan、結果の外部依存、一時値とその破棄に必要な依存は通常の寿命を保つ。後続オペランドが前の入力を Move・置換・排他借用する場合も、通常の競合検査を行う。

失敗の暗黙伝播はない。`Output` が `Result` なら結果はその値であり、伝播には `try (a / b)` のように明示する。`try a / b` は既存の優先順位どおり `(try a) / b` である。Abort・途中転出・一時値の破棄は既存規則に従い、以前の効果を巻き戻さない。

組み込み演算またはその intrinsic witness の算術チェックは、演算子利用なら算術式、複合代入なら更新式の開始位置を報告する。ユーザー実装内部の演算・明示 Abort は自身の位置を保ち、外側の演算子位置へ一律に置き換えない。明示的な要件呼び出しは通常の呼び出し位置規則に従う。

### 2.7. 複合代入

`op=` は同じ `op` の選択規則を使い、独立した更新 Contract を設けない。結果は `()` とする。

1. 右辺を評価し、選択した演算に必要な形で取得する。
2. 更新対象を一度だけ特定し、必要な旧値を一度だけ取得する。
3. 選択済みの演算を一度実行し、結果を確保する。
4. 演算専用 Loan が終了した上で、既存の置換規則または setter によって一度書き込む。

これは `target = target op rhs` へのテキスト置換でも、排他 receiver の更新メソッド呼び出しでもない。receiver・添字は再評価せず、getter / setter を迂回しない。演算の結果は、旧値と同型とは限らず、選択された setter の入力へ既存の規則で適合すればよい。

- Non-Copy の旧値も、standard get が共有借用を許すならその場で読める。custom set があるだけでは拒否しない。暗黙の Move や空の格納への参照は作らない。
- computed get が返す値は通常の一時値として借用する。その一時値が receiver への依存を保持する場合、演算結果が独立でも setter と競合し得る。成立させるための早期破棄は追加しない。
- `x += x` は演算専用の共有 Loan だけなら許可する。別の生存中 Loan や破棄時依存が書き戻しと競合する場合は拒否する。
- 結果が置換で失われる旧内容に依存する更新は拒否する。参照変数の更新先を参照先へ変更せず、必要なら `r@follow += rhs` と書く。
- RHS・対象特定・演算・旧値破棄が正常完了しなければ、それ以降の段階を実行しない。通常のクリーンアップと Abort 規則を保ち、原子性・ロールバックは保証しない。

### 2.8. 変更しないこと

独自記号・優先順位・結合規則、単項 `+`、`++` / `--`、ビット演算・シフトのユーザー拡張は追加しない。`and` / `or` / `not`、`=`、`@`、`is`、範囲、制御移動、既存の比較 Contract も変更しない。

文字列連結は補間、既存のポインター算術は §5.3 のままとする。暗黙の数値変換・ユーザー変換、数値リテラルによるユーザー型構築、交換法則による左右生成、実行時 Contract dispatch、ユーザー演算の定数評価は導入しない。最適化による定数畳み込みは、定数式としての適法性を広げない。

## 3. 使用例と境界

次は本書を採用した場合のコードであり、現在の実装で実行できるという記録ではない。

```kimi
struct Vec2
    Self is Addable<Vec2>
    Self is Multipliable<f64>
    Self is LeftMultipliable<f64>
    associate Addable<Vec2>.Output is Vec2
    associate Multipliable<f64>.Output is Vec2
    associate LeftMultipliable<f64>.Output is Vec2

    public let x: f64
    public let y: f64

    public init(x: f64, y: f64)
        self.x = x
        self.y = y

    public func added(self: ref/Self, right: ref/Vec2) -> Vec2
        return Vec2.init(self.x + right.x, self.y + right.y)

    public func multiplied(self: ref/Self, right: ref/f64) -> Vec2
        return Vec2.init(self.x * right, self.y * right)

    public func multipliedFrom(left: ref/f64, self: ref/Self) -> Vec2
        return Vec2.init(left * self.x, left * self.y)

let a = Vec2.init(1.0, 2.0)
let b = a * 2.0
let c = 2.0 * a
var total = a + b
total += c
total += total // 入力を消費せず、独立した結果を書き戻す。

func sumPair<T>(left: ref/T, right: ref/T) -> T
    T is Addable<T>
    T.(Addable<T>).Output is T
    return left + right
```

`Vec2` に Copy 適合は不要である。`Time - Time -> Duration` や `Matrix * Vector -> Vector` も、それぞれの左辺型で対応する Contract と `Output` を指定する。

| ケース | 要求する結果 |
| --- | --- |
| `Vector<f32>` が `LeftMultipliable<f32>` だけを提供し、`2.0 * v` | リテラルを `f32` に適合 |
| 同じ型が `f32` と `f64` の両方を提供し、`2.0 * v` | 曖昧性エラー。`2.0@f32 * v` などで明示 |
| `f64` 相手だけの `2 * v` | 拒否。`2.0` または `2@f64` を使う |
| 組み込み整数の `n + bump(n@uniq)` | 左の値を先に取得。通常の他の条件が成立すれば許可 |
| ユーザー演算の `n * changeAndMakeVector(n@uniq)` | 左の共有 Loan と競合するため拒否 |
| 上記の左を `n@copy` に変更 | 独立した一時値を借用。通常の他の条件が成立すれば許可 |
| `r: ref/Vec2` に対する `r * 2.0` | 既知層をたどって `Vec2` の演算を使用 |
| `ref/Vec2 is Multipliable<f64>` という適合要求 | `Vec2` の適合からは成立しない |
| `holder.item += rhs`（standard get、custom set、Non-Copy 値） | 共有取得と結果・一時値の依存が setter を許せば成立 |
| receiver を借用する getter 一時値が setter 実行時も生存 | 通常の Loan 競合として拒否 |
| 演算の結果が `Result<T, E>`、setter 入力が `T` | 複合代入では暗黙に取り出さず、型不一致 |

## 4. 診断とコンパイラサービス

診断は [SPEC §23](../../docs/spec/23-compiler-services.md) と [診断開発手順](../../docs/dev/DIAGNOSTICS.md) に従う。

- 提供元なし・適合なしは、演算子、左右の終端型、必要な bound Contract を示す。右辺提供の領域外と、通常適合の不足を区別する。
- 不正な適合条件・不足要件・関連型・署名・重複は宣言位置で報告する。不正な適合を「存在しない」として別経路を採用しない。
- リテラルの曖昧性は候補の相手型と関連する適合位置を示す。型の明示を Advice とし、根拠なく一方を選ぶ自動修復は提示しない。
- Loan、Origin、setter 入力、効果の失敗は既存の原因別診断を使う。ユーザー演算の不足を一律に「数値でない」と説明せず、文字列補間の案内は既存の文字列禁止に対応させる。
- 実装途中の仕様上有効な形は Unsupported として、対応済み範囲・未実装条件を区別する。独立した問題を抑制で隠さない。

意味情報には演算種別、組み込み / Contract の経路、提供側、bound Contract・要件・実装 Identity、`Output`、オペランド取得順序・参照層・依存・効果、複合代入の書き戻しを保持する。参照元のソース snapshot と結び付け、CLI・LSP・CSP 用の出力で同じ情報を再利用する。表示のために再解決せず、CSP 未実装部分を対応済みとは報告しない。

## 5. 実装計画

### 5.1. 共通方針

[CODEMAP](../../docs/dev/CODEMAP.md) を入口に、既存の比較適合・bound Contract・引数取得・置換処理を再利用する。

- `Binding.Expressions.BindBinary` の同型数値前提を、オペランド型付け、提供元選択、取得、結果適合に分ける。最後に fallback を足すだけの実装にはしない。
- `Binding.Comparisons`、`Binding.RequirementInstances` と同様に選択済みの呼び出し計画を保持する。右辺提供は引数対応で表し、通常呼び出しの効果・所有権・生成経路へ渡す。
- `Binding.Indexers` の有限候補からのリテラル選択を参考にする。候補の試行では取得を実行せず、確定後に一度だけ実行する。
- 複合代入は「取得済み RHS と旧値から結果を作る」計画を共通化し、local・Field・Property・index・Place 結果の全経路を接続する。
- Kimi の公開宣言とライブラリによる算術は Kimigayo で記述する。組み込み計算は従来の intrinsic と直接的な数値生成を利用し、不要なボックス化・動的探索・ヒープ割り当てを加えない。
- interning、安定した ID、保持容量と scratch buffer を再利用する。型・適合・制約・依存の変更時には計画を無効化する。再利用によって旧 snapshot の意味を残さない。
- バッファ再利用が必要な利用者には名前付き更新メソッドを提供できる。独立した更新演算子は追加せず、最適化は評価・alias・破棄・Abort・setter の観測を保てる場合だけ行う。

### 5.2. 作業単位

以下はこの機能内の順序であり、既存のマイルストーン順序を変更したり、今回実装を開始したりする指示ではない。各単位は再現例、実装、独立した期待値を持つ回帰、正式検証で完結させる。

| 単位 | 作業と既存入口 | 完了条件 |
| --- | --- | --- |
| A0 仕様取り込み | §6 の文書対応。受け入れ例・拒否例を実装に先立って確定 | 正式仕様が自己完結し、未実装範囲が STATUS / PLAN に明記される |
| A1 Contract と組み込み適合 | `Library/`、`KimiLibrary*`、`Binding.Contracts*`、`Binding.BoundContracts`、`Binding.RequirementInstances` | 宣言 Identity、適合領域、全演算の数値行列、`Output`、条件付き適合・継承・衝突・公開条件を検証 |
| A2 通常・単項演算 | `Binding.Expressions`、通常 call / ownership / emission、比較の保持済み計画 | 同型・異型・異なる結果型、Non-Copy、参照層、ジェネリックの対応固定、Origin・効果が直接・汎用経路で成立 |
| A3 左オペランドとリテラル | 左オペランド用 requirement、引数対応、`Binding.Indexers` を参考にした候補適合 | 定義側が一意、左から一度ずつ取得、非可換演算、数値領域の証明、literal-only・型付き・曖昧性・推論境界を検証 |
| A4 複合代入 | `OwnershipAnalysis.Values.ComputeUpdate`、`SupportsUpdate`、各更新経路 | RHS 先行、各 selector / get / set が1回、自己参照、Non-Copy 一時値、破棄・途中転出、依存による拒否を検証 |
| A5 統合と費用 | effect summary、再解析、CLI / LSP、ライブラリ文書、専用プログラム | O0/O2、公開出力、既存回帰、割り当て・容量再利用、固定条件の計測、最終 Session が完了 |

全単位で新しい診断経路を確認し、A5 まで先送りしない。既存の関連テストは `ComparisonContractTest`、`ComparisonCompilationCostTest`、`AssociatedContractParameterTest`、`CallAdaptationTest`、`ReferenceLayerAdaptationTest`、`WrappingIntegerTest`、`ElementUpdateEmissionTest`、`ContentUpdateTest`、`IndexableContractTest` を入口に選ぶ。新しい回帰クラス名と専用プログラムの番号は実装着手時に割り当てる。

### 5.3. 検証と証拠

§3 のケースに加え、組み込み適合の全表、除算・剰余の境界、禁止される再結合、左右が異なる意味の減算・除算、条件付き適合の Unknown、期待結果で候補を選ばないこと、外部借用を保持する結果、object / raw / string の境界、import 順序、適合変更後の無効化を検証する。

診断は正例対照と独立に書いた公開出力の期待を持ち、primary range、Reason、関連位置、Advice / 修復条件、独立エラー、代表的な CLI・JSON・LSP 出力を目視確認する。テスト成功だけで説明の明瞭さを完了扱いにしない。

実装時の手順は [VERIFICATION](../../docs/dev/VERIFICATION.md) に従う。

- 編集中はテストプロジェクトの増分ビルドと選択メソッドで feedback を得る。
- 各完成単位は `./scripts/verify.ps1 -Class <関連クラス> -Fixtures '<関連パターン>'` による Release 非増分ビルド、関連の機能・割り当て回帰、native O0/O2 を通す。
- 専用プログラムは実装着手時に番号を割り当て、原本ソースを O0/O2 で各1回ビルドし、それぞれの生成物を直接1回実行する harness を完了条件に含める。feature / rejection fixture とは別に記録する。
- セッション末に `./scripts/verify.ps1 -Mode Session` を1回実行する。検証中はソースを変更せず、NativeAOT は実行しない。
- 計算結果・取得・破棄の期待は最適化前後で一致させる。再 Binding・所有権解析・生成の割り当てと保持容量を固定 workload で確認し、タイミング計測は `src/Benchmark` で別途実行する。追加 warm-up や assertion 緩和で失敗を消さない。
- 成功・失敗の証拠を `artifacts/verify/`、計測を `artifacts/benchmarks/` に保存する。検証済み単位だけを commit し、`verify-commit.ps1` で証拠を関連付けてから現在の branch を origin へ push する。

今回の文書作成は draft のみの変更である。文面・リンク・Markdown・差分を検査し、コンパイラの対応追加や実行検証の完了とは扱わない。速度向上・割り当て削減は実測するまで主張しない。

## 6. 正式仕様への取り込みと完了条件

**以下は将来の取り込み時に行う。今回これらのファイルは変更しない。**

| 本書の項目 | 主な取り込み先 |
| --- | --- |
| §2.1–2.3 定義権限・Contract・組み込み適合 | SPEC §3.1.1.1、§8.4・§8.7、§22.1、LIBRARY |
| §2.4–2.5 選択・リテラル・参照・汎用検証 | SPEC §3.4.1・§3.5.3、§8.10、§10、§12.3.1 |
| §2.6 評価・依存・失敗 | SPEC §7 の呼び出し規則との接続、§13、§15、§17、§22.5 |
| §2.7–2.8 複合代入・範囲 | SPEC §13.2–13.4・§13.7–13.8、必要な Property 例、付録 D/E/F |
| §3–4 例・診断・意味情報 | spec の例、SPEC §23、dev/DIAGNOSTICS、検証要件 |
| §5 実装と検証 | dev/CODEMAP、dev/PLAN、dev/PLAN_HISTORY、STATUS、IMPL と検証要件 |

算術を一律に同型数値へ限定する記述、ユーザー算術を保留する記述、組み込み適合一覧、Non-Copy の複合代入を無条件に拒否する例を整合させる。SPEC の索引と STYLE の新しい公開名も更新する。正式文書の更新は英語で記述する。

[SETTLED](../../docs/SETTLED.md) の文字列連結・所有権を隠す accessor などの非採用理由は維持する。本書はその判断を撤回しない。

取り込み時には [INTEGRATED](../INTEGRATED.md) に本書の項目と取り込み先を記録する。全項目の扱いが確定した時点で `draft/Changes` へ移動して凍結する。仕様の取り込み完了と実装完了は別であり、部分実装に合わせて本書の有効な形を禁止へ変更しない。

実装完了は、既存プロファイルが許す範囲で全対象演算と定義側、直接・ジェネリック・参照・複合代入の経路が本書どおり検証され、公開診断の品質確認、必要な Unit / Session、費用確認、文書更新と証拠の保存が終わった状態とする。§2.3 の既存 native 制限は明記して保持する。それ以外に本書の実装範囲で条件が残れば、未実装境界と次の作業を記録し、機能全体を完了扱いにしない。
