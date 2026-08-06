public enum OrderStatus // Fixed
{
    Unknown = 0,
    Pending = 1,
}

public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
