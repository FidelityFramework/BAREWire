namespace BAREWire.Core

/// <summary>
/// Error types used throughout BAREWire.
/// Uses standard F# Result type - no external dependencies.
/// </summary>
module Error =
    /// <summary>
    /// Errors that can occur during BAREWire operations
    /// </summary>
    type Error =
        | SchemaValidationError of message:string
        | DecodingError of message:string
        | EncodingError of message:string
        | TypeMismatchError of expected:string * actual:string
        | OutOfBoundsError of offset:int * length:int
        | InvalidValueError of message:string

    /// <summary>
    /// Result type for BAREWire operations, leveraging Alloy's Result infrastructure
    /// </summary>
    type Result<'T> = Result<'T, Error>
    
    /// <summary>
    /// Creates a schema validation error with the specified message
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>A SchemaValidationError</returns>
    let inline validationError message = SchemaValidationError message
    
    /// <summary>
    /// Creates a decoding error with the specified message
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>A DecodingError</returns>
    let inline decodingError message = DecodingError message
    
    /// <summary>
    /// Creates an encoding error with the specified message
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>An EncodingError</returns>
    let inline encodingError message = EncodingError message
    
    /// <summary>
    /// Creates a type mismatch error with expected and actual types
    /// </summary>
    /// <param name="expected">The expected type</param>
    /// <param name="actual">The actual type</param>
    /// <returns>A TypeMismatchError</returns>
    let inline typeMismatchError expected actual = TypeMismatchError(expected, actual)
    
    /// <summary>
    /// Creates an out of bounds error with the specified offset and length
    /// </summary>
    /// <param name="offset">The offset that caused the error</param>
    /// <param name="length">The buffer length</param>
    /// <returns>An OutOfBoundsError</returns>
    let inline outOfBoundsError offset length = OutOfBoundsError(offset, length)
    
    /// <summary>
    /// Creates an invalid value error with the specified message
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns>An InvalidValueError</returns>
    let inline invalidValueError message = InvalidValueError message
    
    /// <summary>
    /// Converts an error to a human-readable string
    /// </summary>
    /// <param name="error">The error to convert</param>
    /// <returns>A string representation of the error</returns>
    let inline toString error =
        match error with
        | SchemaValidationError message -> "Schema validation error: " + message
        | DecodingError message -> "Decoding error: " + message
        | EncodingError message -> "Encoding error: " + message
        | TypeMismatchError(expected, actual) -> "Type mismatch: expected " + expected + ", got " + actual
        | OutOfBoundsError(offset, length) -> "Out of bounds: offset " + string offset + ", length " + string length
        | InvalidValueError message -> "Invalid value: " + message
        
    /// <summary>
    /// Handles an error by converting it to a string
    /// </summary>
    /// <param name="error">The error to handle</param>
    /// <returns>A string representation of the error</returns>
    let inline handle error = toString error
    
    /// <summary>
    /// Maps a successful result using the specified function
    /// </summary>
    /// <param name="f">The mapping function</param>
    /// <param name="result">The result to map</param>
    /// <returns>A new result with the mapped value</returns>
    let inline map f result = Result.map f result
        
    /// <summary>
    /// Binds a successful result to the specified function
    /// </summary>
    /// <param name="f">The binding function</param>
    /// <param name="result">The result to bind</param>
    /// <returns>The result of applying the function to the value</returns>
    let inline bind f result = Result.bind f result
        
    /// <summary>
    /// Maps the error in a failed result using the specified function
    /// </summary>
    /// <param name="f">The error mapping function</param>
    /// <param name="result">The result to map</param>
    /// <returns>A new result with the mapped error</returns>
    let inline mapError f result =
        match result with
        | Ok value -> Ok value
        | Error error -> Error (f error)
        
    /// <summary>
    /// Returns the result, or a default value if Error
    /// </summary>
    /// <param name="defaultValue">The default value to use if result is Error</param>
    /// <param name="result">The result</param>
    /// <returns>The value or default</returns>
    let inline defaultValue defaultValue result = 
        Result.defaultValue defaultValue result
        
    /// <summary>
    /// Returns the result, or applies a function to the error to produce a value
    /// </summary>
    /// <param name="defaultFactory">The function to apply to the error</param>
    /// <param name="result">The result</param>
    /// <returns>The value or result of the default factory</returns>
    let inline defaultWith defaultFactory result =
        Result.defaultWith defaultFactory result
        
    /// <summary>
    /// Converts an option to a Result, using the provided error if None
    /// </summary>
    /// <param name="error">The error to use if option is None</param>
    /// <param name="option">The option to convert</param>
    /// <returns>A Result containing the option value or the error</returns>
    let inline ofOption error option =
        match option with
        | Some value -> Ok value
        | None -> Error error

    /// <summary>
    /// Converts a ValueOption to a Result, using the provided error if None
    /// </summary>
    /// <param name="error">The error to use if option is None</param>
    /// <param name="option">The ValueOption to convert</param>
    /// <returns>A Result containing the option value or the error</returns>
    let inline ofValueOption error option =
        match option with
        | ValueSome value -> Ok value
        | ValueNone -> Error error

    /// <summary>
    /// Converts a Result to an option, discarding the error
    /// </summary>
    /// <param name="result">The result to convert</param>
    /// <returns>Some value if Ok, None if Error</returns>
    let inline toOption result =
        match result with
        | Ok value -> Some value
        | Error _ -> None

    /// <summary>
    /// Converts a Result to a ValueOption, discarding the error
    /// </summary>
    /// <param name="result">The result to convert</param>
    /// <returns>Some value if Ok, None if Error</returns>
    let inline toValueOption result =
        match result with
        | Ok value -> ValueSome value
        | Error _ -> ValueNone

    /// <summary>
    /// Applies a function to both the value and error paths of a result
    /// </summary>
    /// <param name="mapFunc">The function to apply to the value</param>
    /// <param name="errorMapFunc">The function to apply to the error</param>
    /// <param name="result">The result to transform</param>
    /// <returns>A new result with transformed value and error</returns>
    let inline bimap mapFunc errorMapFunc result =
        match result with
        | Ok value -> Ok (mapFunc value)
        | Error err -> Error (errorMapFunc err)

    /// <summary>
    /// Filters a successful result with a predicate, returning error if predicate fails
    /// </summary>
    /// <param name="predicate">The predicate to test the value</param>
    /// <param name="error">The error to return if predicate fails</param>
    /// <param name="result">The result to filter</param>
    /// <returns>The original result if Ok and predicate passes, otherwise Error</returns>
    let inline filter predicate error result =
        match result with
        | Ok value when predicate value -> Ok value
        | Ok _ -> Error error
        | Error e -> Error e
        
    /// <summary>
    /// Converts a validation result to an Error.Result
    /// </summary>
    /// <param name="result">The validation result</param>
    /// <returns>The corresponding Error.Result</returns>
    let inline ofValidation result =
        match result with
        | Ok value -> Ok value
        | Error errors -> 
            let message = String.concat "; " errors
            Error (validationError message)