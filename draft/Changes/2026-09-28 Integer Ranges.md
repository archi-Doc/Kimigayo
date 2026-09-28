# 仕様変更案：整数範囲と位置範囲

日付: 2026-09-28

状態: 最終提案。正式仕様への取り込み・実装・新仕様の動作検証は未実施。

本書は採用後の変更内容を定める。取り込みまでは `docs/SPEC.md` とその参照先が正式仕様であり、本書だけで現行仕様を変更しない。明記しない事項には既存仕様を適用する。

## 1. 目的と変更概要

整数値の範囲を直接反復できるようにし、対象長に依存する位置指定と型で区別する。範囲を変数に保存しても、関数に渡しても意味は変わらず、`for` や添字内だけの特例は設けない。

| 変更前の名前 | 採用する名前 | 役割 |
| --- | --- | --- |
| 旧提案の `IntegerRange<T>` | `Range<T>` | 同じ整数型Tの確定した両端と包含指定。負数も扱い、Tの値を生成する。 |
| 旧提案の `IntegerRangeIterator<T>` | `RangeIterator<T>` | 整数範囲の反復状態を保持する。 |
| 現行仕様の非ジェネリック `Range` | `IndexRange` | 先頭相対・末尾相対・省略を含む位置指定。対象長に対して解決する。 |
| 現行仕様の `ResolvedRange` | 変更なし | 検査済みの非負の半開区間。安全な長さ計算とisizeの反復を提供する。 |

`Range<isize>` は他の整数型と同じ整数範囲であり、IndexRangeやResolvedRangeの代用にはしない。`Range<Index>` は導入しない。これらはKimi直下の宣言とし、旧名の互換エイリアスや、型引数を省略したRangeの別名は設けない。

```kimi
let numbers = -3..3                  // Range<i32>。-3から2まで。
let positions: Range<isize> = 0..4    // isizeでも直接反復できる。
let inner: IndexRange = 1..^1        // 対象の先頭と末尾を一つずつ除く。
let values: [4 of i32] = [10, 20, 30, 40]
let middle = values[inner]           // 20、30の共有Slice。
let resolved = inner.resolve(values.length) // ResolvedRange [1, 3)。

for var number in numbers
    Console.writeLine("Count \(number)")
```

## 2. 型と公開契約

### 2.1. 閉じた整数要件 `Kimi.Integer`

メソッド要求を持たないコンパイラー内在の要件Integerを導入する。適合するのは、外側のSemanticsがownerで、Coreが `i8/u8`、`i16/u16`、`i32/u32`、`i64/u64`、`i128/u128`、`isize/usize` のいずれかである型だけとする。浮動小数点・char・参照・ユーザー定義型は適合せず、同名宣言やユーザー適合でも付与できない。識別には正規の宣言IDを使う。

`T is Integer` は、定義時に次の能力を証明する。

- Tは整数Scalarであり、Copy・Owned・Equatable・Comparableを満たす。整数への参照の取得には既存のScalar read規則を使う。
- 全許容型に定義された組み込み整数演算・比較、およびIntegerを満たす型間の明示的な検査付き整数変換を利用できる。同型の被演算子、シフト右辺、更新対象、結果型、失敗条件の規則は変更しない。単項 `-` は符号付き整数に限られるため、Integerだけでは許可しない。
- 未確定リテラルは、すべての許容型に収まる0〜127に限りTへ適合できる。具体型が決まった位置では、その型の通常のリテラル適合規則を使う。

`Range<T>`・`RangeIterator<T>`の宣言と、これらを未確定のTで使う宣言には `T is Integer` を明記する。署名から制約を逆算しない。範囲型自身のCopy導出とEquatable実装も通常どおり検査する。

これは新しい組み込み証明規則である。既存の `T is i8 or u8 or …` だけでは、SPEC §8.7に場合分けの証明がないため共通の演算能力を導けない。一般的な型列挙・場合分け推論や、拡張可能なNumeric Contractは追加しない。

初期実装プロファイルのi128/u128除算・剰余の制限は、Integerの言語上の保証を弱めない。具体型が確定したコードを最適化前に診断する既知の実装制限として扱い、範囲の構築・反復・正規化をこれらの演算に依存させない。

### 2.2. 整数範囲 `Range<T>`

`Range<T>`はOwned・Copy・Equatableで、構築後は `start <= end` を満たす。比較順序や範囲の算術は提供しない。

