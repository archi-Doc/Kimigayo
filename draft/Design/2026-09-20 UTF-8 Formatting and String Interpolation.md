# UTF-8書式化・文字列補間 — 仕様変更案（改訂版）

- 日付: 2026-09-20（2026-09-23 改訂）
- 状態: 設計案。例は提案中のAPIを使い、実装済みの機能や検証済みのABIを示さない。
- 優先順位: 本案で変更する事項はSPEC.mdと参照先より優先する。変更しない事項には既存仕様を適用する。
- 前提（例外は加えない）:
  - **取得**: 直接の所有Placeは、排他的に貸すとき`@uniq`、転送するとき`@move`を書く。借用値と排他参照を経由したPlaceは裸で再借用する（§10.2、§15.1.5）。
  - **Origin**: 借用注釈は後置`during`（§3.3.6、§15.3）、省略は§15.4に従う。名前付き型の`{name}`はbinding setの命名、型宣言の`{source}`はスキーマヘッダーである。
  - **Loan**: 引数で作った排他Loanは、結果の依存が`ref`しか要求しなくても格下げせずに保持する。Originの等式でLoanを作ったり解除したりしない。§15.6.4でこれを明確化する（§8）。
- 目的: 書式化を1回の評価と1回の書き込みで行い、借用とバッファ再利用で不要な確保・コピー・再検証をなくす。

## 1. 基本方針

1. 必須Contractの`Stringify`を削除し、`Utf8Format`に一本化する。フォールバックはない。`Stringify`は普通の識別子として使える。
2. 通常の補間は所有`string`を返す。Writerへ書き、失敗したら残りを省く補間は`$tryWrite`で書く。
3. `Console.writeLine`は`ref/string`と`Text.Utf8Slice`を受け取り、所有権を取得・保持・破棄しない。
4. 出力はUTF-8で、既定書式だけを提供する。桁揃え・精度・基数・ロケール・実行時書式文字列、数値の`@string`変換、文字列`+`の変更はない。
5. `BufferWriter`と`Utf8Format`はユーザーが実装できる静的Contractで、確保もboxingも必要としない。
6. 固定長領域の容量不足は`BufferFull`。伸長可能なWriterの確保失敗・サイズ上限超過は既存の確保用Abort。
7. 長さと容量はすべてバイト数の`isize`。
8. **堅牢性**: 安全なコードと、unsafe操作の既存の義務を守る実装では、書式化やWindowの規約違反だけで未初期化領域や不正なUTF-8の`string`/`Utf8Slice`を公開しない。ユーザー処理の副作用・終了性・Abortを制限する保証ではない。

## 2. 公開API

### 2.1. 配置と生成

Contractの署名に現れる名前は`Kimi`直下、それ以外は`Kimi.Text`に置く。既定aliasで`Text`は使えるが、内部は再帰的に開かない。

| 場所 | 名前 |
| --- | --- |
| `Kimi` | `Utf8Format`、`BufferWriter`、`WriteWindow`、`Utf8Writer`、`BufferFull` |
| `Kimi.Text` | `FixedBuffer`、`HeapBuffer`、`Utf8Slice`、`InvalidUtf8`、§2.4の関数 |

状態を持たないエラー型（`BufferFull`、`InvalidUtf8`）は引数なしの公開`init()`で作る。不変条件や借用を持つ型は`init`を公開せず、`Text`の関数か他の操作の結果としてだけ得る。

### 2.2. Contract

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>

contract Utf8Format
    func format(self: ref/Self, writer: uniq/Utf8Writer) -> Result<(), BufferFull>
