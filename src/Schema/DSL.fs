namespace BAREWire.Schema

/// <summary>
/// Domain-specific language for building schema definitions in a fluent style.
/// Uses NTUKind from FNCS - BAREWire defers to NTU for all types.
/// </summary>
module DSL =
    /// <summary>
    /// Starts building a schema with the specified root type name
    /// </summary>
    let schema rootName = Schema.create rootName

    // ==========================================================================
    // Fixed-width integer types
    // ==========================================================================

    /// Defines a BARE u8 type (8-bit unsigned integer)
    let u8 = Bare.u8

    /// Defines a BARE u16 type (16-bit unsigned integer)
    let u16 = Bare.u16

    /// Defines a BARE u32 type (32-bit unsigned integer)
    let u32 = Bare.u32

    /// Defines a BARE u64 type (64-bit unsigned integer)
    let u64 = Bare.u64

    /// Defines a BARE i8 type (8-bit signed integer)
    let i8 = Bare.i8

    /// Defines a BARE i16 type (16-bit signed integer)
    let i16 = Bare.i16

    /// Defines a BARE i32 type (32-bit signed integer)
    let i32 = Bare.i32

    /// Defines a BARE i64 type (64-bit signed integer)
    let i64 = Bare.i64

    // ==========================================================================
    // Floating point types
    // ==========================================================================

    /// Defines a BARE f32 type (32-bit floating point)
    let f32 = Bare.f32

    /// Defines a BARE f64 type (64-bit floating point)
    let f64 = Bare.f64

    // ==========================================================================
    // Variable-length integer types (varint encoding)
    // ==========================================================================

    /// Defines a BARE uint type (variable-length unsigned integer)
    let uint = Bare.uint

    /// Defines a BARE int type (variable-length signed integer)
    let int = Bare.int

    // ==========================================================================
    // Other primitive types
    // ==========================================================================

    /// Defines a BARE bool type (boolean value)
    let bool = Bare.bool

    /// Defines a BARE void type (no data)
    let voidType = Bare.void

    /// Defines a BARE string type (UTF-8 encoded string)
    let string = Bare.string

    /// Defines a BARE data type (variable-length byte array)
    let data = Bare.data

    /// Defines a BARE fixed data type (fixed-length byte array)
    let fixedData length = Bare.fixedData length

    // ==========================================================================
    // Enum type
    // ==========================================================================

    /// Defines a BARE enum type (named constants with numeric values)
    /// Uses NTUKind.NTUuint64 as the base type by default
    let enum values = Bare.enum' NTUKind.NTUuint64 values

    /// Defines a BARE enum type with a specific base kind
    let enumWith baseKind values = Bare.enum' baseKind values

    // ==========================================================================
    // Aggregate types
    // ==========================================================================

    /// Defines a BARE optional type (value that may be present or absent)
    let optional typ = Bare.optional typ

    /// Defines a BARE list type (variable-length array of values)
    let list typ = Bare.list typ

    /// Defines a BARE fixed-length list type (fixed-length array of values)
    let fixedList typ length = Bare.fixedList typ length

    /// Defines a BARE map type (key-value mapping)
    let map keyType valueType = Bare.map keyType valueType

    /// Defines a BARE union type (tagged variant type)
    let union cases = Bare.union cases

    /// Defines a BARE struct type (record with named fields)
    let struct' fields = Bare.struct' fields

    // ==========================================================================
    // Type references and fields
    // ==========================================================================

    /// References a user-defined type by name
    let typeRef name = Bare.typeRef name

    /// Creates a field definition for a struct
    let field name typ : StructField = Bare.field name typ

    // ==========================================================================
    // Schema construction
    // ==========================================================================

    /// Adds a type to a schema
    let withType name typ schemadef =
        Schema.addType name typ schemadef

    /// Sets the root type of a schema
    let withRoot rootName schemadef =
        Schema.setRoot rootName schemadef

    /// Validates a schema
    let validate schemadef =
        Validation.validate schemadef
