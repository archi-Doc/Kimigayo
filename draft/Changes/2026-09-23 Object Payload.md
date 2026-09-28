# ObjectPayload とオブジェクト化の禁止

## 1. 位置付け

本変更案は、ここで定める事項について `SPEC.md` およびその参照先より優先する。それ以外の規則は現行仕様を維持する。本書は変更後の言語仕様を定義するものであり、実装完了を示すものではない。言語は pre-alpha であり、言語バージョンの変更と移行文書は伴わない。節番号は、「本書 §」と書いたものが本書の節を、それ以外が現行仕様の節を指す。

本書は次の三つを定める。

- **A. 共通規則**: Semantics パラメーターの許容集合（本書 §3）と、オブジェクト対象の判定（本書 §5）。現行仕様に散らばっていた判定を、この二つにまとめる。
- **B. `ObjectPayload`**: 値から新しいオブジェクトを作れることを表す組み込み要件を新設する。オブジェクト形成の根拠という役割を `Sealed` から切り離す（本書 §4、§6）。
- **C. オブジェクト化の禁止**: 型の宣言に `Self is not ObjectPayload` と書くと、その型を `obj`・`rc`・`arc`・`objref`・`objuniq` にできなくなる（本書 §7）。

目的は次のとおりである。

- 実装者が想定しないオブジェクト化を、型で防ぐ。防ぐのは、その型を直接 payload にするオブジェクト化である。ヒープ確保をしないことの保証ではない（本書 §7.5）。
- 汎用 API が型引数をオブジェクト化しうるかどうかを、シグネチャの制約から読めるようにする。
- 将来の実行時 Contract View（§8.5）で起こりうる暗黙のボクシングを、先に型で制御できるようにする。

無駄なコピーの防止は目的に含めない。ユーザー定義の struct と enum は既定で Non-Copy であり、裸の place は Move しない（§3.5、§15.1.5）。`ref` を書き忘れても、暗黙のコピーにはならずコンパイルエラーになる。

```kimi
struct Parser
    Self is not ObjectPayload     // オブジェクト化を禁止する
    public var pos: i32 = 0

func boxed<T>(value: T) -> obj/T
    T is ObjectPayload            // 値から新しいオブジェクトを作る汎用コードは、適格性を宣言する
    return Kimi.Intrinsics.makeObj(value@move)

let p = Parser.init()             // 値として使うのは自由
// let o = boxed(p@move)          // エラー: Parser は ObjectPayload を満たさない
```

## 2. 用語

- **オブジェクト系の Semantics**: `obj`・`rc`・`arc`・`objref`・`objuniq` をいう。カテゴリーでは `object` と `objectborrow` の和である。
- **オブジェクト形式**: オブジェクト系の Semantics を型 `X` に当てはめた型（`obj/X` など）をいう。`X` を対象（View Target）という。
- **許容集合**: Semantics パラメーターが取りうる Semantics の集合をいう（本書 §3）。
- **ObjectPayload**: 組み込み要件 `Kimi.ObjectPayload` をいう。値から新しいオブジェクトを作るとき、その payload にできることを表す（本書 §4）。
- **オブジェクト対象**: オブジェクト形式の対象にできる型をいう（本書 §5）。
- **禁止型**: `Self is not ObjectPayload` を宣言した型、またはそれを基底型から受け継いだ型をいう（本書 §7）。

## 3. Semantics の許容集合

§8.7 の証明規則に、Semantics パラメーターについての規則を加える。ほかの要件についての §8.7 の規則（選言から個々の項の証拠を得ないことなど）は変えない。

### 3.1. 計算

組 `<s/T>` の `s` の許容集合は、その位置で使える `s` への Semantics 前提から計算する。

| 前提の形 | 集合 |
| --- | --- |
| 具体的な Semantics の名前（`obj` など） | その一つ |
| カテゴリー（`object` など、§3.3） | カテゴリーの要素 |
| `A and B`／`A or B`／`not A` | 積集合／和集合／補集合（全体は 9 種の Semantics） |
| 複数の節 | 積集合 |
| 前提なし | 全体 |

### 3.2. 判定

要件 `s is R` の集合を `S` とする。

