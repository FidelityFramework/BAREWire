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
- **Status**: 237 FNCS type errors (down from 323)
  - Mostly int/int32, byref handling, string interpolation

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
