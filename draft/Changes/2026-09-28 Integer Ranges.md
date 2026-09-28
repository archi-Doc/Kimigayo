# Design Change: 整数範囲

日付: 2026-09-28

状態: 合意した設計方針に基づく変更提案。正式仕様への取り込み・実装・新仕様の動作検証は未実施。

本書で変更する事項は `SPEC.md` とその参照先より優先し、変更しない事項には既存仕様を適用する。

## 1. 現状と目的

現在の `0..10` は、対象の長さに対する未解決の区間指定 `Range` を作る。整数境界は `isize` から `Index` に正規化される。`Range` は反復できず、非負の確定区間 `ResolvedRange` だけが反復できる（SPEC §4.6.2–4.6.4）。

一般の整数範囲 `IntegerRange<T>` を導入し、負数や整数型を保ちながら、次のように反復できるようにする。変数への保存や関数への受け渡しでも同じ意味を保ち、`for` だけの特例は設けない。

```kimi
let numbers = -3..3
for var x in numbers
    Console.writeLine("Count \(x)") // Count -3 ～ Count 2
```

## 2. 型と共通要件

### 2.1. 三つの範囲型

| 型 | 保証・役割 | 単独の反復 |
| --- | --- | --- |
| `IntegerRange<T>`（新設） | 同じ整数型Tの確定した両端と包含指定。負数も通常の値として扱う。 | Tの値を生成する。 |
| `Range` | 先頭相対・末尾相対・省略を含む位置の指定。対象長に対して解決する。 | 不可。 |
| `ResolvedRange` | 非負・順序保証付きの半開区間。両端はisizeで、`length = end - start` は常に表現可能。 | `isize`を生成する。 |

三つの型を維持し、`Range.resolve` / `tryResolve` と `values.indices` の結果も `ResolvedRange` のままとする。適用先の長さは再検査するが、非負性・順序・安全な長さ計算の保証は失われない。`IntegerRange<isize>` への統合や `Range` の改名は行わず、検査・反復の内部処理を共有する。

### 2.2. 閉じた整数要件 `Kimi.Integer`

メソッド要求を持たないコンパイラー内在の要件 `Kimi.Integer` を導入する。満たすのは、外側のSemanticsがownerで、Coreが `i8/u8`、`i16/u16`、`i32/u32`、`i64/u64`、`i128/u128`、`isize/usize` のいずれかである型だけとする。浮動小数点・`char`・参照・ユーザー定義型は満たさない。同名宣言やユーザー適合では付与できず、コンパイラーが認識する宣言IDで識別する。

`T is Integer` から、次の能力を定義時に証明できるようにする。

- Tは整数Scalarであり、Copy・Owned・Equatable・Comparableを満たす。参照からのScalar readには既存の取得規則を使う。
- 同じTの組み込み整数演算・比較と、Integerを満たす型間の明示的な検査付き整数変換を利用できる。演算の結果型・失敗条件は既存規則に従い、暗黙の拡幅や異型間演算は追加しない。
- 未確定リテラルは、すべての許容型に収まる場合にTへ適合できる。この共通域は0〜127。具体型が決まっている場合は、その型の通常のリテラル適合規則を使う。

`IntegerRange<T>`・`IntegerRangeIterator<T>` と、それらを型引数未確定のまま使う宣言には `T is Integer` を明記する。型を署名に書いたことから制約を逆算しない。範囲型自身のCopy導出やEquatable実装は通常どおり検査する。

これは新しい組み込み証明規則である。既存の `T is i8 or u8 or …` という選択制約だけでは、§8.7に場合分けの証明がないため共通の演算能力を導けない。一般的な型列挙・場合分け推論や、ユーザーが拡張するNumeric Contractは導入しない。

## 3. 整数範囲の仕様

### 3.1. 構文と型推論

| 境界 | 結果 |
| --- | --- |
| 同じ整数型Tの `a..b` / `a..=b` | `IntegerRange<T>`。終端を除く／含む。 |
| 境界に `Index` を含む | `Range`。整数側は位置として正規化する（§3.2）。 |
| 片側または両側を省略する | `Range`。省略開始は0、省略終了は `^0`。包含終端の省略は不可。 |