```

- `reserve`の結果の`source`は省略規則（§15.4.3）で`self`になる。`format`の外側の借用と`Utf8Writer`の`target`は独立した入力Originで、整形式条件により`target`が外側の借用より長く生存する。
- 適合は既存の適合宣言と実装照合（§8.4.4–5）に従う。`BufferWriter`が保証する操作は`reserve`だけである。
- **`reserve`の効果上限**: 実装は`self`から与えられた権限だけを使い、外部から可変状態へのアクセス権限を新たに取得しない。
  - 禁止するのはアクセス経路である。呼び出し効果の要約（§15.6.4。呼び出し先・遅延初期化・破棄を含む）に可変の静的Fieldへのアクセスを含めず、効果が不明な呼び出しもしない。適合時に効果の照合（§8.4.5）で検査する。
  - `self`経由の借用によるアクセスは、参照先が静的記憶域でも通常のLoanで検査する（例は§3.3）。
  - これにより、型を消去した`reserve`呼び出し（§2.3.1）の効果が要件で固定され、`Utf8Writer.write`と各`format`の効果要約を公開できる。
- Runtime Contract Viewへの対応は追加しない。

### 2.3. 型

| 型 | 分類 | 所有・依存 |
| --- | --- | --- |
| `BufferFull` / `InvalidUtf8` | 通常の宣言 | 状態を持たないCopy |
| `HeapBuffer` | 通常の宣言 | Non-Copy。伸長可能なヒープ領域を所有する |
| `Utf8Slice {source}` | 通常の宣言 | Copy。検証済みUTF-8を共有借用する（非公開の`Slice<u8>`を保持、Loan要求`ref`）。参照カウント更新なし |
| `FixedBuffer {source}` | 検証済み組み込み型 | Non-Copy。呼び出し側の固定長バイト領域を排他借用する。Loan要求`uniq` |
| `WriteWindow {source}` | 検証済み組み込み型 | Non-Copy。書き込み領域と共通状態（§3.1）を排他借用する。Loan要求`uniq` |
| `Utf8Writer {target}` | 検証済み組み込み型 | Non-Copy。型を消去したWriterへの`uniq`借用と失敗状態を持つ。Loan要求`uniq` |

検証済み組み込み型（§15.3.5）は生ポインターで実装し、スロット・Loan要求・変性（`source`/`target`について共変）・Copy分類を§22.1の固定メタデータとして定める。Phantom Originは権限を与えないため、ソースから同等の型は作れない。

#### 2.3.1. `Utf8Writer`の表現と型消去

- **呼び出し**: 標準Writer（`HeapBuffer`、`FixedBuffer`）の`reserve`は直接呼び出し、ユーザーWriterだけ関数ポインターで呼ぶ。表現とインライン化の範囲は実装が決める（例: 容量確認と領域取得だけインライン化し、確保・伸長・コピーは共通の呼び出し先にまとめる）。
- **寿命**: `Text.writer`に渡す`uniq/W during target`の整形式条件（§15.6.1）により、`W`の依存は`target`以上に生存する。アダプターは`target`を超えて使えない。`W`のLoanは呼び出し側の値が保持し続け、消去されない。
- **アクセス**: `W`の値を保存・移動・公開せず、`reserve`だけを呼ぶ。
- **効果**: §2.2の上限で検査する。値ごとの効果追跡、実行時の寿命タグや借用検査はない。

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
| `fixed` | 確定済み長さ0、容量N。入力配列は初期化済みでなければならない |
| `heap` | 確定済み長さ0、容量は指定値以上。0なら未確保で開始する。負数は`KIMI_E_ARG_RANGE`、上限超過・確保失敗は既存の確保用Abort |
| `writer` | 失敗状態を持たないアダプター |
| `utf8` | 確保・コピー・再検証なしのビュー |
| `validateUtf8` | 全体を1回検証する。確保・コピーなし |
| `toString` | §5.1と同じ経路で所有`string`を作る。書式化の`BufferFull`は`KIMI_E_FORMAT`。**`string`複製の正式な入口**でもある |
| `tryFormat` | ローカルの`FixedBuffer`とアダプターで1回書式化し、`intoText()`でビューを返す。失敗時は`BufferFull`（途中のバイトは領域に残り得るが結果に含めない）。空のバッファにアダプターだけで書くので、`intoText()`は再検証なしで必ず成功する。長さ測定・自動再試行なし |

`tryFormat`の結果は`destination`の排他Loanを保持する。ビューを使う間、元配列はビュー経由でしか読めない。

```kimi
var small: [3 of u8] = [3 of 0]
let result = Text.tryFormat(123, small@uniq)  // Ok: 長さ3。ヒープ確保なし
// let b = small[0]                           // エラー: resultの使用が続くため排他Loanが有効
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

- `[Length of value]`は期待型の有無にかかわらず**常に固定配列**を構築する。`Array<T>`を期待する位置では型不一致のエラーで、変換はしない。
- `Length`は§4.2の長さ文法に従う。`of`はこの位置でも文脈キーワードである。
- 要素型`T`は`value`の型でCopyでなければならない。型は既存の期待型・リテラル規則で決める。
- `value`は`N = 0`でも1回だけ評価・取得し（裸のPlaceはCopy）、N回Copyする。
- fillの省略は§4.5に従う。生成関数による構築や未初期化領域の借用は追加しない。

### 2.6. 標準バッファの操作

