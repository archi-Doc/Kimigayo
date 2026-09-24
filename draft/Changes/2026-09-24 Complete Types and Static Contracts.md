# 完全型と静的 contract の統一

日付: 2026-09-24

改訂日: 2026-09-25

状態: 議論で採用した方針をまとめた仕様変更案。正式仕様への取り込み・実装検証は未実施。

## 1. 位置付けと基本方針

本書が変更する事項には、[SPEC.md](../../SPEC.md) およびその参照先に優先して本書を適用する。変更しない事項には既存仕様を適用する。本書は独立した変更案であり、他の draft の内容を取り込むものではない。

中心となる型モデルを維持する。

```text
Type = Semantics/Core during Origin
```

この式は一層の説明である。型引数や要素は完全型を含み、借用や生ポインターの直接の対象も完全型になり得る。Core と、Semantics が直接適用される対象を混同しない。

仕様を次の共通規則から構成する。各規則は本書の担当節で一度だけ定義し、個別機能はその適用として説明する。

| 共通規則 | 担当 |
| --- | --- |
| 完全型の構成と比較 | §2 |
| 型と Origin の束縛・代入 | §3 |
| Loan の保持・分割・移譲 | §3.3–3.4 |
| 適合、関連型、要求と実装の対応 | §4 |
| 要求の呼出しと取得計画 | §5 |
| Subject の取得とパターン | §6 |
| Iterator／Iterable と標準型への適用 | §7 |

## 2. 完全型の構成と比較

### 2.1. すべての型束縛の共通対象

総称型引数、関連型、`Self`、引数・戻り値・格納位置の型は、Semantics と Origin 依存を保持した完全型を扱う。関連型の Core 限定と、Kimi の Element だけを完全型にする例外を廃止する。

これは、任意の型をあらゆる役割で使えるという意味ではない。型形成、Object Target、基底型、有限の配置、アクセス、格納、Copy／Owned などの条件は、それぞれ必要な位置で検証する。bare contract、Semantics 単体、一般の値引数を完全な値の型にはしない。長さ引数は従来どおり別の種類である。

Semantics の適用は合成であり、既存の層を置換・平坦化しない。

```text
ref/(uniq/T) ≠ uniq/T
ref/(obj/T)  ≠ objref/T
owner/W     = W
```

`owner/W` は冗長な指定を正規化するだけで、`W` の借用や object Semantics を取り除かない。object 形式の形成条件、生ポインターの Unsafe 条件も維持する。

### 2.2. 三つの判断

| 判断 | 条件と用途 |
| --- | --- |
| **型の同一性** | 宣言 Identity、すべての Semantics 層、型引数、長さ、入れ子の構造を比較し、Origin の束縛・関係を再帰的に除外する。適合の衝突判定など、明示された用途で使う。 |
| **完全な型記述の同値性** | 型の構造に加え、Origin の束縛、量化、成立条件が対応する。型の完全な置換・代入に使う。 |
| **使用上の適合性** | 既存の部分型・分散・寿命短縮の規則と、取得、初期化、アクセス、Loan の条件を満たす。その位置で値を使用できることを判断する。 |

型同士の **`T is U` は、完全な型記述の同値性を要求する**。代入可能性の検査でも、Origin を除いた比較でもない。右辺が contract や Semantics の場合の `is` は、それぞれの要件検査を維持する。

例えば `ref/T during a` と `ref/T during b` は同じ型であるが、`a` と `b` の同値性を証明できなければ完全な型記述は同値ではない。`a outlives b` だけなら、許可された方向への寿命短縮が可能なだけである。

型の同一性から、Origin の等しさ、Copy／Owned、代入可能性、現在の使用権限を導かない。特に、同じ型 Identity でも Origin によって Owned や条件付き適合の成立結果は異なり得る。

### 2.3. 正規化と証明の範囲

比較前に完全型を形成・検証し、別名、括弧、冗長な `owner/`、確定した関連型を正規化する。束縛名の綴りは Identity ではない。対応する量化変数は位置とスコープで対応付け、固定された外側の Origin を新しい量化変数として扱わない。

Origin の同値性は、既存の等式、outlives、交差の正規化と限定された証明規則で判断する。任意の論理的同値性の探索は要求しない。必要な証明が期限までに得られなければエラーとし、Unknown を成功や不成立に置き換えない。

Runtime Object Type Identity と overload の Signature は目的別に判断し、receiver の順序は §5.1 で統一する。Origin だけが異なる具体化は、選択した実装、検証済みの操作、配置・ABI など既存の生成条件が一致すればコードを共有できる。適合の証明とコード共有の判断は分ける（§9.1）。

## 3. 型と Origin の束縛

### 3.1. 入力・出力・対象の束縛

| 形式 | 完全型を決める側 |
| --- | --- |
| 総称型引数 `T` | 利用側 |
| 関連型 `Self.Element` | 適合を定義する側 |
| `Self` | その宣言が対象とする型 |

これらは同じ完全型の束縛・代入規則を使う。`associate Element is U` は、Origin を含む `U` 全体を束縛する。実装の戻り値や本体から関連型を推測しない。

`<s/T>` は一つの完全型の外側の Semantics と直接の対象を分解する既存の形式である。元の `s/T` は完全型全体を保持する。別の対象への `s/U` は新しい型構成であり、元の外側の Origin を自動でコピーしない。各構成の成立条件は、許されるすべての Semantics 束縛について検証する。

`semantics` という暗黙の変数、単独の Semantics 総称引数、`associate s/Element` は導入しない。完全型の関連型だけで今回の要求を満たす。

### 3.2. 既存の束縛を参照しても再束縛しない

**既に束縛された型を参照しても、Origin を再束縛しない。** 総称引数、`Self`、関連型、別名、入れ子の型すべてに適用する。代入後に省略規則を再実行して、新しい Origin や `static` を補わない。

新しい借用層を構成する位置だけが、その位置の既存の Origin 規則に従う。例えば `Self` に固定された source があっても、要求の `uniq/Self` はその外側に呼出しごとの借用を作る。source をその短い借用で置き換えない。

| 導入位置 | 束縛の範囲 |
| --- | --- |
| 適合ヘッダーの新しい借用 Origin | 適合全体。公開条件を満たすすべての有効な束縛について検証する。 |
| 制約の主語で構成する新しい借用 Origin | その制約節内。型形成条件を満たすすべての有効な束縛について要求する。 |
| 関数・要求の新しい入力借用 Origin | 呼出しごと。既存の署名の量化規則に従う。 |
| 既に完全型に含まれる Origin | 元の束縛を保持する。 |
| 関連型の指定 | 外側の束縛、`static`、完全型の依存と明示的な関係から完成させる。独立した自由な Origin を追加しない。 |

