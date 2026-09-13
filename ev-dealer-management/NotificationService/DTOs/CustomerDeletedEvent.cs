namespace NotificationService.DTOs
{
    /// <summary>
    /// Mirrors CustomerService.DTOs.CustomerDeletedEvent (customer_events /
    /// "customer.deleted"). CustomerId is the int primary key.
    /// </summary>
    public class CustomerDeletedEvent
    {
        public int CustomerId { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
