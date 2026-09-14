# Composition Root 再レビュー — 統合前の検討記録

**統合済み:** 本書の改善策は [Composition Root 仕様](2026-09-13%20Composition%20Root%20Review.md)へ反映した。現在の規則と具体的な決定は統合先を参照する。以下は統合前の検討記録であり、統合先や SPEC に優先する仕様ではない。

2026-09-13。原案、root function は置換不能という指定、および関連する設計文書を検討した記録を保持する。

[初回レビュー](2026-09-13%20Composition%20Root%20Review.md)を再評価し、問題と対策を共通規則へ整理した。[依存・成果物仕様](../Design/2026-09-13%20Dependencies%20and%20Artifacts.md)、[generic 共有設計](../Design/2026-09-13%20Generic%20Sharing%20and%20Specialization.md)、[テスト仕様](../Design/2026-09-13%20Testing.md)の現在の内容も確認した。実装参照は HEAD `9ab255a` と既存の未コミット変更を含む作業ツリー。コード・SPEC・STATUS は変更せず、テストと性能測定は実施していない。

## 1. 判断と前回からの修正

**実現可能である。問題の中心は新構文そのものではなく、API の意味と実装選択の境界が未定義なことである。** 宣言と構成を分ける `group $` / `compose $` は維持してよい。

旧 SPEC が構文を認めていないこと、既存 group の項目と違うこと、emitter が未対応なことは、変更案の欠陥とはしない。新しい規則として明記する対象、または実装作業である。また Core.writeLine との違いは互換性維持を強制する理由ではなく、「標準出力の依存が二系統になる」ことが問題である。

前回の Self・関連型の制限も、旧仕様が許さないから禁止するという提案ではない。Provider 非依存の型検査と公開 API を維持するために必要な条件である。抽象型を導入する別解はあるが、この機能だけのために新しい型消去・配置・所有権規則を増やす費用が大きい。

改善策は、以下の五つへまとめられる。

| 共通規則 | 整理できる問題 |
| --- | --- |
| **契約を先に固定し、実装は契約を満たすものから選ぶ** | overload、型、Origins、所有権、効果、Library 検証 |
| **宣言の Identity と最終接続の Identity を分ける** | Library→Application の見かけの循環、複数版、cache、再利用 |
| **一つの最終実行対象について、各 Entry の Provider は一つに固定する** | Default、重複、テスト、実行中の差し替え、直接 call |
| **構成は値を生成しない。状態には通常の値の規則を適用する** | singleton、共有・排他、複数インスタンス、初期化、破棄 |
| **最適化は検証済みの接続と取得計画を使い、選択結果を変えない** | direct call、共有コード、DCE、キャッシュ、診断の再現性 |

## 2. 問題と改善策

### R1. Contract を見せるだけでは、Provider による API 変更を防げない

**問題。** 原案 §9 は Provider の追加 member を隠すが、Contract の Self、関連型、Constraints、Origins、効果まで固定されるとは定めていない。

```kimi
contract FactoryComposition
    func create() -> Self
```

この Entry に A と B を指定すると、create の結果型が変わる。A が Copy、B が Non-Copy なら、同じ利用側ソースの取得・破棄も変わる。`associate Handle` の実装ごとの違いも同じ問題を生む。名前と引数の数が合うことは代替可能性の証明にならない。

**改善策。** Composition Contract の適格性を次の一規則で定める。

> Provider を知らなくても、Entry から公開する操作の型・呼び出し条件・取得・寿命・保証を確定できなければならない。

判定は綴りではなく、継承、alias、関連型を正規化した後の意味に対して行う。Provider 非依存に固定できる関連型まで一律禁止する必要はない。未固定の Self/関連型はこの条件により拒否する。通常の関数 generic とその Constraints は、同じ条件を満たせば許容できる。

Provider は、通常の静的 Contract に明示的に適合する、完全に束縛済みの具体型を推奨する。Entry から公開する操作は receiverless な型関数とする。これにより型関数の既存規則を利用でき、Entry に implicit self を作らずに済む。group Provider や instance Provider が原理的に不可能という意味ではないが、別の適合・状態管理モデルを増やす必要は薄い。

Provider の照合は通常の Contract 適合を使う。パラメーターの型・順序・ラベル、generic schema、safe/unsafe、Origins、Constraints、結果の保証、access を検証する。Composition 専用の緩い照合や、自動 Borrow/Move/default 補完は作らない。

