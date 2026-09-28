# Origin の表記・結合・引数省略 — 仕様変更案（最終版）

日付: 2026-09-20

本書で変更する事項は、[SPEC.md](../../SPEC.md) およびその参照先より優先する。変更しない事項には既存仕様を適用する。本書は仕様変更案であり、仕様本体への反映・実装・検証の完了を意味しない。例は独立した例で、必要な型・Contract は定義済みとする。シグネチャだけの例では本体を省略する。

## 1. 基本方針

宣言では `<...>` は型・長さ等のジェネリック、`{...}` は Origin、`(...)` は値を表す。Origin の指定先は構文で区別し、入力の省略は既存契約の継承または独立した匿名パラメーターとして補う。

| 用途 | 表記 |
| --- | --- |
| ジェネリックパラメーター | `<T>`、`<s/T>`、`<length N>` |
| Origin パラメーターの宣言 | `{source}`、`{a, b : a}` |
| 集成型への Origin 引数 | `View<T>{a}`、`Pair<A, B>{left => a, right => b}` |
| 借用 Origin | `ref{a}/T`、`uniq{borrow}/W` |
| 値パラメーター | `(value: T)` |

```swift
struct Utf8Writer<W> {target}
    W is BufferWriter

    var buffer: uniq{target}/W

contract Utf8Format
    func format<W>(self, writer: uniq/Utf8Writer<W>) -> Result<(), BufferFull>
        W is BufferWriter
```

この関数要件は、次の明示形と同等である。

```swift
contract Utf8Format
    func format<W> {target}(self, writer: uniq/Utf8Writer<W>{target}) -> Result<(), BufferFull>
        W is BufferWriter
```

`self` は引き続き `self: ref/Self` の省略形である。所有権、Loan、variance、Reborrow、`Owned`、破棄、静的ストレージの安全条件は変更しない。

## 2. 宣言・指定・結合

### 2.1. 宣言と共通規則

宣言順序を「名前 → `<...>` → `{...}` → 値パラメーターまたは基底型」に統一する。

```swift
func read<T> {source}(value: ref{source}/T) -> ref{source}/T
    return value

struct View<T> {source} : Base<T>
    let value: ref{source}/T
```

明示的な Origin 宣言リストを、通常の関数、コンストラクター、明示シグネチャを持つアクセサー、Contract の関数・アクセサー要件、struct、enum に許可する。コンストラクターとアクセサーでは `init {a}(...)`、`get {a}(...)`、`set {a}(...)` の位置に置く。新しい型パラメーターやオーバーロードは追加しない。

コンストラクター・アクセサー自身の Origin は呼び出しごとに束縛し、外側の型の Origin は既存の束縛として保持する。コンストラクターの結果は含有型の契約に従い、自身の Origin を隠れた結果スロットとして追加しない。Contract 自体、group、特殊化、関数型、`Callable`、匿名関数、シグネチャのない標準アクセサーには明示リストを追加しない。

- すべての Origin 用 `{...}` は非空とし、末尾カンマを許可する。`{}` は禁止し、不要ならリスト全体を省略する。
- 1つの宣言に Origin 宣言リストは1つまでとする。リスト内と、外側から継承した Origin 名との重複を禁止する。現行 §9.6.1.1 と同じく入れ子の struct・enum にも適用し、衝突は内側の宣言位置で診断して改名を案内する。型パラメーターとは別の名前空間で解決する。
- Origin 宣言は基底型・引数・結果・制約・本体に有効である。リスト全体を束縛してから制約を解決する。
- `{a, b : a}` の `b : a` は `region(b) ⊇ region(a)` を意味する。各パラメーターの明示制約は1つまでで、対象は可視の抽象 Origin または `static` とする。不明な対象は拒否する。相互の outlives は領域の等しさを要求し、制約を既定値として扱わない。
- `_` に新しい匿名宣言・推論指定の意味を与えない。

`{` と `}` は1組で1段の区切りとし、括弧内の継続行・区切り領域に含める。本体や集合を表さず、実行スコープや本体の基準インデントも作らない。現行 §2.4 の利用不可トークン分類、§2.2 の区切り規則、§1.2 と付録 D の `{}` 予約は、この用途に合わせて置き換える。将来の Struct Pattern には Origin 指定と区別できる別構文が必要である。

空白の有無で意味は変わらない。標準整形は `f<T> {a}(...)`、`View<T>{a}`、`ref{a}/T` とする。既存のシフト演算とジェネリックの括弧規則は維持する。

### 2.2. 集成型・Container への Origin 引数

名前付き参照の末尾の `{...}` は、その参照の Origin スロットに結合する。外側の借用には付かない。

