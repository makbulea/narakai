using BuildingBlocks.Core.Errors;
using BuildingBlocks.Core.Paging;
using BuildingBlocks.Messaging.Contracts;
using BuildingBlocks.Persistence.Outbox;
using ECommerce.Customers.Contracts;
using ECommerce.Customers.Domain;
using ECommerce.Customers.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ECommerce.Customers.Application;

/// <summary>
/// Customer use cases.
///
/// No repository interface sits between this and EF Core. DbContext already is a Unit
/// of Work with a queryable set, and a repository wrapping it here would only forward
/// calls while making the outbox-in-the-same-transaction guarantee harder to see.
/// Where a repository earns its place — OrderService's aggregate loading — there is one.
/// </summary>
public sealed class CustomerService(
    CustomerDbContext db,
    IOutboxWriter outbox,
    ILogger<CustomerService> logger)
{
    public async Task<CustomerResponse> CreateAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Pre-check for a friendly 409. The unique index is still the real guarantee —
        // two simultaneous signups both pass this check and one of them hits the
        // constraint below. Checking first turns the common case into a clear error
        // instead of a database exception.
        if (await db.Customers.AnyAsync(c => c.Email == email, ct))
            throw new ConflictException($"A customer with email '{email}' already exists.");

        var customer = Customer.Create(request.FirstName, request.LastName, request.Email, request.Phone);

        db.Customers.Add(customer);

        // Queued, not published. The row and the event commit together on the
        // SaveChanges below; the OutboxProcessor moves it to Kafka afterwards.
        outbox.Enqueue(
            Topics.CustomerEvents,
            EventTypes.CustomerCreated,
            customer.Id.ToString(),
            new CustomerCreatedPayload(
                customer.Id, customer.Email, customer.FirstName,
                customer.LastName, customer.Phone, customer.CreatedAt));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Lost the race described above.
            throw new ConflictException($"A customer with email '{email}' already exists.");
        }

        logger.LogInformation("Customer {CustomerId} created with email {Email}", customer.Id, customer.Email);

        return CustomerResponse.From(customer);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.Status != CustomerStatus.Deleted, ct)
            ?? throw new NotFoundException("Customer", id);

        return CustomerResponse.From(customer);
    }

    public async Task<CustomerResponse> UpdateAsync(Guid id, UpdateCustomerRequest request, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.Status != CustomerStatus.Deleted, ct)
            ?? throw new NotFoundException("Customer", id);

        customer.UpdateDetails(request.FirstName, request.LastName, request.Phone);

        outbox.Enqueue(
            Topics.CustomerEvents,
            EventTypes.CustomerUpdated,
            customer.Id.ToString(),
            new CustomerUpdatedPayload(
                customer.Id, customer.Email, customer.FirstName, customer.LastName,
                customer.Phone, customer.Status.ToString(), customer.UpdatedAt));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // xmin changed between read and write — someone else edited this customer.
            throw new ConflictException(
                "This customer was modified by someone else. Reload and try again.");
        }

        return CustomerResponse.From(customer);
    }

    /// <summary>
    /// Soft delete. The row stays so that historical orders can still resolve the
    /// customer they belong to; only the status changes.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.Status != CustomerStatus.Deleted, ct)
            ?? throw new NotFoundException("Customer", id);

        customer.MarkDeleted();

        outbox.Enqueue(
            Topics.CustomerEvents,
            EventTypes.CustomerDeleted,
            customer.Id.ToString(),
            new CustomerDeletedPayload(customer.Id, customer.Email, customer.UpdatedAt));

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Customer {CustomerId} soft-deleted", customer.Id);
    }

    public async Task<PagedResult<CustomerResponse>> ListAsync(CustomerQuery query, CancellationToken ct)
    {
        var q = db.Customers.AsNoTracking().Where(c => c.Status != CustomerStatus.Deleted);

        if (query.Status is { } status)
            q = q.Where(c => c.Status == status);

        if (query.CreatedAfter is { } after)
            q = q.Where(c => c.CreatedAt >= after);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = $"%{query.Search.Trim()}%";

            // EF.Functions.ILike maps to Postgres ILIKE — case-insensitive without
            // calling ToLower() on the column, which would make the index unusable.
            q = q.Where(c =>
                EF.Functions.ILike(c.FirstName, term) ||
                EF.Functions.ILike(c.LastName, term) ||
                EF.Functions.ILike(c.Email, term));
        }

        // Count before paging, and only when there is something to count.
        var total = await q.LongCountAsync(ct);
        if (total == 0)
            return PagedResult<CustomerResponse>.Empty(query.Page, query.PageSize);

        q = ApplySort(q, query);

        var items = await q
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(c => CustomerResponse.From(c))
            .ToListAsync(ct);

        return new PagedResult<CustomerResponse>(items, query.Page, query.PageSize, total);
    }

    /// <summary>
    /// Whitelisted sorting. Passing the client's string straight into an expression
    /// would let them order by an unindexed column and table-scan the customer list.
    /// </summary>
    private static IQueryable<Customer> ApplySort(IQueryable<Customer> q, CustomerQuery query) =>
        (query.SortBy?.ToLowerInvariant(), query.SortDescending) switch
        {
            ("email", false) => q.OrderBy(c => c.Email),
            ("email", true) => q.OrderByDescending(c => c.Email),
            ("lastname", false) => q.OrderBy(c => c.LastName).ThenBy(c => c.FirstName),
            ("lastname", true) => q.OrderByDescending(c => c.LastName).ThenByDescending(c => c.FirstName),
            ("createdat", true) => q.OrderByDescending(c => c.CreatedAt),
            ("createdat", false) => q.OrderBy(c => c.CreatedAt),
            _ => q.OrderByDescending(c => c.CreatedAt)
        };

    /// <summary>
    /// Postgres reports a unique-constraint breach as SQLSTATE 23505. Matching on the
    /// code rather than the message keeps this working when the message is localised.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
