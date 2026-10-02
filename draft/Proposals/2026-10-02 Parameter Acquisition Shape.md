# 仕様変更案：引数位置ごとの取得モードの統一

日付：2026-10-02

状態：提案。採否は未決定。正式仕様への取り込みと実装は未実施。

本書で変更する事項は SPEC より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例は独立した断片であり、「エラー」と記した行は意図した拒否例である。

## 1. 問題と方針

### 1.1. 現仕様の問題

SPEC §7.3 は、同名の関数が receiver の取得方法（receiver shape）を揃えることを要求し、「名前だけで receiver の取得が決まる」ようにしている。一方、通常の引数では、外側の Semantics だけが違う同名宣言（`T`／`ref/T`／`uniq/T` など）が共存でき、呼出しごとに候補の調停が必要になる。これには次の問題がある。

**(a) 更新が黙って失われる。** 排他借用版は、`@uniq` のない裸の Place には適用できないので候補から外れる。Copy 型なら値渡し版が選ばれ、戻り値は捨てられる。呼出しは「効果のない値の破棄」警告の対象外（SPEC §17.4.2）なので、何も報告されない。

```kimi
func bump(score: Score) -> Score     // 新しい値を返す
func bump(score: uniq/Score) -> ()   // その場で更新する

struct Game
    var score: Score
    func play(self: uniq/Self)
        bump(self.score@uniq)        // 更新される
        bump(self.score)             // Score が Copy なら値渡し版。結果は捨てられ、更新が消える
```

receiver については SPEC §7.3 が「Copy を変更して更新を捨てることはない」と明記して防いでいるが、引数では同じ事故が起きる。SPEC §10.2.2 の取得競合は「値取得と新規共有借用」だけを扱い、この組を扱わない。

**(b) 選択が周囲の Semantics に依存する。** 共有版と排他版の組では、既存の参照を渡したときの候補が、その参照の Semantics（多くは外側のシグネチャ）で決まる。Kimi ライブラリの `Storage.borrowStorage(self)` は、`self` が `ref/Self` か `uniq/Self` かで別の関数になる。呼出し式だけでは、どちらが呼ばれるか分からない。

**(c) 調停規則が重い。** SPEC §10.2.2（取得競合、Copy 証明の保留と検査順序）と §10.4 の例の多くは、この種の宣言の組を調停するためだけにある。また、STYLE §3.2 は Kimi のコードに「取得方法だけで overload しない」ことを既に求めており、言語規則と慣例が二重になっている。

**(d) 宣言の追加や import が既存の呼出しを変える。** 取得方法だけが違う宣言を追加すると、既存の呼出しが取得競合で拒否されるか、(a) の形で別の関数を選ぶ。

### 1.2. 方針

1. **型が重なる対応位置では、同じ関数グループのすべての宣言が同じ取得モード（値渡し・共有・排他）を持つ。** receiver shape の規則を通常の引数へ広げる。これに反する組は宣言エラー（複数の宣言元から集めたグループでは使用時のエラー）とする。
2. **SPEC §10.2.2 の取得競合を削除する。** 本規則を満たすグループでは、取得競合は生じない（本書 3.6）。
3. 新しい構文は追加しない。取得方法が違う操作は別名で表す（SPEC §4.7.1 の命名規約）。

## 2. 期待する動作

```kimi
func bump(score: Score) -> Score
func bump(score: uniq/Score)            // エラー：位置 0 で値渡しと排他が重なる

func bumped(score: Score) -> Score      // 修正：別名にする
func bump(score: uniq/Score)
bump(game.score)                        // エラー：唯一の候補が排他借用を要求する。@uniq を書く

func inspect(value: ref/i32)
func inspect(value: uniq/i32)           // エラー：共有と排他が重なる

func select(value: Node ! flag: bool)
func select(value: ref/Node ! flag: i32) // エラー：他の引数で区別できても、value の位置で重なる

func g<T>(value: T)
func g(value: uniq/Node)                // エラー：T は Node と重なり得る

func route(value: obj/Node)
func route(value: objref/Node)          // エラー：値渡しと共有が同じ object で重なる
```

次の組は許可する。

```kimi
func writeLine(text: ref/string)
func writeLine(text: Text.Utf8Slice)    // 型が重ならない

func f(value: Node)
func f<T>(value: T)                     // どちらも値渡し

func splitFirst<E>(state: uniq/RefRemainder<E>) -> Option<ref/E during state.source>
func splitFirst<E>(state: uniq/UniqRemainder<E>) -> Option<uniq/E during state.source>  // どちらも排他で、型が重ならない

func show(value: Node)
func show(value: objref/Node)           // 値の層と object の層は同じ引数を受け取らない
```

