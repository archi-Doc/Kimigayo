# Declaration Container の制約緩和 — 最終仕様変更案

日付: 2026-09-17

本書で変更する事項は、[SPEC.md](../../SPEC.md) およびその参照先より優先する。変更しない事項には既存仕様を適用する。本書の確定は実装完了を意味しない。例示コードは、特記しない限り独立した例である。

## 1. 配置と基本原則

関連する型・要求・補助機能をまとめられるよう、`group` と `struct` に他の Declaration Container を置けるようにする。

| 宣言先 | 内側に置ける Declaration Container |
| --- | --- |
| プロジェクトルート、`group`、`struct` | `group`、`struct`、`enum`、`contract` |
| `enum`、`contract` | なし |

再帰的な配置を許可し、言語上の固定の深さ制限は設けない。実装資源の上限は資源診断で扱う。

```kimi
struct Parser
    public enum Result
        Success
        Failure

    private struct State
        var position: i32 = 0

    public group Diagnostics
        public struct Location
            var line: i32 = 0
```

ネストは宣言の所属関係である。コンテナーを置くだけでは保存領域を追加せず、内側の値に外側のインスタンスや receiver を暗黙に保持させない。所有権、型の継承、Copy、Conformance も自動的には伝播しない。

`extension` は導入しない。実行可能 Block と条件付き conformance の実装 Block にはネスト宣言を許可しない。`rootgroup` はソースルート直下だけに置ける。指令で選択されたルート項目も含むが、名前付きコンテナー内には置けない。ルートから group のパスを作り、途中の struct を合成・再定義しない。

## 2. 環境と Identity の共通規則

### 2.1. 環境継承

すべての内側の Declaration Container は、Contract を含め、一律に外側のジェネリック環境を継承する。途中の group は環境を遮断しない。runtime の変数・値・receiver は継承しない。

環境は次の三つに分けて扱う。

| 要素 | 内容 | 宣言参照の引数束縛に含めるか |
| --- | --- | --- |
| 引数束縛 | Type／Semantics の束縛、型引数内の Origin、明示的 Origin の束縛 | 含める |
| 証明の文脈 | 入力条件、Origin の境界条件、証明義務、有効な証拠とその依存先 | 含めない |
| 名前解決の文脈 | 宣言元の字句スコープ、各断片の alias、アクセスの文脈 | 含めない |

引数は「宣言元＋スロット」で識別し、名前や位置番号だけで同一視しない。外側の引数は未使用でも保持し、本文の変更や証明経路によって型の Identity を変えない。

group と Contract は、自身の型・Semantics・Origin パラメーターを宣言できない。外側にパラメーターがあれば、未使用でもジェネリック定義として検査する。

### 2.2. 宣言参照

型・Contract・group・関数・Field の参照を、共通して次の形で扱う。

```text
宣言参照 = 宣言 Identity + 正規化した引数束縛
```

宣言 Identity は、元の Kotonoha、マージ済み親宣言、名前、種類、自身の generic arity 等、既存の宣言識別情報による。具体化された親の束縛は宣言 Identity に含めない。引数束縛には外側の分と自身の分を保持するが、自身の arity に継承分を加えず、引数を二重に指定させない。

```kimi
struct Outer<T>
    public struct Inner<U>
        var first: T
        var second: U

    public struct Tag

    public contract Sink
        func write(self: ref/Self, value: T) -> ()
```

`Outer<i32>.Inner<string>` と `Outer<i64>.Inner<string>` は異なる型である。空の `Outer<i32>.Tag` と `Outer<i64>.Tag`、および `Outer<i32>.Sink` と `Outer<string>.Sink` も、それぞれ異なる宣言参照となる。

完全な型・Contract の参照と適合証拠には Origin を保持する。継承した Origin を結果の寿命に使う Contract では、`source = a` の証拠を `source = b` に無条件では使えない。正規化と必要な寿命・境界条件の証明には既存規則を使う。