適合ヘッダーと制約の主語では、通常の入力借用注釈と同様に、未束縛の単純名を普遍的な Origin として導入できる。省略された外側の借用 Origin は独立した匿名入力になる。既に見える名前はその束縛を参照し、内側の借用や未知のスキーマに新しい省略許可を与えない。

`origin` 関係節そのものは名前を導入しない。`during` を持てる Semantics、集合名、射影、宣言スキーマの既存規則を維持する。Origin を持たない層に架空の `static` を追加しない。

#### 3.2.1. 完全型を主語とする制約

関数・型・contract の制約と `when` は、その位置で許される総称型・Self を起点として、Semantics を合成した型とその関連型射影を主語にできる。例えば `ref/C during a is Iterable` は、既存の a がなければ、有効なすべての a についての要求である。`ref/C is Iterable` も匿名の Origin について同じ意味になる。これは適合の利用条件であり、外部適合の宣言ではない。

新しい a のスコープは一つの制約節の全体である。`(ref/C during a).Iterable.Iterator.Element is ref/i32 during a` のように右辺でも参照できるが、関数本体・署名や別の制約節へ持ち出さない。既存の入力 Origin や型のスキーマを参照する制約は、その束縛についてのみ要求する。

使用時は、実際の借用 Origin を代入して必要な適合・等式の証明を得る。任意の Origin を探索せず、既存の限定された証明規則と期限を使う。一般の高階の関数型や Origin 引数を追加するものではない。関数の先頭に制約を置く既存構文を拡張し、節全体を括弧で囲んだ実行時の式との区別は維持する。

#### 3.2.2. 型等式の右辺での省略

利用条件としての型等式では、右辺に新しく書いた借用層の省略 Origin を、左辺の対応する層から補完する。例えば `X.Iterator.Element is ref/i32` は、左辺がその構造を持つことを要求し、右辺の Origin を左辺と同じ束縛にする。補完後には §2.2 の完全な同値性を検査する。

補完は構造上の対応が一意な位置に限り、不透明な左辺では必要な構造と同値性を制約として保持する。寿命短縮、`static` の仮定、任意の存在変数の探索は行わない。明示した Origin は既存の束縛を参照し、未束縛名はエラーとする。省略の補完で本体から参照できる名前は導入しない。

これは既に決まる左辺への制約であり、`associate Element is U` の定義には適用しない。関連型の定義は §3.1–3.2 に従って U を完成させ、自己参照や実装本体からの推測で補わない。

### 3.3. 依存と Loan の保持

Origin は有効期間を、Loan は領域・権限・借用元を表す。Origin の等式や短縮で Loan を統合・消去したり、排他権限を作ったりしない。

構造が不透明な関連型も、依存する Origin、Loan 要件、検証に必要な関係を保持した完全型である。情報が不明なことは Owned や無依存の証明にならない。具体構造に依存する合法な義務は、既存の期限まで保持する。

射影元に現れた Origin を、射影結果へ無条件に追加しない。射影元の有効性の検証と、選ばれた関連型が実際に保持する依存は別である。一方、結果型の型引数・外側環境などに本当に含まれる依存は、未使用でも既存規則どおり保持する。

元の型を寿命短縮できることから、その関連型も同じように短縮できるとは推論しない。分散と使用上の適合性を結果型について検証する。

### 3.4. Loan の分割と移譲

Loan は、記憶域を有効に保つ**保護元**と、その借用を通じて**アクセスできる領域**を区別して保持する。分割しても、元の記憶域の再確保・移動・破棄などへの保護は失わない。同じ保護元を持つことだけで、分割済みの領域を重複とは扱わない。

安全な分割・移譲は、次の保証を満たさなければならない。

- 同時に使用できる排他領域は重複せず、元の権限を複製・強化しない。結果を共有参照にしても、元の排他保護は弱めない。
- 結果と残部は必要な保護元・Origin・親 Loan を保持する。片方の終了で他方の保護を失わない。
- 短い receiver 借用に依存しない結果を返す場合も、その非重複性と更新後の状態を検証する。単に Origin を長く指定しても権限は分離できない。
- 総称要求、関数参照、別コンパイル、ユーザー型への委譲でも同じ保証を保つ。private な本体の再解析や型名による検査免除を前提にしない。

排他権限を保持する状態は Non-Copy であり、Move は権限も移す。共有参照の Copy は保護元を保持する。以後の操作・cleanup は残る権限だけを使う。分割を使わない借用・Slice の競合規則は変更しない。実装に必要な証明と分割モデルは §9.2 にまとめる。

## 4. 完全型への静的適合

### 4.1. 宣言できる対象

struct／enum 内で、次の適合宣言を認める。

```text
対象型 is 束縛済みContract [when 条件]
    [Origin 関係]
    [関連型の指定・要求実装]
```

対象型は、宣言中の型自身、またはそれを直接の対象として Semantics を合成した完全型とする。既存の囲む総称パラメータは使えるが、新しい型・Semantics パターン変数は導入しない。別の名目型、総称パラメータ単体、他の型の任意の構成への外部適合は宣言できない。

型形成は、囲む型と適合条件が許すすべての束縛について成立しなければならない。object 形式には Object Target の証明が必要であり、この構文だけで形成権限を与えない。

`when` は既存の正の要件条件を使う。ブロックの先頭の Origin 関係は、この適合の公開条件となる。型自体の成立条件には追加しない。関連型の指定に付けた関係は、その型の束縛を完成させ、残る条件を外側の公開条件から証明するものであり、隠れた適合条件を追加しない。

Origin の条件は、候補となる適合を使用できるかの証明に使う。Origin の違いだけで別の実装を選択しない。

### 4.2. `Self` と実装スコープ

適合ヘッダーの `Self` は `when` 条件内も含めて囲む型を表す。適合ブロックは新しい `Self` 束縛を導入し、先頭の Origin 関係を含むブロック内では適合対象の完全型を表す。要求側の `Self` にも同じ完全型を代入する。

```kimi
struct Array<E>
    ref/Self during source is Iterable
        // この Self は ref/Array<E> during source。
        associate Iterator is SharedArrayIterator<E>{it}
            origin it.source == source

        func iterate(self: Self) -> Self.Iterator
            ...
```

この例は適合の形を示す断片である。`SharedArrayIterator` は説明用の名前で、公開名や構築子を追加するものではない。

ブロックは、関連型の指定、要求を実装する関数、型の種類が許す要求の computed Property／accessor を含められる。格納、Case、構築子、`deinit`、入れ子の型・適合、要求と無関係な補助メンバーは置けない。補助関数は通常の宣言として置く。

要求実装は通常の関数・accessor と同じ規則で検証するが、囲む型の通常メンバーグループには追加しない。無条件・条件付きの適合ブロックともこの意味に統一する。

receiver 省略形 `self` は、ここでも常に `self: ref/Self` である。`Self` が既に参照型なら参照が一層増える。適合内の receiver の形は完全型の `Self` に対する式として検証し、代入後の二重参照を旧来の Core 前提だけで拒否しない。

