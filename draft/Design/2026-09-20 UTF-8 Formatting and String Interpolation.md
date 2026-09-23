# UTF-8書式化・文字列補間 — 仕様変更案

- 日付: 2026-09-20（2026-09-22 最終版、2026-09-23 明示的な転送・排他借用と Borrow Origin Suffix に適合）
- 状態: 設計案。例は提案APIを使用し、実装済み機能や検証済みABIを示さない。
- 優先順位: 本案で変更する事項は[SPEC.md](../../SPEC.md)およびその参照先より優先する。変更しない事項には既存仕様を適用する。借用Originの表記には[2026-09-23 Borrow Origin Suffix](../Changes/2026-09-23%20Borrow%20Origin%20Suffix.md)を適用し、それ以外の設計案の未統合機能を前提としない。
- 取得: 転送・借用の綴りは現行仕様（§3.1の転送`@move`、§10.2の引数適合、§13.5の明示操作）に従う。裸の式は所有Placeから転送も排他貸与も開始しない。直接の所有Placeを`uniq/T`へ渡す点には`@uniq`、所有受け手（`owner/Self`）の操作には`@move`を書き、借用値と排他参照経由のPlaceは裸で再借用する。本案は取得規則に例外を加えない。
- Origin: 借用注釈は Borrow Origin Suffix 案の後置`during`を使い、省略・互換性は[§15.3–4](../../spec/15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations)に従う。署名は直接借用入力からの結果省略（§15.4.3）を使い、直接借用の明示注釈は`ref/T during source`、名前付き型のスロット間の関係は`origin`節で書く。`Type{name}`は常にbinding setの命名であり、Originの適用ではない。型宣言の`{source}`はスキーマヘッダーであり、`during`へ置き換えない。
- 目的: 書式化を1回の評価と書き込みで実行し、借用とバッファ再利用によって不要な確保・コピー・再検証を避ける。

## 1. 基本方針

1. 必須Contractの`Stringify`を削除し、`Utf8Format`に一本化する。旧Contractや同名メソッドへのフォールバックは行わない。`Stringify`というユーザー識別子は引き続き使用できるが、特別な効果は持たない。
2. 通常の補間式は所有`string`を返す。Writerへ短絡して書く補間は明示的な構文`$tryWrite`で表す。
3. `Console.writeLine`は既存の`ref/string`に加えて`Utf8Slice`を受け取り、いずれも所有権を取得・保持・破棄しない。
4. 書式化先はUTF-8。初期版は既定書式だけを提供し、桁揃え・精度・基数・ロケール・実行時書式文字列は追加しない。数値の`@string`変換や文字列`+`の動作も追加しない。
5. `BufferWriter`と`Utf8Format`はユーザー実装可能な静的Contractとする。型消去やboxingを必要としない。
6. 固定長領域の容量不足は`BufferFull`。伸長可能なWriterの確保失敗・サイズ上限超過は既存の確保用Abortに従う。
7. 本案の長さ・容量はすべてバイト数の`isize`である。

## 2. 公開API

### 2.1. Contractと型

以下を`Kimi`直下の必須宣言とする。

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>

contract Utf8Format
    func format<W>(self: ref/Self, writer: uniq/Utf8Writer<W>) -> Result<(), BufferFull>
        W is BufferWriter
