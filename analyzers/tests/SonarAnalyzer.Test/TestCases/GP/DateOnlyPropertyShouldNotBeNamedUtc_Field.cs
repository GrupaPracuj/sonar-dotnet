namespace GP.Juno.Dates
{
    public struct LocalDate { }
}

public class Contract
{
    private GP.Juno.Dates.LocalDate utcExpirationDate; // Noncompliant {{Rename 'utcExpirationDate' - a date without a time component should not have 'Utc' in its name.}}
}
