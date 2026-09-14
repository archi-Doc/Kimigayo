# Result Copy・静的継承の確定変更案と実装計画

2026-09-12。設計確定、実装は未完了。本書と SPEC.md が異なる箇所は本書を優先する。本作業では SPEC.md とコンパイラーを変更しない。以下の SPEC 節番号は、後続の仕様反映先を示す。

## 1. 変更の範囲

| 確定事項 | 効果と互換性 |
| --- | --- |
| Core.Result に conditional Copy を追加 | Option と通常の enum 導出に統一する。取得・共有読取りの結果 Type が変わる |
| 通常の継承メンバーは静的選択 | virtual/override は拡張設計へ集約する。Object View と完全な動的破棄は維持する |
| accessible な祖先の Value-role Name と同名の派生宣言を禁止 | hiding を防ぐ。派生側の overload 追加や Self 依存 Contract の実装を制限する |
| ObjectCompatible を呼出し操作ごとの公開保証にする | 通常本体と閉じた specialization 集合の共通保証を使用する。撤回は利用側を壊し得る |
| 宣言内容に依存する検証を artifact の依存検証へ統合 | 追加された Name・specialization も検出し、必要な箇所を再検証する |

新しい取得構文、暗黙の基底変換、hiding の特例、警告抑止構文、virtual の実装は追加しない。Result の変更は静的継承の変更から独立して実装できる。

## 2. Result の conditional Copy

### 2.1. 宣言と能力判定

```kimi
enum Result<T, E>
    Self is Copy when T is Copy, E is Copy
    Ok(T)
    Err(E)
```

有効な owned Core.Result<T, E> は、完全 Type としての T と E の双方について Copy が証明される場合に Copy になる。通常の enum conditional Copy（SPEC §3.5.2、§8.4.8）を使い、Type 形成の条件にはしない。全 Case の payload を検査するため、実行時に Ok と分かっていても E の条件は省略しない。

次の例では各 Type・Origin が有効であり、Resource は object payload として適格な Non-Copy struct とする。

| 完全 Type | Copy の判定 |
| --- | --- |
| `Result<i32, i32>`、`Result<(), i32>`、`Result<Option<i32>, i32>` | Proven |
| `Result<string, i32>`、`Result<i32, Resource>` | Refuted |
| `Result<ref/Resource, i32>`、`Result<objref/Resource, i32>` | Proven。shared borrow 自体を Copy する |
| `Result<uniq/Resource, i32>`、`Result<objuniq/Resource, i32>` | Refuted |
| `Result<obj/Resource, i32>`、`Result<rc/Resource, i32>`、`Result<arc/Resource, i32>` | Refuted |

| 総称型の証拠 | `Result<T, E> is Copy` |
| --- | --- |
| 両方 Proven | Proven |
| 少なくとも一方 Refuted、他方も有効な証明対象 | Refuted |
| Refuted はなく、少なくとも一方 Unknown | Unknown |

Proven は Copy、Refuted は Non-Copy の証明である。Unknown は既存の証明期限・条件付き取得計画に従い、Non-Copy と扱わない。不正な Type・宣言・矛盾した証拠は Error とする。

Copy は active Case と payload を既存の規則で複製し、取得元を Initialized に保つ。割当て・カウント増加・独自 clone を追加しない。片側が Non-Copy なら enum 全体の所有取得は Move になる。借用 payload の Origin・Loan・storage anchor は Copy 後も保持する。`obj/Result<i32, i32>` など外側の Semantics の取得規則は変えない。

SPEC §17.2.2 の FileError 例には `Self is Copy` を追加する。`Result<Data, FileError>` は Data も Copy の場合に限り Copy となる。payload-free enum の自動 Copy は導入しない。

### 2.2. Core shape

SPEC §22.1 の Copy 条件を、§8.7 の proposition identity と conjunction elimination による原子命題集合で比較する。

| Core enum | 条件集合 |
| --- | --- |
| `Option<T>` | `{ T is Core.Copy }` |
| `Result<T, E>` | `{ T is Core.Copy, E is Core.Copy }` |