- `{expression}` は、§3.1 の有効スキーマにおける未束縛スロットがちょうど1つの場合だけ許可し、そのスロットへ対応させる。継承分を含む総数ではなく、既存の束縛を除いた残りを数える。
- 複数スロットには `{left => a, right => b}` の名前付き指定を使う。単一スロットにも名前付き指定を使える。位置指定 `{a, b}` と、単一式・名前付き指定の混在は禁止する。
- 名前は宣言側のスロットの Binding Identity に対応させる。記載順による対応、不明な名前、重複、束縛済みスロットの再指定を認めない。
- 部分的な名前付き指定を許可する。未指定の各スロットには位置別の省略規則を適用し、記載した指定は固定した制約として保持する。

**スキーマの確定。** Origin スキーマとは、参照先の宣言、宣言 Origin スロット、その制約、既存の束縛からなる構造であり、アクセス修飾子の「公開」を意味しない。名前解決・既存の型同一性の解決により、ジェネリック定義の検査時にこの構造を確定できる場合だけ、新しい Origin 引数を指定できる。

型変数・関連型でスキーマを確定できない場合、`T{a}` は定義時のエラーとする。通常の宣言依存による検査待ちは可能だが、都合のよい型引数が渡されるまで判断を延期しない。既に完全な型として束縛された `T` や `Self` の依存は保持し、具体化後の型が借用型であることを理由に、借用 Origin の上書きへ解釈を変えない。

### 2.3. 借用 Origin と結合規則

#### 2.3.1. 指定する層

借用 Origin は Semantics と `/` の間に置く。安全な借用 Semantics である `ref`、`uniq`、`objref`、`objuniq` に許可し、`owner`、`obj`、`rc`、`arc`、`unsafe` には禁止する。`s{a}/T` は、宣言の制約から `s` が安全な借用 Semantics であると証明できる場合だけ許可する。対象型の既存の適法性検査も必要である。

借用用の `{...}` は Origin 式1つと任意の末尾カンマを受け付ける。名前付き引数・宣言・outlives 制約は受け付けない。Semantics 接頭辞は右結合し、注釈は記載した層だけに付く。

```swift
uniq/Utf8Writer<W>{target}          // 内側を指定。外側は省略
uniq{borrow}/Utf8Writer<W>          // 外側を指定。内側は位置別に補完
uniq{borrow}/Utf8Writer<W>{target}  // 両方を指定
ref{outer}/uniq{inner}/T            // 借用の層ごとに指定
```

`(uniq/T){a}`、`(View<T>){a}`、`View<T>{a}{b}` は禁止する。完成した型を囲む `(View<T>{a})` は許可する。括弧は Origin スロットを作らず、指定先を外側へ移さない。

#### 2.3.2. ペアパラメーターの保持と再構成

`<s/T>` が受け取る完全な型を `W` とすると、次のように区別する。

| 形 | 外側 Origin の扱い |
| --- | --- |
| 元の `s/T` | `W` の束縛を保持する。同じペアの出現間で共有し、匿名 Origin を追加しない |
| `s{a}/T` | 外側の借用 Origin だけを指定し直す。安全な借用である証明が必要 |
| 別の対象への `s/U` | `W` の外側 Origin は引き継がず、使用位置の省略規則を適用する |

元のペアかどうかは宣言の Binding Identity で決め、具体化後に `T` と `U` が同じ型になっても扱いを変えない。内側の完全な型の束縛はいずれも保持する。

`s` が未確定でも、直接入力位置の `s/U` には、新たに構成する外側借用用の匿名 Origin を出現ごとに1つ、定義時に記録する。これは `s` が安全な借用である場合だけ有効な条件付きスロットである。それ以外では束縛・制約を生まず、`U` 自身の依存は消さない。`owner/U` は従来どおり `U` へ正規化する。

省略許可は位置で決まる。局所変数は初期化子から推論し、結果は §4.3 に従う。フィールド・入れ子の借用層など、省略を許可しない位置では、借用の可能性があって必要な Origin を指定できなければエラーとなる。

条件は Semantics の種別に基づき、具体化時には定義済みの契約へ代入するだけで、新しいパラメーターを追加しない。型構成・本体・Loan の適法性は許容するすべての束縛で定義時に証明する。対象型 `U` の適法性まで無条件には認めない。条件付き契約の比較・保存は §5 に従う。

以下は `s = ref`、`T = i32`、`U = string`、`W = ref{a}/i32` のときの型の対応である。`ρ` は直接入力位置で導入する匿名 Origin の説明用記号であり、ソース上の名前ではない。

