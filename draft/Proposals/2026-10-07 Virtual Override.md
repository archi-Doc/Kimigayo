# virtual / override：仕様変更案・実装計画

日付：2026-10-07。状態：合意内容を反映した提案。正式仕様への取り込み・実装・実行検証は未実施。

## 1. 位置付けと設計方針

本書で追加・変更する事項は、[SPEC.md](../../docs/SPEC.md)およびその参照先より優先する。変更しない事項には既存仕様を適用し、他の提案は暗黙に採用しない。本書作成では、正式仕様、実装、既存計画、状態一覧、取り込み記録を変更しない。正式仕様への取り込みは別作業とする。

設計の中心は、**一つのスロットに一つの公開契約を持たせ、override は契約を変えず実装だけを提供する**ことである。

- `virtual` は新しいスロットと初期実装を定義する。`override` は既存スロットの実装を更新する。
- 名前解決・overload・アクセス・呼び出し契約は静的に決定する。動的型は、そのスロットの実装だけを選ぶ。
- 所有権・Origin・Loan・取得・副作用の検査は既存規則を共用する。暗黙の boxing、upcast、所有権移転は追加しない。
- スロット、実装、検証結果を別々の意味情報として保持し、診断・LSP・CSP・コード生成で共有する。

これは、契約の単一化による「One Concept, One Canonical Form」、公開保証による「Local Reasoning」、明示的な差し替えと基底呼び出しによる「Explicit Semantics」、検証可能な意味情報による「Compiler Server Protocol」を満たす。

## 2. 対象と宣言

### 2.1. 初版の範囲

| 項目 | 規則 |
| --- | --- |
| 宣言位置 | struct の直接のメンバー。既存の分割宣言・生成宣言を含む |
| `virtual` | `open struct` 内で、本体を持つ safe なインスタンス関数に指定する |
| `override` | 派生 struct 内で、本体を持つ実装を指定する。派生型自身は open / sealed のどちらでもよい |
| レシーバー | 正規化後に `objref/Self` または `objuniq/Self`。通常の Origin 注釈を許す |
| 非ジェネリック | 関数自身の型・Semantics・長さパラメーターを持たない。囲み型のパラメーターと通常の Origin は許す |
| 引数・結果 | 既存の値、借用、Place result を許す。`task;` も既存規則で許す |
| アクセス | 原スロットは private 不可。アクセス無指定の既定値は従来どおり private。ほかのアクセス形式は既存規則に従う |
| 対象外 | Type 関数、group 関数、Contract 要求への指定、Property / accessor、`init`、`drop`、本体のない宣言、`unsafe func` |

`virtual func` と `override func` を導入し、二つの指定を併用しない。両語は宣言修飾子の位置だけで解釈し、通常の Name としての使用は維持する。`abstract` は引き続き未導入。`final`、`sealed override`、`newslot` は追加しない。

レシーバーの省略記法 `self` は引き続き `ref/Self` なので、virtual / override のレシーバーとしては不適合である。open な派生型の override はさらに override できる。元の関数が通常関数なら、同名の宣言によって virtual 化できない。

### 2.2. スロットの契約と override ヘッダー

公開契約の所有者は原 `virtual` 宣言だけとする。override のヘッダーは、既存の full specialization（SPEC §8.8.2）と共通の契約継承規則を使い、次の差分を加える。

| 項目 | override の規則 |
| --- | --- |
| 入力 | 個数・順序・レシーバー位置・正規化した型を再記述する。レシーバーの Self 対応だけを許す |
| レシーバー | `objref` / `objuniq` を保存する。本体内の `Self` は実装を宣言する派生型 |
| `task;` | 原宣言と同じ有無を明記する。本体が実際には中断しなくても変更しない |
| 結果 | 値 / Place の区分、Place のモード、正規化した型を一致させる。結果省略は Unit。共変戻り値は追加しない |
| 名前 | 外部引数名は一致。内部名は既存の `external => internal: Type` で変更できる |
| Origin / Loan | 原契約の binder と全許容束縛を引き継ぐ。注釈省略は継承であり、新しい量化ではない。明記する注釈・関係も同じ契約に対応させる |
| アクセス、`!`、既定引数、制約、effect | 継承し、override では再宣言・追加・削除しない |
| 宣言属性・追加修飾子 | specialization と同様に追加しない。`unsafe` 指定も不可。本体内の明示的な Unsafe Block は既存の検証に従う |