**性能。** Provider に依存しない利用側の意味計画を一度作り、複数 Application で共有できる。接続時には選択済み Operation と witness の対応を使い、overload 探索や引数取得の再計算を行わない。

### R2. 最終構成を全意味解析より前に置くと、Library と証明の順序が成立しない

**問題。** 原案 §7 は Library が最終 Provider を選ばないとし、§10 は Final Composition を Binding / Semantic analysis より前に置く。Application 用の概念図として読めても、Contract と Provider の適合を検証するための意味情報をどこで確定するかが不足している。

さらに、Default Provider の本体が「たまたま引数を変更しない」ことを根拠に Library の借用や効果を証明すると、別 Provider で証明が壊れる。安全な関数だから副作用がない、関数名が read だから書き込まない、といった推定もできない。

**改善策。** 意味上の境界を三つにする。

```text
契約解析     : Entry と Contract の型・呼び出し契約を確定
利用側検証   : Contract の保証だけで call・所有権・公開保証を検査
最終接続検証 : Provider がその契約を満たすことを確認して接続
```

Library は最初の二段階を完了し、Entry/Operation の参照を残す。Application は最後の段階で Default と明示 Binding を選択して検証する。Provider 候補の名前解決などは前倒しできるが、未検証の Provider を証明の根拠にしない。

公開する保証は Contract の保証から完成させる。任意の効果保証がなければ、既存の保守的な検査を適用する。必須情報の不足、未実装、Unknown を「効果なし」や完成済み NotProven に読み替えない。

「Provider が未選択」と「型検査が未完了」は異なる状態である。前者は Library の正常な接続待ち状態として成果物に残せる。後者を一般に許す機能にはしない。最終接続で検査するのは契約への適合と実装・ABI の結び付けであり、Provider の都合で利用側の型・overload・公開保証を再選択しない。

Mods の生成・directive 選択が宣言集合を変える間は provisional とし、その完了後に最終的な重複・適合・接続を検証する。生成元 Library が Application の構成を決める権限は得ない。

**性能。** Default で Library を一度完全生成し、Application で全面的に Binding し直す二重処理を避ける。再帰する解析は worklist で処理し、既存の証明・witness を再利用する。

### R3. Provider への接続を通常の依存にすると、DAG と Identity が循環する

**問題。** 現在の依存仕様では、モジュール依存は DAG であり、ModuleInputId に直接依存の ModuleInputId が入る。

```text
App は Library を参照する。
Library は $Std を使う。
App は Std => AppStd を指定する。
```

最後の接続を Library の普通の App 依存として登録すると `App → Library → App` となる。全 Library の ModuleInputId に最終 Composition を入れても、Composition が App 側の Provider 宣言 Identity を参照するため、同様の循環を招き得る。循環を避けても、構成変更で無関係な Library の Identity や static が複製される設計は望ましくない。

**改善策。** モジュール入力と接続を別の関係として管理する。

| 関係 | 意味 | 循環 |
| --- | --- | --- |
| 宣言・パッケージ依存 | 名前・型・Contract の定義を得るための参照 | 現行の依存仕様どおり DAG |
| 最終接続・call の関係 | 検証済み Operation がどの Provider 実装を呼ぶか | 通常の有限な相互再帰を許す |

Library が宣言上依存するのは Entry と Contract の所有モジュールであり、Application の Provider ではない。Provider は自分の定義サイトの通常依存を持つ。App で両者を接続しても、Library の名前環境へ App を追加しない。

概念上の識別も分ける。

```text
ModuleInputId : モジュールの固定入力・環境・宣言された依存
CompositionId: 確定した Entry/Contract/Provider の対応と SDK/profile の選択
ArtifactId   : モジュール入力集合・Composition・ABI/生成設定・実際の成果物
```

構成に依存する証明や生成計画は Composition にも依存させる。Library の ModuleInputId へ Application の最終構成を逆流させない。App 自身のソースに書かれた Binding の編集が App の入力を変えることは通常どおりであり、除外する必要はない。

Call graph の循環を、参照先の hash の無限展開で表現しない。宣言 Identity と辺を保持し、再帰する生成物は node を先に登録して後から参照を確定する。

**性能。** 同じ Library の型・Symbol・static を一つに保ち、複数の構成で解析を再利用できる。DAG 解決に Composition の再帰を持ち込まないため、依存検証と hash 計算も単純になる。

### R4. Root の名前と Default の決定性がまだ十分でない

