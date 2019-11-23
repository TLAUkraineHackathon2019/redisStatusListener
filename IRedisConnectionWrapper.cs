using StackExchange.Redis;

namespace Data.Utility.RedisHelper
{
    public interface IRedisConnectionWrapper
    {
        ConnectionMultiplexer Connector { get; }
    }
}