namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors SalesService.DTOs.OrderStatusChangedEvent, published by
    /// OrdersController to the default exchange with routing key
    /// "order.status.changed" (the queue name, from
    /// RabbitMQ:Queues:OrderStatusChanged).
    /// </summary>
    public class OrderStatusChangedEvent
    {
        public string OrderId { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;
        public string OldStatus { get; set; } = string.Empty;
        public string NewStatus { get; set; } = string.Empty;
        public DateTime ChangedAt { get; set; }
    }
}
