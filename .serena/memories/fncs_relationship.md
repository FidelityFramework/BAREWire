# BAREWire and FNCS Relationship - January 2026

## Architecture

BAREWire uses FNCS intrinsics directly for platform operations:

```
BAREWire ──uses intrinsics from──▶ FNCS (F# Native Compiler Services)
```

## What BAREWire Uses from FNCS

### Sys Intrinsics
- `Sys.clock_gettime` - Get current time
- `Sys.clock_monotonic` - Monotonic clock
- `Sys.tick_frequency` - Timer frequency
- `Sys.nanosleep` - High-precision sleep

### NativePtr Operations (F# Core)
- `NativePtr.read` / `NativePtr.write` - Memory access
- `NativePtr.ofNativeInt` / `NativePtr.toNativeInt` - Pointer conversion

### String Semantics
Standard F# `string` with FNCS native semantics (UTF-8 fat pointer).

## What BAREWire Does NOT Use

- **No Alloy** - Alloy was absorbed into FNCS (January 2026)
- **No BCL** - Pure F# with FNCS intrinsics
- **No Runtime** - Compiles to freestanding native code

## Layer Position

| Layer | Component | Role |
|-------|-----------|------|
| 1 | FNCS Intrinsics | Compiler-level primitives |
| 2 | **BAREWire** | Memory descriptors, serialization |
| 3 | Farscape | Hardware binding generation |
| 4 | User Code | Application logic |

## Compilation

BAREWire compiles with Firefly, not `dotnet build`.
Uses `.fidproj` format for project configuration.
