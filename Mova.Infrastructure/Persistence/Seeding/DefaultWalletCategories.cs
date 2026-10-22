
using Mova.Domain.Entities;
using Mova.Domain.Enums;

namespace Mova.Infrastructure.Persistence.Seeding;

public static class DefaultWalletCategories
{
    public static List<WalletCategory> Create()
    {
        return
        [
            new()
            {
                Id = 1,
                Name = "Food & Dining",
                Icon = "Utensils",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 2,
                Name = "Groceries",
                Icon = "ShoppingBasket",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 3,
                Name = "Transportation",
                Icon = "Car",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 4,
                Name = "Rent & Housing",
                Icon = "House",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 5,
                Name = "Bills & Utilities",
                Icon = "Zap",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 6,
                Name = "Shopping",
                Icon = "ShoppingBag",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 7,
                Name = "Health & Medical",
                Icon = "HeartPulse",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 8,
                Name = "Education",
                Icon = "GraduationCap",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 9,
                Name = "Entertainment",
                Icon = "Clapperboard",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 10,
                Name = "Family & Children",
                Icon = "Users",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 11,
                Name = "Personal Care",
                Icon = "Sparkles",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 12,
                Name = "Travel & Holidays",
                Icon = "Plane",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 13,
                Name = "Mobile & Internet",
                Icon = "Smartphone",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 14,
                Name = "Subscriptions",
                Icon = "CalendarClock",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 15,
                Name = "Insurance",
                Icon = "ShieldCheck",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 16,
                Name = "Debt Repayment",
                Icon = "HandCoins",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 17,
                Name = "Personal Savings",
                Icon = "PiggyBank",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 18,
                Name = "Emergency Fund",
                Icon = "ShieldPlus",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 19,
                Name = "Gifts & Donations",
                Icon = "Gift",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 20,
                Name = "Fees & Charges",
                Icon = "Receipt",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 21,
                Name = "Cash & ATM",
                Icon = "Banknote",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 22,
                Name = "Work & Career",
                Icon = "BriefcaseBusiness",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 23,
                Name = "Pets",
                Icon = "PawPrint",
                WalletType = WalletType.Scheduled
            },
            new()
            {
                Id = 24,
                Name = "Other Expenses",
                Icon = "Ellipsis",
                WalletType = WalletType.Scheduled
            },

            // Trustee — wallets managed under trusted oversight
            new()
            {
                Id = 25,
                Name = "Family Support",
                Icon = "Users",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 26,
                Name = "Children's Upkeep",
                Icon = "Baby",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 27,
                Name = "Education Support",
                Icon = "GraduationCap",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 28,
                Name = "Medical Support",
                Icon = "HeartPulse",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 29,
                Name = "Rent & Housing Support",
                Icon = "House",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 30,
                Name = "Business Support",
                Icon = "BriefcaseBusiness",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 31,
                Name = "Emergency Assistance",
                Icon = "LifeBuoy",
                WalletType = WalletType.Trustee
            },
            new()
            {
                Id = 32,
                Name = "General Support",
                Icon = "HandHeart",
                WalletType = WalletType.Trustee
            },

            // Circle — AJo and group savings
            new()
            {
                Id = 33,
                Name = "AJo Contributions",
                Icon = "Users",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 34,
                Name = "Rotating Savings",
                Icon = "RefreshCw",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 35,
                Name = "Group Emergency Fund",
                Icon = "ShieldPlus",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 36,
                Name = "Group Investment",
                Icon = "TrendingUp",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 37,
                Name = "Project Contributions",
                Icon = "FolderKanban",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 38,
                Name = "Wedding & Events",
                Icon = "PartyPopper",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 39,
                Name = "Community Development",
                Icon = "Building2",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 40,
                Name = "Group Purchases",
                Icon = "ShoppingCart",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 41,
                Name = "Group Travel",
                Icon = "Plane",
                WalletType = WalletType.Circle
            },
            new()
            {
                Id = 42,
                Name = "Other Group Savings",
                Icon = "CircleDollarSign",
                WalletType = WalletType.Circle
            }
        ];
    }
}
