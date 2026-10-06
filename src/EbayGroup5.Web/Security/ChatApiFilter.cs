using EbayGroup5.Application.Chat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EbayGroup5.Web.Security;

public sealed class ChatApiFilter : IAsyncResourceFilter, IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ChatException exception) return;
        var status = exception.Error switch { ChatError.Invalid => 400, ChatError.Forbidden => 403, ChatError.NotFound => 404, _ => 409 };
        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) { await next(); return; }
        // Also enforces the actual size for chunked bodies and TestServer, before JSON binding.
        if (request.ContentLength > ChatLimits.MaxBodyBytes) { Reject(context); return; }
        await using var bounded = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, ChatLimits.MaxBodyBytes + 1 - (int)bounded.Length)), context.HttpContext.RequestAborted);
                if (read == 0) break;
                await bounded.WriteAsync(buffer.AsMemory(0, read), context.HttpContext.RequestAborted);
                if (bounded.Length > ChatLimits.MaxBodyBytes) { Reject(context); return; }
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge) { Reject(context); return; }
        var original = request.Body;
        bounded.Position = 0;
        request.Body = bounded;
        try { await next(); }
        finally { request.Body = original; }
    }
    private static void Reject(ResourceExecutingContext context) => context.Result = new ObjectResult(
        new ProblemDetails { Status = 400, Title = "Invalid chat request." }) { StatusCode = 400 };
}
