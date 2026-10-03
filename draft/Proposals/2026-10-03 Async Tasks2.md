# 設計案：非同期処理（タスク・パラメーターと構造化タスク）第 2 版

日付：2026-10-03（2026-10-04 第 2 版）

状態：第 2 版。採否は未決定。正式仕様への取り込みと実装は未実施。初版 `2026-10-03 Async Tasks.md` に、検証済みの改善案を推奨順に適用した改訂版である。本書は初版を置き換え、初版は改訂の経緯として残す。

本書は、Kimigayo に非同期処理を加える設計を示す。C#、Rust、Swift、Go、Kotlin の方式を比較し、現在の所有権・Lifetime 規則（SPEC 第 15・16 章）と Kimigayo Principles に合わせた。SPEC の節は「SPEC §n」、実装仕様の節は「impl §n」、本書の節は「本書 n」と表記する。

要点は五つある。

1. **中断する権限を入力にする。** 中断できる関数は、タスク・パラメーター `task: Async.Task` を持つ。`Async.Task` は値の型ではなく、パラメーターの区分である（SPEC §7.1.1 の Place 結果の区分と同じ考え方）。
2. **未完了の計算を値にしない。** Future や Task 値はなく、`Pin` も要らない。
3. **子タスクは Kimi の呼出しの引数にする。** 呼出しが戻る前に子は必ず完了するので、子は親の局所変数を借用できる。構造化されない（切り離した）タスクはない。
4. **取消しは値にする。** 待機する操作が `Result` で報告する。後始末（Deferred Block・`drop`）は中断せず、取消しも観測しない。
5. **言語規則は二つ。** タスク・パラメーターとタスク呼出しの二つだけを言語規則とし、残りは Kimi の義務とライブラリの契約にする。コンパイラーが特別扱いする Kimi の操作は三つの intrinsic だけで、`join` や実行器は Kimigayo で書く。

## 1. 現仕様の問題

1. **非同期処理がない。** SPEC §22.2.3 は実行スレッドを一つに限り、タスク・スレッド・メモリーモデルは SPEC §D.2 で保留している。I/O を待つ間に他の処理を進める手段がない。
2. **取消しの後始末が決まっていない。** SPEC §16.2.3 は「取消しを導入するなら、Deferred Block・破棄・確保済みの結果について共通の規則が要る」とし、SPEC §15.9 も未定義としている。
3. **他言語の方式をそのまま入れると既存規則と衝突する。**
   - Rust の `Pin` は、すべての値をバイト転送で移動するという規則（impl §21.4.5）と衝突する。
   - Future の破棄による取消しは、SPEC §16.2.3 にない「途中での完了」を必要とする。
   - 環境を借用する Future を返すことは、受け手に依存する公開結果になる。これは保留中である（SPEC Appendix D）。
   - Go や Kotlin のように呼出し側に印がないと、中断点が局所的に分からない（Principle 2）。
   - 値の破棄を前提にした scoped task は、値がリークすると健全でなくなる（Rust の `mem::forget` 問題）。

## 2. 方針

1. **中断の権限は入力。** SPEC §8.4.10.2 は環境効果を「権限を入力からではなく環境から得る操作」と定める。中断の権限を入力（タスク・パラメーター）にすれば、シグネチャーだけで中断できるかどうかが分かる。
2. **権限は値にしない。** タスク・パラメーターの名前は値を表さない。捕捉・格納・型引数・結果になれないことは、既存の規則から自動的に従う。
3. **未完了の計算を値にしない。** 活性（activation）は値ではない。移動もコピーも格納もしない。
4. **構造は呼出しの期間で保証する。** 子タスクは Kimi の操作の引数であり、その操作は子の完了前に戻らない。健全性は破棄の実行に依存しない。
5. **暗黙に実行されるコードは中断しない。** アクセサー・`drop`・既定値・静的初期化子・Deferred Block は中断しない。
6. **Kimi の操作は Kimigayo で書く。** コンパイラーが知る操作は、Storage 境界（SPEC §22.1.2.5）と同じ形の「タスク境界」の三つの intrinsic だけにする。
7. **単一スレッドから始める。** スレッドは第 3 段階で、構造化した退避として加える（本書 14）。

## 3. 言語規則

### 3.1. 規則 1：タスク・パラメーター（新設 SPEC §7.2.x）

1. **区分。** パラメーターの型が、Semantics・Origin・`?`・括弧を付けない名前そのものとして書かれ、その名前が宣言の同一性で `Kimi.Async.Task`（SPEC §22.1）に解決されるとき、そのパラメーターをタスク・パラメーターと呼ぶ。タスク・パラメーターはパラメーターの区分であり、Semantics でも値の型でもない（SPEC §7.1.1 の Place 結果を参照）。
   - 関数宣言・匿名関数・Function Type・Callable シグネチャー・Contract 要件に書ける。構築子（`init`）に書けるかどうかは、本書 18.6 の 3 で決める。
   - 一つのパラメーター・リストに一つまで。既定値を持たず、`self` にならず、外部関数の宣言には書けない。
   - 匿名関数でパラメーターの型を省略したときは、固定された期待シグネチャー（SPEC §7.6.1）から区分を受け継ぐ。
   - SPEC §22.1 の行：`Kimi.Async.Task` は、`Copy`・`Callable` の行と同じく、コンパイラーが同一性を指定する識別子であり、型・値・構築子を宣言しない。SPEC §9.2 の Core の役割は、パラメーターの型の位置全体に限って、この識別子も受け付ける（文法は変えない）。それ以外の場所でこの名前を使うと `CapabilityPosition_Kd` になる。
2. **名前。** タスク・パラメーターの名前は値を表さない。本体の中で、別のタスク・パラメーターへの引数としてだけ書ける（SPEC §10.1 の適用可能性で、タスク引数はタスク・パラメーターにだけ合う）。次の場所には書けない。
   - 既定値の式。
   - その関数が登録した Deferred Block の本体。中に書いた匿名関数は自分の関数境界を持つので（SPEC §16.1.1）、そのタスク・パラメーターは影響を受けない。
3. **互換性。** Function Type・Callable・Contract 実装の互換性は、各位置で同じ区分を要する。SPEC §7.1.1 で結果の区分が同じであることを求めるのと同じである。
4. **取得の形（SPEC §7.3.1）。** タスク・パラメーターは取得モードを持たない。照合キーは `Task` で、`Task` とだけ重なる。

### 3.2. 規則 2：タスク呼出し（SPEC §15.6.4 に追加、SPEC §22.2.3 から参照）

> タスク呼出しとは、選ばれた宣言・Function Type・Callable シグネチャーがタスク・パラメーターを持つ呼出しである。同じスレッドで、その呼出しより前からある他のタスクの活性が動くのは、タスク呼出しの間だけである。`Async.run` が始める木と `TaskBoundary.enter` が始める子は、その呼出しの中で呼ばれた側の一部として動き、その効果は呼出しの効果として比較される。呼出し元では、公開された要約や使える effect bound（SPEC §8.4.10.4）によらず、タスク呼出しを未知の環境効果を持つ呼出しとして比較する。比較の対象は、影響を受けうる生きた静的 Loan のすべてで、その呼出し自身の引数の Loan も含む。この比較は効果要約にも公開記録（SPEC §18.7.2）にも入らない。呼ばれる側自身の効果は通常どおり要約する。

「bound によらず」は省けない。省くと、SPEC §8.4.10.4 の規則 2 により `confined` の要件呼出しは環境効果なしと扱われる。すると、可変静的 Field への Loan を中断越しに保持でき、その間に別のタスクがその Field を置き換えると解放後使用になる（本書 7.2）。

### 3.3. 規則から従うこと

**区分であることから従う制限。** 初版の規則 2 が列挙していた禁止は、すべて既存の規則から従う。

| 初版の規則 2 の禁止 | 拒否する既存の規則 |
| --- | --- |
| 型引数（推論された型引数と関連型の指定を含む） | 型引数は完全な値の型でなければならない（SPEC §8.1.3） |
| 捕捉 | 捕捉は値の束縛である（SPEC §7.6.2） |
| 局所変数・Field・要素・結果 | どれも値の型を要する位置である |
| 結果の既定の入力 | 規則 1 の既定の入力は直接の借用入力だけである（SPEC §15.4.3）。規則 3 の「すべての入力の型」は、タスク・パラメーターを含まないと明記する（本書 16） |
| Callable の呼出しごとの Origin | 対象は `ref/T` と `uniq/T` のパラメーターだけである（SPEC §8.6） |
| 外部表現 | C ABI の表にない型は除外される（SPEC §22.3.2） |
| `noalias` のための表現の義務 | `ref`・`uniq` の安全な値の借用の引数ではないので、impl §21.5.5 の `noalias` の表の対象にならない（物理的な文脈ポインターには付けない、本書 12.1） |

**中断できない場所。** アクセサー（SPEC §11.2）、`drop`（パラメーターを持たない、SPEC §16.3.1）、静的初期化子、`main`（SPEC §22.2.2）は、タスク・パラメーターを持てないので中断しない。既定値と Deferred Block は規則 1 の 2 で除かれる。暗黙に実行されるコードは、どれも中断しない。

**中断をまたぐ借用。** 中断をまたいで局所変数を借用できることは、既存の規則から従う。
- 呼出しの保護は「呼ばれた側の後始末を含め、呼出し全体」に及ぶ（SPEC §15.6.4）。
- 局所変数の記憶域は、スコープの終わりか Move まで生きる（SPEC §5.2.1）。
- 例外はなく（SPEC §17.1）、Abort は巻き戻さない（SPEC §17.3.3）。

活性が動かないこと、つまりフレームの位置が固定されることは、下げ方の義務として impl §21 に置く（本書 12.1）。

**関数の色。** 色は宣言されたパラメーターの区分そのものであり、推論しない（SPEC §7.1、§10.5）。区分の違う関数どうしに変換はない。同期の実装がタスク・パラメーターを持つ Contract 要件を満たすときは、そのパラメーターを使わなければよい。

**局所性。** タスク呼出しは、選ばれた宣言だけで分かる（unsafe の呼出しを選ばれた宣言から判定する SPEC §7.5 と同じ）。タスク・パラメーターは本体のパラメーター・リストにしかないので、中断点は局所的に判定できる。

**なぜ Origin の規則では代わりにならないか。** 能力を `uniq` の借用にすると、捕捉した Reborrow は寿命の規則上は正しい（SPEC §7.6.2、§15.6.3）。そのため、同期の高階関数の枠の中で中断できてしまう。問題は能力が「いつまで生きるか」ではなく「どの枠の中で使われるか」にあるので、区分で表す。

