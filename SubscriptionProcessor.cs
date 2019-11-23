using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Data.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using YC.Core;
using YC.Core.Update;
using YC.Update.Watchdog;

namespace Data.Utility.RedisHelper
{
    public class SubscriptionProcessor: ISubscriptionProcessor
    {
        private  bool IsDisposed = false;
        private readonly IServiceUpdateWatchdog _serviceUpdateWatchdog;
        private readonly IRedisConnectionWrapper _redisConnectionWrapper;
        private readonly ILogger _logger;
        
        private const string StatusChannel = "update";
        
        private ConcurrentDictionary<string,UpdatingService> StatusStorage { get; set; }

        public SubscriptionProcessor(IServiceUpdateWatchdog serviceUpdateWatchdog, IRedisConnectionWrapper redisWrapper, ILoggerFactory loggerFactory)
        {
            _serviceUpdateWatchdog = serviceUpdateWatchdog;
            _redisConnectionWrapper = redisWrapper;
            _logger = loggerFactory.CreateLogger<SubscriptionProcessor>();
            
            StatusStorage = new ConcurrentDictionary<string,UpdatingService>();
        }

        public event StatusMessageDelegate StatusMessageChanged;
        
        public void Subscribe(string serviceName, string query)
        {
            string uniqueKey = $"{serviceName}:{query}";
            if (!StatusStorage.ContainsKey(uniqueKey))
            {
                StatusStorage.TryAdd(uniqueKey, null);
            }
        }

        public void Unsubscribe(string serviceName, string query)
        {
            string uniqueKey = $"{serviceName}:{query}";
            StatusStorage.TryRemove(uniqueKey, out UpdatingService value);
            _logger.LogDebug($"Watching task {uniqueKey} ... stopped!");
        }

        public async Task<UpdatingService> GetStatus(string serviceName, string query)
        {
            string uniqueKey = $"{serviceName}:{query}";
            var status = await _serviceUpdateWatchdog.CheckStatus(serviceName, query);
            UpdateResult updateResult = UpdateResult.Failed;
            if (status == ServiceUpdateStatus.Fail ||
                status == ServiceUpdateStatus.Timeout)
            {
                updateResult = UpdateResult.Failed;
            }
            else if (status == ServiceUpdateStatus.NotUpdated)
            {
                updateResult = UpdateResult.Same;
            }
            else if(status == ServiceUpdateStatus.Updating)
            {
                updateResult = UpdateResult.New;
            }
            else if (status == ServiceUpdateStatus.Ok)
            {
                updateResult = UpdateResult.Updated;
            }
            StatusStorage.TryGetValue(uniqueKey, out UpdatingService record);
            if (record != null)
            {
                record.UpdateResult = updateResult;
            }
            return record;
        }

        /// <summary>
        /// Run status listener
        /// </summary>
        public void Run()
        {
            try{
                _redisConnectionWrapper.Connector.GetSubscriber().Subscribe(StatusChannel,
                    async (channel, message) => await OnMessage(message), CommandFlags.None);
                _logger.LogInformation($"Redis status updater listener has started");
            }
            catch(Exception e)
            {
                _logger.LogInformation($"Failed to start Redis status updater listener: {e.Message}");
            }
        }
        
        /// <summary>
        /// Redis messages processor
        /// </summary>
        /// <param name="message">redis message</param>
        /// <returns>string in format: service:searchQuery:statusCode</returns>
        private async Task OnMessage(string message)
        {
            try
            {
                var messageParts = message.Split(":");
                var (service, code, statusStr) = messageParts;
                Enum.TryParse(statusStr, out UpdateResult status);

                var task = new UpdatingService(service, code);
                if (StatusStorage.ContainsKey(task.UniqueId))
                {
                    _logger.LogInformation($"On Message {message}");
                    
                   StatusStorage.TryGetValue(task.UniqueId, out UpdatingService record);
                    if (record == null)
                    {
                        task.UpdateResult = status;
                        StatusStorage.TryAdd(task.UniqueId, task);
                        record = task;
                    }
                    else
                    {
                        record.UpdateResult = status;
                    }
                    
                    switch (status)
                    {
                        case UpdateResult.New:
                            var stat = await _serviceUpdateWatchdog.Debug(task.Service, task.Query);
                            if (stat.Status != ServiceUpdateStatus.Updating)
                            {
                                _logger.LogDebug($"Task {task.UniqueId} broadcasted as updating but not updating");
                                return;
                            }

                            if (!stat.Ttl.HasValue)
                                _logger.LogWarning($"Task {task.UniqueId} has no ttl");
                            _logger.LogDebug($"Watching task {task.UniqueId}");
                            break;
                        case UpdateResult.Failed:
                            _logger.LogError($"Task {task.UniqueId} failed");
                            break;
                        case UpdateResult.Same:
                            _logger.LogInformation($"Task {task.UniqueId} already updated");
                            break;
                        case UpdateResult.Updated:
                            _logger.LogInformation($"Task {task.UniqueId}  updated");
                            break;
                    }

                    if (record.UpdateResult == UpdateResult.Same ||
                        record.UpdateResult == UpdateResult.Failed ||
                        record.UpdateResult == UpdateResult.Updated)
                    {
                        //_redisConnectionWrapper.Connector.GetSubscriber().Unsubscribe(StatusChannel, async (channel, value) => await OnUnsubscribe(value));
                        _logger.LogInformation($"Try to load  info by for request: {task.Query}");

                        if (StatusMessageChanged != null)
                        {
                            await StatusMessageChanged(record);
                            Unsubscribe(record.Service, record.Query);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
        }
    
        private void UnsubscribeRedisAwaiter()
        {
            _redisConnectionWrapper.Connector.GetSubscriber().Unsubscribe(StatusChannel, async (channel, value) => await OnUnsubscribe(value));
        }
        
        private async Task OnUnsubscribe(RedisValue message)
        {
            _logger.LogInformation($"Redis status updater unsubscribed {message}");
        }
        
        private void Dispose(bool disposing)
        {
            if (IsDisposed) return;
            if (disposing)
            {
                try
                {
                    _logger.LogInformation($"Redis status updater unsubscribed ALL");
                    UnsubscribeRedisAwaiter();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception.Message);
                }
            }

            IsDisposed = true;
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        ~SubscriptionProcessor()
        {
            Dispose(false);
        }
    }
}