```

| 型 | 所有権・依存関係 |
| --- | --- |
| `BufferFull` / `InvalidUtf8` | 状態を持たないCopyのエラー型。引数なしの公開コンストラクターを持つ |
| `WriteWindow {source}` | Non-Copy。書き込み領域と共通状態（§3.1）を排他的に借用する。`source`のLoan要求は`uniq` |
| `FixedBuffer {source}` | Non-Copy。呼び出し側の固定長バイト領域を排他的に借用する。`source`のLoan要求は`uniq` |
| `HeapBuffer` | Non-Copy。伸長可能なヒープ領域を所有する |
| `Utf8Slice {source}` | Copy。検証済みUTF-8領域を共有借用する。参照カウント更新なし。`source`のLoan要求は`ref` |
| `Utf8Writer<W> {target}` | Non-Copy。`W is BufferWriter`。`uniq/W during target`と失敗状態を保持する。`target`のLoan要求は`uniq` |

`reserve`の直接借用入力は受け手だけなので、結果の`source`は省略規則で`self`に補完される。`Utf8Writer<W>`を入力にとる関数では、外側の借用と省略された`target`が独立した入力Originになり、`target`が外側の借用を包含する整形式条件を保持する。適合実装との互換性は正規化されたOrigin契約で判定し、明示・省略の表記だけで区別しない。

`Utf8Writer`は借用専用で、Writer本体を所有せず、独自のヒープ領域を持たない。ユーザーの適合は既存の明示的な適合宣言・公開実装・制約検証（§8.4.4–5）に従う。`BufferWriter`が保証する操作は`reserve`だけで、総称アダプターはContract外のメンバーを呼ばない。`Utf8Format.format`は関数総称パラメーターを持つ静的要件であり、Runtime Contract Viewへの対応は追加しない。

### 2.2. `Kimi.Text`の関数

通常の公開group `Kimi.Text`に次のType関数を置く。既定のKimi aliasは`Text`を使用可能にするが、その内部を再帰的には開かない。型引数・長さ引数は既存規則で推論できる。

```kimi
group Text
    public func fixed<length N>(destination: uniq/[N of u8]) -> FixedBuffer
    public func heap(capacity: isize) -> HeapBuffer
    public func writer<W>(destination: uniq/W) -> Utf8Writer<W>
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
| `fixed` | 確定済み長さ0、容量Nの`FixedBuffer`。入力配列は初期化済みでなければならない |
| `heap` | 確定済み長さ0、容量は指定値以上の`HeapBuffer`。0なら未確保で開始する。負数は`KIMI_E_ARG_RANGE`、上限超過・確保失敗は既存の確保用Abort |
| `writer` | 失敗状態を持たない`Utf8Writer<W>` |
| `utf8` | 確保・コピー・再検証なしのビュー |
| `validateUtf8` | 全体を1回検証。確保・コピーなし |
| `toString` | §5.1の経路で所有`string`へ書式化する。`string`を渡せば独立した所有文字列を得られるが、複製の正式な入口は`Kimi.Intrinsics.clone`の`(value: ref/string) -> string`オーバーロードとする |
| `tryFormat` | 固定長Writerと借用アダプターをローカルに保持し、`writeValue`（§4.2）で1回書式化する。成功時は書き込んだ範囲の検証済みビューを返し、再検証しない。失敗時は途中のバイトが領域に残り得るが、結果には含めない。ゼロ長領域と空出力も許可する。別のContract、長さ測定、自動再試行は設けない |

`uniq/...`を受け取る引数は通常の取得規則に従い、直接の所有Placeからは`@uniq`で排他的に貸す。所有一時値の排他借用は明示の`@uniq`だけで可能だが、一時値は外側の式の終了で破棄されるため、文をまたいで使う借用元は先にローカル変数へ保持する。本案は暗黙の排他借用を追加しない。

```kimi
var scratch: [64 of u8] = [64 of 0]
var buffer = Text.fixed(scratch@uniq)
var writer = Text.writer(buffer@uniq)
let result = $tryWrite(writer@uniq, "\(123)")
// writerの借用が終了すると、buffer.text()などを使用できる。

var small: [3 of u8] = [3 of 0]
let view = Text.tryFormat(123, small@uniq) // 成功: 長さ3のUtf8Slice。ヒープ確保なし
```

### 2.3. 固定配列のfill構築

固定配列の作業領域を要素列挙なしで構築できるよう、§4.3に次の式を追加する。

```kimi
let zeros: [64 of u8] = [64 of 0]
let flags = [8 of false] // [8 of bool]
```

`[N of value]`は長さ定数式`N`（§4.2）と式`value`から`[N of T]`を構築する。`T`は`value`の型で、Copyでなければならない。`value`は要素位置として通常の規則で1回だけ取得され（裸のPlaceはCopy）、N回Copyされる。`N = 0`は空配列を構築する。式文脈で最初の要素の後に`of`が続く場合だけこの形式とし、型構文`[N of T]`と同じ語順にする。要素型は既存の期待型・リテラル規則で決める。fill以外の生成構築や未初期化領域の借用は追加しない。

### 2.4. 標準Writer

`FixedBuffer`と`HeapBuffer`は`BufferWriter`に適合し、次の操作を持つ。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `length` / `capacity` | getterの`ref/Self` | 確定済みバイト数 / 全容量 |
| `bytes()` | `ref/Self` | `Slice<u8>`。確定済み部分だけを共有借用 |
| `text()` | `ref/Self` | `Result<Utf8Slice, InvalidUtf8>`。検証済み長さ（§3.1）より後ろだけを検証し、確定済み全体のビューを返す。読み取り専用で、検証済み長さを更新しない |
| `clear()` | `uniq/Self` | 長さと検証済み長さを0に戻し、容量を保持する。消去は保証しない |
| `reserve(minimum)` | `uniq/Self` | §3.2のWindowを返す |
| `intoString()`（`HeapBuffer`のみ） | `owner/Self` | `Result<string, InvalidUtf8>`。未検証の末尾だけを検証し、成功時は元のヒープポインター・長さ・唯一の解放責任をstringへ渡す。縮小確保・コピーなし。未確保の空結果はStatic、確保済みなら長さ0でも元ポインターの解放責任を保持する。失敗時は消費したバッファを通常破棄する |

