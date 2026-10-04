# 設計案：マイルストーン完了までに決める事項

日付：2026-10-04

状態：最終版。本書 7 の判断は、2026-10-04 にユーザーがすべて推奨案どおりに決定した。正式仕様への取り込みは draft/INTEGRATED.md に記録する。実装は未実施である。

本書は、残りのマイルストーン・プログラム P24、P25、P26、P33、P34、P35、P36、P38（tests/milestones/Milestone<N>.kimi）をすべて実装する前に決めておく必要がある、言語仕様と実装仕様の事項をまとめ、それぞれの推奨案を示す。方法は次のとおりである。各プログラムの構文と期待する動作を、SPEC、IMPL、docs/SETTLED.md、SPEC 付録 D と照らし合わせ、仕様の欠落・矛盾・曖昧さ、保留中だが必要な事項、実装仕様の欠落、プログラムの綴りの誤りを候補として挙げた（29 件）。各候補は反証を試みる検証にかけ、仕様がすでに決めている事項や実装作業だけの事項 15 件を除いた（本書 6）。残る 14 件を六つのテーマにまとめ、規則が少なく、一貫し、広く使える形になるように、Kimigayo Principles に沿って決定案を作った。検証で最初の文言が退けられた案は、検証が示したより良い案に置き換えてある。

SPEC の節は「SPEC §n」、実装仕様の節は「IMPL §n」、本書の節は「本書 n」と表記する。行の参照は `dev` の `e6c98f0e` 時点のものである（`697b001b` との違いは docs/dev/PLAN.md の行だけである）。「NN:LLL」は第 NN 章のファイル（第 20・21 章は docs/impl、それ以外は docs/spec）の LLL 行、「A:LLL」は IMPL 付録 A、「D:LLL」は SPEC 付録 D、「README:LLL」は tests/milestones/README.md、「PNN:LLL」は tests/milestones/MilestoneNN.kimi の LLL 行を表す。

要点は次のとおりである。

1. 決める事項は 14 件で、六つのテーマにまとまる。新しい構文も新しい概念も加えない。P24 は、どの決定も必要としない。
2. **呼出し可能値のシグネチャーを証拠にする（本書 3.1）。** 推論・関数参照・消去が、一つの「既知の呼出しシグネチャー」と一つの固定された期待呼出しシグネチャー `S` を共有する。P35:41 の `makeRcCyclic(build)` と SPEC §13.5.9 の例の推論は、書いたまま有効になる（例の別の誤り `weak@move` は同じコミットで直す）。
3. **Origin だけの失敗は独立した要件にする（本書 3.2）。** 構造の不一致と分けて選択の後に判定し、反証と未証明を二つのコードで報告する。名前のない Origin は原文で示し、構文を作らない。
4. **非修飾名は継承した宣言を選ばない（本書 3.3）。** 基底の層は、止めて報告するためだけに調べる。
5. **地点ごとに一つの型（本書 3.4）。** 精密化した名前は、すべての値の使用で Effective Type を持つ。`is` の対象の束縛は一か所で補完し、事実は集合の積で結合する。
6. **アクセスの種類が効果を決める（本書 3.5）。** 共有アクセスは書き込まず、書き込める別名と view（`uniq`・`obj`・`objuniq`・`raw`）は対象に不変である。ObjectCallCompatible の同一ビルドの第 2 段階を範囲に入れるには、ユーザーの指示が要る。
7. **静的記憶域の一つの生存期間（本書 3.6）。** キーで識別する根、四つの状態、`var` を先にする二段階の終了処理、位置を持つ二つの Abort コードで構成する。
8. マイルストーンのソースのコードを書き換えるのは、P36 の `Resource.drop` の一か所（作成時の誤り）だけである。ほかには、P36 の最後の注釈を Q22 の終了処理の順序に合わせて直す（出力は変わらない）。

## 1. 結論の一覧

| id | 決定事項 | 対象マイルストーン | 種別 | 優先度 |
| --- | --- | --- | --- | --- |
| Q01（本書 3.1.1） | 引数の既知の呼出しシグネチャーを、パラメーターの `S` と step 3 で一緒に照合し、`S` にだけ現れるスロットを決める | P26（G10）、P35:41 | 仕様の欠落 | 実装中に必要（P26）、着手前に必要（P35） |
| Q02（本書 3.1.2） | 関数参照は `S` で候補の自分のスロットを束縛し、SPEC §10.4 で選ぶ。`S` を一度だけ定義し、要件の参照の同一性を決める | P26（G10） | 仕様の曖昧さ | 実装中に必要 |
| Q03（本書 3.1.3） | 共通 Function Type への消去を SPEC §10.2 の表の一行にし、期待型が固定されるすべての位置で働かせる。期待する enum 型が総称引数を先に決める | P26 | 仕様の矛盾 | 実装中に必要 |
| Q26（本書 3.2.1） | Origin だけの失敗を独立した要件「Origin 関係」として選択の後に判定し、Refuted と Unknown を二つのコードで報告する。名前のない Origin は原文で示す | P26、P34、P36、P38 | 仕様の欠落 | 実装中に必要（最初の拒否検査の前） |
| Q10（本書 3.3.1） | 非修飾名は受け手を補わず、継承した宣言を選ばない。基底の層は止めるためだけに調べ、`QualificationRequired_Kd` で報告する | P25（隣接形） | 仕様の曖昧さ | 実装中に必要 |
| Q12（本書 3.4.1） | 地点ごとに一つの Effective Type をすべての値の使用に使う。`is` の対象に Origin を書かず、束縛は一つの補完の段落で決める。`obj`・`objuniq` は View Target に不変 | P33、P38:32 | 仕様の欠落 | 実装中に必要 |
| Q13（本書 3.4.2） | 事実を取りうる Dynamic Type の集合とし、経路では積をとる。空の集合は合流を制約せず、ループは最初の入口から一意に解く | P33 | 仕様の曖昧さ | 実装中に必要 |
| Q19（本書 3.5.1） | 対象に不変なのは `uniq`・`obj`・`objuniq`・`raw` だけとする。pair 層は宣言の変性で共変に数える。`Weak<S>` は `S` を格納するものとして解析する | P34、P35、P38 | 仕様の曖昧さ | 明確化（P35 で Weak を宣言する前） |
| Q18（本書 3.5.2） | オブジェクトへの Borrow は、経由したハンドル Place に Loan を保つ。ハンドルの破棄は、そのハンドル Place だけを破棄する | P34（U4、U6）、P35、P38 | 仕様の曖昧さ | 明確化 |
| Q09（本書 3.5.3） | `ref/Self` の受け手は宣言が有効なら Proven とする。排他入力の上界を定め、ObjectCallCompatible の第 2 段階（同一ビルド）を範囲に入れる | P38（:43、:44、:47）、P25（隣接形） | 保留中だが必要 | 着手前に必要（P38） |
| Q20（本書 3.6.1） | P36 の `Resource.drop` に `2 =>` の腕を加え、PLAN §5 の再表記の規定を作成時の誤りの訂正にも広げる | P36 | プログラムの綴り | 着手前に必要 |
| Q22（本書 3.6.2） | 終了処理は局所変数の後始末の後に始め、初期化済みの `var`、次に `let` を、それぞれ初期化の逆順で破棄する。依存の拒否は削る | P36、P38 | 仕様の欠落 | 実装中に必要（終了処理の前） |
| Q23（本書 3.6.3） | 静的記憶域の暗黙の Abort に `KIMI_E_STATIC_CYCLE` と `KIMI_E_STATIC_SHUTDOWN` を当て、スロットを指す Place 式の始まりで報告する | P36 | 仕様の欠落 | 実装中に必要 |
| Q25（本書 3.6.4） | 静的キーを Place の重なりの根にする。同じ Field の二つのキーは、どちらも閉じていて異なるときだけ分離する。直接アクセスも初期化の効果を持つ | P36、P38 | 仕様の曖昧さ | 実装中に必要 |

## 2. 方針

規則の数を抑えるため、14 件を次の方針で決める。どれも既存の概念を使い回し、新しい構文は加えない。

1. **証拠は書かれたものだけ。** 推論・選択・判定に使う証拠は、書かれたシグネチャー、公開された Constraint、宣言だけとする。本体、実行時の状態、呼出し側の文脈は証拠にしない。本書 3.1 の待つ引数、3.3 の基底の層の検査、3.4 の対象の補完、3.5 の共有受け手は、どれもこの方針の現れである。
2. **一つの概念に一つの位置。** 暗黙の適応は SPEC §10.2 の表の行だけで決める（消去を含む）。関数参照と無名関数の期待は一つの `S`、精密化した名前の型は地点ごとに一つの Effective Type、静的スロットは一つの状態機械で表す。同じ概念を節ごとに列挙し直さない。
3. **一つの要件と分類に一つのコード。記録は確立した事実だけ。** SPEC §23.3.6.1 を、Origin 関係（本書 3.2）、非修飾名（本書 3.3）、実行時の Abort（本書 3.6.3）に同じく当てはめる。証明できないことを偽とは述べず、原文にない構文（静的 Place の Origin 式など）を作らない。
4. **アクセスの種類が効果を決める。** 共有アクセスは書き込まない。書き込める別名と view は対象に不変で、それ以外は共変である。この一つの不変条件から、変性、ハンドルの解放、受け手の保存が導かれる（本書 3.4.1 規則 4、本書 3.5）。健全性の但し書き（内部可変性と並行性がないこと）は、SPEC §15.6.2 に一度だけ書く。
5. **局所の本文で意味が決まる。** 非修飾名は字句的な宣言だけを選ぶ（本書 3.3）。精密化の結果は、経路上の検査の集合だけで決まる（本書 3.4.2）。終了処理での Abort は、宣言だけで決まる（本書 3.6.2）。上流の追加や実行の履歴が、黙って意味を変えることはない。
6. **既存の決定を記録する。** Q18 と Q19（`obj` を除く）は、実装の現状を契約として書く。Q03 の enum 構築は、既存テストの動作を契約として書く。
7. **範囲と再表記。**
   - 機能は、そのソースが使うマイルストーンに属する（PLAN §5）。そのため、ObjectCallCompatible の第 2 段階は P38 の前に範囲へ入れる（本書 3.5.3）。逆に、どのプログラムも使わない要件の参照の実装は、P26 の完了条件に含めない（本書 7）。
   - マイルストーンのソースを書き換えるのは、仕様変更か、ソースが仕様から導いた自身の出力と矛盾する作成時の誤りのときだけとする（本書 3.6.1）。
   - プログラムと規範の例が同じ意図を示し、規則がそれを述べていないときは、仕様を直す（本書 3.1.1、P35:41 と SPEC §13.5.9）。

## 3. 決定案

### 3.1. 呼出し可能値のシグネチャーを通常の証拠にする：推論・関数参照・消去で一つのモデルを共有する（P26、P35）

対象のマイルストーンは二つある。

- P35：`makeRcCyclic(build)`（Milestone35.kimi:41）
- P26：残りの G10（関数参照の選択と消去の位置）

P26 の完了前に決める（G10 と G65 に隣接する）。Q01 は、P35 で Weak を束縛する（G4）前にも決まっている必要がある。

**原則。** 推論と関数参照の選択の証拠は、呼出しの期待に依存しないものに限る。

- **証拠になるもの：** 受け手と引数の型、明示型引数、無名関数の見出しに書いた型、公開された Constraint。局所束縛の型は、その宣言で固定される（SPEC §10.5）。
- **証拠にならないもの：** 呼出しの期待を待つ引数。無名関数の本体、入れ子の呼出し、`S`（下の表）を必要とする関数参照がこれに当たる。これらは期待を受けて検査されるが、外側のスロットを埋めない。
- **既存の境界。** この境界は、すでに SPEC に引かれている。
  - SPEC §10.8：「from the receiver and the independently typable arguments」（10:325）
  - SPEC §10.5：「their bodies and results never select a candidate」（10:271）
- 本テーマは、この境界を Callable 制約・関数参照・消去の三つの場面に同じ形で当てはめる。

**用語と関係。** 本テーマが使うのは次の四つだけである。「照合」と「適合」は既存の関係で、新しい関係は加えない。

| 用語・関係 | 定義 | 使う項目 |
| --- | --- | --- |
| 既知の呼出しシグネチャー | 値を直接呼び出すときのシグネチャー（SPEC §7.6.3、§8.6）。<br>• Function Item：束縛済みの引数を代入した宣言のシグネチャー<br>• 具体 Closure：内部呼出しシグネチャー<br>• 共通 Function Type：その構造<br>• 総称パラメーター `G`：`G` の Callable 事実が一つのシグネチャーを与えるときの、そのシグネチャー（SPEC §8.6 は複数の Callable 制約を許す、08:947）<br>無名関数の引数は、見出しに書いたパラメーター型と結果型だけを、対応する部分として与える。省略した部分は何も与えない | Q01、Q02 |
| 固定された期待呼出しシグネチャー `S` | 次のどちらか。<br>• SPEC §10.2 の位置の期待型が共通 Function Type なら、その型<br>• 期待型が `F`、`ref/F` または `uniq/F` で、`F` が外側の候補の未束縛スロットなら、`F` のすべての `Callable` 制約に共通する一つのシグネチャー。受け手の種類は問わない。シグネチャーが異なれば `S` はない<br>`S` の使い方は二つある。<br>• 証拠として使うとき：`S` の中のスロットを未知数として照合する（Q01）<br>• 期待として使うとき：外側の候補の未束縛スロットを含まない部分だけが導く（Q02、無名関数の省略パラメーター型） | Q01、Q02、SPEC §7.6.1、§10.5 |
| 照合 | 引数型をパラメーター型に照合する既存の規則（SPEC §10.2.1 step 1、§10.8）。<br>• 構造と Semantics は、正規化した同一性で決める<br>• Origin は、SPEC §15.3.6 の限定推論で決める<br>• 適応、部分型の探索、変換の探索はしない<br>• 呼出しごとに束縛される Origin（SPEC §3.2.1、§8.6）は、スロットの中の Origin の解にならない | Q01、Q02 |
| 適合 | SPEC §10.7 の callable 両立性。共通 Function Type への変換と `Callable` の検査は、すでにこの一つの規則を使っている（10:315） | Q02、Q03、step 5 の Callable 検査 |

三つの項目は、同じ照合と適合を次のように使う。

- **Q01：** 引数の既知の呼出しシグネチャーを引数の側に、パラメーターの `S` をパラメーターの側に置いて照合する。
- **Q02：** 位置の `S` を引数の側に、参照の候補のシグネチャーをパラメーターの側に置いて照合する。
- **Q03：** 期待型が共通 Function Type の位置で、適合した値を消去する。その操作を、SPEC §10.2 の表の一行にする。

SPEC §10.5 への取り込みでは、Q01 と Q02 を一つの段落として書く。Q01 は `F` から `S` への向き（引数のシグネチャーが `S` のスロットを埋める）を受け持つ。Q02 は `S` から参照への向きと、`S` の共通の定義を受け持つ。Q03 は、Q02 の `S` が使う SPEC §10.2 の位置の集合を与える。Q03 の SPEC §6.3.2 の文（期待する enum 型が payload の総称引数を決める）は、既存の CallbackEmissionTest の `FunctionItemPayload` に関わる。

#### 3.1.1. Q01. 引数の呼出しシグネチャーを、パラメーターの `S` に照合する（makeRcCyclic）

**現状と問題**

型引数の証拠には順序があり、Constraint は証拠の出所に入っていない。

- SPEC §10.1 は、まず「the receiver and the explicit arguments」から型引数を推論する（step 3、10:15）。
- 残りのスロットは、期待結果で埋める（step 4、10:16）。
- Constraint は、その後で検査する（step 5、10:17）。
- SPEC §10.8 も、構造の制約を受け手と「independently typable arguments」からだけ集める（10:325）。

そのため、次の形が問題になる。

- **`makeRcCyclic`。** `makeRcCyclic<T, F>(build: F) -> rc/T` は `F is Callable<owner, (Weak<rc/T>) -> T>` を要する（SPEC §13.5.8、13:651）。
  - `T` は `S` と結果にしか現れない。そのため、注釈のない `let node = Kimi.Intrinsics.makeRcCyclic(build)` では、`T` に証拠がない。
  - この形は、Milestone35.kimi:41 と SPEC §13.5.9 の例（13:710）にある。
  - 例は規則を足さない（SPEC §1.3、01-overview.md:78）。したがって、この例は現行規則の下では誤った例である。
- **決める手段のない形。** 結果に現れるスロットは、結果の注釈か、注釈した中間の束縛で決められる。Milestone26.kimi:48 の `let received: Packet = consume(take@move)` がその例である。しかし、`S` のパラメーターにだけ現れるスロットは、具体 Closure の引数では決める手段がない。理由は次の二つである。
  - Closure 型には綴りがない（SPEC §8.1.3、08:65）。明示型引数は全部を書く必要があるので（08:72、12-expressions.md:219）、型引数でも補えない。
  - 共通 Function Type に消去してから型引数を書くと、Exclusive と Consuming の callable を失う。
- **読み方が分かれる点。** SPEC §10.5 の Anonymous body context は「The written signature ... is used」と書く（10:269）。しかし、書いた見出しが外側のスロットの証拠になるかは書いていない。実装は、次の二つの条件がそろうときだけ、見出しを証拠にしている（src/Kimi/Compiler/Binding/Binding.Calls.cs:1103-1107）。
  - パラメーターが共通 Function Type であること
  - 見出しを全部書いていること

  また、実装では、Function Item の引数は共通 Function Type のパラメーターのスロットを埋めない。

**決定案**

1. **SPEC §10.1 step 3 と §10.8。** 受け手と引数の型の照合と同じ不動点で、次の照合も解く。パラメーターに `S` があれば、`S` を引数の既知の呼出しシグネチャーと照合する。
   - **共通 Function Type のパラメーター：** 照合の相手は、引数の既知の呼出しシグネチャーである。引数は、Function Item、具体 Closure、共通 Function 値、総称パラメーターのどれでもよい。無名関数なら、見出しに書いた部分が相手になる。
   - **`F is Callable<r, S>` のパラメーター（型が `F`、`ref/F` または `uniq/F`）：** 照合の相手は、`F` に束縛された型の既知の呼出しシグネチャーである。`F` を束縛するのは、明示型引数か、その引数自身である（SPEC §10.2.1 step 1）。無名関数の引数なら、見出しに書いた部分が相手になる。
   - **照合の対象：** 結果の形（値か Place）、パラメーター型、結果型の三つである。
   - step の番号は変えない。期待結果（step 4）は、従来どおり残りのスロットだけを埋める。
2. **一緒に解く。** これらの照合は、引数型の照合と同じく一緒に解く（SPEC §10.2.1 step 1 の最後の文）。
   - 照合の不一致と、同じスロットへの矛盾する証拠は、どちらも SPEC §10.8 の不一致である。その候補は適用不能になる。
   - どの引数も、走査順で先にスロットを決めない。
3. **Origin。** 呼出しごとの Origin は、スロットの中の Origin の解にならない。その部分は、ほかの証拠か SPEC §15.3.6 に任せる。
4. **待つ引数は証拠にならない。** 原則のとおりである。
   - 期待を待っていた無名関数の本体を検査した後で、`F` が具体 Closure に決まることがある。その場合も、その結果型は外側のスロットを埋めない。
   - 本体から結果を推論する二つの記述がある。SPEC §10.5 の「including for return inference」（10:273）と、SPEC §7.6.1 の本体からの結果の推論（07:463）である。どちらも無名関数自身の結果の推論であり、外側のスロットの証拠ではない。このことを両方の節に明記する。
5. **検査は変えない。**
   - **Constraint の検査：** `F is Callable<r, S>` 自体（`r` を含む）と、ほかの Constraint は、従来どおり step 5 で検査する。`T is Owned` と `ObjectPayload` も同じである。
   - **メンバーは探さない：** この照合は、呼出し地点での型引数の推論である。callable 固有の一つのシグネチャーを読むだけで、メンバーは探さない（SPEC §8.6「no user `call` member is searched」、08:922）。SPEC §8.7 の証明系は変わらない。
   - **消去の時点：** 消去は、パラメーターの共通 Function Type が決まった後で計画する。SPEC §10.7 の「Implicit erasure applies only after the expected common Function Type is fixed」（10:319）は、そのまま成り立つ。
6. **診断。** 必要なスロットが解けず、それが待つ引数の `S` に現れるときは、SPEC §10.6 の推論境界の診断に Advice を付ける。
   - 直し方は二つ示す。
     - その引数の型を書くこと。無名関数ならパラメーター型か結果型、関数参照なら明示型引数である。
     - 結果の型を注釈すること。`makeRcCyclic` なら `: rc/<payload>` である。
   - 修復候補は出さない。必要な型は、本体を検査するまで分からないからである。

| step | 証拠 | 区分 |
| --- | --- | --- |
| 1 | 明示型引数 | 既存 |
| 3 | 受け手と引数の型、および各パラメーターの `S` と引数の既知の呼出しシグネチャーの照合（一つの不動点） | 照合を追加 |
| 4 | 独立に分かる期待結果 | 既存 |
| 最後 | リテラルの当てはめと既定値 | 既存（10:99） |
| — | 待つ引数（無名関数の本体、入れ子の呼出し、`S` を必要とする関数参照） | 証拠にならない（明文化） |

step 3 で `S` のパラメーター型が閉じることがある。そのときは、その `S` が、同じ呼出しのほかの無名関数を SPEC §10.5 のとおり導く。

```kimi
func consume<T, F>(action: F) -> T
    F is Callable<owner, () -> T>
    return action@move()

func both<T, F, G>(first: ref/F, second: ref/G) -> T
    F is Callable<() -> T>
    G is Callable<() -> T>
    return first()

func make<T>(action: () -> T) -> T => action()
func makeSmall() -> i32 => 1
func makeLarge() -> i64 => 2

// Packet is that of Milestone26.kimi; Node and build are those of Milestone35.kimi.
let packet = Packet.init("Packet.", 9)
let take = func [packet@move] () -> Packet => packet@move
_ = consume(take@move)                         // T = Packet from take's signature.
let small = make(makeSmall)                    // T = i32 from makeSmall's signature; then erased.

let node = Kimi.Intrinsics.makeRcCyclic(build) // T = Node from build's signature.
let written = Kimi.Intrinsics.makeRcCyclic(func [] (weak: Weak<rc/Node>) => Node.init(weak@move, 3))
                                               // T = Node from the written parameter Type.
let guided: rc/Node = Kimi.Intrinsics.makeRcCyclic(func [] (weak) => Node.init(weak@move, 3))
                                               // The expected result fixes T; S then types weak.

// let open = Kimi.Intrinsics.makeRcCyclic(func [] (weak) => Node.init(weak@move, 3))
// Error: only the body could fix T. Advice: write weak: Weak<rc/Node>, or annotate open: rc/Node.
// let mixed = both(makeSmall, makeLarge)
// Error: T has conflicting evidence, i32 and i64; neither argument fixes T first.
```

**最小で一貫している理由**

- **Principle 1：**
  - 次の四つの場面が、同じ「既知の呼出しシグネチャー」を使う：値の直接呼出し、Callable の適合検査、消去、推論。
  - Callable のパラメーターと共通 Function Type のパラメーターが、同じ照合を使う。そのため、無名関数の見出しだけの特例はなくなる。
  - Closure 型の綴りや部分的な型引数のような、新しい構文は要らない。
- **Principle 2：**
  - 証拠になるのは、その宣言で固定済みの引数の型と、呼び出す関数の公開された Constraint だけである。本体は読まない。
  - すべての照合を一つの不動点で解くので、順序で結果が変わらない。
  - 待つ引数を証拠にしないので、候補の数で推論が変わらない。たとえば、別の多重定義を足しても推論は変わらない。
- **Principle 3：** Constraint は従来どおり検査する。変換も所有権の緩和も加えない。

**採らなかった案**

| 案 | 採らない理由 |
| --- | --- |
| Callable を証拠にしない。代わりに P35:41 と SPEC §13.5.9 の例を `let node: rc/Node = …` に書き換える（本書 7 の 1 の案 B） | `S` のパラメーターにだけ現れるスロットが、具体 Closure では決められない。書いた見出しの読み分けも残る |
| 一般の Constraint 駆動推論 | 型の探索になり、SPEC §8.7 の限定証明系に反する |
| `makeRcCyclic` のパラメーターを共通 Function Type にする | Exclusive と Consuming の builder を失い、消去を加えることになる。SPEC §13.5.8 の「`F` itself need not be Copy or Owned」に反する |
| 見出しを全部書いた無名関数だけを証拠にする（現在の実装） | 全部か無しかの条件になる。書いた型は、すでに候補を絞っている（10:269） |
| 待つ引数の結果も証拠にする（無名関数の本体の結果型、`S` で選んだ参照のシグネチャー） | 候補が一つのときにしか働かず、多重定義を足すと推論が変わる。期待と証拠が循環する。参照は期待結果（step 4）の後でしか選べないことがあるので、段の順序も決まらない |
| Callable の照合を、step 3 の後の別の段にする | 段が一つ増える。引数型の照合と同じ「一緒に解く」規則を、二度書くことになる |

**変更する節**

- SPEC §10.1 step 3：決定案 1。
- SPEC §10.8 第 1 段落：決定案 1–4。
- SPEC §10.5 の Anonymous body context：次の二つを書く。
  - 無名関数の見出しに書いた部分は、既知の呼出しシグネチャーの部分として証拠になる。
  - 10:273 の結果の推論は、無名関数自身のものである。
- SPEC §7.6.1：07:463 に、決定案 4 の一文を加える。
- docs/dev/DIAGNOSTICS.md：決定案 6 の Advice。

次は変えない。

- SPEC §13.5.8 と §13.5.9 の推論の記述
- LIBRARY.md 5.3

**マイルストーンの書き換え**

書き換えはない。Milestone35.kimi:41、Milestone26.kimi:48、SPEC §13.5.9 の例は、書いたまま有効になる。

別件として、SPEC §13.5.9 の例には本テーマと関係のない誤りが二つある。どちらも Non-Copy の `Weak` を、裸の Place のまま値で渡している（SPEC §3.5、10:76）。同じコミットで直す。

- 13:709：`Node.init(weak)` → `Node.init(weak@move)`
- 13:705：`self.selfWeak = selfWeak` → `self.selfWeak = selfWeak@move`

**実装とテスト**

照合は、Binding.Calls.cs の引数の推論（1070–1115 行付近）に加える。置く位置は、期待結果の段（1134 行付近）の前である。1103–1107 行の特例は、この一般の照合に置き換える。

既知の呼出しシグネチャーには、既存の二つの関数を使う。

- `TryCallable`（Binding.ValueCalls.cs:98）：Function Item（G10）と、一つのシグネチャーを持つ総称パラメーターにも広げる。
- `ClosureHeaderType`（Binding.Closures.cs:65）：書いた部分だけを返すように広げる。

焦点テストは次のとおり。

- 有効になる形
  - `let node = makeRcCyclic(build)`
  - `_ = consume(take@move)`
  - パラメーター型だけを書いた無名関数の builder
  - `make(makeSmall)`
  - 総称の本体で、`G is Callable<owner, () -> U>` を `consume` に渡し、`T = U` になる形
- 誤りになる形
  - 型を省略した builder と、その Advice
  - 二つの Callable の矛盾。引数の順を入れ替えても、同じ診断になる
  - 呼出しごとの入力を借用する結果のために、スロットが解けない形

#### 3.1.2. Q02. 関数参照の選択：固定された期待呼出しシグネチャーによる選択、自分のスロットの束縛、要件の参照（G10）

**現状と問題**

SPEC は、関数参照も通常の多重定義解決を使うとしている。

- SPEC §10.5（10:267）：関数参照は「resolved with ordinary evidence, including explicit generics or a fixed expected callable signature」である。一意の宣言は期待型なしでよく、未解決の多重定義の集合は値ではない。
- SPEC §8.8.3（08:1084）と §8.4.8.2（08:532）も同じ立場である。

しかし手順が書かれていないので、次の点で読み方が分かれる。

- **候補の比べ方。** 参照には引数がないので、SPEC §10.4 step 1 では比べるものがない。SPEC §7.3.1 は、`f(value: Node)` と `f<T>(value: T)` の組を認めている（07:363-364）。この二つが同じ期待で両方とも適合するとき、どちらになるかが決まらない。
  - step 3 の「非総称を優先」で選ぶ。
  - 曖昧とする。
- **自分のスロット。** 総称の宣言の自分のスロットを、期待シグネチャーからどう束縛するかが書かれていない。
- **`S` の定義。**
  - 「fixed expected callable signature」という語は SPEC §7.6.1（07:463）にも出てくるが、定義がない。
  - 無名関数については、`Callable<r, S>` が導くと書かれている（10:269）。参照には、同じ文がない。
  - そのため、型が `F` のパラメーターに多重定義名を渡す形（`apply(show)`）は循環する。`F` は、引数の型からしか決まらないからである。
- **要件の参照。** 制約された型を通じて要件を参照する形（`T.compare`）がある。その同一性が、要件か実装かが書かれていない。

実装（src/Kimi/Compiler/Binding/Binding.FunctionTypes.cs:112-155）は、次のように動く。PLAN の G10 は、これを P26 の残りの作業としている。

- 総称・制約付き・受け手付きの候補は、Unsupported にする。
- 二つ目の候補が適合すると、Ambiguous にする。
- unsafe の候補は、曖昧かどうかを調べる前に拒否する。

**決定案**

1. **`S` の定義。** 本書 3.1 の表の定義を、SPEC §10.5 に一度だけ置く。SPEC §7.6.1 と §7.6.4 は、それを参照する。
   - 外側の候補が複数残っているときは、SPEC §10.5 の入れ子の呼出しの規則に従う。すべての候補に共通する `S` だけを使う。
   - 参照が `S` を必要とするのは、次のどちらかのときである。
     - 候補が二つ以上残っている。
     - 自分のスロットが明示されていない候補がある。
   - `S` を必要としない参照は、独立に型が決まる引数であり、Q01 の照合の証拠になる。
