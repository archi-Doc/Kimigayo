# UTF-8書式化・文字列補間 — 仕様変更案（改訂版）

- 日付: 2026-09-20（2026-09-23 改訂）
- 状態: 設計案。例は提案中のAPIを使っており、実装済みの機能や検証済みのABIを示すものではない。
- 優先順位: 本案で変更する事項は、SPEC.mdとその参照先より優先する。本案で変更しない事項には既存仕様を適用する。
- 前提（既存規則のまま、例外は加えない）:
  - **取得**: 直接の所有Placeは、排他的に貸すとき`@uniq`、転送するとき`@move`を書く。借用値と、排他参照を経由したPlaceは裸で再借用する（§10.2、§15.1.5）。
  - **Origin**: 借用注釈は後置の`during`で書く（§3.3.6、§15.3）。省略時の補完は§15.4に従う。名前付き型の`{name}`はbinding setの命名、型宣言の`{source}`はスキーマヘッダーである。
  - **Loan**: Loanのmodeは作成時に決まる。Originの等式によって共有へ格下げされたり、解除されたりしない（§15.3.5、§15.6.4）。
- 目的: 書式化を「1回の評価と1回の書き込み」で行う。借用とバッファの再利用によって、不要な確保・コピー・再検証をなくす。

## 1. 基本方針

1. 必須Contractの`Stringify`を削除し、`Utf8Format`に一本化する。旧Contractへのフォールバックはない。`Stringify`は普通の識別子として使えるが、特別な効果は持たない。
2. 通常の補間は所有`string`を返す。Writerへ書き、失敗したら残りを省く（短絡する）補間は`$tryWrite`で書く。
3. `Console.writeLine`は`ref/string`と`Text.Utf8Slice`を受け取る。どちらでも所有権を取得・保持・破棄しない。
4. 出力はUTF-8で、既定書式だけを提供する。桁揃え・精度・基数・ロケール・実行時書式文字列はない。数値の`@string`変換や文字列`+`は変更しない。
5. `BufferWriter`と`Utf8Format`は、ユーザーが実装できる静的Contractとする。確保もboxingも必要としない。
6. 固定長領域の容量不足は`BufferFull`とする。伸長可能なWriterの確保失敗やサイズ上限超過は、既存の確保用Abortとする。
7. 長さと容量はすべてバイト数で、型は`isize`である。
8. **堅牢性の原則**: ユーザー実装が規約に違反しても、未初期化領域の公開や、不正なUTF-8を含む`string`/`Utf8Slice`は生じない。違反の影響は、失敗になるか、出力内容が誤るかにとどまる。

## 2. 公開API

### 2.1. 配置と生成

**配置規則**: Contractの署名に現れる名前は`Kimi`直下に置き、それ以外は`Kimi.Text`に置く。既定aliasによって`Text`は使えるが、その内部は再帰的には開かない。

| 場所 | 名前 |
| --- | --- |
| `Kimi` | `Utf8Format`、`BufferWriter`、`WriteWindow`、`Utf8Writer`、`BufferFull` |
| `Kimi.Text` | `FixedBuffer`、`HeapBuffer`、`Utf8Slice`、`InvalidUtf8`、§2.4の関数 |

**生成規則**:
- 状態を持たないエラー型（`BufferFull`、`InvalidUtf8`）は、引数なしの公開`init()`で作る。
- 不変条件や借用を持つ型は`init`を公開しない。`Text`の関数か、他の操作の結果としてだけ得られる。

### 2.2. Contract

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

- `reserve`の結果の`source`は、省略規則（§15.4.3）によって`self`に補完される。
- `format`では、外側の借用と`Utf8Writer`の`target`がそれぞれ独立した入力Originになる。整形式条件により、`target`は外側の借用より長く生存する。
- 適合は、既存の明示的な適合宣言と実装照合（§8.4.4–5）に従う。`BufferWriter`が保証する操作は`reserve`だけである。
- `format`はWriter型を総称引数に持たない。Writer型の違いだけでは別インスタンスを要求しない（生成方針は§6.3）。Runtime Contract Viewへの対応は本案では追加しない。

### 2.3. 型

| 型 | 分類 | 所有・依存 |
| --- | --- | --- |
| `BufferFull` / `InvalidUtf8` | 通常の宣言 | 状態を持たないCopy |
| `HeapBuffer` | 通常の宣言 | Non-Copy。伸長可能なヒープ領域を所有する |
| `Utf8Slice {source}` | 通常の宣言 | Copy。検証済みのUTF-8領域を共有借用する（非公開の`Slice<u8>`を保持し、Loan要求は`ref`）。参照カウントは更新しない |
| `FixedBuffer {source}` | 検証済み組み込み型 | Non-Copy。呼び出し側の固定長バイト領域を排他借用する。Loan要求は`uniq` |
| `WriteWindow {source}` | 検証済み組み込み型 | Non-Copy。書き込み領域と共通状態（§3.1）を排他借用する。Loan要求は`uniq` |
| `Utf8Writer {target}` | 検証済み組み込み型 | Non-Copy。型を消去したWriterへの`uniq`借用と、失敗状態を保持する。Loan要求は`uniq` |

**検証済み組み込み型（§15.3.5）**:
- 生ポインターで実装する。スロット、Loan要求、変性（`source`/`target`について共変）、Copy分類は、§22.1の固定メタデータとして定める。
- ソースコードから同等の型を作ることはできない。Phantom Originは権限を与えないためである。

