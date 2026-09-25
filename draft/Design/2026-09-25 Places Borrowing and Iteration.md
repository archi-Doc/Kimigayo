# 仕様変更案：Place・取得・借用・列挙の統一

日付: 2026-09-25

改訂: 2026-09-26

状態: 統合した仕様変更案。正式仕様への取り込み・実装・実行検証は未実施。

## 1. 目的と適用範囲

**格納側が場所・操作権限・依存関係を公開し、利用側が必要な取得・更新を選ぶ。** この原則を、ローカル、Field、添字、関数結果、Pattern、Iteratorに共通して適用する。

本書は変更後の規則を定義する。変更する事項には本書を、変更しない事項には現行の `SPEC.md` と参照先を適用する。保存だけでは正式仕様を変更しない。他のdraftを成立条件にせず、例は本案の構文・契約を示す。例中の補助型・関数は記載した性質を持つものとする。

| 設計上の選択 | 採用する規則 |
| --- | --- |
| 通常取得 | PlaceはCopyを要求し、移動には`@move`を使う。一時値は転送する |
| 明示操作 | `@move`はCopy型でもMove、`@ref`／`@uniq`は直前のスロットの借用、`@deref`は一層の参照先選択 |
| 暗黙適合 | 期待型が確定した値位置で、共有借用・再借用・Scalar readの共通表を使う |
| 場所の選択 | receiver・列挙入口・構造Pattern・比較では、型から決まる参照経路を省略できる |
| Scalarの利用 | 束縛型を保ち、Scalarを必要とする位置でだけ参照先を読む |
| 共有Pattern | Copy要素も参照として束縛する。Copy能力で束縛型を切り替えない |
| 列挙 | `for`の配送はIterator一つ。所有値、外部借用、呼び出しごとの借用を表せる |

## 2. 格納場所とアクセス経路

### 2.1. 基本概念

| 概念 | 意味 |
| --- | --- |
| Storage | 識別可能な格納場所。ローカル、集約の部分、別領域のpayload、一時値の格納先など |
| 完全型 | Semantics、型引数、入れ子の構造、内部Originを含む型 |
| Place | Storageを選んだ結果。完全型、経路、権限、依存関係を持つ。通常の値型ではない |
| Completeness | 一つの値として扱うのに必要な部分が、すべて初期化されている状態 |
| Origin／Loan | 有効期間の契約／借用のanchor・権限・依存・親子関係。既存第15章の意味を使う |

型が完全に決まっていても、部分Move後の値はIncompleteになり得る。サイズゼロにも論理的な場所があり、固有の物理アドレスは要求しない。

Placeを変数・引数・型引数に格納することはできない。場所を保存するには参照値を取得する。`Option<place(...)>`も作れない。

既存用語との対応は次のように整理する。StorageとPlaceは別々の実行時オブジェクトを要求しない。

| 既存の用語・判断 | 本書での扱い |
| --- | --- |
| Local／Temporary／Candidate Placeなど | 格納実体はStorage、その実体へアクセスする選択結果はPlace |
| Movable Place | Takeを持ち、初期化・所有経路・Loanの条件を満たしてMoveできるPlace |
| Move／Consume | Moveは値と破棄責任の移動、Consumeは取得などの効果を記録する解析上の判断。Takeはそれらと区別する能力 |
| normalized Type identity | Originを含む既存の型同定。Originを除く比較は§6.1の「型形状」と呼び分ける |

### 2.2. 権限と投影

基本能力は、値を観察するRead、値を初期化・置換するWrite、値と破棄責任を取り出すTakeとする。三つに強弱の包含関係はない。所有する`let`はTakeできても再代入できず、`uniq/T`の参照先はWriteできてもTakeできない。

子のPlaceは、親の経路と子の宣言から求める。親のSemanticsで子の完全型を上書きしない。共有経路から排他権限を回復せず、アクセス範囲・Fieldの可変性・必要な所有者の保護を維持する。現在のLoan競合は使用時の条件であり、Contract適合そのものを変えない。

異なるField、Tuple位置、固定配列の異なる範囲内整数リテラル添字は非重複の根拠になる。括弧は分類を変えない。動的添字、異なるkey、十分なcapacity、別の関数呼び出しであることだけでは非重複を証明しない。

### 2.3. `@deref`とスロット

`E@deref`は、`ref/T`／`uniq/T`が指す直近のPlaceを選ぶ。参照値も参照先もCopy／Moveせず、権限と依存を保持する。二層たどる場合は二回書く。場所の特定に必要な参照情報の読み取りは行うが、それを参照値の取得とは扱わない。

```kimi
var number: i32 = 1
let r = number@uniq
r@deref = 2              // numberを更新する。
let child = r@deref@uniq  // numberを排他再借用する。
child@deref += 1
let slot = r@ref          // r自身のスロット。ref/(uniq/i32)。
// r@uniq                // rはletなので、スロットの排他借用は不可。
```

`let`はスロットの再代入を禁じる。保持した参照の権限までは弱めない。ただし、共有経路を経た`ref/uniq/T`からTの排他権限は得られない。`uniq/ref/T`なら参照スロットを更新できるが、Tへのアクセスは共有である。

`@deref`は型名・Semantics名として再解釈しない専用の後置操作とする。メンバー・呼び出し・添字と同じ優先順位（第13章のlevel 1）で左から連鎖し、前置演算子より強く結合する。他の`@`操作の優先順位は変えず、その完了した式にも後置操作を連ねられる。

| 表記 | 結合 |
| --- | --- |
| `r@deref@ref` | `(r@deref)@ref` |
| `r@ref@deref` | `(r@ref)@deref` |
| `r@deref.field`、`r@deref[i]` | `(r@deref).field`、`(r@deref)[i]` |
| `try r@deref` | `try (r@deref)` |
| `-r@deref`、`-r@deref.x` | `-(r@deref)`、`-((r@deref).x)` |

`@deref`に型引数、型指定、`?`、`during`は付けない。専用操作としてそこで構文を確定し、後続の`/`は除算とする。通常の借用・Moveも後ろへ連ねられる。安全な参照の前置`*`、`@reRef`、`@reUniq`は追加しない。既存のraw pointerの`*`とUnsafe規則は別であり、raw pointerに本操作を適用しない。

### 2.4. objectのpayload

`obj/T`・`rc/T`・`arc/T`・`objref/T`・`objuniq/T`の`@deref`は、完全なpayload型Tを証明できる場合だけ許す。正規化後のView TargetとTが内部Originまで一致し、`T is Sealed`が証明済みでなければならない。openなView、実行時Contract View、基底部分、最適化による実型の推測では代用できない。

ハンドルのスロットとpayloadは別の場所だが、**所有経路はルートのlet／varとFieldの可変性を継承し、借用経路は参照が与える権限に従う**。所有する`obj/T`のpayloadの更新・排他借用には書き込み可能な所有経路を要求する。`objuniq/T`は借用権限内で排他アクセスを、`rc/T`・`arc/T`・`objref/T`は共有アクセスを提供する。共有経路から排他権限は得られず、payloadはTakeを提供しない。

完全なpayloadを通常の`uniq/T`として借りることは、T全体の置換を許すことでもある。不完全なViewへの直接代入だけを禁止し、参照を別関数へ渡した置換を許す抜け道は作らない。

この規則を`@deref`、`@objref`／`@objuniq`、明示的なobject upcast、暗黙receiver借用へ共通適用する。`let h: obj/T`はpayloadも更新・排他借用できないが、`h@move`で所有権を移すことはできる。`let r: objuniq/T`はrを再代入できなくても参照先を更新できる。書き込み可能なスロットへMoveした後は、その新しい所有経路に従う。

所有ハンドルのスロットへのLoanは、その所有payloadにも必要な保護を及ぼす。hを共有借用している間にhのpayloadを排他更新するなど、別経路による競合を許さない。ハンドルのinline格納とpayloadを一つの物理領域とは扱わず、共有所有の参照カウント操作などの既存規則は維持する。

object Viewの形成・upcast・ObjectCallCompatibleの条件は維持する。`h@objref/Base`などの型指定はViewのupcastを許す一方、値借用の型指定は同じスロットの型一致を検査する（§3.1）。View借用だけでは完全payloadの置換を許さず、値借用には`h@deref@ref`／`h@deref@uniq`を使う。借用や投影は参照カウントを増やさない。

### 2.5. 場所を選ぶときの参照経路の省略

メンバー・添字・メソッドのreceiverでは、宣言と型から定まる参照経路の省略を許す。安全な参照の現在の層に対象がなければ直近の参照先で探索を続け、見つかった層で確定する。取得・適合・Loanの検査に失敗しても別の層へ戻らない。objectのメンバー投影は既存のView条件に従う。

```kimi
func update(node: uniq/Node)
    node.count += 1       // receiver経路を省略してFieldを選ぶ。
    node.validate()       // 選択した宣言に従ってreceiverを借用する。
```

同じ経路選択を、`for`の借用入口のreceiver（§7.1）と、構造を要求するPattern（§7.2）にも用いる。Scalar readと比較は§3.3で定める終端を選ぶ。receiverは対象宣言、Patternは必要な構造を見つけた層で止め、すべての操作を無条件に終端までたどらせない。各層の権限・Origin・Loanを保ち、共有経路を越えて排他権限を回復しない。

総称本体では、公開制約・型等式で正規化しても形状が未確定の型引数や関連型を経路の終端とする。その地点では公開された能力だけを使い、具体化後に参照層・探索先・比較方式を選び直さない。宣言済みの等式で参照形状が証明できる場合は、その既知の経路を定義時に選べる。これはreceiver・列挙入口・Pattern・Scalar read・比較に共通する。

裸の通常取得や単独の名前の束縛では参照を外さない。`@ref`／`@uniq`は省略の対象でなく、必ず直前のスロットを借りる。値receiverも§3の取得規則に従い、経路を見つけただけでは非Scalarの参照先を値取得しない。更新要求の伝播も投影経路に限り、`matrix[f()][j] = value`の`f()`の内部や、通常の関数・getterの背後まで伝播させない。

## 3. 値の取得と暗黙適合

### 3.1. 通常取得と明示操作

Place Pの格納完全型をTとする。

| 操作 | 結果 |
| --- | --- |
| Placeの通常取得 | TがCopyと証明できる場合だけCopy。結果型はT。Non-Copy・Copy未証明はエラー |
| `P@move` | Copy能力によらずTをMoveし、元の場所を未初期化にする |
| `P@ref`、`P@ref/T` | P自体の共有借用。結果は`ref/T` |
| `P@uniq`、`P@uniq/T` | P自体の排他借用。結果は`uniq/T` |
| 一時値の通常取得・`@move` | 取得済みの値を転送する。余分なCopy／Moveをしない |

型を明記した借用でも、指定型はPの格納型と一致しなければならない。参照先の選択、同型参照のCopy、再借用へ読み替えない。外側の借用Originは経路から推論し、格納型の内部Originは保つ。

一時値への明示借用は通常の寿命の一時Storageを作って同じ操作を行う。`@deref`も一時の参照・適格なハンドルへ適用できるが、必要な所有者の寿命を延長しない。

