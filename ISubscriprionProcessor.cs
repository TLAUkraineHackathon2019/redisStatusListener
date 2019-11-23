using System;
using System.Threading.Tasks;
using Data.Models;

namespace Data.Utility.RedisHelper
{
    public delegate Task StatusMessageDelegate(UpdatingService args);
    public interface ISubscriptionProcessor: IDisposable
    {
        event StatusMessageDelegate StatusMessageChanged;
        void Subscribe(string serviceName, string query);
        void Unsubscribe(string serviceName, string query);
        Task<UpdatingService> GetStatus(string serviceName, string query);
        void Run();
    }
}
