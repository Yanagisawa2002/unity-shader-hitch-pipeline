# Windows module identity API check

This .NET 8 console program compiles the actual runtime module enumeration
provider. On Windows it makes two read-only captures and checks that the current
executable, kernel32.dll and existing absolute module paths are returned.
Other operating systems compile the same provider and explicitly skip execution.

```powershell
dotnet run --project DotNet/ShaderHitchPipeline.WindowsModules.Smoke --configuration Release --disable-build-servers --property:UseSharedCompilation=false
```

The functional CI calls this program through its explicit CPU allowlist.
It starts no Unity/Player process, uses no graphics API, changes no driver,
and measures no performance. It does not validate IL2CPP marshalling or the
selected GPU's full registry/module identity; those require a real Player check.
