# 完全型と静的 contract の統一

日付: 2026-09-24

改訂日: 2026-09-25

状態: 議論で採用した方針をまとめた仕様変更案。正式仕様への取り込み・実装検証は未実施。

## 1. 位置付け

本書が変更する事項は [SPEC.md](../../SPEC.md) とその参照先に優先し、変更しない事項には既存仕様を適用する。他の draft の内容は取り込まない。

型モデル `Type = Semantics/Core during Origin` を維持する。この式は一層分の説明であり、型引数、要素、借用や生ポインターの直接の対象は完全型になり得る。Core と、Semantics が直接適用される対象を混同しない。

各共通規則は次の担当節で一度だけ定義し、他の節ではその適用を記述する。

| 共通規則 | 担当 |
| --- | --- |
| 完全型の構成と比較 | §2 |
| 束縛、新しい Origin と省略 | §3.1–3.3 |
| Loan の保持・分割・移譲 | §3.4–3.5 |
| 適合、関連型、要求と実装の対応 | §4 |
| 名前 lookup、呼出し、receiver の取得 | §5 |
| Subject の取得とパターン | §6 |
| Iterator／Iterable と標準型 | §7 |
| 検証、生成、診断 | §9 |

## 2. 完全型

### 2.1. 束縛の対象

総称型引数、関連型、`Self`、引数・戻り値・格納位置の型は、すべて完全型（Semantics と Origin 依存を含む型）を扱う。関連型の Core 限定と、Kimi の Element だけの例外は廃止する。

型形成、Object Target、基底型、有限の配置、アクセス、格納、Copy／Owned などの条件は、必要な位置で従来どおり検証する。bare contract、Semantics 単体、一般の値引数は値の型にならず、長さ引数は別の種類である。

Semantics の適用は合成であり、層を置換・平坦化しない。object 形式の形成条件と生ポインターの Unsafe 条件は維持する。

```text
ref/(uniq/T) ≠ uniq/T
ref/(obj/T)  ≠ objref/T
owner/W      = W        // 冗長な指定の正規化のみ。W の層は残る
```

### 2.2. 三つの判断

| 判断 | 比較する内容 | 用途 |
| --- | --- | --- |
| **型の同一性** | 宣言 Identity、すべての Semantics 層、型引数、長さ、入れ子の構造。Origin は再帰的に除く | 適合の登録キー、衝突判定 |
| **完全な型記述の同値性** | 型の同一性に加え、Origin の束縛・量化・成立条件の対応 | 型の置換・代入、型同士の `T is U` |
| **使用上の適合性** | 既存の部分型・分散・寿命短縮と、取得・初期化・アクセス・Loan の条件 | その位置で値を使えるか |

- 型同士の `T is U` は完全な型記述の同値性を要求する。右辺が contract や Semantics の `is` は、従来どおりの要件検査である。
- 例えば `ref/T during a` と `ref/T during b` は同じ型 Identity だが、`a` と `b` の等しさを証明できなければ同値ではない。`a outlives b` は、許された方向への寿命短縮を許すだけである。
- 型の同一性から、Origin の等しさ、Copy／Owned、代入可能性、使用権限を導かない。Owned や条件付き適合の結果は Origin で変わり得る。

### 2.3. 正規化と証明

- 比較の前に完全型を形成・検証し、別名、括弧、冗長な `owner/`、確定した関連型を正規化する。束縛名の綴りは Identity ではない。量化変数は位置とスコープで対応付け、固定された外側の Origin を新しい量化変数にしない。
- Origin の同値性は、既存の等式・outlives・交差の正規化と限定された証明規則で判断し、任意の論理的同値性は探索しない。期限までに証明できなければエラーとし、Unknown を成功や不成立に置き換えない。
- Runtime Object Type Identity と overload の Signature は目的別に定義する。receiver の順序は §5.1、コード共有は §9.2 に従う。

## 3. 型と Origin の束縛

### 3.1. 束縛する側

| 形式 | 完全型を決める側 |
| --- | --- |
| 総称型引数 `T` | 利用側 |
| 関連型 `Self.Element` | 適合を定義する側 |
| `Self` | その宣言が対象とする型 |

- 三つとも同じ束縛・代入規則を使う。`associate Element is U` は Origin を含む U 全体を束縛し、実装の戻り値や本体から推測しない。
- `<s/T>` は、完全型を外側の Semantics と直接の対象に分解する既存の形式である。元の `s/T` は完全型全体を保つ。別の対象への `s/U` は新しい構成であり、元の外側の Origin を引き継がない。各構成の成立条件は、許されるすべての Semantics 束縛について検証する。
- 暗黙の `semantics` 変数、単独の Semantics 総称引数、`associate s/Element` は導入しない。

### 3.2. 束縛の保持と呼出しでの Self

**束縛済みの型を参照しても、Origin を再束縛しない。** 総称引数、`Self`、関連型、別名、入れ子の型に共通であり、代入後に省略規則を再実行して Origin や `static` を補わない。新しい Origin を得るのは、借用、再借用、要求の `uniq/Self` など、新しい借用層を作る操作だけである。

呼出しでは、Self の決定と receiver の借用を独立に扱う。メソッド形式と明示形で同じ規則を使う。

- **Self**: 固定済み（束縛済みの総称型、`T.(C).f` の T）なら再束縛せず、渡す値は使用上の適合性で検査する。固定されていなければ、receiver の型式と実引数の照合で決める。receiver が `Self` 自体なら、取得した値の完全型（再借用ならその Origin を含む）が Self になる。
- **借用**: receiver の形で決める。`ref/Self`・`uniq/Self` は、外側に呼出しごとの借用（借用値なら再借用）を作り、その層だけが新しい Origin を得る。Self の内側の Origin は保つ。

例えば `it: uniq/I` の `it.next()` と `I.(Iterator).next(it)` では、Self は `I` のままで、外側に短い排他再借用を作る。I に含まれる source を短い借用で置き換えない。

### 3.3. 新しい Origin と省略の意味

| 導入位置 | 意味 |
| --- | --- |
| 署名の借用注釈（関数、構築子、明示 accessor、要求の入力・戻り値） | 既存の §15.3.4 のまま。戻り値だけに現れる Origin も普遍量化する |
| 適合ヘッダーの対象型 | 普遍量化。適合全体で、公開条件を満たすすべての束縛について検証する |
| 制約・`when` の主語（`is` の左辺） | 普遍量化。範囲はその制約節の中だけ |
| 制約の型等式の右辺 | 左辺の対応する層の Origin で補完する（§3.3.2） |
| 関連型の指定 | 外側の束縛、`static`、依存と明示的な関係から完成させる。自由な Origin を追加しない |
| 既存の完全型に含まれる Origin | 元の束縛を保つ |