T/E は対応する型引数スロット、Core.Copy は認識済み Symbol Identity である。順序・透明な括弧・同一原子の重複は集合を変えない。Copy 宣言の欠落、無条件 Copy、原子の不足・追加・別 Identity は不適合。その他の required shape 条件は維持し、任意の論理同値判定は行わない。

合成 Core とロード済み Core に同じ適合規則を使う。生成ソースは T、E 順を標準形にできるが、その固定順を外部 Core の要件にしない。同名のユーザー enum に名前による特別扱いはしない。

Case 順序・payload 構造は維持する。旧 Core の契約に依存した能力判定・取得計画・生成物は §3.4 に従い再検証・再生成し、バイナリ互換性を仮定しない。

### 2.3. 共有読取りと移行

SharedReadResult（SPEC §4.6.6）と ref 経由の Pattern 読取り（§15.1.6）の共通規則は変更しない。Copy になる Result は、storage borrow ではなく値として読まれる。

| 操作 | 変更前 | 変更後 |
| --- | --- | --- |
| `s: Slice<Result<i32, i32>>` の `s[0]` | `ref/Result<i32, i32>` | `Result<i32, i32>` |
| `s[0]@ref` | 要素 Place の shared borrow | 同じ |
| `s.tryGet(0)` | `Option<ref/Result<i32, i32>>` | 同じ |
| `wrapper: Option<Result<i32, i32>>` を `match wrapper@ref` し `.Some(let r)` で取得 | r は `ref/Result<i32, i32>` | r は `Result<i32, i32>` |

要素を常に借用するコードは Place への `@ref` に統一する。

```kimi
func head<T, E> origin source(s: Slice<Result<T, E>> from source) -> ref/Result<T, E> from source
    return s[0]@ref
```

従来の `return s[0]` は、変更後には全許容 binding で戻り値契約を満たさず、定義時エラーとなる。Copy した一時値を借りても source への参照にはならない。型推論・引数適合・Origin・総称本体を再検証する。

Pattern binding に同じ書換えはできない。参照固定 Pattern と payload projection 構文は追加せず、必要なら enum 全体の借用を受ける API へ変更する。公開 Signature の変更もあり得る。この制限は Appendix D.1 の「Pattern binding acquisition selection」に記録する。将来の設計対象は元の Place からの借用指定、結果 Type、Origin/Loan、取得時点であり、既存の Guard candidate capture とは別項目とする。

### 2.4. 破棄警告

discarded-Result は Copy 分類と独立し、Core.Result の Symbol Identity に基づいて判定する。通常の名前解決で同じ Symbol を参照する場合も同じ扱いにする。

同じ値の同じ破棄箇所に複数の警告が該当するときは、**unintended-Unit ＞ Symbol 固有 ＞ effect-free** の最上位一件だけを出す。成立条件自体は変更しない。今後の Symbol 固有警告は中段に置き、同段で競合する場合の順位も導入時に定める。

`do` の末尾の `if` 各 arm で Result を捨て、SPEC §17.4.1 が成立する場合は unintended-Unit を出し、yield 等による結果供給を案内する。別 arm は別の破棄箇所であり、構文木全体を一件にまとめる規則ではない。無関係な診断は抑制しない。

## 3. 静的継承と公開検証情報

### 3.1. 静的選択と同名禁止

通常の構造体関数・呼出しを伴う Property accessor は、継承メンバーや具体 Core の Object View 経由でも、Effective Type から静的に選択した実装を使う。lookup・overload・access・specialization は既存規則に従い、Runtime Object Type による差替えは行わない。`open` は派生を許可する指定であり、sealed 既定は維持する。関数値の呼出しまで静的直接呼出しに限定する規則ではない。

> 派生 struct は、その宣言位置から accessible な祖先層の Value-role member と同じ Name の Value-role member を宣言できない。

Signature、instance/type function、Field/stored/computed Property の違い、overload の適用可否、条件付きメンバーの premise で免除しない。Property は自身の accessibility で判定し、非公開 accessor では禁止を回避できない。protected は派生 struct の receiver に基づいて判定する。

