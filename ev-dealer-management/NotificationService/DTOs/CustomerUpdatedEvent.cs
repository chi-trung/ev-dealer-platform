namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors CustomerService.DTOs.CustomerUpdatedEvent (customer_events /
    /// "customer.updated"). CustomerId is the int primary key.
    /// </summary>
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
}