- 未束縛の単純名を新しい Origin として導入できるのは、既存の署名の借用注釈と、本書が追加する適合ヘッダー・制約の主語だけである。既に見える名前はその束縛を参照する。
- 内側の借用や未知のスキーマに、新しい省略許可を与えない。
- `origin` 関係節は名前を導入しない。`during` を持てる Semantics、集合名、射影、宣言スキーマの既存規則を維持し、Origin を持たない層に `static` を補わない。

#### 3.3.1. 完全型を主語とする制約

関数・型・contract の制約と `when` は、使える総称型・`Self` から Semantics を合成した型と、その関連型射影を主語にできる。これは適合の利用条件であり、外部適合の宣言ではない。

```kimi
func inspectThenConsume<C>(source: C)
    ref/C is Iterable      // すべての借用 Origin について成立する
    C is Iterable
    ...

func total<C>(source: ref/C) -> i32
    ref/C is Iterable      // 射影には適合の証拠が要る
    (ref/C during a).(Iterable).Cursor.(Iterator).Element is ref/i32 during a   // a はこの節の中だけ
    ...
```

- 使用時は実際の借用 Origin を代入して証明し、Origin を探索しない。
- 節で導入した Origin は、本体・署名・別の節へ持ち出せない。
- 高階関数型や Origin 引数は追加しない。関数先頭の制約構文の拡張であり、括弧で囲んだ実行時の式とは区別する。

#### 3.3.2. 型等式の右辺での補完

```kimi
X.Cursor.Element is ref/i32   // 右辺の Origin は、左辺の Element の Origin と同じ
```

- 右辺に新しく書いた借用層の省略 Origin を左辺の対応する層から補完し、その後で §2.2 の同値性を検査する。
- 補完は構造上の対応が一意な位置に限る。不透明な左辺では、必要な構造と同値性を制約として保つ。寿命短縮、`static` の仮定、存在変数の探索は行わない。
- 右辺で明示した Origin 名は、既存の束縛（同じ節の主語で導入した名前を含む）を参照する。
- `associate Element is U` の定義には適用しない。

### 3.4. 依存と Loan の保持

Origin は有効期間を、Loan は領域・権限・借用元を表す。Origin の等式や短縮で、Loan を統合・消去したり、排他権限を作ったりしない。

- 不透明な関連型も、依存する Origin、Loan の要件、必要な関係を保つ。情報がないことは Owned や無依存の証明にならず、具体構造に依存する義務は既存の期限まで保つ。
- 射影元の Origin を射影結果へ無条件に加えない。結果型の型引数・外側環境に含まれる依存は、未使用でも保つ。
- 元の型を寿命短縮できても、関連型も短縮できるとは推論しない。結果型について分散と使用上の適合性を検証する。

### 3.5. Loan の分割と移譲

Loan は、記憶域を有効に保つ**保護元**と、アクセスできる**領域**を区別する。分割しても、元の記憶域の再確保・移動・破棄に対する保護は失わない。保護元が同じだけで、分割済みの領域を重複とは扱わない。

安全な分割・移譲は、次を保証する。

1. 同時に使える排他領域は重複しない。権限を複製・強化せず、結果を共有参照にしても元の排他保護は弱めない。
2. 結果と残部は必要な保護元・Origin・親 Loan を保ち、一方の終了で他方の保護を失わない。
3. 短い receiver 借用に依存しない結果も、非重複性と更新後の状態を検証する。Origin を長く書くだけでは権限を分離できない。
4. 総称要求、関数参照、別コンパイル、ユーザー型への委譲でも同じ保証を保ち、private な本体の再解析や型名による免除に頼らない。

排他権限を持つ状態は Non-Copy であり、Move は権限も移す。共有参照の Copy は保護元を保つ。以後の操作と cleanup は残る権限だけを使う。分割しない借用と Slice の競合規則は変えない。検証モデルは §9.4 に置く。

## 4. 完全型への静的適合

### 4.1. 宣言の形式

struct／enum の中で次の形で宣言する。

```text
対象型 is 束縛済みContract [when 条件]
    [Origin 関係]
    [関連型の指定・要求実装]
```

- **対象型**: 宣言中の型自身か、それを直接の対象として Semantics を合成した完全型に限る。囲む総称パラメータは使えるが、新しい型・Semantics のパターン変数は導入しない。別の名目型、総称パラメータ単体、他の型の構成への外部適合は宣言できない。
- **型形成**: 囲む型と適合条件が許すすべての束縛で成立しなければならない。object 形式には Object Target の証明が要る。
- **条件**: `when` は既存の正の要件条件である。ブロック先頭の Origin 関係は適合の公開条件であり、型自体の成立条件には加えない。関連型の指定に付けた関係は束縛を完成させるだけで、隠れた適合条件を加えない。
- Origin の条件は適合を使えるかの証明に使い、Origin の違いだけで別の実装を選ばない。

### 4.2. `Self` とブロック

ヘッダーの `Self`（`when` を含む）は囲む型を表す。ブロックは新しい `Self` を導入し、先頭の Origin 関係を含むブロック全体で適合対象の完全型を表す。要求側の `Self` にも同じ型を代入する。

```kimi
struct Array<E>
    ref/Self during source is Iterable
        // ここでの Self は ref/Array<E> during source。
        associate Cursor is SharedArrayIterator<E>{it}   // 説明用の名前。公開名ではない
            origin it.source == source

        func iterate(self: Self) -> Self.Cursor
            ...
```

- ブロックに置けるのは、関連型の指定、要求を実装する関数、型の種類が許す要求の computed Property／accessor だけである。補助関数などは通常の宣言として置く。
- 要求実装は通常の関数・accessor と同じ規則で検証するが、通常メンバーグループには入らない。このため、適合が異なれば receiver shape が異なってもよく、共有・排他・消費の実装名を分ける必要はない。通常グループの receiver shape 制約は維持する。
- receiver 省略形 `self` は常に `self: ref/Self` である。`Self` が参照型なら参照が一層増え、これを旧来の Core 前提で拒否しない。

### 4.3. 要求と関連型

#### 4.3.1. 要求の識別

要求（関数、Property、関連型）は、**定義元の宣言 Identity と、定義元 contract の束縛済み環境**（外側の型引数、Semantics、Origin の束縛と条件）で識別する。accessor はさらに `get`／`set` を区別する。

同じ宣言への複数の経路は、環境の完全な同値性と §4.4.2 の整合性を検証してからまとめる。例えば `Family<i32>.Source` と `Family<string>.Source` の Element は別の要求である。