アクセスは原宣言の**意味上の範囲**を継承する。例えば、別 Kotonoha の `protected internal` を派生側の Kotonoha に読み替えない。override は独立の公開 API を作らないため、その本体の派生 Self を原スロットの公開署名へ露出しない。実装本体の名前解決・private access は派生宣言の字句的文脈に従う。

レシーバー以外の `Self` は、その記述位置で解決した型のままである。基底の `other: objref/Self` が `objref/Base` なら、派生 override も `other: objref/Base` と書く。引数や結果を派生型に狭めず、`Self` の使用自体は一律禁止しない。

### 2.3. 宣言例

以下は導入後の仕様を示す例であり、現在のコンパイラでの実行確認を意味しない。

~~~kimi
open struct Base
    public virtual func score(self: objref/Self, bonus: i32 = 1) -> i32
        effect confined
        return bonus

struct Derived : Base
    override func score(self: objref/Self, bonus: i32) -> i32
        return base.score(bonus) + 10

let d = Kimi.Intrinsics.makeObj(Derived.init())
let a = d.score()                        // 11。既定引数は Base が定義する
let b = Base.score(d@objref/Base)         // 11。型名で修飾しても動的に呼ぶ
let operation = Derived.score
let c = operation(d@objref/Base, 1)       // 11。関数値では全引数を渡す
~~~

## 3. スロットの同一性・探索・実装選択

### 3.1. SlotId と override の対応

意味上のスロット参照は、原宣言の同一性と、宣言元の囲み型・基底経路の正規化済み束縛で識別する。完全な静的型と Origin 契約を保存し、実装宣言の同一性や名前文字列とは区別する。実行時の Origin 消去キーと物理的な表インデックスは別物であり、型・寿命・Loan の証明に使わない。

override の対象は次の順に決める。

1. 直接基底から既存の継承 lookup を行い、アクセス可能な同名の関数グループを一つ確定する。全祖先の同名スロットを集めない。
2. そのグループ内の virtual スロットについて、task の有無、レシーバーの対応、入力の個数・順序・正規化型を照合する。
3. 一つだけなら対象を確定し、結果、ラベル、Origin、公開保証などの契約を検査する。候補なし・複数・契約違反は宣言エラー。

戻り値、ラベル、既定引数、条件の成否、effect、宣言順、overload の優先順位で対象を選び直さない。失敗してもさらに遠い祖先や別の本体へ戻らない。例えば `Base<T>` の `f(value: ref/T)` と `f(value: ref/i32)` が `Base<i32>` で同じ入力構造になっても別スロットであり、両方に一致する override は曖昧として拒否する。

環境選択、Mod、断片のマージ後、一つの派生型から同じ SlotId への override は高々一つとする。ファイルの違い、同一の本体、型条件の排他性では重複を許さない。未使用の宣言も検査する。

### 3.2. 継承グループを維持する

override は新しい探索層・overload 候補・公開関数を作らず、継承グループの該当スロットの実装だけを更新する。基底の `f(i32)` と `f(string)` の一方を override しても、他方を隠さない。

有効な override 以外には既存の継承名禁止規則を適用する。派生側でアクセス可能な祖先と同名の新規 overload や新規 virtual は追加できない。既存規則が認めるアクセス不能な祖先名の再使用は維持し、その場合の新規 virtual は別スロットとなる。アクセス不能なスロットを override することはできない。

同じスロットを継承検索した `Derived.f` と `Base.f` は、同じ束縛なら同じ原宣言・Function Item を参照する。公開レシーバーは `objref/Base` 等のままであり、override 本体だけが派生 Self を持つ。`Derived.f(baseObject)` も原契約を満たせば合法である。

### 3.3. 呼び出しと条件付きメンバー

通常の名前解決、overload、Effective Type、アクセス、引数取得、既定引数、Origin・Loan 検査を先に行う。呼び出し可能なスロットが決まった後、実オブジェクトの動的型から、その継承経路で最も派生側にある実装を選ぶ。override がなければ原実装を使う。実行時に名前・overload・制約・アクセスを再探索しない。

