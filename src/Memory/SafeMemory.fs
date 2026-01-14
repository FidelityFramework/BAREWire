#nowarn "9" 

namespace BAREWire.Memory

open FSharp.NativeInterop
open BAREWire.Core
open BAREWire.Core.Error

/// <summary>
/// Safe memory operations using F# native interop
/// </summary>
module SafeMemory =
    /// <summary>
    /// Safely pins memory and executes a function with access to the pinned data
    /// Using NativePtr operations without using the fixed keyword
    /// </summary>
    /// <param name="data">The data to pin</param>
    /// <param name="offset">The offset within the data</param>
    /// <param name="action">The action to execute with the pinned memory address</param>
    /// <returns>The result of the action</returns>
    let withPinnedData<'T> (data: byte[]) (offset: int) (action: nativeint -> 'T) : 'T =
        // Get a pointer to the first element of the array
        let dataPtr = NativePtr.stackalloc<byte> data.Length
        
        // Copy data to the temporary buffer
        for i = 0 to data.Length - 1 do
            NativePtr.set dataPtr i data.[i]
            
        // Calculate the address with offset
        let baseAddr = NativePtr.toNativeInt dataPtr
        let offsetAddr = baseAddr + nativeint offset
        
        // Execute the action with the address
        let result = action offsetAddr
        
        // Copy data back if needed (for write operations)
        for i = 0 to data.Length - 1 do
            data.[i] <- NativePtr.get dataPtr i
            
        result
    
    /// <summary>
    /// Creates a temporary wrapper to work with memory that will be pinned
    /// during operations
    /// </summary>
    /// <typeparam name="'T">The type associated with this memory</typeparam>
    type PinnableMemory<'T when 'T : unmanaged>(arr: byte[]) =
        /// <summary>
        /// The data array
        /// </summary>
        member val Data = arr
        
        /// <summary>
        /// Gets a pointer to a specific offset in the memory
        /// </summary>
        /// <param name="offset">The offset within the memory</param>
        /// <returns>A pointer to the specified offset</returns>
        member this.WithPointerAtOffset(offset: int, f: nativeptr<'T> -> 'U) : 'U =
            withPinnedData this.Data offset (fun addr ->
                let ptr = NativePtr.ofNativeInt<'T> addr
                f ptr
            )
        
        /// <summary>
        /// Reads a value of type 'U from the given offset
        /// </summary>
        /// <param name="offset">The offset to read from</param>
        /// <returns>The value read from memory</returns>
        member this.Read<'U when 'U : unmanaged>(offset: int) : 'U =
            withPinnedData this.Data offset (fun addr ->
                let ptr = NativePtr.ofNativeInt<'U> addr
                NativePtr.read ptr
            )
        
        /// <summary>
        /// Writes a value of type 'U to the given offset
        /// </summary>
        /// <param name="offset">The offset to write to</param>
        /// <param name="value">The value to write</param>
        member this.Write<'U when 'U : unmanaged>(offset: int, value: 'U) : unit =
            withPinnedData this.Data offset (fun addr ->
                let ptr = NativePtr.ofNativeInt<'U> addr
                NativePtr.write ptr value
            )
    
    /// <summary>
    /// Works with pinnable memory
    /// </summary>
    /// <param name="arr">The byte array to work with</param>
    /// <param name="f">The function to execute with the memory</param>
    /// <returns>The result of the function</returns>
    let withPinnableMemory<'T, 'U when 'T : unmanaged> (arr: byte[]) (f: PinnableMemory<'T> -> 'U) : 'U =
        let memory = new PinnableMemory<'T>(arr)
        f memory
    
    /// <summary>
    /// Safely reads a value of type 'T from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>The value read from memory</returns>
    let readUnmanaged<'T when 'T : unmanaged> (data: byte[]) (offset: int) : 'T =
        withPinnedData data offset (fun addr ->
            let ptr = NativePtr.ofNativeInt<'T> addr
            NativePtr.read ptr
        )
    
    /// <summary>
    /// Safely writes a value of type 'T to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    let writeUnmanaged<'T when 'T : unmanaged> (data: byte[]) (offset: int) (value: 'T) : unit =
        withPinnedData data offset (fun addr ->
            let ptr = NativePtr.ofNativeInt<'T> addr
            NativePtr.write ptr value
        )
    
    /// <summary>
    /// Safely reads a byte from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>The byte value read from memory</returns>
    /// <remarks>Throws when the offset is out of bounds</remarks>
    let readByte (data: byte[]) (offset: int) : byte =
        if offset < 0 || offset >= data.Length then
            failwith "Offset out of bounds"
        
        data.[offset]
    
    /// <summary>
    /// Safely writes a byte to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The byte value to write</param>
    /// <remarks>Throws when the offset is out of bounds</remarks>
    let writeByte (data: byte[]) (offset: int) (value: byte) : unit =
        if offset < 0 || offset >= data.Length then
            failwith "Offset out of bounds"
        
        data.[offset] <- value
    
    /// <summary>
    /// Safely copies memory between arrays
    /// </summary>
    /// <param name="source">The source array</param>
    /// <param name="sourceOffset">The offset within the source array</param>
    /// <param name="dest">The destination array</param>
    /// <param name="destOffset">The offset within the destination array</param>
    /// <param name="count">The number of bytes to copy</param>
    /// <remarks>Throws when the copy would exceed array bounds</remarks>
    let copyMemory (source: byte[]) (sourceOffset: int) (dest: byte[]) (destOffset: int) (count: int) : unit =
        if sourceOffset < 0 || sourceOffset + count > source.Length ||
           destOffset < 0 || destOffset + count > dest.Length then
            failwith "Memory copy out of bounds"
        
        for i = 0 to count - 1 do
            dest.[destOffset + i] <- source.[sourceOffset + i]
    
    /// <summary>
    /// Reads a 16-bit integer from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>A result containing the int16 value or an error</returns>
    let readInt16 (data: byte[]) (offset: int) : Result<int16> =
        try
            Ok (readUnmanaged<int16> data offset)
        with ex ->
            Error (decodingError $"Failed to read Int16: {ex.Message}")
    
    /// <summary>
    /// Reads a 32-bit integer from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>A result containing the int32 value or an error</returns>
    let readInt32 (data: byte[]) (offset: int) : Result<int32> =
        try
            Ok (readUnmanaged<int32> data offset)
        with ex ->
            Error (decodingError $"Failed to read Int32: {ex.Message}")
    
    /// <summary>
    /// Reads a 64-bit integer from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>A result containing the int64 value or an error</returns>
    let readInt64 (data: byte[]) (offset: int) : Result<int64> =
        try
            Ok (readUnmanaged<int64> data offset)
        with ex ->
            Error (decodingError $"Failed to read Int64: {ex.Message}")
    
    /// <summary>
    /// Reads a 32-bit floating point value from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>A result containing the float32 value or an error</returns>
    let readFloat32 (data: byte[]) (offset: int) : Result<float32> =
        try
            Ok (readUnmanaged<float32> data offset)
        with ex ->
            Error (decodingError $"Failed to read Float32: {ex.Message}")
    
    /// <summary>
    /// Reads a 64-bit floating point value from memory
    /// </summary>
    /// <param name="data">The memory to read from</param>
    /// <param name="offset">The offset within the memory</param>
    /// <returns>A result containing the float value or an error</returns>
    let readFloat64 (data: byte[]) (offset: int) : Result<float> =
        try
            Ok (readUnmanaged<float> data offset)
        with ex ->
            Error (decodingError $"Failed to read Float64: {ex.Message}")
    
    /// <summary>
    /// Writes a 16-bit integer to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    /// <returns>A result indicating success or an error</returns>
    let writeInt16 (data: byte[]) (offset: int) (value: int16) : Result<unit> =
        try
            writeUnmanaged<int16> data offset value
            Ok ()
        with ex ->
            Error (encodingError $"Failed to write Int16: {ex.Message}")
    
    /// <summary>
    /// Writes a 32-bit integer to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    /// <returns>A result indicating success or an error</returns>
    let writeInt32 (data: byte[]) (offset: int) (value: int32) : Result<unit> =
        try
            writeUnmanaged<int32> data offset value
            Ok ()
        with ex ->
            Error (encodingError $"Failed to write Int32: {ex.Message}")
    
    /// <summary>
    /// Writes a 64-bit integer to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    /// <returns>A result indicating success or an error</returns>
    let writeInt64 (data: byte[]) (offset: int) (value: int64) : Result<unit> =
        try
            writeUnmanaged<int64> data offset value
            Ok ()
        with ex ->
            Error (encodingError $"Failed to write Int64: {ex.Message}")
    
    /// <summary>
    /// Writes a 32-bit floating point value to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    /// <returns>A result indicating success or an error</returns>
    let writeFloat32 (data: byte[]) (offset: int) (value: float32) : Result<unit> =
        try
            writeUnmanaged<float32> data offset value
            Ok ()
        with ex ->
            Error (encodingError $"Failed to write Float32: {ex.Message}")
    
    /// <summary>
    /// Writes a 64-bit floating point value to memory
    /// </summary>
    /// <param name="data">The memory to write to</param>
    /// <param name="offset">The offset within the memory</param>
    /// <param name="value">The value to write</param>
    /// <returns>A result indicating success or an error</returns>
    let writeFloat64 (data: byte[]) (offset: int) (value: float) : Result<unit> =
        try
            writeUnmanaged<float> data offset value
            Ok ()
        with ex ->
            Error (encodingError $"Failed to write Float64: {ex.Message}")