Signature 比較、実装選択、実行時 Identity、生成用キーは、それぞれの既存規則に従う。Origin を消去したキーの一致だけで、適合証拠を同一視したり、コードを共有したり、型形成・アクセス・寿命検査を省略したりしない。静的保存領域のキーは §6 に定める。

### 2.3. 制約の分類と証明

制約は、元の binder・宣言・役割を保って扱う。

| 種類 | 例と扱い |
| --- | --- |
| 入力条件 | 型の `T is C` や Origin の境界条件。使用側が満たし、定義の検査では前提として利用する |
| 宣言の証明義務 | struct の `Self is C` や閉じた型への条件。宣言自身について独立に証明する |
| Contract の実装要求 | 適合する `Self` に依存する要求。各実装の適合検査で満たす |

内側は外側の入力条件を利用できる。外側の証明義務は、証明済みの有効な証拠としてのみ利用し、無条件の前提へ変えない。外側の `Self is C` を内側の適合へ読み替えない。

内側が追加した入力条件は、その内側を使う条件であり、外側へ逆伝播させない。Contract では、外側の引数だけに依存する条件は Contract 参照の入力条件、適合する `Self` にも依存する条件は実装要求、どちらにも依存しない閉じた条件は宣言の証明義務とする。関数などの個別の Constraint subject 制限は維持する。

宣言の収集・参照解決と証明の完了を分離する。例えば、外側 struct が内側 Contract に適合する場合、両宣言を解決した後、その要求と実装から適合を検査する。包含関係だけで親の検査完了を子の参照解決の前提にせず、検査中の適合を自身の証拠にもしない。実際に利用する証拠への依存は保持し、各宣言の義務を受理までに全て検証する。

定義時に、入力条件の下で全ての許可された束縛に対する本体・証明義務を検査する。未解決の依存は既存の期限まで保留できるが、最終的な証明不足を都合のよい具体化まで持ち越さない。

## 3. 名前、Origin、アクセス

### 3.1. 修飾参照と引数の省略

`Parser.State`、`Outer<i32>.Inner<string>` のようなパスを使う。型、Contract、refinement の親、Constraint、associated-Type の修飾、alias は同じコンテナー参照規則を使う。

外側の型引数は、字句スコープまたは解決済みの参照から外側環境が決まっている場合に限り省略できる。`Outer<T>` 内の `Inner<i32>` は、その環境の `Outer<T>.Inner<i32>` を指す。外部から不足する外側引数を期待型や呼び出し引数で逆算しない。自身の引数の推論は各構文の既存規則に従う。

このため、外部からジェネリック struct の型関数を呼ぶ際も、外側は `Factory<i32>.make(...)` のように確定させる。関数自身の引数は通常どおり推論できる。外側も推論させたい API は、group の `make<T>(...)` として宣言できる。修飾子の arity 選択と呼び出し推論を結合する新しい探索は導入しない。

修飾参照は、論理的に次の順で処理する。これはコンパイラーの物理的なパス構成を指定するものではない。

1. 既存の名前空間・検索順序と role・アクセスによる候補の絞り込みに従い、宣言経路と各段階の引数スロットを解決する。
2. 型引数・Origin をスロットに対応付け、字句環境や基底からの束縛と合成する。
3. 最終対象だけでなく、途中の修飾子も含めて、アクセス・型形成・入力条件・Origin を検査する。

Origin がパス末尾で指定される場合、その対応付け前に未指定として拒否しない。逆に、最終対象を直接の宣言参照へ正規化しても、途中の検査義務は消さない。検索層を確定した後は、引数や制約の不適合を理由に別の候補へ戻らない。

### 3.2. Origin の指定

内側の有効な Origin 引数一覧は、継承した明示的 Origin と自身の宣言分を合わせたものとする。継承される Origin と同名の再宣言は禁止する。型引数内部の Origin は完全な型の一部として保持し、この一覧へ重複追加しない。

