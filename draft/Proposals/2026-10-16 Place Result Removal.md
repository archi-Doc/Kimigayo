# Place結果の廃止と参照結果への統一

日付: 2026-10-16

状態: 廃止方針はユーザー支持済み。仕様案と実装計画。正式仕様への取り込み・実装・実行検証は未実施。

調査基準: `ada1862f`。[2026-10-15版](2026-10-15%20Place%20Result%20Removal.md)を参考に再構成した。以後の検討には本書を使う。

## 1. 目的と判断

**関数は値を返し、既存の記憶場所へのアクセスは参照で渡す。** `-> place ref/T` と `-> place uniq/T` を廃止する。変数、フィールド、添字、参照先を表すPlace自体は維持する。

現行のPlace結果には、関数をフィールドのように読み書きできる利点がある。現行仕様が原則に矛盾するための廃止ではない。この利便性より、関数型・Callable・適合判定・推論を通常の参照結果へ統一することを優先する。

添字の `a[i]` は引き続きPlaceを表す。一般の関数では必要に応じて `@follow` を書く。この分担により、添字の利便性を保ち、関数全般の特別な結果モードをなくす。

## 2. 仕様案

### 2.1. 規則1 — 関数の結果は型を持つ値

関数宣言、クロージャー、関数型、Callable、Contract要件の結果は通常の型とする。結果省略時のUnitなど、既存の規則は変えない。

既存の記憶場所を貸す関数は、共有アクセスなら `ref/T`、排他アクセスなら `uniq/T` を返す。返却・適合・型推論・効果・Originには通常の値結果の規則を適用する。

- 文法の `FunctionResult` と `PlaceResult` を削除し、各結果位置を `Type` にする。`place` は通常の名前として使える。
- 関数型の同一性と互換性に結果モードを含めない。
- `index` の参照結果も、通常の関連型推論の証拠になる。添字専用の推論規則は作らない。

### 2.2. 規則2 — 参照先の選択には既存の規則を使う

`f()` が参照を返す場合、呼出し式はその参照値を生む。`f()@follow` は、その参照が指すPlaceを選ぶ。`@follow` 自体は格納値をCopy・Moveしない。

| 参照 | 参照先の能力 |
| --- | --- |
| `ref/T` | Read、共有借用 |
| `uniq/T` | Read、Write、共有・排他借用 |

参照先からのTakeは認めない。値の取得、参照層を通したメンバー選択、固定された期待型へのAdaptationは既存規則に従う。すべての参照結果を暗黙に `@follow` する規則は追加しない。

寿命とLoanも既存の参照規則に従う。

- 参照先を借用した結果は、必要な参照先・親Loanへの依存を保持する。参照値を一時的に保持したスロットへの依存は追加しない。
- `during self` ならレシーバの借用に依存する。`during self.source` などでその外側の借用から独立できるのは、宣言された契約と本体の検査が許す場合だけである。Originの名前だけで実際のLoanを捨てない。
- 排他参照の再借用は親の範囲内に限る。`during self.source` と書いても、フィールド経由の再借用をレシーバより長く生かせない。Unsafeの記憶領域分割には通常の安全性の義務が残る。

### 2.3. 規則3 — 添字式は選択した参照の参照先を表す

単一要素の添字アクセスは、次のContractを使う。

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: ref/Key) -> ref/Element during self

contract UniqIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: ref/Key) -> uniq/Element during self
```

適合を通した `receiver[key]` は、選択した関数が返す参照の参照先を表す。格納型は `Element` で、能力と依存はその参照に従う。同じキーに対する両関数は同じ要素を指す。

選択は現行の[§4.7.1](../../docs/spec/04-arrays-indexing-and-slices.md#471-index-expression-resolution)を保つ。

1. レシーバとキーから適合を一つ選ぶ。期待する結果から `Key` を推論しない。
2. 読み出し・共有借用は `index`、更新・排他借用・排他再借用は `indexUniq` を選ぶ。
3. 必要な能力をレシーバの経路が持つことを検査する。失敗しても別の関数へ選び直さない。

レシーバとキーは呼出し中だけでも必要なLoanで保護する。結果が保持する依存は参照結果の契約に従い、キー引数を借用したLoanは結果に残さない。要素型の内部Originと実際のLoanは通常どおり保持する。

以下も現行規則を保つ。

- 評価順序・評価回数、位置の正規化、境界検査、キー不在時のAbort。
- `a[i] = v`、`a[i]@ref`、`a[i]@uniq`、入れ子の添字と射影。
- 所有する固定長配列の直接の要素射影と、静的なMove Pathに対するTake。参照経由のアクセスとは区別する。
- 範囲添字はスライス値を返す別の操作であり、今回変更しない。

`receiver.index(key)` と明示的に呼んだ場合は通常の参照値が結果になる。添字の意味は添字演算子が所有し、関数名や戻り値型だけでは有効にならない。

### 2.4. 使用例

```kimi
func first<T>(items: ref/Array<T>) -> ref/T during items
    return items[0]@ref