#### 2.3.1. `Utf8Writer`の型消去

実行時の型消去と、コンパイル時の安全性情報の保持を分ける。

- **寿命**: `Text.writer`に渡す`uniq/W during target`の整形式条件（§15.6.1）により、`W`の依存はすべて`target`以上に長く生存する。アダプターは`target`を超えて使えない。
- **アクセス**: アダプターは`W`の値を保存・移動・公開せず、`reserve`だけを呼ぶ。実際のLoanの依存先、mode、親子関係、静的ストレージへの依存は消去しない。
- **呼び出し効果**: 選択した`reserve`実装の検証済み効果情報を保持する。間接呼び出しでも、静的変数へのアクセスや再入を含む効果を、呼び出し時の有効なLoanと照合する（§15.6.4）。効果が不明なら、競合し得るアクセスとして保守的に扱う。

これらの情報は、アダプターの転送・格納・関数境界・別コンパイルでも保持する。複数の実装が到達し得る場合は、すべての候補の効果を考慮する。実行時の寿命タグや借用検査は追加しない。

例えば、書式化する値が静的変数を共有借用している間に、その変数を書き換える`reserve`を呼ぶことはできない。`W`が十分長く生存することだけでは、この競合は解消しない。

### 2.4. `Kimi.Text`の関数

```kimi
group Text
    public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer
    public func heap(capacity: isize) -> HeapBuffer
    public func writer<W>(destination: uniq/W) -> Utf8Writer
        W is BufferWriter
    public func utf8(text: ref/string) -> Utf8Slice
    public func validateUtf8(bytes: Slice<u8>) -> Result<Utf8Slice{r}, InvalidUtf8>
        origin r.source == bytes.source
    public func toString<T>(value: ref/T) -> string
        T is Utf8Format
    public func tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8])
        -> Result<Utf8Slice{r}, BufferFull>
        T is Utf8Format
        origin r.source == destination
```

| 関数 | 動作 |
| --- | --- |
| `fixed` | 確定済み長さ0、容量Nのバッファを返す。入力配列は初期化済みでなければならない |
| `heap` | 確定済み長さ0、容量は指定値以上のバッファを返す。0なら未確保で開始する。負数は`KIMI_E_ARG_RANGE`。上限超過と確保失敗は既存の確保用Abort |
| `writer` | 失敗状態を持たないアダプターを返す |
| `utf8` | ビューを返す。確保・コピー・再検証はしない |
| `validateUtf8` | 全体を1回だけ検証する。確保・コピーはしない |
| `toString` | §5.1と同じ経路で所有`string`を作る。書式化の`BufferFull`は`KIMI_E_FORMAT`。**これを`string`複製の正式な入口とする**。文字列複製は、空なら確保0回でStaticの空文字列、非空ならバイト長ちょうどの確保1回で独立した内容を返す |
| `tryFormat` | ローカルの`FixedBuffer`とアダプターを使って1回だけ書式化し、`intoText()`でビューを返す。失敗時は`BufferFull`を返す（途中まで書いたバイトは領域に残り得るが、結果には含めない）。新しい空のバッファにアダプターだけで書くので、`intoText()`は再検証なしで必ず成功する。長さの事前測定や自動再試行はない |

`tryFormat`の結果は、`destination`の排他Loanを保持し続ける。ビューを使う間、元の配列はビュー経由でしか読めない。

```kimi
var small: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, small@uniq)  // Ok: 長さ3。ヒープ確保なし
// let b = small[0]                           // エラー: resultがまだ使われるなら排他Loanが続いている
match result
    .Ok(let view) => Console.writeLine(view)
    .Err(_) => Console.writeLine("too small")
```

### 2.5. 固定配列のfill構築（§4.3に追加）

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false]                     // [8 of bool]
let cells: [(W * H) of u8] = [(W * H) of 0]  // 複合した長さ式は括弧で囲む
```

- `[Length of value]`は、期待型の有無にかかわらず**常に固定配列**を構築する。`Array<T>`を期待する位置では型不一致のエラーとし、変換はしない。
- `Length`は§4.2の長さ文法に従う。`of`はこの位置でも文脈キーワードである。
- 要素型`T`は`value`の型で、Copyでなければならない。具体的な型は、既存の期待型とリテラルの規則で決める。
- `value`は`N = 0`でも1回だけ評価・取得し（裸のPlaceはCopy）、それをN回Copyする。
- 最適化の許可: fill構築した配列が`FixedBuffer`経由でしか読まれない場合、実装はfillの書き込みを省略してよい。`FixedBuffer`は確定済みの部分しか公開しないので、省略しても観測できない。
- 生成関数による構築や、未初期化領域の借用は追加しない。

### 2.6. 標準バッファの操作

`FixedBuffer`と`HeapBuffer`は`BufferWriter`に適合し、次の操作を持つ。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `length` / `capacity` | getter、`ref/Self` | 確定済みのバイト数 / 全容量 |
| `bytes()` | `ref/Self` | `Slice<u8>`。確定済みの部分だけを返す |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`。未検証の末尾を検証するが、結果を記録しない |
| `validate()` | `uniq/Self` | `Result<(), InvalidUtf8>`。検証に成功したことを記録する |
| `clear()` | `uniq/Self` | 確定済み長さと検証済み長さを0に戻し、容量は保持する。内容の消去は保証しない |
| `reserve(minimum)` | `uniq/Self` | §3.1のWindowを返す |
| `intoText()`（`FixedBuffer`のみ） | `owner/Self` | `Result<Utf8Slice{r}, InvalidUtf8>`、`origin r.source == self.source`。未検証の末尾だけを検証し、元領域の確定済み部分のビューを返す。成功・失敗のどちらでもバッファを消費し、元配列は解放しない |
| `intoString()`（`HeapBuffer`のみ） | `owner/Self` | `Result<string, InvalidUtf8>`。未検証の末尾だけを検証し、ポインター・長さ・解放責任を`string`へ移す。縮小は§3.6に従う。未確保のまま空なら結果はStatic。確保済みなら長さ0でも元のポインターを保持する。検証失敗時はバッファを通常どおり破棄する |

