# Ranges

Build and run `Ranges.kimi` with the Windows LLVM backend:

```powershell
kimi build docs/examples/Ranges/Ranges.kimi
kimi run docs/examples/Ranges/Ranges.kimi
```

Expected output:

```text
Sum of 1..=4 is 10.
Last byte is 255.
Middle has 3 values.
Tail starts at 40.
Resolved range covers 3 positions.
Window 3..9 is out of range.
```

Two integer boundaries construct an iterable `Range<T>`; an omitted or Index boundary constructs an `IndexRange`, which selects from a target but is not iterable. `resolve` turns either into a `ResolvedRange` of validated positions, and `Slice.trySlice` returns `None` for integer boundaries from external input that are negative, reversed or out of range. See [SPEC §4.6.3](../../spec/04-arrays-indexing-and-slices.md#463-ranges) and [§4.6.4](../../spec/04-arrays-indexing-and-slices.md#464-resolution-evaluation-and-failure).
