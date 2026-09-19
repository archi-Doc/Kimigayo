# UTF-8書式化・文字列補間 — 仕様変更案

- 日付: 2026-09-20
- 状態: 設計案。例は提案APIを使用し、実装済み機能や検証済みABIを示さない。
- 優先順位: 本案で変更する事項は[SPEC.md](../../SPEC.md)およびその参照先より優先する。変更しない事項には既存仕様を適用する。他の設計案の未統合機能を前提としない。
- 目的: 書式化を1回の評価と書き込みで実行し、借用とバッファ再利用によって不要な確保・コピーを避ける。

## 1. 基本方針

1. 必須Contractの`Stringify`を削除し、`Utf8Format`に一本化する。旧Contractや同名メソッドへのフォールバックは行わない。
2. 通常の補間式は所有`string`を返す。Writerへの短絡する補間は、明示的な構文`$tryWrite`で表す。
3. `Console.writeLine`は`ref/string`を受け取り、所有権を取得・保持・破棄しない。
4. 書式化先はUTF-8。初期版は既定書式だけを提供する。桁揃え、精度、基数、ロケール、実行時書式文字列は追加しない。
5. `BufferWriter`と`Utf8Format`はユーザー実装可能な静的Contractとする。型消去やboxingを必要としない。
6. 固定長領域の容量不足は`BufferFull`。伸長可能な標準Writerの確保失敗・サイズ上限超過は既存のAbort規則に従う。

`Stringify`というユーザー識別子は引き続き使用できるが、補間への特別な効果は持たない。数値の`@string`変換や、未確定の文字列`+`の動作は追加しない。

## 2. 公開API

### 2.1. Contractと型

以下を`Kimi`の必須宣言とする。API表の操作はすべて公開。表内の`Self`はその操作の所属型を表し、Originを持つ型では既存の束縛も保持する。

```kimi
contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow from self, BufferFull>

contract Utf8Format
    func format<W> origin target(self: ref/Self, writer: uniq/(Utf8Writer<W> from target)) -> Result<(), BufferFull>
        W is BufferWriter
```

| 型 | 所有権・依存関係 |
| --- | --- |
| `BufferFull` / `InvalidUtf8` | 状態を持たないCopyのエラー型。引数なしの公開コンストラクターを持つ |
| `WriteWindow origin source` | Non-Copy。書き込み領域と確定位置を排他的に借用する |
| `FixedBufferWriter origin source` | Non-Copy。呼び出し側の固定長バイト領域を排他的に借用する |
| `ReusableBufferWriter` | Non-Copy。伸長可能なヒープ領域を所有する |
| `Utf8Slice origin source` | Copy。検証済みUTF-8領域を共有借用する。参照カウント更新なし |
| `Utf8Writer<W> origin target` | Non-Copy。`W is BufferWriter`。`uniq/W from target`と失敗状態を保持する |

`Utf8Writer`は常に借用専用で、Writer本体を所有せず、独自のヒープ領域を持たない。型引数`W`の内部Originも保持する。`uniq/W`に`BufferWriter`適合を自動追加する特別規則は設けない。

ユーザーの適合は、既存の明示的な適合宣言・公開実装・制約検証に従う。同名メソッドがあるだけでは適合しない。

### 2.2. 構築と標準Writer

通常のgroup `Text`に次のType関数を置く。関数の型引数・長さ引数は既存規則で推論できる。

| 関数 | 結果と動作 |
| --- | --- |
| `fixed<length N>(destination: uniq/[N of u8])` | `FixedBufferWriter from destination`。確定済み長さ0、容量N |
| `buffer(initialCapacity: isize)` | `ReusableBufferWriter`。確定済み長さ0。0なら未確保で開始できる |
| `writer<W>(destination: uniq/W)`、`W is BufferWriter` | `Utf8Writer<W> from destination`。失敗状態を持たず開始する |

`initialCapacity`の負数は`KIMI_E_INDEX_BOUNDS`、上限超過・確保失敗は既存の確保用Abortとする。