- 書き方の例: `buffer.text()`、`buffer@uniq.validate()`、`buffer@move.intoText()`。
- `bytes()`と`text()`の結果は、受け手の借用に依存する。`intoText()`の結果は元配列の排他Loanを引き継ぎ、ローカルのバッファには依存しない。
- 共有Slice・Window・アダプターが保持するLoanと競合する操作は拒否する。一般の可変Sliceは導入しない。

### 2.7. `Utf8Slice`

| 操作 | 動作 |
| --- | --- |
| `length` | getter、`self: Self`。バイト数 |
| `bytes(self: Self) -> Slice<u8>{s}`、`origin s.source == self.source` | 元領域の借用を保ったままバイト列を返す |

- 有効なUnicode scalarのUTF-8だけを受理する。不完全な列、過長な符号化、surrogate、範囲外の値は拒否する。置換や正規化はしない。NULはデータとして扱う。
- バイト単位で任意に切り出した部分を、有効なUTF-8とみなしてはならない。

### 2.8. `Utf8Writer`

| 操作 | 動作 |
| --- | --- |
| `write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>`、`T is Utf8Format` | §4.2の手順で1回だけ書く |
| `status(self: ref/Self) -> Result<(), BufferFull>` | 失敗状態を返す |

- `string`、`Utf8Slice`、`char`も`Utf8Format`に適合するので、書き込み操作は`write`の1つで足りる。
- 書き方: ローカルのアダプターは`writer@uniq.write(x)`、引数として受け取ったアダプターは`writer.write(x)`と書く。値は共有借用で渡り、元のPlaceを転送しない。
- 状態のリセット、rawなWindow、内部Writerへのアクセス、所有権の取り出しは公開しない。
- `reserve`は、型を消去したWriterへの間接呼び出し1回で行う。標準Writerから作ったアダプターでは、実装が直接呼び出しに置き換えてよい。
- 通常のメソッドは、直接呼び出しでも関数値経由でも、通常どおり引数を評価する。短絡するのは`$tryWrite`（§5.2）だけである。

## 3. バッファの安全性と容量管理

### 3.1. 共通状態と予約

共通状態は、領域のアドレス・容量・確定済み長さ・検証済み長さを持つ。不変条件は次のとおり。

- `0 <= 検証済み長さ <= 確定済み長さ <= 容量 <= MaxObjectSize`。確定済みの部分はすべて初期化済みである。
- 公開するSliceは確定済みの部分に限る。検証済み長さまでは有効なUTF-8である。
- Windowは未確定の末尾だけに書き、書き込み済みの範囲は先頭から途切れない。
- Windowが生きている間、領域と共通状態は排他Loanで保護され、移動・再予約・伸長・破棄はできない。

**`reserve(minimum)`**:
- 成功した結果は`written == 0`かつ`remaining >= minimum`を満たし、確定済み長さを変えない。
- `minimum == 0`のときは空のWindowを返してよい。標準Writerはこの要求だけを理由に確保しない。
- 負数は`KIMI_E_ARG_RANGE`でAbortする。
- `FixedBuffer`は、空きが要求以上なら成功し、足りなければ`BufferFull`を返す。
- `HeapBuffer`は、容量内なら確保せず成功し、足りなければ伸長する。確保失敗を`BufferFull`に置き換えない。
- どのWriterも、`BufferFull`を返すときは確定済みの内容と長さを変えない。

**`HeapBuffer`の伸長**:
1. `minimum > MaxObjectSize - length`なら`KIMI_E_ALLOC_SIZE`とし、それ以外は`required = length + minimum`を計算する。
2. 先行確保分`hint`（§4.4）が表現できないか、`hint > MaxObjectSize - required`なら、`hint`を捨てて`target = required`とする。それ以外は`target = required + hint`とする。`hint`は非負で、補間・`Text.toString`の内部バッファ以外では0である。
3. `target`が容量内なら伸長しない。不足時の新しい容量は、`capacity <= MaxObjectSize / 2`なら`max(target, 2 * capacity)`、それ以外は`target`とする。コピーするのは確定済みの部分だけである。空出力や`minimum == 0`だけを理由に先行確保しない。

### 3.2. Window

Windowは、予約の開始位置・書き込み済み長さ・容量の上限を持つ小さな値で、それ自体は確保を行わない。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `written` / `remaining` | getter、`ref/Self` | 今回書き込んだ長さ / 残りの容量 |
| `push(byte: u8)` | `uniq/Self` | `Result<(), BufferFull>`。末尾に1バイト書く |
| `append(bytes: Slice<u8>)` | `uniq/Self` | `Result<(), BufferFull>`。全バイトを末尾へコピーする |
| `limit(maximum: isize)` | `owner/Self` | `Self`。Windowを消費し、容量を`min(現在の容量, maximum)`に制限したものを返す |
| `commit()` | `owner/Self` | `isize`。書き込み済みの部分を確定し、そのバイト数を返す |

