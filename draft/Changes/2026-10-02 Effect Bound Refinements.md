# 仕様変更案：要求の効果上限の補正

日付：2026-10-02（最終版）

状態：最終版。採用決定済み。正式仕様への取り込みと実装は未実施。

本書は、要求の効果上限（SPEC §8.4.10）を補正する。本書で変更する事項は、SPEC とその参照先より優先する。本書で変更しない事項には、既存仕様を適用する。SPEC の節は「SPEC §n」、本書の節は「本書 n」と表記する。例の「エラー」行は、意図した拒否例である。

## 1. 現仕様の問題

### 1.1. 委譲が、一般規則から外れた特例になっている

SPEC §8.4.10.5 の委譲規則は、次の二つを条件とする。

1. Self が J をただ一つの Field f に格納し、J を名指す値をほかに格納しない。
2. r の結果型が d の結果型と等しい。

そのうえで、「実装は f を通じた d の結果だけを返す」と断定し、`self.f` を通じた d の呼出しを照合から除く。この規則には二つの問題がある。

- **結論が条件から導けない：** 二つの条件は、結果の値がどこから来たかを制約していない。次の `Topped` は二つの条件を満たすが、`spare` を返すことがある。J の `next` が保証するのは、以前の `next` の結果との非競合だけである。そのため、公開された上限からは、`next` の効果が `spare` の Loan と競合しないことを導けない。
- **特例が多い：** 単一 Field、同じ結果型、覆う d は一つ、`zip`・`chain` は対象外、という限定がある。いずれも、要求呼出しの一般規則（SPEC §8.4.10.4）からは出てこない。そのため、結果を包んで返すだけのアダプター（`enumerate` など）も書けない。

```kimi
contract Refill
    associate Item
    func take(self: uniq/Self, spare: Option<Self.Item>) -> Option<Self.Item>
        effect preserves results

struct Topped<J>
    J is Iterator
    Self is Refill
    associate Refill.Item is J.Item
    var inner: J
    public func take(self: uniq/Self, spare: Option<J.Item>) -> Option<J.Item>
        match self.inner.next()
            .Some(let item) => return .Some(item@move)
            .None => return spare@move   // 現行では受理される。spare は f を通じた d の結果ではない
```

### 1.2. 祖先と同じ上限を再指定すると、エラーになる

SPEC §8.4.10.1 は、祖先が宣言済みの上限を再び宣言することをエラーとしている。そのため、祖先が後の版で同じ上限を加えると、それだけで子 Contract がエラーになる。子の宣言は、意味上は何も変えていない。

同種の重複について、SPEC §8.4.4 は「冗長な親への明示的な適合は許す」としている。これとも一致しない。

### 1.3. unsafe と効果の分類が曖昧で、抜け道がある

- **曖昧さ：** SPEC §8.4.10.2 の「Unsafe operations keep their own rules」は、上限の免除とも読める。また、この一文は `confined` の節にだけあり、`preserves results` との関係が書かれていない。
- **抜け道：** 実装は、raw pointer を通じたアクセスを効果サマリーに入れない。そのため、環境から得たポインターを通じた効果が、サマリーに現れない。
  - 不変 static に置いた raw pointer を読み、その指す先に書き込む。
  - 整数から作ったポインターで書き込む。対象環境がそのアドレスへのアクセスを保証する場合（SPEC §5.5）、たとえば次の例のようなデバイスの状態への書込みが、これに当たる。

  現在の利用者のコードが有効なポインターを得る手段は限られている。しかし、Kimi ライブラリーや、同時期に取り込む「raw pointer と unsafe の境界」の案（`@raw`、`Raw.allocate`）では、raw pointer を通常の道具として扱う。そのため、この抜け道は広がる。

```kimi
public func blink()
    unsafe
        let register = 0x40000000@unsafe/u32
        *register = 1    // デバイスの状態への書込みだが、現行ではサマリーに現れない
```

### 1.4. 入力を通じたアクセスの記述が狭い

SPEC §8.4.10.2 が環境への効果から除くのは、「入力の借用 Field を通じたアクセス」である。一方、SPEC §8.4.10.4 の規則 1 は、「receiver と各引数から参照をたどって到達する Place」を入力経由の効果としている。そのため、static Storage を指す借用を引数として直接受け取った場合に、それが許されるのかが §8.4.10.2 からは読み取れない。