| 条件 | 結果 |
| --- | --- |
| 許容集合が空 | Error（矛盾した証拠） |
| 許容集合 ⊆ `S` | Proven |
| 許容集合 ∩ `S` = ∅ | Refuted |
| それ以外 | Unknown |

```kimi
func f<s/T>(handle: s/T) -> i32
    s is obj or rc
    // 許容集合 = {obj, rc}
    // s is object → Proven、s is borrow → Refuted、s is obj → Unknown
    return 0
```

### 3.3. 適用先

この判定を、Semantics の要件の証明すべてに用いる。現行仕様で定義のないまま使われていた次の判定も、これで決まる。

| 判定 | 条件 |
| --- | --- |
| 組の根拠（本書 §5.1 の 3） | 許容集合 ⊆ オブジェクト系 |
| Weak の組（§3.2.2） | 許容集合 ⊆ {`rc`, `arc`} |
| `s/T during a`・`s/U during a`（§8.1.2、§15.3） | 許容集合 ⊆ `borrow`（`ref`・`uniq`・`objref`・`objuniq`） |

## 4. ObjectPayload

### 4.1. 定義

型 `X` は、正規化した後で次の条件をすべて満たすとき、そのときに限り `ObjectPayload` を満たす。

1. 外側の Semantics が `owner` である。
2. Core が、Never 以外の有効な完全 Core である。open な struct、Scalar、`string`、Unit、enum、Tuple、固定長配列、コレクション、Function Item、具体 Closure、共通 Function Type を含む。
3. Core の名前付き宣言が禁止型でない。

判定は外側の Core だけで行い、型引数や格納された成分は見ない（浅い判定）。Callable と実行時 Contract View は Core ではないので、満たさない。オブジェクトの表現はヒープ確保を要求しない（§3.3.3）。本書の規則は表現によらず、型の形成と作成操作に適用する。

### 4.2. 証明

- **直接の判定**: 本書 §4.1 に必要な外側の構造（外側の Semantics、Core、禁止の有無）が確定していれば、型引数が未確定でも直接判定する。条件のどれかが偽と確定すれば Refuted、すべて真と確定すれば Proven とする。組の元の型 `s/T` の外側の Semantics は、許容集合で判定する。

  | 型 | 判定 |
  | --- | --- |
  | `Box<T>`（`Box` は禁止型でない） | Proven |
  | `Parser<T>`（禁止型）、`ref/T` | Refuted |
  | 型そのものが不正 | Error（§8.7） |

- **前提からの証明**: 外側の構造が確定しない型（型パラメーター、組の対象、正規化できない関連型の射影など）では、次だけが根拠になる。`T is Sealed` から `T is ObjectPayload` は導かない。
  - 宣言された前提（`T is ObjectPayload`）
  - §8.7 の Contract 詳細化の規則による導出（本書 §8）
- **導かれる性質**: Proven の `T is ObjectPayload` は、`T` の外側が `owner` の完全な値型であることを証明する。組の `T` を単独の値型として使う根拠（§8.1.1）にもなる。Copy・Owned・Sealed・スレッド間の受け渡しは導かない。

### 4.3. 書ける場所

| 場所 | 書き方 | 意味 |
| --- | --- | --- |
| 関数・型の制約 | `T is ObjectPayload`、`T is not ObjectPayload` | 入力前提 |
| 条件付き適合の条件 | `Self is C when T is ObjectPayload` | 肯定の条件（§8.4.8.1） |
| Contract の制約 | `Self is ObjectPayload`、`Self is not ObjectPayload` | 適合する型への実装の要件（本書 §8） |
| struct・enum の宣言 | `Self is not ObjectPayload` のみ | 禁止の宣言（本書 §7）。肯定形は書けない |

## 5. オブジェクト対象

### 5.1. 定義

型 `X` は、次のいずれかを満たすときオブジェクト対象である。

1. `X is ObjectPayload` が Proven である。
2. `X` が有効な実行時 Contract View の対象である（§8.5）。
3. **組の根拠**: `X` が組 `<s/T>` の対象 `T` であり、`s` の許容集合がオブジェクト系に含まれる。呼び出し側が渡した `s/T` が有効なので、`T` はすでにオブジェクトの対象として有効である。