- 書き方の例: `window@uniq.push(b)`、`window@move.limit(n)`、`window@move.commit()`。
- `push`と`append`は、全体が収まる場合だけ書く。収まらなければ何も変更しない。
- `limit`は、`maximum < written`なら`KIMI_E_ARG_RANGE`でAbortする。領域を拡大・複製・確定することはない。
- `commit()`は確定済み長さだけを更新し、コールバックは呼ばない。
- 消費済みのWindowは使えない。未確定のまま破棄されたWindowは何も確定しない。
- 未初期化領域の読み取り、途中に穴を空ける書き込み、任意の長さを確定する`advance(n)`は許さない。

### 3.3. ユーザー定義Writer

生ポインターからWindowを作る公開APIはない。ユーザーWriterは標準Writerを保持し、そのWindowを転送するか、`limit`で制限してから返す。

```kimi
struct Limited
    Self is BufferWriter
    var inner: Text.HeapBuffer
    let maximum: isize

    public init(maximum: isize)
        if maximum < 0 => $abort("maximum must be nonnegative")
        self.inner = Text.heap(0)
        self.maximum = maximum

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        let remaining = self.maximum - self.inner.length // 不変条件により0以上
        if minimum > remaining => return .Err(BufferFull.init())
        let window = try self.inner.reserve(minimum)      // 負数は内側がKIMI_E_ARG_RANGEで拒否
        return .Ok(window@move.limit(remaining))
```

`maximum`を不変にし、構築時に非負であることを検査しているので、`0 <= inner.length <= maximum`が常に成り立つ。このため減算はオーバーフローしない。出力の順序・内容と容量の約束を守る責任はユーザー実装にあるが、違反しても§1.8の原則は保たれる（§3.4）。

### 3.4. Window規約の検査とUTF-8の検証状態

**原則: 有効なUTF-8と確認できた連続した先頭部分だけを、排他権限で検証済みにする。** 確認方法は、アダプターによる有効なUTF-8の生成と、既存バイト列の検証の2つである。

アダプターは`reserve`の結果を受け取るたびに、`written == 0`かつ`remaining >= minimum`であることをO(1)で検査する。これは、ユーザーが先に書いた未確認のバイトを、アダプターが生成したものとして扱わないためである。

- 満たさない場合は、Windowを確定せずに破棄し、`BufferFull`として失敗状態にする。したがって、ユーザーが先に書いたバイトは確定されない。
- 標準Writerは常にこの条件を満たすので、実装はこの分岐を省略してよい。

| 操作 | 検証済み長さの扱い |
| --- | --- |
| rawな`commit()` | 変更しない |
| アダプターの確定 | 予約時点で`検証済み長さ == 確定済み長さ`だったときだけ、新しい確定済み長さまで進める |
| `text()` | 未検証の末尾を検証するが、記録は変更しない |
| `validate()` | 未検証の末尾が有効なら、確定済み長さまで進める。失敗時は何も変更しない |
| `intoText()` / `intoString()` | 同じ検証を行い、成功した場合だけ結果を返す |
| `clear()` | 0に戻す |

空または検証済みのバッファにアダプターだけで追記すると、`検証済み長さ == 確定済み長さ`が保たれ、検証は0バイトで済む。rawな書き込みの後で繰り返し読む場合は、先に`validate()`で検証結果を記録しておく。

```kimi
try buffer@uniq.validate()      // 未検証の末尾を1回だけ検証する
let first = try buffer.text()   // 再検証なし
let second = try buffer.text()  // 再検証なし
```

`validate()`はアダプターの失敗状態を変更しない。UTF-8として有効かどうかと、書式化が成功したかどうかは別の状態である。

### 3.5. 破棄と借用の終了

- `FixedBuffer`・`WriteWindow`・`Utf8Writer`は`deinit`を持たず、破棄時に借用先を観測しない。未確定のWindowを破棄しても、長さの更新・出力・通知は行わない。
- したがって借用は最終使用の後で終了し、元のWriterを再利用できる。後続の使用や`defer`がある場合は、それまでLoanが続く。一般の`deinit`規則を緩めるものではない。
- `HeapBuffer`は、所有する領域を通常の破棄で1回だけ解放する。`FixedBuffer`は元配列を解放しない。

### 3.6. その場での縮小

`intoString()`は、UTF-8の検証成功後、解放責任を移す前に、不要な容量の縮小を試みてよい。縮小は任意の最適化であり、対応しない実装は元の領域をそのまま移す。

- 成功しても、アドレス、確定済みの内容、唯一の解放責任を保つ。容量は長さ以上とし、追加確保・領域の移動・内容のコピーは行わない。
- 縮小できなければ何も変更せず、元の領域を使って`intoString()`を成功させる。縮小の失敗を`InvalidUtf8`やAbortにせず、再試行もしない。
- 長さ0では縮小を行わない。確保済みの領域を解放したり、Staticの空文字列に置き換えたりしない。

縮小後のポインターも同じアロケーターで1回だけ解放できなければならない。この契約を保証できないホスト操作は使わない。通常の確保・解放の失敗規則は変更しない。

## 4. 書式化の共通規則

### 4.1. 表現と評価

