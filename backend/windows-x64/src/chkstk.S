// Copyright (c) All contributors. MIT license.
// Special Windows x64 probe ABI: RAX is the allocation size and is preserved.
// Probe downward from the caller's pre-call RSP, without installing its frame.
// All general registers are preserved. Flags may change.
.text
.globl __chkstk
.def __chkstk; .scl 2; .type 32; .endef
.p2align 4
.seh_proc __chkstk
__chkstk:
    push %rcx
    .seh_stackalloc 8
    push %rdx
    .seh_stackalloc 8
    .seh_endprologue
    lea 24(%rsp), %rcx
    mov %rax, %rdx
    test %rdx, %rdx
    jz .Ldone
.Lpages:
    cmp $4096, %rdx
    jb .Ltail
    sub $4096, %rcx
    testb $0, (%rcx)
    sub $4096, %rdx
    jnz .Lpages
    jmp .Ldone
.Ltail:
    sub %rdx, %rcx
    testb $0, (%rcx)
.Ldone:
    pop %rdx
    pop %rcx
    ret
.seh_endproc
