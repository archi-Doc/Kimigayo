# 仕様変更案：raw pointer と unsafe の境界

日付：2026-10-02

状態：最終版。方針と決定事項は確定済み（精査の記録は本書 9）。正式仕様への取り込みと実装は未実施。

本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、実装仕様の節は「impl §n」、本書の節は「本書 n」と表記する。例の「エラー」「未定義動作」行は意図した例である。

## 1. 現仕様の問題

1. **位置づけが述べられていない（原則 2）：** 当初の `unsafe/T` は、所有権・寿命の管理の外で循環参照をライブラリーの責任で扱うためのものだった。現行の SPEC §5 は「unsafe 文脈は義務を免除しない」とし、pointee の Origin も追跡する（SPEC §15.2.3）。規則はこの立場で一貫しているが、支配原則として述べた規定がない。
2. **一つの語が二つの概念を表す（原則 1、4）：** `unsafe` は型（`unsafe/T`）と、約束の場所（`unsafe func`、Unsafe Block）の両方を表す。保持や比較は safe なのに型名が unsafe を名乗り、`unsafe` を検索しても約束の場所だけを見つけられない。
3. **unsafe 文脈の基準が一つでない（原則 2）：** 未定義動作が起こり得る操作と、それ自体は何も壊さない変換の両方に unsafe 文脈を要求する。基準は表の列挙からしか分からない。
4. **境界が用途ごとの primitive に分散している（原則 1、Coding Guidelines）：** safe と raw の変換は SPEC §5.6 で保留され、代わりに 13 個の compiler-known な internal primitive が追加された。`lendKey`・`lendValue`・`splitValue` は `lend`・`split` と `keyAt`・`valueAt` の組み合わせにすぎず、`addressOfI64` は型ごとの primitive である。利用者のコードは有効なポインターを作れず、ライブラリーが unsafe の責任でデータ構造を実装するという当初の目的は、標準ライブラリーにしか果たせない。
5. **FFI で、型で表せる意味を散文に移している（原則 3）：** 外部関数は借用を受け取れないので、「呼び出しの間だけ排他的に借り、保持しない」引数を `unsafe/i64` に落とし、意味を `- safety:` に書いている。また、外部関数はすべて unsafe なので、呼び出し側に確かめることがなくても Unsafe Block が要る。
6. **スロットの性質を、標準型だけがメタデータで持つ（原則 1、2）：** remainder 4 型と Formatting 3 型（`FixedBuffer`、`WriteWindow`、`Utf8Writer`）は raw pointer の Field しか持たず、スロットの分散と Loan 要求を compiler-known なメタデータで与えられている（SPEC §22.1.2.5、utf8-formatting §1.1）。利用者の型の格納のないスロットは Phantom Origin になり、Loan 要求は `none` になる（SPEC §15.3.5）。また、`unsafe/T` の分散は仕様に定めがない（実装は不変）。
7. **raw Place からの取り出しが既定の取得と食い違う：** SPEC §5.2 の `let value = *pointer`（Non-Copy の Move）は、SPEC §3.5 の bare acquisition の規則（Non-Copy はエラー）にも、SPEC §15.1.5 の Take の一覧（raw Place がない）にも合わない。

## 2. 理想的な動作

判断基準：**unsafe は検証の委譲であり、規則の免除ではない。**

1. 所有権・Loan・Origin・初期化・一度だけの破棄・効果上限・別名の規則は、unsafe なコードにも適用される。
2. 約束の場所は、Unsafe Block、unsafe func の契約、`#LibraryImport` の宣言の三つである。unsafe 文脈が要るのは、未定義動作が起こり得る操作だけである。約束が正しい限り、safe なコードだけでは未定義動作は起こらない。
3. 型名は能力を表し、`unsafe` は約束の場所だけを表す。raw pointer は `raw/T` と書く。
4. safe と raw の変換は、既存の操作（Borrow、`@` の変換）の延長として書く。標準ライブラリーと利用者のライブラリーは同じ道具を使う。
5. 外部関数の宣言は、signature どおりに振る舞うという約束である。借用の意味は型で表し、`unsafe func` にするのは、呼び出し側に型で表せない義務があるときだけである。
6. Kimigayo で宣言した型のスロットの性質は、格納から決まる。格納のない依存は、サイズ 0 の `Kimi.Loan<T>` で表す。

## 3. 仕様の変更点

### 3.1. 位置づけ（SPEC §5 冒頭）

> An unsafe context delegates verification; it never exempts a program from a rule. Every ownership, Loan, Origin, initialization, destruction, effect-bound, aliasing and data-race rule applies to unsafe code. Unsafe Blocks, unsafe function contracts and foreign-function declarations mark where the programmer promises what the compiler cannot verify; breaking such a promise is undefined behavior.

### 3.2. 改名：`unsafe/T` から `raw/T` へ

| 対象 | 現行 | 変更後 |
| --- | --- | --- |
| 型 | `unsafe/T`、`unsafe/(ref/T during a)` | `raw/T`、`raw/(ref/T during a)` |
| 型付き null、変換 | `null@unsafe/i32`、`p@unsafe/u8` | `null@raw/i32`、`p@raw/u8` |
| Semantics の具体名 | `s is unsafe` | `s is raw` |
| 型の木と Semantics の表 | 「Unsafe: unsafe」「Unsafe／Pointer」 | 「Raw: raw」「Raw／Pointer／Raw pointer」 |

- **字句：** `raw` は、Semantics の位置と `@` の直後でだけ予約される（SPEC §2）。それ以外では通常の Name である（Field `raw` など）。`unsafe` は、`func` の前と、Unsafe Statement の Body の前でだけ認識する。
- **分類：** `reference` は `raw` を含み、`s is owning or borrow` は `raw` を除く（現行の `unsafe` と同じ）。
- **用語：** SPEC §5 のファイル名と見出しは変えず、用語を「raw pointer」に統一する。

### 3.3. unsafe 文脈の基準（SPEC §5 の表）

基準：**違反が未定義動作になり得る操作だけが unsafe 文脈を要求する。**

