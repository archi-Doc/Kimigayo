# 仕様変更案：要求の効果上限

日付：2026-10-01

状態：最終版。方針と決定事項は確定済み。正式仕様への取り込みと実装は未実施。

本書で変更する事項は SPEC より優先し、変更しない事項には既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例の「エラー」行は意図した拒否例である。

## 1. 問題と方針

### 1.1. 現仕様の問題

引数・戻り値の型が合うことと、呼出し中に何へアクセスするかを保証することは別である。ユーザーが実装できる Contract のうち、後者の保証（**効果上限**）を持つのは、SPEC §8.4.5 が名前で列挙する次の二つだけである。

| 要求 | 保証 | 定義 |
| --- | --- | --- |
| `BufferWriter.reserve` | 可変状態へのアクセス権限を `self` からだけ得る | [utf8-formatting §1.2](../../docs/spec/utf8-formatting.md#12-effects-and-erasure) |
| `Iterator` の `next` | 以前に返した item の Loan と競合しない | [SPEC §22.1.2.4](../../docs/spec/22-core-execution-and-foreign-functions.md#22124-iterator-independence) |

これらの保証は宣言の識別に結び付いている。同じ綴りやシグネチャのユーザー Contract は保証を得られず、ユーザーが自分の要求に保証を宣言する手段もない。コメントで約束しても、コンパイラは適合実装を検査できず、ジェネリック呼出し側も保証として使えない。保証を得るには標準 Contract を詳細化するしかない。これは原則1（一つの概念に一つの形）と原則3（意味を構文か契約で表す）に反する非対称である。

```kimi
contract Source
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>

func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is Source
    let first = source.take()
    let second = source.take() // エラー：uniq の receiver を通じて first の Loan の参照先に書き込み得る
    return (first@move, second@move)
```

**既存仕様の不整合**（本書 3.1・3.8 で解消する）

- SPEC §22.1.2.5 は「No general effect syntax」とし、保証を宣言する手段がない。
- Appendix E の「Effect bound」は Iterator の保証だけを定義し、SPEC §8.4.5 と食い違う。
- Appendix F の `ContractItem` に、`Iterator` が使う修飾付きの `associate LendingIterator.LentItem(step) is Item` がない。
- SPEC §8.4.1 は要求の制約領域を「one or more Constraint Clauses」とし、Origin relation が漏れている。

**現実装との差**（証拠：`artifacts/verify/20261001-requirement-effect-bounds-probe/`）

- 上の `takeTwo` を受理する。ジェネリックな要求呼出しが receiver から到達できる Place に及ぼす効果を、保持中の Loan と照合していない（SPEC §15.6.3、§22.1.2.1 に反する）。
- 上限違反は `IncompatibleContractImplementation_Kd` の汎用文になり、`Self is C` の位置だけを示す。どの上限にどの効果が違反したかは示さない。
- `Binding.EffectBounds.cs` の上限の選択、要求呼出しの合成、委譲の判定が、`BufferWriter`・`LendingIterator`・`Iterator`・`Option` を直接参照している。

### 1.2. 方針

1. **語彙を二つに固定する。** 環境の可変状態に触れない `confined` と、以前の結果を壊さない `preserves results` である。包括的な語（`pure` など）やユーザー定義の上限は設けない。
2. **上限は Contract の保証とする。** 上限は、それを宣言した Contract への適合が与える保証である。要求の宣言そのものは強化しない。
3. **既存の規則を再利用する。** 適格性は公開結果の独立性（SPEC §15.6.3）、検証は推移的効果サマリー（SPEC §8.4.5）、呼出しの照合は Loan の競合規則（SPEC §15.6.2）で判定する。新しい解析は作らない。
4. **標準 Contract も同じ構文を使う。** 識別による特権をなくす。上限は実装者に義務を課すだけなので、誰が宣言しても健全性は変わらない。権限を与える機能（`Kimi.Storage` の分割など）は、従来どおり識別に結び付ける。
5. **静的検査で完結させる。** 実行時タグや呼出し履歴は持たない。

## 2. 期待する動作

### 2.1. 結果非干渉

```kimi
contract Source
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>
        effect preserves results

func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is Source
    let first = source.take()
    let second = source.take() // OK：preserves results により first の Loan とは競合しない
    return (first@move, second@move)
```

### 2.2. 保証は Contract ごとに決まる

```kimi
contract Source
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>

contract StableSource: Source
    effect Source.take preserves results

func takeTwoStable<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is StableSource
    let first = source.take()
    let second = source.take() // OK
    return (first@move, second@move)

func takeTwoLoose<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is Source
    let first = source.take()
    let second = source.take() // エラー：S is Source だけでは first の Loan を照合から除けない
    return (first@move, second@move)
```

`take` が上限の検査を受けるのは、`Self is StableSource` を宣言した型だけである。`Self is Source` だけの型は、検査を受けず、保証も与えない。`Iterator` と `LendingIterator` も同じ関係である。

### 2.3. 上限から求めた効果で照合する

```kimi
contract View
    func read(self: ref/Self) -> ref/i32
        effect confined

func sumTwo<V>(view: ref/V) -> i32
    V is View
    let first = view.read()
    let second = view.read() // OK：共有 receiver の呼出しは読むだけで、first の共有 Loan と競合しない
    return first + second
```

`preserves results` は非競合を示す手段の一つにすぎない。`read` の結果は receiver の借用に依存するため、そもそも `preserves results` を宣言できない。`confined` があると環境への不明な効果が除かれるので、`view` が static を借用していても、`sumTwo` の呼出し元で競合しない（SPEC §15.6.4）。

### 2.4. 権限の閉包

```kimi
contract Sink
    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        effect confined

group Metrics
    var puts: isize = 0
    public func record() => Metrics.puts += 1

struct CountingSink
    Self is Sink
    var count: isize = 0
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        self.count += 1 // OK：self から得た権限
        return .Ok(())

struct LoggingSink
    Self is Sink
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        Metrics.record() // エラー：可変 static Metrics.puts に到達し、Sink.put の confined に反する
        return .Ok(())

struct Forward<W>
    W is Sink
    Self is Sink
    var inner: W
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        return self.inner.put(value) // OK：W.put は confined で、効果は self.inner を通じたものに限られる
```

### 2.5. 委譲

本書 2.1 の `Source` を実装する例である。

```kimi
struct Drain<J>
    J is Iterator
    Self is Source
    associate Source.Item is J.Item
    var inner: J
    public var taken: isize = 0
    public func take(self: uniq/Self) -> Option<J.Item>
        self.taken += 1
        return self.inner.next() // OK：J の上限が、self.inner から得た以前の結果を覆う

struct Merge<J>
    J is Iterator
    Self is Source
    associate Source.Item is J.Item
    var left: J
    var right: J
    public func take(self: uniq/Self) -> Option<J.Item>
        match self.left.next() // エラー：J を名指す Field が二つあり、委譲規則で覆われない
            .Some(let item) => return .Some(item@move)
            .None => return self.right.next()
```

### 2.6. 不適格な宣言

```kimi
contract Cursor
    associate Lent(step) for uniq/Self during step
    func advance(self: uniq/Self during step) -> Option<Self.Lent(step)>
        effect preserves results // エラー：結果が呼出しの receiver 借用 step に依存し得る

contract Peek
    associate Item
    func peek(self: ref/Self) -> Option<ref/Self.Item>
        effect preserves results // エラー：省略した結果 Origin は self になる
```

## 3. 仕様変更

### 3.1. 構文

```text
RequirementConstraints := IndentedList<ConstraintClause | OriginRelation | EffectClause>
EffectClause         := "effect" EffectBound
EffectSpecification  := "effect" ContractSelector "." Name EffectBound
EffectBound          := "confined" | "preserves" "results"
ContractItem         := ContractRequirement | AssociatedTypeDeclaration | AssociatedTypeSpecification
                      | EffectSpecification | ConstraintClause | Directive<ContractItem>
```

`effect`、`confined`、`preserves`、`results` は、これらの位置でだけ働く文脈語である。`ContractItem` に `AssociatedTypeSpecification` を加えるのは、既存の記述漏れの修正である。

### 3.2. 宣言

1. **`EffectClause`：** 囲む Contract が、自分の要求に宣言する上限である。
2. **`EffectSpecification`：** 祖先 `ContractSelector` から継承した関数要求 `Name` に対して、囲む Contract が宣言する上限である。
   - `Name` は、その祖先の関数要求を一つだけ識別しなければならない。
   - 自分の要求には `EffectClause` だけを使う。
3. **重複の禁止：** Contract 自身または祖先がすでに宣言した上限を、同じ要求に重ねて宣言するとエラーになる。
4. **書ける場所：** 効果項目は Contract の中にだけ書ける。
   - 実装、通常の関数、Property 要求、Function Type、Callable 制約には書けない。
   - 実装は、上限を追加することも取り消すこともできない。
5. **語彙と公開性：** 語彙は閉じており、上限の追加には仕様変更が要る。上限は Contract の公開契約の一部であり、既存の意味記録（SPEC §18.7.1）に含まれる。

### 3.3. `confined`

実装の推移的効果サマリー（SPEC §8.4.5：呼出し先、特殊化、アクセサー、既定値、遅延初期化、破棄を含む）は、**環境への効果**を含んではならない。環境への効果とは、次のものをいう。

- 可変 static Field へのアクセス（読取り、借用、書込み、置換、破棄、初回アクセス時の初期化）
- 実行環境の外部状態にアクセスする標準操作（Console 出力など）
- 分類できない効果（環境への効果が不明な呼出しなど）

次のものは許す。

- 割当てと Abort
- 不変 static の読取り（その初期化子もサマリーに含める）
- 入力の借用 Field を通じたアクセス（参照先が static Storage にあっても、通常の Loan 検査に従う）

unsafe 操作の扱いは変更しない。現在の `reserve` の「`self` からだけ」は、`minimum: isize` が権限を運ばないので、この定義と同値である。

### 3.4. `preserves results`

**適格性**

- receiver は借用（`ref/Self` または `uniq/Self`）でなければならない。
- 要求の正準な結果契約（SPEC §15.3.7）が、許容されるすべての束縛で公開結果の独立性（SPEC §15.6.3）を満たさなければならない。つまり結果は、呼出しの receiver の Loan にも、receiver の Storage にも依存しない。
  - 判定は意味的な依存で行い、表記からは判定しない。宣言する Contract の前提（関連型の指定と Constraint）、結果 Origin の既定（SPEC §15.4.3）、別名を正規化してから判定する。
  - 実装の結果互換性（SPEC §8.4.5）によって、独立性はすべての実装に及ぶ。

**保証**

型 T が C に適合するとき、T の値 v に対する要求 r の呼出しの効果は、v に対する r のより前の呼出しが返した結果の Loan と競合しない。その結果の移譲や Reborrow によって保たれる Loan も含む。

- 効果には、読取り、書込み、借用、新しい結果の Loan、static と capture へのアクセス、呼出し内の後始末を含む。
- v の同一性は、Move の前後を通じて、既存の Loan・依存追跡で定まる。
- 対象外は次のとおりである。純粋性も保証しない。
  - v 全体の破棄や置換
  - 他の要求の呼出し
  - 結果と無関係な Loan
  - 結果を返した後にユーザーが加えた依存

**検証**

適合検査では、実装の推移的効果サマリーを、結果が保持し得る Loan と照合する。根・Loan・効果の共通サマリーと、領域分割（SPEC §15.6.3）を使う。分割された子や移譲された値は、残りへのアクセスと競合しない。

### 3.5. 上限の検査と利用

**実装側の検査**

- 適合 `T is C` では、C が宣言した上限について、各 witness を検査する。
- 祖先の上限は、`T is C` が含意する祖先への適合（SPEC §8.4.2）で検査する。
- 条件付き適合は、その条件の下で検査する（SPEC §8.4.8.2）。
- 同じ witness を同じ上限で二度検査しない。

**使える上限**

- 効果が具体的な実装から分からない要求呼出し（ジェネリックの要求呼出し、`Utf8Writer` の消去された呼出し）は、呼出しの前提が証明する Contract（SPEC §8.7）が、その Requirement Identity に宣言した上限を使える。上限を実装から探すことはない。
- SPEC §8.4.6 で一つの候補にまとめた複数の Requirement Identity は、それらすべての上限を使える。まとめた要求は、同じ実装を選ぶからである。

**要求呼出しの効果**

上の呼出しの効果は、使える上限から次のように求める。実装側の検査の中で到達した要求呼出しにも、呼出し側にも、同じ規則を使う。

1. **入力経由の効果：** receiver と各引数から参照をたどって到達できる Place に、パラメーターのアクセス方式で及ぶ。
   - 共有なら読取りと共有借用、排他なら書込み・置換・破棄まで含む。
   - 上限の有無によらない。
   - 抽象型の入力からは、その型の Origin が示し得るすべての Loan に到達し得るものとみなす。
2. **環境への効果：** `confined` があれば無い。なければ不明である。
   - 上限の検証では、不明な効果を競合とみなす（SPEC §8.4.5）。
   - それ以外では SPEC §15.6.4 に従う。
3. **以前の結果の除外：** `preserves results` があれば、同じ値に対する同じ要求の、より前の結果が保持する Loan を照合から除く。

求めた効果を、保持中の Loan と Loan の競合規則（SPEC §15.6.2）で照合する。Loan を持たない結果は、照合の対象にならない。`preserves results` は非競合を示す手段の一つであり、受理の唯一の条件ではない。

### 3.6. 委譲

`preserves results` を持つ要求 r の実装が、抽象型 J の値に対して要求 d を呼ぶとする。前提によって、J の d には `preserves results` があるものとする。次の二つを満たす場合を考える。

1. Self が J を、ただ一つの Field f に、J 自身または J の借用として格納する。J を名指す型を持つ値は、ほかに格納しない。
2. 適合の範囲で正規化した r の結果型が、d の結果型と等しい。

このとき実装は、f を通じた d の結果だけを返す。そのため r の以前の結果は、d の以前の結果に当たる。実装自身の本体で `self.f` を通じて行う d の呼出しは、回数によらず、本書 3.5 の 3 によってそれと競合しない。覆われる d は一つに限る。

- d の結果は J の Storage に依存しない。そのため、借用 Field `s/J during o` を通じて J に到達しても、o を介して保持中の結果とは競合しない。
- 次の呼出しは、本書 3.5 の通常の効果として扱う。
  - 呼出し先の本体での呼出し
  - 別の経路で到達した J への呼出し
  - J の他の要求の呼出し
  - 他の値への呼出し
- `zip` や `chain` のような複合アダプターは対象外である。任意のラッパーを証明する仕組みは追加しない。
- `confined` には委譲規則が要らない。本書 3.5 の 1・2 で合成できるからである。

### 3.7. 標準宣言

```kimi
public contract BufferWriter
    func reserve(self: uniq/Self, minimum: isize) -> Result<WriteWindow, BufferFull>
        effect confined

public contract Iterator: LendingIterator
    associate Item
    associate LendingIterator.LentItem(step) is Item
    effect LendingIterator.next preserves results
```

- 保証の内容は現行と同じである。`LendingIterator.next` 自体は上限を持たない。
- `Utf8Writer` の消去された呼出しは、`BufferWriter` の `confined` で検査する。
- 次のものは変更しない。
  - `Kimi.Storage` の権限
  - 内在 Contract の識別に依存する効果
  - 閉じた Contract の固定された効果

### 3.8. 既存の節の改訂

| SPEC | 変更 |
| --- | --- |
| §8.4.1 | 要求の制約領域を「Constraint Clause、Origin relation、効果節」とする |
| §8.4.5 | 二つの上限の列挙を §8.4.10 への参照に置き換える。「Calling context and Effects」の行は §8.4.10 の上限を指すようにする。「no new effect system」は「§8.4.10 の閉じた語彙以外の効果体系を定義しない」に改める |
| §8.4.7 | 必須 Kimi 宣言の上限を、宣言された上限（§8.4.10）とする。内在 Contract の効果は変更しない |
| §8.4.10（新設） | Requirement effect bounds：§8.4.10.1 宣言（本書 3.1・3.2）、§8.4.10.2 `confined`（本書 3.3）、§8.4.10.3 `preserves results`（本書 3.4）、§8.4.10.4 検査と利用（本書 3.5）、§8.4.10.5 委譲（本書 3.6） |
| §12.4.4.2 | 「requirement Effect contracts」を §8.4.10 の上限とする |
| §15.6.3、§15.6.4 | 公開結果の独立性の括弧書きを §8.4.10 に一般化する。ジェネリックな要求呼出しの効果を §8.4.10.4 で求めることを、§15.6.4 に加える |
| §22.1 の表、§22.1.2.1 | Iterator の宣言に効果指定を加える。表の「only an Iterator publishes」は「`preserves results` を宣言した Contract（Iterator など）」に改める |
| §22.1.2.4 | 一般則（上限の定義、検証、委譲）を §8.4.10 へ移し、Iterator 固有の事実だけを残す。残すのは、item の分割と移譲、標準 iterator が利用者コードを呼ばないこと、全体の破棄の扱い、`zip`・`chain`、LendingIterator の扱いである |
| §22.1.2.5 | 「No general effect syntax or runtime tag is added」を「実行時タグは追加しない。効果の宣言は §8.4.10 の上限だけである」に改める |
| utf8-formatting §1、§1.2 | 宣言に `effect confined` を加える。§1.2 の第1段落は §8.4.10.2 への参照に置き換え、消去の段落は残す |
| Appendix D | 具体的な関数、Property 要求、Function Type、Callable 制約への効果上限を、延期項目として記録する |
| Appendix E | 「Effect bound」を一般的な定義に改め、`confined` と `preserves results` を加える。Iterator の項はこれを参照する |
| Appendix F | 本書 3.1 |

SPEC §18.7 は変更しない。効果は、既存の意味記録にすでに含まれている。

## 4. 診断と回復

1. **宣言：** Language Error `InvalidEffectBound_Kd` を新設する。
   - **対象：** 識別できない・曖昧・祖先でない対象、重複した上限、Contract 以外での記述、借用 receiver のない `preserves results`、結果の依存。
   - **位置：** 主範囲は効果項目、関連位置は対象の要求とする。
   - **依存の場合：** 依存する部分と、その経路（関連型の指定、省略された Origin など）を Reason に示す。依存は API の意味そのものなので、自動修正は出さない。
2. **適合：** `IncompatibleContractImplementation_Kd` の Reason 値として扱う（SPEC §23.3.6.1）。
   - **Reason：** 上限の種類、それを宣言した Contract、対象の要求を示す。違反が確定したもの（static Field、外部操作、競合する Loan）か、規則によって競合とみなした不明な効果かも区別して示す。
   - **位置：** 主範囲は、実装自身の本体にある最初の違反効果とする。関連位置は、違反までの効果の経路、`Self is C`、上限の宣言とする。
   - **保証を満たす修正：** 状態を `self` の Field に移すか、引数で渡す。競合するアクセスを避ける。単一 Field の委譲の形にする。
   - **公開契約を弱める設計変更：** 上限のない Contract へ適合先を変える（例：`LendingIterator`）、または自分の Contract から上限を外す。これは修正ではない。保証に依存する呼出し側にも変更が要ることを明記したうえで、該当する宣言を編集できる場合に限り、条件付きで示す。実装側だけで継承した上限を取り消すことはできない。
3. **呼出し側：** Language Error `CallEffectConflict_Kd` を新設する。
   - **位置：** 主範囲は後の呼出しとする。関連位置は、競合する保持中の Loan と、それを生んだ呼出しとする。
   - **Reason：** どの入力を経由したどのアクセスが、どの Loan と競合し得るかを示す。そのアクセスを除く上限が、前提の下で使えないことも示す。実際に競合すると断定はしない。
   - **Advice：** 上限を宣言した Contract へ制約を強める案を示す（すべての利用型がその Contract に適合する場合に限る）。または、保持中の結果の使用を呼出しの前に終える案を示す。
   - **既存の診断との分担：** receiver の借用そのものとの競合は、従来どおり既存の診断（`CallActivationConflict_Kd` など）で報告する。
4. **回復：** 上限に違反した適合は、現行どおり無効とする。不適格な効果項目は保証を与えない。その保証があれば解消したはずの呼出し側の競合は、宣言エラーを前提とする従属診断として扱う（SPEC §23.3.6.4）。
5. **意味の検査：** AST・意味の検査と言語サーバーの hover から、次の情報を参照できるようにする。
   - Contract が宣言する上限
   - 要求呼出しで使える上限と、それを証明した Contract

## 5. 実装計画

[CODEMAP](../../docs/dev/CODEMAP.md) を起点に進める。

### 5.1. 実装単位

| 単位 | 作業 | 完了条件 |
| --- | --- | --- |
| U1：構文 | 要求の制約領域と Contract 項目の構文解析を作る。`tests/diagnostics/syntax.json` に行を加える。拡張に変更が要る場合は `src/kimi-ext/AGENTS.md` に従う | 文脈語の曖昧性の対照例（`effect` という名前の型パラメーターを含む）が通る |
| U2：宣言と上限表 | Contract ごとの上限表と、要求ごとに上限を宣言した Contract の一覧を作り、宣言を検査する。独立性は、結果 Origin の既定と関連型の正規化を済ませた正準な結果型に対する一つの判定とし、宣言・実装側・呼出し側で共有する。表記による独自の判定は作らない | 本書 2.6 と本書 4 の 1 の対照例が通る |
| U3：実装側検査の一般化 | `Binding.EffectBounds.cs` で、上限の選択を上限表に置き換え、要求呼出しを本書 3.5 の効果で合成し、委譲の判定から型固有の参照を除く。違反したノードと、各 context を作った呼出し位置を記録する（Binding と所有権解析後の両経路）。`Formatting.kimi`・`Iterator.kimi` に効果項目を加え、識別による分岐を除く | 既存の Iterator と reserve の効果テストが期待値を変えずに通る。本書 2.4・2.5 が通る。違反の経路を取り出せる |
| U4：呼出し側 | ジェネリックの要求呼出しと消去された要求呼出しについて、本書 3.5 の効果と照合を `OwnershipBody` に加える（既存の実装差の解消を兼ねる） | 本書 1.1 の `takeTwo` と 2.2 の `takeTwoLoose` が拒否される。`nextPair`・`takeTwoStable`・本書 2.3 の `sumTwo`・同じコレクションに対するジェネリックな入れ子の `for` が通る |
| U5：診断の表示と意味の検査 | 本書 4 を実装する。経路は U3 の記録から表示する。CLI と言語サーバーの出力を確認する | DIAGNOSTICS §10 の手順を完了する |

各単位は、再現・実装・focused tests の順で進める。診断や安全性の修正を後回しにして、完了扱いにしない。環境への効果と static Loan の一般的な照合（SPEC §15.6.4）は本案の範囲外とし、既存の制限として STATUS に残す。

### 5.2. 性能方針

- 実行時の機構は追加しない。
- 上限表は一度だけ作る。上限のない要求と適合には、費用を足さない。上限のない適合は、従来どおりサマリーを作らない。
- 二つの上限を持つ witness でも、サマリーの走査は一回にする。
- 経路の記録を含め、既存のプールを再利用する。温まった再 Binding と所有権解析の割当てゼロ（`WarmEffectBoundChecksDoNotAllocate`）を維持し、ユーザー Contract の版の検査も加える。
- 呼出し側の照合は、ジェネリックまたは消去された要求呼出しのうち、保持中の Loan がある場合に限る。Loan ごとの割当ては追加しない。
- コンパイル時間と解析時の割当てを、一般化した委譲と詳細化、および既存のマイルストーンで測定する。測定前に速度の改善を主張しない。

### 5.3. 検証

| 観点 | 必須の対照例 |
| --- | --- |
| 宣言 | 二つの形、祖先でない対象、オーバーロードした名前、重複、Contract 以外での記述、借用 receiver のない要求、関連型・別名・省略 Origin に隠れた依存、`LentItem(step)` の固定の有無 |
| 上限の利用 | `S is Source` と `S is StableSource`、Iterator と LendingIterator、条件付き適合、ダイヤモンド継承、§8.4.6 でまとめた候補、Move 後の再呼出し |
| 呼出し側の照合 | 共有と排他の receiver、Loan を持たない結果、receiver の借用に依存する結果、ジェネリックな入れ子の `for`、別の要求の呼出し |
| `confined` | 可変 static（直接、呼出し先、遅延初期化、破棄）、Console、不明な呼出し、不変 static、static を参照する借用 Field、`confined` 要求への委譲 |
| `preserves results` | 本書 2.5 の委譲、二つの Field、別の d、呼出し先での呼出し、借用 Field `s/J during o`、item の破棄、領域分割 |
| 診断 | 本書 4 の位置・Reason・Advice、条件付きの設計変更、従属診断、CLI と言語サーバー |
| 割当て・性能 | 温まった再 Binding の割当てゼロ、上限のない Contract が増えても費用が変わらないこと |

単位の完了時には、`scripts/verify.ps1 -Class ... -Fixtures ...` で正式に検証し、native O0/O2 で既存の動作を確認する。最後に `-Mode Session` を一度実行する。計測は `src/Benchmark` で行い、証拠は `artifacts/verify/`、測定結果は `artifacts/benchmarks/` に置く。NativeAOT は実行しない。

## 6. 文書更新計画

正式な取り込みでは、英語で更新する。仕様に draft への依存は残さない。pre-alpha のため、言語バージョンの更新や移行文書は要求しない。

| 文書 | 更新内容 |
| --- | --- |
| SPEC | 本書 3.8 |
| LIBRARY | `BufferWriter` の行と Iterator の節に、宣言された上限と、それによって呼出し側に許されることを示す |
| STYLE | `[Kimi]` の規則として、要求の制約領域を Constraint、`origin`、`effect` の順に並べる。Contract 単位の `effect` 指定は `associate` 指定の後に置く。上限は呼出し側が必要とする場合にだけ宣言する（追加は実装者に、削除は呼出し側に影響する） |
| CODEMAP | 効果上限の行（`Binding.EffectBounds`、`OwnershipAnalysis.EffectBounds`、代表テスト）を加える |
| DIAGNOSTICS | 新しいコード、効果の経路を関連位置とする規則、条件付きの設計変更の示し方 |
| STATUS、PLAN、PLAN_HISTORY | 対応境界（ユーザーの上限、呼出し側の照合、残る環境効果の照合）、実装単位、履歴 |
| draft/INTEGRATED.md | 取り込みと同じコミットで、本書の各節の移行先を記録する |

## 7. 設計判断

### 7.1. 採用しなかった案

| 案 | 不採用の理由 |
| --- | --- |
| 現状を維持し、`Iterator` の詳細化を案内する | 非対称が残り、独自の要求に保証を付けられない |
| 一般的な効果体系（読み書きの集合、効果変数、効果多相） | 推論とジェネリクスが重くなり、原則2の有界な推論に反する。必要性も示されていない |
| `pure` など一語の包括的な上限 | 権限の閉包と結果非干渉は直交する。たとえば `reserve` は前者を満たすが、以前の Window とは競合する |
| 印の Contract（`contract Source: ResultIndependent`） | 効果は要求ごとの性質なので、型単位の印では対象の要求を表せない |
| 実装サマリーの和から上限を推論する | 開いた Contract では実装が分からない。閉じた Contract でも、実装を変えると公開契約が黙って変わる |
| 子 Contract の指定で要求の宣言自体を強化する | `I is LendingIterator` だけの呼出しにまで保証が漏れ、詳細化の意味が崩れる |
| 結果の依存を Origin の表記で判定する | 関連型や別名に隠れた依存、Storage への依存を見落とす |
| 呼出し側で `preserves results` を唯一の受理条件にする | Loan の競合規則より強くなる。receiver の借用に依存する結果（`preserves results` を宣言できない）や、Loan を持たない結果を保持したままの呼出しまで拒否してしまう |
| 委譲規則を任意のラッパーへ広げる | 新しい証明の仕組みが要る。既存の単一 Field の規則から型固有の部分を外すだけで、目的の範囲を覆える |
| 自分の要求にも Contract 単位の修飾指定を使い、形を一つにする | 保証がシグネチャから離れ、局所的に読めなくなる。オーバーロードの識別も必要になる。宣言と継承指定の使い分けは、`associate` の既存の先例と同じである |

### 7.2. Kimigayo Principles との対応

- **原則1：** 標準 Contract とユーザー Contract が、同じ構文で上限を宣言する。二つの形は、自分の要求と継承した要求という別々の状況に、一つずつ対応する。
- **原則2：** 保証は、呼出し位置の Constraint と公開宣言から決まる。実装の本体や実体化は調べない。
- **原則3：** 権限の閉包と結果非干渉を、別々の語で明示する。上限のない要求の環境への効果は、不明として扱う。
- **原則4：** 違反を効果の経路とともに示す。保証を満たす修正と、公開契約を弱める設計変更とを区別して提示する。意味の検査で、使える上限を参照できるようにする。

### 7.3. 評価

- **実現可能性：** 検査の基盤（推移的サマリー、上限の照合、領域分割、単一 Field の委譲）はすでにある。主な作業は次の四つで、新しい解析は要らない。呼出しの効果の求め方は、現在の `reserve` の合成を一般化したものである。
  - 識別による分岐を上限表に置き換える
  - 委譲の判定を一般の結果型で行う
  - 呼出し側の照合を加える
  - 構文を追加する
- **利点：**
  - ユーザーの API（Source、Sink、カーソル、Writer 類）が、ジェネリックな結果の保持や合成を、保証として使える。
  - 標準 Contract を、同じ規則で Kimigayo ソースに宣言できる。
  - 仕様では、§8.4.5・utf8-formatting §1.2・§22.1.2.4 に分かれた規則が一つの節にまとまる。
  - 違反の説明が具体的になる。
- **欠点と限界：**
  - 文脈語が四つ増える。
  - 上限を追加すると実装者が、削除すると呼出し側が影響を受けるので、API の進化に注意が要る。
  - 検査は保守的なので、より精密な解析なら受理できる実装を拒否することがある。
  - 自前の Storage から排他的な item を返す iterator は、分割の権限が内部にあるため、宣言しても検証に通らないことが多い。
  - 現実装には、ユーザーの関連型に最上位の借用を指定できない既存の制限がある。その解除は本案に含めない。
- **費用に見合うか：** 構文と規則の追加は小さく、既存の規則を再利用するので、仕様の総量はほとんど変わらない。呼出し側の照合と診断の改善は、本案がなくても既存仕様を満たすために必要である。語彙を閉じ、委譲を広げない限り、追加の複雑さに見合う。

本書は、仕様・ソース・既存テストとの静的な照合と、現行コンパイラのプローブに基づく。新しい規則の実装、実行試験、性能測定は未実施である。