環境選択・Mod・fragment 統合後の宣言集合と有効な base graph で検査し、派生宣言にエラーを出す。同一層の overload と他の名前空間は従来どおり。specialization は元の関数の実装であり、新たな同名メンバーではない。派生 Container から基底関数を specialize する権限は追加しない。

```kimi
open struct Base
    public func f(self: ref/Self, x: i32) -> i32 => x

struct Derived : Base
    public func f(self: ref/Self, x: string) -> i32 => 0 // Error: Base.f と同名
```

SPEC §9.5 の層コミットは維持する。選択後の引数適合・使用検査の失敗で基底 overload に戻らない。派生作者から inaccessible な private、別 Kotonoha の internal/private-protected などの同名宣言は許可する。

保証するのは、派生作者から accessible で、各呼出し位置でも使える既存メンバーの**宣言層**が refinement で変わらないことである。public Animal.speak は、絞込み前後の parameter/let、join 後、var、Dog/Animal View で同じ宣言層を参照する。派生の別名メンバーは refinement 後に追加利用できる。引数型による overload 選択やアクセス境界を含む「あらゆる Name の検索結果不変」は保証しない。

open struct の accessible な Value-role Name は派生側への API である。Name 追加・access 拡大は下流の同名宣言を不正にし得る。破壊的変更の分類と実際の診断位置は §3.4 に統一する。

### 3.2. Conformance の継承

> 基底 B の検証済み (B, C) は、requirement の Self を D に置換した各要件を、保持した実装 mapping がすべて満たす場合に限り D に継承される。実装 Member M の宣言元を A とし、実装側の Self は A のまま保つ。D から A への基底経路で型・Origin を置換し、receiver の差だけを適法な borrowed receiver projection で埋めてよい。

A → B → D の多段継承でも M を B の宣言と扱わない。関連型 binding を保持し、SPEC §8.4.5 により parameter/result・access・Origin・effect・premise を照合する。Property は §11.4 の witness 対応を使う。receiver 以外の基底変換や所有 receiver の slicing は追加しない。projection を伴う呼出しは §3.3 の Proven を必要とし、直接の標準 Place 操作とは区別する。

| requirement の形 | 継承経路の判定 |
| --- | --- |
| Self は borrowed receiver だけ（Stringify 型） | 他の照合と ObjectCompatible が成立すれば可能 |
| `other: ref/Self`（Equatable / Comparable） | A と D の引数型が一致せず不成立 |
| `func empty() -> Self` | A と D の結果型が一致せず不成立 |
| `owner/Self` receiver（Iterable） | projection できず不成立 |
| `Self.Element` が固定された同じ Type に正規化される | 正規化後に照合する。Self の表記だけでは除外しない |

この規則は通常の Contract に適用する。Copy・Owned・Callable 等の intrinsic 能力は固有規則に従う。Copy は派生 struct 自身の opt-in と全格納要素の導出を要し、自動継承しない。

一経路の不成立だけで D の宣言をエラーにしない。(D, C) は、明示・条件付き・Contract refinement を含む候補全体について不成立を証明できた場合だけ Refuted とする。総称依存は Unknown と既定の証明期限に従い、不正な宣言・矛盾は Error とする。

継承した mapping は Member Identity と receiver 対応を固定し、同名で再検索しない。新たな明示 conformance は通常の実装検索を使う。同じ (D, C) の複数成立経路は関連型・実装 mapping が一致しなければエラーとし、generic 呼出しも確定済み mapping を使用する。

open 基底への継承障害警告は出さない。派生の `Self is C` が失敗した位置、または制約使用で (D, C) が Refuted となる位置に、不成立の requirement と原因を付記する。Self 不一致・所有 receiver・access/Origin/premise・NotProven を同じ形式で扱う。Unknown の期限切れは未証明として診断する。

例えば Equatable な Shape からの `Circle : Shape` は有効だが、それだけで Circle は Equatable にならない。必要な位置で `ref/Shape` と `ref/Circle` の不一致と、同名実装を追加できないことを示す。派生型でもこの Contract が必要なら、葉の型で実装する構成や合成を検討する。基底自身の適合は正当であり、同名禁止の例外は設けない。

