# Design Change: 範囲の型分割と静的contractによる共通化（A案）

日付: 2026-09-29  
状態: 変更提案。正式仕様への反映・実装・性能検証は未実施。

本書が明示的に変更する事項は、提案の評価上、SPECとその参照先に優先する。それ以外は既存仕様に従う。

## 1. 現状と変更理由

基準は現在の [SPEC §4.6](../../docs/spec/04-arrays-indexing-and-slices.md#46-indexing-and-slicing) と [§8.4](../../docs/spec/08-generics-constraints-and-contracts.md#84-static-contracts)。9月28日の整数範囲案は正式仕様へ取り込み済みであり、旧来の非ジェネリック `Range` は比較の基準としない。実装状況は [STATUS](../../docs/STATUS.md) に従う。

| 現行仕様 | 問題・変更する理由 |
| --- | --- |
| `Index` は方向と非負のoffsetを持つ | 実装は `isize` と `bool` を格納する。方向を符号化すれば1ワードに収まる |
| `Range<T>` は整数の両端と包含フラグを持つ | 半開・包含の区別が値と反復状態へ持ち込まれる |
| 省略境界やIndex境界は `IndexRange` になる | `0..n` と `..n` で型・検査の段階が変わる。負のnについて、前者の `trySlice` はNone、後者は引数のIndex化でAbortする |
| 範囲構文に整数→Index変換と専用の評価手順がある | 型推論・変換・副作用の規則が通常の構築と異なる |
| 範囲の公開constructorがなく、`ResolvedRange` の生成元も限定される | 通常のライブラリー宣言で説明できる部分にも、特別な生成経路が必要になる |
| contractは本文を持たず、反復の3入口は独立する | 同じ失敗処理や、範囲からIteratorを作る処理が繰り返される |

## 2. 目的と方針

1. **形は型、境界は値、操作はcontractで表す。** 型数より、推論・実装選択・失敗の規則数を減らす。
2. **構築と検証を分ける。** 範囲値は端点を保持し、反復・位置解決が必要な条件を検査する。
3. **実装を静的に確定する。** defaultによる共通化は、動的dispatch・確保・呼び出し場所による再選択を導入しない。
4. **普通のKimiで実装する。** 既存の型推論・条件付き適合・完全関数特殊化・所有権規則を再利用する。

降順・任意刻み・無終端の列挙、一般の数値演算contract、runtime Contract View、単一要素添字の整数型拡張は本提案の対象外とする。

## 3. 範囲とIndexの変更

### 3.1. 型・構築・推論

| 構文 | 正規のKimi型 | 読み取り専用の格納値 |
| --- | --- | --- |
| `a..b` | `Range<T>` | `start: T`, `end: T` |
| `a..=b` | `RangeInclusive<T>` | `start: T`, `end: T` |
| `a..` | `RangeFrom<T>` | `start: T` |
| `..b` | `RangeTo<T>` | `end: T` |
| `..=b` | `RangeToInclusive<T>` | `end: T` |
| `..` | `RangeFull` | なし |

各型に対応する公開 `init` を設け、範囲構文はその構築へ下げる。構文は常に正規のKimi宣言を使う。`IndexRange` と範囲値の `isInclusive` は廃止し、互換エイリアスを設けない。

- **型形成:** Tは通常の格納可能な完全Typeとする。5つのジェネリック型はTの能力に応じてCopy・Equatableへ条件付き適合し、Owned・Origin・Loanは通常の格納規則に従う。RangeFullは無条件にOwned・Copy・Equatableとする。保持できることは反復・スライス可能性を意味しない。
- **型推論:** 形を構文で決め、端点を同じTへ照合する。既存のジェネリック引数推論、期待型、リテラル適合を使い、確定済みの異なる型を暗黙変換しない。添字・`for` の文脈で形や意味を変更しない。
- **構築との接続:** 現行の `Type.init` は型引数の明示を要求する（§6.2.3）。範囲構文でTを推論してから、確定した `Range<T>.init` 等を呼ぶ。一般のconstructor引数省略を追加する提案ではない。
- **リテラル:** 存在する全端点がリテラル式なら、既存の範囲リテラル適合を片端形式にも適用し、Tへの期待を端点へ伝える。既定型は端点の通常のリテラル規則に従い、整数だけで他に情報がなければi32になる。範囲を端点とする場合も型の構造を保つ。`RangeFull` にTはなく、既定型でオーバーロードの曖昧さを解消しない。
- **取得と評価:** 左から各1回、通常の引数取得・評価・構築に従う。順序・対象長は検査しない。範囲固有のScalar readやIndex化は除去し、借用からの値取得も一般の適合規則か明示的な `@follow` に従う。
- **等価性:** 同じ具体型の端点で比較する。異なる形の暗黙変換・交差型比較はない。解決後の区間を比較したければ `ResolvedRange` 同士を比較する。

```kimi
let a = 1..3                       // Range<i32>
let b: Range<isize> = 1..3          // Range<isize>
let c = ..3                        // RangeTo<i32>
let d = Index.init(1)..^1           // Range<Index>
let e = ..^1                       // RangeTo<Index>
let bad = 1..^1                    // エラー: 整数とIndexを混在させない。
```

整数→Indexの暗黙変換も、整数リテラルのIndexへの適合も追加しない。後者は単一要素添字の `isize` / `Index` 候補を曖昧にする。短い `1..^1` を失う代わりに、変換と失敗する操作をソースへ明示する。

範囲演算子の優先順位・非結合性は維持する。ただしTの一般化に伴い、同じ型の範囲値を端点とする括弧付き構築も通常の型形成で判定する。構築を許しても、反復や位置解決の適合は自動的には得られない。

### 3.2. Indexの1ワード化

内部に `encoded: isize` だけを持ち、先頭相対nは `n`、末尾相対nは `-n - 1` とする。nは0以上isize最大値以下であり、両方向の全状態を表現できる。負のencodedのoffsetは `-(encoded + 1)` で復号し、isize最小値の直接の符号反転を避ける。

公開 `offset`・`isFromEnd` は値を返す計算プロパティにする。`Index.init(offset: isize, fromEnd: bool = false)`、`^n` の全整数型からの検査付き構築、方向とoffsetによる等価性は維持する。整数値の負数を末尾位置へ読み替えない。

両プロパティは格納場所を公開しなくなるため、`index.offset@ref` は計算結果の一時値への借用になる。長く借用する場合は `let offset = index.offset` と保存してから `offset@ref` を使う。寿命延長や隠れた格納領域は追加しない。

全ビットパターンを使うため、無効値やOption用の空き表現はない。現行実装の `Index.init(unchecked: -1)` による無効値の伝達は廃止する。Indexの並べ替えや算術は追加しない。

### 3.3. 解決済み区間

`ResolvedRange` は `start: isize`・`end: isize` と、不変条件 `0 <= start <= end` を持つ。公開 `init(! start:, end:)` で検査し、違反ならAbortする。`length`・`isEmpty`・等価性・isizeの反復を維持し、特定の対象長に対する有効性や、Storage・Origin・Loanは保持しない。

対象へ適用するときは現在長を検査する。`indices` は `[0, length)` のスナップショットのままとする。任意の整数範囲の要素数はusizeにも収まらない場合があるため、全範囲型へ一律の `length` は付けない。未解決の範囲には今回は `isEmpty` も追加しないが、これは要素数のoverflowから必要になる制約ではなくAPIの範囲の選択である。

## 4. Contractと操作の共通化

### 4.1. 静的なデフォルト実装

関数要件に通常の本文を許す。本文がなければ従来の必須要件、本文があればdefaultとする。引数の既定値・default関連型・stored memberを同時に追加するものではない。

#### 4.1.1. 要件と本文の識別

- Requirement Identityは既存の§8.4.2/8.4.9に従い、**宣言と正規化済みの完全な束縛**で識別する。`C<i32>` と `C<i64>` は、同じ宣言でも別要件である。
- defaultの提供元も、本文の宣言と完全な束縛で識別する。外側の型・Semantics・Originを含め、実装対応にはSelfの束縛とreceiverの対応も保持する。同じ提供元・同じ束縛への経路だけを重複除去する。
- 子contractは `func Parent.member(...)` により、本文のない親要件へdefaultを提供できる。Parentは型引数を含む通常のcontract参照で、署名まで照合して対象要件を一意に定める。新しい同名要件は作らず、親の既存defaultの差し替えも許さない。

#### 4.1.2. 適合を確定してから公開する

`Self is C` による明示適合を維持し、次の2段階で処理する。条件付き適合・refinementの全経路を照合し、列挙順に依存させない。

1. **適合対応を確定する。** 既存の実装探索・適合検査を使い、一意な具体実装があれば採用する。不適合・曖昧ならエラーとし、defaultへ戻らない。候補が0個の場合だけ、そのRequirement Identityに提供された一意なdefaultを使う。defaultも0個なら未実装、複数なら具体型の明示実装を要求する。親の順序・制約の強さによる優先順位は作らない。
2. **呼び出し候補を公開する。** 選んだdefaultを、適合を定義した型の探索層に、対応する要件の署名・公開範囲を持つ候補として加える。これは通常Member宣言の追加ではなく、確定した適合対応の公開である。この候補を第1段階の実装探索へ戻したり、別要件の実装として流用したりしない。既存の内在的導出・Property witnessは従来どおり扱う。

例えば、`A.f` のdefaultと本文のない `B.f` が同じ署名でも、AのdefaultだけでBへの適合は満たせない。共用するなら、具体型が両要件に適合する通常の `f` を宣言する。

公開後は同じ探索層の通常関数と候補集合を作り、§8.4.6・§9・§10の併合・オーバーロード選択を使う。独立した `f(i32)` と `f(string)` は選び分けられる。同等の呼び出し契約と同じ実装対応を証明できる候補は併合し、一意に選べない場合だけ曖昧とする。公開候補も、既存の継承名・探索層・receiver形状・関数とPropertyの名前衝突の規則に従う。

#### 4.1.3. 本文・継承・呼び出し

- 本文は宣言側の公開前提だけで検査し、要件呼び出しをRequirement Identityへ束縛する。具体型のprivate member探索、呼び出し側の追加制約による再選択、適合の循環証明は許さない。Origin・Loan・効果上限・cleanupも通常どおり検証する。
- 通常呼び出しで公開defaultを選んだ場合も、ジェネリック呼び出しでも、確定した同じ対応を使う。関連型・実装対応の経路整合性は§8.4.8に従う。
- 型の継承は§8.4.4を維持する。継承済み適合のdefaultは実装側Selfを適合定義元へ束縛したままとし、派生型へ自動的に再束縛・再選択しない。新しい明示適合との整合性、所有receiverやSelfを含む引数・結果の制約も維持する。

実装は特殊化された通常の関数として生成し、vtable・boxing・追加確保を要求しない。external conformance、適合全体の特殊化、曖昧なdefaultからの自動選択は導入しない。

### 4.2. 位置と範囲の解決

#### 4.2.1. Contractと失敗規則

次の2つをKimiの公開contractとする。receiverはいずれも `ref/Self`、lengthはisize。

| contract | 必須操作 | default | 標準の適合 |
| --- | --- | --- | --- |
| `Position` | `tryResolve(length) -> Option<isize>` | `resolve(length) -> isize` | 整数12型、Index |
| `SliceRange` | `tryResolve(length) -> Option<ResolvedRange>` | `resolve(length) -> ResolvedRange` | 端点TがPositionである5範囲型、RangeFull、ResolvedRange |

`tryResolve` を正規の解決操作とし、defaultの `resolve` はこれを1回呼び、Someなら値を返し、NoneならAbortする。明示実装で置き換える場合も、この結果・副作用・失敗の関係を意味上の約束とする。ただし適合検査だけでこの約束の遵守を証明できるとは扱わない。

両contractのtryResolveは、負のlengthならNone、成功なら `[0, length]` 内の位置・区間を返すことを約束する。標準の利用側も返り値を検査し、意味上の約束に違反するユーザー実装から不正なアクセスを作らない。

`try`付き操作は自身の検査失敗だけをNoneにし、引数評価・ユーザー呼び出しのAbortは捕捉しない。`PrimitiveInteger` はPositionをrefineし、`Position.tryResolve` のdefaultをKimiで提供する。組み込み12整数型だけが満たすという内在要件の境界を保ち、共通helperと既存の完全関数特殊化を使う。

#### 4.2.2. 標準の解決手順

Positionの標準実装は、長さLが非負で、正確な絶対位置pが `0 <= p <= L` のときSome(p)、それ以外はNoneを返す。整数はその値、Indexは方向に応じてoffsetまたは `L - offset` を位置とする。整数の狭幅化・符号変換・減算は、表現可能性を確認するか、同値でoverflowしない計算を使う（§6.1）。

標準の範囲解決はLを検査し、存在する端点の `Position.tryResolve` を開始・終了の順に各1回呼ぶ。各結果の `0 <= p <= L` を検査し、Noneまたは不正な位置なら、その場でNoneを返して後続端点を呼ばない。省略開始は0、省略終了はLとし、次の条件を満たすときだけ成功する。

| 形式 | 成功条件 | ResolvedRange |
| --- | --- | --- |
| 半開 | `0 <= s <= e <= L` | `[s, e)` |
| 包含 | `0 <= s <= e < L` | `[s, e + 1)` |

包含形式は**元のeに対して順序を検査してから**加算する。`3..=2` を `[3, 3)` として受理しない。`^0` は有効な境界だが、要素添字・包含終端としては無効である。

RangeFullは非負のLに対して `[0, L)` を返す。ResolvedRangeは、自身の不変条件に加えて `end <= L` を確認して返す。

#### 4.2.3. スライスへの適用と借用

固定配列・Array・Sliceの範囲添字は `R is SliceRange` を共有借用で受け付ける。`Slice<T>` の `trySlice` も次の公開署名へ統一する（本文省略）。

```kimi
public func trySlice<R>(self: Self, range: ref/R) -> Option<Slice<T> during self.source>
    R is SliceRange
```

`x[r]` と `s.trySlice(r)` は、同じ取得・検証手順を使う。

1. receiverとrを通常の順序で各1回取得する。rは `ref/R` として借り、Copy・Moveを要求しない。一時的な範囲値も通常の一時値寿命で扱う。
2. 対象の有効な現在長Lに対して `SliceRange.tryResolve` を1回呼ぶ。返された区間は、適用時にも `end <= L` を確認する。`0 <= start <= end` はResolvedRangeの不変条件による。
3. 成功なら対応するSliceを作る。Noneまたは適用時の検査失敗なら、範囲添字はAbort、trySliceはNoneとする。

両入口は `resolve` を呼ばない。これにより、ユーザーがresolveを置き換えても両入口の検証経路は分岐しない。取得済みの区間を適用するときに再び範囲解決へ戻らず、重複呼び出しを避ける。

結果は対象Storageの依存と要素Tの既存依存を保持し、rやその端点への依存は追加しない。Slice receiverなら `self.source` を保持する。アクセス予約・Loan・効果検査で対象を保護し、解決から適用までに対象の長さ・Storageを無効化する操作を許さない。contractの意味上の約束だけで検査を除去せず、標準経路の重複検査も証明できる場合だけ融合する。

範囲添字はSlice値を返すため、要素PlaceのIndexableへ統合しない。Dictionaryやユーザーreceiverの添字能力は変えず、Positionへの適合だけで単一要素添字・`tryGet`・`splitAt` の引数型も広げない。

### 4.3. 反復入口と状態

`Range<T>`・`RangeInclusive<T>` は `T is PrimitiveInteger` のとき、`ResolvedRange` は常に整数を列挙する。片端・全範囲・Index境界の範囲は、長さに対して解決してから列挙する。

#### 4.3.1. 共通の入口

反復の重複を減らすため、通常のライブラリーcontract `CopyIterable: Iterable, UniqIterable, IntoIterable` を設ける。

- `Self is Copy`、`associate Cursor is Iterator`、必須の `makeIterator(self: Self) -> Cursor` を持つ。
- 借用2入口の `IteratorType(source)` と所有入口の `IteratorType` を、明示的に同じCursorへ固定する。Cursorは入口のsourceによらず、Self内部に既にある依存は保持する。
- 共有・排他入口のdefaultはSelfをCopyして、所有入口はSelfを渡して `makeIterator` を呼ぶ。元のSelfのStorageへの借用を結果に残さない。
- 明示適合した型だけが使う部品であり、Copyな型への自動適合や、コンパイラーの第4の反復方式にはしない。

#### 4.3.2. 列挙の保証と内部表現

半開の `RangeIterator<T>` と包含の `RangeInclusiveIterator<T>` は、いずれもNon-Copyとする。入口で `start <= end` を検査し、逆転ならAbortする。昇順に各要素を1回返し、包含形式は最大値の終端も列挙できる。終了後は常にNoneを返し、公開範囲値の端点・等価性を変えない。

第一候補の実装は `current: T`・`end: T` の2端点だけを持つ。半開は `current < end` の間だけ返す。包含のnext本文は次の状態遷移にできる。

```kimi
let value = self.current
if value < self.end
    self.current = value + 1
    return .Some(value)
if value == self.end
    self.current = 1
    self.end = 0
    return .Some(value)
return .None
```

途中の加算は `value < end` によって溢れず、最後の値には加算しない。終了後の `(1, 0)` は全12整数型で表せる。これは内部表現の実装案であり、Iteratorの格納形式は公開保証に含めない。end固定・終了フラグ方式とも比較し、§6.2の測定で選ぶ。

両IteratorはIteratorの効果上限を満たし、公開constructorや反復入口への自動適合は設けない。Iterator値の列挙には既存の `Iteration.owning` / `borrowing` を使う。`for var`・途中終了・cleanupは既存規則を維持する。

## 5. 副作用と設計上の交換条件

| 影響 | 判断・対処 |
| --- | --- |
| `1..^1` が使えなくなる | `Index.init(1)..^1` と明示する。短縮構文のための暗黙変換は増やさない |
| 同じ端点でも半開・包含は別型になる | 実行時に形を選んで1変数へ保存する場合はenumを使う。contractは共通操作であり共通の格納型ではない |
| `..` と `0..^0` の従来の等価性がなくなる | 後者はIndexへの明示構築も必要。対象長で解決した結果を比較する |
| `trySlice(n..)` / `trySlice(..n)` の負数がNoneになる | 省略だけを理由にIndex化しなくなる効果。明示的な `^n` / `Index.init(n)` のAbortは捕捉しない |
| 公開constructor・任意Tにより利用範囲が広がる | Non-Copy端点のMove、借用端点のOrigin・Loanを通常規則で追跡する。全範囲がOwned・Copy・借用非保持という旧保証は外す |
| default追加が既存の名前解決へ影響する | 具体型とジェネリックで同じ適合対応を使う。競合を診断し、関連する依存成果物を無効化する |
| Indexのプロパティが格納場所を公開しなくなる | 既存のProperty借用・Place前提のコードを点検し、必要なら計算結果をローカルへ保存する（§3.2） |
| 型・適合・生成関数が増える | 範囲の種類による実行時分岐は減るが、コンパイル時間・コードサイズを測定する |
| 逆転区間の扱い | 構築は成功、反復開始はAbort、try解決はNone。空・降順への読み替えを追加しない |

## 6. 性能・一貫性・概念整理の改善

### 6.1. 格納とIndexの直接計算

Indexは1ワード、両端範囲・ResolvedRangeは端点2個、片端範囲は1個、RangeFullのpayloadは0とする。全整数をisizeへ変換せず、例えばRange<i32>はi32を2個保持する。Iteratorは§4.3.2の実装選択を行う。

Indexの等価性はencoded同士の比較1回で判定できる。解決もoffsetの復号を経由せず、`L >= 0` を確認した後、次の同値な計算を使える。

| encodedをcとした条件 | 結果 |
| --- | --- |
| `c >= 0` | `c <= L` ならSome(c)、それ以外はNone |
| `c < 0` | `q = L + c` を計算し、`q < -1` ならNone、それ以外はSome(q + 1) |

後者の加算は異符号なので溢れず、`q <= isize.MaxValue - 1` のため最後の加算も安全である。意味は従来の `L - offset` と同じ。生成コードを比較し、同等以上なら直接計算を採用する。

### 6.2. 反復と適用の最適化

- **反復:** 半開・包含で型とnextを分ける。2端点方式は小さいが、終了時のend更新がループ不変値の認識・境界検査除去・ベクトル化を妨げる可能性がある。end固定・終了フラグ方式と、短いループ・長い配列添字ループ・途中終了・終了後の再呼び出しで比較する。小さい格納量だけで最速とは判断しない。
- **解決:** static dispatchとインライン化でOption・ResolvedRangeの中間実体化を除去する。頻出のisize・usize・i32では不要な128-bit変換を残さない。
- **検査:** 標準Iteratorの値域を、対象の長さ・Storageが不変と証明できる領域で再利用する。検査の融合は副作用・Abort・借用の意味を保存する。

共通部品はPosition・SliceRange・CopyIterableに絞る。汎用Resolvable、Bound enum、Step、関連型の自動推論は追加しない。default選択と呼び出しの分離、tryResolveへの解決経路の統一により、例外的な探索・検証規則を減らす。

### 6.3. 要求と測定目標

標準の整数・Indexによる構築・解決・反復開始・各nextはO(1)、n要素の列挙はO(1+n)、追加領域はO(1)、自身のヒープ確保・参照カウント更新はゼロを要求する。任意TのCopy・破棄やユーザー実装の費用は別であり、defaultの使用だけでゼロコストを保証しない。

Windows x64の格納目標は、Index 8 bytes、isize/Indexの両端範囲・ResolvedRange 16 bytes、片端範囲8 bytes、RangeFullのpayload 0 bytesとする。isizeの両Iteratorも16 bytesを第一候補とするが、実測性能を優先する。公開ABIや他プラットフォームの配置は固定しない。O2では同じ意味の手書きコード・変更前・代替実装との生成コード、実行時間、コードサイズを比較する。

## 7. 修正箇所と実装上の課題

| 対象 | 修正内容・注意点 |
| --- | --- |
| SPEC §3・§4.6・§5.3・§13・§14・§22、付録E/F | 新しい範囲型、生成・能力・失敗・等価性、Index表現、raw pointerの除外、反復入口、必須宣言 |
| SPEC §6.2.3・§8.4/8.7・§9・§10・§11・§12.3.1・§15 | constructorと推論の接続、defaultの識別・2段階処理・継承、片端リテラル、計算プロパティ、範囲の共有借用と結果依存 |
| IMPL §21、SPEC §18/23 | レイアウト、特殊化・呼出先・効果要約・キャッシュ依存、成果物検証、defaultと適合元の診断・AST情報 |
| `src/Kimi/Library/Core.kimi`、`Slice.kimi`、`Iteration.kimi` 等 | 型分割、Position/SliceRange/CopyIterable、Index圧縮、2種類のIterator、公開constructorと共通API |
| `src/Kimi/Compiler/Binding/Binding.Ranges.cs`、`Binding.Sequences.cs`、`Binding.Contracts*.cs`、`Binding.Calls.cs` 等 | 範囲の種別判定・遅延Index化の除去、既存推論手続きの利用、defaultの検査・選択・公開呼び出し |
| Parser・宣言モデル・`Emission/BodyLowering.Sequences.cs` 等 | contract本文・親要件指定の構文、通常Kimiコードへの接続、旧Range専用処理・レイアウト・必須宣言識別の点検 |
| `docs/LIBRARY.md`、`docs/STYLE.md`、Library README、例・milestones・テスト | 同じ変更単位で公開API・規約・利用例を更新。STATUSは実装と検証が済んだ対応境界だけを記録 |

特に次の実装境界に注意する。

- **推論と構築:** 範囲構文が既存の引数照合を共有できる構成にする。推論後のconstructor呼び出しと、借用・取得の検査を分離する。
- **適合とキャッシュ:** 通常宣言・default提供元・確定した適合対応・公開候補を区別する。完全な束縛をキャッシュキーへ反映し、PrimitiveIntegerの内在的証明とPositionの実装選択を循環させない。
- **依存と最適化:** CopyIterableのsource非依存性をLoanまで検証する。直接添字と保存した範囲を同じ処理へ接続し、検査の重複除去は生成コードで確認する。診断・成果物にも実際の適合元と効果を保持する。

## 8. 実装順序と受け入れ条件

1. defaultの要件識別・本文検証・呼び出し規則を一般機能として実装し、範囲に依存しないテストを通す。
2. Indexを圧縮し、PositionとSliceRangeの解決処理を実装する。無効Index用sentinelを残さない。
3. 6型と公開constructor、構文・推論の切り替えを実装し、範囲の保存・直接添字・trySliceを統一する。
4. CopyIterableと2種類のIterator、ResolvedRangeの生成規則を実装し、旧型・包含フラグ・専用処理を整理する。
5. 正式仕様・公開索引・例を対応する実装単位で更新し、統合検証と性能比較を行う。

受け入れ検証を次の4群に分ける。

| 群 | 主な確認内容 |
| --- | --- |
| 型・値 | 全12整数型、最小・最大値、負の長さ、`^0`、空・単一要素・逆転・包含最大値、片端・入れ子の推論、異なる形の型エラー、明示変換の失敗順序、Indexの直接計算と復号経由の一致 |
| 適合・呼び出し | 同じ宣言の異なる束縛、defaultの競合・diamond、別要件への流用禁止、条件付き適合、型継承のSelf、通常のオーバーロード、効果上限、依存成果物の再検証 |
| 借用・適用 | 借用端点・Non-Copyな範囲の再利用、範囲一時値と結果の独立性、IndexのProperty借用、不正な位置・区間、解決回数と順序、resolveを置き換えても通常添字とtrySliceが同じ検証を行うこと |
| 反復・性能 | 終了後のNone、再走査、途中終了、`for var`、IteratorのMove/Copy境界、§6の格納量・生成コード・実測性能 |

設計検討時の独立モデルでは、i8/u8の全65,792個の順序付き区間・5,658,112要素で包含Iteratorの状態遷移を確認した。Indexの直接計算も、符号付き8-bit相当の長さとencodedの全65,536組で復号経由と一致した。これはKimi実装や速度の検証ではない。実装時に境界値を含む回帰テストとして再現する。

実装単位は `scripts/verify.ps1 -Class ...` と関連するO0/O2 fixture・milestoneで検証し、セッション末にSession検証を行う。性能比較は§6に従い、保存した範囲・実行時の境界・warm Bindingも含める。証拠は `artifacts/verify/`、測定は `artifacts/benchmarks/` に保持する。NativeAOTは対象外とする。

本書の保存だけでは正式仕様・実装・STATUS・取り込み台帳を変更しない。採用・取り込み時は正式仕様を自己完結させ、本書の取り込み先を `draft/INTEGRATED.md` に記録して固定する。
