// int pylon_eh_try(void (*body)(void*), void* arg, void* raw)
//
// 存被调用方保存寄存器 → 调 body(arg) → 返回 0。帧里写一条标记给个人风格（见 Guard.cpp）：
//     [sp+160] = kMarkMagic ^ (sp+160)   [sp+168] = raw
// 两个出口都擦掉它（死栈上的残留会被误认）。个人风格找到最内层标记后调 pylon_eh_land(mark-160)，
// 从同一个尾奏带 1 返回，对调用方就是"一个返回 1 的函数"。kMarkMagic 与 Guard.cpp 一致。

#if defined(__aarch64__)

    .text
    .p2align 2
    .globl  pylon_eh_try
    .hidden pylon_eh_try
    .type   pylon_eh_try, %function
pylon_eh_try:
    .cfi_startproc
    // 0x9b = 间接 | 相对 pc | sdata4：.eh_frame 指向存着个人风格地址的格子。
    .cfi_personality 0x9b, pylon_eh_personality_ref
    stp     x29, x30, [sp, #-176]!
    .cfi_def_cfa_offset 176
    .cfi_offset w30, -168
    .cfi_offset w29, -176
    mov     x29, sp
    .cfi_def_cfa w29, 176
    stp     x19, x20, [sp, #16]
    stp     x21, x22, [sp, #32]
    stp     x23, x24, [sp, #48]
    stp     x25, x26, [sp, #64]
    stp     x27, x28, [sp, #80]
    stp     d8, d9, [sp, #96]
    stp     d10, d11, [sp, #112]
    stp     d12, d13, [sp, #128]
    stp     d14, d15, [sp, #144]
    .cfi_offset w19, -160
    .cfi_offset w20, -152
    .cfi_offset w21, -144
    .cfi_offset w22, -136
    .cfi_offset w23, -128
    .cfi_offset w24, -120
    .cfi_offset w25, -112
    .cfi_offset w26, -104
    .cfi_offset w27, -96
    .cfi_offset w28, -88
    .cfi_offset b8, -80
    .cfi_offset b9, -72
    .cfi_offset b10, -64
    .cfi_offset b11, -56
    .cfi_offset b12, -48
    .cfi_offset b13, -40
    .cfi_offset b14, -32
    .cfi_offset b15, -24
    movz    x11, #0x6d6b
    movk    x11, #0x685f, lsl #16
    movk    x11, #0x5f65, lsl #32
    movk    x11, #0x626c, lsl #48
    add     x10, sp, #160
    eor     x11, x11, x10
    stp     x11, x2, [sp, #160]
    mov     x9, x0
    mov     x0, x1
    blr     x9
    mov     w0, #0
.Leh_restore:
    str     xzr, [sp, #160]
    ldp     d14, d15, [sp, #144]
    ldp     d12, d13, [sp, #128]
    ldp     d10, d11, [sp, #112]
    ldp     d8, d9, [sp, #96]
    ldp     x27, x28, [sp, #80]
    ldp     x25, x26, [sp, #64]
    ldp     x23, x24, [sp, #48]
    ldp     x21, x22, [sp, #32]
    ldp     x19, x20, [sp, #16]
    ldp     x29, x30, [sp], #176
    ret
    .cfi_endproc
    .size   pylon_eh_try, .-pylon_eh_try

// [[noreturn]] void pylon_eh_land(uintptr_t sp)：在展开器栈最底部调用，trampoline 之下全丢弃。
    .p2align 2
    .globl  pylon_eh_land
    .hidden pylon_eh_land
    .type   pylon_eh_land, %function
pylon_eh_land:
    mov     sp, x0
    mov     w0, #1
    b       .Leh_restore
    .size   pylon_eh_land, .-pylon_eh_land

    .section .data.rel.ro.pylon_eh_personality_ref, "aw", %progbits
    .p2align 3
    .hidden pylon_eh_personality_ref
pylon_eh_personality_ref:
    .xword  pylon_eh_personality
    .size   pylon_eh_personality_ref, 8

#endif

    .section .note.GNU-stack, "", %progbits