func firstUniq<T>(items: uniq/Array<T>) -> uniq/T during items
    return items[0]@uniq

var values: Array<i32> = [10, 20]
let copied = first(values@ref)@follow  // i32をCopyする。
firstUniq(values@uniq)@follow += 1     // 元の要素を更新する。
values[0] += 1                        // 添字の書き方は変わらない。
let borrowed = first(values@ref)      // ref/i32を保持する。
```

参照は通常の値なので、既存の寿命規則を満たせば `Option<ref/T>` などに格納したり、一般の高階関数へ渡したりできる。Place用の型やラッパーは設けない。

## 3. 移行で変わる意味

宣言から `place` を削除するだけでは移行できない。以下の `f` は、旧Place結果から対応する参照結果へ変更する関数を表す。

| 旧コード・条件 | 宣言だけ変更した場合 | 元の意図を保つ書き方 |
| --- | --- | --- |
| `let x = f()`、格納値がCopy可能 | 値のCopyではなく参照を保持する | `let x = f()@follow` |
| `f() = v`、排他Place | 参照先への代入にならない | `f()@follow = v` |
| `f()@ref` / `f()@uniq` | 要素ではなく一時参照スロットの借用になる | `f()@follow@ref` / `f()@follow@uniq` |
| `f()@copy` | 要素でなく参照のCopyを要求する。`uniq`はCopy不可 | `f()@follow@copy` |
| `f()@move` | 禁止されていた要素のTakeではなく、参照値のMoveになる | 要素を取り出す意図なら引き続き禁止。`f()@follow@move` も無効 |
| `match f()` / `for x in f()` | 特に排他参照ではSubjectモードが変わり得る | 元のPlaceをSubjectにするなら `f()@follow` |
| `_ = f()`、元のPlaceからのbare acquisitionが禁止される型 | 要素取得ではなく参照値を捨てる操作になり得る | 意図を確認する。元の要素取得は引き続き無効 |

格納された `uniq/T` のbare acquisitionは、通常の規則で再借用できる。Non-Copyという理由だけで禁止しない。固定された期待型があれば、通常のAdaptationで参照から値を読める場合もある。すべての呼出しに機械的に `@follow` を追加しない。

別途、次の二点を仕様変更として受け入れる。

- **結果だけで決まる型引数:** `get<T>() -> ref/T` の `T` は、期待する値型から参照を逆算して推論しない。必要なら `get<i32>()` と書くか、結果の参照型を直接指定する。入力から型が決まる通常の呼出しは影響しない。
- **レシーバへの依存:** Place結果に固有の「実際のレシーバLoanを常に残す」規則を削除する。参照結果が外側のレシーバ借用から独立する契約を満たすなら、その借用は結果に残らない。参照元を壊せるようにする変更ではない。

## 4. コンパイラーの設計

### 4.1. 一つの決定経路

**必要な意味:** 関数は値を返し、添字演算子が参照先を選ぶ。

**現在の表現:** 一般のPlace呼出しでは、呼出し式の型が要素型、CallPlanの結果型が参照型となり、結果モードと専用分岐を各所が持つ。

**変更:** 通常の呼出しは結果型をそのまま公開する。添字の所有コンポーネントだけが、選択済みの参照結果から要素のPlaceを決める。

**削除:** 結果モード、Place専用の返却検査・期待型推論、一般のPlace呼出しを識別する分岐。

| 所有者 | 入力 → 出力と制約 |
| --- | --- |
| Parser | 結果位置 → 通常の型。`PlaceResultKoto` と専用構文を削除する |
| Bindingの呼出し処理 | 選択済みCallPlan → その結果型。要素型へ読み替えない |
| `Binding.Indexers` / `Binding.Views` | 選択済みCallPlanの参照結果 → 添字式の要素型・能力・依存。範囲選択は値のまま |
| HIRの公開処理 | 添字の決定 → `HirPlaceKind.CallResult`。一般の関数呼出しには使わない |
| MIR構築 | 公開済みの添字計画 → 参照型の一時値とDeref。通常の `@follow` は既存のDerefを使う |
| MIRの検証・解析・生成 | 検証済みの通常の参照・Place操作を処理する。元の構文から判断し直さない |

BindingはBinding自身のCallPlanを読み、まだ公開されていないHIRを読まない。MIRは公開済みの添字計画を読み、一般呼出しの計画へのフォールバックを持たない。計画の欠落は内部契約違反として扱う。

### 4.2. 削除するものと残すもの

| 削除するもの | 主な対象 |
| --- | --- |
| `FunctionResultMode`、型・CallPlan内の結果モード | `BindingModel`、`TyCtxt`、型同一性・関係・置換、`CallPlan`、Hover |
| Place結果の構文と診断 | `PlaceResultKoto`、Parser、構文の列挙、`PlaceRequired_Kd` とその事実・カタログ |
| 専用の取得・推論・適合 | `PlaceExpected`、Place返却検査、結果モードによる照合、関連型推論のPlace除外 |
| 専用の呼出し処理 | `IsPlaceCall` / `PlaceCallReference`、呼出しの二重の型、MIRの結果一時値分岐 |
| Place公開用のAdaptation | `ExpectedAdaptationKind.ExclusiveBorrow` とその生成・消費箇所。他用途の `HirAdaptation.ExclusiveBorrow` は残す |

`IsUserIndex`、添字の選択計画、通常の参照・Loan・Place操作は残す。辞書などのアルゴリズムはKimigayoのまま維持し、新しい組込み関数・結果モード・互換モードは作らない。

### 4.3. 比較した設計と導入時期

| 案 | 判断 |
| --- | --- |
| Place結果を維持し、既存所有者内でFollowと処理を共通化 | 実装重複は減らせるが、結果モード・Callable・推論の規則は残る。今回は採らない |
| 参照結果へ統一し、添字側が参照先を選ぶ | 採用。通常の関数に新しい規則を足さず、既存の添字の責任内で決定できる |
| Place結果を添字だけに残す／互換構文として残す | 特例を維持するため採らない |
| 全参照呼出しを暗黙にFollowする | `let r = f()` で参照を保持できなくなるため採らない |

独立に作成した二案を比較し、結果形式の統一を選んだ。導入時期は、旧提案の先行実施から、**R6（旧解析・生成経路を削除する移行段階）完了後を既定**へ変更する。

現行の[D5](../../docs/dev/COMPILER_ARCHITECTURE.md#rules-during-the-migration)は旧Analysis・Emissionへの機能追加を凍結している。R6後なら、旧経路向けの一時的な型の読み替えや凍結例外が不要になる。廃止方針への支持を、凍結・語彙・予算の例外承認とは解釈しない。前倒しが必要になった場合だけ、時期と旧経路への影響を別途判断する。

## 5. 実装計画

### 5.1. 作業単位

実装開始時にR6の完了と最新の所有者・表現を確認し、基準コミットを記録する。旧経路の削除で既に不要になった準備作業は、理由を記録して省く。各単位は独立レビューと対応する検証が済んでから確定する。

| 単位 | 作業と境界 | 完了条件 |
| --- | --- | --- |
| U0 基準と等価形 | Place利用箇所を再検索し、通常の参照＋`@follow`で代替できる操作を既存のテスト所有者で確認する。診断・意味・生成IR・性能の基準を保存する | §5.2の代表形と不正な対例を確認。未対応形を特定し、原因と担当を記録する |
| U1 添字の型取得を整理 | `Binding.Indexers`、`Binding.Views`、添字の関連判定を、合成呼出しの表面型ではなくCallPlanの参照結果から要素型を得る形へ統一する。まだPlace結果は廃止しない | 診断・意味スナップショットと生成IRが同じ入力で一致。範囲選択の型は不変 |
| U2 一般アクセサーの利用を移行 | 独立に移せる一般関数・利用例・ネイティブfixtureを参照結果＋`@follow`へ変更する。添字ContractとPlace構文の受理・拒否テストは残す | 同じ操作・出力・評価順序・破棄・割当てを維持。型が変わる箇所は§3に照らして確認 |
| U3 結果形式を切り替える | §4.2を削除し、Indexable要件・標準ライブラリー・添字の公開・正式仕様・残るテストとマイルストーンを一体で更新する | §2・§3の受理と拒否が一致。参照結果のContract適合、関連型推論、直接呼出し、旧構文の拒否を確認 |
| U4 エディターの構文を追従 | TextMate文法からPlace結果の扱いを削除し、通常の名前 `place` と無効な旧構文のテストを更新する | 拡張機能の規定の検証を通過。未依頼の配布・インストールは行わない |
| U5 全体の確認 | 残存検索、性能測定、最終レビュー、Session、取り込み・支援範囲の記録を確認する | §5.3を満たし、未検証事項と残る制限を明示する |

U1は挙動不変の構造整理、U2は等価なソースへの移行、U3は言語仕様の変更として、別々に記録する。U3のContract・コンパイラー・ライブラリーを不整合なまま分割公開しない。準備をU0–U2へ移すことでU3を小さくし、分割のための二重実装や機能フラグは設けない。

U0で通常の参照操作に不備が見つかった場合は、共通の所有者で修正する別単位を先に完了する。型・利用形ごとの例外で回避しない。仕様上有効だが未対応なら `Unsupported_Kd` と支援範囲を記録し、対応済みとして次へ進めない。

### 5.2. 検証する規則

全組合せは作らず、各規則を観測できる最下層で、代表形と独立した不正例を選ぶ。

| 規則 | 正常例 | 不正例・対照例 |
| --- | --- | --- |
| 添字の能力 | Copy読み出し、更新、複合代入、借用、格納された `uniq/T` の再借用 | 共有経路の更新、`let` 所有レシーバの排他取得、参照経由のTake |
| 関数と合成 | 直接呼出し、関数値、Callable、ジェネリック転送、投影、呼出し可能な要素 | 参照結果への直接代入、互換でない結果型 |
| Loanと寿命 | `during self` の保持、外側レシーバから独立する共有ビュー、要素内部の参照 | 生きた参照の元を更新・破棄、親より長い排他再借用 |
| 推論 | 入力からの型推論、明示型引数、参照結果からの `Element` 推論 | 値型からの結果専用型引数の逆算、異なる要素型の証拠 |
| 取得とSubject | §3のCopy・Move・借用・破棄、`match`・`for` のモード | 古い綴りのまま意味が変わる例を見落とさない |
| 添字固有の契約 | キーの一回評価、固定長配列の直接射影と静的Move Path | 複数適合の曖昧さ、借用経由のMove |
| 実行と表現 | 非Copy・ゼロサイズ・入れ子の参照、評価順序、破棄一回 | 余分なCopy・Move・割当て・一時スロット依存がない |
| 構文と公開情報 | 通常の名前 `place`、参照結果のHover・意味情報 | `-> place ref/T` と `-> place uniq/T` はLanguageエラー |

既存の主な所有者は `IndexableContractTest`、`DictionaryIndexableTest`、`DictionaryStoredReferenceTest`、`UniqSliceTest`、`DereferenceUpdateTest`、`PairFollowTest`、`PlaceCalleeTest`、`PublishedPlaceProjectionTest`、関連型・Origin・Callableのテストである。

`PlaceResultTest`、`PlaceResultParseTest`、`CallablePlaceResultTest` は、独自の検証を通常の参照・添字・構文の所有者へ移した後に整理する。重複ケースを残さず、割当て・評価順序・寿命の検証を失わせない。

### 5.3. 検証・測定・確定

[VERIFICATION](../../docs/dev/VERIFICATION.md)に従い、変更単位ごとに対象のクラス・メソッド・native fixtureを明示する。コンパイラー変更では `ArchitectureRulesTest`、ライブラリー変更では `KimiLibraryShapeTest` を含める。作業終了時はSessionを実行する。

```powershell
# U3の代表的な選択例。実際の変更範囲に応じ、§5.2の検査を加える。
./scripts/verify.ps1 -Class IndexableContractTest,DictionaryIndexableTest,DictionaryStoredReferenceTest,UniqSliceTest,KimiLibraryShapeTest -Fixtures 'Indexable*.ll','Dictionary*.ll','UniqSlice*.ll' -Milestone 27,28
```

- **U1:** 変更前後で同じ入力を使い、診断、意味情報、HIR/MIR、生成IRを比較する。
- **U2・U3:** 書き換えたソースの期待差を列挙する。出力だけでなく、取得対象・Loan・評価順序を検証する。仕様変更に伴う期待値の変更と、回帰を区別する。
- **生成コード:** 変更したライブラリー本体と添字・通常呼出しのO2 IRを比較する。`DictionaryCostTest` と `UniqSliceTest` の割当て保証を維持する。`ExclusiveViews.kimi` は同じ条件で前後7回を比較し、失敗も保存する。
- **コンパイラー:** 対象処理の初回・再利用時の時間、割当て、保持メモリーと領域別の正味トークン数を測る。再利用時0 Bの回帰検査は緩めない。計測値の種類を混同しない。
- **最終Session:** 必要なnative fixtureとMilestone 27・28を明示して選ぶ。Sessionという名前だけで未選択の検証を済んだ扱いにしない。NativeAOTは対象外。

証跡は `artifacts/verify/` と `artifacts/benchmarks/` に保存する。実装単位は検証成功後にコミットし、`verify-commit.ps1` で同じ入力の証跡を関連付けてpushする。最後のSessionが同一入力の必要なUnit検査をすべて含む場合は重複実行しない。

旧提案の削減見積りは約1,700–1,900非自明C#トークンで、未測定である。R6後は対象コードが減るため、その値を予算や確約に使わない。Parsing・Bindingの削減、Root・MIRの専用処理の削減を見込むが、領域ごとの正味量は実測する。ABIが参照と同じため生成性能は同等と予想するものの、高速化や割当て削減は主張しない。

## 6. 取り込み先と調査対象

### 6.1. 仕様・文書

| 対象 | 更新内容 |
| --- | --- |
| SPEC §7.1、§7.1.1、付録F | Place結果を削除し、結果を通常の型に統一。構文と文脈キーワードを整理 |
| SPEC §4.7.1–2、§13.5.5–6 | 参照を返す添字Contractと、添字式による参照先の選択 |
| SPEC §6.4.1、§8.4–6・§8.10、§9.1、§10.2–3・§10.7–8 | 宣言・適合・Callable・関連型推論から結果モードの規則を削除 |
| SPEC §15.1.5・§15.6、§22.1、付録E | 通常の参照・Loanへの接続、必須宣言、用語 |
| SPEC索引、SPECJ・SPECJ2、LIBRARY、STYLE、GUIDE | 宣言・説明・例を同じ仕様へ揃える |
| DECISIONS、COMPILER_ARCHITECTURE、CODEMAP | 採用した表現・所有者・削除を記録。上限は引き上げない |
| SETTLED、DIAGNOSTICS、VERIFICATION、STATUS、PLAN | 不採用案、消える診断、検証対象、実証した支援範囲と残る制限 |
| `draft/INTEGRATED.md` | 正式取り込み時に本書の節と反映先を記録。現時点では未取り込み |

凍結された過去の提案やレビュー本文は変更しない。正式仕様への取り込みと、実装・検証の完了は別に記録する。

### 6.2. 実装・テスト

- **構文・型:** `Parsing/Koto/Types/PlaceResultKoto.cs`、Parser、`BindingModel`、`TyCtxt`、型同一性・関係・置換、Hover。
- **束縛:** `Binding.Calls`、`ValueCalls`、`FunctionTypes`、`CallableSelection`、`Indexers`、`Views`、`ArgumentOperations`、`Expressions`、`Functions`、`Closures`、`ContractMatching`、`VirtualOverrides`、`AssociatedInference`、`EffectBounds`、HIR公開。
- **MIR:** `MirBuilder.Calls`、`MirBuilder.Places`、`HirVocabulary`。R6で削除された旧Analysis・Emissionを再導入しない。
- **ライブラリー:** `Indexable.kimi`、`Dictionary.kimi`、`UniqSlice.kimi` と `indexPosition*`。要素Placeを返していた本体は借用を返し、既に排他参照値を得ている本体はその参照をMoveして返す。
- **周辺:** §5.2のテスト、`SemanticSnapshotTest`、`HirColumnsTest`、`tests/mir/calls.*`、構文診断コーパス、Milestone 27・28とREADME、拡張機能の文法とテスト。

実装開始時と終了時に、現行の `src/`・`tests/`・`docs/` で `FunctionResultMode`、`ResultMode`、`PlaceResult`、`PlaceExpected`、`IsPlaceCall`、`PlaceCallReference`、`PlaceRequired`、`place ref/`、`place uniq/` を検索する。構文拒否テストや履歴など、残す一致は用途を確認する。明示的な `.index(...)`・`.indexUniq(...)` 呼出しも別に調べ、参照層の取り違えを防ぐ。

## 7. 未検証事項

実行検証が必要なのは、§5.2の等価形の全経路、レシーバから独立する結果の受理範囲、関連型推論、診断の正確な位置、生成IR・O2性能・割当て、実際の削減量である。現行コードから確認できた表現の共通性を、これらの検証済みという意味には使わない。
