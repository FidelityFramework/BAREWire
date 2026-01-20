namespace BAREWire.Memory

open BAREWire.Core
open BAREWire.Core.Error
open BAREWire.Core.Memory

/// <summary>
/// Memory region operations for managing contiguous memory blocks.
/// FNCS provides Arena and nativeptr types - this module provides
/// higher-level operations over byte array memory.
/// </summary>
module Region =
    /// <summary>
    /// A memory region is simply a Memory view
    /// </summary>
    type Region<'T> = Memory<'T>

    /// <summary>
    /// Creates a new memory region from a byte array
    /// </summary>
    let create<'T> (data: byte[]) : Region<'T> =
        Memory.fromArray<'T> data

    /// <summary>
    /// Creates a new memory region filled with zero bytes
    /// </summary>
    let createZeroed<'T> (size: int) : Region<'T> =
        Memory.createZeroed<'T> size

    /// <summary>
    /// Creates a slice of an existing memory region
    /// </summary>
    let slice<'T, 'U> (region: Region<'T>) (offset: int) (length: int) : Result<Region<'U>, Error.Error> =
        try
            let sliced = Memory.slice<'T, 'U> region offset length
            Ok sliced
        with _ ->
            Error (outOfBoundsError offset length)

    /// <summary>
    /// Copies data from one region to another
    /// </summary>
    let copy<'T, 'U> (source: Region<'T>) (destination: Region<'U>) (count: int) : Result<unit, Error.Error> =
        try
            Memory.copy<'T, 'U> source destination count
            Ok ()
        with _ ->
            Error (outOfBoundsError 0 count)

    /// <summary>
    /// Resizes a memory region, preserving its contents
    /// </summary>
    let resize<'T> (region: Region<'T>) (newSize: int) : Result<Region<'T>, Error.Error> =
        try
            let newData = Array.zeroCreate newSize
            let copySize = min region.Length newSize
            Array.blit region.Data region.Offset newData 0 copySize
            Ok (Memory.fromArray<'T> newData)
        with ex ->
            Error (invalidValueError "Failed to resize region: " + ex.Message)

    /// <summary>
    /// Gets the size of a memory region
    /// </summary>
    let getSize<'T> (region: Region<'T>) : int =
        region.Length

    /// <summary>
    /// Checks if a memory region is empty
    /// </summary>
    let isEmpty<'T> (region: Region<'T>) : bool =
        region.Length = 0

    /// <summary>
    /// Merges two memory regions into a new region
    /// </summary>
    let merge<'T> (region1: Region<'T>) (region2: Region<'T>) : Result<Region<'T>, Error.Error> =
        try
            let newSize = region1.Length + region2.Length
            let newData = Array.zeroCreate newSize

            Array.blit region1.Data region1.Offset newData 0 region1.Length
            Array.blit region2.Data region2.Offset newData region1.Length region2.Length

            Ok (Memory.fromArray<'T> newData)
        with ex ->
            Error (invalidValueError "Failed to merge regions: " + ex.Message)

    /// <summary>
    /// Splits a memory region into two at the specified offset
    /// </summary>
    let split<'T> (region: Region<'T>) (offset: int) : Result<Region<'T> * Region<'T>, Error.Error> =
        if offset < 0 || offset > region.Length then
            Error (outOfBoundsError offset 0)
        else
            try
                let firstLength = offset
                let secondLength = region.Length - offset

                match slice<'T, 'T> region 0 firstLength with
                | Ok first ->
                    match slice<'T, 'T> region offset secondLength with
                    | Ok second -> Ok (first, second)
                    | Error e -> Error e
                | Error e -> Error e
            with ex ->
                Error (invalidValueError "Failed to split region: " + ex.Message)

    /// <summary>
    /// Fills a memory region with a specific byte value
    /// </summary>
    let fill<'T> (region: Region<'T>) (value: byte) : Result<unit, Error.Error> =
        try
            for i = 0 to region.Length - 1 do
                region.Data.[region.Offset + i] <- value
            Ok ()
        with ex ->
            Error (invalidValueError "Failed to fill region: " + ex.Message)

    /// <summary>
    /// Compares two memory regions for equality
    /// </summary>
    let equal<'T, 'U> (region1: Region<'T>) (region2: Region<'U>) : bool =
        if region1.Length <> region2.Length then
            false
        else
            let mutable result = true
            let mutable i = 0
            while result && i < region1.Length do
                if region1.Data.[region1.Offset + i] <> region2.Data.[region2.Offset + i] then
                    result <- false
                i <- i + 1
            result

    /// <summary>
    /// Finds a byte pattern in a memory region
    /// </summary>
    let find<'T> (region: Region<'T>) (pattern: byte[]) : int option =
        if pattern.Length = 0 || region.Length < pattern.Length then
            None
        else
            let mutable found = false
            let mutable offset = 0

            while not found && offset <= region.Length - pattern.Length do
                let mutable matches = true
                let mutable i = 0

                while matches && i < pattern.Length do
                    if region.Data.[region.Offset + offset + i] <> pattern.[i] then
                        matches <- false
                    i <- i + 1

                if matches then
                    found <- true
                else
                    offset <- offset + 1

            if found then Some offset else None