まず構文と独立に判明する境界型から種別を決める。整数への参照は組み込み演算子と同じScalar readで終端の整数型を調べる。省略もIndexもない未確定整数リテラル同士は整数範囲とする。期待型が `Range` でも再解釈せず、添字内だけの型決定や実行時の値による型変更も行わない。

整数範囲では両端に同じTを要求する。片端の確定型は他端の未確定リテラルに伝わるが、異なる確定済み整数型はエラーとする。整数範囲の境界には、§4.6.2の位置に対するisize期待型を適用しない。

両端が未確定整数リテラルだけの範囲式を、配列リテラルと同様の**合成リテラル**として新たに定義し、SPEC §4.3–4.4・§10.2・§14.9.1の共通規則に接続する。

- 候補の `IntegerRange<I>` へ直接値として適合する場合のクラスはLiteral fitting。別途借用などが必要なら、その取得の適合クラスを用いる。型付き境界や保存済みの範囲は確定型を保ち、他の適応がなければExactとなる。
- 期待型・他の引数・配列の兄弟要素・分岐結果から得られる制約を、既定化より先に集める。既存規則が認める型情報だけを使い、未解決の呼び出しを推測しない。
- 候補比較中は既定化せず、曖昧さの解消にも使わない。制約処理後に他の型情報がない場合だけ、Tをi32に既定化する。
- 境界内の呼び出しを外側の候補ごとに再束縛しない。既存の共通または選択済み期待型で決まらなければ、注釈を要求する。

例えば `accept(IntegerRange<i32>)` と `accept(IntegerRange<i64>)` の両方がある場合、`accept(0..10)` は曖昧であり、先にi32へ既定化しない。`let r = 0..10` で独立に型を確定した後の `accept(r)` はi32側を選ぶ。

```kimi
let a = 0..10                       // IntegerRange<i32>
let b = 0@i64..10                   // IntegerRange<i64>
let c: IntegerRange<i64> = 0..10     // 未確定リテラルをi64に適合
let n = 10
let d = 0..n@ref                    // Scalar read。IntegerRange<i32>
let e = 1..^1                       // Range
let f = ..10                        // Range。0..10へ読み替えない。
let ranges = [0..3, 5..8@i64]        // Array<IntegerRange<i64>>
```

`if c => 0..3 else => 0..n` でも、nの確定整数型がリテラルだけの分岐に伝わる。型決定後に範囲構築へ下げ、合成した内部呼び出しで型推論をやり直さない。

### 3.2. 位置としての整数

`Range` の整数境界と接頭辞 `^` の被演算子は、任意の `I is Integer` を受け付ける。位置への正規化時に「非負かつisizeで表現可能」を検査し、失敗ならAbortする。未確定リテラルにはisize期待型を与え、確定済みのIは変更しない。Indexは引き続き `offset: isize` を保持する。

Range式は左境界、右境界を各1回評価した後、整数境界を左から検査・正規化する。境界式内の `^` / `Index.init` はその評価中に検査し、失敗すれば後続を評価しない。対象長による順序・上限の検査はRangeの解決時に行う。

```kimi
let a: i32 = 1
let b: i32 = 3
let whole = a..b  // IntegerRange<i32>
let tail = a..    // Range。aを位置へ検査付きで正規化。
let inner = a..^1 // Range
let last = ^a     // Index
```

公開 `Index.init(offset: isize, fromEnd: bool = false)` は維持する。独自の型引数を持つコンストラクターや、単一要素添字の整数型拡張は追加しない。位置の受け付けの拡張は、Range構文と接頭辞 `^` の共通規則に限定する。

### 3.3. 整数範囲の構築と失敗

- 範囲式は左境界、右境界を各1回評価し、完了後に順序を検査する。評価自体がAbortすれば以降は評価しない。
- 開始値 > 終了値なら構築時にAbort。空範囲や降順へ読み替えない。`a..a` は空、`a..=a` は1要素。
- 負数と各Tの最小値・最大値を許す。包含終端に最大値を指定しても構築・反復できる。
- 通常の式のAbortを、定数が見えるという理由でコンパイルエラーに変えない。必須定数評価、型の不適合、リテラル範囲外には既存の静的エラー規則を適用する。

### 3.4. 公開API