`value.f(...)`、`Base.f(...)`、Function Item、Callable、共通 Function value は、同じスロットを選んだ限り同じ dispatch を行う。関数値取得時に特定の本体へ固定しない。unbound 呼び出しは通常引数の規則に従い、派生から基底への暗黙 upcast や receiver projection を追加しない。`value.f` の bound method 値は導入しない。

囲み型の引数を制約する原 virtual の条件 `P` は、既存の条件付きメンバーとして許す。スロットは宣言として存在し、呼び出し・関数値取得には `P` の証明が必要である。override 本体は、派生型の制約と、基底経路で置換した `P` の下で検査する。

override 独自の適用条件は作らない。条件付き conformance ブロック内に置く場合も、そのブロック条件を上記の前提から証明できることを要求し、追加条件で実装を切り替えない。`P` が不成立なら通常の適用不可であり、基底実装への fallback や実行時条件判定はない。適用不可の束縛について、スロットの存在だけを理由に本体生成を強制しない。

## 4. `base.f(...)`

`base.f(...)` は、現在の字句的な `self` を使い、宣言元 struct の直接基底から既存の lookup を行い、選ばれた関数の実装を直接呼ぶ構文とする。専用の値・型・ビューは作らない。

- override 本体と派生型の通常のインスタンス関数から利用できる。対象はアクセス可能な基底インスタンス関数であり、virtual / nonvirtual の両方を許す。
- `A → B → C` の C 内では、B が保持する実装を呼ぶ。B に override がなければ B の継承実装を呼び、必ず原宣言まで飛ぶわけではない。
- 直接呼び出しになるのはその一回だけである。基底本体内の `self.g()` が virtual なら、元の動的型で dispatch する。オブジェクトの動的型・メタデータは変えない。
- 公開契約・既定引数は静的に選ばれた関数のものを使い、アクセス・取得・OCC・Loan・構築破棄の制限を維持する。直接本体の effect summary は既存の直接呼び出しとして利用できる。
- 匿名関数内では通常の明示的な `self` のキャプチャを必要とする。基底探索の起点は元の字句的宣言に固定し、キャプチャのモード・寿命は既存規則に従う。名前付きローカル関数へ暗黙に `self` を渡さない。

`base` 単体、`base.f` の関数値、`base.base`、新しい基底フィールドアクセスは導入しない。レシーバーを持たない関数には通常の型修飾を使う。既存のコンストラクター初期化 `: base(...)` は別の構文として維持する。

## 5. 安全性と公開保証

### 5.1. レシーバーと生存期間

virtual 呼び出しのオブジェクト借用は、**元の完全なオブジェクト payload 全体**を保護する。基底の inline subobject だけに Loan や effect の対象を狭めない。派生実装が派生フィールドへアクセスするためである。通常の `ref` / `uniq` 基底部分射影と、オブジェクト借用によるスロット呼び出しを混同しない。

共有／排他の権限、予約・活性化、評価順、呼び出し全体と依存する結果まで続く Loan は既存規則を使う。`rc` / `arc` は共有アクセスだけを与える。通常の値・値借用から呼び出すための暗黙 boxing や、一時オブジェクト化は行わない。

原実装と全 override は、それぞれ宣言時に ObjectCallCompatible Proven を必要とする。基底の証明で派生本体を承認せず、未使用でも証明失敗は宣言エラーとする。再帰の証明は既存の固定点と義務解決を使い、未完了の証明を成功扱いしない。

既存の完全な対象の規則を維持する。open な基底ビューや基底部分全体の置換は禁止されるが、sealed な派生実装が `self@follow` で選ぶ完全 payload の合法な更新まで一律禁止しない。最適化による具体型の推測は、型・完全性・更新権限の証明を追加しない。

dispatch のレシーバー対応は、検証済みの基底経路と型・Origin の置換として保持し、全許容束縛で実装側の契約を満たすことを検査する。実行時の型 ID から、隠れた派生 Origin を任意に `static` と復元しない。公開 upcast の Owned erasure 証明、呼び出し時 Origin、元の receiver projection が保持する内部依存を区別する。結果契約に派生固有の隠れた Origin を追加しない。