`FixedBuffer`と`HeapBuffer`は`BufferWriter`に適合し、次の操作を持つ。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `length` / `capacity` | getter、`ref/Self` | 確定済みバイト数 / 全容量 |
| `bytes()` | `ref/Self` | 確定済み部分の`Slice<u8>` |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`。未検証の末尾を検証するが記録しない |
| `validate()` | `uniq/Self` | `Result<(), InvalidUtf8>`。検証成功を記録する |
| `clear()` | `uniq/Self` | 確定済み長さと検証済み長さを0に戻し、容量は保持する。消去は保証しない |
| `reserve(minimum)` | `uniq/Self` | §3.1のWindow |
| `intoText()`（`FixedBuffer`のみ） | `owner/Self` | `Result<Utf8Slice{r}, InvalidUtf8>`、`origin r.source == self.source`。未検証の末尾だけ検証し、元領域の確定済み部分のビューを返す。成否ともにバッファを消費し、元配列は解放しない |
| `intoString()`（`HeapBuffer`のみ） | `owner/Self` | `Result<string, InvalidUtf8>`。未検証の末尾だけ検証し、ポインター・長さ・解放責任を`string`へ移す（縮小は§3.6）。未確保で空ならStatic、確保済みなら長さ0でも元ポインターを保持する。検証失敗時はバッファを通常破棄する |

- 書き方: `buffer.text()`、`buffer@uniq.validate()`、`buffer@move.intoText()`。
- `bytes()`と`text()`の結果は受け手の借用に依存する。`intoText()`の結果は元配列の排他Loanを引き継ぎ、ローカルのバッファには依存しない。
- 共有Slice・Window・アダプターのLoanと競合する操作は拒否する。一般の可変Sliceは導入しない。

### 2.7. `Utf8Slice`

| 操作 | 動作 |
| --- | --- |
| `length` | getter、`self: Self`。バイト数 |
| `bytes(self: Self) -> Slice<u8>{s}`、`origin s.source == self.source` | 元領域の借用を保ったままバイト列を返す |

有効なUnicode scalarのUTF-8だけを受理し、不完全な列・過長符号化・surrogate・範囲外の値を拒否する。置換や正規化はしない。NULはデータとして扱う。バイト単位で任意に切り出した部分を有効なUTF-8とみなしてはならない。

### 2.8. `Utf8Writer`

| 操作 | 動作 |
| --- | --- |
| `write<T>(self: uniq/Self, value: ref/T) -> Result<(), BufferFull>`、`T is Utf8Format` | §4.2の手順で1回書く |
| `status(self: ref/Self) -> Result<(), BufferFull>` | 失敗状態を返す |

- `string`・`Utf8Slice`・`char`も`Utf8Format`に適合するので、書き込み操作は`write`だけで足りる。
- 書き方: ローカルのアダプターは`writer@uniq.write(x)`、引数で受け取ったものは`writer.write(x)`。値は共有借用で渡り、元のPlaceを転送しない。
- 状態のリセット、rawなWindow、内部Writerへのアクセス、所有権の取り出しは公開しない。
- 通常のメソッドは直接呼び出しでも関数値経由でも通常どおり引数を評価する。短絡は`$tryWrite`（§5.2）だけである。

## 3. バッファの安全性と容量管理

### 3.1. 共通状態と予約

共通状態は領域のアドレス・容量・確定済み長さ・検証済み長さを持つ。

- 不変条件: `0 <= 検証済み長さ <= 確定済み長さ <= 容量 <= MaxObjectSize`。確定済み部分は初期化済みで、検証済み長さまでは有効なUTF-8である。
- 公開するSliceは確定済み部分に限る。Windowは未確定の末尾だけに先頭から連続して書く。
- Windowの生存中は、領域と共通状態を排他Loanで保護し、移動・再予約・伸長・破棄を許さない。

**`reserve(minimum)`**:
- 成功結果は`written == 0`かつ`remaining >= minimum`で、確定済み長さを変えない。
- `minimum == 0`では空のWindowを返してよく、標準Writerはこれだけを理由に確保しない。負数は`KIMI_E_ARG_RANGE`。
- `FixedBuffer`は空きが足りれば成功し、足りなければ`BufferFull`。`HeapBuffer`は下記の規則で伸長し、確保失敗を`BufferFull`に置き換えない。
- どのWriterも`BufferFull`のときは確定済みの内容と長さを変えない。

**`HeapBuffer`の伸長**:
1. `minimum > MaxObjectSize - length`なら`KIMI_E_ALLOC_SIZE`。それ以外は`required = length + minimum`。
2. `required <= capacity`なら伸長しない。`hint`だけを理由に伸長しない。
3. 伸長時の目標は`target = required + hint`（`hint`は内部アダプターの状態、§4.4）。`hint`が表現できないか`hint > MaxObjectSize - required`なら`hint`を捨てて`target = required`。新しい容量は`capacity <= MaxObjectSize / 2`なら`max(target, 2 * capacity)`、それ以外は`target`。コピーは確定済み部分だけである。

### 3.2. Window

Windowは予約開始位置・書き込み済み長さ・容量上限を持つ小さな値で、確保を行わない。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `written` / `remaining` | getter、`ref/Self` | 今回書いた長さ / 残り容量 |
| `push(byte: u8)` | `uniq/Self` | `Result<(), BufferFull>`。末尾に1バイト書く |
| `append(bytes: Slice<u8>)` | `uniq/Self` | `Result<(), BufferFull>`。全バイトを末尾へコピーする |
| `limit(maximum: isize)` | `owner/Self` | `Self`。消費し、容量を`min(現在の容量, maximum)`に制限して返す。`maximum < written`は`KIMI_E_ARG_RANGE` |
| `commit()` | `owner/Self` | `isize`。書き込み済み部分を確定し、そのバイト数を返す。コールバックは呼ばない |

- 書き方: `window@uniq.push(b)`、`window@move.limit(n)`、`window@move.commit()`。
- `push`と`append`は全体が収まる場合だけ書き、収まらなければ何も変えない。`limit`は拡大・複製・確定をしない。
- 消費済みのWindowは使えない。未確定のまま破棄したWindowは何も確定しない。
- 未初期化領域の読み取り、穴を空ける書き込み、任意長を確定する`advance(n)`はない。

### 3.3. ユーザー定義Writer

生ポインターからWindowを作る公開APIはない。ユーザーWriterは標準Writerかその借用を保持し、そのWindowを転送するか`limit`で制限して返す。

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

struct LogWriter {source}
    Self is BufferWriter
    let target: uniq/Text.HeapBuffer during source    // 参照先は静的記憶域でもよい

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        return self.target.reserve(minimum)           // self経由なのでLoanで検査される
// reserveの中で静的Fieldの名前から可変の記憶域へアクセスすると、効果上限（§2.2）違反で適合エラー
```

