# 完全型と静的 contract の統一

日付: 2026-09-24

改訂日: 2026-09-25

状態: 議論で採用した方針をまとめた仕様変更案。正式仕様への取り込み・実装検証は未実施。

## 1. 位置付け

本書が変更する事項は、[SPEC.md](../../SPEC.md) とその参照先に優先する。変更しない事項には既存仕様を適用する。他の draft の内容は取り込まない。

型モデルは次の式を維持する。

```text
Type = Semantics/Core during Origin
```

この式は一層分の説明である。型引数、要素、借用や生ポインターの直接の対象は完全型になり得る。Core と、Semantics が直接適用される対象を混同しない。

各共通規則は担当節で一度だけ定義し、個別機能はその適用として記述する。

| 共通規則 | 担当 |
| --- | --- |
| 完全型の構成と比較 | §2 |
| 型と Origin の束縛、新しい Origin と省略の意味 | §3.1–3.3 |
| Loan の保持・分割・移譲 | §3.4–3.5 |
| 適合、関連型、要求と実装の対応 | §4 |
| 要求の lookup、呼出し、receiver の取得 | §5 |
| Subject の取得とパターン | §6 |
| Iterator／Iterable と標準型 | §7 |
| 検証、生成・最適化、診断 | §9 |

## 2. 完全型

### 2.1. 型束縛の共通対象

総称型引数、関連型、`Self`、引数・戻り値・格納位置の型は、すべて完全型（Semantics と Origin 依存を含む型）を扱う。関連型の Core 限定と、Kimi の Element だけを完全型にする例外は廃止する。

完全型を束縛できることは、任意の役割で使えることを意味しない。型形成、Object Target、基底型、有限の配置、アクセス、格納、Copy／Owned などの条件は、それぞれ必要な位置で検証する。bare contract、Semantics 単体、一般の値引数は値の型にならない。長さ引数は従来どおり別の種類である。

Semantics の適用は合成であり、既存の層を置換・平坦化しない。

```text
ref/(uniq/T) ≠ uniq/T
ref/(obj/T)  ≠ objref/T
owner/W      = W        // 冗長な指定の正規化のみ。W の借用や object Semantics は残る
```

object 形式の形成条件と、生ポインターの Unsafe 条件は維持する。

### 2.2. 三つの判断

| 判断 | 比較する内容 | 用途 |
| --- | --- | --- |
| **型の同一性** | 宣言 Identity、すべての Semantics 層、型引数、長さ、入れ子の構造。Origin の束縛・関係は再帰的に除く | 適合の登録キー、衝突判定など明示された用途 |
| **完全な型記述の同値性** | 型の同一性に加え、Origin の束縛・量化・成立条件の対応 | 型の置換・代入、型同士の `T is U` |
| **使用上の適合性** | 既存の部分型・分散・寿命短縮と、取得・初期化・アクセス・Loan の条件 | その位置で値を使えるか |

型同士の `T is U` は、完全な型記述の同値性を要求する。代入可能性の検査でも、Origin を除いた比較でもない。右辺が contract や Semantics の `is` は、従来どおりの要件検査である。

例えば `ref/T during a` と `ref/T during b` は同じ型 Identity を持つが、`a` と `b` の等しさを証明できなければ同値ではない。`a outlives b` だけでは、許された方向へ寿命を短縮できるだけである。

型の同一性から、Origin の等しさ、Copy／Owned、代入可能性、使用権限を導かない。同じ型 Identity でも、Origin によって Owned や条件付き適合の結果は異なり得る。

### 2.3. 正規化と証明の範囲

比較の前に完全型を形成・検証し、別名、括弧、冗長な `owner/`、確定した関連型を正規化する。束縛名の綴りは Identity ではない。量化変数は位置とスコープで対応付け、固定された外側の Origin を新しい量化変数として扱わない。

Origin の同値性は、既存の等式・outlives・交差の正規化と、限定された証明規則で判断する。任意の論理的同値性は探索しない。期限までに必要な証明が得られなければエラーとし、Unknown を成功や不成立に置き換えない。

Runtime Object Type Identity と overload の Signature は、既存どおり目的別に定義する（receiver の順序は §5.1）。コード共有の条件は §9.2 で定める。

## 3. 型と Origin の束縛

### 3.1. 束縛する側

| 形式 | 完全型を決める側 |
| --- | --- |
| 総称型引数 `T` | 利用側 |
| 関連型 `Self.Element` | 適合を定義する側 |
| `Self` | その宣言が対象とする型 |

三つとも同じ束縛・代入規則を使う。`associate Element is U` は、Origin を含む `U` 全体を束縛する。実装の戻り値や本体から関連型を推測しない。

`<s/T>` は、一つの完全型を外側の Semantics と直接の対象に分解する既存の形式である。元の `s/T` は完全型全体を保持する。別の対象へ適用した `s/U` は新しい型構成であり、元の外側の Origin を引き継がない。各構成の成立条件は、許されるすべての Semantics 束縛について検証する。

暗黙の `semantics` 変数、単独の Semantics 総称引数、`associate s/Element` は導入しない。

### 3.2. 束縛の保持と新しい借用層

**既に束縛された型を参照しても、Origin を再束縛しない。** 総称引数、`Self`、関連型、別名、入れ子の型のすべてに適用する。代入後に省略規則を再実行して、新しい Origin や `static` を補わない。

新しい借用層を作る操作（借用、再借用、要求の `uniq/Self` など）だけが、その層に新しい Origin を与える。これは既存の束縛の変更ではなく、新しい値の型の構成である。

- `Self` に固定された source があっても、要求の `uniq/Self` はその外側に呼出しごとの借用を作る。source を短い借用で置き換えない。
- 借用値を再借用して receiver や Subject に渡す場合、渡す値の外側 Origin は再借用の Origin である。`Self` などは、その値の完全型で具体化する。

### 3.3. 新しい Origin と省略の意味

新しい借用 Origin（名前付き・省略）の意味は、導入位置で次のように決まる。

| 導入位置 | 意味と範囲 |
| --- | --- |
| 適合ヘッダーの対象型 | 普遍的に量化する。適合全体で、公開条件を満たすすべての束縛について検証する |
| 制約・`when` の主語（`is` の左辺） | 普遍的に量化する。範囲はその制約節の中だけ |
| 制約の型等式の右辺 | 左辺の対応する層の Origin で補完する（§3.3.2） |
| 関数・要求の入力 | 呼出しごと。既存の署名の量化規則に従う |
| 関連型の指定 | 外側の束縛、`static`、完全型の依存と明示的な関係から完成させる。独立した自由な Origin を追加しない |
| 既存の完全型に含まれる Origin | 元の束縛を保持する |

- 未束縛の単純名は、既存の入力借用注釈と同様に、適合ヘッダーと制約の主語でも新しい Origin として導入できる。型等式の右辺では導入できない。既に見える名前はその束縛を参照する。
- 内側の借用や未知のスキーマに、新しい省略許可を与えない。
- `origin` 関係節は名前を導入しない。`during` を持てる Semantics、集合名、射影、宣言スキーマの既存規則を維持する。Origin を持たない層に `static` を補わない。

