namespace BAREWire.Schema

// =============================================================================
// BAREWire Schema Definition
//
// Schemas describe the structure of BARE messages. Types come from NTUKind.
// BAREWire does NOT define its own type system - NTUKind IS the type system.
// =============================================================================

/// How a value is encoded on the BARE wire format
[<RequireQualifiedAccess>]
type WireEncoding =
    /// Fixed-width encoding (natural size from NTU)
    | Fixed
    /// Variable-length integer encoding (LEB128)
    | VarInt
    /// Length-prefixed encoding (for strings, byte arrays)
    | LengthPrefixed

/// A schema type - NTUKind primitives or aggregate structures
type SchemaType =
    /// An NTU type with wire encoding strategy
    | NTU of kind: NTUKind * encoding: WireEncoding
    /// Fixed-length byte array
    | FixedData of length: int
    /// Enum with named values over an NTU integer type
    | Enum of baseKind: NTUKind * values: Map<string, uint64>
    /// Aggregate/composite type
    | Aggregate of AggregateType
    /// Reference to a named type in the schema
    | TypeRef of name: string

/// A field in a BARE struct
and [<Struct>] StructField = {
    Name: string
    FieldType: SchemaType
}

/// BARE aggregate types (structural composition)
and AggregateType =
    | Optional of SchemaType
    | List of SchemaType
    | FixedList of elementType: SchemaType * length: int
    | Map of keyType: SchemaType * valueType: SchemaType
    | Union of cases: Map<uint32, SchemaType>
    | Struct of fields: StructField list

/// A complete schema definition
[<Struct>]
type SchemaDefinition = {
    /// Named types in this schema
    Types: Map<string, SchemaType>
    /// The root type name
    Root: string
}

/// Size information (derived from NTU + encoding)
[<Struct>]
type Size = {
    Min: int
    Max: int option
    IsFixed: bool
}

/// Alignment information
[<Struct>]
type Alignment = {
    Value: int
}

// =============================================================================
// Schema Construction
// =============================================================================

module Schema =
    /// Create an empty schema with a root type name
    let create (rootTypeName: string) : SchemaDefinition =
        { Types = Map.empty; Root = rootTypeName }

    /// Add a type to the schema
    let addType (name: string) (schemaType: SchemaType) (schema: SchemaDefinition) : SchemaDefinition =
        { schema with Types = Map.add name schemaType schema.Types }

    /// Get a type from the schema
    let getType (name: string) (schema: SchemaDefinition) : SchemaType option =
        Map.tryFind name schema.Types

    /// Check if a type exists
    let hasType (name: string) (schema: SchemaDefinition) : bool =
        Map.containsKey name schema.Types

    /// Set the root type
    let setRoot (rootTypeName: string) (schema: SchemaDefinition) : SchemaDefinition =
        { schema with Root = rootTypeName }

    /// Get all type names
    let typeNames (schema: SchemaDefinition) : string list =
        schema.Types |> Map.keys

// =============================================================================
// NTU Type Constructors for BARE wire format
// =============================================================================

module Bare =
    // Fixed-width integers
    let u8 = SchemaType.NTU(NTUKind.NTUuint8, WireEncoding.Fixed)
    let u16 = SchemaType.NTU(NTUKind.NTUuint16, WireEncoding.Fixed)
    let u32 = SchemaType.NTU(NTUKind.NTUuint32, WireEncoding.Fixed)
    let u64 = SchemaType.NTU(NTUKind.NTUuint64, WireEncoding.Fixed)
    let i8 = SchemaType.NTU(NTUKind.NTUint8, WireEncoding.Fixed)
    let i16 = SchemaType.NTU(NTUKind.NTUint16, WireEncoding.Fixed)
    let i32 = SchemaType.NTU(NTUKind.NTUint32, WireEncoding.Fixed)
    let i64 = SchemaType.NTU(NTUKind.NTUint64, WireEncoding.Fixed)

    // Floating point
    let f32 = SchemaType.NTU(NTUKind.NTUfloat32, WireEncoding.Fixed)
    let f64 = SchemaType.NTU(NTUKind.NTUfloat64, WireEncoding.Fixed)

    // Other fixed types
    let bool = SchemaType.NTU(NTUKind.NTUbool, WireEncoding.Fixed)
    let void = SchemaType.NTU(NTUKind.NTUunit, WireEncoding.Fixed)

    // Variable-length integers (varint encoding)
    let uint = SchemaType.NTU(NTUKind.NTUuint64, WireEncoding.VarInt)
    let int = SchemaType.NTU(NTUKind.NTUint64, WireEncoding.VarInt)

    // Length-prefixed types
    let string = SchemaType.NTU(NTUKind.NTUstring, WireEncoding.LengthPrefixed)
    let data = SchemaType.NTU(NTUKind.NTUuint8, WireEncoding.LengthPrefixed)

    // Fixed-length data
    let fixedData length = SchemaType.FixedData length

    // Enum
    let enum' baseKind values = SchemaType.Enum(baseKind, values)

    // Aggregates
    let optional t = SchemaType.Aggregate(AggregateType.Optional t)
    let list t = SchemaType.Aggregate(AggregateType.List t)
    let fixedList t len = SchemaType.Aggregate(AggregateType.FixedList(t, len))
    let map k v = SchemaType.Aggregate(AggregateType.Map(k, v))
    let union cases = SchemaType.Aggregate(AggregateType.Union cases)
    let struct' fields = SchemaType.Aggregate(AggregateType.Struct fields)

    // Type reference
    let typeRef name = SchemaType.TypeRef name

    // Field helper
    let field name fieldType : StructField = { Name = name; FieldType = fieldType }
