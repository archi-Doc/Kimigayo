# Settled Design Decisions

> **Note.** This file records changes the design has deliberately decided not to make. Each entry gives the item, the
> problem with a short example, the proposed fix and the reason the fix is not applied. Check this list before reviewing
> the specification or proposing a change, and do not re-raise an entry unless its reason no longer holds. This file is
> non-normative: [SPEC.md](SPEC.md) and its chapters define required behavior. When a specification change invalidates
> a reason or an example, update or remove the entry in the same commit.

## `is` has several meanings

- **Problem:** `is` is a runtime test, a Type identity or conformance requirement, a conformance declaration, a capability opt-in or opt-out, a Semantics test or an associated-Type binding, chosen by position and name resolution (§7.5, §8.2, §13.6.4).
- **Example:**

  ```kimi
  func isDog(pet: objref/Animal) -> bool
      return pet is Dog      // Runtime test: also true for Types derived from Dog

  func feed<T>(pet: ref/T)
      T is Animal            // Constraint: T must be exactly Animal
      return

  feed(dog@ref)              // Error: T = Dog is Refuted, so the misuse is rejected
  ```

- **Proposed fix:** Separate spellings, such as `T == U` for identity, `associate X = T` and an explicit Constraint-region introducer.
- **Why not applied:** Every misuse found is rejected at compile time, none silently, and one `X is Y` form keeps Constraints readable.

## Signature Origin names are introduced implicitly

- **Problem:** An unbound Origin name in a function signature introduces a universal Origin (§15.3.4). A misspelled name becomes a new Origin that surfaces only as a lifetime error, and a member's name binds to a Type slot added later to the header.
- **Example:**

  ```kimi
  // A misspelling introduces a new Origin instead of reporting an unknown name.
  func head<T>(values: ref/Array<T> during source) -> ref/T during sorce
      return values[0]       // Error: the result must be valid for any caller-chosen sorce

  // Intended use: the caller chooses s. `during static` would require T to outlive static.
  func empty<T>() -> Slice<T> during s
  ```

- **Proposed fix:** Reject Origin names that appear only in the result, and require member signatures to reference Type slots in a qualified form such as `self.source`.
- **Why not applied:** A result-only Origin chosen by the caller is part of the Origin design; `static` cannot replace it when the result contains `T` or is exclusive (§3.3.2, §15.4.3). Names resolve lexically without hiding, adding a slot is already an API change (§15.3.7), and constructors have no `self`. A misspelling is still reported where it matters: the relation it fails names the introduced Origin as an end (§23.1).

## A string concatenation operator

- **Problem:** `string + string` and string `+=` read naturally, and the specification once parsed them while deferring their acquisition, Loan, result-ownership and failure rules. An interpolated literal already produces an owning `string` from borrowed parts (§12.4.2), so the operator was a second form of one operation, with undefined ownership.
- **Example:**

  ```kimi
  let first = "Hello, "
  let second = "world"
  let s1 = first + second       // Error: string arithmetic is prohibited
  let s2 = "\(first)\(second)"  // Owning string; first and second are borrowed
  ```

- **Proposed fix:** Keep `+` and `+=` for strings and define them as a strict desugaring to interpolation: `a + b` as `"\(a)\(b)"` and `t += v` as `t = "\(t)\(v)"`.
- **Why not applied:** The desugaring keeps two forms of one operation (Principle 1). A chain `a + b + c` either allocates per step or needs a special fold that one interpolated literal performs by construction, and `+=` would hide the replacement of the whole string behind an update spelling (§13.7.2). Text built in steps belongs to `Text.HeapBuffer` and `Utf8Writer`, whose allocation and failure behavior are stated. The operators are removed instead (§13.3.1), and the interpolated literal is the one form that joins strings.

## Exclusive or owning accessor receivers

- **Problem:** A computed getter with a `uniq/Self`, owning `Self` or object-form receiver makes a read-looking access `x.p` lend `x` exclusively, consume it or Copy it, and a setter with an owning `Self` receiver runs `point.x = 10` on a Copy and loses the update. A method shows its receiver through `()` and its Name; a Property shows nothing, so the effect of `x.p` cannot be read from the use (Principles 2 and 3).
- **Example:**

  ```kimi
  struct Meter
      var hits: i32 = 0
      public computed reading: i32
          get(self: uniq/Self) -> i32    // Error: a getter receiver is ref/Self (§11.2)
              self.hits += 1
              return self.hits
      public func nextReading(self: uniq/Self) -> i32   // The call form shows the exclusive acquisition
          self.hits += 1
          return self.hits

  let fixed = Meter.init()
  let a = fixed.nextReading()          // Error: a let binding is not lent exclusively; nothing is read instead
  ```

