namespace Contracts;

// The shared contract both halves of the system compile against: the shape the extractor
// publishes and the loader consumes. It lives in neither block — mirroring the Contracts/
// library `intropy int create` scaffolds once per workspace.

/// <summary>An order crossing the system, published by the extractor and loaded by the loader.</summary>
public sealed record Order(string OrderId, string CustomerId, DateTimeOffset OrderDate, IReadOnlyList<OrderLine> Lines);

/// <summary>One line of an order.</summary>
public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
