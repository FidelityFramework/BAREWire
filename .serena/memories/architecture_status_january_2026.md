# BAREWire Architecture Status - January 2026

## Current State: FNCS MATURE, ACTIVE INTEGRATION

**FNCS has reached production maturity (January 2026).** Firefly samples 01-09 all pass with principled implementations. BAREWire is now actively being used by FNCS for wire protocol needs.

### Integration Status

- ✅ **Types.fs DELETED** - No local type system, NTUKind IS the type system
- ✅ **Schema uses NTUKind** - `SchemaType.NTU(kind, encoding)` pattern
- ✅ **Platform-resolved sizes** - `PlatformContext.resolveSize/resolveAlign`
- ✅ **No BCL** - Pure F#, compiles with Firefly
- ✅ **FNCS collections mature** - List, Map, Set, Option, Seq all have Baker decomposition
- ✅ **DU infrastructure complete** - DULayout coeffect for heterogeneous DUs (Result, etc.)

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
- **Status**: Ready for full integration with FNCS

### Completed Migrations
- ✅ Core files (Error.fs, Binary.fs, Memory.fs, Capability.fs, Uuid.fs)
- ✅ Decoder.fs: byref→tuple conversion, tuple destructuring→match
- ✅ Hardware/Descriptors.fs: Active patterns→functions with DUs
- ✅ Memory/View.fs: Phantom type parameters removed
- ✅ Array slicing→manual loops
- ✅ String operations via FNCS intrinsics

### FNCS Blockers RESOLVED
- ✅ Map/List/Set/Seq intrinsics - NOW AVAILABLE via Baker decomposition
- ✅ Option.map/bind/filter - NOW AVAILABLE
- ✅ Result type with DULayout coeffect - NOW AVAILABLE

### Future: Use FNCS Memory Types
BAREWire's Core/Memory.fs should migrate to FNCS intrinsics:
- `Span<'T, 'region, 'access>` instead of local Memory type
- `MemoryRegions.stack/arena/peripheral/etc.` instead of MemoryRegionKind
- `Arena<'lifetime>` for bump allocation

## FNCS Intrinsics Status (January 2026) - MATURE

### Collection Operations - COMPLETE

| Module | Status | Operations |
|--------|--------|------------|
| `List` | ✅ **COMPLETE** | map, fold, filter, exists, forall, length, rev, append, collect, contains, tryPick, minBy, max, forall2, sumBy (Baker decomposition) |
| `List` | ✅ **COMPLETE** | empty, isEmpty, head, tail, cons (Alex primitives) |
| `Map` | ✅ **COMPLETE** | toList, toSeq, tryFind, add, containsKey, keys, values, forall (Baker decomposition) |
| `Map` | ✅ **COMPLETE** | empty, isEmpty (Alex primitives) |
| `Set` | ✅ **COMPLETE** | add, contains, remove, union, intersect, difference (Baker decomposition) |
| `Set` | ✅ **COMPLETE** | empty, isEmpty (Alex primitives) |
| `Seq` | ✅ **COMPLETE** | map, filter, collect, append, toList, toArray, fold, tryPick, max, min, minBy, maxBy, exists, forall, length, isEmpty, head, tryHead (Baker decomposition) |
| `Seq` | ✅ **COMPLETE** | empty, getEnumerator (Alex primitives) |
| `Option` | ✅ **COMPLETE** | map, bind, filter (Baker decomposition) |
| `Option` | ✅ **COMPLETE** | None, Some, isSome, isNone, get, defaultValue (Alex primitives) |

### DU Infrastructure - COMPLETE

| Feature | Status | Notes |
|---------|--------|-------|
| Homogeneous DUs | ✅ | Inline struct representation (e.g., Option) |
| Heterogeneous DUs | ✅ | Arena allocation with DULayout coeffect (e.g., Result) |
| Bits coercion | ✅ | float64→int64, float32→int32 for DU slot storage |

### Coeffect System - COMPLETE

| Coeffect | Status | Purpose |
|----------|--------|---------|
| NodeSSAAllocation | ✅ | Pre-computed SSA assignments |
| ClosureLayout | ✅ | Flat closure struct layout |
| DULayout | ✅ | Arena-allocated DU construction |

### BAREWire Can Now Use

All collection intrinsics are available. No workarounds needed for:
- List operations (map, fold, filter, etc.)
- Map operations (add, tryFind, toList, etc.)
- Set operations (add, contains, union, etc.)
- Option operations (map, bind, filter)
- Seq operations (full cold sequence support)

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

### BAREWire-Specific Integration
- Migrate Core/Memory.fs to use FNCS `Span<uint8, 'region, 'access>` directly
- Wire up BAREWire schemas to FNCS compilation pipeline
- Test full encode/decode cycles through Firefly

### Future Enhancements
- Arena computation expressions
- Full escape analysis integration
- IPC patterns for WrenStack integration

## Related Memories
- `fncs_relationship` - FNCS NTUKind integration details
- `pure_fsharp_design_principles` - Design constraints
- `native_binding_role` - Platform binding patterns