```kimi
// r: uniq/Node
r@ref                 // ref/(uniq/Node)
r@ref/uniq/Node        // 同じスロット借用
r@deref@ref            // ref/Node
// r@ref/Node          // 格納型が一致しないためエラー
```

通常取得は初期化、代入元、値引数、値returnに共通とする。次節以降の適合が指定される文脈では、先にその取得計画を決め、二重取得しない。許可した適合がない場合、Copy未証明を暗黙Moveや自動借用へ切り替えない。

Placeからの移動を明示する記法は`@move`だけとする。`@owner`、`@obj`、`@rc`、`@arc`やその型指定形はMoveの代用にならず、成立する同型取得・既存の明示適応を通常の取得規則で行う。所有表現の生成・複製は既存の明示APIによる。

明示的なByValue Subjectと、所有Iteratorなどから既に配送された値の分配は、§7.2の所有分解とする。取得済みの所有権を各束縛へ転送する段階では、束縛ごとに`@move`を書かせない。

### 3.2. 期待型が確定した値位置の共通適合

引数、型注釈付き初期化子、代入元、値return、集約要素・enum payload、既定値と、確定したTarget Result Typeを受ける結果源に共通の適合を使う。結果源にはif／matchのarm、yield、exit、単一式本体を含み、既存§14.9.1の伝播先に限る。Place結果は§5.2に従う。

期待型は宣言・構造・既存の共通型推論から独立に確定させる。未知の共通型を借用やScalar readから逆算せず、期待型のない値取得は§3.1に従う。例えば`ref/i32`と`i32`の結果源が混在し共通型を決められない場合は、結果型の注釈か明示した`@deref`を求める。通常の関数・関数値・Contract呼び出し、推論・明示型引数・別名展開で適合規則を変えない。Uは完全な直接の参照先型である。

| 入力 | 期待型 | 操作 |
| --- | --- | --- |
| 読めるowner/UのPlace | `ref/U` | 新しい共有借用 |
| owner/Uの一時値 | `ref/U` | 一度実体化し、共有借用 |
| `ref/U` | `ref/U` | 共有参照のCopyと許可されたOrigin適合 |
| `uniq/U` | `uniq/U` | 使用範囲に必要な排他再借用 |
| `uniq/U` | `ref/U` | 共有再借用 |
| 読める所有objectハンドル | 対応する`objref/U` | 共有object借用 |
| `objuniq/U` | `objuniq/U`／`objref/U` | 対応する再借用 |
| 安全な値参照の層の終端がScalar U | U | §3.3のScalar read |
| 上記の適合を必要としない値・Place | その完全型 | §3.1の通常取得・明示Move・一時値の転送 |

同型の取得済み一時値はそのまま転送する。同型のCopy・再借用という表の操作は、既存の参照Placeに対して行う。新しく返された参照値や`@uniq`の結果を保存するためだけに、一時の参照スロットへ依存する再借用を作らない。

一時値の共有借用もこの表で扱い、引数だけの特例にはしない。所有する一時Storageの寿命は既存の完全式・構文の規則から決まり、参照の保存では延びない。`let view: ref/Node = makeNode()`の後でviewを使う場合や、その借用をreturnする場合は拒否し、失効する一時値と使用箇所を診断に示す。呼び出し全体で使い終える集約内の借用などは同じ寿命検査で許す。

所有Placeから`uniq/U`／`objuniq/U`への新しい借用は自動生成せず、`@uniq`／`@objuniq`を要求する。receiverだけは選択済みの宣言が要求する借用を暗黙に行える。借用は実際の使用範囲で検証し、注釈・代入・returnを理由に寿命や権限を増やさない。

```kimi
func work(node: uniq/Node)
    validate(node)       // validate: ref/Node。共有再借用。
    normalize(node)      // normalize: uniq/Node。排他再借用。
    let transferred = node@move // 参照値自体を移す。

var node = Node.init()
validate(node)           // 所有値から共有借用。
normalize(node@uniq)     // 新しい排他借用は明示。
let view: ref/Node = node // 型注釈付き初期化にも同じ共有借用。

let result: i32 = match optionalNumber
    .Some(let number) => number // ref/i32を確定した結果型へScalar read。
    .None => 0
```

適合は表の一つの操作と通常のOrigin適合から決め、借用・Scalar readなどを任意に連結して探索しない。Scalar readだけは一意な参照経路をたどる。参照・ハンドルのスロットへ層を追加する暗黙借用、objectから値payloadへの暗黙投影は行わない。

明示操作は先にそのまま実行する。`node@ref`で作ったスロット借用を参照先の借用へ訂正しない。`node@move`は参照値自体を移し、同型の期待型へは再借用せずその値を渡す。後続の適合があっても元のスロットのMoveは取り消さない。所有ローカルを借りて返すなど、cleanup後に無効になる結果は拒否する。

### 3.3. Scalar readと比較

Scalarは整数、浮動小数点、bool、charの閉じた集合とする。string、Unit、Never、Tuple、任意のCopy struct、参照、raw pointerは含まない。公開の型名・制約名`Scalar`は追加しない。

**Scalar Tを必要とする位置では、`ref`／`uniq`の層をたどり、終端のTをCopyしてよい。参照を格納した変数の型は変えない。** §3.2の値位置と、組み込み演算・bool条件などに適用する。演算子は参照層の終端のScalar型から演算を決められる。未知の総称型をScalarと推測しない。

```kimi
for number in numbers        // numbers: Array<i32>。number: ref/i32。
    let reference = number   // ref/i32をCopy。
    let snapshot: i32 = number
    let doubled = number * 2 // i32。
    total += number

for number in numbers@uniq   // number: uniq/i32。
    number@deref = number + 1 // 更新先は明示、右辺はScalar read。

for number in references     // references: Array<ref/i32>。
    total += number          // ref/(ref/i32)の終端のi32を読む。
```

次の境界を維持する。

- たどるのは安全な値参照の層だけ。`ref/(ref/i32)`も読めるが、object・raw pointer・Fieldを自動でたどらない。各層の初期化・権限・Loanを検査する。
- i32からi64などの数値変換を追加しない。未確定literalは相手のScalar型へ既存規則で適合する。Scalarごとの演算可否、overflow、短絡評価は既存どおり。
- 読むたびにその評価時点の値を取得する。束縛時のsnapshotにはしない。
- 代入・複合代入・増減の更新先を参照先へ切り替えない。`number += 1`で参照先は更新しない。
- 元の参照のLoanを消去・短縮せず、Scalarの取得結果には新しい借用を残さない。
- 非ScalarのCopy値の通常取得は、必要なら`r@deref`で明示する。比較には次の共有観察規則を使う。

#### 3.3.1. 比較で観察する場所

`==`、`!=`、`<`、`<=`、`>`、`>=`は、各オペランドの安全な値参照（ref／uniq）を終端までたどり、その型へ既存§13.4の比較を適用する。参照をたどるだけでは値を取得せず、選択後のCopyまたは共有観察は既存の比較規則に従う。終端型の一致、literal適合、比較能力、Tuple内の比較順序・短絡、浮動小数点の規則を維持する。Contract比較の意味を組み込み比較へ置き換えない。

object・raw pointerは自動投影せず、終端型自身の既存規則で扱う。参照の層ごとの有効性を保ち、必要な検査Loanは選んだ場所を保護する。左右の評価順序とLoanの開始・終了は既存どおりであり、右辺の副作用で左辺を無効化できない。

例えば`T is Equatable`の総称コードで`ref/T`同士を比較するなら、既知の一層を選んでTのEquatableを使う。Tが具体化後に`ref/f32`でも、そのContract比較を追加の参照追跡や浮動小数点の組み込み`==`へ変更しない（§2.5）。

```kimi
// names: Array<ref/string>。nameはref/(ref/string)。
for name in names
    if name == "admin" => found = true
// r: ref/(ref/(i32, i32))にも r == (0, 0) を許す。
```

### 3.4. 型推論と候補選択

**型の推論と取得計画を分離する。** 型を調べるだけではCopy・Move・借用を実行しない。

1. 明示型引数を束縛し、receiverと引数の元の完全型から残る型引数を推論する。裸のTには元の型を使い、`ref/T`など宣言にある層は§3.2の許された対応で照合する。複数引数の制約を一括して調べ、走査順でTを固定しない。
2. 独立して既知の期待結果型は、通常の型照合で未確定部分を補える。借用・Scalar readを逆算して未知の型を選ばず、既に得た型を変更しない。Originの推論・短縮は通常の規則に従い、内部の束縛済みOriginは保つ。
3. 仮引数型を正規化し、§3.1–3.3から取得計画を決める。借用型へ渡す既存参照は総称呼び出しでも再借用し、参照値自体の消費には`@move`を使う。

型注釈のないローカル初期化は通常取得である。例えば`r: uniq/Node`に対する`let saved = r`はエラーであり、`let saved: uniq/Node = r`は再借用、`let saved = r@move`は転送となる。

```kimi
// n: ref/i32、r: uniq/Node。
// identity<T>(value: T) -> Tの本体はreturn value@move。
let a = identity(n)        // T = ref/i32。
let b = identity<i32>(n)   // 固定されたi32へScalar read。
let c = identity(r)        // T = uniq/Node。引数は排他再借用。
// cが依存する間、rの競合する使用は不可。
let d = identity(r@move)   // cの使用終了後。参照値自体を転送し、rはMove済み。
```

候補の適用可能性を取得計画とともに調べ、実際の取得は選択後に一度だけ行う。優先順位は、追加適応のない適合、literal適合、同じSemanticsの再借用、異なるSemanticsの借用・再借用またはScalar readの順とする。最後の区分内で変換の好みを追加しない。参照仮引数が要求する再借用を、同型だからという理由で暗黙Moveへ置き換えない。

```kimi
func process(value: Node)
func process(value: ref/Node)
// 以下は独立した呼び出し例。
process(node)       // NodeがCopyなら値渡し版、Non-Copyなら共有借用版。
process(node@move)  // 値渡し版。nodeの所有権を明示的に移す。
process(node@ref)   // 共有借用版。
```

Copyの証明、必要な明示Move、Takeなど型・構文・経路から決まる条件は適用可能性で検査する。裸のNon-Copy Placeを値渡しする候補は適用不可であり、その候補の追加によって共有借用を暗黙Moveへ変えない。選択後の初期化状態・Loan違反では候補を選び直さない。`uniq/T`を`ref/T`の部分型とはしない。入れ子の呼び出しは共有された期待型だけを使い、候補ごとの本体再解析は行わない。

総称本体でも、裸のPlaceの通常取得にはCopyの証明、所有権の転送には`@move`または取得済みの一時値を要求する。既存参照への適合などは公開制約から記号的に検証する。Copyが不明なTを具体化後にCopy／Moveへ振り分けない。候補の優先関係を含めて証明できなければ定義時エラーとし、具体化後に候補を選び直さない。