両標準Writerは`BufferWriter`に適合し、次の操作を持つ。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `length` / `capacity` | getterの`ref/Self` | `isize`。確定済みバイト数 / 全容量 |
| `bytes()` | `ref/Self` | `Slice<u8> from self`。確定済み部分だけを共有借用 |
| `clear()` | `uniq/Self` | `()`。長さを0に戻し、容量を保持する。消去は保証しない |
| `reserve(minimum: isize)` | `uniq/Self` | §3のWindowを返す |

共有Slice、Window、アダプターが保持するLoanと競合する操作は拒否する。`FixedBufferWriter`の破棄は借用を終了するだけで、元領域を解放しない。`ReusableBufferWriter`は所有する領域を通常破棄で1回解放する。

固定長Writerの入力配列は初期化済みでなければならない。未初期化配列の借用を許す変更や、一般の可変Sliceは導入しない。

### 2.3. UTF-8ビューと書き込み

構築は`Text`のType関数、取得は`Utf8Slice`のインスタンス操作とする。構築先型のOriginを先に指定する必要はない。

| 操作 | シグネチャ・動作 |
| --- | --- |
| `Text.utf8(text: ref/string)` | `Utf8Slice from text`。確保・コピー・再検証なし |
| `Text.validateUtf8 origin source(bytes: Slice<u8> from source)` | `Result<Utf8Slice from source, InvalidUtf8>`。全体を1回検証。確保・コピーなし |
| `byteLength` | getterの`self: Self`から`isize`を返す |
| `bytes(self: Self)` | `Slice<u8> from self.source`。元領域を共有借用したまま返す |

検証は有効なUnicode scalarのUTF-8だけを受理する。不完全な列、過長符号化、surrogate、範囲外の値を拒否し、置換や正規化は行わない。共有Loanにより、検証後の競合する書き換えを禁止する。バイト単位の任意の切り出しを有効なUTF-8とみなしてはならない。

`Utf8Writer<W>`の書き込みはすべて`self: uniq/Self`を受け取り、`Result<(), BufferFull>`を返す。

| 操作 | 動作 |
| --- | --- |
| `write(text: ref/string)` | 文字列のUTF-8を一括追記する |
| `write origin input(text: Utf8Slice from input)` | 検証済み領域を再検証せず一括追記する |
| `writeChar(value: char)` | 1つのUnicode scalarを追記する |
| `writeValue<T>(value: ref/T)`、`T is Utf8Format` | §4の共通規則で適合実装を1回呼ぶ |

`status(self: ref/Self) -> Result<(), BufferFull>`で失敗状態を確認できる。状態のリセット、rawなWindow、内部Writerへのアクセス、所有権を取り出す操作は公開しない。アダプターの使用を終了すれば、元のWriterを再び使用できる。

通常の関数・メソッドは、直接呼び出しでも関数値経由でも通常の引数評価を行う。`tryWrite`という特別なメソッドは設けず、短絡動作は§5の`$tryWrite`だけに与える。

## 3. バッファの安全性と容量管理

### 3.1. 共通状態と予約

領域のアドレス、容量、確定済み長さは、標準ライブラリ内部の状態管理部が保持する。次を不変条件とする。

- `0 <= 確定済み長さ <= 容量 <= MaxObjectSize`。確定済み部分はすべて初期化済み。
- 公開する共有Sliceは確定済み部分に限る。
- Windowは未確定の末尾領域だけに書く。書き込み済み範囲は常に先頭から連続する。
- Window生存中は、領域と状態管理部の両方を排他的Loanで保護する。移動・再予約・伸長・破棄を競合して行えない。

`reserve(minimum)`の成功結果は、少なくとも`minimum`バイトの空き容量を持つ。予約だけでは確定済み長さを増やさない。`minimum == 0`では空のWindowを返してよく、標準Writerはこの要求だけを理由に確保しない。負数は`KIMI_E_INDEX_BOUNDS`でAbortする。

固定長Writerは空きが要求以上なら必ず成功し、不足時は既存の確定済みバイトと長さを変更せず`BufferFull`を返す。伸長可能Writerは容量内なら確保せず成功し、不足時は伸長する。確保失敗を`BufferFull`に置き換えない。