| 公開API | 契約 |
| --- | --- |
| `start: T`、`end: T`、`isInclusive: bool` | 読み取り専用。構築時の両端と包含指定を保持する。 |
| `isEmpty: bool` | 終端非包含かつ `start == end` のときだけtrue。 |
| `tryCreate(! start: T, end: T, inclusive: bool = false) -> Option<Self>` | `start <= end` ならSome、逆転ならNone。引数評価中のAbortは捕捉しない。 |

通常の生成は範囲構文を正準形とし、同義の公開init / createは設けない。逆転した範囲式は構築時にAbortし、空範囲や降順へ読み替えない。`a..a` は空、`a..=a` は1要素とする。Tの最小値・最大値も使用でき、包含終端が最大値でも反復できる。

失敗を値で扱う場合は `Range<i32>.tryCreate(start: 0, end: 10)` を使う。コンテナーのTを引数から推論する新規則は追加しない。生成時の引数名は `inclusive:`、観測Propertyは `isInclusive` とする。

等価性は同じTのstart・end・isInclusiveを比較する。`0..3` と `0..=2`、位置の異なる空範囲は等しくない。異なるTや他の範囲型への暗黙変換・交差型等価性は設けない。

全整数域の要素数はTにもusizeにも収まるとは限らないため、lengthは提供しない。resolve / tryResolveも追加せず、スライスとの接続は§4で定める。

### 2.3. 反復と `RangeIterator<T>`

`Range<T>`は次の三つに適合し、昇順・刻み幅1でTの値を生成する。入口ごとに先頭から新しい反復を開始し、`for var` の反復変数を再代入しても生成順序は変わらない。

| 入口 | 結果 |
| --- | --- |
| `iterate(self: ref/Self)`（Iterable） | `RangeIterator<T>` |
| `iterateUniq(self: uniq/Self)`（UniqIterable） | `RangeIterator<T>` |
| `intoIterator(self: Self)`（IntoIterable） | `RangeIterator<T>` |

`RangeIterator<T>`はOwned・Non-Copyで、Iteratorに適合する。関連型ItemはT、`next(self: uniq/Self)` は `Option<T>` を返す。最終要素の後では加算せず、一度Noneになった後はNoneのままとする。取得した要素はIteratorのStorageやLoanに依存せず、Iteratorの効果上限（SPEC §22.1.2.4）を満たす。

三入口は境界を値として取り込み、元の範囲を変更せず、そのStorage・Origin・Loanを結果に保持しない。IterableとUniqIterableの各 `IteratorType(source)` は、どのsourceにも同じ`RangeIterator<T>`を対応させる。IntoIterableのIteratorTypeも同じ型とする。取得中の借用、Subjectの取得、早期終了時のcleanupには既存の反復規則を適用する。

進行状態の移譲には通常の `@move` を使い、再走査には範囲から新しいIteratorを得る。RangeIteratorの公開コンストラクターやIterable・UniqIterable・IntoIterable適合は追加しない。forで使う場合は既存のアダプターを通す。

```kimi
func makeNumbers() -> RangeIterator<i32>
    let numbers = 0..3
    return numbers.iterate() // ローカルnumbersの借用を結果に残さない。

for number in Kimi.Iteration.owned(makeNumbers())
    Console.writeLine("Count \(number)")
```

### 2.4. 位置指定 `IndexRange` と `ResolvedRange`

現行の非ジェネリックRangeをIndexRangeに改名する。整数境界の受理範囲を§3.3のとおり拡張し、それ以外はSPEC §4.6.2–4.6.4の契約を維持する。

| 型 | 境界と保証 | 長さ・反復 |
| --- | --- | --- |
| `IndexRange` | 読み取り専用の `start: Index`、`end: Index`、`isInclusive: bool`。省略開始は先頭相対0、省略終了は `^0`。構築時には順序を検査しない。 | 対象に依存しないlength・isEmptyはなく、直接反復できない。絶対位置だけでも同じ。 |
| `ResolvedRange` | 読み取り専用の `start: isize`、`end: isize`。常に `0 <= start <= end <= maximum isize` の半開区間。 | `length: isize = end - start`、`isEmpty: bool`。三つの反復入口からisizeを生成する。 |

両型ともOwned・Copy・Equatableで、対象のStorage・Origin・Loanを保持せず、Comparableや算術は提供しない。IndexRangeの等価性は正規化したIndexの方向・オフセットと包含指定、ResolvedRangeの等価性は両端で決める。例えば `..` と `0..^0` は等しい。型をまたぐ暗黙変換や等価比較はない。