Copy適合の追加・条件変更は、裸のPlaceに対する値渡し候補を適用可能にし、呼び先を変え得る。総称本体で固定した呼び先と、具体型で直接解決した呼び先が異なることもある。これは取得可能な候補間の値渡し優先の帰結であり、CopyでPatternの束縛型を変えない規則とは別である。借用を固定したい呼び出しには`@ref`を使う。

### 3.5. 結果の適合とoverload

候補の公開結果型・結果カテゴリが確定したら、**その利用文脈で許される取得・適合**を候補の適用可能性に含める。通常値、Place、Scalar readに同じ手順を使い、結果変換の少なさで候補を順位付けしない。

| 利用位置 | 既知の結果に適用する規則 |
| --- | --- |
| 期待型が確定した値位置・結果源 | §3.2の共通適合。構文ごとの寿命を別途検査する |
| 期待型のない値位置 | §3.1の通常取得 |
| Placeを必要とする位置 | 場所の型・権限・Originを検査。通常値をPlace返却へ実体化しない |
| 明示操作・破棄 | その操作自身の規則。捨てるだけではUnit期待型を与えない |

この検査は確定した宣言情報だけを使う。未知の結果Tを、取得後の型から`ref/U`などへ逆算しない。Placeの公開型も借用変換から推論せず、Origin・Loanは実際に選んだ取得に従う。

```kimi
// それぞれ正当な本体を持つoverloadの署名。
func read(x: i32) -> ref/i32
func read(x: i64) -> i32

// let value: i32 = read(1) // 両結果が適合するため曖昧。
let value: i32 = read(1@i32) // 引数で選択後、結果をScalar read。
```

## 4. 初期化状態・更新・後始末

### 4.1. 操作の成立条件

| 操作 | 必要な条件 |
| --- | --- |
| Copy | Read、Complete、完全型がCopy |
| 共有借用 | Read、Complete、有効な場所と依存関係 |
| 排他借用 | Read・Write、Complete、排他権限 |
| Move | Take、Complete、追跡できる所有経路 |
| Initialize | 未初期化、その宣言・状態での初期化権限 |
| Replace | Write、旧状態を合法に破棄でき、新旧の依存を壊さない |

全操作でアクセス条件、Origin、Loan競合を検査する。初回のlet初期化や構築中の初期化は専用の権限であり、WriteやMoveから導かない。Moveしたletの再初期化は不可。未初期化領域を安全な`uniq/T`として借りてから初期化することも不可。

Takeを追跡する場所は、所有ルート、許可されたinline Field・Tuple要素、範囲内整数リテラルによる固定配列要素とする。借用先、動的コレクションの要素Place、関数が公開したPlaceにはTakeを与えない。`remove`や所有Iteratorは、コレクションの状態と破棄責任を更新する契約で要素を取り出す。

未知のTの通常取得も結果型はTであり、PlaceならCopyの証明を要求する。`@move`はCopy能力によらずTakeを要求する。型をT／ref/Tの族へ変える`SharedReadResult`や、未知Tの暗黙Moveは使わない。

### 4.2. 部分MoveとCompleteness

Moveは対象を未初期化にし、Initializeは復元する。親全体を使わずに選べる残存部分は利用できる。全体の読み取り・借用・Move・全体を受け取る呼び出し・destructorの実行直前には、その対象のCompletenessを要求する。

```kimi
// Box.valueは変更可能なResource。Boxのdestructorは完全なBoxを要求する。
var box = Box.init(Resource.init())
let saved = box.value@move  // 部分Move。
// inspect(box)            // Box全体の共有借用なので不可。
box.value = Resource.init() // 直接Initialize。親全体はまだ借りない。
inspect(box)               // 復元済みなので可。
```

祖先にdestructorがあるだけで部分Moveを拒否しない。ただし、必要な地点へ至る**すべての経路**で復元を証明する。通常経路、return、try伝播、ループ終了（既存のexit）、continue、defer、部分構築のcleanupを同じ状態解析に含める。将来の代入予定や最適化を証明に使わない。

例えば`box.value`を取り出した後、復元値の取得に`try`を使うなら、その失敗returnでもBoxを破棄できなければならない。Abortは巻き戻さず、発散先の実行されないdestructorも要求しないが、通常の失敗値の返却はこの例外ではない。

後始末は既存のスコープ順に従う。ローカル宣言とdefer登録は合成した順の逆、一時値は生成順の逆とし、再初期化でローカルの破棄位置を変えない。inline部分は宣言・要素順の逆、部分構築の中断は配置・取得の完了順の逆とする。独自destructorを自動的な部分破棄より先に呼ぶ。完全Move済みの値は破棄せず、独自destructorのない不完全集約は残る初期化済み部分だけを破棄する。内部のdestructorやdeferによる復元も実行順どおりに検査する。

### 4.3. 代入と複合代入

`a = b`は、bの評価・取得、aの場所の特定、旧状態の合法な破棄、配置の順に各一回行い、Unitを返す。aが未初期化ならInitializeする。旧状態の破棄は、新しい値や保持中の借用を無効にしてはならない。`var a`の`a = a@move`は、旧値を読まずに場所を特定できれば復元になる。

`a += b`なども**右辺先行**とする。b、aの場所、演算に必要な旧値の取得、演算、配置の順であり、aを再評価しない。旧値の取得は選択済み演算子の契約と共通適合に従い、暗黙Moveを追加しない。Non-Copy値を消費して作り直す更新には、`a = transform(a@move)`などの明示Moveを使い、再初期化と途中のCompletenessを検査する。借用先にはTakeがないためMoveできない。

場所と依存は配置まで保護するが、未初期化になり得る場所へ安全な参照を強制生成しない。取得・演算・破棄が通常完了しなければ後続処理を行わず、それまでの副作用を巻き戻さない。演算結果が置換で失われる旧状態へ依存する場合は拒否する。増減も更新先を一度特定し、通常の読み取り・計算・更新を行う。

computed／custom／required Propertyは宣言されたget／setの呼び出しであり、隠れたFieldへ直接アクセスしない。本書はユーザー定義算術演算子を新設せず、将来の演算子もこの取得・評価順序に従う。

## 5. Placeを返す関数と添字

### 5.1. 結果カテゴリと契約

```text
FunctionResult := Type
                | 'place' '(' ('ref' | 'uniq') ',' Type ')' ['during' OriginAtom]
```

`place(ref, T)`／`place(uniq, T)`のTは**格納された完全型**、ref／uniqは場所の公開方法を表す。共有形式は共有借用とCopy可能な値の読み出しを、排他形式はさらに排他借用・置換を提供する。どちらもCompleteな実在Storageを公開し、Takeは提供しない。`place(owner, T)`、`place(T)`、`place(ref/T)`は設けない。

例えば`place(ref, Node)`はNodeの場所、`place(ref, ref/Node)`は参照値を格納した場所を共有で公開する。後者への`@ref`は`ref/(ref/Node)`となる。

Placeは関数の結果カテゴリであり、Semanticsや値型ではない。関数宣言、明示結果のある匿名関数、Function Type、Callable、Contract要求で同じ構文を使う。未注釈の匿名関数からPlace結果を推論しない。

結果位置の非修飾名`place`に`(`が続く場合だけ専用構文とする。空白で意味を変えず、失敗時に型名へ戻らない。直後の`during`はPlace結果に結合する。内部型のOriginは別である。

返却元の実型Aと公開型Tは、Semantics・Core・型構造が一致しなければならない。Originの差は対応する`ref/A`から`ref/T`、または`uniq/A`から`uniq/T`への通常の適合で検査する。不変位置を緩和せず、実際のLoanを保つ。排他Placeを共有結果へ制限して公開することは可能だが、逆は不可。関数型同士の暗黙の結果モード変換は追加しない。

```kimi
func first<T>(values: ref/Array<T>) -> place(ref, T) during values
    return values[0]       // 値を取得せず、場所を返す。

let view = first(resources@ref)@ref
inspect(view)
```

### 5.2. return・Origin・関数境界

Place結果のreturn／単一式本体は場所を指定し、通常の値取得をしない。直接のPlaceまたは互換なPlace返却呼び出しを認める。終了するローカル・値引数のスロットは返せないが、ローカル参照から到達する外部Storageは返せる。通常の値結果を暗黙に実体化してPlace返却を成立させない。

各通常完了経路で場所を返す。Neverは結果を要求せず、到達不能な構文の型検査は維持する。fallthroughのUnitは`place(ref, ())`も満たさない。複数候補は分岐ごとにreturnし、if／match／do自体をPlace生成式にする拡張は行わない。

外側Originの名前導入・量化・省略は、対応する通常の参照結果と同じとする。省略時は直接の借用入力の外側Originの共通範囲を使い、receiverだけを優先しない。該当入力がなければ共有結果だけ既存のstatic既定を使い、排他結果には明示した有効なOrigin契約を要求する。いずれも本体の依存を検査する。検索keyへ依存しないアクセサは`during self`と明記する。

`during self`はreceiverの借用Originであり、self変数のスロットではない。`during self.source`などの注釈だけで実際のreceiver Loanを消去しない。Function Type／CallableのOrigin導入・注釈制限を迂回せず、特殊化も元の契約を継承する。

結果モードだけが違うoverloadは共存できない。公開型Tは引数・明示型情報・期待されるPlace契約などから確定し、§3.5で利用位置との適合を検査する。`@ref`が外側にあるだけでPlace返却候補を優先しない。

関数参照、Callable、Contract実装、間接呼び出し、別コンパイルで、結果モード・権限・Origin・Loan契約を保持する。通常の型引数RにPlaceモードは束縛できない。値結果の高階APIへ渡す場合は、通常の参照を返す明示ラッパーを使う。

関数契約の適合では同じ結果モードと対応する参照契約の適合を要求し、入力の反変性、Origin量化、Unsafe条件、Closure環境の既存規則を維持する。

### 5.3. Placeの利用と公開上の制限

通常の値利用では§3に従う。Non-CopyのPlaceを型注釈なしで取得するとCopyできないためエラーとなり、Place結果にはTakeもないので`@move`でも取り出せない。参照が必要なら`@ref`を使うか、確定した共有参照の期待型へ暗黙借用する。

```kimi
let view = table.get(key)@ref // 場所を一回検索し、参照を保存。
inspect(table.get(key))      // ref/Resource引数へ共有借用。
// let value = table.get(key) // ResourceがNon-Copyなら不可。
```

Placeを先に値へ取得しない文脈は、Placeのreturn、明示借用・Move、投影、Subject取得、更新先である。未取得のPlaceを捨てる文脈では必要な呼び出しと副作用だけを行い、その格納値を破棄しない。一方、`_ = expression`は通常の値取得と破棄を要求するため、Non-Copyの借用Placeを捨てる手段にはならない。

排他のT全体を公開するAPIは、有効な任意のTによる置換を許す。内部不変条件をこれで壊せる場所は公開しない。Dictionaryのvalueは公開できるが、keyを自由に置換できる排他Placeは公開しない。

### 5.4. Indexable

#### 5.4.1. 単一要素の場所を選ぶ契約

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key)
        -> place(ref, Element) during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key)
        -> place(uniq, Element) during self
