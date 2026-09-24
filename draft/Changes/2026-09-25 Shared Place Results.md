# 共有Placeの返却

日付: 2026-09-25
状態: 仕様変更案。正式仕様への取り込み・実装・動作検証は未実施。

本書で変更する事項は `SPEC.md` とその参照先より優先し、変更しない事項には既存仕様を適用する。

## 1. 目的と採用する設計

関数から、値を格納している場所を**共有アクセス専用のPlace**として返せるようにする。格納値のCopy・Move・所有権移譲は、Placeを返す操作では行わない。

```kimi
struct Box<V>
    private var value: V

    public func Get(self: ref/Self) -> place(V) during self
        return self.value
```

これにより、ユーザー定義のHashtableなどが内部の要素を取り出さずに公開し、呼び出し側が共有借用・格納参照のコピー・Copy値の取得を選べる。

### 1.1. この方式を選ぶ理由

| 方式 | 判断 |
| --- | --- |
| 要素型から別の読み出し型を計算する | Copy能力やsemanticsに応じた公開型・取得操作・寿命の対応規則が必要になる。今回は導入しない。 |
| 普通の値を返し、Moveだけ禁止する | Non-Copy値を元の場所に残したまま返す方法が定まらない。非所有性・寿命・更新権限も必要になる。 |
| 共有Placeを返す | 元の格納場所を公開し、既存の共有参照と取得規則を利用できる。本書で採用する。 |

`ref/V` を返す既存APIでもスロット借用は可能であり、固定の参照結果で十分ならその方式を使える。Place返却の追加価値は、呼び出し側が**スロットそのものを借りるか、格納された値を共有アクセスするか**を選べる点にある。

### 1.2. 記法

推奨記法は `-> place(W) during source` とする。`W` はsemanticsと内部Originsを含む完全型で、`place(s/V)` とも書ける。

```text
ResultClause := '->' Type
              | '->' 'place' '(' Type ')' [ 'during' OriginAtom ]
```

`place` は結果位置の文脈キーワードであり、新しいsemantics・Core・値Typeではない。括弧内は公開する格納型、括弧外の `during` は格納場所の寿命を表す。

| 候補 | 評価 |
| --- | --- |
| `place(W)` | 既存のPlaceという概念と対応し、型と格納場所のOriginを区別できる。採用する。 |
| `place W` | 内側の借用型に付く `during` との境界が分かりにくい。 |
| `borrow(W)` | 取得済みの参照値を返すように見える。 |
| `nomove W` / `moveback W` | 所在・非所有性・共有権限を表さず、後者はMove後の復元も連想させる。 |

`place` は常に共有アクセスを意味する。将来の書き込み可能なPlaceは、既存の意味を変えず別の明示契約として検討する。

## 2. 共通の意味と検査規則

### 2.1. 共有参照を基準にする

**`place(W) during r` は、有効な `ref/W during r` が指す格納場所を、共有Placeとして公開する結果モードである。** これは参考モデルではなく、型形成・アクセス権限・寿命・Loan・コード生成の共通基準とする。

- 返却時には、場所とその有効性を確保する。Wの値を複製・移動せず、破棄責任も取得しない。
- 呼び出し元が使い終わるまで関数を停止する仕組みではない。関数のcleanupを完了してから制御を戻す。
- Placeを指す情報と、そこから取得する値を区別する。値を取得した後は、その値に通常の規則を適用する。
- この結果モード自体を、ローカル変数・引数・集約・クロージャの格納型にはできない。

既存のPropertyアクセス権、初期化、構築完了、静的ストレージ、Unsafeの条件を迂回できない。

### 2.2. 実際の格納型と公開型

実際の格納型を `A`、公開型を `W`、実ストレージを共有借用できるOriginを `a` とする。返却には次を要求する。

1. 別名展開後、AとWのsemantics・Core・型構造が一致する。違いとして認めるのはOriginsだけである。
2. `ref/A during a` が、既存のOrigin部分型規則で `ref/W during r` に適合する。
3. 実際のLoan・依存元・親子関係を保持し、必要な権限と寿命を証明する。

