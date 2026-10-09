using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace XFLCSMS.Models.Support
{
    /// <summary>
    /// A product of AIPG that brokerage houses get support for (order management system, mobile app, ...).
    /// Support types, categories, sub-categories and affected sections can belong to one product; the ticket form
    /// then offers them only for that product. Maintained under Administration > Products.
    /// </summary>
    public class Product
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProductId { get; set; }

        [Required(ErrorMessage = "The name is required.")]
        [StringLength(100, MinimumLength = SupportListRules.MinimumLength, ErrorMessage = SupportListRules.LengthMessage)]
        [RegularExpression(SupportListRules.NamePattern, ErrorMessage = SupportListRules.NameMessage)]
        [Display(Name = "Product")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Short name shown where space is tight (lists, reports), for example "OMS".</summary>
        [StringLength(20, ErrorMessage = "The short name has at most 20 characters.")]
        [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9 ._\-]*$", ErrorMessage = "Use letters, digits, spaces and . _ - only.")]
        [Display(Name = "Short name")]
        public string? Code { get; set; }

        [StringLength(500, ErrorMessage = "The description has at most 500 characters.")]
        public string? Description { get; set; }

        /// <summary>
        /// An inactive product is no longer offered on the ticket form; its tickets and lists stay.
        /// (No default: a form that does not send the box - it is not ticked - means inactive.)
        /// </summary>
        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        /// <summary>
        /// The support engineer new tickets for this product go to (user id); empty: nobody in particular, the rule
        /// for new tickets under System > Automation decides. No foreign key on purpose, as with IssueTable.AssignedToId.
        /// </summary>
        [Display(Name = "Default engineer")]
        public int? EngineerId { get; set; }

        /// <summary>What to show where space is tight.</summary>
        [NotMapped]
        public string ShortName => string.IsNullOrWhiteSpace(Code) ? Name : Code!;
    }

    /// <summary>The rule for the names of products and of the support lists (one place, so the pages agree).</summary>
    public static class SupportListRules
    {
        public const int MinimumLength = 2;
        public const string LengthMessage = "The name must be 2 to 100 characters long.";

        /// <summary>Letters (with their marks) and digits of any language, spaces and a few signs; starts with a letter or digit.</summary>
        public const string NamePattern = @"^[\p{L}\p{N}][\p{L}\p{M}\p{N} .,&/()+'\-]*$";
        public const string NameMessage = "Use letters, digits, spaces and . , & / ( ) + ' - only, starting with a letter or digit.";
        public const string Hint = "2 to 100 characters: letters, digits, spaces and . , & / ( ) + ' -";
    }
}