2. **候補。** コミットした関数グループのうち、SPEC §10.1 step 1 の明示型引数の検査を通ったものを候補とする。
   - 型修飾のインスタンス関数参照は、非束縛の並び（SPEC §7.3、07:272）として扱う。
   - そのとき `self` は、宣言の位置にある通常のパラメーターになる。
3. **`S` を必要とし、`S` のパラメーター型がすべて閉じているとき。** `S` の結果型は、開いたままでよい。次の手順で選ぶ。
   - **スロットの束縛。** 各候補の自分のスロットを、`S` に対する照合で束縛する。
     - 照合するのは、候補のパラメーター型と、結果型である。結果型は、`S` の閉じた部分とだけ照合する。
     - SPEC §10.2 の適応は使わない。
   - **適用可能性。** 次の三つをすべて満たす候補が、適用可能である。
     - 自分のスロットが、すべて束縛された。
     - Constraint と条件付きメンバーの前提が、Proven である（SPEC §10.1 step 5、08:532）。
     - 代入後のシグネチャーが、`S` の閉じた部分と SPEC §10.7 で両立する。引数の数の一致も、この条件に含まれる。
   - **比較。** SPEC §10.4 を、そのまま使う。参照には実引数も既定値の使用もないので、step 1 と step 4 はつねに等しい。結果で順位は付けない。
4. **`S` がないとき。** 参照が値になるのは、次の二つを満たすときだけである。
   - 候補が一つだけ残っている。
   - その候補に自分のスロットがないか、すべて明示されている。
5. **選択の後。**
   - **後で検査する条件。** 次の二つは、選択の後で検査する。
     - unsafe と `drop` の禁止（SPEC §7.5、10:267）
     - 消去の条件（SPEC §7.6.4）

     どちらかが成り立たなければ誤りとし、別の候補を選び直さない（07:443）。
   - **`F` の束縛。** 期待型が `F`、`ref/F` または `uniq/F` なら、`F` を選んだ Function Item の型に束縛する。消去はしない。参照は一時値なので、`ref/F` では一時値を実体化して共有借用する（SPEC §10.2）。
   - **待つ引数。** `S` を必要とした参照は、待つ引数である。選んだ Item のシグネチャーは検査に使う。しかし、`S` の開いた結果を埋める証拠にはならない（原則）。
6. **要件の参照。** 制約された型を通じた要件の参照は、要件の宣言を `Self` の束縛で具体化した Function Item である。たとえば、`T is Comparable` の下の `T.compare` がそうである。
   - **シグネチャー：** `Self` と関連型を代入した、要件のシグネチャーである。
   - **呼出し：** 適合の対応を通って、実装に届く（SPEC §8.4.6、IMPL §21.2.5.1）。
   - **具体化：** 具体化しても、実装自身の Function Item には置き換えない（SPEC §10.2.1「instantiation neither reselects candidates」）。
   - **誤りになる形：** `Comparable.compare` は実装を識別しないので、参照としても誤りである（08:376）。
7. **診断。** どちらの場合も、修復候補は出さない。
   - **曖昧なとき：** 適合した候補を、代入後のシグネチャーと宣言の位置とともに示す（SPEC §10.6）。Advice では、明示型引数か型注釈を勧める。
   - **呼出しごとの Origin のためにスロットが解けないとき：** そのスロットと、`S` のパラメーターを示す。Advice では、無名関数で包む形を示す。

```kimi
func show(value: i32) -> () => ()
func show<T>(value: T) -> () => ()      // The pair SPEC §7.3.1 allows.
func identity<T>(value: T) -> T => value@move
func inspect<T>(value: ref/T) -> () => ()

func apply<F>(action: ref/F) -> ()
    F is Callable<(i32) -> ()>
    action(1)

func sortAll<T>(values: uniq/Array<T>) -> ()
    T is Comparable
    values.sort(by: T.compare)          // The Item of Comparable.compare with Self = T.

let a: (i32) -> () = show               // show(i32): §10.4 step 3 prefers the non-generic function.
apply(show)                             // The same; F is its Function Item Type, and the temporary is borrowed.
let b: (i32) -> i32 = identity          // T = i32 from S; then erased (Q03).
let c: (ref/Node) -> () = inspect       // T = Node; the per-call Origin stays outside T.
let d = show<i32>                       // No S: one candidate remains after the explicit arguments.
// let e = show                         // Error: no S and two candidates.
// let f = identity                     // Error: no S and T is unbound.
// let g: (ref/Node) -> ref/Node = identity
// Error: T would hold a per-call Origin. Advice: func (node) => identity(node).
```

**最小で一貫している理由**

- **Principle 1：**
  - 呼出しと参照は、次の四つを共有する：候補の集合、明示型引数の検査、Constraint の検査、SPEC §10.4 の比較。
  - 参照と無名関数は、一つの `S` を使う。
  - 共通 Function Type への変換と Callable は、SPEC §10.7 の一つの適合を使う。
- **Principle 2：**
  - 選択は、位置が宣言した型と候補のシグネチャーだけで決まる。
  - 選んだ Item は、具体化しても変わらない。
  - 待つ参照は外側の推論に戻らないので、順序の問題が生じない。
- **Principle 3：**
  - 参照は値を取るだけで、適応を隠さない。消去は、Q03 の一つの行として別に現れる。
  - 呼出しごとの Origin の規則が、書けない higher-rank な具体化を防ぐ（SPEC §8.6「no general higher-ranked Origin binder」、08:924）。

**採らなかった案**

| 案 | 採らない理由 |
| --- | --- |
| `S` の型の一時値を引数にした呼出しとして解決する | SPEC §10.2 の適応（排他 Reborrow、値読み取り、Copy 証明）と、位置引数の `!` 制限まで持ち込む。結果に §10.3 の適応がかかり、SPEC §10.7 の適合と食い違う |
| ちょうど一つの候補が適合するときだけ値にする | SPEC §7.3.1 が認める組を、参照では使えなくなる。Callable の導き方も、無名関数と参照とで違ってしまう |
| 要件の参照を、具体化で実装の Item に変える | 具体化で型が変わり、SPEC §10.2.1「instantiation neither reselects candidates」に反する。実装のシグネチャーは、結果の部分型や強い Origin で要件と違い得る（SPEC §8.4.5）。組み込みの適合には、実装の宣言がない |

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §10.5 | Function references の段落を、決定案 1–6 で置き換える。Anonymous body context の「`Callable<r, S>` may guide the parameters」を、「固定された期待呼出しシグネチャーが、省略したパラメーター型を導く」にする |
| SPEC §7.6.1 | 07:463 から、§10.5 の定義を参照する |
| SPEC §7.6.4 | 07:572 の「A resolved function reference」から、§10.5 を参照する |
| SPEC §8.4.6 | 決定案 6 を一文で加える |
| docs/dev/DIAGNOSTICS.md | 決定案 7 |

次は変えない。

- SPEC §3.2.1：要件の宣言と `Self` の束縛は、すでに「One resolved function declaration and instantiation」に当たる。
- IMPL：IMPL §21.2.5.1 は、すでに「Function Item identity, bound arguments and the selected implementation are static information」としている。
- 付録 D の「qualified requirement calls」：`value@A.reset()` の形の別の機能であり、関係しない。

**マイルストーンの書き換え**

書き換えはない。Milestone26.kimi:58–60 の `increment` は、一意の非総称の宣言である。決定案 4 で値になり、Q01 の証拠にもなる。

**実装とテスト**

BindFunctionReference を、次のように直す。

- 総称と受け手付きの候補も扱う。
- 選択は SPEC §10.4 で行う。
- unsafe と `drop` は、選択の後で検査する。

ほかの変更は次のとおり。

- 適合の判定には、FunctionReferenceFits（Binding.FunctionTypes.cs:179-240）を残す。
- Binding.Calls.cs:1262-1273 の関数グループの引数は、`F` の Callable の `S` でも選ぶようにする。
- CODEMAP に、関数参照の選択の行を加える。

焦点テストは次のとおり。

- 有効になる形
  - 上の例の a〜d
  - `sortAll` の `T.compare`
- 誤りになる形
  - 上の例の e〜g
  - unsafe の候補が選ばれる形。誤りになり、別の候補は選ばない
  - `map(1@i32, identity)` の形。`S` の開いた結果が埋まらず、Advice で `identity<i32>` を示す

#### 3.1.3. Q03. 共通 Function Type への消去を、SPEC §10.2 の表の一行にする

**現状と問題**

消去を行う位置の一覧が、節によって違う。

- **SPEC §7.6.4（07:580）：** 変換を「At an initialization, argument or return position」に限る。
- **SPEC §10.2（10:43）：** 期待型が固定される位置は、初期化・引数・結果だけではない。次の位置も含む。
  - 代入の源、集約の要素、enum の payload、既定値
  - 固定された Target Result Type の源（arm、`yield`、`exit`、単一項目の本体）
- **ほかの節：**
  - SPEC §3.8（03:788）は、表の操作と「the fixed-expectation common function conversion」を並べている。
  - SPEC §10.2.1（10:166）は、この変換を「handled separately and adds no rank」とする。
  - SPEC §10.9（10:338）は、表を超える暗黙の引数適応を定めないとする。

そのため、次の形で読み方が分かれる。

- **初期化か代入か。** `let handler: (i32) -> i32` と宣言し、後で `handler = inc` と書く形がある。この代入は SPEC §15.1.5 の Initialize なので、「initialization」とも読める。そう読むと、同じ字面でも、変換するかどうかが流れの状態で変わる。
- **その他の位置。** 次の位置で消去するかが決まらない。
  - `[inc, dec]`
  - `.Some(inc)`
  - `if ready => inc else => dec`
  - `yield`、`exit`、既定値

  SPEC §14.9.1 step 4 の「Check every source for fitting」（14:722）に消去が含まれるかも、読み分かれる。
- **修飾形の enum 構築。** 期待型 `Option<(i32) -> i32>` の位置に、修飾形 `Option.Some(inc)` を置く場合である。
  - SPEC §6.3.2（06:339）は「Generic arguments must be explicit or uniquely inferred」とだけ言う。
  - 呼出しと同じ順で推論すれば、`T` は Item 型に決まり、期待型と合わない。

実装は、次のようになっている。

- **代入の源と、配列・Tuple の要素：** ここでも変換する（src/Kimi/Compiler/Binding/Binding.Expressions.cs:124-144）。そこのコメントは、存在しない「SPEC 7.7」を挙げている。
- **enum の構築：** 期待する enum 型を、payload より先に使う（Binding.Enums.cs:264-285）。tests/xUnitTest/Tests/CallbackEmissionTest.cs:69 の `FunctionItemPayload` は、この動作に依存している。

**決定案**

1. **表の一行。** SPEC §10.2 の表で、最後の行（「Any other value or Place」）の前に、次の行を置く。

   | Input | Expected | Operation |
   | --- | --- | --- |
   | Function Item または具体 Closure（値または Place） | 共通 Function Type `U` | 消去（SPEC §7.6.4）。最後の行と同じく源を取得する（裸の取得、明示の転送、または一時値の転送）。そのあと、新しい `U` に所有させる |

   - **条件：** 消去できる条件は、SPEC §7.6.4 のまま変えない。
   - **無名関数：** 無名関数の式は具体 Closure の一時値なので、同じ行を通る。IMPL §21.2.5.3 は、それをその場で構築する。
   - **マイルストーンの形：** Milestone26.kimi の二つの形は、どちらもこの一行で扱われる。
     - :55 の `concrete@move`：明示の転送
     - :59 の `item`：Copy の裸の取得
   - **働く位置：** この行が働くのは、SPEC §10.2 が期待型を固定した位置だけである。SPEC §14.9.1 step 2 の共通型の探索（14:720）は、源自身の型を比べるだけである。この行によって `U` を選ぶことはない。
2. **帰結。** 表の一行なので、次のことは新しい規則なしに決まる。
   - **位置。** 期待型が固定されるすべての位置で働く。初期化、代入の源、引数、結果、集約の要素、enum の payload、既定値、arm・`yield`・`exit`・単一項目の本体である。
     - SPEC §3.5（03:551）、§10.3（10:189）、§15.1.5（15:148）は、すでにこの表を参照している。
     - 行き先が未初期化かどうかは、関係しない。
   - **連鎖しない（10:58）。** 次の三つは誤りである。
     - 借用した Closure `ref/C` を消去する形（03:209 と同じ）
     - `ref/((i32) -> i32)` のパラメーターに `inc` を渡す形。消去と借用の二つの操作になる。
     - 構造の中で消去する形。たとえば `Option<Item>` を `Option<(i32) -> i32>` にする。
   - **埋める要素。** `[2 of inc]` を `[2 of (i32) -> i32]` に置く形は誤りである。埋める要素は Copy でなければならないが、消去した値は Non-Copy だからである（SPEC §4.3、04:72）。
3. **順位。** SPEC §10.2.1 の 10:166 の最後の文を置き換える。消去の行は、SPEC §10.2.1 のどのクラスにも入らない。SPEC §10.4 step 1 では、次のように比べる。
   - 同じ引数での二つの消去は、等しい。
   - 消去とほかの行は、比較できない（incomparable）。

   これは、SPEC §10.7（10:319）の「No overload ordering is defined」をそのまま書いたものである。10:319 の第 2 文は、この文を参照するように直す。付録 D の「Direct-match versus callable-erasure overload ranking」は、保留のまま変わらない。比較できないとしておけば、将来優先順位を決めても、誤りだった呼出しが有効になるだけである。
4. **enum の構築。** 構築の期待型が同じ enum の所有型であれば、その型が Case 構築の総称引数を与える。
   - 総称引数は、payload を検査する前に決まる。
   - これは修飾形にも leading-dot 形にも同じに当てはまる。leading-dot 形は、すでにそうなっている（06:351）。
   - payload は、その後で SPEC §10.2 によって適応する。消去も、この適応に含まれる。
   - 明示した総称引数が期待型と違えば、誤りである。
5. **SPEC §7.6.4 の書き方。**
   - 「At an initialization, argument or return position whose expected Type is a fixed common Function Type」を、「At a position whose fixed expected Type (§10.2) is a common Function Type」に変える。
   - そのうえで、変換は §10.2 の消去の行で行う、と書く。
   - 条件の三項目は変えない。

```kimi
func inc(value: i32) -> i32 => value + 1
func dec(value: i32) -> i32 => value - 1
func twice(action: ref/((i32) -> i32), value: i32) -> i32 => action(action(value))

var handler: (i32) -> i32 = inc
handler = dec                                         // Assignment source: the old value is destroyed.
let table: [2 of (i32) -> i32] = [inc, dec]           // Aggregate elements.
let some: Option<(i32) -> i32> = .Some(inc)           // Payload.
let same: Option<(i32) -> i32> = Option.Some(inc)     // The expected enum fixes T first.
let chosen: (i32) -> i32 = if ready => inc else => dec // Each arm is erased.
// let mixed = if ready => handler@move else => inc   // Error: the search forms no common Function Type.
// let filled: [2 of (i32) -> i32] = [2 of inc]       // Error: a fill element must be Copy.
// let result = twice(inc, 1)                         // Error: erasure and a borrow are two operations.
```

**最小で一貫している理由**

- **Principle 1：** 暗黙の適応は、一つの表と一つの位置の集合で決まる。SPEC §7.6.4 だけが持っていた位置の一覧は、なくなる。
- **Principle 2：**
  - 位置での操作は、入力の型と固定された期待型だけで決まる。初期化か代入かといった流れの状態では変わらない。
  - enum の構築は、修飾の有無にかかわらず同じ意味になる。
- **Principle 3：** 消去は一つの見える操作のままで、ほかの操作と連ねない。構造の中での暗黙の消去も加えない。

**採らなかった案**

| 案 | 採らない理由 |
| --- | --- |
| 三つの位置を保ち、それ以外では型付きの中間を書かせる | 暗黙の適応の位置の一覧が二つになる。「initialization」の意味が、流れの状態に依存する。`[inc, dec]` や `.Some(inc)` に一時変数が要る。実装は、すでに対応した位置を取り下げることになる |
| 消去を Exact のクラスにする | 付録 D で保留している「直接一致と消去の順位」を、暗黙に決めてしまう |
| 修飾形の enum 構築を、呼出しと同じ順（payload が先）で推論する | 同じ期待型の下で、修飾形と leading-dot 形の意味が分かれる。enum を名指すだけの修飾子が、payload の適応を変えてしまう |

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §10.2 | 決定案 1 の行と、共通型の探索についての一文を加える |
| SPEC §10.2.1 | 10:166 の最後の文を、決定案 3 に置き換える |
| SPEC §10.7 | 10:319 の第 2 文から、§10.2.1 を参照する |
| SPEC §3.8 | 03:788 の「or the fixed-expectation common function conversion」を、「including its erasure row」にする |
| SPEC §7.6.4 | 07:580 を決定案 5 のとおりに直し、上の例を加える |
| SPEC §6.3.2 | 06:339 に、決定案 4 を加える |

次は変えない。

- SPEC §10.9：消去が表の行になるので、10:338 との食い違いはなくなる。
- IMPL：IMPL §21.2.5.3 は、位置に依存しない。

**マイルストーンの書き換え**

書き換えはない。Milestone26.kimi:55 と :59 は、注釈付きの初期化である。どちらの読み方でも有効である。既存テスト CallbackEmissionTest の `FunctionItemPayload`（`Option.Some(inc)`）は、決定案 4 の下で有効のまま残る。

**実装とテスト**

実装の変更は次のとおり。

- **IsAcquisitionPosition。** 一覧を、「期待型が固定され、callee ではない位置」という判定に置き換える。arm、`yield`、`exit`、既定値は、まだ扱えない。扱えるようになるまで、その不足を STATUS と PLAN（G10）に記録する。
- **消去の順位。** Binding.Calls.cs は、消去を Exact として記録している（1311-1316 行と、関数グループの 1262-1273 行）。これを、決定案 3 の扱いに変える。
- **コメント。** 「SPEC 7.7」を挙げる 3 か所（Binding.Expressions.cs:124、:166、:1049）を、§7.6.4 と §7.5 に直す。

焦点テストは次のとおり。

- 有効になる形
  - 代入の源
  - 集約の要素
  - 修飾形と leading-dot 形の payload
  - arm、`yield`、`exit`、既定値
- 誤りになる形
  - 上の例の三つ
  - `ref/C` の消去
  - 直接一致と消去の二つの候補が残る呼出し（曖昧）

### 3.2. Origin 関係を独立した要件として報告する（P26、P34、P36、P38）

P26・P34・P36・P38 の拒否検査には、構造は合うが Origin だけが合わない場合が多く含まれる。本テーマでは、この失敗を「Origin 関係」という一つの要件として判定し、報告する規則を定める。考え方は一つである。一つの要件と分類には一つのコードを与える（SPEC §23.3.6.1）。記録は確立した事実だけを述べ、原文にない構文を作らない。

この規則は診断の基盤なので、Origin だけの適合か、非 static の Origin を名指す Owned の失敗を報告する、最初の必須の拒否検査より前に要る。該当する検査は次の四つである。

- P26 の外部依存の脱出
- P34 の、局所変数を借用する payload を持つハンドルの脱出
- P36 の、可変静的記憶域の `during static` の借用（SPEC §11.3.2）
- P38 の、payload が局所変数を借用する静的 Weak

本テーマは、本書 3.4（精密化の対象の Owned の失敗）、3.5（変性の合流での Origin の失敗）、3.6 と関わる。本書 3.6.2（Q22）が終了時の依存の拒否を削るので、P36 の静的な拒否は SPEC §11.3.2 の検査だけになる。本テーマは、その失敗を、静的 Place に Origin 式を作らずに表示する。本書 3.6.3（Q23）は、同じ「一つの要件と分類に一つのコード」を実行時の Abort に当てはめる。

#### 3.2.1. Q26. Origin だけが合わない適合を、独立した要件「Origin 関係」として判定し報告する（G56）

**現状と問題**

1. **必須の拒否検査がこの場合に当たる。** PLAN §5 条件 2（docs/dev/PLAN.md:140）は、拒否診断がそろうことを完了条件にしている。
   - P36「可変静的記憶域の `during static` の借用」（tests/milestones/README.md:1865）。SPEC §11.3.2（docs/spec/11-properties.md:358）は、この借用が "has a finite, use-bounded Origin and cannot be fitted to `static`" と定める。
   - P34「局所変数を借用する payload を持つハンドルの脱出」（README:1794）と P38「payload が局所変数を借用する静的 Weak」（README:1950–1951）。関数の結果や静的記憶域へ出すと、省略された slot は `static` になる（SPEC §15.4.3 規則 3、§15.4.2 の Static storage 行）。局所の借用の Origin はこれを満たさない。
   - P26「外部依存の脱出」（README:1463）。Function Type への消去なら Owned の失敗である（SPEC §7.6.4、§15.2.3）。Closure を外側の局所変数へ持ち出すなら Loan の衝突である。
2. **どの要件の失敗かが決まっていない。** SPEC §3.8 の表（docs/spec/03-types-and-values.md:784）は、Origin の短縮を "A subtype proof, not a new borrow" とする。これは適合と同じ要件とも読める。一方、SPEC §15.6.1（docs/spec/15-ownership-and-lifetime-analysis.md:768–774）は、Subtyping と Outlives を別の制約として並べる。SPEC §23.3.6.1（docs/spec/23-compiler-services.md:138）は "a different requirement or category is a different code" と定めるので、どちらに読むかでコードが変わる。
3. **証明できない場合と偽の場合を区別しなければならない。** SPEC §15.3.6（15:582）は "An unknown proof remains distinct from a contradiction" と定め、SPEC §23.3.6.1（23:140）は "A `Proof` failure is never described as a false condition" と定める。
   - G56 の再現（tests/xUnitTest/Tests/BorrowedSlotReferenceTest.cs:44–46）は、互いに関係のない二つの universal Origin の関係である。偽ではなく、証明できないだけである。
   - SPEC §15.6.5 の例（15:866–868）と §11.3.2 は偽、つまり反証できる場合である。
   - 両方を Language の `TypeMismatch_Kd` で報告すると §23.3.6.1 に反する。
4. **表示の形がない。** SPEC §23.3.6.5（23:188）は "A mismatch keeps the differing parts and elides the common ones." と定める。
   - 局所の借用や一時値の Origin には Origin 式がない（SPEC §15.2.1、15:298 "Storage Origins of owned locals likewise remain compiler-internal"）。静的 Place を名指す Origin 式は定義されていない（SPEC §15.9、15:1071）。
   - そのため実装は `expected ref/ref/i32, found ref/ref/i32` を出している（docs/dev/PLAN.md:175 の G56）。docs/dev/DIAGNOSTICS.md:300 も、同じ種類の表示 `expected ref/string, found ref/string` を記録している。
   - 関連位置だけで示すこともできない。関連位置は上限で省かれる（23:189）ので、SPEC §23.3.6.2（23:158）"An essential fact is never placed only in an omissible supplement" に反する。
5. **反証される Origin の範囲が曖昧である。** SPEC §15.6（15:747）には "Local regions are inferred" とあり、§15.6.5（15:863）には "a local region cannot be widened to satisfy a universal return Origin" とある。推論されるすべての region を広げられないと読めば、正しい `let r = x` の後の `return r` まで拒否される。また、§15.2.3（15:331）の "finite Origin" は可変静的記憶域にしか使われていない。§11.3.2 は、この Origin を "only through a result Origin bounded by a borrowed input" で外へ出すことを許し（11:358）、それ以外の経路を許さない。これを判定の規則として書いた節はない。
6. **適用可能性にかかわるかどうかが曖昧である。** SPEC §10.2（docs/spec/10-overload-resolution-and-inference.md:58）は "Exactly one table operation, plus ordinary Origin fitting, is selected" と定め、§10.3（10:172）は "Origins are instantiated and checked" と定める。Origin の失敗で候補が落ちるなら、候補が一つだけの場合は `NoApplicableOverload_Kd` になり、同じ規則が二つのコードに分かれる。一方、SPEC §9.1（docs/spec/09-names-signatures-and-access.md:54）は Signature の比較から Origin と関係を除いている。SPEC §8.8.1（docs/spec/08-generics-constraints-and-contracts.md:1041）は Origin の関係で曖昧さを解決しない。
7. **同じ失敗を二重に報告するおそれがある。** 同じ脱出が、制御フローの `IncompatibleResult_Kd`（G56 の前半。src/Kimi/Compiler/Analysis/ControlFlowAnalysis.cs:1725 は `BoundType.Name` で表示する）や、破棄時の Loan 衝突としても現れうる。DIAGNOSTICS §2 規則 1（docs/dev/DIAGNOSTICS.md:20）は、一つの要件を一つの段階で判定するよう求めている。
8. **実装は Origin にかかわる失敗を三つのコードに分けている。**
   - `Binding.TypeRelations.cs:174–224`（`CheckTypeUse`）は、Origin だけの違いを義務に回す。しかし `uniq`・`objuniq`・`raw` の成分では false を返すので、`TypeMismatch_Kd`（Language）になる。
   - 残った義務は、`OwnershipAnalysis.cs:96–99` で最初の一件だけが `OwnershipFailure.UnprovenOrigin` になる。これは `OwnershipModel.cs:227` で `UnprovenConstraint_Kd` に写される。その文言 "The required Constraint cannot be proven by its semantic deadline" は Constraint の失敗として述べている。
   - Owned でない環境を持つ Closure を共通 Function Type へ変換する失敗は、`TypeMismatch_Kd` に Note "Common Function conversion requires an Owned environment" を付けて報告している（src/Kimi/Compiler/Binding/Binding.cs:672–678、Binding.Diagnostics.cs:70–85）。Owned は Constraint である（SPEC §7.6.4、§15.2.3）のに、Type 不一致のコードになっている。
   - SETTLED.md の項目「Signature Origin names are introduced implicitly」は、その理由を "diagnostics that explain the introduced Origin and suggest similar names" に置いている。しかし、その説明の規則がまだない。

**決定案**

規則は四つある（R1〜R4）。

**R1. 適合を構造部分と Origin 関係に分け、Origin 関係は選択の後に判定する（SPEC §3.8、§15.6.1、§10.1〜§10.3）。**

値の適合 `type(value) <: type(destination)` は二つの部分からなる。値の適合とは、§10.2 が挙げる位置での適合と、Place 結果（§7.1.1）の適合である。§10.2 の位置は、引数、注釈付きの初期化子、代入元、結果、要素、payload、既定値、固定された Target Result Type の結果源である。

- **構造部分。** すべての Origin 束縛を同じとみなして、§3.8 の同一性と部分型の証明を行う。この部分の失敗だけが Type 不一致である。構造部分が失敗した適合は Origin 関係を生まない。これは、失敗した検査が飛ばした部分にあたる（§23.3.6.4）。
- **Origin 関係。** 構造部分が成り立つと、§3.8 の「Origin shortening and variance」行が位置ごとに関係を生む。共変と反変の位置では outlives、不変の位置では等式である。

宣言された関係、呼出しで置換された関係（§15.3.3、§15.6.4 手順 3）、well-formedness（§15.6.1）も Origin 関係である。§11.3.2 の静的記憶域の規則は `static` への適合として現れるので、別の種類は作らない。これらはすべて一つの要件「Origin 関係」（required Origin relation）であり、Type 不一致ではない。

適用可能性（§10.1 手順 5・6）と結果の絞り込み（§10.3）は、構造部分だけを使う。Origin 関係は、選んだ候補について選択の後に R2 で判定する。局所の region は選択の後に解かれる（§15.4.4）。また、Origin だけで異なるオーバーロードは存在しない（§9.1）。だから、寿命で選択を変える理由はない。

次はそれぞれ別の要件のままとし、R4 の表示だけを共有する。

- 契約全体を比べる検査。Contract 実装の照合（§8.4.5）、特殊化（§8.8）、§10.7 の Callable 互換性と共通 Function Type への変換（§15.3.7 の比較）がこれにあたる。
- 証明に Origin がかかわる Constraint。`Owned`、Callable、Type 同一性（§8.7）がこれにあたる。これらは、これまでどおり §10.1 の適用可能性に加わる。
- 排他借用の一意の Loan anchor（§15.2.3）。
- 主解のない推論で、注釈が必要になる場合（§15.3.6）。

**R2. 判定は三つの結果のどれかになり、反証の規則は上限だけである（SPEC §15.6.5、§15.3.6）。**

Origin 関係は、§8.7 の Proven・Refuted・Unknown で判定する。Origin を次の三つに分ける。

- **固定された Origin。** 本体の契約が固定する Origin である。シグネチャーの universal Origin、パラメーターと受け手の Origin、それらの射影、`static` がこれにあたる。Closure 本体では、捕捉の固定された Origin と呼出しごとの Origin（§15.8.2）も含む。固定された Origin は、本体のすべての点を含む。
- **有限 Origin（finite Origin）。** 上限を持つ Borrow の Origin である。region は使用に従って推論されるが、上限を越えることはできない。
  - **本体局所の Origin。** Loan の anchor が、本体の局所変数・パラメーター・一時値の root にある Borrow の Origin である。上限はその本体である。Place は root そのものか、root から Field・要素・所有する層の射影でたどった Place である。借用や生ポインターの先をたどった Place は含まない（Place の射影は §15.6）。暗黙の Borrow（§10.2 の表、§7.3 の受け手）と、捕捉項目の Borrow（§7.6.2）も含む。Closure の環境の束縛は、隠れた受け手の Field として扱う（§15.8.2）。そのため、借用した受け手を通じた Borrow は本体局所ではなく、消費する呼出しでは本体局所である。
  - **可変静的記憶域の新しい Borrow の Origin**（§11.3.2）。上限は、その本体と、借用した入力で上限を持つ結果である。その結果では、Field の anchor が効果の要約に残る（§15.6.4）。
  - 生 Place の Borrow の Origin は上限を持たない（§5.2.2、§15.2.3）ので、有限 Origin ではない。
- **推論された region。** それ以外の局所の Origin である。たとえば、注釈のない局所変数の Type の slot がこれにあたる（§15.4.4）。

関係は、推論された region と有限 Origin を通じて推移的につながる。判定は、つながった連鎖ごとに行う。

