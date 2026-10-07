using System.Net.Http.Headers;
using System.Text;

namespace XFLCSMS.Services.Notify
{
    /// <summary>
    /// Sends an SMS through the HTTP interface of an SMS gateway. Nearly every gateway works the same way: one
    /// request to an address, with the number, the text, a sender name and a key. Address and request body are
    /// templates entered on the settings page; {to} {message} {sender} {key} are replaced.
    /// </summary>
    public class SmsSender
    {
        public const string UrlKey = "sms.url";
        public const string MethodKey = "sms.method";         // GET or POST
        public const string BodyKey = "sms.body";             // POST body template
        public const string ContentTypeKey = "sms.contenttype"; // form or json
        public const string HeaderKey = "sms.header";         // optional "Name: value" (may contain {key})
        public const string ApiKeyKey = "sms.apikey";         // secret
        public const string SenderKey = "sms.sender";
        public const string PrefixKey = "sms.prefix";         // country code for numbers that start with 0
        public const string SuccessKey = "sms.success";       // optional text the answer must contain

        // how the two "the gateway itself is the problem" answers start (the sender then leaves SMS alone for a minute)
        public const string Unreachable = "The gateway could not be reached";
        public const string NoAnswer = "The gateway did not answer in time.";

        private readonly SettingsStore _settings;
        private readonly IHttpClientFactory _clients;

        public SmsSender(SettingsStore settings, IHttpClientFactory clients)
        {
            _settings = settings;
            _clients = clients;
        }

        public bool IsConfigured => Uri.TryCreate(_settings.Get(UrlKey, string.Empty).Replace("{", string.Empty).Replace("}", string.Empty), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// "01712-345678" becomes "+8801712345678" with the country code +880: spaces and dashes go, a leading 0
        /// is replaced by the country code. A number that already starts with + or 00 is kept.
        /// Returns null for something that is no phone number.
        /// </summary>
        public static string? NormalizeNumber(string? number, string? countryCode)
        {
            var digits = new string((number ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
            if (digits.StartsWith("00", StringComparison.Ordinal)) { digits = "+" + digits.Substring(2); }
            var code = new string((countryCode ?? string.Empty).Where(char.IsDigit).ToArray());

            if (!digits.StartsWith("+", StringComparison.Ordinal) && code.Length > 0)
            {
                digits = "+" + code + (digits.StartsWith("0", StringComparison.Ordinal) ? digits.Substring(1) : digits);
            }

            var count = digits.Count(char.IsDigit);
            return count >= 7 && count <= 15 && digits.LastIndexOf('+') <= 0 ? digits : null;
        }

        /// <summary>Null when the gateway accepted the message, otherwise what went wrong (never contains the key).</summary>
        public async Task<string?> SendAsync(string number, string message, CancellationToken cancel = default)
        {
            if (!IsConfigured)
            {
                return "No SMS gateway is set up (Notification settings > SMS).";
            }

            var to = NormalizeNumber(number, _settings.Get(PrefixKey));
            if (to == null)
            {
                return "“" + number + "” is not a phone number.";
            }

            var key = _settings.GetSecret(ApiKeyKey) ?? string.Empty;
            if (key.Length == 0 && _settings.SecretIsUnreadable(ApiKeyKey))
            {
                return "The stored gateway key can no longer be read. Enter it again on the settings page.";
            }

            var sender = _settings.Get(SenderKey, string.Empty);
            var json = _settings.Get(ContentTypeKey, "form") == "json";
            // One pass over the template: what is put in is never looked at again. (Replacing one placeholder after
            // the other would expand a "{key}" that somebody typed into a ticket title.)
            string Fill(string template, Func<string, string> encode)
            {
                return System.Text.RegularExpressions.Regex.Replace(template, @"\{(to|message|sender|key)\}", match =>
                {
                    switch (match.Groups[1].Value)
                    {
                        case "to": return encode(to);
                        case "message": return encode(message);
                        case "sender": return encode(sender);
                        default: return encode(key);
                    }
                });
            }

            try
            {
                var url = Fill(_settings.Get(UrlKey, string.Empty), Uri.EscapeDataString);
                var post = _settings.Get(MethodKey, "POST").Equals("POST", StringComparison.OrdinalIgnoreCase);
                using var request = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, url);
                if (post)
                {
                    var body = Fill(_settings.Get(BodyKey, string.Empty), json ? JsonEscape : Uri.EscapeDataString);
                    request.Content = new StringContent(body, Encoding.UTF8, json ? "application/json" : "application/x-www-form-urlencoded");
                }

                var header = _settings.Get(HeaderKey);
                if (header != null && header.Contains(':'))
                {
                    var name = header.Substring(0, header.IndexOf(':')).Trim();
                    var value = Fill(header.Substring(header.IndexOf(':') + 1).Trim(), text => text.Replace('\r', ' ').Replace('\n', ' '));
                    request.Headers.TryAddWithoutValidation(name, value);
                }

                var client = _clients.CreateClient("sms");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
                var answer = await ReadStartAsync(response, cancel);
                var shown = Shorten(Mask(answer, key));

                if (!response.IsSuccessStatusCode)
                {
                    return "The gateway answered " + (int)response.StatusCode + " " + response.ReasonPhrase + (shown.Length > 0 ? ": " + shown : string.Empty);
                }

                var mustContain = _settings.Get(SuccessKey);
                if (mustContain != null && answer.IndexOf(mustContain, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return "The gateway did not confirm the message: " + shown;
                }

                return null;
            }
            catch (TaskCanceledException)
            {
                return NoAnswer;
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is UriFormatException || exception is InvalidOperationException)
            {
                return Unreachable + ": " + Shorten(Mask(exception.Message, key));
            }
        }

        /// <summary>The first 4,000 characters of the answer: enough to judge it, whatever the address really sends.</summary>
        private static async Task<string> ReadStartAsync(HttpResponseMessage response, CancellationToken cancel)
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancel);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[4000];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancel);
            return new string(buffer, 0, read);
        }

        /// <summary>The key never appears in a message for people, also not in the form a gateway may echo it.</summary>
        private static string Mask(string text, string key)
        {
            if (key.Length == 0)
            {
                return text;
            }

            return text.Replace(key, "***").Replace(Uri.EscapeDataString(key), "***").Replace(JsonEscape(key), "***");
        }

        private static string JsonEscape(string text)
        {
            var encoded = System.Text.Json.JsonSerializer.Serialize(text);
            return encoded.Substring(1, encoded.Length - 2); // without the quotes: the template has them
        }

        private static string Shorten(string text)
        {
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length > 300 ? text.Substring(0, 300) + "…" : text;
        }
    }
}