- `format`は値を共有借用する。値を消費せず、出力先や入力への借用を結果に残さない。成功時は完全な表現を出力する。
- **同じ値・同じ外部状態からの成功出力は、出力先の型や容量によって変わってはならない。** 容量が足りないときに省略形を成功として返さず、`BufferFull`を返す。これはユーザー実装が守る法則で、コンパイラはユーザー実装についてこの法則を最適化の根拠にしない。
- 純粋性は要求しない（副作用や独自の確保は許可する）。`format`は1回だけ呼ぶ。事前の長さ測定や、容量拡張のための再実行はしない。
- 組み込み型の参照動作は次のとおり。状態を確認し、正確なバイト長を求め、その長さで1回予約し、一括で追記して確定する。空出力は予約しない。例えば`123`は3バイトで予約するので、ちょうど3バイトの領域で成功する。
- 具体的な書き込み先が標準Writerに確定し、§4.5の保存条件を満たす場合は、直接の符号化や予約の統合を許可する。ユーザーWriterへの予約引数と呼び出し回数は保つ。
- アダプターが確定する各区間は有効なUTF-8である。失敗までに確定した区間は残るが、不完全な文字は確定しない。

### 4.2. 失敗状態と`write`の手順

アダプターは最初の`BufferFull`を保持する。以後の書き込みは、内部の予約・書式化を呼ばずに同じ失敗を返す。空出力も同じである。

`write`の手順（通常の補間、`$tryWrite`、`toString`、`tryFormat`も共通）:

1. すでに失敗状態なら、失敗を返す。
2. 選択された`format`を1回呼ぶ。組み込み型の場合は§4.1の参照動作を行う。
3. `format`が失敗を返した場合、または呼び出し中に失敗状態になった場合は、失敗を保持して返す。実装が書き込みエラーを無視して成功を返しても、成功とは扱わない。実装が独自に返した`BufferFull`も保持する。

- 失敗状態は、ユーザーの`format`がエラーを握りつぶしても成功扱いにしないための**安全網**である。基本の書き方は、`try`か`$tryWrite`による伝播とする。結果を捨てた場合のResult破棄警告（§17.4）は通常どおり出る。
- `format`を直接呼ぶのは通常の関数呼び出しで、状態確認を含まない。
- 失敗状態は暗黙にリセットしない。再開するには、アダプターの使用を終え、必要なら元のWriterを`clear()`して、新しいアダプターを作る。確定済みの出力や副作用の巻き戻しは保証しない。
- rawなWindowで起きた`BufferFull`は、アダプターの失敗状態に影響しない。

```kimi
writer = Text.writer(buffer@uniq) // 失敗後の再開は新しいアダプターで行う
```

### 4.3. 既定書式

| 値 | 表記 |
| --- | --- |
| 整数 | ASCIIの10進数。負数だけに`-`を付け、不要な先頭ゼロはない。最小負数も扱う |
| `bool` | `true` / `false` |
| `char` | そのUnicode scalarのUTF-8 |
| Unit | `()` |
| `string` / `Utf8Slice` | 内容をそのまま出力する。NULも含め、バイト長で扱う |
| 浮動小数点 | 次の段落の規則 |

**浮動小数点**:
- 有限かつ非ゼロの値は、「対象型へ最近接・偶数丸めで戻すと同じ値になる10進表現」のうち、有効桁数が最少のものを選ぶ。同じ桁数の候補が複数あれば正確な値に最も近いものを、同じ距離なら末尾の有効数字が偶数のものを選ぶ。`f32`は`f32`の値として桁を選ぶ。
- 正規化した10進指数`e`が`-4 <= e < 16`なら固定小数点、それ以外は指数表記とする。不要な末尾のゼロや小数点は出さない。指数は小文字の`e`で、正の指数の`+`と先頭ゼロは省く。
- 特殊値は`0` / `-0`、`Infinity` / `-Infinity`、`NaN`（符号やpayloadによらない）と表記する。
- 小数点は`.`とし、桁区切りやロケール依存の表記はない。

**適合しないもの**:
- 借用型から参照先の適合への委譲は設けない。借用値の書式化は、§5.1の引数適合によって参照先の型で決まる。
- オブジェクトハンドル、オブジェクト借用、ポインターは書式化の対象外とする。payloadを書式化するには、明示の射影（§13.5.5.1）で渡す。診断ではその書き方を提案する。
- Tuple・配列・ユーザー型の自動適合は追加しない。

### 4.4. 容量の見積もり

| 型 | 最大バイト長 |
| --- | --- |
| `i8` / `u8` | 4 / 3 |
| `i16` / `u16` | 6 / 5 |
| `i32` / `u32` | 11 / 10 |
| `i64` / `u64` | 20 / 20 |
| `i128` / `u128` | 40 / 39 |
| `isize` / `usize` | ポインター幅に対応する整数型と同じ |
| `bool` / `char` / Unit | 5 / 4 / 2 |
| `f32` / `f64` | 17 / 24 |

`string`、`Utf8Slice`、ユーザー型は**無界**（静的な上限なし）とする。上限の加算はコンパイル時に行い、`MaxObjectSize`を超えたら見積もりを放棄する（Abortしない）。

補間と`Text.toString`の内部`HeapBuffer`は、次の規則で確保する。長さを知るために式を先に評価したり、ユーザー書式化を実行したりはしない。