具体 Core への Supports は base graph により存続するが、静的 conformance 一般の存続は保証しない。将来の runtime Contract View は、RuntimeUsable の制限・固定 binding・公開保証を通じて、その View の Supports が全派生層で維持される条件を拡張側で定める。

### 3.3. ObjectCompatible

#### 3.3.1. 判定単位と使用条件

ObjectCompatible は、receiver の完全性・借用権限を保つ呼出しの公開保証である。通常の Type・access・Origin・Loan 検査も引き続き必要とする。

公開状態は**呼出し操作の Identity ごとに一つ**とする。関数と custom/computed get/set は各宣言の Identity を用いる。receiver kind は Signature に保持し、型置換ごとの公開状態は作らない。receiver のない helper は効果要約を持てるが、ObjectCompatible の状態は持たない。

標準 get/set の直接アクセスは Place 操作のまま、Copy・Move・Borrow・Replacement の操作別条件で検査する。`Box<T>.item@ref` を T の Copy 証明に依存させない。Contract の標準 witness は呼出し操作として検証し、その Identity に conformance mapping 内の Requirement Identity・Property Identity・bridge 種別を識別させる。結果 Type・公開前提を保持し、異なる witness を一つの標準 getter にまとめない。

| 公開状態 | 基底 subobject projection / object borrow による呼出し |
| --- | --- |
| Proven | 通常の呼出し検査を満たせば使用可能 |
| NotProven | 使用位置でエラー。完全な通常値への呼出しは禁止しない |

NotProven は「共通保証を証明できない」であり、全 binding の不適合を証明する Refuted ではない。caller の追加前提、具体化、実体の Dynamic Type、選択される一つの specialization で状態を強めない。使用違反による overload 再選択もしない。

#### 3.3.2. 効果の検証

公開結果を解析精度・最適化・処理順で変えないため、次の抽象検証を共通規則とする。入力は最適化前の解決済み操作と取得計画であり、Signature・Constraints・条件付きメンバーの premise が許す全 Type/Origin binding を検査する。証明には SPEC §8.7–§8.10 の規則を使い、本体から隠れた呼出し条件を追加しない。

効果要約は receiver・各入力・capture/static anchor と操作の対応を保持する。格納領域との関係を Whole（全体）、Base（基底経路のみ）、Part（Field/Tuple/array 要素を含む）、Separate（分離を証明済み）、MayAlias（未特定）の有限集合で表す。操作種別・返却 alias・Origin/Loan の情報を残し、callee の NotProven という真偽値だけに縮約しない。

| 操作・依存 | 要約と検査 |
| --- | --- |
| receiver の Whole/Base の置換・再構築・所有取得 | 保存違反 |
| receiver の格納領域から、不完全にする MoveOut | 保存違反。後の埋戻しで取り消さない |
| Part の完全性を保つ Replacement / Exchange、適法な読取り・借用 | それだけでは保存違反にしない。通常の access/Loan/cleanup 条件を検査 |
| receiver/base を操作できる unrestricted exclusive borrow の流出 | 保存違反。返却・格納・callee 経由も追跡 |
| 借用結果 | root との対応を保持し、Origin/Loan と返却権限を検査 |
| Separate の操作 | 分離が証明された root に対しては保存違反にしない |
| receiver に禁止効果を及ぼし得る MayAlias、保証のない unsafe/間接操作 | 未証明効果。単なる読取り等の既に証明済みの効果まで禁止効果に変えない |
| call、custom accessor、default、cleanup | 公開 root 別要約を引数・capture/static の経路へ写して合成 |
| 分岐・繰返し | 型検査対象の全経路の和集合。環境選択済みの除外以外は、実行時条件や最適化で削らない |

経路合成では base のみなら Base、要素格納を含めば Part とする。ただし参照値を格納する Field と、その参照の referent を混同しない。参照先は既存の anchor/alias 対応から判定し、分離を証明できなければ MayAlias とする。ある入力からの Separate は、別の入力・capture/static からも分離している証拠にはならない。

Part へ写した callee の全体置換は、caller 全体の置換とは扱わない。一方、操作対象自身が基底 subobject である等の使用制限は保持する。粗い Part 分類で個別の projection 検査を消さない。Replacement / Exchange は既定の完全性保持操作として要約し、lowering 内部の転送を独立した不正な MoveOut と数えない。