- **Refuted.** 有限 Origin から、その上限の外にある固定された Origin へ至る連鎖である。たとえば、本体局所の Origin から本体の外の Origin へ至る連鎖、可変静的記憶域の Borrow から `static` へ至る連鎖、その Borrow から結果以外の経路でパラメーターの Origin へ至る連鎖である。Origin 関係を反証する規則はこれだけである。
- **Proven.** 両端が固定された Origin で、§15.3.6 の solver が前提から導ける連鎖である。前提とは、宣言された関係、well-formedness、`static` が最大であること、反射律・推移律・meet の法則である。片方の端が有限 Origin または推論された region で、Refuted でない連鎖も Proven であり、region 推論の制約になる。
- **Unknown.** 両端が固定された Origin で、solver が導けない連鎖である。§15.3.6 の既存の期限までに Proven にならなければ失敗する。

したがって、失敗しうる連鎖の短い側の端は、必ず固定された Origin である。有限 Origin どうしの関係と、推論された region どうしの関係は、Origin 関係としては失敗しない。局所の Place の寿命を越える使用は、これまでどおり破棄や Move の Loan 衝突として報告する（§15.6.2、§15.6.6）。

**R3. 報告は二つのコードで行い、失敗した関係はその後仮定しない。**

| 結果 | コード | 分類 |
| --- | --- | --- |
| Refuted | `UnsatisfiedOriginRelation_Kd` | Error, `Language` |
| 期限で Unknown | `UnprovenOriginRelation_Kd` | Error, `Proof` |

- この二つは、Constraint の `UnsatisfiedConstraint_Kd` と `UnprovenConstraint_Kd` の対（§8.7 "Use and finalization" の Refuted と未証明の区別）にならう。`OwnershipFailure.UnprovenOrigin` を `UnprovenConstraint_Kd` に写す現在の対応は、二つ目のコードに置き換える。
- 問題の同一性は（主位置、関係の出所、長い側の端）である。関係の出所は、適合なら宛先 Type の中の位置、宣言された関係ならその節、well-formedness なら Type 出現である。失敗した連鎖ごとに一つ報告し、最初の一件で止めない。
- 主位置は、短い側の端を持ち込んだ関係で、長い側の Origin を供給する式である。適合しない値や、置換された関係の長い側の引数がこれにあたる。そのような式がなければ、関係を求める構文（節、または Type 出現）とする。§10.2 の「Temporaries」が求める、期限の切れた一時値と使用を名指す記録は、この記録が一時値を R4 の `borrow` で示すことで満たす。
- 同じ主位置の記録は、関係の出所の順に並べ（外側の借用層、Semantics の対象、ヘッダー順の slot、Type 引数の順を再帰的に適用する）、次に長い側の端の構文の位置の順に並べる（§23.3.6.6）。
- `Proof` の記録は「証明できない」と述べ、偽とは述べない。
- 失敗した関係は、その後の region 推論に制約を加えない。Loan・破棄・結果検証の検査は、その関係なしで進む。そのため、その関係だけが引き起こす Loan 衝突や結果の不一致（`IncompatibleResult_Kd`）は生じない。§15.6.5 の例は `local@ref` の一件だけになる。他の使用が引き起こす衝突は、独立した問題として残る。前提条件の失敗で判定できない関係は、これまでどおり派生した問題として扱う（§23.3.6.4。例は §15.3.2 の存在しない slot）。
- Advice は条件付きの文で書き、修正候補にはしない。§23.3.6.9（23:268）は、Origin の修正を選択が要るものとして Advice にしている。
  - Unknown で、両端が universal Origin またはその射影の場合。`origin <長い側> outlives <短い側>` 節を加える（公開契約の変更）か、結果の Origin を長い側にする。端が `omitted` なら、先にその Type 出現に集合名を付ける（§15.3.1）。端が暗黙に導入された名前なら、見えている似た名前も示す。
  - 短い側が §15.4.3 の既定で完成した結果の Origin の場合。§15.4.3 が求める、省略された入力の境界の説明を Note に置く。
  - Refuted は注釈では直らない。本体局所の Origin なら、所有する値を移すか、入力から借用する。可変静的記憶域なら、§11.3.2 と §15.9 の手段を使う。手段は、直接のアクセス、入力で上限を持つ結果、scoped callback、不変の静的記憶域である。

**R4. Origin の表示と Reason の事実（SPEC §23.3.6.5、§15.2.3）。**

Reason の中の Origin は、種類と、長さに上限のある文字列で表す。表示するのは連鎖の端だけなので、推論された region を表示することはない。

| 種類 | 対象 | 文字列 |
| --- | --- | --- |
| `expression` | Origin 式（§15.2.1）で書ける固定された Origin。`static`、シグネチャーの名前、パラメーター、受け手、`p.a` のような射影、meet | シグネチャーに書かれた式。名前が書かれていなければ、パラメーターまたは射影 |
| `borrow` | 有限 Origin | Borrow の原文（`local@ref`、`State.count@ref`）。暗黙の Borrow ではその Place または一時値の原文（`node`、`makeResource()`）、捕捉項目ではその項目（`bias@ref`） |
| `omitted` | 名前も射影もない固定された Origin。例は `items: ref/Array<View<T>>` の中の `View` の slot | その Type 出現の原文 |

- `borrow` と `omitted` の文字列は、Origin 式として書かない。たとえば「the borrow `State.count@ref`」と書き、`during State.count` とは書かない（§15.9）。
- この二つの種類には、その構文を指す関連位置（役割 `origin`）も加える。文字列は Reason に残すので、省略できる補足だけに置かれることはない（§23.3.6.2）。
- この表示は、Origin を名指すすべての Reason に使う。R3 の記録、Owned の失敗、§15.3.2 と §15.4.3 の記録がこれにあたる。
- Type 不一致は構造部分の失敗である（R1）。そのため、その表示は Origin 束縛を比べず、省く。R3 の記録の宛先 Type では、失敗した位置の Origin だけを明示する。

R3 の記録は次の事実を持つ。

- `relation`：`outlives` または `==`。
- `longer`・`shorter`：上の表示。
- `source`：`fit`、`declared`、`wellFormed` のいずれか。`fit` では `destination`（宛先 Type の表示）を加える。`declared` では、その節を関連位置（役割 `relation`）にする。

Owned の失敗（§15.2.3）は、Constraint のコード（`UnsatisfiedConstraint_Kd`、`UnprovenConstraint_Kd`）のままとする。共通 Function Type への変換の Owned 条件（§7.6.4）も同じコードで報告し、Type 不一致にはしない。Reason には `origin`（上の表示）と `member` を加える。`member` は OwnedOrigins に寄与した部分で、外側の Origin、Semantics の対象、n 番目の引数、基底、Field 名、payload、n 番目の成分、要素、捕捉名のいずれかである。名指すのは、§15.2.3 の列挙順の深さ優先で最初に Refuted になったもの、なければ最初の Unknown である。残りは Omissions に件数を示す。

**例**

```kimi
struct H {a}
    public let item: ref/i32 during a
    public init(item: ref/i32 during a) => self.item = item

func pick(p: ref/H, q: ref/i32) -> ref/(ref/i32 during q) during p
    return p.item@ref
    // Error (UnprovenOriginRelation_Kd, Proof) at p.item@ref: p.a is not proven to outlive q.
    // Advice: add origin p.a outlives q, which changes the contract, or bound the inner result by p.a.

func pickRelated(p: ref/H, q: ref/i32) -> ref/(ref/i32 during q) during p
    origin p.a outlives q
    return p.item@ref                       // Valid: the premise proves p.a outlives q.
```

```kimi
func bad(x: ref/i32) -> ref/i32 during x
    let local: i32 = 1
    let r = local@ref
    return r
    // One record at r (UnsatisfiedOriginRelation_Kd): the borrow local@ref cannot outlive x.
    // Related location (origin): local@ref. Destroying local reports no Loan conflict.
```

```kimi
group State
    public var count: i32 = 0

func pinned() -> ref/i32 during static
    return State.count@ref
    // Error (UnsatisfiedOriginRelation_Kd): the borrow State.count@ref cannot outlive static.
    // The Reason never writes during State.count (SPEC §15.9).

func current(anchor: ref/i32) -> ref/i32 during anchor
    return State.count@ref                  // Valid: an input-bounded result (SPEC §11.3.2).

func store(anchor: ref/i32, target: uniq/(ref/i32 during anchor))
    target@follow = State.count@ref
    // Error (UnsatisfiedOriginRelation_Kd): the borrow State.count@ref reaches anchor outside a result.
```

```kimi
struct Counter {source}
    public let value: ref/i32 during source
    public init(value: ref/i32 during source) => self.value = value

func leak() -> rc/Counter                   // source defaults to static (SPEC §15.4.3 rule 3).
    let number: i32 = 5
    return Kimi.Intrinsics.makeRc(Counter.init(number@ref))
    // Error (UnsatisfiedOriginRelation_Kd) at the returned value: the borrow number@ref cannot satisfy
    // static at slot source of rc/Counter. Note: the omitted result slot defaults to static.

group Registry
    public var counter: Option<Weak<rc/Counter>> = .None // source defaults to static.

func remember()
    let level: i32 = 3
    let counter = Kimi.Intrinsics.makeRc(Counter.init(level@ref))
    Registry.counter = .Some(Kimi.Intrinsics.downgrade(counter@ref))
    // Error (UnsatisfiedOriginRelation_Kd): the borrow level@ref cannot satisfy static.
```

```kimi
public func main()
    let bias: i32 = 4
    let visitor = func [bias@ref] () -> i32 => bias
    let erased: () -> i32 = visitor@move
    // Error (UnsatisfiedConstraint_Kd): the environment is not Owned (SPEC §7.6.4).
    // Reason: member capture bias, origin the borrow bias@ref (related location, role origin).

    var kept: Option<rc/Counter> = .None
    do
        let number: i32 = 5
        kept = .Some(Kimi.Intrinsics.makeRc(Counter.init(number@ref)))
    match kept
        .Some(_) => Console.writeLine("Kept.")
        .None => ()
    // Unchanged: ComparisonLoanConflict_Kd when number is destroyed; only local regions are involved.
```

**最小で一貫している理由**

- **Principle 1。** 一つの要件（Origin 関係）を一度だけ判定し、結果ごとに一つのコードを与える。Constraint と同じ Unsatisfied と Unproven の対を使う。§11.3.2 の "cannot be fitted to `static`" と "only through a result"、§15.6.5 の "cannot be widened" は、有限 Origin の上限という一つの規則にまとまる。表示は、どの記録でも同じ規則に従う。
- **Principle 2。** 反証は、Borrow の構文、その Place の射影、関係を求めた位置だけで決まる。生存解析や本体の外の情報は要らない。オーバーロードの選択は寿命に依存しない。記録は、固定された Origin を求める使用の位置に出る。
- **Principle 3。** 証明できないことを偽とは述べない。名前のない Origin は、種類を添えた原文で示す。存在しない構文は作らない。
- 既存の概念を使い回す。§8.7 の三つの結果、§15.3.6 の solver と期限、§5.2.2 と §15.2.3 の「上限」、§23.3.6.4 の飛ばした部分と派生、§23.3.6.2 の必須の事実である。新しい語は「有限 Origin」の上限だけで、これは §15.2.3 の finite Origin と §5.2.2 の "no upper bound" を一つにしたものである。

**採らなかった案**

| 案 | 理由 |
| --- | --- |
| `TypeMismatch_Kd` に Origin の表示を足すだけにする | 関係の失敗を Type 不一致として報告することになり、Unknown も `Language` になる（§23.3.6.1）。 |
| 一つのコードで Refuted と Unknown を兼ねる | コードは一つの分類しか持てない（§23.3.6.1）。 |
| 一つの適合で失敗した関係を一つの記録にまとめる | 問題の同一性には条件が入る（§23.3.6.4、DIAGNOSTICS §4.1）。上限を超えると、必須の事実が省かれる。 |
| Origin 関係を適用可能性に含める | 寿命で選択が変わる。局所の region は選択の後に解く（§15.4.4）。Origin だけで異なるオーバーロードは存在しない（§9.1）。 |
| 可変静的記憶域の Borrow の上限を「`static` ではない」だけにする | 結果以外の経路（排他借用のパラメーターへの格納など）でも外へ出せることになり、§11.3.2 の "only through a result" と効果の要約（§15.6.4）に反する。 |
| 名前のない Origin を `'1` のような番号や `during State.count` で表示する | 原文にない構文を作ることになる（§15.9）。 |
| 名前のない Origin を関連位置だけで示す | 必須の事実が省略できる補足だけに置かれる（§23.3.6.2）。 |
| 本体の外への脱出もすべて破棄時の Loan 衝突として報告する | 失敗は Borrow の構文だけで決まるのに、原因から離れたスコープの末尾で報告することになる。終了しない経路では衝突が起きず、§15.6.5 の例とも合わない。 |
| Owned の失敗も Origin 関係のコードにする | Owned は宣言された Constraint で、別の要件である（§15.2.3、§8.7）。共有するのは表示だけにする。 |

**変更する節**

| 文書 | 変更 |
| --- | --- |
| SPEC §3.8 | Origin 行に追記する。この行は Origin 関係（§15.6.1）を生み、構造部分が成り立つ適合の失敗は Type 不一致ではない。 |
| SPEC §4.6.1 | 187 行の Type 不一致の表示は構造部分の違いを示し、Origin 束縛は §23.3.6.5 に従って省くと明記する。 |
| SPEC §10.1〜§10.3 | R1 の選択の規則。§10.2:58 の "plus ordinary Origin fitting" を、Origin 関係は選択の後に §15.6.1 で判定する、に改める。§10.3:172 の "Origins are instantiated and checked" を、Origin は具体化するがその関係で候補を除かない、に改める。 |
| SPEC §11.3.2 | "cannot be fitted to `static`" と "only through a result Origin bounded by a borrowed input" が、§15.6.5 の有限 Origin の上限であることを添える。 |
| SPEC §15.2.3 | 有限 Origin の定義を §15.6.5 に置き、ここから参照する。Owned の失敗の Reason（R4）を加える。 |
| SPEC §15.6.1 | R1 と R3 を加える。 |
| SPEC §15.6.5 | R2 を加える。"local region" を本体局所の Origin に置き換え、例のコメントを R3 の記録に合わせる。 |
| SPEC §23.3.6.5 | R4 の表示を加える。 |
| docs/dev/DIAGNOSTICS.md | 要件 `Ownership.UnprovenOrigin` を一つの Origin 関係の要件に改め、判定する場所を一つにする。二つのコードと、事実の種類 `Origin`（種類と文字列）を加える。`IncompatibleResult_Kd` は構造部分だけを判定する。共通 Function Type への変換の Owned 条件は Constraint のコードで報告する。§15.4.3 の Note を新しいコードへ移す。145 行の派生の記述を改める。 |
| docs/dev/CODEMAP.md、PLAN §7 | 判定する場所を記載する。G56 を閉じる。 |
| テスト | Origin だけの違いの期待コードを改める。たとえば BorrowedSlotReferenceTest.cs:44–46 は `UnprovenOriginRelation_Kd` にする。PlaceResultTest.cs:47（`make()[0]` を `place ref/i32 during static` で返す）は `UnsatisfiedOriginRelation_Kd` にする。Origin や well-formedness の義務に `UnprovenConstraint_Kd` を期待するテストも見直す。 |

IMPL は変更しない（ABI と実行時に影響しない）。受理されるプログラムの集合は、次の二つを除いて変わらない。一つは、本体局所の Origin が終了しない経路でだけ外へ出る場合で、これを新たに拒否する。そのような借用を観測できる呼出し元の続きは存在しない。もう一つは、Origin 関係だけで候補が落ちて別の候補が選ばれていた呼出しで、選択が変わって拒否されうる。§9.1 により、これはまれである。

**マイルストーンの書き換え**

不要である。P26・P34・P36・P38 の元のプログラムは正しく、拒否検査は変異として書く。

- 本体の外の universal Origin や `static` への脱出（P34 の返したハンドル、P36 の可変静的記憶域の `during static`、P38 の静的 Weak）は、`UnsatisfiedOriginRelation_Kd` を期待する。
- 局所どうしの脱出（`do` ブロックの外へ代入したハンドルなど）は、`ComparisonLoanConflict_Kd` のままである。拒否検査の期待コードは、本書の例と同じく脱出の形で選ぶ。
- Owned でない消去（P26）は、新しい Origin の表示を持つ `UnsatisfiedConstraint_Kd` を期待する。今の `TypeMismatch_Kd` と Note（Binding.cs:672-678）を置き換える。

### 3.3. 非修飾名は継承した宣言を選ばない：止めて報告する（P25）

この主題の項目は Q10 だけである。既存の missing receiver の規則（SPEC §9.4）を、「Type と Value の名前空間で、非修飾名は受け手を補わず、struct が継承した宣言を選ばない」という規則に一般化する。基底の層は、lookup を止めるためだけに調べる。

P25 のソースは影響を受けない（修飾形と受け手の形だけを使う）。隣接テストと、新しい BindingFailure・目録・CODEMAP の連絡口を固めるため、P25 と一緒に入れる。現在の字句だけの実装は、どの案を採っても変える必要がある。

#### 3.3.1. Q10. 派生 struct の中の非修飾名が、基底の層を探すかどうか

**現状と問題**

SPEC §9.4 の段には、字句的な Container だけが並んでいる（09:229-230「2. The current Container. 3. Each parent Container separately, up to the project root.」）。

基底の層を一層ずつ探す規則は、SPEC §9.5 の「Inherited ordinary lookup」にしかない（09:299「lookup searches the statically selected struct and then its direct bases, one layer at a time」）。この歩みが §9.4 の段 2・3 に含まれるかどうかは、どこにも書かれていない。

実装は字句的に読んでいる。

- `src/Kimi/Compiler/Binding/Binding.Types.cs:73-117` の `Lookup` は、`scope.Parent` だけをたどる。
- 基底を探すのは、修飾された経路のときだけである（`Binding.MemberLookup.cs:124-213` の `LookupTypeMember`）。

読み方によって意味が変わる。

```kimi
func marker() -> i32 => 1

open struct Base
    public var count: i32 = 0

    public func marker() -> i32 => 25

struct Derived : Base
    func read(self) -> i32 => marker() // 1 under the lexical reading; 25 if inherited declarations count.
```

**字句的な読み（現在の実装）の欠点**

1. SPEC §9.4 の missing receiver（09:253「An unqualified reference that finds only accessible instance members reports a missing receiver instead of searching for an outer static member.」）が、自分の層にしか効かない。`Derived` の中で裸の `count` を書くと、外側の `count` に黙って結び付くか、未定義になる。
2. 本文を `Base` から `Derived` へ移しただけで、`marker()` の意味が黙って変わる。
3. SPEC §6.2.2（06:193）と SPEC §9.6.1（09:351）は、継承した名前が派生側でもその名前を占めるものとして扱う。字句的な読みは、これと揃わない。

**継承を含める読み（段 2 に基底の層を入れる）の欠点**

1. SPEC §18.1（18:21「they are never found unqualified」）と矛盾する。`struct Mine : Lib.Base` の中で、外部 Kotonoha のメンバーが非修飾で見つかってしまう。
2. 優先順位が逆になる。非修飾で届く外部の名前は、本来は別名の段 5・6 で最後に見つかる。この読みでは、外部の基底のメンバーが段 2 で見つかり、プロジェクトの root や別名より先になる。
3. 上流の基底が `marker` や `Node` を追加すると、下流の非修飾の `marker()`・`Node` が黙って別の宣言を指すようになる。SPEC §18.7.3（18:316）の不在の依存によって再検証は起きる。しかし再検証は別の対象で成功するので、診断は出ない。
4. 基底節を解決するときに基底の層を探すと、循環する。

SETTLED.md と付録 D には、この項目はない。残りのマイルストーン（P25・P33・P38）は、継承したメンバーを `self.`・`value.`・`Derived.` の形でだけ使う。したがって、この項目が止めているのはプログラム本体ではなく、それに隣接する形である。

**決定案**

規則は一つである。

> **R1.** Type と Value の名前空間で、非修飾名は受け手を補わず、struct が基底から継承した宣言を選ばない。

R1 は次のように具体化する。

1. **止める場所。** 段 2・3 で Container `C` を探すとき、次のどちらかに当たれば、外へは探しに行かない。その場で `QualificationRequired_Kd` を報告して止まる。
   - `C` 自身の eligible な宣言が、すべてインスタンスメンバーである。これは既存の missing receiver である。今と同じく struct 以外の Container（受け手を持つ関数を宣言した enum など）にも適用する。
   - `C` が struct で、自身には eligible な宣言がなく、基底の層の検査がその名前の宣言を見つける。
2. **基底の層の検査。** 直接の基底、その基底、と一層ずつ進む。その名前の、accessible で role に合う宣言を持つ最初の層で止まる。これは SPEC §9.5 の層の歩みと同じである。
   - 対象は、環境選択、Mod の出力、断片の併合を終えた、完成した宣言の集合である（06:195 の継承名の検査と同じ）。conditional member も宣言として数える（SPEC §10.5、10:290）。
   - 検査は宣言を読むだけで、何も選ばない。基底の Type 引数や Origin 引数の束縛は使わない。使うのは SPEC §9.6.1 の手順 1（宣言の経路）の結果だけである。
3. **アクセス可能性。** 使用位置の effective access domain（SPEC §9.3.1）で判定する。
   - protected の受け手の制限は、明示の受け手にかかる制限である。検査には適用しない。
   - inaccessible な宣言と role の違う宣言では止まらない（SPEC §9.5 と同じ）。そのため、上流が private のメンバーを足しても、下流には影響しない。
4. **メンバーではないもの。** 基底の総称パラメーターと `Self` はメンバーではないので、検査しない。これは、開く別名の除外（18:60「not formal parameters, Origins or `Self`」）と同じ扱いである。Origin と Label の lookup は変えない（SPEC §15.3.4）。基底の Origin スロットや出現集合（15:526-528）は Origin の名前空間に属するので、R1 の対象外である。
5. **基底節。** struct の基底節は、引数も含めて、その struct 自身の基底の層を検査しないで解決する。
   - 層を決める部分は、層の外側の環境で読む。局所の初期化子が外側の環境を使う（09:96）のと同じ考え方である。総称パラメーターと Origin ヘッダーは名前を宣言するだけで lookup をしないので、例外は基底節だけで足りる。
   - Constraint 領域は本体に属するので、検査の対象になる。基底の引数の射影（`Base<T.Element>`）が Constraint 領域を必要としても、循環はしない。検査が使うのは、基底宣言の同一性（手順 1）だけだからである。
   - この例外があっても循環が残る場合は、解決できない依存の循環として誤りになる（09:22）。たとえば `struct S : S.N` のように、自分の入れ子の struct `N` を基底にする場合である。`N` の基底節の名前を段 3 で `S` まで探すと、`S` の基底の層として `N` を調べることになり、`N` の基底節そのものが要る。これはまれな形であり、どのマイルストーンも使わない。
6. **二つの名前空間。** SPEC §9.5 が Type と Value の両方の経路を探す場合、この停止は、その名前空間で失敗した経路として扱う。もう一方の経路が成功すれば、そちらを選ぶ。上流の追加が片方の経路を止めても、選ばれる宣言は黙って変わらない。
7. **正規の書き方。** 継承した宣言は次の形で書く。どの形も SPEC §9.5 によって、検査が見つけたのと同じ層を選ぶ。
   - インスタンスメンバー：`self.member`、または別の明示の受け手
   - それ以外：`Self.member` か `C.member`

SPEC §9.4 の 09:253 の段落を、次の英文に置き換える。

> **No implicit receiver or inheritance.** In the Type and Value namespaces, an unqualified Name never supplies a receiver and never selects a declaration that a struct inherits from its bases. When stage 2 or 3 searches a Container `C`, lookup stops there with `QualificationRequired_Kd` instead of searching outward if `C`'s own eligible declarations are all instance members, or if `C` is a struct without own eligible declarations and an examination of its base layers finds one. The examination visits the direct base, then its base, one layer at a time, over the completed declaration set (§6.2.2), and stops at the first layer with an accessible, role-compatible declaration of the Name, as inherited ordinary lookup does (§9.5). It reads declarations only: it needs no base Type or Origin arguments and selects nothing. Accessibility is the effective access domain at the use (§9.3.1); the protected receiver restriction concerns an explicit receiver and does not apply. A base's generic parameters and `Self` are not members and are never examined. A struct's base clause, with its arguments, resolves without examining that struct's own base layers; a dependency cycle that an examination still forms is an unresolvable dependency cycle. In the two-namespace exploration of §9.5, such a stop is a failed path of its namespace. Instance members are reached with `self.member` or another explicit receiver, and other inherited declarations with `Self.member` or `C.member`. Origin and Label lookup are unchanged.

**例**

```kimi
struct Node

func marker() -> i32 => 1

open struct Base<T>
    protected var count: i32 = 0

    public func marker() -> i32 => 25

    public struct Node

struct Derived : Base<Node>                  // The base clause skips its own base layers: the root Node.
    let tag: i32 = Self.marker()             // An inherited Type function: 25.

    func total(self) -> i32 => self.count + Self.marker()
    func make() -> Self.Node => Self.Node.init() // Base<Node>.Node (§9.6.1).
    func root() -> i32 => ::marker()         // Selects the root marker explicitly: 1.

    func first(self) -> i32 => marker()      // Error: lookup finds Base<Node>.marker and stops.
    func second(self) -> i32 => count        // Error: an inherited instance member; write self.count.
    func third(value: Node) -> () => ()      // Error: the inherited Node; write Self.Node or ::Node.

    struct Probe
        func read() -> i32 => Derived.marker() // Self is Probe, so Derived. is written.
```

`Probe` の中の裸の `marker()` は、段 3 で `Derived` を探すときに止まる。`Derived` を複数の断片に分けても、結果は同じである。基底節を持たない断片の中の `marker()` も同じエラーになり、関連位置は別の断片にある基底節を指す。

**診断と修復候補（SPEC §23.3.6.9）**

- code を一つ新設する：`QualificationRequired_Kd`（Error、`Language`）。
  - missing receiver も継承した宣言も、同じ要件「非修飾名は受け手を補わず、継承した宣言を選ばない」に違反した場合である。そのため一つの code にまとめ、Reason の値 `cause: Receiver | Inherited` で区別する（SPEC §23.3.6.1）。
  - Reason：名前、名前空間、`cause`、探した Container `C`、見つかった宣言がインスタンスメンバーを含むかどうか。`Inherited` の場合は、止まった層の基底宣言も含める。
- 主位置はその名前である。関連位置は次のとおりである。
  - 見つかった宣言（関数の集まりでは最初のもの。役割 declaration）
  - `Inherited` の場合は `C` の基底節（役割 base）。基底節は別の断片にあってもよい。
- 修復の種類を一つ加える：`Repair.Qualify`。名前の前に修飾子を一つ挿入する。候補は互いに代わりとなるもので、どの条件も検査で得た事実から決まる。

| 修飾子 | 提示する条件 |
| --- | --- |
| `self.` | 見つかった宣言がインスタンスメンバーを含む。かつ、使用位置と同じ Function Boundary の中で `self` が見え、その Effective Core が `C` である。`self` が構築の受け手のときは、constructor の本体の中で、自分の層の stored Property を指す場合に限る。SPEC §6.2.3.4 が、構築中の継承した Field、computed、メソッドへのアクセスを禁じているからである |
| `Self.` または `C.` | 見つかった宣言が、インスタンスメンバーでない宣言を含む。`Self` が `C` を表せば `Self.` を使う。そうでなければ、次の両方を満たすときに `C.` を使う。<br>• `C` が総称パラメーターも Origin ヘッダーも持たない<br>• 使用位置で `C` の名前を Qualifier として lookup すると `C` が選ばれる |
| `::P.` | 診断のための探索（09:255）で、後の段に、インスタンスメンバーでない eligible な宣言が見つかる。かつ、その Container への Compilation root からの経路 `P` が Type 引数・Origin 引数なしで書け、経路の各区間が使用位置から accessible である。project root の宣言には `::` だけを付ける |

- 条件の語彙に `Selection` を一つ加える。意味は「修飾した名前が、使用の求めるものを選ぶ」である。つまり、Type name selection（SPEC §9.6）による一つの Type、値として使う Field または Property、overload resolution（SPEC §10.1–§10.4）による適用可能な関数のどれかである。判定は次のとおりである。
  - 呼び出さない Field または Property：verified
  - Type：書かれた引数で Type name selection を行い、その結果で verified か refuted
  - それ以外（関数の集まりと、呼び出す Field）：required
- `UsageLegality` は、値の使用で required とする（この版では常に required）。Type の使用には関係しない。
- 候補を出さない場合は、受け手や修飾の書き方を Advice で述べる。たとえば、段 3 で見つかったインスタンスメンバーや、Field 初期化子と基底の引数の中のインスタンスメンバーである。

§23.3.6.9 に加える英文は次のとおりである。条件の表には次の行を加える。

> | Condition | Meaning |
> | --- | --- |
> | `Selection` | The qualified Name selects what its use needs: one Type by Type name selection (§9.6), a Field or Property used as a value, or an applicable function by overload resolution (§10.1–§10.4) |

目録には次の行を加える。

> | Diagnostic | Kind and edits | Judgment | Offered when |
> | --- | --- | --- | --- |
> | `QualificationRequired_Kd` (§9.4) | `Repair.Qualify`: a qualifier before the Name: `self.`, `Self.` or `C.` for the searched Container `C`, or `::P.` for a declaration of a later stage | `Selection`: verified for a Field or Property that is not called, verified or refuted by Type name selection for a Type, required otherwise; `UsageLegality`: required for a value | `self.`: the found declarations include an instance member, and a `self` visible in the same Function Boundary has Effective Core `C`; a construction receiver qualifies only in the constructor body and for an own stored Property (§6.2.3.4). `Self.`: they include a non-instance declaration and `Self` denotes `C`; otherwise `C.` when `C` has no generic parameters or Origin header and the Qualifier `C` selects `C` at the use. `::P.`: the diagnostic exploration of the later stages finds eligible non-instance declarations in a Container reached from the Compilation root by a path `P` without Type or Origin arguments whose every segment is accessible at the use (`::` alone at the project root) |

**最小で一貫している理由**

