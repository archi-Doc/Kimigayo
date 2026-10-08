# Kimigayo Implementation Specification

This is the index of the Kimigayo implementation specification. It defines how the initial implementation builds, lays out, runs and tests programs, and what its compiler must verify. The [language specification](SPEC.md) defines the language itself. This document continues its numbering, so section references such as §20.8 or Appendix A stay stable, and its rules never narrow or override a language rule.

## Normative status

| Part | Status |
| --- | --- |
| Chapters 20–21, [object creation and virtual dispatch generation](impl/virtual-dispatch.md), [associated-Type inference](impl/associated-type-inference.md), and the test execution profile | Normative contracts of the initial implementation profile: compilation configuration, commands and native builds; layout, runtime metadata and code generation; test execution. |
| Appendix A | Normative compiler requirements. |
| Appendix B | Non-normative reference models (optional algorithms). |

Implementation coverage and verified support boundaries are recorded in [STATUS.md](STATUS.md).

## Contents

- [20. Compilation configuration](impl/20-compilation-configuration.md): build units and inputs, Mods, LLVM output, native builds and the compiler commands.
- [21. Layout, runtime metadata, and code generation](impl/21-layout-runtime-and-code-generation.md): structure layout and ABI, runtime representations, generic code generation, checked lowering, the LLVM Windows x64 profile and task lowering.
- [Test execution profile](impl/testing-profile.md): solution execution, settings, temporary storage, limits, identities and results.
- [Associated-Type inference](impl/associated-type-inference.md): staged headers, retained identification, graph reuse and verification requirements.
- [Appendix A. Compiler implementation requirements](impl/appendices/A-compiler-requirements.md)
- [Appendix B. Non-normative reference models](impl/appendices/B-reference-models.md)
