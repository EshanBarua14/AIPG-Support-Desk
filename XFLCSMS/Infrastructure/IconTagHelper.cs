using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace XFLCSMS.Infrastructure
{
    /// <summary>
    /// &lt;icon name="ticket" /&gt; renders one icon from wwwroot/img/icons.svg (a sprite, so the browser
    /// downloads the icons once). The path goes through Url.Content, so it also works when the site
    /// runs in an IIS virtual directory.
    /// </summary>
    [HtmlTargetElement("icon", Attributes = "name", TagStructure = TagStructure.NormalOrSelfClosing)]
    public class IconTagHelper : TagHelper
    {
        // Change the number when icons.svg changes, so browsers fetch the new file.
        public const string SpriteVersion = "2";

        private readonly IUrlHelperFactory _urlHelperFactory;

        public IconTagHelper(IUrlHelperFactory urlHelperFactory)
        {
            _urlHelperFactory = urlHelperFactory;
        }

        [ViewContext]
        [HtmlAttributeNotBound]
        public ViewContext ViewContext { get; set; } = default!;

        public string Name { get; set; } = string.Empty;

        public override void Process(TagHelperContext context, TagHelperOutput output)
        {
            var sprite = _urlHelperFactory.GetUrlHelper(ViewContext).Content("~/img/icons.svg") + "?v=" + SpriteVersion;

            output.TagName = "svg";
            output.TagMode = TagMode.StartTagAndEndTag;

            var extra = output.Attributes.TryGetAttribute("class", out var existing) ? " " + existing.Value : string.Empty;
            output.Attributes.SetAttribute("class", "icon" + extra);
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Content.SetHtmlContent("<use href=\"" + sprite + "#" + System.Net.WebUtility.HtmlEncode(Name) + "\"></use>");
        }
    }
}