- **Principle 1（一つの概念に一つの形）。**
  - 継承した宣言の書き方は、`self.`・`Self.`・`C.` に限られる。
  - 新しい概念は足さない。既存の「止めて報告する」規則（09:143、09:253）と、既存の層の歩み（09:299）を組み合わせるだけである。
  - 自分の層のインスタンスメンバーも基底の宣言も、同じ code と同じ修復で扱う。
- **Principle 2（局所的な推論）。**
  - 成功する非修飾名は、常に字句的な宣言を指す。つまり、局所の宣言、`C` 自身、親、root、別名のどれかである。
  - 基底節が別の断片や別の Kotonoha にあっても、名前の意味は目に見える本文だけで決まる。
  - SPEC §18.1 の「never found unqualified」は、変更なしでそのまま成り立つ。
- **Principle 3（明示の意味）。**
  - 上流の基底が名前を足しても、下流の非修飾の使用が黙って別の宣言を指すことはない。代わりに、位置が分かり修復できるエラーになる。
  - その検出には、SPEC §18.7.3 の不在の依存がそのまま使える。
- **Principle 4（Compiler Server Protocol）。** 修飾は機械的な挿入であり、候補の条件は事実から決まる。
- **費用が小さい。** 検査は宣言の名前表を引くだけで、引数の置換も推論も要らない。`C` の本体の中では使用位置の access domain が同じなので、結果は (`C`, 役割, 名前) ごとに共有できる。

**採らなかった案**

- **継承を含める読み（段 2 に基底の層を入れる）。** SPEC §18.1 と矛盾する。外部の基底のメンバーが root や別名より先に見つかり、上流の追加が黙って意味を変える。また、基底節の例外は結局必要になる。
- **字句的な読みのまま（現在の実装）。** 基底のインスタンスメンバーが missing receiver を逃れ、外側の宣言に結び付く。本文を層の間で動かすと、意味が黙って変わる。
- **インスタンスメンバーだけを止める。** Type 関数と入れ子の Type は、黙って外側に結び付いたままになる。規則が二つに分かれる。
- **外側に候補がないときだけ基底の宣言を選ぶ。** 後で外側に宣言が足されると、黙って別の宣言を指すようになる。No backtracking（09:22）にも反する。
- **警告にとどめる。** 意味が見えないままで、受理するかどうかの判断も変わらない。
- **非修飾の `x` を `Self.x` と同じとみなす。** `Type.method(receiver, ...)` という unbound 呼出しがある（07:272）ので、非修飾の `read(self)` が通ってしまう。暗黙の `self` を禁じる 09:253 と 07:272 に反する。
- **struct の基底節では一切検査しない（入れ子の struct の基底節も含める）。** 循環は完全になくなる。しかし、派生 struct の中の入れ子の struct が `struct Leaf : Node` と書いたとき、継承した `Node` を黙って飛ばし、外側の `Node` を選ぶ。本体と基底節で規則が分かれる。

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §9.4 | 09:253 の段落を上の英文に置き換える。例に、継承した Type 関数と入れ子の Type を非修飾で書いた誤りと、`Self.`・`::` を使った書き方を加える |
| SPEC §9.5 | 「Inherited ordinary lookup」の段落（09:299）に、次の一文を加える：「Unqualified uses never select declarations inherited from a base; §9.4 examines base layers only to stop lookup.」 |
| SPEC §6.2.2 | 06:197 の前半を、次のように改める：「The accessible member Names of an open struct, in the Value and Type namespaces, are part of its API toward derived Types. Adding Names or widening access can invalidate downstream declarations and unqualified uses in derived bodies (§9.4) under dependency revalidation.」後半は今のまま残す |
| SPEC §10.6 | 10:307 の「missing receivers」を、「missing receivers and inherited declarations named without a qualifier (§9.4)」にする |
| SPEC §23.3.6.9 | 条件の表（23:242-247）に `Selection` の行を加える。目録（23:257-266）に `QualificationRequired_Kd`（`Repair.Qualify`）の行を加える。どちらも上の英文を使う |
| IMPL §21.3.4.1 | 21:577 の「Adding or widening an accessible Container name on an open base can break derived declarations」を、「Adding or widening an accessible member Name on an open base can break derived declarations or unqualified uses in derived bodies (§6.2.2, §9.4)」にする |
| docs/SETTLED.md | 継承を含める読み（非修飾名が継承した宣言を選ぶ）を採らないことと、その理由を一項目として記す |

SPEC §18.1 と §18.7 は変えない。不在の依存（18:316）が、そのまま検出の仕組みになる。SPEC §8.4.3 の Contract 本体の短い名前（08:218、08:233）も変えない。Contract の refinement は struct の継承ではないので、R1 の対象外である。

**マイルストーンの書き換え**

書き換えは要らない。

- **P25：** Milestone25.kimi:42 は `Derived.marker()`、:40 は `value.count` と書いている。`: base(count)`（:31）の `count` は段 1 のパラメーターである。`Derived` の Field 初期化子（:29）と本体が使う `Resource`・`Construction`・`Console` は、`Base` が入れ子の Type を宣言していないので、検査を通り過ぎて今と同じ宣言を選ぶ。
- **P33：** Milestone33.kimi:16-25 の `Leaf`・`Other` は、パラメーターと自分のメンバーだけを使う。
- **P38：** Milestone38.kimi:15-25 は、`self.name`・`self.level`・`self.watts` と書いている。`Lamp.init` と `Sensor.init` の `name` は段 1 のパラメーターである。`Utf8Format`・`Utf8Writer`・`Result`・`BufferFull` は、`Device` が入れ子の Type を宣言していないので影響を受けない。
- **完了済みのプログラムと Kimi ライブラリ：** 基底節を持つ struct はない。

**実装への注記**

- `Binding.Types.cs` の `Lookup`（73-117）：struct のスコープで、自分の層に eligible な宣言がなければ、基底の層を宣言だけで検査する。層の歩みは `LookupTypeMember`（`Binding.MemberLookup.cs:124-213`）のものを、引数の置換（`StoredType`）を行わずに使う。総称パラメーターを飛ばす処理（:144）は、そのまま使える。基底節を束縛している間は、その struct の検査を行わない。
- `Binding.Access.cs:443-450` は、受け手のない protected インスタンスメンバーを inaccessible と判定する。検査には、domain だけで判定する方法が要る。
- missing receiver には、専用の失敗種別がない（`BindingModel.cs:87-99` の `BindingFailure`）。今は、受け手のないインスタンス候補を Inapplicable にしているだけである（`Binding.Calls.cs:1027-1031`）。次のものを加える。
  - 失敗種別 `QualificationRequired`
  - code（`src/Kimi/Diagnostics/DiagnosticCode.tinyhand`）
  - 要件の語彙と目録の行
- `RepairKind.tinyhand` に `Qualify` を、`DiagnosticRepair.cs` の `RepairCondition` に `Selection` を加える（docs/dev/DIAGNOSTICS.md §4.5）。CODEMAP の §9 の行も更新する。
- この作業は P25 の単位に入れる。テストには次のものを含める。
  - 隣接する形：裸の `marker()`・`count`・`Node`、Field 初期化子、`: base(...)` の引数、入れ子の group と struct、基底節を持たない断片、外部 Kotonoha の基底、private な基底の名前（止まらないこと）、基底節の例外、`struct S : S.N` の循環、二つの名前空間、受け手を持つ関数を宣言した enum の missing receiver
  - 修飾子ごとに、候補を提示する場合と提示しない場合の反例（構築の受け手を含む）

### 3.4. 精密化の一つのモデル：地点ごとに一つの Effective Type、対象の補完、事実の積（P33、P38）

P33（と P38）の実行時 `is` 検査と流れによる精密化（SPEC §13.6.1、§14.10）を、次の三つの考え方にまとめる。

1. **地点ごとに一つの型。** ある地点で、名前の値の静的な型は Effective Type 一つだけである。値の使用はすべてそれを使う。宣言型を保つのは、束縛のスロットと、それを通す値の別名と、捕捉だけである。
2. **対象の補完は一か所。** `is` と checked cast の対象には Origin を書かない。束縛は SPEC §13.6.2 の一つの段落で決める。
   - 対象が被演算子の View Target かその基底なら、その束縛を取る。
   - そうでないと証明されれば、Owned erasure の証明（SPEC §15.8.1）によって、固定 Origin 束縛は `static` になる。補完した型は Owned でなければならない。
   - 総称本体で関係が未決なら、補完しない。
   - この証明が object の寿命の間保たれるように、payload に書き込める handle（`obj`・`objuniq`）は View Target について不変とする。
3. **事実は集合の積。** 事実は、取りうる Dynamic Type の集合である。経路上では積をとり、合流では和をとる。
   - 空の集合は、合流で何も制約しない。まだ到達していない後退辺も同じ扱いである。
   - ループの解は一意で、処理順序によらない。

| 項目 | マイルストーンへの影響 | 主な変更節 |
| --- | --- | --- |
| Q12 規則 1（Effective Type とその使用） | P33:45 の `view@follow@uniq` と P38:32 の `view@follow@ref` がこの規則に依存する | SPEC §14.10.1 |
| Q12 規則 2・3（対象の形と補完） | 近傍の形だけに関わる。P33・P38 の対象 `Leaf`・`Other`・`Lamp` は Origin も型引数も持たない | SPEC §13.6.1、§13.6.2、§15.4.2、§15.8.1 |
| Q12 規則 4（object handle の変性） | P33 の精密化を健全にするために要る。object handle で不変なのは `obj` と `objuniq` だけで、Origin を持つ View Target の `obj`・`objuniq` を使うマイルストーンはない（P34:45 の `rc/Counter` は共変のまま）ので、受理は変わらない | SPEC §15.3.5（本書 3.5.1 と共通の文案） |
| Q13（事実と合流） | 影響なし。どの読み方でも P33・P38 の型付けは同じ | SPEC §14.10.2、IMPL §A.9 |

P33・P38 のソースは変えない。Q12 による 14:828 の書き直しは、精密化したパラメーターに直接 `@follow` を適用する P33:45（`view@follow@uniq`）と P38:32（`view@follow@ref`）に要る。本書 6 の Q11 のとおり既存の文からも導けるが、読み分けをなくすために書き直す。Q13 は P33 の型付けを変えない。Q12 の対象の補完の段落は、将来の checked cast（付録 D）とも共有し、Owned の証明（SPEC §15.2.3）に依存する。Q12 の規則 4 は、本書 3.5.1（Q19）の変性の規則と同じ一つの規則であり、SPEC §15.3.5 には一度だけ書く。

#### 3.4.1. Q12. `is` の対象の形と、精密化した View Target の Origin

**現状と問題**

1. **Effective Type の使い道が、閉じた列挙になっている。**
   - SPEC §14.10.1 は「The Effective Type is used for member lookup, argument applicability, overload resolution, assignment sources, results and local inference」と列挙する（14-control-flow.md:828）。明示操作（SPEC §13.5）の被演算子は入っていない。
   - P33:45 の `view@follow@uniq` と P38:32 の `view@follow@ref` は、精密化したパラメーターに直接 `@follow` を適用する。
   - SPEC §13.5.5.1 は、`@follow` を次の場合だけ許す（13-operators-and-assignment.md:465）。View Target が「exactly the complete payload Type `T`, including internal Origins」であり、かつ `T is Kimi.Sealed` が Proven であること。
   - 被演算子に宣言型 `objuniq/Base`・`objref/Device` を使う実装は、どちらも open な基底なので、この二行を拒否する。Effective Type を使う実装は受理する。
   - P33:32〜33 は新しい束縛 `narrowed` を経由するので、「local inference」で決まる。P33:45 と P38:32 は決まらない。
   - 検証では、この点は既存の文（14:824 の定義、13:720、12:237、07:174）から導けるとも判断された（本書 6 の Q11）。本書は、読み分けをなくすため 14:828 を規則 1 の文で書き直す。
2. **対象の Origin を決める規則がない。**
   - SPEC §13.6.1 は、対象を「with resolved Type arguments, but without Semantics, Origin, binding name or requirement composition」とする（13:720）。そのうえで、精密化がその object 形を被演算子の Effective Type にすると述べる。
   - しかし、次の位置を何が束縛するかは書かれていない。
     - 対象自身の Origin の位置
     - 基底節から継承する位置
     - 型引数の中の借用層（`Box<ref/i32>` など）
   - `static` を補う規則は checked cast にしかない。13:758 の「a missing fixed Origin binding may be supplied as `static` only where the §15.2.3 proof covered that binding」と、15-ownership-and-lifetime-analysis.md:1021 である。
   - SPEC §15.4.2 の位置の表には、対象の行がない。SPEC §15.4.4 は「Uncertainty never becomes an invented `static`」とする（15:662）。
   - 実行時の同一性は Origin を消す（IMPL §21.2.1 の `ArgKey(A) = remove every Origin`、21-layout-runtime-and-code-generation.md:184）。実行時に Origin を回復する手段もない。
   - そのため、実装が分かれる。ある実装は checked cast に倣って `static` を補う。別の実装は位置が未束縛だとして、使用か検査を拒否する。
3. **「Unresolved Type parameters」の範囲が二通りに読める。**
   - 13:720 の「Unresolved Type parameters, associated Types and non-struct targets are outside this initial syntax」は、裸の対象 `T` だけを除くとも読める。`T` を含む `Leaf<T>` まで除くとも読める。
   - 実装は、型パラメーターに依存する対象をすべて `UnsupportedBinding_Kd` にする（src/Kimi/Compiler/Binding/Binding.RuntimeTypeTests.cs:58-61）。裸の `T` はどちらの読み方でも仕様が除く形なので、Unsupported で報告するのは SPEC §23.3.6.1 に反する。同節は「A form this specification permits but the implementation does not support is reported with an `Unsupported` code, never with a `Language` code」と定める（23-compiler-services.md:139）。
   - 書かれた Origin は、最上位のものしか拒否していない（同ファイル :63）。
4. **素朴な補完は健全でない。** 「書かれていない Origin はすべて `static`」とすると、総称パラメーターを通して入る Origin を見落とす。

   ```kimi
   open struct Base
       protected init() => ()

   struct Slot<U> : Base
       public var value: U
       public init(value: U) : base() => self.value = value@move

   func put<T>(view: objuniq/Base, value: T)
       if view is Slot<T>
           view.value = value@move
   ```

   - 呼出し側は、`Slot<ref/i32 during static>` を消去した `obj/Base` を持っているとする。この呼出し側が `T = ref/i32 during local` で `put` を呼ぶ。
   - Runtime Type Identity は Origin を無視する（IMPL §21.2.1 の表、21:195 の「`Box<ref/i32 during a>` and `Box<ref/i32 during b>` | Equal」）。そのため、検査は成功する。
   - 精密化した `objuniq/Slot<ref/i32 during local>` は、局所の借用を格納する。そのオブジェクトは、他の保持者からは `Slot<ref/i32 during static>` に見える。`put` から戻った後、格納した借用はぶら下がる。
   - 消去の時点で証明されているのは、payload が Owned であることだけである（SPEC §15.8.1、15:1013）。
5. **書き込める object handle の変性が決まっていない。そのため、補完がなくても精密化が健全でない。**
   - SPEC §15.3.5 は `ref`・`uniq`・`raw` の変性を定める（15:547）。object handle については「Mutable storage follows its representation's invariance requirements」があるだけである。
   - 実装は `uniq`・`objuniq`・`raw` だけを不変とし、`obj` を View Target について共変にしている（src/Kimi/Compiler/Binding/Binding.TypeRelations.cs:45, :150）。
   - `obj` が共変だと、消去済みの基底 view の束縛を短くして、payload に短い借用を書き込める。その後の精密化は、派生型の宣言から `static` を読む。

   ```kimi
   open struct Base {source}
       public var number: ref/i32 during source
       protected init(number: ref/i32 during source) => self.number = number

   struct Leaf : Base{b}
       origin b.source == static
       public init(number: ref/i32 during static) : base(number) => ()

   func leak(handle: obj/(Base during static), number: ref/i32) -> ref/i32 during static
       var shortened: obj/(Base during number) = handle@move // Valid only while obj/T is covariant.
       shortened.number = number
       let view = shortened@move
       require view is Leaf else => $abort("Not a leaf")
       return view.number // Leaf fixes source = static, but the payload holds number.
   ```

   - `Leaf` は Origin を持たないので、この穴は補完の規則によらない。P33 と同じ、Origin のない対象への精密化だけで起こる。
   - 現行の checked cast の `static` 補完（13:758）と、「later upcasts and casts inherit that certification」（15:1021）も、同じ前提に頼っている。

**決定案**

規則 1（SPEC §14.10.1、Effective Type とその使用）。
- 精密化できる名前の Effective Type は、宣言型の View Target を、その地点の Effective Core に置き換えた型である。
  - Effective Core が宣言の Core なら、束縛は宣言のままである。
  - より派生した Core なら、束縛は規則 3 の補完で決まる。つまり、固定 Origin 束縛はすべて `static` になる。
  - ハンドルの Semantics と外側の Origin、Loan、可変性、初期化状態、同一性、破棄責任は保つ。
- 名前の**値の使用はすべて** Effective Type を使う。従来の列挙（メンバー検索、引数の適用可能性、多重定義解決、代入の source、結果、局所推論）に、次を加える。
  - 明示操作（SPEC §13.5）の被演算子。`@follow`、`@move`、`@copy`、`@objref`、`@objuniq`、upcast がこれにあたる。
  - 暗黙の receiver の取得（SPEC §7.3）
  - 実行時 `is` 検査の被演算子
- 束縛の**スロット**を指す操作だけは、宣言型を使う。
  - スロットを借用する `@ref`・`@uniq` と、スロットのアドレスをとる `@raw` である。SPEC §13.5.5.2 と §5.4 は、これらを格納型で型付けしている。
  - スロットの借用を通した読みは値の別名なので、事実を受け継がない。
- 捕捉も、従来どおり宣言型を使う（SPEC §14.10.2）。
- 既存の但し書きは保つ。各候補には通常の適合を適用する。確定済みの宣言と代入先の型は変わらない。通常必要な明示 upcast は、精密化後も必要である。

規則 2（SPEC §13.6.1、`is` の対象の形）。右辺は、名前付きの struct Core 一つである。修飾してよい。Semantics・束縛名・要件の合成は持たない。
- 型引数は、Origin を除いて完全な型である。囲む総称パラメーターと関連型を含んでよい。
- 対象のどこにも Origin・`during`・束縛集合を書かない。型引数の中も、型の alias を展開した後も同じである。束縛は規則 3 が与える。
- 対象そのものが型パラメーター・関連型・struct でない Core のときは、Language エラーとする。総称本体の検査（SPEC §8.10）では、それが struct Core であることを確立できないからである。
- 対象の object 形は、従来どおり形成できなければならない（SPEC §8.4.7.2）。ObjectPayload は外側の構造だけで判定するので、この検査は補完によらない。
- checked cast には綴りがなく、Contract view も対象にできる（SPEC 付録 D）。そのため、規則 2 は `is` だけに適用する。checked cast と共有するのは、規則 3 の補完である。

規則 3（SPEC §13.6.2、対象の補完）。`is` 検査、精密化、checked cast に共通の段落とする。
- S を被演算子の View Target、D を対象とする。精密化した名前の S は、規則 1 により Effective Type の View Target である。
- S と D の関係は、`Supports`（SPEC §3.3.5）で判定する。比較は Runtime Type Identity で行う。Origin は無視し、型引数は含める。総称本体では、許されるすべての束縛について判定する（SPEC §8.10）。

| S と D の関係 | D の束縛 | `is` 検査 | checked cast |
| --- | --- | --- | --- |
| D が S か S の基底だと証明される | S が与える束縛。S 自身の束縛か、S の中で D が基底として現れる位置の束縛 | 常に真。事実を加えない | 常に成功し、upcast と同じ束縛を持つ |
| D が S でも S の基底でもないと証明される | D とその型引数の中の固定 Origin 束縛をすべて `static` にする。補完した D は Owned でなければならない | Q13 規則 1 に従う | 成功した結果は、補完した D を持つ |
| 未決（総称本体だけで起こる） | 補完しない | 事実を加えない（Q13 規則 1）。Owned の証明は要らない | 拒否する |

- 2 行目が健全である理由は次のとおりである。
  - 単一継承（SPEC §6.2.2）では、このとき検査や cast が成功するのは、Dynamic Type が S より真に派生している場合だけである。
  - そのような object は upcast で消去されており、消去は payload の Owned を証明している（SPEC §15.8.1）。Contract view も、消去でしか作れない。
  - その証明は、規則 4 により object の寿命の間保たれる。
- 総称本体では、Owned を宣言された Constraint から証明する。証明できない対象は、upcast の消去と同じく Owned の証明の失敗として、対象の位置で報告する（コードと表示は本書 3.2.1 の R4 に従う）。
- どの場合も、次を守る。
  - 非 static の束縛は作らない。
  - 呼出しごとの callable Origin は束縛しない（SPEC §15.2.3）。
  - ハンドルの Semantics、外側の Origin、Loan は保つ。

規則 4（SPEC §15.3.5、object handle の変性）。
- `objref/T`・`rc/T`・`arc/T` は、`ref/T` と同じく View Target `T` について共変である。これらは payload を共有でしか読めない。
- `obj/T`・`objuniq/T` は、`uniq/T` と同じく `T` について不変である。どちらも payload に書き込めるからである。
- `objref`・`objuniq` の外側の Origin は、`ref`・`uniq` と同じく共変である。
- これで、消去で証明した Owned は、どの view を通して payload を書き換えても保たれる。規則 3 の `static` と、派生型の宣言にある束縛は、その証明を読むだけになる。問題 5 の `leak` は、`shortened` の初期化で拒否される。
- SPEC §15.3.5 の文案は、本書 3.5.1（Q19）の決定案 1 に一つにまとめて示す。

**例**

```kimi
open struct Base
    public let id: i32
    protected init(id: i32) => self.id = id

struct Note {source} : Base
    public let text: ref/string during source
    public init(text: ref/string during source) : base(1) => self.text = text

struct Slot<U> : Base
    public var value: U
    public init(value: U) : base(2) => self.value = value@move

func readNote(view: objref/Base) -> ref/string during static
    require view is Note else => $abort("Not a note")
    let payload = view@follow@ref // Rule 1: the operand is objref/(Note during static).
    let handle = view@ref         // ref/(objref/Base): the slot keeps its declaration Type.
    return payload.text           // Owned erasure certified source = static.

func put<T>(view: objuniq/Base, value: T)
    T is Owned                    // Required: Slot<T> is proven neither view's View Target nor its base.
    if view is Slot<T>
        view.value = value@move   // Sound: the payload erased to Base was Owned.

func probe<T>(view: objref/Base, slot: objref/Slot<T>)
    if slot is Slot<T> => ()      // The View Target itself: takes slot's bindings; no Owned proof.
    if slot is Base => ()         // A base of the View Target: always true, adds nothing.
    if slot is Slot<i32> => ()    // Undecided in this body: no completion and no fact.
    // if view is Note during static => ()         // Error: no Origin is written in a target.
    // if view is Slot<ref/i32 during static> => () // Error: nor inside its Type arguments.
    // if view is T => ()                          // Error: a Type parameter is not a struct Core.
    // if view is Slot<T> => ()                    // Error: Slot<T> is not proven Owned.
```

**最小で一貫している理由**

- **Principle 1。** 対象の書き方は一つである（Origin を書かない）。補完の規則も一つで、`is` 検査、精密化した型の束縛（規則 1）、checked cast がこれを共有する。新しい構文も用語も要らない。
- **Principle 2。** 束縛は、被演算子の静的な View Target と対象だけで決まる。実行時の Origin の照会も、呼出し側の情報も要らない。単相化しても、インスタンスごとに同じ判断になる。一つの名前は、一つの地点で一つの型だけを持つ。
- **Principle 3。** 総称の対象が Owned を必要とするときは、`T is Owned` をシグネチャーに書く。これは、そのオブジェクトを基底に消去した場所で、すでに必要だった証明と同じである。
- **既存概念の再利用。** 次の既存の概念をそのまま使う。
  - Owned erasure の証明（SPEC §15.8.1）
  - checked cast の「非 static の束縛を作らない」規則（SPEC §13.6.2）
  - `Supports`（SPEC §3.3.5）
  - Origin を除いた同一性（IMPL §21.2.1）
  - OwnedOrigins の閉包（SPEC §15.2.3）
  - 共有なら共変、書き込めるなら不変という、`ref`・`uniq` の変性の二分（SPEC §15.3.5）
- **既存規則との一致。** スロットの扱いは、SPEC §13.5.5.2 の「A written `V` must be exactly the normalized stored Type」と一致する。値の別名に事実を移さないという規則（SPEC §14.10.1）からも導かれる。

**採らなかった案**

| 案 | 退けた理由 |
| --- | --- |
| Origin のない対象だけを精密化する | Origin を持つ型では「検査してから使う」ができない。checked cast とも食い違う。問題 5 の穴も残る |
| 対象に Origin を書かせる（`is Note during static`） | 13:720 の「without … Origin」と矛盾する。書ける束縛は補完の結果と同じなので、同じ意味に二つの綴りができる |
| 書かれていない Origin だけを `static` にする | 総称パラメーターから来る Origin を見落とすので、健全でない（問題 4 の `put`） |
| D が S から派生するとき、D の基底節にある S の出現を S の束縛に合わせて、D の束縛を決める | `objref` の S は共変な短縮を経ていることがあり、payload より短い束縛になる。その View Target は payload と正確に一致しないので、`@follow`（13:465 の「including internal Origins」）が使えない。D のほかのスロットには、結局 `static` が要る |
| Origin も含めて S と D を比べる | `objref/Box<ref/i32 during a>` を `is Box<ref/i32>` で検査したとき、`static` と読み替えてしまう |
| 未決の関係を表の 2 行目に含める | checked cast では、D が S と一致するインスタンスで、消去されていない payload に `static` を補うことになり、健全でない。`is` 検査では、使われない補完のために Owned を要求するだけになる |
| `Leaf<T>` を除外して Language エラーにする | 総称の階層が自分の型を検査できなくなる。単相化では型引数が具体的になるので、除く理由がない |
| 明示操作の被演算子だけ宣言型のままにする | P33:45 と P38:32 に余分な `let` が要る。一つの名前が一つの地点で二つの型を持つことになる |
| スロットの借用にも Effective Type を使う | SPEC §13.5.5.2 はスロットの借用を格納型で型付けする。スロットの借用は値の別名であり、ハンドル表現が View によらないことも仮定することになる |
| `obj/T` を共変のままにし、消去済みの view への書き込みを別に禁じる | 消去済みかどうかは、静的な型からはわからない（`obj/Base` は Base そのものも持てる）。変性で書けば、`uniq/T` と同じ一つの規則で済む |

**変更する節**

- **SPEC §13.6.1：** 対象の形を規則 2 にする。「Unresolved Type parameters …」の文を置き換える。
- **SPEC §13.6.2：** 「Target completion」の段落（規則 3 の表）を加える。
  - 「For a source certified by Owned payload erasure, a missing fixed Origin binding may be supplied …」の文は、この段落を参照する文にする。
  - 「a cast to a concrete `Box<ref/i32 during static>`」の例は、「a cast to `Box<ref/i32>`, completed to `Box<ref/i32 during static>`」に直す。
- **SPEC §14.10.1：** 828 行の列挙を、規則 1 の使用の規定で置き換える。824 行の「Declared Semantics, Origins, … are preserved」は、外側の Origin と Core の束縛を区別して、規則 1 の定義に合わせて書き直す。
- **SPEC §15.3.5：** 547 行の変性の列挙を、規則 4 を含む本書 3.5.1（Q19）の文案で置き換える。
- **SPEC §15.4.2：** 位置の表に「Runtime test or checked-cast target | Target completion (§13.6.2); no Origin is written」の行を加える。
- **SPEC §15.8.1：** 1021 行の「Only proof-covered fixed Origin bindings may be supplied as `static` in a checked cast (§13.6.2)」を、「in the target completion of runtime tests and checked casts (§13.6.2)」に広げる。証明が保たれる理由として、規則 4（§15.3.5）を参照する文を加える。

**実装への注記**

src/Kimi/Compiler/Binding/Binding.RuntimeTypeTests.cs:58-66 を次のように直す。
- 裸の型パラメーターと関連型の対象は、Language エラー（`InvalidTypeFormation_Kd`）にする。
- 書かれた Origin は、どの深さでも拒否する。
- 補完した対象の Owned は、upcast の消去と同じ経路で証明する。この経路は Binding.ObjectViews.cs:41-45 の `ProveOwned` と `RequireConstraint` である。Advice として、`T is Owned` の宣言を条件付きで示す。
- Owned が証明された `Slot<T>` は、実装するまで `UnsupportedBinding_Kd` のままとする。

src/Kimi/Compiler/Binding/Binding.TypeRelations.cs:45 と :150 の不変な Semantics に `Obj` を加える。問題 5 の `leak` を、`shortened` の初期化で拒否される回帰として残す。`obj` の View Target を短くする既存の焦点テストがあれば、この変更のときに見直す。

ほかの影響は次のとおりである。
- 規則 1 で、明示操作の選択も被演算子の Effective Type に依存するようになる。IMPL §A.3 のキャッシュキー（A-compiler-requirements.md:89）では、これを「refinement affects input Effective Types」の対象に含める。
- P33・P38 の対象には Origin も型引数もないので、補完した型は自明に Owned で、束縛も変わらない。
- 既存規則はそのままである。精密化した名前を基底型のパラメーターへ渡すには、従来どおり明示 upcast が要る（SPEC §14.10.1、§10.2、§13.5.7）。本案はこれを変えない。

**マイルストーンの書き換え**：なし。

#### 3.4.2. Q13. 矛盾する事実と、合流・ループの解

**現状と問題**

SPEC §14.10.2 は次のように定める（14-control-flow.md:859, :868）。
- 「Incompatible facts on one path revert that binding to declaration-Type checking; they neither make the path unreachable nor prove arbitrary Types.」
- 「At joins, the most derived base guaranteed by every reachable incoming path is retained, never wider than the declaration Type.」
- ループは「solved to a stable result independent of processing order」とする。

