# 1. Overview

[Specification index](../SPEC.md)

## 1.1. Purpose

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun, and safe.

This specification defines the intended language. Language rules, compiler requirements and the recorded implementation status are distinct; see [implementation status](../STATUS.md).

Kimigayo prioritizes consistency and language quality over compatibility between versions. A reproducible build must pin the compiler build, sources, dependencies and configuration; a language-version label alone does not identify a pre-alpha compiler.

**Basic example.**

```kimi
alias Kimi.Base

#if windows
alias Kimi.Windows

public group Program
    public func runExample(arg?: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        return

    func getString<s/T>(value?: s/T) -> string
        s is ref or obj
        T is Comparable

        #switch
            #case windows
                return "windows"
            #case linux
                return "linux"
            #case _
                return "other"
```
> **Design Note (Do not modify this text!)**
>
> * `$` is not a macro. It is the Composition Root.

## 1.2. Conventions and notation

User-defined Types, Contracts and Declaration Containers conventionally use PascalCase; built-in Type keywords keep their specified spellings. Functions, Fields, Properties, local bindings, parameters and other value names generally use camelCase.

| Notation | Meaning and uses |
| --- | --- |
| `[]` | Array and Dictionary construction, fixed-array Types, indexing, Range-based slicing, and anonymous-function Capture Lists. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions, and operator precedence. |
| `<>` | Type arguments and [function length arguments](04-arrays-indexing-and-slices.md#44-function-length-parameters). |
| `{...}` | Closed struct/enum Origin schema (possibly empty), one borrow expression before `/`, or a fresh binding-set name after a Type. Not a body or collection. |
| `=` | Initialization, parameter defaults, or assignment, depending on context. Acquisition follows [Copy and Move](03-types-and-values.md#35-copy-and-move). |
| `@` | Explicit Type/Semantics adaptation; see [explicit operations](13-operators-and-assignment.md#135-explicit-operations). |
| `->` | Introduces the result Type of a function declaration or Function Type. |
| `=>` | Introduces a single-item executable Body, a parameter-name mapping, or a Container-alias target, according to context. |
| `:` | Separates names from Types, argument names from values, Dictionary keys from values, labels from constructs, and named transfer targets from values. It also introduces structure bases, Contract parents and constructor `: base(...)`, but never an executable Body. In Origin relations, `a : b` means that `a` outlives `b`, including equal lifetimes. |
| `#` | A compile-time construct. Lowercase reserved directives such as `#if` differ from PascalCase Attributes such as `#Inline`. |
| `$` | Selects a language-provided Composition Root operation; see [§13.8](13-operators-and-assignment.md#138-extension-boundaries-and-reserved-syntax). |
| `;` | Forbidden outside comments and literals; never a statement or Type separator. |

A Type combines Semantics, a Core or object View Target, and Origins; see [Types and values](03-types-and-values.md#3-types-and-values).

Examples are independent unless explicitly connected. Application-specific Types and APIs in examples are assumed, not required library interfaces. API examples may show signatures without implementations; this does not permit bodyless source definitions. `text` code blocks may show conceptual notation, and lines marked `Error` intentionally violate a rule.

## 1.3. Reading the rules

| Wording | Meaning |
| --- | --- |
| must / must not | Mandatory requirement or prohibition. The feature defines whether a violation is a compile-time error, an Abort, or an Unsafe contract violation. |
| may | Permission within all stated constraints. |
| should | Recommendation; not a condition of language conformance. |
| is planned | Implementation work is intended; this is not a language rule. |
| is deferred | In a design-status note: the design is postponed. It grants no language permission. |
| implementation-defined | The implementation chooses within the stated limits and must document its choice. |
| unspecified | Any result within the stated limits is permitted, and the choice need not be documented. This does not imply undefined behavior. |

Unqualified declarative rules and imperative requirements are normative even without `must`. Examples illustrate ordinary use, boundaries or intentional errors; they add no rules and never override the rules they illustrate.

Compiler requirements (Appendix A) preserve required information and invariants. A **non-normative reference model** (Appendix B) is an optional algorithm, not an alternative semantics.

Design-status labels:

| Label | Meaning |
| --- | --- |
| Specified, not implemented | The rules are settled; no implementation is available yet. |
| Partially specified | Design questions remain; the settled parts are normative. |
| Deferred design | The feature is withheld. |

Neither a design status nor an implementation plan grants language permission.
