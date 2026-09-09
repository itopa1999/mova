using Mova.Domain.Entities;

namespace Mova.Infrastructure.Persistence.Seeding;

public static class DefaultWalletCategories
{
    public static List<WalletCategory> Create()
    {
        return
        [
            new WalletCategory
            {
                Id = 1,
                Name = "Food",
                Icon = "Utensils"
            },
            new WalletCategory
            {
                Id = 2,
                Name = "Transport",
                Icon = "Car"
            },
            new WalletCategory
            {
                Id = 3,
                Name = "Rent & Housing",
                Icon = "Wallet"
            },
            new WalletCategory
            {
                Id = 4,
                Name = "Bills & Utilities",
                Icon = "Zap"
            },
            new WalletCategory
            {
                Id = 5,
                Name = "Shopping",
                Icon = "ShoppingBag"
            },
            new WalletCategory
            {
                Id = 6,
                Name = "Health",
                Icon = "PlusCircle"
            },
            new WalletCategory
            {
                Id = 7,
                Name = "Education",
                Icon = "FileText"
            },
            new WalletCategory
            {
                Id = 8,
                Name = "Entertainment",
                Icon = "Gamepad2"
            },
            new WalletCategory
            {
                Id = 9,
                Name = "Family",
                Icon = "User"
            },
            new WalletCategory
            {
                Id = 10,
                Name = "Personal Care",
                Icon = "Sparkles"
            },
            new WalletCategory
            {
                Id = 11,
                Name = "Travel",
                Icon = "Calendar"
            },
            new WalletCategory
            {
                Id = 12,
                Name = "Groceries",
                Icon = "ShoppingBag"
            },
            new WalletCategory
            {
                Id = 13,
                Name = "Mobile & Internet",
                Icon = "Smartphone"
            },
            new WalletCategory
            {
                Id = 14,
                Name = "Subscriptions",
                Icon = "Clock"
            },
            new WalletCategory
            {
                Id = 15,
                Name = "Insurance",
                Icon = "Shield"
            },
            new WalletCategory
            {
                Id = 16,
                Name = "Debt Repayment",
                Icon = "Calculator"
            },
            new WalletCategory
            {
                Id = 17,
                Name = "Savings",
                Icon = "PiggyBank"
            },
            new WalletCategory
            {
                Id = 18,
                Name = "Investment",
                Icon = "TrendingUp"
            },
            new WalletCategory
            {
                Id = 19,
                Name = "Salary",
                Icon = "TrendingUp"
            },
            new WalletCategory
            {
                Id = 20,
                Name = "Business",
                Icon = "BarChart3"
            },
            new WalletCategory
            {
                Id = 21,
                Name = "Gifts & Donations",
                Icon = "PlusCircle"
            },
            new WalletCategory
            {
                Id = 22,
                Name = "Fees & Charges",
                Icon = "AlertTriangle"
            },
            new WalletCategory
            {
                Id = 23,
                Name = "Cash Withdrawal",
                Icon = "Wallet"
            },
            new WalletCategory
            {
                Id = 24,
                Name = "Other",
                Icon = "FileText"
            }
        ];
    }
}