```

KeyとElementは完全型である。検索引数は共有借用し、Non-Copyのkeyを消費しない。整数などの小さいkeyも同じ契約とし、実体化にヒープ確保を要求しない。参照・objectハンドルなど、§3.2でスロットの暗黙借用を許していないKeyには`key@ref`を使う。

両契約のElementは同じ型であり、同じkeyが同じ要素を選ぶ。`UniqIndexable`は要素のplaceへの排他アクセスを提供する契約であり、格納型を`uniq/Element`へ変えない。

添字の選択は次の順とする。これは静的な解決手順であり、実行時の評価順序を変えない。

1. receiverとkeyから`Indexable<Key>`の適合とElementを確定する。結果への期待型からKeyを逆算しない。
2. 利用側の取得・更新計画から必要な権限を決める。§3.2の暗黙の排他再借用も投影経路へ要求を伝える。
3. 読み取り・共有借用ならindex、更新・排他借用なら同じ適合のindexUniqを選ぶ。後者には`UniqIndexable<Key>`を要求し、経路・初期化・Loanを検査する。

```kimi
// refs: 書き込み可能なArray<uniq/Node>。
normalize(refs[0]) // 引数型uniq/Node。indexUniqでスロットを選び、格納参照から再借用。
```

共有経路しかなければ排他要求は失敗する。通常の関数・getterの宣言済み結果モードや内部処理は変更せず、適合・権限・Loan違反から別のモードや候補へ選び直さない。

receiver・keyは通常の呼び出し順で各一回評価する。代入では§4.3により右辺が先である。keyを含む全入力のLoanを呼び出し中保持し、結果はreceiverの契約に依存する。key自体の借用へ依存する結果は、この契約を満たさない。

#### 5.4.2. 標準の添字操作

aはreceiverの借用Origin、sはSliceの外部sourceとする。要素の内部Originは別に保持する。

| 対象・操作 | 入力と取得 | 結果 | 依存先 |
| --- | --- | --- | --- |
| Array<E>・固定配列の要素 | isize／Indexを共有借用 | 読取は`place(ref, E)`、更新は`place(uniq, E)` | a |
| Dictionary<K,V>の要素 | Kを`ref/K`として借用 | 読取は`place(ref, V)`、更新は`place(uniq, V)` | a。検索keyには依存しない |
| Slice<E>の要素 | isize／Indexを共有借用 | `place(ref, E)`のみ | s |
| Array<E>・固定配列の範囲 | Range／ResolvedRangeを通常取得 | 通常値の`Slice<E>` | 元の要素領域 |
| Slice<E>の範囲 | Range／ResolvedRangeを通常取得 | 通常値の`Slice<E>` | s |

Array・固定配列はKeyがisizeとIndexの各`UniqIndexable`に、Dictionaryは`UniqIndexable<K>`に、SliceはisizeとIndexの各`Indexable`に適合する。型引数で区別した適合と関連型の同定は§6.3による。要素の範囲外・key不在はAbortし、`tryGet`は`Option<ref/E>`などの通常値を返す。

Sliceの要素用indexは共有receiverを取り、具体的な公開結果を`place(ref, E) during self.source`とする。これは要求の`during self`より強い保証であり、外部sourceへの経路も検証する。具体的なSliceの添字ではこの保証を使う。`S is Indexable<Key>`だけの総称コードでは、要求の`during self`を越える保証を仮定しない。他のSlice操作のハンドル取得は既存規則を保つ。

範囲添字は単一要素用Indexableに含めず、既存のSlice生成とする。生成したSliceを置く一時Storageは、元コレクション内のPlaceではない。範囲全体の代入・排他Sliceは追加しない。

直接の固定配列要素は、同じ入力・取得規則で選ぶ組み込み投影であり、全体を借りる関数呼び出しへ置き換えない。範囲内整数リテラルなら§4.1のTake経路を保持し、部分Move後の残存要素の利用・欠けた要素の再初期化もできる。抽象的なIndexable適合と一般のPlace結果はTakeや未初期化Storageを公開しない。Fieldの標準get／set権限も維持する。

## 6. 完全型・関連型・静的契約

### 6.1. 完全型の比較と保持

総称引数、Self、関連型はすべて完全型を表す。Semanticsの追加は層の合成であり、既存の層を上書きしない。束縛済み型の代入・関連型射影・別名展開でOriginを再束縛したりstaticを補ったりしない。

| 判断 | 内容 |
| --- | --- |
| 型形状 | 宣言の同一性、全Semantics層、型引数、長さ、構造。Originを除く |
| 完全型の同値性 | 上記にOriginの束縛・量化・条件の対応を加える |
| 使用上の適合性 | 部分型・Origin短縮と、取得・権限・初期化・Loanの成立 |

型同士の`T is U`は完全型の同値性を要求する。関連型の実装を同定する際も、型形状の一致だけで値の置換を認めない。「型形状」は同形の別宣言を同一視する構造的型付けではなく、既存のnormalized Type identityや実行時型同定の再定義でもない。証明には宣言された等式・outlives・交差と限定された構造規則を使い、任意の論理探索を要求しない。Unknownを成功やNon-Copyへ置き換えない。

### 6.2. Origin引数を持つ関連型

```text
associate Item(step)                    // 要求の宣言
associate Item(step) is E               // 所有値など
associate Item(step) is ref/E during step
associate Item(step) is uniq/E during step
associate Item(step) is ref/E during source
```

stepは関連型宣言が導入する仮Origin、sourceは囲む環境で束縛済みのOriginである。仮Originのスコープはその関連型宣言の右辺と付属する制約節に限り、兄弟のメソッド宣言へ漏らさない。メソッド署名の同名Originは別の束縛であり、`Self.Item(step)`で明示的に適用する。重複名・隠蔽は既存のOrigin名前規則に従う。

型文脈の`Item(a)`は、仮Originへ既存のOrigin式を位置順に代入した通常の完全型である。引数には`during`と同じOrigin atomを使い、交差は`Item((a and b))`のように括弧で囲む。宣言・適用とも一つ以上の引数を要求し、個数を一致させる。未適用・部分適用・`_`・引数省略は許さない。名前の変更では別の契約にせず、実行時の呼び出し・型計算・Place型にもならない。

#### 6.2.1. 形成条件の宣言と検査

新しい関連型要求の定義域には、囲む契約と付属する`origin`関係節を使う。右辺を完全型に固定する要求は、その公開された右辺のOrigin形成条件も自動的に含める。`is Iterator`などの能力制約は完全型の指定ではない。

右辺を完全型に固定しない要求では、直下に**`wellformed Type`**を一段下げて記述し、必要な型のOrigin形成条件を公開できる。wellformedはこの節だけの文脈語であり、型構造・Semantics・能力・長さの成立は囲む公開制約から先に証明する。Copyなどの能力を推論せず、既存の型形成検査を再利用する。Place結果はTypeに指定できない。

```kimi
// Eは囲むContractの完全型引数。
associate View(a) is ref/E during a

associate Item(step)
    wellformed uniq/Self during step
