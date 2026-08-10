public enum OrderStatus : ulong // Noncompliant {{'OrderStatus' is exposed by a contract with implicit values - reordering or inserting a member would silently change what is already on the wire.}}
{
    Unknown = 0,
    Large = 9223372036854775808UL,
    Next,
}

public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
