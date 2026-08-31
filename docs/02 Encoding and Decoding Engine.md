# Encoding and Decoding Engine

The heart of BAREWire is its encoding and decoding engine, which handles the conversion between native Clef values and their binary BARE representation. This document explains the core encoding and decoding functions and how they work together.

## Design Philosophy

The encoding/decoding engine is designed with these principles in mind:

1. **Zero allocations** where possible
2. **Type safety** through the native type universe
3. **Composability** of encoding/decoding functions
4. **Performance** through careful optimization

## Primitive Encoding

Primitive types form the foundation of the encoding system:

```fsharp
/// Encoding functions for primitive types
module Encoder =
    /// Write a uint value using ULEB128 encoding
    let writeUInt (buffer: Buffer<'T>) (value: uint64): unit =
        let mutable val' = value
        while val' >= 128UL do
            buffer.Write(byte (val' ||| 128UL))
            val' <- val' >>> 7
        buffer.Write(byte val')
    
    /// Write an int value using zigzag ULEB128 encoding
    let writeInt (buffer: Buffer<'T>) (value: int64): unit =
        // Zigzag encoding: (n << 1) ^ (n >> 63)
        let zigzag = (value <<< 1) ^^^ (value >>> 63)
        writeUInt buffer (uint64 zigzag)
    
    /// Write a boolean value
    let writeBool (buffer: Buffer<'T>) (value: bool): unit =
        buffer.Write(if value then 1uy else 0uy)
    
    /// Write a string value
    let writeString (buffer: Buffer<'T>) (value: string): unit =
        // Get UTF-8 bytes
        // A Clef string IS a length-carried memref of UTF-8 bytes: no
        // conversion exists — toBytes is an identity view of the same memory
        let bytes = String.toBytes value
        // Write length as uint
        writeUInt buffer (uint64 bytes.Length)
        // Write bytes
        buffer.WriteSpan(ReadOnlySpan(bytes))
    
    /// Write a data value
    let writeData (buffer: Buffer<'T>) (value: byte[]): unit =
        // Write length as uint
        writeUInt buffer (uint64 value.Length)
        // Write bytes
        buffer.WriteSpan(ReadOnlySpan(value))
    
    /// Write fixed-length data
    let writeFixedData (buffer: Buffer<'T>) (value: byte[]) (length: int): unit =
        if value.Length <> length then
            failwith $"Fixed data length mismatch: expected {length}, got {value.Length}"
        
        // Write bytes directly (no length prefix)
        buffer.WriteSpan(ReadOnlySpan(value))
```

## Primitive Decoding

Decoding is the inverse of encoding, reading binary data and converting it to native values:

```fsharp
/// Decoding functions for primitive types
module Decoder =
    /// Read a uint value using ULEB128 encoding
    let readUInt (memory: Memory<'T, 'region>) (offset: int<offset>): uint64 * int<offset> =
        let mutable result = 0UL
        let mutable shift = 0
        let mutable currentOffset = offset
        let mutable currentByte = 0uy
        
        repeat {
            currentByte <- memory.Data.[int currentOffset]
            currentOffset <- currentOffset + 1<offset>
            
            result <- result ||| ((uint64 (currentByte &&& 0x7Fuy)) <<< shift)
            shift <- shift + 7
        } until (currentByte &&& 0x80uy = 0uy || shift >= 64)
        
        result, currentOffset
    
    /// Read an int value using zigzag ULEB128 encoding
    let readInt (memory: Memory<'T, 'region>) (offset: int<offset>): int64 * int<offset> =
        let uintVal, newOffset = readUInt memory offset
        
        // Zigzag decoding: (n >>> 1) ^ -(n &&& 1)
        let value = (int64 uintVal >>> 1) ^^^ (-(int64 (uintVal &&& 1UL)))
        value, newOffset
    
    /// Read a boolean value
    let readBool (memory: Memory<'T, 'region>) (offset: int<offset>): bool * int<offset> =
        let b = memory.Data.[int offset]
        match b with
        | 0uy -> false, offset + 1<offset>
        | 1uy -> true, offset + 1<offset>
        | _ -> failwith $"Invalid boolean value: {b}"
    
    /// Read a string value
    let readString (memory: Memory<'T, 'region>) (offset: int<offset>): string * int<offset> =
        // Read length
        let length, currentOffset = readUInt memory offset
        
        // Read bytes
        let bytes = memory.Data.[int currentOffset .. int currentOffset + int length - 1]
        let str = String.fromBytes bytes  // identity: both are memref<?xi8>
        
        str, currentOffset + (int length * 1<offset>)
    
    /// Read a data value
    let readData (memory: Memory<'T, 'region>) (offset: int<offset>): byte[] * int<offset> =
        // Read length
        let length, currentOffset = readUInt memory offset
        
        // Extract bytes
        let bytes = memory.Data.[int currentOffset .. int currentOffset + int length - 1]
        
        bytes, currentOffset + (int length * 1<offset>)
    
    /// Read fixed-length data
    let readFixedData (memory: Memory<'T, 'region>) (offset: int<offset>) (length: int): byte[] * int<offset> =
        // Extract bytes
        let bytes = memory.Data.[int offset .. int offset + length - 1]
        
        bytes, offset + (length * 1<offset>)
```

