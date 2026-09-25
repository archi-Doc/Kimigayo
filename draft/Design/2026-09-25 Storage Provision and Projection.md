# Storage Provision と Projection

更新日: 2026-09-25

状態: 採用方針を反映した独立した仕様原案。正式仕様への統合・実装・実行検証は未実施。

旧 D1・D3〜D8 は推奨案を採用する。D2 は、通常の Non-Copy 取得では Move を行い、必要な時点で Completeness を検査する方針とする。途中で一部が未初期化になっても、必要になる前に再初期化できればよい。ただし、再代入の予定はアクセス権限を増やさない。

本書の規則は現仕様から独立している。例の構文・Contract は本原案のもので、現在のコンパイラーでの動作を示すものではない。例中の補助関数は、示した引数型に対応するものとする。

## 1. 基本モデル

> 格納側は、場所・許可する操作・依存関係を公開する。利用側は、その範囲内で操作を選ぶ。

```text
Storage                         データを格納する
  ↓ Projection                  対象の場所を選ぶ
Place + Storage Provision       格納型・経路・能力・依存関係
  ↓ Access                      取得または更新を検査する
Value / 更新結果
  ↓ Binding / Placement         値を格納する
Storage
```

| 用語 | 意味 |
| --- | --- |
| Storage | 識別可能な格納場所。ローカル、集約の部分、別領域のデータ、一時値の格納先など。 |
| 完全型 | semantics、型引数、内部の Origin を含む格納型。 |
| Place | Storage を特定した結果。通常の値型ではない。 |
| Provision | 宣言・型・アクセス経路が提供する操作とその条件。 |
| Completeness | 対象を一つの値として利用するための部分が、すべて初期化されていること。 |
| Origin / Loan | 借用先の有効期間を表す情報と、実際の借用・競合・親子関係。 |

完全型と Completeness は別である。型が確定していても、部分 Move 後の値は不完全になり得る。

Place は、完全型、storage の経路、Provision、依存する所有者・Origin・Loan を保持する。`Option<place(...)>` や Place 型の変数は作れない。場所を保存する場合は通常の参照値を取得する。サイズが 0 でも論理的な場所はあり、固有のアドレスは要求しない。

特定した場所と必要な所有者は、取得・更新が完了するまで有効でなければならない。借用を得た場合は、その Loan が必要な間も保護を続ける。関数が公開する場所も、結果の選択から callee の後始末を経て、この条件を満たす必要がある。

`for` は次の対象を、`match` は Pattern に対応する対象を選ぶ。取得・代入・束縛には共通の Access 規則を使う。

## 2. Storage Provision と経路

### 2.1. 能力と使用時の検査

基本能力は独立した三つとする。

| 能力 | できること |
| --- | --- |
| Read | 初期化された値を観察する。 |
| Write | 値を初期化・置換する。 |
| Take | 値と破棄責任を取り出し、元の場所を未初期化にする。 |

`Read → Write → Take` という強さの順序はない。所有する `let` は Move できても再代入できず、`uniq/T` の参照先は更新できても Take できない。

Provision は完全型の Copy 能力、Storage semantics、Accessibility、親からの投影、公開 API の制約を考慮する。使用時にはさらに、初期化状態、Completeness、Loan の競合、Origin、構築・破棄条件を検査する。借用中かどうかによって Contract 適合そのものが変わることはない。

### 2.2. Aggregate の投影

子の Place は、親の経路に子の宣言の制約を加えて求める。親の semantics で子の完全型を上書きしない。

- inline の部分は、親のアクセス上限と自身の可変性・公開範囲に従う。
- 共有経路から子の排他権限を作ることはできない。
- 子を借用した間は、その有効性に必要な親・所有者・格納領域を保護する。
- Move・更新の効果は、対象の部分と影響する祖先の状態に反映する。

異なるフィールド、Tuple の位置、固定長配列の異なる範囲内整数リテラル添字は、非重複の根拠にできる。括弧は添字の分類を変えない。動的添字やユーザー定義の投影は、別の式というだけでは非重複にならない。

