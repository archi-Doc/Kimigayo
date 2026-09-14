# Composition Root 仕様

2026-09-13 改訂。原案と[再レビュー](2026-09-13%20Composition%20Root%20Review%20Revision.md)の改善策を統合した。**本書の対象では、本書を SPEC.md と先行する Composition Root 文書より優先する。** 第I部は言語と成果物の規則、第II部は実装・性能の方針、第III部は今回導入しない拡張を定める。

変更しない型・所有権規則は [SPEC](../../SPEC.md)、依存の取得と配布は[依存・成果物仕様](../Design/2026-09-13%20Dependencies%20and%20Artifacts.md)、検証操作の詳細は[テスト仕様](../Design/2026-09-13%20Testing.md)、コード共有は[generic 共有仕様](../Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md)に従う。例は本仕様での記述であり、現在のコンパイラーが実行できることを示さない。実装状況は [STATUS](../../STATUS.md) に記録する。

## 第I部　言語・構成・成果物

### 1. 基本モデル

Composition Root は、プログラムが利用する環境依存の実装を、最終実行対象ごとに選ぶ仕組みである。$ で参照する。名前解決と実装選択はコンパイル時に行い、実行中は変更しない。

| 用語 | 意味 |
| --- | --- |
| root function | $abort など、コンパイラーが意味を定める置換不能な操作 |
| Entry | $Std など、固定された Contract を公開する接続点 |
| Composition Contract | Entry を通して使える操作と保証を定める通常の静的 Contract |
| Provider | Contract に適合し、実装を提供する具体型 |
| Binding | Std => MyStd のような Entry と Provider の対応 |
| 最終実行対象 | Application、またはコンパイラーが生成するテストhost |

$ と Entry は値・型・Property・保存域ではなく、マクロや実行時の DI container でもない。通常のローカル変数や alias によって隠されない。

**利用側は Contract で操作を確定し、最終実行対象はその操作を適合する Provider へ接続する。** Provider の選択で利用側の名前解決、overload、公開型、取得・寿命の規則をやり直さない。

~~~kimi
$Std.writeLine("Hello")
~~~

この呼び出しは、標準 Entry の Contract に従って引数を取得し、選択された標準出力 Provider を呼ぶ。

#### 1.1 root function

root function は言語が指定する Identity を持つ。ユーザーによる宣言、同名 overload の追加、Binding、関数値としての取得を認めない。通常の同名関数は組み込み性を得ず、未知の $name は診断する。

| 操作 | 主な意味 |
| --- | --- |
| $abort(message) | message を一度評価し、正常完了後に Abort。型は Never。Abort 開始後は通常の cleanup を行わない |
| $expect(condition, message: …) | 条件を一度評価し、偽なら失敗を記録して継続。テスト用の関数本体で使う |
| $require(condition, message: …) | 条件を一度評価し、偽なら失敗を記録して、その #Test 関数から通常 return する |

expect / require の message: は省略でき、失敗時だけ評価する。条件の cleanup、メッセージの評価・cleanup、継続または return の順を守る。使用位置、制御境界、診断・回収はテスト仕様に従う。通常の require condition else … 文とは区別する。

~~~kimi
group Checks
    #Test
    func sample()
        $expect(1 + 1 == 2)
        $require(true, message: "必要な条件")
~~~

SDK が文書生成用の宣言情報を持つ場合も、それはコンパイラー指定のカタログである。root function を group $ の実装可能な関数や Default Binding として定義しない。

### 2. 宣言・名前・アクセス

#### 2.1 Entry の宣言

SourceDocument のルートに group $ を置く。通常の group の全機能を継承せず、Entry と、それらを選択する既存の #if / #switch だけを含む。関数本体、通常の Field、Property、初期化式、入れ子の Container は置けない。空の宣言リストは許す。

~~~kimi
public contract ClockComposition
    func nowTicks() -> i64

group $
    public Clock: ClockComposition
~~~

Clock: ClockComposition は専用の Entry 宣言である。ClockComposition 型の値を保存する宣言ではない。Contract 名は宣言した文書の通常の型・Contract 名前環境で解決する。

Entry は宣言元 Kotonoha のルートに所属する。access は通常のルート宣言と同じで、既定の private と internal は同じ Kotonoha 内、public は他の Kotonoha からも参照できる。公開範囲全体で Contract とその API のアクセス条件を満たすこと。group $ 自体には access・generic・Origin 指定を付けない。

