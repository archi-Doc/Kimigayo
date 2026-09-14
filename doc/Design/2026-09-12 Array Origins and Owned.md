# Array の Origin 伝播と Owned

2026-09-12。[SPEC.md](../../SPEC.md) に反映した設計。実装完了を意味しない。[レビューの採否](../Decisions/2026-09-12%20Borrow%20Retention%20Review.md) と [実装計画](2026-09-12%20Borrow%20Retention%20and%20Lifetime%20Plan.md) を参照。

## 1. 基本規則

```text
Complete Type
    → 通常の型形成規則のもとで Origin を含められる

Array / Dictionary / Tuple / fixed array / struct / enum
concrete Closure / concrete object payload (obj/rc/arc)
    → 内容の Origin・Loan requirement と実際の Loan を伝播する

Owned
    → OwnedOrigins(T) の全 Origin が static と証明できる
       依存がなければ成立

Ordinary storage
    → Owned 不要

Current base/contract payload erasure
Current common Function Type environment erasure
Static storage
    → Owned 必須

Lifetime-hiding library API
    → 必要な T is Owned を公開制約として明示する
```

heap allocation 自体は追加の寿命条件を生まない。有限レイアウト、初期化、Semantics、アクセス権限、Loan は通常どおり検査する。

## 2. Owned の唯一の定義

定義は SPEC §15.2.3 の **OwnedOrigins(T)** に集約する。実際の safe-borrow Field だけではなく、外側の Origin、全 Type/Origin 引数（未使用 slot を含む）、raw pointer の pointee Type、base、Field、全 enum payload、sequence component、concrete Closure の capture を再帰的に調べる。alias 展開と置換後の束縛を使い、再帰構造は構造解析の固定点で扱う。

callable の公開引数・結果の契約と per-call binder は environment ではない。その契約は保持し、environment の Owned 証明から書き換えない。

`a : static` は static が最大 Origin であるため等値を証明する。境界のない抽象 a は Unknown であり、必要な証明を期限までに得られなければエラーとする。Unknown を `not Owned` の証明にしない。

| 例 | Owned |
| --- | --- |
| i32、所有する string、Array<string> | 成立 |
| `ref/i32 from static` | 成立 |
| `Array<ref/i32 from a>` | a が static と証明できれば成立 |
| `RawBox<ref/i32 from local>` | 不成立。unsafe/T Field しかなくても Type 引数を調べる |
| 未使用 slot に非 static borrow を束縛した型 | 不成立 |
| local borrow を capture する concrete Closure | 不成立 |

raw pointer の Type を調べても dereference や Loan の生成は行わない。型から寿命を隠した unsafe 実装の正当性や pointer の有効性は、Owned が保証するものではない。unsafe 実装は公開 API の寿命契約を守る必要がある。

`owner` は所有・取得 Semantics、`Owned` は非 static な寿命依存からの独立性である。Copy、排他性、並行安全性とは別であり、static borrow を含み得るため「外部依存が一切ない」という意味にはしない。

## 3. Array<T> の型と値

Array は通常の要素レイアウト要件を満たす任意の valid complete T を認める。Owned、Copy、Storable は要求しない。Array 自体は Non-Copy であり、要素が borrow の場合は借用値を所有し、借用元を所有しない。

```kimi
func singleton<T>(value: T) -> Array<T>
    return [value]

func collect<T> origin source(
    a: ref/T from source,
    b: ref/T from source)
    -> Array<ref/T from source>
    return [a, b]
```

singleton は T を一度取得し、その完全な依存を結果へ転送する。本文内だけで Array を作る関数にも追加制約は不要である。通常の generic universal verification を行い、本文の未証明の能力要件を具体化まで保留しない。

Array 専用の Origin パラメータは追加しない。T の複数 Origin を保持する。local の省略 Origin は初期化子と使用制約から推論するが、後の append/assignment から Type 推論を再開しない。公開契約は private body から推論しない。

