using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// Gives every &lt;script&gt; written in a view the nonce of the response, so the Content-Security-Policy lets it
    /// run (see <see cref="SecurityHeaders"/>). A script that reaches a page any other way - typed into a ticket,
    /// injected through a field somebody forgot to encode - has no nonce and is not run by the browser.
    /// </summary>
    [HtmlTargetElement("script")]
    public class ScriptNonceTagHelper : TagHelper
    {
        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            output.Attributes.SetAttribute("nonce", SecurityHeaders.Nonce(ViewContext.HttpContext));
        }
    }
}
