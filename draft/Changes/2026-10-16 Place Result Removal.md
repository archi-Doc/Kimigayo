# Place結果の廃止と参照結果への統一

日付: 2026-10-16（最終版）

状態: 仕様変更案。廃止方針は所有者が支持済み。導入時期は所有者の確認待ち（本書§1.3）。正式仕様への取り込み・実装・実行検証は未実施。

基準: `8ffc296b`（`dev`）。Place結果に関わるSPECの規則は`4e205c94`から変わっていない。[2026-10-15版](2026-10-15%20Place%20Result%20Removal.md)は`4e205c94`の行番号による調査記録として残す（`7d18e204`以降、第6章は44行目以降が2行、付録Fは370行目以降が1行ずれる）。内容が食い違う場合は本書を優先する。

「§n」は正式仕様の節、「本書§n」は本書の節を指す。

## 1. 概要

### 1.1. 変更内容

- 関数の結果は常に型とし、`-> place ref/T`と`-> place uniq/T`を廃止する。既存の記憶域は`ref/T`・`uniq/T`の参照値を返して貸す。
- `index`・`indexUniq`は参照を返す。適合を通した`receiver[key]`は、選択した呼出しが返す参照の参照先を表すPlaceとして残る。
- 一般の関数の呼出し側は、必要な位置に`@follow`を書く（本書§3.1）。
- 変数、フィールド、添字、参照先を表すPlace自体は変えない。

### 1.2. 理由と原則

現行のPlace結果は仕様として矛盾していない。結果カテゴリーとして定義され（§7.1.1）、関数をフィールドのように読み書きできる利点がある。それでも廃止するのは、一つの概念を一つの形に揃える利点の方が大きいためである。

- **原則1:** 既存の記憶域を貸す形が参照結果だけになる。関数型、Callable、適合判定、推論から結果モードが消える。
- **原則2:** 呼出しの意味が結果型だけで決まる。関数型の同一性と互換性も型だけで判断できる。
- **原則3:** 式そのものが呼出しの参照先を表すのは、もともとPlaceを表す添字式だけになる。一般の関数の参照結果は、`@follow`・参照経路の選択・値読み取りという既存の規則で参照先に届く（§3.3.3）。

### 1.3. 必要な判断

所有者の確認が必要なもの:

- **導入時期:** R6完了後、R7とマイルストーンの実施順（PLAN §3のP38 → …）の再開より前を既定とする（本書§5.5）。PLANへの記録は本書§7に従う。
- **前倒し:** R6完了前に行う場合は所有者が判断する。旧経路にはD5の例外か、R6まで残る一時構造が要る（本書§5.5）。R4中なら、R4の判断条件O3（R4計画の付録B外の受理の変更、マイルストーン・ライブラリーのプログラムの拒否）にも当たる。廃止方針への支持は、これらの承認を含まない。

不要なもの:

- 語彙と上限の変更。`HirPlaceKind`は11項目のまま。`BindingFailure`、`DiagnosticCode`、`ExpectedAdaptationKind`は1項目ずつ減る。削減に承認は要らない。
- SETTLEDの再開。Place結果を扱う項目はない。
- 仕様変更の個別承認。理由と原則の保ち方は本書§1.2に示した（AGENTS）。

## 2. 仕様

### 2.1. 規則1 — 関数の結果は型

関数宣言、クロージャー、関数型、Callable、Contract要件の結果は型である。結果を省略したときのUnitなど、既存の規則は変えない。

- 既存の記憶域を貸す関数は、共有なら`ref/T`、排他なら`uniq/T`を返す。返却、期待型に合うかの判定、推論、効果、Originは通常の値結果の規則に従う。
- 文法の`FunctionResult`と`PlaceResult`を削除し、各結果位置を`Type`にする。`place`は通常の名前になる。
- 関数型の同一性と互換性、オーバーライド、実装の照合は結果モードを比較しない。
- `index`・`indexUniq`の参照結果も関連型推論の証拠になる（§8.5.5）。添字専用の推論規則は作らない。

### 2.2. 規則2 — 参照先は既存の規則で選ぶ

参照を返す呼出し`f()`は参照値を生む。`f()@follow`はその参照先のPlaceを選び、格納値をCopy・Moveしない。

- `ref/T`の参照先はRead、`uniq/T`の参照先はReadとWriteを持ち、どちらもTakeを持たない（§13.5.5）。値の取得、参照層を通したメンバー選択、固定された期待型へのAdaptationは既存の規則に従う。
- すべての参照結果を暗黙に`@follow`する規則は作らない。`let r = f()`は参照を保持する。

寿命とLoanも既存の規則に従う。

