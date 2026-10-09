using XFLCSMS.Models.Desk;

namespace XFLCSMS.Services
{
    /// <summary>
    /// Finds the articles of the knowledge base that fit some words: the search of the knowledge base, the
    /// suggestions under the title of a new ticket, and "related articles" on a ticket. The articles are few and
    /// short compared with tickets, so they are ranked in memory: a word in the title or in the keywords counts
    /// most, a word in the text a little.
    /// </summary>
    public static class KnowledgeSearch
    {
        public sealed class Hit
        {
            public KbArticle Article { get; init; } = null!;
            public int Score { get; init; }
        }

        // words that say nothing about the subject
        private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "for", "not", "are", "was", "has", "have", "with", "from", "this", "that", "our", "your", "you", "can", "cannot", "does", "doesn", "did",
            "how", "what", "when", "why", "who", "all", "any", "but", "its", "it's", "out", "get", "got", "one", "two", "new", "now", "again", "please", "need", "want",
            "problem", "issue", "error", "ticket", "help", "after", "before", "into", "over", "than", "then", "there", "they", "been", "will", "would", "should", "could"
        };

        /// <summary>
        /// Part of a word: a letter or digit of any script, and the marks that belong to letters (vowel signs and
        /// joiners of Bangla and other scripts are not "letters" for .NET, but a word without them is another word).
        /// </summary>
        public static bool IsWordChar(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '\u200C' || c == '\u200D')
            {
                return true;
            }

            var category = char.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.NonSpacingMark || category == System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        /// <summary>The words of a text that are worth searching for: letters and digits, three or more, lower case, each once.</summary>
        public static List<string> Words(string? text)
        {
            var words = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return words;
            }

            var current = new System.Text.StringBuilder();
            foreach (var c in text + " ")
            {
                if (IsWordChar(c))
                {
                    current.Append(char.ToLowerInvariant(c));
                    continue;
                }

                if (current.Length >= 3 || (current.Length == 2 && current.ToString().All(char.IsLetter) && text.Contains(current.ToString().ToUpperInvariant())))
                {
                    // two letters count when they were typed as capitals: "BO", "KB", "IP"
                    var word = current.ToString();
                    if (!Noise.Contains(word) && !words.Contains(word)) { words.Add(word); }
                }

                current.Clear();
                if (words.Count >= 12) { break; }
            }

            return words;
        }

        /// <summary>
        /// The articles that fit, best first. With a product: its articles and the ones for every product (an article
        /// of another product is no answer). <paramref name="strict"/>: at least one word in title or keywords, or two
        /// in the text - for suggestions nobody asked for; the search page shows every article with any word in it.
        /// </summary>
        public static List<Hit> Find(IEnumerable<KbArticle> articles, string? text, int? productId, int take, bool strict)
        {
            var words = Words(text);
            if (words.Count == 0)
            {
                return new List<Hit>();
            }

            var hits = new List<Hit>();
            foreach (var article in articles)
            {
                if (productId != null && article.ProductId != null && article.ProductId != productId)
                {
                    continue;
                }

                var title = " " + article.Title.ToLowerInvariant();
                var keywords = " " + (article.Keywords ?? string.Empty).ToLowerInvariant().Replace(',', ' ');
                var body = TicketService.PlainText(article.Body).ToLowerInvariant();
                int score = 0, strong = 0, weak = 0;
                foreach (var word in words)
                {
                    if (title.Contains(" " + word)) { score += 7; strong++; }
                    else if (title.Contains(word)) { score += 5; strong++; }
                    else if (keywords.Contains(" " + word)) { score += 6; strong++; }
                    else if (keywords.Contains(word)) { score += 4; strong++; }
                    else if (body.Contains(word)) { score += 1; weak++; }
                }

                if (score == 0 || (strict && strong == 0 && weak < 2))
                {
                    continue;
                }

                if (productId != null && article.ProductId == productId) { score += 2; }
                hits.Add(new Hit { Article = article, Score = score });
            }

            return hits.OrderByDescending(hit => hit.Score).ThenByDescending(hit => hit.Article.Helpful - hit.Article.NotHelpful).ThenBy(hit => hit.Article.Title).Take(take).ToList();
        }

        /// <summary>The start of an article as plain text, for lists.</summary>
        public static string Snippet(KbArticle article, int length = 170)
        {
            // without the headings ("Problem", "Solution"): they say nothing in a line of text
            var text = TicketService.PlainText(System.Text.RegularExpressions.Regex.Replace(article.Body ?? string.Empty, "<h[1-6][^>]*>.*?</h[1-6]>", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromMilliseconds(200)));
            return text.Length <= length ? text : text.Substring(0, length - 1).TrimEnd() + "…";
        }
    }
}
