using System;
using HarmonyLib;

namespace ValheimAutoCleanup.Patches
{
    /// <summary>
    /// Records which network connection the routed RPC currently being processed arrived on,
    /// so that <see cref="ChatMessagePatch"/> can tell who really sent a chat message.
    ///
    /// WHY THIS IS NEEDED
    /// ------------------
    /// <c>ZRoutedRpc.RPC_RoutedRPC(ZRpc rpc, ZPackage pkg)</c> deserialises the sender ID
    /// straight out of the packet the client wrote and never compares it with <c>rpc</c>, the
    /// connection the packet arrived on. The ID is therefore only a claim: a modified client
    /// can put any player's ID there, including an admin's. The connection is the one
    /// identity a client cannot choose.
    ///
    /// <c>RPC_RoutedRPC</c> calls <c>RouteRPC</c> synchronously, so the value held here is
    /// exactly the connection of the message the chat postfix is looking at. Anything routed
    /// another way - the server's own <c>InvokeRoutedRPC</c> - sees null and is ignored,
    /// which is correct: the server is not a player.
    ///
    /// SAFETY
    /// ------
    /// The prefix only stores a reference and never skips the original. The finalizer runs
    /// even when the original throws, so a connection can never leak into a later message.
    /// </summary>
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class RoutedRpcReceivePatch
    {
        [ThreadStatic]
        private static ZRpc _current;

        /// <summary>The connection of the routed RPC being processed, or null outside one.</summary>
        internal static ZRpc CurrentConnection => _current;

        private static void Prefix(ZRpc __0)
        {
            _current = __0;
        }

        private static Exception Finalizer(Exception __exception)
        {
            _current = null;

            // Returning the original exception (or null) leaves the game's behaviour unchanged.
            return __exception;
        }
    }
}
