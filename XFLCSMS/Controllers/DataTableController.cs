using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using XFLCSMS.Models.DataTable;

namespace XFLCSMS.Controllers
{
    [SessionAuthorize(SessionAuthorizeAttribute.Admin)]
    public class DataTableController : Controller
    {
        private readonly DataContext _context;

        public DataTableController( DataContext context)
        {
            _context = context;
        }
        public IActionResult Index()
        {

            var customerList = _context.Customers.ToList();
            

            return View(customerList);
            //return View();
        }
        public IActionResult Create()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Create(Customer customer)
        {
            // The two calls were not awaited (the request ended before the save ran) and
            // the Index view was returned without its model.
            await _context.AddAsync(customer);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }
        
        public ActionResult GetList()
        {

            //List<Customer> customerList = new List<Customer>();

            var customerList = _context.Customers.ToList();

            return new JsonResult(customerList);
        }
    }
}
