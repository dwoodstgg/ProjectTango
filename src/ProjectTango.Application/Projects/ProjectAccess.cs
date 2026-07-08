using ProjectTango.Application.Common;
using ProjectTango.Domain;
using ProjectTango.Domain.Entities;

namespace ProjectTango.Application.Projects;

public static class ProjectAccess
{
    /// <summary>Ops manages any project; a PM only their own; Admin bypasses.
    /// The returned override flag MUST be recorded in the audit event for mutations.</summary>
    public static bool RequireCanManage(this ICurrentUser currentUser, Project project)
    {
        if (currentUser.IsInRole(RoleNames.OperationsManager))
        {
            return false;
        }

        if (currentUser.IsInRole(RoleNames.ProjectManager) && project.ProjectManagerId == currentUser.EmployeeId)
        {
            return false;
        }

        if (currentUser.IsInRole(RoleNames.Admin))
        {
            return true;
        }

        throw new UnauthorizedAccessException("Only Operations Managers or the project's PM can manage this project.");
    }
}