| 操作 | unsafe 文脈 |
| --- | --- |
| raw pointer の宣言・保持・Copy・Move・受け渡し・破棄、`null`、同じ型の等値比較と `null` との比較 | 不要 |
| `@raw/U` による取得と変換（同じ型、異なる raw pointer 型、`usize` との間、型付き null） | **不要（変更）** |
| `@raw` によるアドレスの取得（本書 3.5） | **不要（新規）** |
| `*p`、`p[n]` による raw Place の形成。raw Place の借用と取り出し（本書 3.6、3.7）を含む | 必要 |
| `p + n`、`p - n`、`p += n`、`p -= n` | 必要 |
| unsafe func の呼び出し | 必要 |

変換は provenance を作らない（SPEC §5.4、§5.5。変更なし）。効果上限の分類は、unsafe 文脈の要否と独立している。整数からポインターへの変換は、safe になっても環境への効果である（SPEC §8.4.10.2、`2026-10-02 Effect Bound Refinements.md` の 3.1）。

```kimi
let address = pointer@usize      // 変更：unsafe 文脈は不要
let bytes = pointer@raw/u8       // 変更：unsafe 文脈は不要
unsafe
    let value = bytes[13]        // 参照には unsafe 文脈が要る
```

### 3.4. raw アクセスの条件（SPEC §5.2）

raw pointer を通したアクセス（読み取り、書き込み、取り出し、raw Place の借用）は、次をすべて満たすときだけ有効である。コンパイラは検査せず、違反は未定義動作である。

1. **範囲：** アクセスする範囲が、ポインターの provenance に収まる。
   - `P@raw`：書かれた Place `P` の格納範囲（one-past を含む）。`x[0]@raw` から `x[1]` には届かない。
   - `Raw.allocate`：その確保の全体。
   - 整数からの変換と外部関数の結果：SPEC §5.5 と各関数の契約に従う（変更なし）。
2. **生存と値：** 格納が生存していること。読み取りには初期化済みの有効な値が要り、書き込みは SPEC §5.2 の初期化と置換の規則に従う。内容を置換すると、格納は残るが、古い内容の中を指すポインターは無効になる（SPEC §15.7.3）。
3. **権限：** アドレスを取った経路の権限を超えない。
   - 読み取りだけ：`let` の束縛、`ref` を通した Place、共有の経路。
   - 読み書き：書き込み可能な所有の経路、`Raw.allocate` の結果。
   - その Loan が生存している間だけ読み書き：`uniq` を通した Place。
4. **Loan：** その格納に対して生存している Loan と衝突しない。共有 Loan があれば書けず、排他 Loan があればアクセスできない。ただし、ポインターを取った経路の Loan の内側で行うアクセスは、衝突しない。

Loan の生存は、safe な値の使用だけで決まる（SPEC §15.6）。`@raw` も raw pointer の使用も、Loan を延長しない。格納が生存していることと、借用の Loan が生存していることは別である。

**格納の生存：**

- **局所変数：** スコープが終わるか、値が Move されるまでのうち、早いほうまで生存する。実装は、`@raw` の対象になった局所変数を、アドレスが観測されたものとして扱う（impl §21.5.5）。
- **オブジェクトのペイロード：** 公開から破棄の開始まで、アドレスは変わらない。handle を Move・借用・clone・downgrade・upcast しても動かない。ただし、アドレスが変わらなくても、条件 3 と 4 は別に満たす必要がある。たとえば、親への後方リンクを使うには、親への排他アクセスと衝突しないことが要る。

**依存の保持：** raw な格納へ移した値の Origin と Loan 要求は、消えない。その値が生存している間、依存を、格納を所有する値の型（型引数か `Loan<T>` の Field）に残すのは、書き手の義務である。

impl §21.5.5 の「Unsafe and FFI implementations receiving these borrows must satisfy the same promises」は、この規則の一つの場合として整理する。

### 3.5. アドレスの取得と変換（SPEC §13.5）

| 形 | 意味 |
| --- | --- |
| `P@raw` | Place `P` のアドレス。結果は `raw/V`（`V` は `P` の格納型）。 |
| `E@raw/U` | raw pointer の取得と変換（本書 3.3）。`E` は raw pointer、`usize`、`null` のいずれか。 |

- **意味の分離：** bare の `@raw` は、`@follow` と同じく型をとらない操作であり、Semantics の省略形（SPEC §13.5.1 の `E@Semantics`）ではない。bare の所有の省略形（`@owner` など）を個別に定めている現行の扱いと同じく、bare の形をここで定める。`@raw/U` は変換だけを表し、アドレスを取らない。たとえば `p: raw/T` に対して、`p@raw` は `p` の slot のアドレス（`raw/(raw/T)`）、`p@raw/u8` は `p` の値の変換である。結果の型を確かめたいときは、束縛に注釈を書く。
- **対象：** `@raw` は、書かれた slot そのものを対象にする。参照先は `r@follow@raw`、Sealed なオブジェクトのペイロードは `h@follow@raw`、raw Place の一部は `(*p).field@raw` と書く。
- **検査：** 直ちに終わる `@ref` と同じ検査（初期化、読み取りの能力、Loan との衝突）を行う。値を読まず、Loan を残さない。一時値は `@ref` と同じく実体化し、ポインターはその一時値の寿命の間だけ有効である。
- **効果：** 効果の要約では、`P@raw` を、Place `P` へのアクセス（直ちに終わる借用）として扱う。可変 static の Place に対する `@raw` は、環境への効果である（SPEC §8.4.10.2）。

```kimi
var number: i32 = 1
let p = number@raw            // raw/i32。Loan を作らない
let r = number@uniq
let q = r@follow@raw          // r の Loan の内側でだけ書ける
unsafe
    *q = 2                    // 有効：r の Loan の内側で行うアクセス
    *p = 3                    // 未定義動作：r の排他 Loan と衝突する
r@follow += 1                 // r を後で使うので、上の二つの時点で r の Loan は生存している
```

### 3.6. raw Place の借用（SPEC §5.2、§13.5.5.2、§15.6.2）

`@ref`、`@uniq`、`@objref`、`@objuniq` は、raw Place（`*p`、`p[n]`、それらの Field・要素）も借用できる。