- **有界な補間**（すべての値に上限があり、リテラル長との合計が`MaxObjectSize`以下）: 合計が0なら未確保のまま、それ以外は合計を1回だけ確保し、伸長しない。
- **それ以外**: 未確保で開始する。各予約の`hint`（§3.1）は、それより後に続くリテラル長と有界な値の上限の合計である。無界の値は0として数え、その予約自身の分は含めない。
- **遅延と統合**: §4.5の条件で、リテラルの書き込みと確保を後続の値の予約まで遅らせ、1回にまとめてよい。保留したリテラルは予約の実必要長に含め、元の順序で書く。後続の値が空出力で予約しない場合も、成功完了までに保留分を確定する。
- 実際に必要な長さが上限を超えたらAbortし、見積もり（`hint`）が上限を超えたら`hint`だけを捨てる。式や書式化の再実行、確保の再試行はしない。
- `hint`を使うのは内部バッファだけである。`$tryWrite`とユーザーWriterには適用せず、予約の引数と回数をそのまま保つ。

例: 上限をMとし、長さM−5の文字列の後に数値`0`を出力する場合、数値の最大長20を加えると上限を超える。この場合は`hint`だけを捨てれば、書き込みを続けられる。

### 4.5. 最適化で保存する動作

直接符号化、予約の統合、確保の遅延、スタックへの配置には、次の共通規則を適用する。

| 項目 | 規則 |
| --- | --- |
| 型・借用 | 受理するプログラム、選択した実装、Loan、Origin、一時値の寿命を変えない |
| 評価・副作用 | 式と書式化の評価順・回数、ユーザーの副作用、破棄順序、制御移動を保つ |
| 意味上の失敗 | `BufferFull`、明示的なAbort、引数検査、実必要長の上限検査と、それらより前に行う副作用の順序を保つ |
| 内部の資源失敗 | 補間・`Text.toString`の非公開な一時バッファについて、確保・解放の省略・統合・遅延による資源失敗の有無や時点の差は観測対象から除く |
| 外部の操作 | ユーザーWriterの予約、ユーザーコードの確保・解放、OSへの出力には、内部の資源失敗の例外を適用しない |

遅延したリテラルも論理上の出力長には直ちに加え、上限検査を後続の式の評価より後へ移さない。実際に行う確保・解放が失敗した場合は、既存のAbortと診断位置の規則に従う。確保を再試行したり、ユーザー処理を再実行したりしない。

例えば`"prefix\(sideEffect())"`の内部確保を遅らせると、確保失敗より先に`sideEffect()`が実行される場合がある。この資源失敗との順序差は許すが、明示的なAbortや長さ上限の検査を越えて同関数を実行してはならない。

## 5. 補間

### 5.1. 通常の補間

```kimi
let message = "My number is \(self.number)"
```

- 構文・エスケープ・入れ子は§2.9に従う。結果は所有`string`である。
- 各埋め込み式は、`write<T>(value: ref/T)`の引数として§10.2の規則で適合させる。決まった`T`には`Utf8Format`への適合を静的に要求する。

| 埋め込み式 | 適合 | `T` |
| --- | --- | --- |
| 所有Place（Copy型を含む）・所有一時値 | 共有借用 | 式の型 |
| `ref/U` | Exact | `U` |
| `uniq/U`・排他参照を経由したPlace | 共有Reborrow | `U` |

- 裸の式は元のPlaceを転送しない。`\(x@move)`と書くと、転送された一時値を共有借用する。リテラルは通常の既定型で型が決まり、補間結果の`string`を期待型にはしない。
- **下降**:
  1. §4.4に従って内部`HeapBuffer`を作り、`Utf8Writer`で借用する。
  2. 左から順に書く。各式は1回だけ評価し、その値を書き終えてから次の式へ進む。内部処理の遅延・統合は§4.4–5に限る。
  3. 最初の失敗で終了し、`KIMI_E_FORMAT: Formatting failed`でAbortする。成功したらアダプターの使用を終え、`intoString()`で所有`string`にする。
- 値の借用は`write`の終了時に終わる。一時値の破棄境界は既存規則のままである。
- 内部バッファはコンパイラだけが保持し、アダプター以外からは書かれない。そのため`検証済み長さ == 確定済み長さ`が保たれ、`intoString()`は再検証なしで成功する。生成コードは失敗分岐を持たない。

### 5.2. 短絡する`$tryWrite`

```kimi
$tryWrite(writer@uniq, "(\(point.x), \(point.y))")
```

- `$tryWrite`はComposition Rootの操作で、関数値にはならない。
- **第1項**は`uniq/Utf8Writer`の引数位置として、通常の規則で取得する。裸の直接所有Placeはエラーとし、`writer@uniq`を提示する。
- **第2項**は文字列リテラルの構文に限る。補間を含まない通常の文字列やraw文字列も書ける。任意の文字列値を書くには`write`を使う。
- 結果は`Result<(), BufferFull>`で、全体を表す`string`は作らない。
- **評価**:
  - 第1項を1回だけ評価して借用を開始し、左からリテラルと値を同じアダプターへ書く。埋め込み式の型付けは§5.1と同じである。
  - 最初の失敗で終了し、以降の埋め込み式は評価しない。開始時点で失敗状態なら、埋め込み式を1つも評価しない。
  - 評価されない式にも、型・適合・制御移動先の検査は行う。未確定のWindowは破棄し、確定済みの出力は残す。