#### 4.3.2. 関連型の指定

- 実装側の指定は、適合ブロック内の `associate Element is U` だけとする。型本体の `associate C.Element is U` と、等式による代用は廃止する。
- 要求の制約・祖先から一意に決まるなら再指定しない。指定が必要ならブロックを書く。
- contract 内の関連型宣言と、利用側の関連型制約は維持する。関連型に独自の総称パラメータや Origin スキーマは追加しない。

#### 4.3.3. 修飾名と射影

| 形式 | 意味 |
| --- | --- |
| `T.(C).N` | T の C 適合の要求 N（関連型・関数・Property）。完全な射影 |
| `T.N` | T の通常メンバー（入れ子の型を含む）を先に探し、なければ T で利用可能な要求の短縮形。N を contract 名としては解釈しない |

- 短縮形の候補が複数なら曖昧性エラーとし、結果型が同じでもまとめない。例えば二つの Source に適合する T の `T.Element` は曖昧であり、`T.(Source).Element` で選ぶ。
- 通常メンバーに遮られた要求も `T.(C).N` で指定できる。contract を追加しても既存の `T.N` の意味は変わらない。適合の証明結果や期待型で選び直さない。
- Semantics を合成した型全体を射影元にするときは括弧で囲む。prefix／suffix の既存構文を維持する。
- 各 qualifier の形成・アクセス・条件を検証する。射影元になっても Core 専用の役割は得ない（借用型の関連型に `.init` を付けても構築子は生成されない）。

```kimi
(ref/Array<E> during source).(Iterable).Cursor   // 借用型の適合の Cursor
ref/(Array<E>.(Iterable).Cursor)                  // 所有配列の適合の Cursor への参照
```

### 4.4. 一意性と整合性

#### 4.4.1. 登録キーと重複

- 登録キーは、対象の型 Identity と、contract 宣言とその束縛済み環境の Identity の組である（Semantics と型引数は含み、Origin は除く）。完全な Origin 束縛と条件は証明情報として別に保ち、実装の選択には使わない。
- 直接適合は、断片の併合後、同じキーに一致し得る宣言を重複として拒否する。Origin、`when`、条件の強弱・排他性の違いでは許可しない。限定された構造照合で非衝突を証明できなければ、衝突とみなす。
- 言語による導出（§7.1、§8）は、それぞれ**結論の適合**（対象型、contract、すべての前提、関連型と実装対応）を定める。前提は、前提の適合とその `when`・公開 Origin 条件である。前提の適合が登録された対象について、結論を前提付きの直接適合とみなし、上の規則を適用する。導出に優先順位はない。
  - 例えば `I is Iterator` ⇒ `I is Iterable` の結論は Iterator 適合を持つ型にだけ登録されるため、Array の Iterable 適合とは衝突しない。
- intrinsic の既存宣言が導出の条件を検証させるだけなら、それ自体は実装登録ではない。

#### 4.4.2. 経路の整合性

- 直接適合、refinement、基底型からの継承が同じ適合に到達するなら、同時に成立し得る経路の関連型（完全な同値性で比較）と実装対応は一致しなければならない。
- 適合は定義時に、囲む型と公開条件が許すすべての束縛で検証する。選ばれた実装対応は保ち、具体化や呼出し側で選び直さない。宣言中・循環中の適合だけで自分自身の成立を証明しない。
- 総称環境でも同じである。例えば `I is Iterator` と `I is Iterable` が与えられたとき、I の Iterable 適合は導出以外にないため、`I.(Iterable).Cursor` を `I` に正規化する。

#### 4.4.3. refinement

`contract C: A, B` は A と B の要求・制約を取り込み、それぞれへの適合を要求する。

- 親の順序に優先順位はなく、循環は拒否する。同じ要求はまとめ、独立した要求は同名でも別々に実装できる。
- 直接宣言した要求には、既存の Signature と receiver shape の規則を適用する。継承した独立の要求は同じ通常グループにしないため、同居できないことだけでは拒否しない。同一要求の矛盾、両立しない型等式・公開条件は拒否する。
- 親適合が明示されていれば、その関連型と実装対応を再利用し、子は不足分だけを補う。明示されていなければ、子の指定から親への対応も構成し、親の有効アクセス範囲も検証する。複数の子から同じ親に至る場合は §4.4.2 に従う。

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

### 4.5. 実装の照合と公開範囲

**照合**

1. 名前、関数の種類、総称スロット、引数の順序・ラベル、receiver と引数の正規化した型構造は、既存の実装照合規則で照合する。Origin は識別後の互換性で検証する。
2. §4.4 で再利用する対応は照合し直さない。
3. 残る要求は、ブロックに同名の実装があればそこで確定し、失敗しても通常メンバーへ戻らない。なければ既存の通常メンバー照合か、明示された intrinsic 導出を使う。ブロックなしの適合宣言も使える。
4. 各ローカル実装は、少なくとも一つの未対応要求を満たさなければならない。既存の対応の置換には使えない。

**互換性.** 実装は、要求が許すすべての入力を受け入れ、要求以上の結果保証を与える。強い Origin 前提、追加の Constraints、Unsafe な呼出し条件を隠れて要求しない。Property の保証と既存の witness bridge は維持する。

**公開範囲**

- 適合の公開範囲は、対象型、contract、束縛と条件の有効アクセス範囲で決まる。ローカル実装はアクセス修飾子を持たず、親適合を含む各対応先の範囲に従う。子の狭い範囲で親の要求を弱めない。
- 公開適合から呼べても、通常メソッドとして公開されるわけではない。
- 実装から呼ぶ補助関数や、対応付ける通常メンバーには、通常のアクセス規則を適用する。

## 5. 要求の選択と呼出し

### 5.1. 明示形

C は束縛済み contract、T は完全型とする。

| 要求 | Self を receiver から決める | Self を T に固定する |
| --- | --- | --- |
| receiver を持つ関数 | `C.f(receiver, ...)` | `T.(C).f(receiver, ...)` |
| Type 関数 | 使用不可 | `T.(C).f(...)` |
| Property の get | `C.p.get(receiver)` | `T.(C).p.get(receiver)` |
| Property の set | `C.p.set(receiver, value)` | `T.(C).p.set(receiver, value)` |

```kimi
contract Apply
    func apply(! value: i32, self: ref/Self)

Apply.apply(target@ref, value: 1)     // self は宣言では末尾でも、先頭に置く
Iterable.iterate(values@ref)
Iterator.next(iterator@uniq)          // iterator は所有する var 局所値
T.(C).create()
```