### 1.5. 保証の根拠が、利用者から見えない

SPEC §8.4.10.6 が意味の検査に公開するのは、使える上限と、それを証明した Contract である。しかし、利用者のコードのどの前提から保証が来たのかは示されない。上限の宣言元が祖先にあるとき、利用者が書いた前提は、宣言元の Contract とは別の名前になる。

## 2. 理想的な動作

1. **委譲は一般規則で扱う：** 委譲を特例として扱わない。要求呼出しの一般規則（SPEC §8.4.10.4 の規則 3）と、結果の Loan の由来の追跡だけで判定する。
2. **権限は得る時点で分類する：** 環境への効果は、可変状態への権限を環境から得る時点で分類する。safe と unsafe で同じ規則を使い、unsafe はどちらの上限も免除しない。
3. **再指定はエラーにしない：** 子 Contract が祖先と同じ上限を再指定しても、エラーにしない。保証は一つとして扱う。
4. **保証の根拠を示す：** 呼出しで使える上限ごとに、宣言元の Contract と、それを証明した前提を示す。照合から除かれなかった Loan については、その理由を示す。

## 3. 仕様の変更点

### 3.1. 効果の分類（SPEC §8.4.10、§8.4.10.2）

#### 3.1.1. 原則

環境への効果は、**可変状態への権限を、入力以外の環境から得る操作**として分類する。権限を得た後のアクセスは、safe な参照を通じたものでも、raw pointer を通じたものでも、新たな環境への効果にはならない。

`confined` の定義（実装の推移的な効果サマリーに、環境への効果を含まない）と、分類できない効果を競合とみなす規則（SPEC §8.4.5）は変えない。本書は、環境への効果の列挙を、この原則に沿って整理する。

#### 3.1.2. 分類

| 操作 | 分類 |
| --- | --- |
| 入力（receiver と各引数）から参照やポインターをたどって得る。入力そのものも含む | 環境への効果ではない。参照先が static Storage にあっても、通常の Loan 検査に従う |
| 呼出しの中で作った Storage（割当てを含む）から得る | 環境への効果ではない |
| 不変 static を読む（raw pointer を含まない値） | 環境への効果ではない。初期化子はサマリーに含める |
| 可変 static Field にアクセスする（読取り、借用、書込み、置換、破棄、初回アクセス時の初期化） | 環境への効果（現行どおり） |
| 外部状態にアクセスする標準操作（Console 出力など） | 環境への効果（現行どおり） |
| 不変 static から、raw pointer を含む値を読む | 環境への効果（新規）。指す先は不変性で保護されないので、可変状態への権限を環境から得たことになる |
| 整数をポインターへ変換する | 環境への効果（新規）。provenance を持たないポインターを作るため。参照するには、対象環境の保証が要る（SPEC §5.5） |
| 環境への効果が分からない呼出し（本体のない外部関数の呼出しを含む） | 分類できない効果。どちらの上限でも競合とみなす（SPEC §8.4.5、現行どおり）。外部関数の呼出しは、raw pointer の案の取り込み時に環境への効果へ移る（本書 7） |

割当てと Abort は、現行どおり許す。

#### 3.1.3. unsafe と raw pointer

- **免除しない：** unsafe は、どちらの上限も免除しない。Unsafe Block の中にあることは、上限を満たす証拠にならない。本書 3.1.2 の分類は、safe と unsafe で同じである。
- **raw pointer を通じたアクセス：** 権限を得た時点で分類が済んでいるので、アクセスそのものは環境への効果として数えない。保持中の Loan と競合しないことと、provenance の条件を満たすことは、SPEC §5 の義務である。
- **外部関数：** 効果を表明する手段は設けない（Appendix D の延期を維持する）。本体のない外部関数の呼出しは、現行の SPEC では分類できない効果とする。

§8.4.10.2 の「Unsafe operations keep their own rules」は削除する。

#### 3.1.4. 例

```kimi
contract Sink
    func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        effect confined

group Native
    #LibraryImport("logger", "write_log")
    public unsafe func writeLog(value: i32) -> ()

struct RawSink
    Self is Sink
    var slot: unsafe/i32
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        unsafe
            *self.slot = value                      // OK：self が保持するポインター（入力から得た権限）
        return .Ok(())

struct DeviceSink
    Self is Sink
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        unsafe
            let register = 0x40000000@unsafe/i32    // エラー：整数から作ったポインターは環境から得た権限
            *register = value
        return .Ok(())

struct ForeignSink
    Self is Sink
    public func put(self: uniq/Self, value: i32) -> Result<(), BufferFull>
        unsafe
            Native.writeLog(value)                  // エラー：外部関数の効果は分類できない
        return .Ok(())
```

