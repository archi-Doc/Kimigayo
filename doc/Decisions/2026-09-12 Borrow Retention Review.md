# Borrow retention レビューの採否

2026-09-12。対象は [Array の Origin 伝播と Owned](../Design/2026-09-12%20Array%20Origins%20and%20Owned.md)。採用した規則と関連する格納改訂を SPEC.md に反映し、実装との差は STATUS.md に記録した。

| 指摘 | 判断 | 理由・適用内容 |
| --- | --- | --- |
| 1 raw pointee/未使用 slot | (a) を採用 | OwnedOrigins に全 Type/Origin 引数と pointee Type を含める。保持宣言の新構文を要さず、RawBox 等で型に現れる非 static 依存を見落とさない。実際の Loan 生成と unsafe 実装の正当性は別 |
| 2 無期限保持 API | 採用 | コンパイラが要求する場所を static storage、現在の payload/environment 消去、明示的 Owned Constraint に限定。「無期限を本文から判定して制約を追加する」規則にはしない |
| 3 mutable static | 第一案を採用。ただし根拠を限定 | safe code の新しい mutable-static borrow は有限 Origin とし、from static を禁止。隠れた長期保持の照合を減らす。既存の modular summary が分割コンパイルと必ず衝突する、という主張は採用しない |
| 4 図の分類 | 採用 | concrete Closure、Dictionary、具体的 obj/rc/arc payload を記載。現行消去は requires Owned とする |
| 5 reachable/保持 | 採用 | 保守的な Type-level の OwnedOrigins を §15.2.3 の単一定義とする。実際の保持値の Loan と区別し、両者を同義語にしない |
| 6 要素単位の追跡 | 修正して採用 | remove/replace/clear で要素別の Loan 解除をしない。ただし「型に Origin がある値が生きていれば十分」という定義にはせず、provenance、必要な使用、破棄観測を維持 |
| 7 reallocation | 採用 | 構造変更が必要とする全体への排他アクセスと既存の Loan overlap から導く。実際の再確保の有無による別判定を設けない |
| 8 checked cast | 範囲を限定して採用 | Owned の証明対象だった欠落 data-Origin のみ static として与える。Box<ref/i32 from static> は他の全条件を満たせば対象にできる。非 static の復元や任意の Origin の static 化は認めない |
| 9 将来 erased view/API | 採用 | 現行の規範表から除き、将来設計の非規範注記と計画に置く |

## そのまま採用しなかった部分

