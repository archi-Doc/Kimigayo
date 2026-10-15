# raw記憶領域を通常の記憶領域にする

**状態：** 提案。2026-10-16の最終版。取り込みも実装もまだで、採否はオーナーが決める。独立に書いた2つの設計を比較した。
採らなかった選択肢は3.3節に残す。

本書は[英語版](2026-10-15%20Raw%20Storage%20as%20Storage.md)の日本語版である。引用と提案文は訳文で、取り込みには英語版の
文面を使う。用語は[SPECJ_TERMINOLOGY](../../docs/SPECJ_TERMINOLOGY.md)に従う。raw storageは「raw記憶領域」と書く
（raw Place、rawポインターと同じ扱い）。

`NN:行`はSPECまたはIMPLの第NN章の行（20–21章がIMPL）、`ファイル:行`はその他のファイルの行を表し、どちらも`8ffc296b`時点で
ある。§はSPECとIMPLの節を指し、本書自身の節は「3.3節」と書く。**N**は規範変更、**D**は再現例、**E**は例、**RZ**は後続課題、
**（H）**は仮説を表す。

## 1. 概要

異なる0サイズの値は同じアドレスを共有できる（SPEC §4.1）。ところがSPEC 5はraw記憶領域の状態をアドレスごとに持つため、初期化、
Take、借用、`Raw.slice`、解放のいずれも、どの値に作用するかを定められない。SPEC自身の`Buffer<T>`も、0サイズでNon-Copyの`T`
では未定義になる（D1）。D2–D6はほかの場合を示す。

本提案はraw記憶領域を通常の記憶領域にする。

- **Place。** raw記憶領域に配置した値はPlaceを占め、その各構成要素はそのPlaceの部分を占める（N1）。初期化・完全性・権限・
  Loan・破棄のPlaceごとの規則が、そのままraw記憶領域に適用される。
- **指す先の決定。** raw Placeが指すのはアドレスではなく、ポインターのprovenance（由来の領域）内のPlace 1つである（N5）。
  指すPlaceの選び方が1つでもあり、それで全操作が規則を満たす間は、動作が定義される（N8）。
- **配置と完全性。** `Raw.initialize`、再配置、契約による構築を1つの配置規則で扱う。埋めるPlaceへの書き込みはrawアクセス条件に
  従い、そのPlaceは値を保持しておらず、祖先以外の初期化済みPlaceはそのバイトを1つも保持していない。値全体へのrawアクセスには
  完全値が要る（N9）。
- **バイト。** 型の読み替えアクセス（N14）と、バイトコピーによる再配置（N15）を明示的な規則にする。
- **重なり。** 定義は§3.4の1か所だけ（N2）。

D1–D5は有効になり、D6はRZ4–RZ6の後に有効になる。2.3節は関連するプログラムの判定も示す。

**オーナーの判断：**

- 4節の採用。SETTLEDの新項目とPLANの行RZを含む。
- 意図的な5つの選択：
  - 新しいPlaceを作る配置はraw記憶領域に限る。そのため、Move済みの局所変数を別の型の値の置き場に使うことは未定義になる。
  - 整数から作ったポインターは、0サイズのアクセスでも実装の保証を要する。Rustより厳しい。
  - 契約が構築を定めないバイトコピーは値を再配置し、複製はしない。
  - 解放の前提条件は0バイト結果にも適用する。ただし0バイト結果は何度でも解放できる。
  - 破棄してもCopyの構成要素を破棄するだけのNon-Copy値（ライブラリーの`Links`など）は、解放の前に破棄しなくてよい。配置した
    `Array`、`string`、ハンドル、クロージャーは破棄が要る。

## 2. 問題

### 2.1. 求められる意味

サイズによらず、どの値も自身の初期化状態、構築完了、構成要素、Loan、破棄責任を持つ（SPEC §3.4、§15.1、§16）。IMPL §21.1.1は
0サイズについて「0サイズの値も評価・初期化・所有権・Loan・破棄を保つ」と定める。strideは0でもよく（§4.1）、`p[n]`は
`*(p + n)`そのものである（§5.3）。

対象となる形の族：すべてのraw操作（形成、読み取り、Copy、置換、Take、その場の破棄、共有・排他借用、`Raw.initialize`、
`Raw.slice`、`Raw.release`）、同じ値への安全な操作、重なり、data race（§22.3.7）、再配置。

### 2.2. 原因

§3.4の記憶領域の種類（03:243）にraw記憶領域がない。そのため、rawポインター経由で配置した値はPlaceを持たない。SPEC 5は
rawの状態をアドレスごとの「記憶領域」で持つことになり、アドレスごとの状態では0サイズの値を区別できない。バイトコピーも値を
作らない（05:36）ので、バイトコピーで値を移すことにはサイズによらず意味がない。

### 2.3. プログラム

例は次の共通宣言を使う。

```kimi
struct Token                                // 0サイズでNon-Copy。dropが観測できる。
    drop => Console.writeLine("drop")
```

**再現例：**

| | プログラム | 現行SPEC | 提案後 |
| --- | --- | --- | --- |
| D1 | §5.6の`Buffer<Token>`に1回push、その後drop | 未定義：`_ = data[0]@move`が長さ0のprovenance（05:126）の一つ先（05:34）を逆参照する | 有効：pushした値ごとにdropが1回 |
| D2 | `Buffer<()>`に2回push | 未定義：記憶領域が既に値を保持している（05:128） | 有効：同じアドレスに2つのPlace（E1） |
| D3 | `Array<Token>`の所有反復：`takeFirst`（Storage.kimi:107）、その後残部のdrop（:42） | 未定義：最初のTakeで記憶領域が未初期化になる（05:67） | 有効：各Takeが別のPlaceを指す。ただし:43が`allocate`結果を解放することをRZ6で明記する必要がある |
| D4 | 0サイズ要素への`Storage.tryGetPairUniq`（Storage.kimi:182） | 未定義：2つ目の`uniq`が1つ目の記憶領域にアクセスする（05:63） | 有効：異なる2つのPlace |
| D5 | `Raw.allocate<(i64, Token)>(1)`、initialize、`_ = (*p).1@move`、解放 | 未定義：要素は破棄もTakeもされておらず、SPEC 5は構成要素の状態を持たない（05:127） | 有効：構成要素のPlace（E3） |
| D6 | `Dictionary<K, V>`の拡張（DictionaryStorage.kimi:91）と詰め直し（:135）。`K`と`V`は任意 | 未定義：バイトコピーは何も配置しない（05:36）ので、新しいスロットは値を保持せず、解放する旧バッファーは保持したまま | RZ4が`Links`の値を配置し、RZ5とRZ6がHandleの`buffer`フィールドから読んだポインターと`tryAllocateBytes`の結果にprovenanceを与えれば有効：再配置がコピーする各範囲の値を移す（E5） |