static を指す借用でも、入力として受け取ったものなら環境への効果ではない。

```kimi
struct Counter
    var count: isize = 0
    public func increment(self: uniq/Self) => self.count += 1

contract Tally
    func add(self: ref/Self, total: uniq/Counter)
        effect confined

group Metrics
    var total: Counter = Counter()

struct Adder
    Self is Tally
    public func add(self: ref/Self, total: uniq/Counter)
        total.increment()                     // OK：入力 total から得た権限

func bumpGlobal<T>(tally: ref/T)
    T is Tally
    tally.add(Metrics.total@uniq)             // 呼出し側が static の借用を明示的に渡す
```

### 3.2. 委譲（SPEC §8.4.10.5）

#### 3.2.1. 規則

§8.4.10.5 の条件と特例をすべて削除し、次の一つの規則に置き換える。

> `preserves results` を持つ要求 r の実装を検査するとき、r の結果が保持する Loan を、既存の依存追跡でたどる。その Loan が、self から Field の経路で到達する値 w に対する要求 d の呼出しの結果に由来するなら、「w に対する d の以前の結果の Loan」として扱う。

この規則の後は、SPEC §8.4.10.4 の規則 3 がそのまま働く。つまり、w の d に `preserves results` が使えるなら、後の r の呼出しの中で w に対して行う d の効果は、その Loan と照合しない。

- **経路：** Field の経路には、借用 Field（`s/J during o`）を通じた到達も含む。d の結果は w の Storage にも receiver の借用にも依存しないので（`preserves results` の適格性）、借用 Field を介した効果は、その結果と競合しない。
- **値の同一性：** r の本体のどこかで、w に至る経路の値（Field の値や、借用 Field の参照先）を置換・交換・Move する場合は、呼出しをまたいで w を同じ値とみなさない。その場合、除外は適用しない。
- **それ以外の Loan：** 引数、別の要求の結果、別の値の結果から来た Loan は、除外せず、通常どおり照合する。

#### 3.2.2. 削除する特例とその帰結

削除する特例は、いずれも一般規則から帰結として導かれる。

| 削除する特例 | 一般規則での帰結 |
| --- | --- |
| J を格納する Field は一つだけ | 二つの Field を持つ場合、片方の d の効果は、もう片方の結果の Loan と照合される。競合すれば拒否する |
| 結果型が等しい | 不要。結果を包んで返しても、由来が d の結果であれば除外される |
| 覆う d は一つだけ | 規則 3 は同じ要求の結果しか除かないので、自動的にそうなる |
| `zip`・`chain` は対象外 | 特別扱いはなくなる。Loan の規則に従って、受理か拒否かが決まる |
| `confined` には委譲規則が要らない | 委譲規則そのものがなくなるので、記述も不要になる |

#### 3.2.3. 例

```kimi
struct Drain<J>
    J is Iterator
    Self is StableSource
    associate Source.Item is J.Item
    var inner: J
    public func take(self: uniq/Self) -> Option<J.Item>
        return self.inner.next()                // OK

struct Numbered<J>
    J is Iterator
    Self is StableSource
    associate Source.Item is (isize, J.Item)
    var inner: J
    var index: isize = 0
    public func take(self: uniq/Self) -> Option<(isize, J.Item)>
        match self.inner.next()
            .Some(let item)
                self.index += 1
                return .Some((self.index, item@move))   // OK：現行では結果型の条件で拒否される
            .None => return .None

struct Merge<J>
    J is Iterator
    Self is StableSource
    associate Source.Item is J.Item
    var left: J
    var right: J
    public func take(self: uniq/Self) -> Option<J.Item>
        match self.left.next()                  // エラー：right から得た以前の結果の Loan と競合し得る
            .Some(let item) => return .Some(item@move)
            .None => return self.right.next()
```

本書 1.1 の `Topped` も拒否する。`spare` の Loan は除外されないので、`self.inner.next()` の効果と照合される。その効果は、J の Origin が示し得るすべての Loan に排他的に到達し得るので、競合となる。

### 3.3. 上限の再指定（SPEC §8.4.10.1）

