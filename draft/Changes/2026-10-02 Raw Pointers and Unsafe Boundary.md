# 仕様変更案：raw pointer と unsafe の境界

日付：2026-10-02

状態：草案。方針（本書 2）と本書 3 の主要な決定は採用済み。精査の結果（本書 10）を反映し、決定待ちの事項を本書 9 に挙げる。正式仕様への取り込みと実装は未実施。

本書で変更する事項は SPEC とその参照先より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、実装仕様の節は「impl §n」、本書の節は「本書 n」と表記する。例の「エラー」行は意図した拒否例である。

## 1. 現仕様の問題

### 1.1. unsafe の位置づけが定まっていない

当初の `unsafe/T` は、Kimigayo の所有権・寿命の管理の外に置き、オブジェクトツリーの循環参照をライブラリーの責任で扱うためのものだった。一方、現行の SPEC §5 は「unsafe 文脈は検証できない操作を許すが、義務を免除しない」としており、pointee の Origin も追跡する（SPEC §15.2.3）。正式仕様の規則はこの立場で一貫しているが、それを支配原則として述べた規定がなく、当初の意図との違いが文書から読み取れない（原則 2）。

### 1.2. 一つの語が二つの概念を表す

`unsafe` は、型（`unsafe/T`）と、許可と義務の場所（`unsafe func`、Unsafe Block）の両方を表す。ポインターを保持・Copy・比較することは safe なのに、型名が unsafe を名乗る。そのため `unsafe` を検索しても、検証を委ねた場所だけを見つけることができない（原則 1、原則 4）。

### 1.3. unsafe 文脈の基準が一つでない

SPEC §5 の表は、未定義動作が起こり得る操作（参照、位置の計算、unsafe func の呼び出し）と、それ自体は何も壊さない操作（ポインターの型変換、ポインターと整数の変換）の両方に unsafe 文脈を要求する。基準は表の列挙からしか分からない（原則 2）。

### 1.4. safe と raw の境界が個別の primitive に分散している

safe な値と raw pointer の変換は SPEC §5.6 で保留され、代わりに compiler-known な internal primitive が用途ごとに追加されてきた（`lend`、`split`、`lendKey`、`lendValue`、`splitValue`、`keyAt`、`valueAt`、`release`、`inlineBase`、`dictionaryStorage`、`placeEntry`、`placeValue`、`addressOfI64`）。

- `lendKey`・`lendValue`・`splitValue` は、`lend`・`split` と `keyAt`・`valueAt` の組み合わせにすぎない（原則 1）。
- `addressOfI64` は型ごとの primitive であり、FFI の型が増えるたびに同種の primitive が増える（原則 1）。
- 利用者のコードは、有効なポインターを作れない。得られるのは `null`、整数からの変換、外部関数の戻り値だけである。そのため、当初の目的だった「ライブラリーが unsafe の責任を負ってデータ構造を実装する」ことは、標準ライブラリーにしかできない（Coding Guidelines：コアライブラリーは Kimigayo で書く）。

### 1.5. FFI で、型で表せる意味を散文に移している

`QueryPerformanceCounter` は「呼び出しの間だけ排他的に借り、保持しない」引数をとる。これは `uniq/i64` で表せるが、外部関数の引数に借用を使えないため、`Storage.addressOfI64` で `unsafe/i64` に落とし、その意味を `- safety:` の散文に移している（原則 3）。また、すべての外部関数が `unsafe func` でなければならないため、呼び出し側に確認することが何もない関数でも、呼び出しに Unsafe Block が要る。

### 1.6. 格納のない依存と分散を、標準型だけが intrinsic なメタデータで持つ

Kimigayo で宣言された標準型のうち、次の型は raw pointer の Field しか持たず、スロットの分散と Loan 要求を compiler-known なメタデータで与えられている。

| 型 | メタデータ |
| --- | --- |
| `RefRemainder<E>`、`DictionaryRefRemainder<K, V>` | `source` は共変、Loan 要求は `ref`（SPEC §22.1.2.5） |
| `UniqRemainder<E>`、`DictionaryUniqRemainder<K, V>` | `source` は共変、Loan 要求は `uniq` |
| `FixedBuffer`、`WriteWindow`、`Utf8Writer` | スロットは共変、Loan 要求は `uniq`（SPEC utf8-formatting §1.1） |
| `Slice<T>` | `source` は共変、Loan 要求は `ref`、`T` は共変 |

利用者の型が `{source}` を宣言しても、格納のないスロットは Phantom Origin になり、Loan 要求は `none`、分散は不変に決まる（SPEC §15.3.5）。そのため、「この値は source の排他 Loan を保持する」ことも、「`T` について共変である」ことも書けない（原則 1：標準型だけの例外）。

また、SPEC は `unsafe/T` の `T` についての分散を定めていない。実装は不変として扱っている（原則 2：仕様の欠落）。

### 1.7. raw Place からの取り出しが既定の取得規則と食い違う

SPEC §5.2 は `let value = *pointer` を Non-Copy の pointee の Move の例とする。しかし SPEC §3.5 の既定の取得では、Non-Copy の Place の bare acquisition はエラーで、転送は `@move` と書く。`@move` は Take を提供する Movable Place を要求するが、SPEC §15.1.5 の Take の一覧に raw Place はない。

## 2. 理想的な動作

判断基準：**unsafe は検証の委譲であり、規則の免除ではない。**

1. 所有権・Loan・Origin・初期化・一度だけの破棄・効果上限・別名の規則は、unsafe なコードを含むすべてのプログラムに適用される。unsafe は、コンパイラが検証できない規則を書き手が満たすと約束する場所を示すだけである。
2. 約束の場所は、Unsafe Block、unsafe func の契約、`#LibraryImport` の宣言の三つに限る。unsafe 文脈が要るのは、未定義動作が起こり得る操作だけである。約束が正しい限り、safe なコードだけでは未定義動作は起こらない。
3. 型名は能力を表し、`unsafe` は約束の場所だけを表す。raw pointer は `raw/T` と書く。
4. safe と raw の変換は、既存の Borrow 操作の延長として書く。型ごと・用途ごとの primitive は作らない。標準ライブラリーと利用者のライブラリーは、同じ道具を使う。
5. 外部関数の宣言は、その signature どおりに振る舞うという約束である。借用の意味は型で表し、`unsafe func` にするのは、呼び出し側に型で表せない義務があるときだけである。
6. Kimigayo で宣言した型のスロットの性質（Loan 要求・分散）は、格納から決まる。格納を持たない依存は、サイズ 0 の格納 `Kimi.Loan<T>` で表す。