2 と 3 は `X is ObjectPayload` を証明しない。どちらも `X` が View の対象でありうるからである。

### 5.2. 形成と操作

- **オブジェクト形式**: `obj/X`・`rc/X`・`arc/X`・`objref/X`・`objuniq/X` を形成するには、`X` がオブジェクト対象でなければならない。
  - 汎用の定義では、シグネチャとボディのどちらで形成する場合も、定義時にこの判定を要する。成り立たなければ定義エラーとする（§8.10）。シグネチャの型が有効であることから、前提を暗黙に導くことはしない。
  - `obj/Box<T>` は、`T` が未確定でも作れる（本書 §4.2 の直接の判定）。
- **組の元の型**: 呼び出し側が渡した `s/T` そのものは形成済みなので、根拠を要しない（§8.1.2）。
- **`s/U`**: 組の Semantics を別の型 `U` に当てはめる場合、`s` の許容集合がオブジェクト系と交わるなら、`U` がオブジェクト対象であることを追加で要する。
  - これはオブジェクト系についての追加条件である。許容集合のすべての Semantics について、既存の型形成規則（§8.1.1、§8.1.2）も満たさなければならない。
  - たとえば許容集合が {`owner`, `obj`} で `U` が View の対象 `C` なら、`obj/C` は形成できても `owner/C` は形成できないので、`s/C` は拒否される。
- **作成**: 値から新しいオブジェクトを作る操作（本書 §9.1、§10）は、`ObjectPayload` を要する。本書 §5.1 の 2 と 3 では足りない。
- **既存ハンドルの操作**: 借用・View の変更・cast など、新しい payload を作らない操作は、`ObjectPayload` を再証明しない。結果の型の形成と、各操作の既存の成立条件を検査する。
  - 例: upcast には `Supports`（§13.5.7）、初回の payload の消去には `Owned`（§15.8.1）を要する。`rc/T` から `objuniq/T` は作れない（§13.5.5）。

### 5.3. 適用先

次の規則は、オブジェクト対象の判定をそのまま用いる。個別の例外は設けない。

| 規則 | 判定 |
| --- | --- |
| 型の形成（シグネチャ・フィールド・ローカル・型引数） | 本書 §5.2 |
| upcast と checked cast（§13.5.7、§13.6.2） | 結果の型の形成 |
| 実行時の `is` テスト（§13.6.1） | 右辺 `X` に左辺の Semantics を当てはめたオブジェクト形式を形成できること。絞り込み（§14.10）で、実効型がその形式になるからである |
| `Weak<S>`（§3.2.2） | `S` の外側が `rc` か `arc` と証明され、その対象がオブジェクト対象であること。組の元の型 `s/T` では、許容集合 ⊆ {`rc`, `arc`} であればよい（対象は本書 §5.1 の 3 で満たされる） |

§13.6.1 の「常に真や常に偽のテストも受理する」原則は変わらない。禁止型への `is` テストが拒否されるのは、形成の規則によるものである。

### 5.4. 例

```kimi
// 値から新しいオブジェクトを作る: ObjectPayload が必要
func share<T>(value: T) -> rc/T
    T is ObjectPayload
    return Kimi.Intrinsics.makeRc(value@move)   // Copy は未証明なので、Move を明示する

// 既存のハンドルから別のオブジェクト形式を作る: 組の根拠で足りる
func inspect<s/T>(handle: s/T) -> i32
    s is object                           // 許容集合 = {obj, rc, arc}
    let view: objref/T = handle@objref    // objref/T の形成に ObjectPayload は不要
    return 0

// 汎用の型のフィールド: 型の制約として宣言する
struct Shared<T>
    T is ObjectPayload
    var inner: rc/T

// 根拠がない
func boxedBad<T>(value: T) -> obj/T       // エラー（定義時）: obj/T の形成に根拠がない
    return Kimi.Intrinsics.makeObj(value@move)
```

## 6. Sealed との役割分担

`Sealed` の意味（外側が `owner` で、Never でも open でもない有効な Core）は変えない。オブジェクト形成の根拠という役割だけを外す。

