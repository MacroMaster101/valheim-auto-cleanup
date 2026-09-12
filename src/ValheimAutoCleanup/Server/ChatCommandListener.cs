using System;
using BepInEx.Logging;
using ValheimAutoCleanup.Policy;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// Lets server admins run cleanup commands by typing them into ordinary in-game chat,
    /// with no mod on the client.
    ///
    /// Messages reach this class from <see cref="Patches.ChatMessagePatch"/>, which explains
    /// why a Harmony patch on the routing layer is the only workable hook: current Valheim
    /// sends chat to each player individually rather than broadcasting it, so a dedicated
    /// server is never a recipient and never runs <c>Chat.RPC_ChatMessage</c>.
    ///
    /// A consequence of that design is that the server observes the SAME message once per
    /// connected player. Commands are therefore de-duplicated here: without it, a single
    /// typed command would run once for every player online.
    ///
    /// SECURITY
    /// --------
    /// Nothing inside a chat message identifies its sender reliably. The <c>UserInfo</c> and
    /// display name are written by the sending client, and so is the routed RPC's sender ID:
    /// Valheim reads it from the packet and never checks it against the connection. A command
    /// is therefore obeyed only when that claimed ID matches the connection the message really
    /// arrived on (<see cref="ChatSenderCheck"/>), and the admin check then uses that
    /// connection's platform ID - the same value Valheim itself uses for adminlist.txt.
    /// Anything that cannot be resolved is treated as "not an admin".
    /// </summary>
    internal sealed class ChatCommandListener
    {
        private const int MaxChatReplyLines = 3;

        /// <summary>
        /// How long a command stays de-duplicated. The per-player copies of one message are
        /// routed within the same frame, so this only has to span a moment; it is generous
        /// to also absorb an accidental double-send.
        /// </summary>
        private const float DuplicateWindowSeconds = 3f;

        private readonly ManualLogSource _log;
        private readonly PluginConfig _config;
        private readonly Commands _commands;
        private readonly Broadcaster _broadcaster;

        private long _lastSenderPeerId;
        private string _lastText;
        private float _lastHandledAt = float.NegativeInfinity;

        internal ChatCommandListener(
            ManualLogSource log, PluginConfig config, Commands commands, Broadcaster broadcaster)
        {
            _log = log;
            _config = config;
            _commands = commands;
            _broadcaster = broadcaster;
        }

        /// <summary>
        /// Handles one observed chat message. Returns immediately for anything that is not an
        /// admin command, which is the overwhelming majority of messages.
        /// </summary>
        /// <param name="connection">The connection the message arrived on, or null if unknown.</param>
        /// <param name="claimedSenderPeerId">The sender ID written inside the message. Untrusted.</param>
        /// <param name="text">The chat text.</param>
        internal void OnChatMessage(ZRpc connection, long claimedSenderPeerId, string text)
        {
            try
            {
                if (string.IsNullOrEmpty(text) || !_config.EnableChatCommands)
                {
                    return;
                }

                var prefix = _config.ChatCommandPrefix;
                if (string.IsNullOrEmpty(prefix))
                {
                    return;
                }

                var trimmed = text.Trim();
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var peer = ResolveConnection(connection);
                switch (ChatSenderCheck.Verify(claimedSenderPeerId, peer?.m_uid))
                {
                    case ChatSenderVerdict.Verified:
                        break;

                    case ChatSenderVerdict.Mismatch:
                        _log.LogWarning(
                            "Ignoring a cleanup command that claims to come from peer " + claimedSenderPeerId +
                            " but arrived on the connection of " + Describe(peer) +
                            ". A modified client may be trying to pose as another player.");
                        return;

                    default:
                        _log.LogInfo("Ignoring a cleanup command whose sending connection could not be identified.");
                        return;
                }

                // The same command arrives once per connected player; run it only once.
                if (IsDuplicate(peer.m_uid, trimmed))
                {
                    return;
                }

                if (!IsAdmin(peer, out var who))
                {
                    _log.LogInfo("Ignoring a cleanup command from a non-admin connection (" + who + ").");
                    return;
                }

                var arguments = trimmed.Substring(prefix.Length).Trim();
                _log.LogInfo("Admin " + who + " ran: " + Commands.RootCommand + " " + arguments);

                var output = _commands.Execute(arguments);

                foreach (var line in output)
                {
                    _log.LogInfo("  " + line);
                }

                _broadcaster.Reply(
                    peer.m_uid,
                    Commands.Flatten(output, MaxChatReplyLines),
                    _config.AnnouncementPrefix);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Failed to handle a chat command: " + ex.Message);
            }
        }

        /// <summary>
        /// True when this exact command from this exact sender was already handled a moment
        /// ago, which is how the per-player copies of a single chat message present.
        /// </summary>
        private bool IsDuplicate(long senderPeerId, string trimmed)
        {
            var now = UnityEngine.Time.realtimeSinceStartup;

            if (senderPeerId == _lastSenderPeerId &&
                string.Equals(trimmed, _lastText, StringComparison.Ordinal) &&
                now - _lastHandledAt < DuplicateWindowSeconds)
            {
                return true;
            }

            _lastSenderPeerId = senderPeerId;
            _lastText = trimmed;
            _lastHandledAt = now;
            return false;
        }

        /// <summary>
        /// The connected peer that owns this connection, or null.
        ///
        /// <c>ZNet.GetPeer(ZRpc)</c> is private, so the connection is matched against the
        /// connected peers by reference - the same comparison that method makes.
        /// </summary>
        private static ZNetPeer ResolveConnection(ZRpc connection)
        {
            if (connection == null)
            {
                return null;
            }

            var znet = ZNet.instance;
            var peers = znet == null ? null : znet.GetConnectedPeers();
            if (peers == null)
            {
                return null;
            }

            for (var i = 0; i < peers.Count; i++)
            {
                var peer = peers[i];
                if (peer != null && ReferenceEquals(peer.m_rpc, connection))
                {
                    return peer;
                }
            }

            return null;
        }

        private static string Describe(ZNetPeer peer)
        {
            return peer == null
                ? "an unknown connection"
                : (peer.m_playerName ?? "unknown") + " / peer " + peer.m_uid;
        }

        /// <summary>
        /// Checks a verified peer's platform ID against the server's admin list. Anything that
        /// cannot be resolved is treated as "not an admin".
        /// </summary>
        private static bool IsAdmin(ZNetPeer peer, out string description)
        {
            description = Describe(peer);

            var znet = ZNet.instance;
            if (znet == null || peer.m_socket == null)
            {
                return false;
            }

            string hostName;
            try
            {
                hostName = peer.m_socket.GetHostName();
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(hostName))
            {
                return false;
            }

            description = (peer.m_playerName ?? "unknown") + " / " + hostName;
            return znet.IsAdmin(hostName);
        }
    }
}