- **自動では復元しない：** raw pointer から、元の所有者や既存の Loan は復元されない。
- **新しい anchor：** 結果の参照先を、新しい Loan の anchor とする。この Loan の mode は借用の mode である。region は、通常どおり使用と結果の適合で決まる。Origin は、期待される型・戻り値・格納先に合わせて決まり、上限がない（`static` も可）。Adaptation Target に Origin を書かない規則（SPEC §13.5.1）は変えない。safe な導出についての一意の anchor の制限（SPEC §15.2.3）は適用しない。
- **重なり：** この anchor は、そこから派生していない Place（他の raw Place や元の格納）との重なりを判定しない。そのような未知の重なりがないことは、unsafe 側の義務である（本書 3.4）。結果を通したアクセス、結果からの Reborrow、その子は、この anchor の下で通常どおり検査する。
- **呼び出し側の Loan：** 結果を signature の Origin に合わせて返すと、呼び出し側は SPEC §15.6.4 に従い、その Origin に束縛された Loan を、結果の region の間保持する。Origin が一致しても、Loan も排他性も作られない（SPEC §15.3.5）。`uniq` の排他性の根拠は、unsafe 側の約束だけである。
- **義務：** 結果と、その Origin を保つ派生値が使われる全期間で、本書 3.4 の条件が、借用のアクセスについて成り立たなければならない。`uniq` では、他の経路（他の raw pointer を含む）からのアクセスがあってはならない。

```kimi
func get(self: ref/Self, index: isize) -> ref/T
    require 0 <= index and index < self.length else => $abort("Index out of range")
    unsafe => return self.data[index]@ref   // Origin は戻り値の型（self）に合わせて決まる
```

### 3.7. raw Place からの取り出し（SPEC §5.2、§15.1.5）

raw Place とその Field・要素は Take を提供する。状態は追跡しない。

- `(*p)@move` は値を取り出し、格納を未初期化にする。その後の読み取りや、二重の破棄を防ぐのは、書き手の義務である（現行の SPEC §5.2 の義務と同じ）。
- `_ = (*p)@move` は、その場で破棄する。
- bare acquisition は、他の Place と同じ規則に従う。Copy な値は Copy し、Non-Copy な値ではエラーになる（SPEC §3.5）。

SPEC §5.2 の例 `let value = *pointer` は、`let value = (*pointer)@move` に改める。

### 3.8. 領域の分割と効果上限（SPEC §15.6.3、§8.4.10）

- **非重複の根拠：** SPEC §15.6.3 で非重複を証明する根拠は、「§15.6.2 の構造的規則、または raw Place の `uniq` の借用についての unsafe の約束（本書 3.6）」とする。標準の境界の verified operations への言及は削除する。現行の `split` も、呼び出し側の unsafe の約束に依っており、自分では何も検査していなかった。
- **分割の書き方：**
  - 親の Loan を `Loan<uniq/… during source>` の Field で保持する（本書 3.10）。
  - 子を raw Place の `uniq` の借用で作り、`during state.source` の signature で返す。
  - 子は親の Loan に依存する。Iterator や remainder を破棄しても、子の Loan は終わらない（Loan の生存は使用で決まる）。
  - 子を残りの領域から除き、二度と貸さないことは、unsafe 側の義務である。
- **効果の要約（SPEC §8.4.5、§15.6.4）：**
  - **Loan との比較：** raw アクセスと raw Place の借用は、本書 3.6 の anchor の規則で比較する。つまり、派生していない Place とは衝突しない。そのため、`preserves results` の検証（SPEC §8.4.10.3）は、現行の split と同じ結果になる。保持中の Loan と衝突しないことは、本書 3.4 の unsafe の約束である。
  - **環境への効果：** raw アクセスそのものは、環境への効果にならない。環境への効果は、ポインターを得た時点で分類する（SPEC §8.4.10.2、`2026-10-02 Effect Bound Refinements.md` の 3.1）。

### 3.9. raw storage の操作：`Kimi.Raw`（SPEC §5.6 を置き換える）

```kimi
public group Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

| 関数 | 契約 |
| --- | --- |
| `allocate` | safe。要素 `count` 個分の、初期化されていない格納を確保する。<br>・`count < 0`、`count * stride(T)` の overflow、確保の失敗では Abort する（SPEC §22.5.2）。16 を超える整列は、コンパイル時に未対応として報告する。<br>・確保する量が 0 バイトのときは確保しない。代わりに、`T` に整列した非 null のアドレスを返す。その provenance は長さ 0 の領域であり、有効なアクセスはサイズ 0 の pointee へのものだけである。このアドレスは、他の 0 バイトの結果と同じになることがある。 |
| `release` | `storage` は、null か、まだ解放していない `allocate` の結果の先頭アドレスである。raw pointer の型を変換したものでもよいが、内部のアドレスは許さない。<br>・中の要素は、すべて破棄済みか取り出し済みでなければならず、`release` は要素を破棄しない。<br>・null と 0 バイトの結果には何もしない。<br>・解放した後は、その確保に由来するポインターはすべて無効になる。 |
| `initialize` | `storage` は、生存し、整列し、書き込み可能で、初期化済みの値を保持していない格納を指す。`value` をそこへ移し、古い内容は破棄しない。 |
| `slice` | `storage` から `length` 個の要素を参照する、共有の `Slice` を作る。<br>・`length >= 0` は呼び出し側の義務である。<br>・`length == 0` のときは、`storage` は何でもよく（null も可）、アクセスしない。<br>・`length > 0` のときは、`storage` は非 null で `T` に整列しており、`length * stride(T)` は `isize` に収まり、provenance の中の初期化された有効な要素を指さなければならない。<br>・結果の Origin `s` は、結果だけに現れる全称 Origin であり（SPEC §15.3.1、§15.3.4）、呼び出し側の期待される型で決まる。<br>・結果は、本書 3.6 と同じく新しい anchor を持つ。その Origin の全期間、要素に書き込んではならない。 |

要素の取り出しと破棄は、本書 3.7 の操作で書く。Abort しない確保は、必要になったときに、命名の対（SPEC §4.7.1）に従って `tryAllocate` として加える。

効果の要約では、`allocate` を割当てとして扱う。`release`・`initialize`・`slice` は、引数のポインターを通じた raw アクセスとして扱う。いずれも環境への効果ではない（本書 3.8）。

### 3.10. 分散と `Kimi.Loan<T>`（SPEC §15.3.5、§22.1）

- **`raw/T` の分散：** `raw/T` は `T` について不変とする（現行の実装と同じ）。raw pointer は読み書きを区別しない。共変にすると、書き込みを通じて、短い寿命の値を長い寿命の場所へ入れられてしまう。
- **`Kimi.Loan<T>`：** compiler-managed なサイズ 0 の struct Core である。
  - 形成：正規化した `T` は、外側の Semantics が `ref`、`uniq`、`objref`、`objuniq` のいずれかである完全な借用型でなければならない（`Kimi.Weak<S>` と同じ種類の形成条件、SPEC §3.2.2）。
  - 解析：`Loan<T>` の Field は、`T` を格納しているものとして扱う。スロットの使用、Loan 要求、分散、Owned は、そこから推論する。Copy になるのは `T` が Copy のときだけである（SPEC §3.5.1 の表）。この Field が使うスロットは Phantom Origin ではない。
  - 能力：アドレスを持たず、何も読まず、アクセスの能力を与えない。
  - 作成と破棄：`Loan<T>.init(value: T)` は safe であり、借用の値を受け取って（`ref` は Copy、`uniq` は転送）、その依存を保つ。破棄で終わるのは、その値が持つ責任だけである。Loan の生存は、同じ Origin を持つ他の値（Copy や派生した参照）の使用で決まる（SPEC §15.6.1 の Liveness）。依存を加えるだけで、権限は作らない。
- **共変な view：** `T` について共変な view は、アドレスを `raw/()` に保ち、要素の依存を `Loan<ref/T during source>` で表す。アクセスの直前に `data@raw/T` で変換する。

```kimi
public struct Window<T> {source}                // T について共変
    let loan: Loan<ref/T during source>
    let data: raw/()
    let length: isize
