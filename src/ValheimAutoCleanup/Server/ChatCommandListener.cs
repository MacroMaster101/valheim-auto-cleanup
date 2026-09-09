using System;
using BepInEx.Logging;

namespace ValheimAutoCleanup.Server
{
    /// <summary>
    /// Lets server admins run cleanup commands by typing them into ordinary in-game chat,
    /// with no mod on the client.
    ///
    /// Messages reach this class from <see cref="Patches.ChatMessagePatch"/>, which explains
    /// why a Harmony patch is unavoidable here: a dedicated server runs its own <c>Chat</c>
    /// component, so the "ChatMessage" routed RPC is already registered by the game, and
    /// Valheim allows only one handler per RPC name.
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

        private readonly ManualLogSource _log;
        private readonly PluginConfig _config;
        private readonly Commands _commands;
        private readonly Broadcaster _broadcaster;

        internal ChatCommandListener(
            ManualLogSource log, PluginConfig config, Commands commands, Broadcaster broadcaster)
        {
            _log = log;
            _config = config;
            _commands = commands;
            _broadcaster = broadcaster;
        }

        /// <summary>
        /// Handles one chat message. Called from the Harmony prefix on
        /// <c>Chat.RPC_ChatMessage</c>; returns immediately for anything that is not an admin
        /// command, which is the overwhelming majority of messages.
        /// </summary>
        internal void OnChatMessage(long senderPeerId, string text)
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
