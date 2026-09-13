namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors SalesService.DTOs.PaymentReceivedEvent, published by
    /// PaymentsController to the default exchange with routing key
    /// "payment.received" (the queue name, from RabbitMQ:Queues:PaymentReceived).
    /// </summary>
    public class PaymentReceivedEvent
    {
        public string PaymentId { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime PaidDate { get; set; }
        public DateTime CreatedAt { get; set; }
        // Issue #37: resolved from the payment's Order at the publish site
        // (PaymentsController), so the consumer can push via the registry
        // subject customer:<CustomerId>.
        public int CustomerId { get; set; }
    }
}
