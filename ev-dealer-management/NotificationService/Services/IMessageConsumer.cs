namespace NotificationService.Services
{
    public interface IMessageConsumer
    {
        void StartConsuming();
        void StopConsuming();

        /// <summary>
        /// True when the broker connection is open. The hosted service polls this
        /// so a connection that dies mid-life is re-opened instead of leaving all
        /// 14 queues silently unconsumed.
        /// </summary>
        bool IsConnected { get; }
    }
}