通常メソッドグループの receiver shape 制約は維持する。異なる適合の要求実装は同じ通常グループに入らないため、共有・排他・消費の実装名を分ける必要はない。

### 4.3. 要求の識別と関連型

要求を識別する単位は、**定義元の宣言 Identity と、その定義元 contract の束縛済み環境**である。関数、Property、関連型に共通とし、accessor ではさらに `get`／`set` を区別する。環境には外側の型引数、Semantics、Origin の束縛と条件を保持する。

同じ宣言への複数経路は、完全な環境の同値性と §4.4 の経路整合性を検証してから統合する。例えば `Family<i32>.Source` と `Family<string>.Source` の Element は、宣言元が同じでも別の要求参照である。Origin を除いた登録キーの一致だけで、要求や関連型を統合しない。

実装側の関連型指定は、その適合ブロック内の `associate Element is U` に統一する。型本体に置く従来の `associate C.Element is U` は廃止し、等式を型本体へ移して指定を代用することも認めない。要求の制約・祖先の指定から一意に決まる場合は再指定を要しない。指定が必要ならブロックを書く。contract 内の関連型宣言と、利用側の関連型制約は維持し、関連型の独自の総称パラメータや Origin スキーマは追加しない。

`T.C.Element` を完全な射影、`T.Element` を利用可能な要求参照が一意な場合の短縮形とする。上の二つの Source に適合する T では `T.Element` は曖昧であり、結果型が偶然同じでも統合しない。`C` を利用箇所の contract 名とする解釈と、通常の修飾名としての解釈の両方が成立する場合、`T.C.Element` は曖昧性エラーとする。優先順位や適合・期待型による再選択は設けない。`T.(C).Element` は常に contract を選ぶ。同じ規則を要求の関数・Property 名にも適用する。

Semantics を適用した型全体を射影元にする場合は括弧を使う。

```kimi
(ref/Array<E> during source).Iterable.Iterator
```

これは `ref/(Array<E>.Iterable.Iterator)` とは異なる。既存の prefix／suffix の構文を維持し、名前解決で都合のよい方へ再解釈しない。Origin を含む各 qualifier の形成・アクセス・条件を検証する。

射影元になれることは、Core 専用の役割を与えるものではない。例えば借用型の関連型に `.init` を付けても、借用値の構築子は生成されない。

### 4.4. 一意性、証明、継承

#### 4.4.1. 登録と経路の整合性

適合の登録キーは、対象の型 Identity と、contract 宣言およびその束縛済み環境の Identity の組で決める。すべての Semantics と型引数を保持し、Origin は再帰的に除く。完全な Origin 束縛と条件は適合の証明に保持し、実装の選択キーにはしない。

直接適合は、断片の併合後に、同じ組へ一致し得る宣言を重複として拒否する。別の Origin、異なる `when`、条件の強弱・排他性で重複を許可しない。既存の限定された構造の照合で非衝突を証明できなければ、衝突の可能性があるものとする。

言語による個別の導出にも独立した実装の優先順位を設けない。導出の前提が成立する範囲で、同じ登録キーへのユーザー定義の適合と重なれば重複エラーとする。未確定の総称束縛で非衝突を証明できない場合も拒否する。前提のない万能な適合としては扱わず、例えば Iterator でない Array の Iterable 適合は §7.1 の導出と衝突しない。intrinsic の既存の宣言が導出の条件を検証させるだけの場合、それ自体は別の実装登録ではない。

直接適合、contract の refinement、有効な基底型からの継承が同じ適合へ到達する場合、同時に成立し得る経路の関連型と実装対応は一致しなければならない。関連型の一致には完全な型記述の同値性を要求する。

適合は定義時に、囲む型と公開条件が許すすべての束縛について検証する。選択された実装対応は保持し、具体化や呼出し側の名前解決で選び直さない。宣言中・循環中の適合だけを根拠に、自分自身の成立を証明してはならない。

#### 4.4.2. refinement は要求と証明の合成

`contract C: A, B` は、A と B の要求・制約を取り込み、それぞれへの適合を要求する。親の順序に優先順位はなく、循環は拒否する。同じ要求参照は一つとし、独立した要求は同名でも別々の実装対応を持てる。

各 contract が直接宣言する要求には、既存の Signature と receiver shape の宣言規則を適用する。継承した独立の要求まで一つの通常メンバーグループへ併合しない。「通常の宣言として同居できない」という理由だけでは refinement を拒否しない。同一要求の矛盾、両立しない型等式・公開条件は引き続き拒否する。修飾なしの利用時の曖昧性は §5.2 で扱う。

明示された親適合がある場合、親側で検証した関連型と実装対応を子で再利用し、子では不足する要求を補う。親適合が直接宣言されていない場合は、子の指定から親への対応も構成し、親の有効アクセス範囲を含めて検証する。複数の子から同じ親へ到達する場合も §4.4.1 を適用し、宣言・検証の順序で対応を選ばない。

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
    Self is Both
```

二つの put は別々の要求である。`Both.put(sink@ref, 1)` は曖昧だが、`Left.put(sink@ref, left: 1)` で一方を選べる。

#### 4.4.3. 基底型からの継承

基底型からの継承は既存の完全な要求照合と許可された receiver 投影に従う。Semantics の自動変更、所有する値の slicing、関連型の再選択は追加しない。

### 4.5. 実装の対応と公開範囲

要件を識別する名前、関数の種類、総称スロット、引数の順序・ラベル、receiver と引数の正規化した型構造は、既存の実装照合規則を使う。Origin は識別後の互換性検証に残す。

§4.4 で再利用する対応は再照合しない。残る要求について、ブロックに同名の実装グループがあればそこで照合を確定し、失敗しても通常メンバーへ戻らない。グループがなければ既存の通常メンバー照合または明示された intrinsic 導出を使う。各ローカル実装は少なくとも一つの未対応要求を満たさなければならず、既存対応を置換するためには使えない。ブロックなしの適合宣言も引き続き使える。

実装は、要求が許すすべての入力を受け入れ、要求以上の結果保証を提供する。強い Origin 前提、追加の Constraints、Unsafe な呼出し条件などを隠れて要求しない。Property の保証と既存の許可された witness bridge も維持する。

適合の公開範囲は、対象型、contract、その束縛と条件が満たす有効アクセス範囲で決める。ローカル実装は、親適合を含む各対応先で必要な範囲に従い、独立したアクセス修飾子を持たない。子の狭い公開範囲で親の要求を弱めず、API 署名も各範囲で検証する。公開適合から呼べることは、通常メソッドとして公開されることではない。

実装から private な補助関数を呼ぶ場合は、通常の宣言位置のアクセス規則を使う。外側の通常メンバーを直接対応付ける場合の既存のアクセス検証は省略しない。

## 5. 要求の選択と呼出し

### 5.1. 明示形と公開引数順序

`C` は束縛済み contract、`T` は完全型とする。C の修飾は §4.3 と同じ規則を使い、必要なら `T.(C)` や括弧付きの完全型で曖昧性を除く。

| 要求 | Self を receiver から決める | Self を T に固定する |
| --- | --- | --- |
| receiver を持つ関数 | `C.f(receiver, ...)` | `T.C.f(receiver, ...)` |
| Type 関数 | 使用不可 | `T.C.f(...)` |
| Property の get | `C.p.get(receiver)` | `T.C.p.get(receiver)` |
| Property の set | `C.p.set(receiver, value)` | `T.C.p.set(receiver, value)` |

receiver を明示する呼出し・関数参照では、宣言内の位置によらず、receiver を先頭の無名位置に置く。この規則を表の要求経由の形式と、通常の `Type.method` の両方に適用する。残りの引数は、参照先の仮引数列から `self` を除いた順序、ラベル、`!` 境界で照合する。receiver は通常引数の名前省略境界に数えず、`self:` ラベルも設けない。accessor の明示形は表の固定位置引数だけを取り、ラベル・default・`!` を持たない。

関数の Signature における引数順序も同じ形へ正規化し、self の宣言位置の違いだけで overload を区別しない。それ以外の Signature と実装照合の条件は維持する。

```kimi
Iterable.iterate(values@ref)
Iterator.next(iterator@uniq)  // iterator は owner Semantics の var 局所値。
T.C.create()