#### 3.3.1. 完全型を主語とする制約

関数・型・contract の制約と `when` は、その位置で使える総称型・`Self` から Semantics を合成した型と、その関連型射影を主語にできる。これは適合の利用条件であり、外部適合の宣言ではない。

```kimi
func inspectThenConsume<C>(source: C)
    ref/C is Iterable      // すべての借用 Origin について ref/C が Iterable
    C is Iterable
    ...
```

- `ref/C during a is Iterable` の `a` は、既存の `a` がなければ制約節内の新しい普遍 Origin である。`ref/C is Iterable` は匿名の Origin について同じ意味になる。
- 新しい Origin は同じ制約節の右辺で参照できる。関数本体・署名・別の制約節へは持ち出せない。

  ```kimi
  (ref/C during a).Iterable.Cursor.Element is ref/i32 during a
  ```

- 使用時は実際の借用 Origin を代入して、必要な適合・等式を証明する。任意の Origin は探索しない。
- 一般の高階関数型や Origin 引数は追加しない。関数先頭の制約構文を拡張するだけで、括弧で囲んだ実行時の式との区別は維持する。

#### 3.3.2. 型等式の右辺での補完

制約としての型等式では、右辺に新しく書いた借用層の省略 Origin を、左辺の対応する層から補完する。補完の後で §2.2 の同値性を検査する。

```kimi
X.Cursor.Element is ref/i32   // 右辺の Origin は、左辺の Element の Origin と同じ
```

- 補完は、構造上の対応が一意な位置に限る。不透明な左辺では、必要な構造と同値性を制約として保持する。
- 寿命短縮、`static` の仮定、存在変数の探索は行わない。補完で本体から参照できる名前は導入しない。
- 右辺で明示した Origin 名は既存の束縛（同じ節の主語で導入した名前を含む）を参照する。未束縛の名前はエラーとする。
- `associate Element is U` の定義には適用しない（§3.1）。

### 3.4. 依存と Loan の保持

Origin は有効期間を、Loan は領域・権限・借用元を表す。Origin の等式や短縮で、Loan を統合・消去したり、排他権限を作ったりしない。

- 構造が不透明な関連型も、依存する Origin、Loan の要件、検証に必要な関係を保持する。情報がないことは、Owned や無依存の証明にならない。具体構造に依存する義務は、既存の期限まで保持する。
- 射影元の Origin を、射影結果へ無条件に追加しない。射影元の有効性の検証と、関連型が実際に持つ依存は別である。結果型の型引数・外側環境に含まれる依存は、未使用でも保持する。
- 元の型を寿命短縮できても、その関連型も短縮できるとは推論しない。結果型について分散と使用上の適合性を検証する。

### 3.5. Loan の分割と移譲

Loan は、記憶域を有効に保つ**保護元**と、その借用でアクセスできる**領域**を区別する。分割しても、元の記憶域の再確保・移動・破棄に対する保護は失わない。保護元が同じだけで、分割済みの領域を重複とは扱わない。

安全な分割・移譲は、次を保証する。

1. 同時に使える排他領域は重複しない。元の権限を複製・強化しない。結果を共有参照にしても、元の排他保護は弱めない。
2. 結果と残部は、必要な保護元・Origin・親 Loan を保持する。一方の終了で他方の保護を失わない。
3. 短い receiver 借用に依存しない結果も、非重複性と更新後の状態を検証する。Origin を長く書くだけでは権限を分離できない。
4. 総称要求、関数参照、別コンパイル、ユーザー型への委譲でも同じ保証を保つ。private な本体の再解析や、型名による検査免除に頼らない。

排他権限を保持する状態は Non-Copy であり、Move は権限も移す。共有参照の Copy は保護元を保持する。以後の操作・cleanup は、残る権限だけを使う。分割を使わない借用と Slice の競合規則は変更しない。検証モデルは §9.4 に置く。

## 4. 完全型への静的適合

### 4.1. 宣言の形式

struct／enum の中で、次の適合を宣言できる。

```text
対象型 is 束縛済みContract [when 条件]
    [Origin 関係]
    [関連型の指定・要求実装]
```

- 対象型は、宣言中の型自身か、それを直接の対象として Semantics を合成した完全型に限る。囲む総称パラメータは使えるが、新しい型・Semantics のパターン変数は導入しない。別の名目型、総称パラメータ単体、他の型の構成への外部適合は宣言できない。
- 型形成は、囲む型と適合条件が許すすべての束縛について成立しなければならない。object 形式には Object Target の証明が要る。
- `when` は既存の正の要件条件を使う。
- ブロック先頭の Origin 関係は、この適合の公開条件である。型自体の成立条件には加えない。関連型の指定に付けた関係は、その型の束縛を完成させるものであり、隠れた適合条件を加えない。
- Origin の条件は、適合を使えるかの証明に使う。Origin の違いだけで別の実装を選ばない。

### 4.2. `Self` と実装スコープ

- ヘッダーの `Self` は、`when` の中も含めて囲む型を表す。
- ブロックは新しい `Self` を導入する。先頭の Origin 関係を含むブロック全体で、`Self` は適合対象の完全型を表す。要求側の `Self` にも同じ完全型を代入する。

```kimi
struct Array<E>
    ref/Self during source is Iterable
        // ここでの Self は ref/Array<E> during source。
        associate Cursor is SharedArrayIterator<E>{it}
            origin it.source == source

        func iterate(self: Self) -> Self.Cursor
            ...
```

`SharedArrayIterator` は説明用の名前であり、公開名や構築子を追加しない。

| ブロックに置けるもの | 置けないもの |
| --- | --- |
| 関連型の指定、要求を実装する関数、型の種類が許す要求の computed Property／accessor | 格納、Case、構築子、`deinit`、入れ子の型・適合、要求と無関係な補助メンバー（補助関数は通常の宣言として置く） |

- 要求実装は通常の関数・accessor と同じ規則で検証するが、囲む型の通常メンバーグループには入らない。このため、異なる適合の要求実装どうしは receiver shape の制約を受けず、共有・排他・消費の実装名を分ける必要はない。通常メソッドグループの receiver shape 制約は維持する。
- receiver 省略形 `self` は常に `self: ref/Self` である。`Self` が参照型なら参照が一層増える。これを旧来の Core 前提だけで拒否しない。

### 4.3. 要求の識別と関連型

#### 4.3.1. 要求参照

要求（関数、Property、関連型）を識別する単位は、**定義元の宣言 Identity と、定義元 contract の束縛済み環境**である。accessor ではさらに `get`／`set` を区別する。環境は、外側の型引数、Semantics、Origin の束縛と条件を含む。

同じ宣言への複数の経路は、環境の完全な同値性と §4.4.2 の経路整合性を検証してから一つにまとめる。例えば `Family<i32>.Source` と `Family<string>.Source` の Element は、別の要求参照である。Origin を除いたキーの一致だけではまとめない。

