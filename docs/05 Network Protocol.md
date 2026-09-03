# Network Protocol

> **Envelope adopted (2026-09-03).** The frame format below, with its magic bytes, version byte, flags, and per-frame schema id, is the earlier design and is superseded by the envelope the Readiness Audit §4 step 3 adopts from Conclave.Wire, implemented in `src/Framing/Envelope.fs`: a kind byte (Tell, Ask, Reply, Event, Control), a u32 little-endian correlation id, and the BARE payload; a u32 little-endian length prefix only on stream transports; the schema epoch carried once in a `Hello` control frame; no magic or version bytes, because the schema is fixed at compile time on both ends. The `Request`/`Response` records below with `Guid` correlation and the per-frame `SchemaId` are replaced by the envelope's correlation and the epoch handshake. The transport, RPC, streaming, and security sections remain design notes to be re-read against that envelope; where they use `Guid`, `BitConverter`, `DateTimeOffset`, `Array.Copy`, or an `ITransport` interface, the implementation will not (Readiness Audit §5). The `0xREuy` literal that stood at the magic-bytes line since this document was written did not compile and has been replaced by a compiling placeholder; the adopted envelope has no magic bytes at all.

BAREWire provides a flexible, efficient, and type-safe network protocol layer that enables binary communication over various transport mechanisms. This document explains the network protocol architecture and how to use it effectively.

## Core Concepts

The BAREWire network protocol layer is designed around these key concepts:

1. **Frame Format**: The binary framing structure for messages
2. **Transport Independence**: Support for various transport protocols (TCP, UDP, QUIC, etc.)
3. **Type Safety**: Schema-based encoding and decoding for network messages
4. **Efficiency**: Minimal overhead and zero-copy operations where possible

## Frame Format

BAREWire uses a simple and efficient frame format:

```fsharp
/// Frame format types
module FrameFormat =
    /// Magic bytes that identify a BAREWire frame
    let MagicBytes = [| 0xBAuy; 0x5Euy |]  // superseded: the adopted envelope has no magic bytes
    
    /// Protocol version
    let Version = 1uy
    
    /// Frame flags
    [<Flags>]
    type FrameFlags =
        | None = 0uy
        | Compressed = 1uy
        | Encrypted = 2uy
        | ExtendedHeader = 4uy
        | HasSchema = 8uy
    
    /// Frame header format
    type FrameHeader = {
        /// Magic bytes (0xBARE)
        Magic: byte[] // 2 bytes
        
        /// Protocol version
        Version: byte // 1 byte
        
        /// Frame flags
        Flags: FrameFlags // 1 byte
        
        /// Schema ID (if HasSchema flag is set)
        SchemaId: Guid option // 16 bytes
        
        /// Payload length in bytes
        PayloadLength: uint32 // 4 bytes
        
        /// Extended header data (if ExtendedHeader flag is set)
        ExtendedHeader: byte[] option
    }
    
    /// A complete frame with header and payload
    type Frame = {
        /// Frame header
        Header: FrameHeader
        
        /// Frame payload
        Payload: byte[]
    }
    
    /// Encode a frame to a byte array
    let encodeFrame (frame: Frame): byte[] =
        // Calculate total length
        let headerSize = 
            2 + // Magic
            1 + // Version
            1 + // Flags
            (if frame.Header.SchemaId.IsSome then 16 else 0) + // Schema ID
            4 + // Payload length
            (match frame.Header.ExtendedHeader with 
             | Some ext -> ext.Length 
             | None -> 0) // Extended header
        
        let totalSize = headerSize + frame.Payload.Length
        
        // Create the buffer
        let buffer = Array.zeroCreate totalSize
        let position = ref 0
        
        // Write magic bytes
        Array.Copy(MagicBytes, 0, buffer, !position, 2)
        position := !position + 2
        
        // Write version
        buffer.[!position] <- Version
        position := !position + 1
        
        // Write flags
        let flags = frame.Header.Flags
        buffer.[!position] <- byte flags
        position := !position + 1
        
        // Write schema ID if present
        if frame.Header.SchemaId.IsSome then
            let schemaIdBytes = frame.Header.SchemaId.Value.ToByteArray()
            Array.Copy(schemaIdBytes, 0, buffer, !position, 16)
            position := !position + 16
        
        // Write payload length
        let lengthBytes = BitConverter.GetBytes(uint32 frame.Payload.Length)
        Array.Copy(lengthBytes, 0, buffer, !position, 4)
        position := !position + 4
        
        // Write extended header if present
        match frame.Header.ExtendedHeader with
        | Some extHeader ->
            Array.Copy(extHeader, 0, buffer, !position, extHeader.Length)
            position := !position + extHeader.Length
        | None -> ()
        
        // Write payload
        Array.Copy(frame.Payload, 0, buffer, !position, frame.Payload.Length)
        
        buffer
    
    /// Decode a frame from a byte array
    let decodeFrame (data: byte[]): Frame option =
        // Check minimum size
        if data.Length < 8 then
            None
        else
            // Check magic bytes
            if data.[0] <> MagicBytes.[0] || data.[1] <> MagicBytes.[1] then
                None
            else
                // Read version
                let version = data.[2]
                if version <> Version then
                    None
                else
                    // Read flags
                    let flags = enum<FrameFlags>(int data.[3])
                    
                    // Calculate header size
                    let baseHeaderSize = 2 + 1 + 1 + 4 // Magic, Version, Flags, PayloadLength
                    let hasSchemaId = flags.HasFlag(FrameFlags.HasSchema)
                    let hasExtendedHeader = flags.HasFlag(FrameFlags.ExtendedHeader)
                    
                    let position = ref 4 // Start after Magic, Version, Flags
                    
                    // Read schema ID if present
                    let schemaId =
                        if hasSchemaId then
                            if data.Length < !position + 16 then
                                None
                            else
                                let idBytes = data.[!position..(!position + 15)]
                                position := !position + 16
                                Some (Guid(idBytes))
                        else
                            None
                    
                    // Read payload length
                    if data.Length < !position + 4 then
                        None
                    else
                        let lengthBytes = data.[!position..(!position + 3)]
                        let payloadLength = BitConverter.ToUInt32(lengthBytes, 0)
                        position := !position + 4
                        
                        // Read extended header if present
                        let extendedHeader =
                            if hasExtendedHeader then
                                // Extended header format would be defined here
                                // For simplicity, we assume it's not present
                                None
                            else
                                None
                        
                        // Read payload
                        if data.Length < !position + int payloadLength then
                            None
                        else
                            let payload = data.[!position..(!position + int payloadLength - 1)]
                            
                            // Create the frame
                            Some {
                                Header = {
                                    Magic = MagicBytes
                                    Version = version
                                    Flags = flags
                                    SchemaId = schemaId
                                    PayloadLength = payloadLength
                                    ExtendedHeader = extendedHeader
                                }
                                Payload = payload
                            }
```

