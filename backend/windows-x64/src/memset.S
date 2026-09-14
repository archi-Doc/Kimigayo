// Copyright (c) All contributors. MIT license.
.text
.globl memset
.def memset; .scl 2; .type 32; .endef
.p2align 4
.seh_proc memset
memset:
    .seh_endprologue
    mov %rcx, %rax
    movzbl %dl, %edx
    movabs $0x0101010101010101, %r9
    imul %rdx, %r9
    cmp $8, %r8
    jb .Ltail
.Lwords:
    mov %r9, (%rcx)
    add $8, %rcx
    sub $8, %r8
    cmp $8, %r8
    jae .Lwords
.Ltail:
    test %r8, %r8
    jz .Ldone
.Lbyte:
    mov %dl, (%rcx)
    inc %rcx
    dec %r8
    jnz .Lbyte
.Ldone:
    ret
.seh_endproc