Kimi ライブラリの呼出しは、周囲の Semantics ではなく名前で取得方法を示す。

```kimi
// 変更前：self が uniq/Self なので、順位付けで排他版が選ばれる
public func iterateUniq(self: uniq/Self during source) -> ArrayUniqIterator<T> during source
    return ArrayUniqIterator<T>.init(Storage.borrowStorage(self))
// 変更後
    return ArrayUniqIterator<T>.init(Storage.borrowStorageUniq(self))
```

## 3. 仕様変更

### 3.1. パラメーターの取得モード

通常のパラメーター（receiver を除く）の正規化した型（別名の展開、括弧と冗長な `owner` の除去後）について、外側の Semantics から**パラメーターの取得モード**を決める。用語は SPEC §15.1.6 の Subject mode に合わせる。

| 取得モード | 外側の Semantics |
| --- | --- |
| ByValue | `owner`（Core、Tuple、固定長配列、Function Type、通常の型パラメーター `T`、関連型を含む）、`obj`、`rc`、`arc`、`raw` |
| Shared | `ref`、`objref` |
| Exclusive | `uniq`、`objuniq` |

ペアのパラメーター `s/T`（または `s/U`）は、`s` の許容集合（SPEC §8.7）の Semantics ごとに、その Semantics で書いたものとして検査する。

### 3.2. 対応するパラメーター

二つの宣言のパラメーターは、次のいずれかのとき**対応する**。

- 両方が位置指定で供給できる（SPEC §7.2.2）、同じ位置番号のパラメーターである。
- 同じ外部名を持つ。

既定値の有無、引数の個数、`!` 境界の位置、ラベル、Constraints、`when` 条件は対応を変えない。

### 3.3. 型の重なり

対応するパラメーターの型から、次の**照合キー**を作る。

| パラメーターの型 | 照合キー |
| --- | --- |
| `owner/U`（＝`U`）、`ref/U`、`uniq/U` | `Value(U)` |
| `obj/U`、`rc/U`、`arc/U`、`objref/U`、`objuniq/U` | `Object(U)` |
| 単独の型パラメーター `T`、正規化できない関連型の射影 | 任意（すべてのキーと重なる） |
| その他（`raw/U`、Core、Tuple、固定長配列、Function Type） | `Value(その型自身)` |

二つのキーは、両宣言の固有の generic スロット（型・ペアの target・長さ）を互いに独立な変数とみなし、何らかの代入で同じ正規化型になるとき**重なる**。外側の Type から受け継いだスロットは固定とし、変数にしない。Origin と binding set は無視し、正規化できない関連型の射影は任意の型と重なるものとする。Constraints は使わない（本書 3.7）。

### 3.4. 規則

**同じ関数グループの二つの宣言が、取得モードの異なる対応パラメーターを持ち、その照合キーが重なるとき、その組は不正である。**

| グループの形成 | 判定 |
| --- | --- |
| 一つの宣言スコープの同名宣言（Container、その fragment、プロジェクトのルート、局所スコープ、Type のメンバー、コンストラクター、Contract の要求） | 宣言エラー |
| 使用時に複数の宣言元から集めたグループ（SPEC §9.4 の別名の段階、SPEC §9.5 の Constraints から集めた要求） | その使用のエラー。引数にかかわらない |

- receiver shape（SPEC §7.3）と同様に、引数の個数・ラベル・既定値・互いに排他的な `when` 条件は例外にならない。
- Contract の refinement で合わさる要求は、SPEC §8.4.2 の既存の共存判定に本規則を含めて検査する。
- explicit full specialization は元の宣言の契約を継承するので、新たな組を作らない。
- 演算子・添字・比較の候補は、既存の組込みと公開 Contract（`index`／`indexUniq` など別名の組）が供給するもので、本規則の影響を受けない。

### 3.5. 適用の効果

- 型が重なる対応位置では、引数の取得モードが関数名と位置だけで決まる。どの候補が選ばれても、その引数の取得方法（値渡し・共有借用・排他借用）は変わらない。
- 本書 1.1 (a) の組は宣言できないので、`@uniq` の書き忘れは「唯一の候補が排他借用を要求する」エラーになり、別の関数へは逃げない。
- 本書 1.1 (b) のように、既存の参照の Semantics で共有版と排他版が切り替わることはない。