IMPL §A.9 も「Short-circuit and loop joins must not depend on analysis order」を求め（A-compiler-requirements.md:149）、「contradictory facts」の試験を求める（:151）。

1. **「revert」が二通りに読める。**
   - (a) その経路の状態を宣言型に戻し、後の検査で再び狭める。
   - (b) 両立しない事実が残り、後の検査をしても狭まらない。

   `view` が Leaf に精密化された後で、`require view is Other else => return`、`require view is Leaf else => return`、`view.extra` と続けるとする。(a) では受理され、(b) では拒否される。(a) では `is T` の遷移が単調でない。Leaf の入力からは Base が出るが、Base の入力からは Other が出る。
2. **ループの解が一つに決まらない。**
   - 矛盾がなくても決まらない。継承が Base > Mid > Leaf、入口の状態が Leaf、本体が `require view is Mid else => exit` のとき、ループの入口の状態は Leaf でも Mid でも安定する。
   - 読み方 (a) で、本体が Other と Leaf を検査するループでは、Leaf と Base がどちらも安定する。ループ先頭の `view.extra` が受理されるかどうかは、どちらの解を選ぶかで変わる。
   - 「independent of processing order」という要求は、どの安定解をとるかを決めていない。
3. **死んだ分岐が合流を広げる。**
   - 現在の文では、`if view is Other => Console.writeLine("…")` の真の経路は宣言型だけを保証する。
   - 経路は刈り込まない（SPEC §14.9.2、14:771「Paths are never pruned using … dynamic-Type contradictions」）。そのため、合流で Leaf の精密化が失われる。
   - これは現在の規定どおりの振る舞いである。変えるなら、意図した仕様変更になる。

P33・P38 は、どの読み方でも同じように型付けされる。
- P33:31 の矛盾は、`$abort` だけの失敗本体にある。そこでは `view` を読まない。真の経路は、14:852 の「Retain」により Leaf のままである。
- P38:31〜33 には矛盾がない。

それでも、P33 の精密化を実装するには、IMPL §A.9 の「contradictory facts」の試験のために一つの答えが要る。

**決定案**

事実は、束縛が取りうる Dynamic Type の集合（Runtime Type Identity の集合）を表す。束縛は宣言型、つまり宣言の Core とその派生から始まる。新しい用語は導入しない。

規則 1（SPEC §14.10.2、経路上の事実）。`x is D` の事実は、現在の Effective Core S に対して次のように働く。関係は Q12 規則 3 と同じく Runtime Type Identity で判定し、総称本体では許されるすべての束縛について判定する（SPEC §8.10）。
- D が S から真に派生すると証明されるときは、D に狭める。
- D が S と無関係だと証明されるとき（どちらも他方を Supports しないとき）は、取りうる Dynamic Type は残らない。
- それ以外のときは、何も加えない。D が S かその基底である場合と、総称本体で関係が未決の場合がこれにあたる。
- 取りうる Dynamic Type のない束縛は、その経路では宣言型で検査する。以後、その経路での検査は何も加えない。
- 経路は到達可能なままである。ほかの Flow State の成分は、それぞれの規則と合流を保つ（14:859 の「Other Flow State components keep their own joins」）。初期化、Move、Loan、cleanup、Structural Completion、結果の source、`require` の失敗の検証がこれにあたる。
- 矛盾だけなら警告してよいが、エラーではない（従来どおり）。

単一継承（SPEC §6.2.2）では、「D かその派生」の集合どうしは入れ子か、互いに素である。そのため具体型では、この規則はちょうど集合の積になり、一つの経路の上での検査の順序によらない。`x is not D` の偽の経路も、同じ事実を加える（14:852 の表どおり）。

規則 2（SPEC §14.10.2、合流とループ）。
- 合流では、取りうる Dynamic Type を持つ到達入力経路のすべてが保証する、最も派生した基底を保つ。宣言型より広くはしない（従来どおり）。
- 取りうる Dynamic Type のない入力経路は、その束縛については何も制約しない。結果が空になるのは、すべての入力経路が空のときだけである。
- ループの入口は、最初の入口の状態から始める。後退辺は、到達するまで何も寄与しない。入口の状態は広がるだけで、安定するまで繰り返す。こうして得る最も精密な安定解は一意で、処理順序によらない。
- 「a previous successful iteration alone is insufficient」の文は保つ。

補足：現在の言語では、精密化についてループの入口は最初の入口の状態そのものになり、反復は要らない。
- 精密化できる束縛は、`let` とパラメーターである。パラメーターは再代入できず（SPEC §7.2）、`let` は再初期化できない（SPEC §13.7.1）。どちらもループの中で値が置き換わらない。
- 各検査の出力は入力に含まれるので、後退辺の状態は入口の状態より広くならない。
- 規則 2 のループの文は、他の Flow State の成分と同じ一般形を保つために書く。

**例**

Base、Leaf、Other は Milestone33.kimi:11-25 の宣言である。

```kimi
func afterDeadBranch(view: objref/Base) -> i32
    require view is Leaf else => return 0
    if view is Other
        Console.writeLine("Unreachable at run time.") // No possible Dynamic Type: view is checked as Base.
    return view.extra   // Accepted: the contradicted branch constrains nothing at the join.

func insideContradiction(view: objref/Base) -> i32
    require view is Leaf else => return 0
    require view is Other else => return 1 // Always false; a warning is allowed.
    require view is Leaf else => return 2  // Adds nothing on this path.
    return view.extra   // Error: view is checked as Base; the report relates both incompatible tests.

func loopEntry(view: objref/Base) -> i32
    require view is Leaf else => return 0
    var total = 0
    loop
        total += view.extra                // The loop entry keeps Leaf from its first entry.
        require view is Other else => exit
        require view is Leaf else => exit
    return total
```

総称本体の例を挙げる。`open struct Tree<U>` と `struct Branch<U> : Tree<U>` があり、`view: objref/Tree<T>` とする。
- `view is Branch<i32>` は、`T` が `i32` のときだけ `Tree<T>` の派生になる。
- この関係は未決なので、何も加えない。補完もしないので、Owned の証明も要らない（Q12 規則 3）。

**最小で一貫している理由**

- **一つの概念。** 取りうる Dynamic Type の集合という一つの概念で、経路上の結合、矛盾、合流、ループを説明する。新しい用語も状態も要らないので、付録 E は変えない。空の集合が合流で何も制約しないことは、既存の合流の文（「guaranteed by every reachable incoming path」）を集合として読んだときの帰結でもある。
- **解の一意性。** 各検査の出力は入力に含まれる。そのため、後退辺が何も寄与しない状態から始めると、最初の入口の状態がそのまま安定する。どの安定解も最初の入口の状態を含むので、これが最も精密な安定解であり、一つしかない。処理順序にもよらない（Principle 2、IMPL §A.9）。この議論は検査の単調性を使わないので、総称本体の未決の関係があっても成り立つ。
- **局所性。** 具体型の場合、経路上の結果は検査の順序によらない。読み手は、その経路にある検査の集合だけを見ればよい。
- **明示性。** 死んだ経路で任意の型を証明しない（Principle 3）。そこでの誤りは宣言型で検査する。診断は、両立しない二つの検査を関連位置として示す。

**採らなかった案**

| 案 | 退けた理由 |
| --- | --- |
| 読み方 (a)（宣言型に戻して再び狭める） | 単調でない。ループに複数の安定解ができ、どれを選ぶかを別に決める必要がある |
| 最後の事実で置き換える | 死んだ経路で検査した型を証明してしまう（14:859 の「nor prove arbitrary Types」に反する）。死んだ分岐による合流の拡大も残る |
| 最初の事実を優先する | 単調でない。Leaf の入力からは Leaf が出るが、Base の入力からは Other が出る |
| 矛盾をエラーにする | P33:31 に反する。SPEC §13.6.1 の「Well-typed tests are accepted even when static information proves them always true or always false」（13:732）にも反する |
| 新しい状態名 Contradicted を導入する | 集合として読めば、空の集合で足りる。用語が増えるだけである |
| 宣言型から始めて、最も広い安定解をとる | 入口ですでに分かっている精密化を失う |
| 総称本体で未決の関係を「無関係」とみなす | その経路は合流で空として無視される。実際には空でないインスタンスで健全でない |

**変更する節**

- **SPEC §14.10.2：** 859 行の段落を、規則 1 と規則 2 の合流の部分で置き換える。868 行の最後の文を、規則 2 のループの部分で置き換える。
- **SPEC §14.10.3：** 規則は変えない。「keeping only common guarantees at joins」はそのまま成り立つ。
- **IMPL §A.9：** 149 行を「Short-circuit and loop joins compute the most refined stable solution of SPEC §14.10.2, independent of analysis order」に直す。151 行の試験に、次の五つを加える。
  - 短絡演算の被演算子が矛盾している場合
  - 合流の前に死んだ分岐があっても、精密化が残ること
  - 矛盾した経路で再び検査しても、宣言型のままであること
  - ループ本体で再び検査しても、入口が一意に Leaf になること
  - 総称本体で未決の関係が、何も加えないこと

**実装への注記**

- 精密化の状態は、束縛ごとに「Effective Core」か「空」のどちらかを持てば足りる。
  - 合流は、最も派生した共通基底をとる。空は単位元である。
  - 検査は規則 1 に従う。
  - ループの入口は、最初の入口の状態をそのまま使える。
- 空の経路で、宣言型による検査が失敗した場合を考える。このとき診断は、両立しない二つの検査を関連位置として示す（SPEC §23.3.6、docs/dev/DIAGNOSTICS.md §11 の P33 の行）。任意の警告だけでは原因がわからないからである。
- 静的に結果が決まる検査に対する任意の警告は、P33:31（`view is not Other`）と P33:51（`view is Leaf`）にも当たる。警告を出すかどうかは実装の方針であり、本案は決めない（本書 7）。

**マイルストーンの書き換え**：なし。

### 3.5. アクセスの種類が効果を決める：共有アクセスは書き込まず、書き込める別名と view は不変である（P34、P35、P38）

本テーマの三項目（Q19、Q18、Q09）は、既存の一つの不変条件から導く。新しい概念は加えない。規則は次の七つにまとめる。

| # | 規則 | 項目 | 主な変更節 |
| --- | --- | --- | --- |
| 1 | 共有アクセスは書き込まない（共通の前提） | 共通 | SPEC §15.6.2、§8.4.10.4 |
| 2 | 対象に不変なのは `uniq`、`obj`、`objuniq`、`raw` だけである。宣言の変性では、pair 層を共変に数える | Q19（本書 3.4.1 規則 4 と共通） | SPEC §15.3.5 |
| 3 | 格納 Field を持たないコンパイラー管理の型は、その型自身の規則に従う。`Loan<T>` と `Weak<S>` は、引数を格納しているものとして解析する | Q19 | SPEC §3.2.2、§15.3.5 |
| 4 | オブジェクトへの Borrow は、経由したハンドル Place の Loan を保つ。ハンドルを破棄しても、破棄されるのはそのハンドル Place だけである | Q18 | SPEC §13.5.5.1、§16.3.3、§15.7.3 |
| 5 | 受け手が影響を受けるのは、受け手を通した操作だけである。そのため、`ref/Self` の受け手は宣言が有効なら Proven である | Q09 | SPEC §12.4.4.2、§11.2.1 |
| 6 | 排他入力の上界：呼出し先が排他入力に及ぼす効果は、実際の対象を完全な値で一度置き換えることを超えない | Q09 | SPEC §12.4.4.2 |
| 7 | 範囲：ObjectCallCompatible の第 2 段階のうち、一つのビルドの中で行う部分を現在の範囲にする | Q09 | SPEC 付録 D |

入れる順序は次のとおりである。

- Q19 と Q18 は、P34（U4〜U8）で先に入れる。どちらも主に実装の現状を記録するもので、ハンドルは `uniq`・`objuniq`・`raw` を除いて共変として扱われ、payload の Loan はすでに自分のハンドルを固定している。ただし Q19 は、`obj` を不変にする点で実装を変える（本書 3.4.1 規則 4）。
- Q19 は、P35 で Weak を宣言する（G4）前に要る。そうしないと、Weak のスロットは Binding.OriginRequirements の既定によって Invariant に閉じる。Loan と同じ特例が要る。
- Q18 は、解放を SPEC §15.6.2 のハンドル Place で定める。本書 3.6.4（Q25）は、同じ表の根の行を後で一般化する。表は一貫して書き換え、Q18 を先に（P34）、Q25 を後に（P36）入れる。Q25 は Q18 の結果を変えない。
- Q09 は二つに分かれる。
  - 共有受け手の補題（規則 5）は、Q18・Q19 を述べた後ならいつでも入れられる。これで P38:44、:47 と、P25 に隣接する基底の `ref/Self` メンバーが通る。
  - 排他受け手の第 2 段階の単位は、付録 D.5.1 が「implementation begins only on explicit instruction」とするので、ユーザーの明示の範囲指示が要る。P36 の後・P38 の前に置く。この位置なら、SPEC §12.4.4.2 の記憶域の関係が本書 3.6.4（Q25）の静的な根を使える。P38:43 のセッターは、その自明な場合である。
  - README:1950 の別検査は、排他受け手の NotProven の場合に書き換える。

**共通の前提（規則 1。SPEC §15.6.2 に一度だけ書く）。**

- Place に書き込み、Move、排他借用、破棄を行えるのは、次の二つの場合だけである。
  - 直接の経路か排他参照を通す場合（SPEC §3.4 の経路の分類）。
  - 権限の範囲内で raw アクセスを行う場合（SPEC §5.2.1 の条件 3）。
- 共有の層（`ref`、`objref`、`rc`、`arc`）より先には、読取りと共有借用しか届かない（SPEC §13.5.5.1 の表、13:472）。効果要約（SPEC §15.6.4）と要件呼出しの効果（SPEC §8.4.10.4 規則 1）も同じである。
- 参照カウントと Weak の管理領域は管理状態であり、Place ではない。共有借用を通した `clone` と `downgrade` は、Place に書き込まない。

規則 2〜6 は、この前提と単一スレッド実行（SPEC §22.2.3、22:337「This revision admits one execution thread」）から導く。健全性の但し書きは、SPEC §15.6.2 に一度だけ書く。IMPL §21.5.5 の「Future interior mutability or concurrency requires revisiting the shared proof.」（21:952）は、この段落を参照する形に変える。

取り込み文案（SPEC §15.6.2。15:806 の「This permits shared aliasing or mutation, never both at once.」の直後に置く）：

> **Shared access grants no mutation.** A Place is written, Moved, exclusively borrowed or destroyed only through a direct path or an exclusive reference (§3.4), or by raw access within its authority (§5.2.1). Past a shared layer (`ref`, `objref`, `rc` or `arc`) only reads and shared borrows are reached, in effect summaries (§15.6.4) and requirement-call effects (§8.4.10.4) as well. Reference counts and Weak tables are management state, not Places. Variance (§15.3.5), handle destruction (§16.3.3) and receiver preservation (§12.4.4.2) rest on this rule and on single-thread execution (§22.2.3); a design that adds interior mutability or concurrency (Appendix D.2) must revisit all three.

SPEC §8.4.10.4 規則 1（08:769）は、この前提に合わせて二点を変える。

- 「in the access mode of its parameter」を「in the access mode of its parameter, and only with shared access past a shared layer (§15.6.2)」にする。
- 「Destroying an owned handle argument contributes the effects of §16.3.3.」を加える。

これで、所有の `rc/T` 引数をどの様式で扱うかが決まる。今の規則 1 は、所有の `rc/T` 引数の様式を書いていない。

#### 3.5.1. Q19. オブジェクトハンドルと Weak の変性

**現状と問題**

- SPEC §15.3.5（15:547）が名前を挙げるのは、`ref`、`uniq`、`raw` の三つだけである。
  - 続く「Mutable storage follows its representation's invariance requirements.」は、二通りに読める。
  - 一つは、書込み可能な所有経路を持つ `obj/T` を「可変な記憶域」として不変とする読みである。
  - もう一つは、所有の `T` と同じく共変とする読みである。
- `obj`、`rc`、`arc` の変性を決める規定は、ほかにもない。
  - SPEC §3.3.5（03:330）は「The View Target and payload dependencies remain part of the complete Type.」とだけ書く。
  - SPEC §8.1.1 の表（08:29-35）は、対象と外側 Origin を決めるだけである。
  - `objref` と `objuniq` の変性は、SPEC §3.7（03:771）の「follow the same shared and exclusive rules」から類推するしかない。
- `Weak<S>` の変性も決まらない。
  - SPEC §3.2.2（03:219）は、変性を書いていない。
  - 15:567 は「Only `Slice<T>`, a compiler-managed representation without stored Fields, keeps compiler metadata for them」とする。
  - この文は、SPEC §4.5（04:161）の「The element position preserves Origin variance」とも食い違う。`Array<T>` も格納 Field を持たない、コンパイラー管理の型だからである（src/Kimi/Library/Array.kimi:1-3）。
- pair の出現の変性も書かれていない。例は、SPEC §8.2 の `Container<s/T>`（08:92）である。
  - 一つの読みは、admitted set のうち最も弱い Semantics に合わせる。
  - もう一つの読みは、pair の引数を完全型として比べる。
- 実装の状況は次のとおりである。
  - 対象に不変とするのは `uniq`、`objuniq`、`raw` だけで、ほかは共変として扱う。pair 層（`SemanticsKind.Parameter`）も共変に数える（Binding.TypeRelations.cs:45, 150、Binding.OriginInference.cs:50、Binding.TypeOrigins.cs:332）。
  - `Array` は独自の型の種類なので、成分を共変として扱う。
  - 宣言された型では、格納のない総称スロットを Unused から Invariant に閉じる（Binding.OriginRequirements.cs:87-92）。特例は Slice（35-42 行）と Loan（45-48 行）だけである。
- 阻害の度合いは「明確化」である。残りのプログラムの結果は、どちらの読みでも変わらない。
  - P34:45 では、Origin の源が一つである。
  - P35 と P38 では、Weak の payload が Owned である。
  - 違いが出るのは、固定された Origin どうしを関係付ける形である。例は、パラメーター Origin を結果へ短縮する関数、`Weak<rc/Counter{σ}>` の格納、`Array<rc/Counter>` の要素である。
  - Weak の宣言（PLAN G4、P35）より前に決める。

**決定案**

1. SPEC §15.3.5 の規則文を置き換える。範囲は「The complete-Type variance rules apply: …」から「Mutable storage follows …」までである。続く「Declaration variance is inferred from all occurrences …」以降は残す。この文案は、本書 3.4.1 の規則 4 を含む。

> The complete-Type variance rules apply. Every safe borrow is covariant in its outer Origin. `uniq`, `obj`, `objuniq` and `raw` are invariant in their target: an exclusive borrow writes through an alias whose Type stays fixed, a writable object handle writes a payload whose Dynamic Type keeps the bindings certified at erasure (§15.8.1) even where its View Target does not show them, and a raw pointer does not distinguish reads from writes. Every other Semantics is covariant in its target, as an owned `T` is: owned contents are written only through their owning path, whose Type is exact, and `ref`, `objref`, `rc` and `arc` grant no mutation (§15.6.2). In declaration variance a pair layer counts as covariant, because a pair argument is a complete Type compared under its own Semantics wherever two arguments meet; in a generic body a relation between pair layers must hold for every admitted Semantics (§8.1.2). Function parameters reverse polarity and results preserve it. Mutability does not change variance: a Type declared in Kimigayo derives it from the Types of its storage.

2. SPEC §3.2.2 の「A Weak keeps `S`'s complete View Type …」の後に、次の文を加える。

> For variance, Loan requirements and OwnedOrigins, `Weak<S>` is analyzed as storing an `S` (§15.3.5); it remains Non-Copy and grants no access.

3. SPEC §15.3.5 の末尾の文「Only `Slice<T>`, a compiler-managed representation without stored Fields, keeps compiler metadata for them (§22.1).」を、次の文に置き換える。

> A compiler-managed Type without stored Fields takes them from its own rule: `Slice<T>` from its compiler metadata (§22.1), `Array<T>` from its elements (§4.5), and `Loan<T>` and `Weak<S>` (§3.2.2) by being analyzed as storing their argument.

| Semantics | 対象への変性 | 外側 Origin | 理由 |
| --- | --- | --- | --- |
| `owner` | 共変 | なし | 内容を書くのは所有経路だけで、その型は正確である |
| `obj` | 不変 | なし | 書き込む payload の Dynamic Type は、消去で証明した束縛を保つ。View Target がそれを示さなくても同じである（本書 3.4.1 規則 4） |
| `ref`、`objref` | 共変 | 共変 | 共有アクセスは書き込まない |
| `rc`、`arc` | 共変 | なし | 共有アクセスは書き込まない |
| `uniq`、`objuniq` | 不変 | 共変 | 別名を通して書き込む |
| `raw` | 不変 | なし | 読みと書きを区別しない |
| pair 層 `s` | 宣言の変性では共変に数える。総称本体の中では、admitted set のすべてで成り立つ関係だけを認める | 借用なら共変 | 二つの引数を比べるときに、引数自身の Semantics の規則が働く |
| `Weak<S>`、`Loan<T>` | 格納した引数から導く | — | 格納しているものとみなす |

**健全性**

- 所有の `T` と Array の要素（SPEC §4.5）は共変である。書き込むのは所有経路だけで、値全体の当てはめは Move なので、長い型の別名は残らない。派生から基底への値変換はない（SPEC §6.2.2）ので、値の型は正確である。
- `obj` は不変とする（本書 3.4.1 規則 4）。`obj` も Non-Copy で、ハンドル全体の当てはめは Move である。それでも、開いた View Target の背後にある payload は、消去の時点で証明した束縛（SPEC §15.8.1）を Dynamic Type として持ち続ける。共変にすると、短くした view を通して短い借用を payload に書き込め、後の精密化と checked cast がそれを `static` として読む（本書 3.4.1 の `leak`）。payload に書き込む残りの経路（`@objuniq` と `@follow@uniq`）は、もともと不変である（SPEC §15.7 も `uniq/T` を要する）。
- `rc` と `arc` では、静的型の異なる別名どうしは読むことしかできない（SPEC §13.5.5.1 の表、13:472）。
- Weak は、`upgrade` するまで payload にアクセスできない。`upgrade` の結果は `rc` か `arc` である。
- pair の引数は完全型である。二つの引数を比べるとき（SPEC §3.8）は、その引数自身の Semantics の規則が働く。
  - `Container<uniq/X{long}>` から `Container<uniq/X{short}>` へは短縮できない。`uniq` が対象に不変だからである。`obj` の引数も同じである。
  - `ref` の引数なら短縮できる。
  - admitted set のうち最も弱い Semantics に合わせると、`s = ref` の具体化での短縮まで拒否してしまう。
- 基底や Contract View へ消去するには、Owned の payload が要る（SPEC §15.8.1）。そのため、短くした Origin を消去で隠すことはできない。

**例**

```kimi
struct Counter {source}
    public let value: ref/i32 during source
    public init(value: ref/i32 during source) => self.value = value

func larger(first: rc/Counter{a}, second: rc/Counter{b}) -> rc/Counter{r}
    origin a.source outlives r.source
    origin b.source outlives r.source
    if first.value >= second.value
        return first@move // Valid: rc/Counter{a} shortens to rc/Counter{r}.
    return second@move

func store(target: uniq/(rc/Counter{t}), next: rc/Counter{n})
    origin n.source outlives t.source
    target@follow = next@move // Valid: next fits the slot's Type by covariance.

func remember(target: uniq/Option<Weak<rc/Counter{t}>>, handle: ref/(rc/Counter{h}))
    origin h.source outlives t.source
    target@follow = .Some(Kimi.Intrinsics.downgrade(handle)) // Valid: Weak<S> varies as a stored S.

public func main()
    let outer: i32 = 1
    var kept = Kimi.Intrinsics.makeRc(Counter.init(outer@ref))
    do
        let inner: i32 = 2
        let short = Kimi.Intrinsics.makeRc(Counter.init(inner@ref))
        let chosen = larger(Kimi.Intrinsics.clone(kept@ref), short@move)
        require chosen.value == 2 else => $abort("Wrong counter")
        // store(kept@uniq, chosen@move) // Error: uniq/(rc/Counter) is invariant; kept is read later.
    require kept.value == 1 else => $abort("Counter changed")
```

`store` を呼ぶ行は、`uniq` が不変なので `t` が `kept` の Origin に固定され、拒否される。もし `uniq` が共変なら、短い `chosen` を `kept` に書き込めてしまい、最後の `require` は解放済みの `inner` を読むことになる。

**最小で一貫している理由**

- Principle 1：一つの規則で、九つの Semantics と pair 層が決まる。オブジェクト用の別表は作らない。Weak の変性も、Loan と同じ「引数を格納しているものとみなす」から導く。Array についての既存の矛盾も、同じ一文で解消する。
- Principle 2：変性は、Semantics の名前、つまり別名や view を通して書けるかどうかだけで決まる。局所的に読める。
- Principle 3：不変になるのは、書き込む別名か view がある所だけである。規則の文に、その意味が現れる。
- Weak と `obj` を除けば、実装の現状と一致する。`obj` は Binding.TypeRelations.cs:45、:150 の不変な Semantics に加える（本書 3.4.1 の実装への注記と同じ変更）。

**採らなかった案**

- オブジェクトハンドルをすべて不変にする：`objref`・`rc`・`arc` の読み取り専用の短縮まで拒否する。共有アクセスは書き込まないので、不変にする理由がない。
- `obj` を所有の `T` と同じく共変にする（実装の現状）：消去した view を短くして payload に書き込むと、Dynamic Type が保つ束縛が破られる（本書 3.4.1 の `leak`）。
- pair 層を、admitted set のうち最も弱い Semantics に合わせる：`s = ref` の具体化での短縮まで拒否する。安全性は、引数自身を比べることで既に保たれている。
- Weak についてだけ「共変」と書く：15:567 の規則と根拠が二重になる。
- 変性注釈を加える：明示の変性注釈を持たない方針（15:547）に反する。

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §15.3.5 | 規則文を置き換える（決定案 1。本書 3.4.1 規則 4 を含む）。末尾の文を置き換える（決定案 3） |
| SPEC §3.2.2 | Weak を、引数を格納しているものとみなす文を加える（決定案 2） |

**実装への注記**

- Weak を宣言するときは、総称スロットに Loan と同じ特例を与える（Binding.OriginRequirements.cs:45-48）。与えないと Invariant に閉じる。
- Binding.TypeRelations.cs:45 と :150 の不変な Semantics に `Obj` を加える（本書 3.4.1 と同じ変更）。
- Binding.TypeRelations.cs:150 は、pair 層を共変として比べる。宣言の変性ではこれで正しい。ただし、総称本体の中で `s/U{long}` と `s/U{short}` を関係付ける場合は、admitted set のすべてで成り立つ関係だけを認めなければならない（SPEC §8.1.2）。`s is owner or uniq` で確かめる。
- 焦点テストを加える。
  - 上の `larger`、`store`、`remember`
  - `if`/`match` で合流する `rc/Counter{a}` と `rc/Counter{b}`
  - 源の異なる要素を持つ `Array<rc/Counter>`
  - 拒否：`{short}` へ当てはめる `objuniq/Counter{long}` と `obj/Counter{long}`
  - 拒否：上の `store(kept@uniq, …)`

**マイルストーンの書き換え**：なし。

#### 3.5.2. Q18. 強参照の解放と、別のハンドルを通した payload の Loan

**現状と問題**

既に決まっていること：

- payload の借用は、それを形成したハンドルに依存する。
  - 03:490：「Reference slots and owners protected to keep the target valid are not part of the lending point」
  - 13:543：「A new Borrow depends on the borrowed slot and on the owner's validity」
  - 実装も同じである（OwnershipAnalysis.Objects.cs:34-35、docs/dev/SHARED_OBJECT_RUNTIME.md:29）。
- 最後になりうる解放は、payload の Origin を観測する。15:873 は「every Origin and Loan that destruction may observe」とする。
- 別々の rc 変数の payload は、別物とは証明されない。15:792 は「Distinct shared-reference or raw-pointer variables alone do not prove independence」とする。
- 静的解析は参照の数を使わない。13:564 は「a runtime reference count of one grants no exception」とする。

決まっていないこと：

- ハンドル `b` を解放するとき、それは別のハンドル `a` を通して形成した payload の Loan に対して、payload Place の破棄に当たるか。
  - SPEC §15.6.2 の表（15:804）は、生きている Loan があれば「Destroy the borrowed Place」を禁じる。
  - SPEC §16.3.3（16:258）は、「exactly the release that reaches zero performs object destruction」とする。
  - 文字どおりに読めば、どの解放も最後かもしれないので拒否になる。数の不変条件で読めば受理になる。
- 呼出しの効果要約（SPEC §15.6.4）と要件呼出しの効果（SPEC §8.4.10.4 規則 1）でも、同じ問いが起きる。
- 13:476 の「After a Move, the payload follows the new owning path.」は、payload の借用が生きている間でもハンドルを Move できる、とも読める。その読みでは、数の不変条件が成り立たない。
- 15:1007 は「Handle replacement follows its own rules.」とするが、その規則はどこにもない。

阻害の度合いは「明確化」である。残りのプログラムは、どちらの読みでも受理される。

- P34：`payload` は :26 が最後の使用で、解放は :32 である。
- P35：`upgrade(node.selfWeak@ref)` の借用は、呼出しで終わる。
- P38：:62 の `describe(live@objref/Device)` の借用も、呼出しで終わる。

決める必要があるのは、U4 と U6 の隣接テスト（ハンドルスロットの `swap`/`replace`）と、診断の期待値である。

**決定案**（規則 4）

1. 前提を明文化する（SPEC §13.5.5.1）。「After a Move, the payload follows the new owning path.」を、次の文に置き換える。

> A Borrow whose lending point lies in an object (an object borrow, a payload follow, a projection of either, or an implicit receiver borrow) keeps, directly or through its parent Loans, a Loan in its own mode on the handle Place through which the object was reached. While the Borrow is live, that handle cannot be Moved, replaced, exclusively borrowed or destroyed; a shared Borrow still permits reading the handle and `Kimi.Intrinsics.clone(handle@ref)`. A handle Moved when no such Borrow is live reaches the payload through its new path.