共有参照で許されるOriginの短縮は認めるが、不変位置では従来どおり一致を要求する。内側の `uniq`、関数型、可変ストレージなどの変性を独自に緩和しない。

実ストレージの宣言型を書き換えるわけではない。利用者には公開型Wの範囲の保証だけを与え、短縮前のOriginを復元する操作は認めない。Core変換、参照層の平坦化、object payload投影は返却自体では行わない。

### 2.3. 返せる場所と正常完了

`return` と単一式本体をPlace結果の文脈で検査する。通常完了する式は、既に存在する初期化済みの場所、または互換なPlace返却呼び出しを指定しなければならない。

```kimi
func Forward<V>(box: ref/Box<V>) -> place(V) during box
    return box.Get()

func BorrowSlot<V>(box: ref/Box<V>) -> ref/V during box
    return box.Get()@ref/V

func Missing() -> place(i32)
    => $abort("missing") // 正常完了しないので、場所を返す必要はない。
```

場所の取得元を「一時値だから禁止」のような構文分類だけで判定せず、公開Originを満たすかで判定する。

| 取得元 | 判定 |
| --- | --- |
| 呼び出し先で終了するローカル・値引数のスロット | 返却先の寿命を満たせないので不可。 |
| 呼び出し元の一時receiver内のストレージ | 通常の一時寿命内でのみ使用可能。返却による寿命延長はない。 |
| ローカル参照変数から到達する外部ストレージ | 外部ストレージの契約を満たせば可能。ローカル参照変数自身のスロットとは区別する。 |
| 標準Field・Tuple・配列要素・適法な参照経路 | 型・アクセス・初期化・寿命の検査を満たせば可能。 |
| 普通の値返却やcomputed getterの値結果 | Place返却を成立させるための暗黙の実体化は行わない。 |

非完了の式は結果モードを問わず適合する。到達不能な構文の検査は省略しない。関数末尾への到達はUnit値を生成するだけなので、`place(())` を含め、Place返却関数の正常完了には使えない。

複数の場所を選ぶ場合は各分岐で明示的に `return` し、全経路を検査する。`if`・`match`・`do` 自体をPlace生成式にする拡張は含めない。

## 3. 呼び出し側の取得と制約

### 3.1. 通常の共有Placeとして使う

旧案の「使用前に必ず `@ref` 等を記述する」という制限は設けない。Placeそのものの保存と、Placeから通常の値を取得することを区別する。

| 使用文脈 | 規則 |
| --- | --- |
| 共有参照引数・共有receiver | 既存仕様でその型・文脈に許された暗黙の借用・Reborrow・参照Copyを使用できる。 |
| Copyな型の値取得 | 通常のCopyを許可する。格納値は消費しない。 |
| Non-Copyな値の取得 | 暗黙Moveを行わず、既存の借用適合がなければエラー。 |
| `@ref` / `@objref` / 完全型を指定した共有借用 | §3.2の共通規則を適用する。 |
| Field・Tuple・添字projection | 通常の場所特定を行い、取得元の共有権限と依存を維持する。 |
| `match` / `for` | 既存のSubject取得規則を適用する。排他的なSubjectを新設しない。 |
| 破棄文脈 | 呼び出しとその副作用を一度実行する。未取得のPlaceが指す値は破棄しない。 |

格納型がNon-Copyの `Resource`、`inspect` が `ref/Resource` を受け取る例:

```kimi
inspect(table.Get(key))            // 既存の共有借用適合。
table.Get(key).inspect()           // 共有receiverなら可能。
let view = table.Get(key)@ref      // ref/Resourceを保存。
let bad = table.Get(key)           // ResourceのCopyを証明できないためエラー。
```

格納型が `i32` なら `let x = table.Get(key)` は整数のCopyであり、Placeの別名を保存する意味にはならない。未知のVについては、値Copyに `V is Copy` の証明が必要になる。

格納された参照値についても既存の規則を使う。共有参照はCopyできるが、格納された `uniq` を期待型なしで排他的にReborrowするなど、共有経路を超える取得は許可しない。