型は Origin と Loan requirement を保持し、値は具体的な Loan Identity、storage anchor、Reborrow 関係を伝播する。同じ source の a/b でも借用元は別であり得る。Origin の meet を簡約しても Loan を統合しない。保守的な Type 依存は、それ自体では実際の Loan を作らない。

```text
Owned(Array<T>)        ⇔ Owned(T)
Owned([N of T])        ⇔ Owned(T)
Owned(Dictionary<K,V>) ⇔ Owned(K) and Owned(V)
```

これらは well-formed な Type に適用する。空配列、N = 0、選択 Case、要素削除で型の判定を変えない。

## 4. 操作・生存性・破棄

| 操作 | 規則 |
| --- | --- |
| 格納/Move | 要素の所有・依存・破棄責任を移す。借用元の寿命を延長しない |
| shared borrow 要素の Copy | 元の参照の Loan を維持。要素スロットへの新しい借用ではない |
| 要素スロットの `@ref` / Slice | 配列 storage の Loan と内側の依存を維持 |
| uniq 要素の shared read | shared Reborrow。排他的 capability を複製しない |
| append/insert/remove/clear 等の構造変更 | Array 全体への排他アクセスを要求。active view との衝突は通常の Loan 規則で検査 |
| 要素 replacement | 固定された T への適合、更新権限、旧値の破棄を検査 |
| 明示的 remove/pop | 結果へ T と依存を転送。通常の動的 index 読み取りに Non-Copy Move を追加しない |
| consuming iteration | 要素を順に転送し、途中終了時は残る要素を破棄 |

reallocation は構造変更の効果であり、独立した寿命検査機構を設けない。実際に再確保しない append でも、必要な排他アクセスと active Loan が競合すれば拒否する。

Array の要素位置は Origin の極性を保存し、内側の variance を合成する。`uniq/Array<T>` は完全な参照先 Type について不変であり、長寿命要素の Array に短寿命参照を書き込める alias を作らない。異なる Core 間の一般的な container covariance は追加しない。

Array は要素由来の Loan の保守的な集合を伝播する。remove/replace/clear で要素単位に集合を縮めない。Loan の生存性は、その依存を持つ配列・結果・派生値の必要な使用と観測可能な破棄から決める。型に同じ Origin があるという理由だけで、無関係な Loan を同一視しない。字句スコープの終端まで一律に生存させる規則でもない。

配列中の共有参照を Copy して取り出せば、借用元が有効な範囲で配列の終了後も使える。一方、配列のスロットやその storage への borrow は配列の終了後に使えない。

§16.3.2 に従い、残る初期化済み要素を論理 index の降順で破棄する。spare capacity や移動済み要素を破棄しない。user deinit による依存の観測、途中構築、defer、return 前 cleanup、最後の strong-owner release も検査する。新しい mutation API の構文は本改訂に含めない。

## 5. Owned を要求する場所

規範上の境界は、static storage、現在の base/contract payload 消去、common Function Type の environment 消去、明示的な Owned Constraint に限定する。コンパイラは API の保持期間を推測して隠れた Owned 制約を追加しない。

通常の格納からは Owned を要求しない。現在の型消去では隠れる payload/environment に要求し、object handle の外側の借用 Origin や callable の公開引数・結果の契約は独立して保持する。

**非規範の将来注記。** 保持寿命を公開できる erased view/API を設計すれば、その境界に適合する非 Owned 値を許せる。本改訂はその構文や保証を追加しない。

### Static

static storage の完全 Type は Owned を満たす必要がある。初期化済みで、参照する経路が safe mutation から保護された immutable static を由来とする shared static borrow は保存できる。

mutable static の slot・inline subplace・更新で無効化される backing data への新しい safe borrow は有限の Origin を持ち、static に適合させられない。generic 置換や getter を経由しても同じであり、Owned を要求する消去・保存先には渡せない。mutable slot から既存の参照値を Copy する場合は、その参照の元の Origin/anchor を保つ。