パス末尾の一つの `from (...)` は、最終対象の有効な Origin スロットへ対応付ける。未知の名前、重複指定、既に束縛済みのスロットへの再指定はエラーとする。有効な Origin が一つの場合は `from a` と短縮できる。対応付けは名前を解決した後の binder Identity による。

```kimi
struct View<T> origin source
    let value: ref/T from source

    public struct Tag

    public contract Source
        func read(self: ref/Self) -> ref/T from source

func inspect<T> origin a(value: View<T>.Tag from (source => a)) => ()
```

`Tag` と `Source` は、ともに `T` と `source` を継承する。Contract 参照も `View<T>.Source from (source => a)` と書ける。内側が独自に `origin local` を宣言した場合は、同じ末尾のリストで `source` と `local` を指定する。

通常の型注釈では、未指定の Origin に既存の位置別省略規則を適用する。Contract 参照・alias・単独のコンテナー修飾子では、既に束縛済みの Origin 以外を明示し、根拠なく `static` にしない。`Self` や省略した外側環境を変更せず、別環境を指定するときは親パスを明示して形成する。

途中で Origin を確定させる場合は `(ContainerPath from (...)).member` を使う。括弧は型側の修飾子を区切り、runtime 値を作らない。子の参照は確定済みの束縛を保持する。継承経由の最終対象に残らない Origin は、その途中の修飾子で指定する。例えば、Origin を持つ Derived から Origin を持たない基底の Node を参照する場合は `(Derived from (...)).Node` とする。

借用を表す Semantics の Origin と、コンテナーの Origin は別である。例えば `ref/(View<T>.Tag from (source => a)) from r` は、`source` を `a` に、外側の借用 Origin を `r` に束縛する。

関連型の明示修飾は `X.(ContractPath from (...)).Element`、関連型指定は `associate (ContractPath from (...)).Element is T` とする。Origin を伴わない既存の形式も維持する。

内側の型の `OwnedOrigins` には、継承した Origin と未使用の型引数も含める。空の `Outer<ref/i32 from local>.Tag` も `Owned` にはならない。型の寿命依存は、実際の Loan や外側インスタンスの保持とは区別する。

enum Case の修飾子では、enum 自身と継承した明示的 Origin の引数を書かない。構築時は期待型と payload、Pattern では照合する型から束縛を決め、不足する場合は期待型を注釈する。完全な型引数の内部に書かれた Origin は保持する。Case の型引数推論は既存規則に従うが、外側の型引数は §3.1 に従う。

### 3.3. Self と宣言位置

`Self` は最も内側の Self 文脈で解釈する。

| 文脈 | `Self` |
| --- | --- |
| `struct`、`enum` | 外側と自身の引数を適用した、その型 |
| `contract` | その Contract に適合する型。Contract 自体ではない |
| `group` | 新しい文脈を作らず、外側の Self 文脈を参照 |

group 内の `Self` は最も近い外側の型を指す型名であり、暗黙の receiver を作らない。外側に Self 文脈がなければ使用できない。さらに外側の別の型を指す場合は `Outer<T>` のように明示する。

group に直接属する関数・Property は静的で、receiver とその省略構文を持たない。明示的な型を持つ引数名 `self` は、既存の §7.3 と同じく通常の引数名として許可する。group 本体に直接 Constraint Clause・適合宣言・関連型指定を置けない。group 内の関数や型が自身の許可された制約を書くことは妨げない。

```kimi
struct Parser
    group Helpers
        func identity(value: Self) -> Self => value // Self は Parser。

        struct State
            func identity(value: Self) -> Self => value // Self は State。
```

どちらの `identity` も、明示された `value` だけを受け取る。

### 3.4. アクセスと名前の衝突

アクセスは既存の有効アクセス領域に従い、以下を適用する。

