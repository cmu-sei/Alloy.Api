# Caster.Api.Client Update Required

## Current State
- Alloy API uses `Caster.Api.Client` version `1.5.0`
- This version does not include `GetAllProjectsAsync` method
- Alloy UI cannot fetch Caster project names to display in directory dropdowns

## Solution
1. Run Caster's "Build Client Package" GitHub workflow with version `1.6.0`
   - Workflow: `.github/workflows/build-client.yml` 
   - This will generate client code from current Caster API (which includes GetAllProjects endpoint)
   - Publish to NuGet as version 1.6.0

2. Update Alloy.Api.csproj to use `Caster.Api.Client` version `1.6.0`

3. Add GetProjects endpoint to Alloy API (CasterController)

4. Update Alloy UI to fetch projects and display "ProjectName - DirectoryName" instead of "Project: {uuid} | DirectoryName"

## Verification
The swagger.json generated from Caster API includes:
- `operationId: "GetAllProjects"` with parameters `OnlyMine` (bool)