#### 4.3.2. 関連型の指定

- 実装側の関連型指定は、その適合ブロック内の `associate Element is U` だけとする。型本体に置く従来の `associate C.Element is U` と、型本体の等式による代用は廃止する。
- 要求の制約・祖先の指定から一意に決まる場合は再指定しない。指定が必要ならブロックを書く。
- contract 内の関連型宣言と、利用側の関連型制約は維持する。関連型に独自の総称パラメータや Origin スキーマは追加しない。

#### 4.3.3. 射影と修飾名

- `T.C.Element` は完全な射影である。`T.Element` は、利用可能な要求参照が一意なときの短縮形である。二つの Source に適合する T では `T.Element` は曖昧であり、結果型が偶然同じでもまとめない。
- `T.N` の N が、contract 名としても、T の通常メンバー・関連型の名前としても解決できる場合は曖昧性エラーとする。判断は名前の解決で行い、適合の証明結果や期待型で選び直さない。`T.(C)` は常に contract を選ぶ。要求の関数・Property 名にも同じ規則を使う。
- Semantics を合成した型全体を射影元にするときは括弧を使う。既存の prefix／suffix の構文を維持し、名前解決で都合のよい方へ再解釈しない。

```kimi
(ref/Array<E> during source).Iterable.Cursor   // 借用型の適合の Cursor
ref/(Array<E>.Iterable.Cursor)                  // 別の型: 所有配列の適合の Cursor への参照
```

各 qualifier の形成・アクセス・条件を検証する。射影元になっても、Core 専用の役割は得ない。例えば借用型の関連型に `.init` を付けても、借用値の構築子は生成されない。

### 4.4. 一意性と経路の整合性

#### 4.4.1. 登録キーと重複

- 適合の登録キーは、対象の型 Identity と、contract 宣言とその束縛済み環境の Identity の組である。Semantics と型引数は保持し、Origin は除く。完全な Origin 束縛と条件は証明情報として別に保持し、実装の選択には使わない。
- 直接適合は、断片の併合後、同じキーに一致し得る宣言を重複として拒否する。Origin の違い、`when` の違い、条件の強弱・排他性では重複を許可しない。限定された構造照合で非衝突を証明できなければ、衝突し得るものとする。
- 言語による個別の導出（§7.1 など）は、前提の適合と同じ対象・同じ `when` を持つ直接適合として登録されたものとみなし、上の規則をそのまま適用する。導出に優先順位はない。intrinsic の既存宣言が導出の条件を検証させるだけの場合、それ自体は別の実装登録ではない。

#### 4.4.2. 経路の整合性

直接適合、contract の refinement、基底型からの継承が同じ適合に到達する場合、同時に成立し得る経路の関連型と実装対応は一致しなければならない。関連型の一致には、完全な型記述の同値性を要求する。

適合は定義時に、囲む型と公開条件が許すすべての束縛について検証する。選ばれた実装対応は保持し、具体化や呼出し側の名前解決で選び直さない。宣言中・循環中の適合だけを根拠に、自分自身の成立を証明しない。

総称環境でも同じ整合性を使う。例えば `I is Iterator` と `I is Iterable` が両方与えられたとき、I の Iterable 適合は §7.1 の導出以外にあり得ないため、`I.Iterable.Cursor` を `I` に正規化する。

#### 4.4.3. refinement

`contract C: A, B` は、A と B の要求・制約を取り込み、それぞれへの適合を要求する。

- 親の順序に優先順位はない。循環は拒否する。
- 同じ要求参照は一つにまとめる。独立した要求は、同名でも別々の実装対応を持てる。
- 各 contract が直接宣言する要求には、既存の Signature と receiver shape の規則を適用する。継承した独立の要求どうしは一つの通常メンバーグループにしないため、同居できないという理由だけで refinement を拒否しない。同一要求の矛盾、両立しない型等式・公開条件は拒否する。
- 親適合が明示されていれば、親側で検証した関連型と実装対応を再利用し、子は不足分だけを補う。明示されていなければ、子の指定から親への対応も構成し、親の有効アクセス範囲を含めて検証する。複数の子から同じ親に至る場合も §4.4.2 を適用し、宣言・検証の順序で選ばない。

```kimi
contract Left
    func put(self, left: i32)

contract Right
    func put(self, right: i32)

contract Both: Left, Right

struct Sink
    Self is Left
        func put(self, left: i32) => ()
    Self is Right
        func put(self, right: i32) => ()
    Self is Both                  // Left／Right の対応を再利用する

Left.put(sink@ref, left: 1)       // 定義元の contract で選ぶ
// Both.put(sink@ref, 1)          // エラー: 二つの put が候補になり曖昧
```

#### 4.4.4. 基底型からの継承

既存の完全な要求照合と、許可された receiver 投影に従う。Semantics の自動変更、所有値の slicing、関連型の再選択は追加しない。

### 4.5. 実装の対応と公開範囲

**照合の手順**

1. 要件を識別する名前、関数の種類、総称スロット、引数の順序・ラベル、receiver と引数の正規化した型構造は、既存の実装照合規則で照合する。Origin は識別後の互換性検証で扱う。
2. §4.4 で再利用する対応は照合し直さない。
3. 残る要求について、ブロックに同名の実装グループがあればそこで確定する。失敗しても通常メンバーへは戻らない。グループがなければ、既存の通常メンバー照合か明示された intrinsic 導出を使う。
4. 各ローカル実装は、少なくとも一つの未対応要求を満たさなければならない。既存の対応を置き換えるためには使えない。

ブロックなしの適合宣言も引き続き使える。

**互換性.** 実装は、要求が許すすべての入力を受け入れ、要求以上の結果保証を与える。強い Origin 前提、追加の Constraints、Unsafe な呼出し条件などを隠れて要求しない。Property の保証と既存の witness bridge は維持する。

**公開範囲**

- 適合の公開範囲は、対象型、contract、その束縛と条件の有効アクセス範囲で決まる。
- ローカル実装は独立したアクセス修飾子を持たず、親適合を含む各対応先で必要な範囲に従う。子の狭い範囲で親の要求を弱めない。
- 公開適合から呼べることは、通常メソッドとして公開されることを意味しない。
- 実装から private な補助関数を呼ぶ場合は、通常の宣言位置のアクセス規則を使う。外側の通常メンバーを対応付ける場合のアクセス検証も省略しない。

## 5. 要求の選択と呼出し

### 5.1. 明示形と receiver 先頭の規則

`C` は束縛済み contract、`T` は完全型とする。修飾と曖昧性は §4.3.3 に従う。

| 要求 | Self を receiver から決める | Self を T に固定する |
| --- | --- | --- |
| receiver を持つ関数 | `C.f(receiver, ...)` | `T.C.f(receiver, ...)` |
| Type 関数 | 使用不可 | `T.C.f(...)` |
| Property の get | `C.p.get(receiver)` | `T.C.p.get(receiver)` |
| Property の set | `C.p.set(receiver, value)` | `T.C.p.set(receiver, value)` |

