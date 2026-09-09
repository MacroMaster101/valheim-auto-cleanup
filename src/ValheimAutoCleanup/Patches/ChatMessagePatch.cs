using System;
using HarmonyLib;
using UnityEngine;

namespace ValheimAutoCleanup.Patches
{
    /// <summary>
    /// The plugin's ONLY Harmony patch. Everything else uses public game APIs.
    ///
    /// WHY THIS PATCH IS NECESSARY
    /// ---------------------------
    /// Admin chat commands need the server to see chat messages sent by players. Chat is a
    /// routed RPC named "ChatMessage", and the obvious patch-free approach is to register a
    /// handler for that name on the server's <c>ZRoutedRpc</c>.
    ///
    /// That approach was implemented first and does not work. A Valheim dedicated server
    /// creates a <c>Chat</c> component of its own - the headless server log prints the
    /// "/w [text] - Whisper" and "/s [text] - Shout" help lines that only <c>Chat.Awake</c>
    /// emits - so <c>Chat.Awake</c> has already called
    ///
    ///     ZRoutedRpc.Register&lt;Vector3, int, UserInfo, string&gt;("ChatMessage", RPC_ChatMessage)
    ///
    /// by the time any plugin runs. <c>ZRoutedRpc.Register</c> stores exactly one delegate
    /// per name and overwrites silently, so registering our own would *replace* the game's
    /// handler rather than run alongside it. There is no public way to add a second listener
    /// and no public event to subscribe to.
    ///
    /// A patch is therefore the only way to observe chat server-side without displacing
    /// vanilla behaviour.
    ///
    /// WHY A PREFIX RATHER THAN A POSTFIX
    /// ----------------------------------
    /// The patched method leads to <c>Chat.OnNewChatMessage</c>, which calls
    /// <c>RelationsManager.CheckPermissionAsync</c> and from there dereferences
    /// <c>PlatformManager.DistributionPlatform.LocalUser</c>. Whether that is initialised on
    /// a headless server is not something this plugin should depend on. A prefix runs before
    /// any of it, so command handling works even if the vanilla path later fails.
    ///
    /// The prefix returns void and never reports that it handled the call, so the original
    /// method always runs afterwards exactly as it would have. It is read-only with respect
    /// to game state, and every failure inside it is swallowed.
    /// </summary>
    [HarmonyPatch(typeof(Chat), "RPC_ChatMessage")]
    internal static class ChatMessagePatch
    {
        /// <summary>
        /// Set by <c>Plugin</c> once the server is active and chat commands are enabled.
        /// While null the patch does nothing at all.
        /// </summary>
        internal static Server.ChatCommandListener Listener;

        // ReSharper disable once InconsistentNaming - Harmony parameter naming convention.
        private static void Prefix(long sender, Vector3 position, int type, UserInfo userInfo, string text)
        {
            var listener = Listener;
            if (listener == null)
            {
                return;
            }

            try
            {
                listener.OnChatMessage(sender, text);
            }
            catch (Exception)
            {
                // Never let this plugin interfere with the game's chat handling. The listener
                // already logs its own failures; anything reaching here is swallowed on
                // purpose so the original method still runs untouched.
            }
        }
    }
}