**receiver 先頭の規則**

- receiver を明示するすべての呼出し・関数参照（上表の形式と通常の `Type.method`）で、receiver を先頭の無名位置に置く。残りの引数は、`self` を除いた仮引数の順序、ラベル、`!` 境界で照合する。`self:` ラベルはなく、receiver は名前省略の境界に数えない。
- Signature、Function Item、関数値の型、呼出し規約の引数順序も receiver 先頭に正規化する。`self` の位置だけが異なる overload は区別せず、関数参照に並べ替えの adapter は要らない。宣言内の束縛・cleanup の順序は宣言どおりである。
- receiver のない関数の順序は変えず、bound-method 値は追加しない。
- accessor の明示形は表の位置引数だけを取り（ラベル・default・`!` なし）、呼出し専用である。

**Self の推論と評価**

- Self は receiver 型と先頭の実引数から一意に決め（§3.2）、その適合を検証する。期待型だけからは推論せず、適合の有無で解を順位付けせず、変換も探索しない。例えば `Iterable.iterate(values)` の Self を `ref/Array<E>` に変えない。所有する配列なら `@move`、共有列挙なら `@ref` と書く。
- C に独立した同名要求が残る場合は §5.2 の候補規則を使う。receiver は overload の優劣比較に使わない。
- 全実引数を通常の引数として、記述順に一度ずつ評価・取得する。receiver も Receiver Expression ではなく、所有する Place の新しい排他借用には `@uniq`／`@objuniq` が要る。
- `T.(C).f` は Self を固定した要求参照であり、要求の型・Origin・効果・実装対応を保つ。関数値では全引数を位置で渡し、実装側の default や名前省略許可は伝播しない。

### 5.2. 層ごとの名前 lookup

値を receiver とする関数呼出しと Property の get／set は、完全型 R から次の順で名前を探す。

1. 現在の層の通常メンバー（アクセス可能で役割が合うもの）。
2. なければ、その層の型に対する公開適合・総称制約の同名の要求。重複経路は §4.3.1 でまとめ、適合条件と Origin の証明は確定後の義務として残す。
3. どちらもなく、現在の層が安全な `ref/U`／`uniq/U` なら U で繰り返す。それ以外はエラーとする。

- 名前が見つかった層で確定し、その層が Self を決める。確定後の失敗（適合条件、overload、receiver の取得、Loan）で、別の層や適合へ戻らない。総称本体では宣言時の証明環境で確定し、具体化後にやり直さない。
- 借用を辿るときは既存の receiver 取得表に従い、経路の権限を超えない再借用・Copy read を使う。共有経路から排他権限を回復しない。object と生ポインターは既存規則のままとする。
- 型を経由する参照、Type 関数、`T.(C)` は T 自身で探し、借用層を辿らない。`for` は §7.3 のとおり完全型の適合を使う。
- 関数の候補は、receiver shape が一つであることを確認してから overload 選択を行う。Property は get／set の契約全体で一つを選び、setter の有無や期待型で選び直さず、別の要求の getter と setter を組み合わせない。
- 独立した同名要求を一候補にまとめるのは、操作契約と実装対応の同値性をすべての束縛で証明できる場合だけである。それ以外は明示形で選ぶ。

```kimi
func advance<I>(it: uniq/I) -> Option<I.Element>
    I is Iterator

    return it.next()    // uniq/I の層に next はないため、I の層の要求を使い、参照先を排他再借用する
```

`uniq/I` 自身に next の要求があればその層で確定する。その receiver が `uniq/Self` なら、it の参照スロットへの排他借用（it の変更権限）が必要になる。

### 5.3. receiver の取得と Property

§5.2 で確定した層の完全型を Self に代入して、取得を計画する。適合を探すための借用は行わない。

| 要求の receiver | 取得 |
| --- | --- |
| `Self` | 完全型の値を渡す。一時値はそのまま、所有する Place は Copy か明示 Move、借用値は Copy か再借用。明示 Move は参照自体を移し、参照先の Copy 読みに置き換えない |
| `ref/Self` | Self を保持する位置への共有借用 |
| `uniq/Self` | Self を保持する位置への排他借用。位置の変更権限が必要 |

- Self が参照型でも層を省略しない。明示形で参照スロットそのものを借りるには、`@uniq/Self` に相当する完全な対象型を書く（`@uniq` は参照先の再借用）。object receiver は既存の条件のままとする。
- Property 要求は accessor の呼出しであり、格納場所を公開しない。`value.p`、`value.p = rhs`、明示 accessor は、要求の型・権限・Loan と getter 結果の一時値制限を保つ。直接の格納操作へ lower しても、隠れた Field の Move や排他借用は許可しない。receiver 省略形と witness bridge は既存規則による。
- 候補の検討中は値を取得せず、確定した計画を通常の予約・活性化・cleanup 規則で実行する。メソッド形式は receiver を先に、明示形は記述順に評価する。Property 代入は右辺先行なので、`C.p.set(receiver, value)` とは順序が異なり得る。

## 6. Subject の取得とパターン

### 6.1. 共通取得

`for` と `match` は Subject の式を一度だけ評価し、パターンや適合によらず次の表で取得する（上の行を優先する）。

| 式 | 取得 |
| --- | --- |
| 明示的な `E@move` | 値を移動して取得する。借用値なら参照自体を移す |
| 所有する Place（Copy を含む） | その格納完全型への共有借用 |
| 共有借用値 | Semantics を保った Copy または共有再借用 |
| 排他借用値 | Semantics を保った排他再借用。親は移動せず、子の Loan が必要な間だけ競合する使用を禁止する |
| 借用以外の一時値 | 値として取得する |

- 括弧、局所変数、引数・戻り値を経由しても、借用の Semantics を弱めない。弱めるなら `x@ref`、参照自体を移すなら `x@move` と書く。
- 格納済みの `uniq` 値や `uniq` 引数の Subject は、従来の共有再借用から排他再借用に変わる。派生 Loan は arm 全体へ延ばさず、実際の使用と観測可能な cleanup に従う。
- この規則は `ref`／`uniq` と `objref`／`objuniq` に共通であり、`@uniq`／`@objuniq` と書いただけの Subject エラーは廃止する。object 形式の Iterable 適合や object payload の暗黙の分解は追加しない。
- 必要な適合がなければエラーとし、別の取得モードで再試行しない。

```kimi
for item in values          // 所有する Place を共有借用し、ref 適合を使う
    inspect(item)

for item in values@uniq     // uniq 適合による排他列挙
    update(item)

let access = values@uniq
for item in access          // 同じく排他列挙。access は移動せず、ループ後も使える
    update(item)
```