## Aggregate Type Handling

Beyond primitives, BAREWire handles aggregate types like lists, maps, and unions:

```fsharp
module Encoder =
    // ... primitive encoders ...
    
    /// Write an optional value
    let writeOptional (buffer: Buffer<'T>) (valueOpt: 'a option) (writeValue: Buffer<'T> -> 'a -> unit): unit =
        match valueOpt with
        | None -> buffer.Write(0uy)  // Not present
        | Some value ->
            buffer.Write(1uy)  // Present
            writeValue buffer value
    
    /// Write a list
    let writeList (buffer: Buffer<'T>) (values: 'a seq) (writeValue: Buffer<'T> -> 'a -> unit): unit =
        // Write count as uint
        let count = Seq.length values
        writeUInt buffer (uint64 count)
        
        // Write each value
        for value in values do
            writeValue buffer value
    
    /// Write a fixed-length list
    let writeFixedList (buffer: Buffer<'T>) (values: 'a seq) (length: int) (writeValue: Buffer<'T> -> 'a -> unit): unit =
        let count = Seq.length values
        if count <> length then
            failwith $"Fixed list length mismatch: expected {length}, got {count}"
        
        // Write each value (no length prefix)
        for value in values do
            writeValue buffer value
    
    /// Write a map
    let writeMap (buffer: Buffer<'T>) 
                (entries: ('k * 'v) seq) 
                (writeKey: Buffer<'T> -> 'k -> unit) 
                (writeValue: Buffer<'T> -> 'v -> unit): unit =
        // Write count as uint
        let count = Seq.length entries
        writeUInt buffer (uint64 count)
        
        // Write each key-value pair
        for key, value in entries do
            writeKey buffer key
            writeValue buffer value
    
    /// Write a union value
    let writeUnion (buffer: Buffer<'T>) (tag: uint) (value: 'a) (writeValue: Buffer<'T> -> 'a -> unit): unit =
        // Write tag as uint
        writeUInt buffer (uint64 tag)
        
        // Write value
        writeValue buffer value

module Decoder =
    // ... primitive decoders ...
    
    /// Read an optional value
    let readOptional (memory: Memory<'T, 'region>) 
                     (offset: int<offset>) 
                     (readValue: Memory<'T, 'region> -> int<offset> -> 'a * int<offset>): 
                     'a option * int<offset> =
        let tag = memory.Data.[int offset]
        let currentOffset = offset + 1<offset>
        
        match tag with
        | 0uy -> None, currentOffset
        | 1uy -> 
            let value, newOffset = readValue memory currentOffset
            Some value, newOffset
        | _ -> failwith $"Invalid optional tag: {tag}"
    
    /// Read a list
    let readList (memory: Memory<'T, 'region>) 
                 (offset: int<offset>) 
                 (readValue: Memory<'T, 'region> -> int<offset> -> 'a * int<offset>): 
                 'a list * int<offset> =
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
    
    /// Read a fixed-length list
    let readFixedList (memory: Memory<'T, 'region>) 
                      (offset: int<offset>) 
                      (length: int) 
                      (readValue: Memory<'T, 'region> -> int<offset> -> 'a * int<offset>): 
                      'a list * int<offset> =
        // Read each value (no length prefix)
        let mutable values = []
        let mutable currentOffset = offset
        
        for _ in 1..length do
            let value, newOffset = readValue memory currentOffset
            values <- value :: values
            currentOffset <- newOffset
        
        List.rev values, currentOffset
    
    /// Read a map
    let readMap (memory: Memory<'T, 'region>) 
                (offset: int<offset>) 
                (readKey: Memory<'T, 'region> -> int<offset> -> 'k * int<offset>) 
                (readValue: Memory<'T, 'region> -> int<offset> -> 'v * int<offset>): 
                Map<'k, 'v> * int<offset> =
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
    
    /// Read a union value
    let readUnion (memory: Memory<'T, 'region>) 
                  (offset: int<offset>) 
                  (readValueForTag: uint -> Memory<'T, 'region> -> int<offset> -> 'a * int<offset>): 
                  'a * int<offset> =
        // Read tag
        let tagVal, currentOffset = readUInt memory offset
        let tag = uint tagVal
        
        // Read value based on tag
        readValueForTag tag memory currentOffset
```