構築・破棄対象自身への runtime dispatch は、helper 経由も含めて既存どおり禁止する。最適化で直接呼び出しになっても禁止は解除しない。`init` と `drop` の専用 receiver・既存の動的破棄は変更しない。

### 5.2. Effect と結果の依存

原 virtual の Constraint 領域に、既存の `effect confined` と `effect preserves results` を許す。重複・適格性・検証は Contract / Callable の既存規則を共用し、override は追加・削除しない。virtual 自体を新しい Contract 型に変換する仕組みは作らない。

仮想呼び出しは、スロットの公開契約と公開保証だけを使う。入力経由の effect は既存の requirement-call の規則、環境への effect は保証がなければ unknown とする。結果の依存先も公開 Origin / Loan 契約と既存の間接呼び出しの保守的な規則で扱い、特定本体の返却フィールドや static anchor を一般の保証にしない。sealed な呼び出し先でも合法性の判定規則は変えない。

receiver 評価、明示引数、既定引数、呼び出し準備中の離脱と cleanup の effect は従来どおり別途検査・合成する。スロットの `confined` を理由に、引数や既定式の環境アクセスを消さない。

`preserves results` のレシーバー適格性を、`ref` / `uniq` に加え `objref` / `objuniq` を含む借用レシーバーへ一般化する。結果がその呼び出しの receiver Loan や receiver 自身のストレージに依存してはならない条件は維持する。同じスロット・同じ実オブジェクトに対する過去の結果を追跡し、ビューの変更で別オブジェクトと扱わない。

この保証の呼び出し同一性は、動的呼び出しでは SlotId、`base` の直接呼び出しでは選択した実装の同一性とし、それぞれ実オブジェクトと組にする。別本体を呼び得る両者の間で、過去結果の Loan 除外を自動適用しない。通常の effect 検査で非競合を証明することはできる。基底実装への委譲では結果の由来を保持して既存の委譲検査を使い、最適化による直接化では動的呼び出しの同一性を変えない。

virtual Function Item の `confined` はスロットの公開保証から利用できる。`preserves results` は Callable の保証へ自動的に移さない。unbound 関数には毎回別の receiver を渡せるためであり、Contract requirement の Function Item と同じ規則を使う。共通 Function Type への消去は既存どおり effect 保証を保持しない。

`task;` 付き呼び出しは既存のタスク・借用規則に従う。`confined` があっても、他タスクの実行を考慮する静的 Loan 検査は省略しない。

### 5.3. Contract 適合

virtual を既存の適合条件の下で Contract 要求の実装に使う場合、保持する対応付けは特定本体ではなくスロットを指す。通常呼び出しと Contract 経由で別の実装を選ばない。

検証を次の二段階に統一する。

1. 型・Self 対応・Origin・前提を既存の適合規則で照合した上で、スロットの公開契約が要求契約と effect 保証を満たすことを証明する。
2. 原実装と各 override が、そのスロットの契約を満たすことを証明する。

現在の本体がたまたま強い保証を満たすことを適合の根拠にせず、conformance からスロットへ追加義務を後付けしない。sealed な末端型にも例外を設けない。例えば、公開 `confined` のないスロットを、現在の本体だけを根拠に `confined` 要求へ対応付けない。必要な保証は原スロットに明記するか、別の操作・ラッパーで公開する。

派生実装だけの強い保証をそのまま Contract に使える自由は減るが、子孫・別 artifact・未発見の実装を列挙せず検証できる。関連型の一致、条件付き適合、継承適合の Self 検査は維持し、適合を自動追加・黙って削除しない。runtime Contract View 全体の導入は本計画に含めない。

## 6. 実行時表現と性能

既存のオブジェクトヘッダーと、動的型ごとの共有 descriptor を使用し、descriptor 側にスロット表への到達経路を追加する。通常の値・値借用・各基底部分に vptr を追加しない。現在の Windows x64 のハンドル 8 bytes、ヘッダー 16 bytes、payload の開始位置を維持する。descriptor の現行 24 bytes という内部配置は必要に応じて変更し、永続・外部 ABI として固定しない。

