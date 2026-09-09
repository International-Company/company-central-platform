using CCP.Kernel.Infrastructure.Outbox;
using CCP.Kernel.Infrastructure.Persistence;
using CCP.Modules.Workflow.Domain.Definitions;
using CCP.Modules.Workflow.Domain.Instances;
using Microsoft.EntityFrameworkCore;

namespace CCP.Modules.Workflow.Infrastructure.Persistence;

/// <summary>
/// The <c>workflow</c> schema.
/// <para>
/// One DbContext and one migration history per module (ADR-004). <b>No foreign
/// key leaves this schema.</b> <c>RequestedBy</c>, <c>AssignedToUserId</c> and
/// <c>ActorUserId</c> all point at <c>identity.users</c> and are deliberately
/// just columns — asserted by an architecture test, and the reason this module
/// could be lifted out and run on its own.
/// </para>
/// <para>
/// The business resource is two opaque strings. There is no foreign key to a
/// purchase order because the Platform has never heard of one, and adding a
/// column named <c>PurchaseOrderId</c> here is precisely the mistake that would
/// turn a general engine into one system's approval logic.
/// </para>
/// </summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options)
    : DbContext(options)
{
    public const string SchemaName = "workflow";

    public DbSet<WorkflowDefinition> Definitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowStep> Steps => Set<WorkflowStep>();

    public DbSet<WorkflowTransition> Transitions => Set<WorkflowTransition>();

    public DbSet<WorkflowInstance> Instances => Set<WorkflowInstance>();

    public DbSet<WorkflowInstanceAction> InstanceActions => Set<WorkflowInstanceAction>();

    public DbSet<WorkflowTask> Tasks => Set<WorkflowTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);

        ConfigureDefinitions(modelBuilder);
        ConfigureSteps(modelBuilder);
        ConfigureTransitions(modelBuilder);
        ConfigureInstances(modelBuilder);
        ConfigureInstanceActions(modelBuilder);
        ConfigureTasks(modelBuilder);

        // Mapped so a staged event commits in the same transaction as the change
        // that produced it (§8.5). Created by the kernel migration, never here.
        modelBuilder.ConfigureOutbox();

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplySnakeCaseNames();
    }

    private static void ConfigureDefinitions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("definitions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ApplicationCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Code).HasMaxLength(64).IsRequired();
            entity.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(e => e.NameEn).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.InitialStepKey).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();

            // One version of one process per application. Two rows claiming to
            // be version 3 of "purchase-approval" would make "which definition
            // is this instance running on" unanswerable.
            entity.HasIndex(e => new { e.ApplicationCode, e.Code, e.Version })
                .HasDatabaseName("ux_definitions_app_code_version")
                .IsUnique();

            // The lookup that starts every instance: newest published version.
            entity.HasIndex(e => new { e.ApplicationCode, e.Code, e.Status })
                .HasDatabaseName("ix_definitions_app_code_status");

            entity.HasMany(e => e.Steps)
                .WithOne()
                .HasForeignKey(s => s.DefinitionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Steps).AutoInclude();

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureSteps(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowStep>(entity =>
        {
            entity.ToTable("steps");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Key).HasMaxLength(64).IsRequired();
            entity.Property(e => e.NameAr).HasMaxLength(200).IsRequired();
            entity.Property(e => e.NameEn).HasMaxLength(200).IsRequired();

            // A complex property, not an owned entity. The rule is a value: it
            // has no identity, is never queried on its own, and belongs to
            // exactly one step. Owning it would give it a shadow key and a
            // second primary-key constraint on the same table, which is the
            // model telling you it is not really a separate thing.
            entity.ComplexProperty(e => e.Assignee, assignee =>
            {
                assignee.Property(a => a.Strategy).HasColumnName("assignee_strategy")
                    .HasConversion<int>().IsRequired();
                assignee.Property(a => a.UserId).HasColumnName("assignee_user_id");
                assignee.Property(a => a.RoleId).HasColumnName("assignee_role_id");
                assignee.Property(a => a.PositionId).HasColumnName("assignee_position_id");
                assignee.Property(a => a.UnitId).HasColumnName("assignee_unit_id");
            });

            entity.HasIndex(e => new { e.DefinitionId, e.Key })
                .HasDatabaseName("ux_steps_definition_key")
                .IsUnique();

            entity.HasMany(e => e.Transitions)
                .WithOne()
                .HasForeignKey(t => t.StepId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Navigation(e => e.Transitions).AutoInclude();
        });

    private static void ConfigureTransitions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowTransition>(entity =>
        {
            entity.ToTable("transitions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Action).HasConversion<int>();
            entity.Property(e => e.TargetStepKey).HasMaxLength(64);

            // One outcome per action per step, enforced in the database as well
            // as in the aggregate: two "approve" transitions from one step is a
            // process whose next state nobody can predict.
            entity.HasIndex(e => new { e.StepId, e.Action })
                .HasDatabaseName("ux_transitions_step_action")
                .IsUnique();
        });

    private static void ConfigureInstances(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.ToTable("instances");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.DefinitionCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ApplicationCode).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.ResourceId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CurrentStepKey).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<int>();

            // The question a business application actually asks: "what is
            // happening to my record?" It arrives with a type and an id and
            // wants one answer, so both are indexed together.
            entity.HasIndex(e => new { e.ApplicationCode, e.ResourceType, e.ResourceId })
                .HasDatabaseName("ix_instances_resource");

            entity.HasIndex(e => new { e.Status, e.StartedAt })
                .HasDatabaseName("ix_instances_status_started");

            entity.HasIndex(e => e.RequestedBy).HasDatabaseName("ix_instances_requested_by");

            entity.HasMany(e => e.Actions)
                .WithOne()
                .HasForeignKey(a => a.InstanceId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Ignore(e => e.DomainEvents);
        });

    private static void ConfigureInstanceActions(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowInstanceAction>(entity =>
        {
            entity.ToTable("instance_actions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepKey).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Action).HasConversion<int>();
            entity.Property(e => e.Comment).HasMaxLength(2000);

            entity.HasIndex(e => new { e.InstanceId, e.OccurredAt })
                .HasDatabaseName("ix_instance_actions_instance_time");
        });

    private static void ConfigureTasks(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<WorkflowTask>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.StepKey).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CompletedWith).HasConversion<int>();

            // The inbox query, which is the one people wait on: everything
            // pending for me, newest first.
            entity.HasIndex(e => new { e.AssignedToUserId, e.Status, e.AssignedAt })
                .HasDatabaseName("ix_tasks_assignee_status");

            entity.HasIndex(e => new { e.InstanceId, e.Status })
                .HasDatabaseName("ix_tasks_instance_status");

            // The escalation sweep. Partial, because it runs every few minutes
            // and only ever wants the small set that is pending, has a deadline
            // and has not been escalated — scanning every task ever created to
            // find them would make the sweep cost grow with history.
            entity.HasIndex(e => e.DueAt)
                .HasDatabaseName("ix_tasks_due")
                .HasFilter("status = 1 AND due_at IS NOT NULL AND escalated_at IS NULL");

            entity.Ignore(e => e.DomainEvents);
        });
}
