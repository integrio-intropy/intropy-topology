namespace Contracts;

// The shared contract both halves of the system compile against: the shape the extractor
// publishes and the loader consumes. It lives in neither block — mirroring the Contracts/
// library `intropy int create` scaffolds once per workspace.

/// <summary>An order crossing the system, published by the extractor and loaded by the loader.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="CustomerId">The customer identifier.</param>
/// <param name="OrderDate">The time the order was placed.</param>
/// <param name="Lines">The ordered items.</param>
public sealed record Order(string OrderId, string CustomerId, DateTimeOffset OrderDate, IReadOnlyList<OrderLine> Lines);

/// <summary>One ordered item and its unit price.</summary>
/// <param name="Sku">The product stock-keeping unit.</param>
/// <param name="Quantity">The ordered quantity.</param>
/// <param name="UnitPrice">The price for one unit.</param>
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