- **同じ Contract 内の重複：** 同じ要求に同じ上限を二度宣言するのは、現行どおりエラーとする。
- **祖先の上限の再指定：** 許可する。
  - 祖先の上限と合わせて一つの保証として扱うので、両方があっても義務と保証は増えない。
  - 祖先の宣言が後でなくなっても、子の宣言は残る。
  - 検査は、現行どおり、各上限について witness ごとに一度だけ行う。
- **慣習：** 祖先が宣言済みの上限は、再指定しない（STYLE）。

```kimi
contract Source
    associate Item
    func take(self: uniq/Self) -> Option<Self.Item>
        effect preserves results           // 後の版で追加された

contract StableSource: Source
    effect Source.take preserves results   // 現行ではエラー。改訂後は許可（保証は一つ）

contract StrictSource: Source
    effect Source.take confined
    effect Source.take confined            // エラー：同じ Contract で二度宣言している
```

### 3.4. 診断と意味の検査（SPEC §8.4.10.6）

- **宣言：** `InvalidEffectBound_Kd` の対象のうち「duplicate bound」を、同じ Contract 内の重複に限る。
- **適合：** `IncompatibleContractImplementation_Kd` の Reason に、次を加える。
  - **環境への効果の場合：** 権限を得た操作の種類を示す（本書 3.1.2）。主範囲は、権限を得た操作とする。
  - **以前の結果の Loan との競合の場合：** その Loan が照合から除かれなかった理由を示す。理由とは、出所が引数・別の要求・別の値であること、または r の本体が経路の値を置換することである。その出所や置換の位置を、関連位置に加える。
- **意味の検査：** 呼出しで使える上限ごとに、宣言元の Contract と、それを証明した前提を示す。宣言元が複数ある場合（本書 3.3）は、すべて示す。

次の例の `Source` と `StableSource` は、SPEC §8.4.10.1 の例と同じものとする。つまり、上限を宣言するのは `StableSource` だけである。

```kimi
contract SafeSource: StableSource

func takeTwo<S>(source: uniq/S) -> (Option<S.Item>, Option<S.Item>)
    S is SafeSource
    let first = source.take()
    let second = source.take()
    return (first@move, second@move)
```

```text
呼出し：source.take()
要求：Source.take
使える上限：preserves results
宣言元：StableSource
前提：S is SafeSource
```

### 3.5. 変更しないこと

- 上限の語彙、構文、文脈語
- `confined` の定義と、分類できない効果を競合とみなす規則（SPEC §8.4.5）
- `preserves results` の適格性と保証
- 使える上限の決め方と、要求呼出しの効果の規則 1〜3（SPEC §8.4.10.4）
- 標準宣言（`BufferWriter.reserve` の `confined`、`Iterator` の `preserves results`）
- 外部関数に効果を表明する手段を設けないこと（Appendix D）

## 4. 評価

### 4.1. メリット

- **健全性：**
  - 委譲で照合から除く Loan が、公開された上限から導ける範囲に収まる。
  - 環境から得たポインターによる効果が、サマリーから漏れなくなる。
- **原則 1：**
  - 委譲は規則 3 の適用になり、特例がなくなる。
  - 効果の分類は、safe と unsafe で一つの原則にまとまる。
  - 上限の重複は、SPEC §8.4.4 と同じ考え方で扱う。
- **原則 2：** 保証は、公開された上限と実装自身の本体から決まる。子 Contract の再指定は、祖先が変わっても壊れない。
- **原則 3：** unsafe が上限を免除しないことと、権限を環境から得る操作が明示される。
- **原則 4：** 照合から除かれなかった理由と、保証の根拠となった前提を示せる。
- **表現力：** `Numbered` のように結果を包むアダプターや、競合しない複数 Field の実装が書ける。

### 4.2. デメリットと費用

- **実装：**
  - 委譲の判定に、結果の Loan の由来の追跡が必要になる。これには、所有権解析が保持する結果の依存を使う。
  - 新しく分類する二つの操作を、サマリーに加える必要がある。
- **拒否が増える：**
  - `Topped` のように、別の出所の結果を返す `preserves results` の実装を拒否する。
  - `DeviceSink` のように、整数からの変換や、raw pointer を含む不変 static の読取りを行う `confined` の実装を拒否する。この二つは、得たポインターを使わなくても拒否する（保守的）。
