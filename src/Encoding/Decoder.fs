namespace BAREWire.Encoding

open BAREWire.Encoding

/// <summary>
/// Decoding functions for BARE types.
/// Uses pure F# with no Alloy dependencies.
/// </summary>
module Decoder =

    // Helper to compute actual byte index in Memory
    let inline private memIndex (memory: Memory) (localOffset: int) : int =
        int memory.Offset + int localOffset

    /// <summary>
    /// Reads a uint value using ULEB128 encoding
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded uint64 value and the new offset</returns>
    let inline readUInt (memory: Memory) (offset: int): uint64 * int =
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
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded uint64 value and the new offset</returns>
    let inline readUIntArray (bytes: byte[]) (offset: int): uint64 * int =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable currentOffset = offset
        let mutable currentByte = 0uy

        let mutable shouldContinue = true
        while shouldContinue && currentOffset < bytes.Length do
            currentByte <- bytes.[currentOffset]
            currentOffset <- currentOffset + 1

            result <- result ||| ((uint64 (currentByte &&& 0x7Fuy)) <<< shift)
            shift <- shift + 7
            shouldContinue <- (currentByte &&& 0x80uy <> 0uy) && (shift < 64)

        result, currentOffset

    /// <summary>
    /// Reads an int value using zigzag ULEB128 encoding
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded int64 value and the new offset</returns>
    let inline readInt (memory: Memory) (offset: int): int64 * int =
        match readUInt memory offset with
        | (uintVal, newOffset) ->
            // Zigzag decoding: (n >>> 1) ^ -(n &&& 1)
            let value = (int64 uintVal >>> 1) ^^^ (-(int64 (uintVal &&& 1UL)))
            value, newOffset

    /// <summary>
    /// Reads an int value using zigzag ULEB128 encoding from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The decoded int64 value and the new offset</returns>
    let inline readIntArray (bytes: byte[]) (offset: int): int64 * int =
        match readUIntArray bytes offset with
        | (uintVal, newOffset) ->
            // Zigzag decoding: (n >>> 1) ^ -(n &&& 1)
            let value = (int64 uintVal >>> 1) ^^^ (-(int64 (uintVal &&& 1UL)))
            value, newOffset

    /// <summary>
    /// Reads a u8 (byte) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The byte value and the new offset</returns>
    let inline readU8 (memory: Memory) (offset: int): byte * int =
        let value = memory.Data.[memIndex memory offset]
        value, offset + 1

    /// <summary>
    /// Reads a u8 (byte) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The byte value and the new offset</returns>
    let inline readU8Array (bytes: byte[]) (offset: int): byte * int =
        let value = bytes.[offset]
        value, offset + 1

    /// <summary>
    /// Reads a u16 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint16 value and the new offset</returns>
    let inline readU16 (memory: Memory) (offset: int): uint16 * int =
        let idx = memIndex memory offset
        let value = Bits.toUInt16 memory.Data idx
        value, offset + 2

    /// <summary>
    /// Reads a u16 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint16 value and the new offset</returns>
    let inline readU16Array (bytes: byte[]) (offset: int): uint16 * int =
        let value = Bits.toUInt16 bytes offset
        value, offset + 2

    /// <summary>
    /// Reads a u32 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint32 value and the new offset</returns>
    let inline readU32 (memory: Memory) (offset: int): uint32 * int =
        let idx = memIndex memory offset
        let value = Bits.toUInt32 memory.Data idx
        value, offset + 4

    /// <summary>
    /// Reads a u32 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint32 value and the new offset</returns>
    let inline readU32Array (bytes: byte[]) (offset: int): uint32 * int =
        let value = Bits.toUInt32 bytes offset
        value, offset + 4

    /// <summary>
    /// Reads a u64 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint64 value and the new offset</returns>
    let inline readU64 (memory: Memory) (offset: int): uint64 * int =
        let idx = memIndex memory offset
        let value = Bits.toUInt64 memory.Data idx
        value, offset + 8

    /// <summary>
    /// Reads a u64 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The uint64 value and the new offset</returns>
    let inline readU64Array (bytes: byte[]) (offset: int): uint64 * int =
        let value = Bits.toUInt64 bytes offset
        value, offset + 8

    /// <summary>
    /// Reads an i8 (sbyte) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The sbyte value and the new offset</returns>
    let inline readI8 (memory: Memory) (offset: int): sbyte * int =
        let value = sbyte memory.Data.[memIndex memory offset]
        value, offset + 1

    /// <summary>
    /// Reads an i8 (sbyte) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The sbyte value and the new offset</returns>
    let inline readI8Array (bytes: byte[]) (offset: int): sbyte * int =
        let value = sbyte bytes.[offset]
        value, offset + 1

    /// <summary>
    /// Reads an i16 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int16 value and the new offset</returns>
    let inline readI16 (memory: Memory) (offset: int): int16 * int =
        let idx = memIndex memory offset
        let value = Bits.toInt16 memory.Data idx
        value, offset + 2

    /// <summary>
    /// Reads an i16 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int16 value and the new offset</returns>
    let inline readI16Array (bytes: byte[]) (offset: int): int16 * int =
        let value = Bits.toInt16 bytes offset
        value, offset + 2

    /// <summary>
    /// Reads an i32 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int32 value and the new offset</returns>
    let inline readI32 (memory: Memory) (offset: int): int32 * int =
        let idx = memIndex memory offset
        let value = Bits.toInt32 memory.Data idx
        value, offset + 4

    /// <summary>
    /// Reads an i32 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int32 value and the new offset</returns>
    let inline readI32Array (bytes: byte[]) (offset: int): int32 * int =
        let value = Bits.toInt32 bytes offset
        value, offset + 4

    /// <summary>
    /// Reads an i64 value in little-endian format
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int64 value and the new offset</returns>
    let inline readI64 (memory: Memory) (offset: int): int64 * int =
        let idx = memIndex memory offset
        let value = Bits.toInt64 memory.Data idx
        value, offset + 8

    /// <summary>
    /// Reads an i64 value in little-endian format from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The int64 value and the new offset</returns>
    let inline readI64Array (bytes: byte[]) (offset: int): int64 * int =
        let value = Bits.toInt64 bytes offset
        value, offset + 8

    /// <summary>
    /// Reads an f32 (float32) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The float32 value and the new offset</returns>
    let inline readF32 (memory: Memory) (offset: int): float32 * int =
        match readI32 memory offset with
        | (bits, newOffset) ->
            let value = Bits.int32BitsToFloat32 bits
            value, newOffset

    /// <summary>
    /// Reads an f32 (float32) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The float32 value and the new offset</returns>
    let inline readF32Array (bytes: byte[]) (offset: int): float32 * int =
        match readI32Array bytes offset with
        | (bits, newOffset) -> Bits.int32BitsToFloat32 bits, newOffset

    /// <summary>
    /// Reads an f64 (double) value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The double value and the new offset</returns>
    let inline readF64 (memory: Memory) (offset: int): float * int =
        match readI64 memory offset with
        | (bits, newOffset) ->
            let value = Bits.int64BitsToFloat64 bits
            value, newOffset

    /// <summary>
    /// Reads an f64 (double) value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The double value and the new offset</returns>
    let inline readF64Array (bytes: byte[]) (offset: int): float * int =
        match readI64Array bytes offset with
        | (bits, newOffset) -> Bits.int64BitsToFloat64 bits, newOffset

    /// <summary>
    /// Reads a boolean value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The offset to read at</param>
    /// <returns>The boolean value and the new offset</returns>
    /// <remarks>Throws when the byte is not 0 or 1</remarks>
    let inline readBool (memory: Memory) (offset: int): bool * int =
        let b = memory.Data.[memIndex memory offset]
        match b with
        | 0uy -> false, offset + 1
        | 1uy -> true, offset + 1
        | _ -> failwith $"Invalid boolean value: {b}"

    /// <summary>
    /// Reads a boolean value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The boolean value and the new offset</returns>
    /// <remarks>Throws when the byte is not 0 or 1</remarks>
    let inline readBoolArray (bytes: byte[]) (offset: int): bool * int =
        let b = bytes.[offset]
        match b with
        | 0uy -> false, offset + 1
        | 1uy -> true, offset + 1
        | _ -> failwith $"Invalid boolean value: {b}"

    /// <summary>
    /// Reads a string value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The string value and the new offset</returns>
    let inline readString (memory: Memory) (offset: int): string * int =
        // Read length
        match readUInt memory offset with
        | (length, currentOffset) ->
            if length = 0UL then
                "", currentOffset
            else
                // Extract bytes and decode (manual copy since slicing not supported)
                let startIdx = memIndex memory currentOffset
                let len = int length
                let strBytes = Array.zeroCreate len
                for i = 0 to len - 1 do
                    strBytes.[i] <- memory.Data.[startIdx + i]
                let str = String.fromBytes strBytes
                str, currentOffset + len

    /// <summary>
    /// Reads a string value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The string value and the new offset</returns>
    let inline readStringArray (bytes: byte[]) (offset: int): string * int =
        // Read length
        match readUIntArray bytes offset with
        | (lengthVal, currentOffset) ->
            let length = int lengthVal
            if length = 0 then
                "", currentOffset
            else
                // Extract bytes and decode (manual copy since slicing not supported)
                let strBytes = Array.zeroCreate length
                for i = 0 to length - 1 do
                    strBytes.[i] <- bytes.[currentOffset + i]
                let str = String.fromBytes strBytes
                str, currentOffset + length

    /// <summary>
    /// Reads a variable-length data value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readData (memory: Memory) (offset: int): byte[] * int =
        // Read length
        match readUInt memory offset with
        | (length, currentOffset) ->
            if length = 0UL then
                Array.zeroCreate 0, currentOffset
            else
                // Extract bytes (manual copy since slicing not supported)
                let startIdx = memIndex memory currentOffset
                let len = int length
                let result = Array.zeroCreate len
                for i = 0 to len - 1 do
                    result.[i] <- memory.Data.[startIdx + i]
                result, currentOffset + len

    /// <summary>
    /// Reads a variable-length data value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readDataArray (bytes: byte[]) (offset: int): byte[] * int =
        // Read length
        match readUIntArray bytes offset with
        | (lengthVal, currentOffset) ->
            let length = int lengthVal
            if length = 0 then
                Array.zeroCreate 0, currentOffset
            else
                // Extract bytes (manual copy since slicing not supported)
                let result = Array.zeroCreate length
                for i = 0 to length - 1 do
                    result.[i] <- bytes.[currentOffset + i]
                result, currentOffset + length

    /// <summary>
    /// Reads fixed-length data
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The length of data to read</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readFixedData (memory: Memory) (offset: int) (length: int): byte[] * int =
        // Extract bytes (manual copy since slicing not supported)
        let startIdx = memIndex memory offset
        let result = Array.zeroCreate length
        for i = 0 to length - 1 do
            result.[i] <- memory.Data.[startIdx + i]
        result, offset + length

    /// <summary>
    /// Reads fixed-length data from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The length of data to read</param>
    /// <returns>The byte array and the new offset</returns>
    let inline readFixedDataArray (bytes: byte[]) (offset: int) (length: int): byte[] * int =
        // Extract bytes (manual copy since slicing not supported)
        let result = Array.zeroCreate length
        for i = 0 to length - 1 do
            result.[i] <- bytes.[offset + i]
        result, offset + length

    /// <summary>
    /// Reads an optional value using F#'s ValueOption for zero-allocation
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read the value if present</param>
    /// <returns>The optional value and the new offset</returns>
    /// <remarks>Throws when the tag is not 0 or 1</remarks>
    let inline readOptional (memory: Memory)
                     (offset: int)
                     (readValue: Memory -> int -> 'a * int):
                     ValueOption<'a> * int =
        let tag = memory.Data.[memIndex memory offset]
        let currentOffset = offset + 1

        match tag with
        | 0uy -> ValueNone, currentOffset
        | 1uy ->
            match readValue memory currentOffset with
            | (value, newOffset) -> ValueSome value, newOffset
        | _ -> failwith $"Invalid optional tag: {tag}"

    /// <summary>
    /// Reads an optional value from a byte array using F#'s ValueOption
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read the value if present (returns value and new offset)</param>
    /// <returns>The optional value and the new offset</returns>
    /// <remarks>Throws when the tag is not 0 or 1</remarks>
    let inline readOptionalArray (bytes: byte[])
                        (offset: int)
                        (readValue: byte[] -> int -> 'a * int):
                        ValueOption<'a> * int =
        let tag = bytes.[offset]
        let currentOffset = offset + 1

        match tag with
        | 0uy -> ValueNone, currentOffset
        | 1uy ->
            match readValue bytes currentOffset with
            | (value, newOffset) -> ValueSome value, newOffset
        | _ -> failwith $"Invalid optional tag: {tag}"

    /// <summary>
    /// Reads a list of values (returns as array for FNCS compatibility)
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The array of values and the new offset</returns>
    let inline readList (memory: Memory)
                 (offset: int)
                 (readValue: Memory -> int -> 'a * int):
                 'a array * int =
        // Read count
        match readUInt memory offset with
        | (count, currentOffset) ->
            let countInt = int count
            // Read each value into array
            let values = Array.zeroCreate countInt
            let mutable currentOff = currentOffset

            for i = 0 to countInt - 1 do
                match readValue memory currentOff with
                | (value, newOffset) ->
                    values.[i] <- value
                    currentOff <- newOffset

            values, currentOff

    /// <summary>
    /// Reads a list of values from a byte array (returns as array for FNCS compatibility)
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValue">A function to read each value (returns value and new offset)</param>
    /// <returns>The array of values and the new offset</returns>
    let inline readListArray (bytes: byte[])
                    (offset: int)
                    (readValue: byte[] -> int -> 'a * int):
                    'a array * int =
        // Read count
        match readUIntArray bytes offset with
        | (countVal, currentOffset) ->
            let count = int countVal
            // Read each value into array
            let values = Array.zeroCreate count
            let mutable currentOff = currentOffset

            for i = 0 to count - 1 do
                match readValue bytes currentOff with
                | (value, newOffset) ->
                    values.[i] <- value
                    currentOff <- newOffset

            values, currentOff

    /// <summary>
    /// Reads a fixed-length list of values (returns as array for FNCS compatibility)
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The number of elements to read</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The array of values and the new offset</returns>
    let inline readFixedList (memory: Memory)
                      (offset: int)
                      (length: int)
                      (readValue: Memory -> int -> 'a * int):
                      'a array * int =
        // Read each value (no length prefix)
        let values = Array.zeroCreate length
        let mutable currentOffset = offset

        for i = 0 to length - 1 do
            match readValue memory currentOffset with
            | (value, newOffset) ->
                values.[i] <- value
                currentOffset <- newOffset

        values, currentOffset

    /// <summary>
    /// Reads a fixed-length list of values from a byte array (returns as array for FNCS compatibility)
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="length">The number of elements to read</param>
    /// <param name="readValue">A function to read each value (returns value and new offset)</param>
    /// <returns>The array of values and the new offset</returns>
    let inline readFixedListArray (bytes: byte[])
                         (offset: int)
                         (length: int)
                         (readValue: byte[] -> int -> 'a * int):
                         'a array * int =
        // Read each value (no length prefix)
        let values = Array.zeroCreate length
        let mutable currentOffset = offset

        for i = 0 to length - 1 do
            match readValue bytes currentOffset with
            | (value, newOffset) ->
                values.[i] <- value
                currentOffset <- newOffset

        values, currentOffset

    /// <summary>
    /// Reads a map of key-value pairs (returns as array of tuples for FNCS compatibility)
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readKey">A function to read each key</param>
    /// <param name="readValue">A function to read each value</param>
    /// <returns>The array of key-value pairs and the new offset</returns>
    let inline readMap (memory: Memory)
                (offset: int)
                (readKey: Memory -> int -> 'k * int)
                (readValue: Memory -> int -> 'v * int):
                ('k * 'v) array * int =
        // Read count
        match readUInt memory offset with
        | (count, currentOffset) ->
            let countInt = int count
            // Read each key-value pair
            let pairs = Array.zeroCreate countInt
            let mutable currentOff = currentOffset

            for i = 0 to countInt - 1 do
                match readKey memory currentOff with
                | (key, keyOffset) ->
                    match readValue memory keyOffset with
                    | (value, valueOffset) ->
                        pairs.[i] <- (key, value)
                        currentOff <- valueOffset

            pairs, currentOff

    /// <summary>
    /// Reads a map of key-value pairs from a byte array (returns as array of tuples for FNCS compatibility)
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readKey">A function to read each key (returns key and new offset)</param>
    /// <param name="readValue">A function to read each value (returns value and new offset)</param>
    /// <returns>The array of key-value pairs and the new offset</returns>
    let inline readMapArray (bytes: byte[])
                   (offset: int)
                   (readKey: byte[] -> int -> 'k * int)
                   (readValue: byte[] -> int -> 'v * int):
                   ('k * 'v) array * int =
        // Read count
        match readUIntArray bytes offset with
        | (countVal, currentOffset) ->
            let count = int countVal
            // Read each key-value pair
            let pairs = Array.zeroCreate count
            let mutable currentOff = currentOffset

            for i = 0 to count - 1 do
                match readKey bytes currentOff with
                | (key, keyOffset) ->
                    match readValue bytes keyOffset with
                    | (value, valueOffset) ->
                        pairs.[i] <- (key, value)
                        currentOff <- valueOffset

            pairs, currentOff

    /// <summary>
    /// Reads a union value
    /// </summary>
    /// <param name="memory">The memory region to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValueForTag">A function to read a value based on its tag</param>
    /// <returns>The union tag, value and the new offset</returns>
    let inline readUnion (memory: Memory)
                  (offset: int)
                  (readValueForTag: uint32 -> Memory -> int -> 'a * int):
                  uint32 * 'a * int =
        // Read tag
        match readUInt memory offset with
        | (tagVal, currentOffset) ->
            let tag = uint32 tagVal
            // Read value based on tag
            match readValueForTag tag memory currentOffset with
            | (value, finalOffset) -> tag, value, finalOffset

    /// <summary>
    /// Reads a union value from a byte array
    /// </summary>
    /// <param name="bytes">The byte array to read from</param>
    /// <param name="offset">The starting offset</param>
    /// <param name="readValueForTag">A function to read a value based on its tag (returns value and new offset)</param>
    /// <returns>The union tag, value and the new offset</returns>
    let inline readUnionArray (bytes: byte[])
                     (offset: int)
                     (readValueForTag: uint32 -> byte[] -> int -> 'a * int):
                     uint32 * 'a * int =
        // Read tag
        match readUIntArray bytes offset with
        | (tagVal, currentOffset) ->
            let tag = uint32 tagVal
            // Read value based on tag
            match readValueForTag tag bytes currentOffset with
            | (value, finalOffset) -> tag, value, finalOffset