| 要件 | 表すもの | 主な用途 |
| --- | --- | --- |
| `Sealed` | 派生型が存在しない | payload 投影（§13.5.5.1）、値全体の置き換え（§15.7） |
| `ObjectPayload` | 値から新しいオブジェクトを作れる | 作成、オブジェクト対象の根拠（本書 §5） |

- 両者は独立している。
  - 禁止型は `Sealed` を満たしうるが、`ObjectPayload` は満たさない。
  - open な型は `Sealed` を満たさないが、禁止型でなければ `ObjectPayload` を満たす。汎用コードで open な型をオブジェクト化できるようになる。
- payload 投影には `Sealed` を、オブジェクト形式の形成には本書 §5.1 のいずれかの根拠を要する。組の根拠があれば `ObjectPayload` は要らない。

```kimi
// 組を使わないので、objref/T の形成に ObjectPayload を要する
func borrowPayload<T>(source: objref/T) -> ref/T during source
    T is Sealed and ObjectPayload
    return source@ref/T
```

## 7. オブジェクト化の禁止

### 7.1. 型の宣言の制約節

型の宣言の制約節は、次の三つに分かれる。

| 節 | 種類 | 扱い |
| --- | --- | --- |
| `T is C`（型引数への制約） | 入力前提 | 定義の中で前提として使える（現行どおり） |
| `Self is C` | 適合の宣言 | 既存の適合検証の義務を生じる。宣言しただけでは、自身の実装の証拠にならない（現行どおり、§8.4.4、§8.7） |
| `Self is not C` | 禁止の宣言 | 組み込みの判定に反映する。否定命題の表明としては検証しない |

- 禁止の宣言ができるのは、定義に禁止を含む組み込み要件だけである。本書の時点では `ObjectPayload` だけが該当する。
- `Copy` は `Self is Copy` で有効にし、`ObjectPayload` は `Self is not ObjectPayload` で外す。既定の異なる組み込み要件を、同じ仕組みで扱う。
- 型自身と派生型の中では、`Self is ObjectPayload` は Refuted になる。

### 7.2. 書き方

```kimi
struct Parser
    Self is not ObjectPayload
    public var pos: i32 = 0

enum Token
    Self is not ObjectPayload
    Number(i32)
    End
```

- struct と enum の先頭の制約の領域にだけ書ける。汎用の struct・enum にも書ける。
- 条件（`when`）は付けられず、型のすべての束縛に無条件で効く。
- ほかの要件と組み合わせず、独立した節として書く。同じ宣言の中で繰り返すとエラーになる。
- `ObjectPayload` は `Kimi.ObjectPayload` として解決されなければならない。同名のユーザー宣言が見える場合は、修飾して書く。

### 7.3. 継承

- 禁止は派生型へ受け継がれる（受け継がれない open（§6.2.2）とは異なる）。派生型での書き直しは、冗長だが許す。肯定形で禁止を解くことはできない。
- 禁止していない基底型から、禁止型を派生させてよい。基底型のオブジェクトはそのまま作れる。派生型から基底型への暗黙の値変換はなく、`obj/Derived` も作れないので、派生型の値が基底型のオブジェクトに入る経路はない。
- 基底型がオブジェクトのレシーバー（`self: objref/Self` など）を持っていても、派生の宣言は診断しない。禁止型の値からは呼び出せないだけである。

### 7.4. 効果

禁止型はオブジェクト対象でない。そのため本書 §5 の規則によって、次がすべて拒否される。

- `obj/X` などのオブジェクト形式（型自身の中のレシーバーやフィールドを含む）
- upcast・checked cast・`is` テスト・`Weak<rc/X>`
- 作成（本書 §9.1）
- 制約環境で `Self is ObjectPayload` が Proven の Contract への適合（本書 §8）

汎用の定義はボディで一度だけ検証する（§8.10）ので、インスタンス化の時点でこの禁止を新たに検出することはない。汎用コードへの影響は、宣言された要件を通して呼び出し側で判定される。

### 7.5. 影響しないもの

禁止は浅い。次はすべて許される。