受け手の取得は通常の規則に従う。直接の所有Place `buffer`に対しては、`ref/Self`の操作は`buffer.text()`、`uniq/Self`の操作は`buffer@uniq.clear()`・`buffer@uniq.reserve(n)`、`owner/Self`の`intoString`は`buffer@move.intoString()`と書く。`uniq/Self`の受け手や排他参照経由のPlace（`self.inner.reserve(n)`）では印は要らない。

共有Slice、Window、アダプターが保持するLoanと競合する操作は拒否する。`FixedBuffer`の破棄は借用を終了するだけで、元領域を解放しない。`HeapBuffer`は所有する領域を通常破棄で1回解放する。一般の可変Sliceは導入しない。

### 2.5. UTF-8ビュー

`Utf8Slice`の操作:

| 操作 | 動作 |
| --- | --- |
| `length` | getterの`self: Self`。バイト数 |
| `bytes(self: Self) -> Slice<u8>{s}`、`origin s.source == self.source` | 元領域を共有借用したまま返す |

検証は有効なUnicode scalarのUTF-8だけを受理する。不完全な列、過長符号化、surrogate、範囲外の値を拒否し、置換や正規化は行わない。共有Loanにより、検証後の競合する書き換えを禁止する。バイト単位の任意の切り出しを有効なUTF-8とみなしてはならない。

### 2.6. UTF-8書き込みアダプター

`Utf8Writer<W>`の書き込み操作はすべて`self: uniq/Self`を受け取り、`Result<(), BufferFull>`を返す。

| 操作 | 動作 |
| --- | --- |
| `write(text: ref/string)` | 文字列のUTF-8を一括追記する |
| `write(text: Utf8Slice)` | 再検証せず一括追記する。入力の省略Originは受け手の`target`と独立 |
| `writeChar(value: char)` | 1つのUnicode scalarを追記する |
| `writeValue<T>(value: ref/T)`、`T is Utf8Format` | §4.2の共通手順で適合実装を1回呼ぶ |

ローカルに保持したアダプター`writer`では`writer@uniq.write(text)`・`writer@uniq.writeValue(value)`と書き、`uniq/Utf8Writer<W>`の引数として受け取った`writer`では`writer.write(text)`と裸で再借用する。書き込む値は共有借用（`Utf8Slice`と`char`はCopy）で渡り、元のPlaceを転送しない。

`status(self: ref/Self) -> Result<(), BufferFull>`で失敗状態を確認できる。状態のリセット、rawなWindow、内部Writerへのアクセス、所有権を取り出す操作は公開しない。アダプターの破棄は確定・出力・エラー通知を行わず、元Writerを破棄しない。借用が終了すれば元Writerを再び使用できる。

通常の関数・メソッドは、直接呼び出しでも関数値経由でも通常の引数評価を行う。短絡動作は§5.2の`$tryWrite`だけに与える。

## 3. バッファの安全性と容量管理

### 3.1. 共通状態と予約

領域のアドレス、容量、確定済み長さ、検証済み長さは、標準ライブラリ内部の共通状態が保持する。不変条件:

- `0 <= 検証済み長さ <= 確定済み長さ <= 容量 <= MaxObjectSize`。確定済み部分はすべて初期化済み。
- 公開する共有Sliceは確定済み部分に限る。検証済み長さまでの部分は有効なUTF-8である。
- Windowは未確定の末尾領域だけに書く。書き込み済み範囲は常に先頭から連続する。
- Window生存中は、領域と共通状態を排他的Loanで保護し、移動・再予約・伸長・破棄を競合して行えない。

`reserve(minimum)`の成功結果は`written == 0`かつ`remaining >= minimum`を満たし、確定済み長さを増やさない。`minimum == 0`では空のWindowを返してよく、標準Writerはこの要求だけを理由に確保しない。負数は`KIMI_E_ARG_RANGE`でAbortする。

`FixedBuffer`は空きが要求以上なら必ず成功し、不足時は`BufferFull`を返す。`HeapBuffer`は容量内なら確保せず成功し、不足時は伸長する。確保失敗を`BufferFull`に置き換えない。どのWriterも`BufferFull`では既存の確定済みバイトと長さを変えない。

`HeapBuffer`の必要長は`required = length + minimum`とし、超過・算術オーバーフローは`KIMI_E_ALLOC_SIZE`。伸長時は`capacity <= MaxObjectSize / 2`なら`max(required, 2 * capacity)`、それ以外は`required`を使う。倍増の計算自体をオーバーフローさせず、確定済み部分だけをコピーする。

### 3.2. Windowの操作

Windowは予約開始位置・書き込み済み長さ・容量上限を保持する小さな値で、追加の確保を行わない。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `written` / `remaining` | getterの`ref/Self` | 今回の書き込み済み長さ / 残容量 |
| `push(byte: u8)` | `uniq/Self` | `Result<(), BufferFull>`。1バイトを末尾へ書く |
| `append(bytes: Slice<u8>)` | `uniq/Self` | `Result<(), BufferFull>`。全バイトを末尾へコピーする。入力の省略Originは`source`と独立 |
| `limit(maximum: isize)` | `owner/Self` | `Self`。元のWindowを消費し、容量を`min(現在の容量, maximum)`へ制限する |
| `commit()` | `owner/Self` | `isize`。書き込み済み部分を確定し、そのバイト数を返す |