- **受理が増える：** 判定を一般規則に委ねるので、受理・拒否の理由を診断で示す必要があり、テストも増える。
- **外部関数：** 上限を持つ実装の中では、外部関数を呼べない（現行の挙動のまま）。raw pointer の案の取り込み後は、`confined` の実装からだけ呼べなくなる（本書 7）。
- **再指定：** 同じ保証を「書く／書かない」の二通りで表せる。そのため、STYLE で一つに揃える。

### 4.3. 複雑性

規則は全体として減る。

- **削る：**
  - 委譲の条件 1・2
  - 「覆う d は一つ」
  - `zip`・`chain` と `confined` についての記述
  - 祖先の上限との重複検査
  - unsafe についての一文
- **加える：**
  - 委譲を規則 3 に結び付ける一つの規則
  - 環境への効果の列挙に、二つの操作
  - 診断の Reason

**実装の現状（2026-10-02）：** 効果上限の実装（`2026-10-01 Requirement Effect Bounds.md`）は、次の段階にある。

- U1（構文、`4eb5c175`）と U2（宣言の検査、`5e653db1`）は実装済みである。本書 3.3 のため、U2 の重複の検査に小さな修正が要る。
- U3（実装側の検査）は作業中である。本書 3.1・3.2 を U3 の中で直接実装すれば、作り直しは生じない。旧規則のまま U3 を完了すると、委譲と分類の判定を作り直すことになる。

### 4.4. 検討して採らなかった案

| 案 | 採らなかった理由 |
| --- | --- |
| 二つの上限の独立性と、使える上限の決め方を書き直す | SPEC §8.4.10.1・§8.4.10.4 にすでにあり、重複するだけである |
| `confined` を「権限の由来」による肯定形の定義に置き換える | 定義が二つになる。否定形の列挙を保ち、それを取得の観点で整理する方が、既存の効果サマリーをそのまま使える |
| 委譲の条件を保ったまま、「r は d の結果だけを返す」を受理条件に加える | 特例が残る。一般規則と由来の追跡で、同じ健全性が得られる |
| raw pointer を通じたアクセスを、分類できない効果とする | Dictionary の削除など、raw pointer で書かれた Kimi ライブラリーの操作を、上限を持つ実装から使えなくなる |
| raw pointer を通じたアクセスの環境への効果を、unsafe を書いた者の義務とする | 安全な関数の中に効果が隠れる。義務を負う者は、上限のことを知らない。Loan との衝突だけを義務とする（SPEC §5） |
| 外部関数の宣言で効果を表明できるようにする（`effect confined`） | 本書の範囲外とする。Appendix D の延期を維持し、必要な例が出たときに別提案とする |
| 保証と根拠を共通の意味記録で扱う、と仕様に定める | 実装の設計である。仕様では表示する内容だけを定める |

## 5. 実装計画

本書の変更は、`2026-10-01 Requirement Effect Bounds.md` の実装単位に組み込む。単独の実装単位は作らない。進め方と検証は、その計画書と AGENTS.md に従う。

| 単位 | 追加する作業 | 追加する完了条件 |
| --- | --- | --- |
| U2：宣言と上限表（実装済みの修正） | 重複の検査を、同じ Contract 内に限る。祖先の上限の再指定は、上限表で一つの保証にまとめ、宣言元をすべて記録する | 本書 3.3 の三つの例が、意図どおり受理・拒否される |
| U3：実装側検査（作業中。本書の規則で完了させる） | 次の三つを行う。<br>・**分類：** 本書 3.1 を実装する。整数からの変換と、不変 static からの raw pointer の読取りをサマリーに加える。raw pointer を通じたアクセスは、環境への効果として数えない。<br>・**委譲：** 現行の型固有の判定と委譲の条件を、本書 3.2 の規則に置き換える。由来は、所有権解析の結果の依存で判定する。<br>・**記録：** 照合から除かなかった理由と、権限を得た操作を記録する。 | `RawSink`・`Adder`・`Drain`・`Numbered` を受理する。`DeviceSink`・`ForeignSink`・`Merge`・`Topped` と、本体で経路の値を置換する実装を拒否する。既存の Iterator と reserve の効果テスト、Kimi ライブラリーの適合（`OwningIterator`・`BorrowingIterator` を含む）が通る。上限を持つ実装から到達するライブラリーの経路に、新しく分類した操作がないことを確認する |
| U5：診断と意味の検査 | 本書 3.4 を実装する。前提は、既存の適合証拠を参照して表示する。呼出しごとに記録を作らない | Reason・関連位置・hover を、CLI と言語サーバーで確認する。温まった再 Binding の割当てゼロを維持する |