D6のレイアウト（BodyLowering.Dictionaries.cs:21-26）：スロットは16バイトの`Links`、キー、値の順で、それぞれのアラインメントに
合わせて置く。strideはスロットのアラインメント（8以上）の倍数に切り上げる。したがって`Dictionary<i64, ()>`の`()`は
`slot + 24 = slot + stride`、つまり次のスロットの先頭にある。

**関連するプログラム：**

| 規則 | プログラム | 現行SPEC | 提案後 |
| --- | --- | --- | --- |
| 指す先の決定（N5、N8） | 配置前の読み取り。0サイズの値（Non-Copy、Loanを持つもの、`Never`）の偽造 | 未定義（05:43） | 未定義：値を保持するPlaceがない |
| | `x[0]@raw`から同じアドレスの0サイズの`x[1]`へ到達 | 0サイズでは不明確（05:42） | 未定義：provenanceは`x[0]`の記憶領域 |
| | 互いに順序付けのない2つのスレッドが、唯一の0サイズの値をそれぞれTake | 表現不可（15:214） | 両方のTakeから未定義 |
| 配置（N9） | 別の初期化済みPlaceが保持するバイトへの`Raw.initialize`。別の値をそのバイト上に配置した後で、Move済みのPlaceを再初期化する場合を含む | 未定義（05:128） | 未定義（E2） |
| | 値をMoveした宣言済み局所変数への、別の型の値の配置 | 有効（05:128） | 未定義：新しいPlaceを作る配置はraw記憶領域に限る。後の安全な初期化がその値と重なる |
| | ある0バイト結果経由の`Raw.initialize`の後、同じアドレスの別の結果経由でTake | 規定なし | 未定義：各結果は自身のprovenanceを持つ（N16） |
| | 生きた`uniq`経路から得たポインターで値をTakeし、配置し直すこと | 有効 | 有効：ポインターの取得元経路のLoanは除外される（条件4） |
| 完全性（N9） | 一部をTakeした値への、値全体のraw読み取り・置換・Take | 規定なし（15:86） | 未定義（E3） |
| | 配置した正サイズの値の全構成要素をTakeした後の、値全体の`Raw.initialize` | 規定なし | 未定義：不完全値がまだバイトを保持する。構成要素ごとに配置し直す。0サイズではこの呼び出しが新しい値を配置する |
| メモリー割当てと解放（N16、N17） | `count`を超える数の0サイズの配置 | D2と同じく未定義（05:128） | 有効：`count`が制限するのはバイト数で、値の数ではない（E6） |
| | 配置した`Token`が残ったままの解放 | 規定なし | 未定義（E6）。`()`や、破棄してもCopyの構成要素を破棄するだけの値（`Links`など）なら有効。配置した`Array<Token>`が残れば未定義 |
| | 解放済みの0バイト結果経由の配置 | 規定なし | 未定義：解放でそのprovenanceが終わる |
| | 0バイト結果の2回目の解放 | 有効（05:127） | 有効 |
| 型の読み替え（N14） | 整数のバイトの`raw/u8`読み取り。1つの`u64`の異なるバイトへの2つの`uniq/u8`借用 | 読み取りは有効。借用は同じ記憶領域にアクセスする（05:63） | 有効：バイトを共有しない読み替えPlace（E4） |
| | パディング、またはenumの選択中のケースが使わないバイトの読み替え読み取り | 規定なし | 未定義：そのバイトは値を表さない。パディングへの読み替え書き込みは有効（E4） |
| | Takeした構成要素のバイトの`u8`読み替え読み取り。たとえば`pair.1@move`後の`(u64, string)`の`pair.1` | 未定義：記憶領域は未初期化（05:43、05:47） | 未定義：値を保持しない部分は読み替えの対象外 |
| | `bool`が次に読まれる時点で2を保持したままになる読み替え書き込み | 規定なし | 未定義（E4） |
| | ケース`Empty`と`Full(Token)`を持つNon-Copyのenumで、`Empty`を`Full(Token)`に変える読み替え書き込み | 規定なし | 未定義：読み替えはペイロードを構築しない |
| | 配置済みの値の内側のアドレスで、`T`を配置していないまま行う、0サイズの`T`のジェネリックな`T is Copy`読み取り | 規定なし | 未定義：読み替えには正のサイズが要る |
| 再配置（N15） | 契約が構築を定める`i64`値の外部コピーの後、両方のコピーを読む | 不明確（05:36と22:448が食い違う） | 有効：契約による構築 |
| | 契約がバイトコピーだけを定める同じコピーの後、両方を読む | コピー先は未定義（05:36） | 未定義：再配置するか否かのどちらでも、一方は値を保持しない |
| | `(i64, Token)`と、それとは別にそのToken構成要素を再配置する選択をしたバイトコピー | 未定義（05:36） | 未定義：構成要素は、それを含む値と一緒にだけ移る |
| | 値、その祖先、またはそのバイト上の読み替えPlaceへの`ref`が生きている間に、値を再配置で移して元へ戻すこと | 未定義（05:36） | 未定義：再配置と配置は条件4に従う |
| | 範囲の各バイトを配置するユーザーのループ | 有効 | 再配置ではない：`u8`値を配置し、コピー元の値は残る |

## 3. 設計

### 3.1. 表現と担当する節

- **§3.4。** 記憶領域、Place、権限、重なりを定める（N1、N2）。raw記憶領域のPlaceは配置からだけ生じ、その他の記憶領域は宣言済み
  のPlaceを保つ。読み替えPlace（N14）は、どちらの上にもその場限りで作るビューである。
- **§5.2。** raw固有の規則を定める：provenance、指す先の決定、配置、型の読み替え、再配置。
- **その他の担当。** Loan（§15.6）、完全性（§15.1.2）、破棄（§16）、data race（§22.3.7）は担当を変えず、raw記憶領域のPlaceに
  適用する。N13は契約による構築、読み替えPlace、再配置を完全性へ加え、N21は競合をPlaceの上で定める。

正サイズでは、同じバイト上の読み替えPlaceを除き、アドレスと型で指すPlaceが決まる。0サイズでは、指す先は同じアドレスのPlaceから
の選択になる。

### 3.2. 選択が実行を変えない理由（論証。形式的な証明はない）

- **正サイズ。** ある正サイズの型について、与えられたバイトを保持する、その型の配置済みまたは宣言済みの初期化済みPlaceは高々1つ
  である。型は自身をインラインに含められないからである。配置は、そこにあって値を保持しない、その型のどのPlaceを埋めてもよく、
  それらはすべて同じバイトを占める。そのバイト上の読み替えPlaceは、同じバイトを読み、借用する。
- **0サイズ。**
  - 候補のPlaceは型以外に何も格納しないので、どの操作もバイトに触れず、破棄は同じアドレスで型のコードを実行する。
  - 値全体のアクセスには完全性が要る。これで、選択によって実行するコードが変わりうる唯一の場合、つまり不完全値と完全値の
    取り違えがなくなる。
- **結果。** rawの読み取りや借用の結果は、Originをポインター型から、依存を保持依存の規則（05:59）から得る。

