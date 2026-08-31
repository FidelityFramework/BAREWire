# Memory Mapping

BAREWire's memory mapping capabilities enable direct access to structured binary data without copying, making it ideal for interacting with native code, memory-mapped files, or shared memory regions. This document explains BAREWire's approach to memory mapping and how to use it effectively.

## Core Concepts

Memory mapping in BAREWire revolves around a few key concepts:

1. **Memory Regions**: Contiguous areas of memory with a specific layout
2. **Memory Views**: Type-safe views over memory regions based on schemas
3. **Address Calculation**: Determining the location of specific fields within a memory region
4. **Zero-Copy Access**: Reading and writing data directly in memory without intermediate copies

## Memory Regions

Memory regions represent contiguous areas of memory:

```fsharp
/// Memory region types and functions
module MemoryRegion =
    /// A memory region with type safety
    type Region<'T, [<Measure>] 'region> =
        { 
            /// The underlying byte array
            Data: byte[]
            
            /// The starting offset of the region
            Offset: int<offset>
            
            /// The length of the region in bytes
            Length: int<bytes>
            
            /// The schema associated with this region
            Schema: SchemaDefinition<validated>
        }
    
    /// Create a new memory region from a byte array
    let fromArray<'T> 
                 (data: byte[]) 
                 (schema: SchemaDefinition<validated>): Region<'T, region> =
        {
            Data = data
            Offset = 0<offset>
            Length = data.Length * 1<bytes>
            Schema = schema
        }
    
    /// Create a slice of an existing memory region
    let slice<'T, 'U, [<Measure>] 'region> 
             (region: Region<'T, 'region>) 
             (offset: int<offset>) 
             (length: int<bytes>) 
             (schema: SchemaDefinition<validated>): Region<'U, 'region> =
        if int offset + int length > int region.Length then
            failwith "Slice extends beyond region bounds"
            
        {
            Data = region.Data
            Offset = region.Offset + offset
            Length = length
            Schema = schema
        }
    
    /// Get the absolute address of a location within a region
    let getAddress<'T, [<Measure>] 'region> 
                  (region: Region<'T, 'region>) 
                  (relativeOffset: int<offset>): Address<'region> =
        if int relativeOffset >= int region.Length then
            failwith "Address outside region bounds"
            
        { Offset = region.Offset + relativeOffset }
```

## Memory Views

Memory views provide type-safe access to memory regions based on schemas:

```fsharp
/// Memory view types and functions
module MemoryView =
    /// A view over a memory region with a specific schema
    type View<'T, [<Measure>] 'region> =
        {
            /// The underlying memory region
            Region: Region<'T, 'region>
            
            /// Field offset cache for faster access
            FieldOffsets: Map<string, int<offset>>
        }
    
    /// Create a new memory view from a region
    let create<'T, [<Measure>] 'region> 
              (region: Region<'T, 'region>): View<'T, 'region> =
        // Calculate field offsets based on the schema
        let fieldOffsets = calculateFieldOffsets region.Schema
        
        {
            Region = region
            FieldOffsets = fieldOffsets
        }
    
    /// Get a field value from a view
    let getField<'T, 'Field, [<Measure>] 'region> 
                (view: View<'T, 'region>) 
                (fieldPath: string list): 'Field =
        // Navigate the path to find the field
        let fieldOffset = resolveFieldPath view fieldPath
        
        // Decode the field value
        let fieldType = getFieldType view.Region.Schema fieldPath
        let memory = {
            Data = view.Region.Data
            Offset = view.Region.Offset + fieldOffset
            Length = view.Region.Length - fieldOffset
        }
        
        // Decode the value based on its type
        let value, _ = Decoder.decodeWithType fieldType memory 0<offset>
        unbox<'Field> value
    
    /// Set a field value in a view
    let setField<'T, 'Field, [<Measure>] 'region> 
                (view: View<'T, 'region>) 
                (fieldPath: string list) 
                (value: 'Field): unit =
        // Navigate the path to find the field
        let fieldOffset = resolveFieldPath view fieldPath
        
        // Get the field type
        let fieldType = getFieldType view.Region.Schema fieldPath
        
        // Create a buffer for encoding
        let fieldSize = SchemaAnalysis.getTypeSize view.Region.Schema fieldType
        let buffer = {
            Data = Array.zeroCreate (int fieldSize.Min + 10) // Add some extra space
            Position = 0<offset>
        }
        
        // Encode the value
        Encoder.encodeWithType fieldType (box value) buffer
        
        // Copy the encoded bytes to the memory region
        let bytesToCopy = int buffer.Position
        if bytesToCopy > int view.Region.Length - int fieldOffset then
            failwith "Field value too large for memory region"
            
        Array.Copy(buffer.Data, 0, view.Region.Data, 
                   int (view.Region.Offset + fieldOffset), bytesToCopy)
```

