namespace ECommerce.Customers.Contracts;

/// <summary>
/// Payloads this service publishes. They live here, not in a shared contracts library:
/// a consumer that wants CustomerCreated declares its own local shape with the fields
/// it needs. That is what lets this service add a field without a coordinated deploy
/// of every consumer.
/// </summary>
public sealed record CustomerCreatedPayload(
    Guid CustomerId,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    DateTimeOffset CreatedAt);

public sealed record CustomerUpdatedPayload(
    Guid CustomerId,
    string Email,
    string FirstName,
    string LastName,
    string? Phone,
    string Status,
    DateTimeOffset UpdatedAt);

public sealed record CustomerDeletedPayload(
    Guid CustomerId,
    string Email,
    DateTimeOffset DeletedAt);
