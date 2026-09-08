using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VitrinYa.Services;

namespace VitrinYa.Controllers;

public sealed class ServiceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ServiceException exception) return;
        context.Result = exception.Error switch
        {
            ServiceError.NotFound => new NotFoundResult(),
            ServiceError.Conflict => new ConflictObjectResult(exception.Message),
            _ => new BadRequestObjectResult(exception.Message)
        };
        context.ExceptionHandled = true;
    }
}