## Farscape Integration

BAREWire integrates with Farscape to enable memory mapping for C/C++ structures:

```fsharp
/// Farscape integration for memory mapping
module FarscapeIntegration =
    /// Convert a Farscape C struct definition to a BAREWire schema
    let createSchemaFromCStruct 
        (structName: string)
        (fields: (string * string) list): SchemaDefinition<draft> =
        
        // Start with an empty schema
        let schema = Schema.create structName
        
        // Add the struct definition
        let structFields =
            fields
            |> List.map (fun (fieldName, cType) ->
                let bareType = mapCTypeToBareType cType
                { Name = fieldName; Type = bareType })
        
        let schema = Schema.addStruct structName structFields schema
        
        // Validate the schema
        match SchemaValidation.validate schema with
        | Ok validSchema -> validSchema
        | Error errors ->
            failwith $"Invalid schema: {errors}"
    
    /// Map a C/C++ memory pointer to a BAREWire memory region
    let mapPointerToRegion<'T, [<Measure>] 'region> 
                          (pointer: nativeint) 
                          (length: int<bytes>) 
                          (schema: SchemaDefinition<validated>): Region<'T, 'region> =
        // Create a byte array that maps to the pointer's memory
        let array = Array.zeroCreate (int length)
        
        // Copy memory from pointer to array
        for i = 0 to int length - 1 do
            let bytePtr = NativeInterop.NativePtr.add 
                           (pointer |> NativeInterop.NativePtr.ofNativeInt<byte>)
                           i
            array.[i] <- NativeInterop.NativePtr.read bytePtr
        
        // Create a memory region
        {
            Data = array
            Offset = 0<offset>
            Length = length
            Schema = schema
        }
    
    /// Map a C/C++ type to a BAREWire type
    let private mapCTypeToBareType (cType: string): Type =
        match cType with
        | "char" -> Primitive U8
        | "unsigned char" -> Primitive U8
        | "short" | "short int" | "signed short" -> Primitive I16
        | "unsigned short" | "unsigned short int" -> Primitive U16
        | "int" | "signed int" -> Primitive I32
        | "unsigned int" -> Primitive U32
        | "long" | "long int" | "signed long" -> Primitive I32
        | "unsigned long" | "unsigned long int" -> Primitive U32
        | "long long" | "long long int" | "signed long long" -> Primitive I64
        | "unsigned long long" | "unsigned long long int" -> Primitive U64
        | "float" -> Primitive F32
        | "double" -> Primitive F64
        | "bool" -> Primitive Bool
        | t when t.EndsWith("*") -> Primitive U64 // Pointers are U64 on 64-bit systems
        | t when t.Contains("[") && t.EndsWith("]") ->
            // Array type
            let baseCType = t.Substring(0, t.IndexOf("[")).Trim()
            let lengthStr = t.Substring(t.IndexOf("[") + 1, t.IndexOf("]") - t.IndexOf("[") - 1)
            let length = int lengthStr
            let baseType = mapCTypeToBareType baseCType
            Aggregate (FixedList (baseType, length))
        | _ -> failwith $"Unsupported C type: {cType}"
```

## Measure Integration

Measures are native to the type universe, so type safety for memory mapping needs no integration layer: measured field types are declared once, on the record and in the schema, and the view API flows them. There is no tagging ceremony because there is nothing to tag — the dimension is part of the type.

