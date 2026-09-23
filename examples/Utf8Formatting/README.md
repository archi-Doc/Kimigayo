# UTF-8 formatting

Build and run `Utf8Formatting.kimi` with the Windows LLVM backend:

```powershell
kimi build examples/Utf8Formatting/Utf8Formatting.kimi
kimi run examples/Utf8Formatting/Utf8Formatting.kimi
```

Expected output:

```text
point=(3, 7)
君: (3, 7)
ready=true
123
```

`Utf8Format.format` borrows the value and writes through the same adapter for owning interpolation, fixed buffers and heap buffers. `$tryWrite` stops before evaluating later substitutions after a failure. Ordinary interpolation creates an independent owning string and Aborts on formatting failure.

`Text.fixed` borrows an initialized byte array. It starts empty; `clear()` retains its storage for reuse. `buffer.text()` returns a validated shared view, so its uses must end before clearing the buffer. `Text.tryFormat` handles a single value and returns a view retaining the array's exclusive Loan. The exact three-byte destination fits `123` without allocation.

See the [formatting profile](../../spec/utf8-formatting.md) for representations, borrowing, failure and cost requirements. `Utf8IntegrationTest` executes this example at O0/O2; [Milestone 32](../../milestones/Milestone32.kimi) also exercises generic formatting and source/result independence.