#### 5.1.1. receiver 先頭の規則

receiver を明示するすべての呼出し・関数参照（上表の形式と通常の `Type.method`）で、receiver を先頭の無名位置に置く。

- 残りの引数は、`self` を除いた仮引数列の順序、ラベル、`!` 境界で照合する。receiver は名前省略の境界に数えず、`self:` ラベルは設けない。
- 関数の Signature、Function Item と関数値の型も receiver 先頭に正規化する。`self` の宣言位置だけが異なる overload は区別しない。
- 呼出し規約の引数順序もこの正規化した順序とし、関数参照に並べ替えの adapter を要しない。宣言内の束縛・cleanup の順序は宣言どおりとする。
- receiver を持たない関数の順序は変えない。bound-method 値は追加しない。
- accessor の明示形は表の固定位置引数だけを取り（ラベル・default・`!` なし）、呼出し専用とする。

```kimi
contract Apply
    func apply(! value: i32, self: ref/Self)

Apply.apply(target@ref, value: 1)     // self は宣言では末尾でも、先頭に置く
Iterable.iterate(values@ref)
Iterator.next(iterator@uniq)          // iterator は所有する var 局所値
T.C.create()
```

#### 5.1.2. Self の推論と評価

- 要求の receiver 型と先頭の実引数から Self を一意に決め、その適合を検証する。結果の期待型だけからは推論せず、適合の有無で曖昧な解を順位付けしない。
- 適合を探すための変換探索は追加しない。例えば `Iterable.iterate(values)` の Self を、適合を成立させるために `ref/Array<E>` へ変えない。Non-Copy の所有配列なら `@move`、共有列挙なら `@ref` と書く。
- C に独立した同名要求が残る場合は、§5.2 の候補統合と曖昧性の規則を使う。receiver は overload の優劣比較に使わない。
- 明示形の全実引数は通常の引数位置として、記述順に一度ずつ評価・取得する。receiver も Receiver Expression ではない。所有する Place の新しい排他借用には `@uniq`／`@objuniq` が必要であり、既存の借用値は通常どおり再借用できる。
- `T.C.f` は Self を固定した要求参照であり、要求の型・Origin・効果・実装対応を保持する。関数値としては全引数を位置で指定する。実装側の default や名前省略許可は伝播しない。

### 5.2. 層ごとの名前 lookup

値を receiver とする関数呼出しと Property の get／set は、完全型 R から次の順で名前を探す。

1. 現在の層の通常メンバーを探す。アクセス可能で役割が合うグループか Property があれば確定する。
2. なければ、その層の型に対する公開適合・総称制約から同名の要求を集め、§4.3.1 に従って重複経路をまとめる。要求名があればその層で確定する。適合条件・Origin の証明は、確定した候補の義務として残す。
3. どちらもなく、現在の層が安全な `ref/U`／`uniq/U` なら U で繰り返す。それ以外は名前がないエラーとする。

**確定の規則**

- 名前が見つかった層が要求の Self を決める。同じ層では通常メンバーを優先し、同じ実装を二重の候補にしない。
- 確定後の失敗（適合条件、overload、receiver 取得、Loan）で、別の層や適合へ戻らない。
- 総称本体では宣言された証明環境で lookup を確定し、具体化後にやり直さない。
- 借用を辿るときは既存の receiver 取得表に従い、経路の権限を超えない再借用・Copy read を使う。共有経路から排他権限を回復しない。object と生ポインターの lookup は既存規則を維持する。
- 型を経由する参照、Type 関数、明示した `T.C` は T 自身で lookup し、借用層を辿らない。`for` は §7.3 のとおり完全型の適合を使う。

**候補の選択**

- 要求の関数は、候補の receiver shape が一つであることを確認してから、既存の overload 選択を行う。
- Property は get／set の契約全体で一つを選び、その後で必要な accessor を検証する。setter の有無や期待型で選び直さず、別の要求の getter と setter を組み合わせない。
- 独立した同名要求を一候補にまとめられるのは、公開する操作契約と実装対応の同値性を、許されるすべての束縛について証明できる場合だけである（Property は get／set の組全体で比較する）。それ以外は §5.1 の明示形で選ぶ。

```kimi
func advance<I>(it: uniq/I) -> Option<I.Element>
    I is Iterator

    return it.next()    // uniq/I の層に next はないので、I の層の要求を使い、参照先を排他再借用する
```

`uniq/I` 自身に next の要求があればその層で確定する。その receiver が `uniq/Self` なら、it の参照スロットへの排他借用（it の変更権限）が必要になる。

### 5.3. receiver の取得と Property の動作

名前の選択と receiver の取得は分ける。§5.2 で確定した層の完全型を Self に代入し、次の取得を計画する。新しい Semantics の適合を探すための借用は行わない。

| 要求の receiver | 取得 |
| --- | --- |
| `Self` | 完全型の値を渡す。一時値はそのまま、所有する Place は Copy か明示 Move、借用値は Copy か再借用（§3.2）。明示 Move は参照自体を移し、参照先の Copy 読みに置き換えない |
| `ref/Self` | Self を保持する位置への共有借用 |
| `uniq/Self` | Self を保持する位置への排他借用。位置の変更権限が必要 |

- Self が参照型でも層を省略しない。明示形で参照スロットそのものを借りるには、`@uniq/Self` に相当する完全な対象型を指定する。参照先を再借用する `@uniq` とは異なる。
- object receiver の形成・取得条件は維持する。
- Property 要求は accessor の呼出しであり、格納場所を公開しない。`value.p`、`value.p = rhs`、明示 accessor は、要求の型・権限・Loan と getter 結果の一時値制限を保持する。直接の格納操作へ lower しても、隠れた Field の Move／排他借用を許可しない。get／set の receiver 省略形と witness bridge は既存規則を使う。
- 候補の検討中には値を取得しない。確定した計画を、通常の予約・活性化・cleanup 規則で実行する。メソッド形式は receiver を先に、明示形は記述順に評価する。Property 代入は既存の右辺先行を保つため、`C.p.set(receiver, value)` と評価順序が同じとは限らない。

## 6. Subject の取得とパターン

### 6.1. `for` と `match` の共通取得

Subject の式は一度だけ評価し、パターンや適合の結果によらず次の表で取得する。上の行を優先する。

| 式 | 取得 |
| --- | --- |
| 明示的な `E@move` | 値を移動して取得する。借用値なら参照自体を移す |
| 所有する Place（Copy を含む） | その格納完全型への共有借用 |
| 共有借用値 | Semantics を保持した Copy／共有再借用 |
| 排他借用値 | Semantics を保持した排他再借用。親は移動せず、子の Loan が必要な間だけ競合する使用を禁止する |
| 借用以外の一時値 | 値として取得する |