`Limited`は`maximum`を不変にし、構築時に非負を検査するので、`0 <= inner.length <= maximum`が成り立ち減算はオーバーフローしない。出力の順序・内容と容量の約束はユーザー実装の責任である（違反時の保証は§1.8、§3.4）。

### 3.4. Window規約の検査とUTF-8の検証状態

**原則: 有効なUTF-8と確認できた連続した先頭部分だけを、排他権限で検証済みにする。** 確認方法は、アダプターによる生成と、既存バイト列の検証の2つである。

アダプターは`reserve`の結果ごとに`written == 0`かつ`remaining >= minimum`をO(1)で検査する。満たさなければWindowを確定せずに破棄し、`BufferFull`として失敗状態にする。これにより、ユーザーが先に書いた未確認のバイトは確定されない。標準Writerでは常に満たすため、この分岐は省略できる。

| 操作 | 検証済み長さ |
| --- | --- |
| rawな`commit()` | 変えない |
| アダプターの確定 | 予約時に`検証済み長さ == 確定済み長さ`だったときだけ、新しい確定済み長さまで進める |
| `text()` | 未検証の末尾を検証するが記録しない |
| `validate()` | 未検証の末尾が有効なら確定済み長さまで進める。失敗時は何も変えない |
| `intoText()` / `intoString()` | 同じ検証をし、成功時だけ結果を返す |
| `clear()` | 0に戻す |

空または検証済みのバッファにアダプターだけで追記すれば、検証は常に0バイトで済む。raw書き込みの後で繰り返し読む場合は、先に`validate()`で記録する。`validate()`はアダプターの失敗状態を変えない。

```kimi
try buffer@uniq.validate()      // 未検証の末尾を1回だけ検証する
let first = try buffer.text()   // 再検証なし
let second = try buffer.text()  // 再検証なし
```

### 3.5. 破棄と借用の終了

- `FixedBuffer`・`WriteWindow`・`Utf8Writer`は`deinit`を持たず、破棄時に借用先を観測しない。未確定Windowの破棄は長さの更新・出力・通知をしない。
- したがって借用は最終使用の後で終わり、元のWriterを再利用できる。後続の使用や`defer`があればそれまでLoanが続く。一般の`deinit`規則は緩めない。
- `HeapBuffer`は所有する領域を通常の破棄で1回解放する。`FixedBuffer`は元配列を解放しない。

### 3.6. その場での縮小

`intoString()`は、検証成功後、解放責任を移す前に、余剰容量の縮小を試みてよい（任意の最適化）。

- 余剰が実装定義の閾値（例: 容量の1/4以上かつ64バイト以上）を超えるときだけ試みる。長さ0では試みない。
- 成功時もアドレス・内容・唯一の解放責任を保ち、容量は長さ以上とする。追加確保・移動・コピーはしない。
- 失敗・非対応なら何も変えず、元の領域で成功させる。Abort・`InvalidUtf8`・再試行にしない。
- 縮小後のポインターも同じアロケーターで1回だけ解放できなければならない。

## 4. 書式化の共通規則

### 4.1. 表現と評価

- `format`は値を共有借用し、消費せず、出力先や入力への借用を結果に残さない。成功時は完全な表現を出力する。
- **同じ値・同じ外部状態からの成功出力は、出力先の型や容量で変わってはならない。** 容量不足で省略形を成功として返さず`BufferFull`を返す。これはユーザー実装の法則であり、コンパイラはこれを最適化の根拠にしない。
- 純粋性は要求しない（副作用・独自の確保は可）。`format`は1回だけ呼び、長さ測定や容量拡張のための再実行はしない。
- **組み込み型の参照動作**: 状態を確認し、正確なバイト長を求め、その長さで1回予約し、一括追記して確定する。空出力は予約しない。例えば`123`は3バイトで予約するので、ちょうど3バイトの領域で成功する。
- アダプターが確定する各区間は有効なUTF-8で、不完全な文字を確定しない。失敗までに確定した区間は残る。

