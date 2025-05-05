// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Alloy.Api.Data
{
    public enum EventStatus
    {
        Creating = 1,
        Active = 2,
        Paused = 3,
        Ended = 4,
        Expired = 5,
        Planning = 6,
        Applying = 8,
        Failed = 10,
        Ending = 11
    }

    public enum InternalEventStatus
    {
        LaunchQueued = 1,
        CreatingView = 2,
        CreatingScenario = 3,
        CreatingWorkspace = 4,
        WritingWorkspaceFile = 5,
        PlanningLaunch = 6,
        PlannedLaunch = 7,
        ApplyingLaunch = 8,
        FailedLaunch = 9,
        AppliedLaunch = 10,
        StartingScenario = 11,
        Launched = 12,
        EndQueued = 21,
        DeletingView = 22,
        DeletedView = 23,
        DeletingScenario = 24,
        DeletedScenario = 25,
        DeletingWorkspace = 26,
        VerifyingWorkspace = 27,
        DeletedWorkspace = 28,
        PlanningDestroy = 29,
        PlannedDestroy = 30,
        ApplyingDestroy = 31,
        FailedDestroy = 32,
        AppliedDestroy = 33,
        Ended = 34,
        PlanningRedeploy = 35,
        PlannedRedeploy = 36,
        ApplyingRedeploy = 37,
        AppliedRedeploy = 38
    }

    public enum AlloyClaimTypes
    {
        AlloyBasic,
        SystemAdmin,
        ContentDeveloper
    }

    public enum SystemPermission
    {
        CreateEventTemplates,
        ViewEventTemplates,
        EditEventTemplates,
        ManageEventTemplates,
        CreateEvents,
        ViewEvents,
        EditEvents,
        ExecuteEvents,
        ManageEvents,
        ViewUsers,
        ManageUsers,
        ViewRoles,
        ManageRoles,
        ViewGroups,
        ManageGroups
    }

    public enum EventPermission
    {
        ViewEvent,
        EditEvent,
        ExecuteEvent,
        ManageEvent
    }

    public enum EventTemplatePermission
    {
        ViewEventTemplate,
        EditEventTemplate,
        ManageEventTemplate
    }


}
