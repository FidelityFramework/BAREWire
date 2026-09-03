# BAREWire Documentation

## Overview

This folder documents BAREWire: the typed contract between Clef components, and the declared authority on memory layout for every processor a program runs on. Two missions, ranked ([Substrate_Formalism](./Substrate_Formalism.md), "The two missions"):

- **Primary.** A value crossing a BAREWire boundary between Clef components arrives with its case structure, payload types, and dimensional annotations intact, by construction and without tagging: the contract is a design-time fact, the bytes are its untagged image under a mapping both sides hold ([Substrate_Formalism](./Substrate_Formalism.md), "Three layers"); read inward, the same vocabulary declares each platform's memory spaces, buffer schemas, boundary surfaces, and transports for the compiler's three observers ([11](./11%20Platform%20Description.md)).
- **Secondary.** The same contract binds to JavaScript, .NET, Rust, C, and C++: source-shared where one protocol file compiles under Fable, .NET, and Composer, schema-shared where a BARE schema is the interchange artifact.

The documents specify the system; [Implementation Status](./Implementation%20Status.md) says which parts are built; [Readiness Audit](./Readiness%20Audit.md) is the build plan; [12 Intersection Subset](./12%20Intersection%20Subset.md) is the compiler-facing rulebook the shared source follows and the gates that enforce it.

## Table of Contents

### Start here

- [Substrate_Formalism](./Substrate_Formalism.md) — what BAREWire *is*: a
  formalizable substrate upholding a typed contract across a boundary, argued
  from first principles.
- [11 Platform Description](./11%20Platform%20Description.md) — the same substrate read *inward*: BAREWire as the declared authority on memory layout for every processor, generalized from what the FPGA path (HelloArty, the Fidelity.Platform FPGA articulation) was forced to build.
- [10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) — the same
  position argued from evidence, drawn from two projects that crossed
  boundaries without it.
- [Implementation Status](./Implementation%20Status.md) — which of the documents
  below describe code and which describe design.
- [Readiness Audit](./Readiness%20Audit.md) — the mission, the gaps in the
  applications, and the build order (2026-09-03).
- [12 Intersection Subset](./12%20Intersection%20Subset.md) — the rules one
  source follows to compile under Fable, .NET, and Composer, with evidence and
  the gates.

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
11. [The Case from Practice](./10%20The%20Case%20from%20Practice.md)
12. [Platform Description](./11%20Platform%20Description.md)
13. [Intersection Subset](./12%20Intersection%20Subset.md)

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

BAREWire is a core component of the Fidelity framework's native compilation
ecosystem for the Clef language:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Fidelity Ecosystem                              │
│                                                                         │
│  Clef/CCS ──native type universe──▶ Farscape ──uses──▶ BAREWire        │
│     │                                  │                   │            │
│     ▼                                  ▼                   ▼            │
│  NTU types with                 Parses C/C++        PeripheralDescriptor│
│  dimensional and                headers via clang   FieldDescriptor     │
│  pin/layout metadata            (real ABI)          AccessKind          │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

BAREWire provides:

1. **Memory Descriptors**: Type definitions for hardware memory mapping (see [Hardware Descriptors](./08%20Hardware%20Descriptors.md))
2. **Zero-Copy IPC**: Efficient inter-process communication without data copying
3. **Schema System**: Wire format definitions for serialization
4. **Platform Description** *(direction)*: the declared authority on memory layout, boundary contracts, and transports for every processor — see [11 Platform Description](./11%20Platform%20Description.md)

### Related Projects

| Project | Role | Documentation |
|---------|------|---------------|
| **Clef / CCS** | The language and its compiler services; the native type universe | `~/repos/clef`, `~/repos/clef-lang-spec` |
| **Composer** | Native compilation pipeline (formerly Firefly) | `~/repos/Composer/docs/` |
| **Farscape** | C/C++ binding generator (clang-based) | `~/repos/Farscape/docs/` |
| **Fidelity.Platform** | Per-target platform source trees (CPU, FPGA) | `~/repos/Fidelity.Platform/` |

## Getting Started

If you're new to BAREWire, we recommend starting with the [Architecture Overview](./00%20Architecture%20Overview.md) and proceeding through the documents in numerical order for the most coherent learning experience.

For Fidelity integration specifically, start with [Hardware Descriptors](./08%20Hardware%20Descriptors.md) after reading the Architecture Overview.

## Contributing

To contribute to this documentation, please follow the existing formatting conventions and file naming patterns. Submit pull requests for any additions or corrections.
