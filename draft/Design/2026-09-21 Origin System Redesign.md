# Origin system — 最終設計仕様

- 日付: 2026-09-21
- 状態: 再設計の最終仕様。正式仕様への統合と実装は別工程であり、例示構文の実装済みを意味しない。
- 優先順位: 再設計する事項は本書を優先する。それ以外の型・所有権・アクセス・評価順などは [SPEC.md](../../SPEC.md) とその参照先を維持する。
- 対象: Origin の宣言・命名・射影・関係・省略と、完全な型・Loan への接続。既存の関数 Origin 省略規則は維持する。

本体を省いたコード例は契約の説明である。別々の例にある同名宣言は、同時に定義する意味ではない。

## 1. 目的と基本概念

### 1.1. 設計原則

Origin system は、借用の lifetime dependency（有効期間の依存関係）を表す。依存関係を正しく検証しながら、記法とコンパイラーの推論によって記述の負担を減らす。

| 原則 | 記述の方針 |
| --- | --- |
| **Infer when possible.** | 推論・省略規則で決まる Origin は書かない。 |
| **Relate when necessary.** | 必要な関係を `origin` 節で直接書く。 |
| **Name only when useful.** | 依存を参照するときだけ Origin や binding set に名前を付ける。 |
| **Declare when needed.** | 直接の借用注釈から導出できない依存、複数のスロット、固定したい公開スキーマを型ヘッダーで宣言する。 |

関数の Origin parameter list と型への Origin mapping は使用しない。意味上必要な量化・束縛・推論変数は、コンパイラーが保持する。

### 1.2. 四つの概念

| 概念 | 意味 | 例 |
| --- | --- | --- |
| Origin | 借用の有効性を保証するプログラム上の範囲 | `source`、`value`、`static` |
| Origin binding set | 型スキーマの各スロットと、その出現箇所で束縛された Origin の対応表 | `View<T>{v}` の `v` |
| Origin projection | set または値の型からスロットを選ぶ操作 | `v.source`、`view.source` |
| Origin relation | Origin 間の等値・包含関係 | `origin a outlives b` |

スキーマは型が公開するスロットの定義、binding set はその具体的な束縛である。set ではない単一の Origin を scalar Origin と呼ぶ。

```text
型スキーマ: Pair の left / right
  └─ 型の出現箇所: Pair<A, B>{p}
       ├─ p.left  → Origin α
       └─ p.right → Origin β
                    └─ == / outlives / and で関係を表す

Loan は別に保持する
  └─ 借用元の Place、共有・排他の権限、保護期間、Reborrow の関係
```

同じ Origin でも、同じ Place や Loan とは限らない。Origin の等値化・短縮は Loan を統合せず、借用権限も与えない。

## 2. 表記と意味

### 2.1. 三種類の波括弧

| 位置 | 表記 | 意味 |
| --- | --- | --- |
| 借用の `/` の前 | `ref{source}/T` | その借用層の Origin を指定する。 |
| 型の出現箇所 | `View<T>{v}` | その出現箇所の binding set に名前を付ける。 |
| struct・enum のヘッダー | `struct View<T> {source}` | その型自身のスロットを宣言する。 |

役割は構文位置で確定し、名前解決の失敗や空白の有無で解釈を変えない。名前に続く `{...}` の直後に `/` があれば、Semantics の借用注釈として解析する。括弧で囲んだ完全な型そのものに借用注釈は付けない。

借用注釈は一つの Origin 式、set の命名は一つの単純名を取る。それぞれ末尾カンマを許すが、空の `{}` は型ヘッダーだけで許す。`_` は Origin の宣言・命名・推論指定には使わない。

借用注釈を使えるのは `ref`、`uniq`、`objref`、`objuniq` と、安全な借用であることが証明された Semantics パラメーターである。`owner`、`obj`、`rc`、`arc`、`unsafe` には付けられない。

### 2.2. Origin 式と relation

Origin 式には、単純名、値・set の射影、`static`、`and` を使う。一節に一つの relation を書き、複数の節はすべて満たす。relation は次の二種類とする。

```kimi
origin a == b
origin a outlives b
```

```text
a outlives b       ⇔ region(a) ⊇ region(b)
a == b             ⇔ a outlives b かつ b outlives a
region(a and b)     = region(a) ∩ region(b)
```

`outlives` は等しい範囲を含み、反射性・推移性を持つ。`static` は最大の Origin。`and` は交換・結合・冪等則を満たし、`a outlives b` を証明できれば `a and b` を `b` に簡約できる。