- 参照先の借用は、参照先と親Loanに依存する。参照を一時的に保持したスロットには依存しない（§10.2、§13.5.6、§15.6.3）。
- `during self`の結果はレシーバの借用に依存する。`during self.source`などで外側の借用から独立できるのは、宣言と本体の検査がそれを証明する場合だけである。
- 共有参照は内側の`ref`のOriginを保てる（§10.2）。レシーバを通したフィールドの排他再借用はレシーバのOriginに含まれるので、`(self: uniq/Self) -> uniq/T during self.source`をそれで満たすことはできない。raw記憶域の分割は、unsafeの義務を負うraw Placeの`uniq`借用でこれを実装する（§15.5、§15.6.3）。

### 2.3. 規則3 — 添字式は選択した参照の参照先

単一要素の添字は次のContractを使う。

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
```

意味:

- 同じキーに対して、両要件は同じ要素への参照を返す。
- 適合を通した`receiver[key]`は、選択した呼出しが返す参照の参照先を表す。格納型は`Element`で、能力と依存はその参照に従う。
- 明示的な呼出し`receiver.index(key)`の結果は通常の参照値である。添字の意味は添字演算子だけが持ち、関数名や結果型では有効にならない。

選択は現行の§4.7.1（手順1〜3と続く段落）を保つ。

- 適合はレシーバとキーから一つ選ぶ。`Key`は期待する結果から推論しない。
- 読み取りと共有借用には`index`、更新・排他借用・排他再借用には`indexUniq`を選ぶ。
- レシーバの経路が必要な能力を持つか検査する。失敗しても、別の関数や候補を選び直さない。

依存:

- レシーバとキーは呼出しの間Loanされる。結果はキーに依存しない。
- 要素型の内部のOriginと実際のLoanは通常どおり保持する。

| 対象 | 適合を通した要素のPlace | 依存 |
| --- | --- | --- |
| `Array<E>`、`[N of E]`、`UniqSlice<E>` | `ref/E`または`uniq/E`の参照先 | レシーバの借用 |
| `Slice<E>` | `ref/E during s.source`の参照先 | `s.source` |
| `Dictionary<K, V>` | `ref/V`または`uniq/V`の参照先 | レシーバの借用 |

次は現行のまま保つ。

- 評価順序と評価回数、位置の正規化、境界検査、キーがないときのAbort。
- `a[i] = v`、`a[i]@ref`、`a[i]@uniq`、入れ子の添字と射影。
- 固定長配列の直接の要素は組込みの射影で、配列全体を借用しない（§4.7.1）。所有する固定長配列では、範囲内の整数リテラル添字だけが静的Move Pathを作り、Takeできる（§4.7.2、§15.1.3）。その他の添字の要素と、適合を通した要素はTakeできない（§15.1.5）。
- 範囲添字はスライス値を返す別の操作であり、変更しない。

### 2.4. 例

```kimi
func first<T>(items: ref/Array<T>) -> ref/T during items
    return items[0]@ref

func firstUniq<T>(items: uniq/Array<T>) -> uniq/T during items
    return items[0]@uniq

var values: Array<i32> = [10, 20]
let copied = first(values@ref)@follow  // i32をCopyする。
let read: i32 = first(values@ref)      // 値読み取り（§3.5.3）。
firstUniq(values@uniq)@follow += 1     // values[0]を更新する。
values[0] += 1                         // 添字の書き方は変わらない。
let kept = first(values@ref)           // ref/i32を保持する。
// firstUniq(values@uniq) = 5          // エラー: 代入先は参照値（uniq/i32）で、参照先へ転送されない。