```kimi
// 初版の uniq/Async.Task だと、寿命の規則だけでは拒否できない例
func apply<F>(f: uniq/F) -> ()
    F is Callable<uniq, () -> ()>
    f()                                                    // apply は中断できる枠ではない

func bad(task: uniq/Async.Task, delay: Time.Duration) -> ()
    var later = func [task, delay] () => _ = task.sleep(delay)   // 捕捉は Reborrow。寿命は正しい
    apply(later@uniq)                                       // apply の枠の中で中断してしまう
// 第 2 版では func [task] がそもそも書けない（task は値を表さない）
```

## 4. タスク境界（新設 SPEC §22.1.x、Kimi の義務）

タスク境界は、Kimi の中だけで使える内部の group である。宣言の同一性に結び付き、別名・再公開・同名の宣言ではその権限を得られない（SPEC §22.1.2.5 と同じ一文を置く）。コンパイラーが知る操作は三つだけである。

```kimi
// SPEC §22.1.x。コンパイラーが知る宣言（本体なし）
internal group TaskBoundary
    // 別のタスク文脈でタスク本体を動かす唯一の入口。完了したら true、中断したら false
    internal unsafe func enter<F, R>(context: raw/TaskRecord, body: F, slot: raw/R) -> bool
        F is Callable<owner, (Async.Task) -> R>
    // 今の活性のハンドルを waiter に記録して中断する唯一の操作
    internal unsafe func park(task: Async.Task, waiter: raw/Waiter) -> ()
    // 別の活性へ制御を移す唯一の操作。実行器の dispatch ループだけが呼ぶ
    internal unsafe func resume(handle: raw/Activation) -> ()
```

`join`・`race`・`each`・`pipe`・`run`、実行器・タイマー・IOCP・チャネルは、この三つと生記憶域（SPEC 第 5 章）の上に、普通の Kimigayo で書く。子の記録などの内部の型は、Kimi の関数の private な局所変数と Field にだけ現れる。`enter` は `body` を値で受け取って子の活性に移すので、子の依存を型で保つものはない。子の依存は、子を受け取った公開の操作の引数 Loan（SPEC §15.6.4 の呼出しの保護）と義務 2 によってだけ保たれる。

Kimi が負う義務：

1. **再開の規律。** 活性を二度再開しない。完了した活性を再開しない。活性を自分の内側から再開しない。完了前の活性を破棄しない。
2. **子の完了。** `enter` で開始した子は、その `enter` を実行した活性が完了する前に完了し、結果を取り出される。したがって、子はそれを開始した公開の Kimi 関数が戻る前に完了する。子と合流せずに戻る補助関数から `enter` を呼んではならない。
3. **待機の登録。** 待機の登録（実行待ちのリンク、タイマーのノード、OVERLAPPED、チャネルの待機者、合流の計数）は、待っている活性かタスク記録の中に置き、戻る前に外す。I/O の登録だけは、完了通知を受け取るまで残す。
4. **表現。** 他のタスクが状態を変えうるハンドル（`Async.Sender`・`Async.Receiver`）は、借用されている間インライン記憶域を書き換えない。変わる状態は、読み込んだポインターの先の Kimi 内部の生記憶域に置き、待機の intrinsic はその状態に対するコンパイラーバリアとする。`Async.Task` は値ではないので、この義務は要らない。
5. **取消し要求。** 次の一文を公開の定義とする。

> タスクに取消しが要求されると、その配下で待っている（または後で待つ）、取消しを観測する待機は、下の操作を取り消して完了させたうえで終わる。下の操作が取消しより先に完了していたとき（I/O の成功、`send` の値の受渡し、`receive` への項目の引渡し）は、その結果を返す。それ以外は `Err(Async.Cancelled)` を返す。`receive` は、完了したときにだけ項目を取り出す。取消しは活性を中断・巻き戻し・破棄せず、完了の種類も増やさない。

## 5. 公開 API（`Kimi.Async`）

```kimi
public group Async
    public struct Cancelled                  // フィールドなし。Copy・Owned。公開の初期化子なし（先例は Start・ResolvedRange）
        Self is Copy
        internal init() => ()

    public enum SendFailure
        Closed
        Cancelled(Async.Cancelled)

    public enum Raced<A, B>                  // 勝った子を Case で示し、両方の最終結果を持つ
        First(A, B)
        Second(A, B)

    public func run<F, R>(root: F) -> R
        F is Callable<owner, (Async.Task) -> R>

    public func sleep(task: Async.Task, duration: Time.Duration) -> Result<(), Cancelled>
    public func checkpoint(task: Async.Task) -> Result<(), Cancelled>

    public func join<A, B, RA, RB>(task: Async.Task, first: A, second: B) -> (RA, RB)
        A is Callable<owner, (Async.Task) -> RA>
        B is Callable<owner, (Async.Task) -> RB>

    public func joinOk<A, B, TA, TB, E>(task: Async.Task, first: A, second: B) -> Result<(TA, TB), E>
        A is Callable<owner, (Async.Task) -> Result<TA, E>>
        B is Callable<owner, (Async.Task) -> Result<TB, E>>

    public func race<A, B, RA, RB>(task: Async.Task, first: A, second: B) -> Raced<RA, RB>
        A is Callable<owner, (Async.Task) -> RA>
        B is Callable<owner, (Async.Task) -> RB>

    public func each<I, F>(task: Async.Task, items: I, child: ref/F ! limit: isize) -> ()
        I is Iterator
        F is Callable<(Async.Task, I.Item) -> ()>

    public func eachReceived<T, F>(task: Async.Task, items: uniq/Receiver<T>, child: ref/F ! limit: isize) -> Result<(), Cancelled>
        F is Callable<(Async.Task, T) -> ()>

    public func pipe<P, C, T, R>(task: Async.Task, producer: P, consumer: C ! capacity: isize) -> R
        P is Callable<owner, (Async.Task, uniq/Sender<T>) -> ()>
        C is Callable<owner, (Async.Task, uniq/Receiver<T>) -> R>
```

送信端と受信端（同じ `group Async` の中）：

```kimi
    public struct Sender<T>
        public func send(self: uniq/Self, task: Async.Task, value: T) -> Result<(), (T, SendFailure)>

    public struct Receiver<T>
        public func receive(self: uniq/Self, task: Async.Task) -> Result<Option<T>, Cancelled>
```

`join`・`joinOk`・`race` は、子の数ごとに多重定義する（可変長の型引数はない、SPEC §8.1）。各操作の契約は「決定の事象」で示す。どの操作も、すべての子と合流してから戻る。

| 操作 | 決定の事象 | そのときに残りの子へ | 取消しの観測 |
| --- | --- | --- | --- |
| `join` | 最後の完了 | 何もしない | しない |
| `joinOk` | 最初の `Err`（なければ最後の `Ok`） | 取消しを要求する | しない |
| `race` | 最初の完了 | 取消しを要求する | しない |
| `each` | 最後の完了 | 何もしない | しない |
| `eachReceived` | 受信端が閉じて空になり、最後の子が完了したとき | 何もしない | 受信の待機で観測する |
| `pipe` | consumer の完了 | 受信端を閉じ、producer に取消しを要求する | しない |
| `sleep`、`checkpoint`、`send`、`receive`、I/O | — | — | 観測する |

- **`joinOk` の結果。** すべての子と合流した後、決定の事象となった最初の `Err` を返す。どの子も `Err` を返さなければ、すべての結果を `Ok` の Tuple で返す。返さない子の結果は、`joinOk` の中で破棄する。
- **`joinOk` という名前。** 「すべての子が `Ok` のときだけ `Ok`」という成功の条件を表す。`try` を付けないのは、STYLE §3.2 の try 形が「同じ入力の操作の try 形」を意味するからである。この操作は `join` の try 形ではなく、兄弟の取消しという別の動作を足す。
- **try 接頭辞。** タスクを取り `Result` を返す Kimi の操作には、try 接頭辞を付けない。その `Result` は操作自身の失敗と、子以外の事象を待つ操作では取消しを報告する。前例は `WriteWindow.push` と `append` である（STYLE §5.2 に追記）。
- **`limit` と `capacity`。** 個数と容量は名前で渡す（STYLE §3.2 の `!` の規則）。`limit` は同時に動く子の数の上限で、1 未満なら Abort する。`capacity` はチャネルの容量で、0 はランデブー、0 未満なら Abort する（どちらも契約違反）。
- **`checkpoint`。** 取消しの要求があれば報告する。他に実行待ちのタスクがあるか、時間の区切りが来たときだけ、実際に譲る。

## 6. エフェクトの使い方

### 6.1. Kimigayo の effect

Kimigayo の effect はアクセスの権限を表し、制御の流れは表さない（SPEC §8.9、§8.4.10、§15.6.4）。権限の出所は二つに分かれる。

- **入力から得る権限。** シグネチャーで見える。
- **環境から得る権限。** 可変静的 Field や外部状態から得る。効果要約で扱う。

### 6.2. 中断の権限は入力で、値ではない

本書は、中断の権限を入力の側に置く。これは「能力としての効果」で、Effekt の能力渡し（第二級の能力）、Scala 3 の capture checking、Zig の明示的な `Io` パラメーターと同じ考え方である。さらに、権限そのものは値にしない（規則 1）。

- **ハンドラー。** タスク文脈を作った Kimi の操作（`run`・`join` など）がハンドラーに当たる。ハンドラーは字句的で、静的に決まる。
- **再開。** 再開は一度だけで、活性は値にならない。そのため、所有権の規則と矛盾しない。
- **制限された中断。** 型で表せる。タスク・パラメーターを受け取らない Callable は中断できない。

### 6.3. 中断中に起きること

中断している間は他のタスクが動く。これは「効果が未知の呼出し」と同じなので、既存の比較（SPEC §15.6.4）をタスク呼出しにそのまま適用する（規則 2）。比較は効果要約に入らないので、中断しない呼出し元へは広がらず、`confined` や `preserves results` の証明も壊さない。チャネルの端点の操作は入力から権限を得るので（SPEC §8.4.10.2 の最初の行）、環境効果ではない。

### 6.4. 採らなかった効果の方式