```

前者は右辺からE内部の観察可能なOriginがaをoutliveする条件を得る。後者はItemの型を固定せず、Selfの借用型が形成可能なstepを定義域とする。既存Originを名前で指定できる場合は、同じ節へ`origin source outlives a`などを付けられる。形成条件はLoanやアクセス権限を生成せず、実際の借用時に別途検査する。

付属節は既存§15.3.3と同じ順に処理する。まず右辺の未束縛Originを等式などで束縛し、その完全型を確定する。例えば`Borrowed<Self>{view}`と`origin view.source == a`は、未束縛のsourceをaへ定める定義である。set名を付けただけではOriginを作り直さず、既に束縛済みの型は再束縛しない。

新規要求では、確定した右辺と明示条件から定義域を公開し、使用側が代入後の条件を証明する。実装・継承要求の精緻化では、右辺の束縛後に残る条件と形成義務を継承した定義域から証明する。右辺や本体を理由に呼び出し条件を追加・強化しない。右辺も追加条件もない要求は、囲む契約が許すすべての仮Originを対象とする。閉じた矛盾、循環した自己証明、Unknownの成立扱いは拒否する。

IteratorのItemには`wellformed uniq/Self during step`、Iterable／UniqIterableのIteratorTypeには対応する`ref/Self`／`uniq/Self during source`を記述する。標準型も同じ仕組みで扱い、返す型の内部依存を保持する。関連型の共変性は仮定せず、共有Originの短縮はoutlives、排他借用の短縮は適法な再借用に従う。条件・束縛・型形成義務は通常の静的メタデータに保存し、Origin引数で実行時表現を増やさない。

Contractの精緻化でも、`associate Parent.Item(a) is E`で継承した関連型の等式を指定できる。aはこの等式の仮Originであり、親の定義域にあるすべてのaについて成立しなければならない。Eがaを使わなければ、Itemはその引数に依存しない。この等式だけで実際のLoanの独立性を証明したことにはならない。

#### 6.2.2. binding-set記法との境界

役割を構文位置で確定し、空白・名前解決の結果・型検査の成否で切り替えない。

| 構文 | 意味 |
| --- | --- |
| `associate Item(a)`の宣言・実装見出し | 仮Originの導入 |
| 型文脈の`Self.Item(a)` | 既存Originの適用。解決先に対応する仮引数を要求する |
| 名前付き型の`View<T>{v}` | 既存§15.3.1のbinding-set命名。非空で既知のschemaを要求する |
| `Self.Item(a){v}` | 適用済みの型のbinding-set命名。内部schemaが既知の場合だけ許す |

型文脈の名前付き型に続く`(...)`だけをOrigin適用の接尾辞として追加する。単独の`(...)`による型の括弧・Tuple／Function Typeと、値文脈の関数呼び出しは既存どおりであり、相互に読み替えない。通常型へのOrigin適用、引数の誤り、未適用の関連型の族はエラーとする。型引数・別名へ未適用の族を渡さず、適用済みの合法な固定別名は既存の命名規則に従う。`{...}`をOriginの位置引数へ解釈する規則は設けない。

### 6.3. 要求の同定と実装照合

Contractにも完全型の型引数を許す。`Indexable<Key>`のように宣言・参照・親Contract指定・適合指定へ同じ型引数構文を使い、既存の総称束縛規則に従う。独立のOrigin引数や長さ引数は追加せず、内部Originは完全型と囲む環境から保持する。

要求は定義元Contractの宣言Identityと、型引数を含む束縛済み環境で識別する。`Indexable<isize>`と`Indexable<Index>`は別の適合であり、各Elementは独立して指定する。同じ要求への複数経路は、関連型・Origin条件・実装対応が同値の場合だけまとめる。独立した同名要求は同じ型を返しても同一視しない。

完全修飾は`T.(Contract).Item(a)`とし、要求内の短縮名はその要求を指す。外部の短縮名は通常メンバーを優先し、要求が一意な場合だけ使える。名前・対象適合・実装宣言を先に確定し、結果型やLoan違反を理由に選び直さない。

既存の使用側の`T.Contract.Item`はこの完全修飾へ統一する。次段落の`associate Contract.Item`はSelfの実装指定であり、使用側の型投影とは区別する。Origin引数の例示表記は`Item(a)`に統一し、空白の有無で意味を変えない。

型本体での関連型指定は`associate Contract.Item(a) is Type`とする。要求が一意ならContract修飾を省略できる。aは実装側の仮Originであり、要求の仮引数と位置で対応する。IterableとUniqIterableなど、独立した同名要求はそれぞれ修飾して指定する。

既知の要求・明示指定・実装署名の型等式から一意に決まる情報だけ省略できる。署名の照合にはreceiver、完全型、Origin引数、結果カテゴリを含める。本体、暗黙適合、Copy判定、具体化時の偶然から推論しない。複数の要求の推論結果が一致しなければエラーとする。

実装は要求が許す全入力を受け入れ、要求以上の結果保証を与える。Originだけの違いで別の適合を選ばず、同じ対象と束縛済みContractに重複する実装を許さない。総称の適合宣言同士が重なる場合は、既存の限定された証明規則で非重複または同じ継承要求の一致を示せなければ拒否する。公開適合の型・条件・実装対応は有効アクセス範囲を満たす。完全型一般への適合ブロックやreceiver引数の並べ替えは追加せず、既存の名目型の適合宣言を使う。

## 7. SubjectとPattern

### 7.1. Subjectの取得

`for`と`match`は式を一度評価し、最外側の指定でモードを決める。括弧は分類を変えず、その指定を二重適用しない。内側の操作は通常どおり評価する。

| Subject | モード | forの入口 |
| --- | --- | --- |
| `E`、`E@ref` | Shared | Iterable.iterate |
| `E@uniq` | Exclusive | UniqIterable.iterateUniq |
| `E@move` | ByValue。Copyでも強制Move | IntoIterable.intoIterator |

型を明記した外側のスロット借用も、型一致を検査した上で同じモードになる。変数がvarか、Copyか、適合があるか、本体が何をするかからモードを変更しない。裸の一時値もSharedとし、内部所有変数へ保存して構文の寿命まで保持する。

`for`のShared／Exclusive入口は、取得した対象をreceiverとして§2.5の経路選択を行う。`values: ref/Array<T>`に対する`for value in values`はArrayの共有入口を呼べる。モードは変えず、明示借用は入口探索より先に直前のスロットに対して成立しなければならない。

例えば`let values: uniq/Array<T>`に対する`values@uniq`は、letスロットの排他借用なので不可である。参照先の排他列挙には`values@deref@uniq`を使う。varの参照スロットを明示借用した場合も、その後の参照経路に共有の層があれば排他入口へ到達できない。

ByValueは取得した完全型のIntoIterableを使い、参照先の所有権を取得する入口へ読み替えない。`r@move`は参照自体の転送である。`match`はSubject全体の参照を自動的には外さず、構造Patternが要求する場所だけを§7.2で選ぶ。

### 7.2. 確定後の束縛

Patternは場所を選び、選択が確定してから左から順に新しいローカルを初期化する。

| 選択した場所 | 束縛する値 |
| --- | --- |
| 取得済みの所有Subject／itemの部分 | 格納完全型Tの値と破棄責任を束縛へ転送する |
| 共有借用されたTの場所 | `ref/T`。Copy型でも同じ |
| 排他借用されたTの場所 | 権限があれば`uniq/T` |
| `_` | 取得しない。破棄責任は現在の所有者に残す |

`let`／`var`の違いは新しいスロットの再代入権限だけである。共有・排他のSubject全体を束縛する場合も元の対象を借り、内部の管理用参照スロットへの余分な層を作らない。

Case・Tuple・Unit・Literalの構造Patternでは、現在の型に必要な構造がなければ安全な値参照の直近の参照先を選び、必要な構造に達するまで繰り返す。型から一意に決め、選択後の失敗から別の層へ戻らない。object・raw pointerはこの省略に含めない。Tuple／enumの子にも同じ規則を再帰適用し、`ref/(ref/())`にもUnit Patternの`()`を使える。

参照先を選んだ枝では、その経路の借用権限で部分を束縛し、借用先からMoveしない。所有itemが`ref/(A, B)`なら成分は共有借用、`uniq/(A, B)`なら排他借用となる。途中に共有経路があれば共有を上限とし、明示的なExclusive要求を満たせない場合は拒否する。Literal Patternは選んだ値を観察し、単独の名前・Wildcardには参照追跡を適用しない。

所有分解は、明示したByValue Subjectや配送済みitemの所有権を配分する操作であり、裸のNon-Copy Placeを取得する例外ではない。未束縛部分の破棄責任は内部所有者に残る。

### 7.3. guardの共有候補

guardでは各候補の格納型Tに対し、**候補名を`ref/T`の共有参照として公開する**。Copy能力やSubjectのモードで型を変えない。候補名はguard専用のlet相当の参照スロットを持ち、本体の束縛とは別のBinding Identityとする。共有armの束縛と型構造は一致し、外側Originはそれぞれの使用範囲に従う。

```kimi
match optionalNumber
    .Some(let number) if number > 0
        // guardも本体もref/i32。演算・条件ではScalar read。
        total += number
    _ => ()
```

候補名への代入・Move・直接captureと、候補Storageへの排他操作は禁止する。通常の参照層の規則を保ち、`candidate@ref`はguard用の参照スロットを借りる。格納値をCopyするなら`candidate@deref`、元の候補場所を借り直すなら`candidate@deref@ref`とする。格納型が`ref/U`なら候補名は`ref/(ref/U)`であり、自動的に平坦化しない。

候補参照と、それから新しく作る候補依存のLoanはguard外へ持ち出せない。Scalarのsnapshotや、`candidate@deref`でCopyした格納済み外部参照は、候補から独立していれば通常の依存規則で保存できる。間接呼び出しや別の値へ包んでも制限は変わらない。

Pattern検査からguardのcleanupまでCaseと候補場所を保護する。falseならguardの参照スロット・一時値・Loanをcleanupして次のarmへ進む。trueなら同じcleanup後、元の場所から§7.2で本体の束縛を作る。副作用を巻き戻さず、guard中にpayloadをMoveしない。

## 8. Cursor・Iterator・for

### 8.1. 二つの役割

Iteratorは次のItemを通常値として配送するAPI、Cursorは現在位置のPlaceを繰り返し公開する任意のライブラリAPIとする。`for`はIteratorだけを呼び、Iterator実装にCursorを要求しない。Cursorでは進めずに同じ位置を参照し、必要に応じて共有・排他のアクセスを選べる。

```kimi
contract Cursor
    associate Element
    func advance(self: uniq/Self) -> bool
    func current(self: ref/Self) -> place(ref, Element) during self

contract UniqCursor: Cursor
    func currentUniq(self: uniq/Self) -> place(uniq, Element) during self

contract Iterator
    associate Item(step)
        wellformed uniq/Self during step
    func next(self: uniq/Self during step) -> Option<Self.Item(step)>
```

Cursorは作成直後とadvanceがfalseの後にcurrentを持たず、current／currentUniqはAbortする。trueの後は次のadvanceまで同じ論理的要素を選ぶ。false後のadvanceで再びtrueを返してもよい。これは実装が守るAPI契約であり、コンパイラに状態機械の自動証明や実行時フラグの挿入は要求しない。要素の借用と競合するadvance・更新・破棄は通常のLoan検査で拒否する。

Iteratorは各nextのreceiver LoanとOriginをItemへ代入する。一般のIteratorにはNone後も終了状態を保つ義務を課さず、`for`は最初のNoneで終了し、以後nextを呼ばない。§8.4の標準IteratorとCursorアダプターはNone後もNoneを返す。Owned／Borrowedは元のIteratorの保証を引き継ぐ。所有する未返却要素は通常のcleanupで破棄し、借用したコレクション自体は破棄しない。

| Itemの定義 | 保持と次のnext |
| --- | --- |
| `E` | 値自身の所有権・内部依存に従う。呼び出しからの独立性は別に検証する |
| `ref/E during step`、`uniq/E during step` | そのnextのreceiver借用に依存し、保持中は次のnextと競合する |
| `ref/E during source`など | receiverからの独立性を証明した外部借用。必要なsourceの保護は残る |

型注釈だけでreceiver依存を除去しない。`I is Iterator`だけの総称コードは、保持したItemと次のnextが競合しないとは仮定できない。独立した結果を要求する追加契約は§8.5で定義する。NoneにはItemが存在しないため、存在しないpayloadのLoanを保持しない。

所有する数値を返す最小例は次のようになる。Itemのstepを使わないことも正当な定義である。

```kimi
struct Countdown
    Self is Iterator
    associate Iterator.Item(step) is i32
    var remaining: i32 = 3

    public func next(self: uniq/Self during step) -> Option<i32>
        if self.remaining == 0 => return .None
        self.remaining -= 1
        return .Some(self.remaining)
```

### 8.2. 列挙元の三つの契約

```kimi
contract Iterable
    associate IteratorType(source) is Iterator
        wellformed ref/Self during source
    func iterate(self: ref/Self during source) -> Self.IteratorType(source)

contract UniqIterable
    associate IteratorType(source) is Iterator
        wellformed uniq/Self during source
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType(source)

contract IntoIterable
    associate IteratorType is Iterator
    func intoIterator(self: Self) -> Self.IteratorType
```

三つの能力は独立しており、選択した入口に適合がなければエラーとする。借用版は呼び出しごとのsourceを保ち、所有版は消費したSelfの内部依存を保つ。消える引数スロットやローカルを借用して返せない。

UniqIterableはreceiverをuniqで借りる入口であり、Itemが排他参照であることや要素の変更可能性までは保証しない。UniqIndexable／UniqCursorでは個々の要求が排他Place結果も規定する。いずれも実際の型・権限は名前から推測せず、要求署名で決める。

要素型は選択したIteratorTypeの`Iterator.Item(step)`で決まる。外側の列挙モードからItemのSemanticsを強制しない。同名メソッドによるduck typing、IteratorからIterableへの自動導出、参照型への自動列挙適合は行わない。

Iteratorと列挙入口の同時適合は禁止しない。標準Iteratorと§8.6の各アダプターは、自身を転送するIntoIterableと、`Borrowed<Self>`を返すUniqIterableに明示的に適合する。`for x in it@uniq`はIteratorを借りて進め、途中終了後に残りを再利用できる。`it@move`はIteratorの所有権と残部のcleanup責任を移す。全要素を必ず消費する保証ではない。

ユーザー型には自動適合を与えず、同じ実装か§8.6のアダプターを使う。例えばCountdownの型本体に次を追加する。裸の一時SubjectをByValueへ変える特例や、共有借用でIteratorを進める標準適合は設けない。

入口を型自身へ追加しない場合も、`for x in Kimi.Iteration.owned(it@move)@move`、または`for x in Kimi.Iteration.borrowed(it@uniq)@move`で使える。この用途だけの導出Contractや、Contractの既定本体は追加しない。

```kimi
// Countdownの型本体へ追加する宣言。
Self is IntoIterable
Self is UniqIterable
associate IntoIterable.IteratorType is Self
associate UniqIterable.IteratorType(a) is Kimi.Iteration.Borrowed<Self>{view}
    origin view.source == a
