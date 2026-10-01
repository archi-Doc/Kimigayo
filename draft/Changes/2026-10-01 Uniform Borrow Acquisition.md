# 借用・再借用の取得規則の統一案

日付: 2026-10-01
状態: 提案・設計レビュー済み。未取り込み・未実装。

本書は正式仕様への変更提案であり、現在の言語規則を変更しない。例は改訂後の期待動作を示す。正式仕様・実装との照合による机上検証を行ったが、改訂後のコンパイラによる実行検証は未実施。

## 1. 目的と現仕様の問題

目標は、**既存参照の再借用は取得元の型で判断し、所有値を新しく排他的に貸す呼出しは明示する**ことである。新しいキーワード、借用型、実行時の検査は追加しない。

| 現仕様 | 問題 | 根拠 |
| --- | --- | --- |
| `r: uniq/T`で`let s = r`は拒否、`let s: uniq/T = r`は再借用 | 同じ型の注釈が取得操作の可否を変える | [§10.2.1](../../docs/spec/10-overload-resolution-and-inference.md#1021-inference-and-adaptation-classes) |
| 所有値の通常引数には`@uniq`が必要だが、Receiverは暗黙に排他借用できる | 呼出し形式・getter・Closureによって記法が変わる | [§7.3](../../docs/spec/07-functions-and-callable-values.md#73-explicit-receivers) |
| Captureの一般表は裸取得をCopyに限定するが、参照用表は`[r]`を排他的再借用とする | 同じ節の受理条件が一致しない | [§7.6.2](../../docs/spec/07-functions-and-callable-values.md#762-capture-acquisition-and-environment) |
| Captureの`r@uniq`は参照先を再借用し、通常式の同じ表記は参照スロットを借用する | 一つの表記が異なる場所を指す | 同上、[§13.5.5](../../docs/spec/13-operators-and-assignment.md#1355-follow-borrow-and-reborrow) |

現仕様の推論は無制限ではなく、Receiver形の統一、公開契約、候補選択後のLoan検査によって局所性を確保している。本案は安全性の欠陥を主張するものではなく、取得規則の文脈差を減らす提案である。

## 2. 理想的な動作

1. 同じ完全Typeと同じOrigin条件の注釈を付け外ししても、取得操作・Loan・cleanupが変わらない。
2. 値の転送は`@move`、所有値からの新しい排他借用は`@uniq`または`@objuniq`で読める。
3. 再借用は元の参照をMovedにせず、子Loanが生きている間の競合操作を禁止する。
4. 共有化・値読み出し・再借用を連鎖させず、元の式から一つの最終取得計画を作る。
5. 型推論、取得計画、実際のLoan合法性を分離し、後段の失敗で名前解決や候補選択をやり直さない。

## 3. 仕様変更

### 3.1. 既定の値取得

§3.5を取得規則の主担当とする。明示操作と、固定された期待型への適応がない場合は、次の表を適用する。

| 取得元 | 既定の取得 |
| --- | --- |
| Copyを証明できるPlace | Copy。参照なら参照値をCopyする |
| `uniq/T`・`objuniq/T`を保持するPlace | 同じSemanticsの再借用。経路の権限とLoan合法性を要求する |
| その他のNon-CopyまたはCopy未証明のPlace | 拒否。転送可能なら`@move`を要求する |
| 一時値 | 通常の一時値転送 |

適用先は初期化、代入元、引数、値返却、既存の結果ソース、aggregate要素、enum payload、default、明示Captureの裸の項目である。`var`によるCapture先の可変性は取得方法と独立に扱う。

再借用は**外側が排他的参照と証明された値**に適用する。`Option<uniq/T>`や参照を含むstruct全体を暗黙に分解・再借用しない。`[r]`という配列リテラルでは要素式`r`に適用するが、既存配列の裸取得には適用しない。

`let`に保持された排他的参照も、その参照先の権限が許せば再借用できる。`let`の所有値を新しく排他借用できるようにはしない。共有経路内の排他的参照から排他権限を回復しない。

型・Origin・実際のLoan祖先を保持し、初期化状態、スロットの保護、親子Loan、破棄時の依存を現行どおり検査する。参照スロットのOriginを参照先のOriginと混同せず、不必要なスロット依存も追加しない。

```kimi
var number: i32 = 1
let r = number@uniq

let first = r                    // 改訂後: uniq/i32への再借用。
first@follow += 1
let second: uniq/i32 = r          // 同じ取得。firstの最終使用後。
second@follow += 1
r@follow += 1                    // 子の最終使用後は再利用できる。

let held = r
// r@follow += 1                 // heldを後で使うため、競合して拒否。
held@follow += 1

let transferred = r@move         // 参照値自体の転送。
// r@follow += 1                 // Movedなので拒否。
transferred@follow += 1
```

`@copy`はCopyのみであり、再借用の別名にしない。`@move`の結果を後から再借用・共有化・値読み出しで修正しない。同じ型の一時参照はそのまま渡し、不要な再借用を生成しない。

### 3.2. 期待型と推論

型推論は元の完全Typeから行い、取得処理によって未知の型を逆算しない。期待型が固定されている場合は§10.2の適応を選び、それ以外は§3.1の既定取得を選ぶ。既定再借用を先に実行してから適応する二段階処理は禁止する。

次の適応は維持する。意味が固定された期待型・演算契約から判断でき、読み取りやPattern・反復での実益があるためである。

- 所有値から`ref/T`、所有object handleから`objref/T`への共有借用。
- 既存の排他的参照から共有参照への再借用。
- safe value-referenceの層から一つの共有参照を得る操作と、その既存の依存計算。
- read Typeへの値読み出し、literal fitting、既存のOrigin fitting。

同じ型の排他的再借用は、型注釈・明示Type引数の有無で受理が変わらない。共有化や値読み出しを指定する異なる期待型、異なるOrigin制約まで同値とするものではない。結果型を省略するとUnitになる名前付き関数の規則も変更しない。

候補評価は計画のみ作り、実際のLoanを作らない。既存の適応順位を維持し、期待型のない再借用を追加したことを理由に同型の再借用をExactへ変更しない。Copyによる値渡し候補の優先、関数消去変換、結果型による候補絞込みは本案で変更しない。

### 3.3. 排他的Receiver

所有値から排他的Receiverを作る省略を廃止し、通常引数と同じ明示を要求する。所有一時値、メソッド、custom/computed/required getter、直接・間接Closure呼出しも同じ規則とする。

| 入力・操作 | 改訂後の表記 |
| --- | --- |
| 所有値の排他的メソッド | `node@uniq.normalize()` |
| 通常関数の排他的引数 | `normalize(node@uniq)` |
| `r: uniq/Node`のメソッド | `r.normalize()`。既存権限から再借用 |
| 所有値の排他的getter | `meter@uniq.reading` |
| 所有ClosureのExclusive呼出し | `(next@uniq)()` |
| 所有一時値の排他的メソッド | `makeNode()@uniq.normalize()` |
| 所有object handleの排他的object借用 | `handle@objuniq.method()` |

value参照、object借用、完全Sealed payload、基底projectionの区別は維持する。必要な取得がpayloadのvalue借用なら`handle@follow@uniq`を使う。object型変換、完全性、ObjectCallCompatibleの既存条件を明示記号で免除しない。

記号は**実際に新しく貸すPlace**に付ける。`self: uniq/Self`でも、所有Fieldには`self.items@uniq.append(...)`が必要である。`holder@uniq.items.append(...)`を`holder.items@uniq.append(...)`と同値にはしない。共有経路、不可変所有値、変更禁止のgetter-result storageは、記号を付けても不正のままである。

同名グループのReceiver形統一、lookup、member/indexの参照経路選択、評価順は維持する。共有Receiverの省略を維持するため、共有アクセスだけで済む読み取りに記号は増えない。読み取り構文でも排他的getterには明示が必要である。

```kimi
struct Counter
    var count: i32 = 0

    public func bump(self: uniq/Self) => self.count += 1

    public func read(self) -> i32 => self.count

var counter = Counter.init()
// counter.bump()                // 改訂後は明示不足。
counter@uniq.bump()
let current = counter.read()

let n: i32 = 0
var next = func [var n] () -> i32
    n += 1
    return n
let value = (next@uniq)()
```

### 3.4. 書込み・Place・Subject・予約

本案は通常の値取得と呼出し入力の規則を変える。次の構文・契約は別の操作を明示しており、Receiver例外の代用品にはしない。

- `x = value`、複合代入、inc/dec、添字更新は書込み操作。書込み先の権限を取得し、追加の`@uniq`を要求しない。getterを呼ぶ途中の経路は、そのgetter自身の入力規則を満たす必要がある。
- `place ref/T`・`place uniq/T`の返却はPlace指定。返却元の値取得を新設しない。公開Placeに格納された排他的参照の通常取得には§3.1を適用できるが、Takeは付与しない。
- `match`・`for`のbare Place Subjectは現行のin-place取得を維持する。Patternで作られた束縛を後で値取得するときに§3.1を適用する。
- 反復、indexer、setter、比較、formattingの内部呼出しは、元の構文・公開契約から許可された権限を使う。生成コードだから所有Receiverを自由に排他借用できる、という実装にはしない。

呼出しの最終入力を直接準備する明示借用・再借用は、現行§15.6.7の予約を使う。位置を一度だけ評価し、全引数・defaultの準備後に活性化する。明示借用とcall-only Reborrowを一つの準備として扱い、途中で活性化しない。

```kimi
var values: Array<isize> = [1]
values@uniq.append(values.length)        // 予約中の共有読み取りは可能。
// values@uniq.append(values@uniq.remove(0)) // 排他的予約が重なり、拒否。
```

ローカル・aggregate・Captureに保存する再借用は即時活性化する。保存、別呼出し、制御式を越えて予約を持ち越さない。代入先は呼出し予約を開始しない。`$tryWrite`の即時活性化、引数中断、Abort、cleanup、結果が保持するLoanも維持する。

### 3.5. ジェネリック

定義時に、宣言された制約から全許容Semanticsで取得を証明する。外側が借用と証明される入力には参照の規則を使い、所有の場合にはCopy証拠を要求する。Semantics pairは既存のadmitted setに対する有限の条件付き計画を持ち、結果型と各場合のLoanを保持する。

一般の未知の`T`には、Copyまたは参照であると仮定しない。既存の証明規則で必要な場合分けを証明できなければ定義エラーとし、暗黙の制約、新しい一般論理ソルバー、具体化後の再選択を導入しない。

```kimi
func inspect<T>(value: uniq/T)
    let local = value           // 外側がuniqなので再借用できる。

func transfer<T>(value: T) -> T => value@move

func invalid<T>(value: T)
    let local = value           // Copyも参照形も証明できず、定義エラー。
```

### 3.6. Captureと追加改善案

基本案では、明示リストの`[r]`・`[var r]`を§3.1へ揃え、既存表の不一致を解消する。`var`は環境内束縛の可変性だけを指定する。Capture List省略は引き続きCopy限定であり、本体から借用を推測しない。左右順、外部Origin、ClosureのCall Receiver Requirement、escapeとcleanupを維持する。

**追加案C1（採否判断が必要）:** 明示Captureの`x@ref`・`x@uniq`も通常式と同じスロット借用に統一する。参照値に対する特例を削除し、`x@uniq`には通常どおりスロットの書込み権限を要求する。参照先の共有化が必要なら、既存構文で先に参照値を作る。

```kimi
var number: i32 = 1
let r = number@uniq
let view: ref/i32 = r
let reader = func [view] () -> i32 => view@follow
let snapshot = reader()

// C1では [r@ref] はref/(uniq/i32)としてrのスロットを借りる。
// [r] はuniq/i32として再借用し、[r@move]は参照値自体を転送する。
```

C1は同じ演算子を一つの意味にできる一方、既存Captureの型・依存先を変更する。スロットを借りるClosureはそのスロットより長生きできず、Capture時の借用は即時活性化する。`var x@ref`・`var x@uniq`は引き続き許可せず、Captureに任意式、別名、新しいobject借用記法も追加しない。C1を採らない場合は、Capture固有操作であることを例外として明記する必要がある。

## 4. 実装計画

着手時に作業差分と最新CODEMAPを確認する。下記は既存コードを読んだ変更候補であり、動作・性能の検証結果ではない。

| 単位 | 作業・主な入口 | 完了条件 |
| --- | --- | --- |
| U0 仕様 | 取得表、generic、Capture、Place・書込み・予約の境界を確定し、実装から独立した期待値を作成 | 各境界で受理・拒否と結果依存が一意。C1を採用・不採用のどちらかに決定 |
| U1 取得 | `Binding.ArgumentOperations`、`Binding.Expressions`、`Binding.Calls`、`Binding.ValueCalls`、`Binding.Closures`で共通の取得計画を使用し、対応する所有権解析・生成まで実装 | 同型注釈の有無、直接・generic呼出し、明示Captureで意味が一致。候補試行でLoanを生成しない |
| U2 Receiver | `Binding.PropertyCalls`、object/payload/pair/projection、Closureを含む暗黙排他取得を撤去。各変更で影響するLibrary・例・テストも更新 | 所有値の明示不足を全入口で拒否。既存参照、書込み・反復等の契約を維持 |
| U3 相互作用 | `OwnershipAnalysis`、`OwnershipAnalysis.Borrows/Reservations/Closures`と`OwnershipBody`、関連`BodyLowering`で複合ケースを整備 | 親子Loan、格納参照、予約、返却、cleanup、再解析を検証。下流で取得方法を再推論しない |
| U4 診断・統合 | 各単位の診断レビューを総合確認し、native・allocation・Sessionを検証 | 公開説明が正確で、対応境界と未解決事項が記録されている |

既存の`ExpectedAdaptation`は期待型を前提とし、`AdaptInput`はReceiverの例外を持つ。二か所へ独立に分岐を追加せず、取得元・明示操作・期待型・経路権限から共通計画を作る。`BoundAdaptation`/`BoundArgumentOperation`等の既存表現と再利用バッファを優先する。式評価用と候補評価用で可変状態を混用しない。

U1–U3は必要に応じて取得元・入口ごとの小単位に分け、各小単位をBindingから所有権解析・生成まで完結させる。再現例→実装→focused testで進め、正式Unit検証後にコミットする。下流処理や診断を後の単位へ先送りして完了扱いにしない。仕様変更に伴うソース更新は型・取得計画を確認し、`.method`への機械的な`@uniq`挿入は行わない。

## 5. 検証計画

| 観点 | 必須の対・確認 |
| --- | --- |
| 型注釈 | 同じ完全Type・Origin条件の有無で受理、取得、Loan、cleanupが一致 |
| 所有・参照 | Copy/Non-Copy、ref/uniq、objref/objuniq、一時参照、明示Move、参照を含むaggregate |
| 経路 | local、Field、Tuple、配列、Dictionary、公開Place、共有経路。参照スロットと参照先を区別 |
| 生存期間 | 子の使用中の親操作を拒否し、終了後の再利用は許可。返却、格納、Capture、破棄の依存も検査 |
| Capture | 明示リストと省略リスト、`var`、nested Closure、環境のShared/Exclusive権限。C1採用時はスロットのescape拒否と参照先への依存を区別 |
| 呼出し | メソッド、unbound、generic、Contract、getter、Closure、payload・基底経由、内部protocol呼出し |
| 予約・実行 | 位置の単一評価、共有読み取り、重複排他、default、中断、Abort、即時借用との区別 |
| 解析再利用 | 再Binding、失敗後の再解析、ゼロサイズ値、保持容量、warm時のallocation |

既存の`ReferenceLayerAdaptationTest`、`ImplicitReceiverTest`、`CallReservationTest`、`StoredReferenceLoanTest`、`BorrowDependencyStorageTest`、`ConcreteClosureTest`、Property/object関連テストを起点にする。型注釈の除去、Receiver/unbound表記、明示Moveへの置換という変形で、意図した同値性・差異を検査する。無差別な直積は作らない。

診断は明示不足、権限不足、Loan競合、未証明、実装未対応を区別する。実際の貸出位置、Receiver宣言、親子Loan、競合使用を示す。修正候補は、完全な式の評価順と依存を保ち、その変更で合法になる場合だけ提示する。CLI/LSPの範囲・説明・独立エラーを確認し、将来のCSPではsource snapshotに対する構造化修復へ結び付ける。Adviceから自動編集を推測しない。

開発中はtest projectの増分buildと選択methodを使う。単位完了時は`./scripts/verify.ps1 -Class <関連class> -Fixtures '<関連pattern>'`、該当milestoneも選択し、最後に`-Mode Session`を一度実行する。nativeはO0/O2、hot pathはallocation/reuse回帰と固定条件のBenchmark測定を行う。NativeAOTは実行しない。build/検証中にソースを変更せず、失敗分を含む証跡を`artifacts/verify/`、測定を`artifacts/benchmarks/`へ保持する。

## 6. 文書更新計画

| 文書 | 更新内容・時点 |
| --- | --- |
| SPEC §3.4–3.5、§7、§8.9–8.10、§10.2、§11、§13.5、§15 | 取得の主規則、Receiver、Capture、generic、演算子、Loanと予約を正式取り込み時に同期 |
| SPEC §4、§14、§22、UTF-8 profile、Appendices E/F、SPEC index | 内部protocolと通常呼出しの境界、用語、例、要約の整合を確認 |
| IMPL・Appendix A/B | 取得計画・所有権・生成の記述を点検し、関連する要求・参照モデルを同期 |
| STYLE、Library本体、docs/examples、milestone source | 新しい表記へ更新。元の評価順・結果・cleanupを維持し、無関係なstyle修正をしない |
| LIBRARY | public宣言・保証が変わる場合に同一コミットで更新。呼出し記法だけの変更とは区別 |
| CODEMAP、DIAGNOSTICS | 責務・入口・test参照と診断workflowの変更時に更新 |
| PLAN、PLAN_HISTORY、STATUS | PLANは現行計画のみ200行未満、履歴は数行。STATUSは検証済みの対応境界が変わったときだけ更新 |
| draft/INTEGRATED.md | 正式取り込みと同一コミットで本書の節→正式仕様を記録し、取り込んだ範囲を凍結 |

正式仕様の更新は英語で行い、draftへの参照・依存を残さない。未決事項を残して部分取り込みする場合は、その範囲と未決事項を記録する。取り込み・凍結は実装完了を待たない。pre-alphaのためversion bumpや移行文書を追加しない。既存の凍結済み提案は編集しない。

## 7. 精査結果と代替案

| 論点 | 評価・反映 |
| --- | --- |
| 実現可能性 | Borrow/Reborrow、経路権限、親子Loan、予約の既存モデルを利用できる。新しいruntime機構は不要。ただしobject・Capture・genericの全経路での実装完了は未検証 |
| 言語的利点 | 同型注釈の意味安定、通常引数とReceiverの統一、排他借用位置の可視化。新構文なし |
| 言語的欠点 | mutation呼出しが長くなる。裸の参照取得でも子Loanを作るので、後続操作の競合は意識する必要がある |
| 複雑性 | Receiver例外を削減できる一方、期待型なしの取得・generic・格納・Captureへ再借用を広げる実装が必要。改修量削減や高速化を未測定で主張しない |
| 安全性 | aggregate全体の再帰的再借用、共有から排他への昇格、暗黙Move、寿命延長は追加しない |
| 原則1 | §3.5に取得を集約。C1採用なら借用演算子の対象も統一できる |
| 原則2・3 | 元の型・公開制約・明示記号で判断。genericの本体から隠れた条件を推論しない |
| 原則4 | 修復の前提・保証・位置を明確化し、検証を正確なsource/configurationへ結び付ける。CSP全体の実装は本案に含めない |

代替案「再借用も常に`@follow@uniq`等で明示」は規則が単純だが、引数・返却・ヘルパー連携の記述量が大きい。「Receiver例外を維持して注釈差だけ解消」は変更が小さいが、今回の可視性の問題を残す。「専用の再借用演算子を追加」は既存操作と役割が重なる。これらより基本案を推奨する。

ただし、利益は主に言語規則の説明と編集の安定性にある。取得計画を共通化できず、入口ごとの新しい例外が必要になるなら、実装拡大前に基本案を再評価する。新たな一般効果システム、Copyによるoverload選択変更、参照層適応の全面廃止は同時に持ち込まない。

保存後の再精査では、①型注釈の同値性を名前付き関数の結果型省略へ広げない、②排他的getterを「読み取りだから記号不要」と説明しない、③C1でスロットのescape条件を明記する、④所有権解析・生成・診断まで完了してから各実装単位を閉じる、の四点を補正した。

## 8. 採否判断と残る確認

1. **基本案の採用:** Receiverの簡潔さを減らしても、所有値からの排他貸出を明示するか。本書は採用を推奨する。
2. **C1の採用:** Captureの借用演算子を通常式へ統一するか。本書は原則1のため採用を推奨するが、既存Captureの型・寿命の変化を伴うため別に決定する。
3. **実装見積り:** object、条件付きSemantics、格納参照、Captureの未対応範囲をU0で棚卸しする。未実装の形式を言語エラーにして計画を縮めない。完了日・速度改善・全経路の対応は現時点で保証できない。

基本案を採る場合、型注釈なしの再借用を有効にしながら一部の通常呼出しだけ暗黙排他Receiverを残す、という最終仕様にはしない。C1を保留して基本案だけ取り込む場合は、Capture演算子の既存特例を明示した部分取り込みとする。