- **境界**:
  - 展開しても、元の式の一時値境界と、`return`・`exit`・`yield`の対象は変わらない。
  - 借用は埋め込み式の評価より前から有効なので、埋め込み式から同じアダプターやその借用元を読むことはできない。
  - `$tryWrite`は呼び出しではないので、呼び出し予約（§15.6.7）は適用しない。
- `writer@uniq.write("…\(v)…")`では通常の補間が先に完了し、その後で書き込まれる。補間を含む文字列リテラルが`Utf8Writer.write`の引数に直接現れた場合は、§17.4の警告を出して`$tryWrite`を提案する（意味は変えない）。

```kimi
struct Point
    Self is Utf8Format
    var x: i32
    var y: i32

    public func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
        return $tryWrite(writer, "(\(self.x), \(self.y))")
```

## 6. コンソール出力と最適化

### 6.1. `writeLine`

```kimi
public func writeLine(text: ref/string) -> ()
public func writeLine(text: Text.Utf8Slice) -> ()
```

- `string`の値には`ref/string`の候補だけが適用でき、`Utf8Slice`の値には`Utf8Slice`の候補がExactになる。暗黙の変換はないので、2つの候補は競合しない。
- 期待型のない関数参照`Console.writeLine`は、既存のオーバーロード規則により曖昧になる。
- どちらもUTF-8の全体とLFを出力してUnitを返し、`WriteStdout(data, length)`を直接呼ぶ。stringハンドルの実体化や解放責任の移動は行わない。出力の失敗、部分出力、flushなどは§22.4に従う。

公開APIだけで、確保なしの出力を書ける。

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]   // FixedBuffer経由でしか読まないので、fillは省略できる
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    match buffer.text()                   // writerの借用はここで終わっている
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => $abort("unexpected invalid UTF-8")
    return .Ok(())