同じ Kotonoha の複数の group $ は異なる Entry を追加できる。同じ Entry の再宣言は、同一 Contract を書いていてもエラー。生成された宣言も同じ規則に従う。別 Kotonoha の宣言は merge しない。

#### 2.2 参照と Identity

Entry の参照形を次のように定める。ReferenceName はプロジェクトに設定した直接依存の参照名であり、ソース内の alias ではない。

| 表記 | 対象 |
| --- | --- |
| $Clock | 現在の Kotonoha の Entry |
| $Time.Clock | 直接依存 Time が公開する Entry |
| $Std | コンパイラーが指定する標準 Entry |
| $Time.Clock.nowTicks() | その Entry の Contract が公開する操作 |

Root 用の名前領域には、現在の Entry、直接依存の ReferenceName、コンパイラー指定の短い名前を登録する。同一 Entry への参照は統合し、同じ先頭名が異なる Identity や Entry/モジュールの役割に衝突する入力は診断する。標準の短い名前と root function 名をユーザーの Entry や ReferenceName に割り当てない。通常の値・型の名前領域とは独立している。

Entry は依存・成果物仕様のモジュール・宣言の識別規則を使い、参照名の文字列では識別しない。宣言を対応付けるキーと、その内容の世代・検証条件を区別する。同じ依存を別名で参照した場合は同じ Entry。別版・別宣言の同名 Entry は別物であり、暗黙に統合しない。Entry と Contract の宣言元は異なっていてもよい。

推移依存の Entry は必要な意味情報として読み込めるが、ソースから名前で参照するには所有元への直接依存が必要である。$A.B.Clock によって A の直接依存 B を辿る機能や、Entry の re-export は導入しない。コンパイラーが要求を追跡するときは名前の可視性を拡大しない。

Entry を利用する場所で Entry/Contract のアクセスを、Binding の場所で Provider 型のアクセスを検査する。Provider の適合は通常の有効アクセス範囲を満たすこと。接続のために Library の名前環境へ Application の名前や private member を追加しない。

### 3. Contract と操作

#### 3.1 Composition に使える Contract

**Provider を知らなくても、公開操作の型・呼び出し条件・取得・寿命・保証を確定できなければならない。** 継承・alias・関連型を正規化した結果にこの規則を適用する。

公開操作は receiver のない型関数とする。通常の関数 generic、Origins、Constraints は上記の条件を満たせば使える。関連型は Contract 側の宣言と制約から Provider 非依存に固定できる場合に限る。固定した関連型は操作の型の正規化に使い、Entry 自体から型を取り出す構文は追加しない。instance function と Property 要件は対象外とする。

~~~kimi
contract UnfixedFactory
    func create() -> Self

group $
    Factory: UnfixedFactory // エラー: 結果型が Provider によって変わる。
~~~

Entry を介さない通常の Contract としての UnfixedFactory まで禁止する規則ではない。実装ごとに異なる値を隠して返す機能も今回追加しない。状態を受け渡す API には、共有する公開型と通常の値・引数を使う。

#### 3.2 Provider の適合

Provider は、通常の型関数の修飾子にできる、完全に束縛済みの具体型であり、通常の静的適合を宣言する。閉じた generic 型も、その通常の型形成・Constraints・適合を満たせば指定できる。型の指定によってインスタンスを生成しない。group、Entry、実行時の値、factory 呼び出しは Binding の右辺に置けない。

~~~kimi
public struct FixedClock
    Self is ClockComposition
    public func nowTicks() -> i64 => 42

compose $
    Clock => FixedClock

let observed = $Clock.nowTicks()
~~~

この例は §2.1 と同じ Application に置く。ClockComposition は説明用の独自 Contract であり、標準の時計 API を定めるものではない。

適合は通常の Contract 検証で行う。名前・関数種別・generic slot・パラメーター順序・外部ラベル・正規化した型で実装を識別し、その後、結果型、Origins、Constraints、safe/unsafe、アクセスと効果の互換性を確認する。入力条件を強めたり、結果の寿命保証を弱めたりしてはならない。未検証の循環した適合を証拠にしない。

Provider の追加 member、overload、default 引数は Entry の候補集合に入れない。Contract の関数要件は通常どおり default/optional 引数を持たない。共通祖先の同じ Requirement Identity は重複排除するが、異なる要件を今回の Provider が同じ関数へ接続するという理由で一候補にまとめない。検証した要件と実装の対応を witness として保持する。

