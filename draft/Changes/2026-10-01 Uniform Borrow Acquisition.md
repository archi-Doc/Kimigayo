# 借用・再借用の取得規則の統一案

日付：2026-10-01

状態：最終版。既定の再借用、Placeの選択、Captureの統一（C1）を採用決定。Receiverの暗黙の排他取得（現行§7.3）は維持し、後置操作の優先順位の変更は本案から除外した。正式仕様への取り込み・実装は未実施。

本書で変更する事項は正式仕様に優先し、変更しない事項には既存仕様を適用する。仕様の節は「§n」、本書の節は「本書 n」と表記する。例は改訂後の期待動作を示し、「エラー」と記した行は意図した拒否例である。正式仕様・実装と机上で照合したが、改訂後の実行結果と性能は未検証である。

## 1. 目的と方針

### 1.1. 現仕様の問題

| 現仕様 | 問題 | 根拠 |
| --- | --- | --- |
| `r: uniq/T`の`let saved = r`は拒否、`let saved: uniq/T = r`は再借用 | 同じ型の注釈で取得の可否が変わる | [§10.2.1](../../docs/spec/10-overload-resolution-and-inference.md#1021-inference-and-adaptation-classes) |
| Captureの一般表は裸取得をCopyに限り、参照用の表は`[r]`を再借用とする | 同じ節の表が一致しない | [§7.6.2](../../docs/spec/07-functions-and-callable-values.md#762-capture-acquisition-and-environment) |
| Captureの`r@uniq`は参照先を、通常式の`r@uniq`は参照スロットを借用する | 同じ表記で対象が変わる | 同上、[§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow-borrow-and-reborrow) |

本案は安全性の欠陥を正すものではなく、取得規則の文脈差を減らす。所有Placeの排他貸出を、Receiverでは省略でき、引数では明示する差は維持する（本書 7.2）。

### 1.2. 方針

**既存の排他的参照は、期待型の有無にかかわらず、その型に従って再借用する。** 明示Captureは、通常の初期化と同じ取得規則で扱う。次の規則は現行のまま変えない。

- Receiver Expressionの暗黙取得（§7.3）
- Receiver以外の位置で所有Placeを新しく排他的に貸すときの`@uniq`／`@objuniq`（§15.1.5）
- 書込み構文の取得（§13.7）

新しいキーワード、借用型、実行時検査は追加しない。

### 1.3. 目標とする性質

1. **注釈の同値性：** 同じ完全Type・Origin条件の注釈を付け外ししても、取得・Loan・cleanupが変わらない。
2. **再借用：** 元の参照をMovedにせず、子Loanの生存中は親の競合操作を禁止する。
3. **一つの計画：** 元の式から最終取得計画を一つ作る。型推論・取得計画・Loan合法性を分け、後段の失敗で候補を選び直さない。

## 2. 仕様変更

### 2.1. 既定の値取得

明示操作も固定された期待型もない取得には、次の表を使う（§3.5に集約）。

| 取得元 | 既定の取得 |
| --- | --- |
| Copyを証明できるPlace | Copy。`ref`／`objref`なら参照値をCopyする |
| `uniq/T`／`objuniq/T`を保持するPlace | 同じSemanticsの再借用。経路の権限とLoan合法性を要求する |
| その他のNon-CopyまたはCopy未証明のPlace | エラー。転送するなら`@move`と書く |
| 一時値 | そのまま転送する |

- **適用位置：** 初期化、代入元、引数、値返却、結果ソース、aggregate要素、enum payload、default、明示Capture（本書 2.5）、明示discard。期待型が固定されている位置では本書 2.2、genericでは本書 2.4、Receiverでは§7.3に従う。
- **再借用の対象：**
  - 再借用するのは外側の排他的参照だけである。`Option<uniq/T>`、参照を含むstruct、既存の配列全体は分解しない。配列リテラル`[r]`では要素式`r`に適用する。
  - `let`に保持した排他的参照も再借用できる。`let`の所有値には排他権限を与えない。
  - 共有経路上の`uniq/T`（`ref/(uniq/T)`の参照先など）は排他的に再借用できず、エラーになる。共有で足りるなら期待型`ref/T`を書く。
- **保持するもの：** 型・Origin・実際のLoan祖先・初期化状態・スロットの保護・破棄依存を保つ。参照スロットと参照先を区別し、再借用に不要なスロット依存は加えない。
- **他の操作との区別：**
  - `@copy`は再借用の別名ではない。
  - `@move`の結果を、後から再借用・共有化・値読み出しで修正しない。
  - 同じ型の一時参照は、そのまま渡す。
  - `_ = r`は再借用した子を直ちに破棄するだけで、`r`を消費しない。参照値自体を消費するには`_ = r@move`と書く。

```kimi
var number: i32 = 1
let r = number@uniq
let first = r                 // uniq/i32への再借用。
first@follow += 1
let second: uniq/i32 = r      // 注釈なしと同じ取得。firstの最終使用後。
second@follow += 1
r@follow += 1                 // 子の最終使用後はrを再利用できる。

let held = r
// r@follow += 1              // エラー：heldを後で使うため競合する。
held@follow += 1

_ = r                         // 子を作って直ちに破棄する。rは消費しない。
let transferred = r@move      // 参照値自体を転送する。以後rはMoved。
transferred@follow += 1
```

保存した再借用は、その時点で有効になる（§15.6.7の予約は、呼出しの入力を直接準備する借用だけに使う）。

### 2.2. 期待型への適応

- 型推論は元の完全Typeで行う。期待型が固定されていれば、§10.2の適応を一度だけ適用する。既定の再借用を実行してから適応する二段階の処理はしない。
- 既存の適応は変えない：所有値の共有借用、排他的参照の共有化、参照層から一つの共有参照を得る操作、read Typeの値読み出し、literal fitting、Origin fitting。
- 同値になるのは、同じ完全Type・Origin条件の注釈だけである。共有化や値読み出しを求める注釈は、その適応を選ぶ。結果型を省略した名前付き関数がUnitを返す規則も変えない。
- 候補評価は計画だけを作り、Loanを生成しない。同じ型の再借用の適応順位をExactに変えず、関数消去変換と結果型による絞込みも変えない。

```kimi
func validate(node: ref/Node) => ()
func normalize(node: uniq/Node) => ()

func work(r: uniq/Node)
    validate(r)               // 共有化（従来どおり）。
    normalize(r)              // 同じ型の排他的再借用（従来どおり）。
    let saved = r             // 改訂後は再借用。let saved: uniq/Node = r と同じ。
    normalize(saved)
```

### 2.3. Placeの選択

Field・Tuple要素・添字は**Placeを選ぶ操作**である。選んだPlaceの値種別に、本書 2.1・2.2を適用する。

- 選んだPlaceが`uniq/T`を保持していれば、経路の権限が足りる限り、記号なしで再借用する。
- 選んだPlaceが所有値なら、Receiver以外の位置で排他的に貸すには、そのPlaceに記号を付ける（現行どおり）。親に付けた記号は、子の所有Placeを貸す記号の代わりにならない。
- 添字の`index`と`indexUniq`は、最終取得計画が要求する能力で選ぶ（§4.6.9）。最終取得計画には、既定の取得、期待型の適応、書込み、明示借用、Subject、Receiverの取得を含む。
- 合成した`indexUniq`の受信側の取得はPlace選択の一部であり、記号を要求しない。`let`の所有コレクションや共有経路からは、従来どおり排他を得られない。

```kimi
// holder: var Holder（items: Array<i32>、link: uniq/Counter）
// values: var Array<Counter>、refs: var Array<uniq/Counter>
// fill: uniq/Array<i32>を受け取る関数
let link = holder.link        // 参照Field：既定の再借用。
let first = refs[0]           // indexUniqを選んで再借用。let first: uniq/Counter = refs[0] と同じ。
fill(holder.items@uniq)       // 引数で所有Fieldを貸すときは、そのFieldに記号を付ける（現行どおり）。
// fill(holder@uniq.items)    // エラー：holderを貸してもitemsの記号は省けない（現行どおり）。
values[0].bump()              // Receiverは§7.3の暗黙取得。indexUniqの選択に記号は不要。
```

### 2.4. ジェネリック

- 宣言制約のadmitted setの**各場合**について、本書 2.1の取得と、その後の使用・Loan・cleanupが合法であることを定義時に検査する。
- Copyと再借用の有限な条件付き計画と、各場合の依存を保持する。全場合で合法である必要があるが、所有の場合に架空のLoanを作る単純な合併はしない。
- 未知のCopyをNon-Copyとみなさない。暗黙の制約、一般論理ソルバー、具体化後の候補の再選択は追加しない。有限な証明規則で表せない計画は定義エラーとする。

```kimi
func inspect<s/T>(value: s/T)
    s is owner or uniq
    T is Copy
    let local = value         // owner：Copy、uniq：再借用。
    // _ = value              // エラー（定義時）：uniqの場合にlocalと競合する。
    _ = local

func transfer<T>(value: T) -> T => value@move

func invalid<T>(value: T)
    let local = value         // エラー（定義時）：Copyも排他的参照も証明できない。
```

### 2.5. Capture

明示Captureの各項目を**環境束縛の初期化**とする。右辺は外側のBinding Identityを指し、Closureの作成時に左から右へ取得する。

| Capture | 相当する初期化 |
| --- | --- |
| `[x]`／`[var x]` | `let x = x`／`var x = x` |
| `[x@op]`／`[var x@op]` | `let x = x@op`／`var x = x@op` |

- `op`は既存の`move`・`ref`・`uniq`だけである。`@ref`・`@uniq`は通常式と同じく束縛のスロットを借用する（§13.5.5.2）。参照用の特例表は削除する。
- `var x@ref`／`var x@uniq`を新たに許可する。`var`は環境束縛の可変性だけを変え、元の権限を強めない。
- 任意式、別名、項目間の参照、Field、`@copy`、追加のobject借用構文は導入しない。重複名・引数との衝突・contextual bindingの制限は維持する。
- Capture Listの省略は、Copyだけを取得する現行規則を維持する。本体から取得方法を推測しない境界であり、既定の再借用を適用しない。
- スロットを借用するClosureは、そのスロットより長く生存できない。呼出し権限・escape・cleanupは従来どおり検査する。
- 入れ子の`[x@uniq]`は、外側の環境束縛が`let`ならエラーとし、`var`なら呼出し権限とLoanで判定する。

参照を保持する束縛では、結果が次のように変わる（上の規則から導かれる早見表）。

| 取得元 | `[x]` | `[x@move]` | `[x@ref]` | `[x@uniq]` |
| --- | --- | --- | --- | --- |
| `ref/T` | 参照のCopy | 参照の転送 | スロットの借用`ref/(ref/T)`（現行：参照のCopy） | `var`の束縛ならスロットの借用（現行：エラー） |
| `uniq/T` | 再借用（現行どおり） | 参照の転送 | スロットの借用`ref/(uniq/T)`（現行：共有再借用） | `var`の束縛ならスロットの借用（現行：排他的再借用） |

```kimi
var number: i32 = 1
let r = number@uniq
let view: ref/i32 = r         // 参照先を共有するなら、先に共有化する。
let reader = func [view] () -> i32 => view@follow
let snapshot = reader()

var bump = func [r] () => r@follow += 1  // [r]：uniq/i32への再借用。
bump()                        // Exclusive呼出し。Closureの取得は§7.6.3のまま。
// [r@move]：参照値の転送。[r@ref]：rのスロットをref/(uniq/i32)として借用。
// [r@uniq]：エラー。letのスロットはWriteを持たない。
```

### 2.6. 変更しない規則

| 規則 | 扱い |
| --- | --- |
| Receiver Expression（§7.3、§7.6.3、§8.6、§11.2） | 所有Place・所有一時値の暗黙の排他取得、基底・object・完全Sealed payloadのprojection、Receiver形の統一、評価順、予約をすべて維持する。既定の再借用はReceiverの取得を変えない |
| 書込み構文（§13.7） | 代入・複合代入・inc/decの取得を変えない |
| 言語が生成する呼出し | 反復・比較・formatting・合成した`indexUniq`は、構文と公開契約が指定する取得を使う。生成されたことだけでは権限を与えず、読取り構文から新たな排他権限を推論しない |
| Place返却・Subject（§7.1.1、§15.1.6） | `place ref/T`／`place uniq/T`は公開PlaceにTakeを与えない。`match`／`for`のin-place規則を維持し、束縛後の値取得だけを本案の対象とする |
| 後置操作の優先順位（§13.1） | 変えない（本書 7.3） |

```kimi
struct Counter
    var count: i32 = 0
    public func bump(self: uniq/Self) => self.count += 1

func touch(target: uniq/Counter) => target.bump()

var counter = Counter.init()
counter.bump()                // Receiverは§7.3の暗黙の排他取得（現行どおり）。
touch(counter@uniq)           // 引数では明示する（現行どおり）。
let r = counter@uniq
let saved = r                 // 改訂後は既定の再借用。
saved.bump()                  // 排他的参照のReceiverは再借用（現行どおり）。
r.bump()                      // savedの最終使用後。
```

## 3. Explicit Acquisition案との関係

[Explicit Acquisition in Overload Resolution案](2026-10-01%20Explicit%20Acquisition%20in%20Overload%20Resolution.md)は、正式仕様へ取り込み済み（`868ce9f8`）で実装済み（`7ba4db3a`）である。本案はその後に取り込む。

- **役割分担：** Copyと新規共有借用の競合は相手案（§10.2.2）、再借用は本案が定める。
- **分類：** 既定の再借用は、§10.2.2の値取得にも新規共有借用にも当たらない。実装では`AdaptInput`が名付ける取得計画の`Other`である。
- **条件付き計画：** Copyの場合を含むなら、相手案の競合検査も適用する。
- **文面の調整：** §10.2の「A bare Non-Copy or Copy-unproven Place cannot be acquired by value」は、排他的参照を除く形に直す。

## 4. 実装計画

### 4.1. 実装単位

着手時に最新の[CODEMAP](../../docs/dev/CODEMAP.md)と作業差分を確認する。

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U0：棚卸し | 各規則の受理・拒否・依存を独立に定義する。既定の再借用とC1がLibrary・例・milestoneへ与える影響件数、object・pair・Captureの未対応範囲を調べる | 入力事実と候補ごとの判定が分離され、改修範囲と検証の期待値が明確 |
| U1：既定の取得・Place選択 | `Binding.ArgumentOperations/Expressions/Calls/ValueCalls/Indexers`で、既定の再借用・期待型の適応・添字の能力選択を一つの取得計画にする | 同型注釈・generic・添字で意味が一致し、所有権解析と生成まで通る。Receiverの挙動は不変 |
| U2：Capture | `Binding.Closures`と`OwnershipAnalysis.Closures`で、明示Captureを環境束縛の初期化として扱う | 参照用の特例を除き、スロット借用のescape・入れ子・`var`を検証 |
| U3：相互作用・統合 | `OwnershipAnalysis`のBorrows/Reservations、`OwnershipBody`、関連する`BodyLowering`で依存・cleanup・再解析を確かめる | 診断、native、allocation/reuse、Session検証が完了し、残る未対応を記録 |

### 4.2. 共通設計

- `ExpectedAdaptation`と`AdaptInput`に別々の例外を足さない。元の型・値種別・明示操作・経路権限・Copy証拠は式ごとに一度求めて共有する。期待型、基底path、候補の契約とOrigin適合は候補ごとに判定する。
- Receiverの取得経路（`AdaptInput`の`receiver`指定、`Binding.PropertyCalls`、Closure呼出し）は変更しない。
- 共有は同じ解析snapshot・generic環境の中に限る。候補試行と確定状態を混用しない。

### 4.3. 性能方針

| 案 | 採否と制約 |
| --- | --- |
| P1：取得元の情報を一度だけ計算 | 採用。型・Origin・制約の照合を含む判定全体のO(1)は保証しない |
| P2：小さな計画を保持 | 採用。既存の表と再利用bufferを優先し、配置は測定で決める |
| P3：既存アドレスの再利用 | 条件付き採用。call-onlyの再借用と同じ型の一時参照で、不要な中間slotを作らない。ABI・アドレス取得・実体化が必要な一時値は除き、O0/O2のIRと動作を確かめる |
| P4：重複分岐の削減 | 採用。Captureの参照用の重複規則を除く。コンパイラの単純化であり、生成プログラムの高速化とは区別する |

### 4.4. 進め方

- 各単位は必要なら小分けし、再現例→実装→focused test→診断レビュー→正式Unit検証まで完結させてコミットする。下流の解析・診断を先送りして完了扱いにしない。
- 未対応の形式を言語エラーに置き換えて範囲を縮めない。入口固有の例外が必要と分かったら、実装を広げる前に再評価する。
- hot pathのallocation/reuse回帰と既存のbinding Benchmarkを比較する。
- 開発中はtest projectの増分buildと選択したmethodを使う。単位の完了時は`./scripts/verify.ps1 -Class <関連class> -Fixtures '<関連pattern>'`と該当milestone、最後に`-Mode Session`を一度実行する。nativeはO0/O2で確かめ、NativeAOTは実行しない。
- 検証中はソースを変えない。失敗を含む証跡は`artifacts/verify/`、測定は`artifacts/benchmarks/`に置く。

## 5. 検証・診断計画

| 観点 | 必須ケース |
| --- | --- |
| 取得 | 同型注釈の有無、Copy／Non-Copy、ref／uniq・object借用、一時参照、明示Move、aggregate、discard、共有経路上のuniq |
| Place選択・Loan | local／Field／Tuple／配列／Dictionary／公開Place、所有要素と参照要素の添字、`let`・共有経路のコレクション、Subjectの添字、スロットと参照先、子の生存中と終了後 |
| Receiver（回帰） | 暗黙の排他Receiver・getter・Closure呼出しが不変であること。既定の再借用で得た値・Field・要素のReceiver、基底・object・pair経路、戻り値の依存 |
| 予約・保存 | 保存した再借用の即時の有効化、呼出しの予約との区別、位置の一回評価、共有読取り、重複排他、default・中断・Abort |
| Capture・generic | 明示／省略、`var`と借用、入れ子の`let`／`var`環境、escape、owner／uniq混在のCopy証拠・後続使用・cleanup |
| 再利用・費用 | 失敗後の再Binding、genericの置換、ゼロサイズ値、保持容量・allocation、必要／不要な一時slot |

- 既存の関連テストを起点にする。無差別な直積は作らず、拒否例ごとに独立した正常例を用意する。
- 診断は明示不足・権限不足・Loan競合・未証明・実装未対応を区別し、貸出位置、親子Loan、競合使用を示す。
- 修復案は意味・評価順・依存の変化と前提を示し、合法性を確かめられない編集を確定修復として提示しない。
- CLI/LSPの範囲・説明・独立エラーを確かめる。CSPの修復はsource snapshotに結び付け、Adviceから自動編集を推測しない。

## 6. 文書更新計画

取得の意味を重複して定義しない。正式仕様での担当は、既定の取得が§3.5、期待型の適応が§10.2、Place選択が§3.4.1・§4.6.9、Captureが§7.6.2、合法性が§15.1.5である。§7.3のReceiver取得表と§15.1.5のReceiver例外は維持する。

| 文書 | 正式取り込み時の更新 |
| --- | --- |
| §3.5 | 既定の取得の表、適用位置、再借用の対象と範囲、`_ = r` |
| §10.2・§10.2.1・§10.2.2 | 注釈の同値性、`let saved = r`の例、既定の再借用が取得競合の計画に当たらないこと、本書 3の文面調整 |
| §3.4.1・§4.6.9 | Place選択、添字の能力を最終取得計画から選ぶこと、合成した`indexUniq`の受信側の取得 |
| §7.6.2 | Captureを環境束縛の初期化として再定義し（C1）、参照用の表を削除 |
| §8.9・§8.10 | 有限な条件付き取得と全場合の検証 |
| §13.5.5.2・§14.2.4 | 既定の再借用の例、discardの再借用 |
| §15.1.5・§15.1.6・§15.6.7 | 値種別の取得表（期待型のない借用値）、Subjectの記述、保存した再借用の即時の有効化 |
| 付録E・SPEC.md | Bare acquisitionとLending ruleの説明を更新（Receiverの例外は維持） |
| §22・UTF-8 profile・IMPL・付録A/B | 影響する例と要求を同期 |
| STYLE・Library・docs/examples・milestone | Captureの変化を意味に沿って更新し、公開宣言・保証が変わる場合はLIBRARYも更新 |
| CODEMAP・DIAGNOSTICS・PLAN・PLAN_HISTORY・STATUS | 責務・入口・診断の変更を反映。PLANは200行未満、履歴は数行、STATUSは検証済みの対応境界だけ |
| draft/INTEGRATED.md | 同じコミットで、取り込んだ節・移行先・両案の順序を記録し、その範囲を凍結 |

正式仕様は英語で自立させ、draftへの参照を残さない。未決範囲は部分取り込みとして区別し、凍結済みの提案は編集しない。取り込みと凍結は実装完了を待たない。pre-alphaのため、version bumpや移行文書は追加しない。

## 7. 設計判断

### 7.1. 原則との対応と受け入れる欠点

- **原則1：** 既定の取得表とCaptureを統一し、添字をFieldと同じPlace選択として扱う。Receiverの暗黙取得は§7.3の一つの表に閉じた例外として残す。
- **原則2・3：** 同型注釈で意味が変わらない。Receiver以外での所有Placeの排他貸出は記号で見え、Receiverの排他取得はReceiver形の統一と公開シグネチャから局所的に分かる。
- **原則4：** 原因と前提を示す修復と、source snapshot単位の検証で対応する。

既存のLoan・予約・有限なgeneric計画を使うため、実現できると判断する。次は欠点として受け入れる。

- **Captureの変化：** `[x@ref]`などで型と寿命が変わる。
- **Receiverと引数の文脈差：** 所有Placeの排他貸出は、Receiverでは暗黙、引数では`@uniq`のまま残る。

### 7.2. Receiverの暗黙取得の維持

改訂前の本案は、所有Placeから排他的Receiverを作る省略を廃止し、`counter@uniq.bump()`と書くことを求めていた。これを採らず、§7.3を維持する。

- 排他的メソッドの呼出しは頻出であり、明示を求めると記述負担と移行費用が大きい。2026-10-01の試算では、Receiverに明示を必須にすると既存テスト14,748件中8,483件が失敗した。主因は、埋め込みライブラリ（`Iteration.kimi`の`self.iterator.next()`など）が暗黙の排他Receiverに依存していることである。
- 現行規則でも、Receiver形の統一、公開シグネチャ、候補選択後のLoan検査により、取得は呼出し位置と宣言から局所的に決まる。
- 本案の他の項目は、Receiverの取得に依存しない。

### 7.3. 採らなかった案

| 案 | 理由 |
| --- | --- |
| Receiverでも所有Placeの排他貸出を明示する | 本書 7.2のとおり、記述負担と移行費用に見合う局所性の向上がない |
| 後置操作の優先順位の変更（`-x@move`を`-(x@move)`とするなど） | Receiverを明示しない方針では主な動機がなくなり、既存の使用例は0件だった。`@uniq`と`@uniq/T`で結合が分かれ、生ポインターの参照外し（`*p@copy`、`*p@move`）と組み合わせると意味が変わる。必要なら別提案で扱う |
| 再借用も常に明示する | 引数・返却・ヘルパー連携の記述負担が大きい |
| 専用の再借用演算子 | 既存の操作と概念が重複する |
| 引数でも、排他的なアクセス経路なら記号を省く | 新規の排他借用の暗黙化が引数へ広がり、Copy能力によって候補が切り替わる（§10.2.2と矛盾する） |
| 添字を読取り構文として扱い、排他を推論しない | 同型注釈の同値性と、Fieldとの一貫性を壊す |
| 共有Receiverの前の`@uniq`を一律に警告する | 排他権限を意図して取得・維持する用途があり、冗長とは言えない |
| `_ = r`への専用の警告 | 意図的なdiscardから、早期解放の意図を推測しない |
| 記号で同名のReceiver形を選ぶ | `Uniq`接尾辞はReceiver形の統一と公開APIの区別を担うため維持する。必要なら別提案で評価する |

実装への影響件数、全経路の対応、計画の格納方法、性能差はU0以降に確かめる。現時点で完了・高速化・無劣化は保証しない。