| 宣言 | 保証 |
| --- | --- |
| `IntegerRange<T>` | Owned、Copy、Equatable。比較順序や範囲の算術は追加しない。 |
| `start: T`、`end: T`、`isInclusive: bool` | 読み取り専用。構築時の境界と包含指定を保持する。 |
| `isEmpty: bool` | 終端非包含かつ開始と終了が等しいときだけtrue。 |
| `tryCreate(! start: T, end: T, inclusive: bool = false) -> Option<Self>` | 有効ならSome、逆転ならNone。通常の引数評価順に従い、引数評価中のAbortは捕捉しない。 |

通常の生成は範囲構文を正準形とし、同義の公開 `init` / `create` は追加しない。`tryCreate` は失敗を値で扱う入口であり、`IntegerRange<i32>.tryCreate(start: 0, end: 10)` のように使う。コンテナーのTを引数から推論する新規則は設けない。引数名は既存の生成処理と揃えて `inclusive:`、観測Propertyは `isInclusive` とする。

等価性は同じTの `start`・`end`・`isInclusive` を比較する。`0..3` と `0..=2` は異なり、異なる位置の空範囲も同一視しない。異なるTや他の範囲型への暗黙変換・交差型等価性は追加しない。

`length` は設けない。全整数域の要素数はTにも `usize` にも収まるとは限らない。範囲値の `Utf8Format` 適合も今回追加しない。

### 3.5. 反復と寿命

昇順、刻み幅1でTの値を生成する。各反復は先頭から始まり、`for var x` のxを再代入しても生成順序は変わらない。

| 入口 | 返す型 |
| --- | --- |
| `iterate(self: ref/Self)`（Iterable） | `IntegerRangeIterator<T>` |
| `iterateUniq(self: uniq/Self)`（UniqIterable） | `IntegerRangeIterator<T>` |
| `intoIterator(self: Self)`（IntoIterable） | `IntegerRangeIterator<T>` |

`IntegerRangeIterator<T>` はOwnedで `Iterator` に適合し、`Item` はT、`next(self: uniq/Self)` の結果は `Option<T>` とする。公開コンストラクターやIntoIterable適合は追加せず、標準Iteratorと揃える。Iteratorを直接forに渡す場合は既存のアダプターを使う。

三入口は境界の値を取り込み、元の範囲を変更せず、そのStorageやOriginを結果に保持しない。借用入口の関連型 `IteratorType(source)` は、どのsourceにも同じ `IntegerRangeIterator<T>` を返す。取得中の借用とSubjectの取得規則は維持し、呼び出し後は結果のために元の範囲のLoanを延長しない。

```kimi
func makeNumbers() -> IntegerRangeIterator<i32>
    let numbers = 0..3
    return numbers.iterate() // ローカルnumbersを借用し続けない。

for x in Kimi.Iteration.owned(makeNumbers())
    Console.writeLine("Count \(x)")
```

進行状態はIteratorだけが持つ。成功した `next` の値はIteratorから独立し、終了後は `None` のままとする。最終要素の後で加算せず、早期終了時のcleanupは通常の反復規則に従う。

## 4. 配列・スライスとの接続

### 4.1. 配列と単一要素の添字

`[0..10]` は `IntegerRange<i32>` を1個含む配列で、自動展開しない。範囲を変数rに保存した `[r]` と同じ意味になる。

単一要素の添字は既存の `isize` / `Index` 規則を維持する。`for i in 0..10` のiは `i32` なので、必要なら明示変換する。添字走査の標準形は `for i in values.indices`（iは `isize`）とする。

raw pointerの添字は従来どおり単一のisizeだけを受け付け、`IntegerRange`・`Range`・`ResolvedRange` は受け付けない。

### 4.2. スライスの共通検査

組み込みの固定配列・Array・Sliceのスライスと `Slice.trySlice` に整数範囲を追加する。整数範囲用のAPIは `I is Integer` を持つ単一のジェネリック宣言とし、12個の具体型別オーバーロードにはしない。既存のRange用・ResolvedRange用の入口は維持する。

例えばSliceの追加宣言は次の形とする。組み込みスライス添字も同じIの規則で型付けする。

```kimi
public func trySlice<I>(self: Self, range: IntegerRange<I>) -> Option<Slice<T> during source>
    I is Integer
```

これにより `slice.trySlice(1..3)` は、他の制約がなければIをi32に既定化できる。

対象長Lは非負のisizeとする。成功条件を**数学的整数上の条件**として定め、比較回数や機械命令の順序は規定しない。

