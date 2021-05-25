namespace Microsoft.eShopOnContainers.Services.Catalog.API.IntegrationEvents.Events
{
  using System;
  using BuildingBlocks.EventBus.Events;

    // Integration Events notes: 
    // An Event is “something that has happened in the past”, therefore its name has to be past tense
    // An Integration Event is an event that can cause side effects to other microservices, Bounded-Contexts or external systems.
    public record ProductPriceChangedIntegrationEvent : IntegrationEvent
    {
        public int ProductId { get; private init; }

        public decimal NewPrice { get; private init; }

        public decimal OldPrice { get; private init; }

        public int SeqId { get; private init; }
        public Guid PuReqId {get; set; }

        public ProductPriceChangedIntegrationEvent(int productId, decimal newPrice, decimal oldPrice, int seqId, Guid puReqId)
        {
            ProductId = productId;
            NewPrice = newPrice;
            OldPrice = oldPrice;
            SeqId = seqId;
            PuReqId = puReqId;

        }
    }
}