ローカルのWindow `window`では`window@uniq.push(b)`、`window@move.limit(n)`、`window@move.commit()`と書く。`limit`と`commit`は所有受け手なので、裸の`window.commit()`はエラーとなる。

`push`と`append`は、呼び出し全体が収まる場合だけ書く。容量不足では変更がなく、以前の書き込みはWindow内に残る。`limit`は拡大・複製・確定をせず、`maximum < written`なら`KIMI_E_ARG_RANGE`でAbortする。書き込み済み範囲、Origin、確定先を引き継ぎ、新しい`remaining`は制限後の容量から`written`を引いた値になる。

`commit()`は共通状態の確定済み長さだけを更新し、ユーザーWriterへのコールバックを呼ばない。消費済みWindowは使用不可。未確定の通常破棄では長さを増やさず借用を終了する。未初期化領域の読み取り、穴を空ける書き込み、任意長を確定する`advance(n)`は許さない。

`Utf8Writer`は公開`commit()`の代わりに内部の確定操作を使う。この操作は同じ更新に加え、確定する区間が検証済み長さの直後から始まる場合に検証済み長さも新しい確定済み長さへ進める。検証済み長さを進めるのはこの排他的な操作と`intoString`だけで、`text()`は進めない。したがってrawな`commit()`の後に`text()`を繰り返すと、同じ範囲を再検証する。Windowは常に共通状態から生じるため、ユーザーWriterが転送したWindowでも内側の標準Writerの検証済み長さが正しく進む。

### 3.3. ユーザー定義Writer

生ポインターからWindowを作る公開APIは設けない。ユーザーWriterは標準Writerを保持し、そのWindowを転送（`.Ok(window@move)`）または`limit`して返す。`BufferWriter`は`reserve`しか保証しないため、内側の長さが必要なWriterは具体的な標準Writerを保持する。上限付きWriterの例:

```kimi
struct Limited
    Self is BufferWriter
    var inner: HeapBuffer
    let maximum: isize

    public init(maximum: isize)
        if maximum < 0 => $abort("maximum must be nonnegative")
        self.inner = Text.heap(0)
        self.maximum = maximum

    public func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        let remaining = self.maximum - self.inner.length // 不変条件により 0 以上
        if minimum > remaining => return .Err(BufferFull.init())
        let window = try self.inner.reserve(minimum)      // 排他参照経由の再借用。負数は内側が KIMI_E_ARG_RANGE で拒否
        return .Ok(window@move.limit(remaining))          // 所有受け手なのでWindowを転送する
```

`self`は`uniq/Self`なので、`self.inner`への`reserve`は印なしの排他再借用で、`try`の被演算子は所有一時値である。`maximum`を不変にし構築時に非負を検査することで`0 <= inner.length <= maximum`が常に成立し、減算はオーバーフローせず、負の`minimum`は標準の範囲エラーになる。上限の変更を認める場合は、現在長未満への変更を拒否する。内側の容量が4096バイトでも残り予算20バイトのWindowだけを返せ、追加確保や確定コールバックは不要である。ユーザー実装には、確定済みの出力順序・内容を保ち、容量に関する約束を守る責任がある。型検査は動作規則の正しさまでは証明しないが、安全な実装からWindowの偽造や未初期化領域の公開はできない。

## 4. 書式化の共通規則

### 4.1. 表現と評価

`Utf8Format`は値を共有借用し、元の値を消費せず、出力先や入力への借用を結果に保持しない。成功時には値の完全な文字列表現を出力する。

**同じ値・同じ外部状態からの書式化では、出力先の型や容量を理由に成功時のUTF-8列を変えてはならない。** 容量不足による省略形を成功として返さず、`BufferFull`を返す。この規則は特殊化にも適用する。純粋性は要求せず、副作用や独自の確保は許可する。コンパイラは呼び出しを省略・複製する根拠にしない。ユーザーの書式化は1回だけ呼び、事前の長さ測定や容量拡張のために再実行しない。

組み込み値、`write`、`writeChar`の参照動作は、§4.2の状態確認後に正確なバイト長を求め、その長さで1回予約し、一括追記後に確定する。空出力は予約せず成功する。数値には小さな固定スタック領域を使用できる。例えば`123`は3バイトで予約するため、ちょうど収まる固定長領域で成功する。予約が成功すれば、そのWindowへの一括追記も必ず収まる。

具体型が標準Writerに確定し、値・失敗・副作用の順序が変わらないと証明できる場合は、直接符号化や予約・コピーの統合を許可する。ユーザーWriterの予約引数・呼び出し回数など、観測可能な動作は維持する。