- **代数的効果とハンドラー（Koka、OCaml 5）。**
  - ハンドラーを動的に探すので、Principle 2 に反する。
  - 再開されない継続には、SPEC §16.2.3 にない後始末規則が要る。
  - Koka の複数回の再開は、Non-Copy の状態を複製する（OCaml 5 の継続は一度だけ再開できる）。
  - 例外や yield の効果は、第 17 章の失敗と LendingIterator（SPEC §22.1.2.1）を二重にする（Principle 1）。
- **`effect suspends` のような effect bound。** effect bound は実装を縛るだけである（SPEC §8.4.10.1）。中断は呼び方そのものを変えるので、この形に収まらない。本体から推論すると中断が見えなくなる（Principle 2）。
- **効果の多相、`reasync`。** 汎用の本体は一度だけ検証されるので（SPEC §8.10）、得るものが少ない。SPEC Appendix D が保留する「一般の効果システム」とともに保留する。

## 7. 書き方

### 7.1. 宣言と呼出し

```kimi
func greetLater(task: Async.Task, delay: Time.Duration) -> Result<(), Async.Cancelled>
    try Async.sleep(task, delay)            // 中断点：task をタスク・パラメーターに渡す
    Console.writeLine("hello")
    return .Ok(())

public func main() -> ()
    let delay = Time.Duration.init(microseconds: 1000000)
    _ = Async.run(func [delay] (task: Async.Task) => greetLater(task, delay))
```

- **規約（STYLE）。** タスク・パラメーターは `self` の直後、`self` がなければ最初に置き、名前は `task` とする。子として渡す匿名関数のパラメーターも `task` と名付け、親の `task` を隠す（SPEC §9.2）。同期版と非同期版の対（`Async` 接尾辞など）は作らない。
- **呼出し。** タスクを渡すことが、そのまま待つことになる。`await` は書かない。

### 7.2. 中断をまたぐ静的 Loan

```kimi
group Settings
    public var greeting: string = "hello"

contract Source
    func take(self: uniq/Self, task: Async.Task) -> u32
        effect confined

func show<S>(task: Async.Task, source: uniq/S) -> ()
    S is Source
    let text = Settings.greeting@ref
    _ = source.take(task)               // Error: タスク呼出し。confined でも未知の効果として比較する
    Console.writeLine(text)
```

### 7.3. 子タスク

```kimi
func fetchBoth(task: Async.Task, a: ref/Url, b: ref/Url) -> Result<(Page, Page), Net.Error>
    let getA = func [a] (task: Async.Task) -> Result<Page, Net.Error> => fetchPage(task, a)
    let getB = func [b] (task: Async.Task) -> Result<Page, Net.Error> => fetchPage(task, b)
    return Async.joinOk(task, getA, getB)   // 中断点。一方が失敗すると他方を取り消す

func fillBoth(task: Async.Task, left: uniq/Buffer, right: uniq/Buffer) -> ()
    let fillLeft = func [left] (task: Async.Task) => fill(task, left)    // 捕捉した uniq を通して変更する Exclusive な本体
    let fillRight = func [right] (task: Async.Task) => fill(task, right)
    _ = Async.join(task, fillLeft@move, fillRight@move)                  // 子は値で受け、一度の呼出しで消費する

func replyBoth(task: Async.Task, first: Net.Connection, second: Net.Connection) -> ()
    let a = func [first@move] (task: Async.Task) => respond(task, first@move)    // 捕捉を消費する Consuming な本体
    let b = func [second@move] (task: Async.Task) => respond(task, second@move)
    _ = Async.join(task, a@move, b@move)

func normalizeAll(task: Async.Task, rows: uniq/Array<Row>, limits: ref/Limits) -> ()
    let fix = func [limits] (task: Async.Task, row: uniq/Row) => normalize(task, row, limits)
    Async.each(task, rows.iterateUniq(), fix@ref, limit: 16)            // 並行に何度も呼ぶ子は共有で受ける
```

**子の受け方。** STYLE §4.3 の意図の行で決める。一度だけ呼ぶ子は呼出しで消費するので、`Callable<owner, ...>` で受ける。`owner` の取得は、Shared・Exclusive・Consuming のどの本体も受ける（SPEC §8.6）。`each` の子は並行に呼ぶので共有（`ref/F`）で受け、Shared の本体だけを受ける。`let` に置いた Non-Copy の子は `@move` で渡す。`each` の項目は、step に依存しない Iterator（SPEC §22.1.2.4）を要する。

利用者が書く高階関数がタスクを受け渡すのは、同じ鎖の中のタスク呼出しであり、タスク境界ではない。

```kimi
func retryOnce<F, R>(task: Async.Task, body: ref/F) -> Result<R, Net.Error>
    F is Callable<(Async.Task) -> Result<R, Net.Error>>
    match body(task)                        // 中断点
        .Ok(let value) => return .Ok(value@move)
        .Err(let error) => _ = error@move
    return body(task)                       // 中断点：もう一度だけ試す
```

### 7.4. 拒否される使い方

```kimi
func misuse(task: Async.Task, delay: Time.Duration) -> ()
    let saved = task                                            // Error: task は値を表さない
    let later = func [task, delay] () => _ = Async.sleep(task, delay)  // Error: 値でないので捕捉できない
    let maybe: Option<Async.Task> = .None                       // Error: Async.Task は値の型ではない
    Console.writeLine(task)                                     // Error: タスク・パラメーター以外に渡せない

struct Job
    let task: Async.Task                                        // Error: Field の型にならない

contract Bad
    func current() -> Async.Task                                // Error: 結果にならない
    func twice(first: Async.Task, second: Async.Task)           // Error: 一つのリストに一つまで
    func peek(task: ref/Async.Task)                             // Error: Semantics・Origin・? を付けられない

contract Step
    associate Ctx

struct Sleeper
    Self is Step
    associate Step.Ctx is Async.Task                            // Error: 関連型は完全な値の型を要する

func runLater<F>(body: F) -> ()
    F is Callable<owner, (i32) -> ()>
    body@move(0)

func caller(task: Async.Task, delay: Time.Duration) -> ()
    let wait = func [delay] (task: Async.Task) => _ = Async.sleep(task, delay)
    runLater(wait)                                              // Error: 位置 0 の区分が違う（タスク・パラメーターと i32）
```

タスクを受け取る子は、タスクを受け取らない Callable に渡せない。同じ数のパラメーターでも、区分が違えば互換にならない（規則 1 の 3）。

### 7.5. Contract

```kimi
public contract Reader
    func read(self: uniq/Self, task: Async.Task, buffer: uniq/Array<u8>) -> Result<u64, Io.Error>
```

非同期の実装も同期の実装も、同じ要件を満たす。同期の実装は `task` を使わない。

### 7.6. 後始末

```kimi
func appendLine(task: Async.Task, path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task, path)
    // defer => _ = file.flush(task)        // Error: Deferred Block はタスク・パラメーターを使えない
    try file.write(task, line)
    return file.flush(task)                 // 待つ後始末は名前付きの操作として通常の経路に書く
                                            // file の drop は同期的に閉じる（待たない）

// 早期 return の経路でも flush したいとき（STYLE の Advice）
func appendLineAlways(task: Async.Task, path: ref/string, line: ref/string) -> Result<(), Io.Error>
    var file = try Io.File.open(task, path)
    let written = file.write(task, line)    // try を付けず、取消し以外の失敗でも flush まで進む
    let flushed = file.flush(task)
    try written@move
    return flushed@move
```

`drop` が唯一の解放で、待たない。各 I/O の呼出しは完了まで Loan を保持するので、`drop` の時点で実行中の操作はない。解放の前に待つ必要がある処理（flush・正常な切断）は、`self: uniq/Self` とタスクを取る名前付きの操作にし、その `Result` を各経路で扱う。

`appendLineAlways` が flush まで進めるのは、取消し以外の失敗のときだけである。取消しが要求されていると、その要求は残り続けるので（本書 4 の義務 5）、flush の待機も `Err(Cancelled)` で終わる。取消しの下でも必ず待つ必要があれば、入れ子の `Async.run` を使える。新しい木には取消しが及ばないが、その間は外側の実行器が止まる（本書 11.1、STYLE では Advice のみ）。

### 7.7. 取消しの扱い

```kimi
group Net
    public enum Error
        Refused
        TimedOut
        Cancelled(Async.Cancelled)          // 取消しの Case は一つだけ（STYLE §5.1 に追記する規約）

func fetchAfter(task: Async.Task, url: ref/Url, delay: Time.Duration) -> Result<Page, Net.Error>
    match Async.sleep(task, delay)
        .Ok(_) => ()
        .Err(let reason) => return .Err(.Cancelled(reason))  // 変換はどの領域でも同じ形（SPEC §17.2.4）
    return fetchPage(task, url)

func sumAll(task: Async.Task, values: ref/Array<u64>) -> Result<u64, Async.Cancelled>
    var total: u64 = 0
    var index: isize = 0
    while index < values.length
        total += values[index]
        index += 1
        if index % 4096 == 0 => try Async.checkpoint(task)   // 4096 要素ごとに譲り、取消しを確かめる
    return .Ok(total)
```

`checkpoint` はタスク呼出しなので、そこをまたいで生きる値（`total`・`index`）はフレームに置かれる。同梱の LLVM 22.1.8 の CoroSplit は、これらを定義の直後にフレームへ書き出す。そのため、この例のように 4096 回に一度しか呼ばなくても、反復ごとにフレームへの読み書きが起こり、ループはベクトル化されない。同じコルーチンの中で内側のループに分けても同じである。塊ごとに呼んで減るのは、呼出しと切替えの費用だけである。

フレームの読み書きをなくせることは確かめた。中断点を含まない塊の計算を、コルーチンにインライン化されない普通の関数に切り出した場合だけ、ループはベクトル化された。この切り出しを STYLE の勧めにするか、コンパイラーが自動で行うかは impl §21 の未決事項とし、`src/Benchmark` で測る（本書 18.6）。

## 8. 所有権と Lifetime

### 8.1. 中断をまたぐ Loan

- **局所変数とパラメーター。** これらへの Loan は中断をまたいで保持できる（本書 3.3）。
- **他のタスクが触れられる経路。** 他のタスクがこのタスクの状態に触れる経路は四つしかない。
  - 子として渡された Loan。子の引数の依存として検査する（SPEC §8.6「F の依存はすべて保たれる」）。
  - 可変静的 Field と外部状態。規則 2 が扱う。
  - `rc`・`arc` の payload。共有アクセスしかできない（SPEC §13.5.5.2）。
  - Kimi 内部の生記憶域。Place を公開しない。
