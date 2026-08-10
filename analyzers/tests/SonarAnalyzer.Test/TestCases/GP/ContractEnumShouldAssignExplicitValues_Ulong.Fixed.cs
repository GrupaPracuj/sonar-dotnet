public enum OrderStatus : ulong // Fixed
{
    Unknown = 0,
    Large = 9223372036854775808UL,
    Next = 9223372036854775809UL,
}

public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
