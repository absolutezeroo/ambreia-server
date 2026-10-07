using System;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Vortex.Database.Attributes;
using Vortex.Database.Entities;

namespace Vortex.Database.Extensions;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies a global <c>DeletedAt == null</c> query filter to every mapped <see cref="VortexEntity"/>,
    /// so soft-deleted rows are excluded from every LINQ query by default instead of relying on each
    /// call site remembering to add <c>.Where(x =&gt; x.DeletedAt == null)</c>. Call sites that must
    /// still see soft-deleted rows (e.g. to revive one) opt out per query with
    /// <c>.IgnoreQueryFilters()</c>.
    /// </summary>
    public static void ApplySoftDeleteQueryFilter(this ModelBuilder mb)
    {
        foreach (IMutableEntityType entityType in mb.Model.GetEntityTypes())
        {
            // Owned types are mapped into their owner's table and cannot carry their own filter.
            if (entityType.IsOwned())
            {
                continue;
            }

            if (!typeof(VortexEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
            LambdaExpression filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(parameter, nameof(VortexEntity.DeletedAt)),
                    Expression.Constant(null, typeof(DateTime?))
                ),
                parameter
            );

            mb.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public static void ApplyDefaultAttributesFromEntities(this ModelBuilder modelBuilder)
    {
        // Iterate only types already in the model (no assembly hardcoding)
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type clr = entityType.ClrType;
            EntityTypeBuilder entity = modelBuilder.Entity(clr);

            foreach (
                PropertyInfo prop in clr.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            )
            {
                // Skip if property not mapped
                IMutableProperty? propMeta = entityType.FindProperty(prop.Name);
                if (propMeta is null)
                {
                    continue;
                }

                // 1) SQL default
                DefaultValueSqlAttribute? sqlAttr =
                    prop.GetCustomAttribute<DefaultValueSqlAttribute>();
                if (sqlAttr is not null)
                {
                    entity.Property(prop.Name).HasDefaultValueSql(sqlAttr.Sql);
                    continue; // if you set SQL default, you typically skip constant default
                }

                // 2) Constant default (supports enums)
                DefaultValueAttribute? constAttr = prop.GetCustomAttribute<DefaultValueAttribute>();
                if (constAttr is not null)
                {
                    // If this is an enum, HasDefaultValue(enumValue) is fine
                    // provided the property is mapped as enum (or has a converter).
                    entity.Property(prop.Name).HasDefaultValue(constAttr.Value);
                }

                // 3) Optional: enum storage guidance (int/long/string)
                EnumStorageAttribute? enumAttr = prop.GetCustomAttribute<EnumStorageAttribute>();
                if (enumAttr is not null && prop.PropertyType.IsEnum)
                {
                    Type underlying = enumAttr.Underlying ?? typeof(int);
                    if (underlying == typeof(int))
                    {
                        entity.Property(prop.Name).HasConversion<int>();
                    }
                    else if (underlying == typeof(long))
                    {
                        entity.Property(prop.Name).HasConversion<long>();
                    }
                    else if (underlying == typeof(string))
                    {
                        entity.Property(prop.Name).HasConversion<string>();
                    }
                }
            }
        }
    }

    public static void ApplyConventions(this ModelBuilder mb)
    {
        foreach (
            IMutableProperty p in mb
                .Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(string))
        )
        {
            p.SetMaxLength(p.GetMaxLength() ?? 512);
        }

        ValueConverter<DateTime, DateTime> utc = new(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
        );

        foreach (
            IMutableProperty p in mb
                .Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(DateTime))
        )
        {
            p.SetValueConverter(utc);
        }

        foreach (
            IMutableProperty p in mb
                .Model.GetEntityTypes()
                .SelectMany(t => t.GetProperties())
                .Where(p => p.ClrType == typeof(decimal))
        )
        {
            p.SetPrecision(p.GetPrecision() ?? 18);
            p.SetScale(p.GetScale() ?? 6);
        }
    }

    public static void ApplyTablePrefix(this ModelBuilder mb, string prefix, string? schema = null)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return;
        }

        foreach (IMutableEntityType entity in mb.Model.GetEntityTypes())
        {
            // Skip owned types (mapped into owner's table)
            if (entity.IsOwned())
            {
                continue;
            }

            string? current = entity.GetTableName();

            if (string.IsNullOrEmpty(current))
            {
                continue;
            }

            // Don’t double-prefix
            if (!current.StartsWith(prefix, StringComparison.Ordinal))
            {
                entity.SetTableName(prefix + current);
            }

            if (!string.IsNullOrEmpty(schema))
            {
                entity.SetSchema(schema);
            }
        }
    }
}