- 括弧、局所変数への保存、引数・戻り値の経由で、借用の Semantics を暗黙に弱めない。弱めるなら `access@ref`、参照自体を移すなら `access@move` と書く。
- 格納済みの `uniq` 値や `uniq` 引数を Subject にした場合は、従来の共有再借用から排他再借用に変わる。派生 Loan は arm 全体へ延ばさず、既存どおり実際の使用と観測可能な cleanup に従う。
- Semantics の保持は `ref`／`uniq` と `objref`／`objuniq` に共通である。`@uniq`／`@objuniq` と書いたことだけを理由とする Subject エラーを廃止する。ただし、object 形式の Iterable 適合や object payload の暗黙の分解は追加しない。
- 取得した型に必要な適合がなければエラーとし、別の取得モードで再試行しない。

```kimi
for item in values          // 所有する Place を共有借用し、ref 適合を使う
    inspect(item)

for item in values@uniq     // 排他列挙: uniq 適合を使う
    update(item)

let access = values@uniq
for item in access          // 同じく排他列挙。access は移動せず、ループ後も使える
    update(item)
```

### 6.2. 分解後の取得

構造パターンは、各位置で `ref/U` か `uniq/U` を最大一層だけ辿れる。object Semantics と生ポインターは辿らない。Binding／Wildcard 自体は参照を辿らず、括弧は層を取り除かない。

- 子の権限は、通過した経路の権限を超えない。共有経路の内側で `uniq` を見つけても、排他権限は回復しない。
- 同じ位置で `ref/ref/U` を繰り返し辿ってパターンに合わせない。§5.2 の lookup は名前を探すために層を繰り返し辿るが、パターンは各位置の型構造を一層ずつ照合する。

位置の格納完全型を U とすると、body の束縛は次のように取得する。

| 経路 | 取得 |
| --- | --- |
| 値として取得した Subject、またはその借用を通らない部分 | 通常の Copy／Move |
| 共有の経路 | その位置への `ref/U`（Copy 型でも） |
| 排他の経路 | その位置への `uniq/U`（Copy 型でも） |
| Wildcard | 取得しない。既存の破棄責任を維持する |

- 所有する Place の Subject は共有借用なので、その束縛は共有の経路の行に当たる。
- `let`／`var` は束縛の再代入可否だけを決め、参照先の権限を増やさない。U が参照型なら、その参照スロットへの借用になる。
- 共有パターンの Copy 要素は、従来の値取得から `ref/U` に変わる。値が必要な位置では既存の Copy read を使う。束縛の型と総称推論の結果は参照型のままである。

```kimi
match slot                    // slot: Option<i32>、所有する var
    .Some(let v) =>           // v: ref/i32
        let n: i32 = v        // 値の位置で Copy read
        slot = .None          // v の最後の使用の後なので有効
        record(n)
    .None => ()
```

**guard**

- candidate は位置を `ref/U` として読み、値が必要な位置で Copy read を使う。
- candidate と body の束縛は別の Identity である。body の取得は、成功した guard の cleanup の後に行う。失敗時は候補を保持する。
- candidate を読むための新しい Loan は guard の外へ持ち出せない。格納済み外部参照の Copy は元の依存を保持する。その他の guard 制限、Case の変更・全体更新に対する Loan 検査は維持する。

`for` の単一束縛は、next の Some payload を値として取得する。タプル形式は、返された要素の型にこの節の分解規則を適用する。一般の match パターンは `for` に追加しない。

### 6.3. 書込みと寿命

- `let item: uniq/U` は参照の付け替えを禁じるだけで、参照先への排他アクセスは残る。`=` と複合代入の意味は変えない。参照先の更新には、既存のメソッドや `Intrinsics.replace`／`exchange`／`swap` を使う。
- 借用した部分を未初期化にする Move は、引き続き禁止する。借用値の `@move` は参照自体の移動であり、参照先の所有権の移動ではない。
- 借用した Subject から得た結果は、隠れた局所スロットではなく、元の referent と Loan に依存する。所有する Subject の内部への新しい借用は、その Subject の破棄を越えて逃がせない。格納済みの外部参照の Copy／Move は元の依存を保持する。

## 7. Iterator／Iterable

### 7.1. 要求の定義

```kimi
public contract Iterator
    associate Element

    func next(self: uniq/Self) -> Option<Self.Element>

public contract Iterable
    associate Cursor is Iterator

    func iterate(self: Self) -> Self.Cursor
```

- Iterable の関連型は、`Iterator` から `Cursor` に改名する。Iterator 型は下記の導出で Iterable にもなるため、旧名では `I.Iterator` が contract 名と関連型名の両方に解決し、§4.3.3 の曖昧性エラーになる。
- Iterable の Element と旧等式を削除する。要素型は `T.Cursor.Element` で得る。`T.Element` の別名は追加しない。
- `iterate` は適合対象の完全型の値を受け取る。`next` は iterator の状態を更新するため、その完全型を排他借用する。列挙対象、iterator、要素の Semantics は互いに独立である。
- Element は next の呼出しより外側で束縛され、呼出しごとの receiver Origin を参照できない。このため既存の外部 source への借用は返せるが、next の一時借用や iterator 所有の記憶域への借用は返せない。lending iterator は導入しない。

**導出.** 言語による個別の導出として、`I is Iterator` から `I is Iterable` を導く。`I.Iterable.Cursor` は `I` であり、iterate は受け取った I をそのまま返す。これにより `for x in it@move` で iterator を直接消費できる。重複の扱いは §4.4.1 に従う。

`uniq/I is Iterator` は導出しない。導出すると外側の next が §5.2 の lookup を遮り、`it: uniq/I` の `it.next()` に参照スロットの変更権限を要求してしまう。iterator を借用して途中まで列挙するには、§7.5.3 のように参照を保持する型へ委譲する。関連型 Cursor に借用型を指定する場合も、その完全型自身の Iterator 適合が必要である。

### 7.2. 標準型の適合

組み込みの列挙モード選択表と、Copy view 読みの代替経路を廃止し、次を通常の適合として要求する。`E`、`K`、`V` は完全型、`a` は列挙対象の借用 Origin、Slice の `source` は背後の記憶域の Origin である。