```fsharp
/// Units of measure for specific memory regions
[<Measure>] type structRegion
[<Measure>] type headerRegion
[<Measure>] type dataRegion

/// Units of measure for field types
[<Measure>] type userId
[<Measure>] type timestamp
[<Measure>] type messageId

/// The record declares its measures; every access site inherits them
type MessageHeader = {
    Id: string<messageId>
    UserId: string<userId>
    Timestamp: int64<timestamp>
}

let example () =
    // Define a schema for the message header (field representations; the
    // measures ride on the record's types)
    let headerSchema =
        schema "MessageHeader"
        |> withType "MessageHeader" (struct' [
            field "id" string
            field "userId" string
            field "timestamp" int
        ])
        |> SchemaValidation.validate
        |> function
           | Ok schema -> schema
           | Error errors -> failwith $"Invalid schema: {errors}"

    // Create a memory region and a typed view over it
    let headerData = Array.zeroCreate 128
    let headerRegion = MemoryRegion.fromArray<MessageHeader> headerData headerSchema
    let headerView = MemoryView.create headerRegion

    // Set fields: literals adopt the measured field types; a value carrying
    // the wrong measure is a compile error at the call site
    MemoryView.setField headerView ["id"] "msg-123"
    MemoryView.setField headerView ["userId"] "user-456"
    MemoryView.setField headerView ["timestamp"] 1631234567L<timestamp>

    // Get fields: the measures come back with the values
    let id: string<messageId> = MemoryView.getField headerView ["id"]
    let user: string<userId> = MemoryView.getField headerView ["userId"]
    let time: int64<timestamp> = MemoryView.getField headerView ["timestamp"]

    // MemoryView.setField headerView ["id"] user
    //   ^ does not compile: string<userId> is not string<messageId>

    printfn "Message %s from user %s at time %d" id user time
```

## Shared Memory for IPC

BAREWire supports shared memory for inter-process communication:

```fsharp
/// Shared memory for IPC
module SharedMemory =
    /// A shared memory region with type safety
    type SharedRegion<'T, [<Measure>] 'region> =
        {
            /// The memory region
            Region: MemoryRegion.Region<'T, 'region>
            
            /// The name of the shared memory region
            Name: string
            
            /// The handle to the shared memory
            Handle: nativeint
        }
    
    /// Create a new shared memory region
    let create<'T> 
              (name: string) 
              (size: int<bytes>) 
              (schema: SchemaDefinition<validated>): SharedRegion<'T, region> =
        // Platform-specific shared memory creation
        // This is a simplified version
        
        // Create the backing array
        let array = Array.zeroCreate (int size)
        
        // Create the memory region
        let region = {
            Data = array
            Offset = 0<offset>
            Length = size
            Schema = schema
        }
        
        {
            Region = region
            Name = name
            Handle = 0n // Would be a real handle on a real system
        }
    
    /// Open an existing shared memory region
    let open'<'T> 
              (name: string) 
              (schema: SchemaDefinition<validated>): SharedRegion<'T, region> =
        // Platform-specific shared memory opening
        // This is a simplified version
        
        // Find the existing shared memory
        let size = 1024<bytes> // Would be determined from the actual shared memory
        
        // Create the backing array
        let array = Array.zeroCreate (int size)
        
        // Create the memory region
        let region = {
            Data = array
            Offset = 0<offset>
            Length = size
            Schema = schema
        }
        
        {
            Region = region
            Name = name
            Handle = 0n // Would be a real handle on a real system
        }
    
    /// Get a memory view for a shared region
    let getView<'T, [<Measure>] 'region> 
              (shared: SharedRegion<'T, 'region>): MemoryView.View<'T, 'region> =
        MemoryView.create shared.Region
    
    /// Close a shared memory region
    let close<'T, [<Measure>] 'region> 
             (shared: SharedRegion<'T, 'region>): unit =
        // Platform-specific shared memory closing
        // This is a simplified version
        ()
```

## Memory-Mapped Files

BAREWire also provides support for memory-mapped files:

```fsharp
/// Memory-mapped file support
module MemoryMappedFile =
    /// A memory-mapped file with type safety
    type MappedFile<'T, [<Measure>] 'region> =
        {
            /// The memory region
            Region: MemoryRegion.Region<'T, 'region>
            
            /// The path to the file
            Path: string
            
            /// The handle to the file mapping
            Handle: nativeint
        }
    
    /// Create a new memory-mapped file
    let create<'T> 
              (path: string) 
              (size: int<bytes>) 
              (schema: SchemaDefinition<validated>): MappedFile<'T, region> =
        // Platform-specific memory-mapped file creation
        // This is a simplified version
        
        // Create the backing array
        let array = Array.zeroCreate (int size)
        
        // Create the memory region
        let region = {
            Data = array
            Offset = 0<offset>
            Length = size
            Schema = schema
        }
        
        {
            Region = region
            Path = path
            Handle = 0n // Would be a real handle on a real system
        }
    
    /// Open an existing memory-mapped file
    let open'<'T> 
              (path: string) 
              (schema: SchemaDefinition<validated>): MappedFile<'T, region> =
        // Platform-specific memory-mapped file opening
        // This is a simplified version
        
        // Read the file
        let array = [| 0uy |] // Would be actual file contents
        let size = array.Length * 1<bytes>
        
        // Create the memory region
        let region = {
            Data = array
            Offset = 0<offset>
            Length = size
            Schema = schema
        }
        
        {
            Region = region
            Path = path
            Handle = 0n // Would be a real handle on a real system
        }
    
    /// Get a memory view for a mapped file
    let getView<'T, [<Measure>] 'region> 
              (mapped: MappedFile<'T, 'region>): MemoryView.View<'T, 'region> =
        MemoryView.create mapped.Region
    
    /// Flush changes to the file
    let flush<'T, [<Measure>] 'region> 
             (mapped: MappedFile<'T, 'region>): unit =
        // Platform-specific memory-mapped file flushing
        // This is a simplified version
        ()
    
    /// Close a memory-mapped file
    let close<'T, [<Measure>] 'region> 
             (mapped: MappedFile<'T, 'region>): unit =
        // Platform-specific memory-mapped file closing
        // This is a simplified version
        ()
```

