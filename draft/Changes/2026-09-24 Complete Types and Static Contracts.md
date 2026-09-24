# 完全型と静的 contract の統一

日付: 2026-09-24

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

Runtime Object Type Identity、overload の Signature、生成コードの共有キーは、既存の目的別の定義を維持する。型の同一性の一致だけで、実装や生成コードを共有してはならない。

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
| 関数・要求の新しい入力借用 Origin | 呼出しごと。既存の署名の量化規則に従う。 |
| 既に完全型に含まれる Origin | 元の束縛を保持する。 |
| 関連型の指定 | 外側の束縛、`static`、完全型の依存と明示的な関係から完成させる。独立した自由な Origin を追加しない。 |

適合ヘッダーでは、通常の入力借用注釈と同様に、未束縛の単純名を普遍的な Origin として導入できる。省略された外側の借用 Origin は独立した匿名入力になる。既に見える名前はその束縛を参照し、内側の借用や未知のスキーマに新しい省略許可を与えない。

`origin` 関係節そのものは名前を導入しない。`during` を持てる Semantics、集合名、射影、宣言スキーマの既存規則を維持する。Origin を持たない層に架空の `static` を追加しない。

### 3.3. 依存と Loan の保持

Origin は有効期間を、Loan は領域・権限・借用元を表す。Origin の等式や短縮で Loan を統合・消去したり、排他権限を作ったりしない。

構造が不透明な関連型も、依存する Origin、Loan 要件、検証に必要な関係を保持した完全型である。情報が不明なことは Owned や無依存の証明にならない。具体構造に依存する合法な義務は、既存の期限まで保持する。

射影元に現れた Origin を、射影結果へ無条件に追加しない。射影元の有効性の検証と、選ばれた関連型が実際に保持する依存は別である。一方、結果型の型引数・外側環境などに本当に含まれる依存は、未使用でも既存規則どおり保持する。

元の型を寿命短縮できることから、その関連型も同じように短縮できるとは推論しない。分散と使用上の適合性を結果型について検証する。

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

適合ヘッダーの `Self` は囲む型を表す。適合ブロックは新しい `Self` 束縛を導入し、ブロック内では適合対象の完全型を表す。要求側の `Self` にも同じ完全型を代入する。

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

### 4.3. 関連型と射影

関連型は完全型を明示的に束縛する。要求の制約・祖先の指定から一意に決まる場合は再指定を要しない。関連型の独自の総称パラメータや Origin スキーマは追加しない。

`T.C.Element` を完全な射影、`T.Element` を利用可能な要件の中で宣言が一意な場合の短縮形とする。同じ宣言への複数経路は一つとして数え、独立した同名宣言は混同しない。

Semantics を適用した型全体を射影元にする場合は括弧を使う。

```kimi
(ref/Array<E> during source).Iterable.Iterator
```

これは `ref/(Array<E>.Iterable.Iterator)` とは異なる。既存の prefix／suffix の構文を維持し、名前解決で都合のよい方へ再解釈しない。Origin を含む各 qualifier の形成・アクセス・条件を検証する。

射影元になれることは、Core 専用の役割を与えるものではない。例えば借用型の関連型に `.init` を付けても、借用値の構築子は生成されない。

### 4.4. 一意性、証明、継承

適合の同一性は、対象の型 Identity と、contract 宣言およびその束縛済み環境の Identity の組で決める。すべての Semantics と型引数を保持し、Origin は実装を区別するキーにしない。完全な Origin 束縛と条件は適合の証明に保持する。

直接適合は、断片の併合後に、同じ組へ一致し得る宣言を重複として拒否する。別の Origin、異なる `when`、条件の強弱・排他性で重複を許可しない。既存の限定された構造の照合で非衝突を証明できなければ、衝突の可能性があるものとする。

直接適合、contract の refinement、有効な基底型からの継承が同じ適合へ到達する場合、同時に成立し得る経路の関連型と実装対応は一致しなければならない。関連型の一致には完全な型記述の同値性を要求する。

適合は定義時に、囲む型と公開条件が許すすべての束縛について検証する。選択された実装対応は保持し、具体化や呼出し側の名前解決で選び直さない。宣言中・循環中の適合だけを根拠に、自分自身の成立を証明してはならない。

基底型からの継承は既存の完全な要求照合と許可された receiver 投影に従う。Semantics の自動変更、所有する値の slicing、関連型の再選択は追加しない。

### 4.5. 実装の対応と公開範囲

要件を識別する名前、関数の種類、総称スロット、引数の順序・ラベル、receiver と引数の正規化した型構造は、既存の実装照合規則を使う。Origin は識別後の互換性検証に残す。

