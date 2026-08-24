using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

public class OrdersController : ControllerBase
{
    [HttpPost]
    public IActionResult CreateOrder(object order)
    {
        return Ok(order); // Noncompliant {{Method 'CreateOrder' looks like it creates a resource - return 201 (Created/CreatedAtAction) instead of 200 (Ok).}}
    }

    [HttpPost]
    public IActionResult AddOrder(object order)
    {
        return Ok(order); // Noncompliant {{Method 'AddOrder' looks like it creates a resource - return 201 (Created/CreatedAtAction) instead of 200 (Ok).}}
    }

    [HttpPost("{id:guid}")]
    public IActionResult AddResponse(Guid id, object response)
    {
        return Ok(response); // Compliant - this POST is an action on an existing resource, not a collection create
    }

    [HttpPost]
    public IActionResult InsertOrder(object order)
    {
        return Ok(order); // Noncompliant
    }

    [HttpPost]
    public IActionResult RegisterOrder(object order)
    {
        return Ok(order); // Noncompliant
    }

    [HttpPost]
    public IActionResult Create(object order)
    {
        return Ok(order); // Noncompliant
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> CreateOrders(object orders)
    {
        await Task.Yield();
        return Ok(orders); // Noncompliant
    }

    [HttpPost]
    public IActionResult CreateEmptyOrder()
    {
        return Ok(); // Noncompliant - still a 200, the missing payload does not make it a 201
    }

    [HttpPost]
    public IActionResult CreateOrderConditionally(object order, bool draft)
    {
        if (draft)
        {
            return Ok(order); // Noncompliant
        }

        return CreatedAtAction(nameof(CreateOrderConditionally), new { id = 1 }, order);
    }

    [HttpPost]
    public IActionResult CreateOrderWithStatusCode(object order)
    {
        return StatusCode(200, order); // Compliant - the rule is about the Ok factory, an explicit status code is a deliberate choice
    }

    [HttpPost]
    public IActionResult CreateOrderProperly(object order) =>
        CreatedAtAction(nameof(CreateOrderProperly), new { id = 1 }, order); // Compliant

    [HttpPost]
    public IActionResult CreateOrderDryRun(object order)
    {
        return Ok(new { Preview = order }); // Compliant - a DryRun endpoint validates/previews work without creating a resource
    }

    [HttpPost]
    public IActionResult CreateOrderDryRunAsync(object order)
    {
        return Ok(new { Preview = order }); // Compliant
    }

    [HttpPost]
    public IActionResult CreateDryRunOrder(object order)
    {
        return Ok(new { Preview = order }); // Compliant
    }

    [HttpPost]
    public IActionResult CreateOrderWithCreated(object order)
    {
        return Created("/orders/1", order); // Compliant
    }

    [HttpPost]
    public IActionResult CreateOrderExpressionBody(object order) =>
        Ok(order); // FN: the rule only inspects return statements, an expression-bodied action has none

    [HttpGet]
    public IActionResult CreateOrderForm()
    {
        return Ok(new { Fields = 3 }); // Compliant - no POST, nothing is created
    }

    public IActionResult CreateOrderWithoutVerb(object order)
    {
        return Ok(order); // Compliant - not routed as an action without an HTTP verb attribute
    }

    [HttpPost]
    public IActionResult UpdateOrder(object order)
    {
        return Ok(order); // Compliant - updating is not creating
    }

    [HttpPost]
    public IActionResult Login(object credentials)
    {
        return Ok(new { Token = "abc" }); // Compliant
    }

    [HttpPost]
    [NonAction]
    public IActionResult CreateOrderHelper(object order)
    {
        return Ok(order); // Compliant - excluded from routing
    }

    [HttpPost]
    private IActionResult CreateOrderInternal(object order)
    {
        return Ok(order); // Compliant - not a public action
    }

    [HttpPost]
    public static IActionResult CreateOrderStatically(object order)
    {
        return new OkObjectResult(order); // Compliant - a static method is not an action
    }

    [HttpPost]
    public IActionResult CreateOrderInLocalFunction(object order)
    {
        return Local();

        IActionResult Local() => Ok(order); // Compliant - the local function is not the action
    }

    [HttpPost]
    public IActionResult CreateOrderInLambda(object order)
    {
        Func<IActionResult> build = () => { return Ok(order); }; // Compliant - the lambda is not the action
        return build();
    }
}

public class InvoicesController : ControllerBase
{
    [HttpPost]
    public IResult CreateInvoice(object invoice)
    {
        return Results.Ok(invoice); // Noncompliant - an MVC action may return an IResult too
    }

    [HttpPost]
    public IResult AddInvoice(object invoice)
    {
        return TypedResults.Ok(invoice); // Noncompliant
    }

    [HttpPost]
    public IResult CreateInvoiceProperly(object invoice)
    {
        return Results.Created("/invoices/1", invoice); // Compliant
    }
}

public class ReportsController : Controller
{
    [HttpPost]
    public IActionResult CreateReport(object report)
    {
        return Ok(report); // Noncompliant - Controller derives from ControllerBase
    }
}

// "Ok" is resolved to ControllerBase: a same-named helper on the controller itself is not the MVC 200 factory.
public class ReceiptsController : ControllerBase
{
    [HttpPost]
    public object CreateReceipt(object receipt)
    {
        return Ok(receipt, true); // Compliant
    }

    private static object Ok(object value, bool acknowledged) => null;
}

public class OrderFactory
{
    [HttpPost]
    public IActionResult CreateOrder(object order)
    {
        return new OkObjectResult(order); // Compliant - not a controller
    }
}