## 3. 仕様の変更点

### 3.1. 位置づけ（SPEC §5 冒頭）

SPEC §5 の冒頭に、本書 2 の判断基準を規範として置く。

> An unsafe context delegates verification; it never exempts a program from a rule. Every ownership, Loan, Origin, initialization, destruction, effect-bound, aliasing and data-race rule applies to unsafe code. Unsafe Blocks, unsafe function contracts and foreign-function declarations mark where the programmer promises what the compiler cannot verify; breaking such a promise is undefined behavior.

### 3.2. 改名：`unsafe/T` から `raw/T` へ

| 対象 | 現行 | 変更後 |
| --- | --- | --- |
| raw pointer の型 | `unsafe/T` | `raw/T` |
| 型付き null | `null@unsafe/i32` | `null@raw/i32` |
| Semantics の具体名 | `s is unsafe` | `s is raw` |
| Semantics の分類 | 木の「Unsafe: unsafe」、表の「Unsafe／Pointer」 | 「Raw: raw」、「Raw／Pointer／Raw pointer」 |
| 入れ子の型 | `unsafe/(ref/T during a)` | `raw/(ref/T during a)` |
| ポインターの変換 | `p@unsafe/u8` | `p@raw/u8` |

- **字句：** `raw` は、Semantics の位置と `@` の直後でだけ予約される文脈キーワードとする（SPEC §2 の表）。`unsafe` は、`func` の前と、Unsafe Statement を始める Body の前でだけ認識する。それ以外の `raw` は通常の Name である（Field `raw` など）。
- **分類：** `reference` は `raw` を含む。`s is owning or borrow` は `raw` 以外のすべてを許す（現行の `unsafe` と同じ扱い）。
- **ファイル名：** SPEC §5 のファイル名と見出しは変えない。用語は「raw pointer」に統一する。

### 3.3. unsafe 文脈の基準（SPEC §5 の表）

基準：**違反が未定義動作になり得る操作だけが unsafe 文脈を要求する。**

| 操作 | unsafe 文脈 |
| --- | --- |
| raw pointer の宣言・保持・Copy・Move・受け渡し・破棄 | 不要 |
| `null`、同じ型の等値比較、`null` との比較 | 不要 |
| raw pointer の `@` による取得と変換（同じ型、異なる raw pointer 型、`usize` との間） | **不要（変更）** |
| `@raw` によるアドレスの取得（本書 3.5） | **不要（新規）** |
| `*p`、`p[n]` による raw Place の形成 | 必要 |
| `p + n`、`p - n`、`p += n`、`p -= n` | 必要 |
| unsafe func の呼び出し | 必要 |

raw Place の借用・取り出し（本書 3.6、3.7）は `*p` か `p[n]` を含むので、unsafe 文脈の中にある。変換は provenance を作らないという現行の規則（SPEC §5.4、§5.5）は変えない。

```kimi
let address = pointer@usize      // 変更：unsafe 文脈は不要
let bytes = pointer@raw/u8       // 変更：unsafe 文脈は不要
unsafe
    let value = bytes[13]        // 参照は unsafe 文脈が必要
```

### 3.4. raw アクセスの条件（SPEC §5.2 に追加）

raw pointer を通したアクセス（読み取り、書き込み、取り出し、raw Place の借用）は、次をすべて満たすときだけ有効である。コンパイラはこれらを検査せず、違反は未定義動作である。

1. **範囲：** アクセスする範囲が、ポインターの provenance に収まる。
   - `P@raw` の provenance は、書かれた Place `P` の格納範囲である（one-past を含む）。`x[0]@raw` から `x[1]` には届かない。配列全体をたどるには `x@raw` を要素型に変換する。
   - `Raw.allocate` の結果の provenance は、その確保の全体である。
   - 整数から変換したポインターと、外部関数から得たポインターは、現行どおり SPEC §5.5 と各関数の契約に従う。
2. **生存と値：** 格納が生存している。読み取りには初期化済みの有効な値が要り、書き込みには SPEC §5.2 の初期化と置換の規則が適用される（現行どおり）。
3. **権限：** 権限は、アドレスを取った経路の権限を超えない。
   - `let` の束縛、`ref` を通した Place、共有の経路から取ったポインターでは、読み取りしかできない。
   - 書き込み可能な所有の経路から取ったポインターでは、読み書きができる。
   - `uniq` を通した Place から取ったポインターでは、その Loan が生存している間に限り、読み書きができる。
   - `Raw.allocate` の結果では、読み書きができる。
4. **Loan：** アクセスが、その格納に対して生存している Loan と衝突しない。共有 Loan があれば書き込めず、排他 Loan があればアクセスできない。ただし、ポインターをある Loan の経路から取った場合（`r@follow@raw`）、その Loan の内側のアクセスとして扱う。

格納の生存期間について、次を保証する。

- **局所変数：** `x@raw` で得たアドレスの格納は、`x` のスコープが終わるか、`x` の値が Move されるまでのうち、早いほうまで生存する。実装は、`@raw` を受けた局所変数をアドレスが観測されたものとして扱い、その間は SSA への昇格や格納の再利用をしない（impl §21.5.5）。
- **オブジェクトのペイロード：** ペイロードのアドレスは、公開から破棄の開始まで変わらない。handle の Move・借用・clone・downgrade・upcast は、ペイロードを動かさない（impl §21.2.3 の現行の表現と同じ）。

impl §21.5.5 の「Unsafe and FFI implementations receiving these borrows must satisfy the same promises」は、この規則の一つの場合として整理する。

### 3.5. アドレスの取得：`@raw`（SPEC §13.5.5.2）

