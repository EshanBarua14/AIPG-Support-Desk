namespace XFLCSMS.Models.Common
{
    public class Pager
    {
        public int TotalRecord { get; set; }
        public int TotalPages { get; set; }

        public int CurrentPage { get; set; }
        public int PageSize { get; set; }
        public int StartPage { get; set; }
        public int LastPage { get; set; }
        //New property added on 

        public int StartButton { get; set; }
        public int StopButton { get; set; }
        public int Previous { get; set; }
        public int Next { get; set; }

        public int SerialStart { get; set; }

        public string? SearchString { get; set; }

        public Pager()
        {

        }
        public Pager(int totalrecord, int currentpage, int pagesize = 10, int buttons = 3, string? searchString = null)
        {
            if (pagesize <= 0) { pagesize = 10; }
            TotalPages = Math.Max(1, (int)Math.Ceiling(totalrecord / (decimal)pagesize));
            if (currentpage <= 0) { currentpage = 1; }
            if (currentpage > TotalPages) { currentpage = TotalPages; }
            CurrentPage = currentpage;
            SearchString = searchString;

            StartPage = 1;
            LastPage = TotalPages;

            //if(StartPage<=0)
            //{
            //    StartPage = 1;
            //    EndPage = pagesize;
            //}
            //if(EndPage>TotalPages) { EndPage = TotalPages; }

            TotalRecord = totalrecord;
            PageSize = pagesize;

            StartButton = currentpage - buttons;
            StopButton = currentpage + buttons;
            if (StartButton <= 0) StartButton = 1;
            if (StopButton >= LastPage) StopButton = LastPage;
            Previous = CurrentPage - 1;
            Next = CurrentPage + 1;

            if (Previous <= 0) Previous = 1;
            if (Next > LastPage) Next = LastPage;
            SerialStart = (currentpage - 1) * pagesize + 1;

        }
    }
}
