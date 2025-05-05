As of version 3.5.0, Alloy transitioned to a new permissions model, allowing for more granular access control to different features of the application. This document will detail how the new system works.

# Permissions

Access to features of Alloy are governed by sets of Permissions. Permissions can apply globally or on a per Event Template/Event basis. Examples of global Permissions are:

- CreateEventTemplates - Allows creation of new Event Templates
- ViewEvents - Allows viewing all Events and their Users and Groups
- ManageUsers - Allows for making changes to Users.

The Administration area now can be accessed by any User with View or Manage Permission to an Administration function (e.g. ViewRoles, ManageGroups, etc), but only the areas they have Permissions for will be accessible in the sidebar menu.

There are many more Permissions available. They can be viewed by going to the new Roles section of the Administration area.

# Roles

Permissions can be applied to Users by grouping them into Roles. There are two types of Roles in Alloy:

- System Roles - Each User can have a System Role applied to them that gives global Permissions across all of Alloy. The three default System Roles are:

  - Administrator - Has all Permissions within the system.
  - Content Developer - Has the `CreateEventTemplates`, `CreateEvents`, `ExecuteEvents` Permissions. Users in this Role can create and manage their own Event Templates and Events, but not affect any global settings or other User's Event Templates and Events.
  - Observer - Has the `ViewEventTemplates`, `ViewEvents`, `ViewUsers`, `ViewRoles`, and `ViewGroups` Permissions. Users in this role can view all of these areas, but cannot make any changes.

  Custom System Roles can be created by Users with the `ManageRoles` Permission that include whatever Permissions are desired for that Role. This can be done in the Roles section of the Administration area.

A User can be assigned a Role in the Users section of the Administration area.

Roles can also optionally be integrated with your Identity Provider. See Identity Provider Integration below.

# Seed Data

The SeedData section of appsettings.json has been changed to support the new model. You can now use this section to add Roles and Users on application startup. See appsettings.json for examples.

SeedData will only add objects if they do not exist. It will not modify existing Roles and Users so as not to undo changes made in the application on every restart. It will re-create objects if they are deleted in the application, so be sure to remove them from SeedData if they are no longer wanted.

# Identity Provider Integration

Roles can optionally be integrated with the Identity Provider that is being used to authenticate to Alloy. There are new settings under `ClaimsTransformation` to configure this integration. See appsettings.json. This integration is compatible with any Identity Provider that is capable of putting Roles into the auth token.

## Roles

If enabled, Roles from the User's auth token will be applied as if the Role was set on the User directly in Alloy. The Role must exist in Alloy and the name of the Role in the token must match exactly with the name of the Role in the token.

- UseRolesFromIdp: If true, Roles from the User's auth token will be used. Defaults to true.
- RolesClaimPath: The path within the User's auth token to look for Roles. Defaults to Keycloak's default value of `realm_access.roles`.

  Example: If the defaults are set, Alloy will apply the `Content Developer` Role to a User whose token contains the following:

```json
  realm_access {
    roles: [
        "Content Developer"
    ]
  }
```

If multiple Roles are present in the token, or if one Role is in the token and one Role is set directly on the User in Alloy, the Permissions of all of the Roles will be combined.

## Keycloak

If you are using Keycloak as your Identity Provider, Roles should work by default if you have not changed the default `RolesClaimPath`. You may need to adjust this value if your Keycloak is configured to put Roles in a different location within the token.

# Migration - BREAKING CHANGES

When moving from a version prior to 3.5.0, the database will be migrated from the old Permissions sytem to the new one. **There are breaking changes that require actions to be taken for continued access**

In previous versions, Alloy used Player as a source of Permissions. Now it has it's own Permissions and Users table. Because of this, permissions will not carry over from the old version. **You MUST set at least one user as an Administrator in SeedData BEFORE accessing Alloy with that user after updating, or the user will not have Administrator permissions.** If you are using roles from your identity provider, this is not necessary. If not and you do not set an Administrator in SeedData before accessing Alloy with that user, a User record will be created for that user and it will not be updated by SeedData on subsequent restarts of the application. If this happens, you must create a new user and set it as Administrator in SeedData, or manually set your desired user's Role in the database.
