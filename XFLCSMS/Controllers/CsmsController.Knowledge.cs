using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Desk;
using XFLCSMS.Models.Issue;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // The knowledge base (version 3.4), the same for every role:
    //   reading   everybody who is signed in: published articles; AIPG roles also the internal ones
    //   writing   permission "write the knowledge base": drafts, publishing, removing
    //   suggest   the ticket form asks for articles that fit the title being typed
    public abstract partial class CsmsController
    {
        /// <summary>May this role write articles?</summary>
        private bool WritesKnowledge => Rbac.IsStaff(MyRole) && Can(Permission.Knowledge);

        /// <summary>
        /// The articles this role may read. People of a brokerage house: published and not internal. AIPG roles: also
        /// the internal ones. Drafts only for the people who write.
        /// </summary>
        private IQueryable<KbArticle> ReadableArticles
        {
            get
            {
                if (WritesKnowledge) { return Db.KbArticles; }
                return Rbac.IsStaff(MyRole)
                    ? Db.KbArticles.Where(article => article.IsPublished)
                    : Db.KbArticles.Where(article => article.IsPublished && !article.IsInternal);
            }
        }

        /// <summary>The list of articles, searched by <paramref name="q"/> and narrowed to one product.</summary>
        /// <param name="show">"drafts" (writers): what is not published yet. "internal" (AIPG roles): the internal articles.</param>
        [HttpGet]
        public async Task<IActionResult> Knowledge(string? q = null, int product = 0, string? show = null)
        {
            try
            {
                var all = await ReadableArticles.ToListAsync();
                var products = await Db.Products.OrderBy(item => item.Name).ToListAsync();
                var view = new KnowledgeView
                {
                    Query = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
                    ProductId = products.Any(item => item.ProductId == product) ? product : 0,
                    Show = show == "drafts" && WritesKnowledge ? "drafts" : show == "internal" && Rbac.IsStaff(MyRole) ? "internal" : null,
                    Products = products,
                    CanWrite = WritesKnowledge,
                    Total = all.Count,
                    Drafts = all.Count(article => !article.IsPublished),
                    Internal = all.Count(article => article.IsInternal)
                };

                IEnumerable<KbArticle> rows = all;
                if (view.Show == "drafts") { rows = rows.Where(article => !article.IsPublished); }
                if (view.Show == "internal") { rows = rows.Where(article => article.IsInternal); }
                if (view.ProductId > 0) { rows = rows.Where(article => article.ProductId == null || article.ProductId == view.ProductId); }

                if (view.Query != null)
                {
                    var hits = KnowledgeSearch.Find(rows, view.Query, null, 200, strict: false);
                    // words the search drops ("how", "the") or a part of a word: fall back to plain "contains"
                    view.Articles = hits.Count > 0
                        ? hits.Select(hit => hit.Article).ToList()
                        : rows.Where(article => article.Title.Contains(view.Query, StringComparison.OrdinalIgnoreCase)
                                || (article.Keywords ?? string.Empty).Contains(view.Query, StringComparison.OrdinalIgnoreCase)).OrderBy(article => article.Title).ToList();
                }
                else
                {
                    view.Articles = rows.OrderBy(article => article.Title).ToList();
                }

                return View(view);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>One article. Reading it counts once per sign-in.</summary>
        [HttpGet]
        public async Task<IActionResult> Article(int id)
        {
            try
            {
                var article = await ReadableArticles.FirstOrDefaultAsync(item => item.Id == id);
                if (article == null)
                {
                    return NotFound();
                }

                var seen = SessionList(SeenArticles);
                if (article.IsPublished && !seen.Contains(id))
                {
                    article.Views++;
                    await Db.SaveChangesAsync();
                    seen.Add(id);
                    HttpContext.Session.SetString(SeenArticles, string.Join(",", seen.TakeLast(200)));
                }

                ViewBag.ProductName = article.ProductId == null ? null
                    : await Db.Products.Where(item => item.ProductId == article.ProductId).Select(item => item.Name).FirstOrDefaultAsync();
                ViewBag.CanWrite = WritesKnowledge;
                ViewBag.Voted = SessionList(VotedArticles).Contains(id);
                if (Rbac.IsStaff(MyRole) && article.SourceIssueId != null)
                {
                    var source = await Db.Issues.FirstOrDefaultAsync(issue => issue.IssueId == article.SourceIssueId);
                    ViewBag.SourceTicket = source != null && CanAccessIssue(source) ? source.TNumber : null;
                }

                return View(article);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private const string SeenArticles = "kb.seen";
        private const string VotedArticles = "kb.voted";

        private List<int> SessionList(string key)
        {
            return (HttpContext.Session.GetString(key) ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => int.TryParse(part, out var value) ? value : 0).Where(value => value > 0).ToList();
        }

        /// <summary>"Did this answer your question?" - once per sign-in and article.</summary>
        [HttpPost]
        public async Task<IActionResult> ArticleFeedback(int id, bool helpful)
        {
            try
            {
                var article = await ReadableArticles.FirstOrDefaultAsync(item => item.Id == id && item.IsPublished);
                if (article == null)
                {
                    return NotFound();
                }

                var voted = SessionList(VotedArticles);
                if (!voted.Contains(id))
                {
                    if (helpful) { article.Helpful++; } else { article.NotHelpful++; }
                    await Db.SaveChangesAsync();
                    voted.Add(id);
                    HttpContext.Session.SetString(VotedArticles, string.Join(",", voted.TakeLast(200)));
                }

                TempData["SuccessMessage"] = helpful
                    ? "Thank you. Good to hear it helped."
                    : "Thank you. " + (Can(Permission.TicketCreate) ? "If your question is still open, raise a ticket: the support team answers it." : "The authors see that this article did not help.");
                return RedirectToAction("Article", new { id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>
        /// Articles that fit what is being typed as the title of a new ticket (JSON). Only what this role may read;
        /// nothing for fewer than two useful words - a suggestion nobody asked for has to be a good one.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> SuggestArticles(string? q, int? productId = null)
        {
            try
            {
                Response.Headers["Cache-Control"] = "no-store";
                if (string.IsNullOrWhiteSpace(q) || q.Length > 300 || KnowledgeSearch.Words(q).Count == 0)
                {
                    return Json(Array.Empty<object>());
                }

                var articles = await ReadableArticles.Where(article => article.IsPublished).ToListAsync();
                var hits = KnowledgeSearch.Find(articles, q, productId > 0 ? productId : null, 4, strict: true);
                return Json(hits.Select(hit => new
                {
                    id = hit.Article.Id,
                    title = hit.Article.Title,
                    text = KnowledgeSearch.Snippet(hit.Article, 140),
                    url = Url.Action("Article", new { id = hit.Article.Id }),
                    isInternal = hit.Article.IsInternal
                }));
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Related articles for the ticket page of AIPG staff: found by the title of the ticket, within its product.</summary>
        private async Task<List<KbArticle>> RelatedArticlesAsync(IssueTable issue)
        {
            var articles = await ReadableArticles.Where(article => article.IsPublished).ToListAsync();
            return KnowledgeSearch.Find(articles, issue.ITitle, issue.ProductId, 4, strict: true).Select(hit => hit.Article).ToList();
        }

        // ---- writing ----------------------------------------------------------------------------------------

        /// <param name="id">The article to change; 0 for a new one.</param>
        /// <param name="fromTicket">A new article starts with the title and details of this ticket.</param>
        [HttpGet]
        [RequirePermission(Permission.Knowledge)]
        public async Task<IActionResult> EditArticle(int id = 0, int fromTicket = 0)
        {
            try
            {
                KbArticle? article;
                if (id > 0)
                {
                    article = await Db.KbArticles.FirstOrDefaultAsync(item => item.Id == id);
                    if (article == null)
                    {
                        return NotFound();
                    }
                }
                else
                {
                    article = new KbArticle { IsPublished = false };
                    var issue = fromTicket > 0 ? await Db.Issues.FirstOrDefaultAsync(item => item.IssueId == fromTicket) : null;
                    if (issue != null && CanAccessIssue(issue))
                    {
                        // a start, not a copy to publish: what the customer wrote is theirs, and names have to go
                        article.Title = issue.ITitle ?? string.Empty;
                        article.ProductId = issue.ProductId;
                        article.SourceIssueId = issue.IssueId;
                        article.Body = "<h3>Problem</h3>" + (string.IsNullOrWhiteSpace(issue.Details) ? "<p></p>" : HtmlSanitizer.Sanitize(issue.Details))
                            + "<h3>Cause</h3><p></p><h3>Solution</h3><p></p>";
                        ViewBag.FromTicket = issue.TNumber;
                    }
                }

                ViewBag.Products = await Db.Products.OrderBy(item => item.Name).ToListAsync();
                return View(article);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.Knowledge)]
        public async Task<IActionResult> SaveArticle(int id, string? title, string? body, string? keywords, int? productId, int? sourceIssueId, bool isInternal = false, bool isPublished = false)
        {
            try
            {
                title = System.Text.RegularExpressions.Regex.Replace((title ?? string.Empty).Trim(), "\\s+", " ");
                var clean = HtmlSanitizer.Sanitize(body ?? string.Empty);
                keywords = string.Join(", ", (keywords ?? string.Empty).Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
                var product = productId > 0 ? await Db.Products.FirstOrDefaultAsync(item => item.ProductId == productId) : null;

                var article = id > 0 ? await Db.KbArticles.FirstOrDefaultAsync(item => item.Id == id) : new KbArticle { CreatedAt = DateTime.Now };
                if (article == null)
                {
                    return NotFound();
                }

                string? problem = null;
                if (title.Length < 5 || title.Length > 200) { problem = "Give the article a title of 5 to 200 characters: the question, as somebody would ask it."; }
                else if (TicketService.PlainText(clean).Length < 20) { problem = "Write the article: at least a sentence or two."; }
                else if (clean.Length > 400_000) { problem = "The article is too long. Split it into several."; }
                else if (keywords.Length > 300) { problem = "Too many keywords: at most 300 characters."; }
                else if (await Db.KbArticles.AnyAsync(item => item.Title == title && item.Id != id)) { problem = "There is already an article with this title."; }

                var isNew = id <= 0;
                var before = isNew ? null : (article.IsPublished ? "published" : "draft") + (article.IsInternal ? ", internal" : string.Empty);
                article.Title = title;
                article.Body = clean;
                article.Keywords = keywords.Length == 0 ? null : keywords;
                article.ProductId = product?.ProductId;
                article.IsInternal = isInternal;
                article.IsPublished = isPublished;
                if (isNew && sourceIssueId > 0)
                {
                    // only a ticket the writer may open (the article page shows its number to AIPG staff)
                    var source = await Db.Issues.FirstOrDefaultAsync(issue => issue.IssueId == sourceIssueId);
                    article.SourceIssueId = source != null && CanAccessIssue(source) ? source.IssueId : null;
                }

                if (problem != null)
                {
                    // back to the form with what was typed
                    if (!isNew) { Db.Entry(article).State = EntityState.Detached; }
                    ViewBag.Problem = problem;
                    ViewBag.Products = await Db.Products.OrderBy(item => item.Name).ToListAsync();
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return View("EditArticle", article);
                }

                article.UpdatedAt = DateTime.Now;
                article.UpdatedBy = CurrentUser!.FullName;
                if (isNew) { Db.KbArticles.Add(article); }
                var now = (article.IsPublished ? "published" : "draft") + (article.IsInternal ? ", internal" : string.Empty);
                Audit(isNew ? AuditActions.DataCreate : AuditActions.DataUpdate, "Article", isNew ? null : article.Id, title,
                    isNew ? "Wrote the article (" + now + ")" : "Changed the article" + (before == now ? string.Empty : " (" + before + " → " + now + ")"), null);
                await Db.SaveChangesAsync();

                TempData["SuccessMessage"] = article.IsPublished
                    ? "Saved. The article is published" + (article.IsInternal ? " for AIPG staff." : ": the brokerage houses find it in the knowledge base and while they type a ticket.")
                    : "Saved as a draft. Only the people who write the knowledge base see it until it is published.";
                return RedirectToAction("Article", new { id = article.Id });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [HttpPost]
        [RequirePermission(Permission.Knowledge)]
        public async Task<IActionResult> DeleteArticle(int id)
        {
            try
            {
                var article = await Db.KbArticles.FirstOrDefaultAsync(item => item.Id == id);
                if (article != null)
                {
                    Audit(AuditActions.DataDelete, "Article", article.Id, article.Title, "Removed the article", null);
                    Db.KbArticles.Remove(article);
                    await Db.SaveChangesAsync();
                    TempData["SuccessMessage"] = "“" + article.Title + "” was removed. Links to it in old replies no longer open.";
                }

                return RedirectToAction("Knowledge");
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }
    }
}