各呼び出しは、既知のスロットインデックスから entry を取得する。entry は検証済みの実装と、必要な型束縛・ABI 調整・生成コンテキストに到達できるものとする。型ごとの文脈は共有し、呼び出しのためだけのヒープ確保・参照カウント変更・名前探索・型探索を行わない。引数、既定式、本体、既存の関数値消去が本来行う確保は別である。

静的な SlotId、Origin 消去後の生成キー、表の物理インデックスを分離する。Origin の違いだけで実装選択や実行時スロットを増やさず、コード共有の可否は既存の証明で判断する。slot や override のソース順・読み込み順を意味上の優先順位にしない。

動的型や到達する実装を証明できるときは直接呼び出し化・インライン化を許す。合法性と公開保証の検査を先に完了し、最適化の有無で受理結果を変えない。entry や本体の省略は到達不能を証明した範囲に限り、未使用宣言の検証は省略しない。

## 7. 診断・意味情報・依存関係

宣言モデルは、公開スロット、override 本体、直接基底からの選択、検証義務を別々に保持する。対応関係を先に解決してから本体・effect の固定点を検証し、証明待ちを理由に lookup 候補から消さない。無効な override を無視して基底実装に fallback しない。

診断は、対象なし・複数対象・契約不一致・重複・OCC / effect 違反を区別する。主位置は問題の修飾子または署名要素、関連位置は原スロット・競合実装・失敗の根拠とし、Reason に異なる事実を示す。未対応の実装経路は Unsupported として、言語上の不正と分ける。`override` の追加を修正候補にする場合は、対象の一意性と契約適合などの前提を明示・検証する。

CLI / LSP の診断と Hover、CSP の意味検査では、少なくとも次を区別できる情報を提供する。

- 呼び出しが参照する原スロットと公開契約、動的呼び出しか `base` による直接呼び出しか。
- 型ごとの対応実装、override の原スロット、レシーバー対応、証明結果と未確立の事項。
- `base` が選ぶ実装と、その字句的な探索起点。
- 対象ソース snapshot・構成・依存関係と、それに対応する検査・変更結果。

原契約、基底関係、アクセス、条件、型束縛、実装、effect が変わった場合、関連する lookup・適合・証明・生成表・表示を再検証する。artifact には必要な意味上の対応と保証を保存し、情報欠落を「override なし」と解釈しない。利用側は private 本体や既知の全子孫を探索しない。表示文字列や物理インデックスを意味 API にしない。

## 8. 実装計画と完了条件

### 8.1. 現状と前提

計画作成時点の [CODEMAP](../../docs/dev/CODEMAP.md) と [STATUS](../../docs/STATUS.md) を基準とする。現在は virtual / override を未対応修飾子として拒否する。ObjectCallCompatible の推論・使用時の強制・公開、およびオブジェクトレシーバーの継承射影は完成済みの基盤ではない。CSP §23.5 の adapter も未実装である。

task の構文・実行基盤も未実装範囲にある。同期 virtual の実行対応には OCC を必須とし、task virtual の実行対応は既存の task / coroutine 基盤の完成に依存する。未検証を NotProven に隠したり、静的呼び出しへ代替したりしない。

これらを前提作業として含める。ほかの未実装機能と交差する有効な形式は、仕様を狭めず依存課題として記録する。段階的な部分対応は許すが、部分対応を本計画全体の完了とはしない。

### 8.2. 作業単位

各段階を必要に応じて最小再現例・実装・関連テストの単位に分ける。診断・依存無効化は各段階で実装し、V5 は横断確認と公開接続を担う。表の入口は現行の主な参照先であり、固定の新規ファイル構成ではない。