- 値（`owner`）、`ref`・`uniq` の借用、`unsafe` のポインター
- 禁止型を含むほかの型（フィールド、enum の payload、Tuple、固定長配列、`Array`、`Dictionary`、`Option`、`Result`）と、それらのオブジェクト化（`obj/Box<X>`、`rc/(X, i32)`）やヒープ配置
- 禁止型を捕捉した Closure と、共通 Function Type への変換
- static ストレージ

禁止は `Copy`・`Owned`・`Sealed` の判定を変えない。

## 8. Contract

Contract の中の `Self is ObjectPayload` と `Self is not ObjectPayload` は、§6.1.3.1 のとおり `Self` に依存する実装の要件である。struct・enum の禁止の宣言とは異なり、適合によって禁止の有無は変わらない。

要件のシグネチャで `Self` のオブジェクト形式を使う Contract では、その制約環境で `Self is ObjectPayload` が Proven でなければならない。直接の宣言か、親 Contract からの継承（§8.4.2）かは区別しない。

| 立場 | `Self is ObjectPayload` の役割 |
| --- | --- |
| Contract 自身 | 要件のシグネチャで `objref/Self` などを形成する前提 |
| 適合する型 | 満たすべき義務。禁止型は適合できない |
| 利用者 | `T is Shape` から、§8.7 の Contract 詳細化の規則で `T is ObjectPayload` が導かれる。重ねて書く必要はない |

```kimi
contract Shape
    Self is ObjectPayload
    func area(self: objref/Self) -> f64

contract ColoredShape: Shape              // Shape から継承するので、再宣言は要らない
    func color(self: objref/Self) -> i32

func total<T>(item: objref/T) -> f64
    T is Shape                            // ObjectPayload は Shape から導かれる
    return item.area()
```

- 否定形は、オブジェクト化できない型だけを適合させる Contract に使う。その要件で `Self` のオブジェクト形式を使うと、通常の形成規則で拒否される。
- 肯定形と否定形が同じ制約環境にそろうと、§8.7 の矛盾として Error になる。
- 禁止型の派生では、肯定形の Contract について基底型の適合を受け継ぐ経路が失敗する（§8.4.4）。基底型自身の適合は有効なままである。

## 9. 標準ライブラリ

### 9.1. 組み込み関数

| API | 追加する制約 |
| --- | --- |
| `Kimi.Intrinsics.makeObj<T>`、`makeRc<T>`、`makeArc<T>` | `T is ObjectPayload` |
| `Kimi.Intrinsics.makeRcCyclic<T, F>`、`makeArcCyclic<T, F>` | 既存の `T is Owned` に加えて `T is ObjectPayload` |

- §13.5.8 の「`T` is a valid concrete object payload Core」を `T is ObjectPayload` に置き換え、「Any valid complete owner Core other than Never may be the concrete payload」から禁止型を除く。
- `clone`・`downgrade`・`upgrade` は、有効なハンドル型を受け取るだけなので変えない。
- §22.1 の組み込み要件の表に `ObjectPayload` を加え、`Kimi.ObjectPayload` を必須の宣言にする。

### 9.2. 禁止する型

UTF-8 書式化プロファイル（utf8-formatting.md §1.1）の `WriteWindow {source}`・`Utf8Writer {target}`・`FixedBuffer {source}` に、`Self is not ObjectPayload` を加える。

- いずれも排他の Loan に縛られた一時的なアダプターであり、オブジェクト化しても寿命は Loan から逃れられない。
- 同プロファイルの「Window, view and adapter management performs no heap allocation」は、操作と実装への性能要件として従来どおり維持する。
- ほかの標準型には付けない。汎用コードの型引数として頻繁に使われるからである。

## 10. 将来の実行時 Contract View

実行時 Contract View（§8.5）は拡張設計である。導入するときは、次を満たさなければならない。

- 値から新しいオブジェクトや View を作る操作（暗黙のボクシングを含む）は、完全な data Type について `ObjectPayload` を要求する。
- 既存ハンドルの View の変更は、本書 §5.2 の既存ハンドルの操作として扱う（`ObjectPayload` は再証明せず、`Supports`・`Owned`・Loan の規則を検査する）。
- View の対象は、本書 §5.1 の 2 によりオブジェクト対象になる。