これにより、引数・initializer・集約要素などの位置ごとに新しい禁止一覧を作らず、共通の取得規則と共有権限で判定する。

### 3.2. スロット借用と内容への共有アクセス

以下の `p` はPlace式の説明用記号であり、Place型の変数宣言ではない。

| 公開される格納型W | 操作 | 結果 |
| --- | --- | --- |
| `owner/T` | `p@ref` | 格納値を借りる `ref/T`。Copy能力によらない。 |
| `ref/T` | `p@ref` | 格納された `ref/T` のCopy。 |
| `uniq/T` | `p@ref` | 共有Reborrowによる `ref/T`。 |
| `obj/T`・`rc/T`・`arc/T` | `p@objref` | 対象objectを借りる `objref/T`。参照カウントを増やさない。 |
| `objref/T` | `p@objref` | 格納された `objref/T` のCopy。 |
| `objuniq/T` | `p@objref` | 共有Reborrowによる `objref/T`。 |
| 有効な完全型W | `p@ref/W` | 完全なスロットを借りる `ref/W`。 |

`W = ref/T` なら、`@ref` と `@ref/ref/T` は別の操作になる。型引数が完全型Vなら `Get()@ref/V`、pairが `s/V` なら `Get()@ref/s/V` でスロットを借りられる。

既存の同型Copy・Reborrowの優先順位、明示的なobject upcast、Sealed payloadの共有投影条件は維持する。`@ref` を自動的に `@objref` へ変える規則は追加しない。`unsafe/T` のスロット借用はできるが、指す先への安全な参照は生成しない。

### 3.3. 消費・更新の禁止と通常値への復帰

返却Placeと、その共有アクセスに依存する経路では、Move・置換・書き込み・排他的借用・排他的receiver呼び出しを禁止する。格納型がCopyでも `@move` は許可しない。

格納された `uniq` やobject handleを経由して権限を回復することもできない。判定は、相当する共有スロット参照を経由した場合と同じにする。

一方、取得済みの通常値には通常の規則を適用する。Copyした値を変更したり、取得済み共有参照をMoveしたりすることは、元の要素の消費ではない。永続的な「Move禁止」属性を派生値へ付けない。

```kimi
let view = numbers.Get(key)@ref/i32
let snapshot: i32 = numbers.Get(key)@ref/i32 // 既存のCopy read。
let direct = numbers.Get(key)              // i32の通常Copy。
```

### 3.4. 場所の特定と評価順序

参照経路の追跡やSliceハンドルの読み取りなど、場所の特定に必要な処理と、利用者へ値を渡す取得を区別する。前者だけを理由にprojectionを禁止しない。

receiver・引数・添字・getterは既存の順序で一度だけ評価する。値引数などを取得する場面では、PlaceからのCopyまたは借用まで終えてから、後続のoperandを評価する。場所の特定後は、後続の添字計算やcleanupを含め、取得に必要なストレージを連続して保護する。custom getterやメソッドを通る場合は通常の関数境界と共有receiver検査を適用し、隠れた格納場所を直接公開したことにはしない。

## 4. Origin・Loan・cleanup

### 4.1. Originの導入と省略

`place(W) during r` の外側Originには、`ref/W during r` と同じ名前解決・量化・整形式・省略規則を適用する。

- 名前付き関数・Contract関数要求では、許可された位置の未束縛名は既存規則どおり普遍Originを導入する。
- Function Type・Callable・匿名関数は既存の名前導入制限を維持する。Callableの明示注釈制限をPlace構文で迂回できない。
- 特殊化は元の契約を継承し、新しいOriginを導入しない。
- 外側Originの省略は、直接の借用入力すべての外側Originの共通範囲を使う。該当入力がない場合は既存の共有結果のstatic既定を使い、本体の成立を別途検査する。
- W内部の既存Originsを外側Originで上書きしない。内部の未完了箇所にも既存の位置別規則を適用する。

```kimi
-> place(ref/Node during source) during self
```