| 段階 | 内容・依存 | 完了条件・主な入口 |
| --- | --- | --- |
| V0 基準と再現例 | 本書の受理・拒否・公開契約を独立した期待値にする。OCC、Origin、task、関数値、artifact、CSP の既存支援境界を調査する | §8.3 のケースと依存課題を対応付ける。`docs/dev/CODEMAP.md`、`docs/STATUS.md`、既存回帰テスト |
| V1 構文・宣言 | contextual modifier、`base.f(...)`、スロットと実装の宣言モデル、対象制限と構文回復 | 解析後に原宣言と実装を区別でき、誤宣言後も解析継続。`Parser.cs`、`FunctionKoto.cs`、`ModifierKind.cs` |
| V2 対象解決・契約 | V1。継承グループ、対象一意性、契約継承、公開 Self / 本体 Self、条件、アクセス、Function Item | 部分 override が他の overload を隠さず、原契約を全経路で保持。`Binding.MemberLookup.cs`、`Binding.Specializations.cs`、`Binding.FunctionItems.cs`、`Binding.Access.cs` |
| V3 安全性 | V2。object receiver 射影、OCC の推論・強制・公開、全オブジェクト Loan、公開 effect / alias、Contract 適合、base / capture / task / 生存期間 | 未使用実装を含め証明完了。合法な経路を Unsupported で残す場合は未完了範囲を明記。`Binding.ArgumentOperations.cs`、`Binding.ContractMatching.cs`、`Binding.EffectBounds.cs`、`OwnershipAnalysis.Objects.cs`、`OwnershipBody.Borrows.cs` |
| V4 生成・実行 | V3 の証明完了が前提。型共有表、entry、直接 / 動的呼び出し、ジェネリック囲み型、Function Item / 消去、task、破棄との接続 | 同じ公開スロットを全経路で同じ実装へ送る。O0/O2 で結果・Loan に対応する生存期間・cleanup を確認。`ObjectGenerationPlan.cs`、`BodyLowering.Calls.cs`、`FunctionAbi.cs`、`LlvmModuleWriter.Objects.cs` |
| V5 公開・再検証 | V2–V4。artifact 契約、編集・依存変更の無効化、CLI / LSP の出力、CSP の必要 adapter と snapshot 検証への接続 | 古い対応・証明・表を再利用しない。CSP 接続前は CSP 対応を完了扱いしない。`Binding.Diagnostics.cs`、`Binding.Hover.cs`、`HoverFacts.cs`、`CheckService.cs`、`LspSession.cs`、依存 artifact 処理 |
| V6 性能・統合 | V4–V5。直接呼び出し化、表とコンテキストの共有、allocation / capacity 回帰、性能計測、Session 検証 | 正確なソース・構成に結び付いた全検証と測定を保存。`LlvmEmitter.cs`、`ObjectGenerationPlan.cs`、`src/Benchmark` |

Compiler の入口は `src/Kimi/Compiler/`、`FunctionKoto.cs` はその `Parsing/Koto/Declarations/` にある。ほかは `src/Kimi/Misc/ModifierKind.cs`、`src/Kimi/Checking/HoverFacts.cs`、`src/Kimi/Checking/CheckService.cs`、`src/Kimi/Lsp/LspSession.cs` を参照する。現行の責務分割に従い、LLVM writer に名前解決や契約判定を持ち込まない。

関連する既存テストは `InheritedNameBindingTest`、`InheritedReceiverBindingTest`、`BaseProjectionAccessBindingTest`、`MemberFunctionReferenceTest`、`EffectBoundImplementationTest`、`EffectBoundCallerTest`、`CallableEffectBoundTest`、`CallReservationTest`、`ContextualCaptureTest`、object runtime 系、`SyntaxEditInvalidationTest`、診断・Hover 系とする。新しい virtual 専用クラス・fixture は実装時に追加し、既存テスト名と区別して選択する。

### 8.3. 必須の検証範囲

