# Schema System

The schema system is a central component of BAREWire, providing the mechanism to define, validate, and use structured data formats. This document explains how BAREWire's schema system works and how to use it effectively.

> **Built vocabulary (2026-09-03).** The schema speaks BARE's own fixed type vocabulary, not the compiler's `NTUKind`: `PrimKind` is a string tag (`"uint"`, `"u8"`, `"str"`, `"data"`, ...), aggregates are `Optional`, `List`, `FixedList`, `Map`, `Union`, `Struct`, `TypeRef`, and every collection is an array (`src/Schema/Definition.fs`). A schema is the mapping layer of [Substrate_Formalism](./Substrate_Formalism.md) "Three layers": it is held by both endpoints at compile time and never travels on the wire. This is what a contract at a boundary needs: **Fixed** dimensions, concrete widths both sides read the same way, rather than dimensions each side resolves for itself ([10 The Case from Practice](./10%20The%20Case%20from%20Practice.md) §"What this implies", point 2; `clef-lang-spec` §7.3). The mapping from a Clef type to this vocabulary is the compiler's job, the `[<BAREWireSchema>]` consumer of Readiness Audit §4 step 8, not the library's. Validation returns an array of `{ Kind; Location }` errors, empty when valid, rather than a `Result` (docs/12); it refuses a type defined in terms of itself by any route, as BARE does, and a negative tag, a duplicate case type, and duplicate enum names or values. An enum is encoded as `uint` whatever its declared base, so `Analysis.wireSize` sizes it as a varint and `Emit` writes `enum { ... }` with no base; `EnumSpec.Base` bounds the values only. The samples below keep the earlier `Map`-based shapes for the design they illustrate; the built API is in `src/Schema/`.

> **Extent integrity (2026-09-06).** `Analysis.wireSize` and `isFixedWidth` now return `Result<_, ValidationError array>` after validating the schema and the type expression. `packedOffsets` returns `Result<FieldExtent array option, ValidationError array>`: `Ok None` is a valid data-dependent layout, while errors include unresolved references and hosted extent overflow. Invalid dimensions never supply zero-sized, empty, or unbounded allocation facts. The concrete API in `src/Schema/Analysis.fs` governs the earlier design examples below.


## The two contract modes and the interchange artifact

A schema has two jobs, and the Readiness Audit ranks them.

**Source-shared.** One protocol file, compiled by Fable, .NET, and Composer from the same source, is the contract: the types themselves. The codec for each type is a pair of module functions in the Encoding tier's shapes (`write: byte array -> int -> 'a -> int`, `read: byte array -> int -> 'a * int`), written beside the consumer today (WrenHello `Codec.fs`, Conclave `Codecs.fs`) and generated from the type by the compiler later. The schema *value* is derived from the type when a program needs it, for the epoch handshake and for compatibility checks. This mode is designed first and firmest.

**Schema-shared.** Where source cannot be shared, the `.bare` schema text is the interchange artifact. `Emit.schema` (`src/Schema/Emit.fs`) writes a `SchemaDefinition` in the BARE schema language, deterministically, in declaration order:

```text
type Message struct {
  id: str
  sender: str
  timestamp: int
  attachment: optional<data>
  type: MessageType
}
type MessageType enum { TEXT = 0 IMAGE = 1 VIDEO = 2 FILE = 3 }
```

Each language's codec is generated from that text by its own generator (the BARE ecosystem has them for Go, Rust, C, JavaScript, and others), and the frames it produces are byte-identical to the source-shared codec's for the same values, because both follow the one wire format of [02](./02%20Encoding%20and%20Decoding%20Engine.md) and the one envelope of [05](./05%20Network%20Protocol.md). The schema-shared mode therefore degrades to the same bytes; a golden-frame corpus (`tests/EncodingTests.fs`) is the check that it does.