ブロックに同名の実装グループがある場合はそこで照合を確定し、失敗しても通常メンバーへ戻らない。グループがない要件は、既存の通常メンバー照合または明示された intrinsic 導出で満たせる。ブロックなしの適合宣言も引き続き使える。

実装は、要求が許すすべての入力を受け入れ、要求以上の結果保証を提供する。強い Origin 前提、追加の Constraints、Unsafe な呼出し条件などを隠れて要求しない。Property の保証と既存の許可された witness bridge も維持する。

適合の公開範囲は、対象型、contract、その束縛と条件が満たす有効アクセス範囲で決める。ブロック内の要求実装はその範囲に従い、独立したアクセス修飾子を持たない。公開適合から呼べることは、通常メソッドとして公開されることではない。

実装から private な補助関数を呼ぶ場合は、通常の宣言位置のアクセス規則を使う。外側の通常メンバーを直接対応付ける場合の既存のアクセス検証は省略しない。

## 5. 要求の呼出し

### 5.1. 明示形

```kimi
Iterable.iterate(values@ref)
Iterator.next(iterator@uniq)  // iterator は owner Semantics の var 局所値。
T.C.create()
```

基本形を `C.f(...)` と `T.C.f(...)` とする。`C` は束縛済みの contract 参照、`T` は完全型であり、必要な場合は §4.3 の括弧を使う。

- `C.f(...)` は receiver を持つ要求に使う。要求の receiver 型と実引数から `Self` を一意に決め、その適合を検証する。
- `T.C.f(...)` は `Self` を `T` に固定する。Type 関数に使え、receiver を持つ要求にも明示的な receiver 引数を渡して使える。
- `Self` を結果の期待型だけから求めない。曖昧な解を適合の有無で順位付けしない。
- receiver 以外の関数総称引数、overload、引数適合は既存規則に従う。適合を探すための追加の変換探索は行わない。

明示形では receiver も引数として書く。宣言の `self` の位置に無名の引数を一つ供給し、それ以外の引数のラベル・`!` 境界は要求に従う。`self` は名前省略境界の通常引数に数えず、`self:` という引数ラベルは導入しない。

引数は通常の引数位置の取得・評価順序に従い、この形式には暗黙の Receiver Expression を設けない。所有する Place の新しい排他借用には `@uniq`／`@objuniq` が必要である。既存の借用値は通常どおり再借用できる。

`Iterable.iterate(values)` の `Self` を、適合を成立させるために `ref/Array<E>` へ変更してはならない。Non-Copy の所有する配列を渡すなら `@move`、共有列挙なら `@ref` と書く。

関数参照では、`T.C.f` のように適合対象を確定し、要求の署名と検証済み対応を保持する。実装側の default や引数名省略許可は、要求経由の呼出しへ伝播しない。

### 5.2. メソッド形式と選択の確定

`value.f(...)` は通常メンバーの既存の lookup を先に行う。有効な通常メンバーグループが見つかればそこで確定し、適用不能・Loan 違反などから contract を探し直さない。

通常メンバーが見つからない場合は、その値の完全型について利用可能な公開適合または総称制約の要求を調べる。既存の overload 規則で要求が一意に決まれば呼出しを許し、独立した同名要求が曖昧なら明示形を要求する。同一要求の重複経路と、既存の「同じ署名・同じ実装対応」の証明による統合は維持する。

別の Semantics の適合を探すための借用は行わない。選択済みの要求が `uniq/Self` などを受け取る場合の receiver 取得は、通常のメソッド呼出し規則に従う。したがって `iterator.next()` は iterator 自身の適合の要求を使い、必要な排他借用を取得できる。

取得先は、完全な `Self` を代入した期待 receiver 型で決める。例えば Self 自体が参照型なら、`uniq/Self` は参照を保持するスロットへの借用である。借用値に対する `@uniq` 省略形へ機械的に置き換えて、参照先の再借用にしてはならない。明示形でこのスロットを借りる場合は `@uniq/Self` に相当する完全な対象型を指定する。

呼出し先と取得方法は静的に計画し、候補の検討中に値を取得しない。確定した計画を、通常の評価順序・予約・活性化・cleanup 規則で実行する。メソッド形式は receiver を先に評価し、明示形は通常の引数評価順序を使う。

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
| 共有の経路 | 既存の shared reading（Copy／共有 Borrow／Reborrow） |
| 排他の経路 | その位置への `uniq/U`。Copy 型でも位置の借用を保持 |
| Wildcard | 取得せず、既存の破棄責任を維持 |

`let`／`var` は得た値を保持する束縛の変更可否を決め、参照先の権限を増やさない。排他経路で `U` 自体が参照型なら、その参照スロットへの借用となる。

guard は従来どおり candidate を共有で読み、body 用の排他束縛は成功した guard の cleanup 後に作る。失敗時は候補を保持し、guard から権限を持ち出さない。Case の変更・全体更新と、payload の生きた借用との競合も既存の Loan 規則で拒否する。

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

