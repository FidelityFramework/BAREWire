# BAREWire Architecture Status - January 2026

## Current State: CLEAN

BAREWire is now a pure F# library with no BCL dependencies:

- ✅ **No Alloy dependencies** - All absorbed into FNCS
- ✅ **No BCL code** - No `System.*` in source code
- ✅ **No BCL patterns** - DUs + modules instead of interfaces
- ✅ **Clean XML docs** - No `<exception cref="System.Exception">`

## Compilation Targets

- **Firefly (native)**: Uses NativePtr, FNCS Sys intrinsics
- **Fable (JavaScript)**: Uses Fable.Core JS interop

## Key Files

| File | Purpose |
|------|---------|
| `Core/Capability.fs` | Lifetime measure types (stack, arena, static', heap) |
| `Core/Memory.fs` | Memory<'T,'region> + Buffer struct |
| `Core/Binary.fs` | Pure bit conversion (NativePtr/JS DataView) |
| `Core/Time.fs` | FNCS Sys intrinsics (clock_gettime, nanosleep) |
| `Core/Utf8.fs` | Pure UTF-8 encoding |
| `Encoding/Encoder.fs` | BARE encoding with Buffer |
| `Encoding/Decoder.fs` | BARE decoding from arrays |

## Architectural Decisions

### Fable Support
Fable blocks exist for JavaScript compilation via `#if FABLE`.
Question pending: Should Fable support be removed since BAREWire is designed for native memory operations?

### Memory Model
Uses capability-based memory with measure types for:
- Region tracking (`'region`)
- Access control (`ReadOnly`, `ReadWrite`, `WriteOnly`)
- Lifetime tracking (`stack`, `arena`, `static'`, `heap`)

## HelloWorld Integration Points

1. **String literals** → static lifetime capability
2. **readln result** → stack lifetime (PROBLEM: currently dies when frame pops)
3. **Concatenation** → must allocate with appropriate lifetime
4. **writeln argument** → borrows capability for I/O duration

## Next Steps
- Integrate with Firefly HelloWorld sample
- Demonstrate BAREWire memory management patterns
- Fix readln stack lifetime issue

## Related Memories
- `wren_stack_integration` - WREN stack WebSocket communication
- `fable_target_boundary` - Dual-target module classification
- `fncs_relationship` - FNCS intrinsics usage
- `pure_fsharp_design_principles` - Design constraints

## Cross-References
- See `barewire_memory_architecture` in Firefly project
- See `deterministic_memory_management` in Firefly project