2. ハンドルの破棄を定める（SPEC §16.3.3。rc/arc の文の後に加える）。

> **Handle destruction and Loans.** Destroying an object handle (`obj`, `rc` or `arc`) at scope exit, replacement, temporary expiry, discard or Field cleanup destroys that handle Place, which is compared with active Loans by §15.6.2. Storage owned through the object, its payload and everything the payload owns, including through nested handles, is never a target of that destruction or of an effect summary containing it: every Borrow into such storage keeps a Loan on a handle through which it was reached (§13.5.5.1), either the destroyed handle, where the conflict is reported, or another strong handle, which keeps its object alive. Because any `rc`/`arc` release may be final, it observes what destroying the complete payload observes (§15.6.6) and contributes that cleanup's effects on other storage, such as static Fields, to the enclosing summary (§15.6.4). A callee that destroys an owned handle parameter contributes the same effects.

3. 参照を加える。
   - SPEC §15.6.2 の衝突表の後に、「Destroying an object handle destroys the handle Place (§16.3.3).」を加える。
   - 15:1007 の「Handle replacement follows its own rules.」を、「Handle replacement writes the handle Place and destroys its old value as §16.3.3 specifies.」にする。
   - SPEC §8.4.10.4 規則 1 の変更は、共通の前提に挙げたとおりである。

二つの `rc` 変数は独立な局所の根なので、既存の構造規則（15:782-790）によって互いに重ならない。そのため、P34 の隣接形は既存の表だけで決まる。

「別の強参照の payload どうしは、重なりの証明が要らない」という文は加えない。二つの payload は同じオブジェクトでありうる。そのような文は、非重なりの証明として誤用されうる（例：SPEC §15.7.2 の `swap`）。

**例**

```kimi
struct Payload
    public let id: i32
    public init(id: i32) => self.id = id

struct Tracked
    public init() => ()
    drop => Audit.released += 1

group Audit
    public var released: i32 = 0

func release(handle: rc/Payload) => ()

public func main()
    let first = Kimi.Intrinsics.makeRc(Payload.init(1))
    let view = first@follow@ref               // Keeps a shared Loan on the handle Place first.
    release(Kimi.Intrinsics.clone(first@ref)) // Valid: destroys only the clone's handle Place.
    let second = Kimi.Intrinsics.clone(first@ref)
    _ = second@move                           // Valid: second does not overlap first.
    // release(first@move)                    // Error: first is Moved while view is live.
    require view.id == 1 else => $abort("Payload lost")

    let tracked = Kimi.Intrinsics.makeRc(Tracked.init())
    let count = Audit.released@ref
    // _ = Kimi.Intrinsics.clone(tracked@ref) // Error: the release may run drop, which writes Audit.released.
    require count == 0 else => $abort("Released early")
```

受理される例：

- 一時的な clone を解放する間も、元のハンドルを通した借用を使える。
- `view` が生きている間でも、`_ = second@move` は書ける。
- P35 で、`node.value@ref` が生きている間に `upgrade` の結果を解放してよい。

拒否される例：

- `view` が生きている間に `first@move` を書くこと。
- `view` が生きている間に、`var` のハンドルへ `h = other` を書くこと。
- U6 の `swap(h@uniq, other@uniq)` を、`h` を通した借用が生きている間に行うこと。
- 借用中の静的 Field に `drop` が書き込む payload を、解放すること。

`let v = Kimi.Intrinsics.clone(first@ref)@follow@ref` の後で `v` を使うことも、既存の規則で拒否される。一時ハンドルは文の終わりで破棄されるからである。これは 13:476 の「extends no owner's lifetime」と一致する。保護はオブジェクト単位ではなく、ハンドル単位である。

**最小で一貫している理由**

- 新しい概念は加えない。既存の「Borrow は owner の有効性に依存する」（13:543）を、ハンドルに対する Loan として明文化するだけである。
- `obj`、`rc`、`arc` の三つに、同じ一つの規則が働く。`obj` では、衝突は破棄されるハンドル自身で報告される。`rc` と `arc` では、別の強参照が残るので、その解放は最後にならない。
- Principle 2：ハンドル Place どうしの重なりを、既存の構造規則で比べるだけで判定できる。数もオブジェクトの同一性も追わない。
- Principle 3：解放の効果（観測と、外の記憶域への効果）は `obj` の破棄と同じである。違いは「オブジェクトが所有する記憶域は、効果の対象にならない」の一点だけである。

**採らなかった案**

- 文字どおりの衝突（どの解放も payload Place の破棄とする）：一時的な clone が消える間に、元のハンドルで読む。この普通のプログラムを拒否してしまう。
- `make*` と `clone` をたどって、オブジェクトの同一性を静的に追う：局所的でない別名解析になる。この規則より得るものがない。
- 数を数えて、最後でない解放を特定する：13:564 に反する。

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §13.5.5.1 | 決定案 1 |
| SPEC §16.3.3 | 決定案 2 |
| SPEC §15.6.2、§15.7.3 | 参照を加える（決定案 3） |
| SPEC §8.4.10.4 | 規則 1 の様式と、ハンドル引数の破棄（共通の前提） |
| IMPL 付録 A.8 | A:139 に三つの例を加える：別のハンドルを通した借用が生きている間の解放（受理）、借用しているハンドル自身の Move・置換・破棄（拒否）、静的 Field に書き込む `drop` を持つ payload の解放（借用中は拒否）。A:139 の「release through different views」は View Type の異なるハンドルの解放を指し、同じ規則で扱う |

**マイルストーンの書き換え**：なし。P34、P35、P38 はそのまま受理される。SharedObjectOwnershipTest の既存の拒否（objref が生きている間の Move）は、仕様に裏付けられた回帰テストになる。

#### 3.5.3. Q09. 射影を通した受け手呼出しの ObjectCallCompatible

**現状と問題**

1. 言語規則は、P38 の結果を既に決めている。
   - 対象の呼出しは三つある。
     - P38:43 の `owner.level = 30` は、Device の custom `set`（`uniq/Self`）を呼ぶ。
     - :44 と :47 の `owner.isOff` は、Device の computed `get`（`ref/Self`）を呼ぶ。
   - どれも、完全な Sealed の Lamp payload を経て、Device へ基底射影する。09:331 は「inherited Self remains the defining base」とするので、Sealed の例外（12:235）は及ばない。
   - 以下の規定が Proven を求める。
     - SPEC §7.3 の検査 3（07:185）と SPEC §9.5.1（09:327）は、Proven を求める。
     - SPEC §11.2.1（11:185）は、「writing `ref/Self` or `uniq/Self` alone supplies no proof」とする。
     - SPEC §12.4.4.1（12:248）は、未実装の検証を NotProven として隠すことを禁じる。
   - SPEC §12.4.4.2 を当てはめれば、setter も getter も Proven になる。
     - setter は、`require` の後で `storage = value`（Part の置換）を行う。
     - getter は読むだけである。
2. 実装は保留されている。
   - 付録 D の表（D:29）と D.5.1 が保留する。D:103 は「implementation begins only on explicit instruction」、D:111 は「Stages 2 and 3 await further instructions」とする。
   - PLAN §1 は付録 D を範囲から除く。tests/milestones/README.md の :190-191 と :285 も、保留を確認している。
   - コンパイラーは、射影した受け手の状態を Unknown とし（Binding.ArgumentOperations.cs:188-190）、UnsupportedBinding を報告する（Binding.Calls.cs:767-775）。
   - 一方で README:305 は、「no new feature family is deferred to this final target」と約束している。このままでは、P38 は受理されない。
3. 規則にも曖昧さが残る。
   - 12:266 の行「MayAlias, or unverified unsafe/indirect effects … | Unproven effect on every potentially affected root」と、12:276 の「unproven effects propagate to the potentially affected roots」は、次のことを決めていない。呼出し全体で Loan に守られた受け手が、静的 Field への書込みや未知の呼出し先にとって「potentially affected」かどうかである。
   - そのため、関数値や unsafe な補助関数を呼ぶ getter の判定が、実装者によって分かれる。
     - NotProven とする読みは、12:252 の「A formal `ref`/`uniq` parameter alone does not prove caller storage completeness」に支えられる。
     - Proven とする読みは、15:851、15:857、05:78-80 に支えられる。
   - README:1950 の別検査「a computed accessor through an open view without ObjectCallCompatible」は、NotProven の getter があることを前提にしている。
4. SETTLED.md:81 の理由は「`ref/Self` and `uniq/Self` are reachable through object handles by the path rules, so no capability is lost …」である。基底で宣言された getter について、これが厳密に成り立つのは、その getter が Proven のときだけである。

**決定案**

1. **範囲**（規則 7。ユーザーの指示として採る）。
   - D.5.1 の第 2 段階のうち、次の部分を現在の範囲にする。
     - 一つのビルドの宣言と特殊化に対する推論。
     - 使用時の強制。
     - SPEC §18.7 の記録を作るビルドでは、完了した状態の公開（18:310）。
   - 第 3 段階（リリース比較）は保留のままとする。
   - 付録 D の表の行は「ObjectCallCompatible release comparison | Deferred」とし、D.5.1 は各段階の状態を書き換える。
   - 本書の採用を、D.5.1 のいう「explicit instruction」とみなす。
   - 状態をまだ計算していない使用は、12:248 が既に決めているとおり Proven にも NotProven にもしない。位置付きの UnsupportedBinding とし、選ばれた実装、射影の経路、未了の検証を示す（IMPL 付録 A.8 と DIAGNOSTICS）。
2. **受け手に届く操作**（規則 5。SPEC §12.4.4.2 に加える）。

> **Potentially affected roots.** At every call the receiver's Loan is active for the whole call (§15.6.4), so an operation on the receiver's storage through any other path, such as another argument, a capture, a static anchor or a callee with unknown effects, is rejected at that call (§15.6.2, §15.6.4) or is undefined behavior for raw access (§5.2.1). Receiver preservation therefore considers only operations reached through the receiver: its path, the Places and references derived from it, and the callees given such access. Through a `ref/Self` receiver only reads and shared borrows are reached (§15.6.2), so an operation with a `ref/Self` receiver, including every custom or computed `get` and every shared standard witness bridge (§11.4.2), is Proven exactly when its declaration is valid.

   - 12:252 は、「A formal `uniq` parameter alone does not prove caller storage completeness: callee effects are composed at each actual storage target, and unknown-call, unsafe and specialization checks are preserved. A `ref` input has only read and shared-borrow effects.」にする。
   - SPEC §11.2.1（11:185）は、「a `ref/Self` receiver is Proven by its shape (§12.4.4.2); writing `uniq/Self` alone supplies no proof」にする。
   - 15:859 には、「which roots they affect follows §12.4.4.2」の一句を加える。
   - 明示的な特殊化は、元の受け手の形を保つ（07:154）。そのため、共有受け手の実装族（SPEC §12.4.4.3）は、全体が Proven になる。
   - 排他の `set` を橋渡しする標準 witness は、Field を書き込むだけである。これは既存の規則（12:262）で、完全性を保つ Part の置換になる。
3. **排他入力の上界**（規則 6。同じ節に加える）。

> **Exclusive input bound.** A callee affects its inputs at most as §8.4.10.4 rule 1 permits, and its results keep the dependencies its signature states. For receiver preservation, everything it does to an exclusive input is at most one completeness-preserving Replacement of the input's actual target, because borrowed referents offer no Take (§15.1.5) and unsafe code bears the same obligation (§5.2.3). A callee without a computed or validated summary, such as a bodiless compiler-owned declaration or an indirect or generic-requirement call, contributes exactly this bound, and unproven or unsafe effects inside a callee's body contribute at most it. `Kimi.Intrinsics.replace` and `exchange` are such a Replacement and `swap` an Exchange of both targets (§15.7). The bound mapped onto a receiver Part preserves the receiver; mapped onto its Whole or Base it is a violation.

   - 12:266 の行は、「MayAlias with receiver storage, or unsafe access in the operation's own body that may reach it | Unproven effect on the receiver」にする。
   - 12:276 の第 2 文「Without an optional effect guarantee, unproven effects propagate to the potentially affected roots;」は、「Without such a summary they contribute the exclusive input bound;」にする。
4. **計画**（PLAN §4、§6、§7）。
   - OCC-S（共有受け手。署名だけで決まる）：有効な `ref/Self` の受け手について、`ProjectedReceiverProof` を Proven にする。
     - P25 の継承メンバー射影と同じ単位、またはその直後に置く。
     - これで、P38 の :44 と :47 が通る。P25 の隣接形（基底の `ref/Self` メソッドや getter を、Derived の値に対して使う形）も通る。
   - OCC-X（排他受け手の第 2 段階）：P36 の後、P38 の前に一単位置く。P38:43 は、自明な場合である（`require` と `storage = value`）。
   - 同じ単位で、次を更新する。
     - STATUS（221 行と 314 行）と CODEMAP
     - InheritedReceiverBindingTest（Unknown を期待している 107、239、334、358、371 行）

**例**

```kimi
open struct Device
    public let name: string
    public var level: i32
        set(value: i32) -> ()
            require value >= 0 and value <= 100 else => $abort("Level out of range")
            storage = value // Part Replacement: Proven by inference.
    public computed isOff: bool
        get() -> bool => self.level == 0 // ref/Self receiver: Proven by its shape.

    public init(name: string, level: i32)
        self.name = name@move
        self.level = level

    public func restart(self: uniq/Self)
        Kimi.Intrinsics.replace(self, with: Device.init("restarted", 0)) // Whole Replacement: NotProven.

struct Lamp : Device
    public init(name: string) : base(name@move, 0) => ()

public func main()
    var owner = Kimi.Intrinsics.makeObj<Lamp>(Lamp.init("desk"))
    owner.level = 30 // Valid: the Device setter is Proven.
    require owner.isOff == false else => $abort("Level not stored")
    // owner.restart() // Error: NotProven; it would replace the Device inside a Lamp.
    var bench = Device.init("bench", 10)
    bench.restart() // Valid: an ordinary complete-value call.
```

**最小で一貫している理由**

- Principle 1：一つの規則（受け手は受け手を通してだけ影響を受ける）から、二つのことが決まる。共有受け手が Proven であることと、排他受け手の推論の範囲である。上界は、既存の SPEC §8.4.10.4 規則 1 と SPEC §15.1.5 を使い回す。`replace`、`exchange`、`swap` に個別の要約表は作らない。
- Principle 2：共有受け手の状態は、署名から読める。getter の合法性は本体に依存しない。これで SETTLED.md:81 の理由が、getter について厳密に成り立つ。SETTLED.md は変更しなくてよい。第 3 段階に入っても、共有の操作は、署名を変えない限り Proven から NotProven へ後退しない。
- Principle 3：未計算の状態は、Proven にも NotProven にもしない。NotProven になるのは、受け手を通した Whole の置換、Move、脱出だけである。

**採らなかった案**

- 第 2 段階だけを有効にし、形の規則を入れない：12:266 の曖昧さが残る。未知の呼出し先を持つ getter が NotProven になりうるので、SETTLED.md:81 の理由が弱まる。推論も重くなる。
- P38 を書き換える（`level` と `isOff` を Lamp に移す）：プログラムの主題を失い、README:305 の約束に反する。
- 使用箇所で、呼出し先の本体から証明する：12:248 の「呼出し側に固有の強化はない」に反する。
- open struct の借用受け手メンバーすべてに、宣言上の義務を課す：完全な `B` の全体を置き換える有効なメンバーまで拒否する。SPEC §12.4.4、§8.8.2、§18.7、付録 D の大きな再設計にもなる。
- 検証されない呼出し先を、一律に Unproven とする（12:276 の文字どおりの読み）：Array の Field や総称要件を受け手の Part に使う `uniq/Self` メソッドが、すべて NotProven になる。そうした効果は、署名と SPEC §15.1.5 が既に上から抑えている。

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §12.4.4.2 | 12:252 を改める。二段落（決定案 2、3）を加える。12:266 の行と 12:276 の第 2 文を置き換える |
| SPEC §11.2.1 | 11:185 の証明の文 |
| SPEC §15.6.4 | 15:859 に、影響する根は SPEC §12.4.4.2 に従うと一句加える |
| SPEC 付録 D | 表の行（D:29）と D.5.1 の段階の状態 |
| IMPL §21.5.5 | 但し書きを、SPEC §15.6.2 への参照にする |
| IMPL 付録 A.8 | A:135 に加える：排他入力の上界、共有受け手、未計算の状態の診断 |
| PLAN、STATUS、CODEMAP | OCC-S と OCC-X の単位、範囲、支援の境界 |

**マイルストーンの書き換え**

- ソースは変更しない。
- tests/milestones/README.md の P38 の別検査（:1948-1950）を書き換える。
  - 「a computed accessor through an open view without ObjectCallCompatible」を、「a setter or method whose body replaces the whole receiver, used through an open view or base projection (NotProven)」にする。
  - 正の検査「a computed getter through `objref/Device`」を加える。
- :190-191、:285、:1427-1429、:1762 にある ObjectCallCompatible の保留の記述を、新しい範囲（同一ビルドの第 2 段階は現在の範囲、リリース比較は保留）に合わせる。

### 3.6. 静的記憶域の一つの生存期間：キーで識別する根、四つの状態、二段階の終了処理、位置を持つ目録 Abort（P36、P38）

対象は P36（一般の静的記憶域）である。P38 の静的 `var lastSeen` も同じ規則に従う。本テーマは、四つの項目（Q20、Q22、Q23、Q25）を次の一つのモデルで扱う。

- **根。** 静的スロットは、SPEC §22.2.4 の記憶域キーで識別する。キーは、Field 宣言の Identity と、Origin を消去した正規化済みの束縛から成る。キーは、局所変数や一時値と同じく、Place の重なりを判定するときの根である（Q25）。
- **四つの状態。** スロットは、Not started、Initializing、Initialized、Finished のどれか一つの状態にある。読み、Borrow、書き込みなど、すべての記憶域操作がこの状態を調べる（Q22、Q23）。
- **二段階の終了処理。** 終了処理は、本体の局所変数の後始末が終わってから始まる。まず初期化済みの `var` スロットをすべて破棄し、次に `let` スロットを破棄する。どちらの段階も、初期化に成功した順の逆順で破棄する（Q22）。
- **位置を持つ目録 Abort。** 状態が操作を許さないときは、目録のコードで Abort する。報告位置は、そのスロットを指す Place 式の始まりである（Q23）。

統合後の SPEC §22.2.3 の状態表は次のとおりである（英文は Q22 に示す）。

| 状態 | 操作の扱い |
| --- | --- |
| Not started | 終了処理の前：Initializing にして、宣言の初期化子を評価する。正常に完了したら Initialized にし、それから操作を行う。終了処理中：`KIMI_E_STATIC_SHUTDOWN` で Abort |
| Initializing | `KIMI_E_STATIC_CYCLE` で Abort |
| Initialized | 初期化をやり直さずに操作を行う |
| Finished | `KIMI_E_STATIC_SHUTDOWN` で Abort |

終了処理は、スロットの値を破棄する前に、そのスロットを Finished にする。Finished のスロットは終了処理の中にしか現れない。また、終了処理の中に Initializing のスロットはない（Q23）。

入れる順序は次のとおりである。

- Q20 は、P36 の最初に直す一行の訂正で、ほかの項目に依存しない。Q20 が一般化する PLAN §5 の再表記の規定は、Q01 案 B（本書 7 の 1）を採った場合の再表記にも使う。
- Q22 は終了処理の段階を決める。Q23 の `KIMI_E_STATIC_SHUTDOWN` は、Q22 が新たに生む「`let` の `drop` が `var` に届く」Abort を含め、終了処理のすべての Abort を扱う。README にはその Abort 入力を加える。StaticInitializationTest.cs:130 は、宣言ではなくアクセスの位置（3:30）を報告するように直す。
- Q22 と Q23 は、初期化済みの静的スロットと、SPEC §22.2.3 の終了処理の Abort すべてについて書く。そのため、どのスロットが終了処理に加わるかの別の決定（本書 6 の Q21）に依存しない。
- Q25 は、P36 の可変静的記憶域の Loan の比較と、swap・予約の検査の前に要る。その閉じたキーの条件は、総称のキー（`Cache<T>`）についての SPEC §8.10 の一度だけの検証に依存する。

#### 3.6.1. Q20. Program 36 の `Resource.drop` が、置き換えられる Resource 2 で Abort する

**分類。** プログラムと仕様の不一致である。P36 の完了を止めている。言語仕様の判断は要らない。

**現状と問題**

仕様が求める動作は次のとおりである。

- SPEC §11.3.2（docs/spec/11-properties.md:360）：「A standard write initializes the slot first and then replaces its value」。
- SPEC §22.2.3（docs/spec/22-core-execution-and-foreign-functions.md:327, 331）：Not started の行は「evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation」である。同じ節には「A first write initializes before Replacement.」ともある。
- SPEC §13.7.1（docs/spec/13-operators-and-assignment.md:768, 775）：手順 3 は「Destroy the old value」である。さらに「A complete old value is cleaned up by its exact Type's full destruction chain」とある。

この仕様では、`Registry.replaced = Resource.init(3)`（tests/milestones/Milestone36.kimi:37）は次の順で進む。

1. 右辺を評価する。何も出力しない。
2. 代入先を決める。スロットが Not started なので初期化し、「Resource 2 initialized.」を出力する。
3. Resource 2 を破棄する。
4. Resource 3 を置く。

README（tests/milestones/README.md:1855-1856）も、「Resource 2 initialized.」と「Static resource 2 destroyed.」がこの順に出ることを期待している。

プログラムの問題は次のとおりである。

- `Resource.drop`（Milestone36.kimi:5-9）には `1 =>` と `3 =>` の腕しかない。id 2 は `_ => $abort("Unexpected resource")` に進む。
- そのため、プログラムは「Resource 2 initialized.」を出力した直後に Abort する。これは、:37 にある自身の注釈「Replacement destroys Resource 2」とも、README の期待出力とも合わない。
- この形は作成時（f5a1ec21）からあり、84c2358a で `deinit` を `drop` に書き換えたときにも残った。P36 は終了処理もまだ実装していない（docs/dev/PLAN.md:66）。そのため、ネイティブ実行で出力を照合したことがない。

手続きの問題は次のとおりである。

- PLAN §5 の条件 1（docs/dev/PLAN.md:139）は、仕様から導いた出力を、チェックインしたソースで得ることを求める。今のソースでは、この条件を満たせない。
- 再表記の規定（PLAN.md:145、README:84-88）が扱うのは、仕様変更による再表記だけである。未完了のプログラムにある作成時の誤りを直す場合は、扱っていない。

**決定案**

1. Milestone36.kimi の `Resource.drop` に、`2 => Console.writeLine("Static resource 2 destroyed.")` の腕を加える。位置は `1 =>` の腕と `3 =>` の腕の間である。ほかは変えない。
   - `_ => $abort("Unexpected resource")` は残す。一度もアクセスしない Resource 9 を誤って初期化し、破棄する実装は、この腕で引き続き見つかる。id 1〜3 が余分に破棄されたり、二重に破棄されたりする誤りは、出力の照合で見つかる。
   - README の期待出力（:1850-1861）は変えない。この出力は、もともと SPEC §11.3.2・§13.7.1・§22.2.3 から導いたものである。
   - tests/milestones/stage-baselines.json:8 も変えない。錨の `Registry` は、最初の診断が下線を引く文字列である。`public func main()` は、ソースに一度だけ現れればよい（tests/xUnitTest/Tests/MilestoneSourcesTest.cs:66, 73-75）。加える腕は、どちらにも影響しない。
2. 再表記の規定を一つの規則にまとめる。PLAN §5（:145）を次の文に置き換え、README:84-88 の段落も同じ内容にする。
   > A program source is re-spelled only for a specification change or for an authoring correction, where the source contradicts its own specification-derived output. `tests/milestones/README.md` records each re-spelling with its cause; a completed program keeps DONE when conditions 1–3 hold for the re-spelled source.
3. README の再表記表（:90-97）に P36 の行を加える。
   - 再表記：「`Resource.drop` gains the `2 =>` arm」
   - 根拠：「Authoring correction; SPEC 11.3.2, 13.7.1, 22.2.3」
   - P36 の状態行（:136）にも、再表記した日付を書く。

```kimi
struct Resource
    public let id: i32
    public init(id: i32) => self.id = id
    drop
        match self.id
            1 => Console.writeLine("Static resource 1 destroyed.")
            2 => Console.writeLine("Static resource 2 destroyed.")
            3 => Console.writeLine("Static resource 3 destroyed.")
            _ => $abort("Unexpected resource")
```

**最小で一貫している理由**

- 出力は、既存の三つの規則だけで決まる。スロットの状態機械、初回の書き込みでの初期化、置換での破棄である。言語の規則は一つも増えない。
- 初回の書き込みでも初期化子を実行する。そのため、初期化の効果はアクセスの種類によらない（Principle 1・2）。
- 再表記の規定は、二つの原因（仕様変更と作成時の誤りの訂正）を一つの規則で扱う。どちらの原因でも、記録と条件 1〜3 を求める。そのため、記録のない編集が、実装に合わせて目標を動かしたように見えることはない。

**採らなかった案**

- **期待出力を Abort に変える。** P36 の目的は、初回の書き込みでの置換と、終了処理での破棄を示すことである。この案はその目的と、README:303 の検査「First write before replacement」を失う。
- **初回の書き込みでは初期化子を実行しない（SPEC §11.3.2・§22.2.3 を変える）。** 初期化の効果がアクセスの種類で変わる。すべての操作に共通の状態表も成り立たなくなる。
- **README だけに一回限りの注記を書く。** 同じ種類の訂正が起きるたびに、改めて判断が必要になる。

**変更する文書。** tests/milestones/Milestone36.kimi、docs/dev/PLAN.md §5、tests/milestones/README.md（再表記の段落と表、P36 の行）を変える。SPEC・IMPL・STATUS・SETTLED は変えない。

#### 3.6.2. Q22. 終了処理の順序：`var` を先に、`let` を後に破棄する

**分類。** 仕様の欠落である。P36 の範囲のうち、借用を含む記憶域と集約の記憶域という隣接形を止めている。

**現状と問題**

SPEC §22.2.3（22:333）は次のように定める。

> Initialized static values are then destroyed in reverse order of successful initialization, retaining the required lifetime dependencies; dependencies that cannot survive this order are rejected.

問題は次のとおりである。

- 破棄の順序は実行時に決まる（:331「Actual access determines dependency order」）。一方、拒否はコンパイル時の規則である。しかし、どの書き込みを拒否するかを定める節がない。
- SPEC §11.3.2（11-properties.md:356, 358）と SPEC §15.6.4（15-ownership…:857「Immutable anchors are kept for shutdown dependencies even after erasure」）は、この検査を参照するだけで、内容を定めていない。
- 危険な形は合法である。
  - 静的記憶域で省略した共有借用の Origin は `static` になる（SPEC §15.4.2、15-ownership…:633）。
  - 不変の静的記憶域の共有借用は、保持してよい（SPEC §11.3.2、11-properties.md:356）。

```kimi
group Registry
    public let empty: string = ""
    public let text: string = "static text"
    public var view: ref/string = Registry.empty@ref // Static storage: the omitted Origin is static.

public func main()
    Console.writeLine(Registry.view) // empty completes, then view.
    Registry.view = Registry.text@ref // text completes after view.
```

- 今の規則では、後で完了した `text` が、`view` より先に破棄される。
- その間に破棄される別の静的値の `drop` が `Registry.view` を読むと、破棄済みの記憶域を参照する。`view` を含む値に `drop` があれば、その `drop` も同じである。SPEC §15.6.6（15-ownership…:879）は、利用者が定義した `drop` が、到達できるすべての Origin を観測するとみなす。
- この形を拒否するかどうかは決まっていない。`func set(v: ref/string during static) => Registry.view = v` のように、錨の分からない値を書き込む形もある。これを、ある実装はすべて拒否し、別の実装は不健全なまま受理する。

**決定案**

1. SPEC §22.2.3（:333）の二番目と三番目の文を、次の文に置き換える。依存を拒否する節と、「Initializing new static storage, accessing destroyed storage or reentering a Field's destruction during shutdown Aborts.」は削除する。この Abort は、状態表の行に移る。
   > Normal body exit cleans up its locals exactly once, and shutdown then begins. Shutdown destroys every Initialized static `var` slot and afterwards every Initialized static `let` slot, each group in reverse order of successful initialization, and marks a slot Finished before destroying its value.
2. SPEC §22.2.3 の状態表（:325-329）を、次の表に置き換える。
   - 今の文言では、「accessing destroyed storage」と「reentering a Field's destruction」が別の場合として書かれている。新しい表では、どちらも Finished のスロットへのアクセスになるので、一つの行にまとまる。

   | State | Action |
   | --- | --- |
   | Not started | Before shutdown: mark Initializing; evaluate the declaration initializer; after normal completion mark Initialized, then perform the operation. During shutdown: Abort with KIMI_E_STATIC_SHUTDOWN |
   | Initializing | Abort with KIMI_E_STATIC_CYCLE |
   | Initialized | Perform the operation without rerunning initialization |
   | Finished | Abort with KIMI_E_STATIC_SHUTDOWN |

3. 次の非規範の注記を添える。
   > No shutdown dependency check is needed. In safe code a `during static` borrow is anchored only in a static `let` slot or in constant data (§11.3.2, §15.2.3), and a `let` slot's value, including payloads reached through its handles, cannot change after its initialization completes (§11.3.2, §13.5.5.1, §15.1.5). A `let` therefore depends only on `let` slots that completed earlier, and no value depends on a `var` slot's storage. Raw-pointer derivations keep the obligations of §5.2.1.
4. 次の帰結を、受け入れるものとして明記する。`let` の段階で静的な `var` にアクセスすると、必ず `KIMI_E_STATIC_SHUTDOWN` で Abort する。初期化済みの `var` はすべて Finished になっており、未初期化の `var` は終了処理中に初期化できないからである。この Abort が起きるかどうかは、実行の履歴によらず、宣言だけで決まる。