- **生ポインター。** 生ポインターに固定された Loan は上限を持たない（SPEC §5.2.2）。中断をまたぐ場合は、それを作った Unsafe Block の既存の一覧（SPEC §23.5.3）に義務の事実として載せる。

### 8.2. 子タスクの借用

- **子の Loan。** 子の Loan は、その操作が受け取った引数の依存である。
  - 一度だけ呼ぶ子（値で受ける）の Loan は、その操作が子を破棄するまで、またそれに依存する結果がある間、生きている。
  - `each`・`eachReceived` の子（`ref/F`）の Loan は、共有の引数 Loan として呼出しの間生きている（SPEC §15.6.4）。
- **共有借用と排他借用。**
  - 共有借用は `ref` のコピーとして子に配れる。
  - 排他借用は互いに重なってはならない。別々のパラメーター、Reborrow の捕捉、または領域分割された Iterator の項目から得る。
- **重なる排他捕捉。** 二つの子が同じ `uniq` を捕捉すると、通常の Loan の衝突になる（SPEC §7.6.2、§15.6.7）。
- **Owned の要否。** 切り離したタスクがないので、子に Owned は要らない。種類の異なる仕事を動的に送るときだけ、消去した Function 値（SPEC §7.6.4 の Owned 条件）を使う（本書 10.3）。

### 8.3. リークへの耐性

- **リークが健全性に影響しない理由。** 実行中の子を表す値（ハンドルやガード）は利用者の手に渡らない。生成器も値にしない（本書 10.2）。そのため、`rc` の循環（SPEC §16.4）や unsafe なリークがあっても、親の Loan が早く終わることはない。
- **早期終了。** Abort はすべての観測者を終わらせ、発散は戻らない。
- **要らないもの。** 型が Origin より長く生きることを表す制約（Rust の `F: 'a`）も、規範的なリーク禁止の規則も要らない。

### 8.4. 移動と Pin

- **Pin は要らない。** 活性は値ではないので、固定する概念が要らない。
- **既存の規則は変えない。** すべての値をバイト転送で移動するという規則（impl §21.4.5）はそのまま残る。

## 9. 取消しと失敗

- **取消しの発生源。** 取消しは列挙しない。Kimi の操作が契約（本書 5 の表）に従って要求する。
- **観測。** 子以外の事象を待つ Kimi の操作だけが観測し、`Err` で報告する。ただし、下の操作が取消しより先に完了していたときは、その結果を返す（本書 4 の義務 5）。`join` は観測しない。
- **`Cancelled` の形。** `Async.Cancelled` はフィールドなしの Copy・Owned な struct で、公開の初期化子を持たない。
  - 取消ししか失敗のない待機は `Result<T, Async.Cancelled>` を返す。
  - 領域ごとのエラー enum は、Case `Cancelled(Async.Cancelled)` を一つだけ持つ（STYLE §5.1 に追記する規約）。
  - 変換は、どの領域でも `.Err(let reason) => return .Err(.Cancelled(reason))` の形になる。
- **後始末。** 後始末は中断せず、取消しも観測しない。Deferred Block・破棄・確保済みの結果は SPEC §16.2 のままである。SPEC §16.2.3 の取消しの文はこの一文に替え、SPEC §15.9 の取消しの境界は解決済みとする。
- **Abort。** プロセスを終わらせる（SPEC §17.3.3）。
- **非同期の `drop` はない。**
- **時間の上限。** タイムアウトは `race` と `sleep` で書く。上限が効くのは、取消しを観測する操作を通る場合だけである。

## 10. 通信と非同期の列

### 10.1. チャネル

```kimi
func drain(task: Async.Task, inbox: uniq/Async.Receiver<Message>) -> Result<u32, Async.Cancelled>
    var count: u32 = 0
    loop
        match try inbox.receive(task)                  // 中断点
            .Some(let message) => handle(message@move)
            .None => return .Ok(count)                   // 閉じて空。以後も毎回 None
        count += 1

func forward(task: Async.Task, outbox: uniq/Async.Sender<Message>, message: Message) -> Result<(), Async.Cancelled>
    match outbox.send(task, message@move)
        .Ok(_) => return .Ok(())
        .Err((let rejected, .Closed)) => return keepForLater(rejected@move)   // 受け付けなかった値が戻る
        .Err((_, .Cancelled(let reason))) => return .Err(reason)
```

- **`receive`。** 閉じて空なら `Ok(None)` を返し、以後も None を返す（SPEC §22.1.2.3 の Iterator と同じ）。成功の形は `Iterator.next` と同じで、届いた項目は受信端の借用にもチャネルの記憶域にも依存しない（Kimi の義務、SPEC §15.6.3 の独立性）。
- **`send`。** `Dictionary.tryInsert` と同じく、受け付けなかった値を `Err` の Tuple で返す（STYLE §5.1）。
- **効果。** 端点の操作は入力から権限を得るので環境効果ではなく、`confined` の実装でも使える。端点どうしの結び付きは、タスク呼出し（規則 2）だけで扱える。
- **共有した変更を作らない。** 端点は `uniq/Self` の受け手で操作する。`ref/Self` で変更する API は作らない。作ると、共有経路からの変更という新しい可変性の概念が入り、`rc` を通って Owned でない循環も作れてしまう。

### 10.2. pipe と生成器

```kimi
func sumEvens(task: Async.Task, limit: u64) -> Result<u64, Async.Cancelled>
    let produce = func [limit] (task: Async.Task, out: uniq/Async.Sender<u64>) -> ()
        var n: u64 = 0
        while n < limit
            match out.send(task, n)
                .Ok(_) => ()
                .Err(_) => exit                // 消費側が終わった（Closed）か、取り消された
            n += 2
    let consume = func [] (task: Async.Task, inbox: uniq/Async.Receiver<u64>) -> Result<u64, Async.Cancelled>
        var total: u64 = 0
        loop
            match try inbox.receive(task)
                .Some(let value) => total += value
                .None => return .Ok(total)
    return Async.pipe(task, produce, consume, capacity: 0)
```

`pipe` は、自分の活性の中にチャネルの状態を作り、両端を `uniq` で二つの子に貸す。どちらの子も本書 4 の義務 2 に従う。
- **容量。** 容量が 1 以上なら、`capacity` 個の項目を入れる環状バッファーを pipe のタスクのアリーナから取る（定常状態ではヒープ割当ては 0）。フレームの大きさはインスタンスごとに固定なので、実行時の容量をフレームには置けないからである。容量が 0 未満なら Abort する。
- **残った項目。** pipe が戻るとき、バッファーに残った項目はチャネルとともに破棄する。
- **consumer が完了したとき。** 受信端を閉じて producer に取消しを要求し、合流してから consumer の結果を返す。
- **producer が先に戻ったとき。** 送信端が閉じ、`receive` は `Ok(None)` を返す。
- **pipe 自身が取り消されたとき。** 両方の子に取消しを伝える。

生成器の値を作らないので、利用者が中断中の活性を持つことも、リークの問題もない。止め方は、既存の `Closed` と `Cancelled` だけで済む。容量を 1 以上にすれば、バッファー付きのチャネルになる。容量の引数は、検証した生成器の案（容量 0 だけ）に本書が加えたもので、チャネルを作る方法を `pipe` 一つにまとめる。`for` で回せる同期の生成器は、別の提案とする。

### 10.3. 動的に来る仕事

```kimi
func serve(task: Async.Task, listener: uniq/Net.Listener) -> Result<(), Async.Cancelled>
    let accept = func [listener] (task: Async.Task, out: uniq/Async.Sender<Net.Connection>) => acceptLoop(task, listener, out)
    let handleAll = func [] (task: Async.Task, inbox: uniq/Async.Receiver<Net.Connection>) -> Result<(), Async.Cancelled>
        let handle = func [] (task: Async.Task, conn: Net.Connection) => respond(task, conn@move)
        return Async.eachReceived(task, inbox, handle@ref, limit: 64)   // 受け取るごとに子を一つ。同時に最大 64
    return Async.pipe(task, accept@move, handleAll, capacity: 16)

// 種類の異なる仕事は、消去した Function 値で送る（消去には SPEC §7.6.4 の Owned 条件がかかる）
func runJobs(task: Async.Task, jobs: uniq/Async.Receiver<(Async.Task) -> ()>) -> Result<(), Async.Cancelled>
    let runOne = func [] (task: Async.Task, job: (Async.Task) -> ()) => job(task)
    return Async.eachReceived(task, jobs, runOne@ref, limit: 8)
```

切り離したタスク（初版の `detach`）は作らない。動的に来る仕事は、受信端を受け取る `eachReceived` で扱う。`eachReceived` は受信を待つ間は取消しを観測し、取り消されたら受信をやめ、すべての子と合流してから `Err` を返す。

### 10.4. 非同期の列の正準形

非同期の列は二つの形をとり、どちらも `loop` と `match` で受ける。

- **引く形。** タスクを取る呼出しを繰り返す（例：`reader.readLine(task)`）。バッファーにデータがあれば、中断せずに返る。新しい規則は要らない。
- **タスクをまたぐ形。** `Async.Receiver<T>`（本書 10.1）。

`for` は変えない（SPEC §14.6.2）。`for` が呼ぶ `LendingIterator.next(self: uniq/Self during step)` はタスク・パラメーターを持たず、タスクを Field に持つ反復子も作れないからである。`AsyncIterator` のような並行するプロトコルも作らない。

```kimi
func countLines(task: Async.Task, reader: uniq/Io.LineReader) -> Result<u64, Io.Error>
    var count: u64 = 0
    loop
        match try reader.readLine(task)
            .Some(_) => count += 1
            .None => return .Ok(count)
```

## 11. 実行器と実行時

### 11.1. `run` の定義と、スレッドごとの実行器の状態

> `Async.run(root)` は、現在のスレッドで、新しいタスクの木として `root` を始める。戻るまで、その木の活性だけが動く。木の活性がなくなったら `root` の結果を返す。

```kimi
func loadConfig(path: ref/string) -> Result<Config, Io.Error>      // 同期の関数
    return Async.run(func [path] (task: Async.Task) => readConfig(task, path))
```