```

- **標準型の移行：** 本書 1 の 6 の 7 型は、メタデータの代わりに `Loan` の Field を持つ。その値は、それぞれの compiler-known な作成関数（`borrowStorage`、`Text.fixed`、`Text.writer`、`reserve`）が作る。

| 型 | 加える Field |
| --- | --- |
| `RefRemainder<E>`／`UniqRemainder<E>` | `Loan<ref/E during source>`／`Loan<uniq/E during source>` |
| `DictionaryRefRemainder<K, V>`／`DictionaryUniqRemainder<K, V>` | `Loan<ref/(K, V) during source>`／`Loan<uniq/(K, V) during source>` |
| `FixedBuffer`、`WriteWindow` | `Loan<uniq/u8 during source>` |
| `Utf8Writer` | `Loan<uniq/u8 during target>` |

`Slice<T>` は格納の Field を持たない compiler-managed な表現なので、メタデータを残す（本書 6）。

### 3.11. 外部関数（SPEC §22.3、impl §21.1.6）

- **宣言の約束：** `#LibraryImport` の宣言は、signature と `- safety:` の条件を満たすすべての呼び出しで、外部の実装が同じ signature の Kimigayo 関数と同じように振る舞うことを約束する。具体的には、次のことを守る。
  - 引数が許す範囲だけにアクセスする（`ref` は読み取り、`uniq` は呼び出しの間の排他的なアクセス）。
  - 受け取った値の借用の Field を書き換えるときは、新しい値がその Field の型・Origin・権限を満たす。
  - 結果の Origin を超えて借用を保持しない。
  - 結果の型の有効な値を返す。
  - SPEC §22.3.1 の巻き戻しの制限を守る。

  約束が偽なら未定義動作であり、責任は宣言の書き手にある。
- **`unsafe func` の基準：** 呼び出し側に型で表せない義務があるときだけ `unsafe func` にし、その義務を `- safety:` に書く。`#LibraryImport` は、safe な宣言にも付けられる。直接の呼び出しだけを許す規則は変えない。
- **効果：** 外部関数の呼び出しの効果は、signature が許す引数へのアクセスと、環境効果（SPEC §8.4.10.2）からなる。
  - `2026-10-02 Effect Bound Refinements.md` では、本体のない外部関数の呼び出しを「環境への効果が分からない呼出し」に含めていた。本書は、宣言の約束に基づいて、これを環境への効果に移す。
  - したがって、`confined` な実装からは呼べない。`preserves results` の実装からは、引数へのアクセスが以前の結果の Loan と競合しない限り呼べる。
  - 外部関数に効果を宣言する手段は設けない（本書 6）。
- **signature の型（SPEC §22.3.2）：** 次の表の型を加える。`T` は C-exchangeable な格納である。

| Kimigayo の引数・結果 | C の値 | LLVM の型 |
| --- | --- | --- |
| `ref/T` | `const T *`（非 null） | ptr |
| `uniq/T` | `T *`（非 null） | ptr |
| `Option<ref/T>`、`Option<uniq/T>` | null を許すポインター | ptr |

  借用の注釈は、通常どおり signature の Origin を導入する（SPEC §15.3.4）。結果の Origin は SPEC §15.4 に従う。親の Container から継承した Origin パラメーターは、現行どおり拒否する。引数には参照先のアドレスを渡し、Scalar の `ref` を値で渡す最適化（impl §21.5.5）は適用しない。`Option` は、一語の nonnull 表現（impl §21.1.5）を使う。
- **C-exchangeable（impl §21.1.6）：** C-exchangeable な referent への `ref`・`uniq` と、その Option を、Field の中を含めて C-exchangeable とする。
- **Windows（SPEC §22.7.1）：** `queryPerformanceCounter(value: uniq/i64) -> i32` と `queryPerformanceFrequency(value: uniq/i64) -> i32` を safe な宣言にする。`Storage.addressOfI64` の段落は削除する。

### 3.12. 標準ライブラリーの境界 primitive（SPEC §22.1.2.5）

