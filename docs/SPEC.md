# Kimigayo Language Specification

This file is the table of contents of the language specification. The chapters under `spec/` hold the rules; [§1.3](spec/01-overview.md#13-normative-status-and-wording) states what is normative, and [Appendix F](spec/appendices/F-syntax-summary.md) is the normative grammar. Chapters 20 and 21 and Appendix A are in the [implementation specification](IMPL.md). [STATUS.md](STATUS.md) records what the compiler implements.

## Contents

### Part I. Introduction and source text

- [1. Overview](spec/01-overview.md)
- [2. Source and lexical structure](spec/02-source-and-lexical-structure.md)

### Part II. Types, values and sequences

- [3. Types and values](spec/03-types-and-values.md)
- [4. Arrays, indexing, and slices](spec/04-arrays-indexing-and-slices.md)
- [5. Raw pointers and unsafe memory](spec/05-raw-pointers-and-unsafe-memory.md)

### Part III. Declarations and static semantics

- [6. Declarations and containers](spec/06-declarations-and-containers.md)
- [7. Functions and callable values](spec/07-functions-and-callable-values.md)
- [8. Generics, constraints, and contracts](spec/08-generics-constraints-and-contracts.md)
- [9. Names, signatures, and access](spec/09-names-signatures-and-access.md)
- [10. Overload resolution and inference](spec/10-overload-resolution-and-inference.md)
- [11. Properties](spec/11-properties.md)

### Part IV. Expressions and control flow

- [12. Expressions](spec/12-expressions.md)
- [13. Operators and assignment](spec/13-operators-and-assignment.md)
- [14. Control flow](spec/14-control-flow.md)

### Part V. Ownership, cleanup and failure

- [15. Ownership and lifetime analysis](spec/15-ownership-and-lifetime-analysis.md)
- [16. Scope exit and destruction](spec/16-scope-exit-and-destruction.md)
- [17. Failure handling](spec/17-failure-handling.md)

### Part VI. Programs and the Kimi library

- [18. Modules and dependencies](spec/18-modules-and-dependencies.md)
- [19. Compile-time directives](spec/19-compile-time-directives.md)
- [22. Kimi, program execution, and foreign functions](spec/22-core-execution-and-foreign-functions.md)

### Part VII. Compiler services

- [23. Compiler services](spec/23-compiler-services.md)
  - [Check output schema](spec/schemas/check-output.schema.json)

### Part VIII. Asynchronous execution

- [24. Suspension and asynchronous tasks](spec/24-suspension-and-asynchronous-tasks.md)

### Appendices

- Appendix A. Compiler requirements: [implementation specification](IMPL.md)
- [Appendix D. Deferred features](spec/appendices/D-deferred-features.md)
- [Appendix E. Terminology](spec/appendices/E-terminology.md)
- [Appendix F. Grammar](spec/appendices/F-syntax-summary.md)
