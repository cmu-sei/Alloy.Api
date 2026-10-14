# Alloy API tests

See [docs/Testing.md](../docs/Testing.md) for how to run the suite (Docker required), how the harness is
built, and what is particular to Alloy. The files in `Support/Shared/` are the harness shared by the
Crucible APIs and are not edited in this repository.

From the repository root:

```sh
dotnet test Alloy.Api.Tests
```