### 3.6. 取得競合（SPEC §10.2.2）の削除

取得競合は、同じ裸の Place 引数について、ある候補が値取得（格納された完全型 `W` の Copy）を、別の候補が新規共有借用（`ref/W`）を計画する状況である。前者の対応パラメーターは `W`（または `W` と重なる型パラメーター）で ByValue、後者は `ref/W` で Shared であり、照合キーはどちらも `Value(W)` と重なる。したがって本規則を満たすグループでは取得競合は生じない。参照やハンドルのスロットは暗黙に借用しないので、`W` がそれらの場合も競合しない。

これにより次を削除・変更する。

- SPEC §10.2.2 の全体（取得競合の定義、検査の順序、Copy 条件を順位付けの前に置く理由、ジェネリックの扱い）。
- SPEC §10.1 の Copy 証明の保留。裸の値取得に必要な Copy の証明は、他の条件と同じく適用可能性の検査（SPEC §10.1 の手順5）に戻す。同じ型が重なる対応位置で値渡し版と借用版が共存しないので、Copy 能力の有無で選択が入れ替わることはない。
- SPEC §10.2.1 の順位クラス（Exact、Literal fitting、Same-Semantics Reborrow、Cross-Semantics adaptation）は残す。参照の層が複数ある入力（例：`uniq/(uniq/Node)` に対する `uniq/(uniq/Node)` と `ref/Node`）では、キーが重ならない候補の間で引き続き必要になる。

### 3.7. ジェネリック

- 重なりは照合キーの構文的な単一化だけで判定し、Constraints による排他性は使わない。不成立の証明（Refuted）を要するため、SPEC §8.7 の限定的な証明体系では Unknown が多く、判定が予測しにくくなる。この結果、Constraints で実際には重ならない組（例：`f<T>(value: T)` に `T is PrimitiveInteger` を付けたものと `f(value: ref/Node)`）も拒否する。これは意図した保守性である。
- 判定は宣言時に一度だけ行い、実体化時には行わない。ジェネリック本体の呼出しも、本規則を満たすグループに対しては取得競合を生じない。

## 4. 診断と回復

### 4.1. 診断

Language Error `ParameterShapeMismatch_Kd` を新設する（`ReceiverShapeMismatch_Kd` と対になる）。

- **単位：** 宣言エラーは、ソース順で後の宣言の対応パラメーターごとに一件とする。同じパラメーターについて複数の先行宣言と重なる場合も一件にまとめる。
- **位置：** 主範囲は後の宣言のパラメーターの型。関連位置は、重なる先行宣言の対応パラメーター。
- **Reason：** 関数名、対応の根拠（位置番号または外部名）、双方の取得モード、重なる型（表示上限つき）。
- **Advice：** SPEC §4.7.1 の命名規約に沿った別名（`sorted`／`sort`、排他版の `Uniq` 接尾辞など）、または取得モードを揃える型の変更。改名は呼出し側に及ぶので、機械適用できる修正としては提示しない。
- **使用時のエラー：** 主範囲は呼出しの関数名、関連位置は重なる宣言。Advice は、別名の段階で集めたグループなら Container での修飾（`A.f`）を示す。Constraints から集めた要求のグループでは、SPEC §9.5 の receiver の場合と同じく選択の構文はない。

### 4.2. 回復

- 宣言エラーの宣言も関数グループに残し、それを選べる呼出しは通常どおり検査する。独立したエラーは隠さない。
- 宣言エラーの組のために選択できない呼出し（曖昧性、候補なし）は、その宣言エラーを前提とする派生問題として扱い（SPEC §23.3.6.4）、独立した診断を重ねない。
- 削除する `AcquisitionRequired_Kd` は、報告する検査がなくなるのでカタログから除く。

## 5. 既存の提案との関係

### 5.1. Explicit Acquisition in Overload Resolution（2026-10-01、取り込み済み）

[同提案](../Changes/2026-10-01%20Explicit%20Acquisition%20in%20Overload%20Resolution.md) は SPEC §10.2.2 を導入し、その 8.1 で「値渡しと借用の同名宣言を禁止する」案を次の理由で不採用とした。本書はその判断を見直す。

