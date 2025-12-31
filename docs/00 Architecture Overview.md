# BAREWire Architecture Overview

BAREWire is designed as a modular system with several core components that work together to provide a comprehensive solution for binary data encoding, memory mapping, and communication.

> **Architecture Update (December 2024)**: BAREWire participates in the **quotation-based memory architecture** for the Fidelity framework.
> Farscape generates `Expr<PeripheralDescriptor>` quotations and active patterns using BAREWire types.
> See `~/repos/Firefly/docs/Quotation_Based_Memory_Architecture.md` for the unified four-component architecture.

## Project Structure 

BAREWire/
├── Core/
│   ├── Types.fs           # Core type definitions and measures
│   ├── Memory.fs          # Memory representation and operations
│   └── Error.fs           # Error handling types
├── Encoding/
│   ├── Encoder.fs         # Encoding primitives
│   ├── Decoder.fs         # Decoding primitives
│   └── Codec.fs           # Combined encoding/decoding operations
├── Schema/
│   ├── Definition.fs      # Schema type definitions
│   ├── Validation.fs      # Schema validation logic
│   ├── Analysis.fs        # Schema analysis tools
│   └── DSL.fs             # Domain-specific language for schema definition
├── Memory/
│   ├── Region.fs          # Memory region operations
│   ├── View.fs            # Memory view operations
│   └── Mapping.fs         # Memory mapping functions
├── Network/
│   ├── Frame.fs           # Frame format for binary communication
│   ├── Transport.fs       # Transport abstractions
│   └── Protocol.fs        # Message passing primitives
└── IPC/
    ├── SharedMemory.fs    # Shared memory regions
    ├── MessageQueue.fs    # Message queues
    └── NamedPipe.fs       # Named pipes

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
        UMX["FSharp.UMX<br>Type Safety"]
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

1. **Zero Dependencies**: BAREWire is implemented in pure F# with no external dependencies, making it suitable for use in constrained environments.

2. **Type Safety**: The library leverages F#'s type system, particularly through integration with FSharp.UMX, to provide compile-time type safety for serialized data.

3. **Performance First**: All operations are optimized for high performance with minimal allocations and efficient memory usage.

4. **Composability**: Components are designed to be composable, allowing developers to use only the parts they need.

5. **Cross-Platform Compatibility**: The library is designed to work across .NET, Fable, and the Fidelity Framework.

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