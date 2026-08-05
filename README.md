# BAREWire

Type-safe binary encoding and zero-copy memory operations for the Fidelity Framework.

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![License: Commercial](https://img.shields.io/badge/License-Commercial-orange.svg)](Commercial.md)

<p align="center">
🚧 <strong>Under Active Development</strong> 🚧<br>
<em>This project is in early development and not intended for production use.</em>
</p>

## Overview

BAREWire implements the [BARE (Binary Application Record Encoding)](https://baremessages.org/) protocol with F#'s type system providing compile-time safety. It enables zero-copy operations, structured memory access, and efficient inter-process communication - all without runtime overhead.

### Key Characteristics

- **Zero Dependencies**: Pure F# implementation (FSharp.UMX for phantom types)
- **Type Safety**: Leverages units of measure for compile-time memory safety
- **Zero-Copy Operations**: Direct memory access without intermediate allocations
- **Schema-Driven**: Type-safe DSL for defining binary data structures
- **Modular Design**: Use only the components you need

## The Fidelity Framework

BAREWire is part of the **Fidelity** native compilation ecosystem:

| Project | Role |
|---------|------|
| **Composer** | AOT compiler: Clef → PSG → MLIR → native binary (formerly Firefly) |
| **CCS** | Clef Compiler Services — intrinsics, the Native Type Universe, `NTUKind` (formerly FNCS) |
| **BAREWire** | Binary encoding, memory mapping, zero-copy IPC |
| **[Farscape](https://github.com/speakeztech/farscape)** | C/C++ header parsing for native library bindings |
| **[XParsec](https://github.com/speakeztech/xparsec)** | Parser combinators powering PSG traversal and header parsing |

The name "Fidelity" reflects the framework's core mission: **preserving type and memory safety** from source code through compilation to native execution.

## Dual-Target Architecture

BAREWire supports two compilation targets:

| Target | Use Case | Available Modules |
|--------|----------|-------------------|
| **Native** | Native desktop/embedded applications (Composer) | All modules |
| **Fable** | WREN stack WebSocket IPC | Encoding modules only |

### WREN Stack Integration

The [WREN Stack](https://speakez.tech/blog/wren-stack/) (WebView + Reactive + Embedded + Native) uses BAREWire for type-safe communication between the native Composer backend and the Fable/JavaScript frontend:

```
┌─────────────────────────────────────────────────────────┐
│                 WREN Stack Application                   │
├────────────────────┬────────────────────────────────────┤
│  Frontend (Fable)  │      Backend (native)              │
│  Partas.Solid UI   │      Native Application Logic      │
├────────────────────┴────────────────────────────────────┤
│         WebSocket + BAREWire Binary Protocol            │
└─────────────────────────────────────────────────────────┘
```

### Module Availability by Target

**Dual-Target (native + Fable):**
- `BAREWire.Core.Buffer` - Sequential write buffer
- `BAREWire.Core.Binary` - Byte conversion utilities
- `BAREWire.Encoding.*` - BARE encoding/decoding

**Native-Only:**
- `BAREWire.Core.Memory<'T,'region>` - Capability-based memory
- `BAREWire.Memory.*` - Region, View, SafeMemory
- `BAREWire.Core.Capability` - Lifetime markers

## Core Concepts

BAREWire provides four interconnected capabilities:

### 1. Schema Definition

Define binary data structures with a type-safe DSL:

```fsharp
open BAREWire.Schema.DSL

let messageSchema =
    schema "Message"
    |> withType "UserId" string
    |> withType "Timestamp" int64
    |> withType "Content" string
    |> withType "Message" (struct' [
        field "sender" (userType "UserId")
        field "timestamp" (userType "Timestamp")
        field "content" (userType "Content")
    ])
    |> validate
    |> Result.get
```

### 2. Binary Encoding/Decoding

Convert between F# values and compact binary representations:

```fsharp
open BAREWire.Encoding.Codec

type Message = {
    Sender: string
    Timestamp: int64
    Content: string
}

// Encode to bytes
let encoded = encode messageSchema message buffer

// Decode from bytes
let decoded = decode<Message> messageSchema memory
```

### 3. Memory Mapping

Access structured data in memory without copying:

```fsharp
open BAREWire.Memory.Region
open BAREWire.Memory.View

// Create a typed memory region
let region = create<Message, heap> data

// Create a view for field access
let view = View.create<Message, heap> region messageSchema

// Read fields directly from memory
let sender = View.getField<Message, string, heap> view ["sender"]
```

### 4. Inter-Process Communication

Share typed data between processes:

```fsharp
open BAREWire.IPC.SharedMemory

// Process 1: Create shared region
let shared = create<Message> "channel" size messageSchema

// Process 2: Open existing region
let received = open'<Message> "channel" messageSchema
```

## Type Safety with Units of Measure

BAREWire uses F#'s units of measure and FSharp.UMX phantom types to prevent memory errors at compile time:

```fsharp
open FSharp.UMX
open BAREWire.Core

// Memory regions are typed
[<Measure>] type heap
[<Measure>] type stack
[<Measure>] type shared

// Offsets and sizes are dimensioned
let offset = 16<offset>
let size = 1024<bytes>

// Type system prevents mixing incompatible memory
let heapMem: Memory<Message, heap> = ...
let stackMem: Memory<Message, stack> = ...
// Cannot accidentally mix these - compile error!
```

## Hardware Integration

For embedded targets, BAREWire provides **Peripheral Descriptors** that capture memory-mapped hardware layouts:

```fsharp
type PeripheralDescriptor = {
    Name: string                          // "GPIO"
    Instances: Map<string, unativeint>    // GPIOA → 0x48000000
    Layout: PeripheralLayout
    MemoryRegion: MemoryRegionKind        // Peripheral | SRAM | Flash
}

type FieldDescriptor = {
    Name: string                          // "ODR", "BSRR"
    Offset: int                           // Byte offset from base
    Type: RegisterType
    Access: AccessKind                    // ReadOnly | WriteOnly | ReadWrite
}
```

Farscape generates these descriptors from C/C++ headers (like CMSIS HAL), and Composer's Alex component uses them to emit correct memory-mapped access code with proper volatile semantics.

## Schema Compatibility

BAREWire includes tools for evolving schemas safely:

```fsharp
open BAREWire.Schema.Analysis

match checkCompatibility oldSchema newSchema with
| FullyCompatible ->
    printfn "Schemas are fully compatible"
| BackwardCompatible ->
    printfn "New schema can read old data"
| ForwardCompatible ->
    printfn "Old schema can read new data"
| Incompatible reasons ->
    printfn "Breaking changes: %A" reasons
```

## Architecture

```
src/
├── Core/           # Fundamental types and operations
│   ├── Binary.fs   # Binary conversion (Dual-target ✓)
│   ├── Memory.fs   # Buffer (Dual-target ✓) + Memory<'T,'region> (native)
│   ├── Types.fs    # Core type definitions and measures
│   ├── Utf8.fs     # UTF-8 encoding/decoding (Dual-target ✓)
│   └── Capability.fs # Lifetime markers (native only)
├── Encoding/       # BARE protocol implementation (Dual-target ✓)
│   ├── Codec.fs    # Combined encode/decode
│   ├── Decoder.fs  # Decoding primitives
│   └── Encoder.fs  # Encoding primitives
├── Memory/         # Memory mapping and views (native only)
│   ├── Region.fs   # Memory region operations
│   ├── View.fs     # Typed field access
│   └── Mapping.fs  # Memory mapping functions
├── IPC/            # Inter-process communication (native only)
│   ├── SharedMemory.fs  # Shared memory regions
│   ├── MessageQueue.fs  # Message queues
│   └── NamedPipe.fs     # Named pipes
├── Network/        # Network protocol support
│   ├── Protocol.fs # Message passing primitives
│   ├── Transport.fs # Transport abstractions
│   └── Frame.fs    # Wire frame format
└── Schema/         # Schema definition and validation
    ├── DSL.fs      # Schema definition language
    ├── Definition.fs # Schema type definitions
    ├── Validation.fs # Schema validation
    └── Analysis.fs   # Compatibility checking
```

**Legend:** Modules marked "(Dual-target ✓)" work on both the native and Fable targets. All others are native-only.

## Performance

BAREWire is designed for high-performance scenarios:

- **Zero-copy**: Read structured data directly from memory/network buffers
- **No allocations**: Encoding/decoding can work with pre-allocated buffers
- **Compact format**: BARE encoding is smaller than JSON, MessagePack, or Protobuf
- **Type-safe without overhead**: All safety checks happen at compile time

## Installation

For .NET projects:
```bash
dotnet add package BAREWire
```

For Fidelity/Composer projects, BAREWire is included as source files via the project configuration.

## Development Status

BAREWire is under active development. Current focus:

- Core encoding/decoding for primitive and composite types
- Memory region abstractions
- Shared memory IPC
- Peripheral descriptor support for embedded targets

## License

BAREWire is dual-licensed under both the Apache License 2.0 and a Commercial License.

### Open Source License

For open source projects, academic use, non-commercial applications, and internal tools, use BAREWire under the **Apache License 2.0**.

### Commercial License

A Commercial License is required for incorporating BAREWire into commercial products or services. See [Commercial.md](Commercial.md) for details.

### Patent Notice

BAREWire includes technology covered by U.S. Patent Application No. 63/786,247 "System and Method for Zero-Copy Inter-Process Communication Using BARE Protocol". See [PATENTS.md](PATENTS.md) for licensing details.

## Contributing

Contributions are welcome! By submitting a pull request, you agree to license your contributions under the same dual license terms.

## Acknowledgments

- **[BARE Protocol](https://baremessages.org/)**: The binary encoding specification
- **[FSharp.UMX](https://github.com/fsprojects/FSharp.UMX)**: Phantom types for units of measure
- **Composer Team**: For the native compilation infrastructure