| 同提案の不採用理由 | 本書の回答 |
| --- | --- |
| 他の引数で区別できる組まで失う | 意図した負担として受け入れる。receiver shape（SPEC §7.3）も、引数・ラベル・`when` 条件が違う組を例外にしていない。また SPEC §10.2.2 は本書 1.1 (a)(b) を扱わない。これを呼出し側で扱うには「`@uniq` を書けば適用可能になる候補」という仮定の適用可能性判定が要り、調停規則がさらに重くなる |
| ジェネリック代入後の重なりの判定が単純でない | 外側の層を除いた照合キーの構文的な単一化に限定し、Constraints と Origin を使わない（本書 3.3、3.7）。宣言の組ごとの有限の判定で、実体化を要しない。その代わり、Constraints で重ならない組も保守的に拒否する |

同提案の本文は固定のまま変更しない。本書を取り込む場合は、同提案の 3.1–3.5 と 4 から正式仕様に入った内容を本書で置き換えることを、`draft/INTEGRATED.md` に追記する。

### 5.2. Implicit Exclusive Receiver（2026-09-23、取り込み済み）

receiver shape の規則（SPEC §7.3）を導入した提案である。本書は同じ原則を通常の引数へ広げるもので、receiver の規則は変更しない。

## 6. 既存コードへの影響

テキスト走査による調査（2026-10-02）。コンパイラーによる判定ではないので、実装時に新しい検査で全件を確認する。

| 対象 | 該当 | 対応 |
| --- | --- | --- |
| Kimi ライブラリ | `Kimi.Storage.borrowStorage` の共有版・排他版の 3 組（[StorageOperations.kimi](../../src/Kimi/Library/StorageOperations.kimi)：Array、Dictionary、固定長配列）。`internal` でユーザーからは呼べない | 排他版を `borrowStorageUniq` へ改名する。排他版の呼出し：`Array.kimi`（`iterateUniq`、`tryGetPairUniq`）、`Dictionary.kimi`（`indexUniq`、`iterateUniq`）、`Storage.kimi`（固定長配列の `iterateUniq`、`tryGetPairUniq`）。共有版の呼出しは変更しない |
| コンパイラー | `KimiLibraryCatalog.cs`（`borrowStorage` の 6 項目の名前と Overload 番号）、`KimiLibraryValidation.cs`、`KimiLibraryStorage.cs` | 排他版の名前の変更に追随する |
| 仕様の例 | SPEC §10.2.2 の全例、§10.4 の `inspect(ref/i32)`／`inspect(uniq/i32)` と `bump`、§10.6 の `unsafe func inspect(value: i32)`／`inspect(value: ref/i32)`、§12.3.1 の `take(range: Range<i32, i32>)`／`take(range: ref/Range<i32, i32>)` | 削除するか、取得モードだけが違う組を使わない例へ書き換える（§10.6 は型の異なる組で unsafe の検査順を示す） |
| テスト | `AcquisitionConflictTest` | 本規則の宣言エラーの検査へ置き換える。その他の該当は実装時の検査で特定する |
| マイルストーン、`docs/examples` | なし | — |

影響はすべて宣言時（または使用時）のコンパイルエラーとして現れ、これまで有効だった呼出しの挙動が黙って変わることはない。pre-alpha のため互換性の手当ては要求しない。

## 7. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

### 7.1. 実装単位

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U1：宣言時の検査 | `Binding.cs` の `ValidateSignatures`（receiver shape の検査と同じ箇所）に、取得モード・照合キー・重なりの判定を加える。単一化は既存の正規化型の表現を使う | 本書 2 の拒否例と許可例、ジェネリック・ペア・名前付き・`!` 境界・コンストラクター・Contract・refinement の対照例が通る |
| U2：使用時の検査 | 別名の段階で集めたグループと、`Binding.ContractCalls.cs` の Constraints から集めた要求のグループに、使用時の判定を加える | 修飾した呼出しが通り、集めたグループの使用だけが拒否される |
| U3：ライブラリの改名 | `borrowStorageUniq` への改名と、カタログ・検証・呼出しの追随 | 既存のライブラリ・マイルストーン・native O0/O2 の結果が変わらない |
| U4：取得競合の削除 | `Binding.CandidateEvaluation` の `CheckAcquisitionConflicts`・`HeldCopyProven` と関連経路を除き、Copy の証明を適用可能性の検査へ戻す。`AcquisitionRequired_Kd` をカタログから除く | 単一候補・複数候補の既存の合法性と推論が、本規則で拒否される組を除いて変わらない |
| U5：診断と統合 | `ParameterShapeMismatch_Kd` の事実・位置・関連位置・前提関係と、CLI・LSP の出力 | DIAGNOSTICS §10 の手順を満たし、Session 検証が通る |

