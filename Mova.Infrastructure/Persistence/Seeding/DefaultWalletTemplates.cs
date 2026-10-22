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
        //    Category: 3 — Transportation (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Transport Allowance",
            Description = "Daily transport money, released every weekday morning.",
            CategoryId = 3, // Transportation
            WalletType = WalletType.Scheduled,
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
        // 2. Ada's monthly rent release plan.
        //    Category: 4 — Rent & Housing (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Rent Release Plan",
            Description = "Keep rent money aside and release it at the start of each month.",
            CategoryId = 4, // Rent & Housing
            WalletType = WalletType.Scheduled,
            DefaultTargetAmount = Money.FromNaira(120_000),
            DefaultReleaseAmount = Money.FromNaira(30_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Home",
            Tags = new[] { "rent", "housing", "monthly", "release", "home" },
            SortOrder = 2,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 3. Chidi's weekly grocery budget.
        //    Category: 2 — Groceries (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Weekly Groceries",
            Description = "Weekly food and grocery money, released every Saturday morning.",
            CategoryId = 2, // Groceries
            WalletType = WalletType.Scheduled,
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
        //    Category: 8 — Education (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "School Fees",
            Description = "Term-based school fees, released at the start of each term.",
            CategoryId = 8, // Education
            WalletType = WalletType.Scheduled,
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
        //    Category: 18 — Emergency Fund (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Emergency Fund",
            Description = "Keep emergency money aside and release it on your monthly rule.",
            CategoryId = 18, // Emergency Fund
            WalletType = WalletType.Scheduled,
            DefaultTargetAmount = Money.FromNaira(100_000),
            DefaultReleaseAmount = Money.FromNaira(20_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Shield",
            Tags = new[] { "emergency", "release", "safety", "fund", "backup" },
            SortOrder = 5,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 6. Ngozi's daily personal allowance.
        //    Category: 11 — Personal Care (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Daily Allowance",
            Description = "A small amount every day to keep daily spending in check.",
            CategoryId = 11, // Personal Care
            WalletType = WalletType.Scheduled,
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
        //    Category: 13 — Mobile & Internet (Scheduled)  ✅ was already correct
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Data & Airtime",
            Description = "Monthly data and airtime budget — released in three chunks.",
            CategoryId = 13, // Mobile & Internet
            WalletType = WalletType.Scheduled,
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
        //    Category: 10 — Family & Children (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Baby Essentials",
            Description = "Weekly money for baby supplies — diapers, formula, and more.",
            CategoryId = 10, // Family & Children
            WalletType = WalletType.Scheduled,
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
        //    Category: 3 — Transportation (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Fuel Budget",
            Description = "Daily fuel money, released every morning.",
            CategoryId = 3, // Transportation
            WalletType = WalletType.Scheduled,
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
        //     Category: 12 — Travel & Holidays (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Holiday Fund",
            Description = "Keep travel money separate and release it on your monthly schedule.",
            CategoryId = 12, // Travel & Holidays
            WalletType = WalletType.Scheduled,
            DefaultTargetAmount = Money.FromNaira(200_000),
            DefaultReleaseAmount = Money.FromNaira(50_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Plane",
            Tags = new[] { "holiday", "travel", "vacation", "release", "trip" },
            SortOrder = 10,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 11. Ada's monthly utility bills.
        //     Category: 5 — Bills & Utilities (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Utility Bills",
            Description = "Electricity and water bills — released at the start of each month.",
            CategoryId = 5, // Bills & Utilities
            WalletType = WalletType.Scheduled,
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
        //     Category: 22 — Work & Career (Scheduled)
        //     (no "Business" scheduled category; Work & Career fits best)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Side Hustle Capital",
            Description = "Weekly capital for small business restocking.",
            CategoryId = 22, // Work & Career
            WalletType = WalletType.Scheduled,
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
        //     Category: 9 — Entertainment (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Entertainment",
            Description = "Weekly entertainment budget — movies, dining out, and fun.",
            CategoryId = 9, // Entertainment
            WalletType = WalletType.Scheduled,
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
        //     Category: 7 — Health & Medical (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Health & Fitness",
            Description = "Monthly gym, supplements, and health expenses.",
            CategoryId = 7, // Health & Medical
            WalletType = WalletType.Scheduled,
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
        // 15. Ada's wedding release plan.
        //     Category: 10 — Family & Children (Scheduled)
        //     (no "Wedding" scheduled category; Family & Children is closest)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Wedding Release Plan",
            Description = "Keep event money aside and release it when your schedule says so.",
            CategoryId = 10, // Family & Children
            WalletType = WalletType.Scheduled,
            DefaultTargetAmount = Money.FromNaira(500_000),
            DefaultReleaseAmount = Money.FromNaira(50_000),
            DefaultFrequency = ReleaseFrequency.Monthly,
            DefaultFrequencyConfig = "{\"type\":\"monthly\",\"datesOfMonth\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "HeartHandshake",
            Tags = new[] { "wedding", "events", "release", "celebration", "marriage" },
            SortOrder = 15,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 16. Lucky's rent paid directly to the landlord.
        //     Category: 4 — Rent & Housing (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Landlord Rent",
            Description = "Monthly rent paid directly to your landlord's bank account.",
            CategoryId = 4, // Rent & Housing
            WalletType = WalletType.Scheduled,
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
        //     Category: 11 — Personal Care (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Pocket Money",
            Description = "Small daily pocket money for everyday expenses.",
            CategoryId = 11, // Personal Care
            WalletType = WalletType.Scheduled,
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
        //     Category: 14 — Subscriptions (Scheduled)  ✅ was already correct
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Annual Subscriptions",
            Description = "Yearly subscriptions — domains, software, insurance.",
            CategoryId = 14, // Subscriptions
            WalletType = WalletType.Scheduled,
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
        // 19. Tunde's weekly release plan.
        //     Category: 17 — Personal Savings (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Weekly Release Plan",
            Description = "Keep a set amount aside and make it available in weekly releases.",
            CategoryId = 17, // Personal Savings
            WalletType = WalletType.Scheduled,
            DefaultTargetAmount = Money.FromNaira(52_000),
            DefaultReleaseAmount = Money.FromNaira(1_000),
            DefaultFrequency = ReleaseFrequency.Weekly,
            DefaultFrequencyConfig = "{\"type\":\"weekly\",\"daysOfWeek\":[1],\"time\":\"09:00\"}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Trophy",
            Tags = new[] { "release", "weekly", "access", "control", "habit" },
            SortOrder = 19,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 20. Ngozi's family support (Scheduled side).
        //     Category: 10 — Family & Children (Scheduled)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Family Support",
            Description = "Monthly support sent directly to a family member's bank account.",
            CategoryId = 10, // Family & Children
            WalletType = WalletType.Scheduled,
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

        // ─────────────────────────────────────────────────────────
        // 21. Family emergency fund with joint control.
        //     Category: 25 — Family Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Family Emergency Fund",
            Description = "Shared family money that needs another family member's approval to release.",
            CategoryId = 25, // Family Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(200_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "ShieldCheck",
            Tags = new[] { "family", "emergency", "trustee", "shared", "approval" },
            SortOrder = 21,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 22. Joint wedding planning fund.
        //     Category: 32 — General Support (Trustee)
        //     (no wedding category in Trustee range)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Wedding Fund",
            Description = "Money for the big day, with a trusted family member co-signing every release.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(500_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "HeartHandshake",
            Tags = new[] { "wedding", "trustee", "event", "shared", "approval" },
            SortOrder = 22,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 23. Business partnership capital.
        //     Category: 30 — Business Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Business Partnership Fund",
            Description = "Capital held by the partnership — releases need sign-off from a partner.",
            CategoryId = 30, // Business Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(1_000_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Briefcase",
            Tags = new[] { "business", "partnership", "trustee", "capital", "approval" },
            SortOrder = 23,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 24. Elder care fund.
        //     Category: 25 — Family Support (Trustee)
        //     (Medical Support = 28 is also a valid alternative)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Elder Care Fund",
            Description = "Money set aside for parents' care, with a sibling approving every release.",
            CategoryId = 25, // Family Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(300_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Heart",
            Tags = new[] { "parents", "elder", "care", "trustee", "family" },
            SortOrder = 24,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 25. Children's school fees (co-signed).
        //     Category: 27 — Education Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "School Fees (Co-signed)",
            Description = "Fees that need another guardian's approval before money leaves the wallet.",
            CategoryId = 27, // Education Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(300_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "GraduationCap",
            Tags = new[] { "school", "fees", "education", "trustee", "children" },
            SortOrder = 25,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 26. Couples' joint savings.
        //     Category: 32 — General Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Joint Savings",
            Description = "A shared goal — both of you decide when money leaves this wallet.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(400_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Users",
            Tags = new[] { "couple", "savings", "trustee", "shared", "joint" },
            SortOrder = 26,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 27. Group project fund.
        //     Category: 32 — General Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Group Project Fund",
            Description = "Shared money for a project — releases need a teammate's sign-off.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(250_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Users",
            Tags = new[] { "team", "project", "trustee", "shared", "approval" },
            SortOrder = 27,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 28. Debt repayment reserve.
        //     Category: 32 — General Support (Trustee)
        //     (no debt category in Trustee range)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Debt Repayment Reserve",
            Description = "Money ring-fenced for debt — a trustee approves before any payout.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(150_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "Landmark",
            Tags = new[] { "debt", "repayment", "trustee", "reserve", "approval" },
            SortOrder = 28,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 29. Charity / Zakat fund.
        //     Category: 32 — General Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Charity Fund",
            Description = "Money set aside for giving — a trusted person approves each disbursement.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(100_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Bank,
            IconName = "HandCoins",
            Tags = new[] { "charity", "zakat", "giving", "trustee", "shared" },
            SortOrder = 29,
            IsActive = true,
        },

        // ─────────────────────────────────────────────────────────
        // 30. Friends' group trip fund.
        //     Category: 32 — General Support (Trustee)
        // ─────────────────────────────────────────────────────────
        new WalletTemplate
        {
            Name = "Group Trip Fund",
            Description = "Friends' group trip money — one trusted member approves every release.",
            CategoryId = 32, // General Support (Trustee)
            WalletType = WalletType.Trustee,
            DefaultTargetAmount = Money.FromNaira(400_000),
            DefaultReleaseAmount = Money.FromNaira(0),
            DefaultFrequency = ReleaseFrequency.Once,
            DefaultFrequencyConfig = "{}",
            DefaultPayoutDestination = PayoutDestination.Wallet,
            IconName = "Plane",
            Tags = new[] { "travel", "group", "trip", "trustee", "shared" },
            SortOrder = 30,
            IsActive = true,
        },
    };
}