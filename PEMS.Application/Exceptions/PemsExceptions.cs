namespace PEMS.Application.Exceptions;

/// <summary>
/// The error taxonomy of the application (SCRUM-464): what went wrong, not how it is shown. One mapping table (guideline chapter 08, PemsErrorMapper in PEMS.Web)
/// turns a kind into the HTTP status, the stable code and the localised text; no exception message ever reaches a user.
/// </summary>
public enum PemsErrorKind
{
    /// <summary>The record or resource does not exist (or the caller may not know it exists).</summary>
    NotFound,
    /// <summary>The request is fine but the current state refuses it: concurrency, a unique key, a frozen document, a wrong workflow stage.</summary>
    Conflict,
    /// <summary>The input breaks a rule the user can fix.</summary>
    Validation,
    /// <summary>The caller is known and not allowed.</summary>
    Forbidden,
    /// <summary>A dependency is down or too slow (database, storage, mail); trying again later can work.</summary>
    Unavailable,
    /// <summary>Anything else: a defect. Shown generically, logged in full.</summary>
    Unexpected,
}

/// <summary>Base of the taxonomy. <see cref="Code"/> is stable (clients and tests depend on it); <see cref="Exception.Message"/> is for the log only.</summary>
public class PemsException : Exception
{
    public PemsException(PemsErrorKind kind, string code, string message, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        Code = code;
    }

    public PemsErrorKind Kind { get; }

    public string Code { get; }
}

/// <summary>The record does not exist. <paramref name="what"/> names it for the log (for example "WorkOrder 17").</summary>
public sealed class NotFoundException : PemsException
{
    public NotFoundException(string what) : base(PemsErrorKind.NotFound, "not-found", what + " was not found.") { }
}

/// <summary>The current state refuses the request. <paramref name="code"/> is a short stable word such as <c>unique</c>, <c>concurrency</c>, <c>frozen</c>, <c>stage</c>.</summary>
public sealed class ConflictException : PemsException
{
    public ConflictException(string code, string message, Exception? inner = null) : base(PemsErrorKind.Conflict, code, message, inner) { }
}

/// <summary>The input breaks rules; <see cref="Fields"/> lists them as (JSON pointer or field name, constraint code).</summary>
public sealed class PemsValidationException : PemsException
{
    public PemsValidationException(string message, IReadOnlyList<(string Field, string Code)>? fields = null)
        : base(PemsErrorKind.Validation, "validation", message) => Fields = fields ?? Array.Empty<(string, string)>();

    public IReadOnlyList<(string Field, string Code)> Fields { get; }
}

/// <summary>The caller is known and may not do this.</summary>
public sealed class ForbiddenException : PemsException
{
    public ForbiddenException(string message) : base(PemsErrorKind.Forbidden, "forbidden", message) { }
}

/// <summary>A dependency is down or too slow. <see cref="RetryAfterSeconds"/> is a hint for the client.</summary>
public sealed class UnavailableException : PemsException
{
    public UnavailableException(string code, string message, int retryAfterSeconds = 5, Exception? inner = null) : base(PemsErrorKind.Unavailable, code, message, inner)
        => RetryAfterSeconds = retryAfterSeconds;

    public int RetryAfterSeconds { get; }
}