選択した Provider は Contract 全体を満たす。利用した操作だけを実装する部分適合は認めない。動作上の法則も各 Contract に記述するが、型検査だけで出力先・時刻の進行・等価性などを証明したことにはしない。

#### 3.3 呼び出し・関数参照・保証

Entry 経由の呼び出しは、Contract の候補だけで通常の overload と引数取得を確定する。Provider の選択後に再探索せず、検証済みの要件→実装対応を使う。接続時に追加の暗黙変換、Borrow、Move、default 補完を導入しない。

Operation の関数参照は通常の Function Item 規則に従う。論理 Identity は Entry、Operation、束縛済み generic/Origin 引数で定め、Provider の関数アドレスでは決めない。通常の unsafe・呼び出し可能性・Origins・共通 Function Type への変換条件を満たすこと。

~~~kimi
let write: (string) -> () = $Std.writeLine
write("Hello")
let service = $Std // エラー: Entry 自体は値ではない。
~~~

Library の公開保証は Contract の保証から完成させる。任意の効果保証がなければ通常の保守的な検査を行う。必須情報の不足、Unknown、未実装、Error を、効果なしや完成済みの NotProven へ読み替えない。具体的な利用時の Loan・初期化・cleanup 検査も省略しない。

### 4. 構成の選択

#### 4.1 compose $

最終実行対象のルートソースに compose $ を置く。compose はその宣言位置で $ が続く場合だけ contextual keyword とし、普通の名前としての使用を妨げない。本体 は Binding と既存の compile-time directive だけを含む。空のリストと複数文書への分散を許す。

~~~kimi
// Time は Clock Entry を公開する直接依存。
public struct TestClock
    Self is Time.ClockComposition
    public func nowTicks() -> i64 => 100

compose $
    Time.Clock => TestClock

$Std.writeLine("Ready")
~~~

左辺は §2.2 の Entry 参照から先頭の $ を除いた形。右辺はその文書の通常の型名解決で得る Provider 型である。別文書の alias を流用しない。

group $ と compose $ は宣言であり、トップレベル実行項目や startup 候補に数えない。関数内、通常の Container 内には置けず、Entry-to-Entry alias、操作ごとの部分 override、優先度、Default へ戻る super 構文は持たない。

#### 4.2 一意な選択と Default

directive 選択と生成後、最終実行対象に属する全 Binding を Entry Identity ごとに集計する。

| 明示 Binding | 選択結果 |
| --- | --- |
| 複数 | 同じ Provider の重複でもエラー |
| 一件 | その Provider。無効ならエラーにし、Default へ戻さない |
| なし | 指定済みの SDK Default。必要な Entry に Default もなければエラー |

SDK Default は対応する compiler/SDK/profile/target の入力として一意に指定する。通常の適合検証を省略せず、出所を記録する。インストール済みパッケージの探索順、ソース・依存の列挙順、weak symbol の勝敗で選ばない。今回、ユーザーや Library による Default の自己登録は導入しない。

~~~kimi
compose $
    Std => FirstStd
    Std => SecondStd // エラー: 選択済み Binding が二件。
~~~

設定で実装を選ぶ場合も同じ規則を使う。以下の useFixedClock はプロジェクトが宣言した通常の bool 設定、SystemClock は別途定義した適合型とする。

~~~kimi
#switch
    #case useFixedClock
        compose $
            Clock => FixedClock
    #case _
        compose $
            Clock => SystemClock
~~~

#### 4.3 必要な Entry と検証範囲

Contract 宣言と明示 Binding は未使用でも検証する。Entry 宣言だけでは実装を要求しない。最終対象の選択済み製品モジュールを検査単位とし、その検査対象本体が持つ Entry の 呼び出し・関数参照を要求として集計する。テスト対象では §5.4 の入力を加える。

Library が外へ持ち出す製品の構成要求も公開契約として扱う。要求する Entry は最終所有者が指定できる public な Entry、またはコンパイラー指定の標準 Entry でなければならない。private な関数の内部で使う場合も同じであり、外部から指定不能な要求は Library の検証時に診断する。宣言しただけの非公開 Entry と、同じ root のテストだけが使う Entry は製品要求にならない。

Provider の実装、initializer、destructor、必要な generic 具体化・生成操作・cleanup の要求も含め、追加要求がなくなるまで閉じる。未呼び出し本体や実行時の定数偽分岐を理由に意味検査を省かない。compile-time directive で除外された内容は要求を増やさない。DCE は検証後のコードだけを削除できる。