- **Proposed fix:** Keep the ownership-bearing accessor receivers and require a spelling at the use (`meter@uniq.reading`), mark the declaration (`mutating get`), or warn.
- **Why not applied:** A use spelling conflicts with the implicit acquisition of Receiver Expressions (§7.3) and with the meaning of `tasks@uniq.length`, a declaration mark leaves `x.p` looking like a read at every use, and a warning keeps the failing `let` receiver and the lost Copy update. The receiver of an accessor is fixed per operation instead, `ref/Self` for `get` and `uniq/Self` for `set` (§11.3), and the exclusive or consuming operation is a function named by §4.9.1 (`nextReading`, `intoItem`). `ref/Self` and `uniq/Self` are reachable through object handles by the path rules, so no capability is lost except a getter that returns `self` as a handle.

## Unqualified names find inherited declarations

- **Problem:** Inside a derived struct, an unqualified Name never reaches a declaration inherited from a base, although the inherited Names belong to the derived struct (§6.2.2, §9.7.1). Every inherited declaration needs a qualifier or an explicit receiver: `Self.marker()` for `marker()`, `self.count` for `count` (§9.5).
- **Example:**

  ```kimi
  group Outer
      func marker() -> i32 => 1

      open struct Base
          public var count: i32 = 0

          public func marker() -> i32 => 25

      struct Derived : Base
          func first(self) -> i32 => marker() // Error: lookup stops at the inherited Base.marker; write Self.marker()
          func second(self) -> i32 => count   // Error: an inherited instance member; write self.count
          func total(self) -> i32 => self.count + Self.marker()
  ```

- **Proposed fix:** Search the base layers at lookup stage 2, one layer at a time, so that an unqualified Name inside a derived struct selects the inherited declaration: `marker()` would call `Base.marker`, and a bare nested Type Name would name the inherited Type.
- **Why not applied:** Members of an external base would be found unqualified, contradicting §18.2, where external members are never found unqualified. They would also be found at stage 2, before the project root and the aliases of stages 5 and 6, through which unqualified Names otherwise reach external declarations last, so the precedence would be reversed. An upstream base that adds `marker` or a nested `Node` would silently retarget a downstream unqualified use: dependency revalidation (§A.2) succeeds against the new target and reports nothing. Resolving a base clause would still need an exception, because searching the struct's own base layers there forms a cycle. §9.5 instead examines the base layers only to stop lookup with `QualificationRequired_Kd`, whose repair candidates insert the canonical qualifiers `self.`, `Self.` or `C.`, so an unqualified Name that succeeds always denotes a lexical declaration.

## A no-leak guarantee

- **Problem:** A scoped guard whose destructor joins borrowing children is unsound when the guard is leaked, as with Rust's `mem::forget`. A language-wide guarantee that every non-Owned value is destroyed before its Origins end would make such guards sound.
- **Example:**

  ```kimi
  func fillBoth(task; left: uniq/Buffer, right: uniq/Buffer) -> ()
      let fillLeft = func [left] (task;) => fill(task; left)
      let fillRight = func [right] (task;) => fill(task; right)
      _ = Async.join(task; fillLeft@move, fillRight@move)   // Returns only after both children complete
  ```

- **Proposed fix:** Guarantee that every non-Owned value is destroyed before its Origins end, and add a scope or join-handle value whose destruction waits for its children.
- **Why not applied:** The guarantee would bind every future cycle-forming API (`rc` cycles, §16.4) and all unsafe code, and tasks do not need it. Children start only inside a Kimi operation that returns after they complete (§22.3.2), and no value represents a running child or a suspended task frame, so safe-code soundness never depends on a destructor running. Future guards and scopes keep that constraint and use the same call-duration shape.

## Deferred Blocks that suspend

- **Problem:** Finalization such as a flush belongs on every exit path, and `defer` is the construct for that. A Deferred Block cannot pass its function's task, so `defer => _ = file.flush(task;)` is rejected.
- **Example:**

  ```kimi
  func appendLine(task; path: ref/string, line: ref/string) -> Result<(), Io.Error>
      var file = try Io.File.open(task; path)
      // defer => _ = file.flush(task;)    // Error: a Deferred Block cannot pass the task
      try file.write(task; line)
      return file.flush(task;)             // Finalize on the path that needs it
  ```

- **Proposed fix:** Let Deferred Blocks pass the task, or add an asynchronous `drop`.
- **Why not applied:** Every exit path would gain resume states in its cleanup, cleanup under cancellation would need an abrupt completion that §16.2.3 does not have, and a Deferred Block that ignored cancellation would hide an unbounded mask that stops outer timeouts. Finalization stays on explicit paths, and one that must run under cancellation is called through `Async.shield` with an explicit grace (§22.3.3).

## Running full CI before integration into main

- **Problem:** The full build and test workflow is expensive. Additional triggers repeat that work while changes are still being developed.
- **Example:** A push to `dev`, a pull request update, and its merge into `main` can each run the same full test suite.
- **Proposed fix:** Run `.github/workflows/test.yml` on `dev` pushes, pull requests, or manual dispatch as well as `main` pushes.
- **Why not applied:** The extra runs add substantial test load. Keep this workflow restricted to pushes to `main`, including merge updates. Do not add `dev`, pull request, or manual triggers. Local verification remains part of the implementation workflow.

