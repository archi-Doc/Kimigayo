# 型の同一性の正規化（案 G′）

日付：2026-10-22

状態：提案（未採用・正式仕様へ未取り込み・未実装）

本書が変更する事項は正式仕様に優先し、他は既存仕様による。仕様の節は「§n」、本書の節は「本書 n」と書く。例の「エラー」は意図した拒否、「Resource」は実装定義の上限による診断でプログラムを不正としないもの（括弧内は報告する宣言）、「（変更）」は現行仕様と結果が異なることを示す。検討は机上だけで、実装・実行による検証はしていない（論証は本書 4.10 の参考にまとめる）。

## 1. 目的と方針

### 1.1. 問題

- Contract の制約は `Self` についてのテンプレートで、導出された主語や再帰する主語にも適用される（§8.3.2、§8.5.3）。`T is CopyTree` は `T.Child`、`T.Child.Child`、… のすべてに Copy を与える。
- 現在の実装は書かれた修飾ごとに一度だけ前提を展開するので、証明が綴りに依存する（PLAN G170：`T.(Tree).Child.(Tree).Child is Copy` が Unknown）。
- 需要駆動の解法（[PlanB](2026-10-20%20G170%20Premise%20Dependencies%20and%20Inference%20PlanB.md) の案 C、単位 C1）は、同一性をテンプレート側の仮想ノードを持つ合同閉包として扱い、四回の独立レビューでもこの処理の誤った結果と二乗的な増大がなくならなかった。
- 仕様にも一因がある。同一性を分解しないので、一つのクラスに `Option<U>` と `Option<i32>` が並び、クラスを一つの形で表せない。Refuted も両側が完全確定型の場合に限られる。

### 1.2. 方針

**型の同一性を、宣言ごとに一度だけ閉じた前提のもとでの正規形の一致として定める。**

- 構築型は分解し、射影と Contract の引数は分解しない。
- 適合は型のクラスの性質とし、Contract の制約は、その Contract に適合するすべての型に綴りによらず適用する。
- 前提の閉包、その誤りと上限による Resource は宣言で扱う。使用箇所では正規化、比較と §8.3.2 の証明だけを行う（構築型の適合の証明の深さ上限は現行どおり）。

### 1.3. 変えないこと

