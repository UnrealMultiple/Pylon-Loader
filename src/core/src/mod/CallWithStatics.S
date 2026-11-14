// int32_t pylon_call_with_x27(void* arg0, void* arg1, void* fn, void* statics)
//
// x27 注入桥：先给 x27 装上调用方给的静态块地址，再调 fn(arg0, arg1)。给 SaveWorldTilesFast
// 这类"函数体开头 ldr x8,[x8,#0xb8] 取静态块、之后全程用块内裸指针"的编译产物用：配合把那条
// ldr 改成 `mov x8, x27`（见 PylonMod_PatchInstruction），一次调用读到的就是注入的块——
// 游戏其余部分看到的还是真块。x19..x28 是被调用方保存寄存器，fn 内部不还原 x27 也不影响外部。

#if defined(__aarch64__)

    .text
    .p2align 2
    .globl  pylon_call_with_x27
    .hidden pylon_call_with_x27
    .type   pylon_call_with_x27, %function
pylon_call_with_x27:
    .cfi_startproc
    stp     x29, x30, [sp, #-32]!
    .cfi_def_cfa_offset 32
    .cfi_offset w30, -24
    .cfi_offset w29, -32
    str     x27, [sp, #16]
    .cfi_offset w27, -16
    mov     x29, sp
    .cfi_def_cfa w29, 32
    mov     x27, x3
    blr     x2
    ldr     x27, [sp, #16]
    ldp     x29, x30, [sp], #32
    .cfi_def_cfa sp, 0
    .cfi_restore w27
    .cfi_restore w29
    .cfi_restore w30
    ret
    .cfi_endproc
    .size   pylon_call_with_x27, .-pylon_call_with_x27

#endif