要求された各 Entry と明示 Binding を検証する一方、機械語を出さない generic 本体の正当な表現義務は既存規則で保持する。無限の型増殖や必須生成の資源不足を、構成の fallback で隠さない。

### 5. 実行時の意味と標準環境

#### 5.1 状態と寿命

構成は値を生成・複製・所有しない。Provider が使う引数、戻り値、静的保存域は通常の型・取得・Loan・初期化・破棄規則に従う。Provider を指定しただけで constructor や initializer を実行しない。

静的保存域は実際の初回アクセスで初期化し、正常初期化の逆順と寿命依存に従って破棄する。Binding の順やモジュールのロード順を初期化順にしない。相互再帰は通常の関数呼び出しとして許し、静的初期化の循環や shutdown 中の不正アクセスは既存の Abort 規則で扱う。

選択された Provider が同じ Entry を呼ぶと、自分へ戻る。例えば MyStd.writeLine が $Std.writeLine を呼んでも Default への委譲にはならない。通常の再帰として扱い、診断・構成レポートでその経路を追えるようにする。

別 Entry が同じ Provider を使っても、同じモジュールの static を複製しない。別 Provider が同じ helper/static を共有する場合もある。Entry の違いから状態の独立性や非aliasを推定しない。複数の独立した logger、接続、短命な arena は、必要数の値を生成して明示的に渡す。

#### 5.2 $Std と出力経路

コンパイラーと互換な Core は、次の公開 Contract と標準 Entry を指定する。これらの宣言を別名・同名のユーザー宣言で置換できない。Provider のみ §4 に従って選択する。

~~~kimi
// Core が所有する宣言。
public contract StdComposition
    func writeLine(text: string) -> ()

group $
    public Std: StdComposition
~~~

$Std.writeLine(text: string) は、選択した Provider の出力先へ一行を渡す API とする。string は通常の所有する引数として取得し、既存 local を渡すと Move する。借用版や回復可能な I/O エラー API をこの接続から推定しない。

対応する SDK/profile は Std の Default Provider を供給する。Default は UTF-8 の内容と LF を OS の stdout へ出し、検出した出力失敗は Abort とする。部分出力や診断の不達など、既存の低水準出力の限界は維持する。他の Provider は出力先や保存・破棄方針を定められ、その挙動を文書化する。

~~~kimi
public struct DiscardStd
    Self is ::Core.StdComposition
    public func writeLine(text: string) -> () => ()

compose $
    Std => DiscardStd

$Std.writeLine("この行は破棄される")
~~~

既存の Core.writeLine(text: string) は同じ Entry へ転送する公開互換入口とし、独立した低水準出力の特別扱いを廃止する。新コードの標準入口は $Std.writeLine とする。Default Provider はその公開入口へ戻らず、compiler/SDK 内部の低水準操作へ接続する。

~~~text
$Std.writeLine ──────────┐
Core.writeLine の転送 ───┴→ 選択 Provider → 必要な低水準出力
~~~

StdComposition は標準出力の交換単位とする。今後の時計・乱数・ファイル等は独立して交換する Entry に分ける。同じ資源を管理する操作群は一つの Contract にまとめ、操作ごとの override を追加しない。

#### 5.3 言語の基盤

Core の型 Identity、Copy/Owned、配置・所有権、Abort の保証を Binding で変更しない。依存のパッケージ解決、低水準 native 接続、実行時の値の受け渡しも、それぞれの規則に従う。Composition は環境実装を集約する仕組みであり、全ての依存を暗黙の global service にする指定ではない。

Abort と検証結果の通信は、差し替え可能な Std や allocator の成功に依存させない。必要な最小の診断・終了経路を保持し、診断の失敗で終了保証を失わない。allocation とログが互いに必要になる再入経路も避ける。

$ の使用だけでは純粋性、権限分離、sandbox を保証しない。通常の呼び出しや FFI があることを踏まえ、依存の表示と安全性の証明を区別する。

#### 5.4 Library とテストhost

Library の製品ソースは Entry を宣言・利用できるが、compose $ を持てない。依存 Library の Default 選択や構成を最終対象へ持ち込まない。Library の配布・check は意味検証を行い、最終 Provider の不在だけでは失敗にしない。

