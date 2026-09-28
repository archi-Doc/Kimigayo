// Copyright (c) All contributors. MIT license.
.text
.globl test_probe
.def test_probe; .scl 2; .type 32; .endef
.seh_proc test_probe
test_probe:
    sub $40, %rsp
    .seh_stackalloc 40
    .seh_endprologue
    mov %rcx, 32(%rsp)
    mov %rcx, %rax
    mov $12345, %edx
    call __chkstk
    cmp 32(%rsp), %rax
    jne .Lbad
    cmp %rax, %rcx
    jne .Lbad
    cmp $12345, %edx
    jne .Lbad
    xor %eax, %eax
    jmp .Ldone
.Lbad:
    mov $1, %eax
.Ldone:
    add $40, %rsp
    ret
.seh_endproc