| primitive | 扱い |
| --- | --- |
| `lend`、`split`、`lendKey`、`lendValue`、`splitValue` | raw Place の借用（`keyAt`・`valueAt` と組み合わせる）で置き換え、削除する |
| `inlineBase` | `storage@follow@raw@raw/E` で置き換え、削除する。SPEC §22 に、`InlineStorage<A>` が `A` をオフセット 0 に置くことを明記する |
| `addressOfI64` | 削除する（本書 3.11） |
| `release`、`placeValue` | `Raw.release`、`Raw.initialize` に統合する |
| `borrowStorage`、`ownStorage`、`dictionaryStorage`、`keyAt`、`valueAt`、`placeEntry` | 残す。Array と Dictionary の表現は、コンパイラが管理する |

`splitFirst` などの本体は、引き続き Kimigayo で書く。範囲・非重複・初期化の保証は、本書 3.8 の unsafe の義務として、Kimi ライブラリーが負う。

### 3.13. 診断と Compiler Server

- **不要な Unsafe Block：** unsafe 文脈を要求する操作は、それを囲む最も内側の Unsafe Block の許可を使う。入れ子の Deferred Block の中の操作も同じである。どの操作にも許可を使われない Unsafe Block には、warning `UnnecessaryUnsafeBlock_Kd`（category `Language`）を出す。
  - 主範囲：`unsafe` キーワード
  - Reason：このブロックの許可を使う操作がない。
  - Advice：Body は独立したスコープなので（SPEC §14.3.1）、Body に宣言や `defer` がないときだけ、「`unsafe` を削除して、中の文を残す」ことを示す。宣言や `defer` があるときは、削除によって名前の範囲や、破棄・`defer` の時点が変わることを示し、削除は勧めない。
  - CSP の修復候補（SPEC §23.5.3）も、スコープ・破棄順・制御移動を保つことを証明できるときだけ出す。
  - 変換が safe になるので、変換だけを包んでいた既存のブロックにも、この warning が出る。
- **既存の診断：**
  - `UnsafeBlockRequired_Kd` の対象を、本書 3.3 の表に合わせて狭める。
  - LibraryImport の診断の Message と Reason を、本書 3.11 に合わせる。
  - `Loan<T>` の形成の違反と、C-exchangeable でない referent への借用は、既存の、不正な型引数の診断と、未対応の signature の診断の枠組みで報告する。
- **CSP（SPEC §23.5.3）：** CSP の要件に、次の一覧を加える。いずれも、満たすべき義務（SPEC §5 の条件と `- safety:`）を併せて示す。
  - Unsafe Block と、その許可を使う操作
  - unsafe func の呼び出し
  - `#LibraryImport` の宣言

### 3.14. 変更しないこと

- null、ポインター演算の形、provenance と往復の保証（SPEC §5.1、§5.3〜§5.5）。ただし、unsafe 文脈の要否は本書 3.3 に従う。
- unsafe func の本体は unsafe 文脈にならず、関数値にできないこと（SPEC §7.5）
- Unsafe Block の構文とスコープ（SPEC §14.3）
- 外部関数を直接にしか呼べないこと、aggregate の値渡しと callback を除外すること（SPEC §22.3）
- raw の pair layer を follow しないこと（SPEC 付録 D）
- `Kimi.Storage` が internal であること（SPEC §22.1.2.5）

## 4. 例：利用者のコンテナー

```kimi
public struct Buffer<T>
    let data: raw/T
    var length: isize
    let capacity: isize

    public init(capacity: isize)
        self.data = Raw.allocate<T>(capacity)
        self.length = 0
        self.capacity = capacity

    public func push(self: uniq/Self, value: T)
        require self.length < self.capacity else => $abort("Buffer is full")
        unsafe => Raw.initialize(self.data + self.length, value@move)
        self.length += 1

    public func get(self: ref/Self, index: isize) -> ref/T
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref        // Origin は self に合わせて決まる

    public func items(self: ref/Self) -> Slice<T>
        unsafe => return Raw.slice(self.data, self.length)

    public func view(self: ref/Self) -> BufferView<T>   // source は self に合わせて決まる
        return BufferView<T>.init(self)

    drop
        let data = self.data
        while self.length > 0
            self.length -= 1
            unsafe => _ = data[self.length]@move     // 逆順に破棄する
        unsafe => Raw.release(data)

public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>
    let data: raw/T
    let length: isize

    init(buffer: ref/Buffer<T> during source)
        self.data = buffer.data
        self.length = buffer.length
        self.loan = Loan.init(buffer)

    public func get(self: ref/Self, index: isize) -> ref/T during self.source
        require 0 <= index and index < self.length else => $abort("Index out of range")
        unsafe => return self.data[index]@ref
```

- **view と借用検査：** `BufferView` が生存している間は、`source` の共有 Loan が保たれるので、`Buffer` への `push` はエラーになる。これは通常の借用検査である。unsafe なのは、「`data` が `Buffer` の要素を指す」というライブラリーの不変条件だけである。
- **参照を含む `T`：** `T` が `ref/i32 during a` のように参照を含む場合も、`T` は `Buffer<T>` の型に現れる。そのため、挿入した値の Loan は、`Buffer` と、そこから取り出したり借用したりした値が生存している間保たれる（本書 3.4 の依存の保持）。
- **分散：** `raw/T` の Field を持つので、`Buffer<T>` は `T` について不変である。

```kimi
func readCounter() -> i64
    var value: i64 = 0
    require Windows.queryPerformanceCounter(value@uniq) != 0 else
        $abort("Performance counter query failed")
    return value
```

## 5. 評価

### 5.1. メリット

- **原則 1：**
  - 語が分かれる。`raw` は能力、`unsafe` は約束の場所を表す。
  - unsafe 文脈の基準が一つになる。
  - 操作ごとに綴りが一つになる。アドレスの取得は `@raw`、変換は `@raw/U`、raw から safe への変換は raw Place の借用である。
  - 用途ごとの primitive 9 個を、公開の汎用操作 4 個と `Loan<T>` に置き換える。
  - Kimigayo で宣言した型は、`Slice<T>` を除いて、スロットの性質が格納から決まる。
- **原則 2：**
  - 約束の場所が三つに限られる。
  - FFI の引数の意味と、スロットの依存・分散が、宣言から読める。
  - `raw/T` の分散が仕様に書かれる。