`Utf8Writer`が確定する各区間は、それ自体で有効なUTF-8でなければならない。ユーザー書式化が複数の操作を行う場合、失敗までに確定した区間は残るが、不完全な文字は確定しない。rawなWindowで書かれたバイトや規約違反のユーザーWriterまで有効なUTF-8だと仮定しない。

### 4.2. 失敗状態

各書き込み操作のResultはその呼び出しの成否を返す。アダプターは最初の`BufferFull`を保持し、後続操作の成功や呼び出し側の無視で消えないようにする。以後の書き込み操作は同じ失敗を返し、内側の予約・書き込み・書式化実装の呼び出しを行わない。空出力もこの状態確認に従う。

`writeValue`の手順。通常の補間、`$tryWrite`、`Text.toString`、`Text.tryFormat`もこの経路を使う。

1. すでに失敗状態なら、書式化実装を呼ばず失敗を返す。
2. 選択済みの`format`を1回呼ぶ。
3. 戻り値が失敗、または呼び出し中に失敗状態になった場合は、失敗を保持して返す。実装が書き込みエラーを無視して成功を返しても成功扱いにしない。独自に返した`BufferFull`も保持するため、伸長可能なWriterへの書式化でも失敗し得る。

ユーザー実装にも失敗伝播を要求し、不要な後続処理の終了を推奨するが、関数内部の副作用を強制的に中断する保証はない。`format`の直接呼び出しは通常の関数呼び出しであり、共通の状態確認を含む入口は`writeValue`である。

状態を暗黙リセットしない。再開する場合はアダプターの使用を終了し、必要に応じて元Writerを`clear`（`buffer@uniq.clear()`）したうえで新しいアダプター（`Text.writer(buffer@uniq)`）を作る。確定済み出力・副作用の巻き戻しは保証しない。rawなWindowの容量エラーはアダプター外の独立した操作なので、この失敗状態を持たない。

### 4.3. 既定書式

組み込み型と`Utf8Slice`は以下の表記で適合する。借用型から参照先の適合への委譲は設けず、借用値の書式化は§5.1の引数適合で参照先の型が決まる。ポインター・オブジェクトアドレスの暗黙書式化や、Tuple・配列・ユーザー型の自動適合は追加しない。

| 値 | 表記 |
| --- | --- |
| 整数 | ASCIIの10進数。負数だけ`-`。不要な先頭ゼロなし。最小負数も処理する |
| `bool` | `true` / `false` |
| `char` | そのUnicode scalarのUTF-8 |
| Unit | `()` |
| `string` / `Utf8Slice` | 内容をそのまま追記。NULを含めてバイト長で扱う |
| 浮動小数点 | 以下の共通規則 |

有限・非ゼロの浮動小数点は、対象型へ最近接・偶数丸めで戻すと同じ値になる、最少有効桁数の10進表現を選ぶ。同桁数の候補は正確な値に最も近いもの、同距離なら末尾の有効数字が偶数のものを選ぶ。`f32`は`f32`の値として桁を選ぶ。「最短」は有効桁数を指し、全文字数の最小化ではない。

正規化した10進指数（`d.ddd × 10^e`の`e`）が`-4 <= e < 16`なら固定小数点、それ以外は指数表記とする。不要な小数末尾ゼロ・末尾小数点を出さず、指数は小文字`e`、正指数の`+`と先頭ゼロを省略する。正負のゼロは`0` / `-0`、無限大は`Infinity` / `-Infinity`、NaNは符号・payloadによらず`NaN`。小数点は`.`とし、桁区切りとロケール依存を禁止する。

### 4.4. 容量の見積もり

既定書式の符号込みの最大バイト長を、作業領域と容量見積もりに用いる。

| 型 | 最大バイト長 |
| --- | --- |
| `i8` / `u8` | 4 / 3 |
| `i16` / `u16` | 6 / 5 |
| `i32` / `u32` | 11 / 10 |
| `i64` / `u64` | 20 / 20 |
| `i128` / `u128` | 40 / 39 |
| `isize` / `usize` | 対象のポインター幅に対応する整数型と同じ |
| `bool` / `char` / Unit | 5 / 4 / 2 |
| `f32` / `f64` | 17 / 24。§4.3の固定小数点・指数表記と特殊値を含む |

`string`・`Utf8Slice`・ユーザー型に静的上限はなく、見積もりでは0として扱う。見積もりの加算はコンパイル時に行い、`MaxObjectSize`の超過は検出して見積もりを放棄する。Abortしない。

補間と`Text.toString`は次の2経路を使う。長さのために式を先行評価したり、ユーザー書式化を実行したりしない。