要求は storage の投影経路にだけ伝播する。例えば `matrix[f()][j] = value` の更新要求は対象の行・要素に関係するが、`f()` の内部まで排他文脈にはしない。通常の関数・getter は宣言された引数・結果で扱い、その背後の storage へ要求を伝播させない。

### 2.3. スロットと参照先

`*r` は、安全な参照が指す直近の Place を表す。参照値も参照先も Copy／Move しない。暗黙の繰り返し dereference は行わない。

```kimi
var number: i32 = 1
let r = number@uniq
*r = 2                      // number を更新する。
// r = otherReference       // r 自身は let なので再代入不可。
```

`let` は格納スロットの不変性であり、参照先まで不変にするものではない。ただし、途中で共有借用を通った場合は、その共有上限を維持する。

参照値の Move はアクセス能力を移し、参照値の破棄はその保持を終える。どちらも参照先の値を Move・破棄する操作ではない。

| 経路 | 権限 |
| --- | --- |
| 直接保持する `uniq/T` | スロットが `let` でも T を排他的に扱える。 |
| `ref/uniq/T` | 内側の型が uniq でも、共有経路から排他権限を回復できない。 |
| `uniq/ref/T` | 参照スロットは更新できるが、T へのアクセスは共有。 |

所有ハンドルにも同じ分離を適用し、`*h` はハンドルの payload の Place を表す。直接保持する `obj/T` は `let` でも payload を排他的に扱える。`rc/T`・`arc/T` のスロットを更新できても、payload は共有である。共有経路を経由したハンドルは、その上限に従う。payload の投影は Take を提供しない。

オブジェクトの基底部分や不完全な View を、全体の値として置換してはならない。全体更新には、初期化の完全性に加え、実際の型・領域・破棄責任を含む置換対象の完全な識別が必要である。

## 3. 取得・初期化状態・寿命

### 3.1. 共通の取得規則

Place P の格納型を T とする。

| 指定 | 結果と効果 |
| --- | --- |
| 通常の値取得 | T が Copy なら Copy、そうでなければ Move。結果型はどちらも T。 |
| `P@ref` | P 自体を共有借用し、`ref/T` を得る。 |
| `P@uniq` | P 自体を排他借用し、`uniq/T` を得る。 |
| `P@owner` | Copy 能力によらず、格納された完全型の値を Move する。 |
| 通常の一時値 | 既に取得済みの値を、そのまま移す。 |

通常の値取得は、変数初期化、代入元、値引数、通常の return、所有された部分の分解に共通とする。失敗時に自動借用へ切り替えない。期待型は結果を制約するが、借用や参照先への変換を追加しない。receiver・演算子・制御構文が宣言上指定する取得方法も、同じ検査を使う。

Copy は完全型の性質であり、ユーザーコードの実行、資源の取得、参照カウントの増加を伴わない。`ref/T` は Copy、`uniq/T` と所有資源ハンドルは Non-Copy とする。Copy 集約は Non-Copy 部分や複製できない destructor 責任を持てない。

借用記法は常に直前の Place を対象にする。参照先の再借用は明示する。

```kimi
var n: i32 = 0
let r = n@uniq
let child = (*r)@uniq        // n の排他再借用。
*child = 1
let slot = r@ref             // r のスロットを借用。ref/(uniq/i32)。
```

各借用は実際の利用に応じて競合を検査する。`let child = r` なら、Non-Copy の参照値 r を Move する。参照先の借用にはならない。参照先の値を読む場合も `*r` を書き、参照層を暗黙に平坦化しない。

### 3.2. 操作の必要条件

| 操作 | 主な条件 |
| --- | --- |
| Copy | Read、対象が Complete、T が Copy。 |
| Shared Borrow | Read、対象が Complete、借用中の場所と依存関係が有効。 |
| Exclusive Borrow | Read・Write、対象が Complete、排他権限。 |
| Move | Take、対象が Complete、追跡可能な所有経路。 |
| Initialize | 対象が未初期化、初期化権限、新しい値の依存関係が有効。 |
| Replace | Write、旧値を合法に破棄でき、新しい値と保持中の依存関係を壊さない。 |