## Schema-Based Encoding/Decoding

The above functions handle specific types, but BAREWire also provides schema-based encoding and decoding:

```fsharp
/// Schema-based encoding and decoding
module Schema =
    /// Encode a value based on its schema
    let encode (schema: SchemaDefinition<validated>) (value: 'T) (buffer: Buffer<'a>): unit =
        let rec encodeWithType (typ: Type) (value: obj) (buffer: Buffer<'a>): unit =
            match typ with
            | Primitive primType ->
                match primType with
                | UInt -> Encoder.writeUInt buffer (unbox<uint64> value)
                | Int -> Encoder.writeInt buffer (unbox<int64> value)
                | Bool -> Encoder.writeBool buffer (unbox<bool> value)
                | String -> Encoder.writeString buffer (unbox<string> value)
                | Data -> Encoder.writeData buffer (unbox<byte[]> value)
                | FixedData length -> Encoder.writeFixedData buffer (unbox<byte[]> value) length
                | Void -> ()
                // ... other primitive types ...
                
            | Aggregate aggType ->
                match aggType with
                | Optional innerType ->
                    let opt = unbox<option<obj>> value
                    Encoder.writeOptional buffer opt (fun buf v -> encodeWithType innerType v buf)
                
                | List innerType ->
                    let list = unbox<obj seq> value
                    Encoder.writeList buffer list (fun buf v -> encodeWithType innerType v buf)
                
                | FixedList (innerType, length) ->
                    let list = unbox<obj seq> value
                    Encoder.writeFixedList buffer list length (fun buf v -> encodeWithType innerType v buf)
                
                | Map (keyType, valueType) ->
                    let map = unbox<(obj * obj) seq> value
                    Encoder.writeMap buffer map 
                        (fun buf k -> encodeWithType keyType k buf)
                        (fun buf v -> encodeWithType valueType v buf)
                
                | Union cases ->
                    let (tag, innerValue) = unbox<uint * obj> value
                    let innerType = Map.find tag cases
                    Encoder.writeUnion buffer tag innerValue (fun buf v -> encodeWithType innerType v buf)
                
                | Struct fields ->
                    let record = value
                    for field in fields do
                        let fieldValue = getRecordField record field.Name
                        encodeWithType field.Type fieldValue buffer
            
            | UserDefined typeName ->
                // Look up the type in the schema
                let actualType = Map.find typeName schema.Types
                encodeWithType actualType value buffer
        
        // Start encoding with the root type
        let rootType = Map.find schema.Root schema.Types
        encodeWithType rootType (box value) buffer
    
    /// Decode a value based on its schema
    let decode<'T> (schema: SchemaDefinition<validated>) (memory: Memory<'a, 'region>): 'T * int<offset> =
        let rec decodeWithType (typ: Type) (memory: Memory<'a, 'region>) (offset: int<offset>): obj * int<offset> =
            match typ with
            | Primitive primType ->
                match primType with
                | UInt -> 
                    let value, newOffset = Decoder.readUInt memory offset
                    box value, newOffset
                | Int -> 
                    let value, newOffset = Decoder.readInt memory offset
                    box value, newOffset
                | Bool -> 
                    let value, newOffset = Decoder.readBool memory offset
                    box value, newOffset
                | String -> 
                    let value, newOffset = Decoder.readString memory offset
                    box value, newOffset
                | Data -> 
                    let value, newOffset = Decoder.readData memory offset
                    box value, newOffset
                | FixedData length -> 
                    let value, newOffset = Decoder.readFixedData memory offset length
                    box value, newOffset
                | Void -> box (), offset
                // ... other primitive types ...
                
            | Aggregate aggType ->
                match aggType with
                | Optional innerType ->
                    let optValue, newOffset = 
                        Decoder.readOptional memory offset 
                            (fun mem off -> decodeWithType innerType mem off)
                    box optValue, newOffset
                
                | List innerType ->
                    let list, newOffset = 
                        Decoder.readList memory offset 
                            (fun mem off -> decodeWithType innerType mem off)
                    box list, newOffset
                
                | FixedList (innerType, length) ->
                    let list, newOffset = 
                        Decoder.readFixedList memory offset length
                            (fun mem off -> decodeWithType innerType mem off)
                    box list, newOffset
                
                | Map (keyType, valueType) ->
                    let map, newOffset = 
                        Decoder.readMap memory offset
                            (fun mem off -> decodeWithType keyType mem off)
                            (fun mem off -> decodeWithType valueType mem off)
                    box map, newOffset
                
                | Union cases ->
                    let tagVal, tagOffset = Decoder.readUInt memory offset
                    let tag = uint tagVal
                    
                    if not (Map.containsKey tag cases) then
                        failwith $"Unknown union tag: {tag}"
                        
                    let innerType = Map.find tag cases
                    let value, newOffset = decodeWithType innerType memory tagOffset
                    box (tag, value), newOffset
                
                | Struct fields ->
                    // Create a record with the fields
                    let mutable fieldValues = Map.empty
                    let mutable currentOffset = offset
                    
                    for field in fields do
                        let value, newOffset = decodeWithType field.Type memory currentOffset
                        fieldValues <- Map.add field.Name value fieldValues
                        currentOffset <- newOffset
                    
                    let record = createRecord fieldValues
                    box record, currentOffset
            
            | UserDefined typeName ->
                // Look up the type in the schema
                let actualType = Map.find typeName schema.Types
                decodeWithType actualType memory offset
        
        // Start decoding with the root type
        let rootType = Map.find schema.Root schema.Types
        let value, finalOffset = decodeWithType rootType memory 0<offset>
        unbox<'T> value, finalOffset
```

