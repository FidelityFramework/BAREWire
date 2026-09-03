# Core Types and Measures

> **Built types (2026-09-03).** The foundational types the tiers actually share are smaller than the ones this document sketches, and the dimensional discipline lives where the document says it does, in the native type universe rather than in library wrappers. What is built: the byte model is a plain `byte array` with an `int` offset threaded through every operation and `Cursor.Fault` (`-1`) as the failure value (`src/Encoding/Cursor.fs`); a bounded extent is `Region = { Data; Offset; Length }` and a typed access path is `View = { Region; Layout }` (`src/Memory/`); the BARE type vocabulary is `PrimKind` as a string tag plus the aggregate cases of `SchemaType` (`src/Schema/Definition.fs`); wire size is `Size = { Min; Max; IsBounded; IsFixed }` and layout alignment comes from an `AbiProfile` (`src/Hardware/Abi.fs`); errors are values, an array of `{ Kind; Location }` records for a schema and a `LayoutVerdict` with `Findings` for a descriptor, never a `Result` or an exception (docs/12). The `offset`, `bytes`, and `region` measures below are the design's phantom types; the built code carries `int` and relies on the compiler's measures at the call sites that declare them, which is what the section on measures says should happen. The `Buffer<'T>` with `Write` members, `Memory<'T,'region>`, and the `Error` union are not built.

BAREWire's compile-time guarantees rest on the **native type universe (NTU)** of Clef's compiler services: a native ML-family type system in which Andrew Kennedy's units-of-measure frame is the type system's own dimensional structure. Dimensions are solved by abelian-group unification over ℤ as an extension of Hindley–Milner inference, and they persist as native metadata through MLIR lowering rather than erasing at compile time (see `~/repos/clef-lang-spec/spec/native-type-universe.md` and the DTS+DMM paper). Measures are native dimensional structure the compiler carries with the value. This document describes the foundational types used throughout the library.

## Units of Measure

Units of measure are used to create distinct types that have the same underlying representation. This creates a form of "phantom type" that provides compile-time type checking without runtime overhead.

```fsharp
/// Units of measure for BAREWire types
[<AutoOpen>]
module Measures =
    /// Marker for byte offset within a buffer
    [<Measure>] type offset

    /// Marker for byte count
    [<Measure>] type bytes

    /// Marker for a typed memory region
    [<Measure>] type region

    /// Base marker for schema types
    [<Measure>] type schema

    /// Schema validation state
    [<Measure>] type validated

    /// Schema draft state (not yet validated)
    [<Measure>] type draft
```

## Memory Representation

BAREWire uses a minimalist approach to memory representation, focusing on raw byte arrays and spans:

```fsharp
/// Core memory types
[<AutoOpen>]
module Memory =
    /// A region of memory with type safety
    type Memory<'T, [<Measure>] 'region> =
        { Data: byte[]
          Offset: int<offset>
          Length: int<bytes> }

    /// An address within a memory region
    [<Struct>]
    type Address<[<Measure>] 'region> =
        { Offset: int<offset> }
    
    /// A memory buffer that can be written to
    type Buffer<'T> =
        { Data: byte[]
          mutable Position: int<offset> }
        
        member inline this.Write(value: byte): unit =
            this.Data.[int this.Position] <- value
            this.Position <- this.Position + 1<offset>
            
        member inline this.WriteSpan(span: ReadOnlySpan<byte>): unit =
            // Write span contents to buffer
            for i = 0 to span.Length - 1 do
                this.Data.[int this.Position + i] <- span.[i]
            this.Position <- this.Position + (span.Length * 1<offset>)
```

## BARE Type System

These types correspond directly to the types defined in the BARE specification:

```fsharp
/// BARE primitive types
type PrimitiveType =
    | UInt
    | Int
    | U8
    | U16
    | U32
    | U64
    | I8
    | I16
    | I32
    | I64
    | F32
    | F64
    | Bool
    | String
    | Data
    | FixedData of length:int
    | Void
    | Enum of values:Map<string, uint64>

/// BARE aggregate types
type AggregateType =
    | Optional of Type
    | List of Type
    | FixedList of Type * length:int  
    | Map of keyType:Type * valueType:Type
    | Union of cases:Map<uint, Type>
    | Struct of fields:StructField list

/// A field in a BARE struct
and StructField = {
    Name: string
    Type: Type
}

/// A BARE type (either primitive or aggregate)
and Type =
    | Primitive of PrimitiveType
    | Aggregate of AggregateType
    | UserDefined of string

/// A schema definition with type safety
type SchemaDefinition<[<Measure>] 'state> = {
    Types: Map<string, Type>
    Root: string
}
```

## Size and Alignment

BAREWire includes types for handling memory size and alignment requirements:

```fsharp
/// Size information for a BARE type
[<Struct>]
type Size = {
    /// The minimum size in bytes
    Min: int<bytes>
    
    /// The maximum size in bytes (None if variable-length)
    Max: option<int<bytes>>
    
    /// Whether the size is fixed (Min = Max)
    IsFixed: bool
}

/// Alignment requirements for a type
[<Struct>]
type Alignment = {
    /// Alignment in bytes (must be a power of 2)
    Value: int<bytes>
}
```

## Memory Views

To support zero-copy operations, BAREWire provides "views" over memory regions:

```fsharp
/// A view over a memory region with a specific schema
type MemoryView<'T, [<Measure>] 'region> = {
    Memory: Memory<'T, 'region>
    Schema: SchemaDefinition<validated>
}

/// A memory accessor with schema-aware access
module MemoryView =
    /// Create a view over a memory region
    let create<'T, [<Measure>] 'region> 
              (memory: Memory<'T, 'region>) 
              (schema: SchemaDefinition<validated>): MemoryView<'T, 'region> =
        { Memory = memory; Schema = schema }
        
    /// Access a field within a struct memory view
    let getField<'T, [<Measure>] 'region, 'Field> 
                (view: MemoryView<'T, 'region>) 
                (fieldName: string): 'Field =
        // Implementation accesses the field based on schema
        // and returns the decoded value
        failwith "Not implemented"
```

## Error Handling

BAREWire uses a Result-based approach for error handling:

```fsharp
/// Errors that can occur during BAREWire operations
type Error =
    | SchemaValidationError of message:string
    | DecodingError of message:string
    | EncodingError of message:string
    | TypeMismatchError of expected:Type * actual:Type
    | OutOfBoundsError of offset:int<offset> * length:int<bytes>
    | InvalidValueError of message:string
```

These core types form the foundation upon which the rest of the BAREWire library is built. They provide the necessary type safety and memory management capabilities while maintaining a minimal footprint and zero external dependencies.