## Transport Layer

BAREWire's transport layer is designed to be independent of the underlying protocol:

```fsharp
/// Transport layer interfaces and implementations
module Transport =
    /// Result of a send operation
    type SendResult =
        | Success
        | Failure of error:string
    
    /// Result of a receive operation
    type ReceiveResult =
        | Data of frame:FrameFormat.Frame
        | NoData
        | Error of message:string
    
    /// Transport configuration
    type TransportConfig = {
        /// Buffer size for receiving data
        ReceiveBufferSize: int
        
        /// Buffer size for sending data
        SendBufferSize: int
        
        /// Timeout for operations in milliseconds
        Timeout: int
    }
    
    /// Default transport configuration
    let defaultConfig = {
        ReceiveBufferSize = 8192
        SendBufferSize = 8192
        Timeout = 30000 // 30 seconds
    }
    
    /// Transport interface
    type ITransport =
        /// Send a frame
        abstract Send: FrameFormat.Frame -> SendResult
        
        /// Receive a frame
        abstract Receive: unit -> ReceiveResult
        
        /// Close the transport
        abstract Close: unit -> unit
    
    /// TCP transport implementation
    module Tcp =
        /// Create a TCP client transport
        let createClient (host: string) (port: int) (config: TransportConfig): ITransport =
            // Implementation would create a TCP client
            // This is a simplified version
            { new ITransport with
                member _.Send(frame) =
                    // Encode and send the frame
                    let data = FrameFormat.encodeFrame frame
                    // Send data over TCP
                    Success
                
                member _.Receive() =
                    // Receive data over TCP
                    // Decode the frame
                    NoData
                
                member _.Close() =
                    // Close the TCP connection
                    ()
            }
        
        /// Create a TCP server transport
        let createServer (port: int) (config: TransportConfig): ITransport =
            // Implementation would create a TCP server
            // This is a simplified version
            { new ITransport with
                member _.Send(frame) =
                    // Encode and send the frame
                    let data = FrameFormat.encodeFrame frame
                    // Send data over TCP
                    Success
                
                member _.Receive() =
                    // Receive data over TCP
                    // Decode the frame
                    NoData
                
                member _.Close() =
                    // Close the TCP connection
                    ()
            }
    
    /// UDP transport implementation
    module Udp =
        /// Create a UDP transport
        let create (host: string) (port: int) (config: TransportConfig): ITransport =
            // Implementation would create a UDP socket
            // This is a simplified version
            { new ITransport with
                member _.Send(frame) =
                    // Encode and send the frame
                    let data = FrameFormat.encodeFrame frame
                    // Send data over UDP
                    Success
                
                member _.Receive() =
                    // Receive data over UDP
                    // Decode the frame
                    NoData
                
                member _.Close() =
                    // Close the UDP socket
                    ()
            }
    
    /// QUIC transport implementation
    module Quic =
        /// Create a QUIC transport
        let create (host: string) (port: int) (config: TransportConfig): ITransport =
            // Implementation would create a QUIC connection
            // This is a simplified version
            { new ITransport with
                member _.Send(frame) =
                    // Encode and send the frame
                    let data = FrameFormat.encodeFrame frame
                    // Send data over QUIC
                    Success
                
                member _.Receive() =
                    // Receive data over QUIC
                    // Decode the frame
                    NoData
                
                member _.Close() =
                    // Close the QUIC connection
                    ()
            }
```