contract Apply
    func apply(! value: i32, self: ref/Self)

Apply.apply(target@ref, value: 1)
```

Self を推論する形では、要求の receiver 型と先頭の実引数から一意に決め、その適合を検証する。結果の期待型だけから推論せず、適合の有無で曖昧な解を順位付けしない。他の関数総称引数、overload、引数適合は既存規則に従い、適合を探すための追加の変換探索を行わない。

C の中に独立した同名要求が残る場合も、§5.2 の候補統合・receiver shape・曖昧性の規則を使う。必要なら定義元の contract を明示して選ぶ。receiver は既存どおり overload の優劣比較に使わない。

明示形の全実引数は通常の引数位置として、記述順に一度だけ評価・取得する。receiver にも暗黙の Receiver Expression を設けない。所有する Place の新しい排他借用には `@uniq`／`@objuniq` が必要であり、既存の借用値は通常どおり再借用できる。取得後に宣言の `self` 位置へ対応付け、宣言内の束縛・cleanup 順序は変えない。

`Iterable.iterate(values)` の Self を、適合を成立させるために `ref/Array<E>` へ変更してはならない。Non-Copy の所有する配列を渡すなら `@move`、共有列挙なら `@ref` と書く。

関数要求の参照は `T.C.f` として Self を固定し、要求の型・Origin・効果・実装対応を保持する。通常の `Type.method` と同様に、Function Item と関数値の型も receiver を先頭へ正規化する。関数値としては全引数を位置指定する。要求参照へ実装側の default や名前省略許可は伝播しない。receiver を持たない関数の順序は変えず、bound-method 値も追加しない。accessor の明示形は呼出し専用とする。

### 5.2. 層ごとの名前 lookup

値を receiver とする関数呼出しと Property の get／set は、その完全型 R から次の順に名前を探す。

1. 現在の層の通常メンバーを探す。アクセス可能で役割が合うグループまたは Property があれば、そこで確定する。この段階では安全な借用の参照先へ進まない。
2. なければ、その層の型構造に対応する公開適合・総称制約から同名の要求を集め、§4.3 に従って重複経路を除く。適合条件・Origin の証明は候補の義務として保持し、要求名があればその層で確定する。
3. 両方になく、現在の層が安全な `ref/U`／`uniq/U` なら U で繰り返す。それ以外は名前がないというエラーにする。

名前が見つかった層が要求の Self を決める。通常メンバーは既存の宣言元・基底型の対応を使う。同じ層では通常メンバーを優先し、要求と同じ実装を二重の候補にしない。確定後の適合条件、overload、receiver 取得、Loan などの失敗で別の層・適合へ戻らない。総称本体では宣言された証明環境でこの計画を決め、具体化後に lookup をやり直さない。

要求の関数は、候補の receiver shape が一つであることを確認してから既存の overload 選択を行う。Property は get／set の契約全体で一つを選び、その後に必要な accessor を検証する。setter の有無や結果の期待型で同名 Property を選び直さず、別の要求から getter と setter を組み合わせない。

独立した同名要求を一候補にまとめられるのは、公開する操作契約と実装対応の同値性を、許されるすべての束縛について証明できる場合だけである。Property では get／set の組全体を比較する。これ以外の曖昧性は §5.1 の明示形で解消する。

借用を辿る場合は既存の receiver 取得表に従い、経路の権限を超えない再借用・Copy read を使う。新しい Semantics の適合は生成せず、共有経路から排他権限を回復させない。object と生ポインターの lookup は既存規則を維持する。型経由の参照・Type 関数・明示した `T.C` は T 自身で lookup し、借用層を辿らない。`for` も §7.3 の完全型に対する適合を使う。

### 5.3. receiver 取得と Property の動作

名前の選択と receiver の取得は分ける。§5.2 で到達した層を対象とし、新しい Semantics の適合を探すための借用は行わない。選択した要求の receiver に完全な Self を代入して、次の取得を計画する。

| 要求の receiver | メソッド・Property 形式の取得 |
| --- | --- |
| `Self` | 完全型の値を渡す。一時値はそのまま、所有する Place は Copy／明示 Move、借用値は Copy／Reborrow で取得する。明示 Move は参照自体を移し、参照先の Copy 読みへ置き換えない。 |
| `ref/Self` | 完全な Self を保持する位置への共有借用 |
| `uniq/Self` | 完全な Self を保持する位置への排他借用。位置の変更権限が必要 |

例えば `it: uniq/I`、`I is Iterator` の `it.next()` は、外側に next がなければ I の層で要求を見つけ、参照先を排他再借用する。it の参照スロットの変更権限は不要である。一方、`uniq/I` 自身に next の要求があればそちらで確定し、receiver が `uniq/Self` なら参照スロットへの排他借用が必要になる。Self 自体が参照型でも層を省略しない。

明示形で参照スロットを借りるには `@uniq/Self` に相当する完全な対象型を指定し、参照先を再借用する `@uniq` 省略形とは区別する。object receiver などは既存の形成・取得条件を維持する。

Property 要求は accessor の呼出しであり、格納場所を公開しない。`value.p`、`value.p = rhs` と明示 accessor は、要求の型・権限・Loan と getter 結果の一時値制限を保持する。直接の格納操作へ lower しても、隠れた Field の Move／排他借用を許可しない。get／set の receiver 省略形と witness bridge は既存規則を使う。

候補の検討中には値を取得せず、確定した計画を通常の予約・活性化・cleanup 規則で実行する。メソッド形式は receiver を先に、明示形は全引数を記述順に評価する。Property 代入は既存の右辺先行を保つため、`C.p.set(receiver, value)` と評価順序が同じとは限らない。

## 6. Subject の取得とパターン

### 6.1. `for` と `match` の共通取得

Subject の式は一度だけ評価する。取得は、パターンや適合の成立結果によらず次の表で決める。

| 式 | 取得 |
| --- | --- |
| 所有する Place（Copy のものも含む） | その格納完全型への共有借用 |
| 共有借用値 | Semantics を保持した Copy／共有再借用 |
| 排他借用値 | Semantics を保持した排他再借用。親は移動せず、子の Loan が必要な間は競合する使用を禁止 |
| 借用以外の一時値 | 値として取得 |
| 明示的な `E@move` | 値を移動して取得。借用値の場合も、参照自体の移動を保持する |

明示的な `@move` の行を優先する。新たに得た借用の一時値は、その型・権限を保って引き継ぐ。括弧、局所変数への保存、引数・戻り値経由で、借用の Semantics を暗黙に弱めない。

これにより、格納済みの uniq 値や uniq 引数を Subject にした場合も、従来の共有再借用から排他再借用に変わる。派生 Loan の期間は arm 全体へ一律に延ばさず、既存どおり実際の使用と観測可能な cleanup に従う。

Copy の Subject を値の Copy に置き換える最適化も、束縛の型、Loan、更新先を変えない場合に限る。排他借用先をコピーへ置き換えてはならない。

安全な借用値の Semantics 保持は `ref`／`uniq` と `objref`／`objuniq` に共通である。`@uniq`／`@objuniq` と書いたことだけを理由とする Subject エラーを廃止する。ただし、これによって object 形式の Iterable 適合や object payload の暗黙のパターン分解を追加するわけではない。

```kimi
for item in values              // 所有する Place を共有借用。
    inspect(item)