- 内側から外側の private 宣言へアクセスできるが、instance member の操作には明示的な receiver が必要。
- 親に子の private メンバーへの特権を与えない。分割宣言は同じマージ済み宣言のアクセス権を共有する。
- 宣言参照の有効アクセス領域と API 検査には、親コンテナーと具体的な外側引数の制限を含める。適合の領域は適合する型と Contract 参照の領域の共通部分とする。
- struct に直接属する `protected group`、`protected struct` 等を許可する。group に直接属する宣言では protected 系を禁止する。非インスタンス宣言に receiver 制限を課さない。

通常のネスト型と associated Type は別である。同名の struct を置いても適合や関連型の指定を生成しない。`T.Element` で両方の解釈が成功した場合は曖昧性エラーとし、宣言の追加で既存の射影を黙って別の型に変えない。射影は `T.(C).Element` で Contract を明示できる。既存の `T.C.Element` も残すが、複数解釈が成功すればエラーとする。

関連型の候補は、§4.1 の「定義元宣言＋束縛」で重複排除する。同じ宣言でも束縛が異なれば別候補であり、結果の型が偶然同じでも統合しない。Contract を明示した後も候補が一つに定まらなければ曖昧性エラーとする。

名前の衝突は名前空間と宣言スコープごとに判定する。型パラメーターと同じ宣言空間に同名のネスト宣言を置けない。祖先スコープの名前の隠蔽は既存の字句規則に従い、名前が隠れても継承したスロットは消さない。Origin は別の名前空間なので、型宣言との同綴りだけではエラーにしない。

### 3.5. 構文の変更点

付録 F の該当生成規則を、次の共通パスに置き換える。`TypeSegment`、`TypeArguments`、`OriginAnnotation`、`Argument` は既存の生成規則を使う。

```ebnf
PlainContainerPath := "::"? TypeSegment ("." TypeSegment)*
BoundContainerQualifier := "(" ContainerPath OriginAnnotation ")"
ContainerPath := ("::"? TypeSegment | BoundContainerQualifier)
                 ("." TypeSegment | "." "(" ContractReference ")" "." Name)*
ContainerReference := ContainerPath OriginAnnotation?
ContractReference := ContainerReference
ContractSelector := ContainerPath | "(" ContractReference ")"
NamedType := ContainerPath
TypeQualifier := ContainerPath
AssociatedTypeName := Name | ContractSelector "." Name
AssociatedTypeReference := TypeQualifier "." Name
                         | TypeQualifier "." ContractSelector "." Name
AliasDeclaration := "alias" ContainerReference
ConstructionExpression := ContainerPath "." "init"
                          "(" TrailingList<Argument>? ")"
CaseReference := PlainContainerPath "." Name | "." Name
```

これは構文上のパスの共通化であり、group や Contract を値の型として使えるようにする変更ではない。各位置で最終対象の役割を検査する。型引数は各セグメントが宣言する引数に対応し、group・Contract 自身に引数リストを追加しない。型位置の `BoundContainerQualifier` は通常の括弧付き型と同じ参照へ正規化し、二つの候補に数えない。

- **宣言と Contract**：`ContainerBody` の配置条件を §1・§3.3 に合わせる。`ContractParentList`、適合、Constraint、関連型指定は拡張後の `ContractReference` を使う。§8.4.2 の「型引数なしの修飾名」という制限を外側のセグメントについて撤回する。
- **型と式**：`NamedType` の変更を構築式にも適用し、各段階の型引数と途中の Origin 束縛を受け付ける。通常の式では `Primary` に `BoundContainerQualifier`、`PostfixSuffix` に `"." "(" ContractReference ")" "." Name` を追加する。これらは型側の修飾としてのみ解決し、前者単独を値にしない。予約済みの `.init(...)` は構築式とし、値メンバーの呼び出しへ戻らない。
- **関連型**：括弧付き `ContractSelector` は Contract による明示修飾を指定する。従来の括弧なしパスでは、複数の解釈が成功した場合の曖昧性診断を維持する。
- **enum Case**：Pattern は上記 `CaseReference`、式は従来どおり Postfix の Binding で分類する。修飾子は `PlainContainerPath` に限定し、§3.2 の Origin 省略規則を適用する。型引数内部の完全な型は制限しない。