Borrow の表に、次の行を加える。

| 入力 | 操作 | 結果 |
| --- | --- | --- |
| `V` を格納する読み取り可能な Place | `@raw`、`@raw/V` | 新しい `raw/V`。Loan を作らない |

- **対象：** `@ref` と同じく、書かれた slot そのものを対象にする。参照先は `r@follow@raw`、Sealed なオブジェクトのペイロードは `h@follow@raw`、raw Place の一部は `(*p).field@raw` や `p[n]@raw` と書く（後者は `*p`、`p[n]` を含むので unsafe 文脈の中にある）。
- **検査：** 直ちに終わる `@ref` と同じ検査（初期化、読み取りの能力、Loan との衝突）を行う。値を読まず、Copy せず、Loan も残さない。
- **一時値：** `@ref` と同じく実体化する。ポインターは、その一時値の寿命の間だけ有効である。
- **型付きの形：** `E@raw/V` の意味は、オペランドの型で決まる。`E` の型が raw pointer なら、本書 3.3 の取得または変換である。そうでなければ借用であり、`V` は `@ref/V` と同じく、格納されている型と一致しなければならない。raw pointer を保持する変数 `p` 自身のアドレスは、bare の `p@raw` で取る。

```kimi
var number: i32 = 1
let p = number@raw            // raw/i32。Loan を作らない
let r = number@uniq
let q = r@follow@raw          // r の参照先。r の Loan の間だけ書ける
unsafe
    *q = 2                    // 有効：r の Loan の内側のアクセス
    *p = 3                    // 未定義動作：r の排他 Loan と衝突する（本書 3.4 の 4）
```

### 3.6. raw Place の借用（SPEC §5.2、§13.5.5.2）

SPEC §13.5.5.2 の Borrow の行は、raw Place（`*p`、`p[n]`、それらの Field・要素）にも適用する。

- **Origin：** raw Place には Loan の出どころがないので、結果は Loan を持たず、Origin に Loan による上限はない。Origin は、期待される型・戻り値・格納先の要求に合わせて、通常の結果の適合（SPEC §13.5.3 の Origin Restriction）で決まる。Adaptation Target に Origin を書かない規則（SPEC §13.5.1）は変えない。SPEC §15.2.3 の一意の anchor の制限は safe な導出についての規則なので、raw Place の `uniq` の借用には適用しない。
- **義務：** 結果と、そこから得た値のうちその Origin を保つものが使われる全期間にわたって、本書 3.4 の条件が、その借用のアクセス（`ref` は読み取り、`uniq` は読み書き）について成り立たなければならない。`uniq` では、同じ格納への他のアクセス（他の raw pointer を含む）があってはならない。
- **その後：** 結果は通常の借用として検査される。Reborrow、Loan の衝突、寿命の規則がそのまま適用される。

```kimi
func get(self: ref/Self, index: isize) -> ref/T
    require 0 <= index and index < self.length else => $abort("Index out of range")
    unsafe => return self.data[index]@ref   // Origin は戻り値の型（self）に合わせて決まる
```

### 3.7. raw Place からの取り出し（SPEC §5.2、§15.1.5）

raw Place とその Field・要素は Take を提供する。ただし、その状態は追跡しない。

- `(*p)@move` は値を取り出し、格納を未初期化にする。他の経路や元の所有者による後の読み取りや二重の破棄を防ぐのは、書き手の義務である（現行の SPEC §5.2 の義務と同じ）。
- `_ = (*p)@move` は、その場で破棄する。
- Copy な pointee の bare acquisition は Copy である。Non-Copy の bare acquisition は、他の Place と同じくエラーになる（SPEC §3.5）。

SPEC §5.2 の例 `let value = *pointer` を `let value = (*pointer)@move` に改める。

### 3.8. raw storage の操作：`Kimi.Raw`（SPEC §5.6 を置き換える）

```kimi
public group Raw
    public func allocate<T>(count: isize) -> raw/T
    public unsafe func release<T>(storage: raw/T)
    public unsafe func initialize<T>(storage: raw/T, value: T)
    public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

| 関数 | 契約 |
| --- | --- |
| `allocate` | 要素 `count` 個分の、初期化されていない格納を確保する。<br>・`count < 0`、`count * stride(T)` の overflow、確保の失敗は Abort する（SPEC §22.5.2）。16 を超える整列は、コンパイル時に未対応として報告する。<br>・確保する量が 0 バイトのときは確保せず、`T` に整列した非 null のアドレスを返す。<br>・未定義動作は起こらないので safe である。 |
| `release` | `storage` は、null か、まだ解放していない `allocate` の結果（raw pointer の型を変換したものでもよい）である。<br>・中の要素は、すでに破棄または取り出し済みでなければならない。要素の破棄は行わない。<br>・0 バイトの結果と null には何もしない。解放後、その確保に由来するポインターはすべて無効になる。 |
| `initialize` | `storage` は、生存し、整列し、書き込み可能で、初期化済みの値を保持していない格納を指す。`value` をそこへ移し、古い内容は破棄しない。 |
| `slice` | `storage` から `length` 個の、初期化された有効な要素を参照する共有の `Slice` を作る（`length >= 0`、一つの確保の中）。<br>・結果の Origin `s` は、結果だけに現れる全称 Origin であり（SPEC §15.3.1 の単一スロットの束縛、§15.3.4）、呼び出し側の期待される型で決まる。<br>・その Origin の全期間にわたって、要素への書き込みがあってはならない。 |

要素の取り出しと破棄は、本書 3.7 の `(*p)@move` と `_ = (*p)@move` で書く。Abort しない確保は、必要になったときに命名の対（SPEC §4.7.1）に従って `tryAllocate` として加える。

### 3.9. 分散と格納のない依存（SPEC §15.3.5、§22.1）

#### 3.9.1. `raw/T` の分散

`raw/T` は `T` について不変とする（現行の実装と同じ）。raw pointer は読み書きの区別を持たないので、共変にすると、書き込みを通じて短い寿命の値を長い寿命の場所へ入れられてしまう。

#### 3.9.2. `Kimi.Loan<T>`

`Kimi.Loan<T>` は、compiler-managed なサイズ 0 の struct Core である。

- **形成：** 正規化した `T` は、外側の Semantics が `ref`、`uniq`、`objref`、`objuniq` のいずれかである完全な借用型でなければならない。`Kimi.Weak<S>` の形成条件（SPEC §3.2.2）と同じ種類の規則である。
- **解析：** `Loan<T>` の Field は、解析上 `T` を格納しているものとして扱う。スロットの使用、Loan 要求、分散、Owned はそこから推論する。Copy になるのは `T` が Copy のときだけである（SPEC §3.5.1 の表に行を加える）。`Loan<T>` の Field が使うスロットは、格納を持つので Phantom Origin ではない。
- **能力：** アドレスを持たず、何も読まず、アクセスの能力を与えない。
- **作成：** `Loan<T>.init(value: T)` は safe である。借用の値を受け取り（`ref` は Copy、`uniq` は転送）、その Loan と Origin を保つ。破棄すると、借用を破棄したときと同じく、その Loan が終わる。依存を加えるだけで権限を作らないので、未定義動作は起こらない。

```kimi
public struct BufferView<T> {source}
    let loan: Loan<ref/Buffer<T> during source>   // source は共変で、共有 Loan を要求する
    let data: raw/T
    let length: isize
