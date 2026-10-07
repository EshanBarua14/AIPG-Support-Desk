using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// A form posted without a valid anti-forgery token normally ends in an empty "400 Bad Request".
    /// The usual cause is harmless (the page stayed open for hours, or was opened in two tabs with
    /// different users), so send the user back with an explanation instead.
    /// </summary>
    public sealed class AntiforgeryFailureFilter : IAlwaysRunResultFilter
    {
        public void OnResultExecuting(ResultExecutingContext context)
        {
            if (context.Result is not IAntiforgeryValidationFailedResult)
            {
                return;
            }

            var request = context.HttpContext.Request;
            if (SessionAuthorizeAttribute.IsAjax(request))
            {
                context.Result = new ContentResult
                {
                    StatusCode = StatusCodes.Status400BadRequest,
                    Content = "This page is out of date. Reload it and try again."
                };
                return;
            }

            var tempData = context.HttpContext.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(context.HttpContext);
            tempData["ErrorMessage"] = "The page was out of date, so nothing was saved. Please try again.";
            tempData.Remove("Message");
            // The request was stopped before the filter that normally saves TempData ran, so save it here.
            tempData.Save();

            // Back to the page the form was on (same site only), otherwise to the start page.
            var referer = request.Headers["Referer"].ToString();
            if (Uri.TryCreate(referer, UriKind.Absolute, out var from) && string.Equals(from.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new LocalRedirectResult(from.PathAndQuery);
            }
            else
            {
                context.Result = new LocalRedirectResult("~/");
            }
        }

        public void OnResultExecuted(ResultExecutedContext context)
        {
        }
    }
}