既存の構文位置ごとの制限は、新しいパスにも適用する。括弧を使って Origin 記載の禁止、型・値の区別、runtime Contract の制限を回避できない。

## 4. Contract と適合

### 4.1. Identity と要求

Conformance は「適合する型＋Contract の宣言参照」で識別する。要求・associated Type は「定義元宣言＋定義元 Contract への引数束縛」で識別する。refinement の経路に沿って引数を置換し、要求側の `Self` は最終的な適合する型に置換する。

refinement で受け継ぐ条件も §2.3 の分類を保つ。親 Contract の入力条件は置換して子の公開入力条件に含め、証明義務を入力条件や実装要求へ変えない。

同じ参照へ至る複数経路は、その前提と検証義務を保持して集約する。異なる束縛や associated Type の指定で同じ適合を上書きしない。Origin と各種キーの区別は §2.2 に従う。

### 4.2. 衝突判定と直接適合

同じ型に対する直接適合と証明経路の合流には、同じ衝突判定を使う。

1. 同じ Contract 宣言への束縛を、既知の型同一性・関連型指定・Origin の規則で正規化する。別の Contract 宣言なら衝突しない。
2. 型パラメーターと、構造が固定された名目的型・Tuple・関数型等の自由項部分は、変数の種類を保ち、occurs-check（自己を含む置換の禁止）付きで単一化する。同じ宣言元のスロットは両辺で同じ変数とする。関数型内の局所 binder は既存規則で名前を正規化し、外側の入力スロットとして単一化しない。
3. 固定された構造の不一致など、既存規則で不一致を証明できれば「非衝突」とする。単一化できれば、その置換と等式を衝突条件として保持する。
4. 未解決の関連型射影、Semantics の適用、長さ式、Origin の交差・順序など、自由項ではない部分は残余条件として保持する。単なる構文不一致を非衝突の証拠にしない。残余条件を解消できなければ「衝突可能」とする。

単一化は定義時の衝突検査であり、呼び出し側の引数を推論したり、条件に応じて実装を選んだりする機能ではない。規定された正規化と有限な証明規則だけを使う。例えば、`A.Item` と `B.Item` は A と B が異なっても同じ型になり得るため、射影を単射な型構築子として扱わない。

直接適合は、型の通常の入力条件の下で「非衝突」と証明できる組だけを許可する。「衝突可能」は定義時に拒否する。

```kimi
struct Family<T>
    public contract Marker

struct Good
    Self is Family<i32>.Marker
    Self is Family<string>.Marker // 束縛が異なるので許可。

struct Bad<A, B>
    Self is Family<A>.Marker
    Self is Family<B>.Marker      // A = B になり得るので定義時エラー。
```

同じ `Family<i32>.Marker` を二度直接宣言した場合も、重複エラーとなる。

適合の `when` 条件が排他的であることを例外にしない。同じ実装による自動マージ、優先順位、具体化後の選び直しは導入しない。未解決依存の期限は §2.3 に従う。

### 4.3. 証明経路の合流と refinement の循環

直接適合、Contract refinement、基底型からの有効な適合継承による証明経路を対象とする。直接適合どうしの組は §4.2 で検査し、それ以外の組は、同節の衝突条件に各経路が成立する条件を加えて検査する。同時に成立して合流し得る場合、その条件の下で関連型の指定・要求・実装対応の整合性を定義時に証明する。

Contract 定義では要求と関連型の条件を、適合定義では実装対応も検査する。単一化の置換と残余条件を使い、既存規則で同時成立や合流を排除できず、整合性も証明できない場合は拒否する。これにより、直接適合の重複は禁止しつつ、整合する複数の証明経路は許可する。