for item in values@uniq         // 排他列挙。
    update(item)

let access = values@uniq
for item in access              // 同じく排他列挙。access 自体は移動しない。
    update(item)
```

共有に弱める場合は `access@ref`、参照自体を移す場合は `access@move` と書く。取得した型に必要な適合がなければエラーとし、別のモードへ再試行しない。

### 6.2. 分解後の取得

構造パターンは、各位置で `ref/U` または `uniq/U` を最大一層だけ辿れるものに拡張する。object Semantics と生ポインターは暗黙に辿らない。Binding／Wildcard はそれ自体では参照を辿らず、括弧は層を取り除かない。

子のアクセス権限は、通過した経路の権限を超えない。共有の経路を通った後に内側の `uniq` を見つけても、排他権限は回復しない。`ref/ref/U` などを同じ位置で繰り返し辿ってパターンに合わせない。

選択した位置の格納完全型を `U` とすると、body の取得は次のとおりとする。

| 経路 | 取得 |
| --- | --- |
| Subject 自身、または借用を通らない所有する部分 | 通常の Copy／Move |
| 共有の経路 | その位置への `ref/U`。Copy 型でも位置の借用を保持 |
| 排他の経路 | その位置への `uniq/U`。Copy 型でも位置の借用を保持 |
| Wildcard | 取得せず、既存の破棄責任を維持 |

`let`／`var` は得た値を保持する束縛の変更可否を決め、参照先の権限を増やさない。U 自体が参照型なら、共有・排他ともその参照スロットへの借用となる。共有パターンの Copy 要素も、従来の値取得から参照取得に変わる。

得た参照を値として使う位置では、既存の Copy read を適用できる。例えば `x: ref/i32` または `uniq/i32` から `let n: i32 = x` として値を得られる。これは期待型など既存の適合規則に基づく使用時の処理であり、束縛自体の型や総称推論を値型へ変更しない。

guard の candidate も、格納完全型 U の位置を `ref/U` として読み、値が必要な位置では Copy read を使う。candidate と body の束縛は別の Identity であり、body の取得は成功した guard の cleanup 後に行う。失敗時は候補を保持する。candidate を読むための新しい Loan は guard から持ち出せず、格納済み外部参照の Copy は元の依存を保持する。その他の guard 制限と、Case 変更・全体更新に対する Loan 検査は維持する。

`for` の単一束縛は next の Some payload を値として取得する。タプル形式は、返された要素自身の型からこの分解規則を使う。一般の match パターンを `for` の構文へ追加するものではない。

### 6.3. 書込みと寿命

`let item: uniq/U` は参照の付け替えを制限するが、参照先への排他アクセスを失わせない。今回、`=` や複合代入の意味を変更しない。参照先の更新には既存のメソッドや `Intrinsics.replace`／`exchange`／`swap` を使う。

借用した部分を未初期化にする Move は引き続き禁止する。借用値の `@move` と、参照先の所有権の移動は別である。

借用した Subject からの結果は、参照を保持する隠れた局所スロットではなく、元の referent と Loan に依存する。所有する Subject の内部への新しい借用は、その Subject の破棄を越えて逃がせない。格納済みの外部参照の Copy／Move は元の依存を保持する。

## 7. Iterator／Iterable

### 7.1. 要求の定義

```kimi
public contract Iterator
    associate Element

    func next(self: uniq/Self) -> Option<Self.Element>

public contract Iterable
    associate Iterator is ::Kimi.Iterator

    func iterate(self: Self) -> Self.Iterator
```

Iterable の Element を削除し、要素型は `T.Iterator.Element` から一意に得る。`T.Element` という新たな別名は今回追加しない。

`iterate` は適合対象の完全型の値を受け取る。`next` は iterator の状態を更新するため、その完全型を排他借用する。列挙対象・iterator・要素の Semantics は独立している。

Iterator の Element は next の呼出しより外側で束縛され、呼出しごとの receiver Origin を参照できない。このため、既存の外部 source への借用は返せるが、next の一時借用や iterator 所有の記憶域への借用は返せない。lending iterator は今回導入しない。

言語による個別の導出として、`I is Iterator` から `I is Iterable` を導く。`I.Iterable.Iterator is I` とし、iterate は受け取った I をそのまま返す。これにより `for x in it@move` は iterator を直接消費できる。この導出と独自の Iterable 適合の重複は §4.4.1 に従って拒否する。

`uniq/I is Iterator` は自動導出しない。これを追加すると外側の next が §5.2 の lookup を遮り、`it: uniq/I` の呼出しにも参照スロットの変更権限を要求するためである。借用して途中まで列挙する場合は、§7.5.3 のように参照を保持する型へ委譲する。関連型 Iterator に借用型を指定する場合も、その完全型自身の適合が必要である。

### 7.2. 標準型の適合

以下を通常の適合として要求し、組み込みの列挙モード選択表や Copy view 読みの代替経路を廃止する。`E`、`K`、`V` は完全型である。表の `a` は列挙対象の借用 Origin、Slice の `source` は元から保持する背後の記憶域の Origin を表す。

| 型 | 所有する値の Element | `ref` 適合の Element | `uniq` 適合の Element |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `E` | `ref/E during a` | `uniq/E during a` |
| `Dictionary<K,V>` | `(K,V)` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` |
| `Slice<E>` | `ref/E during source` | 同左 | 同左 |
| `ResolvedRange` | `isize` | `isize` | `isize` |