**問題。** 「列挙順に依存しない」だけでは、二つの Library が同じ `$Clock` を宣言した場合、SDK の版が異なる場合、明示 Binding が壊れている場合の結果が決まらない。全 Library の `group $` を名前だけで merge すると、無関係な依存の追加で公開 API が変わる。

**改善策。** Entry は既存のモジュール・宣言 Identity に所属させ、各 Entry に一つの固定 Contract を与える。新しい独立した package identity 体系を作らない。

- 同じ Entry の別参照名は同じ Entry。同じ綴りでも別宣言・別版なら別 Entry。
- Entry と Contract の別々の所有者を許すが、Entry は指定した Contract Identity を保持する。
- `$Std` などの SDK の短い名前は canonical Entry に結び付ける。ユーザー宣言で横取りしない。
- Library 独自 Entry は所有元を識別できる限定参照を持つ。具体的な表記は仕様で決める必要がある。単純名の全依存探索は行わない。
- `group $` fragment は異なる Entry を追加できるが、既存 Entry の再宣言や API の追加を行わない。

選択規則は一つの表にまとめる。

| 明示 Binding 数 | 結果 |
| --- | --- |
| 2 以上 | 同じ Provider を書いていても重複エラー |
| 1 | その Provider を選択し、無効ならエラー。Default に戻さない |
| 0 | SDK/profile が指定する一意な Default を使う。必要な Entry に Default もなければエラー |

Default は自動発見した Provider 候補の順位付けではない。Library は構成待ちの要求だけを保持し、そのビルド環境の Default を固定しない。Default の出所と最終選択は manifest に表示できるようにする。

root function はユーザー指定によりすでに置換不能なので、この表の対象外である。原案の DefaultExpect / DefaultRequire は記述上削除すべき不整合であり、設計上の選択肢として再検討する必要はない。

**性能。** 宣言収集で文字列を一度解決し、以後は Entry ID と固定配列を使える。明示 Binding が指す具体型を直接検証し、全 Provider の総当たりを避けられる。

### R5. 「Entry は値でない」だけでは、状態の共有と寿命が決まらない

**問題。** Provider を選ぶと暗黙の instance を作るのか、二つの Entry に同じ Provider を指定すると状態が二つになるのかが未定義である。Entry 名が違うことを根拠に、借用や効果で非aliasを仮定する危険もある。

```text
LogA => BufferedLog
LogB => BufferedLog
```

BufferedLog が同じ group の static buffer を使うなら、二つの Entry から触る状態は共通である。別の Provider 型でも、同じ helper/static を使えば共有できる。Entry Identity と runtime storage Identity は一致しない。

**改善策。** 構成は操作の接続だけを行い、値の生成・複製・所有を行わない。Provider 型関数は普通に呼ばれ、そこで利用する値と静的保存域に既存の規則を適用する。

静的初期化は実際の初回アクセス、破棄は正常初期化の逆順とし、Binding の順序で起動順を作らない。二つの独立した logger が必要なら、通常の値として二つ生成し、それぞれを引数や所有する Field に渡す。Entry の数に応じた hidden instance を追加しない。

Entry を値にしないことと、その Operation を関数値にできるかは別に定める。将来関数参照を許すなら、通常の Function Item と Origins の規則を使い、論理的な Entry/Operation Identity を維持する。初期実装が直接呼び出しだけに対応する制限は、実装範囲として明示する。

循環も共通の既存規則で分けられる。通常の関数相互再帰は許し、静的初期化の循環は既存の Abort 規則で検出する。接続時点で全 call graph を DAG に制限しない。`MyStd.writeLine → $Std.writeLine → MyStd.writeLine` は Default への委譲ではなく自己再帰である。

**性能。** Composition 専用の heap allocation、hidden receiver、初期化ガード、破棄リストが不要になる。普通の static が必要とするガードまで除去してよいという意味ではない。

### R6. 全依存の集約で、出力の二系統化と失敗処理の循環が起きる

**問題。** `$Std.writeLine` を差し替えても、Core.writeLine が今までどおり独立して OS 出力を呼べば、Application から全体の出力先を選べない。逆に公開 Core.writeLine を `$Std` に接続した後、DefaultStd が Core.writeLine を呼ぶと循環する。

allocation、ログ、Abort を同じ経路へ無条件に載せても、失敗した allocator の診断が同じ allocator を要求する、といった循環が生じる。

**改善策。** 入口と実装の方向を固定する。

```text
公開される環境 API → Entry → 選択された Provider → 必要な低水準実装
```