`and` は両辺で使用できる。`a and b outlives c` は `a outlives c` と `b outlives c` に分解できるが、`x outlives a and b` は共通部分を包含する条件であり、二つの包含条件の連言にも選言にも置き換えられない。

relation は名前を宣言せず、未知名はエラーとする。連鎖比較、`or`、否定、実行時判定は追加しない。`origin` と `outlives` はこの構文での文脈キーワードとする。

### 2.3. 値名と射影

Origin を要求する文脈で、直接借用の引数・receiver・ローカルの名前を使うと、その値の**最外層の借用 Origin**を表す。変数の格納場所の寿命ではない。

```kimi
func first<T>(value?: ref/T) -> ref{value}/T
    return value

func get<T>(view?: ref/View<T>) -> ref{view.source}/T
    return view.value
```

| 表記 | 意味 |
| --- | --- |
| `view`（`ref/View<T>` の値） | View 自体への外側の借用 Origin |
| `view.source` | View が保持する参照の Origin |
| `ref{borrow}/View<T>{v}` | 外側は `borrow`、内部スロットは `v.source` |

所有値 `view: View<T>` には外側の借用 Origin がないため、`ref{view}/T` はエラーとなる。内部依存は `view.source` と書く。

値からの射影は、別名・冗長な `owner` を正規化し、連続する安全な借用層を取り除いた対象のスキーマを見る。フィールド・型引数・raw pointer の内部を探索しない。例えば `ref/(ref{inner}/View<T>)` の値からも `source` を射影できる。

射影は型情報の参照であり、実行時の dereference や getter 呼び出しではない。未初期化値の使用や借用獲得も許可しない。未知のスロット、空のスキーマへの命名、定義時にスキーマを確認できない `T{v}` はエラーとする。

### 2.4. binding set の命名

`View<T>{v}` の `v` は単一の Origin ではない。`ref{v}/T` はエラーで、`ref{v.source}/T` のように射影する。

```kimi
func identity<T>(value?: View<T>) -> View<T>{r}
    origin r.source == value.source
    return value
```

命名だけでは型・依存・変換・量化を追加しない。有効かつ未参照の set 名の追加・削除や、全参照と一貫した改名は契約を変えない。完全な型への命名は、既存の束縛を保持する。

`Type{v}` は常に命名であり、二度目を既存 set の適用とは解釈しない。set 全体の `==`、位置による Origin 適用、`{source => value}` は使わず、関係は各スロットについて書く。値名から射影できる場合は値名を使い、結果・入れ子など参照名が必要な位置に set 名を付けることを推奨する。

## 3. 型のスキーマと保存契約

### 3.1. 明示ヘッダーと暗黙スロット

struct・enum のヘッダーの有無で、その型自身のスロットの導入方法を決める。

| ヘッダー | 自身のスロット |
| --- | --- |
| 省略 | 保存型に直接書かれた借用注釈から、最大一つを暗黙導入する。 |
| `{source}` | `source` だけ。追加の暗黙導入は禁止。 |
| `{left, right}` | 列挙したスロットだけ。複数なら明示する。 |
| `{}` | 自身のスロットはゼロ。追加の暗黙導入は禁止。 |

ヘッダーありのスキーマを「閉じたスキーマ」と呼ぶ。ヘッダーは単純名だけを列挙し、`static`・射影・`and`・bound は書かない。非空ヘッダーでは末尾カンマを許す。名前の重複と継承した Origin の再宣言はエラー。スロット間の条件は `origin` 節に書く。

```kimi
struct View<T>
    public let value: ref{source}/T

struct Pair<A, B> {left, right}
    public let first: ref{left}/A
    public let second: ref{right}/B
```

`View<T>` も `{source}` を明示してよい。ヘッダー省略時は、型全体の保存フィールド・enum payload・基底型の借用注釈に現れる単純名から候補を収集する。同名は一つと数え、異なる名前が二つ以上あれば型全体をエラーとする。出現順では決めない。

```kimi
struct A
    let a: ref{source}/i32
    let b: ref{source}/i32 // OK: 一つのスロット。

struct Typo
    let a: ref{source}/i32
    let b: ref{souce}/i32 // エラー: 誤記か、明示ヘッダーが必要。
```

型引数の中に**ソース上で直接書いた**借用注釈も収集する。一方、完全な型引数が既に保持する内部依存、継承済みの依存、`static`、set 名は個数に含めない。入れ子の型宣言・callable は別の導入領域であり、型の個数制限を関数や匿名入力 Origin に適用しない。一箇所だけの誤記も防ぎたい場合は、ヘッダーで公開名を固定する。