いずれも競合する Loan があってはならない。未初期化の場所への Initialize は可能だが、その前に安全な `uniq/T` を作ることはできない。

Take を追跡できる直接の場所は、所有ルート、アクセス可能な inline フィールド・Tuple 要素、範囲内整数リテラルによる固定長配列要素とする。最適化によってこの分類を変更しない。借用先・動的コレクション要素・関数が公開した借用 Place は Take を提供しない。取り出しには、状態と破棄責任を更新する `remove` などの操作を使う。

### 3.3. Completeness は必要になる時点で検査する

Move は対象を未初期化にし、Initialize は再び初期化済みにする。部分 Move 後も、親全体を使わずに特定できる初期化済みの部分は利用できる。

**再代入が後に書かれているだけでは不十分である。Completeness が必要な地点へ到達するすべての経路で、必要な部分が初期化済みでなければならない。**

Completeness が必要な地点は、対象全体の読み取り・借用・Move・全体を受け取る呼び出し、およびその全体を前提にする destructor の実行である。未初期化部分への直接代入は、親全体の Completeness を要求しない。

例では `Resource` は Non-Copy、`Box.value` は書き換え可能で、Box の destructor は完全な Box を必要とする。

```kimi
var box = Box.init(Resource.init())
let saved = box.value       // Move。box は一時的に不完全になる。
// inspect(box@ref)         // 不可: Box 全体の借用。
// return                   // 不可: 不完全な Box の destructor が必要。
box.value = Resource.init() // 未初期化部分を復元する。
inspect(box@ref)            // 可: Box は再び Complete。
```

部分 Move 時点で祖先に destructor があることだけを理由に拒否しない。分岐、通常の return、ループの `continue`・終了、`defer` を含め、実際の利用と後始末まで状態を追跡する。借用越しの Move は、後で復元しても Take がないため不可である。

各スコープの後始末は、ローカル・一時値の生成と `defer` の登録の逆順とする。inline 部分は宣言・要素順の逆順で破棄し、独自 destructor は自動的な部分の破棄より前に呼ぶ。`defer` による復元も、この実行順に従って各地点の状態へ反映する。

後始末には次の共通規則を適用する。

- 完全に Move 済みの値は、元の場所では破棄しない。
- 独自 destructor のない集約は、残っている初期化済み部分を通常の順序で破棄する。
- destructor を呼ぶ部分には、その呼び出し直前に Completeness を要求する。外側に destructor がなくても、内側のこの条件は消えない。
- 不完全な集約全体への代入も、残った旧状態をこの規則で破棄できる場合だけ許可する。
- `let` の Move は再初期化権限を作らない。再代入が必要なスロットは `var` とする。

Abort は巻き戻し・後始末を行わず、発散後には処理が続かない。これらの経路に実行されない destructor を要求しない。通常の失敗値の返却は、この例外には当たらない。

### 3.4. ジェネリックと寿命

未知の T に対する通常取得も結果型は T である。Copy の場合と Move の場合の効果を保持し、許されるすべての T について本体を検査する。

- Take がある所有ルートは、T が未知でも取得できる。
- Take がない場所から値を得るには、T が Copy であるという証明が必要。
- 通常取得後の元の場所は、Copy の場合だけ初期化済みである。以後の処理はこの条件付き状態を保つ。

Copy に応じて `T` と `ref/T` を切り替えないため、`SharedReadResult` は不要である。

借用は、参照先と必要な所有者・親 Loan を保持する。Origin が同じでも異なる Loan を合一せず、Origin 注釈で依存関係を消去しない。

通常の一時値は文の完了まで保持し、その後に後始末する。借用を保存しても参照先の寿命は自動延長しない。文を越えて使う場合は、所有値を先にローカルへ保存する。`for`／`match` の一時 Subject は、構文が明示的に作る内部所有変数に保持し、その構文の寿命を持つ。

## 4. Place を公開する Contract

### 4.1. 結果の能力と寿命

```text
place(ref, T) during origin
place(uniq, T) during origin
```

前者は共有借用と可能な Copy、後者はさらに排他借用・置換を公開する。どちらも Take や未初期化 storage を公開しない。`place(owner, T)` は設けない。

