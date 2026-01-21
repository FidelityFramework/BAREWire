# BAREWire Architecture Status - January 2026

## Current State: NTU INTEGRATED

BAREWire now defers to FNCS NTUKind for all type information:

- ✅ **Types.fs DELETED** - No local type system, NTUKind IS the type system
- ✅ **Schema uses NTUKind** - `SchemaType.NTU(kind, encoding)` pattern
- ✅ **Platform-resolved sizes** - `PlatformContext.resolveSize/resolveAlign`
- ✅ **No BCL** - Pure F#, compiles with Firefly
- ⚠️ **FNCS type alignment** - Some int/int32, byref issues remaining

## Schema Architecture

The schema layer pairs FNCS types with wire encoding:

```fsharp
type SchemaType =
    | NTU of kind: NTUKind * encoding: WireEncoding
    | FixedData of length: int
    | Enum of baseKind: NTUKind * values: Map<string, uint64>
    | Aggregate of AggregateType
    | TypeRef of name: string
```

### Key Files Changed

| File | Change |
|------|--------|
| `Core/Types.fs` | **DELETED** |
| `Schema/Definition.fs` | Uses NTUKind, defines SchemaType |
| `Schema/Analysis.fs` | Uses PlatformContext.resolveSize/resolveAlign |
| `Schema/Validation.fs` | Validates SchemaType |
| `Schema/DSL.fs` | Fluent API using Bare.* constructors |

## Compilation

- **Build**: Firefly using `.fidproj`
- **BCL .fsproj**: DELETED to avoid confusion
- **Status**: ~130 FNCS type errors (down from 237, originally 323)

### Fixed (January 2026)
- ✅ Core files (Error.fs, Binary.fs, Memory.fs, Capability.fs, Uuid.fs)
- ✅ Decoder.fs: byref→tuple conversion, tuple destructuring→match
- ✅ Hardware/Descriptors.fs: Active patterns→functions with DUs
- ✅ Memory/View.fs: Phantom type parameters removed
- ✅ Array slicing→manual loops (no `arr.[a..b]` syntax)
- ✅ String interpolation→concatenation

### Remaining Blockers
- Schema files need Map/List/Set/Seq intrinsics
- View.fs uses box/unbox (not available in FNCS)
- Missing: max, min, fst, snd, compare
- Missing: PlatformContext (import path issue?)

### Future: Use FNCS Memory Types
BAREWire's Core/Memory.fs should migrate to FNCS intrinsics:
- `Span<'T, 'region, 'access>` instead of local Memory type
- `MemoryRegions.stack/arena/peripheral/etc.` instead of MemoryRegionKind
- `Arena<'lifetime>` for bump allocation

## FNCS Intrinsics Status (Updated January 2026)

### Collection Operations - SIGNIFICANT PROGRESS

| Module | Status | Operations |
|--------|--------|------------|
| `List` | ✅ **COMPLETE** | map, fold, filter, exists, forall, length, rev, append, collect (Baker decomposition) |
| `List` | ✅ **COMPLETE** | empty, isEmpty, head, tail, cons (Alex primitives) |
| `Map` | ⚠️ Partial | empty, isEmpty (Alex primitives) |
| `Map` | ❌ Missing | add, tryFind, containsKey, values, keys, toList |
| `Set` | ⚠️ Partial | empty, isEmpty (Alex primitives) |
| `Set` | ❌ Missing | add, contains, remove |
| `Seq` | ✅ Cold | map, filter, take, fold, collect (Alex witnesses) |
| `Seq` | ❌ Missing | append, tryPick, exists, minBy, max |
| `Option` | ✅ **COMPLETE** | None, Some, isSome, isNone, get, defaultValue (Alex primitives) |
| `Option` | ❌ Missing | map, bind, filter |

### Still Missing (For BAREWire)

| Module | Missing Functions |
|--------|-------------------|
| `List` | tryPick, contains, minBy, max |
| `Map` | add, tryFind, containsKey, values, keys, toList, forall |
| `Set` | add, contains |
| Core | min, max, compare, fst, snd |
| Operators | `not` (logical) |

### Workarounds Used

- Lists converted to arrays (Array.zeroCreate, for loops)
- Maps converted to arrays of tuples
- String.concat replaced with custom concatWithSeparator
- byref parameters converted to tuple returns
- Memory<'T> phantom type parameter removed (not supported)

## Key Architectural Principles

### 1. NTUKind IS the Type System
BAREWire does NOT define its own primitives. It defers completely to FNCS.

### 2. Platform Widths at Resolution Time
No hardcoded sizes. Everything flows through PlatformContext:
```fsharp
let size = PlatformContext.resolveSize ctx NTUKind.NTUint32
// Returns 4 on all platforms for int32
```

### 3. WireEncoding is Separate from Type
A type's identity (NTUKind) is separate from how it's encoded on the wire:
- `NTUint64 + Fixed` → 8 bytes
- `NTUint64 + VarInt` → 1-10 bytes (LEB128)

## Remaining Work

### FNCS Type Alignment
- `int` vs `int32` (FNCS is stricter)
- `byref<int>` parameter handling in Decoder.fs
- String interpolation for non-string types
- Result type alias visibility

### Future
- Arena computation expressions
- Full escape analysis

## Related Memories
- `fncs_relationship` - FNCS NTUKind integration details
- `pure_fsharp_design_principles` - Design constraints
- `native_binding_role` - Platform binding patterns