- 構文。`is` の一つの綴り（[SETTLED](../../docs/SETTLED.md#is-has-several-meanings)）と制約の主語の形（§F.6.2 の `ConstraintSubject`）。
- `and`・`or`・`not`、閉世界、Concrete judgment、許容集合（§8.3.4）、能力規則（§8.7）の証明規則。
- Conformance Identity（§8.6.1）と外部登録の禁止。§8.3.3 の再帰 Contract の定義、自己使用の禁止、証明の深さ上限、検査順への非依存（「依存する」の読み方だけ本書 4.11 で定める）。
- Origin の関係と長さの等式は、それぞれの規則（§15.6.5、§4.4）でだけ決まる。

## 2. 現行仕様

- **同一性前提**（[§8.2.3](../../docs/spec/08-generics-constraints-and-contracts.md#823-type-identity-and-identity-premises)）：完全な型を名指す前提 `X is U` と関連型の指定から、それらを含み、型の内部（射影を含む）での置換で閉じた最小の同値関係でクラスを作る。クラスに異なる完全確定型が二つある宣言と、型が自分を真部分に含む宣言は誤り。同一性は分解しない（例 `same` で `U` と `i32` は別の型）。
- **証明**（[§8.3.1](../../docs/spec/08-generics-constraints-and-contracts.md#831-propositions-and-outcomes)、[§8.3.2](../../docs/spec/08-generics-constraints-and-contracts.md#832-proof-rules)）：規則だけで証明し、SAT・定理証明・型の列挙を使わない。`T is C` は祖先の適合と `C` の制約を、`Self` を `T` として与える。同一性は同じクラスなら Proven、両側が完全確定型で異なるときだけ Refuted。矛盾する証拠は、直接に得られるか問い合わせた命題についてだけ Error。
- **再帰と Finality**（[§8.3.3](../../docs/spec/08-generics-constraints-and-contracts.md#833-recursion-and-finality)）：再帰 Contract は有効だが、具体型の適合は証明できない。適合は自分を証拠に使えない。証明の深さは実装定義の上限で、超えると Resource。結果は検査順に依存せず、自分の判定に依存する事実は証拠にならない。
- **関連型**（[§8.5.1](../../docs/spec/08-generics-constraints-and-contracts.md#851-declaration-and-specification)、[§8.5.3](../../docs/spec/08-generics-constraints-and-contracts.md#853-constraints-on-inherited-associated-types)）：指定の複数の同一性原子は §8.2.3 に従い、完成した型は指定のすべてを満たす。Contract は深い射影を制約でき、継承した Origin パラメーターを持つ関連型（以下 family、§8.5.4）を指定できる（Iterator の `LentItem(step) is Item`）。
- **推論と抽出**（[§8.5.5](../../docs/spec/08-generics-constraints-and-contracts.md#855-inference)）：推論は凍結前提を使い、現在の定義の推論値に依存する証拠を除く。抽出は同じ名義型・タプル・具体的な Semantics の層をたどり、固定配列には長さがすでに等しいときだけ入り、Function Type・未解決の射影・未解決の Semantics の適用には入らない。他の部分（Origin を含む）は通常の結果互換性で比べる。
- **条件付き適合**（[§8.8.2](../../docs/spec/08-generics-constraints-and-contracts.md#882-verification-and-use)）：`P` を満たす型がなくても、`D` と `P` のもとで検証する。条件付きメンバー（[§7.5](../../docs/spec/07-functions-and-callable-values.md#75-function-constraints)）の前提は、Refuted なら使用時に不適用になる。
- **衝突と経路**（[§8.9.1](../../docs/spec/08-generics-constraints-and-contracts.md#891-direct-conformance-collisions)、[§8.9.2](../../docs/spec/08-generics-constraints-and-contracts.md#892-merging-paths-and-cycles)）：衝突検査は名義型・タプル・Function Type・確定した Semantics を単一化し、射影・ペアバインダーの適用・非定数の長さ・Origin を残余条件にする。親 Contract をたどって同じ Contract 宣言に戻る経路は、引数（外側の宣言の引数を含む）が違っても誤り。
- **有限の格納**（[§6.2](../../docs/spec/06-declarations-and-containers.md#62-structure-declarations)）：インライン格納が同じ宣言に戻る宣言は、引数によらず Language エラー。間接参照を通る実体化の増大は、生成の上限（Resource）に任せる。
- **構文**（[§F.6.2](../../docs/spec/appendices/F-syntax-summary.md#f62-contracts-requirements-and-conformance)）：制約の主語は Name・`Self`・関連型参照で、`Option<U> is Option<i32>` は書けない。

## 3. 変更点

| 項目 | 現行 | 新 | 理由（原則） |
| --- | --- | --- | --- |
| 判定の方法 | 最小の合同関係。決め方は規定しない | 同じ関係に分解を加え、閉じた前提での正規形の一致で決める（本書 4.10） | 2：綴り・順序に依存しない |
| 分解 | しない | 構築型は構造表（本書 4.4）で分解する。射影と Contract の引数は分解しない | 1：クラスに正規形が一つ |
| Refuted | 両側が完全確定型で異なる | 対応する位置の頭部・定数が両立しない、または出現検査に反する（本書 4.8） | 1：前提の誤りと同じ表 |
| 満たせない前提 | 完全確定型二つと自己包含だけが宣言の誤り | 宣言で報告する（本書 4.9）。条件付き適合の条件と条件付きメンバーの前提も含む（§8.8.2、§7.5 を改める、Q9） | 2：問い合わせを待たない |
| 書かれない一致を要する前提 | 誤りでない | Origin・長さ・ペアバインダーの Semantics の一致を要する前提は、証明されない要件（本書 4.9、Q3） | 3：隠れた前提を作らない |
| 構築型の適合 | 前提で仮定できる | 宣言から証明できなければならない（本書 4.5、Q1） | 1、3：源は一つ、条件は書く |
| Contract の制約 | 適合するすべての主語に適用 | 同じ。制約が語に与える事実は、多段の語のものも同一性と一つの完備化で閉じ、綴りと同一性の経路によらず伝わる（本書 4.10 の 3、Q11） | 2：綴りに依存しない |
| 再帰 Contract の引数 | 親 Contract の経路だけ制限（§8.9.2）。関連型の要件などによる再帰は制限なし | 循環のたびに束縛（外側の宣言の束縛を含む）が大きくなる再帰は誤り（本書 4.6、Q7） | 2：閉包が有限 |
| 無限の型 | 自分を真部分に含む型だけが誤り | 正規形が限りなく深くなる型も誤り（本書 4.9、Q10） | 1、2：正規形が有限 |
| Resource | 証明の深さ（§8.3.3、位置の規定はない）と推論の作業量（§8.5.5） | 経路等式の規則を含む完備化の上限超過（宣言、Q5）を加える。構築型の適合の証明の深さ（宣言と使用箇所）と推論の作業量は現行どおり（本書 4.10） | 2 |
| 構造の判断 | 抽出（§8.5.5）と衝突（§8.9.1）が個別に列挙 | 一つの構造表から読む。抽出は Function Type に入る（Q2）ほかは現行どおり | 1 |
| 指定の同一性原子 | 一つのクラスになる（「指定のすべてを満たす」との関係は読み方が分かれる） | 未決定の関連型を決めるだけで、入力を同一視しない（本書 4.11、Q12） | 3：入力への隠れた仮定を防ぐ |
| 「依存する」（§8.5.5、§8.3.3） | 一部の証拠だけが依存する場合が明確でない | 現在の定義の推論値を保留しても同じ値に決まるか（本書 4.11、Q8） | 2：順序・探索に依存しない |
| オーバーロード（§10.1、§10.4） | 書かれた同一性前提が適用可能性と比較に影響する | 分解で導かれた同一性も同じく影響する（本書 4.11、5.10） | 1：同じ同一性は同じ扱い |
| §8.3.1 の手段 | 定理証明によらない | 同一性と抽象型の適合は正規形の比較で決める。探索・場合分け・背理法はしない | 3：実際の手段を明示 |

## 4. 新しい仕様

### 4.1. 用語

| 用語 | 意味 |
| --- | --- |
| 抽象型 | 型パラメーター、ペア対象、Contract の `Self` と型パラメーター（その Contract の中）、推論中の未決定の関連型と、それらを基底とする射影 |
| 構築型 | 名義型、原始型、Unit、Never、タプル、固定配列、Semantics の層、ペアバインダーの適用、Function Type、Function Item・具体的なクロージャーの型。struct・enum の `Self` を含む。構築型を基底とする射影は、解決した型で表す（本書 4.5） |
| 段・語 | 段は射影の一歩で、関連型の宣言、定義元 Contract の束縛済み参照、Origin 引数で決まる（`T.Child` と `T.(Tree).Child` が同じ宣言と束縛を指せば同じ段）。語は、基底（射影でない抽象型）に段を並べた抽象型 |
| 具体形 | 抽象型のクラスと同一な構築型。正規形のうえで一つに決まる（本書 4.3 の 7） |
| 正規形・代表 | 各型のただ一つの形。具体形があればその正規形、なければクラスの最小の語（代表）の段の引数を正規化したもの。構築型は各部分の正規形 |
| 文脈 | 語のクラスが満たす Contract（引数つき）の集合。Contract の制約は文脈を条件として、その語と下の語に事実（同一性、適合、具体形）を与える（本書 4.10 の 3） |
| 別名 | 文脈 `C` を持つすべての主語 `S` で成り立つ一段の語どうしの同一性 `S.X ≡ S.Y` のうち、多段の語の同一性を使わずに導けるもの（書かれたもの、一段の具体形の分解、Contract 引数、連言から。`I.LentItem(a) ≡ I.Item`、本書 5.9 の `Mirror`）。`C` を持つ語の後ならどの深さにも当てはまる（`T.A` が `C` を持てば `T.A.X ≡ T.A.Y`） |
| 経路等式 | 文脈 `C` を持つすべての主語で成り立つ、主語からの異なる二つの語の同一性のうち、別名だけからは導かれないもの。書かれたもののほか、具体形の分解、`Self` を含む Contract 引数、Contract の連言からも導かれる（本書 5.4、5.9）。スコープの前提だけで成り立つ語どうしの同一性は含まない（同じ完備化には入る、本書 4.10 の 3） |

### 4.2. 前提の源

| 源 | 前提になるもの |
| --- | --- |
| 宣言の入力制約（関数の先頭領域、struct・enum の制約、条件付きメンバー、`when` の条件、要件の前提） | 連言消去（§8.3.2）で得た同一性原子と適合原子。外側の宣言の入力を含む（§6.1.4）。内側の前提は外へ出ない |
| 適合：抽象型の適合（書かれたもの、導出されたもの）、閉じた含意（`PrimitiveInteger` による `Position` など、§8.7.4）、構築型の宣言・継承・条件付きの適合 | 本書 4.3 の規則 4–6 |
| 推論の凍結前提 | その経路のスコープでだけ（本書 4.11） |

前提にならないもの：義務（宣言中の型自身の適合など）、本体、呼出し、実体化、`or`・`not` を含む原子（§8.3.2 の命題として残る）、Semantics の要件（許容集合になる）。

### 4.3. 同一性と適合

スコープの同一性 `≡` と適合 `:` は、次の規則で閉じた最小の関係である（記号は説明用で、構文ではない）。型検査は同一な型を一つの型として扱う（現行どおり）。

1. **前提**：同一性原子 `X is U` は `X ≡ U`、適合原子 `X is K` は `X : K` を与える。
2. **同値**：`≡` は反射的・対称的・推移的。前提の左右と記述順は意味を持たない（案 G の向き付けは採らない）。
3. **合同**：同一な型は、構造表の分解する位置、射影の基底、段の Contract 引数で互いに置き換えられる。`X : K<a>`、`X ≡ Y`、`a ≡ b` なら `Y : K<b>`。
4. **祖先と含意**：`X : C<a>` は祖先の適合（束縛を代入）と閉じた含意を与える。
5. **Contract の制約**：`X : C<a>` は、`C` の自身と継承した制約を、`Self` を `X`、`C` のパラメーターを `a` として連言消去したすべての原子を与える（任意の深さ、§8.5.3）。`C` の関連型宣言の要件 `associate A is R` は `Self.(C).A is R`、継承した関連型の指定 `associate P.X is R` は `Self.(P).X is R` である。
6. **宣言された適合**：構築型は、その宣言の適合のうち条件がこの関係のもとで Proven なものから、適合と関連型束縛を得る（本書 4.5）。
7. **分解（新）**：同じ頭部の二つの構築型が同一なら、対応する分解する位置どうしも同一である。

射影は分解しない（`T.(C).X ≡ U.(C).X` から `T ≡ U` は出ない）。Contract の引数も分解しない（`X : K<U>` と `X : K<i32>` は二つの適合で、`U` と `i32` を関係づけない）。本書 4.9 の誤りのないスコープでは、構築型の適合はすべて規則 6 から得たものと一致する（本書 4.5）。

### 4.4. 構造表

分解、Refuted（本書 4.8）、前提の誤り（本書 4.9）、§8.5.5 の抽出、§8.9.1 の衝突検査は、型の形をこの一つの表から読む。

| 形 | 頭部（同一には一致が必要） | 分解する位置 | 比較だけの位置 |
| --- | --- | --- | --- |
| 原始型、Unit、Never | 型そのもの | なし | なし |
| 名義型（struct、enum、ライブラリーの型、`Wrapping<T>`、入れ子の型） | 宣言 | 自身と外側の宣言の型引数 | Origin（`during`、束縛集合） |
| タプル | 要素数 | 各要素 | なし |
| 固定配列 `[N of E]` | 固定配列 | `E` | 長さ（§4.4.2 の正規化後） |
| Semantics の層 `k/V during o`（`owner/V` は `V`、§3.3.2） | `k` | `V` | `o` |
| ペアバインダーの適用 `s/V during o` | `s`。許容集合（§8.3.4）が一つの `k` なら `k/V` の行。他の形とは、許容集合が相手の Semantics と交わらないときだけ異なる。`owner` を含む集合は、`owner/V` が `V` なので何とでも交わる | `V`（同じ `s` どうし） | `o` |
| 共通 Function Type | task スロットの有無、パラメーター数 | パラメーター型、結果型 | Origin（呼出しごとの Origin は位置で対応） |
| Function Item・具体的なクロージャーの型 | 関数宣言・クロージャー式 | 実体化の型引数 | Origin |
| 抽象型 | なし（分解しない） | なし | なし |

- 同じ `s` どうしの `s/U ≡ s/V` は、どの Semantics でも `U ≡ V` を与える。頭部が同じとも異なるとも決まらない組（`s is ref or uniq` のもとの `s/U` と `ref/i32 during a`）は比べられず、それを同一とする前提は Proof の誤りになる（本書 4.9）。Semantics ケースごとの前提は作らない（§8.15.2）。
- 比較だけの位置は一致を判定するだけで、同一性から Origin の関係・長さの等式・Semantics の前提を作らない（原則 3）。一致は、Origin なら宣言の `origin` 関係のもとで §15.6.5（宣言の関係は §15.3.3）により、長さなら §4.4.2 の正規化で判定する。
- 抽出（§8.5.5）は、頭部の一致した分解する位置をたどる。固定配列には長さがすでに等しいときだけ入り、未解決のペアバインダーの適用と Function Item には入らない。Function Type には入る（Q2）。Origin は抽出の条件にせず、通常の結果互換性で比べる。
- 衝突検査（§8.9.1）の手順 2–4 は分解する位置を単一化し、比較だけの位置、射影、比べられない組を残余条件にする。手順 1 の正規化にも分解が加わるので、非衝突を証明できる組が増える。

### 4.5. 構築型と射影の解決

- 構築型の射影 `τ.(C).A(o)` は、`τ` の宣言の `C` への適合（明示、継承、親 Contract 経由、条件付き）の関連型束縛に、`τ` の型引数と `o` を代入した型である。条件はそのスコープで §8.3.2 により判定し、その証明は §8.3.3 の深さ上限に従う。適合が Refuted または Unknown なら射影は不正（§8.5.2）、深さ上限に達したら Resource（§8.3.3）。
- 具体形 `τ` を持つクラスの語の射影は、`τ` の射影と同一である。解決の結果が抽象型なら二つのクラスを併合するだけで（`T is Cell<T.(Linked).Next>` では `T.Next` 自身）、構築型の結果だけが具体形になる。
- **構築型は前提から適合を得ない。** 具体形 `τ` を持つクラスが本書 4.3 で適合 `K` を得るとき（前提、Contract の制約）、`τ is K` は宣言と能力規則から Proven でなければならない。Refuted なら前提の矛盾、Unknown なら証明されない要件（本書 4.9）で、修復候補は `τ is K` を証明する条件（`U is Copy`）。
- 閉じた型は同一性で新しいメンバー・適合・具体形を得ない。Contract の制約を構築型に当てはめた帰結は、その適合の検証（§8.6.1、§8.8.2）で保証済みである。

### 4.6. Contract の引数と再帰

- Contract の引数は段と適合の一部で、正規形で比較する（`Indexable<isize>` と `Indexable<string>` は別の適合、§8.6.1）。
- `Self` またはその射影を含む引数は、その参照を書いた節の主語からの位置を指す。それを受けたパラメーターと同一な語はその位置と同一になり、主語からの二つの語の同一性になれば経路等式である（本書 5.4 の `Parented`）。
- **拡大する再帰の禁止。** 互いに参照し合う Contract の強連結成分（以下 Contract の成分）ごとに、次の辺を調べる。
  - 参照：`Self is K` が `Self` または `Self` からの語に与える前提（祖先への適合と、本書 4.2 の適合の行）に現れる、同じ成分の Contract への束縛済み参照 `M`。その束縛は、自身の型引数と外側の宣言の束縛である（§8.6.1）。
  - 辺：`M` の各束縛を `K` の前提のもとで正規化し、`K` の束縛のパラメーター `P` が `M` の束縛 `b` に現れれば（段の Contract 引数の中を含む）、`P` から `b` を受ける `M` のパラメーターへ引く。`b` が `P` そのものなら平らな辺、`P` を真部分に含めば拡大する辺である。
  - 誤り：拡大する辺を含む循環は、循環上の各 Contract の Language エラーで、関連位置は循環を作る参照である。性質は宣言だけで決まり、上限に依存しない。
- 先例は §8.9.2（親 Contract の経路は `Outer<T>.C` から `Outer<List<T>>.C` でも誤り）である。失うのは、具体型が適合できない Contract（無限に多くの適合を要するもの）の抽象的な使用だけである。有限性の論証は本書 4.10 の参考に置く。

### 4.7. family

- family の段は Origin 引数を含み、引数の異なる適用は別の語である。family の制約は Origin パラメーターについて図式的で、すべての引数に当てはまる。family の指定は、右辺の段の数によらず Origin 引数を持たない側へ正規化される（代表の順序、本書 4.10 の 6、本書 5.7）。Iterator の `associate LendingIterator.LentItem(step) is Item`（§22.2.1）により、任意の `a` で `I.LentItem(a)` の正規形は `I.Item` になる。
- `LendingIterator` だけのもとでは `I.LentItem(a)` と `I.LentItem(b)` は別の語で、その同一性は Unknown（Refuted ではない）。二つを結ぶ前提は二つの語の併合で、`a` と `b` を関係づけない。形成条件（§8.5.4）は適用の妥当性で、同一性の規則ではない。

### 4.8. 判定結果

命題は正規形で識別する。同じ頭部の構築型どうしの同一性の命題は、分解する位置の同一性と比較だけの位置の一致の連言と同じ命題とする（§8.3.1 の正規化に加える）。

| 命題 | Proven | Refuted | Unknown |
| --- | --- | --- | --- |
| `X is U`（同一性） | 正規形が一致 | 対応する位置で頭部（本書 4.4）または定数の長さが異なる、または一方の語が他方の分解する位置に真部分として現れる（`owner` を含む `s/V` の内側を除く） | それ以外 |
| `X is K`、`X` の正規形が抽象型 | 文脈が `K` を持つ | 利用できる `not` 前提があるときだけ（閉世界） | それ以外 |
| `X is K`、`X` の正規形が構築型 | 現行どおり（Concrete judgment、検証済みの適合） | 現行どおり（閉世界） | 現行どおり |

- Error は現行どおりで、スコープの矛盾（本書 4.9 の Language の誤り）を加える。
- Refuted は命題を前提に加えた矛盾から得ない（背理法をしない、§8.3.2）。使用箇所では閉包を作り直さない。
- 完備化が上限に達したときの結果は本書 4.10 の 5 による。

### 4.9. 宣言の誤り

本書 4.10 の 2 の各スコープで、閉じた前提について次を検査する。

- **報告位置**：誤りは、それを生む前提が初めてそろうスコープで一度報告する。Contract の成分の前提（`Self is C`）だけから生じるものは、その節が関わる成分の各 Contract（Contract での検査は新しい規則）、Contract の連言や他の前提を要するものはその制約付き宣言（本書 4.10 の 2 の一覧）で報告する。
- **区分**：満たす型がない前提と、正規形が有限にならない前提は Language エラー。書かれていない事実を要する前提と、ペアバインダーの Semantics で満否が変わる前提（`owner` を含む `s/V` だけを通る無限の型を含む）は証明されない要件（Proof、§23.1.1）。

| 誤り | 区分 | 例 | 修復候補 |
| --- | --- | --- | --- |
| 衝突：頭部や定数が異なる構築型が同一 | Language | `T is Option<U>` と `T is Array<V>` | 前提を除く |
| 無限の型：正規形が限りなく深くなる（下記。自分を真部分に含む型を含む） | Language | `T is Option<U>` と `U is Option<T>`、本書 5.3 の `Stream`・`Pairing`・`Spiral` | 同上 |
| 両極性：`not P` の前提で `P` が Proven | Language | `T is Option<U>`、`U is i32`、`T is not Option<i32>` | 同上 |
| 構築型の適合が Refuted（本書 4.5） | Language | `T is Linked` と `T is Option<T.(Linked).Next>` | 同上 |
| 呼出しごとの Origin が Function Type の外へ出る分解 | Language | `F is (V) -> ()` と `F is (ref/i32) -> ()` | なし |
| 拡大する再帰（本書 4.6） | Language | 本書 5.4 の `Nested`・`Tower` | なし |
| 構築型の適合が証明されない（本書 4.5） | Proof | `T is Option<U>` と `T is Copy` | 証明する条件 `U is Copy` |
| 比較だけの位置（Origin、長さ）の一致が証明されない | Proof | 本書 5.6 の `otherView`、`T is [N of u8]` と `T is [M of u8]` | `origin a == b`、同じ長さパラメーター |
| ペアバインダーの Semantics で満否が変わる：頭部が比べられない組（本書 4.4）、`owner` を含む `s/V` だけを通る無限の型 | Proof | `s is ref or uniq` のもとの `T is s/U` と `T is ref/i32 during a`、本書 5.6 の `loopy` | `s is ref`、`s is owner` |

**無限の型の判定。** 正規形の計算は、具体形の分解する位置と語の段の引数に入る。この計算が限りなく深くなる型が無限の型である。具体形のないクラスの正規形は代表の順序により有限なので（本書 4.10 の 6）、無限の型は具体形を通る。具体形には、分解、別名、経路等式、Contract 引数、構築型の射影の解決、Contract の連言、多段の制約から導かれたものも含む（本書 5.3 の `Pairing`）。判定は次の有限のグラフ（本書 4.10 の参考）の循環による。

- 節点：具体形を持つスコープのクラスと、組 `(C, w)`。`C` は文脈、`w` はその閉包が扱う語で、`C` を持つすべての主語 `S` について `S.w` が具体形を持つもの。
- 辺：節点から、その具体形に（分解する位置と段の引数を通って）現れる各語の具体形を与える節点へ引き、主語の深さの変化を付ける。主語からの語 `S.v` は、`S.u.w`（`S.u` の文脈が `C`）と分けて `(C, w)` が節点になるもののうち `w` が最も短いものへの `+|u|` の辺である。Contract 引数（パラメーターの束縛、`Self` からの位置）の中の語は、その引数を書いた節の主語（`k` 段上。スコープの前提ならスコープ）から同じように分け、`−k` を加える。`Stream` では `S.Next.Head` が `(Stream, Head)` への `+1` の辺になり、`Window` では `S.Head` に具体形がないので `(Window, Next·Head)` から辺が出ない。
- 無限の型：変化の合計が正の循環（`Stream`、`Spiral`）と、0 の循環（自分を真部分に含む型）。負の循環は根で止まるので有限である。
- 区分：循環が `owner` を含む `s/V` だけを通るなら `s is owner` で消えるので Proof の行、それ以外は Language の行である。

### 4.10. 決定と Resource（仕様が定める範囲）

1. 前提の閉包が完了し本書 4.9 の誤りのないスコープでは、各型に正規形がただ一つあり、二つの型は正規形が一致するとき、かつそのときに限り本書 4.3 の意味で同一である。
2. 閉包は、Contract の成分と制約付き宣言（関数、型の制約、条件付き適合のスコープ、要件の検査、条件付きメンバー、推論の経路）ごとに、その宣言で一度、外側のスコープの結果に追加して行う。スコープの閉包は、そこで初めて生じる文脈（Contract の連言）の閉包を含む。使用箇所（式、呼出し、条件付きメンバーの適用、実体化）では正規化、比較と §8.3.2 の証明だけを行う。
3. 語どうしの同一性（別名、経路等式、スコープで成り立つ語どうしの同一性）は、代表の順序で大きい語から小さい語への書き換え規則とし、Contract の多段の制約が語に与える事実（深い射影の適合、多段の語の具体形、§8.5.3）とともに、Knuth–Bendix 型の一つの完備化で閉じる。別名、経路等式の規則と多段の事実はその文脈を持つ語の後に、スコープの規則はその語とそれに段を続けた語にだけ当てはまる。重なりは、スコープの規則の両辺の各接頭辞の文脈を含めてすべて調べ、事実を持つ語を書き換えた語は同じ事実を持つ（本書 5.1 の `dup`、5.9 の `Mirror`、`square`）。閉包の手順のうち上限を持つのは経路等式の規則を含む完備化だけである。これとは別に、構築型の適合の証明（条件付き適合の条件と射影の解決を含む、本書 4.5）は宣言でも使用箇所でも §8.3.3 の深さ上限に、推論は §8.5.5 の作業量の上限に従う（いずれも現行どおり、本書 5.5）。使用箇所で上限に達しうるのは構築型の適合の証明だけである。
4. 経路等式の規則を含む完備化は、実装定義の二つの上限（導いた規則の数とラウンド数）を持つ。超えたら、その完備化が初めて要るスコープ（本書 4.9 と同じ位置）の Resource 診断で、プログラムを不正としない（§23.1.1）。経路等式の規則を含まない完備化（別名、スコープの規則と多段の事実だけ）は上限なしに終わる。
5. 上限に達しても、得た規則を使う。どれも正しい同一性なので、正規形の一致による Proven と、得た規則による Error は残る。Refuted または Unknown になる判定のうち、その完備化の規則が及ぶ語（その文脈を持つ語、そのスコープの語）を正規化するものは Resource になり、他の判定は通常どおりである。上限の診断は上限に達したスコープで一度だけ報告し、それを外側に持つ宣言やその Contract を使う宣言で Resource になった判定は、それに関連づけてまとめる（§23.1.2）。
6. 結果は前提と制約の記述順・向き・綴り・検査順に依存しない。代表と完備化の順序は、段の引数を先に正規化して語を比べる整礎な順序で、語の前後と段の引数の中での書き換えと両立し、語は段の引数に含む語より大きい。仕様は (a) family の段（段の引数の中のものを含む）の少ない語が先、(b) その数が同じなら大きさ（基底、段、段の引数の各部分をそれぞれ 1 と数えた和）の小さい語が先、を定め、残りは宣言の同一性（パラメーターの位置、修飾名）だけで決まり同じ性質を持つ順序として IMPL が定める。(a) により family の指定は右辺の段の数によらず Origin 引数を持たない側へ向き（本書 4.7）、(b) により具体形のないクラスの正規形は有限である（本書 4.9、5.4）。完備化は重なりをすべて調べるので、順序が変えうるのは上限に達するかどうか（と、そのとき Resource になる判定）だけで、それは宣言だけで決まり、検査順では変わらない（名前の変更では変わりうる）。

**参考：論証と正規形の形（仕様の規定ではない。論証は机上で、検証していない）。**

- 有限性（本書 4.6）：成分の束縛済み参照の束縛（外側の宣言の束縛を含む）に入る型は、`Self` を含む語を記号とみなせば有限個なので、文脈と閉包は有限である（名義的部分型の expansive 判定、Kennedy–Pierce 2007 と同じ形）。§6.2 のように生成の上限に任せると、宣言ごとの閉包が終わらない。
- 停止（本書 4.9、4.10 の 3–4）：文脈も各文脈の閉包が扱う語も有限なので、無限の型のグラフは有限で、完備化以外の手順は本書 4.6 と 4.9 により終わる。経路等式の規則がなければ、規則の重なりはいつも既存の規則の左辺の中で起こり（別名は一段、スコープの規則は語の先頭に固定）、重なった規則をその左辺より小さい語の規則に置き換えるだけなので、左辺の多重集合が整礎な順序で減り、完備化は終わる。family の段を減らす規則は語を長くしうるが、この論証は語の長さにも段の種類の有限性にもよらない。多段の事実が移るのは、スコープの規則の左辺にかかる有限個の位置だけである。
- 文脈は語のクラスの性質で、書き換え規則ではない。段を一つ進めると、その文脈と一段の制約から次の文脈が決まる。文脈は有限なので、`T.Child^k` を深さごとに作らない。G170 はこの遷移を二回たどるだけで決まる。多段の制約（`Deep` の `Next·Head is Copy`、`Window` の具体形）は文脈を条件とする事実として完備化に入り、`dup` ではスコープの規則 `T·Next·Next → U` と重なって `U·Head` に移る。
- 具体形は書き換えの対象ではなく、分解（本書 4.3 の 7）と衝突（本書 4.9）に使う。
- 単一化が語どうしの同一性を導けば（本書 5.9 の `both`）、それも完備化に加わる。語の文脈が増えたら（前提、クラスの併合）、増えた文脈の規則と事実（別名、経路等式、多段の事実）と既存の規則の重なり（critical pair）を調べる。他の規則と重ならない経路等式（`Sequence`）は規則を導かない。
- Swift の Requirement Machine（文字列書き換え系の完備化、具体型の性質表、上限を超えたときのエラー）と同じ構造だが、適合を文字にしない点が異なる（本書 6.3）。

### 4.11. 他の機能との関係

- **関連型の指定と推論**（§8.5.1、§8.5.5）：適合の凍結前提では、Contract の制約と指定の同一性は、その定義の未決定の関連型を決めるため（分解を含む）にだけ使う。それ以外の帰結（型パラメーターなど入力どうし、決定済みの型どうしの同一性）は適合の義務として検査し、仮定しない。決定とは、経路のもとでの正規形に未決定の関連型が残らないことである。
- **依存の判定**：§8.5.5 で使える証拠は、現在の定義で推論したすべての値を保留（抽象型）にしても同じ値に決まるものとする。§8.3.3 の「自分の判定に依存する事実」も、その判定を保留して読む。受理は導出の記録に依存しない。
- **条件付き適合と条件付きメンバー**（§8.8、§7.5）：`when P` の同一性原子は適合スコープの前提で、分解を含む。満たせない `P` と、条件付きメンバーの満たせない前提は本書 4.9 の誤りである（Q9）。使用時の `P` は頭部の違いで Refuted になりやすく、ブロックのメンバーや経路が未証明でなく不適用になる。
- **オーバーロード**（§10.1、§10.4）：スコープの前提から分解で導かれた同一性は、書かれた同一性前提と同じく、候補の適用可能性と比較に影響する。
- **表示と診断**（原則 4）：診断と hover は記述どおりの型を示し、同一性を使った判断では「≡ 正規形」を併記する。導出した同一性は源（前提、制約、分解、解決）を持ち、関連位置と修復候補に使う。Unknown の同一性の修復候補は単一化子を前提に加えることで、前提条件は「Refuted でなく、矛盾を生まない」、保証は「その要求が Proven になり、呼出し側がそれを証明する」である。

## 5. 例

`Source` と `IntSource` は §8.4.1・§8.5.3 の宣言である。

### 5.1. G170 と再帰 Contract

```kimi
contract Tree
    associate Child is Tree
    associate Leaf

contract CopyTree: Tree, Copy
    Self.(Tree).Child is CopyTree

contract IntTree: Tree
    Self.(Tree).Child is IntTree
    Self.(Tree).Leaf is i32

func twice<T>(value: T.(Tree).Child.(Tree).Child) -> (T.Child.Child, T.Child.Child)
    T is CopyTree
    return (value, value)           // 有効: 綴りによらず孫は Copy。

func twiceBoth<T>(value: T.Child.Child) -> (T.Child.Child, T.Child.Child)
    T is CopyTree
    T is IntTree
    return (value, value)           // 有効: 文脈 {CopyTree, IntTree, Tree, Copy} は深さによらない。

contract Deep
    Self.(Deep).Next.(Deep).Head is Copy
    associate Next is Deep
    associate Head

func dup<T, U>(value: U.(Deep).Head) -> (U.(Deep).Head, U.(Deep).Head)
    T is Deep
    T.(Deep).Next.(Deep).Next is U
    return (value, value)           // 有効: T.Next での Deep の制約が、書き換えた U.Head に移る。
```

### 5.2. 分解

```kimi
func same<T, U>(value: U) -> i32
    T is Option<U>
    T is Option<i32>
    return value                    // 有効（変更）: 分解で U は i32。
```

`F is (U) -> U` と `F is (i32) -> i32` からも、`Self.(Iterator).Item is Option<Self.Payload>` を持つ Contract のもとの `I.(Iterator).Item is Option<E>` からも（`E ≡ I.Payload`）同じ規則で出る。前提の左右は意味を持たず、`U is T.(Source).Element` の代表は小さい `U` である（本書 4.10 の 6）。

### 5.3. 無限の型

```kimi
contract Stream
    // エラー（変更）: Head の正規形が Option<Option<…>> と限りなく深くなる。
    Self.(Stream).Head is Option<Self.(Stream).Next.(Stream).Head>
    associate Next is Stream
    associate Head

contract Pairing
    Self.(Pairing).W is Option<Self.(Pairing).Head>
    // エラー（変更）: 二つの W の分解が Head ≡ Option<Next.Head> を導き、Stream と同じになる。
    Self.(Pairing).W is Option<Option<Self.(Pairing).Next.(Pairing).Head>>
    associate Next is Pairing
    associate W
    associate Head

contract Window
    Self.(Window).Next.(Window).Head is Option<Self.(Window).Head>  // 有効: Self.Head に具体形がない。
    associate Next is Window
    associate Head

contract Segment
    associate Head
    associate Tail
    associate Next is Segment

contract Spiral<P>: Segment
    Self.Tail is Option<P>
    Self.Next is Spiral<Self.Head>
    Self.Head is Option<Self.Next.Next.Tail>  // エラー（変更）: 引数を通って Head ≡ Option<Option<Next.Head>>。
```

- `Pairing`：`Next`・`W`・`Head` を共通の親 Contract で宣言し、二つの W の節を、`Next` を自身に適合させる二つの子 Contract に分けて関数で連言すると、子はそれぞれ有効で、同じ誤りはその関数で報告される（本書 4.9）。
- `Spiral`：循環は `Next.Next.Tail` への辺（+2）と、`P` を書いた一段上の `Head` への辺（−1）からなり、合計が正である。段はすべて `Segment` のもので、`Head` の正規形に同じ主語の `P` は現れないので、本書 4.6 の辺はない。
- `Window`：`T is Window` と `T.(Window).Next.(Window).Next is T` を前提にする関数では、`T.Next` での具体形も `T.Head` に移り、`T.Head ≡ Option<Option<T.Head>>` の誤りになる（現行も自己包含の誤り）。

### 5.4. Contract の引数と再帰

```kimi
contract Nested<A>
    associate Inner is Nested<Option<A>>  // エラー（変更）: 引数が大きくなる。Nested<A> なら平らな辺で有効。

contract Walker<A>
    associate Here
    associate Next is Walker<Here>  // エラー（変更）: Here は Self.(Walker<A>).Here で A を含む。

contract Upper<A>: Lower<Option<A>>  // エラー（変更）: 親 Contract を通る拡大する循環。

contract Lower<B>
    associate Turn is Upper<B>      // エラー（変更）: 同じ循環。

struct Tower<T>
    public contract Level
        associate Above is Tower<Option<T>>.Level  // エラー（変更）: 外側の束縛 T が大きくなる。

contract Parented<A>
    associate Parent is A
    associate Child is Parented<Self>   // 有効: Self.Child.Parent は Self（導かれた経路等式）。

contract Carrier<A>
    Self.(Carrier<A>).Value is A
    associate Value

contract Boxed
    Self is Carrier<Self.(Boxed).Content.(Source).Element>  // 有効: Self.Value の正規形は Self.Content.Element。
    associate Content is Source
```

`Boxed` では `Self.(Carrier<…>).Value` の段の引数が `Self.Content.Element` を含むので、Value の語のほうが大きい（本書 4.10 の 6）。段の数だけで比べると、Value の語が代表になり、その引数の正規化が同じクラスに戻って終わらない。

### 5.5. 射影を通る自己参照と構築型の適合

```kimi
contract Linked
    associate Next

enum Cell<E>
    Self is Linked
    associate Linked.Next is E
    Item(E)
    Empty

func depth<T>(value: T) -> i32
    T is Linked
    T is Cell<T.(Linked).Next>      // 有効: T.(Linked).Next は自身に解決し、T と別の型。案 G では Resource。
    return 0

func optional<T>(value: T) -> i32
    T is Linked
    T is Option<T.(Linked).Next>    // エラー（変更）: Option<…> is Linked は Refuted。
    return 0

func copyOption<T, U>(value: T) -> (T, T)
    T is Option<U>
    T is Copy                       // エラー（変更）: 証明されない。修復候補 U is Copy。
    return (value, value)
```

構築型の適合の証明は現行どおり深さ上限を持つ。`struct Loop<T>` が `Self is Done when T is Linked, T.(Linked).Next is Done` を持ち、`struct Grow<X>` の `Linked.Next` が `Loop<Grow<Option<X>>>` なら、`Loop<Grow<i32>> is Done` の証明を要する箇所（宣言でも使用箇所でも。呼出し、条件付きメンバーの適用、射影 `Loop<Grow<i32>>.(Done).Value` を含む）は Resource になる（本書 4.10 の 3）。

### 5.6. Origin とペアバインダー

```kimi
func sameView<T, U>(first: ref/U during a, second: ref/i32 during b, item: U, value: T) -> i32
    T is ref/U during a
    T is ref/i32 during b
    origin a == b
    return item                     // 有効（変更）: Origin が一致し、U は i32。

func otherView<T, U>(first: ref/U during a, second: ref/i32 during b, value: T) -> i32
    T is ref/U during a
    T is ref/i32 during b           // エラー（変更）: a と b の一致が証明されない。修復候補 origin a == b。
    return 0

func loopy<s/T, U>(value: s/T, item: s/U during a) -> i32
    s is owner or ref
    U is s/U during a               // エラー: s が ref なら満たせない。修復候補 s is owner（現行は読み方が分かれる）。
    return 0

func distinct<X, Y>(first: X, second: Y) -> i32
    X is not Y
    return 0

func probe<s/T, U>(value: s/T, first: s/U, second: U) -> i32
    s is owner or ref
    return distinct(first@move, second@move)  // エラー: s が owner なら s/U は U なので、s/U is not U は Unknown。
```

### 5.7. Iterator と LentItem

```kimi
func firstItem<I>(iterator: uniq/I) -> Option<I.(Iterator).Item>
    I is Iterator
    return iterator.next()          // 有効: 結果の正規形は Option<I.Item>。

contract Adapted: LendingIterator
    associate Inner is Iterator
    associate LendingIterator.LentItem(step) is Self.(Adapted).Inner.(Iterator).Item

func firstInner<A>(source: uniq/A) -> Option<A.(Adapted).Inner.(Iterator).Item>
    A is Adapted
    return source.next()            // 有効: A.LentItem(step) の正規形は A.Inner.Item。
```

`next` の結果 `Option<I.LentItem(step)>` は別名で `Option<I.Item>` になり、`step` の Loan は残らない。ライブラリーの `OwningIterator<I>`（`Self is Iterator when I is Iterator`）の検証でも、`LentItem(step)` は条件のもとで `I.(Iterator).Item` に正規化される。`Adapted` では右辺が二段でも、family の段を持たない `A.Inner.Item` が代表になる（本書 4.10 の 6 (a)）。段の数を先に比べると `A.LentItem(o)` が `A.Inner.Item` より小さくなり、代表の Origin 引数 `o` が一つに決まらない（宣言した結果型には `step` がない）。

### 5.8. 関連型の指定

```kimi
contract Wrapped
    associate Inner
    associate Outer is Option<Inner>

struct Number
    Self is Wrapped
    associate Wrapped.Outer is Option<i32>  // 有効（変更）: Inner は i32 に決まる。

contract Holder
    associate Item

struct Pack<T>
    Self is Holder
    associate Holder.Item is Option<T> and Option<i32>  // エラー: T を i32 と仮定できない（現行は読み方が分かれる）。
```

制約による同じ形（`Self is IntSource` と `associate Source.Element is T`）も、`T` を `i32` と仮定できないので誤りになる。

### 5.9. 語どうしの同一性と Resource

```kimi
contract Sequence
    Self.(Sequence).Cursor.(Iterator).Item is Self.(Sequence).Element  // 有効: 重なりのない経路等式。
    associate Element
    associate Cursor is Iterator

contract Mirror: Sequence
    Self.(Sequence).Cursor is Self.(Mirror).Back  // 別名。
    associate Back is Iterator

func last<T>(value: T.(Mirror).Back.(Iterator).Item) -> T.(Sequence).Element
    T is Mirror
    return value@move               // 有効: どの順序でも Proven（下記）。

contract Braid
    // Resource（変更、Braid の宣言）: どの順序でも完備化が終わらない。
    Self.(Braid).A.(Braid).B.(Braid).A is Self.(Braid).B.(Braid).A.(Braid).B
    associate A is Braid
    associate B is Braid

contract Commuting
    Self.(Commuting).A.(Commuting).B is Self.(Commuting).B.(Commuting).A  // 有効: 自身と重ならず、規則を導かない。
    associate A is Commuting
    associate B is Commuting

func square<T>(value: T) -> i32
    T is Commuting
    // Resource（変更、square の宣言）: Commuting の規則と重なり、どの順序でも段の数ごとに規則が増える。
    T.(Commuting).A.(Commuting).A is T.(Commuting).B.(Commuting).B
    return 0
```

- `last`：順序が `Cursor` を後とすれば、別名 `Cursor → Back` と `Cursor·Item → Element` の重なりから `Back·Item → Element` が導かれる。逆の順序では重なりがなく、`Back·Item → Cursor·Item → Element` と書き換わる。
- `Braid`：`ABA = BAB` は二文字の上で有限の完全な書き換え系を持たない（Kapur–Narendran 1985）。
- `square`：前提は経路等式でないが、`A·B ≡ B·A` と合わせると `T·Bⁱ·A·A → T·Bⁱ⁺²`（逆の順序では `T·Aⁱ·B·B → T·Aⁱ⁺²`）がすべての `i` で要る。
- `both`：経路等式は Contract の連言からも導かれる。共通の親 Contract が `W`・`A`・`B` を宣言し、それを継承する `Left` と `Right` がそれぞれ `A`・`B` を自身に適合させ、`Left` が `Self.W is Option<Self.A.B.A>`、`Right` が `Self.W is Option<Self.B.A.B>` を持てば、それぞれは経路等式を持たない。両方を前提にする関数 `both` では `A`・`B` が両方の文脈を持ち、W の分解が `ABA ≡ BAB` を導くので、`both` が Resource になる（変更）。

### 5.10. 他の機能との関係

```kimi
contract Flag

struct Slot<T>
    Self is Flag when T is Option<i32>
    var value: T

func plain<T>(value: T) -> i32
    T is not Flag
    return 0

func fill<V>(slot: Slot<Array<V>>) -> i32
    return plain(slot@move)         // 有効（変更）: 条件 Array<V> is Option<i32> は Refuted（現行は Unknown）。

contract Transformer
    associate Input
    func apply(self) -> (Input) -> i32

func doubled(value: i32) -> i32 => value * 2

struct Doubler
    Self is Transformer
    // 有効（変更、Q2）: 抽出が Function Type に入り、Input は i32。
    public func apply(self) -> (i32) -> i32 => doubled
```

オーバーロードでは、`same` の前提のもとで `value: U` を渡す呼出しが、`func show(value: i32)` と `func show<V>(value: V)` のうちジェネリックでない前者を選ぶ（§10.4、変更。現行は後者）。

## 6. 評価

### 6.1. Kimigayo Principles

| 原則 | 評価 |
| --- | --- |
| 1. One Concept, One Canonical Form | 同一性は一つの関係で、各型の正規形は一つで有限。前提の向き・綴り・記述順は意味を持たない。型の形は一つの構造表（本書 4.4）、語どうしの同一性は一つの完備化で扱う。誤りのないスコープでは構築型の適合の源は宣言だけ |
| 2. Local Reasoning | 同一性は、宣言の制約、外側の入力、名指した Contract と祖先、現れる構築型の適合宣言だけで決まる。前提の誤り（満たせない前提、拡大する再帰を含む）と完備化の Resource は宣言だけで決まり、宣言で一度報告する（本書 4.9、4.10） |
| 3. Explicit Semantics | Origin・長さ・Semantics の関係は書かれたものだけ。構築型の適合の条件は書く。入力への同一性を指定から仮定しない |
| 4. Compiler Server Protocol | 正規形、代表、導出の源、前提条件と保証つきの修復候補を問い合わせられる。結果は宣言と設定だけで決まり、再現できる |

### 6.2. consistent・short・fast

- **Consistent**：同一性の決定は一つの所有者（宣言の閉包と使用箇所の正規化）にあり、Contract の事実と同一性が一つの不動点になる。誤りと Resource の報告位置は一つの規則（本書 4.9）で決まる。PlanB の同一性の部品（本書 1.1 の仮想ノード）が要らない。
- **Short**：宣言ごとの閉包は合同閉包、単一化、有限の文脈の遷移、無限の型のグラフで、上限つきの完備化は経路等式の規則があるときだけ働く。各規則の形の一覧を構造表に置き換える。
- **Fast**：使用箇所の判定は型の大きさにほぼ比例する。宣言の前提はほぼ線形の合同閉包で、無関係な問い合わせは他のクラスを読まない。いずれも未計測。

### 6.3. 比較した案

| 案 | 判断 | 理由 |
| --- | --- | --- |
| 現行（分解しない）と一般規則・e-graph の実装方針 | 不採用 | 正規形がなく、本書 1.1 の問題が残る |
| 案 G（向き付き束縛、限界付き合流、構築子の分解） | 不採用 | 向きが意味を持ち、`depth` を Resource にし、再帰 Contract で主語ごとの規則が無限になる |
| G′ の項 2 そのまま（適合を文字 `T·[C] → T`） | 不採用 | `CopyTree` で深さごとの規則 `T·Childᵏ·[CopyTree] → T·Childᵏ` が生じ、完備化が終わらない |
| 適合の文字と Contract ごとの段（Swift の形） | 不採用 | 再帰する二つの Contract の連言（`twiceBoth`）で、連言ごとの記号が要る（机上の分析） |
| 経路等式が書いたとおりに合流することを要求 | 不採用 | 利用者に完備化を手で書かせ、有効性が名前の順序で変わる。本書では順序は Resource の有無にしか影響しない |
| 別名、前提の語どうしの同一性、多段の制約の事実を合同閉包や正規形に沿った伝播だけで扱う | 不採用 | 他の規則と重なると完全にならず、成り立つ同一性や適合が順序や綴りによって Unknown になる（`last`、`square`、`dup`） |
| `Self` を含む Contract 引数を禁止する | 不採用 | 有限性に要らず、`Parented` や `contract Summable: Addable<Self>` を失う |
| 構築型の適合を前提で仮定する | 不採用 | 適合の源が二つになり、仮定の意味が局所に読めない |
| 引数の増大を許し、完備化の発散だけを Resource にする | 不採用 | 宣言だけで決まる性質を上限に依存させ、閉包が有限でなくなる |
| `owner` を含む `s/V` を具体形にしない | 不採用 | クラスに具体形でない二種類目の成員が要る。満否は Semantics で決まるので Proof の誤りにする（`loopy`） |
| 命題を加えた矛盾で Refuted を判定する | 不採用 | 背理法で、使用箇所で閉包が要る |
| 分解で Origin・長さ・Semantics の関係を導く | 不採用 | 書かれない依存（原則 3）。Semantics ケースごとに不健全（SETTLED「Pair-slot premises in every Semantics case」） |
| 構築型の主語を書けるようにする | 不採用 | `U is i32` の二つ目の綴りになる |
| 同日の別案「[Type Identity by Structural Decomposition](2026-10-22%20Type%20Identity%20by%20Structural%20Decomposition.md)」 | 別案 | 決定の方法と Resource の位置を定めず、Q1・Q2 に別の答えを持つ。本書を採用するときは閉じる |

### 6.4. 結果の変化

| 区分 | 内容 |
| --- | --- |
| 有効になる | 分解の帰結（`same`、`Number`、`sameView`）。頭部の違いによる Refuted（`fill`、衝突検査の非衝突、`not` 要件）。抽出が Function Type に入る（`Doubler`、Q2）。G170 は仕様上すでに有効で、綴りによらないことが決定の規則になる |
| エラーになる | 本書 4.9 の誤りのうち現行で誤りでないもの（頭部の衝突、`Stream`、`Pairing`、`Spiral`、両極性、`optional`、`copyOption`、`otherView`、長さの一致を要する前提、頭部が比べられないペアバインダーの組、呼出しごとの Origin が外へ出る分解、条件付き適合の条件と条件付きメンバーの前提で満たせないもの）。拡大する再帰（`Nested`、`Walker`、`Upper`・`Lower`、`Tower`）。入力を同一視する指定（`Pack`）、`loopy`、段の引数を通る循環（Q10）は、現行は読み方が分かれる |
| Resource | 上限を超える完備化（本書 4.10 の 4。`Braid`、`square`、`both`）。構築型の適合の証明の深さは現行どおり（本書 5.5） |
| 結果が変わる | オーバーロードの比較（本書 5.10）。矛盾の報告位置が問い合わせから宣言へ移る。§8.5.5 の依存の判定（Q8） |

## 7. 未決事項

| 番号 | 問い | 推奨 |
| --- | --- | --- |
| Q1 | 構築型は前提から適合を得ない（本書 4.5）。複数の経路の条件の選言は書けなくなる | 採用する |
| Q2 | 抽出（§8.5.5）を Function Type に入れる（`Doubler`）。呼出しごとの Origin を含む抽出は従来どおり使えない | 入れる |
| Q3 | Origin（名義型の束縛集合を含む）・長さ・ペアバインダーの Semantics の一致を要する前提を Proof の誤りにする（満たせる前提を狭める、本書 5.6） | 誤りにする。Origin とペアバインダーは `origin` 関係・Semantics 前提を書けば足りる。長さには前提形式を加えず、同じ長さパラメーターで書く（長さを同一視する前提は書けなくなる） |
| Q4 | 経路等式を残すか、Appendix D に送るか。送るなら、書かれた経路等式と、それを導く宣言（`Parented`、`both`）を誤りにし、`Sequence` の形も書けなくなる。完備化は別名とスコープの規則だけになり、上限は要らない | 残す |
| Q5 | 完備化の上限を、経路等式の規則を含む完備化だけに数える（本書 4.10 の 4） | 採用する |
| Q6 | 上限の値と数え方、代表の順序のうち仕様が定めない部分 | IMPL に置く。名前の変更で上限到達が変わりうる（検査順では変わらない）ことを認める |
| Q7 | 拡大する再帰を誤りにする（本書 4.6） | 採用する |
| Q8 | §8.5.5 と §8.3.3 の「依存する」を保留で定める（本書 4.11）。現在の定義の値に依存しない証拠と依存する証拠が両方ある関連型は、保留で読めば決まり、導出の記録で読めば決まらない | 採用する |
| Q9 | 満たせない前提を宣言で報告する（本書 4.9）。条件付き適合の満たせない条件と条件付きメンバーの満たせない前提も誤りになる（今は §8.2.3 の二つの場合を除き、使用時に不適用になるだけ） | 採用する。別案は、それらを使われない宣言として警告に留める |
| Q10 | 正規形が限りなく深くなる型（`Stream`、`Pairing`、`Spiral`）を誤りにするか、一段ずつの正規形で有効のまま扱うか。段の引数を通る循環 `T is Option<U.(K<T>).X>` も同じ規則で誤りになる。これが現行の自己包含（§8.2.3）に入るかは読み方が分かれる（射影の基底を部分と読めば有効な `T is Cell<T.(Linked).Next>` も入る） | 誤りにする。これらに適合できる具体型はなく、正規形を有限に保てる（有効のままでは比較に別の規則が要る）。段の引数を通る循環は満たせることがあるが、まれな形である |
| Q11 | Contract の多段の制約が語に与える事実（深い射影の適合、多段の語の具体形）を、文脈を条件とする事実として語どうしの同一性と一つの完備化で閉じる（本書 4.10 の 3、`dup`）。別案は、文脈を「Contract と、多段の節の残りの語」の有限の状態とし、クラスの併合で合わせる | 完備化に入れる。重なりの扱いが別名・経路等式・スコープの規則と一つになり、文脈は Contract の集合のままである |
| Q12 | 関連型の指定の複数の同一性原子は、未決定の関連型を決めるだけで、入力どうしの同一性を仮定しない（本書 4.11、`Pack` は誤り）。別案は現行の「一つのクラス」に分解を加えて読み、`Pack<T>` の検証で `T ≡ i32` を仮定する | 採用する。別案はどの `T` にも `T ≡ i32` を仮定し、入力への隠れた仮定になる |