### 4.2. 失敗状態と`write`の手順

`write`の手順（補間、`$tryWrite`、`toString`、`tryFormat`も共通）:

1. 失敗状態なら、内部の予約・書式化を呼ばずに同じ失敗を返す（空出力も同じ）。
2. 選択された`format`を1回呼ぶ（組み込み型は§4.1の参照動作）。
3. 失敗が返った場合、または呼び出し中に失敗状態になった場合は、最初の`BufferFull`を保持して返す。書き込みエラーを無視して成功を返しても成功扱いしない。独自に返した`BufferFull`も保持する。

- 失敗状態は、ユーザーの`format`がエラーを握りつぶしても成功扱いしないための安全網である。基本の書き方は`try`か`$tryWrite`による伝播で、結果を捨てればResult破棄警告（§17.4）が出る。
- `format`を直接呼ぶのは通常の関数呼び出しで、状態確認を含まない。
- rawなWindowの`BufferFull`はアダプターの状態に影響しない。
- 失敗状態は暗黙にリセットしない。確定済み出力や副作用の巻き戻しは保証しない。再開は新しいアダプターで行う。

```kimi
buffer@uniq.clear()                   // 必要な場合のみ
var retry = Text.writer(buffer@uniq)  // 最初のアダプターの最終使用より後に作る
```

### 4.3. 既定書式

| 値 | 表記 |
| --- | --- |
| 整数 | ASCIIの10進数。負数だけに`-`、不要な先頭ゼロなし。最小負数も扱う |
| `bool` | `true` / `false` |
| `char` | そのUnicode scalarのUTF-8 |
| Unit | `()` |
| `string` / `Utf8Slice` | 内容をそのまま。NULも含めバイト長で扱う |
| 浮動小数点 | 下記 |

**浮動小数点**:
- 有限・非ゼロの値は、対象型へ最近接・偶数丸めで戻すと同じ値になる10進表現のうち、有効桁数が最少のものを選ぶ。同桁数なら正確な値に最も近いもの、同距離なら末尾の有効数字が偶数のもの。`f32`は`f32`の値として選ぶ。
- 正規化した10進指数`e`が`-4 <= e < 16`なら固定小数点、それ以外は指数表記。不要な末尾ゼロ・小数点を出さず、指数は小文字`e`、正指数の`+`と先頭ゼロを省く。
- 特殊値は`0` / `-0`、`Infinity` / `-Infinity`、`NaN`（符号・payloadによらない）。小数点は`.`で、桁区切り・ロケール依存はない。

**適合しないもの**:
- 借用型から参照先への適合の委譲はない。借用値は§5.1の引数適合により参照先の型で書式化される。
- オブジェクトハンドル・オブジェクト借用・ポインターは対象外。payloadは明示の射影（§13.5.5.1）で渡し、診断でその書き方を提案する。
- Tuple・配列・ユーザー型の自動適合はない。

### 4.4. 容量の見積もりと内部アダプター

| 型 | 最大バイト長 |
| --- | --- |
| `i8` / `u8` | 4 / 3 |
| `i16` / `u16` | 6 / 5 |
| `i32` / `u32` | 11 / 10 |
| `i64` / `u64` | 20 / 20 |
| `i128` / `u128` | 40 / 39 |
| `isize` / `usize` | ポインター幅の整数型と同じ |
| `bool` / `char` / Unit | 5 / 4 / 2 |
| `f32` / `f64` | 17 / 24 |

`string`・`Utf8Slice`・ユーザー型は**無界**とする。上限の加算はコンパイル時に行い、`MaxObjectSize`を超えたら見積もりを放棄する（Abortしない）。

補間と`Text.toString`は、コンパイラが作る**内部アダプター**で内部`HeapBuffer`へ書く。長さを知るために式を先に評価したり、ユーザー書式化を実行したりはしない。

- **確保**: 有界な補間（すべての値に上限があり、リテラルとの合計が`MaxObjectSize`以下）は合計を1回確保し、伸長しない。それ以外は未確保で開始する。
- **`hint`**: 補間・`Text.toString`の下降だけが、値を書く前ごとに設定する。値は後続のリテラル長と有界値の上限の合計で、無界値は0、当の値自身は含めない。
- **保留リテラル**: §4.5の条件で、リテラルの書き込みと確保を次の予約まで遅らせてよい。保留分は次の予約の実必要長に含めて先頭に書き、後続が空出力で予約しなくても成功完了までに確定する。論理上の出力長には直ちに加え、上限検査を後続の式の評価より後へ移さない。
- 実必要長の超過はAbort、`hint`の超過は`hint`だけを捨てる（§3.1）。式・書式化の再実行や確保の再試行はしない。
- **適用範囲はアダプターの生成元で決める**: 内部アダプターへの予約は、構文によらず（ユーザーの`format`内の`write`や`$tryWrite`を含む）現在の`hint`を使い、保留リテラルを先に書く。入れ子の`$tryWrite`は`hint`を変えず、短絡だけを担う。`Text.writer`で作ったアダプターには`hint`も保留もなく、予約の引数と回数を保つ。どちらの状態も`format`からは観測できない。