例えば、ある Contract が `Family<A>.C` と `Family<B>.C` を親に持つ場合、`A = B` では合流する。両経路で同じ関連型を `i32` と `string` に固定する宣言は、その合流を排除できなければ定義時エラーとなる。具体化後に矛盾を発見して選び直すことはしない。

一つの refinement 経路で同じ Contract 宣言に戻る場合、引数が異なっていても循環として拒否する。`Outer<T>.C → Outer<List<T>>.C → …` も対象とする。別々の経路に現れる異なる具体化は、この理由だけでは禁止しない。

Contract 自身の引数宣言、実装からの Contract 引数推論、外部からの適合追加、実装優先順位、runtime Contract View は導入しない。

## 5. 分割宣言、継承、alias

### 5.1. 分割宣言

分割宣言は、マージ済み親宣言 Identity の下で再帰的にマージする。自身のヘッダー・Origin・アクセス等の一致、制約と基底節の定義箇所、メンバー重複には既存規則を適用する。継承分を各ヘッダーへ書き直させず、各断片のソース環境を保持する。enum と Contract の分割は禁止を維持する。

具体化ごとに断片を選んだりマージし直したりしない。指令選択・生成後の親子の宣言ツリーを確定し、そこへ引数束縛を適用する。

### 5.2. 継承経由の参照

継承経由の検索は、宣言元 Identity と基底への置換で得た引数束縛を保持する。

```kimi
open struct Base<T>
    public struct Node

struct Derived : Base<i32>
```

`Derived.Node` と `Base<i32>.Node` は同じ型である。Derived を新しい親にせず、内部の `Self` も宣言元の文脈を維持する。

継承時の同名宣言禁止を Type 名前空間のコンテナーメンバーにも適用する。派生 struct からアクセスできる祖先のコンテナーメンバーと同名の group・struct・enum・contract を直接宣言した場合、arity にかかわらず宣言エラーとする。したがって、この Derived に別の Node を宣言できない。基底とのマージも行わない。

名前空間をまたぐ同綴りは禁止せず、アクセスできない祖先の名前は既存の Value 規則と同じく再使用できる。open struct へのアクセス可能なコンテナーメンバーの追加・公開範囲拡大は下流を壊し得る API 変更とし、§21.3.4 の依存再検証に含める。

### 5.3. alias と他の機能

`alias Outer<i32>.Helpers` のように具体化されたコンテナーを開ける。alias は宣言時に確定した宣言参照を保持し、使用時に引数を推論し直さない。

alias は SourceDocument の先頭に置き、Compilation root から他の alias に依存せず対象を解決する。直接のメンバーだけを開き、使用箇所でアクセスを検査する。同じ宣言かつ同じ引数束縛だけを重複排除し、再公開・アクセス拡大・型別名の導入は行わない。

コンテナーを開く操作は新しい名前と型の対応を宣言する `alias Name = Type` ではない。形式パラメーター・Origin・`Self` をメンバーとして取り込むこともない。二つの alias が同じ宣言の異なる具体化を導入した場合は別候補とし、§9.6 の通常の型名選択で同名・同 arity の候補が残れば曖昧性エラーとする。

```kimi
// Definitions.kimi
struct Family<T>
    public group Helpers
        public struct Token

// Use.kimi（同じ Kotonoha の別文書）
alias Family<i32>.Helpers
group Use
    func make() -> Token => Token.init()
```

この `Token` は `Family<i32>.Helpers.Token` を指す。同じ文書で `Family<string>.Helpers` も開くと、この非修飾の `Token` は曖昧になる。

`#Test`、明示的特殊化、外国関数宣言などの適格性は、継承した引数も含めて既存の個別規則で検査する。内側の group を経由して非ジェネリック性などの制限を回避できないようにする。

## 6. group の静的保存領域

### 6.1. 領域の同一性

group 内の Field は、struct の内側でも静的である。

```kimi
struct Cache<T>
    public group Statistics
        public var count: i32 = 0
```

