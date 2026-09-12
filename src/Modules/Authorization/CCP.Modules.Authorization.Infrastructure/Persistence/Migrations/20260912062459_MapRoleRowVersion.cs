using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CCP.Modules.Authorization.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records that <c>authz.roles</c> is now mapped with a row version, and
    /// changes nothing in the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately empty, and it has to be.</b> The row version is
    /// PostgreSQL's own <c>xmin</c> — a system column every table already has,
    /// which is the entire reason optimistic concurrency could be added here
    /// without a schema change. EF does not know that: it sees a new property in
    /// the model and generates <c>AddColumn</c>, which PostgreSQL refuses
    /// because the column exists and cannot be added.
    /// </para>
    /// <para>
    /// The migration is still needed. Mapping a shadow property changes the
    /// model snapshot, and EF refuses to start against a database whose snapshot
    /// does not match — which is how this was found: twelve integration tests
    /// across five unrelated suites failing in a millisecond each, because every
    /// host that built the Authorization context threw before serving anything.
    /// </para>
    /// </remarks>
    public partial class MapRoleRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nothing. See the remarks: xmin is already there.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo. Dropping xmin is not a thing PostgreSQL permits,
            // and unmapping it is a code change rather than a schema one.
        }
    }
}