### 3.2. Windowの操作

Windowは予約開始位置・書き込み済み長さ・容量上限を保持する小さな値であり、追加の確保を行わない。

| 操作 | 受け手 | 結果・動作 |
| --- | --- | --- |
| `written` / `remaining` | getterの`ref/Self` | `isize`。今回の書き込み済み長さ / 残容量 |
| `push(byte: u8)` | `uniq/Self` | `Result<(), BufferFull>`。1バイトを末尾へ書く |
| `append origin input(bytes: Slice<u8> from input)` | `uniq/Self` | `Result<(), BufferFull>`。全バイトを末尾へコピーする |
| `limit(maximum: isize)` | `owner/Self` | `Self`。元のWindowを消費し、容量を`min(現在の容量, maximum)`へ制限する |
| `commit()` | `owner/Self` | `isize`。初期化済み部分を確定し、今回の確定バイト数を返す |

`push`と`append`は、その呼び出し全体が収まる場合だけ書く。容量不足では、その呼び出しによるバイトと長さの変更はない。以前の書き込みはWindow内に残る。

`limit`は拡大も複製もしない。`maximum < written`または負数なら`KIMI_E_INDEX_BOUNDS`でAbortする。Originと確定先は元のWindowから保持する。安全なAPIでは、未初期化領域の読み取り、穴を空ける書き込み、任意の長さを確定する`advance(n)`を許さない。

`commit()`は共通状態の長さだけを更新し、ユーザーWriterへのコールバックを呼ばない。消費済みWindowは使用不可。未確定の通常破棄では長さを増やさず借用を終了する。バイトの消去や容量の巻き戻しは要求しない。Abort時の後始末は既存規則に従う。

### 3.3. ユーザー定義Writerと伸長

ユーザーは標準Writerを保持・合成し、そのWindowを転送して独自Writerを実装できる。確定位置の正本は共通状態とし、別途の累計値は次の予約や明示的な出力時に確定済み長さから更新する。生ポインターからWindowを作る公開APIは設けない。

例えば上限付きWriterは、次の共通手順で制限を強制できる。

```text
remaining = 出力上限 - 内部Writerの確定済み長さ
minimumが負数ならAbort、remainingを超えるならBufferFull
内部Writerからminimum以上のWindowを取得
window.limit(remaining)を返す
```

これにより、内部容量が4096バイトでも、残り予算20バイトのWindowだけを返せる。追加確保や確定コールバックは不要。

`ReusableBufferWriter`の必要長は`required = length + minimum`、伸長先は`max(required, 2 * capacity)`を基本とする。倍増だけが上限を超える場合は有効な`required`を使い、必要長自体の超過・算術オーバーフローは`KIMI_E_ALLOC_SIZE`とする。計算は検査付きで行い、確定済み部分だけをコピーする。

ユーザー実装には、確定済みの出力順序・内容を保ち、容量に関する約束を守る責任がある。型検査は動作規則の正しさまでは証明しないが、安全な実装からWindowの偽造や未初期化領域の公開はできない構造とする。

## 4. 書式化の共通規則

### 4.1. 表現と評価

`Utf8Format`は値を共有借用する。元の値を消費せず、出力先や入力への借用を結果に保持しない。成功時には値の完全な文字列表現を出力する。

**同じ値・同じ外部状態からの書式化では、出力先の型や容量を理由に成功時のUTF-8列を変えてはならない。** 容量不足による省略形を成功として返さず、`BufferFull`を返す。この規則は特殊化にも適用する。純粋性を要求するものではなく、副作用や独自の確保は許可する。コンパイラは呼び出しを省略・複製する根拠にしない。

ユーザーの書式化は1回だけ呼ぶ。事前の長さ測定や容量拡張のために再実行しない。組み込み書式化は、固定長Writerの実際の空きに収まるなら成功する。例えば`123`は3バイトで成功し、最大桁数の予約に失敗しただけで全体を失敗させない。必要時には小さな固定スタック領域で長さを確定してよい。