kimi test は root プロジェクトのテストhostを最終実行対象とする。Application の通常ソースにある構成を引き継ぎ、root の TestSources にある compose $ を同じ host の明示 Binding に加える。Library 単体テストの構成はその TestSources に置く。依存先のテスト入力は取り込まない。

~~~kimi
// Library の TestSources に指定したファイル。
struct TestClock
    Self is ClockComposition
    public func nowTicks() -> i64 => 42

compose $
    Clock => TestClock

group ClockTests
    #Test
    func fixedValue()
        $expect($Clock.nowTicks() == 42)
~~~

製品 Binding とテスト Binding に同じ Entry があれば、通常どおり重複エラー。テスト用の後勝ち規則は設けない。Entry と Root の名前解決にも製品/テストの所属を適用する。通常ソースの Binding は製品の名前環境、テスト専用の Binding は製品とテストの名前環境で検証する。テスト宣言・参照名は製品の Entry 選択、型、overload、適合を変更しない。

異なる接続を選ぶには、通常のプロジェクト入力や §4.2 の設定・directive で一つの構成を選び、別成果物を作る。製品の設定を変える場合はその入力を再検証する。通常ソースから test-only Provider を参照してはならない。

一つのテスト成果物は全ケースで同じ構成を使う。フィルターは実行ケースだけを選び、Provider を変えず、ケースごとの再コンパイル・再リンク・実行中の差し替えを行わない。状態の隔離はテスト仕様のプロセス単位で行う。

### 6. 解析・接続・成果物

#### 6.1 検証の境界

~~~text
入力・SDK・target・設定を固定
  → directive 選択・宣言収集・Mods の生成と provisional Binding
  → Entry と Contract の型・公開契約を確定
  → 利用側を Contract で検証し、Operation と取得計画を保持
      ├→ Library: 接続待ちの参照と検証済み意味を保存
      └→ 最終実行対象: Binding/Default を選択
          → Provider の適合と要求の閉包を検証
          → 最終構成を固定し、ABI・生成計画を確定
          → lowering・コード生成
~~~

図は意味上の境界であり、全 pass を直列に固定する指定ではない。候補の名前解決や header 検証は前倒しできる。生成が終わる前の結果を最終構成と扱わず、生成ソースもその所属の権限・重複・アクセス規則に従う。最終構成を読んで同じ構成を書き換える生成ループは導入しない。

接続待ちと未完了の型検査を区別する。最終接続は未検証の利用側を救済せず、通常の具体使用時義務と ABI 適合をすべて満たしてから executable を公開する。

#### 6.2 モジュール依存と最終接続

宣言を得るモジュール依存は、依存・成果物仕様の DAG を維持する。Library が Entry を通して Application の Provider を呼ぶ接続は、Library の通常依存へ追加しない。最終呼び出しグラフは通常の有限な相互再帰を許す。

| 識別 | 内容 |
| --- | --- |
| ModuleInputId | モジュールのソース・設定・宣言された依存など、Provider 非依存の解析入力 |
| CompositionId | 確定した Entry/Contract/Provider の対応、SDK/profile の選択と内容依存 |
| ArtifactId | モジュール入力集合、Composition、ABI・生成設定と実際の成果物の結び付き |

既存のモジュール・宣言 ID を使い、参照名、取得パス、pointer、ロード順から別の同一性を作らない。同じモジュールの Symbol・型・static は一つの最終対象内で共有する。

Library の ModuleInputId に Application の最終構成を逆流させない。構成に依存する接続証明・生成計画には、その依存を別に記録する。App 自身のソースに書いた Binding を編集した場合は、通常どおり App のソース入力が変わる。

#### 6.3 保存と再利用

初期配布は既定のソースパッケージを使う。利用側の環境で独立したモジュールとして検証し、最終構成を決めてから生成する。Default を inline 済みの機械語を、別構成へそのまま再利用しない。Koto のメモリ上の object graph を portable な配布形式にしない。

| 保存する計画 | 必要な情報 |
| --- | --- |
| Entry・Contract | 宣言元・内容 Identity、アクセス、Requirement、完全な型・generic/Origin schema・Constraints・公開保証 |
| 利用側 | 確定済み Operation、呼び出し/関数参照、引数対応と取得、Loan・cleanup、生成/破棄を含む要求、定義元の source context |
| 最終接続 | Provider の具体型・内容、検証済み witness・substitution・必要な適応、要求閉包、Default の出所 |
| 最終生成 | target/profile、ABI・layout、実装・context、実際の native 入力、IR/link 成果物との対応 |

