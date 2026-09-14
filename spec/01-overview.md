# 1. Overview

[Specification index](../SPEC.md)

## 1.1. Purpose

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun, and safe.

This document defines the intended language. Language rules, Compiler requirements, and the recorded implementation snapshot are distinct; see [implementation status](../STATUS.md).

**Basic example.**

```kimi
alias Kimi.Base

#if windows
alias Kimi.Windows

public group Program
    public func runExample(arg: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        return

    func getString<s/T>(value: s/T) -> string
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

Kimigayo prioritizes consistency and language quality over compatibility between versions. Reproducible builds must pin the compiler build, source, dependencies, and configuration; a language-version label alone does not identify a pre-alpha compiler.

User-defined Types, Contracts, and Declaration Containers conventionally use PascalCase; built-in Type keywords retain their specified spellings. Functions, Fields, Properties, local bindings, parameters, and other value names generally use camelCase.

| Notation | Meaning and uses |
| --- | --- |
| `[]` | Array and Dictionary construction, fixed-array Types, indexing, Range-based slicing, and anonymous-function Capture Lists. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions, and operator precedence. |
| `<>` | Type arguments and [function length arguments](04-arrays-indexing-and-slices.md#44-function-length-parameters). |
| `{}` | Unused; reserved for future language evolution. |
| `=` | Initialization, parameter defaults, or assignment according to context; acquisition follows [Copy and Move](03-types-and-values.md#35-copy-and-move). |
| `@` | Explicit Type/Semantics adaptation; see [explicit operations](13-operators-and-assignment.md#135-explicit-operations). |
| `->` | Result Type associated with the input side of a function declaration or Function Type. |
| `=>` | Introduces a single-item executable Body; also maps parameter names and named Origin arguments in their own grammars. |
| `:` | Separates names from Types, argument names from values, dictionary keys from values, labels from constructs, and named transfer targets from values. It also introduces structure bases, Contract parents, and constructor `: base(...)`, but never an executable Body. In Origin relations, `a : b` means a outlives b, including equal lifetimes. |
| `#` | A compile-time construct. Lowercase reserved directives such as `#if` differ from PascalCase Attributes such as `#Inline`. |
| `$` | Selects a language-provided Composition Root operation; see §13.8. |
| `;` | Forbidden outside comments and literals; never a statement or Type separator. |

Types combine Semantics, a Core or object View Target, and Origins; see [Types and values](03-types-and-values.md#3-types-and-values). Examples are independent unless explicitly connected. Application-specific Types and APIs are assumed, not required library interfaces. API examples may show signatures without implementations; this does not permit bodyless source definitions. `text` fences may show conceptual notation; lines marked Error intentionally violate a rule.

## 1.3. Reading the rules

Examples illustrate ordinary use, boundaries, or intentional errors; they do not add rules.

| Wording | Meaning |
| --- | --- |
| must / must not | Mandatory requirement or prohibition. The feature defines whether a violation is a compile-time error, Abort, or an Unsafe contract violation. |
| may | Permission within all stated constraints. |
| should | Recommendation, not a condition for language conformance. |
| is planned | Implementation work is intended; this is not a language rule. |
| is deferred | In a design-status note, design is postponed; it grants no language permission. |
| implementation-defined | The implementation chooses within the stated limits and must document the choice. |
| unspecified | Any result within the stated limits is permitted; the choice need not be documented. This does not imply undefined behavior. |

Unqualified declarative rules and imperative requirements are normative even without `must`. Examples illustrate those rules and do not override them. Compiler requirements preserve required information and invariants; a **Non-normative reference model** is an optional algorithm, not an alternative semantics.

**Specified, not implemented** means settled rules with no available implementation. **Partially specified** identifies remaining design questions; **Deferred design** withholds the feature. Neither design status nor implementation plans grant language permission.