### 6.2. パターンの束縛

構造パターンは、各位置で `ref/U` か `uniq/U` を最大一層だけ辿れる（object Semantics と生ポインターは辿らない）。Binding／Wildcard は参照を辿らず、括弧は層を取り除かない。子の権限は通過した経路の権限を超えず、共有経路の内側の `uniq` から排他権限は回復しない。

位置の格納完全型を U とすると、body の束縛は次のように取得する。

| 束縛の位置 | 取得 |
| --- | --- |
| Subject 全体 | 取得済みの Subject の完全型を Copy／Move で引き継ぐ。参照層を加えず、隠れた Subject スロットに依存しない |
| 借用層を通らずに到達した部分 | 通常の Copy／Move |
| 共有の借用層を通って到達した位置 | その位置への `ref/U`（Copy 型でも） |
| 排他の借用層を通って到達した位置 | その位置への `uniq/U`（Copy 型でも） |
| Wildcard | 取得しない。既存の破棄責任を維持する |

- `let`／`var` は再代入の可否だけを決め、参照先の権限を増やさない。U が参照型なら、その参照スロットの借用になる。
- 共有パターンの Copy 要素は、従来の値から `ref/U` に変わる。値が必要な位置では既存の Copy read を使い、束縛の型と総称推論の結果は参照型のままである。

```kimi
match n                       // n: i32、所有する var。Subject は ref/i32
    let x => use(x)           // x: ref/i32（Subject 全体。ref/ref/i32 にはならない）

match slot                    // slot: Option<i32>、所有する var。Subject は ref/Option<i32>
    .Some(let v) =>           // 共有の借用層を通るので v: ref/i32
        let m: i32 = v        // 値の位置で Copy read
        slot = .None          // v の最後の使用の後なので有効
        record(m)
    .None => ()
```

**guard**

guard は候補を共有で読むだけであり、上の表の Copy／Move や排他借用は成功後の body でだけ行う。candidate は次のように読む。

| candidate の位置 | 読み方 |
| --- | --- |
| 借用層（共有・排他）を通って到達した位置 | その位置への `ref/U` |
| 借用層を通らない位置（値として取得した Subject の全体と内部） | 既存の shared reading（Copy 型は Copy、それ以外は `ref/U`） |
| 借用の Subject の全体 | 参照先の共有読み（`ref` は Copy、`uniq` は共有再借用）。隠れた Subject スロットへの層は加えない |

例えば `match packet@move` の Non-Copy の payload `Data` は、guard では `ref/Data`、成功後の body では `Data` になる。

- candidate と body の束縛は別の Identity であり、body の取得は成功した guard の cleanup の後に行う。失敗時は候補を保つ。candidate を読むための Loan は guard の外へ持ち出せない。その他の guard 制限と、Case の変更・全体更新に対する Loan 検査は維持する。

`for` の単一束縛は next の Some payload を値として取得し、タプル形式はその要素の型にこの節の分解規則を適用する。一般の match パターンは `for` に追加しない。

### 6.3. 書込みと寿命

- `let item: uniq/U` は参照の付け替えを禁じるだけで、参照先への排他アクセスは残る。`=` と複合代入の意味は変えず、参照先の更新には既存のメソッドや `Intrinsics.replace`／`exchange`／`swap` を使う。
- 借用した部分を未初期化にする Move は禁止のままである。借用値の `@move` は参照自体の移動であり、参照先の所有権は移さない。
- 借用した Subject から得た結果は、隠れた局所スロットではなく、元の referent と Loan に依存する。所有する Subject の内部への新しい借用は、その Subject の破棄を越えて逃がせない。格納済みの外部参照の Copy／Move は元の依存を保つ。

## 7. Iterator／Iterable

### 7.1. 要求

```kimi
public contract Iterator
    associate Element

    func next(self: uniq/Self) -> Option<Self.Element>

public contract Iterable
    associate Cursor is Iterator

    func iterate(self: Self) -> Self.Cursor
```

- Iterable の関連型を `Iterator` から `Cursor` に改名する。導出で Iterator 型は Iterable にもなるため、旧名では `I.Iterator`（関連型）と `I.(Iterator)`（contract）が別物を指し、読み誤りやすい。
- Iterable の Element と旧等式は削除する。T の要素型は `T.(Iterable).Cursor.(Iterator).Element` と定義し、`for` もこの要求に直接結び付ける。短縮形 `T.Cursor.Element` は、通常メンバーに遮られず一意な場合の書き方にすぎない。
- `iterate` は適合対象の完全型の値を受け取り、`next` は iterator の完全型を排他借用する。列挙対象、iterator、要素の Semantics は互いに独立である。
- Element は next の外側で束縛され、呼出しごとの receiver Origin を参照できない。既存の外部 source への借用は返せるが、next の一時借用や iterator 所有の記憶域への借用は返せない。lending iterator は導入しない。

**導出**

- `I is Iterator` から `I is Iterable` を導く。`I.(Iterable).Cursor` は `I` であり、iterate は I をそのまま返す。これにより `for x in it@move` で iterator を直接消費できる。重複の扱いは §4.4.1 に従う。
- `uniq/I is Iterator` は導出しない。導出すると外側の next が §5.2 の lookup を遮り、`it.next()` に参照スロットの変更権限を要求してしまう。途中までの列挙は、§7.5.3 のように参照を保持する型へ委譲する。Cursor に借用型を指定する場合は、その完全型自身の Iterator 適合が必要である。

### 7.2. 標準型の適合

組み込みの列挙モード選択表と、Copy view 読みの代替経路を廃止し、次を通常の適合として要求する。E、K、V は完全型、a は列挙対象の借用 Origin、source は Slice の背後の記憶域の Origin である。

| 型 | 所有する値の Element | `ref` 適合の Element | `uniq` 適合の Element |
| --- | --- | --- | --- |
| `Array<E>`、`[N of E]` | `E` | `ref/E during a` | `uniq/E during a` |
| `Dictionary<K,V>` | `(K,V)` | `(ref/K during a, ref/V during a)` | `(ref/K during a, uniq/V during a)` |
| `Slice<E>` | `ref/E during source` | 同左 | 同左 |
| `ResolvedRange` | `isize` | `isize` | `isize` |