この結果を返す `return` は場所を指定する操作であり、通常の値取得ではない。格納型の一致、Complete、公開能力、callee の後始末後も続く依存関係を検査する。

直接の借用 receiver を持つ member は、外側の Origin を省略した場合、その receiver の借用 Origin に依存する。`during self` は self 変数のスロットの寿命ではない。他の入力に依存する場合は明示する。複数の依存関係がある場合、公開契約に必要な関係をすべて保持する。

`during self.source` は、実際の receiver Loan を不要にする指定ではない。独立性を証明できなければ、その Loan への依存も必要である。callee のローカルや破棄される一時値の場所は返せない。Cursor 内のキャッシュなど、計算後に存続する実在 storage は公開できる。

排他的な T 全体を公開する API は、通常の有効な T による置換を許可する。これで内部不変条件が壊れる場所は公開してはならない。例えば Dictionary の value は公開できても、key を自由に置換できる場所は公開しない。

計算 getter の結果は通常の値、setter は通常の呼び出しである。結果の一時 storage を借りても、元の隠れた storage の借用にはならない。宣言された操作を先に選び、失敗した Place アクセスを getter／setter へ再解釈しない。

### 4.2. Index

```kimi
contract Indexable<Key>
    associate Element
    func index(self: ref/Self, key: Key)
        -> place(ref, Element) during self

contract MutableIndexable<Key>: Indexable<Key>
    func indexUniq(self: uniq/Self, key: Key)
        -> place(uniq, Element) during self
```

`Element` は完全型であり、拡張側も同じ関連型を使う。共有版と排他版は同じ選択規則、すなわち添字・key の意味を公開する。読み取り・共有借用は `index`、更新・排他借用は `indexUniq` を選ぶ。receiver の現在の型から目的を推測せず、選んだ操作の権限を経路が満たすか検査する。失敗時に別の操作を試さない。

receiver と key は定められた順に各一回評価する。標準の安全な配列添字は範囲外で Abort し、Dictionary 添字は既存 value を選び、不在なら Abort する。不在を通常処理する API や挿入は別の操作とする。

直接の固定長配列投影は §3.2 の Move Path を保持できる。一方、抽象的な Indexable 適合から Take を導くことはできない。これにより、Non-Copy の `array[i]` の取得は、型だけでなく公開された経路にも依存する。

位置値の型名 `Index` とアクセス能力の `Indexable` は区別する。

## 5. 代入と束縛

### 5.1. Assignment

単純代入 `a = b` は次の順に実行し、Unit を返す。

1. b を評価し、共通規則で値を取得する。
2. 取得結果と破棄責任を確保する。
3. a の Place を一回特定する。
4. 未初期化なら Initialize、旧状態があれば合法な後始末の後に配置する。
5. 残った一時値を取得の逆順で後始末する。

旧状態の破棄によって新しい値や保持中の借用を無効にしてはならない。破棄が Abort／発散したら配置は実行されない。Move 済みの値を読まずに格納先を特定できるなら、`var a` に対する `a = a@owner` は再初期化できる。

### 5.2. 複合代入

`a += b` も右辺先行とし、b、a の場所、演算、配置の順に各一回行う。a の場所・依存関係は配置まで保持するが、未初期化になり得る対象に安全な参照を強制的に作らない。

旧値の取得方法は選択された演算子の契約に従い、通常の値取得なら Copy／Move、借用指定なら Borrow を行う。専用の Move 禁止規則は置かない。Move した場合の配置は再初期化となり、途中の Completeness と後始末は §3.3 に従う。借用先に Take がない場合は、ここでも Move できない。

演算結果が置換で無効になる旧状態の借用を含む場合は拒否する。演算子が要求する取得に失敗しても別の取得方法を試さず、添字・getter・場所を再評価しない。

### 5.3. let／var と Pattern

`let`／`var` は取得結果を保存する新しいローカル storage を作る。違いはそのスロットへの再代入権限だけであり、元の Place の別名にはならない。

`for`／`match` の Pattern もこの束縛を使う。