## Protocol Layer

The protocol layer ties together schemas, frame formats, and transports:

```fsharp
/// Protocol layer for high-level messaging
module Protocol =
    /// A protocol client with schema-based messaging
    type Client<'T> = {
        /// The transport used by the client
        Transport: Transport.ITransport
        
        /// The schema used for messages
        Schema: SchemaDefinition<validated>
        
        /// The schema ID
        SchemaId: Guid
    }
    
    /// Create a new protocol client
    let createClient<'T> 
                    (transport: Transport.ITransport) 
                    (schema: SchemaDefinition<validated>): Client<'T> =
        {
            Transport = transport
            Schema = schema
            SchemaId = Guid.NewGuid() // In practice, this would be derived from the schema
        }
    
    /// Send a message
    let send<'T> (client: Client<'T>) (message: 'T): Transport.SendResult =
        // Create a buffer for encoding
        let buffer = {
            Data = Array.zeroCreate 8192 // Use a reasonable initial size
            Position = 0<offset>
        }
        
        // Encode the message
        Schema.encode client.Schema message buffer
        
        // Create the payload
        let payload = buffer.Data.[0..int buffer.Position - 1]
        
        // Create the frame
        let frame = {
            Header = {
                Magic = FrameFormat.MagicBytes
                Version = FrameFormat.Version
                Flags = FrameFormat.FrameFlags.HasSchema
                SchemaId = Some client.SchemaId
                PayloadLength = uint32 payload.Length
                ExtendedHeader = None
            }
            Payload = payload
        }
        
        // Send the frame
        client.Transport.Send(frame)
    
    /// Receive a message
    let receive<'T> (client: Client<'T>): Result<'T, string> =
        // Receive a frame
        match client.Transport.Receive() with
        | Transport.ReceiveResult.Data frame ->
            // Verify schema ID if present
            match frame.Header.SchemaId with
            | Some schemaId when schemaId <> client.SchemaId ->
                Error $"Schema ID mismatch: expected {client.SchemaId}, got {schemaId}"
            | _ ->
                // Create a memory region from the payload
                let memory = {
                    Data = frame.Payload
                    Offset = 0<offset>
                    Length = frame.Payload.Length * 1<bytes>
                }
                
                // Decode the message
                try
                    let message, _ = Schema.decode<'T> client.Schema memory
                    Ok message
                with ex ->
                    Error $"Failed to decode message: {ex.Message}"
        
        | Transport.ReceiveResult.NoData ->
            Error "No data available"
            
        | Transport.ReceiveResult.Error msg ->
            Error $"Transport error: {msg}"
    
    /// Close the client
    let close<'T> (client: Client<'T>): unit =
        client.Transport.Close()
```

## Type-Safe RPC

BAREWire supports type-safe remote procedure calls (RPC):