## 11. 互換性

禁止の有無は、公開宣言の要約（§18.3、§21.3.4）に含める。利用者は、パッケージの本体を見ずに判定できる。`Sealed` と同じく、肯定と否定の両方の結果が依存先に観測されうる（§8.10）。

| 変更 | 扱い |
| --- | --- |
| 禁止を加える | 互換でない |
| 禁止を取り除く | 公開される能力の変更。`T is not ObjectPayload` を使う依存先は壊れうるので、再検証する |
| 汎用 API に `T is ObjectPayload` を加える | 互換でない |
| 汎用 API から `T is ObjectPayload` を取り除く | 受け付ける型引数は広がる。ただし制約は overload の適用可能性に関わる（§10.1）ので、依存先の候補選択を再検証する。曖昧さや選択の変化が生じうる |

## 12. 診断

新しい診断は二つとする。診断コードの名前は実装時に決める。

| 診断 | 対象 |
| --- | --- |
| オブジェクト形成の拒否 | 禁止型のオブジェクト形式・作成・upcast・cast・`is` テスト・Weak。禁止を宣言した型（受け継いだ場合は基底型）を示す |
| 否定の `Self` 節と `Self is ObjectPayload` の誤用 | 下の一覧。Contract の中では、どちらの形も実装の要件として許される（本書 §8） |

誤用の対象:

- 否定形を、struct・enum の先頭の制約と Contract 以外に書く
- 否定形に、禁止を定義しない要件・条件・組み合わせを使う
- 否定形を同じ宣言の中で繰り返す
- 肯定形 `Self is ObjectPayload` を struct・enum の適合の宣言として書く。`Self is Copy` など、ほかの適合の宣言は対象外

次は既存の未証明要件の診断を使う。

- 汎用の定義で、オブジェクト対象の根拠がない。`T is ObjectPayload` の追加、または `s` の制限を提示する。
- Contract の `Self is ObjectPayload`・`Self is not ObjectPayload` を満たさない型を適合させた。
- Contract の要件のシグネチャで、`Self is ObjectPayload` が Proven でないまま `Self` のオブジェクト形式を使った。

## 13. 実装と性能

以下は実装上の方針である。

- **ライブラリ**
  - `Kimi/Library/Core.kimi` に `public contract ObjectPayload` を宣言し、`KimiLibraryCatalog` に組み込み要件として登録する。
  - `Kimi/Library/Intrinsics.kimi` の `makeObj` に制約を加える。`Kimi/Library/Formatting.kimi` の 3 型に禁止を加える。
- **禁止の情報**: fragment を統合した後の宣言ごとに、実効ビットと、禁止を最初に宣言した型への参照を持つ。
  - 基底型を束縛するときに派生型へ伝播させ、判定のたびに基底型の連鎖をたどらない。診断はこの参照から宣言元を示す。
  - `ObjectPayload` の直接の判定は、外側の Semantics・Core の種類・このビットの参照だけで済み、アロケーションを伴わない。
- **許容集合**: 証明環境（制約環境）ごとに、Semantics パラメーターの `SemanticsMask` を保持する。
  - 条件付きメンバーの前提などで環境ごとに使える前提が変わるので、マスクをパラメーターだけには結び付けない。
  - 要件式の Semantics 部分も束縛時にマスクへ変換する。判定はマスク同士の比較（定数時間）になる。
- **形成の検査**: Semantics を適用した型の束縛で行う。外側の Semantics がオブジェクト系でなければ、ビットを 1 回比べて抜ける。
  - 汎用のオブジェクト形成の根拠を検査する処理は、現行コードに見当たらない（`ProveSealed` には呼び出し元がない）。新規に実装する前提とし、着手時に現状を確かめる。
- **`semantics` 主語の削除**: `semantics is ...` の構文は SPEC に定義がなく、本書でも採用しない。
  - `Parser.ParseTypeConstraint` の `semantics` 主語の分岐、`SemanticsMaskKoto`、`InvalidSemanticsConstraint_Kd`、関連するパーサーのテストを削除する。削除後の `semantics` は通常の名前として扱われる。
  - Semantics パラメーターへの制約（`s is reference` など）と `SemanticsMask` は残す。
