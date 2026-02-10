using GameShared.Messages;
using Xunit;

namespace GameShared.Tests;

/// <summary>
/// PacketCodec 单元测试
/// </summary>
public class PacketCodecTests
{
    [Fact]
    public void Encode_Decode_ShouldPreserveMessageContent()
    {
        // Arrange
        var original = new C2S_Login
        {
            Account = "test_user",
            Token = "test_token",
            Platform = 1
        };

        // Act
        byte[] packet = PacketCodec.Encode(original);
        var (messageId, decoded) = PacketCodec.Decode(packet);

        // Assert
        Assert.Equal(1001, messageId); // C2S_Login 的消息 ID
        var login = Assert.IsType<C2S_Login>(decoded);
        Assert.Equal("test_user", login.Account);
        Assert.Equal("test_token", login.Token);
        Assert.Equal(1, login.Platform);
    }

    [Fact]
    public void Decode_UnknownMessageId_ShouldThrowException()
    {
        // Arrange
        byte[] packet = new byte[10]; // 伪造一个无效的数据包

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => PacketCodec.Decode(packet));
    }
}
