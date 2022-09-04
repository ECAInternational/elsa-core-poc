using Elsa.Mediator.Services;
using Elsa.Workflows.Management.Entities;

namespace Elsa.Workflows.Management.Notifications;
public record WorkflowDefinitionPublished(WorkflowDefinition WorkflowDefinition) : INotification;