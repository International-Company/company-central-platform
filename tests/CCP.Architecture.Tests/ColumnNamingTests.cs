using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CCP.Architecture.Tests;

/// <summary>
/// Every table and column the Platform generates is snake_case.
/// <para>
/// <b>This is the Phase 1 defect, and it would have failed at migration time.</b>
/// <c>ApplySnakeCaseNames()</c> was written and never called, so EF generated
/// columns as <c>Username</c> while the hand-written partial index filters
/// referred to <c>username</c> — columns that did not exist. The indexes would
/// have failed on the first deployment, and nothing would have said why until
/// somebody read the generated SQL.
/// </para>
/// <para>
/// It was fixed by adding one line to one <c>OnModelCreating</c>, which is
/// exactly the kind of fix that does not stay fixed: the twelfth module has to
/// remember the same line. `adding-a-module.md` says it is "not optional, and
/// not cosmetic" — and until now that sentence was the entire enforcement, which
/// is the same arrangement `[FromServices]` had before somebody looked.
/// </para>
/// <para>
/// The model is built from each module's design-time factory, so this needs no
/// database: EF resolves the whole model from <c>OnModelCreating</c> without
/// ever opening a connection.
/// </para>
/// </summary>
public sealed partial class ColumnNamingTests
{
    /// <summary>
    /// Lower case, digits and underscores. Nothing else.
    /// <para>
    /// PostgreSQL folds an unquoted identifier to lower case, so a PascalCase
    /// column has to be quoted everywhere for ever — in every migration, every
    /// index filter and every piece of hand-written SQL. One place that forgets
    /// is a column that does not exist.
    /// </para>
    /// </summary>
    [GeneratedRegex("^[a-z0-9_]+$", RegexOptions.Compiled)]
    private static partial Regex SnakeCase();

    [Fact]
    public void EveryColumnIsSnakeCase()
    {
        var wrong = new List<string>();

        foreach ((string context, IModel model) in Models())
        {
            foreach (IEntityType entity in model.GetEntityTypes())
            {
                StoreObjectIdentifier? table = StoreObjectIdentifier.Create(
                    entity, StoreObjectType.Table);

                if (table is null)
                {
                    continue;
                }

                foreach (IProperty property in entity.GetProperties())
                {
                    string? column = property.GetColumnName(table.Value);

                    // xmin is PostgreSQL's own system column, mapped as a
                    // concurrency token. Its name is not ours to choose.
                    if (column is null || column == "xmin" || SnakeCase().IsMatch(column))
                    {
                        continue;
                    }

                    wrong.Add($"{context}: {table.Value.Name}.{column}");
                }
            }
        }

        Assert.True(
            wrong.Count == 0,
            "These columns are not snake_case, which means the context is missing "
            + "ApplySnakeCaseNames() in OnModelCreating. PostgreSQL folds unquoted identifiers to "
            + "lower case, so a PascalCase column must be quoted in every migration, every index "
            + "filter and every piece of hand-written SQL — and the one place that forgets is "
            + "referring to a column that does not exist."
            + Environment.NewLine
            + string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void EveryTableIsSnakeCase()
    {
        var wrong = new List<string>();

        foreach ((string context, IModel model) in Models())
        {
            foreach (IEntityType entity in model.GetEntityTypes())
            {
                string? table = entity.GetTableName();

                if (table is null || SnakeCase().IsMatch(table))
                {
                    continue;
                }

                wrong.Add($"{context}: {table}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "These tables are not snake_case." + Environment.NewLine
            + string.Join(Environment.NewLine, wrong));
    }

    /// <summary>
    /// Proves the models are really being loaded.
    /// <para>
    /// Both assertions above are "nothing was wrong", which is what a test says
    /// when it has stopped looking. A factory that failed to resolve, a renamed
    /// assembly or a moved directory would empty the set silently and leave two
    /// green tests guarding nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheModelsAreActuallyLoaded()
    {
        (string Context, IModel Model)[] models = [.. Models()];

        // Ten module schemas and the kernel's. Eleven, not twelve: the
        // Operations module owns no data at all -- it reads the kernel schema to
        // report what the Platform's own machinery is doing, which is why it has
        // no context and no migrations of its own.
        Assert.True(
            models.Length >= 11,
            $"Only {models.Length} model(s) were built: "
            + string.Join(", ", models.Select(m => m.Context)));

        Assert.True(
            models.Sum(m => m.Model.GetEntityTypes().Count()) > 40,
            "The models carry almost no entities, so they are not the real ones.");
    }

    /// <summary>
    /// Builds every module's model through its own design-time factory.
    /// <para>
    /// The factory rather than a hand-made <c>DbContextOptions</c>, because the
    /// factory is what the migration tooling uses — so this asserts against the
    /// model that actually generates the SQL.
    /// </para>
    /// </summary>
    private static IEnumerable<(string Context, IModel Model)> Models()
    {
        string binary = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        foreach (string path in Directory.GetFiles(binary, "CCP.*.Infrastructure.dll").Order())
        {
            Assembly assembly;

            try
            {
                assembly = Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            foreach (Type type in assembly.GetTypes())
            {
                Type? factoryInterface = type.GetInterfaces().FirstOrDefault(i =>
                    i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>));

                if (factoryInterface is null || type.IsAbstract)
                {
                    continue;
                }

                object factory = Activator.CreateInstance(type)!;

                MethodInfo create = factoryInterface.GetMethod(
                    nameof(IDesignTimeDbContextFactory<DbContext>.CreateDbContext))!;

                using var context = (DbContext)create.Invoke(factory, [Array.Empty<string>()])!;

                yield return (context.GetType().Name, context.Model);
            }
        }
    }
}