- **入口は一つ。** 同期の関数から非同期の操作を使う入口はこれだけである。タスクの中で呼んでも、特別な規則はない。
- **定義から従うこと。**
  - 外側の木のタスクは `run` の中で動かないので、初期化中の静的 slot（SPEC §22.2.3）を他のタスクが見ることはない。木の中での再入は、同期の再帰と同じく同じスレッドの循環として Abort する。
  - `run` はタスク・パラメーターを持たないので、タスク呼出しではない。
  - `run` はスレッドごとの反応器に触れるので、`confined` ではない（SPEC §8.4.10.2）。ただし、その内部状態は利用者の Loan と衝突しない。
  - 進めない木は発散する。それを検出するのは任意の診断であり、Abort の原因にはしない。
- **スレッドごとの状態。** 実装は、スレッドごとに一つの実行器の状態を Kimi 内部の生記憶域に持ち、`run` をまたいで保持する。
  - 中身：完了ポート（最初に結び付けるときか、最初にブロックして待つときに作る）、取り出し用の配列、実行待ちの先頭、タイマー構造、アリーナの空きリスト、`run` のスコープの積み重ね。
  - 入れ子の `run` はスコープを一つ積むだけである。外側のタスク宛ての起床（完了通知・タイマー・チャネル）は、そのタスクのスコープの保留リストに入れる。スコープが終わったら、再開するスコープの保留分だけを戻す。
  - 根が同期的に完了したら、ポートもタイマーも見ずに戻る。
- **一つのポートが必須である理由。** Windows では、ハンドルを結び付けられる完了ポートは閉じるまで一つだけである。そのため、`run` ごとにポートを作る方式は成り立たない。

### 11.2. 待機の登録とタイマー

```text
sleep(task, d) の概略（Kimi.Async の内部、Kimigayo で書く）：
    var node = TimerNode(deadline = QPC の読取り + d を tick に切り上げ)   ← 待っている活性の局所変数
    タイマー構造に node をつなぐ                                        ← 実行器はリストの先頭だけを持つ
    park(task, node.waiter)                                              ← 中断点
    node を外す（完了・取消しのどちらでも、戻る前に必ず）
```

- **waker。** 本書 4 の義務 3 により、waker は登録のポインターで済む。参照計数・世代番号・割当ては要らない。
- **実行待ち。** `queued` ビット付きの侵入型 FIFO にする。
- **取消し。** 子のリンクをたどってフラグを立てる。
  - タイマー・チャネル・実行待ちの登録は O(1) で外し、タスクを実行待ちに入れる。
  - I/O だけは `CancelIoEx` を発行し、完了通知を受け取るまで登録を残す。通知の結果（成功を含む）で完了する。
- **タイマーの構造。** 階層型タイミングホイールか侵入型 pairing heap を、タイムアウトの多い benchmark で選ぶ。
- **仕様に書くこと。** 締切は呼出し時の QPC の読取り（SPEC §22.7）から決め、`d` より早く起こさない。タイマーの登録で割り当てない。
- **早く起こさないために。** 待機の時間切れは時計の刻みのため早く来ることがあるので、待機から戻ったら QPC を読み直し、締切を過ぎたノードだけを起こす。待機の時間は、残りをミリ秒に切り上げ、INFINITE（0xFFFFFFFF）未満に抑える。

### 11.3. IOCP

```kimi
internal group IoCompletion
    #LibraryImport("kernel32", "SetFileCompletionNotificationModes")
    internal unsafe func setFileCompletionNotificationModes(handle: raw/(), flags: u8) -> i32

    #LibraryImport("kernel32", "GetQueuedCompletionStatusEx")
    internal unsafe func getQueuedCompletionStatusEx(port: raw/(), entries: raw/OverlappedEntry, count: u32, removed: uniq/u32, milliseconds: u32, alertable: i32) -> i32
```

```text
read(task, file, buffer) の概略：
    ok = ReadFile(..., OVERLAPPED = この活性の中)
    すぐ成功した、かつ通知の省略を設定できたハンドル → その場で完了（中断しない、ポートにも触れない）
    すぐ成功した、ただし通知の省略がないハンドル → 通知が来るので、待機を登録して park（中断点）
    ERROR_IO_PENDING → 待機を登録して park（中断点）

実行器の一回り：
    実行待ちのスナップショットを動かす
    GetQueuedCompletionStatusEx(port, entries[64], timeout = 実行待ちがあれば 0、なければ次のタイマーか INFINITE, alertable = 0)
    取り出した全エントリを先に待機者へ対応付けて実行待ちに入れ、それからタスクを動かす
```

- **結び付け。** ハンドルを開いたときに一度だけポートに結び付け、`FILE_SKIP_COMPLETION_PORT_ON_SUCCESS | FILE_SKIP_SET_EVENT_ON_HANDLE` を設定する。設定に成功したハンドルにだけ印を付け、その印があるときだけ即時の完了を使う。印のないハンドルでは、すぐ成功しても通知が来るので、通知まで待つ。そうしないと、活性が戻った後に、通知が活性の中の OVERLAPPED を指してしまう。
- **重ね合わせでないハンドル。** null の OVERLAPPED で `ReadFile`・`WriteFile` を同期的に呼ぶ経路に戻す。現仕様の同期の経路は標準出力の書込み（SPEC §22.5.3）だけなので、一般のファイルの同期 I/O の経路は `Io` の設計で新設する。
- **ソケット。** そのソケットのプロトコル情報（`SO_PROTOCOL_INFOW` の `dwServiceFlags1`）が `XP1_IFS_HANDLES` を持つ場合だけ、このモードを使う。libuv は、アドレス族ごとに試しのソケットを作ってこれを調べる。
- **取り出し。** 完了は最大 64 件まとめて取り出す。取り出した分は、どのタスクを動かすより前に全部実行待ちへ移す。入れ子の `run` が同じ配列を上書きするからである。
- **APC は使わない。** `alertable = 0`（SPEC §22.3.1）。
- **宣言の置き場所。** `Kimi.Windows`（SPEC §22.7.1）と同じく普通の `#LibraryImport` で書き、SPEC §22.5.6 の実行時シンボルの表には入れない。BOOL は `i32`、HANDLE は不透明なアドレスとして `raw/()`（STYLE §4.5、SPEC §22.3.2）で渡す。

### 11.4. アリーナ

```text
タスクを開始：塊はまだ取らない
最初に積むとき：max(フレーム + ヘッダー, 最小の段) 以上の最小の段の塊を取る
あふれたとき：max(前の段の 2 倍, 必要な大きさ) の段を取る       ← 深さ d でも塊は O(log d) 個
一番上の塊が空になったとき：予備として一つだけ残す              ← 境界をまたぐループでも取得と解放を繰り返さない
タスクの完了：塊をスレッドごとの段別の空きリストに返す（run をまたいで保持し、上限か高水位で間引く）
```

- **静的には決めない。** フレームの大きさは CoroSplit の後でないと分からない（前段の IR からは見えず、O0 と O2 でも違う）ので、静的に大きさを決める案は採らない。
- **子のアリーナ。** 子は並行に動くので、親のアリーナを後入れ先出しで分け合えない。子はそれぞれ自分の塊を遅延して取る。固定個数の子は、本書 12.3 の埋め込みで塊を取らないことが多い。
- **置き場所。** これは実装の方針なので、SPEC ではなく impl §21 か `src/Kimi/Library/README.md` に書く。

## 12. 実装と性能

### 12.1. 下げ方と内部 ABI（impl §21）

- **ABI。** タスク・パラメーターを持つ関数は、一つの内部 ABI `entry(task, continuation, args..., resultSlot) -> i1`（true は完了）を使う。物理的なタスクは、コンパイラーが選ぶ文脈ポインターで、`noalias` を付けない（impl §21.4.2）。
- **普通の関数にできるインスタンス。** インスタンスの呼出しグラフを、強連結成分ごとに下から計算する。中断の intrinsic を含まず、間接のタスク呼出しも、普通の関数でないインスタンスへのタスク呼出しもない本体は、普通の関数（`llvm.coro.*` を出さない）として常に「完了」を返す。
  - O2 では、LLVM がすでにこれを畳む（同梱の LLVM 22.1.8 で確認）。
  - 効果は主に O0 のコード量とアリーナの積み下ろしである。コルーチンを含まないモジュールでは、opt の段を省ける。
- **それ以外の本体。** switched-resume コルーチンに下げる。フレームはタスクのアリーナに置き、生きている間は動かさない。
- **借用パラメーター。** 普通の関数でない入口の借用パラメーターには、`captures(none)` を付けない。活性が物理的な return の後も保持するからである。
- **O0 のパイプライン。** `llc -O0` は `llvm.coro.*` を下げられない（同梱の llc は「Do not know how to promote this operator!」で止まる）。再開可能な本体を含むモジュールでは、`llc -O0` の前に opt で `-passes=coro-early,cgscc(coro-split,coro-annotation-elide),coro-cleanup` を走らせる。opt はこの文字列に空白を入れると受け付けない。これをツールチェーンのマニフェストに記す。

### 12.2. 完了の手順

```llvm
define internal i1 @g(ptr %task, ptr %cont, ptr %slot) presplitcoroutine {
  ; ... coro.id / coro.alloc（アリーナに積む）/ coro.begin で %hdl を得る ...
  %done = call i1 @wait_register(ptr %task, ptr %hdl)  ; 待たずに済めば complete へ
  br i1 %done, label %complete, label %wait
wait:
  %s = call i8 @llvm.coro.suspend(token none, i1 false)
  switch i8 %s, label %suspend [i8 0, label %complete
                                i8 1, label %never]     ; 破棄の経路はない
complete:
  store i32 42, ptr %slot                               ; 結果は呼出し側の slot に置く
  %inramp = call i1 @llvm.coro.is_in_ramp()
  br i1 %inramp, label %end, label %transfer            ; 同期の完了なら、そのまま「完了」を返す
transfer:
  call void @set_next(ptr %task, ptr %cont)             ; 再開後の完了なら、親を直接再開させる
  br label %end
suspend:
  br label %end
end:
  %st = phi i1 [true, %complete], [true, %transfer], [false, %suspend]
  call void @llvm.coro.end(ptr %hdl, i1 false, token none)
  ret i1 %st
never:
  unreachable                                           ; 完了前の活性は破棄しない（本書 4 の義務 1）
}
```