- 順序は、Array／固定配列が添字順、Dictionary が挿入順、ResolvedRange が既存の区間順である。
- Range は Iterable ではない。object 形式と生ポインターへの標準適合は追加しない。
- 借用する配列 iterator の source は a であり、Element も a を保つ。所有する配列 iterator は残る要素を所有する。
- Slice／ResolvedRange の iterator はハンドル・区間の内容を取得して使い、ハンドルを借りた局所 Origin は不要なら結果へ残さない。Slice の実際の source は必ず保つ。
- Dictionary のキーは排他列挙でも共有参照である。`uniq/Slice<E>` はハンドルへの排他アクセスであり、要素を排他にはしない。
- iterator の公開名や構築子は要求しない。利用者は関連型の射影を使う。

列挙モードは collection 自身の外側の Semantics で決まり、要素の Semantics とは独立である。例えば `E = ref/Dog during b` のとき、Element は次のとおりである。

```text
消費: ref/Dog during b
共有: ref/(ref/Dog during b) during a
排他: uniq/(ref/Dog during b) during a    // Dog への排他アクセスは与えない
```

#### 7.2.1. 標準借用 iterator の性能

上表の ref／uniq 適合と Slice の iterator に、次を要求する。型を固定し、n は開始時の要素数（Dictionary では capacity ではなく生きた entry 数）とする。

| 操作・資源 | 保証 |
| --- | --- |
| iterator の生成・破棄 | O(1) |
| next | 償却 O(1)。空・終了後は O(1) |
| 最初の None までの全走査 | O(1 + n) |
| iterator 自身の追加記憶域 | O(1) |
| 列挙のためのヒープ割当て・参照カウント更新 | なし |

- 要素参照の配列を事前に作らない。Dictionary は開始時に capacity 全体を走査しない。
- 利用者が保持する要素結果と body の処理は別に数える。所有する iterator の要素移動・破棄は既存の計算量と責任に従う。この保証はユーザー定義 Iterator には要求しない。
- Dictionary の remove の割当て禁止と記憶域再利用の保証も維持する。非規範の実現例として、既存の O(capacity) の管理領域に生きた entry の挿入順リンクと空き位置のリストを持ち、削除でリンクから外し、再利用で末尾へつなぐ方式がある。

### 7.3. `for` の処理

1. §6.1 で式を一度取得し、隠れた iterable 局所値に置く。
2. その完全型の Iterable 適合で iterate を一度呼び、局所値を `@move` 相当で渡す。借用値なら参照と権限を移し、参照先は消費しない。
3. 結果を、更新可能な隠れた iterator 局所値に置く。
4. iterator の完全型自身の Iterator 適合を使い、その格納スロットへの短い排他借用で next を呼ぶ（§5.3）。
5. Some なら要素（§7.1 の要素型）を取得して body を実行し、最初の None で終える。
6. cleanup は通常のスコープ終了規則に従う。break、return、try 伝播では未取得の所有要素を既存の順序で破棄し、借用 iterator は元の要素を破棄しない。Abort では巻戻しを保証しない。

- 同名メソッドを探す duck typing は行わない。
- 一般の Iterator に「None の後は常に None」を要求しない。Kimi の標準 iterator は終了後も None を返す。

### 7.4. 要素の排他借用と分離

標準の Array／固定配列／Dictionary の uniq 適合は、§3.5 と §9.4 に従い、次のように分割・移譲する。

1. iterate は受け取った排他権限を iterator の状態へ移す。状態は順序と未取得の領域を持つ。
2. next は未取得の先頭要素を検証済み操作で分離し、残部を状態へ、分離した権限を Some の要素へ渡す。参照を公開する前に、cursor と権限の移譲を確定する。
3. 要素がなければ None を返す。渡した領域を再取得せず、cursor の巻戻しで権限を戻さない。

- next の短い receiver 借用は iterator の状態を守り、返す要素は外部 source に依存する。このため、前の結果を保持したまま次の next を呼べる。
- Dictionary は entry のキーと値を分離し、キーには共有、値には排他のアクセスを渡す。
- iterator の破棄は残る権限を終えるだけで、要素や collection を破棄しない。保持された結果は source の保護を保つため、collection 経由の競合する読み書き、構造変更、再確保、移動、破棄は拒否される。cleanup の検証も同じである。
- 一般の安全な動的添字分割や、排他 Slice の公開 API は追加しない。ユーザー型は標準の操作への委譲で排他列挙を提供でき、独自実装にも同じ Loan の契約を要求する。

### 7.5. 例

#### 7.5.1. collection への委譲

```kimi
public struct Bag<E>
    var items: Array<E>

    public init(items: Array<E>)
        self.items = items@move

    ref/Self during source is Iterable
        associate Cursor is (ref/Array<E> during source).(Iterable).Cursor

        func iterate(self: Self) -> Self.Cursor
            return Iterable.iterate(self.items@ref)
```

具体的な iterator 名や、Origin スロット付きの関連型は要らない。

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

for item in values@uniq         // values: Array<i32>、item: uniq/i32
    Intrinsics.replace(item, with: 0)
```

- `count(values@ref)` と `count(values@uniq)` は借用値を、`count(values@move)` は所有する配列を渡す。`source@move` は X の値を移すだけであり、X が借用型なら元の collection は消費しない。
- `@ref/C` は、C が借用型でも格納値全体を借りる。`@ref` だと参照先の再借用になり得るため、総称本体では層を明示する。

#### 7.5.3. iterator を借用して列挙する

説明用のユーザー型であり、標準 API ではない。

```kimi
struct BorrowingIterator<I> {source}
    I is Iterator
    let inner: uniq/I during source

    public init(inner: uniq/I during source)
        self.inner = inner@move

    Self is Iterator
        associate Element is I.(Iterator).Element

        func next(self: uniq/Self) -> Option<Self.Element>
            return I.(Iterator).next(self.inner)

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

    Self is Iterable
        associate Cursor is ResolvedRange.(Iterable).Cursor

        func iterate(self: Self) -> Self.Cursor
            return ResolvedRange.(Iterable).iterate(self.range)

    ref/Self is Iterable
        associate Cursor is Span.(Iterable).Cursor

        func iterate(self: Self) -> Self.Cursor
            let value: Span = self               // Copy read
            return Span.(Iterable).iterate(value@move)