public func intoIterator(self: Self) -> Self
    return self@move
public func iterateUniq(self: uniq/Self during source)
    -> Kimi.Iteration.Borrowed<Self>{result}
    origin result.source == source
    return Kimi.Iteration.borrowed(self)
```

### 8.3. forの実行

1. Subjectを§7.1で一度取得し、選択した入口を一度呼ぶ。
2. Iteratorを内部のvarに保存し、その完全型のIterator要求へ短い排他借用でnextを呼ぶ。
3. Noneなら終了。Someならpayloadをその反復の所有された内部itemとして一度取得する。
4. itemへ§7.2の所有分解を適用する。裸の名前はlet、var付きの名前は書き込み可能な反復ローカルとする。Tuple形式には§7.2の参照先選択も使うが、一般のmatch Patternは追加しない。
5. 本体終了・continueでは反復のローカルと残るitemをcleanupして次へ進む。
6. 終了・return・try伝播では、Iterator、内部所有Subjectなどを生成の逆順でcleanupする。

Itemが参照なら、単独束縛はその参照値を転送する。`for`はItemへ参照層を追加せず、Copy能力による型の切替もしない。varは反復ローカルの再代入を許すだけで、参照先への権限を増やさない。Wildcardもnextを省略せず、未取得のitemを反復終了時に通常どおりcleanupする。

```text
ForBinding := ForSlot | '(' List<ForSlot> ')'
ForSlot    := Name | 'var' Name | '_'
```

Nameは通常の束縛名とし、同じForBinding内で重複させない。`var _`は許さない。

外へ保存されたLoanを反復終了で強制的に終わらせない。次のnextと競合すればループの戻り経路を拒否する。早期終了では保持する結果の依存を保護してからcleanupし、Move済みSubjectを二重破棄しない。

```kimi
for var number in numbers@move // i32
    number += 1                // ローカルだけを更新

for (a, b) in rows@uniq      // a・bはuniq/i32。
    a@deref += b

for (a, b) in pairs          // pairs: Array<(i32, i32)>。
    total += a + b           // ref/Tupleを分解し、a・bはref/i32。

for (key, value) in dictionary@uniq
    inspectKey(key)          // ref/K
    update(value)            // uniq/Vを引数として再借用
```

### 8.4. 標準コレクション

aは列挙元の借用Origin、sはSliceの外部sourceとする。E、K、Vは完全型である。

| 列挙元 | IterableのItem | UniqIterableのItem | IntoIterableのItem |
| --- | --- | --- | --- |
| Array<E>、固定配列 | `ref/E during a` | `uniq/E during a` | `E` |
| Dictionary<K,V> | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K,V)` |
| Slice<E> | `ref/E during s` | 同左 | 同左 |
| ResolvedRange | `isize` | `isize` | `isize` |

Array／固定配列は添字順、Dictionaryは挿入順、ResolvedRangeは既存の区間順とする。Rangeそのもの、object形式、raw pointerへの標準適合は追加しない。Sliceの排他列挙はハンドルへのアクセスであり、共有の要素を排他へ変えない。Slice／区間の局所ハンドルに不要な依存を残さず、実際のsource依存は保つ。

Eが`ref/Node during b`なら、共有Itemは`ref/(ref/Node during b) during a`、排他Itemは`uniq/(ref/Node during b) during a`となる。内側のNodeへの排他権限を作らず、自動的に層を平坦化しない。

標準の借用Array／固定配列／DictionaryのItemはstepに依存しない。排他版は§9.1.1で領域を分割するため、前の要素の借用だけを理由に次のnextを妨げない。外部効果は§8.5に従う。所有Iteratorは残る要素を所有し、取り出した要素の責任だけを結果へ渡す。

### 8.5. 独立したItem

```kimi
contract IndependentIterator: Iterator
    associate StableItem
    associate Iterator.Item(step) is StableItem
```

`Kimi.IndependentIterator`は、**取得済みItemを保持したまま次のnextとIteratorのMoveを行える**ことを保証する標準Contractとする。StableItemはstepに依存しない配送値の完全型であり、値の不変性を意味しない。格納要素を指すIndexable／CursorのElementとは区別する。`for`は引き続きIteratorだけを使う。

適合には、すべての有効なstepと許された型束縛について、次を要求する。

1. Itemの完全型はstepに依存しないStableItemである。
2. 返却値は、nextのreceiver LoanやIterator自身のStorageに依存しない。外部source・所有者・必要な親Loanへの依存は保持する。
3. 分割した排他領域では、返却済み結果と残部のanchorが非重複である。Iterator自身のStorageを移動・解放しても返却済み領域を無効化しない。
4. **nextの効果は、同じIteratorの返却済みItemが保持するLoanと競合しない。** 読み書き、借用、結果のLoan形成、static・captureへのアクセス、その呼び出し内のcleanupを含む。返却済みItemから通常の転送・再借用で維持されたLoanも対象とする。IteratorのMoveはこの保証を引き継ぐ。

第4項をnextの公開Effect契約の上限とし、既存のroot・Loan・効果要約と§9.1の分割で適合時に検証する。すべての実装・特殊化と呼び出し先の効果を含め、証明できない実装は適合を公開できない。型等式や長いOriginだけではこの保証を得られない。専用の実行時台帳や一般の効果指定構文は追加しない。

総称コードはこの上限をrequirementの公開要約として使い、実装本体を再解析せず、保持中のItemに対する後続nextを検証できる。これは純粋性の保証ではない。Itemと無関係な呼び出し側のLoan、明示した別の操作、結果の寿命は通常どおり検査する。

標準コレクションの§8.4に掲げた全Iteratorはこの契約に適合する。nextは検証済みの走査・分離・転送だけを行い、利用者の比較・destructor・callbackを呼ばない。ユーザー型は`Self is IndependentIterator`で要求し、検証に成功した場合だけ公開できる。単なるIterator適合から自動導出しない。

```kimi
func nextPair<I>(iterator: uniq/I)
    -> (Option<I.StableItem>, Option<I.StableItem>)
    I is IndependentIterator
    let first = iterator.next()
    let second = iterator.next() // 公開された効果上限により、firstを保持できる。
    return (first@move, second@move)
```

**Iterator全体の破棄・置換は、nextとは別の効果である。** 残部のdestructorが返却済みItemの借用先を更新する場合、その破棄は拒否する。Move先の旧値の破棄もMove自体の保証に含めない。標準の借用Iteratorのcleanupは借用ハンドルだけを終了し、所有Iteratorのcleanupは残る要素型の破棄要約を使う。

したがって、借用したIteratorからItemを蓄積する総称処理は書けるが、所有引数を最後に破棄する`collect<I>(it: I)`の全経路まで、この契約だけで許可するものではない。所有する版には、返却結果とcleanupが非競合である証明も必要である。未知の破棄効果を空と扱ったり、本体から隠れた条件を追加して具体化時へ検査を遅らせたりしない。

結果の外部Loanによる元コレクションの保護は、この契約でも終了しない。一般のIteratorを使う処理は、Itemを次のnextまでに使い終えるか、公開された依存契約から個別に非競合を証明する。

ユーザー型やアダプターへの委譲にも共通の投影・転送・効果要約を使う。自分自身を借りる結果や、返却済みItemと競合するnextの副作用を加えたラッパーは独立性を失う。別コンパイル・間接呼び出しへも上限と実際の依存を渡し、保証を失う型消去で新しい権限を得ない。

### 8.6. 標準アダプターとDrain

`Kimi.Iteration`に次の操作と結果型を設ける。IはIterator、Cは対応するCursorである。結果は通常の総称structで、Iteratorと§8.2の二つの入口（IntoIterable／UniqIterable）に明示的に適合する。

| 操作 | 結果型（同group内） | 契約 |
| --- | --- | --- |
| `owned(iterator)` | `Owned<I>` | Iteratorを値で受け取り、その`Item(step)`と依存を転送する。Non-CopyのPlaceは呼び出し側で`@move`する |
| `borrowed(iterator)` | `Borrowed<I>` | `uniq/I`を受け取って保持する。元のIteratorを進めるがMoveしない。ItemはIの契約を保つ |
| `shared(cursor)` | `Shared<C>` | Cursorを値で受け取り、advance成功後のcurrentを借りて`ref/Element during step`を返す |
| `uniq(cursor)` | `Uniq<C>` | UniqCursorを値で受け取り、advance成功後のcurrentUniqから`uniq/Element during step`を返す |

uniqはSemantics位置だけの文脈語であるため、`Kimi.Iteration.uniq(cursor)`は通常の関数呼び出しであり、明示借用の`@uniq`とは別の操作である。

BorrowedはOriginスロットsourceを持ち、入力の外側借用Originに結び付ける。各結果型はI／C内部の依存も保つ。borrowedのItemのstepは実際のnext再借用に対応する。OwnedとBorrowedは`I is IndependentIterator`の場合に限り同じStableItemでIndependentIteratorにも適合し、型等式・Loan依存・nextの効果上限を転送する。SharedとUniqはstepに依存するlending型であり、この追加適合を持たない。CursorアダプターはPlaceをOptionに入れず、通常の参照をpayloadにする。

Shared／Uniqは自身の終了状態を保持する。未終了ならadvanceを一度呼び、trueのときだけ対応するcurrentを呼ぶ。最初のfalseで終了状態に入り、その後はCursorを呼ばずNoneを返す。したがってCursor自身の終了後の挙動に依存せずfusedとなる。終了状態の表現は既存の位置情報と統合してよい。

```kimi
// iteratorはUniqIterableに適合する書き込み可能な所有変数。
for item in iterator@uniq
    inspect(item)
    exit // 残る要素を消費せず終了。保持中のLoanが終わればiteratorを再利用できる。
```

アダプター自体はヒープ確保・参照カウント更新・要素列の事前生成を要求しない。Drainは通常のIterator APIとして取り出し方と早期終了時の残部処理を公開し、第三の言語プロトコルにはしない。

## 9. 依存管理と性能保証

### 9.1. 保護の開始と終了

| 依存 | 保持するもの |
| --- | --- |
| 場所の保護 | 選択・転送・使用に必要なスロット、Storage、所有者 |
| 格納値の既存依存 | 内容の参照先・内部Origin・既存Loan |
| 取得が作る依存 | 新しいBorrow／Reborrowと親Loan |

入力取得からcallee終了まで通常のcall-wide保護を維持する。結果の場所・値を確保した時点で必要な保護を成立させ、calleeのdefer・引数／ローカル破棄、返却、呼び出し側の投影・取得まで切れ目を作らない。

独立したCopy値やScalarを取得した後は、取得だけに必要だったスロット保護を残さない。格納済み共有参照のCopyも、参照先の公開Originと実際の依存を保つが、取得元スロットのためだけの新しいLoanは残さない。借用結果には必要な所有者・親Loanを保持する。

Originが等しくてもLoanを合一せず、共有結果になっても元の排他保護を勝手に弱めない。複数の返却元は依存候補を保守的に合成し、公開契約より長い寿命を復元しない。