### 3.2. 継承と部分宣言

ヘッダーなしの型の保存型では、借用注釈の単純名を自身の候補として扱い、可視な継承 Origin と衝突したらエラーにする。外側の参照へ黙って切り替えない。継承 Origin を直接参照する型は閉じたヘッダーを持ち、自身の追加スロットがなければ `{}` を使う。

```kimi
struct Outer<T> {source}
    struct Inner {}
        let value: ref{source}/T // 外側の source を参照。
```

空のヘッダーでも、継承した Origin や完全な型引数の依存は消えない。group・contract は自身の Origin ヘッダーを持たず、既存の外側環境を継承する。

分割された struct は、全フラグメントに同じスロット数・順序・名前の閉じたヘッダーを要求する。ゼロ個でも `{}` を書く。型レベルの `origin` 節は他の型制約と同じ一意の制約定義領域に置き、他のフラグメントはそれを共有する。group の部分宣言にはヘッダーを要求しない。

### 3.3. 入れ子の保存型

保存型の未束縛スロットは、公開 Origin、`static`、完全な型引数、明示注釈・relation で完成させる。フィールド初期化式やコンストラクターの代入から、保存契約を逆算しない。

```kimi
struct Wrapper<T> {source}
    let value: View<T>{inner}
        origin inner.source == source

struct Incomplete<T>
    let value: View<T> // エラー: 保存する source が未指定。
```

`Wrapper` の公開スロットは `source`。`inner` はフィールドと付属節だけの局所名で、型の先頭の節・別フィールド・メソッドでは使えない。入れ子の依存を平坦化したり、隠れた自由 Origin を追加したりしない。

フィールドの節は保存型を束縛し、残る条件を包含する型の公開前提・整形式条件から証明する。追加の公開前提が必要なら型の制約領域に明示し、フィールドの条件を自動的に昇格させない。

```kimi
struct Bounded<T> {a, b}
    origin a outlives b

    let value: View<T>{inner}
        origin inner.source == a
        origin inner.source outlives b // 公開前提から証明する。
```

同じ規則を enum の全 payload に適用する。選択中の Case や実行時の空状態を理由に、型の依存を削除しない。

### 3.4. 完全な型の依存

完全な型とは、必要な Origin の束縛・量化を含む契約が確定した型をいう。Origin が具体的な領域に決まっている必要はない。

binding set は対象宣言の有効なスキーマを扱い、字句的に継承したスロットを含む。型引数・フィールド・基底型の内部依存は、それぞれの完全な型に保持し、外側へ名前だけで平坦化しない。

```kimi
struct Box<T>
    let value: T

func inspect<T>(value?: Box<Pair<T, T>{p}>)
    origin p.left == p.right
```

`Box` に `left`・`right` は合成されないが、`T` の依存は失われない。`Owned` は既存の依存閉包を検査し、外側の借用・型引数・保存値・phantom スロット・捕捉などを含める。空のスキーマや型引数が未使用であることだけを根拠に `Owned` を推定しない。

## 4. 宣言への付属と名前解決

### 4.1. `origin` 節の所属

`origin` 節は対象の宣言に一段インデントして付属させる。関数・型では先頭の制約領域に、フィールドなどでは accessor 等より前に置く。付属先を構文で決め、そこで名前の可視性を検査する。

| 所属先 | 意味 |
| --- | --- |
| 関数・型の公開契約 | 利用側の前提として公開し、許容されるすべての束縛について定義を検査する。 |
| フィールド・enum Case・関連型指定 | 型を束縛し、残る義務を包含する公開契約から証明する。 |
| 基底型 | 型ヘッダーの一部として束縛し、追加の公開前提は型の制約領域に書く。 |
| ローカル・固定された Container alias | 既存の証拠・初期化式と合わせて検査する。未知の事実を仮定しない。 |

参照名に合わせて節の所属を移動する規則は設けない。宣言のパラメーターに依存しない閉じた条件は定義時に証明し、矛盾する条件を前提にして不正な定義を受理しない。実行文の途中や任意の式への制約ブロックは追加しない。

### 4.2. 名前の導入とスコープ

