namespace XFLCSMS.Data
{
    /// <summary>
    /// The products a new installation starts with, each with its own support types and support categories.
    /// Added once, at the first start with this version, to a database that has no product yet (see
    /// DbInitializer.SeedProducts). After that the lists live in the database only: change, add or delete them
    /// under Administration > Products - editing this file has no effect on a database that was already filled.
    /// To start without them, set "Products": { "Seed": false } in appsettings.json before the first start.
    /// </summary>
    public static class ProductSeed
    {
        public sealed class Entry
        {
            public string Name { get; init; } = string.Empty;
            public string Code { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            /// <summary>What kind of help is needed (first choice on the ticket form).</summary>
            public string[] Types { get; init; } = Array.Empty<string>();
            /// <summary>The area of the product the ticket is about.</summary>
            public string[] Categories { get; init; } = Array.Empty<string>();
        }

        public static readonly Entry[] Products =
        {
            new Entry
            {
                Name = "Trading OMS", Code = "OMS",
                Description = "Order management system: dealer terminals, order routing to the exchanges, pre-trade risk checks.",
                Types = new[] { "Bug or error", "Service outage", "Configuration change", "Data correction", "New feature request", "How-to question" },
                Categories = new[] { "Order entry", "Order routing and execution", "Market data feed", "Risk and limits", "Dealer terminal", "User and permission setup", "Exchange connectivity (DSE/CSE)", "End-of-day processing", "Reports" }
            },
            new Entry
            {
                Name = "Mobile Trading App", Code = "APP",
                Description = "The trading app the clients of a brokerage house use on their phone.",
                Types = new[] { "App crash or error", "Sign-in problem", "Account update", "New feature request", "How-to question" },
                Categories = new[] { "Sign-in and OTP", "Order placement", "Portfolio and holdings", "Market watch", "Fund deposit and withdrawal", "Notifications", "App performance" }
            },
            new Entry
            {
                Name = "Back Office System", Code = "BO",
                Description = "Client accounts, settlement, ledger, charges and statements of a brokerage house.",
                Types = new[] { "Bug or error", "Data correction", "Report request", "Configuration change", "How-to question" },
                Categories = new[] { "Client accounts", "Trade settlement", "Ledger and accounting", "Charges and commission", "CDBL integration", "Margin loan", "Statements and reports", "End-of-day processing" }
            },
            new Entry
            {
                Name = "Risk Management System", Code = "RMS",
                Description = "Limits, margin and exposure monitoring for clients and dealers.",
                Types = new[] { "Bug or error", "Limit change request", "Configuration change", "How-to question" },
                Categories = new[] { "Client limits", "Dealer limits", "Margin and exposure", "Alerts and notifications", "Restricted instruments", "Reports" }
            },
            new Entry
            {
                Name = "Market Data & Analysis", Code = "MDA",
                Description = "Real-time DSE and CSE prices with technical and fundamental analysis.",
                Types = new[] { "Data issue", "Bug or error", "Subscription request", "How-to question" },
                Categories = new[] { "Real-time price feed", "Charts", "Technical indicators", "Fundamental data", "Screener and watchlist", "Historical data" }
            }
        };
    }
}