一時値の寿命は通常の文脈別規則に従い、参照を保存しただけでは延長しない。`makeTable().get(key)@ref`も同じである。Subjectの内部所有変数だけは構文の寿命を持つ。callの予約・活性化では実際に残る依存を検査し、活性化を通すため一時値を早く破棄しない。

#### 9.1.1. 共通の領域分割

借用領域の分割は、既存§15.6.3の再借用として、親Loanから複数の子Loanを作る操作である。排他の子同士と残部のanchorには非重複の証明を要求し、共有の子同士は重複してよい。親経路の競合アクセスは停止したままとし、子の有効性を親参照の変数の寿命へ結び付けない。親Loan・所有者の保持には既存の依存規則を使う。

新しく加えるのは**動的な非重複の証明元**である。異なるinline Field・Tuple位置などは既存の構造規則、標準Storageの未取得領域は§9.1.2の検証済み操作の契約を使う。別々の添字・keyや長いOrigin注釈だけでは証明できず、通常のSliceのLoanも自動分割しない。一般の整数定理証明や実行時Loan台帳は要求しない。

分割先は有効な初期化済みStorageで、境界・配置・provenanceが正しくなければならない。結果を公開する前に残部の権限を更新し、返却済み部分への権限を残部から再生成しない。サイズゼロは物理アドレスでなく論理位置で区別する。Dictionaryでは一entryを分離してからkey・valueの異なる部分を共有・排他で借りるため、同一性を守るkeyへの排他権限は公開しない。

Iteratorや残部ハンドルの破棄は、返却済みの子Loanを終了させない。元コレクションの競合する読み書き・再確保・Move・破棄は、保持する結果が必要とする間は拒否する。要素の内部依存や外部効果は、領域の非重複とは別に通常検査する。

#### 9.1.2. 標準Storageの内部API

検証済みの内部APIを、Kimi直下の**`internal group Storage`**へ置く。型と操作もinternalとし、既存§9.3により同じKimi Kotonohaだけから利用できる。公開の`Kimi.Intrinsics`とは分離し、別名・再公開・同名宣言でアクセスや組み込みの権限を得られない。

以下の三型は不透明なNon-Copy型とする。公開Iteratorはprivate Fieldとして保持できるが、公開結果・関連型にこれらの内部型を直接露出しない。利用者向けの排他Sliceやraw pointer変換は追加しない。

| 型 | 保持する責任 |
| --- | --- |
| `RefRemainder<S>` | Originスロットsourceの共有Loanと未取得部分の走査位置。S自体は所有しない |
| `UniqRemainder<S>` | Originスロットsourceの親Loanと未取得部分への排他アクセス。S自体は所有しない |
| `OwnedRemainder<S>` | Sから移されたStorageと未返却要素の破棄責任。S内部の依存も保持する |

Sは下表の三形式に限定する。各操作は表の形式ごとにE、K、V、必要なら`length N`を宣言したoverloadとして提供する。S、R、U、Oは署名をまとめる説明記号であり、未知のSへの汎用適合や新しい型関数ではない。

| S | 共有結果R(a) | 排他結果U(a) | 所有結果O |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `ref/E during a` | `uniq/E during a` | E |
| `Dictionary<K,V>` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` | `(K,V)` |

| 操作の署名テンプレート | 契約 |
| --- | --- |
| `borrowStorage(value: ref/S during a) -> RefRemainder<S>{r}` | `origin r.source == a`。共有Loanを保持し、先頭から走査する |
| `borrowStorage(value: uniq/S during a) -> UniqRemainder<S>{r}` | `origin r.source == a`。受け取った全体への権限を未取得部分へ移し、独立した全体アクセスを残さない |
| `ownStorage(value: S) -> OwnedRemainder<S>` | 取得したSのStorageとcleanup責任を転送する |
| `splitFirst(state: uniq/RefRemainder<S>{r}) -> Option<R(r.source)>` | 未取得の先頭を共有で借り、走査位置を進める |
| `splitFirst(state: uniq/UniqRemainder<S>{r}) -> Option<U(r.source)>` | 未取得の先頭を子Loanとして分離し、残部の権限を更新する |
| `takeFirst(state: uniq/OwnedRemainder<S>) -> Option<O>` | 未返却の先頭をMoveし、未初期化状態と破棄責任を更新してから返す |

借用結果はsourceに依存し、stateの短い借用・スロットには依存しない。共有版は排他的な領域分割を要求せず、結果同士の重複も安全性違反にはしない。走査順序と一要素一回の配送は共有版にも適用する。例えばKimi.Storage内のArray用署名は次になる。

```kimi
internal func splitFirst<E>(state: uniq/RefRemainder<Array<E>>{r})
    -> Option<ref/E during r.source>
internal func splitFirst<E>(state: uniq/UniqRemainder<Array<E>>{r})
    -> Option<uniq/E during r.source>
internal func takeFirst<E>(state: uniq/OwnedRemainder<Array<E>>)
    -> Option<E>