Library の意味計画は Contract と検証に用いた入力が一致する場合に共有する。Provider の変更で影響する接続証明、効果に依存した判断、インライン化済み本体、context、生成物は失効させる。情報不足なら保守的に再検証・再生成する。

候補が存在しないこと、宣言集合、閉じた specialization 集合に基づく判断も依存として記録する。Contract の変更で影響する利用側と Provider を再検証し、同名・同サイズ・同じ版番号だけで互換としない。

最終構成を安定した構造順で正規化して記録する。キャッシュ有無、別名、依存列挙順、並列処理の完了順で接続結果を変えない。manifest へ必須情報を追加する際は schema を改訂し、部分的な接続表や古い証明を混在させない。

#### 6.4 生成と診断

通常の確定呼び出しは実装へ直接接続できる。ABI 適応が必要なら検証済み adapter を使い、引数評価・取得・結果・cleanup と失敗位置を保つ。利用側の呼び出し位置と Provider 内の失敗位置を混同しない。

generic の共有本体で操作の entry/context pair を渡す場合も、既存の共有規則を使う。全呼び出しの direct 化を保証するものではないが、Composition のための実行時の名前検索・reflection・container を要求しない。

診断は、失敗した Entry/Contract/Provider、宣言・Binding の位置、要求元の経路を示す。重複、欠落、不適合、不可視、未対応、未解決の証明、資源不足を区別する。未知の構文・成果物を黙って省略しない。依存経路の表示量にも有限の上限を設ける。

構成レポートには、確定した対応、明示/Default の出所、各 Library・Provider が要求する Entry を示す。これにより間接的に増えた環境依存も追える。ソースの private な名前を新たな参照候補にする機能ではない。

## 第II部　実装と性能

### 7. 実装方針

#### 7.1 変更する段階

| 段階 | 変更 |
| --- | --- |
| Parser / Koto | root function、Entry 参照、Entry 宣言、compose を区別する。現在の unary/MacroKoto による通常 operand lookup を使わない |
| Binding | Entry registry、宣言元とアクセス、Contract の適格性、Binding の一意性、Provider 非依存の操作選択を追加する |
| 適合・呼び出し計画 | 既存の静的 Contract witness と BoundCall の型・取得・Origin 情報を再利用し、論理 Operation と実装接続を保持する |
| 制御フロー・所有権 | root function の Abort/継続/return と lazy message を専用に扱う。Entry の呼び出しは通常の検証済み取得計画へ流す |
| lowering / Core | Provider 型関数を収集・生成し、ABI 対応から callee を選ぶ。Core.writeLine の直接 runtime 特別扱いを共通 Entry への転送に置き換える |
| 外部入力・成果物 | 複数 Kotonoha の Entry/Contract/要求を読み込み、構成を最終対象へ保持する。意味計画と生成物の失効を区別する |
| startup / test / Mods | 宣言を実行項目と数えず、所属と生成境界を維持する。テストhostにも同じ Binding 規則を適用する |

現在の実装には [Parser](../../Kimi/Compiler/Parsing/Parser.cs)、[Contract 検証](../../Kimi/Compiler/Binding/Binding.ContractMatching.cs)、[呼び出し計画](../../Kimi/Compiler/Binding/Binding.Calls.cs)、[lowering](../../Kimi/Compiler/Emission/BodyLowering.Calls.cs)を土台として使える部分がある。構文対応だけで explicit Abort、テスト操作、外部 Library の生成まで完了したとは扱わない。

実装は、単一 Compilation の Entry/適合/接続と Provider 型関数 → root function の不足段階 → 複数モジュール → 一般の generic・関数参照 → 精密な再利用の順に進められる。未対応段階は明示診断する。初期は source 依存とともに一つの最終 LLVM module を生成でき、安定した別 object ABI や永続機械語 cache を前提にしない。

#### 7.2 allocation・探索・共有の削減