1. **結果。** 結果は呼出し側が渡す slot に置く。
2. **破棄の経路はない。** `coro.suspend` の破棄側は `unreachable` にする。最後の中断点はなく、完了は普通の後始末を通って一つの `coro.end` に着く。O2 では destroy と cleanup の複製は空になるが、O0 ではコード量として残る。その量は測定値として記録する。
3. **アリーナの位置。** 呼出し側が保存し、完了時（同期でも再開後でも）に戻す。呼ばれる側はフレームを解放しない。呼ばれる側が普通の関数なら、保存も復元も省ける。
4. **親の再開。** 再開後に完了したときは、継続をタスク記録の `next` に置いて戻る。実行器はそれを同じ回ですぐ再開し、実行待ちの列を通さない。実行待ちの列が受け取るのは、待機（タイマー・I/O・チャネル・最後の子の完了）からの起床だけである。`llvm.coro.is_in_ramp()` で同期の完了と区別するので、フレームに印のビットは要らない。

### 12.3. 固定個数の子のフレームの埋め込み

```llvm
define internal i1 @join(ptr %task, ptr %cont, ptr %ta, ptr %tb, ptr %slots) presplitcoroutine {
  ; 子の記録（フラグ・リンク・結果 slot へのポインター）は join 自身の活性の局所変数
  %ra = call i1 @child(ptr %ta, ptr null, ptr %slots) coro_elide_safe
  %sb = getelementptr i8, ptr %slots, i64 4
  %rb = call i1 @child(ptr %tb, ptr null, ptr %sb) coro_elide_safe
  ; ... 両方の完了を待つ ...
}
; O2 と本書 12.1 の O0 段の結果：二つの子のフレーム（各 96 バイト）が join のフレームに入り
; （remark は frame_size=224・align=16、アリーナに積む llvm.coro.size は 216 バイト）、子はアリーナに積まない
```

- **付ける条件（構文的）。** どのループの本体にも `while` の条件にもない（繰り返し評価される位置にない）`TaskBoundary.enter` の呼出しは、一つの活性の中で高々一度しか動かない。また、本書 4 の義務 2 により、その子は `enter` を実行した活性より先に完了する。そこで、コンパイラーはこうした呼出しにだけ `coro_elide_safe` を付ける。これで `join`・`joinOk`・`race`・`pipe` は名前を挙げずに含まれ、ループの中で開始する `each` は自動的に外れる。
  - 条件分岐の中の `enter` は、一度しか動かないので含めてよい。
  - 再帰で書いたループは、再帰の各活性が義務 2 で自分の子と合流するので、安全である。
  - 義務 2 がなければ、子と合流せずに戻る補助関数で、子のフレームが補助関数のフレームに置かれる。補助関数が完了すると、そのフレームは再利用されてしまう（同梱の LLVM 22.1.8 で確認）。`while` の条件にある呼出しも、一つのフレーム領域を同時に生きる子が共有してしまう（同じく確認）。
  - 属性を付けた呼出しを含む関数をループの中へインライン化するときは、属性を外す。
- **効果。** LLVM の CoroAnnotationElide が、子の最上位フレームを親のフレームの中に置く。末端の子は、アリーナにもプールにも触れない。
- **条件。**
  - 子の本体を noinline にしない（`.noalloc` が作られないため）。
  - O0 のコルーチン段にもこのパスを入れる（本書 12.1）。これで O0 と O2 の振る舞いがそろう。
  - 内部の切替えで属性を外せるようにする。
- **普通の呼出しには付けない。** 順に呼ぶ呼出しのフレームが重ならず、足し合わされるからである。
- **検査は依存させない。** 機能と割当て回数の検査は、この最適化に依存させない。省略の回帰は、CoroSplit の remark で確かめる。

### 12.4. フレームの中身

- **lifetime マーカー。** マーカーがないと、借用で渡した局所変数は、エスケープした alloca としてすべてフレームに入る（確認済み）。コルーチンに下げたインスタンスでは、次の位置に `llvm.lifetime.start`/`end` を出す。
  - start：slot を最初に初期化する位置。
  - end：証明できる最も早い位置。スコープの終わりか、最後の使用の後で、アドレスが観測されず、破棄の責任も生きた Loan もない位置（impl §21.5.5）。
- **誤ったコードを生まないための条件。** 再開可能な呼出しに貸した記憶域の最後の使用は、その呼出しが完了した後の、呼出し側の再開位置とする。呼出しの命令の位置にしてはならない。そうしないと、呼ばれた側のフレームが指したまま、記憶域がランプのスタックに残る。
- **規則 2 の検査の費用。** 本体ごとに、可変静的 Field に固定された Loan のマスクを前もって作る。各タスク呼出しでは、それと生きた Loan のビット集合の AND を取るだけで済む（docs/dev の実装指針）。

### 12.5. 割当ての上限

| 操作 | ヒープ割当て |
| --- | --- |
| タスク呼出し | 0（アリーナの積み増し。呼ばれる側が普通の関数なら積まない） |
| 固定個数の子（O0・O2 とも埋め込みが効く場合） | 0（親のフレームの中） |
| タスクの開始、`each` の子 | プールした記録とアリーナの塊。定常状態では 0 |
| タイマー、チャネル操作、I/O の待機 | 0（登録は活性の中、リングバッファー） |
| `pipe` の開始 | 容量 0 なら 0。容量 1 以上ならアリーナの積み増し（定常状態では 0） |
| `run` の繰り返し | 定常状態では 0（スレッドごとの状態を保持） |

これらの上限は、`NativeAllocationAudit` 型の O0/O2 fixture で、`Purpose=Allocation` の回帰として固定する。主な fixture は次の四つ。

- 塊の境界をまたぐ呼出しを N 回繰り返すループ
- N 回の `Async.run`
- すぐ完了する読取りのループ（ポートの呼出しが 0 回）
- パイプの往復（一回りにポートの呼出しが高々 1 回）

時間の測定は `src/Benchmark` で行う。測るのは、`run` 一回あたりの費用、深さを変えたときの費用、タイムアウトの多い負荷の三つ。

## 13. コンパイラーサービス

| 診断 | 主範囲 | 関連位置 | 修復 |
| --- | --- | --- | --- |
| `ComparisonLoanConflict_Kd`（既存の、Loan の衝突一般の code）、Reason `suspension point (other tasks)` | タスク呼出しのタスク引数 | `loan`、`static`、`suspension`、`use` | Advice のみ（先にコピーする、中断後に借り直す） |
| `CapabilityPosition_Kd`（新設、規則 1 の唯一の code） | 不正な名前・型・捕捉・束縛 | タスク・パラメーター | Advice（タスク・パラメーターを受け取る。シグネチャーが変わる） |
| タスクの渡し忘れ（通常の引数の誤り） | 呼出し | 呼ばれる側の宣言 | `Repair.PassCapability`（新設、本書 13 の条件のときだけ）。それ以外は Advice（パラメーターを足す、`Async.run` を使う） |
| 段階より先の形 | — | — | `Unsupported` 系の code（SPEC §23.3.6.1） |

- **code の明記。** SPEC §15.6.4 の静的な呼出し効果の比較には、今は code の名前が付いていない。同じ変更で、SPEC §15.6.4 と §23 に、静的な呼出し効果の比較とタスク呼出しの比較の code として `ComparisonLoanConflict_Kd` を明記する。
- **一つの Loan に一つの記録。** 同じ呼出しで、導出された効果がすでに衝突する場合（Contract 要件の呼出しでは SPEC §8.4.10.6 の `CallEffectConflict_Kd`、それ以外は自分の要約による `ComparisonLoanConflict_Kd`）は、中断をその記録の Reason 値として加える（SPEC §23.3.6.4）。Reason は、使える bound（`confined`・`preserves results`）がこの比較を除かないことも述べる。
- **`Repair.PassCapability` の条件。** 次をすべて満たすときだけ、その候補のタスク・パラメーターの位置にタスクの名前を差し込む（`UsageLegality`: required）。それ以外は Advice とする（SPEC §23.3.6.9）。
  - 呼出しを直接含む最も内側の関数（匿名関数を含む）がタスク・パラメーターを持つ。
  - 呼出しが、その関数の登録した Deferred Block の本体にも、既定値の式にもない。
  - タスク引数を足すと適用可能になる候補が、ちょうど一つである。
- **`CapabilityPosition_Kd` の位置。** 位置（捕捉・局所変数・型引数・Deferred Block の本体など）は Reason 値で示す。文面は `UnsafeFunctionValue_Kd` の形に合わせる。
- **中断点の一覧（CSP）。** 中断点ごとに、シグネチャーと本体から局所的に得られる事実を列挙する。
  - タスク・パラメーター。
  - 中断点をまたいで生きる Loan と所有値。
  - 取消しを報告しうるかどうか。選ばれた呼ばれる側の、具体化した結果の型から、enum の payload、Tuple の要素、`Option`/`Result` の型引数をたどって、`Kimi.Async.Cancelled`（宣言の同一性で識別）に届くかどうかで決める。可能性の事実であり、抽象的な型引数では不明とし、本体からは導かない（SPEC §18.7.2）。
  - 子を開始する呼出しでは、子が捕捉する Loan とその領域。
- **生成の事実。** インスタンスが普通の関数かどうかは、単一化したインスタンスの呼出しグラフから求める生成の事実である（本書 12.1）。局所的な事実とは分けて、インスタンスに結び付けて示す。
- **フレームの大きさ。** 検査の事実ではない。CoroSplit の remark から読む測定値として、O レベルとツールチェーンの同一性に結び付ける（SPEC §23.5.3）。
- **hover。** SPEC §8.4.10.6 の型付きの点検ブロックを使う。

## 14. スレッド（第 3 段階の方向性：構造化した退避）

```kimi
func checksumAll(task: Async.Task, blocks: ref/Array<Block>) -> u64
    let work = func [blocks] () -> u64 => checksum(blocks)    // タスクを受け取らない。親の局所データを借用してよい
    return Async.offload(task, work)                           // 中断点。作業スレッドで最後まで走り、合流して戻る

// Kimi 側の宣言。Callable 制約への effect bound は、今は保留中の機能（仮の書き方）
public func offload<F, R>(task: Async.Task, child: F) -> R
    F is Callable<owner, () -> R>
        effect confined

// 拒否される例
//  func [config@ref] () -> u64 => use(config)    // Error: 入力が rc に届く（スレッドをまたげない）
//  func [] () -> u64 => Metrics.count            // Error: confined が可変静的 Field を禁じる
```

1. **操作。** `Async.offload(task, child)` と `Async.offloadEach(task, items, child: ref/F, limit)` を加える。
   - どちらもタスク呼出しであり、子は作業スレッドで最後まで走り、合流してから戻る。
   - `offloadEach` は Iterator を要し、`next` を親のスレッドで呼ぶ。
