namespace BAREWire.Core

/// BAREWire domain-specific error types.
module Error =

    /// Errors that can occur during BAREWire operations
    type Error =
        | SchemaValidationError of message: string
        | DecodingError of message: string
        | EncodingError of message: string
        | TypeMismatchError of expected: string * actual: string
        | OutOfBoundsError of offset: int * length: int
        | InvalidValueError of message: string

    /// Converts an error to a human-readable string
    let inline toString error =
        match error with
        | SchemaValidationError message -> $"Schema validation error: {message}"
        | DecodingError message -> $"Decoding error: {message}"
        | EncodingError message -> $"Encoding error: {message}"
        | TypeMismatchError(expected, actual) -> $"Type mismatch: expected {expected}, got {actual}"
        | OutOfBoundsError(offset, length) -> $"Out of bounds: offset {offset}, length {length}"
        | InvalidValueError message -> $"Invalid value: {message}"