## 6. 文書更新計画

| 文書 | 更新内容 | 時期 |
| --- | --- | --- |
| SPEC §8.4.5 | 分類できない効果の文に、§8.4.10.2 の分類への参照を加える | 取り込み時 |
| SPEC §8.4.10 | 冒頭に、本書 3.1.3 の「unsafe は免除しない」を加える | 取り込み時 |
| SPEC §8.4.10.1 | 本書 3.3 | 取り込み時 |
| SPEC §8.4.10.2 | 本書 3.1 の原則と分類で、環境への効果と許すものの記述を置き換える。unsafe の一文を削除する。例に `DeviceSink` と `Adder` を加える | 取り込み時 |
| SPEC §8.4.10.5 | 本書 3.2 で全体を置き換える。例は `Drain`・`Numbered`・`Merge`・`Topped` とする | 取り込み時 |
| SPEC §8.4.10.6 | 本書 3.4 | 取り込み時 |
| SPEC §22.1.2.4 | 「single-Field delegation rule」を「§8.4.10.5 の委譲」に改める。「`zip` や `chain` は、この上限の下では Iterator ではない」を削除する | 取り込み時 |
| SPEC Appendix E | `confined` の項を、§8.4.10.2 の環境への効果を参照する形に改める | 取り込み時 |
| `docs/STYLE.md` | 「祖先が宣言済みの上限は再指定しない」を加える | 取り込み時 |
| `draft/INTEGRATED.md` | 本書の取り込みを記録する。`2026-10-01 Requirement Effect Bounds.md` の行に、本書による補正の注記を追記する（凍結した本文は変更しない） | 取り込み時 |
| `docs/dev/DIAGNOSTICS.md` | 照合から除かなかった理由と、権限を得た操作を示す Reason | 実装時 |
| `docs/STATUS.md`、`docs/dev/PLAN.md` | 効果上限の実装と合わせて更新する | 実装時 |
| `docs/LIBRARY.md`、`docs/dev/CODEMAP.md` | 変更しない | — |

## 7. raw pointer の案との関係

本書は、`2026-10-02 Raw Pointers and Unsafe Boundary.md`（以下「raw pointer の案」）と同時期に処理する。二つの案は別々に取り込み、接点は次のとおり分担する。

- **順序：**
  - 仕様の取り込みは、本書を先、raw pointer の案を後とする。
  - 実装は、本書を含む効果上限の U3 を、raw pointer の案の単位 3〜5 と単位 7 より先に完了する（その案の 7 章）。
- **原則：** 「unsafe は検証の委譲であり、規則の免除ではない」（raw pointer の案の 3.1）は、本書 3.1.3 と同じ立場である。raw pointer の案を取り込むときに、§8.4.10 冒頭の文を §5 冒頭への参照に置き換える。
- **raw pointer の案が担う事項：** 次は、raw pointer の案が導入するものの分類なので、その案で定める。いずれも、本書 3.1 の原則に従う。
  - `@raw` を Place へのアクセスとして扱うこと（その案の 3.5）
  - `Kimi.Raw` の関数を、割当てと raw アクセスとして扱うこと（同 3.9）
  - safe になった変換でも、整数からの変換は環境への効果のままであること（同 3.3）
  - raw アクセスと Loan を、anchor の規則で比較すること（同 3.8）
  - 外部関数の呼出しを、宣言の約束に基づいて、分類できない効果から環境への効果へ移すこと（同 3.11）。その結果、`preserves results` の実装からは呼べるようになり、`confined` の実装からは引き続き呼べない。
  - 本書の例の `unsafe/` を `raw/` に改めること

## 8. 未確認事項

**呼出しの間の経路の置換：** 本書 3.2.1 で同一性を失うとするのは、r の本体の中での置換だけである。r の呼出しと呼出しの間に、Self の別のメンバーが経路の値を置換する場合は、呼出し側の既存の Loan 追跡によって、以前の結果と新しい値との競合が防がれると考えている。新しい値を作るには、以前の結果が保持する Loan と同じ参照先を借用し直す必要があるためである。ただし、これは証明していない。U3 で再現例を作って確認し、反例が見つかれば規則を加える。
