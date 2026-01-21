namespace BAREWire.Schema

open FSharp.Native.Compiler.NativeTypedTree.NativeTypes

// =============================================================================
// Schema Validation - Ensures schema correctness
// =============================================================================

module Validation =

    /// Validation errors
    type ValidationError =
        | CyclicTypeReference of typeName: string
        | UndefinedType of typeName: string
        | InvalidVoidUsage of location: string
        | InvalidMapKeyType of typeName: string
        | EmptyEnum
        | EmptyUnion
        | EmptyStruct
        | InvalidFixedLength of length: int * location: string

    /// Convert validation error to string
    let errorToString error =
        match error with
        | CyclicTypeReference typeName -> $"Cyclic type reference: {typeName}"
        | UndefinedType typeName -> $"Undefined type: {typeName}"
        | InvalidVoidUsage location -> $"Invalid void usage at {location}"
        | InvalidMapKeyType typeName -> $"Invalid map key type: {typeName}"
        | EmptyEnum -> "Empty enum"
        | EmptyUnion -> "Empty union"
        | EmptyStruct -> "Empty struct"
        | InvalidFixedLength(length, location) -> $"Invalid fixed length {length} at {location}"

    /// Validation context
    type ValidationContext =
        | TypeRoot of name: string
        | StructField of name: string
        | UnionCase
        | OptionalValue
        | ListItem
        | MapKey
        | MapValue

    /// Convert context path to string
    let typePathToString (path: ValidationContext list) =
        let rec pathToString path =
            match path with
            | [] -> ""
            | [ TypeRoot name ] -> name
            | TypeRoot name :: rest -> $"{name}.{pathToString rest}"
            | StructField name :: rest -> $"{name}.{pathToString rest}"
            | UnionCase :: rest -> $"case.{pathToString rest}"
            | OptionalValue :: rest -> $"optional.{pathToString rest}"
            | ListItem :: rest -> $"item.{pathToString rest}"
            | MapKey :: rest -> $"key.{pathToString rest}"
            | MapValue :: rest -> $"value.{pathToString rest}"

        pathToString (List.rev path)

    /// Check if context is within a union
    let isUnionContext (path: ValidationContext list) =
        List.exists (function UnionCase -> true | _ -> false) path

    /// Get referenced type names from a schema type
    let rec getReferencedTypes (typ: SchemaType) : string seq =
        match typ with
        | SchemaType.NTU _ -> Seq.empty
        | SchemaType.FixedData _ -> Seq.empty
        | SchemaType.Enum _ -> Seq.empty
        | SchemaType.TypeRef name -> seq { yield name }
        | SchemaType.Aggregate agg ->
            match agg with
            | AggregateType.Optional innerType -> getReferencedTypes innerType
            | AggregateType.List innerType -> getReferencedTypes innerType
            | AggregateType.FixedList(innerType, _) -> getReferencedTypes innerType
            | AggregateType.Map(keyType, valueType) ->
                Seq.append (getReferencedTypes keyType) (getReferencedTypes valueType)
            | AggregateType.Union cases ->
                cases |> Map.values |> Seq.collect getReferencedTypes
            | AggregateType.Struct fields ->
                fields |> Seq.collect (fun f -> getReferencedTypes f.FieldType)

    /// Validate type invariants
    let validateTypeInvariants (typeName: string) (typ: SchemaType) : ValidationError seq =
        let rec validateType path t =
            match t with
            | SchemaType.NTU(NTUKind.NTUunit, _) ->
                // Void/unit can only be used in a union
                if not (isUnionContext path) then
                    seq { yield InvalidVoidUsage(typePathToString path) }
                else
                    Seq.empty

            | SchemaType.Enum(_, values) ->
                if Map.isEmpty values then seq { yield EmptyEnum } else Seq.empty

            | SchemaType.Aggregate(AggregateType.Union cases) ->
                if Map.isEmpty cases then
                    seq { yield EmptyUnion }
                else
                    cases |> Map.values |> Seq.collect (validateType (UnionCase :: path))

            | SchemaType.Aggregate(AggregateType.Struct fields) ->
                if List.isEmpty fields then
                    seq { yield EmptyStruct }
                else
                    fields |> Seq.collect (fun f -> validateType (StructField f.Name :: path) f.FieldType)

            | SchemaType.Aggregate(AggregateType.Map(keyType, valueType)) ->
                // Map keys must be valid key types
                let keyErrors =
                    match keyType with
                    | SchemaType.NTU(NTUKind.NTUfloat32, _)
                    | SchemaType.NTU(NTUKind.NTUfloat64, _)
                    | SchemaType.NTU(NTUKind.NTUunit, _)
                    | SchemaType.FixedData _ ->
                        seq { yield InvalidMapKeyType "float/void/data" }
                    | _ -> Seq.empty
                let valueErrors = validateType (MapValue :: path) valueType
                Seq.append keyErrors valueErrors

            | SchemaType.Aggregate(AggregateType.Optional innerType) ->
                validateType (OptionalValue :: path) innerType

            | SchemaType.Aggregate(AggregateType.List innerType) ->
                validateType (ListItem :: path) innerType

            | SchemaType.Aggregate(AggregateType.FixedList(innerType, length)) ->
                if length <= 0 then
                    seq { yield InvalidFixedLength(length, typePathToString path) }
                else
                    validateType (ListItem :: path) innerType

            | _ -> Seq.empty

        validateType [ TypeRoot typeName ] typ

    /// Validate a schema definition
    let validate (schema: SchemaDefinition) : Result<SchemaDefinition, ValidationError list> =
        // Check root type exists
        if not (Map.containsKey schema.Root schema.Types) then
            Error [ UndefinedType schema.Root ]
        else
            // Check for cyclic references
            let detectCycles () =
                let rec visit visited path typeName =
                    if List.contains typeName path then
                        Some(CyclicTypeReference typeName)
                    else if Set.contains typeName visited then
                        None
                    else
                        match Map.tryFind typeName schema.Types with
                        | None -> Some(UndefinedType typeName)
                        | Some typ ->
                            let referencedTypes = getReferencedTypes typ
                            let newPath = typeName :: path
                            let newVisited = Set.add typeName visited
                            Seq.tryPick (fun t -> visit newVisited newPath t) referencedTypes

                Map.keys schema.Types
                |> Seq.tryPick (fun typeName -> visit Set.empty [] typeName)

            match detectCycles () with
            | Some error -> Error [ error ]
            | None ->
                // Check invariants
                let invariantErrors =
                    schema.Types
                    |> Map.toSeq
                    |> Seq.collect (fun (name, typ) -> validateTypeInvariants name typ)
                    |> Seq.toList

                if not (List.isEmpty invariantErrors) then
                    Error invariantErrors
                else
                    Ok schema