- **原則 3：** 「借用し、保持しない」が型で表され、散文の義務が減る。
- **原則 4：** 約束の場所と義務を一覧にでき、修復候補には前提条件が付く。
- **性能：**
  - raw Place の借用と `@raw` は、置き換える primitive と同じアドレス計算になる。
  - 既存の経路に加わる実行時の処理はない。新しい `Raw.release` だけが、0 バイトの結果を判定する。
  - `Loan<T>` はサイズ 0 である。

### 5.2. デメリットと費用

- **改名の量：** 機械的な変更だが、範囲が広い。テストの C# に 445 箇所、Kimi ライブラリーに 90 箇所、文書に 56 箇所ある。
- **safe な変換：** safe なコードで、整数から任意のポインターを作れるようになる。ただし、参照するには unsafe 文脈が要り、変換は provenance を作らない。
- **raw Place の借用：** unsafe なコードが、必要以上に長い Origin を選べる。また、その anchor は未知の重なりを検査しない。いずれも、unsafe の約束の範囲内のことである。Origin は、戻り値の型や注釈など、目に見える位置で決まる。
- **safe な外部関数：** 宣言の誤りは、呼び出し側からは見えない。信頼の位置は、`#LibraryImport` と CSP の一覧に現れる。
- **所有のコンテナーの分散：** raw で書いた所有のコンテナーは、`T` について不変になる。
- **新しい要素：** 語 `raw`、`Kimi.Raw` の 4 関数、`Loan<T>`、raw Place の借用の anchor の規則が加わる。

### 5.3. 複雑性

削る規則が、加える規則を上回る。

- **削る：**
  - 変換に unsafe 文脈を要求する規則
  - internal primitive 9 個と、その契約の段落
  - 7 型のメタデータ
  - 外部関数の引数から借用を除く規定と、外部関数をすべて unsafe にする規定
  - SPEC §5.6 の保留
  - SPEC §15.6.3 の、標準の境界についての特別扱い
- **加える：**
  - `@raw` の操作
  - raw Place の借用（既存の Borrow と結果の適合を使い、anchor の規則を一つ加える）
  - アクセスの条件の一覧（現行の義務を整理したもの）
  - `Kimi.Raw`、`Loan<T>`、`raw/T` の分散
  - FFI の型の 3 行
  - warning 一つ

### 5.4. 検討して採らなかった案

| 案 | 採らなかった理由 |
| --- | --- |
| `unsafe/T` の名前を残す | 一つの語が二つの概念を表したままになる。 |
| 変換に unsafe 文脈を求め続ける | unsafe 文脈の基準が二つになる。 |
| `@raw/V` を、オペランドの型によって借用か変換に分ける | 同じ綴りの意味が入力の型で変わり、間接参照の段数を読み違えやすい（原則 2）。 |
| safe から raw への変換を関数 `Raw.address(of:)` にする | Borrow と別の形が増える。また、値を受け取るので、経路の権限が読み取れない。 |
| raw から safe への変換で Origin を書く（`p@(ref/T during a)`） | SPEC §13.5.1 の、Adaptation Target に Origin を書かない規則の例外になる。 |
| raw から safe への変換を関数 `Raw.borrow(p)` にする | Field や要素を借用するたびに、関数を呼ぶことになる。 |
| raw 由来の参照に Loan を持たせない | Reborrow と衝突の検査に、anchor が要る。 |
| 領域の分割を宣言する専用の構文や primitive | signature、`Loan<T>`、unsafe の約束で足りる。専用の宣言を設けても、非重複は検証できない。 |
| ヘッダーで Loan 要求を宣言する（`{source: uniq}`） | 「スロットの性質は格納から決まる」という規則の例外になる。 |
| 読み取り専用と書き込み可能の、2 種類の raw pointer | FFI での区別は `ref`・`uniq` で表せる。共変な view は `raw/()` と `Loan<ref/T>` で表せる。 |
| `raw/T` を共変にする | 書き込みを通じた寿命の誤りを、型ごとに防がなければならなくなる。 |
| 外部関数を常に unsafe にする | 型で表せる義務を呼び出し側に残し、宣言の書き手の約束が表に出ない。 |
| raw Place への代入を初期化とする | 代入の意味が、Place の種類によって変わる。 |
| 局所変数の格納を、Move の後もスコープの終わりまで保証する | 実装の格納の再利用（impl §21.5.5）を制限する。また、Move の後の格納を使う正当な用途がない。 |

## 6. 範囲外

- Unsafe Function Types、unsafe func と外部関数の関数値
- 外部関数の効果の宣言（`confined` な外部関数）
- raw pointer の、並行実行での能力
- unowned 参照、unsafe な weak pointer（SPEC 付録 D）。安全な循環は `rc`・`arc` と `Weak` で書く。raw pointer による後方参照の safe な API には、ライブラリー独自の不変条件（本書 3.4）が要る。
- Array と Dictionary を、Kimigayo の struct として書き直すこと
- `Slice<T>` を、本書 3.10 の共変な view の形で書き直すこと
- 共変な所有のコンテナーを表す手段
- 排他の Slice、raw のバイト列から作る `string`
- aggregate の値渡しや callback を含む FFI の拡張
- `tryAllocate`

## 7. 実装計画

各単位では、再現例・実装・焦点テストを揃え、AGENTS.md の手順で Verify してからコミットする。診断の単位では DIAGNOSTICS §10 の手順に従い、各 commit で `docs/dev/CODEMAP.md` の該当行を更新する。セッションの最後には、Session の検証を行う。

**他の案との順序：** 効果上限の実装（`2026-10-01 Requirement Effect Bounds.md` の U3。`2026-10-02 Effect Bound Refinements.md` の分類と委譲を含む）を、本書の単位 3〜5 と単位 7 より先に完了する。これらの単位は、その検査を前提に、効果の要約を更新する。

1. **改名：**
   - `Constants.UnsafeKeyword` を、Semantics 用の `raw` と、修飾子・文用の `unsafe` に分ける。
   - `SemanticsKind`、`SemanticsMask`、`CompilerHelper.TryParse`／`ToText`、`Parser.Origins` を更新する。
   - 診断の文、`.kimi`、テスト、文書を機械的に更新する。この単位では意味を変えない。