| 場所 | 導入する名前と有効範囲 |
| --- | --- |
| 通常関数・コンストラクター・明示 accessor・Contract 要求のシグネチャ | 借用注釈中の未束縛の単純名を、その callable の scalar Origin とする。シグネチャ・制約・本体で有効。 |
| 型ヘッダー／ヘッダーなしの保存型 | §3 の型スロット。型定義内で有効。 |
| シグネチャ中の `Type{set}` | 所属する callable のシグネチャ・制約・本体で有効。 |
| フィールド・enum Case・関連型指定の `Type{set}` | その宣言と付属節だけで有効。 |
| 基底型の `Type{set}` | 型の制約領域で有効。メソッドへ公開する別スロットにはならない。 |
| ローカルの `Type{set}` | 宣言の型・付属節と、宣言後の通常のローカルスコープで有効。 |
| Container alias の `Type{set}` | 付属節と、その SourceDocument の alias スコープで有効。 |

relation・ローカルの借用注釈は既存名を参照し、新しい scalar Origin を宣言しない。特殊化や保存 accessor など元の契約を継承する位置にも、新しいパラメーターを追加しない。

Origin の名前解決は内側から字句スコープを検索し、最初に候補があるスコープで確定する。同じスコープで値名・Origin 名・set 名が競合したらエラー。役割が違う候補でも外側へ検索し直さず、未知名として導入し直さない。フィールドの単純名は receiver なしの値 Origin 候補にしない。型パラメーターの名前空間は既存どおり別に扱う。

新しい Origin／set 名は、可視な Origin／set 名や Origin 文脈で競合する引数・ローカル名を隠せない。set 名の二重宣言も禁止する。別フィールドなど互いに見えない局所名は再利用でき、通常の値名同士のシャドーイングは既存規則を維持する。

シグネチャと型スキーマは名前を収集してから解決し、引数・フィールド・ファイルの列挙順に依存させない。フィールド局所名の型全体への持ち上げや、実行時の前方参照・関数境界を越えた値捕捉は行わない。メソッドは可視な外側 Origin を参照できるため、外側スキーマを変更したらその束縛も再検証する。

## 5. 関数契約の完成と省略

### 5.1. 全称契約と完成の順序

関数の abstract Origin は全称量化される。定義は公開 relation・整形式条件を満たす任意の束縛で成立し、呼び出し側はその契約を満たす束縛を推論する。関数本体が新しい寿命を生成し、隠して返す存在量化は追加しない。

```kimi
func nested<T>(x?: ref/(ref{s}/T), y?: ref{s}/T)
// s は関数の abstract Origin。x の外側は独立した入力 Origin。

func constant() -> ref{s}/i32
// 結果だけの s も全称。ローカル変数への参照は返せない。
```

```text
宣言・型の出現箇所を収集
  → 継承済み契約・完全な型・公開スキーマを確認
  → 名前と射影を解決し、明示注釈・relation を正規化
  → 残る省略位置を位置別の規則で補完
  → 完全な契約を保存
  → 本体を検証／使用時に束縛・型・Loan を検証
```

### 5.2. 明示した結果関係と省略の区別

通常 callable の集約型結果スロットは、宣言時に次の順で完成させる。

1. 完全な型・継承済み契約・明示注釈・等値置換で既存 Origin 式に決まった位置は、その束縛を保持する。
2. **明示した `origin` 節**を正規化した後、非自明な outlives 関係に残る未束縛の結果スロットを、その関係の下で全称化する。上限・下限・複合式のいずれも対象とする。
3. それ以外は既存の結果省略規則で補完する。未束縛の結果スロット同士だけの等値は一つの未決定の同値類とし、各位置の既定値が一致しなければ拒否する。

型から自動的に生じる整形式条件だけを理由に、結果の省略位置を全称化しない。その条件は、完成した型の妥当性として別途検査する。`{set}` の命名だけでも省略は止まらない。

正規化は等値・相互 outlives の統合と限定的な簡約による。既存 Origin 式への束縛が決まった事実は、等値の表記を消しても保持する。単純な置換にできない等値は両方向の outlives として残し、循環する式を無限展開しない。

`static outlives x`、`x outlives x`、`x == x` などの自明な条件は省略・量化を変えない。検査対象の条件自身を前提として自明と判定せず、簡約は宣言順によらず決定的に行う。一般の意味的同値判定は要求しない。

```kimi
func shorten<T>(value?: ref/T) -> View<T>{r}
    origin value outlives r.source
// 入力に支えられる任意の結果 Origin に対応する。

struct Marker {source}

func makeMarker() -> Marker{r}
    origin static outlives r.source
// 自明な節なので、節なしと同じ省略結果: source は static。
```

直接の `ref{s}/T` による scalar Origin の導入は明示契約であり、この集約型の省略判定とは別である。無制約の集約型結果スロットだけを全称化する専用構文は設けない。

契約の完成は定義時に一度行う。使用時の置換で条件が自明になっても、量化や省略をやり直さない。既に束縛された型への relation は検査であり、再束縛ではない。ローカル・保存型・入れ子の callable は、それぞれの完成規則に従い、この結果全称化の対象にしない。