```

#### 3.9.3. 共変な view の書き方

`T` について共変な view は、アドレスを `raw/()` に保ち、要素の依存を `Loan<ref/T during source>` で表す。アクセスの直前に `data@raw/T` で変換する。不変な `raw/T` の Field を持たないので、分散は `Loan` の Field だけから決まる。

```kimi
public struct Window<T> {source}                // T について共変
    let loan: Loan<ref/T during source>
    let data: raw/()
    let length: isize
```

#### 3.9.4. 標準型の移行

本書 1.6 の表のうち、Kimigayo で宣言された型は、intrinsic なメタデータの代わりに `Loan` の Field を持つ。値は、それぞれの compiler-known な作成関数（`borrowStorage`、`Text.fixed`、`Text.writer`、`reserve`）が作る。

| 型 | 加える Field |
| --- | --- |
| `RefRemainder<E>` | `Loan<ref/E during source>` |
| `UniqRemainder<E>` | `Loan<uniq/E during source>` |
| `DictionaryRefRemainder<K, V>` | `Loan<ref/(K, V) during source>` |
| `DictionaryUniqRemainder<K, V>` | `Loan<uniq/(K, V) during source>` |
| `FixedBuffer`、`WriteWindow` | `Loan<uniq/u8 during source>` |
| `Utf8Writer` | `Loan<uniq/u8 during target>` |

`Slice<T>` は格納の Field を持たない compiler-managed な表現なので、メタデータを残す（本書 6）。

### 3.10. 外部関数（SPEC §22.3、impl §21.1.6）

#### 3.10.1. 宣言の約束

`#LibraryImport` の宣言は、次を約束する。signature と `- safety:` の条件を満たすすべての呼び出しで、外部の実装は、同じ signature の Kimigayo 関数と同じように振る舞う。具体的には、次の三点を守る。

- 引数が許す範囲だけにアクセスする（`ref` は読み取り、`uniq` は呼び出しの間の排他的なアクセス）。
- 結果の Origin を超えて借用を保持しない。
- 結果の型の有効な値を返し、SPEC §22.3.1 の巻き戻しの制限を守る。

約束が偽なら未定義動作であり、その責任は宣言の書き手にある。

#### 3.10.2. `unsafe func` の基準

外部関数にも、Kimigayo の関数と同じ基準を適用する。呼び出し側に、型で表せない義務があるときだけ `unsafe func` にし、その義務を `- safety:` に書く。`#LibraryImport` は、`unsafe` のない宣言にも付けられる。直接の呼び出しだけを許す現行の規則は変えない。

#### 3.10.3. signature の型

SPEC §22.3.2 の表に、次の行を加える。`T` は C-exchangeable な格納である。

| Kimigayo の引数・結果 | C の値 | LLVM の型 |
| --- | --- | --- |
| `ref/T` | `const T *`（非 null） | ptr |
| `uniq/T` | `T *`（非 null） | ptr |
| `Option<ref/T>`、`Option<uniq/T>` | null を許すポインター | ptr |

- **Origin：** 借用の注釈は、通常どおり signature の Origin を導入する（SPEC §15.3.4）。借用を含む結果の Origin は、SPEC §15.4 に従う。親の Container から継承した Origin パラメーターは、現行どおり拒否する。
- **ABI：** 引数には、参照先のアドレスを渡す。impl §21.5.5 にある、Scalar の `ref` を値で渡す最適化は適用しない。`Option` は impl §21.1.5 の一語の nonnull 表現を使う。

#### 3.10.4. C-exchangeable の定義（impl §21.1.6）

C-exchangeable な型に、C-exchangeable な referent への `ref`・`uniq` と、その Option を加える。この定義は一つにまとめ、Field の中にも同じように適用する。外部のコードがそのような Field を書くときは、本書 3.10.1 により、その型の有効な値（生存し、Origin を守る非 null の借用）を書かなければならない。

#### 3.10.5. Windows の関数

```kimi
public group Windows
    /// Writes the current performance-counter value and returns nonzero on success.
    #LibraryImport("kernel32", "QueryPerformanceCounter")
    public func queryPerformanceCounter(value: uniq/i64) -> i32

    /// Writes the counter frequency in counts per second and returns nonzero on success.
    #LibraryImport("kernel32", "QueryPerformanceFrequency")
    public func queryPerformanceFrequency(value: uniq/i64) -> i32
```

SPEC §22.7.1 から `Storage.addressOfI64` の段落を削除する。

### 3.11. 標準ライブラリーの境界 primitive（SPEC §22.1.2.5）