同一ビルドの呼出しグラフでは、宣言 schema 上の有限な root 関係・操作効果を直接効果から和集合で伝播し、再帰を含む最小固定点を求める。型の具体化を列挙しない。要約の合成にも §3.3.3 と同じ実装集合を使い、具体的な呼出しを理由に specialization を除外しない。再帰自体は未証明効果ではないが、途中の空集合を公開保証にしてはならない。

別 Kotonoha・関数値・generic requirement の呼出しは、検証済み公開要約または効果契約を使う。任意の効果保証がない場合は、影響し得る root に未証明効果を伝播する。私有本体の閲覧や候補実装の列挙で補わない。必須の artifact 情報の欠落・破損はこの保守的扱いではなく、不適合として診断する。

通常の意味検証を満たし、全許容 binding について receiver の違反・未証明効果がなければ、その実装の証明は成功する。意味上有効でもこの検証を通らなければ共通保証を与えない。Unknown は内部の途中状態に限り公開しない。未実装の解析や本体エラーを NotProven へ読み替えて受理しない。

#### 3.3.3. 実装集合の共通保証

> 公開状態は、通常本体と、環境選択・生成・宣言収集後に閉じた explicit specialization 集合の全実装の証明の論理積とする。

通常の総称本体は全許容 binding で独立して検証し、specialization の担当 binding を除いて救済しない。各 specialization は代入済み契約で検証する。現在の call site の有無で集合を減らさない。

通常本体の推論 Proven を specialization の宣言義務にはしない。不適合な specialization があれば公開状態を NotProven とし、それだけでは宣言エラーにしない。元の Signature・Constraints・Safety 等の継承契約違反は宣言エラーのままである。通常本体の改善は既存 specialization に新しい義務を生じさせない。

使用可否は元の公開契約で判定し、その後に実装を選択する。エラーには原因実装の Identity・理由を付記し、specialization の型引数や依存する callee/witness まで追跡可能にする。利用側へ私有本体を開示する必要はない。

### 3.4. 依存検証と API 互換性

同名検査、継承 conformance、効果要約・公開状態は、依存した宣言内容を保持した判定として SPEC §21.3.4 に統合する。§18.3 の Symbol/access/Signature/mapping 情報に検証要約・閉じた実装集合を対応させ、別系統の依存検証や固定のバイナリ形式は追加しない。

参照した Member だけでなく、**宣言が存在しないという判断に使った Container の完成済み内容**も追跡する。宣言一覧または内容キーで Name・specialization の追加を検出する。package version の一致だけでは足りず、環境選択と検証規則の版も前提に含める。

利用時に実際の依存内容を照合し、不一致なら依存側の判定・生成結果を再検証する。必要条件を失った場合だけ、その派生宣言・conformance・制約使用・呼出し位置で診断する。条件を満たす変更は更新して受理する。必要情報がなく再検証できなければ、再ビルドを要する artifact 不適合として拒否する。旧証明の流用やロード順による選択は認めない。

Name 追加、access 拡大、Signature 変更、Proven 撤回、specialization 集合の変更は、既存の利用側を失敗させ得る API 変更である。「破壊的」はこの互換性分類であり、上流に未知の下流を調べて警告・エラーを出す義務はない。

## 4. Object と拡張設計の整理

### 4.1. 現行 Object の規則

Object View、明示 upcast、既定の Type test、receiver adjustment、完全な動的破棄を維持する。基底 subobject に receiver を調整しても、静的に選択した Member Identity は変えない。

Descriptor は Runtime Type Identity、定義済み Supports に必要な関係、動的破棄、layout/receiver 調整情報を持つ。ObjectCompatible は compile-time interface 情報であり、Descriptor の必須項目にはしない。receiver 調整用 adapter は実装可能だが、「verified object entry」を独立した使用許可根拠にはしない。

