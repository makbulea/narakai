namespace BuildingBlocks.Core.Errors;

/// <summary>
/// Base for errors that are part of the domain's vocabulary rather than bugs.
/// These map to 4xx responses; anything else is a 500.
/// </summary>
public abstract class DomainException(string message) : Exception(message)
{
    public abstract string ErrorCode { get; }
}

/// <summary>The requested aggregate does not exist. Maps to 404.</summary>
public sealed class NotFoundException(string resource, object key)
    : DomainException($"{resource} '{key}' was not found.")
{
    public override string ErrorCode => "resource_not_found";
    public string Resource { get; } = resource;
}

/// <summary>
/// The request conflicts with current state — duplicate email, duplicate SKU,
/// cancelling an already-shipped order. Maps to 409.
/// </summary>
public sealed class ConflictException(string message) : DomainException(message)
{
    public override string ErrorCode => "conflict";
}

/// <summary>
/// A business rule rejected the request. Distinct from input validation: the
/// payload was well-formed, the operation is simply not allowed. Maps to 422.
/// </summary>
public sealed class BusinessRuleException(string rule, string message) : DomainException(message)
{
    public override string ErrorCode => "business_rule_violated";
    public string Rule { get; } = rule;
}

/// <summary>
/// A downstream service could not be reached or refused the call. Maps to 502.
/// Kept separate from DomainException on purpose: this one is often retryable,
/// domain errors never are.
/// </summary>
public sealed class DownstreamServiceException(string service, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string Service { get; } = service;
}