例: 上限をMとし、長さM−5の文字列の後に数値`0`を書く場合、最大長20を加えると上限を超えるが、`hint`だけを捨てて書き込みを続ける。

### 4.5. 最適化で保存する動作

対象: 直接符号化、予約の統合、確保の遅延、スタック配置（§6.2）、fillの省略。予約の統合と直接符号化は、書き込み先が標準Writerに確定する場合に限る。

| 項目 | 規則 |
| --- | --- |
| 型・借用 | 受理するプログラム、選択した実装、Loan、Origin、一時値の寿命を変えない |
| 評価・副作用 | 式と書式化の評価順・回数、ユーザーの副作用、破棄順序、制御移動を保つ |
| 意味上の失敗 | `BufferFull`、明示的なAbort、引数検査、実必要長の上限検査と、それらより前の副作用との順序を保つ |
| 内部の資源失敗 | 補間・`Text.toString`の非公開な一時バッファの確保・解放について、省略・統合・遅延による資源失敗の有無や時点の差は観測対象外 |
| 外部の操作 | ユーザーWriterの予約、ユーザーコードの確保・解放、OSへの出力には、内部資源失敗の例外を適用しない |
| fillの省略 | 配列の生存期間全体で`FixedBuffer`経由以外の読み取りがないとき、fill構築の書き込みを省略してよい |

実際に行った確保・解放の失敗は既存のAbortと診断位置の規則に従う。例えば`"prefix\(sideEffect())"`の確保を遅らせると、確保失敗より先に`sideEffect()`が実行され得る。この資源失敗との順序差は許すが、明示的なAbortや長さ上限の検査を越えて`sideEffect()`を実行してはならない。

## 5. 補間

### 5.1. 通常の補間

```kimi
let message = "My number is \(self.number)"
```

- 構文・エスケープ・入れ子は§2.9に従い、結果は所有`string`である。
- 各埋め込み式は`write<T>(value: ref/T)`の引数として§10.2で適合させ、決まった`T`に`Utf8Format`への適合を静的に要求する。

| 埋め込み式 | 適合 | `T` |
| --- | --- | --- |
| 所有Place（Copy型を含む）・所有一時値 | 共有借用 | 式の型 |
| `ref/U` | Exact | `U` |
| `uniq/U`・排他参照を経由したPlace | 共有Reborrow | `U` |

- 裸の式は元のPlaceを転送しない。`\(x@move)`は転送した一時値を共有借用する。リテラルは通常の既定型で決まり、`string`を期待型にしない。
- **下降**:
  1. §4.4の内部アダプターを作る。
  2. 左から書く。各式は1回だけ評価し、書き終えてから次の式へ進む（内部の遅延・統合は§4.4–5に限る）。
  3. 最初の失敗で`KIMI_E_FORMAT: Formatting failed`のAbort。成功ならアダプターの使用を終え、`intoString()`で所有`string`にする。
- 値の借用は`write`の終了で終わり、一時値の破棄境界は既存規則のまま。
- 内部バッファはアダプター以外から書かれないので`検証済み長さ == 確定済み長さ`が保たれ、`intoString()`は再検証なしで成功する。生成コードは失敗分岐を持たない。

### 5.2. 短絡する`$tryWrite`

```kimi
$tryWrite(writer@uniq, "(\(point.x), \(point.y))")
```

- Composition Rootの操作で、関数値にはならない。結果は`Result<(), BufferFull>`で、全体の`string`は作らない。
- **第1項**: `uniq/Utf8Writer`の引数位置として通常の規則で取得する。裸の直接所有Placeはエラーとし、`writer@uniq`を提示する。
- **第2項**: 文字列リテラルの構文に限る（補間なしの文字列やraw文字列も可）。任意の文字列値には`write`を使う。
- **評価**: 第1項を1回評価して借用を開始し、左からリテラルと値を同じアダプターへ書く（型付けは§5.1と同じ）。最初の失敗で終了し、以降の式は評価しない。開始時に失敗状態なら式を1つも評価しない。評価されない式にも型・適合・制御移動先の検査を行う。未確定のWindowは破棄し、確定済み出力は残す。
- **境界**: 元の式の一時値境界と`return`・`exit`・`yield`の対象を変えない。借用は埋め込み式の評価前から有効なので、埋め込み式から同じアダプターやその借用元は読めない。呼び出しではないので呼び出し予約（§15.6.7）は適用しない。
- `writer@uniq.write("…\(v)…")`は通常の補間を先に完了してから書く。補間を含む文字列リテラルが`Utf8Writer.write`の引数に直接現れた場合は§17.4の警告で`$tryWrite`を提案する（意味は変えない）。

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