A second serialization in this mode is BTF, the BPF Type Format the Linux kernel reads for map definitions and relocation. Where `.bare` text carries the packed wire vocabulary, BTF carries C natural layout with member offsets in bits, which is what `Hardware.Validator.derive` computes; `Hardware/Btf.fs` writes the type and string tables from `StructDescriptor` arrays and reads them back (docs/11 §"The kernel as a described platform").

Not yet built in this mode: a parser from `.bare` text back to `SchemaDefinition`, and a generator that writes the Clef-side per-type module functions from a schema (Readiness Audit §4 step 14).

## Schema Definition

BAREWire schemas define the structure of binary data using a type-safe approach:

```fsharp
/// Module containing schema definition types and functions
module Schema =
    /// Create a new draft schema
    let create (rootTypeName: string): SchemaDefinition<draft> =
        { Types = Map.empty; Root = rootTypeName }
    
    /// Add a primitive type to a schema
    let addPrimitive 
        (name: string) 
        (primitiveType: PrimitiveType) 
        (schema: SchemaDefinition<draft>): SchemaDefinition<draft> =
        
        let types = Map.add name (Primitive primitiveType) schema.Types
        { schema with Types = types }
    
    /// Add a struct type to a schema
    let addStruct 
        (name: string) 
        (fields: StructField list) 
        (schema: SchemaDefinition<draft>): SchemaDefinition<draft> =
        
        let types = Map.add name (Aggregate (Struct fields)) schema.Types
        { schema with Types = types }
    
    /// Add a union type to a schema
    let addUnion 
        (name: string) 
        (cases: Map<uint, Type>) 
        (schema: SchemaDefinition<draft>): SchemaDefinition<draft> =
        
        let types = Map.add name (Aggregate (Union cases)) schema.Types
        { schema with Types = types }
    
    /// Add a list type to a schema
    let addList 
        (name: string) 
        (itemType: Type) 
        (schema: SchemaDefinition<draft>): SchemaDefinition<draft> =
        
        let types = Map.add name (Aggregate (List itemType)) schema.Types
        { schema with Types = types }
    
    /// Add a map type to a schema
    let addMap 
        (name: string) 
        (keyType: Type) 
        (valueType: Type) 
        (schema: SchemaDefinition<draft>): SchemaDefinition<draft> =
        
        let types = Map.add name (Aggregate (Map (keyType, valueType))) schema.Types
        { schema with Types = types }
```

## Schema Validation

Before a schema can be used, it must be validated to ensure it's well-formed:

```fsharp
/// Schema validation module
module SchemaValidation =
    /// Errors that can occur during schema validation
    type ValidationError =
        | CyclicTypeReference of typeName:string
        | UndefinedType of typeName:string
        | InvalidVoidUsage of location:string
        | InvalidMapKeyType of typeName:string
        | EmptyEnum
        | EmptyUnion
        | EmptyStruct
    
    /// Validate a schema
    let validate (schema: SchemaDefinition<draft>): Result<SchemaDefinition<validated>, ValidationError list> =
        // Step 1: Check that the root type exists
        if not (Map.containsKey schema.Root schema.Types) then
            Error [UndefinedType schema.Root]
        else
            // Step 2: Check for cyclic references
            let detectCycles () =
                let rec visit visited path typeName =
                    if List.contains typeName path then
                        // Cycle detected
                        Some (CyclicTypeReference typeName)
                    else if Set.contains typeName visited then
                        // Already visited, no cycle
                        None
                    else
                        match Map.tryFind typeName schema.Types with
                        | None -> Some (UndefinedType typeName)
                        | Some typ ->
                            // Check referenced types
                            let referencedTypes = getReferencedTypes typ
                            let newPath = typeName :: path
                            let newVisited = Set.add typeName visited
                            
                            Seq.tryPick (fun t -> visit newVisited newPath t) referencedTypes
                
                Map.keys schema.Types
                |> Seq.tryPick (fun typeName -> visit Set.empty [] typeName)
            
            match detectCycles () with
            | Some error -> Error [error]
            | None ->
                // Step 3: Check invariants for each type
                let invariantErrors =
                    schema.Types
                    |> Map.toSeq
                    |> Seq.collect (fun (name, typ) -> validateTypeInvariants name typ)
                    |> Seq.toList
                
                if not (List.isEmpty invariantErrors) then
                    Error invariantErrors
                else
                    // All checks passed
                    Ok (unbox<SchemaDefinition<validated>> schema)
    
    /// Get types referenced by a type
    let private getReferencedTypes (typ: Type): string seq =
        match typ with
        | Primitive _ -> Seq.empty
        | UserDefined name -> Seq.singleton name
        | Aggregate agg ->
            match agg with
            | Optional innerType -> getReferencedTypes innerType
            | List innerType -> getReferencedTypes innerType
            | FixedList (innerType, _) -> getReferencedTypes innerType
            | Map (keyType, valueType) -> 
                Seq.append (getReferencedTypes keyType) (getReferencedTypes valueType)
            | Union cases -> 
                cases |> Map.values |> Seq.collect getReferencedTypes
            | Struct fields -> 
                fields |> Seq.collect (fun f -> getReferencedTypes f.Type)
    
    /// Validate invariants for a type
    let private validateTypeInvariants (typeName: string) (typ: Type): ValidationError seq =
        let rec validateType path t =
            match t with
            | Primitive (Void) ->
                // Void can only be used in a union
                if not (isUnionContext path) then
                    seq { yield InvalidVoidUsage (typePathToString path) }
                else
                    Seq.empty
                    
            | Primitive (Enum values) ->
                // Enum must have at least one value
                if Map.isEmpty values then
                    seq { yield EmptyEnum }
                else
                    Seq.empty
            
            | Aggregate (Union cases) ->
                // Union must have at least one case
                if Map.isEmpty cases then
                    seq { yield EmptyUnion }
                else
                    cases |> Map.values |> Seq.collect (validateType (UnionCase :: path))
            
            | Aggregate (Struct fields) ->
                // Struct must have at least one field
                if List.isEmpty fields then
                    seq { yield EmptyStruct }
                else
                    fields |> Seq.collect (fun f -> validateType (StructField f.Name :: path) f.Type)
            
            | Aggregate (Map (keyType, valueType)) ->
                // Map keys must be primitive and not f32, f64, data, void
                let keyErrors =
                    match keyType with
                    | Primitive (F32) 
                    | Primitive (F64)
                    | Primitive (Data)
                    | Primitive (FixedData _)
                    | Primitive (Void) ->
                        seq { yield InvalidMapKeyType (typeToString keyType) }
                    | Primitive _ -> Seq.empty
                    | _ -> 
                        validateType (MapKey :: path) keyType
                
                let valueErrors = validateType (MapValue :: path) valueType
                Seq.append keyErrors valueErrors
            
            | Aggregate (Optional innerType) ->
                validateType (OptionalValue :: path) innerType
            
            | Aggregate (List innerType) ->
                validateType (ListItem :: path) innerType
            
            | Aggregate (FixedList (innerType, length)) ->
                if length <= 0 then
                    seq { yield InvalidFixedLength (length, typePathToString path) }
                else
                    validateType (ListItem :: path) innerType
            
            | _ -> Seq.empty
        
        validateType [TypeRoot typeName] typ
```

## Schema DSL

For convenient schema definition, BAREWire provides a domain-specific language (DSL):

