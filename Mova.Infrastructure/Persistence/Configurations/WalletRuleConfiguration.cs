using Microsoft.EntityFrameworkCore; 
using Microsoft.EntityFrameworkCore.Metadata.Builders; 
using Mova.Domain.Entities; 

namespace Mova.Infrastructure.Persistence.Configurations; 

public class WalletRuleConfiguration : IEntityTypeConfiguration<WalletRule> 
{ 
    public void Configure(EntityTypeBuilder<WalletRule> builder) 
    { 
        builder.ToTable("wallet_rules");

        builder.HasKey(x => x.Id); 
        
        builder.Property(x => x.WalletId) 
            .IsRequired(); 
        builder.Property(x => x.Frequency) 
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(x => x.StartDate)
            .IsRequired(); 
        builder.Property(x => x.EndDate)
            .IsRequired();

        builder.ComplexProperty( 
            x => x.Amount, 
            money => { 

                money.Property(x => x.MinorUnits)
                    .HasColumnName("amount_minor_units")
                    .IsRequired(); 

                money.Property(x => x.Currency) 
                    .HasColumnName("amount_currency") 
                    .HasMaxLength(3) 
                    .IsRequired(); }); 

        builder.HasIndex(x => x.WalletId) 
            .IsUnique(); 
    } 
}