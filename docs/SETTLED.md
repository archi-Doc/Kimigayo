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
