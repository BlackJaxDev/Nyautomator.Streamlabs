# Nyautomator Streamlabs Module

Streamlabs integration module for Nyautomator. It owns Streamlabs OAuth, token status, the dashboard and settings cards, and packaging.

## Source Layout

- `src/Nyautomator.Streamlabs`: Streamlabs OAuth client and token helpers.
- `src/Nyautomator.Module.Streamlabs`: Module entrypoint and `/modules/streamlabs/api/*` handler (`status`, `authorize`, `disconnect`, `DELETE token`).
- `tests/Nyautomator.Module.Streamlabs.Tests`: API handler tests.
- `module/module.json`: Nyautomator module contribution manifest.
- `module/assets`: icon, settings/dashboard/tab UI, config schema, and graph template assets.

## Build

From inside a Nyautomator checkout (`Submodules/Nyautomator.Streamlabs`), the projects reference the host SDK projects by source:

```powershell
dotnet build .\Nyautomator.Streamlabs.slnx
dotnet test .\Nyautomator.Streamlabs.slnx
```

Standalone binary-reference build against host SDK DLLs:

```powershell
dotnet build .\Nyautomator.Streamlabs.slnx -p:UseNyautomatorProjectReferences=false -p:NyautomatorReferencePath=C:\Path\To\NyautomatorDlls\
```

## Packaging

```powershell
.\scripts\pack-module.ps1 -Configuration Release -UseNyautomatorProjectReferences false -NyautomatorReferencePath C:\Path\To\NyautomatorDlls\
```

The script writes `artifacts/modules/streamlabs-<version>.zip`. Install it from the Nyautomator **Modules** tab. The package contains only Streamlabs-owned assemblies, required third-party dependencies, `module.json`, and `module/assets/**`; host SDK assemblies stay compile-time references and are excluded.
