# Expected Types and Argument Temporaries (O-331)

Status: 一部取り込み (open): the owner's direction for argument typing and argument temporaries; no section is integrated into SPEC or
IMPL yet, no text is frozen; the design, the label census and the implementation are the Front's; adoption establishes no support.
Request: 2026-11-15. On O-331 (PLAN G910's Dictionary-literal join, against DECISIONS BU-43 C1) the owner asked, verbatim
「ケース毎に扱いが異なり、煩雑に感じる。一貫性があり、規則を少なくするには、どのような仕様にしたほうが良いと考えますか？今までの仕様や常識に
囚われず、自由に発想して下さい。」; K answered with the two principles below; the owner decided, verbatim 「提案の方針（原則A：期待型を引数の式に
下ろす、原則B：ref/U に渡す値の式は引数の位置で一時値にして1回借用する）でOKです。提案書を作成し、設計、実装を進めて下さい。SPECもそれに合わせ
て、修正して下さい。」
Owner of the design and implementation: the Front (Binding; WORKFLOW Sessions). K writes the SPEC text from the Front's design, with an
independent review, to land with the implementation.

## 0. 要約（日本語）

- いまの仕様は「引数の位置に値を渡す」という1つの操作に、式の形（Tuple・配列・fill・Dictionary のリテラル、join、待ちの呼び出し）ごとに
  別の規則を持つ。型をどこから決めるか（BU-43 の Dictionary 全体型付け、Tuple と配列の部分ごとの型付け、10.5.1 の列挙、14.9.2 の共通型）
  と、一時値をどこに置くか（BU-187 の例外、分岐の一時値は分岐で終わる規則、BU-156 と BU-203 の暫定）が、形ごとに継ぎ足されてきた。
- 原則A（型は上から決める）: 引数の式はパラメータの Type（`ref/U` なら U）を期待型として検査する。期待型は join の各分岐、リテラルの各要素
  （形を区別しない）、ジェネリックな呼び出しの結果へ、1つの規則で下りる。期待型がないときだけ、式の中身から型を作る（14.9.2 の共通型）。
- 原則B（値は値として流れ、借用は使う位置で1回）: join は選ばれた分岐の値を受け取る式で、分岐の一時値を借用しない。`ref/U` に値の式を渡すと、
  引数の位置で一時値を1つ作り、呼び出しの間だけ借用する。式の形は問わない。分岐の中で一時値を借用して外へ出すものだけが invalid のまま。

## 1. The principles

**Principle A: the expected Type comes from above.** An argument expression is checked against its parameter's Type as its expected
Type (a parameter `ref/U` gives `U` for the value). The expected Type flows, by one rule, into each source of a selection, `do` or
`loop`, into every part of a literal (a Tuple, an array, a fill and a Dictionary alike), and into a generic call's result. A Type is
synthesized from the expression itself only where no expected Type exists; 14.9.2's common Type is that synthesis.

**Principle B: values flow as values; a borrow happens once, where the value is used.** A join (a selection, `do` or `loop`) is an
expression whose value is the selected source's value; it borrows no source's temporary. A value expression (a literal, a join, a call)
passed to a parameter `ref/U` is materialized as one temporary at the argument's position and borrowed for the call, whatever the
expression's form. What stays invalid is borrowing a temporary inside a source and passing the borrow out of it.

Today's SPEC already holds both in part: 14.9.2 steps 1 and 3 propagate a fixed outer expectation, and step 3 makes a selection at
`ref/U` with a literal source "one owner `U` temporary at the expression's position, shared-borrowed once"; 3.6 lets that temporary
belong to the expression's position. The proposal removes the conditions ("with a literal source", "fixed", the per-form lists) so that
one rule covers every form.

## 2. The programs on which today's rules and the principles differ

```kimi
func idf<T>(value: T) -> T => value@move
func kd(x: ref/Dictionary<string, i32>) -> i32 => 0
func fdv(x: Dictionary<string, i64>) -> i32 => 0
func kf32(x: ref/[2 of i32]) -> i32 => 0
func kf(x: ref/[2 of i64]) -> i32 => 0
```

| Call | Today (dev, or the in-flight decisions) | Under the principles |
| --- | --- | --- |
| `kd(if c => ["a": idf(1)] else => ["b": idf(2)])` (O-331's P2) | accepted (BU-43 C1); rejected under O-331 (b) | accepted: `Dictionary<string, i32>` flows into each source; the join's value is one temporary, borrowed once |
| `fdv(if c => ["a": idf(1)] else => ["b": idf(2)])` (P3) | rejected (`idf(1)` defaults to `i32`) | accepted (`idf<i64>`) |
| `kf32(if c => [idf(1), 2] else => [2 of idf(2)])` (O-327's (1)) | depends on source order | accepted in both orders: each source is checked as `[2 of i32]` |
| `kf(if c => [2 of idf(1)] else => [2 of idf(2)])` (G910's x-joinarm-call) | accepted on dev, rejected by G910 (O-322 (a)) | accepted |
| a source that borrows its own temporary and passes the borrow out | rejected | rejected (unchanged) |

## 3. What the principles remove

- DECISIONS BU-43 C1's whole-Dictionary typing, as a separate rule beside the part-by-part typing of Tuples and arrays.
- BU-187's one exception and the part-kind classification of a join at `ref/U` (`PartKind Join`); BU-156's and BU-203's waited-source
  interims where they only exist for a form's typing.
- SPEC 10.5.1's literal-form list as a separate argument kind beside "Waiting" (the kinds become: typed independently, or checked
  against the expected Type), and 14.9.2 step 3's literal-source condition.
- PLAN G910's form function (`FittedTarget`/`FormedShape`) and the questions O-322, O-327 and O-331 answered for one form each; PLAN
  G916's ranking question becomes one rule for every open argument.

## 4. Costs and what the design must check

1. **Temporaries (SPEC 3.6, 14.2.1).** A join's value becomes a new temporary at the join's position, each source's value moved into it;
   some joins labelled invalid today (j-callscalar-call, j-callarm32-call: "each arm's temporary ends with its arm") become valid. The
   design states which source temporaries end where, and that no borrow of a source temporary escapes.
2. **Overload resolution (SPEC 10.2, 10.4, 10.8).** The expected Type differs per candidate. Parts whose Type does not depend on the
   expected Type are typed once and reused; only the parts that do are checked per candidate. The design bounds the work (local
   reasoning) and measures it (RP8; the per-candidate rechecks on generated workloads).
3. **Ranking.** 10.2.2's classes for a value checked against an expected Type (today's Literal, Exact, CrossSemanticsBorrow rows) become
   one column every argument form reads (PLAN G915, G916).
4. **Defaults.** An untyped literal's default (`i32`) applies only where no expected Type reaches it.
5. **The label census.** Every program whose label changes (valid to invalid or back) is listed with its SPEC reading before the first
   prototype: the coverage rows of G910, G915, G916, G267fd, G812's literal rows, bundle 3a (landed: BU-182/BU-187), O-322, O-327 and
   O-331's programs, and the tests that pin today's records. Decisions the census contradicts are named for supersession.
6. **Generated code.** Materializing a join's temporary may add one move; O2 is expected to elide it; RP8 measures it.

## 5. Order (the Front plans the units)

1. The census and the coverage table on dev, labels from SPEC as the principles state them.
2. The design (Owner, Exposure, Readings), naming the canonical owner of "an argument's expected Type" and of "an argument temporary".
3. A prototype, then the implementation units, each with its review; SPEC text (K) with an independent review, landing with the units
   that implement it; STATUS and DECISIONS with them.
4. In-flight units the principles supersede or must wait (G910, G915, G916, G267fd, G812's literal rows) are held or re-scoped by the
   Front's plan; O-331 is answered by this proposal.

## 6. The owner's readings (O-333, 2026-11-15)

The design's four readings before its prototype (`front\notes\ArgFlow\design.md`, `census.md`: 435 programs on dev b1aaf418d, 84
move under A/B); the owner chose, verbatim 「O-333 R2 (a)・R3 (a)・R6 (b)・R5 (a)」. With the prelude of §2 and
`func fo2(x: i32) -> i32 => 0`, `func fo2(x: i64) -> i32 => 1`, `func fo3(x: ref/[2 of i64]) -> i32 => 0`,
`func fo3(x: bool) -> i32 => 1`, `func put<T>(a: T, b: T) -> () => ()`:

| Reading | Rule | Program and verdict |
| --- | --- | --- |
| R2 (a) | A and B apply at every fixed expected Type: a declared Type is an expected Type as a parameter's is. | `let x: ref/[2 of i64] = [2 of idf(1)]`: accepted (`idf<i64>`; one temporary at the `let`, ending with it). |
| R3 (a) | At overloads, a part whose Type depends on the expected Type is checked at each remaining candidate; independent parts are typed once. | `fo3([2 of idf(1)])`: accepted (only `ref/[2 of i64]` fits); `fo2(idf(1))`: `AmbiguousBinding_Kd` (both fit, no operation). |
| R6 (b) | A join is one value (Principle B): a Non-Copy Place source is not taken by value; a join of Places only stays a Place join. | `kd(if c => dm else => ["b": 2])` with `dm: Dictionary<string, i32>`: `TransferRequired_Kd` (write `dm@move`); `kd(if c => dm else => dm2)`: accepted. |
| R5 (a) | A join's sources classify as the same expression would outside a join (one rule). | `put(f, if c => [y, y] else => [y, y])` with `f: [2 of i32]`, `y: i32`: accepted, `T = [2 of i32]`. |
