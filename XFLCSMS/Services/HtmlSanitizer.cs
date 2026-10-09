using System.Net;
using System.Text;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Allow-list sanitizer for the rich-text "Details" field of a ticket.
    ///
    /// The field is written by any signed-in user and shown inside the page of whoever opens the ticket
    /// (and loaded into the editor on the edit page), so everything that is not plain formatting has to go:
    /// scripts, event handlers, frames, forms, positioning styles, links with a script address, and images
    /// that would make the reader's browser call another address.
    ///
    /// It is a single pass over the text (no regular expressions): the time it takes grows with the length
    /// of the text, also for deliberately broken input. Tags are balanced on the way out, so a ticket cannot
    /// leave a &lt;a&gt; or &lt;font&gt; open and restyle the rest of the page.
    /// </summary>
    public static class HtmlSanitizer
    {
        private static readonly HashSet<string> AllowedTags = new(StringComparer.Ordinal)
        {
            "p", "br", "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup", "span", "div", "font",
            "ul", "ol", "li", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "pre", "code", "hr",
            "table", "thead", "tbody", "tfoot", "tr", "td", "th", "caption", "colgroup", "col", "a", "img"
        };

        // Elements without an end tag.
        private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal) { "br", "hr", "img", "col" };

        // Removed together with everything inside them.
        private static readonly HashSet<string> DroppedBlocks = new(StringComparer.Ordinal)
        {
            "script", "style", "iframe", "object", "embed", "form", "svg", "math", "noscript", "template",
            "textarea", "select", "button", "title", "head", "frameset", "applet", "audio", "video", "canvas"
        };

        private static readonly HashSet<string> AllowedAttributes = new(StringComparer.Ordinal)
        {
            "href", "src", "alt", "title", "style", "color", "face", "size", "target",
            "width", "height", "colspan", "rowspan", "align", "valign", "border", "cellpadding", "cellspacing"
        };

        // Colours, weight and alignment only. Anything that moves, sizes or layers an element is left out:
        // the text is printed inside the page, where "position: fixed" or a huge width would cover or stretch
        // the screen of whoever opens the ticket.
        private static readonly HashSet<string> AllowedStyles = new(StringComparer.Ordinal)
        {
            "color", "background-color", "font-weight", "font-style", "text-decoration", "text-align", "list-style-type"
        };

        private const int MaxNesting = 60;

        public static string Sanitize(string? html)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var output = new StringBuilder(html.Length + 64);
            var open = new List<string>();      // allowed elements that are open right now, innermost last
            var position = 0;

            // Every tag needs a '>' after it. Remembering the last one makes "<<<<<<..." cost nothing.
            var lastTagEnd = html.LastIndexOf('>');
            // Work spent on things that looked like a tag but were not; when it gets out of hand the rest is text.
            long wasted = 0;
            var budget = 8L * html.Length + 4096;

            while (position < html.Length)
            {
                var start = html.IndexOf('<', position);
                if (start < 0 || start > lastTagEnd || wasted > budget)
                {
                    AppendText(output, html, position, html.Length);
                    break;
                }

                AppendText(output, html, position, start);

                // comments: <!-- ... -->  (also <!doctype ...>, <?xml ...?>)
                if (start + 1 < html.Length && (html[start + 1] == '!' || html[start + 1] == '?'))
                {
                    var isComment = string.CompareOrdinal(html, start, "<!--", 0, 4) == 0;
                    var close = isComment ? html.IndexOf("-->", start + 4, StringComparison.Ordinal) : html.IndexOf('>', start + 2);
                    position = close < 0 ? html.Length : close + (isComment ? 3 : 1);
                    continue;
                }

                if (!TryReadTag(html, start, out var tag, out var scanned))
                {
                    wasted += scanned;
                    output.Append("&lt;");
                    position = start + 1;
                    continue;
                }

                position = tag.End;

                if (DroppedBlocks.Contains(tag.Name))
                {
                    if (!tag.IsEnd)
                    {
                        position = SkipBlock(html, tag.Name, tag.End);
                    }

                    continue;
                }

                if (!AllowedTags.Contains(tag.Name))
                {
                    continue; // unknown tag: drop the tag, keep its inner text
                }

                if (tag.IsEnd)
                {
                    // Close up to the matching start tag; an end tag without a start tag is dropped.
                    var match = open.LastIndexOf(tag.Name);
                    if (match >= 0)
                    {
                        CloseDownTo(output, open, match);
                    }

                    continue;
                }

                var isVoid = VoidTags.Contains(tag.Name);
                if (!isVoid && open.Count >= MaxNesting)
                {
                    continue;
                }

                if (tag.Name == "a" && open.Contains("a"))
                {
                    CloseDownTo(output, open, open.LastIndexOf("a")); // links do not nest
                }

                output.Append('<').Append(tag.Name);
                AppendAttributes(output, tag);
                output.Append('>');

                if (!isVoid)
                {
                    open.Add(tag.Name);
                }
            }

            CloseDownTo(output, open, 0);
            return output.ToString();
        }

        private static void CloseDownTo(StringBuilder output, List<string> open, int index)
        {
            for (var i = open.Count - 1; i >= index; i--)
            {
                output.Append("</").Append(open[i]).Append('>');
                open.RemoveAt(i);
            }
        }

        private static void AppendAttributes(StringBuilder output, Tag tag)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, rawValue) in tag.Attributes)
            {
                if (!AllowedAttributes.Contains(name) || !seen.Add(name))
                {
                    continue; // drops every on* handler, class, id and anything else not on the list
                }

                var value = WebUtility.HtmlDecode(rawValue);

                if (name == "href" && !IsSafeLink(value))
                {
                    continue;
                }

                if (name == "src" && !IsEmbeddedImage(value))
                {
                    continue;
                }

                if (name == "target")
                {
                    value = "_blank";
                }

                if (name == "style")
                {
                    value = SafeStyle(value);
                    if (value.Length == 0)
                    {
                        continue;
                    }
                }

                if ((name == "width" || name == "height") && !IsSmallNumber(value))
                {
                    continue;
                }

                output.Append(' ').Append(name).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
            }

            if (tag.Name == "a")
            {
                output.Append(" rel=\"noopener noreferrer\"");
            }
        }

        private static string SafeStyle(string style)
        {
            var kept = new List<string>();
            foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = declaration.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var property = declaration.Substring(0, colon).Trim().ToLowerInvariant();
                var setting = declaration.Substring(colon + 1).Trim();
                if (!AllowedStyles.Contains(property) || setting.Length == 0 || setting.Length > 60 || !IsPlainStyleValue(setting))
                {
                    continue;
                }

                kept.Add(property + ": " + setting);
            }

            return string.Join("; ", kept);
        }

        // Letters, digits and the few signs colours and keywords need: "red", "#1d5fc4", "rgb(1, 2, 3)", "line-through".
        // No url(...), no expression(...), no escapes, no quotes.
        private static bool IsPlainStyleValue(string value)
        {
            foreach (var c in value)
            {
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                         || c == ' ' || c == '#' || c == ',' || c == '.' || c == '-' || c == '%' || c == '(' || c == ')';
                if (!ok)
                {
                    return false;
                }
            }

            var lower = value.ToLowerInvariant();
            return !lower.Contains("url") && !lower.Contains("expression") && !lower.Contains("var(");
        }

        private static bool IsSmallNumber(string value)
        {
            var digits = value.EndsWith("%", StringComparison.Ordinal) ? value.Substring(0, value.Length - 1) : value;
            return digits.Length > 0 && digits.Length <= 4 && digits.All(char.IsDigit);
        }

        // Web and mail links only (no javascript:, data:, vbscript:, ...). Relative addresses are refused too:
        // a link inside a ticket should not point at an action of this application. The one exception are the
        // short addresses of an article or a ticket (Controllers/GoController.cs): they only ever show a page,
        // and only one the reader may open anyway.
        private static bool IsSafeLink(string url)
        {
            var value = Compact(url);
            return value.StartsWith("http://", StringComparison.Ordinal)
                || value.StartsWith("https://", StringComparison.Ordinal)
                || value.StartsWith("mailto:", StringComparison.Ordinal)
                || IsShortLink(value);
        }

        /// <summary>"/go/article/12" or "/go/ticket/345": a fixed start, then nothing but a few digits.</summary>
        public static bool IsShortLink(string value)
        {
            foreach (var start in new[] { "/go/article/", "/go/ticket/" })
            {
                if (value.StartsWith(start, StringComparison.Ordinal))
                {
                    var number = value.Substring(start.Length);
                    return number.Length > 0 && number.Length <= 9 && number.All(c => c >= '0' && c <= '9');
                }
            }

            return false;
        }

        // Images must carry their own data (a picture pasted into the old editor). An image with an address
        // would make the reader's browser request that address just by opening the ticket: a tracking pixel,
        // or a call to an address of this application.
        private static bool IsEmbeddedImage(string url)
        {
            var value = Compact(url);
            foreach (var type in new[] { "png", "jpeg", "jpg", "gif", "bmp", "webp" })
            {
                if (value.StartsWith("data:image/" + type + ";base64,", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Lower case, without the white space and control characters browsers ignore inside addresses.
        private static string Compact(string url)
        {
            var builder = new StringBuilder(Math.Min(url.Length, 64));
            foreach (var c in url)
            {
                if (builder.Length >= 64)
                {
                    break;
                }

                if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        // Text between tags: keep existing entities (&amp; &nbsp; ...) but neutralise angle brackets.
        private static void AppendText(StringBuilder output, string html, int from, int to)
        {
            for (var i = from; i < to; i++)
            {
                var c = html[i];
                if (c == '<')
                {
                    output.Append("&lt;");
                }
                else if (c == '>')
                {
                    output.Append("&gt;");
                }
                else
                {
                    output.Append(c);
                }
            }
        }

        // After <script ...> (and the like): continue behind the matching end tag, or at the end of the text.
        private static int SkipBlock(string html, string name, int from)
        {
            var search = from;
            while (true)
            {
                var close = html.IndexOf("</" + name, search, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                {
                    return html.Length;
                }

                var after = close + 2 + name.Length;
                if (after >= html.Length || !char.IsLetterOrDigit(html[after]))
                {
                    var end = html.IndexOf('>', after);
                    return end < 0 ? html.Length : end + 1;
                }

                search = after;
            }
        }

        private sealed class Tag
        {
            public string Name = string.Empty;
            public bool IsEnd;
            public int End;
            public List<(string Name, string Value)> Attributes = new();
        }

        /// <summary>
        /// Reads one tag starting at html[start] == '&lt;'. False when it is not a complete tag
        /// (no name, no closing '&gt;', an unterminated quote, or another '&lt;' before the end);
        /// "scanned" is how many characters were looked at.
        /// </summary>
        private static bool TryReadTag(string html, int start, out Tag tag, out int scanned)
        {
            tag = new Tag();
            var i = start + 1;
            var length = html.Length;

            if (i < length && html[i] == '/')
            {
                tag.IsEnd = true;
                i++;
            }

            var nameStart = i;
            while (i < length && IsNameChar(html[i], i == nameStart))
            {
                i++;
            }

            if (i == nameStart)
            {
                scanned = i - start;
                return false;
            }

            tag.Name = html.Substring(nameStart, i - nameStart).ToLowerInvariant();

            while (i < length)
            {
                var c = html[i];

                if (c == '>')
                {
                    tag.End = i + 1;
                    scanned = tag.End - start;
                    return true;
                }

                if (c == '<')
                {
                    break; // a new tag starts before this one ended: not a tag
                }

                if (char.IsWhiteSpace(c) || c == '/')
                {
                    i++;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    // a stray quoted string where an attribute name should be: skip it as a whole
                    var stray = html.IndexOf(c, i + 1);
                    if (stray < 0)
                    {
                        break;
                    }

                    i = stray + 1;
                    continue;
                }

                // attribute name
                var attributeStart = i;
                while (i < length && !char.IsWhiteSpace(html[i]) && html[i] != '=' && html[i] != '>' && html[i] != '<' && html[i] != '/' && html[i] != '"' && html[i] != '\'')
                {
                    i++;
                }

                var attribute = html.Substring(attributeStart, i - attributeStart).ToLowerInvariant();

                while (i < length && char.IsWhiteSpace(html[i]))
                {
                    i++;
                }

                var value = string.Empty;
                if (i < length && html[i] == '=')
                {
                    i++;
                    while (i < length && char.IsWhiteSpace(html[i]))
                    {
                        i++;
                    }

                    if (i < length && (html[i] == '"' || html[i] == '\''))
                    {
                        var quote = html[i];
                        var close = html.IndexOf(quote, i + 1);
                        if (close < 0)
                        {
                            break; // unterminated quote: not a tag
                        }

                        value = html.Substring(i + 1, close - i - 1);
                        i = close + 1;
                    }
                    else
                    {
                        var valueStart = i;
                        while (i < length && !char.IsWhiteSpace(html[i]) && html[i] != '>' && html[i] != '<')
                        {
                            i++;
                        }

                        value = html.Substring(valueStart, i - valueStart);
                    }
                }

                if (attribute.Length > 0 && !tag.IsEnd)
                {
                    tag.Attributes.Add((attribute, value));
                }
            }

            scanned = Math.Min(i, length) - start;
            return false;
        }

        private static bool IsNameChar(char c, bool first)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (!first && c >= '0' && c <= '9');
        }
    }
}