`Utf8Writer`は、成功した書き込みが有効なUTF-8になるように処理する。分割時は文字境界で確定し、容量不足でも不完全な文字を確定しない。既存のrawバイトや規約違反のユーザーWriterまで有効なUTF-8だと仮定しない。所有`string`の生成には、標準の文字列用Writerで保持したUTF-8成立の証拠を使う。

### 4.2. 失敗状態

`Utf8Writer`はアダプター単位で、公開の書き込み操作が最終的に返す最初の`BufferFull`を保持する。内部の予約試行が不成立でも、値を再評価・再書式化せずに収まる別の書き方を選べる場合は、まだ操作全体の失敗としない。以後、失敗状態での書き込み操作は同じ失敗を返し、内部Writerへの予約・書き込み・書式化実装の呼び出しを行わない。通常の関数では、その呼び出しに到達するまでの引数評価は行われる。

`writeValue`は次の順序で動作する。通常の補間、`$tryWrite`、§5.3の関数もこの経路を使う。

1. すでに失敗状態なら、書式化実装を呼ばず失敗を返す。
2. 選択済みの`format`を1回呼ぶ。
3. 戻り値が失敗、または呼び出し中に失敗状態になった場合は、失敗を保持して返す。実装が書き込みエラーを無視して成功を返しても成功扱いにしない。

ユーザー実装にも、書き込みの失敗を伝播し、不要な後続処理を終了することを要求する。ただし、任意のユーザー関数内部の副作用を強制的に中断する保証はない。`format`メソッドの直接呼び出しは通常の関数呼び出しであり、共通の状態確認を含む入口は`writeValue`とする。

状態を暗黙リセットしない。再開する場合はアダプターの使用を終了し、必要に応じて元Writerを`clear`したうえで新しいアダプターを作る。すでに確定した出力・副作用の巻き戻しは保証しない。rawなWindowの容量エラー自体は、アダプター外の独立した予約操作なので、この失敗状態を持たない。

### 4.3. 既定書式

組み込み型と`Utf8Slice`は以下の表記で適合する。安全な`ref/T`と`uniq/T`は、`T`の`Utf8Format`適合を共有アクセスで転送する。ポインター・オブジェクトアドレスの暗黙書式化や、Tuple・配列・ユーザー型の自動適合は追加しない。

| 値 | 表記 |
| --- | --- |
| 整数 | ASCIIの10進数。負数だけ`-`。不要な先頭ゼロなし。最小負数も処理する |
| `bool` | `true` / `false` |
| `char` | そのUnicode scalarのUTF-8 |
| Unit | `()` |
| `string` / `Utf8Slice` | 内容をそのまま追記。NULを含めてバイト長で扱う |
| 浮動小数点 | 以下の共通規則 |

有限・非ゼロの浮動小数点は、対象型へ最近接・偶数丸めで戻すと同じ値になる、最少有効桁数の10進表現を選ぶ。同桁数の候補は正確な値に最も近いもの、同距離なら末尾の有効数字が偶数のものを選ぶ。`f32`は`f32`の値として桁を選ぶ。

正規化した10進指数を`e`とし、`-4 <= e < 16`では固定小数点、それ以外は指数表記とする。不要な小数末尾ゼロ・末尾小数点を出さず、指数は小文字`e`、正指数の`+`と先頭ゼロを省略する。「最短」は有効桁数を指し、全文字数の最小化ではない。

正負のゼロは`0` / `-0`、無限大は`Infinity` / `-Infinity`、NaNは符号・payloadによらず`NaN`。小数点は`.`とし、桁区切りとロケール依存を禁止する。NaNのpayload復元は保証しない。

## 5. 補間と文字列生成

### 5.1. 通常の補間

```kimi
let message = "My number is \(self.number)"
```

構文・エスケープ・入れ子は既存§2.9に従う。結果は所有`string`。各埋め込み式は通常の推論とリテラル既定型で型を決め、`Utf8Format`適合を静的に検査する。補間結果の`string`を各式の期待型にしない。