```text
s/T       → ref{a}/i32
s{b}/T    → ref{b}/i32
s/U       → ref{ρ}/string   // 直接入力位置。a は引き継がない
```

### 2.4. Origin 式と安全条件

名前、`static`、`value.source` などの射影、`and` の共通部分は維持する。

```swift
ref{x}/T
ref{self.source}/T
ref{x and y}/T
View<T>{a and b}
```

`{a and b}` は1つの Origin 式である。`{a, b}` は宣言位置では2つの宣言、使用位置ではエラーであり、共通部分を意味しない。

Origin 間の関係を、記述した**宣言制約**と、型構成から必要になる**整形式関係**に分ける。`{b : a}` は宣言制約であり、名前のない入力 Origin に直接は記述できない。整形式関係は匿名 Origin にも適用する。現行 §15.3 の「暗黙入力 Origin に bound を宣言できない」は、宣言制約だけを指すものとする。

例えば `uniq{borrow}/Utf8Writer<W>{target}` は整形式関係 `target : borrow` を要求する。内側は外側と同じか、それより長く有効でなければならず、`W` 自身の依存も保持する。両種の関係を公開契約に記録し、本体では前提、呼び出し側では検証条件とする。本体から追加の呼び出し条件を後付けしない。

型の構成は値の変換ではない。Origin の指定・推論・簡約は寿命や排他性を作らず、別の Loan を消去しない。`static` の指定だけで安全な排他的借用を作ることもできない。

## 3. 名前付き参照と使用位置

### 3.1. 有効スキーマと経路

型、基底型、Contract、関連型の Contract 選択、Container 修飾子で、同じ名前付き参照と Origin 引数の構文を使う。構文の共通化は、参照先の種類・アクセス・基底型の制限を緩めない。基底型は引き続きアクセス可能な構築済みまたは非ジェネリックの open struct Core に限る。

**有効スキーマ**は、外側から継承したスロットと当該宣言のスロット、および既存の束縛を合わせたものとする。型引数内部の Origin を重複して追加しない。末尾指定で解決できる未束縛スロットの診断は、参照の末尾を確認してから行う。Contract 自身に Origin 宣言がなくても、`Outer<T>.Source{a}` のように外側から継承した未束縛スロットを指定できる。

```swift
Outer<T>.Inner<U>{outerSource => a, innerSource => b}
(Outer<T>{outerSource => a}).Inner<U>{innerSource => b}
```

最終スキーマに含まれる Origin は、上の2例のように末尾でも途中でも指定できる。経路の途中に書く場合は必ず括弧付き形式とし、`Outer<T>{a}.Inner` は追加しない。最終スキーマに含まれない中間 Origin は、その修飾子で明示する。

括弧付き修飾子には後続のメンバー選択（`.init` を含む）を要求する。型位置の単独 `(View<T>{a})` はグループ化だけであり、`(View<T>{a}){b}` への再指定はできない。

経路上の束縛は Binding Identity で正規化し、最終スキーマに対応する分は末尾へまとめた形と同等に扱う。最終スキーマにない中間 Origin の束縛と検証義務も保持する。意味解析済みの正規出力では、束縛先・可視性・条件を変えずに移せる指定を名前付きで末尾へまとめ、必要な中間指定だけを括弧内に残す。構文だけの整形では移動しない。既存の束縛や字句的な `Self` 環境を書き換えない。

### 3.2. Origin を記述できない対象

`@` の適応対象と実行時 `is` の対象は、入れ子の型引数・修飾子を含めて Origin 注釈を記述できない。既に束縛された型に含まれる依存は保持する。これらの対象内の `{` は専用のエラーとし、型名・Semantics の別解釈へ戻らない。適応対象の `/` と Semantics の認識は既存規則を維持する。

Origin 引数を必須とする `BoundContainerQualifier` は、適応・実行時 `is` の対象には現れない。現行 §13.5.1 の括弧付き Container 修飾子に関する句は、この規則に置き換える。その他の括弧構文の可否は、それぞれの対象の既存規則に従う。

構築式は別であり、`(View<T>{a}).init(...)` のように構築対象の型の Origin を指定できる。これは `init` 自身の呼び出し時 Origin 引数の指定ではない。`View<T>{a}.init(...)` は追加せず、既存の括弧付き型修飾子を用いる。

## 4. 位置別の Origin 省略

### 4.1. 継承を先に行う共通規則

**対応する完全な契約が既に決まっている位置では、省略は新しい量化ではなく契約の継承である。** 保存プロパティのカスタムアクセサーは保存型に対応する入力・結果の Origin を、特殊化は元の関数契約を継承する。明示した指定は上書きせず、継承契約との適合を検証する。

