using ValheimAutoCleanup.Policy;
using Xunit;

namespace ValheimAutoCleanup.Tests
{
    public class ChatSenderCheckTests
    {
        [Fact]
        public void ClaimMatchingTheConnectionIsVerified()
        {
            Assert.Equal(ChatSenderVerdict.Verified, ChatSenderCheck.Verify(42, 42));
        }

        [Fact]
        public void ClaimNamingAnotherPlayerIsRejected()
        {
            // A modified client connected as peer 7 writes peer 42 - an admin - as the sender.
            Assert.Equal(ChatSenderVerdict.Mismatch, ChatSenderCheck.Verify(42, 7));
        }

        [Fact]
        public void MessageWithNoKnownConnectionIsRejected()
        {
            Assert.Equal(ChatSenderVerdict.NoConnection, ChatSenderCheck.Verify(42, null));
        }

        [Fact]
        public void ZeroClaimIsNotAWildcard()
        {
            Assert.Equal(ChatSenderVerdict.Mismatch, ChatSenderCheck.Verify(0, 7));
        }
    }
}
