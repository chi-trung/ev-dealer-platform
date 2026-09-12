namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors CustomerService.DTOs.CustomerCreatedEvent (customer_events /
    /// "customer.created"). CustomerId is the int primary key.
    /// </summary>
    public class CustomerCreatedEvent
    {
        public int CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