IndexRangeは構文でのみ生成し、公開init / factoryは設けない。公開 `resolve(length: isize)` はResolvedRange、`tryResolve(length: isize)` は `Option<ResolvedRange>` を返す。負のlength、解決不能な境界、逆転、対象外の包含終端では、resolveはAbort、tryResolveはNoneとする。境界解決と半開区間への正規化は§4.2に従う。

ResolvedRangeの公開 `init(! start: isize, end: isize)` は上記の不変条件を検査し、不正ならAbortする。`values.indices` は引き続きResolvedRangeで、取得時の `[0, values.length)` のスナップショットとなる。保存後のサイズ変更は反映せず、添字・スライスに使う際には現在の対象長を再検査する。

## 3. 範囲構文と型推論

### 3.1. 種別の決定

まず省略の有無と、独立に判明する境界型から種別を決める。境界には整数またはIndexを要求し、整数への参照は通常のScalar readで取得する。

| 構文・境界 | 結果 |
| --- | --- |
| 両端が同じ整数型Tの `a..b` / `a..=b` | `Range<T>`。終端を除く／含む。 |
| 境界にIndexを含む | `IndexRange`。整数側は位置へ正規化する。 |
| 片側または両側を省略する | `IndexRange`。`a..`、`..b`、`..=b`、`..`。包含終端は省略できない。 |

種別決定のために未確定の整数式を先に既定化しない。省略もIndexもない整数式同士は整数範囲とし、期待型がIndexRangeでも再解釈しない。逆に、期待型が`Range<T>`でも位置指定を整数範囲に変えない。型は実行時の値や添字内かどうかに依存しない。

```kimi
let integers = 1..3                          // Range<i32>
let relative = 1..^1                         // IndexRange
let head = ..3                               // IndexRange。0..3とは異なる型。
let explicit: IndexRange = Index.init(1)..Index.init(3)
let invalid: IndexRange = 1..3               // 型エラー。
```

範囲演算子の優先順位・非結合性と接頭辞 `^` の優先順位は変更しない。`a..b..c` は構文エラー、`(a..b)..c` は境界型のエラーとする。構文は正規のKimi宣言を参照し、同名のユーザー宣言では置き換わらない。

### 3.2. 整数範囲の推論

**未確定の整数式**とは、未確定整数リテラル、括弧、およびそれらだけを被演算子とする既存の組み込み算術・ビット・シフト演算からなる、既定化前の式をいう。型付きの値・明示変換・呼び出しは含めない。これは期待型を伝えられる式の分類であり、新しい構文ではない。

`Range<T>`の両端には同じTを要求する。確定した境界型や期待型から未確定部分へ型を伝え、確定済みの異なる整数型は変換せずエラーとする。整数範囲の境界には、位置指定のisize期待型を適用しない。

両端が未確定の整数式である範囲式を**合成リテラル**とし、SPEC §4.3–4.4・§10.2・§12.3.1・§14.9.1の期待型伝播・候補適合・共通型推論に接続する。

1. 期待型、他の引数、配列の兄弟要素、分岐結果から、既存規則で得られる制約を先に集める。未解決の呼び出しを推測しない。
2. 候補の `Range<I>` へ直接値として適合するクラスはLiteral fittingとする。別途借用などが必要なら、その取得の適合クラスを使う。型付き境界や保存済みの範囲は確定型を保ち、他の適応がなければExactとなる。
3. 候補比較中は既定化せず、曖昧さの解消にも使わない。制約処理後に他の型情報がない場合だけTをi32に既定化する。
4. 境界内の呼び出しを外側の候補ごとに再束縛しない。既存の共通または選択済み期待型で決まらなければ注釈を要求する。型決定後の内部生成呼び出しで推論をやり直さない。

```kimi
let a = 0..10                       // Range<i32>
let b = 0@i64..10                   // Range<i64>
let c: Range<i64> = 0..10            // 未確定リテラルをi64に適合。
let n = 10
let d = 0..n@ref                    // Scalar read。Range<i32>
let ranges = [0..3, 5..8@i64]        // Array<Range<i64>>
let product: Range<i64> = 0..(2 * 5)
let shifted: Range<i64> = 0..(1 << 4)
```

