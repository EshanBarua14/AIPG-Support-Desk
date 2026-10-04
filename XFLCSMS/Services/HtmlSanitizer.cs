using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Allow-list sanitizer for the rich-text "Issue Details" field. The ticket views print that field
    /// with Html.Raw, so without this any user could store a script that runs in the browser of the
    /// admin / support engineer who opens the ticket (stored XSS).
    /// </summary>
    public static class HtmlSanitizer
    {
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "p", "br", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup", "span", "div", "font",
            "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "code", "hr",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "colgroup", "col", "a", "img"
        };

        private static readonly HashSet<string> AllowedAttributes = new(StringComparer.OrdinalIgnoreCase)
        {
            "href", "src", "alt", "title", "class", "style", "color", "face", "size", "target", "rel",
            "width", "height", "colspan", "rowspan", "align", "valign", "border", "cellpadding", "cellspacing"
        };

        private static readonly Regex TagRegex = new(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>""']|""[^""]*""|'[^']*')*)>", RegexOptions.Compiled);
        private static readonly Regex AttributeRegex = new(@"([a-zA-Z][a-zA-Z0-9\-]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Compiled);
        private static readonly Regex DroppedBlocks = new(@"<(script|style|iframe|object|embed|form|svg|math|noscript|template)\b.*?(</\1\s*>|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex Comments = new(@"<!--.*?(-->|$)", RegexOptions.Compiled | RegexOptions.Singleline);

        public static string Sanitize(string? html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            html = Comments.Replace(html, string.Empty);
            html = DroppedBlocks.Replace(html, string.Empty);

            var output = new StringBuilder(html.Length);
            var position = 0;
            foreach (Match tag in TagRegex.Matches(html))
            {
                AppendText(output, html.Substring(position, tag.Index - position));
                position = tag.Index + tag.Length;

                var name = tag.Groups[2].Value.ToLowerInvariant();
                if (!AllowedTags.Contains(name))
                {
                    continue; // unknown tag: drop the tag, keep its inner text
                }

                if (tag.Groups[1].Value == "/")
                {
                    output.Append("</").Append(name).Append('>');
                    continue;
                }

                output.Append('<').Append(name);
                foreach (Match attribute in AttributeRegex.Matches(tag.Groups[3].Value))
                {
                    var attributeName = attribute.Groups[1].Value.ToLowerInvariant();
                    if (!AllowedAttributes.Contains(attributeName))
                    {
                        continue; // drops every on* handler and anything else not on the list
                    }

                    var value = attribute.Groups[2].Success ? attribute.Groups[2].Value
                              : attribute.Groups[3].Success ? attribute.Groups[3].Value
                              : attribute.Groups[4].Value;
                    value = WebUtility.HtmlDecode(value);

                    if ((attributeName == "href" || attributeName == "src") && !IsSafeUrl(value, attributeName == "src"))
                    {
                        continue;
                    }

                    if (attributeName == "style" && Regex.IsMatch(value, @"expression|javascript|url\s*\(|@import|behavior", RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    output.Append(' ').Append(attributeName).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
                }

                if (name == "a")
                {
                    output.Append(" rel=\"noopener noreferrer\"");
                }

                output.Append('>');
            }

            AppendText(output, html.Substring(position));
            return output.ToString();
        }

        // Text between tags: keep existing entities (&amp; &nbsp; ...) but neutralise stray angle brackets.
        private static void AppendText(StringBuilder output, string text)
        {
            output.Append(text.Replace("<", "&lt;").Replace(">", "&gt;"));
        }

        private static bool IsSafeUrl(string url, bool isImageSource)
        {
            var value = Regex.Replace(url, @"[\s\u0000-\u001F]+", string.Empty).ToLowerInvariant();
            if (value.StartsWith("http://") || value.StartsWith("https://") || value.StartsWith("mailto:"))
            {
                return true;
            }

            if (isImageSource && Regex.IsMatch(value, @"^data:image/(png|jpe?g|gif|bmp|webp);base64,"))
            {
                return true; // images pasted into the editor
            }

            // relative links only (no scheme such as javascript: / data: / vbscript:)
            return !value.Contains(':') && !value.StartsWith("//");
        }
    }
}
