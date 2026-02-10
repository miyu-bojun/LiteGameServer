using GameShared.Messages;
using Xunit;

namespace GameShared.Tests;

/// <summary>
/// PacketFrameDecoder 单元测试
/// </summary>
public class PacketFrameDecoderTests
{
    [Fact]
    public void OnDataReceived_SinglePacket_ShouldDecodeCorrectly()
    {
        // Arrange
        var decoder = new PacketFrameDecoder();
        var message = new C2S_Login { Account = "test", Token = "token", Platform = 1 };
        byte[] packet = PacketCodec.Encode(message);

        // Act
        var results = decoder.OnDataReceived(packet);

        // Assert
        Assert.Single(results);
        Assert.Equal(1001, results[0].messageId);
        var login = Assert.IsType<C2S_Login>(results[0].message);
        Assert.Equal("test", login.Account);
    }

    [Fact]
    public void OnDataReceived_StickyPackets_ShouldDecodeBothPackets()
    {
        // Arrange
        var decoder = new PacketFrameDecoder();
        var msg1 = new C2S_Login { Account = "user1", Token = "token1", Platform = 1 };
        var msg2 = new C2S_Login { Account = "user2", Token = "token2", Platform = 2 };
        byte[] packet1 = PacketCodec.Encode(msg1);
        byte[] packet2 = PacketCodec.Encode(msg2);

        // 模拟粘包：两个包合并发送
        byte[] stickyData = new byte[packet1.Length + packet2.Length];
        packet1.CopyTo(stickyData, 0);
        packet2.CopyTo(stickyData, packet1.Length);

        // Act
        var results = decoder.OnDataReceived(stickyData);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(1001, results[0].messageId);
        Assert.Equal(1001, results[1].messageId);
        var login1 = Assert.IsType<C2S_Login>(results[0].message);
        var login2 = Assert.IsType<C2S_Login>(results[1].message);
        Assert.Equal("user1", login1.Account);
        Assert.Equal("user2", login2.Account);
    }

    [Fact]
    public void OnDataReceived_FragmentedPacket_ShouldDecodeAfterReceivingAllData()
    {
        // Arrange
        var decoder = new PacketFrameDecoder();
        var message = new C2S_Login { Account = "test", Token = "token", Platform = 1 };
        byte[] packet = PacketCodec.Encode(message);

        // 模拟拆包：一个包分两次发送
        byte[] part1 = packet.AsSpan(0, packet.Length / 2).ToArray();
        byte[] part2 = packet.AsSpan(packet.Length / 2).ToArray();

        // Act
        var results1 = decoder.OnDataReceived(part1);
        var results2 = decoder.OnDataReceived(part2);

        // Assert
        Assert.Empty(results1); // 第一次接收不够一个完整包
        Assert.Single(results2); // 第二次接收后解析出完整包
        Assert.Equal(1001, results2[0].messageId);
        var login = Assert.IsType<C2S_Login>(results2[0].message);
        Assert.Equal("test", login.Account);
    }
}
