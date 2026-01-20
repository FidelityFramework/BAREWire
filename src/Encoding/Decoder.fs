namespace BAREWire.Encoding

open BAREWire.Core
open BAREWire.Core.Error

/// <summary>
/// Decoding functions for BARE types.
/// Uses pure F# with no Alloy dependencies.
/// </summary>
module Decoder =

    // Helper to compute actual byte index in Memory
    let inline private memIndex (memory: Memory<'T>) (localOffset: int) : int =
        int memory.Offset + int localOffset

    /// <summary>
    /// Reads a uint value using ULEB128 encoding
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded uint64 value and the new offset</returns>
    let inline readUInt (memory: Memory<'T>) (offset: int): uint64 * int =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable currentOffset = offset
        let mutable currentByte = 0uy

        let mutable shouldContinue = true
        while shouldContinue do
            currentByte <- memory.Data.[memIndex memory currentOffset]
            currentOffset <- currentOffset + 1

            result <- result ||| ((uint64 (currentByte &&& 0x7Fuy)) <<< shift)
            shift <- shift + 7
            shouldContinue <- (currentByte &&& 0x80uy <> 0uy) && (shift < 64)

        result, currentOffset

    /// <summary>
    /// Reads a uint value using ULEB128 encoding from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The decoded uint64 value</returns>
    let inline readUIntArray (bytes: byte[]) (offset: byref<int>): uint64 =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable currentByte = 0uy

        let mutable shouldContinue = true
        while shouldContinue && offset < bytes.Length do
            currentByte <- bytes.[offset]
            offset <- offset + 1

            result <- result ||| ((uint64 (currentByte &&& 0x7Fuy)) <<< shift)
            shift <- shift + 7
            shouldContinue <- (currentByte &&& 0x80uy <> 0uy) && (shift < 64)

        result

    /// <summary>
    /// Reads an int value using zigzag ULEB128 encoding
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded int64 value and the new offset</returns>
    let inline readInt (memory: Memory<'T>) (offset: int): int64 * int =
        let uintVal, newOffset = readUInt memory offset

        // Zigzag decoding: (n >>> 1) ^ -(n &&& 1)
        let value = (int64 uintVal >>> 1) ^^^ (-(int64 (uintVal &&& 1UL)))
        value, newOffset

    /// <summary>
    /// Reads an int value using zigzag ULEB128 encoding from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The decoded int64 value</returns>
    let inline readIntArray (bytes: byte[]) (offset: byref<int>): int64 =
        let uintVal = readUIntArray bytes &offset

        // Zigzag decoding: (n >>> 1) ^ -(n &&& 1)
        (int64 uintVal >>> 1) ^^^ (-(int64 (uintVal &&& 1UL)))

    /// <summary>
    /// Reads a u8 (byte) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The byte value and the new offset</returns>
    let inline readU8 (memory: Memory<'T>) (offset: int): byte * int =
        let value = memory.Data.[memIndex memory offset]
        value, offset + 1

    /// <summary>
    /// Reads a u8 (byte) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The byte value</returns>
    let inline readU8Array (bytes: byte[]) (offset: byref<int>): byte =
        let value = bytes.[offset]
        offset <- offset + 1
        value

    /// <summary>
    /// Reads a u16 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint16 value and the new offset</returns>
    let inline readU16 (memory: Memory<'T>) (offset: int): uint16 * int =
        let idx = memIndex memory offset
        let value = Binary.toUInt16 memory.Data idx
        value, offset + 2

    /// <summary>
    /// Reads a u16 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The uint16 value</returns>
    let inline readU16Array (bytes: byte[]) (offset: byref<int>): uint16 =
        let value = Binary.toUInt16 bytes offset
        offset <- offset + 2
        value

    /// <summary>
    /// Reads a u32 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint32 value and the new offset</returns>
    let inline readU32 (memory: Memory<'T>) (offset: int): uint32 * int =
        let idx = memIndex memory offset
        let value = Binary.toUInt32 memory.Data idx
        value, offset + 4

    /// <summary>
    /// Reads a u32 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The uint32 value</returns>
    let inline readU32Array (bytes: byte[]) (offset: byref<int>): uint32 =
        let value = Binary.toUInt32 bytes offset
        offset <- offset + 4
        value

    /// <summary>
    /// Reads a u64 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint64 value and the new offset</returns>
    let inline readU64 (memory: Memory<'T>) (offset: int): uint64 * int =
        let idx = memIndex memory offset
        let value = Binary.toUInt64 memory.Data idx
        value, offset + 8

    /// <summary>
    /// Reads a u64 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The uint64 value</returns>
    let inline readU64Array (bytes: byte[]) (offset: byref<int>): uint64 =
        let value = Binary.toUInt64 bytes offset
        offset <- offset + 8
        value

    /// <summary>
    /// Reads an i8 (sbyte) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The sbyte value and the new offset</returns>
    let inline readI8 (memory: Memory<'T>) (offset: int): sbyte * int =
        let value = sbyte memory.Data.[memIndex memory offset]
        value, offset + 1

    /// <summary>
    /// Reads an i8 (sbyte) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The sbyte value</returns>
    let inline readI8Array (bytes: byte[]) (offset: byref<int>): sbyte =
        let value = sbyte bytes.[offset]
        offset <- offset + 1
        value

    /// <summary>
    /// Reads an i16 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int16 value and the new offset</returns>
    let inline readI16 (memory: Memory<'T>) (offset: int): int16 * int =
        let idx = memIndex memory offset
        let value = Binary.toInt16 memory.Data idx
        value, offset + 2

    /// <summary>
    /// Reads an i16 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The int16 value</returns>
    let inline readI16Array (bytes: byte[]) (offset: byref<int>): int16 =
        let value = Binary.toInt16 bytes offset
        offset <- offset + 2
        value

    /// <summary>
    /// Reads an i32 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int32 value and the new offset</returns>
    let inline readI32 (memory: Memory<'T>) (offset: int): int32 * int =
        let idx = memIndex memory offset
        let value = Binary.toInt32 memory.Data idx
        value, offset + 4

    /// <summary>
    /// Reads an i32 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The int32 value</returns>
    let inline readI32Array (bytes: byte[]) (offset: byref<int>): int32 =
        let value = Binary.toInt32 bytes offset
        offset <- offset + 4
        value

    /// <summary>
    /// Reads an i64 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int64 value and the new offset</returns>
    let inline readI64 (memory: Memory<'T>) (offset: int): int64 * int =
        let idx = memIndex memory offset
        let value = Binary.toInt64 memory.Data idx
        value, offset + 8

    /// <summary>
    /// Reads an i64 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The int64 value</returns>
    let inline readI64Array (bytes: byte[]) (offset: byref<int>): int64 =
        let value = Binary.toInt64 bytes offset
        offset <- offset + 8
        value

    /// <summary>
    /// Reads an f32 (float32) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The float32 value and the new offset</returns>
    let inline readF32 (memory: Memory<'T>) (offset: int): float32 * int =
        let bits, newOffset = readI32 memory offset
        let value = Binary.int32BitsToSingle bits
        value, newOffset

    /// <summary>
    /// Reads an f32 (float32) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The float32 value</returns>
    let inline readF32Array (bytes: byte[]) (offset: byref<int>): float32 =
        let bits = readI32Array bytes &offset
        Binary.int32BitsToSingle bits

    /// <summary>
    /// Reads an f64 (double) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The double value and the new offset</returns>
    let inline readF64 (memory: Memory<'T>) (offset: int): float * int =
        let bits, newOffset = readI64 memory offset
        let value = Binary.int64BitsToDouble bits
        value, newOffset

    /// <summary>
    /// Reads an f64 (double) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The double value</returns>
    let inline readF64Array (bytes: byte[]) (offset: byref<int>): float =
        let bits = readI64Array bytes &offset
        Binary.int64BitsToDouble bits

    /// <summary>
    /// Reads a boolean value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The boolean value and the new offset</returns>
    /// <remarks>Throws when the byte is not 0 or 1</remarks>
    let inline readBool (memory: Memory<'T>) (offset: int): bool * int =
        let b = memory.Data.[memIndex memory offset]
        match b with
        | 0uy -> false, offset + 1
        | 1uy -> true, offset + 1
        | _ -> failwith $"Invalid boolean value: {b}"

    /// <summary>
    /// Reads a boolean value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The boolean value</returns>
    /// <remarks>Throws when the byte is not 0 or 1</remarks>
    let inline readBoolArray (bytes: byte[]) (offset: byref<int>): bool =
        let b = bytes.[offset]
        offset <- offset + 1
        match b with
        | 0uy -> false
        | 1uy -> true
        | _ -> failwith $"Invalid boolean value: {b}"

    /// <summary>
    /// Reads a string value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The string value and the new offset</returns>
    let inline readString (memory: Memory<'T>) (offset: int): string * int =
        // Read length
        let length, currentOffset = readUInt memory offset
        if length = 0UL then
            "", currentOffset
        else
            // Extract bytes and decode
            let startIdx = memIndex memory currentOffset
            let strBytes = memory.Data.[startIdx .. startIdx + int length - 1]
            let str = Utf8.getString strBytes

            str, currentOffset + (int length * 1)

    /// <summary>
    /// Reads a string value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The string value</returns>
    let inline readStringArray (bytes: byte[]) (offset: byref<int>): string =
        // Read length
        let length = int (readUIntArray bytes &offset)
        if length = 0 then
            ""
        else
            // Extract bytes and decode
            let strBytes = bytes.[offset .. offset + length - 1]
            let str = Utf8.getString strBytes
            offset <- offset + length
            str

    /// <summary>
    /// Reads a variable-length data value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readData (memory: Memory<'T>) (offset: int): byte[] * int =
        // Read length
        let length, currentOffset = readUInt memory offset
        if length = 0UL then
            Array.empty, currentOffset
        else
            // Extract bytes
            let startIdx = memIndex memory currentOffset
            let result = memory.Data.[startIdx .. startIdx + int length - 1]

            result, currentOffset + (int length * 1)

    /// <summary>
    /// Reads a variable-length data value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <returns>The byte array</returns>
    let inline readDataArray (bytes: byte[]) (offset: byref<int>): byte[] =
        // Read length
        let length = int (readUIntArray bytes &offset)
        if length = 0 then
            Array.empty
        else
            // Extract bytes
            let result = bytes.[offset .. offset + length - 1]
            offset <- offset + length
            result

    /// <summary>
    /// Reads fixed-length data
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The length of data to read</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readFixedData (memory: Memory<'T>) (offset: int) (length: int): byte[] * int =
        // Extract bytes
        let startIdx = memIndex memory offset
        let result = memory.Data.[startIdx .. startIdx + length - 1]

        result, offset + (length * 1)

    /// <summary>
    /// Reads fixed-length data from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="length">The length of data to read</param>
    /// <returns>The byte array</returns>
    let inline readFixedDataArray (bytes: byte[]) (offset: byref<int>) (length: int): byte[] =
        // Extract bytes
        let result = bytes.[offset .. offset + length - 1]
        offset <- offset + length
        result

    /// <summary>
    /// Reads an optional value using F#'s ValueOption for zero-allocation
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read the value if present</param>
    /// <returns>The optional value and the new offset</returns>
    /// <remarks>Throws when the tag is not 0 or 1</remarks>
    let inline readOptional (memory: Memory<'T>)
                     (offset: int)
                     (readValue: Memory<'T> -> int -> 'a * int):
                     ValueOption<'a> * int =
        let tag = memory.Data.[memIndex memory offset]
        let currentOffset = offset + 1

        match tag with
        | 0uy -> ValueOption<'a>.None, currentOffset
        | 1uy ->
            let value, newOffset = readValue memory currentOffset
            ValueOption.Some value, newOffset
        | _ -> failwith $"Invalid optional tag: {tag}"

    /// <summary>
    /// Reads an optional value from a byte array using F#'s ValueOption
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="readValue">A function to read the value if present</param>
    /// <returns>The optional value</returns>
    /// <remarks>Throws when the tag is not 0 or 1</remarks>
    let inline readOptionalArray (bytes: byte[])
                        (offset: byref<int>)
                        (readValue: byte[] -> byref<int> -> 'a):
                        ValueOption<'a> =
        let tag = bytes.[offset]
        offset <- offset + 1

        match tag with
        | 0uy -> ValueOption<'a>.None
        | 1uy ->
            let value = readValue bytes &offset
            ValueOption.Some value
        | _ -> failwith $"Invalid optional tag: {tag}"

    /// <summary>
    /// Reads a list of values
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The list of values and the new offset</returns>
    let inline readList (memory: Memory<'T>)
                 (offset: int)
                 (readValue: Memory<'T> -> int -> 'a * int):
                 'a list * int =
        // Read count
        let count, currentOffset = readUInt memory offset

        // Read each value
        let mutable values = []
        let mutable currentOff = currentOffset

        for _ in 1UL..count do
            let value, newOffset = readValue memory currentOff
            values <- value :: values
            currentOff <- newOffset

        List.rev values, currentOff

    /// <summary>
    /// Reads a list of values from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The list of values</returns>
    let inline readListArray (bytes: byte[])
                    (offset: byref<int>)
                    (readValue: byte[] -> byref<int> -> 'a):
                    'a list =
        // Read count
        let count = int (readUIntArray bytes &offset)

        // Read each value
        let mutable values = []

        for _ in 1..count do
            let value = readValue bytes &offset
            values <- value :: values

        List.rev values

    /// <summary>
    /// Reads a fixed-length list of values
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The number of elements to read</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The list of values and the new offset</returns>
    let inline readFixedList (memory: Memory<'T>)
                      (offset: int)
                      (length: int)
                      (readValue: Memory<'T> -> int -> 'a * int):
                      'a list * int =
        // Read each value (no length prefix)
        let mutable values = []
        let mutable currentOffset = offset

        for _ in 1..length do
            let value, newOffset = readValue memory currentOffset
            values <- value :: values
            currentOffset <- newOffset

        List.rev values, currentOffset

    /// <summary>
    /// Reads a fixed-length list of values from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="length">The number of elements to read</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The list of values</returns>
    let inline readFixedListArray (bytes: byte[])
                         (offset: byref<int>)
                         (length: int)
                         (readValue: byte[] -> byref<int> -> 'a):
                         'a list =
        // Read each value (no length prefix)
        let mutable values = []

        for _ in 1..length do
            let value = readValue bytes &offset
            values <- value :: values

        List.rev values

    /// <summary>
    /// Reads a map of key-value pairs
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readKey">A function to read each key</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The map of key-value pairs and the new offset</returns>
    let inline readMap (memory: Memory<'T>)
                (offset: int)
                (readKey: Memory<'T> -> int -> 'k * int)
                (readValue: Memory<'T> -> int -> 'v * int):
                Map<'k, 'v> * int =
        // Read count
        let count, currentOffset = readUInt memory offset

        // Read each key-value pair
        let mutable map = Map.empty
        let mutable currentOff = currentOffset

        for _ in 1UL..count do
            let key, keyOffset = readKey memory currentOff
            let value, valueOffset = readValue memory keyOffset

            map <- Map.add key value map
            currentOff <- valueOffset

        map, currentOff

    /// <summary>
    /// Reads a map of key-value pairs from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="readKey">A function to read each key</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The map of key-value pairs</returns>
    let inline readMapArray (bytes: byte[])
                   (offset: byref<int>)
                   (readKey: byte[] -> byref<int> -> 'k)
                   (readValue: byte[] -> byref<int> -> 'v):
                   Map<'k, 'v> =
        // Read count
        let count = int (readUIntArray bytes &offset)

        // Read each key-value pair
        let mutable map = Map.empty

        for _ in 1..count do
            let key = readKey bytes &offset
            let value = readValue bytes &offset

            map <- Map.add key value map

        map

    /// <summary>
    /// Reads a union value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValueForTag">A function to read a value based on its tag</param>
    /// <returns>The union tag, value and the new offset</returns>
    let inline readUnion (memory: Memory<'T>)
                  (offset: int)
                  (readValueForTag: uint -> Memory<'T> -> int -> 'a * int):
                  uint * 'a * int =
        // Read tag
        let tagVal, currentOffset = readUInt memory offset
        let tag = uint tagVal

        // Read value based on tag
        let value, finalOffset = readValueForTag tag memory currentOffset
        tag, value, finalOffset

    /// <summary>
    /// Reads a union value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset (ref parameter updated with new position)</param>
    /// <param name="readValueForTag">A function to read a value based on its tag</param>
    /// <returns>The union tag and value</returns>
    let inline readUnionArray (bytes: byte[])
                     (offset: byref<int>)
                     (readValueForTag: uint -> byte[] -> byref<int> -> 'a):
                     uint * 'a =
        // Read tag
        let tagVal = readUIntArray bytes &offset
        let tag = uint tagVal

        // Read value based on tag
        let value = readValueForTag tag bytes &offset
        tag, value