Array／固定配列は添字順、Dictionary は挿入順、ResolvedRange は既存の区間順を維持する。Range は引き続き Iterable ではない。object 形式や生ポインターへの標準の Iterable 適合は追加しない。

借用する配列 iterator の source は `a` であり、Element にも保持される。所有する配列 iterator は残る要素を所有する。Slice／ResolvedRange の iterator はハンドル・区間の内容を取得して使えるため、ハンドルを借用した局所 Origin を無条件に結果へ保持しない。Slice の実際の source は必ず保持する。

Dictionary のキーは排他列挙でも共有参照とする。`uniq/Slice<E>` はハンドルへの排他アクセスであり、共有 view の要素を排他的にするものではない。

`Array<s/T>` の `s` は格納要素の Semantics である。列挙モードは配列自身の外側の Semantics で決まる。例えば `E = ref/Dog during b` の場合、消費・共有・排他の Element はそれぞれ次になる。

```text
ref/Dog during b
ref/(ref/Dog during b) during a
uniq/(ref/Dog during b) during a
```

最後の型も Dog への排他アクセスは与えない。iterator の具体的な公開名や構築子は要求せず、利用者は関連型の射影を使える。

#### 標準借用 iterator の性能

上表の ref／uniq 適合と Slice の iterator に、次の管理コストを要求する。型を固定し、`n` は開始時の要素数とする。Dictionary では capacity ではなく生きた entry 数を数える。

| 操作・資源 | 保証 |
| --- | --- |
| iterator の生成・破棄 | O(1) |
| next | 償却 O(1)。空・終了後は O(1) |
| 最初の None までの全走査 | O(1 + n) |
| iterator 自身の追加記憶域 | O(1) |
| 列挙のためのヒープ割当て・参照カウント更新 | なし |

要素参照の配列を事前生成しない。Dictionary は列挙開始時に capacity 全体を走査せず、collection の既存の管理情報で生きた entry を辿れるようにする。利用者が保持する要素結果と body の処理は別に数える。所有する iterator の要素移動・破棄は既存の計算量と責任を維持する。この保証を一般のユーザー定義 Iterator へ要求しない。

Dictionary の remove における割当て禁止と記憶域再利用の保証も維持する。実現例は、既存の O(capacity) の管理領域に生きた entry の挿入順リンクと空き位置のリストを持ち、削除時にリンクから外し、再利用時に末尾へ接続する方式である。墓石を走査せず両方の保証を満たせる。これは非規範の実装例であり、墓石数の上限や特定の圧縮方式は言語規則にしない。

### 7.3. `for` の処理

1. §6.1 で式を一度取得し、隠れた iterable 局所値を作る。
2. その完全型の Iterable 適合を選び、取得した値を `@move` 相当で渡して iterate を一度呼ぶ。借用値なら参照と権限を移し、参照先を所有・消費しない。
3. 戻り値を更新可能な隠れた iterator 局所値へ置く。
4. iterator 自身の Iterator 適合を使い、その格納スロットへの短い排他借用で next を呼ぶ（§5.3）。
5. Some の要素を取得して body を実行する。最初の None で終了する。
6. 各反復とループ終了時の cleanup は、通常のスコープ終了規則に従う。

Iterable／Iterator の同名メソッドだけを探す duck typing は行わない。一般の Iterator に「一度 None を返したら永久に None」という追加の法則は課さないが、上記 Kimi iterator は終了後も None を返す。

break、return、try 伝播などの通常の制御移動では、未取得の所有要素を既存の順序で破棄する。借用 iterator は元の要素を破棄しない。Abort は既存どおり通常の巻戻しを保証しない。

### 7.4. 要素の排他借用と分離

標準の Array／固定配列／Dictionary の uniq 適合は、§3.4 の保証と §9.2 の検証要件に従い、次の手順で分割・移譲する。

1. iterate が受け取った排他権限を iterator の状態へ移す。状態は順序と未取得の領域を保持する。
2. next は未取得の先頭要素を検証済み操作で分離し、残部を状態へ、分離した権限を Some の要素へ渡す。要素の参照を公開する前に cursor と権限の移譲を確定する。
3. 要素がなければ None を返す。既に渡した領域を再び取得せず、cursor の巻戻しで権限を復元しない。

next の短い receiver 借用は iterator の状態を保護し、返す要素は元の外部 source に依存する。両者を混同しないため、前の結果を保持して次の next を呼べる。Dictionary は entry のキーと値を分離し、キーには共有、値には排他のアクセスだけを渡す。

iterator の破棄は残る権限だけを終了し、要素や元の collection を破棄しない。保持された結果は source の保護を維持するため、元の collection 経由の競合する読書き、構造変更、再確保、移動、破棄は引き続き拒否する。cleanup を含む検証にも同じ権限規則を適用する。

一般の安全な動的添字分割や排他 Slice の公開 API は追加しない。標準の操作への委譲でユーザー型も排他列挙を提供できる。独自実装にも同じ Loan の契約を要求し、Unsafe や型名を根拠に省略しない。

### 7.5. ユーザー型と総称関数の例

#### 7.5.1. collection への委譲

次は、配列に共有列挙を委譲する型の例である。具体的な iterator 名や独自の Origin スロット付き関連型を必要としない。

```kimi
public struct Bag<E>
    var items: Array<E>

    public init(items: Array<E>)
        self.items = items@move

    ref/Self during source is Iterable
        associate Iterator is (ref/Array<E> during source).Iterable.Iterator

        func iterate(self: Self) -> Self.Iterator
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

    for _ in source@ref/C       // C 全体を借り、その Origin で制約を具体化。
        ()
    for _ in source@move        // 借用の終了後に C 自体を消費。
        ()
```

`count(values@ref)` と `count(values@uniq)` は借用値を、`count(values@move)` は所有する配列を渡す。関数内の `source@move` は X の値を移すのであり、X が借用型なら元の collection を所有・消費するものではない。

`inspectThenConsume` の `@ref/C` は、C が借用型でもその格納値全体を借りる。`@ref` の省略形へ置き換えると参照先の再借用になり得るため、総称本体で意図する層を明示している。

排他要素の更新も既存操作で記述できる。

```kimi
for item in values@uniq          // values: Array<i32>
    Intrinsics.replace(item, with: 0)
```

#### 7.5.3. iterator を借用して列挙する

