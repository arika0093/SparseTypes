using Microsoft.AspNetCore.Components.Forms;

namespace SparseTypes.Blazor.Tests;

public sealed class BlazorValidationErrorTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    [Test]
    public void NestedSessionFieldAccepted()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        var namePath = OrderDto.SparsePath.Customer.Name;
        session.AddValidationError(store, session.Field(namePath), "Required.");

        editContext.GetValidationMessages(session.Field(namePath)).ShouldContain("Required.");
    }

    [Test]
    public void ListElementAndDictionaryValueFieldsAccepted()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        var quantityPath = OrderDto.SparsePath.Lines.At(0).Quantity;
        var contactPath = OrderDto.SparsePath.Contacts.Key("billing").Name;
        session.AddValidationError(store, session.Field(quantityPath), "Must be positive.");
        session.AddValidationError(store, session.Field(contactPath), "Required.");

        editContext
            .GetValidationMessages(session.Field(quantityPath))
            .ShouldContain("Must be positive.");
        editContext.GetValidationMessages(session.Field(contactPath)).ShouldContain("Required.");
    }

    [Test]
    public void TypedPathOverloadResolvesSessionFields()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        var namePath = OrderDto.SparsePath.Customer.Name;

        session.AddValidationError(store, namePath, "Required.");

        editContext.GetValidationMessages(session.Field(namePath)).ShouldContain("Required.");
        Should.Throw<ArgumentException>(() =>
            session.AddValidationError(
                store,
                SparsePath.Parse<OrderDto>("Customer.Unknown"),
                "No such member."
            )
        );
    }

    [Test]
    public void UnrelatedSiblingGraphRejected()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        var sibling = Order().CreateEditSession();

        var thrown = Should.Throw<ArgumentException>(() =>
            session.AddValidationError(
                store,
                sibling.Field(OrderDto.SparsePath.Customer.Name),
                "Wrong graph."
            )
        );
        thrown.ParamName.ShouldBe("field");
        editContext.GetValidationMessages().ShouldBeEmpty();
    }

    [Test]
    public void DetachedElementRejected()
    {
        var session = Order().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        var detached = new FieldIdentifier(
            new OrderCustomer { Name = "Zed" },
            nameof(OrderCustomer.Name)
        );

        Should.Throw<ArgumentException>(() =>
            session.AddValidationError(store, detached, "Detached.")
        );
        editContext.GetValidationMessages().ShouldBeEmpty();
    }
}