## Type Safety through Measures

Measures are structure of the native type universe, so they flow through encoding and decoding with no wrapping layer: a value of type `'T<'m>` encodes as its representation `'T`, and the measure is compile-time dimensional structure the compiler carries — nothing is tagged or untagged, at runtime or in source.

```fsharp
/// Encoding and decoding are measure-transparent: the schema speaks to the
/// representation, the NTU carries the dimension. Passing a value with the
/// wrong measure is a compile error at the call site.
let encodeMeasured<'T, [<Measure>] 'm>
                   (schema: SchemaDefinition<validated>)
                   (value: 'T<'m>)
                   (buffer: Buffer<'a>): unit =
    Schema.encode schema value buffer

let decodeMeasured<'T, [<Measure>] 'm>
                   (schema: SchemaDefinition<validated>)
                   (memory: Memory<'a, 'region>): 'T<'m> * int<offset> =
    Schema.decode<'T<'m>> schema memory
```

## Zero-Copy Decoding

A key feature of BAREWire is its ability to provide zero-copy views of data structures:

```fsharp
/// Zero-copy memory view operations
module ZeroCopy =
    /// Create a view over a memory region for a specific schema
    let createView<'T, [<Measure>] 'region> 
                  (memory: Memory<'T, 'region>) 
                  (schema: SchemaDefinition<validated>): MemoryView<'T, 'region> =
        { Memory = memory; Schema = schema }
    
    /// Access a field within a struct without copying
    let getField<'T, 'Field, [<Measure>] 'region> 
                (view: MemoryView<'T, 'region>) 
                (path: string list): 'Field =
        // Navigate the schema to find the field
        // and return a decoded value or memory view
        // without copying the data
        failwith "Not implemented"
```

These encoding and decoding engines form the core of BAREWire's functionality, enabling efficient and type-safe binary data processing. The design emphasizes performance, minimal allocations, and seamless integration with the native type universe.