```kimi
struct Probe
    public let id: i32

    public init(id: i32) => self.id = id

    drop => Console.writeLine("Count is \(Registry.count).")

group Registry
    public var count: i32 = 0
    public let probe: Probe = Probe.init(1)

public func main()
    Registry.count = 1
    require Registry.probe.id == 1 else => $abort("Unexpected probe")
    // Shutdown destroys count in the var phase and probe in the let phase.
    // The drop of probe reads Registry.count, so shutdown Aborts at 6:43.
```

- 今の規則では、count が先に完了しているので、probe が先に破棄される。そのため「Count is 1.」が出力される。
- 新しい規則では、この形は宣言だけで決まる Abort になる。直すには、probe を `var` にするか、`drop` が静的記憶域に触れないようにする。

**健全性**

- 安全なコードで `during static` の借用を作れるのは、不変の静的記憶域か定数データだけである（SPEC §11.3.2:358、SPEC §15.2.3:331）。
  - 可変の静的記憶域は、その内側の部分や backing data も含めて、`static` に借用できない。
  - `uniq` の `static` 借用は作れない。
  - 静的記憶域の Type は Owned なので、静的でない Origin を含まない。
- 静的な `let` の値は、初期化が完了した後は変わらない。
  - 静的な `let` は、外部から初期化することも、置き換えることもできない（SPEC §11.3.2:360）。
  - 静的な根のうち排他的に取得できるのは、可変の静的記憶域だけである（SPEC §15.1.5:158）。
  - 静的記憶域は Take を提供しない（SPEC §15.1.5:133）。
  - `let h: obj/T` の payload には、読み取りと共有借用しかできない。`rc`・`arc` の payload には、共有アクセスしかできない（SPEC §13.5.5.1:469-472）。
  - 内部可変性はない。
- `let` の初期化子が借用する `let` は、その時点で初期化済みでなければならない。もし初期化中なら、循環として Abort する。したがって、`let` の値の錨は、それより前に完了した `let` か、定数データである。`let` を完了の逆順に破棄すれば、錨は保持者より後に破棄される。
- `var` の値の錨も、`let` か定数データである。そのため、`var` を先にすべて破棄すれば足りる。
- SPEC §15.6.6 の破棄の生存期間検査は、`static` Origin については、この順序によって満たされる。
- 実装は、`var` と `let` のそれぞれについて、完了したスロットを記録し、逆順にたどればよい。終了処理中かどうかは、プロセス全体の一つの段階として持つ。コンパイル時に錨を追跡する必要はなく、書き込みごとの実行時コストもない。IMPL に契約を足す必要もない。

**P36・P38 とライブラリへの影響**

- P36 で完了する順は、count、primary、replaced、`Cache<i32>` の hits、`Cache<string>` の hits である。
  - 新しい順序では、`var` の段階で、二つの hits、replaced（Resource 3）、count の順に破棄する。`let` の段階で、primary（Resource 1）を破棄する。
  - README:1860-1861 の出力は変わらない。
- P38 の静的スロットは `var lastSeen` だけである。`var` の段階で解放される。
- ライブラリの静的スロットはすべて `let` で、`drop` を持たない（src/Kimi/Library/Time.kimi:3、DictionaryStorage.kimi:22-24）。完了済みのプログラムで静的スロットを持つのは Milestone11 の `let defaultWeight` だけで、これも `drop` を持たない。
- 実行時の状態（標準ハンドルやヒープ）は静的スロットではない。その取得は各実行時操作の中で行ってよい（SPEC §22.2.3、22:319）。そのため、`let` の段階で `Console.writeLine` を呼んでも Abort しない。P36 の primary（Resource 1）の `drop` は、この場合に当たる。ライブラリに静的な `var` を加えるときは、それに触れる操作を静的な `let` の `drop` から呼ぶと Abort することを、その操作の文書に書く。

**採らなかった案**

- **宣言の初期化子以外で、錨を持つ値を格納することを拒否する。** パラメーター、消去、結果をまたいで錨を追跡しなければならない。しかも、setter、Array への追加、総称の値といった正当な流れまで拒否してしまう。
- **最後に値を置いた順に並べ直す。** 書き込みのたびに実行時の付け替えが要る。順序も実行の履歴で決まる（Principle 2 に反する）。
- **「借用を含み得る Type」の `var` だけを先に破棄する。** 新しい述語が必要になる。その述語は、消去された基底、実行時 Contract の View、共通 Function Type の隠れた環境まで扱わなければならない。また、`var` の Type を変えると、破棄の順序が気づかないうちに変わる。
- **今の順序を保ち、実行時に検査する。** 実行時の Loan 表現が必要になる。
- **終了処理で静的値を破棄しない。** P36 の出力と、資源解放の決定性を失う。

**変更する節**

| 節 | 変更 |
| --- | --- |
| SPEC §22.2.3（:323-333） | 四つの状態の表、終了処理の開始、二段階の順序、Finished の設定を入れる。依存を拒否する節と、終了処理中の Abort を並べた文を削除する。非規範の注記を添える |
| SPEC §22.2.4（:353） | 「reverse-initialization shutdown destruction」を「the shutdown order of §22.2.3」にする |
| SPEC §11.3.2（:356） | 「Acquisition, initialization and destruction dependencies are checked separately」を「Acquisition and initialization dependencies are checked separately, and the shutdown order of §22.2.3 keeps destruction dependencies valid」にする |
| SPEC §11.3.2（:358） | 最後の文を「Local Loans remain checked (§15.6.4).」にする |
| SPEC §15.6.4（:857） | 「Immutable anchors are kept for shutdown dependencies even after erasure, while borrows of mutable sources cannot cross an Owned boundary.」を「Borrows of mutable sources cannot cross an Owned boundary.」にする |
| IMPL 付録 A（docs/impl/appendices/A-compiler-requirements.md:398） | 「reverse cleanup」を「var-then-let reverse cleanup and shutdown Aborts」にする |

**マイルストーンの書き換え**

- Milestone36.kimi の最後の注釈（今の :45。Q20 で腕を加えた後は :46）を、「Shutdown destroys static vars, then static lets, each in reverse initialization order: Resource 3, then Resource 1.」にする。出力は変わらない。
- README の対応する記述（:303 の表の行、:1843-1845 の説明）も同じ内容にする。
- README:1864-1868 の個別の入力に、「静的な `let` の破棄が静的な `var` にアクセスすると Abort する」入力を加える。

#### 3.6.3. Q23. 静的記憶域の暗黙の Abort：コード、理由、位置

**分類。** 仕様の欠落である。P36 の、Abort を確かめる個別の入力で、期待する stderr が決まらない。

**現状と問題**

- **位置はすでに決まっている。**
  - SPEC §17.3.3（17-failure-handling.md:225）は、暗黙の Abort の位置を「the failed operation for an implicit Abort」とする。
  - SPEC §22.2.3（:323）は、状態の検査を記憶域操作に属させる。
  - SPEC §22.5.1（:465）は、元の操作の行と列を保つことを求める。
- **コードと理由は決まっていない。**
  - SPEC §22.5.4（:506）の目録には、静的記憶域のコードがない。
  - `KIMI_E_ABORT` は、明示の `$abort` のコードとして定められている（:517）。
  - SPEC §22.2.3（:328, 333）は、循環と、終了処理中の三つの失敗を Abort とするだけである。
- **実装は仕様に合っていない。** 実装は、`KIMI_E_ABORT: Static initialization cycle` を、Field 宣言の位置で出力する（src/Kimi/Compiler/Emission/LlvmEmitter.cs:135-142）。tests/xUnitTest/Tests/StaticInitializationTest.cs:130 も「Hello.kimi:2:12」を期待している。これでは、言語が検出した失敗と、利用者が書いた `$abort` を区別できない。位置も SPEC §17.3.3 に反する。
- **修飾された Place の位置が決まっていない。** `Values.first` の位置が、修飾子の始まりなのか、Field 名の始まりなのかが決まっていない。

**決定案**

1. コードは、状態表の行に結び付ける（Q22 の表）。
   - Initializing の行は `KIMI_E_STATIC_CYCLE` を使う。
   - 終了処理中の Not started の行と、Finished の行は `KIMI_E_STATIC_SHUTDOWN` を使う。SPEC §22.2.3 が定める終了処理の Abort は、すべてこのコードになる。
   - SPEC §22.5.4 では、状態を列挙し直さない。
2. SPEC §22.5.4 の目録に、次の項目を加える。
   > - `KIMI_E_STATIC_CYCLE: Static initialization cycle` and `KIMI_E_STATIC_SHUTDOWN: Static storage unavailable during shutdown` are the static-storage Aborts of §22.2.3. Each reports the start of the Place expression that designates the slot, including its qualifier: `Values.first` reports `Values`, and `Cache<i32>.Statistics.hits` reports `Cache`. An unqualified name, including `storage` in the Field's own accessor, reports itself.
3. SPEC §17.3.1（17-failure-handling.md:178）が列挙する、検査される操作に、「static storage access in a state that forbids it (§22.2.3)」を加える。
4. 二つのコードの優先順位を決める規則は要らない。二つの状態は同時には起きないからである。
   - 終了処理が始まる時点で、Initializing のスロットはない。本体が初期化子の動的範囲の中で終わることはないからである。
   - 終了処理は、初期化子を開始しない。
   - 終了処理の前に Initializing のスロットに出会うのは、そのスロットの初期化子の動的範囲の中だけである。したがって、それは必ず循環である。自分自身を参照する場合も含む。

```kimi
group Values
    public let first: i64 = Values.second + 1
    public let second: i64 = Values.first + 1 // This Values.first meets first while it is Initializing.

public func main()
    let value = Values.first
```

```text
Main.kimi:3:30: abort KIMI_E_STATIC_CYCLE: Static initialization cycle
```

- 報告位置は、宣言（2 行目）でも、最初のアクセス（6 行目）でもない。状態に出会った操作の位置である。
- Q22 の Probe の例は、`Main.kimi:6:43: abort KIMI_E_STATIC_SHUTDOWN: Static storage unavailable during shutdown` になる。

**最小で一貫している理由**

- 目録の既存の方針に従う。その方針は、一つの原因と一つの直し方に、一つのコードを当てることである。たとえば `INT_DIV_ZERO` と `INT_OVERFLOW` は分かれている。一方、`REF_COUNT` は強参照と weak の両方を扱う。
  - 循環を直すには、初期化子の依存を直す。
  - 終了処理の Abort を直すには、終了時の破棄が静的記憶域に触れないようにする。
  - 直し方が二通りなので、コードも二つにする。それ以上は増やさない（Principle 1）。
- 位置は、既存の「式の始まり」という慣例に従う（SPEC §22.5.1:467、SPEC §22.5.4:512）。単純代入と複合代入の左辺では、この位置は更新式の始まりと一致する。
- コードは状態表の行に結び付ける。そのため、状態の規則が変わっても、目録を直す必要がない。

**採らなかった案**

- **一つのコード `KIMI_E_STATIC_STATE` にする。** 原因も直し方も違う二つの場合を、一つにまとめてしまう。
- **終了処理の三つの場合（新しい初期化、破棄済み、破棄中の再入）に、それぞれコードを当てる。** Finished は破棄の前に設定するので、後の二つは同じ状態である。三つとも直し方は同じである。
- **今のまま、宣言の位置で `KIMI_E_ABORT` を使う。** 言語が検出した失敗と利用者の Abort を混同する。SPEC §17.3.3 にも反する。
- **Field 名の始まりで報告する。** 「式の始まり」の慣例から外れる。

**変更する節**

- SPEC §22.2.3（状態表。Q22 と共通）
- SPEC §22.5.4（目録と位置）
- SPEC §17.3.1（列挙）

IMPL は変えない。SPEC §22.5.1:465 は、物理的な補助関数が、元の操作の位置を内部 ABI で運ぶことをすでに求めている。共有コードが使う initialize-and-address 操作（IMPL §21.3.3.4、docs/impl/21-layout-runtime-and-code-generation.md:565）にも、この規則が及ぶ。

**実装への注記（仕様の項目ではない）**

- WindowsLowering.Abort.cs の理由表に、二つの項目を加える。
- 各アクセスは、`state == Initialized` を調べるインラインの速い経路を持つ。呼出し側の位置を運ぶのは、遅い経路の呼出しだけである。そのため、スロットごとの位置と文言の定数（LlvmEmitter.cs:141-142）は要らなくなる。
- StaticInitializationTest.cs:130 の期待は、`Hello.kimi:3:30: abort KIMI_E_STATIC_CYCLE: Static initialization cycle\n` になる。このテストのソースは修飾しない `first` を使うので、位置はその名前の始まりである。

#### 3.6.4. Q25. Place の重なり：静的キーを根に含める

**分類。** 曖昧さの明確化である。P36・P38 のプログラム本体はこの規則に依存しない。ただし、P36 の検査範囲（README:303 の「alias paths to one key, effects/reentry」）の焦点テストには必要である。

**現状と問題**

- SPEC §15.6.2（15-ownership…:780）は、「Static Place analysis proves non-overlap using only these structural rules」と定める。
  - 根の行は「Independent local roots and their inline parts | Disjoint」（:785）だけである。
  - 集約の行は「of one aggregate」（:786）に限られる。
  - それ以外は「Non-overlap unproven; operations requiring a proof are rejected」（:790）になる。
  - 静的記憶域は、局所的な根でも、集約の部分でもない。
- 一方、ほかの節は、分離していることを前提にしている。
  - SPEC §3.4（03-types…:447）：「Distinct Fields … designate non-overlapping Places (§15.6.2)」。
  - SPEC §22.2.4（:341）：「Distinct keys have distinct storage … Different paths to one key use one … Loan and effect identity」。
  - SPEC §15.6.4（:857）は、`reset` が「may replace that Field」である場合にだけ拒否する例を挙げている。
  - SPEC §15.1.5（:158）と SPEC §12.4.4.2（12-expressions.md:270）も、静的記憶域を根として扱っている。
- 文字どおりに読むと、次のものがすべて拒否される。
  - 二つの静的 Field の `swap`（SPEC §15.7.2:992）。
  - 局所変数に Loan があるときの、静的記憶域への書き込み。
  - 静的記憶域への書き込みを要約に持つ呼出し（SPEC §15.6.4:853）。
- 総称のキーには、健全性の落とし穴がある。
  - `Cache<T>` と `Cache<U>` は、字面の上では別のキーである。しかし、`T = U` のときは同じキーになる。
  - 総称の本体は一度だけ検証し、インスタンス化では意味の条件を足さない（SPEC §8.10、08-generics…:1159, 1184）。
- 遅延初期化の効果の扱いも、一部しか決まっていない。SPEC §15.6.4（:853-855）は、初回アクセスの初期化効果を呼出しの要約で扱う。SPEC §8.4.10.2（08-generics…:670）も、不変の静的記憶域を読むときは「The initializer enters the summary」とする。しかし、本体の中で静的記憶域に直接アクセスするときに、その効果を本体の生きている Loan と比べるかどうかは書かれていない。

**決定案**

1. SPEC §15.6.2 の根の行を、次のように一般化する。
   > | Distinct roots and their inline parts | Disjoint |

   表の後に、次の文を加える。根の一覧を作り直すのではなく、静的キーを根に加えるだけにする。
   > A static storage key (§22.2.4) is a root like a local or an owned temporary, and every path to one key is that one root. Keys of different Field declarations are distinct. Two keys of one Field declaration are distinct only when both are closed after normalization and Origin erasure and are unequal; otherwise their non-overlap is unproven.
2. SPEC §15.6.4 の「Summaries distinguish first-access initialization effects …」で始まる段落に、次の文を加える。
   > A direct static access in a body has the same first-access initialization effects and is checked against active Loans like a call.
3. SPEC §15.6.4（:853）の「the static Field identities it may access」を、「the static storage keys (§22.2.4) it may access」にする。SPEC §22.2.4 は効果の同一性をすでにキーごとに定めているので、これは文言をそろえるだけである。
4. SPEC §3.4（03-types…:431）の Storage の列挙に、「a static slot (§22.2.4)」を加える。
5. 必要なら、SPEC §15.7.2 の例に `Kimi.Intrinsics.swap(Registry.first@uniq, Registry.second@uniq) // Valid: distinct static keys.` を加える。

```kimi
group Registry
    public var first: i32 = 1
    public var second: i32 = 2

func exchange()
    Kimi.Intrinsics.swap(Registry.first@uniq, Registry.second@uniq) // Valid: distinct keys.
    var local: i32 = 0
    let r = local@uniq
    Registry.first = 3 // Valid: a static key and a local are distinct roots.
    r@follow = 1
```

```kimi
struct Cache<T>
    public group Stats
        public var hits: i32 = 0

func exchangeHits<T, U>()
    Kimi.Intrinsics.swap(Cache<i32>.Stats.hits@uniq, Cache<string>.Stats.hits@uniq) // Valid: distinct closed keys.
    Kimi.Intrinsics.swap(Cache<T>.Stats.hits@uniq, Cache<U>.Stats.hits@uniq) // Error: one key when T = U.
```

```kimi
group Counter
    public var total: i32 = 0
    public var snapshot: i32 = Counter.capture()

    public func capture() -> i32
        Counter.total += 1
        return Counter.total

func update()
    let r = Counter.total@uniq
    let s = Counter.snapshot // Error: its first initialization may write total, which r borrows.
    r@follow = s
```

**最小で一貫している理由**

- 局所変数、一時値、静的キーを、「根」という一つの概念で扱う。行を足すのではなく、既存の行を一般化する（Principle 1）。
  - SPEC §15.1.5:158 と SPEC §12.4.4.2:270 は、すでに同じ見方をしている。
- 同じキーへの異なる経路が一つの根になることは、SPEC §22.2.4:341 が定めている。本項はそれを参照するだけで、繰り返さない。
- 総称のキーは、両方が閉じていて、しかも等しくないときだけ分離しているとみなす。この判定は有界で、インスタンス化に検査を足さない（Principle 2、SPEC §8.10）。
- 遅延初期化の効果を、直接アクセスにも課す。呼出しの要約と同じ扱いになり、Place が分離していても、隠れた書き込みを見逃さない（Principle 3）。

**採らなかった案**

- **静的キー専用の行を加える。** 一つの概念に、行が二つできてしまう。
- **根を網羅的に定義し直す。** 捕捉や既存の「root」の用法（SPEC §15.1.5、§12.4.4.2）とずれる危険がある。静的キーを加えるだけで足りる。
- **SPEC §3.4:447 だけに頼る。** 静的記憶域と局所変数の関係が決まらないままになる。
- **字面の上で異なるキーを、分離しているとみなす。** `T = U` のときに重なる二つの排他借用を、受理してしまう。
- **開いたキーを単一化で比べる（`Cache<T>` と `Cache<Array<T>>` は常に異なる、など）。** 証明の仕組みが増えるわりに、得るものが少ない。

**変更する節**

- SPEC §15.6.2（根の行と、静的キーの文）
- SPEC §15.6.4（:853 のキー、:855 の直接アクセス）
- SPEC §3.4（:431）
- SPEC §15.7.2（任意の例）

**マイルストーンへの影響**

- P36 の `main` での静的アクセスは、どれも読んでから書くだけで、その間に生きている Loan がない。代入は右辺を先に評価するからである（SPEC §13.7.1:766）。
- P38 の `Registry.lastSeen = .Some(Kimi.Intrinsics.downgrade(shared@ref))` も、右辺を先に評価する。`downgrade` は「no lasting Loan on the source handle slot」である（SPEC §13.5.9、13-operators…:676）。
- 根の規則は、分離を増やすだけである。直接アクセスの初期化効果は、SPEC §15.6.4 が呼出しに課す検査を、直接アクセスにも課す。今の実装が受理する静的アクセスは、閉じた非総称の不変スカラーを読むことだけである（docs/STATUS.md:312）。可変の静的記憶域に Loan を持つプログラムはまだ受理されないので、受理済みのプログラムは変わらない。

#### 3.6.5. 本テーマで既に決まっている事項

- **初回の書き込みの順序。** 初回の書き込みでは、右辺を評価してから代入先を決める。スロットはそこで初期化される。根拠は SPEC §13.7.1 の手順 1・2 と SPEC §22.2.3 である。P36 の `Resource.init(3)` は何も出力しないので、この順序は出力に現れない。
- **暗黙の Abort の位置。** 暗黙の Abort の位置は、失敗した操作である（SPEC §17.3.3:225）。Q23 が決めるのは、コードと、修飾された Place の始まりだけである。
- **同じキーへの経路。** 同じキーへの異なる経路は、一つの初期化状態、一つの Loan と効果の同一性、一つの破棄責任を持つ（SPEC §22.2.4:341）。
- **入力の借用が静的記憶域を指す場合。** 呼出し先の要約は、呼出し側の生きている Loan と比べる。引数の Loan は、呼出しの間ずっと有効である（SPEC §15.6.4:851, 853）。そのため、静的 Field の `uniq` 借用を、その Field に触れる関数に渡すと、呼出し側で拒否される。呼出し先の本体では、入力の借用の先と、自分がアクセスする静的キーを比べる必要がない。新しい規則は要らない。
- **match の束縛。** `match Registry.lastSeen` のように Place を Subject にすると、`ref` の束縛になる（SPEC §14、14-control-flow.md:580）。静的記憶域は Take を提供しないが、P38 の `lookup` はこの規則の範囲で書かれている。

## 4. マイルストーン別の対応

実行順は PLAN §3（PLAN.md:34）のとおり、P26 を先に進め、その後 Property とオブジェクトの系列（P24 → P25 → P33 → P35 → P36 → P38）を進める。P34 は交互の共有オブジェクトの系列で進める（U4 が進行中、続いて U5〜U8。U8 は P35 の前）。本書のテーマは、それを最初に必要とするマイルストーンの順に並べてある。

### 4.1. P26 一般の Closure と Callable（進行中）

- **完了前に必要な決定：**
  - Q01（本書 3.1.1）：G10 の推論。P26:48 は注釈付きなので、どの案でも書いたまま有効である。注釈のない形は隣接テストで確かめる。
  - Q02（本書 3.1.2）：G10 の関数参照の選択。P26:58–60 の `increment` は一意の非総称の宣言なので、値になる。
  - Q03（本書 3.1.3）：消去の位置と順位。P26:55、:59 は注釈付きの初期化で、どの読みでも有効である。arm、`yield`、`exit`、既定値での消去が実装されるまでは、STATUS と PLAN（G10）に不足として記録する。
  - Q26（本書 3.2.1）：外部依存の脱出の拒否検査（README:1463）の前に要る。Owned でない環境の消去は `UnsatisfiedConstraint_Kd`（新しい Origin の表示付き）、Closure を外側の局所変数へ持ち出す形は Loan の衝突を期待する。
- **ソースの書き換え：** なし。SPEC §13.5.9 の例の `weak@move` と `selfWeak@move` の訂正は、Q01 と同じコミットで行う。
- **備考：**
  - 要件の参照（`T.compare`）の実装は、残りのプログラムが使わないので、P26 の完了条件に含めない（本書 7 の 7）。
  - G20（Dictionary の容量コールバック）は決定事項ではない（本書 6 の Q05）。ただし、「P26 の Closure で足りる」という計画の前提は不正確で、呼出し位置を運ぶ私的な手段も要る。

### 4.2. P24 所有権を持つ Property（未着手）

- **必要な決定：** なし。14 件のどれも P24 のソースに関わらない。
- **備考：** 次は決定ではなく、文書と文言の整理である（本書 6）。
  - Q07：PLAN.md:47 の「Owned/borrow-producing defaults remain a STATUS limit owned by P24」を、担当未定の PLAN §7 の項目に移す。
  - Q08：出力文言「Owned getter received.」（P24:49–50、README:1376）は任意で直せる。直すなら README の再表記表に、2026-10-02 の `Parcel.intoResult()` の再表記の行も加える。
  - 現在の停止位置 `value.item`（UnsupportedBinding_Kd）は実装作業である。

### 4.3. P25 継承（進行中）

- **実装中に必要な決定：**
  - Q10（本書 3.3.1）：P25 の単位で実装する。P25 のソースは影響を受けないが、隣接テスト（裸の `marker()`・`count`・`Node` など）と、新しい失敗種別・コード・修復の連絡口を固める。
  - Q09 の共有受け手（OCC-S、本書 3.5.3 の決定案 4）：継承メンバーの射影と同じ単位か、その直後に置く。基底の `ref/Self` メソッドや getter を Derived の値に使う隣接形が通る。Q18・Q19 を正式仕様に取り込んだ後なら、いつでも入れられる。
- **ソースの書き換え：** なし。

### 4.4. P33 排他オブジェクトと view（進行中）

- **実装中に必要な決定：**
  - Q12（本書 3.4.1）の規則 1〜4。P33:45 の `view@follow@uniq` は規則 1 で決まる。規則 4（`obj` を不変にする）は Binding.TypeRelations.cs の変更を伴う。
  - Q13（本書 3.4.2）。P33 の型付けには影響しないが、IMPL §A.9 の「contradictory facts」の試験に一つの答えが要る。
  - 対象の Owned の失敗は、本書 3.2.1 の R4（Constraint のコードと Origin の表示）に従って報告する。
- **ソースの書き換え：** なし。P33:31、:51 に当たる任意の警告は、方針として当面出さない（本書 7 の 16）。

### 4.5. P34 共有オブジェクトの所有権 rc/arc（進行中、交互の系列）

- **実装中に必要な決定：**
  - Q19（本書 3.5.1）：主に実装の現状の記録である。`obj` の不変は Q12 規則 4 と同じ変更である。
  - Q18（本書 3.5.2）：U4 の解放と、U6 のハンドルスロットの `swap`/`replace` の隣接テストと診断の期待値を決める。
  - Q26（本書 3.2.1）：局所変数を借用する payload を持つハンドルの脱出（README:1794）は `UnsatisfiedOriginRelation_Kd` を期待する。局所どうしの脱出は `ComparisonLoanConflict_Kd` のままである。
- **ソースの書き換え：** なし。

### 4.6. P35 Weak（未着手）

- **着手前に必要な決定：**
  - Q01（本書 3.1.1）：P35:41 の `let node = Kimi.Intrinsics.makeRcCyclic(build)` は、Q01 案 A（本書 7 の 1）の下でだけ有効である。
  - Q19（本書 3.5.1）：Weak を宣言する（G4）前に、Weak の総称スロットに Loan と同じ特例を与える（Binding.OriginRequirements.cs:45-48）。与えないと Invariant に閉じる。
  - Q18 は、`node.value@ref` が生きている間に `upgrade` の結果を解放してよいことを裏付ける。
- **前提：** U8（非 null の Option 表現）が P35 の前に完了していること（PLAN の共有オブジェクトの系列）。
- **ソースの書き換え：** Q01 案 A なら不要である。案 B を採るなら、P35:41 と SPEC 13:710 を `let node: rc/Node = …` に書き換え、本書 3.6.1 の再表記の規定で記録する。

### 4.7. P36 一般の静的記憶域（一部実装）

- **着手前に必要な決定：** Q20（本書 3.6.1）。最初に `Resource.drop` を直す。ほかの項目に依存しない。
- **実装中に必要な決定：**
  - Q22（本書 3.6.2）：終了処理を実装する前に。
  - Q23（本書 3.6.3）：Abort の個別の入力の stderr を決める。
  - Q25（本書 3.6.4）：可変静的記憶域の Loan の比較と、swap・予約の検査の前に。閉じたキーの条件は、`Cache<T>` についての SPEC §8.10 に依存する。
  - Q26（本書 3.2.1）：可変静的記憶域の `during static` の拒否（README:1865）。Q22 で終了時の依存の拒否がなくなるので、P36 の静的な拒否はこれだけである。
- **ソースとテストの書き換え：**
  - Milestone36.kimi：`Resource.drop` に `2 =>` の腕を加える。最後の注釈（腕を加えた後の :46）を var と let の順序に合わせる。期待する標準出力（README:1850-1861）は変わらない。stage-baselines.json:8 も変わらない。
  - README：再表記表に P36 の行を加え、:136 に日付を書く。:303 と :1843-1845 を var と let の順序に合わせる。:1864-1868 に「静的な `let` の `drop` が静的な `var` に届く」Abort 入力（その Place で `KIMI_E_STATIC_SHUTDOWN`）を加える。
  - PLAN §5：再表記の規定を一般化する。
  - StaticInitializationTest.cs:130：期待を `Hello.kimi:3:30: abort KIMI_E_STATIC_CYCLE: Static initialization cycle\n` にする。

### 4.8. P38 統合アプリケーション（未着手）

- **着手前に必要な決定：**
  - Q09（本書 3.5.3）：P38:44、:47 は OCC-S で、P38:43 のセッターは OCC-X で通る。OCC-X は P36 の後・P38 の前に一単位として置き、付録 D.5.1 のユーザーの範囲指示が要る（本書 7 の 4）。
  - Q12 規則 1（本書 3.4.1）：P38:32 の `view@follow@ref` は、14:828 の書き直しで決まる。
- **実装中に必要な決定：**
  - Q26（本書 3.2.1）：payload が局所変数を借用する静的 Weak の拒否（README:1950–1951）は `UnsatisfiedOriginRelation_Kd` を期待する。
  - Q19（本書 3.5.1）：`Weak<rc/Lamp>` の変性。
  - Q18（本書 3.5.2）：P38:62 の `describe(live@objref/Device)` の借用は呼出しで終わるので、どの読みでも受理される。決まるのは、ハンドルの破棄にかかわる隣接テストと診断の期待値である。
  - Q22（本書 3.6.2）：静的 `var lastSeen` は `var` の段階で解放される。出力は変わらない。
  - Q25（本書 3.6.4）：`Registry.lastSeen` への書き込みは右辺を先に評価するので、生きている Loan と衝突しない。
- **ソースの書き換え：** なし。tests/milestones/README.md の P38 の別検査（:1948-1950）を排他受け手の NotProven の場合に書き換え、`objref/Device` を通した computed getter の正の検査を加える。:190-191、:285、:1427-1429、:1762 の保留の記述を新しい範囲に合わせる。

## 5. 仕様への反映先

決定ごとの反映先は次のとおりである。LIBRARY.md と STYLE.md を変える決定はない。取り込むときは、同じコミットで draft/INTEGRATED.md に記録する。