基底 View からの解放でも、実際の Dynamic Type の派生層から基底層を一度だけ破棄し、元の割当てを解放する。SPEC §16.3.1 の virtual 固有の禁止文は削除し、§16.4 の破棄中の当該 object への runtime dispatch 禁止に統一する。helper 経由や最適化で直接呼出しになった場合も意味上の操作で判定する。独立して生存する object は対象外。既存の nonescape、破棄済み層へのアクセス制限、constructor の self 呼出し禁止は維持する。

### 4.2. 未導入 modifier の診断

`virtual`・`override`・`abstract` は、extension の前例に従い、宣言先頭の modifier 列で文脈的に認識し、共通の未導入機能診断で拒否する。型・関数・Property、Contract requirement、constructor、deinit、accessor を含み、Access/open を置ける位置だけに限定しない。

後続の既存 modifier・宣言開始語を同一の論理的な宣言 header 内で確認する。`abstract open struct`、requirement の `virtual func`、`virtual init`、`override deinit`、`abstract get` が対象となる。独立項目を区切る改行・indent/dedent を越えて探索しない。

`struct abstract`、`func virtual(...)`、`let override: i32`、`x.abstract()`、`virtual(...)` は通常の Name として扱う。単独の Name 式 `abstract` と次行の func 宣言を結合しない。これは診断・回復用の認識であり、全位置の予約語化や有効な将来構文の追加ではない。

### 4.3. 拡張設計の所有節

SPEC §6.2.4 に virtual/override の意味論・図を集約し、現行節から重複を除く。Appendix D/F は状態と所有節への参照にする。保持する設計は次のとおり。

- 明示された overridable 宣言に slot identity を与え、有効な明示 override だけを同名禁止の例外とする。通常メンバーを暗黙に virtual 化しない。
- receiver kind、正規化後の parameter/result Type を対応させる。共変 result は追加しない。label/default は静的宣言に従い、前提・入力 Origin を強めず、結果寿命保証を弱めない。
- accessible な対象だけを override し、accessor を含め access を保持する。別 Kotonoha の protected-internal override を protected とする既存の例外もここに集約する。
- 明示 virtual/override の実装には独立して Proven を要求し、違反を宣言エラーにする。通常メンバーの推論結果と異なり、明示的な保証義務である。
- runtime Contract と併用する場合は、有効な override に requirement mapping を対応させ、各 requirement を再検証する。不適合な override は拒否し、conformance を黙って失わせない。

導入時には最終構文、対象・generic の適格性、accessor 単位、abstract と構築可否、基底実装呼出し、slot/metadata、separate compilation を一緒に確定する。追加の実行時選択表は導入する拡張が規定する。runtime Contract View 自体は §8.5 が所有し、override との連動だけを §6.2.4 に置く。

## 5. 実装手順

### 5.1. 現状と分割単位

[CoreIntrinsics.cs](../../Kimi/Compiler/Binding/CoreIntrinsics.cs) は Result に opt-in を生成せず、ValidEnum もその形を検査している。[EnumBindingTest.cs](../../xUnitTest/Tests/EnumBindingTest.cs) と [EnumOwnershipTest.cs](../../xUnitTest/Tests/EnumOwnershipTest.cs) も現行の Non-Copy を前提とする。宣言だけの修正で完了させない。

[STATUS.md](../../STATUS.md) では ObjectCompatible の本体・callee 検証、specialization、外部 Kotonoha・binary interface 等が未完了である。既存の未解決義務を成功や NotProven に置き換えず、次の単位で実装する。