通常の関数・コンストラクター・アクセサーの入力は、対応する完全な契約がなければ次の規則で確定する。Contract の要件と、保存契約のない計算プロパティ・required アクセサーにも適用する。

1. 既存の束縛を保持し、引数型に現れる各集成型参照の未指定スロットに、新しい匿名 Origin パラメーターを導入する。部分的な名前付き指定の残りにも適用する。
2. 別の引数・出現箇所・スロットには別のパラメーターを導入する。ただし、実際に同じ領域へ束縛されることは禁止しない。
3. 呼び出しごとに束縛し、宣言の制約と型の整形式条件を満たすすべての束縛に対して本体を検査する。匿名 Origin を本体から推論したり、省略を理由に `static` に固定したりしない。

`View<T>` が1つの Origin を持つ場合、次の入力契約は同等である。

```swift
func inspect<T>(left: View<T>, right: View<T>) -> ()
func inspect<T> {a, b}(left: View<T>{a}, right: View<T>{b}) -> ()
```

型引数、Tuple・配列要素、借用の対象へ再帰的に適用する。ただし、既に束縛された完全な型や集成型のフィールドを展開して新しいパラメーターを作らず、関数型・`Callable` の別シグネチャ境界も越えない。

外側の直接借用入力・receiver の省略は維持し、集成型内部の Origin とは独立に扱う。内側の直接借用への新しい省略許可は追加しない。

```swift
func inspect<T>(values: Array<View<T>>) -> ()  // View の Origin を補う
func inspect<T>(value: ref/View<T>) -> ()     // 外側と内部を独立に補う
func inspect<T>(value: ref/ref/T) -> ()       // 内側の借用 Origin 不足
func inspect<T> {a}(value: ref/ref{a}/T) -> () // 内側を明示
```

### 4.2. 保存・期待型・その他の位置

契約の補完後に本体と保存先への適合を検査する。保存アクセサーの対応する値・結果は保存型から補完し、receiver の借用は通常の呼び出し時契約を使う。明示 Origin パラメーターを導入しても、保存アクセサーが受け付けるべき呼び出しを狭めてはならない。

コンストラクターの入力はフィールドと自動対応しない。入力を保存する場合は、含有型の結果・保存契約への適合を証明する。名前付きの入力 Origin に外側の保存 Origin への制約を記述することはできるが、本体の代入から入力契約を逆推論しない。

| 位置 | 規則 |
| --- | --- |
| インスタンスフィールド・enum payload | 引き続き明示し、初期化から保存契約を推論しない |
| 初期化子のある局所変数 | 既存の型・Origin・Loan 推論。匿名の全称量化ではない |
| 戻り値 | §4.3。直接借用候補と借用層の既定を維持し、集成型スロットの既定を限定 |
| 静的フィールド | 既存の `Owned`・静的ソース規則 |
| 関数型・`Callable`・匿名関数 | 既存の量化・期待型・推論規則。集成型入力を新しく全称量化しない |
| Contract 参照・alias・単独の Container 修飾子 | 未束縛の Origin を明示 |
| 特殊化 | §5.3 に従い元の契約から補完。新しい量化ではない |

関数型・`Callable` の入力内部で、集成型の必須 Origin が既存の完全な型や契約から決まらなければ、明示を要求する。`(View<T>) -> ()` を新しい量化として受理しない。共通関数型なら `(View<T>{a}) -> ()` と書ける。明示注釈を禁止する `Callable` では、既に Origin を束縛した完全な型を型引数等から受け取る。各シグネチャ自身の結果省略や、既存の匿名関数の期待型による検査まで禁止しない。

### 4.3. 戻り値への依存

既存契約から補完できる結果を先に処理し、残る未指定の結果 Origin を、借用層・集成型スロットごとに次の順で決める。部分的な名前付き指定の残りも同じ規則に従う。

1. 未指定の Origin がなければ補完しない。明示指定と、完全な型に既に含まれる束縛は保持する。
2. 直接借用入力があれば、その外側 Origin の共通部分を使う。集成型内部の Origin は候補へ追加しない。
3. 直接候補がなければ、次のように補う。

   - **借用層:** 共有借用は従来どおり `static` とする。排他借用には明示を要求する。
   - **集成型スロット:** すべての入力型について、既存の前提から `Owned` を証明できる場合だけ `static` とする。入力なしもこの条件を満たす。`Unknown` または `Refuted` なら明示を要求する。既定化できる Loan 要件は `none` または `ref` に限り、`uniq` には明示を要求する。

