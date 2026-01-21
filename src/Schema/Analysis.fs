namespace BAREWire.Schema

// PlatformContext is compiler infrastructure for platform-specific type resolution
open FSharp.Native.Compiler.NativeTypedTree.NativeTypes

// =============================================================================
// Schema Analysis - Size and Alignment derived from NTU
//
// Size/alignment information comes from PlatformContext.resolveSize/resolveAlign.
// BAREWire defers to NTU for all type metadata.
// =============================================================================

module Analysis =

    /// Compatibility level between schemas
    type Compatibility =
        | FullyCompatible
        | BackwardCompatible
        | ForwardCompatible
        | Incompatible of reasons: string list

    /// Get wire size for a schema type
    let rec getTypeSize (ctx: PlatformContext) (schema: SchemaDefinition) (typ: SchemaType) : Size =
        match typ with
        | SchemaType.NTU(kind, encoding) ->
            match encoding with
            | WireEncoding.Fixed ->
                let size = PlatformContext.resolveSize ctx kind
                { Min = size; Max = Some size; IsFixed = true }
            | WireEncoding.VarInt ->
                // Varint: 1-10 bytes for 64-bit integers
                { Min = 1; Max = Some 10; IsFixed = false }
            | WireEncoding.LengthPrefixed ->
                // Length prefix (varint) + content
                { Min = 1; Max = None; IsFixed = false }

        | SchemaType.FixedData length ->
            { Min = length; Max = Some length; IsFixed = true }

        | SchemaType.Enum(_, _) ->
            // Enums are varint encoded
            { Min = 1; Max = Some 10; IsFixed = false }

        | SchemaType.Aggregate aggType ->
            match aggType with
            | AggregateType.Optional innerType ->
                let innerSize = getTypeSize ctx schema innerType
                { Min = 1; Max = innerSize.Max |> Option.map (fun m -> m + 1); IsFixed = false }

            | AggregateType.List _ ->
                { Min = 1; Max = None; IsFixed = false }

            | AggregateType.FixedList(innerType, length) ->
                let innerSize = getTypeSize ctx schema innerType
                if innerSize.IsFixed then
                    let totalSize = innerSize.Min * length
                    { Min = totalSize; Max = Some totalSize; IsFixed = true }
                else
                    { Min = innerSize.Min * length; Max = None; IsFixed = false }

            | AggregateType.Map(_, _) ->
                { Min = 1; Max = None; IsFixed = false }

            | AggregateType.Union cases ->
                let caseSizes = cases |> Map.values |> List.map (getTypeSize ctx schema)
                let minSize = 1 + (List.minBy (fun s -> s.Min) caseSizes).Min
                let maxSize =
                    List.fold (fun acc size ->
                        match acc, size.Max with
                        | None, _ -> None
                        | _, None -> None
                        | Some accMax, Some sizeMax -> Some(max accMax sizeMax)
                    ) (Some 10) caseSizes
                { Min = minSize; Max = maxSize; IsFixed = false }

            | AggregateType.Struct fields ->
                let fieldSizes = fields |> List.map (fun f -> getTypeSize ctx schema f.FieldType)
                let totalMinSize = fieldSizes |> List.sumBy (fun s -> s.Min)
                let totalMaxSize =
                    fieldSizes
                    |> List.fold (fun acc size ->
                        match acc, size.Max with
                        | None, _ -> None
                        | _, None -> None
                        | Some accMax, Some sizeMax -> Some(accMax + sizeMax)
                    ) (Some 0)
                let isFixed = fieldSizes |> List.forall (fun s -> s.IsFixed)
                { Min = totalMinSize; Max = totalMaxSize; IsFixed = isFixed }

        | SchemaType.TypeRef typeName ->
            match Map.tryFind typeName schema.Types with
            | Some t -> getTypeSize ctx schema t
            | None -> failwith ("Type not found: " + typeName)

    /// Get alignment for a schema type
    let rec getTypeAlignment (ctx: PlatformContext) (schema: SchemaDefinition) (typ: SchemaType) : Alignment =
        match typ with
        | SchemaType.NTU(kind, _) ->
            { Value = PlatformContext.resolveAlign ctx kind }

        | SchemaType.FixedData _ ->
            { Value = 1 }

        | SchemaType.Enum(baseKind, _) ->
            { Value = PlatformContext.resolveAlign ctx baseKind }

        | SchemaType.Aggregate aggType ->
            match aggType with
            | AggregateType.Optional innerType ->
                let innerAlign = getTypeAlignment ctx schema innerType
                { Value = max 1 innerAlign.Value }

            | AggregateType.List innerType
            | AggregateType.FixedList(innerType, _) ->
                let innerAlign = getTypeAlignment ctx schema innerType
                { Value = max 1 innerAlign.Value }

            | AggregateType.Map(keyType, valueType) ->
                let keyAlign = getTypeAlignment ctx schema keyType
                let valueAlign = getTypeAlignment ctx schema valueType
                { Value = max keyAlign.Value valueAlign.Value }

            | AggregateType.Union cases ->
                let caseAlignments = cases |> Map.values |> List.map (getTypeAlignment ctx schema)
                let maxAlign = caseAlignments |> List.map (fun a -> a.Value) |> List.max
                { Value = max 1 maxAlign }

            | AggregateType.Struct fields ->
                let fieldAlignments = fields |> List.map (fun f -> getTypeAlignment ctx schema f.FieldType)
                let maxAlign = fieldAlignments |> List.map (fun a -> a.Value) |> List.max
                { Value = maxAlign }

        | SchemaType.TypeRef typeName ->
            match Map.tryFind typeName schema.Types with
            | Some t -> getTypeAlignment ctx schema t
            | None -> failwith ("Type not found: " + typeName)

    /// Check if two schema types are compatible
    let rec areTypesCompatible
        (ctx: PlatformContext)
        (schema1: SchemaDefinition)
        (schema2: SchemaDefinition)
        (type1: SchemaType)
        (type2: SchemaType)
        : bool =
        match type1, type2 with
        | SchemaType.NTU(k1, e1), SchemaType.NTU(k2, e2) ->
            k1 = k2 && e1 = e2

        | SchemaType.FixedData len1, SchemaType.FixedData len2 ->
            len1 = len2

        | SchemaType.Enum(k1, v1), SchemaType.Enum(k2, v2) ->
            k1 = k2 && v1 = v2

        | SchemaType.TypeRef n1, SchemaType.TypeRef n2 ->
            n1 = n2

        | SchemaType.Aggregate agg1, SchemaType.Aggregate agg2 ->
            match agg1, agg2 with
            | AggregateType.Optional t1, AggregateType.Optional t2 ->
                areTypesCompatible ctx schema1 schema2 t1 t2

            | AggregateType.List t1, AggregateType.List t2 ->
                areTypesCompatible ctx schema1 schema2 t1 t2

            | AggregateType.FixedList(t1, len1), AggregateType.FixedList(t2, len2) ->
                len1 = len2 && areTypesCompatible ctx schema1 schema2 t1 t2

            | AggregateType.Map(k1, v1), AggregateType.Map(k2, v2) ->
                areTypesCompatible ctx schema1 schema2 k1 k2
                && areTypesCompatible ctx schema1 schema2 v1 v2

            | AggregateType.Union cases1, AggregateType.Union cases2 ->
                Map.forall (fun tag typ1 ->
                    match Map.tryFind tag cases2 with
                    | Some typ2 -> areTypesCompatible ctx schema1 schema2 typ1 typ2
                    | None -> false
                ) cases1

            | AggregateType.Struct fields1, AggregateType.Struct fields2 ->
                List.length fields1 = List.length fields2
                && List.forall2 (fun (f1: StructField) (f2: StructField) ->
                    f1.Name = f2.Name
                    && areTypesCompatible ctx schema1 schema2 f1.FieldType f2.FieldType
                ) fields1 fields2

            | _, _ -> false

        | _, _ -> false

    /// Check compatibility between two schemas
    let checkCompatibility (ctx: PlatformContext) (oldSchema: SchemaDefinition) (newSchema: SchemaDefinition) : Compatibility =
        let checkRootCompatibility () =
            match Map.tryFind oldSchema.Root oldSchema.Types, Map.tryFind newSchema.Root newSchema.Types with
            | Some oldRoot, Some newRoot ->
                match oldRoot, newRoot with
                | SchemaType.Aggregate(AggregateType.Union oldCases), SchemaType.Aggregate(AggregateType.Union newCases) ->
                    let allOldCasesExist =
                        oldCases |> Map.forall (fun oldTag oldType ->
                            match Map.tryFind oldTag newCases with
                            | Some newType -> areTypesCompatible ctx oldSchema newSchema oldType newType
                            | None -> false)

                    let allNewCasesExist =
                        newCases |> Map.forall (fun newTag newType ->
                            match Map.tryFind newTag oldCases with
                            | Some oldType -> areTypesCompatible ctx newSchema oldSchema newType oldType
                            | None -> false)

                    match allOldCasesExist, allNewCasesExist with
                    | true, true -> FullyCompatible
                    | true, false -> BackwardCompatible
                    | false, true -> ForwardCompatible
                    | false, false -> Incompatible [ "Incompatible union types" ]

                | SchemaType.Aggregate(AggregateType.Struct oldFields), SchemaType.Aggregate(AggregateType.Struct newFields) ->
                    let rec checkFields oldFields newFields =
                        match oldFields, newFields with
                        | [], _ -> true
                        | _, [] -> false
                        | oldField :: oldRest, newField :: newRest ->
                            oldField.Name = newField.Name
                            && areTypesCompatible ctx oldSchema newSchema oldField.FieldType newField.FieldType
                            && checkFields oldRest newRest

                    let allOldFieldsExist = checkFields oldFields newFields

                    if allOldFieldsExist then
                        if List.length oldFields = List.length newFields then
                            FullyCompatible
                        else
                            BackwardCompatible
                    else
                        Incompatible [ "Incompatible struct types" ]

                | _ ->
                    if areTypesCompatible ctx oldSchema newSchema oldRoot newRoot then
                        FullyCompatible
                    else
                        Incompatible [ "Root types are different" ]

            | None, _ -> Incompatible [ ("Old root type '" + oldSchema.Root + "' not found") ]
            | _, None -> Incompatible [ ("New root type '" + newSchema.Root + "' not found") ]

        checkRootCompatibility ()