### 5.3. 位置ごとの省略規則

既存の [Origin 省略規則](../../spec/15-ownership-and-lifetime-analysis.md#154-origin-elision-and-return-contracts) を次のように適用する。

| 位置 | 省略の扱い |
| --- | --- |
| 直接借用の入力・receiver | 最外層の借用ごとに独立した入力 Origin。 |
| 通常 callable の入力にある集約型 | 未束縛スロットごとに独立した全称 Origin。 |
| 入れ子の借用層 | 新しい省略許可は与えず、既存の注釈規則を維持する。 |
| 戻り値 | §5.4 の規則。名前付き関数が結果型全体を省略した場合は Unit。 |
| ローカル | 初期化式から推論。初期化式なしなら完全な明示情報が必要（§6.1）。 |
| インスタンス保存型・enum payload | 保存契約を明示して完成させる（§3.3）。 |
| static storage | `Owned` と既存の静的な借用元の規則を要求。省略した共有借用と Loan 要求 `none`／`ref` のスロットは `static`。排他的借用・`uniq` 要求は拒否。 |
| accessor・特殊化 | 保存契約・元の契約を先に継承し、残りだけを補完する。getter も実際の receiver に従う。 |
| Adaptation Target | 被演算子・操作・制約から推論する。関数の結果省略は使わない。 |

入力の規則は型引数・Tuple・配列要素・借用対象の型へ再帰するが、フィールドや完全な型を展開し直さず、別の Function Type／Callable 境界も越えない。異なる入力・型の出現箇所・スロットは独立している。

Semantics/type pair の元の `s/T` は完全な型を保持する。別の `s/U` への再構成では、安全な借用の場合だけ外側 Origin が有効となる既存の条件付き規則を維持する。

### 5.4. 戻り値の省略

明示・継承されていない借用 Origin と集約型スロットを、次の規則で補完する。

1. 直接借用の入力（借用 receiver を含む）があれば、その**最外層の Origin すべての交差**を使う。集約型内部の Origin は候補に加えない。
2. 直接借用入力がなければ、共有借用は `static`。排他的借用には明示契約が必要。
3. 同じく直接借用入力がない集約型スロットは、全入力型が既存の前提から `Owned` と証明でき、スロットの Loan 要求が `none`／`ref` の場合だけ `static`。入力ゼロもこの条件を満たす。それ以外は明示契約が必要。

```kimi
func first<T>(x?: ref/T) -> ref/T                  // x
func choose<T>(x?: ref/T, y?: ref/T) -> ref/T      // x and y
func view<T>(x?: ref/T) -> View<T>                 // source は x
func identity<T>(x?: View<T>) -> View<T>           // エラー: 結果関係が必要
```

条件付きの借用入力は、許容される Semantics ごとに検査する。結果省略のために `Owned` 制約を追加せず、名前付き関数の本体から契約を推測しない。Origin が適合しても、実際の Loan と型の整形式条件は満たす必要がある。

### 5.5. 入れ子の Function Type／Callable

入れ子のシグネチャでは、新しい名前付き scalar Origin を暗黙導入しない。既存の直接借用入力の per-call 量化（呼び出しごとの全称 Origin）と、そのシグネチャ自身の結果省略は維持する。

内部の集約型への `{set}` は、それを含む宣言の命名として許可する。内部の**入力**スロットは完全な型、または外側の既存 Origin 式への等値束縛などで固定し、自由な per-call スロットにしない。outlives の上限だけでは完成しない。

```kimi
func useView<T>(x?: View<T>, callback?: (View<T>{c}) -> ())
    origin c.source == x.source

func invalid<T>(callback?: (ref{s}/T) -> ref{s}/T)
// エラー: s が外側で束縛されていない。
```

`c.source` は外側の呼び出しで決まった `x.source` に固定され、callback 呼び出しごとに選び直さない。Callable で set 命名を許可しても、直接借用層への書かれた Origin 注釈を新たに許可するものではない。

内部の**結果**は、固定束縛を反映してから内部シグネチャの省略規則に従う。例えば `(ref/T) -> View<T>` は per-call 入力の Origin を結果へ引き継げる。未参照の set 名を付けても同じだが、その per-call Origin を外側へ射影・捕捉することはできない。外側の節で関係を指定するスロットは、外側の既存 Origin 式へ固定する。

この境界を入れ子の各段で守る。匿名関数は既存の期待型・捕捉・結果推論に従い、新しい名前付き Origin パラメーターは導入しない。

## 6. ローカルと独立した型式

### 6.1. ローカルの推論と完全注釈

`makeView` が入力に依存する既知の関数であれば、次はいずれも推論できる。ここでは `value` を可視な直接借用の値とする。

```kimi
let first = makeView(value)
let second: View<T> = makeView(value)
let third: View<T>{v} = makeView(value)

let view: View<T> = makeView(value)
    origin view.source == value
```

付属節は初期化式を検査する前に型注釈と合わせて収集する。節内の `view.source` は宣言先の型だけを参照し、未初期化値を読まない。初期化式内の名前解決は通常どおりで、宣言先の値を前方参照させない。

推論には許される短縮を含む。宣言で固定した型と Origin 制約に初期化式・後続の使用・代入を適合させ、後の代入で契約を作り直さない。非 generic の省略は本体の Origin／Loan 解析完了までに、延期が許された generic の義務も既存の確定期限までに解決する。未解決なら注釈を要求する。

初期化式がなくても、全スロットが既存 Origin 式に決まる完全な注釈なら許可する。

```kimi
var pending: View<T>{p}
    origin p.source == value
```

等値置換や既存の等値証明を使えるが、outlives の上限だけでは不十分である。後の代入による穴埋め、新しい全称 Origin、勝手な `static` の補充は行わない。変数自体の初期化前の使用は引き続き禁止する。

### 6.2. 基底型・関連型・alias

独立した型式にも、その型式を含む宣言の節を使う。次の `Base` は適切な open struct、`Source.Element` は既存の関連型要求とする。

```kimi
struct Derived<T> {source}: Base<T>{baseOrigins}
    origin baseOrigins.source == source

associate Source.Element is View<T>{elementOrigins}
    origin elementOrigins.source == source
```

関連型の役割、継承のアクセス・構築・レイアウトなど、Origin 以外の成立条件は維持する。

alias は既存の Container alias に限る。例えば `Family.Helpers` が外側の `source` を継承する group なら、次のように固定する。

```kimi
alias StaticHelpers => Family.Helpers{familyOrigins}
    origin familyOrigins.source == static
```

一般的な型 alias・generic alias は追加しない。既存のルート検索・import・アクセス規則を維持し、alias から実行時値や別ファイルの束縛は参照しない。

### 6.3. 式中の型式

明示型引数などに relation が必要なら、中間宣言の節を使う。

```kimi
let result = consume<View<T>{argumentOrigins}>(view)
    origin argumentOrigins.source == view.source
```

ローカルの初期化式に直接書かれた型式の set 名も、そのローカル宣言に所属する。型引数・構築型・Adaptation Target を含め、入れ子の関数・宣言には入らず、推論された型から名前を発見しない。型式内の Function Type／Callable には §5.5 を適用する。

付属節なしの式では既存の文脈から推論できる範囲で使用する。関数先頭の契約節から本体中の型式を参照して契約を作らない。中間修飾子は `(Outer<T>{o}).Inner<U>` の括弧付き経路構文を使い、最終型に残らない依存もその修飾子で検証する。

## 7. 型適合・推論・借用の安全性

### 7.1. 型適合と限定された推論

relation は値の変換ではない。型の変性から制約を集め、Origin の関係と合わせて解き、候補をすべての型・Origin・Loan 制約で検査する。

| 位置 | 推論の規則 |
| --- | --- |
| 共変 | 等値で固定されていない場合、上限の交差を候補とする。複数の短い解があっても、最長の共通範囲を主解とする。 |
| 不変 | Origin の等値を証明する。交差への置換で緩めない。 |
| 反変 | 制約の向きを逆にする。一つの下限が他のすべてを包含すると証明できる場合に採用し、比較不能な下限の和集合は作らない。 |
| 混在・循環 | 等値と包含を合わせ、表現可能で、証明済み等値を除いて一意な主解を要求する。 |

主解は変性と型適合の下で最も一般的な許容解である。既存の限定 solver は等値置換・反射性・推移性・meet の規則を使い、任意の領域の列挙や一般の定理証明は行わない。表現できない主解や証明不足は、既存の確定期限までに解決できなければ拒否する。証明不足と矛盾は区別して診断する。

複合式の条件は正規化した式のまま保持し、同じ前提や導出できる関係を使える。右辺の `and` を原子的な辺へ誤って分解しない。

`ref` と `uniq` の外側 Origin は共変だが、`uniq/T` の `T` は不変である。共変な入力を共通範囲へ短縮して仮引数の等値を満たしても、元の寿命・借用元・Loan が同一になるわけではない。入力同士の等値も、`static` 条件などを伝えることがあるため、一律に冗長と扱わない。

### 7.2. 整形式条件と Loan

完全な型は内部依存と整形式条件を保持する。例えば `ref{borrow}/View<T>{v}` には、内部依存が外側の借用を支える `v.source outlives borrow` が必要である。こうした型固有の条件は定義の前提・使用時の証明義務となるが、アクセス権は与えない。

スロットの Loan 要求は既存の `none < ref < uniq` に従い、複数の用途には強い方を取り、入れ子の型へ伝播する。実際の Loan・Place・権限・Reborrow 関係は、値の獲得・保存・返却・破棄まで保持する。共有借用から排他権限を作らず、必要な期間の破棄・再配置・競合する書き換えを拒否する。

`static` の共有借用も、実際の借用元の初期化・不変性などを満たす必要がある。安全なコードで寿命の長さだけから `uniq{static}/T` を作ったり、`uniq` 要求のスロットを `static` に束縛したりできない。Loan 要求は Copy 能力を自動付与せず、既存の構造的な Copy 判定と使用時の競合検査を維持する。

### 7.3. Phantom Origin

安全な参照フィールドがなくても、型ヘッダーで依存を宣言できる。

```kimi
struct RawView<T> {source}
    let pointer: unsafe/T
    let count: isize

    func get(self: ref/Self, index?: isize) -> ref{self.source}/T

struct ArenaRef<T> {arena}
    let index: isize
```

宣言は依存を保持するだけで、Loan・pointer validity・共有／排他の権限・Copy 能力を与えない。安全な API を提供する実装は Unsafe 境界または検証済み組み込み処理で、初期化・範囲・アラインメント・権限・保存期間を保証する。入力から取得した借用に依存する値は、必要な実際の Loan も保持する。

構造から短縮の安全性を証明できない一般の phantom スロットは保守的に不変とする。安全なフィールドがないことを寿命の延長・依存の消去に使わない。検証済み組み込み型は既存の契約・権限情報を保持する。新しい変性注釈や、一般の phantom wrapper の自動的な権限推論は追加しない。

## 8. 代表例

### 8.1. 構築と返却

```kimi
struct View<T> {source}
    public let value: ref{source}/T

    public init(value?: ref/T)
        origin value outlives source
        self.value = value

func makeView<T>(value?: ref/T) -> View<T>{r}
    origin r.source == value
    return View<T>.init(value)
```

コンストラクターは型の `source` を継承し、入力をそこへ保存できることを証明する。構築時の束縛は期待型と引数から求め、コンストラクター独自の結果スロットやフィールド名による自動対応は作らない。

### 8.2. 複数の入力と保存先

```kimi
func select<T>(a?: View<T>, b?: View<T>, useA?: bool)
    -> ref{a.source and b.source}/T
    if useA
        return a.value
    else
        return b.value

func makePair<A, B>(a?: ref/A, b?: ref/B) -> Pair<A, B>{r}
    origin r.left == a
    origin r.right == b

struct Slot<T>
    public var value: ref{source}/T

func replace<T>(slot?: uniq/Slot<T>, value?: ref/T)
    origin value outlives slot.source
    slot.value = value
```

`select` が `a.source` 全体で使える結果を返すなら、結果を `ref{a.source}/T` にし、`origin b.source outlives a.source` を要求する。等値が必要な場合だけ `==` を使う。

`makePair` は依存を別々に保持する。片方だけ明示すれば残りには省略規則が適用される。`replace` は外側の排他借用 `slot` と保存先の `slot.source` を区別する。

### 8.3. 入れ子の依存を返す

```kimi
func firstView<T>(items?: ref/Array<View<T>{inputOrigins}>)
    -> View<T>{r}
    origin r.source == inputOrigins.source

enum MaybeView<T> {source}
    Some(View<T>{v})
        origin v.source == source
    None
```

Array 自体の借用と要素の内部依存を分け、必要な型の出現箇所に名前を付ける。enum でも Case の局所名から型の公開スロットへ束縛し、新しい型引数パス構文は必要としない。

## 9. 公開契約と実装上の不変条件

### 9.1. 契約の保持・比較

公開型のスロット名は、由来となるフィールドが private でも API の一部である。射影はフィールドへのアクセスを許可しない。スロットの改名・追加・関係変更は API 変更として扱う。

契約には、完全な型、量化とスコープ、固定束縛、条件付きスロットの有効条件、正規化した関係、整形式条件、Loan 要求・依存を保持する。名前・明示／暗黙の違いだけでオーバーロードや特殊化を区別しない。

Contract 実装・override は、元の機能の型構造規則に従い、前提を強めず結果保証を弱めない。要求側の全称 Origin は任意の固定記号として扱い、実装側で実体化可能な呼び出し Origin だけを解く。固定された型・捕捉は再束縛せず、義務自身を証明の前提に使わない。関数参照・成果物・再読み込みでも契約と証明依存を保持する。

### 9.2. 正規化と再利用

スロットは宣言に結び付いた安定した ID で識別する。匿名入力は宣言・入力位置・正規化した型の出現箇所・対象スロットで識別し、別の出現箇所を混同しない。交差は平坦化し、証明済みの等値・包含で簡約して、綴りや走査順によらない安定順に整列する。Origin を簡約しても独立した Loan は残す。

再帰型は有限の宣言スキーマを先に確定し、依存・変性を固定点で検証する。閉じたヘッダーだけで確定するのはスロットの名前・数・ID であり、変性・Loan 要求・Copy・レイアウトまで本体なしで確定するとは限らない。

共有スキーマと出現箇所の束縛を分け、正規化した構造・ID・バッファを再利用する。キャッシュは束縛・前提・有効条件・証明依存の変更を検証し、使用箇所の Loan・初期化・アクセス検査を省略しない。置換後の式は再正規化するが、完成済みの量化は変えない。

原子的な等値には union-find、相互 outlives には強連結成分を利用できる。ただし複合式・条件付きスロット・型適合を単純な辺の到達判定だけに還元しない。二乗規模になり得る推移閉包の常時計算は要求しない。Origin は実行時引数・寿命タグを追加せず、Origin の違いだけを理由に機械語を複製しない。

## 10. 診断・移行・検証

### 10.1. 診断

| 状況 | 示す内容 |
| --- | --- |
| 値に外側の借用がない／set を単一 Origin として使用 | 必要なスロット射影。 |
| 未知のスロット・relation の未知名 | 既存名の候補。relation は宣言ではないこと。 |
| 暗黙スロットが二つ以上／閉じたヘッダーの未知名 | 誤記の候補、または必要な明示ヘッダー。 |
| 結果だけに現れる scalar Origin | 全称契約であること。誤記の可能性も示す。 |
| 保存型・初期化式なしのローカルが未完成 | 未決定の位置と、必要な完全注釈・等値関係。 |
| relation 成立後も型適合・借用に失敗 | 不変位置、整形式条件、不足する Loan など実際の原因。 |
| フィールドで追加の公開前提が必要 | 型の制約領域に書くべき条件。 |
| callable 内の固定束縛不足／per-call Origin の漏出 | 固定 Origin と内部量化の境界。 |

自明な relation や、完全な契約で効果がないと証明できる記述には冗長性の警告を出せる。結果に現れないという理由だけで入力の関係を冗長と判定しない。

### 10.2. 旧構文からの移行

```kimi
// 旧: func get<T> {s}(x?: View<T>{s}) -> ref{s}/T
// 新:
func get<T>(x?: View<T>) -> ref{x.source}/T
```

関数の `{s}` 宣言は廃止し、必要な scalar 名はシグネチャの借用注釈で導入する。旧 `View<T>{s}` は Origin の適用、新構文は set の命名であり、意味が異なる。mapping は set 名と各スロットの relation に、ヘッダー bound の `a : b` は `origin a outlives b` に置き換える。

旧 `origin`／`from` 借用注釈と、関数呼び出しへの `f{a}(...)` 適用は導入しない。移行時に `{s}` を文字列として機械的に置換せず、束縛の役割を確認する。

### 10.3. 統合・実装時の検証項目

以下を肯定例・否定例の両方で検証する。本書の完成を実装の検証完了とは扱わない。

1. **表記とスコープ**: 命名の中立性、暗黙スロット最大一つ、閉じたヘッダーと `{}`、衝突、局所名、フラグメント順序、callable 境界。
2. **契約の完成**: 自明な relation、等値置換、結果の全称化と省略、直接入力の交差、Owned による既定値、使用時に量化を変えないこと。
3. **保存と推論**: 入れ子・enum・基底・完全な型引数、初期化式なしの完全注釈、後続代入、関連型・alias・式中の型式。
4. **安全性**: Loan の分離・保持、排他権限、ローカル参照の escape、phantom の不正な依存消去・寿命延長、破棄時の依存。
5. **比較と再利用**: Contract・override・特殊化・accessor・関数参照・成果物、右辺の交差、循環的な証明の禁止、意味変更によるキャッシュ無効化。
