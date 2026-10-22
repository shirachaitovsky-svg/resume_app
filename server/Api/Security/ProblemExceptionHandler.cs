using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using ResumeApp.Api.Resources;

namespace ResumeApp.Api.Security;

/// <summary>
/// Turns an unhandled exception into a generic 500 problem+json. Logs only the exception type and the trace id: never the exception object
/// or its message (database messages can carry key values such as emails). The framework's own logging of the exception is silenced in appsettings.
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
        logger.LogError("Unhandled exception of type {ExceptionType} (trace {TraceId})", exception.GetType().Name, traceId);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = { Status = StatusCodes.Status500InternalServerError, Title = Strings.UnexpectedError },
        });
    }
}