```fsharp
/// Schema definition DSL
module SchemaDSL =
    /// Start building a schema
    let schema rootName = Schema.create rootName
    
    /// Define a BARE uint type
    let uint = Primitive UInt
    
    /// Define a BARE int type
    let int = Primitive Int
    
    /// Define a BARE string type
    let string = Primitive String
    
    /// Define a BARE data type
    let data = Primitive Data
    
    /// Define a BARE fixed data type
    let fixedData length = Primitive (FixedData length)
    
    /// Define a BARE bool type
    let bool = Primitive Bool
    
    /// Define a BARE void type
    let void = Primitive Void
    
    /// Define a BARE f32 type
    let f32 = Primitive F32
    
    /// Define a BARE f64 type
    let f64 = Primitive F64
    
    /// Define a BARE optional type
    let optional typ = Aggregate (Optional typ)
    
    /// Define a BARE list type
    let list typ = Aggregate (List typ)
    
    /// Define a BARE fixed-length list type
    let fixedList typ length = Aggregate (FixedList (typ, length))
    
    /// Define a BARE map type
    let map keyType valueType = Aggregate (Map (keyType, valueType))
    
    /// Create a field for a struct
    let field name typ = { Name = name; Type = typ }
    
    /// Define a BARE struct type
    let struct' fields = Aggregate (Struct fields)
    
    /// Define a BARE union type
    let union cases = Aggregate (Union cases)
    
    /// Define a BARE enum type
    let enum values = Primitive (Enum values)
    
    /// Reference a user-defined type
    let userType name = UserDefined name
    
    /// Add a type to a schema
    let withType name typ schema =
        { schema with Types = Map.add name typ schema.Types }
```

## Schema Usage Example

Here's how to define and use a schema for a simple messaging system:

```fsharp
// Define a messaging schema
let messagingSchema =
    schema "Message"
    |> withType "UserId" string
    |> withType "MessageId" string
    |> withType "Timestamp" int
    |> withType "MessageContent" string
    |> withType "Attachment" (optional data)
    |> withType "MessageType" (enum (Map.ofList [
        "TEXT", 0UL
        "IMAGE", 1UL
        "VIDEO", 2UL
        "FILE", 3UL
    ]))
    |> withType "Message" (struct' [
        field "id" (userType "MessageId")
        field "sender" (userType "UserId")
        field "timestamp" (userType "Timestamp")
        field "content" (userType "MessageContent")
        field "attachment" (userType "Attachment")
        field "type" (userType "MessageType")
    ])
    |> withType "MessageList" (list (userType "Message"))

// Validate the schema
match SchemaValidation.validate messagingSchema with
| Ok validSchema ->
    // Use the schema for encoding/decoding
    printfn "Schema is valid"
| Error errors ->
    // Handle validation errors
    for error in errors do
        printfn "Schema error: %A" error
```

## Schema Versioning

BAREWire provides support for schema versioning to maintain compatibility:

```fsharp
/// Schema versioning module
module SchemaVersioning =
    /// Compatibility level between schemas
    type Compatibility =
        | FullyCompatible
        | BackwardCompatible
        | ForwardCompatible
        | Incompatible of reasons:string list
    
    /// Check if two schemas are compatible
    let checkCompatibility 
        (oldSchema: SchemaDefinition<validated>) 
        (newSchema: SchemaDefinition<validated>): Compatibility =
        
        // Two schemas are fully compatible if they have the same structure
        // They are backward compatible if new schema can read old data
        // They are forward compatible if old schema can read new data
        
        // For a full implementation, we would need detailed comparison
        // of all types and their relationships
        
        let checkRootCompatibility () =
            let oldRoot = Map.find oldSchema.Root oldSchema.Types
            let newRoot = Map.find newSchema.Root newSchema.Types
            
            match oldRoot, newRoot with
            | Aggregate (Union oldCases), Aggregate (Union newCases) ->
                // A union is backward compatible if all old cases exist in the new schema
                let allOldCasesExist =
                    oldCases
                    |> Map.forall (fun oldTag oldType ->
                        match Map.tryFind oldTag newCases with
                        | Some newType -> areTypesCompatible oldType newType
                        | None -> false)
                
                // A union is forward compatible if all new cases exist in the old schema
                let allNewCasesExist =
                    newCases
                    |> Map.forall (fun newTag newType ->
                        match Map.tryFind newTag oldCases with
                        | Some oldType -> areTypesCompatible oldType newType
                        | None -> false)
                
                match allOldCasesExist, allNewCasesExist with
                | true, true -> FullyCompatible
                | true, false -> BackwardCompatible
                | false, true -> ForwardCompatible
                | false, false -> Incompatible ["Incompatible union types"]
            
            | Aggregate (Struct oldFields), Aggregate (Struct newFields) ->
                // A struct is backward compatible if all old fields exist in the new schema
                // in the same order
                let allOldFieldsExist =
                    List.forall2 (fun (oldField: StructField) (newField: StructField) ->
                        oldField.Name = newField.Name && 
                        areTypesCompatible oldField.Type newField.Type) 
                        oldFields newFields
                
                if allOldFieldsExist then
                    if List.length oldFields = List.length newFields then
                        FullyCompatible
                    else
                        BackwardCompatible
                else
                    Incompatible ["Incompatible struct types"]
            
            | _ ->
                // For other types, we require exact equality
                if oldRoot = newRoot then
                    FullyCompatible
                else
                    Incompatible ["Root types are different"]
        
        checkRootCompatibility ()
    
    /// Check if two types are compatible
    let private areTypesCompatible (oldType: Type) (newType: Type): bool =
        // This would be a recursive function that checks if two types
        // are compatible according to BARE's compatibility rules
        oldType = newType // Simplified for this example
```

