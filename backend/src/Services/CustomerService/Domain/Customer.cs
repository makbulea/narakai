namespace ECommerce.Customers.Domain;

public enum CustomerStatus
{
    Active = 0,
    Suspended = 1,

    /// <summary>
    /// Soft-deleted. Orders reference customers by id and must stay readable after the
    /// customer leaves, so the row survives and is filtered out of normal queries.
    /// </summary>
    Deleted = 2
}

public class Customer
{
    public Guid Id { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public CustomerStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token, mapped to Postgres <c>xmin</c>. Two support agents
    /// editing the same customer no longer silently overwrite each other; the second
    /// save fails and the caller gets a 409.
    /// </summary>
    /// <summary>
    /// Optimistic concurrency token, mapped to the Postgres xmin system column via
    /// UseXminAsConcurrencyToken. Two people editing the same row no longer silently
    /// overwrite each other; the second save fails and the caller gets a 409.
    /// </summary>
    public uint Version { get; private set; }

    private Customer() { }

    public static Customer Create(string firstName, string lastName, string email, string? phone)
    {
        var now = DateTimeOffset.UtcNow;

        return new Customer
        {
            Id = Guid.NewGuid(),
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            // Stored lower-cased so the unique index is genuinely case-insensitive.
            // "A@b.com" and "a@b.com" are the same mailbox.
            Email = email.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Status = CustomerStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void UpdateDetails(string firstName, string lastName, string? phone)
    {
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ChangeEmail(string email)
    {
        Email = email.Trim().ToLowerInvariant();
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Suspend()
    {
        Status = CustomerStatus.Suspended;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Reactivate()
    {
        Status = CustomerStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkDeleted()
    {
        Status = CustomerStatus.Deleted;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>Only active customers may place orders. OrderService checks this.</summary>
    public bool CanPlaceOrders => Status == CustomerStatus.Active;
}
