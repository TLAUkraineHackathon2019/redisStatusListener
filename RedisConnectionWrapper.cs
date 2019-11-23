using System;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using YC.Update.Watchdog.Config;

namespace Data.Utility.RedisHelper
{
    public class RedisConnectionWrapper : IRedisConnectionWrapper, IDisposable
    {
        public ConnectionMultiplexer Connector { get; }
        public IDatabase Database { get; }

        public RedisConnectionWrapper(IOptions<WatchdogConfig> options)
        {
            var config = options.Value;
            Connector = ConnectionMultiplexer.Connect(config.DSN);
            Database = Connector.GetDatabase(config.DatabaseId);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Connector?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~RedisConnectionWrapper()
        {
            Dispose(false);
        }
    }

}