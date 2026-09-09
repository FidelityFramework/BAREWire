# BAREWire Documentation

## Overview

BAREWire supplies shared contract and representation glue for memory layout, IPC, and network communication. Declarations connect source meaning to the byte layout used by each participant. Type, schema, dimension, and proof metadata remain in PSG/codata until their compilation role is fulfilled; the final lowered payload is untagged with respect to that metadata. Application case indices, presence bits, and framing serve their declared encoding roles. [Substrate_Formalism](./Substrate_Formalism.md) explains the layers and the two missions:

- **Primary.** Shared contracts between Clef components and platform declarations for memory spaces, buffers, boundary surfaces, and transports ([11 Platform Description](./11%20Platform%20Description.md)).
- **Secondary.** Bind those contracts across JavaScript, .NET, Rust, C, and C++: source-shared where one protocol source can compile under the participating compilers, schema-shared where a BARE schema is the interchange artifact. Implementation coverage varies by pathway.

The documents specify the system; [Implementation Status](./Implementation%20Status.md) says which parts are built; [Readiness Audit](./Readiness%20Audit.md) is the build plan; [12 Intersection Subset](./12%20Intersection%20Subset.md) is the compiler-facing rulebook the shared source follows and the gates that enforce it.

## Compiler and editor integration

[Composer's Lattice integration plan](https://github.com/FidelityFramework/Composer/blob/main/docs/Lattice_Integration.md) coordinates the planned .NET-hosted server, CCS projections, and editor acceptance gates. BAREWire supplies the declarations behind layout and boundary obligations. Lattice should present their compiler-owned results through standard LSP, alongside measured-type hover and diagnostics.

The current code-owned [obligation definitions and projections](../src/Platform/Obligations.fs) produce declaration-derived obligations, solver input, and ledger records. The [JavaScript solver gate](../tests/js/tiers.mjs) exercises that projection; [Intersection Subset §5.1](./12%20Intersection%20Subset.md#51-what-the-javascript-solver-gate-establishes) distinguishes these declaration-model checks from implementation preservation. Live graph export to Lattice, proof-verdict delivery, and edit invalidation remain integration work. The external ledger remains a scaffold for reconciling graph and lowered-artifact evidence. No BAREWire binary LSP transport is supplied by this path.

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
14. [Dispatch Regions](./13%20Dispatch%20Regions.md) — implemented spatial validators and byte guards, with the layout, access and lifetime contract extracted from HelloWayland for Ariel integration.

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
2. **IPC contracts**: Declared layouts and framing for shared-memory and messaging integration; OS-level IPC integration remains design work (see [Implementation Status](./Implementation%20Status.md))
3. **Schema System**: Wire format definitions for serialization
4. **Platform Description** *(direction)*: the declared authority on memory layout, boundary contracts, and transports for every processor — see [11 Platform Description](./11%20Platform%20Description.md)

### Related Projects

| Project | Role | Documentation |
|---------|------|---------------|
| [Clef / CCS](https://github.com/FidelityFramework/clef) | The language and its compiler services; the native type universe | [Language specification](https://github.com/FidelityFramework/clef-lang-spec) |
| [Composer](https://github.com/FidelityFramework/Composer) | Compilation pipeline and planned Lattice server host | [Lattice integration](https://github.com/FidelityFramework/Composer/blob/main/docs/Lattice_Integration.md) |
| [Farscape](https://github.com/FidelityFramework/Farscape) | C/C++ binding generator (clang-based) | [Documentation](https://github.com/FidelityFramework/Farscape/tree/main/docs) |
| [Fidelity.Platform](https://github.com/FidelityFramework/Fidelity.Platform) | Per-target platform source trees (CPU, FPGA) | [Platform description contract](./11%20Platform%20Description.md) |

## Getting Started

If you're new to BAREWire, we recommend starting with the [Architecture Overview](./00%20Architecture%20Overview.md) and proceeding through the documents in numerical order for the most coherent learning experience.

For Fidelity integration specifically, start with [Hardware Descriptors](./08%20Hardware%20Descriptors.md) after reading the Architecture Overview.

## Contributing

To contribute to this documentation, please follow the existing formatting conventions and file naming patterns. Submit pull requests for any additions or corrections.
