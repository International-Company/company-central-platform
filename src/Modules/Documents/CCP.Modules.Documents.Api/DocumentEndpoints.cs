using CCP.Kernel.Api.Context;
using CCP.Kernel.Api.Errors;
using CCP.Kernel.Api.Security;
using CCP.Kernel.Paging;
using CCP.Kernel.Results;
using CCP.Modules.Documents.Application;
using CCP.Modules.Documents.Contracts.Dtos;
using CCP.Modules.Documents.Domain.Access;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CCP.Modules.Documents.Api;

/// <summary>
/// The documents surface.
/// <para>
/// <b>Two checks on every route, not one.</b> The permission says whether the
/// caller may work with documents at all; the access rules say which ones. The
/// second check lives in the handlers, because it needs the document — and an
/// endpoint that only had the first would let anybody holding
/// <c>platform.documents.read</c> read every file in the company.
/// </para>
/// </summary>
public static class DocumentEndpoints
{
    public static void MapDocumentEndpoints(this IEndpointRouteBuilder versionGroup)
    {
        RouteGroupBuilder documents = versionGroup
            .MapGroup("/documents")
            .WithTags("Documents");

        MapReading(documents);
        MapWriting(documents);
        MapSharing(documents);
        MapLinking(versionGroup, documents);
    }

    // -----------------------------------------------------------------------
    // Reading
    // -----------------------------------------------------------------------