現行 §15.4 の結果省略規則1〜3を以上で置き換える。既存の束縛の保持、直接借用候補、借用層の既定は維持し、集成型スロットの `static` 既定だけを制限する。補完後の整形式・variance・Loan 検証は引き続き必要であり、本体から結果 Origin を推論しない。

`Owned` は現行 §15.2.3 と同じ述語、すなわち完全な入力型の `OwnedOrigins` がすべて `static` であることを指す。別の依存判定や隠れた `Owned` 制約は導入しない。証明できなければ結果を明示すればよく、本体から入力制約を追加しない。

借用層は既存の既定との互換性を保つ。集成型スロットは、新しい匿名入力 Origin との関係を意図せず `static` に固定しないため、明示を優先する。この差は省略の方針であり、借用結果が集成型入力に依存できないという意味ではない。

条件付きの直接借用入力も §2.3.2 の有効条件に従って候補へ含める。結果の補完規則を各条件の下で適用した契約を定義時に確定し、許容される場合のいずれかで補完・検証できなければ明示を要求する。既存の完全な型から引き継ぐ依存は固定したまま扱う。

```swift
func describe<T>(value: T) -> ref/string           // 結果は ref{static}/string
func describeView<T>(value: View<T>) -> ref/string // 同上
func identity<T>(value: View<T>) -> View<T>        // 結果の集成型 Origin を明示
```

内部 Origin を結果へ公開するときは、宣言名または値からの射影を明示する。例えば、`View<T>` が `source` と共有借用フィールド `value` を持つ場合、次は同じ関係を表す。

```swift
func get<T> {a}(value: View<T>{a}) -> ref{a}/T
    return value.value

func get<T>(value: View<T>) -> ref{value.source}/T
    return value.value
```

後者も明示的な結果契約である。射影は既存の名前解決・可視性に従い、型引数内の匿名スロットを探す新しい射影は導入しない。例えば `Array<View<T>>` 内部の Origin を結果と結び付けるには、入力側にも名前を明記する。

```swift
func firstView<T> {source}(items: ref/Array<View<T>{source}>) -> View<T>{source}
```

必要な Origin を射影できない場合は、入れ子の入力型の該当箇所と結果に同じ宣言名を指定するよう診断する。局所値からの既存の射影は維持するが、公開シグネチャには局所値を使えない。

## 5. Origin 契約の確定と利用

### 5.1. 共通の正規化

宣言の意味を確定するとき、入力・結果・参照環境を含む Origin 契約を正規化し、次を区別して保持する。

- **パラメーター:** 明示・匿名の束縛、量化範囲、条件付きスロットの有効条件。匿名入力は有効な場合に呼び出しごとに束縛する。
- **既存の束縛:** 型引数・外側の宣言・捕捉などから引き継ぐ Origin。新しい量化へ置換しない。
- **関係:** 射影先、Origin 式、宣言制約と整形式関係を区別した根拠、結果の依存と Loan 要件。経路上の指定は §3.1 の対応へ正規化する。

局所変数や呼び出しの推論変数は、解くべき束縛を表す一時的な変数であり、新しい公開パラメーターではない。`value.source` などの射影は対象スロットへ対応させ、名前付き指定の順や匿名の仮名で契約が変わらないようにする。

グループ化・冗長な `owner` などを正規化した後も、異なる型の出現箇所は区別する。匿名の束縛は宣言・入力位置・型内の構造上の位置・対象スロットで識別し、解析順や一時名に依存させない。Origin の等価性と Loan の同一性を混同しない。

具体化や前提の更新後は有効条件を再評価する。無効と確定した条件付きスロットは契約比較から除外し、型の構成履歴による同一性の差を作らない。内側の型の束縛・依存は保持する。

### 5.2. 同等性・適合性・関数値

**同等性**は、束縛を対応付けた後の型構造・量化・有効条件・制約・結果依存が同じことを意味する。明示と匿名の違いや、明示パラメーター数だけで不一致にしない。`OriginArity` は明示宣言数の記録として残せるが、意味上の契約には匿名パラメーターも含める。型宣言の Origin スキーマと分割宣言の一致要件は維持する。

**適合性**は、要求側が許すすべての呼び出しを実装が受け付け、要求以上の結果保証を満たすこととする。同等性や、パラメーター数の機械的な1対1対応とは区別する。Contract 適合・共通関数型への変換・`Callable` 適合の Origin 検証を、次の共通手順で行う。

