namespace CustomerService.DTOs
{
    /// <summary>
    /// Published to the "customer_events" topic exchange, routing key "customer.created".
    /// CustomerId is the int primary key - the previous Guid round-trip hack is gone.
    /// </summary>
    public class CustomerCreatedEvent
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class CustomerUpdatedEvent
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class CustomerDeletedEvent
    {
        public int CustomerId { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