| 型 | 所有する値の Element | `ref` 適合の Element | `uniq` 適合の Element |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `E` | `ref/E during a` | `uniq/E during a` |
| `Dictionary<K,V>` | `(K,V)` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` |
| `Slice<E>` | `ref/E during source` | 同左 | 同左 |
| `ResolvedRange` | `isize` | `isize` | `isize` |

- 順序は、Array／固定配列が添字順、Dictionary が挿入順、ResolvedRange が既存の区間順である。
- Range は Iterable ではない。object 形式と生ポインターへの標準の Iterable 適合は追加しない。
- 借用する配列 iterator の source は `a` であり、Element もそれを保持する。所有する配列 iterator は残る要素を所有する。
- Slice／ResolvedRange の iterator は、ハンドル・区間の内容を取得して使う。ハンドルを借用した局所 Origin は、不要なら結果へ保持しない。Slice の実際の source は必ず保持する。
- Dictionary のキーは、排他列挙でも共有参照である。`uniq/Slice<E>` はハンドルへの排他アクセスであり、共有 view の要素を排他にはしない。
- 列挙モードは collection 自身の外側の Semantics で決まり、要素の Semantics とは独立である。例えば `E = ref/Dog during b` のとき、Element は次のとおりである。

```text
消費: ref/Dog during b
共有: ref/(ref/Dog during b) during a
排他: uniq/(ref/Dog during b) during a    // Dog への排他アクセスは与えない
```

iterator の具体的な公開名や構築子は要求しない。利用者は関連型の射影を使う。

#### 7.2.1. 標準借用 iterator の性能

上表の ref／uniq 適合と Slice の iterator に、次を要求する。型を固定し、n は開始時の要素数（Dictionary では capacity ではなく生きた entry 数）とする。

| 操作・資源 | 保証 |
| --- | --- |
| iterator の生成・破棄 | O(1) |
| next | 償却 O(1)。空・終了後は O(1) |
| 最初の None までの全走査 | O(1 + n) |
| iterator 自身の追加記憶域 | O(1) |
| 列挙のためのヒープ割当て・参照カウント更新 | なし |

- 要素参照の配列を事前に作らない。Dictionary は、開始時に capacity 全体を走査しない。
- 利用者が保持する要素結果と body の処理は別に数える。所有する iterator の要素移動・破棄は、既存の計算量と責任を維持する。
- この保証は一般のユーザー定義 Iterator には要求しない。
- Dictionary の remove の割当て禁止と記憶域再利用の保証は維持する。非規範の実現例: 既存の O(capacity) の管理領域に、生きた entry の挿入順リンクと空き位置のリストを持つ。削除時にリンクから外し、再利用時に末尾へつなぐ。これで墓石を走査せずに両方の保証を満たせる。

### 7.3. `for` の処理

1. §6.1 で式を一度取得し、隠れた iterable 局所値に置く。
2. その完全型の Iterable 適合を選び、局所値を `@move` 相当で渡して iterate を一度呼ぶ。借用値なら参照と権限を移し、参照先は消費しない。
3. 戻り値を、更新可能な隠れた iterator 局所値に置く。
4. iterator の完全型自身の Iterator 適合を使い、その格納スロットへの短い排他借用で next を呼ぶ（§5.3）。
5. Some の要素を取得して body を実行する。最初の None で終了する。
6. 各反復とループ終了の cleanup は、通常のスコープ終了規則に従う。

- 同名メソッドだけを探す duck typing は行わない。
- 一般の Iterator に「一度 None を返したら以後も None」という法則は課さない。Kimi の標準 iterator は終了後も None を返す。
- break、return、try 伝播などの通常の制御移動では、未取得の所有要素を既存の順序で破棄する。借用 iterator は元の要素を破棄しない。Abort では従来どおり巻戻しを保証しない。

### 7.4. 要素の排他借用と分離

標準の Array／固定配列／Dictionary の uniq 適合は、§3.5 の保証と §9.4 の検証モデルに従い、次の手順で分割・移譲する。

1. iterate が受け取った排他権限を iterator の状態へ移す。状態は順序と未取得の領域を持つ。
2. next は、未取得の先頭要素を検証済み操作で分離し、残部を状態へ、分離した権限を Some の要素へ渡す。参照を公開する前に、cursor と権限の移譲を確定する。
3. 要素がなければ None を返す。渡した領域を再取得せず、cursor の巻戻しで権限を復元しない。

- next の短い receiver 借用は iterator の状態を保護し、返す要素は元の外部 source に依存する。このため、前の結果を保持したまま次の next を呼べる。
- Dictionary は entry のキーと値を分離し、キーには共有、値には排他のアクセスだけを渡す。
- iterator の破棄は残る権限だけを終了し、要素や collection を破棄しない。保持された結果は source の保護を保つため、collection 経由の競合する読み書き、構造変更、再確保、移動、破棄は拒否される。cleanup の検証にも同じ規則を適用する。
- 一般の安全な動的添字分割や、排他 Slice の公開 API は追加しない。ユーザー型は、標準の操作への委譲で排他列挙を提供できる。独自実装にも同じ Loan の契約を要求し、Unsafe や型名を根拠に省略しない。

### 7.5. 例

#### 7.5.1. collection への委譲

具体的な iterator 名や、Origin スロット付きの関連型は要らない。

```kimi
public struct Bag<E>
    var items: Array<E>

    public init(items: Array<E>)
        self.items = items@move

    ref/Self during source is Iterable
        associate Cursor is (ref/Array<E> during source).Iterable.Cursor

        func iterate(self: Self) -> Self.Cursor
            return Iterable.iterate(self.items@ref)
```

#### 7.5.2. 値の型と借用した型への制約

```kimi
func count<X>(source: X) -> isize
    X is Iterable

    var result: isize = 0
    for _ in source@move
        result += 1
    return result

func inspectThenConsume<C>(source: C)
    ref/C is Iterable
    C is Iterable

    for _ in source@ref/C       // C の値全体を借り、その Origin で制約を具体化する
        ()
    for _ in source@move        // 借用の終了後に C 自体を消費する
        ()
```

- `count(values@ref)` と `count(values@uniq)` は借用値を、`count(values@move)` は所有する配列を渡す。`source@move` は X の値を移すだけであり、X が借用型なら元の collection は消費しない。
- `@ref/C` は、C が借用型でも格納値全体を借りる。`@ref` と書くと参照先の再借用になり得るため、総称本体では層を明示する。

排他要素の更新は既存の操作で書ける。

```kimi
for item in values@uniq          // values: Array<i32>、item: uniq/i32
    Intrinsics.replace(item, with: 0)
```

#### 7.5.3. iterator を借用して列挙する

説明用のユーザー型であり、標準 API ではない。

```kimi
struct BorrowingIterator<I> {source}
    I is Iterator
    let inner: uniq/I during source

    init(inner: uniq/I during source)
        self.inner = inner@move

    Self is Iterator
        associate Element is I.Iterator.Element

        func next(self: uniq/Self) -> Option<Self.Element>
            return I.Iterator.next(self.inner)

func consumeOne<I>(it: uniq/I)
    I is Iterator

    for _ in BorrowingIterator<I>.init(it)    // it を再借用する。ループ後も it は使える
        break
```

§7.1 の導出で、このラッパーも Iterable になる。反復は元の iterator を進めるが、所有権は移さない。ヒープ割当ては不要で、転送呼出しは通常の最適化の対象である。

#### 7.5.4. Copy 型の共有列挙

`for x in value` で Copy 型を列挙させるには、`ref/Self` の適合を宣言し、値をコピーして所有型の適合へ委譲する。

```kimi
struct Span
    Self is Copy
    let range: ResolvedRange

    init(range: ResolvedRange)
        self.range = range

    Self is Iterable
        associate Cursor is ResolvedRange.Iterable.Cursor

        func iterate(self: Self) -> Self.Cursor
            return ResolvedRange.Iterable.iterate(self.range)

    ref/Self is Iterable
        associate Cursor is Span.Iterable.Cursor

        func iterate(self: Self) -> Self.Cursor
            let value: Span = self               // Copy read
            return Span.Iterable.iterate(value@move)
