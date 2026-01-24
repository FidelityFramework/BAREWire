# BAREWire and FNCS Relationship - January 2026

## Status: FNCS MATURE, BIDIRECTIONAL INTEGRATION

**FNCS reached production maturity in January 2026.** Firefly samples 01-09 all pass with principled implementations including full DU infrastructure, closure layouts, and Baker decomposition for all collection operations.

## Architecture

BAREWire and FNCS have a **bidirectional relationship**:

```
BAREWire ──defers to──▶ FNCS NTUKind (Native Type Universe)
BAREWire ──uses──▶ FNCS PlatformContext (size/alignment resolution)
FNCS ──uses──▶ BAREWire (wire protocol for native serialization)
```

## Critical Principle: NTUKind IS the Type System

**BAREWire does NOT define its own primitive types.** The old `Types.fs` with `PrimitiveType` DU was DELETED (January 2026).

| Before | After |
|--------|-------|
| `PrimitiveType.U8` | `NTUKind.NTUuint8` |
| `PrimitiveType.I32` | `NTUKind.NTUint32` |
| `PrimitiveType.String` | `NTUKind.NTUstring` |
| `Type.Primitive` | `SchemaType.NTU(kind, encoding)` |
| Hardcoded sizes | `PlatformContext.resolveSize ctx kind` |
| Hardcoded alignment | `PlatformContext.resolveAlign ctx kind` |

## What BAREWire Uses from FNCS

### NativeTypedTree.NativeTypes
- `NTUKind` - The native type universe (35 type kinds)
- `PlatformContext` - Platform info (WordSize, PointerSize, PointerAlign)
- `PlatformContext.resolveSize` - Get byte size for NTUKind
- `PlatformContext.resolveAlign` - Get alignment for NTUKind

### Memory Regions (NativeGlobals.fs)
FNCS provides type-safe memory regions via measure types:
- `MemoryRegions.stack` - Stack memory (automatic cleanup)
- `MemoryRegions.arena` - Arena/heap memory
- `MemoryRegions.sram` - Fast on-chip RAM (embedded)
- `MemoryRegions.flash` - Persistent storage (embedded)
- `MemoryRegions.peripheral` - Memory-mapped I/O
- `MemoryRegions.dma` - DMA-accessible memory

### Memory Types
- `Span<'T, 'region, 'access>` - Fat pointer with region/access safety
- `ReadOnlySpan<'T, 'region>` - Immutable view
- `Ptr<'T, 'region, 'access>` - Native pointer with tracking
- `Arena<'lifetime>` - Bump allocator

**TODO**: BAREWire's Core/Memory.fs defines its own `Memory` and `Buffer` types.
These should be migrated to use FNCS `Span<uint8, 'region, 'access>` directly.

### Sys Intrinsics
- `Sys.clock_gettime` - Get current time
- `Sys.clock_monotonic` - Monotonic clock
- `Sys.tick_frequency` - Timer frequency
- `Sys.nanosleep` - High-precision sleep

### Arena (FNCS Intrinsic)
- `Arena.fromPointer` - Create arena from backing memory
- `Arena.alloc` - Bump allocate
- `Arena.allocAligned` - Aligned allocation
- `Arena.remaining` - Query remaining capacity
- `Arena.reset` - Reset to empty

## Schema Architecture

BAREWire schemas pair NTUKind with wire encoding:

```fsharp
type WireEncoding =
    | Fixed          // Natural size from NTU
    | VarInt         // LEB128 variable-length
    | LengthPrefixed // Length prefix + content

type SchemaType =
    | NTU of kind: NTUKind * encoding: WireEncoding
    | FixedData of length: int
    | Enum of baseKind: NTUKind * values: Map<string, uint64>
    | Aggregate of AggregateType
    | TypeRef of name: string
```

## Platform Width Resolution

Sizes are NOT hardcoded. They come from FNCS PlatformContext:

```fsharp
let getTypeSize (ctx: PlatformContext) (schema: SchemaDefinition) (typ: SchemaType) =
    match typ with
    | SchemaType.NTU(kind, WireEncoding.Fixed) ->
        let size = PlatformContext.resolveSize ctx kind  // Platform-specific!
        { Min = size; Max = Some size; IsFixed = true }
```

## Layer Position

| Layer | Component | Role |
|-------|-----------|------|
| 1 | FNCS NTUKind | Type identity (what the type IS) |
| 2 | FNCS PlatformContext | Platform metadata (sizes, alignment) |
| 3 | **BAREWire SchemaType** | Wire encoding strategy |
| 4 | **BAREWire Schema** | Message structure definition |

## Compilation

BAREWire compiles with Firefly using `.fidproj` format.
BCL `.fsproj` was DELETED to avoid confusion.

## Current Build Status (January 2026)

**FNCS is production-ready.** All blockers that were waiting on FNCS intrinsics are now resolved:

| Former Blocker | Status |
|----------------|--------|
| Collection intrinsics (List, Map, Set, Seq) | ✅ RESOLVED - Baker decomposition complete |
| Option.map/bind/filter | ✅ RESOLVED - OptionRecipes implemented |
| Result type support | ✅ RESOLVED - DULayout coeffect for heterogeneous DUs |
| Closure captures | ✅ RESOLVED - Flat closure model with ClosureLayout coeffect |

BAREWire schema architecture is complete and ready for full integration with FNCS native compilation.
