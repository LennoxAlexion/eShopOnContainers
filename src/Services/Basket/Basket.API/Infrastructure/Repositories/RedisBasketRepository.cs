using Basket.API.IntegrationEvents.Events;
using Microsoft.eShopOnContainers.Services.Basket.API.IntegrationEvents.Events;
using Microsoft.eShopOnContainers.Services.Basket.API.Model;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.eShopOnContainers.Services.Basket.API.Infrastructure.Repositories
{
  public class RedisBasketRepository : IBasketRepository
  {
    private readonly ILogger<RedisBasketRepository> _logger;
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _database;

    public RedisBasketRepository(ILoggerFactory loggerFactory, ConnectionMultiplexer redis)
    {
      _logger = loggerFactory.CreateLogger<RedisBasketRepository>();
      _redis = redis;
      _database = redis.GetDatabase();
    }

    public async Task<bool> DeleteBasketAsync(string id)
    {
      return await _database.KeyDeleteAsync(id);
    }

    public IEnumerable<string> GetUsers()
    {
      var server = GetServer();
      var data = server.Keys();

      return data?.Select(k => k.ToString());
    }

    public async Task<CustomerBasket> GetBasketAsync(string customerId)
    {
      var data = await _database.StringGetAsync(customerId);

      if (data.IsNullOrEmpty)
      {
        return null;
      }

      return JsonConvert.DeserializeObject<CustomerBasket>(data);
    }

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket)
    {
      var created = await _database.StringSetAsync(basket.BuyerId, JsonConvert.SerializeObject(basket));

      if (!created)
      {
        _logger.LogInformation("Problem occur persisting the item.");
        return null;
      }

      _logger.LogInformation("Basket item persisted succesfully.");

      return await GetBasketAsync(basket.BuyerId);
    }

    private IServer GetServer()
    {
      var endpoint = _redis.GetEndPoints();
      return _redis.GetServer(endpoint.First());
    }

    public void UpdateWaitingOperations(int itemId, int seqId, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent) operation)
    {
      ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)> seqIdOperationDict = IBasketRepository.waitingOperations.GetValueOrDefault(itemId, new ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)>());

      if (IBasketRepository.waitingOperations.TryGetValue(itemId, out seqIdOperationDict))
      {
        // Add by dict reference
        seqIdOperationDict.TryAdd(seqId, operation);
      }
      else
      {
        // Initialize the waiting operation
        seqIdOperationDict = new ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)>();
        seqIdOperationDict.TryAdd(seqId, operation);
        IBasketRepository.waitingOperations.TryAdd(itemId, seqIdOperationDict);
      }
      if (operation.Item2 == null)
      {
        _logger.LogError("UpdateWaitingOperations (PU) -> itemId = {itemId}; SeqId = {seqId}; waitOpsCount = {waitOp} ", itemId, seqId, IBasketRepository.waitingOperations.Count);
      }

      if (operation.Item1 == null)
      {
        _logger.LogError("UpdateWaitingOperations (CO) -> itemId = {itemId}; SeqId = {seqId}; waitOpsCount = {waitOp} ", itemId, seqId, IBasketRepository.waitingOperations.Count);
      }
    }

    public void ExecuteOperations(int itemId)
    {
      ConcurrentDictionary<int, (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent)> seqIdOperationDict;
      if (!IBasketRepository.waitingOperations.TryGetValue(itemId, out seqIdOperationDict))
      {
        _logger.LogError("Error no operations to execute for item Id {itemId}", itemId);
      }
      else
      {
        int seqId;
        if (!IBasketRepository.executionOrder.TryGetValue(itemId, out seqId))
        {
          _logger.LogError("Error getting seqId {seqId} for itemId {itemId}", seqId, itemId);
        }
        else
        {
          (ProductPriceChangedIntegrationEvent, UserCheckoutAcceptedIntegrationEvent) operation;
          // Wait for pending requests to arrive before execute operations waiting in the queue based on SeqId.
          while (seqIdOperationDict.TryRemove(seqId, out operation))
          {
            _logger.LogError("Item Id -> {ItemId}; Current SeqId -> {seqId}", itemId, seqId);
            if (operation.Item1 != null)
            {
              // Execute Price Update
              _logger.LogError("ExecuteOperations: Process PU -> itemID = {itemId}, SeqId = {seqId}, newPrice = {newPrice}", itemId, seqId, operation.Item1.NewPrice);
              processPriceUpdate(operation.Item1).Wait();
            }
            else if (operation.Item2 != null)
            {
              // Execute Checkout
              _logger.LogError("ExecuteOperations: Process Checkout -> itemID = {itemId}, SeqId = {seqId}, checkout = {operation}", itemId, seqId, operation.Item2.PUReqId);
              processCheckout(operation.Item2).Wait();
            }
            seqId++;
            IBasketRepository.executionOrder[itemId] = seqId;
          }
          _logger.LogError("ExecuteOperations: Waiting for pending requests... executionOrder = " + string.Join(Environment.NewLine, IBasketRepository.executionOrder));
        }
      }
    }

    private async Task processPriceUpdate(ProductPriceChangedIntegrationEvent @event)
    {
      _logger.LogInformation("----- Updating basket price at {AppName} - ({@IntegrationEvent})", @event.Id, Program.AppName, @event);

      var userIds = GetUsers();

      foreach (var id in userIds)
      {
        var basket = await GetBasketAsync(id);
        await UpdatePriceInBasketItems(@event.ProductId, @event.NewPrice, @event.OldPrice, basket);
      }
    }
    private async Task UpdatePriceInBasketItems(int productId, decimal newPrice, decimal oldPrice, CustomerBasket basket)
    {
      var itemsToUpdate = basket?.Items?.Where(x => x.ProductId == productId).ToList();

      if (itemsToUpdate != null && itemsToUpdate.Count != 0)
      {
        _logger.LogWarning("----- ProductPriceChangedIntegrationEventHandler - Updating items in basket for user: {BuyerId} ({@Items})", basket.BuyerId, itemsToUpdate);

        foreach (var item in itemsToUpdate)
        {
          if (item.UnitPrice == oldPrice)
          {
            var originalPrice = item.UnitPrice;
            item.UnitPrice = newPrice;
            item.OldUnitPrice = originalPrice;
          }
        }
        await UpdateBasketAsync(basket);
      }
    }

    private async Task processCheckout(UserCheckoutAcceptedIntegrationEvent checkoutData)
    {
      _logger.LogWarning("Checking out user counter: " + checkoutData.UserId);
      // Once basket is checkout, sends an integration event to
      // ordering.api to convert basket to order and proceeds with
      // order creation process
      // Get the latest basket.
      var basket = await GetBasketAsync(checkoutData.UserId);
      checkoutData.UpdateBasket(basket);
      var eventMessage = checkoutData;
      try
      {
        IBasketRepository.eventBus.Publish(eventMessage);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "ERROR Publishing integration event: {IntegrationEventId} from {AppName}", eventMessage.Id, Program.AppName);

        throw;
      }
    }
  }
}