| 補間 | 条件 | 経路 |
| --- | --- | --- |
| **有界な補間** | すべての値に静的上限があり、リテラル長との合計が`MaxObjectSize`以下 | 合計を事前確保し、伸長しない。見積もりを超える必要長の検査は§3.1に従う |
| それ以外 | 無界の値を含む、または合計が超過 | 通常の伸長経路。無界の値を0とした合計が制限内なら、それを初期容量の下限にする。超過なら未確保で開始する |

予約は確定を増やさず観測不能なので、通常の伸長経路では、内部`HeapBuffer`への各予約要求に「残りのリテラル長と有界値の上限の合計」を加えてよい。この加算は内部`HeapBuffer`だけに適用し、`$tryWrite`とユーザーWriterへの予約要求には適用しない（固定長領域で誤った`BufferFull`を生むため）。

## 5. 補間

### 5.1. 通常の補間

```kimi
let message = "My number is \(self.number)"
```

構文・エスケープ・入れ子は既存§2.9に従う。結果は所有`string`。各埋め込み式は`writeValue<T>(value: ref/T)`の引数として既存§10.2で適合させ、決まった`T`に`Utf8Format`適合を静的に要求する。所有Place（Copy型を含む）と所有一時値は共有借用、`ref/T`はExact、`uniq/T`と排他参照経由のPlaceは共有Reborrowで、いずれも`T`は参照先の型になる。裸の埋め込み式は元のPlaceを転送せず、印も要らない。`\(x@move)`は転送した一時値の共有借用となり、一時値の破棄境界で破棄される。リテラルは通常の既定型で決め、補間結果の`string`を各式の期待型にしない。

下降は次の順序とする。

1. §4.4の見積もりで`HeapBuffer`をローカルに構築し、`Utf8Writer`で排他的に借用する（ソース上の`Text.writer(buffer@uniq)`に相当）。
2. 左からリテラル部分を追記し、各式を1回評価して`writeValue`で書式化する。完了してから次の式へ進む。
3. 最初の失敗で終了する。成功時はアダプターの借用を終了し、`intoString`（§2.4）で所有`string`へ移譲する（`buffer@move.intoString()`に相当）。

値の借用は書式化呼び出し後に終了するが、一時値の破棄境界は既存規則を維持する。最終的な`BufferFull`は`KIMI_E_FORMAT: Formatting failed`でAbortする。

`HeapBuffer`本体はコンパイラだけが保持し、ユーザーコードには借用アダプターだけが渡るため、確定済みバイトはすべて`Utf8Writer`経由で書かれ、検証済み長さ == 確定済み長さが構造的に成立する。したがって`intoString`は再検証なしに成功し、生成コードはその失敗分岐を省略できる。

### 5.2. 短絡するWriter補間

```kimi
$tryWrite(writer@uniq, "(\(point.x), \(point.y))")   // writerはローカルのUtf8Writer
```

`$tryWrite`はコンパイラ認識構文で、関数値にはならない。第1項は`uniq/Utf8Writer<W>`を要求する引数位置として通常の規則で取得する。直接の所有Placeには`@uniq`が要り（裸の`$tryWrite(writer, ...)`はエラーとし、`writer@uniq`を提示する）、`uniq/Utf8Writer<W>`の値と排他参照経由のPlaceは裸で排他再借用する。所有一時値は明示の`@uniq`だけで借用できる。第2項は文字列リテラル構文に限定する。補間を含まない通常・raw文字列リテラルも許可し、任意の文字列値には`write`を使う。結果は`Result<(), BufferFull>`で、全体の所有`string`は作らない。

第1項を1回評価して排他的借用を開始し、同じアダプターへ左からリテラルと値を書き込む。埋め込み式の型付けは§5.1と同じ。最初の失敗で後続の埋め込み式を評価せず終了し、開始時に失敗状態なら埋め込み式を一切評価しない。実行されない式にも型・適合・制御移動先の検査を行う。未確定Windowは通常破棄し、確定済み出力は残す。

展開は元の式の一時値境界と`return`・`exit`・`yield`の対象を維持する。内部の複数呼び出しを理由に、一時値を早く破棄したり、新しい制御移動先を作ったりしない。受け手借用は埋め込み式の評価前から有効で、競合する入力参照を拒否する。`$tryWrite`は呼び出しではないので、第1項の`@uniq`や排他再借用に§15.6.7の呼び出し予約は適用せず、二段階借用も導入しない。埋め込み式から同じWriterや、その借用元を読むことはできない。

`writer@uniq.write("...\(value)...")`では通常の補間が先に完了し、この意味はラッパー・関数値経由でも変わらない。補間を含む文字列リテラルが`Utf8Writer.write`の引数に直接現れた場合、§17.4の警告として`$tryWrite`を提案する。意味は変えない。

ユーザー実装の例:

```kimi
struct Point
    Self is Utf8Format
    var x: i32
    var y: i32

    public func format<W>(self: ref/Self, writer: uniq/Utf8Writer<W>) -> Result<(), BufferFull>
        W is BufferWriter
        return $tryWrite(writer, "(\(self.x), \(self.y))") // 借用値のwriterは裸で排他再借用
```

## 6. コンソール出力と最適化

### 6.1. 借用による出力

既存の必須Symbol `writeLine(text: ref/string)`（§22.4）に`Utf8Slice`のオーバーロードを加え、次の2つとする。所有引数のオーバーロードは追加しない。

```kimi
public func writeLine(text: ref/string) -> ()
public func writeLine(text: Utf8Slice) -> ()
```

```kimi
let name = "Kimigayo"
Console.writeLine(name)
Console.writeLine(name) // 借用なので転送されない
Console.writeLine("Hello, \(name)")
```

所有Place・所有一時値からの共有借用は既存§10.2を使い、`string`の値には`ref/string`候補だけが適用可能、`Utf8Slice`の値には`Utf8Slice`候補がExactとなる。暗黙変換は追加しないので両候補が競合することはない。一時値は既存の外側の式・条件・matchの境界まで生存し、少なくとも呼び出し終了までは有効。新しい暗黙変換や寿命延長は追加しない。

両オーバーロードはUTF-8全体とLFを出力してUnitを返し、内部の`WriteStdout(data, length)`を直接呼ぶ。stringハンドルの実体化や解放責任の移動は行わない。出力失敗、部分出力、flush、NUL、符号化、Symbol Identityは既存§22.4に従う。

ユーザーは公開APIだけでゼロ確保出力を書ける。

```kimi
func printNumber(n: i64) -> Result<(), BufferFull>
    var scratch: [64 of u8] = [64 of 0]
    var buffer = Text.fixed(scratch@uniq)
    var writer = Text.writer(buffer@uniq)
    try $tryWrite(writer@uniq, "My number is \(n)")
    match buffer.text()           // writerの借用は終了済み。共有借用で読む
        .Ok(let text) => Console.writeLine(text)
        .Err(_) => ()
    return .Ok(())
```

### 6.2. 有界な補間の最適化

`Console.writeLine`の引数に補間リテラルが直接現れ、次の条件をすべて満たす場合、実装はそれを上記の等価なソースへ下降してよい。

- 有界な補間（§4.4）である。
- 見積もりの合計が実装定義のスタック領域上限以下である。長大なリテラルや多数の数値を含む補間は有界でも上限を超え得るため、スタックフレームの肥大化を防ぐ。
- 補間式の型、適合先、選択済みの特殊化、一時値寿命、評価順を変えない。

固定領域の長さは見積もりの合計とする。生成コードは検証済み長さ == 確定済み長さを構造的に保証するため`text()`の再検証を省略する。有界な補間は失敗しないため、途中でヒープへ移す経路や、式・ユーザー書式化の再実行は生じない。条件を満たさない補間は通常のヒープ経路を使う。全補間が成功してから外部へ出力し、後続の書式化がAbortしたとき補間の先頭だけを先に出力してはならない。除去した一時領域の確保・解放そのものの資源失敗は観測対象から除いてよい。

### 6.3. 性能の合格条件

| 経路 | 要求 |
| --- | --- |
| Window・UTF-8ビュー・アダプターの内部管理 | 追加ヒープ確保、管理用コールバック、参照カウント更新なし |
| 組み込み型＋`FixedBuffer` | 実際に収まる場合は書式化処理のヒープ確保0回。中間string・boxing・引数配列なし |
| 組み込み型＋`HeapBuffer` | 容量内では追加確保0回。`clear`で容量を再利用 |
| `Utf8Writer`だけで書いた標準Writerの`text()`・`intoString()` | 追加検証0バイト。`intoString`は追加確保・コピーなし |
| 有界な補間の所有結果 | 書式化処理のヒープ確保1回以内、完成時コピーなし |
| 無界の値が`string`または`Utf8Slice` 1個だけで、他は有界な補間の所有結果 | ヒープ確保2回以内（§4.4の予約加算により、その値の実長と残りの上限を1回で予約する） |
| `Console.writeLine("My number is \(n)")`、ローカル変数`n: i64` | 最適化有効時は本文最大33バイト（リテラル13 + 数値20）を固定スタック領域に置き、ヒープ確保0回 |

初期プロファイルはmonomorphizationであり（§21.3.1）、具体型と標準Writerが確定する組み込み書式化は直接呼び出しまたはインライン化する。将来の共有生成では`Utf8Format.format<W>`のような関数総称要件の呼び出し方式を別途決める必要があり、本案はその設計を保留する。任意長の所有結果、容量を超える伸長、ユーザー実装・OS内部の確保にはゼロ確保を保証しない。標準の内部数値変換は、検証済みの直接書き込みで初期化範囲を一括更新してよい。

## 7. 検証

実装時は、通常のネイティブ実行・確保カウンター・生成コードで次を検証する。

