# Result の conditional Copy と virtual の仕様範囲

2026-09-12。レビュー反映済みの仕様変更提案。SPEC.md への採用とコンパイラ実装は別途行う。

## 1. 評価と推奨

| 提案 | 判断 | 得られる改善 | 代償・限界 |
| --- | --- | --- | --- |
| Result に conditional Copy を追加する | 採用を推奨 | Option と同じ導出規則になり、Copy な成功値・失敗値を通常の値として再利用できる。新構文や例外規則は不要 | 既存の Copy 判定、取得計画、制約に依存するコードの意味が変わる |
| virtual/override を将来拡張へ隔離する | A 案を推奨 | 現行の宣言構文と意味論が対応する | 動的な実装差替えは延期される |
| accessible な基底メンバーと同名の宣言を禁止する | 採用を推奨 | hiding による意図しない呼出し先の変更を防ぐ | 異なる Signature の派生 overload 追加も禁止する。既存の hiding は移行が必要 |
| ObjectCompatible の検証情報を公開する | 総称型の検証期限を保って採用 | 制限付き receiver での利用可否が公開 API の一部になる | Compatible の撤回は API 変更。自動判定だけで本体変更の互換性を保証するものではない |

標準 enum に通常の能力導出を適用し、現行の呼出し規則を現行の宣言構文だけで完結させる。Result の変更は再利用性を増す。virtual の延期は表現力の追加ではないが、runtime Contract Views も §8.5 で拡張扱いの現状に合う。

## 2. Result の conditional Copy

### 2.1. 現状の確認