`if ready => 0..3 else => 0..n`（readyはbool）でも、nの確定整数型を未確定の分岐へ伝える。`accept(Range<i32>)` と `accept(Range<i64>)` の両候補があるとき、`accept(0..10)` は曖昧となる。一方、`let r = 0..10` で独立に型を確定した後の `accept(r)` はi32側を選ぶ。

リテラルの適合と演算の実行は区別する。直接リテラルの括弧・符号にはSPEC §13.5.4を適用し、算術式全体を数学的な定数として適合させない。i8の期待型で `127 + 1` の各リテラルは適合するが、加算は実行時にAbortする。演算子の適用条件・中間結果の検査・シフト右辺の型決定も維持する。

### 3.3. 評価順と位置への正規化

範囲式は左境界、右境界を各1回評価し、両方の評価が完了してから次の構築検査を行う。境界式自体がAbortすれば、後続は評価しない。

| 種別 | 構築検査 |
| --- | --- |
| `Range<T>` | `start <= end` を検査し、逆転ならAbortする。 |
| `IndexRange` | 整数境界を左から「非負かつisizeで表現可能」な先頭相対Indexへ正規化し、不適合ならAbortする。順序・対象長による上限は解決時に検査する。 |

IndexRangeの整数境界は任意の `I is Integer` を受け付ける。未確定の整数式にはisize期待型を伝え、確定済みのIは変更しない。境界内の `^` / `Index.init` は、その式の評価中に通常の検査を行う。

```kimi
let a: i32 = 1
let b: i32 = 3
let numbers = a..b       // Range<i32>
let tail = a..           // IndexRange。aを位置へ正規化。
let inner = a..^1        // IndexRange
let last = ^(a@isize)    // Index。^の被演算子はisizeのまま。
```

接頭辞 `^`、公開 `Index.init(offset: isize, fromEnd: bool = false)`、単一要素添字・位置APIは既存のisize/Index規則を維持する。上のaに対する `values[a]` と `values[^a]` は型エラーとなる。範囲演算子の整数境界の拡張を、境界内の `^` にまで伝えない。

通常の式のAbortを、定数が見えるという理由でコンパイルエラーへ変えない。必須定数評価・型不適合・リテラル範囲外には既存の静的エラー規則を適用する。

## 4. 配列・添字・スライス

### 4.1. 受理する型

固定配列・Array・Sliceのスライスは `Range<I>`（`I is Integer`）・IndexRange・ResolvedRangeを受け付ける。Slice.trySliceの既存入口はIndexRangeへの改名後も維持し、整数範囲には次の単一ジェネリック宣言を追加する。12個の具体型別オーバーロードにはしない。

```kimi
// Slice<T>{source} 内の追加宣言。本文は省略。
public func trySlice<I>(self: Self, range: Range<I>) -> Option<Slice<T> during source>
    I is Integer
```

組み込みスライス添字も同じIの推論規則を使う。`slice.trySlice(1..3)` は他の制約がなければIをi32に既定化する。範囲添字は引き続きIndexable系列とは別であり、ユーザー型へ新しいキー型の適合を自動付与しない。

`[0..10]` は`Range<i32>`を1個含む配列であり、保存したrに対する `[r]` と同じく自動展開しない。単一要素添字はisize/Indexのままとする。`for i in 0..10` のiはi32なので必要なら明示変換し、添字走査の標準形には `for i in values.indices` を使う。`0..values.length` なら`Range<isize>`となり直接反復できる。

raw pointerの添字は従来どおり単一のisizeだけを受け付け、Indexおよび三つの範囲型を受け付けない。

### 4.2. 境界検査と半開区間への正規化

対象長Lは非負のisizeとする。IndexRangeでは各Indexの `offset <= L` を確認し、先頭相対ならoffset、末尾相対なら `L - offset` を得る。`^0` は終端境界であり、要素ではない。`Range<I>`では元の整数値を使う。いずれも成功条件を**数学的整数上**で次のように定める。

| 境界形式 | 成功条件 | 正規化後 |
| --- | --- | --- |
| 半開 | `0 <= start <= end <= L` | `[start, end)` |
| 包含 | `0 <= start <= end < L` | `[start, end + 1)` |

包含終端を半開区間へ変換する前の両端に順序条件を適用する。逆転を `end + 1` によって空区間として受理しない。ResolvedRangeを適用する場合も現在のLに対して半開の条件を検査する。