2. **unsafe 文脈の基準：**
   - `ControlFlowAnalysis` の変換の許可検査をなくし、`BindingControlFlowTypes.RequiresUnsafeContext` を本書 3.3 の表に合わせる。
   - `UnnecessaryUnsafeBlock_Kd` を、Advice の条件（本書 3.13）を含めて追加する。
   - 未実装の Typed Null Formation を `Binding.Conversions.BindConversion` に実装する。失敗を期待している現行のテストの期待値も見直す（`StorageBoundaryTest`、`DictionaryStorageValidationTest`、`Utf8FormatBindingTest`）。
3. **`@raw`：**
   - bare の `raw` を、アドレスの取得として `Binding.Conversions` で扱う。`@raw/U` は、現行の変換の経路のままとする。
   - 所有権解析では、直ちに終わる共有の借用として検査する。
   - 効果の要約では、本書 3.5 のとおり Place へのアクセスとして扱う。
   - lowering では Place のアドレスを返し、その局所変数をアドレスが観測されたものとして扱う。
4. **raw Place の Take、借用、分割：**
   - `(*p)@move` と、新しい anchor を持つ raw Place の借用を実装する。対象は `Binding.Conversions`、`Binding.OriginInference`、`OwnershipAnalysis.Pointers` と、§15.6.2 の重なりの判定である。
   - 効果の要約（`Binding.EffectBounds`）で、raw アクセスを本書 3.8 のとおりに扱う。
   - `Storage.kimi` の `lend`・`split`・`lendKey`・`lendValue`・`splitValue`・`inlineBase` を置き換える。そのうえで、宣言、ID（`KimiDeclaration`、`KimiLibraryKinds`）、catalog、検証（`KimiLibraryValidation`、`KimiLibraryStorage`）、lowering（`BodyLowering.Arrays`、`FixedStorage`、`DictionaryStorage`）を削除する。
   - `preserves results` の検証、iteration の割り当てと再利用の回帰テスト、`tryGetPairUniq` を確認する。
5. **`Kimi.Raw`：**
   - 4 関数を追加し、内部の `release` と `placeValue` を統合する。
   - 効果の要約に、本書 3.9 の分類を加える。
   - 0 バイトの結果と、その解放を、impl §21.2.4 の代替アドレスで実装する。
   - `docs/LIBRARY.md` を更新する。
6. **分散と `Loan<T>`：**
   - `raw/T` が不変であることを確かめるテストを加える。
   - `Loan<T>` の形成、サイズ 0 の layout、格納としての扱い（`Binding.TypeOrigins`、`Binding.Capabilities`）、`init` を実装する。
   - 7 型に Field を加え、`Binding.OriginRequirements` の特別扱いを削除する。`Slice<T>` の特別扱いは残す。
7. **FFI：**
   - `Binding.Attributes` の `IsImportShape` から、unsafe の要求を外す。
   - `PhysicalCode` と `LlvmEmitter.CreateImportAbi` に、借用と Option を加える。
   - 外部関数の効果を、環境効果として要約する。
   - C-exchangeable の判定を更新する。
   - `Kimi.Windows` と Time を移行し、`KimiLibraryAddress` と `BodyLowering.Address` を削除する。
8. **測定：** iteration と Dictionary の hot path について、`src/Benchmark` の該当する測定を、変更の前後で比較する。

焦点テストは、次のクラスと native fixture を中心にする。

- テストクラス：`ForeignEmissionTest`、`GenericPointerEmissionTest`、`DependentPointerEmissionTest`、`UnsafeFunctionValueBindingTest`、`LibraryImportTargetBindingTest`、`TimeLibraryTest`、storage 系（`StorageBoundaryTest`、`DictionaryRemainderTest`、`FixedStorageTest` など）
- native fixture：`ForeignPointer*`、`DependentPointer*`、`StorageBoundary*`、`TimeLibrary*`

## 8. 文書更新計画

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §2、§3、§8、付録 F | 予約語、型の木、Semantics の表と分類、§3.3.6 の入れ子の例と「no conversion between pointers and safe references」の文、§3.5.1 の Copy の表（`raw/T`、`Loan<T>`）、Semantics 名の並びを更新する。 | 取り込み時 |
| SPEC §5 | 本書 3.1、3.3〜3.7、3.9 を反映する。§5.6 は `Kimi.Raw` に置き換える。 | 取り込み時 |
| SPEC §8.4.10、§15.6.2、§15.6.3 | raw アクセスと効果上限、raw の anchor の重なり、非重複の根拠（本書 3.6、3.8）を反映する。§8.4.10 については、`2026-10-02 Effect Bound Refinements.md` の取り込み後の本文に対して、次を行う。<br>・冒頭の「unsafe は免除しない」を、§5 冒頭（本書 3.1）への参照に置き換える。<br>・§8.4.10.2 の分類表の外部関数の行を、本書 3.11 に合わせる。<br>・同じ分類表に、`@raw`（本書 3.5）、`Kimi.Raw`（本書 3.9）、safe になった変換（本書 3.3）の扱いを加える。<br>・例の `unsafe/` を `raw/` にする。 | 取り込み時 |
| SPEC §13.5 | `@raw` の操作、`@raw/U` の変換、raw Place の借用を加え、「not specified in this revision (§5.6)」を削除する。 | 取り込み時 |
| SPEC §15.1.5、§15.3.5 | raw Place の Take、`raw/T` の分散、`Loan<T>`、メタデータを `Slice<T>` に限ることを反映する。 | 取り込み時 |
| SPEC §22.1.2.5、§22.3、§22.7.1、utf8-formatting §1.1 | 本書 3.10〜3.12 に合わせる。 | 取り込み時 |
| SPEC §23.5.3 | CSP の要件に、unsafe の一覧を加える。 | 取り込み時 |
| SPEC のその他（§4、§7.5、§12、§14.3.3、§16、付録 D） | `unsafe/` の表記と用語を同期する。 | 取り込み時 |
| impl §21.1.6、§21.5.3、§21.5.5、付録 A | C-exchangeable、raw pointer の操作、FFI の段落、アドレスが観測された局所変数、テストの要件を更新する。 | 取り込み時 |
| `draft/INTEGRATED.md` | 本書の各節と、取り込み先の節の対応を記録する。 | 取り込み時 |
| `docs/LIBRARY.md` | `Kimi.Raw`、`Kimi.Loan<T>`、`Kimi.Windows` を更新する。 | 実装時 |
| `docs/STYLE.md` | 中身を見せないアドレスは `raw/()`、バイト単位の位置の計算は `raw/u8` とする。`- safety:` を本書 3.11 の基準に合わせる。 | 実装時 |
| `docs/GUIDE.md`、`src/Kimi/Library/README.md` | raw pointer の記述と、境界 primitive の一覧を更新する。 | 実装時 |
| `docs/STATUS.md`、`docs/dev/CODEMAP.md` | 各単位で変わる対応範囲と入口を記録する。 | 実装時 |
| `docs/dev/PLAN.md` | 実装を予定に入れるときに加える。 | 計画時 |