**1 の安全性の範囲。** 新しい Owned は型に現れる依存を保守的に検査する。raw pointer のアドレスが local を指すのに Type にその寿命が全く現れない場合まで自動検出する保証ではない。安全な公開メソッドを持つ unsafe 実装は、実際の寿命契約を正しく表現して守らなければならない。Rust も型の寿命条件と unsafe な表現の契約を区別する。[Lifetime bounds](https://doc.rust-lang.org/reference/trait-bounds.html#lifetime-bounds)、[PhantomData](https://doc.rust-lang.org/nomicon/phantom-data.html)

**3 の分割コンパイル。** 検証済み effect/anchor summary を artifact に公開し、不明な効果を保守的に扱えば、呼び出し側で private body を読む必要はない。これは既存 §15.6.4/§18.3 の方針と整合する。ただし mutable-static 借用を消去後まで保持できる設計は複雑なため、今回は禁止側を選ぶ。有限の local Loan と immutable static の shutdown 依存には summary が依然として必要である。

宣言を「どこかで借用されたら永久に書き込み不可」に変える第二案は、宣言の意味が利用箇所に依存するため採用しない。既に mutable slot に保存されている shared reference を Copy する操作と、slot 自体を借りる操作は区別する。

**6 の生存性。** 同じ Origin の参照が異なる storage を借りる場合があり、Origin だけでは Loan の対象を決められない。また、字句スコープ、最後の通常使用、デストラクタの観測は一致しない。要素ごとの精密な解除を導入しないことは採用するが、provenance と §15.6.6 の破棄検査は残す。この保守的な配列規則を Rust NLL 全体と同一とは記述しない。

**8 の復元範囲。** Owned environment の証明は、callable の公開引数/結果の lifetime contract を static にする証明ではない。object handle の外側の借用 Origin も payload 証明の対象外である。これらまで static に置換すると別の不整合になるため、cast は証明で保証された data-Origin に限る。Runtime Type Identity の一致だけでは cast の静的合法性を証明しない。

## 追加反映

§8.1.3 末尾の旧 static borrow 禁止、Array/Dictionary の Core 条件、heap/global 全体の deferred 記述を更新した。owner の意味の Owned は string、Copy 表、parameter、Closure/Callable receiver、iteration、let、ABI 表まで確認した。ABI 表は現在 §21.4.2 にあり、参照番号の §19 は使わない。内部 AccessMode の既存ラベルは capability と異なることを明記した。

受け入れ条件には RawBox/未使用 slot、a : static/Unknown、排他 Array への短寿命注入、mutable-static 借用の消去/別 module 越え、Origin 引数付き checked cast を追加した。実装テストは未追加であり、設計書のケースを実行済みとは扱わない。

## SPEC 反映後の精査による補正

| 箇所 | 補正 |
| --- | --- |
| §15.2.3 OwnedOrigins | 走査対象に Semantics target（値の referent、object payload/View Target）と Tuple component・配列要素を明記。「sequence components」の曖昧さを解消。消去済み view は可視の引数だけを寄与し、隠れた payload は消去時の証明に委ねる |
| §15.2.3 と §3.2.1 | 「全 instantiated 引数」と「Function Item は常に Owned」の矛盾を解消。Function Item の束縛済み generic/Origin 引数は callable contract に属し、環境依存ではない |
| §15.2.3 Unknown | generic 定義自身の Type parameter/抽象 Origin の Owned 証明は宣言済み Constraint/bound だけで行い、instantiation まで保留しないと明記（§8.10 と整合） |
| §11.3.2 mutable static | 有限 Origin の借用を関数/getter から返す手段は borrowed input で上限を与える result Origin と Field anchor に限り、static へ elision される結果はエラーと明記 |
| §15.4 Static Field | 省略 Origin の static 既定から排他 borrow 層と `uniq` Loan requirement を除外（結果 elision の規則と一致） |
| §15.4 例 | immutable static からの static 借用の正例と、mutable static からの負例を併記 |
| §4.6.5 | Slice の Owned 判定の参照先を §15.2 の一般 Origin 規則から OwnedOrigins へ変更 |

## 残った二つの判断

| 論点 | 判断 | 理由・適用内容 |
| --- | --- | --- |
| callable signature の Origin | 保守側を採用。上表の Function Item の補正は置き換える | OwnedOrigins は callable Type の固定 Origin（Function Item の束縛済み引数、concrete Closure の capture と固定 signature Origin、common Function Type の引数/結果に書かれた固定 Origin）を含める。除外は §8.6/§15.4 の per-call binder だけ。将来 environment に寿命境界を持つ消去 callable を導入すると signature Origin が実際の保持依存になり、除外規則は健全でなくなるため。Rust も関数ポインター型の自由 lifetime に outlives を要求する。§3.2.1、§7.6.4、§13.6.2、§15.8.1、Appendix A.8 を合わせて更新 |
| mutable static を返す getter | 現行の制限を維持。新構文は追加しない | 使用箇所での直接 borrow、immutable static、scoped callback、値の取得で代替できる。static Place を Origin として書く構文は、公開 signature への anchor 露出、アクセス検査、別 module の summary 照合を伴うため、§15.9 と Appendix D に deferred として記録。input で上限を与える結果は既存規則から導けるので残すが、推奨パターンとはしない |
