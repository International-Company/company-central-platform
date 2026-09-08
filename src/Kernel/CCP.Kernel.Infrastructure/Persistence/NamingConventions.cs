using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CCP.Kernel.Infrastructure.Persistence;

/// <summary>
/// Applies the database naming convention from ARCHITECTURE.md §10.3:
/// <c>snake_case</c> for tables, columns, keys and indexes.
/// <para>
/// Applied centrally so every module's schema matches without each one
/// remembering. PostgreSQL folds unquoted identifiers to lower case, so
/// snake_case avoids the quoted-identifier friction that PascalCase causes
/// when someone queries the database by hand.
/// </para>
/// </summary>
public static class NamingConventions
{
    /// <summary>Rewrites every mapped name in the model to snake_case.</summary>
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entity in modelBuilder.Model.GetEntityTypes())
        {
            string? tableName = entity.GetTableName();

            if (tableName is not null)
            {
                entity.SetTableName(ToSnakeCase(tableName));
            }

            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                if (key.GetName() is { } keyName)
                {
                    key.SetName(ToSnakeCase(keyName));
                }
            }

            foreach (IMutableForeignKey foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() is { } constraintName)
                {
                    foreignKey.SetConstraintName(ToSnakeCase(constraintName));
                }
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() is { } indexName)
                {
                    index.SetDatabaseName(ToSnakeCase(indexName));
                }
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var builder = new StringBuilder(name.Length + 8);

        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];

            if (char.IsUpper(current))
            {
                bool previousIsLowerOrDigit = i > 0 && !char.IsUpper(name[i - 1]) && name[i - 1] != '_';
                bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                bool previousIsUpper = i > 0 && char.IsUpper(name[i - 1]);

                // Insert a separator at a lower-to-upper boundary (userId ->
                // user_id) and at the end of an acronym (HTTPServer ->
                // http_server), but not in the middle of one.
                if (i > 0 && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }
}
