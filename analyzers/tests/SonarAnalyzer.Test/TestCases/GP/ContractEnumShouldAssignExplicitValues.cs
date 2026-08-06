public enum OrderStatus // Noncompliant {{'OrderStatus' is exposed by a contract with implicit values - reordering or inserting a member would silently change what is already on the wire.}}
{
    Unknown,
    Pending,
}

public sealed record OrderAcceptedContract(System.Guid OrderId, OrderStatus Status);