| 整数範囲 | 成功条件 | 正規化後 |
| --- | --- | --- |
| 半開 | `0 <= start <= end <= L` | `[start, end)` |
| 包含 | `0 <= start <= end < L` | `[start, end + 1)` |

この条件から境界がisizeで表現可能と分かった後に変換し、包含終端の加算も安全なisize上で行う。境界の切り詰めや、Lを狭いIへ変換する実装は禁止する。結果はResolvedRangeと同じ半開区間に正規化し、以後のStorage・借用・Place処理を共有する。

直接書いた範囲と保存した範囲に同じ規則を適用する。受け手・境界を再評価せず、既存のアクセス予約・Loan規則を保つ。ユーザーのIndexable実装に新しいキー型の適合は自動付与しない。

### 4.3. 失敗の責任と外部入力

通常のスライスは不適合でAbortし、`trySlice` は自身の検査失敗だけをNoneにする。引数評価中の失敗は捕捉しない。

| 操作 | 結果 |
| --- | --- |
| `values[-3..3]` | 整数範囲の構築は成功。スライス操作でAbort。 |
| `slice.trySlice(-3..3)` | スライス検査に失敗しNone。 |
| `slice.trySlice(10..0)` | 呼び出し前の範囲構築でAbort。 |

従来は負数がRange構築時にAbortし、逆転は `trySlice` でNoneになっていた。この変化は既存呼び出しの見直しが必要となる副作用である。Range境界は非負の**位置**、整数範囲の境界は負数も許す**値**として扱う。

外部入力は元の整数型を保ったまま `tryCreate` で検査し、成功した範囲を `trySlice` に渡す。先にisizeへ明示変換してAbortさせない。

```kimi
func tryWindow<T, I>(values: Slice<T>, ! start: I, end: I) -> Option<Slice<T> during values.source>
    I is Integer
    return match IntegerRange<I>.tryCreate(start: start, end: end)
        .Some(let range) => values.trySlice(range)
        .None => .None
```

実行時の失敗コードは、範囲・位置の生成時の不正引数に `KIMI_E_ARG_RANGE`、対象長に対する解決・スライス失敗に `KIMI_E_INDEX_BOUNDS` を使う。前者には整数範囲の逆転、位置の負数・isize範囲外、ResolvedRangeの不正境界を含める。明示的な整数変換は既存の `KIMI_E_INT_CONVERSION`、try系の自身の失敗はNoneとする。これは失敗契約の整合であり、診断基盤の拡張は含まない。

## 5. 実装・性能の方針

### 5.1. 共通処理と定義時の検証

- 現行 `Binding.Ranges.cs` の一律なisize/Index化を、種別決定・推論・適合・位置正規化に分ける。具体整数型またはInteger要件が証明されたTを決めてから、宣言IDで特定した内部生成経路へ下げる。
- 構築・tryCreate・反復は可能な限りKimiで実装する。新たな専用LLVMループより、通常のIteratorとモノモーフィゼーションを使う。
- 演算・比較・取得はIntegerの証明規則を用いて定義時に検査し、具体型の呼び出しごとに再束縛しない。128-bitを含む実装上の不足は対象型を減らして回避せず、実装課題とする。
- 位置とスライスの境界正規化は共通の内部処理にまとめる。Abortコードは宣言・失敗種別で識別し、メッセージ文字列の照合に依存しない。

スライスのKimi製基準実装では、Iで非負性を検査した後、境界とLをu128へ変換して比較し、成功後だけisizeへ変換できる。現行ターゲットでは全許容整数型の非負値をu128で表せるため、回復可能な整数変換や異型間比較を新設せずに実装できる。位置の正規化にも、コンパイラーが把握するisize上限を用いて同じ内部検査を使う。特殊化後は§5.3に従って比較幅を縮められる。

構文からの生成入口はinternalとし、利用者が検証を迂回して不正な値を得られないようにする。既存の公開 `Index.init(! unchecked:)` と、仕様にない公開 `Range.between` 等も内部化する。前者は検証迂回、後者は生成APIの重複を解消するためである。

internal化だけでは、利用者のスコープで通常の呼び出しとして束縛する現行loweringが失敗し得る。コンパイラーが合成する既知の生成処理だけを、正規の宣言IDと内部のアクセス規則で接続する。一般のソース呼び出しにアクセス権を与えない。境界の評価・必要な検査が完了するまで、公開値を生成しない。

