using System;

public class Contract
{
    public DateOnly ExpirationDateUtc { get; set; } // Noncompliant {{Rename 'ExpirationDateUtc' - a date without a time component should not have 'Utc' in its name.}}
}