| 選ばれた格納場所 | 束縛する値 |
| --- | --- |
| 所有された部分 | 通常の値取得。Copy なら Copy、それ以外は Move。 |
| 共有借用された T の場所 | `ref/T`。Copy 能力によらず同じ。 |
| 排他借用された T の場所 | 権限がある場合の `uniq/T`。 |
| `_` | 取得せず、破棄責任は現在の所有者に残す。 |

Tuple／enum payload の分解にも同じ規則を再帰的に適用する。参照値を格納する部分も、その完全型のまま扱い、参照先を自動分解しない。参照先の分解には `match (*r)@ref`／`match (*r)@uniq` を使い、専用の dereference Pattern は設けない。

排他要求が read-only 部分に到達した場合はエラーであり、自動的に共有へ弱めない。共有・排他を混在させる API は、それぞれの参照を含む通常の値を返す。

## 6. 関連型と Origin

### 6.1. 完全型と Origin 引数

関連型は semantics・内部 Origin を含む完全型を表せる。Origin を引数に取る関連型も許可する。

```text
associate Item {step}              // 宣言
associate Item{step} is E          // 所有値など。step に依存しない。
associate Item{step} is ref/E during step
associate Item{step} is uniq/E during step
associate Item{step} is ref/E during source
```

`Item{step}` は通常の完全型であり、Place 型でも実行時の型計算でもない。`source` は既に宣言された Origin、`step` はその関連型の仮引数である。呼び出し側は適法な Origin を代入する。新しい Origin 名が既存の依存関係を隠すことはない。

本体は公開制約を満たすすべての Origin について検査する。内部 Origin と Loan の依存関係は保存し、型引数や可変 storage の自動的な共変性は仮定しない。関連型は正規化した完全型の同一性で照合する。共有参照の外側 Origin の短縮は outlives の証明、排他参照の短縮は適法な再借用を必要とする。

### 6.2. Contract 実装の照合

適合、要求、実装する宣言を先に特定し、署名の型同一性から関連型と Origin の対応を求める。Contract 内の短い関連型名はその要求を指す。外部で複数の同名要求がある場合は Contract 名で修飾する。

明示指定と全要求を合わせて一意に決まる情報だけ省略できる。照合には receiver、完全型、結果カテゴリ、Origin 引数と制約を含める。本体、暗黙変換、Copy の選択、都合のよい特殊化からは推論しない。能力制約だけでは具体型を選ばず、循環して一意に決まらない場合も明示を要求する。

## 7. 列挙

### 7.1. Cursor は Place を公開する

```kimi
contract Cursor
    associate Element
    func advance(self: uniq/Self) -> bool
    func current(self: ref/Self)
        -> place(ref, Element) during self

contract MutableCursor: Cursor
    func currentUniq(self: uniq/Self)
        -> place(uniq, Element) during self
```

作成直後と `advance()` が false を返した後には current がなく、current 操作は Abort する。true の後は、次の advance まで同じ論理的要素を公開する。false の後は終了状態を維持する。

要素 Loan が Cursor に依存している間、それと競合する advance・更新・破棄は禁止する。複数の current 取得にも通常の Loan 規則を適用する。キャッシュの要素と外部 storage の要素を、安全性の別規則にはしない。

### 7.2. Iterator は通常の値を返す

`for` が呼ぶ配送プロトコルは Iterator 一つとする。

```kimi
contract Iterator
    associate Item {step}
    func next(self: uniq/Self during step) -> Option<Self.Item{step}>
```

呼び出すたびに receiver の借用 Origin と Loan を定め、`Item{step}` に代入する。

| Item の定義 | 利用可能な期間 |
| --- | --- |
| `E` | 値が持つ通常の所有権・内部依存関係に従う。 |
| `ref/E during step`、`uniq/E during step` | その next の借用に依存し、保持中は次の next と競合し得る。 |
| `ref/E during source` など | step から独立していることを証明した、外部 storage の借用。 |

source が長生きするという注釈だけで、receiver Loan を消去できない。特に複数の排他要素を保持させる実装は、能力の分割と非重複を証明しなければならない。generic な利用側も、公開契約にない独立性を仮定できない。

