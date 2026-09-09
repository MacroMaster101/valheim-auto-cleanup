using System;
using BepInEx.Logging;

namespace ValheimAutoCleanup.Server
{
    /// <summary>Where an announcement appears on a vanilla client's screen.</summary>
    public enum AnnouncementStyle
    {
        /// <summary>The small corner notice, the one used for "Picked up ..." messages.</summary>
        TopLeft = 1,

        /// <summary>The large centre-screen banner, the one used for "You are cold".</summary>
        Center = 2
    }

    /// <summary>
    /// Sends on-screen messages from the server to vanilla clients.
    ///
    /// HOW THIS STAYS VANILLA-COMPATIBLE
    /// ---------------------------------
    /// Every Player registers a routed RPC on its own character in <c>Player.Awake</c>:
    ///
    ///     m_nview.Register&lt;int, string, int&gt;("Message", RPC_Message)
    ///
    /// and <c>RPC_Message</c> forwards straight to <c>MessageHud.ShowMessage</c>. This is
    /// the same call the game itself uses to tell a player something. Invoking it from the
    /// server, addressed to a peer's own character, produces a message on an entirely
    /// unmodified client. No custom RPC, no custom prefab, nothing for players to install.
    ///
    /// WHY NOT THE CHAT RPC
    /// --------------------
    /// Sending Valheim's "ChatMessage" RPC from the server looks like the obvious approach
    /// and does not work in the current build. The receiving client runs the sender's
    /// PlatformUserID through <c>RelationsManager.CheckPermissionAsync</c>, and a server has
    /// no valid platform ID to present; an invalid one comes back as
    /// <c>RelationsManagerPermissionResult.Error</c>, which fails <c>IsGranted()</c>, so the
    /// message is discarded before it is ever displayed. Even past that,
    /// <c>Terminal.AddString</c> resolves the display name via
    /// <c>ZNet.TryGetPlayerByPlatformUserID</c> and bails out when the sender is not a
    /// connected player - which a server never is. The only way to make chat work would be
    /// to impersonate one of the connected players, which would misattribute server notices
    /// to a person. The MessageHud route has neither problem.
    ///
    /// Every send is wrapped: if the network layer cannot deliver, the message still reaches
    /// the server log and nothing throws.
    /// </summary>
    internal sealed class Broadcaster
    {
        private const string MessageRpcName = "Message";
        private const int MaxMessageLength = 300;

        private readonly ManualLogSource _log;
        private bool _warnedUnavailable;

        internal Broadcaster(ManualLogSource log)
        {
            _log = log;
        }

        /// <summary>
        /// Shows <paramref name="message"/> to every connected player and mirrors it to the
        /// server log. Returns the number of players it was actually delivered to.
        ///
        /// The log line reports delivery explicitly rather than just echoing the text: "the
        /// message appeared in the log" and "a player saw it" are very different claims, and
        /// the difference is the first thing worth knowing when an announcement seems to go
        /// missing.
        /// </summary>
        internal int Announce(string message, string prefix, AnnouncementStyle style)
        {
            if (string.IsNullOrEmpty(message))
            {
                return 0;
            }

            var text = Compose(message, prefix);

            try
            {
                var znet = ZNet.instance;
                var rpc = ZRoutedRpc.instance;

                if (znet == null || rpc == null || !znet.IsServer())
                {
                    _log.LogInfo("[announce -> not sent, network not ready] " + text);
                    return 0;
                }

                var peers = znet.GetConnectedPeers();
                if (peers == null || peers.Count == 0)
                {
                    _log.LogInfo("[announce -> nobody online] " + text);
                    return 0;
                }

                var sent = 0;
                var noCharacter = 0;

                for (var i = 0; i < peers.Count; i++)
                {
                    var peer = peers[i];

                    if (peer != null && peer.IsReady() && peer.m_characterID.IsNone())
                    {
                        noCharacter++;
                        continue;
                    }

                    if (SendTo(rpc, peer, text, style))
                    {
                        sent++;
                    }
                }

                var detail = "[announce -> " + sent + " of " + peers.Count + " players, style=" + style;
                if (noCharacter > 0)
                {
                    detail += ", " + noCharacter + " still spawning";
                }

                _log.LogInfo(detail + "] " + text);
                return sent;
            }
            catch (Exception ex)
            {
                WarnOnce(ex);
                _log.LogInfo("[announce -> failed] " + text);
                return 0;
            }
        }

        /// <summary>
        /// Sends one test announcement in each style to every connected player, so an admin
        /// can tell in a single command whether on-screen messages arrive at all and which
        /// style is visible on their client.
        /// </summary>
        internal string SendTestAnnouncement(string prefix)
        {
            var topLeft = Announce("Cleanup test message (corner style).", prefix, AnnouncementStyle.TopLeft);
            var center = Announce("Cleanup test message (centre style).", prefix, AnnouncementStyle.Center);

            var online = ServerState.ConnectedPlayerCount;
            if (online == 0)
            {
                return "Nobody is connected, so there was nobody to send a test message to.";
            }

            return "Sent test messages to " + topLeft + " (corner) and " + center + " (centre) of " +
                   online + " connected player(s). If neither appeared in game, say so - " +
                   "the server log now records delivery counts.";
        }

        /// <summary>Shows a message to one peer. Used to answer admin chat commands.</summary>
        internal void Reply(long peerId, string message, string prefix)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            try
            {
                var znet = ZNet.instance;
                var rpc = ZRoutedRpc.instance;
                if (znet == null || rpc == null || !znet.IsServer())
                {
                    return;
                }

                SendTo(rpc, znet.GetPeer(peerId), Compose(message, prefix), AnnouncementStyle.TopLeft);
            }
            catch (Exception ex)
            {
                _log.LogWarning("Could not reply to peer " + peerId + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Addresses the RPC at the peer's own character. A peer that has not finished
        /// spawning has no character yet and is skipped rather than guessed at.
        /// </summary>
        private bool SendTo(ZRoutedRpc rpc, ZNetPeer peer, string text, AnnouncementStyle style)
        {
            if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone())
            {
                return false;
            }

            try
            {
                rpc.InvokeRoutedRPC(
                    peer.m_uid,
                    peer.m_characterID,
                    MessageRpcName,
                    (int)style,
                    text,
                    0);

                return true;
            }
            catch (Exception ex)
            {
                WarnOnce(ex);
                return false;
            }
        }

        private void WarnOnce(Exception ex)
        {
            if (_warnedUnavailable)
            {
                return;
            }

            _warnedUnavailable = true;
            _log.LogWarning(
                "On-screen announcements are unavailable on this server build; warnings will appear in the " +
                "server log only. Reason: " + ex.Message);
        }

        private static string Compose(string message, string prefix)
        {
            var text = string.IsNullOrEmpty(prefix) ? message : prefix.Trim() + " " + message;
            return text.Length > MaxMessageLength ? text.Substring(0, MaxMessageLength - 3) + "..." : text;
        }

        /// <summary>Replaces the <c>{seconds}</c> token in a configured message template.</summary>
        internal static string Format(string template, int seconds)
        {
            if (string.IsNullOrEmpty(template))
            {
                return string.Empty;
            }

            return template.Replace("{seconds}", seconds.ToString());
        }

        /// <summary>Parses the configured style, defaulting to the corner notice.</summary>
        internal static AnnouncementStyle ParseStyle(string value)
        {
            if (!string.IsNullOrEmpty(value) &&
                value.Trim().Equals("Center", StringComparison.OrdinalIgnoreCase))
            {
                return AnnouncementStyle.Center;
            }

            return AnnouncementStyle.TopLeft;
        }
    }
}