### 5.2. Iterator内部の終端正規化

公開範囲の包含指定は等価性のため保持する。一方、Iteratorでは生成時に「最後に返す値」へ正規化できる。

| 元の範囲 | 初期状態 |
| --- | --- |
| 空の半開区間 | 最初から終了。終端の減算をしない。 |
| 空でない半開区間 | 最終値は `end - 1`。`start < end` が成立するのでTで表現できる。 |
| 包含区間 | 最終値はend。 |

`next` は、終了済みならNoneを返す。それ以外は現在値を返す値として確保し、最終値と等しければ終了状態へ、異なれば現在値を1増やす。活動中は `start <= current <= last` を保つため、`current != last` の分岐では `current < last` となり、加算は溢れない。

この不変条件を、検査付き加算の検査を除去する根拠として認める。新しい非検査の後続値プリミティブは追加しない。要素数の差分・包含最大値への加算・反復ごとの包含分岐を避ける実装モデルであり、物理レイアウトは固定しない。

### 5.3. スライスとループの検査除去

数学的な成功条件と同値なら、検査の統合・除去を許す。isize以下の幅では適切な符号拡張・ゼロ拡張と符号なし比較を利用できる。幅の広い型は表現可能性を先に確認し、切り詰めで検査を代用しない。負数へのソース上の `@usize` は検査付き変換でAbortし得るため、この内部最適化と同一視しない。

`for i in 0..values.length` 内の `values[i]` も、生成値の範囲を境界検査除去の根拠にできる。ただし同じ長さ・同じStorageが維持され、評価順・Abort・借用・反復の副作用を変えないことを証明する。一般のユーザー定義Iteratorへ無条件には適用しない（§4.6.8）。

### 5.4. 計算量と測定

固定したTについて、構築・tryCreate・Copy・反復開始・各next・破棄・スライスの境界正規化はO(1)、n要素の反復はO(1+n)、Iteratorの追加領域はO(1)とする。これら自身のヒープ確保・参照カウント更新はゼロとし、境界式・本体などのユーザー処理は別に数える。

整数反復のホットパスと完了時に時間・割当量を測定する。標準呼び出しのインライン化、Optionの中間コピー除去を既存の最適化条件で許す。数値としての性能改善は測定後に記録する。

## 6. 既存仕様・実装への影響

| 領域 | 必要な変更 |
| --- | --- |
| 型・証明 | §3.5.3にIntegerのScalar証明と範囲境界のScalar read、§8.4.7・§8.7にIntegerの識別・閉じた適合・証明規則を追加する。 |
| 推論 | §4.3–4.4・§10.2・§14.9.1に合成リテラルとしての接続を追加し、候補適合・既定化・共通結果型を揃える。 |
| 範囲・位置 | §4.6.2–4.6.4に三型の役割・位置正規化・失敗の段階を反映する。「負の範囲は利用できない」を改め、適合が値や綴りに依存しない原則は維持する。§5.3でraw pointerの範囲添字を明示的に除外する。 |
| 反復・最適化 | §14.6.2・§22に三入口と寿命を追加し、§4.6.8に不変条件に基づく検査除去を反映する。 |
| 宣言・失敗契約 | §22.1にInteger・IntegerRange・IntegerRangeIteratorと追加API、§17.3.1に逆転時のAbort、§22.5.4に本書§4.3のコード対応を追加する。ARG_RANGEの既存のFormatting用途は維持する。 |
| Kimi・公開索引 | Core・反復・Sliceの宣言と内部生成経路を更新し、同じ単位で `LIBRARY.md` に公開宣言の追加・削除を反映する。実装方針はKimiのREADME、規約を変更する場合はSTYLEも整合させる。 |
| Compiler | `Binding.Ranges.cs`、`Binding.Sequences.cs`、期待型推論、制約証明、ライブラリ宣言ID・型識別、`BodyLowering.Sequences.cs`等を整合させる。 |
| 関連文書 | §12–15の取得・評価規則、実装仕様、付録E/FとSPECの索引を点検する。規則は上記の所有箇所に置き、重複を避ける。 |
| 例・テスト | 範囲・数値推論・反復・スライス・所有権・補間を更新する。Milestone 2/7/27/28/32/39等の関連コード・期待値・ハーネスを確認する。 |