```fsharp
/// Type-safe RPC using BAREWire
module Rpc =
    /// An RPC request
    type Request<'P> = {
        /// Request ID
        Id: Guid
        
        /// Method name
        Method: string
        
        /// Method parameters
        Params: 'P
    }
    
    /// An RPC response
    type Response<'R> = {
        /// Request ID
        Id: Guid
        
        /// Result
        Result: 'R option
        
        /// Error message
        Error: string option
    }
    
    /// An RPC client
    type Client<'P, 'R> = {
        /// The protocol client
        ProtocolClient: Protocol.Client<Request<'P>>
        
        /// The response schema
        ResponseSchema: SchemaDefinition<validated>
    }
    
    /// Create a new RPC client
    let createClient<'P, 'R> 
                    (transport: Transport.ITransport) 
                    (requestSchema: SchemaDefinition<validated>)
                    (responseSchema: SchemaDefinition<validated>): Client<'P, 'R> =
        {
            ProtocolClient = Protocol.createClient<Request<'P>> transport requestSchema
            ResponseSchema = responseSchema
        }
    
    /// Call a remote method
    let call<'P, 'R> 
           (client: Client<'P, 'R>) 
           (method: string) 
           (params: 'P): Result<'R, string> =
        // Create the request
        let request = {
            Id = Guid.NewGuid()
            Method = method
            Params = params
        }
        
        // Send the request
        match Protocol.send client.ProtocolClient request with
        | Transport.SendResult.Success ->
            // Receive the response
            match Protocol.receive<Response<'R>> 
                    { client.ProtocolClient with Schema = client.ResponseSchema } with
            | Ok response ->
                // Check for errors
                match response.Error with
                | Some error -> Error error
                | None ->
                    // Return the result
                    match response.Result with
                    | Some result -> Ok result
                    | None -> Error "No result in response"
            
            | Error err -> Error err
            
        | Transport.SendResult.Failure error ->
            Error $"Failed to send request: {error}"
    
    /// Create an RPC server
    let createServer<'P, 'R> 
                    (transport: Transport.ITransport)
                    (requestSchema: SchemaDefinition<validated>)
                    (responseSchema: SchemaDefinition<validated>)
                    (handler: string -> 'P -> Result<'R, string>): unit =
        // Create the protocol clients
        let requestClient = Protocol.createClient<Request<'P>> transport requestSchema
        let responseClient = 
            { requestClient with Schema = responseSchema }
        
        // Start the server loop
        let rec serverLoop () =
            // Receive a request
            match Protocol.receive<Request<'P>> requestClient with
            | Ok request ->
                // Handle the request
                let response =
                    match handler request.Method request.Params with
                    | Ok result ->
                        {
                            Id = request.Id
                            Result = Some result
                            Error = None
                        }
                    | Error error ->
                        {
                            Id = request.Id
                            Result = None
                            Error = Some error
                        }
                
                // Send the response
                Protocol.send responseClient response |> ignore
                
                // Continue the loop
                serverLoop ()
                
            | Error _ ->
                // Error receiving request, continue the loop
                serverLoop ()
        
        // Start the server loop in a separate thread
        serverLoop ()
```

## Integration with Measures

BAREWire uses dimensional measures for type-safe network communication:

```fsharp
/// Define message-specific units of measure
[<Measure>] type userId
[<Measure>] type authToken
[<Measure>] type sessionId

/// Type-safe protocol module
module TypeSafeProtocol =
    /// A login request
    type LoginRequest = {
        Username: string<userId>
        Password: string
    }
    
    /// A login response
    type LoginResponse = {
        Token: string<authToken>
        SessionId: Guid<sessionId>
    }
    
    /// Send a login request with type safety
    let login 
        (client: Protocol.Client<LoginRequest>) 
        (username: string<userId>) 
        (password: string): Result<LoginResponse, string> =
        
        // Create the request
        let request = {
            Username = username
            Password = password
        }
        
        // Send the request
        match Protocol.send client request with
        | Transport.SendResult.Success ->
            // Receive the response
            Protocol.receive<LoginResponse> client
            
        | Transport.SendResult.Failure error ->
            Error $"Failed to send login request: {error}"
    
    /// Responses are constructed measured at the origin: the values that
    /// enter the protocol already carry their dimensions, and a token can
    /// never be passed where a session id is expected
    let createLoginResponse (token: string<authToken>) (sessionId: Guid<sessionId>): LoginResponse =
        {
            Token = token
            SessionId = sessionId
        }
```