| primitive | 扱い |
| --- | --- |
| `lend`、`split` | raw Place の借用（本書 3.6）で置き換え、削除する |
| `lendKey`、`lendValue`、`splitValue` | `keyAt`・`valueAt` と raw Place の借用で置き換え、削除する |
| `addressOfI64` | FFI の `uniq/i64`（本書 3.10）で不要になるので、削除する |
| `release` | 公開の `Raw.release` に統合する |
| `placeValue` | 公開の `Raw.initialize` に統合する |
| `inlineBase` | `storage@follow@raw@raw/E` で置き換え、削除する。SPEC §22 に、`InlineStorage<A>` が `A` をオフセット 0 に置くことを明記する |
| `borrowStorage`、`ownStorage`、`dictionaryStorage`、`keyAt`、`valueAt`、`placeEntry` | 残す。Array と Dictionary の表現は、コンパイラが管理する |

`splitFirst` などの本体は、引き続き Kimigayo で書く。範囲・非重複・初期化の保証は、Kimi ライブラリーの unsafe なコードが、本書 3.4 の義務として負う。現行の `lend`・`split` も検査を行わない能力の付与だけだったので、置き換えによって失われる検査はない。

### 3.12. 診断と Compiler Server

- **不要な Unsafe Block：** unsafe 文脈を要求する各操作は、それを囲む最も内側の Unsafe Block の許可を使う（入れ子の Deferred Block の中の操作を含む）。許可がどの操作にも使われない Unsafe Block には、warning `UnnecessaryUnsafeBlock_Kd`（category `Language`）を出す。
  - 主範囲：`unsafe` キーワード
  - Reason：Body の中に、このブロックの許可を使う操作がない
  - Advice：`unsafe` を削除し、中の文を残す
  - 本書 3.3 で変換が safe になると、変換だけを包んでいた既存のブロックにこの warning が出る。
- **既存の診断：**
  - `UnsafeBlockRequired_Kd` の対象は、本書 3.3 の表に合わせて狭まる。
  - LibraryImport の形と型の診断は、Message と Reason を本書 3.10 に合わせる。
  - `Loan<T>` の形成違反と、C-exchangeable でない referent への借用は、既存の不正な型引数と未対応の signature の診断の枠組みで報告する。
- **Compiler Server（SPEC §23.5.3）：** CSP の要件に、次の一覧を返すことを加える。それぞれについて、満たすべき義務（SPEC §5 の該当条件と `- safety:`）も示す。
  - Unsafe Block と、その許可を使う操作
  - unsafe func の呼び出し
  - `#LibraryImport` の宣言

### 3.13. 変更しないこと

- null の意味、ポインター演算の形、provenance と往復の保証（SPEC §5.1、§5.3〜§5.5。ただし unsafe 文脈の要求は本書 3.3 に従う）
- unsafe func の本体が unsafe 文脈にならないこと、unsafe func を関数値にできないこと（SPEC §7.5）
- Unsafe Block の構文と字句的な範囲（SPEC §14.3.3）
- 外部関数の直接呼び出しだけの規則、aggregate の値渡しの除外、callback の除外（SPEC §22.3）
- raw の pair layer を follow しないこと（SPEC 付録 D）
- `Kimi.Storage` が internal であり、利用者の格納が標準の境界に登録できないこと（SPEC §22.1.2.5）

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

`BufferView` が生存している間は、`source` の共有 Loan が保持されるので、`Buffer` への `push` はエラーになる。この検査は通常の借用検査である。unsafe なのは、`data` が `Buffer` の要素を指すという、ライブラリーの不変条件だけである。

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
  - 型名（`raw`）と約束の場所（`unsafe`）の語が分かれる。
  - unsafe 文脈の基準が一つになる。
  - safe から raw への変換は `@raw`、raw から safe への変換は raw Place の借用という、それぞれ一つの形になる。どちらも既存の Borrow の表の延長である。
  - 用途ごとの internal primitive 9 個を、公開の汎用操作 4 個（`Kimi.Raw`）と `Loan<T>` に置き換える。標準ライブラリーと利用者のライブラリーが同じ道具を使う。
  - Kimigayo で宣言した型のスロットの性質が、例外なく格納から決まる（`Slice<T>` を除く）。
- **原則 2：**
  - 約束の場所は三つに限られ、約束が正しい限り、safe なコードだけでは未定義動作が起こらない。
  - FFI の引数の意味は signature から読める。
  - スロットの依存と分散は格納から読める。`raw/T` の分散も仕様に明記される。
- **原則 3：** 「借用し、保持しない」が型で表され、散文の義務が減る。
- **原則 4：** 約束の場所と義務を一覧にでき、監査と修復の対象を絞れる。
- **Coding Guidelines：** compiler-known な primitive と intrinsic なメタデータが減り、コアライブラリーをより多く Kimigayo で書ける。

### 5.2. デメリットと費用

- **改名：** 機械的だが範囲が広い。テストの C# に 445 箇所（42 ファイル）、Kimi ライブラリーに 90 箇所、文書に 56 箇所ある。
- **safe な変換：** safe なコードで、整数から任意のポインターを作れる。ただし、参照には unsafe 文脈が要り、provenance は作られない（SPEC §5.5）。ポインターの出どころは unsafe 文脈に現れなくなるが、義務を確かめる場所（参照）には必ず現れる。
- **raw Place の借用の Origin：** unsafe なコードが、必要以上に長い Origin（`static` など）を選べる。これは raw pointer の本質的な性質である。Origin は、戻り値の型や注釈という目に見える位置で決まる。
- **safe な外部関数：** 宣言の誤りは呼び出し側から見えない。ただし、`#LibraryImport` が信頼の位置を明示し、CSP の一覧にも現れる。
- **所有のコンテナーの分散：** `raw/T` が不変なので、raw で書いた所有のコンテナー（本書 4 の `Buffer<T>`）は `T` について不変になる。共変な所有のコンテナーは、現時点では書けない。共変な view は、本書 3.9.3 の形で書ける。
- **新しい標準の型と関数：** `Loan<T>`、`Kimi.Raw` の 4 関数、`raw` キーワードが増える。

### 5.3. 複雑性

削る規則が、加える規則を上回る。

- **削る：**
  - 変換に unsafe 文脈を要求する 2 行
  - internal primitive 9 個と、それぞれの契約の段落（SPEC §22.1.2.5、§22.7.1）
  - 7 型の intrinsic なメタデータ（本書 3.9.4）
  - 外部関数の引数から借用を除く規定と、外部関数をすべて unsafe にする規定
  - SPEC §5.6 の保留
