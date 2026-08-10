public class Contract
{
    public string EndPoint { get; set; } // Noncompliant {{Rename 'EndPoint' to 'Endpoint' - that is the standard spelling for this compound word.}}
}

public abstract class BaseHandler
{
    public abstract void Load(int orderId);
}

public class Handler : BaseHandler
{
    // Renaming an override's parameter would only make it disagree with the base declaration, which is what S927
    // reports - so the misspelling is reported here but no automatic rename is offered.
    public override void Load(int orderID) { } // Noncompliant {{Rename 'orderID' to 'orderId' - that is the standard spelling for this compound word.}}
}