- `string`の値には`ref/string`の候補だけが、`Utf8Slice`の値には`Utf8Slice`の候補がExactで適用される。暗黙変換はないので競合しない。期待型のない関数参照`Console.writeLine`は既存規則で曖昧になる。
- どちらもUTF-8全体とLFを出力してUnitを返し、`WriteStdout(data, length)`を直接呼ぶ。stringハンドルの実体化や解放責任の移動はしない。出力失敗・部分出力・flushは§22.4に従う。

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]   // FixedBuffer経由でしか読まないのでfillは省略できる
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    match buffer.text()                   // writerの借用はここで終わっている
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => $abort("unexpected invalid UTF-8")
    return .Ok(())
```

### 6.2. 有界な補間の最適化

`Console.writeLine`の引数に補間リテラルが直接現れ、有界な補間（§4.4）で、見積もりの合計が実装定義のスタック上限以下なら、実装は§6.1と等価なスタック上の経路へ下降してよい（§4.5に従う）。

- 固定領域の長さは見積もりの合計とする。fill構築と`text()`の再検証は不要である。
- 書き込みは容量不足にならないので、ヒープ経路への切り替えや再実行は起きない。埋め込み式自体のAbortや制御移動は通常どおり扱う。
- 補間全体が成功してから出力する。書式化が失敗したら本文を出力しない。出力開始後のOSエラーによる部分出力は§6.1に従う。
- 条件を満たさない補間は通常のヒープ経路を使う。

### 6.3. 性能の合格条件

| 経路 | 要求 |
| --- | --- |
| Window・ビュー・アダプターの管理 | 追加のヒープ確保・管理用コールバック・参照カウント更新なし |
| アダプターの`reserve` | 標準Writerへは`format`本体が非総称でも間接呼び出し0回。ユーザーWriterへは予約ごとに1回 |
| 組み込み型＋`FixedBuffer` | 収まればヒープ確保0回。中間string・boxing・引数配列なし |
| 組み込み型＋`HeapBuffer` | 容量内なら追加確保0回。`clear()`で容量を再利用できる |
| `検証済み長さ == 確定済み長さ`での`text()`・`validate()`・`intoText()`・`intoString()` | 追加の検証0バイト、追加の確保・コピーなし |
| 有界な補間の所有結果 | ヒープ確保1回以下、完成時のコピーなし |
| 無界の値が`string`/`Utf8Slice`の1つだけで、その前がリテラルだけの補間 | ヒープ確保1回以下（空結果は0回）。`hint`を捨てた場合は上限を適用しない |
| `Text.toString(s)`（`s: string`） | 空なら確保0回（Static）、非空ならバイト長ちょうどの確保1回 |
| `Console.writeLine("My number is \(n)")`、`n: i64` | 最適化有効時は33バイト（リテラル13＋数値20）のスタック領域を使い、ヒープ確保0回 |
| コード量 | Writer型の違いだけでは`format`の別インスタンスを要求しない |

最初のプロファイルはmonomorphization（§21.3.1）である。組み込み型の書式化は直接呼び出しかインライン化で行う。§4.5を満たすインライン化や自動特殊化による複製は許可する。任意長の所有結果、容量を超える伸長、ユーザー実装・OS内部の確保には確保ゼロを保証しない。

## 7. 検証

ネイティブ実行・確保カウンター・生成コードで次を確認する。

- **表記と評価**
  - 整数・浮動小数点の境界値と最大長、UTF-8検証、NUL、空出力。
  - 各式と`format`が1回だけ実行され、出力先で成功結果が変わらないこと。
  - ちょうど収まる領域の成功、1バイト不足の失敗、`limit`。
  - 無視された書き込みエラーの検出、独自の`BufferFull`が`KIMI_E_FORMAT`になること、`$tryWrite`だけが短絡すること、直接呼び出しと関数値経由の一致。
- **容量と内部アダプター**
  - 実必要長の超過が`KIMI_E_ALLOC_SIZE`、見積もりの超過では`hint`だけを捨てること。
  - `required <= capacity`では`hint`があっても伸長しないこと。伸長計算の境界、空の予約。
  - ユーザーの`format`内の複数予約（入れ子の`$tryWrite`を含む）に同じ`hint`を使い、保留リテラルが順序どおり書かれ、空の埋め込み値でも欠落しないこと。`Text.writer`で作ったアダプターでは予約の引数と回数が変わらないこと。
  - 遅延・統合の前後で、明示的なAbort・上限検査・副作用・破棄・制御移動の順序が保たれ、内部資源失敗との差だけがあること。§6.3の確保回数。
  - スタック上限を超える有界な補間がヒープ経路になること。
- **Windowと検証状態**
  - 二重確定・消費後の使用・競合する予約・伸長・aliasの拒否。未確定破棄で長さが変わらないこと。
  - 規約違反のWindow（`written != 0`、`remaining < minimum`）が`BufferFull`になり、不正なUTF-8を確定しないこと。
  - raw書き込み後の`text()`が記録せず`validate()`は成功時だけ記録すること、未検証部分を飛び越えないこと、`clear()`後の再利用、失敗状態がリセットされないこと。
- **借用・寿命・効果**
  - `intoText()`と`tryFormat`の結果が元配列の排他Loanを保持し、競合する直接の読み取り・書き換えと、元領域より長い寿命への脱出を拒否すること。呼び出し元の配列に依存するビューの返却は許可すること。
  - 管理型の破棄が借用先を観測せず、最終使用後に再利用できること。`defer`や後続の使用ではLoanが続くこと。
  - Originの明示形と省略形の一致。型消去後も`target`を超えて使えず、`W`のLoanが保持されること。
  - 呼び出し先・遅延初期化・破棄を含めて可変の静的Fieldへアクセスする、または効果不明の呼び出しをする`reserve`実装が適合エラーになること。`self`経由で静的記憶域の借用を使うWriterは、通常のLoan検査で受理・拒否されること。
- **取得と構文**
  - `@uniq`・`@move`が必要な位置での裸の直接所有Placeの診断、借用値の裸の再借用、埋め込み式が転送しないこと。
  - fill構築: `N = 0`でも1回評価、`Array`の期待型でエラー、複合した長さ式に括弧が必要。
- **出力と記憶域**
  - `writeLine`後も入力を使えること。書式化失敗では本文を出力せず、出力開始後のOSエラーでは部分出力を許すこと。
  - `intoString()`と途中離脱で二重解放・解放漏れがなく、スタック領域が脱出しないこと。
  - 縮小: 閾値未満・長さ0では試みず、成功・失敗・非対応で内容と解放責任を保ち、Abort・追加確保・再試行がないこと。
  - Writer型を増やしても通常の`format`本体のインスタンス数が増えないこと（インライン化・自動特殊化による複製は分けて測る）。標準Writerのインライン化の範囲はコードサイズと実行時間で判断する。§6.3の性能条件。

## 8. 正式仕様への適用範囲

本案の編集ではSPEC.mdと参照先を変更しない。統合時に次を反映する。

| 箇所 | 変更内容 |
| --- | --- |
| §2.5.1、Appendix F | `of`の文脈にfill構築を加え、`[Length of value]`と`$tryWrite`の構文を追加する |
| §2.9.1、§12.2、§12.3.3 | 補間を`Utf8Format`と`write`の引数適合に変更し、借用値の適合委譲を削除する。`$tryWrite`の評価順と短絡を追加する |
| §4.3 | fill構築を追加し、「fillなし」のConstruction boundaryの段落を置き換える |
| §7.2.3 | stringifyingの例を`Text.toString`に置き換える |
| §8.4.4、§8.4.7、Appendix A | `Stringify`を`Utf8Format`に置き換え、特殊効果の記述を補間の写像に合わせる |
| §8.4.5、§22.1 | `BufferWriter.reserve`の効果上限（§2.2）と、型消去した呼び出しをその上限で検査することを記載する |
| §13.8 | Composition Rootに`$tryWrite`を追加する |
| §15.6.4 | 引数で作った排他Loanは、結果の依存が`ref`だけを要求しても格下げせず保持することを明確化する |
| §17.4 | `Utf8Writer.write`への補間リテラル直接渡しの警告を追加する（破棄警告の優先順位とは独立） |
| §22.1、§22.1.1、SPEC.mdの宣言索引 | `Stringify`とstringの「Stringify」操作を削除し、§2.1の配置で宣言を登録する。検証済み組み込み型のメタデータ、`writeLine`のオーバーロードと関数参照の曖昧さを記載する |
| §22.2.2、§22.4 | 「Moveされる」注釈と`stringify`の記述を置き換える |
| §22.5.2 | §3.6の任意の縮小を追加する |
| §22.5.4 | `KIMI_E_ARG_RANGE: Argument out of range`と`KIMI_E_FORMAT: Formatting failed`を追加する |
| §22.5.5、§22.5.6 | `Utf8Slice`版`writeLine`の下降と外部シンボルを追加する |
| §21、Appendix A | §4.5の最適化規則、§6の性能・生成方針、§7の検証条件を接続する |
| Appendix E | `Stringify`の用語を除き、本案の型とContractを追加する |

上記以外のOrigin、一般の静的Contract、Copy、Owned、`@move`と`@uniq`の取得規則、`Kimi.Intrinsics.clone`は変更しない。一般の可変Slice、rawメモリ取得、Runtime Contract Viewの拡張、隠れた共有バッファ、グローバルプールは追加しない。