[§3.5.2](../../SPEC.md#352-enum-copy) は enum の明示的 opt-in と全 Case の payload 検査を要求する。[§17.2.2](../../SPEC.md#1722-result) と [§22.1](../../SPEC.md#221-required-core-declarations) の Core.Result には opt-in がない。したがって `Result<i32, i32>` が Non-Copy になるという指摘は正しい。

実装もこの規則に一致している。[CoreIntrinsics.cs](../../Kimi/Compiler/Binding/CoreIntrinsics.cs) の生成ソースは Result に Copy 宣言を付けず、`ValidEnum` もその形を検証する。[EnumBindingTest.cs](../../xUnitTest/Tests/EnumBindingTest.cs) はこの分類を、[EnumOwnershipTest.cs](../../xUnitTest/Tests/EnumOwnershipTest.cs) は取得後の再利用失敗を確認している。単なる表示上の欠落としては扱わない。

### 2.2. 採用する宣言

```kimi
enum Result<T, E>
    Self is Copy when T is Copy, E is Copy
    Ok(T)
    Err(E)
```

`when` のコンマ区切りは既存の §8.4.8.1 により論理積を意味する。新しい制約構文は追加しない。

規範規則を次のようにする。

> 有効な owned Core.Result<T, E> の Copy conformance は、完全 Type としての T と E の双方について Copy が証明される場合に成立する。これは通常の enum conditional Copy の導出であり、Result 固有の取得規則ではない。条件は Type 形成の制約ではない。

これにより、いずれかが Non-Copy でも Result 自体を形成できる。両方の Copy が証明できないことと、Non-Copy が確定することは区別する。未解決の総称引数は既存の条件付き取得計画と証明義務に従い、Unknown を Non-Copy と見なさない。

Copy 判定は全 Case の payload に基づく。実行時に `.Ok` と分かっていても E の条件を省略せず、`.Err` と分かっていても T の条件を省略しない。Case 判定で enum 全体の Copy 能力は変わらない。payload 単独の取得は既存の match 規則に従う。

### 2.3. Copy の証明と完全 Type

次の例は、それぞれの Type と Origin が通常の規則で有効であることを前提とする。Resource は Non-Copy の所有値型とする。

| 完全 Type | `Type is Copy` の判定 | 理由 |
| --- | --- | --- |
| `Result<i32, i32>` / `Result<(), i32>` | Proven | 両 payload が Copy |
| `Result<Option<i32>, i32>` | Proven | 内側の conditional Copy が成立 |
| `Result<string, i32>` | Refuted | 所有する string が Non-Copy |
| `Result<i32, Resource>` | Refuted | Err の payload が Non-Copy。値が Ok でも同じ |
| `Result<ref/Resource, i32>` / `Result<objref/Resource, i32>` | Proven | shared borrow は Copy。Resource 自体の Copy は不要 |
| `Result<uniq/i32, i32>` / `Result<objuniq/i32, i32>` | Refuted | exclusive borrow が Non-Copy |
| `Result<obj/Resource, i32>` | Refuted | owning object handle が Non-Copy |
| `Result<rc/Resource, i32>` / `Result<arc/Resource, i32>` | Refuted | カウント所有者の複製は明示操作 |

| 総称型での前提 | `Result<T, E> is Copy` の判定 |
| --- | --- |
| T と E の Copy がともに Proven | Proven |
| 少なくとも一方が Refuted、他方は有効な証明対象 | Refuted |
| Refuted はなく、少なくとも一方が Unknown | Unknown。既存の証明期限と条件付き取得計画に従う |

Proven は Copy、Refuted は Non-Copy の証明を表す。無効な Type・宣言・矛盾した証拠は §8.7 の Error であり、上表の分類に含めない。

shared borrow を含む値を Copy しても Origin・Loan・storage anchor は保持する。Copy は所有権、寿命独立性、排他的アクセスを保証しない。また `obj/Result<i32, i32>` など外側の Semantics が異なる Type は、その Semantics 自身の取得規則に従う。

§17.2.2 の FileError 例には `Self is Copy` を加える。これにより `Result<i32, FileError>` は Copy になるが、`Result<Data, FileError>` は Data も Copy の場合に限る。payload-free enum の自動 Copy は追加しない。

### 2.4. 取得と失敗処理

```kimi
let first: Result<i32, i32> = .Ok(42)
let second = first  // Copy。first は Initialized のまま。
let third = first   // 再利用できる。

let resourceResult: Result<i32, string> = .Ok(42)
let transferred = resourceResult  // enum 全体を Move。
// let again = resourceResult     // 再初期化なしの再利用はエラー。
```

Copy は既存の enum Copy と同じく、active Case と payload の値をユーザーコードなしに複製する。割当て、カウント増加、独自 clone 処理を導入しない。

§17.2.3 の discarded-Result warning は Copy 分類と独立させる。Copy な Result でも Discard Context の警告対象であり、Symbol Identity による判定と明示的な match による処理を維持する。Non-Copy は「結果を必ず一度処理する」という保証ではなく、その保証の代用として Result を Non-Copy に固定する設計上の利点は小さい。

§17.4 では、同一の破棄箇所・破棄される値に Symbol Identity 固有の警告が該当する場合、その一件を優先し、同じ破棄に由来する汎用の effect-free / unintended-Unit 警告を重ねない。別の破棄箇所や無関係な診断は抑制しない。現行の対象は Core.Result であり、今後固有警告を増やす場合は同士の競合順位も定義する。

### 2.5. Core の互換性と実装への反映

§22.1 の Copy 条件を以下の原子命題集合で指定する。条件は既存の conditional conformance 文法に従い、§8.7 の conjunction elimination と proposition identity により比較する。順序、透明な括弧、同一原子の重複は集合を変えない。任意の論理同値変換は行わない。

| Core enum | `Self is Copy` の条件集合 |
| --- | --- |
| `Option<T>` | `{ T is Core.Copy }` |
| `Result<T, E>` | `{ T is Core.Copy, E is Core.Copy }` |

T/E は対応する型引数スロット、Core.Copy は認識済み Symbol Identity を表す。`E is Copy, T is Copy` も適合する。Copy 宣言の欠落、無条件 Copy、原子の不足・追加・別 Identity は不適合。他の required shape 条件も引き続き検査する。

合成 Core とロード済み Core に同じ規則を適用する。生成ソースの標準形を T、E 順に固定し、合成 Core の自己検査だけをその構文順で行うことは実装上の選択である。ロード済み Core の適合性をこの固定順照合で判定してはならない。

同名のユーザー enum は通常の明示的 opt-in に従う。名前が Result だから自動的に Copy になる仕組みは追加しない。

採用時には、Core の契約とそれを参照する能力判定・取得計画・生成物を更新する。旧 Core を新仕様と同一契約として受理せず、必要な再検査・再生成を行う。Case 順序と payload 構造は維持するが、それだけを理由に旧生成物とのバイナリ互換性を約束しない。

### 2.6. 共有読取りの結果 Type と移行

§4.6.6 の SharedReadResult と §15.1.6 の ref 経由の構造 Pattern 読取りは共通規則を維持する。owned Result が Copy になると、共有読取りの結果は storage borrow から Result の値へ変わる。型推論、引数適合、Origin と総称関数本体を再検証する。

| 読取り | 変更前 | 変更後 |
| --- | --- | --- |
| `s: Slice<Result<i32, i32>>` の `s[0]` | `ref/Result<i32, i32>` | `Result<i32, i32>` |
| `s[0]@ref` | 要素 Place の shared borrow | 同じ。Copy 判定から独立 |
| `s.tryGet(0)` | `Option<ref/Result<i32, i32>>` | 同じ。既定の固定参照結果 |
| `match wrapper@ref` の `.Some(let r)`、wrapper は `Option<Result<i32, i32>>` | r は `ref/Result<i32, i32>` | r は `Result<i32, i32>` |

```kimi
func head<T, E> origin source(s: Slice<Result<T, E>> from source) -> ref/Result<T, E> from source
    return s[0]@ref
```

要素読取りの移行には Place への `@ref` を使う。従来の `return s[0]` は、変更後には全 binding で戻り値契約を満たさないため定義時エラーとなる。Copy された一時値を借りても source への参照にはならない。

この書換えを Pattern binding に機械的には適用できない。`match wrapper@ref` でも子の読取りは SharedReadResult に従う。参照固定の Pattern や通常の payload projection 構文は追加せず、必要なら enum 全体の借用を受ける API に変更する。

## 3. virtual/override を将来拡張へ分離する

### 3.1. 仕様の配置

[§6.2.4](../../SPEC.md#624-virtual-members-and-overrides) を virtual/override の拡張設計の唯一の所有節とする。各現行節には現行規則だけを記載し、override 固有の意味論・図は §6.2.4 に集約する。Appendix D/F は状態と所有節への参照だけを持つ。実装の進捗は STATUS.md に記録する。

### 3.2. 静的選択と同名再宣言の禁止

§6.2.2 と §12.4.3 の現行規則を次のようにする。

> 通常の構造体関数と Property accessor の呼出しは、継承メンバーや具体 Core の Object View 経由でも、Effective Type から静的に選択した実装を使う。lookup、overload、access、specialization は既存規則に従い、Runtime Object Type による実装の差替えは行わない。`open` は構造体の派生を許可する。

§6.2.2 に次の宣言規則を追加する。

> 派生 struct は、その宣言位置から accessible な基底層の Value-role member と同じ Name の Value-role member を宣言できない。

すべての祖先層を対象とし、Signature、instance/type function、Field/stored/computed Property の違いで免除しない。Property は Property 自身の accessibility で判定し、非公開 accessor を理由に同名宣言を許可しない。protected の判定には派生 struct の receiver を用いる。適用不能な overload や条件付きメンバーも、Name が宣言され accessible なら禁止対象となる。

環境選択・Mod・fragment 統合後の宣言集合と有効な base graph で検査し、宣言位置にエラーを出す。同一層の overload と Value 以外の名前空間は既存規則に従う。明示的 specialization は元の関数の実装なので、新たな同名メンバーとして数えない。基底の関数を派生側の宣言として specialize する権限も追加しない。

```kimi
open struct Base
    public func f(self: ref/Self, x: i32) -> i32 => x

struct Derived : Base
    public func f(self: ref/Self, x: string) -> i32 => 0 // Error: accessible な Base.f と同名。
```

§9.5 の層コミット規則は維持し、hiding の肯定例を上記の禁止例に替える。通常の lookup を実行してから引数適合の失敗で基底 overload に戻ることはない。

**保証の範囲。** 派生宣言側から accessible で、両方の呼出し位置でも使用できる既存メンバーは、refinement 前後で同じ宣言層を参照する。Animal.speak が public なら Dog に同名を追加できず、`a`、絞り込まれた `a`、絞り込まれない `var v` はいずれも Animal.speak を参照する。派生型だけの別名メンバーは refinement 後に利用できる。

派生作者から inaccessible な同名宣言は許可する。したがって「あらゆる文脈で同じ Name は同じ宣言」という強い保証にはしない。例外には private のほか別 Kotonoha の internal/private-protected も含む。引数の Effective Type、access 条件、overload 選択まで固定する規則ではない。

**Conformance mapping。** §8.4.4/§8.4.8 で検証済みの conformance を継承する際は、Member Identity・関連型 binding・型置換・receiver 対応を保持する。同じ Name の派生宣言を検索して置き換えない。§9.5 の継承検索は、新たな明示的 conformance の実装候補を確定するときに適用する。既存と同じ `(D, C)` へ到達する conformance path は mapping が一致しなければエラーとする。総称呼出しはこの確定済み mapping を使用し、呼出し側で再検索しない。

これらは継承 conformance の既存の適格性・Signature・access・Origin 検査を省略しない。直接呼出しと Contract 呼出しの実装一致は、それらが同じ requirement の検証済み実装を参照する場合に限る。

### 3.3. ObjectCompatible の検証と公開

ObjectCompatible の安全性条件は §12.4.4 に一本化する。borrowed receiver を持つメンバーと各 accessor について、Member Identity、型置換、receiver kind ごとに検証情報を保持する。標準 accessor や適用可能な specialization も対象とする。

検証は宣言の検証処理が担い、callee・返却 borrow の effect を含める。「一度」は呼出しごとに私有本体を再解析しないという意味であり、構文を読んだ瞬間の一回の判定ではない。再帰には既定の固定点解析、総称型には §8.7–§8.10 の証明義務と期限を適用する。

| 検証情報 | 制限付き receiver での利用 |
| --- | --- |
| Proven（Compatible） | 通常の Type・access・Origin・Loan 検査のもとで利用可能 |
| Refuted | 使用位置でエラー。完全な通常値への利用までは禁止しない |
| Unknown | 証明を要する使用の期限までに解決しなければ使用位置でエラー。Refuted と同一視しない |

宣言自体の不正や矛盾は通常の宣言エラーとする。互換性を証明できないだけの通常メンバーは、完全値用として有効であり得る。

> **統一した使用条件：** 基底 subobject への borrowed receiver projection または Object Semantics による借用アクセスでは、その receiver kind の ObjectCompatible が Proven のメンバーだけを使える。

Object borrow は実体が完全な同じ型でもこの条件の対象である。「実体が完全か」の実行時判定で解除しない。完全な通常値への通常の borrow は、従来の取得・所有権規則に従う。使用違反で別 overload を選び直すことはない。

§18.3 に状態、検証済みの根拠・effect summary、既存の公開前提に依存する証明義務、依存先の Identity/version を記録する。総称宣言は公開された前提の全 binding に対して検証し、好都合な具体化だけで Compatible を得ない。私有本体から新たな Copy/Contract 条件を推論して公開呼出しの制約へ付け足さない。既存の公開前提から証明できない使用は §8.10 の定義エラーとする。

公開された Compatible と根拠を、利用側は本体の閲覧なしに使用できる。記録の欠落・不適合は安全の証明にならない。本体・callee・specialization の変更は依存する検証情報を無効化して再検証し、Compatible の撤回や公開前提の強化は API の破壊的変更として下流を再検証する。旧証明を保持したまま新実装をリンクしない。

この規則は §8.10 と直接矛盾していた規則の訂正ではなく、明示されていた使用時検査を公開検証情報へ整理する変更である。本体変更を必ず宣言エラーにする保証修飾子は今回導入しない。

### 3.4. Object View・metadata・破棄

Object View、明示的 upcast、既定の Type test、receiver adjustment、完全な動的破棄は維持する。静的に選択した基底メンバーの receiver を正しい subobject に調整しても、Member Identity は変えない。

§21.2.1 の Descriptor は Runtime Type Identity、定義済みの Supports に必要な関係、完全な動的破棄、layout/receiver 調整情報を持つ。実行時に実装を選ぶ拡張は、自身の所有節で追加の選択情報を定める。現行の必須 slot や固定 ABI は導入しない。

ObjectCompatible は compile-time interface 情報であり、各 Descriptor に載せる義務はない。実装は検証済みの処理に receiver 調整用 adapter を設けられる。現行規範文から「verified object entry」を独立した許可根拠として使う記述を除き、§3.3 の公開検証情報を参照する。

§16.3.1 の virtual 呼出し禁止文を削除し、§16.4 の破棄中の当該 object への runtime dispatch 禁止に一本化する。helper や最適化で直接呼出しに変換された場合も、意味上その object を runtime dispatch の receiver にする操作は禁止する。独立して生存する object の呼出しは対象外。nonescape、破棄済み派生層へのアクセス制限、constructor の self 呼出し禁止は維持する。

基底 View からの解放も、実際の Dynamic Type の派生層から基底層まで一度だけ破棄し、元の割当てを解放する。

### 3.5. 未導入 modifier の診断

§2.5.1 の extension と同じく、`virtual`、`override`、`abstract` をメンバー宣言の修飾子位置だけで文脈的に認識し、未導入機能の専用診断で拒否する。全位置での予約語にはしない。

認識は構造体メンバー宣言の先頭にある modifier 列で、後続が `func`、`let`、`var`、`computed` など既存のメンバー宣言開始と分かる位置に限定する。get/set 宣言の直前も同様に拒否する。`func virtual(...)`、`let override: i32`、`x.abstract()`、通常の `virtual(...)` はこの拒否に含めず、Name の既存規則に従う。語の出現だけで行全体を拒否しない。

これは診断・回復用の文脈認識であり、有効な宣言 production や修飾子順序を将来機能のために確定するものではない。

### 3.6. §6.2.4 に保持する拡張設計

次の意味論と関連図をここに集約し、現行の各節には重複させない。

- overridable 宣言と override の明示指定、元の宣言に基づく slot identity。同名再宣言禁止の例外は有効な明示 override だけに限定する。
- receiver kind と正規化後の parameter/result Type の互換性。共変 result は追加せず、label/default は静的宣言に従う。公開前提や入力 Origin 条件を強めず、結果の寿命保証を弱めない。
- accessible な対象だけを override し accessibility を保持する。別 Kotonoha の protected-internal override は protected とする例外、各 accessor の access 検査を含む。
- 各 virtual implementation と override で独立に ObjectCompatible の Proven を要求し、違反は宣言エラーにする。基底の証明は流用しない。
- runtime Contract Views と併用する場合、継承した requirement mapping は対象 slot の有効な override を追い、各 requirement を再検証する。不適合なら override を拒否し、conformance を黙って失わせない。

runtime Contract Views 自体の設計は引き続き §8.5 が所有する。そこでの override 連動の説明だけを §6.2.4 へ移し、runtime Contract を通常メンバーの virtual と同時に導入する義務は課さない。

導入時には modifier 構文、対象・receiver・generic の適格性、accessor の指定単位、abstract と構築可否、基底実装呼出し、slot/metadata と separate compilation を一緒に確定する。既存の通常メンバーは暗黙に virtual 化しない。

## 4. 採用時の文書変更範囲

| 箇所 | 必要な変更 |
| --- | --- |
| §2.5.1 / F.3 / F.6 / F.9 | 未導入 modifier の文脈的な認識・専用診断と、通常の Name を保つ境界を記載 |
| §3.5.2 | 通常の全 payload 導出規則を維持。Core.Result の適用例または参照を追加 |
| §4.6.6 / §15.1.6 | Result 要素の共有読取りと Pattern binding の結果 Type 変更、明示的 Place 借用、総称本体の再検証を追加 |
| §6.2.2 / §9.5 | accessible な基底 Value-role member と同名の宣言を禁止。hiding 例を禁止例へ変更 |
| §6.2.4 | override 固有の互換性・access・検証・図・runtime conformance 連動を集約 |
| §8.4.4 / §8.4.8 / §8.5 / §9.5 | 継承 mapping の保持と、新たな conformance の実装検索を区別。override の説明は §6.2.4 へ移す |
| §9.5.1 / §11 / §12.4.3–§12.4.4 | 静的選択と ObjectCompatible の統一した使用条件を記載。現行規範文の object entry を許可根拠とする表現を置換 |
| §8.10 / §18.3 | 公開 ObjectCompatible 記録、総称証明期限、依存関係の無効化と API 変更時の下流再検証を明記 |
| §14.10.1 | accessible な継承メンバーの宣言層が refinement 前後で変わらない例と、アクセス境界の限定を追加 |
| §16.3.1 / §16.4 | virtual 固有の禁止文を削除し、意味上の runtime dispatch 禁止を §16.4 に一本化 |
| §17.2.2 / §22.1 | Result の conditional Copy、FileError の opt-in、Option/Result の条件集合による Core shape を記載 |
| §17.2.3 / §17.4 | Copy 分類と独立した Result 警告と、同一破棄における固有警告の優先を明記 |
| §21.2 | identity・Supports・破棄・receiver 調整を一般形で記載。追加の選択情報は各拡張が定義 |
| Appendix A.3 / A.8 / B.4 | 現行の宣言・使用・artifact 検証を更新。override 固有の規則と図は §6.2.4 へ移す |
| Appendix D / F.9 | 機能の状態と所有節への参照だけを保持 |
| STATUS.md | 採用した設計範囲と、実際に完了した実装範囲を別々に更新 |

採用作業では `virtual`、`override`、`abstract`、`dispatch`、`effective implementation` と関連参照を横断確認する。§6.2.4 と Appendix D だけを修正して完了とはしない。

## 5. 採用後の受入条件

以下は実装時の検証項目であり、本提案書で実行済みとするテストではない。

| 対象 | 必須の確認 |
| --- | --- |
| Result の分類 | §2.3 の両側・入れ子・Semantics 別の判定。payload-free enum の opt-in も確認 |
| Result の取得 | Copy な Result の取得後再利用が成功。片側 Non-Copy は反対側の Case を保持していても全体取得で Move |
| Generic Copy | 両条件の証明で Copy。片方 Unknown を Copy/Non-Copy と決めつけない |
| Borrow payload | Copy 後も借用依存を保持し、寿命延長や exclusive alias を許さない |
| 共有読取り | §2.6 の Slice・ref 経由 Pattern の結果 Type 変更、推論・引数適合、tryGet の固定参照結果を確認 |
| 総称本体の移行 | head の `return s[0]` を定義時に拒否。`s[0]@ref` は全有効 binding で元の storage/Origin を借りる |
| Core shape | 両順序・括弧・重複を含む等しい原子集合を適合とする。欠落・片側のみ・無条件・追加原子・別 Copy Identity を拒否。合成自己検査と外部適合判定を分離 |
| Result の破棄と警告 | Non-Copy payload の責任は一回。Copy Result の local 破棄には固有警告一件。alias 経由も同じ。別の破棄箇所は独立に診断 |
| 同名宣言の禁止 | Field・stored/computed Property・関数の相互衝突、異なる Signature、instance/type function、祖先層、fragment/Mod、条件付きメンバーを確認 |
| access 境界 | public/protected と同一 Kotonoha の internal を拒否。派生側から inaccessible な private/別 Kotonoha の internal は許可。非公開 accessor で禁止を回避しない |
| 静的選択と refinement | public Animal.speak は let/parameter の絞込み前・中・join 後、var、具体 Dog と Animal View で同じ宣言層。別名の派生メンバーのみ追加利用可能 |
| Conformance mapping | `(Dog, Speaker)` の継承 mapping、generic 呼出し、冗長な conformance path の一致を確認。inaccessible な同名があっても既存 mapping を再検索しない |
| ObjectCompatible | 完全値に許される self 置換を projection/object borrow では拒否。get/set、callee、返却 borrow、再帰、specialization を含めた記録で検証 |
| 公開検証情報 | 本体なしの下流検証、Compatible 撤回時の下流エラーと旧証明の無効化、Unknown の期限、総称本体への隠れた条件追加禁止を確認 |
| 動的型の保持 | 静的呼出しでも既定の Type test、receiver adjustment、派生型の完全な破棄を維持 |
| 宣言・文書境界 | modifier 位置だけで専用診断。通常の同名関数・変数・member access を維持。現行の使用検証が将来専用の規則に依存しない |

Result の変更と virtual の範囲整理は独立して採用できる。Result を先に反映する場合も、Core の生成・shape 検証・能力判定と既存テストを一組として更新する。

## 6. レビュー修正の判断

| 指摘 | 判断と補正 |
| --- | --- |
| 1 hiding 禁止 | 採用。保証は accessible な基底宣言に限定し、継承 mapping と新規の実装検索を明確化した（§3.2） |
| 2 共有読取りの結果 Type | 採用。Pattern と総称本体の影響、`@ref` による移行を追加した（§2.6） |
| 3 ObjectCompatible の公開 | 修正して採用。即時の二値判定は総称型・再帰・Unknown と合わないため採用せず、宣言検証で公開情報を確定する（§3.3） |
| 4 拡張設計の集約 | 採用。所有節を一つにし、破棄禁止と metadata の重複を除いた（§3.1、§3.4、§3.6） |
| 5 Core shape の条件集合 | 採用。順序非依存の適合判定と合成 Core 自己検査を分けた（§2.5） |
| 6 警告の優先 | 採用。同一の破棄箇所に限定し、無関係な警告まで消さない（§2.4） |
| 7 未導入 modifier | 採用。宣言の文脈と lookahead に限定して専用診断を出す（§3.5） |
| 8 用語・FileError | 採用。証明結果を統一し、FileError に opt-in を加える。Data の Copy は別途必要（§2.3） |