1. 各機能の既存の型構造規則で対応位置を決める。Contract の入力構造一致と、関数型・`Callable` の既存の反変・共変な適合を混同しない。
2. 要求側の量化 Origin は任意だが固定された記号とし、実装側の具体化可能な呼び出し時 Origin だけを推論変数にする。捕捉・型引数等の固定束縛は変数にしない。
3. 入力・結果の型関係から制約を集め、現行 §15.3.4 の限定ソルバーで、実装側の変数を要求側の式や可視の固定 Origin へ置く代入を求める。入力には `要求入力 <: 実装入力`、結果には `実装結果 <: 要求結果` を課す。Contract の入力は手順1で一致した型構造上の Origin 関係を検証する。Contract の結果には既存の型規則が許す部分型も認め、結果の構造一致は要求しない。
4. 代入後の実装側の宣言制約・整形式関係・結果保証が、要求側の前提から導けることを確認する。証明したい実装条件を前提へ追加してはならない。Loan・receiver・環境・アクセス等も通常どおり検証する。

条件付きスロットは、対応する Semantics の条件の下で同じ手順を適用する。有効な場合だけ Origin 変数・制約・結果候補として扱い、無効なスロットには主解を要求しない。許容されるすべての場合で適合を証明し、具体化後だけの成功では受理しない。条件には既存の Semantics 種別を使い、実行時分岐や一般の条件式ソルバーを導入しない。

使用する証明規則・主解・不変位置の等式条件は既存の限定ソルバーに従う。一般の領域探索や定理証明は行わず、必要な主解や証明を期限までに得られなければ適合を認めない。量化範囲を越えた変数の参照は許可しない。

全称量化された実装を、要求側の固定された Origin に具体化することは許可する。

```swift
// View は共有借用を保持する型とする。
func inspect(value: View<i32>) -> () => ()
let f: (View<i32>{static}) -> () = inspect
```

逆に、固定された Origin の入力しか受け付けない実装を、任意の Origin の入力を要求する契約へ適合させてはならない。捕捉の固定 Origin を新しい量化へ変えること、Loan・依存を消去することも禁止する。要求を既存の関数型・`Callable` で表現または証明できなければ拒否し、新しい高階の量化能力を暗黙に追加しない。`Callable` 内の明示 Origin 注釈など、既存の構文制限も維持する。

関数参照は完全な契約を保持する。通常の呼び出しは既存の推論・適合規則で束縛し、`f{a}(...)` という関数 Origin 引数構文は追加しない。型・Container 参照の Origin 引数とは区別する。Origin の名前・個数・制約・明示の有無だけでオーバーロードや特殊化を増やさない。

### 5.3. 特殊化の契約継承

特殊化の省略箇所は、元の契約から埋める位置であり、新しい匿名パラメーターでも、自由な推論変数でもない。

```swift
func inspect<T>(value: View<T>) -> () => ()
specialize func inspect<i32>(value: View<i32>) -> () => ()
```

次の順で処理する。

1. 既存の名前・宣言種別・ジェネリック個数の規則で候補を集め、明示ジェネリック引数を各候補へ対応させる。
2. 候補ごとに正規化した入力の型構造を照合し、省略された Origin 位置を、元の契約または束縛したジェネリック引数内の位置へ仮対応させる。Origin の共有・独立・固定・制約を保持し、Origin に依存する検証条件は捨てずに保留する。
3. 対象は既存の Origin を除いた構造条件で一意に選ぶ。0件・複数件はエラーで、Origin 制約の成否や結果型で候補を絞らない。
4. 選択した契約から省略を補完し、明示指定との一致、完全な型の適法性、結果、制約、Loan を検証する。失敗しても別候補へ戻らない。

旧仕様の「比較前に完全な型の Origin 検証を完了する」手順は、この手順に置き換える。未確定の Origin 対応を持つ候補を有効な宣言として公開せず、最終検証前の生成も行わない。対応先のない省略はエラーとし、特殊化の本体から新しい条件を追加しない。

### 5.4. 共有・保存と性能

正規化した契約を名前解決・型検査・適合・保存成果物で共通利用する。型スキーマと契約の構造は共有し、出現箇所ごとの束縛を分離する。条件付きスロットも条件とともに共有できるが、量化・依存・Loan 検査は省かない。

必要時に制約ノードを作る遅延表現と、構造上の位置を共有 ID にまとめる方式を推奨する。固定長 ID の組なら、同一の管理範囲内で比較・ハッシュを定数時間にできる。ID の構築、成果物間の対応付け、契約全体のハッシュ生成までの保証ではなく、配列・リスト等の実装も許可する。

キャッシュには正規化した構造・束縛・前提・有効条件・依存スキーマの版を含める。`OwnedOrigins` と証明結果も既存の解析結果を再利用し、同じ依存閉包の再走査を避ける。ハッシュ衝突を同一性の証明にせず、依存変更時に無効化する。場所ごとの Loan・初期化・可変性の検査は別途行う。

