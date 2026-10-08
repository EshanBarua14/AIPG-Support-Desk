using XFLCSMS.Models.Affected;
using XFLCSMS.Models.Support;

namespace XFLCSMS.Models.Admin
{
    /// <summary>One product on the list of products: the product and how much belongs to it.</summary>
    public class ProductRow
    {
        public Product Product { get; set; } = new();
        public int Types { get; set; }
        public int Categories { get; set; }
        public int Tickets { get; set; }
    }

    /// <summary>The page of one product: the product and its four support lists.</summary>
    public class ProductDetails
    {
        public Product Product { get; set; } = new();
        public List<SupportType> Types { get; set; } = new();
        public List<SupportCatagory> Categories { get; set; } = new();
        public List<SupportSubCatagory> SubCategories { get; set; } = new();
        public List<AffectedSection> Sections { get; set; } = new();
        public int Tickets { get; set; }
        /// <summary>Entries offered for every product (they have no product of their own), per list.</summary>
        public int SharedTypes { get; set; }
        public int SharedCategories { get; set; }
    }
}