var names: Dictionary<i64, string> = [:]
_ = names.tryInsert(1, "one")
let key: i64 = 1
let lent = names.index(key)            // ref/string: 明示的な呼出しは参照を返す。
names.indexUniq(key)@follow = "uno"    // 参照先を置き換える。
```

参照は通常の値なので、寿命の規則を満たせば`Option<ref/T>`などに格納したり、一般の高階関数へ渡したりできる。Place用の型やラッパーは設けない。

### 2.5. 仕様書への反映

| 節 | 変更 |
| --- | --- |
| §7.1、§7.1.1 | 結果を型だけにし、§7.1.1を削除する。§7.1に「既存の記憶域は参照値で貸す」段落（規則1・2）と例を置く |
| §4.7.1、§4.7.2 | Contractを参照結果にし、規則3の意味・依存・表・明示的な呼出しの例を書く。§4.7.2の要素Placeに「適合を通した場合は返された参照の参照先」と加える |
| §6.4.1 | オーバーライドの結果から「Place mode」を削除する |
| §8.4.1、§8.4.2、§8.5.4 | 例を新しい署名にする。要件の結果を「省略可能な結果型」にする。形成型の「Place結果ではない」を削除する |
| §8.5.5、§8.6.3 | 証拠を出さない結果から「Place結果」を削除する。実装照合のキーから結果カテゴリーを削除し、「結果で絞らない」に揃える |
| §8.10、§9.1 | 「`R`はPlace結果でもよい」を削除する。署名を区別しないものから「result modes」を削除する |
| §10.2、§10.3、§10.7、§10.8 | §7.1.1への参照、「Place required」の行、「`@ref`はPlaceを返す候補を優先しない」を削除する。§10.7の「required value result `Q`」を「required result `Q`」にする。§10.8の呼出しシグネチャ照合から「result shape」を削除する |
| §13.5.6 | ゲッター結果の文を「呼出しやゲッターが返す参照は値である」に広げる |
| §15.1.5、§15.6.1 | Takeを持たないものから「関数が公開するPlace」を削除する（借用の参照先に含まれる）。§7.1.1へのリンクを削除する |
| §22.1、付録E | Indexableの署名と説明を更新する。用語「Place result」を削除する |
| 付録F | `place`の文脈キーワードと「Place results,」を削除し、文法を規則1に合わせる（`FunctionType := FunctionParameters "->" Type`） |

- SPECJ、SPECJ2、LIBRARY（§2.4、§3.2、§3.2.1、§3.5）、STYLE §4.2、GUIDEを同じ内容に揃える。STYLEの「Index an existing element」行の結果は`ref/T during self`・`uniq/T during self`とし、続く「A Place result exposes storage…」の2文を参照結果の説明に改める。
- 英語の置換文は10-15版§9を出発点にし、本書の規則で上書きする。特に§4.7.1の表の要素の列は適合を通した要素とし、固定長配列の扱いは本書§2.3に従う。

## 3. 移行で変わる意味

### 3.1. 呼出し側

宣言から`place`を消すだけでは移行できない。下表の`f`は、Place結果から対応する参照結果へ変えた関数である。

| 旧コード | 宣言だけ変えた場合 | 元の意図を保つ書き方 |
| --- | --- | --- |
| `let x = f()` | 参照を保持する。旧はbare取得（Copyか再借用）で、それができない格納値ではエラー | `let x = f()@follow` |
| `f() = v`、`f() += v`（`uniq/T`） | エラー。代入先は参照値で、参照先へ転送されない | `f()@follow = v`、`f()@follow += v` |
| `f()@ref`、`f()@uniq` | 一時値の参照をさらに借用する（`ref/(ref/T)`など）。一時値の寿命を超えて使えない（§3.6） | `f()@follow@ref`、`f()@follow@uniq` |
| `f()@copy` | 参照をCopyする。`uniq/T`はCopyできない | `f()@follow@copy` |
| `f()@move` | 旧はエラー（Takeなし）。新は参照値のMoveとして有効 | 要素の取出しは引き続き不可。`f()@follow@move`もエラー |
| `match f()`、`for x in f()`（`uniq/T`） | Subjectが参照値になり、モードがSharedからExclusiveに変わる。`for`は`iterateUniq`を呼ぶ（§15.1.6、§14.6.2） | `match f()@follow`、`for x in f()@follow` |
| `_ = f()`（bare取得できない格納値） | 旧はエラー（§14.2.4）。新は参照値を捨てるだけで有効 | 意図を確認する。`_ = f()@follow`も引き続きエラー |

- 値として使う位置（`let x = f()`、値引数など）は`f()@follow`にする。格納値が`uniq/U`なら、これで旧と同じ再借用になる（§3.5.1）。期待型が読み取り型なら、値読み取りで足りる（§3.5.3）。
- 次は書き方も意味も変わらない: `ref/T`引数への受渡し、メンバー選択`f().x`・`f().m()`（§3.4.3）、呼出し`f()(x)`、`ref/T`を返す`match f()`・`for`のモード（Shared）と束縛の型。

```kimi
// counts: var Array<i32>
// 旧
func slot(values: uniq/Array<i32>) -> place uniq/i32 during values
    return values[0]
slot(counts@uniq) += 1

// 新
func slot(values: uniq/Array<i32>) -> uniq/i32 during values
    return values[0]@uniq
