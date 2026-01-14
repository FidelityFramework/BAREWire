# BAREWire and WREN Stack Integration

## Overview

BAREWire provides the binary serialization layer for WREN stack applications,
enabling type-safe communication between the native Firefly backend and the
Fable/JavaScript frontend over WebSocket.

## WREN Stack Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    WREN Stack Application                    │
├─────────────────────┬───────────────────────────────────────┤
│   Frontend (Fable)  │         Backend (Firefly)             │
│                     │                                        │
│  Partas.Solid UI    │      Native F# Application Logic      │
│  DaisyUI/Tailwind   │      Hardware Access, File I/O        │
│                     │      FNCS Sys Intrinsics              │
├─────────────────────┴───────────────────────────────────────┤
│              WebSocket + BAREWire Binary Protocol           │
└─────────────────────────────────────────────────────────────┘
```

## Why BAREWire for WREN?

1. **Shared Type Definitions**: Same F# types compile to both targets
2. **Binary Efficiency**: Compact encoding vs JSON text
3. **Zero Parsing**: Bytes decode directly to typed structures
4. **Type Safety**: Compile-time guarantees on both sides

## Shared Protocol Pattern

The Shared project contains message types compiled by BOTH Fable and Firefly:

```fsharp
// Shared/Protocol.fs - compiled by both targets
namespace MyApp.Shared

type Command =
    | LoadFile of path: string
    | SaveFile of path: string * content: byte[]
    | QueryHardware

type Event =
    | FileLoaded of content: byte[] * metadata: FileMetadata
    | FileSaved of success: bool
    | HardwareStatus of status: HardwareInfo
```

## BAREWire Modules Used by WREN

| Module | Purpose | Fable? |
|--------|---------|--------|
| `BAREWire.Core.Buffer` | Sequential write buffer | ✅ Yes |
| `BAREWire.Core.Binary` | Byte conversion utilities | ✅ Yes |
| `BAREWire.Encoding.Encoder` | BARE encoding (ULEB128, strings) | ✅ Yes |
| `BAREWire.Encoding.Decoder` | BARE decoding | ✅ Yes |
| `BAREWire.Core.Memory` | Capability-based memory | ❌ Firefly only |
| `BAREWire.Memory.*` | Region/View abstractions | ❌ Firefly only |

## Communication Flow

```
Frontend (JS)                              Backend (Native)
    │                                           │
    │  1. User action triggers command          │
    │  2. Encode Command via BAREWire           │
    │  3. Send binary frame over WebSocket ───► │
    │                                           │  4. Decode Command
    │                                           │  5. Process (file I/O, etc)
    │                                           │  6. Encode Event via BAREWire
    │  ◄─── 7. Receive binary frame ─────────── │
    │  8. Decode Event                          │
    │  9. Update UI signals                     │
    │                                           │
```

## Build Process ("The Weld")

The WREN build coordinates two parallel tracks:

1. **Fable Track**: Frontend/*.fs → Fable → JavaScript → Vite → index.html
2. **Firefly Track**: Backend/*.fs → FNCS → PSG → MLIR → LLVM → Native

Both tracks compile the Shared project:
- Fable produces JavaScript with BAREWire codecs
- Firefly produces native code with matching codecs

The "weld" embeds the frontend HTML into the native binary's `.rodata` section.

## Key Design Decisions

### No Interfaces in Shared Code

The Shared project must use pure F# patterns:
- ✅ Discriminated unions
- ✅ Records
- ✅ Module functions
- ❌ Interfaces (BCL pattern)
- ❌ Abstract classes

### Codec Implementation

Codecs should be module functions, not interface implementations:

```fsharp
// Good - module functions
module CommandCodec =
    let encode (buffer: Buffer byref) (cmd: Command) = ...
    let decode (bytes: byte[]) (offset: byref<int>) = ...

// Avoid - interface pattern
type CommandCodec() =
    interface ICodec<Command> with ...
```

## Cross-References

- WREN Stack blog post: `~/repos/SpeakEZ/hugo/content/blog/WREN Stack.md`
- WrenHello example: `~/repos/WrenHello/`
- Fable boundary docs: `fable_target_boundary` memory
