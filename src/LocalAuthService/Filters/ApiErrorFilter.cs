using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Authentication;

namespace LocalAuthService.Filters;

/// <summary>
/// Exception filter globale che converte tutte le eccezioni in risposte REST standardizzate
/// </summary>
public class ApiErrorFilter : IExceptionFilter
{
    private readonly ILogger<ApiErrorFilter> _logger;

    public ApiErrorFilter(ILogger<ApiErrorFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        var exception = context.Exception;
        var response = new ApiErrorResponse();
        var statusCode = 500;

        switch (exception)
        {
            case ArgumentException argEx:
                response.Error = "invalid_argument";
                response.Message = argEx.Message;
                statusCode = 400;
                break;

            case UnauthorizedAccessException uaEx:
                response.Error = "insufficient_access";
                response.Message = uaEx.Message;
                statusCode = 403;
                break;

            case KeyNotFoundException knfEx:
                response.Error = "not_found";
                response.Message = knfEx.Message;
                statusCode = 404;
                break;

            case InvalidOperationException ioEx:
                response.Error = "invalid_operation";
                response.Message = ioEx.Message;
                statusCode = 400;
                break;

            default:
                response.Error = "internal_error";
                response.Message = "Si è verificato un errore interno";
                _logger.LogError(exception, "Unhandled exception: {ExceptionType}", exception.GetType().Name);
                statusCode = 500;
                break;
        }

        context.Result = new ObjectResult(response) { StatusCode = statusCode };
        context.ExceptionHandled = true;
    }
}

/// <summary>
/// Formato standard per le risposte di errore REST
/// </summary>
public class ApiErrorResponse
{
    /// <summary>
    /// Codice di errore standardizzato (es: "insufficient_access", "not_found", "invalid_argument")
    /// </summary>
    public string Error { get; set; } = "internal_error";

    /// <summary>
    /// Messaggio leggibile dall'utente
    /// </summary>
    public string Message { get; set; } = "An error occurred";

    /// <summary>
    /// Timestamp dell'errore per logging e troubleshooting
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Request ID per correlazione nei log
    /// </summary>
    public string? RequestId { get; set; }
}
