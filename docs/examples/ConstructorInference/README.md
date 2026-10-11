# Constructor inference

`Box.init(n)` infers the structure's own Type argument from `n`. A borrow retains its source dependency;
a callback retains its concrete Closure Type. An independent result annotation fills the otherwise
unbound slot of `Empty.init()`. Constructors do not infer Types from their bodies or Field initializers.

Run `kimi run docs/examples/ConstructorInference/ConstructorInference.kimiproj`:

```text
inference ok
create 1
create 2
drop 2
drop 1
```

Writing `Pair<Token>.init(...)` produces the same acquisition, evaluation and cleanup behavior. Explicit
arguments may still be required when evidence is missing or the inferred selection would change after
fixing the construction Type. See [SPEC §10.8.1](../../spec/10-overload-resolution-and-inference.md#1081-constructor-type-inference).