そこでN8は、選択が実行に含まれないことを規則として定める。

### 3.3. 採らなかった選択肢

| 選択肢 | 理由 |
| --- | --- |
| 正のstride、各rawポインター内の論理位置、`Raw.write`、アドレスごとの計数、Rustと同様のprovenance免除、生成元の一覧 | 理由とともにSETTLEDに記録する（N27） |
| 記憶領域を所有する型が宣言するプロトコルによる指す先の決定 | 有効性がプログラムではなく、述べたプロトコルに依存する（原則2） |
| `P@raw`を`P`とその部分に限ること | `P`のバイト上の読み替えPlaceを除いてしまう。0サイズの`x[1]`は`P`の記憶領域という条件で既に除かれる |
| 重なりを§15.6.2で定義すること | Placeは§3.4が定める。担当が2つあると1つの定義が分かれる |
| 完全性をrawの読み取り・置換・Takeだけに要求すること | raw借用は安全なコードへ値全体を渡す。不完全値の借用は15:41が禁じる |
| すべての型の読み替え | Non-Copy値と`Never`値を偽造できる。不変条件・Loan・provenanceを持つCopy値の偽造はRZ5に残る |
| 読み替え書き込みをCopy型のPlace上だけに限ること | `DictionaryStorage`の`Handle`への書き込みは、フィールドを宣言しない`Dictionary`値の上で読み替える。偽造はRZ5に残る |
| Copy値や、破棄してもCopyの構成要素を破棄するだけの値に解放前の義務を課すこと | こうした値の破棄には行う処理がない |
| 範囲に完全に収まるエントリーのレコードだけを再配置すること | エントリーのレコードは存在しない。`appendSlot`が`Links`を書き（DictionaryStorage.kimi:176-184）、その後ヘルパーがキーと値を別々に格納する（LlvmModuleWriter.DictionaryHelpers.cs:104-112）。そのため`slot + stride`の`()`が残ってしまう |
| 再配置の代わりに、`Dictionary`でエントリーごとに型付きのMoveをすること | 使用中のスロットを辿る必要があり、O2で拡張が遅くなる見込み（H） |
| Copy値を複製もするバイトコピー | 値を構築する3つ目の手段になる（原則1）。契約は既に構築を定めることができる |
| data raceに「重なる記憶域」を残すこと | 重なりの概念が2つになり、一方は0サイズで定義されない |
| 再配置を、Takeの後の`Raw.initialize`とみなすこと | Takeには完全性が要るので、不完全値を移せない |

## 4. 規範変更

各項目は対象、現行文（ある場合）、提案文を示す。どちらの「…」も変えない部分を表す。提案文のリンクは対象ファイルでの形で書く。
E1–E6は規則の例示であり、対象ファイルには入れない。

### 4.1. 記憶領域、Place、重なり

**N1** §3.4（03:243）。記憶領域の種類の列挙の末尾「…、オブジェクトペイロード、実体化した一時値」を次にする。

> …、オブジェクトペイロード、実体化した一時値、またはメモリー割当ての結果などの**raw記憶領域**。raw記憶領域は、そこに配置する
> 値のPlaceを宣言しない（[§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)）。raw記憶領域に配置した値はそこで自身の
> 型のPlaceを占め、その値の各構成要素はそのPlaceの部分であるPlaceを占める。

**N2** §3.4（03:253）。「Placeの重なりは[§15.6.2](…)に従う。」を次にする。