| 分類 | 最小限の受理・拒否・相互作用 |
| --- | --- |
| 宣言 | open / sealed、private、通常 Name と contextual modifier、対象外宣言、両修飾子、未使用の違反、fragment / Mod / 環境選択後の重複、隣接エラーからの回復 |
| スロット | 三段継承、中間型に override なし、一部 overload だけ更新、通常関数との同名拒否、アクセス不能名の合法な再使用、型束縛後の曖昧性、遠い祖先への再探索なし |
| 契約 | 共有 / 排他、receiver 位置、外部名、`!`・既定引数の継承、値 / Place result、Self の非レシーバー使用、Origin の省略・隠れた派生依存、追加前提の拒否、条件付き root と不成立時の扱い |
| 呼び出し | member、unbound、Function Item、Callable、共通 Function value、明示 upcast、ジェネリック囲み型、`task;`、評価・既定引数の順序と一回性 |
| base | 直近の継承実装、内部の再 dispatch、通常メソッドからの使用、明示 capture、共有 / 排他、関数値化の拒否、基底コンストラクター構文との区別 |
| 安全性 | 派生フィールドを含む全オブジェクト Loan、引数との競合、返却借用・Place、生存期間、OCC の独立証明、完全 sealed payload の合法な更新、構築・破棄対象自身への直接化後も変わらない禁止 |
| Effect / 適合 | 後続 override が基底本体より広い effect を持つ例、未知の環境 effect、引数・既定式の独立した effect、公開保証からの適合、conformance からの義務後付け拒否、関数値への保証伝播境界、過去結果・オブジェクト同一性、動的呼び出しと base の交差、task の静的 Loan |
| 実行時 | `obj` / `rc` / `arc` と共有・排他 borrow、ヘッダーと Identity の維持、動的破棄と一回だけの cleanup、dispatch 自体の追加確保・参照カウント操作なし、O0/O2 の同一結果 |
| 公開・再利用 | 別 Kotonoha・artifact 往復、必須情報欠落、基底・契約・本体変更後の再検証、CLI / LSP / CSP の位置と保証、snapshot 不一致、温まった解析・生成の allocation と保持容量 |

不正例には独立に書いた公開診断期待値と有効な対例を置く。診断件数だけで改善を判断せず、主位置・Reason・関連位置・Note / Advice・修正候補の前提を確認する。未対応を言語上の拒否として固定せず、対応時には native 受理例へ置き換える。

### 8.4. 検証と記録

[VERIFICATION.md](../../docs/dev/VERIFICATION.md)とリポジトリの実装・診断ワークフローに従う。途中の incremental build / 選択テストはフィードバックに使い、各実装単位の完了は関連クラス・生成 fixture・必要な milestone を選ぶ Unit Verify で確認する。実装セッションの最後には Session Verify を一度実行する。Release、warnings as errors、functional と allocation / reuse、native O0/O2 を維持し、検証中はソースを編集しない。NativeAOT は実行しない。

性能測定は `src/Benchmark` で、直接呼び出しと仮想呼び出し、同一 / 混在動的型、共有 / 排他、再解析・再生成を固定条件で比較する。言語上の確保保証と timing を分け、測定前に速度向上を主張しない。

成功・失敗を含む検証証拠は `artifacts/verify/`、測定は `artifacts/benchmarks/` に保存する。必要な検証を通した単位だけを、その単位の変更ファイルに限定して commit し、証拠を対応付けて現在のブランチを origin へ push する。未完了の依存・対応経路と次の作業を残し、検査済みの範囲だけを支援済みとする。

## 9. 後続の正式取り込み

本書作成では以下を変更しない。正式取り込み時に、重複した規則を増やさず、主担当節へ統合する。

| 取り込み先 | 主な変更 |
| --- | --- |
| SPEC §2・§6・付録 D/E/F | contextual modifier、宣言適格性、slot / override、用語・構文・未導入範囲 |
| §7–§10 | 共通の実装契約継承、公開 / 実装 receiver、探索、対象照合、条件付きメンバー、関数値、Contract 適合と effect |
| §12–§16・§24 | 静的 / 動的 / base 呼び出し、object acquisition、OCC、Origin / Loan、capture、構築破棄、task |
| §18・§23・IMPL §20–§21 | artifact と依存再検証、意味情報・診断・CSP、生成段階、descriptor / slot 表、最適化 |
| CODEMAP・PLAN・PLAN_HISTORY | 入口と責務、実装順と依存、簡潔な作業記録。PLAN は既存の行数制限を守る |
| STATUS・STYLE・LIBRARY・SETTLED | 検証済みの支援境界、必要な規約・公開宣言・不採用理由の変更だけを反映する |
| draft/INTEGRATED.md | 本書の節と取り込み先、残件、最終処分を正式仕様の変更と同じ commit に記録する |

正式仕様の更新は英語で行い、影響する例・テスト・milestone を同時に整える。未解決項目があれば取り込んだ範囲だけを凍結し、一部取り込みとして記録する。全項目の処分が確定した時点で、一項目以上取り込んだ場合は取り込み済みとして `draft/Changes`、取り込みがなければ完了として `draft/Obsolete` に移し、本文全体を凍結する。正式取り込み、実装完了、検証完了は別々に記録する。
