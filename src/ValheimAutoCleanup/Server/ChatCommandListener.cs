using System;
using BepInEx.Logging;

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
    /// The <c>UserInfo</c> carried inside a chat message is supplied by the sending client
    /// and must not be trusted; neither may the display name. The admin check resolves the
    /// sender's peer and uses the platform ID from its authenticated socket, which is the
    /// same value Valheim itself uses for adminlist.txt. Anything that cannot be resolved is
    /// treated as "not an admin", and a non-admin typing the command prefix is ignored.
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
        internal void OnChatMessage(long senderPeerId, long messageId, string text)
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

                // The same command arrives once per connected player; run it only once.
                if (IsDuplicate(senderPeerId, trimmed))
                {
                    return;
                }

                if (!IsAdmin(senderPeerId, out var who))
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
                    senderPeerId,
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
        /// Resolves the sender's authenticated platform ID and checks it against the server's
        /// admin list. Anything that cannot be resolved is treated as "not an admin".
        /// </summary>
        private static bool IsAdmin(long senderPeerId, out string description)
        {
            description = "peer " + senderPeerId;

            var znet = ZNet.instance;
            if (znet == null)
            {
                return false;
            }

            var peer = znet.GetPeer(senderPeerId);
            if (peer == null || peer.m_socket == null)
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
