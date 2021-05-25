using Basket.API.IntegrationEvents.Events;
using Basket.API.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.Services.Basket.API.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Microsoft.eShopOnContainers.Services.Basket.API.Controllers
{
  [Route("api/v1/[controller]")]
  [ApiController]
  public class BasketController : ControllerBase
  {
    private readonly IBasketRepository _repository;
    private readonly IEventBus _eventBus;
    private readonly ILogger<BasketController> _logger;

    public BasketController(
        ILogger<BasketController> logger,
        IBasketRepository repository,
        IEventBus eventBus)
    {
      _logger = logger;
      _repository = repository;
      _eventBus = eventBus;
      IBasketRepository.eventBus = eventBus;
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CustomerBasket), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<CustomerBasket>> GetBasketByIdAsync(string id)
    {
      var basket = await _repository.GetBasketAsync(id);

      return Ok(basket ?? new CustomerBasket(id));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerBasket), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<CustomerBasket>> UpdateBasketAsync([FromBody] CustomerBasket value)
    {
      return Ok(await _repository.UpdateBasketAsync(value));
    }

    [Route("checkout")]
    [HttpPost]
    [ProducesResponseType((int)HttpStatusCode.Accepted)]
    [ProducesResponseType((int)HttpStatusCode.BadRequest)]
    public async Task<ActionResult> CheckoutAsync([FromBody] BasketCheckout basketCheckout, [FromHeader(Name = "x-requestid")] string requestId, [FromHeader(Name = "userId")] String userIdStr)
    {
      var userId = userIdStr;
      _logger.LogInformation("Checkout controller: " + userId + " PUReqId: " + basketCheckout.PuReqId + " SeqId: "+ basketCheckout.seqId);

      basketCheckout.RequestId = (Guid.TryParse(requestId, out Guid guid) && guid != Guid.Empty) ?
          guid : basketCheckout.RequestId;

      var basket = await _repository.GetBasketAsync(userId);

      if (basket == null)
      {
        return BadRequest();
      }
      
      int prodId;
      try
      {
        prodId = basket.Items[0].ProductId;
      }
      catch
      {
        _logger.LogInformation("No item in teh basket " + userId + " PUSeqId" + basketCheckout.PuReqId);
        return BadRequest();
      }

      // init executionOrder
      if (!IBasketRepository.executionOrder.ContainsKey(prodId))
      {
        IBasketRepository.executionOrder.TryAdd(prodId, 1);
      }

      var userName = "bruce@wayne.com";
      var eventMessage = new UserCheckoutAcceptedIntegrationEvent(userId, userName, basketCheckout.City, basketCheckout.Street,
          basketCheckout.State, basketCheckout.Country, basketCheckout.ZipCode, basketCheckout.CardNumber, basketCheckout.CardHolderName,
          basketCheckout.CardExpiration, basketCheckout.CardSecurityNumber, basketCheckout.CardTypeId, basketCheckout.Buyer, basketCheckout.RequestId, basket,
          basketCheckout.PuReqId, basketCheckout.seqId);

      // Update Waiting Operations
      _repository.UpdateWaitingOperations(prodId, basketCheckout.seqId, (null, eventMessage));

      // Execute Operations
      _repository.ExecuteOperations(prodId);

      return Accepted();
    }

    // DELETE api/values/5
    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(void), (int)HttpStatusCode.OK)]
    public async Task DeleteBasketByIdAsync(string id)
    {
      // Reset locks
      IBasketRepository.executionOrder.Clear();
      IBasketRepository.waitingOperations.Clear();
      _logger.LogError("Cleared the locks");
    }
  }
}