`Cache<i32>.Statistics.count` と `Cache<string>.Statistics.count` は別領域となる。Field が `T` を使っていなくても同じ規則を適用する。

保存領域のキーは、Field の宣言 Identity と、外側の引数束縛を正規化して Origin を再帰的に消去した情報で作る。型の構造と Semantics は保持する。Origin 差だけで領域を増やさず、異なるキーの領域をコード共有で統合しない。

同じキーへの参照は、修飾経路が異なっても同じ領域を指す。例えば `Cache<ref/i32 from a>.Statistics.count` と `Cache<ref/i32 from b>.Statistics.count` は、各参照の検査を満たせば同じ領域である。初期化状態、Loan、静的アクセスの効果、破棄もこの同一性を使う。

### 6.2. 保存と共有の検査

保存する値には `Owned` と既存の静的保存条件を要求するが、外側の型全体に一律の `Owned` は要求しない。完全な Origin 情報と寿命・Loan の検査は保持する。

§8.10 の定義時検証と §21.3 の共有生成規則を静的領域にも適用する。同じキーに属する全ての有効な Origin 束縛に対し、一つの保存表現・初期化・破棄計画を使えることを、必要な操作・証拠・呼び出し先を含めて検証する。保存型が `Owned` であることだけでは、初期化処理の共有可能性まで証明したことにはならない。

独立した同値性証明器は追加せず、検証済みの型付き計画を再利用する。具体配置は既存の具体化段階で確定する。最初のアクセス元の Origin で計画を選ばず、成立しない場合は診断する。Origin 別の領域への分割では救済しない。初期化結果が通常の実行状態に依存することは許される。

### 6.3. 初期化と破棄

§22.2.3 の遅延初期化、初期化循環時の Abort、正常終了時の逆初期化順の破棄を適用する。型・関数の参照だけでは初期化しない。未使用の Field も宣言・初期化式を検査するが、実行時の初期化や破棄は要求しない。

### 6.4. 共有コードからのアクセス

静的 Field に触れるだけで機械語本体を分離する必要はない。§21.3.3 の供給事実に、保存領域キーに対応する「初期化確認と保存領域への到達」の操作を追加する。固定の操作、または既存の entry/context の組で供給する。

操作の private context は、対象の保存領域・初期化状態への参照と必要な初期化・破棄操作を保持する。context 自体は不変とし、可変の初期化状態は領域側に置く。新しい GenericContext のスロット種別、呼び出しごとの context 構築、runtime の型検索は不要である。

参照先が固定なら操作を直接化できるが、§6.3 の初期化確認・循環検出と §6.1 の領域同一性は維持する。

## 7. 実装と検証

### 7.1. 処理量と再利用

以下は、言語上の同一性と検査結果を変えずに処理量を抑える実装指針である。

- 宣言ツリーは共有し、具体化ごとに複製しない。引数束縛は親への参照と自身の差分で共有し、平坦化が必要な場合も再利用する。
- 正規化した宣言参照を共有・ハッシュ化する。証明キャッシュは別にし、前提・証拠・依存先の更新状態を含める。
- refinement の処理済み判定は宣言参照単位、循環検出は現在の経路上の宣言単位で行う。既処理ノードでも経路の前提と合流義務を捨てない。
- 適合衝突は Contract 宣言ごとに分類し、同じ宣言に属する束縛の組だけを詳しく比較する。refinement の到達先と経路条件も再利用する。
- 外側のレイアウト完成を内側の名前解決の前提にせず、全ての子を先行して具体化しない。実際の保存型の循環・表現不能性は既存の検査で拒否する。

異なる束縛が実際に多数生じる場合の増大は、キャッシュ・具体化数を含む資源制限で扱う。最適化や追加の強力な証明器で言語の受理条件を変えない。資源上限への到達は資源診断とし、規定の証明規則による証明不足とは区別する。

