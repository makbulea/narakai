using BuildingBlocks.Core.Paging;
using ECommerce.Customers.Domain;

namespace ECommerce.Customers.Application;

public sealed record CreateCustomerRequest(string FirstName, string LastName, string Email, string? Phone);

public sealed record UpdateCustomerRequest(string FirstName, string LastName, string? Phone);

public sealed record CustomerResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string FullName,
    string Email,
    string? Phone,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// Entities never leave the service. Mapping here keeps the wire contract stable
    /// when the entity changes, and stops EF navigation properties from being walked
    /// during serialisation.
    /// </summary>
    public static CustomerResponse From(Customer c) => new(
        c.Id, c.FirstName, c.LastName, c.FullName, c.Email, c.Phone,
        c.Status.ToString(), c.CreatedAt, c.UpdatedAt);
}

/// <summary>Filters for the list endpoint. Inherits page/size/sort from PageRequest.</summary>
public sealed record CustomerQuery : PageRequest
{
    /// <summary>Free-text match across name and email.</summary>
    public string? Search { get; init; }

    public CustomerStatus? Status { get; init; }

    public DateTimeOffset? CreatedAfter { get; init; }
}