この例の `source` は格納参照の参照先、`self` はその参照を入れたスロットの契約である。

Hashtableの `Get(self: ref/Self, key: ref/K)` は `during self` を明示する。省略すると検索キーのOriginも既定に含まれるためであり、receiverだけを優先する新しい省略規則は設けない。

### 4.2. 三種類の依存を分離する

検査と公開メタデータでは、次を区別する。

| 依存 | 意味 |
| --- | --- |
| 格納場所の保護 | 場所の取得・転送・使用中に必要な、スロットとownerの保護。 |
| 格納値の既存依存 | W内部の参照などが元から持つLoan・依存元。 |
| 取得で生成する依存 | 新しいBorrow・Reborrowと、その親Loanとの関係。 |

処理は次の順序で行う。

1. 入力の取得から関数終了まで、既存のcall-wide保護を維持する。
2. 戻す場所を決めた時点で結果を確保し、必要な保護をcleanup前に成立させる。
3. cleanup・返却・呼び出し側のprojectionや取得まで、その保護を切れ目なく維持する。
4. 値の取得後は下表に従って依存を引き継ぎ、以降は通常のLoan livenessで期間を決める。

| 取得操作 | 引き継ぐ依存 |
| --- | --- |
| スロット借用 | スロットの公開された保護と、内容の必要な依存。 |
| object借用 | 対象objectと、それを生存させるowner等の必要な保護。 |
| Reborrow | 既存の親Loan・依存元と、新しい子Loan。 |
| 格納済み共有参照のCopy | 公開型が保持する参照先のOriginと既存依存。取得元のスロットだけを理由とする新しいLoanは残さない。 |
| 独立したCopy値の取得 | 値内部の既存依存。取得にだけ必要だったスロット保護は残さない。 |

既に格納値がコレクションへ依存していれば、その依存は残る。等しいOrigin名からLoanの同一性や独立性を推論せず、実際の依存元とReborrow関係を保存する。入力の取得で形成した排他的Loanも、結果への依存が続く間は共有Loanへ弱めない。Originを短縮して公開した場合、Copyしても公開契約より長い寿命には戻らない。

複数の返却元があるときは依存元候補を保守的に合成する。privateな本体を呼び出し側で再解析しなくても同じ判定ができる契約を保存する。

### 4.3. 更新・一時receiver・破棄

関数境界は、privateなFieldの非重複性を呼び出し側へ自動公開しない。コレクション要素への借用は、公開契約に応じて全体の更新と競合する。異なるキーや十分なcapacityだけでは非競合の証明にならない。

```kimi
let view = table.Get(key)@ref/Resource
table.clear() // 後でviewを使用するため、共有借用と競合。
inspect(view)
```

`defer`・引数破棄・ローカル破棄・静的アクセスの効果を合成し、確保した結果をcleanupが無効化しないことを確認する。Abort・非終了・通常の破棄順序は変更しない。

```kimi
let view = makeTable().Get(key)@ref/Resource
// 一時tableがinitializer末尾で終了する場合、その後viewは使用できない。
```

一時receiverは既存の文脈別の一時寿命に従う。Place返却自体による延長はない。独立したCopy値や、receiverに依存しない外部共有参照のCopyは、それぞれの通常の寿命に従う。

## 5. 型推論・関数境界

### 5.1. オーバーロード選択と取得を分ける

結果モードは関数契約に含めるが、オーバーロードの識別要素には追加しない。同じ引数シグネチャで戻り値だけが `W` と `place(W)` の関数は共存できない。

- Placeを返す `return` 文脈では、期待される結果モードと公開型を使える。
- Place結果の公開型Wは、receiver・引数・明示型引数・期待されるPlace契約から確定させる。通常の期待値Typeから、借用変換を逆算して未知のWやsemanticsを選ぶことはしない。
- Wが確定したPlace候補は、通常の値文脈で許される共有取得またはCopyによって期待型を満たせるかを検査する。これは候補の適用可能性の検査であり、戻り値の取得方法による順位は付けない。
- 普通の値結果には従来の期待結果適合を適用する。値結果をPlace候補として扱うための実体化や、従来禁止された結果変換は追加しない。
- 外側に `@ref` があるだけでPlace返却を優先しない。結果モードを変換する隠れた処理も挿入しない。
- 明示適応の型情報は既存の推論境界内だけで使用する。候補ごとの本体解析や変換経路の探索は行わない。
- 型・結果モードを確定して取得計画を選び、その後のLoan違反で候補を選び直さない。