| 段階 | 作業・主な着手先 | 完了条件 |
| --- | --- | --- |
| R：Result | CoreIntrinsics、Binding.CopyDeclarations / Binding.Enums、取得・match 解析を更新。Core shape は生成順と独立した条件集合の照合に整理 | §6 の R 群。共有読取りと警告の未実装部分は明示的に追跡し、R 全体の完了扱いにしない |
| S1：宣言集合 | Parser と共通宣言解析で modifier 診断。Binding.Access 等で全祖先の同名検査、確定した検索層と依存 Container を保持 | S 群の構文・同名・access・refinement。Mod/fragment 完成前の判断を最終結果にしない |
| S2：検証情報 | §3.3 の操作 Identity、witness、root 効果、原因・依存をモデル化。取得/所有権解析と要約の合成を接続し、再帰固定点・実装集合を検証 | O 群。未確定の空要約・情報欠落・未実装を Proven にしない。検証完了した無効果の要約は有効 |
| S3：適合と使用 | Binding.ConditionalConformances / Binding.ContractMatching / Binding.PropertyMatching と継承 receiver 対応に §3.2 を反映。S2 の保証で projected/object 呼出しを検査 | C 群。通常の本体・適合義務を完了し、mapping を確定 |
| S4：artifact と生成 | §18.3 / §21.3.4 の依存内容検証・再検証を実装。確定 mapping と取得/cleanup 計画を lowering に渡す | A 群・動的型の保持。外部検証や実行が未実装なら該当機能の完了を主張しない |
| D：文書同期 | §5.3 の箇所を更新し、STATUS に設計と実装の完了範囲を別記 | 古い肯定的 hiding 例、現行の virtual entry 義務、重複規則が残らない |

R と S1 は独立して進められる。S2/S3 は下記の検証順序で接続する。S4 は両方の確定情報を必要とする。部分実装を受理範囲の拡大として扱わず、未対応の意味検証が必要な使用は既存の診断で拒否する。

### 5.2. 一ビルド内の検証順序

1. 環境選択・生成・fragment 統合を完了し、宣言 Identity、base graph、specialization 集合、依存 Container を閉じる。
2. 公開 Signature と premise を検証し、同名検査・通常の lookup・receiver 対応を行う。conformance mapping と呼出し対象は未解決義務を伴う候補として保持できるが、検証済み証拠として使用しない。
3. 解決した操作から直接効果を集め、呼出し・specialization を含む効果固定点を計算する。lookup に必要な情報が未解決なら先に解決し、空要約で穴埋めしない。
4. 実装別の証明、公開状態、継承 conformance、各使用義務を依存順に確定する。効果閉包の固定点は循環した conformance の証拠ではない。独立した証拠なしの循環や残存義務は、既定の証明期限で診断する。
5. 完了した検証情報だけを公開し、確定対象を選択・生成する。依存変更時は影響する段階から再実行し、古い mapping・効果・生成物を混用しない。

ObjectCompatible の結果を理由に lookup/overload/specialization 候補を再選択しない。通常の semantic error と、意味上有効な実装の NotProven を診断・内部状態の両方で区別する。

### 5.3. SPEC 反映先

| 所有節・参照箇所 | 反映内容 |
| --- | --- |
| §3.5.2、§8.4.8、§17.2.2、§22.1 | conditional Copy、FileError、Core 条件集合。能力導出の重複定義は避ける |
| §4.6.6、§15.1.6、Appendix D.1 | 結果 Type と移行例、Pattern 取得指定の制限 |
| §17.2.3、§17.4 | Copy と独立した警告、破棄単位の優先順位 |
| §6.2.2、§9.5、§14.10.1、§18.3 | 同名禁止、層コミット、保証を限定した refinement 例、API 互換性 |
| §8.4.4–§8.4.5、§8.7、§9.5.1、§11.4 | 継承成立条件・mapping・診断の所有規則と参照 |
| §8.5、§13.5.7 | 静的 conformance と存続を保証する Supports の区別 |
| §12.4.3–§12.4.4、§8.8.2–§8.8.3、§8.10、§15.6.4 | 静的選択、公開状態と効果合成、specialization の共通保証 |
| §18.3、§21.3.4 | 検証情報と依存内容検証。不在の宣言を前提にした判断も含む |
| §2.5.1、§6.2.3、§16.3 冒頭 / §16.3.3 | modifier の共通診断へ統一 |
| §16.3.1、§16.4、§21.2.1–§21.2.2 | 破棄の一般規則、Descriptor。vtable の義務を除き、最適化による直接解決は一般形で記述 |
| §6.2.4、Appendix A.3 / A.8 / B.4 / D / F.9 | 拡張意味論・図の集約、検証境界の更新、状態と所有節への参照 |

反映時は節番号だけでなく `virtual`、`override`、`abstract`、`dispatch`、`effective implementation`、`vtable`、`devirtualization` と相互参照を横断確認する。

## 6. 受入条件