Core.writeLine は新入口へ統合または置換する。互換 API を残す場合も同じ Entry へ接続し、独立した環境選択を持たせない。Default Provider は自分へ戻る公開 API を呼ばず、compiler/SDK 内部の低水準実装を使う。

Abort など言語が保証する操作には、その保証を弱めるユーザー Provider を使わない。診断の成功に依存せず終了できる最小経路を保持する。これは Std だけの例外を増やす提案ではなく、すでに確定している「言語の組み込み保証は置換不能」を低水準実装にも適用することである。

allocator まで差し替える設計では、allocate/free/resize、alignment、所有者、失敗、FFI 境界を一つの資源契約として規定する。各関数の Signature 一致だけでは、別 allocator による誤解放を防げない。今回すべての runtime primitive の自由な差し替えを同時に導入する必要はない。

**性能。** Default への直接接続や不要な forwarding の削除が可能になる。ただし adapter の ABI、引数取得、source location、cleanup を保てる場合に限る。

### R7. 巨大な Std と、要求する範囲の曖昧さが変更コストを増やす

**問題。** Std に出力・時刻・乱数・ファイル・network・allocation を集めると、一操作を差し替えるために全機能を実装する必要がある。追加された未使用 Operation を無視してよいことにすると、Provider の適合が利用側や最適化ごとに変わる。

**改善策。** 同時に交換する操作を一つの Contract にまとめる。Console、Clock、Random など独立した機能は小さい Entry へ分割し、同じ資源を管理する allocate/free はまとめる。関数一つごとへの機械的な分割も避ける。

検証とコード生成を区別する。

- Contract 宣言と明示 Binding は未使用でも検証する。
- 選択した Provider は Contract 全体に適合する。使用した member だけの部分適合を作らない。
- Entry 宣言だけでは必ずしも Provider を要求しない。最終対象に含めるモジュールの、選択済みソースの検査対象本体が持つ Entry 要求を集計する。
- compile-time directive で除外された本体は要求を増やさない。runtime 分岐の定数畳み込みや DCE で意味上の欠落診断を変えない。
- Provider 本体・initializer・destructor・必要な生成処理の要求も収集し、追加要求を解決する。単にユーザーが書いた `$` の call site を集計するだけでは不足する。
- 実際に生成する不要コードは、通常の規則の下で除去できる。

**性能。** 小さい Contract は適合検証、再検証範囲、Provider の実装量を減らせる。選択した Provider の要求を一度収集し、既訪問集合で依存閉包を求める。列挙全体を繰り返す方式は避ける。

### R8. テスト時の差し替えと成果物再利用に境界が必要

**問題。** Library 自身は最終構成を選べないため、Library のテストでは構成の所有者が不在になる。テストソースの後勝ち override を認めると、製品の確定済み意味と、同じ成果物を全ケースで使う規則を崩す。

**改善策。** Application と compiler が作るテスト host を、共通に「最終実行対象」として扱う。一つの最終実行対象が一つの固定 Composition を持つ。

通常のテストでは製品と同じ接続入力を基本にする。別構成を明示するテスト target は、最終接続段階で異なる成果物を作る。製品の Contract 検証済み意味を変更せず、製品ソースとテストソースに書いた二つの Binding を暗黙の順位で解決しない。

新しい target を選ぶ具体的な設定と、製品ソース内の compose をどう選択するかは、テスト仕様と同時に定義すべき未確定事項である。初期対応では per-case 構成と runtime swap を導入しない。ケースごとのフィルター変更は構成を変えず、同じ実行ファイルを再利用する。

root function 自体は差し替えない。expect/require の使用可能位置と評価手順は独立した言語契約であり、通常の関数宣言に見える記法だけから推定しない。既存テスト仕様を維持するなら、原案の例を #Test 内に移す。通常製品コードへ広げたい場合は、今回の構成機構とは別に意味を明示する必要がある。

**性能。** ケースごとの再コンパイル・再リンクを避け、同じモジュール解析を複数のテスト構成で共有できる。

## 3. 性能改善の優先順位

ここでいう効果は実装上の期待であり、測定済みの性能保証ではない。