```kimi
// 引数型が異なる二つのシグネチャの例。
func Select(n: i32) -> i32
func Select(n: i64) -> place(i32) during static

let x = Select(1)@ref // ほかに選択根拠がなければ曖昧。Place版を優先しない。
```

通常の値結果とPlace結果が同じ値文脈で使用できても、関数値同士の暗黙変換を認める意味にはならない。

### 5.2. ジェネリクス・Callable・特殊化

`place(V)` はVに新たなCopy・Owned・ObjectPayload制約を課さない。通常の格納型形成と借用の条件は必要であり、定義時に許される全束縛について本体を検査する。

関数・メソッド・明示結果を持つ匿名関数・Function Type・Contract関数要求で結果モードを保持する。未注釈の匿名関数からPlaceモードを推論しない。

```kimi
(ref/Box<V>) -> place(V)
```

関数契約の適合には、同じ結果モードと§2.2の共有アクセス適合を要求する。入力の反変性・Origin量化・既存のhidden environment制限は維持する。普通の型引数Rに `place(V)` を束縛できず、Placeを転送するgeneric callableはそのモードを明示する。

Contractの実装、特殊化、関数参照、間接呼び出し、別コンパイルで、モード・Origins・Loan契約を落としてはならない。通常の参照結果へ変える場合は、明示的なラッパー関数で取得する。

## 6. Arrayと周辺機能

### 6.1. 借用の省略形を統一する

固定長配列・Array・Sliceの添字Placeにも、Fieldおよび関数が返すPlaceと同じ明示的借用規則を適用する。添字の `@ref` / `@uniq` が常に要素スロットを借りる特別扱いを廃止する。

型とアクセス権限に応じて、共有・排他のBorrow/Reborrowを選ぶ。返却PlaceとSliceは共有権限しか持たないため、排他的操作は引き続きできない。スロット自体を借りるときは完全型を指定する。

```kimi
// refs: Array<ref/Node>
let value = refs[0]@ref          // 格納されたref/NodeをCopy。
let slot = refs[0]@ref/ref/Node  // スロットへのref/ref/Node。

// nodes: Array<obj/Node>
let objectView = nodes[0]@objref     // objref/Node。
let handleSlot = nodes[0]@ref/obj/Node // ハンドルのスロットを借りる。
```

明示的な `@` は通常読み出しの後処理ではなく、解決したPlaceに直接適用する。完全型を指定した排他スロット借用も、通常の権限検査に従う。

### 6.2. 変更しない読み出しと対象外の機能

裸のArray添字に適用する既存の `SharedReadResult` と、`match`・ガード・Tuple分解の取得規則は変更しない。したがって、裸の `array[i]` と `Get()` の通常値取得が全面的に同じになるわけではない。本書で統一するのは明示的借用と共有Placeの権限である。

次は今回の対象外とする。

- 書き込み可能なPlace返却、Placeの変数・引数、`Option<place(V)>`。
- ユーザー定義indexer、Placeを返すcomputed Property。
- 任意のsemantics変換、未定義のraw pointerから安全な参照を生成する操作。

`Get()` は正常完了するなら場所を返す。不在を扱うAPIには既存の `Option<ref/V>` などを使用できる。`Contains()` の成功は将来の場所を予約しない。

通常のcustom/computed getterは値返却のままである。Place返却関数がprivateな標準Fieldを公開する場合も、本体で合法なアクセスと公開型の可視性を要求する。

## 7. 性能と実装上の契約

Place返却の基準は、**相当する共有スロット参照の返却と、通常の参照操作**とする。