```

`ref/Self` の適合がなく、所有型だけが Iterable の型は、Copy 型でも `value@move` か所有する一時値で列挙する。取得モードを切り替えて適合を探すことはしない。

## 8. 既存機能との境界

- `T is C` から `ref/T is C`、`uniq/T is C` を一般には導出しない。標準型の明示的な適合と、言語が個別に定める導出だけを使う。
- Copy／Owned／Callable／Sealed／ObjectPayload の固有の条件と効果は維持する。適合の構文で、intrinsic が許さない実装を登録することはできない。
- Equatable／Comparable の借用・タプル合成は、既存の対象と条件を持つ個別の導出として維持する。重複の扱いは §4.4.1 に従う。
- 演算子が operand を借用して操作することと、その借用型が contract に適合することは別である。比較の operand 取得、浮動小数点の意味、built-in の優先順位は変えない。
- 次は導入せず、その設計を暗黙に確定しない: runtime contract View、object erasure、任意の外部適合、適合の優先順位、default 実装、関連型の独自の型／Origin 引数、lending iterator、参照を通した新しい代入規則。

## 9. 検証要件

### 9.1. 定義と保存

- 総称本体は宣言された証明環境で検証し、具体化で欠けた前提を補わない。具体型・総称引数・関連型のどれを経由しても、同じ完全型は同じ Semantics と Origin の保証を持つ。
- 適合の登録キーと完全な証明情報を分けて保持する。Origin を除いたキーでは、候補と実装対応のテンプレートだけを共有する。Origin 条件、Owned、完全な関連型、使用計画は、実際の証明環境で検証する。候補情報を適合成立の証明として再利用しない。検証結果は、必要な完全な環境が一致すればキャッシュしてよい。特定のキャッシュ構造は要求しない。
- 要求の束縛済み環境、実装対応、Origin の量化・条件、Loan の分割・移譲、アクセスと効果を、保存・再読込み・無効化でも保持する。別コンパイル先の private な本体を調べずに、公開契約と検証済み情報だけで使用を判定できなければならない。

### 9.2. 生成と最適化

- 静的適合の選択と Origin に、実行時の型タグ、寿命タグ、割当てを必須としない。
- Origin だけが異なる具体化は、選んだ実装、検証済みの操作、配置・ABI などの生成条件が一致すればコードを共有できる。
- 最適化は、取得順序、Loan、破棄責任、診断すべき違反を変えてはならない。標準コレクションの既存の計算量・割当て保証を維持する。

上の条件と、型・寿命・aliasing・評価・cleanup の保証を保てる場合、次の最適化を許す。

| 対象 | 最適化 | 追加の条件 |
| --- | --- | --- |
| `ref/(ref/T)` | 内側の参照値を直接渡す | 外側スロットのアドレスが観測されない。ABI の合意か adapter が必要。外側スロットの非 alias 保証を参照先へ転用しない |
| Copy の Subject、共有パターンの Copy 要素 | 値で処理する | 束縛の型、Loan、更新先を変えない。排他借用先をコピーで置き換えない |
| `for` の隠れた iterable 局所値 | iterate への移動と結果の配置を同じ記憶域で行う | 観測可能な差がない（固定配列の消費列挙で要素を二度移さない） |

### 9.3. 診断

- 型形成の失敗、同値性の未証明、適合の欠如・衝突、関連型の不足・曖昧性、実装の非互換、使用時の Loan 違反を区別する。使用違反を「別の適合が必要」と誤って扱わない。
- 取得した型に適合がない `for` では、成立する書き方がある場合に限り提示する。例えば、所有する iterator の Place には `it@move` を、途中まで列挙したい場合は借用する型への委譲を示す。
- 未使用の名前付き Origin に対する lint は任意とする。`Self`、関連型、条件を通した暗黙の使用も数える。lint の有無は適合の成立を変えない。

### 9.4. Loan 分割の検証モデル

分割には、既存の構造的な非重複の証明か、実装まで検証された記憶域操作が必要である。`splitLoan(L, A)` はその検証モデルであり、特定の内部命令や公開 API を要求しない。L のアクセス領域を R とする。

| 段階 | 検証・保証 |
| --- | --- |
| 前提 | L の有効な排他権限。A は分割可能な R の部分領域で、残部と重複しない。範囲・初期化・配置・provenance も検証する |
| 移譲 | R 全体への権限を消費し、A と R ∖ A に別々の権限を渡す。消費した L を再使用しない |
| 保持 | 両方に、元の保護元・Origin・必要な親 Loan を保持する。元の権限・有効期間を超えない |
| 終了 | 一方の終了で他方の保護を消さない。元への競合するアクセスは、必要な派生 Loan の終了まで拒否する |

- 全部の移譲は A = R とする。空の領域やサイズゼロの要素も論理的な位置で区別し、アドレスだけで重複を判断しない。分割自体は値の Move や未初期化化を伴わない。
- 標準の排他列挙には、残部の先頭要素を分離して安全な参照を返す検証済み操作を用意する。この操作は境界・位置・provenance を検証し、cursor と権限の更新を確定してから参照を返す。結果の Origin は元の source に接続する。
- 動的添字の比較、Origin 注釈、Unsafe 指定だけでは証明にならない。未定義の生ポインター変換 API も前提にしない。
- 呼出しの検証情報は、入力から消費する権限と、結果・更新後の状態へ渡す領域・保護元を保持する。通常の receiver 保護も維持する。
- 同じ保証を証明できる別のモデルを使ってよい。Loan の実行時タグ、要素ごとの割当て、一般の整数定理証明は要求しない。

### 9.5. 成立例と拒否例

#### 9.5.1. 型と束縛

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 型比較 | 同じ構造の借用は、Origin が異なっても同じ型 Identity | Origin の同値性なしで `T is U` を成立させ、完全型を置換する |
| 完全型の束縛 | 総称引数・関連型・別名経由で内外の Origin を保持する | 射影や代入のたびに再束縛する、`static` を補う |
| 再借用 | 再借用した receiver・Subject の Self は再借用の Origin を持つ | 再借用した値に元の長い Origin を付ける |
| 制約の量化 | `ref/C is Iterable` を実際の借用 Origin で使う | 制約節内の新しい Origin を本体へ持ち出す、既存 Origin を再量化する |
| 型等式の補完 | `X.Cursor.Element is ref/i32` は左辺の Origin を保持する | 構造の不一致を補完で許す、未束縛名を存在変数にする、関連型定義へ転用する |
| Semantics の合成 | `uniq/(ref/T)` は参照スロットへの排他アクセス | それを T への排他アクセスへ平坦化する |

#### 9.5.2. 適合と要求

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 適合 | 同じ Array の owner／ref／uniq に別々の適合 | Origin や `when` だけが違う重複した直接適合 |
| 個別導出 | Iterator から Iterable を導き、借用列挙はラッパーへ委譲する | 導出が適用される型へ同じキーの独自適合を登録する、`uniq/I` の Iterator 適合を暗黙に作る |
| 関連型の指定 | 適合ブロックごとに Cursor を指定する | 型本体の `associate Iterable.Cursor` で複数モードの指定を兼ねる |
| 要求の識別 | 同じ宣言・同値な束縛済み環境への経路だけをまとめる | 外側の型引数や Origin が異なる関連型を短縮名で一つにする |
| 修飾名 | `T.(C).f` で contract を明示する | `T.C.f` が通常の修飾名とも解釈できるのに片方を暗黙に選ぶ |
| refinement | Left／Right を別々に実装し、Both でその対応を再利用する | 同一要求への経路で、完全な関連型や実装対応が一致しない |
| 実装互換 | 要求の任意の入力 Origin を受け入れる | `static` だけを受け入れる実装で、一般の借用要求を満たす |

#### 9.5.3. 呼出しと lookup

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 層ごとの lookup | `it: uniq/I` から I の next を呼ぶ。R 自身に同名要求があれば R を優先する | 外側の要求の receiver 取得に失敗して参照先へ戻る、共有経路で排他権限を回復する |
| 借用型の iterator | 借用型自身の適合の next は参照スロットを借り、完全な Self を保持する | `for` の next をメソッド lookup に置き換え、参照先の適合で代用する |
| 明示呼出し | `Iterable.iterate(values@ref)` | 適合がないため、owner から ref へ実装探索をやり直す |
| receiver 先頭 | self が末尾でも、`Type.method` と `T.C.f` は receiver が先頭。関数値の型も同順序 | `self:` ラベル、self の位置だけが異なる overload、実装側の名前省略許可の転用 |
| Property | 通常メンバーにない要求を get／set し、明示形で曖昧性を除く | 別の要求の getter と setter を組み合わせる、明示 get で一時値制限を回避する |
| 総称使用 | 例の count を三つの取得形で使う | 宣言時に証明できない要求を、好都合な具体化だけで許す |

#### 9.5.4. Subject・パターン・列挙

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 排他 Subject | 直接の `@uniq` と保存した uniq 値で同じモード | 保存したという理由で暗黙に ref へ弱める |
| 排他パターン | guard は共有で読み、成功後に payload を排他借用する | guard の候補から権限を持ち出す、共有経路から排他へ強める |
| 共有パターン | Copy 要素も `ref/U` で束縛し、値の位置で Copy read する | 束縛を値型にして Loan を消す、総称推論で参照型を値型に変える |
| Copy の列挙 | ref 適合からコピーして所有型の適合へ委譲する | 所有型の Iterable 適合だけで、共有 Subject の適合を補う |
| 非 lending | Element が既存の外部 source に依存する | iterator 所有の記憶域を、next の短い借用より長く返す |
| 分離 | 前の要素を保持して次の next を呼ぶ。総称・別コンパイル・委譲でも保持する | 同じ要素を二度排他的に返す、分割前の権限を再使用する |
| 終了・保持 | iterator の破棄後も、要素に必要な Loan が残る | 要素を保持したまま元の collection を再確保・破棄する |
| 標準 view | Slice の source を保持し、局所ハンドルの借用は不要なら終了する | `uniq/Slice` から要素の uniq 権限を作る |
| Dictionary | 排他列挙で値を更新し、キーは共有参照 | 生きた entry のキーを排他列挙から変更する |
| 境界と cleanup | 空・サイズゼロの要素、途中終了、未取得要素の一度だけの破棄 | 物理アドレスだけで分離を判定する、二重破棄・破棄漏れ |
| 標準借用の性能 | 空・疎な Dictionary も要素数に比例する走査、O(1) の生成・記憶域 | capacity 全体の事前走査、参照配列の生成、列挙用のヒープ割当て |
| キャッシュ・生成 | Origin ごとの証明を保ち、生成条件が同じコードを共有する | Origin を除いた候補を成立証明にする、外側スロットの非 alias 保証を参照先へ転用する |

## 10. 正式仕様へ取り込む際の担当箇所

これは将来の反映先であり、本書の作成では変更しない。

| 反映先 | 内容 |
| --- | --- |
| [第3章](../../spec/03-types-and-values.md) | 完全型の構成、三つの判断、Core と直接の対象の区別 |
| [第6章](../../spec/06-declarations-and-containers.md)、[第8章](../../spec/08-generics-constraints-and-contracts.md) | 完全型の制約、適合と Self、一意性と導出の登録、refinement、継承。§8.4.3 の Core 制限と型本体での関連型指定を廃止 |
| [第7章](../../spec/07-functions-and-callable-values.md)、[第9章](../../spec/09-names-signatures-and-access.md)、[第10章](../../spec/10-overload-resolution-and-inference.md) | 完全型の qualifier と曖昧性エラー、層ごとの lookup、receiver の取得、公開範囲。`Type.method` を含む明示呼出し・関数参照・Signature・呼出し規約の receiver 先頭 |
| [第11章](../../spec/11-properties.md) | 要求 Property の選択、明示 accessor、一時値制限、実装と既存 witness bridge の照合 |
| [第15章](../../spec/15-ownership-and-lifetime-analysis.md) | 新しい Origin と省略の意味、再借用の Origin、依存、Subject とパターン、Loan の保護元と領域、分割・移譲の保証と検証モデル |
| [第14章](../../spec/14-control-flow.md)、[第16章](../../spec/16-scope-exit-and-destruction.md) | for／match の共通取得、要素の取得、guard の共有参照と Copy read、終了時の責任 |
| [第4章](../../spec/04-arrays-indexing-and-slices.md)、[第22章](../../spec/22-core-execution-and-foreign-functions.md) | Iterable の Element と旧等式の削除、関連型 Cursor への改名、Iterator の完全型化、Iterator からの導出、標準適合、要素分離、Dictionary を含む計算量・割当て保証 |
| [第13章](../../spec/13-operators-and-assignment.md) | 個別の比較適合導出と operand 取得の区別。代入の意味は維持 |
| [第18章](../../spec/18-modules-and-dependencies.md)、[第21章](../../spec/21-layout-runtime-and-code-generation.md) | 完全な証明情報の保存・無効化、用途別のキャッシュと生成キー、Origin だけが異なる具体化のコード共有、§9.2 の最適化条件 |
| 付録 A／D／E／F | 検証例、今回解消する境界、用語、構文 |

取り込み時は、本文の担当節を定義元とし、他の節ではその適用と参照を記述する。例・標準宣言・milestone のソースは、格納済み uniq の Subject、共有パターンの束縛型、明示 receiver の引数順序、`Iterable.Cursor` の名前を含めて同時に整合させる。実装済み範囲は実際の検証後に STATUS.md へ記録し、本書だけを根拠に対応済みとは扱わない。
