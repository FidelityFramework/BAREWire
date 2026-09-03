# BAREWire Hardware Memory Descriptor System

## The "Fidelity" Principle

> **The entire point of Fidelity is that the F# compiler controls memory layout - not MLIR, not LLVM.**

Types (including memory region types, access kinds, and peripheral descriptors) carry semantic meaning through the entire compilation pipeline. They ARE erased - but at the **last possible lowering stage**, after Fidelity has made all memory layout decisions. **Fidelity dictates; LLVM implements.**

## Status: DESCRIPTOR LAYER IMPLEMENTED (March 2026)

`Hardware/Descriptors.fs` is fully implemented with NTUKind integration. All types use
FNCS NTUKind — no local type system. BAREWire compiles via fidproj with Composer.

### Implemented in Hardware/Descriptors.fs
- `MemoryRegionKind` DU: Flash, SRAM, Peripheral, SystemControl, DMA, CCM
- `AccessKind` DU: ReadOnly (__I), WriteOnly (__O), ReadWrite (__IO) — maps 1:1 to CMSIS qualifiers
- `BitFieldDescriptor` [Struct]: Name, Position, Width, Access — for _Pos/_Msk macros
- `FieldDescriptor`: Name, Offset (bytes), Type (NTUKind), Access, BitFields array, Documentation
- `PeripheralLayout`: Size, Alignment, Fields array
- `PeripheralDescriptor`: Name, Instances ((string * unativeint) array), Layout, MemoryRegion
- `StructDescriptor`: For non-hardware ABI-critical structs (ioctl, DMA descriptors)
- `VolatilityKind`, `CacheabilityKind`, `ExecutabilityKind` classification DUs
- `MemoryRegion` module: getVolatility, getCacheability, getExecutability, isVolatile, isCacheable
- Constructor DSL modules: `Field.simple/withDoc/withBitFields`, `BitField.create/flag`,
  `Peripheral.single/multi`, `Layout.create/withNaturalAlignment`, `StructLayout.create`

### Not Yet Implemented (Access Layer)
- No volatile read/write primitives — these are CCS/Alex concerns, not BAREWire
  (CCS has `NTUMemorySpace.Peripheral` defined; Alex witness wiring is the remaining gap)
- No `PeripheralView` type binding descriptor + base address into the accessor
- No memory map descriptor composing multiple peripherals with chip address ranges
- No interrupt vector table descriptor (vector tables handled via `[<VectorTable>]` attribute in Clef)

### Current BAREWire File Structure
- `Hardware/Descriptors.fs` — peripheral/struct descriptor types + constructor DSL
- `Encoding/Memory.fs` — byte-array-backed buffer for serialization (NOT MMIO)
- `Encoding/Encoder.fs` / `Decoder.fs` — BARE wire format encode/decode
- `Encoding/Codec.fs` — IBARECodec interface
- `Schema/Definition.fs` — SchemaType using NTUKind, WireEncoding, AggregateType
- `Schema/Validation.fs` / `Analysis.fs` / `DSL.fs` — schema validation and fluent API

## Farscape → BAREWire Generation Pattern

Farscape parses C headers and can emit `PeripheralDescriptor` source text referencing
BAREWire types. Device-specific descriptors live in binding libraries, not BAREWire itself:
- `Fidelity.CMSIS.Core` — NVIC, SysTick, SCB, GPIO peripheral descriptors
- `Fidelity.STM32.HAL` — vendor HAL function bindings (FidelityExtern, not descriptors)
- `Fidelity.Renesas.FSP` — vendor HAL function bindings

BAREWire provides the vocabulary types. Farscape populates them. Binding libraries carry them.

## Farscape Dependency Relationship

BAREWire serves as a foundational dependency for Farscape. When Farscape parses C/C++ headers for hardware targets (CMSIS, HAL libraries), it needs BAREWire types to express the hardware memory catalog.

**BAREWire development should advance in parallel with Farscape** to provide the memory descriptor infrastructure.

## Core Design: Invisible Memory Management

From "Memory Management By Choice":
> "In the mature phase, developers write idiomatic F# without memory annotations... The compiler identifies patterns and employs region-based memory management, eliminating individual allocations and providing deterministic cleanup without explicit developer intervention."

For hardware targets, this means:
- Developer writes clean F# peripheral access code
- Compiler uses memory descriptors to generate correct hardware access
- Tree-shaking keeps only used peripherals
- No manual memory/layout management required

## Microcontroller Memory Map Model

Different memory regions have different characteristics:

| Region | Typical Address | Characteristics |
|--------|-----------------|-----------------|
| Flash | `0x0800_0000` | Execute-in-place, read-only at runtime |
| SRAM | `0x2000_0000` | Stack, heap, .bss, .data |
| Peripherals | `0x4000_0000+` | Volatile, specific access widths |
| System | `0xE000_0000` | ARM core peripherals |

Alex (in Firefly) uses `MemoryRegionKind` to determine:
- Whether to emit volatile access
- Cache behavior hints
- Valid access widths

## Access Kind Semantics

CMSIS uses `__I`, `__O`, `__IO` qualifiers that encode hardware constraints:

- **ReadOnly (`__I`)**: Hardware state register. Writing is undefined behavior.
  - Example: `GPIO->IDR` (input data register)
  
- **WriteOnly (`__O`)**: Trigger register. Reading returns undefined.
  - Example: `GPIO->BSRR` (bit set/reset register)
  
- **ReadWrite (`__IO`)**: Normal register with defined read/write behavior.
  - Example: `GPIO->ODR` (output data register)

Code generation MUST respect these constraints for correct hardware operation.

## Compilation Pipeline Role

```
Farscape parses CMSIS headers (CppParser.fs + clang JSON AST)
    ↓
Extracts structs with __IO/__I/__O qualifiers, base address macros, bit field _Pos/_Msk
    ↓
Emits BAREWire PeripheralDescriptor source + [<FidelityExtern>] binding declarations
    ↓
CCS type-checks binding library (NTUMemorySpace.Peripheral on MMIO types)
    ↓
Baker saturates (extern nodes pass through as leaves)
    ↓
PlatformBindingResolution resolves FidelityExtern → ExternCall bindings
    ↓
Alex witnesses: Ptr.read/Ptr.write → memref.load/memref.store
    (Gap: Peripheral memory space should trigger volatile flag on LLVM lowering)
    ↓
LLVM backend: arm-none-eabi target, -nostdlib -static -ffreestanding
```

## Future: Unified Memory Description

The hardware descriptor system should eventually unify with BAREWire's IPC/serialization memory descriptions:

- **Hardware peripherals**: Fixed addresses, volatile, hardware-constrained access
- **Shared memory IPC**: Dynamic addresses, potentially volatile, software-defined layout
- **Network buffers**: Dynamic, schema-driven, serialization-aware

All use the same underlying memory layout primitives, with different allocation and access semantics.