```

構築時に、借用または所有する対象のCompletenessを検査する。型の内部表現・構築・複製・初期化状態の変更は上記の操作だけに閉じ、空のハンドルを含めて一般のField操作から権限を偽造できない。splitFirst／takeFirstは空ならNone、非空なら先頭を一度だけ返す。順序は§8.4に従い、利用者のcallbackを呼ばない。OwnedRemainderは未返却部分だけを保持する有効な値であり、取り出し後もハンドル自体はCompleteである。穴を含むS全体への安全な参照は公開しない。

RefRemainder／UniqRemainderの破棄はハンドルが保持する権限だけを終了し、要素も返却済み結果のLoanも破棄しない。OwnedRemainderは未返却部分を元のコレクションのcleanup順序で一度だけ破棄し、所有する領域を解放する。返却済み要素を再び破棄せず、残部のdestructorの外部効果を通常の破棄要約へ公開する。借用先へTakeを追加する操作ではなく、不透明な所有状態に基づく取り出しである。

型束縛を固定したsplitFirst／takeFirstの管理処理は、Array／固定配列では一回O(1)、Dictionaryでは償却O(1)とする。空・終了後はO(1)、走査全体は開始時の生存要素数nに対してO(1 + n)を維持する。借用ハンドルの構築・破棄と追加管理領域もO(1)とし、capacity走査や参照配列の事前生成は行わない。所有する元Storageの大きさ、要素の転送・cleanupは別に数える。

Array／固定配列の共有・排他走査は、先頭と残数などで有効な未取得範囲を保持する。非空判定で次の一要素が有効と分かるため、公開indexの呼び出しや別の添字範囲検査を経由しない。空・終了判定は残し、サイズゼロも論理位置を進める。Dictionaryは内部の生存entryの順序情報を用いる。hash探索用のtombstoneと走査管理を区別し、償却という語でcapacityに比例する空き走査を許さない。

この限定した組み込み境界が、Storageの有効性、動的anchorの非重複、初期化状態の更新を保証する。型・Loan・効果は標準の宣言Identityに結び付け、同じ綴りを根拠に権限を与えない。標準Iteratorの残りの処理とユーザーの委譲実装は通常のLoan規則で検査する。任意のユーザーStorageへ同等の境界を登録する構文は、raw Storage APIの設計まで追加しない。

#### 9.1.3. 公開効果の伝達

通常の関数と同じく、結果のanchor・親Loan・残部の非重複、static・capture・cleanupの効果を静的な公開情報へ含める。呼び出し側はその情報を合成し、別コンパイルや間接呼び出しでprivateな本体を再解析しない。不明な効果は保守的に扱い、関数型変換で保証を失っても既存Loanを消去しない。Unsafeという指定だけで独立性や長いOriginを作れない。

IndependentIteratorのnextには§8.5の効果上限を含め、適合を使う総称呼び出しへ保持する。Iterator全体の破棄要約とは合算せず、各操作の実行地点で対応する効果を検査する。要素型に依存するcleanupは既存§4.7.5と同じ記号的要約を使う。

### 9.2. 表現と計算量

同じ型束縛・生成方式・関数契約の下で、`place(ref, T)`の結果ABIは`ref/T`、`place(uniq, T)`は`uniq/T`と同じとする。直接・間接呼び出し、Callable、Contractのentryとadapterも一致させる。結果モードを値型として同一視する規則ではない。

Placeの形成・一段の転送はO(1)とし、それ自体のためのヒープ確保、TのCopy、一時T領域、参照カウント更新を要求しない。検索、投影計算、実際の取得、利用者の処理の費用は別である。ゼロサイズに追加IDを要求せず、無条件のnoalias属性も付けない。

参照引数の最適化属性は既存§21.5.5の共通契約に従う。証明済みの`ref/V`・`uniq/V`には適切なreadonly・noalias等を使えるが、保証はVのinline格納に関するもので、読み込んだポインタの先やobject全体へ自動拡張しない。Place結果にも結果モードだけを根拠にnoaliasを付けない。

Scalarの`ref/Key`を物理的に値渡しする最適化は、アドレスの違いが観察されず、逸出・依存・評価順序を含む意味を保てると証明した場合だけ許す。結果型がkeyに依存しないことだけでは足りず、Unsafeでのアドレス観察を新たに未規定にはしない。直接・間接呼び出し、entry・adapterは共通のABIを維持する。

標準の借用Array／固定配列／Dictionary、Slice、ResolvedRangeのIteratorは、型を固定し、開始時の要素数をnとして次を満たす。

| 操作・資源 | 保証 |
| --- | --- |
| 生成・破棄 | O(1) |
| next | 償却O(1)。空・終了後はO(1) |
| 最初のNoneまでの走査 | O(1 + n) |
| Iterator自身の追加記憶域 | O(1) |
| 列挙のためのヒープ確保・参照カウント更新 | なし |

Dictionaryのnはcapacityでなく生きたentry数とする。開始時のcapacity全走査や要素参照配列の事前生成をしない。利用者が保持する結果、body、所有Iteratorの未返却要素の破棄は別に数える。任意のユーザーIteratorに同じ計算量を強制しない。

Dictionaryの削除に伴う通常の空き・挿入順・走査管理は償却O(1)とする。空きリストや生存entryの連結管理などは実装が選べ、削除ごとの物理的な詰め直しは要求しない。検索・利用者の処理・破棄の費用は別とし、再構築・成長・tombstone整理は既存§4.7.7の全体の償却上限に従う。疎になっても生存数に対する走査保証を維持する。

最適化は型・権限・Loan・評価順序・回数・観測可能なcleanupを変えない。Origin引数や検証情報に専用の実行時タグを追加せず、静的メタデータとして保持する。キャッシュは結果モード・適合・依存契約を区別し、変更時に無効化する。

#### 9.2.1. nonnull形式のOption表現

Rが`ref/T`・`uniq/T`・`obj/T`・`rc/T`・`arc/T`・`objref/T`・`objuniq/T`のいずれかなら、`Option<R>`の格納表現はポインタ幅の一語とし、nullをNone、Rの有効なnonnull表現をSomeとする。size・alignment・strideはRと同じであり、現行Windows x64では8バイトとなる。型別名は正規化して判定する。

これは既存の「enumはnicheを使わない」規則に対する限定した変更である。サイズゼロの参照先でもSomeには既存のnonnull代替アドレスを使う。Noneにはpayloadもその破棄・参照カウント責任もない。SomeはRの通常の所有権・Loan・cleanupに従う。

`Option<Option<R>>`や参照を含むTuple・structなどへ再帰的に一般化しない。他のenum／Optionは既存のtagとpayloadの表現を使う。構築・Case判定・配置・cleanup・生成キャッシュは同じ表現規則に従う。関数ABIは既存どおり生成方式が決めるため、格納サイズだけから戻り値のレジスタ数やメモリ返却を固定しない。

### 9.3. Itemを配送する一時領域

§8.3のOption payload、内部item、利用者の束縛は意味上の段階であり、それぞれに物理的な格納領域やT全体のコピーを要求しない。コンパイラが生成する一時領域を統合し、単独束縛ではnextの成功結果を束縛先へ直接配送できるようにする。

格納先への直接構築・転送は、通常の関数結果・集約構築にも使える共通の最適化とする。元の格納先が以後観察されず、必要なアドレスの安定性、論理的な場所、Loan、初期化状態、cleanupの順序・回数を保てる場合だけ行う。利用者が指定したコピーや借用の意味を、最適化の都合でMoveへ変更しない。

固定配列の`for x in array@move`にも同じ規則を使い、OwnedRemainderが移動元の物理領域を引き継いで、開始時の配列全体の転送を省略してよい。残部・束縛への転送とcleanupの責任は移す。Move済みという事実だけでは十分でなく、移動元の再初期化、アドレス観察、Iteratorの逸出を検査する。元の領域の有効期間を越える結果へスタックへのポインタだけを渡してはならず、条件を満たさない場合は通常の転送を行う。

Tuple分解は選ばれた成分を直接配送できる。Wildcardでもnextと必要な破棄は省略せず、未束縛部分の破棄責任を保持する。標準の所有IteratorとOwnedアダプターは、配送のためだけの要素配列・ヒープ領域を作らない。

### 9.4. 共通最適化の対象

具体型と呼び先が分かる場合は、次の共通最適化を標準Iteratorにも適用する。特定の機械命令や最適化の成功を言語の成立条件にしない。

| 対象 | 条件と目的 |
| --- | --- |
| iterate／next／adapter呼び出し | 通常のインライン化で状態更新を呼び出し側へ展開する |
| 境界検査 | nextの成功判定で得た範囲情報を再利用する。長さ・領域の不変性を証明できる区間だけで重複検査を除く |
| `ref/Key`の一時領域 | §9.2のアドレス観察・逸出・ABI条件を満たす場合に実体化を除く |
| Optionから束縛への配送 | §9.3に従い中間格納とコピーを統合する |

ユーザーIteratorにも同じ意味保存条件を適用する。nextの副作用、呼び出し回数、None判定、必要なcleanupをIteratorという名前だけで省略しない。lending型ではLoanが終わる前へ次のnextを移動しない。

## 10. 正式仕様への反映と検証

### 10.1. 変更境界

| 領域 | 取り込み時に置き換える内容 |
| --- | --- |
| 型・取得（第3、10、13、15章） | Placeの通常取得はCopy、移動は`@move`。所有分解と配送済み値の転送を区別。確定した期待型・結果源の共通適合、多層Scalar read、取得可能な候補間の優先順位とCopy変更の影響を規定。§10.3の適合範囲を置換し、寿命検査では再選択しない |
| Field・配列・投影（第4、11、12、13章） | Indexable／UniqIndexable、検索引数借用・標準適合・範囲添字、暗黙再借用からのアクセサ選択。所有objのlet／var制約、payload保護、`@deref`と参照経路省略。§11.1・§4.7.7の旧境界を更新 |
| 演算子・receiver（§7.3、§13.1、§13.4ほか） | 所有receiverの明示Move、`@deref`の後置level 1。比較の多層参照追跡と共有観察。代入・複合代入の右辺先行と暗黙Moveをしない旧値取得 |
| 関数・契約（第7–10章、§15.3.1・§15.3.3–4） | Place結果。関連型のCore限定を撤廃し、完全型・Origin仮引数・形成条件・精緻化を追加。Origin適用は`Item(a)`、binding-set命名は`{name}`と構文で区別。新規要求の公開右辺から条件を求め、実装は束縛後に残る義務を検査 |
| Subject・Pattern（第14、15章、付録F） | 明示モード、一時Subjectの共有既定、Case・Tuple・Unit・Literalの参照追跡、共有束縛・guard候補。参照Tuple分解を統合し、ForSlotに`var Name`を追加。§14.9.1の結果源に共通適合を接続 |
| 寿命・cleanup（第5、15、16、17章） | 部分Move、全早期終了、結果の連続保護。既存の子Loanへ動的分割の証明元を追加。internalなKimi.StorageにRef／Uniq／Ownedの残部と操作を定義し、一般のraw pointer変換とは区別 |
| 標準列挙・効果（第4、12、15、22章ほか） | Iterator、Cursor／UniqCursor、三つの入口、StableItem。IndependentIteratorのnextの効果上限を適合時に検証し、総称呼び出しへ公開。全体の破棄効果は別途検査。標準型の二つの入口・fused・shared／uniqアダプター、Dictionaryの生存数に対する走査保証 |
| 表現・生成（第21章） | Place結果の参照相当ABI、nonnull Option表現、配送領域の統合・固定配列の移動元領域の再利用。既存の参照属性・アドレス観察・ABIを保持し、総称本体の経路・操作選択は具体化後も固定 |
| 保留項目・用語・文法（付録D／E／F） | 排他Subject、lending Iterator、共有Iterable、indexerの対象部分を保留から外す。用語と関連型・形成条件・Place結果・後置操作・forの文法を同期。任意Storage用Unsafe境界は保留 |

既存の数値変換、object View・raw pointer・並行実行の規則は、明記した変更以外を維持する。Placeを返すcomputed Property、任意のraw pointerからの安全な参照生成、公開の排他Slice型、任意の演算子拡張は追加しない。

正式仕様へは本書の採用内容を自立した規則として取り込み、重複した旧規則・例・milestoneを更新する。実装制限で要求を弱めない。`STATUS.md`は検証済み支援範囲が変わった時点で更新し、取り込み時に`draft/INTEGRATED.md`へ記録して本書を凍結する。

### 10.2. 必須の検証例

| 分野 | 確認事項 |
| --- | --- |
| 取得 | 裸のNon-Copy・Copy未証明Placeを拒否、明示MoveとTake、一時値・所有分解の転送、型指定でも同じスロット |
| 暗黙適合・推論 | 値位置とarm・yield・exit・単一式本体の共通適合。期待型の有無、総称再借用、推論順序。一時借用の完全式内利用と保存・returnの拒否。値候補の優先、Copy適合の変更と総称内外の選択差、Loan違反で再選択しないこと |
| 投影・比較 | 多層Scalar readと更新先の区別、多層string・Tuple比較、Literal Patternとの整合、左右評価中のLoan、Contract比較。Sealed、let obj／let objuniq、payload保護、共有上限、`@deref`の結合と単回評価 |
| 添字 | Non-Copy keyの非消費、参照keyの明示借用、Sliceのsource、複数Key適合、固定配列の部分Move。`Array<uniq/Node>`の暗黙再借用がindexUniqを選ぶこと、共有経路・getter境界での拒否 |
| 更新 | 単純・複合代入の右辺先行、自己Move復元、旧状態依存、accessor境界、不完全な場所への安全な借用の拒否 |
| 部分Move | 全分岐での復元、return・try・continue・exit・defer・部分構築、内側destructor、Abortと二重破棄 |
| Place結果 | 借用元と内部Origin、複数return、Never、Unit fallthrough、一時receiver、cleanup、Callable。既知の値／Place結果の適合、Scalar結果overloadの曖昧さ |
| 関連型 | 仮Originのスコープ・個数、`Item(a)`と`{name}`の構文上の区別、新規要求の右辺からの形成条件。右辺の未束縛Originの定義と残る義務、完全型の再束縛・実装による定義域縮小・未適用の族の拒否 |
| Pattern | 多層参照のTuple／enum／Unit／Literal、単独名では参照を外さないこと、共有上限、guardの型とLoan持出禁止、false guard・Wildcard。positiveOrZeroを確定したi32期待型で検査 |
| Iterator・Cursor | refの裸Subject、letスロットのuniq拒否、for var、モードとItemの独立性。標準Iterator・アダプターのUniqIterableで途中終了・再開、IntoIterableの残部cleanup。一般Iterator／Cursorの再開と、標準Iterator／Cursorアダプターのfusedを区別 |
| 独立性・領域分割 | 総称nextPairと借用collect、競合するnext・特殊化の適合拒否、独立した破棄効果の検査。Ref／Uniq／Owned残部のLoan・空・サイズゼロ・順序・key保護・返却後の保護。Kimi外からの内部型利用・権限偽造の拒否、別コンパイルへの効果上限の伝達 |
| 総称の経路 | 未確定T・関連型での停止、公開等式で既知の参照層の選択、具体化後の追加追跡の禁止。ref/f32へ具体化したEquatable比較でNaNの意味を変えないこと |
| 表現・性能 | 参照相当ABI、nonnull OptionのSome／None・サイズゼロ・入れ子・cleanup、Scalar参照の観察と属性、大きいItemの直接配送、Tuple残部。固定配列の領域再利用と再初期化・逸出時の通常転送、共有走査の非空判定、疎なDictionaryの償却・全走査上限。測定は付録A |

診断は、要求した操作、対象経路、失敗条件を示す。型不一致、適合不足、アクセス権限不足、未初期化／Incomplete、Loan競合、Origin不成立を区別する。修正案は実際に成立する場合だけ示す。

### 10.3. 設計資料

以下は設計の出典であり、本案の規則の追加条件ではない。

- [Storage Provision and Projection](2026-09-25%20Storage%20Provision%20and%20Projection.md)
- [Complete Types and Static Contracts](../Changes/2026-09-24%20Complete%20Types%20and%20Static%20Contracts.md)
- [Shared Place Results](../Changes/2026-09-25%20Shared%20Place%20Results.md)

改訂時の採否と理由は[改善案の採否一覧](2026-09-26%20Places%20Borrowing%20and%20Iteration%20Review.md)に記録する。規則の本文は本書に集約する。

## 付録A. 実装・性能検証の方針

本節は実装作業の指針であり、実装言語を言語仕様への適合条件にはしない。Iterator・Dictionary・アダプターは可能な限りKimigayoで実装し、§9.1.2の低水準操作だけを必要な組み込み境界に置く。

§9.4の効果は、Scalar配列、密／疎なDictionary、lending Iterator、大きい所有Item・固定配列を分けて測定する。forと意味が等しい直接ループを比較し、O0／O2の結果・cleanup回数を照合する。共有・排他走査の非空判定と余分な添字検査、固定配列の開始時転送、呼び出し・中間格納、実行時間、追加確保を確認する。性能量は実測で記録し、文書だけで改善済みとは扱わない。
