using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Basket.API.IntegrationEvents.Events;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.Services.Basket.API.IntegrationEvents.Events;

namespace Microsoft.eShopOnContainers.Services.Basket.API.Model
{
    public interface IBasketRepository
    {
        static ConcurrentDictionary<int, int> executionOrder = new ConcurrentDictionary<int, int>(); // <itemId, nextSeqId>
        static ConcurrentDictionary<int, ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)>> waitingOperations = new ConcurrentDictionary<int, ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)>>();
        static IEventBus eventBus;
        Task<CustomerBasket> GetBasketAsync(string customerId);
        IEnumerable<string> GetUsers();
        Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket);
        Task<bool> DeleteBasketAsync(string id);
        void UpdateWaitingOperations(int itemId, int seqId, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent) operation);
        void ExecuteOperations(int itemId);
    }
}
