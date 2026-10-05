using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SocialMediaAutomation.Domain.Entities;

namespace SocialMediaAutomation.Infrastructure.Persistence.Configurations;

internal static class ColumnRules
{
    /// <summary>Stores an enum by name and adds a CHECK constraint so the database rejects values the code does not define.</summary>
    public static void IsEnumString<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum>> property, string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        builder.Property(property).HasColumnName(column).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.AddEnumCheck<TEntity, TEnum>(column);
    }

    /// <inheritdoc cref="IsEnumString{TEntity,TEnum}(EntityTypeBuilder{TEntity},Expression{Func{TEntity,TEnum}},string)"/>
    public static void IsEnumString<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, TEnum?>> property, string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        builder.Property(property).HasColumnName(column).HasConversion<string>().HasMaxLength(32);
        builder.AddEnumCheck<TEntity, TEnum>(column);
    }

    /// <summary>Maps a string list to a JSON array column guarded by ISJSON (AGENTS.md §63); the domain validates the items.</summary>
    public static void IsJsonStringList<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, List<string>>> property, string column)
        where TEntity : class
    {
        builder.PrimitiveCollection(property).HasColumnName(column).IsRequired();
        builder.AddJsonCheck(column);
    }

    public static void AddJsonCheck<TEntity>(this EntityTypeBuilder<TEntity> builder, string column) where TEntity : class =>
        builder.ToTable(t => t.HasCheckConstraint($"CK_{builder.Metadata.GetTableName()}_{column}_Json", $"ISJSON([{column}]) = 1"));

    /// <summary>
    /// The acting user must be a member of the row's own organization: a composite FK to OrganizationMembers (OrganizationId, UserId)
    /// makes the database reject a user id borrowed from another workspace.
    /// </summary>
    public static void HasMemberReference<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, object?>> organizationAndUser)
        where TEntity : class =>
        builder.HasOne<OrganizationMember>().WithMany()
            .HasForeignKey(organizationAndUser)
            .HasPrincipalKey(m => new { m.OrganizationId, m.UserId })
            .OnDelete(DeleteBehavior.Restrict);

    private static void AddEnumCheck<TEntity, TEnum>(this EntityTypeBuilder<TEntity> builder, string column)
        where TEntity : class
        where TEnum : struct, Enum
    {
        var allowed = string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"));
        builder.ToTable(t => t.HasCheckConstraint($"CK_{builder.Metadata.GetTableName()}_{column}", $"[{column}] IN ({allowed})"));
    }
}