標準の伸長可能な文字列用Writerへ、左からリテラル部分を追記し、各式を1回評価・共有借用して`writeValue`で書式化する。完了してから次の式へ進み、先に全式を評価しない。値の借用は書式化呼び出し後に終了するが、一時値の破棄境界は既存規則を維持する。

最終的な`BufferFull`は`KIMI_E_FORMAT: Formatting failed`でAbortする。標準の伸長自体は容量不足を返さないが、ユーザー実装が返すエラーも無視しない。

完成したヒープ領域は文字列へ移譲し、不要な縮小確保・完成時コピーを行わない。空文字列は既存のStatic表現を利用できる。文字列用Writerの正規の型はすべての呼び出しで同一とし、内部の記憶域最適化でジェネリック実装の選択を変えない。この内部型に一般のrawバイト書き込みや公開コンストラクターは追加しない。

### 5.2. 短絡するWriter補間

```kimi
$tryWrite(writer, "(\(point.x), \(point.y))")
```

`$tryWrite`はコンパイラ認識構文で、関数値にはならない。第1項は`Utf8Writer<W>`への排他的アクセス、第2項は文字列リテラル構文に限定する。通常の括弧で囲んでもよい。補間を含まない通常・raw文字列リテラルも許可し、任意の文字列値には通常の`write`を使用する。

第1項を1回評価して排他的借用を開始し、同じアダプターへ左からリテラルと値を書き込む。結果は`Result<(), BufferFull>`。全体の所有`string`は作らない。

最初の失敗で後続の埋め込み式を評価せず終了する。開始時に失敗状態なら埋め込み式を一切評価しない。実行されない式にも型・適合・制御移動先の検査を行う。未確定Windowは通常破棄し、確定済み出力は残す。

展開は元の式の一時値境界と`return`・`exit`・`yield`の対象を維持する。内部の複数呼び出しを理由に、一時値を早く破棄したり、新しい制御移動先を作ったりしない。受け手借用は埋め込み式の評価前から有効で、競合する入力参照を拒否する。二段階借用は導入しない。

`writer.write("...\(value)...")`では通常の補間が先に完了する。この意味はラッパー・関数値経由でも変わらず、最適化で短絡動作へ変更してはならない。

### 5.3. 明示的な書式化関数

| `Text`の関数 | 動作 |
| --- | --- |
| `format<T>(value: ref/T) -> string`、`T is Utf8Format` | 文字列用Writerへ1回書式化する。所有結果と失敗規則は通常の補間と同じ |
| `tryFormat<T, length N>(value: ref/T, destination: uniq/[N of u8]) -> Result<isize, BufferFull>`、`T is Utf8Format` | 固定長Writerへ1回書式化し、成功時にバイト数を返す |

`tryFormat`は成功時の先頭バイト数だけを完成した結果とする。失敗時には途中のバイトが残り得るため、結果全体として扱わない。ゼロ長領域と空出力も許可する。別の`TryFormat` Contract、長さ測定Contract、自動再試行は設けない。

```kimi
var destination: [3 of u8] = [0, 0, 0]
let result = Text.tryFormat(123, destination) // 成功: 3バイト。追加ヒープ確保なし
```

ユーザー実装の例。`x`と`y`が`i32`の`Point`内に置く。

```kimi
// Pointには Self is Utf8Format を宣言する。
public func format<W> origin target(self: ref/Self, writer: uniq/(Utf8Writer<W> from target)) -> Result<(), BufferFull>
    W is BufferWriter
    return $tryWrite(writer, "(\(self.x), \(self.y))")
```

## 6. コンソール出力と最適化

### 6.1. 借用による出力

必須Symbolを`public func writeLine(text: ref/string) -> ()`へ置き換え、所有引数の旧オーバーロードは残さない。

```kimi
let name = "Kimigayo"
Console.writeLine(name)
Console.writeLine(name) // Moveされない
Console.writeLine("Hello, \(name)")
```

所有Place・所有一時値からの共有借用は既存§10.2を使う。一時値は既存の外側の式・条件・matchの境界まで生存し、少なくとも呼び出し終了までは有効。新しい暗黙変換や寿命延長は追加しない。