### 7.2. 性能方針

- 宣言時の判定は同名グループごとに宣言の組 × 対応パラメーターで行う。同名グループは小さいので、二乗の走査でよい。照合キーは宣言ごとに一度だけ作り、作業領域は再利用する。
- 呼出しごとの取得競合の走査と Copy 証明の保留がなくなる分、overload 解決の作業は減る見込みだが、測定前に高速化を主張しない。関連する割当て・再利用の回帰を含めて確認する。

### 7.3. 検証

単位の完了時は `scripts/verify.ps1 -Class ... -Fixtures ...` で正式に検証し、最後に一度 `-Mode Session` を実行する。証拠は `artifacts/verify/`、測定結果は `artifacts/benchmarks/` に置く。NativeAOT は実行しない。

## 8. 文書更新計画

正式な取り込みでは英語で更新し、仕様に draft への依存を残さない。

| 文書 | 更新内容 |
| --- | --- |
| SPEC §7.2 または §7.3 | 本規則（取得モード、対応、照合キー、重なり、判定の時点）を receiver shape と並べて新設する |
| SPEC §9.1、§9.5 | Signature とは別の共存規則であることと、集めたグループの使用時の判定 |
| SPEC §10.1、§10.2、§10.2.1、§10.4–§10.6 | Copy 証明の保留と §10.2.2 を削除し、関連する記述・例・診断の段落を整理する。§10.2.2 以降の番号を詰める |
| SPEC §3.5、§8.9、§13.5.3、§12.3.1 | 取得競合への参照を除き、`@copy` の説明と例を整える |
| SPEC §4.7.1 | 取得方法だけが違う操作に別名が必要なのは、receiver だけでなく引数でも言語の要求であることを書く |
| SPEC §22.1.2.5、docs/SPEC.md | `borrowStorageUniq` と索引の要約（Lending rule の取得競合の記述を含む） |
| Appendix E | 「Acquisition conflict」を本規則の用語へ置き換える |
| STYLE §3.2 | 「取得方法だけで overload しない」の `[Kimi]` 規則を、言語規則の要約（`[Language]`）にする |
| CODEMAP、DIAGNOSTICS | 検査の入口、代表テスト、新しい診断の単位と前提 |
| STATUS、PLAN、PLAN_HISTORY | 実装後に、取得競合の項目を本規則へ置き換える。STATUS は検証済みの対応境界が変わったときだけ更新する |
| draft/INTEGRATED.md | 本書の取り込み範囲と、本書 5.1 の置き換えを同じコミットで記録する |

## 9. 設計判断

### 9.1. 採用しなかった案

| 案 | 不採用の理由 |
| --- | --- |
| 取得競合を排他借用にも広げる（呼出し側の調停を強化する） | 仮定の適用可能性判定が要り、SPEC §10.2.2 をさらに複雑にする。名前と位置だけで取得方法が決まる性質も得られない |
| 外側の Semantics 以外が完全に一致する組だけを禁止する | `f<T>(value: T)` と `f(value: uniq/Node)` のような重なりを見逃し、取得競合の規則を残す必要がある |
| 呼出しの形が重ならない組（引数の個数や他の引数の型で区別できる組）を許可する | 判定が呼出し形の全組合せに依存し、名前と位置だけで取得方法が決まる性質を失う。receiver shape とも揃わない |
| Constraints で重なりを判定する | 本書 3.7 のとおり、不成立の証明に依存して予測しにくい |
| 警告にとどめる | 組が残る限り、呼出し側の調停規則（SPEC §10.2.2）も残る |

### 9.2. Kimigayo Principles との対応

- **原則1：** 一つの名前と位置には一つの取得方法を対応させ、取得方法が違う操作は別名で表す。
- **原則2：** 引数の取得方法を、overload の順位や周囲のシグネチャの Semantics ではなく、名前と位置から決める。overload 解決の規則も小さくなる。
- **原則3：** 値渡し・共有・排他の区別が、常に宣言の名前とシグネチャに現れる。
- **原則4：** 問題を呼出しごとではなく宣言の組で一度だけ報告し、両方の宣言を関連位置として示す。

### 9.3. 残る確認

本書は仕様・ソース・既存テストとの静的な照合による。新しい規則の実装・実行試験・性能測定は未実施である。残る確認は、テスト全体での既存の組の数、関連型の射影と長さスロットを含む単一化の細部、別名の段階で集めたグループの使用時エラーの頻度である。
