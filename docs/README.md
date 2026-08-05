# BAREWire Documentation

## Overview

This repository contains comprehensive documentation for the BAREWire project. Each document covers a specific aspect of the system architecture and implementation.

## Table of Contents

### Start here

- [Substrate_Formalism](./Substrate_Formalism.md) — what BAREWire *is*: a
  formalizable substrate upholding a typed contract across a boundary, argued
  from first principles.
- [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) — the same
  position argued from evidence, drawn from two projects that crossed
  boundaries without it.
- [Implementation Status](./Implementation%20Status.md) — which of the documents
  below describe code and which describe design. Most describe design.

### Core Documentation

1. [Architecture Overview](./00%20Architecture%20Overview.md)
2. [Core Types and Measures](./01%20Core%20Types%20and%20Measures.md)
3. [Encoding and Decoding Engine](./02%20Encoding%20and%20Decoding%20Engine.md)
4. [Schema System](./03%20Schema%20System.md)
5. [Memory Mapping](./04%20Memory%20Mapping.md)
6. [Network Protocol](./05%20Network%20Protocol.md)
7. [IPC Integration](./06%20IPC%20Integration.md)
8. [IPC Platform Specific APIs](./07%20IPC%20Platform%20Specific%20APIs.md)
9. [Hardware Descriptors](./08%20Hardware%20Descriptors.md) *(Fidelity Integration)*
10. [Cache-Aware Layouts](./09%20Cache-Aware%20Layouts.md)

### Design references

- [Arena Design](./Arena_Design.md) — authoritative design; implemented
  elsewhere as a compiler intrinsic.
- [Eliminating .NET Dependencies](./99%20Elminating%20Dotnet%20Dependencies.md)

## Document Structure

The documentation is organized in a sequential manner, beginning with a high-level overview and progressively diving into more specific components of the system:

- **Architecture Overview**: Provides a high-level understanding of the BAREWire system and its components
- **Core Types and Measures**: Details the fundamental data types and measurement units used throughout the system
- **Encoding and Decoding Engine**: Explains the mechanisms for transforming data between different representations
- **Schema System**: Describes how data structures are defined and validated
- **Memory Mapping**: Covers efficient memory management techniques implemented in BAREWire
- **Network Protocol**: Details the communication protocols for networked applications
- **IPC Integration**: Explains how BAREWire integrates with inter-process communication mechanisms
- **IPC Platform Specific APIs**: Platform-specific IPC implementation details
- **Hardware Descriptors**: Memory-mapped peripheral descriptors for Fidelity/Farscape integration

## Fidelity Framework Integration

BAREWire is a core component of the Fidelity native F# compilation ecosystem:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Fidelity Ecosystem                              │
│                                                                         │
│  fsnative ──provides types──▶ Farscape ──uses──▶ BAREWire              │
│     │                            │                   │                  │
│     ▼                            ▼                   ▼                  │
│  Ptr<'T,                    Parses C/C++       PeripheralDescriptor    │
│  peripheral,                headers            FieldDescriptor          │
│  readWrite>                                    AccessKind               │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

BAREWire provides:

1. **Memory Descriptors**: Type definitions for hardware memory mapping (see [Hardware Descriptors](./08%20Hardware%20Descriptors.md))
2. **Zero-Copy IPC**: Efficient inter-process communication without data copying
3. **Schema System**: Wire format definitions for serialization

### Related Projects

| Project | Role | Documentation |
|---------|------|---------------|
| **fsnative** | F# Native compiler with phantom type measures | `~/repos/fsnative/docs/fidelity/` |
| **Farscape** | C/C++ binding generator | `~/repos/Farscape/docs/` |
| **Composer** | Native compilation pipeline (formerly Firefly) | `~/repos/Composer/docs/` |
| **Alloy** | Native F# standard library | `~/repos/Alloy/` |

### Key Integration Documents

- [Memory Interlock Requirements](https://github.com/speakeztech/firefly/docs/Memory_Interlock_Requirements.md) - Dependency chain between fsnative, Farscape, and BAREWire
- [Staged Memory Model](https://github.com/speakeztech/firefly/docs/Staged_Memory_Model.md) - Fidelity's approach to deterministic memory management

## Getting Started

If you're new to BAREWire, we recommend starting with the [Architecture Overview](./00%20Architecture%20Overview.md) and proceeding through the documents in numerical order for the most coherent learning experience.

For Fidelity integration specifically, start with [Hardware Descriptors](./08%20Hardware%20Descriptors.md) after reading the Architecture Overview.

## Contributing

To contribute to this documentation, please follow the existing formatting conventions and file naming patterns. Submit pull requests for any additions or corrections.
