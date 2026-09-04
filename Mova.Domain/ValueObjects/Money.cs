namespace Mova.Domain.ValueObjects;

public readonly record struct Money
{
    private const int MinorUnitsPerUnit = 100;

    public long MinorUnits { get; }

    public string Currency { get; }

    private Money(long minorUnits, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException(
                "Currency is required.",
                nameof(currency));

        Currency = currency.Trim().ToUpperInvariant();

        if (Currency.Length != 3)
            throw new ArgumentException(
                "Currency must be a valid 3-letter currency code.",
                nameof(currency));

        MinorUnits = minorUnits;
    }
    public static Money FromNaira(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Amount cannot be negative.");

        var minorUnitsDecimal = amount * MinorUnitsPerUnit;

        if (minorUnitsDecimal != decimal.Truncate(minorUnitsDecimal))
            throw new ArgumentException(
                "Amount cannot have more than 2 decimal places.",
                nameof(amount));

        var minorUnits = checked((long)minorUnitsDecimal);

        return new Money(minorUnits, "NGN");
    }

    public decimal ToDecimal()
    {
        return MinorUnits / (decimal)MinorUnitsPerUnit;
    }

    public static Money FromMinorUnits(
        long minorUnits,
        string currency = "NGN")
    {
        if (minorUnits < 0)
            throw new ArgumentOutOfRangeException(
                nameof(minorUnits),
                "Minor units cannot be negative.");

        return new Money(minorUnits, currency);
    }

    public static Money operator +(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(
                "Cannot add money with different currencies.");

        return FromMinorUnits(
            checked(left.MinorUnits + right.MinorUnits),
            left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        if (left.Currency != right.Currency)
            throw new InvalidOperationException(
                "Cannot subtract money with different currencies.");

        if (left.MinorUnits < right.MinorUnits)
            throw new InvalidOperationException(
                "Money cannot become negative.");

        return FromMinorUnits(
            left.MinorUnits - right.MinorUnits,
            left.Currency);
    }
}