2. **借用。** 親は合流するまで中断しているので、子は親の局所変数を `ref` や `uniq` で借用できる。根拠は Rust の `thread::scope` と同じで、第 3 段階で加える Kimi の義務が与える。その義務は、作業スレッドの子は、それを開始した `offload` の活性が完了する前に完了し合流する、というものである。`offload` の子は `enter` を通らないので、本書 4 の義務 2 とは別に必要になる。Owned は要らない。
3. **子の効果。** 子には `confined` を要求する。これで、可変静的 Field・外部呼出し・Console・不変静的からの生ポインターの読取りは、すでに禁じられる。初版の第 3 段階にあった、スレッドごとの静的 Field の規則は要らない。そのために、Callable 制約に既存の effect bound を書けるようにする（SPEC §8.4.10.1 と Appendix D を改める）。
4. **移送の述語。** 子の入力（捕捉・引数・`offloadEach` の項目の型）に、宣言させず導出する述語を課す。
   - OwnedOrigins（SPEC §15.2.3）と同じ走査で、rc、rc の Weak、利用者の生ポインターに届かないこと。
   - 隠れた中身を走査できない、消去した Function Type、実行時 Contract の View、基底オブジェクトの View（SPEC §15.8.1）は越えられない。
   - Kimi 内部の生ポインター（SPEC §22.1.2.5 の剰余など）は、宣言の同一性に結び付いた Kimi の義務として通す。
   - 結果 `R` には条件を課さない。
5. **confined の表の引締め。** 不変静的から rc のハンドル（またはその Weak）を含む値を読むことを、環境効果にする。生ポインターの行と同じ理由による。
6. **実行時。** 静的 slot の初期化を、スレッドをまたぐ一度だけの手順にする。他のスレッドが初期化中なら待ち、同じスレッドでの循環だけを Abort とする。
7. **メモリーモデル。** 一文でよい。安全なコードにはデータ競合がなく、同期は `offload` の開始と合流、`arc` の手順（impl §21.2.3.3）でだけ生じる。
8. **保留するもの。** ブロックする外部呼出しの退避は、外部関数の効果宣言（SPEC Appendix D で未導入）を待つ。スレッドごとの I/O 実行器とスレッド間チャネルは、同じ述語で後から加えられる。重ね合わせのハンドルは、開いたスレッドのポートに結び付いたままになることに注意する。

## 15. 他言語との比較

| 観点 | C# | Rust | Swift | Go | Kotlin | 本書 |
| --- | --- | --- | --- | --- | --- | --- |
| 中断の仕組み | ヒープの状態機械 | Future 値（インラインの状態機械）と `Pin` | タスクの割当て器から取るフレーム（値でない） | スタックフル（スタックを複製） | CPS 変換と `Continuation` | スタックレス、固定アドレスの活性（値でない） |
| 印 | `async`/`await` | `async`/`.await` | `async`/`await` | なし | `suspend`（呼出し側に印なし） | タスク引数（パラメーターの区分） |
| 未完了の計算の値 | `Task` | `Future` | `Task`、`async let` | なし | `Deferred` | なし |
| 構造化 | 慣習（`WhenAll`） | `thread::scope` のみ健全。非同期の scoped task は forget で不健全 | `TaskGroup`、`async let`（切り離しもある） | `errgroup`（慣習） | `CoroutineScope`、Job の木（`GlobalScope` もある） | 構造化のみ。子は Kimi の呼出しの引数 |
| 取消し | `CancellationToken` と例外 | Future の破棄（取消し安全性の問題） | 協調フラグと `CancellationError` | `context.Context` | `CancellationException` | 協調フラグ、`Result` の値。後始末は観測しない |
| スレッド安全 | 検査なし | Send・Sync の auto trait | `Sendable`、`sending`、actor | 検査なし（race detector） | Dispatcher | 第 3 段階：`confined` と導出する移送の述語 |
| 実行器 | スレッドプール、SynchronizationContext | ライブラリごとに分かれる | 標準 | 標準の M:N | Dispatchers | 標準の一つ（スレッドごとの状態） |
| 生成器 | `yield return`（別の仕組み） | `gen`（不安定、別の仕組み） | `AsyncStream` | push 型の反復子 | `sequence {}`（制限された中断） | `pipe`（同じ仕組み、値を作らない） |

取り入れたもの：

- **Rust：** 単一化による静的なフレームの大きさ。中断をまたぐ借用の精密な検査（`Pin` なし）。`thread::scope` 型の借用（第 3 段階の `offload` にも）。
- **Swift：** 動かないフレームとタスクごとのアリーナ。協調的な取消し。
- **Kotlin：** 構造化の木と、失敗したら兄弟を取り消す形（`joinOk`）。生成器を同じ中断の仕組みで書くこと（ただし値を作らない `pipe` として）。
- **Go：** 未完了の計算を値にしないこと。明示的に渡す文脈。所有権を受け渡すチャネル。実行時が一つであること。
- **Effekt：** 第二級の能力（値にならない権限）。
- **C#：** 取消しを明示的に扱う規律。ただし、例外ではなく値で表す。

採らなかったもの：

- **`Pin` と Future 値：** 二つ目の移動可能性の概念になる（Principle 1）。
- **Future の破棄による取消し：** 途中での完了が要る（SPEC §16.2.3）。
- **非同期の `drop`。**
- **actor：** 再入で局所的な推論が崩れる。
- **暗黙の現在タスク：** Swift の `Task.isCancelled`、Kotlin の `coroutineContext`。暗黙の文脈は Principle 2 に反する。
- **呼出し側に印のない中断：** Go、Kotlin。
- **例外。**
- **スコープを出るときの暗黙の待機。**
- **切り離したタスク。**
- **代数的効果のハンドラー：** 本書 6.4。

## 16. 仕様の変更箇所

| 箇所 | 変更 |
| --- | --- |
| 新しい章（第 24 章「中断と非同期タスク」、または第 22 章の拡張） | 規則 1・2 の要約と、本書 3.3 の導かれる事項への参照 |
| SPEC §7.2（新設 §7.2.x）、§7.3.1、§7.6.1 | 規則 1：区分、名前、互換性、照合キー `Task`、匿名関数での区分の受け継ぎ |
| SPEC §9.2 | Core の役割は、パラメーターの型の位置全体に限って `Kimi.Async.Task` の識別子も受け付ける（文法は変えない） |
| SPEC §10.1 | タスク引数はタスク・パラメーターにだけ合う |
| SPEC §15.4.3 | 規則 3 の「すべての入力の型」は、タスク・パラメーターを含まない（タスク・パラメーターは値の入力ではない） |
| SPEC §15.6.4 | 規則 2（タスク呼出しの比較）。静的な呼出し効果の比較とタスク呼出しの比較の code として `ComparisonLoanConflict_Kd` を明記する |
| SPEC §22.2.3 | 「その呼出しより前からある他のタスクの活性が動くのは、タスク呼出しの間だけ」（規則 2 への参照） |
| SPEC §16.1、§16.2.3、§15.9 | Deferred Block はタスク・パラメーターを使えない。後始末は中断せず取消しも観測しない。取消しの境界は解決済み |
| SPEC §22.1 | `Kimi.Async.Task` の行（区分を表す名前。型・値・構築子を宣言しない）。新設 §22.1.x タスク境界（三つの intrinsic と義務 1〜5）。`Kimi.Async` の宣言 |
| 変更しない箇所 | SPEC §8.1.3、§8.6、§22.3.2（区分は既存の規則で自動的に除かれる）。SPEC §2.5.1 と Appendix F（キーワード・文法の変更なし）。第 17 章 |
| impl §21 | 内部 ABI（状態を返す）、普通の関数にできるインスタンス、コルーチンへの下げ方、O0 のパイプライン、完了の手順、子のフレームの埋め込み、lifetime マーカー、`noalias` なしの文脈ポインター。impl §21.4.5 は変えない |
| impl §21 または `src/Kimi/Library/README.md` | 実行器・タイマー・IOCP・アリーナの実装方針（SPEC の義務にはしない） |
| SPEC §8.4.10.6、§23.3.6、§23.5.3 | `ComparisonLoanConflict_Kd` と `CallEffectConflict_Kd` の Reason `suspension point`、`CapabilityPosition_Kd`、`Repair.PassCapability`、中断点の一覧、測定値としてのフレームの大きさ、生ポインターの固定の事実 |
| SPEC Appendix D、E | §D.2 を「単一スレッドのタスクは規定済み、スレッドは保留」に改める。用語（タスク・パラメーター、タスク呼出し）を加える |
| `docs/STYLE.md` | タスク・パラメーターの位置と名前、子の受け方、`checkpoint` は塊ごと、取消しの Case、タスクを取り `Result` を返す操作に try 接頭辞を付けないこと、待つ後始末は名前付きの操作にすること |
| `docs/LIBRARY.md` | `Kimi.Async` の宣言 |
| `docs/SETTLED.md` | 採否の決定後に、採らなかった案（`async`/`await` キーワード、`Pin`、破棄による取消し、暗黙の待機、切り離したタスク、代数的効果のハンドラー、後始末の中での中断）を記録する |
| 第 3 段階 | SPEC §8.4.10.1（Callable 制約への effect bound）、§8.4.10.2 の表（不変静的からの rc の読取り）、静的初期化の手順、`offload` の子の完了の義務 |

## 17. 段階計画

1. **第 0 段階（仕様）。**
   - 規則 1・2 を書き、規則 1 の検査と `CapabilityPosition_Kd`、規則 2 の比較を実装する。
   - 実行は `Unsupported` と報告する。
2. **第 1 段階（健全な最小の部分集合）。**
   - 内部 ABI とコルーチンへの下げ方（O0 のパイプラインを含む）、タスク境界の三つの intrinsic。
   - `run`・`sleep`・`checkpoint`・`join`・`joinOk`・`race`・`each`、スレッドごとの実行器の状態とタイマー。
   - 診断と CSP の一覧。
   - **前提（`docs/STATUS.md` で未完了の項目）：**
     - Callable 制約（owner 取得の witness、任意の Callable のパラメーターと結果）
     - 集合値を捕捉する Closure
     - 借用の入口と lending loop のディスパッチ
     - 可変静的 Field（規則 2 を試すため）
   - 前提がそろうまでは、タスクを順に渡す呼出しと、スカラーだけを捕捉する Closure の `join` に絞る。それ以外の形は `Unsupported` とする。
