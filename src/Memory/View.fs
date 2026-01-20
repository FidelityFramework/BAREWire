#nowarn "9"

namespace BAREWire.Memory

open FSharp.NativeInterop
open FSharp.Native.Compiler.NativeTypedTree.NativeTypes
open BAREWire.Core
open BAREWire.Core.Error
open BAREWire.Core.Memory
open BAREWire.Core.Utf8
open BAREWire.Schema
open BAREWire.Memory.SafeMemory

/// <summary>
/// Memory view operations for working with typed data in memory regions.
/// Uses NTUKind from FNCS for all type information.
/// </summary>
module View =
    /// <summary>
    /// A field path in a memory view, represented as a list of field names
    /// </summary>
    type FieldPath = string list

    /// <summary>
    /// Field offset information within a memory view
    /// </summary>
    type FieldOffset = {
        /// The offset of the field in bytes
        Offset: int
        /// The schema type of the field
        SchemaType: SchemaType
        /// The size of the field in bytes (min size for variable types)
        Size: int
        /// The alignment of the field in bytes
        Alignment: int
    }

    /// <summary>
    /// A view over a memory region with a specific schema
    /// </summary>
    type MemoryView<'T> = {
        /// The underlying memory region
        Memory: Memory<'T>
        /// The schema defining the structure of the data
        Schema: SchemaDefinition
        /// Field offsets cache for faster access
        FieldOffsets: Map<string, FieldOffset>
    }

    // ==========================================================================
    // Read functions for primitive types
    // ==========================================================================

    /// Reads an unsigned variable-length integer
    let readUInt (data: byte[]) (offset: int) : uint64 =
        let mutable value = 0UL
        let mutable shift = 0
        let mutable pos = offset
        let mutable shouldContinue = true

        while shouldContinue do
            let b = readByte data pos
            value <- value ||| ((uint64 (b &&& 0x7Fuy)) <<< shift)
            pos <- pos + 1
            shift <- shift + 7
            shouldContinue <- (b &&& 0x80uy) <> 0uy && shift < 64

        value

    /// Reads a signed variable-length integer (zigzag encoded)
    let readInt (data: byte[]) (offset: int) : int64 =
        let unsigned = readUInt data offset
        if (unsigned &&& 1UL = 0UL) then
            int64 (unsigned >>> 1)
        else
            ~~~(int64 (unsigned >>> 1))

    /// Reads an unsigned 8-bit integer
    let readU8 (data: byte[]) (offset: int) : byte =
        readByte data offset

    /// Reads an unsigned 16-bit integer
    let readU16 (data: byte[]) (offset: int) : uint16 =
        BAREWire.Core.Binary.toUInt16 data offset

    /// Reads an unsigned 32-bit integer
    let readU32 (data: byte[]) (offset: int) : uint32 =
        BAREWire.Core.Binary.toUInt32 data offset

    /// Reads an unsigned 64-bit integer
    let readU64 (data: byte[]) (offset: int) : uint64 =
        BAREWire.Core.Binary.toUInt64 data offset

    /// Reads a signed 8-bit integer
    let readI8 (data: byte[]) (offset: int) : sbyte =
        sbyte (readByte data offset)

    /// Reads a signed 16-bit integer
    let readI16 (data: byte[]) (offset: int) : int16 =
        BAREWire.Core.Binary.toInt16 data offset

    /// Reads a signed 32-bit integer
    let readI32 (data: byte[]) (offset: int) : int32 =
        BAREWire.Core.Binary.toInt32 data offset

    /// Reads a signed 64-bit integer
    let readI64 (data: byte[]) (offset: int) : int64 =
        BAREWire.Core.Binary.toInt64 data offset

    /// Reads a 32-bit floating point number
    let readF32 (data: byte[]) (offset: int) : float32 =
        let bits = readU32 data offset
        BAREWire.Core.Binary.int32BitsToSingle (int32 bits)

    /// Reads a 64-bit floating point number
    let readF64 (data: byte[]) (offset: int) : float =
        let bits = readU64 data offset
        BAREWire.Core.Binary.int64BitsToDouble (int64 bits)

    /// Reads a boolean value
    let readBool (data: byte[]) (offset: int) : bool =
        readByte data offset <> 0uy

    /// Reads a string
    let readString (data: byte[]) (offset: int) : string =
        let length = int (readUInt data offset)
        let mutable pos = offset

        // Skip the length bytes
        while pos < data.Length && (readByte data pos &&& 0x80uy) <> 0uy do
            pos <- pos + 1
        pos <- pos + 1

        if length > 0 && pos + length <= data.Length then
            let bytes = Array.init length (fun i -> readByte data (pos + i))
            getString bytes
        else
            ""

    /// Reads binary data
    let readData (data: byte[]) (offset: int) : byte[] =
        let length = int (readUInt data offset)
        let mutable pos = offset

        while pos < data.Length && (readByte data pos &&& 0x80uy) <> 0uy do
            pos <- pos + 1
        pos <- pos + 1

        if length > 0 && pos + length <= data.Length then
            Array.init length (fun i -> readByte data (pos + i))
        else
            [||]

    /// Reads fixed-size binary data
    let readFixedData (data: byte[]) (offset: int) (length: int) : byte[] =
        if offset + length <= data.Length then
            Array.init length (fun i -> readByte data (offset + i))
        else
            [||]

    // ==========================================================================
    // Write functions for primitive types
    // ==========================================================================

    /// Writes an unsigned variable-length integer
    let writeUInt (data: byte[]) (offset: int) (value: uint64) : int =
        let mutable remaining = value
        let mutable pos = offset

        while remaining >= 0x80UL do
            data.[pos] <- byte (0x80uy ||| (byte (remaining &&& 0x7FUL)))
            pos <- pos + 1
            remaining <- remaining >>> 7

        data.[pos] <- byte remaining
        pos - offset + 1

    /// Writes a signed variable-length integer (zigzag encoded)
    let writeInt (data: byte[]) (offset: int) (value: int64) : int =
        let zigzag =
            if value >= 0L then
                uint64 (value <<< 1)
            else
                uint64 ((value <<< 1) ^^^ -1L)
        writeUInt data offset zigzag

    /// Writes an unsigned 8-bit integer
    let writeU8 (data: byte[]) (offset: int) (value: byte) : unit =
        writeByte data offset value

    /// Writes an unsigned 16-bit integer
    let writeU16 (data: byte[]) (offset: int) (value: uint16) : unit =
        let bytes = BAREWire.Core.Binary.getUInt16Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes an unsigned 32-bit integer
    let writeU32 (data: byte[]) (offset: int) (value: uint32) : unit =
        let bytes = BAREWire.Core.Binary.getUInt32Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes an unsigned 64-bit integer
    let writeU64 (data: byte[]) (offset: int) (value: uint64) : unit =
        let bytes = BAREWire.Core.Binary.getUInt64Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes a signed 8-bit integer
    let writeI8 (data: byte[]) (offset: int) (value: sbyte) : unit =
        writeByte data offset (byte value)

    /// Writes a signed 16-bit integer
    let writeI16 (data: byte[]) (offset: int) (value: int16) : unit =
        let bytes = BAREWire.Core.Binary.getInt16Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes a signed 32-bit integer
    let writeI32 (data: byte[]) (offset: int) (value: int32) : unit =
        let bytes = BAREWire.Core.Binary.getInt32Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes a signed 64-bit integer
    let writeI64 (data: byte[]) (offset: int) (value: int64) : unit =
        let bytes = BAREWire.Core.Binary.getInt64Bytes value
        for i = 0 to bytes.Length - 1 do
            data.[offset + i] <- bytes.[i]

    /// Writes a 32-bit floating point number
    let writeF32 (data: byte[]) (offset: int) (value: float32) : unit =
        let bits = BAREWire.Core.Binary.singleToInt32Bits value
        writeI32 data offset bits

    /// Writes a 64-bit floating point number
    let writeF64 (data: byte[]) (offset: int) (value: float) : unit =
        let bits = BAREWire.Core.Binary.doubleToInt64Bits value
        writeI64 data offset bits

    /// Writes a boolean value
    let writeBool (data: byte[]) (offset: int) (value: bool) : unit =
        writeByte data offset (if value then 1uy else 0uy)

    /// Writes a string
    let writeString (data: byte[]) (offset: int) (value: string) : int =
        let bytes = getBytes value
        let bytesLength = bytes.Length
        let lengthSize = writeUInt data offset (uint64 bytesLength)
        let stringOffset = offset + lengthSize

        for i = 0 to bytesLength - 1 do
            if stringOffset + i < data.Length then
                data.[stringOffset + i] <- bytes.[i]

        lengthSize + bytesLength

    /// Writes binary data
    let writeData (data: byte[]) (offset: int) (value: byte[]) : int =
        let valueLength = value.Length
        let lengthSize = writeUInt data offset (uint64 valueLength)
        let dataOffset = offset + lengthSize

        for i = 0 to valueLength - 1 do
            if dataOffset + i < data.Length then
                data.[dataOffset + i] <- value.[i]

        lengthSize + valueLength

    /// Writes fixed-size binary data
    let writeFixedData (data: byte[]) (offset: int) (value: byte[]) (length: int) : unit =
        let valueLength = value.Length

        for i = 0 to length - 1 do
            if offset + i < data.Length then
                if i < valueLength then
                    data.[offset + i] <- value.[i]
                else
                    data.[offset + i] <- 0uy

    // ==========================================================================
    // Size and alignment calculation using PlatformContext
    // ==========================================================================

    /// Gets the size and alignment for a schema type
    let rec getSizeAndAlignment (ctx: PlatformContext) (schema: SchemaDefinition) (typ: SchemaType) : int * int =
        match typ with
        | SchemaType.NTU(kind, encoding) ->
            match encoding with
            | WireEncoding.Fixed ->
                let size = PlatformContext.resolveSize ctx kind
                let align = PlatformContext.resolveAlign ctx kind
                size, align
            | WireEncoding.VarInt ->
                // Variable length - return max size, alignment 1
                10, 1
            | WireEncoding.LengthPrefixed ->
                // Variable length - return min size (length prefix only)
                1, 1

        | SchemaType.FixedData length ->
            length, 1

        | SchemaType.Enum(baseKind, _) ->
            // Enums use varint encoding, but base alignment
            let align = PlatformContext.resolveAlign ctx baseKind
            10, align

        | SchemaType.Aggregate aggType ->
            match aggType with
            | AggregateType.Optional innerType ->
                let innerSize, innerAlign = getSizeAndAlignment ctx schema innerType
                innerSize + 1, max 1 innerAlign

            | AggregateType.List _ ->
                // Variable length
                16, 8

            | AggregateType.FixedList(innerType, length) ->
                let innerSize, innerAlign = getSizeAndAlignment ctx schema innerType
                innerSize * length, innerAlign

            | AggregateType.Map(_, _) ->
                16, 8

            | AggregateType.Union cases ->
                let maxSize =
                    cases
                    |> Map.values
                    |> Seq.map (fun t -> fst (getSizeAndAlignment ctx schema t))
                    |> Seq.fold max 0

                let maxAlign =
                    cases
                    |> Map.values
                    |> Seq.map (fun t -> snd (getSizeAndAlignment ctx schema t))
                    |> Seq.fold max 1

                maxSize + 8, max 8 maxAlign

            | AggregateType.Struct fields ->
                let mutable totalSize = 0
                let mutable maxAlign = 1

                for field in fields do
                    let fieldSize, fieldAlign = getSizeAndAlignment ctx schema field.FieldType

                    // Align the current offset
                    let rem = totalSize % fieldAlign
                    if rem <> 0 then
                        totalSize <- totalSize + (fieldAlign - rem)

                    totalSize <- totalSize + fieldSize
                    maxAlign <- max maxAlign fieldAlign

                // Final size is multiple of max alignment
                let rem = totalSize % maxAlign
                if rem <> 0 then
                    totalSize <- totalSize + (maxAlign - rem)

                totalSize, maxAlign

        | SchemaType.TypeRef typeName ->
            match Map.tryFind typeName schema.Types with
            | Some t -> getSizeAndAlignment ctx schema t
            | None -> 8, 8  // Default if type not found

    // ==========================================================================
    // View creation and field access
    // ==========================================================================

    /// Calculates field offsets for a schema using platform context
    let calculateFieldOffsets (ctx: PlatformContext) (schema: SchemaDefinition) : Map<string, FieldOffset> =
        let rec calcOffsets typeName parentPath currentOffset acc =
            match Map.tryFind typeName schema.Types with
            | None -> acc
            | Some typ ->
                match typ with
                | SchemaType.Aggregate(AggregateType.Struct fields) ->
                    let mutable fieldOffset = currentOffset
                    let mutable result = acc

                    for field in fields do
                        let fieldPath =
                            if List.isEmpty parentPath then
                                field.Name
                            else
                                String.concat "." (parentPath @ [field.Name])

                        let fieldSize, fieldAlign = getSizeAndAlignment ctx schema field.FieldType

                        // Apply alignment
                        let rem = fieldOffset % fieldAlign
                        let alignedOffset =
                            if rem = 0 then fieldOffset
                            else fieldOffset + (fieldAlign - rem)

                        result <- Map.add
                            fieldPath
                            {
                                Offset = alignedOffset
                                SchemaType = field.FieldType
                                Size = fieldSize
                                Alignment = fieldAlign
                            }
                            result

                        fieldOffset <- alignedOffset + fieldSize

                        // Recurse for nested types
                        match field.FieldType with
                        | SchemaType.Aggregate(AggregateType.Struct _) ->
                            let newPath = parentPath @ [field.Name]
                            result <- calcOffsets (getTypeName field.FieldType) newPath alignedOffset result
                        | SchemaType.TypeRef nestedTypeName ->
                            let newPath = parentPath @ [field.Name]
                            result <- calcOffsets nestedTypeName newPath alignedOffset result
                        | _ -> ()

                    result
                | _ -> acc

        calcOffsets schema.Root [] 0 Map.empty

    and getTypeName (typ: SchemaType) : string =
        match typ with
        | SchemaType.TypeRef name -> name
        | _ -> ""

    /// Creates a view over a memory region with a schema
    let create<'T> (ctx: PlatformContext) (memory: Memory<'T>) (schema: SchemaDefinition) : MemoryView<'T> =
        let fieldOffsets = calculateFieldOffsets ctx schema
        {
            Memory = memory
            Schema = schema
            FieldOffsets = fieldOffsets
        }

    /// Resolves a field path to get its offset in memory
    let resolveFieldPath<'T> (view: MemoryView<'T>) (fieldPath: FieldPath) : Result<FieldOffset, Error.Error> =
        let pathString = String.concat "." fieldPath

        match Map.tryFind pathString view.FieldOffsets with
        | Some offset -> Ok offset
        | None -> Error (invalidValueError "Field path not found: " + pathString)

    /// Gets a field value from a view (simplified - returns boxed value)
    let getField<'T, 'Field> (view: MemoryView<'T>) (fieldPath: FieldPath) : Result<'Field, Error.Error> =
        match resolveFieldPath view fieldPath with
        | Error e -> Error e
        | Ok fieldOffset ->
            try
                let offsetInt = view.Memory.Offset + fieldOffset.Offset
                let result =
                    match fieldOffset.SchemaType with
                    | SchemaType.NTU(kind, _) ->
                        match kind with
                        | NTUKind.NTUuint8 -> box (readU8 view.Memory.Data offsetInt)
                        | NTUKind.NTUuint16 -> box (readU16 view.Memory.Data offsetInt)
                        | NTUKind.NTUuint32 -> box (readU32 view.Memory.Data offsetInt)
                        | NTUKind.NTUuint64 -> box (readU64 view.Memory.Data offsetInt)
                        | NTUKind.NTUint8 -> box (readI8 view.Memory.Data offsetInt)
                        | NTUKind.NTUint16 -> box (readI16 view.Memory.Data offsetInt)
                        | NTUKind.NTUint32 -> box (readI32 view.Memory.Data offsetInt)
                        | NTUKind.NTUint64 -> box (readI64 view.Memory.Data offsetInt)
                        | NTUKind.NTUfloat32 -> box (readF32 view.Memory.Data offsetInt)
                        | NTUKind.NTUfloat64 -> box (readF64 view.Memory.Data offsetInt)
                        | NTUKind.NTUbool -> box (readBool view.Memory.Data offsetInt)
                        | NTUKind.NTUstring -> box (readString view.Memory.Data offsetInt)
                        | _ -> failwith "Unsupported NTUKind"
                    | SchemaType.FixedData length ->
                        box (readFixedData view.Memory.Data offsetInt length)
                    | _ ->
                        failwith "Aggregate types not supported in this simplified implementation"

                Ok (unbox<'Field> result)
            with ex ->
                Error (decodingError "Failed to decode field: " + ex.Message)

    /// Sets a field value in a view (simplified)
    let setField<'T, 'Field> (view: MemoryView<'T>) (fieldPath: FieldPath) (value: 'Field) : Result<unit, Error.Error> =
        match resolveFieldPath view fieldPath with
        | Error e -> Error e
        | Ok fieldOffset ->
            try
                let offsetInt = view.Memory.Offset + fieldOffset.Offset
                match fieldOffset.SchemaType with
                | SchemaType.NTU(kind, _) ->
                    match kind with
                    | NTUKind.NTUuint8 -> writeU8 view.Memory.Data offsetInt (unbox<byte> (box value))
                    | NTUKind.NTUuint16 -> writeU16 view.Memory.Data offsetInt (unbox<uint16> (box value))
                    | NTUKind.NTUuint32 -> writeU32 view.Memory.Data offsetInt (unbox<uint32> (box value))
                    | NTUKind.NTUuint64 -> writeU64 view.Memory.Data offsetInt (unbox<uint64> (box value))
                    | NTUKind.NTUint8 -> writeI8 view.Memory.Data offsetInt (unbox<sbyte> (box value))
                    | NTUKind.NTUint16 -> writeI16 view.Memory.Data offsetInt (unbox<int16> (box value))
                    | NTUKind.NTUint32 -> writeI32 view.Memory.Data offsetInt (unbox<int32> (box value))
                    | NTUKind.NTUint64 -> writeI64 view.Memory.Data offsetInt (unbox<int64> (box value))
                    | NTUKind.NTUfloat32 -> writeF32 view.Memory.Data offsetInt (unbox<float32> (box value))
                    | NTUKind.NTUfloat64 -> writeF64 view.Memory.Data offsetInt (unbox<float> (box value))
                    | NTUKind.NTUbool -> writeBool view.Memory.Data offsetInt (unbox<bool> (box value))
                    | NTUKind.NTUstring -> ignore (writeString view.Memory.Data offsetInt (unbox<string> (box value)))
                    | _ -> failwith "Unsupported NTUKind"
                | SchemaType.FixedData length ->
                    writeFixedData view.Memory.Data offsetInt (unbox<byte[]> (box value)) length
                | _ ->
                    failwith "Aggregate types not supported in this simplified implementation"

                Ok ()
            with ex ->
                Error (encodingError "Failed to encode field: " + ex.Message)

    /// Checks if a field exists in the view
    let fieldExists<'T> (view: MemoryView<'T>) (fieldPath: FieldPath) : bool =
        let pathString = String.concat "." fieldPath
        Map.containsKey pathString view.FieldOffsets

    /// Gets all field names at the root level of a view
    let getRootFieldNames<'T> (view: MemoryView<'T>) : string list =
        match Map.tryFind view.Schema.Root view.Schema.Types with
        | Some(SchemaType.Aggregate(AggregateType.Struct fields)) ->
            fields |> List.map (fun field -> field.Name)
        | _ -> []

    /// Applies a function to transform a field value
    let updateField<'T, 'Field> (view: MemoryView<'T>) (fieldPath: FieldPath) (updateFn: 'Field -> 'Field) : Result<unit, Error.Error> =
        match getField<'T, 'Field> view fieldPath with
        | Error e -> Error e
        | Ok currentValue ->
            let newValue = updateFn currentValue
            setField<'T, 'Field> view fieldPath newValue