### 7.3. `for` の処理

1. §6.1 で式を一度取得し、隠れた iterable 局所値を作る。
2. その完全型の Iterable 適合を選び、局所値を渡して iterate を一度呼ぶ。
3. 戻り値を更新可能な隠れた iterator 局所値へ置く。
4. iterator 自身の Iterator 適合を使い、その格納スロットへの短い排他借用で next を呼ぶ。iterator が借用型でも層を省略しない（§5.2）。
5. Some の要素を取得して body を実行する。最初の None で終了する。
6. 各反復とループ終了時の cleanup は、通常のスコープ終了規則に従う。

Iterable／Iterator の同名メソッドだけを探す duck typing は行わない。一般の Iterator に「一度 None を返したら永久に None」という追加の法則は課さないが、上記 Kimi iterator は終了後も None を返す。

break、return、try 伝播などの通常の制御移動では、未取得の所有要素を既存の順序で破棄する。借用 iterator は元の要素を破棄しない。Abort は既存どおり通常の巻戻しを保証しない。

### 7.4. 排他列挙の安全性

排他 iterator は、返した要素と今後扱う領域が重ならず、同じ要素の排他参照を重複して返さないことを保証する。返した要素を保持したまま次の next を呼べる。

この保証を、型注釈や iterator という名前だけから認めない。通常の所有権検証、一般に利用できる検証済みの借用分割・移譲操作、または責任を明示した Unsafe 実装で成立させる。実装は実際の記憶域と権限を結果の依存へ接続し、Origin の宣言だけで権限を作ってはならない。特定の Kimi iterator Identity だけを安全とする検査免除は設けない。

元の collection 全体には、iterator と保持された要素が必要とする保護を残す。返した領域と残りの領域の操作だけを、それぞれの分離された権限で許可する。元の collection 経由の競合する読書き、構造変更、再確保、移動、破棄は拒否する。

iterator の破棄で、保持されている要素の Loan を消してはならない。サイズゼロの要素も論理的な要素位置・領域で扱い、アドレスの不一致だけを分離の根拠にしない。一般の安全な動的添字分割や排他 Slice の新 API は、本書から暗黙に追加しない。

### 7.5. ユーザー型と総称関数の例

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

func count<X>(source: X) -> isize
    X is Iterable

    var result: isize = 0
    for _ in source@move
        result += 1
    return result
```

`count(values@ref)` と `count(values@uniq)` は借用値を、`count(values@move)` は所有する配列を渡す。関数内の `source@move` は X の値を移すのであり、X が借用型なら元の collection を所有・消費するものではない。

排他要素の更新も既存操作で記述できる。

```kimi
for item in values@uniq          // values: Array<i32>
    Intrinsics.replace(item, with: 0)