以下は今後の実装の完了条件であり、本書の編集によって実行済みとなるテストではない。既存テストを更新し、新しい規則の境界を追加する。

| 群 | 必須の確認 |
| --- | --- |
| R：分類・取得 | §2.1 の完全 Type / 総称証明表。両側条件・入れ子・active Case 非依存、Copy 後の再利用、Non-Copy の Move、借用依存の維持と cleanup の一回性 |
| R：共有読取り | §2.3 の全行、推論・引数適合。総称 head の `s[0]` は定義エラー、`s[0]@ref` は元の storage/Origin を保持。Pattern 借用指定を誤って受理しない |
| R：Core | 条件の両順序・括弧・重複を受理。欠落・無条件・片側のみ・余分な原子・別 Copy Identity を拒否。ユーザー Result を名前で特別扱いしない。実装済みの cache/生成物を旧 Core の前提で再利用せず、未対応の外部 artifact は拒否 |
| R：警告 | unintended-Unit と競合する Result は最上位のみ。その他の Result は固有警告のみ、別破棄は独立。Copy/Non-Copy の両方を確認 |
| S：同名・access | Field/stored/computed Property/関数の相互衝突、instance/type function、祖先・fragment/Mod・条件付きメンバー。public/protected/同一 Kotonoha の internal を拒否し、inaccessible な Name は許可。非公開 accessor で回避できない |
| S：静的選択 | parameter/let の refinement 前・中・join 後、var、具体型・基底 View で既存 public メンバーの宣言層が同じ。別名メンバーの追加利用、使用失敗時の fallback 禁止 |
| S：modifier | struct/contract 宣言、requirement、constructor、deinit、accessor で共通診断。通常の同名 Name と次行の独立宣言を維持 |
| C：継承成立 | Proven な Stringify 型は継承。Equatable/Comparable、owner receiver、Self 結果は不成立でも派生宣言は有効。Copy は自動継承しない。固定関連型・別経路・Unknown/Error を区別 |
| C：mapping・診断 | A → B → D の宣言元 Self、型/Origin 置換、返却借用、複数経路の一致。明示適合と制約使用の両方で requirement と原因を示す。open 基底には警告しない |
| O：単位・境界 | 操作 Identity ごとに公開状態一つ、Unknown 非公開。標準 Place の借用を Copy に依存させず、異なる witness を分離。完全値の self 置換と object/projection での拒否を確認 |
| O：効果 | Whole/Base 置換・MoveOut・exclusive 流出を検出。Part の完全性保持置換は許可。Field 内参照と referent、別入力 alias、返却・capture/static・default・cleanup・間接/generic 呼出しを検証 |
| O：順序・再帰 | 保存違反のない相互再帰を固定点で証明し、違反は依存先へ伝播。循環 conformance は正当化しない。宣言順・解析順・最適化で公開結果と受理可否が変わらない |
| O：specialization | 一実装でも不適合なら公開 NotProven。その理由だけでは宣言エラーにせず、使用エラーが原因実装を示す。本体改善で既存 specialization を不正にしない。未使用実装も集合に含める |
| A：再検証 | Name 追加・access 拡大・Signature 変更・Proven 撤回・specialization 追加で古い判断を無効化。要件を失った依存側は診断し、衝突しない変更は再検証後に受理。欠落情報・旧 Core・ロード順も確認 |
| A：実行 | 確定 mapping の使用、Type test・receiver adjustment の維持、基底 View からの完全な動的破棄と元 storage の解放。helper/最適化で破棄中の禁止を回避しない |

Result は EnumBindingTest / EnumOwnershipTest / MatchOwnershipTest / CoreCatalogTest、継承・witness は InheritedReceiverBindingTest / ConditionalConformanceBindingTest / PropertyBindingTest、構文は PropertyRevisionParseTest 等を起点にする。効果・artifact・実行の新規検証は対応段階に追加する。

各段階で対応するテストを実行し、統合時に `dotnet test xUnitTest/xUnitTest.csproj` で回帰を確認する。実行時の条件は実際の生成・実行テストで確認し、Binding テストや source-snapshot の再読込みだけで代用しない。文書同期ではリンク・相互参照・旧規則の残存を検査する。