| 順位 | 改善 | 主に減るもの | 正しさの条件 |
| --- | --- | --- | --- |
| 1 | Contract で確定した call に、最終 Provider の callee を直接接続 | runtime lookup、間接 call、余分な wrapper | ABI・取得・cleanup・診断位置を保存する |
| 2 | `(Provider の具体型, Contract, 検証環境)` の適合結果を共有 | 繰り返す照合と witness allocation | 型/Origin 引数・条件・依存内容が同じ。異なる Entry の接続は区別 |
| 3 | Entry/Operation を intern し、call site は小さい参照、接続表は compilation ごとに一つ | メモリ、文字列 hash、call ごとの配列 | 永続 ID と一時的な配列 index を混同しない |
| 4 | Library の意味計画と Composition 接続計画を分ける | 構成変更時の全面再解析 | 実際に構成へ依存した証明は確実に失効させる |
| 5 | 依存を逆参照付きで記録し、変更箇所から worklist で伝播 | 無関係な再検証、全表走査 | 選択候補の不在・内容集合への依存も記録 |
| 6 | 共通 generic body と entry/context pair を既存の方式で利用 | Provider/Entry ごとのコード複製 | 読む schema・実装・埋め込む定数・ABI が適合 |
| 7 | 実際に必要な code/context/runtime helper だけ生成 | binary と生成時間 | 意味検証や必要な初期化・cleanup を省略しない |

特に、Composition 全体の ID をすべての関数 body の共有キーへ入れる方法は単純だが過剰である。意味に関係のない Clock の変更で、Clock を使わない関数まで共有不能になる。最初は保守的に失効させ、依存を正確に記録できた計画から「実際に読む接続と埋め込む実装」へ絞る。正確性を証明できない段階で、見た目の呼び出し一覧だけからキーを縮めない。

初期配布が source 先行と決まったため、Provider を確定してから一つの最終 LLVM module を生成できる。これを利用し、最初から安定した別 object ABI や永続 machine-code cache を導入しない。意味計画の再利用を先行させる。

一方、次の変換は Composition が固定されたことだけでは許されない。

```kimi
let a = $Clock.now()
let b = $Clock.now()
```

同じ Provider を呼ぶからといって、二回目を a で置き換えたり loop 外へ移動したりはできない。固定されるのは実装の選択であり、結果や副作用ではない。同様に Entry 名が違うから noalias、safe だから readonly、といった LLVM 属性を付けない。実装と通常の証明が与える根拠に従う。

実装後は次を測る。

| ケース | 測定と意味の確認 |
| --- | --- |
| 小さい確定 Provider の大量 call | 通常の直接 call と比較し、IR・実行時間・call site allocation を確認 |
| 多数 call site が同じ Contract を使う | 適合検証回数・witness 数・compiler allocation が call 数に比例増殖しないこと |
| 同じ Library を構成 A/B で生成 | Library の解析再利用、Provider と生成物の正しい更新 |
| Clock のみ変更 | 影響のない意味計画の再利用と、影響する inline body/context の失効 |
| 複数 Entry が同じ Provider を使用 | 適合証明とコード共有の効果、static/借用の誤分離がないこと |
| 大量の Entry・深い要求連鎖・有限な再帰 | worklist の時間・最大メモリ・資源上限診断 |
| 大量の短いテストケース | 構成固定で再ビルドが発生せず、ケース結果・cleanup が一致すること |

## 4. 原案へ反映する最小の規範

仕様を長い例外一覧へ広げず、次の規則を中心に改訂することを勧める。

1. root function は compiler 指定の言語操作であり、再定義・overload の追加・Composition Binding を認めない。
2. Entry は所属する宣言 Identity と固定 Contract を持つ。Entry は値・保存域ではなく、構成によって instance を生成しない。
3. Composition Contract は、Provider 非依存に公開操作の型・条件・取得・寿命・保証を確定できるものに限る。
4. Provider は通常の静的適合でその Contract を満たす。利用側は Contract で一度操作を確定し、最終接続で lookup をやり直さない。
5. Library は Provider 非依存の意味を検証し、未接続の Entry/Operation 参照を保持する。最終接続は通常のモジュール依存と区別する。
6. 一つの最終実行対象で各 Entry は一つの Provider に固定する。明示一件、SDK Default、欠落エラーの規則を使い、無効指定の fallback と source-order 優先を認めない。
7. Provider の状態・相互再帰・初期化・破棄は通常の言語規則に従う。Entry 名から instance の独立性を推定しない。
8. 最終接続を検証した後に、通常の ABI・code sharing・最適化で生成する。構成と依存を成果物に記録し、キャッシュや DCE で意味を変えない。

この整理なら、宣言と実装選択の分離という原案の利点を保ちながら、型体系、依存 Identity、所有権、成果物、最適化をそれぞれ既存の共通規則に接続できる。