slot(counts@uniq)@follow += 1
```

### 3.2. 受理の変更

- `-> place ref/T`と`-> place uniq/T`はLanguageエラーになる。
- **結果だけで決まる型引数:** `get<T>() -> ref/T`の`T`は、期待する値型から逆算しない（§10.2、§10.3）。`get<i32>()`と書くか、期待型を`ref/i32`にする。入力から決まる呼出しは影響を受けない。
- **レシーバのLoan:** Place結果だけにあった「`during self.source`でも実際のレシーバLoanを残す」規則を削除する（本書§2.2）。参照元を壊せるようにする変更ではない。
- **関連型推論:** `index`・`indexUniq`の結果が新たに`Element`の証拠になる（本書§2.1）。

## 4. ライブラリーとマイルストーン

要素のPlaceを返していた本体は、`@ref`・`@uniq`で借用を返す。既に参照値を持つ本体は、それを返す（`ref/T`はCopy、`uniq/T`はMove）。§10.2は暗黙の排他借用を与えないので、`@uniq`のない`indexUniq`本体（現在のMilestone 28の`=> self.items[key]`など）はエラーになる。

```kimi
// Dictionary.kimi
public func index(self: ref/Self, key: ref/K) -> ref/V during self
    match self.tryGet(key)
        .Some(let value) => return value                      // ref/VをCopyする。
        .None => Storage.missingDictionaryKey()

public func indexUniq(self: uniq/Self, key: ref/K) -> uniq/V during self
    var state = Storage.borrowStorageUniq(self)
    loop
        match Storage.splitFirst(state@uniq)
            .Some((let storedKey, let value))
                if storedKey.equals(key) => return value@move  // Array.tryGetUniqと同じ形。
            .None => Storage.missingDictionaryKey()

// UniqSlice.kimi。indexPosition・indexPositionUniqは結果型だけを変え、本体は変えない。
public func index(self: ref/Self, key: ref/isize) -> ref/T during self
    require key >= 0 and key < self.length else => Storage.indexBounds()
    unsafe
        let element = self.storage[key]@ref
        return element

public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self
    require key >= 0 and key < self.length else => Storage.indexBounds()
    unsafe
        let element = self.storage[key]@uniq
        return element@move
```

- Indexable.kimiの要件の署名は本書§2.3のとおり。Placeを公開すると述べる文書コメント（`- return:`を含む）は、Dictionary.kimiも含めて参照を返す説明に改める。

Milestone 27:

```kimi
// A user Type lends its stored elements through the Indexable Contracts (SPEC 4.7.1).
public func index(self, key: ref/isize) -> ref/T during self
    if key == 0 => return self.first@ref
    return self.second@ref

public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/T during self
    if key == 0 => return self.first@uniq
    return self.second@uniq

// A generic Constraint borrows the element that indexing selects.
func firstPlace<S>(items: ref/S) -> ref/S.Element during items
    S is Indexable<isize>
    return items[0]@ref