成功時は両端を正確にisizeで表し、包含終端の加算が溢れないことを保証する。比較回数や命令順序は固定しないが、切り詰めによる誤受理や、try系の検査中の変換による余分なAbortは許さない。成功条件・結果・評価と失敗の時点を保ちながら、正規化後のStorage・借用・Place処理を共有する。

直接記述した範囲と保存した範囲には同じ規則を適用する。受け手・境界を再評価せず、既存のアクセス予約・Loan規則を維持する。

### 4.3. 失敗と外部入力

通常のスライスは検査失敗でAbortし、trySliceは自身の検査失敗だけをNoneにする。引数評価中の失敗は捕捉しない。

| 操作 | 結果 |
| --- | --- |
| `values[-3..3]` | Rangeの構築は成功し、スライスでAbort。 |
| `slice.trySlice(-3..3)` | スライス検査に失敗してNone。 |
| `slice.trySlice(10..0)` | 呼び出し前のRange構築でAbort。 |
| `slice.trySlice(Index.init(10)..Index.init(0))` | IndexRangeの構築は成功し、解決時の検査でNone。 |

現行仕様では両端が整数でも位置指定となるため、負数は構築時にAbortし、逆転はtrySliceでNoneになる。整数範囲への切り替えで失敗の時点が変わる既存呼び出しを点検する。

外部入力は元の整数型のままtryCreateで順序を検査し、成功した範囲をtrySliceへ渡す。先にisizeへ変換して、回復可能な不適合をAbortへ変えない。

```kimi
func tryWindow<T, I>(values: Slice<T>, ! start: I, end: I) -> Option<Slice<T> during values.source>
    I is Integer
    return match Range<I>.tryCreate(start: start, end: end)
        .Some(let range) => values.trySlice(range)
        .None => .None
```

Abortコードの再割り当ては行わない。Kimiで書く構築検査には既存の `$abort(message)`（`KIMI_E_ABORT`）を使う。コード指定の内部組込み処理は追加せず、既存仕様の演算・変換・添字などのコードも維持する。

## 5. 実装と性能

### 5.1. 束縛・生成経路

現行の一律なisize/Index化を、種別決定・推論・適合・位置正規化へ分離する。具体整数型またはInteger要件が証明されたTを決めた後、宣言IDで特定する内部生成処理へ下げる。演算・比較・取得は定義時に検査し、具体化時に再束縛しない。

構築・tryCreate・反復は可能な限りKimiで実装し、通常のIteratorとモノモーフィゼーションを使う。構文からの生成入口はinternalとし、既存の公開 `Index.init(! unchecked:)` と、旧Rangeのbetween / from / to / all / through / upToも内部化する。

内部化した入口は、コンパイラーが合成する正規の生成処理だけに宣言IDと内部のアクセス規則で接続する。利用者の同名呼び出しにはアクセス権を与えない。境界の評価と必要な構築検査が完了する前に、不正な公開値を生成してはならない。

### 5.2. 位置の共通正規化

内部関数 `tryPosition<I>(value: I, limit: isize) -> Option<isize>` を `I is Integer` 付きで用意する。数学的条件 `0 <= value <= limit` を満たせば正確なisize値、それ以外ならNoneを返す。

- スライスでは対象長をlimitとし、§4.2の順序・包含終端の検査と組み合わせる。
- IndexRange構文では、コンパイラーがターゲットのisize最大値を通常の定数引数として渡す。新しい内部定数宣言や `isize.MaxValue` APIは設けない。

一般実装はvalueとlimitの負数を先に除外し、u128へ変換して比較した後、成功値をisizeへ変換する。現行ターゲットの全許容型を扱え、除算・剰余は不要となる。

頻出するisize・i32・usizeにはSPEC §8.8の明示的な完全関数特殊化を用意する。現行64-bitターゲットではisizeまたはusize上で検査し、O0でもu128を経由しない。追加の特殊化は測定で判断する。

特殊化も一般実装と同じ結果・失敗・評価契約を守り、不正なジェネリック定義を救済しない。構文からの内部呼び出しにも通常の選択規則を適用する。選択は型引数確定後なので、公開オーバーロードや推論の分岐を増やさない。

### 5.3. Iteratorと検査除去

公開Rangeの包含指定は等価性のため保持する。Iterator内部では、生成時に「最後に返す値」へ正規化できる。