- **加える：**
  - `@raw` の行（既存の Borrow の規則を使う）
  - raw Place の借用の段落（既存の結果の適合を使う）
  - アクセスの条件の一覧（現行の義務を整理したもの）
  - `Kimi.Raw` の 4 関数、`Loan<T>`、`raw/T` の分散の 1 文
  - FFI の型の 3 行
  - warning 一つ

### 5.4. 検討して採らなかった案

| 案 | 採らなかった理由 |
| --- | --- |
| `unsafe/T` の名前を残す | 一つの語が二つの概念を表したままになる。 |
| 変換に unsafe 文脈を求め続ける | unsafe 文脈の基準が二つになる。 |
| safe から raw への変換を関数 `Raw.address(of:)` にする | Borrow と別の形が増える。また、値を受け取るので、アドレスを取った経路とその権限が読み取れない。 |
| raw から safe への変換で Origin を書く（`p@(ref/T during a)`） | Adaptation Target に Origin を書かない規則（SPEC §13.5.1）の例外になる。 |
| raw から safe への変換を関数 `Raw.borrow(p)` にする | Field や要素を借用するたびに、位置の計算と関数呼び出しが要る。Borrow の行を使えば、Place の形をそのまま書ける。 |
| `@raw/V` の重なりを、借用の優先で解く | 「Borrow targets take precedence」（SPEC §13.5.3）は Identity Acquisition についての規定であり、変換との優先順位は新しい規則になる。オペランドの型で決めれば、規則を足さずに済む。 |
| ヘッダーで Loan 要求を宣言する（`{source: uniq}`） | 「スロットの性質は格納から決まる」という規則（Declared Origin Slots 案を含む）の例外になる。 |
| 読み取り専用と書き込み可能の 2 種類の raw pointer | FFI での区別は `ref`・`uniq` で表せる。共変な view は `raw/()` と `Loan<ref/T>` で表せる。raw pointer はもともと能力を持たない。 |
| `raw/T` を共変にする | 書き込みを通じた寿命の誤りを、unsafe なコードの書き手が型ごとに防がなければならなくなる。共変が要る場合は、本書 3.9.3 の形で明示する。 |
| 外部関数を常に unsafe にする | 型で表せる義務を呼び出し側に残し、宣言の書き手の約束が表に出ない。 |
| raw Place への代入を初期化にする | 代入の意味が、Place の種類によって変わってしまう。 |
| 局所変数の格納を、Move の後もスコープの終わりまで保証する | 実装の格納の再利用（impl §21.5.5）を制限する。Move 後の格納を使う正当な用途もない。 |

## 6. 範囲外

- Unsafe Function Types、unsafe func と外部関数の関数値
- raw pointer の並行実行での能力
- unowned 参照、unsafe な weak pointer（SPEC 付録 D）。安全な循環は `rc`・`arc` と `Weak` で書く。raw pointer による後方参照は本案で書けるようになるが、safe な API に包むには、親への排他アクセスと衝突しないことを保証する、ライブラリー独自の不変条件が要る。
- Array と Dictionary を Kimigayo の struct として書き直すこと（`borrowStorage`、`dictionaryStorage`、`keyAt`、`valueAt` を削除できる）
- `Slice<T>` を、本書 3.9.3 の形の格納を持つ struct として書き直し、メタデータをなくすこと
- 共変な所有のコンテナーを表す手段
- 排他の Slice、raw のバイト列から作る `string`
- aggregate の値渡しと callback を含む FFI の拡張
- `tryAllocate`

## 7. 実装計画

各単位では、再現例・実装・焦点テストを揃え、AGENTS.md の手順で Verify してからコミットする。診断を追加・変更する単位では、DIAGNOSTICS §10 の手順に従う。各単位の commit で、`docs/dev/CODEMAP.md` の該当行を更新する。セッションの最後には Session の検証を行う。

1. **改名：**
   - `Constants.UnsafeKeyword` を、Semantics 用の `raw` と、修飾子・文用の `unsafe` に分ける。`SemanticsKind`・`SemanticsMask` の名前、`CompilerHelper.TryParse`・`ToText` の表、`Parser.Origins` の診断を更新する。
   - 診断の文（`DiagnosticCode.tinyhand`）、Kimi ライブラリーの `.kimi`、テスト、文書を機械的に更新する。この単位では意味を変えない。
2. **unsafe 文脈の基準：**
   - `ControlFlowAnalysis` の変換の許可検査をなくし、`BindingControlFlowTypes.RequiresUnsafeContext` を本書 3.3 の表に合わせる。
   - `UnnecessaryUnsafeBlock_Kd` を追加する。各操作が使う最も内側のブロックを記録し、使われないブロックを報告する。変換だけを包んでいたライブラリーのブロックを整理する。
   - 現行で未実装の Typed Null Formation（`null@raw/T`）を、`Binding.Conversions.BindConversion` で実装する。現行のテスト（`StorageBoundaryTest`、`DictionaryStorageValidationTest`、`Utf8FormatBindingTest`）は失敗を期待しているので、期待値を見直す。
3. **`@raw`：**
   - `Binding.Conversions` の bare の形と型付きの形に、`raw` を加える。`E@raw/V` は、オペランドの型で変換か借用かを決める。
   - 所有権解析では、直ちに終わる共有の借用として検査する（`ConversionBinding.Borrow` の各利用箇所）。
   - lowering では Place のアドレスを返し、その局所変数を、アドレスが観測されたものとして扱う。
4. **raw Place の Take と借用：**
   - `(*p)@move` と、Loan による上限のない Origin を持つ raw Place の借用を、`Binding.Conversions`、`Binding.OriginInference`、`OwnershipAnalysis.Pointers` に実装する。
   - `Storage.kimi` の `lend`・`split`・`lendKey`・`lendValue`・`splitValue`・`inlineBase` を置き換え、primitive の宣言、ID（`KimiDeclaration`、`KimiLibraryKinds`）、catalog、検証（`KimiLibraryValidation`、`KimiLibraryStorage`）、効果（`Binding.EffectBounds`）、lowering（`BodyLowering.Arrays`、`BodyLowering.FixedStorage`、`BodyLowering.DictionaryStorage`）を削除する。
   - iteration の割り当てと再利用の回帰テストを含める。