共有 Cursor アダプターは、advance の成功後に current を借用し、`ref/E during step` を返す。排他版は `uniq/E during step` を返す。どちらも通常の Option 値であり、Place を Option に格納しない。

None の後は終了状態を維持する。Iterator の破棄は通常の後始末に従い、所有している未返却要素は破棄するが、借用したコレクション自体を破棄しない。Drain は取り出しと中断時の方針を持つ Iterator API とし、第三の言語プロトコルにはしない。

### 7.3. 列挙元を取得する Contract

```kimi
contract Iterable
    associate IteratorType {source} is Iterator
    func iterate(self: ref/Self during source) -> Self.IteratorType{source}

contract MutableIterable
    associate IteratorType {source} is Iterator
    func iterateUniq(self: uniq/Self during source) -> Self.IteratorType{source}

contract IntoIterable
    associate IteratorType is Iterator
    func intoIterator(self: Self) -> Self.IteratorType
```

借用版の IteratorType も Origin 引数を持ち、呼び出しごとの source に対応する。所有版は消費した Self の依存関係を保持し、消える引数スロットやローカルを借用して返してはならない。これらの関係は公開契約で検査する。

三つの能力は独立している。各モードは固定の入口を選び、すべて Iterator を返す。Cursor／Iterator の適合を探索して方式を変更せず、同名メソッドだけによる適合や別モードへの再試行は行わない。

### 7.4. for／match 共通の Subject 取得

| Subject の末尾指定 | 取得方法 | for の入口 |
| --- | --- | --- |
| `E` または `E@ref` | Shared | Iterable.iterate |
| `E@uniq` | Exclusive | MutableIterable.iterateUniq |
| `E@owner` | ByValue。Copy 型でも明示的な Move。 | IntoIterable.intoIterator |

この位置の最外側の指定を取得指定として一回だけ適用する。括弧は分類を変えず、内側の操作は通常どおり評価する。変数の `var`、本体、Copy 能力、適合の有無からモードを推測しない。

指定のない一時値も Shared とし、内部所有変数に保存して借りる。参照値や View の ByValue はその値の転送であり、参照先の所有権取得ではない。参照先を列挙するなら `for x in (*r)@uniq` のように書く。参照・Iterator 全体への自動的な列挙適合は設けない。

### 7.5. for の実行と束縛

1. 列挙元を一回取得し、選択した入口を一回呼ぶ。
2. 得た Iterator を内部の `var` に保持し、next を呼ぶ。
3. None なら終了する。Some の通常の値を、その反復の内部 item として保持する。
4. item に §5.3 の Pattern を適用し、通常のローカル変数を初期化する。単独の名前は `let` とする。
5. 本体終了・continue 時は、反復内の変数と残った item を後始末して次へ進む。
6. 終了・通常の外向き制御移動では、Iterator、残る列挙元を生成の逆順で後始末する。

外へ保存された値の Loan を、反復の終了という理由だけで打ち切らない。次の next と競合すればループの戻り経路も拒否する。移動済みの列挙元を二重破棄せず、残る不完全な所有値の後始末には §3.3 を適用する。

```kimi
for x in numbers             // Item は ref/i32。
    print(*x)

for x in numbers@uniq        // x のスロットは let、Item は uniq/i32。
    *x += 1                  // 元の要素を更新。

for var x in numbers@owner   // Item は i32。
    x += 1                   // ローカルの x だけを更新。
```

`for var x in numbers` は共有参照を可変スロットに保存するだけであり、参照先を更新する権限を増やさない。

参照で渡される Tuple 要素を分解する場合も、共通の dereference と match を使う。次の rows は `(i32, i32)` の列である。

```kimi
for row in rows@uniq
    match (*row)@uniq
        (let a, let b) => *a += *b
```

Dictionary の排他列挙は、通常の Tuple 値 `(ref/K, uniq/V)` を返せる。

```kimi
for (key, value) in dictionary@uniq
    inspectKey(key)          // 共有参照を渡す。
    update((*value)@uniq)    // value の参照先を再借用。
```