- Placeの形成・一段の転送自体は、格納値のサイズや要素数に依存しないO(1)とする。検索・projection経路・ユーザー処理の費用は別に数える。
- この機能だけを理由とするヒープ確保、Wのコピー、一時W領域、参照カウント更新を要求しない。
- 結果モード・権限・Origin・Loanの検査情報は静的に扱い、専用の実行時Loan台帳や寿命タグを追加しない。参照表現に本来必要な情報は維持する。
- 転送ラッパーは参照の転送として生成できる。明示取得後のCopy readでも、不要な参照の保存・再読み取りを除去できる。
- 最適化の有無で合法性・借用期間・評価回数・観測可能な副作用を変えない。

一律の一語ABIは要求しない。型情報・別コンパイル成果物にはモードと公開契約を保持し、通常の依存無効化を行う。

複数回アクセスするときは検索を一度にする。

```kimi
let item = table.Get(key)@ref/Resource
inspect(item)
inspectAgain(item) // Getを再実行せず、通常の参照を再利用。
```

## 8. 統合時の確認項目

### 8.1. 必須の検証

| 領域 | 確認する内容 |
| --- | --- |
| 返却元 | Field・配列要素・外部参照先・staticを返せる。終了するローカルへの参照を返せない。 |
| 結果検査 | 複数return、Never、cleanup後の非完了、`place(())` のfallthroughを区別する。 |
| 型とOrigin | 外側と内部Origins、共変位置の短縮、不変位置、名前導入、Callableの境界を保持する。 |
| 取得 | 共有引数・receiver、Copy値取得、参照Copy、Reborrow、完全型スロット借用が同じ規則で動く。 |
| 権限 | Move・書き込み・排他的操作と、格納uniq経由の権限回復を拒否する。 |
| 依存 | 格納共有参照のCopyでは不要なスロット保護を残さず、実際の既存依存は保持する。 |
| 評価順序 | 添字やgetterの副作用、call予約、cleanup、破棄文脈を含めて一度だけ評価する。 |
| 関数境界 | 転送・Contract・特殊化・間接呼び出しで契約を維持し、返却モードだけのoverloadを拒否する。 |
| Array | 新しい省略借用規則と、変更しない裸の読み出し・Pattern規則を分けて検証する。 |
| 性能 | 追加確保・Wの不要なコピー・カウント更新がなく、ゼロサイズ値でも論理的な場所を区別する。 |

### 8.2. 正式仕様への反映先

| 章 | 主な変更 |
| --- | --- |
| [3 型と値](../../spec/03-types-and-values.md)、[7 関数](../../spec/07-functions-and-callable-values.md) | 結果モード、共有Place、関数構文、返却文脈。 |
| [8 ジェネリクス](../../spec/08-generics-constraints-and-contracts.md)、[9 シグネチャ](../../spec/09-names-signatures-and-access.md)、[10 推論](../../spec/10-overload-resolution-and-inference.md) | 定義検査、Origin適合、候補選択、Callable・Contract・特殊化。 |
| [4 配列](../../spec/04-arrays-indexing-and-slices.md)、[11 Property](../../spec/11-properties.md)、[12 式](../../spec/12-expressions.md)、[13 操作](../../spec/13-operators-and-assignment.md) | 借用省略形の統一、通常取得、projection、既存getterとの区別。 |
| [14 制御フロー](../../spec/14-control-flow.md)、[15 所有権](../../spec/15-ownership-and-lifetime-analysis.md)、[16 cleanup](../../spec/16-scope-exit-and-destruction.md) | Never・正常完了、Origin束縛、依存の分離、返却前後の連続保護。 |
| [18 成果物](../../spec/18-modules-and-dependencies.md)、[21 コード生成](../../spec/21-layout-runtime-and-code-generation.md)、付録A・E・F | 契約の保存、表現と性能、検証項目・用語・構文。 |

正式仕様には採用内容を自立した規則として取り込み、影響する例・milestoneを更新する。`STATUS.md` は実装・検証済みの支援範囲が変わった時点で更新する。取り込み時は `draft/INTEGRATED.md` に記録し、本書を凍結する。