    private static void MapReading(RouteGroupBuilder documents)
    {
        documents.MapGet("/", async (
            string? term,
            string? category,
            Guid? organizationUnitId,
            bool? includeDeleted,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] SearchDocumentsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> paging = PageRequest.Create(page, pageSize);

            if (paging.IsFailure)
            {
                return paging.ToHttpResult(context, requestContext);
            }

            Result<PagedResult<DocumentDto>> result = await handler.HandleAsync(
                new SearchDocumentsQuery(
                    term, category, organizationUnitId, includeDeleted ?? false, paging.Value),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .Produces<PagedResult<DocumentDto>>(StatusCodes.Status200OK)
            .WithName("SearchDocuments")
            .WithSummary("Documents the caller may see.");

        documents.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] GetDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<DocumentDto> result = await handler.HandleAsync(
                new GetDocumentQuery(id), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .WithName("GetDocument")
            .WithSummary("One document, with every version of it.");

        documents.MapGet("/{id:guid}/content", async (
            Guid id,
            int? version,
            HttpContext context,
            [FromServices] PrepareDownloadHandler prepare,
            [FromServices] OpenDocumentContentHandler open,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            // Ask for a pre-signed URL first. When the store can issue one the
            // bytes never touch this process, which is the difference between a
            // download and a held request thread.
            Result<DocumentDownloadDto> prepared = await prepare.HandleAsync(
                new DownloadDocumentQuery(id, version), cancellationToken);

            if (prepared.IsFailure)
            {
                return prepared.ToHttpResult(context, requestContext);
            }

            if (prepared.Value.Url is { } url)
            {
                return Results.Redirect(url, permanent: false, preserveMethod: false);
            }

            Result<OpenDocumentContentHandler.OpenedContent> opened = await open.HandleAsync(
                new DownloadDocumentQuery(id, version), cancellationToken);

            if (opened.IsFailure)
            {
                return opened.ToHttpResult(context, requestContext);
            }

            // Always an attachment. Rendering an uploaded file inline would run
            // whatever it contains on the Platform's own origin, which is the
            // whole of a stored cross-site-scripting attack for any format that
            // can carry script.
            return Results.File(
                opened.Value.Content,
                opened.Value.ContentType,
                opened.Value.FileName,
                enableRangeProcessing: true);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .WithName("DownloadDocument")
            .WithSummary("The content, by redirect to storage or streamed from here.");

        documents.MapGet("/{id:guid}/access-log", async (
            Guid id,
            int? page,
            int? pageSize,
            HttpContext context,
            [FromServices] GetAccessLogHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<PageRequest> paging = PageRequest.Create(page, pageSize);

            if (paging.IsFailure)
            {
                return paging.ToHttpResult(context, requestContext);
            }

            Result<PagedResult<DocumentAccessLogDto>> result = await handler.HandleAsync(
                new GetAccessLogQuery(id, paging.Value), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .Produces<PagedResult<DocumentAccessLogDto>>(StatusCodes.Status200OK)
            .WithName("GetDocumentAccessLog")
            .WithSummary("Who opened this document, and who tried and could not.");
    }

    // -----------------------------------------------------------------------
    // Writing
    // -----------------------------------------------------------------------

    private static void MapWriting(RouteGroupBuilder documents)
    {
        documents.MapPost("/", async (
            HttpContext context,
            [FromServices] UploadDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<UploadedFile> file = await ReadUploadAsync(context, cancellationToken);

            if (file.IsFailure)
            {
                return file.ToHttpResult(context, requestContext);
            }

            IFormCollection form = file.Value.Form;

            Result<DocumentDto> result = await handler.HandleAsync(
                new UploadDocumentCommand(
                    form["title"].ToString() is { Length: > 0 } title
                        ? title
                        : file.Value.File.FileName,
                    form["category"].ToString(),
                    Guid.TryParse(form["organizationUnitId"], out Guid unit) ? unit : null,
                    file.Value.File.FileName,
                    file.Value.File.ContentType,
                    file.Value.Content),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.create"))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .DisableAntiforgery()
            .WithName("UploadDocument")
            .WithSummary("Stores a file and the document that holds it.");

        documents.MapPost("/{id:guid}/versions", async (
            Guid id,
            HttpContext context,
            [FromServices] AddVersionHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<UploadedFile> file = await ReadUploadAsync(context, cancellationToken);

            if (file.IsFailure)
            {
                return file.ToHttpResult(context, requestContext);
            }

            Result<DocumentDto> result = await handler.HandleAsync(
                new AddVersionCommand(
                    id,
                    file.Value.File.FileName,
                    file.Value.File.ContentType,
                    file.Value.Content,
                    file.Value.Form["notes"].ToString()),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.update"))
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .DisableAntiforgery()
            .WithName("AddDocumentVersion")
            .WithSummary("Adds a version. The previous one stays downloadable.");

        documents.MapPut("/{id:guid}", async (
            Guid id,
            UpdateDocumentRequest request,
            HttpContext context,
            [FromServices] UpdateDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<DocumentDto> result = await handler.HandleAsync(
                new UpdateDocumentCommand(
                    id, request.Title, request.Category, request.OrganizationUnitId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.update"))
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .WithName("UpdateDocument")
            .WithSummary("Renames a document, and moves it if the caller may.");

        documents.MapDelete("/{id:guid}", async (
            Guid id,
            HttpContext context,
            [FromServices] DeleteDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.delete"))
            .WithName("DeleteDocument")
            .WithSummary("Marks a document for deletion. Content survives the grace period.");

        documents.MapPost("/{id:guid}/restore", async (
            Guid id,
            HttpContext context,
            [FromServices] RestoreDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.delete"))
            .WithName("RestoreDocument")
            .WithSummary("Takes back a deletion, inside the grace period.");
    }

    // -----------------------------------------------------------------------
    // Sharing
    // -----------------------------------------------------------------------

    private static void MapSharing(RouteGroupBuilder documents)
    {
        documents.MapGet("/{id:guid}/access", async (
            Guid id,
            HttpContext context,
            [FromServices] GetAccessRulesHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<DocumentAccessRuleDto>> result =
                await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .Produces<IReadOnlyList<DocumentAccessRuleDto>>(StatusCodes.Status200OK)
            .WithName("GetDocumentAccess")
            .WithSummary("Who else can see this document.");

        documents.MapPut("/{id:guid}/access", async (
            Guid id,
            GrantAccessRequest request,
            HttpContext context,
            [FromServices] GrantAccessHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(request.SubjectKind, ignoreCase: true, out AccessSubjectKind kind))
            {
                return Results.BadRequest();
            }

            if (!Enum.TryParse(request.Level, ignoreCase: true, out DocumentAccessLevel level))
            {
                return Results.BadRequest();
            }

            Result<DocumentAccessRuleDto> result = await handler.HandleAsync(
                new GrantAccessCommand(
                    id, kind, request.SubjectId, level, request.IncludesSubUnits ?? false),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.share"))
            .Produces<DocumentAccessRuleDto>(StatusCodes.Status200OK)
            .WithName("GrantDocumentAccess")
            .WithSummary("Shares a document, or changes what somebody already has.");

        documents.MapDelete("/{id:guid}/access/{ruleId:guid}", async (
            Guid id,
            Guid ruleId,
            HttpContext context,
            [FromServices] RevokeAccessHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new RevokeAccessCommand(id, ruleId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.share"))
            .WithName("RevokeDocumentAccess")
            .WithSummary("Takes access away.");
    }

    // -----------------------------------------------------------------------
    // Linking
    // -----------------------------------------------------------------------

    private static void MapLinking(IEndpointRouteBuilder versionGroup, RouteGroupBuilder documents)
    {
        documents.MapGet("/{id:guid}/links", async (
            Guid id,
            HttpContext context,
            [FromServices] GetLinksHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<DocumentLinkDto>> result =
                await handler.HandleAsync(id, cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .Produces<IReadOnlyList<DocumentLinkDto>>(StatusCodes.Status200OK)
            .WithName("GetDocumentLinks")
            .WithSummary("What this document is filed against.");

        documents.MapPost("/{id:guid}/links", async (
            Guid id,
            DocumentLinkRequest request,
            HttpContext context,
            [FromServices] LinkDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<DocumentLinkDto> result = await handler.HandleAsync(
                new LinkDocumentCommand(id, request.ResourceType, request.ResourceId),
                cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.update"))
            .Produces<DocumentLinkDto>(StatusCodes.Status200OK)
            .WithName("LinkDocument")
            .WithSummary("Attaches a document to a record in a business system.");

        documents.MapDelete("/{id:guid}/links", async (
            Guid id,
            string resourceType,
            string resourceId,
            HttpContext context,
            [FromServices] UnlinkDocumentHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result result = await handler.HandleAsync(
                new UnlinkDocumentCommand(id, resourceType, resourceId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.update"))
            .WithName("UnlinkDocument")
            .WithSummary("Detaches it again.");

        // Outside the /documents group on purpose: this is the question a
        // business system asks about its own record, not about a document.
        versionGroup.MapGet("/resources/{resourceType}/{resourceId}/documents", async (
            string resourceType,
            string resourceId,
            HttpContext context,
            [FromServices] GetLinkedDocumentsHandler handler,
            [FromServices] RequestContextAccessor requestContext,
            CancellationToken cancellationToken) =>
        {
            Result<IReadOnlyList<DocumentDto>> result = await handler.HandleAsync(
                new GetLinkedDocumentsQuery(resourceType, resourceId), cancellationToken);

            return result.ToHttpResult(context, requestContext);
        })
            .RequireAuthorization()
            .WithMetadata(new RequirePermissionAttribute("platform.documents.read"))
            .WithTags("Documents")
            .Produces<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)
            .WithName("GetDocumentsForResource")
            .WithSummary("Everything filed against one business record, as the caller may see it.");
    }

    // -----------------------------------------------------------------------
    // Multipart
    // -----------------------------------------------------------------------

    /// <summary>The one file in a multipart request, and the fields beside it.</summary>
    private sealed record UploadedFile(IFormFile File, Stream Content, IFormCollection Form);

    /// <summary>
    /// Pulls the file out of a multipart body, and refuses anything else.
    /// <para>
    /// Exactly one file. A request carrying several would raise the question of
    /// what the other ones were for, and the honest answers — several documents,
    /// or a caller confused about the endpoint — are both better served by
    /// saying so than by silently taking the first.
    /// </para>
    /// </summary>
    private static async Task<Result<UploadedFile>> ReadUploadAsync(
        HttpContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.HasFormContentType)
        {
            return Result.Failure<UploadedFile>(Error.Validation(
                "DOCUMENTS.NOT_MULTIPART",
                "Upload a file as multipart/form-data.",
                "file"));
        }

        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);

        if (form.Files.Count != 1)
        {
            return Result.Failure<UploadedFile>(Error.Validation(
                "DOCUMENTS.ONE_FILE_EXPECTED",
                "Send exactly one file.",
                "file"));
        }

        IFormFile file = form.Files[0];

        return Result.Success(new UploadedFile(file, file.OpenReadStream(), form));
    }
}

/// <summary>Renaming, and possibly moving.</summary>
public sealed record UpdateDocumentRequest(
    string Title, string? Category, Guid? OrganizationUnitId);

/// <summary>Sharing a document with a user, a role or a unit.</summary>
public sealed record GrantAccessRequest(
    string SubjectKind, Guid SubjectId, string Level, bool? IncludesSubUnits);

/// <summary>Attaching a document to a business record.</summary>
public sealed record DocumentLinkRequest(string ResourceType, string ResourceId);