## 9. 精査の記録

### 9.1. 初回の精査

| 初稿の記述 | 問題 | 修正 |
| --- | --- | --- |
| safe なコードだけでは未定義動作は起こらない | safe な外部関数の宣言が偽なら成り立たない。 | 「約束が正しい限り」と限定した（本書 2、3.1）。 |
| 局所変数の格納は、スコープの終わりまで生存する | impl §21.5.5 は、Move の後に格納を再利用する。 | スコープの終わりと Move の、早いほうまでとした（本書 3.4）。 |
| `Loan<T>` で、すべての標準型のメタデータを置き換えられる | `Slice<T>` の共変を、不変な `raw/T` では表せない。 | `raw/T` の不変を明記し、共変な view の形を加えた。`Slice<T>` のメタデータは残した（本書 3.10）。 |
| `let value = *pointer` による取り出し | 現行の SPEC §3.5、§15.1.5 と矛盾する。 | raw Place の Take を定めた（本書 3.7）。 |
| 「primitive 9 個がなくなる」 | `release` と `placeValue` は、公開の操作として残る。 | 「置き換える」と改めた（本書 5.1）。 |

決定事項 1〜5（`raw/T` の不変と共変な view、Formatting 3 型の移行、局所変数の有効期間、名前 `Loan<T>`、Typed Null Formation の実装）は、推奨のとおり採用した。

### 9.2. 外部からの指摘の検討

評価の軸は Kimigayo Principles、他の規定との整合、複雑性、性能である。

| 指摘 | 判断 | 反映 |
| --- | --- | --- |
| 1. 「raw から作った参照は Loan を持たない」は強すぎる | 採用する。Loan を持たないと、Reborrow や衝突の検査ができない。復元しないことと、新しい anchor を持つことを分ければ、規則を一つ加えるだけで済み、実行時の費用もない。 | 本書 3.6 |
| 2. `Loan<T>` だけでは、領域の分割が完成しない | 採用する。ただし、新しい仕組みは加えない。現行の `split` も非重複を自分では検査しておらず、呼び出し側の unsafe の約束に依っていた。そこで、非重複の根拠、効果の要約での扱い、Loan の生存を一般の規則として書き直した。`Loan<T>` の破棄の記述も直した。 | 本書 3.8、3.10 |
| 3. `@raw` が Loan を保持しないことと、例の寿命の判定が合わない | 採用する。例が誤っていた。Loan の生存は safe な値の使用だけで決まり、格納の生存とは別であることを明記した。後方リンクについての注記も加えた。 | 本書 3.4、3.5 |
| 4. `@raw/T` に、取得と変換の二役を持たせない | 採用する。意味がオペランドの型に依存しなくなり（原則 2）、規則も減る。代わりに、bare の `@raw` を Semantics の省略形から外す例外が一つできる。ただし、bare の所有の省略形と同じく、bare の形を個別に定める既存の扱いの範囲に収まる。 | 本書 3.5、5.4 |
| 5. 不要な Unsafe Block の Advice が、意味を変える | 採用する。Body は独立したスコープである。Advice と修復候補に、意味が保たれる条件を付けた。 | 本書 3.13 |
| 6. `Kimi.Raw` と FFI の境界の条件を補う | 採用する。0 バイトの provenance、解放できる条件、`slice` の長さと整列、内容の置換による無効化、外部による Field の書き換え、外部関数の効果、参照を含む `T` の依存の保持を定めた。外部関数の効果は、既存の環境効果に分類するだけで済んだ。`slice` の長さの検査は、実行時の費用を避けるため、Abort ではなく呼び出し側の義務とした。 | 本書 3.4、3.9、3.11、4 |

採らなかった部分はない。指摘 2 で求められた分割の宣言方法は、専用の仕組みを加えず、既存の signature・`Loan<T>`・unsafe の約束を組み合わせて満たした（本書 5.4）。

### 9.3. 実現性

| 項目 | 確認したこと |
| --- | --- |
| 改名 | Semantics の名前は、一つの表（`CompilerHelper.TryParse`）で解決される。`unsafe` は予約語ではなく、文脈で認識される。 |
| unsafe 文脈 | 検査は `ControlFlowAnalysis` に集まっている。変換を検査しているのは一箇所である。 |
| `@raw` | bare の名前は、`@follow` と同じ構文の位置で認識される（SPEC §13.5.1）。`@raw/U` は、現行の変換の経路をそのまま使う。 |
| raw Place の借用 | Loan は `(place, mode, region)` であり、`*place` の射影はすでにある（SPEC §15.6）。新しい anchor と、重なりを判定しない規則を加える。最も設計の負担が大きい単位である。 |
| `Loan<T>` | Loan 要求と分散は、`Binding.TypeOrigins` が格納から集めている。標準型は `Binding.OriginRequirements` で上書きされているが、この上書きは削除できる。 |
| FFI | 形と型の検査は `Binding.Attributes` にまとまっている。借用は、ABI 上ポインターである（impl §21.2.4）。 |

本案とは別に、Typed Null Formation が未実装であることを見つけた（本書 7 の 2 で実装する）。