有限な mutable-static Loan には従来の効果 summary を用いる。別 module は公開された検証済み summary を使い、不明な効果は保守的に扱う。private body を呼び出し側で調べる規則にはしない。immutable anchor も shutdown の依存確認には残す。逆初期化順の破棄で依存が生存できなければ拒否する。

### Checked cast

現在の payload 消去の Owned 証明が対象とした data-Origin binding に限り、欠けた binding を static として与えられる。`Box<ref/i32 from static>` への cast は、Runtime Type Identity/Supports、対象 Type の成立、Semantics、Loan の全条件を満たせば許す。

これは過去の Origin の runtime 復元ではない。非 static binding、handle の外側の Origin、Owned 証明の対象外である callable 契約は復元・書き換えない。必要な証明または保存済み契約がない対象は拒否する。checked cast のソース構文は引き続き未設計である。

## 6. SPEC の反映箇所

| 内容 | 主な節 |
| --- | --- |
| OwnedOrigins の単一定義、raw pointee/未使用引数/Origin bounds | §15.2.3 |
| Array と aggregate の格納、推論、保守的 Loan、Core 条件 | §4.3–§4.6、§6.3、§8.1.3、§13.5.8、§15.4、§22.1 |
| static 格納と mutable-static borrow | §8.1.3 末尾、§11.3.2、§15.2.3、§15.4、§15.6.4、§22.2.3 |
| 型消去、cast、明示 API 制約 | §13.6.2、§15.8.1、§15.8.3 |
| owner と Owned の用語分離 | §3.1.4、§3.3、§3.5、§7.3/§7.6.3、§8.6、§14.6.2、§15.1.5、§21.4.2 ABI 表、Appendix B の AccessMode |
| heap/global 全体の deferred 記述を除去 | §15.9、Appendix D |

## 7. 受け入れ条件

| ケース | 期待 |
| --- | --- |
| 制約なし singleton と本文内 Array | 定義時に成功 |
| 入力由来の参照を格納して返す / local 由来を返す | 前者は依存を伝播、後者は寿命不足で拒否 |
| 同じ Origin の異なる借用元 | 個別の Loan を保持 |
| shared 参照値の取り出し / slot borrow の返却 | 元の借用元と配列 storage の寿命を区別 |
| unique 要素の格納と読み取り | Move/shared Reborrow を適用、複製を拒否 |
| append と active Slice | 再確保の有無にかかわらず通常の排他 Loan 衝突で拒否 |
| `uniq/Array<ref/i32 from long>` から短寿命参照を格納 | 拒否 |
| remove/clear の後も型に依存を持つ配列を使う | 要素ごとの Loan 解除を行わない |
| user deinit の観測前に借用元を破棄 | 拒否 |
| RawBox/未使用 Type slot に local borrow | Owned 不成立、capture 後の common Function Type 変換も拒否 |
| `a : static` / bound のない抽象 a | 前者は他の依存も満たせば Proven、後者は Unknown のままなら必要な証明の期限でエラー |
| immutable static borrow の配列/static 保存 | 初期化・Loan・shutdown 条件を満たせば成功 |
| mutable static への borrow を消去・別 module の Owned API に渡す | 拒否。有限 Origin を static にしない |
| 非 static payload の具体的 object 生成 / 現在の型消去 | 前者は依存を伝播、後者は拒否 |
| 消去 object から Origin 引数付き Type への cast | 証明対象の data-Origin は static のみ。非 static の推測と callable 契約の復元を拒否 |
| concrete Closure の Array 格納 / common Function Type 変換 | 格納は capture の依存を保持、変換は environment の Owned を要求 |

実装時は、未サポートによる拒否を上記の負例の合格と数えない。要求された寿命/Loan/制約の理由で判定する。
