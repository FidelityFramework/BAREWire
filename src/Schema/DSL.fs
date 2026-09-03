namespace BAREWire.Schema

/// The fluent schema surface of docs/03 "Schema DSL": open the module and
/// write `schema "Message" |> withType "UserId" string |> ...`. Thin over
/// `Schema`, `Bare`, and `Validation`. Opening it shadows the `int`,
/// `string`, `bool`, and `uint` conversions with the BARE types of those
/// names; use `Bare` qualified where the conversions are needed.
module SchemaDSL =

    /// Start a schema whose root type has the given name.
    let schema (root: string) : SchemaDefinition = Schema.create root

    /// Declare a named type at the end of the schema.
    let withType (name: string) (t: SchemaType) (s: SchemaDefinition) : SchemaDefinition =
        Schema.addType name t s

    /// Change the root type name.
    let withRoot (root: string) (s: SchemaDefinition) : SchemaDefinition =
        { Types = s.Types; Root = root }

    /// Variable-width unsigned integer.
    let uint : SchemaType = Bare.uint
    /// Variable-width signed integer.
    let int : SchemaType = Bare.int
    /// Unsigned 8-bit integer.
    let u8 : SchemaType = Bare.u8
    /// Unsigned 16-bit integer.
    let u16 : SchemaType = Bare.u16
    /// Unsigned 32-bit integer.
    let u32 : SchemaType = Bare.u32
    /// Unsigned 64-bit integer.
    let u64 : SchemaType = Bare.u64
    /// Signed 8-bit integer.
    let i8 : SchemaType = Bare.i8
    /// Signed 16-bit integer.
    let i16 : SchemaType = Bare.i16
    /// Signed 32-bit integer.
    let i32 : SchemaType = Bare.i32
    /// Signed 64-bit integer.
    let i64 : SchemaType = Bare.i64
    /// IEEE-754 binary32.
    let f32 : SchemaType = Bare.f32
    /// IEEE-754 binary64.
    let f64 : SchemaType = Bare.f64
    /// Boolean.
    let bool : SchemaType = Bare.bool
    /// UTF-8 string.
    let string : SchemaType = Bare.string
    /// Length-prefixed bytes.
    let data : SchemaType = Bare.data
    /// Zero-length type, legal only as a union case.
    let void' : SchemaType = Bare.void'

    /// Exactly `n` bytes.
    let fixedData (n: int) : SchemaType = Bare.fixedData n

    /// A value that may be absent.
    let optional (t: SchemaType) : SchemaType = Bare.optional t

    /// A variable-length list.
    let list (t: SchemaType) : SchemaType = Bare.list t

    /// A list of exactly `n` elements.
    let fixedList (t: SchemaType) (n: int) : SchemaType = Bare.fixedList t n

    /// A map from keys to values.
    let map (k: SchemaType) (v: SchemaType) : SchemaType = Bare.map k v

    /// A tagged union.
    let union (cases: UnionCase array) : SchemaType = Bare.union cases

    /// A union case with an explicit tag.
    let case (tag: int) (t: SchemaType) : UnionCase = Bare.case tag t

    /// A struct with fields in wire order.
    let struct' (fields: StructField array) : SchemaType = Bare.struct' fields

    /// A struct field.
    let field (name: string) (t: SchemaType) : StructField = Bare.field name t

    /// An enum over `uint`, BARE's default base.
    let enum (values: EnumValue array) : SchemaType = Bare.enum' PrimKind.UInt values

    /// An enum over an explicit unsigned base kind.
    let enumWith (baseKind: PrimKind) (values: EnumValue array) : SchemaType = Bare.enum' baseKind values

    /// An enum value.
    let enumValue (name: string) (v: uint64) : EnumValue = Bare.enumValue name v

    /// A reference to a declared type.
    let typeRef (name: string) : SchemaType = Bare.typeRef name

    /// Validate a schema; empty means well-formed.
    let validate (s: SchemaDefinition) : ValidationError array = Validation.validate s