既存例は次のように改める。

- `let r: Range = 1..3` は、位置指定が必要なら `Index.init(1)..Index.init(3)` と明示する。値の範囲ならIntegerRangeとして保持する。
- §14.6.2の `(0..n).resolve(n)` は、目的に応じて `0..n` の直接反復または `values.indices` にする。旧Rangeとの等価比較や既定isizeへの依存も点検する。
- §4.6.4の `(-1)..sideEffect()` は、終端が-1以上なら有効な整数範囲となる。`^(-1)..sideEffect()` が終端評価前にAbortする規則は維持する。

pre-alphaのため移行用の構文や言語バージョン分岐は追加しない。実装時にPLAN・PLAN_HISTORYへ進捗を記録し、STATUSは検証済みの対応範囲が変わった時だけ更新する。正式仕様へ取り込んだら `draft/INTEGRATED.md` に記録して本書を固定する。

## 7. 検証と実装順序

### 7.1. 受け入れ条件

| 対象 | 検証事項 |
| --- | --- |
| 構築 | 全整数型、負数、空、1要素、逆転、最小・最大値近傍、包含最大値、tryCreateの成否と名前付き引数の評価順。巨大範囲の全走査は不要。 |
| Integer要件 | 12整数型だけが適合し、ユーザー適合・参照型を拒否すること。ジェネリック内の演算・取得・変換・0/1の適合、制約不足の定義時拒否、具体化後も同じ意味となること。 |
| 推論 | 型付き境界、期待型、参照境界、異型の拒否、Literal fittingとExact、オーバーロードの曖昧性、兄弟要素・分岐結果、保存前後の既定化、境界内の呼び出し、単一ジェネリックtrySlice。 |
| 位置・生成経路 | 任意整数型のRange境界・^、未確定リテラルのisize適合、負数・表現不能値、両境界の評価と検査の順序。内部生成入口へ利用者がアクセスできないこと。 |
| 反復 | 三入口、同じ範囲の再利用、元範囲の寿命から独立したIterator、for var再代入、終了後None、exit/continue・cleanup、整数最大値で余分な加算がないこと。 |
| スライス | 保存・直接記述で同じ結果、符号・幅・対象長の検査、空・包含境界、tryCreateからの接続、失敗段階とAbortコード、借用・アクセス予約、既存Indexableの選択、raw pointerの拒否。 |
| コスト | 空・短い区間・最大値近傍で確保数ゼロ、O(1)の初期化とnext。O0/O2で意味が一致すること。 |

### 7.2. 作業単位

1. **利用側を先に追加する。** Integer要件、IntegerRange・tryCreate、Iteratorと三入口、スライスの受け付けを実装する。範囲構文は切り替えず、tryCreate経由で検証する。必要なら、既存コードが通る複数の検証単位に分ける。
2. **構文を切り替える。** 範囲式の種別・合成リテラル推論・位置正規化を変更し、同じ単位で既存の呼び出し、例、Milestone、テストを更新する。internal化に必要なloweringも揃える。
3. **内部処理を整理する。** ResolvedRangeは維持し、不要になったloweringを除去する。検査・反復処理の共有と最適化を進め、性能を測定する。

正式仕様・公開索引は対応する実装単位で更新する。各単位は `verify.ps1 -Class ...` と関連O0/O2 fixture・Milestoneで検証してコミットし、実装セッション末尾に `-Mode Session` を実行する。NativeAOTは対象外。本書だけの更新では正式仕様・実装・STATUSを変更しない。

## 8. 対象外と参照

診断改善は今回の対象外とする。降順・任意刻み・無限範囲、配列の自動展開、範囲の自動文字列化、一般Numeric Contract、単一要素添字の整数型拡張も含めない。

- [範囲・添字・スライス](../../spec/04-arrays-indexing-and-slices.md)
- [整数型とScalar read](../../spec/03-types-and-values.md)
- [ジェネリックと制約](../../spec/08-generics-constraints-and-contracts.md)
- [オーバーロードと推論](../../spec/10-overload-resolution-and-inference.md)
- [反復と取得規則](../../spec/14-control-flow.md)
- [Kimiの反復Contract](../../spec/22-core-execution-and-foreign-functions.md)
- [公開宣言](../../LIBRARY.md)、[コーディング規約](../../STYLE.md)