## Secure Communication

BAREWire supports secure communication through encryption and authentication:

```fsharp
/// Secure communication module
module SecureProtocol =
    /// Encryption algorithms
    type EncryptionAlgorithm =
        | None
        | AES256
        | ChaCha20Poly1305
    
    /// Security configuration
    type SecurityConfig = {
        /// Encryption algorithm
        Encryption: EncryptionAlgorithm
        
        /// Pre-shared key (if using symmetric encryption)
        PreSharedKey: byte[] option
        
        /// Server certificate (for TLS)
        ServerCertificate: byte[] option
        
        /// Client certificate (for mutual TLS)
        ClientCertificate: byte[] option
    }
    
    /// Default security configuration
    let defaultConfig = {
        Encryption = None
        PreSharedKey = None
        ServerCertificate = None
        ClientCertificate = None
    }
    
    /// Create a secure transport
    let createSecureTransport 
        (baseTransport: Transport.ITransport) 
        (config: SecurityConfig): Transport.ITransport =
        
        match config.Encryption with
        | None -> 
            // No encryption, use the base transport
            baseTransport
            
        | AES256 ->
            // AES-256 encryption
            match config.PreSharedKey with
            | None -> failwith "AES-256 encryption requires a pre-shared key"
            | Some key ->
                // Create a secure transport that encrypts/decrypts using AES-256
                { new Transport.ITransport with
                    member _.Send(frame) =
                        // Encrypt the payload
                        let encryptedPayload = encrypt AES256 key frame.Payload
                        
                        // Create a new frame with the encrypted payload
                        let secureFrame = {
                            frame with
                                Header = {
                                    frame.Header with
                                        Flags = frame.Header.Flags ||| FrameFormat.FrameFlags.Encrypted
                                        PayloadLength = uint32 encryptedPayload.Length
                                }
                                Payload = encryptedPayload
                        }
                        
                        // Send the secure frame
                        baseTransport.Send(secureFrame)
                    
                    member _.Receive() =
                        // Receive a frame
                        match baseTransport.Receive() with
                        | Transport.ReceiveResult.Data frame ->
                            // Check if the frame is encrypted
                            if frame.Header.Flags.HasFlag(FrameFormat.FrameFlags.Encrypted) then
                                // Decrypt the payload
                                try
                                    let decryptedPayload = decrypt AES256 key frame.Payload
                                    
                                    // Create a new frame with the decrypted payload
                                    let decryptedFrame = {
                                        frame with
                                            Header = {
                                                frame.Header with
                                                    Flags = frame.Header.Flags ^^^ FrameFormat.FrameFlags.Encrypted
                                                    PayloadLength = uint32 decryptedPayload.Length
                                            }
                                            Payload = decryptedPayload
                                    }
                                    
                                    Transport.ReceiveResult.Data decryptedFrame
                                with ex ->
                                    Transport.ReceiveResult.Error $"Failed to decrypt payload: {ex.Message}"
                            else
                                // Frame is not encrypted
                                Transport.ReceiveResult.Data frame
                        
                        | other -> other
                    
                    member _.Close() =
                        // Close the base transport
                        baseTransport.Close()
                }
                
        | ChaCha20Poly1305 ->
            // ChaCha20-Poly1305 encryption
            match config.PreSharedKey with
            | None -> failwith "ChaCha20-Poly1305 encryption requires a pre-shared key"
            | Some key ->
                // Similar implementation to AES-256, but using ChaCha20-Poly1305
                failwith "ChaCha20-Poly1305 not implemented"
    
    /// Encrypt data using the specified algorithm
    let private encrypt (algorithm: EncryptionAlgorithm) (key: byte[]) (data: byte[]): byte[] =
        // Implementation would use the specified algorithm to encrypt the data
        // This is a simplified version
        data
    
    /// Decrypt data using the specified algorithm
    let private decrypt (algorithm: EncryptionAlgorithm) (key: byte[]) (data: byte[]): byte[] =
        // Implementation would use the specified algorithm to decrypt the data
        // This is a simplified version
        data
```

## Streaming Protocol

BAREWire supports streaming data for large transfers:

