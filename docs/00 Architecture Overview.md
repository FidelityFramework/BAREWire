# BAREWire Architecture Overview

BAREWire is designed as a modular system with several core components that work
together to provide a comprehensive solution for binary data encoding, memory
mapping, and communication.

> **Read [Substrate_Formalism](./Substrate_Formalism.md) first.** It states what
> BAREWire is: a formalizable substrate upholding a *typed contract* across a
> boundary, not a memory layout and not a wire format. The encoding serves the
> contract, not the other way round. [10 The Case from
> Practice](./10%20The%20Case%20from%20Practice.md) supplies the evidence from
> two projects that crossed boundaries without it.

## Project Structure

Two trees are listed below, because they differ and the difference is the point.
Documents in this folder specify a system considerably larger than what is
built; treating them as a description of the repository has caused confusion
before. See [Implementation Status](./Implementation%20Status.md) for the ledger.

**What exists** (`src/`, and what `BAREWire.fidproj` compiles):

```text
BAREWire/src/
├── Hardware/
│   └── Descriptors.fs     # Peripheral, field, bitfield and StructDescriptor types
├── Encoding/
│   ├── Memory.fs          # Memory representation and operations
│   ├── Encoder.fs         # Encoding primitives
│   ├── Decoder.fs         # Decoding primitives
│   └── Codec.fs           # Combined encoding/decoding operations
└── Schema/
    ├── Definition.fs      # Schema type definitions
    ├── Validation.fs      # Schema validation logic
    ├── Analysis.fs        # Schema analysis tools
    └── DSL.fs             # Domain-specific language for schema definition
```

**What the documents specify** — design only, no implementation:

```text
├── Memory/                # 04 Memory Mapping — Region, View, address calculation
├── Network/               # 05 Network Protocol — Frame, Transport, Protocol
├── IPC/                   # 06/07 IPC — shared memory, queues, pipes
└── (tier modules)         # BAREWire.HSA / .CXL / .RDMA — the three scales
```

A `Core/` module is referenced by the orphaned test project and by older
documents. It does not exist; error and core types moved out during the
compiler-services migration.

## Core Components

```mermaid
%%{init: {'theme': 'dark', 'themeVariables': { 'primaryColor': '#242424', 'primaryTextColor': '#fff', 'primaryBorderColor': '#888', 'lineColor': '#d3d3d3', 'secondaryColor': '#2b2b2b', 'tertiaryColor': '#333' }}}%%
flowchart TB
    subgraph Core["Core Components"]
        Types["Type System"]
        Encoding["Encoding Engine"]
        Decoding["Decoding Engine"]
        Schema["Schema Definitions"]
    end
    
    subgraph Features["Domain-Specific Features"]
        Memory["Memory Mapping"]
        Network["Network Protocol"]
        IPC["Inter-Process Communication"]
    end
    
    Core --> Features
    
    subgraph Integration["Integration Points"]
        NTU["Native Type Universe<br>Dimensional Measures"]
        Farscape["Farscape<br>C/C++ Bindings"]
        Fidelity["Fidelity Framework"]
        XParsec["XParsec<br>Schema Parsing"]
    end
    
    Features --> Integration
    
    style Core fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
    style Features fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
    style Integration fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
```

## Design Principles

1. **Zero Dependencies**: BAREWire is implemented with no external dependencies, making it suitable for constrained environments.

2. **Type Safety**: compile-time safety through the native type universe's dimensional measures — offset, byte, count, and region distinctions carried in the types themselves (see [01 Core Types and Measures](./01%20Core%20Types%20and%20Measures.md)).

3. **Performance First**: All operations are optimized for high performance with minimal allocations and efficient memory usage.

4. **Composability**: Components are designed to be composable, allowing developers to use only the parts they need.

5. **Boundary Reach**: the substrate discipline extends wherever a TCB can be articulated — native targets through the Fidelity Framework, and the type-erasure boundary of the JavaScript/WREN path ([Substrate_Formalism](./Substrate_Formalism.md), §"two boundaries").

## Layered Architecture

BAREWire follows a layered architecture:

```mermaid
%%{init: {'theme': 'dark', 'themeVariables': { 'primaryColor': '#242424', 'primaryTextColor': '#fff', 'primaryBorderColor': '#888', 'lineColor': '#d3d3d3', 'secondaryColor': '#2b2b2b', 'tertiaryColor': '#333' }}}%%
flowchart TB
    L1["Layer 1: Core Types and Measures"]
    L2["Layer 2: Encoding/Decoding Primitives"]
    L3["Layer 3: Schema Definition and Validation"]
    L4["Layer 4: Domain-Specific Features"]
    L5["Layer 5: Integration Points"]
    
    L1 --> L2
    L2 --> L3
    L3 --> L4
    L4 --> L5
    
    style L1 fill:#4a5568,stroke:#fff,stroke-width:1px,color:#fff
    style L2 fill:#6b7280,stroke:#fff,stroke-width:1px,color:#fff
    style L3 fill:#4b5563,stroke:#fff,stroke-width:1px,color:#fff
    style L4 fill:#374151,stroke:#fff,stroke-width:1px,color:#fff
    style L5 fill:#1f2937,stroke:#fff,stroke-width:1px,color:#fff
```

### Layer 1: Core Types and Measures

The foundation of BAREWire consists of core types and units of measure that define the binary format and ensure type safety.

### Layer 2: Encoding/Decoding Primitives

This layer provides the fundamental operations for encoding and decoding primitive BARE types.

### Layer 3: Schema Definition and Validation

Schemas define the structure of BARE messages and provide validation during encoding and decoding.

### Layer 4: Domain-Specific Features

This layer implements domain-specific features like memory mapping, network protocols, and IPC.

### Layer 5: Integration Points

The topmost layer provides integration points with other systems like Farscape, the Fidelity Framework, and application code.

## Memory Management

BAREWire uses a zero-copy approach whenever possible to minimize allocations and copies:

```mermaid
%%{init: {'theme': 'dark', 'themeVariables': { 'primaryColor': '#242424', 'primaryTextColor': '#fff', 'primaryBorderColor': '#888', 'lineColor': '#d3d3d3', 'secondaryColor': '#2b2b2b', 'tertiaryColor': '#333' }}}%%
flowchart LR
    subgraph Source["Source Data"]
        SourceMem["Memory Region"]
    end
    
    subgraph BAREWire["BAREWire Layer"]
        View["Memory View<br>Zero-Copy Access"]
    end
    
    subgraph Target["Target Application"]
        TargetAccess["Direct Access<br>No Copy"]
    end
    
    SourceMem --> View
    View --> TargetAccess
    
    style Source fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
    style BAREWire fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
    style Target fill:#333,stroke:#aaa,stroke-width:2px,color:#fff
```

This architecture enables efficient processing of large binary data structures without unnecessary memory copying.