```

### 6.2. 有界な補間の最適化

`Console.writeLine`の引数に補間リテラルが直接現れ、次の条件をすべて満たす場合、実装は§6.1と等価なスタック上の経路へ下降してよい。

- 有界な補間（§4.4）である。
- 見積もりの合計が、実装が定めるスタック領域の上限以下である。
- §4.5の保存条件を満たす。

この経路の規則:
- 固定領域の長さは見積もりの合計とする。fill構築は不要で、`text()`の再検証も省略する。
- 有界な補間の書き込みは容量不足にならないので、ヒープ経路への切り替えや再実行は起きない。埋め込み式自体のAbortや制御移動は通常どおり扱う。
- 補間全体が成功してから出力する。書式化に失敗した場合は補間本文を出力しない。出力開始後のOSエラーによる部分出力は、§6.1の規則に従う。
- 条件を満たさない補間は、通常のヒープ経路を使う。

### 6.3. 性能の合格条件

| 経路 | 要求 |
| --- | --- |
| Window・ビュー・アダプターの管理 | 追加のヒープ確保、管理用コールバック、参照カウント更新なし。`reserve`ごとの間接呼び出しは1回以下（標準Writerでは0回にできる） |
| 組み込み型＋`FixedBuffer` | 収まる場合、ヒープ確保0回。中間のstring、boxing、引数配列なし |
| 組み込み型＋`HeapBuffer` | 容量内なら追加の確保0回。`clear()`で容量を再利用できる |
| `検証済み長さ == 確定済み長さ`のときの`text()`・`validate()`・`intoText()`・`intoString()` | 追加の検証0バイト。追加の確保・コピーなし |
| 有界な補間の所有結果 | ヒープ確保1回以下。完成時のコピーなし |
| 無界の値が`string`/`Utf8Slice`の1つだけで、それより前がリテラルだけの補間 | ヒープ確保1回以下。空結果は0回。`hint`を捨てた場合は回数上限を適用しない |
| `Text.toString(s)`（`s: string`） | 空なら確保0回。非空ならバイト長ちょうどの確保1回 |
| `Console.writeLine("My number is \(n)")`、`n: i64` | 最適化が有効なら、33バイト（リテラル13＋数値20）のスタック領域を使い、ヒープ確保0回 |
| コード量 | Writer型の違いだけでは`format`の別インスタンスを要求しない |

最初のプロファイルはmonomorphization（§21.3.1）である。書式化対象の同じ具体型では、Writer型によらず通常の`format`本体を共用する。§4.5を満たすインライン化や自動特殊化は許可し、物理的なコードの複製まで禁止するものではない。組み込み型の書式化は直接呼び出しかインライン化で行う。

任意長の所有結果、容量を超える伸長、ユーザー実装やOS内部の確保については、確保ゼロを保証しない。

## 7. 検証

実装時は、ネイティブ実行、確保カウンター、生成コードで次を確認する。

- **表記と評価**
  - 整数・浮動小数点の境界値と最大長、UTF-8検証、NUL、空出力が正しいこと。
  - 各式と`format`が1回だけ実行され、出力先によって成功結果が変わらないこと。
  - ちょうど収まる領域で成功し、1バイト足りない領域で失敗すること。`limit`が効くこと。
  - 無視された書き込みエラーを検出し、独自に返された`BufferFull`が`KIMI_E_FORMAT`になること。
  - 短絡するのは`$tryWrite`だけであること。直接呼び出しと関数値経由の呼び出しで結果が一致すること。
- **容量**
  - 実際に必要な長さの超過が`KIMI_E_ALLOC_SIZE`になり、見積もりの超過では`hint`だけが捨てられること。
  - 伸長計算の境界、空の予約、遅延確保をしたときの確保回数が正しいこと。
  - 空文字列の複製と空結果が確保0回、非空文字列の複製がバイト長ちょうどの確保1回であること。空の埋め込み値が予約しなくても、保留したリテラルが欠落しないこと。
  - 確保の遅延・統合が§4.5に従うこと。明示的なAbort、実必要長の上限検査、副作用、破棄、制御移動の順序を保ち、内部資源失敗との順序差だけを区別すること。
  - スタック上限を超える有界な補間がヒープ経路になること。
- **Windowと検証状態**
  - 二重確定、消費後の使用、競合する予約・伸長・aliasを拒否すること。未確定のまま破棄しても長さが変わらないこと。
  - 規約違反のWindow（`written != 0`、`remaining < minimum`）が`BufferFull`になり、不正なUTF-8を確定しないこと。
  - rawな書き込みの後の`text()`は検証結果を記録せず、`validate()`は成功時だけ記録すること。未検証部分を飛び越えないこと、`clear()`後の再利用、書式化の失敗状態がリセットされないことも確認する。
- **借用と寿命**
  - `intoText()`と`tryFormat`の結果が元配列の排他Loanを保持し、競合する直接の読み取り・書き換えを拒否すること。呼び出し元の配列に依存するビューの返却は許可し、元領域より長い寿命への脱出だけを拒否すること。
  - 管理型の破棄が借用先を観測せず、最終使用の後で再利用できること。`defer`や後続の使用がある場合はLoanを保持すること。
  - Originの明示形と省略形で結果が一致すること。`Utf8Writer`の型消去によって`target`を超える使用ができないこと。
  - 型消去・転送・格納・別コンパイルの後も、Loanの依存先と`reserve`の効果情報を保持すること。静的変数を借用する書式化入力と、それを書き換える`reserve`の競合を拒否し、不明な効果も保守的に検査すること。
- **取得と構文**
  - `@uniq`や`@move`が必要な位置で、裸の直接所有Placeを診断すること。借用値は裸で再借用できること。埋め込み式が元のPlaceを転送しないこと。
  - fill構築で、`N = 0`でも`value`を1回評価すること、`Array`の期待型でエラーになること、複合した長さ式に括弧を要求すること。
- **出力と記憶域**
  - `writeLine`の後も入力を使えること。書式化失敗では補間本文を出力せず、出力開始後のOSエラーでは既存仕様どおり部分出力を許すこと。
  - `intoString()`と補間の途中離脱で、二重解放や解放漏れがないこと。スタック領域が外部へ脱出しないこと。
  - その場での縮小の成功・失敗・非対応時に、内容と解放責任を保持すること。長さ0では縮小せず、縮小失敗によるAbort・追加確保・再試行がないこと。
  - Writer型を増やしても、通常の`format`本体のインスタンス数が増えないこと。インライン化・自動特殊化による複製は分けて測定すること。
  - §6.3の性能条件を満たすこと。

## 8. 正式仕様への適用範囲

本案の編集では、SPEC.mdと参照先の仕様を変更しない。統合時には次を反映する。

| 箇所 | 変更内容 |
| --- | --- |
| §2.5.1、Appendix F | `of`の文脈にfill構築を加える。`[Length of value]`と`$tryWrite`の構文を追加する |
| §2.9.1、§12.2、§12.3.3 | 補間を`Utf8Format`と`write`の引数適合に変更する。借用値の適合委譲の記述を削除する。`$tryWrite`の評価順と短絡を追加する |
| §4.3 | fill構築を追加し、「fillなし」のConstruction boundaryの段落を置き換える |
| §7.2.3 | stringifyingの例を`Text.toString`に置き換える |
| §8.4.4、§8.4.7、Appendix A | `Stringify`を`Utf8Format`に置き換える。特殊効果の記述を補間の写像に合わせる |
| §13.8 | Composition Rootに`$tryWrite`を追加する |
| §15.6.4、§22.1 | 型消去したWriterにも既存のLoan・呼び出し効果検査を適用する。§2.3.1の情報保持と保守的な検査を記載する |
| §17.4 | `Utf8Writer.write`に補間リテラルを直接渡した場合の警告を追加する。これは破棄に関する警告の優先順位とは独立である |
| §22.1、§22.1.1、SPEC.mdの宣言索引 | `Stringify`と、stringの「Stringify」操作を削除する。§2.1の配置で宣言を登録する。検証済み組み込み型のメタデータ、`writeLine`のオーバーロードと関数参照の曖昧さを記載する |
| §22.2.2、§22.4 | 「Moveされる」という注釈と`stringify`の記述を置き換える |
| §22.5.2 | §3.6の任意の縮小を追加する。アドレスと解放責任を維持し、失敗時は状態を変えず、長さ0では行わない |
| §22.5.4 | `KIMI_E_ARG_RANGE: Argument out of range`と`KIMI_E_FORMAT: Formatting failed`を追加する |
| §22.5.5、§22.5.6 | `Utf8Slice`版の`writeLine`の下降と外部シンボルを追加する |
| §21、Appendix A | §4.5の最適化共通規則、§6の性能・生成方針、§7の検証条件を接続する |
| Appendix E | `Stringify`の用語を除き、本案の型とContractを追加する |

Origin、一般の静的Contract、Copy、Owned、`@move`と`@uniq`の取得規則、`Kimi.Intrinsics.clone`は変更しない。一般の可変Slice、rawメモリの取得、Runtime Contract Viewの拡張、隠れた共有バッファ、グローバルプールは追加しない。