次は説明用のユーザー型であり、新しい標準 API ではない。参照の格納だけを追加し、要素型と next の契約は元の Iterator に委譲する。

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

    for _ in BorrowingIterator<I>.init(it)
        break
```

§7.1 の導出により、このラッパーも Iterable になる。反復は元の iterator の位置を進め、所有権は移さない。追加のヒープ割当ては不要で、転送呼出しは通常の最適化対象とする。参照や要素の Loan は省略しない。

#### 7.5.4. Copy 型の共有列挙

Copy 型を `for x in value` で列挙させる場合は、`ref/Self` の適合を宣言し、値をコピーして所有型の適合へ委譲できる。次の Span はユーザー型の例である。

```kimi
struct Span
    Self is Copy
    let range: ResolvedRange

    init(range: ResolvedRange)
        self.range = range

    Self is Iterable
        associate Iterator is ResolvedRange.Iterable.Iterator

        func iterate(self: Self) -> Self.Iterator
            return ResolvedRange.Iterable.iterate(self.range)

    ref/Self is Iterable
        associate Iterator is Span.Iterable.Iterator

        func iterate(self: Self) -> Self.Iterator
            let value: Span = self
            return Span.Iterable.iterate(value@move)
```

共有適合がない所有型だけの Iterable は、Copy 型でも `value@move` または所有する一時値で列挙する。適合を探すために取得モードを切り替えない。

## 8. 既存機能との境界

`T is C` から `ref/T is C` や `uniq/T is C` を一般には導出しない。標準型の明示的な適合と、言語が個別に規定する導出だけを使う。

Copy／Owned／Callable／Sealed／ObjectPayload の固有の条件と効果は維持する。完全型への適合構文を使って、その intrinsic が許さない実装を登録することはできない。Equatable／Comparable の借用・タプル合成も、既存の対象と条件を持つ個別の導出として維持する。

演算子が operand を借用して操作することと、その借用型が contract に適合することは別である。比較の既存の operand 取得、浮動小数点の意味、built-in の優先順位を変えない。

runtime contract View、object erasure、任意の外部適合、適合の優先順位、default 実装、関連型の独自の型／Origin 引数、lending iterator、参照を通した新しい代入規則は導入しない。今回の静的規則は、これらの設計を暗黙に確定しない。

## 9. 検証要件

### 9.1. 定義・生成・保存

総称本体は宣言された証明環境で検証し、具体化によって欠けた前提を補わない。具体型・総称引数・関連型を経由した同じ完全型は、同じ Semantics と Origin の保証を持つ。

適合の登録キーと完全な証明情報は分けて保持する。Origin を除いたキーで候補と実装対応のテンプレートを共有し、Origin 条件、Owned、完全な関連型と使用計画は実際の証明環境で検証する。前段の候補情報を適合成立の証明として再利用してはならない。検証結果も、必要な完全な環境が一致すればキャッシュできる。この区別は論理上の要件であり、特定のキャッシュ構造は要求しない。

要求の束縛済み環境、実装対応、Origin の量化・条件、Loan の分割・移譲、アクセスと効果を、保存・再読込み・無効化でも保持する。別コンパイル先の private な本体を調べなくても、公開契約と検証済み情報で使用を判定できなければならない。

静的適合の選択や Origin に、実行時の型タグ、寿命タグ、割当てを必須としない。コード共有や最適化は、取得順序、Loan、破棄責任、診断すべき違反を変えてはならない。標準コレクションの既存の計算量・割当て保証を維持する。

例えば `ref/(ref/T)` は、外側の参照スロットのアドレスが観測されず、型・寿命・aliasing・評価と cleanup の保証を保てる場合、内側の参照値を直接渡す形へ最適化できる。ABI の合意または適切な adapter を必要とし、外側スロットの非 alias 保証などを参照先へ転用しない。共有パターンの Copy 要素を値で処理する最適化にも同じ条件を適用する。

診断では、型形成の失敗、同値性の未証明、適合の欠如・衝突、関連型の不足・曖昧性、実装の非互換、使用時の Loan 違反を区別する。使用違反を「別の適合が必要」と誤って扱わない。

未使用の名前付き Origin に対する lint は任意とする。`Self`・関連型・条件を通した暗黙の使用も数え、ヘッダー以外に名前が現れないことだけでは警告しない。lint の有無は適合の成立を変えない。

### 9.2. Loan 分割の検証モデル

分割には、既存の構造的な非重複の証明、または実装まで検証された記憶域操作を必要とする。次の `splitLoan(L, A)` はその検証モデルであり、特定の内部命令や公開 API を要求するものではない。L のアクセス領域を R とする。

| 段階 | 必須の検証・保証 |
| --- | --- |
| 前提 | L の有効な排他権限、A が分割可能な R の部分領域であること、残部との非重複、範囲・初期化・配置・provenance |
| 移譲 | R 全体への権限を消費し、A と R ∖ A に別々の権限を渡す。消費した L を再使用しない |
| 保持 | 両方に元の保護元・Origin・必要な親 Loan を保持し、元の権限・有効期間を超えない |
| 終了 | 一方の終了で他方の保護を消さず、元への競合するアクセスは必要な派生 Loan の終了まで拒否する |

全部の移譲は A = R とする。空領域やサイズゼロの要素も論理的な位置で区別し、アドレスだけで重複を判断しない。分割自体は値の Move や未初期化化を伴わない。

標準の排他列挙には、残部の先頭要素を分離して安全な参照を返す検証済み操作を用意する。境界・位置・provenance を検証し、cursor と権限の更新を確定してから参照を返す。結果の Origin は元の source に接続する。動的添字の比較、Origin 注釈、Unsafe 指定だけでは証明にならず、未定義の生ポインター変換 API も前提にしない。

入力から消費する権限と、結果・更新後の状態へ渡す領域・保護元を呼出しの検証情報に保持する。通常の receiver 保護も維持する。同じ保証を証明できる別のモデルを使ってよく、Loan の実行時タグ、要素ごとの割当て、一般の整数定理証明は要求しない。

### 9.3. 成立例と拒否例

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 型比較 | 同じ構造の借用は Origin が異なっても同じ型 Identity | Origin の同値性なしで `T is U` を成立させ、完全型を置換 |
| 完全型の束縛 | 総称引数・関連型・別名経由で内外の Origin を保持 | 射影や代入のたびに再束縛、または `static` を補充 |
| 制約の量化 | `ref/C is Iterable` を実際の借用 Origin で使用 | 制約節内の新しい Origin を本体へ持ち出す、既存 Origin を再量化 |
| 型等式の補完 | `X.Iterator.Element is ref/i32` は左辺の Origin を保持 | 型構造の不一致を補完で許可、未束縛名の存在変数化、関連型定義への転用 |
| Semantics の合成 | `uniq/(ref/T)` は参照スロットへの排他アクセス | それを T への排他アクセスへ平坦化 |
| 層ごとの lookup | `it: uniq/I` から I の next を呼ぶ。R 自身に同名要求があれば R が優先 | 外側の要求の receiver 取得に失敗して参照先へ戻る、共有経路で排他権限を回復 |
| 借用型の iterator | 借用型自身の適合を選んだ next は参照スロットを借り、完全な Self を保持 | `for` の next をメソッド lookup に置き換え、参照先の適合で代用 |
| 適合 | 同じ Array の owner／ref／uniq に別々の適合 | Origin や `when` だけが違う重複した直接適合 |
| 個別導出 | Iterator から Iterable を導き、借用列挙はラッパーへ委譲 | 導出が適用される型へ同じキーの独自適合を登録、`uniq/I` の Iterator 適合を暗黙生成 |
| 関連型の指定 | 適合ブロックごとに Iterator を指定 | 型本体の `associate Iterable.Iterator` で複数モードの指定を兼用 |
| 要求の識別 | 同じ宣言・同値な束縛済み環境への経路だけを統合 | 外側の型引数や Origin が異なる関連型を短縮名で一つにする |
| 修飾名 | `T.(C).f` で contract を明示 | `T.C.f` が通常の修飾名とも解釈できるのに片方を暗黙選択 |
| refinement | Left／Right を別々に実装し、Both でその対応を再利用 | 同一要求への経路で完全な関連型や実装対応が不一致 |
| 実装互換 | 要求の任意の入力 Origin を受け入れる | `static` だけを受け入れる実装で一般の借用要求を満たす |
| 明示呼出し | `Iterable.iterate(values@ref)` | 適合がないため owner から ref へ実装探索をやり直す |
| 公開引数順序 | self が末尾でも `Type.method` と `T.C.f` は receiver が先頭。関数参照も同順序 | `self:` ラベル、self の位置だけが異なる overload、実装側の名前省略許可の転用 |
| Property | 通常メンバーにない要求を get／set し、明示形で曖昧性を除く | 別の要求の getter／setter を合成、明示 get で一時値制限を回避 |
| 総称使用 | 例の count を三つの配列の取得形で使用 | 宣言時に証明できない要求を、好都合な具体化だけで許可 |
| 排他 Subject | 直接の `@uniq` と、保存した uniq 値で同じモード | 保存したという理由で暗黙に ref へ弱める |
| 排他パターン | guard は共有で読み、成功後に payload を排他借用 | guard の候補から権限を持ち出す、共有経路から排他へ強化 |
| 共有パターン | Copy 要素も `ref/U` で束縛し、必要な値位置で Copy read | 束縛を値型にして Loan を消す、総称推論で参照型を無条件に値型へ変更 |
| Copy の列挙 | ref 適合からコピーして所有型の適合へ委譲 | 所有型の Iterable 適合だけで、共有 Subject の適合を補う |
| 非 lending | Element が既存の外部 source に依存 | iterator 所有の局所記憶域を next の短い借用より長く返す |
| 分離 | 前の要素を保持して別の要素へ next。総称・別コンパイル・委譲でも保持 | 同じ要素を二度排他的に返す、分割前の権限を再使用する |
| 終了・保持 | iterator 破棄後も要素の必要な Loan が残る | 要素を保持したまま元の collection を再確保・破棄 |
| 標準 view | Slice の source を保持し、局所ハンドルの借用は不要なら終了 | uniq/Slice から要素の uniq 権限を作る |
| Dictionary | 排他列挙で値を更新し、キーは共有参照 | 生きた entry のキーを排他列挙から変更 |
| 境界と cleanup | 空・サイズゼロの要素、途中終了、未取得要素の一度だけの破棄 | 物理アドレスだけで分離を判定、二重破棄・破棄漏れ |
| 標準借用の性能 | 空・疎な Dictionary も要素数に比例する走査、O(1) の生成・記憶域 | capacity 全体の事前走査、参照配列の生成、列挙用ヒープ割当て |
| キャッシュ・生成 | Origin ごとの証明を保ち、生成条件が同じコードを共有 | Origin を除いた候補を成立証明にする、外側スロットの非 alias 保証を参照先へ転用 |

## 10. 正式仕様へ取り込む際の担当箇所

これは将来の反映先であり、本書の作成では変更しない。

| 反映先 | 内容 |
| --- | --- |
| [第3章](../../spec/03-types-and-values.md) | 完全型の構成、三つの比較・適合判断。Core と直接の対象の区別 |
| [第6章](../../spec/06-declarations-and-containers.md)、[第8章](../../spec/08-generics-constraints-and-contracts.md) | 完全型の制約、適合と Self、一意性、refinement、継承。§8.4.3 の Core 制限と型本体での関連型指定を廃止 |
| [第7章](../../spec/07-functions-and-callable-values.md)、[第9章](../../spec/09-names-signatures-and-access.md)、[第10章](../../spec/10-overload-resolution-and-inference.md) | 完全型の qualifier、曖昧性エラー、層ごとの lookup、取得・公開範囲。Type.method を含む明示呼出し・関数参照・Signature の receiver 順序を統一 |
| [第11章](../../spec/11-properties.md) | 要求 Property の選択・明示 accessor・一時値制限、実装と既存 witness bridge の照合 |
| [第15章](../../spec/15-ownership-and-lifetime-analysis.md) | 制約の Origin 量化と等式の補完、依存、Subject とパターン、Loan の保護元とアクセス領域、分割・移譲の保証と検証モデル |
| [第14章](../../spec/14-control-flow.md)、[第16章](../../spec/16-scope-exit-and-destruction.md) | for／match の共通取得、要素取得、guard の共有参照と Copy read、終了時の責任 |
| [第4章](../../spec/04-arrays-indexing-and-slices.md)、[第22章](../../spec/22-core-execution-and-foreign-functions.md) | Iterable の Element と旧等式を削除し Iterator を完全型化。Iterator からの導出、標準適合、要素分離、Dictionary を含む計算量・割当て保証 |
| [第13章](../../spec/13-operators-and-assignment.md) | 個別の比較適合導出と operand 取得の区別。代入の意味は維持 |
| [第18章](../../spec/18-modules-and-dependencies.md)、[第21章](../../spec/21-layout-runtime-and-code-generation.md) | 完全な証明情報の保存・無効化、用途別のキャッシュと生成キー、Origin ごとのコード共有、参照スロットの最適化条件 |
| 付録 A／D／E／F | 検証例、今回解消する境界、用語、構文 |

取り込み時は、本文の担当節を定義元とし、他の節ではその適用と参照を記述する。例・標準宣言・milestone のソースは、格納済み uniq の Subject、共有パターンの束縛型、明示 receiver の引数順序を含めて同時に整合させる。実装済み範囲は実際の検証後に STATUS.md へ記録し、本書だけを根拠に対応済みとは扱わない。
