using XFLCSMS.Models.Support;

namespace XFLCSMS.Models.Desk
{
    /// <summary>The list of the knowledge base: the articles found, and what was asked for.</summary>
    public class KnowledgeView
    {
        public string? Query { get; set; }
        public int ProductId { get; set; }
        /// <summary>null, "drafts" or "internal".</summary>
        public string? Show { get; set; }
        public List<Product> Products { get; set; } = new();
        public List<KbArticle> Articles { get; set; } = new();
        /// <summary>The signed-in user writes articles: sees drafts, gets the buttons.</summary>
        public bool CanWrite { get; set; }
        /// <summary>How many articles this role can read at all, how many of them are drafts / internal.</summary>
        public int Total { get; set; }
        public int Drafts { get; set; }
        public int Internal { get; set; }
    }
}