意味上の検査後に既存規則で生成用キーから Origin を除外し、Origin だけの差で機械語や実行時メタデータを複製しない。改善対象は主にコンパイル時間とメモリであり、効果は実装・測定で確認する。

## 6. 共通文法と診断

### 6.1. 文法

以下で既存の対応規則を置き換え、列挙しない要素は既存仕様に従う。`OriginAnnotation` は廃止する。波括弧の内容は一度読み取り、宣言位置・Semantics 直後・参照末尾という役割に従って §2 の制限を検査する。

```ebnf
OriginBraces     := "{" OriginItem ("," OriginItem)* ","? "}"
OriginItem       := Name ":" OriginBound
                  | Name "=>" OriginExpression | OriginExpression
OriginBound      := Name | "static"
OriginExpression := OriginAtom ("and" OriginAtom)*
OriginAtom       := Name ("." Name)* | "static"
OriginParameters := OriginBraces  // 宣言: 名前または名前付き制約
OriginArguments  := OriginBraces  // 引数: 単一式または全項目が名前付き
BorrowOrigin     := OriginBraces  // 借用: 単一式のみ

PlainNamedType   := ContainerPath
NamedReference   := PlainNamedType OriginArguments?
NamedType        := NamedReference
ContainerReference := NamedReference
ContractReference := NamedReference
BoundContainerQualifier := "(" ContainerPath OriginArguments ")"
PathSuffix       := "." TypeSegment | "." "(" ContractReference ")" "." Name
ContainerPath    := "::"? TypeSegment PathSuffix*
                  | BoundContainerQualifier PathSuffix+
TypeSegment      := TypeName TypeArguments?

Type             := FunctionType | SemanticsType
SemanticsType    := Semantics BorrowOrigin? "/" SemanticsType | TypeAtom
TypeAtom         := CoreType | "(" Type ")"
CoreType         := NamedType | UnitType | TupleType | FixedArrayType
BaseClause       := ":" NamedReference
StructureDeclaration := Access? "open"? "struct" Name GenericParameters?
                        OriginParameters? BaseClause? ContainerBody
ConstructorDeclaration := Access? "init" OriginParameters?
                          "(" TrailingList<Parameter>? ")"
                          BaseInitializer? ExecutableBody
GetterSignature  := "get" OriginParameters? "(" AccessorReceiver? ")" "->" Type
SetterSignature  := "set" OriginParameters?
                    "(" (AccessorReceiver ",")? "value" ":" Type ")" "->" UnitType

OriginFreePath   := ? ContainerPath の全階層に明示 Origin がない経路 ?
NamedCoreType    := OriginFreePath
AdaptationAtom   := OriginFreePath | UnitType | "(" OriginFreeType ")"
                  | "(" OriginFreeType "," TrailingList<OriginFreeType>? ")"
                  | "[" ArrayLength "of" OriginFreeType "]"
OriginFreeType   := ? Type の全階層に明示 Origin がない型構文 ?
ConstructionQualifier := PlainNamedType | BoundContainerQualifier
ConstructionExpression := ConstructionQualifier "." "init"
                          "(" TrailingList<Argument>? ")"
BoundContainerExpression := BoundContainerQualifier
                           ("." Name | "." "(" ContractReference ")" "." Name)
```

`PlainNamedType` は末尾指定を持たないが、中間修飾子・型引数には Origin があり得る。適応・実行時 `is` では再帰的な Origin 禁止条件を使う。構築式にはこの禁止を流用しない。式の `Primary` にある単独の `BoundContainerQualifier` は `BoundContainerExpression` に置き換える。

現行 §6.2.3 と同じく、普通のメンバー名に `init` は使えない。この禁止を `BoundContainerExpression` にも適用し、型側の経路に続く `.init(` は括弧付き修飾子を含め常に `ConstructionExpression` として読む。

関数・Contract 関数要件・enum は既存位置で新しい `OriginParameters` を使用する。Contract の親・関連型の選択は共通の `ContractReference` を使用する。コンストラクター・アクセサーの明示 Origin 禁止と、コンストラクターの全 Origin が含有型だけに由来するという旧規定は、§2.1・§4 に置き換える。

### 6.2. 役割の確定と診断

`{...}` は一度読み、構文位置で役割を確定する。

- **宣言位置:** Origin パラメーター宣言。
- **Semantics キーワード直後:** 借用 Origin 指定。`/` と対象型を要求し、欠落はその場で診断する。
- **型引数・修飾のない Name 直後:** 続くトークンが `/` なら借用指定、それ以外なら参照への Origin 引数。
- **その他の名前付き参照末尾:** 参照への Origin 引数。