```

Milestone 28:

```kimi
public func index(self, key: ref/isize) -> ref/E during self => self.items[key]@ref
public func indexUniq(self: uniq/Self, key: ref/isize) -> uniq/E during self => self.items[key]@uniq
```

- `firstPlace(counts) == 42`は参照層をたどる比較（§13.4）なので、書き換えない。期待出力も変わらない。
- 両方の書換えを、仕様変更を原因として`tests/milestones/README.md`に記録する（PLAN §5）。

## 5. コンパイラーの設計

### 5.1. 決定の所有者

- **必要な意味と対象の形:** 本書§2の規則1〜3（§7.1・§4.7.1）と、本書§3の各形。
- **原因を共有する箇所:** 10-15版§3・§6（`4e205c94`の行番号）にあり、本書§6.1の検索で更新する。
- **現在の表現の問題:** 一般のPlace呼出しでは、呼出し式の型が格納型、CallPlanの結果型が参照型になる。各所が結果モードと`IsPlaceCall`・`PlaceCallReference`で分岐し、同じ決定をFollowとPlace呼出しで2回行う。
- **変更:** 呼出しは常にCallPlanの結果型を公開する。添字の所有者だけが、選択した参照結果から要素のPlaceを決める。

| 所有者 | 入力 → 出力 |
| --- | --- |
| Parser | 結果位置 → `Type` |
| `Binding.Calls`・`Binding.ValueCalls` | 選択したCallPlan → その結果型。格納型へ読み替えない |
| `Binding.Indexers`・`Binding.Views` | 選択したCallPlanの参照結果 → 添字式の要素型・能力・依存。範囲選択は値のまま |
| HIRの公開 | 添字の決定 → `HirPlaceKind.CallResult`。生成するのは添字だけ |
| MIR構築 | 公開済みの添字計画 → 参照型の一時値とDeref。一般の呼出し計画へのフォールバックはない |
| MIRの検証・解析・生成 | 変更なし。既に一時値とDerefを扱う |

- BindingはBinding自身のCallPlanを読み、公開前のHIRを読まない。
- 計画の欠落は`InternalInvariant_Kd`とする。
- 明示的な`@follow`は既存のFollow（`HirOp.Deref`、MIRのDeref）を使う。

### 5.2. 削除と維持

| 削除 | 主な対象 |
| --- | --- |
| 結果モード | `FunctionResultMode`、`BoundType.ResultMode`とその内部化・同一性・関係・置換、`TyCtxt.Intern`の引数、`CallPlan.ResultMode`、`ResultModeOf`、Hoverのキー |
| 構文 | `PlaceResultKoto`、`KotoKind.PlaceResult`、`SyntaxForm.PlaceResultSuffix`、`ParseFunctionResult`、`place`の文脈キーワード |
| 返却と期待 | `CheckPlaceResultOperand`とその呼出し、`BindingFailure.PlaceRequired`、`PlaceRequired_Kd`とその要件カタログ、`PlaceExpected` |
| Adaptation | Place公開用の`ExpectedAdaptationKind.ExclusiveBorrow`とその生成・消費。`HirAdaptation.ExclusiveBorrow`は他の用途があるので残す |
| 照合 | 適合の識別・オーバーライド・クロージャー・Callableの結果モード比較、関連型推論のPlace除外、型表示の`-> place` |
| 呼出し | 二重の型、BindingのPlace呼出しの分岐（Followか添字の所有者に吸収）、HIRのゲッター用の死んだ分岐、`IsPlaceCall`・`PlaceCallReference`、MIRの結果一時値の分岐と`Referent`のフォールバック |

残すもの: `ElementAccess.IsUserIndex`、`IndexerCall`、`HirPlaceKind.CallResult`（名前は保ち、注釈を「選択した添字呼出しが返す参照の参照先（SPEC 4.7.1）」に改める）、添字の選択計画、通常の参照・Loan・Place操作。辞書などのアルゴリズムはKimigayoのまま書き、新しい組込み関数・結果モード・互換モードは作らない。

### 5.3. 診断

- `-> place ref/T`: `place`は型名として解析され、既存の宣言ヘッダーの規則が`ref`で1件を報告する（仮説）。この形式専用のコード・Advice・修復は作らない。
- `f() = v`・`f() += v`（`f -> uniq/T`）: 現行の参照結果で確認済み。`v`が`T`型の変数なら代入先に`InvalidAssignment_Kd`、整数リテラルなら右辺に`TypeMismatch_Kd`（期待型`uniq/T`）が出る。`@follow`を勧める修復は、前提と保証を定めた別の単位で扱う。
- 値結果の`index`実装: 識別は結果で絞らないので、`MissingContractImplementation_Kd`から`IncompatibleContractImplementation_Kd`に変わる（仮説）。
- 削除する診断: コード`PlaceRequired_Kd`とその要件`Binding.PlaceRequired`、`MisplacedSyntax_Kd`の要件`Syntax.PlaceResultSuffix`。PLAN_HISTORYに記録する。

### 5.4. 比較した設計

| 案 | 判断 |
| --- | --- |
| Place結果を維持し、既存の所有者の中でFollowと処理を共通化 | 実装の重複は減るが、結果モード・Callable・推論の規則が残る |
| Place結果を添字の要件だけに残す、または互換構文として残す | 特例とモードが関数型・CallPlan・照合に残る。添字の要件だけに残す案は約+100トークン |
| 構文と`BoundType.ResultMode`だけを消し、合成した添字呼出しのために`CallPlan.ResultMode`を残す | Place呼出しの分岐、MIRの分岐、二重の型が恒久的に残る。約−1,000トークン |
| Place結果を「参照＋呼出し位置の暗黙Follow」の糖衣にする | モードがすべての呼出し位置に伝わり、形も二つ残る |
| **参照結果に統一し、添字式が参照先を選ぶ** | **採用。** 形が一つになり、決定は添字の所有者だけが持つ。約−1,700〜−1,900トークン |
| すべての参照呼出しを暗黙にFollowする | `let r = f()`で参照を保持できない（§3.3.3、§3.6） |
| 添字を通常の呼出しにし、`x[k]@follow`と書かせる | 組込みの要素との対称性（§4.7.2）を壊す |
| 添字ごとにFollowノードを合成する、またはDerefとして公開する | 規則が異なる（選択は束縛後、能力はレシーバの経路で決まる）。一つの概念に二つの形ができ、症状対処の兆候もある。約−10トークン |

- トークン数は10-15版の見積り（R6前の基準、未測定）。R6後の採用案は本書§5.6を参照。
- 独立に作成した二つの設計案は、どちらも採用案を選んだ（10-15版§8）。

### 5.5. 導入時期

既定は、R6完了後、R7とマイルストーンの実施順の再開より前とする。

- **先行実施の費用:**
  - 旧経路の凍結（D5）の例外か、R6まで残る一時構造が必要になる。
  - 旧経路とMIRの両方で等価形を確かめ、差分台帳（mir-diff）のPlace呼出しの行を分類しなければならない。
  - 進行中のR4のBinding作業と直列に進めなければならない。
- **遅延の費用（10-15版が先行実施を選んだ理由への答え）:**
  - 10-15版の指摘どおり、R4–R6の間は二つの形、重複した決定、MIRの分岐が残る。
  - ただし既存のコードが残るだけで、遅延中の規則を守れば増えない。MIRは既にPlace呼出しを参照型の一時値とDerefで表し、`ResultMode`を読むのは`MirBuilder.Calls`の1箇所だけである。MIR解析とR5の生成はMIRを読む。
  - 10-15版の「延期で節約できるのは一時コードか凍結経路の編集だけ」という前提は成り立たない。延期すれば、旧経路での等価確認、台帳の分類、直列化も不要になる。
- **遅延中の規則:** R4–R6の新しいコードは、`ResultMode`、`IsPlaceCall`、`PlaceCallReference`、Place結果の構文を新たに読まず、Place呼出しの二重の型（呼出し式の格納型とCallPlanの参照型の違い）にも新たに依存しない。PLANに記録し（本書§7）、レビューで確認する。
- **R7と実施順の再開より前に行う理由:** R7はCallPlanの処理経路と推論文脈を統合する。先に結果モードとPlace期待を消せば、統合する分岐が減る。再開後のマイルストーン作業も、結果モードや二重の型に依存するコードを増やさずに済む。

### 5.6. 規模と性能

- **削減:** 約1,400〜1,800トークン（トリビアを除くC#トークン、未測定）。10-15版の見積り（Parsing約−400、Binding −950〜−1,350、Root約−50、Mir約−25）から、R6で消える旧経路の分を除いた。どの領域も増えない見込み。各コミットで領域別に実測する。
- **生成コード:** Place結果は既に参照結果と同じABIを使うので、変化しない見込み。高速化や割当ての削減は主張しない。
- **コンパイラー:** 型の内部化で比較するフィールドが1つ減り、選択と返却の分岐も減る。新しい割当てはない。

## 6. 実装計画

### 6.1. 作業単位

開始時にR6の完了と最新の所有者・表現を確かめ、基準コミットを記録する。

- `src/`・`tests/`・`docs/`で`FunctionResult`、`ResultMode`、`PlaceResult`、`PlaceExpected`、`IsPlaceCall`、`PlaceCallReference`、`PlaceRequired`、`place ref/`、`place uniq/`を検索し、大文字小文字を区別せずに`7.1.1`、`Place result`、`Place call`も検索する。
- SPEC 7.1.1を引く注釈は、§4.7.1か§13.5.5へ引き直すか、コードと一緒に消す。構文拒否テストや履歴など、残す一致は用途を確かめる。
- 明示的な`.index(...)`・`.indexUniq(...)`の呼出しも調べ、参照層の取り違えを防ぐ。
- 旧経路の削除で不要になった準備は、理由を記録して省く。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U0 基準と等価形 | Place結果の各利用形（本書§3.1の右列、参照越しの射影`f().0`・`f()[0]`・`f().item`、`first(xs@ref)(5)`、`@ref`・`@uniq`で転送するジェネリック関数）に対する参照結果の等価形を、既存のテスト所有者に加える。診断・意味・生成IR・性能の基準を保存する | 等価形が通る。通らない形は原因を特定し、共通の所有者で直す単位を先に完了する |
| U1 添字の型取得 | `Binding.Indexers`・`Binding.Views`・`PlaceOrigin`が、合成した添字呼出しの表面型ではなく、選択したCallPlanの参照結果から要素型を得る形に統一する。合成した添字呼出しの`BoundType`を読む他の箇所も検索して同じ形にする（挙動不変） | 同じ入力で診断・意味情報・HIR/MIR・生成IRが一致する（挙動不変の証拠） |
| U2 一般関数の移行 | 一般関数、テスト内の例、ネイティブfixtureを参照結果＋`@follow`へ書き換える。マイルストーン、添字Contract、Place構文のテストは残す | 出力・評価順序・破棄・割当てが同じ。型が変わる箇所を本書§3で確認する |
| U3 結果形式の切替え | 本書§5.2の削除、Indexable要件、ライブラリー、添字の公開、正式仕様、Milestone 27・28、残るテストを一体で変える | 本書§2・§3の受理と拒否が一致する。参照結果の適合、関連型推論、明示的な呼出し、旧構文の拒否を確認する |
| U4 エディター | TextMate文法のPlace結果の規則と、そのスコープのテストを削除する。`place`を通常の名前として扱う既存のテストは残す | 拡張機能の規定の検証を通る。依頼のない配布・インストールはしない |
| U5 確認 | 残存検索、性能測定、最終レビュー、Session、記録 | 本書§6.3を満たし、未検証事項と残る制限を明示する |

- U1は挙動不変の構造整理、U2は等価なソースへの移行、U3は言語仕様の変更として、別々にコミットする。
- U3は分割しない。Contract・コンパイラー・ライブラリーが食い違ったまま公開されるためである。正式仕様もU3より先に取り込まない。先に取り込むと、参照結果のIndexable実装が`MissingContractImplementation_Kd`（有効な形への保守的なLanguageエラー）になる。二重の実装や機能フラグも設けない。
- 仕様上有効だが未対応の形が見つかれば、`Unsupported_Kd`と対応範囲を記録する。型や利用形ごとの例外で回避しない。

### 6.2. 検証する規則

各規則を観測できる最下層で、代表形と独立した不正例を選ぶ。全組合せは作らない。

| 規則 | 正常例 | 不正例・対照例 |
| --- | --- | --- |
| 添字の能力 | Copyの読み取り、更新、複合代入、`@ref`・`@uniq`、格納された`uniq/U`の再借用、`match pair[0]`、`pair[0].bump()`、`pts[0].x = 5` | 共有経路の更新（`SharedPathAccess_Kd`）、`let`のレシーバの排他取得（`InvalidAssignment_Kd`）、`pair[0]@move`（`ExclusivePathTake_Kd`）、`S is Indexable`での`@uniq` |
| 関数と合成 | 直接の呼出し、関数値（`Dictionary<i32, i32>.index`を`-> ref/i32`の関数型で使う）、Callable、ジェネリックな転送、射影`f()@follow.0`、呼出し可能な要素`first(xs@ref)(5)` | 参照結果への直接の代入、`-> i32`の関数型への当てはめ |
| Loanと寿命 | `during self`の保持、`during self.source`の共有ビュー（ビューを置き換えても要素が残る）、要素内部の参照 | 生きている参照の元を更新・破棄する、親より長い排他再借用 |
| 推論 | 入力からの推論、`get<i32>()`、期待型`ref/i32`による推論、`index`からの`Element`の推論 | `let v: i32 = get()`、異なる`Element`の証拠 |
| 取得とSubject | 本書§3.1の各行の新しい書き方 | 旧の綴りが新しい意味で通る形（参照の保持、`ref/(ref/T)`、Exclusive Subject）を期待値で区別する。`let v = f()@ref`の後で`v`を使う形はエラー |
| 添字固有 | キーの1回評価、固定長配列の直接射影と静的Move Path | 複数の適合による曖昧さ、借用経由のMove、`@uniq`のない`indexUniq`本体、値結果の`index`（本書§5.3） |
| 実行と表現 | 非Copy・ゼロサイズ・入れ子の参照、評価順序、破棄1回 | 余分なCopy・Move・割当て・一時スロットへの依存がない |
| 構文と公開情報 | 通常の名前`place`、参照結果のHoverと意味情報 | `-> place ref/T`・`-> place uniq/T`がLanguageエラー |

テストの扱い:

- **主な所有者:** `IndexableContractTest`、`DictionaryIndexableTest`、`DictionaryStoredReferenceTest`、`UniqSliceTest`、`DereferenceUpdateTest`、`PairFollowTest`、`PlaceCalleeTest`、`PublishedPlaceProjectionTest`、関連型・Origin・Callableのテスト。
- **書き換え:** 各規則の期待を保つ。本書§6.1の検索で見つかるテストすべてが対象で、特に次に注意する。
  - 明示的な`index`呼出し（`DictionaryIndexableTest`・`DictionaryCostTest`・`DictionaryStoredReferenceTest`）。結果が参照になるので、`entries.index(1)@follow@ref`は`@follow@follow@ref`になる。
  - `KimiLibraryShapeTest`（Indexable・UniqIndexableの宣言文字列）、`UserArithmeticUpdateTest`、`VirtualOverrideContractTest`（Originの依存は残し、モードの検査は削除）、`HirColumnsTest`（添字の行は`CallResult`のまま）、`SemanticSnapshotTest`（ResultModeを出力しない）。
  - `tests/mir/calls.*`と構文診断コーパス（実行で得た記録に置き換える）。
- **削除:** `PlaceResultTest`、`PlaceResultParseTest`、`CallablePlaceResultTest`。独自の検証を先に移す。`PlaceResultParseTest`からはContractの型引数と精緻化の解析、`CallablePlaceResultTest`からはゼロサイズ・長さItem・射影`f(a@uniq)@follow.0 = 40`・入れ子の参照・割当ての検査を移す。重複したケースは残さない。

### 6.3. 検証と測定

[VERIFICATION](../../docs/dev/VERIFICATION.md)に従い、各単位で対象のクラス・メソッド・native fixtureを明示する。コンパイラーの変更には`ArchitectureRulesTest`、ライブラリーの変更には`KimiLibraryShapeTest`を含める。作業の終わりに、必要なnative fixtureとマイルストーンを明示してSessionを実行する。NativeAOTは対象外。

```powershell
# U3の選択例。CallablePlaceResultTestから移したケースのfixtureと、本書§6.2の検査を加える。
./scripts/verify.ps1 -Class IndexableContractTest,DictionaryIndexableTest,DictionaryStoredReferenceTest,DictionaryCostTest,UniqSliceTest,PlaceCalleeTest,HirColumnsTest,SemanticSnapshotTest,VirtualOverrideContractTest,MirDumpTest,SyntaxDiagnosticTest,MilestoneSourcesTest,KimiLibraryShapeTest -Fixtures 'Indexable*.ll','Dictionary*.ll','UniqSlice*.ll','PlaceCallee*.ll' -Milestone 27,28,31,32
```

- **U2・U3:** 書き換えの期待差を列挙し、出力だけでなく取得対象・Loan・評価順序も検証する。仕様変更による期待値の変更と回帰を区別し、コミットに理由を書く。Milestone 31・32はDictionaryの`indexUniq`を通る。
- **生成コード:** ソースを変えないプログラムの生成IRが一致することを確かめる。変更したライブラリー本体と、添字・通常呼出しの呼出し側のO2 IRを比較する。`DictionaryCostTest`と`UniqSliceTest`の割当て保証を保つ。`src/Benchmark/Kimi/ExclusiveViews.kimi`を同じ条件で前後7回ずつ測り、失敗も保存する。
- **コンパイラー:** Bindの初回・再利用時の時間、割当て、保持メモリーと、領域別の正味トークンを測る。再利用時0 Bの回帰検査は緩めない。
- **記録:** 証跡は`artifacts/verify/`と`artifacts/benchmarks/`に置く。各単位は検証の成功後にコミットし、`verify-commit.ps1`で同じ入力の証跡を関連付けてpushする。

## 7. 記録

- **DECISIONS:** 表現を変えるU3のコミットで項目を追加する。採用した表現と所有者、削除したもの、比較した案（本書§5.4）、R6後に行う理由を書く。10-15版§15の草案を出発点にするが、段階S1・S2と「R6後への延期を却下」は除き、2026-09-25版との比較で成り立たなくなった理由は残す。番号は実装時の段階に従う。
- **SETTLED:** 不採用の言語案を追加する。糖衣にする案、すべての参照呼出しを暗黙にFollowする案、添字を通常の呼出しにする案の三つ。
- **PLAN:** 時期の確認時に、PLAN §3へ1行（位置と本書§5.5の遅延中の規則）を加え、完了時に削除する。範囲外の問題はPLAN §7の1行にまとめ、200行未満を保つ。
- **CODEMAP・STATUS・DIAGNOSTICS・VERIFICATION:** 所有者、削除、対応範囲、消える診断、検証対象を反映する。COMPILER_ARCHITECTUREと上限は変えない（前倒しで一時構造を作る場合だけ、その除去段階を記録する）。
- **`draft/INTEGRATED.md`:** U3のコミットで、本書と10-15版の節・反映先・処分を記録し、両方を`draft/Changes`へ移す（取り込み済み）。凍結された過去の提案やレビューの本文は変えない。

本変更の範囲外の既存の問題:

- `VirtualOverrides`がBindingの中でメッセージ文を組み立てている（AGENTSの診断の規則に反する）。結果モードの文言はU3で消えるが、仕組みは別の単位で直す。
- 現行コンパイラーは、参照を返す呼出しに`@ref`を付けた`ref/(ref/T)`を、一時値の寿命の後も使える（`let v = first(words@ref)@ref`の次の文の`show(v@follow)`を受理する）。§3.6に反し、本書§3.1の`f()@ref`の行を検査できないので、U2より前に別の単位で直す。

## 8. 未検証事項

現行コードで表現が共通だと確認できても、次の事項は未検証である。

1. U0の等価形がすべての経路で通るか。`DereferenceUpdateTest`は`pick(...)@follow += 10`を示している。
2. 外側の借用から独立する結果の受理範囲。
3. `index`からの関連型推論（§8.5.5の除外を外した後）。
4. 本書§5.3の`-> place ref/T`と値結果の`index`実装の診断のコードと位置。
5. ライブラリー本体の生成IRの同一性、O2性能、割当て。
6. すべてのトークン数。
7. 合成した添字呼出しの`BoundType`を読む箇所が、U1の対象以外にあるか（U1の検索で確かめる）。
8. R6後の所有者と表現。U0の開始時に確認する。