成果物には引数束縛、制約の分類・出所、証拠、選択した宣言への依存を記録する。外側の変更で古くなった内側の証明・計画は無効化する。

### 7.2. 検証項目

| 対象 | 主な検証事項 |
| --- | --- |
| 宣言と名前 | 多段ネスト、rootgroup と禁止箇所、分割・再読込、Self、名前空間、アクセス、型引数省略 |
| 構文と Origin | 未使用引数の Identity、末尾・途中の束縛、括弧付き Contract 修飾、構築式、Case、Owned |
| 制約と Contract | 前提と義務の分離、親子の証明依存、自由項と非自由項の衝突判定、基底経由を含む条件付き合流、宣言単位の循環 |
| 継承と alias | 直接参照との同一性、途中の検査義務、同名宣言禁止、束縛の保持、重複排除と曖昧性 |
| 静的領域 | Origin 消去後の共有、アクセス元非依存、entry/context 経由の到達、初期化・Loan・破棄 |
| 再利用と資源 | 深いネスト、refinement の合流、依存モジュール、変更時の無効化、キャッシュと資源上限 |

### 7.3. 仕様本文への統合

仕様本文へ統合する際は、次の箇所を更新する。

| 統合先 | 更新内容 |
| --- | --- |
| [§3](../../spec/03-types-and-values.md) | 宣言参照、完全な型と実行時 Identity の区別 |
| [§6](../../spec/06-declarations-and-containers.md)、[§7](../../spec/07-functions-and-callable-values.md) | 配置、分割、継承時の Type 名重複禁止、Case、Self と receiver |
| [§8](../../spec/08-generics-constraints-and-contracts.md) | 外側環境を継承する静的 Contract、親パス、衝突・合流・循環、定義時検証。§8.5 の runtime Contract 条件は緩和しない |
| [§9](../../spec/09-names-signatures-and-access.md)、[§10](../../spec/10-overload-resolution-and-inference.md) | 束縛付き候補、アクセス領域、関連型修飾、曖昧性、外側引数の省略と推論の境界 |
| [§11](../../spec/11-properties.md)、[§12](../../spec/12-expressions.md)、[§13](../../spec/13-operators-and-assignment.md)、[§14](../../spec/14-control-flow.md)、[§15](../../spec/15-ownership-and-lifetime-analysis.md) | 静的保存、構築・修飾式、adaptation の構文境界、Case Pattern、継承した Origin と OwnedOrigins、静的 Contract の寿命設計境界 |
| [§18](../../spec/18-modules-and-dependencies.md)、[§19](../../spec/19-compile-time-directives.md)、[§20](../../spec/20-compilation-configuration.md) | 具体化された alias、指令選択後の配置、継承環境を含むテストの適格性・発見・識別 |
| [§21](../../spec/21-layout-runtime-and-code-generation.md)、[§22](../../spec/22-core-execution-and-foreign-functions.md) | 静的領域キーと供給事実、共有生成、依存再検証、遅延初期化と破棄 |
| [付録 A](../../spec/appendices/A-compiler-requirements.md)、[付録 F](../../spec/appendices/F-syntax-summary.md) | 上表の検証項目、§3.5 の生成規則と役割制約 |

[付録 D](../../spec/appendices/D-deferred-features.md) の境界は、次のように整理する。

- **User-defined generic Contracts, outer generic capture, …**：外側環境の継承だけを本書の採用事項へ移す。Contract 自身の引数宣言、既定実装、外部適合、修飾付き要求呼び出しは未導入のままとする。
- **Contract-level abstract Origins, non-static erased views, …**：静的 Contract が外側の Origin を継承することを採用事項として分離する。Contract 自身の Origin 宣言と runtime View の拡張は先送りを維持する。§8.5 の禁止を静的適合全体へ適用しない。
- **Source transparent Type aliases**：未導入を維持する。具体化されたコンテナーを開く alias は §5.3 の名前解決機能として記述する。
