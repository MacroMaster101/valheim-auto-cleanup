using System;
using HarmonyLib;

namespace ValheimAutoCleanup.Patches
{
    /// <summary>
    /// The plugin's ONLY Harmony patch: it lets the server observe in-game chat so that
    /// admins can run cleanup commands from an unmodified client.
    ///
    /// WHY IT HOOKS ROUTING RATHER THAN Chat.RPC_ChatMessage
    /// -----------------------------------------------------
    /// The obvious target is <c>Chat.RPC_ChatMessage</c>. That does not work, and neither
    /// does registering a handler for the "ChatMessage" routed RPC, for two separate
    /// reasons found by reading the game:
    ///
    /// 1. A dedicated server creates its own <c>Chat</c> component - the headless log prints
    ///    the "/w [text] - Whisper" help lines that only <c>Chat.Awake</c> emits - so the
    ///    "ChatMessage" RPC name is already registered before any plugin loads, and
    ///    <c>ZRoutedRpc.Register</c> keeps only one delegate per name.
    ///
    /// 2. More decisively, current Valheim does not broadcast chat at all.
    ///    <c>Chat.CheckPermissionsAndSendChatMessageRPCsAsync</c> walks
    ///    <c>ZNet.GetPlayerList()</c> and calls
    ///    <c>InvokeRoutedRPC(thatPlayersId, "ChatMessage", ...)</c> once per player. The
    ///    target is never <c>ZRoutedRpc.Everybody</c> (0), and the server is not a player,
    ///    so it is never a target. <c>ZRoutedRpc.RPC_RoutedRPC</c> only invokes a local
    ///    handler when the message is addressed to this peer or to everybody, which means
    ///    <c>Chat.RPC_ChatMessage</c> never executes on a dedicated server no matter what is
    ///    patched onto it.
    ///
    /// What the server *does* do with every chat message is forward it:
    /// <c>RPC_RoutedRPC</c> calls <c>RouteRPC</c> for anything not addressed to itself, and
    /// that call is guarded by <c>m_server</c>. Hooking there is the only place a dedicated
    /// server reliably sees chat.
    ///
    /// SAFETY
    /// ------
    /// This is a postfix, so routing has already completed before it runs - it cannot delay,
    /// alter or drop anyone's chat. The parameter payload is parsed from a *copy* of the
    /// bytes, so the original package's read position is never disturbed. Every failure is
    /// swallowed.
    ///
    /// Because the client sends one copy per online player, the server sees the same command
    /// N times when N players are connected. <c>ChatCommandListener</c> de-duplicates.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "RouteRPC")]
    internal static class ChatMessagePatch
    {
        /// <summary>Hash of the vanilla chat RPC name, resolved once.</summary>
        private static readonly int ChatMessageHash = "ChatMessage".GetStableHashCode();

        /// <summary>
        /// Set by <c>Plugin</c> once the server is active and chat commands are enabled.
        /// While null the patch does nothing at all.
        /// </summary>
        internal static Server.ChatCommandListener Listener;

        private static void Postfix(ZRoutedRpc.RoutedRPCData rpcData)
        {
            var listener = Listener;
            if (listener == null || rpcData == null || rpcData.m_methodHash != ChatMessageHash)
            {
                return;
            }

            try
            {
                var text = ReadChatText(rpcData.m_parameters);
                if (!string.IsNullOrEmpty(text))
                {
                    listener.OnChatMessage(rpcData.m_senderPeerID, rpcData.m_msgID, text);
                }
            }
            catch (Exception)
            {
                // Chat routing must never be affected by this plugin. The listener logs its
                // own failures; anything reaching here is deliberately ignored.
            }
        }

        /// <summary>
        /// Pulls the message text out of a "ChatMessage" payload.
        ///
        /// The parameters were written by <c>ZRpc.Serialize</c> in the order the client's
        /// handler is registered with: <c>Vector3 position, int type, UserInfo sender,
        /// string text</c>. They are read back from a copy of the byte array so the live
        /// package that is being routed to other players is left completely untouched.
        /// </summary>
        private static string ReadChatText(ZPackage parameters)
        {
            if (parameters == null)
            {
                return null;
            }

            var bytes = parameters.GetArray();
            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            var copy = new ZPackage(bytes);
            copy.SetPos(0);

            copy.ReadVector3();          // position
            copy.ReadInt();              // Talker.Type

            var sender = new UserInfo();
            sender.Deserialize(ref copy); // sender, not trusted - the admin check uses the peer

            return copy.ReadString();     // text
        }
    }
}