```

`ref/Self` の適合がない型は、Copy 型でも `value@move` か所有する一時値で列挙する。取得モードを切り替えて適合を探すことはしない。

## 8. 既存機能との境界

- `T is C` から `ref/T is C`、`uniq/T is C` を一般には導出しない。標準型の明示的な適合と、言語が個別に定める導出だけを使う。
- Copy／Owned／Callable／Sealed／ObjectPayload の固有の条件と効果は維持する。適合の構文で、intrinsic が許さない実装を登録することはできない。
- Equatable／Comparable の借用・タプル合成は、既存の対象と条件を持つ個別の導出として維持する（重複は §4.4.1）。
- 演算子が operand を借用して操作することと、その借用型が contract に適合することは別である。比較の operand 取得、浮動小数点の意味、built-in の優先順位は変えない。
- 次は導入せず、その設計を暗黙に確定しない: runtime contract View、object erasure、任意の外部適合、適合の優先順位、default 実装、関連型の独自の型／Origin 引数、lending iterator、参照を通した新しい代入規則。

## 9. 検証要件

### 9.1. 定義と保存

- 総称本体は宣言時の証明環境で検証し、具体化で欠けた前提を補わない。どの経路（具体型、総称引数、関連型）を経ても、同じ完全型は同じ Semantics と Origin の保証を持つ。
- 適合の登録キーと完全な証明情報を分けて保つ。Origin を除いたキーでは、候補と実装対応のテンプレートだけを共有する。Origin 条件、Owned、完全な関連型、使用計画は実際の証明環境で検証し、候補情報を成立の証明に流用しない。検証結果は、必要な完全な環境が一致すればキャッシュしてよい。
- 導出は規則として保ち、使用や重複判定で必要になった対象についてだけ具体化してよい。入れ子の借用型などへの結論を事前に列挙しない。
- 要求の束縛済み環境、実装対応、Origin の量化・条件、Loan の分割・移譲、アクセスと効果を、保存・再読込み・無効化でも保つ。別コンパイル先の private な本体を調べずに、公開契約と検証済み情報だけで使用を判定できなければならない。

### 9.2. 生成と最適化

- 静的適合の選択と Origin に、実行時の型タグ、寿命タグ、割当てを必須としない。
- Origin だけが異なる具体化は、選んだ実装、検証済みの操作、配置・ABI などの生成条件が一致すればコードを共有できる。
- 最適化は、取得順序、Loan、破棄責任、診断すべき違反を変えてはならない。標準コレクションの既存の計算量・割当て保証を維持する。

上の条件と、型・寿命・aliasing・評価・cleanup の保証を保てる場合は、次の最適化を許す。

| 対象 | 最適化 | 追加の条件 |
| --- | --- | --- |
| `ref/(ref/T)` | 内側の参照値を直接渡す | 外側スロットのアドレスが観測されない。ABI の合意か adapter が必要。外側スロットの非 alias 保証を参照先へ転用しない |
| Copy の Subject、パターンの Copy 要素、借用先の値 | レジスターなどに保持し、不要な load／store を除く | 束縛の型、Loan、更新先、観測可能な読み書きと終了時の動作を変えない。排他借用先への更新は元の位置に反映する |
| `for` の隠れた iterable 局所値 | iterate への移動と結果の配置を同じ記憶域で行う | 観測可能な差がない（固定配列の消費列挙で要素を二度移さない） |

### 9.3. 診断

- 型形成の失敗、同値性の未証明、適合の欠如・衝突、関連型の不足・曖昧性、実装の非互換、使用時の Loan 違反を区別する。使用違反を「別の適合が必要」と誤って扱わない。
- 取得した型に適合がない `for` では、成立する書き方がある場合に限り提示する。例えば所有する iterator の Place には `it@move` を、途中までの列挙には借用する型への委譲を示す。
- 未使用の名前付き Origin の lint は任意とする。`Self`、関連型、条件を通した暗黙の使用も数え、lint は適合の成立を変えない。

### 9.4. Loan 分割の検証モデル

分割には、既存の構造的な非重複の証明か、実装まで検証された記憶域操作が必要である。`splitLoan(L, A)` はその検証モデルであり、特定の内部命令や公開 API を要求しない。L のアクセス領域を R とする。

| 段階 | 検証・保証 |
| --- | --- |
| 前提 | L の有効な排他権限。A は分割可能な R の部分領域で残部と重複しない。範囲・初期化・配置・provenance も検証する |
| 移譲 | R 全体への権限を消費し、A と R ∖ A に別々の権限を渡す。消費した L を再使用しない |
| 保持 | 両方に元の保護元・Origin・必要な親 Loan を保ち、元の権限・有効期間を超えない |
| 終了 | 一方の終了で他方の保護を消さない。元への競合するアクセスは、必要な派生 Loan の終了まで拒否する |

- 全部の移譲は A = R とする。空の領域やサイズゼロの要素も論理的な位置で区別し、アドレスだけで重複を判断しない。分割自体は値の Move や未初期化化を伴わない。
- 標準の排他列挙には、残部の先頭要素を分離して安全な参照を返す検証済み操作を用意する。境界・位置・provenance を検証し、cursor と権限の更新を確定してから参照を返し、結果の Origin を元の source に接続する。
- 動的添字の比較、Origin 注釈、Unsafe 指定だけでは証明にならない。未定義の生ポインター変換 API も前提にしない。
- 呼出しの検証情報は、入力から消費する権限と、結果・更新後の状態へ渡す領域・保護元を保つ。通常の receiver 保護も維持する。
- 同じ保証を証明できる別のモデルを使ってよい。Loan の実行時タグ、要素ごとの割当て、一般の整数定理証明は要求しない。

### 9.5. 成立例と拒否例

#### 9.5.1. 型と束縛

| 観点 | 成立させる | 拒否する |
| --- | --- | --- |
| 型比較 | Origin だけが異なる借用は同じ型 Identity | Origin の同値性なしに `T is U` を成立させ、完全型を置換する |
| 束縛 | 総称引数・関連型・別名を経ても内外の Origin を保つ | 射影や代入のたびに再束縛する、`static` を補う |
| 呼出しの Self | `it.next()` で Self を I のまま保ち、外側の借用だけに新しい Origin を与える | 再借用した値に元の長い Origin を付ける、固定済みの Self を再束縛する |
| 制約の量化 | `ref/C is Iterable` を実際の借用 Origin で使う | 節の Origin を本体へ持ち出す、既存 Origin を再量化する |
| 型等式の補完 | `X.Cursor.Element is ref/i32` が左辺の Origin を保つ | 構造の不一致を補完で許す、未束縛名を存在変数にする |
| Semantics の合成 | `uniq/(ref/T)` は参照スロットへの排他アクセス | それを T への排他アクセスへ平坦化する |

#### 9.5.2. 適合と要求

| 観点 | 成立させる | 拒否する |
| --- | --- | --- |
| 適合 | 同じ Array の owner／ref／uniq に別々の適合 | Origin や `when` だけが違う重複した直接適合 |
| 導出 | Iterator から Iterable を導き、借用列挙はラッパーへ委譲する | 導出の結論と同じキーの独自適合、`uniq/I` の Iterator 適合の暗黙生成 |
| 関連型の指定 | 適合ブロックごとに Cursor を指定する | 型本体の `associate C.Cursor is U` で複数モードの指定を兼ねる |
| 要求の識別 | 同じ宣言・同値な環境への経路だけをまとめる | 外側の型引数や Origin が異なる関連型を短縮名で一つにする |
| 修飾名 | `T.(C).N` で contract を明示し、`T.N` は通常メンバーを先に探す | contract の追加で `T.N` の意味を変える、短縮形の複数候補を結果型が同じという理由でまとめる |
| refinement | Left／Right を別々に実装し、Both で再利用する | 同一要求への経路で関連型や実装対応が一致しない |
| 実装互換 | 要求の任意の入力 Origin を受け入れる | `static` だけを受け入れる実装で一般の借用要求を満たす |

#### 9.5.3. 呼出しと lookup

| 観点 | 成立させる | 拒否する |
| --- | --- | --- |
| 層ごとの lookup | `it: uniq/I` から I の next を呼ぶ。R 自身に同名要求があれば R を使う | 確定後の失敗で参照先へ戻る、共有経路で排他権限を回復する |
| 借用型の iterator | 借用型自身の適合の next は参照スロットを借りる | `for` の next をメソッド lookup に置き換え、参照先の適合で代用する |
| 明示呼出し | `Iterable.iterate(values@ref)` | 適合がないため owner から ref へ探索し直す |
| receiver 先頭 | self が末尾でも `Type.method` と `T.(C).f` は receiver が先頭。関数値の型も同じ | `self:` ラベル、self の位置だけが違う overload、実装側の名前省略許可の転用 |
| Property | 通常メンバーにない要求を get／set し、明示形で曖昧性を除く | 別の要求の getter と setter を組み合わせる、明示 get で一時値制限を回避する |
| 総称使用 | count を三つの取得形で使う | 宣言時に証明できない要求を、好都合な具体化だけで許す |

#### 9.5.4. Subject・パターン・列挙

| 観点 | 成立させる | 拒否する |
| --- | --- | --- |
| 排他 Subject | 直接の `@uniq` と保存した uniq 値で同じモード | 保存しただけで ref へ弱める |
| 排他パターン | guard は共有で読み、成功後に payload を排他借用する | guard の候補から権限を持ち出す、共有経路から排他へ強める |
| 共有パターン | Copy 要素も `ref/U` で束縛し、値の位置で Copy read。Subject 全体は Subject の型のまま | 束縛を値型にして Loan を消す、全体束縛に隠れたスロットへの参照層を加える |
| Copy の列挙 | ref 適合からコピーして所有型の適合へ委譲する | 所有型の適合だけで共有 Subject を列挙する |
| 非 lending | Element が外部 source に依存する | iterator 所有の記憶域を next の借用より長く返す |
| 分離 | 前の要素を保持して次の next を呼ぶ（総称・別コンパイル・委譲でも） | 同じ要素を二度排他的に返す、分割前の権限を再使用する |
| 終了・保持 | iterator の破棄後も要素に必要な Loan が残る | 要素を保持したまま collection を再確保・破棄する |
| 標準 view | Slice の source を保ち、局所ハンドルの借用は不要なら終える | `uniq/Slice` から要素の uniq 権限を作る |
| Dictionary | 排他列挙で値を更新し、キーは共有参照 | 生きた entry のキーを排他列挙で変更する |
| 境界と cleanup | 空・サイズゼロの要素、途中終了、未取得要素を一度だけ破棄 | 物理アドレスだけで分離を判定する、二重破棄・破棄漏れ |
| 性能 | 疎な Dictionary も要素数に比例する走査、O(1) の生成・記憶域 | capacity 全体の事前走査、参照配列の生成、列挙用のヒープ割当て |
| キャッシュ・生成 | Origin ごとの証明を保ち、生成条件が同じコードを共有する | Origin を除いた候補を成立証明にする、非 alias 保証を参照先へ転用する |

## 10. 正式仕様へ取り込む際の担当箇所

将来の反映先であり、本書の作成では変更しない。

| 反映先 | 内容 |
| --- | --- |
| [第3章](../../spec/03-types-and-values.md) | 完全型の構成、三つの判断、Core と直接の対象の区別 |
| [第6章](../../spec/06-declarations-and-containers.md)、[第8章](../../spec/08-generics-constraints-and-contracts.md) | 完全型の制約、適合と Self、登録キーと導出の結論、refinement、継承。§8.4.3 の Core 制限と型本体の関連型指定を廃止 |
| [第7章](../../spec/07-functions-and-callable-values.md)、[第9章](../../spec/09-names-signatures-and-access.md)、[第10章](../../spec/10-overload-resolution-and-inference.md) | `T.(C).N` と `T.N` の区別、層ごとの lookup、receiver の取得、公開範囲。`Type.method` を含む明示呼出し・関数参照・Signature・呼出し規約の receiver 先頭 |
| [第11章](../../spec/11-properties.md) | 要求 Property の選択、明示 accessor、一時値制限、witness bridge との照合 |
| [第15章](../../spec/15-ownership-and-lifetime-analysis.md) | 新しい Origin と省略、呼出しでの Self と借用 Origin、依存、Subject とパターン、Loan の保護元と領域、分割・移譲と検証モデル |
| [第14章](../../spec/14-control-flow.md)、[第16章](../../spec/16-scope-exit-and-destruction.md) | for／match の共通取得、要素の取得、guard、終了時の責任 |
| [第4章](../../spec/04-arrays-indexing-and-slices.md)、[第22章](../../spec/22-core-execution-and-foreign-functions.md) | Iterable の Element と旧等式の削除、Cursor への改名、Iterator からの導出、標準適合、要素分離、計算量・割当て保証 |
| [第13章](../../spec/13-operators-and-assignment.md) | 比較適合の個別導出と operand 取得の区別。代入の意味は維持 |
| [第18章](../../spec/18-modules-and-dependencies.md)、[第21章](../../spec/21-layout-runtime-and-code-generation.md) | 証明情報の保存・無効化、キャッシュと生成キー、コード共有、§9.2 の最適化条件 |
| 付録 A／D／E／F | 検証例、解消する境界、用語、構文 |

取り込み時は本文の担当節を定義元とし、他の節ではその適用と参照を記述する。例・標準宣言・milestone のソースは、格納済み uniq の Subject、共有パターンの束縛型、明示 receiver の順序、修飾名 `T.(C).N`、`Cursor` の名前を含めて同時に整合させる。実装済み範囲は実際の検証後に STATUS.md へ記録し、本書だけを根拠に対応済みとは扱わない。
