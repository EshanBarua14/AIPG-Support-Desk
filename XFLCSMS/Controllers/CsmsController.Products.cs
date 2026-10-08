using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using XFLCSMS.Models.Admin;
using XFLCSMS.Models.Issue;
using XFLCSMS.Models.Support;
using XFLCSMS.Services;

namespace XFLCSMS.Controllers
{
    // The products of XFL (order management system, mobile app, ...). A support type, category, sub-category or
    // affected section can belong to one product: the ticket form then offers it for that product only. An entry
    // without a product is offered for every product.
    // For every role that holds Permission.MasterData (by default the platform admin). The pages are in Views/Shared.
    public abstract partial class CsmsController
    {
        // ---- what the support list pages share ------------------------------------------------------

        /// <summary>The products for the "Product" box of a form, and where the form returns to.</summary>
        private async Task LoadProductChoicesAsync(string? back = null)
        {
            ViewBag.Products = await Db.Products.OrderBy(product => product.Name).ToListAsync();
            ViewBag.Back = back == "product" ? "product" : null;
        }

        /// <summary>The id when such a product exists, else null.</summary>
        private async Task<int?> ExistingProductAsync(int? productId)
        {
            return productId != null && await Db.Products.AnyAsync(product => product.ProductId == productId) ? productId : null;
        }

        /// <summary>A product chosen on a form has to exist (it can have been deleted in the meantime).</summary>
        private async Task CheckProductAsync(int? productId)
        {
            if (productId != null && await ExistingProductAsync(productId) == null)
            {
                ModelState.AddModelError("ProductId", "This product no longer exists. Choose another one.");
            }
        }

