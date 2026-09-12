namespace ValheimAutoCleanup.Policy
{
    /// <summary>Outcome of checking who really sent a chat command.</summary>
    public enum ChatSenderVerdict
    {
        /// <summary>The claimed sender is the connection the message arrived on.</summary>
        Verified,

        /// <summary>The message's connection could not be identified.</summary>
        NoConnection,

        /// <summary>The message names a different player than the connection it arrived on.</summary>
        Mismatch
    }

    /// <summary>
    /// Decides whether a chat message's claimed sender can be believed.
    ///
    /// Valheim copies the sender ID out of the packet the client wrote and never compares it
    /// with the connection the packet arrived on, so a modified client can put any player's
    /// ID there - including an admin's. The connection is the only identity a client cannot
    /// choose, so a command is obeyed only when the claim and the connection agree.
    /// </summary>
    public static class ChatSenderCheck
    {
        /// <param name="claimedSenderId">The sender ID carried inside the message.</param>
        /// <param name="connectionPeerId">
        /// The peer ID of the connection the message arrived on, or null when it is unknown.
        /// </param>
        public static ChatSenderVerdict Verify(long claimedSenderId, long? connectionPeerId)
        {
            if (connectionPeerId == null)
            {
                return ChatSenderVerdict.NoConnection;
            }

            return connectionPeerId.Value == claimedSenderId
                ? ChatSenderVerdict.Verified
                : ChatSenderVerdict.Mismatch;
        }
    }
}
