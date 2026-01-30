using Microsoft.Extensions.Options;

namespace DevHabit.Api.Extensions;

public static class ContentNegotiationResultExtensions
{
    public static IResult OkWithContentNegotiation<T>(this IResultExtensions _, T obj)
    {
        return new OkContentNegotiation<T>(obj);
    }

    private sealed class OkContentNegotiation<TValue> :
        IResult,
        IStatusCodeHttpResult,
        IValueHttpResult,
        IValueHttpResult<TValue>
    {
        internal OkContentNegotiation(TValue? value)
        {
            Value = value;
        }

        public TValue? Value { get; }

        object? IValueHttpResult.Value => Value;

        private static int StatusCode => StatusCodes.Status200OK;

        int? IStatusCodeHttpResult.StatusCode => StatusCode;

        public async Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            httpContext.Response.StatusCode = StatusCode;

            await httpContext.Response.WriteAsJsonAsync(
                Value,
                options: httpContext.RequestServices
                    .GetService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()?
                    .Value.SerializerOptions,
                contentType: httpContext.Request.Headers.Accept.ToString());
        }
    }
}
