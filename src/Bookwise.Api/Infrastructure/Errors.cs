using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Bookwise.Api.Infrastructure;

/// <summary>A valid request that breaks a business rule. Returned as 409 with a machine-readable code.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public class NotFoundException(string message) : Exception(message);

/// <summary>Turns domain errors into RFC 9457 problem details. Anything else falls through to a 500.</summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, code) = exception switch
        {
            DomainException d => (StatusCodes.Status409Conflict, "Business rule violated", d.Code),
            NotFoundException => (StatusCodes.Status404NotFound, "Not found", "NOT_FOUND"),
            _ => (0, "", ""),
        };
        if (status == 0)
        {
            return false;
        }

        context.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Detail = exception.Message };
        problem.Extensions["code"] = code;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