| 元の範囲 | 初期状態 |
| --- | --- |
| 空の半開区間 | 終了状態。終端の減算はしない。 |
| 空でない半開区間 | 最終値は `end - 1`。`start < end` によりTで表現可能。 |
| 包含区間 | 最終値はend。 |

活動中は `start <= current <= last` を保ち、nextでは現在値を一度取得して比較・加算・返却に使う。

```kimi
// RangeIterator<T>.nextの本文例。
require not self.done else => return .None
let value = self.current
if value < self.last => self.current = value + 1
else => self.done = true
return .Some(value)
```

`value < last` から `value + 1` の非オーバーフローを証明できる。通常の検査付き加算を使い、非検査プリミティブやIterator宣言の特別扱いは追加しない。O2で検査が除去されるかは生成コードで確認し、物理レイアウトは固定しない。

境界の検査は、§4.2の意味を保つ限り統合・除去できる。幅を狭める前に表現可能性を検査する。負数へのソース上の `@usize` はAbortし得るため、内部の符号なし比較による最適化と同一視しない。

`for i in 0..values.length` 内の `values[i]` も、同じ長さ・Storageが維持されることを証明できれば検査除去の対象となる。評価順・Abort・借用・反復の副作用を保ち、一般のユーザー定義Iteratorへ無条件に適用しない（SPEC §4.6.8）。

### 5.4. 計算量と測定

固定したTについて、構築・tryCreate・範囲値のCopy・反復開始・各next・破棄・境界正規化はO(1)、n要素の反復はO(1+n)、Iteratorの追加領域はO(1)とする。これら自身のヒープ確保・参照カウント更新はゼロとし、境界式や本体などのユーザー処理は別に数える。

標準呼び出しのインライン化、Optionの中間コピー除去は既存の最適化条件に従う。整数反復のホットパスと完了時に時間・割当量を測定し、数値としての性能改善は測定後に記録する。

## 6. 取り込みと検証

### 6.1. 変更対象

| 対象 | 反映内容 |
| --- | --- |
| SPEC §3.5.3・§8.4.7・§8.7 | Integerの識別・閉じた適合・演算とScalar readの証明規則。 |
| SPEC §4.3–4.4・§10.2・§12.3.1・§14.9.1 | 未確定の整数式・合成リテラル・候補適合・期待型伝播・既定化。 |
| SPEC §4.6・§5.3・§13.2 | IndexRangeへの改名、三型の契約、範囲構文、位置正規化、スライスの受理型。^と単一添字の規則を維持。 |
| SPEC §14.6.2・§17.3.1・§22 | Range・RangeIterator・Integer、反復三入口と寿命、逆転時のAbort、公開API。§22.5.4のコード指定規則は維持。 |
| Kimi・Compiler | Core・反復・Slice、宣言IDと型識別、`Binding.Ranges.cs`・`Binding.Sequences.cs`・推論・制約証明・`BodyLowering.Sequences.cs`など。 |
| 公開索引・規約 | `docs/LIBRARY.md` の宣言一覧と内部化したAPI、`docs/STYLE.md` の旧Range名、`src/Kimi/Library/README.md` の実装方針を整合させる。 |
| 用語・関連文書 | SPEC索引、付録Eの用語、付録F、§12–15、実装仕様の旧Range名と関連規則を点検する。 |
| 例・テスト | 範囲・数値推論・反復・スライス・所有権・補間、およびMilestone 2/7/27/28/32/39などを更新する。 |

型名の一括置換だけでは、整数境界の意味変更を反映できない。既存コードは用途を確認して更新する。

- `let r: Range = 1..3` は、整数範囲なら `Range<isize>` など必要な整数型を指定する。位置指定なら `let r: IndexRange = Index.init(1)..Index.init(3)` とする。
- `(0..n).resolve(n)` は目的に応じて直接反復・`values.indices`・明示したIndexRangeの解決へ置き換える。旧Rangeとの等価比較や既定isizeへの依存も点検する。
- `(-1)..sideEffect()` は、右境界が同じ整数型で-1以上なら有効な整数範囲となる。`^(-1)..sideEffect()` が右境界の評価前にAbortする規則は維持する。

pre-alphaのため言語バージョン分岐や移行用構文は追加しない。正式仕様・公開索引は対応する実装単位で更新する。正式仕様は本書を参照せず自己完結させ、取り込み後に `draft/INTEGRATED.md` へ対象節またはコミットを記録して本書を固定する。

### 6.2. 受け入れ条件