### 7.1. 表記・評価・失敗

- 整数の境界、UTF-8検証、NUL、空出力、浮動小数点の境界と表記。§4.4の最大長、見積もり超過（Abortせず放棄すること）、伸長計算の境界、スタック領域上限を超える有界な補間がヒープ経路になること。
- 各式・`format`が1回だけ実行され、拡張で再実行しないこと。出力先によって成功結果が変わらないこと。
- ちょうど収まる固定長領域の成功、1バイト不足の失敗、容量制限Window、無視された書き込みエラーの検出。
- 成功した予約が要求長以上を返し、失敗で確定済み内容が変わらないこと。ユーザーWriterで参照動作どおりの予約を行うこと。
- 失敗後の追加出力禁止と、`$tryWrite`だけが後続式を短絡すること。通常の関数の直接・間接呼び出しの一致。
- 独自に返された`BufferFull`を保持し、通常の補間では`KIMI_E_FORMAT`になること。

### 7.2. 借用・公開API

- Windowの二重確定、消費後使用、競合する再予約・伸長・入力aliasを拒否すること。未確定破棄で長さが増えないこと。
- 検証済みビューの競合書き換えを拒否し、確定したUTF-8に不完全な文字を含めないこと。rawなWindowで書いた後の`text()`が末尾だけを検証し、検証済み長さを更新しないこと。
- 公開APIだけで上限付きWriter、ユーザー書式化、ゼロ確保出力を実装でき、借用・破棄・再利用が成立すること。`[N of value]`で作業領域を構築できること。
- 総称アダプターが`reserve`以外を要求しないこと。借用値の書式化が引数適合だけで決まること。
- 直接の所有Placeを`Text.fixed`・`Text.writer`・`Text.tryFormat`・`$tryWrite`の排他位置へ裸で渡すとエラーとなり`@uniq`を提示すること。借用値の`writer`は裸で再借用できること。`commit`・`limit`・`intoString`の裸の呼び出しがエラーとなり`@move`を提示すること。埋め込み式が元のPlaceを転送しないこと。
- Originの明示形と省略形の適合が一致し、返却ビューの依存先とLoanを保持すること。不正な寿命延長・参照の脱出を拒否すること。

### 7.3. 出力・記憶域・性能

- `writeLine`後も入力変数を使え、書式化失敗で補間本文を部分出力しないこと。§6.3の性能条件。
- `intoString`の成功・失敗と補間の途中離脱で二重解放・解放漏れがないこと。最適化によるスタック領域の脱出がないこと。

## 8. 正式仕様への適用範囲

本案の編集では`SPEC.md`と参照先仕様を変更しない。将来の統合では次を反映する。

| 箇所 | 変更内容 |
| --- | --- |
| §2.9.1、§12.2、§12.3.3 | 補間を`Utf8Format`と`writeValue`引数適合へ変更し、`$tryWrite`の第1項の取得、評価順と短絡を追加する。借用値の適合委譲（「borrows forward through shared access」）の記述を削除する |
| §4.3、Appendix F | 固定配列のfill構築`[N of value]`を追加する |
| §13.8、Appendix F | Composition Rootの構文に`$tryWrite`を追加する |
| §7.2.3 | 「stringifying」の例を`Text.toString`へ差し替える |
| §8.4.4、Appendix A | `Stringify`の記述を`Utf8Format`へ差し替え、既存の適合条件は維持する |
| §13.5.8、§15.7 | `Kimi.Intrinsics.clone`に`ref/string -> string`を追加する |
| §17.4 | `Utf8Writer.write`への補間リテラル直接渡しの警告を追加する |
| §22.1・§22.1.1、SPEC.mdの宣言索引 | `Stringify`とstringの「Stringify」操作を削除し、公開Contract・型・`Kimi.Text`と本案の関数を登録する |
| §22.1、§22.2.2、§22.4、§22.5.5 | 既存の`writeLine(text: ref/string)`に`Utf8Slice`オーバーロードを追加する。§22.2.2の例に残る「Moveされる」旨の注釈と、§22.4の`stringify`結果の記述を差し替える。stringの有効な`releaseKind`は変更しない |
| §22.5.4 | `KIMI_E_ARG_RANGE: Argument out of range`と`KIMI_E_FORMAT: Formatting failed`を追加する。要素指標の範囲外は従来どおり`KIMI_E_INDEX_BOUNDS` |
| §21、Appendix A | §6の生成計画と§7の検証条件を接続する |
| Appendix E | `Stringify`の用語を除去し、本案の型・Contractを追加する |

Origin、一般の静的Contract・Copy・Ownedの規則、転送（`@move`）と排他借用（`@uniq`）の取得規則は変更しない。一般の可変Slice、rawメモリ取得、Runtime Contract Viewの拡張、隠れた共有バッファ、グローバルプールは追加しない。