## Schema Analysis

BAREWire provides tools for analyzing schemas to determine properties like size and alignment:

```fsharp
/// Schema analysis module
module SchemaAnalysis =
    /// Get the size information for a type
    let getTypeSize (schema: SchemaDefinition<validated>) (typ: Type): Size =
        let rec calcSize t =
            match t with
            | Primitive primType ->
                match primType with
                | UInt -> { Min = 1<bytes>; Max = Some 10<bytes>; IsFixed = false }
                | Int -> { Min = 1<bytes>; Max = Some 10<bytes>; IsFixed = false }
                | U8 -> { Min = 1<bytes>; Max = Some 1<bytes>; IsFixed = true }
                | U16 -> { Min = 2<bytes>; Max = Some 2<bytes>; IsFixed = true }
                | U32 -> { Min = 4<bytes>; Max = Some 4<bytes>; IsFixed = true }
                | U64 -> { Min = 8<bytes>; Max = Some 8<bytes>; IsFixed = true }
                | I8 -> { Min = 1<bytes>; Max = Some 1<bytes>; IsFixed = true }
                | I16 -> { Min = 2<bytes>; Max = Some 2<bytes>; IsFixed = true }
                | I32 -> { Min = 4<bytes>; Max = Some 4<bytes>; IsFixed = true }
                | I64 -> { Min = 8<bytes>; Max = Some 8<bytes>; IsFixed = true }
                | F32 -> { Min = 4<bytes>; Max = Some 4<bytes>; IsFixed = true }
                | F64 -> { Min = 8<bytes>; Max = Some 8<bytes>; IsFixed = true }
                | Bool -> { Min = 1<bytes>; Max = Some 1<bytes>; IsFixed = true }
                | String -> { Min = 1<bytes>; Max = None; IsFixed = false }
                | Data -> { Min = 1<bytes>; Max = None; IsFixed = false }
                | FixedData length -> 
                    { Min = length * 1<bytes>; Max = Some (length * 1<bytes>); IsFixed = true }
                | Void -> { Min = 0<bytes>; Max = Some 0<bytes>; IsFixed = true }
                | Enum _ -> { Min = 1<bytes>; Max = Some 10<bytes>; IsFixed = false }
            
            | Aggregate aggType ->
                match aggType with
                | Optional innerType ->
                    let innerSize = calcSize innerType
                    { 
                        Min = 1<bytes>
                        Max = innerSize.Max |> Option.map (fun m -> m + 1<bytes>)
                        IsFixed = false
                    }
                
                | List innerType ->
                    { Min = 1<bytes>; Max = None; IsFixed = false }
                
                | FixedList (innerType, length) ->
                    let innerSize = calcSize innerType
                    if innerSize.IsFixed then
                        let totalSize = innerSize.Min * length
                        { Min = totalSize; Max = Some totalSize; IsFixed = true }
                    else
                        { Min = innerSize.Min * length; Max = None; IsFixed = false }
                
                | Map (keyType, valueType) ->
                    { Min = 1<bytes>; Max = None; IsFixed = false }
                
                | Union cases ->
                    let caseSizes = 
                        cases 
                        |> Map.values 
                        |> Seq.map calcSize
                    
                    let minSize = 1<bytes> + (Seq.minBy (fun s -> s.Min) caseSizes).Min
                    let maxSize = 
                        Seq.fold (fun acc size -> 
                            match acc, size.Max with
                            | None, _ -> None
                            | _, None -> None
                            | Some accMax, Some sizeMax -> Some (accMax + sizeMax)
                        ) (Some 10<bytes>) caseSizes
                    
                    { Min = minSize; Max = maxSize; IsFixed = false }
                
                | Struct fields ->
                    let fieldSizes = fields |> List.map (fun f -> calcSize f.Type)
                    
                    let totalMinSize = 
                        fieldSizes 
                        |> List.sumBy (fun s -> s.Min)
                    
                    let totalMaxSize = 
                        fieldSizes 
                        |> List.fold (fun acc size -> 
                            match acc, size.Max with
                            | None, _ -> None
                            | _, None -> None
                            | Some accMax, Some sizeMax -> Some (accMax + sizeMax)
                        ) (Some 0<bytes>)
                    
                    let isFixed = 
                        fieldSizes 
                        |> List.forall (fun s -> s.IsFixed)
                    
                    { Min = totalMinSize; Max = totalMaxSize; IsFixed = isFixed }
            
            | UserDefined typeName ->
                // Look up the type in the schema
                match Map.tryFind typeName schema.Types with
                | Some typ -> calcSize typ
                | None -> failwith $"Type not found: {typeName}"
        
        calcSize typ
    
    /// Get the alignment requirements for a type
    let getTypeAlignment (schema: SchemaDefinition<validated>) (typ: Type): Alignment =
        let rec calcAlignment t =
            match t with
            | Primitive primType ->
                match primType with
                | UInt | Int | Bool | String | Data | Enum _ | Void -> 
                    { Value = 1<bytes> }
                | U8 | I8 -> { Value = 1<bytes> }
                | U16 | I16 -> { Value = 2<bytes> }
                | U32 | I32 | F32 -> { Value = 4<bytes> }
                | U64 | I64 | F64 -> { Value = 8<bytes> }
                | FixedData _ -> { Value = 1<bytes> }
            
            | Aggregate aggType ->
                match aggType with
                | Optional innerType ->
                    let innerAlign = calcAlignment innerType
                    { Value = max 1<bytes> innerAlign.Value }
                
                | List innerType | FixedList (innerType, _) ->
                    let innerAlign = calcAlignment innerType
                    { Value = max 1<bytes> innerAlign.Value }
                
                | Map (keyType, valueType) ->
                    let keyAlign = calcAlignment keyType
                    let valueAlign = calcAlignment valueType
                    { Value = max keyAlign.Value valueAlign.Value }
                
                | Union cases ->
                    let caseAlignments = 
                        cases 
                        |> Map.values 
                        |> Seq.map calcAlignment
                    
                    let maxAlign = 
                        caseAlignments 
                        |> Seq.map (fun a -> a.Value) 
                        |> Seq.max
                    
                    { Value = max 1<bytes> maxAlign }
                
                | Struct fields ->
                    let fieldAlignments = 
                        fields 
                        |> List.map (fun f -> calcAlignment f.Type)
                    
                    let maxAlign = 
                        fieldAlignments 
                        |> List.map (fun a -> a.Value) 
                        |> List.max
                    
                    { Value = maxAlign }
            
            | UserDefined typeName ->
                // Look up the type in the schema
                match Map.tryFind typeName schema.Types with
                | Some typ -> calcAlignment typ
                | None -> failwith $"Type not found: {typeName}"
        
        calcAlignment typ
```

The schema system plays a central role in BAREWire, providing the foundation for type-safe binary data processing. By using these schema tools, developers can define, validate, and analyze the structure of their binary data formats, enabling robust and efficient serialization and deserialization.