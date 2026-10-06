# Fixed-array annotations and skipped initial stores

The two written array dimensions infer their lengths from the initializer. An element hole does not add another fixed
dimension: `let rows: [_ of _] = [[1], [2, 3]]` instead stores two dynamic Arrays.

`noinit` completes construction of a local Scalar array without storing its elements. It requires an explicit annotation
and an Unsafe Block. The example passes a borrow to a function that writes every element before the first read.
Borrowing and metadata access do not read elements; whole-array Copy and Move do. No per-element state is tracked.

Run `kimi run docs/examples/FixedArrays/FixedArrays.kimiproj`. The output is `2 7` followed by a newline.
Required behavior is defined in [SPEC §4.3](../../spec/04-arrays-indexing-and-slices.md#43-initialization-and-inference).