- **検証**
  - 許容集合: 積・和・補集合、空集合の Error、Weak と `during`（`objref`・`objuniq` を含む）、条件付きメンバーでの前提の追加
  - 直接の判定: `Box<T>` の Proven、`Parser<T>`（禁止型）と `ref/T` の Refuted、不正な型の Error
  - 具体型: 形成・作成・upcast・cast・`is` テスト・Weak の拒否（禁止を受け継いだ派生型を含む）。診断が禁止の宣言元を示すこと
  - 浅い判定: `obj/Box<X>`、`Array<X>`、Closure の捕捉を受理する
  - 汎用の定義: 根拠がない場合の定義エラー。`ObjectPayload`・組の根拠・Contract 詳細化による受理。元の `s/T` の受理。`s/U` で許容集合が混ざる場合（{`owner`, `obj`} と View の対象など）の受理と拒否
  - 既存ハンドルの操作: `ObjectPayload` を要さないこと。各操作の成立条件（`rc/T` から `objuniq/T` の拒否など）は維持されること
  - `Sealed`: 単独ではオブジェクト形成の根拠にならないこと。組の根拠と `Sealed` で payload 投影ができること。open な型を汎用コードでオブジェクト化できること
  - Contract: 三つの役割、親 Contract からの継承、禁止型の適合の拒否、否定形の Contract への適合、肯定形との矛盾の Error
  - 診断: 否定の `Self` 節と `Self is ObjectPayload` の誤用。Contract の中の両方の形と、ほかの適合の宣言は受理すること
  - 標準の 3 型の禁止

## 14. ほかの draft との関係

- `draft/Design/2026-09-19 Shared Ownership and Using Guards.md` は未統合である。
  - 同書は、型が不明な値から新しいオブジェクトを作る根拠として `T is Sealed` を用いている。仕様に取り込むときは、これを `T is ObjectPayload` に読み替える。
  - 有効なハンドルから同じ対象を使い回すときに再証明を要しないという同書の方針は、本書の組の根拠と既存ハンドルの操作（本書 §5）と整合する。
  - 同書のファイルは編集しない。
- `2026-09-17 Whole Value Replacement.md` は取り込み済みである。`Sealed` の値全体の置き換えの役割は、本書で変わらない。

## 15. SPEC.md への影響

正式仕様は本書に依存せず、次を更新する。

- **共通規則**
  - §8.7: 許容集合（本書 §3）と `ObjectPayload` の証明（本書 §4.2）
  - §3.3: カテゴリーの節から、許容集合を参照する
  - §8.1.1・§8.1.2・§15.3: 表のオブジェクト系の行をオブジェクト対象で書き直す。Sealed の段落と、`during` の safe borrow の判定を改める
  - §3.2.2: Weak の根拠をオブジェクト対象と許容集合で書き直す
  - §3.3.6: オブジェクト形式の形成の文
- **ObjectPayload と Sealed**
  - §8.4.7: `ObjectPayload` を加え、§8.4.7.2 を新設する（本書 §4〜§6）
  - §8.4.7.1: オブジェクト形成の根拠の記述を削除し、例を改める
  - §8.10: 「Sealed and complete payloads」の段落に `ObjectPayload` と禁止の再検証を加える
  - §13.5.5.1: 例（`borrowPayload`・`borrowPayloadMut`）
  - §13.5.8: 作成関数の制約と payload の適格性
  - §22（冒頭・§22.1 の表）、Appendix A（組み込み要件の検証）、Appendix E（用語）
- **オブジェクト化の禁止**
  - §6.2・§6.2.2・§6.3: 宣言の場所、継承、enum
  - §6.1.3.1・§8.2・§8.4.4: 型の宣言の制約節の三分類。禁止の宣言は否定命題の表明として検証しないこと。Contract の中の否定形は実装の要件であること
  - §13.5.7・§13.6.1・§13.6.2・§14.10: upcast、`is` テスト、checked cast、絞り込み
  - §18.3・§21.3.4: 公開要約と互換性
  - utf8-formatting.md §1.1: 3 型の禁止
