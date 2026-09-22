using Mova.Domain.Entities;
using Mova.Domain.Enums;
using Mova.Domain.ValueObjects;

namespace Mova.Infrastructure.Persistence.Seeding;

public static class DefaultWalletTemplates
{
    public static List<WalletTemplate> Create() => new()
    {
        // ─────────────────────────────────────────────────────────
        // 1. Lucky's daily transport money.
        //    ₦30k budget, ₦1k every weekday morning.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Transport Allowance",
            Description = "Daily transport money, released every weekday morning.",
            CategoryId = 2, // Transport
            DefaultTargetAmount = Money.FromNaira(30_000),
            DefaultReleaseAmount = Money.FromNaira(1_000),
            DefaultFrequency = ReleaseFrequency.Daily,
            DefaultFrequencyConfig = "{\"type\":\"daily\",\"daysOfWeek\":[1,2,3,4,5],\"time\":\"07:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Bus",
            Tags = new[] { "transport", "daily", "commute", "weekday", "bus" },
            SortOrder = 1,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 2. Ada's monthly rent savings.
        //    ₦120k goal, ₦30k released on the 1st of each month.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Rent Savings",
            Description = "Save for rent — released at the start of each month.",
            CategoryId = 3, // Rent & Housing
            DefaultTargetAmount = Money.FromNaira(120_000),
            DefaultReleaseAmount = Money.FromNaira(30_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Home",
            Tags = new[] { "rent", "housing", "monthly", "savings", "home" },
            SortOrder = 2,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 3. Chidi's weekly grocery budget.
        //    ₦20k for weekly shopping, released every Saturday.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Weekly Groceries",
            Description = "Weekly food and grocery money, released every Saturday morning.",
            CategoryId = 12, // Groceries
            DefaultTargetAmount = Money.FromNaira(20_000),
            DefaultReleaseAmount = Money.FromNaira(5_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[6],\"time\":\"08:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "ShoppingCart",
            Tags = new[] { "groceries", "food", "weekly", "shopping", "market" },
            SortOrder = 3,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 4. Ada's term-based school fees.
        //    ₦150k per term, released 3 times a year to the school's bank.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "School Fees",
            Description = "Term-based school fees, released at the start of each term.",
            CategoryId = 7, // Education
            DefaultTargetAmount = Money.FromNaira(150_000),
            DefaultReleaseAmount = Money.FromNaira(50_000),
            DefaultFrequency = ReleaseFrequency.Quarterly,
            DefaultFrequencyConfig = "{\"type\":\"quarterly\",\"months\":[1,4,9],\"daysOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "GraduationCap",
            Tags = new[] { "school", "education", "fees", "term", "children" },
            SortOrder = 4,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 5. Tunde's emergency fund.
        //    ₦100k safety net, ₦20k set aside each month.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Emergency Fund",
            Description = "Build a safety net with small monthly deposits.",
            CategoryId = 17, // Savings
            DefaultTargetAmount = Money.FromNaira(100_000),
            DefaultReleaseAmount = Money.FromNaira(20_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Shield",
            Tags = new[] { "emergency", "savings", "safety", "fund", "backup" },
            SortOrder = 5,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 6. Ngozi's daily personal allowance.
        //    ₦30k for the month, ₦1k per day to cover small spends.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Daily Allowance",
            Description = "A small amount every day to keep daily spending in check.",
            CategoryId = 10, // Personal Care
            DefaultTargetAmount = Money.FromNaira(30_000),
            DefaultReleaseAmount = Money.FromNaira(1_000),
            DefaultFrequency = ReleaseFrequency.Daily,
            DefaultFrequencyConfig = "{\"type\":\"daily\",\"daysOfWeek\":[1,2,3,4,5,6,7],\"time\":\"08:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Wallet",
            Tags = new[] { "daily", "allowance", "personal", "spending", "pocket" },
            SortOrder = 6,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 7. Chidi's monthly data and airtime.
        //    ₦15k for the month, ₦5k released every 10 days.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Data & Airtime",
            Description = "Monthly data and airtime budget — released in three chunks.",
            CategoryId = 13, // Mobile & Internet
            DefaultTargetAmount = Money.FromNaira(15_000),
            DefaultReleaseAmount = Money.FromNaira(5_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1,10,20],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Main,
            IconName = "Smartphone",
            Tags = new[] { "data", "airtime", "phone", "monthly", "internet" },
            SortOrder = 7,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 8. Ada's weekly baby essentials.
        //    ₦40k baby budget, ₦10k every Monday for diapers, formula.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Baby Essentials",
            Description = "Weekly money for baby supplies — diapers, formula, and more.",
            CategoryId = 9, // Family
            DefaultTargetAmount = Money.FromNaira(40_000),
            DefaultReleaseAmount = Money.FromNaira(10_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[1],\"time\":\"08:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Baby",
            Tags = new[] { "baby", "family", "weekly", "essentials", "children" },
            SortOrder = 8,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 9. Tunde's daily fuel budget.
        //    ₦45k fuel wallet, ₦1.5k every morning for petrol.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Fuel Budget",
            Description = "Daily fuel money, released every morning.",
            CategoryId = 2, // Transport
            DefaultTargetAmount = Money.FromNaira(45_000),
            DefaultReleaseAmount = Money.FromNaira(1_500),
            DefaultFrequency = ReleaseFrequency.Daily,
            DefaultFrequencyConfig = "{\"type\":\"daily\",\"daysOfWeek\":[1,2,3,4,5,6,7],\"time\":\"07:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Fuel",
            Tags = new[] { "fuel", "transport", "daily", "petrol", "car" },
            SortOrder = 9,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 10. Ngozi's holiday fund.
        //     ₦200k trip fund, ₦50k saved each month for 4 months.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Holiday Fund",
            Description = "Set aside money for that trip or vacation.",
            CategoryId = 11, // Travel
            DefaultTargetAmount = Money.FromNaira(200_000),
            DefaultReleaseAmount = Money.FromNaira(50_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Plane",
            Tags = new[] { "holiday", "travel", "vacation", "savings", "trip" },
            SortOrder = 10,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 11. Ada's monthly utility bills.
        //     ₦60k for electricity and water, ₦20k released each month.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Utility Bills",
            Description = "Electricity and water bills — released at the start of each month.",
            CategoryId = 4, // Bills & Utilities
            DefaultTargetAmount = Money.FromNaira(60_000),
            DefaultReleaseAmount = Money.FromNaira(20_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Zap",
            Tags = new[] { "utilities", "bills", "electricity", "monthly", "water" },
            SortOrder = 11,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 12. Chidi's side hustle restocking.
        //     ₦100k business capital, ₦25k every Monday for restock.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Side Hustle Capital",
            Description = "Weekly capital for small business restocking.",
            CategoryId = 20, // Business
            DefaultTargetAmount = Money.FromNaira(100_000),
            DefaultReleaseAmount = Money.FromNaira(25_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[1],\"time\":\"08:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Briefcase",
            Tags = new[] { "business", "capital", "weekly", "hustle", "restock" },
            SortOrder = 12,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 13. Lucky's weekend entertainment.
        //     ₦20k for fun, ₦5k released every Friday evening.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Entertainment",
            Description = "Weekly entertainment budget — movies, dining out, and fun.",
            CategoryId = 8, // Entertainment
            DefaultTargetAmount = Money.FromNaira(20_000),
            DefaultReleaseAmount = Money.FromNaira(5_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[5],\"time\":\"18:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Popcorn",
            Tags = new[] { "entertainment", "fun", "weekly", "leisure", "movies" },
            SortOrder = 13,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 14. Ngozi's health & fitness.
        //     ₦30k for gym and supplements, ₦10k set aside monthly.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Health & Fitness",
            Description = "Monthly gym, supplements, and health expenses.",
            CategoryId = 6, // Health
            DefaultTargetAmount = Money.FromNaira(30_000),
            DefaultReleaseAmount = Money.FromNaira(10_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Heart",
            Tags = new[] { "health", "fitness", "gym", "medical", "wellness" },
            SortOrder = 14,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 15. Ada's wedding savings.
        //     ₦500k for the big day, ₦50k set aside every month.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Wedding Savings",
            Description = "Set aside money for that special day.",
            CategoryId = 9, // Family
            DefaultTargetAmount = Money.FromNaira(500_000),
            DefaultReleaseAmount = Money.FromNaira(50_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "HeartHandshake",
            Tags = new[] { "wedding", "events", "savings", "celebration", "marriage" },
            SortOrder = 15,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 16. Lucky's rent paid directly to the landlord.
        //     ₦90k, ₦30k released monthly to the landlord's bank.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Landlord Rent",
            Description = "Monthly rent paid directly to your landlord's bank account.",
            CategoryId = 3, // Rent & Housing
            DefaultTargetAmount = Money.FromNaira(90_000),
            DefaultReleaseAmount = Money.FromNaira(30_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Building2",
            Tags = new[] { "rent", "landlord", "monthly", "bank", "housing" },
            SortOrder = 16,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 17. Chidi's daily pocket money.
        //     ₦15k, ₦500 per day for small everyday spends.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Pocket Money",
            Description = "Small daily pocket money for everyday expenses.",
            CategoryId = 10, // Personal Care
            DefaultTargetAmount = Money.FromNaira(15_000),
            DefaultReleaseAmount = Money.FromNaira(500),
            DefaultFrequency = ReleaseFrequency.Daily,
            DefaultFrequencyConfig = "{\"type\":\"daily\",\"daysOfWeek\":[1,2,3,4,5,6,7],\"time\":\"07:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Coins",
            Tags = new[] { "pocket", "daily", "personal", "small", "spending" },
            SortOrder = 17,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 18. Ada's annual subscriptions.
        //     ₦120k once a year — domains, software, insurance.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Annual Subscriptions",
            Description = "Yearly subscriptions — domains, software, insurance.",
            CategoryId = 14, // Subscriptions
            DefaultTargetAmount = Money.FromNaira(120_000),
            DefaultReleaseAmount = Money.FromNaira(120_000),
            DefaultFrequency = ReleaseFrequency.Yearly,
            DefaultFrequencyConfig = "{\"type\":\"yearly\",\"months\":[1],\"daysOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "CalendarCheck",
            Tags = new[] { "subscriptions", "annual", "yearly", "bills", "software" },
            SortOrder = 18,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 19. Tunde's savings challenge.
        //     52-week challenge — ₦1k saved every week.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Savings Challenge",
            Description = "52-week savings challenge — small weekly deposits.",
            CategoryId = 17, // Savings
            DefaultTargetAmount = Money.FromNaira(52_000),
            DefaultReleaseAmount = Money.FromNaira(1_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Trophy",
            Tags = new[] { "challenge", "savings", "weekly", "goal", "habit" },
            SortOrder = 19,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 20. Ngozi's family support.
        //     ₦60k for parents, ₦20k sent monthly to their bank.
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Family Support",
            Description = "Monthly support sent directly to a family member's bank account.",
            CategoryId = 9, // Family
            DefaultTargetAmount = Money.FromNaira(60_000),
            DefaultReleaseAmount = Money.FromNaira(20_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Users",
            Tags = new[] { "family", "support", "monthly", "bank", "parents" },
            SortOrder = 20,
            IsActive = true,
        },
    };
}