| 方針 | 条件 |
| --- | --- |
| Entry/Operation を一度 intern し、呼び出し箇所は小さい ID/参照を持つ | 永続 Identity と compilation 内の配列 index を区別する |
| 接続表を compilation ごとに一つ固定する |呼び出しごとの Dictionary・hidden receiver・witness 配列を生成しない |
| Provider 具体型・Contract・検証環境が同じ適合証明を共有する | generic/Origin 引数、条件、内容依存を含める。別 Entry の論理 Identity は保つ |
| 選択された組だけを照合し、要求を worklist で辿る | 全 Provider の総当たりと全表の反復走査を避ける |
| 意味計画と接続・生成計画を分離する | 実際に依存する内容を失効対象にし、不明な段階は広く再検証する |
| 逆依存から変更を伝播し、既存の配列/list 容量を再利用する | 不在依存、initializer/destructor、生成依存を落とさない |
| 直接化・不要な転送の除去をコード複製より先に行う | ABI、取得、cleanup、source context が適合する場合に限る |
| 既存の generic 本体・entry/context 共有を使う | 全 CompositionId を無条件に本体の共有キーへ入れず、読む接続・実装・schema・定数に従う |

再帰する参照は node を先に登録してから辺を確定し、参照先の hash を無限展開しない。hash の一致だけで同一とせず、内容・構造も検証する。不要な code/context/helper の生成を省けるが、必須の意味検証は省けない。

Provider が固定されても、呼び出し結果・副作用・保存域の独立性は固定されない。

~~~kimi
let first = $Clock.nowTicks()
let second = $Clock.nowTicks()
// 同じ Provider という理由だけで二回目を first に置き換えない。
~~~

同様に、Entry 名の違いだけで noalias、safe という理由だけで readonly を付けない。通常の証明と最適化規則に従う。Composition 自体の heap allocation や名前検索は不要だが、Provider 自身の処理・静的初期化チェック・必要な adapter まで無コストとは保証しない。

### 8. 検証項目

| 観点 | 確認する振る舞い |
| --- | --- |
| 宣言・名前 | sealed 操作の再定義拒否、通常名による非shadow、別名の同一 Identity、別版の分離、Entry/ReferenceName 衝突、宣言位置と startup |
| Contract | Self/関連型の非依存性、継承要件、overload/default の非漏出、ラベル・Origins・unsafe・access・取得の不適合、外へ指定不能な Library 要求 |
| 選択 | 明示優先、重複、無効指定の非fallback、Default 不在、未使用 Binding、directive/生成/文書順によらない結果 |
| Library | Provider 未選択での検証、App A/B の別接続、DAG と接続の分離、生成・cleanup を含む要求閉包 |
| 状態・基盤 | 選択だけでは初期化しないこと、共有 static、相互再帰と初期化循環、出力の一系統化、Abort 経路の独立 |
| テスト | 製品/テスト所属、Binding 重複、lazy message、require return/cleanup、全ケースでの構成固定 |
| 再利用・生成 | 古い witness/インライン化済み本体/context の失効、Contract が同じ場合の解析再利用、キャッシュ・並列順・別ディレクトリでの意味一致 |

性能は、通常の直接呼び出しとの比較、同一適合を使う多数の呼び出し、構成 A/B、単一 Entry の変更、多数 Entry・深い要求連鎖・有限再帰、短いテスト多数件で測る。適合検証回数、allocation、最大メモリ、コンパイル時間、コード/context bytes、実行時間を記録し、結果・効果・cleanup の一致を先に確認する。測定なしの速度保証は置かない。NativeAOT テストは明示指定時だけ行う。

## 第III部　今回の対象外

### 9. 将来の拡張

| 拡張 | 導入前に定めること |
| --- | --- |
| instance/group Provider、Entry ごとの自動生成状態 | 新しい適合・構築・receiver・借用・破棄の規則が必要。通常の値で表せる場合は追加しない |
| 実装依存の Self/関連型を隠す API | 抽象型、配置、型同一性、所有権、別コンパイルの契約 |
| ユーザー Default、部分 override、decorator、Entry alias/re-export | 所有者・選択・循環・公開範囲を共通規則で定義する |
| allocator/object 実行時の差し替え | allocate/free/resize の資源対応、alignment、失敗・再入、FFI と解放、固定された言語保証との境界 |
| 動的ロード、実行中の差し替え、同一プロセス内の複数構成 | 構成の境界、値の移動、allocator、ABI、保持する関数参照の意味 |
| バイナリ配布、別 object 接続、永続機械語 cache | 意味情報とコードの対応、操作 ABI、再接続・失効・検証条件 |
| Entry 利用の許可リスト | 依存表示と安全性・権限制御の保証範囲を区別する |

実装の段階制限と、この表の未導入機能を混同しない。本仕様で定めた機能の未対応は診断し、将来の構文や実行時機構を暗黙に生成しない。