## Example Usage

Here's a comprehensive example of using BAREWire's memory mapping capabilities:

```fsharp
// Define a schema for a structured log entry
let logEntrySchema =
    schema "LogEntry"
    |> withType "LogLevel" (enum (Map.ofList [
        "DEBUG", 0UL
        "INFO", 1UL
        "WARNING", 2UL
        "ERROR", 3UL
        "CRITICAL", 4UL
    ]))
    |> withType "Timestamp" int
    |> withType "Message" string
    |> withType "LogEntry" (struct' [
        field "level" (userType "LogLevel")
        field "timestamp" (userType "Timestamp")
        field "message" (userType "Message")
        field "data" (optional data)
    ])
    |> withType "LogEntries" (list (userType "LogEntry"))
    |> SchemaValidation.validate
    |> function
       | Ok schema -> schema
       | Error errors -> failwith $"Invalid schema: {errors}"

// Create a shared memory region for logging
let logSharedMemory = 
    SharedMemory.create<LogEntries>
        "application_log"
        4096<bytes>
        logEntrySchema

// Get a view of the shared memory
let logView = SharedMemory.getView logSharedMemory

// Write a log entry
let writeLogEntry (level: string) (message: string) (data: byte[] option) =
    // Get the current entries
    let entries = 
        MemoryView.getField<LogEntries, obj list, region>
            logView ["LogEntries"]
    
    // Create a new entry
    let entry = {|
        level = 
            match level with
            | "DEBUG" -> 0UL
            | "INFO" -> 1UL
            | "WARNING" -> 2UL
            | "ERROR" -> 3UL
            | "CRITICAL" -> 4UL
            | _ -> failwith "Invalid log level"
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        message = message
        data = data
    |}
    
    // Add the entry to the list
    let newEntries = box entry :: entries
    
    // Update the shared memory
    MemoryView.setField<LogEntries, obj list, region>
        logView ["LogEntries"] newEntries

// Read log entries from another process
let readLogEntries () =
    // Open the shared memory
    let logSharedMemory = 
        SharedMemory.open'<LogEntries>
            "application_log"
            logEntrySchema
    
    // Get a view of the shared memory
    let logView = SharedMemory.getView logSharedMemory
    
    // Read the entries
    let entries = 
        MemoryView.getField<LogEntries, obj list, region>
            logView ["LogEntries"]
    
    // Process each entry
    for entry in entries do
        let e = entry :?> {| level: uint64; timestamp: int64; message: string; data: byte[] option |}
        let levelStr =
            match e.level with
            | 0UL -> "DEBUG"
            | 1UL -> "INFO"
            | 2UL -> "WARNING"
            | 3UL -> "ERROR"
            | 4UL -> "CRITICAL"
            | _ -> "UNKNOWN"
            
        let time = DateTimeOffset.FromUnixTimeSeconds(e.timestamp)
        
        printfn "[%s] %s: %s" 
            (time.ToString("yyyy-MM-dd HH:mm:ss")) 
            levelStr 
            e.message
    
    // Close the shared memory
    SharedMemory.close logSharedMemory
```

BAREWire's memory mapping capabilities provide a powerful foundation for interacting with native code, shared memory, and memory-mapped files. By combining type safety, zero-copy access, and flexible schema definitions, BAREWire enables efficient and robust memory-based communication across process and language boundaries.