適応・実行時 `is` の対象では §3.2 の禁止を優先する。名前解決に失敗しても別の役割へ読み直さない。AST の表現・割り当て方法は規定しない。

統合文法で読めても、役割に反する項目・組み合わせはエラーである。宣言項目は単純な名前または `名前 : 境界` に限り、`{static}`・`{a.b}`・`{a and b}`・`{a => b}` を拒否する。境界としての `static` は許可する。使用位置の `{a, b}` は「位置指定は不可。名前付き指定を使用」、`{a, b => c}` は混在禁止と診断する。借用位置の `=>`、使用位置の `:`、空指定、禁止 Semantics、未束縛の関数型入力も原因別に診断する。末尾カンマはすべての役割で許可する。

## 7. 移行と受け入れ条件

### 7.1. 移行

`origin ...`・`from ...` を Origin 構文から削除し、その用途の文脈キーワード扱いも廃止する。通常の識別子としての使用は妨げない。旧構文、`<<...>>`、借用型全体への後置 Origin 注釈は併存させない。

| 旧表記 | 新表記 |
| --- | --- |
| `func f<T> origin a(...)` | `func f<T> {a}(...)` |
| `struct D<T> : B<T> origin a` | `struct D<T> {a} : B<T>` |
| `View<T> from a` | `View<T>{a}` |
| `Pair<A, B> from (left => a, right => b)` | `Pair<A, B>{left => a, right => b}` |
| `ref/T from a` | `ref{a}/T` |
| `uniq/(View<T> from a) from b` | `uniq{b}/View<T>{a}` |
| `ref/(ref/T from inner) from outer` | `ref{outer}/ref{inner}/T` |

移行は旧構文の指定先を解決してから行う。文字列置換で結合先を変えず、一般の型変数の外側 Origin 指定は必要に応じてペアパラメーターによる明示へ書き換える。§4.3 の集成型結果の既定条件に該当しなくなる宣言には、意図した Origin を明記する。従来の固定保証を保つ場合は `{static}`、入力との関係を表す場合は宣言名・射影を使う。

### 7.2. 確認事項

実装時に以下を確認する。本書で検証済みとはしない。

- **構文:** 全宣言・参照、`init/get/set` の明示 Origin、型の入れ子、末尾カンマ、複数行、出力と再読込、誤記からの回復。Semantics キーワード後の `/` 欠落、`@`・実行時 `is` の注釈拒否、括弧付き `.init` の一意な構築式解析と指定保持。
- **指定:** 中間修飾子と二重注釈、継承 Origin を持つ Contract、入れ子宣言での継承名の衝突拒否、一意な名前・束縛に基づく末尾への正規化、最終スキーマにない中間指定の保持、未束縛スロット数による単一式の判定。
- **省略:** 新しい量化・既存契約の継承・局所推論の区別、保存アクセサーと特殊化の補完、関数型の未束縛入力拒否、借用層と集成型スロットの `static` 既定の違い、既存の `Owned` 判定との一致、入れ子の Origin の明示による結果への関連付け。
- **契約と安全性:** 限定ソルバーでの安全な具体化と逆方向の拒否、反変・共変・不変位置、量化範囲、特殊化の曖昧性、元の `s/T` の束縛保持、`s/U` の条件付きスロット・結果候補・適合検証、非借用時の `U` の依存保持、具体化時に新しい量化を追加しないこと、Origin・Loan・静的ストレージ違反の拒否。
- **成果物と性能:** 明示・匿名契約の保存、共有しても別の入力が混ざらないこと、依存変更の無効化、ハッシュ衝突の扱い、遅延表現でも検証条件を落とさないこと。

参照した現行仕様: [字句・レイアウト](../../spec/02-source-and-lexical-structure.md)、[型](../../spec/03-types-and-values.md)、[宣言](../../spec/06-declarations-and-containers.md)、[関数](../../spec/07-functions-and-callable-values.md)、[ジェネリック・Contract](../../spec/08-generics-constraints-and-contracts.md)、[名前・シグネチャ](../../spec/09-names-signatures-and-access.md)、[呼び出し・適合](../../spec/10-overload-resolution-and-inference.md)、[プロパティ](../../spec/11-properties.md)、[演算子・適応](../../spec/13-operators-and-assignment.md)、[所有権・Origin](../../spec/15-ownership-and-lifetime-analysis.md)、[構文要約](../../spec/appendices/F-syntax-summary.md)、[概要の記法](../../spec/01-overview.md)、[予約・将来機能](../../spec/appendices/D-deferred-features.md)。