5. **`Kimi.Raw`：** `allocate`・`release`・`initialize`・`slice` を追加し、内部の `release`・`placeValue` を統合する。lowering は、既存の確保と解放の経路（`WindowsLowering.StorageRelease` など）を使う。`docs/LIBRARY.md` を更新する。
6. **分散と `Loan<T>`：**
   - `raw/T` の不変を仕様どおりに確認するテストを加える。
   - `Loan<T>` の形成、サイズ 0 の layout、解析上の格納としての扱い（`Binding.TypeOrigins`、`Binding.Capabilities`）、`init` を実装する。
   - 本書 3.9.4 の 7 型に `Loan` の Field を加え、`Binding.OriginRequirements` の該当する特別扱いを削除する（`Slice<T>` の特別扱いは残す）。
7. **FFI：**
   - `Binding.Attributes` の `IsImportShape` から `unsafe` の要求を外し、`PhysicalCode` に借用と Option を加える。`LlvmEmitter.CreateImportAbi` を合わせる。C-exchangeable の判定に、借用と Option を加える。
   - `Kimi.Windows` と Time を移行し、`addressOfI64`（`KimiLibraryAddress`、`BodyLowering.Address`）を削除する。
8. **測定：** iteration と Dictionary の hot path について、`src/Benchmark` の該当する測定を変更前後で比較する。

焦点テストは、`ForeignEmissionTest`、`GenericPointerEmissionTest`、`DependentPointerEmissionTest`、`UnsafeFunctionValueBindingTest`、`LibraryImportTargetBindingTest`、`TimeLibraryTest`、storage 系（`StorageBoundaryTest`、`DictionaryRemainderTest`、`FixedStorageTest` など）と、native fixture の `ForeignPointer*`、`DependentPointer*`、`StorageBoundary*`、`TimeLibrary*` を中心にする。

## 8. 文書更新計画

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §2 | 予約語の表で、Semantics の `unsafe` を `raw` に置き換え、`unsafe` は `func` と Body の前だけとする。 | 取り込み時 |
| SPEC §3 | 型の木、Semantics の表、分類の表、§3.3.6 の入れ子の例と表、「There is no safe `*` operator and no conversion between pointers and safe references」、§3.5.1 の Copy の表（`raw/T` と `Loan<T>`）を更新する。 | 取り込み時 |
| SPEC §5 | 冒頭の位置づけ（本書 3.1）、unsafe 文脈の表（本書 3.3）、アクセスの条件と格納の生存（本書 3.4）、raw Place の借用と取り出し（本書 3.6、3.7）、§5.6 の置き換え（本書 3.8）を反映する。 | 取り込み時 |
| SPEC §7.5、§14.3.3 | 例の `unsafe/i32` を `raw/i32` にする。 | 取り込み時 |
| SPEC §8 | Semantics の表の `unsafe` の行を `raw` にする。 | 取り込み時 |
| SPEC §13.5.3、§13.5.5.2 | 変換の表を更新し、「Conversions between raw pointers and safe references … are not specified」を削除する。`@raw` と raw Place の借用の行を加える。 | 取り込み時 |
| SPEC §15.1.5、§15.3.5 | raw Place の Take、`raw/T` の分散、`Loan<T>` の Field の扱いを加え、intrinsic なメタデータの記述を `Slice<T>` だけにする。 | 取り込み時 |
| SPEC §22.1.2.5、§22.3、§22.7.1 | 本書 3.9.4、3.10、3.11 に合わせる。 | 取り込み時 |
| SPEC utf8-formatting §1.1 | intrinsic な型の記述を、`Loan` の Field による記述に改める。 | 取り込み時 |
| SPEC §23.5.3 | CSP の要件に、unsafe の一覧を加える。 | 取り込み時 |
| SPEC の他の章（§4、§12、§16）と付録 D、F | `unsafe/` の表記と、raw pointer の用語を同期する。付録 F の Semantics 名の並びに `raw` を入れる。 | 取り込み時 |
| impl §21.1.6、§21.5.3、§21.5.5、付録 A | C-exchangeable の定義、raw pointer の操作の表記、借用を受け取る FFI の段落、アドレスを観測された局所変数、テストの要件を更新する。 | 取り込み時 |
| `draft/INTEGRATED.md` | 本書の各節と、取り込み先の節の対応を記録する。 | 取り込み時 |
| `docs/LIBRARY.md` | `Kimi.Raw`、`Kimi.Loan<T>`、`Kimi.Windows` の signature を更新する。 | 実装時 |
| `docs/STYLE.md` | 中身を見せないアドレスは `raw/()`、バイト単位の位置の計算は `raw/u8` とする。`- safety:` の説明を、外部関数の基準（本書 3.10.2）に合わせる。 | 実装時 |
| `docs/GUIDE.md`、`src/Kimi/Library/README.md` | raw pointer の記述と、境界 primitive の一覧を更新する。 | 実装時 |
| `docs/STATUS.md` | 各単位で変わる対応範囲を記録する。 | 実装時 |
| `docs/dev/CODEMAP.md` | `@raw`、`Kimi.Raw`、`Loan<T>`、FFI の入口を加える。あわせて、現行の誤った参照を直す（raw pointer の行の `Binding.Pointers` は zero stride の判定だけで、unsafe の検査は `ControlFlowAnalysis`・`BindingControlFlowTypes` にある。外部関数の行の `Binding.Imports` は module の import で、LibraryImport の検証は `Binding.Attributes` にある）。 | 実装時 |
| `docs/dev/PLAN.md` | 実装を予定に入れるときに加える。 | 計画時 |

## 9. 決定待ちの事項

推奨案で本書を書いている。異なる判断をする場合は、該当する節を改める。

