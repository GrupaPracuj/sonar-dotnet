public class Contract
{
    public string Endpoint { get; set; } // Fixed
}

public abstract class BaseHandler
{
    public abstract void Load(int orderId);
}

public class Handler : BaseHandler
{
    // Renaming an override's parameter would only make it disagree with the base declaration, which is what S927
    // reports - so the misspelling is reported here but no automatic rename is offered.
    public override void Load(int orderID) { } // Fixed
}
