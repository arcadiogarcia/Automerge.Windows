using System;
using System.Threading;
using System.Threading.Tasks;

namespace Automerge.Windows
{
    /// <summary>
    /// Metadata about a peer.
    /// </summary>
    public sealed class PeerMetadata
    {
        /// <summary>The peer's storage ID, if known.</summary>
        public string? StorageId { get; init; }

        /// <summary>Whether this peer is an ephemeral (non-persisting) peer.</summary>
        public bool IsEphemeral { get; init; }
    }

    /// <summary>
    /// A network message exchanged between peers.
    /// </summary>
    public sealed class NetworkMessage
    {
        /// <summary>The peer who sent this message.</summary>
        public required string SenderId { get; init; }

        /// <summary>The intended recipient peer.</summary>
        public required string TargetId { get; init; }

        /// <summary>The document ID this message pertains to (null for non-document messages).</summary>
        public string? DocumentId { get; init; }

        /// <summary>The message type ("sync", "request", "ephemeral", etc.).</summary>
        public required string Type { get; init; }

        /// <summary>The raw message payload (e.g., automerge sync bytes).</summary>
        public byte[]? Data { get; init; }
    }

    /// <summary>
    /// Pluggable network transport interface for the <see cref="Repo"/>.
    /// Matches the JS <c>NetworkAdapter</c> abstract class from <c>@automerge/automerge-repo</c>.
    ///
    /// <para>
    /// Implement this interface to provide custom transport (Bluetooth, USB,
    /// local IPC, custom WebSocket handling, etc.).
    /// </para>
    /// </summary>
    public interface INetworkAdapter : IAsyncDisposable
    {
        /// <summary>
        /// Connect to the network with the given local peer identity.
        /// Called by <see cref="Repo"/> during startup.
        /// </summary>
        Task ConnectAsync(string peerId, PeerMetadata? metadata = null, CancellationToken ct = default);

        /// <summary>
        /// Disconnect from the network. Called during <see cref="Repo.DisposeAsync"/>.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Send a message to a specific peer.
        /// </summary>
        Task SendAsync(NetworkMessage message, CancellationToken ct = default);

        /// <summary>
        /// Raised when a message is received from any peer.
        /// The <see cref="Repo"/> subscribes to this to process incoming sync messages.
        /// </summary>
        event Action<NetworkMessage>? MessageReceived;

        /// <summary>
        /// Raised when a new peer is discovered (connected).
        /// </summary>
        event Action<string, PeerMetadata?>? PeerConnected;

        /// <summary>
        /// Raised when a peer disconnects.
        /// </summary>
        event Action<string>? PeerDisconnected;
    }
}