        /// <summary>A name without spaces around it and without double spaces.</summary>
        private static string CleanName(string? name)
        {
            return string.Join(" ", (name ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>
        /// Checks the rules of a form again after what was typed has been tidied (a space in front of a name is not
        /// a reason to refuse it). Messages for the other fields - a product id that is not a number - stay.
        /// </summary>
        private void Revalidate(object model, params string[] tidied)
        {
            foreach (var key in tidied)
            {
                ModelState.Remove(key);
            }

            TryValidateModel(model);
        }

        private string TakenMessage(string what, int? productId)
        {
            var product = productId == null ? null : Db.Products.Where(item => item.ProductId == productId).Select(item => item.Name).FirstOrDefault();
            return product == null
                ? "There is already a " + what + " with this name for all products."
                : "There is already a " + what + " with this name for " + product + ".";
        }

        /// <summary>After saving an entry: back to the page of its product when the form was opened from there.</summary>
        private IActionResult BackToSupportList(string listAction, int? productId, string? back)
        {
            return back == "product" && productId != null
                ? RedirectToAction("ViewProduct", new { id = productId })
                : RedirectToAction(listAction);
        }

        // ---- products ---------------------------------------------------------------------------------

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ProductList()
        {
            try
            {
                var products = await Db.Products.OrderBy(product => product.Name).ToListAsync();
                var types = await CountPerProduct(Db.SupportTypes.Where(item => item.ProductId != null).Select(item => item.ProductId!.Value));
                var categories = await CountPerProduct(Db.SupportCatagories.Where(item => item.ProductId != null).Select(item => item.ProductId!.Value));
                var tickets = await CountPerProduct(Db.Issues.Where(issue => issue.ProductId != null).Select(issue => issue.ProductId!.Value));

                return View(products.Select(product => new ProductRow
                {
                    Product = product,
                    Types = types.GetValueOrDefault(product.ProductId),
                    Categories = categories.GetValueOrDefault(product.ProductId),
                    Tickets = tickets.GetValueOrDefault(product.ProductId)
                }).ToList());
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        private static async Task<Dictionary<int, int>> CountPerProduct(IQueryable<int> productIds)
        {
            return (await productIds.GroupBy(id => id).Select(group => new { Id = group.Key, Count = group.Count() }).ToListAsync())
                .ToDictionary(row => row.Id, row => row.Count);
        }

        /// <summary>One product with its support types, categories, sub-categories and affected sections.</summary>
        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> ViewProduct(int id)
        {
            try
            {
                var product = await Db.Products.FirstOrDefaultAsync(item => item.ProductId == id);
                if (product == null)
                {
                    return NotFound();
                }

                return View(new ProductDetails
                {
                    Product = product,
                    Types = await Db.SupportTypes.Where(item => item.ProductId == id).OrderBy(item => item.SType).ToListAsync(),
                    Categories = await Db.SupportCatagories.Where(item => item.ProductId == id).OrderBy(item => item.SCatagory).ToListAsync(),
                    SubCategories = await Db.SupportSubCatagories.Where(item => item.ProductId == id).OrderBy(item => item.SubCatagory).ToListAsync(),
                    Sections = await Db.AffectedSectionss.Where(item => item.ProductId == id).OrderBy(item => item.ASection).ToListAsync(),
                    Tickets = await Db.Issues.CountAsync(issue => issue.ProductId == id),
                    SharedTypes = await Db.SupportTypes.CountAsync(item => item.ProductId == null),
                    SharedCategories = await Db.SupportCatagories.CountAsync(item => item.ProductId == null)
                });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public IActionResult CreateProduct()
        {
            return View(new Product { IsActive = true });
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProduct([Bind("Name,Code,Description,IsActive")] Product product)
        {
            try
            {
                product.ProductId = 0; // identity column: the database assigns the id
                await CleanAndCheckProductAsync(product);
                if (!ModelState.IsValid)
                {
                    return View(product);
                }

                Db.Products.Add(product);
                await Db.SaveChangesAsync();
                TempData["SuccessMessage"] = "Created " + product.Name + ". Add its support types and categories below.";
                return RedirectToAction("ViewProduct", new { id = product.ProductId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        public async Task<IActionResult> EditProduct(int id)
        {
            try
            {
                var product = await Db.Products.FirstOrDefaultAsync(item => item.ProductId == id);
                return product == null ? NotFound() : View(product);
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        [RequirePermission(Permission.MasterData)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateProduct([Bind("ProductId,Name,Code,Description,IsActive")] Product product)
        {
            try
            {
                var existing = await Db.Products.FirstOrDefaultAsync(item => item.ProductId == product.ProductId);
                if (existing == null)
                {
                    return NotFound();
                }

                await CleanAndCheckProductAsync(product);
                if (!ModelState.IsValid)
                {
                    return View("EditProduct", product);
                }

                existing.Name = product.Name;
                existing.Code = product.Code;
                existing.Description = product.Description;
                existing.IsActive = product.IsActive;
                await Db.SaveChangesAsync();
                return RedirectToAction("ViewProduct", new { id = existing.ProductId });
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        /// <summary>Tidies what was typed and adds the messages for what cannot be saved.</summary>
        private async Task CleanAndCheckProductAsync(Product product)
        {
            product.Name = CleanName(product.Name);
            product.Code = CleanName(product.Code);
            product.Code = product.Code.Length == 0 ? null : product.Code;
            product.Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description.Trim();
            Revalidate(product, nameof(Product.Name), nameof(Product.Code), nameof(Product.Description));

            if (ModelState.IsValid && await Db.Products.AnyAsync(item => item.Name == product.Name && item.ProductId != product.ProductId))
            {
                ModelState.AddModelError(nameof(Product.Name), "There is already a product with this name.");
            }

            if (ModelState.IsValid && product.Code != null && await Db.Products.AnyAsync(item => item.Code == product.Code && item.ProductId != product.ProductId))
            {
                ModelState.AddModelError(nameof(Product.Code), "Another product already has this short name.");
            }
        }

        /// <summary>
        /// Deletes a product together with its support lists. Refused while tickets were raised for the product or
        /// use one of its entries: such a product is made inactive instead (Edit), so the tickets keep what they say.
        /// </summary>
        [RequirePermission(Permission.MasterData)]
        [HttpDelete]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            try
            {
                var product = await Db.Products.FindAsync(id);
                if (product == null)
                {
                    return NotFound("The product was not found.");
                }

                var types = await Db.SupportTypes.Where(item => item.ProductId == id).ToListAsync();
                var categories = await Db.SupportCatagories.Where(item => item.ProductId == id).ToListAsync();
                var subCategories = await Db.SupportSubCatagories.Where(item => item.ProductId == id).ToListAsync();
                var sections = await Db.AffectedSectionss.Where(item => item.ProductId == id).ToListAsync();
                var typeIds = types.Select(item => (int?)item.SupportTypeId).ToList();
                var categoryIds = categories.Select(item => (int?)item.SupportCatagoryId).ToList();
                var subCategoryIds = subCategories.Select(item => (int?)item.SupportSubCatagoryId).ToList();
                var sectionIds = sections.Select(item => (int?)item.AffectedSectionId).ToList();

                if (await Db.Issues.AnyAsync(issue => issue.ProductId == id
                        || typeIds.Contains(issue.SupportTypeId) || categoryIds.Contains(issue.SupportCatagoryId)
                        || subCategoryIds.Contains(issue.SupportSubCatagoryId) || sectionIds.Contains(issue.AffectedSectionId)))
                {
                    return Conflict("Tickets were raised for this product or use one of its entries, so it cannot be deleted. Make it inactive instead: it is then no longer offered on the ticket form.");
                }

                Db.SupportTypes.RemoveRange(types);
                Db.SupportCatagories.RemoveRange(categories);
                Db.SupportSubCatagories.RemoveRange(subCategories);
                Db.AffectedSectionss.RemoveRange(sections);
                Db.Products.Remove(product);
                await Db.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return HandleError(ex);
            }
        }

        // ---- the ticket form --------------------------------------------------------------------------

        /// <summary>
        /// Fills what the "Create ticket" form offers: the active products and the four support lists. An entry of
        /// an inactive product is left out. The page shows the entries of the chosen product (wwwroot/js/app.js).
        /// </summary>
        protected void FillTicketChoices(IssueViewModel form)
        {
            var products = Db.Products.Where(product => product.IsActive).OrderBy(product => product.Name).ToList();
            var active = products.Select(product => (int?)product.ProductId).ToList();
            form.Products = products;
            form.SupportTypes = Db.SupportTypes.Where(item => item.ProductId == null || active.Contains(item.ProductId)).OrderBy(item => item.SType).ToList();
            form.SupportCatagories = Db.SupportCatagories.Where(item => item.ProductId == null || active.Contains(item.ProductId)).OrderBy(item => item.SCatagory).ToList();
            form.SupportSubCatagories = Db.SupportSubCatagories.Where(item => item.ProductId == null || active.Contains(item.ProductId)).OrderBy(item => item.SubCatagory).ToList();
            form.AffectedSections = Db.AffectedSectionss.Where(item => item.ProductId == null || active.Contains(item.ProductId)).OrderBy(item => item.ASection).ToList();
        }
    }
}
