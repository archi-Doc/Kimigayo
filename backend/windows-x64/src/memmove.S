// Copyright (c) All contributors. MIT license.
#include "copy.inc"
.text
.globl memmove
.def memmove; .scl 2; .type 32; .endef
.p2align 4
.seh_proc memmove
memmove:
    .seh_endprologue
    mov %rcx, %rax
    cmp %rdx, %rcx
    je .Ldone
    jb .Lforward
    mov %rcx, %r9
    sub %rdx, %r9
    cmp %r8, %r9
    jae .Lforward
    // Overlap with destination above source: traverse from the end.
    add %r8, %rcx
    add %r8, %rdx
    cmp $8, %r8
    jb .Ltail
.Lback:
    sub $8, %rcx
    sub $8, %rdx
    mov (%rdx), %r9
    mov %r9, (%rcx)
    sub $8, %r8
    cmp $8, %r8
    jae .Lback
.Ltail:
    test %r8, %r8
    jz .Ldone
.Lbyte:
    dec %rcx
    dec %rdx
    mov (%rdx), %r9b
    mov %r9b, (%rcx)
    dec %r8
    jnz .Lbyte
.Ldone:
    ret
.Lforward:
    copy_forward
.seh_endproc
