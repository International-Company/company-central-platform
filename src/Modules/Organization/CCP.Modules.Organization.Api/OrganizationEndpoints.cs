using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Organization.Application.Employees;
using CCP.Modules.Organization.Application.Units;
using CCP.Modules.Organization.Contracts.Dtos;
using CCP.Modules.Organization.Domain.Units;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Organization.Api;

/// <summary>
/// Organizational structure and employee records.
/// <para>
/// Reading the structure requires <c>platform.organization.view</c>; changing it
/// requires <c>platform.organization.manage</c>. Org data is broadly readable
/// inside the company and narrowly writable (ARCHITECTURE.md §7.2.2) — the
/// people who may see the org chart are many, the people who may restructure it
/// are few.
/// </para>
/// <para>
/// These permissions are <b>enforced</b> from Phase 4. The employee search also
/// applies the caller's organizational scope filter at the data layer, so
/// someone entitled to one department sees one department — not the whole
/// company with a permission check in front of it.
/// </para>
/// </summary>
public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        MapUnitEndpoints(versionGroup);
        MapEmployeeEndpoints(versionGroup);
    }

    private static void MapUnitEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder units = versionGroup
            .MapGroup("/organization/units")
            .WithTags("Organization");

        units.MapGet("/tree", async (
            bool? includeInactive,
            HttpContext context,
            [FromServices] GetUnitTreeHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<OrganizationUnitTreeDto>> result = await handler.HandleAsync(
                new GetUnitTreeQuery(includeInactive ?? false), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.organization.view"))
            .WithName("GetOrganizationTree")
            .WithSummary("Returns the company structure as a nested tree.");

        units.MapPost("/", async (
            CreateUnitRequest request,
            HttpContext context,
            [FromServices] CreateUnitHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<OrganizationUnitDto> result = await handler.HandleAsync(
                new CreateUnitCommand(
                    request.ParentId, request.ParsedUnitType, request.Code,
                    request.NameAr, request.NameEn),
                cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult(
                    $"/api/v1/organization/units/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.organization.manage"))
            .WithName("CreateOrganizationUnit")
            .WithSummary("Creates an organizational unit under an optional parent.");

        units.MapPut("/{id:guid}/name", async (
            Guid id,
            RenameUnitRequest request,
            HttpContext context,
            [FromServices] RenameUnitHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<OrganizationUnitDto> result = await handler.HandleAsync(
                new RenameUnitCommand(id, request.NameAr, request.NameEn), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.organization.manage"))
            .WithName("RenameOrganizationUnit")
            .WithSummary("Renames a unit in both languages.");

        units.MapPost("/{id:guid}/move", async (
            Guid id,
            MoveUnitRequest request,
            HttpContext context,
            [FromServices] MoveUnitHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new MoveUnitCommand(id, request.NewParentId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.organization.manage"))
            .WithName("MoveOrganizationUnit")
            .WithSummary("Moves a unit and every descendant to a new parent, atomically.");

        units.MapPost("/{id:guid}/deactivate", async (
            Guid id,
            HttpContext context,
            [FromServices] DeactivateUnitHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(new DeactivateUnitCommand(id), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.organization.manage"))
            .WithName("DeactivateOrganizationUnit")
            .WithSummary("Deactivates a unit that has no active children or employees.");
    }

    private static void MapEmployeeEndpoints(IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder employees = versionGroup
            .MapGroup("/organization/employees")
            .WithTags("Employees");

        employees.MapGet("/", async (
            int? page,
            int? pageSize,
            string? sort,
            string? q,
            Guid? unitId,
            bool? includeSubUnits,
            bool? isActive,
            HttpContext context,
            [FromServices] SearchEmployeesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> pageRequest = PageRequest.Create(page, pageSize, sort);

            if (pageRequest.IsFailure)
            {
                return pageRequest.ToHttpResult(context, requestContext);
            }

            // The filter the authorization handler attached after admitting this
            // request. Reading it here is what turns "may list employees" into
            // "may list these employees"; defaulting to SelfOnly means a wiring
            // mistake denies rather than leaks.
            ScopeFilter scope = context.GetScopeFilter();

            Result<PagedResult<EmployeeDto>> result = await handler.HandleAsync(
                new SearchEmployeesQuery(
                    pageRequest.Value, q, unitId, includeSubUnits ?? false, isActive,
                    scope.UnitPathPrefixes,
                    ScopeRestricted: scope.Kind != ScopeFilterKind.All),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.employees.view"))
            // Declares what a success returns, so the OpenAPI document describes
            // the response and not merely the request. The frontend generates its
            // types from that document; without this the response shape is a guess
            // written by hand, which is how `expiresIn` and a top-level
            // `mustChangePassword` reached production.
            .Produces<PagedResult<EmployeeDto>>(StatusCodes.Status200OK)
            .WithName("SearchEmployees")
            .WithSummary("Lists employees, optionally scoped to a unit and everything beneath it.");

        employees.MapPost("/", async (
            CreateEmployeeRequest request,
            HttpContext context,
            [FromServices] CreateEmployeeHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result validation = request.Validate();

            if (validation.IsFailure)
            {
                return validation.ToHttpResult(context, requestContext);
            }

            Result<EmployeeDto> result = await handler.HandleAsync(
                new CreateEmployeeCommand(
                    request.EmployeeNumber, request.FullNameAr, request.FullNameEn,
                    request.UnitId, request.PositionId, request.ManagerId, request.UserId,
                    request.WorkEmail, request.WorkPhone, request.HireDate),
                cancellationToken);

            return result.IsSuccess
                ? result.ToCreatedResult(
                    $"/api/v1/organization/employees/{result.Value.Id}", context, requestContext)
                : result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.employees.manage"))
            .WithName("CreateEmployee")
            .WithSummary("Creates an employee record.");

        employees.MapPost("/{id:guid}/transfer", async (
            Guid id,
            TransferEmployeeRequest request,
            HttpContext context,
            [FromServices] TransferEmployeeHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new TransferEmployeeCommand(
                    id, request.NewUnitId, request.NewPositionId, request.NewManagerId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.employees.manage"))
            .WithName("TransferEmployee")
            .WithSummary("Moves an employee to a different unit, position or manager.");

        employees.MapPut("/{id:guid}/user", async (
            Guid id,
            LinkUserRequest request,
            HttpContext context,
            [FromServices] LinkEmployeeUserHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new LinkEmployeeUserCommand(id, request.UserId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.employees.manage"))
            .WithName("LinkEmployeeUser")
            .WithSummary("Links or unlinks an employee's Platform account.");
    }
}

// ---------------------------------------------------------------------------
// Request bodies
// ---------------------------------------------------------------------------

/// <summary>Create-unit request body.</summary>
public sealed record CreateUnitRequest(
    Guid? ParentId,
    string UnitType,
    string Code,
    string NameAr,
    string NameEn)
{
    /// <summary>The parsed unit type. Only meaningful after <see cref="Validate"/> succeeds.</summary>
    public OrganizationUnitType ParsedUnitType { get; private set; }

    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(Code))
        {
            errors.Add(Error.Validation("ORGANIZATION.CODE_REQUIRED", "A code is required.", "code"));
        }

        if (string.IsNullOrWhiteSpace(UnitType)
            || !Enum.TryParse(UnitType, ignoreCase: true, out OrganizationUnitType parsed))
        {
            errors.Add(Error.Validation(
                "ORGANIZATION.INVALID_UNIT_TYPE",
                $"Unknown unit type. Allowed: {string.Join(", ", Enum.GetNames<OrganizationUnitType>())}.",
                "unitType"));
        }
        else
        {
            ParsedUnitType = parsed;
        }

        // Names are validated by the domain, which owns the rule that both
        // languages are required. Duplicating it here would let the two drift.
        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

/// <summary>Rename request body.</summary>
public sealed record RenameUnitRequest(string NameAr, string NameEn)
{
    public Result Validate()
        => string.IsNullOrWhiteSpace(NameAr) || string.IsNullOrWhiteSpace(NameEn)
            ? Result.Failure(Error.Validation(
                "ORGANIZATION.NAME_REQUIRED", "Both Arabic and English names are required.", "name"))
            : Result.Success();
}

/// <summary>Move request body. A null parent moves the unit to the top level.</summary>
public sealed record MoveUnitRequest(Guid? NewParentId);

/// <summary>Create-employee request body.</summary>
public sealed record CreateEmployeeRequest(
    string EmployeeNumber,
    string FullNameAr,
    string FullNameEn,
    Guid UnitId,
    Guid? PositionId,
    Guid? ManagerId,
    Guid? UserId,
    string? WorkEmail,
    string? WorkPhone,
    DateOnly? HireDate)
{
    public Result Validate()
    {
        List<Error> errors = [];

        if (string.IsNullOrWhiteSpace(EmployeeNumber))
        {
            errors.Add(Error.Validation(
                "ORGANIZATION.EMPLOYEE_NUMBER_REQUIRED",
                "An employee number is required.",
                "employeeNumber"));
        }

        if (UnitId == Guid.Empty)
        {
            errors.Add(Error.Validation(
                "ORGANIZATION.UNIT_REQUIRED", "A unit is required.", "unitId"));
        }

        return errors.Count == 0 ? Result.Success() : Result.Failure(errors);
    }
}

/// <summary>Transfer request body.</summary>
public sealed record TransferEmployeeRequest(Guid NewUnitId, Guid? NewPositionId, Guid? NewManagerId);

/// <summary>User-link request body. A null user id unlinks.</summary>
public sealed record LinkUserRequest(Guid? UserId);