```fsharp
/// Streaming protocol module
module Streaming =
    /// Streaming flags
    [<Flags>]
    type StreamingFlags =
        | None = 0uy
        | FirstChunk = 1uy
        | LastChunk = 2uy
        | Aborted = 4uy
    
    /// A streaming chunk
    type Chunk = {
        /// Stream ID
        StreamId: Guid
        
        /// Chunk sequence number
        SequenceNumber: uint32
        
        /// Streaming flags
        Flags: StreamingFlags
        
        /// Chunk data
        Data: byte[]
    }
    
    /// Stream state
    type StreamState =
        | Initializing
        | Streaming
        | Completed
        | Aborted
    
    /// A data stream
    type Stream = {
        /// Stream ID
        Id: Guid
        
        /// Current stream state
        State: StreamState
        
        /// Total size in bytes (if known)
        TotalSize: uint64 option
        
        /// Received size in bytes
        ReceivedSize: uint64
        
        /// Next expected sequence number
        NextSequence: uint32
        
        /// Received chunks
        Chunks: Map<uint32, byte[]>
    }
    
    /// A streaming client
    type Client = {
        /// The transport used by the client
        Transport: Transport.ITransport
        
        /// Active streams
        Streams: Map<Guid, Stream>
    }
    
    /// Create a new streaming client
    let createClient (transport: Transport.ITransport): Client =
        {
            Transport = transport
            Streams = Map.empty
        }
    
    /// Start sending a stream
    let startSending (client: Client) (data: byte[]) (chunkSize: int): Stream =
        // Create a new stream
        let streamId = Guid.NewGuid()
        let stream = {
            Id = streamId
            State = Initializing
            TotalSize = Some (uint64 data.Length)
            ReceivedSize = 0UL
            NextSequence = 0u
            Chunks = Map.empty
        }
        
        // Add the stream to the client
        let client = { client with Streams = Map.add streamId stream client.Streams }
        
        // Send the first chunk
        sendNextChunk client streamId data 0 chunkSize
    
    /// Send the next chunk of a stream
    let private sendNextChunk 
                 (client: Client) 
                 (streamId: Guid) 
                 (data: byte[]) 
                 (offset: int) 
                 (chunkSize: int): Stream =
        
        // Get the stream
        let stream = Map.find streamId client.Streams
        
        // Calculate the chunk size
        let remainingSize = data.Length - offset
        let actualChunkSize = min chunkSize remainingSize
        let isLastChunk = remainingSize <= chunkSize
        
        // Create the chunk
        let chunk = {
            StreamId = streamId
            SequenceNumber = stream.NextSequence
            Flags = 
                (if stream.NextSequence = 0u then StreamingFlags.FirstChunk else StreamingFlags.None) |||
                (if isLastChunk then StreamingFlags.LastChunk else StreamingFlags.None)
            Data = data.[offset..(offset + actualChunkSize - 1)]
        }
        
        // Send the chunk
        sendChunk client chunk |> ignore
        
        // Update the stream state
        let newState =
            if isLastChunk then
                Completed
            else
                Streaming
        
        let newStream = {
            stream with
                State = newState
                NextSequence = stream.NextSequence + 1u
        }
        
        // Update the client
        let newClient = { client with Streams = Map.add streamId newStream client.Streams }
        
        // If not the last chunk, send the next chunk
        if not isLastChunk then
            sendNextChunk newClient streamId data (offset + actualChunkSize) chunkSize
        else
            newStream
    
    /// Send a chunk
    let private sendChunk (client: Client) (chunk: Chunk): Transport.SendResult =
        // Create a buffer for encoding
        let buffer = {
            Data = Array.zeroCreate 8192 // Use a reasonable initial size
            Position = 0<offset>
        }
        
        // Encode the chunk
        // In a real implementation, this would use Schema.encode
        
        // Create the payload
        let payload = buffer.Data.[0..int buffer.Position - 1]
        
        // Create the frame
        let frame = {
            Header = {
                Magic = FrameFormat.MagicBytes
                Version = FrameFormat.Version
                Flags = FrameFormat.FrameFlags.None
                SchemaId = None
                PayloadLength = uint32 payload.Length
                ExtendedHeader = None
            }
            Payload = payload
        }
        
        // Send the frame
        client.Transport.Send(frame)
    
    /// Receive chunks
    let receiveChunks (client: Client): Map<Guid, Stream> =
        // Receive a frame
        match client.Transport.Receive() with
        | Transport.ReceiveResult.Data frame ->
            // Decode the chunk
            // In a real implementation, this would use Schema.decode
            let chunk = {
                StreamId = Guid.Empty
                SequenceNumber = 0u
                Flags = StreamingFlags.None
                Data = [||]
            }
            
            // Get or create the stream
            let stream =
                match Map.tryFind chunk.StreamId client.Streams with
                | Some s -> s
                | None ->
                    {
                        Id = chunk.StreamId
                        State = Initializing
                        TotalSize = None
                        ReceivedSize = 0UL
                        NextSequence = 0u
                        Chunks = Map.empty
                    }
            
            // Update the stream with the new chunk
            let newStream =
                if chunk.Flags.HasFlag(StreamingFlags.Aborted) then
                    { stream with State = Aborted }
                else
                    let newChunks = Map.add chunk.SequenceNumber chunk.Data stream.Chunks
                    let newReceivedSize = stream.ReceivedSize + uint64 chunk.Data.Length
                    
                    let newState =
                        if chunk.Flags.HasFlag(StreamingFlags.FirstChunk) then
                            if chunk.Flags.HasFlag(StreamingFlags.LastChunk) then
                                Completed
                            else
                                Streaming
                        else if chunk.Flags.HasFlag(StreamingFlags.LastChunk) then
                            Completed
                        else
                            stream.State
                    
                    {
                        stream with
                            State = newState
                            ReceivedSize = newReceivedSize
                            NextSequence = max stream.NextSequence (chunk.SequenceNumber + 1u)
                            Chunks = newChunks
                    }
            
            // Update the client
            { client with Streams = Map.add chunk.StreamId newStream client.Streams }.Streams
            
        | _ -> client.Streams
    
    /// Get the complete data for a stream
    let getStreamData (stream: Stream): byte[] option =
        if stream.State <> Completed then
            None
        else
            // Combine all chunks in sequence
            let sortedChunks =
                stream.Chunks
                |> Map.toSeq
                |> Seq.sortBy fst
                |> Seq.map snd
            
            let totalSize = int stream.ReceivedSize
            let result = Array.zeroCreate totalSize
            
            let mutable offset = 0
            for chunk in sortedChunks do
                Array.Copy(chunk, 0, result, offset, chunk.Length)
                offset <- offset + chunk.Length
            
            Some result
```

