namespace BAREWire.Schema

open BAREWire.Core

/// <summary>
/// Schema analysis tools for determining size, alignment, and other properties
/// </summary>
module Analysis =
    /// <summary>
    /// Compatibility level between schemas
    /// </summary>
    type Compatibility =
        /// <summary>Schemas are completely compatible in both directions</summary>
        | FullyCompatible
        /// <summary>New schema can read data from old schema</summary>
        | BackwardCompatible
        /// <summary>Old schema can read data from new schema</summary>
        | ForwardCompatible
        /// <summary>Schemas are incompatible, with reasons for incompatibility</summary>
        | Incompatible of reasons:string list
    
    /// <summary>
    /// Gets the size information for a type
    /// </summary>
    /// <param name="schema">The schema definition</param>
    /// <param name="typ">The type to analyze</param>
    /// <returns>Size information including minimum, maximum, and fixed-size status</returns>
    /// <remarks>Throws when a type is not found in the schema</remarks>
    let rec getTypeSize (schema: SchemaDefinition<validated>) (typ: Type): Size =
        match typ with
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
            | FixedData length -> { Min = length * 1<bytes>; Max = Some (length * 1<bytes>); IsFixed = true }
            | Void -> { Min = 0<bytes>; Max = Some 0<bytes>; IsFixed = true }
            | Enum _ -> { Min = 1<bytes>; Max = Some 10<bytes>; IsFixed = false }
        
        | Aggregate aggType ->
            match aggType with
            | Optional innerType ->
                let innerSize = getTypeSize schema innerType
                { 
                    Min = 1<bytes>
                    Max = innerSize.Max |> Option.map (fun m -> m + 1<bytes>)
                    IsFixed = false
                }
            
            | List innerType ->
                { Min = 1<bytes>; Max = None; IsFixed = false }
            
            | FixedList (innerType, length) ->
                let innerSize = getTypeSize schema innerType
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
                    |> Seq.map (getTypeSize schema)
                
                let minSize = 1<bytes> + (Seq.minBy (fun s -> s.Min) caseSizes).Min
                let maxSize = 
                    Seq.fold (fun acc size -> 
                        match acc, size.Max with
                        | None, _ -> None
                        | _, None -> None
                        | Some accMax, Some sizeMax -> Some (max accMax sizeMax)
                    ) (Some 10<bytes>) caseSizes
                
                { Min = minSize; Max = maxSize; IsFixed = false }
            
            | Struct fields ->
                let fieldSizes = fields |> List.map (fun f -> getTypeSize schema f.Type)
                
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
            | Some typ -> getTypeSize schema typ
            | None -> failwith $"Type not found: {typeName}"
    
    /// <summary>
    /// Gets the alignment requirements for a type
    /// </summary>
    /// <param name="schema">The schema definition</param>
    /// <param name="typ">The type to analyze</param>
    /// <returns>Alignment information for the type</returns>
    /// <remarks>Throws when a type is not found in the schema</remarks>
    let rec getTypeAlignment (schema: SchemaDefinition<validated>) (typ: Type): Alignment =
        match typ with
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
                let innerAlign = getTypeAlignment schema innerType
                { Value = max 1<bytes> innerAlign.Value }
            
            | List innerType | FixedList (innerType, _) ->
                let innerAlign = getTypeAlignment schema innerType
                { Value = max 1<bytes> innerAlign.Value }
            
            | Map (keyType, valueType) ->
                let keyAlign = getTypeAlignment schema keyType
                let valueAlign = getTypeAlignment schema valueType
                { Value = max keyAlign.Value valueAlign.Value }
            
            | Union cases ->
                let caseAlignments = 
                    cases 
                    |> Map.values 
                    |> Seq.map (getTypeAlignment schema)
                
                let maxAlign = 
                    caseAlignments 
                    |> Seq.map (fun a -> a.Value) 
                    |> Seq.max
                
                { Value = max 1<bytes> maxAlign }
            
            | Struct fields ->
                let fieldAlignments = 
                    fields 
                    |> List.map (fun f -> getTypeAlignment schema f.Type)
                
                let maxAlign = 
                    fieldAlignments 
                    |> List.map (fun a -> a.Value) 
                    |> List.max
                
                { Value = maxAlign }
        
        | UserDefined typeName ->
            // Look up the type in the schema
            match Map.tryFind typeName schema.Types with
            | Some typ -> getTypeAlignment schema typ
            | None -> failwith $"Type not found: {typeName}"
    
    /// <summary>
    /// Checks if two types are compatible
    /// </summary>
    /// <param name="schema1">The first schema</param>
    /// <param name="schema2">The second schema</param>
    /// <param name="type1">The first type</param>
    /// <param name="type2">The second type</param>
    /// <returns>True if the types are compatible, false otherwise</returns>
    let rec areTypesCompatible (schema1: SchemaDefinition<validated>) (schema2: SchemaDefinition<validated>) (type1: Type) (type2: Type): bool =
        match type1, type2 with
        | Primitive p1, Primitive p2 -> p1 = p2
        | UserDefined n1, UserDefined n2 -> 
            // Types are compatible if they have the same name
            n1 = n2
        | Aggregate agg1, Aggregate agg2 ->
            match agg1, agg2 with
            | Optional innerType1, Optional innerType2 ->
                areTypesCompatible schema1 schema2 innerType1 innerType2
                
            | List innerType1, List innerType2 ->
                areTypesCompatible schema1 schema2 innerType1 innerType2
                
            | FixedList (innerType1, len1), FixedList (innerType2, len2) ->
                len1 = len2 && areTypesCompatible schema1 schema2 innerType1 innerType2
                
            | Map (keyType1, valueType1), Map (keyType2, valueType2) ->
                areTypesCompatible schema1 schema2 keyType1 keyType2 &&
                areTypesCompatible schema1 schema2 valueType1 valueType2
                
            | Union cases1, Union cases2 ->
                // All cases in cases1 must exist in cases2 with compatible types
                Map.forall (fun tag typ1 ->
                    match Map.tryFind tag cases2 with
                    | Some typ2 -> areTypesCompatible schema1 schema2 typ1 typ2
                    | None -> false
                ) cases1
                
            | Struct fields1, Struct fields2 ->
                // Fields must match in name, order, and type
                List.length fields1 = List.length fields2 &&
                List.forall2 (fun (f1: StructField) (f2: StructField) ->
                    f1.Name = f2.Name && areTypesCompatible schema1 schema2 f1.Type f2.Type
                ) fields1 fields2
                
            | _, _ -> false
            
        | _, _ -> false
    
    /// <summary>
    /// Checks compatibility between two schemas
    /// </summary>
    /// <param name="oldSchema">The old schema</param>
    /// <param name="newSchema">The new schema</param>
    /// <returns>A Compatibility value indicating the level of compatibility</returns>
    let checkCompatibility (oldSchema: SchemaDefinition<validated>) (newSchema: SchemaDefinition<validated>): Compatibility =
        // Two schemas are fully compatible if they have the same structure
        // They are backward compatible if new schema can read old data
        // They are forward compatible if old schema can read new data
        
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
                        | Some newType -> areTypesCompatible oldSchema newSchema oldType newType
                        | None -> false)
                
                // A union is forward compatible if all new cases exist in the old schema
                let allNewCasesExist =
                    newCases
                    |> Map.forall (fun newTag newType ->
                        match Map.tryFind newTag oldCases with
                        | Some oldType -> areTypesCompatible newSchema oldSchema newType oldType
                        | None -> false)
                
                match allOldCasesExist, allNewCasesExist with
                | true, true -> FullyCompatible
                | true, false -> BackwardCompatible
                | false, true -> ForwardCompatible
                | false, false -> Incompatible ["Incompatible union types"]
            
            | Aggregate (Struct oldFields), Aggregate (Struct newFields) ->
                // A struct is backward compatible if all old fields exist in the new schema
                // in the same order
                let rec checkFields (oldFields: StructField list) (newFields: StructField list) =
                    match oldFields, newFields with
                    | [], _ -> true // All old fields matched
                    | _, [] -> false // More old fields than new fields
                    | oldField :: oldRest, newField :: newRest ->
                        if oldField.Name <> newField.Name then false
                        else if not (areTypesCompatible oldSchema newSchema oldField.Type newField.Type) then false
                        else checkFields oldRest newRest
                
                let allOldFieldsExist = checkFields oldFields newFields
                
                if allOldFieldsExist then
                    if List.length oldFields = List.length newFields then
                        FullyCompatible
                    else
                        BackwardCompatible
                else
                    Incompatible ["Incompatible struct types"]
            
            | _ ->
                // For other types, we require exact equality
                if areTypesCompatible oldSchema newSchema oldRoot newRoot then
                    FullyCompatible
                else
                    Incompatible ["Root types are different"]
        
        checkRootCompatibility ()