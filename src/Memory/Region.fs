namespace BAREWire.Memory

open BAREWire.Core
open BAREWire.Core.Error
open BAREWire.Core.Memory

/// <summary>
/// Memory region operations for managing contiguous memory blocks
/// </summary>
module Region =
    /// <summary>
    /// A memory region with type safety
    /// </summary>
    /// <typeparam name="'T">The type associated with this memory region</typeparam>
    /// <typeparam name="'region">The memory region measure type</typeparam>
    type Region<'T, [<Measure>] 'region> = Memory<'T, 'region>
        
    /// <summary>
    /// Creates a new memory region from a byte array
    /// </summary>
    /// <param name="data">The byte array to use as the underlying storage</param>
    /// <returns>A new memory region containing the provided data</returns>
    let create<'T, [<Measure>] 'region> (data: byte[]): Region<'T, 'region> =
        fromArray<'T, 'region> data
    
    /// <summary>
    /// Creates a new memory region filled with zero bytes
    /// </summary>
    /// <param name="size">The size of the region in bytes</param>
    /// <returns>A new zeroed memory region of the specified size</returns>
    let createZeroed<'T, [<Measure>] 'region> (size: int<bytes>): Region<'T, 'region> =
        let data = Array.zeroCreate (int size)
        fromArray<'T, 'region> data
    
    /// <summary>
    /// Creates a slice of an existing memory region
    /// </summary>
    /// <param name="region">The source memory region</param>
    /// <param name="offset">The starting offset within the region</param>
    /// <param name="length">The length of the slice in bytes</param>
    /// <returns>A result containing the sliced region or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when the slice parameters are invalid</exception>
    let slice<'T, 'U, [<Measure>] 'region> 
             (region: Region<'T, 'region>) 
             (offset: int<offset>) 
             (length: int<bytes>): Result<Region<'U, 'region>> =
        try
            let sliced = Memory.slice<'T, 'U, 'region> region offset length
            Ok sliced
        with ex ->
            Error (outOfBoundsError offset length)
    
    /// <summary>
    /// Copies data from one region to another
    /// </summary>
    /// <param name="source">The source memory region</param>
    /// <param name="destination">The destination memory region</param>
    /// <param name="count">The number of bytes to copy</param>
    /// <returns>A result indicating success or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when the copy operation exceeds region bounds</exception>
    let copy<'T, 'U, [<Measure>] 'region1, [<Measure>] 'region2> 
            (source: Region<'T, 'region1>) 
            (destination: Region<'U, 'region2>) 
            (count: int<bytes>): Result<unit> =
        try
            Memory.copy<'T, 'U, 'region1, 'region2> source destination count
            Ok ()
        with ex ->
            Error (outOfBoundsError 0<offset> count)
    
    /// <summary>
    /// Resizes a memory region, preserving its contents
    /// </summary>
    /// <param name="region">The memory region to resize</param>
    /// <param name="newSize">The new size in bytes</param>
    /// <returns>A result containing the resized region or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when resizing fails</exception>
    let resize<'T, [<Measure>] 'region> 
              (region: Region<'T, 'region>) 
              (newSize: int<bytes>): Result<Region<'T, 'region>> =
        try
            let newData = Array.zeroCreate (int newSize)
            let copySize = min region.Length newSize
            Array.blit region.Data (int region.Offset) newData 0 (int copySize)
            Ok (fromArray<'T, 'region> newData)
        with ex ->
            Error (invalidValueError $"Failed to resize region: {ex.Message}")
    
    /// <summary>
    /// Gets the size of a memory region
    /// </summary>
    /// <param name="region">The memory region</param>
    /// <returns>The size of the region in bytes</returns>
    let getSize<'T, [<Measure>] 'region> (region: Region<'T, 'region>): int<bytes> =
        region.Length
    
    /// <summary>
    /// Checks if a memory region is empty
    /// </summary>
    /// <param name="region">The memory region to check</param>
    /// <returns>True if the region has zero length, false otherwise</returns>
    let isEmpty<'T, [<Measure>] 'region> (region: Region<'T, 'region>): bool =
        region.Length = 0<bytes>
    
    /// <summary>
    /// Merges two memory regions into a new region
    /// </summary>
    /// <param name="region1">The first memory region</param>
    /// <param name="region2">The second memory region</param>
    /// <returns>A result containing the merged region or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when merging fails</exception>
    let merge<'T, [<Measure>] 'region> 
             (region1: Region<'T, 'region>) 
             (region2: Region<'T, 'region>): Result<Region<'T, 'region>> =
        try
            let newSize = region1.Length + region2.Length
            let newData = Array.zeroCreate (int newSize)
            
            Array.blit region1.Data (int region1.Offset) newData 0 (int region1.Length)
            Array.blit region2.Data (int region2.Offset) newData (int region1.Length) (int region2.Length)
            
            Ok (fromArray<'T, 'region> newData)
        with ex ->
            Error (invalidValueError $"Failed to merge regions: {ex.Message}")
    
    /// <summary>
    /// Splits a memory region into two at the specified offset
    /// </summary>
    /// <param name="region">The memory region to split</param>
    /// <param name="offset">The offset at which to split the region</param>
    /// <returns>A result containing a tuple of (first region, second region) or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when the offset is invalid or splitting fails</exception>
    let split<'T, [<Measure>] 'region> 
             (region: Region<'T, 'region>) 
             (offset: int<offset>): Result<Region<'T, 'region> * Region<'T, 'region>> =
        if offset < 0<offset> || int offset * 1<bytes> > region.Length then
            Error (outOfBoundsError offset 0<bytes>)
        else
            try
                let firstLength = int offset * 1<bytes>
                let secondLength = region.Length - firstLength
                
                match slice<'T, 'T, 'region> region 0<offset> firstLength with
                | Ok first ->
                    match slice<'T, 'T, 'region> region offset secondLength with
                    | Ok second -> Ok (first, second)
                    | Error e -> Error e
                | Error e -> Error e
            with ex ->
                Error (invalidValueError $"Failed to split region: {ex.Message}")
    
    /// <summary>
    /// Fills a memory region with a specific byte value
    /// </summary>
    /// <param name="region">The memory region to fill</param>
    /// <param name="value">The byte value to fill with</param>
    /// <returns>A result indicating success or an error</returns>
    /// <exception cref="BAREWire.Core.Error.Error">Thrown when filling fails</exception>
    let fill<'T, [<Measure>] 'region> 
            (region: Region<'T, 'region>) 
            (value: byte): Result<unit> =
        try
            for i = 0 to int region.Length - 1 do
                region.Data.[int region.Offset + i] <- value
            Ok ()
        with ex ->
            Error (invalidValueError $"Failed to fill region: {ex.Message}")
    
    /// <summary>
    /// Compares two memory regions for equality
    /// </summary>
    /// <param name="region1">The first memory region</param>
    /// <param name="region2">The second memory region</param>
    /// <returns>True if the regions have the same content, false otherwise</returns>
    let equal<'T, 'U, [<Measure>] 'region1, [<Measure>] 'region2> 
             (region1: Region<'T, 'region1>) 
             (region2: Region<'U, 'region2>): bool =
        if region1.Length <> region2.Length then
            false
        else
            let mutable result = true
            let mutable i = 0
            while result && i < int region1.Length do
                if region1.Data.[int region1.Offset + i] <> region2.Data.[int region2.Offset + i] then
                    result <- false
                i <- i + 1
            result
    
    /// <summary>
    /// Finds a byte pattern in a memory region
    /// </summary>
    /// <param name="region">The memory region to search</param>
    /// <param name="pattern">The byte pattern to find</param>
    /// <returns>The offset of the first occurrence of the pattern, or None if not found</returns>
    let find<'T, [<Measure>] 'region> 
            (region: Region<'T, 'region>) 
            (pattern: byte[]): option<int<offset>> =
        if pattern.Length = 0 || int region.Length < pattern.Length then
            None
        else
            let mutable found = false
            let mutable offset = 0
            
            while not found && offset <= int region.Length - pattern.Length do
                let mutable matches = true
                let mutable i = 0
                
                while matches && i < pattern.Length do
                    if region.Data.[int region.Offset + offset + i] <> pattern.[i] then
                        matches <- false
                    i <- i + 1
                
                if matches then
                    found <- true
                else
                    offset <- offset + 1
            
            if found then Some (offset * 1<offset>) else None