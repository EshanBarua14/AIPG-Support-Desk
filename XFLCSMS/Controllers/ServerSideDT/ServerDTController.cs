using Microsoft.AspNetCore.Mvc;
using XFLCSMS.Infrastructure;

namespace XFLCSMS.Controllers.ServerSideDT
{
    [SessionAuthorize(SessionAuthorizeAttribute.Admin)]
    public class ServerDTController : Controller
    {
        private readonly DataContext _context;

        public ServerDTController( DataContext context)
        {
            _context = context;
        }
        public async Task<IActionResult> Index()
        {
            var data = _context.Customers.ToList();
            return new JsonResult(data);
        }

        public async Task<IActionResult> GetData()
        {
            var data = _context.Customers.ToList();
            return new JsonResult(data);
        }
    }
}
