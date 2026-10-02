# Settled Design Decisions

> **Note.** This file records changes the design has deliberately decided not to make. Each entry gives the item, the
> problem with a short example, the proposed fix and the reason the fix is not applied. Check this list before reviewing
> the specification or proposing a change, and do not re-raise an entry unless its reason no longer holds. This file is
> non-normative: [SPEC.md](SPEC.md) and its chapters define required behavior. When a specification change invalidates
> a reason or an example, update or remove the entry in the same commit.

## `is` has several meanings

- **Problem:** `is` is a runtime test, a Type identity or conformance requirement, a conformance declaration, a capability opt-in or opt-out, a Semantics test or an associated-Type binding, chosen by position and name resolution (§7.4, §8.3, §13.6.1).
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
- **Why not applied:** A result-only Origin chosen by the caller is part of the Origin design; `static` cannot replace it when the result contains `T` or is exclusive (§3.3.6, §15.4.3). Names resolve lexically without hiding, adding a slot is already an API change (§15.3.7), and constructors have no `self`. Misspellings are handled by diagnostics that explain the introduced Origin and suggest similar names.

## A string concatenation operator

- **Problem:** `string + string` and string `+=` read naturally, and the specification once parsed them while deferring their acquisition, Loan, result-ownership and failure rules. An interpolated literal already produces an owning `string` from borrowed parts (§12.3.3), so the operator was a second form of one operation, with undefined ownership.
- **Example:**

  ```kimi
  let first = "Hello, "
  let second = "world"
  let s1 = first + second       // Error: + requires numeric operands
  let s2 = "\(first)\(second)"  // Owning string; first and second are borrowed
  ```

- **Proposed fix:** Keep `+` and `+=` for strings and define them as a strict desugaring to interpolation: `a + b` as `"\(a)\(b)"` and `t += v` as `t = "\(t)\(v)"`.
- **Why not applied:** The desugaring keeps two forms of one operation (Principle 1). A chain `a + b + c` either allocates per step or needs a special fold that one interpolated literal performs by construction, and `+=` would hide the replacement of the whole string behind an update spelling (§13.7.2). Text built in steps belongs to `Text.HeapBuffer` and `Utf8Writer`, whose allocation and failure behavior are stated. The operators are removed instead (§13.3), and the diagnostic names the interpolated literal that joins the same operands in the same order.

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
- **Why not applied:** A use spelling conflicts with the implicit acquisition of Receiver Expressions (§7.3) and with the meaning of `tasks@uniq.length`, a declaration mark leaves `x.p` looking like a read at every use, and a warning keeps the failing `let` receiver and the lost Copy update. The receiver of an accessor is fixed per operation instead, `ref/Self` for `get` and `uniq/Self` for `set` (§11.2), and the exclusive or consuming operation is a function named by §4.7.1 (`nextReading`, `intoItem`). `ref/Self` and `uniq/Self` are reachable through object handles by the path rules, so no capability is lost except a getter that returns `self` as a handle.