3. **第 2 段階。**
   - チャネル（`uniq` の端点）、`pipe`、`eachReceived`。
   - IOCP によるファイル・パイプ・ソケット（高速経路、まとめての取り出し）。
   - I/O の待機の取消し（`CancelIoEx` を発行し、完了通知を待つ）。これで、第 1 段階から書ける `race` と `sleep` のタイムアウトが I/O にも効く。
4. **第 3 段階（SPEC §D.2）。** 構造化した退避（本書 14）。
5. **別の提案とするもの。**
   - `for` で回せる同期の生成器。
   - 複数の送信者のチャネルと `select`。
   - 決定的なスケジューラー（仮想時刻）。
   - Origin で上限を付けた消去。
   - 非同期の `main`（SPEC §22.2.2）。
   - 外部関数の効果宣言とブロックする呼出しの退避。

## 18. 設計判断

### 18.1. 初版からの変更

推奨順に適用した。「規則」は言語規則と Kimi の義務を合わせた規範の数の増減で、検証で見積もった概数である。

| 改善案 | 変更 | 本書 | 規則 |
| --- | --- | --- | --- |
| 1 | `Async.Task` をパラメーターの区分にする（綴りは宣言の同一性）。受け手の形をやめ、`Async.sleep(task, d)` にする | 3.1、3.3 | −4 |
| 2（13 を含む） | タスク呼出しを一つの定義にし、bound によらず比較する（穴を塞ぐ）。診断は既存の `ComparisonLoanConflict_Kd` と Reason 値。CSP は既存の形を再利用する | 3.2、13 | −3 |
| 8 | 一度だけ呼ぶ子は `owner`、`each` の子は `ref/F`。初版の §6.2 と §5.2 の矛盾を直す | 5、7.3 | 0 |
| 4 | 規則 5・6 と内部操作をタスク境界にまとめる。三つの intrinsic、義務 1〜5、決定の事象の表、取消し要求の一文 | 4、5 | −3 |
| 5 | `detach` をなくす。動的な仕事は `eachReceived` | 10.3 | −3 |
| 6 | 「能力型の閉じた一覧」を削る | 3.1 | −2 |
| 3 | 規則 4（再開可能な活性）を言語規則から外し、impl §21 の義務にする | 3.3、12.1 | −3 |
| 9 | Deferred Block は中断しない。後始末は取消しを観測しない | 3.1、7.6、9 | 0 |
| 10 | `tryJoin` を `joinOk` に改名する | 5 | 0 |
| 11 | `Cancelled` の形を決める。`checkCancelled` を `checkpoint` にまとめる | 5、7.7、9 | 0 |
| 12 | チャネルの形を `Iterator` と `Dictionary.tryInsert` にそろえる | 10.1 | 0 |
| 7 | `run` を一つの定義にする（入れ子の `run` の特例をなくす） | 11.1 | −2 |
| 16 | 待機の登録を活性の中に置く（本書 4 の義務 3） | 4、11.2 | +1 |
| 14、15、17〜21 | 完了の手順、子のフレームの埋め込み、IOCP、スレッドごとの状態、アリーナ、lifetime マーカー、普通の関数にできるインスタンス | 11、12 | 実装の方針 |
| 22 | 非同期の列は引く形と Receiver の二つ。`for` は変えない | 10.4 | 0 |
| 23 | 生成器は `pipe` で書く（容量を引数にしてバッファー付きのチャネルも兼ねる） | 10.2 | +1（ライブラリ操作） |
| 24 | 第 3 段階を構造化した退避にする（方向性） | 14 | −2（初版の第 3 段階との比較。`offload` の子の完了の義務を含む） |

結果として、言語規則として残るのは二つである（規則 1 は Deferred Block と既定値の除外を含む）。初版の規則 5・6 はタスク境界の義務 2・5 に、規則 4 の交互実行の文は規則 2 に、規則 4 の残りは impl §21 の義務に移した。表の増減は検証で数えた条項単位の概数で、言語規則の数とは単位が違う。

### 18.2. 決定した事項

1. **区分の綴り。** 宣言の同一性で `Kimi.Async.Task` を認識する。文法は変えず、SPEC §22.1 に「型を宣言しない行」を設ける。文脈語にする案（`place` と同じ扱い）は、SPEC §2.5.1 と Appendix F の変更が要るので採らない。
2. **後始末の中での中断。** 採らない（本書 7.6）。§16.2.3 の要件を自明に満たし、終了経路ごとに展開される後始末に再開状態を増やさない。代償は、早期 return の経路での非同期の後始末を、名前付きの操作と明示的な合成で書くこと。
3. **失敗したら兄弟を取り消す合流の名前。** `joinOk` とする。成功の条件（すべての子が `Ok`）を名前に出し、`try` 接頭辞は使わない（本書 5）。他の候補は次の理由で採らない。
   - `joinAll`：`join` と区別がつかない。
   - `joinFailFast`：冗長である。
   - `all`：JavaScript の `Promise.all` に合わせた名前で、何を待つかが名前から分からない。

### 18.3. 採用しなかった案

| 案 | 理由 |
| --- | --- |
| `async`/`await` の区分 | 次点。キーワードが二つ要り、取消しや子の開始に暗黙の「現在のタスク」が要る（Principle 2 の明示的な文脈に反する）。本書は権限そのものを区分で運ぶ |
| 能力を `uniq/Async.Task` の型にする（初版） | 禁止を列挙する規則が多く、関連型を通る抜け道を個別に塞ぐ必要があった。区分なら既存の規則で自動的に除かれる |
| Origin の規則で能力の脱出を禁じる | 問題は寿命ではなく、どの枠の中で使われるかである（本書 3.3） |
| Future 値と `Pin`（Rust） | 二つ目の移動可能性の概念で、impl §21.4.5 と衝突する |
| スタックフルなタスク（Go） | 中断が見えない（Principle 2）。生ポインターがスタックを指すので、スタックを移せない |
| `await` を併記する | 一つの事実に二つの綴りができる（Principle 1、SETTLED の項目 3 と同じ理由） |
| join ハンドル、`async let`、`detach` | 健全性が破棄の実行に依存する。子を開始する方法が増える |
| `ref/Async.Task` で観測し、`uniq` で中断する | 区分と両立しない。単一スレッドでは取消し要求は観測側の中断中にしか出ないので、得るものがない |
| 公開の `cancelSiblings` | 呼ばれた側の奥から兄弟を取り消せる暗黙の権限になる（Principle 2） |
| Deferred Block の中では取消しを観測しない（遮蔽） | 外側のタイムアウトと `joinOk` の失敗の伝播が効かなくなる |
| 実行器を値（`Async.Runtime`）にする | 「ハンドル一つにポート一つ」と衝突し、同期から非同期への橋渡しにパラメーターが増える。決定的なスケジューラーは別の提案とする |
| アリーナの大きさを静的に決める | フレームの大きさは CoroSplit の後でないと分からない |
| `Async.Blocking`、`Async.Wait`、`Async.block` | 効果は能力によらず同じなので、多相は飾りでしかない。入れ子の `run` で足りる |
| 入れ子の `run` で Abort する | Abort するかどうかが動的な文脈で決まる（Principle 2） |
| 規則 2 を効果要約の行にする | 権限は入力から来るので、環境効果の定義に合わない。中断しない呼出し元にまで広がる |
| チャネルを `ref/Self` で操作する | 共有経路から変更できるようになる（新しい可変性の概念）。`rc` を通って循環も作れる |

### 18.4. Kimigayo Principles との対応

| Principle | 対応 |
| --- | --- |
| 1. One Concept, One Canonical Form | 中断の印はタスク引数だけ。子の開始は Kimi の操作だけで、構造化された形だけ。取消しは値だけ。非同期の列は `loop` と `match` の一つの書き方。同期版と非同期版の対を作らない |
| 2. Local Reasoning | タスク呼出しは選ばれた宣言だけで分かる。推論も暗黙の現在タスクもない。子の寿命は呼出しの期間で決まる。暗黙に実行されるコードは中断しない |
| 3. Explicit Semantics | 中断の権限・取消し・子の消費はシグネチャーと値に現れる。隠れた割当てはアリーナの積み増しだけで、その上限を回帰で固定する |
| 4. Compiler Server Protocol | 中断点とタスク境界の一覧、既存の code と Reason 値による診断、事実で条件を検証した修復候補、構成に結び付いた測定値 |

### 18.5. 検証の記録

1. **初版の設計。** 五つの観点（Rust/C#、Swift、Go、Kotlin、エフェクト）から独立に案を作った。それらを採点して統合し、三つの観点（所有権の健全性、仕様と Principles の整合、規則の最小性）から反証を試みた。
2. **第 2 版の改善案。** 四つの観点（規則の削減、一貫性、性能、応用性）から案を出し、観点ごとに反証の検証をかけた。最後に、案どうしの衝突・重複・欠落を確かめた。
   - LLVM に関わる主張は、同梱の LLVM 22.1.8 で IR を書いて確かめた。確かめたのは `is_in_ramp`、`coro_elide_safe` による埋め込み、中断のない本体の畳込み、lifetime マーカーとフレーム、破棄の経路の空き。
   - Win32 の事実も確かめた。完了ポートの結び付け、成功時の通知の省略、IFS の条件の三つである。
3. **却下した案。** 本書 18.3 に挙げた。

### 18.6. 未決事項

1. **配置。** 新しい第 24 章にするか、第 22 章を拡張するか。
2. **タスクを取る操作の範囲。** どの Kimi の操作がタスクを取るか。ネットワーク・ファイル・タイマーは取る。既存の同期的な `Console` 出力の扱いを決める。タスクを取る操作には、タスクを取らない版を作らない。
3. **`init` での中断。** `init` にタスク・パラメーターを許すか。`init` の `try` の行き先は Unit なので（SPEC §17.2.4）、取消しを報告できない。禁止するか、STYLE で Type 関数を勧めるだけにするか。
4. **リークの保証。** 規範的なリーク禁止の保証を今のうちに述べるか。本書の設計はそれに依存しないが、将来のガードを守る。
5. **`each` の `limit`。** 既定値を持たせるか。
6. **第 3 段階の前提。** Callable 制約への effect bound（SPEC §8.4.10.1）を先に別の提案として進めるか。
7. **熱いループのフレームの読み書き。** 中断点を含まないループを普通の関数に切り出すことを、STYLE の勧めにするか、コンパイラーが自動で行うか（本書 7.7。impl §21 の未決事項として `src/Benchmark` で測る）。