- **将来の Contract View**: §8.5・§15.8.1 に本書 §10 の要求を記す
- **Appendix F**: 文法は変えない。否定の `Self` 節は既存の `ConstraintClause` で書ける。書ける場所の制限は本文で定める
- **統合時にあわせて直す既存の不整合**（本書の規則とは独立）
  - §8.10 の例 `func transfer<T>(value: T) -> T => value // Valid for both Copy and Move.` は、§8.9 と §15.1.5 に反する。裸の place は Move せず、Copy が未確定の裸の place は定義時に Copy の証明を要するからである。`2026-09-23 Explicit Transfer and Exclusive Borrow.md` の取り込みで古くなったと考えられる。例は `=> value@move` とし、コメントを「Copy・Non-Copy のどちらでも Move する」に改める。
  - 同じ節の例の直前の段落（Copy が未確定の取得は、Copy と Move の条件付き効果計画を持てるとする記述）も、同じ理由で古い可能性がある。条件付きの効果が残る取得があるかを確かめ、なければ段落を改める。

**完了条件**: 仕様の中で `Sealed` をオブジェクト形成の根拠として使っている記述と例、定義のない Semantics の判定を、すべて改める。候補は `is Sealed`・「object Types」「payload Core」「View Target evidence」「s must be」「safe borrow」で検索し、一つずつ意味を確かめる。

## 16. 決定

採用する。本書で不採用とした案は次のとおり。

- **記法**
  - **`semantics is ...` による任意の Semantics マスク**: 型の要件と Semantics のマスクという二つの概念が並ぶ。`ref`・`uniq` を禁止すると、暗黙の借用（レシーバー、`for`、パターン、getter、`Equatable` など）と衝突する。
  - **`Self is NonObject` などの別名**: 名前が二つになる。小文字の `nonobject` は Semantics カテゴリーと紛らわしい。
  - **宣言の修飾子（`inline struct` など）**: キーワードが増え、`open` との組み合わせ規則が要る。
- **検査の方式**
  - **インスタンス化の時点で検査する案**: 公開契約を満たした呼び出しが、呼び出し先のボディで後から失敗する。§8.10 に反し、将来のコード共有とも両立しない。
  - **シグネチャの型から前提を暗黙に導く案**: 規則が二つになり（シグネチャなら不要、ボディだけなら必要）、型を変えるだけで要件が黙って変わる。
  - **Semantics の包含を判定ごとに定める案**: Weak・`during`・組の根拠で同じ判定を繰り返す。許容集合で一つにまとめる。
- **根拠の範囲**
  - **禁止型は `Sealed` を満たさないとする案**: `Sealed` の意味が混ざり、値全体の置き換えなどで禁止型が不当に拒否される。
  - **組の根拠を認めない案**: 有効なハンドルを受け取る関数でも `ObjectPayload` が必要になり、View の対象を不当に拒否する。
  - **既存ハンドルの View の変更にも `ObjectPayload` を要求する案**: 新しい payload を作らない操作に作成能力を求め、作成と既存ハンドルの操作の区別（本書 §5.2）を崩す。
  - **Contract ごとに `Self is ObjectPayload` の直接の宣言を必須にする案**: 親 Contract からの継承（§8.4.2）と別の構文上の規則になり、重複した記述を強いる。
- **禁止の範囲**
  - **深い禁止（C# の `ref struct` 相当）**: 汎用コンテナ、Closure、static ストレージまで伝播が要る。目的には浅い禁止で足りる。
  - **`obj`・`rc`・`arc` を個別に禁止する案**: 規則が増える割に、用途が見当たらない。
  - **ほかの標準型（`Slice`・`Utf8Slice` など）の禁止**: 汎用コードの型引数として頻繁に使われる。型の消去は、ローカルな source を持つ Slice を §15.8.1 ですでに拒否している。
- **例外の追加**
  - **禁止型への `is` テストを受理し、絞り込みだけ行わない案**: 絞り込みの規則に例外が要る。
  - **否定の要件 `T is not ObjectPayload` や、Contract の中の否定形を禁止する案**: §8.7 の否定の一般規則に例外を作る。