> 2つのPlaceは、同一であるとき、一方が他方のインライン部分であるとき、またはバイトを共有するときに**重なる**。同じアドレスの
> 異なる0サイズのPlaceは、一方が他方のインライン部分であるときだけ重なる。重ならないことの証明は
> [§15.6.2](15-ownership-and-lifetime-analysis.md#1562-place-overlap-and-conflicts)が定める。

**N3** §3.4（03:257）。「残る記憶領域を未初期化にする」を「そのPlaceを未初期化にする」にする。

**N4** §15.6.2（15:487）と§15.6.3（15:515）。

- 「2つのPlaceは、一方への操作が他方に影響しうるときに重なる。重ならないことは次の規則だけで証明する：」を「Placeの重なりは
  [§3.4](03-types-and-values.md#34-places-and-storage)が定める。重ならないことは次の規則だけで証明する：」にする。
- 「子を二度貸さないことがunsafeの義務である」を「重なるPlaceを生きた2つの排他的な子に貸さないことがunsafeの義務である」に
  する。

### 4.2. provenanceと指す先の決定

**N5** §5.2（05:34）。段落を2つにする。provenanceがすべてのアクセスと配置を制限するので、一つ先の逆参照の禁止は削る。

> **provenance**（由来の領域）は、ポインターの由来となる記憶領域と、そのポインターのアクセスが及びうるその範囲である
> （[§5.2.1](#521-access-conditions)の条件1）。Placeは、そのバイトが範囲内にあるとき、または0サイズならそのアドレスが範囲内か
> 範囲の終端にあるときに、範囲内にある。Placeは、provenanceの範囲内にあり、provenanceの記憶領域に属するときに、ポインターの
> provenance内にある。読み替えPlaceは、覆うバイトの記憶領域に属する。
>
> `*p`は型`T`の**raw Place**であり、`p[n]`と、raw Placeのフィールド・要素もraw Placeである。nullポインターは逆参照できない。
> 操作が作用するraw Placeは、Placeを1つ指す。そのPlaceはraw Placeのアドレスにあってポインターのprovenance内にあり、その型は、
> Originを等しいとみなす正規化型同一性（[§3.8](03-types-and-values.md#38-type-relations-and-operations)）でraw Placeの型と
> 一致する。フィールドや要素の射影の基底は何も指さない。1つの型の異なる0サイズのPlaceは同じアドレスを共有できる
> （[§4.1](04-arrays-indexing-and-slices.md#41-fixed-arrays)）。raw Placeはそのどれを指してもよく、どれを指すかはアドレス・
> 添字・フィールド名のいずれでも決まらない。

**N6** §5.2.1の条件1–3（05:42-44）。

- 条件1「**範囲。** アクセスする範囲がポインターのprovenance内にある：`P@raw`では`P`の記憶領域（`x[0]@raw`は`x[1]`へ届かない）、
  …」を「**provenance。** アクセスが作用するものがポインターのprovenance（[§5.2](#52-raw-places)）内にある：`P@raw`では`P`の
  記憶領域（`x[0]@raw`は、同じアドレスでも`x[1]`へ届かない）、…」にする。残りは変えない。
- 条件2「記憶領域が生存し、`T`のアラインメントを満たす」を「記憶領域が生存し、`T`が0サイズでも`T`のアラインメントを満たす」に
  する。
- 条件3の「そのPlaceに対して持つ権限（[§13.5.5](13-operators-and-assignment.md#1355-follow)のオブジェクトペイロード表、
  [§11.8](11-properties.md#118-static-storage)）」を「そのPlaceに対して持つ権限（…）、または読み替えPlace
  （[§5.2](#52-raw-places)）では、代わりにそのバイトのいずれかを保持する各Placeに対して持つ権限」にする。

**N7** provenanceとraw Placeの規則へのその他の参照。

- §3.4.1（03:275）：raw Placeの規則への参照を「raw Placeの規則（[§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)、
  [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)、
  [§5.2.2](05-raw-pointers-and-unsafe-memory.md#522-borrowing-raw-places)、
  [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places)）に従う」にする。
- §5.3（05:73）：「そのためジェネリックな`raw/T`はサイズ制約を要さない。」の後に「このとき、そうした要素の列の終端はポインターの
  等価比較では見つからないので、列は個数で辿る。」を加える。
- §5.3（05:75）：「ポインターのprovenance範囲（[§5.2.1](#521-access-conditions)の条件1）内またはその一つ先」を「ポインターの
  provenance（[§5.2](#52-raw-places)）の範囲内またはその一つ先」にする。
- §5.4（05:93）：「`U`としてのアクセスには引き続き[§5.2.1](#521-access-conditions)が要る」を「…[§5.2](#52-raw-places)と
  [§5.2.1](#521-access-conditions)が要る」にする。
- §5.5（05:110）：「範囲と権限を与える、実装が文書化したprovenanceの保証が要る」を「ポインターのprovenance
  （[§5.2](#52-raw-places)）、その権限、そのprovenance内に配置された値を与える、実装が文書化した保証が要る」にする。
- §15.1.5（15:97）：「raw Placeは[§5.2.1](…)の義務を保つ。」を「raw Placeは
  [§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)と
  [§5.2.1](05-raw-pointers-and-unsafe-memory.md#521-access-conditions)の義務を保つ。」にする。

**N8** §5.2。配置の段落（N9）の後に新しい段落を置く。

> **指す先の決定。** 操作の集合は、その要素のどれかにhappens-before関係で先行する操作をすべて含むときに**閉じている**
> （[§22.3.7](22-core-execution-and-foreign-functions.md#2237-data-races)）。閉じた集合は、各raw Placeが指すPlace、各配置が
> 埋めるPlace、各再配置が移す値の組み合わせを1つ選んで、安全か否かを問わずすべての要素が規則を満たし、選んだPlace上で要素どうしに
> data raceがないようにできるときに、**整合する**。操作Xについて、Xを含み、happens-before関係でXに後続する操作を含まない閉じた
> 集合が整合せず、その集合からXを除くと整合するとき、Xから先の動作は未定義である。選択は実行に含まれない。操作は、選択が何を
> 割り当てても、自身のアドレスで型の定めるとおりに作用する。選択が決めるのは、動作が定義されるかどうかだけである。

逐次コードでは閉じた集合は実行の先頭部分なので、どの選択でも成り立たない最初の操作から未定義になる。

**E1** 指す先の決定（D2–D4）：

```kimi
let p = Raw.allocate<Token>(2)              // 0バイトなので p + 1 == p。
unsafe
    Raw.initialize(p, Token.init())
    Raw.initialize(p + 1, Token.init())     // 有効：同じアドレスに2つ目のPlace。
    let both = (p[0]@uniq, p[1]@uniq)       // 有効：p[0]とp[1]は異なるPlaceを指す。
    _ = p[0]@move                           // "drop"を出力する。
    _ = p[0]@move                           // 有効：添字はPlaceを選ばない。"drop"を出力する。
    // _ = p[1]@move                        // 未定義動作：p上のTokenはどれも値を保持していない。
    Raw.release(p)                          // 有効：値は残っていない。
```

### 4.3. 配置、完全性、Take

**N9** §5.2（05:36）。段落を2つにする。

> 読み取りと書き込みは通常のCopyと代入の規則に従う。raw Placeの値全体の読み取り・置換・Take・借用には完全値が要る
> （[§15.1.2](15-ownership-and-lifetime-analysis.md#1512-construction-and-completeness)）。rawポインターはLoan、排他性、
> 完全性の証明を与えない。
>
> **配置。** rawポインター経由でPlaceを初期化するのは、`Raw.initialize`（[§5.6](#56-raw-storage-operations)）、再配置（後述）、
> および外部関数（[§22.6](22-core-execution-and-foreign-functions.md#226-foreign-function-imports)）など契約が構築を定める
> 操作だけである。Placeは、未初期化またはMovedのときに**値を保持しない**
> （[§15.1.1](15-ownership-and-lifetime-analysis.md#1511-initialization-state)）。Placeの初期化はそのPlaceへの書き込みで
> あり、初期化に使うポインターについて[§5.2.1](#521-access-conditions)の条件を満たす。そのPlaceは既存のPlace、またはraw
> 記憶領域では新しいPlaceでなければならない。値を保持していてはならず、祖先以外の初期化済みPlaceがそのバイトを1つも保持して
> いてはならない。`noinit`配列（[§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)）は初めから初期化済みである。

**N10** §5.2.1の局所変数（05:47）とその例（05:54）。

- 「記憶領域は生存したまま未初期化になり、後の初期化（…）が同じ記憶領域を埋める。」を「記憶領域は生存したままで、その内容の
  Placeは値を保持せず、後の初期化（…）が同じPlaceを埋める。」にする。
- 例のコメント「// 有効：同じ記憶領域を再び初期化した。」を「// 有効：同じPlaceを再び初期化した。」にする。

**N11** §5.2.3（05:67）。「`(*p)@move`は値をTakeし、記憶領域を未初期化にする。記憶領域は生存したままで、`Raw.initialize`で
再び埋められる。」を「`(*p)@move`は値をTakeし、raw Placeが指すPlaceは値を保持しなくなる。記憶領域は生存したままで、
`Raw.initialize`で再び値を配置できる。」にする。

**N12** §5.6の`initialize`（05:128）を次にする。

> - **`initialize`**は、[§5.2](#52-raw-places)の配置規則に従い、`storage`にある`T`のPlaceへ`value`をMoveする。

**N13** §15.1.2（15:38）と§15.1.5（15:86）。

- 構築完了を確定する操作の列挙「…タプル・配列の構築、`noinit`（[§4.3.4](04-arrays-indexing-and-slices.md#434-noinit)）」に、
  契約が定める構築を加え、次の文を続ける：「読み替えPlaceは完全値を保持し、再配置は値の構築完了を保つ
  （[§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)）。」
- 「状態の追跡も完全性の検査もせずにTakeを提供する（[§5.2.3](…)）」を「状態を追跡せずにTakeを提供する。完全性は作者の義務で
  ある（[§5.2](05-raw-pointers-and-unsafe-memory.md#52-raw-places)、
  [§5.2.3](05-raw-pointers-and-unsafe-memory.md#523-taking-from-raw-places)）」にする。

**E2** 配置：

```kimi
var value = Token.init()
let q = value@raw
let r = Raw.allocate<i64>(1)
unsafe
    // Raw.initialize(q, Token.init())      // 未定義動作：valueのPlaceが値を保持している。
    Raw.initialize(r, 1)
    // Raw.initialize(r, 2)                 // 未定義動作：初期化済みのPlaceがこのバイトを保持している。
    _ = (*r)@move                           // i64のPlaceは値を保持しなくなる。
    Raw.initialize(r@raw/u8, 7)             // 有効：どの値も保持しないバイト上の新しいu8のPlace。
    // Raw.initialize(r, 2)                 // 未定義動作：u8のPlaceがバイトの1つを保持している。
    Raw.release(r)                          // 有効：u8はCopy。
```

**E3** 構成要素のPlaceと完全性（D5）：

```kimi
let p = Raw.allocate<(i64, Token)>(1)
unsafe
    Raw.initialize(p, (7, Token.init()))    // タプルと2つの構成要素を配置する。
    _ = (*p).1@move                         // 有効：Tokenの構成要素だけを破棄する。"drop"を出力する。
    let n = (*p).0                          // 有効：残った構成要素のCopy読み取り。
    // let whole = (*p)@move                // 未定義動作：タプルは不完全。
    Raw.release(p)                          // 有効：Non-Copyの構成要素は残っていない。
```

### 4.4. 型の読み替えアクセス

**N14** §5.2。指す先の決定（N8）の後に新しい段落を置く。

> **型の読み替えアクセス。** 型がCopyで正サイズのraw Placeは、読み取り・書き込み・借用が作用するとき、または`Raw.slice`が要素と
> して扱うときに、代わりに**読み替えPlace**を指してよい。読み替えPlaceは、raw Placeのアドレスにありポインターのprovenance内にある
> その型のPlaceで、初期化済みのPlaceが保持するバイトの上にあり、そのどのバイトも値を保持しない部分には属さない。読み替えPlaceは、
> それに作用するアクセスか、それを借用するLoanの間だけ存在する。配置・再配置・解放はそのバイトと値を考慮しないが、そのLoanは
> [§5.2.1](#521-access-conditions)の条件4の対象になる。読み替えPlaceは完全値を保持する。その読み取りは、借用経由でも
> `Raw.slice`の要素としてでも、そのバイトが自身の型の有効な値を表すことを要する。パディング
> （[§21.1.1](../impl/21-layout-runtime-and-code-generation.md#2111-common-rules)）と、enumの選択中のケースが使わないバイトは
> 値を表さない。読み替えPlaceを通じた書き込みは、借用経由でも、変えたバイトを保持する各初期化済みPlaceが、次に読み替えPlace以外
> からアクセスされるときまでに、そのPlaceの型の有効な値を保持し、型がCopyでないenumのPlaceであれば選択中のケースも元のままで
> あるようにしなければならない。読み替えはペイロードを構築しない。

**E4** 型の読み替えアクセス：

```kimi
#Layout("C")
struct Header
    var tag: u8 = 1
    var size: u32 = 2

var word: u64 = 0x0102
var header = Header.init()
var flag = true
let bytes = word@raw@raw/u8                 // word@rawと同じアドレス・provenance。
let headerBytes = header@raw@raw/u8
let flagBytes = flag@raw@raw/u8
unsafe
    let low = bytes[0]                      // 有効：wordのバイト上のu8の読み替えPlace。
    let both = (bytes[0]@uniq, bytes[1]@uniq)  // 有効：読み替えPlaceはバイトを共有しない。
    headerBytes[1] = 0                      // 有効：パディングには書き込める。
    // let pad = headerBytes[1]             // 未定義動作：パディングのバイトは値を表さない。
    // flagBytes[0] = 2                     // flagを読むと未定義動作：2はboolの有効な値ではない。
```

### 4.5. 再配置、メモリー割当て、解放

**N15** §5.2。型の読み替えアクセス（N14）の後に新しい段落を置く。

> **再配置。** 外部関数など、コピー元範囲のバイトをraw記憶領域へコピーすると契約が定める操作は、完了時に、Placeがコピー元範囲内、
> かつコピー元ポインターのprovenance内にある値を再配置してよい。再配置した値の構成要素はその値と一緒に移り、単独では再配置
> されない。再配置した値は、構成要素とその状態ごと、コピー先の同じオフセットにあるPlaceを占め、コピー元のPlaceは値を保持しなく
> なる。各コピー先Placeは、再配置したコピー元のPlaceは値を保持しないものとみなしたうえで、配置規則を満たす。再配置した各コピー元
> Placeには、Takeと同じく[§5.2.1](#521-access-conditions)の条件3と4を適用する。

**N16** §5.6の`allocate`（05:126）。

- 「`count`個の要素のための未初期化の記憶領域を割り当てる。」を「`count`個の要素のためのraw記憶領域を割り当てる。その領域は
  値を保持しない。」にする。
- 「長さ0のprovenance」を「自身の長さ0のprovenance」にする。

**N17** §5.6の`release`（05:127）を次にする。

> - **`release`**は、null、0バイト結果、または未解放の`allocate`結果の先頭を受け取る。結果は別のrawポインター型に変換して
>   あってもよい。nullは無視し、0バイト結果では何も解放せず、0バイト結果は何度でも解放できる。記憶領域に配置したNon-Copy値は、
>   既に破棄・Take・再配置されていなければならない。値全体でなくてもよいのは、値全体を破棄しても構成要素を破棄するだけの場合
>   （[§16.3.2](16-scope-exit-and-destruction.md#1632-component-destruction)）で、そのときは残るNon-Copyの構成要素が同じ規則
>   で破棄・Take・再配置されていればよい。解放後、記憶領域内のポインターはすべて無効になる。

**E5** 再配置（D6を1組の値に縮めたもの）。コピー範囲の終端にある0サイズの値も一緒に移る。

```kimi
group Native
    /// `source`から`target`へ`count`バイトをコピーする。
    ///
    /// - safety: 両方の範囲がそれぞれのprovenance内にあり、重ならない。
    #LibraryImport("copier", "copy_bytes")
    public unsafe func copyBytes(target: raw/u8, source: raw/u8, count: u64) -> raw/u8

let source = Raw.allocate<i64>(1)
let target = Raw.allocate<i64>(1)
unsafe
    Raw.initialize(source, 7)
    Raw.initialize((source@raw/u8 + 8)@raw/Token, Token.init())  // 範囲の終端のToken。
    _ = Native.copyBytes(target@raw/u8, source@raw/u8, 8)         // 両方の値を再配置してよい（N15）。
    Raw.release(source)                                          // 有効：選択がTokenを再配置する（N8）。
    _ = (*((target@raw/u8 + 8)@raw/Token))@move                  // "drop"を出力する。
    Raw.release(target)
```

**E6** 解放：

```kimi
let tokens = Raw.allocate<Token>(1)         // 0バイト。countが制限するのはバイト数で、値の数ではない。
let numbers = Raw.allocate<i64>(1)
unsafe
    Raw.initialize(tokens, Token.init())
    Raw.initialize(tokens, Token.init())    // 有効：2つ目の0サイズのPlace。
    Raw.initialize(numbers, 5)
    Raw.release(numbers)                    // 有効：i64は破棄を要さない。
    _ = tokens[0]@move                      // "drop"を出力する。Tokenが1つ残る。
    // Raw.release(tokens)                  // 未定義動作：配置したTokenが残っている。
```

### 4.6. Loan、スライス、data race

**N18** §5.2.1の条件4（05:45）。「記憶領域の生きたLoanと競合しない。ただし」を「作用するものと重なるPlaceの生きたLoanと競合
しない。ただし」にする。

**N19** §5.2.2（05:63）。「別のrawポインターを含め、記憶領域にアクセスする」を「別のrawポインターを含め、借用したPlaceと重なる
Placeにアクセスする」にする。

**N20** §5.6の`slice`（05:129）。「`length`個の要素が`storage`のprovenance内に連続して並び、それぞれが`T`の初期化済みの値
（たとえば`Raw.initialize`で書いたもの）を保持するか、`noinit`配列に属する（…）」を「`length`未満の各`i`について、
`storage[i]`が`storage`のprovenance内の異なるPlaceを指し、そのPlaceが`T`の完全値（たとえば`Raw.initialize`で配置したもの）を
保持するか、`noinit`配列に属する（…）。結果の要素`i`はそのPlaceである」にする。

**N21** §22.3.7（22:320）。「2つのアクセスは、重なる記憶域に触れ、少なくとも一方が書き込みで、…」を次にする。

> 2つのアクセスは、重なるPlace（[§3.4](03-types-and-values.md#34-places-and-storage)）または1つのオブジェクトの参照数
> （[§13.6.2](13-operators-and-assignment.md#1362-object-creation-clone-and-lifecycle)）に触れ、少なくとも一方が書き込みで、
> 実装によるatomic access（`arc`の参照数の更新など）どうしではないときに**競合する**。Take、破棄、配置は書き込みとみなす。
> 再配置はコピー元のPlaceにも書き込み、解放は自身の記憶領域のすべてのPlaceに書き込む。

### 4.7. 付録E、IMPL、LIBRARY、ライブラリーのコメント

**N22** 付録E（E-terminology.md:32、:91と新しい行）。

- Conflicting access：「重なる記憶域への2つのアクセス。少なくとも一方が書き込みで、実装のatomic accessどうしではない。」を
  「重なるPlace、または1つのオブジェクトの参照数への2つのアクセス。少なくとも一方が書き込みで、実装のatomic accessどうしでは
  ない。」にする。
- Raw pointer / raw Place：「`*p`、`p[n]`、またはその部分。」を「`*p`、`p[n]`、またはその部分。アドレスにあるPlaceを指す。」
  にする。
- Overrideの前に新しい行：
  「| Overlap | 同一のPlace、Placeとそのインライン部分、またはバイトを共有するPlace。 |
  [§3.4](../03-types-and-values.md#34-places-and-storage) |」
- Raw pointerの前に新しい行：
  「| Punned Place | 初期化済みPlaceが保持するバイト上に、アクセスやLoanの間だけ存在する、Copyで正サイズの型のPlace。 |
  [§5.2](../05-raw-pointers-and-unsafe-memory.md#52-raw-places) |」
- Raw pointerの後に新しい行：
  「| Raw storage | メモリー割当ての結果など、配置する値のPlaceを宣言しない記憶領域。 |
  [§3.4](../03-types-and-values.md#34-places-and-storage) |」

**N23** IMPL §21.1.1（21:15）。「異なる0サイズのPlaceはアドレスを共有できる。」を「異なる0サイズのPlaceはアドレスを共有できる。
raw Placeが指すのはアドレスではなくPlaceである（[§5.2](../spec/05-raw-pointers-and-unsafe-memory.md#52-raw-places)）。」
にする。

**N24** LIBRARY §5.4のRaw表（LIBRARY.md:509-512）の保証の列：

| 関数 | 保証 |
| --- | --- |
| `allocate` | 安全。`count`個の要素のためのraw記憶領域を割り当て、その領域は値を保持しない。負の個数、`count * stride(T)`のオーバーフロー、メモリー割当ての失敗はAbortする。0バイトでは何も割り当てず、`T`のアラインメントを満たす非nullのアドレスを、自身のprovenanceとともに返す。要素のアラインメントが16を超える場合はコンパイル時にUnsupported。 |
| `release` | `allocate`結果をその先頭アドレスで解放し、値は破棄しない。nullは無視し、0バイト結果では何も解放せず、0バイト結果は何度でも解放できる。記憶領域に配置したNon-Copy値は、既に破棄・Take・再配置されていなければならない。値全体を破棄しても構成要素を破棄するだけの値では、値全体の代わりに、残るNon-Copyの構成要素が同じ規則で既に破棄・Take・再配置されていればよい（SPEC §5.6）。 |
| `initialize` | `storage`にある`T`のPlace（既存のもの、またはraw記憶領域では新しいもの）へ`value`をMoveし、何も破棄しない。書き込みはrawアクセス条件を満たす：`storage`は生存し、アラインメントを満たし、書き込み可能で、Placeはそのprovenance内にあり、生きたLoanと競合しない。Placeは値を保持せず、祖先以外の初期化済みPlaceはそのバイトを1つも保持していない（SPEC §5.2、§5.2.1）。 |
| `slice` | `storage`から`length`個の要素の共有Sliceを、要素を読まずに作る。`s`は呼び出し側の期待型が決める。正の長さでは、`storage`が非nullでアラインメントを満たし、`length * stride(T)`が`isize`に収まり、`length`未満の各`storage[i]`が、完全で有効な値を保持するか`noinit`配列に属する異なるPlaceを指し、`s`の間はどの要素にも書き込まれないことを要する。長さ0ではどの`storage`でもよい。 |

**N25** `src/Kimi/Library/RawOperations.kimi:2-13`。コメントだけを変える。STYLE §6.2の形で120桁以内。ソースに入れる英文の
ままとする。

```kimi
/// Allocates raw storage for `count` elements; it holds no value.
///
/// A zero-byte request allocates nothing and returns a nonnull address aligned for `T`, with provenance of its own.
///
/// - abort: `count` is negative, `count * stride(T)` overflows, or the allocation fails.
public func allocate<T>(count: isize) -> raw/T
/// Releases storage that `allocate` returned, at its start address, without destroying any value.
///
/// Null is ignored, and nothing is freed for a zero-byte result, which may be released again. Afterwards every
/// pointer into the storage is invalid.
///
/// - safety: Every Non-Copy value placed in the storage is already destroyed, taken or relocated, whole or, if
///   destroying it whole would only destroy its components, by its remaining Non-Copy components under the same rule
///   (SPEC 5.6).
public unsafe func release<T>(storage: raw/T)
/// Moves `value` into a Place of `T` at `storage`, an existing one or, in raw storage, a new one, destroying nothing.
///
/// - safety: The write meets the raw access conditions (SPEC 5.2.1): `storage` is live, aligned for `T` and writable,
///   the Place lies within its provenance, and no live Loan conflicts. The Place holds no value, and no Initialized
///   Place other than its ancestors holds any of its bytes (SPEC 5.2).
public unsafe func initialize<T>(storage: raw/T, value: T)
/// Forms a shared Slice of `length` elements starting at `storage` without reading them; the caller fixes the
/// result Origin `s`.
///
/// - safety: `length >= 0`. A positive `length` needs a nonnull, aligned `storage` with `length * stride(T)` fitting
///   in `isize`, each `storage[i]` below `length` designating a distinct Place that holds a complete, valid value or
///   belongs to a noinit array, and no element is written for `s` (SPEC 5.6). The programmer writes valid values
///   before reading noinit elements through this view.
public unsafe func slice<T>(storage: raw/T, length: isize) -> Slice<T> during s
```

**N26** その他のライブラリーのコメント（英文）。

- `StorageOperations.kimi:33-34`（`transferBytes`）：「…whatever their initialization state; no responsibility moves and no
  user code runs.」を「…whatever their initialization state; it may relocate the values in the source range (SPEC 5.2) and
  runs no user code.」にし、120桁以内に折り返す。
- `StorageOperations.kimi:36-37`（`placeEntry`）：「and transfers `key` and `value` into it.」を「and places `key` and
  `value` in it (SPEC 5.2).」にし、120桁以内に折り返す（現在121桁）。
- `src/Kimi/Library/README.md:107`：「transfers the acquired pair」を「places the acquired pair」にする。
- `Storage.kimi:202`：「// Logical positions, not pointer equality, prove disjointness even when E has zero size.」を
  「// Distinct positions let the raw Places designate distinct Places, also when E is zero-sized (SPEC 5.2).」にする。

### 4.8. SETTLEDとPLAN

DECISIONSの項目は要らない。DECISIONSはコンパイラーの決定だけを記録し、本提案は言語規則である。既存のSETTLED項目は再検討しない。
raw記憶領域をリークしても引き続き有効なので、「A no-leak guarantee」はそのまま残る。STATUSは変わらない：検証済みの境界は動かない。

**N27** SETTLEDの最後に新しい項目を加える（取り込むのは英語版の文面）。

````markdown
## stride、ポインター内の位置、`Raw.write`、計数、provenanceの免除、生成元の一覧による0サイズのraw値の区別

- **問題：** 異なる0サイズの値は同じアドレスを共有できる（§4.1）。そのため、アドレスごとに持つraw記憶領域の状態ではそれらを区別できず、初期化、Take、借用、`Raw.slice`、解放のいずれも、どの値に作用するかを定められない。
- **例：**

  ```kimi
  let p = Raw.allocate<()>(2)
  unsafe
      Raw.initialize(p, ())
      Raw.initialize(p + 1, ())   // p + 1 == p：アドレスごとの状態ではここに既に値がある。
  ```

- **修正案：** 0サイズの型に正のstrideを与える。各rawポインターに論理位置を持たせる。`Raw.initialize`を、破棄せずに上書きする`Raw.write`に置き換える。0サイズの値をアドレスごとに数える。Rustのように0バイトのアクセスをprovenanceから免除する。または「raw要素」を記憶領域の生成元の一覧で定める。
- **採らない理由：** 正のstrideは§4.1と0バイトのメモリー割当てのコストを変える。ポインター内の位置は、算術、キャスト、射影、外部関数の契約に状態を加える。`Raw.write`ではTake、借用、`Raw.slice`の対象が依然として決まらず、値を手放す2つ目の手段が増える（原則1）。計数は、数えた0サイズの値の組ごとに権限とLoanの規則を言い直すことになる。Rustが0バイトのアクセスを免除できるのは、その抽象機械が0バイトの範囲にメモリー状態を持たないからである。Kimigayoは0サイズのPlaceごとに初期化、Loan、破棄を保つので、免除するとポインターが届くPlaceについて、サイズに依存する2つ目の制限が要る。それがなければ、等しい0バイト結果が異なる所有者のPlaceを共有してしまう。生成元の一覧は、契約が提供する記憶領域と、再配置が移す値を取りこぼす。代わりにraw記憶領域を通常の記憶領域とする：配置した値はPlaceを占め、raw Placeはその1つを指す（§3.4、§5.2）。
````

**N28** PLAN §7。G87の後に1行加える（合計199行）。

> | RZ | [Raw storage as Storage](../../draft/Changes/2026-10-15%20Raw%20Storage%20as%20Storage.md)（SPEC §3.4、§5.2）が
> 残す8つの後続課題（その7節）：RZ1 raw Placeの借用権限、RZ2 `VirtualAlloc`のページを値として使うネイティブテスト、
> RZ3 §14.2.4に対する`_ = (*p)@move`、RZ4 `DictionaryStorage`の`Links`の配置、RZ5 型の読み替えアクセスによる偽造（H）、
> RZ6 記憶領域の境界契約、RZ7 宣言済みPlaceへ配置した値のOrigin、RZ8 Library READMEに残る古い`placeValue`。 |
> 仕様とライブラリー、予定なし |

## 5. 影響

- **削除する文。**
  - 一つ先の逆参照の禁止（05:34）。
  - §5.2、§5.2.2、§5.2.3、§5.6の記憶領域ごとの状態の文（D1–D4）。
  - §15.6.2独自の重なりの定義（15:487）と、§22.3.7・付録Eの「重なる記憶域」。§3.4の定義がこれらを置き換える。
- **コード。** コンパイラー、IR、ランタイム、APIのコードは変わらず、本変更はライブラリーのコードも加えない（RZ4で加える）。
  変わるのはN25とN26のコメントだけである。ネイティブのO2コード、メモリー割当て回数、コンパイル時間は変わらない。
- **分量。** 提案文の概算で、SPECとIMPLが約+1,000語、SETTLED、LIBRARY、PLAN、ライブラリーのコメントが約+550語。取り込み時に
  計測する。
- **取り込み。**
  - SPEC、IMPL、LIBRARY、SETTLED、PLANは文書なので、差分と参照を確認する。
  - `.kimi`のコメント変更はKimiライブラリーの行に当たる：選んだ検査と`KimiLibraryShapeTest`、`ArchitectureRulesTest`、その後に
    Session。
  - 取り込みを`draft/INTEGRATED.md`に記録し、本提案を取り込み済みとして閉じ、`draft/Changes`へ移す（RZ1–RZ8はPLANのRZへ
    移管）。PLANのリンクはこれを前提とする。
  - 置き換える規則を残している日本語版を更新する：SPECJ.md:249、489–498、524–527、1302、1323、1422–1424、1432、1776、2045、
    2405、2469と、付録EのOverlap、Punned Place、Raw storageの行。SPECJ2.mdはそれぞれ2行後。
- **原則。**
  - Placeの概念も配置規則も1つ（原則1）。
  - アクセスは、raw Placeが指すPlaceの状態、権限、Loanで判断する（原則2）。
  - 完全性、配置、読み替え、再配置はアドレスから推論せず、規則として述べる（原則3）。

## 6. 関係箇所

**検索：** `docs/spec`、`docs/impl`、`docs/*.md`、`docs/examples`、`src/Kimi/Library`を「raw storage」、`Raw\.`、
「the storage」、「storage holding」、「one-past」、「overlapping Storage」、「Uninitialized」、「zero-siz」、「zero-byte」、
「transferBytes」、「completeness check」、「lending a child」、「logical position」、「overlap」、「relocat」、「contiguous」と
§5.2.1へのリンクで検索した。変更箇所はN1–N28。

**確認して変更しない箇所：**

- **SPEC §4。**
  - §4.1、§4.3.4（04:91はrawポインター経由の読み取りを既に扱う）、§4.8.5。
  - §4.8.1（04:352）と§4.8.4（04:424）：N5とN20が要素の同一性を与える。UniqSliceはraw Place経由で要素に届く。
  - §4.9.7（04:575）：コレクションのRelocationはコレクションの契約に残る（RZ6）。Dictionaryの`transferBytes`は§5.2の再配置
    である（N26）。
- **SPEC §5。**
  - §5.6の`Buffer`の例。どの`T`でも有効になる。
  - 05:59：「raw storage」はN1の意味になる。義務の範囲は、メモリー割当ての結果を所有する型のままである。
- **SPEC §11から§22。**
  - §11.2.2：記憶領域ごとの文言はない。
  - §13.7.1（13:569）：部分的な後始末を伴う置換は安全なコードだけのもの。rawの値全体の置換には完全値が要る。
  - §14.2.4（RZ3）、16:205、§16.3.2：rawの規則を述べていない。
  - §22.6（22:448）：その構築は契約による配置である。
- **IMPL。** 21:14（パディング。N14が参照する）、§21.4.3（0バイトのprovenanceはN16がSPECで定める）。
- **その他の文書。** STATUS:279、GUIDE、STYLE §4.5は置き換える規則を述べていない。
- **ライブラリー。**
  - Library README :61（ポインターの等価比較ではなく位置を使うという記述は引き続き正しい）。
  - `Array.kimi`（:195：コピー元のLoanは引き続き重なるPlaceを除く）と`Dictionary.kimi`（:58-61：新しい値を配置する前に古い値を
    Takeする）。
  - `DictionaryStorage.kimi`（:71はRZ4に依存。:123-124は引き続き正しい）。
  - `Storage.kimi:3-4`：「initialization」は義務を控えめに述べるが、どの規則とも矛盾しない。
  - `Raw.kimi`（:1-2は既に120桁を超える）。

置き換えるコメントを検査するテストはない。

## 7. 未解決事項と未検証点

**後続課題**（PLANのRZ）：

- **RZ1** raw Placeの借用権限：§5.2.1の条件3は借用を挙げていない。
- **RZ2** ネイティブテストが`VirtualAlloc`のページを、配置も構築の契約もなしに値として使う：
  - `GenericPointerEmissionTest.ConcreteReadsPreserveCopyMoveAndCleanup`
  - `RawPlaceAccessTest.AnExplicitTakeMovesAndDestroys`
  - `ForeignEmissionTest`のポインター読み書きの各ケース

  値を`Raw.initialize`で配置するか、各importの`- safety:`契約に構築を明記する。
- **RZ3** `_ = (*p)@move`はその場で破棄する（§5.2.3）が、§14.2.4は移してから後始末する。
- **RZ4** `DictionaryStorage`は、`Links`の値を配置していない場所で`Links`のフィールドに書き込む。`appendSlot`
  （DictionaryStorage.kimi:176-184）が書き込み、:133、:172、:206-219とStorage.kimi:87、:132、:141、:150で読む。これは本提案
  以前からの問題である。
  - 各スロットには、最初に使うときに配置した`Links`の値が1つ要る。
  - `clearLinks`は古い`used`より下のスロットを埋めたままにするので、`free == 0`の分岐では新しいスロットを判別できない。これまでに
    使った最大のスロット位置（high-water mark）を持つか、`clearLinks`で`Links`の値をTakeまたは破棄する。
  - その後`compact`は再配置した`Links`に作用する。`Links`は`drop`を宣言せず、フィールドはすべてCopyなので、破棄してもCopyの
    構成要素を破棄するだけである。そのためN17の下では、解放の前に破棄する必要はない。
- **RZ5** 型の読み替えアクセスは値を偽造しうる（H）：
  - コンストラクターを持つCopy型（不変条件）
  - 参照とSlice（Loan）
  - rawポインター（provenance）
  - コンストラクターを持つ型への書き込み、およびArrayの長さのメタデータ（§4.7.3）など、その他のNon-Copy値のバイトへの書き込み

  制限すべきなのは、読み替えPlaceが完全値を保持するというN14の文と、その§15.1.2での対応（N13）である。
- **RZ6** 記憶領域の境界契約（`borrowStorage`、`borrowStorageUniq`、`ownStorage`、`tryAllocateBytes`。StorageOperations.kimi）
  が次を述べていない：
  - Dictionary.kimi:59-61とDictionaryStorage.kimi:35-37が使う書き込み権限
  - Array.kimi:179と:197が書き込む容量の範囲
  - Storage.kimi:43が解放するArrayのバッファーが`allocate`結果であること
  - `tryAllocateBytes`のprovenance
  - これらの契約が公開する`Array`と`Dictionary`の要素の記憶領域がraw記憶領域であること（SPEC §3.4）

  これは本提案以前からの問題である。
- **RZ7** 宣言済みPlaceへ配置した値のOrigin：Originだけを変換したポインター経由で配置した値が、宣言済みPlaceのOriginに合わなければ
  ならないとは、どの規則も述べていない（05:59はraw記憶領域だけを扱う）。
- **RZ8** 原因の範囲外：`src/Kimi/Library/README.md:108`が、もう存在しない`Storage.placeValue`をまだ挙げている。

**未検証点：**

- 3.2節の論証には形式的な証明がない。
- worker間での閉じた集合による指す先の決定とdata raceは試験していない。ユーザーのrawポインターではTransferableがRefutedになり、
  付録Dは並行実行でのrawポインターを先送りしている。ただし15:214が除外するコレクションのrawポインターはworkerに届く。たとえば
  `offloadEach`が渡す`Array<Token>`の`uniq`要素である。これらのポインターでも、指す先が選択で決まる場合に、N21の書き込みの規則に
  よって§22.3.7は定義されたままである。
- Dictionaryのレイアウトと下位変換はソースを読んだだけで、実行していない。
- windows-x64は§5.5の保証を文書化していないので、整数から作ったポインター経由のアクセスは、0サイズのものも含め、すべて未定義に
  なる。
