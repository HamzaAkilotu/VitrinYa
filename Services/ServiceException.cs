namespace VitrinYa.Services;

public enum ServiceError { Validation, NotFound, Conflict }

// Application failures contain no HTTP, view or redirect information.
public sealed class ServiceException(string message, ServiceError error = ServiceError.Validation, string field = "") : Exception(message)
{
    public ServiceError Error { get; } = error;

    public string Field { get; } = field;
}