| 決定 | SPEC | IMPL | その他 |
| --- | --- | --- | --- |
| Q01 | §10.1 step 3、§10.8、§10.5（Anonymous body context）、§7.6.1、§13.5.9 の例（`weak@move`、別件） | — | DIAGNOSTICS（Advice） |
| Q02 | §10.5（Function references、`S` の定義）、§7.6.1、§7.6.4、§8.4.6 | — | DIAGNOSTICS、CODEMAP |
| Q03 | §10.2（消去の行）、§10.2.1、§10.7、§3.8、§7.6.4、§6.3.2 | — | STATUS、PLAN（G10 の不足） |
| Q26 | §3.8、§4.6.1、§10.1〜§10.3、§11.3.2、§15.2.3、§15.6.1、§15.6.5、§23.3.6.5 | — | DIAGNOSTICS、CODEMAP、PLAN §7（G56 を閉じる）、テストの期待 |
| Q10 | §9.4、§9.5、§6.2.2、§10.6、§23.3.6.9 | §21.3.4.1 | SETTLED.md（新しい項目）、CODEMAP |
| Q12 | §13.6.1、§13.6.2、§14.10.1、§15.3.5（Q19 と共通）、§15.4.2、§15.8.1 | §A.3 | — |
| Q13 | §14.10.2 | §A.9 | — |
| 本書 3.5 の共通の前提 | §15.6.2（Shared access grants no mutation）、§8.4.10.4 規則 1 | §21.5.5 | — |
| Q19 | §15.3.5、§3.2.2 | — | — |
| Q18 | §13.5.5.1、§16.3.3、§15.6.2（参照）、§15.7.3、§8.4.10.4 規則 1（所有ハンドル引数の破棄） | §A.8 | — |
| Q09 | §12.4.4.2、§11.2.1、§15.6.4、付録 D（表と D.5.1） | §21.5.5、§A.8 | PLAN、STATUS、CODEMAP、README |
| Q20 | — | — | Milestone36.kimi、PLAN §5、README |
| Q22 | §22.2.3、§22.2.4、§11.3.2、§15.6.4 | 付録 A（A:398） | Milestone36.kimi の注釈、README |
| Q23 | §22.2.3（Q22 と共通）、§22.5.4、§17.3.1 | — | StaticInitializationTest |
| Q25 | §15.6.2、§15.6.4、§3.4、§15.7.2（任意の例） | — | — |

同じ節を複数の決定が変える。取り込みのときは、次の節を一度に書き直す。

| 節 | 変える決定 |
| --- | --- |
| SPEC §3.8 | Q03（消去の行の参照）、Q26（Origin 行は Origin 関係を生む） |
| SPEC §7.6.1 | Q01（本体の結果推論は外側の証拠でない）、Q02（`S` の定義を参照） |
| SPEC §7.6.4 | Q02（参照の解決）、Q03（位置と消去の行） |
| SPEC §8.4.10.4 | 本書 3.5 の共通の前提（共有の層の先）、Q18（所有ハンドル引数の破棄） |
| SPEC §10.1〜§10.3 | Q01（step 3）、Q03（§10.2 の行、§10.2.1 の順位）、Q26（Origin 関係は選択の後） |
| SPEC §10.5 | Q01 と Q02（一つの段落） |
| SPEC §11.3.2 | Q22（:356、:358）、Q26（有限 Origin の上限） |
| SPEC §15.3.5 | Q12 規則 4 と Q19（一つの文案、本書 3.5.1） |
| SPEC §15.6.2 | 本書 3.5 の共通の前提、Q18（ハンドルの破棄の参照）、Q25（根の行） |
| SPEC §15.6.4 | Q09（:859）、Q22（:857）、Q25（:853、:855） |
| SPEC §22.2.3 | Q22（順序と状態表）、Q23（コード） |
| IMPL §21.5.5 | 本書 3.5 の共通の前提と Q09（同じ一つの参照の変更） |
| IMPL 付録 A | Q12（A.3）、Q13（A.9）、Q18 と Q09（A.8）、Q22（A:398） |

## 6. 決定済みと確認した事項

### 6.1. 決定済みの事項（実装作業）

次の事項は、仕様がすでに振る舞いを決めている。残るのは実装作業だけである。

**P24**

| 事項 | 決める節 |
| --- | --- |
| セッターの置換の順序（「Setter entered.」が「Resource 1 destroyed.」より先） | SPEC §11.2.4、§13.7.1、IMPL §21.4.4 |
| セッターの中の `storage` と `value`、受け手の省略形 | SPEC §11.2、付録 F.6 |
| 構築子の最初の配置はカスタムセッターを通らない | SPEC §11.3.1 |
| computed getter `self.item@ref/Resource` と結果の Origin の省略 | SPEC §13.5.5.2、11:66、11:327 |
| getter の受け手の取得は常に共有 | SPEC §11.2.2、§7.3、§3.4.1 |
| 要件 `Viewed.item` を、カスタムセッター付きの格納 Field で witness する | SPEC §11.4 |
| 総称の `inspect(value.item)`（Contract の Property 要件） | SPEC §9.5、§8.4.6、§12.4.4.1、IMPL §21.3.3.2 |
| `parcel@move.intoResult()`（消費は関数。SETTLED） | SPEC §7.3、§15.1.5、11:243-250、SETTLED.md:60-81 |
| `self.item@move` と不完全な Parcel の後始末 | SPEC §15.1.3、§15.1.5、16:142-160 |
| 一時値と借用した getter の結果 | SPEC §3.6.2、§11.2.3 |
| P24 の拒否検査と期待出力の順序 | SPEC §11、§15.6、§16.2.1 |
| 既定値の意味（所有値や借用を作る既定値を含む） | SPEC §7.2.3 |
| アクセサーと witness の物理 ABI | IMPL §21.3.3.2、§21.4.2、§21.4.4 |

**P25**

| 事項 | 決める節 |
| --- | --- |
| 派生の構築順序 | SPEC §6.2.3.3、§6.2.3.5 |
| 暗黙の基底呼出しと、派生 struct の暗黙の構築子 | 06:235、06:253、10:210 |
| 到達できない基底の名前の再利用、Field の同一性、到達できない層は lookup を確定しない | SPEC §6.2.2、§9.5 |
| 継承した Type 関数 `Derived.marker()` と Type 名前空間のメンバー | SPEC §6.2.2、§9.5、§9.6.1 |
| 継承した標準 Property・Field のアクセスは Place 操作で、ObjectCallCompatible は要らない | SPEC §11.1、§11.1.2、§9.5.1、§12.4.4、§12.4.4.1 |
| 派生全体の Move は、構築子を再実行せずにすべての層を移す | 06:199、06:247、15:45、16:254 |
| 層ごとの破棄順序（静的と `obj/Base` を通した動的）、部分的な層 | SPEC §16.3.1〜§16.3.3、IMPL 21:231-241 |
| オブジェクトと基底の配置 | IMPL §21.1.1〜§21.2.4 |
| 派生から基底への値変換はない | 06:201、09:325、09:331、D:11 |
| 派生 struct の Copy の導出は struct ごと | 03:629、08:320 |
| 構築中の受け手の制限、drop 中の Field アクセス、構築中の Abort と転送 | SPEC §6.2.3.4、11:339、§16 |
| ObjectCallCompatible の呼出し規則とハンドル経路の権限（実装は Q09） | SPEC §9.5.1、§7.3、§12.4.4、§13.5.5.1、SETTLED.md:81 |

**P26**

| 事項 | 決める節 |
| --- | --- |
| `ref/F` を通した Callable<ref> | SPEC §8.6、§7.6.3、10:123 |
| 総称の捕捉 `[value@move, visitor]` と、呼出しごとの Origin が捕捉の固定 Origin になること | SPEC §7.6.2、08:924、15:344 |
| 総称本体での Closure の受け手要件と Callable の適合 | SPEC §7.6.3、08:922、08:1159-1161 |
| Callable<uniq> と `[var pair]` のスナップショット | 08:919、07:222-226、07:510-518 |
| Callable<owner> と `action@move()`、P35 の builder の取得 | SPEC §8.6、§7.6.3、13:651、IMPL 21:329 |
| 期待結果による `let received: Packet = consume(take@move)` の推論 | SPEC §10.1 step 4、§10.2.1 |
| `[bias@ref]` の捕捉と `(value: ref/Packet)` の呼出しごとの Origin | SPEC §7.6.2、15:536、08:924-926 |
| Owned 環境を持つ Non-Copy Closure の消去、ヒープ環境 | SPEC §7.6.4、IMPL 21:333-359 |
| 一意の宣言の Function Item | 03:192、SPEC §7.6.4、10:267、IMPL 21:327 |
| Closure の結果 Origin（G65） | SPEC §15.8.2、07:463 |
| 共通値と Function Item のアダプターの間接呼出し ABI | IMPL 21:342-351、21:692-696 |
| P26 に関わる付録 D の行 | D:10、D:21 |
| P26 の期待出力と破棄順序 | README:1449-1458、IMPL 21:329、21:353 |

**P33**

| 事項 | 決める節 |
| --- | --- |
| Leaf、Lamp、Sensor、Payload は既定で Sealed | SPEC §6.2.2、§8.4.7.1 |
| 精密化できる対象と、精密化しない形 | SPEC §14.10、§7.2、13:722、13:736 |
| 短絡の `and`、`is not`、`require` の伝播と、`if` の後の合流 | SPEC §14.10.2 |
| 無関係な `is not Other` の対象も型が正しい | SPEC §13.6.1 |
| `let narrowed = view` は `objref/Leaf` を推論し、Origin と Loan を保つ | SPEC §14.10.1 |
| 完全な Sealed payload の follow とその ABI | SPEC §13.5.5.1、IMPL 21:321 |
| objuniq の精密化は権限を増やさない | 14:824、13:471 |
| 完全な payload の exchange、payload の更新後も精密化が残る | SPEC §15.7、§16、14:824、15:1007 |
| P33 の対象の精密化を無効にするもの（Move か破棄だけ） | 15:21、03:338、14:824 |
| upcast と Owned 消去の証明、rc/arc の実行時 `is`、モードをまたぐ変換はない | SPEC §13.5.3、§13.5.7、§13.6.1、§15.8.1 |
| obj ハンドルとオブジェクト view を通した Field の読取り | SPEC §3.4、§3.4.1、13:467-474 |
| DIAGNOSTICS §11 の精密化の行の土台 | SPEC §14.10 |

**P34**

| 事項 | 決める節 |
| --- | --- |
| 借用依存を持つ payload の rc | SPEC §3.3.5、§13.5.8、15:340 |
| `clone` の結果は元のハンドルスロットに Loan を残さない | 13:647、SPEC §15.6.4 |
| rc を通した Field の読取り | 03:502、13:465-474 |
| 強参照の `clone(first@ref)` の明示の借用と、Weak スロットの暗黙の借用 | 13:647、13:694-698、SPEC §10.2 |
| 強参照と Weak の `clone` は互いに素 | SPEC §13.5.9 |
| 転送と解放（Move や借用は数を変えない） | 03:215、03:545-551、SPEC §16 |
| `copy` を局所名に使う `inspect(copy@objref)` | 13:212-213、02:292 |
| スコープ終了での解放の順序、payload の一度だけの破棄、解放は破棄の観測 | SPEC §16、§15.6.6 |
| 単一スレッドの初期プロファイルでの arc | IMPL §21.2.3、D:81-83 |
| 参照カウントのあふれ | SPEC §22.5.4、IMPL 21:260 |
| rc/arc を通した排他アクセスと Field の書込みはない | 13:472、13:564 |
| U6 のハンドルスロットの swap・replace・exchange | SPEC §15.7 |
| 総称本体のオブジェクト工場（G67） | SPEC §13.5.8、§8.4.7.2、IMPL §21.3 |
| 非 null の Option 表現（U8） | IMPL §21.1.5 |
| PLAN の「Checks at implementation」（U3、U4） | PLAN.md:88、IMPL 21:241 |
| オブジェクトハンドルの等価比較は拒否 | 13:129、13:164 |

**P35**

| 事項 | 決める節 |
| --- | --- |
| rc の payload を通した `upgrade(node.selfWeak@ref)` | SPEC §3.4.1、13:679 |
| `match upgrade(...)` と `.Some(let live)`、`.Some(_)` | SPEC §15.1.6、14:545-551 |
| 循環工場の生存期間（Building、builder の Weak、公開、Abort） | SPEC §13.5.8、IMPL 21:262、21:268、21:303 |
| `Weak<rc/Node>` を持つ Node は Owned で Object Target | 15:340、08:421-423 |
| Weak、側表、数、移行、upgrade（G4）と Weak の失効 | IMPL §21.2.3、SPEC §13.5.8〜§13.5.9、§3.2.2 |
| Weak とその補助の宣言 | 22:23、22:45、22:79-82、src/Kimi/Library/README.md |
| Destroying 状態と drop 中の upgrade | 16:274、IMPL 21:270 |
| 無名関数のパラメーター `weak` による隠蔽 | 09:96 |
| P35 の拒否検査と期待出力 | SPEC §13.5.8、README:1813-1824、§16.2.1 |

**P36**

| 事項 | 決める節 |
| --- | --- |
| 最初の静的書き込みは初期化子を実行してから置換する | SPEC §11.3.2、§22.2.3、§13.7.1 |
| `Registry.count = Registry.count + 1` は読取りで一度だけ初期化する | 13:766、22:327-329 |
| 部分 Place のアクセスもスロットを初期化する。静的記憶域は Take を提供しない | 22:323、15:133 |
| 使わない静的スロット、実際のアクセスによる初期化順 | 22:331、11:360 |
| 総称の静的キー（`Cache<i32>` と `Cache<string>`） | SPEC §22.2.4 |
| Abort と非ゼロ終了は終了処理をしない。通常の終了処理は一度だけ | SPEC §22.2.3（22:333、22:335）、§17.3.3（17:212） |
| 静的記憶域の Type は Owned、省略した共有 Origin は `static`（G55） | SPEC §11.3.2、§15.4.2、§8.8.2 |
| 可変静的記憶域の借用は有限 Origin。`during static` は不変の静的記憶域だけ | 11:358-360、15:331 |
| 要約の静的効果と呼出し側の Loan の比較 | SPEC §15.6.3〜§15.6.4、08:670-671 |
| 可変静的記憶域は排他取得と値全体の更新の根 | 15:158、15:950-958 |
| 置き換えた旧値の drop が同じ静的 Field に届くと拒否 | 16:262、13:775 |
| 定数として読める静的 `let` のコンパイル時の読取り | SPEC §4.2 |

**P38**

| 事項 | 決める節 |
| --- | --- |
| カスタムセッターを持つ `owner.level = 30` の評価 | SPEC §13.7.1、§11.2.4、12:262 |
| ハンドル全体の置換 `owner = makeObj<Lamp>(...)` | SPEC §13.7.1、12:237、§16 |
| 層の drop と補間、派生の drop が継承した `self.name` を読む | 16:203-207、16:219、16:274 |
| 派生 Lamp の `Self is Utf8Format` と `Text.toString<T>(value: ref/T)` | utf8-formatting.md、08:308-312 |
| 静的 `var lastSeen` とその書き込み | SPEC §11.3.2、§22.2.3、§13.5.9 |
| `match Registry.lastSeen` と `.Some(let weak) => return upgrade(weak)` | SPEC §15.1.6、15:853-857、15:1071、D:35 |
| main の局所変数の後の静的 Weak の破棄 | SPEC §22.2.3、IMPL 21:270-272（順序は Q22） |
| `objref` パラメーターの `if view is Lamp` による精密化 | SPEC §14.10 |
| 値の流れによる効果境界の委譲 | SPEC §8.4.10.5 |

**横断する点検項目**

| 事項 | 決める節 |
| --- | --- |
| BINDING_REVIEW の「§10.2 の表の二つの写し」と `let` の一時値 | SPEC §10.2、03:777-795 |
| 外側の候補が一つ残るとき、引数に期待を渡す | 10:22、10:254 |
| 一つの Contract の二つの束縛参照を精密化する Contract | 08:144、08:182 |
| G51 の演算子の被演算子要件 | 13:58、13:96、13:104、13:123、13:189 |
| G34、G46、G59、G62 の所有権の制限 | SPEC §8、15:810、03:502 |
| DIAGNOSTICS §11 の予約の行と到達不能パターンの事実 | 15:780-806、15:895、14:466、14:689 |
| G47、G48、G52 の完了と重複記録の残り | 14:466、14:777、SPEC §23.3.6 |
| G60 の字句の残り（`>=` 以外） | SPEC §2.6、§23.3.6 |

### 6.2. 落とした候補

| id | 題目 | 判定 | 根拠 |
| --- | --- | --- | --- |
| Q04 | Function Type、Callable、無名関数のシグネチャーに書く `during`（G65 の残り） | 決定済み | SPEC §15.3.4（15:536、15:543）が禁じるのは新しい名前の導入だけで、書いた `during` を禁じるのは Callable だけである。15:344 も共通 Function Type に書いた固定 Origin を前提にし、OriginSyntaxRevisionTest.cs:131 などが同じ読みを確かめている |
| Q05 | Dictionary の容量コールバックを、借用やヒープの環境なしに Kimigayo ソースで作れない（G20） | 実装のみ | どの残りのプログラムも使わない。位置の運び方は SPEC §22.5.1（22:465）がコンパイラーに任せている。`ref/F` の具体 Callable（08:922、15:1060）や生ポインターの捕捉（15:340、IMPL 21:337）で、割当てなしに作れる。PLAN G20 の「P26 の Closure で足りる」という前提だけが不正確である |
| Q06 | computed と要件の Property の見出しの Origin の補完 | 決定済み | 見出しの型は getter の結果である（11:33、11:364、11:381）。Origin は、実際の受け手を使う通常の関数の省略で補完する（11:327、15:634、15:643） |
| Q07 | 既定値の制限の担当を、既定値を使わない P24 にしている | 決定済み | 意味は SPEC §7.2.3（07:124-132）が決める。残りのプログラムは既定値を使わない。PLAN.md:47 の担当を PLAN §7 の担当未定の項目へ移す計画の修正だけが要る |
| Q08 | P24 の出力文言が、消費する関数を getter と呼ぶ | 実装のみ | 仕様は `intoResult` を関数とし（11:243、11:250）、プログラムもそのとおりである。文言（P24:49-50、README:1376）は規範ではなく、変えても基準線に影響しない |
| Q11 | 精密化した名前の Effective Type を、明示の `@` 操作を含むすべての使用に使う | 決定済み（明確化は本書 3.4.1 規則 1） | 14:824 の定義、13:720、12:237、07:174 から導かれ、14:828 の列挙は例示である。読み分けをなくすため、本書は 14:828 を規則 1 の文で書き直す |
| Q14 | match guard が精密化の伝播の表にない | 決定済み | 条件の状態は任意の条件 C について定義され（14:845、14:857）、guard の両方の結果は到達性の経路に入る（14:777、14:787） |
| Q15 | 静的に決まる `is` への任意の警告で、適合する出力が実装ごとに異なる | 決定済み | 「may」は許可である（01:70-72）。受理・効果・到達性・精密化は決まっている（13:732、14:859） |
| Q16 | 精密化が届かない位置でのメンバーの診断記録 | 実装のみ | 通常のメンバー検索の要件で、既存のコード（`UnresolvedBinding_Kd`）を使う（14:828、23:138）。関連位置と Advice は記録器の設計である（DIAGNOSTICS §10） |
| Q17 | ObjectDescriptor の viewMap の形式が IMPL §21.2.2 にない | 実装のみ | モジュール局所の記録で、生成と読取りが同じ単位にある（21:208、21:229、21:581）。記すなら任意の編集上の注記である |
| Q21 | 終了処理に加わる静的スロットと、終了処理中のアクセス（Finished 状態） | 決定済み | 22:333 は型による例外を設けず、初期化済みのスロットをすべて破棄する。本書 3.6.2 は状態表を書き直すときに Finished を明示し、この一様な参加を保つ（本書 7 の 21） |
| Q24 | 静的記憶域・初期化ガード・終了登録の IMPL 契約がない | 実装のみ | 表現はコンパイラーが選ぶ（21:692、21:208、22:355）。検証は「`var` を先に破棄する」案が現行の 22:333 に反すると指摘したが、本書 3.6.2（Q22）はその順序を意図して仕様に採る。そのとき生じる Abort は、宣言だけで決まる |
| Q27 | `Array<i32>= []` のように `>=` で型引数を閉じる | 決定済み | SPEC §2.4（02:205、02:221、02:223）が分割を許すのは `>>` と `>>=` だけである。TokenReader.cs:693、:704 の受理は実装の不適合で、PARSING_REVIEW.md:54 と G60 に記録済みである |
| Q28 | 付録 D の言語サーバーの行が §23.4.8 の code action や hover の要求と食い違う | 保留（不要） | 所有する節が優先する（SPEC.md:14、D:5）。code action は §23.4.8 が定め、hover は保留中の機能である。D:56 と STATUS.md:402 は古い記述で、文書の手入れで直す |
| Q29 | マイルストーンの集合と状態についての古く食い違った記述 | 実装のみ（文書の整理） | README の状態表が正である（SPEC.md:104、PLAN §5 条件 5）。SPEC.md:104、README:7-8、:188、:277、:1544、:1972、:1993、PLAN:59、:66、Binding.Expressions.cs の「SPEC 7.7」などを直す編集作業である |

## 7. 残る論点

### 7.1. 採否を決めてほしいこと

1. **Q01 の案。** 引数の既知の呼出しシグネチャーと `S` の照合を、step 3 の新しい証拠として加えるか（案 A）。加えずに、P35:41 と SPEC 13:710 を `let node: rc/Node = …` に書き換えるか（案 B）。
   - **推奨：案 A。** 案 B では、`S` のパラメーターにだけ現れるスロットを具体 Closure の引数で決める手段がなく、書いた見出しが証拠になるかの読み分けも残る。案 B を採るなら、本書 3.6.1 の再表記の規定で記録する。
2. **Q01 の範囲。** 共通 Function Type のパラメーターにも同じ照合を使う（`make(makeSmall)` で `T = i32`）か、Callable だけに限るか。残りのマイルストーンは、共通 Function Type の形を使わない。
   - **推奨：両方に使う。** 無名関数の見出しだけを特別に扱う現在の実装の規則が、一つにまとまる。
3. **待つ引数の制限。** 待つ引数は外側のスロットを埋めないので、`map(func (o) => o.amount)` には結果型か注釈が、`map(1@i32, identity)` には `identity<i32>` が要る。
   - **推奨：受け入れる。** 待つ引数を証拠にすると、候補の数と処理の順序で推論が変わる。
4. **Q09 の範囲指示。** ObjectCallCompatible の第 2 段階のうち、同一ビルドの推論・使用時の強制・公開を現在の範囲に入れる。付録 D.5.1（D:103、D:111）は、明示の指示を求めている。
   - **推奨：本書の採用をその指示とする。** リリース比較（第 3 段階）は保留のままにする。断ると P38:43、:44、:47 が止まり、P38 を書き換えると README:305 の約束に反する。
5. **Q09 の排他入力の上界。** 要約のない呼出し先が受け手から受け取った排他入力に及ぼす効果を、完全性を保つ一度の置換とみなす。12:276 を文字どおりに読めば、Unproven の効果になる。
   - **推奨：採る。** 採らないと、Kimi ライブラリや総称要件の操作を受け手の Part に使う `uniq/Self` メソッドが、すべて NotProven になる。
6. **`obj` の変性（Q12 規則 4 と Q19）。** 実装は `obj/T` を View Target について共変にしているが、本書は不変にする。
   - **推奨：不変にする。** 共変のままでは、消去した view を短くして payload に書き込める（本書 3.4.1 の `leak`）。マイルストーンの受理は変わらない。

### 7.2. 方針として確認してほしいこと

7. **要件の参照の実装時期（Q02）。** `T.compare` は、残りのマイルストーンで使わない。
   - **推奨：** 仕様は本書で決めるが、実装は P26 の完了条件に含めない。実装までは位置付きの Unsupported とし、STATUS に記録する（PLAN §5「機能は、そのソースが使うマイルストーンに属する」）。
8. **修飾形の enum 構築（Q03 決定案 4）。** 修飾形の構築でも、期待型を payload より先に使う。
   - **推奨：採る。** 実装と CallbackEmissionTest の `FunctionItemPayload` に合う。逆（payload が先）を好むなら、規則をそう書き、テストの `Option.Some(inc)` を `.Some(inc)` に変える。
9. **互換性（Q01、Q02）。** 新しく適用可能になる候補によって、既存の多重定義の呼出しが曖昧になりうる。
   - **推奨：** pre-alpha として許容し、移行の文書は書かない。
10. **Origin 関係を判定する場所（Q26）。** R2 は生存解析を要しないので、Binding の本体 Origin の段でも所有権解析でも判定できる。DIAGNOSTICS 規則 1 は、そのどちらか一つを求める。
    - **推奨：** 本体の Origin 推論を持つ場所、つまり今の所有権解析の `UnprovenOriginObligation` とする。R3 が頼る Loan の検査より前に走らせ、最初の一件で止めずに、失敗した連鎖ごとに報告する。
11. **名前（Q26）。** `UnsatisfiedOriginRelation_Kd`、`UnprovenOriginRelation_Kd`、事実の種類 `Origin`（種類と文字列）は提案の名前である。一時値の Borrow は、種類 `borrow` と一時値の原文で示し、別の種類 `temporary` は作らない。
    - **推奨：** この名前と表示を採る。
12. **受理の変化（Q26）。** (a) 終了しない経路でだけ外へ出る本体局所の Borrow を、新たに拒否する。(b) Origin 関係だけで候補が落ちていた呼出しは、構造上適用可能な候補から選び直し、その後で関係を報告する。Origin がかかわる Constraint（Owned、Callable、Type 同一性）は、従来どおり適用可能性に加わる。
    - **推奨：受け入れる。** 完了済みのマイルストーンが依存していないことを、実装時にマイルストーンの snapshot で確かめる。
13. **修復の条件 `Selection`（Q10）。** 条件の語彙に `Selection` を一つ加え、関数の集まり（裸の `marker()` から `Self.marker()`）にも候補を出す。代わりの案は、選択が決まっている場合（呼ばない Field・Property、Type）だけ候補を出し、関数は Advice にするものである。
    - **推奨：`Selection` を加える。**
14. **`C.` の候補（Q10）。** 総称パラメーターも Origin ヘッダーも持たない `C` に限る。総称の `C` に出すには、引数の綴り方の規則が別に要る。
    - **推奨：限る。** `Repair.Qualify` を SPEC §9.5 の Type と Value の曖昧さ（09:291-292）にも使う拡張は、後で別に決める。
15. **`struct S : S.N` の循環（Q10）。** 今は制限のない（まれな）形を、解決できない依存の循環として誤りにする。
    - **推奨：誤りにする。** 代わりに、すべての基底節で検査を飛ばすと、入れ子の struct の基底節が継承した入れ子の Type を黙って飛ばす。
16. **静的に決まる `is` への任意の警告（Q12、Q13）。** SPEC §13.6.1 は警告を許し、P33:31（`view is not Other`）と P33:51（`view is Leaf`）に当たる。
    - **推奨：当面は出さない。** 出すなら固有のコードを持たせ、Diagnostic Development Workflow に従う。
17. **総称本体の未決の関係（Q13）。** 関係が未決の検査は事実を加えない。一つの経路の上で、結果が検査の順序に従うことがある。
    - **推奨：受け入れる。** どちらでも健全である。記号的な型の等式の事実は、後の設計が必要とするまで加えない。
18. **総称の対象の実装（Q12）。** Owned が証明された `Slot<T>` の対象は、インスタンス化した引数の実行時同一性を実装するまで `UnsupportedBinding_Kd` のままにする。裸の型パラメーターと関連型の対象は、今すぐ Language のコードに移す（Binding.RuntimeTypeTests.cs:58-61）。
    - **推奨：** そのとおりにする。
19. **数を使わない静的解析（Q18）。** 借用中の静的 Field に `drop` が書き込む payload の解放は、別の強参照が見えていても拒否する。実行時には正しいプログラムの一部を拒否する。
    - **推奨：受け入れる。** 13:564 に合う。
20. **Q09 の計画の位置。** OCC-S は P25 の継承メンバー射影と同じ単位か直後、OCC-X は P36 の後・P38 の前の一単位とする。
    - **推奨：** このとおりにする。交互の系列の都合で別の順序を選んでもよい。
21. **終了処理への一様な参加（本書 3.6）。** 後始末の要らないスロットも Finished にするので、静的 `let` の `drop` がスカラーの静的 `var` の数を読むだけでも Abort する。後始末の要らないスロットを除けば、そうした数は読めるが、Abort するかどうかが Type の破棄に依存するようになる（Principle 2 に反する）。
    - **推奨：一様にする。** 直すには、保持者を `var` にするか、その `drop` を静的記憶域から離す。
22. **Abort の目録の見直し（本書の範囲外）。** 実装は、§22.5.4 の目録にないコードを出している。`KIMI_E_DUPLICATE_KEY`（§17.3.3 は位置だけを決める）、`KIMI_E_ARGUMENT`（utf8-formatting.md の `KIMI_E_ARG_RANGE` と重なる）、`KIMI_E_STRING_RELEASE` である（src/Kimi/Compiler/Emission/WindowsLowering.Abort.cs:51、:47、:40）。また、Kimi ソースの `$abort` で実装した §17.3.1 の検査は、`KIMI_E_ABORT` をライブラリの位置で報告する。例は src/Kimi/Library/Core.kimi の `$abort("Reversed range")` で、tests/xUnitTest/Tests/IntegerRangeTest.cs:111 が期待している。
    - **推奨：別の提案で見直す。** マイルストーンは止めない。本書 3.6.3 は、静的記憶域の二つのコードだけを割り当てる。
23. **任意の診断（本書 3.6）。** 破棄の効果要約が静的 `var` に届きうる静的 `let` に、Advice の水準の警告を出すか。宣言だけで判定できるが、必須ではない。
    - **推奨：今は加えない。** 加えるなら、Diagnostic Development Workflow に従う。