| # | 事項 | 推奨 | 関係する節 |
| --- | --- | --- | --- |
| 1 | `raw/T` を `T` について不変と明記し、共変な view は `raw/()` と `Loan<ref/T>` で書く | 採用する | 本書 3.9.1、3.9.3 |
| 2 | Formatting の 3 型も、intrinsic なメタデータを `Loan` の Field に置き換える（本案の範囲に含める） | 含める | 本書 3.9.4 |
| 3 | 局所変数のアドレスの有効期間を、スコープの終わりか値の Move の早いほうまでとする | 採用する | 本書 3.4 |
| 4 | 格納のない依存を表す型の名前を `Kimi.Loan<T>` とする | 採用する | 本書 3.9.2 |
| 5 | 現行で未実装の Typed Null Formation を、本案の実装単位 2 で実装する | 実装する | 本書 7 |

## 10. 精査の記録

### 10.1. 実現性

実装の入口を調べ、各単位が既存の構造の延長で実装できることを確認した。

| 項目 | 確認したこと | 評価 |
| --- | --- | --- |
| 改名 | Semantics 名は `CompilerHelper.TryParse` の一つの表で解決される。`unsafe` は予約語ではなく、文脈で認識される。 | 機械的。量は多い（本書 5.2）。 |
| unsafe 文脈 | 検査は `ControlFlowAnalysis` に集まっており、変換の検査は一箇所である。 | 容易。 |
| `@raw` | bare の Semantics 名と型付きの借用は、`Binding.Conversions` の同じ分岐で扱われる。 | 既存の借用の経路に一行加える規模。局所変数の SSA への昇格を止める点に注意する。 |
| raw Place の借用 | Loan を持たない Origin の推論変数を作る必要がある。`ref` は `static` からの短縮で表せるが、`uniq` は一意の anchor の制限（SPEC §15.2.3）に当たるので、推論変数で表す。 | 最も設計の負担が大きい単位。 |
| `Loan<T>` | スロットの Loan 要求と分散は、`Binding.TypeOrigins` が格納から集め、標準型は `Binding.OriginRequirements` で上書きしている。`Loan<T>` の Field を `T` の格納として集めれば、上書きを削除できる。 | 既存の推論の経路に乗る。 |
| FFI | 形と型の検査は `Binding.Attributes` の `IsImportShape`・`PhysicalCode` にまとまっている。借用は ABI 上ポインターである（impl §21.2.4）。 | 容易。 |

### 10.2. 見つけた矛盾と修正

| 初稿の記述 | 問題 | 修正 |
| --- | --- | --- |
| safe なコードだけでは未定義動作は起こらない | safe な外部関数の宣言が偽なら、safe なコードからでも未定義動作になる。 | 「約束が正しい限り」と限定し、約束の場所を三つと明記した（本書 2、3.1）。 |
| 局所変数の格納はスコープの終わりまで生存する | impl §21.5.5 は、Move の後の格納の再利用を許している。 | スコープの終わりか Move の早いほうまでとした（本書 3.4）。 |
| `p@raw/(raw/T)` は「Borrow targets take precedence」で借用になる | この規定は Identity Acquisition についてのものであり、変換との優先順位には使えない。 | オペランドの型で決める規則にした（本書 3.5）。 |
| raw Place の `uniq` の借用は、期待される型に合わせて適合する | `uniq/T during static` は、一意の anchor の制限（SPEC §15.2.3）に当たる。 | この制限が safe な導出の規則であり、raw Place の借用には適用しないと明記した（本書 3.6）。 |
| `Loan<T>` で標準型のメタデータを置き換えられる | `Slice<T>` は `T` について共変だが、`raw/T` は不変なので、`raw/T` の Field と `Loan` では共変を表せない。 | `raw/T` の不変を明記し、共変な view の書き方（本書 3.9.3）を加えた。`Slice<T>` は格納の Field を持たないので、メタデータを残した。 |
| `let value = *pointer` などの取り出し | 既定の取得（SPEC §3.5）と Take の一覧（SPEC §15.1.5）に合わない、現行仕様の矛盾である。 | raw Place の Take と `(*p)@move` を定めた（本書 1.7、3.7）。 |
| 「9 個の primitive がなくなる」 | `release`・`placeValue` は公開の操作として残る。 | 「9 個を、公開の汎用操作 4 個と `Loan<T>` に置き換える」とした（本書 5.1）。 |
| `Raw.release` は 0 バイトの結果を受け付ける | 型を変換した後のポインターを渡したときの扱いが、決まっていなかった。 | 型を変換したものでもよいと明記した（本書 3.8）。 |

### 10.3. 原則に照らした追加の改善

- **分散の明示（原則 2、3）：** 仕様の欠落だった `raw/T` の分散を定め、共変を格納で明示する形を加えた（本書 3.9）。
- **例外の削減（原則 1）：** Formatting の型も `Loan` の Field に移し、Kimigayo で宣言した型の intrinsic なメタデータを `Slice<T>` だけにした（本書 3.9.4）。
- **warning の判定の明確化（原則 2）：** 各操作が最も内側の Unsafe Block の許可を使うと定め、入れ子のときにどのブロックが不要かを一意にした（本書 3.12）。

### 10.4. 複雑性とメリットの釣り合い

本書 5.3 のとおり、削る規則と型ごとの例外が、加える規則を上回る。加える規則の多くは、既存の Borrow の表、結果の適合、格納からの推論を raw pointer に広げたものである。新しい概念は、`raw` という語、`Kimi.Raw` の 4 関数、`Loan<T>` の三つに限られる。最も費用がかかるのは改名の機械的な更新と、Loan を持たない Origin の推論変数である。前者は意味を変えない単独の単位にでき、後者は `lend`・`split` という compiler-known な primitive をなくす効果が大きい。以上から、複雑性の増加に見合うメリットがあると判断する。

### 10.5. 実装の調査で見つけた、本案と別の問題

- **Typed Null Formation の未実装：** `null@unsafe/T` を扱う binding の経路がなく、関連するテストは失敗を期待している（本書 9 の 5）。
- **CODEMAP の誤った参照：** raw pointer の行と外部関数の行が、実際の実装と違うファイルを指している（本書 8）。
