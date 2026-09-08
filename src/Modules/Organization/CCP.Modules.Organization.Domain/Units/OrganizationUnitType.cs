namespace CCP.Modules.Organization.Domain.Units;

/// <summary>
/// What kind of organizational unit this is.
/// <para>
/// <b>Departments, sections and centers are one entity, not three.</b> The brief
/// names them separately, and modelling them as three tables would be the
/// literal reading — but it is the wrong one. Three near-identical tables mean
/// every hierarchy query becomes a union of three, "everything under this unit"
/// stops being a single indexed prefix scan, and adding a fourth kind of unit
/// requires a schema change, a migration and code in every query.
/// </para>
/// <para>
/// One <see cref="OrganizationUnit"/> with a type gives arbitrary nesting for
/// free, keeps the materialized path a single column, and makes a new unit kind
/// an enum value rather than a table. The brief's own requirement — that the
/// structure be extensible and assume no fixed number of departments or centers
/// — is better served this way than by encoding today's three levels into the
/// schema.
/// </para>
/// <para>
/// The type is deliberately an enum rather than a reference table. Making unit
/// kinds runtime data would be more flexible and is not yet warranted (P1); if
/// the company ever needs to define its own kinds, promoting this to a reference
/// table is a contained change, and that is the documented escalation path.
/// </para>
/// </summary>
public enum OrganizationUnitType
{
    /// <summary>A major division of the company.</summary>
    Department = 1,

    /// <summary>A subdivision, normally within a department.</summary>
    Section = 2,

    /// <summary>A centre — often geographic or functional.</summary>
    Center = 3,

    /// <summary>A division, above departments in larger structures.</summary>
    Division = 4,

    /// <summary>A branch, normally geographic.</summary>
    Branch = 5,

    /// <summary>A team, normally the smallest unit.</summary>
    Team = 6
}