## Example Usage

Here's a comprehensive example of using BAREWire's network protocol capabilities:

```fsharp
// Define a schema for a chat application
let chatSchema =
    schema "ChatMessages"
    |> withType "MessageType" (enum (Map.ofList [
        "TEXT", 0UL
        "IMAGE", 1UL
        "JOIN", 2UL
        "LEAVE", 3UL
    ]))
    |> withType "UserId" string
    |> withType "MessageId" string
    |> withType "Timestamp" int
    |> withType "Message" (struct' [
        field "id" (userType "MessageId")
        field "type" (userType "MessageType")
        field "sender" (userType "UserId")
        field "content" string
        field "timestamp" (userType "Timestamp")
        field "attachmentData" (optional data)
    ])
    |> withType "ChatMessages" (list (userType "Message"))
    |> SchemaValidation.validate
    |> function
       | Ok schema -> schema
       | Error errors -> failwith $"Invalid schema: {errors}"

// Create a chat client
let createChatClient (host: string) (port: int): Protocol.Client<obj> =
    // Create the transport
    let transport = Transport.Tcp.createClient host port Transport.defaultConfig
    
    // Create the protocol client
    Protocol.createClient transport chatSchema

// Send a chat message
let sendChatMessage 
    (client: Protocol.Client<obj>) 
    (messageType: string) 
    (sender: string) 
    (content: string) 
    (attachmentData: byte[] option): Transport.SendResult =
    
    // Create the message
    let message = {|
        id = Guid.NewGuid().ToString()
        type = 
            match messageType with
            | "TEXT" -> 0UL
            | "IMAGE" -> 1UL
            | "JOIN" -> 2UL
            | "LEAVE" -> 3UL
            | _ -> failwith "Invalid message type"
        sender = sender
        content = content
        timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        attachmentData = attachmentData
    |}
    
    // Send the message
    Protocol.send client message

// Receive chat messages
let receiveChatMessages (client: Protocol.Client<obj>): Result<obj list, string> =
    // Receive messages
    Protocol.receive<obj list> client
```

BAREWire's network protocol capabilities provide a powerful foundation for efficient, type-safe binary communication over various transport mechanisms. By combining structured schemas, flexible framing, and transport independence, BAREWire enables robust communication between distributed systems in a wide range of scenarios.