`writeLine`はUTF-8全体とLFを出力してUnitを返し、文字列の解放責任は呼び出し側に残す。出力失敗、部分出力、flush、NUL、符号化、Symbol Identityは既存§22.4に従う。

### 6.2. 最適化の条件

直接渡される補間の一時文字列は、スタック領域などへ置き換えてよい。ただし補間式の型、適合先、Writerの意味上の型、選択済みの特殊化、一時値寿命を変えない。スタック領域を既存stringのStatic/Heapとして偽装せず、非脱出の記憶域・借用・解放省略を生成計画で検証する。

全補間が成功してから外部へ出力する。後続の書式化がAbortしたとき、補間の先頭だけを先に出力してはならない。ユーザー書式化自身の副作用は維持する。

除去した一時領域の確保・解放そのものの資源失敗は観測対象から除いてよい。必要長の上限検査、書式化失敗、ユーザーコードによる確保、その他のAbort・副作用の順序は維持する。

### 6.3. 性能の合格条件

| 経路 | 要求 |
| --- | --- |
| Window・UTF-8ビュー・アダプターの内部管理 | 追加ヒープ確保、管理用コールバック、参照カウント更新なし |
| 組み込み型＋固定長Writer | 実際に収まる場合は書式化処理のヒープ確保0回。中間string・boxing・引数配列なし |
| 組み込み型＋再利用Writer | 容量内では追加確保0回。`clear`で容量を再利用 |
| 容量を安全に事前確定できる動的な所有結果 | 確保1回以内、完成時コピーなし |
| `Console.writeLine("My number is \(n)")`、`n: i64` | 最適化有効時は本文最大33バイトを固定スタック領域に置き、補間処理のヒープ確保0回 |

具体型と標準Writerが確定する組み込み書式化は、最適化時に直接呼び出しまたはインライン化する。共有ジェネリック本体は既存§21.3に従い、間接呼び出しが残り得る。大きな確保処理・OS出力処理は共有し、無制限な特殊化を要求しない。

任意長の所有結果、容量を超える伸長、ユーザー実装・OS内部の確保にはゼロ確保を保証しない。標準の内部数値変換は、検証済みの直接書き込みで初期化範囲を一括更新してよい。コンパイラ内部の作業領域は、必要部分の初期化が証明できれば全面ゼロ初期化を省略できる。

## 7. 検証と適用範囲

実装時は、通常のネイティブ実行・確保カウンター・生成コードで次を検証する。NativeAOT試験は含めない。

- 整数の境界、UTF-8検証、NUL、空出力、浮動小数点の境界と表記。
- 各式・`format`が1回だけ実行され、拡張で再実行しないこと。出力先によって成功結果が変わらないこと。
- ちょうど収まる固定長領域の成功、1バイト不足の失敗、容量制限Window、無視された書き込みエラーの検出。
- 失敗後の追加出力禁止と、`$tryWrite`だけが後続式を短絡すること。通常の関数の直接・間接呼び出しの一致。
- Windowの二重確定、消費後使用、競合する再予約・伸長・入力aliasを拒否すること。未確定破棄で長さが増えないこと。
- 検証済みビューの競合書き換えを拒否し、確定したUTF-8に不完全な文字を含めないこと。
- 公開APIだけで上限付きWriterとユーザー書式化を実装でき、借用・破棄・再利用が成立すること。
- `writeLine`後も入力変数を使え、書式化失敗で補間本文を部分出力しないこと。§6.3の性能条件。

本案は主に既存§12.3.3、§22.1、§22.4、§22.5.5を置き換え、静的Contract、Origin・Loan、生成計画へ新宣言を接続する。一般の可変Slice、rawメモリ取得、隠れた共有バッファ、グローバルプールは追加しない。

本案の編集では`SPEC.md`と参照先仕様を変更しない。将来の統合では、旧`Stringify`要求と所有引数の`writeLine`要求を削除し、関連する例と検証条件を本案に合わせる。