| 分類 | 検証事項 |
| --- | --- |
| 名前と型 | `Range<T>`・`RangeIterator<T>`・IndexRangeの宣言と構文の接続、旧名の除去、`Range<Index>`の拒否。`Range<isize>`の負数・反復とIndexRangeの非反復を区別する。 |
| 構築 | 全12整数型、負数、空、1要素、逆転、最小・最大値近傍、包含最大値、等価性、tryCreateと名前付き引数の評価順。巨大範囲の全走査は不要。 |
| Integer | ユーザー適合・参照型の拒否。Integerだけを仮定した演算・取得・変換、0・1・127の適合と128・-1の拒否、単項マイナスと制約不足の定義時拒否。既知の128-bit実装制限は言語上の拒否と区別する。 |
| 推論 | 型付き・参照境界、異型の拒否、Literal fittingとExact、曖昧性、兄弟要素・分岐結果、保存前後、境界内の呼び出し、単一ジェネリックtrySlice。リテラルだけの算術・ビット・シフト式への期待型伝播と演算時Abort。 |
| 位置・評価 | 任意整数型のIndexRange境界、isize期待型、負数・表現不能値、左右の評価・検査順。^と単一位置APIの型制約、内部入口へのアクセス拒否。 |
| 反復 | 三入口、再走査、Iteratorの独立した寿命、Copy拒否とMove、for var再代入、終了後None、exit/continue・cleanup、最大値の後で加算しないこと。 |
| 解決・スライス | 省略・末尾相対・^0、負の解決長、保存／直接記述の一致、符号・幅・対象長・包含境界、逆転を空にしないこと、ResolvedRangeの再検査、tryCreateからの接続と失敗段階、借用・予約・Indexable・raw pointerの境界。 |
| コスト | 確保数ゼロ、O(1)の初期化とnext、O0/O2の意味の一致、正規化の一般実装と特殊化の結果・失敗の一致。頻出3型でO0でもu128を経由しないこと、O2の加算検査除去を生成コードで確認する。 |

### 6.3. 実装順序

1. **位置指定を改名する。** 現行RangeをIndexRangeに改名し、宣言・構文の生成先・API・既存例・テストを同じ単位で揃える。この段階では範囲構文の意味を変えない。
2. **整数範囲の利用側を追加する。** Integer、`Range<T>`とtryCreate、RangeIteratorと三入口、共通正規化と頻出型の特殊化、スライスの受理を実装する。構文切り替え前はtryCreate経由で検証する。
3. **構文を切り替える。** 種別決定・合成リテラル推論・位置正規化を有効にし、既存の呼び出し・例・Milestoneを更新する。内部化とその生成経路も揃える。
4. **共通処理と性能を検証する。** ResolvedRangeの保証を維持して不要な処理を整理し、検査・反復の共有と最適化を測定する。

各段階は必要に応じて整合した検証単位へ分ける。実装単位ごとに `./scripts/verify.ps1 -Class ...` と関連O0/O2 fixture・Milestoneで検証してコミットし、実装セッション末尾に `./scripts/verify.ps1 -Mode Session` を実行する。NativeAOTは対象外とする。

`docs/dev/PLAN.md`・`docs/dev/PLAN_HISTORY.md` は実装時に進捗を記録し、`docs/STATUS.md` は検証済みの対応範囲が変わった時だけ更新する。本提案書だけの更新では、正式仕様・実装・STATUSを変更しない。

## 7. 対象外と参照

降順・任意刻み・無限範囲、配列の自動展開、範囲値のUtf8Format適合、一般Numeric Contract、単一要素添字の整数型拡張、API横断のAbortコード統一と診断改善は対象外とする。

- [範囲・添字・スライス](../../docs/spec/04-arrays-indexing-and-slices.md)
- [整数型とScalar read](../../docs/spec/03-types-and-values.md)
- [ジェネリックと制約](../../docs/spec/08-generics-constraints-and-contracts.md)
- [オーバーロードと推論](../../docs/spec/10-overload-resolution-and-inference.md)
- [反復と取得規則](../../docs/spec/14-control-flow.md)、[Kimiの反復Contract](../../docs/spec/22-core-execution-and-foreign-functions.md)
- [コード生成の実装プロファイル](../../docs/impl/21-layout-runtime-and-code-generation.md)、[検証済みの対応範囲](../../docs/STATUS.md)
- [公開宣言](../../docs/LIBRARY.md)、[コーディング規約](../../docs/STYLE.md)
