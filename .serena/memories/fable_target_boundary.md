# BAREWire Fable Target Boundary

## Dual-Target Architecture

BAREWire supports two compilation targets:

1. **Firefly (Native)**: Full capability-based memory model
2. **Fable (JavaScript)**: Encoding/decoding for WREN stack WebSocket IPC

This is NOT about making all of BAREWire work in JavaScript. It's about enabling
the specific subset needed for type-safe communication in WREN stack applications.

## Module Classification

### Dual-Target Modules (Firefly + Fable)

These modules work on both targets and are essential for WREN stack:

| Module | File | Purpose |
|--------|------|---------|
| `Buffer` type | `Core/Memory.fs` | Sequential write buffer |
| `Buffer` module | `Core/Memory.fs` | Buffer operations |
| `Binary` module | `Core/Binary.fs` | Byte conversion |
| `Encoder` module | `Encoding/Encoder.fs` | BARE encoding |
| `Decoder` module | `Encoding/Decoder.fs` | BARE decoding |
| `Utf8` module | `Core/Utf8.fs` | UTF-8 string conversion |

### Firefly-Only Modules

These modules use native concepts that don't exist in JavaScript:

| Module | File | Why Firefly-Only |
|--------|------|------------------|
| `Memory<'T,'region>` type | `Core/Memory.fs` | Region measure types |
| `Memory` module | `Core/Memory.fs` | Capability operations |
| `fromNativePointer` | `Core/Memory.fs` | Native pointer interop |
| `Capability` module | `Core/Capability.fs` | Lifetime markers |
| `Region` module | `Memory/Region.fs` | Region abstractions |
| `View` module | `Memory/View.fs` | Memory views |
| `SafeMemory` module | `Memory/SafeMemory.fs` | Safe memory ops |

## Fable Block Conventions

### When to Use `#if FABLE`

Use Fable blocks for **platform-specific implementations** of dual-target APIs:

```fsharp
// Binary.fs - different implementations, same API
let singleToInt32Bits (value: float32) : int32 =
#if FABLE
    jsFloat32ToInt32 value  // JavaScript DataView
#else
    // NativePtr reinterpretation
    let mutable v = value
    let valuePtr = &&v
    NativePtr.read (NativePtr.ofNativeInt<int32> (NativePtr.toNativeInt valuePtr))
#endif
```

### When NOT to Use `#if FABLE`

Do NOT use Fable blocks to:
- Create "shim" types that don't make sense in JS (e.g., `type Span<'T> = byte[]`)
- Provide degraded functionality for native-only concepts
- Work around fundamental architectural differences

Instead, mark modules as Firefly-only in documentation.

## Documentation Requirements

### Module-Level Documentation

Each module should clearly state its target support:

```fsharp
/// <summary>
/// Buffer operations (Dual-target: Firefly + Fable).
/// </summary>
/// <remarks>
/// Available in both targets for WREN stack WebSocket IPC.
/// </remarks>
module Buffer = ...
```

### Section Markers in Mixed Files

Files with both dual-target and Firefly-only sections should use clear markers:

```fsharp
// ============================================================================
// BUFFER TYPE (Dual-target: Firefly + Fable)
// Used by Encoding module for WREN stack WebSocket communication
// ============================================================================

// ============================================================================
// MEMORY OPERATIONS (Firefly only)
// ============================================================================
```

## Testing Dual-Target Code

Dual-target code should be testable on both targets:

1. **Unit tests**: Run via `dotnet test` (uses .NET runtime, close to Fable behavior)
2. **Integration tests**: Actual WREN stack communication tests

The tests in `tests/` use .NET runtime, which is acceptable since they verify
the logic, and Fable compilation is verified by actual WREN stack builds.

## Cross-References

- WREN integration: `wren_stack_integration` memory
- Architecture status: `architecture_status_january_2026` memory
- Pure F# principles: `pure_fsharp_design_principles` memory