物理的な `(K, V)` の Place を作ったことにはせず、key は共有のままとする。参照を step に依存させる実装と、独立性を証明して source に依存させる実装を、通常の型・Loan の違いとして表す。

## 8. match

### 8.1. 選択と取得

Subject は §7.4 で一回取得する。Pattern は値を取得せず、対象の Tuple／enum payload の Place を選ぶ。arm の確定後、§5.3 に従って左から順にローカル変数を初期化する。

```kimi
match optionalNumber@uniq
    .Some(let number) => *number = 42
    .None => ()

match makeMessage()@owner
    .Text(let text) => consume(text)
    _ => ()
```

前者の number は `uniq/i32` を格納した `let` である。後者は所有した Subject を分解し、Non-Copy の text を取得・転送する。`@owner` を省略した一時 Subject は Shared になる。

payload の Loan が有効な間、Case や場所を無効化する enum 全体の変更はできない。所有 Subject の残った部分は通常の規則で破棄する。destructor のある内部 Subject を分解して不完全にした場合も例外はなく、後始末時に Complete にできなければ拒否する。借用 Subject の参照先は破棄しない。

### 8.2. guard

guard 中の候補名は、共有アクセスだけを許す候補 Place を表す。まだ arm のローカル変数ではない。Copy の読み取りは可能だが、Non-Copy の検査は `candidate@ref` と明示する。共有経路に Take がないため、通常取得による Move はできない。

候補への代入・排他借用・直接 capture は禁止する。新しく作った候補依存の Loan は、guard の終了までに終える。既存参照の Copy は元の依存関係を保持する。

Pattern 検査から guard の後始末まで、Case と候補位置を無効化する変更を防ぐ。false なら後始末して次の arm へ進み、true なら後始末後に元の候補 Place から束縛する。どちらも guard 中には payload を Move しない。独立した副作用の巻き戻しは行わない。

## 9. 実装と確認事項

### 9.1. 共通処理への対応

```text
ResolveProjection  → 対象の場所・型・能力・依存関係
PlanAccess         → Copy / Borrow / Move / Initialize / Replace
ValidateAccess     → 権限・状態・Loan・Origin・後始末の検査
BindOrPlaceValue   → 通常の格納
```

型が同じでも効果が異なる通常取得は、条件付きのアクセス計画として保持する。Index・for・match ごとに型計算や所有権規則を複製しない。コンパイル時の記述のために実行時のラッパーや割り当てを要求せず、最適化で評価回数・順序・状態・Loan・破棄責任を変更しない。

### 9.2. 必須の検証例

| ケース | 期待する結果 |
| --- | --- |
| 所有 Non-Copy の通常取得 | Move。元の場所は未初期化。 |
| 部分 Move 後、全経路で復元して全体を借用・破棄 | 許可。 |
| 復元前の全体利用、または不完全な値への destructor | 拒否。 |
| destructor のない部分 Move 済み集約の後始末 | 残った初期化済み部分だけを破棄。 |
| 借用先を Move して後で復元する計画 | Take がないため拒否。 |
| 未知 T の所有ルートを通常取得 | Copy／Move の両方の効果で検査。結果型は T。 |
| 未知 T の共有 Place を通常取得 | Copy の証明がなければ拒否。 |
| `ref/uniq/T` を経由した更新 | 共有上限により拒否。 |
| 借用要素を保持したまま次の next | 実際の依存関係・非重複証明に従って判定。 |
| false guard、continue、return、早期終了 | 取得状態と通常の後始末を維持。 |
| 添字を伴う単純・複合代入 | 右辺先行、対象の評価は一回。 |

診断は、要求操作、対象経路、失敗した条件を示す。能力不足、アクセス不可、未初期化／不完全、Loan 競合、Origin 不成立を区別する。

### 9.3. 対象外の詳細

本書は Storage／Operation の共通モデルを定義する。オブジェクト View と動的破棄の全規則、raw pointer・並行実行、非重複を保証する unsafe primitive の API、各 Drain の中断方針は別途定義する。未規定部分を、権限の追加や依存関係の省略の根拠にしてはならない。