```

## 8. 既存機能との境界

`T is C` から `ref/T is C` や `uniq/T is C` を一般には導出しない。標準型の明示的な適合と、言語が個別に規定する導出だけを使う。

Copy／Owned／Callable／Sealed／ObjectPayload の固有の条件と効果は維持する。完全型への適合構文を使って、その intrinsic が許さない実装を登録することはできない。Equatable／Comparable の借用・タプル合成も、既存の対象と条件を持つ個別の導出として維持する。

演算子が operand を借用して操作することと、その借用型が contract に適合することは別である。比較の既存の operand 取得、浮動小数点の意味、built-in の優先順位を変えない。

runtime contract View、object erasure、任意の外部適合、適合の優先順位、default 実装、関連型の独自の型／Origin 引数、lending iterator、参照を通した新しい代入規則は導入しない。今回の静的規則は、これらの設計を暗黙に確定しない。

## 9. 検証要件

### 9.1. 定義・生成・保存

総称本体は宣言された証明環境で検証し、具体化によって欠けた前提を補わない。具体型・総称引数・関連型を経由した同じ完全型は、同じ Semantics と Origin の保証を持つ。

適合のキーと証明情報は分けて保持する。Origin を除いた型 Identity だけで Owned、適合の成立、関連型、呼出し計画をキャッシュしてはならない。要求 Identity、実装対応、Origin の量化・条件、依存する証明、アクセスと効果を、保存・再読込み・無効化でも保持する。

静的適合の選択や Origin に、実行時の型タグ、寿命タグ、割当てを必須としない。コード共有や最適化は、取得順序、Loan、破棄責任、診断すべき違反を変えてはならない。標準コレクションの既存の計算量・割当て保証を維持する。

診断では、型形成の失敗、同値性の未証明、適合の欠如・衝突、関連型の不足・曖昧性、実装の非互換、使用時の Loan 違反を区別する。使用違反を「別の適合が必要」と誤って扱わない。

### 9.2. 成立例と拒否例

| 観点 | 成立させる例 | 拒否する例 |
| --- | --- | --- |
| 型比較 | 同じ構造の借用は Origin が異なっても同じ型 Identity | Origin の同値性なしで `T is U` を成立させ、完全型を置換 |
| 完全型の束縛 | 総称引数・関連型・別名経由で内外の Origin を保持 | 射影や代入のたびに再束縛、または `static` を補充 |
| Semantics の合成 | `uniq/(ref/T)` は参照スロットへの排他アクセス | それを T への排他アクセスへ平坦化 |
| 借用型の iterator | next が iterator 値のスロットを借り、完全な Self を保持 | `@uniq` 省略形へ展開して参照先の別の適合を呼ぶ |
| 適合 | 同じ Array の owner／ref／uniq に別々の適合 | Origin や `when` だけが違う重複した直接適合 |
| refinement | 複数経路で完全な関連型と実装対応が一致 | Origin を除くと同じ、という理由だけで経路を併合 |
| 実装互換 | 要求の任意の入力 Origin を受け入れる | `static` だけを受け入れる実装で一般の借用要求を満たす |
| 明示呼出し | `Iterable.iterate(values@ref)` | 適合がないため owner から ref へ実装探索をやり直す |
| 総称使用 | 例の count を三つの配列の取得形で使用 | 宣言時に証明できない要求を、好都合な具体化だけで許可 |
| 排他 Subject | 直接の `@uniq` と、保存した uniq 値で同じモード | 保存したという理由で暗黙に ref へ弱める |
| 排他パターン | guard は共有で読み、成功後に payload を排他借用 | guard の候補から権限を持ち出す、共有経路から排他へ強化 |
| 非 lending | Element が既存の外部 source に依存 | iterator 所有の局所記憶域を next の短い借用より長く返す |
| 分離 | 前の要素を保持して別の要素へ next | 同じ要素を二度排他的に返す |
| 終了・保持 | iterator 破棄後も要素の必要な Loan が残る | 要素を保持したまま元の collection を再確保・破棄 |
| 標準 view | Slice の source を保持し、局所ハンドルの借用は不要なら終了 | uniq/Slice から要素の uniq 権限を作る |
| Dictionary | 排他列挙で値を更新し、キーは共有参照 | 生きた entry のキーを排他列挙から変更 |
| 境界と cleanup | 空・サイズゼロの要素、途中終了、未取得要素の一度だけの破棄 | 物理アドレスだけで分離を判定、二重破棄・破棄漏れ |

## 10. 正式仕様へ取り込む際の担当箇所

これは将来の反映先であり、本書の作成では変更しない。

| 反映先 | 内容 |
| --- | --- |
| [第3章](../../spec/03-types-and-values.md) | 完全型の構成、三つの比較・適合判断。Core と直接の対象の区別 |
| [第6章](../../spec/06-declarations-and-containers.md)、[第8章](../../spec/08-generics-constraints-and-contracts.md) | 適合ヘッダーとスコープ、Self、完全型の関連型、一意性、条件、継承、intrinsic の境界 |
| [第7章](../../spec/07-functions-and-callable-values.md)、[第9章](../../spec/09-names-signatures-and-access.md)、[第10章](../../spec/10-overload-resolution-and-inference.md) | 完全型の qualifier、明示呼出し、要求 lookup、receiver 取得、公開範囲 |
| [第11章](../../spec/11-properties.md) | 適合内の要求 accessor と既存 witness bridge の照合 |
| [第15章](../../spec/15-ownership-and-lifetime-analysis.md) | Origin の束縛時点、再束縛禁止、依存、Subject、排他パターンと領域の分離 |
| [第14章](../../spec/14-control-flow.md)、[第16章](../../spec/16-scope-exit-and-destruction.md) | for／match の共通取得、要素取得、guard、終了時の責任 |
| [第4章](../../spec/04-arrays-indexing-and-slices.md)、[第22章](../../spec/22-core-execution-and-foreign-functions.md) | 二つの contract、標準型の明示的な適合と Element、Source 依存 |
| [第13章](../../spec/13-operators-and-assignment.md) | 個別の比較適合導出と operand 取得の区別。代入の意味は維持 |
| [第18章](../../spec/18-modules-and-dependencies.md)、[第21章](../../spec/21-layout-runtime-and-code-generation.md) | 適合と完全な証明情報の保存・無効化、用途別 Identity と生成キー |
| 付録 A／D／E／F | 検証例、今回解消する境界、用語、構文 |

取り込み時は、本文の担当節を定義元とし、他の節ではその適用と参照を記述する。例・標準宣言・milestone のソースは同時に整合させる。実装済み範囲は実際の検証後に STATUS.md へ記録し、本書だけを根拠に対応済みとは扱わない。
