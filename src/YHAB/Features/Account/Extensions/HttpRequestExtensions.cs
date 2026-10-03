namespace YHAB.Features.Account.Extensions;

internal static class HttpRequestExtensions
{
    extension(HttpRequest request)
    {
        internal bool IsGet => HttpMethods.IsGet(request.Method);
    }
}
