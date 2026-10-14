# 1. Overview

[Specification index](../SPEC.md)

## 1.1. Purpose

**Kimigayo** is a programming language built from scratch to be consistent, fast, simple, fun and safe. Its revisions put consistency and language quality ahead of compatibility with earlier revisions.

This specification defines the language. The [implementation specification](../IMPL.md) defines the build, runtime and code-generation contracts of the initial implementation, and [STATUS.md](../STATUS.md) records what is implemented.

```kimi
alias Kimi.Console

#if windows
alias Kimi.Windows

public group Program
    public func runExample(arg: string) -> ()
        var array = [0, 1, 2,]
        var map = [0:"Zero", 1:"One", ]
        writeLine(arg)
        return

    func getString<s/T>(value: s/T) -> string
        s is ref or uniq
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

User-defined Types, Contracts and Declaration Containers conventionally use PascalCase; built-in Type keywords keep their spellings. Functions, Fields, Properties, local bindings, parameters and other value names conventionally use camelCase.

| Notation | Uses |
| --- | --- |
| `[]` | Array and Dictionary construction, fixed-array Types, indexing, slicing and Capture Lists. |
| `()` | Ordered grouping: parameters, arguments, Tuples, Unit, Function Types, conditions and precedence. |
| `<>` | Type arguments and function length arguments ([§4.4](04-arrays-indexing-and-slices.md#44-function-length-parameters)). |
| `{...}` | A struct or enum Origin schema header, or a fresh binding-set name after a named Type ([§15.3](15-ownership-and-lifetime-analysis.md#153-origin-schemas-names-and-relations)); never a body or collection. Borrow annotations use `during` ([§3.3.2](03-types-and-values.md#332-nested-semantics-and-origin-annotations)). |
| `=` | Initialization, parameter default or assignment; acquisition follows Copy and Move ([§3.5](03-types-and-values.md#35-copy-and-move)). |
| `@` | An explicit operation: adaptation, `@move`, `@copy` or `@follow` ([§13.5](13-operators-and-assignment.md#135-explicit-operations)). |
| `->` | The result Type of a function or Function Type. |
| `=>` | A single-item executable Body, a parameter-name mapping or a Container-alias target. |
| `:` | Separates names from Types, argument names from values, Dictionary keys from values and labels from constructs; introduces bases, Contract parents and `: base(...)`. Never an executable Body. |
| `#` | A compile-time construct: lowercase directives such as `#if`, PascalCase Attributes such as `#Inline`. |
| `$` | A language-provided Composition Root operation ([§13.8](13-operators-and-assignment.md#138-reserved-syntax-and-the-composition-root)). |
| `;` | Only the task separator `task;` ([§24.2](24-suspension-and-asynchronous-tasks.md#242-task-slots)). |

A Type combines Semantics, a Core or object View Target, and Origins ([§3](03-types-and-values.md#3-types-and-values)).

```kimi
group Geometry
    func area(width: i32, height: i32) -> i32
        let sides = [width, height]
        return sides[0] * sides[1]
```

## 1.3. Normative status and wording

This specification defines language version `0.0.2`.

| Part | Status |
| --- | --- |
| Chapters 1–19, 22, 24 and Appendix F (grammar) | Normative language rules. Appendix F is the single normative grammar. |
| Chapter 23 and `schemas/check-output.schema.json` | Normative tool contracts; they never change which programs are valid. |
| Appendices D and E | Informative; the linked sections are authoritative. |
| IMPL (Chapters 20–21, Appendix A) | Contracts of the initial implementation profile; they never decide which programs are valid or what they mean. |

A conforming implementation accepts exactly the valid programs and gives them the specified behavior. An implementation may instead report Unsupported ([§23.1.1](23-compiler-services.md#2311-categories)) for a valid form it does not implement yet, and is then partial; it must never accept an invalid program or give a valid one another meaning. Appendix D lists features outside this revision; it grants no syntax or permission.

| Wording | Meaning |
| --- | --- |
| must / must not | A requirement or prohibition. The owning rule says whether a violation is a compile-time error, an Abort or undefined behavior. |
| may | Permission within all stated constraints. |
| should | A recommendation; not a condition of conformance. |
| implementation-defined | The implementation chooses within the stated limits and documents its choice. |
| unspecified | Any result within the stated limits is permitted, undocumented. This is not undefined behavior. |
| undefined behavior | The program has no defined behavior from that point on. Only a broken unsafe promise can cause it ([§5](05-raw-pointers-and-unsafe-memory.md#5-raw-pointers-and-unsafe-memory)); safe code alone never does. |

Unqualified declarative rules and imperative requirements are normative. Examples illustrate rules and add none; an API example may show only a signature, which does not permit a bodyless definition. Examples are independent of each other; application-defined Types and APIs in them are assumed, and an `// Error:` line intentionally violates a rule. `text` blocks show non-Kimigayo notation.
